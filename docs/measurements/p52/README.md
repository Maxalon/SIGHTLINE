# P52 — THE SINGLETON COMES OUT, AND THE MISSION WALKS PAST THE FIGHT

**Base commit `4ba84b3`** (`main` after PROGRAM PARALLAX milestone 45, P51 THE SPLIT). Heat PINNED.
Runner: `docs/measurements/p15/run_chunk.sh`. Binary: a snapshot in `runbin/P52/` (gitignored).

## The question P51 left, and why this round exists

P51 measured the same lever on two objectives and concluded:

> **P49's collapse was the SINGLETON OBJECTIVE, not the room.** A room with an objective in it is a
> good object — as long as it is not the only place the mission can be won.

and named the next lever exactly: **move `T` out of the room, then re-run P49's own HACK round.**
This is that round. It is a direct test of a published claim, which is the only reason to spend it.

## The lever

`Maps.CitadelEdged` redrawn. The two SINGLETON objectives come out of the walled room:

    T  HACK terminal   (8,3) inside  ->  (4,5)  two tiles west of the room's one door
    C  RESCUE captive  (9,6) inside  -> (14,7)  far east

The room keeps the **GARRISON** (`A` at (7,6), P50's lever) and **one of the three sabotage charges**
(`X` at (8,3)) — so it stays P51's good object, a defended position, and only the singletons move.

    arm off   SIGHTLINE_SITEGLYPHS=0    the shipping board (literal sites, no room content)
    arm on    SIGHTLINE_SITEGLYPHS=1    the room, its garrison, and the singletons OUTSIDE it

Instrument, unchanged from P49 so the archive is readable against it: `SIGHTLINE_MAP=4
SIGHTLINE_OBJ=hack`, every mission a HACK on CITADEL. **96 chunks, 1,920 campaigns, 960 CRN pairs,
zero BAD, `ARM CHECK` PASS over all 96**, `LEAK-CHECK PASS` on both arms (48/48 chunks pinned,
`campaignsRaised = missionsAbovePin = 0`). ON arm: `byArena == {4}`, `byObjective == {Hack}`,
**zero procedural fallbacks**, `arenaFunnel` 103/103 authoredApplied.

## THE BRIDGE IS EXACT

P52's OFF arm reproduces P49's OFF arm on **960 of 960 paired campaigns, zero discordant**. Two
binaries, four waves apart, identical outcomes — so the cross-round table below is licensed rather
than assumed. (P50's README establishes the other link: its OFF arm reproduces P49's ON arm on every
field.)

## Result — THE CLAIM DOES NOT SURVIVE. The easing is unchanged.

    rung    n    OFF%    ON%   delta    b    c  n_disc   MDE       z
    h0    320    66.2   94.4   +28.1   12  102     114  9.34   +8.43
    h4    320    40.0   95.6   +55.6    3  181     184 11.87  +13.12
    h8    320    18.1   87.2   +69.1    1  222     223 13.07  +14.80
    POOLED 960 pairs: n_disc = 521, delta +50.94, MDE 6.66, McNemar z = +21.42

P49, with the terminal INSIDE the empty room, read **+25.6 / +54.1 / +72.5, pooled +50.7**. P52
reads **+28.1 / +55.6 / +69.1, pooled +50.9**. **Moving the singleton out changed nothing that the
round can resolve.** The ladder is still flat — h8 is 87.2% — and heat still stops mattering.

## The four boards, one instrument

    board                                win h0/h4/h8      choices  shots%  deaths  losses
    literal sites (shipping)             66.2/40.0/18.1     4.06     54.3    5,592   1,091
    P49  room, prize IN, EMPTY           91.9/94.1/90.6     1.18     36.8    1,410     249
    P50  room, prize IN, HELD            94.7/91.2/59.7     6.77     87.0    2,312     465
    P52  room HELD, prize OUTSIDE        94.4/95.6/87.2     8.15     83.9    1,530     303

**Read the last two rows against each other, because that is the finding.** P52 is the RICHEST board
of the four — 8.15 meaningful choices per turn, the highest this project has measured, and every
richness metric resolved:

    meaningfulChoicesPerTurn      h0 4.059 -> 8.154  +4.096  t +36.3
                                  h4 3.932 -> 8.420  +4.488  t +38.6
                                  h8 2.059 -> 5.080  +3.021  t +36.6
    turnsWithAShotPct             h0 54.31 -> 83.85 +29.537  t +40.3
    positionChoices/armed-turn    h8 1.396 -> 2.352  +0.956  t +22.5
    targetChoices/armed-turn      h8 0.527 -> 0.192  -0.335  t -14.8

— and yet the apex went **59.7 -> 87.2** and attrition fell **2,312 -> 1,530 deaths**. More
choices, more shooting, and far fewer dead soldiers.

## The finding — it is not CARDINALITY, it is whether the mission is FORCED through the fight

P51's hypothesis was that three sites behave differently from one *because there are three*. That is
not the mechanism, and this round is what shows it. **The mechanism is in the win condition, and it
is one line of code:**

    src/Game.cs:4470   if (SabotageBlown.Count >= SabotageSites.Count) EnterBarracks();

**SABOTAGE requires EVERY site.** One of CITADEL's three charges is inside the held room, so a
SABOTAGE mission *cannot be completed* without entering it — the room is on the critical path by the
objective's own rule. HACK requires **one** terminal, and with that terminal outside the room the
garrison becomes a threat you can shoot at across open ground and never close with. The squad hacks
and leaves.

So P51's finding should be restated:

> **A held room changes the mission only when the win condition forces the squad into it.** Site
> count is a proxy for that and not the thing itself — three sites mattered because ALL THREE are
> required and one of them is inside.

This is P26's archive result arriving from a different direction: **declining the fight is optimal**
(skill is worth +0.2 points; 23.3% of the deployed force is ever killed). Give the player a fight
they are allowed to decline and they will decline it — even while the instruments record a rich,
noisy, shot-filled board. **Choices per turn and turns-with-a-shot went UP while the mission got
easier at every rung**, which is the sharpest counter-example this project has to reading richness
as difficulty.

## Verdict

**`SIGHTLINE_SITEGLYPHS` stays default OFF, and the reason has moved again.** It is no longer "the
prize is uncontested" (P49) or "one place to be" (P50/P51). It is that on this board the objective
is not on the critical path through the contested space, in either of the two placements tried.

The content is not wrong — it is the best board of the four on every richness metric — but a board
that is 87% winnable at the apex is not shippable, and no further glyph-shuffling should be spent
before the next lever is chosen by the restated rule above rather than by site count.

## The gate this round leaves

`Maps.SitesDoorLocked` (shipped in this wave, `ARENAEDGETEST` leg (G)) enforces P51's roadmap item:
no objective category may have ALL of its sites behind one door. It is correct and it is kept — a
singleton behind one door really is the P49 board — **but note what it does NOT say**, because that
is this round's result: it cannot tell you whether the mission is forced through the room, only that
it is not sealed inside it. Those are different properties and only the first one is gated.

## Reproducing

    dotnet build -c Release && mkdir -p runbin/P52 && cp -r bin/Release/net8.0/* runbin/P52/

    BIN=runbin/P52 OUT=docs/measurements/p52 \
      EXTRA="SIGHTLINE_SITEGLYPHS=0 SIGHTLINE_MAP=4 SIGHTLINE_OBJ=hack" \
      bash docs/measurements/p15/run_chunk.sh off-h0-b0 0 0 10
    # ... over H in {0,4,8}, B in {0,10,...,150}, both arms, then:

    python3 docs/measurements/p52/armcheck.py docs/measurements/p52
    python3 docs/measurements/l5/cluster.py   off --dir docs/measurements/p52
    python3 docs/measurements/l5/cluster.py   on  --dir docs/measurements/p52
    python3 docs/measurements/l6/pairs.py     off docs/measurements/p52 on docs/measurements/p52
    python3 docs/measurements/l6/pairs.py     off docs/measurements/p49 off docs/measurements/p52   # the bridge
    python3 docs/measurements/p52/richness.py docs/measurements/p52 off on

The RESCUE leg (roadmap item 2) is under `rescue/` with its own `armcheck.py` and README — same
arms and rungs with `SIGHTLINE_OBJ=rescue`. **It did not behave like HACK, and neither did its
SHIPPING arm**: 92.2 / 93.4 / 12.8 against HACK's 66.2 / 40.0 / 18.1, at 9-11 meaningful choices a
turn against 4.06. The easing is +7.2 / +5.9 / +15.3 (pooled +9.5, n_disc 159) — small at h0/h4
only because that baseline is already at the ceiling. Read its README before quoting the +9.5.

## A process note, because it nearly cost the round

The first attempt ran **two driver instances concurrently against the same chunk tags** — a detached
launcher survived a step it was assumed not to. It was caught on an arithmetic tell (121 status
lines for 96 chunks) and **every byte of it was discarded**; the round above was re-run from a fresh
snapshot with an `flock` on the driver. Nothing in this directory comes from that attempt. The
lesson is the ordinary one for this repository: the check that caught it was a COUNT, not a gate,
and there was no gate that would have.

Also worth writing down: `l5/cluster.py` takes a **PREFIX**, not a directory. Given a directory it
matches zero chunks and prints `LEAK-CHECK: PASS (all 0 chunks pinned)` — a vacuous pass that looks
exactly like a real one. Pass the arm.
