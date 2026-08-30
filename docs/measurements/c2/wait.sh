#!/usr/bin/env bash
# Block until the C2 ladder has produced all 48 chunks (or a BAD line appears).
F=/home/user/SIGHTLINE/.claude/worktrees/agent-a1c26e3c14d98312b/docs/measurements/c2/ladder.progress.txt
while true; do
  n=$(grep -c OK "$F" 2>/dev/null || echo 0)
  b=$(grep -c BAD "$F" 2>/dev/null || echo 0)
  if [ "$n" -ge 48 ] || [ "$b" -gt 0 ]; then break; fi
  sleep 20
done
echo "LADDER DONE: ${n} OK, ${b} BAD"
