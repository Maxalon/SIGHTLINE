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
# Output is ONE self-contained executable plus libraylib.so (a native library the runtime
# dlopen()s — it cannot be linked into the single file), the assets/ directory, and
# THIRD-PARTY-NOTICES.txt. SHIP THE WHOLE OUTPUT DIRECTORY: the notice file and
# assets/NotoMono-LICENSE.txt and assets/ChakraPetch-LICENSE.txt are licence obligations,
# not optional extras.
#
# Measured on this project (linux-x64, self-contained, .NET SDK 8.0.130, this container).
# "start" = wall time for one window-free SIGHTLINE_SAVETEST launch, median of 10 after a
# warm run, so it is startup + JIT of the persistence path, not frame time:
#
#   mode        flags                                  size   start   files
#   release     PublishTrimmed + ReadyToRun + single    25 MB    95 ms     6   <- default
#   small       PublishTrimmed + single                 18 MB   350 ms     6
#   no-trim     ReadyToRun + single                     80 MB   115 ms     6
#   plain       single                                  68 MB   168 ms     6
#   (folder, no single file: 75 MB across 193 files)
#
# Trimming is what makes "small" slow: it strips the framework's precompiled ReadyToRun code,
# so everything JITs at startup. Adding ReadyToRun back costs 7 MB and buys the fastest start
# of any config — which is why the default is both.
#
# On PublishTrimmed: it USED to silently destroy all persistence (save.json AND the entire
# cross-run meta profile) because reflection-based System.Text.Json loses the type metadata
# trimming strips — the game booted, played and finished a whole campaign while saving nothing.
# That is fixed: SaveGame and Display serialise through source-generated JsonSerializerContexts,
# and Sightline.csproj roots our own assembly for the one remaining reflective site (the
# SIGHTLINE_BALANCE telemetry export, which serialises anonymous types). This script re-proves it
# on every publish by running the two persistence self-tests against the binary it just built.
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
  for t in SAVETEST METATEST; do
    line=$( cd "$OUT" && "${RUN[@]}" env "SIGHTLINE_$t=1" ./Sightline 2>/dev/null | grep -E "^$t: " || true )
    echo "   ${line:-$t: NO OUTPUT}"
    case "$line" in *": PASS"*) ;; *) ok=0 ;; esac
  done
  if [ "$ok" != 1 ]; then
    echo
    echo "!! PERSISTENCE IS BROKEN IN THIS PUBLISH CONFIG. Do not ship it." >&2
    echo "   Most likely a serialization change stopped going through the source-generated" >&2
    echo "   JsonSerializerContexts in SaveGame/Display. See docs/DISTRIBUTION.md." >&2
    exit 1
  fi
fi

echo
echo ">> done. Ship the whole '$OUT' directory."
