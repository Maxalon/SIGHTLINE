#!/usr/bin/env bash
# C1 — extend one rung with EIGHT MORE disjoint CRN slot sets (bases 160..300 at N=20),
# taking that rung from n=320 to n=640. Used to sharpen the shipping decision at heat 6.
#   Usage: PREFIX=C1m1 BIN=runbin/C1lev EXTRA="SIGHTLINE_MIDTOOTH=1" run_extra.sh <heat> [N]
set -u
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
H=$1; N=${2:-20}
PREFIX=${PREFIX:-C1ctl}
export BIN=${BIN:-runbin/C1base}
export OUT=${OUT:-docs/measurements/c1}
export EXTRA=${EXTRA:-}
jr=0
for k in 8 9 10 11 12 13 14 15; do
  B=$((k*N))
  bash "$HERE/run_chunk.sh" "$PREFIX-h$H-b$B" "$H" "$B" "$N" &
  jr=$((jr+1)); if [ "$jr" -ge 4 ]; then wait -n; jr=$((jr-1)); fi
done
wait
