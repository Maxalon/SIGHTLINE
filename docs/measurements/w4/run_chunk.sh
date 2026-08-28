#!/usr/bin/env bash
# W4 measurement chunk runner. Usage: run_chunk.sh <tag> <heat> <base> [N] 
# Env: BIN=<dir containing Sightline> (default bin/Release/net8.0), DEPLOY=<SIGHTLINE_DEPLOY value>
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
TAG=$1; H=$2; B=$3; N=${4:-10}
BIN=${BIN:-bin/Release/net8.0}
mkdir -p "$PWD/.xdg/$TAG"; export XDG_CONFIG_HOME="$PWD/.xdg/$TAG"
OUT=docs/measurements/w4
mkdir -p "$OUT"
env ${DEPLOY:+SIGHTLINE_DEPLOY=$DEPLOY} \
  SIGHTLINE_BALANCE=$N SIGHTLINE_BALANCE_HEAT=$H SIGHTLINE_BALANCE_BASE=$B \
  SIGHTLINE_BALANCE_JSON="$PWD/$OUT/$TAG.json" \
  xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" > "$OUT/$TAG.log" 2>&1
EXPECT=$((N*2))
sed -n '/BALANCE REPORT/,$p' "$OUT/$TAG.log" > "$OUT/$TAG.report.txt" 2>/dev/null
if grep -q "runs=$EXPECT" "$OUT/$TAG.log"; then echo "OK   $TAG runs=$EXPECT"; else echo "BAD  $TAG (no runs=$EXPECT)"; grep -m2 "runs=" "$OUT/$TAG.log"; fi
