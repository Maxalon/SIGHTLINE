#!/usr/bin/env bash
# P24 "THE TOP OF THE LADDER" (PROGRAM PARALLAX) — THE ROUND.
#
#   bash docs/measurements/p24/run_round.sh              # 2 arms x 6 rungs x 16 CRN slot bases
#   RUNGS="0 4 6 8" BASES="$EXT" bash .../run_round.sh    # the out-of-sample extension
#
# ONE LEVER. `Mission.HostileAimTrim` 0 -> 5: five flat points off every hostile's aim, in the one
# funnel (`Mission.MakeHostile`) every hostile in the game is built through.
#
#   base   SIGHTLINE_AIMTRIM=0    the pre-P24 tree — the control, and the wave's restore flag
#   aim    (nothing)              the shipped tree (Mission.HostileAimTrim = 5)
#
# Both arms come off ONE binary snapshot (runbin/p24), so the only difference between them is the
# dial. Both arms share the 16 slot bases, so every contrast is CRN-paired campaign-for-campaign.
#
# THERE IS NO INERTNESS CONTROL IN THIS ROUND, and that is a property of the lever, not an
# oversight: a LEVEL lever moves every rung by construction. What plays the inertness role is the
# BRIDGE — the base arm against the P23 archive's AB arm, which must reproduce it campaign for
# campaign (bridge.py). That is also what proves the restore flag is a true restoration and that
# this wave's non-lever edits (FORCETEST leg (H), the `levers` block in the balance JSON) are
# stream-neutral.
#
# ── THE PREDICTION, WRITTEN BEFORE THE ROUND RAN (L7's rule: the prediction goes above the data) ──
#   1. DIRECTION: every rung rises. An enemy-accuracy give-back cannot make the game harder.
#   2. SIZE: X2 measured this exact dial's dose-response at h0 on the pre-W1 stream (n=40/arm,
#      docs/measurements/x2/): 35.0 baseline, 42.5 at dose 5, 50.0 at dose 10. So the prior for h0
#      is about +7.5 — quoted as a PRIOR from an incomparable stream, not a forecast with a CI.
#   3. PROFILE: the effect should be SMALLEST at the ends (RECRUIT compresses against 100%, h8
#      against the floor) and largest through the middle rungs.
#   4. SHAPE: RECRUIT->h0 (29.0 points against a band that implies 20) should SHRINK; h6->h8
#      (4.4 against a band that implies 10) should GROW. Both are movements toward the band's own
#      implied shape, and neither is what the lever was chosen for.
#   5. THE TEST OF THE WAVE: h0 and h6 clear their floors (47 / 12) WITHOUT h2 or RECRUIT crossing
#      their ceilings (48 / 83) and WITHOUT h8 falling (it cannot go below 5 and stay in band).
#      A rung that ends out of band is a finding to publish, not a reason to re-pick the constant.
#
# Runner of record: docs/measurements/p15/run_chunk.sh — rm -f, exit code, then check_chunk.py.
# P24 adds a FOURTH layer: the artifact's own `levers.aimTrim` must equal the arm this chunk was
# launched as. Until this wave the only record of a chunk's ARM was its FILE NAME.
set -u
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ROOT=$(cd "$HERE/../../.." && pwd)
export BIN=${BIN:-runbin/p24} OUT=docs/measurements/p24
BASES=${BASES:-"0 10 20 30 40 50 60 70 80 90 100 110 120 130 140 150"}
RUNGS=${RUNGS:-"-1 0 2 4 6 8"}
ARMS=${ARMS:-"base aim"}
JOBS=${JOBS:-4}
N=${N:-10}

mkdir -p "$ROOT/$OUT"
one() {   # arm rung base
  local ARM=$1 H=$2 B=$3 E TAG want
  case "$ARM" in
    base) E="SIGHTLINE_AIMTRIM=0"; want=0 ;;
    aim)  E="";                    want=5 ;;
    *) echo "unknown arm $ARM" >&2; return 2 ;;
  esac
  TAG="P24-$ARM-h$H-b$B"
  EXTRA="$E" bash "$HERE/../p15/run_chunk.sh" "$TAG" "$H" "$B" "$N"
  local rc=$?
  [ $rc -ne 0 ] && return $rc
  # (d) the ARM, asserted against the artifact rather than against the file name.
  python3 - "$ROOT/$OUT/$TAG.json" "$want" "$TAG" <<'PY'
import json, sys
j = json.load(open(sys.argv[1])); want = int(sys.argv[2]); tag = sys.argv[3]
got = (j.get("levers") or {}).get("aimTrim")
if got != want:
    print(f"BAD  {tag} — WRONG ARM: levers.aimTrim={got} want={want}"); sys.exit(4)
print(f"     ARM   levers.aimTrim = {got}")
PY
}
jobs_running=0
for ARM in $ARMS; do
  for H in $RUNGS; do
    for B in $BASES; do
      ( one "$ARM" "$H" "$B" > "$ROOT/$OUT/P24-$ARM-h$H-b$B.chunk.txt" 2>&1 ) &
      jobs_running=$((jobs_running+1))
      if [ "$jobs_running" -ge "$JOBS" ]; then wait -n; jobs_running=$((jobs_running-1)); fi
    done
  done
done
wait
cat "$ROOT/$OUT"/P24-*-h*-b*.chunk.txt >> "$ROOT/$OUT/P24-chunks.txt"
rm -f "$ROOT/$OUT"/P24-*-h*-b*.chunk.txt
sort -o "$ROOT/$OUT/P24-chunks.txt" "$ROOT/$OUT/P24-chunks.txt"
ok=$(grep -c '^OK' "$ROOT/$OUT/P24-chunks.txt"); bad=$(grep -c '^BAD' "$ROOT/$OUT/P24-chunks.txt")
echo "CHUNKS (P24, cumulative): ok=$ok bad=$bad"
exit $(( bad != 0 ))
