#!/usr/bin/env bash
# P15 THE UNVERIFIED — THE CHUNK RUNNER OF RECORD. Copy THIS one, not w1/run_chunk.sh.
#
#   Usage: run_chunk.sh <tag> <heat> <base> [N]
#   Env:   BIN=<dir containing Sightline>   (REQUIRED — a SNAPSHOT, e.g. runbin/P15)
#          OUT=<dir under docs/measurements> (default docs/measurements/p15)
#          POLICY=sloppy|dumb                 -> single-policy batch (expect N runs, not 2N)
#          ENDLESS=1                          -> SIGHTLINE_BALANCE_ENDLESS instead of _BALANCE
#          HEATPIN=0                          -> deliberately unpinned (the L4/L5 bridge arm)
#          EXTRA="VAR=val VAR2=val2"          -> extra env for the batch
#
# WHAT CHANGED FROM w1/c1/run_chunk.sh, and why (both are P15 findings):
#   * layer (c) no longer hard-codes `runs == N*2`. That marked every legitimate SINGLE-POLICY
#     batch BAD — the exact shape `campaigns[]` was shipped to enable — and it never checked the
#     one thing that decides what the chunk MEANS. check_chunk.py asserts `runs` against the
#     batch's OWN `batch.expectedRuns`, and asserts that the rung and slot base in the artifact
#     are the ones this script exported. A chunk's FILE NAME is now checkable.
#   * exit code 3 is a new refusal: "the batch could not name itself" (an unparseable
#     SIGHTLINE_BALANCE / _HEAT / _BASE). Pre-P15 those fell back SILENTLY — a typo'd `-h4`
#     cycled {0,2,4,6,8} and was archived as a heat-4 chunk with `runs` correct and exit 0.
#
# THE THREE LAYERS ARE OTHERWISE UNCHANGED (CLAUDE.md's measurement contract):
#   (a) rm -f the target FIRST. MANDATORY. A refusing batch leaves an existing JSON byte- and
#       mtime-identical, so without this a stale file satisfies every downstream assertion.
#   (b) the process EXIT CODE. 2 = no display, 3 = bad batch env. Neither can be faked by a file.
#   (c) the artifact's own accounting — check_chunk.py.
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
TAG=$1; H=$2; B=$3; N=${4:-10}
BIN=${BIN:?set BIN to a binary snapshot dir, e.g. BIN=runbin/P15}
OUT=${OUT:-docs/measurements/p15}
POLICY=${POLICY:-}
ENDLESS=${ENDLESS:-}
HEATPIN=${HEATPIN:-}
EXTRA=${EXTRA:-}
mkdir -p "$PWD/.xdg/$TAG"; export XDG_CONFIG_HOME="$PWD/.xdg/$TAG"
mkdir -p "$OUT"
rm -f "$PWD/$OUT/$TAG.json"                                   # (a)

# the batch's own shape, so check_chunk.py can be told what to expect on a pre-P15 binary too
case "$POLICY" in
  sloppy) POLENV="SIGHTLINE_BALANCE_SLOPPY=1"; LEGS=1 ;;
  dumb)   POLENV="SIGHTLINE_BALANCE_DUMB=1";   LEGS=1 ;;
  "")     POLENV="";                            LEGS=2 ;;
  *) echo "run_chunk.sh: POLICY must be sloppy, dumb or empty" >&2; exit 2 ;;
esac
NVAR=SIGHTLINE_BALANCE; [ -n "$ENDLESS" ] && NVAR=SIGHTLINE_BALANCE_ENDLESS
PINARG=--pinned; PINENV=""
if [ "$HEATPIN" = "0" ]; then PINARG=--unpinned; PINENV="SIGHTLINE_HEATPIN=0"; fi

START=$(date +%s)
env $POLENV $PINENV $EXTRA \
  "$NVAR=$N" SIGHTLINE_BALANCE_HEAT="$H" SIGHTLINE_BALANCE_BASE="$B" \
  SIGHTLINE_BALANCE_JSON="$PWD/$OUT/$TAG.json" \
  setsid xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" > "$OUT/$TAG.log" 2>&1 &
APP=$!
RC=""
for _ in $(seq 1 7200); do
  if [ -f "$OUT/$TAG.json" ] && python3 -c "import json,sys;json.load(open('$OUT/$TAG.json'))" 2>/dev/null; then break; fi
  if ! kill -0 $APP 2>/dev/null; then wait $APP 2>/dev/null; RC=$?; break; fi
  sleep 5
done
DONE=$(date +%s)
sleep 3; kill -9 -$APP 2>/dev/null; kill -9 $APP 2>/dev/null; wait $APP 2>/dev/null
sed -n '/BALANCE REPORT/,$p' "$OUT/$TAG.log" > "$OUT/$TAG.report.txt" 2>/dev/null
echo "TIME $TAG data-ready-after=$((DONE-START))s"

# (b) the exit code — the only signal a stale file cannot fake
case "${RC:-}" in
  2) echo "BAD  $TAG — NO DISPLAY. The batch refused and wrote nothing (exit 2). Run under xvfb-run."
     echo "     Any $TAG.json on disk is a STALE file from an earlier chunk. Do not read it."; exit 2 ;;
  3) echo "BAD  $TAG — BAD BATCH ENV (exit 3): the batch could not name itself and wrote nothing."
     sed -n 's/^BALANCE: /       /p' "$OUT/$TAG.log" | head -5; exit 3 ;;
esac
if [ -n "${RC:-}" ] && [ "${RC:-}" != "0" ] && [ ! -f "$OUT/$TAG.json" ]; then
  echo "BAD  $TAG — the batch exited $RC with no JSON written. See $OUT/$TAG.log."; exit 1
fi

# (c) the artifact's own accounting
python3 "$(dirname "$0")/check_chunk.py" "$OUT/$TAG.json" \
  --heat "$H" --base "$B" --expect $((N*LEGS)) $PINARG
