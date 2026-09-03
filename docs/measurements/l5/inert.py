#!/usr/bin/env python3
"""L5 — the INERTNESS diff for the instrument commit, key-exact.

w1/inert_diff.sh answers "are these two JSONs identical once `harness{}` is dropped?". This wave
ADDED keys to the artifact, so that answer is "no" by construction and the honest question is the
narrower one: is the diff EXACTLY the new keys and nothing else? This script names the keys the
instrument commit added, removes ONLY those from both sides, folds the renamed STALEMATE arms
back onto the old word (the base tree logged one word; the pin tree logs which arm), and diffs
what is left. It prints every key it removed and every path that still differs; an empty
"REMAINING DIFF" is the inertness claim.

Usage: inert.py <base.json> <new.json>
"""
import json, sys

NEW_TOP = ["heatLeak", "campaigns"]                       # new top-level blocks
NEW_IH = ["stalemateMissionLosses", "stalemateRunLosses", "stalemates"]   # new instrumentHealth fields
VARIES = ["harness"]                                        # designed to vary (nproc/load/elapsed)


def fold_stalemate(d):
    """STALEMATE-MISSION / STALEMATE-RUN -> STALEMATE wherever a loss cause is a dict key."""
    lc = d.get("lossCauses")
    if isinstance(lc, dict):
        out = {}
        for k, v in lc.items():
            k2 = "STALEMATE" if k.startswith("STALEMATE") else k
            out[k2] = out.get(k2, 0) + v
        d["lossCauses"] = out


def strip(d, side):
    removed = []
    for k in VARIES + NEW_TOP:
        if k in d:
            d.pop(k); removed.append(k)
    ih = d.get("instrumentHealth", {})
    for k in NEW_IH:
        if k in ih:
            ih.pop(k); removed.append("instrumentHealth." + k)
    fold_stalemate(d)
    return removed


def walk(a, b, path, out):
    if type(a) != type(b):
        out.append((path, a, b)); return
    if isinstance(a, dict):
        for k in sorted(set(a) | set(b)):
            if k not in a or k not in b:
                out.append((path + "." + k, a.get(k, "<absent>"), b.get(k, "<absent>")))
            else:
                walk(a[k], b[k], path + "." + k, out)
    elif isinstance(a, list):
        if len(a) != len(b):
            out.append((path + ".len", len(a), len(b))); return
        for i, (x, y) in enumerate(zip(a, b)):
            walk(x, y, f"{path}[{i}]", out)
    elif a != b:
        out.append((path, a, b))


def main(pa, pb):
    a = json.load(open(pa)); b = json.load(open(pb))
    ra = strip(a, "base"); rb = strip(b, "new")
    print(f"removed from BASE ({pa}): {ra}")
    print(f"removed from NEW  ({pb}): {rb}")
    only_new = sorted(set(rb) - set(ra))
    print(f"keys that exist ONLY in the new artifact: {only_new}")
    diffs = []
    walk(a, b, "$", diffs)
    print(f"REMAINING DIFF: {len(diffs)} path(s)")
    for p, x, y in diffs[:60]:
        print(f"  {p}: {x!r} -> {y!r}")
    return 0 if not diffs else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
