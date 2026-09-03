# L5 — THE PINNED LADDER OF RECORD, THE BRIDGE TO L4, AND THE h6 FACTORIAL

**Base commit: `7180374`** — wave "THE HEAT PIN AND L5" part A on `178464a` (`main` after PROGRAM
PARALLAX P4). Binary snapshot `runbin/L5` (gitignored), Release, copied from `bin/Release/net8.0`
of that commit. **272 chunks, every one asserted `runs=20`, zero `BAD` — 5,440 campaigns.**
Instrument: `SIGHTLINE_BALANCE`, Release snapshot under `xvfb-run`, greedy+sloppy per slot,
**heat PINNED** (`EventCatalog.HeatPinned`, the default for a batch since part A) except where the
bridge says otherwise.

## Why this round exists

Every ladder before this one was measured on an instrument that let three field-event arms raise a
campaign's heat mid-run (`Events.cs` `AddHeat`), so a "heat-N rung" contained heat-N+1 missions —
4.0% of L4's missions, 0% at h8 because heat 8 is clamped. The leak is directional: it compresses the
top of the ladder, the exact region L4 reported collapsed. Part A pinned it. L5 is the first ladder
where a rung means the rung, and it doubles the slot space (16 disjoint CRN slot sets per rung
against every earlier ladder's 8) because L4's own README said an 8-cluster rung "resolves roughly
13–14 points". It also runs the design ROADMAP asked for on L4's unresolved "+5.8 interaction".

## What is in this directory

| file | what |
|---|---|
| `L5-h{R,0,2,4,6,8}-b{0..150}.json/.log/.report.txt` | **the ladder** — 96 chunks, heat pinned, 16 slot sets x 6 rungs. |
| `L5bridge-h*-b{0..70}.*` | **the bridge** — L4's 8 slot sets, `SIGHTLINE_HEATPIN=0`, SAME binary. 48 chunks. |
| `L5fac-m{0,3}a{0,1}b{0,1}-h6-b{0..150}.*` | **the factorial** — 8 arms x 16 slot sets at h6, heat pinned. 128 chunks. |
| `L5-chunks.txt` `L5bridge-chunks.txt` `L5fac-chunks.txt` | the runner's own `OK <tag> runs=20` line for all 272 chunks — the completion assertions, verbatim. |
| `L5-LADDER.txt` `L5-BRIDGE.txt` `L5-PINEFFECT.txt` `L5-SPLITHALF.txt` `L5-FACTORIAL.txt` | the tools' output, as quoted below. |
| `INERT-*.*` | part A's inertness chunks (base `178464a` vs pin-off vs pin-on at h0-b0 and h4-b10). |
| `run_ladder.sh` `run_factorial.sh` | the runners (every chunk through `../c1/run_chunk.sh`). |
| `cluster.py` `rows.py` `factorial.py` `inert.py` `pincheck.py` | the analysis. |

The raw round is committed in full (JSON + log + report per chunk, ~23 MB of text) so every table
below is re-derivable and every per-campaign outcome is on disk.

## Method

```bash
cp -r bin/Release/net8.0 runbin/L5                              # from commit 7180374
bash docs/measurements/l5/run_ladder.sh ladder                  # 96 chunks: 6 rungs x 16 bases x N=10, pinned
bash docs/measurements/l5/run_ladder.sh bridge                  # 48 chunks: 6 rungs x L4's 8 bases, SIGHTLINE_HEATPIN=0
bash docs/measurements/l5/run_factorial.sh 6                    # 128 chunks: 8 arms x 16 bases at h6, pinned
python3 docs/measurements/l5/cluster.py L5                      # ladder + LEAK-CHECK + stalemates
python3 docs/measurements/l5/cluster.py L5bridge --bridge L4    # bridge + chunk-for-chunk vs the L4 archive
python3 docs/measurements/l5/rows.py L5bridge L5                # CRN-paired pin effect on the shared 8 bases
python3 docs/measurements/l5/factorial.py                       # the 2^3 factorial, chunk-paired
python3 docs/measurements/l5/rows.py --check docs/measurements/l5/L5*.json   # rows == pairedPolicy on all 272
```

Every chunk goes through `c1/run_chunk.sh` (rm the target first / exit code 2 = no display / assert
the JSON's own `runs`), XDG pinned per chunk. RECRUIT is heat −1 to the batch and `hR` in a file
name, as in L4. Slot sets step by 10 (N=10), bases 0..150 — the first eight are L4's exactly.
`rows.py --check` on all 272 chunks: rows-derived `pairedPolicy.slots` `==` the chunk's own block
and all five tallies equal on every one (`ROWS-CHECK: PASS`).

## THE LADDER — pinned, 16 clusters of 20 per rung

| rung | **L5 %** | n | binomial SE | **cluster SE** | jackknife | band | verdict |
|---|---|---|---|---|---|---|---|
| RECRUIT | **70.9** | 320 | 2.54 | 2.93 | 69.3–72.3 | 67–83 | IN (+3.9; 1.34 cluster-SE clear) |
| h0 | **46.9** | 320 | 2.79 | 3.41 | 45.3–48.3 | 47–63 | **OUT −0.1** (0.04 cluster-SE under) |
| h2 | **32.8** | 320 | 2.62 | 3.09 | 31.7–34.3 | 32–48 | IN (+0.8; 0.26 clear) |
| h4 | **20.0** | 320 | 2.24 | 2.96 | 18.7–21.0 | 22–38 | **OUT −2.0** (0.68 cluster-SE under) |
| h6 | **13.1** | 320 | 1.89 | 2.32 | 12.0–14.0 | 12–28 | IN (+1.1; 0.48 clear) |
| h8 | **8.1** | 320 | 1.53 | 1.98 | 7.0–8.7 | 5–15 | IN (+3.1; 1.58 clear) |

**Monotone at every step; four of six in band.** Steps RECRUIT→h0 **24.1**, h0→h2 14.1, h2→h4 12.8,
h4→h6 6.9, h6→h8 5.0. The cluster SE exceeds the binomial at all six rungs (ratio 1.15–1.32): the
slot sets disagree, as they did in every round since C3's review said to look.

**Neither OUT is a measured breach.** h0 is on its floor to the decimal; h4 is 0.68 of its own
cluster SE under. Nor is any IN a robustness claim — the jackknife range at h0 (45.3–48.3) straddles
the floor.

`LEAK-CHECK: PASS` — all 96 chunks pinned, `campaignsRaised = missionsAbovePin = 0` on every one
(6,749 missions). The bot still took a heat-raising arm 144 times in these 1,920 campaigns
(`heatRaisingPicks` 28/25/25/21/21/24 by rung): the pin nulls the OUTCOME, not the choice, so the
event economy of the batch is the same as the leaky one's, minus the heat.

## THE BRIDGE — L4 reproduced to the campaign, and what the pin is worth

The same binary, `SIGHTLINE_HEATPIN=0`, L4's 8 slot sets:

| | RECRUIT | h0 | h2 | h4 | h6 | h8 |
|---|---|---|---|---|---|---|
| L4 (`7315425`, unpinned) | 71.9 | 53.1 | 33.1 | 21.9 | 10.6 | 4.4 |
| bridge (`7180374`, unpinned) | 71.9 | 53.1 | 33.1 | 21.9 | 10.6 | 4.4 |
| **chunk-for-chunk vs L4** | **48/48 chunks, 960/960 (slot, policy) outcomes identical** | | | | | |
| campaigns raised / missions off-rung (the leak L4 carried) | 16 / 29 | 17 / 30 | 16 / 32 | 13 / 25 | 12 / 18 | **0 / 0** |

**The CRN chain is intact from `7315425` through every merge since (PARALLAX P1–P4, SETTINGS
EVERYWHERE, THE STRIDE, THE MODES GET THE BESTIARY) and part A of this wave** — not one of 960
outcomes moved. Every "gameplay-inert" claim those waves made is confirmed at the campaign level on
these worlds.

**The pin, CRN-paired on the shared 8 bases** (`rows.py L5bridge L5`, 160 pairs per rung):

| rung | unpinned | pinned | delta | pinned-only wins | unpinned-only | z |
|---|---|---|---|---|---|---|
| RECRUIT | 71.9 | 74.4 | **+2.5** | 4 | 0 | +2.00 |
| h0 | 53.1 | 54.4 | +1.2 | 3 | 1 | +1.00 |
| h2 | 33.1 | 35.0 | +1.9 | 6 | 3 | +1.00 |
| h4 | 21.9 | 20.6 | −1.2 | 1 | 3 | −1.00 |
| h6 | 10.6 | 12.5 | +1.9 | 3 | 0 | +1.73 |
| h8 | 4.4 | 4.4 | **0.0** | **0** | **0** | — |

Pooled over 960 pairs: **32.5% → 33.5%, +1.04, 24 discordant (17 vs 7), McNemar z = +2.04.** The
direction is the predicted one (the leak had made every rung below 8 harder) and **h8 has zero
discordant pairs because it cannot leak** — the prediction the pin was built on, observed. A rung's
worth of leak is one or two points; it was never the whole of L4's top-of-ladder collapse.

## THE LEVEL IS A SLOT-SPACE QUESTION — split-half, both halves pinned

| rung | L4's 8 sets (b0–70) | 8 NEW sets (b80–150) | diff | SE of diff | t |
|---|---|---|---|---|---|
| RECRUIT | 74.4 | 67.5 | +6.9 | 5.78 | +1.19 |
| **h0** | **54.4** | **39.4** | **+15.0** | 5.82 | **+2.58** |
| h2 | 35.0 | 30.6 | +4.4 | 6.30 | +0.69 |
| h4 | 20.6 | 19.4 | +1.2 | 6.11 | +0.20 |
| h6 | 12.5 | 13.8 | −1.2 | 4.79 | −0.26 |
| **h8** | **4.4** | **11.9** | **−7.5** | 3.58 | **−2.09** |

L4's "h0 in band by +6.1" and "h8 under its floor" were each **one draw of eight clusters**; the
other eight read fifteen points lower at h0 and seven higher at h8. C2 found the same thing inside
L3's slot space at h2 (b0–30 vs b40–70, p = 0.005). Two consequences, stated plainly:

1. **The L4 rows and the L5 rows are not comparable at h0 or h8** — the difference between them is
   mostly which slot sets were drawn, not the pin and not any lever. Do not subtract one from the
   other.
2. **A rung is sixteen slot sets now.** An 8-set rung on this instrument can miss its own band
   centre by 7–8 points on the draw alone; L5's cluster SEs (2–3.4) are the honest resolution.

## THE FACTORIAL — h6, MIDTOOTH{0,3} x AIDECLINE{0,1} x BIOMEMECH{0,1}

8 arms x 16 CRN slot sets x 20 = **2,560 campaigns**, heat pinned. Coding: +1 = lever REMOVED
(`SIGHTLINE_MIDTOOTH=0` / `AIDECLINE=0` / `BIOMEMECH=0`), so a positive effect reads "removing it
makes h6 easier", L4's sign. The all-shipped arm `m3a1b1` sets the three dials to their defaults
explicitly and **reproduces the ladder's own h6 chunks byte-for-byte (minus `harness{}`) 16/16** —
the round's CRN-chain check, and the proof that "explicit default" and "unset" are the same table.
C3's `KILLTREADMILL` is not a factor: L4's archive shows its removal changed play (0/8 chunks
byte-identical) but not one of 160 outcomes at h6, so the three-lever Q below is L4's four-lever
quantity exactly on those slots.

| arm | MIDTOOTH | AIDECLINE | BIOMEMECH | win% | cluster SE |
|---|---|---|---|---|---|
| m3a1b1 (composed = L5 h6) | 3 | 1 | 1 | **13.12** | 2.32 |
| m3a1b0 | 3 | 1 | 0 | 11.25 | 1.55 |
| m3a0b1 | 3 | 0 | 1 | 9.38 | 2.09 |
| m3a0b0 | 3 | 0 | 0 | 13.44 | 2.08 |
| m0a1b1 | 0 | 1 | 1 | **20.00** | 2.04 |
| m0a1b0 | 0 | 1 | 0 | 18.12 | 1.88 |
| m0a0b1 | 0 | 0 | 1 | 19.06 | 2.34 |
| m0a0b0 (all off) | 0 | 0 | 0 | **20.31** | 2.30 |

Chunk-paired contrasts (mean over 16 clusters, cluster SE, t on 15 df, 95% CI):

| contrast | mean | cluster SE | t(15) | 95% CI | L4 (8 sets, unpinned) |
|---|---|---|---|---|---|
| s_M MIDTOOTH=0, others shipped | **+6.88** | 2.13 | **+3.22** | [+2.3, +11.4] | +6.2 |
| s_A AIDECLINE=0, others shipped | −3.75 | 2.60 | −1.44 | [−9.3, +1.8] | −3.8 |
| s_B BIOMEMECH=0, others shipped | −1.88 | 2.92 | −0.64 | [−8.1, +4.3] | +1.2 |
| sum of single removals | +1.25 | **6.32** | +0.20 | [−12.2, +14.7] | +3.6 |
| joint (all three off) | +7.19 | 2.74 | +2.63 | [+1.4, +13.0] | +9.4 |
| **Q = joint − sum (L4's "+5.8")** | **+5.94** | **5.25** | **+1.13** | **[−5.2, +17.1]** | +5.8 (t=2.05) |
| c_MA two-way at the composed point | +2.81 | 3.56 | +0.79 | [−4.8, +10.4] | |
| c_MB two-way at the composed point | 0.00 | 3.20 | 0.00 | [−6.8, +6.8] | |
| c_AB two-way at the composed point | +5.94 | 4.04 | +1.47 | [−2.7, +14.5] | |
| **main M (MIDTOOTH removed)** | **+7.58** | **0.96** | **+7.91** | **[+5.5, +9.6]** | |
| main A (AIDECLINE removed) | −0.08 | 1.52 | −0.05 | [−3.3, +3.2] | |
| main B (BIOMEMECH removed) | +0.39 | 1.35 | +0.29 | [−2.5, +3.3] | |
| int M×A | +0.70 | 1.51 | +0.46 | [−2.5, +3.9] | |
| int M×B | −0.70 | 1.06 | −0.66 | [−3.0, +1.6] | |
| int A×B | +2.27 | 1.27 | +1.78 | [−0.4, +5.0] | |
| int M×A×B | −0.70 | 1.02 | −0.69 | [−2.9, +1.5] | |

**VERDICT: NOT RESOLVED by the pre-registered criterion** (|t| ≥ 2.3, or a CI excluding +5.8):
Q = +5.94 ± 5.25, t(15) = +1.13, and the CI includes both +5.8 and zero. The point estimate
reproduces L4's to the decimal — on twice the clusters, with a pinned instrument — and it is still
one SE from nothing. **The factorial says why, and that is the result:** Q is `joint − (s_M + s_A +
s_B)`, and the sum of three conditional contrasts carries an SE of **6.3** by itself. Pushing Q's SE
to 2.5 needs ~(5.25/2.5)² × 16 ≈ **70 clusters, ~1,400 campaigns per arm** — more than this entire
round per arm. What the same 2,560 campaigns DO resolve, because every arm contributes to every
averaged term:

* **`Heat.MidTooth` is the term.** Main effect **+7.58 ± 0.96, t = +7.9**, positive in 14 of 16
  clusters and never negative. Removing it puts h6 at 20.0 (m0a1b1) — L3's h6, to the decimal, on a
  different instrument and slot space.
* **AIDECLINE and BIOMEMECH have main effects of zero** (−0.08 ± 1.52, +0.39 ± 1.35). L4's "removing
  C2 makes h6 harder, −3.8" reproduces as the conditional s_A (−3.75) — and averaged over the other
  two levers it is nothing, which is what a conditional contrast looks like when a two-way term is
  in play: A×B at +2.27 ± 1.27 (t = 1.78) is the largest interaction in the design and it is not
  resolved either.
* **No two-way or three-way term reaches |t| = 2.** The levers are additive within ±3 points at
  this resolution.

**So: stop citing +5.8.** Not because it was wrong — it reproduces — but because it is a quantity
this instrument cannot resolve at any affordable n, while the averaged effects it was standing in
for are resolved and say the composition is additive and MIDTOOTH-dominated.

## THE STALEMATE SPLIT — what the harness floor actually is

Part A split the guard's one word into `STALEMATE-MISSION` (per-mission cap, 50 turns) and
`STALEMATE-RUN` (W9's run cap, 150 turns). Across **all 5,440 campaigns of this round (ladder + bridge + factorial): 63 mission-arm, 0 run-arm**:

* **The run arm fired ZERO times.** Every stalemate is the mission arm; `runTurns` at the stall
  ranges 51–79 against a cap of 150. W9's backstop is a backstop.
* **L5: 27/1,920 = 1.41%** — RECRUIT **14/320 = 4.4%**, h0 4, h2 2, h4 4, h6 1, h8 2. The bridge's
  16/960 = 1.67% is L4's 16 exactly (same outcomes). ROADMAP's "2.1%" was L3's.
* **It is the bot's finishing line on task objectives, late in a long run:** objectives Escort 10,
  Eliminate 9, Evac 4, Rescue 3, Sabotage 1; missions 3–4 hold 18 of 27. RECRUIT carries the most
  because RECRUIT campaigns live longest and meet the most Escorts. Three worlds stall at more than
  one rung — slot 46 sloppy on **mission-1 Eliminate** at h0, h2 AND h4 (rt 51 each: the sloppy
  policy has a deadlock on that opener regardless of heat), slot 74 greedy m3 Eliminate at h2/h4,
  slot 80 sloppy at RECRUIT twice.
* **Ex-stalemate, the ladder moves by at most 0.6 at any heat rung** (`runWinRateExStalemate`); at
  RECRUIT by ~3. It is an instrument floor, sized, and it is not why any rung is where it is.

## What this round does NOT do

* **Ships no lever.** h0 on its floor and h4 −2.0 are findings; the ROADMAP's balance items are
  unchanged in kind, re-pointed in number (h6 and h8 are IN band on the pinned instrument; the OUT
  rungs are now the middle-low ones).
* **Does not resolve Q**, and says the cost of doing so; **does** resolve the averaged effects.
* **Does not make L4 comparable with L5 at h0 or h8.** The bridge proves L4's outcomes are
  reproduced; the split-half proves its slot draw was atypical at those two rungs. Both are true.
* **Does not measure a camping policy, the opener re-tune, or a single-policy batch** — `campaigns[]`
  makes the last of these pairable, which is all part A promised.
* **The stalemate floor is sized and located, not fixed.** The bot's Escort finishing line and the
  slot-46 opener deadlock are autopilot work.
