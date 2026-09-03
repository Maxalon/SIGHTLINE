#!/usr/bin/env bash
# P10 — the PRICED-AND-NOT-SPENT probe on Ai.DeclineWatchRatio (shipped 0.45).
# ROADMAP's standing claim: a real lane "would justify a much higher DeclineWatchRatio". The main
# round measured the lane and found it inert on win rate BECAUSE the branch it improves fires
# 0.15-0.46% of enemy acts. This probe asks whether the ratio is the lever that feeds it, on the
# SAME 24 CRN chunks as the `p10-on-*` arm (which is therefore the baseline — R0diag.json proves
# the binary carrying the new dial reproduces that arm byte-for-byte at the default).
#   Usage: BIN=runbin/p10b bash docs/measurements/p10/battery_dw.sh [jobs]
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
BIN=${BIN:?set BIN to a binary snapshot dir, e.g. BIN=runbin/p10b}
JOBS=${1:-3}
N=${N:-20}
i=0
for dw in 1.20 3.00; do
  tagdw=$(echo "$dw" | tr -d '.')
  for h in 0 4 8; do
    for b in 0 10 20 30 40 50 60 70; do
      BIN="$BIN" LANE=1 DW="$dw" bash "$HERE/run_chunk.sh" "p10-dw$tagdw-h$h-b$b" "$h" "$b" "$N" &
      i=$((i+1)); [ $((i % JOBS)) -eq 0 ] && wait
    done
  done
done
wait
echo "=== DW BATTERY DONE ==="
