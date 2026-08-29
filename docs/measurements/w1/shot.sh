#!/usr/bin/env bash
# W1: a screenshot with the FX layer staged — unit recoil/flinch/lean, scorch decals and live
# fire/smoke hazards, all of which run through Fx and therefore through the newly-routed
# Util.FxRng draws. The eyes-only check that moving all 29 Fx draw sites did not break anything.
# NOTE: do NOT combine SIGHTLINE_SHOT with SIGHTLINE_AUTOPLAY here — that combination draws the
# full board every frame for an entire campaign under llvmpipe (tens of minutes).
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg/shot"
export XDG_CONFIG_HOME="$PWD/.xdg/shot"
export SIGHTLINE_BALANCE_JSON="$PWD/.xdg/shot/balance.json"
env SIGHTLINE_SHOT="${1:-120}" SIGHTLINE_UNITFX=1 SIGHTLINE_HAZARD=1 SIGHTLINE_SEED="${2:-99}" \
  xvfb-run -a -s "-screen 0 1280x800x24" bin/Release/net8.0/Sightline 2>/dev/null | tail -2
ls -la sightline_shot.png
