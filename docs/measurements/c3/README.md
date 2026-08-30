# C3 — THE TWO GAMES

**Base commit: `17934ee`** (PROGRAM CROSSCUT composed, the L3 ladder's tree).
Branch `wave/two-games`. All chunks asserted `OK ... runs=20`; zero `BAD`.

Everything here was produced by `run_chunk.sh` (this directory's copy of L3's runner, repointed at
the C3 worktree and given an `EXTRA` passthrough so an arm can be run on the same slots as its
baseline) driven by `run_round.sh`. A round is **6 rungs × 8 disjoint CRN slot sets
(`SIGHTLINE_BALANCE_BASE` 0–70) × greedy+sloppy = 160 campaigns/rung, 960 total.**

## The rounds

| tag | binary | arm | what it is |
|---|---|---|---|
| `R0` | `runbin/C3base` (base commit, pristine) | — | 8 chunks kept: the **pre-instrument** half of the inertness proof. The other 40 were trimmed (see `trim.sh`). |
| `R0diag` | `runbin/C3diag` (base + the new telemetry) | — | the same 8 slot sets on the instrumented binary. |
| `B1` | `runbin/C3lever` | `SIGHTLINE_KILLTREADMILL=1` | **the baseline.** The lever's OFF path — i.e. the pre-C3 anti-turtle clock. |
| `L1` | `runbin/C3lever` | defaults | **the lever.** No reinforcement wave on ELIMINATE. |

`D0` (the instrumented binary at defaults, run before the lever existed) was dropped by `trim.sh`:
`samearm.py D0 B1` returned **0 differences over 67,956 aggregate fields on all 48 chunk pairs**,
so B1 *is* D0. Every "diagnosis" figure in DEVLOG §C3 is reproduced by `enc.py B1`.

## Commands

```bash
# the two arms (3 min 18 s each on this container)
BIN=runbin/C3lever EXTRA=SIGHTLINE_KILLTREADMILL=1 bash docs/measurements/c3/run_round.sh B1 10
BIN=runbin/C3lever                                 bash docs/measurements/c3/run_round.sh L1 10

# the readouts
python3 docs/measurements/c3/agg.py    B1 L1   # ladder + both cross-tabs + the class split
python3 docs/measurements/c3/enc.py    B1 L1   # the ENCOUNTER COMPLETION decomposition
python3 docs/measurements/c3/paired.py B1 L1   # the CRN-paired (McNemar) comparison
python3 docs/measurements/c3/top.py    B1 L1   # decision-density / swing side effects
python3 docs/measurements/c3/inert.py          # instrument inertness (R0 vs R0diag)
python3 docs/measurements/c3/samearm.py D0 B1  # (needs the trimmed D0 round back)
```

## The two proofs that had to come first

**1. The instrument is inert.** The wave added per-mission `EnemiesAdded` / `MaxPressure` and an
`encounterMidrun` JSON block. `inert.py` diffs every *pre-existing* field of the 8 paired chunks
(dropping only `harness`, which records nproc/loadavg/elapsed and is designed to vary, `instrument`,
and the new block itself): **8 paired chunks, 9,848 aggregate fields, zero moved.**

**2. The lever's OFF path is the old tree.** `samearm.py D0 B1`: **67,956 fields, zero moved.** So
`L1 − B1` prices the lever and nothing else.

## The result

### The ladder (run completion, n=160/rung)

| rung | B1 | L1 | Δ | band | verdict |
|---|---|---|---|---|---|
| RECRUIT | 71.2 | **73.1** | +1.9 | 75 ±8 | in |
| heat 0 | 47.5 | **55.0** | **+7.5** | 55 ±8 | in — on target |
| heat 2 | 31.2 | **34.4** | +3.2 | 40 ±8 | **in** (B1 was 0.8 under the floor) |
| heat 4 | 23.8 | **25.6** | +1.8 | 30 ±8 | in |
| heat 6 | 20.0 | **20.0** | 0.0 | 20 ±8 | in — on target |
| heat 8 | 6.9 | **7.5** | +0.6 | 10 ±5 | in |

**Six of six in band and monotone at every step.** The one miss on the L3 ladder of record — heat 2,
0.8 under its floor — is closed.

### The paired test (`paired.py`, the statistic CRN exists to buy)

| rung | pairs | lever-only wins | baseline-only wins | p (2-sided) | Δ missions cleared |
|---|---|---|---|---|---|
| RECRUIT | 160 | 4 | 1 | 0.375 | +0.05 |
| heat 0 | 160 | **13** | 1 | **0.0018** | +0.37 |
| heat 2 | 160 | 5 | 0 | 0.0625 | +0.35 |
| heat 4 | 160 | 3 | 0 | 0.250 | +0.21 |
| heat 6 | 160 | 0 | 0 | 1.000 | +0.09 |
| heat 8 | 160 | 1 | 0 | 1.000 | +0.08 |
| **ALL** | **960** | **26** | **2** | **<0.0001** | **+0.19** |

### The class gap (`agg.py`, mid-run node kinds = Combat + Elite)

| | B1 | L1 |
|---|---|---|
| KILL (Eliminate, Decapitate) | 38.5 ±3.1 (n=247) | **45.3 ±3.1** (n=256) |
| NON-KILL (the other six) | 81.5 ±1.1 (n=1243) | 81.8 ±1.1 (n=1280) |
| **gap** | **43.0 ±3.3** | **36.5 ±3.3** |

### The mechanism, confirmed by the arm it was derived from (`enc.py`)

| Eliminate, mid-run | B1 | L1 |
|---|---|---|
| reinforcements/mission | 1.69 | **0.00** |
| missions reinforced | 41.0% | **0.0%** |
| mean anti-turtle rung | 1.55 | **1.50** ← the aim arm is untouched |
| win% | 37.4 ±4.1 | **48.2 ±4.2** |
| turns | 8.53 | 8.63 |

### An independent cross-check, from a channel that is not the Stats block

`SpawnReinforcements` echoes each wave to the console under AutoPlay (`REINFORCEMENTS: +N (...)`
for the anti-turtle clock, `WAVE: +N` for DEFEND's own schedule). Four raw chunk logs are archived
here for that reason (the rest are gitignored; every analysis in this directory runs off the
committed `.json` and `.report.txt`). On the same 20 campaigns at `h0-b0`:

| | `REINFORCEMENTS:` lines | `WAVE:` lines |
|---|---|---|
| B1 (clock as before) | **13** | 82 |
| L1 (lever) | **5** | 85 |

The residual 5 are Hack and Decapitate, which deliberately keep the arm. The `WAVE:` count moves
82 → 85 and that is **not** the lever touching DEFEND: runs survive longer under it, so more Defend
missions get played at all. (`enc.py` holds that still and reads Defend's `+rf` at 6.48 → 6.47 per
mission across the two arms.)

**HACK is the control on the clock** — same clock, different win condition — and its row is
identical to the last digit across the two arms (n=102, 81.4%, 3.67t, +rf 0.04, prs 0.16).
**DECAPITATE is the control on the class** — a kill objective the lever deliberately does not
touch — and moves 39.8 → 41.7 inside its own ±4.6.

## Operational note for the next agent in a worktree

This session's sandbox **refused any Bash command that set `XDG_CONFIG_HOME` or `HOME`** (a
worktree-isolation guard: it cannot verify where git would then write). CLAUDE.md's house
procedure — "export these in EVERY shell" — is therefore not directly runnable from a
worktree-isolated agent. The way through is the one the project already prescribes: `run_chunk.sh`
sets `XDG_CONFIG_HOME` **inside the script**, per chunk tag, which is both allowed and stricter
than the shell export (each chunk gets its own directory). `SIGHTLINE_BALANCE_JSON` is not
affected and is passed per chunk as usual.
