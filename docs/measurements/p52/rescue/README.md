# P52 / RESCUE — the leg that was assumed, and was not the same

**Base commit `4ba84b3`.** Heat PINNED. Runner: `docs/measurements/p15/run_chunk.sh`.
Binary: `runbin/P52/` (the same snapshot as the HACK leg). **96 chunks, 1,920 campaigns, 960 CRN
pairs, zero BAD, `ARM CHECK` PASS over all 96, `LEAK-CHECK PASS` on both arms** (48/48 pinned).
Instrument: `SIGHTLINE_MAP=4 SIGHTLINE_OBJ=rescue` — `byArena == {4}`, `byObjective == {Rescue}`.

## Why it exists

P51's roadmap item 2 read:

> **RESCUE has the same shape as HACK and has never been measured.** `C` is exactly 0 or 1 per
> template and CITADEL's captive is inside the room — a singleton objective behind one door, which
> is precisely the configuration P49 measured as a collapse. **Either move it out with `T`, or
> measure it** before defaulting anything on.

P52 did both, because "the same shape" was an assumption and this repository's own rule is not to
ship one. **The assumption was wrong**, and it was wrong about the SHIPPING board, not just the arm.

## Result

    rung    n    OFF%    ON%   delta    b    c  n_disc   MDE       z
    h0    320    92.2   99.4    +7.2    2   25      27  4.55   +4.43
    h4    320    93.4   99.4    +5.9    2   21      23  4.20   +3.96
    h8    320    12.8   28.1   +15.3   30   79     109  9.14   +4.69
    POOLED 960 pairs: n_disc = 159, delta +9.48, MDE 3.68, McNemar z = +7.22

    meaningfulChoicesPerTurn   h0  9.288 -> 16.290  +7.002  t +21.2   RESOLVED
                               h4 10.736 -> 16.977  +6.241  t +18.9   RESOLVED
                               h8  1.025 ->  4.517  +3.492  t +32.6   RESOLVED
    targetChoices/armed-turn   h8  0.387 ->  0.347  -0.039  t  -1.7   not resolved
    missions 3,957 -> 4,375;  soldier deaths 2,122 -> 1,642;  losses 369 -> 430

## The finding, and it is about the SHIPPING board

**RESCUE on CITADEL is nothing like HACK on CITADEL, in either arm.**

    objective   OFF (shipping literals)   OFF choices/turn      ON
    HACK        66.2 / 40.0 / 18.1        4.06 / 3.93 / 2.06    94.4 / 95.6 / 87.2
    RESCUE      92.2 / 93.4 / 12.8        9.29 / 10.74 / 1.03   99.4 / 99.4 / 28.1

The shipping RESCUE board is **already at the ceiling at h0 and h4** (92-93%) and then falls off a
cliff to 12.8% at h8 — a two-state mission, not a ladder — while carrying 9-11 meaningful choices a
turn at the low rungs and **1.03** at the apex. The HACK board is a monotone 66/40/18 at a flat
4.06. Two objectives, one arena, one instrument, and they are different games.

So the easing here (+9.5 pooled) is small **because the baseline is already near-ceiling**, not
because the content is gentle. Reading +9.5 as "RESCUE is fine" would be reading a floor effect
backwards: at the only rung with room to move, the apex, the content **more than doubles** win rate
(12.8 -> 28.1, resolved, +15.3).

**This is the third time in four waves that an assumption about how one objective behaves has failed
when measured.** P49 assumed the room was the object; P51 assumed site count was; this leg assumed
two singleton objectives on one board behave alike. None survived. `Run.IsKillObjective` splits the
eight objectives into two classes (C3, worth 43 points) — **nothing in this project has established
that the six task objectives behave alike WITHIN that class, and two of them measurably do not.**

## Verdict

Same as the HACK leg: `SIGHTLINE_SITEGLYPHS` stays default OFF. Moving `C` out of the room was the
right structural change (the gate this wave ships would refuse it inside), but it does not make this
arena's RESCUE shippable, and the shipping RESCUE board on this arena is its own open problem that
predates every one of these five waves.

## Reproducing

    BIN=runbin/P52 OUT=docs/measurements/p52/rescue \
      EXTRA="SIGHTLINE_SITEGLYPHS=0 SIGHTLINE_MAP=4 SIGHTLINE_OBJ=rescue" \
      bash docs/measurements/p15/run_chunk.sh off-h0-b0 0 0 10
    # ... over H in {0,4,8}, B in {0,10,...,150}, both arms, then:

    python3 docs/measurements/p52/rescue/armcheck.py docs/measurements/p52/rescue
    python3 docs/measurements/l5/cluster.py   off --dir docs/measurements/p52/rescue
    python3 docs/measurements/l6/pairs.py     off docs/measurements/p52/rescue on docs/measurements/p52/rescue
    python3 docs/measurements/p52/rescue/richness.py docs/measurements/p52/rescue off on
