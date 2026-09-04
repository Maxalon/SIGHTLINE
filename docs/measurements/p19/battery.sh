#!/usr/bin/env bash
# P19 "THE ROSTER CONTESTS" — the wave's CRN round.
#   THREE arms on IDENTICAL worlds, so the two levers can be attributed separately:
#     pre   SIGHTLINE_ELITEBOSS=0 SIGHTLINE_ROSTERID=0  — the pre-P19 tree, an exact restoration
#                                                          (proven field-for-field by the R0diag
#                                                           chunks against the BASE-COMMIT binary)
#     boss  SIGHTLINE_ELITEBOSS=1 SIGHTLINE_ROSTERID=0  — item 1 alone (the named elite moves to
#                                                          the ELITE node + the final-approach floor)
#     full  (defaults)                                   — the SHIPPED tree: item 1 + item 2 (the
#                                                          SMG monoculture's three range bands)
#   3 rungs {h0,h4,h8} x 8 CRN slot bases {0,10,...,70} x SIGHTLINE_BALANCE=20
#   = 40 campaigns per chunk (20 CRN worlds x greedy+sloppy) => n=320 per rung per arm,
#   2,880 campaigns over 72 chunks.
# Every chunk goes through run_chunk.sh, which does the three-layer completion check the CLAUDE.md
# measurement contract requires: rm -f the target first, check the process EXIT CODE (2 = no
# display / nothing written), and assert the JSON's own `runs` field.
#   Usage: BIN=runbin/p19/new bash docs/measurements/p19/battery.sh [jobs]
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
BIN=${BIN:?set BIN to a binary snapshot dir, e.g. BIN=runbin/p19/new}
JOBS=${1:-4}
N=${N:-20}
i=0
for arm in pre boss full; do
  case "$arm" in
    pre)  eb=0; ri=0 ;;
    boss) eb=1; ri=0 ;;
    full) eb=1; ri=1 ;;
  esac
  for h in 0 4 8; do
    for b in 0 10 20 30 40 50 60 70; do
      BIN="$BIN" ELITEBOSS="$eb" ROSTERID="$ri" bash "$HERE/run_chunk.sh" "p19-$arm-h$h-b$b" "$h" "$b" "$N" &
      i=$((i+1)); [ $((i % JOBS)) -eq 0 ] && wait
    done
  done
done
wait
echo "=== BATTERY DONE ($i chunks) ==="
