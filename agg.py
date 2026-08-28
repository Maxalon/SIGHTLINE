import json,sys,glob
files=sys.argv[1:]
tot={'runs':0,'wins':0,'missions':0,'mwins':0}
obj={}; heatrun={}
chT=0.0; chN=0; swSum=0.0; swN=0
turnsW=0.0
cls={}
for f in files:
    d=json.load(open(f))
    for h in d['byHeatRun']:
        k=h['heat']; a=heatrun.setdefault(k,[0,0.0])
        a[0]+=h['runs']; a[1]+=h['runWinRate']/100.0*h['runs']
    for o in d['byObjective']:
        a=obj.setdefault(o['objective'],[0,0.0,0.0])
        a[0]+=o['n']; a[1]+=o['winRate']/100.0*o['n']; a[2]+=o['avgTurns']*o['n']
    for h in d['byHeat']:
        tot['missions']+=h['n']; tot['mwins']+=h['winRate']/100.0*h['n']; turnsW+=h['avgTurns']*h['n']
    dr=d.get('decisionRichness') or {}
    # decisionRichness holds per-turn averages; weight by missions
    n=d['missions']
    chT+=dr.get('meaningfulChoicesPerTurn',0)*n; chN+=n
    swSum+=dr.get('leadSwingsPerMatch',0)*n; swN+=n
    for c in d['playerClasses']:
        a=cls.setdefault(c['cls'],[0,0,0.0,0])
        a[0]+=c['shots']; a[1]+=c['hits']; a[2]+=c['dmg']; a[3]+=c['kills']
print("files:",len(files))
for k in sorted(heatrun):
    r,w=heatrun[k]; print(f"  heat {k}: run-completion {100*w/r:.1f}%  (n={int(r)})")
print(f"  mission win {100*tot['mwins']/tot['missions']:.1f}%  n={tot['missions']}  mean turns {turnsW/tot['missions']:.2f}")
print(f"  choices/turn {chT/chN:.2f}   lead-swings/match {swSum/swN:.2f}   (weighted by missions)")
print("  by objective (win% / mean turns / n):")
for k,(n,w,t) in sorted(obj.items(), key=lambda x:-x[1][0]):
    print(f"    {k:<11} {100*w/n:5.1f}%  {t/n:5.2f}t  n={n}")
print("  classes (shots/hit%/dmg/kills/dmg-per-shot):")
for k,(s,h,dm,ki) in cls.items():
    print(f"    {k:<13} {s:5d} {100*h/max(1,s):5.1f}% {dm:7.0f} {ki:4d}  {dm/max(1,s):.2f}")
