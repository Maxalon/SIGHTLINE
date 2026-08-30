#!/usr/bin/env bash
# W2 (THE OPPONENT ACTS) measurement chunk runner — docs/measurements/w1/run_chunk.sh verbatim,
# re-pointed at docs/measurements/w2 and with the wave's own dial threaded through.
#   Usage: run_chunk.sh <tag> <heat> <base> [N]
#   Env:   BIN=<dir containing Sightline>   (REQUIRED — e.g. runbin/W2)
#          FIX=0|1 -> SIGHTLINE_AIIDLEFIX (the wave's single lever; unset = the shipped default)
# THREE-LAYER COMPLETION CHECK (CLAUDE.md measurement contract item 1, as amended by W1):
#   (a) rm -f the target FIRST. MANDATORY, not tidiness. W1's no-display path deliberately leaves
#       an existing JSON byte- and mtime-identical, so without this a stale file from the previous
#       chunk would satisfy the `runs` assertion below and report OK for a batch that measured
#       nothing.
#   (b) the process EXIT CODE. 2 = "no display, nothing written". The only signal a stale file
#       cannot fake.
#   (c) the JSON's own `runs` field == 2N. Third line of defence; still catches a short batch.
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
TAG=$1; H=$2; B=$3; N=${4:-10}
BIN=${BIN:?set BIN to a binary snapshot dir, e.g. BIN=runbin/W2}
mkdir -p "$PWD/.xdg/$TAG"; export XDG_CONFIG_HOME="$PWD/.xdg/$TAG"
OUT=docs/measurements/w2
mkdir -p "$OUT"
rm -f "$PWD/$OUT/$TAG.json"
START=$(date +%s)
env ${FIX:+SIGHTLINE_AIIDLEFIX=$FIX} \
  SIGHTLINE_BALANCE=$N SIGHTLINE_BALANCE_HEAT=$H SIGHTLINE_BALANCE_BASE=$B \
  SIGHTLINE_BALANCE_JSON="$PWD/$OUT/$TAG.json" \
  setsid xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" > "$OUT/$TAG.log" 2>&1 &
APP=$!
RC=""
for _ in $(seq 1 7200); do
  if [ -f "$OUT/$TAG.json" ] && python3 -c "import json,sys;json.load(open('$OUT/$TAG.json'))" 2>/dev/null; then break; fi
  if ! kill -0 $APP 2>/dev/null; then wait $APP 2>/dev/null; RC=$?; break; fi
  sleep 2
done
DONE=$(date +%s)
sleep 2; kill -9 -$APP 2>/dev/null; kill -9 $APP 2>/dev/null; wait $APP 2>/dev/null
sed -n '/BALANCE REPORT/,$p' "$OUT/$TAG.log" > "$OUT/$TAG.report.txt" 2>/dev/null
EXPECT=$((N*2))
GOT=$(python3 -c "import json;print(json.load(open('$OUT/$TAG.json'))['runs'])" 2>/dev/null || echo 0)
WR=$(python3 -c "import json;print(json.load(open('$OUT/$TAG.json'))['runWinRate'])" 2>/dev/null || echo "?")
echo "TIME $TAG data-ready-after=$((DONE-START))s"
if [ "${RC:-}" = "2" ]; then
  echo "BAD  $TAG — NO DISPLAY. The batch refused and wrote nothing (exit 2). Run under xvfb-run."
  echo "     Any $TAG.json on disk is a STALE file from an earlier chunk. Do not read it."
  exit 2
fi
if [ -n "${RC:-}" ] && [ "${RC:-}" != "0" ] && [ ! -f "$OUT/$TAG.json" ]; then
  echo "BAD  $TAG — the batch exited $RC with no JSON written. See $OUT/$TAG.log."
  exit 1
fi
if [ "$GOT" = "$EXPECT" ]; then echo "OK   $TAG runs=$EXPECT winRate=$WR"; else echo "BAD  $TAG (runs=$GOT, wanted $EXPECT)"; fi
