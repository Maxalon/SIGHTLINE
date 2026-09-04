#!/usr/bin/env bash
# P22 "NOTHING WITHOUT A SWITCH" — pricing the SUPPLY heal ORDERING.
#
#   Two arms on identical CRN cells, ONE binary (runbin/P22), one env var apart:
#     base       defaults                    <- the shipped ordering (heal AFTER the wound gauge)
#     healfirst  SIGHTLINE_HEALFIRST=1       <- THE FORK PAYS' pre-wave ordering, restored
#
#   This is NOT an inertness round. SUPPLY is 828 of 5,413 played nodes (15.3%, P21's census) and
#   the flag decides whether a soldier who ends a cleared SUPPLY node hurt carries a WOUND out of
#   it, so the two arms are expected to DIFFER. The question is by how much, and whether the round
#   can resolve it — report n_disc and the MDE beside the delta (C2's rule), never the win-rate
#   column alone.
#
# Usage: round.sh <arm>            (arm = base | healfirst)
set -u
cd "$(dirname "$0")/../../.." || exit 1
ARM=${1:?arm}
case "$ARM" in
  base)      BIN=runbin/P22; EXTRA="" ;;
  healfirst) BIN=runbin/P22; EXTRA="SIGHTLINE_HEALFIRST=1" ;;
  *) echo "unknown arm $ARM" >&2; exit 2 ;;
esac
BASES=${BASES:-"0 10 20 30 40 50 60 70 80 90 100 110 120 130 140 150"}
HEATS=${HEATS:-"0 4 8"}
PAR=${PAR:-3}
for H in $HEATS; do
  for B in $BASES; do
    while [ "$(jobs -rp | wc -l)" -ge "$PAR" ]; do wait -n; done
    BIN=$BIN OUT=docs/measurements/p22 EXTRA="$EXTRA" \
      bash docs/measurements/p22/run_chunk.sh "$ARM-h$H-b$B" "$H" "$B" 20 \
      >> "docs/measurements/p22/$ARM-chunks.txt" 2>&1 &
  done
done
wait
echo "== $ARM: $(grep -c '^OK ' docs/measurements/p22/$ARM-chunks.txt) OK / $(grep -c '^BAD ' docs/measurements/p22/$ARM-chunks.txt) BAD"
