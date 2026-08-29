#!/usr/bin/env bash
# W1 review blocker 6 — the STALE-FILE hazard the amended refusal path created, and its fix.
#
# W1/1 made a display-less batch refuse and leave the target JSON's bytes AND mtime untouched.
# That is right for the DATA, but it broke the procedure every chunk script inherited from X2:
# `json.load(out)['runs'] == 2N` now reads the PREVIOUS chunk's runs and reports OK for a batch
# that measured nothing. Pre-W1 the same check worked only because the file got clobbered with
# runs=0. Part 1 demonstrates the trap; part 2 shows run_chunk.sh refusing it.
#
# Part 2 uses a STUB binary that exits 2 without writing, because run_chunk.sh always launches
# under xvfb-run and therefore cannot reach the no-display path by unsetting DISPLAY — the real
# way it gets there is xvfb-run itself failing (no Xvfb, display numbers exhausted), which is
# exactly what the stub reproduces.
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg/gate6"
export XDG_CONFIG_HOME="$PWD/.xdg/gate6"
BIN=${BIN:-bin/Release/net8.0}
J="$PWD/.xdg/gate6/stale.json"

echo "=== PART 1 — the TRAP: the X2-era assertion against a display-less batch ==="
# a convincing "previous chunk": a complete-looking aggregate with runs=20 in it
python3 -c "import json;json.dump({'runs':20,'runWinRate':65.0,'note':'PREVIOUS CHUNK'},open('$J','w'))"
env -u DISPLAY SIGHTLINE_BALANCE=10 SIGHTLINE_BALANCE_JSON="$J" "$BIN/Sightline" >/dev/null 2>&1
echo "batch exit code: $?   (2 = refused, nothing written)"
GOT=$(python3 -c "import json;print(json.load(open('$J'))['runs'])")
echo "the old runs-field check reads runs=$GOT -> it would print 'OK runs=20' for a batch that measured NOTHING"

echo
echo "=== PART 2 — the FIX: run_chunk.sh against a binary that refuses ==="
STUB="$PWD/.xdg/gate6/stub"
mkdir -p "$STUB"
printf '#!/bin/sh\necho "BALANCE: no display - run under xvfb-run. No data written." >&2\nexit 2\n' > "$STUB/Sightline"
chmod +x "$STUB/Sightline"
# plant a stale, complete-looking result where this chunk would write
python3 -c "import json;json.dump({'runs':20,'runWinRate':65.0,'note':'PREVIOUS CHUNK'},open('docs/measurements/w1/gate6probe.json','w'))"
echo "planted stale docs/measurements/w1/gate6probe.json with runs=20"
BIN="$STUB" bash docs/measurements/w1/run_chunk.sh gate6probe 0 100 10
echo "run_chunk.sh exit code: $?   (non-zero = the stale file did not fool it)"
rm -f docs/measurements/w1/gate6probe.json docs/measurements/w1/gate6probe.log docs/measurements/w1/gate6probe.report.txt
