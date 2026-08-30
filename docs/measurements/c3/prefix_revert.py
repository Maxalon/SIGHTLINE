#!/usr/bin/env python3
"""C3 — revert THIS WAVE'S BEHAVIOUR to the pre-C3 tree, leaving CLASSTEST wired, so the test can
be watched to FAIL. Four reverts, one per thing the wave shipped:

  1. Game.ClockMayReinforce  -> always true          (the pre-C3 anti-turtle clock)
  2. the campaign-map node label drops its class mark
  3. the hover tooltip drops its class row
  4. the legend drops the class key row

Run:  python3 prefix_revert.py apply   ->  build  ->  SIGHTLINE_CLASSTEST=1  ->  expect FAIL
      git checkout -- src/                          ->  back to the shipped tree
"""
import sys, os, re

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))

EDITS = [
    ("src/Game.cs",
     "    bool ClockMayReinforce => ClockWavesOnEliminate || Objective != Objective.Eliminate;",
     "    bool ClockMayReinforce => true;   // PRE-C3 REVERT"),
    ("src/Hud.cs",
     "                if (marked) DrawClassMark(lr.X + 4.5f, lr.Y + 6f, 4.2f, n.Card.Objective);",
     "                // PRE-C3 REVERT: no class mark on the node label"),
    ("src/Hud.cs",
     '            string lc = hovered.Kind == NodeKind.Event ? null : ObjClassLine(c.Objective);',
     '            string lc = null;   // PRE-C3 REVERT: no class row in the tooltip'),
    ("src/Hud.cs",
     '                (Objective.Eliminate, "PITCHED - clear the field"),\n'
     '                (Objective.Evac,      "TASKED - the objective ends it"),',
     '                // PRE-C3 REVERT: no class key row'),
]


def main():
    if len(sys.argv) < 2 or sys.argv[1] != "apply":
        print(__doc__)
        return 2
    for path, old, new in EDITS:
        p = os.path.join(ROOT, path)
        s = open(p).read()
        if old not in s:
            print("NOT FOUND in", path, ":", old[:70])
            return 1
        open(p, "w").write(s.replace(old, new, 1))
        print("reverted:", path, "--", old.strip()[:60])
    return 0


sys.exit(main())
