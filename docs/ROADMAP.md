# SIGHTLINE — Roadmap (history + open work)

> Extracted from `CLAUDE.md`. Phases 1–5 are **complete**; what remains is
> open-ended polish (listed at the end of Phase 5 and in the "OPEN/NEXT" notes of
> `docs/DEVLOG.md`). Kept for provenance — the checkbox history shows how the game
> was built and why each system exists.

## ROADMAP — pick up here (ordered by impact)

## ⚑ OWNER DIRECTION (2026-10-01) — WHAT THE GAME CAN BECOME. This is the top of the list.

**Rationale: `docs/DESIGN.md` §6.6.** About 90% of missions on BIG boards (18x11 only for the tutorial
and the opening missions), boards **bigger than 36x22 and VERTICAL** (an XCOM-scale housing block, or
a ~6-floor tower), a **within-run power curve** sized for a roguelike whose soldiers retire after one
run, and the **projected view as the default**. The 2026-09-16 block below stays in force; this
block re-orders it and adds to it.

### The order of work, and why this order

- [x] **A. THE PROJECTED VIEW IS THE DEFAULT — P61.** The real launch opens in the projected view
      (`View3D.LaunchEnabled`); `I` flips it and the choice persists (`Display.FlatView`, absent in an
      old settings file = projected); `SIGHTLINE_VIEW3D=0` forces flat for one launch. Every harness
      path keeps the view it asks for explicitly. The opening framing now fits the board into the band
      between the HUD plates (`View3D.FitHudBand`) on every board size. `SIGHTLINE_VIEWDEFAULTTEST`.
      **Left open:** at Zoom > 1 the projected pan clamp still lets the edge rows reach the SCREEN
      edge, not the band edge (the flat view got that in P60).
- [ ] **B. A MISSION THAT DOES NOT END AT THE TASK — the extraction model, as the owner specified
      it** (`docs/DESIGN.md` §6.6, "The owner's answers"). P59: on a big board the task completes
      before the fight arrives, so every campaign wins. **It gates C.** Live on BIG boards only, so
      the 18x11 tutorial band and every gate pinned to it keep today's rules. In waves:
      - [x] **B1. BOARD and CALL EVAC — P62.** BOARD [J] takes a soldier in the zone off the field
            (`Game.Aboard`); the mission never ends on its own while a living unit is on the ground;
            CALL EVAC [Z] (one aboard, asset secured) wins with the aboard + in-zone and leaves the
            rest behind dead. Big boards, campaign, EVAC/ESCORT/RESCUE; `SIGHTLINE_EXTRACTION=0`
            restores; `SIGHTLINE_EXTRACTIONTEST`. **Not done here:** the zone is still the fixed
            top-right block (far from a west-deployed squad by geometry, not by rule), and a big-board
            EVAC still wins 8/8 — nothing opposes the walk until B3's reinforcements. Skirmish/daily
            on a big board keep the old rule (`CheckSkirmish` is untouched).
      - [x] **B2. HACK / SABOTAGE — P63.** On a big board the evac (a 2x4 block on the east edge, in
            the corner farther from the objective) is reserved at setup so Build keeps it clear,
            HIDDEN until the task is done, then opened (`Game.PendingEvac`/`EvacOpen`) and B1's
            rules apply. A cleared field wins outright, task or no task. The pressure clock fields
            no reinforcements there (`ClockMayReinforce`). `SIGHTLINE_EXTRACTIONTEST` legs (G)-(J).
            **Indication, not a result:** a 4+4-campaign batch at unpinned heat read HACK 62.5% /
            SABOTAGE 50% campaign wins where P59 read 100%. n=8; measure before quoting.
      - [x] **B3. RESCUE and ESCORT converge; the withdrawal is opposed — P64.** On a big board an
            ESCORT node plays and reads as RESCUE (`Game.EscortIsRescue`): the asset waits mid-board
            to be reached. Once it is, each player turn brings 1 hostile (2 from heat 4) onto the board
            EDGE of the extraction's half, never within 6 tiles of the exit, up to `4 + heat/2` a
            mission (`Game.WithdrawalCap`). The pressure clock's waves and the forward BEACON are off
            there. `SIGHTLINE_WITHDRAWAL=0` turns the trickle off. **Measured, forced RESCUE on 36x22, two
            slot sets × 16 campaigns per arm, paired: trickle ON 9/32 (28%), OFF 27/32 (84%).** That is a
            LEVEL change and it has not been tuned; the first cut, which spawned ON the exit, read 0/8.
      - [ ] **B4. STEAL — a new objective** (appended to `Objective`): pick up a carryable object,
            PASS it to an adjacent soldier, get it out through the extraction.
      - [ ] **B5. REMOVE THE VETERAN RESERVE.** Owner decision. Recall gone from the draft, stored
            veterans discarded on load, `StandingReserve`/`DeepReserve` re-meant as power-curve
            unlocks (members kept: append-only). `SIGHTLINE_VETTEST` is retired or repurposed.
- [ ] **C. THE CAMPAIGN'S BOARD CURVE.** Tutorial and the first missions on 18x11; most of the run on
      big boards, sized per node (`Cfg.SetBoard` is already runtime). Re-baseline the heat ladder on
      the new game: a new instrument, never compared with the 18x11 ladder of record.
- [ ] **D. BIGGER: THE HOUSING BLOCK.** Push past 48x30 toward XCOM scale. Measure what breaks first:
      planner cost per enemy act, Dijkstra per unit, draw cost in the projected view, fog of war.
      The authored arenas come back here as ROOMS / BLOCKS inside the big board (P37's cell grid
      already builds a big board as 18x11 cells; a cell can take an arena's terrain instead of an
      archetype). Decided by the owner's answer: the arenas become pieces of a big map, not the map.
- [ ] **E. STOREYS — the tower.** A storey axis in `Grid` (and in the save format: a new persisted
      field, append-only), plus stairs/ladders in `CostMap`, LoS across floors, cover by floor, the
      planner, and rendering by floor in the projected view (cut-away above the active storey). The
      largest single change in this list. Plan it as its own program.
- [ ] **F. THE POWER CURVE.** Within a run: a much steeper growth in soldier power (upgrades,
      abilities, synergies). Across runs: meta unlocks widen the OPTIONS (a richer upgrade pool, new
      abilities), never carried soldiers (they retire). A fresh run must feel unique through a big
      mission and map library. The heat ladder must then be set against the GROWN squad, which is
      why F is measured after C, not before.
      **The veteran conflict is RESOLVED: the owner chose removal — item B5.**

## ⚑ OWNER DIRECTION (2026-09-16) — THE BOARD IS THE COMMITMENT. This supersedes the insertion-frame plan.

**Rationale: `docs/DESIGN.md` §6.5. Read it before touching any item here.** The owner overrode the
standing "bigger *and denser*, **not raw size**" clause. Raw size IS the point: on 18x11 with ~6-tile
moves and two actions, every tile is reachable from every tile in about a turn, so **positioning
costs nothing and is therefore not a choice.** The dilemma a big board buys is: spend one or two FULL
turns relocating to better ground and be unable to fight while you do, or stay put with worse cover
and keep the action points.

**"Empty traversal = boredom" is not the counter-argument it looks like.** The fix is not SHORT
traversal, it is **CONTESTED** traversal — and the mechanism is given below.

**This retires `P38`'s insertion-frame rule** ("DEPTH stays at the reference, LATERAL goes to the
whole board"; "insert NEAR the objective and let size buy lateral choice, not distance"). That plan
was coherent and is kept below for provenance, but it was built to AVOID distance and distance is now
the goal. Its four seams still need parameterising — the transform they need is just the other one.

### The order of work, and why this order

- [x] **0. BOARD SIZE IS NOT A DIFFICULTY LEVER — ASSERTED, and learned the hard way.**
      *"We need a difficulty curve very clearly, and map size doesn't do that alone."* The squad does
      not grow with the board so the opposition must not either. **An attempt to scale the force by
      board area was reverted whole**: it lost autoplay on mission 1 in fifteen turns (16 hostiles
      vs 4 soldiers), i.e. it turned the board into a difficulty dial. `SIGHTLINE_BOARDNEUTRALTEST`
      now asserts the force requested and seated is identical at 18x11 / 24x15 / 36x22 across the
      opener, a mid-run node and the apex, and that the deploy cap does not vary. **The sparse
      feeling is real; the fix is items 2 and 1 below (contested traversal, fixed sight), never more
      starting bodies.**
- [x] **0b. THE HOSTILE FORCE IS SPREAD THROUGH A BIG BOARD'S DEPTH — P57.** `SIGHTLINE_MAPSHAPEPROBE`
      measured every pod massed against the far edge (36x22: cols 32-34, 3.2 turns from the squad;
      48x30: 44-46, 4.2 turns) because every `PodAnchor` is far-edge-relative. Pods are now pulled
      toward the squad by a fixed fraction of the extra width (36x22: 16-34, first contact 2.0 turns;
      48x30: 14-46, 2.5 turns). Same bodies (P56's gate still holds); nearest pod never closer than
      on 18x11; 18x11 byte-identical. `SIGHTLINE_DEPTHSPREAD=0` restores. ENVELOP: see 0f.
- [x] **0c. ZOOM OUT — P58.** Checked first: the projected view already frames the whole board at its
      zoom 1, but it is not the default. `Game.FlatZoomFloor` gives the default flat view a floor at
      which the whole board fits (36x22: 0.644, 48x30: 0.640), capped at 1 so 18x11 is untouched.
      `SIGHTLINE_FLATZOOMTEST` (picking round-trips 45/45 at the floor).
- [x] **0e. P59 MEASURED A BIG BOARD — AND IT IS TRIVIALLY EASY.** Forced objectives at heat 0 on a
      procedural size ladder: every objective saturates at 36 wide (Defend 99.5, Hack 100.0, Sabotage
      99.0) and **every campaign wins, 24 of 24 chunks at 36 and 48 wide.** The enemy acts but rarely
      shoots (Defend acts-with-a-shot 11.9 -> 4.4 -> 3.2 per mission) and task missions end in ~4
      turns — **the task completes before the fight arrives.** Map geometry alone cannot fix that;
      it is what item 2's extraction model is for. `docs/measurements/p59/`.
- [x] **0d. THE BOTTOM ROWS UNDER THE ACTION BAR — P60.** On a board that OVERFLOWS the screen,
      `Game.ClampPan` now solves its vertical bounds for the band between the top bar and the action
      bar (`Cfg.HudTopInset` 64 / `Cfg.HudBotInset` 132 — the bar at its usual two rows), so the
      first and last rows can be panned out from under the HUD at any zoom, the floor included. Keyed
      on the BOARD, not the zoom, so the bounds never jump; 18x11 fits and keeps the screen bounds
      exactly (its bottom row stays under the translucent bar, as designed). Autocam's own pre-P29
      clamp is gone — it uses `ClampPan` too (`Game.AutoCamPan`); on 36x22 the old one held it to
      ±247px of a ±480px range, so a soldier at the map edge could not be followed there.
      **Left open:** a unit whose verbs wrap to a THIRD row still overlaps the bottom row by 46px;
      and the PROJECTED view's own framing still puts the bottom rows under the bar on a big board.
- [x] **0f. ENVELOP ON A BIG BOARD — P60.** Its squad table and rim anchors were absolute 18x11
      coordinates, so on 36x22 the "surrounded" squad opened in the NW quarter (west pod 7 tiles off,
      east pod 26). Now the squad seat is carried to the board's centre and each rim pod is blended
      between the big rim and the reference ring by P57's `DepthFrac` (pod 0 on the far rim, pod 1 at
      the 18x11 standoff). 18x11 byte-identical; `SIGHTLINE_ENVELOPCENTRE=0` restores.
- [ ] **1. MAKE A BIG BOARD A FAIR TEST BEFORE MEASURING ONE. Top item.** Nothing has ever measured
      a big board, but measuring one today measures a BROKEN configuration: enemy count, mission
      pacing and sight range are all still 18x11 numbers, and the 35 authored arenas are out of play
      at any other size. **Note what item 0 removed from this item**: "enemy count" is NOT on the
      list any more — a constant force across board sizes is CORRECT and is now gated. What remains
      is mission pacing (turn budgets), the arenas, and sight range. Sight range is the one that
      changes the game most (9 tiles on an 18-wide board is half of it; on a 30-wide board the same
      number is 30% — that is also the fog-of-war prerequisite, already noted below).
- [ ] **2. THE EXTRACTION MODEL — and it is the answer P54/P55 went looking for.**
      **ESCORT must not start with the asset in the squad** ("why did you enter a combat zone with
      them in the first place?"). The asset is REACHED; only then does extraction begin; the exit is
      far enough that it takes multiple turns; and **reinforcements spawn one or two at a time from
      the half of the board the extraction point is in**, so the withdrawal is opposed and worsens
      the longer it takes.
      **This is what P55 could not buy with a constant.** P55 gave the asset a heat term: surgical
      where it aimed (Escort h8 41.8 -> 61.2) and almost no campaign win rate, because the asset's
      survivability was never the missing pressure — **the free, unopposed walk was.** It also
      reaches P55's declared-open half (Rescue 97.4 / Escort 88.9 at h4), which no heat term can.
      **It depends on item 1**: "far enough to take multiple turns" is not expressible on 18x11.
- [ ] **3. MISSION SHAPES a big board makes possible.** Enter one side, objective in the middle,
      extract on the other. A shorter variant that sends you to the far end and back to the start.
      And **bosses / longer challenges — raid a bunker or a bastion.**
- [ ] **4. THE ARENAS.** 35 hand-authored 18x11 templates go out of play at any other size. Decide
      between re-authoring at the new reference, tiling them as ROOMS inside a bigger board (P37's
      "more rooms, not one stretched room" applies directly), or accepting procedural-only on big
      boards for now. **P47's double-resolution format and P26's site glyphs are both ready for
      whichever is chosen.**

### The cost, stated up front so no round is surprised by it

**A big board severs the CRN chain on every axis at once.** `SIGHTLINE_BIGMAP` unset is the restore
arm. **Every number in CLAUDE.md's ladder of record is an 18x11 number and stays one** — a big board
is a different game and needs its own baseline, never a comparison against that ladder. (P48's rule
for a forced arena, one level up.)


## OPEN — left by PARALLAX P55 "THE ASSET ANSWERS HEAT" (2026-09-16)

P55 built and priced P54's lever. It is **surgical and large where it aims** (Escort h8 41.8 -> 61.2,
Rescue h8 35.2 -> 50.5, every other objective ≤1.3) and **near-inert on campaigns** (+0.0/+0.3/+1.2/
+1.6, 12 discordant in 1,280). At h8, **27 fewer NPC deaths bought 5 fewer lost campaigns** — so the
NPC death was largely a SYMPTOM. Shipped ON as a defect repair. Round: `docs/measurements/p55/`.

- [ ] **1. P54's SECOND DEFECT IS THE ONE LEFT, AND A HEAT TERM CANNOT REACH IT. Top item.**
      Rescue **97.4%** and Escort **88.9%** at h4, 96-98% at h0. An objective that is free before
      heat arrives is not an objective, and P55 moved neither by a single point there (by
      construction: `Heat.StatDelta(4)` is 1). **This is a DESIGN question, not a tuning one** — the
      escort/rescue mission has no failure mode at low heat because the asset is never meaningfully
      threatened.

      **⚠ TWO CORRECTIONS TO THIS ITEM, BOTH FROM READING THE CODE RATHER THAN GUESSING:**

      **(a) "Make the objective require the asset to ARRIVE" ALREADY EXISTS and was a wrong
      candidate.** `Game.cs`'s win checks are `EvacZone.Contains((Vip.X, Vip.Y))` for Escort and
      `!CaptiveLocked && EvacZone.Contains(...)` for Rescue — both already require the walk, and the
      evac zone is the far corner, so it is a cross-board escort. The mission is not too easy because
      arrival is unrequired; it is too easy because **arrival is unopposed**.

      **(b) THE REAL HOLE IS THAT NEITHER OBJECTIVE HAS A CLOCK.** `DefendTurns` is the only
      per-mission turn cap in the game. Measured on P55's own archive, Escort averages **9.31 turns
      at h0** — the longest objective in the game apart from Defend — and wins 95%. Rescue averages
      5.76. **A squad can advance one tile a turn behind full cover indefinitely and nothing
      punishes it**, so P26's finding (declining the fight is optimal) applies here with no
      counter-pressure at all. That is why heat is the only thing that ever makes these missions
      hard: heat is the only source of pressure they have.

      So the design question is narrower than this item first stated: **what supplies
      counter-pressure that is not difficulty?** A mission clock is the obvious candidate and is
      consistent with the "stakes that bite" pillar; a leash, a pursuing force, or accepting these
      as easy-by-design in `docs/DESIGN.md` are the alternatives. **None is priced, and the choice
      changes what the mission IS rather than how hard it is — it is a design call, not a tuning
      one.**
- [ ] **2. THE 22 CAMPAIGNS THAT KEPT THE ASSET ALIVE AND LOST ANYWAY are the apex's real content.**
      P55 converted NPC deaths into other losses almost one-for-one. That says the apex's difficulty
      is not located in any single labelled cause, and it is the strongest evidence yet for P54 item
      3's conclusion that the remaining question needs **instrumentation, not analysis**: a per-turn
      record of when the squad fell behind. Nothing in the archive can answer it.
- [ ] **3. A MEASURED LIMIT ON THE LOSS-CAUSE METHOD, to be quoted with it.** P54 introduced
      cross-tabbing `lossCause` by rung and it found a real defect. P55 shows the ceiling: **a
      loss-cause cross-tab says where losses are LABELLED, and the label can be downstream of the
      cause.** Any future wave reasoning from `lossCauses` should cite both.


## OPEN — left by PARALLAX P54 "THE APEX FAILS DIFFERENTLY" (2026-09-16)

P54 answered P53's top item from the existing archive with no new compute. **At h8, 26.8% of
campaign losses are the protected NPC dying; at h0 it is 0.38%** — replicated on four unforced
rounds, 5,365 pooled h8 losses. Rescue 63% / Escort 37%, landing m3/m4/m5 and never m6. **The finale
is where the FEWEST campaigns end at h8** (16%). So all three apex levers were aimed at the 73%.
Cause: `Mission.MakeVip` is `hp = 14 + 2*depth`, `armor = depth/2` — **no heat term** — while heat 8
gives the force ~+4 bodies, ~+4 stat, +1 dmg, AI tier 2. Full analysis: `docs/measurements/p54/`.

- [x] **1. GIVE THE PROTECTED ASSET A HEAT TERM, AND PRICE IT. DONE — P55, shipped ON.** The dose
      was the argued one (`Heat.StatDelta` HP / `Heat.DmgDelta` armor, clamped at 0) and the clamp
      delivered what it promised: h0 came back **0 discordant in 320**. Surgical (Escort h8
      41.8 -> 61.2, Rescue h8 35.2 -> 50.5, all others ≤1.3) and near-inert on campaigns. Both arms
      4 of 4 in band, and it lifts the two rungs P24 published as sitting ON their floors. The h4
      caution written into this item was correct — Rescue/Escort did not move there at all.
      Original text:
- [x] **1. (done) GIVE THE PROTECTED ASSET A HEAT TERM, AND PRICE IT.** One constant
      in one funnel covers both objectives (the captive is a renamed VIP). **The dose should be
      ARGUED, not searched** — the obvious principled pair is `Heat.StatDelta` for HP (the same +1
      per rung the force gets) and `Heat.DmgDelta` for armor (exactly cancels heat's damage bump),
      both clamped at 0 so that **h0 AND RECRUIT are provably inert** (`Heat.Active(0)` is empty,
      and RECRUIT's StatDelta is −1 which must not shrink the asset). That clamp is worth having for
      its own sake: it makes two rungs into inertness controls for the round.
      Ship it with `SIGHTLINE_VIPHEAT=0` and price it on the FREE instrument (not a forced arena) at
      h4/h6/h8. **Item 2 revised what the low rungs mean here**: h0 is a provable inertness control,
      but h4 is NOT — Rescue/Escort sit at 96.2%/91.5% there, so the round must report h4 as data
      and show the +1 HP did not make an already-free objective freer.
- [x] **2. RECOMPUTE THE RESCUE AND ESCORT WIN RATES BY RUNG. DONE — `objective_rungs.py`, and it
      changed item 1.** Rescue **97.9 / 96.2 / 40.0**, Escort **95.3 / 91.5 / 42.8** (mid-run
      97.3/96.2/**30.4** and 95.6/91.3/**36.2**). The two `MakeVip` objectives are the two FLATTEST
      on the ladder and then fall off a cliff; every other objective degrades gradually. **So they
      are broken at BOTH ends and that is TWO defects** — the asset does not scale with heat (the
      cliff) AND the objective is uncontested below it (96-98% is not an objective). A heat term is
      a candidate for the first only and must not be written up as fixing both.
      Method controls in the same run: W8's artifact reproduces (`Eliminate` 83.1 pooled vs 44.8
      mid-run), and **`Defend` is NON-MONOTONE** (70.3 / 59.9 / 71.1, n>3,000 per cell) — its own
      unexplained anomaly, and a candidate for its own wave.
- [x] **3. THE OTHER 73%. ATTEMPTED AND ANSWERED "NOT FROM THESE FIELDS" — `wipe_rungs.py`.**
      **It is not an archetype**: no archetype's share of soldier deaths grows more than +2.0 from
      h0 to h8 (SNIPER 5.7->7.7 is the largest rise; ELITE 11.1->6.0 the largest fall). The apex
      wipe is DIFFUSE, so a bestiary lever is not indicated. The squad's behaviour does change
      (HUNKER 9.6->4.0, OVERWATCH 2.7->6.6, MOVE 42.1->37.6) but as a RESPONSE; and shot
      concentration is non-monotone (soleOrDominant 38.5 / 35.1 / 46.1), most simply read as "at h8
      the squad is smaller".
      **STILL OPEN, but re-scoped: it needs INSTRUMENTATION, not analysis.** Every archive field is
      a per-mission aggregate; the question is about the TRAJECTORY inside a mission — when soldiers
      die, at what HP margin, whether the squad was ever ahead. No cross-tab of the existing chunks
      can answer it. **Do not re-attempt it from the archive.**
- [x] **4. AUDIT THE OTHER DEPTH-ONLY CONSUMERS. DONE — one of four, and it is `MakeVip`.**
      `OpenerTrim` is fine (a grace that subtracts bodies; heat reaches the count it trims from).
      `Combat.HvtHpBonus` is defensible (heat reaches the BODY through `Mission.MakeHostile`; only
      the target premium is depth-scaled, and W8 made its three constants pinnable). DEFEND waves
      read `Heat.StatDelta` explicitly. **The hunt is closed; do not re-audit this list.** Also
      recorded: in `Game.SetupMission` the asset is built sixteen lines BEFORE `int heat =
      _run.HeatLevel` is even read — P48's shape one level in. Original text:
- [x] **4. (done) AUDIT THE OTHER DEPTH-ONLY CONSUMERS.** `MakeVip` was found because a loss cross-tab
      pointed at it. P14's own comment lists FOUR consumers of "how deep is this fight" and fixed
      them on the MODE axis; **the HEAT axis was never checked for any of them.** `Combat.HvtHpBonus`
      (the Decapitate HVT) has exactly the same shape and Decapitate is a finale objective. Grep for
      `DepthFor` and ask, per call site, whether heat belongs there too.


## OPEN — left by PARALLAX P53 "THE CONTROL" (2026-09-16)

P53 ran the control P52's rule never had and **the rule is withdrawn as an explanation**. Removing
the room's required site, CRN-paired on one instrument:

    room holds         h0      h4       h8
    HACK      1 of 1   -0.3    +4.4   +27.5   RESOLVED at the apex only
    SABOTAGE  1 of 3  +12.2   +13.1    -2.5   RESOLVED at the low rungs only

> **Difficulty is carried by how much REQUIRED WORK a mission has, and by what SHARE of that work
> sits in contested space. Cardinality sets the floor; the room's share decides which RUNG responds.**

Full round: `docs/measurements/p53/`, DEVLOG §P53.

- [x] **1. THE APEX IS THE PROBLEM, AND IT IS BIGGER THAN THIS ARENA. ANSWERED BY P54** — see its
      list above; the pattern was three levers aimed at the 73% while a different failure mode grew
      from 0.4% to 27%. Original text:
- [x] **1. (answered) THE APEX IS THE PROBLEM, AND IT IS BIGGER THAN THIS ARENA.**
      The h8 rung has now declined to respond to **hostile accuracy** (P24: −0.6 / +0.3 / −0.2, the
      round's tightest MDE), to **finale bodies and stats** (L7/P23: the ceiling defect was real,
      fixed, and bought −1.9), and now to **room geometry** (this round, both objectives). Three
      structurally unrelated levers, three non-responses. **That pattern is a finding about the
      model, not about any of the three levers**, and it deserves a wave aimed at it directly rather
      than another content round that discovers it again. First question to answer: at h8, what
      actually ends a campaign? `lossCauses` and `byMission` across the existing archive would say,
      and no new compute is needed to look.
- [ ] **2. DO NOT BUILD THE TWO-TERMINAL HACK on P52's reasoning.** It was roadmap item 1 an hour
      ago. On P53's evidence it would buy the SABOTAGE shape at the low rungs and little at the
      apex — and the apex (87.2%) is where that board is broken. **A lever aimed at it has to ADD
      required work, not relocate it.** If it is built, it should be built as an ADDITION (hack two
      terminals, both required, one in the room) and priced against P52's ON arm, with the
      expectation set by item 1 above rather than by P52's withdrawn rule.
- [ ] **3. `SIGHTLINE_ROOMSITE` is a measurement arm and must never ship on.** Same status as
      `SIGHTLINE_STALEGROUND=1`. It exists so a future round can re-isolate "where the required site
      is" from "how many there are"; `ARENAEDGETEST` leg (H) is its gate.


## OPEN — left by PARALLAX P52 "THE SINGLETON COMES OUT" (2026-09-16)

P52 tested P51's published claim directly and **the claim did not survive**: moving the singleton
objectives out of CITADEL's held room left the easing unchanged (+28.1/+55.6/+69.1 against P49's
+25.6/+54.1/+72.5). The rule that replaced it: **a held room changes the mission only when the WIN
CONDITION forces the squad into it.** `Game.cs:4470` requires EVERY sabotage site, and one is inside
the room; HACK requires one terminal, and outside the room the garrison is a fight you may decline.
Full round: `docs/measurements/p52/`, DEVLOG §P52.

- [ ] ~~**1. THE NEXT LEVER IS THE WIN CONDITION, NOT THE GEOMETRY.**~~ **SUPERSEDED BY P53** —
      the rule this rested on was withdrawn as an explanation one wave later. Kept for provenance;
      act on P53's list above instead. Original text:
- [ ] **1. (superseded) THE NEXT LEVER IS THE WIN CONDITION, NOT THE GEOMETRY.** Every board
      tried so far moves where the site SITS. The restated rule says to move what the mission
      REQUIRES. Two candidates, both measurable on P52's own instrument against its ON arm:
      a **HACK that needs two terminals** (one inside the held room, one outside — SABOTAGE's shape
      transplanted onto a one-site objective, which isolates "all sites required" from "three
      sites"); or an **extraction leg** — hack, then leave from a zone the garrison covers — which
      puts the room on the critical path without touching site count at all.
      **Do not spend another round shuffling one glyph.** P49, P52 and this item are the same
      geometry lever three times; only the first two were worth running.
- [ ] **2. THE GATE COVERS THE WRONG HALF, AND THAT IS NOW KNOWN.** `Maps.SitesDoorLocked` refuses a
      template whose objective category is SEALED behind one door. P52 shows the property that
      actually matters is whether the mission is FORCED THROUGH the contested space, which is a
      joint property of the template AND the objective's win condition — so it cannot live in
      `Maps` alone. The gate is correct and is kept; the second check would need `Run.IsKillObjective`'s
      neighbourhood (what does this objective require?) crossed with the template's rooms.
      Cheap version: assert that for each objective a template declares, at least one REQUIRED site
      is inside a room that has a garrison.
- [ ] **3. THE SHIPPING RESCUE BOARD ON THIS ARENA IS ITS OWN PROBLEM, and it predates all five
      waves.** 92.2% at h0 and h4, then 12.8% at h8, with `meaningfulChoicesPerTurn` 10.74 at h4 and
      **1.03** at h8. No content lever touched this — it is the OFF arm. It is one forced arena so
      it is not a band claim, but a two-state objective is worth a look on the free instrument.
      Related and bigger: **C3 split the eight objectives into two classes, and nothing has ever
      established that the six TASK objectives behave alike within their class** — two of them
      measurably do not.
- [ ] **4. RICHNESS IS NOT DIFFICULTY, and this project now has the counter-example to prove it.**
      P52 is the richest board ever measured here (8.15 meaningful choices/turn, 84% of turns with a
      shot, every metric resolved) and the easiest at the apex (87.2%). Any future wave that argues
      for a default flip from `meaningfulChoicesPerTurn` alone should be pointed at this row first.


## CLOSED by PARALLAX P52 "THE SINGLETON COMES OUT" (2026-09-16)

P51 re-ran P49's lever on an objective with THREE sites instead of one and found the easing shrinks
with heat (+18.8 / +12.8 / +7.2) instead of exploding (+25.6 / +54.1 / +72.5), with decision richness
UP 2.2/turn and attrition kept. **P49's collapse was the singleton objective, not the room.** All
three items below are P52's; see `docs/measurements/p52/` and DEVLOG §P52.

- [x] **1. MOVE `T` OUT OF THE ROOM, THEN RE-RUN P49's HACK ROUND. DONE.** CITADEL's terminal now
      sits at (4,5), two tiles west of the room's one door, so the garrison covers its approach
      through the doorway instead of standing on top of it. The room keeps the GARRISON and one of
      the three charges. Re-measured on P49's own instrument.
- [x] **2. RESCUE. DONE — moved AND measured, and the premise was wrong.** `C` moved to (14,7), and
      the round was run on `SIGHTLINE_OBJ=rescue` rather than assuming the two objectives behave
      alike. **They do not, in either arm.** Shipping RESCUE on CITADEL reads **92.2 / 93.4 / 12.8**
      against HACK's 66.2 / 40.0 / 18.1, at 9-11 meaningful choices a turn against 4.06 — a
      two-state mission, not a ladder. Easing +7.2 / +5.9 / +15.3 (pooled +9.5), small at h0/h4 only
      because that baseline is already at the ceiling. `docs/measurements/p52/rescue/`.
- [x] **3. The general rule is a GATE now.** `Maps.SitesDoorLocked` seals each `Door` edge in turn
      and refuses a template where one door cuts off EVERY site of some objective category. It runs
      over all 35 shipped arenas in `SIGHTLINE_ARENAEDGETEST` leg (G), and — because a gate whose
      red nobody has seen is a comment — leg (G) also asserts it REFUSES P49's own placement, kept
      verbatim as `Maps.CitadelP49`, and refuses a board whose only charge is the interior one.
      **It is an AUTHORING gate on purpose**: a runtime rejection falls back to the procedural board
      silently, so a door-locked arena would ship as an arena nobody plays and nothing would say so.


## OPEN — left by PARALLAX P50 "THE GARRISON" (2026-09-16)

P50 made P26's `A` enemy-pod anchor glyph actually seat a pod (it had been parsed and discarded for
three programs, with a restore flag and no consumer) and priced it. The fight in the room is now
good — choices 1.18 → 6.77, shots 37% → 87%, and the apex rung responds to heat again (h8 90.6 →
59.7, resolved). `SIGHTLINE_SITEGLYPHS` is still default OFF.

- [ ] **1. THE GEOMETRY, NOT THE FIGHT. This is the top item.** At h0/h4 the composite board reads
      94.7% / 91.2% against the shipping board's 66.2% / 40.0%. **The garrison fixes the fight and
      does not fix the difficulty**: a HACK whose terminal sits in one room is a mission with ONE
      PLACE TO BE, and one place to be is easy however hard the fight there is. Three candidate
      levers, each measurable on P50's instrument against its own ON arm:
      a **second site** so the squad must split (SABOTAGE already wants three); a **second door** so
      holding the room is a choice rather than a gift; or the garrison counted **on top of** the
      mission's headcount rather than out of it (P50's h0/h4 flatness is entirely that it is the
      same bodies, relocated).
- [ ] **2. Only then reconsider the default.** Four waves have now priced four versions of this
      object (P42 a rectangle, P48 a room, P49 a room with the prize in it, P50 that room held). The
      fifth is the one that might earn a default flip.
- [ ] **3. `GarrisonRing` is a first cut.** An anchored pod's members fill outward on a fixed
      16-offset ring, which is draw-free and reproducible but knows nothing about walls — it is run
      BEFORE `TryApplyLayout` stamps them, so a member can land outside the room it is meant to
      hold. It happens not to on CITADEL; it will on the next arena with a tighter interior.


## OPEN — left by PARALLAX P49 "SOMETHING WORTH GOING IN FOR" (2026-09-16)

P49 put the HACK terminal and the RESCUE captive inside P48's room, priced it on a doubly-forced
instrument, and **shipped it OFF**: an uncontested objective behind one door takes HACK to 90%+ at
every rung and costs 2.6 meaningful choices a turn. `SIGHTLINE_SITEGLYPHS=1` turns it on.

- [ ] **1. GARRISON THE ROOM. This is the top item, and P49 is the argument for it.** P26 shipped an
      `A` ENEMY-POD ANCHOR glyph and no template has ever used it. A room you must fight your way
      into is a different object from a room with the prize already in it — and P49 measured exactly
      how bad the second one is (soldier deaths −75%, missions 4.1 → 3.4 turns, the apex rung
      buying nothing). Seat a pod inside, re-run P49's own round with the sites ON in both arms so
      the garrison is the only lever, and read it against `docs/measurements/p49/`.
- [ ] **2. Only then reconsider the default.** The sites stay off until a room is worth entering.
      Three waves have now priced three versions of this object (P42 a rectangle, P48 a room, P49 a
      room with the prize in it); the fourth is the one that might earn a default.
- [ ] **3. A second door is the cheaper alternative and is untested.** A single choke is what lets
      the squad hold the room for free; two entrances make holding it a CHOICE. It costs one glyph
      change and one round, and it does not need the AI to do anything new.


## OPEN — left by PARALLAX P48 "THE FIRST ROOM" (2026-09-16)

P48 redrew CITADEL on the edge layer — walls on the boundaries, an interior, a firing platform, one
door — and priced it on a FORCED-ARENA instrument. `SIGHTLINE_EDGEARENAS=0` restores the block.

- [ ] **1. PUT SOMETHING IN THE ROOM. This is the top item and it is the other half of P42's
      question.** The room has a firing platform and no objective, so there is still no *reason* to
      go in. P26's site glyphs (`T`/`X`/`E`/`C`) have never been used by any template; a HACK
      terminal or a captive inside a room with one door is the whole argument. Deliberately a
      SEPARATE lever from P48's — L7's lesson is that two measured together do not resolve.
      Price it on the forced instrument (`SIGHTLINE_MAP=4`), not the shipped distribution.
- [ ] **2. A SINGLE ARENA CANNOT BE PRICED AT THE CAMPAIGN LEVEL — do not re-derive this.** P48's
      free round produced NINE discordant campaigns in 960 and 52 missions on the board under test.
      One arena of thirty-five is ~1.7% of missions; resolving ±5 points needs order 47,000 missions.
      Use `SIGHTLINE_MAP=<i>` to force the arena, and never read a forced round's win rate against
      the heat band — a campaign played entirely on one arena is a different game (P48's OFF arm:
      43.4 / 10.3 / 1.2 against the ladder's 51.9 / 25.6 / 5.9 on the same tree).
- [ ] **3. Thirty-four arenas are still single-resolution.** The forced instrument means each can now
      be priced on its own, and P48 measured that a room trades POSITION choices for TARGET choices.
      Which arenas want that trade is a design question per arena — and at ~20 minutes a round,
      converting all of them is a program, not a wave.


## OPEN — left by PARALLAX P47 "THE DOUBLE-RESOLUTION TEMPLATE" (2026-09-16)

P47 gave the template format a notation for EDGES — walls on the boundaries between tiles, with
doors — so an authored building with an inside is finally expressible. It ships INERT: every one of
the 35 arenas is still single-resolution and `ARENAEDGETEST` leg (A) asserts it.

- [ ] **1. AUTHOR THE FIRST DOUBLE-RESOLUTION ARENAS. This is the top item.** P42 measured that
      procedural rectangles of wall buy nothing and COST decision richness at every rung, and named
      the authored building as the unpriced object. The format exists now; the content does not.
      What earns its keep is a building with a REASON TO GO IN — the objective inside it, a door
      worth breaching, a roof worth holding — not more cover.
      **That commit severs the CRN stream** (leg (A) will go red, by design) and wants a measured
      round against `SIGHTLINE_EDGES=0`, the same shape as P42's.
- [ ] **2. Site glyphs are still unused too** (P26's `T`/`X`/`E`/`C`/`P`/`A`). The two belong in the
      same authored template: a HACK terminal inside a room with one door is the whole argument for
      both features at once, and neither is worth measuring alone.
- [ ] **3. The format cannot say "roof".** A building with a holdable roof needs the edge layer AND
      an elevation the template can place inside it — `^`/`=` already exist as tile glyphs, so this
      may already work; nobody has drawn one to find out.


## OPEN — left by PARALLAX P46 "THE PIECE CARRIES ITS STATE" (2026-09-16)

P46 closed P45's item 1: the projected view now draws HP, ammo, stance and the status chip row at
each unit, using `Renderer.DrawUnitBadges` — the flat view's own block, extracted verbatim, so the
two views cannot drift. `SIGHTLINE_UNITSTATE=0` restores the pre-P46 view.

- [ ] **1. The badge offsets are FIXED screen pixels.** -30 for the HP pips, -34..-53 for the tags,
      +24 for the chip row — all measured against the flat view's 24px body disc. The projected
      chip's on-screen size changes with zoom, so at a hard zoom-out the badges float well clear of
      the piece they belong to. Anchor them to the chip's PROJECTED radius instead of a constant.
- [ ] **2. Nothing culls badges by density.** Ten units in one corner of a zoomed-out board is ten
      overlapping pill rows. The flat view has the same problem and has never hit it because the
      board is a fixed 18x11; `SIGHTLINE_BIGMAP` would hit it immediately.


## OPEN — left by PARALLAX P45 "THE PIECE WALKS" (2026-09-16)

P45 drove the projected view's pieces from `Unit.Pos` (the tween) instead of the tile index, so
W1's stride is finally visible in the view that is now the game. `SIGHTLINE_CHIPTWEEN=0` restores
the tile-centre placement.

- [ ] **1. UNIT STATE IS INVISIBLE IN THE PROJECTED VIEW, AND THIS IS THE BIG ONE.** HP pips, enemy
      ammo pips and the status chip row (`Renderer.DrawHpPips` / `DrawEnemyAmmoPips` /
      `DrawUnitStatusChips`) are drawn at the unit in the flat view and **nowhere at all** in 3D.
      Players read HP off the roster strip; **hostiles have no HP, no ammo and no status anywhere on
      screen.** All three already take (or can take) a screen anchor, and `DrawMarkers` already runs
      in screen space with the projected anchor in hand — so this is the decal layer's argument
      again (reuse, don't re-author), not 3D geometry. It is also the `Slot.Chrome` question from
      P43: this is UNIT-space information currently living in a screen corner.
- [ ] **2. A piece POPS at an elevation change.** `ChipWorld` samples the height of the tile the
      piece is over, which matches the flat view's `ElevLift` exactly — but 3D can express a real
      ramp, so the pop is now a choice rather than a constraint.
- [ ] **3. The drone HOVER and the idle BOB never made it into 3D.** Both are 2D fakes computed at
      draw time in `Renderer`; in the projected view they are one more world-Y term and a drone that
      does not hover loses its clearest identity cue.


## OPEN — left by PARALLAX P44 "THE DECAL LAYER" (2026-09-16)

P44 split the board's 2D feedback layer on one question — REGION of the board, or OBJECT above it —
and gave the region half real ground geometry (`View3D.BakeDecals` / `DrawDecalLayer`), so it is
depth-tested, sits at each tile's own elevation and is gated by `Vision`. `SIGHTLINE_DECALLAYER=0`
restores the pre-P44 single bridged pass.

- [ ] **1. The sheet is a fixed 64 px per tile and the camera zooms.** Authored at board resolution
      and stretched across whatever the zoom makes of a tile, so a hard zoom softens every decal.
      Bilinear filtering hides it rather than fixing it; a zoom-aware sheet size (or a sheet sized
      to the board's on-screen extent) would.
- [ ] **2. Ink drawn OUTSIDE the board rect is clipped by the sheet.** Labels escape through
      `Cfg.TextSink` and land upright, so what is lost is decoration nobody has yet noticed missing
      — but it is a real difference from the flat renderer and it will bite the first decal that
      deliberately overhangs an edge.
- [ ] **3. SMOKE is the one AIR member that wants to be a volume.** It is correctly not on the
      floor, but it is still a flat bridged card with no depth, so it neither occludes nor is
      occluded. A billboard or a cheap volume is the honest answer; nothing else in the air half
      has this problem.
- [ ] **4. The flat renderer does not use the split.** `Renderer.DrawBoard` still calls the 27
      methods individually, so the ground/air argument exists in exactly one renderer. Harmless
      today; a second consumer would make it a real source of truth.


## OPEN — left by PARALLAX P43 "THE SURFACE" (2026-09-16)

P43 made UI PLACEMENT a policy: `Surface.Panel` generalises P34's affine bridge to any plane, and
`SIGHTLINE_UISURFACE=table` puts the action bar on a holo-console — a horizontal world plane in
front of the projection — with pointing done by the same matrix's inverse. It is the UI half of a
future VR mode, built so the flat game pays for it and uses it.

- [ ] **1. THE MODAL SCREENS HAVE NO PLACEMENT STORY.** Barracks, campaign map, shop, field manual,
      pause, war room — all full-screen, all written against `Cfg.ScreenW/ScreenH` (73 references in
      `Hud.cs` alone). A surface is 1280x800 of local pixels, so they would *work* on one unchanged;
      what is missing is the argument for WHICH surface a modal belongs on, and whether "modal"
      even means anything when the board is a physical object you can look away from. **This is the
      hard part and P43 deliberately did not touch it.**
- [ ] **2. `Slot.Chrome` is one bucket and wants to be several.** Roster chips, the unit card and the
      combat log each have a different natural home. The unit card especially: it is UNIT-space
      information (this soldier, their ammo, their wound) sitting in a screen corner because that is
      where screens have corners. Attached to the chip it would need no corner at all.
- [ ] **3. The console is camera-anchored on screen.** A world-locked rim you orbit around — walk to
      the other side of the table and the controls are where you left them — is one function
      (`Surface.Console`) and touches no call site. It is a better object and a worse HUD; the
      trade needs a decision, not a patch.
- [ ] **4. OPENXR IS A DEPENDENCY DECISION, NOT A WAVE.** Raylib-cs 8.0 ships stereo rendering and
      barrel distortion only — no head pose, no controller input. Real VR needs an external binding
      (Silk.NET.OpenXR or similar) added to `Sightline.csproj`. **Do not plan VR work around
      raylib's VR functions.** When a pose and a ray do arrive, they enter through
      `Surface.Unproject` and every placed panel is already pointable.
- [ ] **5. The `Game.cs` click seam is untested.** SURFACETEST asserts `Surface.PointerIn` gives the
      right answer and that `Hud.Mouse()` routes through it; nothing asserts that `Game.Update`'s
      action-bar loop calls it. A second placeable slot would make that gap bite.


- [x] **1. Procedural audio.** DONE. `src/Audio.cs` synthesises 16-bit PCM WAVs
      in memory (`LoadWaveFromMemory(".wav", bytes)` → `LoadSoundFromWave`) for
      select/move/shoot/hit/crit/miss/overwatch/death/hunker/reload/turn/win/lose.
      `Audio.Init/Play/Shutdown`, gated on `IsAudioDeviceReady` (headless = no-op,
      verified crash-safe). Mute toggle = **M**. Tuning lives in the `Add(...)`
      recipes in `Audio.Init`.
- [x] **2. Combat juice pass.** DONE. Hit-stop on impact/kill (`Game.HitStop`
      freezes the sim a few frames), camera **zoom-punch** on kills
      (`Game._camPulse`, board-centred `Camera2D`), and weapon **recoil**
      (`Unit.Recoil`, set in `ShotAnim.Apply`, decays in `Game.Update`,
      applied in `Renderer.DrawUnit`). Shotgun kicks harder; crits freeze longer.
- [x] **3. Run-to-run loop (the meta).** DONE. `src/Run.cs` holds the persistent
      squad across a **6-mission** campaign (`Run.MaxMissions`). Kills accrue on
      `Unit.Kills` (attributed in `ShotAnim.Apply`, covers overwatch too) →
      promotions via `Run.DebriefSurvivors` (rank up at `KillReq` thresholds,
      buff cycles +Aim/+HP/+Mobility) + partial field-heal between missions.
      `Phase.Barracks` shows the debrief (`Hud.DrawBarracks`); `Mission.Build`
      now takes a missionNum and scales the hostile force. Flow:
      Intro→Mission→(clear)→Barracks→NextMission… →Win after mission 6, or Lose
      on squad wipe. Autoplay auto-advances the barracks so the smoke test still
      plays whole runs. NOTE: run state is in-memory only (no save file yet).
- [x] **4. Tactical depth.** DONE.
      - [x] **Grenades.** `GrenadeAnim` (src/Anim.cs): lobbed arc → AoE explosion,
            Chebyshev radius 1, ignores cover, hits BOTH teams (friendly fire),
            destroys low cover in the blast. 1 charge/soldier, refilled each
            mission (`Unit.Grenades`). Action key **4**; targeting mode in `Game`
            (`GrenadeMode`/`GrenValid`) with range ring + blast + arc preview
            (`Renderer.DrawGrenade`). The enemy AI also throws frags (`Ai.BestGrenade`,
            `EnemyPlan.Grenade`): from mission 2 some hostiles (bruisers always,
            else ~22%) carry one and lob it at a 2+ soldier cluster, or to flush a
            single well-covered target it can't shoot well — never catching allies.
      - [x] **Enemy activation pods.** DONE. Enemies spawn dormant (`Unit.Active`
            false, grouped by `Unit.PodId`). `Game.CheckPodActivation` (called each
            player frame + on player tile-entry) wakes a whole pod when any soldier
            gets LoS within `SightRange` (12); `ActivatePod` gives a free scatter
            (Ai.Plan move) + "CONTACT!" banner. Shooting/grenading a dormant enemy
            also wakes its pod. Dormant enemies are skipped in `UpdateEnemy` and
            drawn dimmed with a "?" (`Renderer.DrawUnit`).
      - [x] **Elevation / high-ground.** DONE. `Grid.Height[,]` layer (0 ground,
            1 high). Firing from a higher tile onto a lower one grants
            `Combat.HighGroundAim` (+15 hit) + `HighGroundCrit` (+10), surfaced in
            `Combat.ComputeOdds`/`ShotOdds.HighGround` and the shot tooltip
            ("+ HIGH GROUND"). High ground also **sees over LOW cover**
            (`ShotOdds.SeesOver`: negates a low-cover target's defense/flank, keeps
            high cover; tooltip "+ OVER LOW COVER"; verified by `SIGHTLINE_COMBATTEST`).
            `Mission.RaisePlateau` carves 2-3 walkable plateaus
            mid-field (skipping spawns/evac); `Renderer.DrawElevation` draws them
            faux-3D (raised top + front wall + lit edge) and lifts cover/units that
            stand on them (`ElevLift`); move/path/hover overlays are height-aware.
            The enemy AI values seizing high ground (`Ai.Plan`). Movement cost is
            unchanged (plateaus are just walkable floor).
- [x] **5. Map variety & objectives.** DONE.
      - [x] **Objectives.** `Objective` enum (Eliminate / Evac). Every 3rd mission
            (3 & 6) is **Evac**: a 2x2 extraction zone (`Game.EvacZone`, drawn by
            `Renderer.DrawEvac`); win when all living soldiers stand in it. Others
            are Eliminate. `Game.CheckEnd` branches on objective; HUD shows the
            objective; `Mission.Build` keeps the evac zone clear; autopilot extracts.
      - [x] **Hack-a-terminal objective.** DONE. `Objective.Hack` places a central
            `Game.Terminal`; a soldier Chebyshev-adjacent hacks it (HACK action,
            key **H**, costs 1 action, `Game.HackRequired`=3 charges; `Game.CanHack`/
            `DoHack`). Win on `HackProgress >= HackRequired`. `Renderer.DrawTerminal`
            draws the console + a segmented progress ring; HUD top bar shows
            "HACK x/3" + a contextual HACK button. Objective rotation is now
            Elim / Hack / Evac (n%3: 2=Hack, 0=Evac, else Elim).
      - [x] **Hand-authored map layouts.** DONE. `src/Maps.cs` holds ASCII arena
            templates (legend: `.` floor / `o` low / `#` high / `^` plateau);
            `Mission.Build` rolls ~55% to stamp a random template over the grid
            (else procedural). Reserved tiles (spawns/evac/terminal+ring) stay open
            floor; `Mission.TryApplyLayout` flood-fills from a soldier to verify all
            spawns/evac/terminal stay reachable and reverts to procedural otherwise.
            **Six arenas:** PLAZA (central plateau), GAUNTLET (lane spine), PILLARS
            (column field), CHEVRON (diagonal cover wall + redoubt), CITADEL (bunker
            with interior plateau + doorway), ZIGGURAT (stepped mound with a commanding
            **tier-2** `=` core). Test hook `SIGHTLINE_MAP=<index>` forces a specific
            layout (`Mission.ForcedLayout`).
      - [x] **VIP escort objective.** DONE. `Objective.Escort` (rotation is now
            Elim / Hack / Evac / Escort, `Game.ObjectiveFor` = `(n-1)%4`). A fragile
            gold **VIP** (`Mission.MakeVip`, `Unit.IsVip`: 6 HP, 45 aim, sidearm, no
            frags) must reach the shared extraction zone alive. Implemented as a
            Player-team unit added to a per-mission copy of the roster
            (`Players = new List<Unit>(_run.Squad)` + `Players.Add(Vip)`), so
            occupancy/targeting/overwatch/render all work generically; it's excluded
            from the persistent squad at debrief (`EnterBarracks` filters `!IsVip`).
            Win when the VIP stands in the evac zone; **losing the VIP is a loss**
            (`Game.CheckEnd` Escort branch; "VIP DOWN" banner in `KillUnit`). The
            enemy AI prioritises it (`Ai.Plan`: +40 shoot value + advance bias on
            the VIP). 5th `PlayerSpawns` slot seats the VIP; renderer draws a gold
            ring + diamond + "VIP" tag (`Renderer.DrawUnit`); HUD shows "ESCORT VIP"
            and an "ASSET" roster/card label; squad counter excludes the VIP.
            Autopilot walks the VIP to evac while soldiers screen.
- [x] **6. Polish/UX.** DONE.
      - [x] Squad **roster strip** (left edge): all soldiers' HP/AP/rank/status,
            click to select, dims when spent (`Hud.DrawRoster` + `RosterChips`).
      - [x] **End-turn confirmation** when a soldier still has actions
            (`Game.RequestEndTurn`/`EndTurnArmed`; button shows "CONFIRM?").
      - [x] **Mute indicator** in the top bar when audio is off.
      - [x] **Threat preview:** while positioning, reachable tiles a live, active
            enemy could fire on with no cover get a red warning pip
            (`Game.ComputeThreat` -> `Game.Threat`, drawn by `Renderer.DrawThreat`),
            so "move into cover" decisions are legible at a glance.
      - [x] **Camera zoom/pan.** `Game.CamZoom`/`CamPan` feed `Game.ViewCamera(bool
            withShake)` (render variant adds shake/zoom-punch, picking variant is
            stable). Mouse wheel zooms toward the cursor, middle-drag pans, **C**
            resets. Defaults to identity so default mouse picking + the headless
            harness are byte-for-byte unchanged. Mouse->tile now routes through
            `GetScreenToWorld2D` in `UpdateHoverAndAim`.
      - [x] **Keyboard tile cursor.** Arrows/WASD move `Game.CurX/CurY` (`KbCursor`);
            it overrides the mouse hover so path/odds/grenade previews all work, and
            **Space** runs `Game.BoardAct` (the shared move/fire/select logic). Any
            mouse movement hands control back to the mouse.
      - [x] **Pause/settings menu.** **Esc** opens `Game.Paused` (cancels aim/grenade
            first); `Hud.DrawPause` offers Resume, Audio, Screen-shake (`Fx.ShakeOn`),
            Threat-preview (`Game.ShowThreatPref`) toggles, and Abandon Run.
            Screenshot hooks: `SIGHTLINE_ZOOM`, `SIGHTLINE_PAUSE`.
- [x] **7. Class signature abilities.** DONE. Per-class self-cast ability (key **5**,
      1 charge/mission). `AbilityKind` (RunGun/Blitz/Steady/Suppress) derived from
      `Unit.Cls` (`Unit.AbilityKindFor`); transient stances (`RunGun`/`Blitz`/
      `Steady`) cleared each `BeginTurn`, the `Suppress` aim-debuff cleared at the
      victim-owner's next `StartPlayerTurn` so it bites during the enemy turn.
      `Game.DoAbility`/`CanAbility` drive it; Run&Gun edits `IssueShoot` (shot costs
      1 action, doesn't end the turn), Blitz edits `IssueMove` (one action cheaper),
      Steady + Suppress feed `Combat.ComputeOdds` (`SteadyAim/Crit`, `SuppressAim`).
      HUD adds an ability button (reflowed to fit 7 buttons) + tooltip "+ STEADY";
      renderer shows stance tags. The test autopilot fires abilities on Eliminate
      missions to keep the paths covered. VIP has no ability.

When you finish an item: verify (build + autoplay + a screenshot), commit, merge
to `main`, tick the box, and update "Current state".

---

## ROADMAP — PHASE 2 (next horizon)

Items 1-7 are done: the tactical layer, the objectives, and the UX are feature-
complete and polished. The game now plays well moment-to-moment and minute-to-
minute. **The frontier is the run-to-run loop and content breadth** — right now
every run feels mechanically identical because squad growth is fixed and the
enemy roster is tiny. Phase 2 is about making runs feel *different* and giving
the player meaningful long-game decisions. Ordered by impact:

- [x] **A. Perk-based promotions (build variety).** DONE. Each rank-up queues a
      **pick-1-of-2 perk** choice (`Run.PendingPerks` / `PerkOffer`), resolved in
      the barracks (`Hud.DrawPerkChooser`, `Game.ChoosePerk`; autopilot auto-picks).
      `Perk` enum + `Unit.Perks` + `PerkDef` (Name/Code/Desc); 10 perks: LockOn
      (+15 aim vs exposed), Hardened (-1 dmg taken, in `Combat.Resolve` + grenade),
      Reflexes (overwatch +aim in `OnUnitEnteredTile`), Bandolier (+1 grenade),
      CloseQuarters/Marksman (+15 aim by range), Deadeye (+15 crit), Tank (+3 HP),
      Sprinter (+1 mob), Adrenal (+1 ability charge). Stat perks apply on grant;
      passives read in `Combat.ComputeOdds`/`Mission.Build`. Maxed soldiers fall
      back to a stat bump. Barracks roster shows earned perk codes.
- [x] **B. Enemy variety + an elite/boss.** DONE. New `Mission.SpawnEnemies`
      archetypes (gated by mission #): **SNIPER** (VIPER, sniper rifle, kites to
      range + height — `Ai.Plan` distance bonus), **TURRET** (SENTRY, Mobility 0 so
      it can't move — sits and overwatches for free), **BERSERKER** (REAVER, tanky
      shotgun rusher — `Ai.Plan` 3.4x advance weight; ELITE charges too). Capstone
      **ELITE** boss on the final mission (WARLORD: 20+2n HP, 2 grenades, high aim,
      a one-time low-HP **RAGE** in `Game.UpdateEnemy` that buffs aim/mobility +
      "WARLORD ENRAGED" banner). Renderer gives each a distinct glyph; the elite is
      a larger orange figure with a ring + name/rage tag (`Pal.Elite`). **MEDIC**
      (ORDERLY, from mission 3): a support hostile that mends wounded allies instead
      of fighting — `Ai.Plan` MEDIC branch picks the most-wounded active ally, moves
      to a covered tile within `Ai.HealRange` (4) + LoS and heals `Ai.HealAmount` (4)
      via `HealAnim` (`Game.UpdateEnemy` heal branch). Distinct green-cross glyph
      (`Renderer.DrawUnit`); falls back to normal combat AI when no one's hurt.
      Screenshot hook `SIGHTLINE_WAKE` reveals dormant pods.
- [x] **C. Strategic between-mission layer.** DONE (choice + reward + intel
      currency/shop). The barracks now ends with **3 deployment cards** (`Run.Offers` /
      `MissionCard`, `Hud.DrawDeployCard`, `Game.ChooseCard`): RECON (other objective,
      lighter force, +full heal), STANDARD (rotation objective, normal), ONSLAUGHT
      (other objective, heavier force, +bonus perk). The pick sets the next mission's
      **objective + difficulty** (`MissionCard.EnemyDelta/StatDelta` thread into
      `Mission.Build`/`SpawnEnemies`); the cleared card's reward is applied in
      `EnterBarracks` (heal squad / `Run.AddBonusPerk`). `Run.ObjectiveFor` is the
      STANDARD baseline. Autopilot picks card 0. Hook `SIGHTLINE_CARDS`.
      **Intel currency + requisition shop (DONE):** `Run.Intel` accrues each mission
      cleared in `EnterBarracks` (`8 + 3*survivors + missionNum`, +6 on ONSLAUGHT)
      and persists in the save. The barracks opens with a **REQUISITION** screen
      (`Hud.DrawRequisition`, gated by `Game.ShopDone`) BEFORE the perk/card steps:
      spend intel on FIELD MEDKIT (heal most-wounded to full, 6), COMBAT STIMS (+2
      max HP to the frailest, permanent, 10), ADV. TRAINING (a bonus perk choice,
      16), or FRAG CACHE (+1 permanent grenade/mission, `Unit.BonusGrenades`, caps at
      +2, persisted, 12). `Game.CanBuy/DoPurchase/HandleShopClick` + `Game.ShopName/
      Desc/Cost` (the shop card auto-sizes to the item count); autopilot buys a medkit
      then proceeds (`AutoShop`). Hook `SIGHTLINE_SHOP`.
- [x] **D. Procedural music + ambience.** DONE (blind ship) — see Phase 3 item 3.10:
      a synthesised looping ambient bed + a combat layer that crossfades by intensity
      (`Audio.BuildAmbient`/`BuildCombat`/`UpdateMusic`, `Game.MusicIntensity`). Built but
      not heard in this sandbox (no audio device); the human should verify + tune.
- [x] **E. Run persistence (save/load).** DONE. `src/SaveGame.cs` serialises the
      `Run` (squad incl. perks/weapon/rank/HP + mission # + the active deployment
      card) to the OS user-data dir (`ApplicationData/Sightline/save.json`, NOT the
      repo) via `System.Text.Json` (compact DTOs; transient per-mission state is
      rebuilt by `Mission.Build`). The run is **checkpointed at each mission start**
      (`Game.SetupMission`) and the save is **deleted when a run ends** (win in
      `EnterBarracks`, wipe via `Game.LoseRun`). The intro shows a **CONTINUE RUN**
      button (key **C**) when `SaveGame.Exists` (`Game.ContinueRun` reloads + resumes
      the current mission from its start; `Hud.OverlayBtn2`). All file I/O is gated
      behind `Game.NoPersist` (set by the harness) so the smoke test never touches
      disk. Verified: `SIGHTLINE_SAVETEST=1` round-trips squad/perks/weapon/card;
      `SIGHTLINE_INTRO=1` screenshots the CONTINUE button. NOTE: CONTINUE resumes the
      *last-started* mission from its start (mid-mission progress is not saved).
- [x] **F. Biome/visual variety.** DONE (palette swaps). `Biome` (Util.cs) defines
      a per-mission floor checker + grid/edge tint; `Biome.For(n)` cycles STEEL /
      ARID / TUNDRA / VERDANT / ASH / VOID so each mission reads as a distinct place.
      `Game.Biome` is set in `SetupMission` and shown in the mission banner; the
      renderer tints floor, grid lines, board edge, and now **cover + plateaus**
      (blended toward `Biome.Tint` via `Pal.Mix` in `Renderer.DrawCover`/
      `DrawElevation`). Still open: *themed authored arenas* per biome (`Maps.cs`).

Supporting polish (any time): a distinct "VIP EXTRACTED" win flourish (the LOST/
wipe lose cards are now distinct via `Game.LoseTitle/LoseReason`); a 2nd elevation
tier; secondary objectives; more authored arenas (and *themed-per-biome* arena
selection); more requisition options (recruits/gear) for the shop.

---

## ROADMAP — PHASE 3 (specced; the next big push)

Phase 1 + Phase 2 (A–F) are complete; the game is tactically rich and readable.
**The frontier is still the run-to-run loop** — runs are mechanically same-y, and
the human flagged that losing soldiers feels cheap. Phase 3 makes a run feel like a
*campaign with stakes*, then broadens tactical + content variety, then presentation.
Ordered by impact. Each item lists the concrete hooks to touch and how to verify it
(headless unless noted). Keep the hard constraints: NO CI/tests-runner, drawn text
ASCII-only **until a font ships** (Phase 5.3; see the clarified Art policy), verify via
`SIGHTLINE_*` harness + autoplay + screenshots, ship compiling code to `main`.

### Tier 1 — make the run loop bite (highest impact)

- [x] **3.1 Wounds & attrition (core).** DONE. Survivors that end a mission badly hurt
      carry a **Wound** (`Unit.Wound` = missions remaining; 2 if downed to ≤¼ MaxHp, 1 if
      ≤½). Assigned/decayed in `Run.DebriefSurvivors` (recover one step per mission, then
      gauge fresh damage *before* the field-heal). While `Wound > 0`: **−12 Aim**
      (`Combat.ComputeOdds`) and **−1 Mobility** (`Unit.MoveBudget`), via `Unit.WoundAim/
      WoundMob`. `FIELD MEDKIT` heals to full **and cures the wound** (`Game.DoPurchase`/
      `ShopTarget`/`ShopEffect`/`CanBuy` now consider wounds). UI: red "WOUNDED (n)" in the
      roster strip + a "WOUNDED (n missions) −aim/−mob" dossier line. Persisted in
      `SaveGame`. Verified by `SIGHTLINE_WOUNDTEST` (assign → penalise aim+mob → decay →
      clear) + `SIGHTLINE_WOUND` screenshot + autoplay. **Still TODO:** the *bench /
      deploy-short-handed* option (the squad still auto-backfills to 4, so a wipe doesn't
      yet shrink strength) — that's the remaining half of "attrition bites".

- [x] **3.2 Soldier identity (nicknames, traits, bonds).** DONE. `Unit.Nickname`
      (shown as `NAME "NICK"` via `Unit.FullName`) + `Unit.Traits` (List) earned on
      FEATS: **multi-kill turn** → KILLER INSTINCT (+12 aim vs wounded), **clutch kill
      while bloodied** → COLD BLOOD (+15 crit while self ≤½ HP), **avenged a fallen
      squadmate** → VENGEFUL (+12 aim while a squadmate is down), **survived near
      death** → IRON WILL (+2 max HP). Feats are flagged during play in `Game.CreditKill`
      (multi-kill/clutch/vengeful, with a "FEAT:" banner) + `Game.MarkPlayerHurt`
      (near-death), and resolved into traits + a nickname in `Run.DebriefSurvivors`
      (`GrantTrait`/`AssignNickname`). Traits read in `Combat.ComputeOdds`
      (`Unit.HasTrait` + `KillerAim`/`ColdBloodCrit`/`VengefulAim`/`IronWillHp`).
      **Bonds:** `Run.BondTally` (per-pair co-survival) forms a `Unit.Bonds` link after
      `Run.BondThreshold` (3) shared missions (`Run.AdvanceBonds`); bonded squadmates
      get **+10 aim while adjacent** (`Unit.BondAura`, refreshed each frame by
      `Game.UpdateBondAuras`, read in `ComputeOdds`). UI: dossier shows nickname +
      Traits + Bonds (gold); the roster strip shows the nickname + a live "BOND" tag.
      Persisted in `SaveGame` (nickname/traits/bonds + BondTally). Verify:
      `SIGHTLINE_TRAITTEST=1` → `TRAITTEST: PASS` + `SIGHTLINE_TRAITS=1` dossier
      screenshot; `SIGHTLINE_SAVETEST` now round-trips identity too. **Still TODO (3.2
      follow-ups):** trait/"FEAT" FX on the unit is minimal; tooltip doesn't yet flag
      "+ BOND"/trait bonuses; bond progress shows no UI hint before it forms.

- [x] **3.3 Branching campaign map.** DONE. The 3-card barracks pick is replaced by a
      Slay-the-Spire-style node path. `Run.Map` = a DAG of `MissionNode`
      (Col/Row/Kind/Card/Next edges/Visited), generated deterministically from
      `Run.MapSeed` via `Run.GenerateMap(seed)`: `Run.MaxMissions` columns (mission 1 =
      single START, last = single BOSS, middles 2-3 nodes), each wired to 1-2 next-column
      nodes with a connectivity fix-up so every node is reachable and every non-boss node
      leads onward. `NodeKind` (Start/Combat/Elite/Supply/Boss) → a `MissionCard` via
      `Run.CardForNode` (ELITE = +force/bonus perk, SUPPLY = -force/full heal, BOSS =
      forced Eliminate so the WARLORD must fall; objective varies per row for branch
      variety). Flow: `Run.Start` builds the map + seats `MapPos=0` (START); the barracks
      renders the DAG (`Hud.DrawCampaignMap`, replacing the deploy-card block) with the
      current node ringed + reachable next nodes glowing/clickable (rects in
      `Hud.NodeBtns`) + hover tooltips; `Game.ChooseNode` adopts the picked node's card +
      `NextMission`. `Run.JumpTo(n)` walks the map for the `SIGHTLINE_MISSION` harness
      jump; autopilot greedily takes `NextNodes()[0]` (always reaches the boss). Persisted
      as just `MapSeed`+`MapPos` (regenerated on load in `SaveGame.FromDto`). Legacy
      `Offers`/`ChooseCard`/`DrawDeployCard` kept as a fallback if the map is empty.
      Verify: `SIGHTLINE_CAMPAIGN=1` screenshot, `SIGHTLINE_SAVETEST` (round-trips
      seed/pos/node), autoplay clean across mission jumps incl. the BOSS node.

### Tier 2 — tactical depth (second-to-second)

- [x] **3.4 Utility items (smoke / flash / deployable cover).** DONE. A second
      throwable slot beyond grenades, **1 charge/mission, assigned by class**
      (`Unit.Item`/`ItemKindFor`: Ranger+Sharpshooter = SMOKE, Assault = FLASH, Gunner =
      BARRICADE; refilled in `Mission.Build`). Targeting mirrors the grenade pattern
      (`Game.ItemMode`/`ItemValid`/`ItemTargetOk`/`IssueItem`, action key **6**, HUD ITEM
      button + tooltip; `Renderer.DrawItem` range ring + footprint). **Smoke:** a
      `Grid.Smoke[,]` turn-counter layer that `BlocksSight` treats as blocking (so
      `HasLineOfSight` + overwatch are cut through it), laid 3x3 by `SmokeAnim`, decays
      one turn per round in `Game.StartPlayerTurn` (`Grid.TickSmoke`), cleared per mission
      (`Grid.ClearSmoke`); drawn as a drifting haze (`Renderer.DrawSmoke`). **Flash:**
      `FlashAnim` AoE that applies `StatusKind.Disoriented` (3.5: -aim + no overwatch) to
      both teams in the blast and breaks held overwatch. **Barricade:** drops a LowCover
      tile on an empty floor tile (instant, no projectile). `LobAnim` base in `Anim.cs`
      backs Smoke/Flash. Autopilot uses items (~30%) to keep the paths covered. Verify:
      `SIGHTLINE_ITEMTEST=1` → `ITEMTEST: PASS` (smoke blocks+decays LoS, barricade=cover,
      loadouts map) + `SIGHTLINE_ITEM=1` screenshot + autoplay clean. **TODO:** AI doesn't
      use utility items yet; no loadout-choice UI (fixed per class); shot tooltip doesn't
      flag a smoked target.

- [x] **3.5 Status effects.** DONE. `Unit.Statuses` (`List<Status>` of {`StatusKind`,
      `Turns`}) + `Unit.AddStatus`/`HasStatus`; per-mission, cleared in `Game.SetupMission`,
      never persisted. Kinds: **Burning** (DoT at turn start), **Bleed** (DoT per tile
      moved), **Stun** (lose one action), **Disoriented** (−15 aim + can't overwatch).
      Ticked in `Game.TickStatuses` (called right after `BeginTurn` in `StartPlayerTurn`/
      `EndPlayerTurn`/first-turn `SetupMission`); Bleed ticks in `OnUnitEnteredTile`; DoT
      flows through `Game.EnvDamage` (source-less damage + FX + kill/near-death). Reads:
      `Combat.ComputeOdds` (`StatusKind.Disoriented` → −`Unit.DisorientAim`), `DoOverwatch`
      + the AI overwatch branch both refuse while disoriented. Magnitudes are consts on
      `Unit` (`BurnDamage`/`BleedDamage`/`DisorientAim`). FX: floating "-n BURN/BLEED"
      text + `Renderer` draws stacked status codes (BRN/BLD/STN/DAZ, `StatusDef.Code`)
      under each figure. **Live source:** grenade survivors catch fire (`GrenadeAnim` →
      `AddStatus(Burning, 2)`), so autoplay exercises it. Verify: `SIGHTLINE_STATUSTEST=1`
      → `STATUSTEST: PASS` (burn/bleed/stun/disorient tick+read) + `SIGHTLINE_STATUS=1`
      screenshot. **TODO:** the *system* is complete + tested, but Bleed/Stun/Disoriented
      have no in-game source yet — those land with 3.4 (flash/incendiary utility items)
      and 3.7 (status-inflicting enemies).

- [x] **3.6 Destructible high cover + 2nd elevation tier.** DONE (both parts).
      - [x] **(a) Destructible cover. DONE.** `Grid.CoverHp[,]` (`HighCoverHp=2`/
            `LowCoverHp=1`) charged for every cover tile at the end of `Mission.Build`
            (`ResetCoverHp`; `SetCoverHp` for a deployed barricade). `Grid.DamageCover`
            degrades **High→Low→Floor** as HP runs out (`CoverHit` enum); `Grid.CoverTile`
            finds the frontal block. Sources: **grenades** chew a full level in the blast
            (`GrenadeAnim` now calls `DamageCover(HighCoverHp)` — high→low, low→gone,
            replacing the old instant low-clear); **heavy fire** chips on hit
            (`Game.TryChipCover` in `ShotAnim.Apply`: LMG any range / shotgun point-blank,
            only when the target actually has cover). `Game.CoverHitFx` does the FX/sound
            ("COVER CRACKED"/"COVER DOWN"); `Renderer.DrawCover` draws fissures on a
            chipped-but-not-degraded block. Verify: `SIGHTLINE_COVERTEST=1` →
            `COVERTEST: PASS` + `SIGHTLINE_COVER=1` screenshot + autoplay clean.
      - [x] **(b) 2nd elevation tier. DONE.** `Grid.Height` now supports level 2.
            High ground is fully **relative** (`heightAdv = HeightAt(a)-HeightAt(d)` in
            `Combat.ComputeOdds`, so tier-2 beats tier-1 for free); a **commanding 2-tier
            advantage** sees over the target's HIGH cover too (`seesOver` when
            `heightAdv>=2`) AND can target through intermediate high cover via a new
            `Grid.HasLineOfSight(..., overHighCover)` overload (smoke still blocks),
            wired in `Game.CanTarget`. `Renderer` is height-aware: `ElevRect`/`ElevCenter`
            lift by `HeightAt*ElevLift`, `DrawElevation` draws taller walls + a brighter
            top for tier 2, `DrawCover` sits cover at the right tier. `Mission.RaisePlateau`
            took a `level` param; procedural missions 4+ raise a tier-2 redoubt; `Maps.cs`
            legend gains `=` (tier-2 plateau). No climb cost (plateaus are walkable floor),
            so reachability holds without ramps. Verify: `SIGHTLINE_COMBATTEST` tier-2 case
            (sees over high cover) + `SIGHTLINE_COVERTEST` LoS overload + `SIGHTLINE_ELEV=1`
            screenshot + autoplay on missions 4-6.

### Tier 3 — content breadth (variety)

- [x] **3.7 New enemy archetypes + recurring mid-boss.** DONE (drone / shield / mid-boss
      / sapper).
      - [x] **DRONE (WASP).** Cls `DRONE`, low HP, fast, **ignores the target's cover**
            (`Combat.ComputeOdds` `ignoresCover` folds into `seesOver`); AI beelines
            (advW 3.0 + cancels its own cover value). Renderer hovers it above its
            shadow (diamond glyph). Spawns mission 2+.
      - [x] **SHIELD (AEGIS).** Cls `SHIELD`, `Unit.ShieldDx/Dy`; `Game.FaceShields`
            (each enemy turn) re-faces the barrier toward the **nearest soldier**, so the
            squad must keep moving to flank it. `Combat.ShieldedFrom` gives **full cover
            (lvl 2) from the barred side regardless of terrain** — flank it, or bypass with
            a DRONE / commanding tier-2 height. Renderer draws a frontal barrier arc.
            Spawns mission 3+.
      - [x] **Mid-boss.** A named `ELITE` band on missions 3 (BREAKER) & 5 (WARDEN),
            lighter than the final WARLORD but with the same rage; renderer + rage banner
            now use `u.Name` (not a hardcoded "WARLORD"). Verify: `SIGHTLINE_COMBATTEST`
            (drone-ignores-cover + shield front/flank cases) + `SIGHTLINE_MISSION=3`
            screenshot + autoplay.
      - [x] **SAPPER (BREACH). DONE.** Cls `SAPPER` (mission 3+, no grenades). `Ai.Plan`
            computes `sapTarget = CoverTile(nearest, e)`, scores tiles toward it, and sets
            `EnemyPlan.SapTile` when it ends adjacent (clearing ShootTarget); `Game.UpdateEnemy`
            has a sap branch that `DamageCover(HighCoverHp)`s the tile ("BREACH" + `CoverHitFx`).
            Renderer gives it a demo-charge marker. Pairs with 3.6 destructible cover.

- [x] **3.8 New objectives.** DONE — SABOTAGE, RESCUE, and DEFEND all shipped.
      - [x] **SABOTAGE. DONE.** `Objective.Sabotage` added to the enum + `Run.ObjectiveFor`
            (rotation now %6: Elim/Hack/Evac/Escort/Sabotage/Rescue). 3 charge sites
            (`Game.SabotageSites`, spread mid-map; reserved in `Mission.Build` via a new
            `sabotage` param) each demolished by one **PLANT** action — the HACK action/key
            generalised (`HasHackAction`/`CanHack`/`DoHack` branch on `HasSabotage`,
            `NearestSabotageSite`). Win in `CheckEnd` when `SabotageBlown.Count == sites`.
            HUD "SABOTAGE x/3" + PLANT button + `Renderer.DrawSabotage` (blinking charge
            consoles, ARMED once set). Autopilot plants each site. Verify:
            `SIGHTLINE_OBJ=sabotage` (force hook, shot or autoplay) — autopilot WINs it +
            screenshot.
      - [x] **RESCUE. DONE.** `Objective.Rescue`. A caged captive (reuses the `Vip` unit,
            `Cls="VIP"`, Name "CAPTIVE") seated mid-field, `CaptiveLocked` true: invulnerable
            (guarded in `CanTarget` + `GrenadeAnim`), immobile (Mobility 0). `TryFreeCaptive`
            (called from `CheckPodActivation` + `AutoStep`) unlocks it when a soldier is
            Chebyshev≤1 → it becomes a fragile escort (Mobility 6) to walk to the shared
            evac zone. `CheckEnd` Rescue branch: win = freed captive in evac; lose = captive
            dies after freeing. Captive seat reserved + connectivity-verified via the
            `terminal` Build arg (and `sabotage` sites now verified too). Renderer: caged =
            gray figure + cage bars + "CAPTIVE" tag, gold "FREED" after. HUD "RESCUE/EXTRACT
            CAPTIVE". Autopilot springs then extracts it. Verify: `SIGHTLINE_OBJ=rescue` +
            screenshot.
      - [x] **DEFEND. DONE.** `Objective.Defend`: survive `Game.DefendTurns` (8) player
            turns. `CheckEnd` wins when `_turnCount > DefendTurns`. `Game.SpawnDefendWave`
            (called at the top of `EndPlayerTurn` before the enemy turn) adds `2 + n/2`
            active `Mission.MakeWaveHostile` grunts/scouts at the right edge on odd turns
            (capped at 12 alive). HUD "DEFEND x/8" countdown (`Game.Turn`); autopilot holds
            + overwatches; `RollSecondary` skips SWIFT on Defend (can't finish early).
            Verify: `SIGHTLINE_OBJ=defend` (autopilot survives → advances, or wipes) +
            screenshot.

- [x] **3.9 Secondary objectives.** DONE. An optional per-mission bonus goal worth
      `Game.SecondaryIntel` (12) extra `Run.Intel`. `SecondaryKind` {None, NoLosses,
      Swift, CleanSweep}; `Game.RollSecondary(n)` picks one in `SetupMission` (none on
      mission 1; CLEAN SWEEP skipped on Eliminate where it's automatic). Tracked live:
      NO LOSSES fails in `KillUnit` on a soldier death (`SecondaryFailed`), SWIFT checks
      `_turnCount <= SwiftTurns` (7), CLEAN SWEEP checks all hostiles dead. `EnterBarracks`
      evaluates `SecondaryAchieved()`, awards intel + a debrief line. HUD top bar shows
      `g.SecondaryHud` (green on track / red blown via `SecondaryOnTrack`). No persistence
      (per-mission). Verify: `SIGHTLINE_MISSION=3` HUD screenshot + autoplay clean.

### Tier 4 — feel, audio & accessibility

- [x] **3.10 Procedural music & ambience (roadmap item D). DONE (blind ship).** Two
      synthesised looping beds in `src/Audio.cs`: `BuildAmbient` (an A-minor sine pad with
      slow LFO tremolo) and `BuildCombat` (a tenser pad + a 2 Hz driving sub-bass pulse),
      each an 8-second buffer of **integer-Hz tones over an integer-second loop so it loops
      seamlessly**. Loaded via `LoadMusicStreamFromMemory(".wav", …)`, `Looping=true`, both
      played at volume 0. `Audio.UpdateMusic(dt)` (called each frame in the `Program` loop)
      pumps `UpdateMusicStream` and crossfades ambient↔combat toward `Audio.SetMusicIntensity`
      — fed by `Game.MusicIntensity()` (1 on the enemy turn, 0.5 while live hostiles are
      about, 0.15 when clear, 0 in menus). Mute (**M**) zeroes both via `Enabled`. All gated
      behind `_music`/`IsAudioDeviceReady`, so it's a **no-op headless** (screenshots/autoplay
      unchanged — verified crash-safe). **NOT heard in this sandbox** (no audio device) — the
      human should verify audibly and tune the `Build*`/volume recipes. Tuning lives in
      `BuildAmbient`/`BuildCombat` (freqs/vols) + the `ambT`/`combT` mix in `UpdateMusic`.

- [x] **3.11 Game-feel + death feedback.** DONE. **KIA stamp:** a fallen soldier
      (`Game.KillUnit`, player non-VIP) gets a prominent `KIA  NAME "NICK"` stamp
      (`Fx.Stamp`: slow-fade, barely-rising), a red full-screen **death-flash**
      (`Game.DeathFlash`, decayed in `Update`, drawn under the HUD in `Game.Draw`), and
      is logged to `_missionKia` → inserted at the top of the barracks debrief in
      `EnterBarracks` (`KIA  NAME`). **Final-blow kill-cam:** `Game.IsMissionEndingKill`
      (last hostile on Eliminate / squad wipe / lost VIP) punches up the deciding death
      with `AddZoomPunch` + shake and — since THE BEAT (2026-09-03) — a real 0.45 s SLOW-MO
      window (`Game.KillCamWindow` / `KillCamScale`, the zoom held). The line here used to read
      "extra `HitStop` (0.4s slow-mo)": that was a hard freeze in which nothing moved and the zoom
      spent itself unseen; FEELTEST leg d FAILed on it with 24 still frames and the zoom at 1%.
      Per-mission state cleared in `SetupMission`. Verify: `SIGHTLINE_FEELTEST=1`,
      `SIGHTLINE_KILLCAM=750 SIGHTLINE_SHOT=750 SIGHTLINE_SHOTSEQ=60` filmstrip, autoplay clean.

- [x] **3.12 Onboarding tutorial.** DONE. A **non-blocking** 4-step callout on the
      first-ever run (mission 1 only). `Game.TutStep`/`TutPrompts` + flags `_tutMoved`/
      `_tutOver`/`_tutShot` (set in `IssueMove`/`DoOverwatch`/`IssueShoot`); `UpdateTutorial`
      advances on the prompted action, `EndPlayerTurn` advances it too (so it never sticks),
      and the final step auto-dismisses after 7s. `StartTutorialMaybe` (in `SetupMission`)
      gates on `!NoPersist && Mission==1 && !Display.TutorialSeen` and calls
      `Display.MarkTutorialSeen` (persisted in `display.json`) so it only ever shows once.
      `Hud.DrawTutorial` renders a word-wrapped "TRAINING x/4" tip card (`WrapText` helper).
      Off in the harness (NoPersist). Verify: `SIGHTLINE_TUTORIAL=1` screenshot. (ASCII-only.)

- [x] **3.13 Accessibility & display extras.** DONE (brightness + colorblind; contrast +
      text-scale deferred). **Brightness:** `Display.BrightLevels` (70–130%) applied as a
      translucent darken/lighten quad in `Display.DrawBrightness` (called at the end of both
      `RenderFrame` paths; neutral 100% draws nothing → headless byte-identical).
      **Colorblind palette:** `Pal.SetColorblind` swaps the threat/good hues to a
      deuteranopia/protanopia-safe set (Foe red→vermillion-orange, Good green→blue-green;
      blue Friend unchanged) — `Pal.Foe`/`FoeDk`/`Good` made mutable. Both live in the
      **pause menu** (`Hud.PauseBright`/`PauseColorblind`, card grown to 9 buttons) and
      persist in `display.json` (`Display` Dto `BrightIdx`+`Colorblind`, applied in `Load`).
      Verify: `SIGHTLINE_CB=1` (orange foes) + `SIGHTLINE_PAUSE`+`SIGHTLINE_BRIGHT=1` (menu +
      dim) screenshots. ~~**TODO:** a true contrast/gamma post-pass (needs a shader) + an
      independent UI text scale (invasive — all DrawText sizes are fixed).~~ **BOTH DONE** —
      true gamma landed in APEX W9 (`uGamma` in the post-FX shader); the **UI text scale**
      landed in RESONANCE W5 (`Display.UiScaleLevels` 90/100/110/120%, applied once in
      `Cfg.Text`/`Cfg.Measure` with a size taper; see DEVLOG §W5 ON-RAMP). Still open from the
      same family: **key rebinding**.

**PHASE 3 IS COMPLETE — every item 3.1 through 3.13 is DONE and on `main`.** The game is
feature-complete against the whole spec. Remaining work is now *open-ended polish*, not a
fixed roadmap. Highest-value next ideas (pick by feel): tune the procedural music once it's
been heard; a true contrast/gamma post-pass (needs a shader) + UI text scale (3.13
follow-ups); enemy AI using utility items + exploiting the commanding-view LoS; themed
authored arenas per biome (`Maps.cs`); more authored maps using the `=` tier-2 legend; mid-
mission save granularity; a VIP/captive-EXTRACTED win flourish. Keep the hard rules: NO
CI/test-runner, ASCII-only drawn text, verify via the `SIGHTLINE_*` harness + autoplay +
screenshots, ship compiling code to `main`.

---

## ROADMAP — PHASE 4 — encounter design & information feel (specced)

> **Design rationale + the fog-of-war decision: see [`docs/DESIGN.md`](docs/DESIGN.md)
> §5-6.** Phase 1-3 made the game wide and juicy; a research pass into design
> fundamentals found the **weakest link is the encounter *opening*** — first contact is
> an accident, not a choice. With an 18-wide map, ~8-tile moves, and a 12-tile pod sight
> range, almost any advance trips a pod on turn 1 (a *flow*/anxiety + *telegraphing*/
> gotcha violation). Phase 4 fixes that **while staying perfect-information** (keep
> threat preview, %-to-hit, full-board readability). **Fog of war + a restrictive camera
> is deliberately DEFERRED** — that's an identity pivot toward a recon-survival game, to
> be prototyped behind a flag only if the items below prove insufficient (DESIGN.md §5).
> Ordered by leverage and risk — do the safe, high-value UI win first.

- [x] **4.1 Full-bleed, translucent, non-cropping UI.** DONE. The board was a 1008x616
      cropped island (~60% of the window) framed by opaque-ish bars. Bumped `Cfg.Tile`
      56->64 (board now 1152x704, ~79% of the screen) + `Cfg.OriginY` 64->40, so the play
      area fills the frame and the dead margins (esp. the empty right strip) are reclaimed.
      Tile 64 is chosen so the left roster strip still just clears the leftmost player
      column. The HUD now **floats over the board**: the bottom bar is screen-anchored
      (`barY = ScreenH-106`, decoupled from the board) with a taller scrim, and the
      floating panels (roster chips + unit card) get a soft drop-shadow (`Hud.PanelShadow`)
      so they read as hovering over busy terrain. Everything board-side moved coherently
      because all tile<->px math routes through `Cfg`/`Util` (no other code touched).
      Verified: build 0/0, autoplay clean (no exceptions/TIMEOUT), screenshots across
      Eliminate/Extract, biomes, and the pause/shop/campaign-map overlays. **Follow-up:**
      the HUD is still mostly always-on; *contextual / progressive disclosure* of panels
      is a future polish, as is scaling the unit-figure constants (they're ~12% smaller
      relative to the larger tiles now — still readable, intentionally left simple).
- [x] **4.2 Encounter geometry — spacing, density, standoff.** DONE (kept the map at
      18x11 per "**NOT raw map size**" — the full-bleed 4.1 board already fills the
      window; bigger = camera/scroll, out of scope). Three levers, all in
      `Mission.Build` + `Game`: (1) **`Game.SightRange` 12->9** so the squad can creep
      closer before a pod wakes (pods are always *drawn*, so perfect-info is preserved —
      only *activation* is delayed). (2) A staggered **mid-field SCREEN of high cover**
      (cols 7-11, in the procedural generator) that breaks the long cross-board
      sightlines; no column is fully walled and **row 5 is left as an open "risky direct"
      lane**, so a soldier can advance into the midfield under cover without auto-tripping
      a pod (footing!), while the open lane is the deliberate high-risk route. Sprinkles
      biased a touch toward high cover (LoS-blocking). (3) The free reveal-scatter in
      `Game.ActivatePod` is **capped to a single move** (was a full `Ai.Plan` dash — the
      most-criticized "free move on reveal"); an immobile turret now gets none. Plus a
      `Mission.EnsureConnectivity` safety net (carves a lane if the denser cover ever
      walls a hostile/objective off — runs for both layout paths). Verified: build 0/0,
      autoplay x6 clean (no TIMEOUT — connectivity holds), `SIGHTLINE_SHOT` openings show
      the screen + covered approaches, authored maps (`SIGHTLINE_MAP`) still apply.
      **Follow-up:** PLAZA/ZIGGURAT authored arenas stay deliberately open (variety); the
      enemy AI doesn't yet exploit the screen's LoS. Next: 4.3 alert tiers, 4.4 concealment.
- [x] **4.3 Alert / awareness tiers (green -> yellow -> red).** DONE. Binary
      dormant->instant-scatter is replaced by a 3-state `AlertLevel` (Unaware / Suspicious /
      Alert) on `Unit`; `Active` is now derived `=> Alert == AlertLevel.Alert`, so every
      read site is unchanged. Pods escalate gradually: a soldier sighting one within
      `SightRange` (9) sets the pod **Suspicious** (amber "!" + pulsing ring, a "CONTACT?"
      telegraph) WITHOUT acting or scattering; at the player's turn end `Game.ResolveSuspicion`
      either confirms it (-> Alert, acts that enemy turn, **no free scatter** since it had a
      turn's warning) if still in sight, or it loses interest (-> Unaware) if the squad broke
      contact. Blundering within the new `AlertRange` (4) or any aggression (shoot/pin/grenade)
      still snaps a pod straight to Alert **with** the (4.2-capped, single-move) reaction
      scatter. So first contact is telegraphed and the free scatter is softened to surprise-only.
      Renderer draws the three glyph states; `Game.DebugAlertTiers` + `SIGHTLINE_ALERT=1` shot
      shows all three. Verify: build 0/0, autoplay x5 clean (no TIMEOUT), `SIGHTLINE_ALERT` shot.
- [x] **4.4 Concealment + ambush (the marquee mechanic).** DONE. Squad starts every mission
      **concealed** (`Game.SquadConcealed`, set in `SetupMission`): it scouts/repositions freely
      and `CheckPodActivation` is gated so pods can't escalate via sight. **The player chooses
      when to break stealth** — first shot/grenade/flashbang/pinning fire, or stepping within
      `RevealRange` (3) of an active foe (`Game.BreakConcealment`, called from IssueShoot/
      IssueGrenade/IssueItem(Flash)/DoAbility(Suppress)/OnUnitEnteredTile/AutoStallCheck). The
      breaking shot springs an **ambush**: `Unit.FiredFromConcealment` grants +20 aim/+25 crit
      (`Combat.AmbushAim/Crit`, consumed by that one shot, `ShotOdds.Ambush` -> "+ AMBUSH"
      tooltip); pods already in sight wake with the 4.2-capped scatter, then the normal 4.3
      alert tiers resume. HUD shows a pulsing CONCEALED pill (in the MISSION slot); friendly
      units get a ghost ring. Autopilot springs the ambush when it has a shot (no TIMEOUT).
      Verify: build 0/0, `SIGHTLINE_CONCEALTEST`=PASS, `SIGHTLINE_COMBATTEST`=PASS, autoplay
      clean x5 + sabotage/rescue/defend, `SIGHTLINE_CONCEAL=1` shot.
- [ ] **4.5 (DEFERRED — flagged prototype only) Fog of war + soldier-focused auto-cam.**
      Do NOT build unless 4.1-4.4 ship and playtests still want the recon-survival genre.
      If attempted: a visibility mask behind a flag, on the larger maps, with strong
      auto-framing (snap-to-selected, auto-pan-to-action). Judge on one question: is
      partial-info SIGHTLINE *more fun* than full-info, knowing it costs threat-preview +
      readability? See DESIGN.md §5.

Supporting / any-time (now grounded by DESIGN.md §3-4): output-randomness mitigation
(graze/partial hit, guaranteed-damage floor, many small rolls — DESIGN.md §3B); audit
for a dominant overwatch-camp strategy + false-choice perks (§3A/§4); the bench /
deploy-short-handed half of attrition so a wipe actually shrinks strength (§3F, ties to
3.1). The Phase 3 Tier-4 items (3.10 music, 3.12 tutorial, 3.13 accessibility) still
stand and are reinforced by DESIGN.md §3D/E/G.

---

## ROADMAP — PHASE 5 — visual identity & presentation (specced)

> **Design rationale + style guide: [`docs/DESIGN.md`](docs/DESIGN.md) §3.H.** Orthogonal
> to Phase 4 (encounter design) — can interleave. Enabled by the **clarified asset
> policy** (see the pillars block): generated assets are allowed (procedural / in-engine /
> shader first, AI only where it clearly wins; commit small generated files, no large
> binaries). Engine note: Raylib generates noise/gradient textures (`GenImage*`) and bakes
> TTF/OTF fonts (`LoadFontEx`) in-engine, so most of this needs **no committed binaries** —
> a small font file is the main exception. Ordered by impact-per-effort.

- [x] **5.1 Visual style guide (docs).** DONE. `DESIGN.md` §3.H locks the **semantic color
      roles** table (friendly / enemy / cover / objective / neutral — one job per accent),
      the **60-30-10** split, the **squint-test** acceptance check, AND a **shape/icon
      redundancy** rule (meaning never rides on hue alone — every coded state ships a glyph;
      verify in both palettes). Everything below conforms to it.
- [x] **5.2 Post-processing pass.** DONE (Sprint 1). An embedded-GLSL post stage in
      `src/Display.cs` over the render-target: soft **vignette**, event-reactive **bloom**
      (`Game.AddBloom` spikes on hits/kills, decays), subtle per-**biome color grade**, and
      impact **chromatic aberration**. Shader is a C# string (`LoadShaderFromMemory`, no asset
      files), guarded by `IsShaderValid`. Headless harness keeps Display OFF (byte-stable
      shots); `SIGHTLINE_POSTFX=1` forces it on to view. Bloom kept subtle in live play.
- [x] **5.3 Real font (kills the ASCII `?` limit).** DONE (Sprint 2). Committed
      `assets/NotoMono-Regular.ttf` (107KB, SIL OFL-1.1, free to redistribute) baked at 64px
      via `LoadFontEx` in `Program.cs` (ASCII + em/en-dash, curly quotes, bullet, ellipsis,
      x, middot), stored as `Cfg.Font` with a `GetFontDefault()` fallback. ~154
      `DrawText`/`MeasureText` sites migrated to `DrawTextEx`/`MeasureTextEx` (Hud/Renderer/Fx),
      sizes+positions unchanged. **The ASCII-only constraint is now lifted** — non-ASCII
      glyphs are available; new strings can use them (though most still ASCII for now).
- [x] **5.4 Procedural texturing & particles.** DONE (Sprint 5 + Sprint 6). A 128x128
      `GenImagePerlinNoise` texture (lazy-init in `Renderer`, crash-safe fallback, unloaded on
      shutdown, board-origin-anchored UV so software GL shows no seam) tiles over floor/cover-tops/
      plateau faces at 9-11% alpha, biome-tinted via `Pal.Mix` (`Renderer.DrawNoiseRect`);
      particles got a dim-halo + bright-core soft-glow (`Fx`). Generated **HUD action-bar icon
      glyphs** landed too (`Hud.DrawActionIcon`, primitive-drawn, inherit button text color).
      Subtle — squint test holds per biome.
- [~] **5.5 Semantic color + colorblind pass (folds in 3.13).** PARTIAL. Colorblind-safe
      `Pal` variants + the persisted toggle shipped in 3.13; **shape/icon redundancy** now
      covers the action bar (`Hud.DrawActionIcon`), the **objective readout**
      (`Hud.DrawObjectiveIcon` — crosshair/brackets/arrow/diamond/spark/cage/shield) and
      **on-unit status effects** (`Renderer.DrawStatusGlyph` — flame/droplet/star/swirl beside
      BRN/BLD/STN/DAZ), plus cover's △/— cues — all primitive-drawn, inheriting the role colour
      so they work in every palette. **Still open:** a full audit enforcing the 5.1 colour roles
      across *every* `Renderer`/`Hud` draw site (some biome tints/accents still ad-hoc); a UI
      text-scale (3.13 follow-up). Verify: `SIGHTLINE_CB=1` + `SIGHTLINE_MISSION=2 SIGHTLINE_SHOT`.
- [x] **5.6 Focal-point lighting.** DONE (Sprint 3, focal half). `Renderer.DrawUnit` threads
      a per-unit figure alpha: selected = 1.0 (+ a soft outer glow halo), spent player 0.60,
      other friendlies 0.82, enemies 0.85 (threats stay visible). All SIGNAL stays full-alpha
      (selection/ghost/VIP rings, HP pips, status codes, alert ?/! markers, labels, damage
      flash). Squint test holds. (Still open: emissive cover/plateau edges + faux 2D lighting
      via the 5.2 post pass.)

---


## PROGRAM "COUNTERPLAY" — closed items (see docs/DEVLOG.md for the full write-up)

- [x] **Cross-run veteran carry-over** (the long-deferred replay keystone). Promoted survivors of a finished
      run retire into a persistent VETERAN reserve (`meta.json`, append-only, capped 12); a new run's DRAFT
      recalls up to 2, carrying rank/perks/traits/spec/scars. `SIGHTLINE_VETTEST`.
- [x] **Game.cs partial split** (deferred across many programs). `Game.cs` 7648→4707; the autopilot →
      `Game.Autopilot.cs`, the Debug/SelfTest harness → `Game.Harness.cs`. Behaviour-neutral (proven
      IL-identical per method).
- [x] **Biome visual identity** lands above the squint-test floor (floor/signature/ambient/grade); dormant-pod
      + enemy-intent legibility.
- [x] **Focused (cone) overwatch** — a directional braced kill-lane (+aim, blind outside) vs the wide watch;
      purely additive so the base can't regress. `SIGHTLINE_OWTEST`.
- [x] **3 new authored arenas** — CAUSEWAY / REDANS / DONJON (pool 32→35).

Open / next: watch the veteran-recall power floor via the flywheel (a persistent 2-of-6 veteran draft could
ease low-Heat difficulty over many runs); on-device audio; endless difficulty curve; the design fan-out's other
player-verb ideas (universal suppress, objective-interaction forks, a banked enemy-turn reaction).

---

## PROGRAM "UNDERTOW" — closed items (see docs/DEVLOG.md for the full write-up + measured numbers)

Thesis: SIGHTLINE was one-directional attrition with no enemy will-state, so matches tipped once and never tipped
back (lead-swings/match 0.48, policy gap +29.2). The fix: add the *missing half* of the action economy — mechanics
that SUBTRACT enemy tempo/will, not add HP. Flywheel-validated: lead-swings 0.48→0.59, the punish-spiral gap
collapsed, run-completion 60→73%, Evac drag 10.9→7.8t, dead economy/perks revived.

- [x] **W2 — BRACE interrupt** (the keystone): a disrupting reaction stance that STAGGERS a mover (denies its action
      this turn) for reduced damage — trade a kill for tempo, the earnable comeback lever. `SIGHTLINE_STAGGERTEST`.
- [x] **W3 — enemy pod MORALE / ROUT**: a pod chewed to ≤ half spawn strength routs its survivors (flee, drop
      overwatch, shoot wild, then rally). The second kill panics the pod. `SIGHTLINE_MORALETEST`.
- [x] **W4 — sequenced coordination**: setup verbs act before finishers + a live per-unit focus recompute, so the
      pod collapses on a freshly-exposed soldier the same turn (the counterweight that restores the skill premium).
- [x] **W5 — balance roots**: LockOn de-superset (flank-only, not any-exposed); PLATING de-throned from the
      autopilot's always-buy slot (369→203 buys, dead perks revived).
- [x] **W6 — de-drag Evac/Escort**: a player-planted forward EVAC beacon (with a fallback corner so it can't
      soft-lock) + a VIP leash. Evac 10.9→7.8t. `SIGHTLINE_BEACONTEST`.
- [x] **W7 — board-space depth**: key light + cover legibility + AO (Renderer-only, deterministic, colorblind-safe).
- [x] **W1 — double-kill correctness fix**: idempotent `KillUnit` + surplus-reaction purge; honest kill telemetry.

Open / next: the ESCORT leash lifted win% but left escort turns UP (~14t, a corner fight not empty walking) — a
forward beacon for Escort is the clean de-drag; the softened policy gap (~0-4) is forgiving-by-design with the new
comeback levers, watch it doesn't slide negative; more setup-verb archetypes to exercise W4's coordination.

---

## PROGRAM "APEX" — closed items (see docs/DEVLOG.md for the full write-up + measured numbers)

Thesis: the game's TOP END was fictional — heat 7-8 could hard-crash, LAST STAND fought the campaign's hidden
pressure clock with no progression, the setup-verb archetypes were unreachable in faction fights, and the
flywheel was blind to all of it. Fix correctness → give the instrument eyes → ship the real top end → close
the flagged drags. Research: 6-lens fan-out, every wave adversarially verified against live code pre-dev
(6 of 9 designs corrected). Owner feedback mid-run became its own UI wave.

- [x] **W1 — heat>=7 zero-roster crash** fixed (emergency conscripts at Count==0) + endless freed from the
      campaign pressure clock. First heat-8 number: 25%. `SIGHTLINE_HEATLADDERTEST`.
- [x] **W2 — interactive correctness:** Rescue captive truly caged + CAPTIVE ABANDONED (campaign+skirmish);
      overwatch resource leak (predicted-HP break); tutorial teaches the shipped fire rule. `SIGHTLINE_RESCUETEST`.
- [x] **W3 — persistence armor:** atomic save/meta writes + corrupt-file .bak evidence (no silent meta wipe).
- [x] **W4 — flywheel eyes:** heats {0,2,4,6,8}; BALANCE_ENDLESS depth stats; VETSIM; win-rate-by-boon/spec/
      contract; value-biased perk picker (guarded: 85%→85%, starved perks revived).
- [x] **W5 — content reachability:** 4 archetypes join faction rosters (0 → 2-4% of faction spawns); Defend
      waves diversified fairly; 40 dedup'd callsigns (squad+fallen+reserve).
- [x] **W6 — the enemy plays better:** commanding-LoS truthfulness + crossfire pin (h0 gap −20→+5); data-driven
      Ai.Tier at rungs 6+; NO QUARTER +1 dmg. h8 harder via play quality (choices/turn 1.56→1.79).
- [x] **W7 — LAST STAND ladder:** opener grace; mid-stand promotions/boons; heal decay + elite ending. Depth
      median 3 → 5-6, p90 finite, zero caps in 192 stands.
- [x] **W8 — Escort de-drag + gap lever:** leash through real anims (fire/overwatch apply); Escort-only
      far-third cold-LZ beacon; depth-scaled recruits. Escort 12.9-15.8t → **5.8t** at 96% win.
- [x] **W9/W10 — presentation + owner-feedback UI:** honest odds banding; true gamma; wrapping action bar
      (no ellipsis, ever); 13px modifier rows; three-zone top bar; map-label + chip-occlusion fixes.

Open / next: endless overall greedy median is 5 vs the 6-8 target (h0 in band at 6) — next lever is the
EndlessWaveScale toughness ramp; run the VETSIM=2-vs-0 pricing batch (instrument shipped, measurement
pending); the aggregate policy gap is noisy at N=20 pins — trend it across future full-ladder batches;
on-device audio + endless FEEL still need the human. Heat-2's corrected baseline is 63% (accepted with the
truthfulness fix).

## PROGRAM "SIGNAL" — closed items (see docs/DEVLOG.md for the full write-up + measured numbers)

Research: six parallel lenses (visual/UX, design gaps, code health, balance, content, onboarding) →
PM synthesis → two-verifier adversarial sharpening → a 12-wave plan; every wave dev'd in an isolated
worktree, adversarially reviewed, and merge-gated on self-tests + autoplay + Release 0/0.

- [x] **W1 Mode-seam integrity.** End-card MAIN MENU (+ overwrite warning), draft BACK, mode-aware
      checkpoint-preserving abandon, ResetModeState() at all five mode entries, NoPersist-gated daily
      env seed (cross-process leak proof), endless mid-stand boons actually republish (were dead).
- [x] **W2 Compass rebuild.** CRN-paired policy legs (PAIRTEST pins A/A identity), positional
      sloppiness on an isolated RNG stream, ACTION MIX across ~26 verbs, win-rate BY PERK/PURCHASE/
      ARENA + fallback rate, whole-run objective pinning, DoT attribution ('?' deaths 11%→0%).
- [x] **W3 Board reads.** Biome-true plateaus (0/8 → 5-6/8 separated), visible focus cone, 13px
      late-pass status pills, role-shaped rings (square/hex/dashed), member-tile EVAC label.
- [x] **W4 Rescue repair.** Caged-captive soft-lock closed at every damage entry point; freed Rescue
      gets the Escort beacon+leash. 9.0t/67% → 6.07t/98.3% per-mission at h0.
- [x] **W5 Boss identity.** Capability flags (HasShieldArc/HasSiege), faction mid-boss signatures,
      three finale kits (SIEGELORD/SPYMASTER/WARLORD) hashed from MapSeed; m6 96% → 82% conditional.
- [x] **W6 Heat ladder tooth.** Fresh paired baseline retired the stale table; rung-4 coordination
      tooth (AiTier=1) at zero completion cost; finale body heat-gated to h4+. Final ladder
      80/70/50/32.5/17.5 (goal 80/70/60/40/20 ±8), h8 ≥10% floor restored, no policy inversion.
- [ ] **W7 Exposure plumbing — NOT SHIPPED (docs over-claim caught at landing verification).**
      The program's docs commit claimed this wave, but no W7 commit, no SIGHTLINE_EXPOSURETEST
      hook, and no column-constraint/arena-deck code exist in the tree (Run.cs CardForNode is
      plain ObjectiveFor(n + node.Row)). Spec carried forward as ready-to-dev: column-constrained
      objective assignment (every path: ≥1 Eliminate, ≥1 Defend-or-Rescue, ≤1 Escort), per-run
      no-repeat arena deck, biome-true arena hints, SIGHTLINE_EXPOSURETEST 200-seed histogram.
- [x] **W8 Morale visible & contested.** WAVERING telegraph (truthful, banner-aware), WARBRINGER
      banner anchor (Cheb-4 aura, 1/mission), CUSTODIAN objective re-locker; routed specialists
      (medic/bombard/custodian) now actually flee.
- [x] **W9 Salvage economy.** Priced veteran recall (10+8×rank, atomic in ConfirmDraft), pending-
      ledger barracks sinks (REHAB, re-rolls — quit-safe), three horizontal unlocks, heat-multiplied
      bounty, once-per-stamp daily payouts + streak; METATEST save.json clobber fixed.
- [x] **W10 Pool expansion.** Six verb boons (SHOCK DOCTRINE, TERROR (duration redesign), FIELD
      DRILLS, PYROMANIACS, FIELD STORES, RECLAIMER), BIPOD + SUPPRESSOR (target-pod-only wake),
      GHOST/DEMOLITION/BOUNTY secondaries, the INTEL CACHE. All enum tails append-only, pinned.
- [x] **W11 Teach it where it's played.** Wrapped help, enemy ID tooltips, FIELD CRAFT rules codex
      (every number code-verified), honest loss cards, NEW CONTACT banners, HUD de-occlusion,
      tutorial re-offer loop closed.
- [x] **W12 Strategic facelift.** Sized-to-fit campaign map (labels + legend), class glyphs across
      the meta screens, coherent intro hierarchy, WAR ROOM progress bars + NEXT UNLOCK card,
      promotion delta lines, first-run RECOMMENDED draft.

## PROGRAM "FULCRUM" — CLOSED 2026-08-28 (13/13 waves landed; the measured close is docs/DEVLOG.md §FUL-13)

Research: six fresh lenses on the post-SIGNAL tree → PM synthesis → orchestrator code-sharpening.
Through-line: **systems that exist but never reach play** — the comeback economy (BRACE/morale/verb
boons) plays out over 2-enemy pods and 3-4-turn missions where it can never fire; the balance bot
has used BRACE zero times in ~500 measured missions, so a whole verb layer is balance-blind; 52%
of missions skip the 35 authored arenas; Defend is a hidden 23%-win cell; the run's biggest
rewards are invisible or don't exist (no downed-soldier drama). This section is the durable plan
of record (container suspensions have wiped every scratchpad copy — docs are the only safe store).

- [x] **FUL-3 CHROME** (16e24e9). Roster chips reflow full-size below the strip instead of
      collapsing to a one-letter rail from turn 1 (incl. VIP/captive chips); action-bar dim made
      truly per-button (floor 0.45, dormant pods exempt); INTEL cache row-clamped out of HUD
      shadow (clamp-not-reroll, draw-count stable); row-0 marker labels flip below tile; dormant
      bodies 0.75x/0.85x + tighter ring; LOCK-ON/NO QUARTER desc truth ride-alongs.
- [x] **FUL-2 SEAM INTEGRITY** (4690748). In-session assist cache refreshed at run end (was
      EnsureMetaLoaded-only — same-sitting runs read a stale streak); SIGHTLINE_INTRO shot
      stash/restores a real save.json (was a silent clobber); codex-from-pause no longer resumes
      queued enemy shots (anim queue freezes in Phase.Codex, pause restored on exit); EXTRACT
      routes its pull through OnUnitEnteredTile (BIPOD disarm/bleed/burn/cache/overwatch apply;
      CheckEnd deferred while reactions queue); supercover LOS made real — a sealed diagonal
      corner blocks sight at range both directions (point-blank keeps the true-corner=cover
      exception; +6 COMBATTEST legs); Pinned comment truth. LOS budget A/B (CRN slots, h0,
      N=10/leg): completion 65%→60% (at the ±5 boundary, in budget); leg swings (greedy 70→50,
      sloppy 60→70) are ~1.3 SD at n=10 — carried as a FUL-13 watch item, fix retained per the
      W6a truthfulness precedent.
- [x] **FUL-1 COMPASS TRUTH** (a4ef1dd). Telemetry-only compass upgrade (zero game-logic change,
      NoPersist-safe, CRN draw-count neutral): per-slot pair records + all-pairs missions-cleared
      PAIRED MARGIN (every pair contributes, ~halves CI; ±SE printed) in report+JSON;
      (code,heat)-keyed "@h<N>" win-rate tables when a batch spans heats; binomial ±SE on n<30
      win-rate rows; boon PROC counters at the six effect sites as a PROCS column — h4 N=10:
      SHK 7 picks/0 procs, FDR 3/0, RCL 3/0, PYR 4/2 (the FUL-5 finding, now measured; FST/TRR
      fire); arena funnel authored-applied/connectivity-reject/procedural-roll summing to 100%
      (h0: 52.6/0.0/47.4 — the guard rejects ~nothing, the 55-roll IS the funnel); BY ARENA
      stratified by mission; RecordEvent(id,arm) + BY EVENT-CHOICE tables; SIGHTLINE_PERK=<code>
      probe (PerkDef.Parse mirrors ContractDef.Parse; override lands AFTER the value roll draws —
      probe-off RFX 1 pick vs probe-on 10/10). DESIGN.md §4 re-graded: engagement mass + death
      stakes named the thin pillars. Verified Release 0/0, PAIRTEST/SAVETEST, BALANCE=10 h0+h4,
      paired 2xN=5 probe; logic-identity vs a706152 by seeded-autoplay frame-exact A/B (5 seeds)
      + BALANCE=2 same-slot JSON field-identity (29/29 base-schema fields) — SHOT byte-equality
      is environmentally impossible for any change (clock-seeded RNG + wall-clock pulses; see
      the FUL-1 DEVLOG gotcha).
- [x] **FUL-4 HOLDFAST** (wt-ful4 through 021a84b). Defend 38% h0 (fresh n=32 reference; the
      23.1% audit number was an older tree) / gap ~0 → **66-73% h0** (two disjoint CRN batches,
      n=59/n=70), **69% h4**, |gap| <= 8 every round — all three bands HIT. Levers landed, one
      measured round each: SmartDefend co-fix FIRST (fall back to better cover / refuse a flank),
      defend flag in SpawnEnemies (opener count-3, mirrors the sabotage trim), first wave graced
      to t3, wave size 1+m/2, waves as real morale pods (ids 100+, _podOrig-snapshotted;
      pressure-clock waves stay morale-exempt), wave-edge telegraph one player turn ahead (shared
      DefendWaveTurn read; SIGHTLINE_WAVEBANNER shot hook). The rich-tier cap + stop-t5 levers
      were NOT needed — the band was reached without them (still in the toolbox for FUL-13).
      Budget: unpinned h0 completion 60% (unchanged; FUL-13 input); nominal ±3 breaches on
      Sabotage/Decapitate shown to be reference noise by a zero-Defend null batch (full rounds
      table + analysis in docs/DEVLOG.md).
- [x] **FUL-11 CEREMONY** (P11, M — wt-ful11). m6 finale presentation kit (HVT-named intro card, red
      top-bar plate, boss ring/aura in DrawUnit, first-sighting banner via NEW CONTACT lane); Wardens
      finale kit — MakeFinaleRetinue Wardens: SIGNIFER banner (pod 0) + ORDERLY medic (MEDIC over
      CUSTODIAN — the boss node is always Decapitate, a keeper has nothing to re-lock; the TERROR
      lesson), cost-neutral cascade-fill replacement (zero extra RNG draws); deterministic no-RNG
      post-pass guarantees the banner aura covers the boss as spawned (FUL11PROBE: bannerDistMax 4,
      retinue slots present at h0's 6-body and h4's 9-body finales, 20 seeds x 3 kits x 2 heats).
      **Measured (h0, 30 campaigns/kit, CRN slots 0-29 shared across kits): Wardens 73 → 83.7,
      Legion 87.5, Syndicate 81.6 — all in the 78-88 band; pooled 84.2 (n=146) vs the 82±4 goal.
      No tuning needed; no breaches.** Landed before FUL-13's baseline as sequenced.
- [ ] **FUL-12 SIGNPOSTS** (P12, L). Run-end card SALVAGE/HEAT-UNLOCKED/ACHIEVEMENT slabs (new
      Game fields from AwardMetaRunEnd/UnlockHeatOnWin — no Report parsing, NoPersist-gated);
      tutorial concealment/AMBUSH step + FIELD MANUAL pointer + one-shot BRACE callout (careful
      around TutStep>=2 gates at Game.cs:1586/:1730); dormant-enemy ID tooltip (DrawTooltip bails
      at Hud.cs:1391 on !ShowOdds — draw ID-only card + alert-state line); top-bar pill hovers
      (CONCEALED/PRESSURE/HEAT/CACHE rects → DrawHudHovers); tutorial bar hierarchy (lesson verb
      bright, rest ~45%, composes with FUL-3's per-button dim); RECOMMENDED draft full 16-boon
      ranked order (21.4% arbitrary-fallback measured); campaign legend + ('*','BATTLE')
      ('S','START'); DrawCodexGlyph into Hall of Fame + end-card squad/KIA rows; WAR ROOM panels
      sized to content. (LOCK-ON/NO QUARTER copy already done in FUL-3.)
- [x] **FUL-12 SIGNPOSTS** (wt-ful12). All nine sub-items shipped: run-end card SALVAGE slab +
      HEAT-UNLOCKED line + achievement roll via new Game fields (EndSalvage/EndHeatUnlocked/
      EndAchievements set in AwardMetaRunEnd/UnlockHeatOnWin/TryAchievement — no Report parsing,
      dark under NoPersist; SIGHTLINE_SUMMARY stages them); tutorial gained a concealment/AMBUSH
      step 0 + FIELD MANUAL pointer in the wrap-up (named TutStep* constants keep the FIRE-lesson
      completion gates semantic across the renumber); one-shot BRACE field tip (Display.
      BraceTipSeen, never overlaps a lesson card, SIGHTLINE_BRACETIP stages); dormant-enemy hover
      ID card + alert-state line (DrawTooltip no-odds path, SIGHTLINE_IDHOVER); top-bar pill
      hovers — turn/CONCEALED/HEAT/PRESSURE/CACHE all card on hover (SIGHTLINE_HOVERHUD grew the
      ids); tutorial bar hierarchy (lesson verb bright, rest 0.45, min-composed with FUL-3's
      per-button dim); RECOMMENDED draft ranks the full 16-boon pool (arbitrary fallback now an
      unreachable guard); campaign legend names S/START + */BATTLE; DrawCodexGlyph into Hall of
      Fame + end-card squad/KIA rows; WAR ROOM panels sized to content. (LOCK-ON/NO QUARTER copy
      was already done in FUL-3.)
- [x] **FUL-5 HANDS** (wt-ful5). The EV bot learned the verbs; per-20-campaign h0 batch vs the
      spec targets: BRACE 1 → **82** (>=5; the real stage was routing Defend/Escort zone-holds
      through HoldOverwatch's rusher arm — the open-combat gates were provably unreachable, two
      byte-identical probe batches), ITEM 0 → **19** (>=5; TrySmokeCover on the exposed sub-half
      retreat, objective-agnostic), DRAG 0 → 5-7 (Escort march/hold straggler pull), PATCH 1 →
      4-6 (target 10: **verdict** — capped by corpsman presence, founding squad has none;
      gates ready for FUL-7/roster work), GRENADE 8 → 6-8 (target 10: **verdict** — the
      covered-cluster window anti-correlates with shot declines; FUL-6's pods-of-3 is its
      stage). PROCS: SHK 0 → **6**, FST fires, FDR 0 → 0 (**verdict** + FUL-6 rework brief:
      the second-use geometry is self-consuming). AutoEventChoice 70/30 value-biased hashed
      off (MapSeed,node) — zero draws, PAIRTEST-clean; BY EVENT-CHOICE safe-arms-only → 9 arms.
      COUNTER-PREP 0 → 10-12 buys. Mod priors de-flattened: SUPPRESSOR 27% → **9%** of mod
      buys. h0 completion 60 → 75 ±10 on the same CRN slots (bot got better — FUL-13 input:
      the finished-tree h0 baseline under this bot is ~75). Full rounds table + verdicts in
      docs/DEVLOG.md §FUL-5.
- [x] **FUL-9 THE DECK** (wt-ful9). The carried W7 spec, finally BUILT (not just claimed):
      column-constrained objective assignment in CardForNode hashed off (MapSeed,column,row)
      via Util.Hash3 — an event-free ANCHOR mid column deals Defend(80%)-or-Rescue on every
      node, Escort on EXACTLY one hashed node per map (<=1 per route; zero-Escort maps no
      longer occur), START stays Eliminate, boss stays
      Decapitate, the rest deal from an Escort-free 7-pool (per-column offset + row keeps
      siblings distinct) — so >=1 Eliminate / >=1 Defend-or-Rescue / <=1 Escort holds on EVERY
      route by construction (zero rng draws: map shape/kinds/edges/factions byte-identical, so
      saves round-trip; ObjectiveFor stays the skirmish/offer fallback). Per-run no-repeat
      arena deck derived PURELY from MapSeed (Hash3 Fisher-Yates over all 35, recomputed per
      draw — nothing persisted), biome hint reduced to a 25% pull-forward of the DISPLAYED
      biome's arena (Biome.IndexFor; was mission-number-keyed 50%, the FUL-1 confound);
      authored roll 55→80 keeping EXACTLY one Util.Roll (the draw-order contract at the gate:
      PickLayout now takes ZERO draws). SIGHTLINE_EXPOSURETEST (200 seeds, 1098 routes
      ENUMERATED): invariant on all routes, zero in-run deck repeats, all 8 objectives + all
      35 arenas dealt (min 17 draws) — PASS. Measured (paired h0 N=10 x2 slot sets): funnel
      52.6/0.0/47.4 → 76-77/0.0/23-24 (procedural 20-25 HIT); Defend >=80% of runs met on 2 of
      3 slot sets (80/85/65 — the floor is early-death-sensitive; FUL-13 input), fielding
      n=17-19/batch at 59/74% (pooled 67, FUL-4's band; base fielded n=4);
      distinct authored arenas 3.4/full-depth run over 4.5-5.0 fights (the 4.5 target assumed
      6 authored fights/run — events + the 23% procedural floor cap the ceiling at ~3.5-3.9,
      ~90% delivered; repeats are now impossible vs the old with-replacement sampling).
      Budget: h0 completion 60 → 50/45 (−10 to −15, OUTSIDE ±7, reported not hidden): the drag
      is the newly-EXPOSED Defend/mid-Decapitate cells on ~every route, not the arenas —
      FUL-13's re-baseline input (full table in docs/DEVLOG.md).
- [x] **FUL-6 CRITICAL MASS** (P6, L) — **landed** (wt-ful6, base 588d781). Pods of 3 (PodPlan
      greedy split, m3+; m1-2 and the finale keep i/2 — FUL11PROBE green by construction) + pod
      cohesion (anchor-row clumping, zero extra draws) + linked activation ("HEARD THE GUNS":
      ActivatePod links the nearest dormant pod within 6 tiles to Suspicious, confirming unseen
      next turn — one link per wake, no chains, zero RNG) + LAST STAND wave sub-pods (100+/sealed;
      elite exempt; TERROR un-excluded from endless boons) + the FIELD DRILLS rework (+1 move
      after a drag/vault, grant-site proc). Measured wave (fresh same-slot R0 first, one lever
      per round): the full stack breached the dip budget (-12.5 vs <=8), so **escalation lever 1
      landed** (count-1 on all m3+ non-finale missions) -> combined h0 completion 40% == R0's 40% (dip 0, in
      budget). GRENADE >=10 prediction did NOT materialize (verdict recorded: woken pods scatter out of the
      bot's frag window — review note: SmartGrenade has NO Active filter, so the low count is
      emergent geometry, not a coded decline of dormant clumps);
      BRACE held >=30; TRR procs 18-31 (rout economy livelier at 3-pods); RCL now procs 1-3/batch
      (no longer structurally dead); FDR 0 procs in wave batches (0 boon-held drags in those
      worlds — mechanism PODTEST-pinned; FUL-7's drag stage prices it). Endless depth median
      5.5-6 (in the APEX 5-6 band), zero cap hits. SIGHTLINE_PODTEST + PODSHOT. Rounds table in
      docs/DEVLOG.md. Full spec: docs/plans/FUL-6-critical-mass-FUL-7-last-light.md.
- [x] **FUL-7 LAST LIGHT** — **landed** (wt-ful7, base c74378e). Lethal damage on a non-VIP
      soldier becomes a 3-turn BLEED-OUT (once per soldier per mission; AoE/fire on a downed body
      stays lethal; enemies never direct-target the downed — the telegraphed-AoE valve keeps
      stakes): all through the single KillUnit seam. STABILIZE universal verb (key E) freezes the
      timer (the freeze needs a standing squad — all-downed boards stay <= 3-turn bounded);
      corpsman PATCH revives; DRAG/EXTRACT carry pinned; EnterBarracks recovers survivors at Hp 1 /
      Wound 3 + the near-death scar track; a bleed-out runs the full death path (Fallen/Memorial/
      honest loss card names the DOWNING archetype — LGD's veteran-erase needed no special case);
      endless wave-clear revives at the mend value. Zero persistence (DTO whitelist + SAVETEST
      leg); no enum touched. **Measured (paired h0, fresh same-slot R0; review-fixed build):**
      soldier true-KIA **-40%** (125→75 on the re-measured chunk; target band 30-50%); save-rate
      **33%** on the honest ledger (review F2 — a body finished while down is a death, not a
      save); STABILIZE ~50-60 uses/chunk; PATCH >= 10 met in aggregate at 0.27/corpsman-fielded-
      mission — corpsman present in only ~38-41% of missions: the roster-presence verdict
      recorded for FUL-13; completion 50% → 60% re-measured (+10, at the budget boundary — saved
      bodies play better, and review F1 restored the enemy focus layer while a body is down).
      h4 close leg + the SHIP-WITH-FIXES review round (F1-F6) in docs/DEVLOG.md §FUL-7.
      SIGHTLINE_DOWNTEST (legs a-h) + DOWNSHOT (both palettes + mid-rescue). Full spec:
      docs/plans/FUL-6-critical-mass-FUL-7-last-light.md; details docs/DEVLOG.md §FUL-7.
- [x] **FUL-8 PIKEMAN** (wt-ful8). The SARISSA — a Wardens lane-holder that plants a braced
      foe-red cone over a movement lane and STAGGERS the first soldier through; the roster's first
      piece that contests WHERE YOU MAY WALK, and it teaches the player's BRACE by being the
      identical verb pointed back (zero new combat machinery — the OnUnitEnteredTile reaction path
      was already team-symmetric). Shipped: Ai.Plan plant branch (opportunism-first, routed/
      Disoriented/dry-gated, SPOTTER-style plant scoring, MORTAR fall-through safety); ActAfterMove
      exec arms the exact player flag set + faces down the lane; renderer truth gate on
      DrawOverwatchThreat (a focused enemy's wash now mirrors the cone reaction gate exactly) +
      shared DrawConeRays (player gold / enemy foe-red can't drift) + PIKEMAN silhouette (squat
      body, raised pike, crossbar) + STAGGERED pop colored by victim team (the green-on-your-own-
      denial lie fixed); codex row SARISSA + CODEXTEST required; Wardens 10% m2+ re-slice + ~3%
      default m3+ cascade tail (CRN draw-count neutral — windows only); bot: InEnemyBraceLane
      mirrors the reaction gate, +18 TileExposure. Verified: PIKETEST (plant / the ==2 halving pin
      on the enemy-side reaction / cone blindness / stagger-back break / no Disoriented-or-Routed
      re-plant) + full battery PASS, Release 0/0, autoplay clean. Measured (CRN-paired slots):
      h0 30→27.5%, h4 35→40% (both inside the ±5 gate); composition PIKEMAN 3-4% of faction-
      stamped spawns (~10% of Wardens fights), 1-3% default; objective-pinned n~90 legs — Escort
      5.9→5.6t (99→100%), Evac 5.5→5.8t (97→98%): the lane taxes routes, it does not stall them.
      Details: docs/DEVLOG.md §FUL-8.
- [ ] **FUL-10 FORKS** (P10, M). Seven trade-off field events crossing salvage/scar/veteran/
      faction/heat (ids+arm order frozen for the compass; PendingSalvageReward run-committed via
      AwardMetaRunEnd — events must never touch meta directly); two veteran-economy contracts
      (MERCENARY CLAUSE: half-price recalls but no enshrinement; LIVING LEGENDS: pensions + double
      rank-kills but KIA erases the reserve record); the orphaned perk trio Vantage/Breaker/
      Siegebreaker joins real class lines. Contract enum append moves TWO tail pins
      (SaveGame.cs:675 + CONTRACTTEST). Full spec: docs/plans/FUL-8-pikemen-FUL-10-forks.md
      (file name: FUL-8-pikeman-FUL-10-forks.md). Bot arm-uptake measurement lands with FUL-5's
      hashed chooser (FUL-10 makes the forks exist; FUL-5 makes the bot walk them).
- [x] **FUL-10 FORKS** (P10, M — landed on wt-ful10). Seven trade-off field events crossing
      salvage/scar/veteran/faction/heat (ids+arm order frozen for the compass; PendingSalvageReward
      run-committed via AwardMetaRunEnd — events never touch meta directly); two veteran-economy
      contracts (MERCENARY CLAUSE: half-price recalls but no enshrinement; LIVING LEGENDS:
      pensions + double kill credit but a KIA erases the reserve record); the orphaned perk trio
      Vantage/Breaker/Siegebreaker joined real class lines (CONTRACTTEST enumerates the coverage
      rule). Contract enum append moved BOTH tail pins (SaveGame SelfTest + CONTRACTTEST, each
      with a Spearhead-position pin). Bot arm-uptake measurement lands with FUL-5's hashed chooser
      (FUL-10 makes the forks exist; FUL-5 makes the bot walk them). DEVLOG carries the measured
      landing + the accepted IndexForNode version-skew note.
- [x] **FUL-13 TRUE NORTH** (P13, L — LAST; wt-ful13, base c4ef42e). The program close: the
      published numbers made TRUE for the finished game. Intel cash-flow telemetry (61fbccd,
      logic-identity verified — 33/33 base-schema JSON fields); the definitive ladder at proper N
      (200 campaigns, 2 disjoint CRN slot sets/heat): **52.5/35/30/22.5/10** — goal band RE-SET
      to **55/40/30/20/10 ±8** (h8 ±5, floor >=5) with the owner-facing reasoning in DEVLOG (the
      80/70/60/40/20 band predates the exposure repair; un-repairing exposure was out of
      authority). h4 measured ON its re-set band (30.0 vs 30±8) — no rung-average lever; the
      wave's levers went to the measured SHAPE defect: **Defend inverted at the top** (82% h0 →
      97% h6 / 91% h8; defend-pinned h8 96%, n=89, all-Defend completion 80% at a 10% rung).
      R1 waves inherit Heat.StatDelta (7139a2f, truthful-not-binding); R2 defendKeep = graced
      heatEnemy/2 (03f02dc) → pinned h8 Defend **87** = parity with pinned h0's 83, h4 Defend 61
      (FUL-4's band), rung dips in budget (h8 10→5 at the band floor, reported); R3 ceil probe
      REVERTED (no h6 movement, real h4 cost — the h6 residual recorded with mechanism). LOS
      policy-gap thread CLOSED at N=100 pairs: binary −2.0, margin −0.06±0.21, sign-test p=0.87
      — zero, not negative; forgiving-by-design ACCEPTED, sloppy definition unchanged. Intel
      flood RESOLVED no-drain (kicker = 57% of h8 income, ALL converts to shop spend, unspent
      flat 14-19, slope survives). Event EV-weighting (FireWeight) + informant PrepDead gate
      (+8 EVENTTEST legs; h0 A/B byte-identical — binds on future catalogs + human legality).
      Endless depth 32 stands median 6 (APEX band top edge, = FUL-6). m5 all-Defend cell
      resolved 89% (n=9 — the 12.5%/n=8 was noise); per-kit finale drift closed world-driven
      (paired slots: W81/L69/S94, an ordering flip vs FUL-11 = worlds, not kits). "?"-node
      Clamp(1,3) lever measured NEARLY INERT (18/20 slot-pairs byte-identical, zero added event
      volume) — recommendation recorded, NOT applied. RCL kept-as-is (4 procs/200 — bot floor
      understates the human combo line); FDR closed alive (11 procs — FUL-7's drag stage priced
      it). README screenshot retaken (the FULCRUM board). Full tables + the program-close
      write-up: docs/DEVLOG.md §FUL-13.

## PROGRAM "RESONANCE" — WAVE V1 "GROUND AND TYPE" (visual foundation)

- [x] **V1-A — the board got a floor.** `Renderer.DrawBoard`'s floor loop and its grain pass
      both skipped every non-`TileType.Floor` tile, so bare board backing (`Pal.RGBA(7,10,14)`)
      showed under each cover block; with the block inset at 5px that was a hard-black gutter
      ringing all ~45 blocks on every map. Both `continue`s dropped — the ground plane is now
      continuous and cover sits ON it. Verify: `SIGHTLINE_SHOT=90 SIGHTLINE_FORCEBIOME=0..7`.
- [x] **V1-A — real cast shadows.** `Renderer.LightOrigin` / `FloorLight` declared a board key
      light that nothing cast from; cover used a fixed `+3,+4` offset (an emboss — identical in
      every direction). New `Renderer.ShadowVec` returns the per-tile fall direction away from
      the light, and `Renderer.CastShadow` sweeps the block footprint along it (dark at contact,
      feathering to the tip). Length scales high 16 / low 8. Same treatment on the plateau
      front-wall contact shadow. The plateau side wall (flat `Pal.HighSide` = near-black, which
      read as a hole once the floor was continuous) now takes the biome hue + key light.
- [x] **V1-B — two font atlases.** One 64px NotoMono atlas served 11px→92px; the 11–14px body
      text (most of the words in the game) was minified ~5× with bilinear filtering and no mip
      chain. Measured symptom: "WON" in the WAR ROOM hall of fame rendered as "NON". Now a 20px
      UI atlas serves text ≤ `Cfg.UiFontMax` (18px) and 64px serves above it, both with
      `GenTextureMipmaps` + `TextureFilter.Trilinear`; every call site routes through
      `Cfg.Text`/`Cfg.Measure` (`Cfg.FontFor`). Hud.cs 10/11px raised to a 12px floor —
      including the `Clip`/`WrapText`/`WrapLines`/`CenterText` measurement sizes, which would
      otherwise wrap at 11 and draw at 12.
- [x] **V1-B — SHIP BLOCKER: asset paths were cwd-relative.** A published binary launched from
      any directory but its own silently fell back to Raylib's built-in bitmap font and rendered
      every em-dash as `?`. New `Cfg.AssetPath` resolves against `AppContext.BaseDirectory`
      (cwd fallback kept for dev); the font and the latent same-bug audio drop-in paths use it.
      Verified against a real `dotnet publish -r linux-x64 --self-contained -p:PublishSingleFile=true`
      run from a foreign cwd — before: "NotoMono-Regular not found, falling back to default";
      after: all three atlases load by absolute path.
- [x] **V1-C — a display voice.** `assets/ChakraPetch-Bold.ttf` (78,384 bytes) + its licence
      text, handled exactly like NotoMono (csproj `CopyToOutputDirectory`). **SIL Open Font
      License 1.1 — NOT CC0**: zero-cost and zero-royalty, but the licence text must ship with
      the font and the font itself may not be sold. Provenance verified three ways: fetched from
      `google/fonts` `ofl/chakrapetch`, its `METADATA.pb` reads `license: "OFL"`, and the font's
      own name-table IDs 13/14 name the OFL 1.1. Baked at 96px, routed to titles ≥ 24px only via
      `Cfg.TitleText`/`Cfg.TitleMeasure` (wordmark, VICTORY/RUN OVER, WAR ROOM, FIELD MANUAL,
      SKIRMISH, ASSEMBLE STRIKE TEAM, MISSION n COMPLETE, REQUISITION, PROMOTION, SPECIALIZE,
      FIELD DOCTRINE, event titles, PAUSED). Numerals and data stay on NotoMono. Corner brackets
      derive from the measured width, so they re-fit the proportional face automatically.
- [x] **V1-D — shop card title/price collision.** A long title ran straight into its right-
      aligned price ("COUNTER-PREP: SYNDICATE" + "12 INTEL" → `SYNDICATE2 INTEL`). The card now
      reserves the measured price column and shrinks the title 18→14 (new `Hud.FitSize`; `Clip`
      only as the backstop) so the whole name survives. `SIGHTLINE_PREP` now takes a faction
      name (`syndicate`/`legion`/`wardens`) so the longest title can be shot on demand.

**Left for a later wave (deliberately):** board text in `Renderer.cs` still has 10/11px sizes
(the two-atlas fix already sharpens them; bumping tile-constrained labels needs its own layout
pass, and other waves own parts of that file). The biome signature pass stays floor-tile-only,
so its emissive cues do not creep around cover bases.

## OPEN — left by PARALLAX P20 "THE STALE GROUND" (2026-09-04, base `dac9f2f`)

P20 removed a read of the PREVIOUS mission's ground layer from `Mission.Build`
(`Mission.ClearGroundOnBuild`; `SIGHTLINE_STALEGROUND=1` restores it). It closed the
`dailyBoardNonDeterministic` merge blocker — the SEEDED DAILY dealt a different board on its first
build than on its second in 8–15% of processes — and it **moves the terrain**, so it leaves two
things open.

- [ ] **1. RE-MEASURE THE LADDER. L5 is now a pre-P20 ladder.** Priced CRN-paired at two rungs
      (n=320/rung/arm, 8 slot sets, `docs/measurements/p20/`): **25.3% of h0 worlds and 16.6% of h4
      worlds play out differently**, h0 moves +2.2 (**not resolved**) and h4 moves **−3.8, McNemar
      z=−3.00, chunk-paired t(7)=−3.97 with all eight slot sets negative** — a real tightening. The
      pairing machinery is intact (a slot is still a slot; the runner and the heat pin are
      untouched), so this is a re-measure, not a rebuild of the instrument. Six rungs × 16 slot
      sets, the L5 shape, `SIGHTLINE_HEATPIN` default on. **Until it is run, an absolute win rate
      from L5 may not be quoted against one measured on this tree.**
- [x] **2. Nothing else in the codebase reads a board layer before it is written — verify, don't
      assume.** DONE — L6 extended the leg to all eight and found **6 of 8 failing**; P21 closed
      the two it named (see the L6 docket below). The "accounting looks complete" text below is
      kept as the finding it was: it was wrong, and it was wrong in exactly the way it warned.
      **What is true now:** `Mission.Build` clears all eight itself, and MODETEST leg (14a-2)
      asserts it at the Build seam with the detector proven able to fail on each owner separately. P20's leg (14a) pins exactly one statement: `Mission.Build` leaves the ground layer
      empty. The class of defect is wider: a per-mission layer that is stamped AFTER the thing that
      reads it. `Grid` carries seven such arrays (`Tiles`, `Height`, `Smoke`, `CoverHp`,
      `CoverSeed`, `Fire`, `Barrel`, `Ground`); Build wipes Tiles/Height/Smoke, `Grid.ClearHazards`
      wipes Fire/Barrel, `ResetCoverHp` recharges CoverHp at the end, `CoverSeed` is declared
      purely visual — that accounting looks complete, and **"looks complete" is what the ground
      layer looked like for two programs.** The cheap gate is one more assertion in leg (14a): a
      hand-dirtied value in every array, and a statement about which ones Build is allowed to
      carry.

## OPEN — found by PARALLAX P19 "THE ROSTER CONTESTS" and deliberately NOT fixed

**Read the corrected numbers here before acting on the roster docket** — two of its four items were
re-derived to different figures, and one of them would have sent a wave in the wrong direction.

- [ ] **The enemy hover card names the CODEX ENTRY, not the unit.** `src/Hud.cs:2503`:
      `string title = $"{Codex.NameFor(d.Cls)} — {d.Cls}";`. Every named elite and finale boss —
      BREAKER / BULWARK / WARDEN / MARSHAL / SIEGELORD / SPYMASTER — has been captioned
      **"WARLORD — ELITE"** since SIGNAL W5, and P19 made that far more visible by putting a named
      elite on every ELITE node (see `docs/measurements/p19/eliteshot-on.png`). One line:
      `string title = (!string.IsNullOrEmpty(d.Name) && d.Name != Codex.NameFor(d.Cls)) ? $"{d.Name} — {d.Cls}" : $"{Codex.NameFor(d.Cls)} — {d.Cls}";`
      `src/Hud.cs:2375` carries the same expression for the sibling card. P19 left it because
      `src/Hud.cs` belonged to another developer that wave.
- [ ] **The ELITE node's PRICE is still blind to what it fields** (C3's open item, now sharper).
      `Run.ElitePremium` = 14 was set when an ELITE node was "+2 bodies, +1 stat"; it now reliably
      fields the named elite as well. The premium is at least honest for the first time — but it has
      never been priced against the fight, and P19's round measured the node getting materially
      heavier (`byNodeKind` Elite at h8: **38.7% -> 30.1%**, n≈150-166/arm, descriptive not paired).
- [ ] **CORRECTED — the "contest archetypes almost never appear" figure.** The 0.8% / 1.6% body
      rates are real (P19 re-derived **BOMBARD 0.69%**, **WARBRINGER 1.55%** over **30,624 spawns /
      3,796 missions / 960 campaigns**) but they are the WRONG DENOMINATOR: both carry a
      **one-per-mission cap** in `Mission.SpawnEnemies`, so what decides exposure is per-MISSION
      presence — **BOMBARD 5.6% of missions, WARBRINGER 12.5%** (exact, because of the cap), i.e.
      roughly **20%** and **40%** of campaigns at 3.95 missions/campaign. Thin, but not "almost
      never". CUSTODIAN 1.87%/mission-rate 0.151, PIKEMAN 2.02%/0.163, SPOTTER 2.69%/0.217.
      **If a future wave wants these commoner, raise the CAP or the per-mission gate, not the body
      rate**, and price it: P19's own instrument could not resolve either lever it shipped
      (MDE 4.9-8.6 points at n=320/rung/arm), so a rate change needs a bigger round than one wave.
- [ ] **CORRECTED — "two authored arena pairs are ~88% tile-identical".** Naive character agreement
      is meaningless on these templates: they are **85.3% floor on average** (min 69.7%, max 95.5%),
      so two INDEPENDENT layouts agree on ~73% of tiles for free. On Jaccard over NON-FLOOR tiles
      (pairwise mean **16.7%**) there is **exactly ONE** structural near-duplicate,
      **ZIGGURAT / FORGE at 72.7%** — both "commanding raised core" set-pieces. The runner-up is
      ZIGGURAT/STEPWELL at 54.5%, a different arena. The pairs the naive metric flags at 88.4%
      (GARRISON/REFINERY, HOOK/GARRISON, CHASM/GARRISON) share **16.0% / 12.5% / 11.5**% of their
      structure. **De-duplicating one of ZIGGURAT/FORGE is the only defensible arena-diversity item.**
- [ ] **STILL OPEN AND REAL — `Mission.DeckPick` is objective-blind.** `DeckPick(int seed, int
      missionNum)` takes no objective and has no objective-aware caller. An EVAC on a plaza and an
      EVAC whose only open ground is the far corner are the same blind draw. This is a whole wave:
      it needs a per-arena objective-suitability model, and the deck's ZERO-DRAW purity is
      load-bearing for every CRN pairing in the project, so any objective term must be a pure
      derivation off `(seed, mission, objective)` and must be measured (it re-deals arenas, which
      moves every archived world).
- [ ] **REFUTED, for the record — "four archetype pairs are stat twins one planner weight apart".**
      There is **exactly ONE exact statline twin**, SPOTTER/CUSTODIAN (`Smg 5/48/6`), and it is not a
      planner twin (SPOTTER owns a standoff positioning branch at `Ai.cs:858` plus the focus-fire
      grant at `Ai.cs:1555`; CUSTODIAN owns the objective-undo branch at `Ai.cs:406`). The near-pair
      HUNTER/STRIKER differs by HP **and** four planner terms. The roster's AI is well
      differentiated; what was undifferentiated was the NUMBER the player reads, which is what P19
      fixed. Do not re-open this as a planner item.

## OPEN — found by PARALLAX P12 "THE CONFIRMED EIGHT" and deliberately NOT fixed

- [ ] **Delete `Hud.DrawIntro`'s dead caption chain (~35 lines).** It still carries the
      pre-FRONT-DOOR `else if (CheckCollisionPointRec(introMouse, OverlayBtnN)) caption = "…"`
      ladder, and EVERY branch of it is unconditionally overwritten twenty lines later by
      `string hoverId = … g.IntroHit(introMouse); if (hoverId != null) caption = IntroDoorCaption(hoverId);`
      — `IntroHit` resolves any hovered door, so the live string always comes off `IntroDoors`.
      It is not merely dead, it is a **trap**: someone updated the SKIRMISH caption in the dead
      copy (it already read "the opposition") while the live table stayed on the pre-P4 text, which
      is half of P12's C6. Deleting it needs one check — that no branch there says something
      `IntroDoors` does not (the DAILY branch interpolates `g.TodayDailyForceName`, so its caption
      is NOT a constant and must move into the table or stay as a special case).
- [ ] **`HandleOverlayClick`'s `Phase == Phase.Barracks` branch is unreachable** and was carried
      into `Game.ActPrimary` unchanged. The function is dispatched only for Intro / Win / Lose
      (`Game.Update`'s phase switch), so `NextMission()` can never be reached from it. Harmless;
      removing it wants its own pass over who else could ever call `ActPrimary`.
- [ ] **The pause card's footer line is still keyed on `CardInFight`, not `CardCanAbandon`.** In a
      mid-stand LAST STAND barracks the card now titles itself PAUSED with a RESUME row, while the
      footer reads "Every change is saved as you make it - [Esc] back". Both sentences are true
      (Esc does go back to the offer screen) and the alternative — the camera legend — would be
      wrong there, so P12 scoped the change deliberately. A third footer string for that one cell
      would close it.
- [ ] **`Game.KeyPin` covers three read sites, not the keymap.** P12 added it for two defects that
      are inexpressible without a key press (a global colliding with a per-screen handler; a key
      loop above a drag guard). A general injection layer over every `Raylib.IsKeyPressed` in
      `src/` would make the whole keymap testable — and would have caught the C3 class by
      construction — but it is a large mechanical diff and belongs in its own wave.
- [ ] **Nothing bounds a floating text's RISE.** `Fx.TextTopY` bounds where a text is BORN (P12 C7);
      `Fx.Update`'s `t.Pos.Y -= t.Rise * dt` still carries it off the top of the window as it dies,
      at an alpha heading for zero. Left alone on purpose: pinning risen text to the ceiling would
      read as a pile-up. If a future wave wants it, the bound belongs in `Fx.Update`, not the
      ladder.

## OPEN / NEXT (post-FULCRUM backlog — seeded at the FUL-13 close)

Reference for any future wave: the FUL-13 ladder + re-set goal band (docs/DEVLOG.md §FUL-13)
is the number of record; method per FUL-2/FUL-5 — CRN chunks via SIGHTLINE_BALANCE_BASE slot
sets, one lever per measured round, fresh same-slot R0 first, dip budgets, breaches reported.

> **Ladder update (RESONANCE X1 "THE EXCHANGE", docs/DEVLOG.md §X1).** The numbers of record
> for h0/h4/h8 are now the X1 shipped rungs — **h0 52.5% / h4 27.5% / h8 15.0%** (n=40
> campaigns each, CRN slot sets 0-19 x greedy+sloppy), all inside the FUL-13 band. X1's own
> pre-lever baseline re-measured h0 at 52.5% (FUL-13's number to the decimal), h4 at 22.5%
> and h8 at 10.0%. **h2 and h6 were not re-measured** — X1 moved the top rungs UP, so those
> two are the first to check on any re-baseline.

- [ ] **X1 residual — Escort at heat 8 runs 15.61 turns** (n=17; h0 6.00t and h4 6.01t both
      IMPROVED vs baseline, so this is an apex-only drag). Two candidates in priority order,
      with the measured caveats, in DEVLOG §X1 "THE ONE HONEST BREACH": (1) `SmartEscort`'s
      downed-squad hole — the lone-VIP self-race tests `p.Alive` but a DOWNED soldier is still
      Alive, so the asset hunkers while the squad bleeds out (an INSTRUMENT fix: needs its own
      paired re-measure); (2) the cold-LZ beacon gate (`Game.EscortBeaconOk`, Chebyshev 3) —
      but BEACON plant counts were UNCHANGED between X1's R0 and R2, so it was not the binding
      constraint at h0. Evac's pooled 11.32t rests on n=9 and is not yet a finding.
- [ ] **X1 residual — meaningful-choices/turn is a CONTACT-DENSITY metric, not a lethality
      one.** X1 added the shot-gate decomposition (`[shot-gate]` report line +
      `decisionRichness.{actingSoldiersPerTurn,armedSoldiersPerTurn,turnsWithAShotPct,
      choicesPerArmedSoldierTurn}`) and it shows the binding constraint is
      choices/ARMED-soldier-turn ~1.5: the typical armed soldier sees exactly ONE worthwhile
      target. Compare rungs — h4 out-scores h0 (2.90 vs 2.33) purely because heat fields MORE
      bodies. **Chase the 3-5 band with simultaneous-target geometry** (pod placement, arena
      sightlines, activation overlap), and always quote choices/ARMED alongside the per-turn
      average so a turn-count change is never mistaken for a decision-quality change.

- [ ] **Owner decisions pending** (decision paragraphs with recommendations in DEVLOG §FUL-13
      "DESIGN-QUESTION DOCKET"): ~~skirmish numeric heat~~ **DECIDED + SHIPPED by W9 THE REPAIR**
      (the m1 grace is gated on `Mode != GameMode.Skirmish`, which covers DAILY too — a skirmish
      player explicitly dialled the rung, so there is no green squad to protect; CAMPAIGN and
      ENDLESS keep the grace byte-for-byte. The dial had been adding ZERO bodies/stats/damage in
      two of the four shipped modes); founding-squad corpsman (recommend: first-backfill guarantee
      or keep-as-is — a founding-four identity choice, not a tune); grenade pre-frag bot arm
      (recommend: accept the human-vs-bot read gap as designed skill expression).
- [ ] **RE-MEASURE THE LADDER — W9 THE REPAIR changed RNG DRAW ORDER.** Three defect fixes move
      the draw sequence (the grapple no longer env-damages its own grappler and so no longer draws
      its FX; a downed unit's queued shot no longer rolls; the autopilot returns after a GRAPPLE
      and takes an extra `Util.Roll(45)` on the next step), and two more change composition without
      changing draw order (the skirmish/daily heat gate, the post-event `AutoDeploy`). **Every
      ladder figure published before W9 is therefore void against this tree.** W9 deliberately did
      NOT price them — a batch would only confirm the numbers moved. Note the frame cap also moved
      20000 -> 120000, which REMOVES the right-censoring that scored the longest campaigns as
      losses (the archived x2 chunks log `frame-cap hits: 1`), so the new baseline may read
      slightly higher for that reason alone.
- [ ] **`Mission.OpenerTrim` still trims a SKIRMISH / DAILY force** (one body off any `n <= 1`
      force). Uniform across every heat rung, so it does not flatten the dial W9 restored and it is
      not a defect — but a skirmish is one body lighter than a campaign mission 1 with the same
      parameters. Changing it is a balance lever, not a repair; decide it with a measured round.
- [ ] **The tooltip's UNBADGED modifiers.** W9 fixed the three badges that LIED; these are
      OMISSIONS — Siegebreaker, Bipod, the defender's CoolHeaded, Routed, Vantage/Breaker/Guardian
      crit, the Marksmen/Fervor/Executioners boons, PressureAim and the faction aim rules all move
      the hit%/crit with no badge. `Hud.DrawTooltip`'s own header claims the panel surfaces EVERY
      modifier, so either the badges or the comment is still over-claiming.
- [ ] **CODEX footer drift** — its legend reads "Up/Down select · Wheel scroll" while keyboard
      scrolling is bound to Left/Right and A/D (`Game.Codex.cs:80-81`). Same class as the SKIRMISH
      "+/- heat" legend W9 fixed by binding the keys; do the same here or reword.
- [ ] **The h6 Defend residual** (the one recorded bump after the FUL-13 rounds: pinned h6
      Defend 97% n=89 while pinned h8 sits at 87 parity). Mechanism named in Game.cs at the
      defendKeep line: at +2 stats extra bodies feed the rout economy instead of pressuring
      the hold. Any future lever should be stat- or cadence-flavoured, not bodies.
- [ ] **Escort at the apex** (h8 29%, n=17 — the wall's killer cell; h6 67%). Allowed today as
      apex texture; if the owner wants the h8 objective spread tightened, start from the FUL-13
      per-objective table and the VIP-durability lever, not blanket rung stats.
- [ ] **Event exposure** (informant/reservecall fielded ZERO times in 200 campaigns; each event
      ~1-in-9 runs at 17 entries). The parked Clamp(1,3) lever is measured nearly inert (FUL-13)
      — the honest levers are floor-2 stamping (Clamp(mids/4,2,3); reshapes in-flight saves'
      unvisited "?" nodes — adjudicate the skew) or a cross-run catalog dedupe (profile-side,
      no skew). The reservecall value prior is rank-blind (noted in DEVLOG) — revisit only with
      real exposure.
- [ ] **RCL sweeten option** (only if the owner wants the boon mainstream): "any overwatch kill
      re-arms, once/turn" — the cone-kill proc is an honest but thin combo line (4 procs/200
      campaigns at FOCUS 843); measure against the FUL-13 procs table.
- [ ] **h8 corpsman blackout** (RELENTLESS kills backfill → corpsman fielded 13% of h8 missions,
      PATCH 5/batch): intended apex cruelty or a hole in the revive economy — pairs with the
      founding-corpsman decision.
- [x] **RESONANCE T2 — "READ THE DANGER" (incoming-fire forecast).** The defensive read was a
      single bool (`ComputeThreat`: some enemy has LoS AND cover==0) drawn as one identical tick,
      so a tile enfiladed by four guns but covered from one read completely clean. Now a per-tile
      `ThreatCell` grid (gun count / best enemy hit% / expected post-armor damage / flanked /
      overwatch-lane), derived from `Combat.ComputeOdds` with the mover placed on the candidate
      tile so it can never disagree with the shot that fires. Surfaced as a graded danger meter
      (bar COUNT = guns, shape-redundant + CB-safe; intensity = heat), an INCOMING FIRE hover card,
      and a worst-tier-tinted move-path preview. Pause toggle is now OFF/SIMPLE/FULL (SIMPLE = the
      pre-T2 read). Signature-cached — rebuilds on change, not per frame. Read-side only: the
      flywheel cannot see it and no win-rate claim is made. Details + perf + squint verdict in
      DEVLOG §RESONANCE T2.
- [ ] **On-device audio tuning** (carried; needs the human).

---

## PROGRAM "RESONANCE" — landed waves (see docs/DEVLOG.md for the write-ups)

- [x] **W9 — THE REPAIR.** DONE. 15 of 15 assigned defects reproduced and fixed, plus one hard
      autoplay HANG found while calibrating the TIMEOUT fix that the brief did not have. Write-up
      in docs/DEVLOG.md §W9. Every fix ships a test that FAILS on the pre-fix tree (proven by
      reintroducing each defect alone and recording the failure tag).
      - **The crashes.** `meta.json` — the file holding ALL permanent progress — had no analogue of
        the "parses fine but is unusable is corruption too" guard `save.json` has had since D2, so
        `{"Veterans":[null]}`, a veteran with `Cls:null`, and `{"Legends":[null]}` each killed NEW
        CAMPAIGN or the WAR ROOM with an unhandled exception, no stash, no recovery. Sanitised at
        the single choke point (`SaveGame.LoadMetaDto`). `MetaDto.SchemaVersion` is now READ, with
        an explicit FORWARD-TOLERANT policy (the opposite of the run save's, on purpose: refusing a
        newer profile would blank a career) that copies the file to `.bak` before the first
        write-back can drop fields it cannot see.
      - **The displayed number.** The tooltip's DMG row printed the RAW weapon band (3-5 for a shot
        dealing 1-2 to a guarded HVT, with its own GRAZE 1 row disagreeing beneath it); the LOCK-ON
        badge was a second, four-waves-stale copy of a rule UNDERTOW W5 changed; and
        `Combat.ComputeOdds` was NOT side-effect free — hovering the guarded HVT popped "GUARDED"
        ~60x/second. `ShotOdds` gains `DmgMinEff/DmgMaxEff` (the raw band stays raw for
        `ExpectedDamage`), `Combat.LockOnAim` is now the single source of truth for both the math
        and the badge, and `HardenedReduce` takes `telegraph`, false at every read site.
      - **Verbs and flow.** GRAPPLE self-rammed the grappler on every adjacent target — 100% of a
        JUGGERNAUT's grapples — so `ShoveAnim` no longer rams its own initiator (the adjacent case
        is a deliberate SLAM; rejecting the target instead would have left the fork with no legal
        grapple). A soldier downed mid-queue still fired its own queued shot: the purge gains the
        shooter clause, `ShotAnim` self-cancels, and the autopilot stops queueing a second action
        behind an unstarted anim.
      - **The no-TIMEOUT contract, made TRUE.** The stall cap was per-MISSION and re-armed by the
        checkpoint redeploy while the harness budget is a whole-campaign frame count; and a
        WITHIN-TURN deadlock was invisible to it entirely (a DISORIENTED soldier hit
        `DoOverwatch(); return;` in AutoStep's DEFEND branch and spent nothing — 38,000 frozen
        frames on seed 3001). Run-scoped `RunTurns` + `AutoMaxRunTurns` + a within-turn idle guard,
        caps recalibrated from a measured census, and **qa-sweep.sh now EXITS NON-ZERO** on a
        TIMEOUT instead of printing it for a reader to notice.
      - **Modes + economy.** SKIRMISH/DAILY heat was numerically inert (see the decided owner item
        above); a field event's recruit fielded cap+1 and its release stranded a healthy benched
        soldier at cap-1 (one `_run.AutoDeploy()`); the SKIRMISH legend's "+/- heat" keys are now
        bound.
      - **WAR ROOM.** STANDING RESERVE was invisible AND unbuyable on a fresh profile (the overflow
        `break` fired before the buy-rect was published). `Hud.WarUnlockPlan` sizes rows to the room
        instead of truncating the catalogue, so every unowned unlock always gets a card and a rect.
      - **New hooks:** `SIGHTLINE_TRUTHTEST`, `SIGHTLINE_GRAPPLETEST` (the first coverage GRAPPLE
        has ever had), `SIGHTLINE_STALLTEST`; plus new legs in SAVETEST, COMBATTEST, DKTEST,
        DOWNTEST, BENCHTEST, MODETEST and METATEST. All wired into `scripts/qa-sweep.sh`.

- [x] **P1 — PRESENTATION.** DONE. The post chain got the cheapest visual headroom left in the
      project, and the strategic layer stopped looking like a spreadsheet.
      - **Bloom is two-pass and half-res.** A bright-extract + 4-tap box downsample to 640x400,
        then a separable gaussian H and V — **~5.6M texel fetches against the old single-pass
        12-tap-at-full-res ~12.3M**, with the outer tap reaching **~10.3px** instead of 5px.
        The bright-pass **knee is UNCHANGED at 0.36** and is still applied PER TAP before the box
        average, so V3's 1px cover rims still cross it. Measured (seed 7, post-FX on, resting):
        board median **59 -> 59**, p95 **114 -> 117**, max **255 -> 254**, and the >180 band V3
        reserves for unit rings **1.05% -> 1.19%**. Nothing blew out; the old 5px ring's hard
        halo edge is gone. Amount retuned `0.5+1.7*b` -> `1.45+4.30*b` (three settings measured).
      - **An ACES shoulder, not a full-range ACES.** Full-range Narkowicz maps the board median
        0.26 -> **0.39** and white -> **0.80** against display-referred input — it would undo V2's
        re-grade. Shipped: the same curve blended only over the **0.85..1.60** luma band, so
        everything at or below 0.70 luma is bit-identical and only blown bloom cores get a shoulder.
      - **Film grain + scan.** A 256px `GenImageWhiteNoise` tile made at init (**zero committed
        bytes**), alpha 0.025, faded out below 0.30 luma; a 3px-period scan at 0.028. Both driven
        by the existing `uTime` accumulator — **no new `Raylib.GetTime()` read** (count still 59).
        All of it stays behind `Display.Enabled`: a plain `SIGHTLINE_SHOT` loads **one** shader
        program (raylib's default) where the P1 chain would add three.
      - **Campaign map.** Seeded contour terrain + per-region column bands (`MapHash`, pure
        arithmetic off `MapSeed` — no alloc, no RNG draw), casing-plus-core route strokes with a
        direction chevron on live edges, and `DrawNodeIcon` geometry replacing the single letters
        (crosshair / depot cross / warning delta / choice fork / boss diamond). The legend draws
        the real markers now; `NodeGlyph` is deleted.
      - **WAR ROOM.** A full-width **CAREER** footer of 7 stat cells closes the L-shaped void and
        grounds the three content-sized columns; BACK sits under it instead of floating in it.
      - **Victory card.** Five accent colours -> two (neutral + the card accent on one headline
        slab). DIFFICULTY stops reading as a warning and gains a filled/hollow rung-pip strip.
      - **Event card.** Sized to content (~82px of dead slab removed) and every option telegraphs
        its risk three ways — rail, drawn mark, word — derived in Hud from outcomes that already
        exist, so `Events.cs` is untouched.
      - **Shop / armory icons.** Geometry transcribed into the existing `DrawActionIcon`
        primitives; the slate card widens 760 -> 808 so the text column keeps EXACTLY its prior
        width (366-28-24 == 342-28), verified against a base capture at 120%.
      - Verified: Release **0 warn / 0 err**, `qa-sweep --full` **45 self-tests, 0 FAIL** (PAIRTEST
        PASS, no COVERAGE GAP), autoplay x5 clean, `SIGHTLINE_BALANCE=10` **runs=20 missions=69**
        **byte-identical to base `764055a`** (0 diff lines after stripping the wall-clock stamps and the worktree path) — the proof this wave is presentation-only. `SIGHTLINE_CB=1` and `SIGHTLINE_UISCALE=3` passes read on every
        touched screen. Write-up: DEVLOG §RESONANCE P1.

- [x] **V3 — SURFACES.** DONE. Cover became a material, biomes became places, and the
      unit tier finally moved into the band the V2 grade reserves for it.
      - **Cover joins its biome.** The tint pull on cover was 0.28 over a strongly slate
        base, so measured (analytic, exact from the colour math) six of eight biomes' cover
        tops sat at hue 190-235 — blue — regardless of the room: ARID cover was hue **204
        at saturation 0.03** (a grey block in a sand room) and MAGMA's was hue 320 at 0.05.
        Pull to **0.55**: ARID cover moves **179 degrees** off slate, MAGMA **158**,
        VERDANT to 156 (green), VOID to 248 (violet). Cover-top hue spread across the eight
        biomes **130 -> 178 degrees**.
      - **Cover became a material.** A per-biome `GenImageCellular` field (256², biome-sized
        cells, CPU-baked, **zero committed bytes**) is inverted at bake and drawn through a
        light biome stone, so the cell faces lift and the seams stay — concrete slabs, ice
        plates, cracked basalt, gravel. Plus purely-visual footprint jitter (+/-3px), a
        hash-picked corner radius (0.12-0.32) and a chipped corner on ~35% of tops. Cover-top
        interior luma std **3.5 -> 5.1-5.5** on the small-cell biomes. `Util.TileRect` and
        every tile-centre consumer are untouched — the jitter is a local copy of the rect.
      - **Biomes became places.** `DrawBiomeFeatures`: 6-12 **board-scale** features per
        mission (fissure + pool, frost drift, soot fan, dune ridge, lattice trunk, moss
        patch, plate seam) drawn *across* tiles under the terrain, all derived from
        `Run.MapSeed` via `Util.Hash3` — no `Random`, **zero draws from `Util.Rng`**, built
        once per (seed, biome, grid) into fixed static buffers (no per-frame allocation).
        MAGMA's per-tile squiggle drops 40% -> 16% of tiles now that structure carries it.
      - **The colorblind collision.** V2 caught MAGMA's per-tile veins landing on the CB foe
        orange. In `SIGHTLINE_CB` the fissure now gives up saturated warmth and works in
        value (dark crevasse, pale hot core). Terrain wearing the CB-foe hue band on MAGMA:
        **0.83% -> 0.61%** of board pixels.
      - **Silhouettes.** A **team chassis carried by topology, not hue**: player = a closed,
        doubled ring; enemy = a broken ring notched in three places (survives greyscale and
        `SIGHTLINE_CB`). GRUNT / SCOUT / HUNTER re-cut as **solid wedge / hollow wedge /
        twin chevrons** — they were the same wedge 2px apart. A dark keyline contour on every
        unit and a white specular catch. Dormant contacts lifted (pale slate body, dark
        backing arc under each dash) — they were near-invisible on several biomes.
      - **The grade: only the unit tier moved.** Board **median and p95 held at base**
        (medians identical; p95 within +/-2 across all eight biomes) while pixels above
        luma 180 went **0.06-0.15% -> 0.49-0.66%**. The V2 blocker is cleared and measured:
        unit ring stroke **179-182**, specular **215**, against a cover top face whose
        worst possible pixel (high cover, cell face, directly under the key light, plus
        grain) is **~149** — below a soldier's body fill (153). Cover tops are now
        **value-targeted** (`Renderer.LiftTo`) so all eight biomes sit on the same rung;
        their luma spread went **12 -> 0**. The p95=150 target is still unmet and was NOT
        chased — see DEVLOG §RESONANCE V3.
      - Verified: Release **0 warn / 0 err**, **41/41** self-tests incl. PAIRTEST, autoplay
        x5 clean, `SIGHTLINE_BALANCE=10` **byte-identical to base** (runs=20, missions=76,
        0 diff lines). V2's hue-convergence table re-run: no regression (ASH dHue 30 -> 24,
        cyan% within 1.5pp everywhere). Write-up: DEVLOG §RESONANCE V3.

- [x] **V2 — LIGHT ON THE BOARD.** DONE. Two measured problems, one of them tactical.
      - **The move overlay stopped repainting the room.** `Renderer.DrawMoveOverlay` filled
        every reachable tile at a=60 and every dash tile at a=55 — 60-120 tiles of flat
        cyan/gold for the whole player turn. Measured (chroma-weighted circular mean board
        hue, overlaid third vs clean third, 8 biomes, seed 4242) it dragged ASH **172
        degrees**, ARID 68 and MAGMA 29 off their own hue and put the cool biomes at 75-79%
        cyan; and **dash-gold sat at the hue and value of a warm-biome plateau top**, so
        ASH/ARID could not distinguish dash range from high ground. Replaced with a boundary
        treatment: a marching-squares outline (**solid** walk / **dashed** dash — shape, so
        it survives `SIGHTLINE_CB`), a corner-tick lattice for per-tile granularity, and a
        whisper-level **white** inner lift (white preserves hue exactly; an a=22 *cyan* tint
        still flipped near-neutral ASH by 170 degrees). Mean dHue **44.3 -> 7.3** against a
        no-overlay floor of 4.3; mean median-hue delta **73.1 -> 1.9** against a floor of 1.9.
      - **The re-grade.** 95% of board pixels sat in the bottom 40% of the range. One
        coordinated pass: floor mean un-darkened (0.16 -> 0.06 + a split tint/value lift),
        key light widened x0.16/x0.34 -> **x0.32/x0.45**, cover tops +16 / walls -8, cover
        rim raised, and a new per-biome AO vignette on the board rect (drawn *under* terrain
        so it never dims a soldier). Board mean **median 75.2 -> 66.6, p95 95.3 -> 114.1**,
        p50->p95 span 20 -> 47.5, >180 unchanged at 0.11%. The p95 **target of 150 was
        missed** — measured, friendly unit bodies peak at ~150, so 5% of pixels above 150
        has nowhere to live that is not a soldier. Reaching it needs the UNIT tier raised
        into the >180 band first; deliberately not done here (see DEVLOG §RESONANCE V2).
      - **Elevation.** Plateau top +30 -> **+64**, front wall -12, lit lip a0.50 -> a0.72.
        High ground was a ~10-luma bump under a gold wash; verified against a stashed base
        build (`SIGHTLINE_ELEV`) it is now an unmistakable raised slab.
      - Tooling: **`scripts/board-metrics.py`** (manual hue/luma measurement over the board
        rect minus HUD overlap) and the **`SIGHTLINE_NOMOVE=1`** ground-truth capture hook.
      - Verified: Release 0/0, **41/41 self-tests**, PAIRTEST PASS, autoplay x5 clean,
        `SIGHTLINE_BALANCE=10` byte-identical to base. Write-up: DEVLOG §RESONANCE V2.
- [x] **X1 — THE EXCHANGE.** DONE (partial, honestly reported). The fight was over before it
      became tactical: a soldier's shot averaged 5.1 damage into an ~8 HP body, so time-to-kill
      was one hit and a match tipped 0.60 times. Two constants in `Mission.MakeHostile` (the
      single hostile funnel) now carry the trade: **`HostileToughness = 3`** (flat HP surcharge
      on every hostile) and **`HostileDamageTrim = 1`** (flat points off both ends of every
      hostile weapon's band, via the new `Weapon.TrimBaseDamage`, which moves the PRISTINE base
      so `ApplyMods` can never resurrect it). Six measured rounds, one lever each, n=40
      campaigns per row. **Landed:** Eliminate 3.60 -> 5.30 turns (into the 5-7 budget),
      shots-per-kill 2.34 -> 3.17, lead-swings 0.59 -> 0.80, Eliminate stopped being a 95% free
      square (-> 81%), Defend did not grow (8.70 -> 8.78), and all three measured rungs stayed
      inside the FUL-13 band (h0 52.5 flat, h4 22.5 -> 27.5, h8 10.0 -> 15.0). **Missed:**
      lead-swings < 1.00, and meaningful-choices/turn fell 2.33 -> 2.09 — the wave's own new
      shot-gate decomposition shows why, and it is a metric finding, not a lever failure (see
      the OPEN/NEXT residual above). **Reverted:** a −2 damage trim (bought ~nothing, worst
      Escort drag of the wave) and toughness +4 (−30 completion; the symmetry warning, measured).
      **Breach recorded:** Escort at heat 8 runs 15.61 turns.

- [x] **T1 — BASIC TRAINING.** DONE. Onboarding stopped being a doc claim.
      `docs/DESIGN.md` §4 graded onboarding "Addressed (W11)"; what shipped was a
      5-card mission-1 callout strip teaching **3 of ~14 verbs** while the action bar
      showed **twelve** (FUL-12 dimmed the other eleven — dimming is not staging), plus
      a **six-bullet rules wall** on the intro. T1 ships:
      - **TRAINING OP** (`GameMode.Training`, intro key **N**, in-drill **[P]** restarts):
        a fixed, scripted, non-persistent drill on its own authored arena
        (`Maps.TrainingArena` + `Mission.BuildTraining`) — **deliberately NOT appended to
        `Maps.Layouts`**, because that array's length feeds the daily's arena derivation and
        the per-run no-repeat deck (appending would have moved the whole measured campaign).
        Eight well-ordered problems (`Game.TrainLessons`): MOVE → COVER → FLANK → FIRE →
        OVERWATCH → GRENADE → ABILITY → CLEAR, each with a turn-budget patience fallback.
        Two 12-HP recruits vs four dormant aim-45 targets = low-cost failure. Biome pinned
        to STEEL so the teaching frame is fixed.
      - **STAGED VERBS** (`Game.OnboardingActive` / `VerbStagingActive` / `VerbRevealed`,
        applied at the end of `Hud.DrawActionButtons`): during the drill and mission 1 the
        bar shows only what has been taught. Permanent **SHOW ALL** escape (**[V]**,
        persisted). Capped to those two places; STABILIZE is never staged away.
      - **JUST-IN-TIME FIELD TIPS** (`Game.FieldTips`): FUL-12's single BRACE tip became a
        10-tip table, each fired once per profile the first time its precondition is true in
        play, priority-ordered. Seen-flags are a `Display.TipsSeen` bitmask; the old
        `BraceTipSeen` bool migrates into bit 0 (bridge verified in both directions).
      - The intro's rules wall is now **one line**.
      - Hook: **`SIGHTLINE_TUTTEST`** (arena/build, every lesson trigger reachable + fires
        once + patience, staging monotonic/capped/escapable, tip bits+prios+reachability,
        seen-flag round-trip + migration, and the drill's no-save/no-meta write contract).
        Screenshot hooks: `SIGHTLINE_TRAINING` / `TRAINLESSON` / `SHOWALL` / `TIP`.
      - **Left for a later wave** (deliberately, not forgotten): the drill teaches nothing
        about the strategic layer (barracks, perks, the campaign map, requisition) — it is a
        tactics drill only; there is no in-drill "replay this lesson" control beyond the
        whole-drill restart; and the tips never fire *during* the drill by construction (the
        lesson card owns the slot), so a player who only ever plays the drill meets 8 verbs,
        not 18.
- [x] **Q1 "NO TWO IN ONE PLACE"** (RESONANCE defect wave, `wt-q1`). Two living units could
      share a tile (the buried one unhoverable/untargetable, since `UnitAt` returns the first
      match): `Game.ActivatePod` planned every dormant pod member against one board snapshot
      before any executed. Fixed with a claim set threaded into `Ai.Plan`; measured 0.655% of
      move steps -> 0.000% over 135 missions, 108 overlap episodes -> 0. New permanent guard
      `SIGHTLINE_STACKTEST=1` (`=2` wide). Also: the STEADYING streak bonus folded into
      `Combat.ComputeOdds` so the displayed HIT% is the rolled probability (was under-reporting
      by up to 12 pts), and the `RUSHED 2ND SHOT` badge ungated from aim mode. Details in
      DEVLOG §RESONANCE Q1.
### PROGRAM RESONANCE — F1 "FOUNDATIONS" (done 2026-08-28, details in DEVLOG §F1)

- [x] Golden-fingerprint append-only enum guard (13 enums incl. the previously unguarded
      `RewardKind`); a mid-enum insertion now fails SAVETEST instead of passing silently.
- [x] `SchemaVersion` migration hook on `RunDto` + `MetaDto`, stamped and asserted.
- [x] D2 — a structurally-valid-but-unusable save no longer leaves a permanently dead
      CONTINUE button; D5 — persisted enum ordinals are validated on read.
- [x] `PublishTrimmed` no longer silently destroys all persistence (source-generated JSON
      contexts); a 25 MB distributable that verifies itself (`scripts/publish.sh`).
- [x] Assets resolve against the executable dir, so a published build works from any CWD.
- [x] `THIRD-PARTY-NOTICES.txt` + `docs/DISTRIBUTION.md`.
- [x] `qa-sweep.sh` runs all 41 self-tests (was 35); CLAUDE.md's false byte-stability and
      stale autoplay claims corrected against measurement.

- [ ] **Root `LICENSE` — OPEN OWNER DECISION.** Deliberately not invented by F1. Options,
      trade-offs and a recommendation are in `docs/DISTRIBUTION.md` §4; the status quo
      (unlicensed private repo = all rights reserved) is safe and blocks nothing until a
      build goes to someone outside the project.

### PROGRAM RESONANCE — C1 "VOICE" (done 2026-08-28, details in DEVLOG §C1)

- [x] **`docs/DESIGN.md` §1.1 AMENDMENT — the light frame.** Narrative was listed as a
      deliberately-unpursued aesthetic; the project owner granted this program permission to
      relax documented constraints, so the change is **recorded**, with its limits, rather than
      allowed to drift. Read §1.1 before adding any word to the game.
- [x] Mission **briefings** (3 lines/campaign node: region × arena × faction × objective),
      skippable, never hit-tested, yielding absolutely to the T1 teaching cards, and clearing
      itself the instant the combat log has an entry.
- [x] **Faction dossiers** (codex FACTIONS tab, each FIELD RULE line interpolating the real
      `Combat` constant) + **named regions** on the campaign map (64 biome-true names off `MapSeed`).
- [x] **Soldier barks** at six beats with four hard rate limits, tagged `VOICE` in the combat log.
- [x] **Run epilogue** — five lines on the campaign end card, off the card's own telemetry.
- [x] `SIGHTLINE_VOICETEST=1` (43rd self-test, wired into `scripts/qa-sweep.sh`; the sweep's
      footer count was also off by one before this wave and is corrected): RNG-separation
      proof with a sensitivity probe, template-completeness, bark reachability + all four gates,
      and a pixel-width fit check for every generated line. `SIGHTLINE_BALANCE=10` byte-identical.

- [ ] **Widen the bark pools.** Three variants per beat is thin; the test that measures fit and
      slot-safety already exists, so this is pure content work.
- [ ] **Briefing opposition line reads as a template by the fourth run** — three of the four are
      structurally identical ("X ground: a, b, c. <rule>."). Worth a rewrite pass, not a rewrite.
- [ ] **No briefing in SKIRMISH / DAILY / LAST STAND** (no `MapSeed` route, no operation number).
      A one-line mode-appropriate variant is cheap if the owner wants it.
- [ ] **Region names are decoration.** Nothing keys off them — no per-region modifier, no return
      visits. Deliberate scope for a *frame*; a future wave could make them mechanical.

### PROGRAM RESONANCE — W4 "THE SECOND AXIS" (done 2026-08-28, details in DEVLOG §W4)

- [x] **The opening geometry is a variable.** `Mission` now deals one of four deployment
      SHAPES per mission — FRONTAL (today's left-to-right push), PINCER (front + both flanks),
      CROSSFIRE (two dense NE/SE masses) and ENVELOP (squad at board centre, pods on every
      rim, the surrounded opening). Derived PURELY from `(MapSeed, mission)` by FNV-1a with
      **zero `Util.Rng` draws**, so every CRN pairing in the project survives; PAIRTEST is green
      with the whole surface on. ENVELOP is objective-gated to Eliminate / Decapitate / Defend,
      so no extraction, hack, beacon or sabotage routing changed.
- [x] **Pod uniformity** — a pod fields one kind of body. Measured exactly ladder-neutral
      (32.5% = 32.5%, n=40) for the wave's biggest single gain on the "which target?" axis.
- [x] **The `SmartEscort` downed-soldier instrument fix** (X1's hand-off), measured as its own
      CRN-paired round with `SIGHTLINE_ESCORTFIX=0` reproducing the broken instrument.
- [x] **New instrumentation** — the `[choice-split]` decomposition (`los-targets` /
      `target-choices` / `position-choices` per ARMED soldier-turn) and a per-shape
      `DEPLOYMENT GEOMETRY` report/JSON block. `SIGHTLINE_EXPOSURETEST` extended to a third
      exposure axis (shape x arena x objective over 4000 seeds) with purity, determinism and
      ENVELOP-legality assertions.

- [ ] **THE LADDER HAS DRIFTED BELOW ITS BAND AND NEEDS A WAVE.** W4's fresh baseline on the
      integration tip measured h0 **32.5%** and h4 **12.5%** run completion (n=40 each) against
      the FUL-13 band 55±8 / 30±8. X1 shipped 52.5 / 27.5. Nothing in W4 caused it — it was
      true before the first lever — but it is now the biggest open number in the project.
- [ ] **`choices/ARMED-soldier-turn` needs a POSITIONING lever, not another threat lever.**
      W4 measured it as a near-invariant at ~1.6 across five structurally different levers,
      because `CountMeaningfulChoices`' two halves respond to threat with opposite signs
      (DEVLOG §W4). The two honest routes: a terrain-grammar pass that adds equally-good
      destinations at constant threat (more LOW cover, which also does not block sightlines),
      or re-specifying axis (b) with an additive rather than multiplicative band.
      **W1 UPDATE — re-argue this against `shotGap` first.** The new decile histogram measures
      the same population continuously instead of through a 12% threshold, and it comes back
      strongly BIMODAL (POOLED n=2412 armed soldier-turns over three h0 chunks: 28.4% in decile 0,
      35.2% in decile 9, a thin middle; each chunk shows the same shape). W4's ~1.6 may be the
      mean of a bimodal population — the statistic least informative about one. DEVLOG §W1.

### RESONANCE W1 "TRUE INSTRUMENT" — opened by the instrument, not yet spent

- [ ] **The route the flywheel walks is not a fair sample of the campaign map.** `ROUTETEST`:
      over 400 maps / 678 real (k≥2) branch choices the shipped `first` policy takes branch 0
      **81.4%** of the time where a fair deal takes it 45.1%, playing **48% more "?" beats and
      15% fewer ELITE fights**. `SIGHTLINE_ROUTE=hash` is built, proven draw-free and
      near-uniform, and **shipped OFF** — switching it is a balance decision and a second
      archive invalidation. One flag, one paired round.
- [ ] **What ELITE nodes actually cost is UNMEASURED — do not read the W1 chunks as an answer.**
      `byNodeKind` pooled over W1's three instrumented chunks: Elite 79.3% (n=29) vs Combat 89.2%
      (n=74), but the three Elite cells range over 35 points (57.1 / 80.0 / 91.7 at n=7/10/12) and
      the chunks straddle the W1/3 CRN break, so they are not strictly poolable. n=29 carries
      ~±7.5 points. A first draft of the W1 write-up quoted the last chunk alone and concluded
      "ELITE measures identical to Combat"; that was cherry-picked and is withdrawn. The open
      question stands — ELITE pays an intel premium AND a bonus perk, so it should read HARDER —
      but it needs a real n on one side of the break.
- [ ] **`runWinRateExStalemate` fired on its first archive and needs a rung sweep.** `R0diag-h0-b0`
      (h0, n=20) reads runWinRate 65.0 vs exStalemate **68.4** — one STALEMATE in twenty, i.e. a
      3.4-point correction at the EASIEST rung, where a stalled autopilot should be rarest. One run
      is not a rate. Whether the harness is quietly scoring its own stalls as campaign losses at
      h6/h8 is **unmeasured**, and a 40-campaign rung can carry two of these.
- [ ] **Make the draw-side RNG counter a permanent tool.** The W1 review found the `Bob` draw by
      swapping `Util.Rng` for a counting property with `StackTrace` capture and running one
      campaign — about ten lines, and it located a defect two rounds of review had missed. W1
      shipped the *assertion* (RNGFRAMETEST phase 2: constructing a Unit and drawing 30 frames must
      leave the stream untouched) but not the *instrument*. A `SIGHTLINE_RNGTRACE=1` dial that
      prints draw-site stacks by frequency would make "who is drawing, and from where" answerable
      on demand rather than by hand-patching `Util.cs`.
- [ ] **`byObjectiveByBucket` is shipped and unread** — deliberately fine-grained (objective ×
      squad size × HP band), so every cell is n≤4 at n=20. It is for the n≥80 wave, and it is
      the fix for the survivorship trap that gave X2 a public 8.03t Escort that was really 12.81t.

      **UPDATE (TRUE BAND, 2026-08-29): the additive band shipped, and the mechanism stated in
      this bullet was measured FALSE — `pbest` does not fall with threat. The numbers in this
      W4 section are multiplicative-instrument numbers. See the TRUE BAND section below.**
- [ ] **ENVELOP rim waves** (`SIGHTLINE_RIMWAVES=1`) — built, deterministic, PAIRTEST-clean,
      shipped OFF because the round budget ran out. One flag, one paired round.
- [ ] **A heavier PINCER / ENVELOP weighting.** Pinned at h0 (n=40 each) PINCER ran 47.5%
      completion and ENVELOP 60.0% against a 32.5% baseline, and inside the shipped mix
      PINCER missions score `choices/ARMED` 1.70 vs FRONTAL's 1.39. The shipped 3/3/1/3 is what
      was measured end-to-end; a 1/4/1/4 deal is the obvious next round.
- [ ] **CROSSFIRE drags Escort** (13.40t pinned vs PINCER's 5.65t) — its NE mass sits on the
      cols 16-17 extraction corner and gets scattered by the spawn-collision loop. Gating it
      off evac objectives the way ENVELOP is gated is the cheap fix, unmeasured.

### PROGRAM RESONANCE — W5 "THE FIRST HOUR AND THE FRONT DOOR" (2026-08-29, details in DEVLOG §W5)

Balance-inert by construction *and* by measurement: `PAIRTEST` byte-identical, and a pinned-slot
`SIGHTLINE_BALANCE=5` (`runs=10` asserted, `SIGHTLINE_BALANCE_BASE=120`, heat 0) whose JSON is
**field-for-field identical** to the same batch on the branch point `d350416`.

- [x] **THE HEADLINE DEFECT — mission 1's briefing could not draw, and it REPRODUCES.** The audit
      traced the chain by code-read and said it could not be reproduced; `SIGHTLINE_BRIEFTEST`
      (the one self-test that drives the LIVE, persisting path, because `NoPersist` is what hid
      it) measures **0.00 s of 11 s** on the pre-fix tree. Fixed by ORDERING: the briefing is a
      pre-fight beat and the mission-1 lesson strip arms PENDING behind it (`Game.TutPending`),
      opening the frame the card retires. `TutStepFire` gains the turn-count patience fallback its
      three siblings had. `SIGHTLINE_BRIEFFIRST=0` restores the old order and turns the test red.
- [x] **THE BLOOM STOPS EATING THE TYPE** (`visual-2`). `Display.RenderFrame` splits the frame
      on the **bloom source**: board + overlay-screen backdrop are the bright-pass input, the
      chrome is painted on top of it into the same target. Main-menu TRAINING OP measured
      **2.19:1 → 10.76:1** glyph-vs-plate with post-FX ON (method stated in DEVLOG §W5-2). The
      first attempt drew the HUD after the composite and **stranded BRIGHTNESS and GAMMA on the
      board** — that is recorded in DEVLOG §W5-2 and in `Display.RenderFrame`'s header, because
      the colour-grade seam and the bloom seam are not the same seam. The board's own bloom is
      unchanged: every sampled patch sits inside its own frame-to-frame animation swing (the gold
      objective marker alone spans 18.9 luma across four adjacent frames of one build), and every
      glowing object's peak is identical. `SIGHTLINE_CONTRASTTEST` is the standing gate;
      `SIGHTLINE_HUDINFX=1` falsifies it.
- [x] **THE DOORS** (`newplayer-2`, `wildcard-3`). A third **WAR ROOM [W]** plate on both end
      cards, a "spend it in the WAR ROOM" line under the SALVAGE slab, and an "N JOIN THE RESERVE"
      header with per-survivor recall prices — where N is the *delta* of `SaveGame.VeteranCount()`
      so the card cannot over-claim (METATEST pins it). **QUIT TO DESKTOP [Q]** on the pause card
      (arm-then-confirm, with the honest cost stated) and **QUIT [Q]** on the main menu.
      `SIGHTLINE_QUITTEST` asserts the quit path keeps the mission-start checkpoint byte-identical
      and never touches `meta.json`.
- [x] **THE ON-RAMP IS THE DEFAULT** (`newplayer-4`). A zero-run profile opens on RECRUIT
      (`Game.FirstTimeProfile`) and "< RECRUIT" names the rung below zero on every profile. This
      moves a DEFAULT, not a rung — every archived heat number is untouched, and the measurement
      harness sets heat explicitly under `NoPersist`. ONRAMPTEST asserts the default *and* the
      control (a played profile keeps heat 0).
- [x] **THE CHROME** (`visual-7`, `newplayer-3`, `newplayer-6`). A fixed slot map keyed by verb
      stability (volatile verbs at the tail, where appending cannot move anything) plus one
      backing plate; the CONCEALED pill's pulse floor raised from **0.10 to 0.56** (a 10.0× swing
      to 1.79×); doctrine cards sized to content — **ten of sixteen boons overflowed** the old
      fixed 74px height — and the class-glyph disc moved out of the operator blurb's row.
      `SIGHTLINE_CHROMETEST`, falsified by `SIGHTLINE_OLDCHROME=1`.
- [x] **THE WORDS** (`newplayer-5`, `newplayer-7`). Both "glowing tile" prompts now name the
      outline, the corner ticks and the dashed ring the renderer actually draws. FIELD CRAFT gains
      SHOVE and UTILITY ITEMS, and a **VERBS & KEYS** tab is generated from `Hud.VerbTable` +
      `Hud.VerbHelp` — the same switch the action bar's tooltip reads, so help and manual cannot
      drift. CODEXTEST asserts every verb has a permanent home (`shove` and `item` failed before).

- [ ] **NOT DONE — `visual-6`: unify the intro's four button families onto one system.** Marked
      droppable-last in the brief and dropped. Its *measurable* half (labels washing out) is fixed
      by the post-FX split; the rest is a substantial aesthetic redesign of the storefront screen
      that this wave could only review with its own screenshots. **Still true:** three button
      styles across four widths on five rows with three gutters, hotkey badges inside the corner
      radius, LAST STAND spending the reserved danger red on a menu affordance, and a frame whose
      top-1% chroma is ~189 against a board at ~100.
- [ ] **Residual (recorded, not fixed): the HUD still receives the composite's chromatic
      aberration and vignette.** The audit's *primary* visual-2 finding (contrast collapse) is
      fixed; its smaller secondary one — edge colour-fringing on HUD text up 16-66%, edge
      luminance down ~14% — is not, because the chrome is deliberately still inside the colour
      grade so BRIGHTNESS and GAMMA keep working on it. Fixing it properly means a third pass:
      composite with `uBright`/`uGamma` neutral into a second target, draw the chrome, then blit
      through a small grade-only shader that writes `alpha = 1`. Costs one full-screen RT and one
      blit per frame, on a game with no frame-time instrument yet (`wildcard-7`).
- [ ] **Residual (recorded, not fixed): `SIGHTLINE_POSTFX=1`'s "demo bloom" comment is stale.**
      The boot-time `BloomIntensity = 0.85` / `ChromaIntensity = 0.6` injection is overwritten on
      frame 1 by `Game.Update`'s own `SetPostFxParams(_postFxBloom = 0, …)`, so the hook has been
      photographing the RESTING configuration for waves. That is *better* for W5's purposes (the
      contrast numbers above are what every player sees on the menu, every time) but the hook does
      not do what its comment says. One-liner for whoever next touches that path.

### PROGRAM RESONANCE — W5-FIX "THE REVIEW BLOCKERS" (2026-08-29, details in DEVLOG §W5-FIX)

Five blockers from a four-reviewer pass, plus four cheap evidenced items. Every load-bearing
safety claim from W5 was re-verified by the reviewers and held; none of it was touched here.

- [x] **AUDIO CHECK was left behind by the render split, and three comments asserted the
      opposite.** `Hud.DrawAudition` painted its own `DrawTacticalBackdrop` in the CHROME pass,
      i.e. after `BuildBloom`, so the composite added the **live board's** glow through an opaque
      screen that ships post-FX ON and is reachable from the pause card mid-mission. Against
      `d350416`, post-FX ON: **17,010 px > +20 luma, 8,640 > +40, peak +154.1** (mid-mission
      17,214 / 8,899 / +166.0). After: **0 px > +20, peak +7.0**, with the post-FX-OFF pair still
      byte-identical. `Phase.AudioCheck` joins `Hud.BackdropPhase` and `DrawBackdropLayer`'s
      switch (off `g.AudClock`, never `GetTime`); all three false sentences now name BARRACKS
      alone.
- [x] **A STRUCTURAL gate for the whole class — `SIGHTLINE_BACKDROPTEST`.** Drives every `Phase`
      through the chrome pass and asserts none paints a full-screen backdrop there
      (`Hud.BackdropPaints`, incremented inside `DrawTacticalBackdrop` itself), then through
      `DrawBackdropLayer` and asserts the switch and the registry are the same set, then that the
      modal scrim doubles only where the composite runs. CONTRASTTEST reads nine main-menu labels
      and could never have caught this. `SIGHTLINE_AUDBACKDROP=1` falsifies it.
- [x] **The content-sized doctrine card no longer pushes the DEPLOY row off the screen.** At the
      default 100% the BACK / DEPLOY / RE-ROLL POOL row was sliced at y=800; at 120% it was off
      entirely, and RE-ROLL POOL had no keyboard route. `Hud.DraftLayout` computes the whole stack
      up front and reclaims the height from the gaps (floors set by the type each gap carries at
      120%), and the doctrine row widened to the RUN CONTRACT row's width — the one mismatched
      width on the screen, and the change that turns FIELD DRILLS' four lines back into three.
      CHROMETEST asserts the row is on screen for **16 boons × 4 text sizes**; `[R]` re-rolls.
- [x] **CHROMETEST leg (C)'s overflow assertion was TAUTOLOGICAL** (`need > Math.Max(74, need)`).
      It now measures the last line's real ink bottom through the live font against the renderer's
      own card height.
- [x] **The DERIVED sweep count was wrong in both modes.** `^echo` missed PAIRTEST's indented
      invocation: `--full` printed 53 while 54 ran. Anchor is `^ *echo`; proven by adding a hook
      and re-running both modes (55 `--full`, 54 plain, matching the real line count). Fifth time
      this counter has been wrong.
- [x] **The §1.1 priority change is recorded as an amendment** — `docs/DESIGN.md` **§1.2**. The
      never-simultaneous invariant survives, but the stated priority is inverted for a first-time
      player's first eleven seconds, which is the window §1.1 exists to protect. Argued on its
      merits (the rule as written measured 0.00 s of 11 s — an absolute yield to a layer that
      never ends is a deletion, not a priority), with the limits written down and §1.1's fourth
      bullet cross-referenced.
- [x] **`SIGHTLINE_BRIEFTEST` now observes the DRAW side.** It certified "the briefing plays its
      full 11 s" while reading only `Game.BriefTimer`; a reviewer's one-line `&& false` on the
      dispatch made the card undrawable and it still PASSed. Every watched frame now paints a real
      frame through `Hud.Draw` and counts `Hud.BriefCardDraws`. With the mutant:
      `FAIL (briefCardDrawnOnOnly0framesOf630)` — and every model-side assertion still passing,
      which is the measurement of how blind the old form was.
- [x] **The double scrim is gated on `Display.Enabled && Display.PostFX`.** With post-FX off there
      is no bright pass to attenuate. Board strip under the pause card: base **18.56** → pre-fix
      **12.08** → fixed **18.55**; post-FX ON deliberately unchanged (10.09 → 10.08).
- [x] **`Display.RenderFrame` stops building the combining `draw` closure on the post-FX path**,
      where it was never invoked. Claimed as removed dead work, not as a measured frame-time win.
- [x] **The DEVLOG's "no camera path" sentence corrected.** `Game.ViewCamera` and the
      `BeginMode2D` around `Renderer.DrawBoard` are INTACT (`Game.cs:7629/7658`); only the
      letterbox-blit camera the abandoned first attempt would have needed was never written.

### PROGRAM RESONANCE — X2 "TRUE NORTH II" (2026-08-29, details in DEVLOG §X2)

- [x] **THE LADDER OF RECORD.** The first ladder ever measured on the COMPOSED tree: n=40
      campaigns per rung across six rungs (RECRUIT + h0/2/4/6/8), `runs=20` asserted in all 12
      chunks, base commit `a61ef42`, raw data archived in `docs/measurements/x2/`. Supersedes
      X1's, W5's, W4's and FUL-13's ladders, each of which was measured on its own base.
- [x] **THE BAND, re-argued and KEPT** (h0 55 / h2 40 / h4 30 / h6 20 / h8 10, ±8; h8 ±5), with
      two amendments from measurement: **RECRUIT joins it at 75 ±8** with a standing
      `RECRUIT − h0 ≥ 15` floor, and the band's ±8 is now documented as **≈1 standard error at
      n=40**, so rung ORDER is not a gate at that N.
- [x] **THE COLD-OPENER GRACE** (`Mission.OpenerTrim`, shipped 1). Mission 1 measured **75%**
      win at heat 0 against 90% for m3-m4 — a U-shaped curve whose left arm ended a quarter of
      all runs before the player had earned anything, and the exact front-loaded anxiety
      DESIGN §3.D forbids. The heat grace that already fixes this is gated on `heat > 0`. One
      body off m1 and m2 takes mission 1 to **100% (n=40, zero losses)** and heat 0 from
      **35.0% → 57.5%**, with shots-per-kill UP at every rung. `OPENERTEST` pins it.
- [x] **Three default-OFF dials, measured and priced, for whoever needs one**:
      `SIGHTLINE_AIMTRIM` (**+7.5 completion per 5 aim points** at h0, Eliminate's turn budget
      untouched at the 5-point dose; the 10-point dose reaches the band but breaks two turn
      budgets), `SIGHTLINE_TOUGH` / `SIGHTLINE_TRIM` (X1's pair, now pinnable), and
      `SIGHTLINE_ENEMYBASE`.

- [ ] **RAISE N BEFORE SPENDING ANOTHER LEVER.** The highest-value measurement in the project
      right now is **n≥80 per rung on the state that is already shipped**. At n=40 the error bar
      (±6-8) is the size of the band tolerance and bigger than the step between rungs; three of
      the six deltas in X2's shipped table are indistinguishable from noise, and two waves have
      now argued about rung inversions that no data could resolve.
- [ ] **Heat 8 is out of band at 17.5%** (band 5-15, so +2.5 over the ceiling, 0.4 SE). Do not
      aim a rung-average lever at it: the apex is a wall made of four objectives —
      **Escort 33% (n=15), Evac 0% (n=4), Rescue 33% (n=3), Decapitate 41% (n=17)** — and the
      rung average is what those produce.
- [ ] **Escort is the drag objective and its repair was flattered by a broken ladder.**
      12.81 turns at h0 and 13.48 at RECRUIT in the shipped state, against the 8.03/8.19 that
      W4 and X2's own baseline recorded — those samples contained only the runs healthy enough
      to REACH an Escort (n 13 → 19 once the opener was repaired). Its real h0 cost is ~13 turns.
- [ ] **Lead-swings fell 0.79 → 0.61 at heat 0** and the wave accepted it: a 4-body opener
      against a full squad is not a contested fight, and mission 1 is ~26% of matches played.
      If the swing metric matters more than the opener's shape, the honest fix is to make m1
      contested *some other way* (a mid-mission reinforcement beat, a timed objective), not to
      put the fifth body back.

### PROGRAM RESONANCE — TRUE BAND (2026-08-29, details in DEVLOG §TRUE BAND)

- [x] **THE CAP, NOT THE BAND, IS THE HALF THAT MOVES THE NUMBER** (post-review correction). The
      probe's exact same-denominator 2x2 — {mult window, additive band} x {cap 2, cap 4} over the
      same soldier-turns — reads, at heats 0/4/8 on common base 50: **band effect at fixed cap
      −0.150 / −0.098 / −0.141** (the additive band is NARROWER than the window it replaced and
      admits strictly fewer destinations at every rung) against a **cap effect of +0.510 / +0.601
      / +0.570**. The wave's first write-up credited the lift to the band; it is entirely the cap,
      and the band's own contribution is negative. The band is still right — stated in the score's
      own units, immune to the score's absolute level, not degenerate at a non-positive best — but
      **"it raised the number" is not a reason to prefer it.**
- [x] **THE SPEC ABOVE IS SHIPPED — `CountMeaningfulChoices` now bands both axes ADDITIVELY**
      (`ShotBand` 2 shot-value points, `PosBand` 3 safety points, `PosChoiceCap` raised 2 → 4;
      `SIGHTLINE_CHOICEBAND=mult` restores the pre-wave rule exactly). Proven **gameplay-inert**:
      **600-686 aggregate fields diffed on five** paired mult/add batches (each pair on one slot
      base; heats 0/4/8 on base 50 plus heats 4/8 on bases 60/70), 10-12 choice fields moving and
      **zero non-choice fields moved on every one** — the field counts differ per pair because
      `byDeploy`/`byObjective`/`byArena` are variable-length arrays. PAIRTEST green. `SIGHTLINE_BANDTEST` pins the
      constants, the rule, the exact reproduction of the pre-wave counts against a literal
      transcription over 120 boards, no state mutation and zero `Util.Rng` draws (with a
      sensitivity probe on the purity detector). `SIGHTLINE_BANDPROBE=1` ships as the
      instrument-design probe that chose the constants.
- [x] **THE SPEC'S PREMISE WAS FALSE AND IS NOW CORRECTED IN THE RECORD.** `pbest` does **not**
      slide with threat: measured over 848 armed soldier-turns it is pinned at a **median of 40**
      at heats 0, 4 and 8 (the "full cover, unexposed, ground level" value 24 + 2×8), confirmed in
      all five chunks. (Its *mean* also rises with heat on the common CRN base, but that reverses
      off-base and is world-set noise — not claimed.) The multiplicative window was ~6 points wide at every rung and was never
      shrinking; the `pbest > 0` guard fires on 0.0-1.0% of soldier-turns. **What actually flattened
      axis (b) was the anti-inflation CAP of 2**, which sat between the p25 and p50 of the
      admitted-count distribution: `Math.Min(cap, admitted-1)` means cap 2 first binds at
      `admitted >= 4` (~p70-75) and cap 4 at `admitted >= 6` (~p80-88), so cap 2 retained only
      **41-55%** of the uncapped signal where cap 4 retains 60-84%. (An earlier version of this
      bullet said cap 2 "clipped the median turn". That was arithmetically false — review caught
      it — and it is corrected rather than deleted.)
- [ ] **⚠ EVERY `ch/ARMED` / `ch/turn` / `target-choices` / `position-choices` NUMBER IN THIS FILE
      AND IN `docs/DEVLOG.md` DATED BEFORE 2026-08-29 IS A *MULTIPLICATIVE* NUMBER** and is not
      comparable to anything measured after TRUE BAND. That includes the W4 section above
      (1.55-1.64, 1.70 vs 1.39, the whole `[choice-split]` table), X1's ~1.5, X2's 1.44-1.78 rung
      column, and FUL-13's. They are left in place as provenance. Re-derive with
      `SIGHTLINE_CHOICEBAND=mult` rather than comparing across the change.
- [ ] **W4's TWO DECISION-DENSITY GATES ARE VOID AND MUST BE RESTATED BEFORE ANYONE CLAIMS THEM.**
      On the new instrument this tree reads `ch/ARMED` **2.389** and `meaningful-choices/turn`
      **3.738** at heat 0, crossing W4's published `>= 2.00` and `>= 3.00` (which W4 recorded as
      MISSED at 1.53 / 2.36). **That is not a pass.** Both thresholds were set against the
      multiplicative instrument and name quantities that no longer exist; the numbers moved
      because the ruler did, and §4's inertness proof shows the game did not move at all.
      Restating them is a judgement call about what a rich turn is, and it wants its own round.
- [ ] **THE RUNG-SEPARATION QUESTION IS STILL OPEN — n=10 could not answer it.** TRUE BAND
      re-baselined at `SIGHTLINE_BALANCE=5` per chunk (10 campaigns, `runs=10` asserted ×10). The
      *paired* mult→add delta is exact (identical worlds, identical play), but the rung-to-rung
      spread is not: the same three rungs read 1.59 / 1.86 / 2.11 on slot bases 50/60/70 and
      1.59 / 1.84 / 1.66 on a common base 50. A ±0.4 world-set swing at n=10 is bigger than every
      rung difference on the table. **Re-run the six chunks at n≥40 before anyone quotes a
      decision-density rung ordering again** — this is the same standing lesson as the ladder's
      ±6-8, applied to a metric with a smaller absolute range.
- [ ] **The NEAR-INVARIANCE itself was never re-tested at power on the new instrument.** W4's
      "1.55-1.64 across five levers" is what this wave exists to answer, and TRUE BAND disproved
      the *mechanism* it blamed without re-measuring the *phenomenon* at n=40. It may still be
      near-invariant for a reason nobody has found yet.
- [x] **The archive split is now enforced in DATA, not just in a banner** (review's own point that
      the banner was weak, which the wave had conceded). `SIGHTLINE_CHOICEBAND` is STRICTLY parsed
      — anything but `mult`/`add`/unset makes the binary refuse to start with exit 2, instead of a
      typo silently selecting the new rule — and every `SIGHTLINE_BALANCE` aggregate now carries
      an `instrument` field (`"mult-v1"` / `"add-v2"`). `diff_chunks.py` REFUSES a cross-instrument
      diff unless passed `--cross`, and says why.
- [ ] **SPEC: axis (a)'s additive band is still magnitude-dependent at the bottom of its range.**
      When the best shot is worth under `ShotBand` (2) points — measured p5 is 2-4, so rare but
      not empty — the cut `best - 2` is at or below zero and **every** rival is admitted however
      worthless: at `best = 1.5` the band is 133% of the best, which is the same
      magnitude-dependence TRUE BAND removed, mirrored. The shipped `floorAtZero` clamp documents
      the invariant that makes it arithmetically harmless today (every `ShotValue` is > 0, so the
      interval `[best-2, 0)` is empty of candidates — `SIGHTLINE_BANDTEST` asserts the positivity
      over 200+ real shots) but it does **not** fix the wart. The real options are a hybrid cut
      (`max(best - ShotBand, 0.5 * best)`, a multiplicative FLOOR under an additive band) or an
      eligibility floor on `best` itself. Both are new levers and neither belongs in a wave that
      already shipped two — measure it as its own round.
- [ ] **`SafetyAt` deserves the scrutiny the band just got.** It consults only the NEAREST foe for
      cover, ignores whether a destination keeps a shot, and its `24` base is arbitrary. Any of
      those could matter more to axis (b) than the band shape did. Deliberately out of scope for a
      wave whose point was to change one thing and prove it inert.
- [ ] **The heat ladder's MIDDLE does not measurably escalate.** X2's baseline (n=40/rung, ±6-8)
      reads mission-win 80.2 (h0) / 82.6 (h2) / 75.0 (h4) / 81.7 (h6) — a 2.8-point spread on
      n=263 vs n=266 pooled halves, i.e. nothing. Only RECRUIT (94.3) and heat 8 (68.4) separate.
      Rungs 1-7 add bodies and stat points that the measurement cannot see. Either the rungs need
      real teeth or the ladder needs fewer, bigger steps — but the first job is a **higher-N**
      measurement (n≥80/rung) so the question can be asked at a precision that can answer it.


### PROGRAM RESONANCE — WAVE "THE FIT" (2026-08-30, details in DEVLOG §THE FIT)

Presentation only (base = the W9/W5/W8 integration tip). Proved balance-inert two ways:
`SIGHTLINE_PAIRTEST` PASS, and a pinned-slot `SIGHTLINE_BALANCE=5 BASE=950` chunk (`runs=10`
asserted both sides, exit 0 both sides) **field-for-field identical** to the same batch on the
branch point, `harness{}` excluded.

- [x] **THE STANDING GATE: `SIGHTLINE_FITTEST`** (wired into `scripts/qa-sweep.sh` through
      `verdict`). The five defects this wave took were one structural gap: **no self-test in this
      project ran at any text size but 100%**, while the game ships four {0.90, 1.00, 1.10, 1.20}
      against fixed-pixel chrome. FITTEST asserts that no string on the five surfaces this wave
      touched is painted outside its own chrome, or into another string's pixels, at **all four**
      shipped scales. It PASSes with 15-69 px of STATED headroom per leg; `SIGHTLINE_OLDFIT=1`
      restores all five pre-fix geometries and it reports **40 violations** across 100/110/120%.
      A direct source revert of one fix alone (the armory tag's band) fails it at 8.
- [x] **CHROMETEST's own `blurbEllipsizes` leg moved INSIDE its scale loop.** It had been sitting
      outside, so the one assertion that guarded the operator blurb only ever ran at 100% — where
      the longest blurb had **two pixels** of margin. `SIGHTLINE_OLDFIT=1` now fails CHROMETEST too.
- [x] **The mid-run FIELD DOCTRINE card sizes to its text** (`Hud.BoonOfferCardH`). It was a
      literal 188 px drawing the same sixteen descriptions W5-FIX-2 had already content-sized on
      the DRAFT screen: FIELD DRILLS' fourth line landed ON `[ CHOOSE ]` at the **default** text
      size, five lines at 120%. Same string, second call site.
- [x] **The ARMORY weapon row's two strings stop sharing a band.** The blurb started at a literal
      `r.X+48`; the price/EQUIPPED tag was right-aligned through `Cfg.Measure`, so it scaled while
      the blurb's origin did not and the two converged. The tag moves onto the NAME's band and the
      card widens 560 → 600. Vertical separation, not truncation.
- [x] **The HALL OF FAME legend row splits.** Identity (rank + class) left, score (kills + heat)
      right-aligned to the panel's content edge — where it also columnises down the list. With a
      LIEUTENANT staged at 120% the old single line was painted **outside** the panel border.
- [x] **The draft's bottom row plates are sized from their own widest label** — RE-ROLL POOL was a
      literal 190 px carrying a 201 px label at 120% (text hung past **both** borders); DEPLOY a
      literal 280 px against a 294 px worst-case label. Sized from the widest label the button can
      EVER show, never the current one, so the row cannot re-flow under the cursor.
- [x] **The draft operator card widens 300 → 394** = the RUN CONTRACT row's width / 3, so the class
      blurb (37 chars, 320 px at 120%) fits without ellipsis and the candidate grid stops being the
      one narrow row on a screen whose other two rows are 1230 px. Growing the chrome, not
      shrinking the writing.
- [x] **The three screenshot hooks stage the WORST case, not the first one.** `DebugBoon` leads
      with the longest description (FIELD DRILLS is 1 of 16, so an unseeded glance showed it ~19%
      of the time); `DebugWarRoom`'s NOX is a LIEUTENANT SHARPSHOOTER with a nickname; `DebugArmory`
      picks the SHARPSHOOTER (the 49-char SNIPER blurb) instead of `Squad.First(!IsVip)`. "The
      staged data is short" is why a human eyeballing these screens never saw any of this.
- [ ] **Two of the five were ALREADY FIXED by W5 and are recorded as such.** The draft-screen
      doctrine card overflow (W5-FIX-2's content-sized card) and the DEPLOY row falling off the
      bottom at 120% (`Hud.DraftLayout`) do not reproduce on this tree. Their *residues* did —
      the second call site and the plate widths above.
- [ ] **The REQUISITION card is still sized for the full roster in ARMORY step 2.** `armoryH =
      104 + 28 + Run.RosterMax*52 + 64` regardless of how many weapon rows the chosen soldier
      actually has, so a SHARPSHOOTER (2 options) leaves ~250 px of dead panel. Pre-existing,
      cosmetic, out of scope for a wave about overflow — but it is now visible in every armory
      screenshot because the hook stages a SHARPSHOOTER.
- [ ] **The gate covers five surfaces, not the game.** Every other screen is still asserted at
      100% only (or, for card bodies, by R2's `VOICETEST` leg 8). FITTEST is written so a sixth
      leg is an addition, not a rewrite — the next wave that touches a screen should add one.

### PROGRAM RESONANCE — W4 "THE BOARD BECOMES A PLACE" (2026-08-29, details in DEVLOG §W4 BOARD)

Rendering only (base `d350416`); `src/Renderer.cs` draw path + the `Pal` block of `src/Util.cs`.
Proved gameplay-inert two ways: `SIGHTLINE_PAIRTEST` byte-identical, and a pinned-slot
`SIGHTLINE_BALANCE=5 BASE=140` chunk (`runs=10` asserted both sides) **field-for-field identical**
to the same batch on the branch point.

- [x] **COVER IS MERGED VOLUMES** (audit `visual-1`). Union-find over 4-connected same-`TileType`
      tiles at the same elevation tier; inset, rounding, cast shadow, contact AO, front-face
      gradient, top-edge highlight, structural rim and corner chip all gated on which side is a
      group seam; the top face extends over the wall band on an interior south edge so a run is
      one slab with one wall. Jitter/lift/radius moved from per TILE to per GROUP.
      **Measured: 684 authored cover tiles → 383 groups = 19.0 boxes/map → 10.6 volumes/map (−44%);
      on one seed the rendered connected-component count goes 19 → 12.** `SIGHTLINE_COVERMERGE=0`.
- [x] **PER-BIOME FORM VOCABULARY** (audit `visual-1b`). CRATE / WALL / BOULDER / WRECK, one per
      merged volume, three candidates per biome — 4-6 object types per map instead of one.
- [x] **THE VALUE HIERARCHY, RIGHT WAY UP** (audit `visual-3` + `visual-4`, taken as ONE decision).
      Ring demoted to a 2px state indicator (topology kept — it is the colourblind team/role
      channel), the white specular catch retired, the archetype form drawn as an outlined figure on
      an explicit value rung (`Renderer.ToLuma`), the dormant pod moved onto outline weight + glyph.
      **Measured (14px patches, before → after): SELECTED soldier peak 188.1 → 201.7, ACTIVE
      hostile 211.1 → 171.0, SUSPICIOUS 205.1 → 161.7, DORMANT pod 219.9 → 149.3** — a complete
      inversion becomes monotone, and stays monotone in the colourblind palette (201.7/171.5/149.3).
      **Bright-pixel share inside the archetype figure: 0% → 87%.** `SIGHTLINE_TOKENSTYLE=0`.
- [x] **THE MOVE OVERLAY IS ONE REGION, ONE CONTOUR, AND NOT GOLD** (audit `visual-5`). Dash region
      on demand (hold SHIFT, or hover a tile outside walk range); boundary stitched by marching
      squares into closed loops with a continuous dash phase (**58 edges as 5 strokes = 11.6
      edges/primitive, against 1.0 before** — a code-structure metric, not a visual result: the
      strokes land on the same pixels, what it buys is the welded joint and the continuous dash
      phase); `Pal.MoveYellow` (byte-identical to the reserved
      objective gold) renamed `Pal.MoveDash` and moved onto a value variant of the friendly cyan.
      `SIGHTLINE_MOVESTYLE=0`, `SIGHTLINE_MOVEDASH=1`.
- [x] **`SIGHTLINE_BOARDTEST`** — the project's only pixel-measuring self-test, wired into
      `scripts/qa-sweep.sh`. Its clock is pinned (`Renderer.TimePin`), so it prints one number per
      run. Fails on each pre-wave dial (TOKENSTYLE=0 reports the dormant pod at the auditor's
      **219.9** against a selected soldier at **188.1** — the wave's published 166.8 was the other
      mode of an unpinned animation phase and is superseded; COVERMERGE=0 measures a 10px gutter;
      MOVESTYLE=0 measures 1.0 edges/stroke; COVERSEED=0 re-rolls 1886px of a surviving cover
      tile).

**Left open by this wave (see DEVLOG §W4 BOARD "WHAT I DID NOT FIX"):**
- [ ] **The merged volumes are still axis-aligned rectangles on a square grid**, because the drawn
      footprint is the tile grid and this wave was correctly forbidden from touching tile geometry.
      A board that reads as terrain rather than as well-dressed blocks needs the footprint itself to
      stop being axis-aligned — a different wave, with a gameplay-inertness argument this one
      cannot make.
- [ ] **The S4-B cover-tier cue (△ / —) is still one stamp per TILE, not per volume**, so a
      four-tile wall carries four identical marks. Kept deliberately: cover tier is a per-tile
      gameplay fact and DESIGN.md §3.H names the glyph as its non-colour channel. Revisit only with
      a read that keeps the tier legible at the far end of a long run.

**W4 BOARD REVIEW FIXES (same branch, base `d122ea5`) — see DEVLOG §W4 BOARD REVIEW FIXES:**
- [x] **The cover volume's identity is stable under damage** (`Grid.CoverSeed`, assigned once when
      a cover tile first exists and never re-derived; new cover ADOPTS a volume it touches). The
      wave keyed the material form and the footprint jitter off a union-find root recomputed from
      the live grid every frame, so shooting the NW tile off a wall re-rolled the material of every
      surviving tile. Gate E of `SIGHTLINE_BOARDTEST` asserts a surviving tile is byte-identical
      after the destruction (0 of 3360 px; `SIGHTLINE_COVERSEED=0` measures 1886).
- [x] **`SIGHTLINE_BOARDTEST`'s animation clock is pinned** (`Renderer.TimePin`, set to t = 3π/10
      for the test and restored after). The legacy path was bimodal at 166.8 ×3 / 188.1 ×7 over ten
      runs of one binary; it now prints one line, ten times out of ten.
- [x] **Gate A runs in BOTH palettes**, and `SIGHTLINE_CB` is read where a BOARDTEST run can see it.
      The claim that value rungs stated as target luma survive `Pal.SetColorblind` had zero coverage.
- [x] **The awareness markers cannot be occluded off the board** — the high slot is clamped to the
      board's top edge and drops inside the pod's own tile (on a dark lozenge) when a unit stands on
      the tile above. `SIGHTLINE_MARKERS=1` stages the cases.
- [x] **Three false "no `IsKeyDown` existed before this" claims corrected** (Renderer.cs, DEVLOG,
      CLAUDE.md): `Game.Codex.cs` has read four held keys since before the wave. SHIFT is the first
      key read as a *modifier*, which is the defensible claim.
- [x] **`bal/w4after.json` / `bal/w4base.json` removed from the index** (added in the same commit
      that gitignored `bal/`; byte-identical to `docs/measurements/w4-board/`).

**Costs the W4 review accepted as costs, not defects (lead's call; recorded so a later wave can
price them):**
- [ ] **A merged volume reads FLATTER than the boxes it replaced.** The reviewers' arithmetic: a
      two-tile-deep north-south run draws as a 128px top face with a single ~16px wall band at its
      southern end, so the north half of the run has no "standing up" read left — the very cue the
      per-tile boxes were paying for with their gutters. The merge is still the right trade (one
      wall instead of a light/dark ladder), but the volume needs its height back on the deep case.
      Counter-measures named by the reviewers: a stronger OUTER RIM on the volume, or a north-edge
      occlusion band that darkens the top face where it meets the tile behind it, applied only when
      the run is ≥2 tiles deep. Neither costs a hitbox.
- [ ] **Four identical △ tier glyphs on one slab read as SURFACE PATTERN, not as information.**
      This is the sharper form of the per-tile-cue item above: at four repeats the eye stops
      parsing them as a legend and starts parsing them as texture. The reviewers' proposal keeps
      both properties — draw the tier glyph per VOLUME but at BOTH ENDS of a run, so the far end of
      a long wall keeps its tier read (the thing the per-tile stamp is protecting) without the
      middle of the run being tiled with repeats.
- [ ] **The dash-on-demand SHIFT modifier is undiscoverable** — nothing labels it. The hover
      trigger covers the case that matters and no information is lost, but it wants a FIELD MANUAL
      line.
- [ ] **`DrawMoveOverlay` reads `Raylib.IsKeyDown` directly.** Presentation-only and deterministic
      under the harness, but input in a draw path is off-architecture; route it through `Game` when
      a wave owns that file.
- [ ] **Board rendering performance is unmeasured on real hardware.** Three 900-frame llvmpipe runs
      per configuration could not resolve a difference: 86.9/92.0/120.4 ms/frame new against
      84.7/103.8/123.4 old, i.e. ±20% of container noise in both.

---

## PROGRAM CROSSCUT — THE OPEN BALANCE TARGET (measured, replicated, unspent)

> ### ⚠ CORRECTION (2026-08-30) — THE FIRST VERSION OF THIS SECTION OVER-CLAIMED, AND IT WAS MINE
>
> I originally wrote that "the cold rungs sit ~7-8 points below band". **That is false.** I
> computed each rung's distance from the band's CENTRE and printed it under a column headed
> "vs band" — but the band is a TOLERANCE (±8), and membership is decided against its floor, not
> its midpoint. A W2 reviewer caught it. Checked properly:
>
> | rung | band | floor | L2 (n=160) | verdict | W2's control (n=80) | verdict |
> |---|---|---|---|---|---|---|
> | heat 0 | 55 ±8 | 47 | 46.9 | **out by 0.1** | 47.5 | in |
> | heat 2 | 40 ±8 | 32 | 36.9 | in | 26.2 | **out by 5.8** |
> | heat 4 | 30 ±8 | 22 | 23.1 | in | 22.5 | in |
> | heat 6 | 20 ±8 | 12 | 20.6 | in | 17.5 | in |
> | heat 8 | 10 ±5 | 5 | 8.8 | in | 12.5 | in |
>
> **Nine of those ten measurements are IN BAND.** At n=160, exactly one rung is out — heat 0, by
> 0.1 points, which at SE 3.9 *is* the floor. The two rungs the two instruments disagree about
> (h0 and h2) are the two where they disagree with each other, which is what you would expect
> from n=80 against n=160 rather than from a defect.
>
> This is the same class of error the program sent three waves back for, made by the person
> enforcing the rule. It is corrected in place rather than edited away.

### WHAT SURVIVES THE CORRECTION — and it is still worth a wave

The ladder's LEVEL is fine. Its SHAPE is not, and that claim rests on step sizes rather than on
band membership, so the correction above does not touch it:

| step | L1 (pre-W1, n=80) | L2 (post-W1, n=160) |
|---|---|---|
| RECRUIT → h0 | −25.0 | −25.6 |
| h0 → h2 | −15.0 | −10.0 |
| h2 → h4 | −12.5 | −13.8 |
| **h4 → h6** | **−3.8** | **−2.5** |
| h6 → h8 | −7.5 | −11.9 |

**`h4 → h6` is the smallest step on BOTH ladders**, measured on disjoint world sets, against
neighbours three to five times its size. `Heat.Mods` explains it exactly: **rung 8 is the ONLY
entry in the table carrying either `DmgDelta` or `AiTier`.** The middle rungs add bodies and
stats; only the apex changes KIND. Two rungs of the ladder buy the player almost nothing, and
that is a property of a static table rather than an emergent one — which is what makes it
fixable, and what makes it worth a wave even though every rung is in band.

- [ ] **THE FLAT-MIDDLE WAVE (specced, not started).** Reshape `Heat.Mods` so the middle rungs
      change KIND rather than only quantity — the audit's `balance-3` finding proposed moving
      `DmgDelta` to rung 6 and `AiTier 2` to rung 7, which is the obvious first candidate. Price
      it as ONE lever with a CRN-paired round against a fresh same-slot baseline on the merged
      tree. **Do not aim a rung-average lever at it** — pair it with the per-objective and
      per-node-kind decomposition W8 shipped, because W8 proved a pooled row can hide a
      49.5-point artifact.
      **Prerequisite:** W2, W9 and W8 all move gameplay or composition, so this must be measured
      AFTER they compose. Measuring before composition is the mistake fourteen waves made before
      X2 caught it.

- [ ] **THE MID-RUN DECAPITATE** (wave W8, in flight). 46.0% ±3.9 (n=163) against the boss
      finale's 69.7% (n=479), replicated from L1's 48.9/70.1 on disjoint worlds. Worst mission of
      any kind in the game. `Game.DesignateHvt` skips the HVT buff for an ELITE by its own comment
      and applies `+6 + mission` HP and +6 aim everywhere else — the stat-check wall was removed
      from the boss and left in the mid-run case.

- [x] **RETRACTED — the slot-set effect.** L1 measured bases 0/10 running +8.3 points easier than
      fresh sets (z=1.93, p=0.053) and recorded it as NOT established. L2 tested it with six fresh
      sets over 960 campaigns: +3.1 points, p=0.394, three of six rungs reversed. **The effect is
      not there.** The method rule (a rung is four slot sets) stays on COST grounds — it is free
      insurance — but must not be cited as evidence of a world-set artefact.

### PROGRAM CROSSCUT — W8 "THE HALF WALL" (2026-08-30, details in DEVLOG §W8)

- [x] **THE DECOMPOSITION IS VERIFIED FROM DATA, not only from reading `Run.cs`.** Pooled over
      L2's 48 archived chunks, `byNodeKind` Boss is **n=479 / 334 wins** and `byMission` m6 is
      **n=479 / 334 wins** — identical counts, not merely identical rates. There is no mission 6
      that is not a Boss node in 960 campaigns, so subtracting the m6 row from the Decapitate row
      is valid and the mid-run figure **46.0% ±3.9 (n=163)** stands.
- [x] **`byObjectiveByNodeKind` + `byObjectiveByMission` + `hvt{}` shipped** (`src/Stats.cs`,
      read-only, zero RNG draws), so an objective's row can never again pool a capstone with a
      mid-run node — or one mission depth with another. Inertness proven three ways: 42/43
      aggregate fields byte-identical on three paired chunks (only `harness{}` moves); 45/46 for
      the policy-dial binary at default (`R0diag`); and round **B** re-ran L2's exact 48-chunk grid
      and reproduced the archive with **zero differing rows** on every objective, node-kind,
      mission and rung total.
- [x] **The brief's named mechanism is REFUTED — do not re-open it without new evidence.** Split by
      whether the HVT actually took `DesignateHvt`'s buff, the mid-run population reads **BUFFED
      57.4% (n=61)** against **EXEMPT 39.2% (n=102)**: the buffed half is **18.2 points EASIER**,
      ±8.0. The m3 HVT (23.0 MaxHp) and the m6 HVT (22.4) are the same size of body and their
      missions read 38.3% and 69.7%. It is not the target.
- [x] **The lever was priced and NOT spent.** `SIGHTLINE_HVTDEPTH=-1` (buff `6+m` → `6−m`), 480
      CRN-paired campaigns per arm: buffed missions **73.8% → 78.6% (+4.8 ±2.1)**, mission 1
      **89.8% → 92.9%**, m4 **47.6% → 57.3%**. Real, and shipped OFF: the buffed HVT is **61 of
      3,547 missions played (1.72%, 0.064 per campaign)** and is the EASIER half of the gap.
      `SIGHTLINE_HVTBUFF` / `HVTDEPTH` / `HVTAIM` are default-identical to the pre-W8 arithmetic.
- [ ] **THE REAL ASYMMETRY IS THE FORCE, AND IT IS UNSPENT — this is the next wave's lever.**
      `Mission.Build` de-stacks the finale by **3-4 bodies** and resets `bump` (the
      `n >= Run.MaxMissions` branch); nothing equivalent exists mid-run, and an ELITE node adds
      **+2 bodies and +1 stat**, the exact inverse. Decapitate by node kind: **Supply 67.3%
      (n=52)** (one fewer body, −1 stat) against **Combat 32.9% (n=73)** — 34 points on one body
      and one stat point. One dial, one paired round.
- [ ] **THE BIGGER DEFECT: `Eliminate`'s 89.6% row is 960 mission-1s.** Every campaign opens on a
      Start node and a Start node is always Eliminate, at 97.5%. Strip the opener and Eliminate
      reads **42.3% on Combat nodes (n=104)** and **33.3% on Elite nodes (n=33)**, pooling to
      **40.1% ±4.2 (n=137)** — a **49.5-point** composition artifact, twice Decapitate's 23.7, and
      worse than Decapitate's own mid-run cells. Pooled on mid-run node kinds, the two KILL objectives read
      **38.3% ±3.1 (n=248)** against the six with a non-combat win condition at **83.4% ±1.0
      (n=1259)** — **45.1 points**. At mission 5, Eliminate is **25.6% (n=43)** and Sabotage is
      **96.8% (n=31)**. Five of the eight objectives let a squad decline the encounter and still
      win (C3 measured it; `Defend` is the sixth and it does not — see below).
      That is a design question (`docs/DESIGN.md` §A) and needs its own wave, not a tuning round.
      Two caveats travel with the number: it holds node kind and depth constant but NOT squad
      condition, and the autopilot's non-kill policies are written to skip the fight.
- [ ] **`SIGHTLINE_HVTAIM` is dialled and unpriced.** The `+6` aim half of the HVT buff partly
      duplicates what `bump` already grants; nobody has spent a round on it.

### PROGRAM RESONANCE — W2 "THE OPPONENT ACTS" (2026-08-30, details in DEVLOG §W2)

- [x] **CONTESTED ENEMY PARALYSIS MEASURED AND FIXED TO ZERO — at 6.2% / 3.8%, NOT the 32.4% this
      wave first published.** An idle count is unreadable without the standing-soldier split: when
      every surviving soldier is DOWNED, `Ai.Plan` returns an empty plan **by design** (`Ai.cs:78`,
      the FUL-7 rule that enemies do not execute bodies), so every hostile idles on a board where
      nobody can act. **86% of the raw rate was that bleed-out window.** Split properly, base
      `4784803`: pre-wave a CONTESTED act-opportunity idled **47/755 = 6.2%** at n=16 campaigns and
      **45/1195 = 3.8%** at n=32; post-wave **0.0%** in both. Two causes, and the first write-up had
      their sizes backwards — a dry weapon (89% of contested idles at n=16, 53% at n=32) and the
      missing terminal else (11% / 47%). **The split is not resolvable at these samples; both are
      real, neither dominates.**
- [x] **THE FIX'S OWN FEEL REGRESSION WAS CAUGHT IN REVIEW AND IS NOW GATED AND TESTED.** The first
      build's terminal else fired on all-downed boards too: **319 of 320 of its acts popped
      HUNKERED + SFX over a squad bleeding out**, tails up to 28 consecutive act-opportunities.
      Everything the wave added is now gated on `standing > 0`, and `SIGHTLINE_AIIDLETEST` asserts
      `actedDuringBleedOut == 0` on **both** legs. **No price round could ever have caught this**:
      the 800-campaign round re-run on the corrected binary is **byte-identical to the first
      build's on all 40 chunk pairs** (harness block stripped), because nothing an enemy does in a
      decided state can move a win rate. A CRN round prices consequences and is blind to feel
      changes confined to states whose outcome is already settled.
- [x] **THE TERMINAL ELSE ADDS NO POLICY AND NO RANDOMNESS — and now it also has to WIN.** The plan
      is re-targeted at the best tile among those needing the FULL two-action budget, tracked by the
      *same* per-tile scorer in the *same* pass (zero extra `Util.Rng` draws; PAIRTEST green with
      the dial on). Review caught that the first version dashed **unconditionally**, which is wrong
      by construction: the arm is only reachable when `bestTile` cost 0-1 actions, and `bestTile` is
      the argmax over ALL tiles including two-action ones, so `bestScore >= bestDashScore` always —
      **13 of 13 measured dashes were strictly worse, mean −12.7 points**, while the comment claimed
      the unit moved "exactly as its archetype terms already say it should". The comparison is now
      **move-cost-neutral** (every score carries `-actionsToReach * 6`, a term pricing an action
      that in this branch has no alternative use), and a dash that still loses digs in instead. It
      fires **5 of 9 offers** at n=32.
- [x] **THE HONEST SCALE OF THE WAVE, MEASURED AND REPORTED BY THE TEST ITSELF.** Over 32 campaigns
      / 1589 act-opportunities: 14 reloads + 25 terminal-else = **2.5% of all enemy
      act-opportunities, 3.5% of contested ones**. That is what the wave changes in live play. It is
      a real repair — a 6.2%/3.8% contested idle rate going to zero, plus an ammo economy that was
      never decided — and it is a very different claim from the one this wave first made.
- [x] **THE ENEMY AMMO ECONOMY IS A DECIDED DESIGN POSITION** (`docs/DESIGN.md` §5.1): a RELOAD verb
      (1 action, mirroring `DoReload`) over a per-turn clip refresh, on symmetry + decision grounds
      — **plus the read it requires**. Hostiles previously got one clip at spawn with no reload verb
      anywhere, so dry was permanent. The read: a pip row in the 4px band between the HP pips and
      the body, and **DRY as a status CHIP** (empty-magazine glyph + the word) in the late opaque
      pass. It lives there because the review found the first version — a hand-rolled pill below the
      body — **silently overpainted** by `DrawUnitStatusChips`, which owns p.Y+24..+42 and paints
      last: on any hostile with a status effect the pill lost 9 of 15 px and the pip row vanished,
      while this ROADMAP claimed "it does not collide". `SIGHTLINE_AIIDLESHOT` now STAGES that
      collision (DRY + BRN on one token) so the claim is checkable from one frame.
- [x] **THE READ IS GATED ON THE DIAL, so "one env var reverts the wave" is now true.** It was not:
      the ammo read had no `AiIdleFix` term, so with the dial off hostiles never reloaded but still
      wore a permanent DRY badge advertising a state the player could do nothing with — exactly what
      §5.1's "the read and the reload are one decision" forbids.
- [x] **THE PRICE IS SMALL, NOT ZERO, AND HEAT 0 WAS RE-PRICED ON 320 CAMPAIGNS BEFORE SAYING SO.**
      Round of record `R4-*`: 800 CRN-paired campaigns (5 rungs × four disjoint slot sets, bases
      0/10/20/30 × greedy+sloppy), base `4784803`, all 40 chunks asserting their own `runs`,
      preceded by an `R0diag` pair proving the dial-off leg is byte-identical to the base commit's
      own binary. Pooled run completion **25.2% → 24.2%** (p=0.644); no rung separates.
      **heat 0 got three EXTRA slot-set families** (b40-70, b900-930, b940-970 — `queue_h0.sh`)
      because review pooled four independent sets and found three negative: sixteen sets, **320
      campaigns per leg**, gives **50.3% → 47.5%, −2.8 points, discordant 27/18, p=0.233, 95% CI
      [−6.9, +1.3]**. The direction is more consistent than the significance and I will not call the
      sign noise — but **both legs are inside the h0 band** (55±8, floor 47.0), the fix moves no rung
      out of its band, and mission win-rate (78.94 → 78.56 over ~1400 missions) and soldier deaths
      per mission (**1.340 → 1.340**) agree with the null on ~10× the sample. So the dial ships ON.
      Raw data `docs/measurements/w2/`. **A four-slot-set read of h0 said 47.5 → 42.5 and put the
      shipped leg below the floor; sixteen sets say 50.3 → 47.5 and both inside. That reversal is
      the best argument in this wave for L1's method rule.**
- [ ] **OPEN, HANDED TO W7 — h2 is out of band; every other rung is in it.** The dial-OFF control
      reads **h0 50.3 (n=320) / h2 26.2 / h4 22.5 / h6 17.5 / h8 12.5 (n=80 each)**. The band is
      ±8, so **only h2 is convincingly outside** (−13.8 from centre ≈ 2.5 rung-SE; a rung's binomial
      SE at n=80 is ~5.6). The shortfall from centre runs −4.7 / −13.8 / −7.5 / −2.5 / **+2.5**,
      consistent with a curve flatter than the band — a hypothesis, not a result. This is a control
      leg, not a ladder of record.
- [ ] **OPEN, HANDED TO W3 — the enemy OVERWATCH branch is measured DEAD, and W2's repair to it is
      unexercised.** `Ai.cs` still scores any available shot at `100 + bestHit` against terrain terms
      bounded under ~64, so a lane-hold is only reachable when no reachable tile has ANY shot.
      Measured on this tree: an enemy act-opportunity ended holding an overwatch lane **0 times in
      1595 pre-wave and 3 times in 1589 post-wave** (the 3 are a cascade of the ammo gate changing
      which boards occur, not a designed effect). W2 added a `!Disoriented` plan/exec mirror to that
      branch — correct, and untested by any real play, because the branch does not fire. W3's whole
      premise is making it a real choice.
- [ ] **OPEN — should the bleed-out window have ANY presentation?** Hostiles now stand silent over a
      dying squad, exactly as pre-wave. This wave only refuses to answer that question with a
      chorus; it does not answer it.
- [ ] **OPEN — the ammo read is unmeasured as an affordance.** Nothing shows a player or the
      autopilot ever *baits* a hostile dry; `Game.Autopilot.cs` has no term for enemy ammo at all.

---

## PROGRAM CROSSCUT — CLOSED 2026-08-30. WHAT THE NEXT SESSION SHOULD PICK UP.

Eight waves merged, each independently reviewed, each sent back at least once. The composed-tree
ladder is `docs/measurements/l3/` and the write-up is DEVLOG §L3. Start here:

### The best-evidenced open findings, in priority order (C1 closed the first and opened three)

- [x] **THE FLAT MIDDLE — PARTLY closed by PROGRAM CONTOUR wave C1 (`docs/measurements/c1/`,
      DEVLOG §C1 + §C1-R). One lever shipped; the finding was sharpened and PART OF IT RELOCATED
      rather than removed.** Measuring all TEN rungs (n=320, n=640 at h6, base `17934ee`) showed
      "`h4 → h6` is flat" is really **two rungs, one of them exactly zero**: rung 5 buys
      **0.00 ±3.2**, rung 6 bought **2.34 ±2.7**, against six other rungs averaging 5.7. Mechanism:
      **rung 6 declared `AiTier = 1`, which could never fire** — rung 4 already publishes tier 1 and
      the aggregation is `Math.Max` — so EXPOSED's coordination tooth was dead for two programs.
      Shipped `Heat.MidTooth` (default **3**): BOTH of NO QUARTER's qualitative teeth move down to
      rung 6. **`h4 → h6` −2.3 ±2.7 → −7.0 ±2.7**; six rungs exactly unchanged (0/320 discordant
      each); h6 in band at 13.9, 1.4 SE clear of the floor. `SIGHTLINE_MIDTOOTH=0` restores the
      pre-C1 table; `SIGHTLINE_MIDTOOTHTEST` pins it.
      **What is still open here:** rung 8 now buys 1.6 (was 4.1) and rung 5 still buys 0.3 —
      **C1 fixed the second-flattest rung and left the flattest** — and NO lever beat the control on
      dispersion (SD of the 8 steps 2.36 control / 2.44 mode 3 / 2.89 mode 1). The rung sums are
      identical to the decimal in every mode, so this is an allocation problem, and the next two
      items are why it cannot be solved inside `Heat.Mods`.

- [ ] **THE LADDER'S TOP HALF HAS SUNK ONTO ITS BOTTOM — C1's closing finding, and now the
      biggest open number.** Against band centres, **h0 is 10.6 points low and h4 is 9.1 low**,
      while h6 and h8 are within 2. An apex-neutral `Heat.Mods` lever pins h4 (20.9) and h8 (8.1),
      leaving **12.8 points of win-rate for four rungs — 3.2 each** — so no redistribution inside
      the table can give rungs 5–8 the ~5.7 points per rung that rungs 1–4 buy. C1 could stop one
      rung taking almost none of it, and did; it could not create room that is not there.
      **This is a BASE-difficulty lever, not a heat-table lever.** Price it against the band
      (RECRUIT 75 / h0 55 / h2 40 / h4 30 / h6 20 / h8 10) and expect the whole ladder to move.
      **On the SHIPPED tree h6 is also 6.1 under its centre** — C1's lever added a third rung to
      this deficit list, so read it as h0 −10.6 / h4 −9.1 / h6 −6.1, not as the two-rung list C1's
      first draft wrote here (which described the CONTROL and told the next wave the wrong thing).

- [ ] **RUNG 5 (LINGERING WOUNDS) BUYS EXACTLY ZERO — 0.0 ±3.2 at n=320.** The deadest rung on
      the ladder, and C1 left it alone (one lever per wave). Two unmeasured hypotheses to test
      first: its `+1 enemy` is partly eaten by the **12-hostile spawn cap**
      (`Mission.cs:653`, `Math.Clamp(EnemyBaseCount + n + enemyDelta, 3, 12)`) on late missions,
      and `HarshAttrition` compounds over a run length the autopilot rarely reaches (avgMis 3.66
      at h5). **Instrument the delivered headcount per (heat, mission) before choosing a lever.**

- [x] **A "heat-N rung" IS NOT A FIXED RUNG, AND THE LEAK IS DIRECTIONAL — it biases the very
      finding this program is chasing.** `Events.cs:460` (`EventOutcomeKind.AddHeat`) lets three
      field-event choices raise a run's HeatLevel mid-campaign, +1 each, so every heat-N cell
      contains some heat-N+1 missions. **Heat 8 is clamped and cannot leak, so every rung below it
      is contaminated UPWARD**: the instrument systematically **compresses the top of the ladder it
      is being used to diagnose**, which means some part of the flat middle/top may be the
      INSTRUMENT, not the design. Quantified from C1's archive: h5 shows a 1.56% flip rate under a
      lever that can only bite at heat ≥ 6, implying **≈10% of h5 campaigns reach heat ≥ 6** (h4
      shows zero — from there the escalation must fire twice). C1's campaign-level inertness claims
      survive it (six rungs at 0/320 are exact), but **every past "this rung is unaffected" claim on
      this ladder has had this hole in it.** A clean fix exists and is cheap: a harness-only pin
      that suppresses `AddHeat` for a measured batch, so a rung means the rung. Do that BEFORE the
      base-difficulty round above, or that round re-inherits the bias.
      **DONE — wave THE HEAT PIN AND L5 (base `178464a`).** `EventCatalog.HeatPinned` makes the
      `AddHeat` outcome a reported no-op; every `SIGHTLINE_BALANCE` batch sets it unless
      `SIGHTLINE_HEATPIN=0`, and the JSON's `heatLeak{}` block says whether it held. Re-counted on the
      L4 archive (base `7315425`, 960 campaigns): **88 heat-raising picks, 134 of 3,377 missions (4.0%)
      played above their rung** — RECRUIT 4.2% / h0 4.9% / h2 5.6% / h4 4.6% / h6 3.6% / **h8 0.0%**.
      DEVLOG §THE HEAT PIN AND L5; the pinned ladder is L5 (`docs/measurements/l5/`).

- [ ] **SKIRMISH AT HEAT 6–7 IS UNMEASURED AND C1 CHANGED IT.** `Game.cs` gates the m1–2 heat grace
      on `Mode != GameMode.Skirmish` (W9's fix), so a heat-6 SKIRMISH now takes C1's +1 enemy damage
      **from turn one**, where before only heat 8 did. `SIGHTLINE_BALANCE` measures campaigns only,
      so no instrument in the repo has ever priced a skirmish. DAILY is safe (`DailyHeat` is `% 4u`,
      capped at 3); ENDLESS is safe (`EndlessWaveScale` reads `StatDelta`, never `DmgDelta`).
      **Two of four shipped modes have no measurement at all** — that is the wider item.

- [ ] **THE BRACE COMEBACK ECONOMY, flagged by the code and not yet priced.** `Mission.cs:890-902`
      warns that a wider AI finish band "leans AGAINST the BRACE comeback lever … the comeback
      economy is the first re-tune if lead-swings collapse", and C1 moved exactly that widening down
      two rungs. In C1's committed h6 sample pair lead-swings/match holds at 0.9, but average
      max-swing goes 59.6 → 55.8 and greedy BRACE usage **146 → 96 (−34%)**. n=40 and not
      conclusive. Re-measure at n≥320 before or alongside the next heat-table change.

- [ ] **THE MID-RUN DECAPITATE — replicated three times, mechanism located, lever unspent.**
      44.7% ±3.9 (n=161) against the finale's 68.0% (n=472): **23.3 points harder than the
      climax.** W8 refuted the first hypothesis (the HVT buff — the buffed half is *easier*) and
      located it in the **force**: `Mission.Build` de-stacks the finale by 3-4 bodies and resets
      `bump`; no mid-run Decapitate gets that, and an ELITE node adds +2 bodies and +1 stat.
      **The next lever belongs on the force de-stack, not on the target.**

- [x] **KILL OBJECTIVES ARE A DIFFERENT GAME FROM THE REST.** Taken by **PROGRAM CONTOUR wave
      C3 "THE TWO GAMES"** (branch `wave/two-games`, base `17934ee`; DEVLOG §C3, data
      `docs/measurements/c3/`). Re-established on that tree at **38.5 ±3.1 (n=247)** vs
      **81.5 ±1.1 (n=1243)** — a **43.0-point** gap. **Mechanism located and three rivals refuted
      by measurement**: not force size (the four untrimmed non-kill objectives deploy 8.14–8.96
      against the kill class's 8.34), not mission length (7.17 turns vs 6.73; Defend is the longest
      objective in the game and wins 78%), not reinforcement VOLUME (Defend takes 6.48 added bodies
      a mission and wins 78%; Eliminate takes 1.69 and wins 37%). What survives is the win
      condition: **a WON non-kill mission kills 25.1% of the force it deployed against — Evac 3.3%
      — so FIVE of the eight objectives are routinely won by declining the encounter.** (Not six:
      `Defend` kills 4.28 bodies a mission, 92% of Eliminate's 4.65 and more than any other
      non-kill objective — it does not decline its encounter, it outlasts a bigger one, and its low
      clear% is a denominator artifact of continuous replenishment.) Shipped one
      lever (the anti-turtle clock's reinforcement arm no longer fires on ELIMINATE, the one
      objective where an added body is also win condition) and one information change (the campaign
      fork now names the class: PITCHED / TASKED). Gap **43.0 → 36.5**; all six rungs in band;
      CRN-paired 26 lever-only wins to 2 over 960 pairs, p<0.0001 — **but ~half of that is the
      mission-1 change, and holding the opener fixed the honest figure is +1.39 (p=0.0024)**. See
      the top item above: the opener overshot and needs backing out with a re-measured ladder.

- [ ] **TOP ITEM — C3 OVERSHOT THE OPENER. RE-TUNE `Mission.OpenerTrim` AND RE-MEASURE THE LADDER.**
      C3 removed the anti-turtle clock's reinforcement wave from ELIMINATE. Mission 1 is always an
      Eliminate, **22 of the 24 baseline mission-1 losses in 960 campaigns were that wave**, and
      combined with X2's `OpenerTrim` (one body off the opening force) the opener became a fixed
      force that cannot grow against a full squad. Measured result: mission-1 losses per 160 went
      RECRUIT 0→0, h0 7→**0**, h2 7→**0**, h4 5→**0**, h6 2→1, h8 3→1 — **640 consecutive campaigns
      at RECRUIT/h0/h2/h4 with zero mission-1 losses.**
      **The design position is that this is too far.** DESIGN §3.D's front-loaded-anxiety half was
      already discharged by X2 (97.5% is not anxiety); its "too easy → boredom" half is the live
      risk, and pillar 5 "stakes that bite" is on the far side of a 100%-over-640 opener. §3.G's
      "low-stakes first mission" stopped justifying it when RESONANCE T1 moved onboarding into
      `GameMode.Training`. And X2 itself cited RECRUIT's "100%, zero losses in 40" as the
      **easiest-difficulty control** — every rung up to h4 now matches that control.
      **`Mission.OpenerTrim` is an existing, measured dial** (shipped 1; `SIGHTLINE_OPENERTRIM=0`
      is the pre-X2 opener), so this is ONE lever — but it is a lever **plus a re-measured ladder**,
      because C3's stratified paired test shows h2's gain is 58% opener and h4's is **100%** opener:
      backing the opener out will move both rungs down. Do not ship it without the round.

- [ ] **Add a CAMPING POLICY to the flywheel.** The instrument has exactly two policies, `greedy`
      and `sloppy`, and `sloppy` is an **error** model, not a **passivity** model — neither camps,
      and `SmartStep` hunkers only as a terminal fallback. **No policy in the instrument can reveal
      a turtle exploit**, so nothing this project has ever measured bears on turtling. That matters
      now: C3 removed the reinforcement arm from the 41% slowest Eliminates — precisely the missions
      where a player was already taking their time — leaving the aim arm alone as the
      disincentive, at a measured mean high-water rung of 1.50 (below the rung 2 at which the wave
      arm fired). **Until a camping policy exists, no wave can price that, C3's included.**

- [ ] **OPEN, left by C3 — the class gap is 36.5 points. The PRICING half is DONE; the rest is not.**
      1. [x] **Price the PITCHED node.** DONE by wave THE FORK PAYS (2026-09-03, DEVLOG
         §"THE FORK PAYS"). `Run.ClassPremium` pays **+8** on a Combat/Elite node whose card is a
         kill objective, and the hover, the node label surface and the deploy card PRINT it
         (`+32 intel  (+8 PITCHED)`). `SIGHTLINE_FORKTEST` leg B is the gate and fails on the
         pre-change tree with `B:seed6 c4 Combat pitched30-tasked30`. **The wave could not measure
         whether the price is the RIGHT price** — see the new instrument item below.
      2. [ ] **DECAPITATE's half of the class is untouched.** C3 kept off it deliberately so the round
         had a within-class control. The mechanism is still W8/L3's: `Mission.Build` de-stacks the
         FINALE by 3-4 bodies and resets `bump`, and no mid-run Decapitate gets that.
      Also unbuilt: a bot dial that forces engagement on non-kill objectives, which is the only way
      to separate "the design lets you decline" from "`Game.SmartStep` chooses to".

- [x] **SUPPLY strictly dominated COMBAT — closed by wave THE FORK PAYS (2026-09-03).** The lighter
      fight also paid MORE (`NodeIntel`: SUPPLY base+10 against COMBAT's base; measured Supply 94.4%
      vs Combat 89.2% clear, W1), and `Game.EnterBarracks` applied the SUPPLY full heal BEFORE
      `Run.DebriefSurvivors`, whose fresh-wound gauge reads `u.Hp` — so **no soldier who finished a
      SUPPLY clear on their feet could ever be wounded by it.** SUPPLY now pays `Run.SupplyDiscount`
      (**−6**, the heal IS the reward), ELITE keeps its +14 top premium, and the heal is passed into
      `DebriefSurvivors(bool fullHeal)` and applied AFTER the gauge. `Run.DepthBase` moved 10 → 12 in
      the same wave so the two price changes REDISTRIBUTE the routing economy instead of deflating it
      by ~7% (round 1 measured that deflation at h0: 53.1 → 46.9; DEVLOG has the arithmetic).

- [ ] **NEW INSTRUMENT GAP, opened by THE FORK PAYS: the flywheel's route picker cannot price a
      fork.** `Game.Autopilot.PickAutoNode`'s shipped policy is *"prefer an Event node, else take
      `nn[0]`"* — the lowest row of the next column — and `SIGHTLINE_ROUTE=hash`, its only
      alternative, is a UNIFORM deal. **Neither reads `MissionNode.Intel` or the objective class**,
      so the bot walks past a PITCHED premium without seeing it and eats a SUPPLY discount without
      choosing it. THE FORK PAYS' 640-campaign round is therefore a not-a-regression check
      (h0 −3.1 ± 3.59, h4 +6.2 ± 4.39, both unresolved) and **explicitly not a price for the fork.**
      A VALUING policy (`SIGHTLINE_ROUTE=greedy-intel` / `safe`, plus a run-level "intel banked"
      readout already in `intelByHeat`) is the prerequisite for anyone tuning the three constants
      `Run.SupplyDiscount` / `ElitePremium` / `PitchedPremium`. **This sits beside C3's camping
      policy: both are design questions that are UNMEASURABLE rather than unmeasured.**

### Handed on from C2's review (not C2's code, recorded so it is not lost)

- [ ] **`Hud.DrawThreatCard` ignores the TEXT SIZE setting entirely.** It calls
      `Raylib.MeasureTextEx`/`DrawTextEx` directly (`Hud.cs:2317-2331`) instead of going through
      `Cfg.Text`/`Cfg.Measure`, so the INCOMING FIRE card renders pixel-identically at 90/110/120%.
      It does NOT overflow — measure and draw are consistently unscaled — it simply never scales,
      stranding its body at the 12px floor while every other surface grows. **Pre-existing at
      `17934ee`, not introduced by C2**; found independently by two reviewers on the same card.
      Routed to the wave that owns the text-scale surface.

### The methodological rules this program had to learn the hard way

1. **A pooled row can hide a 49.5-point artifact.** `Eliminate` reads 89.1% pooled and ~40% over
   its mid-run cells, because the row is largely 960 mission-1s. **Always use W8's
   `byObjectiveByNodeKind` / `byObjectiveByMission` cross-tab before concluding anything from a
   per-objective table.**
2. **A rung is four slot sets or it is not a rung.** Proposed by L1 on cost grounds *after* the
   finding that motivated it was retracted — then it decided a shipped default in W2, where four
   slot sets put a leg below the band floor and sixteen put it inside.
3. **A CRN round prices CONSEQUENCES and is structurally blind to feel** in already-decided
   states. W2 removed 320 actions and 319 audio pops from the bleed-out window and all 40 chunk
   pairs came back byte-identical. If a change only affects a state where no soldier can act, the
   flywheel cannot see it and you need eyes.
4. **Count NAMES, not line shapes.** The sweep's test counter has been wrong six times, the last
   two because a correct fix and a correct routing change composed into a broken one.
5. **A test that cannot fail is not a test.** Three waves shipped one — `BANDTEST` pinned constants
   but no additive-mode behaviour, `TRUTHTEST` asserted the values the HUD *should* read rather
   than the panel, `BRIEFTEST` read the model predicate and never observed the draw. Every one was
   caught by reverting the defect and watching the test still pass. **Do that to your own tests.**
6. **A correct assertion in the wrong scope is indistinguishable from no assertion.** W5 wrote the
   right guard for the longest string on the squad screen and placed it outside the scale loop.

### Found by CONTOUR C3 while running its own gate — fixed, and worth knowing

- [x] **A 2.1% HARNESS-FORCED-LOSS FLOOR NOBODY OWNS.** `instrumentHealth.stalemateLosses` reads
      **20 of 960 in both C3 arms and 20 of 960 in the L3 archive** — identical, because it is a
      property of the worlds, not of any lever. Every published ladder figure sits on top of it and
      no wave has ever mentioned it. Somebody should find out what those campaigns are doing.
      **Corrected (THE HEAT PIN AND L5): on L4 (base `7315425`) it is 16 of 960 = 1.67%, not 2.1%
      (RECRUIT 7 / h0 3 / h2 3 / h4 3 / h6 0 / h8 0). The guard now logs WHICH arm fired
      (`STALEMATE-MISSION` / `STALEMATE-RUN`) with the mission, objective and run-turns on the row —
      L5's README says what the campaigns were doing: **27/1,920 = 1.41% on L5, ALL on the mission arm
      (the run arm fired 0 times in 5,440 campaigns); RECRUIT 4.4%, the bot's Escort/Evac finishing line
      late in a long run (Escort 10, Eliminate 9, Evac 4, Rescue 3, Sabotage 1; missions 3-4 hold 18 of
      27), plus one sloppy-policy mission-1 Eliminate world (slot 46) that deadlocks at h0, h2 and h4.
      Ex-stalemate no heat rung moves more than 0.6. Sized and located; the fix is autopilot work.**
- [x] **`scripts/qa-sweep.sh` had no `exit` statement.** It accumulated `_fail` / `_autofail` and
      ended on an `echo`, so `SWEEP-EXIT` was 0 whatever happened. Every green-sweep claim dated
      before C3 quotes the exit code of an echo. Wired, and `AIIDLETEST` — the one self-test line
      not routed through `verdict` — was routed. **If you add a self-test line, route it through
      `verdict` or the gate cannot see it.**
- [x] **A PROOF COMMAND THAT COMPARES NOTHING MUST NOT PRINT A PASS.** C3 documented
      `samearm.py D0 B1` as its instrument-identity proof and then deleted the D0 round as
      redundant, so the command matched zero files and exited 0 over no data — the same defect C1
      shipped in `inert_diff.py` this program, and exactly what CLAUDE.md's W1 contract exists to
      prevent. `samearm.py` and `inert.py` now exit 2 on an empty comparison, and the claim was
      replaced with a stronger one whose data is committed (`l3repro.py`: C3's baseline arm
      reproduces the L3 archive on 59,010 aggregate fields and **960/960 campaigns**).
      **Check every proof command in your own write-up still finds its data after you tidy up.**
- [ ] **A DRAW-observing test has two failure modes this project had not written down**, and C3's
      own gate hit both: state left on screen by the previous case (a parked cursor makes the next
      case draw a tooltip), and a rect read from a PREVIOUS frame that has since moved
      (`DrawBarracks`' 0.15 s slide-down entrance shifts the whole map up to 16 px, which made the
      hover leg fail 3 runs in 4 under `dotnet run -c Debug` and 0 in 8 on the Release binary).
      **Run a new draw test under BOTH the Release binary and the sweep's own
      `dotnet run -c Debug`, several times each.**

### Found by PROGRAM PARALLAX while landing its first waves

- [ ] **MAGMA's vent count has a thin tail the pinned BIOMETEST cannot see.** Before the lead
      pinned the real-board loop's `Util.Rng` stream (DEVLOG §PARALLAX gate fixes), six runs of the
      unpinned guard on `cee3cba` failed once with `realMin[MAGMA]=2` — so on the order of one
      MAGMA board in ~240 stamps fewer than 4 vent tiles, which is C4's own "mechanic that silently
      vanishes on some seeds". The fix is in `Terrain` (a guaranteed minimum fissure length, or a
      re-walk when the first pass lands under the floor), priced CRN-paired; the guard should then
      sample more boards, not fewer.

- [x] **CLOSED by the PARALLAX lead — the COVERAGE GUARD could go quiet two ways.** The sweep's
      guard is the project's only structural defence against a self-test that exists and never
      runs, and CLAUDE.md's whole "DO NOT WRITE A COUNT HERE" doctrine defers to it. Its alphabet
      was `(TEST|GATE)` while its own header comment prescribed `(TEST|GATE|PROBE)` plus a
      hand-written `+1 for FUL11PROBE`, so a real assertion hook the sweep really runs was in
      neither count and any future `*PROBE` was invisible by construction; and its "is it run?"
      side grepped the whole script, so naming a hook in a COMMENT there marked it covered. Both
      demonstrated live with throwaway fixtures before the fix (the old recipe reported an empty
      gap in both cases, the new one named the hook), then removed. Now `TEST|GATE|PROBE` on both
      sides over non-comment lines, with a named `_SWEEP_EXEMPT` for report-shaped probes
      (`BANDPROBE`). Counts 77 -> 78 exist / 78 run, the one added name being `FUL11PROBE`.
      **A hook whose name ends in none of those three is still invisible — end it in one.**
      DEVLOG §PARALLAX LEAD NOTE.

### Standing gaps, honestly declared

- [ ] **A FOUR-SLOT-SET RUNG IS NOT INTERCHANGEABLE WITH ANOTHER FOUR-SLOT-SET RUNG — measured.**
      C2 found this while checking its own baseline: because `src/` is byte-identical between
      `d814f0c` and `17934ee`, its BASE arm turned out to be **L3's b0-b30 replayed bit for bit**,
      which makes L3's two halves directly comparable. At heat 2 they disagree —
      **b0-b30 reads 21.2% and b40-b70 reads 41.2%** (17/80 vs 33/80), difference +20.0, SE 7.16,
      **z = 2.79, p = 0.0052**, and 0.031 after Bonferroni x6. **L3's slot space is heterogeneous
      at heat 2 beyond binomial noise**, so the ladder-of-record's h2 = 31.2% is an average over
      two populations twenty points apart. CLAUDE.md's rule "a rung is four slot sets or it is not
      a rung" is about SIZE; this is about WHICH FOUR. An UNPAIRED comparison across different slot
      sets can therefore be badly misleading at h2 even at n=80 — a CRN-PAIRED one is immune,
      because the effect hits both arms in the same worlds and cancels. **Prefer paired designs;
      if you must compare unpaired rungs, use the same bases.** Raw data `docs/measurements/l3/`.
- [ ] **A CRN round's resolving power comes from its DISCORDANT count, not its n, and nobody had
      been reporting it.** C2's 80-pair rungs can only detect swings of 11-14 points (17-19 family
      wise), and its heat-8 rung had **4 discordant pairs** — an exact two-sided minimum p of
      0.125, so nothing was reachable there at any effect size. A "p = 1.000, no change" row at a
      hard rung is an absence of evidence. **Report n_discordant and the MDE beside every paired
      ladder**, or the flat rows will keep being read as neutrality. `docs/measurements/c2/paired.py`.

- [x] **CLOSED (mechanically) by PROGRAM PARALLAX wave P10 "THE HELD LANE" — THE ENEMY OVERWATCH
      NOW HAS LANE SELECTION.** `Ai.ChooseLane` scores the eight compass axes by the approach
      ground each would cover — the union of the squad's move-radius discs, filtered to tiles the
      watcher can genuinely react on (weapon range + commanding LoS, the same pair
      `OnUnitEnteredTile` gates on) and weighted by soldier proximity — and the ordinary-overwatch
      exec arms the player's own `OwFocused` cone with it. The reaction site is already
      team-symmetric, so the hostile takes the identical FOCUS trade the player takes:
      `+Combat.FocusOwAim` inside the lane, blind outside it. `SIGHTLINE_AILANE=0` restores the
      pre-P10 opponent exactly; `SIGHTLINE_LANETEST` is the gate. **But read the next item before
      quoting this one as a fix — the mechanism landed and the effect did not.**
- [ ] **OPEN, AND SHARPENED BY P10 — THE ORDINARY ENEMY OVERWATCH IS STARVED, NOT BADLY AIMED.**
      P10 gave the branch a chosen lane and measured it CRN-paired (base `4c1ca3a`, 3 rungs x 8
      slot bases x 40, n=320/rung/arm, 1,920 campaigns, `docs/measurements/p10/`): **h4 and h8 came
      out with ZERO discordant campaigns in 320 — the two arms produced literally identical
      outcomes in every world — and h0 with 2 of 320 (0/2, exact p = 0.50).** The cause is in the
      telemetry beside the ladder: the `overwatch` branch fires **84 / 41 / 102 times in 21,874 /
      27,708 / 22,226 enemy acts (0.38% / 0.15% / 0.46%)** at h0/h4/h8. A quality improvement to a
      verb taken once in every 300-700 acts has nothing to move. **The open lever is FREQUENCY, and
      `Ai.DeclineWatchRatio` is priced but NOT spent** — `SIGHTLINE_DECLINEWATCH=<ratio>` (default =
      the shipped 0.45, and `R0diag.json` proves the dial inert at the default). See
      `docs/DEVLOG.md` §THE HELD LANE for what the probe measured and why P10 did not spend it.
      C2's original figures stand as history: a held lane FIRED 24-27% of the time (n=457/461 lanes
      in the maximal-decline diagnostic), *fired* and not *paid off*, so an overwatch was worth at
      most ~0.20 of the shot it replaces; and a decline rate large enough to be *felt* also looked
      like a weaker opponent (51% declines, run completion 55% → 75%, p = 0.29 on 20 paired worlds,
      confounded by an interim binary). **P10's own paid-off column: 20.1% / 15.4% / 32.4% of lanes
      held drew a reaction shot, lane on — statistically indistinguishable from lane off
      (21.8% / 15.4% / 32.4%), on 204 / 175 / 216 lanes per rung.**
- [x] **CLOSED by P10 — the enemy kill-zone wash is perceptible AND selective.** The alpha went
      **0.07-0.12 -> 0.11-0.16** (mean 0.095 -> 0.135, +42%), still a tier below the player's own
      braced lane (0.14-0.21, mean 0.175) so a plan the player made out-reads a plan the opponent
      made; and `DrawConeRays` — the cone-edge rays and direction chevron that were PIKEMAN-only —
      now draw for any focused enemy watcher. The alpha could not be raised BEFORE the cone existed
      and that was the real blocker, not the number: `SIGHTLINE_LANETEST` leg (d) measures it on a
      staged board and a 360 watch marks **59.1% of the floor** against a chosen lane's **28.4%**.
      Leg (c) is what the raise rests on — the wash predicate is now the single shared
      `Game.WatchCovers` (the red wash, the friendly cone wash, `Threat[].Watched`, the bot's
      `InEnemyBraceLane` and `PlayerOverwatchTiles` all call it), and the leg walks every floor tile
      of a live mission board against the REAL `OnUnitEnteredTile` in BOTH directions: 358
      tile-checks, 0 washed tiles that draw nothing, 0 reactions on unwashed ground.
      (C2's `SIGHTLINE_DECLINESHOT` doc comment claimed the wash "lights the ground it now denies";
      that claim was withdrawn in the code and is true again now.)
- [ ] **The flywheel is structurally blind to enemy area denial.**
      `Game.Autopilot.TileExposure` carries `+18` for an enemy BRACE lane (`InEnemyBraceLane`) and
      **no term at all** for an ordinary enemy overwatch — its "exposed to this gun" `+6` is
      identical whether the hostile is watching or not. So the bot walks into enemy kill-zones and
      the denial half of a lane cannot be priced. Fixing it changes the INSTRUMENT and therefore
      invalidates every CRN world in the repo, so it must be a wave of its own with an R0diag and
      a fresh ladder, never smuggled into a gameplay wave.

- [ ] **W10's text-scale gate covers five surfaces, not the game.** Every other screen is still
      asserted at 100% only. `FITTEST` is written so a sixth leg is an addition, not a rewrite.
- [ ] **The enemy OVERWATCH branch is effectively dead** — 0 of 1595 pre-W2 and 3 of 1589 post.
      W3's premise (overwatch as a real enemy choice) is therefore unexercised.

- [x] **CLOSED by C5 "THE HARD EDGES" — W10's text-scale gate now covers the game.** `FITTEST`
      leg (F) stages **40 screens**, DRAWS each at all four shipped text sizes, and reads the
      geometry back from the draw calls (`Cfg.InkProbe` / `Hud.PlateProbe` / `Hud.ClipProbe` /
      `Hud.FloorProbe`) rather than re-deriving layouts in the test. Asserts plate containment, no
      ink off canvas, **no ellipsis**, the type floor, and that each screen drew its own frame.
      FITTEST also gained **THE SCOPE GUARD** (rule 6, made mechanical): it counts assertions per
      leg per scale and fails by name on any leg that did not run at every scale — 13,898
      assertions over 45 legs today.
- [ ] **The enemy OVERWATCH branch is effectively dead — and C5 corrected the number while
      building the test for it.** Measured on the composed tree over **8083 enemy acts**: overwatch
      fires **8 times, 0.10%** — not zero, but once per thousand acts, which is a verb no player
      will see. The CAUSE is still open and belongs to C2/W3. What C5 added is
      `SIGHTLINE_AICOVTEST`, the enemy DECISION CENSUS: every branch of the enemy exec chain tags
      itself, the gate is a RATE (once per 1000 acts, because a zero gate would not have caught
      this), and the effectively-dead branches are a declared registry that fails by name when an
      UNDECLARED branch goes quiet.
- [ ] **W3 THE OPPONENT CHOOSES was never started.** `Ai.cs` still scores any shot at
      `100 + bestHit` against terrain terms bounded under ~64, so the opponent now always ACTS but
      still never DECLINES. That is the single biggest remaining gap in the fight.
- [ ] **W6 (biome mechanical) was never started.** `grep -ci biome` still returns 0 in
      `Combat.cs`, `Ai.cs`, `Grid.cs` and `Unit.cs` — eight biomes are paint.
      (**W7 ships-like-a-product is now DONE** — CONTOUR wave C6, section at the end of this file.)

- [x] **The enemy OVERWATCH branch is effectively dead** — 0 of 1595 pre-W2 and 3 of 1589 post;
      CONTOUR C2 re-measured it at **0 of 1076** contested acts on `17934ee` and it is no longer
      zero. **But see the new open item below: the branch is alive and the LANE is still bad.**
- [x] **W3 THE OPPONENT CHOOSES** — shipped as PROGRAM CONTOUR wave **C2 "THE OPPONENT DECLINES"**
      (`SIGHTLINE_AIDECLINE`, default ON; DEVLOG §C2, raw data `docs/measurements/c2/`). The flat
      `100 + bestHit` is gone: the tile term is now `ShotSeat(18) + bestHit * P(hit)` and a decline
      gate prices the shot against the same shot with the defender's cover stripped
      (`Combat.AsIfExposed`), with the bar rising with the guns already trained on the tile.
      **Read the DEVLOG before quoting the wave**: the brief's "shoots at ANY hit chance" half was
      NOT supported by measurement (0 of 649 pre-change shots were under 20%; 88% were at 60%+) —
      the defect was entirely positional, and that is what moved.
- [ ] **W6 (biome mechanical) and W7 (ships-like-a-product) were never started.** `grep -ci biome`
      still returns 0 in `Combat.cs`, `Ai.cs`, `Grid.cs` and `Unit.cs` — eight biomes are paint.
- [x] **CLOSED by C5 — the enemy turn has a deadlock guard.** `Game.EnemyStallGuard` runs from
      `Update` BEFORE the animation pump (a guard inside `UpdateEnemy` is structurally blind to an
      animation that never completes, because `Update` returns before the phase switch while the
      queue is non-empty). After 480 updates with nothing moving it prints the unit, class, stage,
      planner branch, queue head, turn and mission, then forfeits the stalled unit so the turn
      ends. It runs in REAL PLAY, not only in autoplay. `SIGHTLINE_ENEMYSTALLTEST` wedges a real
      enemy turn to prove all of it, including the leg that makes the rest mean something: the same
      wedge with the guard off still hangs.

- [x] **BIOME MECHANICAL — done for THREE of eight** (PROGRAM CONTOUR **C4 "EIGHT BIOMES ARE
      PAINT"**, base `17934ee`). `src/Terrain.cs` adds a per-tile GROUND layer stamped from
      `(MapSeed, mission)` via `Util.Hash3` (zero `Util.Rng` draws; PAIRTEST byte-identical), and
      three biomes now change the fight on three different axes: **VERDANT UNDERGROWTH** (low
      cover from every angle, but only past 2 tiles — `Grid.GetCover`), **TUNDRA SLICK ICE** (half
      a step to cross — `Grid.CostMap`), **MAGMA THERMAL VENTS** (opaque, dear to cross, and it
      sets you alight — `Grid.HasLineOfSight` + `CostMap` + `OnUnitEnteredTile`). Symmetry is
      structural: every rule lives in a function both sides already ask for the truth, so `Ai.cs`
      cannot play the old game. `SIGHTLINE_BIOMETEST`; `SIGHTLINE_BIOMEMECH=0` restores the pre-C4
      board exactly. Measured CRN-paired at n=160/rung/arm on h0/h2/h4: heat 0 reads 43.8 vs 47.5,
      a −3.7 point estimate that crosses the band floor but is **not a measured breach**
      (chunk-paired t = −1.07). DEVLOG §C4; raw round `docs/measurements/c4/`.
- [ ] **The other FIVE biomes are still paint.** STEEL / ARID / ASH / VOID / NEON change nothing;
      BIOMETEST asserts they stamp nothing, so giving one a mechanic means changing that assertion
      deliberately. The obvious candidates follow the art: ASH = short sight lines in the ashfall,
      ARID = dune ridges as soft high ground, NEON/VOID = the lattice as a conductive/teleport
      grid. None designed, none measured.
- [ ] **THE GROUND LAYER'S COST IS CONCENTRATED ON MISSION 1 — the only cell C4's round could
      individually resolve, and a DESIGN.md §3.D violation.** Splitting the archived round by
      campaign node kind (`aggregate_nodekind.py`): **`Start` A 91.7% vs B 96.0%, chunk-paired
      −4.37, SE 0.97, t = −4.53** over 24 chunks (n=480/arm), against Combat +1.13 (t=+0.60),
      Elite −3.98 (t=−1.16), Supply −0.27, Boss +4.10. `Start` is mission 1, exactly one per
      campaign, so it carries no within-campaign clustering — which is why it resolves while the
      pooled rung does not. `byObjective` agrees: **Eliminate −3.55 ± 1.12, t = −3.16** (m1's
      objective in the baseline rotation). This is the front-loaded anxiety §3.D forbids and that
      X2's `Mission.OpenerTrim` exists to prevent, and it points at a **cheaper, more targeted
      lever than any of the three C4 named**: suppress or thin the ground layer on mission 1.
      Unpriced.
- [ ] **The C4 ladder is now STALE by construction.** Its review pass moved the layer off plateaus
      (M1) and re-tuned density (VERDANT 12.1% → 17.7%, MAGMA mean 9.7 → 13.0), so the shipped
      layer is not the measured one. Re-measure before quoting `docs/measurements/c4/` as the
      price of what ships. Other unpriced levers: the tile budgets (44 / 34 / 24),
      `FoliageMinDist`, `VentStepExtra`.
- [ ] **"A symmetric rule is not a neutral rule" is a DIRECTION, not a result.** The three
      mechanical boards moved −3.2 ± 2.1 in mission win rate against a flat +0.3 ± 1.7 paint
      control, but chunk-clustered the difference-in-differences is **−3.19 ± 1.98, t = −1.61,
      sign test 17/24** — not resolved at n=160. Per rung only MAGMA is consistently negative;
      TUNDRA flips at h2 (+1.9) and VERDANT at h4 (+5.7). C4's hypothesised mechanism ("a longer
      exchange favours the side with more bodies") is **refuted in C4's own archive**: per-biome
      `avgTurns` reads MAGMA 6.36 / TUNDRA 6.18 / VERDANT 5.58 against paint biomes 5.36-6.47, so
      the longest-fight biome in the batch has no mechanic at all.
- [ ] **A quarter of MAGMA boards have no vent-free route.** Treating vents as walls, 47/200
      (23.5%) of MAGMA boards leave an objective fixture or hostile unreachable without a crossing,
      so "the gaps are the fords" is true on most boards and not on all. And a vent step costs 8
      half-tiles — a full-mobility soldier's ENTIRE walk — so a WOUNDED soldier (budget 6) cannot
      enter one at all; 15 tiles across 200 boards have every walkable neighbour a vent, where a
      wounded soldier can never move again (it can still shoot, so no stall). Recorded, unrepaired.
- [ ] **The Escort VIP leash treats vents as a score penalty, not as walls.** Fire gets a two-pass
      leash (an `avoidFire` pass first); vents get penalties only, and a penalty is not a veto, so
      the asset can be routed through a vent and seared. C4's archive shows no measurable harm
      (Escort −0.74 ± 1.99) but the two hazards are not handled symmetrically.
- [ ] **`Hud.DrawThreatCard` (`Hud.cs` ~2333-2347) draws with raw `Raylib.DrawTextEx` at fixed
      12/14px**, bypassing `Cfg.Text` and therefore the TEXT SIZE setting — which CLAUDE.md
      forbids explicitly. Pre-existing (W10's FITTEST does not cover this card), but C4 put its
      longest, most rule-dense strings on it. A C5 surface.
- [ ] **W7 (ships-like-a-product) was never started.**
- [ ] **A deadlock inside `UpdateEnemy`** would still be bounded only by the frame cap; W9's idle
      guard covers the player turn only.
- [ ] **On-device audio** still needs the owner: nobody has heard this game.

---

## PROGRAM CONTOUR — OPENED 2026-08-30 on base `17934ee`. THE SHAPE OF THE FIGHT.

CROSSCUT measured the ladder and found its LEVEL was fine — five of six rungs in band. This
program is about everything that measurement could not price: the ladder's *shape*, the
opponent's *decisions*, the 45-point gap between objective *classes*, eight biomes that are
*paint*, and a build nobody has ever launched the way a player would.

Six waves, all from `17934ee`, each independently reviewed before merge:

- [ ] **C1 THE FLAT MIDDLE** (`wave/flat-middle`) — `h4→h6` is the smallest step on all three
      measured ladders and `h2→h4` joins it on the composed tree, while the ends buy 3-6× as much.
      `Heat.Mods` explains it: rung 8 is the only entry carrying `DmgDelta` or `AiTier`, so the
      middle rungs add bodies and stat points and only the apex changes KIND.
- [ ] **C2 THE OPPONENT DECLINES** (`wave/opponent-declines`) — `Ai.cs:537` scores any available
      shot at `100 + bestHit` against terrain terms bounded under ~64. The opponent always acts
      and never *declines*, which is also why the enemy overwatch branch fired 3 times in 1589
      turns. The handoff calls this the single biggest remaining gap in the fight.
- [ ] **C3 THE TWO GAMES** (`wave/two-games`) — kill objectives 38.3% ±3.1 (n=248) vs 83.4% ±1.0
      (n=1259) for the six with a non-combat win condition. A gap between objective *classes*.
- [ ] **C4 BIOME MECHANICAL** (`wave/biome-mechanical`) — `grep -ci biome` is 0 in `Combat.cs`,
      `Ai.cs`, `Grid.cs` and `Unit.cs`. W4 gave the biomes a visual identity; this is the half
      that makes the place change the fight.
- [ ] **C5 THE HARD EDGES** (`wave/hard-edges`) — `FITTEST` covers five surfaces, not the game;
      the `UpdateEnemy` deadlock is bounded only by the frame cap; a dead branch should be a loud
      coverage failure, not a discovery three programs later; plus a defect hunt on the composed
      tree, which no wave has swept.
- [ ] **C6 SHIPS LIKE A PRODUCT** (`wave/ships`) — "builds clean and passes autoplay" is a
      development standard, not a product one. Nobody has launched this game from outside the
      source tree with an empty profile. The one time anyone checked a neighbouring case, a
      published build silently lost its font.

**The rules carried in from CROSSCUT** (each learned by shipping the mistake): a pooled row can
hide a 49.5-point artifact, so cross-tab; a rung is four slot sets or it is not a rung; a CRN
round prices consequences and is blind to feel; count NAMES not line shapes; a test that cannot
fail is not a test; and a correct assertion in the wrong scope is indistinguishable from no
assertion.

### CONTOUR interim finding — THE GATE WAS A REPORT, NOT A GATE (found by C3, verified by the lead)

`scripts/qa-sweep.sh` accumulated `_fail` and `_autofail` and then **ended on an `echo`. There was
no `exit`.** `_autofail` even printed "DO NOT MERGE" — and the script still exited 0. Verified
directly on base `17934ee`.

**So CLAUDE.md's claim was false:** *"Since W9 the sweep EXITS NON-ZERO on any FAIL line, a
non-empty COVERAGE GAP, a TIMEOUT or a missing RESULT line, so it is a gate rather than a report."*
It was a report the whole time, and **every green-sweep claim quoting `SWEEP-EXIT=0` — including
all eight PROGRAM CROSSCUT merges — was quoting the exit code of an echo.**

**What this does NOT invalidate, stated precisely.** A hardened re-run on `main` @ `17934ee` reads
build 0 warn / 0 err, **FAIL lines 0**, autoplay **3/3** WIN|LOSE, `PAIRTEST: PASS`. `main` is
genuinely green. The CROSSCUT merges were gated by *reading the printed output*, not by the exit
code — which is exactly why the W9 merge defect was caught: every autoplay leg printed
`<no RESULT line>` and the merge was refused on that text. The earlier write-up said "the sweep
correctly refused"; it did not. The automation was hollow, the inspection was real. Both halves of
that sentence matter.

C3 ships the fix (`[ "$_fail" = 1 ] && _rc=1; [ "$_autofail" = 1 ] && _rc=1; exit $_rc`) plus
`AIIDLETEST`, which was the one sweep line not routed through `verdict`.

**Consequence for anyone reading an archived measurement:** a wave's "SWEEP-EXIT=0" line is
evidence of nothing on any tree before C3. The FAIL-line count, the three RESULT lines and the
`PAIRTEST: PASS` line in the same log ARE evidence. Read those.

### CONTOUR interim finding — a bare `.gitignore` rule silently ate three waves' runners

`.gitignore:57` carries a bare `run_chunk.sh` pattern from an earlier wave's scratch cleanup.
**C1, C2 and C4 each cited a `run_chunk.sh` in their archive README and each silently failed to
commit it**, leaving their archived runners inoperable; C2 also lost `qa*.txt` and `shots/*.png`.
Those rules are indiscriminate because `.gitignore` reached `main` carrying unresolved merge
conflict markers (lines 74/93/104) — a lead merge defect, fixed by C6. Narrow the rule and re-check
every wave archive after that merge.

## PROGRAM CONTOUR — wave C6 "SHIPS LIKE A PRODUCT" (CLOSED 2026-08-30, branch `wave/ships`)

The "ships-like-a-product" item declared never-started above is **done**. Full write-up in
`docs/DEVLOG.md` §C6; the shipping contract in `docs/DISTRIBUTION.md` was re-measured from scratch.

- [x] **Publish for real, all five configurations.** `release` / `small` / `no-trim` / `plain` for
      linux-x64 plus a `--rid win-x64` cross-publish; every one verified by three self-tests run
      against the binary it just built. Matrix re-measured **interleaved** (DEVLOG §C6): the
      default is **29.3 MB across 10 files** (MB = 10^6 bytes) and the fastest start of the four.
- [x] **The `PublishTrimmed` hazard is a guard, not a comment.** `C6GuardTrimmedPersistence` in
      `Sightline.csproj` makes removing either mitigation a **build error** (proven by running the
      command that trips it); `SIGHTLINE_SHIPTEST`'s TRIMSAFE leg covers the half MSBuild cannot
      see. **CLAUDE.md's "never publish with `-p:PublishTrimmed=true`" was stale and harmful** —
      trimmed is the recommended default; what you must not do is publish without the script.
- [x] **Walked the player's path** — published directory, outside the source tree, path with a
      space in it, empty profile. Fonts resolve by absolute path from the install directory and a
      full campaign wins. `SIGHTLINE_SHIPTEST` from that install PASSes and leaves the profile
      directory empty.
- [x] **Ship the root `LICENSE` in the distributable.** It had been decided and committed and
      **never copied into any build output**.
- [x] **All three player-data writers are atomic.** `display.json` was not; `save.json` and
      `meta.json` were. One writer now (`SaveGame.WriteAtomic`), plus a **failure-path** `.tmp`
      sweep that a measured full-disk run proved was needed. Scope stated in
      `docs/DISTRIBUTION.md` §5: safe against process death and concurrent readers, **not** proven
      against power loss (no `fsync`), and the probe that verifies the mechanism is Unix-only.
- [x] **A version stamp a bug report can name** — `<Version>` in the `.csproj`, read off the
      assembly, painted on the main menu and the pause card. Stamped **v1.0.0**.
- [x] **A cold-first-run screenshot hook** (`SIGHTLINE_COLD=1`): the main menu and the WAR ROOM's
      zero state, neither of which had ever been photographed (both hooks staged rich profiles).
- [x] **`SIGHTLINE_SAVETEST` no longer fails on the publisher's own save file.** It asserted the
      ABSENCE of `MetaUnlock` ordinal 1 against the real profile; anyone owning STANDING ORDERS
      could not publish. **A latent block on the shipping gate.**
- [x] **`.gitignore` no longer contains committed merge-conflict markers.** They were in `main`.

### What C6 found and deliberately did NOT fix (each is a candidate wave)

- [ ] **The cold WAR ROOM ellipsises 4 of 6 unlock descriptions.** With 0 owned the catalogue wants
      508 px in a 446 px column, so `Hud.WarUnlockPlan` clips each compact row to one 12 px line.
      W9 chose this deliberately over breaking the 12 px floor or dropping a card, and documented
      it — but the cost lands on the **only player who ever sees all six unowned: the new one**,
      who therefore cannot read what any unlock does. `METATEST` asserts the font size and the hit
      rect, not readability. Now photographable via `SIGHTLINE_COLD=1`.
- [x] **The intro screen is not in `FITTEST`'s list and overflows at TEXT SIZE 120%.**
      **CLOSED by wave THE FRONT DOOR** (DEVLOG §THE FRONT DOOR): `FITTEST` audits INTRO / INTRO-COLD /
      INTRO-SAVE / INTRO-HEAT at every scale, `SETTINGSTEST`'s front-door leg fails on a chip painted
      into its label or a card line past the card edge (both FAILED pre-fix, verbatim in the DEVLOG),
      `Hud.LabelX` slides a label clear of its chip, and the level-0 hint fits. The original finding:
      the DIFFICULTY panel's body line paints to x≈1266 against a panel edge at x≈1239 — outside its
      own panel — and the `[K]`/`[U]` chips overlap their labels. The first screen in the game had
      never been checked at any scale but 100%.
- [x] **`display.json` load-back cannot be verified headlessly.** **STALE — CLOSED before P17 went
      looking for it, and P17 verified that rather than re-fixing it.** `SIGHTLINE_SETTINGSTEST`
      leg (B3) does a REAL disk round trip through `Display.SaveForTest()` / `LoadForTest()`
      (which are `Save()` / `Load()`), asserting animation speed and text scale come back, that an
      out-of-range file CLAMPS, and that a pre-W5 profile reads as the shipped defaults;
      `SIGHTLINE_TUTTEST` does the same for every tip/seen flag. P17 added `WinW`/`WinH` to that
      pattern in SHIPTEST leg (8b). **The one half still true:** it is `Load()` that is exercised,
      not `Display.Init`'s call to it — `Init(false)` still returns before `Load()` on every
      headless path, deliberately, for byte-stability.
- [x] **No crash reporter and no log file.** **CRASH REPORTER CLOSED by PARALLAX wave P11 THE CRASH
      FILE** (DEVLOG §THE CRASH FILE; contract in `docs/DISTRIBUTION.md` §6). `Program.Main` is now
      `Crash.Guard` around the whole launch plus `Crash.Install` for background-thread throws; a
      crash writes `crash-<utc>-<pid>.txt` into the player's own data directory (through
      `SaveGame.ConfigDir`, not a second path derivation) carrying `Ship.Version`, the UTC stamp,
      OS/arch/runtime/RID, live game state (mode/phase/objective/mission/heat/turn/roster/anim),
      every `SIGHTLINE_*` variable in force, and the full exception chain; exits 70. Atomic
      (`SaveGame.WriteAtomic`, proven by the open-handle inode probe), never-throws, bounded
      (5 files / 64 KB / 3 per launch), and it degrades to stderr when the directory is unwritable.
      A missing/wrong `libraylib.so` gets plain English naming the file and the loader's search
      path instead of a P/Invoke trace — measured against a build with every `libraylib.so`
      actually deleted. `SIGHTLINE_CRASHTEST` is the gate, in `qa-sweep.sh` and in `publish.sh`
      against the PUBLISHED binary; it was seen RED on three separate pre-fix mutations.
      **STILL OPEN — the other half of this item: there is no general LOG FILE**, only a crash
      file. A rolling session log is a separate decision (where, how large, what it may contain)
      and was deliberately not made in P11.
      **ALSO STILL OPEN, and stated in DISTRIBUTION §6:** a raylib ABI mismatch that faults inside
      NATIVE code (SIGSEGV) still produces nothing at all — no managed handler runs. Only the
      catchable corner (`EntryPointNotFoundException`) is covered. Closing the rest needs a native
      signal handler or an out-of-process supervisor.
- [x] **On Windows the published binary opens a black console window behind the game.**
      **CLOSED by P11** — `Sightline.csproj` sets `OutputType=WinExe` for `win-*` RIDs only (so the
      Linux build, the Debug build and the whole headless harness, which set no RID, are
      byte-for-byte unaffected). **Measured on the artifact from Linux**: the published
      `Sightline.exe`'s PE optional-header `Subsystem` word read **3 (`WINDOWS_CUI`) before and 2
      (`WINDOWS_GUI`) after**, and `scripts/publish.sh` now reads that byte on every win-RID publish
      and FAILS the publish if it is not 2.
      **DECLARED OPEN and NOT TICKED: the harness half.** A `WinExe` has no console, so
      `Console.WriteLine` — which is this project's entire verification story — goes nowhere on
      Windows. `Crash.AttachWindowsConsole` re-attaches the parent process's console at startup to
      keep it working, but **nothing in this sandbox can execute a Windows binary and it has not
      been observed.** The exact command a Windows machine must run to close it, from the published
      directory in `cmd.exe`, is `set SIGHTLINE_SAVETEST=1 && Sightline.exe` — PASS is a
      `SAVETEST: PASS` line in that same window. See `docs/DISTRIBUTION.md` §7.
- [ ] **No installer, ~~icon~~, window-title art or `.desktop` file**; on Windows the binary is
      unsigned and SmartScreen will warn. **Code signing costs money and is out of scope
      permanently** under this project's rules — but say so out loud rather than leaving it as a
      surprise. (P11 fixed the console window on this line's platform; **P17 fixed the ICON** —
      `Ship.IconPixels` generates it in engine from the palette, no committed binary, no licence
      entry, `SIGHTLINE_SHIPTEST` leg (7) asserts the art and `SIGHTLINE_ICONSHOT=1` photographs
      it; **whether a desktop displays it is unverified here** — Xvfb has no window manager and
      GLFW ignores window icons on Wayland by design. Installer, `.desktop` and signing: untouched.)
- [ ] **macOS was never even cross-published**, and the Windows build is unverified beyond its
      file list (nothing here can run either).
- [ ] **`meta.json` has no export or backup path.** It holds every permanent thing the player owns
      and the `.bak` beside it is corruption evidence, not a restore.

### Left open by C5 "THE HARD EDGES" (found and reproduced; C5 fixed none — the ticked one was closed later)

- [ ] **The 12px small-text floor is not met at the DEFAULT text size.** Measured at 100%: 10px on
      the AUDIO CHECK screen and 11px on seventeen others (in-mission HUD, end cards, WAR ROOM,
      EVENT card). Repro: `SIGHTLINE_FITTEST=1 SIGHTLINE_FITDUMP=small` lists every string with its
      authored and rendered size. Meeting the rule is a re-layout of half the chrome, so C5 shipped
      a regression BOUND at the measured worst (9px, which is what the tightest shipped fitter
      declares as its own minimum) and the survey. The next wave that wants the rule starts here.
- [ ] **Six shrink-to-fit calls reach their floor at 120%** (three WAR ROOM achievement
      descriptions, one shop body, one prep body). Nothing is lost yet; they are one authored
      character from losing a word. Counted in FITTEST's PASS line on every run.
- [x] **ACCESSIBILITY: TEXT SIZE AND COLOURBLIND MODE CANNOT BE REACHED UNTIL YOU ARE IN A FIGHT.**
      **CLOSED by wave SETTINGS EVERYWHERE** (DEVLOG §SETTINGS EVERYWHERE): the intro has a SETTINGS door
      (`[O]`, or Escape), Escape in the BARRACKS opens the same card (BACK + QUIT TO DESKTOP, no ABANDON —
      the reason is in `Hud.DrawPause`), `[K]` opens the FIELD MANUAL from the barracks and returns there,
      and `SIGHTLINE_SETTINGSTEST` pins every round trip. The original finding, kept for provenance:
      `Update`'s Escape handler is gated on `PlayerTurn || EnemyTurn`, and the pause card is the
      SOLE home of TEXT SIZE, COLORBLIND, BRIGHTNESS, GAMMA, ANIM SPEED, SCREEN SHAKE, THREAT
      PREVIEW, AUTO-CAM and FULLSCREEN. The INTRO — the first screen a player sees — carries ten
      doors (CONTINUE / DEPLOY / TRAINING / LAST STAND / WAR ROOM / FIELD MANUAL / SKIRMISH / DAILY
      / AUDIO CHECK / QUIT) and a difficulty dial, and **no settings entry at all**. It compounds
      the 12px finding above exactly: the 120% text size that would lift every sub-12px string to
      >=12px is behind the door that cannot be opened.
      **BARRACKS is worse** — `case Phase.Barracks` has no Escape handler whatsoever: no pause card,
      no route back to the intro, no field manual (`K` is `Phase.Intro`-gated). On the screen where
      a player deliberates over perks, the shop and the node pick, the only exits are forward or the
      window's close button. Not a soft-lock (forward always exists), and every overlay phase
      (Draft, SkirmishSetup, WarRoom, Codex, AudioCheck, the end cards) does take Escape — walked
      and confirmed. **VOLUME is the exception and an earlier draft of this item had it wrong:** the
      four faders live on AUDIO CHECK, which the intro opens with `U`.
      `QUITTEST` asserts the arm/confirm/checkpoint contract and **asserts nothing about
      reachability from any phase** — that is the half that is broken.
- [ ] **`terminal-reload` fired 0 times in 8083 enemy acts.** That is a BACKSTOP working as
      designed (the planner's own reload branch gets there first), not a hole — recorded so the
      next reader does not re-discover it as a defect.
- [ ] **The sweep's AICOVTEST is a COARSE version of the gate.** It runs `=2` (48 campaigns,
      ~2740 acts), where the one-per-1000 rate is a threshold of **2.74 events** — "does every
      undeclared branch fire at least 3 times?" — and `item` (10), `sap` (13) and `brace` (13) sit
      at 3.6-4.7x the bar. The headline "overwatch fires 8 times in 8083 acts" is the `=6` run;
      **at the sweep's n, overwatch is 0**. A wave that suspects a verb has gone quiet should run
      `SIGHTLINE_AICOVTEST=6` by hand rather than trust the sweep line.
- [ ] **An OCCLUSION signal would let the text audit assert string-vs-string overprint.**
      `Cfg.InkProbe` already fires in draw order, so z-order is available; what is missing is
      whether an opaque fill landed between two strings. Acquiring it means funnelling **230** raw
      `Raylib.DrawRectangle*` calls in `Hud.cs` and **87** in `Renderer.cs` through a `Cfg.Rect`
      seam. Two overprint assertions exist today, both hand-scoped to one row each (FITTEST legs B
      and C). Priced, declined by C5, and worth doing for whoever next owns the chrome.
## PROGRAM CONTOUR — THE TWO ITEMS C3'S REVIEW PUT AT THE TOP OF THE NEXT WAVE

- [ ] **RE-TUNE `Mission.OpenerTrim`, AND RE-MEASURE THE LADDER AFTER IT.** C3's review stratified
      its paired test on whether the BASELINE survived mission 1 — same CRN worlds, lever the only
      difference — and found the ladder gain and the "unintended" mission-1 cost are **one
      transaction, not two**:
      | | n | B1 | L1 | Δ | L-only : B-only |
      |---|---|---|---|---|---|
      | all pairs (shipped) | 960 | 33.44 | 35.94 | **+2.50** | 26 : 2 |
      | **baseline survived m1** | 936 | 34.29 | 35.68 | **+1.39** | 15 : 2 |
      | baseline lost m1 | 24 | 0.00 | 45.83 | +45.8 | 11 : 0 |
      Per rung, holding the opener fixed: h0 **+4.58** (~39% opener), **h2 +1.31 (~58% opener)**,
      **h4 +0.00 (100% opener)**. So "L3's heat-2 miss is closed" is majority opener, and h4's gain
      is entirely opener. C3's genuine mid-run effect is **+1.39 pooled (p=0.0024)**.
      **And mission 1 is not "nearly" unlosable — at the four heats a person plays it was NEVER
      lost: 640 consecutive campaigns at RECRUIT/h0/h2/h4, zero mission-1 losses.** Structural, not
      luck: 22 of the 24 baseline m1 losses were the reinforcement wave, and that mechanic is now
      absent from the opener. **Backing the opener out will move h2 and h4 back down, so the ladder
      must be re-measured with it** — a lever and its measurement, not a lever alone.
      *Design position (lead):* the wave overshot. X2 already discharged DESIGN §3.D's
      front-loaded-anxiety obligation, so 97.5% was not the problem; and §3.G's "scripted,
      low-stakes first mission" defence stopped applying when RESONANCE T1 moved onboarding into a
      dedicated `GameMode.Training` drill. An unlosable *campaign* opener now buys nothing §3.G
      asks for and spends pillar 5 ("stakes that bite") to do it.

- [ ] **ADD A CAMPING POLICY TO THE FLYWHEEL — the turtle risk is not unmeasured, it is
      UNMEASURABLE.** Both harness policies are `greedy` (advance-and-engage) and `sloppy` (greedy
      plus target/tile mis-picks — an *error* model, not a *passivity* model). Neither camps, and
      `SmartStep` hunkers only as a terminal fallback. **No policy in the instrument could reveal a
      turtle exploit**, so no future wave can price one either. This matters now because C3's lever
      touches only the **41% slowest** Eliminates — precisely the missions where a player was
      already camping — and what remains as a disincentive is the aim arm alone, whose measured
      mean high-water rung is **1.50** (the old wave arm at rung ≥2 rarely fired anyway).
      This is instrument work, so it must land BEFORE any wave that claims to have priced turtling.

- [x] **A 2.1% HARNESS-FORCED-LOSS FLOOR NOBODY OWNS.** `instrumentHealth.stalemateLosses` reads
      20/960 in both of C3's arms and in L3. It has never been mentioned by any wave. It is not a
      C3 defect; it is a property of the instrument that every ladder in this repository inherits.
      **Corrected (THE HEAT PIN AND L5): 16/960 = 1.67% on L4, split by arm since this wave — see the
      copy of this item above and L5's README.**

---

## PROGRAM CONTOUR — CLOSED. THE COMPOSED-TREE LADDER (L4) AND WHAT IT LEAVES OPEN.

All six waves merged; L4 (`docs/measurements/l4/`, base `7315425`, 960 campaigns, 48/48 chunks
asserted) was the ladder of record until **L5 superseded it** (`docs/measurements/l5/`, base
`7180374`, heat PINNED, 16 slot sets, 1,920 campaigns, 96/96 asserted; L5's bridge reproduces L4
960/960). Read L5's README before quoting any rung; the L4 rows are NOT comparable with L5 at h0/h8.

**What the composition bought.** The flat middle this program opened on is **gone**: steps of
18.8 / 20.0 / 11.2 / 11.2 / 6.2, monotone, nothing flat, where the pre-CONTOUR tree had `h4→h6` at
−2.3 and a rung 5 buying exactly zero. Worth sitting with: **C1's lever, measured alone, made
dispersion WORSE on every metric** — its own review proved that and C1 re-decided to mode 3 on the
evidence. The shape improved anyway, in composition. **Shape is a property of the tree, not of a
lever, and no wave can measure it.**

**What it cost.** h6 fell 20.0 → 10.6 and three rungs sit below their floors — though only h6 is a
real move (h4 −0.1 and h8 −0.6 are far inside their own cluster SE).

### The next program's top item, already attributed

- [ ] **RECOVER THE TOP OF THE LADDER. The dial to reach for first is `Heat.MidTooth`.**
      Measured at h6 on the same slot sets, one dial at a time: `SIGHTLINE_MIDTOOTH=0` **+6.2**,
      `SIGHTLINE_BIOMEMECH=0` +1.2, `SIGHTLINE_KILLTREADMILL=1` **0.0**, `SIGHTLINE_AIDECLINE=0`
      **−3.8** (removing C2 makes h6 *harder* — its dominant effect is +2 points of hunkering, so
      the post-C2 opponent is *less* lethal at this rung). **All four off reproduces L3's h6 at
      20.0% to the decimal**, so the levers are the whole story and C5/C6 are gameplay-inert.
      A partial back-off of C1's tooth is the obvious candidate — it is one integer, it is already
      dialled, and its effect is the largest single term. **One lever, CRN-paired, then re-measure
      the whole ladder** — because L4 is precisely the evidence that levers do not compose the way
      their solo measurements predict.
      **Re-pointed by L5 (pinned, 16 slot sets, base `7180374`): h6 reads 13.1 and h8 8.1, both IN band;
      the OUT rungs are now h0 (46.9, on its floor) and h4 (20.0, −2.0, 0.68 cluster-SE). The pin is
      worth +1.0 pooled and the rest of L4's "collapse" was its slot draw (L4's 8 sets read h8 4.4,
      the 8 new ones 11.9). `Heat.MidTooth` is still the dial: main effect +7.58 ± 0.96 at h6.**

- [x] **RESOLVE THE INTERACTION TERM, or stop citing it.** Single-lever removals sum to +3.6
      against a joint +9.4. The apparent +5.8 interaction would be the most interesting result of
      the round and it is **not resolved**: chunk-paired t(7)=+2.05, p≈0.08, per-slot deltas
      `+25 +5 −10 +20 0 +25 +10 0`. It needs more slot sets, not more prose.
      **DONE (THE HEAT PIN AND L5, h6 2^3 factorial, 2,560 campaigns on 16 slot sets): NOT resolved
      by the pre-registered criterion — Q = +5.94 ± 5.25, t(15)=+1.13, CI [−5.2, +17.1] — and it
      cannot be, cheaply (~70 clusters). What IS resolved: MIDTOOTH main effect +7.58 ± 0.96 (t=7.9),
      AIDECLINE and BIOMEMECH main effects zero, no two-way term over |t|=1.8. Stop citing +5.8; quote
      the averaged terms. `docs/measurements/l5/README.md`.**

- [ ] **The three items C3's and C1's reviews put ahead of everything else still stand**, and two
      of them are instrument work that must land BEFORE the balance round above or it re-inherits
      the bias: re-tune `Mission.OpenerTrim` **and re-measure** (roughly half of C3's ladder gain
      was the opener change it booked as an unintended cost; h4's gain was 100% opener); add a
      **camping policy** to the flywheel (both existing policies model error, not passivity, so
      turtling is structurally unmeasurable); and pin `Events.cs` `AddHeat` for measured batches —
      it contaminates every rung below 8 **upward** while heat 8 is clamped, so the instrument
      **compresses exactly the region that collapsed in L4.**
      **The pin landed (THE HEAT PIN AND L5, base `178464a`); the opener re-tune and the camping
      policy still stand.**

### WAVE "THE STRIDE" (2026-09-02, base `cee3cba`, details in DEVLOG §THE STRIDE)

- [x] **Pillar 2 has a gate.** `SIGHTLINE_FEELTEST=1` (in `qa-sweep.sh`) measures the tween on
      `Unit.Pos` through the real anim pump: a six-tile walk's mid-path speed (min/max ≥ 0.60, zero
      stall frames, one lean kick, 48-frame cadence pinned), a VAULT's lift (≥ 20 px, peak over the
      cover, lands on the centre) and stacked floating text (pairwise ≥ `Fx.TextSep` = 14 px, twins
      arc apart). It FAILED on all nine assertions on the pre-fix tree; the profiles are in the DEVLOG.
- [x] **The caterpillar is gone.** `Game.EnqueuePath` is the only path funnel; `MoveStepAnim.Seg` +
      a shared `Path` polyline draw ONE stride profile per walk (push-off, constant stride, brake)
      on the PREDICTED commit period, so no tile boundary stalls. Mid-path reads `8.7 8.7 … 8.7`
      (min/max 1.00) against `2.5 7.4 12.3 16.5 13.3 8.4 3.5 0.1` per tile before. Commit clock and
      `_dur` untouched; seeds 101/202 give identical `frame=`/`turns=` against `cee3cba`; PAIRTEST PASS.
- [x] **VAULT is a leap.** 26 px arc over 0.24 s (`MoveStepAnim.Hop`/`VisDur`), shadow left on the
      deck (`Unit.HopLift`), landing puff + heavier footfall. The commit still fires on frame 8.
- [x] **Floating text climbs a ladder** (`Fx.TextRung`): the kill trio reads 42 px apart (was 7.2),
      the BRACE pair 28 (was 4.0), twin overwatch numbers 36 (was 0.0) and arc opposite ways.
- [ ] **A held stride pose.** `WalkLean` is the push-off and decays within a tile; a lean carried
      through the walk needs a renderer/Unit change and a restated `leanKicks` contract.
- [ ] **Diagonal vs straight drawn speed differs by 13% at 60 Hz** (derived from the 0.155/0.12 s
      commit durations, not measured on a mixed path). Only a diagonal commit of ~0.170 s closes it,
      and that is sim timing.
- [ ] **Film a vault UNDER overwatch.** The reaction fires with the figure at the apex; unjudged.
- [x] **Kill-cam / reaction beat / explosion cue** — pillar-2 items this wave deliberately left alone.
      Built by WAVE "THE BEAT" (2026-09-03), below.

### WAVE "THE MODES GET THE BESTIARY" (2026-09-02, base `3f3e478`, PARALLAX P4, details in DEVLOG §THE MODES GET THE BESTIARY)

- [x] **Roster depth is its own axis.** `Mission.Build`/`SpawnEnemies(..., int rosterTier = -1, bool
      midBossSlot = false)`: every roster gate that keyed on `n` (SelectArchetype's tier, pods of 3, the
      mid-boss slot, the grenade/utility gates) reads `rosterTier`; the numeric ramp stays on `n`.
      `SetupMission` passes `clamp(3 + heat/3, 3, 5)` and `heat >= 4` for SKIRMISH/DAILY, `n` for
      everything else. Pre-fix MODETEST on `3f3e478`: 50 heat-0 skirmish builds fielded exactly
      `GRUNT/SCOUT`, 0/50 pods of 3, 0/10 mid-bosses at h4 and h8. Shipped: 18 classes, 50/50, 10/10.
- [x] **SKIRMISH has an OPPOSITION dial; the DAILY has a faction.** ANY / SYNDICATE / LEGION / WARDENS
      on the setup card (ANY dealt off the map seed among MIXED + the three; a `Faction?` null, not an
      enum member — nothing appended, SAVETEST untouched); `DailyFaction(seed)` off the date seed's
      next byte. `Game.ModeFaction` → `Combat.BeginMission`; the Start node stays `None`.
- [x] **Campaign byte-identical.** PAIRTEST PASS; `SIGHTLINE_BALANCE=10` base 0 on the `3f3e478`
      Release binary vs this branch's, `inert_diff.sh … harness` → empty diff, twice.
- [ ] **A skirmish's difficulty is unmeasured.** No flywheel policy plays one, and pods of 3 carry
      FUL-6's count−1 trim, so a skirmish now fields ONE BODY FEWER at every rung (MODETEST's readout:
      bodies h0/h4/h8 4/6/8 → 3/5/7) in exchange for the roster. Price it before touching the tier map.
- [ ] **The banner names one thing.** On VERDANT/TUNDRA/MAGMA the ground rule keeps the sub-line
      (C4 REVIEW M4) and the faction reads only on the top bar; a second sub-line needs the 92px band
      to grow. The end card does not name the faction either.
- [ ] **No MIXED option on the dial** — reachable only through ANY (a quarter of its deal) or
      `SIGHTLINE_FACTION=mixed`.

### WAVE "THE HEAT PIN AND L5" (2026-09-03, base `178464a`, details in DEVLOG §THE HEAT PIN AND L5)

- [x] **A measured rung means the rung.** `EventCatalog.HeatPinned` (default false; every
      `SIGHTLINE_BALANCE` batch sets it unless `SIGHTLINE_HEATPIN=0`) makes the field-event `AddHeat`
      outcome a no-op that reports `Heat pinned (harness)`; the arm's other outcomes still fire, no
      `Util.Rng` draw moves either way. `heatLeak{pinned, heatRaisingPicks, campaignsRaised,
      missionsAbovePin, maxHeatEnd}` in the JSON; `RunRec.HeatEnd` stamped at every campaign exit.
      Real play and EVENTTEST are untouched — the leak is a design cost there, not an instrument bug.
- [x] **The STALEMATE guard names its arm.** `STALEMATE-MISSION` (the per-mission cap) /
      `STALEMATE-RUN` (W9's run-scoped cap), mission-first when both hold; `Stats.IsStalemate` matches
      the prefix so every consumer and every older archive still read. `instrumentHealth` carries the
      split and a `stalemates[]` row per forced loss (slot, policy, heat, arm, mission, objective,
      missionTurns, runTurns).
- [x] **One row per campaign in the JSON.** `campaigns[]` (slot, policy, mode, heat, heatEnd, win,
      missionsCleared, lossCause, runTurns, endMission, endObjective, heatRaisingPicks) — so a
      SINGLE-policy batch, which `pairedPolicy.slots` drops entirely, is CRN-pairable.
      `docs/measurements/l5/rows.py --check` rebuilds `pairedPolicy` from the rows field for field.
- [x] **`SIGHTLINE_HEATPINTEST`** in `qa-sweep.sh` through `verdict`; FAILS on both pre-fix shapes
      (an inert pin: `pinnedHeat=3` on all three arms; a one-word guard: `runArmTitle=STALEMATE`).
- [x] **Inert with the pin off**, by measurement: base-tree chunk vs this tree with `SIGHTLINE_HEATPIN=0`
      differs in EXACTLY the five new keys at h0-b0 and h4-b10 (`l5/inert.py`); with the pin ON every
      campaign that took no heat-raising arm is identical (38/38, `l5/pincheck.py`).
- [x] **L5 — the PINNED ladder of record** (base `7180374`, 6 rungs x 16 slot sets, 1,920 campaigns,
      96/96 asserted, LEAK-CHECK PASS): RECRUIT 70.9 / h0 46.9 / h2 32.8 / h4 20.0 / h6 13.1 / h8 8.1,
      monotone, four of six in band (h0 on its floor, h4 −2.0). The bridge (same binary,
      `SIGHTLINE_HEATPIN=0`, L4's 8 sets) reproduces L4 **960/960**; the pin is +1.0 pooled (z=2.04)
      and exactly 0.0 at h8. Split-half: L4's 8 sets vs the 8 new ones differ by +15.0 at h0 (t=2.58)
      and −7.5 at h8 — L4 and L5 are not comparable at those rungs. CLAUDE.md's ladder block is L5.
- [x] **RESOLVE THE INTERACTION TERM** — see the ticked item under PROGRAM CONTOUR — CLOSED: not
      resolvable at any affordable n; the averaged effects are, and they say MIDTOOTH and additive.
- [ ] **The stalemate floor is autopilot work now that it is located**: the Escort/Evac finishing
      line late in a RECRUIT run (14/320) and the slot-46 sloppy mission-1 Eliminate deadlock.
- [ ] **h0 and h4 are the OUT rungs on the pinned instrument** (46.9 on the floor; 20.0, −2.0). A
      base-difficulty lever, one at a time, CRN-paired on 16 slot sets — never 8 again.

### WAVE "THE FRONT DOOR" (2026-09-02, base `fa482ed`, details in DEVLOG §THE FRONT DOOR)

- [x] **DEPLOY SQUAD carries `[ENTER]`.** Every intro chip is drawn from `Hud.IntroDoors` +
      `Game.IntroKey`, the hover caption from the same table through `Game.IntroHit`;
      `Hud.IntroNullHintDraws` is asserted unmoved by `SETTINGSTEST` (E), so a door cannot ship
      hint-less again. `Hud.LabelX` keeps chips out of labels at every text size (CONTINUE/DEPLOY
      220 → 240, the utility grid 172 → 188 — the widths the 120% row needed, measured).
- [x] **The cold menu points at the on-ramp.** `Hud.ColdNudge` at rest while `Hud.IntroCold(g)`
      (no drill seen, no save, no LAST STAND best); the old line returns after any of them.
- [x] **The DIFFICULTY card names rung 0 STANDARD and states the unlock rule**
      (`Hud.HeatUnlockRule`): "WIN AT HEAT n TO UNLOCK HEAT n+1" at the earned ceiling and on a
      fresh profile's RECRUIT default, "MAX UNLOCKED: n" below it; the level-0 hint fits the card.
- [x] **README's controls are generated** — `SIGHTLINE_KEYTABLE=1` (hand-run, prints markdown from
      `Hud.VerbTable` + `Hud.KeyTable` + `IntroDoors`); the FIELD MANUAL's VERBS & KEYS tab reads the
      same `KeyTable`. Class table is five rows, `[6]` is UTILITY ITEM, the layout block lists all of
      `src/`. Bound-key coverage derived by grep and checked by hand on this commit.
- [ ] **Rung 0's rule sentence is the short form** ("A WIN UNLOCKS HEAT 1") because it shares a row
      with "< RECRUIT" on a 320 px card. A card re-layout should give it the "WIN AT HEAT 0" form.
- [ ] **README bound-key coverage has no runtime gate.** The hook prints; the grep is the check.


### WAVE "THE BEAT" (2026-09-03, base `23f0bc1`, details in DEVLOG §THE BEAT)

- [x] **The kill-cam is slow-mo, not a freeze.** `Game.TimeScale` / `KillCamWindow` (0.45 s) /
      `KillCamScale` (0.25): Fx, the anim pump, unit flinch/recoil and the scorch fade run on the scaled
      clock and the zoom-punch is HELD for the window. Pre-fix FEELTEST leg d: 24/27 still frames, zoom
      at 1% by frame 24. Post-fix: 0/27, ratio 0.36, zoom 100%. AutoPlay keeps the 0.4 s HitStop —
      seeds 101/202/303 give identical `frame=`/`turns=` against `23f0bc1`.
- [x] **A reaction shot has its own beat.** `ShotAnim.Reaction` (unread for two programs) now: 0.26 s
      wind-up, 0.06 s snap-freeze, a 44 px reticle on the MOVER, a flash, a flinch, the `react` cue.
      Plain shot 0.14 → 0.34 → 0.52 unchanged; windless unchanged (FEELTEST leg e).
- [x] **Explosions, flashes, smoke and mends sound like themselves.** `boom` / `flash` / `smoke` /
      `heal` / `react` recipes; grenade / barrel / siege → boom (incendiary −6 dB), flashbang → flash,
      smoke → smoke, HealAnim / STABILIZE / PATCH / REVIVE → heal. AUDIOGATE 28 cues, spread 11.0 dB,
      `boom+death+st_kill` stack −5.0 dBFS / 0 clipped. AUDIO CHECK lists them (FIELD group).
- [ ] **A vault under overwatch with the new reaction beat** — the reaction now freezes the frame with
      the vaulter at its apex; nobody has filmed it.
- [ ] **The kill-cam on a SQUAD WIPE / lost VIP.** Leg d stages the last-hostile case; the other two
      arms of `IsMissionEndingKill` take the same window but were not photographed.
- [x] **Part B — THE CUE MAP: one meaning, one cue.** `src/Audio.CueMap.cs` adds `Audio.GameEvent`
      (14 beats) and `Audio.CueFor` — the single place that says what a beat sounds like — plus
      `Audio.Cue` / `Audio.PlayFoe` / `Audio.BusOf`. Eight new recipes: `alert` (a pod wakes; was
      `over`), `alarm` (reinforcements / pressure / artillery; was `turn`), `ambush` (concealment
      breaks; was `turn`, twice), `ability` (every soldier verb; was `reload`, and `over` for
      MARK/SUPPRESS), `tick` (objective progress; was `reload`), `ui_ok` / `ui_no` (the shop's
      confirm and refusal; were `hit` and `miss`), `turn_enemy` (the round passing to the opponent;
      was `turn`, identical to the player's). `ShowBanner` takes a `cue` and puts every
      `enemy:true` banner on the SFX fader, so an opponent telegraph no longer dies with the UI
      slider. `Audio.Play("over")` went **13 → 0** sites in `Game.cs`, `Audio.Play("reload")`
      **11 → 0**; 28 → 36 cues, AUDIOGATE spread still 11.0 dB, seeds 101/202/303 byte-identical
      against the working branch. `SIGHTLINE_CUETEST` is the gate (injective table, no telegraph
      on the UI bus, the real `ShowBanner` driven, a source census of the call sites). DEVLOG
      §THE BEAT part B.
- [ ] **`hunker` is the last borrowed cue.** `ShoveAnim` plays it and so does the DEPLOY COVER UP
      ability; neither is digging in. Two one-site collisions of exactly the class part B fixed.
- [ ] **Nobody has HEARD the eight new cues.** No audio device in the sandbox: every claim about
      them is a measurement of the rendered buffer, not a judgement that `alert` reads as alert.
      The owner has a device and the AUDIO CHECK screen (THREAT group).
- [ ] **The AUDIO CHECK scroll has no thumb drag** and no keyboard focus ring — wheel and Up/Down
      only. Fine at 36 cues; revisit if the list grows again.

---

### PROGRAM PARALLAX — P15 "THE UNVERIFIED" (2026-09-03, details in DEVLOG §THE UNVERIFIED)

Closed: the seven ways the BALANCE INSTRUMENT reported a number where it had none. Six fixed,
one handed back. Gate `SIGHTLINE_INSTRUMENTTEST` + `docs/measurements/p15/regress.sh`.
No campaign outcome moved (`docs/measurements/p15/inert/`, three rungs, CRN-paired).

- [x] **The batch validates and records its own request.** `Stats.ParseBatchEnv` is the one reader
      of `SIGHTLINE_BALANCE` / `_HEAT` / `_BASE`; unparseable = `Program.RefuseBatch`, **exit 3**,
      nothing written. `batch{}` in the artifact carries `expectedRuns`, `heatRequested`/`heat`,
      `baseRequested`/`slotBase`, so a chunk's file name is checkable.
- [x] **`docs/measurements/p15/run_chunk.sh` + `check_chunk.py` replace layer (c).** All fourteen
      archived runners hard-code `runs == N*2`; the two canonical ones now carry a SUPERSEDED
      header. Fixtures + `regress.sh` prove accept/reject before and after.
- [x] **`runWinRate` is -1 on no campaign data.**
- [x] **`l5/cluster.py`'s LEAK-CHECK is a gate again** — a MIXED pinned/unpinned round FAILs
      instead of downgrading to a note and exiting 0. Re-reads the 96-chunk L5 archive as PASS and
      reproduces the published ladder to the decimal.
- [x] **The LAST STAND harness stop is named** (`EndEndless(cause)`; the autopilot passes the same
      STALEMATE arm the campaign side does) **and endless runs stamp `runTurns`**. `endless{}`
      gains `stalemateHits` / `wipes` / `depthUncensored`.
- [x] **The erased mission is filed, not dropped** (`Stats.FlushOpenMission`; `REDEPLOYED` from
      the checkpoint redeploy, `UNCLOSED` from the batch's frame-cap/abort exits).
      `SIGHTLINE_MISSIONFLUSH=0` restores the drop.

Open, handed on:

- [ ] **HANDED BACK — `Game.cs`: `StartPlayerTurn` runs to completion on a run `AutoStallCheck`
      has already force-lost.** Traced both arms ("StartPlayerTurn CONTINUES with Phase=Lose ...
      and RAN TO THE END"). Autoplay-only, and most of the tail's telemetry is a no-op because
      `_mission`/`_run` are null — but the batch-global counters (`Stats.RecordDownExpired` /
      `RecordDownFinished` / `RecordProc`) guard on `Enabled` alone and a bleed-out in that tail
      can bump them after `EndRun`. Fix: `if (AutoPlay) { AutoStallCheck(); if (Phase !=
      Phase.PlayerTurn) return; }`. Exact patch: `docs/measurements/p15/handback-Game.cs.patch` (hunk 2; `git apply --check` clean, validated in a scratch build). Leg F of INSTRUMENTTEST
      asserts the invariant that keeps the tail merely wasteful (no phantom run) in the meantime.
- [ ] **OPTIONAL, `Game.cs`: `TryReinforcements` should close its own mission record.** The
      Stats-side flush is a structural backstop and has to approximate the erased row's `Turns`
      from `MissionRec.PlayerTurns`, because `Stats` cannot see `Game._turnCount`. One line at the
      caller makes the row exact — measured to change nothing but `byObjective.avgTurns` on the
      erased rows. Exact patch: `docs/measurements/p15/handback-Game.cs.patch` (hunk 1).
- [ ] **`policyGap.greedyWinRate` / `sloppyWinRate` still read 0.0 with no runs** — the same shape
      as the `runWinRate` defect. Left deliberately: they sit beside `greedyRuns`/`sloppyRuns` in
      the same object, so "no data" is visible there. Change it with the next artifact-schema wave,
      not silently.
- [ ] **Twelve archived chunk runners still carry `runs == N*2`.** Only `w1/` and `c1/` were given
      the SUPERSEDED header (they are the two CLAUDE.md and `l5/run_ladder.sh` point at). The rest
      are provenance for closed rounds; head them if a future round reuses one.
- [ ] **Every per-mission and decision-density figure in the archive is survivorship-biased.**
      Corrected going forward; the historical tables were NOT recomputed (the erased rows do not
      exist in those files and cannot be reconstructed). Re-measure before quoting one.

## OPEN — from wave P13 "THE UNVERIFIED — PERSISTENCE AND THE GATE" (base `a933cfe`)

Three real findings this wave confirmed and deliberately did not fix, plus one gate whose scope is
a declared limit. Detail and traces: `docs/DEVLOG.md` §THE UNVERIFIED.

- [ ] **A forced objective desyncs the mission PLAYED from the intel the node PAYS.**
      Harness-only, but it biases the flywheel. `node.Intel` is assigned once in `Run.GenerateMap`
      (`Run.cs:805`) from `Run.NodeIntel`, which reads `node.Card.Objective` through `ClassPremium`.
      `Game.ForcedObjective` (`SIGHTLINE_OBJ`) is read in exactly one place — `Game.cs:2008` — and
      sets the objective PLAYED without touching any card, so from **mission 2 onward** (mission 1
      is fine: `DebugForceObjective` rewrites the card) an objective sweep plays `<x>` while the
      economy pays whatever the map dealt. Size, from the constants: `Run.PitchedPremium = 8`
      against `BaseIntel(m) = 12 + 4m` (16 at m1, 36 at m6) — **up to a third of a Combat/Elite
      node's payout, in either direction**. New since THE FORK PAYS made `NodeIntel`
      objective-dependent.
      **The fix is one line** — where `Game.cs:2008` overrides `Objective`, also rewrite
      `_run.CurrentCard.Objective` and re-derive `CurrentNode.Intel = Run.NodeIntel(CurrentNode)` so
      the whole world (economy, the PITCHED/TASKED label, the hover and deploy cards) agrees with
      what is played. **It is an INSTRUMENT change and must be paired with a re-measure**: it moves
      measured intel in every `SIGHTLINE_OBJ` batch, so it belongs to a wave that can run one.

- [ ] **Four self-tests still hand-roll a truncating restore of a player-data file.**
      `Game.Endless.cs:535` (HORDETEST), `Game.Modes.cs:640` and `:679` (MODETEST),
      `Game.Meta.cs:612` and `:615` (the WAR ROOM leg), `Program.cs:1579` and `:2586` (two shot
      paths). All are `if (x != null) File.WriteAllText(path, x)` — the null guard means **none can
      zero a file**, which is what made the SETTINGSTEST one dangerous — but every one truncates the
      target in place, the tear `docs/DISTRIBUTION.md` §5 says cannot happen. Convert to
      `SaveGame.StashForSelfTest` / `RestoreForSelfTest` (rename out, rename back), as P13 did for
      SETTINGSTEST / TUTTEST / SAVEEDGETEST / QUITTEST / BRIEFTEST / ONRAMPTEST. Left alone here
      only because those files belong to other developers this sprint.

- [ ] **`SIGHTLINE_KEYTABLEGATE` leg (b) covers ONE screen.** It derives the keys
      `src/Game.Audition.cs` reads and asserts each is named in `Hud.KeyTable`'s AUDIO CHECK row —
      the leg that would have caught THE CUE MAP. AUDIO CHECK is the only screen with a dedicated
      input-handler file; every other row (FIELD MANUAL, SKIRMISH SETUP, BARRACKS, DRAFT, END CARD,
      WAR ROOM, MAIN MENU) is documented and **un-derived**, because their handlers are interleaved
      through `Game.cs`. Closing this needs a seam — a per-screen input dispatch, or a marker
      convention `Game.cs` handlers opt into — not another regex.

- [ ] **`KeyTable` coverage for the game as a whole is still un-gated.** CLAUDE.md's advice is
      `grep -ohE 'KeyboardKey\.[A-Z][a-z0-9]*' src/*.cs | sort -u` before binding anything; nothing
      asserts the result is a subset of what `KeyTable` documents. A whole-project version of leg
      (b) needs an exclusion list for harness-only keys first.

## PROGRAM PARALLAX — wave P14 "THE UNVERIFIED" left these OPEN (2026-09-03, `wave/qa-audio`)

Fourteen findings were re-verified; thirteen were fixed (`docs/DEVLOG.md` §THE UNVERIFIED — AUDIO
AND THE MODES). These are the ones deliberately NOT fixed, and why.

- [ ] **A MIXED skirmish/daily mid-boss has no signature.** P14 fixed the *callsign* — the
  unfactioned `Mission.MakeMidBoss` fallback used to field a `WARDEN` with `siege=False`, i.e. the
  Wardens mid-boss's name on a body that cannot do the thing that name means, and is now a neutral
  `MARSHAL`. The **kit** is still absent: at heat ≥ 4 a MIXED force's climax is a plain ELITE with
  two frags, while every named faction gets rage / shield / siege. A draw-free fix exists (deal the
  kit off `Mission.ModeTierFor` — tier 3 → rage, 4 → shield, 5 → siege, gated on `ModeDepth` so the
  campaign's own unfactioned mid-boss, reachable on a **Supply node at mission 3 or 5**, is
  untouched), but arming a mid-boss is a real force change and belongs in a wave that shows the
  before/after with `SIGHTLINE_MODEFORCEPROBE` rather than one that was re-verifying findings.

- [ ] **OWNER LISTEN: is `over` / `reload` on the UI fader right?** P14's `Audio.NeverUiBus`
  asserts that no beat which *happens to* the player rides the UI fader. It deliberately EXCLUDES
  `OverwatchSet` and `Reload` (with `Ability` and `Objective`), on the argument that those four are
  confirmations of a click the player just made — which is what a UI fader is for — and that the
  asymmetry is already shipped and deliberate (`Turn` is a UI cue; `EnemyTurn` was moved off the UI
  bus by THE CUE MAP precisely because it is the opponent). The counter-argument is that a player
  who pulls UI volume to zero to silence menu chrome also stops hearing their own watch being set
  and their own magazine going in, which are board events with a position. **This is a question
  about sound and nobody in the sandbox can hear it.** Decide it on the AUDIO CHECK screen; if the
  answer is "they are board beats", the change is two entries in `Audio.NeverUiBus` plus two rows
  in `CatOf`, and CUETEST will hold the new line.

- [ ] **Nothing in the audio suite listens.** P14 added `Audio.Spy`, so a self-test can now assert
  WHEN a cue fires, with what pan, on which bus — which is how findings 1, 4 and 5 were caught and
  gated. It still cannot assert that `alert` reads as *alert*. Every audio claim in this repository
  is a claim about a rendered buffer, a registry, or a routing decision. That gap is structural in
  this sandbox and is the owner's to close on a real device.

- [ ] **`SIGHTLINE_DECLINEWATCH` / the flywheel's blindness to an enemy lane** (P10) and the
  pressure clock's depth in the modes: P14's `Mission.DepthFor` also gives SKIRMISH/DAILY
  anti-turtle pressure waves the mode's depth (they were tier-1 bodies too). That is consistent
  with the funnel and is reported, but it was never a measured lever — the flywheel does not cover
  skirmish at all, so no rung has ever included one. If the modes ever want a measured ladder of
  their own, that is the wave.


## PROGRAM PARALLAX — wave P17 "SHIPS AS v1.0.0" (2026-09-04, base `e57e151`, branch `wave/ships-v1`)

Detail in `docs/DEVLOG.md` §SHIPS AS v1.0.0; the contract is `docs/DISTRIBUTION.md` §8.

- [x] **The release artefact.** `scripts/publish.sh` produced a directory and stopped. It now also
      derives `CHANGELOG.md`, packs `dist/SIGHTLINE-v<version>-<rid>[-<mode>].{tar.gz,zip}`, writes
      a `sha256sum(1)`-format checksum beside it with a bare filename (so `sha256sum -c` works
      where the file was downloaded), verifies that checksum, and then **re-runs SHIPTEST from
      inside the payload with `SIGHTLINE_RELEASEDIR` set** so the archive's digest is recomputed
      in-process and compared. The version in the name is scraped off the binary's own SHIPTEST
      line, not read out of the csproj twice.
- [x] **The changelog is DERIVED, not written** — `scripts/changelog.sh`, from
      `git log --first-parent`, cut into sections at `v*` tags. 71 entries for 676 commits, because
      this project lands one merge per wave and those subjects already read like release notes.
      Reasoning and the honest scope (it reports what MERGED, not what a player NOTICES) in
      DISTRIBUTION §8.3.
- [x] **The tag is implemented but NOT CREATED.** `--tag` makes a LOCAL annotated `v<version>` and
      never contacts a remote; it refuses on a dirty tree, on a missing repo, and on an existing
      tag. A publish without `--tag` prints the exact `git tag -a … && git push origin …` pair.
      **`v1.0.0` does not exist in this repository** — creating and pushing it is the lead's call.
- [x] **The window icon.** Procedural, in-engine, from the palette (`Ship.IconPixels` →
      `Raylib.SetWindowIcon`): the game's own aim reticle, brackets in `Pal.Friend` around a
      `Pal.Foe` core. No committed binary ⇒ no `CREDITS.txt` / `THIRD-PARTY-NOTICES.txt` entry, no
      cost, no licence risk. `SIGHTLINE_ICONSHOT=1` writes `sightline_icon.png` + a 256× blow-up.
- [x] **The first-launch window size.** `Display.FitLaunchSize` (pure) asks the monitor and only
      ever SHRINKS: 1366×768 → **1126×704** measured live, 1280×800 → 1176×736, 1920×1080 →
      unchanged and nothing written. Persisted as additive `WinW`/`WinH` in `display.json`, so it is
      decided once. `Display.AllowLaunchFit` defaults to **false** and is set true only by the real
      launch path in `Program.RealMain` — that is what keeps the harness's window size fixed.
- [x] **The version in-game** — **already done by C6 and the docket was stale.** It is painted on
      the main-menu footer and the pause card. P17 verified it by screenshot and added a
      72-character budget guard on the (centred, therefore doubly-overflowing) footer string.

### Left OPEN by P17

- [ ] **The archive is not bit-reproducible.** `tar` is invoked with `--owner=0 --group=0
      --numeric-owner --sort=name` so the *packing* adds no variance, but `dotnet publish` does not
      emit a byte-identical binary run to run, so two publishes of the same commit hash
      differently. Closing this means a deterministic publish (`-p:Deterministic`, a fixed
      `SOURCE_DATE_EPOCH` and mtime normalisation in the stage) and is its own small wave.
- [ ] **No release page, no download location.** The archive and checksum land in `dist/`, which is
      gitignored. Where a person actually GETS the file is unanswered — and it cannot be answered
      by anything in this sandbox, nor (per the hard rules) by any CI. A GitHub Release created by
      hand from the tag is the obvious home; that is the owner's call.
- [ ] **The Windows and macOS archives are unverified beyond their file lists.** A `--rid win-x64`
      publish writes and `sha256sum -c`-verifies its `.zip`, but the release legs do not run (the
      binary cannot execute here) and macOS has still never been cross-published.
- [ ] **The window icon is asserted, not observed.** SHIPTEST checks the pixels; nothing here can
      see a title bar. On Wayland GLFW ignores window icons entirely, so on that desktop the fix is
      a no-op by design and a `.desktop` file is the only answer — which is still open above.
- [ ] **`Display.Sizes` still has no entry below 1280×800**, so a 1366×768 player who cycles WINDOW
      in settings walks a ladder where every rung is bigger than their screen (the P17 fit is
      retired the moment they do). Adding smaller rungs is safe only by APPENDING (the index is
      persisted as `SizeIdx`), which makes the cycle order strange — the honest fix is to sort the
      ladder at use and filter it against the monitor, which is a settings-screen wave.

---

## PROGRAM PARALLAX — wave P16 "GROUND TRUTH" (2026-09-04, base `e57e151`, details in DEVLOG §GROUND TRUTH)

- [x] **VOID → RIFT.** Impassable, but **transparent** and giving **no cover** — the only shape on
      this board that stops movement while hiding nothing. Lives in `Grid.IsFloor` (one word, beside
      barrels); deliberately absent from `BlocksSight` / `IsVapor` / `GetCover`, and BIOMETEST
      measures each of those three absences rather than asserting a field is unset.
- [x] **ARID → SOFT SAND.** A step onto sand costs **3** half-tiles (diagonal **5**) instead of 2/3
      — the exact inverse of TUNDRA's ice, and one line in `Grid.CostMap`. `Ai.cs` gained **zero
      lines** for either mechanic; the opponent re-prices itself off the shared cost map.
- [x] **The rift cannot strand anything.** `Terrain.StampRift` re-floods the board through
      `Grid.CostMap` after every candidate tile and reverts any that costs more than itself, so a
      chasm can never seal and the gaps it leaves ARE the bridges. `SIGHTLINE_RIFTTEST`: 576 real
      VOID boards (8 objectives x 6 missions x 6 seeds x heats 0+8), **zero stranded tiles**; with
      the guard disabled the same sweep strands up to 69 tiles on 32 boards.
- [x] **`Terrain.cs`'s contract updated: five mechanical, three paint (STEEL / ASH / NEON)**, with
      the count asserted in both directions.
- [x] **Measured, and reported honestly.** 960 CRN-paired campaigns on `ba34279`
      (`docs/measurements/p16/`): **−2.5 / −1.2 / +1.2** at h0/h4/h8, pooled McNemar z = −0.43,
      95% CI **[−4.7, +3.0]** — near-inert on win rate at **18.3% discordance** (88 of 480 worlds
      played out differently, so this is a bounded effect, not an absent one). **The opener cell is
      clean: `byNodeKind` Start +0.0 / +0.6 / +0.0**, the cell C4's own layer failed at −4.37.

### OPEN, from P16

- [ ] **THE BIOME DEAL IS EIGHT OF FIFTY-SIX, AND FIXING IT IS ITS OWN WAVE.** Verified: `Biome.IndexFor`
      is a fixed 8-cycle with a per-run offset, so over 4,000 seeds only **8 of the 56 ordered biome
      adjacencies** occur and there are only **8 distinct 6-mission sequences** — TUNDRA is followed
      by VERDANT in every campaign ever played. With five biomes now carrying a rule that is a real
      loss of variety. P16 did **not** fix it, for a cost the docket did not know: `Mission.DeckPick`
      reads `Biome.IndexFor` for its theme hint, so re-dealing the biome **re-deals the arena on a
      measured 28.9% of missions**, which severs the CRN chain for every archive under
      `docs/measurements/` exactly as W1 did. `SIGHTLINE_BIOMEDEAL=hash` is the priced, default-OFF
      dial; the wave that spends it owes a re-measured ladder and must say "re-measure, do not
      rescale" in the same breath.
- [ ] **THE SAVE-FORMAT GUARD HAS A HOLE: it cannot see the biome deal.** Measured — with
      `Biome.IndexFor` replaced by a hash, **all three `SIGHTLINE_SAVETEST` map goldens PASS**.
      `MapFingerprint` feeds `Run.GenerateMap`'s output, and the deal is a pure function of
      `(MapSeed, mission)` computed outside the generator. So a change that re-deals **every existing
      save's arenas and biomes** is invisible to the guard whose whole job is to catch exactly that.
      The fix is to feed the regenerated per-mission `(biomeIndex, DeckPick)` pair into
      `MapFingerprint` for the pinned seeds — a golden move, and it should be done BEFORE anyone
      touches the deal, not after.
- [ ] **LANETEST's second vacuity gate still fails under `SIGHTLINE_AIDECLINE=0`** —
      `vacuousWalk(ordinaryLanesArmed=0)` on the 64-campaign dial-ON walk. Pre-existing on base
      `e57e151` and reproduced there; P16 fixed only the dial-OFF walk (16 → 128 campaigns, floor
      unchanged). Same cause: the enemy `overwatch` branch fires on 0.15–0.46% of acts, so any gate
      counting it needs hundreds of campaigns, not tens. **A gate that fails under the project's own
      shipped restore-the-old-behaviour dials is a gate a measurement round cannot use.**
- [ ] **The round cannot separate RIFT from SOFT SAND.** They rode one flag, and each biome is 1
      mission in 8. A per-mechanic price needs `SIGHTLINE_FORCEBIOME` pinned per arm and its own
      slot space — and, at 1-in-8 exposure, a much larger n than 160/rung to resolve anything.
- [ ] **Nobody has looked at whether a chasm makes a fight more INTERESTING.** Win rate is the only
      axis P16 measured. A rift is a positioning lever at constant threat, which W4 identified as
      the one kind of lever that could move `choices/ARMED-soldier-turn` — and nobody read it.

## P18 "THE SECOND AXIS" — left open (2026-09-04, base `f81d3fa`)

- [ ] **THE COLD WAR ROOM ELLIPSIZES FOUR UNLOCK DESCRIPTIONS, AND FITTEST CANNOT SEE IT.**
      `Game.DebugWarRoom` stages the rich twelve-run demo; the zero state (`SIGHTLINE_COLD=1`) has
      no `ScreenCase`, so **the profile every new player is in is unaudited** — the same blind spot
      C6 found and closed for the screenshot hooks but not for the gate. Measured on this tree at
      0 owned / 6 unowned: `room = 264`, `pitch = 52`, `cardH = 44` -> `descRows = 1` -> `Hud.Clip`
      truncates SUPPLY LINE / STANDING ORDERS / ISSUED PLATING / CROSS-TRAINING / STANDING RESERVE
      (see `shots/p18-warroom-cold-locked.png`). **It is PRE-EXISTING** — before P18 the same split
      landed `cardH = 49`, also `descRows = 1`, also clipped — but P18's NEXT COMMISSION row makes
      it 5px tighter, so this wave owns naming it. Adding a `WARROOM-COLD` case would fail the gate
      on a condition P18 did not create; the real fix needs 342px of compact cards in 264px of
      column, i.e. a WAR ROOM layout wave (scroll the column, or a second page, or shorter copy).
      **Do both together or neither.**
- [ ] **NOBODY HAS PRICED THE WIDTH THE SECOND AXIS SELLS.** COMBAT TRIALS (perk offers 2 -> 3),
      DEEP STORES (+1 slate slot) and DEEP RESERVE (12 -> 20 reserve records) are horizontal by
      construction, but "a wider menu is a small edge by SELECTION" is an admitted, unmeasured
      residual. It is **unmeasurable**, not merely unmeasured: the flywheel runs `NoPersist`, so it
      has no meta profile at all and every one of these reads is `!NoPersist`-gated. Pricing them
      needs a harness that can run a batch against a STAGED profile (an env-seeded unlock set, the
      `SIGHTLINE_VETSIM` pattern applied to `MetaUnlock`) — that hook does not exist and is its own
      small wave. Until it does, **no P18 number may claim the axis is free**; `docs/measurements/p18/`
      proves only that the axis is inert at the flywheel's COLD-profile default (240 campaigns,
      three rungs, 2,356 fields, zero diffs).
- [ ] **An `AddHeat` field-event arm can still push a run ABOVE the heat ceiling the player chose.**
      `Events.Apply` clamps through `Heat.Clamp` (i.e. to `Heat.Max`), not to `UnlockedHeat`.
      P18 considered and left it: the arm's preview says "+1 Heat" and delivers exactly that, and
      clamping would neuter the arm for a player already at their cap. Recorded so the next wave
      decides it deliberately instead of by inheritance. (The half that WAS a defect — the same
      event silently confiscating the adaptive assist — is fixed; see DEVLOG §THE SECOND AXIS.)
- [ ] **The perk chooser's dossier stat line runs UNDER the EDIT TAG button.** Visible as
      `SUPPR. FI` in `shots/p18-bonus-perk-picker-wide.png`. Pre-existing on `PERKCHOOSER` (the
      stats string is drawn full-width at `y + 98` and `PerkTagBtn` is placed over its right end).
      FITTEST does not see it because it is an OVERDRAW, not a clip or an off-canvas string — which
      is itself worth noting: the screen audit has no overlap check between a drawn string and a
      later-drawn opaque plate.
- [ ] **The second axis stops at three.** The gates (2 / 5 / 8) span the ladder and the last lands
      ON `Heat.Max`, so the two curves now terminate together — but a player who owns all nine
      unlocks is back where the docket started, one ladder later. Whether that is fine (the ladder
      is finite, so a finite reward track matching it is correct) or whether the top wants a
      genuinely repeatable, non-power sink is an OWNER decision, not a defect to fix by reflex.

---

## PROGRAM PARALLAX — wave "L6 THE LADDER OF RECORD" (2026-09-04, base `6a6ebee`, DEVLOG §L6)

**L6 is the ladder of record** (`CLAUDE.md`, raw round `docs/measurements/l6/`). The "L5 is a
pre-P20 ladder" warning is resolved. **No corrective lever was shipped** — two rungs under floor is
a finding, as in L4 and L5. What L6 leaves open, in priority order:

- [x] **`Mission.Build` DOES NOT OWN ITS OWN GRID — MOVE `Grid.ClearHazards()` INTO IT.** DONE by
      wave **P21 BUILD OWNS THE BOARD** (2026-09-04, base `108d9ac`); see the CLOSING NOTE below.
      **THE DESIGN IS SETTLED; THIS IS A DECISION, NOT A DISCUSSION.** `Grid` has eight per-tile
      arrays. Six are cleared by `Build` and leak nothing. `Fire` is uncleaned but inert (nothing in
      Build reads it). **`Barrel` MOVES THE BOARD**: `Grid.IsFloor` is
      `InBounds && Tiles==Floor && !Barrel[x,y] && !rift` — a barrel sits in the same predicate the
      rift was added to — so `TryApplyLayout`'s accept/reject guard, `EnsureConnectivity`'s carve
      and `PlaceBarrels`' filter all read the PREVIOUS mission's barrels. Measured, one dirty layer
      at a time: dirtying `Barrel` alone moves **Tiles, Height, CoverHp, CoverSeed and Barrel**.
      **It is LATENT, NOT LIVE — no live board defect exists.** `Game.SetupMission` is the only
      production caller of `Mission.Build`, every mode funnels through it, and it calls
      `Grid.ClearHazards()` unconditionally 28 lines before the call. What is wrong is *where* the
      invariant lives: in the caller, not in Build — **the exact arrangement `Ground` had, which
      bit at 8-15% of daily processes (P20)**. One line, in one place, is all that stands between
      here and P20.
      **THE FIX:** move `Grid.ClearHazards()` from `Game.SetupMission` into `Mission.Build`, beside
      `ClearGround`. On every production path it is a no-op (the clear already runs, immediately
      before, and it is idempotent). **It was deliberately NOT shipped in L6** because 96 ladder
      chunks were already measured when it was found, and a board-touching edit — even a provably
      inert one — would publish a ladder measured on a tree that no longer existed. That is the
      mixing the one-lever-per-round rule prevents, and C4 was burned by it once.
      **WHAT THE WAVE OWES:** the move, then MODETEST leg (14a-2) widened from the six layers Build
      clears to all eight with the same dirt (the leg is written to make that a one-line change),
      then a CRN-paired inertness round to prove the no-op — chunks are ~8 s on this box, so
      2 rungs x 16 slot sets is about five minutes. Until it lands, **any new caller of
      `Mission.Build` must clear hazards first**, and `CLAUDE.md`'s `Grid.cs` entry says so.
      **CLOSING NOTE (P21).** `Grid.ClearHazards()` is now called at the top of `Mission.Build`
      behind `Mission.ClearHazardsOnBuild` (`SIGHTLINE_STALEHAZARDS=1` restores the old seam).
      **`Game.SetupMission`'s own call was KEPT, not removed** — a decision with reasons, in
      DEVLOG §P21: removing it is the only part of the change that could alter a live path (it
      would leave the previous mission's fire and barrels live across the ~28 lines between the
      two), it buys nothing once Build owns the invariant, and it keeps MODETEST leg (14b)'s
      SetupMission-seam assertion describing something the caller actually does. MODETEST leg
      (14a-2) now asserts **all eight** layers at the Build seam and its detector is proven able
      to fail on **each owner separately** (`staleGridProbeInsensitive(ground|hazards)`).
      **Inertness measured, not asserted:** 2 rungs x 16 slot sets x 40 campaigns = **1,280 CRN
      campaigns per arm**, 32/32 chunks byte-identical (harness{} excluded) against BOTH the base
      commit `108d9ac` AND the same binary under `SIGHTLINE_STALEHAZARDS=1`. Raw:
      `docs/measurements/p21/`.
- [x] **THE FORK PAYS HAS NO RESTORE FLAG, AND IT BROKE THE CRN CHAIN.** DONE by wave **P21**
      (2026-09-04) — with a limit, stated below. `Run.DepthBase` (10 -> 12),
      `SupplyDiscount`, `PitchedPremium` and `ElitePremium` are `const int`s in `src/Run.cs` with no
      environment switch, against `CLAUDE.md`'s rule that every gameplay lever has one "because a
      wave that cannot be switched off cannot be attributed". L6's bridge to L5 failed on **96 of 96
      chunks** and bisection put the break on that one merge; L6 could only price the wave by
      building milestone 5 as a second tree. **Give the four constants a `SIGHTLINE_FORKPRICE=0`
      arm** (statics read once at class load, the `Terrain.NewGround` pattern) so the chain becomes
      crossable and the L5 archive becomes reachable again. Cheap, and it retires a permanent hole.
      **CLOSING NOTE (P21).** Shipped as `SIGHTLINE_FORKPRICES=0` -> `Run.SetForkPrices(false)`,
      which restores all four pre-milestone-5 prices **as a set** (10 / 14 / 0 / 10 for
      supply/elite/pitched/depth — a partial restore is an economy that never shipped, because the
      depth bump was the redistribution that hands the premiums' intel back). FORKTEST leg (E)
      pins the round trip against a **literal transcription** of the pre-wave `NodeIntel`, and
      counts the re-pricing so a decorative flag fails: **405 of 481 dealt nodes over 40 maps**
      (the 76 that do not move are Event nodes, which pay 0 under both tables).
      **THE ITEM ABOVE OVERSTATED WHAT THIS BUYS, AND THE OVERSTATEMENT IS CORRECTED HERE:**
      *"the L5 archive becomes reachable again"* is **FALSE**. L5's worlds were measured on a tree
      that no longer exists and no flag brings them back; L6's bridge stays broken. What the flag
      buys is that a FUTURE round can isolate that wave's contribution, which was impossible.
      **AND IT IS NOT A COMPLETE ISOLATION.** THE FORK PAYS shipped a SECOND unflagged gameplay
      change in the same commit — the SUPPLY full heal moved from *before* `Run.DebriefSurvivors`
      to *inside* it (`DebriefSurvivors(bool fullHeal)`), so a soldier who ends a SUPPLY clear on
      low HP can now be wounded by it. That is live in the flywheel (SUPPLY is 828 of 5,413 played nodes — 15.3% —
      across P21's 1,280-campaign default arm) and `SIGHTLINE_FORKPRICES` does not touch it.
      **CLOSED by wave P22 as `SIGHTLINE_HEALFIRST=1` — see the next item.**
- [x] **THE OTHER HALF OF THE FORK PAYS IS STILL UNSWITCHABLE (opened by P21).** DONE by wave
      **P22 "NOTHING WITHOUT A SWITCH"** (2026-09-04, base `935d719`). The heal-ordering change
      (`Game.EnterBarracks` -> `cardFullHeal` -> `Run.DebriefSurvivors(bool)`) is now
      `SIGHTLINE_HEALFIRST=1` -> `Run.SupplyHealFirst`, which puts the card's full heal back BEFORE
      the fresh-wound gauge.
      **SHIPPED AS ITS OWN DIAL, NOT FOLDED INTO `SIGHTLINE_FORKPRICES`, on purpose:** the prices
      move the routing ECONOMY (and the map fingerprints with it), the ordering moves squad
      ATTRITION; they have different blast radii, a future round will want one without the other,
      and env vars compose at zero cost. **TO RESTORE MILESTONE 4 WHOLE, SET BOTH:
      `SIGHTLINE_FORKPRICES=0 SIGHTLINE_HEALFIRST=1`.** Neither is a shipping configuration; with
      HEALFIRST on, FORKTEST leg (C) fails by design.
      FORKTEST leg **(F)** is the gate: 248/248 cells reproduce a LITERAL transcription of the
      pre-wave ordering (the caller-side heal, not a re-derivation), 102 of them move off the
      shipped result so a decorative flag fails, and it round-trips. Proven RED before GREEN.
      Priced CRN-paired, `docs/measurements/p22/`; DEVLOG §P22.
- [ ] **THE MODE FORCE HAS NO OFF SWITCH AND NO INSTRUMENT (found by P22's audit, left).**
      Milestone 2 "THE MODES GET THE BESTIARY" gave SKIRMISH and DAILY their own `rosterTier` and
      mid-boss slot with no restore flag. It is **campaign-inert by construction** (`rosterTier`
      defaults to the mission number, `midBossSlot` is `Mode == Skirmish` only), so no ladder
      crosses it and nothing measured is affected. **P22 deliberately did NOT retro-fit a flag,
      because there is nothing to measure it with:** `SIGHTLINE_BALANCE` runs campaigns and this
      project has no staged mode batch. The right order is instrument first, flag second — a flag
      on a change nobody can measure is decoration. Same for m9's mode-depth *fixes* that
      `SIGHTLINE_MODEDEPTH` does cover but nothing prices.
- [ ] **THE AUTOPILOT IS UNFLAGGED, AND IT MOVES EVERY NUMBER (found by P22's audit, left).**
      `Game.Autopilot.cs` is excluded from every "gameplay change" audit in this project — it is
      the instrument, not the game — but a change to `SmartStep`/`AutoStep` moves every measured
      figure, and no wave that has touched it shipped a restore flag. There is no way today to ask
      "is this ladder difference the game or the bot?" across a wave that changed both. A
      `SIGHTLINE_BOTVERSION`-shaped dial is the shape; nothing needs it yet, and P22 did not build
      one speculatively.
- [x] **CLAUDE.md's FLAG DERIVATION WAS NARROWER THAN ITS DESCRIPTION (found and corrected by
      P22).** *"Grep `Program.cs` for `SIGHTLINE_` for the authoritative set"* misses **59
      `GetEnvironmentVariable` sites outside `Program.cs`** (236 names there against 269 across
      `src/`) — mostly harness staging, but including one GAMEPLAY dial: `SIGHTLINE_BIOMEDEAL=hash`
      is read at class load in `src/Util.cs` and re-deals the ARENA on 28.9% of missions. Default
      off and documented by P16, so nothing was broken — but a reader following the instruction
      would not have found it. CLAUDE.md now says grep `src/`.
- [ ] **THE RULE NEEDS A TEST, NOT JUST A STATEMENT (P22).** CLAUDE.md says every gameplay lever
      gets a restore flag. P22's audit proposes the operational form — *a change earns a flag when
      it moves the CRN stream a future round will need to bridge or isolate* — and applied it to 35
      commits by hand. **Nothing enforces it.** The cheap enforcement is a wave-time habit, not a
      script: at merge, diff the sixteen gameplay files against the base with git's `csharp` diff
      driver, group by enclosing method, and answer for each group "which flag turns this off?"
      P22's `docs/measurements/p22/README.md` has the exact command.
- [ ] **THE LADDER'S SOFT SPOT MOVED AND IS NOT LOCATED.** `h6 -> h8` buys **2.5** points, the
      smallest step in the L6 table, and `h0 -> h2` buys 8.4 where L5 read 14.1. C1 located the
      last flat step by measuring **all ten rungs** (a six-rung ladder cannot see which of two rungs
      is the flat one, which is how a dead `AiTier` row survived two programs). **A per-RUNG L6 is
      the diagnostic**, and at ~8 s a chunk it is affordable now in a way it was not for C1.

      across P21's 1,280-campaign default arm) and `SIGHTLINE_FORKPRICES` does not touch it. Flagging it is a separate,
      smaller item: see below.
- [ ] **THE OTHER HALF OF THE FORK PAYS IS STILL UNSWITCHABLE (opened by P21).** The heal-ordering
      change above (`Game.SetupMission` -> `cardFullHeal` -> `Run.DebriefSurvivors(bool)`) is a
      gameplay change with no restore flag, so `SIGHTLINE_FORKPRICES=0` isolates the wave's PRICES
      but not the wave. It is one bool and one branch; FORKTEST leg (C) already reproduces the
      pre-wave ordering in-process (`Clear(true, true)`), so the behaviour is written down — it
      simply is not reachable from the environment. P21 did not widen its own scope to take it.
- [x] **THE LADDER'S SOFT SPOT MOVED AND IS NOT LOCATED.** DONE by wave **L7 EVERY RUNG**
      (2026-09-04, base `935d719`, DEVLOG §L7, raw round `docs/measurements/l7/`). `h6 -> h8` buys
      **2.5** points, the smallest step in the L6 table, and `h0 -> h2` buys 8.4 where L5 read 14.1.
      C1 located the last flat step by measuring **all ten rungs** (a six-rung ladder cannot see
      which of two rungs is the flat one, which is how a dead `AiTier` row survived two programs).
      **A per-RUNG L6 is the diagnostic**, and at ~8 s a chunk it is affordable now in a way it was
      not for C1.
      **CLOSING NOTE (L7).** Ten rungs x 16 CRN slot sets x n=320, heat pinned, `LEAK-CHECK PASS`:
      **70.6 / 44.4 / 41.9 / 35.9 / 26.2 / 23.1 / 22.5 / 11.2 / 6.6 / 8.8**, buys
      26.2 / 2.5 / 5.9 / 9.7 / 3.1 / **0.6** / 11.2 / 4.7 / **-2.2**. `h6 -> h8` is ONE REAL RUNG
      (rung 7, +4.7) AND ONE THAT BUYS NOTHING (rung 8). The negative sign did not replicate out of
      sample (+0.9 on 16 new sets); pooled over 32 sets, n=640, rung 8 is **-0.6, n_disc 70,
      MDE 3.7** — the flattest rung on the ladder. **Six of the nine steps are unresolved at
      n=320/rung** and the round says so rung by rung. The six rungs L6 also sampled reproduced
      **96/96 chunks, 1,920/1,920 legs**, which certifies L6 valid on `935d719` and P21 inert.
      **CAUSE LOCATED, and it is NOT a dead declaration** (`MIDTOOTHTEST` is green and rung 8's
      cumulative vector is correct) — see the new item below. No corrective lever shipped.
- [x] **h0 IS UNDER ITS FLOOR ON TWO SUCCESSIVE LADDERS** (L5 -0.1, L6 -2.6, P23 -2.6; 1.23
      cluster-SE under). L4/C1 flagged the LEVEL at the top of the ladder as the biggest open number
      and it did not move for four ladders. **CLOSED by wave P24 THE TOP OF THE LADDER**
      (`Mission.HostileAimTrim` 0 -> 5, `SIGHTLINE_AIMTRIM=0` restores): h0 reads **49.5 on 32 slot
      sets (45.8 base) and 51.9 on the 16 it shares with P23 (44.4 base)** — in band on both.
      **The reasoning was verified rather than inherited, and it is stronger than it was written:**
      `Heat.Active(0)` yields nothing, so h0's win rate is a pure function of the base game and NO
      arrangement of `Heat.Mods` can move it by any amount. **But the effect itself is not resolved**
      (+3.8 on 32 sets, MDE 5.70) and its two slot halves read +7.5 and 0.0 — see the h0
      heterogeneity item below.
- [ ] **P20's own h4 number should be corrected where it is quoted.** L6 re-priced the lever on 16
      slot sets: **-2.5, n_disc 22, MDE 4.1, NOT RESOLVED**, with P20's own 8 sets giving -4.4
      (z = -2.33) and 8 sets it never saw giving -0.6. `docs/measurements/p20/README.md` still says
      "h4 is resolved: the fix is a -3.8-point tightening". It was resolved *on those eight sets*.
      **A rung is sixteen slot sets. So is a lever** — third measurement of that shape after L5's
      split-half and W2's four-vs-sixteen.
- [ ] **SLOT 46's MISSION-1 `Eliminate` DEADLOCKS BOTH POLICIES.** It stalls at h0/h2 (sloppy) and
      h6/h8 (greedy), `runTurns` 51 every time; L5 saw the same world stall at h0/h2/h4 on sloppy
      alone, so it has survived a board-moving wave and spread to the other policy. **L7's per-rung
      ladder widened it again: SEVEN of the ten rungs** (h0/h1/h2/h3 sloppy, h6/h7/h8 greedy). That makes it a
      property of the opener, not of a rung or a policy — and the most reproducible autopilot case
      anyone will get: `SIGHTLINE_BALANCE_BASE=40`, slot 46. The stalemate arm is 1.77% of L6's
      1,920 campaigns and it is an instrument floor, not a ladder effect (the RUN arm has now fired
      **zero** times across three ladders).
## PROGRAM PARALLAX — wave "L7 EVERY RUNG" (2026-09-04, base `935d719`, DEVLOG §L7)

**L7 is a per-rung SUPPLEMENT to L6, not a new ladder of record** — it reproduced L6's six rungs
campaign for campaign (96/96 chunks, 1,920/1,920 legs), so `CLAUDE.md` keeps L6 as the headline and
L7 supplies the four rungs nobody had ever measured. **No corrective lever was shipped and no `src/`
file was edited.** What L7 leaves open, in priority order:

- [x] **THE APEX RUNG CANNOT REACH THE MISSION THAT DECIDES A CAMPAIGN. THIS IS THE ONE TO FIX.**
      **DONE by wave P23 "THE APEX BITES"** (2026-09-04, base `fd07d56`, DEVLOG §P23, raw round
      `docs/measurements/p23/`). Both halves shipped as **two independently switchable levers**:
      `Mission.ClampLast` (`SIGHTLINE_CLAMPLAST=0`) applies the board-seating ceiling to the force
      that is SEATED instead of to the number the ladder ASKED for, and `Mission.FinaleHeatStat`
      (`SIGHTLINE_FINALESTAT=0`) separates the finale's stat strip so the deployment CARD's stat is
      still dropped and HEAT's is not. The finale now fields **6/7/7/8/9/10/10/10/11** at heats 0-8
      against 6/7/7/8/9/9/9/9/9, and its `bump` **4/5/5/6/6/6/6/7/8/9** against a flat 5.
      **The ceiling was NOT raised** — leg (E) measured the board seating 16 bodies, distinct and
      reachable, so 12 is not a layout constraint, but the defect was the ORDER and the order fix
      needs no extra seat. Priced: pooled over h4/h6/h8 (n=1,920/arm) lever A is −1.30 (z −2.14),
      lever B −1.25 (z −2.25), both −1.88 (z −2.74), **additive within ±0.9**; h8 base->AB −2.7 at
      32 slot sets (z −2.53, t(31) −2.87) and −3.1 out of sample (b=1 c=11, z −2.89, RESOLVED).
      RECRUIT moves the other way, +2.8. **The costs are published, not tuned away** — see the new
      top item below.
      **The original L7 finding, kept for provenance:**
      `Mission.Build` sizes a force as `Math.Clamp(EnemyBaseCount + n + enemyDelta, 3, 12)` and the
      finale then runs `count = Math.Max(5, count - 3or4); bump = Math.Max(0, n - 1)`. At mission 6
      the request is `10 + EnemyDelta` against a ceiling of 12, so **from heat 3 up heat's bodies
      stop arriving**, and `bump` **discards heat's StatDelta outright at every rung**. NO QUARTER's
      two declared teeth are +1 body and +1 stat, so **both are switched off on mission 6** and the
      rung's whole reach is missions 1-4. Measured on the artifact (one seed, the HUD's own hostile
      chip, `docs/measurements/l7/m6-force-by-heat.png`): the finale fields **6/7/7/8/9/9/9/9/9**
      hostiles at heats 0-8 — **it has not grown a body since heat 4**, and the h3->h4 body is the
      `Ai.Tier >= 1` trim gate, not an EnemyDelta. At h7 and h8 the two screenshots differ in
      nothing but the HEAT chip.
      **THE FIX IS A DESIGN DECISION, NOT A BUG FIX**, which is why L7 did not ship it: does the
      apex deserve a body the clamp cannot eat (raise the ceiling, or apply heat's delta AFTER the
      finale trim), a stat the finale does not discard (`bump` was written to strip the BOSS CARD's
      stat and takes heat's with it — those could be separated), or a smaller carrot (below)? Each
      is one lever and each needs its own CRN round against a fresh baseline. **Whichever is chosen,
      it must be measured at 16 slot sets minimum** — rung 8's step has an MDE of 3.7 at n=640.
- [x] **NO TEST IN `src/` ASKS WHAT FORCE THE BOARD ACTUALLY BUILDS.** **DONE by wave P23** —
      `SIGHTLINE_FORCETEST` (in `qa-sweep.sh`, routed through `verdict`, visible to the COVERAGE
      GUARD). Seven legs over 3 seeds x RECRUIT-h8 x m1-6, all reading the force `Mission.Build`
      actually assembled: no rung's declared body or stat may be eaten (RED pre-fix on all three
      seeds), the opener stays FLAT across rungs (§3.D as an assertion), the stat is on the BODIES
      and not just the telemetry, every hostile on a distinct reachable tile (shipped and with the
      ceiling stressed to 16), both dials real and independent, and the ordering a no-op wherever
      the ceiling never bound. `SIGHTLINE_FORCEDUMP=1` prints the whole matrix.
      **The original L7 finding, kept for provenance:** `HEATLADDERTEST` pins the
      cumulative heat vector; `MIDTOOTHTEST` pins the per-rung deltas, the apex vector and the
      no-dead-declaration rule. Both were **green** while rung 8's correct `EnemyDelta 4` was being
      clamped away and its `StatDelta 4` discarded. C1's defect was a declaration the vector could
      not carry; this is a vector the BUILD does not honour — the same class with the arrow
      reversed, and the same two programs of silence. **A probe that walks (rung x mission) and
      prints the force `Mission.Build` actually produces would have caught both**, and it is the
      natural companion to `MIDTOOTHTEST`. Name it `...PROBE`/`...TEST` so the sweep's COVERAGE
      GUARD can see it (its alphabet is `TEST|GATE|PROBE`).
- [ ] **`Heat.IntelBonus` IS AN ACCELERATING CARROT WITH NO OFF-SWITCH.** `3n + n^2/2`, so the
      per-rung reward increments are +3 +5 +5 +7 +7 +9 +9 **+11** — the reward side of a rung grows
      monotonically and un-clamped while the stick's two quantitative components saturate, and the
      **biggest carrot on the ladder is attached to the rung with no stick left**. Measured
      (n=320/rung): heat-bonus income 94.8 -> 108.2 per run and total earned 184.9 -> 190.2 from h7
      to h8 **despite h8 clearing 0.19 fewer missions**. `Heat.IntelPerLevel` is a `const` with no
      restore flag, so no arm in L7 could switch it off — it is NAMED as a counterweight, not
      attributed as a cause. Give it a flag (the `Run.SetForkPrices` pattern) before pricing it.
- [ ] **RUNG 5 STILL BUYS ~NOTHING, THREE PROGRAMS ON.** C1 measured LINGERING WOUNDS at 0.0 +-3.2
      and left two hypotheses. L7 settles one: **the +1 body IS eaten at the finale** (h4 and h5
      both field 9 hostiles at m6, on the artifact) and on ELITE nodes from mission 4. The other —
      "HarshAttrition compounds over a run length the bot rarely reaches" — is **still unmeasured**;
      `avgMissionsCleared` at h5 is 3.56, but no arm isolates the flag. Pooled over 32 slot sets the
      rung reads **+2.3, MDE 5.7 — not resolved**, so "it buys nothing" is still not a measured zero.
- [ ] **FOUR RUNGS OF THE SHIPPED LADDER HAVE NO GOAL BAND.** The FUL-13 band exists only for
      `{RECRUIT, 0, 2, 4, 6, 8}`. h1/h3/h5/h7 have never had one, so L7's `cluster.py` prints `-`
      for them rather than interpolating a band and grading against it. Now that a per-rung ladder
      is affordable (~8 s a chunk), setting the four missing bands is cheap and would make the
      shape a gradeable property instead of an eyeballed one.
- [x] **THE FINALE'S `bump` STRIP CANNOT BE MEASURED AT ALL.** **DONE by wave P23** —
      `SIGHTLINE_FINALESTAT=0` is that restore flag, and P23's round is the first to price the stat
      half on its own arm: pooled over h4/h6/h8, **−1.25 (n=1,920, n_disc 114, z −2.25)**, against
      lever A's −1.30. The two are worth about the same, which is precisely what L7's combined arm
      could not have told anyone.
      **The original L7 finding, kept for provenance:** `SIGHTLINE_ENEMYBASE` relieves the
      headcount clamp (L7's clamp arm used it: rung 8 buys +4.1 with the ceiling clear against -0.6
      with it binding, DiD +4.7 +-2.0, t=2.39 on the absolute scale but **z = -1.66 on the odds
      scale, NOT resolved**). Nothing relieves `bump = Math.Max(0, n - 1)`. That is why the base-2
      arm's whole recovery came from missions 2-5 and its m6 conditional still read h7 25.5% vs h8
      27.5%. **A restore flag on the finale's stat strip is a precondition for pricing the fix
      above.**

- [ ] **THE SCREEN AUDIT STILL HAS NO OVERLAP CHECK, AND NOW NEITHER HALF OF THE BOARD GATE DID.**
      L6 widened `BoardSignature()` from Tiles + Height + seats to all eight layers, because the
      gate that caught P20 was blind to the layer next door. That is one instance of a pattern
      worth a pass of its own: **ask of each gate what it would have said if the thing it guards
      were broken.** (P18's open item about `FITTEST` having no drawn-string-vs-plate overlap check
      is the other live instance.)

## PROGRAM PARALLAX — wave "P23 THE APEX BITES" (2026-09-04, base `fd07d56`, DEVLOG §P23)

P23 closed three of L7's six items (the apex's dead teeth, the missing force guard, the finale
stat's restore flag). What it leaves open, in priority order:

- [x] **THE LADDER'S TOP HALF IS NOW UNDER ITS BAND, AND P23 DELIBERATELY DID NOT TUNE IT.**
      **SPENT by wave P24 THE TOP OF THE LADDER** (base `3b684a7`, `docs/measurements/p24/`, DEVLOG
      §P24) — one lever, `Mission.HostileAimTrim` 0 -> 5, `SIGHTLINE_AIMTRIM=0` restores. Chosen as a
      LEVEL lever because every heat rung sat under its band CENTRE by a mean of −7.6, and because
      **`Heat.Active(0)` is empty — no arrangement of `Heat.Mods` can move h0 by any amount.**
      Bridge 96/96 chunks and 1,920/1,920 legs against P23's AB arm (48/48 more out of sample).
      Result, best n per rung: **RECRUIT 76.6 / h0 49.5 / h2 40.0 / h4 26.1 / h6 11.9 / h8 4.8.**
      **h0 is FIXED** (45.8 -> 49.5 on 32 sets; 44.4 -> 51.9 on the 16 it shares with P23).
      **h6 is HALF-fixed** — 9.4 -> 11.9, from 2.6 under its floor to 0.1 under it: it now sits ON
      the floor, which C1's precedent says may not be claimed as in band. **h8 did not move at all.**
      The two items immediately below are what is left.
- [ ] **THE APEX DOES NOT RESPOND TO ACCURACY, AND h6/h8 STILL SIT ON THEIR FLOORS (opened by P24).**
      h8 measured **−0.6 / +0.3 / −0.2** on the original 16 slot sets, 16 new ones and all 32
      (n_disc 43, **MDE 2.87 — the tightest contrast in the round**), against a positive point
      estimate at every other rung. So the next corrective lever must come from the quantities
      accuracy cannot reach: bodies, HP/stat, per-hit damage, or the coordination flags at rungs
      3/4/6. Two constraints it inherits. **(1) It cannot push the top down** — h8 is at 4.8 against
      a >= 5 hard floor. **(2) It must budget for power**: *every* per-rung contrast in P24 was
      unresolved at n=640, so a lever aimed at h6/h8 alone needs roughly 4x the clusters to price
      itself. That is affordable now (~8-20 s a chunk) and was not when the n=320 convention was set.
      Dials still named and unspent: `SIGHTLINE_FORCECEILING`, `SIGHTLINE_ENEMYBASE`,
      `SIGHTLINE_TOUGH`, `SIGHTLINE_TRIM`, a partial `FinaleHeatStat` as a fraction of `heatStat`,
      and — for h6 ALONE — `SIGHTLINE_MIDTOOTH=7` (next item).
- [ ] **C1's APEX-NEUTRAL h6 REDISTRIBUTION IS AVAILABLE, UNMEASURED ON THIS TREE, AND P24 REJECTED
      IT ON PURPOSE.** `SIGHTLINE_MIDTOOTH=7` (bit 4) moves rung 6's anonymous +1 stat UP to rung 7,
      leaving the h7 and h8 cumulative vectors identical and lifting h6 alone — C1 measured
      **18.4 -> 23.1, +4.7 ±2.7** on its own tree. **So "the heat table cannot raise h6" is FALSE and
      should stop being repeated**; the true, narrower claim P24 verified is that the heat table
      cannot raise **h0** (`Heat.Active(0)` is empty). P24 rejected the redistribution because it
      cannot touch h0, because it buys h6 by making rung 6 buy less (L7: rung 6 is **+11.2**, one of
      only three resolved steps, next to rung 5's +0.6), and because it leaves the level shortfall
      exactly where it is. A wave that decides h6's band verdict matters more than rung 6's step has
      the dial — but it must re-measure the +4.7 here and read L7's rung-6/rung-5 numbers first.
- [ ] **h0 IS A HETEROGENEOUS RUNG AND SIXTEEN SLOT SETS DO NOT SETTLE IT (opened by P24).** P24's h0
      contrast read **+7.5 (z +2.68) on its first 16 slot sets and exactly 0.0 (b=45, c=45) on the
      next 16**; pooled over 32 it is +3.8 with MDE 5.70, not resolved. The BASE arm's own h0 differs
      by half too (44.4 vs 47.2), as does h8 (6.6 vs 3.4). **Fifth sighting of this shape** after L5's
      split-half on L4, W2's four-vs-sixteen, L6's re-price of P20 and L7's rung-8 sign. Any future h0
      claim needs 32 slot sets minimum, and probably more.
- [ ] **THE `levers{}` BLOCK IS ASSERTED BY ONE WAVE'S RUNNERS, NOT BY THE RUNNER OF RECORD.** P24
      added a `levers{}` block to the balance JSON (aimTrim, toughness, damageTrim, enemyBase,
      openerTrim, forceCeiling, clampLast, finaleHeatStat, midTooth) so a chunk records which ARM it
      measured instead of leaving that to its file name — the gap behind P15's silent heat fallback
      and C4's shipped-is-not-measured layer. `p24/run_round.sh` and `p24/chunks.sh` assert it;
      **`p15/check_chunk.py`, layer (c) for every round, still does not look at it.** Folding an
      optional `--lever k=v` assertion into that shared script would make the fourth layer available
      to every future round. P24 deliberately did not edit the runner of record inside a measurement
      wave.
- [ ] **THE MID-RUN CEILING STILL BINDS, AND IT IS NOW THE ONLY PLACE IT DOES.** With the ordering
      fixed, the remaining cells where a rung's declared body does not reach the board are the ones
      where the board is genuinely full: ELITE mid-run nodes (`+2` from the card) at heat 3 and up,
      and plain m5 at heat 8. `SIGHTLINE_FORCETEST` leg (A2) COUNTS those cells and prints the
      count in its PASS line rather than hiding them, and the question is whether they should be
      allowed past 12. **The seating answer is already measured: they can be** — with
      `Mission.ForceCeiling = 16` and `EnemyBaseCount = 8`, asking for 13-16 bodies, the board seats
      **16, all on distinct tiles, all reachable from the squad, on every mission x rung x 3 seeds**.
      So this is purely a difficulty decision, and `SIGHTLINE_FORCECEILING=<n>` is the dial. It
      would need its own CRN round; note it lands on missions 3-5, not on the finale, so it is a
      DIFFERENT lever from P23's and should not be folded into a re-measure of it.
- [ ] **FOUR OF P23's SEVEN CONTRASTS SIT AT THE EDGE OF THE ROUND'S OWN MDE, and one of them is
      the headline.** Pooled over h4/h6/h8 (n=1,920/arm) the levers read −1.30 / −1.25 / −1.88 with
      McNemar z of −2.14 / −2.25 / −2.74 against an MDE80 of 1.71 / 1.56 / 1.91 — significant at
      p ≈ 0.006-0.03 and the same size as the smallest effect the design can reliably see. The apex
      resolves outright only on the 16 slot sets the round had never seen (−3.1, b=1 c=11,
      z = −2.89). **Nobody should quote these as precise quantities.** If a later wave needs the
      size rather than the sign, it needs roughly 4x the clusters — which is affordable now
      (~8-20 s a chunk) and was not when the convention was set.
- [ ] **THE FINALE'S OWN LADDER STILL HAS ONE STEP BOUGHT BY A BOOLEAN.** `h3 -> h4` grows a finale
      body because the de-stack gate flips −4 to −3 when `Ai.Tier >= 1`, not because the heat table
      declared a body there. It is legal, deliberate (W6 SIGNAL) and now measured — but it means one
      rung of the finale's curve is a step function riding a different axis, and if `Heat.AiTier`
      ever moves, the finale's headcount moves with it silently. `SIGHTLINE_FORCETEST` would see the
      result but would not name the cause.
- [ ] **P23 SAMPLED SIX RUNGS OF TEN.** RECRUIT, h0, h2, h4, h6, h8. h1/h3/h5/h7 are unmeasured on
      this tree, so L7's "rung 5 buys ~nothing" is only half-answered: lever A restores the body the
      finale was eating at h5 (m6 9 -> 10), but whether LINGERING WOUNDS now buys anything is not
      measured. A per-rung re-run is ~10 minutes of wall time on this container.

### WAVE P25 "NOBODY HAS LOOKED" (2026-09-04, base `bea240c`, details in DEVLOG §P25)

**Pillar 2's SECOND instrument.** `FEELTEST` measures the tween; `SIGHTLINE_JUICETEST` measures the
ANSWER — the feedback footprint (SEEN / HEARD / FELT / READ / latency) of every shot outcome, all 23
action-bar verb rows + MOVE, 9 damage routes, and the shot's frame-by-frame beat. **Before quoting
any number out of it, read the "WHAT IT CANNOT SEE" block above `Game.JuiceSelfTest`** — it cannot
hear a cue, cannot see a pixel, cannot judge whether 9 reads as heavier than 5, and cannot measure
fun. Raw round: `docs/measurements/p25/`.

- [x] **The instrument ships and is a gate.** `SIGHTLINE_JUICETEST` in `qa-sweep.sh`, routed through
      `verdict` (86 → 87 hooks, no COVERAGE GAP). 2.7 s, byte-identical across runs. **22 deliberate
      breaks prove every assertion can go red** (`docs/measurements/p25/red-demonstrations.txt`).
- [x] **DEFECT: a whiff out-punched contact.** MISS shook 2.5, GRAZE 2.0. Graze → 3.0. Gated.
- [x] **DEFECT: the frag was the only blast with no bloom** while the barrel (0.5) and the BOMBARD
      strike (0.4) had one — and all three play the same cue. Grenade → `AddBloom(0.35f)`. Gated by a
      new BLAST-CLASS assertion: routes that share a cue must answer on the same channels.
- [x] **Two assertions of the instrument's own were found to be theatre and replaced** — a text
      COUNT that could not see a deleted death word, and a `Seen` sum that counted anims ENQUEUED
      rather than anything drawn. Both were caught only because every assertion had to be shown red.

**Open, in priority order:**

- [ ] **BURN AND BLEED ARE THE ONLY DAMAGE IN THE GAME WITH NO SOUND AND NO WEIGHT.** `Game.EnvDamage`
      sets a flash, throws 8 particles, pops a `-2 BURN` word, and touches neither `Audio` nor
      `AddShake`/`AddHitStop`. Its caller-fronted uses are covered (`STRIKE`/barrel boom before the
      call, `SLAM` behind ShoveAnim's `hunker`), but the two that ARE the event — the `Burning` tick
      in `TickStatuses` and the `Bleed` tick in `OnUnitEnteredTile` — arrive in silence, and there is
      no `fire` recipe in `Audio.BuildRecipes` at all. **P25 deliberately did not patch this**, and
      the reason is the spec for whoever does: (1) the cue must fire **once per tick PASS, not once
      per unit** — a burning pod of three would otherwise stack three cues on one frame, the exact
      defect P14 removed from the overwatch path; (2) it needs a NEW recipe (the cue map is
      injective and `hit`/`crit` mean "a round landed"), and nobody in this sandbox can hear one.
      This is an owner-listen item, not an agent patch.
- [ ] **THE ALWAYS-ON COMBAT LOG RECORDS SHOTS ONLY.** `Stats.Log`'s only gameplay call site outside
      the voice barks is `ShotAnim.Apply`. DESIGN.md §3.B sells the log as the antidote to
      output-randomness rage; a grenade, a barrel, a BOMBARD strike, a burn tick, a bleed tick and a
      shove slam leave no line in it — including the two that kill a soldier while the player
      watches. A pillar-3 hole found by a pillar-2 instrument. `JUICETEST`'s `read` column is the
      measurement; adding the lines is cheap and gateable.
- [ ] **TWELVE MODAL VERB ARMS FIRE NO CUE.** FIRE, GRENADE, GRAPPLE, MARK, SUPPRESS, all four items,
      SHOVE, DRAG, VAULT: arming plays nothing, while *selecting a unit* plays `select` and every
      committing verb has a cue. Reported and NOT asserted, twice over: the arm's real answer is a
      renderer overlay `JUICETEST` cannot draw, and the cheap fix (reuse `select`) would make "I
      armed FIRE" and "I clicked a soldier" the same sound — the disease THE CUE MAP cured. Needs a
      new sound and ears.
- [ ] **THE HOTKEY PATH REFUSES AN ILLEGAL VERB IN SILENCE.** `Game.cs`'s mouse path checks
      `b.Enabled` before `DoAction`; the keyboard path calls `DoReload()` / `ToggleAim()` / ...
      directly, so `R` on a full clip or `1` with no ammo returns with no sound, no word and no
      shake. `ui_no` already exists as a registered recipe (the shop's refusal). Cheap; needs a
      decision about how noisy a refusal should be.
- [ ] **THE WEIGHT LADDER SATURATES AT THE TOP.** Hit-stop is 0.100 for CRIT, KILL *and* CRIT-KILL
      (`AddHitStop` is a MAX); bloom is identical for KILL and CRIT-KILL (`willKill ? 0.13f : ...`
      discards the crit once lethal); shake's 18.0 at CRIT-KILL is `Fx.AddShake`'s own ceiling
      clamping a raw 21. A crit-kill out-punches a plain kill on **particle count and text size
      only**. The instrument can prove the numbers are equal; it cannot say whether that reads as
      flat. Eyes, then possibly a raised ceiling.
- [ ] **THE ARM IS UNMEASURABLE HEADLESSLY, AND THAT IS THE INSTRUMENT'S BIGGEST HOLE.** Everything
      the renderer draws in response to state — targeting overlays, range wash, threat cards, the
      cover shield, the kill-zone wash — is invisible to `JUICETEST` and only partly covered by the
      pixel probes (`BOARDTEST`, `CONTRASTTEST`, `FITTEST`, which read a rendered frame but do not
      diff a before/after pair). A probe that renders TWO frames and diffs them would close it and
      would make the 12 arm rows assertable. That is its own wave.

---

### WAVE P26/P27 (2026-09-07..14, base `74b0e5a`, details in DEVLOG §P26/P27 and `docs/REVIEW-2026-09.md`)

**SHIPPED.** External design review adjudicated against code + archive; two shipped lies fixed
(four unobtainable perks documented in the FIELD MANUAL, the max-pressure banner promising
reinforcements that cannot arrive on ELIMINATE); the flywheel's per-mission tables split by POLICY
so "does skill matter" is readable from a chunk; `Mission.PlanBoard` so an arena can own its
objective sites and spawns; and `src/View3D.cs` + `src/Mesh3D.cs`, a projected-camera prototype.

#### OPEN — the agreed direction (owner decisions already made, see DEVLOG §P27-N)

- [x] **Bake lighting into the TERRAIN.** DONE (P30/1). `Mesh3D.BevelBox` — 6 faces + 12 edge
      strips + 8 corners, key light baked per-vertex, one unit mesh scaled per draw (the lighting
      is baked, so a non-uniform scale cannot break shading the way it would break a runtime
      normal). `View3D.Solid`'s body was replaced and its signature kept, so every call site gained
      it at once. **Two real bugs found by LOOKING at a render, neither by a test:** `Mesh3D.Key`'s
      Z sign lit the faces pointing AWAY from the camera (the lathed chip hid it — rotationally
      symmetric, so the X term still made a gradient; a box cannot), and both Y faces plus half the
      X-Z edge strips were wound backwards and were being back-face culled, which drew a block as a
      dark hole with a lit rim. Winding is now ASSERTED against each face's declared normal rather
      than hand-checked.
- [x] **The 3D view knows about edges.** DONE (P30/1). `View3D.DrawEdges` — a wall is a thin slab
      straddling the grid line, never a filled cell; a door is two jambs and a lintel. Before this
      a building placed under `SIGHTLINE_BUILDINGS=1` was INVISIBLE in 3D.
- [x] **A DISCOVERY layer.** DONE (P30/2). `src/Vision.cs`: Unseen / Remembered / Visible, per TILE
      and per EDGE FACE. Unseen is not drawn at all — not dimmed, not hinted. A wall observed from
      one side renders as a half-thickness plane flush to the observed face, so the operator cannot
      read a depth nobody has been round the back to measure. It reads `Grid.HasLineOfSight` — the
      same predicate that decides whether a soldier can SHOOT — so what is shown and what the rules
      permit cannot drift. Sight is the owner's own rule from §P27-N: 9 tiles, +1 per level below.
      `SIGHTLINE_DISCOVERY=0` restores an all-known board. **Presentation only** — nothing in `Ai`,
      `Combat` or `Mission` consults it.
- [x] **Make a template dimension mismatch a LOUD failure.** DONE (P28). `Mission.TryApplyLayout`
      now separates a wrong SIZE (a template bug) from a connectivity rejection (a runtime
      outcome): it counts `Mission.LayoutDimMismatches` and writes a named warning to stderr.
      `SIGHTLINE_TEMPLATEGATE` checks all 36 authored arenas against `Cfg.GridW/H` up front, so
      the failure is caught before a mission is ever built. **Verified RED before GREEN** — a
      one-column truncation of `Maps.Layouts[0]` fails the gate by name.
- [x] **`SIGHTLINE_BIGMAP` prototype.** DONE (P29). `SIGHTLINE_BIGMAP=<W>x<H>[x<TILE>]`;
      `Cfg.GridW/H/Tile` went `const` -> `static` with `Cfg.SetBoard`, read before any `Grid` is
      built. **Two claims in this item were wrong and are corrected here:** it is not "five
      `Cfg.GridW/H` references" — it is **10 in game code and 12 more in the harness**, derived,
      not remembered. And the camera blocker was one line only in the sense that one line had to
      GO: `CamPan = Vector2.Zero` when `CamZoom <= 1.001f` was replaced by pan bounds DERIVED from
      where the board's edges land on screen (`Game.ClampPan`, pure and tested), which allows pan
      whenever the board overflows the view at any zoom, produces the old no-pan behaviour by
      arithmetic when it fits, and is tighter than the `±BoardW/2` it replaced — that let a
      zoomed-in player drag the board halfway off screen. `Cfg.OriginY` also had to start centring
      once the board is taller than the screen, or `CamPan = 0` was a view pinned 188px low.
      Measured working at 40x28@42px: autoplay WIN and LOSE, no exceptions.
- [ ] **Do NOT author site glyphs into the existing 35 18x11 templates.** They are orphaned by a
      size change. The P26 glyph vocabulary (`T X E C P A`) is the authoring format for the NEW maps.
      P29 made the orphaning EXPLICIT rather than silent: `Maps.TemplateW/H` is the size the arenas
      were drawn for, `Mission.ArenasFitBoard` asks whether the live board can use them, all three
      arena branches are gated on it, and `SIGHTLINE_TEMPLATEGATE` now answers two separate
      questions — are the templates self-consistent (a FAIL) and can this board use them (a
      report, since OUT OF PLAY is deliberate under BIGMAP).

#### OPEN — the projected view, after P30

- [x] **The props are boxes no longer.** DONE (P31). `assets/props/*.glb` is copied by the
      `.csproj` AND listed in `Ship.RequiredFiles` — both, together, or C6's cwd fallback resolves
      a missing file off the repo and the build is broken for a player and green for a developer.
      SHIPTEST confirms 15 bundled files resolve next to the binary, none via the fallback. Cover
      picks its prop from `Grid.CoverSeed` (documented as "PURELY VISUAL: stable per-tile identity
      of the drawn cover VOLUME" — exactly the right slot, already persisted and already ignored by
      every rule): VERDANT's high cover is a TREE, elsewhere a crate stack alternates with the
      plain block. Props carry their OWN baked key light and AO, so they get a separate tint lift —
      tinting them like a flat block darkens them twice and lands the mesh as a black silhouette.
- [x] **The biome reached the 3D view.** DONE (P31). `View3D.Scene`; cover and plateaus take the
      same 0.55 hue pull `Renderer.DrawCover` uses, and the floor checker comes from the biome.
      RESONANCE V3's finding about the 2D board — "forty-five grey widgets in a coloured room read
      as a whitebox level" — had been quietly reintroduced by the projected view.
- [x] **The GROUND layer reached the 3D view.** DONE (P31). Undergrowth blades, ice drift, soft
      sand and vent embers, deterministic from `Util.Hash3` (never `Util.Rng`: this runs inside the
      draw, and a stream-seeded shape would both crawl between frames and spend draws the flywheel
      counts). Before it, a player could not SEE the fern giving them cover or the fissure about to
      set them alight — rules with no picture attached.
- [ ] **The car mesh is unused, because the game has no multi-tile obstacle.** `car.glb` spans two
      tiles and `Grid` has no concept of one object occupying several. That is a DATA-MODEL gap,
      not an art one — see P28's notes on tile obstacles vs edge walls.
- [x] **Four biomes have a species.** DONE (P36). `rock.glb` (ARID), `slag.glb` (MAGMA) and
      `sign.glb` (NEON) join VERDANT's tree; `View3D.BiomeSpecies` routes them. STEEL, ASH, TUNDRA
      and VOID keep the crate on purpose — a depot, a burn scar, a snowfield and a chasm are rooms
      where a crate is the right answer. **A species is a change of MATERIAL, not of what the tile
      does**: all three are built to the crate's envelope and swapped in on the same scale factors,
      so the silhouette a player reads as "waist-high thing I can shoot over" is the same in every
      room, and nothing here is read by `Grid`, `Ai` or `Combat`.
- [x] **The REMEMBERED tier is drawn as a wireframe, for any object.** DONE (P38). `src/Wire.cs`:
      a hard-edge extractor that is a pure function of a `Mesh`, so the prop kit, the procedural
      bevelled block and anything loaded later all get a wireframe with no per-asset preparation.
      `View3D._wire` is the ONE SEAM — set per tile and per edge from `Vision`, read by the solid
      primitives — so a draw site added later gets the tier free and cannot forget it.
      **It is a GEOMETRY answer, not a shader one, and that was the deciding argument**: a shader
      wireframe (barycentric) needs custom per-vertex attributes, which means un-indexing and
      re-authoring every mesh, to solve a problem we do not have (lines drawn OVER a lit solid —
      the three tiers are mutually exclusive). The hard-edge set is also 5-10x smaller than the
      triangle set, and most of a discovered board is remembered, so the common case got faster.
      **`Raylib.DrawModelWires` exists and is the wrong answer**: it draws every TRIANGLE edge, so
      a bevelled box shows the diagonal split of all 26 of its quads. `Wire.Mode = AllEdges`
      reproduces it exactly and is kept as the comparison — on the kit it is 66 edges against 48,
      132 against 96, 486 against 108.
      **The weld is the load-bearing part and the trap**: the kit is flat-shaded with per-corner
      vertex colours, so no two triangles share an index. Key edges on indices and every edge looks
      like a boundary, the crease test never runs, and the "hard edge" pass silently returns the
      full triangle net — a working-looking wireframe that is one by accident. `SIGHTLINE_WIRETEST`
      leg (A) pins it on arithmetic, not on a measurement: a unit cube is exactly 12 hard edges, 18
      unique edges and 8 welded corners. Verified falsifiable — remove the weld and it reads 24/30.
- [x] **Confidence.** DONE (P39). A remembered line's brightness IS the squad's confidence in it,
      and the number comes from what the squad did: `Vision.SeenDist` (how close it stood) and
      `Vision.SeenAt` (how long ago), composed by `Vision.Score`. Recorded only while a surface is
      VISIBLE, so they freeze when sight is lost and age on their own; looking again overwrites
      them, which makes "go and look at it" the way to restore confidence. The CLOSEST observer
      wins, not the last one in the list. Low confidence also loses saturation — two cues beat one
      — and the range is floored well above zero, because a memory this layer stops drawing is a
      memory the player is not told they have.
      **THE SPLIT BETWEEN DATA AND SHADER IS THE POINT.** Most of "confidence" is INFORMATION and
      belongs in the data; it reaches the screen through the colour each line is already handed, and
      a shader that derived it would be a second model of the same thing. The shader does only what
      a per-line colour cannot: vary the picture ALONG a line and ACROSS the board — horizontal scan
      planes the reconstruction brightens through, and a sweep travelling over it.
      **raylib's DEFAULT VERTEX SHADER DOES NOT GIVE YOU WORLD POSITION**, and this is the trap: ask
      a fragment shader for `in vec3 fragPosition` against it and the program still LINKS, still
      reports valid, and draws nothing — which is exactly what this wave's first spike did. `Wire`
      ships its own vertex shader; for rlgl's batched lines the submitted vertices already ARE world
      coordinates.
      One bind per PASS, never per object (a bind per tile is 1,120 batch flushes a frame to change
      nothing between them), and the whole wireframe pass is skipped when discovery is off.
      `SIGHTLINE_WIRESHADER=0` is the no-shader path — the same one a driver that refuses the
      program falls back to — and `SIGHTLINE_WIREGAIN=<0..1>` dials the effect; at 0 the shader is
      bound and is a mathematical identity, which is what `SIGHTLINE_CONFTEST` leg (E) proves **in
      pixels**, because a leg that only asks "did it compile" would have passed on the spike's
      broken program.
- [ ] **Constant-width lines are still not possible.** `Rlgl.SetLineWidth` is not exposed by
      Raylib-cs 8.0, and core-profile GL ignores `glLineWidth > 1` on most drivers anyway. A line
      that does not thin at distance needs quad-expanded geometry (two triangles per edge, billboarded
      in a vertex shader), which is a real change to `Wire.Draw`'s output and worth doing only if the
      1px line ever actually reads as too faint.
      `Wire.CreaseDeg` (25 degrees) is the dial if the line work reads too busy — an icosphere rock
      keeps every edge at that value, which is correct and is also the noisiest thing on the board.
- [ ] **The RAY/coverage render is not in the game.** `Vision` is per-face binary. The
      angular-footprint coverage masks in `prototypes/lidar` give PARTIAL knowledge of a surface
      and a confidence gradient, and need a texture atlas to decouple material grain from mask
      grain. That is a real chunk of work and the prototype README says exactly what it costs.
- [x] **The projected view is PLAYABLE.** DONE (P32). `I` toggles it mid-mission;
      `SIGHTLINE_VIEW3D=1` starts in it. `Game.PickTile` is the ONE picking seam (three call sites
      used to inline `GetScreenToWorld2D` — a second projection would have had to be added in three
      places and rotted in two); in 3D it is a ray/ground-plane intersection, exact because the
      camera is orthographic. `SIGHTLINE_PICKTEST` proves the INPUT path by round trip — every tile
      projected to a pixel and picked back, 792 round-trips over four pitch/yaw pairs, plus a
      refusal check so an off-board pixel does not clamp to an edge tile and make the HUD margin
      act like a live board click. **The HUD needed no work at all**: it is screen-space and never
      knew the board had a projection. Move range, path preview and hover are drawn as REAL ground
      geometry, so they are depth-tested against terrain for free — a range tile behind a wall is
      occluded without an occlusion test.
- [x] **Discovery made opt-in, because a RENDER toggle must not change what the player knows.**
      DONE (P32). Only `View3D` consults `Vision`, so with it on, pressing `I` hid or revealed
      parts of the board. `SIGHTLINE_DISCOVERY=1` enables it; the default is off until it applies
      to BOTH renderers or becomes a real rule the AI honours.

#### OPEN — the projected view, after P33

- [x] **Pan, zoom, orbit and tilt.** DONE (P33). `View3D.Zoom` (1..3.5), `View3D.Pan` (ground-plane
      world units), `PitchMin/PitchMax` 18..82. Wheel zooms and middle-drag pans — the two gestures
      the flat camera already trained the player on — and the two a 3D camera adds get keys, because
      there is no mouse axis left: `[` / `]` orbit in 15-degree steps and `,` / `.` tilt. `C` resets
      both cameras. Pan bounds are DERIVED the way `Game.ClampPan` derives the 2D ones: at zoom z the
      view holds 1/z of the fitted extent, so the slack is `span * 0.5 * (1 - 1/z)` per axis, which
      collapses to zero at zoom 1 — correct rather than a special case, because the whole board is
      already framed there and there is nowhere to pan to.
      **`View3D.DragPan`'s rotation is the INVERSE of the yaw, not the yaw.** Turning a SCREEN delta
      back into a WORLD one means solving against the camera's ground-plane axes — screen-right is
      `(cos yaw, -sin yaw)`, screen-up is `-sin(pitch) * (sin yaw, cos yaw)` — and that is R
      transposed. Written as R it is exactly right at yaw 0 and at yaw 180 and wrong at every other
      angle, which is the whole reason PICKTEST leg (B) drags at four yaws: the first version of
      this wave shipped R, passed at yaw 0, and was caught only by the leg that rotates.
- [ ] **The camera is not framed on anything.** `C` reframes the whole board; there is no
      "centre on the selected soldier" the way `Game.FrameCameraOnSquad` (P29) opens the flat view.
      At zoom 1 it does not matter — the board is entirely on screen — so this only bites once
      BIGMAP is playable, and it is one line against `Pan` when it does.
- [x] **The whole `Fx` layer is in.** DONE (P34). Tracers, muzzle flare, impact rings, particles,
      floating damage numbers, dust, screen shake and the hit-stop zoom punch — pillar 2's entire
      output, which the projected view shipped without.
      **It needed no porting, because the map is AFFINE.** The projected camera is orthographic, so
      board pixel -> tile is a scale and an offset and tile -> screen is a fixed 2x2 plus an offset;
      compose them and the whole 2D layer is one matrix away from correct. `View3D.BoardPxMatrix` is
      pushed on rlgl's stack — the same mechanism `BeginMode2D` itself uses — and `Fx` and the anims
      draw completely unchanged. A tracer lands between the two tiles it was fired between; an
      impact ring lies on the ground and is squashed by the pitch exactly as the ground is.
      TEXT IS THE ONE THING THAT MUST NOT GO THROUGH IT (sheared by yaw, squashed by pitch, a damage
      number is an unreadable parallelogram), so `Fx.ProjectText` takes the projection as a function
      on the ANCHOR and draws the glyphs upright at an isotropic scale.
      Shake and the zoom punch have no `Camera2D` to live on, so they fold into the 3D camera
      itself: the punch scales the orthographic extent, the shake slides position and target along
      the camera's own screen axes. The bridge is built FROM the shaken camera, so the board and
      everything over it shake together instead of sliding apart.
      `SIGHTLINE_FXBRIDGETEST` pins four things, and leg (A) draws a pixel through the real rlgl
      stack and reads the framebuffer back rather than asserting arithmetic — `Rlgl.MultMatrixf`
      wants the TRANSPOSE of the System.Numerics layout, nothing in the type system says so, and
      passed the wrong way it silently draws the entire Fx layer in the board's top-left corner.
      `SIGHTLINE_FXSHOT=1` stages the layer six frames before the capture (staged at setup like
      every other `*SHOT` hook, it photographs nothing: at frame 760 every particle has been dead
      for eleven seconds). Pair it with and without `SIGHTLINE_VIEW3D=1` for the wave's before/after.
- [x] **The static overlays crossed the same bridge.** DONE (P35). `Renderer.DrawGroundOverlays`
      is the curated subset — 27 methods: overwatch and focus cones, threat, siege zones, banner
      auras, EVAC / TERMINAL / SABOTAGE / INTEL markers, barrels, enemy intent, scorch, fire, smoke,
      vent steam, the aim reticle, the barrel reticle, crossfire, grenade / item / shove previews,
      the bounty chevron, and the mark / pin indicators and previews. Called under P34's matrix, in
      `DrawBoard`'s own order with the excluded entries removed, so a dozen waves of layering
      survives. **The line between the two lists is not taste — it is "does the 3D view already own
      this?"**: the floor, vignette, elevation, grid lines, faux-3D cover, edge walls, units, their
      chrome, the move overlay, the path preview, the hover and the keyboard cursor are drawn as
      GEOMETRY and would double.
      **The text escape moved to `Cfg`, and P34's Fx-specific hook is deleted.** Any code drawing
      under the bridge may paint a glyph, so the escape belongs at the one funnel every string in
      this game already goes through (`Cfg.Text` / `TitleText` / `Measure` / `TitleMeasure` — the
      house rule, and this is what it is worth). `Cfg.TextUnmap` is the subtle half: call sites
      centre with `pos -= Measure(...)/2`, and because the projection is affine,
      `project(p - u) = project(p) - A*u` — so `Measure` returning `A⁻¹ * screenSize` makes the
      sheared subtraction land as EXACTLY the screen offset the call site meant. An identity, not
      an approximation; get it wrong and every centred label slides half its width diagonally at
      yaw 45 while looking perfect at yaw 0.
      `SIGHTLINE_FXBRIDGETEST` leg (E) pins all of it, **and its ink half measures SHAPE, not
      position** — the first version checked "ink here, not there" and passed with the escape
      deleted, because the pushed matrix takes an un-escaped glyph to roughly the same place. What
      the escape buys is that the glyph is UPRIGHT, so the assertion is on the ink bounding box: at
      yaw 62 / pitch 44 a four-glyph run is ~50x20 drawn upright and ~49x45 sheared. The width
      barely moves; the height is the discriminator.
- [x] **Barrels were DOUBLE-DRAWN, and that is what the "decal" note actually was.** DONE (P36).
      `View3D.DrawTerrain` has built a barrel as a SOLID since P30; P35 then put `DrawBarrels` in
      the curated subset, so the flat card landed on top of its own geometry and the P35 roadmap
      entry described that as a limitation rather than as the bug it was. Removed from the subset.
      **P35 mis-applied its own rule** — "does the 3D view already own this?" is the test, and the
      3D view owns barrels.
- [ ] **The barrel is still a plain gold-topped block.** It is geometry and it is single-drawn, but
      it is not a DRUM: no ribs, no rim, no lid. One more `props.py` entry whenever somebody wants
      it, on the same envelope as the block it replaces.
- [ ] **`Fx.DrawAmbient` is drawn OVER the board in 3D, under it in 2D.** There is nowhere else to
      put it: these are screen-space primitives with no depth and the ground plane is opaque, so
      "under the board" means "invisible". It reads as atmosphere between the operator and the
      hologram, which suits the premise — but it is a difference between the two views, not a
      choice anybody made.
- [x] **"No post-FX in 3D" WAS FALSE — checked, not assumed (P36).** The claim was inherited from
      P27's prototype and never re-checked after P32 made the view playable. `Game.DrawBoardLayer`
      is a CALLBACK INTO `Display.RenderFrame`, so the projected board renders into `_target` like
      any other frame and takes the bloom, the biome grade, the vignette, brightness and gamma with
      it. Measured: `SIGHTLINE_POSTFX=1 SIGHTLINE_VIEW3D=1 SIGHTLINE_SHOT=760` puts visible bloom on
      the barrels and the biome grade over the whole board. What IS post-FX-free is
      `SIGHTLINE_VIEW3DSHOT` — P27's prototype sweep, which bypasses `RenderFrame` deliberately so
      there are as few layers as possible between the geometry and the PNG. The ordinary screenshot
      harness keeps post-FX off in BOTH views (that is what makes shots comparable), which is why
      every shot in this program looked ungraded and nobody re-read the claim.
- [ ] **Discovery does not affect the RULES.** It decides what is drawn. True fog of war — the AI
      and targeting honouring it — is a separate decision with real balance consequences, and the
      peek-LoS trap in P28's notes applies to it too.

#### OPEN — what a bigger board still needs

- [x] **Procedural density scales with the board.** DONE (P37). **A BIGGER BOARD IS MORE ROOMS,
      NOT ONE STRETCHED ROOM.** Every literal coordinate in the four archetypes is in an 18x11
      reference frame (`Mission.RefW/RefH`); `Mission.CellGrid` divides the board into as many
      reference-sized cells as fit, and each cell gets its own plateaus and its own archetype roll,
      so a large board is a patchwork of different rooms rather than one motif repeated.
      **Stretching was the obvious alternative and it is the wrong one**: it keeps an archetype's
      shape and loses its SCALE, and scale is the whole content of a cover motif — a screen whose
      gaps are six tiles wide is not a screen, it is four separate walls. Tiling keeps every gap,
      lane and breach at the size a soldier's six-tile move was tuned against.
      The sprinkle scales by AREA rather than by cell count, because cells are only approximately
      the reference size and it is tiles-per-tile that a player reads as clutter.
      Measured at 40x28: cover density **3.6% -> 14.5%** against an 18x11 reference of 18.7%, which
      is the intended direction (a bigger board is meant to buy lateral choice; packed to 18x11
      density at 5.7x the area it would be a maze).
      **On the shipped 18x11 board this is a NO-OP BY CONSTRUCTION** — one cell, origin (0,0), area
      ratio exactly 1 — and `SIGHTLINE_DENSITYTEST` leg (A) asserts that over the whole tile+height
      board rather than over a cover count, which is the one thing a density wave could keep while
      moving everything else. `SIGHTLINE_DENSITY=0` is the restore flag.
      Leg (C) exists because `ArchTwinCorridors` ran its spines `for y < grid.H`: on a taller board
      that is a wall through every room below this one, and it is exactly the class of bug tiling
      invites. Leg (D) asserts >=95% of floor tiles reachable from a spawn, because
      `EnsureConnectivity` guarantees the named POINTS are mutually reachable and says nothing about
      the rest of the board.
- [ ] **Enemy count, mission pacing and sight range are all still 18x11 numbers.** `DESIGN.md` §3D
      already names "empty traversal = boredom"; at 40x28 with ~6-tile moves, crossing the map is
      nine turns of walking. The P27-N note's answer stands: insert NEAR the objective and let size
      buy lateral choice, not distance.
      **THE DESIGN IS WORKED OUT — read this before starting, it is the next wave (P38).** The
      coherent rule is one transform, not four fixes: **DEPTH stays at the reference, LATERAL goes
      to the whole board.** Everything the mission is about lives in an INSERTION FRAME that is
      `Mission.RefW` columns wide (centred on the board) and the board's FULL height. So:
      `x` is a reference column offset into the frame, and `y` scales from `RefH` to `Cfg.GridH`.
      **At 18x11 it is the identity** (`ox = 0`, `H == RefH`), which is the same
      no-op-by-construction property P37 has and the same thing its leg (A) should assert — over the
      whole board AND force signature, not over a distance.
      Four seams, all of them already parameterised and none of them far apart:
        1. `Mission.SpawnTableFor`'s two tables are authored in the reference frame -> map through.
        2. `Mission.PodAnchor` already takes `(gw, gh)` -> pass the FRAME's size and offset the
           result. Its `row` jitter only matters for the three non-ENVELOP shapes, and those keep
           the full board height, so no row remapping is needed at all (ENVELOP ignores `row`).
        3. `Game.SetupMission`'s evac block (`Grid.W - 2`, rows 0..3) and terminal
           (`Grid.W / 2 + 1`) -> frame-relative. Sabotage is already board-relative and TIGHT
           (`W/2 - 4 .. W/2 + 4`), so it may need nothing.
        4. `Mission.TryApplyLayout` validates SABOTAGE against the hard-coded triple
           `(5,3) (10,5) (13,7)`, which is `Game.SetupMission`'s expression EVALUATED AT 18x11.
           The two agree there and diverge at every other size, so the arena validator would check
           tiles the build will not use. Latent today (arenas are 18x11-only) — fix it to the
           expression while you are in there, and do not let it become a second source of truth.
      **The tension to decide first, because it decides the shape:** spreading objective sites to a
      big board and capping deployment depth are in direct conflict — a SABOTAGE charge at column 30
      cannot be inside an 18-column frame. The frame rule resolves it by putting the SITES in the
      frame too, which is the "lateral choice, not distance" answer taken seriously. If a future
      wave wants genuinely distant objectives it is choosing a different game, and should say so.
- [ ] **Nothing has measured a big board.** It is a LEVEL lever on every axis at once and severs
      the CRN chain completely. `SIGHTLINE_BIGMAP` unset is the arm.
- [ ] **Sector patrols.** 2-4 posts per group; group moves at its slowest member's speed; arrives
      within Chebyshev 1; turns around next turn; 1-2 tiles of formation slop. Keep groups apart by
      AUTHORING (build-time guard on overlapping routes), not runtime avoidance. Simulate all,
      animate only the visible. Additive to `Ai.Plan`: `if (tier == Unaware) PatrolStep(); else Plan()`.
- [ ] **A captain that calls backup.** `LinkRange = 6` and `HackNoise` are already half of it. The
      call needs a ONE-TURN WIND-UP with a visible marker, or killing the captain first is not a
      choice, it is a coin flip resolved after the fact.
- [ ] **Vision by elevation — NO FLOORS NEEDED.** "9 tiles at your level or above, +1 per level
      below" works on the existing `Grid.Height` scalar. Prove the feel before paying for the 3D
      data model (which touches all eight per-tile arrays, CostMap, HasLineOfSight, GetCover, Build,
      every AI query and all 35 arenas).
- [ ] **Fog of war.** The prerequisite is board size, not code: sight range 9 on an 18-wide board is
      half the board, so fog there is a no-op. At 30 wide the SAME number is 30%. `Game.ClosestSightedDist`
      is a per-team visibility query in all but name; `SquadConcealed` is already a one-way gate.

#### OPEN — wave P28 (the scanned-hologram vision model). Prototype is in `prototypes/lidar`.

The design is settled and demonstrated; what remains is engineering, in this order. The owner's
direction: the player is an operator at HQ reading a holographic reconstruction assembled from what
the squad's own LiDAR has scanned. Read `prototypes/lidar/README.md` before starting — it lists four
bugs already found and fixed, all of the same class, so they are not re-found.

- [x] **`EdgeKind` in `Grid`, behind `SIGHTLINE_EDGES=0`.** DONE (P28/1). `EdgeV[W+1,H]` /
      `EdgeH[W,H+1]`, each boundary stored once. **FIVE touchpoints, not the four predicted here**
      — the fifth is `GetCover`'s `anyAdjacent`, which scanned adjacent cover TILES only, so a
      soldier sheltered by a wall and shot from the open side read as out in the open and the
      crit-vs-exposed bonus and LOCK-ON flank perk would have silently stopped firing against
      exactly the geometry the layer creates. EDGETEST leg (D) failed on the first build, which is
      how it was found. `Ai.cs` gained zero lines. Not a save break. Inert until P28/2 placed one,
      proven by PAIRTEST.
- [x] **Buildings — the first producer of edges.** DONE (P28/2). `Mission.StampBuildings`,
      `SIGHTLINE_BUILDINGS=0/1`. Validates on `Terrain.StampRift`'s precedent: every candidate is
      re-flooded through `Grid.CostMap` and reverted whole unless every reachable tile is still
      reachable. Measured: the validator refuses ~40-60 would-be seals per 24 boards, so it is a
      guard and not decoration. **The first placement rule was doing all the rejecting and none of
      the protecting** — demanding a pristine floor footprint threw out 330 candidates against the
      validator's 1 and walled only 4 of 24 boards; a crate inside a building is furniture.
      Relaxed to "half the interior is floor": 24/24 boards walled, 0 stranded.
- [x] **Draw them.** DONE (P28/3). `Renderer.DrawEdges` paints a wall straddling the grid LINE
      rather than filling a cell, in the cover palette so it reads as the same class of quiet
      terrain, with a door drawn as two jamb stubs because the opening is the information. The
      move overlay needed no work — it comes from `CostMap`, so it stops at a wall already.

#### OPEN — wave P28, still to do

- [x] **DESTRUCTIBLE EDGES.** DONE (P40) — the fix P28 named, built as P28 specified it. Per-edge
      HP (`Grid.EdgeVHp`/`EdgeHHp`, charged by `ResetCoverHp` so walls and blocks charge by one
      call), `Grid.DamageEdge` degrading High -> Low -> None on the same two constants a block
      uses, `Grid.CoverEdge` mirroring `CoverTile`'s dominant-side pick, and `EnemyPlan.SapEdge`
      beside `SapTile`. `SapTile` stays a TILE either way — it is where the sapper must STAND — so
      the planner's movement scoring is untouched.
      **Red before, green after, on a gate that already existed** (AICOVTEST=6, deterministic):
      buildings OFF sap 19 (0.21%) PASS; buildings ON + destructible sap **12 (0.12%) PASS**;
      buildings ON + `SIGHTLINE_DESTRUCTEDGE=0` sap 9 (0.09%) FAIL — the third row reproducing
      P28's recorded failure exactly.
      **A BREACH IS THE ONE DAMAGE CALL ON THIS BOARD THAT CHANGES THE SHAPE OF THE MAP** rather
      than the cost of standing somewhere: `CostMap` and `HasLineOfSight` both read the edge layer,
      so the hole is a hole for both teams by construction. EDGETEST asserts that directly — a wall
      across the board divides it, two hits clear one segment, and the route opens.
      **Scoped to the SAPPER on purpose.** `DamageEdge` is public so grenades and stray fire can be
      wired later, but making every explosion a wall-breach is a far larger tactical change than
      the blocker needed, and it would want its own priced round.
- [x] **Buildings PRICED, and they stay default-off.** DONE (P42) — base `8c75492`, heat pinned,
      3 rungs x 16 CRN slot bases x 2 arms, 96 chunks / 1,920 campaigns / 960 pairs, zero BAD,
      `ARM CHECK` and `LEAK-CHECK` both PASS. Raw round: `docs/measurements/p42/`.
      **Win rate: NOT RESOLVED** — h0 −5.3, h4 −2.2, h8 −0.3, pooled **−2.60 (n_disc 257, MDE 4.68,
      z −1.56)**. But 26.8% of paired campaigns take a different course, so this is a BOUNDED effect
      rather than an absent one. The OFF arm reproduces the ladder of record (51.9 / 25.6 / 5.9
      against P24's 49.5 / 26.1 / 4.8), which is what licenses reading the ON arm against the band —
      and it puts **h0 at 46.6 against a 47 floor**.
      **DECISION RICHNESS IS THE FINDING, AND IT IS DOWN AT EVERY RUNG**: `meaningfulChoicesPerTurn`
      3.32 -> 2.68, 3.22 -> 2.93, 1.81 -> 1.36. That agrees with P40's AICOVTEST census on the same
      lever (hunker 16.05% -> 26.76%). **Walls give you somewhere to sit, and sitting is not a
      decision.** Skill expression is flat (greedy-minus-sloppy edge moves −6.2->−5.6, +0.0->+1.9,
      +5.6->+3.8 — no direction), which matters because P26 measured skill at +0.2 points overall
      and a lever that raised it would have been worth paying win rate for.
      **A lever that costs choices and buys nothing measurable does not earn a default.**
- [ ] **What P42 did NOT price: an AUTHORED building.** The round measured `StampBuildings`'
      procedural rectangles — walls dropped where they fit. A rectangle gives cover without giving a
      REASON TO GO IN, so it buys hunkering; that is the whole result. A building with an objective
      inside it, a roof worth holding, a door worth breaching is a different object and this round
      says nothing about it. **That is where the edge layer earns its keep**, and it wants the
      double-resolution authored template format below rather than another default flip.
- [x] **A damaged wall looks damaged.** DONE (P41), in BOTH renderers, and NOT the same way in
      each — which is the point. The flat board gets **fissures on the wall's lit cap**, the same
      near-black zigzag a chipped cover block has carried since 3.6, keyed on the same predicate
      (`0 < hp < max`) so a player learns one vocabulary for "this is about to go". The projected
      view gets a **shorter, sagging slab** instead: at that scale a crack on a 0.16-wide slab is a
      couple of pixels and says nothing, while a wall visibly dropping toward its next tier reads
      from any camera angle. Same FACT, carried the way each projection can carry it.
      `SIGHTLINE_WALLDMG=1` chips every OTHER High segment so a shot shows intact and damaged walls
      of the same kind side by side — the only comparison that says whether the cue reads — and
      EDGETEST pins the predicate both renderers key on, because that is the part a test can hold.
- [ ] **Wall readability is a first pass, not a finished look.** They read as a building outline
      and they are distinct from cover blocks, but they are thin beside the chunky faux-3D cover
      volumes. `Renderer.DrawEdges`' `HiW`/`HiLift` constants are the dials. Judge it against
      pillar 1 on a real screen before calling it done.
- [ ] **The 3D view does not know about edges.** `src/View3D.cs` still draws only tiles, so the
      projected camera shows a board with no buildings on it. `prototypes/lidar` has the geometry
      worked out (`World.WallT`, a wall as a thin slab straddling the boundary).
- [ ] **Nothing authored places an edge.** Buildings are procedural rectangles. The double-
      resolution template format (`2W+1 x 2H+1`, odd rows/cols are edges) is still the plan for
      authored arenas, and still should NOT be retrofitted into the 35 existing 18x11 templates.
- [ ] **Buildings are unpriced.** They change the board, so they sever the CRN chain: this is a
      LEVEL lever and `SIGHTLINE_BUILDINGS=0` is its arm (free — it spends zero `Util.Rng` draws).
      Nothing has measured what a building does to win rate, decision density, or the policy gap.
      **That last one is the interesting question**: P26 measured skill at +0.2 points, and a
      partly-enclosed board with doors is exactly the situation where playing well should start to
      beat playing badly.
- [ ] **The AI does not understand a door.** `Ai.cs` re-prices itself through `GetCover`/`CostMap`
      so it will not walk through walls, but nothing makes it PREFER a doorway, stack on one, or
      treat a room as a place to be cleared. It sees a wall as terrain, not as architecture.

- [ ] **Peek line-of-sight, behind `SIGHTLINE_PEEKLOS=0`.** Rays leave from a ring over the
      soldier's own footprint (radius 0.34 against a half-tile of 0.5, so a lean can never reach
      through a wall) rather than from a point at their centre. Measured **+43% of the world
      revealed at equal ray budget**. Owner's decision: **visible implies targetable.**
      **THE TRAP, and the rule that avoids it:** if peek decides visibility but cover is computed
      from tile centres, a 0.34-tile lean can flip a defender from full cover to flanked with
      nothing on screen explaining why — cover becomes a continuous function of sub-tile position,
      illegible to the player, unscoreable by the AI, and it invalidates every tuned constant in
      `Combat.ComputeOdds`. **So: peek decides VISIBILITY; the EDGE the winning sightline crosses
      decides COVER.** Both discrete. Leaning buys the shot, not the target's cover — peeking is an
      accuracy trade, not a cover bypass. A doorway correctly gives no cover because a door is its
      own `EdgeKind`, which falls out of the data rather than out of the lean.
      **Optimisation to measure, not assume:** naive multi-origin is 49 Bresenham walks where there
      is now 1, and `Ai.cs` calls it constantly. Try centre-to-centre FIRST and only pay for the
      other 48 on failure — open ground succeeds immediately, so only genuinely blocked queries
      cost more. Expected ~1.1-1.3x, but that is a guess until somebody measures it.
- [ ] **Coverage masks + a texture ATLAS.** A ray is a cell of solid angle; what it reveals is the
      AREA its footprint covers. **The atlas is not polish — it is what makes the separation real.**
      The prototype draws one quad per mask cell, which forces coverage and material to share a
      resolution and lets mask resolution set the texture's grain. In the atlas the face is ONE
      quad, material is sampled per PIXEL, coverage is an alpha channel sampled bilinearly (which
      also softens the stair-stepped reveal edge). Needs no GLSL — Raylib's default shader already
      multiplies texel x material x vertex.
- [ ] **Per-triangle coverage FRACTION for props.** Today a triangle is all-or-nothing, so a
      partially covered one pops in whole; on a 60-triangle tree that reads as holes in the canopy.
      Storing the fraction and fading alpha closes them with the same confidence mechanism already
      used for walls and floor.
- [ ] **Prop spatial bucketing.** Mesh intersection is already the dominant cost (see
      `docs/measurements/p28/`) and the only spatial structure is a per-prop AABB. At 50x50 with
      hundreds of props this needs per-tile bucketing or a BVH.
- [ ] **Ray density is a tunable, and the owner wants it higher.** Measured: ray marching is
      linear at ~3.2-4.0 M rays/s and is NOT the expensive part; coverage stamping is a roughly
      fixed ~70-100 ms/scan. 16x the rays costs 2.9x the time. Three unspent optimisations are
      listed in `docs/measurements/p28/README.md` — bank those before lowering the density target.
- [ ] **Restore flags are MANDATORY here.** Changing the visibility model changes what every unit
      can see, so it changes every AI decision and **severs the CRN chain exactly as W1 did**.
      Every archived balance number becomes incomparable — not wrong, incomparable. The roadmap
      already accepts that for this direction, but per the project's cardinal rule (the one THE
      FORK PAYS broke, which cost the L5 bridge) each gameplay lever ships with its flag **in the
      same commit**.

#### OPEN — found during the review, untouched

- [ ] **The squad chronicle.** `KillUnit` already computes the killer (`Game.cs:3367`) and discards
      it to a class string. `FallenRec` is five fields. Recording who/where/beside-whom is a few
      lines at a seam where all three are in scope.
- [ ] **`Ai.Tier` is nearly inert** — four constants, two of them bypassed whenever a SPOTTER is
      alive, and at Tier 2 one is an exact no-op against the SPOTTER branch. C1's dead-row class,
      one layer down.
- [ ] **The autopilot has no target-priority entry for twelve of ~21 archetypes**, including ones
      whose design brief is "kill it first" — they score as a GRUNT. **Every balance number in this
      project was produced by a bot that cannot tell them apart.**
- [ ] **Shoot-and-hold is legal.** `Unit.BeginTurn` gives 2 actions, a shot costs 1, and
      `DoOverwatch` gates only on `!CanAct || Ammo <= 0` — there is no `FiredThisTurn` check. The
      exclusive "shoot vs hold the reaction" bet does not exist. Consequence nobody recorded: a
      grenade costs TWO actions thrown first and ONE thrown second.
- [ ] **Mission 1 is always the same encounter class** — forced Eliminate, forced no-faction, and
      only SCOUT/GRUNT. It is also the mission that ends roughly a quarter of all runs.
- [ ] **Retire bot win rate as the objective function.** Not because it cannot see fun — because
      §P26-2 shows it cannot see PLAY. The policy GAP is the better target and is now readable.
