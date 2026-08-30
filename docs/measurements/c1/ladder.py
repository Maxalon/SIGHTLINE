#!/usr/bin/env python3
"""C1 ladder aggregator. Pools the per-chunk balance JSONs of one PREFIX into a rung table
(win%, SE, avg missions cleared, decision density) and prints the rung-to-rung DELTAS with
the SE of the DIFFERENCE — the shape, which is what wave C1 is about.

With two prefixes it also prints the CRN-PAIRED per-slot comparison: control and lever
replay the SAME (slot, heat) worlds, so the paired difference has far less variance than
the two independent rung SEs suggest. McNemar's discordant counts are printed with it.

Usage: ladder.py <prefixA> [prefixB] [--heats 0,2,4,6,8]
"""
import json, math, os, sys, glob

HERE = os.path.dirname(os.path.abspath(__file__))


USE_CSV = False   # --csv: read the committed distillation instead of the raw chunk JSONs


def load(prefix, heats):
    """-> {heat: {(slot, policy): win_bool}} pooled over every chunk of this prefix."""
    out = {h: {} for h in heats}
    files = 0
    if USE_CSV:
        # The committed archive. `chunks.csv` carries every campaign's per-slot outcome, so
        # every table below re-derives from it with the raw JSONs deleted.
        import csv
        seen = set()
        with open(os.path.join(HERE, "chunks.csv")) as fh:
            for r in csv.DictReader(fh):
                if r["prefix"] != prefix:
                    continue
                h = int(r["heat"])
                if h not in out:
                    continue
                out[h][(int(r["slot"]), r["policy"][0])] = r["win"] == "1"
                seen.add((h, r["base"]))
        return out, len(seen)
    for h in heats:
        for f in sorted(glob.glob(os.path.join(HERE, f"{prefix}-h{h}-b*.json"))):
            d = json.load(open(f))
            files += 1
            for s in d["pairedPolicy"]["slots"]:
                assert s["heat"] == h, f"{f}: heat {s['heat']} != {h}"
                out[h][(s["slot"], "g")] = bool(s["greedyWin"])
                out[h][(s["slot"], "s")] = bool(s["sloppyWin"])
    return out, files


def aux(prefix, heats):
    """Per-rung pooled auxiliaries. avgMissionsCleared re-derives from the CSV; ch/ARMED is a
    per-chunk aggregate and reads 0 in --csv mode (the column is blanked, never faked)."""
    out = {}
    if USE_CSV:
        import csv
        acc = {h: [0, 0] for h in heats}
        with open(os.path.join(HERE, "chunks.csv")) as fh:
            for r in csv.DictReader(fh):
                if r["prefix"] != prefix:
                    continue
                h = int(r["heat"])
                if h in acc:
                    acc[h][0] += int(r["missions"]); acc[h][1] += 1
        for h in heats:
            n = acc[h][1] or 1
            out[h] = (acc[h][0] / n, 0.0, acc[h][1])
        return out
    for h in heats:
        mis, chn, n = 0.0, 0.0, 0
        for f in sorted(glob.glob(os.path.join(HERE, f"{prefix}-h{h}-b*.json"))):
            d = json.load(open(f))
            r = d["runs"]
            mis += d["avgMissionsCleared"] * r
            chn += d["decisionRichness"]["choicesPerArmedSoldierTurn"] * r
            n += r
        out[h] = (mis / n, chn / n, n)
    return out


def wr(d):
    n = len(d)
    w = sum(1 for v in d.values() if v)
    p = w / n
    return 100 * p, 100 * math.sqrt(p * (1 - p) / n), n


def main():
    global USE_CSV
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    heats = [0, 2, 4, 6, 8]
    for a in sys.argv[1:]:
        if a.startswith("--heats"):
            heats = [int(x) for x in a.split("=", 1)[1].split(",")]
        if a == "--csv":
            USE_CSV = True
    A = args[0]
    B = args[1] if len(args) > 1 else None

    da, fa = load(A, heats)
    ax = aux(A, heats)
    print(f"== {A} ==  ({fa} chunks)")
    print(f"{'rung':>6} {'n':>5} {'win%':>7} {'+-SE':>6} {'avgMis':>7} {'ch/ARM':>7}   step  +-SEstep")
    prev = None
    rowsA = {}
    for h in heats:
        p, se, n = wr(da[h])
        rowsA[h] = (p, se, n)
        if prev is None:
            step = stepse = None
        else:
            step = p - prev[0]
            stepse = math.sqrt(se * se + prev[1] * prev[1])
        m, ch, _ = ax[h]
        print(f"  h{h:<4} {n:>5} {p:>7.1f} {se:>6.1f} {m:>7.2f} {ch:>7.3f}"
              + (f"  {step:+6.1f}  {stepse:>6.1f}" if step is not None else "       -       -"))
        prev = (p, se)

    if not B:
        return
    db, fb = load(B, heats)
    bx = aux(B, heats)
    print()
    print(f"== {B} ==  ({fb} chunks)")
    print(f"{'rung':>6} {'n':>5} {'win%':>7} {'+-SE':>6} {'avgMis':>7} {'ch/ARM':>7}   step  +-SEstep")
    prev = None
    rowsB = {}
    for h in heats:
        p, se, n = wr(db[h])
        rowsB[h] = (p, se, n)
        step = None if prev is None else p - prev[0]
        stepse = None if prev is None else math.sqrt(se * se + prev[1] * prev[1])
        m, ch, _ = bx[h]
        print(f"  h{h:<4} {n:>5} {p:>7.1f} {se:>6.1f} {m:>7.2f} {ch:>7.3f}"
              + (f"  {step:+6.1f}  {stepse:>6.1f}" if step is not None else "       -       -"))
        prev = (p, se)

    print()
    print(f"== CRN-PAIRED {B} vs {A} (same slots, same policy legs) ==")
    print(f"{'rung':>6} {'pairs':>6} {'ctl%':>7} {'lev%':>7} {'delta':>7} {'+-SEpair':>9}"
          f" {'B-only':>7} {'A-only':>7}  McNemar-ish")
    for h in heats:
        keys = sorted(set(da[h]) & set(db[h]))
        n = len(keys)
        b01 = sum(1 for k in keys if db[h][k] and not da[h][k])   # lever-only win
        b10 = sum(1 for k in keys if da[h][k] and not db[h][k])   # control-only win
        delta = 100.0 * (b01 - b10) / n
        # SE of the paired difference in proportions: var of (X_lev - X_ctl) per pair.
        disc = b01 + b10
        var = (disc / n) - ((b01 - b10) / n) ** 2
        se = 100.0 * math.sqrt(max(var, 0.0) / n)
        z = (b01 - b10) / math.sqrt(disc) if disc else 0.0
        print(f"  h{h:<4} {n:>6} {rowsA[h][0]:>7.1f} {rowsB[h][0]:>7.1f} {delta:>+7.1f} {se:>9.1f}"
              f" {b01:>7} {b10:>7}   z={z:+.2f}")


if __name__ == "__main__":
    main()
