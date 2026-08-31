#!/usr/bin/env bash
# C3 archive trim. The wave ran four full 48-chunk rounds; two of them are provably redundant and
# committing 15 MB of duplicate JSON helps nobody:
#   R0    pristine binary, pre-instrument. Only its 8 h0/h6 chunks are used — by inert.py, as the
#         baseline half of the instrument-inertness proof. The other 40 measure the same worlds as
#         B1 on an instrument with fewer fields.
#   D0    instrumented binary at defaults. samearm.py proved B1 reproduces it on all 67,956
#         aggregate fields including encounterMidrun, so B1 IS D0. Dropped entirely; every "D0"
#         figure in the DEVLOG is reproduced by `enc.py B1`.
# Kept in full: B1 (baseline arm) and L1 (lever arm) — the CRN pair the wave's claim rests on.
set -u
cd "$(dirname "$0")" || exit 1
rm -f D0-*
for h in hR h2 h4 h8; do
  for b in 0 10 20 30 40 50 60 70; do rm -f "R0-$h-b$b".*; done
done
for h in h0 h6; do
  for b in 40 50 60 70; do rm -f "R0-$h-b$b".*; done
done
ls *.json | wc -l
du -sh .
