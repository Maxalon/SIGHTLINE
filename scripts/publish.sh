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
# which re-proves the artifact by running SAVETEST + METATEST + SHIPTEST against the binary it just
# built. Sightline.csproj's C6GuardTrimmedPersistence target additionally makes removing either
# mitigation a build ERROR.
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

# VERIFY: the two persistence self-tests, run against the PUBLISHED binary (not the build
# output). This is exactly the check a trimmed publish used to fail while reporting "0 Errors".
if [ -x "$OUT/Sightline" ] && [ "$RID" = "linux-x64" ]; then
  echo
  echo ">> verifying persistence against the published binary"
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
  for t in SAVETEST METATEST SHIPTEST; do
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
    echo "   See docs/DISTRIBUTION.md." >&2
    exit 1
  fi
fi

echo
echo ">> done. Ship the whole '$OUT' directory."
