#!/usr/bin/env bash
# QA sweep for SIGHTLINE — build + full self-test suite + autoplay smoke.
# NOT CI (never wired to Actions); run BY HAND from a session. Documented harness
# quirk: piping an xvfb-run child through $(...) / a for-loop silently drops its
# stdout, so every test is a direct `CMD | grep` statement below.
#
#   bash scripts/qa-sweep.sh          # 50 self-tests + autoplay x3   (~2 min)
#   bash scripts/qa-sweep.sh --full   # all 51 (adds PAIRTEST)         (~2 min 40 s)
#
# COUNT NOTE: this footer has been wrong three times now. C1 found it claiming 41 while running
# 42; the W5/C1 integration then had two waves bumping it from different bases; and TRUE BAND
# found the "derived" recipe itself was wrong - it grepped only `...TEST|FUL11PROBE`, so it never
# counted AUDIOGATE, and it counted over THIS FILE while the label said "exist". Both halves are
# now derived, and from the right place. If you add a test, re-run BOTH:
#   exist (in src/):   grep -ohE 'SIGHTLINE_[A-Z0-9_]+(TEST|GATE)' src/*.cs | sort -u | wc -l    # +1 for FUL11PROBE
#   run   (this file): grep -oE 'SIGHTLINE_[A-Z0-9_]+(TEST|GATE|PROBE)=' scripts/qa-sweep.sh | sort -u | wc -l
# The two must be EQUAL - if `run` is smaller the COVERAGE GUARD below will name the gap.
# `--full` runs all of them; the default skips exactly one (PAIRTEST).
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
echo -n "BANDTEST   : "; SIGHTLINE_BANDTEST=1 run | grep -oE "BANDTEST: (PASS|FAIL)" | head -1
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
echo -n "AUDIOGATE  : "; SIGHTLINE_AUDIOGATE=1 run | grep -oE "AUDIOGATE: (PASS|FAIL)" | head -1
# RESONANCE A3: the AUDIO CHECK audition screen's listing/label/measurement contract.
echo -n "AUDITIONTEST: "; SIGHTLINE_AUDITIONTEST=1 run | grep -oE "AUDITIONTEST: (PASS|FAIL)" | head -1
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
# RESONANCE W5: the RECRUIT rung + the comfort settings (anim speed / UI text scale).
echo -n "ONRAMPTEST : "; SIGHTLINE_ONRAMPTEST=1 run | grep -oE "ONRAMPTEST: (PASS|FAIL)" | head -1
echo -n "OPENERTEST : "; SIGHTLINE_OPENERTEST=1 run | grep -oE "OPENERTEST: (PASS|FAIL)" | head -1
# RESONANCE T1/T2: the onboarding contract and the incoming-fire forecast. These two EXISTED
# but were never run by this sweep - the integration review caught it. THREATTEST prints
# "NAME PASS" with no colon, like EXPOSURETEST.
echo -n "TUTTEST    : "; SIGHTLINE_TUTTEST=1  run | grep -oE "TUTTEST: (PASS|FAIL)" | head -1
echo -n "THREATTEST : "; SIGHTLINE_THREATTEST=1 run | grep -oE "THREATTEST (PASS|FAIL)" | head -1
# R2 FIX 1: the nobody-is-walled-out geometry invariant (all 4 deployment shapes x 8 objectives
# x 2 heats, thousands of fresh boards). ~25 s.
echo -n "GEOMTEST   : "; SIGHTLINE_GEOMTEST=1 run | grep -oE "GEOMTEST: (PASS|FAIL)" | head -1
# W1 TRUE INSTRUMENT: the autopilot's ROUTE through the campaign DAG (the sampling frame every
# published balance number was drawn through) and the frame/RNG independence of gameplay.
echo -n "ROUTETEST  : "; SIGHTLINE_ROUTETEST=1 run | grep -oE "ROUTETEST: (PASS|FAIL)" | tail -1
# ~20 s: gameplay must be a function of the SEED, not of the frame rate, the animation-speed
# setting or the screen-shake comfort toggle. 16 campaigns; FAILs under SIGHTLINE_FXRNG=0.
# Its PHASE 2 (render purity) is the real guard for "presentation never draws from Util.Rng":
# it asserts that constructing a Unit and drawing 30 real frames both leave the shared stream
# untouched, and proves the probe sensitive with a deliberate draw.
echo -n "RNGFRAMETEST: "; SIGHTLINE_RNGFRAMETEST=1 run | grep -oE "RNGFRAMETEST: (PASS|FAIL)" | tail -1

# W1 STATIC BACKSTOP (free, and it runs even when the binary will not build): Fx.cs is the
# FX layer and must contain zero draws from the shared gameplay stream. HONEST SCOPE: this
# grep would NOT have caught the W1 review's Bob defect, which lived in Unit.cs — only
# RNGFRAMETEST phase 2 catches that class. This is a cheap tripwire on the one file whose
# entire job is presentation, not a substitute for the runtime assertion.
# COMMENTS ARE STRIPPED FIRST. The first version of this check grepped the raw file and FAILed on
# Fx.cs:653, a doc comment reading "deterministic hash (NOT Util.Rng)" — a tripwire that fires on
# the word rather than the call is worse than no tripwire, and it would have been ignored by the
# second wave to see it.
_fxhits=$(sed -E 's,//.*,,' src/Fx.cs | grep -nE 'Util\.(RandF|RandInt|RandRange|Roll|Choice)\(|Util\.Rng')
echo -n "FXSTREAM   : "; if [ -n "$_fxhits" ]; then
  echo "FXSTREAM: FAIL (src/Fx.cs draws from the shared gameplay Util.Rng — use Util.FxRand*)"
  echo "$_fxhits" | sed 's/^/     /'
else echo "FXSTREAM: PASS"; fi

if [ "$FULL" = 1 ]; then
  # ~38 s: the CRN identity check. Skipped by default so the sweep stays a quick loop;
  # REQUIRED before a merge (see the header).
  echo -n "PAIRTEST   : "; SIGHTLINE_PAIRTEST=1 run | grep -oE "PAIRTEST: (PASS|FAIL)" | tail -1
else
  echo "PAIRTEST   : SKIPPED (re-run with --full; required before merging)"
fi

# COVERAGE GUARD: this sweep's test list has drifted from src/ twice (a hand-maintained
# counter said 41 while 42 ran; a later recount still missed TUTTEST and THREATTEST). Derive
# it instead of trusting it - if a self-test exists in src/ and is not invoked above, say so.
_missing=$(comm -23 \
  <(grep -ohE 'SIGHTLINE_[A-Z0-9_]+(TEST|GATE)' src/*.cs | sort -u) \
  <(grep -ohE 'SIGHTLINE_[A-Z0-9_]+(TEST|GATE)' scripts/qa-sweep.sh | sort -u))
if [ -n "$_missing" ]; then
  echo "!! COVERAGE GAP - these self-tests exist in src/ but this sweep never runs them:"
  echo "$_missing" | sed 's/^/     /'
fi

echo "=== AUTOPLAY x3 ==="
echo -n "run1: "; SIGHTLINE_AUTOPLAY=1 run | grep -oE "RESULT: (WIN|LOSE|TIMEOUT) mission=[0-9]+" | head -1
echo -n "run2: "; SIGHTLINE_AUTOPLAY=1 run | grep -oE "RESULT: (WIN|LOSE|TIMEOUT) mission=[0-9]+" | head -1
echo -n "run3: "; SIGHTLINE_AUTOPLAY=1 run | grep -oE "RESULT: (WIN|LOSE|TIMEOUT) mission=[0-9]+" | head -1
echo "=== DONE ==="
# W1: this counter is now DERIVED AT RUNTIME, not hand-maintained. It has been wrong four times
# (41-while-42; the W5/C1 double bump; TRUE BAND finding the "derived" recipe itself miscounted;
# and W1 hardcoding a fresh number that TRUE BAND's BANDTEST immediately invalidated). Two waves
# in a row wrote down the right RECIPE and then pasted its answer as a literal, which is how it
# drifts. So run the recipe instead — it costs one grep and it cannot go stale, whatever the next
# wave adds.
# Both sides are counted on the SAME basis — env-var-driven self-tests — so they are comparable.
# FUL11PROBE has no ...TEST/GATE suffix in src/, hence the +1. FXSTREAM is a shell-side grep with
# no SIGHTLINE_ env var at all, so it is reported separately rather than inflating either count.
_exist=$(( $(grep -ohE 'SIGHTLINE_[A-Z0-9_]+(TEST|GATE)' src/*.cs | sort -u | wc -l) + 1 ))   # +1: FUL11PROBE
_ran=$(grep -oE 'SIGHTLINE_[A-Z0-9_]+(TEST|GATE|PROBE)=' scripts/qa-sweep.sh | sort -u | wc -l)
[ "$FULL" = 1 ] || _ran=$((_ran - 1))    # the default skips exactly one (PAIRTEST)
echo "($_exist self-tests exist in src/; this sweep ran $_ran of them, plus the FXSTREAM shell check."
echo " Both counts are derived at runtime, not typed. The COVERAGE GUARD block above is the real"
echo " check — if it is empty, every self-test in src/ was invoked. Every line above must read PASS,"
echo " and every autoplay must read WIN or LOSE — never TIMEOUT, never blank.)"
