#!/usr/bin/env python3
"""C4 — the split the wave should have published and did not: campaign NODE KIND.

`byNodeKind` was already in every archived chunk (W1 put it there). `Start` is mission 1,
exactly one per campaign, so it carries NO within-campaign clustering — which makes it the one
cell in this round that is individually resolvable at n=480/arm.

Reports, per node kind: the pooled A/B mission win rate, and the CHUNK-PAIRED mean difference
(one difference per (rung, slot-set) chunk, 24 of them) with its own SE and t. Chunk pairing is
the right unit here because both arms ran the same slots inside a chunk, and it is the unit that
does not pretend 480 correlated missions are 480 independent ones.

Usage: aggregate_nodekind.py [--heat 0]
"""
import json, math, os, sys

D = os.path.dirname(os.path.abspath(__file__))
BASES = [0, 10, 20, 30, 40, 50, 60, 70]
HEATS = [0, 2, 4]
if "--heat" in sys.argv:
    HEATS = [int(sys.argv[sys.argv.index("--heat") + 1])]


def rows(arm, h, b, block, key):
    d = json.load(open(os.path.join(D, f"C4i-{arm}-h{h}-b{b}.json")))
    return {r[key]: (r["n"], r["winRate"] * r["n"] / 100.0) for r in d.get(block, [])}


def report(block, key, title):
    kinds = set()
    for h in HEATS:
        for b in BASES:
            kinds |= set(rows("A", h, b, block, key)) | set(rows("B", h, b, block, key))
    print(f"\n=== {title}   (heats {HEATS}) ===")
    print(f"{key:>10} | {'A win':>7} {'A n':>5} | {'B win':>7} {'B n':>5} | {'pooled d':>8} | "
          f"{'paired d':>8} {'SE':>5} {'t':>6} {'chunks':>6}")
    for k in sorted(kinds):
        an = aw = bn = bw = 0.0
        diffs = []
        for h in HEATS:
            for b in BASES:
                ra, rb = rows("A", h, b, block, key), rows("B", h, b, block, key)
                if k in ra:
                    an += ra[k][0]; aw += ra[k][1]
                if k in rb:
                    bn += rb[k][0]; bw += rb[k][1]
                if k in ra and k in rb and ra[k][0] and rb[k][0]:
                    diffs.append(100 * (ra[k][1] / ra[k][0] - rb[k][1] / rb[k][0]))
        if not an or not bn or len(diffs) < 2:
            continue
        m = sum(diffs) / len(diffs)
        sd = math.sqrt(sum((x - m) ** 2 for x in diffs) / (len(diffs) - 1))
        se = sd / math.sqrt(len(diffs))
        t = m / se if se else float("nan")
        print(f"{k:>10} | {100*aw/an:6.1f}% {int(an):5d} | {100*bw/bn:6.1f}% {int(bn):5d} | "
              f"{100*(aw/an-bw/bn):+7.2f} | {m:+7.2f} {se:5.2f} {t:+6.2f} {len(diffs):6d}")


report("byNodeKind", "nodeKind", "CAMPAIGN NODE KIND — Start is mission 1, one per campaign")
report("byObjective", "objective", "OBJECTIVE")
report("byBiome", "biome", "BIOME")
