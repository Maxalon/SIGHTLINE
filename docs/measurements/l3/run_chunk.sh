#!/usr/bin/env bash
# L3 chunk runner — the COMPOSED-tree ladder. Usage: run_chunk.sh <tag> <heat> <base> [N]
# Follows W1's rewritten measurement contract, all three layers, in this order:
#   (a) rm -f the target FIRST  — since W1 a display-less batch REFUSES and leaves the previous
#       chunk's file byte- and mtime-identical, so a bare runs-assertion would read the PREVIOUS
#       batch and print OK for a batch that measured nothing. A missing file must mean "no data".
#   (b) check the EXIT CODE     — 2 means "no display, nothing written"; the only signal a stale
#       file cannot fake.
#   (c) assert the JSON's own `runs` field — third line of defence, still catches a short batch.
set -u
cd /home/user/SIGHTLINE || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
TAG=$1; H=$2; B=$3; N=${4:-10}
BIN=${BIN:-runbin/L3}
OUT=docs/measurements/l3
mkdir -p "$OUT" "$PWD/.xdg/$TAG"
export XDG_CONFIG_HOME="$PWD/.xdg/$TAG"
rm -f "$PWD/$OUT/$TAG.json"                                   # (a)
SIGHTLINE_BALANCE=$N SIGHTLINE_BALANCE_HEAT=$H SIGHTLINE_BALANCE_BASE=$B \
  SIGHTLINE_BALANCE_JSON="$PWD/$OUT/$TAG.json" \
  xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" > "$OUT/$TAG.log" 2>&1
EC=$?                                                          # (b)
sed -n '/BALANCE REPORT/,$p' "$OUT/$TAG.log" > "$OUT/$TAG.report.txt" 2>/dev/null
if [ "$EC" = 2 ]; then echo "BAD  $TAG (exit 2 - no display, nothing written)"; exit 1; fi
EXPECT=$((N*2))
GOT=$(python3 -c "import json;print(json.load(open('$OUT/$TAG.json'))['runs'])" 2>/dev/null || echo 0)
if [ "$GOT" = "$EXPECT" ]; then echo "OK   $TAG runs=$EXPECT"; else echo "BAD  $TAG (runs=$GOT want $EXPECT, exit $EC)"; fi
