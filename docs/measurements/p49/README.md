# P49 — PUTTING SOMETHING IN THE ROOM, AND FINDING OUT WHY THAT IS NOT ENOUGH

**Base commit `d23cc6c`** (`main` after PROGRAM PARALLAX milestone 42). Heat PINNED. Runner:
`docs/measurements/p15/run_chunk.sh`. Binary: a snapshot in `runbin/P49/` (gitignored).

## The lever

P48 gave CITADEL an inside. This puts the objective in it: `T` seats the HACK terminal in the
interior's far corner from the room's one door, `C` seats the RESCUE captive in the other.

    arm off   SIGHTLINE_SITEGLYPHS=0     the room, sites at their literal tiles (P48's board)
    arm on    SIGHTLINE_SITEGLYPHS=1     the room, with the objective inside it

One glyph pair. Nothing else in the tree differs between the arms.

## The instrument: DOUBLY forced

P48 established that a single arena cannot be priced through the shipped distribution (9 discordant
campaigns in 960). This lever is narrower still — it only bites on the objectives the arena declares
— so the instrument is forced on BOTH axes:

    SIGHTLINE_MAP=4 SIGHTLINE_OBJ=hack      every mission is a HACK, on CITADEL

**96 chunks, 1,920 campaigns, 960 CRN pairs, zero BAD, `ARM CHECK` PASS over all 96** — and that
check asserts the double force on the artifact itself (`byArena == {4}` and `byObjective == {Hack}`
on every chunk), because P48 was bitten twice by a forcing flag that silently did nothing.

**A doubly-forced round's win rate may NEVER be read against the heat band.** It prices the
INTERACTION of one arena with one objective, which is what this lever is.

## Result — resolved at every rung, and it is a disaster

    rung    n    OFF%    ON%   delta    b    c  n_disc   MDE       z
    h0    320    66.2   91.9   +25.6   18  100     118  9.50   +7.55
    h4    320    40.0   94.1   +54.1    3  176     179 11.71  +12.93
    h8    320    18.1   90.6   +72.5    1  233     234 13.38  +15.17
    POOLED 960 pairs: n_disc = 531, delta +50.7, MDE 6.72, McNemar z = +21.13

**Ninety per cent at every rung, including the apex.** Heat stops mattering: h8 goes from 18.1% to
90.6%. That alone is diagnostic — a rung that buys nothing is a mission the difficulty cannot reach.

And decision richness collapses, resolved at every rung on every metric (paired over the 16 CRN slot
sets, cluster t on 15 df):

    meaningfulChoicesPerTurn      h0 4.059 -> 1.179  -2.880  t -51.5
                                  h4 3.932 -> 1.340  -2.591  t -29.4
                                  h8 2.059 -> 0.772  -1.287  t -17.7
    turnsWithAShotPct             h0 54.31 -> 36.75 -17.562  t -19.2
                                  h4 50.41 -> 37.16 -13.256  t -14.3
    targetChoices/armed-turn      h8 0.527 -> 0.254  -0.273  t -10.7

And the census says plainly what happened:

    soldier deaths   5,592 -> 1,410     (-75%)
    loss causes      1,091 ->   249
    avg mission      4.11 -> 3.37 turns

## The finding

**A terminal behind one door with nothing seated inside is not a reason to go in. It is a place the
fight cannot follow you into.** The squad walks in, hacks, and wins; almost nobody dies; the mission
is over in three turns; the player makes one meaningful choice a turn instead of four.

P42 measured that a rectangle of wall gives cover without giving a reason to enter, and buys
hunkering. P48 measured that a room trades position choices for target choices. **P49 measures the
third case and it is the worst of the three: an UNCONTESTED objective inside a room removes the
fight entirely.** The room is not a crucible, it is a bunker with the prize already in it.

## Verdict

**Default OFF**, as `Mission.Buildings` has been since P42. The machinery is right and is kept — the
format expresses the room, `PlanBoard` seats the sites in it, the gates hold and are run against the
feature rather than the default. What is missing is a **GARRISON**: P26 shipped an `A` enemy-pod
anchor glyph and no template has ever used it. A room you must FIGHT your way into is a different
object again, and it is the next lever.

## The defect this round found, which nothing else could have

`Mission.PlanBoard` read `Maps.Layouts[cand]` — the RAW template row — and P47 had made a template
able to be double-resolution while P48 made one of them so. So the site planner was handed the
LEGACY 11-row CITADEL, saw no glyphs, and silently declined the arena-owned path on the one arena
that had sites to offer: a forced-CITADEL HACK mission built a PROCEDURAL board with a literal
terminal on it. It was invisible in P48 (no glyph existed) and armed the instant P49 added one.

**A screenshot found it.** Every assertion in the tree was about the TEMPLATES; nothing asked what
`Build` actually stamped. `ARENASITETEST` legs (A), (B) and (E) now read the effective arena through
`Maps.Source`, and (E) counts an EDGE as terrain at a site — without which a room built entirely of
boundaries reports "no terrain at the site" and the gate fails the one template doing it right.

## And one gate that was never as pinned as it claimed

`SIGHTLINE_BOARDTEST`'s cover-merge gate chose its probe pair from whatever board the seed dealt,
and a volume's procedural material is keyed on its root tile INDEX — so P49's extra RNG draws moved
the pair from (9,7) to (13,7), changed the material, and the SAME merged volume read a 4px trough
against a `<= 3` threshold. The predicate is also a RATIO of the top face's own median, which moves
with the biome palette (89.2 luma on one board, 54.7 on another).

Measured over six positions on one frame:

    merged, bright board    1  1  1  1  1  1          median 1
    merged, dark board      4  1  5  1  2  1          median 1.5
    SIGHTLINE_COVERMERGE=0 10 12  3  7  7 15          median 7

The WORST is not usable in either direction — a merged volume reaches 5 and the unmerged control
reaches 3. The gate now stamps six pairs and reads the **median**, which separates them by a factor
of four or more and cannot be moved again by an upstream change that re-deals the world.

## Reproducing

    dotnet build -c Release && mkdir -p runbin/P49 && cp -r bin/Release/net8.0/* runbin/P49/

    BIN=runbin/P49 OUT=docs/measurements/p49 \
      EXTRA="SIGHTLINE_SITEGLYPHS=0 SIGHTLINE_MAP=4 SIGHTLINE_OBJ=hack" \
      bash docs/measurements/p15/run_chunk.sh off-h0-b0 0 0 10
    # ... over H in {0,4,8}, B in {0,10,...,150}, both arms, then:

    python3 docs/measurements/p49/armcheck.py  docs/measurements/p49
    python3 docs/measurements/l6/pairs.py      off docs/measurements/p49 on docs/measurements/p49
    python3 docs/measurements/p49/richness.py  docs/measurements/p49 off on
