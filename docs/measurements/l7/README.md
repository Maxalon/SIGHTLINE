# L7 — EVERY RUNG: the per-rung ladder, and the rung that buys nothing because the board never hears it

**Base commit: `935d719`** — `main` after PROGRAM PARALLAX milestone 14 (P21 BUILD OWNS THE BOARD).
Binary snapshot `runbin/l7/` (gitignored), Release, built from that commit **before this wave edited
anything**; the wave edited no `src/` file at all. Instrument `SIGHTLINE_BALANCE`, Release snapshot
under `xvfb-run`, greedy+sloppy per slot, **heat PINNED** (`EventCatalog.HeatPinned`).

**336 chunks, every one asserted, zero `BAD` — 6,720 campaigns** (160 ladder + 80 extension + 96 clamp arm). Every chunk goes through
`docs/measurements/p15/run_chunk.sh`, the runner of record, which keeps all three layers of
`CLAUDE.md`'s measurement contract: (a) `rm -f` the target JSON first, (b) the process EXIT CODE,
(c) `p15/check_chunk.py` asserting `runs` against the artifact's own `batch.expectedRuns` **and** the
rung and slot base against what the runner exported **and** `heatLeak.pinned` with zero raised.

## Why this round exists

L6 is the ladder of record and its steps are 26.2 / 8.4 / 12.8 / 11.9 / **2.5**. That last step —
`h6 → h8` buying 2.5 points across two rungs — is the finding L6 could not resolve, because it
sampled six rungs of ten. Its own "what this round does NOT do" says so: *"Locating a flat step is
what C1's per-RUNG ladder is for, and this round sampled six rungs, not ten."*

C1 is the precedent, exactly. It measured all ten rungs for the first time and found that "the
middle is flat" was **two rungs, one of them buying exactly zero**, caused by a `Heat.Mods` row
declaring an `AiTier` the rung below already provided — dead for two programs under a green
`HEATLADDERTEST`, because that test pins the CUMULATIVE vector and a cumulative pin cannot see a
dead row. `SIGHTLINE_MIDTOOTHTEST` was written for that class.

**So: measure every rung, and find out whether `h6 → h8` is one flat rung or two shallow ones.**

## What is in this directory

| file | what |
|---|---|
| `L7-h{R,0..8}-b{0..150}.json/.report.txt` | **the ladder** — 160 chunks, 10 rungs x 16 CRN slot bases, n=320/rung, pinned, shipped defaults. |
| `L7x-h{4..8}-b{160..310}.*` | **the extension** — the top five rungs replicated on 16 slot sets the ladder never saw. 80 chunks. |
| `L7cap-h{6,7,8}-b{0..310}.*` | **the clamp arm** — `SIGHTLINE_ENEMYBASE=2`, 3 rungs x 32 sets. 96 chunks. |
| `L7-chunks.txt` `L7x-chunks.txt` `L7cap-chunks.txt` | the runner's own `OK <tag> runs=20` line for all 336 chunks (160 / 80 / 96) — the completion assertions, verbatim. |
| `L7-LADDER.txt` `L7-BUYS.txt` `L7-EXT.txt` `L7-CAP.txt` `L7-MECH.txt` `L7-L6CHECK.txt` | the tools' output, as quoted below. |
| `m6-force-by-heat.png` | **the evidence image** — the finale's HUD strip at heats 0-8 on one seed. |
| `run_ladder.sh` `run_ext.sh` `run_cap.sh` | the runners. |
| `cluster.py` `steps.py` `ext.py` `cap.py` `mech.py` `l6check.py` | the analysis. |

Raw round committed in full (JSON + report per chunk) so every table is re-derivable and every
per-campaign outcome is on disk. `.log` files are gitignored, as in every archive here.

## Method

```bash
cd /home/user/wt/ladder-l7
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg"; export XDG_CONFIG_HOME="$PWD/.xdg"
dotnet build -c Release && mkdir -p runbin/l7 && cp -r bin/Release/net8.0/. runbin/l7/   # from 935d719

bash docs/measurements/l7/run_ladder.sh ladder          # 160 chunks: 10 rungs x 16 bases x N=10
bash docs/measurements/l7/run_ext.sh                    #  80 chunks: rungs 4-8 x 16 NEW bases
bash docs/measurements/l7/run_cap.sh                    #  48 chunks: rungs 6-8, ENEMYBASE=2, bases 0-150
BASES="160 170 180 190 200 210 220 230 240 250 260 270 280 290 300 310" \
  bash docs/measurements/l7/run_cap.sh                  #  48 chunks: the same arm on the NEW bases

python3 docs/measurements/l7/cluster.py L7              # the ten-rung ladder + LEAK-CHECK + stalemates
python3 docs/measurements/l7/steps.py   L7              # the BUYS row, three ways
python3 docs/measurements/l7/ext.py                     # the out-of-sample replication
python3 docs/measurements/l7/cap.py                     # the clamp arm
python3 docs/measurements/l7/mech.py    L7              # the mechanism view
python3 docs/measurements/l7/l6check.py                 # L7 vs L6, chunk for chunk

# the evidence image: the finale, one seed, every rung
for H in 0 1 2 3 4 5 6 7 8; do
  SIGHTLINE_MISSION=6 SIGHTLINE_HEAT=$H SIGHTLINE_SEED=4242 SIGHTLINE_SHOT=760 \
    xvfb-run -a -s "-screen 0 1280x800x24" runbin/l7/Sightline; cp sightline_shot.png m6-h$H.png
done      # then crop the HUD strip (520,8)-(1110,36) of each and stack -> m6-force-by-heat.png
```

## THE CONSISTENCY CHECK, FIRST — and it is also P21's inertness check

L7's base is one milestone later than L6's (`935d719` vs `6a6ebee`). Exactly one wave separates
them: **P21 BUILD OWNS THE BOARD**, which claims to be live-path inert (`Mission.ClearHazardsOnBuild`
clears arrays `Game.SetupMission` has always cleared 28 lines earlier, and four `Run` routing prices
went from `const int` to `static int` holding the same shipped values). Same protocol, same runner,
same 16 slot bases, same N, same pin:

| rung | chunks identical | legs identical | L6 % | L7 % | n_disc |
|---|---|---|---|---|---|
| RECRUIT | 16/16 | 320/320 | 70.6 | 70.6 | 0 |
| h0 | 16/16 | 320/320 | 44.4 | 44.4 | 0 |
| h2 | 16/16 | 320/320 | 35.9 | 35.9 | 0 |
| h4 | 16/16 | 320/320 | 23.1 | 23.1 | 0 |
| h6 | 16/16 | 320/320 | 11.2 | 11.2 | 0 |
| h8 | 16/16 | 320/320 | 8.8 | 8.8 | 0 |
| **total** | **96/96** | **1,920/1,920** | | | **0** |

**L7 reproduces L6 campaign for campaign.** So: P21 is campaign-inert as it claimed; the CRN chain is
intact across milestone 14; **L6's published numbers are hereby certified valid on `935d719`**; and
L7 is a per-rung SUPPLEMENT on the same tree, not a new ladder of record. It supersedes nothing.

## THE TEN-RUNG LADDER — 16 clusters of 20 per rung, n=320

| rung | RECRUIT | h0 | h1 | h2 | h3 | h4 | h5 | h6 | h7 | h8 |
|---|---|---|---|---|---|---|---|---|---|---|
| **win%** | **70.6** | **44.4** | **41.9** | **35.9** | **26.2** | **23.1** | **22.5** | **11.2** | **6.6** | **8.8** |
| binomial SE | 2.55 | 2.78 | 2.76 | 2.68 | 2.46 | 2.36 | 2.33 | 1.77 | 1.38 | 1.58 |
| **cluster SE** | 2.41 | 2.13 | 3.12 | 2.89 | 2.17 | 3.09 | 1.37 | 2.17 | 1.27 | 1.41 |
| jackknife | 69.3–71.7 | 43.3–45.7 | 40.7–43.7 | 34.3–37.0 | 25.0–27.3 | 21.3–24.3 | 22.0–23.3 | 10.0–12.0 | 5.7–7.0 | 8.0–9.3 |
| band | 67–83 | 47–63 | — | 32–48 | — | 22–38 | — | 12–28 | — | 5–15 |
| verdict | IN +3.6 | **OUT −2.6** | — | IN +3.9 | — | IN +1.1 | — | **OUT −0.8** | — | IN +3.8 |
| **rung N buys** | — | **26.2** | **2.5** | **5.9** | **9.7** | **3.1** | **0.6** | **11.2** | **4.7** | **−2.2** |

**The odd rungs have no band of record.** The goal band (FUL-13) was written for the six historically
sampled rungs and nobody has ever set one for h1/h3/h5/h7, so `cluster.py` prints `—` rather than
interpolating a band and then grading against it. Inventing four bands inside a measurement round
would be exactly the quiet over-claim this project keeps catching.

**THE LADDER IS NOT MONOTONE. `h7 → h8` is −2.2: the apex rung reads EASIER than the rung below it.**
Every published ladder in this project has been monotone at every step — and every one of them
sampled `{R,0,2,4,6,8}`, which steps straight over h7. **L6's "h6 → h8 buys 2.5" is one real rung and
one rung that buys nothing:** rung 7 buys +4.7 and rung 8 buys −2.2.

`LEAK-CHECK: PASS` — all 160 chunks pinned, `campaignsRaised = missionsAbovePin = 0` on every one
(13,124 missions). The bot still took a heat-raising arm 248 times in these 3,200 campaigns; the pin
nulls the OUTCOME, not the choice.

## WHICH STEPS ARE RESOLVED, AND WHICH ARE NOT

The rungs are CRN-paired to each other, and this is not an assumption: `Program.cs` calls
`Util.Reseed(50000 + slot)` **before** the `Game` is constructed and dials the rung in through an
environment variable read at `StartMission`, so slot *s* plays the same world seed at every rung.
Slot *s* at h5 and slot *s* at h6 are the same campaign with one rung of difference. So each step is
priced three ways — C1's independent-SE difference, a McNemar over the paired campaigns with its
discordant count and MDE, and the same step averaged over the 16 slot sets with a cluster *t*:

| step (rung) | buys | ind. SE | b | c | **n_disc** | **MDE(80%)** | McNemar z | chunk t(15) | verdict |
|---|---|---|---|---|---|---|---|---|---|
| R→h0 | +26.2 | 3.77 | 115 | 31 | 146 | 10.6 | +6.95 | +9.15 | **RESOLVED** |
| h0→h1 (1 REINFORCED) | +2.5 | 3.91 | 50 | 42 | 92 | 8.4 | +0.83 | +0.70 | not resolved |
| h1→h2 (2 HARDENED) | +5.9 | 3.85 | 62 | 43 | 105 | 9.0 | +1.85 | +1.85 | not resolved |
| h2→h3 (3 SHORT FUSE) | +9.7 | 3.64 | 69 | 38 | 107 | 9.1 | +3.00 | +3.22 | **RESOLVED** |
| h3→h4 (4 ELITE CADRE) | +3.1 | 3.41 | 47 | 37 | 84 | 8.0 | +1.09 | +0.92 | not resolved |
| h4→h5 (5 LINGERING WOUNDS) | +0.6 | 3.32 | 39 | 37 | 76 | 7.6 | +0.23 | +0.24 | not resolved |
| h5→h6 (6 EXPOSED) | +11.2 | 2.93 | 60 | 24 | 84 | 8.0 | +3.93 | +5.08 | **RESOLVED** |
| h6→h7 (7 RELENTLESS) | +4.7 | 2.24 | 28 | 13 | 41 | 5.6 | +2.34 | +2.17 | not resolved |
| h7→h8 (8 NO QUARTER) | **−2.2** | 2.10 | 14 | 21 | 35 | 5.2 | −1.18 | −1.28 | not resolved |

**Three of nine steps are resolved at n=320/rung.** That is the honest headline about this
instrument: a rung's tooth is worth 3–6 points and a 16-set rung can only resolve 5–10, so **six of
the ladder's nine steps are absences of evidence, not measured sizes.** Anyone ranking all nine is
reading noise. A step below its own MDE is not a zero either — `h4→h5`'s +0.6 excludes nothing
larger than ±7.6.

**The absolute scale cannot tell a flat rung from a floor**, so every step also gets a scale-free
odds ratio (Woolf SE, 0.5 continuity correction; OR < 1 = the rung cut the player's odds):

| step | R→h0 | h0→h1 | h1→h2 | h2→h3 | h3→h4 | h4→h5 | h5→h6 | h6→h7 | h7→h8 |
|---|---|---|---|---|---|---|---|---|---|
| **OR** | 0.33 | 0.90 | 0.78 | 0.64 | 0.85 | **0.97** | 0.44 | 0.56 | **1.36** |
| lnOR z | −6.62 | −0.64 | −1.54 | −2.63 | −0.91 | −0.19 | −3.72 | **−2.04** | +1.03 |

The odds view **resolves `h6→h7`** (rung 7 really does cut the player's odds, z = −2.04) where the
absolute step could not — 11.2 → 6.6 is only 4.7 points because there are only 11 points left to
take. It leaves rungs 1, 4, 5 and 8 unresolved on both scales, and it puts rung 8 on the wrong side
of 1.0.

## THE EXTENSION — the two flat steps, replicated on 16 slot sets the round never saw

The ladder round leaves the two steps this wave exists to diagnose unresolved, so rungs 4–8 were
re-run on bases 160–310 (disjoint from 0–150 by construction: slot = base + i, i ∈ 0..9). Reported
**new sets first**, which is the point: a step that reverses sign out of sample has not been
measured, however good the pooled p-value looks. This is L5's split-half and L6's P20 re-price.

| rung | old 16 sets | **new 16 sets** | pooled 32 (n=640) |
|---|---|---|---|
| h4 | 23.1 | 26.9 | 25.0 |
| h5 | 22.5 | 22.8 | 22.7 |
| h6 | 11.2 | 10.6 | 10.9 |
| h7 | 6.6 | 7.5 | 7.0 |
| h8 | 8.8 | 6.6 | 7.7 |

| step | old 16 | **new 16** | pooled 32 | n_disc (32) | MDE(80%) | McNemar z | chunk t(31) | sign replicated? |
|---|---|---|---|---|---|---|---|---|
| h4→h5 (rung 5) | +0.6 | +4.1 | **+2.3** | 167 | 5.7 | +1.16 | +1.42 | yes |
| h5→h6 (rung 6) | +11.2 | +12.2 | **+11.7** | 171 | 5.7 | +5.74 | +7.56 | yes — **RESOLVED** |
| h6→h7 (rung 7) | +4.7 | +3.1 | **+3.9** | 85 | 4.0 | **+2.71** | **+2.87** | yes |
| h7→h8 (rung 8) | −2.2 | **+0.9** | **−0.6** | 70 | 3.7 | −0.48 | −0.55 | **NO** |

**The non-monotone sign did NOT replicate.** Out of sample rung 8 reads +0.9; pooled over 32 sets it
is **−0.6 with an MDE of 3.7**. So the correct statement is not "rung 8 makes the game easier" — that
was one draw of sixteen clusters, and this project has now caught that shape four times (L5's
split-half, W2's band floor, L6's P20 re-price, and here). The correct statement is:

> **NO QUARTER, the apex of the difficulty ladder, buys nothing. −0.6 points at n=640, n_disc = 70,
> MDE 3.7. It is the flattest rung on the ladder and the only one whose point estimate is negative.**

Rung 7 at 32 sets is +3.9 with McNemar z = +2.71 and chunk t(31) = +2.87 — **both reject zero at
p ≈ 0.007, while the point estimate sits 0.1 under this round's own MDE threshold of 4.0.** Those two
statements are not in conflict: MDE is a design quantity (the effect detectable with 80% power), the
test is an inference. Rung 7 is a real rung; call it marginal, not measured-to-a-decimal.

## THE CAUSE — located, and it is NOT a dead declaration

`SIGHTLINE_MIDTOOTHTEST` is **green on this tree** (verified, this wave): no dead `AiTier`, no silent
rung, and rung 8 does publish `EnemyDelta 4 / StatDelta 4` into the cumulative vector. The vector is
right. **The board never receives it.**

`Mission.Build` sizes a force as

```csharp
int count = Math.Clamp(EnemyBaseCount + n + enemyDelta, 3, 12);   // EnemyBaseCount = 4
...
if (n >= Run.MaxMissions)                                          // THE FINALE
{
    count = Math.Max(5, count - (Combat.MissionFaction != Faction.None && Ai.Tier >= 1 ? 3 : 4));
    bump  = Math.Max(0, n - 1);            // drop the boss-card/heat StatDelta for the screen
}
```

At mission 6 the requested headcount is `4 + 6 + EnemyDelta` = `10 + EnemyDelta`, and the ceiling is
12. **From heat 3 up the request is already at or over the ceiling, so heat's bodies stop arriving.**
And `bump = Math.Max(0, n - 1)` **discards heat's `StatDelta` outright** — that is the line's stated
purpose, and it applies at every rung. The WARLORD's own statline is `14 + n`, heat-free
(`Mission.cs`), and the anti-turtle clock's reinforcements pass `heatStat = 0` (`Game.cs:6841`), so
there is no other channel.

Predicted, then **measured on the artifact** — `SIGHTLINE_MISSION=6 SIGHTLINE_HEAT=<h>
SIGHTLINE_SEED=4242`, one seed, the HUD's own hostile chip (`m6-force-by-heat.png`):

| heat | R | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|---|---|
| EnemyDelta | −1 | 0 | 1 | 1 | 2 | 2 | 3 | 3 | 3 | 4 |
| requested `10+d` | 9 | 10 | 11 | 11 | 12 | 12 | **13** | **13** | **13** | **14** |
| after the clamp | 9 | 10 | 11 | 11 | 12 | 12 | **12** | **12** | **12** | **12** |
| finale trim (`AiTier≥1` ? −3 : −4) | −4 | −4 | −4 | −4 | −4 | −3 | −3 | −3 | −3 | −3 |
| **predicted bodies** | 5 | **6** | **7** | **7** | **8** | **9** | **9** | **9** | **9** | **9** |
| **MEASURED on screen** | — | **6** | **7** | **7** | **8** | **9** | **9** | **9** | **9** | **9** |
| `bump` (stat) | 5 | 5 | 5 | 5 | 5 | 5 | 5 | 5 | 5 | 5 |

**The finale has not grown a hostile since heat 4, and the one body it grew at h3→h4 is the
`Ai.Tier >= 1` trim gate flipping −4 to −3, not an EnemyDelta.** At heats 7 and 8 the two screenshots
are pixel-comparable and **the only difference anywhere on screen is the `HEAT 7` / `HEAT 8` chip** —
same board, same 9 hostiles, same 4-soldier squad, same LEGION kit. (W9 recorded this exact shape for
SKIRMISH: "the red chip was the only difference on screen".)

So on the mission that decides whether a campaign is won, **rung 8's two declared teeth are both
switched off**: the body by the clamp, the stat by `bump`. Rung 8's entire reach is missions 1–4,
where the clamp only partly bites — and its measured effect is exactly that shape (32 sets, mission
win rate by mission number, h7 → h8): m2 78.7 → **69.8**, m3 42.9 → **38.4**, m4 47.5 → 44.8, m5 41.8
→ 36.7, and **m6 16.8 → 23.9, the wrong way.** `avgMissionsCleared` falls 3.12 → 2.88 and the pooled
mission win rate falls 58.4% → 57.7%. **Rung 8 makes the individual mission harder and the campaign
no harder at all.**

**THIS IS A NEW DEFECT CLASS, AND IT IS C1'S WITH THE ARROW REVERSED.** C1's rung declared a value
the cumulative vector could not carry. Rung 8's value reaches the cumulative vector perfectly and is
then clamped or discarded by `Mission.Build`. `HEATLADDERTEST` pins the cumulative vector.
`MIDTOOTHTEST` pins the per-rung deltas and the apex vector. **Neither of them, and nothing else in
`src/`, ever asks what force the board actually builds.** That is why this sat green for two
programs, exactly as C1's did.

### The counterweight nobody has priced

`Heat.IntelBonus(n) = 3n + n²/2` is **accelerating by design** ("the carrot keeps pace with the
steeper difficulty"). Per-rung increments: +3 +5 +5 +7 +7 +9 +9 **+11**. So the reward side of a rung
grows monotonically and un-clamped while the stick's two quantitative components saturate — and the
biggest single carrot on the ladder, +11 intel per cleared mission, is attached to the rung with no
stick left. Measured (n=320/rung): heat-bonus income per run 94.8 → 108.2 and total earned 184.9 →
190.2 from h7 to h8, **despite h8 clearing 0.19 fewer missions**. This is a NAMED, UNPRICED
counterweight, not a measured cause: `Heat.IntelPerLevel` is a `const` with no restore flag, so no
arm in this round could switch it off. It is the first thing a corrective wave should flag.

## THE CLAMP ARM — priced, and honestly

`SIGHTLINE_ENEMYBASE=2` (X2's shipped measurement dial, `Mission.EnemyBaseCount`) lowers the base by
two, which puts m5 and m6 back **under** the ceiling, so rung 8's body lands where it could not
before. It does NOT restore the finale's stat strip — no flag exists for that — so the arm isolates
the BODY half. Rungs 6–8, 32 CRN slot sets each, n=640/rung/arm:

| step | base 4 (shipped, ceiling binds) | base 2 (ceiling clear) | DiD | SE | t |
|---|---|---|---|---|---|
| h6→h7 (rung 7) | +3.9 (z +2.71) | **+9.2 (z +4.49, RESOLVED)** | +5.3 | 2.31 | 2.30 |
| h7→h8 (rung 8) | **−0.6 (z −0.48)** | **+4.1 (z +2.35)** | +4.7 | 1.96 | **2.39** |

**Directionally exactly as predicted before the arm ran** (the prediction is written into
`run_cap.sh`'s header, above the data). Two caveats, both recorded straight:

1. **On the odds scale it is not resolved.** Base 2 wins more, so it has more points to lose, and the
   absolute DiD flatters it. Odds ratios: rung 8 reads **OR 1.10 at base 4** against **OR 0.70 at base
   2**, ratio 0.64, **z = −1.66 (p ≈ 0.10)**. The absolute-scale t of 2.39 and the odds-scale z of
   −1.66 are the same data on two scales, and the honest verdict is the weaker one: **consistent with
   the clamp, not resolved.**
2. **Relieving the ceiling does not make the FINALE respond to heat.** In the base-2 arm the m6
   conditional still reads h7 25.5% vs h8 27.5% — the wrong way again — and all of rung 8's recovered
   +4.1 comes from missions 2–5 (m2 91.1 → 81.5, m3 59.4 → 46.5, m4 57.7 → 49.3, m5 44.7 → 36.4).
   Because the arm cannot touch `bump`, the finale keeps its stat strip in both arms. **The clamp is
   half the mechanism; the finale's `bump` is the other half and this round could not price it.**

The two arms share slot sets but not worlds (base 2 changes the force from mission 1), so the DiD is
chunk-paired on the slot SET and its SE is the two cluster SEs in quadrature, not a McNemar SE.

## THE SECOND FLAT RUNG — rung 5, and C1's hypotheses half-answered

Rung 5 (LINGERING WOUNDS: +1 enemy, HarshAttrition) buys **+2.3 at n=640, MDE 5.7 — not resolved**,
having read +0.6 on the ladder's sets and +4.1 out of sample. C1 measured it at 0.0 ±3.2 on its own
tree and left two unmeasured hypotheses. L7 can now settle one of them:

* **"the +1 body is partly eaten by the 12-hostile spawn cap on late missions" — CONFIRMED for the
  finale, on the artifact.** h4 and h5 both field **9** hostiles at mission 6 (the table above): rung
  5's body is the first one the clamp eats. It is also eaten on ELITE nodes from mission 4
  (`EnemyDelta` card +2 pushes the request to 12 at both rungs).
* **"HarshAttrition compounds over a run length the bot rarely reaches" — still unmeasured.**
  `avgMissionsCleared` at h5 is 3.56, so most campaigns end before wounds have had three missions to
  linger, but no arm in this round isolates the flag.

## THE MECHANISM VIEW (n=320/rung, ladder sets)

| rung | R | h0 | h1 | h2 | h3 | h4 | h5 | h6 | h7 | h8 |
|---|---|---|---|---|---|---|---|---|---|---|
| run win% | 70.6 | 44.4 | 41.9 | 35.9 | 26.2 | 23.1 | 22.5 | 11.2 | 6.6 | 8.8 |
| avg missions cleared | 5.27 | 4.23 | 4.20 | 4.12 | 3.69 | 3.69 | 3.56 | 3.21 | 3.08 | 2.89 |
| **mission** win% | 85.9 | 75.2 | 73.8 | 71.7 | 66.6 | 66.3 | 65.4 | 60.9 | 58.2 | **57.6** |
| force at start (mid-run) | 5.40 | 6.17 | 6.96 | 6.95 | 7.99 | 8.00 | 8.66 | 8.69 | 8.66 | 9.52 |
| squad losses / mission | 0.89 | 1.68 | 1.72 | 1.88 | 2.11 | 2.00 | 2.17 | 2.31 | 2.20 | 2.27 |
| ch/ARMED | 2.220 | 2.222 | 2.320 | 2.305 | 2.402 | 2.453 | 2.485 | 2.110 | 2.072 | 2.091 |

**The MISSION-level ladder is monotone at every one of the nine steps, including `h7 → h8`.** Only the
CAMPAIGN-level one breaks. That is the whole finding in one row: rung 8 raises the price of every
mission on the way and adds nothing to the gate at the end.

The `force` row is the clamp's fingerprint from the other side: it moves +0.79 / 0.00 / +1.04 / +0.01
/ +0.66 / +0.03 / −0.03 / +0.86 across the nine steps — up on exactly the four rungs that declare a
body (1, 3, 5, 8), flat on the five that do not. Rung 8's +0.86 is a full body short of +1 because the
clamp has already begun eating it mid-run.

## STALEMATES

3,200 campaigns of the ladder round: **mission arm 54, run arm 0, share 1.69%** — in line with L6's
1.77% and L5's 1.41%. **The run arm has now fired ZERO times in three consecutive ladders (L5, L6, L7).** By rung:
R 9, h0 8, h1 11, h2 7, h3 5, h4 1, h5 2, h6 5, h7 2, h8 4. `runTurns` at the stall 51–90 against a
cap of 150.

**Slot 46's mission-1 Eliminate stalls at SEVEN of the ten rungs here** (h0/h1/h2/h3 sloppy,
h6/h7/h8 greedy, `runTurns` 51 every time), after stalling at four rungs in L6 and at three in L5. It has now
survived a board-moving wave, both policies and every rung: it is a property of that opener, not of a
rung or a policy, and it is the most reproducible autopilot deadlock anyone will get —
`SIGHTLINE_BALANCE_BASE=40`, slot 46. Still autopilot work, as L5 and L6 both said.

## What this round does NOT do

* **Ships no corrective lever, and no gameplay source was edited.** L4, L5 and L6 all found rungs out
  of band or flat and all declined to repair inside a measurement round; so does this one. The fix
  for the finale is a design decision (does the apex deserve a body the clamp cannot eat, or a stat
  the finale does not discard, or a smaller carrot?) and it needs its own wave, one lever at a time,
  against a fresh baseline.
* **Does not resolve six of the nine steps.** At n=320/rung the MDE is 5–10 points and a rung's tooth
  is worth 3–6. Rungs 1, 2, 4, 5 and 8 are absences of evidence. Only the extension's rungs got to
  n=640, and even there rung 5 (MDE 5.7) and rung 8 (MDE 3.7) stay unresolved.
* **Does not price the finale's `bump` strip.** `Math.Max(0, n - 1)` has no restore flag, so no arm
  here could switch it on. That is the other half of the mechanism and it is the bigger half: the
  clamp is relieved by an existing dial and the strip is not.
* **Does not price `Heat.IntelBonus`.** `Heat.IntelPerLevel` is a `const`. Named as a counterweight,
  measured as an income, not attributed as a cause.
* **Does not measure FEEL.** A CRN round prices consequences. Whether the apex *reads* like an apex —
  the same blind spot C1 recorded against `AiTier 2` — is untouched, and nobody has looked.
* **Sets no band for h1/h3/h5/h7.** Four rungs of the shipped ladder still have no goal of record.
