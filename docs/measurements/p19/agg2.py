#!/usr/bin/env python3
"""P19 supplement: re-derived per-archetype exposure (body rate AND per-mission presence,
which is the denominator the one-per-mission caps actually govern), stalemate arms, and the
health lines the measurement contract asks to be read rather than assumed."""
import json, glob
from collections import Counter
OUT='docs/measurements/p19'; ARMS=['pre','boss','full']; RUNGS=[0,4,8]
BASES=[0,10,20,30,40,50,60,70]

for arm in ARMS:
    f=Counter(); d=Counter(); missions=0; runs=0; stale_m=0; stale_r=0; pinned=True; above=0
    for h in RUNGS:
        for b in BASES:
            j=json.load(open(f'{OUT}/p19-{arm}-h{h}-b{b}.json'))
            ec=j['enemyComposition']; f.update(ec['factionSpawns']); d.update(ec['defaultSpawns'])
            missions+=j['missions']; runs+=j['runs']
            hl=j.get('heatLeak',{}); pinned &= bool(hl.get('pinned')); above+=hl.get('missionsAbovePin',0)
            for c in j['campaigns']:
                lc=c.get('lossCause','')
                if lc=='STALEMATE-MISSION': stale_m+=1
                if lc=='STALEMATE-RUN': stale_r+=1
    tot=sum(f.values())+sum(d.values()); allc=f+d
    print(f'\n===== ARM {arm}: {runs} campaigns, {missions} missions, {tot} hostile spawns '
          f'({tot/missions:.2f}/mission) | heatPinned={pinned} missionsAbovePin={above} '
          f'| STALEMATE mission={stale_m} run={stale_r} ({100.0*(stale_m+stale_r)/runs:.2f}%)')
    print(f'  {"class":<12}{"spawns":>7}{"%bodies":>9}{"/mission":>10}{"missions with >=1 (cap)":>26}')
    for c,v in allc.most_common():
        cap = ' (cap 1/mission)' if c in ('BOMBARD','WARBRINGER') else ''
        pm = 100.0*v/missions
        print(f'  {c:<12}{v:7d}{100.0*v/tot:8.2f}%{v/missions:10.3f}{pm:14.1f}%{cap}')
