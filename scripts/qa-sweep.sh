#!/usr/bin/env bash
# QA sweep for SIGHTLINE — build + full self-test suite + autoplay smoke.
# NOT CI (never wired to Actions); run BY HAND from a session. Documented harness
# quirk: piping an xvfb-run child through $(...) / a for-loop silently drops its
# stdout, so every test is a direct `CMD | grep` statement below.
set -u
export PATH="$PATH:/usr/lib/dotnet"
export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
cd "$(dirname "$0")/.."

X='xvfb-run -a -s -screen 0 1280x800x24'
run() { xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug 2>/dev/null; }

echo "=== BUILD (Release) ==="
dotnet build -c Release 2>&1 | grep -E "error|Error|Warning\(s\)|Build succeeded" | head -20

echo "=== SELF-TESTS ==="
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
echo -n "MODETEST   : "; SIGHTLINE_MODETEST=1  run | grep -oE "MODETEST: (PASS|FAIL)" | head -1
echo -n "HORDETEST  : "; SIGHTLINE_HORDETEST=1 run | grep -oE "HORDETEST: (PASS|FAIL)" | head -1
echo -n "DEATHTEST  : "; SIGHTLINE_DEATHTEST=1 run | grep -oE "DEATHTEST: (PASS|FAIL)" | head -1
echo -n "SNAPTEST   : "; SIGHTLINE_SNAPTEST=1  run | grep -oE "SNAPTEST: (PASS|FAIL)" | head -1
echo -n "AUDIOTEST  : "; SIGHTLINE_AUDIOTEST=1 run | grep -oE "AUDIOTEST: (PASS|FAIL)" | head -1
echo -n "AMBIENTTEST: "; SIGHTLINE_AMBIENTTEST=1 run | grep -oE "AMBIENTTEST: (PASS|FAIL)" | head -1

echo "=== AUTOPLAY x3 ==="
echo -n "run1: "; SIGHTLINE_AUTOPLAY=1 run | grep -oE "RESULT: (WIN|LOSE|TIMEOUT) mission=[0-9]+" | head -1
echo -n "run2: "; SIGHTLINE_AUTOPLAY=1 run | grep -oE "RESULT: (WIN|LOSE|TIMEOUT) mission=[0-9]+" | head -1
echo -n "run3: "; SIGHTLINE_AUTOPLAY=1 run | grep -oE "RESULT: (WIN|LOSE|TIMEOUT) mission=[0-9]+" | head -1
echo "=== DONE ==="
