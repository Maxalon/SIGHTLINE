#!/usr/bin/env python3
"""P24 — THE BRIDGE, RUN FIRST.

P24's BASE arm is the shipped binary with `SIGHTLINE_AIMTRIM=0`, i.e. this wave's restore flag.
P23's AB arm is the shipped tree one milestone back. Same runner, same slot bases, same N, same
pin — so every rung they share is a direct reproduction test of THREE things at once:

  * that the restore flag is a TRUE restoration and not merely a named one (the house rule two
    waves have been burned by: a lever that cannot be switched off cannot be attributed),
  * that P24's non-lever edits — FORCETEST leg (H), the `levers` block in the balance JSON — are
    stream-neutral,
  * that the CRN chain still reaches back through milestone 17 to L6/L7, the ladder of record.

A single differing campaign here invalidates every contrast in the round, so it is run first.

Usage: bridge.py [--dir DIR] [--ref ../p23] [--rungs -1,0,2,4,6,8] [--bases old|new|both]
"""
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
BASES = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150]
NEWBASES = [160, 170, 180, 190, 200, 210, 220, 230, 240, 250, 260, 270, 280, 290, 300, 310]
NAME = {"-1": "RECRUIT", "0": "h0", "2": "h2", "4": "h4", "6": "h6", "8": "h8"}


def outcomes(p):
    j = json.load(open(p))
    return {(s["slot"], k): bool(s[k + "Win"]) for s in j["pairedPolicy"]["slots"]
            for k in ("greedy", "sloppy")}


def main():
    d, ref, rungs, which = HERE, os.path.join(HERE, "..", "p23"), ["-1", "0", "2", "4", "6", "8"], "old"
    a = sys.argv[1:]
    for i, x in enumerate(a):
        if x == "--dir": d = a[i + 1]
        if x == "--ref": ref = a[i + 1]
        if x == "--rungs": rungs = a[i + 1].split(",")
        if x == "--bases": which = a[i + 1]
    bases = {"old": BASES, "new": NEWBASES, "both": BASES + NEWBASES}[which]
    print(f"  P24 base arm (SIGHTLINE_AIMTRIM=0)  vs  {os.path.normpath(ref)} AB arm — {which} bases")
    print(f"  {'rung':<9}{'chunks':>8}{'ident':>7}{'legs':>7}{'ident':>7}{'P24%':>7}{'ref%':>7}  verdict")
    tc = tci = tl = tli = 0
    for h in rungs:
        nc = nci = nl = nli = w1 = w2 = 0
        for b in bases:
            f1 = os.path.join(d, f"P24-base-h{h}-b{b}.json")
            f2 = os.path.join(ref, f"P23-AB-h{h}-b{b}.json")
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
        tc += nc; tci += nci; tl += nl; tli += nli
        v = "IDENTICAL" if nli == nl else f"*** {nl - nli} LEGS DIFFER ***"
        print(f"  {NAME.get(h, h):<9}{nc:>8}{nci:>7}{nl:>7}{nli:>7}"
              f"{100.0*w1/nl:>7.1f}{100.0*w2/nl:>7.1f}  {v}")
    print(f"  {'TOTAL':<9}{tc:>8}{tci:>7}{tl:>7}{tli:>7}"
          + ("            BRIDGE INTACT" if tli == tl and tl else "   *** BRIDGE BROKEN ***"))
    return 0 if tl and tli == tl else 1


if __name__ == "__main__":
    sys.exit(main())
