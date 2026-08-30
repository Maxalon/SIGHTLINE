# SIGHTLINE — Design Foundation

> **What this is.** `CLAUDE.md` is the *build & continuity* contract — what we're
> making and where we are. **This document is the *rationale* contract — *why* the
> game should feel the way it does.** It collects the game-design principles the
> project is built on, grades SIGHTLINE honestly against them, and records the
> deliberate decisions about the game's *feel* and *information design* (including
> the fog-of-war question) so the end product reads as **considered, not
> accumulated**.
>
> It is a living doc. When a change touches *feel*, *information*, or *decisions*,
> read the relevant principle below and check it against the **Do / Don't**
> checklist (§7) before building.

This was written from a focused research pass into game-design fundamentals
(frameworks, game feel, information design, decision quality, flow, UX, roguelike
meta-loops, onboarding). Sources are linked inline and collected in §8.

---

## 1. Pillars & the experience we're selling

The original three pillars (from `CLAUDE.md`) are correct but under-specified. Here
they are sharpened to **five**, each tied to the player emotion it serves. Naming the
target emotions matters: the [MDA framework](https://en.wikipedia.org/wiki/MDA_framework)
(Mechanics → Dynamics → Aesthetics) reminds us that players experience **aesthetics**
(felt experience), designers build **mechanics** (rules), and the **dynamics** in
between are emergent — so we should **design backwards from the feeling we want**, not
forwards from features we can code.

| # | Pillar | The promise to the player | MDA aesthetic(s) |
|---|--------|---------------------------|------------------|
| 1 | **Looks deliberate** | A coherent geometric world; nothing arbitrary on screen. | Sensation |
| 2 | **Feels good** | Every action lands with weight, motion, and sound. | Sensation |
| 3 | **Reads clearly** | I can always understand the board and *why* an outcome happened. | Challenge (fair) |
| 4 | **Decides meaningfully** | At three timescales, my choices are non-obvious and consequential. | Challenge, Expression |
| 5 | **Stakes that bite** | My squad is *mine*; losses and growth carry across the run. | Challenge, Discovery, Fantasy |

**What we are NOT chasing** (equally important): SIGHTLINE is not pursuing
*Narrative* (no scripted story), *Fellowship* (no multiplayer/social), or simulation
fidelity. Effort spent there is effort stolen from 3–5. Pillar **3 (Reads clearly)**
and pillar **5 (Stakes)** are promoted to first-class status because they are the two
places the game is currently thinnest (see §4) — and because pillar 3 is the lens
through which the fog-of-war decision must be judged (see §5).

### 1.1 AMENDMENT — the light frame (PROGRAM RESONANCE, wave C1, 2026-08-28)

> **This amends the paragraph directly above.** It is a recorded, deliberate change of
> scope, not drift. Read the limits; they are the load-bearing half.

**What changed.** "Not pursuing *Narrative*" is too blunt, and the game paid for it.
SIGHTLINE now pursues a **light frame**: a small, bounded body of generated text whose
only job is to make the systems the game *already has* legible and felt. Concretely
that is four things and no more — a three-line mission briefing, faction dossiers and
named regions, rare soldier barks at real beats, and a five-line run epilogue.

**Why — the measurement that forced it.** Over 16 measured campaigns: **146 soldiers
went down, 74 bled out, 22 were finished while down, and exactly 1 was revived.** That
is ~96 dying people who each had a callsign, a rank, earned traits, a nickname, scars, a
faction grudge and a bond with a specific squadmate — and the game's entire telling of
their deaths was a floating damage number and a name on an end-card list. Three
factions, eight biomes and a branching campaign map shipped with **zero words of world**.
Pillar 5 was *implemented and unnarrated*, which is exactly why the stakes have always
read thinner in play than in the changelog. The fix was never more machinery. It was
acknowledgement. Naming a thing is the cheapest way to make an existing system land.

**What this is NOT — the limits, which are not negotiable.**
- **It is not a sixth pillar.** The frame *serves* pillars 3 and 5. When it competes
  with either, it loses — automatically, not after a discussion.
- **No story.** No plot, no scripted beats, no character arcs, no branching dialogue,
  no cutscene, no unskippable anything. There is no protagonist and there is no villain
  beyond the three factions the combat code already models.
- **Nothing the player must read to play well.** Every word is redundant with a
  mechanical read that already exists on the board or in the FIELD MANUAL. A player who
  never reads a briefing must not be at a disadvantage.
- **Readability wins, always.** §3.E and §3.H govern. Text that fights the signal is a
  regression, and the briefing card yields the shared card slot to wave T1's teaching
  layers *absolutely* — a lesson or a field tip on screen silences the frame outright.
- **Barks are rate-limited by design, not by taste.** One per turn, one per beat kind
  per mission, never the same speaker twice running. The combat log is load-bearing for
  "why did that happen"; flavour may never crowd out a mechanical line.
- **Determinism is a hard constraint, not a preference.** Every generated word comes
  from `src/Voice.cs`, which takes **zero draws from the shared `Util.Rng`** — region
  names, briefings and the epilogue are pure `Util.Hash3` derivations of `MapSeed`, and
  bark variety uses a dedicated `Random`. The project's entire balance methodology rests
  on common-random-number pairing; flavour text must never be able to move a measurement.

**How the limits are kept honest.** `SIGHTLINE_VOICETEST=1` is the contract: it proves
the RNG separation (with a sensitivity probe so it cannot pass vacuously), asserts every
template slot resolves and that no beat can produce a nonsensical combination (a soldier
with no bond can never draw a bond line), walks every rate-limit gate, and measures every
generated string against the real pixel width of the chrome that draws it. A line that
would ship an ellipsis fails the build gate instead.

**The honest cost.** This is scope the project previously spent nowhere, and it competes
for the same attention budget as balance and feel work. It earns its place only while it
stays this small. If a future wave wants dialogue, arcs or a plot, that is a *different*
amendment and it should be argued on its own terms — this one does not authorise it.

---

## 2. The lenses (vocabulary we reason with)

**The three nested loops.** A well-designed game is a stack of loops, each satisfying
on its own timescale. SIGHTLINE's:

- **Second-to-second** — select, move, read odds, shoot. *Must feel good and read
  instantly* (pillars 2, 3).
- **Minute-to-minute** — cover, flank, overwatch, grenade, where-do-I-stand. *Must be
  a series of interesting decisions* (pillar 4).
- **Run-to-run** — wounds, perks, bonds, the campaign map, requisition. *Must make
  this squad feel like mine and make this run feel different from the last* (pillar 5).

A loop is healthy only if **the loop below it is already fun**. You cannot fix a
boring fight with a better meta-progression screen.

**The information spectrum.** Tactics games sit on an axis from **perfect
information** to **hidden information**:

```
  PERFECT INFO  <------------------------------------------>  HIDDEN INFO
  Into the Breach        XCOM (EU/2)            Classic X-COM / Invisible Inc
  (telegraphed puzzle)   (pods + fog)           (recon, scouting, the unknown)
```

[*Into the Breach* is near-perfect-information on purpose](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-subset-games-i-into-the-breach-i-):
enemies telegraph their moves so that "every death feels like the player's own
fault." **SIGHTLINE currently lives near that left end** — the whole board is visible,
%-to-hit is shown, threat tiles are previewed, cover is annotated. *That is a coherent,
defensible identity, and most of our juice and UX assumes it.* Any move rightward
(fog, limited camera) is an **identity decision**, not a tuning tweak — it subtracts
from systems we already built. This is the spine of §5.

---

## 3. Principles library (the coursework)

Format: **Principle** — *source* → **In SIGHTLINE** → **Do / Don't**.

### A. Meaningful decisions — the heart of tactics

**A decision is "interesting" only if no option dominates, the options are
asymmetric, and the player can make it informed.**
*[Sid Meier, "games are a series of interesting decisions"](https://www.gamedeveloper.com/design/gdc-2012-sid-meier-on-how-to-see-games-as-sets-of-interesting-decisions);
[interesting-choice criteria](https://critical-gaming.com/blog/2011/4/12/interesting-choices-interesting-gameplay-pt1.html).*
Interesting decisions need **interesting consequences**; ~**3–5** live options is the
comfortable band. Customization (naming, tags, builds) deepens attachment.
→ **In SIGHTLINE:** the move/shoot/overwatch/grenade choice each turn; perk picks;
the campaign-map fork.
- **Do** make options trade off (range vs. cover, act now vs. set overwatch, spend
  the grenade now vs. save it).
- **Don't** ship a **dominant strategy** (one line that's always right) or a **false
  choice** (options that look different but play the same), or an **obvious choice**
  (one option strictly better). Each of these is a *non-decision* wearing a decision's
  costume.

**Kill the dominant *defensive* strategy.** *[Turtling](https://en.wikipedia.org/wiki/Turtling_(gameplay))*
— hunkering and never advancing — is the classic tactics failure state; it's boring
and often safest. Good designs **incentivize aggression**: mission timers, objectives
that force movement, rewards for advancing.
→ **In SIGHTLINE:** overwatch + hunker is a tempting turtle. Objectives (Hack, Evac,
Sabotage, Defend's wave pressure) already push against it — *keep auditing whether
"camp the chokepoint on overwatch" quietly dominates* (see §4).

### B. Information design & randomness

**Telegraph intent; make failure legible.** *[Enemy telegraphing](https://www.gamedeveloper.com/design/enemy-attacks-and-telegraphing);
[silhouette/shape reading](https://motheread.org/how-blizzard-uses-enemy-silhouettes-to-help-players-react-instantly-in-combat/).*
The player must be able to read *what is about to happen* and *why something happened*.
→ **In SIGHTLINE:** the dimmed "?" pod, threat pips, %-to-hit + FLANKED tooltip are all
telegraphs — good. The weak spot is the **moment of first contact** (it's currently an
accident, not a telegraphed event — see §5).
- **Do** signal danger *before* it triggers (an "alert/suspicious" state, a clear
  reaction).
- **Don't** spring state changes with no warning ("gotcha" design).

**Output randomness is the most-hated kind — use it carefully.**
*[Input vs output randomness](https://www.gamedeveloper.com/design/randomness-and-game-design);
[Burgun on output randomness in strategy](http://keithburgun.net/how-strategy-games-can-use-output-randomness/).*
**Input randomness** happens *before* you decide (map layout, the hand you're dealt) —
players accept it as the puzzle. **Output randomness** happens *after* you commit (a
%-to-hit roll) — "it feels bad to make a good play and get screwed by the dice." Also:
**players misread probability** — [a missed 90% shot reads as betrayal; 90% "feels
like 99%"](https://www.gamedeveloper.com/design/randomness-and-game-design).
→ **In SIGHTLINE:** combat is output-random (hit/crit rolls). That's genre-authentic,
but it's the system most likely to produce rage and **save-scum pressure**.
- **Do** soften the tail: damage *ranges* not single rolls, partial outcomes (graze),
  guaranteed-damage floors, "streak-breaker" nudges, and lots of *small* rolls rather
  than one swingy one. Lean on **input** randomness (map/biome/objective/pod placement)
  for variety, since players forgive it.
- **Don't** let a single 85% roll silently decide a whole mission with no mitigation
  and no feedback explaining the odds.

### C. Game feel & juice

**Feel lives in the input→perception loop; juice is layered and compositional.**
*[Swink, *Game Feel*](https://shop.elsevier.com/books/game-feel/swink/978-0-12-374328-2);
[Vlambeer, "Juice it or lose it"](https://www.gamedeveloper.com/design/squeezing-more-juice-out-of-your-game-design-).*
Juice = extra channels (animation, audio, shake, particles) communicating **intent and
intensity**. SIGHTLINE already does this well (hit-stop, recoil, knockback, tracers,
floating numbers, screen shake, zoom-punch).
- **Do** keep juice *proportional* to event importance (a crit-kill should out-punch a
  graze) and **reinforce** information.
- **Don't** let juice fight readability — **juice must serve clarity, not bury it**
  (see E's signal-to-noise). More shake is not always more better.

### D. Flow & difficulty

**Keep the player in the flow channel** — challenge tracked just above skill.
*[Flow theory](https://yukaichou.com/gamification-analysis/flow-theory-complete-guide-csikszentmihalyi-optimal-experience/);
[Chen, *Flow in Games*](https://www.jenovachen.com/flowingames/Flow_in_games_final.pdf).*
Too hard → **anxiety**; too easy → **boredom**. Prefer a **stair-step** curve (each new
challenge starts a touch below the prior peak).
→ **In SIGHTLINE:** the run *is* the macro difficulty curve (escalating missions).
Two live risks: **anxiety** from the turn-1 forced ambush (§5), and **boredom** from
empty traversal turns if maps grow without density (§5).
- **Do** pace spikes; give the player a beat to find footing before the spike.
- **Don't** front-load anxiety (an unavoidable bad first contact) or pad with dead time.

### E. UX & readability

**Respect the 80/20 attention split and the signal-to-noise ratio.**
*[Game UX](https://www.protopie.io/blog/game-ux-design); [affordances in games](https://machinations.io/articles/affordances-in-game-systems-design).*
Players spend ~80% of visual attention on the play area, ~20% on the HUD. Therefore:
**maximize the play area, keep the HUD legible and out of the way, disclose
progressively, and make affordances obvious.** Feedback must be **clear, timely,
relevant**.
→ **In SIGHTLINE:** today the board is a 1008×616 island inside a 1280×800 window with
opaque bars cropping it — the world reads as a *diorama* and ~40% of the screen is
chrome/margin. A **full-bleed board with translucent, contextual UND floating UI** is
almost pure upside (this is the safe, do-it-now part of the original nudge).
- **Do** go full-bleed; float translucent panels with a scrim/shadow so they stay
  legible over busy terrain; keep iconography high-contrast.
- **Don't** crop the play space with opaque furniture; don't clutter the HUD with
  always-on detail that could be progressive.

**Design for accessibility from the start.** Colorblind-safe palettes, adjustable text
scale, and contrast are *readability* features, not extras (already specced as roadmap
3.13).

### F. Run-to-run / roguelike meta

**Prefer horizontal progression (variety) over vertical (raw power); build content
from combinations; make death reflection, not frustration.**
*[Roguelike meta-progression](https://www.strayspark.studio/blog/building-roguelike-ue5-procedural-progression);
[progression systems](https://gamerant.com/roguelite-games-with-best-progression-systems/).*
A player on run 100 should have **more options**, not be 10× stronger. Runs feel
different because of **combinations** (objectives × biomes × enemy mixes × builds), not
because of a longer stat bar.
→ **In SIGHTLINE:** perks/traits/bonds/items already push *horizontal*. The open
tension is **stakes vs. death-spiral**: losing soldiers should *hurt* (pillar 5), but
the auto-backfill-to-4 currently softens it (good anti-spiral, weak stakes). Wounds
(3.1) added bite; the **bench / deploy-short-handed** half is still open.
- **Do** make loss *felt* (named KIA, lost veterancy) while giving a comeback path.
- **Don't** let a snowball (or a death-spiral) make the outcome a foregone conclusion
  with many turns still to play.

### G. Teaching & onboarding

**Performance before competence; teach with low-cost failure and well-ordered
problems.** *[Tutorials](https://en.wikipedia.org/wiki/Tutorial_(video_games));
[What Video Games Have to Teach Us (Gee)](https://en.wikipedia.org/wiki/What_Video_Games_Have_to_Teach_Us_About_Learning_and_Literacy).*
Let players *act* before they fully understand; sequence mechanics so each is learned in
a safe, contained problem; avoid walls of text.
→ **In SIGHTLINE:** there is no onboarding yet (roadmap 3.12). A scripted, low-stakes
first mission that introduces move → cover → flank → overwatch → fire **in that order**,
with contextual callouts, is the right shape — not a manual.

### H. Visual design & readability (and the asset policy)

**Readability is the master visual principle for tactics — pass the "squint test."**
*[Squint test](https://medium.com/@sifatrabbani_UX/the-squint-test-0677a08de848);
[NN/g](https://www.nngroup.com/videos/squint-test/).* Squint until detail blurs to
silhouettes and color-blocks; the selected soldier, the nearest threat, and the cursor
tile must **still** pop. This is *Into the Breach*'s ethos — [art that "communicates
rather than compels,"](https://pressstartgaming.com/into-the-breach-a-tactical-masterpiece/)
function before flourish.
- **Do** carry critical info on **value (light/dark) contrast**, not hue alone; make the
  focal element the brightest / largest / most-saturated.
- **Don't** add texture or effects that compete with gameplay signal (visual noise).

**Limited palette, semantic color, 60-30-10.** *[Limited palettes](https://www.wayline.io/blog/limited-color-palettes-game-art);
[60-30-10](https://itch.io/blog/478705/a-short-recommendation-color-palettes-and-the-60-30-10-rule).*
Color communicates faster than shape — so give each accent **one job**. Roughly 60%
dominant/neutral, 30% secondary, 10% loud accent.
- **Do** reserve the loudest accent (red) for **danger/enemy only**; keep palettes
  consistent across biomes for cohesion; pair color codes with shape/icon redundancy.
- **Don't** let hue alone carry meaning, or let a biome retint break the semantic roles.

**Post-processing is the cheapest big lift — and it's code, not assets.**
*[Bloom](https://pingpoli.medium.com/the-bloom-post-processing-effect-9352fa800caf).* A
subtle, event-reactive bloom + vignette + per-biome grading + impact chromatic aberration
on the existing `Display` render-target reads as "premium."
- **Do** tie effect intensity to events (spike on hit/kill/crit); keep bloom subtle.
- **Don't** over-bloom (the genre's most-abused effect) or wash out readability.

**Generated > flat, and it stays lean.** Procedural in-engine noise/gradient textures and
baked fonts add richness with [far less storage than bitmaps](https://docs.unity3d.com/550/Documentation/Manual/ProceduralMaterials.html)
and no human-authored art.

#### H.1 AMENDMENT — the asset policy is now a STYLE, not a RULE (owner, 2026-08-29)

> This amends the paragraph above and CLAUDE.md's "Art policy". Recorded, with limits, rather
> than left to drift — the same treatment §1.1 gave the narrative amendment.

**What changed.** "No hand-made / human-authored art or audio" was a hard constraint. The owner
has removed it. Third-party assets are permitted when they clear **both** bars, and only then:
1. **Zero cost** — free to obtain AND free to redistribute inside a shipped build, forever. No
   asset that would cost money if the game were distributed in any capacity.
2. **Zero legal risk** — an explicit licence permitting redistribution (CC0 / public domain /
   OFL / MIT-class). "Free to download" is not a licence. If the licence is unclear, the answer
   is no.
Every added file is recorded in `assets/*/CREDITS.txt` **and** `THIRD-PARTY-NOTICES.txt`, with
its source URL and licence. Small files only; large binaries still stay out of the repo.

**What did NOT change — and this is the load-bearing half.** The geometric aesthetic is kept
**because it is good**, not because a rule forced it. Every principle above still governs: an
imported asset that fails the squint test, breaks the semantic colour roles, or adds texture
competing with gameplay signal is a **regression**, and the fact that it is "real art" does not
earn it a pass. The bar for an imported asset is *higher* than for a procedural one, because
procedural content is authored against the palette by construction and an import is not. In
practice this means the policy mostly unlocks **fonts, shader/LUT data, and audio** — not
sprites or illustrations, which would fight the established visual language.

**The sandbox constraint, measured.** From inside an agent session the outbound proxy blocks
`freesound.org` and `opengameart.org` (403); `raw.githubusercontent.com`, `api.github.com` and
`nuget.org` resolve. So the *audio* half of this amendment is mostly unusable by an agent and is
work for the owner's own machine, where `assets/sfx/<cue-id>.ogg` and
`assets/music/{ambient,combat}.ogg` are picked up by the existing file-first loader with **no
code change**. Do not spend a wave rediscovering this.

**Style guide — semantic color roles (lock in roadmap 5.1):**

| Role | Job | Rule |
|------|-----|------|
| Friendly | the player's squad | cool/neutral family; **brightest when active/selected** |
| Enemy / danger | hostiles, threat pips, lethal warnings | the **loud accent (red)** — reserved, never decorative |
| Cover / terrain | low/high cover, plateaus | muted, low-saturation; never out-shouts units |
| Objective | terminal / evac / sites / VIP | a single distinct **goal hue (gold)**, used only for goals |
| Neutral / inactive | dormant pods, spent units | desaturated/dimmed toward the background |

**Acceptance check for any visual change — the squint test:** squint; can you still
instantly find (a) the selected unit, (b) the nearest threat, (c) the objective? If not,
the change failed.

**Shape / icon redundancy — meaning never rides on hue alone (5.5).** Every coded
distinction carries a *second*, non-colour channel (shape, glyph, or position) so the
game reads in the colorblind palette and at a glance. The in-engine icon vocabulary is
all primitive-drawn (lines/circles/polys, no asset files) and **inherits the role colour
of whatever it labels**, so a single glyph works in every palette + enabled/disabled state:
- **Action bar** — each button carries its glyph (chevron=fire, oval+fuse=grenade, eye=overwatch,
  shield=hunker, …); the glyph takes the button's text colour (`Hud.DrawActionIcon`).
- **Objective readout** — a glyph left of the top-bar objective text: crosshair=ELIMINATE,
  brackets+node=HACK, up-arrow=EVAC, diamond=ESCORT, spark=SABOTAGE, cage=RESCUE,
  shield=DEFEND (`Hud.DrawObjectiveIcon`).
- **Status effects** — a shape beside each on-unit code: flame=burning, droplet=bleed,
  star-burst=stun, swirl=disoriented, so the effect reads without parsing the BRN/BLD/STN/DAZ
  text or its hue (`Renderer.DrawStatusGlyph`).
- **Cover** — type already reads by shape too: △ on high cover, — on low (5.4 / S4-B).

Rule for any new coded state: ship its glyph in the same pass, and verify it in **both**
palettes (`SIGHTLINE_CB=1`).

**Asset policy (clarified — supersedes the old "no assets" wording):**
- **No hand-made / human-authored assets** — the human won't make art/audio by hand.
- **Generated assets are allowed:** **procedural / in-engine / shader first; AI only where
  it clearly wins.** Audio stays synthesised.
- **Commit small, optimised generated files** (a font is the headline win — it kills the
  ASCII-`?` limit); **keep large binaries out** so the repo stays lean.
- Most upgrades need **no committed binaries** (Raylib `GenImage*` textures + `LoadFontEx`
  font baking are in-engine). Build checklist: `CLAUDE.md` → ROADMAP **PHASE 5**.

---

## 4. SIGHTLINE — honest scorecard

Graded against the pillars/principles above. "Strong" = a genuine strength to protect;
"Watch" = a hypothesis worth auditing in code/playtest; "Gap" = a known hole; "Thin"
(FUL-1) = a pillar that exists in code but carries less *play-weight* than the design
assumes — the system is built, measured, and largely fails to reach play.

| Area | Grade | Notes |
|------|-------|-------|
| Feels good (juice) | **Strong** | Hit-stop, recoil, tracers, shake, zoom-punch, floating text, procedural SFX. Protect it. |
| Reads clearly (full-info board) | **Strong** | %-to-hit, threat pips, cover shields, FLANKED tooltip — and since SIGNAL: status pills, role rings, visible focus cone, enemy-ID tooltips. This is the identity — don't erode it lightly. |
| Content breadth | **Strong** | 8 objectives, biomes, a 21-archetype enemy roster, 35 authored arenas, perks/specs/traits/boons/contracts, branching map, 4 modes. Lots of *combinations* — whether they all **reach play** is the engagement-mass question below. |
| **First contact / encounter geometry** | **Strong (was the headline issue)** | 4.2 cut sight range + built the mid-field screen; alert tiers (4.3) + concealment (4.4) completed the fix; SIGNAL made the boards biome-true. First contact is a deliberate, rewarded choice now. See §5. |
| Decision quality per turn | **Measured (W2+)** | No longer a hypothesis: the flywheel instruments meaningful-choices/turn and lead-swings/match, and UNDERTOW/APEX moved both. The live question is the **~0 policy gap** (sloppy play is fully viable) — accept-vs-sharpen is parked at FUL-13. |
| Output-randomness feel | **Addressed** | Graze band, streak-breaker (S4-C), always-on combat log ("did the dice cheat me?"), banded odds colors. The rage surface is mitigated, not gone — %-to-hit stays genre-true. |
| **Death stakes** | **Addressed (FUL-7), measured** | The moment of death is now a 3-turn BLEED-OUT with real counterplay — STABILIZE freezes the clock, the corpsman's PATCH revives, DRAG/EXTRACT carry the body, a won field recovers the survivor gravely wounded (Wound 3 + the near-death scar track), and only telegraphed AoE/fire can finish a downed soldier. Measured (paired h0; review-fixed build, same-slot re-measure): soldier true-KIA **-40%** (125→75 on slots 0-9), 186 downs staged at a **33% save-rate** (the honest ledger — a body finished by AoE/fire while down is a death, not a save), STABILIZE a live first-class verb (~60 uses/chunk). What remains thin is the REVIVE half: the corpsman reaches only ~38% of missions (backfill-only roster), so revives are rare (7 measured) — the FUL-13 founding-squad question, recorded not quota-chased. |
| **Engagement mass** | **Thin** | The comeback economy (BRACE, morale/rout, the verb boons) is tuned for battles that mostly don't happen: 2-enemy pods executed serially in 3-4-turn missions. The balance bot has used BRACE **zero** times in ~500 measured missions, and the FUL-1 PROCS column now shows which held boons never fire. One real multi-pod battle per mission is the fix (FUL-6). |
| Screen usage / UI framing | **Addressed (4.1)** | Full-bleed board with translucent floating panels; W11 de-occluded the HUD and FUL-3 fixed the chip reflow. |
| Onboarding | **Addressed (RESONANCE T1)** — was an over-claim | The W11 grade ("Addressed") was wrong and is recorded here as the mis-grade it was: what shipped was a 5-card callout strip on mission 1 that taught **3 of ~14 verbs** while the bar showed twelve (FUL-12 dimmed the other eleven — dimming is not staging), plus a six-bullet rules wall on the intro, i.e. the exact artefact §3.G says not to ship. T1 replaced it with the shape §3.G actually asks for: a scripted, non-persistent, restartable **TRAINING OP** (8 well-ordered problems on an authored arena — move, cover, flank, fire, overwatch, grenade, ability, clear), **staged verbs** (drill + mission 1 only, with a permanent SHOW ALL escape), and **10 just-in-time field tips** (one per untaught verb, once per profile, fired the first time its precondition is true in play). The intro is one line. The FIELD MANUAL stays the reference it always was. Hook: `SIGHTLINE_TUTTEST`. |
| Accessibility | **Improving** | Colorblind palette + brightness/gamma shipped (Display settings). No text-scale pass yet (3.13). |

**Reading of the board:** SIGHTLINE is **wide, juicy, and — since the flywheel — 
measurable**. The encounter opening that was the weakest link is fixed and protected.
The two thin pillars both got their stage in FULCRUM: **engagement mass** (FUL-6's
pods-of-3 + linked activation put the comeback economy into real multi-pod battles)
and **death stakes** (FUL-7's bleed-out window turned the instant cut into measured
triage drama — a 33% save-rate where death was total). The open tail is presence, not
stage: the corpsman reaches too few missions for REVIVE to carry its weight, and the
policy-gap accept-vs-sharpen question — both parked at FUL-13.

---

## 5. The information-design decision (fog of war: considered, deferred)

This section exists so the decision is **on the record and reversible with eyes open.**

**The problem (validated by the numbers).** First contact in SIGHTLINE is an
*accident*, not a *choice*. With an 18-tile-wide board, ~8-tile moves, and a 12-tile
activation sight range, there is essentially **no opening move that doesn't trip a pod**.
Three knobs (map size, movement, sight range) all push toward instant engagement, and
the woken pod gets a free scatter — a milder cousin of [XCOM's much-criticized
"free move on reveal"](https://steamcommunity.com/app/268500/discussions/0/366298942104526236/).
The *felt* result: the player can't "find their footing before the encounter starts."

**The options considered:**

1. **Lean perfect-information (the *Into the Breach* direction).** Remove surprise
   entirely; telegraph everything; make first contact a non-event. Coherent, but it
   pushes SIGHTLINE toward pure puzzle and discards the squad-tactics fantasy.
2. **Add fog of war + a restrictive, soldier-focused camera (the original nudge).**
   Hide the map; reveal by vision; make scouting real. *This is the most evocative
   option and the most expensive — and it fights our own foundation.* Fog [slows
   pacing and creates information-horizon problems](https://www.gamedev.net/forums/topic/547996-not-having-fog-of-war-in-turn-based-strategytactics/),
   and crucially it **subtracts from systems we already built**: threat-preview can't
   preview unseen enemies, and full-board readability (pillar 3) is the thing we'd be
   trading away. A restrictive camera also fights the genre (tactics is about reading
   the *whole* board) and demands heavy auto-framing work to not feel like chores.
3. **Concealment + alert tiers + spacing/terrain + full-bleed UI (the targeted fix).**
   Keep the perfect-information identity. Solve *first contact* directly:
   [XCOM 2-style **Concealment**](https://www.ufopaedia.org/index.php/Concealment_(XCOM2))
   (start hidden; reposition and scout freely; **you choose when to break and engage**;
   ambush pays off) is *purpose-built* for "find footing, choose your moment." Add
   **alert/awareness tiers** (green → yellow → red) so being spotted is gradual and
   telegraphed, never a gotcha. **Tune the three knobs** (push spawns back, add
   sightline-blocking terrain, lower/soften sight range, reconsider the free scatter).
   Ship the **full-bleed translucent UI** for world presence.

**The decision: pursue Option 3.** Rationale:

- It gives the *exact* experience the human asked for — *careful, deliberate,
  find-your-footing-before-the-fight* play — **without hiding the board**, so it
  preserves pillar 3 and every system that depends on it (threat preview, %-to-hit,
  cover annotation, juice).
- Concealment converts first contact from an **accident** into a **rewarded choice** —
  fixing the root cause, not masking it.
- It is **far lower risk and cost** than a fog + camera + map rebuild, and it doesn't
  require building auto-framing camera tech to avoid being annoying.

**Fog of war is *deferred, not rejected.*** It remains the right tool *if and only if*
we deliberately decide SIGHTLINE should become a **recon-survival** game (the right end
of the spectrum). Revisit it only **after** Option 3 ships and playtests, and only as a
**flagged prototype on the larger maps**, judged on one question: *is partial-information
SIGHTLINE more fun than full-information SIGHTLINE, knowing it costs us threat-preview
and readability?* Until that bar is cleared, we do not pay its price.

---

## 5.1 The enemy ammo economy (decided 2026-08-29, wave W2 "THE OPPONENT ACTS")

This section exists because the project had never *made* this decision — it **defaulted
into** one, and the default was the bad half of a real design fork.

**What was actually shipping.** Hostiles are handed exactly one clip at spawn
(`Mission.cs:277/1236/1249/1323/1832`) and there was **no enemy reload verb anywhere in the
codebase.** So "dry" was not a tempo state, it was **death by other means**: a hostile that
emptied its magazine stopped being a combatant for the rest of the mission and simply stood
on the board. It was invisible on top of that — enemy ammo appeared nowhere in `Hud.cs` or
`Renderer.cs`, so the player could neither read it nor plan around it.

Measured on the current tree (`SIGHTLINE_AIIDLETEST`, base `4784803`), counting only **contested**
act-opportunities — at least one soldier still standing, because on an all-downed board `Ai.Plan`
returns an empty plan by design and every hostile idles regardless of ammo:

| frame | acts | on an empty weapon | of those, produced nothing |
|---|---|---|---|
| n=16 campaigns | 755 | 69 (**9.1%**) | 42 (**60.9%**) |
| n=32 campaigns | 1195 | 39 (**3.3%**) | 24 (**61.5%**) |

The *rate* is sample-dependent (3-9% of contested acts); the *consequence* is not — **~61% of
dry-weapon acts produced nothing in both frames.** An earlier draft of this section cited "11.5%
… essentially all of them": the first figure was unsplit (it counted the bleed-out window) and
the second rounded 61% up to "essentially all". Both are corrected here, and the correction is
the reason CLAUDE.md's rule reads *do not cite a number you have not just re-measured*.

**The fork.** Two coherent options, and only two:

| | **(a) A reload verb** | **(b) Per-turn clip refresh** |
|---|---|---|
| Rule | a dry hostile spends 1 action changing the mag | ammo silently refills each enemy turn |
| Cost to the player | a free turn of tempo they can *bait and punish* | none — the state is deleted |
| What it adds | suppressing fire and long fights acquire a point | nothing; it removes a system |
| What it costs us | one more branch, and it must be **legible** or it is invisible pressure | the enemy's magazine stops meaning anything |

**The decision: (a), the reload verb** — mirroring the player's own `Game.DoReload` exactly
(one action, full clip, does not end the turn). Rationale, in the order that decided it:

1. **Symmetry is the game's contract.** The player reloads; the reaction system, the shove,
   the brace and the overwatch cone are all already team-symmetric (see `CLAUDE.md` →
   "How a turn flows"). An opponent that never has to manage a magazine is playing a
   different game from the one the player is playing, and §3.A calls that a false choice
   worn by the *player's* ammo management.
2. **It creates a decision instead of deleting one.** §3.A: an interesting decision needs
   consequences. Option (b) makes "how much fire has this hostile put out?" unanswerable and
   therefore unusable. Option (a) makes it a live read — *this one is empty, push now* — which
   is exactly the kind of minute-to-minute lever §2 asks for and costs no new verb, no new
   screen and no new number.
3. **It is the only option that fixes the actual defect.** The defect was not "enemies run
   out of ammo"; it was "a hostile that runs out stops existing while still standing on the
   board." (b) hides that by making the state unreachable. (a) turns it into a beat.

**The constraint that comes with it, and it is not optional.** A reload the player cannot
see is invisible pressure — a hidden clock that quietly makes the game harder. So the
decision *includes* the read: the hostile token carries a small ammo pip row
(`Renderer.DrawEnemyAmmoPips`), and a dry hostile carries a **DRY** chip — an empty-magazine
glyph plus the word — in `DrawUnitStatusChips`. Per §3.H the actionable state is carried by
SHAPE and TEXT, not by hue, and both are drawn only for hostiles already in contact
(`AlertLevel.Alert`) so a dormant "?" pod still gives nothing away.

Two things this rule was found violating in review, and both are now enforced rather than
asserted. **(1)** The read must actually be *visible*: the first version drew its own pill and pip
row into p.Y+24..+42, the band `DrawUnitStatusChips` owns and paints LAST, so on any hostile
carrying BRN/BLD/DAZ the pill lost 9 of its 15 px and the pip row vanished entirely. The DRY read
therefore lives *inside* that late pass now, and `SIGHTLINE_AIIDLESHOT` stages a DRY+BRN token so
the claim is checkable from one frame instead of taken on trust. **(2)** "One decision" has to
mean one switch: the read is gated on `Game.AiIdleFix`, the same dial as the reload verb. Before
that, turning the reload off left hostiles wearing a permanent DRY badge advertising a state the
player could do nothing about. **If a future wave removes the read, it must remove the reload with
it** — the two are one decision, and the code now makes that structural.

**What this is NOT.** It is not an ammo *economy* in the resource-management sense: there
are no magazines to count, no ammo pickups, and no attrition model. A hostile can reload
indefinitely. The only thing being bought here is a **one-action tempo window** the player
can create and then spend, and that is the whole of the intended scope.

**The honest cost.** This makes the game harder in principle — a hostile that used to be
permanently neutralised on 3-9% of its contested act-opportunities is now merely delayed by one
action. In practice the cost was measured and is not distinguishable from zero: over 800
CRN-paired campaigns, run completion 25.2% → 23.8% pooled (McNemar p=0.451), mission win-rate
78.94% → 78.56% over ~1400 missions, soldier deaths per mission 1.340 → 1.340. See
`docs/measurements/w2/` and `docs/DEVLOG.md` §W2; `SIGHTLINE_AIIDLEFIX=0` restores the pre-W2
behaviour — and, since the review, the pre-W2 *read* — exactly.

---

## 6. Encounter-design intent (feeds Phase 4)

Design intent only — the build checklist lives in `CLAUDE.md` → **ROADMAP — PHASE 4**.

- **Full-bleed, translucent, non-cropping UI.** Reclaim the ~40% of screen lost to
  margins/bars; float contextual panels with scrims. *(Safe, do-first.)*
- **Bigger *and denser* maps + spawn standoff.** The goal is *standoff distance and
  meaningful traversal*, **not raw size** — empty maps cause boredom turns. More
  sightline-blocking terrain so a 12-tile sight line no longer sees the whole board.
- **Alert/awareness tiers** (green/yellow/red) replacing binary dormant→instant-scatter.
- **Concealment + ambush** as the marquee mechanic: the careful-opening fantasy.
- **Tune the three knobs** (sight range, movement vs. map, the free scatter).
- *(Deferred behind a flag, only if warranted later:)* fog of war + soldier-focused
  auto-camera.

---

## 7. Do's & Don'ts — quick reference

**Loops**
- ✅ Make the fight fun before polishing the meta screen above it.
- ✅ Give every turn at least one non-obvious decision.
- ❌ Don't let any timescale become rote ("move then shoot" every turn) or a turtle.

**Decisions**
- ✅ Trade-offs with consequences; ~3–5 live options.
- ❌ No dominant strategy, false choice, obvious choice, or overwhelming choice.

**Information**
- ✅ Telegraph danger *before* it triggers; explain *why* an outcome happened.
- ✅ Prefer input randomness (map/biome/pods) for variety.
- ❌ Don't spring unwarned state changes; don't let one output-random roll silently
  decide a mission without mitigation.

**Feel**
- ✅ Juice proportional to event weight; reinforce information.
- ❌ Don't let spectacle bury the signal.

**Visual**
- ✅ Pass the squint test; one job per accent color; value-contrast carries readability.
- ❌ Don't over-bloom, rely on hue alone, or add texture that fights the signal.

**UX**
- ✅ Full-bleed play area; legible, translucent, progressively-disclosed HUD;
  high-contrast affordances; accessible by default.
- ❌ Don't crop the board with opaque chrome or clutter the 20% HUD budget.

**Meta / stakes**
- ✅ Horizontal variety (combinations); loss is felt but survivable.
- ❌ Don't make outcomes foregone (snowball or death-spiral) with turns still to play.

**Identity**
- ✅ SIGHTLINE is **perfect-information squad tactics with bite.** Protect readability.
- ❌ Don't drift toward hidden-information by accident — that's a deliberate pivot (§5).

---

## 8. Sources & further reading

**Frameworks & decisions**
- [MDA framework](https://en.wikipedia.org/wiki/MDA_framework)
- [Sid Meier — games as interesting decisions (GDC 2012)](https://www.gamedeveloper.com/design/gdc-2012-sid-meier-on-how-to-see-games-as-sets-of-interesting-decisions) ·
  [Interesting choices analysis](https://critical-gaming.com/blog/2011/4/12/interesting-choices-interesting-gameplay-pt1.html)

**Information & randomness**
- [Randomness and game design (input vs output)](https://www.gamedeveloper.com/design/randomness-and-game-design) ·
  [Burgun — output randomness in strategy](http://keithburgun.net/how-strategy-games-can-use-output-randomness/) ·
  [Kotaku — randomness is not all the same](https://kotaku.com/randomness-in-video-games-is-not-all-the-same-1841049263)
- [Into the Breach — design (Game Developer)](https://www.gamedeveloper.com/game-platforms/road-to-the-igf-subset-games-i-into-the-breach-i-) ·
  [ITB and dynamic puzzles](https://blogofarcanesecrets.wordpress.com/2018/03/09/into-the-breach-and-dynamic-puzzles/)
- [Enemy attacks & telegraphing](https://www.gamedeveloper.com/design/enemy-attacks-and-telegraphing) ·
  [WoW enemy silhouettes](https://motheread.org/how-blizzard-uses-enemy-silhouettes-to-help-players-react-instantly-in-combat/)

**Encounter design (the SIGHTLINE problem)**
- [XCOM 2 Concealment — UFOpaedia](https://www.ufopaedia.org/index.php/Concealment_(XCOM2)) ·
  [How concealment works — PCGamesN](https://www.pcgamesn.com/xcom-2/how-concealment-works-in-xcom-2)
- [Pod-activation criticism — Steam](https://steamcommunity.com/app/268500/discussions/0/366298942104526236/)
- [Fog of war tradeoffs — GameDev.net](https://www.gamedev.net/forums/topic/547996-not-having-fog-of-war-in-turn-based-strategytactics/) ·
  [Turtling](https://en.wikipedia.org/wiki/Turtling_(gameplay)) ·
  [Turn-based tactics](https://en.wikipedia.org/wiki/Turn-based_tactics)

**Feel, flow, UX**
- [Swink — *Game Feel*](https://shop.elsevier.com/books/game-feel/swink/978-0-12-374328-2) ·
  [Vlambeer — "Juice it or lose it"](https://www.gamedeveloper.com/design/squeezing-more-juice-out-of-your-game-design-)
- [Flow theory (Csikszentmihalyi)](https://yukaichou.com/gamification-analysis/flow-theory-complete-guide-csikszentmihalyi-optimal-experience/) ·
  [Chen — *Flow in Games* (thesis)](https://www.jenovachen.com/flowingames/Flow_in_games_final.pdf)
- [Game UX design](https://www.protopie.io/blog/game-ux-design) ·
  [Affordances in game systems](https://machinations.io/articles/affordances-in-game-systems-design)

**Visual design**
- [Limited color palettes in game art](https://www.wayline.io/blog/limited-color-palettes-game-art) ·
  [60-30-10 rule](https://itch.io/blog/478705/a-short-recommendation-color-palettes-and-the-60-30-10-rule)
- [The squint test](https://medium.com/@sifatrabbani_UX/the-squint-test-0677a08de848) ·
  [Squint test (NN/g)](https://www.nngroup.com/videos/squint-test/)
- [Into the Breach — functional art](https://pressstartgaming.com/into-the-breach-a-tactical-masterpiece/) ·
  [Bloom post-processing (use sparingly)](https://pingpoli.medium.com/the-bloom-post-processing-effect-9352fa800caf)

**Meta-loop & teaching**
- [Building a roguelike — progression & run structure](https://www.strayspark.studio/blog/building-roguelike-ue5-procedural-progression) ·
  [Roguelite progression systems](https://gamerant.com/roguelite-games-with-best-progression-systems/)
- [Tutorials (video games)](https://en.wikipedia.org/wiki/Tutorial_(video_games)) ·
  [What Video Games Have to Teach Us — Gee](https://en.wikipedia.org/wiki/What_Video_Games_Have_to_Teach_Us_About_Learning_and_Literacy)

---

*Companion to `CLAUDE.md`. When in doubt about a feel/information change, this doc is
the tie-breaker; when in doubt about build state, `CLAUDE.md` is.*
