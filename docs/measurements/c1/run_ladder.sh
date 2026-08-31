#!/usr/bin/env bash
# C1 ladder driver — runs one whole ladder (5 rungs x 8 disjoint CRN slot sets) through
# run_chunk.sh, four chunks at a time (the container has 4 cores; the sim is deterministic
# per slot, so parallelism changes wall-time only, never a result).
#   Usage: PREFIX=C1ctl BIN=runbin/C1base EXTRA="" run_ladder.sh [N] [heats...]
# Slots are slotBase+i for i in 0..N-1, so the bases MUST step by N to stay disjoint.
set -u
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
N=${1:-20}
shift || true
HEATS=${*:-"0 2 4 6 8"}
PREFIX=${PREFIX:-C1ctl}
export BIN=${BIN:-runbin/C1base}
export OUT=${OUT:-docs/measurements/c1}
export EXTRA=${EXTRA:-}
BASES=""
for k in 0 1 2 3 4 5 6 7; do BASES="$BASES $((k*N))"; done
jobs_running=0
for H in $HEATS; do
  for B in $BASES; do
    bash "$HERE/run_chunk.sh" "$PREFIX-h$H-b$B" "$H" "$B" "$N" &
    jobs_running=$((jobs_running+1))
    if [ "$jobs_running" -ge 4 ]; then wait -n; jobs_running=$((jobs_running-1)); fi
  done
done
wait
