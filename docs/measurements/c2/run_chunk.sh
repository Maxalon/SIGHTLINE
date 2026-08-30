#!/usr/bin/env bash
# C2 chunk runner — "THE OPPONENT DECLINES". Usage:
#   AIDECLINE=0|1 BIN=runbin/<tag> run_chunk.sh <tag> <heat> <base> [N]
# A verbatim descendant of docs/measurements/l3/run_chunk.sh: all three layers of W1's
# measurement contract, in this order.
#   (a) rm -f the target FIRST  — since W1 a display-less batch REFUSES and leaves the previous
#       chunk's file byte- and mtime-identical, so a bare runs-assertion would read the PREVIOUS
#       batch and print OK for a batch that measured nothing. A missing file must mean "no data".
#   (b) check the EXIT CODE     — 2 means "no display, nothing written"; the only signal a stale
#       file cannot fake.
#   (c) assert the JSON's own `runs` field — third line of defence, still catches a short batch.
# The one addition: SIGHTLINE_AIDECLINE is stamped explicitly on EVERY chunk, so a baseline
# chunk can never silently inherit the shipped default.
set -u
# Repo root is derived from THIS SCRIPT's location (docs/measurements/c2/), not hardcoded.
# The first version pinned an agent worktree path and `exit 1`d anywhere else, which made the
# README's "Reproducing" block dead on every other checkout. Override with ROOT=... if needed.
ROOT=${ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)}
cd "$ROOT" || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
TAG=$1; H=$2; B=$3; N=${4:-10}
BIN=${BIN:-runbin/C2}
DECL=${AIDECLINE:-1}
OUT=docs/measurements/c2
mkdir -p "$OUT" "$PWD/.xdg/$TAG"
export XDG_CONFIG_HOME="$PWD/.xdg/$TAG"
rm -f "$PWD/$OUT/$TAG.json"                                   # (a)
SIGHTLINE_BALANCE=$N SIGHTLINE_BALANCE_HEAT=$H SIGHTLINE_BALANCE_BASE=$B \
  SIGHTLINE_AIDECLINE=$DECL \
  SIGHTLINE_BALANCE_JSON="$PWD/$OUT/$TAG.json" \
  xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" > "$OUT/$TAG.log" 2>&1
EC=$?                                                          # (b)
sed -n '/BALANCE REPORT/,$p' "$OUT/$TAG.log" > "$OUT/$TAG.report.txt" 2>/dev/null
if [ "$EC" = 2 ]; then echo "BAD  $TAG (exit 2 - no display, nothing written)"; exit 1; fi
EXPECT=$((N*2))
GOT=$(python3 -c "import json;print(json.load(open('$OUT/$TAG.json'))['runs'])" 2>/dev/null || echo 0)
if [ "$GOT" = "$EXPECT" ]; then echo "OK   $TAG runs=$EXPECT decline=$DECL"; else echo "BAD  $TAG (runs=$GOT want $EXPECT, exit $EC)"; fi
