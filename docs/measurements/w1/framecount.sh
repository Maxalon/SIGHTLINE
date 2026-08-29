#!/usr/bin/env bash
# W1: measure the AUTOPLAY frame budget. The cap in Program.cs had been a round 20000 since it was
# written; nothing in the repo said where that number came from or whether a campaign has ever come
# close to it. Runs N fresh autoplays and prints every RESULT line with its frame count, then the
# distribution.
# READ THE p99 WITH SUSPICION AT SMALL N: at the default n=30 the "p99" is just an interpolation
# between the two largest observations, so the shipped constant is `autoMax` — the observed MAXIMUM
# — and not a quantile. Raise N well above 100 before quoting a p99 as a p99.
#   Usage: framecount.sh [N]   Env: BIN=<dir>
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg/fc"
export XDG_CONFIG_HOME="$PWD/.xdg/fc"
export SIGHTLINE_BALANCE_JSON="$PWD/.xdg/fc/balance.json"
N=${1:-30}
BIN=${BIN:-bin/Release/net8.0}
OUT=docs/measurements/w1/framecount.txt
: > "$OUT"
for i in $(seq 1 "$N"); do
  env SIGHTLINE_AUTOPLAY=1 SIGHTLINE_SMARTPLAY=1 \
    xvfb-run -a -s "-screen 0 1280x800x24" "$BIN/Sightline" 2>/dev/null \
    | grep -oE "RESULT: (WIN|LOSE|TIMEOUT) mission=[0-9]+ frame=[0-9]+" | tail -1 | sed "s/^/run$i /" | tee -a "$OUT"
done
python3 - "$OUT" <<'PY'
import re,sys
f=[int(m.group(1)) for m in (re.search(r'frame=(\d+)',l) for l in open(sys.argv[1])) if m]
f.sort()
if not f: print("NO DATA"); raise SystemExit(1)
def pct(p):
    if len(f)==1: return f[0]
    i=(len(f)-1)*p/100.0
    lo,hi=int(i),min(int(i)+1,len(f)-1)
    return f[lo]+(f[hi]-f[lo])*(i-lo)
print(f"n={len(f)} min={f[0]} p50={pct(50):.0f} p90={pct(90):.0f} p99={pct(99):.0f} max={f[-1]}")
print(f"3x p99 = {3*pct(99):.0f}")
PY
