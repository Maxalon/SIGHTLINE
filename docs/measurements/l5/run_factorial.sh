#!/usr/bin/env bash
# L5 — THE h6 2^3 FACTORIAL: MIDTOOTH{0,3} x AIDECLINE{0,1} x BIOMEMECH{0,1} = 8 arms x 16 disjoint
# CRN slot bases x N=10 (runs=20) = 128 chunks, 320 campaigns per arm, heat PINNED, all on the
# SAME snapshot as the ladder. The all-shipped arm (m3a1b1) sets the three dials to their DEFAULTS
# explicitly, so it must reproduce the ladder's own L5-h6 chunks byte-for-byte (minus harness{}) —
# factorial.py asserts it; that is the CRN-chain check for the round.
set -u
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
export BIN=${BIN:-runbin/L5} OUT=docs/measurements/l5
ROOT=$(cd "$HERE/../../.." && pwd)
H=${1:-6}
jobs_running=0
for m in 3 0; do for a in 1 0; do for b in 1 0; do
  arm="m${m}a${a}b${b}"
  for B in 0 10 20 30 40 50 60 70 80 90 100 110 120 130 140 150; do
    ( EXTRA="SIGHTLINE_MIDTOOTH=$m SIGHTLINE_AIDECLINE=$a SIGHTLINE_BIOMEMECH=$b" \
      bash "$HERE/../c1/run_chunk.sh" "L5fac-$arm-h$H-b$B" "$H" "$B" 10 > "$ROOT/$OUT/L5fac-$arm-h$H-b$B.chunk.txt" ) &
    jobs_running=$((jobs_running+1))
    if [ "$jobs_running" -ge 4 ]; then wait -n; jobs_running=$((jobs_running-1)); fi
  done
done; done; done
wait
cat "$ROOT/$OUT"/L5fac-*-h$H-b*.chunk.txt | sort > "$ROOT/$OUT/L5fac-chunks.txt"
rm -f "$ROOT/$OUT"/L5fac-*-h$H-b*.chunk.txt
ok=$(grep -c '^OK' "$ROOT/$OUT/L5fac-chunks.txt"); bad=$(grep -c '^BAD' "$ROOT/$OUT/L5fac-chunks.txt")
echo "CHUNKS (L5fac): ok=$ok bad=$bad (expect ok=128 bad=0)"
exit $(( bad != 0 || ok != 128 ))
