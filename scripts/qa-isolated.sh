#!/usr/bin/env bash
# Run this repo's self-tests with the HOUSE ISOLATION EXPORTS already applied.
#
#   bash scripts/qa-isolated.sh --sweep [--full]     # the whole QA sweep
#   bash scripts/qa-isolated.sh SIGHTLINE_FITTEST=1  # one hook (any number of VAR=VAL pairs)
#
# WHY THIS EXISTS (C5). CLAUDE.md and qa-sweep.sh's header both say every shell must export
# XDG_CONFIG_HOME and SIGHTLINE_BALANCE_JSON into the worktree, because several agents share this
# container and the persistence self-tests stash-and-restore the real user-data dir. That is four
# lines of setup a reader has to remember, per shell, and one wave has already lost a measurement
# round to a shared path. One script, in the repo, that cannot be forgotten or overwritten.
set -u
cd "$(dirname "$0")/.."
export PATH="$PATH:/usr/lib/dotnet"
export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg"                    # must EXIST: an absent dir makes saves land in ./Sightline
export XDG_CONFIG_HOME="$PWD/.xdg"
export SIGHTLINE_BALANCE_JSON="$PWD/balance.json"

if [ "${1:-}" = "--sweep" ]; then
  shift
  bash scripts/qa-sweep.sh "$@"
  echo "SWEEP-EXIT=$?"
else
  env "$@" xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug 2>&1
fi
