# P10 "THE HELD LANE" — the measured round

**Base commit `4c1ca3a`** (PROGRAM PARALLAX milestone 6, the cue map) **plus this wave's lever.**
Binary snapshots: `runbin/p10` (main round) and `runbin/p10b` (the same tree plus the
`SIGHTLINE_DECLINEWATCH` dial — `R0diag.json` proves it reproduces `p10-on-h0-b0.json`
**byte-for-byte at the default**, `harness{}` excluded, so the two snapshots are one instrument).

Every chunk went through `run_chunk.sh`, which does the three-layer completion check the CLAUDE.md
measurement contract requires: **(a)** `rm -f` the target JSON first, **(b)** check the process
EXIT CODE (2 = no display, nothing written), **(c)** assert the JSON's own `runs` field.
**120 of 120 chunks reported `OK ... runs=40`; zero `BAD`.**

## Command lines

```bash
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
cd /path/to/worktree
dotnet build -c Release && mkdir -p runbin/p10  && cp -r bin/Release/net8.0/* runbin/p10/
# ...add the SIGHTLINE_DECLINEWATCH dial, rebuild...
dotnet build -c Release && mkdir -p runbin/p10b && cp -r bin/Release/net8.0/* runbin/p10b/

# 1. the main round — the LANE, at the shipped Ai.DeclineWatchRatio (0.45)
BIN=runbin/p10  bash docs/measurements/p10/battery.sh 3          # 48 chunks, 1,920 campaigns
python3 docs/measurements/p10/agg.py                             # -> summary.txt

# 2. the instrument-identity check for the dial that round 3 needs
BIN=runbin/p10b LANE=1 bash docs/measurements/p10/run_chunk.sh R0diag 0 0 20

# 3. PRICED AND NOT SPENT — Ai.DeclineWatchRatio at 1.20 and 3.00, lane ON,
#    CRN-paired against the main round's `p10-on-*` arm
BIN=runbin/p10b bash docs/measurements/p10/battery_dw.sh 3       # 48 chunks, 1,920 campaigns
python3 docs/measurements/p10/agg_dw.py                          # -> summary_dw.txt

# 4. THE LANE, MEASURED WHERE THE BRANCH IS NOT STARVED — ratio pinned at 1.20, AILANE flipped
BIN=runbin/p10b bash docs/measurements/p10/battery_dwoff.sh 3    # 24 chunks, 960 campaigns
python3 docs/measurements/p10/agg_lane_unstarved.py              # -> summary_lane_unstarved.txt
```

Design of every round: 3 rungs {h0, h4, h8} x 8 CRN slot bases {0,10,...,70} x
`SIGHTLINE_BALANCE=20` (= 40 campaigns per chunk: 20 slots x greedy + sloppy) =>
**n = 320 campaigns per rung per arm.** Heat is PINNED (`EventCatalog.HeatPinned`, the default in
a batch since THE HEAT PIN), so a rung means the rung.

## 1. The main round — the lane at the shipped ratio: INERT

| rung | LANE ON | LANE OFF | delta | chunk-t(7) | discordant b/c | McNemar p | MDE (pts) |
|---|---|---|---|---|---|---|---|
| h0 | 43.8% | 44.4% | -0.6 | -1.53 | 0/2 | 0.500 | 1.2 |
| h4 | 21.9% | 21.9% | +0.0 | n/a | **0/0** | 1.000 | 1.5\* |
| h8 |  5.3% |  5.3% | +0.0 | n/a | **0/0** | 1.000 | 1.5\* |

\* MDE from the rule-of-three upper bound on the discordant rate (0 discordant observed); at
alpha .05 two-sided, 80% power. **Report the discordant count, not the n** — a paired round's
resolving power comes from the former (ROADMAP, after C2).

**At heat 4 and heat 8 the two arms produced literally identical outcomes in all 320 worlds.**
Not "no significant difference" — no difference at all, campaign for campaign. h0 diverged in 2
worlds of 320, both won by the LANE-OFF arm (exact two-sided p = 0.50). The chunk-paired
t(7) = -1.53 at h0 is arithmetic on those same 2 campaigns and is not a second piece of evidence.

**Why, and it is one line of telemetry:**

| rung | arm | enemy acts | `overwatch` branch | rate | BRACE | lanes held | reactions | paid off |
|---|---|---|---|---|---|---|---|---|
| h0 | on  | 21,874 | 84 | **0.38%** | 120 | 204 | 41 | 20.1% |
| h0 | off | 21,866 | 86 | 0.39% | 120 | 206 | 45 | 21.8% |
| h4 | on  | 27,708 | 41 | **0.15%** | 134 | 175 | 27 | 15.4% |
| h4 | off | 27,708 | 41 | 0.15% | 134 | 175 | 27 | 15.4% |
| h8 | on  | 22,226 | 102 | **0.46%** | 114 | 216 | 70 | 32.4% |
| h8 | off | 22,224 | 102 | 0.46% | 114 | 216 | 70 | 32.4% |

The verb the lever improves is taken once every 200-700 enemy acts. **A quality improvement to a
starved branch has nothing to move.** (`SIGHTLINE_AICOVTEST=6` reads the branch at **87/8404 =
1.04%** and reads *the same 87* with `SIGHTLINE_AILANE=0` — its heat x objective x mission-1 frame
is a different sampling frame from a whole-campaign batch, and both numbers are true of their own
frame. The campaign batch is the one the ladder is drawn through.)

## 2. `Ai.DeclineWatchRatio` — PRICED, NOT SPENT

ROADMAP's standing claim was that a real lane "would justify a much higher `Ai.DeclineWatchRatio`".
Measured, against the main round's own `p10-on-*` arm (same worlds, same binary):

| rung | ratio | win% | `overwatch` branch | branch rate | shots declined | lanes | reactions | vs shipped b/c | p |
|---|---|---|---|---|---|---|---|---|---|
| h0 | 0.45 (shipped) | 43.8% | 84 | 0.384% | 0.77% | 204 | 41 | — | — |
| h0 | 1.20 | **49.4%** | 3,215 | 14.29% | 31.79% | 3,328 | 842 | 70/52 | 0.123 |
| h0 | 3.00 | 49.1% | 3,475 | 15.94% | 34.00% | 3,609 | 867 | 65/48 | 0.132 |
| h4 | 0.45 (shipped) | 21.9% | 41 | 0.148% | 0.55% | 175 | 27 | — | — |
| h4 | 1.20 | **30.9%** | 3,113 | 10.14% | 27.90% | 3,234 | 967 | 56/27 | **0.0019** |
| h4 | 3.00 | 32.2% | 3,288 | 10.83% | 29.20% | 3,413 | 1,003 | 59/26 | **0.0004** |
| h8 | 0.45 (shipped) | 5.3% | 102 | 0.459% | 0.91% | 216 | 70 | — | — |
| h8 | 1.20 | **10.9%** | 4,207 | 14.80% | 36.33% | 4,288 | 1,022 | 30/12 | **0.0079** |
| h8 | 3.00 | 12.2% | 4,218 | 15.00% | 36.58% | 4,315 | 1,117 | 34/12 | **0.0016** |

**The ratio is the lever that feeds the branch — 0.45 -> 1.20 takes it from 0.15-0.46% of acts to
10-15%, a 30-70x rise — and it makes the opponent SIGNIFICANTLY WEAKER: +5.6 / +9.0 / +5.6 points
of player win rate, resolved at h4 (p = 0.0019) and h8 (p = 0.0079).** 3.00 is barely different
from 1.20: the gate saturates.

This reproduces, at n = 320 per rung and with a proper CRN pairing, what C2 could only call
suggestive (51% declines, run completion 55% -> 75%, p = 0.29 on 20 worlds). **ROADMAP's premise
is refuted: a chosen lane does not justify a higher ratio.** The arithmetic C2 wrote down still
holds — a lane pays off ~25-36% of the time and the reaction is a -10 (+15 focused) shot, so a
watch is worth well under half the aimed shot it replaces, and trading more shots for more watches
is a difficulty REDUCTION however well the watch is aimed. **Not spent. `SIGHTLINE_DECLINEWATCH`
is the dial; the shipped value is unchanged at 0.45.**

## 3. THE LANE, MEASURED WHERE THE BRANCH IS NOT STARVED

The main round could not resolve the lane at ~200 lanes a rung. Pin the ratio at the diagnostic
1.20 — 3,100-4,300 lanes per rung per arm — and flip `SIGHTLINE_AILANE`. This is the only sample
in the repository with the power to say what a chosen lane is worth.

| rung | arm | win% | lanes held | reaction shots | paid off | chunk-paired delta | discordant b/c | McNemar p |
|---|---|---|---|---|---|---|---|---|
| h0 | LANE ON  | 49.4% | 3,328 | 842 | **25.3%** | -1.56 +- 2.00 | 22/27 | 0.568 |
| h0 | LANE OFF | 50.9% | 3,454 | 941 | 27.2% | | | |
| h4 | LANE ON  | 30.9% | 3,234 | 967 | **29.9%** | +0.62 +- 1.13 | 23/21 | 0.880 |
| h4 | LANE OFF | 30.3% | 3,113 | 1,114 | 35.8% | | | |
| h8 | LANE ON  | 10.9% | 4,288 | 1,022 | **23.8%** | -0.31 +- 1.00 | 10/11 | 1.000 |
| h8 | LANE OFF | 11.2% | 3,734 | 1,331 | 35.6% | | | |

**A chosen lane trades COVERAGE for ACCURACY, and on this tree the trade is a wash.** The
mechanism is visible in the pay-off column: the cone is blind outside itself, so the watcher fires
on materially fewer of the lanes it holds (35.6% -> 23.8% at h8, a third of the reactions gone),
while `Combat.FocusOwAim` makes each remaining shot +15 to hit. Win rate does not move at
21-49 discordant pairs per rung: -1.6 +- 2.0 / +0.6 +- 1.1 / -0.3 +- 1.0, every McNemar p >= 0.57.
**This is a null with real power behind it, not "not resolved".**

## The caveat that matters most, stated first-class

**The flywheel is structurally blind to the thing the lane exists to create.**
`Game.Autopilot.TileExposure` carries `+18` for a PIKEMAN BRACE lane (`InEnemyBraceLane`) and **no
term at all** for an ordinary enemy overwatch — a standing ROADMAP item that says fixing it must be
a wave of its own with its own R0diag, so P10 deliberately did not touch it (and `InEnemyBraceLane`
kept its `OwBrace` filter through the `WatchCovers` refactor for exactly that reason). So the bot
walks into a chosen cone exactly as blindly as it walked into a 360 watch. **A player who can see
the cone — and after this wave they can, brightly — would route around it.** Every number above is
therefore an *upper* bound on the opponent's strength under the lane, and the counterplay the lane
creates is invisible to this instrument by construction.

## Files

`p10-{on,off}-h{0,4,8}-b{0..70}.json` — the main round (48 chunks).
`p10-{dw120,dw300}-h{0,4,8}-b{0..70}.json` — the ratio probe (48 chunks).
`p10-dw120off-h{0,4,8}-b{0..70}.json` — the unstarved lane contrast (24 chunks).
`R0diag.json` — the instrument-identity chunk.
`summary*.txt` — the aggregator output quoted above.
`lane-on.png` / `lane-off.png` — the same board and seed (`SIGHTLINE_SEED=90210
SIGHTLINE_MISSION=2 SIGHTLINE_LANESHOT=1 SIGHTLINE_SHOT=760`), lever on and off.
