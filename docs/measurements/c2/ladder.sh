#!/usr/bin/env bash
# C2 LADDER — the CRN-paired round. One binary, two arms, distinguished only by
# SIGHTLINE_AIDECLINE, so the pairing is as tight as this project can make it: identical
# executable, identical slot seeds, one environment variable.
#   6 rungs (RECRUIT=-1, 0, 2, 4, 6, 8) x 4 disjoint slot sets (BASE 0/10/20/30)
#   x 2 arms x N=10 (greedy+sloppy = 20 runs per chunk) = 80 campaigns per rung per arm.
# A rung is FOUR slot sets or it is not a rung (CLAUDE.md); n=80 carries roughly +/-5 points.
set -u
ROOT=/home/user/SIGHTLINE/.claude/worktrees/agent-a1c26e3c14d98312b
cd "$ROOT" || exit 1
for H in -1 0 2 4 6 8; do
  RUNG=$([ "$H" = "-1" ] && echo R || echo "$H")
  for B in 0 10 20 30; do
    for ARM in 0 1; do
      NAME=$([ "$ARM" = 0 ] && echo BASE || echo DECL)
      BIN=runbin/C2L AIDECLINE=$ARM bash docs/measurements/c2/run_chunk.sh "L-$NAME-h$RUNG-b$B" "$H" "$B" 10
    done
  done
done
