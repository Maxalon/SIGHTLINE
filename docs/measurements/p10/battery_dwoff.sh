#!/usr/bin/env bash
# P10 — THE LANE, MEASURED WHERE THE BRANCH IS NOT STARVED.
# The main round could not resolve the lane because the `overwatch` branch fires 0.15-0.46% of
# enemy acts (204/175/216 lanes per rung per arm). At SIGHTLINE_DECLINEWATCH=1.20 the same tree
# holds 3,200-4,300 lanes per rung per arm — a sample with real power. This arm is
# SIGHTLINE_AILANE=0 at that same ratio, CRN-paired against p10-dw120-*, so the contrast is the
# LANE and nothing else. It is a DIAGNOSTIC, not a shipped configuration.
#   Usage: BIN=runbin/p10b bash docs/measurements/p10/battery_dwoff.sh [jobs]
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
BIN=${BIN:?set BIN to a binary snapshot dir}
JOBS=${1:-3}; N=${N:-20}; i=0
for h in 0 4 8; do
  for b in 0 10 20 30 40 50 60 70; do
    BIN="$BIN" LANE=0 DW="1.20" bash "$HERE/run_chunk.sh" "p10-dw120off-h$h-b$b" "$h" "$b" "$N" &
    i=$((i+1)); [ $((i % JOBS)) -eq 0 ] && wait
  done
done
wait
echo "=== DWOFF BATTERY DONE ==="
