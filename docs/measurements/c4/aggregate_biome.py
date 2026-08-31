#!/usr/bin/env python3
"""C4 per-BIOME aggregator (instrumented round C4i-*). Pools the eight disjoint CRN slot sets
per (rung, arm) and splits the MISSION win rate by the room it was fought in — the cross-tab
CROSSCUT rule 1 requires before concluding anything from a pooled row.

Prints, per rung: pooled campaign win rate (must match the C4-* round exactly), then the
per-biome mission table for both arms with the A−B delta. The three mechanical biomes are
starred; the five paint biomes are the built-in control (their rows must move only by noise)."""
import json, math, os

D = os.path.dirname(os.path.abspath(__file__))
BASES = [0, 10, 20, 30, 40, 50, 60, 70]
MECH = {"VERDANT", "TUNDRA", "MAGMA"}


def pool(arm, h):
    runs = wins = 0
    bio = {}
    for b in BASES:
        d = json.load(open(os.path.join(D, f"C4i-{arm}-h{h}-b{b}.json")))
        n = d["runs"]
        runs += n
        wins += round(d["runWinRate"] * n / 100.0)
        for row in d.get("byBiome", []):
            k = row["biome"]
            e = bio.setdefault(k, [0, 0.0, 0.0, 0.0])
            e[0] += row["n"]
            e[1] += row["winRate"] * row["n"] / 100.0
            e[2] += row["avgTurns"] * row["n"]
            e[3] += row["avgGroundTiles"] * row["n"]
    return runs, wins, bio


def se(k, n):
    p = k / n if n else 0
    return 100 * math.sqrt(max(p * (1 - p), 1e-9) / max(n, 1))


for h in (0, 2, 4):
    na, wa, ba = pool("A", h)
    nb, wb, bb = pool("B", h)
    print(f"\n=== heat {h} — campaign win: A {100*wa/na:.1f}%  B {100*wb/nb:.1f}%  "
          f"delta {100*(wa/na-wb/nb):+.1f}  (n={na}/arm) ===")
    print(f"{'biome':>9} | {'':1} | {'A n':>5} {'A win':>7} {'+/-':>5} {'grnd':>5} | "
          f"{'B n':>5} {'B win':>7} {'+/-':>5} | {'delta':>7} | {'A t':>5} {'B t':>5}")
    for k in sorted(set(ba) | set(bb)):
        an, aw, at, ag = ba.get(k, [0, 0, 0, 0])
        bn, bw, bt, _ = bb.get(k, [0, 0, 0, 0])
        if an == 0 and bn == 0:
            continue
        apct = 100 * aw / an if an else float("nan")
        bpct = 100 * bw / bn if bn else float("nan")
        star = "*" if k in MECH else " "
        print(f"{k:>9} | {star} | {an:5d} {apct:6.1f}% {se(aw,an):5.1f} {ag/max(an,1):5.1f} | "
              f"{bn:5d} {bpct:6.1f}% {se(bw,bn):5.1f} | {apct-bpct:+6.1f} | "
              f"{at/max(an,1):5.1f} {bt/max(bn,1):5.1f}")
    # the two populations, pooled
    for label, sel in (("MECHANICAL (3)", MECH), ("PAINT (5, control)", None)):
        an = aw = bn = bw = 0
        for k in set(ba) | set(bb):
            inset = (k in sel) if sel else (k not in MECH)
            if not inset:
                continue
            x = ba.get(k, [0, 0, 0, 0]); y = bb.get(k, [0, 0, 0, 0])
            an += x[0]; aw += x[1]; bn += y[0]; bw += y[1]
        if an and bn:
            print(f"{label:>18}: A {100*aw/an:5.1f}% (n={an})   B {100*bw/bn:5.1f}% (n={bn})   "
                  f"delta {100*(aw/an-bw/bn):+.1f}")
