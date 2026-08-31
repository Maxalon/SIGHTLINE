#!/usr/bin/env python3
"""C3 INERTNESS GATE — the same shape as docs/measurements/w1/inert_diff.sh, extended to report
the field COUNT it compared (a diff that compares nothing is indistinguishable from a clean one).

Compares each R0diag-*.json (instrumented binary) against the matching R0-*.json (pristine binary,
same slots), after deleting the keys that are DESIGNED to differ:
  harness        — records nproc/loadavg/elapsed; varies by construction (w1's own exclusion)
  instrument     — echoes the instrument's own configuration
  encounterMidrun— the block this wave ADDED; it does not exist on the pristine side
Every other key must match to the last digit, or the instrumentation is not inert.
"""
import json, os, sys, glob

DIR = os.path.dirname(os.path.abspath(__file__))
DROP = {"harness", "instrument", "encounterMidrun"}


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


bad = 0
fields = 0
pairs = 0
for f in sorted(glob.glob(os.path.join(DIR, "R0diag-*.json"))):
    base = os.path.join(DIR, os.path.basename(f).replace("R0diag-", "R0-"))
    if not os.path.exists(base):
        print("MISSING BASELINE for", f); bad += 1; continue
    a = {k: v for k, v in json.load(open(base)).items() if k not in DROP}
    b = {k: v for k, v in json.load(open(f)).items() if k not in DROP}
    fa, fb = flat(a), flat(b)
    keys = set(fa) | set(fb)
    diffs = [k for k in sorted(keys) if fa.get(k, "<absent>") != fb.get(k, "<absent>")]
    fields += len(keys)
    pairs += 1
    tag = os.path.basename(f).replace("R0diag-", "").replace(".json", "")
    if diffs:
        bad += 1
        print(f"DIFF {tag}: {len(diffs)} of {len(keys)} fields moved")
        for k in diffs[:12]:
            print(f"    {k}: {fa.get(k,'<absent>')!r} -> {fb.get(k,'<absent>')!r}")
    else:
        print(f"OK   {tag}: {len(keys)} fields compared, 0 moved")
print(f"\n{pairs} paired chunks, {fields} aggregate fields diffed, {bad} chunk(s) with a difference")
# Same guard as samearm.py, same reason (review finding F6): an inertness proof that found no
# chunks to compare has proved nothing, and must not exit 0 saying so.
if pairs == 0:
    print("!! NOTHING COMPARED — no R0diag/R0 pairs found. This is not a pass.")
    sys.exit(2)
sys.exit(1 if bad else 0)
