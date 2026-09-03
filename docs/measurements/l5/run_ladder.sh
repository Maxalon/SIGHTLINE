#!/usr/bin/env bash
# L5 — THE PINNED LADDER (wave "THE HEAT PIN AND L5").
#   bash docs/measurements/l5/run_ladder.sh ladder   # 6 rungs x 16 disjoint CRN slot bases x N=10 -> 96 chunks, heat PINNED
#   bash docs/measurements/l5/run_ladder.sh bridge   # 6 rungs x L4's 8 bases, SIGHTLINE_HEATPIN=0 on the SAME binary -> 48 chunks
# Every chunk goes through c1/run_chunk.sh (rm -f the target first / exit-code 2 = no display /
# assert the JSON's own `runs`). Four chunks at a time (4 cores); the sim is deterministic per slot,
# so parallelism moves wall-clock only. RECRUIT is heat -1 to the batch and "hR" in the file name,
# exactly as l4/run_ladder.sh encodes it. BIN must be a SNAPSHOT (runbin/L5) so the tree can keep
# building while a round is in flight.
set -u
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
MODE=${1:-ladder}
export BIN=${BIN:-runbin/L5} OUT=docs/measurements/l5
case "$MODE" in
  ladder) PREFIX=L5;       BASES="0 10 20 30 40 50 60 70 80 90 100 110 120 130 140 150"; export EXTRA="";;
  bridge) PREFIX=L5bridge; BASES="0 10 20 30 40 50 60 70";                                 export EXTRA="SIGHTLINE_HEATPIN=0";;
  *) echo "usage: run_ladder.sh ladder|bridge"; exit 2;;
esac
ROOT=$(cd "$HERE/../../.." && pwd)
mkdir -p "$ROOT/$OUT"
jobs_running=0
for H in R 0 2 4 6 8; do
  hh=$H; [ "$H" = "R" ] && hh=-1
  for B in $BASES; do
    ( bash "$HERE/../c1/run_chunk.sh" "$PREFIX-h$H-b$B" "$hh" "$B" 10 > "$ROOT/$OUT/$PREFIX-h$H-b$B.chunk.txt" ) &
    jobs_running=$((jobs_running+1))
    if [ "$jobs_running" -ge 4 ]; then wait -n; jobs_running=$((jobs_running-1)); fi
  done
done
wait
cat "$ROOT/$OUT/$PREFIX"-h*-b*.chunk.txt | sort > "$ROOT/$OUT/$PREFIX-chunks.txt"
rm -f "$ROOT/$OUT/$PREFIX"-h*-b*.chunk.txt
ok=$(grep -c '^OK' "$ROOT/$OUT/$PREFIX-chunks.txt"); bad=$(grep -c '^BAD' "$ROOT/$OUT/$PREFIX-chunks.txt")
want=$(( $(echo $BASES | wc -w) * 6 ))
echo "CHUNKS ($PREFIX): ok=$ok bad=$bad (expect ok=$want bad=0)"
exit $(( bad != 0 || ok != want ))
