#!/usr/bin/env bash
# W6 measured evidence: a rebound key fires its NEW action and NOT its old one.
#
# METHOD (the thing R1 paid two runs to learn): `xdotool key X` presses AND releases inside
# a single frame, so Raylib's IsKeyPressed - which compares this frame's key state against
# last frame's - never observes it. Only the key QUEUE (GetKeyPressed) sees such a tap. So
# every press here is keydown / sleep 0.35s (~21 frames at 60fps) / keyup.
set -u
export PATH="$PATH:/usr/lib/dotnet"
export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
WT=/home/user/wt/w6
export XDG_CONFIG_HOME="$WT/.xdg"
cd "$WT"

TAG="$1"; shift
BINDS="${1:-}"; shift || true

DISP=:97
pkill -f "Xvfb $DISP" >/dev/null 2>&1; sleep 1
Xvfb $DISP -screen 0 1280x800x24 >/dev/null 2>&1 &
sleep 2
export DISPLAY=$DISP

LOG=/tmp/evid_$TAG.log; rm -f "$LOG"
if [ -n "$BINDS" ]; then export SIGHTLINE_KEYBIND="$BINDS"; else unset SIGHTLINE_KEYBIND; fi
SIGHTLINE_KEYLOG=1 ./bin/Release/net8.0/Sightline > "$LOG" 2>&1 &
GPID=$!
sleep 10

WID=$(xdotool search --name "SIGHTLINE" | tail -1)
xdotool windowactivate --sync "$WID" >/dev/null 2>&1
xdotool windowfocus "$WID" >/dev/null 2>&1
sleep 1

hold() { xdotool keydown "$1"; sleep 0.35; xdotool keyup "$1"; sleep 0.4; }

echo "--- entering the TRAINING OP (intro key N, held) ---"
hold n
sleep 3

for k in "$@"; do
  echo "--- holding $k ---"
  hold "$k"
done
sleep 1
kill $GPID 2>/dev/null; sleep 1; kill -9 $GPID 2>/dev/null
pkill -f "Xvfb $DISP" >/dev/null 2>&1
echo "=== KEYBIND + KEYACT lines from $LOG ==="
grep -E "^(KEYBIND|KEYACT)" "$LOG"
