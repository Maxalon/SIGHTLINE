#!/usr/bin/env python3
"""L5 — per-campaign ROWS: the pairing tool for any two chunk sets, single-policy included.

`pairedPolicy.slots` keeps a (slot, heat) only when it has exactly one greedy AND one sloppy leg,
so a batch run with SIGHTLINE_BALANCE_SLOPPY=1 (or a future camping policy) left NO per-slot rows
and could not be CRN-paired. Every chunk now carries `campaigns[]` — one row per RunRec — and this
tool pairs on (slot, policy, heat) from that array instead.

Modes:
  rows.py <prefixA> [prefixB] [--heats R,0,2,4,6,8] [--dir DIR]
      the rung table for A (win%, binomial SE, n, avg missions, stalemate share), and with B the
      CRN-PAIRED per-rung comparison (discordant counts, paired delta, its SE, McNemar-ish z) —
      the same table c1/ladder.py prints, derived from campaigns[] instead of pairedPolicy.slots.
  rows.py --check <chunk.json> [more...]
      on a greedy+sloppy chunk, rebuild pairedPolicy.slots from campaigns[] and assert it matches
      the chunk's own pairedPolicy block field for field (slots, pairs, concordant, greedyOnlyWon,
      sloppyOnlyWon, pairedGap). This is the proof the two derivations agree; L4's paired table
      is reproduced from rows by construction wherever they do.
Chunk files are <DIR>/<prefix>-h<H>-b<B>.json; RECRUIT is encoded as hR (heat -1 in the JSON).
"""
import glob, json, math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))


def hcode(h):
    return "R" if h == -1 else str(h)


def load(prefix, heats, d):
    """-> {heatcode: {(slot, policy): row}} pooled over every chunk of the prefix, campaign mode only."""
    out = {h: {} for h in heats}
    files = 0
    for h in heats:
        for f in sorted(glob.glob(os.path.join(d, f"{prefix}-h{h}-b*.json"))):
            j = json.load(open(f)); files += 1
            for r in j["campaigns"]:
                if r["mode"] != "campaign":
                    continue
                if hcode(r["heat"]) != h:
                    raise SystemExit(f"{f}: a row at heat {r['heat']} in an h{h} chunk")
                k = (r["slot"], r["policy"])
                if k in out[h]:
                    raise SystemExit(f"{f}: duplicate (slot, policy) {k} at h{h} — overlapping chunks?")
                out[h][k] = r
    return out, files


def wr(rows):
    n = len(rows); w = sum(1 for r in rows.values() if r["win"])
    p = w / n if n else 0.0
    return 100 * p, 100 * math.sqrt(p * (1 - p) / n) if n else 0.0, n


def table(prefix, data, files):
    print(f"== {prefix} ==  ({files} chunks, from campaigns[])")
    print(f"{'rung':>6} {'n':>5} {'win%':>7} {'+-SE':>6} {'avgMis':>7} {'stale%':>7} {'mis-arm':>8} {'run-arm':>8}")
    for h, rows in data.items():
        if not rows:
            continue
        p, se, n = wr(rows)
        mis = sum(r["missionsCleared"] for r in rows.values()) / n
        sm = sum(1 for r in rows.values() if r["lossCause"] == "STALEMATE-MISSION")
        sr = sum(1 for r in rows.values() if r["lossCause"] == "STALEMATE-RUN")
        print(f"  h{h:<4} {n:>5} {p:>7.1f} {se:>6.1f} {mis:>7.2f} {100*(sm+sr)/n:>7.1f} {sm:>8} {sr:>8}")


def paired(A, B, da, db, heats):
    print(f"\n== CRN-PAIRED {B} vs {A} (same slot, same policy leg, same heat) ==")
    print(f"{'rung':>6} {'pairs':>6} {'A%':>7} {'B%':>7} {'delta':>7} {'+-SEpair':>9} {'B-only':>7} {'A-only':>7}  z")
    for h in heats:
        keys = sorted(set(da[h]) & set(db[h]))
        n = len(keys)
        if n == 0:
            continue
        b01 = sum(1 for k in keys if db[h][k]["win"] and not da[h][k]["win"])
        b10 = sum(1 for k in keys if da[h][k]["win"] and not db[h][k]["win"])
        pa = 100 * sum(1 for k in keys if da[h][k]["win"]) / n
        pb = 100 * sum(1 for k in keys if db[h][k]["win"]) / n
        delta = 100.0 * (b01 - b10) / n
        disc = b01 + b10
        var = (disc / n) - ((b01 - b10) / n) ** 2
        se = 100.0 * math.sqrt(max(var, 0.0) / n)
        z = (b01 - b10) / math.sqrt(disc) if disc else 0.0
        print(f"  h{h:<4} {n:>6} {pa:>7.1f} {pb:>7.1f} {delta:>+7.1f} {se:>9.1f} {b01:>7} {b10:>7}  {z:+.2f}")


def check(files):
    bad = 0
    for f in files:
        j = json.load(open(f))
        rows = [r for r in j["campaigns"] if r["mode"] == "campaign" and r["slot"] >= 0]
        by = {}
        for r in rows:
            by.setdefault((r["slot"], r["heat"]), {})[r["policy"]] = r
        mine = []
        for (slot, heat), legs in sorted(by.items(), key=lambda kv: (kv[0][1], kv[0][0])):
            if set(legs) != {"greedy", "sloppy"}:
                continue
            g, s = legs["greedy"], legs["sloppy"]
            mine.append({"slot": slot, "heat": heat, "greedyWin": g["win"], "greedyMissions": g["missionsCleared"],
                         "sloppyWin": s["win"], "sloppyMissions": s["missionsCleared"],
                         "marginMissions": g["missionsCleared"] - s["missionsCleared"]})
        theirs = j["pairedPolicy"]["slots"]
        ok = mine == theirs
        pairs = len(mine)
        conc = sum(1 for m in mine if m["greedyWin"] == m["sloppyWin"])
        gonly = sum(1 for m in mine if m["greedyWin"] and not m["sloppyWin"])
        sonly = sum(1 for m in mine if m["sloppyWin"] and not m["greedyWin"])
        gap = round(100.0 * (gonly - sonly) / pairs, 1) if pairs else 0.0
        pp = j["pairedPolicy"]
        ok2 = (pp["pairs"], pp["concordant"], pp["greedyOnlyWon"], pp["sloppyOnlyWon"], pp["pairedGap"]) == (pairs, conc, gonly, sonly, gap)
        print(f"{os.path.basename(f):>28}: rows-derived slots {'==' if ok else '!='} pairedPolicy.slots ({len(mine)} vs {len(theirs)}); "
              f"tallies {'==' if ok2 else '!='} (pairs={pairs} conc={conc} g-only={gonly} s-only={sonly} gap={gap})")
        bad += (not ok) + (not ok2)
    print("ROWS-CHECK:", "PASS" if bad == 0 else "FAIL")
    return 0 if bad == 0 else 1


def main():
    argv = sys.argv[1:]
    if argv and argv[0] == "--check":
        sys.exit(check(argv[1:]))
    heats = ["R", "0", "2", "4", "6", "8"]
    d = HERE
    args = []
    for a in argv:
        if a.startswith("--heats="):
            heats = a.split("=", 1)[1].split(",")
        elif a.startswith("--dir="):
            d = a.split("=", 1)[1]
        else:
            args.append(a)
    A = args[0]; B = args[1] if len(args) > 1 else None
    da, fa = load(A, heats, d)
    table(A, da, fa)
    if B:
        db, fb = load(B, heats, d)
        print()
        table(B, db, fb)
        paired(A, B, da, db, heats)


if __name__ == "__main__":
    main()
