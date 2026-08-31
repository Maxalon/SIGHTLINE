#!/usr/bin/env python3
"""C3 — pool the round-level decision/health metrics both arms report, so the lever can be checked
for side effects it was never aimed at (X2's "record the breaches straight" rule). Weighted by
each chunk's own `runs`. Usage: top.py <tagA> <tagB>
"""
import json, glob, os, sys

DIR = os.path.dirname(os.path.abspath(__file__))
KEYS = ["meaningfulChoicesPerTurn", "choicesPerArmedSoldierTurn", "leadSwingsPerMatch",
        "avgMaxSwing", "armedSoldiersPerTurn", "turnsWithAShotPct",
        "targetChoicesPerArmedSoldierTurn", "positionChoicesPerArmedSoldierTurn"]


def pool(tag):
    n = 0
    acc = {}
    for f in sorted(glob.glob(os.path.join(DIR, tag + "-h*-b*.json"))):
        d = json.load(open(f))
        r = d["runs"]
        n += r
        dr = d["decisionRichness"]
        for k in KEYS:
            if k in dr:
                acc[k] = acc.get(k, 0.0) + dr[k] * r
        acc["policyGapMeanShot"] = acc.get("policyGapMeanShot", 0.0) + d["shotGap"]["meanGap"] * r
        acc["avgMissionsCleared"] = acc.get("avgMissionsCleared", 0.0) + d["avgMissionsCleared"] * r
        acc["runWinRate"] = acc.get("runWinRate", 0.0) + d["runWinRate"] * r
    return n, {k: v / n for k, v in acc.items()}


a, A = pool(sys.argv[1])
b, B = pool(sys.argv[2])
print(f"{'metric':<38}{sys.argv[1]:>9}{sys.argv[2]:>9}{'delta':>9}   (n={a}/{b} campaigns)")
for k in sorted(set(A) | set(B)):
    va, vb = A.get(k, 0.0), B.get(k, 0.0)
    print(f"  {k:<36}{va:>9.3f}{vb:>9.3f}{vb - va:>9.3f}")
