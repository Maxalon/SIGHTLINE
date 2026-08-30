#!/usr/bin/env bash
# C4 round driver, INSTRUMENTED binary (runbin/C4i — the tree plus Stats' new read-only
# `byBiome` block). Same three rungs, same two arms, same eight disjoint CRN slot sets.
# Its pooled win rates must reproduce the C4-* round EXACTLY; the extra block is what lets the
# rung be split by ROOM (which of the three mechanical biomes bought the move).
set -u
D=$(cd "$(dirname "$0")" && pwd)
export BIN=runbin/C4i
for H in 0 2 4; do
  for ARM in A B; do
    M=1; [ "$ARM" = B ] && M=0
    for B in 0 10 20 30 40 50 60 70; do
      bash "$D/run_chunk.sh" "C4i-$ARM-h$H-b$B" "$H" "$B" "$M" 10
    done
  done
done
