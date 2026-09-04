# L6 — THE LADDER OF RECORD ON THE COMPOSED TREE, AND THE BRIDGE THAT LOCATED ITS OWN BREAK

**Base commit: `6a6ebee`** — `main` after PROGRAM PARALLAX milestone 12 (the second axis, the roster
contests, the stale ground). Binary snapshot `runbin/l6/` (gitignored), Release, copied from
`bin/Release/net8.0` of that commit **before** this wave edited a single source file. Instrument
`SIGHTLINE_BALANCE`, Release snapshot under `xvfb-run`, greedy+sloppy per slot, **heat PINNED**
(`EventCatalog.HeatPinned`) except where a row says otherwise.

**320 chunks, every one asserted, zero `BAD` — 6,400 campaigns.** 224 of them ran on the `6a6ebee`
snapshot and are checked strictly by `p15/check_chunk.py`; the other 96 are the milestone-5 bridge
target, which predates P15 and is `--legacy`-checked (see Method).

## Why this round exists

Wave P20 THE STALE GROUND fixed `Mission.Build` reading the previous mission's ground layer. That
fix moves the board, so `CLAUDE.md` carried a warning that **L5 is a pre-P20 ladder** and an
absolute win rate from it may not be quoted against this tree. P16, P18 and P19 had landed since L5
as well. L6 is the first measurement of the composed tree, on L5's protocol exactly, so that the
method is comparable even where the numbers are not.

## What is in this directory

| file | what |
|---|---|
| `L6-h{R,0,2,4,6,8}-b{0..150}.json/.report.txt` | **the ladder** — 96 chunks, heat pinned, 16 slot sets x 6 rungs, shipped defaults. |
| `L6bridge-h*-b*.*` | **the bridge arm** — the same 96 cells with every post-L5 restore flag set. |
| `bisect/M5-h*-b*.*` | **the bridge target** — the same 96 cells built and run at milestone 5 (`54147dc`). |
| `L6stale-h{0,4}-b*.*` | **P20 alone** — `SIGHTLINE_STALEGROUND=1` at h0/h4 x 16 sets, against the ladder's own chunks. |
| `L6-chunks.txt` `L6bridge-chunks.txt` `L6stale-chunks.txt` `bisect/M5-chunks.txt` | the runner's own `OK <tag> runs=20` line for all 224 chunks — the completion assertions, verbatim. |
| `L6-LADDER.txt` `L6-BRIDGE.txt` `L6-M5BRIDGE.txt` `L6-BISECT.txt` `L6-FORKPAYS.txt` `L6-LEVERS.txt` `L6-P20.txt` `L6-P20-SPLIT.txt` | the tools' output, as quoted below. |
| `run_ladder.sh` `bisect.sh` | the runners. |
| `cluster.py` `rows.py` `pairs.py` | the analysis. |

The raw round is committed in full (JSON + report per chunk) so every table below is re-derivable
and every per-campaign outcome is on disk. `.log` files are gitignored, as in every archive here.

## Method

```bash
cd /home/user/wt/ladder-l6
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg"; export XDG_CONFIG_HOME="$PWD/.xdg"
mkdir -p runbin/l6 && cp -r bin/Release/net8.0/. runbin/l6/          # from commit 6a6ebee

bash docs/measurements/l6/run_ladder.sh ladder      # 96 chunks: 6 rungs x 16 bases x N=10, pinned
bash docs/measurements/l6/run_ladder.sh bridge      # the same 96 cells, every post-L5 lever restored
bash docs/measurements/l6/bisect.sh 7180374 cb58a4b 715e1a0 ad2f6c9 54147dc    # where the chain broke

# the bridge target: milestone 5, built from its own worktree, same 96 cells, its own defaults
git worktree add --detach /tmp/bisect-wt 54147dc && (cd /tmp/bisect-wt && dotnet build -c Release)
mkdir -p runbin/m5 && cp -r /tmp/bisect-wt/bin/Release/net8.0/. runbin/m5/
for H in R 0 2 4 6 8; do for B in 0 10 20 30 40 50 60 70 80 90 100 110 120 130 140 150; do
  hh=$H; [ "$H" = R ] && hh=-1
  BIN=runbin/m5 OUT=docs/measurements/l6/bisect bash docs/measurements/p15/run_chunk.sh "M5-h$H-b$B" "$hh" "$B" 10
done; done

# P20 alone, on the ladder's own 16 slot sets
for H in 0 4; do for B in 0 10 20 30 40 50 60 70 80 90 100 110 120 130 140 150; do
  BIN=runbin/l6 OUT=docs/measurements/l6 EXTRA="SIGHTLINE_STALEGROUND=1" \
    bash docs/measurements/p15/run_chunk.sh "L6stale-h$H-b$B" "$H" "$B" 10
done; done

python3 docs/measurements/l6/cluster.py L6                       # ladder + LEAK-CHECK + stalemates
python3 docs/measurements/l6/cluster.py L6bridge --bridge L5     # the bridge, chunk-for-chunk vs L5
python3 docs/measurements/l6/pairs.py L5 docs/measurements/l5 L6bridge docs/measurements/l6
python3 docs/measurements/l6/pairs.py L6bridge docs/measurements/l6 L6 docs/measurements/l6
python3 docs/measurements/l6/pairs.py L6stale docs/measurements/l6 L6 docs/measurements/l6
python3 docs/measurements/l6/rows.py --check docs/measurements/l6/L6*.json     # ROWS-CHECK: PASS
```

Every chunk goes through **`docs/measurements/p15/run_chunk.sh`, the runner of record** — not
`c1/run_chunk.sh`, which drove L4 and L5 and is superseded. All three layers of `CLAUDE.md`'s
measurement contract are kept and one is stronger: (a) `rm -f` the target JSON first, (b) the
process EXIT CODE (2 = no display, 3 = the batch could not name itself), (c) `check_chunk.py`
asserting `runs` against the artifact's own `batch.expectedRuns` **and** the rung and slot base
against what the runner exported, **and** `heatLeak.pinned` with zero raised.

**The milestone-5 arm is the one exception and it says so out loud.** `54147dc` predates P15, so
its JSONs carry no `batch` block; layer (c) refuses such an artifact rather than guessing, which is
exactly the P15 defect. Those 96 chunks were therefore re-checked explicitly with
`check_chunk.py --legacy --expect 20 --heat H --base B`: **96 OK, 0 BAD**, with the tool printing
"this is an assumption, not a check" for the run count and "UNVERIFIABLE from the artifact" for the
rung. The pin block IS present and verified on all 96 (`CLEAN heat pinned`). What makes that arm
trustworthy is not its own accounting but the 1,920/1,920 outcome match below: a chunk that had
measured the wrong rung could not reproduce the bridge arm campaign for campaign.

`rows.py --check` on all 192 shipped-runner chunks: rows-derived `pairedPolicy.slots` `==` the
chunk's own block and all five tallies equal on every one (`ROWS-CHECK: PASS`).

## THE LADDER — pinned, 16 clusters of 20 per rung, n=320

| rung | **L6 %** | n | binomial SE | **cluster SE** | ratio | jackknife | band | verdict |
|---|---|---|---|---|---|---|---|---|
| RECRUIT | **70.6** | 320 | 2.55 | 2.41 | 0.95 | 69.3–71.7 | 67–83 | IN (+3.6; 1.50 cluster-SE clear) |
| h0 | **44.4** | 320 | 2.78 | 2.13 | 0.77 | 43.3–45.7 | 47–63 | **OUT −2.6** (1.23 cluster-SE under) |
| h2 | **35.9** | 320 | 2.68 | 2.89 | 1.08 | 34.3–37.0 | 32–48 | IN (+3.9; 1.36 clear) |
| h4 | **23.1** | 320 | 2.36 | 3.09 | 1.31 | 21.3–24.3 | 22–38 | IN (+1.1; 0.36 clear) |
| h6 | **11.2** | 320 | 1.77 | 2.17 | 1.23 | 10.0–12.0 | 12–28 | **OUT −0.8** (0.35 cluster-SE under) |
| h8 | **8.8** | 320 | 1.58 | 1.41 | 0.89 | 8.0–9.3 | 5–15 | IN (+3.8; 2.67 clear) |

**Monotone at every step; four of six in band.** Steps RECRUIT→h0 **26.2**, h0→h2 8.4, h2→h4 12.8,
h4→h6 11.9, h6→h8 **2.5**.

**Neither OUT is a measured breach, and neither IN is a robustness claim.** h0 is 1.23 of its own
cluster SE under its floor and h6 is 0.35 under; h4 clears its floor by 0.36 cluster-SE, which is
inside the noise in the other direction. The cluster SE exceeds the binomial at three of six rungs
(h2, h4, h6) and is *smaller* at the other three — the first ladder here where that ratio has gone
both ways, which is a property of this slot draw and not a change in the instrument.

**The shape has a new soft spot and it is not where L5's was.** h6→h8 buys **2.5** points, the
smallest step in the table, and h0→h2 buys 8.4 where L5 read 14.1. The top of the ladder is
compressed against the bottom of it. **This is a finding, not a defect located** — no lever was
moved to chase it, and the per-rung diagnosis C1 did (all ten rungs) is what would locate it.

`LEAK-CHECK: PASS` — all 96 chunks pinned, `campaignsRaised = missionsAbovePin = 0` on every one
(7,928 missions). The bot still took a heat-raising arm 150 times in these 1,920 campaigns
(`heatRaisingPicks` 29/28/27/24/22/20 by rung): the pin nulls the OUTCOME, not the choice.

## THE BRIDGE — it does NOT reproduce L5, and that is the round's most useful result

The bridge arm is the same 96 cells with **every gameplay lever that landed between L5's base
commit and this tree restored to its pre-wave behaviour**. The set is derived, not remembered: the
env-var surface of `src/` at `7180374` was diffed against this tree's, and every new variable that
is a gameplay switch rather than a test hook is in it —
`SIGHTLINE_AILANE=0` (P10), `SIGHTLINE_NEWGROUND=0` (P16), `SIGHTLINE_SECONDAXIS=0`
`SIGHTLINE_PERKPICK=0` `SIGHTLINE_ASSISTLATCH=0` (P18), `SIGHTLINE_ELITEBOSS=0`
`SIGHTLINE_ROSTERID=0` (P19), `SIGHTLINE_STALEGROUND=1` (P20). Deliberately excluded, with reasons:
`SIGHTLINE_MODEDEPTH` (read only under `Mode == GameMode.Skirmish`; a balance batch is campaign),
`SIGHTLINE_BIOMEDEAL` (default off *is* the pre-P16 deal), `SIGHTLINE_MISSIONFLUSH` (a `Stats`
bookkeeping arm — it moves per-mission rows, never an outcome), `SIGHTLINE_DECLINEWATCH` (its
default is the shipped ratio).

**Against L5: 0/96 chunks identical, 1,519/1,920 legs.** The chain is broken. Locating it:

```
7180374  h0-b0=SAME   h0-b30=SAME   h4-b0=SAME  hR-b0=SAME   <- THE CONTROL. L5's own base.
cb58a4b  h0-b0=diff2  h0-b30=diff0  h4-b0=SAME  hR-b0=diff1  <- main WITHOUT the heat pin (see below)
715e1a0  h0-b0=SAME   h0-b30=SAME   h4-b0=SAME  hR-b0=SAME   <- milestone 3
ad2f6c9  h0-b0=SAME   h0-b30=SAME   h4-b0=SAME  hR-b0=SAME   <- milestone 4
54147dc  h0-b0=diff2  h0-b30=diff8  h4-b0=diff5 hR-b0=diff7  <- milestone 5: THE FORK PAYS
```

1. **The control passes**, so the runner change (`c1` → `p15`) is not a difference and the rest of
   the table means what it says.
2. **`cb58a4b` is not a gameplay break.** `7180374` is the tip of the heat-pin wave branch, which
   merged at milestone 3; milestone 2 merged before it, so a batch there runs **unpinned**.
   Milestone 3 — which contains milestone 2 *and* the pin — reproduces L5 on all four cells, which
   is also the proof that "the modes get the bestiary" is campaign-inert.
3. **THE BREAK IS MILESTONE 5, "THE FORK PAYS".** It repriced the routing economy —
   `Run.DepthBase` 10 → 12, a SUPPLY discount, a PITCHED premium, an ELITE premium — and every one
   is a `const int` in `src/Run.cs` with **no environment restore flag**. `CLAUDE.md`'s own house
   rule is that "every gameplay lever has a restore-the-old-behaviour flag, because a wave that
   cannot be switched off cannot be attributed". This one does not, so **no bridge to L5 can exist
   through it, by construction.**

### The bridge that DOES hold, one merge later

Against **milestone 5 itself** (`54147dc`, built and run on the same 96 cells with its own
defaults), the bridge arm is:

| | |
|---|---|
| chunks identical on every (slot, policy) record | **96/96** |
| legs identical on the win flag | **1,920/1,920** |

**So every wave from milestone 6 through milestone 12 is fully switchable and its restore flag does
what it claims**, and the CRN machinery is intact across seven merges. The chain from L5 is broken
at exactly one place, it is named, and everything on either side of it reproduces.

### What the break is worth — THE FORK PAYS, priced on the ladder of record

Because the bridge arm *is* the milestone-5 tree, `L5 → L6bridge` is a clean CRN-paired measurement
of THE FORK PAYS on 6 rungs x 16 slot sets — **twelve times its own round's n, on the ladder's own
slot space**. Positive = the reprice wins more.

| rung | L5 | L6bridge | delta | n_disc | MDE(80%) | McNemar z | chunk t(15) |
|---|---|---|---|---|---|---|---|
| RECRUIT | 70.9 | 69.7 | −1.2 | 76 | 7.6 | −0.46 | −0.53 |
| h0 | 46.9 | 42.5 | −4.4 | 88 | 8.2 | −1.49 | −1.34 |
| h2 | 32.8 | 35.6 | +2.8 | 93 | 8.4 | +0.93 | +0.73 |
| h4 | 20.0 | 20.9 | +0.9 | 67 | 7.2 | +0.37 | +0.36 |
| h6 | 13.1 | 13.1 | 0.0 | 46 | 5.9 | 0.00 | 0.00 |
| h8 | 8.1 | 7.8 | −0.3 | 31 | 4.9 | −0.18 | −0.17 |
| **pooled** | 32.0 | 31.6 | **−0.36** | **401** | **2.9** | **−0.35** | |

**Not resolved at any rung, and the pooled 95% CI is [−2.4, +1.7].** 401 of 1,920 paired worlds
(20.9%) end differently, so the wave is not inert — it is a redistribution with no measured level
effect, which is precisely what its own README claimed on 160 campaigns at two rungs. That claim
now has 1,920 campaigns at six rungs behind it. **It is still not a zero:** every per-rung MDE is
4.9–8.4 points, so a real effect smaller than that is not excluded anywhere.

### And what the flagged waves are worth, composed

`L6bridge → L6` turns P10 + P16 + P18 + P19 + P20 back on together, same 1,920 CRN pairs:

| rung | RECRUIT | h0 | h2 | h4 | h6 | h8 | pooled |
|---|---|---|---|---|---|---|---|
| delta | +0.9 | +1.9 | +0.3 | +2.2 | −1.9 | +0.9 | **+0.73** |
| n_disc | 87 | 110 | 107 | 93 | 50 | 41 | 488 |
| MDE(80%) | 8.2 | 9.2 | 9.1 | 8.4 | 6.2 | 5.6 | 3.2 |
| McNemar z | +0.32 | +0.57 | +0.10 | +0.73 | −0.85 | +0.47 | +0.63 |

**Not resolved at any rung either.** So neither half of the L5 → L6 difference is a measured lever
move: at every rung both contributions sit inside their own MDE, and what separates the two tables
is mostly the draw. **Read that as a limit on this instrument, not as a licence to compare L5 with
L6 directly** — the two ladders are on different gameplay streams and the level comparison still
needs the bridge, which is broken at THE FORK PAYS.

## P20 ALONE, RE-PRICED ON SIXTEEN SLOT SETS — and it does not survive the doubling

This wave exists because P20 was reported as a −3.8-point tightening at h4 (McNemar z = −3.00,
chunk-paired t = −3.97, negative in all eight slot sets), which made L5 unquotable. Same lever,
same binary, `SIGHTLINE_STALEGROUND=1` against the ladder's own chunks, **16 slot sets**:

| rung | slot sets | stale | fix (shipped) | delta | n_disc | MDE(80%) | McNemar z | chunk t |
|---|---|---|---|---|---|---|---|---|
| h0 | P20's 8 (b0–70) | 41.2 | 43.8 | +2.5 | 12 | 6.1 | +1.15 | +0.88 |
| h0 | 8 NEW (b80–150) | 43.1 | 45.0 | +1.9 | 19 | 7.6 | +0.69 | +0.81 |
| **h0** | **all 16** | **42.2** | **44.4** | **+2.2** | **31** | **4.9** | **+1.26** | **+1.24** |
| h4 | P20's 8 (b0–70) | 22.5 | 18.1 | **−4.4** | 9 | 5.3 | **−2.33** | **−2.50** |
| h4 | 8 NEW (b80–150) | 28.8 | 28.1 | **−0.6** | 13 | 6.3 | −0.28 | −0.23 |
| **h4** | **all 16** | **25.6** | **23.1** | **−2.5** | **22** | **4.1** | **−1.71** | **−1.52** |

**P20's h0 non-result reproduces (+2.2 here, +2.2 there). P20's h4 RESULT does not survive the
slot space doubling.** On P20's own eight sets the effect is there and large (−4.4, z = −2.33). On
eight sets it never saw it is **−0.6, indistinguishable from nothing**. On all sixteen it is −2.5
at n_disc = 22, below its own MDE of 4.1 — **not resolved**.

This is the third time this project has measured that shape, and it is now the rule rather than an
anecdote: L5's split-half found L4's h0 and h8 were single draws of eight clusters, W2 found four
slot sets putting a leg below a band floor that sixteen put inside, and here eight sets resolve a
lever that sixteen cannot. **A rung is sixteen slot sets. So is a lever.**

**What P20 unambiguously DID do is move the board, and that reproduces:**

| | course differs | result differs |
|---|---|---|
| h0 | **27.2%** of 320 paired campaigns (P20 reported 25.3%) | 9.7% |
| h4 | **22.2%** of 320 (P20 reported 16.6%) | 6.9% |

So the CLAUDE.md warning was right about the mechanism and overstated about the consequence: the
same slot seed does play a measurably different world after P20, and the ladder did owe a
re-measure — but the *win-rate* effect of P20 alone is not resolved at n = 320 per rung. **Do not
read "not resolved" as "zero": the 16-set h4 point estimate is −2.5 with an MDE of 4.1.**

## THE STALEMATE SPLIT

All four arms of this round, 6,400 campaigns:

| arm | campaigns | mission arm | run arm | share |
|---|---|---|---|---|
| **L6 ladder** | 1,920 | **34** | **0** | **1.77%** |
| L6bridge | 1,920 | 28 | 0 | 1.46% |
| L6stale (P20 arm) | 640 | 10 | 0 | 1.56% |
| milestone-5 target | 1,920 | 28 | 0 | 1.46% |

*(L6bridge and the milestone-5 target read 28 each — a free cross-check on the 1,920/1,920 outcome
identity above, from a field the bridge comparison never looked at.)*

* **The run arm fired ZERO times in all four arms, 6,400 campaigns.** `runTurns` at the stall runs
  51–75 against a cap of 150. W9's backstop remains a backstop, three ladders running.
* **L6 reads 1.77% against L5's 1.41%** — RECRUIT 9/320 (2.81%), h0 8, h2 7, h4 1, h6 5, h8 4.
  The rung profile is flatter than L5's, whose RECRUIT carried 4.4%; the totals differ by 7
  campaigns in 1,920 and nothing here resolves that as a change rather than a draw.
* **It is still the bot's finishing line, not a ladder effect:** by objective, Escort 10,
  Eliminate 9, Evac 8, Rescue 6, Decapitate 1; missions 2–4 hold 26 of the 34.
* **Slot 46's mission-1 Eliminate stalls at FOUR rungs — h0 and h2 sloppy, h6 and h8 greedy,
  runTurns 51 every time.** L5 recorded the same world stalling at h0/h2/h4 on the sloppy policy
  alone. It has now survived a board-moving wave and taken the greedy policy with it, so the
  deadlock is a property of that opener rather than of a rung or a policy. That is autopilot work,
  as L5 said, and it is the most reproducible case anyone will get: `SIGHTLINE_BALANCE_BASE=40`,
  slot 46.

## What this round does NOT do

* **Ships no corrective lever.** h0 −2.6 and h6 −0.8 under their floors are findings to publish.
  L4 and L5 both declined to repair inside a measurement round; so does this one.
* **Does not make L5 and L6 comparable at the level.** The bridge is broken at THE FORK PAYS and
  cannot be repaired without a flag that does not exist. Shape is comparable (both monotone at
  every step, four of six in band); level is not.
* **Does not resolve either half of the L5 → L6 difference.** Both the unflagged wave (−0.36
  pooled) and the flagged composition (+0.73 pooled) sit inside their MDEs at every rung.
* **Does not diagnose the h6→h8 step (2.5).** Locating a flat step is what C1's per-RUNG ladder is
  for, and this round sampled six rungs, not ten.
* **Does not measure a camping policy or the opener re-tune** — both still open from C3.
