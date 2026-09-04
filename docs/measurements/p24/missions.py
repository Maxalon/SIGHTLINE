#!/usr/bin/env python3
"""P24 — the PER-MISSION row, the MISSION-1 check, and the stalemate census.

`docs/DESIGN.md` §3.D forbids front-loaded anxiety, and P23 verified m1/m2 were identical in all
four of its arms. P24's lever is NOT finale-scoped — a level lever touches mission 1 by
construction — so this wave cannot make the same claim and must instead SHOW what it did to the
opener. `FORCETEST` leg (C) still pins the opener's SIZE across rungs; what moves here is how much
of that force's fire lands, and it moves at every mission equally.

`byMission` denominators are per-arm (a campaign that dies at m3 never attempts m4), so the row is
a conditional win rate and the two arms' denominators legitimately differ. Read the direction, not
the difference of two conditionals with different populations.

Usage: missions.py [--dir DIR] [--rungs -1,0,2,4,6,8] [--bases old|new|both]
"""
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
BASES = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150]
NEWBASES = [160, 170, 180, 190, 200, 210, 220, 230, 240, 250, 260, 270, 280, 290, 300, 310]
ARMS = ["base", "aim"]
NAME = {"-1": "RECRUIT", "0": "h0", "2": "h2", "4": "h4", "6": "h6", "8": "h8"}


def main():
    d, rungs, which = HERE, ["-1", "0", "2", "4", "6", "8"], "old"
    a = sys.argv[1:]
    for i, x in enumerate(a):
        if x == "--dir": d = a[i + 1]
        if x == "--rungs": rungs = a[i + 1].split(",")
        if x == "--bases": which = a[i + 1]
    bases = {"old": BASES, "new": NEWBASES, "both": BASES + NEWBASES}[which]

    print("===== PER-MISSION CONDITIONAL WIN% / n (pooled over the slot bases) =====")
    print(f"  {'rung':<9}{'arm':<6}" + "".join(f"{'m'+str(m):>14}" for m in range(1, 7))
          + f"{'avgMisCleared':>15}")
    stale = {}
    for h in rungs:
        for arm in ARMS:
            wins = [0] * 7; ns = [0] * 7; amc = []; sm = {}
            for b in bases:
                f = os.path.join(d, f"P24-{arm}-h{h}-b{b}.json")
                if not os.path.exists(f):
                    continue
                j = json.load(open(f))
                for r in j["byMission"]:
                    m = r["mission"]
                    ns[m] += r["n"]; wins[m] += round(r["n"] * r["winRate"] / 100.0)
                amc.append(j["avgMissionsCleared"])
                for k, v in (j.get("lossCauses") or {}).items():
                    if k.startswith("STALEMATE"):
                        sm[k] = sm.get(k, 0) + v
            if not ns[1]:
                continue
            stale[(h, arm)] = sm
            row = "".join(f"{(100.0*wins[m]/ns[m] if ns[m] else float('nan')):>8.1f}/{ns[m]:<6}"
                          for m in range(1, 7))
            print(f"  {NAME.get(h,h):<9}{arm:<6}{row}{sum(amc)/len(amc):>15.2f}")

    print("\n===== STALEMATES (the autopilot's own failures, not the game's) =====")
    for (h, arm), sm in stale.items():
        if sm:
            print(f"  {NAME.get(h,h):<9}{arm:<6}" + "  ".join(f"{k}={v}" for k, v in sorted(sm.items())))
    if not any(stale.values()):
        print("  none in any cell")


if __name__ == "__main__":
    main()
