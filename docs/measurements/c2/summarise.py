#!/usr/bin/env python3
"""C2 chunk summariser — the tripwires this wave is judged on, printed for one or more
balance JSONs so a BEFORE/AFTER pair can be read side by side.

  python3 summarise.py <a.json> [<b.json> ...]

TRIPWIRES (brief item 3: "do not make the enemy passive"):
  avgTurns       mission length. If declining makes fights LONGER the wave has traded one
                 bad opponent for another.
  shots/kill     player shots divided by player kills — decisiveness of the exchange.
  runWinRate     the consequence, priced.
Plus the ENEMY DECISION MIX this wave exists to move.
"""
import json, sys

def load(p):
    with open(p) as f: return json.load(f)

def row(p):
    d = load(p)
    ms = d.get('byHeat', [])
    n = sum(m['n'] for m in ms) or 1
    turns = sum(m['avgTurns'] * m['n'] for m in ms) / n
    win = sum(m['winRate'] * m['n'] for m in ms) / n
    pc = d.get('playerClasses', [])
    shots = sum(c['shots'] for c in pc); kills = sum(c['kills'] for c in pc)
    hits = sum(round(c['shots'] * c['hitPct'] / 100.0) for c in pc) if pc and 'hitPct' in pc[0] else 0
    ed = d.get('enemyDecisions', {})
    mix = {m['verb']: m['n'] for m in ed.get('mix', [])}
    con = ed.get('contestedActs', 0) or 1
    tk, dc = ed.get('shotTaken', [0]*5), ed.get('shotDeclined', [0]*5)
    return dict(tag=p.split('/')[-1].replace('.json', ''),
                runs=d['runs'], missions=d['missions'], runWin=d['runWinRate'],
                missionWin=round(win, 1), turns=round(turns, 2),
                shots=shots, kills=kills, spk=round(shots / max(1, kills), 3),
                contested=ed.get('contestedActs', 0), withShot=ed.get('actsWithShot', 0),
                declinedPct=ed.get('declinedPct', 0), preempted=ed.get('preempted', 0),
                shoot=mix.get('shoot', 0), over=mix.get('overwatch', 0), hunk=mix.get('hunker', 0),
                move=mix.get('move', 0), idle=mix.get('idle', 0),
                shootPct=round(100.0*mix.get('shoot',0)/con,1), overPct=round(100.0*mix.get('overwatch',0)/con,1),
                hunkPct=round(100.0*mix.get('hunker',0)/con,1), movePct=round(100.0*mix.get('move',0)/con,1),
                taken=tk, declined=dc)

if __name__ == '__main__':
    rows = [row(p) for p in sys.argv[1:]]
    hdr = ('tag', 'runs', 'runWin', 'missionWin', 'turns', 'spk', 'contested',
           'shoot%', 'over%', 'hunk%', 'move%', 'withShot', 'declined%')
    print(f"{hdr[0]:<22}{hdr[1]:>5}{hdr[2]:>8}{hdr[3]:>11}{hdr[4]:>7}{hdr[5]:>7}"
          f"{hdr[6]:>10}{hdr[7]:>8}{hdr[8]:>7}{hdr[9]:>7}{hdr[10]:>7}{hdr[11]:>9}{hdr[12]:>11}")
    for r in rows:
        print(f"{r['tag']:<22}{r['runs']:>5}{r['runWin']:>8}{r['missionWin']:>11}{r['turns']:>7}"
              f"{r['spk']:>7}{r['contested']:>10}{r['shootPct']:>8}{r['overPct']:>7}{r['hunkPct']:>7}"
              f"{r['movePct']:>7}{r['withShot']:>9}{r['declinedPct']:>11}")
    print("\nshot bands (0-19/20-39/40-59/60-79/80+):")
    for r in rows:
        print(f"  {r['tag']:<22} taken {r['taken']}  declined {r['declined']}")
