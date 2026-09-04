#!/usr/bin/env python3
"""P15 THE UNVERIFIED — the CHUNK COMPLETION CHECK, as a check instead of a guess.

Layer (c) of CLAUDE.md's SIGHTLINE_BALANCE measurement contract used to be, in EVERY chunk runner
in this repository (w1, w2, w4, w8, x1, x2, l3, c1, c2, c3, c4, tb, p10, fork-pays — 14 of them,
including the `c1/run_chunk.sh` that produced the L5 ladder of record):

    EXPECT=$((N*2))
    GOT=$(python3 -c "...['runs']")
    if [ "$GOT" = "$EXPECT" ]; then echo OK; else echo BAD; fi

That hard-codes the greedy+sloppy default. A SINGLE-POLICY batch — `SIGHTLINE_BALANCE_SLOPPY=1`
or `_DUMB=1`, the exact shape `campaigns[]` was shipped to enable CRN-pairing for — produces N
runs and is marked **BAD** by a check that ran perfectly. And the check is silent about the thing
that actually decides what the chunk MEANS: it never looks at the rung or the slot base at all,
so a batch whose `SIGHTLINE_BALANCE_HEAT` failed to parse (silent fallback to the cycled default
set, pre-P15) passed layer (c) with `runs` correct while measuring a different ladder.

This script replaces layer (c) with three assertions the artifact can actually support:

  RUNS   `runs` == `batch.expectedRuns` — the batch's OWN arithmetic (N x policy legs), not the
         caller's assumption. Falls back to an explicit `--expect` for a pre-P15 JSON, and says
         out loud that it is guessing.
  NAME   `batch.heatRequested` / `batch.baseRequested` echo the RAW strings the runner exported,
         and `batch.heat` / `batch.slotBase` are what they parsed to. This is what makes a chunk's
         FILE NAME checkable against what it measured.
  CLEAN  `batch.envErrors` empty (unreachable — the batch refuses — but assert it anyway) and, for
         a pinned round, `heatLeak.pinned` true with campaignsRaised = missionsAbovePin = 0.

Usage:
  check_chunk.py <chunk.json> [--heat H] [--base B] [--expect N] [--pinned|--unpinned]
Exit 0 = OK, 1 = BAD. Prints one line per assertion.
"""
import argparse
import json
import os
import sys


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("path")
    ap.add_argument("--heat", default=None, help="the RAW value the runner exported as SIGHTLINE_BALANCE_HEAT")
    ap.add_argument("--base", default=None, help="the RAW value the runner exported as SIGHTLINE_BALANCE_BASE")
    ap.add_argument("--expect", type=int, default=None, help="fallback expected `runs` for a pre-P15 JSON")
    ap.add_argument("--pinned", action="store_true", help="assert the heat pin held (the default for a ladder round)")
    ap.add_argument("--unpinned", action="store_true", help="this chunk was deliberately run with SIGHTLINE_HEATPIN=0")
    ap.add_argument("--legacy", action="store_true",
                    help="this is an ARCHIVED pre-P15 artifact with no `batch` block. Downgrades the NAME "
                         "assertion to a printed caveat and falls back to --expect for RUNS. Only ever passed "
                         "EXPLICITLY by a human re-reading history — the script never downgrades itself, which "
                         "is the whole defect P15 fixed in l5/cluster.py.")
    a = ap.parse_args()

    tag = os.path.basename(a.path)
    if not os.path.exists(a.path):
        print(f"BAD  {tag} — no JSON on disk. The batch wrote nothing (see the exit code: 2 = no display, 3 = bad batch env).")
        return 1
    try:
        d = json.load(open(a.path))
    except Exception as e:
        print(f"BAD  {tag} — JSON did not parse: {e}")
        return 1

    bad = []
    batch = d.get("batch")
    runs = d.get("runs")

    # ── RUNS ────────────────────────────────────────────────────────────────────────
    if batch:
        exp = batch.get("expectedRuns")
        if runs != exp:
            bad.append(f"runs={runs} but the batch asked for {exp} ({batch.get('n')} x {batch.get('policyLegs')} legs, {batch.get('policies')})")
        else:
            print(f"     RUNS  {runs} == batch.expectedRuns ({batch.get('n')} x {batch.get('policyLegs')} legs, {batch.get('policies')})")
    elif not a.legacy:
        bad.append("no `batch` block: this artifact came from a PRE-P15 binary, so the rung, the slot base and "
                   "the run count it should hold are all unverifiable from the file. Re-run it, or pass --legacy "
                   "to read it as history.")
    elif a.expect is not None:
        # pre-P15 archive: there is no `batch` block, so this is the caller's assumption, not a check.
        if runs != a.expect:
            bad.append(f"runs={runs}, wanted {a.expect} (pre-P15 JSON: no batch block, --expect is an ASSUMPTION)")
        else:
            print(f"     RUNS  {runs} == --expect {a.expect}  (pre-P15 JSON: no `batch` block; this is an assumption, not a check)")
    else:
        bad.append("no `batch` block and no --expect: this JSON cannot say how many runs it should hold")

    # ── NAME ────────────────────────────────────────────────────────────────────────
    if batch:
        if a.heat is not None:
            got_raw, got = batch.get("heatRequested"), batch.get("heat")
            want_raw = a.heat if a.heat != "" else None
            if got_raw != want_raw:
                bad.append(f"batch.heatRequested={got_raw!r} but the runner exported {want_raw!r}")
            elif want_raw is not None and str(got) != str(int(want_raw)):
                bad.append(f"batch.heat={got} for a request of {want_raw!r}")
            else:
                print(f"     NAME  heat requested {got_raw!r} -> pinned {got}")
        if a.base is not None:
            got_raw, got = batch.get("baseRequested"), batch.get("slotBase")
            want_raw = a.base if a.base != "" else None
            if got_raw != want_raw:
                bad.append(f"batch.baseRequested={got_raw!r} but the runner exported {want_raw!r}")
            elif want_raw is not None and str(got) != str(int(want_raw)):
                bad.append(f"batch.slotBase={got} for a request of {want_raw!r}")
            else:
                print(f"     NAME  base requested {got_raw!r} -> slotBase {got}")
        if batch.get("envErrors"):
            bad.append(f"batch.envErrors={batch['envErrors']} (a batch that could not name itself still wrote a file)")
    elif a.heat is not None or a.base is not None:
        print("     NAME  (pre-P15 JSON: no `batch` block — the rung and slot base are UNVERIFIABLE from the artifact)")

    # ── CLEAN ───────────────────────────────────────────────────────────────────────
    leak = d.get("heatLeak")
    if leak is None:
        print("     CLEAN (pre-heat-pin JSON: no `heatLeak` block)")
    elif a.unpinned:
        print(f"     CLEAN pin deliberately OFF: pinned={leak.get('pinned')} raised={leak.get('campaignsRaised')} offRung={leak.get('missionsAbovePin')}")
        if leak.get("pinned"):
            bad.append("--unpinned was asserted but the chunk ran PINNED")
    else:
        if not leak.get("pinned"):
            bad.append("heatLeak.pinned is false — this chunk played a rung it does not name (SIGHTLINE_HEATPIN=0?)")
        elif leak.get("campaignsRaised") or leak.get("missionsAbovePin"):
            bad.append(f"pinned yet leaked: campaignsRaised={leak['campaignsRaised']} missionsAbovePin={leak['missionsAbovePin']}")
        else:
            print("     CLEAN heat pinned; campaignsRaised = missionsAbovePin = 0")

    if bad:
        print(f"BAD  {tag}")
        for b in bad:
            print(f"       - {b}")
        return 1
    print(f"OK   {tag} runs={runs}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
