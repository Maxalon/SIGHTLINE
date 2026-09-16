# P50 — THE GARRISON: what a room is for

**Base commit `714e5a1`** (`main` after PROGRAM PARALLAX milestone 43). Heat PINNED. Runner:
`docs/measurements/p15/run_chunk.sh`. Binary: a snapshot in `runbin/P50/` (gitignored).

## The lever

P49 measured that seating the objective in a room with **nobody home** removes the fight. This seats
a pod inside it, through the `A` enemy-pod anchor glyph P26 shipped three programs ago and **nothing
ever read** — `plan.Anchors` was parsed and discarded, and `Mission.ArenaAnchors` was a restore flag
with no consumer.

    arm off   SIGHTLINE_ARENAANCHORS=0     the room, sites inside it, nobody home  (= P49's ON arm)
    arm on    SIGHTLINE_ARENAANCHORS=1     the same room, with a pod holding it

**Both arms run `SIGHTLINE_SITEGLYPHS=1`**, so the garrison is the only lever. Same instrument as
P49: `SIGHTLINE_MAP=4 SIGHTLINE_OBJ=hack`, every mission a HACK on CITADEL. **96 chunks, 1,920
campaigns, 960 CRN pairs, zero BAD, `ARM CHECK` PASS over all 96** (which asserts `siteGlyphs: true`
on both arms as well as the arm's own lever, the forced arena and the forced objective).

## THE BRIDGE IS EXACT

P50's OFF arm reproduces P49's ON arm on **every** field — 91.9 / 94.1 / 90.6 win rate, 4,601
missions, 3.37 avg turns, 1,410 soldier deaths, 249 losses. Two rounds, two binaries, identical
outcomes. The cross-round comparisons below are licensed by that.

## Result 1 — win rate: flat at the bottom, and the APEX GETS ITS TEETH BACK

    rung    n    OFF%    ON%   delta    b    c  n_disc   MDE       z
    h0    320    91.9   94.7    +2.8   17   26      43  5.74   +1.37
    h4    320    94.1   91.2    -2.8   26   17      43  5.74   -1.37
    h8    320    90.6   59.7   -30.9  114   15     129  9.94   -8.72   RESOLVED
    POOLED 960 pairs: n_disc = 215, delta -10.31, MDE 4.28, McNemar z = -6.75

h0 and h4 do not resolve — **the garrison costs the same headcount it moves**, because the anchored
pod is pod 0 of the force the mission was already going to field, relocated rather than added. What
changes at h8 is that those same slots are heat-8 bodies with heat-8 stats, standing between the
squad and the terminal. **The un-garrisoned room made heat irrelevant (h0 91.9 -> h8 90.6, flat);
the garrison restores the gradient (94.7 / 91.2 / 59.7).**

## Result 2 — the fight comes back, and this is the finding

    metric                     rung    OFF      ON     delta   clSE       t
    meaningfulChoicesPerTurn   h0     1.179   6.774   +5.595  0.097   +57.7   RESOLVED
                               h4     1.340   6.601   +5.261  0.117   +44.9   RESOLVED
                               h8     0.772   3.703   +2.931  0.067   +43.6   RESOLVED
    turnsWithAShotPct          h0    36.750  86.719  +49.969  0.981   +50.9   RESOLVED
                               h4    37.156  85.550  +48.394  0.969   +49.9   RESOLVED
                               h8    28.869  60.575  +31.706  0.872   +36.4   RESOLVED

    soldier deaths  1,410 -> 2,312 (+64%)   losses 249 -> 465 (+87%)   turns 3.37 -> 3.70

**A shot happens in 87% of turns instead of 37%, and a soldier has 6.8 meaningful choices a turn
instead of 1.2.** That is not a repair of P49's collapse — it is well past where the board started.

## The three boards, one instrument, side by side

    board                        win h0/h4/h8      choices h0/h4/h8   shots% h0   deaths  losses
    literal sites (shipping)     66.2/40.0/18.1    4.06/3.93/2.06        54.3      5,592   1,091
    room, prize inside, empty    91.9/94.1/90.6    1.18/1.34/0.77        36.8      1,410     249
    room, prize inside, HELD     94.7/91.2/59.7    6.77/6.60/3.70        86.7      2,312     465

**The garrison is the difference between a room that removes the fight and a room that concentrates
it**, and on the metric that IS pillar 3's second-to-second half it beats the shipping board by a
wide margin at every rung.

## Verdict — the mechanism ships ON, the content stays OFF

`Mission.ArenaAnchors` now has a consumer and stays **default on**: it is inert by construction while
no template declares an `A`, and the only template that does is behind `SIGHTLINE_SITEGLYPHS`, which
**stays default OFF**.

Why the content still waits: at h0 and h4 the composite board is 94.7% and 91.2% against the shipping
board's 66.2% and 40.0%. The garrison fixes the FIGHT and does not fix the DIFFICULTY — a HACK whose
terminal sits in one room is a mission with one place to be, and one place to be is easy however
hard the fight there is. The next lever is about that geometry (a second site, a second door, or the
room's force counted ON TOP of the mission's headcount rather than out of it), not about the fight
inside the room, which this round says is now good.

## Reproducing

    dotnet build -c Release && mkdir -p runbin/P50 && cp -r bin/Release/net8.0/* runbin/P50/

    BIN=runbin/P50 OUT=docs/measurements/p50 \
      EXTRA="SIGHTLINE_ARENAANCHORS=0 SIGHTLINE_SITEGLYPHS=1 SIGHTLINE_MAP=4 SIGHTLINE_OBJ=hack" \
      bash docs/measurements/p15/run_chunk.sh off-h0-b0 0 0 10
    # ... over H in {0,4,8}, B in {0,10,...,150}, both arms, then:

    python3 docs/measurements/p50/armcheck.py docs/measurements/p50
    python3 docs/measurements/l6/pairs.py     off docs/measurements/p50 on docs/measurements/p50
    python3 docs/measurements/p50/richness.py docs/measurements/p50 off on
