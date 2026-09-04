#!/usr/bin/env python3
"""P23 — THE PER-MISSION BREAKDOWN, including the opener cell.

L7's whole point is that the CAMPAIGN row and the MISSION row disagreed: the apex rung bought
-0.6 on the campaign while the mission ladder was monotone at all nine steps, and mission 6 moved
the WRONG WAY. So a campaign row alone is not a reading of this change.

Aggregates `byMission` across the 16 chunks of an arm (n-weighted, i.e. the pooled conditional
win rate at that mission given the run reached it) and prints arm-by-arm per rung. Mission 1 is
printed first and separately: DESIGN.md 3.D forbids front-loaded anxiety and neither lever may
touch the opener. `avgMissionsCleared` and the stalemate arms come along for the ride.

Usage: permission.py [--dir DIR] [--rungs 0,4,6,8] [--arms base,A,B,AB]
"""
import json, math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
BASES = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150]
NEWBASES = [160, 170, 180, 190, 200, 210, 220, 230, 240, 250, 260, 270, 280, 290, 300, 310]


def main():
    d = HERE
    rungs = ["0", "4", "6", "8"]
    arms = ["base", "A", "B", "AB"]
    args = sys.argv[1:]
    for i, a in enumerate(args):
        if a == "--dir": d = args[i + 1]
        if a == "--rungs": rungs = args[i + 1].split(",")
        if a == "--arms": arms = args[i + 1].split(",")
        if a == "--bases": which = args[i + 1]
    global BASES
    BASES = {"old": BASES, "new": NEWBASES, "both": BASES + NEWBASES}[locals().get("which", "old")]
    print(f"(slot bases: {len(BASES)} sets)")

    print(f"  {'rung':<6}{'arm':<6}" + "".join(f"{'m'+str(m):>12}" for m in range(1, 7))
          + f"{'cleared':>9}{'stale':>7}")
    for h in rungs:
        for a in arms:
            wins = [0] * 7; ns = [0] * 7; cleared = []; stale = 0; runs = 0
            for b in BASES:
                f = os.path.join(d, f"P23-{a}-h{h}-b{b}.json")
                if not os.path.exists(f): continue
                j = json.load(open(f))
                for row in j["byMission"]:
                    m = row["mission"]
                    ns[m] += row["n"]; wins[m] += round(row["n"] * row["winRate"] / 100.0)
                cleared.append(j["avgMissionsCleared"])
                runs += j["runs"]
                for c in j.get("campaigns", []):
                    if str(c.get("lossCause", "")).startswith("STALEMATE"): stale += 1
            if not runs: continue
            cells = "".join(f"{(100.0*wins[m]/ns[m] if ns[m] else float('nan')):>7.1f}/{ns[m]:<4}"
                            for m in range(1, 7))
            print(f"  h{h:<5}{a:<6}{cells}{sum(cleared)/len(cleared):>9.2f}{100.0*stale/runs:>6.1f}%")
        print()
    paired_finale(d, rungs, arms)


def paired_finale(d, rungs, arms):
    """The m6 conditional, CRN-PAIRED — the honest version of the table above.

    `byMission` denominators are per-arm: a lever that changes who REACHES mission 6 changes the
    row's population as well as its value, so the two arms' m6 cells are not the same worlds. This
    restricts to the slots where BOTH arms reached the finale (missionsCleared >= 5) and asks who
    won it there. n is smaller and the comparison is real.
    """
    def rows(arm, h):
        out = {}
        for b in BASES:
            f = os.path.join(d, f"P23-{arm}-h{h}-b{b}.json")
            if not os.path.exists(f): continue
            for c in json.load(open(f)).get("campaigns", []):
                out[(b, c["slot"], c["policy"])] = (c["missionsCleared"] >= 5, bool(c["win"]))
        return out

    print("\n===== THE FINALE, CRN-PAIRED (only worlds where BOTH arms reached mission 6) =====")
    print(f"  {'rung':<6}{'contrast':<12}{'n_both':>8}{'A win%':>8}{'B win%':>8}{'eff':>7}"
          f"{'b':>4}{'c':>4}{'n_disc':>7}{'MDE80':>7}{'z':>7}")
    for h in rungs:
        base = rows("base", h)
        if not base: continue
        for arm in arms:
            if arm == "base": continue
            other = rows(arm, h)
            ks = [k for k in base if k in other and base[k][0] and other[k][0]]
            if not ks: continue
            w1 = sum(1 for k in ks if base[k][1]); w2 = sum(1 for k in ks if other[k][1])
            bb = sum(1 for k in ks if other[k][1] and not base[k][1])
            cc = sum(1 for k in ks if base[k][1] and not other[k][1])
            nd = bb + cc
            se = 100.0 * math.sqrt(nd) / len(ks) if nd else float("nan")
            z = (bb - cc) / math.sqrt(nd) if nd else float("nan")
            print(f"  h{h:<5}{'base->'+arm:<12}{len(ks):>8}{100.0*w1/len(ks):>8.1f}"
                  f"{100.0*w2/len(ks):>8.1f}{100.0*(w2-w1)/len(ks):>7.1f}{bb:>4}{cc:>4}{nd:>7}"
                  f"{2.80*se:>7.1f}{z:>7.2f}")


if __name__ == "__main__":
    main()
