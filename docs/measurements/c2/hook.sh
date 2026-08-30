#!/usr/bin/env bash
# C2 helper: run one SIGHTLINE_* hook under xvfb with this worktree's isolation exports.
#   bash docs/measurements/c2/hook.sh DECLINETEST=1 [MORE_ENV=...]
# Everything before the command is passed through as SIGHTLINE_<name>=<value>.
set -u
ROOT=/home/user/SIGHTLINE/.claude/worktrees/agent-a1c26e3c14d98312b
cd "$ROOT" || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$ROOT/.xdg"
export XDG_CONFIG_HOME="$ROOT/.xdg" SIGHTLINE_BALANCE_JSON="$ROOT/balance.json"
ENVS=()
for kv in "$@"; do ENVS+=("SIGHTLINE_$kv"); done
env "${ENVS[@]}" xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug 2>/dev/null
