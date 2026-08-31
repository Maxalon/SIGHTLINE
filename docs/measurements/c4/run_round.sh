#!/usr/bin/env bash
# C4 round driver — three rungs x two arms x eight disjoint CRN slot sets.
#   arm A = SIGHTLINE_BIOMEMECH=1 (shipped)   arm B = SIGHTLINE_BIOMEMECH=0 (pre-C4 board)
# 8 chunks x runs=20 = 160 campaigns per (rung, arm). Same binary, same slots, one lever.
set -u
D=$(cd "$(dirname "$0")" && pwd)
for H in 0 2 4; do
  for ARM in A B; do
    M=1; [ "$ARM" = B ] && M=0
    for B in 0 10 20 30 40 50 60 70; do
      bash "$D/run_chunk.sh" "C4-$ARM-h$H-b$B" "$H" "$B" "$M" 10
    done
  done
done
