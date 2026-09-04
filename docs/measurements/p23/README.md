# P23 — THE APEX BITES: the finale hears the ladder, in two independently switchable halves

**Base commit: `fd07d56`** — `main` after PROGRAM PARALLAX milestone 16 (L7 EVERY RUNG). Binary
snapshot `runbin/p23/` (gitignored), Release, built from this wave's own tree. Instrument
`SIGHTLINE_BALANCE`, Release snapshot under `xvfb-run`, greedy+sloppy per slot, **heat PINNED**
(`EventCatalog.HeatPinned`).

Every chunk goes through `docs/measurements/p15/run_chunk.sh`, the runner of record, which keeps all
three layers of `CLAUDE.md`'s measurement contract: (a) `rm -f` the target JSON first, (b) the
process EXIT CODE, (c) `p15/check_chunk.py` asserting `runs` against the artifact's own
`batch.expectedRuns` **and** the rung and slot base against what the runner exported **and**
`heatLeak.pinned` with zero raised.

## Why this round exists, and why it has FOUR arms

L7 located the defect: `Mission.SpawnEnemies` clamped the headcount to the board ceiling **before**
the finale's de-stack subtracted from it, and then reset `bump` to the bare per-mission growth. So
the apex rung's two declared teeth — +1 body, +1 stat — were both switched off on mission 6, the
mission that decides a campaign. The measured finale force at heats 0-8 was **6/7/7/8/9/9/9/9/9**:
no growth since heat 4.

L7 also priced a **partial** relief and could not resolve it. Its arm was `SIGHTLINE_ENEMYBASE=2`,
which eases the ceiling but cannot touch the stat strip (no flag existed), so the two halves were
confounded inside one arm: rung 8 bought +4.1 with the ceiling clear against −0.6 with it binding,
but **z = −1.66 on the odds scale, not resolved**, and the whole recovery came from missions 2-5
rather than from the finale.

P23 ships the two halves as **two dials** precisely so this round can put each on its own arm:

| arm | env | what it is |
|---|---|---|
| **base** | `SIGHTLINE_CLAMPLAST=0 SIGHTLINE_FINALESTAT=0` | the pre-P23 tree — the control |
| **A** | `SIGHTLINE_FINALESTAT=0` | LEVER A alone — the ceiling applied to the SEATED board, not to the request |
| **B** | `SIGHTLINE_CLAMPLAST=0` | LEVER B alone — the finale keeps heat's StatDelta |
| **AB** | *(none)* | the shipped tree |

All four arms share the same 16 CRN slot bases, so every contrast is paired campaign-for-campaign
and a chunk of one arm is directly comparable with the same chunk of another.

**The rungs.** `h0` and `h2` are INERTNESS controls: lever A cannot fire below heat 5 on a plain
route (the request only crosses the ceiling at `EnemyDelta >= 3`) and lever B is a no-op at
`StatDelta 0`. A non-zero contrast there is a bug, not a result. `h4/h6/h8` are where both act; the
derivation is `SIGHTLINE_FORCETEST=1 SIGHTLINE_FORCEDUMP=1`'s own matrix, reproduced below.

## What is in this directory

| file | what |
|---|---|
| `P23-{base,A,B,AB}-h{0,2,4,6,8}-b{0..150}.json/.report.txt` | **the round** — 4 arms x 5 rungs x 16 CRN slot bases, n=320 per arm-rung, pinned. |
| `P23-chunks.txt` | the runner's own `OK <tag> runs=20` line for every chunk — the completion assertions, verbatim. |
| `P23-BRIDGE.txt` `P23-ARMS.txt` `P23-MISSIONS.txt` `P23-FORCE.txt` | the tools' output, as quoted below. |
| `m6-force-by-heat-p23.png` | **the evidence image** — the finale's HUD strip at heats 0-8, BEFORE and AFTER, one seed. |
| `run_round.sh` `shot.sh` | the runners. |
| `bridge.py` `arms.py` `permission.py` `stack.py` | the analysis. |

Raw round committed in full (JSON + report per chunk) so every table is re-derivable and every
per-campaign outcome is on disk. `.log` files are gitignored, as in every archive here.

## Method

```bash
cd /home/user/wt/apex-bites
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg"; export XDG_CONFIG_HOME="$PWD/.xdg"
dotnet build -c Release && mkdir -p runbin/p23 && cp -r bin/Release/net8.0/. runbin/p23/

RUNGS="0 2 4 6 8" JOBS=4 bash docs/measurements/p23/run_round.sh   # 320 chunks, 6,400 campaigns

python3 docs/measurements/p23/bridge.py     > docs/measurements/p23/P23-BRIDGE.txt
python3 docs/measurements/p23/arms.py --rungs 0,2,4,6,8 > docs/measurements/p23/P23-ARMS.txt
python3 docs/measurements/p23/permission.py --rungs 0,2,4,6,8 > docs/measurements/p23/P23-MISSIONS.txt
bash docs/measurements/p23/shot.sh
```

**Totals: 544 chunks, every one asserted by `p15/check_chunk.py`, zero `BAD` — 10,880 campaigns,
43,537 missions, `LEAK-CHECK PASS` (0 off-rung, 0 campaigns raised).** The round is 320 chunks on
16 slot bases (4 arms x 5 rungs), a 192-chunk EXTENSION on 16 slot bases the round never saw
(4 arms x h4/h6/h8 x bases 160-310), and a 32-chunk RECRUIT arm (base + AB).
`P23-chunks.txt` is re-derived from the artifacts themselves rather than from the runner's stdout,
so it asserts all 544 in one pass.

## THE CONSISTENCY CHECK, FIRST — and it certifies two other things for free

P23's BASE arm is the shipped binary with both dials off, i.e. the pre-P23 arithmetic. Its base
commit is one milestone past L7's and exactly one wave separates them (P22 NOTHING WITHOUT A SWITCH,
which claims to be default-inert). Same runner, same bases, same N, same pin — so the five rungs it
shares with the L7 archive are a direct reproduction test (`P23-BRIDGE.txt`):

| rung | h0 | h2 | h4 | h6 | h8 | total |
|---|---|---|---|---|---|---|
| chunks identical | 16/16 | 16/16 | 16/16 | 16/16 | 16/16 | **80/80** |
| legs identical | 320/320 | 320/320 | 320/320 | 320/320 | 320/320 | **1,600/1,600** |
| P23 base % | 44.4 | 35.9 | 23.1 | 11.2 | 8.8 | |
| L7 archive % | 44.4 | 35.9 | 23.1 | 11.2 | 8.8 | |

**BRIDGE INTACT.** So P22 is inert as it claimed; P23's own non-lever edits (the `Floor` witness on
the five trims, the `LastForce*` telemetry, the new `heatStat` parameter) are stream-neutral with the
dials off; and the CRN chain reaches back through milestones 15 and 16 to L6, the ladder of record.

## THE FORCE ON THE BOARD — measured on the artifact, before and after

`SIGHTLINE_MISSION=6 SIGHTLINE_HEAT=<h> SIGHTLINE_SEED=4242`, the HUD's own hostile chip
(`m6-force-by-heat-p23.png`), and the same numbers derived by `SIGHTLINE_FORCETEST=1
SIGHTLINE_FORCEDUMP=1` for all four arms (`P23-FORCE.txt`):

| heat | R | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|---|---|
| declared cumulative `EnemyDelta` | −1 | 0 | 1 | 1 | 2 | 2 | 3 | 3 | 3 | 4 |
| declared cumulative `StatDelta` | −1 | 0 | 0 | 1 | 1 | 1 | 1 | 2 | 3 | 4 |
| **m6 bodies BEFORE** | 5 | 6 | 7 | 7 | 8 | 9 | **9** | **9** | **9** | **9** |
| **m6 bodies AFTER** | 5 | 6 | 7 | 7 | 8 | 9 | **10** | **10** | **10** | **11** |
| **m6 `bump` BEFORE** | **5** | 5 | 5 | 5 | 5 | 5 | 5 | 5 | 5 | 5 |
| **m6 `bump` AFTER** | **4** | 5 | 5 | 6 | 6 | 6 | 6 | 7 | 8 | 9 |

Mission 5 also moves, in one cell: 11 -> 12 at heat 8 (the ceiling was eating the apex body there
too, and on ELITE mid-run nodes from heat 3). **Missions 1 and 2 do not move at any rung, in any
arm** — the front-loaded-anxiety contract (`docs/DESIGN.md` §3.D), asserted by `FORCETEST` leg (C)
and confirmed on the flywheel below.

**The seating question, answered rather than assumed.** `FORCETEST` leg (E) builds every
mission x rung x 3 seeds and asserts every hostile sits on a DISTINCT tile reachable from the squad.
Shipped, the worst case the board is ever asked to seat is **12 of a ceiling of 12**. With the
ceiling stressed to **16** and `EnemyBaseCount` to 8 — asking missions 4-6 for 13-16 bodies — the
board still seats **16**, all distinct, all reachable. **So 12 is not a layout constraint.** P23 did
not raise it anyway: the defect was the ORDER, and the order fix needs no extra seat.
`SIGHTLINE_FORCECEILING=<n>` is priced and unspent.

## THE ARMS

16 slot bases, n=320 per arm-rung (`P23-ARMS.txt`):

| rung | base | A (order) | B (stat) | AB (shipped) | FUL-13 band |
|---|---|---|---|---|---|
| **RECRUIT** | 70.6 | — | — | **73.4** | 67-83 |
| **h0** | 44.4 | 44.4 | 44.4 | **44.4** | 47-63 |
| **h2** | 35.9 | 35.9 | 34.7 | **34.7** | 32-48 |
| **h4** | 23.1 | 22.5 | 22.2 | **22.8** | 22-38 |
| **h6** | 11.2 | 10.0 | 10.3 | **9.4** | 12-28 |
| **h8** | 8.8 | 8.4 | 7.5 | **6.6** | 5-15 |

**The inertness controls come back EXACT.** At h0 every one of the five contrasts is **0 discordant
campaigns in 320** — not "no effect measured", no campaign came out differently at all. At h2,
lever A is likewise 0 discordant in 320 (its request only crosses the ceiling at `EnemyDelta >= 3`).
That is the prediction `FORCETEST`'s matrix makes, observed on 320 worlds a piece.

32 slot bases (the round plus the extension), n=640 per arm-rung (`P23-ARMS32.txt`):

| rung | base | A | B | AB |
|---|---|---|---|---|
| **h4** | 25.0 | 24.4 | 23.4 | **23.6** |
| **h6** | 10.9 | 9.5 | 9.8 | **9.4** |
| **h8** | 7.7 | 5.8 | 6.6 | **5.0** |

| rung | contrast | eff | b | c | n_disc | MDE80 | z | chunk t(31) | verdict |
|---|---|---|---|---|---|---|---|---|---|
| h4 | base->AB | −1.4 | 24 | 33 | 57 | 3.30 | −1.19 | −1.09 | NOT RESOLVED |
| h6 | base->AB | −1.6 | 30 | 40 | 70 | 3.66 | −1.20 | −1.26 | NOT RESOLVED |
| h8 | base->AB | **−2.7** | 14 | 31 | 45 | 2.93 | **−2.53** | **−2.87** | at the edge — see below |
| h8 | base->A | −1.9 | 19 | 31 | 50 | 3.09 | −1.70 | −1.71 | NOT RESOLVED |
| h8 | base->B | −1.1 | 11 | 18 | 29 | 2.36 | −1.30 | −1.27 | NOT RESOLVED |

**Read the h8 row exactly.** `|−2.7|` is 0.2 under the round's own MDE80 of 2.93, so by the house
criterion it is NOT RESOLVED — this design has 80% power for effects of 2.9 and up. But the
two-sided McNemar test on the same data rejects (z = −2.53, p ≈ 0.011) and so does the chunk-paired
t (t(31) = −2.87, p ≈ 0.007). **The honest statement is that the effect is real at conventional
significance and is the same size as the smallest effect this round was built to see** — a
replication should expect noise around it, and nobody should quote −2.7 as a precise quantity.

**The out-of-sample half agrees, which is the check L5, L6 and L7 all say to run first.** On the 16
NEW slot bases alone (`P23-ARMS-OOS.txt`), h8 base->AB reads **−3.1 with b=1 against c=11
(z = −2.89, RESOLVED)**; on the original 16 it reads −2.2. Sign consistent, magnitude consistent.

**Pooled over the three rungs where either lever can fire** (h4+h6+h8, n=1,920 per arm — pre-
specified by `FORCETEST`'s matrix, not chosen after looking):

| contrast | eff | b | c | n_disc | MDE80 | z |
|---|---|---|---|---|---|---|
| base->A | −1.30 | 56 | 81 | 137 | 1.71 | −2.14 |
| base->B | −1.25 | 45 | 69 | 114 | 1.56 | −2.25 |
| **base->AB** | **−1.88** | 68 | 104 | 172 | 1.91 | **−2.74** |

**The two levers are worth about the same and they compose additively.** The interaction term
(`eff(AB) − eff(A) − eff(B)`) is +0.8 / +0.9 / +0.3 at h4/h6/h8 — small and of the sign that says
"slightly sub-additive", which is what two levers competing for the same losses should look like.

**RECRUIT moves the other way, and that is the point of a symmetric lever.** Lever B gives the
relief rung its declared relief at the finale too (`bump` 5 -> 4), and it reads **70.6 -> 73.4**
(+2.8, n_disc 15, z = +2.32, chunk t(15) = +4.39), with the finale conditional **88.3 -> 91.8** on
256 CRN-paired worlds. Missions 1-5 are identical.

## THE PER-MISSION BREAKDOWN — L7's whole point, and the opener check

32 slot bases, conditional win% / n (`P23-MISSIONS32.txt`):

| rung | arm | m1 | m2 | m3 | m4 | m5 | **m6** |
|---|---|---|---|---|---|---|---|
| h4 | base | 93.9/640 | 77.2/289 | 58.9/533 | 68.4/434 | 57.6/363 | **36.8**/435 |
| h4 | **AB** | **93.9**/640 | **77.2**/289 | **58.9**/533 | **68.4**/434 | 57.0/363 | **34.6**/436 |
| h6 | base | 93.3/640 | 78.4/291 | 50.5/519 | 50.1/403 | 39.4/287 | **25.5**/274 |
| h6 | **AB** | **93.3**/640 | **78.4**/291 | **50.5**/519 | 51.5/400 | 40.3/288 | **20.1**/298 |
| h8 | base | 93.3/640 | 69.8/291 | 38.4/539 | 44.8/344 | 36.7/218 | **23.9**/205 |
| h8 | **AB** | **93.3**/640 | **69.8**/291 | 37.8/542 | 43.5/347 | 37.1/210 | **15.5**/206 |

**MISSION 1 AND MISSION 2 ARE IDENTICAL IN EVERY ARM AT EVERY RUNG, to the cell and the
denominator.** That is the §3.D check, on 640 campaigns a rung, and it is structural rather than
lucky: neither lever's read is reachable before mission 3 (lever A only when the request crosses the
ceiling; lever B only inside `n >= Run.MaxMissions`).

**The finale is where the change lands.** `byMission` denominators are per-arm, so the honest
comparison restricts to worlds where BOTH arms reached mission 6:

| rung | n paired finales | base | AB | eff | n_disc | z |
|---|---|---|---|---|---|---|
| h0 | 182 | 78.0 | 78.0 | **0.0** | **0** | — |
| h4 | 300 | 52.7 | 49.7 | −3.0 | 53 | −1.24 |
| h6 | 172 | 36.6 | 31.4 | −5.2 | 57 | −1.19 |
| h8 | 108 | 36.1 | 26.9 | **−9.3** | 32 | −1.77 |

None of the finale contrasts resolves — a rung's finale is 100-300 paired worlds and its MDE is
7-15 points — but the direction is the same at every rung that can move and the size grows with the
rung, which is the shape the mechanism predicts. **L7's diagnostic quantity is repaired**: it
measured `m6` going **16.8 -> 23.9 the WRONG WAY** across h7 -> h8. Here the finale conditional
falls monotonically with the rung on the shipped tree — 34.6 / 20.1 / 15.5 at h4 / h6 / h8, against
36.8 / 25.5 / 23.9 before.

**Stalemates: 0.9-1.4% per arm, all on the MISSION arm; the RUN arm fired 0 times in 10,880
campaigns** — unchanged from L5/L6/L7.

## WHAT THIS COSTS THE BAND, STATED STRAIGHT AND NOT TUNED AWAY

FUL-13 band: RECRUIT 75 / h0 55 / h2 40 / h4 30 / h6 20 / h8 10, ±8 (h8 ±5, **hard floor >= 5**).

| rung | before | after | band | verdict after |
|---|---|---|---|---|
| RECRUIT | 70.6 | **73.4** | 67-83 | IN, and nearer the centre |
| h0 | 44.4 | **44.4** | 47-63 | OUT −2.6 — **unchanged, inherited** |
| h2 | 35.9 | **34.7** | 32-48 | IN |
| h4 (32 sets) | 25.0 | **23.6** | 22-38 | IN +1.6 |
| h6 (32 sets) | 10.9 | **9.4** | 12-28 | **OUT −2.6** (was OUT −1.1) |
| h8 (32 sets) | 7.7 | **5.0** | 5-15 | IN, but **exactly ON the >= 5 hard floor** |

**Two costs, recorded rather than repaired.** h6 was already under its floor before this wave and
is now 1.5 points further under; h8 lands on its hard floor with no margin. Neither is tuned here,
because **a round that changes a mechanism may not also tune toward the band inside itself** — the
correction is a separate lever with its own baseline, and it is filed in `docs/ROADMAP.md`. The h0
miss (−2.6) is untouched by P23 and is inherited from L5 onward.
