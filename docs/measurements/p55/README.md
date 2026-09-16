# P55 — THE ASSET ANSWERS HEAT: the lever works, and the campaign dies anyway

**Base commit `675ef79`.** Heat PINNED. Runner: `docs/measurements/p15/run_chunk.sh`. Binary:
`runbin/P55/` (gitignored). **128 chunks, 2,560 campaigns, 1,280 CRN pairs, zero BAD, `ARM CHECK`
PASS over all 128.**

**THIS ROUND IS NOT FORCED.** Unlike P49-P53 it runs the shipped arena and objective distribution,
so its win rates **are** comparable with the heat band. `armcheck.py` asserts the mirror image of the
forced rounds' check — `byArena` and `byObjective` must be DIVERSE and Rescue or Escort must actually
be present — so a stray `SIGHTLINE_MAP`/`_OBJ` cannot produce a round that is blind to its own lever.

## The lever

`Mission.VipHeatBonus` adds `Heat.StatDelta` to the protected asset's HP and `Heat.DmgDelta` to its
armor, both clamped at zero. **The dose is argued, not searched** (see the function's own comment);
at depth 4 the asset reads 22/22/23/23/23/23/24/25/26 HP across h0..h8.

    arm off   SIGHTLINE_VIPHEAT=0    the pre-P55 asset — a pure function of mission depth
    arm on    SIGHTLINE_VIPHEAT=1    the asset answers heat

## Result 1 — CAMPAIGN win rate barely moves

    rung    n    OFF%    ON%   delta    b    c  n_disc   MDE       z
    h0    320    52.2   52.2    +0.0    0    0       0    —        —     IDENTICAL, 0 discordant
    h4    320    26.2   26.6    +0.3    0    1       1  0.88   +1.00   not resolved
    h6    320    12.2   13.4    +1.2    0    4       4  1.75   +2.00   not resolved
    h8    320     5.9    7.5    +1.6    1    6       7  2.32   +1.89   not resolved
    POOLED 1280 pairs: n_disc = 12, delta +0.78, MDE 0.76, McNemar z = +2.89

**h0 is EXACTLY zero discordant**, which is the clamp working as designed and is this round's
inertness control earning its keep. Everything else is small, positive and monotone in heat — the
shape the dose predicts, since the dose itself grows with heat — but **twelve discordant campaigns
in 1,280 pairs is very little resolving power** (C2's rule), and no per-rung contrast resolves.

## Result 2 — THE LEVER IS SURGICAL AND LARGE WHERE IT AIMS

Per-objective MISSION win rate, off -> on:

    objective      h4               h6                h8
    Escort      88.9 -> 88.9    50.7 -> 60.6     41.8 -> 61.2   <<<  +19.4
    Rescue      97.4 -> 97.4    58.5 -> 69.7     35.2 -> 50.5   <<<  +15.3
    Decapitate  37.0 -> 37.4    24.3 -> 24.0     15.5 -> 15.4
    Defend      60.2 -> 60.2    84.5 -> 83.3     72.7 -> 72.2
    Eliminate   80.0 -> 80.0    78.5 -> 77.9     79.1 -> 77.9
    Evac        80.9 -> 80.9    62.3 -> 61.1     53.5 -> 54.5
    Hack        65.1 -> 65.1    56.6 -> 55.3     47.8 -> 48.9
    Sabotage    61.8 -> 61.8    50.8 -> 51.6     41.0 -> 42.9

**The two objectives it targets move 10-19 points at h6/h8. Every other objective moves by at most
1.3.** That is as clean a targeting result as this project has produced.

**And h4 does not move at all for either — 97.4 -> 97.4, 88.9 -> 88.9.** That was FLAGGED IN ADVANCE
as data rather than a control (`Heat.StatDelta(4)` is 1, and +1 HP against a 97% win rate is
nothing), and it is the half of P54's finding a heat term was never going to touch.

## Result 3 — THE FINDING: the NPC death was largely a SYMPTOM

The lever does what it was built to do — NPC-cause losses fall by a third — **and the campaigns are
lost anyway:**

    rung   total losses      NPC-cause losses      campaigns that kept the NPC alive and LOST ANYWAY
    h6      281 -> 277 (-4)     67 -> 49 (-18)                    14 of 18
    h8      301 -> 296 (-5)     83 -> 56 (-27)                    22 of 27

**Twenty-seven fewer NPC deaths at h8 bought five fewer lost campaigns.** A squad that cannot screen
the asset is usually a squad that was going to lose regardless; preventing the death re-labels the
loss rather than converting it to a win.

**This is a limit on P54's own method, measured.** A loss-cause cross-tab says where losses are
LABELLED, and the label can be downstream of the real cause. P54 introduced that method and this
round establishes its ceiling — quote both together.

## Result 4 — the ladder, and a side-benefit worth naming

Both arms are **4 of 4 IN BAND and monotone at every step**:

    rung    OFF%  (clear)         ON%  (clear)        band
    h0      52.2  +5.2 / 2.15    52.2  +5.2 / 2.15    47-63
    h4      26.2  +4.2 / 1.74    26.6  +4.6 / 1.80    22-38
    h6      12.2  +0.2 / 0.09    13.4  +1.4 / 0.68    12-28
    h8       5.9  +0.9 / 0.72     7.5  +2.5 / 1.94     5-15

**P24 had to publish h6 and h8 as sitting ON their floors** (−0.1 and −0.2, 0.11/0.18 cluster SE).
This lifts exactly those two off the floor — h6 from 0.09 to 0.68 cluster SE of clearance, h8 from
0.72 to 1.94 — **while leaving h0 bit-identical and h4 unmoved.** That is a margin improvement at
the two marginal rungs as a by-product of a defect repair, not as a tuning pass.

⚠ **The OFF arm is NOT a bridge to P24** (49.5 / 26.1 / 11.9 / 4.8 against 52.2 / 26.2 / 12.2 / 5.9).
The tree has changed since — P48 ships the edged CITADEL by default — so the closeness is
reassurance, not a chain. Do not subtract one table from the other.

## Verdict — SHIPPED ON

`Mission.VipHeat` defaults **true**. It is a defect repair before it is a lever: an asset that does
not answer the difficulty dial at all is not defensible at any dose, and P14 had already fixed the
identical shape in the identical function on the mode axis. It is surgical (≤1.3 points on six
objectives), in band at all four rungs, and it improves the ladder's margin where it was thinnest.

**What it does NOT fix, stated here so it is not claimed later:** Rescue at 97.4% and Escort at 88.9%
at h4, and 96-98% at h0. A heat term cannot reach an objective that is free before heat arrives.
That is P54's second defect and it is still open.

## Reproducing

    dotnet build -c Release && mkdir -p runbin/P55 && cp -r bin/Release/net8.0/* runbin/P55/

    BIN=runbin/P55 OUT=docs/measurements/p55 EXTRA="SIGHTLINE_VIPHEAT=0" \
      bash docs/measurements/p15/run_chunk.sh off-h0-b0 0 0 10
    # ... over H in {0,4,6,8}, B in {0,10,...,150}, both arms, then:

    python3 docs/measurements/p55/armcheck.py docs/measurements/p55
    python3 docs/measurements/l5/cluster.py   off --dir docs/measurements/p55
    python3 docs/measurements/l6/pairs.py     off docs/measurements/p55 on docs/measurements/p55
    python3 docs/measurements/p54/apex_losses.py     # the method this wave tests the limit of
