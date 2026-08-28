#!/usr/bin/env bash
# QA sweep for SIGHTLINE — build + full self-test suite + autoplay smoke.
# NOT CI (never wired to Actions); run BY HAND from a session. Documented harness
# quirk: piping an xvfb-run child through $(...) / a for-loop silently drops its
# stdout, so every test is a direct `CMD | grep` statement below.
#
#   bash scripts/qa-sweep.sh          # 42 self-tests + autoplay x3   (~2 min)
#   bash scripts/qa-sweep.sh --full   # + PAIRTEST                    (~2 min 40 s)
#
# COUNT NOTE (C1): the footer used to claim 41 self-tests and the sweep actually ran 42 —
# an off-by-one that predates VOICETEST. Counted by hand from the echo lines: 42 before this
# wave, 43 with VOICETEST. Corrected below rather than carried forward.
#
# RUN --full BEFORE MERGING. PAIRTEST (38 s measured) is the CRN-pairing identity check
# that every paired measurement in this project rests on: two identical greedy legs on the
# same seed must produce byte-identical outcomes. If it regresses, the flywheel's numbers
# are meaningless — so a quick sweep may skip it, but a merge may not.
#
# ISOLATION: the persistence tests stash-and-restore the real user-data dir
# (~/.config/Sightline) and SIGHTLINE_BALANCE writes a shared /tmp/balance.json. When more
# than one agent shares a container, export XDG_CONFIG_HOME=<your worktree>/.xdg (the
# directory must already EXIST) and SIGHTLINE_BALANCE_JSON=<your worktree>/balance.json
# before running this. Both are inherited from your shell; this script does not set them.
set -u
export PATH="$PATH:/usr/lib/dotnet"
export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
cd "$(dirname "$0")/.."

FULL=0
[ "${1:-}" = "--full" ] && FULL=1

run() { xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug 2>/dev/null; }

echo "=== BUILD (Release) ==="
dotnet build -c Release 2>&1 | grep -E "error|Error|Warning\(s\)|Build succeeded" | head -20

echo "=== SELF-TESTS ==="
echo -n "DKTEST     : "; SIGHTLINE_DKTEST=1     run | grep -oE "DKTEST: (PASS|FAIL)" | head -1
echo -n "RESCUETEST : "; SIGHTLINE_RESCUETEST=1 run | grep -oE "RESCUETEST: (PASS|FAIL)" | head -1
echo -n "STAGGERTEST: "; SIGHTLINE_STAGGERTEST=1 run | grep -oE "STAGGERTEST: (PASS|FAIL)" | head -1
echo -n "MORALETEST : "; SIGHTLINE_MORALETEST=1 run | grep -oE "MORALETEST: (PASS|FAIL)" | head -1
echo -n "BEACONTEST : "; SIGHTLINE_BEACONTEST=1 run | grep -oE "BEACONTEST: (PASS|FAIL)" | head -1
echo -n "COMBATTEST : "; SIGHTLINE_COMBATTEST=1 run | grep -oE "COMBATTEST: (PASS|FAIL)" | head -1
echo -n "SAVETEST   : "; SIGHTLINE_SAVETEST=1  run | grep -oE "SAVETEST: (PASS|FAIL)" | head -1
echo -n "AITEST     : "; SIGHTLINE_AITEST=1    run | grep -oE "AITEST: (PASS|FAIL)" | head -1
echo -n "ITEMTEST   : "; SIGHTLINE_ITEMTEST=1  run | grep -oE "ITEMTEST: (PASS|FAIL)" | head -1
echo -n "STATUSTEST : "; SIGHTLINE_STATUSTEST=1 run | grep -oE "STATUSTEST: (PASS|FAIL)" | head -1
echo -n "COVERTEST  : "; SIGHTLINE_COVERTEST=1 run | grep -oE "COVERTEST: (PASS|FAIL)" | head -1
echo -n "TRAITTEST  : "; SIGHTLINE_TRAITTEST=1 run | grep -oE "TRAITTEST: (PASS|FAIL)" | head -1
echo -n "WOUNDTEST  : "; SIGHTLINE_WOUNDTEST=1 run | grep -oE "WOUNDTEST: (PASS|FAIL)" | head -1
echo -n "CDTEST     : "; SIGHTLINE_CDTEST=1    run | grep -oE "CDTEST: (PASS|FAIL)" | head -1
echo -n "FIELDTEST  : "; SIGHTLINE_FIELDTEST=1 run | grep -oE "FIELDTEST: (PASS|FAIL)" | head -1
echo -n "SIEGETEST  : "; SIGHTLINE_SIEGETEST=1 run | grep -oE "SIEGETEST: (PASS|FAIL)" | head -1
echo -n "EVENTTEST  : "; SIGHTLINE_EVENTTEST=1 run | grep -oE "EVENTTEST: (PASS|FAIL)" | head -1
echo -n "VETTEST    : "; SIGHTLINE_VETTEST=1   run | grep -oE "VETTEST: (PASS|FAIL)" | head -1
echo -n "OWTEST     : "; SIGHTLINE_OWTEST=1    run | grep -oE "OWTEST: (PASS|FAIL)" | head -1
echo -n "SCARTEST   : "; SIGHTLINE_SCARTEST=1  run | grep -oE "SCARTEST: (PASS|FAIL)" | head -1
echo -n "CONTRACTTEST: "; SIGHTLINE_CONTRACTTEST=1 run | grep -oE "CONTRACTTEST: (PASS|FAIL)" | head -1
echo -n "SHOVETEST  : "; SIGHTLINE_SHOVETEST=1 run | grep -oE "SHOVETEST: (PASS|FAIL)" | head -1
echo -n "CONCEALTEST: "; SIGHTLINE_CONCEALTEST=1 run | grep -oE "CONCEALTEST: (PASS|FAIL)" | head -1
echo -n "HAZARDTEST : "; SIGHTLINE_HAZARDTEST=1 run | grep -oE "HAZARDTEST: (PASS|FAIL)" | head -1
echo -n "BENCHTEST  : "; SIGHTLINE_BENCHTEST=1 run | grep -oE "BENCHTEST: (PASS|FAIL)" | head -1
echo -n "DRAFTTEST  : "; SIGHTLINE_DRAFTTEST=1 run | grep -oE "DRAFTTEST: (PASS|FAIL)" | head -1
echo -n "METATEST   : "; SIGHTLINE_METATEST=1  run | grep -oE "METATEST: (PASS|FAIL)" | head -1
echo -n "CODEXTEST  : "; SIGHTLINE_CODEXTEST=1 run | grep -oE "CODEXTEST: (PASS|FAIL)" | head -1
echo -n "VOICETEST  : "; SIGHTLINE_VOICETEST=1 run | grep -oE "VOICETEST: (PASS|FAIL)" | head -1
echo -n "MODETEST   : "; SIGHTLINE_MODETEST=1  run | grep -oE "MODETEST: (PASS|FAIL)" | head -1
echo -n "HORDETEST  : "; SIGHTLINE_HORDETEST=1 run | grep -oE "HORDETEST: (PASS|FAIL)" | head -1
echo -n "DEATHTEST  : "; SIGHTLINE_DEATHTEST=1 run | grep -oE "DEATHTEST: (PASS|FAIL)" | head -1
echo -n "HEATLADDERTEST: "; SIGHTLINE_HEATLADDERTEST=1 run | grep -oE "HEATLADDERTEST: (PASS|FAIL)" | head -1
echo -n "SNAPTEST   : "; SIGHTLINE_SNAPTEST=1  run | grep -oE "SNAPTEST: (PASS|FAIL)" | head -1
echo -n "AUDIOTEST  : "; SIGHTLINE_AUDIOTEST=1 run | grep -oE "AUDIOTEST: (PASS|FAIL)" | head -1
echo -n "AMBIENTTEST: "; SIGHTLINE_AMBIENTTEST=1 run | grep -oE "AMBIENTTEST: (PASS|FAIL)" | head -1
# Q1: the no-two-units-on-one-tile invariant. Drives 16 real missions (~70s), so it goes last.
echo -n "STACKTEST  : "; SIGHTLINE_STACKTEST=1 run | grep -oE "STACKTEST: (PASS|FAIL)" | head -1
# FUL-era hooks the sweep used to omit entirely — the bleed-out state machine, the pikeman,
# pod sizing/linking, and the content-exposure invariant. (EXPOSURETEST and FUL11PROBE print
# "NAME PASS" with no colon; the others use "NAME: PASS".)
echo -n "DOWNTEST   : "; SIGHTLINE_DOWNTEST=1  run | grep -oE "DOWNTEST: (PASS|FAIL)" | head -1
echo -n "PIKETEST   : "; SIGHTLINE_PIKETEST=1  run | grep -oE "PIKETEST: (PASS|FAIL)" | head -1
echo -n "PODTEST    : "; SIGHTLINE_PODTEST=1   run | grep -oE "PODTEST: (PASS|FAIL)" | head -1
echo -n "EXPOSURETEST: "; SIGHTLINE_EXPOSURETEST=1 run | grep -oE "EXPOSURETEST (PASS|FAIL)" | head -1
echo -n "FUL11PROBE : "; SIGHTLINE_FUL11PROBE=40 run | grep -oE "FUL11PROBE (PASS|FAIL)" | head -1

if [ "$FULL" = 1 ]; then
  # ~38 s: the CRN identity check. Skipped by default so the sweep stays a quick loop;
  # REQUIRED before a merge (see the header).
  echo -n "PAIRTEST   : "; SIGHTLINE_PAIRTEST=1 run | grep -oE "PAIRTEST: (PASS|FAIL)" | tail -1
else
  echo "PAIRTEST   : SKIPPED (re-run with --full; required before merging)"
fi

echo "=== AUTOPLAY x3 ==="
echo -n "run1: "; SIGHTLINE_AUTOPLAY=1 run | grep -oE "RESULT: (WIN|LOSE|TIMEOUT) mission=[0-9]+" | head -1
echo -n "run2: "; SIGHTLINE_AUTOPLAY=1 run | grep -oE "RESULT: (WIN|LOSE|TIMEOUT) mission=[0-9]+" | head -1
echo -n "run3: "; SIGHTLINE_AUTOPLAY=1 run | grep -oE "RESULT: (WIN|LOSE|TIMEOUT) mission=[0-9]+" | head -1
echo "=== DONE ==="
echo "(43 self-tests exist; this sweep ran $([ "$FULL" = 1 ] && echo 43 || echo 42). Every line above"
echo " must read PASS, and every autoplay must read WIN or LOSE — never TIMEOUT, never blank.)"
