# P48 — PRICING THE FIRST AUTHORED ROOM

**Base commit `8ed2782`** (`main` after PROGRAM PARALLAX milestone 41). Heat PINNED
(`EventCatalog.HeatPinned`). Runner: `docs/measurements/p15/run_chunk.sh` — THE RUNNER OF RECORD.
Binary: a snapshot in `runbin/P48/` (gitignored), built from the working tree at the base commit
plus P48's own change.

## The lever

CITADEL (`Maps.Layouts[4]`) redrawn on P28's edge layer: the same 4x4 footprint, but its walls live
on the tile BOUNDARIES instead of eating fourteen tiles of floor, so it is a room with an interior,
a tier-1 firing platform and one door — instead of a solid block you can only stand behind.

    arm off   SIGHTLINE_EDGEARENAS=0     the pre-P48 CITADEL, 14 high-cover tiles
    arm on    SIGHTLINE_EDGEARENAS=1     the room

One arena. Nothing else in the tree differs between the arms.

## TWO ROUNDS, because the first one could not see its own lever

    round      prefix        n        what every mission played
    free       off / on      960 pairs  the SHIPPED distribution — CITADEL is 1 arena in 35
    forced     foff / fon    960 pairs  SIGHTLINE_MAP=4 — every mission plays CITADEL

**192 chunks, 3,840 campaigns, zero BAD, `ARM CHECK` PASS over all 192** (each artifact's own
`levers.edgeArenas`, `batch.heat`, `batch.slotBase`, `runs == expectedRuns` and `heatLeak.pinned`
asserted against its file name). The forced round is verifiably forced: **7,955 missions, every one
of them on arena 4, zero procedural fallbacks.**

## Result 1 — the free round is BELOW ITS OWN FLOOR, and that is the methodological finding

    rung    n    OFF%    ON%   delta   b   c  n_disc   MDE      z
    h0    320    51.9   52.2    +0.3   1   2       3  1.52   +0.58
    h4    320    25.6   26.2    +0.6   1   3       4  1.75   +1.00
    h8    320     5.9    5.9    +0.0   1   1       2  1.24    0.00
    POOLED 960 pairs: n_disc = 9, delta +0.31, MDE(80%) 0.88, McNemar z = +1.00

**NINE discordant campaigns in 960.** By C2's rule that is an absence of evidence, not a measured
zero — and no n this project can afford fixes it. One arena of thirty-five, on ~80% of builds, is
~1.7% of missions: the whole round produced **52 / 51 missions that played the board under test**,
whose own win rates (57.7% -> 62.7%, unpaired SE 9.6) resolve nothing either. Resolving +-5 points
on one arena through the shipped distribution needs on the order of **47,000 missions**.

**A SINGLE ARENA CANNOT BE PRICED AT THE CAMPAIGN LEVEL. Do not run this round again.**

## Result 2 — the forced round is the instrument that works

    rung    n   fOFF%   fON%   delta    b    c  n_disc   MDE      z
    h0    320    43.4   44.7    +1.2   62   66     128  9.90   +0.35
    h4    320    10.3   13.8    +3.4   28   39      67  7.16   +1.34
    h8    320     1.2    2.8    +1.6    3    8      11  2.90   +1.51
    POOLED 960 pairs: n_disc = 206, delta +2.08, MDE(80%) 4.19, McNemar z = +1.39

**206 discordant of 960 — 21.5%.** Nothing resolves, but this is now a BOUNDED effect rather than
an absent one, and the direction is positive at all three rungs. The room is, if anything, slightly
easier than the block.

**THESE WIN RATES MAY NOT BE READ AGAINST THE BAND.** A campaign played entirely on one arena is a
different game: the OFF arm reads 43.4 / 10.3 / 1.2 against the ladder of record's 51.9 / 25.6 / 5.9
on the same tree. The forced round prices the ARENA, not the game.

## Result 3 — decision richness, and it is the one that answers P42

P42 measured procedural buildings and found the finding was not win rate but **decision richness,
down at every rung** (`meaningfulChoicesPerTurn` 3.32 -> 2.68 / 3.22 -> 2.93 / 1.81 -> 1.36), and
concluded: *a rectangle gives you somewhere to sit, and sitting is not a decision.* It named the
AUTHORED room as the unpriced object. Here it is, on the forced instrument, paired over the 16 CRN
slot sets with a cluster t on 15 df:

    metric                              rung    OFF     ON    delta   clSE      t
    meaningfulChoicesPerTurn            h0    3.394  3.584   +0.189  0.130   +1.45
                                        h4    3.408  3.664   +0.256  0.202   +1.27
                                        h8    1.472  1.242   -0.231  0.033   -7.06  RESOLVED
    positionChoicesPerArmedSoldierTurn  h0    1.788  1.719   -0.069  0.029   -2.42  RESOLVED
                                        h4    1.887  1.823   -0.064  0.045   -1.43
                                        h8    1.755  1.507   -0.249  0.027   -9.25  RESOLVED
    targetChoicesPerArmedSoldierTurn    h0    0.533  0.607   +0.074  0.042   +1.74
                                        h4    0.640  0.769   +0.128  0.072   +1.79
    turnsWithAShotPct                   h0   58.881 60.350   +1.469  0.765   +1.92
                                        h8   39.812 41.331   +1.519  0.984   +1.54

**THE ROOM TRADES POSITIONING FOR SHOOTING.** Position choices fall at every rung (resolved at two
of three); target choices and turns-with-a-shot rise at every rung. That is what removing fourteen
cover TILES and replacing them with four walls does: fewer places worth standing, more lines worth
taking.

**It does not reproduce P42's penalty at h0 and h4 — it reverses the sign** (+0.19, +0.26 against
procedural buildings' -0.64, -0.29), though neither clears its own cluster SE. **At h8 it does
reproduce it, and that is the round's only resolved richness number: -0.231, t = -7.06.**

So: *a rectangle costs choices; a room does not — except at the apex, where it costs them clearly.*

## Verdict

**Ship it.** Win rate is bounded and positive-leaning on the instrument that can see it; decision
richness is flat-to-positive at h0/h4, which is the opposite of what a procedural wall does. The h8
cost is real and resolved, and it is published rather than tuned — P23's precedent.

**What this does NOT say.** It prices ONE room with ONE door and NOTHING INSIDE IT. P26's site
glyphs are still unused, so there is no objective in that room and therefore still no *reason* to
go in beyond the firing platform. That is the next lever and it is deliberately a separate one:
L7's lesson is that two levers measured together give a number that does not resolve.

## Reproducing

    dotnet build -c Release && mkdir -p runbin/P48 && cp -r bin/Release/net8.0/* runbin/P48/

    # free round
    BIN=runbin/P48 OUT=docs/measurements/p48 EXTRA="SIGHTLINE_EDGEARENAS=0" \
      bash docs/measurements/p15/run_chunk.sh off-h0-b0 0 0 10
    # forced round
    BIN=runbin/P48 OUT=docs/measurements/p48 EXTRA="SIGHTLINE_EDGEARENAS=0 SIGHTLINE_MAP=4" \
      bash docs/measurements/p15/run_chunk.sh foff-h0-b0 0 0 10
    # ... over H in {0,4,8}, B in {0,10,...,150}, both arms, both rounds, then:

    python3 docs/measurements/p48/armcheck.py  docs/measurements/p48
    python3 docs/measurements/l6/pairs.py      off  docs/measurements/p48 on  docs/measurements/p48
    python3 docs/measurements/l6/pairs.py      foff docs/measurements/p48 fon docs/measurements/p48
    python3 docs/measurements/p48/richness.py  docs/measurements/p48 foff fon
    python3 docs/measurements/p48/arena4.py    docs/measurements/p48

## The defect this round found in its own instrument

The first 39 chunks reported `levers.edgeArenas: true` on **both** arms. `SIGHTLINE_EDGEARENAS` had
been written beside its own self-test hook, three hundred lines below the `SIGHTLINE_BALANCE` read
that runs the batch and never returns — **a gameplay flag read after the batch entry point is a flag
that does not exist.** `ARM CHECK` caught it on its first run, which is exactly what P42 added the
`levers{}` block for. The round was discarded and re-run from scratch.

`SIGHTLINE_MAP` had the mirror-image fault: set correctly in `RealMain` and then overwritten by
`BalanceBatch`'s own "keep batch-wide static state deterministic" reset. The first forced batch came
back with five DIFFERENT arenas in `byArena` and nothing complained. Both reads now live where they
take effect, and the probe that proves it is two lines.
