#!/usr/bin/env bash
# C3 instrument-inertness diagnostic: run the SAME slots on the instrumented binary that R0 ran on
# the pristine one, so every pre-existing JSON key can be diffed. Two rungs x four slot sets.
set -u
ROOT=${ROOT:-/home/user/SIGHTLINE/.claude/worktrees/agent-a2b7f92f5a4d91dfa}
for H in 0 6; do
  for B in 0 10 20 30; do
    BIN=${DIAGBIN:-runbin/C3diag} bash "$ROOT/docs/measurements/c3/run_chunk.sh" "R0diag-h$H-b$B" "$H" "$B" 10
  done
done
