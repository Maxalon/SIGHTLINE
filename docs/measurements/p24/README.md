# P24 — THE TOP OF THE LADDER: one level lever, argued first and measured once

**Base commit: `3b684a7`** — `main` after PROGRAM PARALLAX milestone 17 (P23 THE APEX BITES).
Binary snapshot `runbin/p24/` (gitignored), Release, built from this wave's own tree. Instrument
`SIGHTLINE_BALANCE`, Release snapshot under `xvfb-run`, greedy+sloppy per slot, **heat PINNED**
(`EventCatalog.HeatPinned`).

**THE LEVER: `Mission.HostileAimTrim` 0 -> 5.** Five flat points off every hostile's aim, applied in
`Mission.MakeHostile` — the one funnel every hostile in the game is built through.
**`SIGHTLINE_AIMTRIM=0` restores the pre-P24 force exactly**, and is the arm this round used as its
baseline.

| arm | env | what it is |
|---|---|---|
| **base** | `SIGHTLINE_AIMTRIM=0` | the pre-P24 tree — the control, and the restore flag |
| **aim** | *(none)* | the shipped tree (`HostileAimTrim = 5`) |

**Totals: 320 chunks, every one asserted, zero `BAD` — 6,400 campaigns, 26,124 missions,
`LEAK-CHECK PASS` (0 campaigns raised, 0 missions off-rung on all 320).** The round is
2 arms x 6 rungs x 16 CRN slot bases (192 chunks) plus a 2 arms x 4 rungs x 16-NEW-slot-base
extension (128 chunks) giving n=640 at h0/h4/h6/h8.

Every chunk goes through `docs/measurements/p15/run_chunk.sh`, the runner of record, which keeps all
three layers of `CLAUDE.md`'s measurement contract: (a) `rm -f` the target JSON first, (b) the
process EXIT CODE, (c) `p15/check_chunk.py` asserting `runs` against the artifact's own
`batch.expectedRuns`, the rung and slot base against what the runner exported, and `heatLeak.pinned`
with zero raised. **P24 adds a fourth: `levers.aimTrim`, read out of the artifact, against the ARM
the chunk was launched as** — see "The fourth layer" below.

---

## 1. WHY THIS LEVER — the argument, which came before the numbers

### 1.1 The two breaches, and what kind of defect they are

P23 left the shipped tree here (its own round, `docs/measurements/p23/`):

| rung | RECRUIT | h0 | h2 | h4 | h6 | h8 |
|---|---|---|---|---|---|---|
| win% | 73.4 | 44.4 | 34.7 | 23.6 | 9.4 | 5.0 |
| FUL-13 band | 67-83 | **47-63** | 32-48 | 22-38 | **12-28** | 5-15 |
| **vs band CENTRE** | −1.6 | **−10.6** | −5.3 | −6.4 | **−10.6** | −5.0 |

Two rungs are under their floors. **But the row that decides what KIND of lever this needs is the
last one: every rung of the heat ladder is under its band CENTRE, by a mean of −7.6 points, and
RECRUIT — the one rung that is not a heat rung — is the only one within 2 points of its own.** That
is not a defect at h6 and a second defect at h0. It is an offset in the whole ladder, of which two
rungs happen to have crossed a line.

### 1.2 The inherited claim, checked rather than inherited — and half of it is wrong

The claim handed down from C1/L4 is *"an apex-neutral redistribution inside the heat table cannot fix
a level problem — the rungs sum identically, so moving points between them is a choice about
ALLOCATION."* Checked on this tree, it splits in two, and only one half survives:

- **TRUE, and provable in one line, for h0.** `Heat.Active(0)` yields **nothing** — the loop is
  `for (i = 0; i < n; i++)` with `n = 0`, and every accessor (`EnemyDelta`, `StatDelta`, `DmgDelta`,
  `AiTier`, `IntelBonus`) iterates it. **No row of `Heat.Mods`, in any arrangement, can move h0 by
  any amount.** h0's win rate is a pure function of the base game. A rung that cannot be reached by
  the instrument that is supposed to tune it is a base-difficulty question by construction.
- **FALSE as usually restated, for h6.** "The heat table cannot raise h6" does *not* follow, and
  C1's own archive contains the counter-example: mode 4 (`bit 4` — move rung 6's anonymous +1 stat
  UP to rung 7) is apex-neutral by construction, because h7 and h8 sum rungs 1-7 and 1-8 and the
  point never leaves that set. C1 measured it at **h6 18.4 -> 23.1, +4.7 ±2.7** on its own tree.
  So a redistribution that lifts h6 alone by roughly the amount needed **exists, is already
  implemented, and already has a dial.**

**It was rejected anyway, on three grounds, and this is the wave's main design decision:**

1. **It cannot touch h0**, which is equally out of band and has been for three successive ladders
   (L5 −0.1, L6 −2.6, P23 −2.6). A correction that fixes one of two identical breaches is half a
   correction.
2. **It buys h6 by making rung 6 buy less.** L7 measured rung 6 (`h5 -> h6`) at **+11.2, one of only
   three steps resolved at n=320**, next door to rung 5 at +0.6, the flattest rung on the ladder.
   Moving rung 6's stat point up to rung 7 spends the ladder's strongest resolved mid-rung to buy a
   band verdict. C1's own dispersion analysis is the precedent for refusing that trade.
3. **It leaves the −7.6 mean shortfall exactly where it is.** Five of six rungs would still sit
   below their centres; only the label on one of them would change.

**Conclusion: this is a LEVEL problem and it needs a BASE-difficulty lever.** The inherited reasoning
holds for the reason it was written, and the corollary people draw from it does not — both are
recorded here because the next wave will inherit whichever of these paragraphs it reads.

### 1.3 Is the BAND wrong instead? — considered, and NOT the conclusion

The brief allowed "the band is wrong at h6" as an outcome, and it was weighed. It is rejected, and
the reason is the shape of the miss rather than a defence of the band:

- **If the band were wrong at h6, h6 would be the outlier.** It is not. Every heat rung is below its
  centre by 5-11 points, and the two that crossed floors are simply the two with the least room. A
  band that is wrong at h0 AND h2 AND h4 AND h6 AND h8, in the same direction, by similar amounts,
  is not a band that is wrong — it is a game that is uniformly harder than the band says.
- **One number is a direct, band-free reading of the same fact.** `RECRUIT -> h0` measured **29.0
  points** on a band whose own step is **20**. RECRUIT is a single relief rung; nothing in the
  design says the first step of the ladder should be half again as large as the whole of `h0 -> h2`
  plus `h2 -> h4`. That is a statement about the game's level that does not depend on the band's
  centres being right.
- **The rules forbid doing both**, and the round that changes the game may not also move the
  measuring stick. The band is left exactly as FUL-13 set it. If a later wave wants to re-derive it,
  it now has this round's data to do it against — and `docs/ROADMAP.md` carries the item that
  h1/h3/h5/h7 have no band at all, which is where a band re-derivation should start.

### 1.4 The five candidate levers, and why four were rejected

Every candidate already had a dial; none needed inventing.

| candidate | verdict |
|---|---|
| **`Mission.EnemyBaseCount` 4 -> 3** (`SIGHTLINE_ENEMYBASE`) | **Rejected on three counts.** (i) It changes hostile COUNT, which W4 deliberately bought (contact breadth) and which is the raw material `los-targets/ARMED-soldier-turn` is made of — the decision-density instrument would move with it. (ii) It is a coarse instrument: L7's own arm measured `base 4 -> 2` at **h6 10.9 -> 24.2, h7 7.0 -> 15.0, h8 7.7 -> 10.9** (n=640/rung), so one body is worth something like 6 points at h6 with no bound on what it is worth at h2 or RECRUIT. (iii) **It partly de-scopes P23**: lowering the base pulls the finale's request back under `ForceCeiling`, which is exactly the region P23's lever A exists to fix. A correction wave may not quietly shrink the wave it is correcting. |
| **`Mission.HostileToughness` 3 -> 2** (`SIGHTLINE_TOUGH`) | **Rejected.** It is one half of X1's deliberately-set pair — the constant that makes a trade take ~2 hits both ways — and `CLAUDE.md` requires "a measured round per side" to move it. Re-opening a decision is not the same as correcting a drift. |
| **`Mission.HostileDamageTrim` 1 -> 2** (`SIGHTLINE_TRIM`) | **Rejected**, same reason: the other half of the same pair. |
| **`Mission.OpenerTrim` 1 -> 2** | **Rejected.** m1's conditional is already 93-94% at every rung. §3.D forbids front-loaded ANXIETY, not front-loaded ease, but another body off the opener buys nothing where the shortfall actually is (missions 3-6) and would make an already-unlosable opener more so. |
| **`Mission.ForceCeiling`** | **Rejected: wrong direction.** Raising it makes the game harder; lowering it below 12 re-introduces the defect P23 just removed. |
| **`Mission.HostileAimTrim` 0 -> 5** (`SIGHTLINE_AIMTRIM`) | **SHIPPED.** See below. |

### 1.5 Why accuracy, in the words of the wave that built the dial and did not spend it

X2 TRUE NORTH II built `HostileAimTrim` for exactly this job and then spent its one lever on
`OpenerTrim` instead. Its argument is in the field's own doc-comment and it still holds on this tree:

> *Fourteen waves each measured on their own base composed into a tree 20 points below its own
> published band. Almost none of that drift was a difficulty DECISION: Q1 stopped pod scatter
> stacking two bodies in one tile, FUL-9 repaired route exposure, FUL-6 fielded pods of 3 with
> linked activation, W6 gave the mid ladder a coordination tier, W4 opened the fight on more than
> one bearing. **Every one of them made the SAME force put MORE FIRE on the squad.** The give-back
> therefore comes out of the same quantity — how much of that fire lands — and out of nothing the
> waves deliberately bought.*

Three properties made it the pick:

1. **It is the only base-difficulty dial whose current value is not itself a designed constant.**
   `HostileToughness = 3` and `HostileDamageTrim = 1` encode X1's two-hit trade; `EnemyBaseCount = 4`
   is the historical force size; `ForceCeiling = 12` is a layout number. `HostileAimTrim = 0` is
   "the pre-X2 force" — a dial parked at the identity. Spending it undoes nobody's decision.
2. **It is a pure level shift.** It is a constant, so it changes no rung-to-rung difference: the
   heat ladder's own aim component (`StatDelta`) is untouched, and the steps L7 and P23 spent two
   rounds restoring cannot be flattened by an offset applied equally to all of them.
3. **It touches nothing else the project measures** — not hostile count, not hostile HP, not player
   damage — so Eliminate's turn budget and the decision-density instruments should not move through
   it. That claim is checked in §6, not repeated.

### 1.6 The dose, and why 5 is not a number anybody searched for

**X2 measured this exact dial's dose-response, at h0, and rejected the larger dose on TEXTURE rather
than on win rate** (`docs/measurements/x2/`, n=40 per arm, pre-W1 stream — quoted as a PRIOR, not as
a comparable number):

| arm | h0 win% |
|---|---|
| R0 baseline | 35.0 |
| **A1 `SIGHTLINE_AIMTRIM=5`** | **42.5** |
| A2 `SIGHTLINE_AIMTRIM=10` — **not shipped**, "Eliminate 4.80t, Escort 13.66t" | 50.0 |

So **5 is the largest dose the project has already accepted**, its one archived price (+7.5 at h0)
is within a point of the −7.6 mean shortfall this wave is correcting, and it was chosen before any
P24 batch ran. **The constant was measured once and published; it was not iterated against the
band.** Tuning a constant until the number lands is how a band becomes unfalsifiable.

### 1.7 The prediction, written into `run_round.sh` before the round ran

`run_round.sh`'s header carries it above the data, and commit `c5ff616` archives the 16-set round
with the extension **declared and not yet run** — because extending n after seeing a marginal rung
is precisely the optional stopping C1 was sent back for.

| # | prediction | outcome |
|---|---|---|
| 1 | every rung rises | **WRONG at the apex.** Five of six rose; h8 read −0.6 / +0.3 / −0.2 on three independent readings. |
| 2 | h0 moves about +7.5 (X2's prior) | **+7.5 on the round's first 16 sets, to the decimal — and +0.0 on the next 16.** See §5. |
| 3 | the effect is smallest at the ends | **half right**: smallest at the apex (0), but RECRUIT (+3.1) is not the largest and h2 (+5.3) is. |
| 4 | `RECRUIT->h0` shrinks toward 20, `h6->h8` grows toward 10 | **BOTH CORRECT**: 29.0 -> 24.7 and 2.8 -> 6.3. |
| 5 | h0 and h6 clear their floors without h2/RECRUIT crossing ceilings or h8 falling | **h0 yes; h6 lands ON its floor; nothing crossed a ceiling; h8 did not move.** |

---

## 2. THE BRIDGE, FIRST — and it certifies three things

`P24-BRIDGE.txt`. P24's base arm is the shipped binary under this wave's own restore flag; P23's AB
arm is the tree it was built from.

| rung | RECRUIT | h0 | h2 | h4 | h6 | h8 | total |
|---|---|---|---|---|---|---|---|
| chunks identical | 16/16 | 16/16 | 16/16 | 16/16 | 16/16 | 16/16 | **96/96** |
| legs identical | 320/320 | 320/320 | 320/320 | 320/320 | 320/320 | 320/320 | **1,920/1,920** |
| P24 base % | 73.4 | 44.4 | 34.7 | 22.8 | 9.4 | 6.6 | |
| P23 AB % | 73.4 | 44.4 | 34.7 | 22.8 | 9.4 | 6.6 | |

…and on the 16 NEW slot bases at h4/h6/h8, **48/48 chunks and 960/960 legs identical** (24.4 / 9.4 /
3.4 both arms). **BRIDGE INTACT on 2,880 campaigns.** So:

* **`SIGHTLINE_AIMTRIM=0` is a TRUE restoration**, not a named one — the house rule two waves have
  been burned by;
* P24's non-lever edits (FORCETEST leg (H), the `levers` block in the balance JSON) are
  **stream-neutral**;
* the CRN chain reaches back through milestone 17 to P23, and through P23's own bridge to L6/L7.

## 3. THE ARMS

### 3.1 Six rungs x 16 CRN slot bases, n=320 per arm-rung (`P24-ARMS16.txt`)

| rung | base | **aim** | band | base verdict | **aim verdict** |
|---|---|---|---|---|---|
| RECRUIT | 73.4 | **76.6** | 67-83 | IN (−1.6 vs centre) | **IN (+1.6 vs centre)** |
| h0 | 44.4 | **51.9** | 47-63 | **OUT −2.6** | **IN (+4.9 over floor)** |
| h2 | 34.7 | **40.0** | 32-48 | IN (−5.3 vs centre) | **IN (exactly on centre)** |
| h4 | 22.8 | **25.6** | 22-38 | IN (+0.8 over floor) | **IN (+3.6 over floor)** |
| h6 | 9.4 | **12.2** | 12-28 | **OUT −2.6** | **IN (+0.2 over floor)** |
| h8 | 6.6 | **5.9** | 5-15 | IN (+1.6) | IN (+0.9) |

### 3.2 Four rungs x 32 CRN slot bases, n=640 per arm-rung (`P24-ARMS32.txt`) — the better estimate

| rung | base | **aim** | band | base verdict | **aim verdict** |
|---|---|---|---|---|---|
| h0 | 45.8 | **49.5** | 47-63 | **OUT −1.2** (0.64 clSE) | **IN (+2.5 over floor)** |
| h4 | 23.6 | **26.1** | 22-38 | IN (+1.6) | **IN (+4.1)** |
| h6 | 9.4 | **11.9** | 12-28 | **OUT −2.6** (1.85 clSE) | **OUT −0.1** (0.11 clSE) |
| h8 | 5.0 | **4.8** | 5-15 | IN, exactly ON the >=5 hard floor | **OUT −0.2** (0.18 clSE) |

**Read §3.1 and §3.2 together and do not quote only the first.** On the 16 sets the round shares with
P23's archive, every rung of the shipped ladder is in band. On 32 sets, h6 sits 0.1 point UNDER its
floor and h8 0.2 under its hard floor — both a fifth of a cluster SE, neither a measured breach in
either direction, and neither a pass. **The honest statement is that h6 and h8 now sit ON their
floors rather than under them**, and C1's precedent (mode 1, P(below floor) ~= 0.49) is explicit that
a rung landing on its floor may not be claimed as in band.

### 3.3 The contrast, with `n_disc` and the MDE beside every row

MDE80 = 2.80 x the McNemar pair SE — the smallest effect this design has 80% power to detect.

**16 sets, n=320/arm-rung:**

| rung | eff | b | c | n_disc | MDE80 | z | chunk t(15) | verdict |
|---|---|---|---|---|---|---|---|---|
| RECRUIT | +3.1 | 39 | 29 | 68 | 7.22 | +1.21 | +1.40 | NOT RESOLVED |
| h0 | **+7.5** | 52 | 28 | 80 | 7.83 | **+2.68** | **+3.22** | at the edge (p≈0.007) |
| h2 | +5.3 | 60 | 43 | 103 | 8.88 | +1.68 | +1.41 | NOT RESOLVED |
| h4 | +2.8 | 36 | 27 | 63 | 6.95 | +1.13 | +1.13 | NOT RESOLVED |
| h6 | +2.8 | 29 | 20 | 49 | 6.12 | +1.29 | +1.04 | NOT RESOLVED |
| h8 | −0.6 | 12 | 14 | 26 | 4.46 | −0.39 | −0.32 | NOT RESOLVED |
| **POOLED (6 rungs, n=1,920)** | **+3.49** | 228 | 161 | 389 | **2.88** | **+3.40** | | **RESOLVED** |

**32 sets, n=640/arm-rung:**

| rung | eff | b | c | n_disc | MDE80 | z | chunk t(31) | verdict |
|---|---|---|---|---|---|---|---|---|
| h0 | +3.8 | 97 | 73 | 170 | 5.70 | +1.84 | +1.86 | NOT RESOLVED |
| h4 | +2.5 | 77 | 61 | 138 | 5.14 | +1.36 | +1.01 | NOT RESOLVED |
| h6 | +2.5 | 53 | 37 | 90 | 4.15 | +1.69 | +1.43 | NOT RESOLVED |
| h8 | −0.2 | 21 | 22 | 43 | **2.87** | −0.15 | −0.15 | NOT RESOLVED |
| **POOLED (4 rungs, n=2,560)** | **+2.15** | 248 | 193 | 441 | **2.30** | **+2.62** | | at the edge (p≈0.009) |

**NOT ONE PER-RUNG CONTRAST RESOLVES.** The lever is resolved only in aggregate, and the aggregate
itself is +3.49 (resolved, 6 rungs x 16 sets) or +2.15 (at the edge, 4 rungs x 32 sets) depending on
which design you pool. Nobody should quote a per-rung number from this round as a size.

## 4. THE APEX DOES NOT RESPOND TO ACCURACY — the round's one falsified prediction

h8 was measured three ways and is zero in all of them:

| slot sets | n | eff | b | c | n_disc | MDE80 | z |
|---|---|---|---|---|---|---|---|
| original 16 | 320 | −0.6 | 12 | 14 | 26 | 4.46 | −0.39 |
| NEW 16 (out of sample) | 320 | +0.3 | 9 | 8 | 17 | 3.61 | +0.24 |
| **all 32** | **640** | **−0.2** | 21 | 22 | 43 | **2.87** | **−0.15** |

**It is the tightest MDE in the round and the only rung where the design can exclude an effect of
~3 points.** Every other rung's point estimate is positive (+2.5 to +7.5). This is a direction with
an n, not a resolved difference between rungs — but it says something the next corrective wave
needs: **whatever is carrying the apex's difficulty, it is not how often hostiles hit.** The
candidates left are the ones a level lever cannot reach through accuracy — bodies, HP/stat, per-hit
damage, and the coordination flags that arrive at rungs 3, 4 and 6.

## 5. THE SPLIT-HALF, AND IT IS THE FIFTH TIME

The check L5, L6 and L7 all say to run first. On the 16 slot sets the round had never seen
(`P24-ARMS-OOS.txt`):

| rung | original 16 | NEW 16 | all 32 |
|---|---|---|---|
| h0 | **+7.5** (z +2.68) | **0.0** (b=45, c=45) | +3.8 (z +1.84) |
| h4 | +2.8 | +2.2 | +2.5 |
| h6 | +2.8 | +2.2 | +2.5 |
| h8 | −0.6 | +0.3 | −0.2 |
| pooled | +3.5 | **+1.2** (z +1.00) | +2.4 |

**h4, h6 and h8 replicate almost exactly. h0 does not: +7.5 against 0.0, with 45 discordant campaigns
each way.** The base arm's own h0 differs by slot half too (44.4 vs 47.2), as does h8 (6.6 vs 3.4).
**So the headline "+7.5 at h0" is a property of sixteen slot sets, and the 32-set +3.8 (MDE 5.70) is
not resolved either.** This is the fifth time this project has caught that shape — L5's split-half on
L4, W2's four-vs-sixteen, L6's re-price of P20, L7's rung-8 sign, and now here. **A rung is sixteen
slot sets; so is a lever; and h0 is evidently a rung where sixteen is still not enough.**

## 6. THE TEXTURE CHECK — because texture is the criterion X2 used to reject the larger dose

`P24-TEXTURE.txt`, pooled over all six rungs x 16 sets (7,933 base / 7,955 aim missions).

| objective | base avgTurns | aim avgTurns | delta |
|---|---|---|---|
| Eliminate | 4.78 | 4.83 | **+0.05** |
| Decapitate | 5.75 | 5.69 | −0.07 |
| Hack | 4.29 | 4.29 | +0.01 |
| Sabotage | 4.60 | 4.62 | +0.03 |
| Rescue | 5.24 | 5.33 | +0.09 |
| Defend | 8.60 | 8.63 | +0.03 |
| Escort | 7.99 | 8.58 | +0.59 |
| Evac | 7.29 | 8.01 | +0.73 |

| decision-density field | base | aim | delta |
|---|---|---|---|
| `choicesPerArmedSoldierTurn` | 2.255 | 2.247 | **−0.008** |
| `targetChoicesPerArmedSoldierTurn` | 0.458 | 0.455 | −0.002 |
| `positionChoicesPerArmedSoldierTurn` | 1.797 | 1.792 | −0.005 |
| `losTargetsPerArmedSoldierTurn` | 2.570 | 2.594 | +0.025 |
| `meaningfulChoicesPerTurn` | 2.844 | 2.897 | +0.053 |
| `leadSwingsPerMatch` | 0.737 | 0.754 | +0.018 |
| `turnsWithAShotPct` | 54.14 | 55.01 | +0.87 |

**The dial's own claim — that it cannot move the decision-density instrument by construction — is
observed, not repeated.** The three `*ChoicesPerArmedSoldierTurn` fields move by 0.002-0.008, i.e.
by nothing. The two "walk to a zone" objectives (Escort +0.59, Evac +0.73) drift, and the drift is a
population effect rather than a pace effect: those two are the objectives a longer-surviving campaign
reaches more often. For scale, the dose X2 REJECTED on this criterion read Escort at 13.66 turns.

## 7. MISSION 1, AND THE OPENER

`P24-MISSIONS32.txt`, 32 sets, identical denominators (n=640) in both arms:

| rung | base m1 | aim m1 | base m2 | aim m2 |
|---|---|---|---|---|
| h0 | 93.4 | 94.7 | 81.2 | 80.5 |
| h4 | 93.9 | 94.2 | 77.2 | 79.7 |
| h6 | 93.3 | 94.2 | 78.4 | 80.1 |
| h8 | 93.3 | 94.2 | 69.8 | 71.5 |

**P23 could say "m1 and m2 are identical in every arm"; P24 cannot and does not.** A LEVEL lever
reaches mission 1 by construction. What it does there is move a 93.3-93.9% conditional up by
**+0.3 to +1.3 points, identically at every rung** — and §3.D's clause is about front-loaded
ANXIETY, so the direction is the safe one. **The clause's actual content is preserved and is still
asserted:** `FORCETEST` leg (C) pins the opener's FORCE flat across rungs 0-8, and the measured m1
conditional stays flat across rungs in both arms (93.3-93.9 base, 94.2-94.7 aim).

**Where the lever actually lands is late.** h0's m6 conditional 61.9 -> 66.9, h6's m5 40.3 -> 48.4,
h4's m6 34.6 -> 42.4 — an accuracy give-back compounds over a campaign, which is why the pooled
campaign effect is several times the per-mission one.

## 8. STALEMATES, AND WHY THEY MAKE THE HEADLINE CONSERVATIVE

All MISSION-arm; **the RUN arm fired 0 times in 6,400 campaigns** — four consecutive ladders now.
Over 6 rungs x 16 sets: base **34/1,920 (1.77%)**, aim **46/1,920 (2.40%)**. The aim arm carries 12
more, which is the autopilot failing to find a finishing line in campaigns that now survive to have
one. A stalemate is scored as a LOSS in both arms, so the contrast is **understated**:

| rung | base | base ex-stalemate | aim | aim ex-stalemate | delta | delta ex-stalemate |
|---|---|---|---|---|---|---|
| RECRUIT | 73.4 | 75.6 | 76.6 | 79.0 | +3.1 | +3.5 |
| h0 | 44.4 | 45.5 | 51.9 | 54.1 | +7.5 | +8.6 |
| h2 | 34.7 | 35.5 | 40.0 | 41.0 | +5.3 | +5.6 |
| h4 | 22.8 | 22.9 | 25.6 | 26.2 | +2.8 | +3.3 |
| h6 | 9.4 | 9.5 | 12.2 | 12.4 | +2.8 | +2.9 |
| h8 | 6.6 | 6.6 | 5.9 | 6.0 | −0.6 | −0.7 |

## 9. THE SHAPE — this wave may not flatten what L7 and P23 restored

Computed on the CONSISTENT design (all six rungs at 16 slot sets), because a step between rungs
measured at different n is not a step:

| step | base | **aim** | the band implies |
|---|---|---|---|
| RECRUIT -> h0 | 29.0 | **24.7** | 20 |
| h0 -> h2 | 9.7 | **11.9** | 15 |
| h2 -> h4 | 11.9 | **14.4** | 10 |
| h4 -> h6 | 13.4 | **13.4** | 10 |
| h6 -> h8 | 2.8 | **6.3** | 10 |

| dispersion metric | base | **aim** |
|---|---|---|
| mean deviation from band centre, five heat rungs | −7.42 | **−3.88** |
| SD of the five steps | 9.64 | **6.68** |
| L1 from the band's implied profile | 26.8 | **19.3** |
| L1 from even spacing | 31.4 | **21.6** |

**Every dispersion metric improves, and the two steps that moved are the two the band says are
wrong**: `RECRUIT -> h0` (29.0 against 20) shrank and `h6 -> h8` (2.8 against 10) grew. **The step
P23 bought is not flattened — it is the one that grew most.** These are descriptive statistics over
point estimates whose per-rung contrasts do not resolve; they are a shape reading, not a resolved
claim. C1's own table recorded that *no lever beat its control on dispersion*; this one does, on a
different tree and a different metric set.

## 10. THE FOURTH LAYER OF THE MEASUREMENT CONTRACT

Until this wave the only record of which ARM a chunk belonged to was its **file name**. That is not
an artifact, and this project has twice been bitten by a chunk that did not measure what its name
said (P15's silent heat fallback; C4's shipped-is-not-the-measured layer). The balance JSON now
carries a `levers{}` block — `aimTrim`, `toughness`, `damageTrim`, `enemyBase`, `openerTrim`,
`forceCeiling`, `clampLast`, `finaleHeatStat`, `midTooth` — and both `run_round.sh` and
`chunks.sh` **assert `levers.aimTrim` against the arm the chunk was launched as**. All 320 chunks
pass it. `chunks.sh` re-derives every assertion from the artifacts on disk rather than from the
runner's stdout, so `P24-chunks.txt` survives a re-run or a lost log.

## 11. What is in this directory

| file | what |
|---|---|
| `P24-{base,aim}-h{-1,0,2,4,6,8}-b{0..310}.json/.report.txt` | **the round** — 320 chunks, 6,400 campaigns, pinned. |
| `P24-chunks.txt` | every chunk's assertion line, **re-derived from the artifacts** by `chunks.sh`. |
| `P24-BRIDGE.txt` `P24-ARMS16.txt` `P24-ARMS32.txt` `P24-ARMS-OOS.txt` | the tables above, as the tools printed them. |
| `P24-MISSIONS16.txt` `P24-MISSIONS32.txt` `P24-TEXTURE.txt` | the per-mission, stalemate and texture readings. |
| `run_round.sh` `chunks.sh` | the runners. The prediction is in `run_round.sh`'s header, above the data. |
| `bridge.py` `arms.py` `missions.py` `texture.py` | the analysis. |

## 12. Method

```bash
cd /home/user/wt/band-correction
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg"; export XDG_CONFIG_HOME="$PWD/.xdg"
dotnet build -c Release && mkdir -p runbin/p24 && cp -r bin/Release/net8.0/. runbin/p24/

JOBS=4 bash docs/measurements/p24/run_round.sh                        # 192 chunks, 6 rungs x 16 sets
RUNGS="0 4 6 8" BASES="160 170 180 190 200 210 220 230 240 250 260 270 280 290 300 310" \
  JOBS=4 bash docs/measurements/p24/run_round.sh                      # 128 chunks, the extension

bash    docs/measurements/p24/chunks.sh   > docs/measurements/p24/P24-chunks.txt
python3 docs/measurements/p24/bridge.py   > docs/measurements/p24/P24-BRIDGE.txt
python3 docs/measurements/p24/bridge.py --rungs 4,6,8 --bases new >> docs/measurements/p24/P24-BRIDGE.txt
python3 docs/measurements/p24/arms.py     > docs/measurements/p24/P24-ARMS16.txt
python3 docs/measurements/p24/arms.py --rungs 0,4,6,8 --bases both > docs/measurements/p24/P24-ARMS32.txt
python3 docs/measurements/p24/arms.py --rungs 0,4,6,8 --bases new  > docs/measurements/p24/P24-ARMS-OOS.txt
python3 docs/measurements/p24/missions.py > docs/measurements/p24/P24-MISSIONS16.txt
python3 docs/measurements/p24/missions.py --rungs 0,4,6,8 --bases both > docs/measurements/p24/P24-MISSIONS32.txt
python3 docs/measurements/p24/texture.py  > docs/measurements/p24/P24-TEXTURE.txt
```

`.log` files are gitignored, as in every archive here; the `.json` and `.report.txt` for all 320
chunks are committed so every table above is re-derivable and every per-campaign outcome is on disk.
