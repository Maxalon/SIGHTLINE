#!/usr/bin/env bash
# P15 THE UNVERIFIED — the SCRIPT-side regression, as fixtures.
#
# Findings 2 and 4 are defects in analysis SCRIPTS, so their regression test is a chunk JSON that
# the old script accepts (or shrugs at) and the new one must not. Every fixture here is REAL —
# produced by the Release binary of this tree, not hand-written — and every "before" run below is
# the ACTUAL pre-wave script, fetched out of git at the wave's base commit, so the comparison
# cannot drift with an edited copy.
#
#   bash docs/measurements/p15/regress.sh
# Exit 0 = every fixture behaves as it must, before AND after.
set -u
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ROOT=$(cd "$HERE/../../.." && pwd)
BASE=${BASE:-a933cfe}                    # the wave's base commit — where the OLD scripts live
FX="$HERE/fixtures"
TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT
fails=0
note() { echo; echo "──────── $* ────────"; }
verdict() { # verdict <what> <expected: OK|BAD> <actual-exit>
  if [ "$2" = "OK" ] && [ "$3" = "0" ]; then echo "  ✓ $1 — accepted, as it must be"
  elif [ "$2" = "BAD" ] && [ "$3" != "0" ]; then echo "  ✓ $1 — REJECTED (exit $3), as it must be"
  else echo "  ✗ $1 — expected $2, got exit $3"; fails=$((fails+1)); fi
}

# ══ FINDING 2: the chunk runner hard-codes runs == N*2 ═══════════════════════════════════
# `fx-single-h4-b0.json` is a real SIGHTLINE_BALANCE_SLOPPY=1 chunk: N=3, ONE policy leg, runs=3.
# It is a perfectly good measurement and the exact batch shape `campaigns[]` was shipped to enable.
note "FINDING 2 — a legitimate SINGLE-POLICY chunk (N=3, sloppy, runs=3)"
echo "BEFORE (the N*2 line every chunk runner in this repo carries):"
for f in fx-good fx-single; do
  GOT=$(python3 -c "import json;print(json.load(open('$FX/$f-h4-b0.json'))['runs'])")
  EXPECT=$((3*2))
  if [ "$GOT" = "$EXPECT" ]; then echo "  OK   $f (runs=$GOT)"; else echo "  BAD  $f (runs=$GOT, wanted $EXPECT)   <-- a good chunk, marked BAD"; fi
done
echo "AFTER (check_chunk.py, against the batch's own expectedRuns):"
python3 "$HERE/check_chunk.py" "$FX/fx-single-h4-b0.json" --heat 4 --base 0 --pinned; verdict "fx-single" OK $?
python3 "$HERE/check_chunk.py" "$FX/fx-good-h4-b0.json"   --heat 4 --base 0 --pinned; verdict "fx-good" OK $?

# ══ FINDING 1 (script half): the chunk's NAME is now checkable ═══════════════════════════
# `fx-unnamed-h4-b0.json` is a real chunk run with NO SIGHTLINE_BALANCE_HEAT at all — the batch
# cycled {0,2,4,6,8} — but it is FILED under -h4, which is exactly what a typo'd or unexported
# heat produced before the batch learned to refuse. runs=6 == N*2, so the old check said OK.
note "FINDING 1 (script half) — a chunk FILED as h4 that measured the cycled ladder"
GOT=$(python3 -c "import json;print(json.load(open('$FX/fx-unnamed-h4-b0.json'))['runs'])")
echo "BEFORE: runs=$GOT == N*2 -> the old layer (c) prints OK. The rung is never looked at."
echo "AFTER:"
python3 "$HERE/check_chunk.py" "$FX/fx-unnamed-h4-b0.json" --heat 4 --base 0 --pinned; verdict "fx-unnamed" BAD $?
echo "  (and on the current binary this chunk is unreachable at all: a typo'd _HEAT exits 3.)"

# ══ FINDING 4: cluster.py's LEAK-CHECK downgrades to a note and exits 0 ══════════════════
# Build two rounds out of the real fixtures: one all-pinned, one with a SINGLE unpinned chunk
# (which really does leak — campaignsRaised=2, missionsAbovePin=7, maxHeatEnd=5 on a "heat 4" chunk).
note "FINDING 4 — one unpinned chunk in a pinned round"
mkdir -p "$TMP/mixed" "$TMP/pinned"
for h in R 0 2 4 6 8; do
  cp "$FX/fx-good-h4-b0.json" "$TMP/pinned/PIN-h$h-b0.json"
  cp "$FX/fx-good-h4-b0.json" "$TMP/mixed/MIX-h$h-b0.json"
done
cp "$FX/fx-unpinned-h4-b0.json" "$TMP/mixed/MIX-h4-b0.json"     # the one bad chunk in 6
git -C "$ROOT" show "$BASE:docs/measurements/l5/cluster.py" > "$TMP/cluster_before.py" 2>/dev/null \
  || { echo "  ! could not fetch $BASE:docs/measurements/l5/cluster.py"; fails=$((fails+1)); }
if [ -s "$TMP/cluster_before.py" ]; then
  echo "BEFORE (cluster.py at $BASE), the MIXED round:"
  python3 "$TMP/cluster_before.py" MIX --dir "$TMP/mixed" 2>&1 | sed -n '/LEAK-CHECK\|unpinned prefix/p' | sed 's/^/  /'
  rc=${PIPESTATUS[0]}
  python3 "$TMP/cluster_before.py" MIX --dir "$TMP/mixed" >/dev/null 2>&1; rc=$?
  echo "  exit=$rc   <-- a round with an off-instrument chunk reported as clean"
  [ "$rc" = "0" ] || { echo "  ! expected the OLD script to exit 0 here"; fails=$((fails+1)); }
fi
echo "AFTER (cluster.py in this tree), the MIXED round:"
python3 "$ROOT/docs/measurements/l5/cluster.py" MIX --dir "$TMP/mixed" 2>&1 | sed -n '/LEAK-CHECK/,+2p' | sed 's/^/  /'
python3 "$ROOT/docs/measurements/l5/cluster.py" MIX --dir "$TMP/mixed" >/dev/null 2>&1; verdict "mixed round" BAD $?
echo "AFTER, the ALL-PINNED control round (must still pass):"
python3 "$ROOT/docs/measurements/l5/cluster.py" PIN --dir "$TMP/pinned" 2>&1 | sed -n '/LEAK-CHECK/p' | sed 's/^/  /'
python3 "$ROOT/docs/measurements/l5/cluster.py" PIN --dir "$TMP/pinned" >/dev/null 2>&1; verdict "pinned round" OK $?
echo "AFTER, an ALL-UNPINNED round (the documented bridge arm — reported, not failed):"
mkdir -p "$TMP/bridge"; for h in R 0 2 4 6 8; do cp "$FX/fx-unpinned-h4-b0.json" "$TMP/bridge/BRG-h$h-b0.json"; done
python3 "$ROOT/docs/measurements/l5/cluster.py" BRG --dir "$TMP/bridge" 2>&1 | sed -n '/LEAK-CHECK/,+1p' | sed 's/^/  /'
python3 "$ROOT/docs/measurements/l5/cluster.py" BRG --dir "$TMP/bridge" >/dev/null 2>&1; verdict "bridge round" OK $?

# ══ the L5 ladder of record must still read exactly as published ═════════════════════════
note "REGRESSION — the L5 archive through the fixed cluster.py"
python3 "$ROOT/docs/measurements/l5/cluster.py" L5 2>&1 | sed -n '/LEAK-CHECK/p' | sed 's/^/  /'
python3 "$ROOT/docs/measurements/l5/cluster.py" L5 >/dev/null 2>&1; verdict "L5 (96 chunks, all pinned)" OK $?

echo
if [ "$fails" = "0" ]; then echo "P15 REGRESS: PASS (every fixture behaves as it must, before and after)"; else echo "P15 REGRESS: FAIL ($fails)"; fi
exit $((fails != 0))
