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
#   bash scripts/publish.sh --tag           # ...and create the LOCAL annotated release tag (never pushes)
#   bash scripts/publish.sh --no-archive    # stop at the directory; skip the release artefact
#
# Output is ELEVEN files: ONE self-contained executable, libraylib.so (a native library the runtime
# dlopen()s — it cannot be linked into the single file), the assets/ directory (two fonts, their
# two OFL licence texts, two CREDITS ledgers), THIRD-PARTY-NOTICES.txt, LICENSE, and (P17)
# CHANGELOG.md, derived from git history at publish time by scripts/changelog.sh.
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
# P17: THE TABLE ABOVE IS STALE ON BOTH SIZE AND FILE COUNT and is kept for provenance only. The
# payload is ELEVEN files now (CHANGELOG.md), and the binary grew ~1.6 MB across C1-C6/P10-P16:
# release measured 28.2 MB exe / 30.9 MB directory on e57e151+P17. Quote the row you measured;
# docs/DISTRIBUTION.md section 2 carries the correction.
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
TAG=0
ARCHIVE=1
RUN=(env)      # replaced with xvfb-run below when a display is needed and none is present
for ((i = 1; i <= $#; i++)); do
  case "${!i}" in
    --small)   MODE="small" ;;
    --no-trim) MODE="no-trim" ;;
    --plain)   MODE="plain" ;;
    --rid)     i=$((i + 1)); RID="${!i}" ;;
    --out)     i=$((i + 1)); OUT="${!i}" ;;
    --tag)     TAG=1 ;;
    --no-archive) ARCHIVE=0 ;;
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
    # P17: the version stamp the BINARY reports, taken off the line SHIPTEST already prints. The
    # archive is named from it, so the name can never disagree with the build inside it.
    case "$t" in SHIPTEST) BINVER=$(printf '%s' "$line" | sed -n 's/.*stamped v\([0-9][0-9.]*\).*/\1/p') ;; esac
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

# ── P17 SHIPS AS v1.0.0 — THE RELEASE ARTEFACT ─────────────────────────────────────────────
# Everything above produces a DIRECTORY. A directory is not a thing you can hand someone: it
# cannot be attached, downloaded, or checked for damage, and it carries no statement of what
# changed. This block turns it into one file with a name, a checksum and a changelog.
#
#   dist/SIGHTLINE-v<version>-<rid>[-<mode>].tar.gz          the archive (.zip for win-* RIDs)
#   dist/SIGHTLINE-v<version>-<rid>[-<mode>].tar.gz.sha256   the checksum, in sha256sum(1) format
#   <OUT>/CHANGELOG.md (and a copy beside the archive)       derived from git; see scripts/changelog.sh
#
# THE VERSION IN THE NAME IS THE ONE THE BINARY REPORTS, not one read out of the csproj a second
# time: it is scraped from the SHIPTEST line printed above ("build stamped v1.0.0"), so a name and
# a build cannot disagree. If that scrape failed we fall back to the csproj and SAY we did.
#
# A recipient verifies the download with, from the directory holding both files:
#     sha256sum -c SIGHTLINE-v1.0.0-linux-x64.tar.gz.sha256
if [ "$ARCHIVE" = 1 ]; then
  VER="${BINVER:-}"
  if [ -z "$VER" ]; then
    VER=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' Sightline.csproj | head -1)
    echo "   note: could not read the version off the binary; using <Version> from Sightline.csproj ($VER)" >&2
  fi
  [ -n "$VER" ] || { echo "!! no version to name the archive from" >&2; exit 1; }

  STAGE_NAME="SIGHTLINE-v$VER-$RID"
  [ "$MODE" = "release" ] || STAGE_NAME="$STAGE_NAME-$MODE"
  ARCDIR="$(cd "$(dirname "$OUT")" && pwd)"
  case "$RID" in win-*) EXT="zip" ;; *) EXT="tar.gz" ;; esac
  ARC="$ARCDIR/$STAGE_NAME.$EXT"

  echo
  echo ">> deriving CHANGELOG.md from git history"
  bash scripts/changelog.sh --version "$VER" --out "$OUT/CHANGELOG.md"
  echo "   $(grep -c '^- ' "$OUT/CHANGELOG.md") entries, $(wc -c < "$OUT/CHANGELOG.md") bytes"
  echo "   the shippable directory is now $(find "$OUT" -type f | wc -l) files:"
  ( cd "$OUT" && find . -type f | sed 's,^\./,     ,' | sort )

  echo
  echo ">> packing $STAGE_NAME.$EXT"
  # Stage under a directory named for the release so the archive unpacks into ONE folder instead
  # of spraying eleven files into whatever directory the recipient was standing in.
  STAGE_ROOT=$(mktemp -d)
  trap 'rm -rf "$STAGE_ROOT"' EXIT
  cp -a "$OUT" "$STAGE_ROOT/$STAGE_NAME"
  rm -f "$ARC" "$ARC.sha256"
  if [ "$EXT" = "zip" ]; then
    ( cd "$STAGE_ROOT" && zip -qr "$ARC" "$STAGE_NAME" )
  else
    # --owner/--group/--numeric-owner: do not leak this container's uid into a distributable.
    # --sort=name: the same input directory packs to the same bytes.
    tar -czf "$ARC" -C "$STAGE_ROOT" --owner=0 --group=0 --numeric-owner --sort=name "$STAGE_NAME"
  fi
  cp -f "$OUT/CHANGELOG.md" "$ARCDIR/CHANGELOG.md"
  rm -rf "$STAGE_ROOT"; trap - EXIT

  # The checksum is written with a BARE filename so `sha256sum -c` works in the directory a
  # recipient downloaded into, and is immediately verified here — an unverified checksum file is
  # decoration.
  ( cd "$ARCDIR" && sha256sum "$STAGE_NAME.$EXT" > "$STAGE_NAME.$EXT.sha256" )
  if ! ( cd "$ARCDIR" && sha256sum -c "$STAGE_NAME.$EXT.sha256" >/dev/null ); then
    echo "!! the checksum does not verify against the archive just written. Do not ship it." >&2
    exit 1
  fi
  echo "   $ARC"
  echo "   $(du -h "$ARC" | cut -f1)  ($(stat -c%s "$ARC") bytes)"
  echo "   $(cat "$ARCDIR/$STAGE_NAME.$EXT.sha256")"

  # ...and now judge the RELEASE, not the build: SHIPTEST's release legs re-derive the archive's
  # SHA-256 in-process and compare it with the recorded one, and check the changelog's shape.
  # Only on a RID this machine can execute — a cross-published Windows archive is checked by the
  # sha256sum -c above and NOT by the binary, which is stated rather than implied.
  if [ -x "$OUT/Sightline" ] && [ "$RID" = "linux-x64" ]; then
    echo
    echo ">> verifying the release artefact against the published binary"
    line=$( cd "$OUT" && "${RUN[@]}" env SIGHTLINE_SHIPTEST=1 "SIGHTLINE_RELEASEDIR=$ARCDIR" ./Sightline 2>/dev/null | grep -E "^SHIPTEST: " || true )
    echo "   ${line:-SHIPTEST: NO OUTPUT}"
    case "$line" in
      *": PASS"*) ;;
      *) echo "!! the release artefact does not verify. Do not ship it." >&2; exit 1 ;;
    esac
  else
    echo
    echo "   (release legs not run: $RID cannot be executed here. The archive and its checksum"
    echo "    were still written and verified with sha256sum -c above.)"
  fi
fi

# ── THE TAG ────────────────────────────────────────────────────────────────────────────────
# A release needs a point in history you can go back to. This creates a LOCAL annotated tag and
# NOTHING ELSE — it never pushes, never touches a remote, and is opt-in behind --tag, because a
# tag is a claim about a commit and the script has no business making one on its own. The push is
# printed for a human to run. Refuses on a dirty tree or an existing tag rather than tagging
# something that is not what was just published and verified.
if [ "$TAG" = 1 ]; then
  echo
  VER="${BINVER:-$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' Sightline.csproj | head -1)}"
  if ! git rev-parse --git-dir >/dev/null 2>&1; then
    echo "!! --tag: not a git repository" >&2; exit 1
  elif [ -n "$(git status --porcelain)" ]; then
    echo "!! --tag: the working tree is dirty. A release tag must name the tree that was built." >&2
    git status --short | sed 's/^/     /' >&2; exit 1
  elif git rev-parse -q --verify "refs/tags/v$VER" >/dev/null; then
    echo "!! --tag: v$VER already exists (at $(git rev-parse --short "v$VER")). Bump <Version> in" >&2
    echo "   Sightline.csproj, or delete the tag deliberately." >&2; exit 1
  else
    git tag -a "v$VER" -m "SIGHTLINE v$VER"
    echo ">> created LOCAL tag v$VER at $(git rev-parse --short HEAD). Nothing was pushed."
    echo "   To publish it:   git push origin v$VER"
  fi
fi

echo
echo ">> done. Ship the whole '$OUT' directory, or the single archive beside it."
if [ "$ARCHIVE" = 1 ] && [ "$TAG" != 1 ]; then
  VER="${BINVER:-$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' Sightline.csproj | head -1)}"
  echo "   To tag this release (LOCAL only; --tag does the same and refuses on a dirty tree):"
  echo "     git tag -a v$VER -m 'SIGHTLINE v$VER' && git push origin v$VER"
fi
