# W2 "THE OPPONENT ACTS" — raw measurement chunks

**Base commit: `4784803`** ("Merge wave W1 TRUE INSTRUMENT"), i.e. the integration tip when this
wave branched. The wave's own code is commit `3a11165` on `wave/opponent-acts`; the dial default
flipped to ON in the follow-up commit, so every chunk below was produced by a binary whose
behaviour is selected entirely by `SIGHTLINE_AIIDLEFIX`, never by the default.

**The lever, and the only one:** `SIGHTLINE_AIIDLEFIX` `0` (the pre-W2 opponent) vs `1`.
Nothing else was touched. No archived figure is compared against — W1 severed the gameplay RNG
from the rendered frame and re-rolled every archived CRN world, so **both legs are fresh on this
tree.**

## How to reproduce

```bash
# a binary snapshot, so the tree can keep building while a round is in flight
dotnet build -c Release && mkdir -p runbin/W2 && cp -r bin/Release/net8.0/. runbin/W2/

# one chunk  (tag, heat, slot base, N slots; FIX picks the leg)
BIN=runbin/W2r3 FIX=1 bash docs/measurements/w2/run_chunk.sh R4-fix1-h0-b0 0 0 10

# the whole round: 5 heat rungs x 4 disjoint slot sets x 2 legs = 40 chunks, ~6 min
BIN=runbin/W2r3 bash docs/measurements/w2/queue.sh
# heat 0, three MORE slot-set families (24 chunks, ~4 min)
BIN=runbin/W2r3 bash docs/measurements/w2/queue_h0.sh

# the aggregation + the paired (McNemar) test
python3 docs/measurements/w2/analyse.py       # the ladder
python3 docs/measurements/w2/analyse_h0.py    # heat 0 across all sixteen slot sets
```

`run_chunk.sh` is `docs/measurements/w1/run_chunk.sh` re-pointed, keeping its three-layer
completion check verbatim: **(a)** `rm -f` the target JSON first, **(b)** assert the process exit
code (2 = "no display, nothing written"), **(c)** assert the JSON's own `runs` field == 2N. Every chunk in this directory printed `OK ... runs=20`.

## What is here

| file | what it is |
|---|---|
| `R0diag-pre-*` / `R0diag-post-*` | the **identity proof**. `R0diag-pre` is the base commit's own binary (`runbin/W2pre`, built from `git archive 4784803`); `R0diag-post` is this wave's binary with `SIGHTLINE_AIIDLEFIX=0`. Two rungs (h0/b0 and h4/b10). Both diff **empty** outside the `harness` block under `docs/measurements/w1/inert_diff.sh` — so the dial-off leg is the pre-wave game, not an approximation of it. |
| `R4-fix{0,1}-h{0,2,4,6,8}-b{0,10,20,30}` | **the round of record**, on the FINAL binary (bleed-out standing gate + move-cost-neutral dash guard). 40 chunks; each is 10 CRN slots x greedy+sloppy = 20 campaigns. A rung is the four disjoint slot sets pooled = **80 campaigns per rung per leg**, 800 campaigns in total. |
| `R4h0-*` (bases 40-70, 900-930, 940-970) | **the heat-0 deep round** — three more disjoint slot-set families, run because review pooled four independent h0 sets and found three negative. With the round of record's own b0-30 that is **sixteen slot sets, 320 campaigns per leg** at h0. `analyse_h0.py` reports each family separately and then pools. |
| `R3-*`, `R3ext-*` | the same round one build earlier: after the standing gate, before the dash guard. Provenance. |
| `R1-*` | the same round on the wave's FIRST build, whose terminal else also fired during the all-downed bleed-out window. Kept as provenance and as **evidence**: all 40 R1/R3 chunk pairs are byte-identical once `harness` is stripped. Removing 320 hostile actions and 319 HUNKERED pops changed the outcome of exactly zero of 800 campaigns — a CRN win-rate round prices *consequences* and is structurally blind to anything confined to a state whose outcome is already decided. |
| `A0-h0-b0` | the first timing chunk (identical settings to `R1-fix0-h0-b0`, kept as the wall-clock reference: 20 campaigns in 6 s of data-ready time). |
| `analyse.py` | pools the four slot sets per rung and runs the paired McNemar test on the discordant worlds. |

**Four slot sets per rung, not two.** The L1 method note found bases 20/30 reading ~8.3 points
harder than 0/10 pooled over six rungs (z=1.93, p=0.053) — suggestive, not proven, but free to
guard against, so every rung here is bases 0/10/20/30.

## The result

```
  rung    OFF%     ON%   delta     n  0->1  1->0  p(2-sided)
    h0    47.5    43.8    -3.8    80     5     2       0.453
    h2    26.2    23.8    -2.5    80     4     2       0.688
    h4    22.5    23.8    +1.2    80     6     7       1.000
    h6    17.5    20.0    +2.5    80     4     6       0.754
    h8    12.5    10.0    -2.5    80     4     2       0.688
  POOL    25.2    24.2    -1.0   400    23    19       0.644
```

`0->1` = worlds the baseline won and the fix lost; `1->0` = the reverse. Concordant worlds carry no
information about the lever — that is what the CRN pairing is for.

### heat 0, on sixteen slot sets

Review pooled four independent 80-campaign h0 sets and found three negative, and asked whether a
5-point cost at the rung most players are on was being filed as noise. It was the right challenge,
and a 4-set answer was not good enough:

```
                family     n    OFF%     ON%   delta  0->1  1->0        p
    main round   b0-30    80    47.5    43.8    -3.8     5     2    0.453
   extension    b40-70    80    46.2    48.8    +2.5     4     6    0.754
   family C   b900-930    80    51.2    48.8    -2.5     8     6    0.791
   family D   b940-970    80    56.2    48.8    -7.5    10     4    0.180
  POOLED (all 16 sets)   320    50.3    47.5    -2.8    27    18    0.233

  paired difference -2.8 points, SE 2.1, 95% CI [-6.9, +1.3]
```

**−2.8 points at heat 0, not distinguishable from zero, but a ~7-point cost is not excluded.**
Three of four families negative and 60% of informative worlds against the fix: the direction is
more consistent than the significance. Two finer-grained fields agree with the null on ~10× the
sample — mission win-rate **78.94% → 78.56%** (n = 1410 / 1404 missions) and soldier deaths per
mission **1.340 → 1.340**.

**Read the OFF column as this wave's control, not as a ladder of record.** The band is ±8, so h0's
floor is 47.0 and the pooled control's 50.3 is comfortably inside — as is the shipped leg's 47.5.
**Only h2 is convincingly out** (26.2 against a floor of 32; −13.8 from centre ≈ 2.5 rung-SE, where
a rung's binomial SE at n=80 is ~5.6). Note what the deep round did to h0: **four slot sets read
47.5 → 42.5 and put the shipped leg below the floor; sixteen read 50.3 → 47.5 and both inside.**
That gap is the composition's, not W2's, and pricing it is W7's job.
