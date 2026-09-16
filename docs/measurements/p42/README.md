# P42 — PRICING BUILDINGS

**Base commit `8c75492`** (`main` after PROGRAM PARALLAX milestone 35). Heat PINNED
(`EventCatalog.HeatPinned`). Runner: `docs/measurements/p15/run_chunk.sh` — THE RUNNER OF RECORD.
Binary: a snapshot in `runbin/P42/` (gitignored), built from the base commit.

## The question

P40 removed P28's blocker (the SAPPER branch is alive again with buildings on, AICOVTEST green), so
`Mission.Buildings` could become the default. **Should it?** A default flip is a LEVEL lever on
every mission, and CLAUDE.md's contract says price it before shipping it.

## The round

3 rungs (h0 / h4 / h8) x 16 CRN slot bases (0..150) x greedy+sloppy x 2 arms.
**96 chunks, 1,920 campaigns, 960 CRN pairs, zero BAD.**

    arm OFF   SIGHTLINE_BUILDINGS=0
    arm ON    SIGHTLINE_BUILDINGS=1

Both arms are the same tree with one lever between them, on identical slot seeds.

* `ARM CHECK`: **PASS** over all 96 chunks. Each artifact's own `levers.buildings`,
  `batch.heat` and `batch.slotBase` were asserted against its file name — P42 added
  `buildings` / `destructibleEdges` / `density` / `edges` to the `levers{}` block precisely so a
  chunk could name its own arm instead of being trusted by its tag. (P15 and C4 were both bitten by
  a chunk that did not measure what its name said.)
* `LEAK-CHECK`: **PASS** — 0 of 1,920 campaigns off-rung.
* Every chunk `heatPinned: true`, `envErrors: []`, `runs == batch.expectedRuns == 20`.

## Result 1 — win rate: NOT RESOLVED, and the discordance says that is a real bound

    rung    n    OFF%    ON%   delta    b    c  n_disc    SE    MDE      z
    h0    320    51.9   46.6    -5.3   70   53     123  3.47   9.70  -1.53
    h4    320    25.6   23.4    -2.2   54   47     101  3.14   8.79  -0.70
    h8    320     5.9    5.6    -0.3   17   16      33  1.80   5.03  -0.17
    POOLED 960 pairs: n_disc=257, delta -2.60, SE 1.67, MDE(80%) 4.68, McNemar z -1.56

No rung resolves; the pooled estimate does not either. But **26.8% of paired campaigns take a
different course** (257 discordant of 960), so this is a BOUNDED effect and not an absent one —
C2's rule the other way round. The direction is consistent (negative at all three rungs) and
shrinks with heat.

**The OFF arm reproduces the ladder of record**, which is what licenses reading the ON arm against
the band: P24 shipped h0 49.5 / h4 26.1 / h8 4.8; this round's OFF arm reads 51.9 / 25.6 / 5.9.
Against the bands, the ON arm puts **h0 at 46.6 against a 47 floor** — fractionally under, and
inside the noise either way.

## Result 2 — decision richness: DOWN AT EVERY RUNG. This is the finding.

    rung   meaningfulChoices/turn   OFF     ON    delta
    h0                             3.32   2.68    -0.64
    h4                             3.22   2.93    -0.29
    h8                             1.81   1.36    -0.45

Consistent, at every rung, on the metric that IS pillar 3's second-to-second half. It agrees with
P40's AICOVTEST census on the same lever — **hunker 16.05% -> 26.76%**, shoot 40.84% -> 34.26%,
idle 28.60% -> 21.72%. Walls give you somewhere to sit, and sitting is not a decision.

## Result 3 — skill expression: unchanged

Greedy-minus-sloppy edge, OFF -> ON: h0 −6.2 -> −5.6, h4 +0.0 -> +1.9, h8 +5.6 -> +3.8. No
consistent direction, all inside the noise. Buildings do not make the game reward play any better —
which matters, because P26 measured that skill is worth only +0.2 points overall and a lever that
raised it would have been worth paying win rate for.

## Verdict

**Do not make buildings the default.** They cost ~0.3-0.6 meaningful choices per turn at every
rung, leave skill expression flat, and put h0 marginally under its band floor. A lever that costs
choices and buys nothing measurable does not earn a default.

**What this does NOT say.** It prices PROCEDURAL buildings — `StampBuildings`' rectangles of wall,
dropped where they fit. A rectangle gives cover without giving a reason to go in, so it buys
hunkering. An AUTHORED building — an objective inside it, a roof worth holding, a door worth
breaching — is a different object and this round says nothing about it. That, not a default flip,
is where the edge layer earns its keep; see `docs/ROADMAP.md`.

## Reproducing

    # snapshot the binary first (runbin/ is gitignored)
    dotnet build -c Release && mkdir -p runbin/P42 && cp -r bin/Release/net8.0/* runbin/P42/

    BIN=runbin/P42 OUT=docs/measurements/p42 EXTRA="SIGHTLINE_BUILDINGS=0" \
      bash docs/measurements/p15/run_chunk.sh off-h0-b0 0 0 10
    BIN=runbin/P42 OUT=docs/measurements/p42 EXTRA="SIGHTLINE_BUILDINGS=1" \
      bash docs/measurements/p15/run_chunk.sh on-h0-b0 0 0 10
    # ... over H in {0,4,8} and B in {0,10,...,150}, then:
    python3 docs/measurements/l6/pairs.py off docs/measurements/p42 on docs/measurements/p42
