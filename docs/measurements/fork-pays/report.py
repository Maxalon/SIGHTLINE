#!/usr/bin/env python3
"""THE FORK PAYS — the measured round's analysis.

Reads the 16 chunk JSONs (2 arms x 2 rungs x 4 CRN slot bases x 40 campaigns) and prints
(1) the win-rate table with BOTH a binomial and a chunk-paired SE, (2) the CRN-paired
per-campaign contrast (the arms replay identical worlds, so this is the powered test),
(3) intel earned/spent per run, and (4) byNodeKind pooled per arm per rung.
"""
import json, math, os, sys
D = os.path.dirname(os.path.abspath(__file__))
ARMS = sys.argv[1:] if len(sys.argv) > 1 else ["base", "new"]
RUNGS = [0, 4]; BASES = [0, 10, 20, 30]

def load(arm, h, b):
    with open(f"{D}/{arm}-h{h}-b{b}.json") as f: return json.load(f)

def mean(v): return sum(v) / len(v) if v else 0.0
def sd(v):
    if len(v) < 2: return 0.0
    m = mean(v); return math.sqrt(sum((x - m) ** 2 for x in v) / (len(v) - 1))

print("=" * 78)
print("THE FORK PAYS — CRN-paired round.  2 arms x {} rungs x {} slot bases x 40 campaigns"
      .format(len(RUNGS), len(BASES)))
print("=" * 78)

for h in RUNGS:
    print(f"\n--- heat {h} " + "-" * 62)
    rows = {}
    for arm in ARMS:
        wins = 0; n = 0; chunk = []; intel = []; spent = []; miss = []
        for b in BASES:
            d = load(arm, h, b)
            cw = sum(1 for c in d["campaigns"] if c["win"]); cn = len(d["campaigns"])
            wins += cw; n += cn; chunk.append(100.0 * cw / cn)
            ib = d["intelByHeat"][0]
            intel.append(ib["earned"]); spent.append(ib["spent"])
            miss.append(d["avgMissionsCleared"])
        wr = 100.0 * wins / n
        binse = math.sqrt(wr * (100 - wr) / n)
        clse = sd(chunk) / math.sqrt(len(chunk))
        rows[arm] = dict(wr=wr, n=n, binse=binse, clse=clse, chunk=chunk,
                         intel=mean(intel), spent=mean(spent), miss=mean(miss))
        print(f"  {arm:5s} win% {wr:5.1f}  n={n}  binomSE {binse:4.2f}  clusterSE {clse:4.2f}"
              f"  chunks {['%.0f' % c for c in chunk]}")
        print(f"        intel earned/run {mean(intel):6.1f}  spent/run {mean(spent):6.1f}"
              f"  avgMissionsCleared {mean(miss):.2f}  earned/MISSION {mean(intel)/mean(miss):5.2f}")

    # chunk-paired delta (the 4 slot sets are the clusters)
    dif = [rows["new"]["chunk"][i] - rows["base"]["chunk"][i] for i in range(len(BASES))]
    se = sd(dif) / math.sqrt(len(dif))
    t = dif and (mean(dif) / se if se > 0 else float("nan"))
    print(f"  DELTA (new - base), chunk-paired: {mean(dif):+.1f} +/- {se:.2f}  t({len(dif)-1})={t:+.2f}"
          f"   per-chunk {['%+.0f' % d for d in dif]}")

    # CRN-paired per campaign: the two arms replay the same (slot, policy) worlds
    pb, pn = {}, {}
    for b in BASES:
        for c in load("base", h, b)["campaigns"]: pb[(b, c["slot"], c["policy"])] = c
        for c in load("new",  h, b)["campaigns"]: pn[(b, c["slot"], c["policy"])] = c
    keys = sorted(set(pb) & set(pn))
    bw = sum(1 for k in keys if pb[k]["win"]); nw = sum(1 for k in keys if pn[k]["win"])
    a = sum(1 for k in keys if pn[k]["win"] and not pb[k]["win"])     # new wins, base lost
    c_ = sum(1 for k in keys if pb[k]["win"] and not pn[k]["win"])    # base wins, new lost
    same = sum(1 for k in keys if pb[k]["win"] == pn[k]["win"])
    z = (a - c_) / math.sqrt(a + c_) if (a + c_) else float("nan")
    print(f"  CRN pairs {len(keys)}: identical outcome on {same} ({100.0*same/len(keys):.1f}%);"
          f" discordant {a+c_}  (new-only {a} / base-only {c_})  McNemar z={z:+.2f}")
    # how many worlds differ AT ALL (missions cleared), the sensitivity check
    difMiss = sum(1 for k in keys if pb[k]["missionsCleared"] != pn[k]["missionsCleared"])
    print(f"  worlds whose missionsCleared differs: {difMiss}/{len(keys)} ({100.0*difMiss/len(keys):.1f}%)"
          " — a 0 here would mean the lever never reached gameplay")

    # byNodeKind pooled
    print("  byNodeKind (pooled over the 4 slot sets, mission win% at the node):")
    for arm in ARMS:
        agg = {}
        for b in BASES:
            for r in load(arm, h, b)["byNodeKind"]:
                k = r["nodeKind"]; e = agg.setdefault(k, [0, 0.0])
                e[0] += r["n"]; e[1] += r["n"] * r["winRate"] / 100.0
        line = "   ".join(f"{k} {100.0*v[1]/v[0]:.1f}% (n={v[0]})" for k, v in sorted(agg.items()))
        print(f"    {arm:5s} {line}")
