#!/usr/bin/env python3
"""C4 aggregator — pools the eight disjoint CRN slot sets per (rung, arm) and prints the
paired A/B table. A = SIGHTLINE_BIOMEMECH=1 (shipped), B = 0 (pre-C4 board), same binary,
same slots. Also prints the decision-density and tempo columns, and the per-mission win
column, because a pooled row can hide an artifact (CROSSCUT rule 1)."""
import json, math, os, sys

D = os.path.dirname(os.path.abspath(__file__))
BASES = [0, 10, 20, 30, 40, 50, 60, 70]


def load(arm, h):
    runs = wins = 0
    tot = {}
    for b in BASES:
        p = os.path.join(D, f"C4-{arm}-h{h}-b{b}.json")
        d = json.load(open(p))
        n = d["runs"]
        runs += n
        wins += round(d["runWinRate"] * n / 100.0)
        for k in ("avgMissionsCleared",):
            tot[k] = tot.get(k, 0.0) + d.get(k, 0.0) * n
        dr = d.get("decisionRichness", {})
        for k, v in dr.items():
            if isinstance(v, (int, float)):
                tot["dr." + k] = tot.get("dr." + k, 0.0) + v * n
    return runs, wins, {k: v / runs for k, v in tot.items()}


def se(p, n):
    return 100.0 * math.sqrt(max(p, 1e-9) * (1 - p) / n)


print(f"{'rung':>6} | {'A (mech ON)':>12} | {'B (pre-C4)':>12} | {'delta':>7} | {'+/-SE(diff)':>11} | n/arm")
print("-" * 78)
rows = []
for h in (0, 2, 4):
    na, wa, ma = load("A", h)
    nb, wb, mb = load("B", h)
    pa, pb = wa / na, wb / nb
    d = 100 * (pa - pb)
    sd = math.sqrt((se(pa, na) ** 2) + (se(pb, nb) ** 2))
    rows.append((h, 100 * pa, 100 * pb, d, sd, na, ma, mb))
    print(f"  h{h:<3} | {100*pa:11.1f}% | {100*pb:11.1f}% | {d:+6.1f} | {sd:11.1f} | {na}")

print()
print("secondary columns (A vs B, pooled n=160/arm):")
for h, pa, pb, d, sd, n, ma, mb in rows:
    keys = sorted(set(list(ma.keys()) + list(mb.keys())))
    print(f"  heat {h}:")
    for k in keys:
        a, b = ma.get(k, float('nan')), mb.get(k, float('nan'))
        print(f"    {k:<34} A={a:8.3f}  B={b:8.3f}  d={a-b:+8.3f}")
