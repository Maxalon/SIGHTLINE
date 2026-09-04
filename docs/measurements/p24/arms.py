#!/usr/bin/env python3
"""P24 THE TOP OF THE LADDER — the two-arm analysis.

ONE LEVER: `Mission.HostileAimTrim` 0 -> 5. Both arms come off one binary snapshot and share the
same CRN slot bases, so a campaign in one arm is the same world as the campaign beside it.

Per rung it reports each arm's pooled win% with a binomial AND a cluster SE (the cluster one is
the one to read — L4/C3/L5 all recorded the binomial as too small), then the CRN-paired contrast
with n_discordant, the McNemar pair SE, the MDE at 80% power (2.80 x pairSE, the L7 convention),
z, and the chunk-paired t. `|effect| < MDE` prints NOT RESOLVED, which is a result and not a
failure. Then it grades both arms against the FUL-13 band and prints the ladder's STEPS, because
this wave may not flatten the steps L7 and P23 spent two rounds restoring.

Usage: arms.py [--dir DIR] [--rungs -1,0,2,4,6,8] [--bases old|new|both]
"""
import json, math, os, statistics, sys

HERE = os.path.dirname(os.path.abspath(__file__))
BASES = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150]
NEWBASES = [160, 170, 180, 190, 200, 210, 220, 230, 240, 250, 260, 270, 280, 290, 300, 310]
ARMS = ["base", "aim"]

# FUL-13 goal band: centre +- width. h8 is +-5 with a HARD floor of 5.
BAND = {"-1": (75, 8), "0": (55, 8), "2": (40, 8), "4": (30, 8), "6": (20, 8), "8": (10, 5)}
NAME = {"-1": "RECRUIT", "0": "h0", "2": "h2", "4": "h4", "6": "h6", "8": "h8"}


def outcomes(path):
    j = json.load(open(path))
    return {(s["slot"], p[0]): bool(s[p + "Win"]) for s in j["pairedPolicy"]["slots"]
            for p in ("greedy", "sloppy")}


def load(d, arms, rungs, bases):
    out = {}
    for a in arms:
        for h in rungs:
            per = {}
            for b in bases:
                f = os.path.join(d, f"P24-{a}-h{h}-b{b}.json")
                if os.path.exists(f):
                    per[b] = outcomes(f)
            if per:
                out[(a, h)] = per
    return out


def pooled(per):
    n = sum(len(v) for v in per.values()); w = sum(sum(v.values()) for v in per.values())
    vals = [100.0 * sum(v.values()) / len(v) for v in per.values()]
    p = w / n
    return (100.0 * p, 100 * math.sqrt(p * (1 - p) / n),
            statistics.stdev(vals) / math.sqrt(len(vals)) if len(vals) > 1 else float("nan"), n, len(vals))


def contrast(pa, pb):
    """pb - pa, paired. b = win in B only, c = win in A only."""
    npair = bb = cc = 0
    per_chunk = []
    for base in sorted(set(pa) & set(pb)):
        A, B = pa[base], pb[base]
        ks = sorted(set(A) & set(B))
        if not ks:
            continue
        per_chunk.append(100.0 * (sum(B[k] for k in ks) - sum(A[k] for k in ks)) / len(ks))
        npair += len(ks)
        bb += sum(1 for k in ks if B[k] and not A[k])
        cc += sum(1 for k in ks if A[k] and not B[k])
    nd = bb + cc
    eff = 100.0 * (bb - cc) / npair if npair else float("nan")
    se = 100.0 * math.sqrt(nd) / npair if nd else float("nan")
    k = len(per_chunk)
    cm = statistics.mean(per_chunk) if k else float("nan")
    cse = statistics.stdev(per_chunk) / math.sqrt(k) if k > 1 else float("nan")
    return dict(eff=eff, b=bb, c=cc, nd=nd, se=se, mde=2.80 * se if nd else float("nan"),
                z=(bb - cc) / math.sqrt(nd) if nd else float("nan"), n=npair, k=k, cm=cm, cse=cse,
                t=cm / cse if cse and cse == cse and cse > 0 else float("nan"))


def verdict(h, w, cse):
    c, wd = BAND[h]
    lo, hi = c - wd, c + wd
    if h == "8":
        lo = max(lo, 5)
    if w < lo:
        return f"OUT {w - lo:+.1f} ({(lo - w) / cse:.2f} clSE under)" if cse == cse else f"OUT {w - lo:+.1f}"
    if w > hi:
        return f"OUT {w - hi:+.1f} over"
    return f"IN  ({w - lo:+.1f} over floor, {w - c:+.1f} vs centre)"


def main():
    d, rungs, which = HERE, ["-1", "0", "2", "4", "6", "8"], "old"
    a = sys.argv[1:]
    for i, x in enumerate(a):
        if x == "--dir": d = a[i + 1]
        if x == "--rungs": rungs = a[i + 1].split(",")
        if x == "--bases": which = a[i + 1]
    bases = {"old": BASES, "new": NEWBASES, "both": BASES + NEWBASES}[which]
    data = load(d, ARMS, rungs, bases)
    print(f"(slot bases: {which} — {len(bases)} sets requested)")

    print("\n===== ARMS, AND THE BAND =====")
    print(f"  {'rung':<9}{'arm':<6}{'k':>3}{'n':>6}{'win%':>7}{'binSE':>7}{'clSE':>7}   band verdict")
    for h in rungs:
        for arm in ARMS:
            if (arm, h) not in data: continue
            w, b, c, n, k = pooled(data[(arm, h)])
            lo, hi = BAND[h][0] - BAND[h][1], BAND[h][0] + BAND[h][1]
            if h == "8": lo = max(lo, 5)
            print(f"  {NAME[h]:<9}{arm:<6}{k:>3}{n:>6}{w:>7.1f}{b:>7.2f}{c:>7.2f}   "
                  f"[{lo}-{hi}] {verdict(h, w, c)}")

    print("\n===== THE CONTRAST (CRN-paired, effect = aim arm minus base arm) =====")
    print(f"  {'rung':<9}{'eff':>7}{'b':>5}{'c':>5}{'n_disc':>7}{'pairSE':>7}{'MDE80':>7}{'z':>7}"
          f"{'chunkD':>8}{'clSE':>6}{'t':>7}  verdict")
    for h in rungs:
        if ("base", h) not in data or ("aim", h) not in data: continue
        r = contrast(data[("base", h)], data[("aim", h)])
        if r["nd"] == 0:
            v = "EXACTLY INERT (0 discordant in %d)" % r["n"]
        elif abs(r["eff"]) >= r["mde"]:
            v = "RESOLVED at 80%% power (z=%.2f)" % r["z"]
        else:
            v = "NOT RESOLVED (|%.1f| < MDE %.1f)" % (r["eff"], r["mde"])
        print(f"  {NAME[h]:<9}{r['eff']:>7.1f}{r['b']:>5}{r['c']:>5}{r['nd']:>7}{r['se']:>7.2f}"
              f"{r['mde']:>7.2f}{r['z']:>7.2f}{r['cm']:>8.1f}{r['cse']:>6.2f}{r['t']:>7.2f}  {v}")

    # pooled over every rung measured — a level lever acts on all of them, so unlike P23 there is
    # no "acting rungs" subset to pre-specify and no inertness control to leave out.
    bb = cc = np_ = 0
    for h in rungs:
        if ("base", h) not in data or ("aim", h) not in data: continue
        r = contrast(data[("base", h)], data[("aim", h)])
        bb += r["b"]; cc += r["c"]; np_ += r["n"]
    if np_:
        nd = bb + cc
        se = 100.0 * math.sqrt(nd) / np_
        print(f"\n  POOLED over all measured rungs: eff {100.0*(bb-cc)/np_:+.2f}  b={bb} c={cc} "
              f"n_disc={nd}  pairSE {se:.2f}  MDE80 {2.80*se:.2f}  z {(bb-cc)/math.sqrt(nd):+.2f}  n={np_}")

    print("\n===== THE STEPS (this wave may not flatten what L7 and P23 restored) =====")
    order = [h for h in ["-1", "0", "2", "4", "6", "8"] if h in rungs]
    print(f"  {'step':<16}{'base':>8}{'aim':>8}{'band implies':>14}")
    for i in range(len(order) - 1):
        h0, h1 = order[i], order[i + 1]
        row = []
        for arm in ARMS:
            if (arm, h0) in data and (arm, h1) in data:
                row.append(pooled(data[(arm, h0)])[0] - pooled(data[(arm, h1)])[0])
            else:
                row.append(float("nan"))
        imp = BAND[h0][0] - BAND[h1][0]
        print(f"  {NAME[h0]+'->'+NAME[h1]:<16}{row[0]:>8.1f}{row[1]:>8.1f}{imp:>14}")


if __name__ == "__main__":
    main()
