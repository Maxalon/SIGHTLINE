#!/usr/bin/env bash
# P24 — re-derive EVERY chunk's assertion line FROM THE ARTIFACTS, not from the runner's stdout.
#
# The runner already asserted each chunk as it landed (P24-chunks.txt is its own log). This
# re-derivation is the version that survives a re-run, a partial round or a lost log: it walks the
# JSONs on disk, reads the rung and the slot base OUT OF THE FILE NAME, and makes p15/check_chunk.py
# prove the artifact agrees — plus P24's fourth layer, `levers.aimTrim` against the arm in the name.
#
#   bash docs/measurements/p24/chunks.sh > docs/measurements/p24/P24-chunks.txt
set -u
cd "$(dirname "$0")/../../.." || exit 1
OUT=docs/measurements/p24
ok=0; bad=0
for f in "$OUT"/P24-*-h*-b*.json; do
  bn=$(basename "$f" .json)                       # P24-<arm>-h<H>-b<B>
  rest=${bn#P24-}
  arm=${rest%%-h*}
  hb=${rest#*-h}
  H=${hb%%-b*}
  B=${hb#*-b}
  case "$arm" in base) want=0 ;; aim) want=5 ;; *) echo "BAD  $bn — unknown arm"; bad=$((bad+1)); continue ;; esac
  line=$(python3 docs/measurements/p15/check_chunk.py "$f" --heat "$H" --base "$B" --pinned 2>&1 | tail -1)
  arml=$(python3 - "$f" "$want" <<'PY'
import json, sys
j = json.load(open(sys.argv[1])); w = int(sys.argv[2])
g = (j.get("levers") or {}).get("aimTrim")
print("aimTrim=%s" % g if g == w else "*** WRONG ARM aimTrim=%s want=%s ***" % (g, w))
PY
)
  case "$line" in
    OK*) echo "OK   $bn runs=$(python3 -c "import json;print(json.load(open('$f'))['runs'])") $arml"
         case "$arml" in \*\*\**) bad=$((bad+1)) ;; *) ok=$((ok+1)) ;; esac ;;
    *)   echo "BAD  $bn $line $arml"; bad=$((bad+1)) ;;
  esac
done
echo "CHUNKS (P24, re-derived from the artifacts): ok=$ok bad=$bad"
