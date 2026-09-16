# P51 — THE SPLIT: it was the singleton, not the room

**Base commit `c95d043`** (`main` after PROGRAM PARALLAX milestone 44). Heat PINNED. Runner:
`docs/measurements/p15/run_chunk.sh`. Binary: a snapshot in `runbin/P51/` (gitignored).

## The question

P49 measured that the authored room, with the HACK terminal inside it, took win rate to 90%+ at
every rung and cut meaningful choices per turn from 4.06 to 1.18. P50 put a garrison in and the
fight came back — but at h0/h4 the board was still 94.7% / 91.2% against the shipping board's
66.2% / 40.0%, and the conclusion was:

> **The garrison fixes the fight and does not fix the difficulty.** A HACK whose terminal sits in
> one room is a mission with ONE PLACE TO BE, and one place to be is easy however hard the fight
> there is.

**That is a testable claim, and this round tests it directly.** Same lever, same board, same dials —
a different objective. SABOTAGE has THREE charge sites instead of one. CITADEL now declares three
`X` glyphs: **one inside the held room, two far outside and eleven tiles apart.**

    arm off   SIGHTLINE_SITEGLYPHS=0     the shipping board (literal sites, no room content)
    arm on    SIGHTLINE_SITEGLYPHS=1     the room, its garrison, and one of three charges inside it

Instrument: `SIGHTLINE_MAP=4 SIGHTLINE_OBJ=sabotage`. **96 chunks, 1,920 campaigns, 960 CRN pairs,
zero BAD, `ARM CHECK` PASS over all 96** (asserting the arm, the forced arena and `byObjective ==
{Sabotage}`).

## Result — the same lever, on two objectives

    HACK, ONE site, in the room                  SABOTAGE, THREE sites, one in the room
    rung    OFF     ON    delta                  rung    OFF     ON    delta
    h0     66.2   91.9   +25.6                   h0     40.6   59.4   +18.8
    h4     40.0   94.1   +54.1                   h4     24.7   37.5   +12.8
    h8     18.1   90.6   +72.5                   h8      5.0   12.2    +7.2
    (P49, before the garrison)                   POOLED +12.92, n_disc 334, MDE 5.33, z +6.78

**THE EASING SHRINKS WITH HEAT INSTEAD OF EXPLODING.** On one site it went +25.6 / +54.1 / +72.5 and
the ladder went flat — h8 was as winnable as h0. On three sites it goes +18.8 / +12.8 / +7.2 and the
ladder keeps its shape: 59.4 / 37.5 / 12.2, monotone, with the apex still the apex. Every rung
resolves, so this is a measured difference and not a shrug.

And decision richness goes UP instead of collapsing:

    meaningfulChoicesPerTurn   h0 1.844 -> 4.082  +2.238  t +19.9   RESOLVED
                               h4 1.888 -> 3.932  +2.044  t +17.6   RESOLVED
                               h8 1.469 -> 3.261  +1.792  t +26.7   RESOLVED
    turnsWithAShotPct          h0 52.67 -> 65.17 +12.500  t  +7.1   RESOLVED

The census says the same thing from the other side:

    objective / board                  missions   turns   deaths   losses
    SABOTAGE, shipping                     3760    4.45    6,636    1,316
    SABOTAGE, room + garrison              3991    5.27    6,126    1,234    deaths -8%, LONGER
    HACK, shipping                         4071    4.11    5,592    1,091
    HACK, room + garrison                  4588    3.70    2,312      465    deaths -59%, shorter

On three sites the authored board **keeps the attrition and makes missions longer**. On one site it
**halved the attrition and made them shorter**. Same room, same garrison, same walls.

## The finding

**P49's collapse was the SINGLETON OBJECTIVE, not the room.** A room with an objective in it is a
good object — it adds two meaningful choices a turn and twelve points of shooting — *as long as it
is not the only place the mission can be won.* Put the one thing the mission needs behind one door
and the mission becomes that door; put one of three there and the room is a decision.

That also reframes the three waves before it. It is not "authored buildings are dangerous". It is:

    P42   a rectangle with no reason to enter        -> buys hunkering, costs choices
    P48   a room with a door and a firing platform   -> trades position choices for target choices
    P49   the mission's ONLY site inside it, empty   -> removes the fight
    P50   the same, held                             -> the fight comes back, the mission stays easy
    P51   one of THREE sites inside it, held         -> choices up, attrition kept, ladder intact

## Verdict — still OFF, and now for a precise reason

`SIGHTLINE_SITEGLYPHS` stays default OFF, but the blocker is no longer "authored rooms are too easy".
It is one glyph: **CITADEL's `T` is the mission's only site and it is inside the room.** The template
declares five site kinds and four of them are fine; the terminal is the one that breaks.

The next lever is therefore small and named: **move `T` out of the room** (the room keeps the
captive, the garrison and one charge), then re-run P49's own HACK round against its archive. If the
HACK easing comes down to SABOTAGE's shape, the content earns its default.

## Reproducing

    BIN=runbin/P51 OUT=docs/measurements/p51 \
      EXTRA="SIGHTLINE_SITEGLYPHS=0 SIGHTLINE_MAP=4 SIGHTLINE_OBJ=sabotage" \
      bash docs/measurements/p15/run_chunk.sh off-h0-b0 0 0 10
    # ... over H in {0,4,8}, B in {0,10,...,150}, both arms, then:

    python3 docs/measurements/p51/armcheck.py docs/measurements/p51
    python3 docs/measurements/l6/pairs.py     off docs/measurements/p51 on docs/measurements/p51
    python3 docs/measurements/p51/richness.py docs/measurements/p51 off on
