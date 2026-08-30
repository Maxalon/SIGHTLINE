#!/usr/bin/env bash
# W2 — the heat-0 DEEP ROUND. The review's adjudicator pooled four independent 80-campaign h0 sets
# and got three negatives out of four (-5.0 / +0.0 / -6.2 / -7.5, discordant 30/15 pooled) and
# asked, correctly, whether a 5-point cost at the rung most players are on is being filed as noise
# because no single set can resolve it. So h0 gets THREE MORE slot-set families here, all measured
# by me on the FINAL binary: 40-70, 900-930, 940-970 (disjoint from each other, from the main
# round's 0-39, and from the 100-700 range other agents hold). Together with the main round that is
# SIXTEEN disjoint slot sets = 320 campaigns per leg at heat 0.
#   Usage: BIN=runbin/W2r3 bash docs/measurements/w2/queue_h0.sh
set -u
cd "$(dirname "$0")/../../.." || exit 1
BIN=${BIN:?set BIN to a binary snapshot dir, e.g. BIN=runbin/W2r3}
for B in 40 50 60 70 900 910 920 930 940 950 960 970; do
  for F in 0 1; do
    BIN="$BIN" FIX=$F bash docs/measurements/w2/run_chunk.sh "R4h0-fix$F-h0-b$B" 0 "$B" 10
  done
done
