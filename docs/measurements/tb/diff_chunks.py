#!/usr/bin/env python3
"""TRUE BAND inertness check.

Compares two SIGHTLINE_BALANCE aggregate JSONs that differ ONLY in the choice-band rule
(SIGHTLINE_CHOICEBAND) on the SAME slot base.  CountMeaningfulChoices is read-only
bookkeeping, so EVERY field except the four decision-richness fields must be byte-identical.
Usage: diff_chunks.py <a.json> <b.json> [--cross]
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


ja, jb = json.load(open(sys.argv[1])), json.load(open(sys.argv[2]))

# TRUE BAND: `instrument` names the decision-density instrument the batch was measured on.
# A diff ACROSS instruments is the thing this wave exists to prevent being done by accident,
# so it is allowed only when explicitly asked for (--cross), and it is then reported loudly.
ia, ib = ja.get("instrument", "unknown"), jb.get("instrument", "unknown")
cross = "--cross" in sys.argv
if ia != ib and not cross:
    print(f"REFUSING: instrument mismatch ({ia} vs {ib}). The `choices*` fields are not "
          f"comparable across instruments. Re-run with --cross if that is deliberate.")
    sys.exit(2)
if ia != ib:
    print(f"!! CROSS-INSTRUMENT DIFF ({ia} vs {ib}) — every `choices*` delta below is an "
          f"instrument artefact, not a game effect.")

a = dict(walk(ja))
b = dict(walk(jb))
a.pop("instrument", None); b.pop("instrument", None)

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
