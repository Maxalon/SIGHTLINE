#!/usr/bin/env python3
"""C4 — the CHUNK-PAIRED test the wave's first write-up did not run.

Both arms of a chunk ran the SAME slot set, so the difference of a chunk's two campaign win
rates is the paired observation. 8 chunks per rung; 24 across the round. This is the unit that
does not pretend 160 campaigns drawn from 8 correlated slot sets are 160 independent ones — and
it is what turns "3.2 below the floor" from a verdict back into a point estimate.

Also reports the mission-level MECHANICAL-vs-PAINT difference-in-differences, chunk-clustered,
plus a sign test over the 24 chunk differences.
"""
import json, math, os

D = os.path.dirname(os.path.abspath(__file__))
BASES = [0, 10, 20, 30, 40, 50, 60, 70]
MECH = {"VERDANT", "TUNDRA", "MAGMA"}


def stats(v):
    m = sum(v) / len(v)
    sd = math.sqrt(sum((x - m) ** 2 for x in v) / (len(v) - 1))
    se = sd / math.sqrt(len(v))
    return m, se, (m / se if se else float("nan"))


def chunk(arm, h, b):
    return json.load(open(os.path.join(D, f"C4i-{arm}-h{h}-b{b}.json")))


print("CAMPAIGN win rate, chunk-paired (A - B), 8 chunks per rung:")
alld = []
for h in (0, 2, 4):
    d = [chunk("A", h, b)["runWinRate"] - chunk("B", h, b)["runWinRate"] for b in BASES]
    alld += d
    m, se, t = stats(d)
    print(f"  heat {h}: mean {m:+6.2f}  SE {se:4.2f}  t {t:+5.2f}  (n=8 chunks)")
m, se, t = stats(alld)
print(f"  pooled : mean {m:+6.2f}  SE {se:4.2f}  t {t:+5.2f}  (n=24 chunks)")

print("\nMISSION win rate, MECHANICAL(3) minus PAINT(5), difference-in-differences per chunk:")
did = []
for h in (0, 2, 4):
    for b in BASES:
        vals = {}
        for arm in "AB":
            rows = chunk(arm, h, b).get("byBiome", [])
            for pop, sel in (("m", True), ("p", False)):
                n = w = 0.0
                for r in rows:
                    if (r["biome"] in MECH) == sel:
                        n += r["n"]; w += r["winRate"] * r["n"] / 100.0
                vals[arm + pop] = 100 * w / n if n else float("nan")
        did.append((vals["Am"] - vals["Bm"]) - (vals["Ap"] - vals["Bp"]))
m, se, t = stats(did)
neg = sum(1 for x in did if x < 0)
print(f"  DiD mean {m:+6.2f}  SE {se:4.2f}  t {t:+5.2f}  (n={len(did)} chunks)")
print(f"  sign test: {neg}/{len(did)} chunks negative")
