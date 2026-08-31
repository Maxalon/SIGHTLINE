#!/usr/bin/env python3
"""C3 — the CRN-PAIRED comparison of two arms, campaign by campaign.

Independent per-rung standard errors (+-3.5 to 3.9 at n=160) throw away the whole point of common
random numbers: B1 and L1 played the SAME 960 worlds, so the right statistic is the paired one.
Each chunk's report prints a PER-SLOT RECORDS block —

    slot   0 @h0: W 6 | L 4   margin +2

— one line per slot, greedy outcome then sloppy. Keyed on (rung, base, slot, policy) that is a
campaign-level pairing, and the discordant counts below are a McNemar table.

Usage: paired.py <tagA> <tagB>
"""
import re, glob, os, sys, math

DIR = os.path.dirname(os.path.abspath(__file__))
LINE = re.compile(r"slot\s+(\d+)\s+@h(-?\d+):\s+([WL])\s+(\d+)\s+\|\s+([WL])\s+(\d+)")
RUNGS = ["hR", "h0", "h2", "h4", "h6", "h8"]


def load(tag):
    out = {}
    for f in sorted(glob.glob(os.path.join(DIR, tag + "-h*-b*.report.txt"))):
        name = os.path.basename(f)[len(tag) + 1:].replace(".report.txt", "")   # hX-bY
        rung, base = name.split("-")
        for m in LINE.finditer(open(f).read()):
            slot = m.group(1)
            out[(rung, base, slot, "g")] = (m.group(3) == "W", int(m.group(4)))
            out[(rung, base, slot, "s")] = (m.group(5) == "W", int(m.group(6)))
    return out


A, B = load(sys.argv[1]), load(sys.argv[2])
keys = sorted(set(A) & set(B))
if len(keys) != len(A) or len(keys) != len(B):
    print(f"!! key mismatch: A={len(A)} B={len(B)} shared={len(keys)}")

print(f"{'stratum / rung':<22}{'pairs':>7}{'A win%':>8}{'B win%':>8}{'delta':>8}"
      f"{'B-only':>8}{'A-only':>8}{'p(2-sided)':>12}{'dMissions':>11}")


def row(label, ks):
    if not ks:
        return
    aw = sum(1 for k in ks if A[k][0])
    bw = sum(1 for k in ks if B[k][0])
    bonly = sum(1 for k in ks if B[k][0] and not A[k][0])
    aonly = sum(1 for k in ks if A[k][0] and not B[k][0])
    dm = sum(B[k][1] - A[k][1] for k in ks) / len(ks)
    d = bonly + aonly
    # exact two-sided sign test on the discordant pairs (McNemar, small-n safe)
    if d == 0:
        p = 1.0
    else:
        lo = min(bonly, aonly)
        p = min(1.0, 2 * sum(math.comb(d, i) for i in range(lo + 1)) / 2 ** d)
    print(f"{label:<22}{len(ks):>7}{100.0*aw/len(ks):>8.2f}{100.0*bw/len(ks):>8.2f}"
          f"{100.0*(bw-aw)/len(ks):>8.2f}{bonly:>8}{aonly:>8}{p:>12.6f}{dm:>11.3f}")


for rung in RUNGS + ["ALL"]:
    row(rung, keys if rung == "ALL" else [k for k in keys if k[0] == rung])

# ── STRATIFIED ON THE OPENER (review finding F1) ───────────────────────────────────────────────
# Mission 1 is always an Eliminate, so it is the largest population a lever on that objective can
# touch, and a pooled ladder delta silently mixes "the campaign got easier" with "the opener
# stopped killing people". C3's first draft reported the pooled figure as its headline and booked
# the mission-1 change fifty lines away as an unintended cost; they are one transaction.
# Stratifying on whether the BASELINE arm survived mission 1 is legitimate under CRN — both arms
# play identical worlds and the lever is the only difference, so the stratum is defined on a
# pre-lever fact. `missionsCleared == 0` IS "lost mission 1".
print()
print("STRATIFIED on whether the BASELINE (arm A) survived mission 1:")
surv = [k for k in keys if A[k][1] >= 1]
lost = [k for k in keys if A[k][1] == 0]
row("A survived m1", surv)
row("A lost m1", lost)
print()
print("per rung, OPENER HELD FIXED (A survived m1):")
for rung in RUNGS:
    row("  " + rung, [k for k in surv if k[0] == rung])
print()
print("mission-1 losses per rung, and how many pairs differ between the arms AT ALL:")
for rung in RUNGS:
    ks = [k for k in keys if k[0] == rung]
    if ks:
        print(f"  {rung}: m1 losses A={sum(1 for k in ks if A[k][1]==0):>3}  B={sum(1 for k in ks if B[k][1]==0):>3}"
              f"   (n={len(ks)}, pairs differing: {sum(1 for k in ks if A[k]!=B[k])})")
