#!/usr/bin/env bash
# C5 THE HARD EDGES — run the house QA sweep with this worktree's isolation exports applied.
#   bash scripts/c5-sweep.sh [--full]
set -u
cd "$(dirname "$0")/.."
mkdir -p "$PWD/.xdg"
export XDG_CONFIG_HOME="$PWD/.xdg"
export SIGHTLINE_BALANCE_JSON="$PWD/balance.json"
bash scripts/qa-sweep.sh "$@"
echo "SWEEP-EXIT=$?"
