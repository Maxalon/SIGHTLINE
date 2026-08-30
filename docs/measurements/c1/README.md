# C1 — THE FLAT MIDDLE (PROGRAM CONTOUR, wave 1)

**Base commit `17934ee`** (PROGRAM CROSSCUT composed, the tree the L3 ladder of record was
measured on). **416 chunks, every one asserted `runs=40`, zero `BAD` — 16,640 campaigns.**
Instrument: `SIGHTLINE_BALANCE`, Release snapshots under `xvfb-run`, greedy+sloppy per slot.

**Shipped: `Heat.MidTooth = 3`** — NO QUARTER's +1 per-hit damage **and** coordination tier 2 both
move down to EXPOSED (rung 6). C1 first shipped mode 1 (damage only), was sent back, and changed
its mind on the review's evidence; the reasoning is in [the decision](#the-decision-mode-3-not-mode-1)
and in `Heat`'s own comment block.

## What is in this directory

| file | what |
|---|---|
| `chunks.csv` | **the archive.** One row per campaign (16,640): prefix, heat, slot-set base, slot, policy, win, missions cleared. Every win-rate table below re-derives from it. |
| `*-chunks.txt` | the runner's own `OK <tag> runs=40` line for all 416 chunks — the completion assertions, kept verbatim. |
| `C1-LADDER.txt` / `-fromcsv.txt` | the mode-1 round's ladder, derived from the raw JSONs and re-derived from `chunks.csv`. Kept as the send-back's provenance. |
| `sample/` | four full chunk JSONs + two BALANCE REPORTs, kept so the aggregate schema survives. |
| `run_chunk.sh` `run_ladder.sh` `run_extra.sh` | the runners. |
| `ladder.py` `inert_diff.py` `distil.py` | the analysis. |

**The raw round was 34 MB (17 MB JSON / 10 MB log / 6.6 MB report) and is NOT committed.**
`distil.py` reduced it to a 428 KB CSV keeping every per-campaign outcome — the whole basis of a
CRN-paired comparison — so **every win-rate number published by this wave is re-derivable.**

**What is NOT re-derivable, stated plainly:** the per-chunk AGGREGATE blocks (`actionMix`,
`byArena`, `decisionRichness`, `perkPicks`, …) are gone. So:

* `ladder.py --csv` reproduces every win%, ±SE, n, step and paired delta **exactly**. Two columns
  it cannot: `ch/ARM`, which prints **0.000** in `--csv` mode (blanked, never faked), and `avgMis`
  at h3, which reads 3.78 against the JSON path's 3.79 — the CSV sums exact per-run integers where
  the JSON path re-weights a figure each chunk had already rounded to 1 dp, so the CSV is the more
  accurate of the two.
* **`inert_diff.py` CANNOT be re-run from this archive.** It needs the raw chunk JSONs. Run today
  it exits non-zero with `NO CHUNKS` — it used to print `0 chunk pairs … 0 DIFFER <- INERT`, a
  green inertness verdict on nothing at all, which is the fail-open shape this wave exists to hunt,
  sitting in this wave's own tooling. Its results below are **recorded, not reproducible**;
  regenerate the chunks with `run_ladder.sh` first if you need to re-run it.

## Method

```
# every chunk (a is rm -f, b is exit-code 2, c is the runs assertion — W1's contract, all three)
bash docs/measurements/c1/run_chunk.sh <tag> <heat> <base> 20

# one whole ladder = 5-10 rungs x 8 disjoint CRN slot sets, four chunks at a time
PREFIX=C1ctl BIN=runbin/C1base                              bash .../run_ladder.sh 20 -1 0 1 2 3 4 5 6 7 8
PREFIX=C1m3  BIN=runbin/C1lev EXTRA="SIGHTLINE_MIDTOOTH=3"  bash .../run_ladder.sh 20 4 6 7 8
# eight MORE disjoint slot sets on one rung (n=320 -> n=640), for the shipping decision
PREFIX=C1m3  BIN=runbin/C1lev EXTRA="SIGHTLINE_MIDTOOTH=3"  bash .../run_extra.sh 6

python3 docs/measurements/c1/ladder.py --csv C1ctl C1m3 --heats=4,6,7,8
python3 docs/measurements/c1/inert_diff.py C1ctl C1r0 0 4 5 6 8   # NEEDS THE RAW JSONs — see above
```

`run_chunk.sh` is a port of `docs/measurements/l3/run_chunk.sh` with **one substantive change**:
`ROOT` derives from the script's own location instead of `cd /home/user/SIGHTLINE`. The L3 runner
hard-codes the main checkout, so an agent running it from a worktree would have silently measured
**the wrong tree**. Everything else — the three layers of W1's completion contract, in order — is
unchanged. (It is also, as the review found, a file that a stale bare `run_chunk.sh` line in
`.gitignore` swallowed on the first commit, leaving both committed runners inoperable; that rule is
now narrowed with explicit negations for `docs/measurements/**`.)

At `N=20`, slot sets must step by 20 to stay disjoint (`slotBase + i`, i in 0..N-1), so the bases
are 0/20/…/140 for n=320 and 160…300 for the second n=320 on top.

## THE CONTROL — the first PER-RUNG ladder in the project's history

n=320 per rung (h6: 640), eight disjoint CRN slot sets, base `17934ee`.

| rung | RECRUIT | h0 | h1 | h2 | h3 | h4 | h5 | h6 | h7 | h8 |
|---|---|---|---|---|---|---|---|---|---|---|
| **win%** | 70.00 | 44.38 | 37.50 | 32.50 | 27.50 | 20.94 | 20.94 | 18.59 | 12.19 | 8.12 |
| ±SE | 2.6 | 2.8 | 2.7 | 2.6 | 2.5 | 2.3 | 2.3 | 1.5 | 1.8 | 1.5 |
| **what rung N buys** | — | 25.62 | **6.88** | **5.00** | **5.00** | **6.56** | **0.00** | **2.34** | **6.41** | **4.06** |

Every published ladder before this one sampled `{RECRUIT, 0, 2, 4, 6, 8}` and reported `h4 → h6` as
"the flat step". At per-rung resolution the flat step **is two rungs, and one of them is exactly
zero**: rung 5 (LINGERING WOUNDS) buys **0.00 ±3.2**, rung 6 (EXPOSED) buys **2.34 ±2.7**, against
a ladder whose other six rungs average 5.7.

**A reviewer restricted this control to L3's exact 160 campaigns and reproduced L3's published
ladder at all six rungs** (71.25 / 47.50 / 31.25 / 23.75 / 20.00 / 6.88). That is stronger
corroboration than either wave presented alone: it proves L3 measured the tree it said it did, and
that the CRN chain is intact across two programs.

## THE MECHANISM — a dead declaration, live for two programs

`Heat.Mods`' rung 6 read `Exposed = true, StatDelta = 1, AiTier = 1`. `Heat.AiTier` aggregates with
`Math.Max`, and **rung 4 (ELITE CADRE) already publishes tier 1** — so EXPOSED's advertised
coordination tooth **could never fire**. Nothing in the repo said so: `HEATLADDERTEST` pins only
the CUMULATIVE vector, which a dead declaration by definition does not move.

## THE CANDIDATES — five levers, all provably apex-neutral, priced at heat 6

`Heat.MidTooth` is a bitfield (1 = the +1 per-hit damage moves rung 8 → 6, 2 = coordination tier 2
moves 8 → 6, 4 = rung 6's stat point moves 6 → 7). **DmgDelta sums and AiTier is a Math.Max, so the
cumulative vector at heat 8 is identical in every mode** — the apex cannot be pushed under its ≥5
hard floor by this dial. A reviewer corroborated it from the committed samples: the h8 chunk pair
diffs **1535 leaf fields, 0 differing**, while the h6 pair diffs **1608 fields, 984 differing** —
the h6 difference is what makes the h8 zero mean something.

| mode | what moves | n | h6 ctl → lev | paired Δ | ±SE | discordant (lev-only / ctl-only) | z |
|---|---|---|---|---|---|---|---|
| **3** | **+1 dmg AND tier 2 → rung 6** | **640** | **18.59 → 13.91** | **−4.69** | **1.73** | 47 / 77 | **−2.71** |
| 1 | +1 dmg → rung 6 | 640 | 18.59 → 12.03 | −6.56 | 1.60 | 30 / 72 | −4.16 |
| 2 | tier 2 → rung 6 alone | 320 | 18.44 → 20.00 | **+1.56** | 1.95 | 22 / 17 | +0.80 |
| 4 | rung 6's stat point → rung 7 | 320 | 18.44 → 23.13 | +4.69 | 2.68 | 44 / 29 | +1.75 |
| 5 | mode 1 + mode 4 | 320 | 18.44 → 10.63 | −7.81 | 2.52 | 20 / 45 | −3.10 |

Mode 4 is a useful by-product: it **prices rung 6's anonymous stat point at 4.69 points**, the
first time a single `StatDelta` on this ladder has been measured in isolation.

### Coordination tier 2 is indistinguishable from zero — with the wrong sign

The first draft of this wave wrote "tier 2 is **not** a difficulty lever" on the strength of one
cell: `+1.56 ±1.95, z = +0.80`, **95% CI [−2.26, +5.38]** — which does not exclude tier 2 buying
2.3 points, *more* than rung 6 was buying at all. That was an over-claim and it is withdrawn.

The archive contains **four contrasts that isolate the same component**, and pooling them
inverse-variance is the right reading:

| contrast | n | lev-only | ctl-only | Δ | ±SE |
|---|---|---|---|---|---|
| m2 vs ctl, h6 | 320 | 22 | 17 | +1.56 | 1.95 |
| m2 vs ctl, h7 | 320 | 13 | 10 | +0.94 | 1.50 |
| m3 vs m1, h6 | 640 | 30 | 18 | +1.88 | 1.08 |
| m3 vs m1, h7 | 320 | 6 | 5 | +0.31 | 1.04 |
| **pooled (1600 CRN pairs)** | | | | **+1.09** | **0.63** |

z = +1.73, **95% CI [−0.15, +2.33]**, and **wrong-signed in all four cells**. The honest statement
is: *moving coordination tier 2 down a rung does not measurably increase difficulty, and its point
estimate is slightly in the player's favour.* Caveat, per the program's own rule 3: **a CRN round
prices CONSEQUENCES and is structurally blind to FEEL.** Tier 2 may well change how a fight reads
without changing who wins it — nobody has looked.

## THE DECISION: mode 3, not mode 1

C1 shipped mode 1 first. The review recomputed the SHAPE from this same archive and mode 1 **lost
on every dispersion metric**. Rung 1–8 step profiles:

```
             h1    h2    h3    h4    h5    h6    h7    h8    sum
control     6.88  5.00  5.00  6.56  0.00  2.34  6.41  4.06  36.25
mode 1      6.88  5.00  5.00  6.56  0.31  8.59  2.66  1.25  36.25
mode 3      6.88  5.00  5.00  6.56  0.31  6.72  4.22  1.56  36.25
```

| metric | control | mode 1 | **mode 3 (shipped)** |
|---|---|---|---|
| SD of the 8 steps (sample, n−1) | 2.36 | 2.89 | **2.44** |
| L1 deviation from even spacing | 14.38 | 18.75 | **15.00** |
| L1 deviation from the band's implied profile | 14.69 | 19.06 | **15.31** |
| L1 from even, inside the rungs 5–8 window | 8.12 | 10.78 | **9.06** |
| steps < 2.0 points | 1 | 2 | 2 |
| **sum of rungs 1–8** | **36.25** | **36.25** | **36.25** |

The sums are equal **to the decimal**, which confirms apex-neutrality — and is exactly what makes
the allocation a **design choice rather than arithmetic**. The ranking control > mode 3 > mode 1 is
identical on all four dispersion metrics.

**Four reasons for mode 3 over mode 1:**

1. **It wins every shape metric mode 1 loses**, and shape is what this wave is for.
2. **The band verdict.** Mode 1's h6 is **12.03 ±1.29** against a floor of 12 — P(true value below
   the floor) ≈ **0.49**, a coin flip. Mode 3's is **13.91 ±1.37**, 1.4 SE clear of the floor
   (P ≈ 0.08), and in band at **both** n=320 (13.4) and n=640 (13.9).
3. **Mode 1's band verdict was reached by optional stopping.** The first n=320 round read 11.2 —
   out of band — and the round was extended to n=640, read 12.0, and shipped. C1 disclosed that,
   but disclosure makes optional stopping *auditable*, not unbiased. Mode 3 never needed the rule.
4. **The tension, resolved.** Mode 1's only advantage was leaving a qualitative tooth on the apex —
   and that tooth is `AiTier 2`, the one component this wave measured as doing nothing. **C1 cannot
   call tier 2 "not a difficulty lever" and simultaneously pay four shape metrics and a band
   verdict to keep it at the apex.** Mode 3 is also weakly **dominant** across that component's
   full CI: at the bottom (−0.15) mode 3 ≈ mode 1; at the top (+2.33) it is clearly better on band
   and shape; it is never worse.

**The counter-argument for mode 1, which C1 never actually made and the review supplied:** with h4
measured at 20.94 against a band asking 30, the band's implied `h4 → h6` step of −10 puts h6 at
**10.9 relative to measured h4**. On that shape-*relative* reading mode 1's 12.03 is better
positioned than mode 3's 13.91, and the control's 18.59 is nowhere near either. It is a real
argument, and it is **not self-consistent on this tree**: the same −10-per-two-rungs slope applied
to `h6 → h8` demands h8 = 0.9, against a measured 8.12 and a hard floor of 5. The shape-relative
target can only be honoured for one step at a time here, and honouring it at `h4 → h6` while
ignoring `h6 → h8` is precisely the cherry-pick that produced mode 1's flat top.

**What mode 3 costs, recorded rather than hidden:** NO QUARTER becomes a quantitative row —
`+1 enemy; +1 stat; the ceiling`. The apex is a wall because of the **stack** beneath it, which the
panel lists in full at heat 8, not because of its own row. That is a legibility cost, paid
deliberately.

## THE SHIPPED LADDER

| rung | RECRUIT | h0 | h1 | h2 | h3 | h4 | h5 | h6 | h7 | h8 |
|---|---|---|---|---|---|---|---|---|---|---|
| control | 70.00 | 44.38 | 37.50 | 32.50 | 27.50 | 20.94 | 20.94 | 18.59 | 12.19 | 8.12 |
| **C1 (mode 3)** | 70.00\* | 44.38\* | 37.50\* | 32.50\* | 27.50\* | **20.94** | 20.62† | **13.91** | **9.69** | **8.12** |
| ±SE | 2.6 | 2.8 | 2.7 | 2.6 | 2.5 | 2.3 | 2.3 | 1.4 | 1.6 | 1.5 |
| paired Δ vs control | — | — | — | — | — | **+0.00** | — | **−4.69** | **−2.50** | **+0.00** |
| discordant pairs | — | — | — | — | — | **0/320** | — | 124/640 | 38/320 | **0/320** |

\* **Not measured under mode 3.** Rungs 1–5 are byte-identical in every dial mode, so heats ≤5 are
unchanged *by construction* at mission-setup level; measured directly for mode 1, they came back
exactly the control at RECRUIT/h0/h1/h2/h3/h4 — **0/320 discordant at each of six rungs**.

† **Substituted from mode 1's measured h5** (20.62). The only path by which the dial can reach a
heat-5 campaign is the AddHeat escalation below, so mode 3's h5 lies within ~0.3 of this. Using the
control's 20.94 instead moves mode 3's rung-6 step from 6.72 to 7.03 and changes no conclusion.
The wave was instructed not to re-measure; this cell is flagged rather than quietly filled.

### Band compliance

Band: RECRUIT 75 / h0 55 / h2 40 / h4 30 / h6 20 / h8 10, ±8 (h8 ±5, hard floor ≥5).

| rung | control | C1 | band | verdict |
|---|---|---|---|---|
| RECRUIT | 70.0 | 70.0 | 67–83 | in (unchanged) |
| h0 | 44.4 | 44.4 | 47–63 | **below by 2.6** — pre-existing, untouched |
| h2 | 32.5 | 32.5 | 32–48 | in, at the floor (unchanged) |
| h4 | 20.9 | 20.9 | 22–38 | **below by 1.1** — pre-existing, untouched |
| h6 | 18.6 | **13.9** | 12–28 | **in, 1.4 SE clear of the floor** |
| h8 | 8.1 | 8.1 | 5–15 | in (unchanged) |

**Do not read this as "nothing that was in band left it"** — that framing is too easy on the
result. h6 moves from 1.4 points above the band CENTRE to **6.1 below it**. What that says is that
the LEVEL at h0 and h4 is the broken half, which is C1's own closing diagnosis.

## THE HONEST PART — the flat spot moved, it did not vanish

Rung 8 now buys **1.56** where it bought 4.06, and rung 5 **still buys 0.31 ±3.2 — indistinguishable
from zero** (the review's paired convention gives ±2.85; either way, nothing). **C1 fixed the
second-flattest rung and left the flattest.** The near-dead region is now {5, 8} where it was {5},
and `Run.cs` still calls the apex "a genuine wall, beatable only by excellent play" — which is now
carried by the cumulative stack, not by rung 8's own step.

That is **arithmetic, not a design failure, and it is this wave's real finding:** the lever is
apex-neutral by construction, so h4 (20.94) and h8 (8.12) are both pinned, leaving **12.8 points of
win-rate for four rungs — 3.2 each.** No redistribution inside that window can give rungs 5–8 the
~5.7 points per rung that rungs 1–4 buy. What it can do, and did, is stop one rung taking almost
none of it while keeping the allocation as even as the pinned endpoints allow.

**The deficit that actually causes this sits ABOVE heat 4.** Against band centres, h0 is **10.6**
points low and h4 is **9.1** low. The ladder is flat at the bottom because its top half has sunk
onto it. That is a BASE-difficulty lever, not a `Heat.Mods` lever, and it was out of C1's scope.
**It is the next thing to price.**

## Undeclared effects, now declared

**The damage tooth is not purely numeric.** `Mission.cs:890-902` says so in its own comment: +1
`DmgMax` **widens the AI finish band** (`Ai.Plan` scores a kill on `p.Hp <= e.Weapon.DmgMax`), so
moving it down two rungs moves a **coordination sharpening** down two rungs as well. The CRN round
prices it; the write-up had not named it. It cuts *toward* this wave's thesis — rung 6 partly
re-earns, through the finish band, the coordination identity its dead `AiTier = 1` never delivered.

**A watch item the same comment names and C1 did not report:** the wider finish band leans
**against the BRACE comeback lever**. In the committed h6 sample pair, lead-swings/match holds at
0.9, but average max-swing goes 59.6 → 55.8 and greedy BRACE usage **146 → 96 (−34%)**. n=40, not
conclusive — and the code told us to look.

**SKIRMISH is changed at heat 6–7 and was NOT measured there.** `Game.cs` gates the m1–2 heat grace
on `Mode != GameMode.Skirmish` (W9), so a heat-6 skirmish now takes the +1 enemy damage **from turn
one**, where before only heat 8 did. `SIGHTLINE_BALANCE` measures campaigns only. DAILY is safe
(`DailyHeat` is `% 4u`, capped at 3) and ENDLESS is safe (`EndlessWaveScale` reads `StatDelta`,
never `DmgDelta`).

## THE AddHeat LEAK IS DIRECTIONAL — and it points at this wave's own headline

`Events.cs` (`EventOutcomeKind.AddHeat`) lets three field-event choices raise a run's HeatLevel
mid-campaign, +1 each, so a heat-N cell contains some heat-N+1 missions. It is the only reason the
lever is not perfectly inert below rung 6: h5 shows 5 discordant pairs in 320 (−0.3, z = −0.45) and
h4 shows zero, because from h4 the escalation must fire twice and in 320 campaigns it never did.

**It is one-directional.** Heat 8 is clamped and cannot leak upward; every rung below 8 is
contaminated **toward the rung above it**, so **the instrument systematically compresses the top of
the ladder it is being used to diagnose.** Some part of the flat middle/top this wave exists to fix
may be the instrument, not the design. Quantified from this archive: h5's 1.56% flip rate under a
lever that can only bite at heat ≥ 6 implies **≈10% of h5 campaigns reach heat ≥ 6**. The
campaign-level inertness claims all survive it (the six 0/320 rungs are exact), but every past
"this rung is unaffected" statement about this ladder has had this hole in it.

## Provenance of the inertness claims (recorded, not reproducible — see above)

* **R0diag**: `SIGHTLINE_MIDTOOTH=0` on the C1 tree vs the pre-C1 binary — 32 chunk pairs at
  h0/h4/h6/h8, **49,961 leaf fields, 0 differing**; h5 added later when the AddHeat leak turned up
  (8 more pairs, 12,454 fields, also 0).
* **Final tree, dial off, vs the pre-C1 binary**: 37,445 fields, 0 differing.
* **Final tree at its default vs the measured lever**: 24,538 fields, 0 differing — proving the
  copy fixes, the new self-test and the AITEST re-pin are gameplay-inert rather than assumed to be.

Those three rounds ran against the mode-1 default. The mode-3 switch is a `Heat.Mods` data change
measured directly (the tables above), not an inertness claim.
