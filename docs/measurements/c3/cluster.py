#!/usr/bin/env python3
"""C3 — the CLUSTER (slot-set) standard error per rung, and the h2 jackknife.

Review finding F5. A rung is not 160 exchangeable campaigns; it is **8 clusters of 20**, each
cluster a disjoint CRN slot set, and the clusters disagree. The binomial SE this project quotes
(±3.5–3.9) assumes exchangeability and is therefore too small wherever the slot sets are
heterogeneous — which is five of six rungs on this tree, and it is a property of the SLOT SETS,
not of any lever (it holds in both arms and in the L3 archive those slot sets came from).

Prints, per rung: the eight slot-set win rates, the cluster SE (SD of the eight means / sqrt(8)),
the binomial SE, their ratio, and the leave-one-slot-set-out jackknife range — which is the honest
answer to "is this rung comfortably inside its band or just over the line".

Usage: cluster.py <tag> [more tags...]
"""
import json, glob, os, sys, math, statistics

DIR = os.path.dirname(os.path.abspath(__file__))
RUNGS = ["hR", "h0", "h2", "h4", "h6", "h8"]
BASES = [0, 10, 20, 30, 40, 50, 60, 70]
# the goal band floors of record (CLAUDE.md): RECRUIT 75±8, h0 55±8, h2 40±8, h4 30±8, h6 20±8, h8 10±5
FLOOR = {"hR": 67, "h0": 47, "h2": 32, "h4": 22, "h6": 12, "h8": 5}


def main(tag):
    print(f"===== {tag} — per-rung slot-set heterogeneity =====")
    print(f"  {'rung':<5}{'mean':>7}{'clusterSE':>11}{'binomSE':>9}{'ratio':>7}"
          f"{'floor':>7}{'jackknife min':>15}   slot sets")
    for r in RUNGS:
        vals = []
        n = 0
        for b in BASES:
            f = os.path.join(DIR, f"{tag}-{r}-b{b}.json")
            if not os.path.exists(f):
                break
            d = json.load(open(f))
            vals.append(d["runWinRate"])
            n += d["runs"]
        if len(vals) < 2:
            continue
        mean = statistics.mean(vals)
        cse = statistics.stdev(vals) / math.sqrt(len(vals))
        p = mean / 100.0
        bse = 100 * math.sqrt(p * (1 - p) / n)
        jack = [statistics.mean([v for j, v in enumerate(vals) if j != i]) for i in range(len(vals))]
        print(f"  {r:<5}{mean:>7.2f}{cse:>11.2f}{bse:>9.2f}{cse/bse:>7.2f}"
              f"{FLOOR[r]:>7}{min(jack):>15.2f}   {vals}")
    print()


if __name__ == "__main__":
    for t in sys.argv[1:] or ["B1", "L1"]:
        main(t)
