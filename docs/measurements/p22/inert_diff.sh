#!/usr/bin/env bash
# W1 GATE 1 — the INERTNESS diff. Normalises two balance JSONs (sorted keys, stable indent),
# optionally deleting named top-level keys from BOTH, then diffs them. An EMPTY diff means every
# per-slot RunRec, win, loss, turn count and shots-per-kill is identical.
#   Usage: inert_diff.sh <a.json> <b.json> [key-to-delete ...]
set -u
cd "$(dirname "$0")/../../.." || exit 1
A=$1; B=$2; shift 2
DEL="$*"
norm() { python3 -c "
import json,sys
d=json.load(open(sys.argv[1]))
for k in sys.argv[2:]:
    d.pop(k,None)
print(json.dumps(d,sort_keys=True,indent=1))
" "$1" $DEL; }
TMP=$(mktemp -d)
norm "$A" > "$TMP/a.json"
norm "$B" > "$TMP/b.json"
echo "\$ diff <(norm $A) <(norm $B)   [deleted keys: ${DEL:-none}]"
if diff "$TMP/a.json" "$TMP/b.json"; then echo "(empty diff — IDENTICAL)"; RC=0; else RC=1; fi
rm -rf "$TMP"
exit $RC
