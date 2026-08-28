#!/bin/bash
# usage: run_chunk.sh <tag> <N> <heat> <base> [extra env...]
set -u
cd /home/user/wt/x1
. ./.envrc
TAG="$1"; N="$2"; HEAT="$3"; BASE="$4"
mkdir -p /home/user/wt/x1/runs
LOG="/home/user/wt/x1/runs/$TAG.log"
JSON="/home/user/wt/x1/runs/$TAG.json"
SIGHTLINE_BALANCE="$N" SIGHTLINE_BALANCE_HEAT="$HEAT" SIGHTLINE_BALANCE_BASE="$BASE" \
  SIGHTLINE_BALANCE_JSON="$JSON" \
  xvfb-run -a -s "-screen 0 1280x800x24" ./bin/Release/net8.0/Sightline > "$LOG" 2>&1
EC=$?
EXPECT=$((N*2))
GOT=$(grep -oP '^runs=\K[0-9]+' "$LOG" | head -1)
echo "CHUNK $TAG exit=$EC runs=${GOT:-NONE} expect=$EXPECT $( [ "${GOT:-0}" = "$EXPECT" ] && echo OK || echo BAD )"
