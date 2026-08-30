#!/usr/bin/env python3
"""W2 — the heat-0 DEEP pooling. Sixteen disjoint CRN slot-set families at heat 0 on the FINAL
binary: the round of record's 0/10/20/30 (R4-*) plus 40/50/60/70, 900/910/920/930 and
940/950/960/970 (R4h0-*). 10 slots x greedy+sloppy per chunk, so 320 campaigns per leg.

Reports each family separately (so a single set can never masquerade as the answer) and then the
pooled McNemar over all of them. Run after `queue.sh` and `queue_h0.sh`.
"""
import json, math, os

OUT = os.path.dirname(os.path.abspath(__file__))
FAMILIES = [
    ("main round   b0-30",   "R4",   [0, 10, 20, 30]),
    ("extension    b40-70",  "R4h0", [40, 50, 60, 70]),
    ("family C   b900-930",  "R4h0", [900, 910, 920, 930]),
    ("family D   b940-970",  "R4h0", [940, 950, 960, 970]),
]


def legs(fix, prefix, bases, offset):
    out = {}
    for b in bases:
        p = os.path.join(OUT, f"{prefix}-fix{fix}-h0-b{b}.json")
        d = json.load(open(p))
        assert d["runs"] == 20, f"{p}: runs={d['runs']}"
        for s in d["pairedPolicy"]["slots"]:
            out[(offset + s["slot"], "g")] = bool(s["greedyWin"])
            out[(offset + s["slot"], "s")] = bool(s["sloppyWin"])
    return out


def mcnemar(off, on):
    ks = sorted(set(off) & set(on))
    a = sum(1 for k in ks if off[k] and not on[k])
    b = sum(1 for k in ks if on[k] and not off[k])
    n, k = a + b, min(a, b)
    p = min(1.0, 2.0 * sum(math.comb(n, i) for i in range(k + 1)) / 2.0 ** n) if n else 1.0
    wo = 100.0 * sum(off.values()) / len(ks)
    wn = 100.0 * sum(on.values()) / len(ks)
    return len(ks), wo, wn, a, b, p


print("W2 — heat 0, sixteen disjoint CRN slot sets on the FINAL binary (R4 / R4h0)\n")
print(f"{'family':>22} {'n':>5} {'OFF%':>7} {'ON%':>7} {'delta':>7} {'0->1':>5} {'1->0':>5} {'p':>8}")
allo, allon = {}, {}
for i, (name, prefix, bases) in enumerate(FAMILIES):
    o = legs(0, prefix, bases, i * 10000)
    n_ = legs(1, prefix, bases, i * 10000)
    k, wo, wn, a, b, p = mcnemar(o, n_)
    print(f"{name:>22} {k:5d} {wo:7.1f} {wn:7.1f} {wn-wo:+7.1f} {a:5d} {b:5d} {p:8.3f}")
    allo.update(o)
    allon.update(n_)
k, wo, wn, a, b, p = mcnemar(allo, allon)
print(f"{'POOLED (all 16 sets)':>22} {k:5d} {wo:7.1f} {wn:7.1f} {wn-wo:+7.1f} {a:5d} {b:5d} {p:8.3f}")
# Paired (McNemar) interval on the difference of proportions: d = (b-a)/n, SE ~= sqrt(a+b)/n.
d = 100.0 * (b - a) / k
sed = 100.0 * math.sqrt(a + b) / k
print(f"\n  paired difference {d:+.1f} points, SE {sed:.1f}, 95% CI [{d-1.96*sed:+.1f}, {d+1.96*sed:+.1f}]")
print("  The paired test is the one that matters: the two legs replay the same worlds, so the")
print("  world-to-world variance that dominates an unpaired rung cancels.")
