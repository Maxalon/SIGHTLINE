#!/usr/bin/env python3
"""P10 THE HELD LANE — aggregate the CRN round.

Reads docs/measurements/p10/p10-{on,off}-h{H}-b{B}.json and reports, per rung:
  * win% per arm with binomial and CLUSTER (chunk) SE — the cluster SE is the honest one; a
    rung is 8 clusters of 40 and the clusters disagree (C3's caveat 3, made routine here).
  * the chunk-paired t on 8 chunk deltas (df=7).
  * the CAMPAIGN-level CRN pairing: b/c discordant counts, exact two-sided McNemar p, and the
    MDE at 80% power — ROADMAP demands n_discordant and the MDE beside every paired ladder,
    because a flat row with few discordant pairs is an ABSENCE of evidence, not neutrality.
  * the enemy lane telemetry (lanes held, reaction shots, the overwatch branch's own count),
    which is what this wave's lever is actually about.
"""
import json, math, os, sys
from math import comb

HERE = os.path.dirname(os.path.abspath(__file__))
RUNGS = [0, 4, 8]
BASES = [0, 10, 20, 30, 40, 50, 60, 70]


def load(arm, h, b):
    p = os.path.join(HERE, f"p10-{arm}-h{h}-b{b}.json")
    with open(p) as f:
        return json.load(f)


def mean(v):
    return sum(v) / len(v)


def sd(v):
    if len(v) < 2:
        return 0.0
    m = mean(v)
    return math.sqrt(sum((x - m) ** 2 for x in v) / (len(v) - 1))


def mcnemar_p(b, c):
    n = b + c
    if n == 0:
        return 1.0
    k = min(b, c)
    tail = sum(comb(n, i) for i in range(0, k + 1)) / (2.0 ** n)
    return min(1.0, 2 * tail)


def mde(pairs, b, c):
    """Minimum detectable effect (percentage points) at alpha=.05 two-sided, power 80%.
    MDE ~= 2.80 * sqrt(p_d / n), where p_d is the DISCORDANT rate — the quantity ROADMAP says
    a paired round's resolving power actually comes from, not n.
    With ZERO discordant pairs the observed p_d is 0 and the formula degenerates, which would
    print an MDE of 0 and claim infinite power. Use the rule of three instead: 0 events in n
    trials puts the 95% upper bound on the rate at 3/n, so the MDE is computed from that bound
    and is an UPPER bound on the round's sensitivity, flagged with a * in the table."""
    if pairs == 0:
        return float('nan'), False
    obs = (b + c) / pairs
    bounded = obs == 0
    pd = obs if obs > 0 else 3.0 / pairs
    return 100.0 * 2.80 * math.sqrt(pd / pairs), bounded


rows = []
for h in RUNGS:
    on_ch, off_ch = [], []
    on_w = on_n = off_w = off_n = 0
    pairs = bb = cc = 0
    tel = {a: dict(lanes=0, react=0, ow=0, brace=0, acts=0) for a in ("on", "off")}
    for b in BASES:
        do, df_ = load("on", h, b), load("off", h, b)
        for arm, d in (("on", do), ("off", df_)):
            ed = d["enemyDecisions"]
            tel[arm]["lanes"] += ed["lanesHeld"]
            tel[arm]["react"] += ed["reactionShots"]
            tel[arm]["acts"] += ed["contestedActs"] + ed["allDownedActs"]
            for m in ed["mix"]:
                if m["verb"] == "overwatch":
                    tel[arm]["ow"] += m["n"]
                if m["verb"] == "brace":
                    tel[arm]["brace"] += m["n"]
        on_ch.append(do["runWinRate"]); off_ch.append(df_["runWinRate"])
        on_w += sum(1 for c in do["campaigns"] if c["win"]); on_n += len(do["campaigns"])
        off_w += sum(1 for c in df_["campaigns"] if c["win"]); off_n += len(df_["campaigns"])
        ka = {(c["slot"], c["policy"]): c["win"] for c in do["campaigns"]}
        kb = {(c["slot"], c["policy"]): c["win"] for c in df_["campaigns"]}
        for k in ka:
            if k not in kb:
                continue
            pairs += 1
            if ka[k] and not kb[k]:
                bb += 1
            elif kb[k] and not ka[k]:
                cc += 1
    d = [x - y for x, y in zip(on_ch, off_ch)]
    t = mean(d) / (sd(d) / math.sqrt(len(d))) if sd(d) > 0 else float('nan')
    rows.append(dict(h=h, on=100.0 * on_w / on_n, off=100.0 * off_w / off_n, n=on_n,
                     onse=100 * math.sqrt((on_w / on_n) * (1 - on_w / on_n) / on_n),
                     offse=100 * math.sqrt((off_w / off_n) * (1 - off_w / off_n) / off_n),
                     onclu=sd(on_ch) / math.sqrt(len(on_ch)), offclu=sd(off_ch) / math.sqrt(len(off_ch)),
                     dmean=mean(d), dse=sd(d) / math.sqrt(len(d)), t=t,
                     pairs=pairs, b=bb, c=cc, p=mcnemar_p(bb, cc),
                     mde=mde(pairs, bb, cc)[0], mdeBounded=mde(pairs, bb, cc)[1], tel=tel))

print(f"P10 THE HELD LANE — CRN round.  n={rows[0]['n']} campaigns per rung per arm "
      f"(8 slot bases x 40), {sum(r['n'] for r in rows) * 2} campaigns total.\n")
print(f"{'rung':>5} {'LANE ON':>9} {'LANE OFF':>9} {'delta':>7} {'chunk-t(7)':>11} "
      f"{'disc b/c':>10} {'McNemar p':>10} {'MDE(pts)':>9}")
for r in rows:
    print(f"h{r['h']:<4} {r['on']:>8.1f}% {r['off']:>8.1f}% {r['on'] - r['off']:>+7.1f} "
          f"{r['t']:>11.2f} {str(r['b']) + '/' + str(r['c']):>10} {r['p']:>10.4f} "
          f"{r['mde']:>8.1f}{'*' if r['mdeBounded'] else ' '}")
print("  * MDE from the rule-of-three upper bound on the discordant rate (0 discordant observed).")
print()
for r in rows:
    print(f"h{r['h']}: binomial SE on/off {r['onse']:.2f}/{r['offse']:.2f}; "
          f"CLUSTER SE {r['onclu']:.2f}/{r['offclu']:.2f}; "
          f"chunk delta {r['dmean']:+.2f} +- {r['dse']:.2f}; discordant {r['b'] + r['c']}/{r['pairs']} pairs")
print("\nENEMY LANE TELEMETRY (summed over the rung's 8 chunks)")
print(f"{'rung':>5} {'arm':>4} {'acts':>7} {'ow branch':>10} {'brace':>7} {'lanes':>7} "
      f"{'reactions':>10} {'react/lane':>11}")
for r in rows:
    for a in ("on", "off"):
        t_ = r['tel'][a]
        print(f"h{r['h']:<4} {a:>4} {t_['acts']:>7} {t_['ow']:>10} {t_['brace']:>7} "
              f"{t_['lanes']:>7} {t_['react']:>10} "
              f"{(100.0 * t_['react'] / t_['lanes'] if t_['lanes'] else 0):>10.1f}%")
