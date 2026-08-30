#!/usr/bin/env bash
# C5 THE HARD EDGES — a one-line runner for this worktree's self-tests.
#   bash scripts/c5-run.sh SIGHTLINE_FITTEST=1 [MORE=1 ...]
# Applies the house isolation exports (XDG_CONFIG_HOME + SIGHTLINE_BALANCE_JSON inside the
# worktree) and the software-GL env, then runs the Debug binary under xvfb.
set -u
cd "$(dirname "$0")/.."
export PATH="$PATH:/usr/lib/dotnet"
export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg"
export XDG_CONFIG_HOME="$PWD/.xdg"
export SIGHTLINE_BALANCE_JSON="$PWD/balance.json"
env "$@" xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug 2>&1
