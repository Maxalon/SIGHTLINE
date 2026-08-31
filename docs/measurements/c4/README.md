# C4 — "EIGHT BIOMES ARE PAINT": the CRN-paired price of the biome ground layer

**Base commit `17934ee`** (PROGRAM CROSSCUT composed, the tree the L3 ladder of record was
measured on), branch `wave/biome-mechanical`.

**The lever, and only the lever:** `SIGHTLINE_BIOMEMECH`.
`=1` (arm **A**, shipped) turns on the biome GROUND layer — VERDANT undergrowth, TUNDRA slick
ice, MAGMA thermal vents. `=0` (arm **B**) sets `Terrain.Enabled = false`, which gates the
stamper *and* every `Grid` predicate, restoring the pre-C4 board exactly. **Both arms are the
same binary, from the same snapshot, on the same slot sets.**

**Design:** 3 rungs (heat 0 / 2 / 4) × 2 arms × **8 disjoint CRN slot sets**
(`SIGHTLINE_BALANCE_BASE` 0-70) × greedy+sloppy = **160 campaigns per (rung, arm)**, 960
campaigns per round. Every one of the 48 chunks per round printed `OK ... runs=20`; **zero
`BAD`**. Runner: `run_chunk.sh` (W1's three-layer completion contract — `rm -f` the target
first, check the exit code, assert the JSON's own `runs`), driven by `run_round.sh`.

```
bash docs/measurements/c4/run_round.sh      # uninstrumented binary  -> C4-*    (the ladder round)
bash docs/measurements/c4/run_round_i.sh    # instrumented binary    -> C4i-*   (the per-biome split)
python3 docs/measurements/c4/aggregate.py         # the paired ladder table
python3 docs/measurements/c4/aggregate_biome.py   # the per-biome cross-tab
python3 docs/measurements/c4/inert_diff.py A.json B.json   # the R0diag
```

## 1. THE LADDER — what the mechanic costs, declared

| rung | A: mechanic ON | B: pre-C4 board | Δ | chunk-paired SE | chunk-paired t | L3 of record | floor |
|---|---|---|---|---|---|---|---|
| heat 0 | **43.8%** | 47.5% | **−3.7** | 3.50 | **−1.07** | 47.5 | 47 |
| heat 2 | **30.0%** | 31.2% | **−1.3** | 6.66 | −0.19 | 31.2 | 32 |
| heat 4 | **25.6%** | 23.8% | **+1.9** | 3.65 | +0.51 | 23.8 | 22 |
| pooled | — | — | −1.04 | 2.71 | −0.38 | — | — |

**Read the t column, not a verdict column.** The first version of this table carried
"A is 3.2 BELOW the floor" in bold. That is a POINT ESTIMATE crossing a threshold, not a measured
breach: chunk-paired (the two arms of a chunk ran the same slot set, so the chunk difference is the
paired observation, and 8 chunks per rung is the honest n) heat 0 reads **t = −1.07**. The README's
own "the round could not resolve it at n=160" was the correct reading all along; the bolded verdict
was not. Reproduce with `paired_t.py`.

**Arm B reproduces the L3 ladder of record to the decimal on all three rungs** (47.5 / 31.2 /
23.8). That is the control this wave gets for free and it is worth stating plainly: with the
flag off, this branch is the same game as `17934ee`, so every point of difference in column A is
the ground layer and nothing else.

**Not one rung is resolved.** The ladder stays monotone (43.8 > 30.0 > 25.6). Heat 0 was sitting
*exactly* on its band floor before this wave (47.5 vs a floor of 47), so the −3.7 point estimate
puts it under — but at t = −1.07 the round cannot tell that move from zero. What it CAN tell is
where the cost sits: see §1b.

> ### ⚠ THIS ROUND NO LONGER DESCRIBES WHAT SHIPS.
> The C4 review pass changed the layer after this round was taken: mechanical ground is no longer
> stamped on RAISED terrain (it rendered as zero pixels under the opaque plateau top), and density
> was re-tuned against real boards (VERDANT 12.1% → 17.7%, MAGMA mean 9.7 → 13.0 tiles, TUNDRA
> 17.2 → 18.3). Every number here is a PRE-FIX number. It remains valid as the price of the layer
> as measured, and as the proof that arm B is `17934ee`; it is **not** the price of the shipped
> tree. Re-measure before quoting it as such.

## 1b. WHERE THE COST IS — the opening mission (`aggregate_nodekind.py`)

`byNodeKind` was in every chunk of this archive from the start (W1 put it there) and the wave
never published it. `Start` is mission 1 — exactly ONE per campaign, so it carries no
within-campaign clustering, which is why it resolves when the pooled rung does not. Chunk-paired
over all 24 chunks, MISSION win rate:

| node kind | A | B | paired Δ | SE | **t** | n/arm |
|---|---|---|---|---|---|---|
| **Start** (mission 1) | **91.7%** | **96.0%** | **−4.37** | **0.97** | **−4.53** | 480 |
| Combat | 78.2% | 77.5% | +1.13 | 1.89 | +0.60 | ~490 |
| Elite | 72.3% | 77.0% | −3.98 | 3.43 | −1.16 | ~240 |
| Supply | 89.2% | 89.0% | −0.27 | 1.55 | −0.17 | ~280 |
| Boss | 66.5% | 64.3% | +4.10 | 4.31 | +0.95 | ~245 |

At heat 0 alone: `Start` −4.37, SE 1.75, **t = −2.50** (8 chunks). `byObjective` agrees —
**Eliminate −3.55, SE 1.12, t = −3.16**, and Eliminate is mission 1's objective in the baseline
rotation.

> **THE GROUND LAYER'S COST IS CONCENTRATED ON THE OPENING MISSION.** That is the front-loaded
> anxiety `docs/DESIGN.md` §3.D forbids, and it is the exact failure X2's `Mission.OpenerTrim`
> exists to prevent. It also points at a cheaper, more targeted lever than any of the three the
> wave named: **suppress or thin the ground layer on mission 1.** Unpriced.

## 2. THE CROSS-TAB — which room bought it (CROSSCUT rule 1)

The pooled campaign rate mixes two populations that are no longer the same game: three biomes
with a mechanic and five that are still paint. `Stats` gained a read-only `byBiome` block for
exactly this. Pooled across all three rungs, **mission** win rate:

| biome | | A n | A win | B n | B win | Δ | ±SE(Δ) |
|---|---|---|---|---|---|---|---|
| ARID | | 227 | 79.7% | 244 | 78.3% | +1.5 | 3.8 |
| ASH | | 217 | 80.6% | 236 | 81.4% | −0.7 | 3.7 |
| **MAGMA** | ★ | 219 | **80.4%** | 220 | 84.5% | **−4.2** | 3.6 |
| NEON | | 205 | 82.9% | 210 | 82.4% | +0.5 | 3.7 |
| STEEL | | 213 | 84.0% | 223 | 83.9% | +0.2 | 3.5 |
| **TUNDRA** | ★ | 218 | **81.2%** | 223 | 84.8% | **−3.6** | 3.6 |
| **VERDANT** | ★ | 212 | **83.5%** | 226 | 85.4% | **−1.9** | 3.5 |
| VOID | | 208 | 78.4% | 211 | 78.7% | −0.3 | 4.0 |
| **MECHANICAL (3)** | ★ | 649 | **81.7%** | 669 | **84.9%** | **−3.2** | **2.1** |
| **PAINT (5, control)** | | 1070 | 81.1% | 1124 | 80.9% | **+0.3** | 1.7 |

**The direction: the mechanical boards moved down, the paint control did not.** Mechanical −3.2
± 2.1 against paint +0.3 ± 1.7. **This is a direction, not a result.** Chunk-clustered, the
difference-in-differences is **−3.19 ± 1.98, t = −1.61**, sign test **17/24 chunks negative** —
not resolved at n=160. And "all three point the same way" is a POOLING ARTIFACT: per rung, only
**MAGMA** is consistently negative (−2.7 / −4.8 / −5.2); TUNDRA flips positive at h2 (+1.9) and
VERDANT at h4 (+5.7), and individually TUNDRA (−3.6 ± 3.6) and VERDANT (−1.9 ± 3.5) are
indistinguishable from zero. The honest sentence is: *the three mechanical boards moved −3.2 ±
2.1, direction consistent, MAGMA the only biome consistent across rungs, not resolved at n=160.*

*The mechanism the wave hypothesised is REFUTED in this same archive.* It proposed that each rule
makes the exchange harder to resolve and that a longer exchange favours the side with more bodies.
Per-biome `avgTurns` from the instrumented A arm: MAGMA 6.36, TUNDRA 6.18, VERDANT 5.58 — against
paint biomes ranging 5.36 (STEEL) to **6.47 (VOID)**. The longest-fight biome in the batch has no
mechanic at all. Whatever is happening, it is not that.

*Caveat on the control:* the paint rows are not a clean control, because a run mauled on a MAGMA
mission arrives at the next (paint) mission weaker. They read flat anyway.

## 3. THE R0diag — the added telemetry is gameplay-inert

`Stats.MissionRec` gained two fields and the report gained a `byBiome` block. The measurement
contract requires proving that instrumentation did not move logic before quoting a round on it.
Same chunk, same slots, uninstrumented binary vs instrumented, both arms:

```
inert_diff.py C4-B-h0-b0.json C4diag-B-h0-b0.json  -> fields compared: 1304  differing: 0  INERT
inert_diff.py C4-A-h0-b0.json C4diag-A-h0-b0.json  -> fields compared: 1256  differing: 0  INERT
```

`harness{}` (nproc/loadavg/elapsed, designed to vary) and `byBiome` (present in only one side by
construction) are the only excluded blocks — the same exclusion W1's `inert_diff.sh` makes, for
the same reason. The full instrumented round then reproduced the uninstrumented round's pooled
rates exactly (43.8 / 30.0 / 25.6 and 47.5 / 31.2 / 23.8).

## 4. Secondary columns, heat 0 (A vs B, n=160/arm)

| | A | B | Δ |
|---|---|---|---|
| avgMissionsCleared | 4.244 | 4.412 | −0.169 |
| meaningful-choices/turn | 3.502 | 3.581 | −0.079 |
| choices/ARMED-soldier-turn | 2.269 | 2.314 | −0.045 |
| position-choices/ARMED | 1.804 | 1.831 | −0.027 |
| target-choices/ARMED | 0.466 | 0.483 | −0.018 |
| LoS-targets/ARMED | 2.771 | 2.694 | +0.077 |
| turns-with-a-shot % | 61.95 | 61.56 | +0.39 |
| lead-swings/match | 0.706 | 0.798 | −0.091 |

**Decision density did not move.** These are TRUE BAND instrument numbers (additive band, cap 4)
and are not comparable to any figure dated before 2026-08-29. W4's law predicted this: the wave
shipped a positioning lever *and* a threat lever at once (ice/undergrowth are positional, the
vent's burn is threat), and the two halves of `CountMeaningfulChoices` respond with opposite
signs. At heat 4 the columns move much more (`turnsWithAShotPct` 62.2 → 58.3,
`meaningfulChoicesPerTurn` 4.13 → 3.51) while the win rate moves the *other* way (+1.9); no
claim is made about either.

## Files

* `C4-{A,B}-h{0,2,4}-b{0..70}.json` — the ladder round (48 chunks, uninstrumented binary).
* `C4i-*` — the same design on the shipped, instrumented tree (48 chunks); the round of record
  for the per-biome table.
* `C4diag-{A,B}-h0-b0.json` — the R0diag pair.
* `run_chunk.sh` / `run_round.sh` / `run_round_i.sh` / `aggregate.py` / `aggregate_biome.py` /
  `aggregate_nodekind.py` / `paired_t.py` / `inert_diff.py` — the exact command lines, committed
  so the round is re-runnable. **`run_chunk.sh` was NOT committed on the first pass**: a bare
  `run_chunk.sh` pattern in `.gitignore` swallowed it, refuting this line, for the third
  consecutive wave. `.gitignore` now carries `!docs/measurements/**/run_chunk.sh` so an archive's
  runner can never be silently dropped again.
* Raw `.log` / `.report.txt` are NOT archived (they are a slice of the JSON and `*.log` is
  gitignored); every number above is derivable from the committed JSONs by the committed scripts.
