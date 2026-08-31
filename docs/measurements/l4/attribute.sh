#!/usr/bin/env bash
# L4 ATTRIBUTION — which of the four composed levers drives the top-of-ladder collapse?
# One arm per lever, each turning exactly ONE lever OFF on the composed tree, same 8 CRN slot
# sets as L4 itself. This is MEASUREMENT, not a fix: it tells the next wave which dial to re-tune.
set -u
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
export BIN=runbin/L4 OUT=docs/measurements/l4
H=${1:-6}
declare -A ARMS=(
  [c1off]="SIGHTLINE_MIDTOOTH=0"       # C1's mid-ladder tooth back to the pre-C1 table
  [c2off]="SIGHTLINE_AIDECLINE=0"      # pre-C2 opponent (flat constant, no decline gate)
  [c3off]="SIGHTLINE_KILLTREADMILL=1"  # restore the clock's reinforcement arm on Eliminate
  [c4off]="SIGHTLINE_BIOMEMECH=0"      # pre-C4 board
)
for arm in c1off c2off c3off c4off; do
  for B in 0 10 20 30 40 50 60 70; do
    EXTRA="${ARMS[$arm]}" bash "$HERE/../c1/run_chunk.sh" "L4att-$arm-h$H-b$B" "$H" "$B" 10
  done
done
