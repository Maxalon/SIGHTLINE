# FUL-6 CRITICAL MASS + FUL-7 LAST LIGHT — re-derived dev-executable specs (2026-08-28, vs branch tip e3bcb3b)

Derived against the CURRENT tree (FUL-1/2/3/4/5/9/11/12 landed; FUL-8/10 specced but not built).
Every seam below was verified to exist at the cited file:line. Inputs: DESIGN.md §4 re-grade
(engagement mass + death stakes are the thin pillars), the FUL-5 DEVLOG verdicts (FDR self-consuming;
GRENADE anti-correlated with shot declines, pods-of-3 is its stage; PATCH corpsman-capped, FUL-7 is
its stage), and the post-FUL-9 re-centered h0 baseline (only paired same-slot readings are
level-comparable — the FUL-5 R7b gotcha). Sequencing: FUL-6 lands first (ROADMAP order); FUL-7
measures on the post-FUL-6 tree with a FRESH reference batch.

---

## FUL-6 CRITICAL MASS (P6, L)

**Goal.** Pods of 3 + linked activation in mid/late missions — one real multi-pod battle per mission
instead of six 2-enemy executions, so the comeback economy (BRACE/morale/verb boons/grenades) gets a
stage; morale/rout reaches LAST STAND's horde. Owns the FIELD DRILLS rework decision (below: rework,
not retire). The research finding this answers: the first kill in a 2-pod routs the survivor
instantly (`RoutThreshold(2)=1`, Game.cs:770), so OPEN combat never stages a waver→rout arc, grenades
never see covered 2+-clusters (FUL-5 verdict 4), and a mission is six serial executions.

**Keystone findings (verified in code):**
- Pod size is set in exactly ONE line for the initial force — `e.PodId = i / 2` (Mission.cs:542) —
  plus the column-stagger read `EnemyPodColOffset[podId % ...]` with `podId = i / 2` (Mission.cs:462-463).
  Everything downstream is already pod-size-agnostic: `_podOrig` snapshots whatever spawns
  (Game.cs:1440-1442), `RoutThreshold(orig) = Max(1, orig/2)` (Game.cs:770), `PodAtWaverPoint`
  (Game.cs:784-789), the POD x/y tooltip (Hud.cs:1584), `CheckPodActivation`/`ActivatePod`/
  `SetPodSuspicious`/`ResolveSuspicion` (Game.cs:3104-3200) all read live membership. **Pods of 3
  finally give morale its full arc**: kill 1 of 3 → WAVERING telegraphs (alive=2, 2-1 <= 1); kill 2 →
  the survivor routs. In a 2-pod today the waver tag is up before a shot is fired and the first kill
  ends the show.
- The endless/wave morale plumbing already exists: FUL-4's `podded:` arm of `SpawnReinforcements`
  (Game.cs:4770-4805 — id `_nextWavePod` 100+, `_podOrig` sealed to what landed at :4798). LAST STAND
  bypasses it only because `SpawnEndlessBodies` hard-codes `e.PodId = -1` (Game.Endless.cs:159) and
  `SpawnEndlessElite` likewise (:237).
- The alert-tier machinery is the whole linked-activation kit: `SetPodSuspicious` (Game.cs:3121-3132,
  the "!" telegraph), `ResolveSuspicion` at EndPlayerTurn (Game.cs:3138-3150, called at :4880)
  confirms-or-forgets, and `ActivatePod` (Game.cs:3174-3200) is the single wake funnel every cause
  routes through (sight :3113, concealment-break :3237, HackNoise :3270, shove/grapple/pin/shot/
  suppress sites :3871/:3922/:3949/:4132/:4630/:4704). Linked activation is a ~30-line rider on
  these three functions.

**Pod-size progression (Mission.cs — one pure helper, no RNG):**
- New `static int[] PodPlan(int count)` → greedy split: while remaining >= 5 take 3; then remainder
  4 → {2,2}, 3 → {3}, 2 → {2}. So 7 → {3,2,2}, 8 → {3,3,2}, 9 → {3,3,3}, 12 → {3,3,3,3}. No pod of 1
  ever (the waver telegraph needs a survivor), no RNG draw (CRN draw-count identical).
- Applied in SpawnEnemies for **missions 3+ only** (`n >= 3`); **m1-2 keep `i/2`** (the teaching
  tier's first contact stays gentle — 4.2/4.3's whole point), and the **finale (`n >= Run.MaxMissions`)
  keeps `i/2` EXACTLY — FUL-11's kit geometry is verified against it (SIGNIFER at i==1 → the boss's
  pod 0, Mission.cs:969; FUL11PROBE stays green by construction). The m3/m5 mid-boss (i==0) joins a
  pod of 3 — its 2-body screen can now rout out from under it; accepted (mid-bosses "already win at
  high rates", Mission.cs:512), named a watch item.
- The same plan array feeds BOTH the PodId assignment (:542) and the column offset (:462) so a pod
  shares a column band. **Cohesion ride-along:** members of a pod currently land on independent
  shuffled rows (rows[i % rows.Count], :461) — a "pod" can be scattered across the board height. Fix
  with zero extra draws: the pod's FIRST member keeps its shuffled row as the anchor; members 2-3 take
  anchor+1/anchor+2 (clamped), falling into the existing collision-relocate loop (:466-467) exactly as
  today. Pods land as visible clumps — the linked-activation geometry, the grenade stage, and the POD
  x/y read all depend on this. (World layouts change vs base — it's a gameplay wave — but the
  unconditional draw count per body is unchanged, so paired A/B legs stay structurally comparable.)
- **Heat: no pod-size lever in v1.** Heat already owns bodies/stats/AI-tier; a pod rung now would
  confound FUL-13's re-baseline. Parked lever for FUL-13: "pods of 3 from m2" at rung 4+ (the W6
  `Ai.Tier >= 1` gate pattern, Mission.cs:437-447).
- Skirmish/Daily share Mission.Build and inherit the mission-keyed rule unchanged.

**Linked activation (Game.cs — the "they heard the guns" rule):**
- New field `readonly HashSet<int> _linkedPods` (cleared in SetupMission next to `_podOrig.Clear()`,
  Game.cs:1440). New const `LinkRange = 6` (tiles, `Util.TileDist` — a sound radius, the HackNoiseRange
  metric, Game.cs:3081).
- **Trigger:** at the END of `ActivatePod(podId)` (after the `any` gate, Game.cs:3194): if
  `_run.Mission >= 3` and the woken pod is real (`podId >= 0`), find the nearest OTHER pod with any
  `Alert == Unaware` member whose closest member is within LinkRange of the woken pod's closest
  member. **Exactly one pod per activation call** (the nearest). Fire: `SetPodSuspicious(B)` +
  `_linkedPods.Add(B)` + PopText `"HEARD THE GUNS"` (Pal.Suspect) over B's nearest member + BannerSub
  `"a nearby pod is moving to the sound"` under the CONTACT! banner — the read never lies: a linked
  pod IS coming.
- **Resolve:** `ResolveSuspicion` (Game.cs:3138-3150) gains one arm: a Suspicious enemy whose pod is
  in `_linkedPods` confirms to Alert **even when unseen** (the sound was enough); the set entry clears
  after the pass. NO scatter either way — the telegraphed path, exactly like today's sighted confirm.
  The player's warning is the remainder of the current turn plus the turn-end beat, identical to the
  existing 4.3 Suspicious semantics.
- **No chain by construction:** a linked pod wakes via ResolveSuspicion, which never calls
  ActivatePod — so a link can never propagate. Directly shooting/blundering into pod B still links
  pod C (it routes through ActivatePod), which is correct: new gunfire, new sound.
- Endless/Defend waves spawn already-Alert (no dormant pods) — the link is inert there by
  construction. Counterplay set (this list IS the codex row): kill the woken pod inside the warning
  turn and set a line (BRACE/overwatch/frag) for the second; pre-frag the telegraphed pod (a real
  player line — grenade range is short but the pod is CLUMPED now); or open the fight from a lane
  where no second pod sits within 6 tiles (the pre-fight read: pod clumps are visible on the board).

**LAST STAND morale (Game.Endless.cs):**
- `SpawnEndlessBodies` (Game.Endless.cs:141-169): split each wave's LANDED bodies into sub-pods via
  the same `PodPlan`, ids `_nextWavePod++` per sub-pod, `_podOrig[id] = landed` (the FUL-4 seal
  pattern, Game.cs:4796-4798). `_nextWavePod` starts at 100 in SetupMission (Game.cs:1441) and only
  climbs across the stand — no collision. `SpawnEndlessElite` keeps `PodId = -1` (the "ending" must
  not be routable — HORDETEST's `elitePodJoined` pin at Game.Endless.cs:465 stays green as-is).
  Pressure-clock waves stay `podded: false` (a routable punishment isn't one, Game.cs:4769).
- Run.cs:913-926: **remove `Boon.Terror` from the endless exclusion list** in `GenerateBoonOffer` —
  the exclusion's own comment ("wave hostiles spawn PodId<0") becomes false this wave, and leaving a
  now-live boon excluded would violate W1's no-inert-picks invariant in the other direction. (This
  changes the endless boon-shuffle pool composition — an accepted endless-only RNG-stream change,
  noted for the DEVLOG; campaign CRN pairing is untouched.)

**FIELD DRILLS rework (the FUL-5 verdict consumed — REWORK, not retire):**
- The verdict: the proc (a SECOND drag/vault by one soldier in one turn) is self-consuming — a legal
  drag lands its target at Cheby-1, which is un-draggable (`DragTargetOk`'s toward-tile rule,
  Game.cs:3981-3982), so the geometry the second use needs is destroyed by the first. Measured 0
  procs across every batch; vaults 0.
- **New effect (the DEVLOG brief's second option, chosen because it fires on play that actually
  occurs — drags measured 5-7/batch and FUL-7 adds a recurring drag stage):** *"FIELD DRILLS: a DRAG
  or VAULT drills the soldier forward — +1 tile of movement for the rest of that turn (limit still
  2/turn)."* Implementation: transient `public bool DrilledThisTurn` on Unit (reset in BeginTurn
  beside DragsThisTurn, Unit.cs:711-712; never persisted), read in `MoveBudget` (Unit.cs:590) as
  `+ (DrilledThisTurn ? 2 : 0)` half-steps appended after the `*2`; granted in `IssueDrag`
  (Game.cs:3998-4014) and `IssueVault` (:4060+) when `HasBoon(FieldDrills) && !u.DrilledThisTurn`,
  with PopText `"DRILLED +1 MOVE"` and a move-overlay refresh so the surplus is visible immediately.
- **PROC honesty:** delete BOTH old `>=2` proc lines (Game.cs:4007 and :4067); `RecordProc("FDR")`
  fires at the grant site — the effect (a real +1 tile this turn) deterministically exists once
  granted, the TRR precedent (counted at rout-start, Game.cs:2313). `FieldCraftLimit` stays 2
  (Combat.cs:573-574) — COMBATTEST's fieldDrills pins (Combat.cs:1562-1575) are untouched. Boon enum
  ordinal untouched; only `BoonDef.Desc` (Run.cs:154) + the codex FIELD CRAFT copy change.
- **RECLAIMER is explicitly NOT reworked here:** pods-of-3 + podded waves put more movers through
  focused cones — re-measure RCL on this wave's batches and record the number; if still 0, FUL-13
  inherits an honest retire-or-rework verdict with data. Do not touch its code in FUL-6.

**Force-budget / coverage answer (the question the spec must close):** same body count, fewer pods —
m5's 9 bodies go from 5 contacts to 3. Each contact is harder (3 guns wake at once; a link can make
it 6) and the map has fewer, larger set-pieces — that is the point (one real battle > six
executions), and the FUL-9 exposure work already guarantees objective variety per route. The
difficulty budget is paid by the warning turn (linked pods never scatter and never alpha-strike) and
watched by the measured wave below; the escalation levers if the dip breaks budget, in order:
(1) trim `count` by 1 on 3-pod missions (the FUL-4 defend-trim precedent, Mission.cs:417),
(2) LinkRange 6 → 4, (3) a one-link-per-mission latch.

**Autopilot implications (verified — no gate assumes 2-enemy pods):** the only pod-literal reads in
Game.Autopilot.cs are id-based or count-agnostic: AutoStallCheck wakes `dormant[0].PodId` (:1622),
the cold-LZ gate counts dormant bodies, TileExposure (:1329) sums active guns, HoldOverwatch's rusher
arm (:1559+) keys on charger archetypes, and SmartGrenade (:1292) already wants clusters >= 2 — 3-pod
clumps make its `preShot` gate FIRE more, no code change. A linked pod arrives as an ordinary Alert
next turn and the bot fights it — accepted v1 (no anticipation read). The greedy leg may dip on
simultaneous 3-gun wakes; that is what the dip budget prices.

**Teaching:** codex battlefield-states rows (the non-enum region, Codex.cs:304-320): update the pod
morale row to name the waver→rout arc at 3, and add `"LINKED ALERTS — gunfire carries: waking a pod
alerts the nearest dormant pod within earshot; it arrives one turn later, without the ambush scatter.
You always get the warning."` + CODEXTEST `required` entries (Codex.cs:396-401).

**Harness — `SIGHTLINE_PODTEST=1` → `Game.PodSelfTest()`** (Game.Harness.cs; register in Program.cs
on the MORALETEST pattern, Program.cs:198-205):
- (a) PLAN PIN: PodPlan {7,8,9,12} → {3,2,2}/{3,3,2}/{3,3,3}/{3,3,3,3}; a built m3 force groups to
  those sizes with `_podOrig` matching; an m1 force stays pods of 2; a finale force stays i/2
  (FUL11PROBE unchanged).
- (b) COHESION: every pod's max intra-pod Chebyshev spread <= 3 as spawned (post-relocate).
- (c) LINK: controlled scene (the MORALETEST staging pattern, Game.Harness.cs:1562+) — pod A + pod B
  dormant at TileDist 5, pod C at 12; `ActivatePod(A)` → B all Suspicious + in `_linkedPods`, C
  Unaware; `ResolveSuspicion()` with NO soldier in sight → B Alert (confirmed unseen), C still
  Unaware (no chain); a second scene with B at dist 8 → no link.
- (d) ARC: `DebugPodOrig(pod,3)` (Game.cs:797) + 3 live members → kill 1: `PodWavering` true on
  survivors; kill 2: survivor `Routed > 0` (the MORALETEST kill pattern, Game.Harness.cs:1656-1658).
- (e) ENDLESS: `SpawnEndlessBodies` lands ids >= 100 with sealed `_podOrig`; the elite stays -1
  (extend HORDETEST, Game.Endless.cs:328+).
- (f) FDR: a boon-held drag sets DrilledThisTurn + exactly one FDR proc + MoveBudget +2 half-steps;
  no boon → no grant; second drag same turn → no second proc.
- Shot hook `SIGHTLINE_PODSHOT=1`: a staged 3-pod clump + a linked "!"-telegraphed pod, BOTH
  palettes (SIGHTLINE_CB=1, the DESIGN §3.H coded-state rule).

**Measured wave (the FUL-5 rounds protocol, named precisely):** all rounds are paired greedy/sloppy
`SIGHTLINE_BALANCE=20` at h0 on CRN slots 0-9 (`SIGHTLINE_BALANCE_HEAT=0`, slot base default;
Program.cs:615-619, `Util.Reseed(50000+slot)` :687) — **R0 = a fresh reference batch on the
pre-FUL-6 tree, same slots, run first** (never reuse published numbers; the post-FUL-9/FUL-5 numbers
re-centered and only same-slot pairs are level-comparable). One lever per round: R1 = PodPlan +
cohesion; R2 = + linked activation; R3 = + endless wave pods (adds a 32-stand endless leg, depth
median vs the APEX 5-6 band); R4 = + FDR rework (procs only — zero balance surface). Close with one
`SIGHTLINE_BALANCE_HEAT=4` leg on the full stack. **Budgets/predictions vs R0 same-slot:** completion
dip <= 8 pts; GRENADE 6-8 → **>= 10** (the FUL-5 verdict says pods-of-3 IS its stage — if it does not
move, record the verdict, don't quota-chase); BRACE stays >= 30/batch (holds unaffected); TRR procs
> 0 on batches that hold it; OW/FOCUS mix shifts recorded not judged; PAIRTEST green (hash-not-draw
everywhere — the link fires off positions, zero RNG). Breach → the three levers above, one per
follow-up round, each measured.

**Save-compat: NONE touched — verified.** Enemies are never serialized (UnitDto whitelist,
SaveGame.cs:339-358); PodId/`_podOrig`/`_linkedPods`/DrilledThisTurn are all transient; no enum
member added anywhere (the Boon rework changes Desc strings only). SAVETEST unchanged.

**Verify.** Release 0/0; PODTEST + MORALETEST + HORDETEST + COMBATTEST + AITEST + SAVETEST +
PAIRTEST + CODEXTEST PASS; EXPOSURETEST PASS (route invariants don't read pods); FUL11PROBE PASS
(finale untouched); autoplay x5 no-exception/no-TIMEOUT; the R0-R4 rounds table + the FDR/RCL
verdict lines into docs/DEVLOG.md. Screenshots: PODSHOT both palettes + one live m3 board.

**Effort:** L. **Files:** src/Mission.cs, src/Game.cs, src/Game.Endless.cs, src/Run.cs, src/Unit.cs,
src/Codex.cs, src/Game.Harness.cs, src/Program.cs (+ docs/ROADMAP.md, docs/DEVLOG.md,
docs/FEATURES.md; src/Hud.cs and src/Renderer.cs need no code — the POD x/y tooltip and pill reads
are live-membership driven, verify only).

---

## FUL-7 LAST LIGHT (P7, L)

**Goal.** Lethal damage on a soldier becomes a 2-3 turn BLEED-OUT with stabilize/carry/revive
counterplay instead of an instant, decision-free cut — the genre's best drama, currently absent
(DESIGN §4 "death stakes: Thin"). Also the stage FUL-5's verdict 5 reserved for PATCH (corpsman-capped
at 4-6/batch; "FUL-7 re-opens this; the gates are ready").

**Keystone findings (verified in code):**
- **Every lethal path funnels through ONE seam — `Game.KillUnit` (Game.cs:2152)**: ShotAnim.Apply
  (Anim.cs:306), GrenadeAnim (Anim.cs:541), EnvDamage — bleed/burn/stun DoT, shove SLAM/STAGGER,
  siege STRIKE (Game.cs:2465), and the barrel blast (Game.cs:2627). It is already idempotent
  (:2160). The whole state machine enters as a guard at its top; no call site changes.
- **A Downed soldier that keeps `Alive == true` inherits the carry kit for FREE:** `DragTargetOk`
  requires only `ally.Alive` — not CanAct (Game.cs:3976), so DRAG pulls a downed body today;
  `ExtractCandidate` requires only `c.Alive` (Game.cs:4438), so a zone soldier hauls a downed body
  aboard on Evac/Escort/Rescue (`HasExtractAction`, Game.cs:669-670); and FUL-6's reworked FIELD
  DRILLS (+1 move after a drag) makes a drag-chain carry a real build. A true pickup (two units, one
  tile) is explicitly deferred — it breaks the occupancy invariant everywhere
  (`IsOccupiedByOther`/`UnitAt`, Game.cs:1974-1985) for marginal drama the drag chain already delivers.
- **The corpsman's revive needs近 ZERO targeting code:** `MostWoundedAdjacentAlly` picks the lowest
  HP-fraction ally and only skips `p.Hp >= p.MaxHp` (Game.cs:4517-4523) — a downed soldier at Hp 0 is
  automatically the most-wounded eligible target. The CombatMedic reach-2 fork and the FieldSurgeon
  triage fork (Game.cs:4511-4514, :4716-4717) ride along unchanged. Only the PATCH executor branch
  (Game.cs:4704-4724) learns "target is Downed → revive".
- **No persistence is needed — verified:** `SaveGame.Save` is called in exactly one gameplay site,
  the mission-START checkpoint in SetupMission (Game.cs:1478; the others are self-tests —
  Events.cs:510/528, Game.Modes.cs:502, Program.cs:422). A mid-mission state can never reach disk,
  and EnterBarracks resolves every Downed BEFORE the next checkpoint. `ToUnitDto` is a WHITELIST
  (SaveGame.cs:339-358) — new transient Unit fields are save-inert by construction. No enum is
  touched: Downed is deliberately NOT a `StatusKind` member (bespoke lifecycle vs the Turns-decayed
  TickStatuses pills, Game.cs:2428-2448; and StatusKind is persisted-by-ordinal vocabulary in the
  codex loops, Codex.cs:301/384).

**The state machine (all transient Unit fields, never persisted):**
`public bool Downed; public bool Stabilized; public int DownedTurns; public bool WasDownedThisMission;
public string DownedByCls;`
- **DOWN (entry):** top of KillUnit, before anything else: `if (CanGoDown(d)) { EnterDowned(d);
  return; }` where CanGoDown = `d.Team == Team.Player && !d.IsVip && !d.Downed &&
  !d.WasDownedThisMission`. The VIP/captive keeps instant death (Escort's VIP-loss and the
  DEATHTEST-pinned solo-win semantics untouched, Game.cs:2928). Enemies never go down (morale/rout is
  their drama). **Once per soldier per mission** (WasDownedThisMission) — the second lethal event
  kills outright; this is the anti-revive-tanking rule, pinned in DOWNTEST.
- **EnterDowned:** Hp = 0, Alive stays true; DownedTurns = 3; DownedByCls = the same attribution
  KillUnit computes (`ActiveAnim switch` killer Cls ?? LastDotSource, Game.cs:2179); Statuses.Clear()
  (no double timers; ground fire still kills via the AoE rule below); drop
  OnOverwatch/OwFocused/OwBrace/Hunkered/stances; purge queued moves + surplus reactions aimed at the
  unit (factor KillUnit's purge block, Game.cs:2266-2267, into a shared helper); squadmates get
  `AllyDown = true` (the Vengeful stage fires at the fall, Game.cs:2222 — an "avenged" revenge shot
  over a still-breathing squadmate is the drama working). NOT fired at down: `_run.Fallen`/Memorial
  (:2213-2217), `_missionKia`/KIA stamp/DeathFlash (:2244-2250), SecondaryFailed for NoLosses
  (:2219), Stats.RecordKill, DeathsByClass — death bookkeeping is death's. FX: "DOWN" stamp
  (Pal.Foe), a softer shake, banner `"SOLDIER DOWN — 3 TURNS TO REACH THEM"` (the honest telegraph),
  Audio "death". Rename the generic death pop at Game.cs:2234 to "KIA" for soldiers so the DOWN
  vocabulary is unambiguous (the FUL-8-style honesty ride-along).
- **While DOWN:** in StartPlayerTurn's player loop (Game.cs:4932, the CaptiveLocked precedent on the
  same line): after `p.BeginTurn()`, `if (p.Downed) { p.ActionsLeft = 0; if (!p.Stabilized &&
  --p.DownedTurns <= 0) ExpireDowned(p); }` — the countdown ticks on the squad's clock, where the
  player decides. Never selectable (CanAct false, Unit.cs:591), never reacts, never leashed. Enemies
  IGNORE downed soldiers (below); AoE stays blind: ANY further damage to a Downed unit —
  grenade/siege/barrel/fire routes through EnvDamage or the blast paths into KillUnit, where
  CanGoDown is now false — is an immediate true death.
- **STABILIZE (universal verb):** new action-bar button id `"stabilize"` (Hud row builder Add(...)
  pattern, Hud.cs:950-961; Game dispatch switch `case "stabilize"`, Game.cs:3702; tooltip row,
  Hud.cs:1465). Gate: an adjacent (Cheby 1) Downed, un-Stabilized ally + 1 action. Effect: 1 action,
  does NOT end the turn (the DRAG/EXTRACT support convention), `Stabilized = true` — the timer
  freezes; the soldier stays down (out of the fight, drag-able) for the rest of the mission.
  `Stats.RecordAction("STABILIZE")` (the W2 verb telemetry chokepoint).
- **REVIVE (the corpsman stage):** the PATCH executor (Game.cs:4704-4724) gains: if the auto-picked
  target is Downed → clear Downed/Stabilized, Hp = the existing baseHeal (PatchHeal 4 / CombatMedic
  3), target.ActionsLeft = 0 (they get up on their NEXT turn), FieldSurgeon's triage (wound + status
  clear) rides along, pop "REVIVED", Cd 3 unchanged. `Stats.RecordAction("PATCH")` already fires
  there — the FUL-5 PATCH counter measures the new stage for free.
- **EXPIRE:** `ExpireDowned` sets `LastDotSource = DownedByCls` and calls KillUnit — the FULL
  existing death flow runs: Fallen + Memorial append (Game.cs:2213-2217), `_missionKia` + KIA stamp,
  DeathsByClass keyed on the DOWNING archetype (so the honest loss card's CAUSE OF DEATH resolves
  through the bestiary — a DoT-caused down carries BURN/BLEED and buckets "?" exactly as DoT deaths
  do today, Game.cs:2198-2206), and — critically for FUL-10 — a bleed-out KIA reaches `Run.Fallen`
  identically to an instant KIA, so LIVING LEGENDS' planned `RemoveVeterans(Fallen ∩ reserve)` needs
  no special case, by construction.
- **Mission end:** in EnterBarracks, BEFORE the squad rebuild at Game.cs:1693: every Downed survivor
  (stabilized OR still ticking — the field is won) is RECOVERED: Downed cleared, Hp = 1, Wound = 3
  (max — DebriefSurvivors' attrition machinery owns it from here, Run.cs:1117-1141), WasNearDeath is
  already true (Game.cs:2421 fired long before Hp hit 0) so the NearDeathCount scar/trait track runs
  (Run.cs:1154-1164), Report line `"NAME recovered from the field — gravely wounded"`. Rationale: the
  triage decision (spend actions stabilizing vs racing the win inside 3 turns) stays fully live
  DURING the mission — the timer can beat you; a WON field never abandons a breathing soldier
  (feel-bad guard, and the Wound-3 + scar cost keeps it a real price). On a LOSS the run ends as
  today (downed-not-dead soldiers are not Fallen; the loss card's KIA roll reads Memorial unchanged).
- **Wipe semantics:** CheckEnd's wipe test counts AlivePlayers (Game.cs:2905) — downed soldiers keep
  the mission alive, correctly: an all-downed squad resolves in <= 3 bounded turns (timers expire →
  KillUnit → the real wipe → TryReinforcements/LoseRun, Game.cs:2910-2911); Escort's VIP-solo win and
  Rescue's freed-captive walk-out still function (downed soldiers are not "every soldier fell" until
  they are). Evac's all-in-zone win (Game.cs:2951) is BLOCKED by a downed body outside the zone —
  carry them in (EXTRACT/drag) or lose them to the timer: the mission-shape drama comes free from
  the existing win test.
- **Endless:** identical in-wave rules; the wave-clear sustain (CheckEndless, Game.Endless.cs:180-191)
  additionally revives downed survivors at the mend value (`Hp = max(1, EndlessWaveHeal)`), Downed
  cleared — the breather gets them up. Bounded as ever; no TIMEOUT surface (timers tick, stalls
  already force-resolve via AutoStallCheck).

**The AI rule (pick one and justify it):** **enemies do not target downed soldiers with direct
fire.** Single seam: filter `players` at the top of `Ai.Plan` (Ai.cs:56, `g.AlivePlayers()` →
exclude Downed) — the shoot-target loop (:327+), the finish band (:327), HOUND prey (:87-98),
advance/nearest, and BestSiege's cluster count (:944) all inherit it; overwatch only triggers on
movers (a downed unit never moves), so no reaction seam exists. Justification vs the comeback
economy: SIGHTLINE's comeback design prices TEMPO (BRACE/rout), and a down already costs the squad a
body plus the rescue actions; an executing AI converts the drama into a guaranteed double-loss and
makes STABILIZE a trap verb (walking into a watched kill-zone). The honesty valve that keeps stakes:
**AoE is blind** — a BOMBARD shell, frag, barrel, or fire field that catches the body kills it, and
all of those are telegraphed verbs the player can answer (vacate/carry/kill the artillery). The
enemy CAN finish your downed — only with the weapons you can see coming. Genre-consistent (XCOM's
rule, for the same reason). Degenerate-case audit: with only downed soldiers left, Ai.Plan's
players list is empty → empty plans → hostiles idle for <= 3 bounded turns while timers run; accepted
and pinned in DOWNTEST.

**UI reads (existing vocabulary only):** overhead status pill `DOWN 3/2/1` in red, flipping to
`STABLE` in amber when stabilized (the status-pill row, Renderer.cs:1755-1765 vocabulary); the unit
draws prone — the dormant-body scale precedent (FUL-3's 0.75x) at ~0.6x with a flattened silhouette +
a pulsing red ground ring (the boss-ring/role-ring vocabulary) so the body reads at a squint; the
HP bar hides while Downed (a 0-HP bar under a countdown pill would lie twice); roster chip gains the
red DOWN state (the FUL-3 full-size chip reflow); STABILIZE button + tooltip in the action bar;
the down-moment banner names the timer. Both palettes (SIGHTLINE_CB=1 rule for every new coded state).

**Teaching:** codex battlefield-states row (the non-enum region, Codex.cs:304-320): `"DOWN (BLEEDING
OUT) — lethal damage drops a soldier for 3 turns instead of killing them. Stabilize (any adjacent
soldier, 1 action) freezes the timer; a CORPSMAN's PATCH gets them back up; drag or haul them to
extraction. Blasts and fire finish the job — and nobody survives going down twice."` + CODEXTEST
required entry; the honest loss card already reads DeathsByClass → a bleed-out KIA names the
archetype that downed them; optional ride-along: first down force-shows a one-shot field tip (the
FUL-12 BraceTip pattern, Display-persisted seen flag, NoPersist-gated).

**Autopilot arm (or the balance read lies):**
- New objective-agnostic block ABOVE the corpsman PATCH block in SmartStep (the FUL-5 precedent slot,
  Game.Autopilot.cs:160-170): any soldier with an action adjacent to a Downed ally → corpsman with
  Heal ready lets PATCH revive (PrepAbility's Heal arm needs one line: a Downed adjacent ally always
  passes the "missing >= 3" gate — Hp 0 always does, verify only, Game.Autopilot.cs:1150-1157); else
  STABILIZE once (`Stabilized` gates repeats).
- `TryMoveToPatch` (Game.Autopilot.cs:1255) generalizes to hurt-or-downed allies at Cheby 2-3
  (same Cd-gate + bounded approach) so a non-corpsman closes to stabilize too — corpsman-only today,
  which would leave the founding squad (no corpsman, Mission.cs:834 note) with no bot response at all.
- Evac: the existing straggler EXTRACT pull hauls a downed body in from zone-adjacent (verified —
  the bot's extract path uses ExtractCandidate); DRAG-toward-zone is NOT probed in v1 (accepted
  bot-vs-player gap, recorded).
- TileExposure/threat: no change (downed are not threats). AutoStep (dumb smoke bot): no arm —
  timers bound it.

**Measured expectations + budgets (fresh post-FUL-6 reference, the FUL-5 protocol — paired h0 N=10,
slots 0-9, one lever visible per round; a h4 leg last because downs concentrate where deaths do):**
PATCH 4-6 → toward the **>= 10** target *when a corpsman is fielded* — report the per-presence rate
alongside the batch total (the FUL-5 verdict's honest cap: corpsman enters via backfill only;
founding-roster change stays out of scope); STABILIZE (new counter) expected 3-8/batch; soldier
true-KIA per batch down 30-50% (downs converted to saves) with Wound/scar grants up — the death
LEDGER moves to the attrition ledger, which is the design intent; completion budget +10/−5 vs
same-slot R0 (a body saved is the bot playing better — the FUL-5 precedent allows recorded
improvement); DOWNTEST pins Fallen/Memorial single-append (the KIA telemetry FUL-1 ranks must not
double-count). Verdict rule: if bot PATCH still can't reach 10 with downs staged, record it as the
roster-presence verdict for FUL-13's founding-squad question — do not quota-chase.

**Save-compat checklist:** NO persisted field, NO enum member, NO DTO change (transients excluded by
the ToUnitDto whitelist — verified; SAVETEST gets a belt-and-suspenders leg: a Squad round-trip of a
soldier that was downed-and-recovered persists only Hp/Wound/scars); missions checkpoint at start
(the single Save site, Game.cs:1478) so Downed can never exist at a load boundary; EnterBarracks
resolves every Downed before the next checkpoint writes; enemies never serialized. Append-only rules
untouched by construction.

**Harness — `SIGHTLINE_DOWNTEST=1` → `Game.DownSelfTest()`** (Game.Harness.cs; register in
Program.cs on the DKTEST pattern, Program.cs:171-179). Controlled scenes via the DKTEST/MORALETEST
staging helpers:
- (a) DOWN: lethal shot on a soldier → Alive true, Downed true, Hp 0, DownedTurns 3, ActionsLeft 0,
  Fallen/Memorial/_missionKia UNCHANGED, queued reactions at the body purged, squadmates AllyDown.
- (b) EXPIRE: three StartPlayerTurn ticks → dead exactly once; Fallen +1, Memorial +1, cause =
  the downing archetype's Cls; NoLosses now failed.
- (c) STABILIZE: timer frozen at Stabilized; EnterBarracks → in Squad, Hp 1, Wound 3, near-death
  track fired.
- (d) REVIVE: corpsman PATCH on the downed → up at PatchHeal HP (CombatMedic 3 at reach 2), Cd 3,
  Downed cleared, ActionsLeft 0 that turn.
- (e) NO SECOND DOWN: a revived soldier's next lethal hit kills outright; and AoE on a downed body
  (grenade leg) kills outright.
- (f) AI IGNORES: Ai.Plan with one healthy + one downed soldier never returns the downed as
  ShootTarget/prey; all-downed squad → empty plans, no exception.
- (g) VIP: lethal VIP hit still instant (DEATHTEST semantics).
- (h) DRAG/EXTRACT: a downed body drags at Cheby-2 and extracts from zone-adjacent (the existing
  verbs, pinned against regression).
- Shot hook `SIGHTLINE_DOWNSHOT=1`: staged downed soldier (pill + ring + prone) + corpsman adjacent +
  STABILIZE button lit, BOTH palettes.

**Verify.** Release 0/0; DOWNTEST + DKTEST + DEATHTEST + AITEST + COMBATTEST + SAVETEST + PAIRTEST +
CODEXTEST + HORDETEST PASS; autoplay x5 no-exception/no-TIMEOUT (incl. one SIGHTLINE_OBJ=escort and
one =evac run); the measured rounds table + the PATCH-presence verdict into docs/DEVLOG.md;
DESIGN.md §4 death-stakes row re-graded with the measured save-rate. Screenshots: DOWNSHOT both
palettes + one live board mid-rescue.

**Effort:** L. **Files:** src/Unit.cs, src/Game.cs, src/Ai.cs, src/Game.Autopilot.cs, src/Hud.cs,
src/Renderer.cs, src/Codex.cs, src/Game.Endless.cs, src/Game.Harness.cs, src/Program.cs
(+ docs/ROADMAP.md, docs/DEVLOG.md, docs/FEATURES.md, docs/DESIGN.md §4; src/SaveGame.cs and
src/Run.cs need no code — whitelist DTO + DebriefSurvivors already carry the recovered state,
verify only).

---

### Critical Files for Implementation
- /home/user/SIGHTLINE/src/Game.cs (KillUnit/EnterDowned seam, ActivatePod/SetPodSuspicious/ResolveSuspicion link rider, StartPlayerTurn tick, EnterBarracks recovery, IssueDrag/IssueVault FDR grant)
- /home/user/SIGHTLINE/src/Mission.cs (PodPlan sizes + cohesion in SpawnEnemies:459-547)
- /home/user/SIGHTLINE/src/Unit.cs (Downed/DrilledThisTurn transients, BeginTurn, MoveBudget)
- /home/user/SIGHTLINE/src/Game.Endless.cs (wave sub-pods, elite exemption, wave-clear revive)
- /home/user/SIGHTLINE/src/Game.Autopilot.cs (stabilize/revive arm, TryMoveToPatch generalization)