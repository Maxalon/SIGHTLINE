#!/usr/bin/env bash
# X3 round driver: run "<tag> <heat> <base>" lines with bounded parallelism.
# Usage: drive.sh <queuefile> [PAR]   ; per-round env (BIN/LEVERS) is inherited.
set -u
Q=$(readlink -f "$1"); PAR=${2:-4}
cd "$(dirname "$(readlink -f "$0")")" || exit 1
xargs -a "$Q" -P "$PAR" -L 1 bash -c './run_chunk.sh $0 $1 $2'
echo "QUEUE DONE $Q"
