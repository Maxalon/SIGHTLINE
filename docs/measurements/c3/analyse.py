#!/usr/bin/env python3
"""C3 — the new baseline. Reads docs/measurements/c3/C3-<arm>-h<h>-b<b>.json.

Per arm and rung it prints the campaign win rate, with a cluster SE over the 16 slot sets
(one cluster per chunk), and the greedy and sloppy legs separately. It also prints where losses
end (by mission number) and why. Finally it pairs the two arms on the same (heat, base, slot,
policy) world: the flat arm is SIGHTLINE_BOARDCURVE=0, so the pairing prices the curve alone.
ARM CHECK: every chunk's levers.board must match its arm, and runLength must be 10.
"""
import json, glob, os, re, math, collections, sys
D = os.path.dirname(os.path.abspath(__file__))
RUNGS = [-1, 0, 2, 4, 6, 8]
def lab(h): return "RECRUIT" if h < 0 else f"h{h}"
data = collections.defaultdict(list)   # (arm,h) -> list of (base, campaigns)
bad = []
for f in sorted(glob.glob(os.path.join(D, "C3-*-h*-b*.json"))):
    m = re.search(r"C3-(curve|flat)-h(-?\d+)-b(\d+)\.json$", f)
    arm, h, b = m.group(1), int(m.group(2)), int(m.group(3))
    j = json.load(open(f))
    lv = j.get("levers", {})
    want = "curve:" if arm == "curve" else "18x11@"
    if not str(lv.get("board", "")).startswith(want): bad.append(f"{f}: board={lv.get('board')}")
    if lv.get("runLength") != 10: bad.append(f"{f}: runLength={lv.get('runLength')}")
    data[(arm, h)].append((b, j["campaigns"]))
print("ARM CHECK:", "PASS" if not bad else "FAIL"); [print("  ", x) for x in bad]

def winrate(cs): return 100.0 * sum(1 for c in cs if c["win"]) / len(cs) if cs else float("nan")
def cluster_se(chunks):
    rates = [winrate(cs) for _, cs in chunks]
    k = len(rates)
    if k < 2: return float("nan")
    mu = sum(rates) / k
    return math.sqrt(sum((r - mu) ** 2 for r in rates) / (k - 1) / k)

for arm in ("curve", "flat"):
    print(f"\n== ARM {arm} ==")
    print(f"{'rung':8} {'win%':>6} {'cSE':>5} {'n':>4} {'greedy':>7} {'sloppy':>7}  chunks")
    for h in RUNGS:
        ch = data.get((arm, h), [])
        allc = [c for _, cs in ch for c in cs]
        if not allc: continue
        g = [c for c in allc if c["policy"] == "greedy"]; s = [c for c in allc if c["policy"] == "sloppy"]
        print(f"{lab(h):8} {winrate(allc):6.1f} {cluster_se(ch):5.2f} {len(allc):4} {winrate(g):7.1f} {winrate(s):7.1f}  {len(ch)}")
    print("  where a LOST campaign ends (mission #), and why:")
    for h in RUNGS:
        allc = [c for _, cs in data.get((arm, h), []) for c in cs]
        lost = [c for c in allc if not c["win"]]
        if not lost: continue
        em = collections.Counter(c["endMission"] for c in lost)
        lc = collections.Counter(c["lossCause"] or "?" for c in lost)
        print(f"  {lab(h):8} n_lost={len(lost):3}  m: " + " ".join(f"{m}:{em.get(m,0)}" for m in range(1, 11))
              + "   " + ", ".join(f"{k} {v}" for k, v in lc.most_common()))

print("\n== PAIRED: curve vs flat on the same world (heat, base, slot, policy) ==")
print(f"{'rung':8} {'curve':>6} {'flat':>6} {'delta':>6} {'b':>4} {'c':>4} {'z':>6}   (b = curve wins/flat loses)")
for h in RUNGS:
    idx = {}
    for b, cs in data.get(("flat", h), []):
        for c in cs: idx[(b, c["slot"], c["policy"])] = c["win"]
    pairs = []
    for b, cs in data.get(("curve", h), []):
        for c in cs:
            k = (b, c["slot"], c["policy"])
            if k in idx: pairs.append((c["win"], idx[k]))
    if not pairs: continue
    bb = sum(1 for a, f in pairs if a and not f); cc = sum(1 for a, f in pairs if f and not a)
    z = (bb - cc) / math.sqrt(bb + cc) if bb + cc else 0.0
    cw = 100.0 * sum(a for a, _ in pairs) / len(pairs); fw = 100.0 * sum(f for _, f in pairs) / len(pairs)
    print(f"{lab(h):8} {cw:6.1f} {fw:6.1f} {cw - fw:+6.1f} {bb:4} {cc:4} {z:+6.2f}   n={len(pairs)}")
