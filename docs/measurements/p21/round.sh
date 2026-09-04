#!/usr/bin/env bash
# P21 BUILD OWNS THE BOARD — the INERTNESS round.
#
#   Three arms on identical CRN cells (2 rungs x 16 slot sets x 20 campaigns x greedy+sloppy):
#     base      runbin/base108  defaults          <- main @ 108d9ac, the tree P21 branched from
#     p21       runbin/p21      defaults          <- the shipped wave
#     p21stale  runbin/p21      STALEHAZARDS=1    <- the pre-P21 Build seam, same binary
#
#   base vs p21      : does the WHOLE P21 diff move a campaign at its shipped defaults?
#   p21 vs p21stale  : is the added Grid.ClearHazards() in Mission.Build a live-path no-op?
#
# Both are expected EMPTY under docs/measurements/w1/inert_diff.sh with harness{} excluded.
# Usage: round.sh <arm>            (arm = base | p21 | p21stale | forkprices)
set -u
cd "$(dirname "$0")/../../.." || exit 1
ARM=${1:?arm}
case "$ARM" in
  base)       BIN=runbin/base108; EXTRA="" ;;
  p21)        BIN=runbin/p21;     EXTRA="" ;;
  p21stale)   BIN=runbin/p21;     EXTRA="SIGHTLINE_STALEHAZARDS=1" ;;
  forkprices) BIN=runbin/p21;     EXTRA="SIGHTLINE_FORKPRICES=0" ;;
  *) echo "unknown arm $ARM" >&2; exit 2 ;;
esac
BASES=${BASES:-"0 10 20 30 40 50 60 70 80 90 100 110 120 130 140 150"}
HEATS=${HEATS:-"0 4"}
PAR=${PAR:-3}
for H in $HEATS; do
  for B in $BASES; do
    while [ "$(jobs -rp | wc -l)" -ge "$PAR" ]; do wait -n; done
    BIN=$BIN OUT=docs/measurements/p21 EXTRA="$EXTRA" \
      bash docs/measurements/p21/run_chunk.sh "$ARM-h$H-b$B" "$H" "$B" 20 \
      >> "docs/measurements/p21/$ARM-chunks.txt" 2>&1 &
  done
done
wait
echo "== $ARM: $(grep -c '^OK ' docs/measurements/p21/$ARM-chunks.txt) OK / $(grep -c '^BAD ' docs/measurements/p21/$ARM-chunks.txt) BAD"
