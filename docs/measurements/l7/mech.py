#!/usr/bin/env python3
"""L7 EVERY RUNG — THE MECHANISM VIEW. What each rung actually changed about the FIGHT.

C1's precedent: when a rung buys nothing, read `Heat.Mods` for a declaration that cannot fire.
`SIGHTLINE_MIDTOOTHTEST` now asserts that class away (no dead AiTier, no silent rung), so when a
rung still buys nothing the cause has to be somewhere the cumulative vector cannot see. The three
candidates the brief names are a tooth that fires but does nothing at that rung, a CLAMP, and a
rung whose only change is invisible to the autopilot — and all three leave fingerprints in the
batch JSON that this tool pools per rung:

  force        `encounterMidrun.enemiesStart` (MID-RUN missions only, so mission 1 is excluded).
               `Mission.Build` clamps the headcount to 12 — `Math.Clamp(EnemyBaseCount + n +
               enemyDelta, 3, 12)` — so a rung whose whole tooth is `EnemyDelta = 1` buys nothing
               on any mission already at the cap. If h7 -> h8 is flat and enemiesStart did not
               move, that is the clamp and not a coincidence.
  depth        `avgMissionsCleared`. Rung 5 (HarshAttrition) and rung 7 (NoReinforcements) are
               RUN-LOOP levers: they can only bite over missions the bot survives to play. A rung
               whose tooth needs mission 4 is inert on a ladder whose campaigns end at mission 2.
  reach        per-MISSION win rate. A rung that moves only late missions cannot move a win rate
               whose denominator is dominated by early ones.
  cost         squad losses and damage taken per mid-run mission — a rung can make the fight
               dearer without changing who wins it, and the win-rate ladder is blind to that.

Usage: mech.py [prefix] [--dir DIR]
"""
import glob, json, math, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
RUNGS = ["R", "0", "1", "2", "3", "4", "5", "6", "7", "8"]
OBJ = {"Eliminate", "Decapitate", "Defend", "Escort", "Hack", "Rescue", "Sabotage", "Evac"}


def base_of(f):
    return int(os.path.basename(f).rsplit("-b", 1)[1].split(".")[0])


def main():
    args = sys.argv[1:]
    d = HERE
    pos = []
    i = 0
    while i < len(args):
        if args[i] == "--dir": d = args[i + 1]; i += 2
        else: pos.append(args[i]); i += 1
    prefix = pos[0] if pos else "L7"

    print(f"===== {prefix} — THE MECHANISM VIEW (pooled over all chunks of each rung) =====")
    print(f"  {'rung':<6}{'runs':>6}{'win%':>7}{'avgMis':>7}{'misWin%':>8}{'turns':>6}"
          f"{'force':>7}{'added':>7}{'kill%':>7}{'sqLoss':>7}{'dmg':>7}{'ch/ARM':>8}")
    per = {}
    for h in RUNGS:
        files = sorted(glob.glob(os.path.join(d, f"{prefix}-h{h}-b*.json")), key=base_of)
        if not files:
            continue
        runs = mis = 0
        wins = 0.0
        misclr = 0.0
        mn = mw = 0            # mission n / wins
        turns = 0.0
        fN = 0.0; fStart = 0.0; fAdd = 0.0; fLoss = 0.0; fDmg = 0.0
        killed = force = 0.0
        chn = 0.0
        bymis = {}
        nodek = {}
        loss = {}
        for f in files:
            j = json.load(open(f))
            r = j["runs"]
            runs += r
            wins += j["runWinRate"] * r / 100.0
            misclr += j["avgMissionsCleared"] * r
            chn += j["decisionRichness"]["choicesPerArmedSoldierTurn"] * r
            for row in j["byHeat"]:
                mn += row["n"]; mw += row["n"] * row["winRate"] / 100.0; turns += row["n"] * row["avgTurns"]
            for row in j["encounterMidrun"]:
                if row["row"] not in OBJ:
                    continue
                n = row["n"]
                fN += n; fStart += n * row["enemiesStart"]; fAdd += n * row["enemiesAdded"]
                fLoss += n * row["squadLoss"]; fDmg += n * row["dmgTaken"]
                killed += row["killedSum"]; force += row["forceSum"]
            for row in j["byMission"]:
                a = bymis.setdefault(row["mission"], [0, 0.0])
                a[0] += row["n"]; a[1] += row["n"] * row["winRate"] / 100.0
            for row in j["byNodeKind"]:
                a = nodek.setdefault(row["nodeKind"], [0, 0.0])
                a[0] += row["n"]; a[1] += row["n"] * row["winRate"] / 100.0
            for k, v in j["lossCauses"].items():
                loss[k] = loss.get(k, 0) + v
        per[h] = dict(runs=runs, win=100.0 * wins / runs, avgMis=misclr / runs,
                      misWin=100.0 * mw / mn, turns=turns / mn,
                      force=fStart / fN if fN else 0, added=fAdd / fN if fN else 0,
                      killpct=100.0 * killed / force if force else 0,
                      sqLoss=fLoss / fN if fN else 0, dmg=fDmg / fN if fN else 0,
                      ch=chn / runs, bymis=bymis, nodek=nodek, loss=loss, midN=fN)
        p = per[h]
        print(f"  h{h:<5}{runs:>6}{p['win']:>7.1f}{p['avgMis']:>7.2f}{p['misWin']:>8.1f}{p['turns']:>6.1f}"
              f"{p['force']:>7.2f}{p['added']:>7.2f}{p['killpct']:>7.1f}{p['sqLoss']:>7.2f}{p['dmg']:>7.1f}{p['ch']:>8.3f}")
    print("  force = mean hostiles at mission start, MID-RUN missions only (mission 1 excluded);")
    print("  added = mean reinforcements; kill% = share of the deployed force killed; sqLoss/dmg per mid-run mission.")

    print(f"\n===== {prefix} — MISSION WIN RATE BY MISSION NUMBER (where a rung reaches) =====")
    ms = sorted({m for h in per for m in per[h]["bymis"]})
    print("  rung  " + "".join(f"{'m'+str(m):>14}" for m in ms))
    for h in RUNGS:
        if h not in per:
            continue
        cells = []
        for m in ms:
            a = per[h]["bymis"].get(m)
            cells.append(f"{100.0*a[1]/a[0]:>8.1f}(n{a[0]})" if a and a[0] else f"{'-':>14}")
        print(f"  h{h:<5}" + "".join(cells))

    print(f"\n===== {prefix} — MISSION WIN RATE BY NODE KIND =====")
    kinds = sorted({k for h in per for k in per[h]["nodek"]})
    print("  rung  " + "".join(f"{k:>14}" for k in kinds))
    for h in RUNGS:
        if h not in per:
            continue
        cells = []
        for k in kinds:
            a = per[h]["nodek"].get(k)
            cells.append(f"{100.0*a[1]/a[0]:>8.1f}(n{a[0]})" if a and a[0] else f"{'-':>14}")
        print(f"  h{h:<5}" + "".join(cells))

    print(f"\n===== {prefix} — RUN LOSS CAUSES (campaign) =====")
    causes = sorted({c for h in per for c in per[h]["loss"]})
    print("  rung  " + "".join(f"{c:>16}" for c in causes))
    for h in RUNGS:
        if h not in per:
            continue
        tot = sum(per[h]["loss"].values()) or 1
        print(f"  h{h:<5}" + "".join(f"{per[h]['loss'].get(c,0):>10}{100.0*per[h]['loss'].get(c,0)/tot:>5.0f}%" for c in causes))


if __name__ == "__main__":
    main()
