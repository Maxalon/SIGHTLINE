#!/usr/bin/env python3
"""P23 — THE FREE CONSISTENCY CHECK, and it is worth more than any single contrast.

P23's BASE arm runs the shipped binary with both dials off, which is the pre-P23 arithmetic. Its
base commit (`fd07d56`) is one milestone past L7's (`935d719`) and exactly one wave separates them
(P22 NOTHING WITHOUT A SWITCH, which claims to be default-inert). Same runner, same 16 slot bases,
same N, same pin — so every rung P23's base arm shares with the L7 archive is a direct
reproduction test of THREE things at once:

  * that P22 is inert as it claimed,
  * that P23's own non-lever edits (the `Floor` witness, the `LastForce*` telemetry, the new
    `heatStat` parameter) really are stream-neutral with the dials off,
  * that the CRN chain still reaches back through milestone 15 and 16.

A single differing campaign here invalidates every contrast in the round, so it is run first.

Usage: bridge.py [--dir DIR] [--ref ../l7] [--rungs 0,2,4,6,8]
"""
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
BASES = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150]


def outcomes(p):
    j = json.load(open(p))
    return {(s["slot"], k): bool(s[k + "Win"]) for s in j["pairedPolicy"]["slots"]
            for k in ("greedy", "sloppy")}


def main():
    d, ref, rungs = HERE, os.path.join(HERE, "..", "l7"), ["0", "2", "4", "6", "8"]
    a = sys.argv[1:]
    for i, x in enumerate(a):
        if x == "--dir": d = a[i + 1]
        if x == "--ref": ref = a[i + 1]
        if x == "--rungs": rungs = a[i + 1].split(",")
    print(f"  {'rung':<6}{'chunks':>8}{'ident':>7}{'legs':>7}{'ident':>7}{'P23%':>7}{'ref%':>7}  verdict")
    tot_c = tot_ci = tot_l = tot_li = 0
    for h in rungs:
        nc = nci = nl = nli = w1 = w2 = 0
        for b in BASES:
            f1 = os.path.join(d, f"P23-base-h{h}-b{b}.json")
            f2 = os.path.join(ref, f"L7-h{h}-b{b}.json")
            if not (os.path.exists(f1) and os.path.exists(f2)):
                continue
            o1, o2 = outcomes(f1), outcomes(f2)
            ks = sorted(set(o1) & set(o2))
            nc += 1; nl += len(ks)
            same = sum(1 for k in ks if o1[k] == o2[k])
            nli += same
            if same == len(ks) and len(o1) == len(o2): nci += 1
            w1 += sum(o1[k] for k in ks); w2 += sum(o2[k] for k in ks)
        if not nc: continue
        tot_c += nc; tot_ci += nci; tot_l += nl; tot_li += nli
        v = "IDENTICAL" if nli == nl else f"*** {nl - nli} LEGS DIFFER ***"
        print(f"  h{h:<5}{nc:>8}{nci:>7}{nl:>7}{nli:>7}{100.0*w1/nl:>7.1f}{100.0*w2/nl:>7.1f}  {v}")
    print(f"  {'TOTAL':<6}{tot_c:>8}{tot_ci:>7}{tot_l:>7}{tot_li:>7}"
          + ("        BRIDGE INTACT" if tot_li == tot_l else "   *** BRIDGE BROKEN ***"))


if __name__ == "__main__":
    main()
