#!/usr/bin/env python3
"""C3 — prove two rounds are the SAME ARM (every pre-existing aggregate field identical).

Used for one thing: B1 (the lever binary with SIGHTLINE_KILLTREADMILL=1) must reproduce D0 (the
pre-lever binary) exactly. If it does, the lever's OFF path is the old tree and the L1-vs-B1
comparison prices the lever and nothing else. Usage: samearm.py <tagA> <tagB>
"""
import json, glob, os, sys

DIR = os.path.dirname(os.path.abspath(__file__))
DROP = {"harness", "instrument"}


def flat(o, p="", out=None):
    if out is None:
        out = {}
    if isinstance(o, dict):
        for k, v in o.items():
            flat(v, f"{p}.{k}" if p else k, out)
    elif isinstance(o, list):
        for i, v in enumerate(o):
            flat(v, f"{p}[{i}]", out)
    else:
        out[p] = o
    return out


A, B = sys.argv[1], sys.argv[2]
bad = fields = pairs = 0
for fa in sorted(glob.glob(os.path.join(DIR, A + "-h*-b*.json"))):
    fb = os.path.join(DIR, os.path.basename(fa).replace(A + "-", B + "-", 1))
    if not os.path.exists(fb):
        print("MISSING", fb); bad += 1; continue
    da = {k: v for k, v in json.load(open(fa)).items() if k not in DROP}
    db = {k: v for k, v in json.load(open(fb)).items() if k not in DROP}
    ka, kb = flat(da), flat(db)
    keys = set(ka) | set(kb)
    diffs = [k for k in sorted(keys) if ka.get(k, "<absent>") != kb.get(k, "<absent>")]
    fields += len(keys); pairs += 1
    tag = os.path.basename(fa).replace(A + "-", "").replace(".json", "")
    if diffs:
        bad += 1
        print(f"DIFF {tag}: {len(diffs)}/{len(keys)} fields")
        for k in diffs[:8]:
            print(f"    {k}: {ka.get(k,'<absent>')!r} -> {kb.get(k,'<absent>')!r}")
print(f"{pairs} chunk pairs, {fields} aggregate fields diffed, {bad} with a difference")
sys.exit(1 if bad else 0)
