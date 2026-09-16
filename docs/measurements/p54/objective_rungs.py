#!/usr/bin/env python3
"""P54 item 2 — PER-OBJECTIVE MISSION WIN RATE BY RUNG, from the existing archive.

P54 reported LOSS-CAUSE shares. That is not a win rate, and W8's rule (a pooled objective row hid a
49.5-point artifact, because `Eliminate`'s 89.1% was largely 960 mission-1s) says the two must not
be conflated. This recomputes the thing that was actually claimed to be at stake.

It aggregates `byObjectiveByMission` — n-weighted, so a chunk with 2 missions does not count as much
as one with 40 — and prints per-objective win rate at each rung, plus the MID-RUN cells only
(missions 3-5), which is where P54 found the NPC deaths land.

Unforced ladder rounds only; `levers.siteGlyphs` chunks skipped.

Usage: objective_rungs.py [rung ...]     (default 0 4 8)

⚠ These are PER-MISSION rates and are subject to P15's correction: mission-level tables from before
2026-09-03 are survivorship-biased. All four rounds read here are post-P15.
"""
import collections, glob, json, os, sys

ROUNDS = ["l6", "l7", "p23", "p24"]
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")


def rows(rung):
    for rnd in ROUNDS:
        for f in glob.glob(os.path.join(ROOT, rnd, f"*-h{rung}-b*.json")):
            j = json.load(open(f))
            if (j.get("levers") or {}).get("siteGlyphs"):
                continue
            for r in j.get("byObjectiveByMission") or []:
                yield r


def main():
    rungs = [int(a) for a in sys.argv[1:]] or [0, 4, 8]
    objs = set()
    allw, midw = {}, {}
    for rung in rungs:
        w = collections.defaultdict(lambda: [0, 0.0])   # obj -> [n, wins]
        m = collections.defaultdict(lambda: [0, 0.0])
        for r in rows(rung):
            o, n, wr, mi = r["objective"], r["n"], r["winRate"], r.get("mission")
            objs.add(o)
            w[o][0] += n; w[o][1] += n * wr / 100.0
            if mi in (3, 4, 5):
                m[o][0] += n; m[o][1] += n * wr / 100.0
        allw[rung], midw[rung] = w, m

    for label, data in (("ALL MISSIONS", allw), ("MID-RUN ONLY (m3-m5)", midw)):
        print(f"\n===== {label} — per-objective mission win rate by rung =====")
        head = "  " + "objective".ljust(12) + "".join(f"  h{r}".rjust(16) for r in rungs)
        print(head)
        for o in sorted(objs):
            cells = []
            for r in rungs:
                n, wins = data[r][o]
                cells.append(f"{100*wins/n:6.1f}% (n={n:5d})" if n else "        —      ")
            print("  " + o.ljust(12) + "".join(cells))


if __name__ == "__main__":
    main()
