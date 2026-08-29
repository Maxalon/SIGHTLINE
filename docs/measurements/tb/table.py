#!/usr/bin/env python3
"""Print the TRUE BAND instrument table from the archived chunk JSONs."""
import json, os, sys

OUT = os.path.dirname(os.path.abspath(__file__))
rows = sys.argv[1:] or ["mult-h0", "add-h0", "mult-h4", "add-h4", "mult-h8", "add-h8"]
hdr = f"{'chunk':10} {'runs':>4} {'compl':>6} {'ch/turn':>8} {'ch/ARM':>7} {'tgt/ARM':>8} {'pos/ARM':>8} {'armed/t':>8} {'los/ARM':>8}"
print(hdr)
print("-" * len(hdr))
for r in rows:
    p = os.path.join(OUT, r + ".json")
    if not os.path.exists(p):
        print(f"{r:10} (pending)")
        continue
    d = json.load(open(p))
    dr = d.get("decisionRichness", {})
    print(f"{r:10} {d['runs']:>4} {d['runWinRate']:>5.1f}% {dr.get('meaningfulChoicesPerTurn',0):>8.3f} "
          f"{dr.get('choicesPerArmedSoldierTurn',0):>7.3f} {dr.get('targetChoicesPerArmedSoldierTurn',0):>8.3f} "
          f"{dr.get('positionChoicesPerArmedSoldierTurn',0):>8.3f} {dr.get('armedSoldiersPerTurn',0):>8.3f} "
          f"{dr.get('losTargetsPerArmedSoldierTurn',0):>8.3f}")
