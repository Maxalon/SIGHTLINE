# WAVE X3 "CONFIRM" — archived measurement artifacts

The wave that raised N instead of spending a lever. Every chunk's JSON summary, its report
extract and the raw log, exactly as run. The write-up in
[`docs/DEVLOG.md` §RESONANCE X3](../../DEVLOG.md) quotes only numbers that appear here.

**Base commit of every number in this directory: `d350416`** (PROGRAM RESONANCE milestone 2 —
the orchestrator's integration tip) plus this wave's own **default-off** class-role scaffolding,
which is logic-identical at the shipped defaults. That identity is not asserted, it is
**measured**: `R0diag-h0-b0` re-runs the X3 binary with every knob at 0 over the same slots as
`R0-h0-b0`, and the two JSONs match on `runs`, `missions`, `runWinRate`, `avgMissionsCleared`,
`policyGap`, `decisionRichness`, `byObjective`, `byMission`, `byHeat`, `byArena`, `actionMix`,
`lossCauses` and `playerClasses` — every compared field, exactly.

## Why n=80

X2 published the ladder of record at n=40 and said plainly that its own instrument could not
resolve it: per-rung SE ±6-8, i.e. the band's whole ±8 tolerance and **larger than the 10-point
step between rungs**. This wave re-measures every rung at **n=80** — four disjoint CRN slot sets
(`SIGHTLINE_BALANCE_BASE` 0 / 10 / 20 / 30) x greedy+sloppy = 80 campaigns per rung — which
halves the variance and takes the SE to ±3.4-5.6.

**`runs=20` was asserted in every one of the 62 chunks below** (`run_chunk.sh` reads the JSON's
own `runs` field — it cannot be half-written — and prints OK/BAD; every chunk printed OK).

## How every chunk was run

```bash
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg/<tag>"; export XDG_CONFIG_HOME="$PWD/.xdg/<tag>"
env [SIGHTLINE_CLASSBAL=<n>] [SIGHTLINE_MEDICAURA=<n>] \
    SIGHTLINE_BALANCE=10 SIGHTLINE_BALANCE_HEAT=<-1|0|2|4|6|8> SIGHTLINE_BALANCE_BASE=<0|10|20|30> \
    SIGHTLINE_BALANCE_JSON=<out.json> \
  xvfb-run -a -s "-screen 0 1280x800x24" <bin>/Sightline > <out.log>
```

`xvfb-run` is mandatory: without a display `SIGHTLINE_BALANCE` prints `runs=0 / (no data)`, still
claims N matches and exits 139 — a silent zero-data batch that looks completed.
`XDG_CONFIG_HOME` and `SIGHTLINE_BALANCE_JSON` are pinned per chunk because several dev agents
share this container and the harness otherwise defaults to a shared `/tmp` path.
`SIGHTLINE_BALANCE_HEAT=-1` is the RECRUIT rung.

**Binaries.** Rounds run from *snapshots* of the Release build (`runbin/<tag>/Sightline`,
gitignored) so the tree can keep building while a round is in flight: `runbin/R0` = the
integration tip, `runbin/X3` = the C1 shape, `runbin/X3b` = the shipped C2 shape.

`drive.sh <queue> [P]` runs a queue of `<tag> <heat> <base>` lines with bounded parallelism
(P=4 here). `agg.py <tag-prefix>...` pools a rung's chunks: counts (runs, wins, per-objective
and per-mission n, shots, hits, damage, kills) pool EXACTLY; the decision-richness ratios pool
as the unweighted mean of the four equal-sized (20-run) chunks, because the harness JSON
publishes those as ratios rather than raw sums (the W4/X2 convention, kept so rows stay
comparable across waves). `agg.py` also prints the pooled PLAYER CLASS PERFORMANCE table — the
explicit gate for Part 2.

## Round index

| tag | state | rungs |
|---|---|---|
| `R0-h{R,0,2,4,6,8}-b{0,10,20,30}` | **the confirmed baseline** — the integration tip, no lever, n=80/rung | RECRUIT / h0 / h2 / h4 / h6 / h8 |
| `R0diag-h0-b0` | the X3 tree with every class knob OFF — the logic-identity check against `R0-h0-b0` (every compared field MATCHES exactly) | h0 |
| `C1-h0-b*` | class round 1 — `CLASSBAL=1` with GUNNER +1/+1 and ASSAULT 0/+1. **Not shipped**: it over-corrected, flipping the Gunner to best (2.64 s/kill) | h0 |
| `C2-h0-b*` | class round 2 — the reshaped composite (GUNNER +1 on the FLOOR, ASSAULT +1 on BOTH ends). The shipped shape | h0 |
| `C3-h0-b*` | the same four damage deltas with `MEDICAURA=0` — isolates what the CORPSMAN FIELD PRESENCE aura is actually worth inside the composite | h0 |
| `S1-h{R,0,2,4,6,8}-b{0,10,20,30}` | **the shipped state** — `CLASSBAL=1`, re-measured end to end at n=80/rung | RECRUIT / h0 / h2 / h4 / h6 / h8 |

`R0-LADDER.txt` and `S1-LADDER.txt` are the pooled tables as `agg.py` printed them. Every chunk's
raw `.log` is force-added alongside its `.json` and `.report.txt` (the repo `.gitignore` blocks
`*.log`, `*.py` and `run_chunk.sh` globally).
