#!/usr/bin/env python3
"""P55 ARM CHECK — every chunk must name its own arm, rung and slot base.

The arm is `levers.vipHeat`: `on-*` is the shipped P55 asset (a heat surcharge on the Escort VIP /
Rescue captive), `off-*` is `SIGHTLINE_VIPHEAT=0`, the pre-P55 asset exactly.

UNLIKE P49-P53 THIS ROUND IS **NOT FORCED**. It runs the shipped arena and objective distribution,
so its win rates ARE comparable with the heat band — and `byArena`/`byObjective` must therefore be
DIVERSE, not singletons. Asserting that is the mirror image of the forced rounds' check, and it
catches the opposite mistake: a stray SIGHTLINE_MAP/OBJ left in the environment.

Usage: armcheck.py <dir>
"""
import glob, json, os, sys

d = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
bad, n = [], 0
for f in sorted(glob.glob(os.path.join(d, "*.json"))):
    tag = os.path.basename(f)[:-5]
    arm, h, b = tag.split("-")
    want = (arm == "on")
    j = json.load(open(f))
    n += 1
    lv, ba = j.get("levers") or {}, j.get("batch") or {}
    if lv.get("vipHeat") != want:
        bad.append(f"{tag}: levers.vipHeat={lv.get('vipHeat')} expected {want}")
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
    # the FREE instrument: a singleton here means a forcing flag leaked in from the environment
    arenas = {r["arena"] for r in (j.get("byArena") or [])}
    if len(arenas) < 5:
        bad.append(f"{tag}: byArena has only {len(arenas)} arenas — is SIGHTLINE_MAP set?")
    objs = {r.get("objective") for r in (j.get("byObjective") or [])}
    if len(objs) < 4:
        bad.append(f"{tag}: byObjective has only {sorted(str(o) for o in objs)} — is SIGHTLINE_OBJ set?")
    # and the objectives this wave is ABOUT must actually be present
    if not ({"Rescue", "Escort"} & {str(o) for o in objs}):
        bad.append(f"{tag}: neither Rescue nor Escort ran — this chunk cannot see the lever")

print(f"ARM CHECK over {n} chunks: " + ("PASS" if not bad else f"FAIL ({len(bad)})"))
for x in bad[:20]:
    print("   " + x)
sys.exit(1 if bad else 0)
