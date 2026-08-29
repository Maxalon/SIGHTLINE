#!/usr/bin/env python3
"""W2 GATE 3 aggregation. Pools the four disjoint CRN slot sets per rung and does the PAIRED
(McNemar) comparison the flywheel's whole methodology exists for: for every (heat, slot, policy)
the two legs replay the SAME world, so a discordant pair is attributable to the lever and a
concordant one carries no information about it.

Usage: python3 docs/measurements/w2/analyse.py
"""
import json, math, os, sys

OUT = os.path.dirname(os.path.abspath(__file__))
HEATS = [0, 2, 4, 6, 8]
BASES = [0, 10, 20, 30]


def legs(fix, heat):
    """(slot, policy) -> win, pooled over the four slot sets."""
    out = {}
    for b in BASES:
        p = os.path.join(OUT, f"R1-fix{fix}-h{heat}-b{b}.json")
        d = json.load(open(p))
        assert d["runs"] == 20, f"{p}: runs={d['runs']}"
        for s in d["pairedPolicy"]["slots"]:
            out[(s["slot"], "greedy")] = bool(s["greedyWin"])
            out[(s["slot"], "sloppy")] = bool(s["sloppyWin"])
    return out


def field(fix, heat, path):
    """Mean of a scalar field over the four chunks of a rung."""
    vals = []
    for b in BASES:
        d = json.load(open(os.path.join(OUT, f"R1-fix{fix}-h{heat}-b{b}.json")))
        cur = d
        for k in path:
            cur = cur[k]
        vals.append(cur)
    return sum(vals) / len(vals)


print("W2 GATE 3 — paired round, SIGHTLINE_AIIDLEFIX 0 vs 1")
print("n = 4 disjoint CRN slot sets (bases 0/10/20/30, 10 slots each) x greedy+sloppy")
print("  = 80 campaigns per rung per leg; 800 campaigns total.\n")
print(f"{'rung':>6} {'OFF%':>7} {'ON%':>7} {'delta':>7} {'n':>5} "
      f"{'0->1':>5} {'1->0':>5} {'p(2-sided)':>11}")
tot_a = tot_b = 0
rows = []
for h in HEATS:
    off, on = legs(0, h), legs(1, h)
    keys = sorted(set(off) & set(on))
    assert len(keys) == 80, len(keys)
    a = sum(1 for k in keys if off[k] and not on[k])     # the fix lost a world the baseline won
    b = sum(1 for k in keys if on[k] and not off[k])     # the fix won a world the baseline lost
    tot_a += a
    tot_b += b
    wo = 100.0 * sum(off.values()) / len(keys)
    wn = 100.0 * sum(on.values()) / len(keys)
    # exact two-sided binomial (McNemar) on the discordant pairs
    n = a + b
    if n == 0:
        p = 1.0
    else:
        k = min(a, b)
        tail = sum(math.comb(n, i) for i in range(0, k + 1)) / 2.0 ** n
        p = min(1.0, 2.0 * tail)
    rows.append((h, wo, wn, wn - wo, len(keys), a, b, p))
    print(f"{'h'+str(h):>6} {wo:7.1f} {wn:7.1f} {wn-wo:+7.1f} {len(keys):5d} "
          f"{a:5d} {b:5d} {p:11.3f}")

pooled_off = sum(r[1] for r in rows) / len(rows)
pooled_on = sum(r[2] for r in rows) / len(rows)
n = tot_a + tot_b
k = min(tot_a, tot_b)
p = min(1.0, 2.0 * sum(math.comb(n, i) for i in range(0, k + 1)) / 2.0 ** n) if n else 1.0
print(f"{'POOL':>6} {pooled_off:7.1f} {pooled_on:7.1f} {pooled_on-pooled_off:+7.1f} "
      f"{400:5d} {tot_a:5d} {tot_b:5d} {p:11.3f}")
print("\n0->1 = worlds the baseline WON and the fix LOST; 1->0 = the reverse. Concordant worlds\n"
      "carry no information about the lever, which is the whole point of the CRN pairing.\n")

print("Texture fields (mean over the rung's four chunks):")
FIELDS = [
    ("missions cleared", ["avgMissionsCleared"]),
    ("meaningful-choices/turn", ["decisionRichness", "meaningfulChoicesPerTurn"]),
    ("choices/ARMED-soldier-turn", ["decisionRichness", "choicesPerArmedSoldierTurn"]),
    ("acting-soldiers/turn", ["decisionRichness", "actingSoldiersPerTurn"]),
    ("armed-soldiers/turn", ["decisionRichness", "armedSoldiersPerTurn"]),
    ("lead-swings/match", ["decisionRichness", "leadSwingsPerMatch"]),
]
for label, path in FIELDS:
    off = sum(field(0, h, path) for h in HEATS) / len(HEATS)
    on = sum(field(1, h, path) for h in HEATS) / len(HEATS)
    print(f"  {label:<28} OFF {off:8.3f}   ON {on:8.3f}   delta {on-off:+8.3f}")
