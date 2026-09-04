# P21 "BUILD OWNS THE BOARD" — the inertness round

**Base commit `108d9ac`** (main, PROGRAM PARALLAX milestone 13). Branch `wave/build-owns-eight`.
Binaries: `runbin/base108` (a `git archive 108d9ac` build) and `runbin/p21` (the wave). Both
Release, both snapshotted before any chunk ran.

## What this round is for

P21 moves `Grid.ClearHazards()` into `Mission.Build`. L6 established that the defect it closes is
**LATENT, NOT LIVE** — `Game.SetupMission` is the only production caller of `Mission.Build` and it
clears the hazards unconditionally 28 lines before the call. This round **proves** that rather
than asserting it.

## Cells

Two rungs (h0, h4) x **16 CRN slot bases** (0, 10, ... 150) x 20 campaigns x greedy+sloppy
= **40 campaigns per chunk, 32 chunks, 1,280 campaigns per arm.** Heat pinned
(`EventCatalog.HeatPinned`, the default). Every chunk asserted by
`docs/measurements/p15/check_chunk.py` (copied here): `runs=40`, the rung and slot base the file
name claims, `heat pinned`, `campaignsRaised = missionsAbovePin = 0`. **32/32 OK, 0 BAD on every
arm** (re-verified after the round, because the parallel driver appends to one log and dropped an
`OK` line twice).

| arm | binary | env | n |
|---|---|---|---|
| `base` | `runbin/base108` | defaults | 1,280 |
| `p21` | `runbin/p21` | defaults | 1,280 |
| `p21stale` | `runbin/p21` | `SIGHTLINE_STALEHAZARDS=1` | 1,280 |
| `forkprices` | `runbin/p21` | `SIGHTLINE_FORKPRICES=0` | 1,280 |

## Result 1 — the whole P21 diff is inert at its shipped defaults

`docs/measurements/w1/inert_diff.sh <base> <p21> harness` on all 32 cells:

```
base vs p21 (defaults): IDENTICAL=32 DIFFERENT=0
```

Every per-slot outcome, win, loss, turn count, objective row, shots-per-kill and node/arena
cross-tab is byte-identical once the `harness{}` block (nproc/loadavg/elapsed, designed to vary)
is excluded. **1,280 CRN campaigns per arm.**

## Result 2 — and the added line specifically is a live-path no-op

Same binary, the new clear switched off:

```
p21 vs p21stale (the added line switched off): IDENTICAL=32 DIFFERENT=0
```

This is the stronger of the two statements, because it isolates *exactly* the one line: with
`SIGHTLINE_STALEHAZARDS=1` the pre-P21 code path runs, and it produces the same 1,280 campaigns.
That is what "latent, not live" means, measured.

**A note for anyone re-running this.** The flag being inert is not a failure of the flag — it is
the finding. `SIGHTLINE_STALEGROUND=1` (P20) moves the board because nothing else cleared Ground;
`SIGHTLINE_STALEHAZARDS=1` cannot, because `Game.SetupMission` still clears the hazards first and
was deliberately left doing so. The flag's job here is the house rule and MODETEST's
detector-can-fail arm, not a price. **If this diff ever comes back non-empty, a live path exists
that nobody has found.**

## Result 3 — a free bridge cross-check to the ladder of record

The `p21` arm's 32 cells overlap L6's ladder cells (L6 ran N=10 per chunk, this round N=20, on the
same 16 slot bases and the same two rungs). Restricted to the overlapping slots:

```
L6 ladder (base 6a6ebee, N=10) vs P21 default arm (108d9ac + P21, N=20):
32/32 chunks, 640/640 campaigns reproduce (win, missionsCleared, runTurns, endMission)
```

So the CRN chain is intact from L6's base through milestone 13 and through this wave. The `p21`
arm reads **h0 44.4% / h4 23.8%**, which is L6's ladder to the decimal at both rungs.

## Result 4 — `SIGHTLINE_FORKPRICES=0` is not decoration

| rung | n | shipped | `FORKPRICES=0` | delta | n_disc | MDE(80%) | McNemar z | course changed |
|---|---|---|---|---|---|---|---|---|
| h0 | 640 | 44.4 | 44.8 | −0.5 | 147 | 5.3 | −0.25 | 71.7% |
| h4 | 640 | 23.8 | 25.5 | −1.7 | 137 | 5.1 | −0.94 | 65.0% |
| **pooled** | **1,280** | **34.1** | **35.2** | **−1.1** | **284** | **3.7** | **−0.83** | **68.4%** |

All 32 chunks differ; 875 of 1,280 paired campaigns take a different course. **That is the whole
claim: the flag bites.** The win-rate column is **NOT** a price for THE FORK PAYS and must not be
quoted as one — every rung sits inside its own MDE, the pooled z is 0.83, and this is a
flag-vs-default contrast on today's tree, not the milestone-4-vs-5 contrast L6 measured
(`−0.36` pooled over 1,920 pairs, also unresolved). The two are consistent in sign and neither
resolves anything.

Note also that the flag restores the **prices only**. THE FORK PAYS' other gameplay change — the
SUPPLY full heal moving from before `Run.DebriefSurvivors` to inside it (SUPPLY is 828 of 5,413 played nodes here, 15.3%) — is still unswitchable
and is present in **both** arms of this table. See `docs/ROADMAP.md`.

## Reproducing

```bash
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
cd <worktree>
mkdir -p .xdg && export XDG_CONFIG_HOME="$PWD/.xdg"
dotnet build -c Release && mkdir -p runbin/p21 && cp -r bin/Release/net8.0/. runbin/p21/
git archive 108d9ac | tar -x -C /tmp/base108 && (cd /tmp/base108 && dotnet build -c Release)
mkdir -p runbin/base108 && cp -r /tmp/base108/bin/Release/net8.0/. runbin/base108/

bash docs/measurements/p21/round.sh base        # 32 chunks, ~3 min at PAR=3
bash docs/measurements/p21/round.sh p21
bash docs/measurements/p21/round.sh p21stale
bash docs/measurements/p21/round.sh forkprices

for h in 0 4; do for b in 0 10 20 30 40 50 60 70 80 90 100 110 120 130 140 150; do
  bash docs/measurements/w1/inert_diff.sh \
    docs/measurements/p21/base-h$h-b$b.json docs/measurements/p21/p21-h$h-b$b.json harness >/dev/null \
    || echo "DIFF h$h-b$b"
done; done
```

A chunk is ~13 s wall on this box at PAR=3 (4 cores, shared).
