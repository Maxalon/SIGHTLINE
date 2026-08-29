# WAVE TRUE BAND — archived measurement artifacts

The re-specification of `Game.Autopilot.cs :: CountMeaningfulChoices` from a **multiplicative**
near-best window to an **additive** one, and the instrument-design probe that chose its
constants. Every chunk's JSON summary, its report extract and the raw log, as run. The write-up
in [`docs/DEVLOG.md` §TRUE BAND](../../DEVLOG.md) quotes only numbers that appear here.

**Base commit of every number in this directory: `d350416`** (PROGRAM RESONANCE milestone 2 —
the integration tip, X2's ladder already merged) plus this wave's own instrument change, which
is proven **gameplay-inert** (see `inertness` below).

**The measured binary vs the shipped tree.** `probe-h0` / `probe-h8` were run on `runbin/tb`,
a snapshot carrying the probe only (the pre-change rule, unmodified). Every `add-*` / `mult-*`
chunk was run on `runbin/tb2`, snapshotted right after the band change landed. `tb2` therefore
predates three later, **logic-identical** edits to the tree: the extraction of the band decision
into `Game.AdmitNearBest` (a literal move of `float cut = mult ? best*frac : best-band;` plus the
counting loop, byte-for-byte the same arithmetic on both axes), the five band constants changing
from `const` to `static readonly` (same values — done so `SIGHTLINE_BANDTEST` can pin them without
the compiler folding the comparison away), and comment-only changes. Nothing in `Game.Harness.cs`,
`Program.cs` or `scripts/` that landed after the snapshot is reachable from a balance batch.

**How that logic identity was actually established — and how the contract says it should have
been.** `CLAUDE.md`'s measurement contract asks for an **`R0diag` chunk**: run the newly
instrumented tree lever-off on a pinned slot set and diff the per-slot records BEFORE spending a
round. This wave did not do that. It established identity **post hoc** instead, three ways:
(1) the five paired `mult`/`add` diffs below, which show zero non-choice movement; (2) a
reviewer's independent cross-tree check against a fresh build of `main` — 529 fields compared,
**0 moved** — which is the R0diag the contract wanted, run by someone else and after the fact;
and (3) the `v2-*` chunks below, re-run on `runbin/tb3` (the post-review tree, carrying the
axis-(a) zero floor, the `AdmitNearBest` extraction, the strict `CHOICEBAND` parse and the probe's
2x2 accumulators) and diffed against the `add-*` chunks they replicate. Post-hoc verification that
comes back clean is still weaker than the pre-flight diagnostic the contract specifies, and it is
recorded that way rather than presented as equivalent.

> **THIS WAVE CHANGED THE INSTRUMENT.** Every `ch/ARMED`, `ch/turn`, `target-choices/ARMED` and
> `position-choices/ARMED` number archived anywhere else in this repo — X1, W4, X2, FUL-13 —
> was measured on the multiplicative instrument and is **not comparable** to anything measured
> after this wave. `SIGHTLINE_CHOICEBAND=mult` reproduces the old rule exactly if one ever has
> to be re-derived; `SIGHTLINE_BANDTEST` pins that reproduction against a literal transcription.
>
> Since the post-review pass this is **enforced in data, not prose**: every aggregate written from
> here on carries an `instrument` field (`"mult-v1"` / `"add-v2"`), `diff_chunks.py` refuses a
> cross-instrument diff unless given `--cross`, and a mistyped `SIGHTLINE_CHOICEBAND` makes the
> binary refuse to start (exit 2) rather than silently selecting the new rule. The chunks archived
> here that predate that field report `unknown` and diff normally — `probe-*`, `mult-*` and
> `add-*` are all pre-field; only the `v2-*` chunks carry a real tag.

## How every chunk was run

`run_chunk.sh` is archived here (force-added — the repo `.gitignore` blocks `run_chunk.sh`
globally). It is the X2 runner with two knobs added and `BIN` defaulted to this wave's snapshot.

```bash
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
BIN=runbin/tb2 PROBE=1 [BAND=mult] bash docs/measurements/tb/run_chunk.sh <tag> <heat> <base> 5
```

which expands to

```bash
mkdir -p "$PWD/.xdg/<tag>"; export XDG_CONFIG_HOME="$PWD/.xdg/<tag>"
env [SIGHTLINE_CHOICEBAND=mult] SIGHTLINE_BANDPROBE=1 \
    SIGHTLINE_BALANCE=5 SIGHTLINE_BALANCE_HEAT=<heat> SIGHTLINE_BALANCE_BASE=<base> \
    SIGHTLINE_BALANCE_JSON="$PWD/docs/measurements/tb/<tag>.json" \
    setsid xvfb-run -a -s "-screen 0 1280x800x24" runbin/tb2/Sightline
```

The runner asserts the JSON's own `runs` field and prints `OK`/`BAD`. **Every chunk here printed
`OK`** — `runs=10` for the ten `add-*`/`mult-*` chunks (`SIGHTLINE_BALANCE=5` x greedy+sloppy) and
`runs=6` for the two exploratory `probe-*` chunks, which were run at `SIGHTLINE_BALANCE=3` before
any code change. `runbin/tb2/` is a
snapshot of the Release binary so the tree could keep building while rounds were in flight; it is
gitignored.

**`SIGHTLINE_BALANCE_BASE` 50 and above throughout** — bases 0-39 were owned by another agent's
in-flight measurement when this wave ran.

## N, and what these numbers are for

`n=10` campaigns per chunk. **These are INSTRUMENT reads, not win-rate claims.** A run-completion
figure at n=10 carries roughly ±15 points and nothing in this directory should ever be quoted as a
ladder rung. What n=10 *is* enough for is the decision-density ratios: `add-h0` alone samples 414
armed soldier-turns and 16,159 candidate destination tiles, and the paired mult/add comparison is
exact (identical worlds, identical play — the diff below proves it), so the instrument delta
carries no sampling error at all.

## The chunks

| tag | band | heat | base | purpose |
|---|---|---|---|---|
| `probe-h0` | mult (pre-change tree) | 0 | 50 | step-1 distributions, before any code change (n=6) |
| `probe-h8` | mult (pre-change tree) | 8 | 60 | step-1 distributions, before any code change (n=6) |
| `mult-h0` / `add-h0` | mult / add | 0 | 50 | paired inertness + rung read |
| `mult-h4` / `add-h4` | mult / add | 4 | 60 | paired inertness |
| `mult-h8` / `add-h8` | mult / add | 8 | 70 | paired inertness |
| `mult-h4b` / `add-h4b` | mult / add | 4 | 50 | rung read on the SAME world set as h0 |
| `mult-h8b` / `add-h8b` | mult / add | 8 | 50 | rung read on the SAME world set as h0 |
| `v2-add-h0/h4/h8` | add | 0/4/8 | 50 | post-review re-run on `runbin/tb3`: replicates `add-h0`/`add-h4b`/`add-h8b` to prove the post-review source changes inert, and carries the probe's exact same-denominator 2x2 decomposition |

The `*-h4`/`*-h8` pairs were run on their own bases before it was noticed that a rung-to-rung
comparison needs a *common* slot set; the `b` chunks redo h4 and h8 on base 50 so the three-rung
read is CRN-paired across heat. Both sets are kept — the off-base pairs are independent
confirmations of inertness on two more world sets.

## The inertness proof

```bash
python3 docs/measurements/tb/diff_chunks.py <mult>.json <add>.json
```

flattens both aggregates to dotted scalar paths and reports every field that moved.
`CountMeaningfulChoices` is read-only bookkeeping, so **only the decision-richness fields may
move**. Result, on **all five** mult/add pairs:

| pair | fields compared | choice fields moved | non-choice fields moved |
|---|---|---|---|
| heat 0, base 50 | 686 | 10 | **0** |
| heat 4, base 50 | 662 | 12 | **0** |
| heat 8, base 50 | 600 | 10 | **0** |
| heat 4, base 60 | 641 | 10 | **0** |
| heat 8, base 70 | 642 | 12 | **0** |

The totals differ per pair because `byDeploy`/`byObjective`/`byArena` are variable-length arrays —
a shorter batch flattens to fewer dotted paths. Run completion, missions, `byObjective`,
`byMission`, `byHeat`, `lossCauses`, `actionMix`, `armedSoldiersPerTurn`,
`losTargetsPerArmedSoldierTurn` and the per-slot paired records are byte-identical in every pair.

`diff_chunks.py` also REFUSES a diff whose two aggregates carry different `instrument` tags
(`--cross` overrides, loudly). Chunks archived before that field existed report `unknown` and
still diff normally.

`table.py` prints the summary table used in the DEVLOG; `deltas.py` prints the paired
mult -> add lift per rung and each instrument's spread across the three rungs.
