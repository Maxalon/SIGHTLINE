#!/usr/bin/env python3
"""L7 — THE L6 CONSISTENCY CHECK, which is also P21's inertness check.

L7 runs L6's protocol exactly (same runner of record, same 16 slot bases, same N=10, same pin)
on a tree ONE MILESTONE LATER: L6's base is `6a6ebee` (milestone 12) and L7's is `935d719`
(milestone 14). Exactly one wave separates them — P21 BUILD OWNS THE BOARD — and P21 claims to
be LIVE-PATH INERT: `Mission.ClearHazardsOnBuild` clears arrays `Game.SetupMission` has always
cleared 28 lines earlier, and `Run.SupplyDiscount`/`ElitePremium`/`PitchedPremium`/`DepthBase`
went from `const int` to `static int` holding the SAME shipped values.

So L7's six shared rungs must reproduce L6's chunk for chunk. This tool checks it two ways:

  IDENTITY   every (slot, policy) outcome of every shared chunk, against the L6 archive.
             This is the strong form: 1,920 CRN campaigns, and P21 is inert only if it is 96/96.
  McNEMAR    the same comparison as a contrast, so that a FAILURE gets a size and a
             direction rather than just a "differs" line.

A failure here outranks everything else in the wave and the round stops.

Usage: l6check.py [--l7dir DIR] [--l6dir DIR]
"""
import glob, json, math, os, statistics, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SHARED = ["R", "0", "2", "4", "6", "8"]


def base_of(f):
    return int(os.path.basename(f).rsplit("-b", 1)[1].split(".")[0])


def outcomes(path):
    j = json.load(open(path))
    d = {}
    for s in j["pairedPolicy"]["slots"]:
        d[(s["slot"], "g")] = bool(s["greedyWin"])
        d[(s["slot"], "s")] = bool(s["sloppyWin"])
    return d


def main():
    l7d, l6d = HERE, os.path.join(HERE, "..", "l6")
    a = sys.argv[1:]
    i = 0
    while i < len(a):
        if a[i] == "--l7dir": l7d = a[i + 1]; i += 2
        elif a[i] == "--l6dir": l6d = a[i + 1]; i += 2
        else: i += 1

    print("===== L7 vs L6 — the six shared rungs, chunk for chunk (P21's inertness check) =====")
    print(f"  {'rung':<6}{'chunks':>7}{'same':>6}{'legs':>6}{'sameLegs':>9}"
          f"{'L6%':>7}{'L7%':>7}{'delta':>7}{'n_disc':>7}{'MDE80':>7}{'z':>7}  verdict")
    TC = TS = TL = TSL = 0
    for h in SHARED:
        chunks = same = legs = samelegs = 0
        n = w6 = w7 = b = c = 0
        per_chunk = []
        for f7 in sorted(glob.glob(os.path.join(l7d, f"L7-h{h}-b*.json")), key=base_of):
            f6 = os.path.join(l6d, f"L6-h{h}-b{base_of(f7)}.json")
            if not os.path.exists(f6):
                continue
            A, B = outcomes(f6), outcomes(f7)
            ks = sorted(set(A) & set(B))
            chunks += 1
            eq = all(A[k] == B[k] for k in ks)
            same += eq
            legs += len(ks)
            samelegs += sum(1 for k in ks if A[k] == B[k])
            n += len(ks); w6 += sum(A[k] for k in ks); w7 += sum(B[k] for k in ks)
            b += sum(1 for k in ks if A[k] and not B[k])
            c += sum(1 for k in ks if B[k] and not A[k])
            per_chunk.append(100.0 * (sum(B[k] for k in ks) - sum(A[k] for k in ks)) / len(ks))
            if not eq:
                print(f"    h{h} b{base_of(f7)}: DIFFERS ({sum(1 for k in ks if A[k] != B[k])} legs)")
        if not chunks:
            continue
        nd = b + c
        pse = 100.0 * math.sqrt(nd) / n if nd else 0.0
        mde = 2.80 * pse if nd else float("nan")
        z = (c - b) / math.sqrt(nd) if nd else float("nan")
        delta = 100.0 * (w7 - w6) / n
        v = "IDENTICAL" if nd == 0 else (f"NOT RESOLVED (|{delta:+.1f}| < MDE {mde:.1f})" if abs(delta) < mde else "RESOLVED — A REAL DIFFERENCE")
        print(f"  h{h:<5}{chunks:>7}{same:>6}{legs:>6}{samelegs:>9}"
              f"{100.0*w6/n:>7.1f}{100.0*w7/n:>7.1f}{delta:>7.1f}{nd:>7}"
              f"{mde if nd else 0:>7.2f}{z if nd else 0:>7.2f}  {v}")
        TC += chunks; TS += same; TL += legs; TSL += samelegs
    print(f"  TOTAL: chunks identical on every (slot, policy) outcome {TS}/{TC}; legs identical {TSL}/{TL}")
    if TS == TC and TL:
        print("  VERDICT: L7 REPRODUCES L6 EXACTLY on all six shared rungs. P21 BUILD OWNS THE BOARD is")
        print("           campaign-inert as it claimed, and the CRN chain is intact across milestone 14.")
        return 0
    print("  VERDICT: L7 DOES NOT REPRODUCE L6. This outranks every other result in the wave.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
