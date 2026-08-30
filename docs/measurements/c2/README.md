# C2 — "THE OPPONENT DECLINES"

**Base commit: `17934ee`** (PROGRAM CROSSCUT composed, all eight waves). Branch
`wave/opponent-declines`. Everything here was produced on that tree plus this wave's diff.

## What is in this directory

| file(s) | what it is |
|---|---|
| `run_chunk.sh` | the chunk runner. A verbatim descendant of `l3/run_chunk.sh` (all three layers of W1's completion contract) with one addition: `SIGHTLINE_AIDECLINE` is stamped explicitly on **every** chunk, so a baseline chunk can never silently inherit the shipped default. |
| `ladder.sh` | the paired round: 6 rungs x 4 slot sets x 2 arms x N=10 (greedy+sloppy) = **80 campaigns per rung per arm, 960 total**. |
| `ladder_table.py` | pools the four slot sets per rung and prints the paired ladder + the pooled decision mix. |
| `summarise.py` | the wave's tripwires for any single chunk: mission length, shots/kill, and the enemy decision mix. |
| `hook.sh` | runs one `SIGHTLINE_*` hook under xvfb with this worktree's isolation exports. |
| `BEFORE-h0-b0.*` | the pre-change decision mix (`AIDECLINE=0`), n=20 runs / 89 missions. The wave's "instrument first" chunk. |
| `AFTER-h0-b0.*` | the same slots with the change on, at the first (later rejected) pricing. |
| `CAL-*.` | the calibration chunks. `CAL-diag` is the **maximal-decline diagnostic** — the one that priced the lane. |
| `R0diag-*` | logic-identity: the base-commit binary vs this tree with `AIDECLINE=0`, two slot sets. |
| `L-{BASE,DECL}-h*-b*.*` | the ladder chunks. |
| `POST-*` | one ladder chunk per ARM re-run on the FINAL binary after three behaviour-neutral tidies; both diff empty against their archived `L-*` JSON outside `harness`, which is the proof the ladder binary is the shipped binary. |
| `paired.py` | McNemar over the identical slot seeds — the honest test for a CRN round, since the two arms play the same worlds. |
| `qa-sweep-full.txt` | the pre-merge gate on the final tree. `qa-sweep-full.interim.txt` is an earlier full sweep DISCARDED because comment-only edits landed mid-run and `run()` rebuilds per test, so it spanned two binaries. |
| `shots/` | the two staged frames (`SIGHTLINE_DECLINESHOT`), flag on and off. |
| `ladder.progress.prelim.txt` | a first 48-chunk pass on an interim binary, kept for provenance. **The round of record is `ladder.progress.txt`**, run on the final binary. |

## The three-layer completion contract (do not shortcut it)

`run_chunk.sh` (a) `rm -f`s the target JSON first, (b) checks the process **exit code** (2 =
"no display, nothing written"), (c) asserts the JSON's own `runs` field == 2N. All three,
in that order. Every chunk in `ladder.progress.txt` printed `OK ... runs=20`; zero `BAD`.

## R0diag — the instrument is inert

This wave adds telemetry (`Stats.RecordEnemyDecision`, `Stats.RecordEnemyReaction`, an extra
`ComputeOdds`+`ExpectedDamage` per plan) that runs in **both** arms. Before any paired number
could be trusted, the `AIDECLINE=0` arm had to be proven identical to the base commit:

```
bash docs/measurements/w1/inert_diff.sh R0diag-base-h0-b0.json  R0diag-c2-h0-b0.json  harness enemyDecisions
bash docs/measurements/w1/inert_diff.sh R0diag-base-h4-b10.json R0diag-c2-h4-b10.json harness enemyDecisions
```

Both print `(empty diff — IDENTICAL)` over **2750 aggregate fields** — every per-slot RunRec,
win, loss, turn count and shots-per-kill. `harness` is excluded because it records nproc /
loadavg / elapsed and is designed to vary; `enemyDecisions` because the base binary has no
such block.

## Reproducing

```bash
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg"; export XDG_CONFIG_HOME="$PWD/.xdg"
dotnet build -c Release && mkdir -p runbin/C2L && cp -r bin/Release/net8.0/* runbin/C2L/
bash docs/measurements/c2/ladder.sh          # ~48 chunks
python3 docs/measurements/c2/ladder_table.py
```
