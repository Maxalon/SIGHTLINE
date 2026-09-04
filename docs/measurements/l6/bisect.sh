#!/usr/bin/env bash
# L6 — LOCATING THE BRIDGE BREAK, by milestone.
#
# The bridge arm (every post-L5 restore flag set) does NOT reproduce L5, so at least one gameplay
# change since 7180374 has no restore flag. This script finds WHICH MERGE by building each
# first-parent milestone merge between L5's base and HEAD and replaying ONE cell of L5's own ladder
# on it, with that snapshot's OWN DEFAULTS. The first merge whose (slot, policy) outcomes stop
# matching L5's archive is where the chain broke.
#
# 7180374 ITSELF IS THE CONTROL: L5 was measured on it with c1/run_chunk.sh, and this script uses
# p15/run_chunk.sh. If the control does not reproduce L5 exactly, the runner is the difference and
# nothing below means anything.
#
#   bash docs/measurements/l6/bisect.sh
set -u
ROOT=/home/user/wt/ladder-l6
WT=/tmp/claude-0/-home-user-SIGHTLINE/4941b348-6a5b-5729-a65f-d3b7c746412f/scratchpad/bisect-wt
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
CELLS="h0-b0 h0-b30 h4-b0 hR-b0"
SHAS="$*"
cd "$ROOT" || exit 1
for SHA in $SHAS; do
  rm -rf "$WT"; git worktree prune >/dev/null 2>&1
  git worktree add --detach "$WT" "$SHA" >/dev/null 2>&1 || { echo "$SHA: worktree failed"; continue; }
  ( cd "$WT" && dotnet build -c Release >/dev/null 2>&1 ) || { echo "$SHA: BUILD FAILED"; continue; }
  rm -rf runbin/bisect; mkdir -p runbin/bisect; cp -r "$WT/bin/Release/net8.0/." runbin/bisect/
  SUBJ=$(git log --oneline -1 "$SHA" | cut -c1-58)
  line=""
  for C in $CELLS; do
    H=${C%%-*}; H=${H#h}; hh=$H; [ "$H" = "R" ] && hh=-1
    B=${C##*-b}
    BIN=runbin/bisect OUT=docs/measurements/l6/bisect \
      bash docs/measurements/p15/run_chunk.sh "bis-$C" "$hh" "$B" 10 >/dev/null 2>&1
    r=$(python3 - "$C" <<'PY'
import json,sys
c=sys.argv[1]
h=c.split('-')[0][1:]; b=c.split('-b')[1]
try:
    a=json.load(open(f"docs/measurements/l6/bisect/bis-{c}.json"))["pairedPolicy"]["slots"]
    o=json.load(open(f"docs/measurements/l5/L5-h{h}-b{b}.json"))["pairedPolicy"]["slots"]
except Exception as e:
    print("ERR"); raise SystemExit
d=sum((x["greedyWin"]!=y["greedyWin"])+(x["sloppyWin"]!=y["sloppyWin"]) for x,y in zip(a,o))
print(("SAME" if a==o else f"diff{d}"))
PY
)
    line="$line $C=$r"
  done
  echo "$SHA $line   $SUBJ"
done
rm -rf "$WT"; git worktree prune >/dev/null 2>&1
