#!/usr/bin/env python3
"""L7 EVERY RUNG — the CLUSTER tool for an ELEVEN-rung ladder.

A copy of l6/cluster.py (which is a copy of l5's) with exactly two changes:

  * `RUNGS` is `R 0 1 2 3 4 5 6 7 8` — every rung of the ladder, which is the whole point of
    this wave. C1 is the precedent: sampling {R,0,2,4,6,8} turned "the middle is flat" into a
    two-rung question it could not answer, and only the per-rung ladder located it.
  * THE ODD RUNGS HAVE NO BAND OF RECORD. The goal band (FUL-13) was written for the six
    sampled rungs and nobody has ever set one for h1/h3/h5/h7. This tool prints "-" for them
    rather than interpolating a band and then grading against it. A band nobody agreed is not
    a band, and inventing four of them inside a measurement round is exactly the kind of
    quiet over-claim this project keeps catching itself in.

Everything else — the cluster SE, the leave-one-cluster-out jackknife, the LEAK-CHECK and the
STALEMATE split — is unchanged and reads the same fields.

Usage: cluster.py [prefix] [--dir DIR]
"""
import glob, json, math, os, statistics, sys

HERE = os.path.dirname(os.path.abspath(__file__))
RUNGS = ["R", "0", "1", "2", "3", "4", "5", "6", "7", "8"]
# The goal band of record (FUL-13), which exists only for the six historically sampled rungs.
CENTRE = {"R": 75, "0": 55, "2": 40, "4": 30, "6": 20, "8": 10}
HALF = {"R": 8, "0": 8, "2": 8, "4": 8, "6": 8, "8": 5}


def band(h):
    if h not in CENTRE:
        return None
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
    print(f"===== {prefix} — THE ELEVEN-RUNG LADDER (k clusters of 20 per rung) =====")
    print(f"  {'rung':<6}{'k':>3}{'n':>5}{'win%':>7}{'binomSE':>8}{'clustSE':>8}{'ratio':>6}"
          f"{'jack min':>9}{'jack max':>9}{'band':>8}   verdict")
    means, cses, bses = {}, {}, {}
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
        bd = band(h)
        if bd is None:
            bandtxt, verdict = "   -   ", "(no band of record for this rung — never sampled before C1/L7)"
        else:
            lo, hi = bd
            bandtxt = f"{lo:>4}-{hi:<3}"
            if mean < lo:
                verdict = f"OUT {mean - lo:+.1f} (floor {lo}; {(lo - mean) / cse:.2f} clusterSE under)"
            elif mean > hi:
                verdict = f"OUT {mean - hi:+.1f} (ceiling {hi})"
            else:
                verdict = f"IN  (+{mean - lo:.1f} over floor {lo}; {(mean - lo) / cse:.2f} clusterSE clear)"
        means[h], cses[h], bses[h] = mean, cse, bse
        print(f"  h{h:<5}{k:>3}{n:>5}{mean:>7.1f}{bse:>8.2f}{cse:>8.2f}{cse / bse:>6.2f}"
              f"{min(jack):>9.1f}{max(jack):>9.1f}{bandtxt}   {verdict}")
    keys = [h for h in RUNGS if h in means]
    steps = [(keys[i], keys[i + 1], means[keys[i]] - means[keys[i + 1]]) for i in range(len(keys) - 1)]
    print("  steps: " + "  ".join(f"h{a}->h{b} {s:+.1f}" for a, b, s in steps))
    mono = all(s > 0 for _, _, s in steps)
    print(f"  MONOTONE: {'yes, at every step' if mono else 'NO — ' + ', '.join(f'h{a}->h{b}' for a, b, s in steps if s <= 0)}")
    inb = sum(1 for h in keys if band(h) and band(h)[0] <= means[h] <= band(h)[1])
    graded = sum(1 for h in keys if band(h))
    print(f"  IN BAND: {inb} of {graded} graded rungs ({len(keys) - graded} rungs have no band of record)")
    return means, cses, bses


def leak(prefix, data):
    print(f"\n===== {prefix} — HEAT LEAK (does 'heat N' mean heat N?) =====")
    print(f"  {'rung':<6}{'chunks':>7}{'pinned':>7}{'picks':>7}{'raised':>7}{'offRung':>8}{'missions':>9}  worst chunk")
    leaky_pinned, unpinned = [], []
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
        for b, j in chunks:
            hl = j["heatLeak"]
            if not hl["pinned"]:
                unpinned.append((h, b))
            elif hl["campaignsRaised"] or hl["missionsAbovePin"]:
                leaky_pinned.append((h, b, hl["campaignsRaised"], hl["missionsAbovePin"]))
        print(f"  h{h:<5}{len(chunks):>7}{pinned:>7}{picks:>7}{raised:>7}{off:>8}{mis:>9}  b{worst[0]} offRung={worst[1]['heatLeak']['missionsAbovePin']}")
    total = sum(len(data[h]) for h in RUNGS)
    ok = True
    if leaky_pinned:
        ok = False
        print(f"  LEAK-CHECK: FAIL — {len(leaky_pinned)} chunk(s) claim the pin and leaked anyway:")
        for h, b, r, o in leaky_pinned[:12]:
            print(f"              h{h} b{b}: campaignsRaised={r} missionsAbovePin={o}")
    if not unpinned:
        if ok:
            print(f"  LEAK-CHECK: PASS (all {total} chunks pinned; campaignsRaised = missionsAbovePin = 0 on every one)")
    elif len(unpinned) == total:
        print(f"  LEAK-CHECK: NOT PERFORMED — all {total} chunks ran with SIGHTLINE_HEATPIN=0.")
    else:
        ok = False
        print(f"  LEAK-CHECK: FAIL — the round is MIXED: {total - len(unpinned)} of {total} pinned, {len(unpinned)} not.")
        print("              unpinned: " + " ".join(f"h{h}-b{b}" for h, b in unpinned[:20]))
    return ok


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


def main():
    args = sys.argv[1:]
    d = HERE
    pos = []
    i = 0
    while i < len(args):
        if args[i] == "--dir": d = args[i + 1]; i += 2
        else: pos.append(args[i]); i += 1
    prefix = pos[0] if pos else "L7"
    data = load(prefix, d)
    ladder(prefix, data)
    ok = leak(prefix, data)
    stalemates(prefix, data)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
