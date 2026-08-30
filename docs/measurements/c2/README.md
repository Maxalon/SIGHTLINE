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
| `hook.sh` | runs one `SIGHTLINE_*` hook under xvfb with the checkout's isolation exports (root derived from the script's own location). |
| `BEFORE-h0-b0.*` | the pre-change decision mix (`AIDECLINE=0`), n=20 runs / 89 missions. The wave's "instrument first" chunk. |
| `AFTER-h0-b0.*` | the same slots with the change on, at the first (later rejected) pricing. |
| `CAL-*` | the calibration chunks — **see the lever table below**, which records what each one ran with and which three pairs are duplicates. `CAL-diag` is the **maximal-decline diagnostic**, the only chunk here whose lanes are genuinely overwatch (442 of 457) rather than PIKEMAN brace. |
| `R0diag-*` | logic-identity: the base-commit binary vs this tree with `AIDECLINE=0`, two slot sets. |
| `L-{BASE,DECL}-h*-b*.*` | the ladder chunks. |
| `POST-*` | one ladder chunk per ARM re-run on the FINAL binary after three behaviour-neutral tidies; both diff empty against their archived `L-*` JSON outside `harness`, which is the proof the ladder binary is the shipped binary. |
| `paired.py` | McNemar over the identical slot seeds — the honest test for a CRN round, since the two arms play the same worlds. |
| `qa-sweep-full.txt` | the pre-merge gate, run on a FROZEN source tree. `qa-sweep-full.interim.txt` and `.interim2.txt` are two earlier full sweeps — both green — DISCARDED because a source edit landed mid-run each time and `run()` is `dotnet run -c Debug`, which rebuilds per test: they spanned two binaries, and a gate that spans two builds is not a gate. |
| `shots/` | the two staged frames (`SIGHTLINE_DECLINESHOT`), flag on and off. |
| `ladder.progress.prelim.txt` | a first 48-chunk pass on an interim binary, kept for provenance. **The round of record is `ladder.progress.txt`**, run on the final binary. |

## THE ARCHIVE NEEDED `git add -f`, AND THE NEXT WAVE WILL TOO

Six artifacts this write-up cites BY PATH were silently absent from the first version of this
archive — `run_chunk.sh`, `qa-sweep-full.txt` and its two interims, and both PNGs in `shots/`.
They were on disk and simply never committed, because `.gitignore` carries rules broad enough to
swallow them wholesale:

| rule | what it ate |
|---|---|
| `.gitignore:57` `run_chunk.sh` | **every wave's chunk runner**, by bare filename, anywhere in the tree |
| `.gitignore:65` `qa*.txt` | every `qa-sweep-*.txt` |
| `.gitignore:35` `shots/` | every screenshot directory |

`ladder.sh` even CALLS `run_chunk.sh`, so the reproduction path was broken by a rule that names
the file it depends on. All six are now committed with `git add -f`. **If you add an artifact to
this directory, run `git status --short` and `git check-ignore -v <path>` before you claim it is
archived** — a cited path that is not in `git ls-files` is a citation to nothing.

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

Both print `(empty diff — IDENTICAL)`, over **1304 and 1216 leaf scalars** respectively (1916 and
1786 lines of normalised JSON) — every per-slot RunRec, win, loss, turn count and shots-per-kill.
The two chunks are different sizes, so there is no single field count for the pair; an earlier
version of this file quoted "2750" for both and it reproduces under no convention. `harness` is excluded because it records nproc /
loadavg / elapsed and is designed to vary; `enemyDecisions` because the base binary has no
such block.

## What each `CAL-*` chunk actually ran — the calibration ladder

The first version of this archive shipped nine calibration chunks with nothing recording which
lever each one carried, and three of them are duplicates. All were heat 0 / `SIGHTLINE_BALANCE_BASE 0`
(the `-h4` suffix is the same settings at heat 4), N=10, so 20 campaigns each. Every chunk ran with
`SIGHTLINE_AIDECLINE=1`; `BEFORE-h0-b0` is the matching `=0` leg.

| chunk | watch ratio | dig ratio | abs keep | threat scale | dig-in | what it was for |
|---|---|---|---|---|---|---|
| `BEFORE-h0-b0` | — | — | — | — | — | the pre-change mix (`AIDECLINE=0`) |
| `AFTER-h0-b0` | *(absolute bars 1.45 / 0.90 E\[dmg])* | | | no | no | the REJECTED absolute-bar pricing — declined 35 shots at 80%+ |
| `CAL-a` / `CAL-a-h4` | 0.70 | 0.45 | 2.60 | no | no | first ratio pricing, deliberately loose |
| `CAL-b` / `CAL-b-h4` | 0.25 | 0.15 | 2.00 | no | no | the ratio implied by the measured lane value alone — fired 0 declines |
| `CAL-c` / `CAL-c-h4` | 0.45 | 0.30 | 2.20 | no | no | the shipped ratios, tight absolute guard |
| `CAL-d` / `CAL-d-h4` | 0.45 | 0.30 | 3.00 | no | no | same, loose guard — proves `AbsKeep` is INERT at a 0.45 ratio |
| `CAL-diag` / `-h4` | **1.10** | 0.00 | 999 | no | no | the **maximal-decline diagnostic**: decline everything a lane can replace |
| `CAL-e` / `CAL-e-h4` | 0.45 | 0.30 | 3.00 | **yes** | no | adds the guns-on-me bar scaling |
| `CAL-f` | 0.45 | 0.30 | 3.00 | yes | **yes** | adds `DeclineDigIn` — the SHIPPED configuration |

**Three pairs are byte-identical outside `harness`, and that IS the result in each case:**

- `CAL-c ≡ CAL-d` — raising `DeclineAbsKeep` 2.20 → 3.00 changed nothing, because at a 0.45 ratio
  a qualifying shot is weak by construction and the absolute guard can never bind. It ships at
  3.00 as a guard against a future wave raising the ratios, not as an active lever.
- `CAL-e ≡ CAL-f` — `DeclineDigIn` never fired in those 20 campaigns (the "two or more guns AND
  cover to hand AND a bad shot" conjunction did not occur). See DEVLOG §9: this is why the wave
  does not claim it as an effect.
- `CAL-b-h4 ≡ CAL-c-h4 ≡ CAL-d-h4` — at heat 4 none of those three settings produced a single
  decline, so all three collapse to the same run.

**What this ladder is and is not.** It is a swept set of candidate settings scored on ONE
20-campaign slot set. That is enough to reject the absolute-bar pricing and to bound the resulting
decline rate; it is **not** enough to locate an optimum, and no chunk was replicated across slot
sets. See DEVLOG §4 for what is measured (the lane, ~0.20) versus what is judged (the 0.45 bar).

## Reproducing

```bash
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg"; export XDG_CONFIG_HOME="$PWD/.xdg"
dotnet build -c Release && mkdir -p runbin/C2L && cp -r bin/Release/net8.0/* runbin/C2L/
bash docs/measurements/c2/ladder.sh          # ~48 chunks (root is derived, not hardcoded)
python3 docs/measurements/c2/ladder_table.py
```
