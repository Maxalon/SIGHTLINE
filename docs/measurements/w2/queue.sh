#!/usr/bin/env bash
# W2 GATE 3 — the paired round. Five heat rungs x FOUR disjoint CRN slot sets (bases 0/10/20/30,
# N=10 each => slots 0-39, greedy+sloppy = 80 campaigns per rung per leg) x the wave's one lever
# (SIGHTLINE_AIIDLEFIX 0 vs 1). The baseline leg is taken FRESH on THIS tree, never against an
# archived figure — W1 severed the gameplay RNG from the rendered frame and re-rolled every
# archived CRN world.
#   Usage: BIN=runbin/W2 bash docs/measurements/w2/queue.sh
set -u
cd "$(dirname "$0")/../../.." || exit 1
BIN=${BIN:?set BIN to a binary snapshot dir, e.g. BIN=runbin/W2}
for H in 0 2 4 6 8; do
  for B in 0 10 20 30; do
    for F in 0 1; do
      BIN="$BIN" FIX=$F bash docs/measurements/w2/run_chunk.sh "R1-fix$F-h$H-b$B" "$H" "$B" 10
    done
  done
done
