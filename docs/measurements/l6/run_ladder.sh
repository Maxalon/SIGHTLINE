#!/usr/bin/env bash
# L6 — THE LADDER OF RECORD (PROGRAM PARALLAX, wave "THE LADDER OF RECORD").
#
#   bash docs/measurements/l6/run_ladder.sh ladder   # 6 rungs x 16 disjoint CRN slot bases x N=10 -> 96 chunks, heat PINNED
#   bash docs/measurements/l6/run_ladder.sh bridge   # the SAME 96 cells with every post-L5 gameplay lever RESTORED
#   bash docs/measurements/l6/run_ladder.sh attr <FLAG=VAL...>   # one-lever attribution at h0/h4 (see run_attr.sh)
#
# Every chunk goes through docs/measurements/p15/run_chunk.sh — THE RUNNER OF RECORD, which keeps
# all three layers of CLAUDE.md's measurement contract (rm -f the target first / the process EXIT
# CODE / the artifact's own `batch.expectedRuns` + rung + base + pin, via p15/check_chunk.py).
# c1/run_chunk.sh (which drove L4 and L5) is SUPERSEDED and is NOT used here.
#
# RECRUIT is heat -1 to the batch and "hR" in a file name, exactly as l4/l5 encode it.
# BIN must be a SNAPSHOT (runbin/l6) so the tree can keep building while a round is in flight.
set -u
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ROOT=$(cd "$HERE/../../.." && pwd)
MODE=${1:-ladder}
export BIN=${BIN:-runbin/l6} OUT=docs/measurements/l6
BASES="0 10 20 30 40 50 60 70 80 90 100 110 120 130 140 150"

# THE BRIDGE ARM — every gameplay lever that landed between L5's base commit 7180374 and this
# tree's 6a6ebee, set back to its pre-wave behaviour. Derived, not remembered: the env-var surface
# of `src/` at 7180374 was diffed against this tree's, and every NEW var that is a gameplay switch
# (rather than a test hook, a screenshot hook or a harness pin) is here.
#   SIGHTLINE_AILANE=0      P10 THE HELD LANE       — the pre-P10 enemy overwatch (no cone)
#   SIGHTLINE_NEWGROUND=0   P16 GROUND TRUTH        — VOID/ARID are paint again
#   SIGHTLINE_SECONDAXIS=0  P18 THE SECOND AXIS     — the pre-P18 WAR ROOM
#   SIGHTLINE_PERKPICK=0    P18                     — the random bonus-perk recipient
#   SIGHTLINE_ASSISTLATCH=0 P18                     — the live-heat assist
#   SIGHTLINE_ELITEBOSS=0   P19 THE ROSTER CONTESTS — the mission-number mid-boss
#   SIGHTLINE_ROSTERID=0    P19                     — the SMG monoculture
#   SIGHTLINE_STALEGROUND=1 P20 THE STALE GROUND    — Build reads the PREVIOUS mission's ground
# Deliberately NOT in the set, with the reason:
#   SIGHTLINE_MODEDEPTH=0   P14 — `Mission.ModeDepth` is read only under `Mode == GameMode.Skirmish`
#                                 (src/Game.cs), and a SIGHTLINE_BALANCE batch is campaign mode.
#   SIGHTLINE_BIOMEDEAL     P16 — DEFAULT OFF; on it is the pre-P16 deal already.
#   SIGHTLINE_MISSIONFLUSH=0 P15 — a `Stats` bookkeeping arm (which OPEN missions are recorded),
#                                 not a gameplay one. It moves per-mission ROWS, never an outcome.
#   SIGHTLINE_DECLINEWATCH  P10 — priced and unspent; its default IS the shipped ratio.
RESTORE="SIGHTLINE_AILANE=0 SIGHTLINE_NEWGROUND=0 SIGHTLINE_SECONDAXIS=0 SIGHTLINE_PERKPICK=0 \
SIGHTLINE_ASSISTLATCH=0 SIGHTLINE_ELITEBOSS=0 SIGHTLINE_ROSTERID=0 SIGHTLINE_STALEGROUND=1"

case "$MODE" in
  ladder) PREFIX=L6;       export EXTRA="";;
  bridge) PREFIX=L6bridge; export EXTRA="$RESTORE";;
  *) echo "usage: run_ladder.sh ladder|bridge"; exit 2;;
esac

mkdir -p "$ROOT/$OUT"
jobs_running=0
for H in R 0 2 4 6 8; do
  hh=$H; [ "$H" = "R" ] && hh=-1
  for B in $BASES; do
    ( bash "$HERE/../p15/run_chunk.sh" "$PREFIX-h$H-b$B" "$hh" "$B" 10 > "$ROOT/$OUT/$PREFIX-h$H-b$B.chunk.txt" 2>&1 ) &
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
