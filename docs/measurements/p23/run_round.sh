#!/usr/bin/env bash
# P23 "THE APEX BITES" (PROGRAM PARALLAX) — THE ROUND.
#
#   bash docs/measurements/p23/run_round.sh          # 4 arms x 4 rungs x 16 CRN slot bases x N=10
#                                                    # -> 256 chunks, 5,120 campaigns, heat PINNED
#
# WHY FOUR ARMS AND NOT TWO. L7 located the defect and priced a PARTIAL relief with a single
# combined arm (SIGHTLINE_ENEMYBASE=2), which could ease the body ceiling but could not touch the
# finale's stat strip — and its +4.1 did not resolve on the odds scale (z = -1.66) partly because
# the two halves were confounded inside one arm. P23 ships the two halves as two dials precisely
# so this round can put each on its own arm:
#
#   BASE   SIGHTLINE_CLAMPLAST=0 SIGHTLINE_FINALESTAT=0   the pre-P23 tree (the control)
#   A      SIGHTLINE_FINALESTAT=0                          lever A alone — the ordering fix
#   B      SIGHTLINE_CLAMPLAST=0                           lever B alone — the finale stat
#   AB     (nothing)                                       the shipped tree
#
# All four arms share the 16 slot bases, so every contrast is CRN-paired and a chunk of one arm is
# comparable campaign-for-campaign with the same chunk of another.
#
# RUNGS. h0 is an INERTNESS control: lever B cannot fire below heat 2 and lever A cannot fire on a
# plain route below heat 5, so a non-zero h0 contrast is a bug, not a result. h4/h6/h8 are where
# both levers act (FORCETEST's dump matrix is the derivation).
#
# Runner of record: docs/measurements/p15/run_chunk.sh — rm -f, exit code, then the artifact's own
# accounting via check_chunk.py. BIN must be a SNAPSHOT so the tree can keep building.
set -u
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ROOT=$(cd "$HERE/../../.." && pwd)
export BIN=${BIN:-runbin/p23} OUT=docs/measurements/p23
BASES=${BASES:-"0 10 20 30 40 50 60 70 80 90 100 110 120 130 140 150"}
RUNGS=${RUNGS:-"0 4 6 8"}
ARMS=${ARMS:-"base A B AB"}
JOBS=${JOBS:-4}
N=${N:-10}

mkdir -p "$ROOT/$OUT"
jobs_running=0
for ARM in $ARMS; do
  case "$ARM" in
    base) E="SIGHTLINE_CLAMPLAST=0 SIGHTLINE_FINALESTAT=0" ;;
    A)    E="SIGHTLINE_FINALESTAT=0" ;;
    B)    E="SIGHTLINE_CLAMPLAST=0" ;;
    AB)   E="" ;;
    *) echo "unknown arm $ARM" >&2; exit 2 ;;
  esac
  for H in $RUNGS; do
    for B in $BASES; do
      ( EXTRA="$E" bash "$HERE/../p15/run_chunk.sh" "P23-$ARM-h$H-b$B" "$H" "$B" "$N" \
          > "$ROOT/$OUT/P23-$ARM-h$H-b$B.chunk.txt" 2>&1 ) &
      jobs_running=$((jobs_running+1))
      if [ "$jobs_running" -ge "$JOBS" ]; then wait -n; jobs_running=$((jobs_running-1)); fi
    done
  done
done
wait
cat "$ROOT/$OUT"/P23-*-h*-b*.chunk.txt | sort > "$ROOT/$OUT/P23-chunks.txt"
rm -f "$ROOT/$OUT"/P23-*-h*-b*.chunk.txt
ok=$(grep -c '^OK' "$ROOT/$OUT/P23-chunks.txt"); bad=$(grep -c '^BAD' "$ROOT/$OUT/P23-chunks.txt")
want=$(( $(echo $BASES | wc -w) * $(echo $RUNGS | wc -w) * $(echo $ARMS | wc -w) ))
echo "CHUNKS (P23): ok=$ok bad=$bad (expect ok=$want bad=0)"
exit $(( bad != 0 || ok != want ))
