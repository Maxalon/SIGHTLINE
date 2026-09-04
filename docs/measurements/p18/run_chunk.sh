#!/usr/bin/env bash
#
# P18 "THE SECOND AXIS" measurement chunk runner — the p15/w1 pattern, re-pointed at
# docs/measurements/p18 and at a caller-supplied binary SNAPSHOT (runbin/<tag>/), so the tree can
# keep building while a chunk is in flight.
#   Usage: BIN=runbin/p18 run_chunk.sh <tag> <heat> <base> [N]
#
# THREE-LAYER COMPLETION CHECK (CLAUDE.md's SIGHTLINE_BALANCE contract, item 1):
#   (a) rm -f the target FIRST. MANDATORY: a display-less batch REFUSES and leaves any existing
#       JSON byte- and mtime-identical, so a stale file would satisfy (c) for a batch that
#       measured nothing.
#   (b) the process EXIT CODE. 2 == "no display, nothing written" — the only signal a stale file
#       cannot fake.
#   (c) the JSON's own `runs` field == 2N (greedy + sloppy).
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
TAG=$1; H=$2; B=$3; N=${4:-10}
BIN=${BIN:?set BIN to a binary snapshot dir, e.g. BIN=runbin/p18}
mkdir -p "$PWD/.xdg/$TAG"; export XDG_CONFIG_HOME="$PWD/.xdg/$TAG"
OUT=docs/measurements/p18
mkdir -p "$OUT"
rm -f "$PWD/$OUT/$TAG.json"                       # (a)
SIGHTLINE_BALANCE=$N SIGHTLINE_BALANCE_HEAT=$H SIGHTLINE_BALANCE_BASE=$B \
  SIGHTLINE_BALANCE_JSON="$PWD/$OUT/$TAG.json" \
  xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" > "$OUT/$TAG.log" 2>&1
RC=$?                                              # (b)
if [ $RC -ne 0 ]; then echo "$TAG: BAD exit=$RC"; exit 1; fi
if [ ! -f "$OUT/$TAG.json" ]; then echo "$TAG: BAD no json"; exit 1; fi
python3 - "$OUT/$TAG.json" "$N" <<'PY'             # (c)
import json,sys
d=json.load(open(sys.argv[1])); want=2*int(sys.argv[2])
print(("OK  " if d.get("runs")==want else "BAD ")+f"runs={d.get('runs')} want={want}")
sys.exit(0 if d.get("runs")==want else 1)
PY
