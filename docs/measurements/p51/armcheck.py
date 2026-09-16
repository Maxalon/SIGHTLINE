#!/usr/bin/env python3
"""P51 ARM CHECK — every chunk must name its own arm, rung and slot base.

P15 and C4 were each bitten by a chunk that did not measure what its FILE NAME said, which is why
the balance artifact carries a `levers{}` block at all. This asserts the artifact's own
`levers.siteGlyphs`, `batch.heat` and `batch.slotBase` against the tag it was archived under — so
the file name is checkable rather than trusted.

Usage: armcheck.py <dir>
"""
import glob, json, os, sys

d = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
bad, n = [], 0
for f in sorted(glob.glob(os.path.join(d, "*.json"))):
    tag = os.path.basename(f)[:-5]
    arm, h, b = tag.split("-")
    # tags are `[f]off|[f]on` — the optional `f` marks the FORCED-ARENA round, so match the
    # suffix rather than the whole word. (Matching the whole word marked all 48 `fon` chunks BAD.)
    want_arm = arm.endswith("on")
    j = json.load(open(f))
    n += 1
    lv, ba = j.get("levers") or {}, j.get("batch") or {}
    if lv.get("siteGlyphs") != want_arm:
        bad.append(f"{tag}: levers.siteGlyphs={lv.get('siteGlyphs')} expected {want_arm}")
    if ba.get("heat") != int(h[1:]):
        bad.append(f"{tag}: batch.heat={ba.get('heat')} expected {h[1:]}")
    if ba.get("slotBase") != int(b[1:]):
        bad.append(f"{tag}: batch.slotBase={ba.get('slotBase')} expected {b[1:]}")
    if j.get("runs") != ba.get("expectedRuns"):
        bad.append(f"{tag}: runs={j.get('runs')} expectedRuns={ba.get('expectedRuns')}")
    if not (j.get("heatLeak") or {}).get("pinned", False):
        bad.append(f"{tag}: heatLeak.pinned is false")
    if j.get("envErrors"):
        bad.append(f"{tag}: envErrors {j['envErrors']}")
    # P49's instrument is DOUBLY forced: one arena and one objective. Assert both on the artifact,
    # because a forcing flag that silently fails is exactly what P48 was bitten by twice.
    arenas = {r["arena"] for r in (j.get("byArena") or [])}
    if arenas != {4}:
        bad.append(f"{tag}: byArena={sorted(arenas)} expected only arena 4")
    objs = {r.get("objective") for r in (j.get("byObjective") or [])}
    if objs and objs != {"Sabotage"}:
        bad.append(f"{tag}: byObjective={sorted(str(o) for o in objs)} expected only Sabotage")

print(f"ARM CHECK over {n} chunks: " + ("PASS" if not bad else f"FAIL ({len(bad)})"))
for b in bad[:20]:
    print("   " + b)
sys.exit(1 if bad else 0)
