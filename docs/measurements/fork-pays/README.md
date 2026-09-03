# THE FORK PAYS — the measured round

**Base commit `0e7c019`** (`Merge the-beat (PROGRAM PARALLAX)`, the working branch
`claude/game-dev-team-orchestration-2jykom`). Branch `wave/fork-pays`.
**Heat pin ON** (the default since wave THE HEAT PIN — `EventCatalog.HeatPinned`, so a rung means
the rung). Instrument `SIGHTLINE_BALANCE`, Release snapshots run under `xvfb-run`, greedy+sloppy
per slot.

**Two rounds, both archived.** Round 1 measured a level shift the wave had not intended; the fix
for it (`Run.DepthBase` 10 → 12) is *why* round 2 exists. Round 1 is kept because the reason a
constant moved is worth more than the constant.

## Shape

| | |
|---|---|
| rungs | heat **0** and heat **4** |
| CRN slot bases | **0, 10, 20, 30** (`SIGHTLINE_BALANCE_BASE`) |
| per chunk | `SIGHTLINE_BALANCE=20` x greedy+sloppy = **40 campaigns**, asserted `runs=40` |
| per rung per arm | 4 chunks = **160 campaigns** |
| total | base 320 + round 1 branch 320 + round 2 branch 320 = **960 campaigns**, 24 chunks, **zero `BAD`** |

## Exact command lines

```bash
# both arms are Release snapshots, so the tree can keep building while a round is in flight
git worktree add --detach /home/user/wt/fork-pays-base 0e7c019
(cd /home/user/wt/fork-pays-base && dotnet build -c Release)
mkdir -p runbin/FPbase runbin/FPnew
cp -r /home/user/wt/fork-pays-base/bin/Release/net8.0/* runbin/FPbase/
cp -r bin/Release/net8.0/*                              runbin/FPnew/

# 8 chunks per arm. run_chunk.sh is a copy of docs/measurements/c1/run_chunk.sh with only the
# BIN/OUT defaults changed — all three layers of W1's contract intact (rm -f the target FIRST,
# check the EXIT CODE, then assert the JSON's own `runs` field).
for H in 0 4; do for B in 0 10 20 30; do
  BIN=runbin/FPbase bash docs/measurements/fork-pays/run_chunk.sh base-h${H}-b${B} $H $B 20
done; done
for H in 0 4; do for B in 0 10 20 30; do
  BIN=runbin/FPnew  bash docs/measurements/fork-pays/run_chunk.sh new-h${H}-b${B}  $H $B 20
done; done

python3 docs/measurements/fork-pays/report.py base new   > REPORT.txt          # the shipped round
python3 docs/measurements/fork-pays/report.py base r1    > REPORT-round1.txt   # the deflating one
```

Both arms exported `XDG_CONFIG_HOME` per chunk (the runner does it) — several agents share this
container and the persistence self-tests stash the real user-data dir.

## Files

| file | what |
|---|---|
| `base-h{0,4}-b{0,10,20,30}.json` / `.report.txt` | the BASE arm at `0e7c019`, shared by both rounds |
| `r1-*` | **round 1** branch arm: the three price changes with the OLD depth base `10 + 4n` |
| `new-*` | **round 2** branch arm: what shipped, depth base `Run.DepthBase (12) + 4n` |
| `*-chunks.txt` | the runner's own `OK <tag> runs=40` lines — the completion assertions, verbatim |
| `report.py` | the analysis: win% with binomial AND chunk-paired SE, the per-campaign CRN/McNemar contrast, intel per run and per mission, `byNodeKind` pooled |
| `REPORT.txt` / `REPORT-round1.txt` | its output for each round |
| `run_chunk.sh` | the runner |
| `shots/` | the three campaign-map hovers this wave photographed: a SUPPLY stop, a PITCHED fight (showing the premium) and an EVENT node |

The runner also writes a `<tag>.log` per chunk (672 KB for the round). Those are **not committed** —
`.gitignore` excludes `*.log` and every archive in `docs/measurements/` follows the same rule. The
`.json` and `.report.txt` are, and every number in this README re-derives from the JSONs with
`report.py`.

## Round 2 — what shipped

| | heat 0 | heat 4 |
|---|---|---|
| base win% (n=160) | **53.1**  binom SE 3.95 / cluster SE 1.20 | **22.5**  3.30 / 3.06 |
| branch win% (n=160) | **50.0**  3.95 / 3.68 | **28.8**  3.58 / 1.61 |
| chunk-paired delta | **−3.1 ± 3.59, t(3) = −0.87** | **+6.2 ± 4.39, t(3) = +1.42** |
| per-chunk deltas | −12, −2, +5, −2 | +12, +15, 0, −2 |
| McNemar (160 CRN pairs) | 45 discordant (20 new / 25 base), **z = −0.75** | 36 discordant (23 / 13), **z = +1.67** |
| intel earned / run | 129.0 → 134.7 | 156.7 → 162.2 |
| intel earned / **mission** | 29.06 → **29.37** | 43.52 → **42.48** |
| avg missions cleared | 4.44 → 4.58 | 3.60 → 3.82 |
| paired worlds differing in `missionsCleared` | 59/160 (36.9%) | 54/160 (33.8%) |

**The two rungs move in OPPOSITE directions and neither is resolved.** That is the finding: the
reprice is a redistribution, the economy LEVEL is back where it started (intel/mission +1.1% and
−2.4%), and no win-rate claim is made in either direction.

### Read this before quoting any row above

1. **This round CANNOT price the fork, and that is structural, not a sample-size problem.**
   `Game.Autopilot.PickAutoNode`'s shipped policy is *"prefer an Event node, else `nn[0]`"* — the
   lowest row of the next column, because `Run.NextNodes()` walks a list appended in row order. It
   reads neither `MissionNode.Intel` nor the objective class, and `SIGHTLINE_ROUTE=hash` (the only
   alternative) is a *uniform* deal, not a *valuing* one. **The bot eats the SUPPLY discount without
   choosing it and walks past the PITCHED premium without seeing it.** So this is a
   *not-a-regression* check on the CONSEQUENCES of the reprice (what the shop can afford; whether a
   SUPPLY clear now leaves a wound), not a measurement of the fork as a decision. A valuing route
   policy in the flywheel is the prerequisite; it is on the ROADMAP.
2. **The arms are emphatically NOT inert** — 34-37% of paired worlds end on a different number of
   missions cleared. The lever reaches gameplay; it just is not reached *through the fork*.
3. **Four slot sets is this project's MINIMUM for a rung and this round has exactly four.** A
   cluster SE over 4 clusters carries 3 degrees of freedom, and the h0 cluster SE differs threefold
   between the arms (1.20 base vs 3.68 branch). Prefer the McNemar row: it is paired at the
   campaign, not at the chunk.
4. **`byNodeKind` moved on Boss in opposite directions at the two rungs** (h0 85.9 → 73.4, h4 53.0 →
   62.2, n ≈ 65-110 per cell) with no mechanism connecting the lever to the finale. That is noise
   and is not reported as an effect.
5. **This is a LEVER check, not a ladder.** Two rungs at n=160 does not restate L5; L5 (6 rungs x 16
   slot sets, n=320/rung) remains the ladder of record.

## Round 1 — the deflation, and why `Run.DepthBase` moved

Round 1 shipped SUPPLY −6 / ELITE +14 / PITCHED +8 on the **old** depth base `10 + 4n`.

| round 1 | heat 0 | heat 4 |
|---|---|---|
| base win% | 53.1 | 22.5 |
| branch win% | **46.9** | **19.4** |
| chunk-paired delta | **−6.2 ± 2.98, t(3) = −2.10** | −3.1 ± 2.77, t(3) = −1.13 |
| McNemar z | −1.71 | −1.09 |
| intel earned / **mission** | 29.06 → **27.47** (−5.4%) | 43.52 → 42.44 |

Neither rung is resolved, but the sign is consistent and **the mechanism is arithmetic that can be
computed without a batch at all.** Over the mix of nodes actually PLAYED in the base arm (603
missions at h0, 536 at h4), the old kind premiums paid **3.20 / 3.35** intel per mission and the new
table pays **1.32 / 1.29** — a loss of **1.88 / 2.06 per mission**, i.e. a ~7% campaign-wide intel
deflation. The counts are re-derivable from any base-arm chunk's `byObjectiveByNodeKind` block:

| node kind | TASKED | PITCHED |
|---|---|---|
| Combat | 141 | 38 |
| Elite | 60 | 10 |
| Supply | 76 | 19 |
| Start | 0 | 160 |
| Boss | 0 | 99 |

`Run.DepthBase` 10 → 12 hands the mean back: 1.32 + 2 = **3.32 against 3.20**, within 0.12
intel/mission of the pre-wave level. Round 2 confirms it empirically (intel/mission +1.1% / −2.4%
instead of −5.4%).

**The lesson worth keeping: a pricing change that quietly moves the LEVEL is a balance change
pretending to be an information change.** Anyone re-tuning `Run.SupplyDiscount`, `Run.ElitePremium`
or `Run.PitchedPremium` owes `Run.DepthBase` this same arithmetic before running a batch.
