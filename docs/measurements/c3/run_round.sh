#!/usr/bin/env bash
# C3 round driver — one full ladder round: 6 rungs (hR,h0,h2,h4,h6,h8) x 8 disjoint CRN slot sets
# (SIGHTLINE_BALANCE_BASE 0..70) x greedy+sloppy = 160 campaigns/rung, 960 total.
# Usage: run_round.sh <round-tag> [N]      ; env: EXTRA (extra env passed to the binary), BIN
set -u
ROOT=${ROOT:-/home/user/SIGHTLINE/.claude/worktrees/agent-a2b7f92f5a4d91dfa}
R=$1; N=${2:-10}
for H in -1 0 2 4 6 8; do
  HL=$H; [ "$H" = "-1" ] && HL=R
  for B in 0 10 20 30 40 50 60 70; do
    bash "$ROOT/docs/measurements/c3/run_chunk.sh" "$R-h$HL-b$B" "$H" "$B" "$N"
  done
done
