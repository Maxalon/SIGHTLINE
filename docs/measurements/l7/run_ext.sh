#!/usr/bin/env bash
# L7 — THE EXTENSION: the two flat steps, replicated OUT OF SAMPLE on 16 NEW slot sets.
#
#   bash docs/measurements/l7/run_ext.sh
#
# WHY, and why it is not optional stopping. The ladder round (run_ladder.sh) is the wave's
# declared protocol and its table is published at the declared n=320/rung on slot bases 0-150.
# It leaves two steps unresolved that the wave exists to diagnose — rung 5 (h4->h5, +0.6,
# n_disc=76, MDE 7.6) and rung 8 (h7->h8, -2.2, n_disc=35, MDE 5.2) — and a step below its own
# MDE is an absence of evidence, not a zero.
#
# So this arm REPLICATES those steps on 16 slot sets the round has never seen (bases 160-310,
# disjoint from 0-150 by construction: slot = base + i, i in 0..9). It is reported FIRST on the
# new sets alone — an out-of-sample test with its own MDE — and only then pooled to 32 sets.
# That is L5's split-half and L6's P20 re-price, and it is the opposite of extending n until a
# p-value cooperates: the replication can fail, and if it does the round says so.
#
# h6 is included so that h6->h7 (the other apex step) is re-priced on the same new sets, and
# because a step needs BOTH its rungs on the same slot space to be CRN-paired.
set -u
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ROOT=$(cd "$HERE/../../.." && pwd)
export BIN=${BIN:-runbin/l7} OUT=docs/measurements/l7
BASES="160 170 180 190 200 210 220 230 240 250 260 270 280 290 300 310"
RUNGS="4 5 6 7 8"
JOBS=${JOBS:-4}
PREFIX=L7x
export EXTRA=""

mkdir -p "$ROOT/$OUT"
jobs_running=0
for H in $RUNGS; do
  for B in $BASES; do
    ( bash "$HERE/../p15/run_chunk.sh" "$PREFIX-h$H-b$B" "$H" "$B" 10 > "$ROOT/$OUT/$PREFIX-h$H-b$B.chunk.txt" 2>&1 ) &
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
