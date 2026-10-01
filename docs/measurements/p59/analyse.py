#!/usr/bin/env python3
"""P59 — does wave-arrival DISTANCE make a board-size a difficulty lever?

P56 gated the hostile HEADCOUNT as board-neutral. That cannot see distance: Defend waves and the
anti-turtle pressure clock's reinforcements drop in at the EAST EDGE (`Game.SpawnReinforcements`,
`x = Grid.W - 2`), so on a bigger board they walk further to reach the squad.

Design: a size ladder over PROCEDURAL-ONLY boards (24x15 / 36x22 / 48x30 — 18x11 is excluded
because it plays authored arenas and would confound terrain with distance), three forced objectives:
    defend    TREATMENT  (Defend waves from the east edge)
    hack      TREATMENT  (pressure-clock reinforcement waves from the east edge)
    sabotage  CONTROL    (no clock, no waves at all)
and the control's trend subtracted from each treatment's (difference-in-differences). Heat 0,
four slot bases, 80 campaigns per cell. Nothing here is read against the 18x11 heat band.

ARM CHECK asserts, on every chunk, `levers.board` (added in this wave) matches the size in the file
name, `byObjective` is exactly the forced objective, and `byArena` is empty (procedural only).

Usage: analyse.py [dir]
"""
import glob, json, os, sys
from collections import defaultdict

d = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
OBJ = {"defend": "Defend", "hack": "Hack", "sabotage": "Sabotage"}
bad = []
cell = defaultdict(lambda: {"n": 0, "w": 0.0, "t": 0.0, "camp": [], "chunks": 0})
for f in sorted(glob.glob(os.path.join(d, "*.json"))):
    tag = os.path.basename(f)[:-5]
    obj, s, h, b = tag.split("-")
    w = int(s[1:])
    j = json.load(open(f))
    board = (j.get("levers") or {}).get("board", "")
    if not board.startswith(f"{w}x"):
        bad.append(f"{tag}: levers.board={board!r}, expected {w}x..")
    objs = {r.get("objective") for r in j.get("byObjective") or []}
    if objs != {OBJ[obj]}:
        bad.append(f"{tag}: byObjective={sorted(map(str, objs))}, expected only {OBJ[obj]}")
    if j.get("byArena"):
        bad.append(f"{tag}: byArena non-empty — an authored arena played on a {w}-wide board")
    if j.get("runs") != (j.get("batch") or {}).get("expectedRuns"):
        bad.append(f"{tag}: runs {j.get('runs')} != expectedRuns")
    if not (j.get("heatLeak") or {}).get("pinned"):
        bad.append(f"{tag}: heat not pinned")
    for r in j.get("byObjective") or []:
        if r.get("objective") == OBJ[obj]:
            c = cell[(obj, w)]
            c["n"] += r["n"]; c["w"] += r["n"] * r["winRate"] / 100.0; c["t"] += r["n"] * (r.get("avgTurns") or 0)
    cell[(obj, w)]["camp"].append(j.get("runWinRate", -1))
    cell[(obj, w)]["chunks"] += 1

print(f"ARM CHECK over {sum(c['chunks'] for c in cell.values())} chunks: " + ("PASS" if not bad else f"FAIL ({len(bad)})"))
for x in bad[:12]:
    print("   " + x)

sizes = sorted({w for (_, w) in cell})
print("\nMISSION win rate of the forced objective (n = missions), and mean mission turns")
print("  objective   " + "".join(f"{str(w)+'-wide':>26s}" for w in sizes) + "     trend (widest - narrowest)")
rate = {}
for obj in ("defend", "hack", "sabotage"):
    cells = []
    for w in sizes:
        c = cell[(obj, w)]
        wr = 100 * c["w"] / c["n"] if c["n"] else float("nan")
        rate[(obj, w)] = wr
        cells.append(f"{wr:6.1f}% n={c['n']:4d} {c['t'] / max(c['n'], 1):4.1f}t")
    tr = rate[(obj, sizes[-1])] - rate[(obj, sizes[0])]
    rate[(obj, "trend")] = tr
    role = "CONTROL" if obj == "sabotage" else "treat"
    print(f"  {obj:9s}   " + "".join(f"{c:>26s}" for c in cells) + f"     {tr:+6.1f}  ({role})")

print("\nDIFFERENCE-IN-DIFFERENCES (treatment trend minus control trend):")
for obj in ("defend", "hack"):
    print(f"  {obj:9s} {rate[(obj, 'trend')] - rate[('sabotage', 'trend')]:+6.1f} points")
print("\nA positive number means that objective got EASIER with board size by more than the control did.")
print("Per-cell n is ~200-400 missions; a binomial SE on one cell is ~2-3.5 points, so a DiD under ~6 is noise.")
sys.exit(1 if bad else 0)
