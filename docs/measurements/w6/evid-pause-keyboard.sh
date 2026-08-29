#!/usr/bin/env bash
# W6: the pause card's KEYBOARD route, driven live. Every press is held across frames.
set -u
export PATH="$PATH:/usr/lib/dotnet"; export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
WT=/home/user/wt/w6; cd "$WT"; export XDG_CONFIG_HOME="$WT/.xdg"
SET="$XDG_CONFIG_HOME/Sightline/display.json"
rm -rf "$XDG_CONFIG_HOME/Sightline"
DISP=:95; pkill -f "Xvfb $DISP" >/dev/null 2>&1; sleep 1
Xvfb $DISP -screen 0 1280x800x24 >/dev/null 2>&1 & sleep 2
export DISPLAY=$DISP
hold() { xdotool keydown "$1"; sleep 0.3; xdotool keyup "$1"; sleep 0.3; }
./bin/Release/net8.0/Sightline > /tmp/evid_pausekb.log 2>&1 & G=$!
sleep 10
WID=$(xdotool search --name "SIGHTLINE" | tail -1); xdotool windowactivate --sync "$WID" >/dev/null 2>&1; sleep 1
hold n; sleep 3                       # intro -> TRAINING OP (a live mission)
hold Escape; sleep 1                  # the reserved key -> the pause menu
for i in $(seq 1 11); do hold Down; done   # 11 Downs lands on TEXT SIZE (row index 10)
import -window root /tmp/pausekb_sel.png 2>/dev/null
hold Return                           # activate it: CycleUiScale 100% -> 110%
sleep 1
import -window root /tmp/pausekb_after.png 2>/dev/null
hold Down; hold Down                  # -> MUTE (11), MASTER fader (12)
hold Left; hold Left                  # nudge MASTER down 2 x 5%
sleep 1
kill $G 2>/dev/null; sleep 1; kill -9 $G 2>/dev/null
pkill -f "Xvfb $DISP" >/dev/null 2>&1
echo "### display.json after the keyboard-only session:"
python3 -c "import json;d=json.load(open('$SET'));print('UiScaleIdx =',d['UiScaleIdx'],' VolMaster =',round(d['VolMaster'],3))"
