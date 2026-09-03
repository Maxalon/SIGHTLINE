#!/usr/bin/env bash
# QA sweep for SIGHTLINE — build + full self-test suite + autoplay smoke.
# NOT CI (never wired to Actions); run BY HAND from a session. Documented harness
# quirk: piping an xvfb-run child through $(...) / a for-loop silently drops its
# stdout, so every test is a direct `CMD | grep` statement below.
#
#   bash scripts/qa-sweep.sh          # every self-test + autoplay x3   (~2 min)
#   bash scripts/qa-sweep.sh --full   # + PAIRTEST                      (~2 min 40 s)
# The counts are DERIVED at runtime and printed in the footer — do not hand-type one here.
#
# COUNT NOTE: this footer has been wrong three times now. C1 found it claiming 41 while running
# 42; the W5/C1 integration then had two waves bumping it from different bases; and TRUE BAND
# found the "derived" recipe itself was wrong - it grepped only `...TEST|FUL11PROBE`, so it never
# counted AUDIOGATE, and it counted over THIS FILE while the label said "exist". Both halves are
# now derived, and from the right place. If you add a test, re-run BOTH:
#   exist (in src/):   grep -ohE 'SIGHTLINE_[A-Z0-9_]+(TEST|GATE|PROBE)' src/*.cs | sort -u   (minus $_SWEEP_EXEMPT)
#   run   (this file): the same alphabet, over NON-COMMENT lines of this file only
# The two must be EQUAL - if `run` is smaller the COVERAGE GUARD below will name the gap.
#
# PARALLAX (the lead, after the docket flagged it): the recipe ABOVE THIS LINE was itself stale in
# two ways, and both were holes in the guard rather than in the footer.
#   (1) THE ALPHABET DISAGREED WITH ITSELF. The `exist` side matched `(TEST|GATE)` and carried a
#       hand-written "+1 for FUL11PROBE"; the `run` side matched `(TEST|GATE|PROBE)`. The code below
#       had drifted to `(TEST|GATE)` on BOTH sides, so `SIGHTLINE_FUL11PROBE` - a real assertion hook
#       that this sweep really runs - was counted by NEITHER, and any future *PROBE assertion hook
#       would have been invisible to the COVERAGE GUARD by construction. The alphabet is now
#       TEST|GATE|PROBE on both sides, with a NAMED exemption list so a report-shaped probe is
#       excluded on purpose and by name instead of by an accident of spelling.
#   (2) A COMMENT COULD MASK A GAP. The `run` side grepped the WHOLE of this file, so merely NAMING
#       a hook in a comment here marked it covered. Nothing was masked on the day this was fixed
#       (checked: zero comment-only matches), but the guard is the project's only defence against a
#       test that exists and never runs, and it must not be defeatable by prose.
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
_SELF="$(cd "$(dirname "$0")" && pwd)/$(basename "$0")"   # W5: absolute, for the derived footer count
export PATH="$PATH:/usr/lib/dotnet"
export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
cd "$(dirname "$0")/.."

FULL=0
[ "${1:-}" = "--full" ] && FULL=1

# PARALLAX: every hook's FULL stdout is kept under $SWEEP_LOGDIR (named by the env var that
# selected it) so that a FAIL line can be quoted in full below. Before this the sweep captured
# only the "X: FAIL" token and the reason was lost — a FITTEST flake on the merged P1+P2 tree
# could not be diagnosed from the sweep that caught it.
SWEEP_LOGDIR="$(mktemp -d /tmp/sightline-sweep.XXXXXX)"
run() {
  local hook; hook="$(env | grep -oE '^SIGHTLINE_[A-Z0-9_]+=' | head -1 | tr -d '=')"
  xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug 2>/dev/null | tee "$SWEEP_LOGDIR/${hook:-run}.out"
}

# W9 REVIEW FIX — THE EXIT CODE IS THE GATE, so it has to mean the whole sweep.
# W9 made this script exit non-zero on a TIMEOUT, and stopped there: a self-test line reading FAIL,
# or a non-empty COVERAGE GAP block, still exited 0. "SWEEP-EXIT=0" therefore read as a whole-sweep
# verdict while it was only an autoplay verdict — and the lead relies on that code at every merge.
# Every PASS/FAIL capture below is now routed through `verdict`, which prints it exactly as before
# and records a failure. A BLANK capture (the test threw, or printed nothing) counts as a failure
# too: that is how a crashed self-test used to read as a quiet blank line.
_fail=0
verdict() {   # verdict <captured-text>
  if [ -z "$1" ]; then echo "<no result line>"; _fail=1; return; fi
  echo "$1"
  case "$1" in *FAIL*)
    _fail=1
    # quote the full failing line (first 900 chars) from the hook's kept output, so the sweep
    # says WHY and not just THAT
    local name; name="${1%%:*}"
    grep -h -m1 -E "^${name}[^A-Za-z0-9]*.*FAIL" "$SWEEP_LOGDIR"/SIGHTLINE_"${name}"*.out 2>/dev/null | cut -c1-900 | sed 's/^/    detail: /'
    ;;
  esac
}

echo "=== BUILD (Release) ==="
dotnet build -c Release 2>&1 | grep -E "error|Error|Warning\(s\)|Build succeeded" | head -20

echo "=== SELF-TESTS ==="
echo -n "DKTEST     : "; verdict "$(SIGHTLINE_DKTEST=1 run | grep -oE "DKTEST: (PASS|FAIL)" | head -1)"
echo -n "RESCUETEST : "; verdict "$(SIGHTLINE_RESCUETEST=1 run | grep -oE "RESCUETEST: (PASS|FAIL)" | head -1)"
echo -n "STAGGERTEST: "; verdict "$(SIGHTLINE_STAGGERTEST=1 run | grep -oE "STAGGERTEST: (PASS|FAIL)" | head -1)"
echo -n "MORALETEST : "; verdict "$(SIGHTLINE_MORALETEST=1 run | grep -oE "MORALETEST: (PASS|FAIL)" | head -1)"
echo -n "BEACONTEST : "; verdict "$(SIGHTLINE_BEACONTEST=1 run | grep -oE "BEACONTEST: (PASS|FAIL)" | head -1)"
echo -n "COMBATTEST : "; verdict "$(SIGHTLINE_COMBATTEST=1 run | grep -oE "COMBATTEST: (PASS|FAIL)" | head -1)"
echo -n "SAVETEST   : "; verdict "$(SIGHTLINE_SAVETEST=1 run | grep -oE "SAVETEST: (PASS|FAIL)" | head -1)"
echo -n "AITEST     : "; verdict "$(SIGHTLINE_AITEST=1 run | grep -oE "AITEST: (PASS|FAIL)" | head -1)"
echo -n "DECLINETEST: "; verdict "$(SIGHTLINE_DECLINETEST=1 run | grep -oE "DECLINETEST: (PASS|FAIL)" | head -1)"
echo -n "BANDTEST   : "; verdict "$(SIGHTLINE_BANDTEST=1 run | grep -oE "BANDTEST: (PASS|FAIL)" | head -1)"
echo -n "ITEMTEST   : "; verdict "$(SIGHTLINE_ITEMTEST=1 run | grep -oE "ITEMTEST: (PASS|FAIL)" | head -1)"
echo -n "STATUSTEST : "; verdict "$(SIGHTLINE_STATUSTEST=1 run | grep -oE "STATUSTEST: (PASS|FAIL)" | head -1)"
echo -n "COVERTEST  : "; verdict "$(SIGHTLINE_COVERTEST=1 run | grep -oE "COVERTEST: (PASS|FAIL)" | head -1)"
echo -n "TRAITTEST  : "; verdict "$(SIGHTLINE_TRAITTEST=1 run | grep -oE "TRAITTEST: (PASS|FAIL)" | head -1)"
echo -n "WOUNDTEST  : "; verdict "$(SIGHTLINE_WOUNDTEST=1 run | grep -oE "WOUNDTEST: (PASS|FAIL)" | head -1)"
echo -n "CDTEST     : "; verdict "$(SIGHTLINE_CDTEST=1 run | grep -oE "CDTEST: (PASS|FAIL)" | head -1)"
echo -n "FIELDTEST  : "; verdict "$(SIGHTLINE_FIELDTEST=1 run | grep -oE "FIELDTEST: (PASS|FAIL)" | head -1)"
# THE STRIDE: pillar 2 ("feels good") — walk speed profile / vault arc / floating-text separation.
echo -n "FEELTEST   : "; verdict "$(SIGHTLINE_FEELTEST=1 run | grep -oE "FEELTEST: (PASS|FAIL)" | head -1)"
echo -n "SIEGETEST  : "; verdict "$(SIGHTLINE_SIEGETEST=1 run | grep -oE "SIEGETEST: (PASS|FAIL)" | head -1)"
echo -n "EVENTTEST  : "; verdict "$(SIGHTLINE_EVENTTEST=1 run | grep -oE "EVENTTEST: (PASS|FAIL)" | head -1)"
echo -n "VETTEST    : "; verdict "$(SIGHTLINE_VETTEST=1 run | grep -oE "VETTEST: (PASS|FAIL)" | head -1)"
echo -n "OWTEST     : "; verdict "$(SIGHTLINE_OWTEST=1 run | grep -oE "OWTEST: (PASS|FAIL)" | head -1)"
echo -n "SCARTEST   : "; verdict "$(SIGHTLINE_SCARTEST=1 run | grep -oE "SCARTEST: (PASS|FAIL)" | head -1)"
echo -n "CONTRACTTEST: "; verdict "$(SIGHTLINE_CONTRACTTEST=1 run | grep -oE "CONTRACTTEST: (PASS|FAIL)" | head -1)"
echo -n "SHOVETEST  : "; verdict "$(SIGHTLINE_SHOVETEST=1 run | grep -oE "SHOVETEST: (PASS|FAIL)" | head -1)"
echo -n "CONCEALTEST: "; verdict "$(SIGHTLINE_CONCEALTEST=1 run | grep -oE "CONCEALTEST: (PASS|FAIL)" | head -1)"
echo -n "HAZARDTEST : "; verdict "$(SIGHTLINE_HAZARDTEST=1 run | grep -oE "HAZARDTEST: (PASS|FAIL)" | head -1)"
echo -n "BIOMETEST  : "; verdict "$(SIGHTLINE_BIOMETEST=1 run | grep -oE "BIOMETEST: (PASS|FAIL)" | head -1)"
echo -n "BENCHTEST  : "; verdict "$(SIGHTLINE_BENCHTEST=1 run | grep -oE "BENCHTEST: (PASS|FAIL)" | head -1)"
echo -n "DRAFTTEST  : "; verdict "$(SIGHTLINE_DRAFTTEST=1 run | grep -oE "DRAFTTEST: (PASS|FAIL)" | head -1)"
echo -n "METATEST   : "; verdict "$(SIGHTLINE_METATEST=1 run | grep -oE "METATEST: (PASS|FAIL)" | head -1)"
echo -n "CODEXTEST  : "; verdict "$(SIGHTLINE_CODEXTEST=1 run | grep -oE "CODEXTEST: (PASS|FAIL)" | head -1)"
echo -n "VOICETEST  : "; verdict "$(SIGHTLINE_VOICETEST=1 run | grep -oE "VOICETEST: (PASS|FAIL)" | head -1)"
echo -n "MODETEST   : "; verdict "$(SIGHTLINE_MODETEST=1 run | grep -oE "MODETEST: (PASS|FAIL)" | head -1)"
echo -n "HORDETEST  : "; verdict "$(SIGHTLINE_HORDETEST=1 run | grep -oE "HORDETEST: (PASS|FAIL)" | head -1)"
echo -n "DEATHTEST  : "; verdict "$(SIGHTLINE_DEATHTEST=1 run | grep -oE "DEATHTEST: (PASS|FAIL)" | head -1)"
echo -n "HEATLADDERTEST: "; verdict "$(SIGHTLINE_HEATLADDERTEST=1 run | grep -oE "HEATLADDERTEST: (PASS|FAIL)" | head -1)"
echo -n "MIDTOOTHTEST: "; verdict "$(SIGHTLINE_MIDTOOTHTEST=1 run | grep -oE "MIDTOOTHTEST: (PASS|FAIL)" | head -1)"
echo -n "SNAPTEST   : "; verdict "$(SIGHTLINE_SNAPTEST=1 run | grep -oE "SNAPTEST: (PASS|FAIL)" | head -1)"
echo -n "AUDIOTEST  : "; verdict "$(SIGHTLINE_AUDIOTEST=1 run | grep -oE "AUDIOTEST: (PASS|FAIL)" | head -1)"
echo -n "AUDIOGATE  : "; verdict "$(SIGHTLINE_AUDIOGATE=1 run | grep -oE "AUDIOGATE: (PASS|FAIL)" | head -1)"
# C5 THE HARD EDGES — DEFECT: this was the ONE self-test in the sweep not routed through
# `verdict`, so an AIIDLETEST FAIL printed "FAIL" and the sweep still exited 0. W9 built `verdict`
# precisely so the exit code means the whole sweep, and this line was missed by it.
# C3: this was the ONE self-test line still not routed through `verdict` — a FAIL here printed and
# was never recorded. It did not matter while the script had no exit statement at all; now that it
# has one, it does. (`tail -1` is kept: this test prints two candidate lines and the last is the
# verdict.)
echo -n "AIIDLETEST : "; verdict "$(SIGHTLINE_AIIDLETEST=1 run | grep -oE "AIIDLETEST: (PASS|FAIL)" | tail -1)"
# THE CUE MAP (wave "cue-map"): one meaning, one cue. Audio.CueFor must be INJECTIVE over the
# canonical game events, no opponent telegraph may resolve to a UI-bus cue, the real ShowBanner
# must put an enemy banner on the SFX fader, and the src/Game.cs call-site census must be under
# its caps. Its (c) leg is a SOURCE SCAN of src/Game.cs relative to the working directory - which
# is the repo root here; from a published binary it reports `census: n/a` and the other legs stand.
echo -n "CUETEST    : "; verdict "$(SIGHTLINE_CUETEST=1 run | grep -oE "CUETEST: (PASS|FAIL)" | head -1)"
# RESONANCE A3: the AUDIO CHECK audition screen's listing/label/measurement contract.
echo -n "AUDITIONTEST: "; verdict "$(SIGHTLINE_AUDITIONTEST=1 run | grep -oE "AUDITIONTEST: (PASS|FAIL)" | head -1)"
echo -n "AMBIENTTEST: "; verdict "$(SIGHTLINE_AMBIENTTEST=1 run | grep -oE "AMBIENTTEST: (PASS|FAIL)" | head -1)"
# Q1: the no-two-units-on-one-tile invariant. Drives 16 real missions (~70s), so it goes last.
echo -n "STACKTEST  : "; verdict "$(SIGHTLINE_STACKTEST=1 run | grep -oE "STACKTEST: (PASS|FAIL)" | head -1)"
# FUL-era hooks the sweep used to omit entirely — the bleed-out state machine, the pikeman,
# pod sizing/linking, and the content-exposure invariant. (EXPOSURETEST and FUL11PROBE print
# "NAME PASS" with no colon; the others use "NAME: PASS".)
echo -n "DOWNTEST   : "; verdict "$(SIGHTLINE_DOWNTEST=1 run | grep -oE "DOWNTEST: (PASS|FAIL)" | head -1)"
echo -n "PIKETEST   : "; verdict "$(SIGHTLINE_PIKETEST=1 run | grep -oE "PIKETEST: (PASS|FAIL)" | head -1)"
# P10 THE HELD LANE (~22 s: it walks 80 short campaigns for its planner legs): the ordinary enemy
# overwatch's CONE. Arms the player's own OwFocused flag set with an axis Ai.ChooseLane picked; the
# lane covers approach ground; the red wash / Threat[].Watched predicate (Game.WatchCovers) agrees
# TILE FOR TILE with the real OnUnitEnteredTile reaction in both directions; and the marked fraction
# of the floor collapses (59.1% -> 28.4% on its staged board). Reads the AMBIENT dial, so
# `SIGHTLINE_AILANE=0 SIGHTLINE_LANETEST=1` FAILS — that is the proof it can.
echo -n "LANETEST   : "; verdict "$(SIGHTLINE_LANETEST=1 run | grep -oE "LANETEST: (PASS|FAIL)" | head -1)"
echo -n "PODTEST    : "; verdict "$(SIGHTLINE_PODTEST=1 run | grep -oE "PODTEST: (PASS|FAIL)" | head -1)"
echo -n "EXPOSURETEST: "; verdict "$(SIGHTLINE_EXPOSURETEST=1 run | grep -oE "EXPOSURETEST (PASS|FAIL)" | head -1)"
echo -n "FUL11PROBE : "; verdict "$(SIGHTLINE_FUL11PROBE=40 run | grep -oE "FUL11PROBE (PASS|FAIL)" | head -1)"
# RESONANCE W5: the RECRUIT rung + the comfort settings (anim speed / UI text scale).
echo -n "ONRAMPTEST : "; verdict "$(SIGHTLINE_ONRAMPTEST=1 run | grep -oE "ONRAMPTEST: (PASS|FAIL)" | head -1)"
echo -n "OPENERTEST : "; verdict "$(SIGHTLINE_OPENERTEST=1 run | grep -oE "OPENERTEST: (PASS|FAIL)" | head -1)"
echo -n "HVTTEST    : "; verdict "$(SIGHTLINE_HVTTEST=1 run | grep -oE "HVTTEST: (PASS|FAIL)" | head -1)"
# CONTOUR C3 THE TWO GAMES: the objective-CLASS contract. Model (exactly Eliminate+Decapitate of
# the eight are PITCHED) + DRAW (the campaign fork paints the class mark, the key and the hover
# tooltip's class line, both classes staged, read at the draw call) + LEVER (the anti-turtle clock
# adds no bodies to an ELIMINATE while its aim ramp still rises; Hack/Decapitate unchanged).
echo -n "CLASSTEST  : "; verdict "$(SIGHTLINE_CLASSTEST=1 run | grep -oE "CLASSTEST: (PASS|FAIL)" | head -1)"
# THE FORK PAYS: the campaign fork's ECONOMY. ELITE > COMBAT > SUPPLY at equal depth (SUPPLY used
# to pay MORE than the fight it is lighter than), C3's declared-open PITCHED premium is paid AND
# printed, the EVENT node's hover no longer prints its sentinel card's objective and force, and a
# SUPPLY clear can wound again (the heal used to land before the fresh-wound gauge read the HP).
echo -n "FORKTEST   : "; verdict "$(SIGHTLINE_FORKTEST=1 run | grep -oE "FORKTEST: (PASS|FAIL)" | tail -1)"
# RESONANCE T1/T2: the onboarding contract and the incoming-fire forecast. These two EXISTED
# but were never run by this sweep - the integration review caught it. THREATTEST prints
# "NAME PASS" with no colon, like EXPOSURETEST.
echo -n "TUTTEST    : "; verdict "$(SIGHTLINE_TUTTEST=1 run | grep -oE "TUTTEST: (PASS|FAIL)" | head -1)"
echo -n "THREATTEST : "; verdict "$(SIGHTLINE_THREATTEST=1 run | grep -oE "THREATTEST (PASS|FAIL)" | head -1)"
echo -n "TUTTEST    : "; verdict "$(SIGHTLINE_TUTTEST=1  run | grep -oE "TUTTEST: (PASS|FAIL)" | head -1)"
echo -n "BRIEFTEST  : "; verdict "$(SIGHTLINE_BRIEFTEST=1 run | grep -oE "BRIEFTEST: (PASS|FAIL)" | head -1)"
echo -n "CONTRASTTEST: "; verdict "$(SIGHTLINE_CONTRASTTEST=1 run | grep -oE "CONTRASTTEST: (PASS|FAIL)" | head -1)"
echo -n "CHROMETEST : "; verdict "$(SIGHTLINE_CHROMETEST=1 run | grep -oE "CHROMETEST: (PASS|FAIL)" | head -1)"
# THE FIT: the shipped TEXT SIZE range {0.90, 1.00, 1.10, 1.20} is a tested surface. Asserts that
# no string on the doctrine / armory / hall-of-fame / draft screens is painted outside its own
# chrome or into another string's pixels, at EVERY scale - not just at 100%, which is the only
# scale any self-test in this project had ever run at. SIGHTLINE_OLDFIT=1 makes it fail (40).
echo -n "FITTEST    : "; verdict "$(SIGHTLINE_FITTEST=1 run | grep -oE "FITTEST: (PASS|FAIL)" | head -1)"
# W5-FIX: the backdrop registry — no phase may paint a full-screen backdrop from the chrome pass.
echo -n "BACKDROPTEST: "; verdict "$(SIGHTLINE_BACKDROPTEST=1 run | grep -oE "BACKDROPTEST: (PASS|FAIL)" | head -1)"
echo -n "QUITTEST   : "; verdict "$(SIGHTLINE_QUITTEST=1   run | grep -oE "QUITTEST: (PASS|FAIL)" | head -1)"
# SETTINGS EVERYWHERE: the settings card reachable from INTRO and BARRACKS, not just a fight.
echo -n "SETTINGSTEST: "; verdict "$(SIGHTLINE_SETTINGSTEST=1 run | grep -oE "SETTINGSTEST: (PASS|FAIL)" | head -1)"
echo -n "THREATTEST : "; verdict "$(SIGHTLINE_THREATTEST=1 run | grep -oE "THREATTEST (PASS|FAIL)" | head -1)"
# R2 FIX 1: the nobody-is-walled-out geometry invariant (all 4 deployment shapes x 8 objectives
# x 2 heats, thousands of fresh boards). ~25 s.
echo -n "GEOMTEST   : "; verdict "$(SIGHTLINE_GEOMTEST=1 run | grep -oE "GEOMTEST: (PASS|FAIL)" | head -1)"
# W9 THE REPAIR: the three hooks this wave shipped. TRUTHTEST ground-truths the DISPLAYED shot
# numbers against rolled outcomes (no test had ever read a displayed quantity); GRAPPLETEST is the
# FIRST coverage the GRAPPLE verb has ever had; STALLTEST asserts the autopilot's own
# "never a RESULT: TIMEOUT" contract instead of leaving it in a comment.
# C6 SHIPS LIKE A PRODUCT: the DISTRIBUTABLE's own contract — the bundled-file manifest resolved
# strictly next to the binary, the licence obligations, the player-data directory, the atomicity of
# all three writers, trim-safe serialization, and the build stamp. HONEST SCOPE: run from HERE it
# is testing bin/Debug/net8.0/, so its manifest leg proves the .csproj copies what it claims. The
# leg that matters most — "is the artifact a player receives complete?" — can only be judged
# against a published directory, and `bash scripts/publish.sh` runs SHIPTEST there on every publish.
echo -n "SHIPTEST   : "; verdict "$(SIGHTLINE_SHIPTEST=1 run | grep -oE "SHIPTEST: (PASS|FAIL)" | head -1)"
# PARALLAX P11 THE CRASH FILE: the crash reporter's own contract, and the only self-test in this
# project that deliberately THROWS. It runs a real exception through Crash.Guard - the same
# function Program.Main is - and then reads the file back: contents (version / UTC stamp / OS /
# runtime / the whole exception chain / live game state), location (the player-data directory, not
# a second derivation of it), the atomic write (C6's open-handle inode probe, same technique), the
# unwritable-directory fallback to stderr, and both disk caps. It writes to a TEMP directory, never
# a real profile, and leg (g) proves that by diffing the real one before and after.
# scripts/publish.sh runs it against the PUBLISHED binary too, where trimming and single-file
# packing change how the version stamp and the base directory resolve.
echo -n "CRASHTEST  : "; verdict "$(SIGHTLINE_CRASHTEST=1 run | grep -oE "CRASHTEST: (PASS|FAIL)" | head -1)"
echo -n "TRUTHTEST  : "; verdict "$(SIGHTLINE_TRUTHTEST=1 run | grep -oE "TRUTHTEST: (PASS|FAIL)" | head -1)"
echo -n "GRAPPLETEST: "; verdict "$(SIGHTLINE_GRAPPLETEST=1 run | grep -oE "GRAPPLETEST: (PASS|FAIL)" | head -1)"
echo -n "STALLTEST  : "; verdict "$(SIGHTLINE_STALLTEST=1 run | grep -oE "STALLTEST: (PASS|FAIL)" | head -1)"
# THE HEAT PIN AND L5: the balance instrument's contracts — the field-event heat pin (and that the
# three arms DO leak with it off), RunRec.HeatEnd/RunTurns + campaigns[] + heatLeak in the JSON, and
# the STALEMATE guard naming its arm (STALEMATE-MISSION / STALEMATE-RUN).
echo -n "HEATPINTEST: "; verdict "$(SIGHTLINE_HEATPINTEST=1 run | grep -oE "HEATPINTEST: (PASS|FAIL)" | head -1)"
# C5 THE HARD EDGES: the ENEMY-turn half of the no-deadlock contract (STALLTEST covers the player
# turn), and the enemy DECISION CENSUS — every branch of the enemy exec chain must be REACHED, at
# a rate a player could actually meet. AICOVTEST=6 walks 144 campaigns (~25 s; N=2 gave ~2470 acts, below the AiCovMinActs floor that keeps the rate verdict from being a Poisson draw); the effectively-dead
# branches it tolerates are named in Game.Harness.cs's AiCovKnownRare and printed on every run.
echo -n "ENEMYSTALLTEST: "; verdict "$(SIGHTLINE_ENEMYSTALLTEST=1 run | grep -oE "ENEMYSTALLTEST: (PASS|FAIL)" | head -1)"
echo -n "AICOVTEST  : "; verdict "$(SIGHTLINE_AICOVTEST=6 run | grep -oE "AICOVTEST: (PASS|FAIL)" | head -1)"
# C5: the HOSTILE SAVE — eight edited/truncated/older-build save.json shapes through the real
# resume path. W9 asked this of meta.json; save.json had never been asked.
echo -n "SAVEEDGETEST: "; verdict "$(SIGHTLINE_SAVEEDGETEST=1 run | grep -oE "SAVEEDGETEST: (PASS|FAIL)" | head -1)"

# W1 TRUE INSTRUMENT: the autopilot's ROUTE through the campaign DAG (the sampling frame every
# published balance number was drawn through) and the frame/RNG independence of gameplay.
echo -n "ROUTETEST  : "; verdict "$(SIGHTLINE_ROUTETEST=1 run | grep -oE "ROUTETEST: (PASS|FAIL)" | tail -1)"
# ~20 s: gameplay must be a function of the SEED, not of the frame rate, the animation-speed
# setting or the screen-shake comfort toggle. 16 campaigns; FAILs under SIGHTLINE_FXRNG=0.
# Its PHASE 2 (render purity) is the real guard for "presentation never draws from Util.Rng":
# it asserts that constructing a Unit and drawing 30 real frames both leave the shared stream
# untouched, and proves the probe sensitive with a deliberate draw.
echo -n "RNGFRAMETEST: "; verdict "$(SIGHTLINE_RNGFRAMETEST=1 run | grep -oE "RNGFRAMETEST: (PASS|FAIL)" | tail -1)"

# W1 STATIC BACKSTOP (free, and it runs even when the binary will not build): Fx.cs is the
# FX layer and must contain zero draws from the shared gameplay stream. HONEST SCOPE: this
# grep would NOT have caught the W1 review's Bob defect, which lived in Unit.cs — only
# RNGFRAMETEST phase 2 catches that class. COMMENTS ARE STRIPPED FIRST: the first version
# grepped the raw file and FAILed on a doc comment reading "deterministic hash (NOT Util.Rng)".
# LEAD, at the W9 merge: routed into $_fail so the sweep's single exit code covers it too.
_fxhits=$(sed -E 's,//.*,,' src/Fx.cs | grep -nE 'Util\.(RandF|RandInt|RandRange|Roll|Choice)\(|Util\.Rng')
echo -n "FXSTREAM   : "; if [ -n "$_fxhits" ]; then
  echo "FXSTREAM: FAIL (src/Fx.cs draws from the shared gameplay Util.Rng — use Util.FxRand*)"
  echo "$_fxhits" | sed 's/^/     /'; _fail=1
else echo "FXSTREAM: PASS"; fi
# W4 THE BOARD BECOMES A PLACE: the only self-test that measures RENDERED PIXELS — the squint
# value hierarchy (selected soldier > live hostile > dormant pod on mean AND peak luminance),
# the cover-volume merge, and the move overlay's contour + palette. Needs the full-size window.
echo -n "BOARDTEST  : "; verdict "$(SIGHTLINE_BOARDTEST=1 run | grep -oE "BOARDTEST: (PASS|FAIL)" | head -1)"

if [ "$FULL" = 1 ]; then
  # ~38 s: the CRN identity check. Skipped by default so the sweep stays a quick loop;
  # REQUIRED before a merge (see the header).
  echo -n "PAIRTEST   : "; verdict "$(SIGHTLINE_PAIRTEST=1 run | grep -oE "PAIRTEST: (PASS|FAIL)" | tail -1)"
else
  echo "PAIRTEST   : SKIPPED (re-run with --full; required before merging)"
fi

# COVERAGE GUARD: this sweep's test list has drifted from src/ twice (a hand-maintained
# counter said 41 while 42 ran; a later recount still missed TUTTEST and THREATTEST). Derive
# it instead of trusting it - if a self-test exists in src/ and is not invoked above, say so.
# The EXEMPTION LIST: hooks whose names match the alphabet but are measurement REPORTS, not
# assertions - they print a table, never a PASS/FAIL, so there is nothing for this sweep to gate on.
# Exempting BY NAME (rather than by leaving them outside the regex) is the point: anything new is
# named by the guard until someone deliberately adds it here.
#   SIGHTLINE_BANDPROBE - TRUE BAND's choice-band instrument-DESIGN probe (Game.Autopilot.cs:2277).
#   SIGHTLINE_MODEFORCEPROBE - P14's SKIRMISH/DAILY force-composition report (Game.Modes.cs): the
#     before/after evidence a change to the single-mission modes has to show. MODETEST asserts.
#   SIGHTLINE_DAILYSIGPROBE  - P14's daily-signature line (Program.cs), the CHILD half of
#     MODETEST leg (11)'s cross-process check. MODETEST launches it and does the comparing, so
#     running it from here would print one hash and gate on nothing.
_SWEEP_EXEMPT='SIGHTLINE_BANDPROBE|SIGHTLINE_MODEFORCEPROBE|SIGHTLINE_DAILYSIGPROBE'
_missing=$(comm -23 \
  <(grep -ohE 'SIGHTLINE_[A-Z0-9_]+(TEST|GATE|PROBE)' src/*.cs | sort -u | grep -vxE "$_SWEEP_EXEMPT") \
  <(grep -vE '^[[:space:]]*#' "$_SELF" | grep -ohE 'SIGHTLINE_[A-Z0-9_]+(TEST|GATE|PROBE)' | sort -u | grep -vxE "$_SWEEP_EXEMPT"))
if [ -n "$_missing" ]; then
  echo "!! COVERAGE GAP - these self-tests exist in src/ but this sweep never runs them:"
  echo "$_missing" | sed 's/^/     /'
  _fail=1     # W9 REVIEW FIX: an unrun self-test is a hole in the gate, not a note for the reader
fi

echo "=== AUTOPLAY x3 ==="
# W9 THE REPAIR: a TIMEOUT is now a HARD FAILURE of this script, not a line for a reader to notice.
# CLAUDE.md has always called TIMEOUT a pre-merge failure, but this sweep only PRINTED the RESULT
# line — and at the ~1% rate two independent stall causes ran at, that is squarely inside the noise
# an agent writes off as "a weak-autopilot flake". That is exactly how both survived. A BLANK result
# (the run threw, or printed nothing) fails too.
_autofail=0
for _i in 1 2 3; do
  echo -n "run$_i: "
  _r=$(SIGHTLINE_AUTOPLAY=1 run | grep -oE "RESULT: (WIN|LOSE|TIMEOUT) mission=[0-9]+ frame=[0-9]+ turns=[0-9]+" | head -1)
  echo "${_r:-<no RESULT line>}"
  case "$_r" in
    *WIN*|*LOSE*) ;;
    *) _autofail=1 ;;
  esac
done
if [ "$_autofail" = 1 ]; then
  echo "!! AUTOPLAY FAILED - a TIMEOUT or a missing RESULT line. The autopilot contract"
  echo "   (Game.AutoMaxRunTurns + the within-turn idle guard) says this is unreachable;"
  echo "   if it fired, something regressed. DO NOT MERGE."
fi
echo "=== DONE ===   (full per-hook outputs kept under $SWEEP_LOGDIR)"
# DERIVED, not typed. This footer's number has now been wrong SIX times (41 / 46 / 49 / 51 / 53
# claimed while a different count ran, and then TWO successive "derivations" that were themselves
# wrong). The 2026 audit's wildcard-4 finding is exactly this class of hand-maintained registry
# drift, and it keeps recurring because each fix counted a LINE SHAPE.
#
# W5 anchored on '^ *echo ... ; SIGHTLINE_' to catch PAIRTEST's indented invocation. W9 then
# routed every invocation through `verdict "$(SIGHTLINE_...)"` so one exit code covers the whole
# sweep — and W5's pattern, which requires `; SIGHTLINE_`, stopped matching ANYTHING. The footer
# printed "0 self-tests ran" while 56 of them passed.
#
# LEAD, at the W5 merge — the lesson, and why this version should survive the next routing change:
# COUNT NAMES, NOT LINES. A hook is identified by its SIGHTLINE_<NAME>(TEST|GATE) env var, and that
# name is stable no matter how the invocation is wrapped. Both halves are counted on the same
# basis, from their own file, so the two are directly comparable and the COVERAGE GUARD block above
# — which lists any hook in src/ this file never names — remains the real check.
_have=$(grep -ohE 'SIGHTLINE_[A-Z0-9_]+(TEST|GATE|PROBE)' src/*.cs | sort -u | grep -vxE "$_SWEEP_EXEMPT" | wc -l)
# ...and the exemption list is filtered off the `run` side too: `_SWEEP_EXEMPT` is itself an
# ordinary (non-comment) line of this file, so without this the footer counted the exemption
# ASSIGNMENT as a run test and reported ran = have + 1.
_ran=$(grep -vE '^[[:space:]]*#' "$_SELF" | grep -ohE 'SIGHTLINE_[A-Z0-9_]+(TEST|GATE|PROBE)' | sort -u | grep -vxE "$_SWEEP_EXEMPT" | wc -l)
[ "$FULL" = 1 ] || _ran=$((_ran - 1))   # PAIRTEST is the only --full-gated one
echo "($_have self-tests exist in src/; this sweep ran $_ran$([ "$FULL" = 1 ] || echo ", PAIRTEST skipped")."
echo " Both counts are derived from env-var NAMES, not line shapes. Every line above must read"
echo " PASS, and every autoplay must read WIN or LOSE — never TIMEOUT, never blank.)"

# ── CONTOUR C3: THE EXIT CODE, WHICH HAD NEVER BEEN WIRED ──────────────────────────────────────
# W9's block at the top of this file says, at length, that a FAIL line / a COVERAGE GAP / a TIMEOUT
# must make the sweep exit non-zero "so it is a gate rather than a report for a reader to notice",
# and CLAUDE.md repeats the claim. Every path faithfully accumulated `_fail` and `_autofail` — and
# then the script ENDED ON AN `echo`, so its exit status was that echo's, i.e. 0, always. C3 found
# it the only way anyone was going to: its own new self-test failed inside a --full sweep and the
# sweep still reported SWEEP-EXIT=0.
#
# Every "qa-sweep --full green, SWEEP-EXIT=0" claim made before this line existed was therefore
# reporting the exit code of an echo. The PASS/FAIL lines were real; the code above them was not.
_rc=0
[ "$_fail" = 1 ] && _rc=1
[ "$_autofail" = 1 ] && _rc=1
if [ "$_rc" != 0 ]; then
  echo "!! SWEEP FAILED - see the FAIL / COVERAGE GAP / AUTOPLAY lines above. DO NOT MERGE."
fi
exit $_rc
