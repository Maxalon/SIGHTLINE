#!/usr/bin/env bash
# C5 review E2: FITTEST flake soak. Prints one PASS/FAIL line per run.
cd "$(dirname "$0")/.."
for i in $(seq 1 "${1:-12}"); do
  printf '%02d ' "$i"
  bash scripts/qa-isolated.sh SIGHTLINE_FITTEST=1 2>&1 | grep -oE "FITTEST: (PASS|FAIL)[^,]*" | head -1
done
