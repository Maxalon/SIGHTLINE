#!/usr/bin/env bash
# C3 — THE NEW BASELINE. Base 65e34a9 (main after P68), binary snapshot runbin/C3.
# Two arms, 6 rungs (RECRUIT=-1, 0, 2, 4, 6, 8) x 16 CRN slot bases (0..150), N=10 slots per chunk,
# greedy+sloppy => n=320 campaigns per rung per arm.
#   curve : the shipped game  (10 missions, board curve)
#   flat  : SIGHTLINE_BOARDCURVE=0 (10 missions, every mission on 18x11) — prices the curve alone
# Runner of record: docs/measurements/p15/run_chunk.sh. 4 chunks in parallel.
cd "$(dirname "$0")/../../.." || exit 1
export BIN=runbin/C3 OUT=docs/measurements/c3
jobs=()
for arm in curve flat; do
  for h in -1 0 2 4 6 8; do
    for b in $(seq 0 10 150); do
      jobs+=("$arm $h $b")
    done
  done
done
printf '%s\n' "${jobs[@]}" | xargs -P 4 -L 1 bash -c '
  arm=$0; h=$1; b=$2; tag="C3-$arm-h$h-b$b"
  if [ "$arm" = flat ]; then EXTRA="SIGHTLINE_BOARDCURVE=0"; else EXTRA=""; fi
  EXTRA="$EXTRA" bash docs/measurements/p15/run_chunk.sh "$tag" "$h" "$b" 10 2>&1 | grep -E "^(OK|BAD|TIME)"
' > docs/measurements/c3/round.log 2>&1
echo ROUND-DONE >> docs/measurements/c3/round.log
