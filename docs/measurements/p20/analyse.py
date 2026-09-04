#!/usr/bin/env python3
"""P20 — CRN-paired comparison of the fix arm against the SIGHTLINE_STALEGROUND=1 control."""
import json, math, os
BASES = [0, 10, 20, 30, 40, 50, 60, 70]
HERE = os.path.dirname(os.path.abspath(__file__))
FIELDS = ['win', 'missionsCleared', 'lossCause', 'runTurns', 'endMission', 'endObjective']
def load(t): return json.load(open(os.path.join(HERE, t + ".json")))
for h in (0, 4):
    n = fw = sw = ao = bo = wd = 0
    per = []
    for b in BASES:
        A, B = load(f"fix-h{h}-b{b}"), load(f"stale-h{h}-b{b}")
        ca = {(c['slot'], c['policy']): c for c in A['campaigns']}
        cb = {(c['slot'], c['policy']): c for c in B['campaigns']}
        ks = sorted(set(ca) & set(cb)); cw = cs = 0
        for k in ks:
            n += 1
            if any(ca[k][f] != cb[k][f] for f in FIELDS): wd += 1
            if ca[k]['win'] and not cb[k]['win']: ao += 1
            if cb[k]['win'] and not ca[k]['win']: bo += 1
            cw += ca[k]['win']; cs += cb[k]['win']
        fw += cw; sw += cs
        per.append((b, 100 * (cw - cs) / len(ks)))
    d = ao + bo
    z = (ao - bo) / math.sqrt(d) if d else 0.0
    ds = [p[1] for p in per]; m = sum(ds) / len(ds)
    sd = math.sqrt(sum((x - m) ** 2 for x in ds) / (len(ds) - 1)); se = sd / math.sqrt(len(ds))
    print(f"heat {h}: n={n}/arm  fix={100*fw/n:.1f}%  stale={100*sw/n:.1f}%  delta={100*(fw-sw)/n:+.1f}  "
          f"worldsDiffer={100*wd/n:.1f}%  McNemar {ao}/{bo} z={z:+.2f}  cluster t(7)={m/se:+.2f} (mean {m:+.1f} +/- {se:.1f})")
    print("   per slot set:", " ".join(f"b{b}:{x:+.0f}" for b, x in per))
