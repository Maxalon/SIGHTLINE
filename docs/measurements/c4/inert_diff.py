#!/usr/bin/env python3
"""C4 instrument-inertness check (the R0diag the measurement contract asks for before a
round is quoted on a tree that gained instrumentation).

Compares two balance JSONs field by field, EXCLUDING:
  * `harness`  — records nproc/loadavg/elapsed and is DESIGNED to vary (W1's inert_diff.sh
                 strips the same block for the same reason);
  * `byBiome`  — the block this wave ADDED, which by definition exists in only one of them.
Everything else must be identical, key for key, or the added telemetry moved gameplay.

Usage: inert_diff.py <a.json> <b.json>
"""
import json, sys

SKIP_TOP = {"harness", "byBiome"}


def flat(o, p=""):
    if isinstance(o, dict):
        for k, v in o.items():
            if not p and k in SKIP_TOP:
                continue
            yield from flat(v, f"{p}.{k}" if p else k)
    elif isinstance(o, list):
        for i, v in enumerate(o):
            yield from flat(v, f"{p}[{i}]")
    else:
        yield p, o


a = dict(flat(json.load(open(sys.argv[1]))))
b = dict(flat(json.load(open(sys.argv[2]))))
keys = sorted(set(a) | set(b))
diff = [k for k in keys if a.get(k, "<missing>") != b.get(k, "<missing>")]
print(f"fields compared: {len(keys)}   differing: {len(diff)}")
for k in diff[:40]:
    print(f"  {k}: {a.get(k,'<missing>')!r} != {b.get(k,'<missing>')!r}")
print("VERDICT:", "INERT" if not diff else "NOT INERT")
sys.exit(0 if not diff else 1)
