#!/usr/bin/env bash
# L7 — EVERY RUNG (PROGRAM PARALLAX, wave "L7 EVERY RUNG").
#
#   bash docs/measurements/l7/run_ladder.sh ladder   # 11 rungs x 16 disjoint CRN slot bases x N=10
#                                                    # -> 176 chunks, 3,520 campaigns, heat PINNED
#
# A verbatim copy of docs/measurements/l6/run_ladder.sh with ONE change: the rung list is
# `R 0 1 2 3 4 5 6 7 8` instead of `R 0 2 4 6 8`. Same runner of record
# (docs/measurements/p15/run_chunk.sh), same 16 slot bases, same N=10 (greedy+sloppy = 20
# campaigns per chunk, 320 per rung), same pin. The five rungs L6 also sampled are therefore a
# straight reproduction check and the five new ones are the point of the wave.
#
# The bridge arm L6 carried is NOT reproduced here: L7's base is one milestone later
# (935d719 vs 6a6ebee) and the only wave between them, P21, is claimed live-path inert. That
# claim is checked by the reproduction of L6's five rungs, which is a stronger test than a
# bridge arm would be (it is 1,600 CRN campaigns against an archived tree).
#
# BIN must be a SNAPSHOT (runbin/l7) so the tree can keep building while a round is in flight.
set -u
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ROOT=$(cd "$HERE/../../.." && pwd)
MODE=${1:-ladder}
export BIN=${BIN:-runbin/l7} OUT=docs/measurements/l7
BASES="0 10 20 30 40 50 60 70 80 90 100 110 120 130 140 150"
RUNGS="R 0 1 2 3 4 5 6 7 8"
JOBS=${JOBS:-4}

case "$MODE" in
  ladder) PREFIX=L7; export EXTRA="";;
  *) echo "usage: run_ladder.sh ladder"; exit 2;;
esac

mkdir -p "$ROOT/$OUT"
jobs_running=0
for H in $RUNGS; do
  hh=$H; [ "$H" = "R" ] && hh=-1
  for B in $BASES; do
    ( bash "$HERE/../p15/run_chunk.sh" "$PREFIX-h$H-b$B" "$hh" "$B" 10 > "$ROOT/$OUT/$PREFIX-h$H-b$B.chunk.txt" 2>&1 ) &
    jobs_running=$((jobs_running+1))
    if [ "$jobs_running" -ge "$JOBS" ]; then wait -n; jobs_running=$((jobs_running-1)); fi
  done
done
wait
cat "$ROOT/$OUT/$PREFIX"-h*-b*.chunk.txt | sort > "$ROOT/$OUT/$PREFIX-chunks.txt"
rm -f "$ROOT/$OUT/$PREFIX"-h*-b*.chunk.txt
ok=$(grep -c '^OK' "$ROOT/$OUT/$PREFIX-chunks.txt"); bad=$(grep -c '^BAD' "$ROOT/$OUT/$PREFIX-chunks.txt")
want=$(( $(echo $BASES | wc -w) * $(echo $RUNGS | wc -w) ))
echo "CHUNKS ($PREFIX): ok=$ok bad=$bad (expect ok=$want bad=0)"
exit $(( bad != 0 || ok != want ))
