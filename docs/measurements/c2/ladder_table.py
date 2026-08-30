#!/usr/bin/env python3
"""C2 ladder table — pools the four slot sets per rung per arm and prints the paired ladder.

  python3 ladder_table.py

Reads docs/measurements/c2/L-{BASE,DECL}-h{R,0,2,4,6,8}-b{0,10,20,30}.json.
Every rung is FOUR slot sets (CLAUDE.md: a rung is four slot sets or it is not a rung);
each chunk is 10 greedy + 10 sloppy campaigns, so n=80 per rung per arm.
SE is the binomial standard error of the pooled run-completion rate.
"""
import json, math, os

D = os.path.dirname(os.path.abspath(__file__))
RUNGS = [("R", "RECRUIT", 75, 8), ("0", "heat 0", 55, 8), ("2", "heat 2", 40, 8),
         ("4", "heat 4", 30, 8), ("6", "heat 6", 20, 8), ("8", "heat 8", 10, 5)]
BASES = [0, 10, 20, 30]

def pool(arm, rung):
    wins = n = 0
    turns_sum = miss = 0.0
    shots = kills = 0
    dec = {}
    withshot = declined = contested = lanes = reacts = 0
    for b in BASES:
        p = os.path.join(D, f"L-{arm}-h{rung}-b{b}.json")
        d = json.load(open(p))
        runs = d["runs"]
        wins += round(d["runWinRate"] * runs / 100.0)
        n += runs
        for m in d.get("byHeat", []):
            turns_sum += m["avgTurns"] * m["n"]; miss += m["n"]
        for c in d.get("playerClasses", []):
            shots += c["shots"]; kills += c["kills"]
        ed = d.get("enemyDecisions", {})
        contested += ed.get("contestedActs", 0)
        withshot += ed.get("actsWithShot", 0)
        declined += sum(ed.get("shotDeclined", []))
        lanes += ed.get("lanesHeld", 0)
        reacts += ed.get("reactionShots", 0)
        for m in ed.get("mix", []):
            dec[m["verb"]] = dec.get(m["verb"], 0) + m["n"]
    pct = 100.0 * wins / n
    se = 100.0 * math.sqrt(pct / 100.0 * (1 - pct / 100.0) / n)
    return dict(n=n, pct=pct, se=se, turns=turns_sum / max(1, miss),
                spk=shots / max(1, kills), dec=dec, contested=contested,
                withshot=withshot, declined=declined, lanes=lanes, reacts=reacts)

print("C2 LADDER — CRN-paired, one binary, arms differ only by SIGHTLINE_AIDECLINE")
print(f"{'rung':<9}{'BASE(=0)':>12}{'DECL(=1)':>12}{'delta':>8}{'band':>8}{'floor':>7}"
      f"{'turnsB':>8}{'turnsD':>8}{'spkB':>7}{'spkD':>7}")
rows = []
for key, label, band, tol in RUNGS:
    b = pool("BASE", key); d = pool("DECL", key)
    rows.append((label, b, d, band, tol))
    print(f"{label:<9}{b['pct']:>7.1f}±{b['se']:<4.1f}{d['pct']:>7.1f}±{d['se']:<4.1f}"
          f"{d['pct']-b['pct']:>+8.1f}{band:>8}{band-tol:>7}"
          f"{b['turns']:>8.2f}{d['turns']:>8.2f}{b['spk']:>7.3f}{d['spk']:>7.3f}")

print("\nIN-BAND CHECK (DECL arm, the shipped default):")
for label, b, d, band, tol in rows:
    lo, hi = band - tol, band + tol
    verdict = "in" if lo <= d["pct"] <= hi else ("BELOW" if d["pct"] < lo else "ABOVE")
    print(f"  {label:<9}{d['pct']:>6.1f}  band {band}±{tol} -> {verdict}")

print("\nENEMY DECISION MIX (pooled over all six rungs):")
for arm in ("BASE", "DECL"):
    tot = {}
    con = ws = dcl = ln = rc = 0
    for key, _, _, _ in [(r[0], 0, 0, 0) for r in RUNGS]:
        p = pool(arm, key)
        con += p["contested"]; ws += p["withshot"]; dcl += p["declined"]
        ln += p["lanes"]; rc += p["reacts"]
        for k, v in p["dec"].items(): tot[k] = tot.get(k, 0) + v
    print(f"  {arm}: contested={con}  shotsOnTable={ws}  declined={dcl} ({100.0*dcl/max(1,ws):.2f}%)"
          f"  lanesHeld={ln}  reactionShots={rc} ({100.0*rc/max(1,ln):.0f}% paid off)")
    for k, v in sorted(tot.items(), key=lambda kv: -kv[1]):
        print(f"      {k:<12}{v:>7}  {100.0*v/max(1,con):>5.1f}%")
