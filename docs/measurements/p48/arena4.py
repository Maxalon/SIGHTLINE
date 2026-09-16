#!/usr/bin/env python3
"""P48 — THE ARENA-LEVEL READ, because the campaign-level one cannot see this lever.

One arena of thirty-five, on ~80% of builds, is ~2.3% of missions. The CRN contrast over campaigns
returned NINE discordant pairs in 960, which by C2's rule is an absence of evidence and not a
measured zero. So this pools the `byArena` cell for CITADEL (index 4) across every chunk of each
arm — the missions that actually played the redrawn board — and reports the mission-level win rate
there beside the whole-round decision-richness means.

Usage: arena4.py <dir> [arenaIndex]
"""
import glob, json, math, os, sys

d = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
IDX = int(sys.argv[2]) if len(sys.argv) > 2 else 4

def collect(arm):
    per_rung = {}
    rich = {}
    for f in sorted(glob.glob(os.path.join(d, f"{arm}-*.json"))):
        h = int(os.path.basename(f).split("-h")[1].split("-")[0])
        j = json.load(open(f))
        n = w = 0
        for row in j.get("byArena") or []:
            if row["arena"] == IDX:
                n += row["n"]; w += row["n"] * row["winRate"] / 100.0
        a, b = per_rung.get(h, (0, 0.0))
        per_rung[h] = (a + n, b + w)
        r = rich.setdefault(h, [])
        r.append(j["decisionRichness"])
    return per_rung, rich

offA, offR = collect("off")
onA, onR = collect("on")

print(f"===== ARENA {IDX} (CITADEL) — the missions that actually played it =====")
print("  rung   offN  off%    onN   on%   delta")
tot = [0, 0.0, 0, 0.0]
for h in sorted(offA):
    n0, w0 = offA[h]; n1, w1 = onA[h]
    p0 = 100 * w0 / n0 if n0 else float("nan")
    p1 = 100 * w1 / n1 if n1 else float("nan")
    print(f"  h{h:<5}{n0:5d} {p0:5.1f}  {n1:5d} {p1:5.1f}  {p1-p0:+6.1f}")
    tot[0] += n0; tot[1] += w0; tot[2] += n1; tot[3] += w1
p0 = 100 * tot[1] / tot[0]; p1 = 100 * tot[3] / tot[2]
se = math.sqrt(p0*(100-p0)/tot[0] + p1*(100-p1)/tot[2])
print(f"  POOLED {tot[0]:4d} {p0:5.1f}  {tot[2]:5d} {p1:5.1f}  {p1-p0:+6.1f}   (unpaired SE {se:.1f})")

print("\n===== DECISION RICHNESS (whole round — every mission, not just arena 4) =====")
keys = ["meaningfulChoicesPerTurn", "positionChoicesPerArmedSoldierTurn", "turnsWithAShotPct"]
print("  rung  " + "  ".join(f"{k[:26]:>26}" for k in keys))
for h in sorted(offR):
    cells = []
    for k in keys:
        a = sum(x[k] for x in offR[h]) / len(offR[h])
        b = sum(x[k] for x in onR[h]) / len(onR[h])
        cells.append(f"{a:8.3f} ->{b:8.3f}")
    print(f"  h{h:<4}" + "  ".join(f"{c:>26}" for c in cells))
