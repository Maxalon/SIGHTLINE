#!/usr/bin/env python3
"""P23 THE APEX BITES — the four-arm analysis.

Every contrast is CRN-PAIRED between two ARMS at the SAME rung and the SAME 16 slot bases, so a
campaign in one arm is the same world as the campaign beside it. Reports, per rung:

  * each arm's pooled win% with a binomial SE and a CLUSTER SE over the 16 chunks (the cluster SE
    is the one to read — L4/C3 both recorded the binomial one as too small by 11-40%),
  * every contrast against BASE, and A+B against A and against B, with n_discordant, the McNemar
    pair SE, the MDE at 80% power (2.80 x pairSE, the L7 convention), z, and the chunk-paired t,
  * an explicit RESOLVED / NOT RESOLVED verdict per contrast: |effect| < MDE means the round could
    not have detected an effect that size, which is a result, not a failure.

Usage: arms.py [--dir DIR] [--rungs 0,4,6,8] [--arms base,A,B,AB]
"""
import glob, json, math, os, statistics, sys

HERE = os.path.dirname(os.path.abspath(__file__))
BASES = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150]
NEWBASES = [160, 170, 180, 190, 200, 210, 220, 230, 240, 250, 260, 270, 280, 290, 300, 310]


def outcomes(path):
    j = json.load(open(path))
    return {(s["slot"], p[0]): bool(s[p + "Win"]) for s in j["pairedPolicy"]["slots"]
            for p in ("greedy", "sloppy")}


def load(d, arms, rungs, bases=BASES):
    out = {}
    for a in arms:
        for h in rungs:
            per = {}
            for b in bases:
                f = os.path.join(d, f"P23-{a}-h{h}-b{b}.json")
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
    mde = 2.80 * se if nd else float("nan")
    z = (bb - cc) / math.sqrt(nd) if nd else float("nan")
    k = len(per_chunk)
    cm = statistics.mean(per_chunk) if k else float("nan")
    cse = statistics.stdev(per_chunk) / math.sqrt(k) if k > 1 else float("nan")
    t = cm / cse if cse and cse == cse and cse > 0 else float("nan")
    return dict(eff=eff, b=bb, c=cc, nd=nd, se=se, mde=mde, z=z, n=npair, k=k, cm=cm, cse=cse, t=t)


def main():
    d = HERE
    rungs = ["0", "4", "6", "8"]
    arms = ["base", "A", "B", "AB"]
    args = sys.argv[1:]
    for i, a in enumerate(args):
        if a == "--dir": d = args[i + 1]
        if a == "--rungs": rungs = args[i + 1].split(",")
        if a == "--arms": arms = args[i + 1].split(",")
        if a == "--bases": which = args[i + 1]
    which = locals().get("which", "old")
    bases = {"old": BASES, "new": NEWBASES, "both": BASES + NEWBASES}[which]
    print(f"(slot bases: {which} — {len(bases)} sets)")
    data = load(d, arms, rungs, bases)

    print("===== ARMS =====")
    print(f"  {'rung':<6}{'arm':<6}{'k':>3}{'n':>6}{'win%':>7}{'binSE':>7}{'clSE':>7}")
    for h in rungs:
        for a in arms:
            if (a, h) not in data: continue
            w, b, c, n, k = pooled(data[(a, h)])
            print(f"  h{h:<5}{a:<6}{k:>3}{n:>6}{w:>7.1f}{b:>7.2f}{c:>7.2f}")

    pairs = [("base", "A"), ("base", "B"), ("base", "AB"), ("A", "AB"), ("B", "AB")]
    print("\n===== CONTRASTS (CRN-paired, effect = second arm minus first) =====")
    print(f"  {'rung':<6}{'contrast':<12}{'eff':>7}{'b':>5}{'c':>5}{'n_disc':>7}{'pairSE':>7}"
          f"{'MDE80':>7}{'z':>7}{'chunkD':>8}{'clSE':>6}{'t':>7}  verdict")
    for h in rungs:
        for x, y in pairs:
            if (x, h) not in data or (y, h) not in data: continue
            r = contrast(data[(x, h)], data[(y, h)])
            if r["nd"] == 0:
                # Not "no effect measured" — no campaign in the arm came out differently AT ALL.
                v = "EXACTLY INERT (0 discordant in %d)" % r["n"]
            elif abs(r["eff"]) >= r["mde"]:
                v = "RESOLVED at 80%% power (z=%.2f)" % r["z"]
            else:
                v = "NOT RESOLVED (|%.1f| < MDE %.1f)" % (r["eff"], r["mde"])
            print(f"  h{h:<5}{x+'->'+y:<12}{r['eff']:>7.1f}{r['b']:>5}{r['c']:>5}{r['nd']:>7}"
                  f"{r['se']:>7.2f}{r['mde']:>7.2f}{r['z']:>7.2f}{r['cm']:>8.1f}{r['cse']:>6.2f}"
                  f"{r['t']:>7.2f}  {v}")

    # ---- POOLED OVER THE RUNGS WHERE THE LEVERS ACT ---------------------------------------
    # Pre-specified, not fished: h4/h6/h8 are the rungs FORCETEST's matrix says either lever can
    # fire on. h0/h2 are the inertness controls and are deliberately NOT pooled in.
    act = [h for h in rungs if h in ("4", "6", "8")]
    if len(act) > 1:
        print(f"\n===== POOLED over the acting rungs {','.join('h'+h for h in act)} =====")
        print(f"  {'contrast':<12}{'eff':>7}{'b':>5}{'c':>5}{'n_disc':>7}{'pairSE':>7}{'MDE80':>7}{'z':>7}")
        for x, y in pairs:
            bb = cc = np = 0
            for h in act:
                if (x, h) not in data or (y, h) not in data: continue
                r = contrast(data[(x, h)], data[(y, h)])
                bb += r["b"]; cc += r["c"]; np += r["n"]
            if not np: continue
            nd = bb + cc
            eff = 100.0 * (bb - cc) / np
            se = 100.0 * math.sqrt(nd) / np if nd else float("nan")
            z = (bb - cc) / math.sqrt(nd) if nd else float("nan")
            print(f"  {x+'->'+y:<12}{eff:>7.2f}{bb:>5}{cc:>5}{nd:>7}{se:>7.2f}{2.80*se:>7.2f}{z:>7.2f}")

    # ---- ADDITIVITY: does A+B equal A alone plus B alone? --------------------------------------
    print("\n===== ADDITIVITY (interaction = eff(base->AB) - eff(base->A) - eff(base->B)) =====")
    print(f"  {'rung':<6}{'A':>7}{'B':>7}{'A+B':>7}{'sum':>7}{'inter':>8}")
    for h in rungs:
        if any((a, h) not in data for a in ("base", "A", "B", "AB")): continue
        ea = contrast(data[("base", h)], data[("A", h)])["eff"]
        eb = contrast(data[("base", h)], data[("B", h)])["eff"]
        eab = contrast(data[("base", h)], data[("AB", h)])["eff"]
        print(f"  h{h:<5}{ea:>7.1f}{eb:>7.1f}{eab:>7.1f}{ea+eb:>7.1f}{eab-ea-eb:>8.1f}")


if __name__ == "__main__":
    main()
