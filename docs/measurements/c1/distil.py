#!/usr/bin/env python3
"""Distil every C1 chunk JSON into ONE csv of per-campaign outcomes.

The raw round is 416 chunks / 34 MB (17 MB of JSON, 10 MB of log, 6.6 MB of report), which
this repo does not commit. Everything C1 concluded rests on the PER-SLOT outcome of each
campaign — that is what makes the rounds CRN-paired — so the archive keeps exactly that, in
full, for all 16,640 campaigns, at 428 KB. `ladder.py --csv` re-derives every published
table from it, so no number in the write-up is unreproducible.

What is DROPPED and why: the aggregate blocks (actionMix, byArena, perkPicks, ...) are
per-chunk sums, recomputable only from a re-run — but nothing C1 claims uses them except
`inert_diff.py`, whose job is done and whose result (0 differing fields) is recorded in the
README. The .log files are raylib boilerplate plus a per-match progress line.

Usage: distil.py > chunks.csv
"""
import csv, glob, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
PAT = re.compile(r"^(?P<prefix>[A-Za-z0-9]+)-h(?P<heat>-?\d+)-b(?P<base>\d+)\.json$")


def main():
    w = csv.writer(sys.stdout)
    w.writerow(["prefix", "heat", "base", "slot", "policy", "win", "missions"])
    n = 0
    for f in sorted(glob.glob(os.path.join(HERE, "*.json"))):
        m = PAT.match(os.path.basename(f))
        if not m:
            continue
        d = json.load(open(f))
        for s in d["pairedPolicy"]["slots"]:
            w.writerow([m["prefix"], m["heat"], m["base"], s["slot"], "greedy",
                        int(bool(s["greedyWin"])), s["greedyMissions"]])
            w.writerow([m["prefix"], m["heat"], m["base"], s["slot"], "sloppy",
                        int(bool(s["sloppyWin"])), s["sloppyMissions"]])
            n += 2
    print(f"# {n} campaigns", file=sys.stderr)


if __name__ == "__main__":
    main()
