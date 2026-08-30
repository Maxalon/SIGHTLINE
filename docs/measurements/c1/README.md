# C1 — THE FLAT MIDDLE (PROGRAM CONTOUR, wave 1)

**Base commit `17934ee`** (PROGRAM CROSSCUT composed, the tree the L3 ladder of record was
measured on). **416 chunks, every one asserted `runs=40`, zero `BAD` — 16,640 campaigns.**
Instrument: `SIGHTLINE_BALANCE`, Release snapshots under `xvfb-run`, greedy+sloppy per slot.

## What is in this directory

| file | what |
|---|---|
| `chunks.csv` | **the archive.** One row per campaign (16,640): prefix, heat, slot-set base, slot, policy, win, missions cleared. Every table below re-derives from it. |
| `*-chunks.txt` | the runner's own `OK <tag> runs=40` line for all 416 chunks — the completion assertions, kept verbatim. |
| `C1-LADDER.txt` / `-fromcsv.txt` | the published before/after ladder, derived from the raw JSONs and re-derived from `chunks.csv`. |
| `sample/` | four full chunk JSONs + two BALANCE REPORTs, kept so the aggregate schema survives. |
| `run_chunk.sh` `run_ladder.sh` `run_extra.sh` | the runners. |
| `ladder.py` `inert_diff.py` `distil.py` | the analysis. |

**The raw round was 34 MB (17 MB JSON / 10 MB log / 6.6 MB report) and is NOT committed.**
`distil.py` reduced it to a 428 KB CSV that keeps every per-campaign outcome — which is the
whole basis of a CRN-paired comparison — so nothing published here is unreproducible.
`ladder.py --csv` reads it and reproduces every win%, SE, n, step and paired delta in this
README **exactly**; the only difference anywhere is `avgMis` at h3 reading 3.78 instead of
3.79, because the CSV sums exact per-run integers where the JSON path re-weights a figure
each chunk had already rounded to 1 dp. The CSV is the more accurate of the two.

## Method

```bash
# every chunk (a is rm -f, b is exit-code 2, c is the runs assertion — W1's contract, all three)
bash docs/measurements/c1/run_chunk.sh <tag> <heat> <base> 20

# one whole ladder = 5-10 rungs x 8 disjoint CRN slot sets, four chunks at a time
PREFIX=C1ctl BIN=runbin/C1base                              bash .../run_ladder.sh 20 -1 0 1 2 3 4 5 6 7 8
PREFIX=C1m1  BIN=runbin/C1lev EXTRA="SIGHTLINE_MIDTOOTH=1"  bash .../run_ladder.sh 20 4 6 7 8
# eight MORE disjoint slot sets on one rung (n=320 -> n=640), for the shipping decision
PREFIX=C1m1  BIN=runbin/C1lev EXTRA="SIGHTLINE_MIDTOOTH=1"  bash .../run_extra.sh 6

python3 docs/measurements/c1/ladder.py --csv C1ctl C1m1 --heats=-1,0,1,2,3,4,5,6,7,8
python3 docs/measurements/c1/inert_diff.py C1ctl C1r0 0 4 5 6 8
```

`run_chunk.sh` is a port of `docs/measurements/l3/run_chunk.sh` with **one substantive
change**: `ROOT` derives from the script's own location instead of `cd /home/user/SIGHTLINE`.
The L3 runner hard-codes the main checkout, so an agent running it from a worktree would have
silently measured **the wrong tree**. Everything else — the three layers of W1's completion
contract, in order — is unchanged.

At `N=20`, slot sets must step by 20 to stay disjoint (`slotBase + i`, i in 0..N-1), so the
bases are 0/20/…/140 for n=320 and 160…300 for the second n=320 on top.

## THE CONTROL — the first PER-RUNG ladder in the project's history

n=320 per rung (h6: 640), eight disjoint CRN slot sets, base `17934ee`.

| rung | RECRUIT | h0 | h1 | h2 | h3 | h4 | h5 | h6 | h7 | h8 |
|---|---|---|---|---|---|---|---|---|---|---|
| **win%** | 70.0 | 44.4 | 37.5 | 32.5 | 27.5 | 20.9 | 20.9 | 18.6 | 12.2 | 8.1 |
| ±SE | 2.6 | 2.8 | 2.7 | 2.6 | 2.5 | 2.3 | 2.3 | 1.5 | 1.8 | 1.5 |
| **what rung N buys** | — | 25.6 | **6.9** | **5.0** | **5.0** | **6.6** | **0.0** | **2.3** | **6.4** | **4.1** |

Every published ladder before this one sampled `{RECRUIT, 0, 2, 4, 6, 8}` and reported
`h4 → h6` as "the flat step". At per-rung resolution the flat step **is two rungs, and one of
them is exactly zero**:

* **rung 5 LINGERING WOUNDS (+1 enemy, HarshAttrition) buys 0.0 ±3.2.** It is the only rung on
  the ladder that cannot be distinguished from doing nothing at all.
* **rung 6 EXPOSED buys 2.3 ±2.7**, against a ladder whose other six rungs average 5.7.

## THE MECHANISM — a dead declaration, live for two programs

`Heat.Mods`' rung 6 read `Exposed = true, StatDelta = 1, AiTier = 1`. `Heat.AiTier` aggregates
with `Math.Max`, and **rung 4 (ELITE CADRE) already publishes tier 1** — so EXPOSED's
advertised coordination tooth **could never fire**. W6b wrote it, W6b's own comment says
"rungs 6-7 stay tier 1", and nothing in the repo said the row was inert: `HEATLADDERTEST`
pinned only the CUMULATIVE vector, which a dead declaration does not change. Rung 6 shipped
+1 body, +1 stat and a concealment flag, and the player climbed two rungs for a stat point.

## THE CANDIDATES — five levers, all provably apex-neutral, priced at heat 6

`Heat.MidTooth` is a bitfield (1 = the +1 per-hit damage moves rung 8 → 6, 2 = coordination
tier 2 moves 8 → 6, 4 = rung 6's stat point moves 6 → 7). **DmgDelta sums and AiTier is a
Math.Max, so the cumulative vector at heat 8 is identical in every mode** — the apex cannot be
pushed under its ≥5 hard floor by this dial, and the h8 chunks are byte-identical to control.

| mode | what moves | n | h6 ctl → lev | paired Δ | ±SE | discordant (lev-only / ctl-only) | z |
|---|---|---|---|---|---|---|---|
| **1** | **+1 dmg → rung 6** | **640** | **18.6 → 12.0** | **−6.6** | **1.6** | 30 / 72 | **−4.16** |
| 3 | +1 dmg AND tier 2 → rung 6 | 640 | 18.6 → 13.9 | −4.7 | 1.7 | 47 / 77 | −2.69 |
| 2 | tier 2 → rung 6 alone | 320 | 18.4 → 20.0 | **+1.6** | 1.9 | 22 / 17 | +0.80 |
| 4 | rung 6's stat point → rung 7 | 320 | 18.4 → 23.1 | +4.7 | 2.7 | 44 / 29 | +1.76 |
| 5 | mode 1 + mode 4 (the "trade") | 320 | 18.4 → 10.6 | −7.8 | 2.5 | 20 / 45 | −3.10 |

**The negative result is the more interesting half.** Moving the apex's coordination tier down
a rung is **not a difficulty lever**: on its own it measured **+1.6 in the player's favour**,
and mode 3 (which bundles it with the damage tooth) is 1.9 points *softer* than mode 1 alone.
Two programs of table comments describe `AiTier` as an escalation; over 320 CRN pairs at heat 6
it does not read as one. (Caveat, per the project's own rule 3: a CRN round prices
CONSEQUENCES and is structurally blind to FEEL. Tier 2 may well change how the fight *reads*
without changing who wins it — nobody has looked.)

Mode 4 is the useful by-product: it **prices rung 6's anonymous stat point at 4.7 points**, the
first time a single `StatDelta` on this ladder has been measured in isolation.

## SHIPPED: mode 1 — the apex's damage tooth moves down to EXPOSED

| rung | RECRUIT | h0 | h1 | h2 | h3 | h4 | h5 | h6 | h7 | h8 |
|---|---|---|---|---|---|---|---|---|---|---|
| control | 70.0 | 44.4 | 37.5 | 32.5 | 27.5 | 20.9 | 20.9 | 18.6 | 12.2 | 8.1 |
| **C1** | **70.0** | **44.4** | **37.5** | **32.5** | **27.5** | **20.9** | **20.6** | **12.0** | **9.4** | **8.1** |
| ±SE | 2.6 | 2.8 | 2.7 | 2.6 | 2.5 | 2.3 | 2.3 | 1.3 | 1.6 | 1.5 |
| paired Δ | +0.0 | +0.0 | +0.0 | +0.0 | +0.0 | +0.0 | −0.3 | **−6.6** | −2.8 | +0.0 |
| discordant pairs | 0/320 | 0/320 | 0/320 | 0/320 | 0/320 | 0/320 | 5/320 | 102/640 | 35/320 | 0/320 |

**Rungs RECRUIT through 4 and rung 8 are EXACTLY unchanged — zero discordant pairs out of 320
at each of six rungs.** The step the wave exists to fix:

| step | control | C1 |
|---|---|---|
| rung 5 | 0.0 | −0.3 |
| **rung 6** | **−2.3** | **−8.6** |
| rung 7 | −6.4 | −2.7 |
| rung 8 | −4.1 | −1.2 |
| `h4 → h6` (the published step) | **−2.3 ±2.7** | **−8.9 ±2.6** |

### Band compliance — nothing that was in band left it

Band: RECRUIT 75 / h0 55 / h2 40 / h4 30 / h6 20 / h8 10, ±8 (h8 ±5, hard floor ≥5).

| rung | control | C1 | band | verdict |
|---|---|---|---|---|
| RECRUIT | 70.0 | 70.0 | 67–83 | in (unchanged) |
| h0 | 44.4 | 44.4 | 47–63 | **below by 2.6** — pre-existing, untouched |
| h2 | 32.5 | 32.5 | 32–48 | in, at the floor (unchanged) |
| h4 | 20.9 | 20.9 | 22–38 | **below by 1.1** — pre-existing, untouched |
| h6 | 18.6 | **12.0** | 12–28 | **in, exactly at the floor** |
| h8 | 8.1 | 8.1 | 5–15 | in (unchanged) |

At the n=320 first round h6 read 11.2 and would have been 0.8 *under* the floor; the shipping
decision was taken on the doubled n=640 round, where it reads 12.0. That is the whole reason
the second eight slot sets were run.

## THE HONEST PART — the flat spot MOVED, it did not disappear

Rungs 7 and 8 now buy 2.7 and 1.2 where they bought 6.4 and 4.1. **That is arithmetic, not a
design failure, and it is the wave's real finding:** the lever is apex-neutral by construction,
so h4 (20.9) and h8 (8.1) are both pinned, leaving **12.8 points of win-rate for four rungs —
3.2 each.** No redistribution inside that window can give rungs 5–8 the ~5.7 points per rung
that rungs 1–4 buy. What the lever can do, and did, is stop one rung taking almost none of it.

The deficit that actually causes this sits **above** heat 4, not in the middle: h0 is 10.6
points under its band centre and h4 is 9.1 under, while h6 and h8 are within 2. The ladder is
flat at the bottom because its top half has sunk onto it. Fixing that is a BASE-difficulty
lever, not a `Heat.Mods` lever, and it is out of C1's scope. **It is the next thing to price.**

## Two things measured on the way that nobody had written down

**1. A "heat-N rung" is not a fixed rung.** `Events.cs:460` (`EventOutcomeKind.AddHeat`) lets
three field-event choices raise a run's HeatLevel mid-campaign, +1 each. So a heat-5 cell
contains some heat-6 missions. It is the *only* reason the lever is not perfectly inert below
rung 6: h5 shows 5 discordant pairs in 320 (−0.3, z=−0.45), h4 shows zero — because from h4
the escalation needs to fire twice, and in 320 campaigns it never did.

**2. `SIGHTLINE_MIDTOOTH=0` on the C1 tree is byte-identical to the pre-C1 binary.** The
`R0diag` round (`C1r0`) diffed every leaf field of 32 chunk pairs at h0/h4/h6/h8 —
**49,961 fields, zero differing** — before any lever was measured, and h5 was added later when
the AddHeat leak turned up (8 more pairs, 12,454 fields, also zero). The final shipping tree was
re-checked the same way against both the pre-C1 control (37,445 fields, 0 differing, dial off)
and the measured lever (24,538 fields, 0 differing, dial at its default), so every source edit
made after the round — the copy fixes, the new self-test, the AITEST re-pin — is proven
gameplay-inert rather than assumed to be.
