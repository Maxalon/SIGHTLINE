#!/usr/bin/env python3
"""P24 — THE TEXTURE CHECK, because texture is the criterion X2 used to pick this dose.

X2 built `Mission.HostileAimTrim`, measured doses 5 and 10 at h0, and **rejected dose 10 on
TEXTURE, not on win rate** — "Eliminate 4.80t, Escort 13.66t" — i.e. the fight's PACE, not its
outcome. P24 spends dose 5, so it owes the same reading on this tree: if the give-back bought its
win rate by turning fights into longer or shorter affairs than they were, that is a cost even
though the band cannot see it.

`Mission.HostileAimTrim`'s own doc-comment claims the knob cannot move the decision-density
instruments "by construction" (it touches neither hostile count, nor hostile HP, nor player
damage). This script is where that claim is checked instead of repeated.

Note both metrics are conditioned on a population the lever changes: a campaign that survives
longer plays more late missions, and late missions are longer. So a small drift is expected and
the useful reading is the SIZE, against X2's own rejected dose as the scale.

Usage: texture.py [--dir DIR] [--rungs -1,0,2,4,6,8] [--bases old|new|both]
"""
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
BASES = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150]
NEWBASES = [160, 170, 180, 190, 200, 210, 220, 230, 240, 250, 260, 270, 280, 290, 300, 310]
ARMS = ["base", "aim"]
NAME = {"-1": "RECRUIT", "0": "h0", "2": "h2", "4": "h4", "6": "h6", "8": "h8"}
RICH = ["meaningfulChoicesPerTurn", "choicesPerArmedSoldierTurn", "losTargetsPerArmedSoldierTurn",
        "targetChoicesPerArmedSoldierTurn", "positionChoicesPerArmedSoldierTurn",
        "leadSwingsPerMatch", "turnsWithAShotPct"]


def main():
    d, rungs, which = HERE, ["-1", "0", "2", "4", "6", "8"], "old"
    a = sys.argv[1:]
    for i, x in enumerate(a):
        if x == "--dir": d = a[i + 1]
        if x == "--rungs": rungs = a[i + 1].split(",")
        if x == "--bases": which = a[i + 1]
    bases = {"old": BASES, "new": NEWBASES, "both": BASES + NEWBASES}[which]

    def gather(arm, rung_list):
        obj, rich, miss = {}, {k: [0.0, 0] for k in RICH}, 0
        for h in rung_list:
            for b in bases:
                f = os.path.join(d, f"P24-{arm}-h{h}-b{b}.json")
                if not os.path.exists(f):
                    continue
                j = json.load(open(f))
                m = j["missions"]; miss += m
                for r in j["byObjective"]:
                    e = obj.setdefault(r["objective"], [0, 0.0, 0.0])
                    e[0] += r["n"]; e[1] += r["n"] * r["avgTurns"]; e[2] += r["n"] * r["winRate"]
                for k in RICH:
                    v = j["decisionRichness"].get(k)
                    if v is not None:
                        rich[k][0] += v * m; rich[k][1] += m
        return obj, rich, miss

    print("===== MISSION PACE by objective — avgTurns, pooled over every measured rung =====")
    ob, rb, mb = gather("base", rungs)
    oa, ra, ma = gather("aim", rungs)
    print(f"  {'objective':<12}{'base n':>8}{'base t':>8}{'aim n':>8}{'aim t':>8}{'d turns':>9}"
          f"{'base win%':>11}{'aim win%':>10}")
    for k in sorted(set(ob) | set(oa)):
        nb, tb, wb = ob.get(k, [0, 0, 0]); na, ta, wa = oa.get(k, [0, 0, 0])
        if not (nb and na):
            continue
        print(f"  {k:<12}{nb:>8}{tb/nb:>8.2f}{na:>8}{ta/na:>8.2f}{ta/na - tb/nb:>+9.2f}"
              f"{wb/nb:>11.1f}{wa/na:>10.1f}")
    print(f"  (missions: base {mb}, aim {ma})")

    print("\n===== DECISION DENSITY — the instrument the dial claims it cannot move =====")
    print(f"  {'field':<36}{'base':>9}{'aim':>9}{'delta':>9}")
    for k in RICH:
        if not (rb[k][1] and ra[k][1]):
            continue
        vb, va = rb[k][0] / rb[k][1], ra[k][0] / ra[k][1]
        print(f"  {k:<36}{vb:>9.3f}{va:>9.3f}{va - vb:>+9.3f}")


if __name__ == "__main__":
    main()
