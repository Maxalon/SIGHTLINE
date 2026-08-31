#!/usr/bin/env python3
"""C3 — pool the encounterMidrun block across a round's 48 chunks.

Every rate is re-derived from summed integer numerators and denominators, never averaged over
chunks: a chunk holding 3 mid-run Hacks must not weigh as much as one holding 30. That is why the
row carries killedSum / forceSum / winKilledSum / winForceSum as raw integers.

Usage: enc.py <round-tag> [more tags...]
"""
import json, glob, os, sys, math

DIR = os.path.dirname(os.path.abspath(__file__))
ORDER = ["Eliminate", "Decapitate", "Defend", "Escort", "Evac", "Hack", "Rescue", "Sabotage",
         "KILL", "NONKILL"]


def se(w, n):
    if n == 0:
        return 0.0
    p = w / n
    return 100.0 * math.sqrt(max(p * (1 - p), 0.0) / n)


def pool(tag):
    acc = {}
    for f in sorted(glob.glob(os.path.join(DIR, tag + "-h*-b*.json"))):
        d = json.load(open(f))
        for r in d.get("encounterMidrun", []):
            n = r["n"]
            if n == 0:
                continue
            a = acc.setdefault(r["row"], dict(n=0, wins=0, turns=0.0, start=0.0, added=0.0,
                                              reinf=0.0, prs=0.0, loss=0.0, dmg=0.0,
                                              kill=0, force=0, wn=0, wkill=0, wforce=0,
                                              wloss=0.0, wturns=0.0))
            a["n"] += n
            a["wins"] += round(n * r["winRate"] / 100.0)
            a["turns"] += n * r["avgTurns"]
            a["start"] += n * r["enemiesStart"]
            a["added"] += n * r["enemiesAdded"]
            a["reinf"] += n * r["reinforcedPct"] / 100.0
            a["prs"] += n * r["avgPressure"]
            a["loss"] += n * r["squadLoss"]
            a["dmg"] += n * r["dmgTaken"]
            a["kill"] += r["killedSum"]
            a["force"] += r["forceSum"]
            wn = r["winN"]
            a["wn"] += wn
            a["wkill"] += r["winKilledSum"]
            a["wforce"] += r["winForceSum"]
            a["wloss"] += wn * r["winSquadLoss"]
            a["wturns"] += wn * r["winTurns"]
    return acc


def report(tag):
    acc = pool(tag)
    print(f"===== {tag} — ENCOUNTER COMPLETION, mid-run campaign nodes (Combat+Elite) =====")
    print(f"  {'row':<12}{'n':>5}{'win%':>7}{'+-SE':>6}{'turns':>7}{'force':>7}{'+rf':>6}"
          f"{'clear%':>8}{'rf%':>6}{'prs':>6}{'sqLoss':>8}{'dmgTk':>7}"
          f"{'| WON n':>9}{'clear%':>8}{'sqLoss':>8}{'turns':>7}")
    for k in ORDER + [k for k in acc if k not in ORDER]:
        if k not in acc:
            continue
        a = acc[k]
        n = a["n"]
        wn = a["wn"]
        print(f"  {k:<12}{n:>5}{100.0*a['wins']/n:>7.1f}{se(a['wins'],n):>6.1f}{a['turns']/n:>7.2f}"
              f"{a['start']/n:>7.2f}{a['added']/n:>6.2f}"
              f"{(100.0*a['kill']/a['force'] if a['force'] else 0):>8.1f}"
              f"{100.0*a['reinf']/n:>6.1f}{a['prs']/n:>6.2f}{a['loss']/n:>8.2f}{a['dmg']/n:>7.1f}"
              f"{wn:>9}{(100.0*a['wkill']/a['wforce'] if a['wforce'] else 0):>8.1f}"
              f"{(a['wloss']/wn if wn else 0):>8.2f}{(a['wturns']/wn if wn else 0):>7.2f}")
    print()


if __name__ == "__main__":
    for t in sys.argv[1:]:
        report(t)
