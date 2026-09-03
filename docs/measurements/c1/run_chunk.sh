#!/usr/bin/env bash
#
# ⚠ SUPERSEDED (P15 THE UNVERIFIED, 2026-09-03) — COPY docs/measurements/p15/run_chunk.sh INSTEAD.
# Layer (c) below hard-codes `runs == N*2`, which marks every legitimate SINGLE-POLICY batch
# (SIGHTLINE_BALANCE_SLOPPY / _DUMB — the shape `campaigns[]` was shipped to enable) BAD, and it
# never looks at the RUNG or the SLOT BASE at all, so a chunk archived under a rung it did not
# measure passed it with `runs` correct and exit 0. p15/check_chunk.py asserts `runs` against the
# artifact's own `batch.expectedRuns` and the rung/base against what the runner exported.
# This file is kept UNCHANGED as provenance: it is how its own round was actually run.
#
# C1 chunk runner — PROGRAM CONTOUR wave C1 "THE FLAT MIDDLE".
# Usage: run_chunk.sh <tag> <heat> <base> [N]
# A verbatim port of docs/measurements/l3/run_chunk.sh with two changes only:
#   * ROOT is derived from this script's own location, so it works in an agent worktree
#     (l3's runner hard-codes `cd /home/user/SIGHTLINE` and would silently measure the WRONG tree).
#   * OUT/BIN default to the c1 directory / runbin/C1base.
# All three layers of W1's measurement contract are kept, in this order:
#   (a) rm -f the target FIRST  — a display-less batch REFUSES and leaves the previous chunk's
#       file byte- and mtime-identical, so a bare runs-assertion would read the PREVIOUS batch.
#   (b) check the EXIT CODE     — 2 means "no display, nothing written".
#   (c) assert the JSON's own `runs` field — third line of defence, catches a short batch.
# Extra env passed through verbatim: EXTRA="SIGHTLINE_FOO=1 ..." lets a paired lever chunk set
# its dial without a second copy of this file.
set -u
ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)
cd "$ROOT" || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
TAG=$1; H=$2; B=$3; N=${4:-10}
BIN=${BIN:-runbin/C1base}
OUT=${OUT:-docs/measurements/c1}
mkdir -p "$OUT" "$PWD/.xdg/$TAG"
export XDG_CONFIG_HOME="$PWD/.xdg/$TAG"
rm -f "$PWD/$OUT/$TAG.json"                                   # (a)
env ${EXTRA:-} SIGHTLINE_BALANCE=$N SIGHTLINE_BALANCE_HEAT=$H SIGHTLINE_BALANCE_BASE=$B \
  SIGHTLINE_BALANCE_JSON="$PWD/$OUT/$TAG.json" \
  xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" > "$OUT/$TAG.log" 2>&1
EC=$?                                                          # (b)
sed -n '/BALANCE REPORT/,$p' "$OUT/$TAG.log" > "$OUT/$TAG.report.txt" 2>/dev/null
if [ "$EC" = 2 ]; then echo "BAD  $TAG (exit 2 - no display, nothing written)"; exit 1; fi
EXPECT=$((N*2))
GOT=$(python3 -c "import json;print(json.load(open('$OUT/$TAG.json'))['runs'])" 2>/dev/null || echo 0)
if [ "$GOT" = "$EXPECT" ]; then echo "OK   $TAG runs=$EXPECT"; else echo "BAD  $TAG (runs=$GOT want $EXPECT, exit $EC)"; fi
