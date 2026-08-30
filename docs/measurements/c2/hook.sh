#!/usr/bin/env bash
# C2 helper: run one SIGHTLINE_* hook under xvfb with this worktree's isolation exports.
#   bash docs/measurements/c2/hook.sh DECLINETEST=1 [MORE_ENV=...]
# Everything before the command is passed through as SIGHTLINE_<name>=<value>.
set -u
# Repo root is derived from THIS SCRIPT's location (docs/measurements/c2/), not hardcoded.
# The first version pinned an agent worktree path and `exit 1`d anywhere else, which made the
# README's "Reproducing" block dead on every other checkout. Override with ROOT=... if needed.
ROOT=${ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)}
cd "$ROOT" || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$ROOT/.xdg"
export XDG_CONFIG_HOME="$ROOT/.xdg" SIGHTLINE_BALANCE_JSON="$ROOT/balance.json"
ENVS=()
for kv in "$@"; do ENVS+=("SIGHTLINE_$kv"); done
env "${ENVS[@]}" xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug 2>/dev/null
