#!/usr/bin/env bash
# Derive CHANGELOG.md from git history. NOT CI — a hand-run script, called by
# scripts/publish.sh on every publish and runnable on its own to preview.
#
#   bash scripts/changelog.sh                          # print to stdout
#   bash scripts/changelog.sh --version 1.0.0          # override the version being cut
#   bash scripts/changelog.sh --out dist/x/CHANGELOG.md
#
# ── WHY IT IS DERIVED AND NOT WRITTEN ───────────────────────────────────────────────────────
# This repository has been burned by hand-maintained registries five separate times: the sweep's
# self-test count was wrong SIX times (41/46/49/51/53 each claimed while a different number ran),
# CLAUDE.md's "free keys" line advertised four letters that were already bound, Ship.RequiredFiles
# shipped listing six files while the document it guards said ten, and a Heat.Mods row declared a
# tooth that could never fire for two whole programs. Every one of them was a list a human kept in
# sync by intention. A hand-written changelog is the same object and would rot the same way — and
# it rots WORSE, because nobody re-reads a changelog to check it.
#
# ── THE SHAPE, AND WHY THIS ONE ─────────────────────────────────────────────────────────────
# `git log --first-parent` is the source of truth. This project lands work as ONE merge per wave
# or milestone, with a subject line that already reads like a release note
# ("Merge wave C3 THE TWO GAMES — the objective-class gap, and the gate that never was"), so the
# first-parent walk IS the changelog at exactly the granularity a reader wants: 71 lines for 676
# commits. Walking every commit instead would produce a 676-line wall nobody reads; curating by
# hand would produce a list that disagrees with the history. Neither is better than the thing the
# repository already maintains for free.
#
# Sections are cut at TAGS (`v*`, newest first), so once `v1.0.0` exists the next publish emits
# only what landed after it under the new version's heading and leaves the earlier sections alone.
# Before any tag exists — which is the state this script was written in — the whole history goes
# under the version being published, which is correct for a first release.
#
# HONEST SCOPE: it reports what was MERGED, not what a player will NOTICE. A wave that only moved
# measurements gets a line here exactly like a wave that added a mode. Fixing that would mean a
# curated list, which is the thing this script exists to not be.
set -euo pipefail
cd "$(dirname "$0")/.."

VER=""
OUTFILE=""
for ((i = 1; i <= $#; i++)); do
  case "${!i}" in
    --version) i=$((i + 1)); VER="${!i}" ;;
    --out)     i=$((i + 1)); OUTFILE="${!i}" ;;
    -h|--help) sed -n '2,40p' "$0"; exit 0 ;;
    *) echo "unknown argument: ${!i}" >&2; exit 2 ;;
  esac
done

# The version being cut comes from the ONE place it is stamped (Sightline.csproj <Version>, which
# Ship.Version reads back off the assembly at runtime). Never a second copy.
if [ -z "$VER" ]; then
  VER=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' Sightline.csproj | head -1)
fi
[ -n "$VER" ] || { echo "changelog.sh: could not read <Version> from Sightline.csproj" >&2; exit 2; }

emit() { if [ -n "$OUTFILE" ]; then cat > "$OUTFILE"; else cat; fi; }

HEAD_LINE='# SIGHTLINE — changelog'
PREAMBLE="\
_Derived from git history by \`scripts/changelog.sh\` at publish time — one entry per change
landed on the main line (\`git log --first-parent\`). Do not hand-edit: it is regenerated on
every \`bash scripts/publish.sh\` and any edit is lost. Sections are cut at release tags._"

if ! git rev-parse --git-dir >/dev/null 2>&1; then
  # Publishing from an export with no history. Say so IN the file rather than shipping a
  # confident-looking empty changelog — a reader must be able to tell the difference.
  {
    echo "$HEAD_LINE"; echo
    echo "$PREAMBLE"; echo
    echo "## v$VER — $(date -u +%Y-%m-%d)"; echo
    echo "- No git history was available where this build was published, so this changelog could not be derived."
  } | emit
  exit 0
fi

section() {   # section <heading> <git-log-range...>
  local heading="$1"; shift
  local body
  body=$(git log --first-parent --pretty=format:'- %s (`%h`)' "$@" 2>/dev/null || true)
  [ -n "$body" ] || return 0
  echo "$heading"; echo
  echo "$body"; echo
}

{
  echo "$HEAD_LINE"; echo
  echo "$PREAMBLE"; echo

  mapfile -t TAGS < <(git tag -l 'v*' --sort=-creatordate 2>/dev/null || true)
  HEAD_SHA=$(git rev-parse --short HEAD)
  HEAD_DATE=$(git log -1 --format=%ad --date=short)

  if [ "${#TAGS[@]}" -eq 0 ]; then
    section "## v$VER — $HEAD_DATE (commit \`$HEAD_SHA\`; not yet tagged)" HEAD
  else
    if [ "${TAGS[0]}" != "v$VER" ]; then
      section "## v$VER — $HEAD_DATE (commit \`$HEAD_SHA\`; not yet tagged)" "${TAGS[0]}..HEAD"
    fi
    for ((t = 0; t < ${#TAGS[@]}; t++)); do
      tag="${TAGS[t]}"
      tdate=$(git log -1 --format=%ad --date=short "$tag")
      if [ $((t + 1)) -lt "${#TAGS[@]}" ]; then
        section "## ${tag#v} — $tdate" "${TAGS[t+1]}..$tag"
      else
        section "## ${tag#v} — $tdate" "$tag"
      fi
    done
  fi
} | emit
