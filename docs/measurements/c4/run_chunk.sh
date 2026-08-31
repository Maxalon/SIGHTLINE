#!/usr/bin/env bash
# C4 chunk runner — the CRN-PAIRED A/B for the biome GROUND layer.
# Usage: run_chunk.sh <tag> <heat> <base> <biomemech 0|1> [N]
#
# Identical in structure to docs/measurements/l3/run_chunk.sh (W1's three-layer completion
# contract), with ONE addition: the arm is SIGHTLINE_BIOMEMECH, and BOTH arms run the SAME
# binary from the SAME snapshot on the SAME slot sets. That is the whole point — the only
# difference between an A row and a B row is whether Terrain.Enabled is true.
#   (a) rm -f the target FIRST  — a display-less batch REFUSES and leaves the previous chunk's
#       file byte- and mtime-identical, so a bare runs-assertion would read the PREVIOUS batch.
#   (b) check the EXIT CODE     — 2 means "no display, nothing written".
#   (c) assert the JSON's own `runs` field == 2N.
set -u
WT=$(cd "$(dirname "$0")/../../.." && pwd)
cd "$WT" || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
TAG=$1; H=$2; B=$3; MECH=$4; N=${5:-10}
BIN=${BIN:-runbin/C4}
OUT=docs/measurements/c4
mkdir -p "$OUT" "$WT/.xdg/$TAG"
export XDG_CONFIG_HOME="$WT/.xdg/$TAG"
rm -f "$WT/$OUT/$TAG.json"                                    # (a)
SIGHTLINE_BALANCE=$N SIGHTLINE_BALANCE_HEAT=$H SIGHTLINE_BALANCE_BASE=$B \
  SIGHTLINE_BIOMEMECH=$MECH \
  SIGHTLINE_BALANCE_JSON="$WT/$OUT/$TAG.json" \
  xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" > "$OUT/$TAG.log" 2>&1
EC=$?                                                          # (b)
sed -n '/BALANCE REPORT/,$p' "$OUT/$TAG.log" > "$OUT/$TAG.report.txt" 2>/dev/null
if [ "$EC" = 2 ]; then echo "BAD  $TAG (exit 2 - no display, nothing written)"; exit 1; fi
EXPECT=$((N*2))
GOT=$(python3 -c "import json;print(json.load(open('$OUT/$TAG.json'))['runs'])" 2>/dev/null || echo 0)
if [ "$GOT" = "$EXPECT" ]; then echo "OK   $TAG runs=$EXPECT"; else echo "BAD  $TAG (runs=$GOT want $EXPECT, exit $EC)"; fi
