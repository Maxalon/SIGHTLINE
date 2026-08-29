#!/usr/bin/env python3
"""Paired mult->add instrument deltas per rung (identical worlds, so these carry no sampling
error in the COMPARISON — only the rung-to-rung spread does)."""
import json, os
OUT = os.path.dirname(os.path.abspath(__file__))

def dr(tag):
    return json.load(open(os.path.join(OUT, tag + ".json")))["decisionRichness"]

pairs = {"0": ("mult-h0", "add-h0"), "4": ("mult-h4b", "add-h4b"), "8": ("mult-h8b", "add-h8b")}
keys = [("ch/ARMED", "choicesPerArmedSoldierTurn"),
        ("tgt/ARMED", "targetChoicesPerArmedSoldierTurn"),
        ("pos/ARMED", "positionChoicesPerArmedSoldierTurn")]
for h, (m, a) in pairs.items():
    M, A = dr(m), dr(a)
    out = []
    for label, k in keys:
        out.append(f"{label} {M[k]:.3f}->{A[k]:.3f} ({A[k]-M[k]:+.3f})")
    print(f"heat {h}: " + "  ".join(out))

print()
for label, k in keys:
    mv = [dr(pairs[h][0])[k] for h in ("0", "4", "8")]
    av = [dr(pairs[h][1])[k] for h in ("0", "4", "8")]
    print(f"{label:10} mult h0/h4/h8 {mv[0]:.3f}/{mv[1]:.3f}/{mv[2]:.3f} "
          f"spread {max(mv)-min(mv):.3f} ({100*(max(mv)-min(mv))/(sum(mv)/3):.1f}% of mean)   "
          f"add {av[0]:.3f}/{av[1]:.3f}/{av[2]:.3f} "
          f"spread {max(av)-min(av):.3f} ({100*(max(av)-min(av))/(sum(av)/3):.1f}% of mean)")
