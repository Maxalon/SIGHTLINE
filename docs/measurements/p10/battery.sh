#!/usr/bin/env bash
# P10 THE HELD LANE — the wave's CRN round.
#   Two arms on IDENTICAL worlds: SIGHTLINE_AILANE=1 (shipped: an ordinary enemy overwatch picks a
#   90-degree lane) vs =0 (the pre-P10 360-degree watch, an exact restoration).
#   3 rungs {h0,h4,h8} x 8 CRN slot bases {0,10,...,70} x SIGHTLINE_BALANCE=20 (= 40 campaigns per
#   chunk: 20 slots x greedy+sloppy) => n=320 per rung per arm, 1,920 campaigns.
# Every chunk goes through run_chunk.sh, which does the three-layer completion check the CLAUDE.md
# measurement contract requires: rm -f the target first, check the process EXIT CODE (2 = no
# display / nothing written), and assert the JSON's own `runs` field.
#   Usage: BIN=runbin/p10 bash docs/measurements/p10/battery.sh [jobs]
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
BIN=${BIN:?set BIN to a binary snapshot dir, e.g. BIN=runbin/p10}
JOBS=${1:-2}
N=${N:-20}
run() { BIN="$BIN" LANE="$2" bash "$HERE/run_chunk.sh" "$1" "$3" "$4" "$N"; }
i=0
for arm in on off; do
  lane=1; [ "$arm" = off ] && lane=0
  for h in 0 4 8; do
    for b in 0 10 20 30 40 50 60 70; do
      run "p10-$arm-h$h-b$b" "$lane" "$h" "$b" &
      i=$((i+1))
      [ $((i % JOBS)) -eq 0 ] && wait
    done
  done
done
wait
echo "=== BATTERY DONE ==="
