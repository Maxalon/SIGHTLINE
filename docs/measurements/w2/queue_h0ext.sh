#!/usr/bin/env bash
# W2 GATE 3, h0 EXTENSION. Run AFTER the main round and BECAUSE of it: heat 0 showed the largest
# (still non-significant) delta of the five rungs, so it gets four MORE disjoint slot sets —
# bases 40/50/60/70, disjoint from the round's 0-39 and from the 100-700 range other agents hold.
# This is an explicitly post-hoc extension of one rung and is reported as its own line, never
# pooled into the headline table.
#   Usage: BIN=runbin/W2 bash docs/measurements/w2/queue_h0ext.sh
set -u
cd "$(dirname "$0")/../../.." || exit 1
BIN=${BIN:?set BIN to a binary snapshot dir, e.g. BIN=runbin/W2}
for B in 40 50 60 70; do
  for F in 0 1; do
    BIN="$BIN" FIX=$F bash docs/measurements/w2/run_chunk.sh "R3ext-fix$F-h0-b$B" 0 "$B" 10
  done
done
