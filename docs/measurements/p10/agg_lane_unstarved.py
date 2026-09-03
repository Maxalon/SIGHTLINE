#!/usr/bin/env python3
"""P10 — THE LANE, MEASURED WHERE THE BRANCH IS NOT STARVED.

The shipped-ratio round could not resolve the lane: the `overwatch` branch fires 0.15-0.46% of
enemy acts, so a rung held ~200 lanes and heat 4/8 produced ZERO discordant campaigns in 320.
This contrast holds SIGHTLINE_DECLINEWATCH=1.20 fixed (a DIAGNOSTIC ratio, not a shipped one) so
the same tree holds 3,200-4,300 lanes per rung per arm, and flips SIGHTLINE_AILANE. It is
therefore the only sample in this repo with the power to say what a chosen lane is worth.
"""
import json, math, os
from math import comb

HERE = os.path.dirname(os.path.abspath(__file__))
RUNGS = [0, 4, 8]
BASES = [0, 10, 20, 30, 40, 50, 60, 70]


def mcnemar_p(b, c):
    n = b + c
    if n == 0:
        return 1.0
    k = min(b, c)
    return min(1.0, 2 * sum(comb(n, i) for i in range(k + 1)) / (2.0 ** n))


def sd(v):
    m = sum(v) / len(v)
    return math.sqrt(sum((x - m) ** 2 for x in v) / (len(v) - 1))


print(f"{'rung':>5} {'arm':>9} {'win%':>7} {'lanes':>7} {'react':>7} {'paid off':>9} "
      f"{'chunk-t(7)':>11} {'disc b/c':>10} {'McNemar p':>10}")
for h in RUNGS:
    agg = {}
    for tag in ("dw120", "dw120off"):
        w = n = lanes = react = 0
        ch = []
        wins = {}
        for b in BASES:
            d = json.load(open(os.path.join(HERE, f"p10-{tag}-h{h}-b{b}.json")))
            ed = d["enemyDecisions"]
            lanes += ed["lanesHeld"]; react += ed["reactionShots"]
            ch.append(d["runWinRate"])
            for c in d["campaigns"]:
                n += 1; w += 1 if c["win"] else 0
                wins[(b, c["slot"], c["policy"])] = c["win"]
        agg[tag] = dict(w=w, n=n, lanes=lanes, react=react, ch=ch, wins=wins)
    a, o = agg["dw120"], agg["dw120off"]
    b_ = sum(1 for k, v in a["wins"].items() if v and not o["wins"][k])
    c_ = sum(1 for k, v in a["wins"].items() if o["wins"][k] and not v)
    d = [x - y for x, y in zip(a["ch"], o["ch"])]
    t = (sum(d) / len(d)) / (sd(d) / math.sqrt(len(d))) if sd(d) > 0 else float('nan')
    for tag, label, r in (("dw120", "LANE ON", a), ("dw120off", "LANE OFF", o)):
        first = tag == "dw120"
        tcol = f"{t:>11.2f}" if first else " " * 11
        pcol = (f" {str(b_) + '/' + str(c_):>10} {mcnemar_p(b_, c_):>10.4f}") if first else ""
        print(f"h{h:<4} {label:>9} {100.0 * r['w'] / r['n']:>6.1f}% {r['lanes']:>7} {r['react']:>7} "
              f"{100.0 * r['react'] / r['lanes']:>8.1f}% {tcol}{pcol}")
    print(f"      chunk-paired delta {sum(d) / len(d):+.2f} +- {sd(d) / math.sqrt(len(d)):.2f} points "
          f"(8 chunks); discordant {b_ + c_}/{a['n']} pairs")
