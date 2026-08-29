#!/usr/bin/env bash
# X2 round driver: run a list of "<tag> <heat> <base>" chunks with bounded parallelism.
# Usage: drive.sh <queuefile> [PAR]   ; per-round env (MIX/TOUGH/TRIM/BIN/...) is inherited.
set -u
Q=$(readlink -f "$1"); PAR=${2:-3}
cd "$(dirname "$(readlink -f "$0")")" || exit 1
xargs -a "$Q" -P "$PAR" -L 1 bash -c './run_chunk.sh $0 $1 $2'
echo "QUEUE DONE $Q"
