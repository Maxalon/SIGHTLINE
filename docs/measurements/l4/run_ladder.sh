#!/usr/bin/env bash
# L4 — the COMPOSED-TREE ladder of record for PROGRAM CONTOUR.
# Replicates L3's methodology EXACTLY (6 rungs x 8 slot bases x N=10 -> runs=20, 160/rung)
# so L3 -> L4 is like-for-like on the same CRN slot space. Every chunk goes through
# c1/run_chunk.sh, which carries all three layers of W1's completion contract.
set -u
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
export BIN=runbin/L4 OUT=docs/measurements/l4
bad=0; ok=0
for H in R 0 2 4 6 8; do
  hh=$H; [ "$H" = "R" ] && hh=-1
  for B in 0 10 20 30 40 50 60 70; do
    r=$(bash "$HERE/../c1/run_chunk.sh" "L4-h$H-b$B" "$hh" "$B" 10)
    echo "$r"
    case "$r" in OK*) ok=$((ok+1));; *) bad=$((bad+1));; esac
  done
done
echo "CHUNKS: ok=$ok bad=$bad (expect ok=48 bad=0)"
exit $(( bad != 0 ))
