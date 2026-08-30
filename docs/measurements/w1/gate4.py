#!/usr/bin/env python3
"""W1 GATE 4 — does the ARCHIVE still reproduce?

Compares the per-slot CRN records of two balance JSONs (the paired-policy `slots` block, which
is the finest-grained record the artifact keeps: per slot, did greedy win, how many missions did
each policy clear). Usage: gate4.py <a.json> <b.json>
"""
import json, sys

def slots(p):
    d = json.load(open(p))
    return d, {s["slot"]: (s["greedyWin"], s["greedyMissions"], s["sloppyWin"], s["sloppyMissions"])
               for s in d["pairedPolicy"]["slots"]}

da, a = slots(sys.argv[1])
db, b = slots(sys.argv[2])
print(f"A {sys.argv[1]}  runs={da['runs']} winRate={da['runWinRate']}")
print(f"B {sys.argv[2]}  runs={db['runs']} winRate={db['runWinRate']}")
same = 0
for k in sorted(set(a) | set(b)):
    if a.get(k) == b.get(k):
        same += 1
    else:
        print(f"  slot {k}: A={a.get(k)}  B={b.get(k)}")
print(f"identical slots: {same}/{len(set(a) | set(b))}")
sys.exit(0 if same == len(set(a) | set(b)) else 1)
