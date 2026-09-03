#!/usr/bin/env python3
"""L5 — the CLUSTER tool for a 16-slot-set ladder (and the 8-set bridge).

A rung is not n exchangeable campaigns; it is k clusters of 20 (each a disjoint CRN slot set), and
the clusters disagree (C3 review F5, L4 README). Per rung this prints the win% over every
`campaigns[]` row, the binomial SE, the CLUSTER SE (SD of the k chunk means / sqrt(k)), the
leave-one-cluster-out jackknife range, the goal band of record and its verdict, the rung-to-rung
steps and a monotonicity verdict. Then two blocks the pin exists for:
  HEAT LEAK   — per rung: chunks pinned, heat-raising picks, campaigns raised, missions above the
                pin. For a PINNED prefix every chunk MUST read campaignsRaised = missionsAbovePin = 0
                (LEAK-CHECK PASS/FAIL); for the bridge it is the size of the leak L4 carried.
  STALEMATES  — per rung, by ARM (STALEMATE-MISSION / STALEMATE-RUN), from the same rows.
`--bridge L4` compares a bridge prefix with the L4 archive chunk-for-chunk on pairedPolicy.slots
(same slot, same policy leg, same outcome) — the CRN-chain check across the merges since 7315425.

Usage: cluster.py <prefix> [--dir DIR] [--bridge <l4prefix> --l4dir DIR]
"""
import glob, json, math, os, statistics, sys

HERE = os.path.dirname(os.path.abspath(__file__))
RUNGS = ["R", "0", "2", "4", "6", "8"]
CENTRE = {"R": 75, "0": 55, "2": 40, "4": 30, "6": 20, "8": 10}
HALF = {"R": 8, "0": 8, "2": 8, "4": 8, "6": 8, "8": 5}


def band(h):
    lo, hi = CENTRE[h] - HALF[h], CENTRE[h] + HALF[h]
    if h == "8":
        lo = max(lo, 5)          # hard floor >= 5
    return lo, hi


def base_of(f):
    return int(os.path.basename(f).rsplit("-b", 1)[1].split(".")[0])


def load(prefix, d):
    out = {}
    for h in RUNGS:
        files = sorted(glob.glob(os.path.join(d, f"{prefix}-h{h}-b*.json")), key=base_of)
        out[h] = [(base_of(f), json.load(open(f))) for f in files]
    return out


def rows_of(j):
    return [r for r in j["campaigns"] if r["mode"] == "campaign"]


def ladder(prefix, data):
    print(f"===== {prefix} — THE LADDER (k clusters of 20 per rung; band = goal band of record) =====")
    print(f"  {'rung':<6}{'k':>3}{'n':>5}{'win%':>7}{'binomSE':>8}{'clustSE':>8}{'ratio':>6}"
          f"{'jack min':>9}{'jack max':>9}{'band':>8}   verdict")
    means = {}
    for h in RUNGS:
        chunks = data[h]
        if not chunks:
            continue
        vals, n, w = [], 0, 0
        for _, j in chunks:
            rs = rows_of(j)
            vals.append(100.0 * sum(r["win"] for r in rs) / len(rs))
            n += len(rs); w += sum(r["win"] for r in rs)
        k = len(vals)
        mean = 100.0 * w / n
        p = w / n
        bse = 100 * math.sqrt(p * (1 - p) / n)
        cse = statistics.stdev(vals) / math.sqrt(k) if k > 1 else float("nan")
        jack = [statistics.mean([v for j, v in enumerate(vals) if j != i]) for i in range(k)] if k > 1 else [mean]
        lo, hi = band(h)
        if mean < lo:
            verdict = f"OUT {mean - lo:+.1f} (floor {lo}; {(lo - mean) / cse:.2f} clusterSE under)"
        elif mean > hi:
            verdict = f"OUT {mean - hi:+.1f} (ceiling {hi})"
        else:
            verdict = f"IN  (+{mean - lo:.1f} over floor {lo}; {(mean - lo) / cse:.2f} clusterSE clear)"
        means[h] = mean
        print(f"  h{h:<5}{k:>3}{n:>5}{mean:>7.1f}{bse:>8.2f}{cse:>8.2f}{cse / bse:>6.2f}"
              f"{min(jack):>9.1f}{max(jack):>9.1f}{lo:>4}-{hi:<3}   {verdict}")
    keys = [h for h in RUNGS if h in means]
    steps = [(keys[i], keys[i + 1], means[keys[i]] - means[keys[i + 1]]) for i in range(len(keys) - 1)]
    print("  steps: " + "  ".join(f"h{a}->h{b} {s:+.1f}" for a, b, s in steps))
    mono = all(s > 0 for _, _, s in steps)
    print(f"  MONOTONE: {'yes, at every step' if mono else 'NO — ' + ', '.join(f'h{a}->h{b}' for a, b, s in steps if s <= 0)}")
    inb = sum(1 for h in keys if band(h)[0] <= means[h] <= band(h)[1])
    print(f"  IN BAND: {inb} of {len(keys)}")
    return means


def leak(prefix, data):
    print(f"\n===== {prefix} — HEAT LEAK (does 'heat N' mean heat N?) =====")
    print(f"  {'rung':<6}{'chunks':>7}{'pinned':>7}{'picks':>7}{'raised':>7}{'offRung':>8}{'missions':>9}  worst chunk")
    bad = 0
    for h in RUNGS:
        chunks = data[h]
        if not chunks:
            continue
        pinned = sum(1 for _, j in chunks if j["heatLeak"]["pinned"])
        picks = sum(j["heatLeak"]["heatRaisingPicks"] for _, j in chunks)
        raised = sum(j["heatLeak"]["campaignsRaised"] for _, j in chunks)
        off = sum(j["heatLeak"]["missionsAbovePin"] for _, j in chunks)
        mis = sum(j["missions"] for _, j in chunks)
        worst = max(chunks, key=lambda c: c[1]["heatLeak"]["missionsAbovePin"])
        if pinned == len(chunks) and (raised or off):
            bad += 1
        print(f"  h{h:<5}{len(chunks):>7}{pinned:>7}{picks:>7}{raised:>7}{off:>8}{mis:>9}  b{worst[0]} offRung={worst[1]['heatLeak']['missionsAbovePin']}")
    allpinned = all(j["heatLeak"]["pinned"] for h in RUNGS for _, j in data[h])
    if allpinned:
        print("  LEAK-CHECK:", "PASS (every chunk pinned; campaignsRaised = missionsAbovePin = 0 on all of them)" if bad == 0 else "FAIL")
    else:
        print("  (unpinned prefix — the numbers above are the size of the leak, not a check)")
    return bad == 0


def stalemates(prefix, data):
    print(f"\n===== {prefix} — STALEMATES by arm (harness-forced losses) =====")
    print(f"  {'rung':<6}{'n':>5}{'mission':>8}{'run':>5}{'share%':>7}  where")
    T = [0, 0, 0]
    for h in RUNGS:
        rs = [r for _, j in data[h] for r in rows_of(j)]
        if not rs:
            continue
        sm = [r for r in rs if r["lossCause"] == "STALEMATE-MISSION"]
        sr = [r for r in rs if r["lossCause"] == "STALEMATE-RUN"]
        where = " ".join(f"[s{r['slot']}{r['policy'][0]} m{r['endMission']} {r['endObjective']} rt{r['runTurns']}]" for r in sm + sr)
        print(f"  h{h:<5}{len(rs):>5}{len(sm):>8}{len(sr):>5}{100.0 * (len(sm) + len(sr)) / len(rs):>7.2f}  {where}")
        T[0] += len(rs); T[1] += len(sm); T[2] += len(sr)
    if T[0]:
        print(f"  TOTAL {T[0]} campaigns: mission-arm {T[1]}, run-arm {T[2]}, share {100.0 * (T[1] + T[2]) / T[0]:.2f}%")


def bridge(prefix, data, l4prefix, l4dir):
    print(f"\n===== {prefix} vs {l4prefix} ({l4dir}) — chunk-for-chunk on pairedPolicy.slots =====")
    tot_chunks = same_chunks = tot_legs = same_legs = 0
    for h in RUNGS:
        for b, j in data[h]:
            f = os.path.join(l4dir, f"{l4prefix}-h{h}-b{b}.json")
            if not os.path.exists(f):
                continue
            o = json.load(open(f))
            a, c = j["pairedPolicy"]["slots"], o["pairedPolicy"]["slots"]
            tot_chunks += 1
            same_chunks += (a == c)
            for x, y in zip(a, c):
                tot_legs += 2
                same_legs += (x["greedyWin"] == y["greedyWin"]) + (x["sloppyWin"] == y["sloppyWin"])
            if a != c:
                print(f"  h{h} b{b}: DIFFERS ({sum((x['greedyWin'] != y['greedyWin']) + (x['sloppyWin'] != y['sloppyWin']) for x, y in zip(a, c))} legs)")
    print(f"  chunks identical on every (slot, policy) outcome: {same_chunks}/{tot_chunks}; legs identical {same_legs}/{tot_legs}")


def main():
    args = [a for a in sys.argv[1:]]
    d = HERE; l4p = None; l4d = os.path.join(HERE, "..", "l4")
    pos = []
    i = 0
    while i < len(args):
        if args[i] == "--dir": d = args[i + 1]; i += 2
        elif args[i] == "--bridge": l4p = args[i + 1]; i += 2
        elif args[i] == "--l4dir": l4d = args[i + 1]; i += 2
        else: pos.append(args[i]); i += 1
    prefix = pos[0] if pos else "L5"
    data = load(prefix, d)
    ladder(prefix, data)
    ok = leak(prefix, data)
    stalemates(prefix, data)
    if l4p:
        bridge(prefix, data, l4p, l4d)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
