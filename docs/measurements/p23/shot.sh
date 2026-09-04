#!/usr/bin/env bash
# P23 — THE EVIDENCE IMAGE. The finale's HUD strip at heats 0-8, BEFORE and AFTER, one seed.
# The same capture L7 used (docs/measurements/l7/m6-force-by-heat.png), run twice: once with both
# dials off (= the pre-P23 tree, which is what L7 photographed) and once on the shipped tree.
# The hostile chip is the number in question; the HEAT chip beside it says which rung it is.
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
BIN=${BIN:-runbin/p23}
OUT=docs/measurements/p23
TMP=$(mktemp -d)
mkdir -p "$PWD/.xdg-shot"; export XDG_CONFIG_HOME="$PWD/.xdg-shot"
for ARM in before after; do
  case $ARM in
    before) E="SIGHTLINE_CLAMPLAST=0 SIGHTLINE_FINALESTAT=0" ;;
    after)  E="" ;;
  esac
  for H in 0 1 2 3 4 5 6 7 8; do
    env $E SIGHTLINE_MISSION=6 SIGHTLINE_HEAT=$H SIGHTLINE_SEED=4242 SIGHTLINE_SHOT=760 \
      xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" >/dev/null 2>&1
    cp sightline_shot.png "$TMP/$ARM-h$H.png"
  done
done
python3 "$(dirname "$0")/stack.py" "$TMP" "$OUT/m6-force-by-heat-p23.png"
echo "wrote $OUT/m6-force-by-heat-p23.png"
