#!/usr/bin/env python3
"""C2 paired analysis. The two arms play the SAME slot seeds under the same binary, so the
honest test is McNemar on the discordant pairs, not two independent standard errors.

Per-slot outcomes are parsed out of each chunk's own report text ("slot N @hH: W 6 | L 4"),
which carries greedy and sloppy separately — 2 pairs per slot, 80 pairs per rung.

  python3 paired.py [rung ...]      # default: all six
"""
import os, re, sys, math

D = os.path.dirname(os.path.abspath(__file__))
RUNGS = ["R", "0", "2", "4", "6", "8"]
BASES = [0, 10, 20, 30]
ROW = re.compile(r"slot\s+(\d+)\s*@h(-?\d+):\s*([WL])\s+(\d+)\s*\|\s*([WL])\s+(\d+)")

def outcomes(arm, rung):
    """(base, slot, policy) -> (win, missionsCleared)"""
    out = {}
    for b in BASES:
        p = os.path.join(D, f"L-{arm}-h{rung}-b{b}.report.txt")
        for m in ROW.finditer(open(p).read()):
            slot = int(m.group(1))
            out[(b, slot, "greedy")] = (m.group(3) == "W", int(m.group(4)))
            out[(b, slot, "sloppy")] = (m.group(5) == "W", int(m.group(6)))
    return out

def mcnemar(b, c):
    """two-sided exact-ish: normal approx with continuity correction (n small -> report raw)."""
    n = b + c
    if n == 0: return 1.0
    chi = (abs(b - c) - 1) ** 2 / n
    # survival of chi2 with 1 df
    return math.erfc(math.sqrt(chi / 2.0))

print("C2 PAIRED (McNemar over identical slot seeds; 4 slot sets x 10 slots x 2 policies = 80 pairs/rung)")
print(f"{'rung':<8}{'pairs':>7}{'both W':>8}{'both L':>8}{'BASEonly':>10}{'DECLonly':>10}"
      f"{'BASE%':>8}{'DECL%':>8}{'delta':>8}{'p':>8}{'dMiss':>8}")
for rung in (sys.argv[1:] or RUNGS):
    A, B = outcomes("BASE", rung), outcomes("DECL", rung)
    keys = sorted(set(A) & set(B))
    bb = cc = ww = ll = 0
    dmiss = 0.0
    for k in keys:
        aw, am = A[k]; bw, bm = B[k]
        dmiss += bm - am
        if aw and bw: ww += 1
        elif not aw and not bw: ll += 1
        elif aw: bb += 1
        else: cc += 1
    n = len(keys)
    ap = 100.0 * (ww + bb) / n
    bp = 100.0 * (ww + cc) / n
    print(f"{rung:<8}{n:>7}{ww:>8}{ll:>8}{bb:>10}{cc:>10}{ap:>8.1f}{bp:>8.1f}"
          f"{bp-ap:>+8.1f}{mcnemar(bb, cc):>8.3f}{dmiss/n:>+8.2f}")
