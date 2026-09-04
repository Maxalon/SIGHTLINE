#!/usr/bin/env python3
"""P16 GROUND TRUTH — read the round's chunks and print the table.

CRN pairing is EXACT: `campaigns[]` carries (slot, policy) per campaign, and both arms run the
same slot seeds off the same binary, so arm A's campaign k and arm B's campaign k are the same
world played with one lever moved. That gives McNemar's discordant counts directly.

Two SEs are reported on purpose, because this project has misread a flat row before:
  * McNemar SE   = sqrt(b+c)/n — the PAIRED sampling error, and the one that says whether a row
                   is 'no effect' or 'no evidence'. b+c IS n_discordant.
  * cluster SE   = the SD of the 8 per-slot-base deltas / sqrt(8) — CLAUDE.md's rule, because a
                   rung is 8 clusters of 20 and the clusters disagree.
MDE is quoted off BOTH at 80% power / two-sided 0.05 (2.8 x SE): a |delta| under the MDE is an
absence of evidence, not neutrality.
"""
import json, glob, math, os, sys, collections
D = os.path.dirname(os.path.abspath(__file__))
BASES = [0,10,20,30,40,50,60,70]
RUNGS = [0,4,8]

def load(arm,h,b):
    p = f"{D}/{arm}-h{h}-b{b}.json"
    return json.load(open(p)) if os.path.exists(p) else None

def cell(rows, key, val):
    for r in rows:
        if r[key]==val: return r
    return None

print(f"{'':6} {'A win%':>8} {'B win%':>8} {'delta':>7} {'McNSE':>7} {'clSE':>7} "
      f"{'t(7)':>6} {'discor':>7} {'b/c':>7} {'MDE_McN':>8} {'MDE_cl':>7}   n/arm")
summary={}
for h in RUNGS:
    A={}; B={}; deltas=[]; nA=nB=0; wA=wB=0
    ok=True
    for b in BASES:
        da,db = load("A",h,b), load("B",h,b)
        if not da or not db: ok=False; break
        ca = {(c['slot'],c['policy']):c['win'] for c in da['campaigns']}
        cb = {(c['slot'],c['policy']):c['win'] for c in db['campaigns']}
        assert set(ca)==set(cb), f"slot sets differ h{h} b{b}"
        A.update({(b,)+k:v for k,v in ca.items()}); B.update({(b,)+k:v for k,v in cb.items()})
        wa=sum(ca.values()); wb=sum(cb.values())
        deltas.append(100.0*wa/len(ca) - 100.0*wb/len(cb))
    if not ok: print(f"h{h:<5} INCOMPLETE"); continue
    keys=sorted(A); n=len(keys)
    wA=sum(A[k] for k in keys); wB=sum(B[k] for k in keys)
    bb=sum(1 for k in keys if A[k] and not B[k]); cc=sum(1 for k in keys if B[k] and not A[k])
    disc=bb+cc
    mcn = 100.0*math.sqrt(disc)/n if disc else 0.0
    m=sum(deltas)/len(deltas)
    sd=math.sqrt(sum((d-m)**2 for d in deltas)/(len(deltas)-1))
    cl=sd/math.sqrt(len(deltas))
    t=m/cl if cl else float('nan')
    summary[h]=dict(a=100.0*wA/n,b=100.0*wB/n,d=m,mcn=mcn,cl=cl,t=t,disc=disc,bb=bb,cc=cc,n=n)
    print(f"h{h:<5} {100.0*wA/n:8.1f} {100.0*wB/n:8.1f} {m:+7.1f} {mcn:7.2f} {cl:7.2f} "
          f"{t:+6.2f} {disc:7d} {bb:3d}/{cc:<3d} {2.8*mcn:8.1f} {2.8*cl:7.1f}   {n}")

print("\nPOOLED over the three rungs (McNemar on all 480 CRN pairs):")
tb=sum(summary[h]['bb'] for h in summary); tc=sum(summary[h]['cc'] for h in summary)
tn=sum(summary[h]['n'] for h in summary)
if tb+tc:
    z=(tb-tc)/math.sqrt(tb+tc)
    print(f"  b={tb} (A won, B lost)  c={tc} (B won, A lost)  discordant={tb+tc}/{tn}"
          f"  z={z:+.2f}  delta={100.0*(tb-tc)/tn:+.2f} pts  McNemar SE={100*math.sqrt(tb+tc)/tn:.2f}")

# --- the mission-1 cell (C4's finding: its layer's cost landed on the opener) ----------------
print("\nbyNodeKind — the OPENER cell (C4 measured Start -4.37, t=-4.53, for its own layer):")
for h in RUNGS:
    out=[]
    for kind in ("Start","Combat","Elite","Boss"):
        acc={}
        for arm in "AB":
            nn=ww=0
            for b in BASES:
                d=load(arm,h,b)
                if not d: continue
                r=cell(d['byNodeKind'],'nodeKind',kind)
                if r: nn+=r['n']; ww+=round(r['winRate']*r['n']/100.0)
            acc[arm]=(nn,ww)
        (na,wa),(nb,wb)=acc['A'],acc['B']
        if na and nb:
            pa,pb=100.0*wa/na,100.0*wb/nb
            se=math.sqrt(pa*(100-pa)/na+pb*(100-pb)/nb)
            out.append(f"{kind} {pa:5.1f} vs {pb:5.1f} = {pa-pb:+5.1f} (uSE {se:4.1f}, n {na}/{nb})")
    print(f"  h{h}: " + " | ".join(out))

# --- the two biomes the lever actually touches -----------------------------------------------
print("\nbyBiome — mission-level win rate on the two biomes P16 changed (and a paint control):")
for h in RUNGS:
    out=[]
    for bio in ("VOID","ARID","STEEL"):
        acc={}
        for arm in "AB":
            nn=ww=0; g=0.0
            for b in BASES:
                d=load(arm,h,b)
                if not d: continue
                r=cell(d['byBiome'],'biome',bio)
                if r: nn+=r['n']; ww+=round(r['winRate']*r['n']/100.0); g+=r['avgGroundTiles']*r['n']
            acc[arm]=(nn,ww,g/nn if nn else 0)
        (na,wa,ga),(nb,wb,gb)=acc['A'],acc['B']
        if na and nb:
            pa,pb=100.0*wa/na,100.0*wb/nb
            se=math.sqrt(pa*(100-pa)/na+pb*(100-pb)/nb)
            out.append(f"{bio} {pa:5.1f} vs {pb:5.1f} = {pa-pb:+5.1f} (uSE {se:4.1f}, n {na}/{nb}, ground {ga:.1f}/{gb:.1f})")
    print(f"  h{h}: " + "\n       ".join(out))

# --- sanity: the arm really was the arm ------------------------------------------------------
print("\nARM CHECK (avgGroundTiles must be >0 for VOID/ARID in A and EXACTLY 0 in B):")
for arm in "AB":
    tot=collections.defaultdict(float); cnt=collections.defaultdict(int)
    for h in RUNGS:
        for b in BASES:
            d=load(arm,h,b)
            if not d: continue
            for r in d['byBiome']: tot[r['biome']]+=r['avgGroundTiles']*r['n']; cnt[r['biome']]+=r['n']
    print(f"  arm {arm}: " + "  ".join(f"{k}={tot[k]/cnt[k]:.1f}" for k in sorted(cnt)))

print("\nSTALEMATES / instrument health:")
for arm in "AB":
    s=0; r=0; ms=0; fc=0; ab=0
    for h in RUNGS:
        for b in BASES:
            d=load(arm,h,b)
            if not d: continue
            ih=d.get('instrumentHealth') or {}
            s+=ih.get('stalemateLosses',0); r+=d['runs']; ms+=d['missions']
            fc+=ih.get('frameCapLosses',0); ab+=ih.get('abortedRuns',0)
    print(f"  arm {arm}: runs={r} missions={ms} stalemateLosses={s} frameCapLosses={fc} abortedRuns={ab}")
