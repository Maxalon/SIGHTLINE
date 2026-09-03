#!/usr/bin/env bash
# Build a distributable SIGHTLINE binary. NOT CI — a hand-run script; nothing invokes it
# automatically.
#
#   bash scripts/publish.sh                 # recommended: trimmed + ReadyToRun, single file
#   bash scripts/publish.sh --small         # smallest binary, slowest start
#   bash scripts/publish.sh --no-trim       # fallback if trimming ever becomes unsafe again
#   bash scripts/publish.sh --plain         # single file, nothing else
#   bash scripts/publish.sh --rid win-x64   # cross-publish (any RID dotnet supports)
#   bash scripts/publish.sh --out /some/dir
#
# Output is TEN files: ONE self-contained executable, libraylib.so (a native library the runtime
# dlopen()s — it cannot be linked into the single file), the assets/ directory (two fonts, their
# two OFL licence texts, two CREDITS ledgers), THIRD-PARTY-NOTICES.txt and LICENSE.
# SHIP THE WHOLE OUTPUT DIRECTORY: the notice file, both assets/*-LICENSE.txt and LICENSE are
# licence obligations, not optional extras. SIGHTLINE_SHIPTEST (below) is what enforces that.
#
# Re-measured by wave C6 on 2026-08-30 (linux-x64, self-contained, .NET SDK 8.0.130, this
# container). "start" = wall time for one window-free SIGHTLINE_SAVETEST launch — startup + JIT of
# the persistence path, not frame time. Measured INTERLEAVED (one launch of each mode per round, 9
# rounds) so all four see the same load on a container shared with five other agents; read the
# ORDERING as the result. Full table + caveats in docs/DISTRIBUTION.md section 2.
#
# SIZES ARE MB = 10^6 BYTES, and are from the final C6 binary. The START column is NOT: it was
# measured on the pre-review-fix binary the same day and deliberately not re-run under nine
# concurrent reviewers. See docs/DISTRIBUTION.md section 2.
#
#   mode        flags                                exe      dir    files   start (median of 9)
#   release     PublishTrimmed + ReadyToRun + single  26.5 MB  29.3 MB  10     169 ms  <- default
#   small       PublishTrimmed + single               16.1 MB  18.8 MB  10     545 ms
#   no-trim     ReadyToRun + single                   82.3 MB  85.1 MB  10     206 ms
#   plain       single                                68.3 MB  71.0 MB  10     317 ms
#   win-x64     (cross-published)                     24.8 MB  26.9 MB  10     n/a here
#
# Trimming is what makes "small" slow: it strips the framework's precompiled ReadyToRun code,
# so everything JITs at startup. Adding ReadyToRun back costs ~10 MB and buys the fastest start
# of any config — which is why the default is both.
#
# On PublishTrimmed: it USED to silently destroy all persistence (save.json AND the entire
# cross-run meta profile) because reflection-based System.Text.Json loses the type metadata
# trimming strips — the game booted, played and finished a whole campaign while saving nothing.
# That is fixed: SaveGame and Display serialise through source-generated JsonSerializerContexts,
# and Sightline.csproj roots our own assembly for the one remaining reflective site (the
# SIGHTLINE_BALANCE telemetry export, which serialises anonymous types). Trimmed is therefore the
# RECOMMENDED default, not a hazard to avoid; what you must not do is publish without this script,
# which re-proves the artifact by running SAVETEST + METATEST + SHIPTEST + CRASHTEST against the
# binary it just built. Sightline.csproj's C6GuardTrimmedPersistence target additionally makes
# removing either mitigation a build ERROR.
#
# P11 THE CRASH FILE also added a WINDOWS-ONLY check at the bottom of this script: a win-* RID now
# publishes as WinExe (no console window behind the game), and the script reads the published
# .exe's PE subsystem byte to prove it. See docs/DISTRIBUTION.md section 7.
set -euo pipefail

cd "$(dirname "$0")/.."

RID="linux-x64"
OUT=""
MODE="release"
for ((i = 1; i <= $#; i++)); do
  case "${!i}" in
    --small)   MODE="small" ;;
    --no-trim) MODE="no-trim" ;;
    --plain)   MODE="plain" ;;
    --rid)     i=$((i + 1)); RID="${!i}" ;;
    --out)     i=$((i + 1)); OUT="${!i}" ;;
    -h|--help) sed -n '2,40p' "$0"; exit 0 ;;
    *) echo "unknown argument: ${!i}" >&2; exit 2 ;;
  esac
done
OUT="${OUT:-dist/$RID-$MODE}"

case "$MODE" in
  release) EXTRA=(-p:PublishTrimmed=true -p:PublishReadyToRun=true) ;;
  small)   EXTRA=(-p:PublishTrimmed=true) ;;
  no-trim) EXTRA=(-p:PublishReadyToRun=true) ;;
  plain)   EXTRA=() ;;
esac

echo ">> publishing '$MODE' for $RID -> $OUT"
rm -rf "$OUT"
dotnet publish -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:DebugType=none "${EXTRA[@]}" -o "$OUT"

echo
echo ">> output: $(du -sh "$OUT" | cut -f1)"
ls -1 "$OUT"

# VERIFY: the persistence self-tests plus the two artifact-contract ones (C6 SHIPTEST, P11
# CRASHTEST), run against the PUBLISHED binary (not the build output). This is exactly the check a
# trimmed publish used to fail while reporting "0 Errors".
if [ -x "$OUT/Sightline" ] && [ "$RID" = "linux-x64" ]; then
  echo
  echo ">> verifying persistence + shipping contract + the crash reporter against the published binary"
  # METATEST briefly opens a window; with no display raylib segfaults on shutdown AFTER printing
  # its verdict (true of every published build, trimmed or not), so run it under Xvfb when one is
  # available and judge on the printed line rather than the exit code.
  RUN=(env)
  if [ -z "${DISPLAY:-}" ] && command -v xvfb-run >/dev/null 2>&1; then
    RUN=(xvfb-run -a -s "-screen 0 1280x800x24")
  fi
  ok=1
  # C6: SHIPTEST is the leg that can ONLY be judged here. Run from the source tree it proves very
  # little — Cfg.AssetPath's cwd fallback quietly resolves anything the .csproj forgot to copy off
  # the repo instead of off the build output, which is exactly how RESONANCE F1's missing font
  # survived every self-test in the suite. Against the PUBLISHED directory there is no repo to fall
  # back to, so the manifest leg is testing the artifact a player actually receives.
  # P11: CRASHTEST joins the publish gate for the same reason SHIPTEST did — it is about the
  # ARTIFACT, not the model. Run here it exercises the crash reporter inside the TRIMMED,
  # single-file binary a player receives, where AppContext.BaseDirectory, the assembly version
  # stamp and RuntimeInformation all behave differently from the source tree. A crash reporter
  # that only works in the development build is the exact shape of defect C6 was created to find.
  for t in SAVETEST METATEST SHIPTEST CRASHTEST; do
    line=$( cd "$OUT" && "${RUN[@]}" env "SIGHTLINE_$t=1" ./Sightline 2>/dev/null | grep -E "^$t: " || true )
    echo "   ${line:-$t: NO OUTPUT}"
    case "$line" in *": PASS"*) ;; *) ok=0 ;; esac
  done
  if [ "$ok" != 1 ]; then
    echo
    echo "!! THIS PUBLISH IS NOT SHIPPABLE. Do not ship it." >&2
    echo "   SAVETEST/METATEST: most likely a serialization change stopped going through the" >&2
    echo "   source-generated JsonSerializerContexts in SaveGame/Display." >&2
    echo "   SHIPTEST: a bundled file (font, licence text, THIRD-PARTY-NOTICES.txt, LICENSE) is" >&2
    echo "   missing from the output directory, or a player-data writer stopped being atomic." >&2
    echo "   CRASHTEST: the crash reporter does not work in the PUBLISHED binary - most likely it" >&2
    echo "   cannot resolve the player-data directory, or its write stopped being atomic." >&2
    echo "   See docs/DISTRIBUTION.md." >&2
    exit 1
  fi
fi

# ── P11 THE CRASH FILE: THE WINDOWS CONSOLE WINDOW, VERIFIED ON THE ARTIFACT ────────────────
# C6 left "on Windows the player gets a black console window behind the game" open, and nothing
# here can RUN a Windows binary to check a fix. But the defect is not a runtime behaviour — it is
# ONE FIELD IN THE FILE, the PE optional header's Subsystem word, and that is readable from Linux.
#   3 = IMAGE_SUBSYSTEM_WINDOWS_CUI -> the OS gives the process a console window   (the defect)
#   2 = IMAGE_SUBSYSTEM_WINDOWS_GUI -> it does not                                 (the fix)
# Sightline.csproj sets OutputType=WinExe for win-* RIDs; this reads back what that produced, so
# the claim in docs/DISTRIBUTION.md section 7 is a MEASUREMENT of the shipped artifact rather than
# a statement about a build flag. A win publish whose subsystem is not 2 FAILS here.
#
# HONEST SCOPE: this proves the console window is gone. It does NOT prove the harness still prints
# on Windows (Crash.AttachWindowsConsole, unverified — section 7 carries the command that closes
# that), and it does not prove the game runs there at all.
case "$RID" in
  win-*)
    EXE="$OUT/Sightline.exe"
    if [ ! -f "$EXE" ]; then
      echo "!! no $EXE to check" >&2; exit 1
    fi
    echo
    echo ">> verifying the Windows subsystem field of the published .exe"
    SUB=$(python3 - "$EXE" <<'PYEOF'
import sys, struct
b = open(sys.argv[1], 'rb').read()
pe = struct.unpack_from('<I', b, 0x3C)[0]
if b[pe:pe+4] != b'PE\0\0':
    print("notPE"); raise SystemExit
print(struct.unpack_from('<H', b, pe + 24 + 68)[0])
PYEOF
)
    case "$SUB" in
      2) echo "   PE subsystem: 2 (IMAGE_SUBSYSTEM_WINDOWS_GUI) - no console window. OK" ;;
      3) echo "   PE subsystem: 3 (IMAGE_SUBSYSTEM_WINDOWS_CUI) - a console window WILL appear behind the game." >&2
         echo "!! THIS PUBLISH IS NOT SHIPPABLE. OutputType is not WinExe for this RID;" >&2
         echo "   see the OutputType block in Sightline.csproj and docs/DISTRIBUTION.md section 7." >&2
         exit 1 ;;
      *) echo "   PE subsystem: could not be read ('$SUB'). Not failing the publish on an unreadable" >&2
         echo "   header, but do not claim the console-window fix without checking it by hand." >&2 ;;
    esac
    ;;
esac

echo
echo ">> done. Ship the whole '$OUT' directory."
