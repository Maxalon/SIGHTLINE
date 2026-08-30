#!/usr/bin/env bash
# W1: scripts/qa-sweep.sh with the house ISOLATION exports applied (CLAUDE.md "Harness isolation").
# qa-sweep.sh deliberately does not set them — it inherits them — and several agents share this
# container, so running the sweep without them lets the persistence tests stash and restore the
# SHARED ~/.config/Sightline out from under another agent's run.
set -u
cd "$(dirname "$0")/../../.." || exit 1
mkdir -p "$PWD/.xdg/sweep"
export XDG_CONFIG_HOME="$PWD/.xdg/sweep"
export SIGHTLINE_BALANCE_JSON="$PWD/.xdg/sweep/balance.json"
exec bash scripts/qa-sweep.sh "$@"
