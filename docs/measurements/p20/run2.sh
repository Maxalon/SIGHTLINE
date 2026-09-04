#!/usr/bin/env bash
set -u
cd /home/user/SIGHTLINE
for h in 0 4; do for b in 20 30 40 50 60 70; do
  BIN=runbin/P20 OUT=docs/measurements/p20 bash docs/measurements/p15/run_chunk.sh "fix-h$h-b$b" "$h" "$b" 20 >/dev/null 2>&1
  BIN=runbin/P20 OUT=docs/measurements/p20 EXTRA="SIGHTLINE_STALEGROUND=1" \
    bash docs/measurements/p15/run_chunk.sh "stale-h$h-b$b" "$h" "$b" 20 >/dev/null 2>&1
  echo "done h$h b$b"
done; done
