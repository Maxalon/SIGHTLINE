#!/usr/bin/env bash
# X2 measurement chunk runner. Usage: run_chunk.sh <tag> <heat> <base> [N]
# Env: BIN=<dir with Sightline> (default runbin/<tag-prefix> else bin/Release/net8.0),
#      MIX=<a,b,c,d>, DEPLOY=<shape>, OBJ=<objective pin>, UNI=0|1, MASS=<n>, RIM=1,
#      TOUGH=<n>, TRIM=<n>  (X2: SIGHTLINE_TOUGH / SIGHTLINE_TRIM overrides)
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
TAG=$1; H=$2; B=$3; N=${4:-10}
BIN=${BIN:-bin/Release/net8.0}
mkdir -p "$PWD/.xdg/$TAG"; export XDG_CONFIG_HOME="$PWD/.xdg/$TAG"
OUT=docs/measurements/x2
mkdir -p "$OUT"
rm -f "$PWD/$OUT/$TAG.json"
env ${DEPLOY:+SIGHTLINE_DEPLOY=$DEPLOY} ${MIX:+SIGHTLINE_DEPLOYMIX=$MIX} ${OBJ:+SIGHTLINE_OBJ=$OBJ} \
    ${UNI:+SIGHTLINE_PODUNIFORM=$UNI} ${MASS:+SIGHTLINE_PODMASS=$MASS} ${RIM:+SIGHTLINE_RIMWAVES=$RIM} \
    ${TOUGH:+SIGHTLINE_TOUGH=$TOUGH} ${TRIM:+SIGHTLINE_TRIM=$TRIM} ${AIMTRIM:+SIGHTLINE_AIMTRIM=$AIMTRIM} ${EBASE:+SIGHTLINE_ENEMYBASE=$EBASE} \
  SIGHTLINE_BALANCE=$N SIGHTLINE_BALANCE_HEAT=$H SIGHTLINE_BALANCE_BASE=$B \
  SIGHTLINE_BALANCE_JSON="$PWD/$OUT/$TAG.json" \
  setsid xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" > "$OUT/$TAG.log" 2>&1 &
APP=$!   # setsid => APP is its own process-group leader, so the reap below takes Xvfb with it
# The batch writes its aggregate JSON as its LAST act; under concurrent llvmpipe contexts the
# GL/audio shutdown can spin for minutes AFTER the data is complete, so completion is asserted
# on the JSON's own `runs` field (it cannot be half-written), not on a log line.
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
