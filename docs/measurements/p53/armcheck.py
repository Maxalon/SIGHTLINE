#!/usr/bin/env python3
"""P53 ARM CHECK — every chunk must name its own arm, rung and slot base.

The arm here is `levers.roomSite`: tags are `in-*` (the shipped board, a required charge INSIDE the
held room) and `out-*` (the falsification arm, all three charges outside). Both arms run
`siteGlyphs=True` — the glyphs are not the lever this time, the charge's LOCATION is — and both are
doubly forced onto arena 4 / Sabotage, asserted on the artifact because P48 was bitten twice by a
forcing flag that silently did nothing.

Usage: armcheck.py <dir>
"""
import glob, json, os, sys

d = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
bad, n = [], 0
for f in sorted(glob.glob(os.path.join(d, "*.json"))):
    tag = os.path.basename(f)[:-5]
    arm, h, b = tag.split("-")
    want_room = (arm == "in")          # "in" = the shipped board, "out" = the arm
    j = json.load(open(f))
    n += 1
    lv, ba = j.get("levers") or {}, j.get("batch") or {}
    if lv.get("roomSite") != want_room:
        bad.append(f"{tag}: levers.roomSite={lv.get('roomSite')} expected {want_room}")
    if lv.get("siteGlyphs") is not True:
        bad.append(f"{tag}: levers.siteGlyphs={lv.get('siteGlyphs')} — BOTH arms must run the glyphs ON")
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
