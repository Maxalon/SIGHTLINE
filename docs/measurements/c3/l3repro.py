#!/usr/bin/env python3
"""C3 — THE BASELINE-IS-THE-ARCHIVE PROOF.

C3's baseline arm B1 (`SIGHTLINE_KILLTREADMILL=1`, i.e. the lever's OFF path) must be the tree the
ladder of record was measured on. This checks it against the COMMITTED L3 archive rather than
against a round of C3's own that could be deleted — the review found exactly that hole: `trim.sh`
removed the D0 round, so the documented `samearm.py D0 B1` command printed a PASS over zero chunks.

Two independent levels, both on data that is in the repository:

  1. AGGREGATE — every pre-existing field of all 48 chunk pairs (dropping `harness`, which records
     nproc/loadavg/elapsed and is designed to vary; `instrument`, which echoes the instrument's own
     configuration; and `encounterMidrun`, which C3 added and L3 cannot have).
  2. CAMPAIGN-FOR-CAMPAIGN — the PER-SLOT RECORDS block each chunk report prints, keyed on
     (rung, slot set, slot, policy), compared on BOTH the outcome and the missions-cleared count.
     960 campaigns, not 48 summaries.

Exits non-zero on any difference AND on zero pairs compared — a proof command that finds nothing
must never print a pass (CLAUDE.md's W1 contract, and the defect this script replaces).
"""
import json, glob, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
L3 = os.path.join(HERE, "..", "l3")
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


bad = pairs = fields = 0
per = []
for fb in sorted(glob.glob(os.path.join(HERE, "B1-h*-b*.json"))):
    fl = os.path.join(L3, os.path.basename(fb).replace("B1-", "L3-", 1))
    if not os.path.exists(fl):
        print("MISSING L3 chunk for", os.path.basename(fb))
        bad += 1
        continue
    a = {k: v for k, v in json.load(open(fl)).items() if k not in DROP}
    b = {k: v for k, v in json.load(open(fb)).items() if k not in DROP}
    fa, fbb = flat(a), flat(b)
    keys = set(fa) | set(fbb)
    diffs = [k for k in sorted(keys) if fa.get(k, "<absent>") != fbb.get(k, "<absent>")]
    pairs += 1
    fields += len(keys)
    per.append(len(keys))
    if diffs:
        bad += 1
        print(f"DIFF {os.path.basename(fb)}: {len(diffs)} of {len(keys)} fields")
        for k in diffs[:8]:
            print(f"    {k}: {fa.get(k,'<absent>')!r} -> {fbb.get(k,'<absent>')!r}")

LINE = re.compile(r"slot\s+(\d+)\s+@h(-?\d+):\s+([WL])\s+(\d+)\s+\|\s+([WL])\s+(\d+)")


def campaigns(d, tag):
    out = {}
    for f in sorted(glob.glob(os.path.join(d, tag + "-h*-b*.report.txt"))):
        nm = os.path.basename(f)[len(tag) + 1:].replace(".report.txt", "")
        rung, base = nm.split("-")
        for m in LINE.finditer(open(f).read()):
            out[(rung, base, m.group(1), "g")] = (m.group(3), m.group(4))
            out[(rung, base, m.group(1), "s")] = (m.group(5), m.group(6))
    return out


A, B = campaigns(L3, "L3"), campaigns(HERE, "B1")
shared = sorted(set(A) & set(B))
same = sum(1 for k in shared if A[k] == B[k])
if len(shared) != len(A) or len(shared) != len(B):
    print(f"!! campaign key mismatch: L3={len(A)} B1={len(B)} shared={len(shared)}")
    bad += 1
if same != len(shared):
    bad += 1
    for k in shared:
        if A[k] != B[k]:
            print(f"    campaign {k}: L3 {A[k]} -> B1 {B[k]}")

print(f"AGGREGATE : {pairs} chunk pairs, {fields} fields "
      f"({min(per) if per else 0}-{max(per) if per else 0} per chunk), differences in {bad} chunk(s)")
print(f"CAMPAIGNS : {same}/{len(shared)} identical on (outcome, missionsCleared)")

if pairs == 0 or len(shared) == 0:
    print("!! NOTHING COMPARED — a proof over zero data is not a proof.")
    sys.exit(2)
print("PASS — C3's baseline arm IS the L3 tree" if bad == 0 else "FAIL")
sys.exit(1 if bad else 0)
