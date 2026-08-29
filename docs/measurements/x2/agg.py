#!/usr/bin/env python3
"""X2: pool the two CRN chunks of a rung (BASE 0 + BASE 10) into one row.

Counts (runs, wins, per-objective n, shots, kills) pool EXACTLY. The harness publishes the
decision-richness numbers as RATIOS, so those pool as the unweighted mean of the two
equal-sized (20-run) chunks — the W4 convention, kept so rows stay comparable across waves.
Usage: agg.py <tag-prefix>...   (reads <prefix>-b0.json and <prefix>-b10.json)
"""
import json, sys, os, math
D = os.path.dirname(os.path.abspath(__file__))

RICH = ['meaningfulChoicesPerTurn','choicesPerArmedSoldierTurn','armedSoldiersPerTurn',
        'actingSoldiersPerTurn','losTargetsPerArmedSoldierTurn','targetChoicesPerArmedSoldierTurn',
        'positionChoicesPerArmedSoldierTurn','leadSwingsPerMatch','turnsWithAShotPct']

def load(pfx):
    out = []
    for b in ('b0','b10'):
        p = os.path.join(D, f'{pfx}-{b}.json')
        if os.path.exists(p):
            d = json.load(open(p))
            d['_tag'] = f'{pfx}-{b}'
            out.append(d)
    return out

def pool(pfx):
    cs = load(pfx)
    if not cs: return None
    runs = sum(c['runs'] for c in cs)
    wins = sum(round(c['runWinRate']*c['runs']/100.0) for c in cs)
    comp = 100.0*wins/runs
    se = 100.0*math.sqrt(max(comp/100*(1-comp/100),0)/runs)
    miss = sum(c['missions'] for c in cs)
    mn = sum(sum(h['n'] for h in c['byHeat']) for c in cs)
    mw = sum(sum(h['n']*h['winRate'] for h in c['byHeat']) for c in cs)/max(mn,1)
    mt = sum(sum(h['n']*h['avgTurns'] for h in c['byHeat']) for c in cs)/max(mn,1)
    r = {k: sum(c['decisionRichness'][k] for c in cs)/len(cs) for k in RICH}
    shots = sum(sum(p['shots'] for p in c['playerClasses']) for c in cs)
    kills = sum(sum(p['kills'] for p in c['playerClasses']) for c in cs)
    obj = {}
    for c in cs:
        for o in c['byObjective']:
            e = obj.setdefault(o['objective'], [0,0.0,0.0])
            e[0] += o['n']; e[1] += o['n']*o['winRate']; e[2] += o['n']*o['avgTurns']
    obj = {k: (v[0], v[1]/v[0], v[2]/v[0]) for k, v in obj.items() if v[0]}
    mis = {}
    for c in cs:
        for m in c['byMission']:
            e = mis.setdefault(m['mission'], [0,0.0]); e[0]+=m['n']; e[1]+=m['n']*m['winRate']
    mis = {k:(v[0], v[1]/v[0]) for k,v in sorted(mis.items()) if v[0]}
    loss = {}
    for c in cs:
        for k, v in c.get('lossCauses', {}).items(): loss[k] = loss.get(k, 0)+v
    return dict(tag=pfx, chunks=[c['_tag'] for c in cs], runs=runs, wins=wins, comp=comp, se=se,
                missions=miss, misswin=mw, turns=mt, shots=shots, kills=kills,
                spk=shots/max(kills,1), obj=obj, mis=mis, loss=loss, **r)

def fmt(p):
    print(f"\n=== {p['tag']}  (chunks {', '.join(p['chunks'])}; runs={p['runs']}) ===")
    print(f"  completion   {p['comp']:.1f}% +-{p['se']:.1f}  ({p['wins']}/{p['runs']})")
    print(f"  mission win  {p['misswin']:.1f}%  (n={p['missions']})   mean turns {p['turns']:.2f}")
    print(f"  ch/turn {p['meaningfulChoicesPerTurn']:.2f}  ch/ARMED {p['choicesPerArmedSoldierTurn']:.2f}"
          f"  armed/turn {p['armedSoldiersPerTurn']:.2f}  acting/turn {p['actingSoldiersPerTurn']:.2f}")
    print(f"  los/ARM {p['losTargetsPerArmedSoldierTurn']:.2f}  tgt/ARM {p['targetChoicesPerArmedSoldierTurn']:.2f}"
          f"  pos/ARM {p['positionChoicesPerArmedSoldierTurn']:.2f}  swings {p['leadSwingsPerMatch']:.2f}")
    print(f"  shots/kill   {p['spk']:.2f}  ({p['shots']}/{p['kills']})")
    print("  objective        n   win%   turns")
    for k, (n, w, t) in sorted(p['obj'].items(), key=lambda kv: -kv[1][2]):
        print(f"    {k:<12} {n:4d}  {w:5.1f}  {t:6.2f}")
    print("  by mission:  " + "  ".join(f"m{k}:{w:.0f}%(n{n})" for k,(n,w) in p['mis'].items()))
    if p['loss']: print("  loss causes:", ", ".join(f"{k}={v}" for k, v in sorted(p['loss'].items(), key=lambda kv:-kv[1])))

if __name__ == '__main__':
    rows = [pool(a) for a in sys.argv[1:]]
    for r in rows:
        if r: fmt(r)
    print("\n--- LADDER ---")
    print(f"{'rung':<14}{'n':>5}{'compl':>9}{'+-':>6}{'miswin':>8}{'turns':>7}{'ch/t':>6}{'ch/ARM':>8}{'arm/t':>7}{'swing':>7}{'s/kill':>8}")
    for r in rows:
        if r: print(f"{r['tag']:<14}{r['runs']:>5}{r['comp']:>8.1f}%{r['se']:>6.1f}{r['misswin']:>8.1f}{r['turns']:>7.2f}"
                    f"{r['meaningfulChoicesPerTurn']:>6.2f}{r['choicesPerArmedSoldierTurn']:>8.2f}"
                    f"{r['armedSoldiersPerTurn']:>7.2f}{r['leadSwingsPerMatch']:>7.2f}{r['spk']:>8.2f}")
