#!/usr/bin/env bash
# W1 GATE 2 — a batch with no display must REFUSE, not report zeroes over the previous chunk.
# Runs the same command twice against the same target JSON: once with no DISPLAY (must exit
# non-zero, print the named message, and leave the file's bytes AND mtime untouched), once under
# xvfb-run (must behave exactly as before).
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg/gate2"
export XDG_CONFIG_HOME="$PWD/.xdg/gate2"
BIN=${BIN:-bin/Release/net8.0}
J="$PWD/.xdg/gate2/target.json"

echo '{"runs":999,"sentinel":"THE PREVIOUS CHUNK S DATA"}' > "$J"
BEFORE_MTIME=$(stat -c %Y "$J"); BEFORE_SUM=$(md5sum < "$J")
echo "before: mtime=$BEFORE_MTIME  md5=$BEFORE_SUM"

echo "--- (a) no display ---"
env -u DISPLAY SIGHTLINE_BALANCE=2 SIGHTLINE_BALANCE_JSON="$J" "$BIN/Sightline"
RC=$?
echo "exit code: $RC"
AFTER_MTIME=$(stat -c %Y "$J"); AFTER_SUM=$(md5sum < "$J")
echo "after : mtime=$AFTER_MTIME  md5=$AFTER_SUM"
[ "$RC" -ne 0 ] && [ "$BEFORE_MTIME" = "$AFTER_MTIME" ] && [ "$BEFORE_SUM" = "$AFTER_SUM" ] \
  && echo "GATE2a: PASS (non-zero exit, JSON untouched)" || echo "GATE2a: FAIL"

echo "--- (b) same command under xvfb-run ---"
env SIGHTLINE_BALANCE=2 SIGHTLINE_BALANCE_JSON="$J" \
  xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" 2>/dev/null | grep -E "^aggregate JSON|^runs=|batch wall-time"
GOT=$(python3 -c "import json;print(json.load(open('$J'))['runs'])" 2>/dev/null || echo ERR)
[ "$GOT" = "4" ] && echo "GATE2b: PASS (runs=4 written)" || echo "GATE2b: FAIL (runs=$GOT)"
