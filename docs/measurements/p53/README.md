# P53 — THE CONTROL P52 NEVER HAD, AND IT CORRECTS P52

**Base commit `c10ac03`** (`main` after PROGRAM PARALLAX milestone 46, P52). Heat PINNED. Runner:
`docs/measurements/p15/run_chunk.sh`. Binary: a snapshot in `runbin/P53/` (gitignored).
**96 chunks, 1,920 campaigns, 960 CRN pairs, zero BAD, `ARM CHECK` PASS over all 96,
`LEAK-CHECK PASS` on both arms**, doubly forced (`byArena == {4}`, `byObjective == {Sabotage}`).

## Why

P52 published this rule:

> **A held room changes the mission only when the WIN CONDITION forces the squad into it.** Site
> count is a proxy for that and not the thing itself.

It was read off SABOTAGE (three charges, ALL required, one inside the room — good behaviour) against
HACK (one terminal, outside — easy). **That is two objectives at once: an observational comparison,
not a controlled one.** The roadmap's next item was to BUILD the rule's prediction (a HACK requiring
two terminals), which would have been building on an unfalsified claim. This round runs the control
first, and it costs one derived template instead of a new win condition.

## The lever

    arm in    SIGHTLINE_ROOMSITE=1    the shipped board — 1 of 3 required charges inside the room
    arm out   SIGHTLINE_ROOMSITE=0    the same board with that charge moved to (10,9), outside

**Both arms run `SIGHTLINE_SITEGLYPHS=1`** and both have THREE charges, so this prices *where the
required site is*, not *how many there are*. `ARENAEDGETEST` leg (H) asserts exactly two cells
change and the count is 3 either way.

**Prediction, stated in the commit before the round:** if the rule holds, the arm reads like P52's
HACK — easy, flat ladder. **It does not.**

## THE BRIDGE IS EXACT

P53's IN arm reproduces **P51's ON arm on 960 of 960 paired campaigns, zero discordant**. Two
binaries, two waves apart — which also proves, for free, that **P52's relocation of `T` and `C` is
inert for SABOTAGE.**

## Result — directionally right, an order of magnitude too small, and at the wrong rung

    rung    n     IN%   OUT%   delta    b    c  n_disc   MDE       z
    h0    320    59.4   71.6   +12.2   31   70     101  8.79   +3.88   RESOLVED
    h4    320    37.5   50.6   +13.1   47   89     136 10.20   +3.60   RESOLVED
    h8    320    12.2    9.7    -2.5   32   24      56  6.55   -1.07   NOT RESOLVED
    POOLED 960 pairs: n_disc = 293, delta +7.60, MDE 4.99, McNemar z = +4.26

Emptying the room of required work **does** make the mission easier — the rule's direction is real
and resolved at two rungs. But the arm reads **71.6 / 50.6 / 9.7**: still monotone, still a ladder,
apex still ~10%. **It is nothing like P52's HACK board (94.4 / 95.6 / 87.2).** The effect is
+12-13 points where the thing it was invented to explain is a 35 / 58 / 75-point gap.

    meaningfulChoicesPerTurn   h0 4.082 -> 4.950  +0.868  t +6.6   RESOLVED
                               h4 3.932 -> 4.737  +0.805  t +6.5   RESOLVED
                               h8 3.261 -> 3.786  +0.525  t +9.7   RESOLVED
    turnsWithAShotPct          h0 65.17 -> 70.01  +4.838  t +2.8   RESOLVED
    missions 3,991 -> 4,140;  soldier deaths 6,126 -> 5,504;  losses 1,234 -> 1,081

## THE FINDING — the same lever, on two objectives, moves OPPOSITE ENDS of the ladder

Both halves are CRN-paired on the same 16 slot sets and the same forced instrument:

    removing the room's required site      room holds      h0      h4       h8
    HACK      (P50 ON -> P52 ON)           1 of 1        -0.3    +4.4   +27.5  RESOLVED
    SABOTAGE  (P53 in -> out)              1 of 3       +12.2   +13.1    -2.5  ns
                                                          ^^      ^^      ^^
                                                       RESOLVED         RESOLVED

*(HACK row: n_disc 33 / 42 / 114, MDE 5.0 / 5.7 / 9.3. SABOTAGE row: n_disc 101 / 136 / 56, MDE
8.8 / 10.2 / 6.6. The two "ns" cells are absences of evidence at those n, not measured zeros — C2's
rule.)*

**When the room holds the mission's ONLY required site, emptying it matters at the APEX and nowhere
else.** At h0/h4 the squad wins either way, so the room is not what decides the campaign; at h8 it
is the whole mission, and removing it is worth +27.5.

**When the room holds one of three, emptying it matters at the LOW rungs and not at the apex.** At
h0/h4 the marginal charge is real work removed; at h8 the other two charges already dominate, and
the room's contribution disappears into them.

So neither P51 nor P52 had it. The statement both rounds were reaching for:

> **A mission's difficulty is carried by how much REQUIRED WORK it has, and by what share of that
> work sits in contested space. Cardinality sets the floor — a three-site mission keeps a ladder
> whatever you do with the room. The room's SHARE of the required work decides which RUNG responds
> to it.**

P51's cardinality claim and P52's win-condition claim are two faces of that, and each was measured
on a board where the other was held constant, which is why each looked like the whole answer.

## What this costs P52

**P52's rule stands as a direction and is WITHDRAWN as an explanation.** It does not account for the
HACK collapse: emptying the room of required work buys 12-13 points, and the collapse is 35-75. The
CLAUDE.md row and DEVLOG entry for P52 are corrected in the commit that carries this round rather
than left to be quoted.

**The roadmap item this was going to justify — "build a HACK that needs two terminals" — is now a
worse bet than it looked.** On the evidence here, a two-terminal HACK with one inside the room would
buy roughly the SABOTAGE shape at the low rungs and little at the apex, and the apex is where the
HACK board is broken (87.2%). Any lever aimed at that board has to add required work, not relocate
it — and the apex has now declined to respond to accuracy (P24), to bodies at the finale (L7/P23)
and to room geometry (this round).

## Reproducing

    dotnet build -c Release && mkdir -p runbin/P53 && cp -r bin/Release/net8.0/* runbin/P53/

    BIN=runbin/P53 OUT=docs/measurements/p53 \
      EXTRA="SIGHTLINE_SITEGLYPHS=1 SIGHTLINE_MAP=4 SIGHTLINE_OBJ=sabotage SIGHTLINE_ROOMSITE=1" \
      bash docs/measurements/p15/run_chunk.sh in-h0-b0 0 0 10
    # ... over H in {0,4,8}, B in {0,10,...,150}, both arms, then:

    python3 docs/measurements/p53/armcheck.py docs/measurements/p53
    python3 docs/measurements/l5/cluster.py   in  --dir docs/measurements/p53
    python3 docs/measurements/l6/pairs.py     in  docs/measurements/p53 out docs/measurements/p53
    python3 docs/measurements/l6/pairs.py     on  docs/measurements/p51 in  docs/measurements/p53  # bridge
    python3 docs/measurements/l6/pairs.py     on  docs/measurements/p50 on  docs/measurements/p52  # the HACK row
    python3 docs/measurements/p53/richness.py docs/measurements/p53 in out

`probe/` holds the P48-mandated one-chunk probe, archived rather than discarded — see its README for
why the DISCORDANT count, not the `levers{}` block, is the half that proves the dial is wired.
