#!/usr/bin/env bash
# W1 GATE 3 — the headline. Runs SIGHTLINE_RNGFRAMETEST twice against the SAME binary:
#   (a) SIGHTLINE_FXRNG=0 — Fx re-coupled to the gameplay stream (the pre-W1 behaviour) -> FAIL
#   (b) default           — Fx on Util.FxRng                                            -> PASS
# Plus the raw reproduction the brief asks for: one autoplay seed at two animation speeds.
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg/gate3"
export XDG_CONFIG_HOME="$PWD/.xdg/gate3"
export SIGHTLINE_BALANCE_JSON="$PWD/.xdg/gate3/balance.json"
BIN=${BIN:-bin/Release/net8.0}

echo "=== (a) PRE-FIX behaviour: SIGHTLINE_FXRNG=0 (Fx draws from the gameplay stream) ==="
env SIGHTLINE_FXRNG=0 SIGHTLINE_RNGFRAMETEST=1 \
  xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" 2>/dev/null | grep RNGFRAMETEST

echo
echo "=== (b) POST-FIX behaviour: default (Fx draws from Util.FxRng) ==="
env SIGHTLINE_RNGFRAMETEST=1 \
  xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" 2>/dev/null | grep RNGFRAMETEST

echo
echo "=== raw reproduction: one seed, two animation speeds, PRE-FIX (SIGHTLINE_FXRNG=0) ==="
for sp in 1 20; do
  echo -n "  ANIMSPEED=$sp  "
  env SIGHTLINE_FXRNG=0 SIGHTLINE_AUTOPLAY=1 SIGHTLINE_SEED=99 SIGHTLINE_SMARTPLAY=1 SIGHTLINE_ANIMSPEED=$sp \
    xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" 2>/dev/null | grep -oE "RESULT: (WIN|LOSE|TIMEOUT) mission=[0-9]+"
done
echo "=== raw reproduction: the same pair POST-FIX (default) ==="
for sp in 1 20; do
  echo -n "  ANIMSPEED=$sp  "
  env SIGHTLINE_AUTOPLAY=1 SIGHTLINE_SEED=99 SIGHTLINE_SMARTPLAY=1 SIGHTLINE_ANIMSPEED=$sp \
    xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" 2>/dev/null | grep -oE "RESULT: (WIN|LOSE|TIMEOUT) mission=[0-9]+"
done
