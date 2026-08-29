#!/usr/bin/env bash
# W1 helper: run one env-gated hook against a binary with the house isolation exports applied.
#   Usage: hook.sh <BINDIR> VAR=val [VAR=val ...] -- (all vars are passed to the process)
# Example: hook.sh bin/Release/net8.0 SIGHTLINE_SAVETEST=1
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg/hook"
export XDG_CONFIG_HOME="$PWD/.xdg/hook"
export SIGHTLINE_BALANCE_JSON="$PWD/.xdg/hook/balance.json"
BIN=$1; shift
exec env "$@" "$BIN/Sightline"
