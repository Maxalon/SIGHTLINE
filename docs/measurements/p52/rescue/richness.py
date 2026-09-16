#!/usr/bin/env python3
"""P48 — DECISION RICHNESS, paired over CRN slot sets.

P42's finding on procedural buildings was decision richness, not win rate, so this round has to
report it the same way — and with a cluster statistic, because CLAUDE.md's rule is that a delta
without an n and a spread is not a number. Each slot base is one cluster; the two arms played the
same seeds, so the deltas are paired and the t runs on k-1 df.

Usage: richness.py <dir> <prefixA> <prefixB> [metric]
"""
import glob, json, math, os, statistics, sys

d, a_pre, b_pre = sys.argv[1], sys.argv[2], sys.argv[3]
METRICS = sys.argv[4:] or ["meaningfulChoicesPerTurn", "targetChoicesPerArmedSoldierTurn",
                           "positionChoicesPerArmedSoldierTurn", "turnsWithAShotPct"]

def load(pre):
    out = {}
    for f in glob.glob(os.path.join(d, f"{pre}-*.json")):
        b = os.path.basename(f)[:-5]
        h = int(b.split("-h")[1].split("-")[0]); base = int(b.rsplit("-b", 1)[1])
        out[(h, base)] = json.load(open(f))["decisionRichness"]
    return out

A, B = load(a_pre), load(b_pre)
print(f"===== DECISION RICHNESS: {a_pre} -> {b_pre}  (paired over CRN slot sets) =====")
for m in METRICS:
    print(f"\n  {m}")
    print("  rung      k       A       B    delta    clSE       t")
    for h in sorted({k[0] for k in A}):
        ds = [B[(h, b)][m] - A[(h, b)][m] for (hh, b) in sorted(A) if hh == h and (h, b) in B]
        av = [A[(h, b)][m] for (hh, b) in sorted(A) if hh == h]
        bv = [B[(h, b)][m] for (hh, b) in sorted(B) if hh == h]
        k = len(ds)
        mean = statistics.fmean(ds)
        se = statistics.stdev(ds) / math.sqrt(k) if k > 1 else float("nan")
        t = mean / se if se else float("nan")
        print(f"  h{h:<5}{k:5d}{statistics.fmean(av):8.3f}{statistics.fmean(bv):8.3f}"
              f"{mean:+9.3f}{se:8.3f}{t:8.2f}   {'RESOLVED' if abs(t) > 2.13 else 'not resolved'}")
