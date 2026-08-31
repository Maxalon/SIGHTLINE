#!/usr/bin/env bash
# C3 chunk runner — adapted from docs/measurements/l3/run_chunk.sh, with the repo root pointed at
# THIS worktree, the output dir at docs/measurements/c3, and an EXTRA env passthrough so a lever
# arm can be run on the SAME slots as its baseline. All three layers of W1's measurement contract,
# in order:
#   (a) rm -f the target FIRST  — a display-less batch REFUSES and leaves the previous chunk's file
#       byte- and mtime-identical, so a bare runs-assertion would read the PREVIOUS batch.
#   (b) check the EXIT CODE     — 2 means "no display, nothing written".
#   (c) assert the JSON's own `runs` field — third line of defence, catches a short batch.
# Usage: run_chunk.sh <tag> <heat> <base> [N]   ; env: ROOT, BIN, OUT, EXTRA
set -u
ROOT=${ROOT:-/home/user/SIGHTLINE/.claude/worktrees/agent-a2b7f92f5a4d91dfa}
cd "$ROOT" || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
TAG=$1; H=$2; B=$3; N=${4:-10}
BIN=${BIN:-runbin/C3base}
OUT=${OUT:-docs/measurements/c3}
EXTRA=${EXTRA:-}
mkdir -p "$OUT" "$ROOT/.xdg/$TAG"
export XDG_CONFIG_HOME="$ROOT/.xdg/$TAG"
rm -f "$ROOT/$OUT/$TAG.json"                                   # (a)
env $EXTRA SIGHTLINE_BALANCE=$N SIGHTLINE_BALANCE_HEAT=$H SIGHTLINE_BALANCE_BASE=$B \
  SIGHTLINE_BALANCE_JSON="$ROOT/$OUT/$TAG.json" \
  xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" > "$OUT/$TAG.log" 2>&1
EC=$?                                                          # (b)
sed -n '/BALANCE REPORT/,$p' "$OUT/$TAG.log" > "$OUT/$TAG.report.txt" 2>/dev/null
if [ "$EC" = 2 ]; then echo "BAD  $TAG (exit 2 - no display, nothing written)"; exit 1; fi
EXPECT=$((N*2))
GOT=$(python3 -c "import json;print(json.load(open('$OUT/$TAG.json'))['runs'])" 2>/dev/null || echo 0)
if [ "$GOT" = "$EXPECT" ]; then echo "OK   $TAG runs=$EXPECT"; else echo "BAD  $TAG (runs=$GOT want $EXPECT, exit $EC)"; fi
