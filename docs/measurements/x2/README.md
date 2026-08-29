# WAVE X2 "TRUE NORTH II" — archived measurement artifacts

The definitive **post-merge** ladder and the correction rounds that followed it. Every chunk's
JSON summary, its report extract and the raw log, as run. The write-up in
[`docs/DEVLOG.md` §RESONANCE X2](../../DEVLOG.md) quotes only numbers that appear here.

**Base commit of every number in this directory: `a61ef42`** (RESONANCE W4 THE SECOND AXIS —
the integration tip) plus this wave's own default-off measurement scaffolding, which is
logic-identical at the shipped defaults (verified by the `R0diag` round below). *This line is
the whole point of the wave: fourteen waves each published a ladder measured on the tree they
branched from, and the composition was never measured until now.*

**How every chunk was run** (`run_chunk.sh`, archived here — force-added, the repo `.gitignore`
blocks `run_chunk.sh` globally):

```bash
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg/<tag>"; export XDG_CONFIG_HOME="$PWD/.xdg/<tag>"
env [SIGHTLINE_AIMTRIM=<n>] [SIGHTLINE_TOUGH=<n>] [SIGHTLINE_TRIM=<n>] \
    [SIGHTLINE_ENEMYBASE=<n>] [SIGHTLINE_DEPLOYMIX=a,b,c,d] \
  SIGHTLINE_BALANCE=10 SIGHTLINE_BALANCE_HEAT=<h> SIGHTLINE_BALANCE_BASE=<0|10> \
  SIGHTLINE_BALANCE_JSON=<out.json> \
  xvfb-run -a -s "-screen 0 1280x800x24" <bin>/Sightline > <out.log>
```

`xvfb-run` is mandatory: without a display `SIGHTLINE_BALANCE` prints `runs=0 / (no data)`, still
claims N matches and exits 139 — a silent zero-data batch. `run_chunk.sh` asserts the JSON's own
`runs` field (it cannot be half-written) and prints OK/BAD; **every chunk quoted in the DEVLOG
printed OK with `runs=20`.**

Two disjoint CRN slot sets (`SIGHTLINE_BALANCE_BASE` 0 and 10) x greedy+sloppy = **40 campaigns
per rung**. `XDG_CONFIG_HOME` and `SIGHTLINE_BALANCE_JSON` are pinned per chunk because several
dev agents share the container and the harness otherwise defaults to a shared `/tmp` path.

**Binaries.** Rounds run from *snapshots* of the Release build (`runbin/<tag>/Sightline`,
gitignored) so the tree can keep building while a round is in flight.

`drive.sh <queue> [P]` runs a queue of `<tag> <heat> <base>` lines with bounded parallelism.
`agg.py <tag-prefix>...` pools the two chunks of a rung: counts (runs, wins, per-objective and
per-mission n, shots, kills) pool EXACTLY; the decision-richness ratios pool as the unweighted
mean of the two equal-sized (20-run) chunks, because the harness JSON publishes those as ratios
rather than raw sums (the W4 convention, kept so rows stay comparable across waves).

## Round index

| tag | state | rungs |
|---|---|---|
| `R0-h{R,0,2,4,6,8}-b{0,10}` | **the definitive post-merge baseline** — no lever, shipped W4 defaults | RECRUIT / h0 / h2 / h4 / h6 / h8 |
| `R0diag-h0-b0` | the X2 tree with every new knob OFF — the logic-identity check against `R0-h0-b0` (runs, missions, completion, `decisionRichness`, `byObjective`, `byMission`, `playerClasses` and the per-slot paired records all MATCH exactly) | h0 |
| `A1-h0-b*` | `SIGHTLINE_AIMTRIM=5` — flat −5 aim on every hostile | h0 |
| `A2-h0-b*` | `SIGHTLINE_AIMTRIM=10` — the double dose (**not shipped**: Eliminate 4.80t, Escort 13.66t) | h0 |
| `O1-h0-b*` | `SIGHTLINE_OPENERTRIM=1` — one body off the m1 / m2 force | h0 |
| `S1-h{R,0,2,4,6,8}-b{0,10}` | **the shipped state** — `OPENERTRIM=1`, measured end to end at every rung | RECRUIT / h0 / h2 / h4 / h6 / h8 |

`R0-LADDER.txt` and `R0-BYOBJECTIVE.txt` are the pooled baseline tables as `agg.py` printed
them; `S1-LADDER.txt` is the same for the shipped state.
