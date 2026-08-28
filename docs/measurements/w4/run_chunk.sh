#!/usr/bin/env bash
# W4 measurement chunk runner. Usage: run_chunk.sh <tag> <heat> <base> [N] 
# Env: BIN=<dir with Sightline> (default bin/Release/net8.0), DEPLOY=<shape>, MIX=<a,b,c,d>,
#      OBJ=<objective pin>, EFIX=0 (restore the pre-fix SmartEscort instrument)
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
TAG=$1; H=$2; B=$3; N=${4:-10}
BIN=${BIN:-bin/Release/net8.0}
mkdir -p "$PWD/.xdg/$TAG"; export XDG_CONFIG_HOME="$PWD/.xdg/$TAG"
OUT=docs/measurements/w4
mkdir -p "$OUT"
rm -f "$PWD/$OUT/$TAG.json"
env ${DEPLOY:+SIGHTLINE_DEPLOY=$DEPLOY} ${MIX:+SIGHTLINE_DEPLOYMIX=$MIX} ${OBJ:+SIGHTLINE_OBJ=$OBJ} ${EFIX:+SIGHTLINE_ESCORTFIX=$EFIX} ${MASS:+SIGHTLINE_PODMASS=$MASS} ${RIM:+SIGHTLINE_RIMWAVES=$RIM} ${UNI:+SIGHTLINE_PODUNIFORM=$UNI} \
  SIGHTLINE_BALANCE=$N SIGHTLINE_BALANCE_HEAT=$H SIGHTLINE_BALANCE_BASE=$B \
  SIGHTLINE_BALANCE_JSON="$PWD/$OUT/$TAG.json" \
  setsid xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" > "$OUT/$TAG.log" 2>&1 &
APP=$!   # setsid => APP is its own process-group leader, so the reap below takes Xvfb with it
# The batch writes its aggregate JSON as its LAST act (Program.BalanceBatch: report -> WriteJson
# -> Display.Shutdown -> CloseWindow). Under concurrent llvmpipe contexts that GL/audio shutdown
# has been observed to spin for minutes AFTER the data is complete, and it also swallows the
# report's stdout buffer — so the batch is considered finished the moment a COMPLETE JSON lands,
# and the straggler is reaped. The completeness assertion is therefore on the JSON's own
# `runs` field, not on a log line: it is the same number and it cannot be half-written.
for _ in $(seq 1 3600); do
  if [ -f "$OUT/$TAG.json" ] && python3 -c "import json,sys;json.load(open('$OUT/$TAG.json'))" 2>/dev/null; then break; fi
  kill -0 $APP 2>/dev/null || break
  sleep 5
done
sleep 3; kill -9 -$APP 2>/dev/null; kill -9 $APP 2>/dev/null; wait $APP 2>/dev/null
sed -n '/BALANCE REPORT/,$p' "$OUT/$TAG.log" > "$OUT/$TAG.report.txt" 2>/dev/null
EXPECT=$((N*2))
GOT=$(python3 -c "import json;print(json.load(open('$OUT/$TAG.json'))['runs'])" 2>/dev/null || echo 0)
if [ "$GOT" = "$EXPECT" ]; then echo "OK   $TAG runs=$EXPECT"; else echo "BAD  $TAG (runs=$GOT, wanted $EXPECT)"; fi
