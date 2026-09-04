#!/usr/bin/env bash
# P16 GROUND TRUTH — the CRN A/B round.
#   arm A (on)  : the shipped tree — VOID rifts + ARID soft sand live
#   arm B (off) : SIGHTLINE_NEWGROUND=0 — the pre-P16 board EXACTLY (C4's three biomes untouched)
# 3 rungs (h0/h4/h8) x 8 CRN slot bases x 20 campaigns = 160/rung/arm, 960 campaigns total.
# Same binary, same slot seeds, one lever. Each chunk goes through run_chunk.sh's three
# completion layers (rm -f first / exit code / check_chunk.py).
set -u
cd "$(dirname "$0")/../../.." || exit 1
BIN=${BIN:-runbin/p16}
for H in 0 4 8; do
  for B in 0 10 20 30 40 50 60 70; do
    BIN="$BIN" OUT=docs/measurements/p16 bash docs/measurements/p16/run_chunk.sh "A-h${H}-b${B}" "$H" "$B" 10
    BIN="$BIN" OUT=docs/measurements/p16 EXTRA="SIGHTLINE_NEWGROUND=0" \
      bash docs/measurements/p16/run_chunk.sh "B-h${H}-b${B}" "$H" "$B" 10
  done
done
