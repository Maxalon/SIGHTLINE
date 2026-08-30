#!/usr/bin/env bash
# W2 GATE 3 — the paired round of record (R4: run on the FINAL, twice-review-corrected binary —
# the bleed-out standing gate AND the move-cost-neutral dash guard). R3 was the same round before
# the dash guard; R1 before the standing gate. Both are kept as provenance.
#
# Five heat rungs x FOUR disjoint CRN slot sets (bases 0/10/20/30, N=10 each => slots 0-39,
# greedy+sloppy = 80 campaigns per rung per leg) x the wave's one lever (SIGHTLINE_AIIDLEFIX 0/1).
# The baseline leg is taken FRESH on THIS tree, never against an archived figure — W1 severed the
# gameplay RNG from the rendered frame and re-rolled every archived CRN world.
#
# heat 0 gets THREE MORE families on top (queue_h0.sh) because the review found three independent
# 80-campaign h0 sets reading negative; at n=80 a rung cannot settle a 5-point question.
#   Usage: BIN=runbin/W2r3 bash docs/measurements/w2/queue.sh
set -u
cd "$(dirname "$0")/../../.." || exit 1
BIN=${BIN:?set BIN to a binary snapshot dir, e.g. BIN=runbin/W2r3}
for H in 0 2 4 6 8; do
  for B in 0 10 20 30; do
    for F in 0 1; do
      BIN="$BIN" FIX=$F bash docs/measurements/w2/run_chunk.sh "R4-fix$F-h$H-b$B" "$H" "$B" 10
    done
  done
done
