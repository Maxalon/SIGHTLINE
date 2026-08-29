#!/usr/bin/env bash
# TRUE BAND measurement chunk runner (adapted from docs/measurements/x2/run_chunk.sh).
# Usage: run_chunk.sh <tag> <heat> <base> [N]
# Env: BIN=<dir with Sightline> (default runbin/tb), BAND=mult|add (SIGHTLINE_CHOICEBAND),
#      PROBE=1 (SIGHTLINE_BANDPROBE), OBJ=<objective pin>.
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
TAG=$1; H=$2; B=$3; N=${4:-5}
BIN=${BIN:-runbin/tb}
mkdir -p "$PWD/.xdg/$TAG"; export XDG_CONFIG_HOME="$PWD/.xdg/$TAG"
OUT=docs/measurements/tb
mkdir -p "$OUT"
rm -f "$PWD/$OUT/$TAG.json"
env ${BAND:+SIGHTLINE_CHOICEBAND=$BAND} ${PROBE:+SIGHTLINE_BANDPROBE=$PROBE} ${OBJ:+SIGHTLINE_OBJ=$OBJ} \
  SIGHTLINE_BALANCE=$N SIGHTLINE_BALANCE_HEAT=$H SIGHTLINE_BALANCE_BASE=$B \
  SIGHTLINE_BALANCE_JSON="$PWD/$OUT/$TAG.json" \
  setsid xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" > "$OUT/$TAG.log" 2>&1 &
APP=$!
for _ in $(seq 1 7200); do
  if [ -f "$OUT/$TAG.json" ] && python3 -c "import json,sys;json.load(open('$OUT/$TAG.json'))" 2>/dev/null; then break; fi
  kill -0 $APP 2>/dev/null || break
  sleep 5
done
sleep 3; kill -9 -$APP 2>/dev/null; kill -9 $APP 2>/dev/null; wait $APP 2>/dev/null
sed -n '/BALANCE REPORT/,$p' "$OUT/$TAG.log" > "$OUT/$TAG.report.txt" 2>/dev/null
EXPECT=$((N*2))
GOT=$(python3 -c "import json;print(json.load(open('$OUT/$TAG.json'))['runs'])" 2>/dev/null || echo 0)
if [ "$GOT" = "$EXPECT" ]; then echo "OK   $TAG runs=$EXPECT"; else echo "BAD  $TAG (runs=$GOT, wanted $EXPECT)"; fi
