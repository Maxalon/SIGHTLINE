#!/usr/bin/env python3
"""P54 item 3 — WHAT THE OTHER 73% LOOKS LIKE. Still no new compute.

P54 explained the bucket that GREW with heat (the protected NPC, 0.4% -> 27%). RUN OVER is the
majority at every rung and P54 said nothing about it. This cross-tabs the wipe side by rung from
fields that have been in every chunk all along: `soldierDeathsByEnemy` (who kills soldiers),
`actionMix` (what the squad does with its turns) and `shotGap` (how concentrated the shooting is).

Unforced ladder rounds only; `levers.siteGlyphs` chunks skipped. Shares are normalised per rung, so
the columns answer "what CHANGES with heat", not "how much data is there".

Usage: wipe_rungs.py [rung ...]     (default 0 4 8)
"""
import collections, glob, json, os, sys

ROUNDS = ["l6", "l7", "p23", "p24"]
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")


def chunks(rung):
    for rnd in ROUNDS:
        for f in glob.glob(os.path.join(ROOT, rnd, f"*-h{rung}-b*.json")):
            j = json.load(open(f))
            if (j.get("levers") or {}).get("siteGlyphs"):
                continue
            yield j


def main():
    rungs = [int(a) for a in sys.argv[1:]] or [0, 4, 8]
    killers, actions, gaps = {}, {}, {}
    for rung in rungs:
        k = collections.Counter()
        a = collections.Counter()
        turns = 0.0
        gsum = 0.0
        gw = 0.0
        sole = 0.0
        for j in chunks(rung):
            for who, n in (j.get("soldierDeathsByEnemy") or {}).items():
                k[who] += n
            for pol in ("greedy", "sloppy"):
                for verb, n in ((j.get("actionMix") or {}).get(pol) or {}).items():
                    a[verb] += n
                    turns += n
            sg = j.get("shotGap") or {}
            w = sg.get("armedSoldierTurns") or 0
            if w:
                gsum += (sg.get("meanGap") or 0) * w
                sole += (sg.get("soleOrDominantPct") or 0) * w
                gw += w
        killers[rung], actions[rung] = k, a
        gaps[rung] = (gsum / gw if gw else 0, sole / gw if gw else 0, gw)

    print("===== WHO KILLS SOLDIERS (share of all soldier deaths at that rung) =====")
    names = set()
    for rung in rungs:
        names |= {n for n, _ in killers[rung].most_common(8)}
    print("  " + "archetype".ljust(13) + "".join(f"h{r}".rjust(9) for r in rungs) + "     h0->h8")
    rows = []
    for n in names:
        cells = []
        for rung in rungs:
            tot = sum(killers[rung].values()) or 1
            cells.append(100 * killers[rung][n] / tot)
        rows.append((cells[-1] - cells[0], n, cells))
    for d, n, cells in sorted(rows, reverse=True):
        print("  " + n.ljust(13) + "".join(f"{c:8.1f}%" for c in cells) + f"   {d:+6.1f}")

    print("\n===== WHAT THE SQUAD DOES (share of all actions at that rung) =====")
    verbs = set()
    for rung in rungs:
        verbs |= {v for v, _ in actions[rung].most_common(10)}
    print("  " + "verb".ljust(13) + "".join(f"h{r}".rjust(9) for r in rungs) + "     h0->h8")
    rows = []
    for v in verbs:
        cells = []
        for rung in rungs:
            tot = sum(actions[rung].values()) or 1
            cells.append(100 * actions[rung][v] / tot)
        rows.append((cells[-1] - cells[0], v, cells))
    for d, v, cells in sorted(rows, reverse=True):
        print("  " + v.ljust(13) + "".join(f"{c:8.1f}%" for c in cells) + f"   {d:+6.1f}")

    print("\n===== SHOT CONCENTRATION (n-weighted over armed-soldier turns) =====")
    print("  " + "metric".ljust(24) + "".join(f"h{r}".rjust(11) for r in rungs))
    print("  " + "meanGap".ljust(24) + "".join(f"{gaps[r][0]:10.3f} " for r in rungs))
    print("  " + "soleOrDominantPct".ljust(24) + "".join(f"{gaps[r][1]:10.1f} " for r in rungs))
    print("  " + "armedSoldierTurns".ljust(24) + "".join(f"{gaps[r][2]:10.0f} " for r in rungs))


if __name__ == "__main__":
    main()
