#!/usr/bin/env bash
# L7 — THE CLAMP ARM. Is rung 8's flat step the 12-body headcount clamp?
#
#   bash docs/measurements/l7/run_cap.sh
#
# `Mission.Build` sizes a force as `Math.Clamp(EnemyBaseCount + n + enemyDelta, 3, 12)`.
# With the shipped base of 4 that ceiling BINDS at the top of the ladder: at mission 6,
# heat 7 asks for 4+6+3 = 13 and heat 8 asks for 4+6+4 = 14, and BOTH are cut to 12 — so
# NO QUARTER's whole body tooth is arithmetic that never reaches the mission a campaign is
# decided on. `SIGHTLINE_ENEMYBASE=2` (X2's shipped measurement dial, Mission.EnemyBaseCount)
# lowers the base by two, which puts m5 and m6 back UNDER the ceiling — heat 7 asks 11 and
# heat 8 asks 12 at m6 — so rung 8's body lands where it could not before.
#
# It is a MECHANISM test, not a level test. Base 2 is a different (easier) game, so the two
# arms' win rates are not comparable; what is comparable is the SHAPE — what rung 8 buys with
# the ceiling binding versus with it clear, on the same 16 CRN slot sets.
#
# The finale's OTHER strip is deliberately NOT restored by this dial and stays as evidence:
# `bump = Math.Max(0, n - 1)` discards the heat StatDelta on mission 6 outright, so rung 8's
# stat point cannot reach the finale in either arm.
set -u
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ROOT=$(cd "$HERE/../../.." && pwd)
export BIN=${BIN:-runbin/l7} OUT=docs/measurements/l7
BASES="${BASES:-0 10 20 30 40 50 60 70 80 90 100 110 120 130 140 150}"
RUNGS="6 7 8"
JOBS=${JOBS:-4}
PREFIX=L7cap
export EXTRA="SIGHTLINE_ENEMYBASE=2"

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
