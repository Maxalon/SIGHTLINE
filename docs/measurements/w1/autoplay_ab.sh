#!/usr/bin/env bash
# W1/4: the AUTOPLAY smoke-path A/B. Same binary, same seed, so post-W1/3 the DICE are identical
# and only the GL work differs — SIGHTLINE_BALANCE_DRAW=1 restores the pre-W1 per-frame clear.
# The RESULT line's frame= must match between the two legs; only the wall-clock may move.
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg/ab"
export XDG_CONFIG_HOME="$PWD/.xdg/ab"
export SIGHTLINE_BALANCE_JSON="$PWD/.xdg/ab/balance.json"
BIN=${BIN:-bin/Release/net8.0}
for seed in 99 4242; do
  for draw in 1 0; do
    S=$(date +%s.%N)
    R=$(env ${draw:+SIGHTLINE_BALANCE_DRAW=$draw} SIGHTLINE_AUTOPLAY=1 SIGHTLINE_SMARTPLAY=1 SIGHTLINE_SEED=$seed \
        xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" 2>/dev/null \
        | grep -oE "RESULT: (WIN|LOSE|TIMEOUT) mission=[0-9]+ frame=[0-9]+" | tail -1)
    E=$(date +%s.%N)
    LBL=$([ "$draw" = 1 ] && echo "DRAW=1 (pre-W1 GL clear)" || echo "default  (PollInputEvents)")
    printf "seed%-6s %-28s %6.1fs  %s\n" "$seed" "$LBL" "$(echo "$E - $S" | bc)" "$R"
  done
done
