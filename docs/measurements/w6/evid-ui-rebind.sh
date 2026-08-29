#!/usr/bin/env bash
# W6 measured evidence, part 2: rebind through the REAL UI, prove it PERSISTS, then prove the
# persisted key fires on a FRESH launch. Same hold-across-a-frame method.
set -u
export PATH="$PATH:/usr/lib/dotnet"
export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
WT=/home/user/wt/w6; cd "$WT"
export XDG_CONFIG_HOME="$WT/.xdg"
SET="$XDG_CONFIG_HOME/Sightline/display.json"

DISP=:96
pkill -f "Xvfb $DISP" >/dev/null 2>&1; sleep 1
Xvfb $DISP -screen 0 1280x800x24 >/dev/null 2>&1 & sleep 2
export DISPLAY=$DISP
hold() { xdotool keydown "$1"; sleep 0.35; xdotool keyup "$1"; sleep 0.4; }

echo "### settings BEFORE:"; [ -f "$SET" ] && python3 -c "import json,sys;print(json.load(open('$SET')).get('Keys'))" || echo "(no settings file yet)"

echo "### launch 1 - rebind FIRE through the CONTROLS screen"
SIGHTLINE_KEYLOG=1 ./bin/Release/net8.0/Sightline > /tmp/evid_ui1.log 2>&1 & G1=$!
sleep 10
WID=$(xdotool search --name "SIGHTLINE" | tail -1); xdotool windowactivate --sync "$WID" >/dev/null 2>&1; sleep 1
hold o                          # intro -> CONTROLS
sleep 1
xdotool mousemove 300 288; sleep 0.5; xdotool mousedown 1; sleep 0.35; xdotool mouseup 1; sleep 0.8   # click the FIRE row (held: a same-frame click is invisible to IsMouseButtonPressed, exactly like a key tap)
xdotool mousemove 300 288 ; sleep 0.2
import -window root /tmp/ui_capture.png 2>/dev/null
hold q                          # bind it to Q
sleep 1
import -window root /tmp/ui_bound.png 2>/dev/null
sleep 1; kill $G1 2>/dev/null; sleep 1; kill -9 $G1 2>/dev/null; sleep 1

echo "### settings AFTER the UI rebind:"; python3 -c "import json;print(repr(json.load(open('$SET')).get('Keys')))"

echo "### launch 2 - FRESH process, no env override: does the PERSISTED key fire?"
SIGHTLINE_KEYLOG=1 ./bin/Release/net8.0/Sightline > /tmp/evid_ui2.log 2>&1 & G2=$!
sleep 10
WID=$(xdotool search --name "SIGHTLINE" | tail -1); xdotool windowactivate --sync "$WID" >/dev/null 2>&1; sleep 1
hold n; sleep 3                 # into the drill
hold 1                          # the OLD FIRE key
hold q                          # the NEW FIRE key
hold 2                          # an untouched control, as a control
sleep 1; kill $G2 2>/dev/null; sleep 1; kill -9 $G2 2>/dev/null
pkill -f "Xvfb $DISP" >/dev/null 2>&1
echo "### press order was: 1 (old FIRE)  q (new FIRE)  2 (untouched OVERWATCH)"
grep -E "^KEYACT" /tmp/evid_ui2.log
