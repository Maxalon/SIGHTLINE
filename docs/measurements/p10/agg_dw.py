#!/usr/bin/env python3
"""P10 — the PRICED-AND-NOT-SPENT probe on Ai.DeclineWatchRatio.

Baseline is the main round's `p10-on-*` arm (lane ON, shipped ratio 0.45); R0diag.json proves the
binary carrying the SIGHTLINE_DECLINEWATCH dial reproduces that arm byte-for-byte at the default.
Reports, per rung and per probed ratio: the win rate, the campaign-level CRN pairing, and the two
numbers the probe exists to answer — how much the `overwatch` BRANCH RATE moves, and how much of
the opponent's shot volume it costs (`declinedPct`).
"""
import json, math, os
from math import comb

HERE = os.path.dirname(os.path.abspath(__file__))
RUNGS = [0, 4, 8]
BASES = [0, 10, 20, 30, 40, 50, 60, 70]
ARMS = [("on", "0.45 (shipped)"), ("dw120", "1.20"), ("dw300", "3.00")]


def mcnemar_p(b, c):
    n = b + c
    if n == 0:
        return 1.0
    k = min(b, c)
    return min(1.0, 2 * sum(comb(n, i) for i in range(k + 1)) / (2.0 ** n))


base = {}
print(f"{'rung':>5} {'ratio':>15} {'win%':>7} {'ow branch':>10} {'acts':>7} {'ow rate':>9} "
      f"{'declined%':>10} {'lanes':>6} {'react':>6} {'vs shipped b/c':>15} {'p':>7}")
for h in RUNGS:
    for tag, label in ARMS:
        w = n = ow = acts = lanes = react = 0
        dec = []
        wins = {}
        for b in BASES:
            d = json.load(open(os.path.join(HERE, f"p10-{tag}-h{h}-b{b}.json")))
            ed = d["enemyDecisions"]
            acts += ed["contestedActs"] + ed["allDownedActs"]
            lanes += ed["lanesHeld"]; react += ed["reactionShots"]
            dec.append(ed["declinedPct"])
            for m in ed["mix"]:
                if m["verb"] == "overwatch":
                    ow += m["n"]
            for c in d["campaigns"]:
                n += 1; w += 1 if c["win"] else 0
                wins[(b, c["slot"], c["policy"])] = c["win"]
        if tag == "on":
            base[h] = wins
            bb = cc = 0
        else:
            bb = sum(1 for k, v in wins.items() if v and not base[h][k])
            cc = sum(1 for k, v in wins.items() if base[h][k] and not v)
        print(f"h{h:<4} {label:>15} {100.0 * w / n:>6.1f}% {ow:>10} {acts:>7} "
              f"{100.0 * ow / acts:>8.3f}% {sum(dec) / len(dec):>9.2f}% {lanes:>6} {react:>6} "
              f"{(str(bb) + '/' + str(cc)) if tag != 'on' else '—':>15} "
              f"{(f'{mcnemar_p(bb, cc):.4f}') if tag != 'on' else '—':>7}")
