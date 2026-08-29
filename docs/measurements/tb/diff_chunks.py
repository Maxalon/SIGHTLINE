#!/usr/bin/env python3
"""TRUE BAND inertness check.

Compares two SIGHTLINE_BALANCE aggregate JSONs that differ ONLY in the choice-band rule
(SIGHTLINE_CHOICEBAND) on the SAME slot base.  CountMeaningfulChoices is read-only
bookkeeping, so EVERY field except the four decision-richness fields must be byte-identical.
Usage: diff_chunks.py <a.json> <b.json>
Exit 0 = inert (only the whitelisted choice fields moved).
"""
import json, sys

CHOICE = {
    "meaningfulChoicesPerTurn",
    "choicesPerArmedSoldierTurn",
    "targetChoicesPerArmedSoldierTurn",
    "positionChoicesPerArmedSoldierTurn",
    "choicesPerTurn",
    "choicesPerArmed",
}


def walk(node, path=""):
    """Flatten a JSON tree to {dotted-path: scalar}."""
    if isinstance(node, dict):
        for k, v in node.items():
            yield from walk(v, f"{path}.{k}" if path else k)
    elif isinstance(node, list):
        for i, v in enumerate(node):
            yield from walk(v, f"{path}[{i}]")
    else:
        yield path, node


a = dict(walk(json.load(open(sys.argv[1]))))
b = dict(walk(json.load(open(sys.argv[2]))))

keys = sorted(set(a) | set(b))
moved_choice, moved_other = [], []
for k in keys:
    va, vb = a.get(k, "<absent>"), b.get(k, "<absent>")
    if va == vb:
        continue
    leaf = k.split(".")[-1].split("[")[0]
    (moved_choice if leaf in CHOICE else moved_other).append((k, va, vb))

print(f"fields compared : {len(keys)}")
print(f"choice fields moved : {len(moved_choice)}")
for k, va, vb in moved_choice:
    print(f"   {k}: {va} -> {vb}")
print(f"NON-choice fields moved : {len(moved_other)}")
for k, va, vb in moved_other[:40]:
    print(f"   {k}: {va} -> {vb}")
print("VERDICT:", "INERT" if not moved_other else "SIDE EFFECT PRESENT")
sys.exit(0 if not moved_other else 1)
