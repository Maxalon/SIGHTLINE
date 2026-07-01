# SIGHTLINE — DEVLOG archive (migrated WIP notes)

> These are the per-session **WIP NOTES** migrated verbatim out of `CLAUDE.md` on
> 2026-07-01 to stop the continuity contract from ballooning. They are a raw,
> chronological record of each autonomous dev-team program/sprint and its
> implementation gotchas. The clean, curated process log is `docs/DEVLOG.md`;
> this file is the unabridged scratch history kept for provenance and for the
> occasional load-bearing gotcha buried in a note.
>
> **Newest entries first-ish (order as it accreted in CLAUDE.md).** Nothing here
> is required reading — start from `CLAUDE.md` and the docs it points to.

### WIP NOTES

> **PROGRAM "HORIZON" — give SIGHTLINE LEGS: new modes + cross-run meta + combat integrity + visual identity (LATEST; read first).**
> Fully-autonomous dev-team session (orchestrator + a 4-lens code-grounded research fan-out + per-wave dev agents
> [one in an isolated worktree] + the SIGHTLINE_BALANCE flywheel). Branch `claude/game-dev-orchestration-a6jxac`,
> **PR #67**. Research (combat-depth / meta-loop / presentation / blue-sky, all grounded in the actual code, not the
> devlog's self-report) converged: the game is tactically deep but has (a) NO reason to replay beyond one ~30-min
> sitting [LEGS — only MaxHeat+LossStreak ever persisted; saves deleted; same 4 soldiers every run], (b) a
> MEASUREMENT-INTEGRITY gap, (c) presentation that under-sells the engine. HORIZON attacks all three. **7 waves, all
> committed + pushed + verified:**
> - **W1 — combat integrity + honest flywheel (`f36f0e3`).** The balance bot's promised `SmartRetreatAfterShot`
>   NEVER EXISTED, so the smart AI never repositioned after firing — the "post-shot positioning" the tempo metric
>   counts was never actually played (the celebrated ~6.15 choices/turn was partly an artifact). Implemented it. Added
>   **EXPOSED BY FIRE**: a unit that fired and didn't move is +12 aim/+12 crit to hit on the opponent's turn
>   (symmetric to the ambush; transient `Unit.MovedAfterFire`), turning post-shot "duck vs double-tap" into a real
>   hedge-vs-gamble + giving the enemy a reason to punish a stationary shooter. Tooltip badge + on-board chevron (W5).
>   MEASURED (honest flywheel, heat-0 N=20): run-completion 80%, policy gap +10, choices/turn 6.16. `Unit.MovedAfterFire`.
> - **W2 — LAST STAND endless horde mode (`86b3cb8`, FLAGSHIP).** New game mode from the intro (key L): hold one arena
>   vs escalating FULL-ROSTER waves (`Mission.MakeEndlessHostile`→`SelectArchetype`; snipers/shields/drones/berserkers/
>   siege deepen the swarm) until wiped. Reports WAVES SURVIVED + persistent BEST WAVE. Logic isolated in a new
>   `src/Game.Endless.cs`; **`class Game`→`partial class Game`** (so future waves add partials, not core-file churn).
>   `SIGHTLINE_ENDLESS`/`_HORDETEST`.
> - **W3 — WAR ROOM cross-run meta-progression (`a8112a7`, the "legs" keystone).** Persistent profile (meta.json,
>   append-only): SALVAGE currency, 7 ACHIEVEMENTS, a HALL OF FAME (fallen KIA + won-run legends), lifetime totals,
>   and 3 ADDITIVE UNLOCKS (StartIntel/StartBoon/StartArmor) bought with salvage. WAR ROOM screen off the intro (key
>   W). **SAFETY INVARIANT: every meta read/write gated behind `!NoPersist` → the flywheel + all `SIGHTLINE_*`
>   harnesses never touch meta and never get unlocks → balance + screenshots stay byte-stable** (verified: an autoplay
>   campaign writes NO meta.json). `src/Meta.cs`, `src/Game.Meta.cs`. `SIGHTLINE_METATEST`/`_WARROOM`.
> - **W5 — visual identity leap (`970fea2`, PARALLEL worktree).** Fixed the inverted hierarchy (units were the SMALLEST
>   thing on the board): bodyR 18.5→24 + silhouette-as-outline + receded cover ⇒ squint test now units>objectives>
>   enemies>cover. Per-biome STRUCTURAL signatures (magma fissures/tundra frost/void-neon grid/dune banding/soot/
>   speckle), louder ambient, fatter tracer + lower bloom knee so kills flood light, the W1 exposed-by-fire on-board
>   marker. Renderer/Fx/Display/Anim only. Integrated via **3-way cherry-pick** (the worktree branched off the
>   pre-VANTAGE base `790304e`; clean auto-merge with VANTAGE's lights/trails — a file-copy would have reverted them).
> - **W6 — CODEX / FIELD MANUAL (`4d62415`, onboarding gap).** A browsable in-game reference (17-enemy bestiary with
>   silhouettes + role blurbs, classes, perks/boons/contracts/specs/traits/scars/weapon-mods/status/objectives) built
>   entirely from the EXISTING Def strings — zero balance change. Intro (key K) + pause menu. `Codex.SelfTest` is a
>   content-completeness guard (a future enum add that forgets its strings fails CODEXTEST). `src/Codex.cs`,
>   `src/Game.Codex.cs`. `SIGHTLINE_CODEXTEST`/`_CODEX`.
> - **W7 — audio drop-in infrastructure (`42991d9`).** Real CC0 audio is now permitted but nothing could load: the
>   csproj never shipped `assets/sfx|music` (BUG — the file-first loader reads them at runtime = the output dir).
>   Fixed the glob (confirmed: `bin/.../assets/{sfx,music}/` now ship), added drop-in folders + `CREDITS.txt`
>   (cue-id convention + CC0 sourcing/size guidance), hardened `Audio.SelfTest` (device-free magic/size validation of
>   any present file), + a `SIGHTLINE_AUDIOASSETS` report. Owner drops in files (audio can't be HEARD here — plumbing
>   only; NOT sourcing external audio blind to avoid unverifiable licensing/quality risk).
> - **W4 — SEEDED DAILY + SKIRMISH (`b3451ee`).** Two single-mission modes complete the intro offering (DEPLOY /
>   LAST STAND / SKIRMISH / DAILY). SKIRMISH (key S): pick objective+heat → one fight on a random arena. DAILY (key Y):
>   a deterministic date-seeded challenge (FNV-1a of yyyymmdd → objective+arena+heat) with a persistent local BEST;
>   headless reads `SIGHTLINE_DAILY` (never `DateTime.Now`). `Util.Reseed` makes the daily board fully reproducible
>   (verified: same seed → identical frame count). `src/Game.Modes.cs`. `SIGHTLINE_SKIRMISH`/`_DAILY`/`_MODETEST`.
> **PROCESS:** Game.cs is the single serialization point, so Game.cs-touching waves (W1,W2,W3,W6,W4) ran SEQUENTIALLY
> (one owner each: spec → dev agent → orchestrator build+self-tests+flywheel+screenshot → commit); the disjoint visual
> wave (W5, Renderer/Fx/Display/Anim) ran in PARALLEL in a worktree, integrated by 3-way cherry-pick (NOT file-copy).
> GOTCHA re-confirmed: an agent's transcript-STUB file can read "static/dead" while the agent is actually
> working+succeeding (W2) — trust the completion NOTIFICATION, not an idle-waiter on the stub. Build 0/0; the full
> self-test sweep PASSES incl. new HORDETEST/METATEST/CODEXTEST/MODETEST + COMBATTEST/SAVETEST/AITEST/SNAPTEST/
> CONCEALTEST/AMBIENTTEST; all four modes autoplay clean (no TIMEOUT); campaign balance held (heat-0 80%). NO CI;
> free-licensed assets only. **OPEN/NEXT (documented):** source real CC0 audio into the W7 loader on a device;
> endless difficulty-curve tuning on-device (the campaign bot caps ~wave 3-4 in a stand-and-fight horde, humans go
> further); a veteran carry-over between runs (deeper meta); more achievements/unlocks; the deferred full Game.cs
> partial-split for throughput (W2 started it: Game is now `partial`, new features live in Game.*.cs partials).

> **PROGRAM "VANTAGE II" — deepen the run-to-run loop: STAKES (scars) + VARIETY (contracts) (prior program).**
> Same fully-autonomous dev-team session, continued after VANTAGE I merged (PR #65). Two more waves on the campaign/
> run-to-run layer (complementing VANTAGE I's per-mission/per-soldier depth), each spec→dev→independent-review→
> flywheel-measure→commit. Restarted the branch from the merged main (the prior PR was finished), opened a NEW PR.
> - **W5 — SCARS & VENDETTAS (`b9ffc11`).** All prior soldier-identity was POSITIVE (feats→traits/nicknames/bonds);
>   this adds the COST of trauma, deepening Pillar 5 ("stakes that bite", DESIGN's thinnest). Mirrors the feat→trait
>   system: an APPEND-ONLY `Scar` enum + `Unit.Scars`/`VendettaFaction`/`NearDeathCount`/`FeatBurned` (persisted via
>   UnitDto + `scarOrdinals` SAVETEST guard), earned in `Run.DebriefSurvivors` from trauma flags, read in
>   `Combat.ComputeOdds`. SHELL-SHOCKED (survived 2+ near-deaths: -1 mob, immune to Disorient/Stun); BURN-SCARRED
>   (survived fire: +3 HP, -aim while burning); HARD-BITTEN (3+ near-deaths: +crit bloodied, -aim at full HP — fights
>   better when it's grim); VENDETTA (+aim/+crit vs the faction that nearly killed you, all run). Each a clear
>   drawback + defiant upside (~a wash, low balance risk); inert on un-scarred units. Dossier shows scars (rust) + the
>   vendetta faction. New `SIGHTLINE_SCARTEST`.
> - **W6 — RUN CONTRACTS (`591b196`).** Boons are run BUFFS, Heat is run DIFFICULTY; a CONTRACT changes the RULES of
>   a whole run — a trade-off chosen at the run-opening DRAFT, so two runs play differently in KIND (DESIGN §3.F
>   variety). **Default = `Contract.None` and the headless/autopilot path never runs the draft → ZERO base-balance
>   regression** (the safest possible add; all 3 forced contracts smoke-tested clean). APPEND-ONLY `Contract` enum +
>   `Run.Contract` (persisted via RunDto + `contractOrdinals` guard). IRON VETERANS (no recruit backfill — a wipe
>   shrinks the squad — but +1 promotion-kill/mission: fewer bodies, faster veterans); HIGH STAKES (+50% Intel but no
>   between-mission field-heal); SPEARHEAD (open UNCONCEALED — no ambush — but a once-per-mission turn-1 +1-action
>   alpha). Compact contract row in the draft UI (STANDARD opt-out default). New `SIGHTLINE_CONTRACT`/`_CONTRACTTEST`.
> **PROCESS/REVIEW:** both Game.cs-touching, ran sequentially; each reviewed by an independent read-only pass over the
> committed shas (no build, so it runs parallel to the next wave). Review of W5+W6 = **SHIP, no CRIT/HIGH/MED** (two
> no-action LOW notes: a grenade-ignite FeatBurned one-tick delay, and intended IronVeterans/HighStakes design notes).
> Build 0/0; SAVETEST/COMBATTEST/CDTEST/AITEST/FIELDTEST/SCARTEST/CONTRACTTEST all PASS; autoplay clean. MEASURED
> (heat-0 N=20, contracts at None default): run-completion **67.5%** (≈ VANTAGE I's 66.7% — confirms no regression),
> policy gap +25 (greedy 80 / sloppy 55 — skill beats sloppy), choices/turn 5.17. **OPEN/NEXT:** scars/contracts are
> player-facing variety the greedy bot doesn't exercise (like DRAG) — a future flywheel pass could force-pick them to
> measure per-contract/per-scar win-rate; more contracts/scars; the still-open VANTAGE I items (win-rate-by-spec
> tuning, Evac/Escort drag, real CC0 audio into the W3 loader, the deferred Game.cs harness split).

> **PROGRAM "VANTAGE" — player decision-space ≥ the AI's + HORIZONTAL progression + STRIKING feel.**
> Fully-autonomous dev-team session (orchestrator + a 3-agent read-only research fan-out + per-wave design-spec
> agents + dev agents in isolated worktrees + independent reviewers per wave + the `SIGHTLINE_BALANCE` flywheel).
> Branch `claude/game-dev-orchestration-7riqqm`. The research converged on the deepest unfixed finding across
> DESIGN §4 / AUDIT #3-4 and three fresh reports: **the player's per-turn AND per-run decision space is shallower
> than the enemy AI's, and progression is almost entirely a POWER axis, not an identity/strategy axis.** Five waves,
> all measured + reviewed + on the branch:
> - **W0 — hardening (`e94ecbb`).** Architecture-audit follow-ups, prerequisite for the enum-adding waves: extended
>   the append-only SAVETEST ordinal guard from just `Objective` to ALL SIX persisted-by-ordinal enums
>   (WeaponKind/Perk/WeaponMod/Trait/Boon/Faction) + APPEND-ONLY banners — a reorder/removal now fails SAVETEST
>   loudly instead of silently corrupting saves. LoS runaway guard fails CLOSED (deny sight); KillUnit guards `_run`
>   null; deleted 6 dead crit-perk constants + a dead Players-alias line. (Deferred the big Game.cs harness-code
>   extraction — H3 — since the gameplay waves are sequential anyway; documented for a future throughput pass.)
> - **W1 — FIELD CRAFT verbs (`46891b2`, best impact/effort).** Two UNIVERSAL positioning verbs that let the player
>   "play the geometry" the AI already plays, both anti-turtle/pro-tempo (no dominant-defensive regression): **DRAG**
>   (key 7, reach-2 — pull a lagging ally one tile toward you: rescue the wounded, accelerate the Evac/Escort/Rescue
>   corner-march, team cohesion) + **VAULT** (key 9 — leap an adjacent cover tile to the floor on its far side in one
>   action; since ALL cover blocks movement this is a genuinely NEW capability: cross/flank/breach an otherwise
>   -impassable cover screen). Both 1 action, never end the turn, once/turn (Unit.DraggedThisTurn/VaultedThisTurn);
>   arrival routes through OnUnitEnteredTile so overwatch/concealment/bleed/fire apply. HUD buttons+glyphs+help;
>   bounded autopilot DRAG in the drag-objectives. New `SIGHTLINE_FIELDTEST`. (GOTCHA caught by wiring+running the
>   self-test myself: the dev's first DRAG was DEAD — a Chebyshev-1 ally's only "toward" tile is the dragger's own,
>   so nothing was ever draggable; fixed to reach-2 = pull a *lagging* ally, which is the useful version anyway.)
> - **W2 — CLASS SPECIALIZATION FORKS (`496c435`, the horizontal-progression keystone).** The 9-program-old wish
>   (DESIGN §3.F): a one-time pick-1-of-2 SPECIALIZATION at a soldier's first Corporal promotion that changes HOW
>   the class plays — swaps/augments its signature VERB or a core RULE, not a +N% stat — so two same-draft runs
>   diverge in kind. New APPEND-ONLY `Spec` enum (None + 10), persisted via UnitDto + `specOrdinals` SAVETEST guard;
>   every read inert on `Spec.None` (old saves/rookies). 10 forks (2/class): Assault BREACHER(grapple staggers)/
>   JUGGERNAUT(+2 armor, reach-1); Ranger PHANTOM(slipstream arms an ambush shot)/PATHFINDER(slipstream free + faster
>   cd — base slip now costs 1 action so this is a real gain); Sharpshooter SENTINEL(overwatch ignores penalty +
>   crits)/HEADHUNTER(mark = squad focus-fire crit); Gunner AREA DENIAL(pin 5×5)/ANCHOR(+2 armor, pin single);
>   Corpsman FIELD SURGEON(patch clears wound+status)/COMBAT MEDIC(patch self + range, −1 heal). Offered in the
>   barracks after perks/before boon; AutoPlay auto-resolves (no stall); balance mode randomizes the pick
>   (`specPicks` telemetry). Sentinel+Guardian use `||` so the overwatch bonus is granted ONCE; the two +2-armor
>   forks read SEPARATELY from shop armor. COMBATTEST cases added. MEASURED: all 10 forks reachable+chosen; the
>   **policy gap flipped from a PATHOLOGICAL −16.7 (sloppy beat greedy) to a healthy +8.3** (greedy 70.8 / sloppy
>   62.5 — skill now matters, in the audit's +7-12 band).
> - **W3 — audio + light overhaul (`e4e7981`, the 'good→striking' lever; DISJOINT worktree, ran PARALLEL).**
>   Transient additive muzzle/impact lights (the bloom bright-pass haloes them for free); tracer wake + grenade/lob
>   arc trails; damage-number sideways arc + scale-punch + spawn flash; per-shot pitch variation + stereo pan in
>   Audio.Play/PlayWeapon (default params keep every call site compiling). **Sample-asset architecture**: an
>   `assets/sfx`+`assets/music` file loader is tried FIRST with the procedural synth as fallback, so real CC0 audio
>   (now permitted) can drop in with NO call-site changes — the synth path stays active here (no device, blind).
>   ALL new visuals deterministic (anim `_t` / frozen FNV hashes, no fresh RNG) so the headless harness stays
>   reproducible; audio crash-safe behind device-ready.
> - **W4 — Decapitate TEETH: the GUARDED HVT (`d25a2a5`).** Fixes the measured weakness (Decapitate was a trivial
>   turn-1 snipe: 2.7 turns, 97-100%). The HVT designates ≤2 nearby bodyguards; while a guard lives within 2 tiles
>   the HVT takes REDUCED (never zero — TIMEOUT-safe: a naive bot still grinds it down) damage, so the kill becomes
>   a positioning puzzle: peel the guards, or GRAPPLE/flank the HVT out of its bubble (synergy with the W1/W2 verbs).
>   Telegraphed (no gotcha): red shield-dome aura + guard link-lines/chevrons, "HVT GUARDED"/"HVT EXPOSED" top-bar
>   readout, a one-shot GUARDED float on a softened hit. Reduction routes through `Combat.HardenedReduce` (the single
>   shot+grenade chokepoint); transient guard flags (no save change); `SmartDecapitate` peels guards first (bounded,
>   falls through). MEASURED: 100%→94% (teeth bite); avg-turns barely moves (2.8→2.9) because the OPTIMAL bot peels
>   guards efficiently — the human design win (a real "expose-then-execute" decision) is undersold by the bot metric.
> **PROCESS:** Game.cs is the single serialization point, so the Game.cs-touching waves (W0→W1→W2→W4) ran
> SEQUENTIALLY (one owner each), each: design-spec agent → dev → independent reviewer → orchestrator measures on the
> flywheel → commit. W3 (disjoint Audio/Fx/Anim) ran in PARALLEL in a worktree, integrated by clean file-copy.
> Specs in `scratchpad/W{1,2,4}-spec.md`. Two independent review passes (W0/W1/W3 then W2/W4): the first found one MED
> (DRAG could move the locked RESCUE captive — fixed, M1) + LOWs; the second = **SHIP, no CRIT/HIGH/MED**. Build 0/0;
> SAVETEST/COMBATTEST/CDTEST/AITEST/FIELDTEST all PASS; autoplay clean (no TIMEOUT). FINAL MEASURE (heat-0 N=24):
> run-completion 66.7% (healthy band — heat-0 was arguably too easy at 79%), **policy gap −16.7→+8.3 (the headline:
> the inverted gap was the real pathology, now fixed)**, choices/turn 5.57. New hooks: `SIGHTLINE_FIELDTEST`.
> **OPEN/NEXT (documented):** win-rate-by-spec instrumentation to prove no single fork dominates (specPicks shows all
> reachable; deeper per-fork win-rate tuning is the open follow-up); W4 guarded-HVT could bite harder for the optimal
> bot (raise HvtGuardReduce/range) if desired; the deferred Game.cs harness-code extraction (audit H3) for throughput;
> the still-draggy Evac/Escort (~8-10 turns even with DRAG); real CC0 SFX/music can now drop into the W3 loader; a
> Corpsman rarely reaches rank-2 so its specs seldom fire (kill-gated) — consider a support-XP path.

> **PROGRAM "VANGUARD" — per-turn DEPTH + a new tactical AXIS + run-to-run VARIETY.**
> Fully-autonomous dev-team session (orchestrator + a 3-agent read-only research fan-out + per-wave design-spec
> agents + dev agents + an independent reviewer per wave + a final cross-wave integration reviewer + the
> `SIGHTLINE_BALANCE` flywheel). Branch `claude/stoic-knuth-zb11k0`. The research converged on the SAME deepest
> finding every prior program circled but didn't fully close: per-turn decisions are **categorically flat** — the
> action economy is solid post-TEMPO, but the impactful verbs (class abilities/items) were once-per-mission charges,
> so the average turn was "move to cover, fire best-EV shot, reposition" and a greedy no-lookahead bot clears
> missions. VANGUARD attacks that on three fronts. **5 waves, all measured + reviewed + on the branch:**
> - **W1 — balance root-fixes (`a95e87f`).** The audit's surviving roots: Sharpshooter dominance is AIM-driven
>   (everyone trimmed its crit, nobody its aim) → NOX 76→72 / recruit 72→68 / Sniper AimBonus 5→3; boss-inversion
>   (BERSERKER/BRUISER HP bump *2→*1 so the WARLORD is the toughest body again); overwatch-camp (Reflexes overwatch
>   +110→+75 — reliable, not auto-99); revived dead choices (COMBAT STIMS 10→8 intel & +2→+3 HP; FRAG CACHE 12→8;
>   SCAVENGER boon re-themed +2 ammo/kill → heal killer +2 HP/kill). MEASURED: Sharpshooter de-dominated (dmg
>   1265→1146, the field tightened), and the 3 dead choices REVIVED (FRAG CACHE 3→95 picks, STIMS 41→87, SCAVENGER
>   the top boon). Save-safe (constants + one boon-effect swap; no enum reorder).
> - **W2 — RENEWABLE ABILITY ECONOMY (`661e62d`, the keystone).** Replaced the 1-charge-per-mission
>   `Unit.AbilityCharge` with a per-unit COOLDOWN `Unit.AbilityCd` (0=ready, ticked at the unit's own `BeginTurn`,
>   set to `Unit.AbilityCooldownFor(kind)` on use): Heal 3 / Slipstream 3 / Mark·Pin·Grapple 2 / legacy stances
>   1-2. Class signature verbs are now a RECURRING per-turn decision, not occasional spice. Transient per-mission
>   state (NOT persisted — no SaveGame/DTO change; old saves load unchanged); action/ammo/end-turn costs unchanged
>   (balance-neutral apart from renewal); autopilot transparent (both bots already gate via `CanAbility`). HUD shows
>   `(N)` cooldown. New `SIGHTLINE_CDTEST`. MEASURED: run-completion stable (Heal Cd-3 keeps attrition intact — no
>   Corpsman runaway). NOTE: the bot-measured decision-richness metric undersells this (it weights target+position,
>   not verb choice) — the human win (a real verb decision most turns) is the point.
> - **W3 — SIEGE/BOMBARD artillery (`82925b5`, a new tactical AXIS).** A telegraphed disruptor enemy demanding a
>   NON-SHOOT response: on its turn it CHARGES a 3×3 strike, telegraphs the danger zone for the player's whole next
>   turn (pulsing red zone + dashed source line + "ARTILLERY INCOMING"), then detonates for heavy cover-ignoring AoE
>   at the top of the next enemy turn — UNLESS the squad relocates / breaks LoS / kills it first. Transient
>   `Unit.ChargeTurns/X/Y` (enemies aren't persisted → no save change); `Ai.BestSiege` (indirect, no-LoS, never
>   centers on allies); `Game.TickSiegeStrikes`/`DetonateSiege` (EnvDamage pattern, fragile-floor, pod-wake, skips
>   the firing gun); `Renderer.DrawSiegeZones`; autopilot parity via a `TileExposure` zone penalty + `SmartFleeSiege`
>   (no TIMEOUT). Spawn m3+, ~5% cascade / Wardens roster, hard cap 1/mission. New `SIGHTLINE_SIEGETEST`/
>   `SIGHTLINE_SIEGE`. Review fixes: gated the Wardens-roster BOMBARD behind m3+ (was a mission-2 fairness breach);
>   the artillery no longer catches itself in its own blast. MEASURED: per-mission win 90-100%, no crater.
> - **W4 — between-mission FIELD EVENTS (`525c396`, run-to-run VARIETY).** Roguelike "?" nodes on the campaign map:
>   entering an Event node presents a situation + 2-3 choices with real trade-offs that mutate persistent run state,
>   so runs branch and feel different. New `src/Events.cs` (10 events: ABANDONED CACHE / WOUNDED MEDIC / DEFECTOR /
>   BLACK MARKET / TRAINING DRILL / DISTRESS BEACON / WAR PROFITEER / CURSED RELIC / ARMS DEPOT / OLD SOLDIER'S
>   GRAVE — every choice carries a trade-off/gamble/opportunity-cost, no pure free power). `NodeKind.Event` appended
>   (append-only); `GenerateMap` stamps 1-2 events onto mid Combat nodes deterministically; seed-derived
>   non-repeating selection + seeded gamble rolls (no save-scum). Outcomes bake into already-persisted Run/Unit
>   fields; `Hud.DrawEventScreen` + "?" node glyph; new `SIGHTLINE_EVENTTEST`/`SIGHTLINE_EVENT`. **Independent
>   review caught a CRITICAL autoplay missed** (the dumb bot routes through Combat siblings, not events): C1 — an
>   event consumed a campaign COLUMN without incrementing `_run.Mission`, so the win gate (`Mission >= MaxMissions`)
>   never fired on an event route → FIX: `ChooseNode` sets `_run.Mission = node.Mission` (column lockstep), verified
>   by biasing autopilot to PREFER event nodes → `WIN mission=6` through event routes. M1/M2 — removed the
>   mid-barracks `SaveGame.Save` (was losing a queued PendingPerk on reload + replaying a cleared mission on
>   CONTINUE); the outcome now bakes at the next mission-start checkpoint, matching shop/perk/boon granularity.
> - **W5 — 4 new authored arenas (`9343960`, parallel content lane).** CRUCIBLE (barrel-rigged chokepoint, MAGMA) /
>   STEPWELL (tier-2 stepped pyramid, STEEL) / COLONNADE (long-sightline pillar gallery, VOID) / ENTRENCHED
>   (asymmetric trenches, ARID). Built in an ISOLATED WORKTREE in parallel with W4 on a strictly-disjoint file set
>   (Maps.cs + Mission.cs — W4 owns Game/Hud/Run/Events/Program), integrated by clean file-copy (zero conflict).
>   The arenas dev also caught that CLAUDE.md's authored-arena COUNT was stale: there are now **32 templates**
>   (indices 0-31), not the "29" the old notes implied.
> **PROCESS:** Game.cs is the single serialization point, so the four Game.cs-touching waves (W1-W4) ran
> SEQUENTIALLY (one owner each), each: design-spec agent → dev → independent reviewer → orchestrator measures on the
> flywheel → commit. W5 (disjoint files) ran in PARALLEL in a worktree. Every wave's spec is in
> `scratchpad/W{2,3,4}-spec.md`. The per-wave reviews caught real bugs autoplay can't (the W4 C1 un-winnable-run
> bug above is the headline). Build 0/0; CDTEST/SIEGETEST/EVENTTEST + COMBATTEST/AITEST/SAVETEST/SNAPTEST/ITEMTEST
> all PASS; autoplay clean (no TIMEOUT, WINs through event routes); cross-wave integration review = **SHIP** (no
> CRIT/HIGH/MED). FINAL MEASURE (N=24 heat0 + N=12 heat4) — see DEVLOG.md for the table. **OPEN/NEXT (documented):**
> the renewable-Heal + SCAVENGER + event-heal sustain stack is bounded but un-co-measured (balance watch);
> `JumpTo` can land the SIGHTLINE_MISSION harness on an event node (harness-only fidelity nit); more verb-renewal
> tuning now that abilities recur; a 2nd disruptor enemy type; more events; the heat-ladder spawn-cap/aim-clamp
> ceiling (R4 in the balance audit) is still the open difficulty-scaling root.

> **PROGRAM "TEMPO" — per-turn decision DEPTH + class normalization + a static-lifecycle refactor.**
> This program executed the evidence-backed roadmap PROGRAM RECKONING's audit left behind (`docs/AUDIT-2026.md` "What
> remains"): it fixed the audit's DEEPEST finding (flat per-turn decisions) plus class dominance and the stale-static bug
> class. Run as a dev team: 3 read-only research/design agents (action-economy redesign + change-surface audit + class/content
> plans) → tech-lead-direct on the coupled keystone (Game.cs is the bottleneck) + a delegated dev for the isolated class
> tuning + an independent reviewer (verdict SHIP, no CRIT/HIGH/MED) → every wave MEASURED on the `SIGHTLINE_BALANCE` flywheel.
> Branch `claude/game-dev-orchestration-34ovtx`. Four waves, all on `main`:
> - **WAVE 1 (KEYSTONE) — "firing no longer ends the turn" (the audit's #1, flagged as deserving its own program).** Root
>   cause of flat turns: an aimed shot zeroed the action budget (`IssueShoot` ActionsLeft=0), collapsing "where do I stand"
>   into a tooltip lookup. FIX: a shot now costs 1 action and does NOT end the turn, so a soldier can move→shoot OR
>   **shoot→reposition** (duck to cover / break LoS) — the new core bet. A 2nd shot/turn is a **rushed follow-up** at the
>   SnapAim penalty (preserves the old ~2-shots/turn DPS ceiling so the enemy-count tuning still holds; the real decision is
>   "duck vs double-tap"). RUN&GUN became a free bonus shot; kill-refunds (MOMENTUM/flank/Adrenaline) clear `FiredThisTurn` so
>   aggressive chains live (capped `Min(3)` + once/turn via `_refundedThisTurn`). Enemy AI MIRRORS it (`Game.TryEnemyReposition`:
>   a shooter caught exposed ducks to cover after firing — gated `curExp>=2.0` so it doesn't crater the sloppy-play floor).
>   SNAP retired (the full-aim non-ending shot dominates it). Decision-richness metric (`CountMeaningfulChoices`) extended to
>   count post-shot positioning. **MEASURED (heat0, N=24): meaningful-choices/turn 1.76 → 6.15 (3.5×, into the 3-5 target band)
>   at IDENTICAL run-completion (67.9 → 68.8%); policyGap -7 → +12 (greedy 75 / sloppy 62, BOTH in the healthy band — skill now
>   matters without punishing). HEAT-4 holds: depth 7.43, run-completion 50% (clean descending ladder).** Files: `Game.cs`
>   (IssueShoot/IssueShootBarrel/TryEnemyReposition/CountMeaningfulChoices/SmartCombatStep/TakeBestShot/AutoShootSmart/hover-odds),
>   `Unit.cs` (`FiredThisTurn` + BeginTurn reset), `Hud.cs` (FIRE/retired-SNAP/RUSHED badge/rules). SNAPTEST rewritten to the
>   new invariants. `SIGHTLINE_SNAPTEST`.
> - **WAVE 2 — class normalization (audit's #2: SHARPSHOOTER was the strict first pick, 128k/88.7% hit).** SAVE-SAFE data
>   tuning only: Sniper CritBase 20→14, DmgMax 8→7, RangeMod harsher point-blank ((dist-4)*4, -30..16); MARK squad-wide crit
>   amp removed (MarkCrit 15→0, keep the +10 aim designator); Ranger base Aim +4 (KRESS 66→70, recruit 62→66) so it OWNS close
>   range (Shotgun +30 close vs Sniper's new -30). MEASURED: Ranger's close-range niche emerged; the false-choice MARK crit is
>   gone; win-rate stayed neutral; the other 3 classes are now tightly grouped with clear niches (close / long / tanky-area /
>   flex). HONEST FINDING: the audit's crit-trim hypothesis was INSUFFICIENT — Sharpshooter's dominance is damage+aim driven, so
>   it remains the top SINGLE-TARGET dealer (thematically a sniper) but is no longer strictly dominant. (Gunner innate-armor was
>   tried + DROPPED — it inflated win-rate past redistribution-neutral; Gunner's niche stays top HP + PIN area-denial.) Per-class
>   kill counts are genuinely NOISY even at N=24 (Gunner survival cascades into who scores) — don't over-tune on one sample.
> - **WAVE 4 — mission-static LIFECYCLE refactor (audit's #4; kills the stale-static bug class).** The 5 per-mission Combat
>   statics (RunBoons/AllUnits/MissionFaction/PrepFaction/PressureAim) were set/cleared at ~14 scattered sites, several
>   commented "defensive: clear ... so no stale value can warp ..." (proof the bug had already bitten). Extracted
>   `Combat.BeginMission/EndMission/EndRun` owning the lifecycle: SetupMission→BeginMission (faction set BEFORE Mission.Build so
>   the spawn roster sees it), barracks→EndMission (refreshes run-scoped RunBoons, clears the 4 mission-scoped), run-end→EndRun.
>   Behaviour-preserving; the DYNAMIC mid-mission updates (PressureAim ramp, AllUnits re-snap on roster growth) are unchanged.
> - **WAVE 5 — tempo-exploiting BUILD VARIETY (the keystone's payoff).** The keystone freed a second action after firing;
>   nothing yet rewarded HOW you spend it. Two append-only (save-safe) perks make it a build axis: **SKIRMISHER** (after you
>   fire, your repositioning move draws NO overwatch — "shoot, then slip away"; folds into `Combat.IgnoresOverwatch` gated on
>   `FiredThisTurn`, distinct from OUTRUNNER's always-on) and **GUNSLINGER** (your rushed 2nd shot fires at FULL aim instead of
>   the SnapAim penalty — the "double-tap" build; the lone rushed-penalty read in IssueShoot/hover/HUD now checks
>   `!HasPerk(Gunslinger)`, HUD shows a DOUBLE-TAP badge). Themed into the class offer-lines (SKIRMISHER → Assault/Ranger,
>   GUNSLINGER → Sharpshooter/Gunner). MEASURED (heat0, N=16): both are REAL picks (GUNSLINGER 20 / SKIRMISHER 12, competing
>   with the old perks — not strictly better); win-rate within noise; depth holds (~5.9). COMBATTEST gained the SKIRMISHER
>   overwatch-gate cases. (Without the class-line biasing they were offered ~never — GOTCHA: a new perk must be added to a
>   `Run.ClassLine` to actually surface in offers, not just to `PerkDef.All`.)
> - **WAVE 3 (content de-bloat — DEFERRED, documented).** The audit's #3 (merge enemy reskins / trim arenas / shorten draggy
>   objectives) was deliberately NOT executed: removing content risks stripping replay variety for marginal clarity gain, and
>   the audit itself counseled caution there. Left as clear FUTURE work — the spawn pool (`Mission.SelectArchetype`) + arena
>   selection (`Mission.PickLayout`) are the levers; do it as a VALUE-ADD (e.g. shorten Evac/Escort via a closer win tolerance,
>   the avgTurns-10 drag), NOT a removal.
> Build 0/0; COMBATTEST/AITEST/SAVETEST/SNAPTEST/ITEMTEST PASS; autoplay clean (WIN/LOSE mix, no exceptions/TIMEOUT);
> independent review = **SHIP** (5 risk areas — autopilot stall, shoot-chain bound, enemy-reposition safety, lifecycle
> correctness, metric null-safety — all traced clean). **PROCESS GOTCHAS:** (a) the keystone's FIRST cut hard-capped at ONE
> shot/turn, which halved player DPS (the old SNAP double-tap was load-bearing for the enemy-count tuning) and cratered
> win-rate to 10.7% — restoring a penalized 2nd shot fixed it (lesson: the ~2-shots/turn ceiling is load-bearing, don't change
> it while adding positional depth). (b) The enemy reposition is a STRONG difficulty lever — the exposure gate swung win-rate
> 75% (gate 3.5) ↔ 50% (full-aggression); 2.0 is the measured sweet spot for balance-neutral. (c) Per-class balance is
> measurement-bound: N=14 is too noisy, N=24 is the floor, and even then it's noisy. **NEXT (documented):** finish class
> normalization (Sharpshooter's damage/aim lead, not just crit — needs a few measured iterations); the deferred content
> de-bloat as value-adds; more tempo-exploiting perks/verbs now that the second action is free (a "skirmisher" duck-perk, a
> no-penalty double-tap perk); heat-ladder re-tune if the +12 policy-gap proves too swingy for real (non-bot) players.

> **PROGRAM "RECKONING" — an AUDIT program: cut the bloat, re-legibilize, fix roots (read after TEMPO).**
> Unlike the eight prior programs (which PILED features + patched symptoms while steering by a win-rate proxy),
> this one QUESTIONED foundations. Five independent read-only auditors interrogated combat math, the run/meta
> loop, content breadth, per-turn decision quality, and architecture+verification — every finding grounded in
> code, not the devlog's self-report. Full audit + results: **`docs/AUDIT-2026.md`**. Branch
> `claude/audit-game-systems-24ldmb`, **PR #61** (independently reviewed → SHIP; self-merged to main).
> **META-DIAGNOSIS:** SIGHTLINE is a well-engineered skeleton made wide and complex by *accretion* — when a
> system misbehaved, a NEW system was bolted on top (anti-turtle clock, crit-damping curve, rotating shop slate)
> instead of fixing the root. Three root-fixing waves shipped, all verified (build 0/0, COMBATTEST/SAVETEST/
> AITEST/ITEMTEST PASS, autoplay clean, flywheel-measured):
> - **Wave 0 — fixed the broken VERIFICATION COMPASS (the deepest finding).** The project measured only "does a
>   greedy bot win ~50% without crashing" — a proxy blind to fun, produced by a bot that never threw items and
>   fell back to the turtle anti-pattern it fights. Now `SIGHTLINE_BALANCE` reports TEXTURE: a **greedy-vs-sloppy
>   POLICY GAP** (swinginess proxy), per-turn **decision-richness** (meaningful-choices/turn) + **lead-swing**,
>   the smart autopilot now exercises items/verbs, and the orphaned balance-JSON path is fixed. Harness-only
>   (`Stats.cs`/`Program.cs`/`Game.cs`, gated behind Stats.Enabled/SmartPlay/SmartSloppy/NoPersist). New knobs
>   `SIGHTLINE_BALANCE_SLOPPY`/`_JSON`. It immediately CONFIRMED 3 audit hypotheses empirically (Sharpshooter
>   dominance, Evac/Escort drag, flat per-turn gradient ≈1.7 choices/turn).
> - **Wave 1 — re-legibilized COMBAT + killed the false-choice crit cluster (keystone; corroborated by 2 audits).**
>   `ComputeOdds` stacked ~17 crit modifiers through a 5-tier `DampedCritStack` curve → unpredictable crit%
>   (violates "Reads clearly"). DELETED DampedCritStack (optional crit bonuses now sum FLAT); cut the flat
>   exposed-crit **35→18** (weakens the rote expose-then-crit dominant line, shifts reward to HIT%/cover); STOPPED
>   OFFERING Deadeye/Opportunist/PointBlank/Vanguard (4 redundant crit perks — **enum KEPT for save-compat**,
>   removed only from `PerkDef.All` + class bias pools + their ComputeOdds branches); Executioner(finisher) vs
>   First Strike(opener) remain as the build-defining crit pair; honest TOOLTIP shows real signed magnitudes
>   (`EXPOSED +18 crit`, `AMBUSH +20 aim/+25 crit`, …). COMBATTEST genuinely rewritten to the flat-sum reality.
>   (`Combat.cs`/`Unit.cs`/`Run.cs`/`Hud.cs`.)
> - **Wave 2 — a RECOVERABLE run container + a REAL economy choice (root fixes).** (a) One-time mid-run
>   **CHECKPOINT**: a squad wipe at mission≥3 is no longer instantly terminal — once per run, `TryReinforcements`
>   rebuilds a fresh ROOKIE cadre (no rank/perks — the price of the wipe) and restarts the current mission,
>   keeping Intel/heat/map; a 2nd wipe is a real loss; VIP/captive-lost stay instant. `Run.CheckpointUsed`
>   (append-only DTO, round-trips, resets on new run). (b) **Economy DE-SEAT**: BALLISTIC PLATING no longer
>   hard-seated first in every shop slate (autopilot bought it ~425x vs ~0) — it rotates in the pool; AutoShop
>   rewritten to spend variedly. (`Game.cs`/`Run.cs`/`SaveGame.cs`.)
> **MEASURED (new compass, heat 0, N=14):** run-completion **64%→68%**; RUN OVER losses 9→7; POLICY GAP +7
> ("healthy slack"); shop spend went from PLATING-425-dominated to a real 6-item spread (PLATING 234 / MAG 66 /
> STIMS 59 / MEDKIT 52 / FRAG 27); cut perks gone from picks, First Strike(18)/Executioner(13) healthy. Mission
> win-rates 87–100% monotonic-ish.
> **NEXT (evidence-backed, the compass now MEASURES these so you can prove a fix):** (1) per-turn decision
> flatness (≈1.7 choices/turn — the deepest finding; the high-ceiling fix "firing doesn't end the turn" deserves
> its own program); (2) SHARPSHOOTER class dominance (Sniper crit/range edge + squad-wide MARK); (3) content
> de-bloat (Evac/Escort/Rescue = one objective in 3 costumes, Decapitate≈Eliminate, ~5 enemies are advW reskins,
> ~20 arenas near-dup — concentrate toward the distinct, but don't strip replay variety); (4) extract a
> `MissionContext` owning the 5 `Combat.*` statics (kills the recurring stale-static-bleed bug class). Known LOW
> (harness-only/cosmetic): `TrySmartItem` smoke branch checks a unit's distance to itself (always 0).
> **PROCESS:** 5 audit agents (parallel, read-only) → 3 dev waves (disjoint hot-file ownership: Wave0 Game/Program/
> Stats, Wave1 Combat/Unit/Run/Hud, Wave2 Game/Run/SaveGame — Game.cs the bottleneck so Wave1∥Wave0 then Wave2
> sequential) → 1 independent reviewer (SHIP). Orchestrator measured all balance centrally (devs build+self-test
> only) to avoid slow contention. GOTCHA: smart-AI balance batches are ~12s/match — keep N≤14 and run them ALONE
> (concurrent dev builds caused timeouts); an agent stalled on its own verify cmd (recover its on-disk work +
> verify yourself, per prior-program notes).

> **PROGRAM "FRONTIER" — the campaign AROUND the fight: visual identity + strategic economy + build depth.**
> Fully-autonomous dev-team session: orchestrator + a 3-agent research fan-out (design-opportunity / code-audit /
> visual-critique) + parallel isolated-worktree devs (strict one-owner-per-hot-file) + an independent reviewer +
> the `SIGHTLINE_BALANCE` flywheel. Branch `claude/game-dev-orchestration-slwslm`, **PR #60**. Thesis (from the
> research): six prior programs exhaustively polished the *fight*; the *campaign wrapped around it* (look, economy,
> audio) was the thin frontier. Every wave grounded in a MEASURED weakness.
> - **Wave 0 — hardening (`c3343d8`):** code-audit follow-ups (no CRIT/HIGH found). `Objective` enum annotated
>   APPEND-ONLY + a runtime ordinal assert in SaveGame.SelfTest; defensive `Combat.AllUnits` clears at barracks/
>   run-end; `Ai.BestGrenade` self-frag veto; barrel-aware connectivity carve; try/finally in `Ai.OddsFrom`.
> - **Wave 1 — VISUAL IDENTITY LEAP (`803b317`, `a04f39a`):** 3 disjoint devs. The board failed the squint test
>   (inverted hierarchy: grey cover was loudest). **Renderer** — recede cover (darker tops, cut emissive rim/gleam),
>   team-colored under-glow + ~16% larger live units, full-alpha enemies, clean dashed-ring dormant pods, pulsing
>   EVAC + amber (de-conflicted from enemy-red) objectives, calmer biome floor, quieter threat pips → units >
>   objectives > enemies > cover. **Display** — real post-FX payoff (luma bright-pass bloom, board-framing vignette,
>   stronger per-biome grade, edge-only impact chroma); default no-POSTFX shot stays byte-stable. **Hud** — killed
>   the empty LOG void, fixed barracks roster legibility, auto-shrink action-bar labels (no more "OVER…"), one-accent
>   top-bar + clearer PRES/ALERT meter.
> - **Wave 2 — STRATEGIC ECONOMY (`42b0600`) + AUDIO (`eec80d4`):** MEASURED dead economy (smart AI bought
>   BALLISTIC PLATING 486x vs ~0 of everything else over 40 runs). **Rotating requisition** — a ~5-item slate per
>   barracks (deterministic from MapSeed+Mission, NO new persisted field) always seating heal+armor + a rotating
>   pool + conditional COUNTER-PREP; Hud/HandleShopClick/AutoShop map slot->id. **Routing economy** — `MissionNode.Intel`
>   (derived, not persisted): base `10+4*mission` (STANDARD economy-neutral), SUPPLY +10, ELITE +14; campaign map
>   shows each node's +N INTEL. **Audio** (disjoint, blind) — ADSR envelopes + filtered noise + click transients give
>   each WeaponKind a distinct layered voice, meatier hit/crit, musically-resolving stingers, fuller ambient/combat
>   beds; device-free safety preserved (AUDIOTEST extended to assert cue presence + near-zero loop endpoints). Owner
>   tunes audibly (no device here).
> - **Wave 3 — BUILD DEPTH (`f0d41df`):** flywheel-dead perks reworked into distinct verbs (enum order unchanged →
>   save-safe; effects only): **ADRENAL→MOMENTUM** (any kill on your turn refunds +1 action, 1/turn, shares the
>   flank-refund guard), **BULWARK→PLATING** (-2 dmg/hit while ≥half HP, ablative), **SPRINTER→OUTRUNNER** (+1 mob +
>   moving never draws overwatch). Single-source predicates `Combat.KillRefundsAction/IgnoresOverwatch`. Also
>   word-wrapped the perk-card description (long descs like OPPORTUNIST were overflowing into the neighbour card).
> **MEASURED (flywheel, same N=40 heat-0-4 methodology, before→after the program):** run-completion **15% →
> 30%→52.5%** across program-end N=40 runs (noisy at 8 runs/heat — trended up, the economy fix is the driver);
> objective **cliffs erased — Evac 57→89 / Sabotage 61→73 / boss-Decapitate 54.5→100**; per-mission win 84-95%
> (healthy, monotonic-ish ladder, not trivialized); 0 frame-cap hits; losses are attrition, not stalls. Reworked
> perks revived in picks (OUTRUNNER 10 / PLATING 9 vs the dead BULWARK 5 / SPRINTER 8 they replaced). **Independent review of W0-W2: SHIP** (no CRIT/HIGH/MED;
> slot→id mapping, AutoShop termination, no-double-intel, save-compat, post-FX gating all verified). Build 0/0;
> COMBATTEST/AITEST/SAVETEST/SNAPTEST/ITEMTEST/AUDIOTEST all PASS; autoplay clean; CB palette holds with the new glows.
> **PROCESS:** the one-owner-per-hot-file discipline + each worktree dev resetting to `origin/<branch>` HEAD as STEP 0
> made every integration a clean file-copy (no merge hazards this program). Game.cs is the bottleneck → economy (W2)
> and perks (W3) ran sequentially; visual (3 files) + audio (1 file) ran fully parallel. **OPEN/NEXT (documented):**
> onboarding is still thin (deep but impenetrable to new players — design-audit #3); more verb-perks / active toys;
> a new objective; the audio needs an audible tuning pass on a real device; Escort dipped slightly (80%, within noise)
> — watch VIP survivability. (Gotcha fixed this session: a stray `git add -A` swept the visual agent's screenshots
> into the tree — removed + `shot_*.png`/`fx_*.png`/etc. now gitignored.)

> **PROGRAM "AGENCY" — decisions that matter: pressure, legibility, loadout, loss, new toys (read
> first).** Fully-autonomous dev-team session run as orchestrator + 2 research agents (gameplay-opportunity +
> balance-audit) + 6 isolated-worktree dev agents + 2 independent reviewers + the `SIGHTLINE_BALANCE` flywheel.
> Branch `claude/game-dev-orchestration-h41iom`. **3 WAVES, 7 features + a UX polish, all measured & reviewed:**
> - **W1 (player-facing decision pressure & legibility):** (A) **anti-turtle PRESSURE CLOCK** — the most-deferred
>   design hole (overwatch-camp was a quiet dominant strategy across 3 prior programs). On camp-friendly
>   objectives (Eliminate/Hack/Decapitate; the movement-pressured ones are excluded) a graced clock (free turns
>   1-4) escalates one rung every 2 turns (cap 4): escalating enemy aim (`Combat.PressureAim` +3/rung) +
>   reinforcement waves from rung 2 (reuse `SpawnReinforcements`/Defend machinery), telegraphed by banner +
>   a `PRES` rung-pip meter in the top bar. Turtling is now strictly worse than advancing. (B) **Visible
>   randomness mitigation** — the hidden graze floor + streak-breaker are now surfaced in the shot tooltip
>   (`DMG GRAZE n / min-max` + `+N STEADYING`), so missing a high-% shot reads as less of a betrayal (math
>   unchanged; `ShotOdds.GrazeFloor/StreakBonus`). (C) **Run-end payoff** — a rich VICTORY/RUN OVER summary card
>   (`Hud.DrawEndScreen`): stat slabs (missions/intel/kills/heat) + SURVIVING SQUAD w/ MVP + a **KIA MEMORIAL**
>   (`Run.Memorial`/`FallenRec`, populated in `KillUnit`, presentation-only/not persisted) + a `Fx.VictoryBurst`
>   flourish on the final win.
> - **W2 (the barracks becomes a real decision layer + new toys):** (D) **ARMORY** — spend Intel at the
>   requisition to re-arm a soldier from their class's thematic weapon pool (`Weapon.ArmoryOptions`,
>   `Game.DoRearm`/`ToggleArmory`, a REQUISITION sub-screen); kills the false-choice weapon lock. Chosen weapon
>   rides the persistent `Run.Squad` unit (round-trips via existing `UnitDto.Weapon`). (D) **MEANINGFUL
>   ATTRITION** — `Run.DebriefSurvivors` no longer instantly backfills to full; recruits trickle 1/barracks above
>   a hard floor of 3 (`RecruitsPerBarracks`/`AttritionFloor`), so a bad mission leaves you short-handed for a
>   mission or two (rookies have no rank/perks) without death-spiralling. (E) **VERB ABILITIES** — replaced the 2
>   most stat-stance signatures with board-changing verbs (append-only `AbilityKind`, save-safe; Steady/RunGun
>   combat paths left intact so COMBATTEST holds): **Sharpshooter MARK** (designate a foe → whole squad gets
>   +10 aim/+15 crit vs it until its next turn — a focus-fire decision; `Combat.MarkAim/MarkCrit` read like
>   crossfire, `Unit.Marked`) and **Assault GRAPPLE** (yank a nearby foe 1 tile out of its cover, reuses
>   `ShoveAnim`, shares the SHOVE per-turn budget). AI uses both directly (no targeting-mode stall → no TIMEOUT).
> - **W3 (close the faction loop + content breadth):** (F) **FACTION-COUNTER PREP** — the campaign already
>   telegraphs the next node's faction; now a barracks requisition item (12 Intel) buys a one-mission counter:
>   vs SYNDICATE → HARDENED OPTICS (deny see-over-low-cover), vs LEGION → REACTIVE PLATING (+1 squad armor),
>   vs WARDENS → FIELD SMOKE (cancel long-range aim edge). `Run.PrepFaction` (persisted, append-only DTO),
>   `Combat.PrepFaction` static gated `==MissionFaction` (no-op otherwise), applied+consumed at SetupMission;
>   AutoShop skips it. (G) **2 enemies + 3 arenas:** **LANCER** (HOPLITE — phalanx; AI rewards ending adjacent
>   to hostiles + exempt from anti-cluster → forms a wall → grenade lure) and **HOUND** (FERAL — fast swarmer
>   that beelines the most-isolated soldier, never retreats, spawns in pairs); pure `Ai.Plan` biases + distinct
>   silhouettes (no Game/Combat change). Arenas GARRISON/PINNACLE/REFINERY (Maps 27/28/29). EnemyHint telegraphs
>   them. Plus a **2-column REQUISITION grid** polish (the shop hit ~10 items and was clipping).
> **MEASURED (flywheel, integrated build, heat-0 N=50 / 249 missions):** run-completion **~60%**, per-mission
> **81-100%** (no gate), per-objective **81-100%** — **Escort is no longer a cliff (92.5%)** — avg 4.58/6 cleared,
> 0 frame-cap hits (no TIMEOUT). Attrition (17 RUN OVER) is the main loss pressure without cratering. The base is
> **well-tuned — no tuning pass was needed**; the Heat ladder carries mastery. Build 0/0; COMBATTEST (+faction-prep
> cases)/AITEST/SAVETEST/ITEMTEST all PASS. **Reviews:** Wave 1+2 reviewed → **SHIP** (no CRIT/HIGH/MED; attrition
> floor, pressure spawn/TIMEOUT fences, and static resets all verified). **PROCESS GOTCHA (important, recurred &
> solved):** a worktree dev's `git diff origin/branch` patch SILENTLY BUNDLED A REVERT of an earlier-merged dev's
> work when origin moved between launch and patch-gen (E's patch was effectively "verbs MINUS armory" → applying it
> stripped D's armory). FIX: regenerate the patch as `git diff <merge-base> <devcommit> -- <explicit owned files>`
> (the pure feature diff), and for later waves the dev RECORDS `$BASE=$(git rev-parse HEAD)` right after reset and
> diffs `git diff $BASE HEAD -- <explicit file list>` — never `origin/branch`. With that protocol, F & G applied
> 100% clean. New hooks: `SIGHTLINE_PRESSURE/_TOOLTIP/_SUMMARY/_ARMORY/_MARK/_PREP/_CONTENT`. **OPEN/NEXT
> (documented):** more verbs for Ranger/Gunner/Corpsman; a new objective type; a strategic overworld economy;
> audio on a real device (free CC0 assets now permitted by the owner); higher-heat ladder re-tune if needed.
> **W4 (SHIPPED after the W1-3 merge, PR #59):** completed the verb vocabulary — **Ranger SLIPSTREAM** (free
> overwatch-immune reposition, was BLITZ) + **Gunner SUPPRESSING FIRE** (AoE pin/area-denial, was single-foe
> SUPPRESS), so all 5 classes now have a board-changing verb. Measured heat-0 N=30 ~57% (within variance);
> COMBATTEST/AITEST/SAVETEST PASS, autoplay clean. Reset hygiene verified (Slipstreaming cleared in BeginTurn,
> Pinned via ClearPins). Heat-4 ladder spot-check (pre-W4): run-completion ~43% (vs heat-0 ~60%) — healthy
> descending ladder, missions 4-5 the intended pinch (68-73%), no hard gate.

> **PROGRAM "RESONANCE" — feel, tactical depth & balance (read first).** Fully-autonomous
> dev-team session (orchestrator + 2 research agents + balance-audit agent + 4 isolated-worktree dev agents
> + 1 reviewer + the `SIGHTLINE_BALANCE` flywheel). Branch `claude/game-dev-orchestration-1g6v1h`, PR #57.
> MEASURED baseline at session start: run-completion **40%**; biggest *felt* gap = all 5 weapons shared ONE
> firing sound; weak spots = Sabotage (noisy ~65-90%), Evac/Escort 13-14-turn drag, a flat/non-monotonic heat
> ladder. **3 WAVES SHIPPED, each measured:**
> - **W1 (audio identity + economy + balance):** per-`WeaponKind` firing voices (shotgun boom / sniper crack /
>   smg snap / lmg chug / rifle) + crit thud + event stingers (kill/lastkill/victory/lose/squadwipe) + device-
>   free `SelfTest`/`SIGHTLINE_AUDIOTEST` (Audio.cs/Anim.cs/Program.cs). **Economy decouple** — intel is now
>   `10+4*mission+survivors` (was `8+3*survivors+mission`, a rich-get-richer death-spiral); BALLISTIC PLATING
>   14->8 + AutoShop buys plating FIRST (front-load the survivability sink); VIP patience 4->2. **Stats**
>   records shop purchases + boon picks (`shopPurchases`/`boonPicks`). **Dev B balance** (Mission/Combat,
>   re-applied onto KEYSTONE trunk via logical merge): Sabotage force-trim + covered fighting positions; heat
>   aim-clamp 82->88 (top-rung StatDelta was silently eaten); **crit-perk DIMINISHING RETURNS**
>   (`Combat.DampedCritStack`, optional crit bonuses scale 1.0/0.8/0.6/0.45/0.3 so a 3rd/4th crit perk isn't a
>   dead pick). **MEASURED: run-completion 40% -> 47-53%; heat ladder now MONOTONIC (h0 ~87% -> h4 ~25-37%,
>   vs the broken baseline 25/37/37/37/62).**
> - **W2 (environmental hazards — the marquee depth feature):** explosive **barrels** (new `Grid.Barrel[,]`,
>   blocks move via the `IsFloor` chokepoint; `Game.DetonateBarrel` = cover-ignoring AoE + cover demolition +
>   chain-detonation + a fire field; **shootable** via `BarrelShotAnim`/`CanShootBarrel`/`IssueShootBarrel` in
>   the aim path + a grenade-chain) and spreading **fire** (`Grid.Fire[,]`, 3-turn deny-ground, applies
>   Burning on step-in + refresh-in-flame each round via `TickHazards`). AI avoids fire (-60) / barrels (-14);
>   autopilot shoots 2+-enemy barrels. Connectivity-guarded placement (`Mission.PlaceBarrels`, CostMap-flood
>   verified) + a `'B'` authored-arena legend (PILLARS/FOXHOLES/CHASM). Drum + animated-flame + aim-reticle
>   rendering (Renderer.cs). `SIGHTLINE_HAZARDTEST` PASS, `SIGHTLINE_HAZARD` shot. 18+ autoplay runs clean (no
>   TIMEOUT). **MEASURED: 42.5% run-completion (barrels add depth without cratering; avgCleared 4.35, highest).**
> - **W3 (EXTRACT lift-out verb — Evac/Escort/Rescue drag):** a soldier in the evac zone can EXTRACT an
>   adjacent ally / VIP / freed captive (key **X**, 1 action, no end-turn) — hauls them the last step into the
>   zone, ending the long "march everyone to the corner" tail. `HasExtractAction`/`CanExtract`/`DoExtract` +
>   contextual HUD button/icon/tooltip + autopilot use in SmartEvac/SmartEscort/SmartRescue. MEASURED: lifts
>   Escort win ~85->90% (secures the fragile VIP at the threshold); avgTurns noisy at 40 runs.
> - **W4 (INCENDIARY item — player fire agency):** the Sharpshooter's utility item is now INCENDIARY (was a
>   3rd Smoke) — lob it to lay a 3x3 fire field that denies ground, ignites foes, and cooks barrels (`IssueItem`
>   case + `IncendiaryAnim` reusing `LobAnim`/`Grid.AddFire`/the barrel-cook path). Completes the hazards arc:
>   fire is now a player VERB, not just barrel residue. Player autopilot avoids ending a move in fire
>   (`TileExposure` +20). Independent code review of W1-W3: **no CRITICAL/HIGH findings — "ship it"** (chain
>   recursion bounded, no fire double-count, `IsFloor`/connectivity fenced, EXTRACT occupancy/turn/win-check
>   correct, crit-damping single-bonus invariant preserved).
> 14 self-tests green; build 0/0; autoplay clean across all objectives + heat 0/4/8. **NEW HOOKS:** `SIGHTLINE_AUDIOTEST`,
> `SIGHTLINE_HAZARDTEST`, `SIGHTLINE_HAZARD`. **PROCESS GOTCHA (re-confirmed + important):** `isolation:worktree`
> dev agents branch off OLD `origin/main` (4de6fc9), NOT the current branch HEAD — a file-copy of such a worktree
> SILENTLY REVERTS all intervening commits' changes to the copied files (it cost a Program.cs draft-hook revert,
> caught + fixed). MITIGATION: (a) push the branch so agents can `git reset --hard origin/<branch>` as STEP 0
> (later agents did this — verify via `git -C <wt> log -1`); (b) before integrating, `git diff <base> <wt>` and
> if base != trunk, re-APPLY the logical diff onto trunk (don't file-copy) — done for Dev B's Combat/Mission.
> **DOCUMENTED FUTURE WORK:** anti-turtle pressure clock on Eliminate/Hack (overwatch-camp is a quiet dominant
> strategy); loadout/gear choice (false-choice weapons); a 2nd hazard source (incendiary item / MORTAR fire);
> tune the Heat-4+ ceiling; procedural music on a real device.

> **PROGRAM "KEYSTONE" — decisions that matter, run-open to each turn (read first; full log in
> `docs/DEVLOG.md`).** Fresh fully-autonomous dev-team session (orchestrator + 3 research agents + 5 isolated-
> worktree dev agents + 2 reviewers + the `SIGHTLINE_BALANCE` flywheel). Branch `claude/adoring-lovelace-2f6q3c`,
> **PR #56**. A 3-agent research fan-out (decision-quality / opportunity / balance) found: the per-turn space was
> thin (~2 real options; anti-turtle offloaded onto enemy AI), the run opening/economy/enemy-identity were
> decision-thin, and mission-1 lost 20% (a heat-3/4 alpha-strike on the green opener, capping a geometric-product
> run). **5 WAVES SHIPPED, each measured:**
> - **W1 (balance/measurement):** early-mission HEAT GRACE (ramp Heat's bodies/stats in over m1-3) — MEASURED
>   mission-1 win 80%->100%; + a run-completion-by-HEAT metric in `Stats` (the ladder's true shape). (Reverted a
>   Sniper crit trim — data showed it didn't fix class dominance + risked DPS.)
> - **W2 (per-turn depth):** CROSSFIRE — a target hit from 2+ diverging angles (>~72deg) takes +10 aim/+10 crit
>   (symmetric; static `Combat.AllUnits` read like `RunBoons`, set by `Game.RefreshCombatRoster`); + smarter
>   enemy AI (crossfire-seeking, proactive smoke/flash); + anim-speed toggle (F2) + SHOVE reach-2. `+ CROSSFIRE`
>   tooltip + an on-board converging-fire indicator (Renderer).
> - **W3 (run-opening):** squad DRAFT — pick 4 of 6 operators + a starting DOCTRINE before m1 (the run's thesis);
>   gated OUT of the harness via `!NoPersist`. `SIGHTLINE_DRAFTTEST`/`SIGHTLINE_DRAFT`.
> - **W4 (enemy identity):** FACTIONS — the ~14 archetypes -> 3 factions warping POSITIONING (SYNDICATE sees over
>   low cover / LEGION +aim+crit closing / WARDENS +aim long), faction-gated rosters + a telegraphed node hint +
>   banner + faction-named HOSTILES counter. `Combat.MissionFaction` (default None == old behavior). Per
>   Combat/Elite node, deterministic from MapSeed.
> - **W5 (smarter opponent):** the enemy AI USES SHOVE — a rusher/Legion enemy shoves an adjacent covered soldier
>   out of cover to expose it (or collides if pinned); `EnemyPlan.ShoveTarget` + a `Game.UpdateEnemy` exec reusing
>   `ShoveAnim`.
> MEASURED: heat-0 run-completion ~83% (W2) / ~33-43% overall across heat 0-4 with factions+AI-shove live —
> winnable base, a declining Heat ladder, every per-mission rate 81-97%. 3 reviews APPROVE-WITH-NITS (nits
> applied). Also de-flaked the COMBATTEST graze band. **14 self-tests green; build 0/0; autoplay clean (no
> TIMEOUT).** New hooks: `SIGHTLINE_DRAFTTEST`/`SIGHTLINE_DRAFT`. **DOCUMENTED FUTURE WORK (clear runway):** the
> Evac/Escort 13-15-turn drag (per-soldier "lift-out" extraction); a strategic OVERWORLD economy (intel scarcity
> / route opportunity cost, R2's pick); faction-counter PREP between missions (make the telegraph actionable);
> more player verbs / suppression-as-area-denial; procedural music on a real device. **PROCESS GOTCHAS:** worktree
> base is unpredictable (check `git merge-base` + grep for recent symbols before integrating — file-copy only if
> base==trunk, else 3-way cherry-pick); a cherry-pick onto a heavily-edited hot file is riskier than doing the
> coupled work directly (so AI-shove was tech-lead-direct); ALWAYS rebuild before trusting `--no-build` self-tests
> (a stale binary masked a real build break); reviewer "dead code" nits are hypotheses — the compiler is the arbiter.

> **PROGRAM "CRUCIBLE" — completability + variety + readability (read first; full log in
> `docs/DEVLOG.md`).** Fully-autonomous orchestrator + 4 parallel research/audit agents + parallel dev agents
> (isolated worktrees, disjoint files) + the `SIGHTLINE_BALANCE` flywheel. Branch `claude/awesome-bardeen-jd6u5q`,
> PR #54. The MEASURED master problem: full-run completion was **~2%** — a 6-mission geometric-product collapse
> with no compounding survivability term + hard gates. Research converged: "long run + terminal wipe + power-only
> meta = the FTL↔XCOM/Hades dead zone; add recovery valves + lateral variety." Shipped, measured at every step:
> - **Wave 1:** deep ROSTER (carry 6) + DEPLOY-GROWTH (deploy 4→5→6 by mission, the action-economy master lever)
>   + adaptive ASSIST (Hades God-Mode loss-streak meta, `Run.LossStreak`/`AssistStatRelief`, base-heat only,
>   persisted in meta.json) + boss node → DECAPITATE (`Run.CardForNode`) + 4 arenas + game-feel juice.
> - **Wave 2:** ARMOR reward-sink (`Unit.Armor`, folded into `Combat.HardenedReduce`; sold as BALLISTIC PLATING)
>   + BULWARK/VANGUARD perks + barracks DEPLOY-PICKER (`Game.ToggleBench` cap-aware + `Hud` deploy UI) + Escort
>   fix (VIP Armor in `Mission.MakeVip` + cut the anti-VIP AI "finish frenzy" in `Ai.Plan`) + Renderer readability.
> - **Wave 3:** 10 run-scoped BOONS (`enum Boon`/`BoonDef`, `Run.ActiveBoons`/`BoonOffer`, FIELD DOCTRINE pick
>   each barracks, read via static `Combat.RunBoons` + on-kill in `CreditKill`; the anti-same-y keystone) +
>   always-on combat-log (`Stats.CombatLog`, recorded in `ShotAnim.Apply`) + combat-log/active-boons HUD.
> - **Wave 4/5:** in-mission combat-log + active-boons HUD; **SHOVE** forced-movement verb (ITB-style:
>   slam an adjacent enemy 1 tile — slide+expose, or collision damage if blocked; `ShoveAnim`/`ShoveMode`/
>   `SIGHTLINE_SHOVETEST`); **perk build-trees** (rank-up offers biased to each class's thematic line).
> - **Fixes:** robust anim-queue pop (`Game.Update`: only pop index 0 if `a` is still front — an anim's Update
>   can mutate `_anims` via KillUnit/overwatch → intermittent IndexOutOfRange); EVAC zone 2×2→2×4 (a 5+-soldier
>   squad couldn't fit the 4-tile zone → unwinnable → TIMEOUT); difficulty RECALIBRATION (stacked squad power
>   overshot to 54% → restored `Mission.SpawnEnemies` count `4+n`/bump `n-1`); hard autopilot turn-cap
>   (`AutoStallCheck` force-loses at 50 turns so the smoke test/batch never hit the frame-cap TIMEOUT).
> MEASURED ARC (competent AI, heat 0-4): **2% → 28% → 32%** run-completion (avg ~3.6 missions; no mission gate
> below 70%/mission; heat ladder validated: heat-0 ~57% run → heat-8 ~3% run; AutoStep 12/12 clean, no TIMEOUT).
> A **16x lift** — base winnable, the 8-rung Heat ladder carries mastery. New hooks `SIGHTLINE_BOON`/
> `SIGHTLINE_SHOVETEST`; COMBATTEST extended (armor/bulwark/vanguard). PR #54 (Waves 1-4) merged; Wave 5 (SHOVE +
> perk-trees + turn-cap) on the branch. **NEXT (documented future):** deliberate squad draft at run start;
> animation-speed toggle; tune the Heat-8 ceiling; AI use of SHOVE; procedural music on a real device.

> **PROGRAM "DEEP STRIKE" — NEW MULTI-WAVE PUSH (read first; full process log in `docs/DEVLOG.md`).**
> Fresh fully-autonomous session running the project as a dev team (orchestrator + parallel dev agents in
> isolated worktrees + independent reviewers + research/audit agents). Develops on
> `claude/fervent-fermat-6lxlyv`, opens PR #51 as a review surface, and **self-merges it to `main` once
> green** (no human review — this is a you-owned project; see the autonomy ground rule). A research+audit pass converged
> on: coordinated enemy AI = biggest fun lever, plus output-randomness mitigation, content breadth, and a
> replay ladder. **WAVE 1 SHIPPED (6 commits, all verified — build 0/0, 10/10 self-tests PASS, 14+ autoplay
> runs clean, no TIMEOUT):**
> - **Coordinated enemy AI** (`2e39592`): per-turn `Game.PlanEnemySquad` -> `EnemyFocus` (focus-fire) +
>   `PlayerOverwatchTiles` (overwatch-aware routing, anti-turtle), read as advisory biases in `Ai.Plan`;
>   plus low-HP fighting-retreat ("FALLING BACK"), anti-cluster, range-band kiting. New `SIGHTLINE_AITEST`.
> - **4 new arenas** BASTION/CHASM/SPUR/HOOK + **richer procedural** (4 archetypes via `Mission.BuildProcedural`) (`d2291cf`).
> - **Enemy-overwatch threat indicator** + unit-facing/impact FX polish (`2c92401`, `Renderer`/`Fx`).
> - **Balance** (`a4be498`): Executioner/Guardian/CoolHeaded de-dominated; full-HP player can't be one-shot.
> - **Combat-tooltip transparency** (`1cd4d61`): 14 modifier badges, each mirroring `ComputeOdds`.
> - **Review follow-ups** (`83314f6`): truthful overwatch model, captive-focus skip, perf hoist, stronger AITEST.
> Worktree gotcha: agent worktrees branch off near-empty `main` — every dev must `git reset --hard
> claude/fervent-fermat-6lxlyv` first (all did; orchestrator verifies base+scope before integrating).
> **WAVE 2 SHIPPED (5 commits, all verified — build 0/0, 10/10 self-tests PASS, autoplay clean across
> heat 0/3/6/8 + objectives, no TIMEOUT):**
> - **HEAT / ASCENSION ladder** (`8420ab3`): 8 cumulative rungs (more/tougher enemies, sooner contact,
>   harsher attrition, top-tier EXPOSED=no concealment) + per-mission intel bonus; unlocked-max persists
>   in `meta.json` (rises on a win at cap); run heat in the append-only Run DTO; intro selector + HUD pill;
>   heat-0 = byte-stable no-op; `SIGHTLINE_HEAT=<n>` hook. (`Heat` table in `Run.cs`.)
> - **HUNTER + MORTAR enemies** + **NEON/MAGMA biomes** + **per-run biome variety** (`94dd65e`): HUNTER
>   flanks (AI seeks exposing tiles), MORTAR is a back-line grenadier (rides the existing grenade AI);
>   both reuse the existing exec (no Game change). `Biome.For(missionNum, runSeed)`.
> - **Combat-feel juice** (`932be49`, `Anim`/`Fx`): impact frames, directional sparks, grenade shockwave +
>   debris, tracer polish, movement dust — new Fx Ring system + helpers, all fired from Anim, scale graze<hit<crit.
> - **Integration wiring** (`d69faad`): biome call site -> `For(n, MapSeed)`; HUNTER/MORTAR in `Run.EnemyHint`.
> - Independent review of the Heat ladder: APPROVE-WITH-NITS (save-format append-only + no-TIMEOUT confirmed).
> **WAVE 3 SHIPPED (4 commits — build 0/0, SNAPTEST/COMBATTEST/AITEST/SAVETEST PASS, autoplay clean across
> all 7 objectives + heat 6/8, no TIMEOUT):**
> - **Aimed-vs-SNAP shot + flank-kill action refund** (`c6fb609`): SNAP (key 7) = 1 action, no end-turn,
>   -15 aim; flank-kill on the player turn refunds +1 action (cap 1/soldier/turn) -- per-turn decision +
>   anti-turtle tempo. Null-safe + bounded. New `SIGHTLINE_SNAPTEST`.
> - **3 perks** OPPORTUNIST/POINT BLANK/GIANT SLAYER (`c1e4e16`, pure ComputeOdds, append-only enum).
> - **3 arenas** GRID/FORGE(tier-2)/CONDUIT + NEON/MAGMA biome affinity (`2d6210f`).
> - Glue: new-perk tooltip badges + `1211e95` SNAPTEST harness wiring.
> **WAVE 4 SHIPPED (6 commits — build 0/0, all 11 self-tests PASS, autoplay clean across heat 0/4/8 + all
> 7 objectives, no TIMEOUT):** a read-only **balance-audit agent** found the real problems, then:
> - **Enemy-intent telegraph** (`fb59009`): before each hostile acts, a ~0.5s beat shows its plan (dashed
>   move path + destination ring + target reticle + verb caption) from the same `_aiPlan` that executes --
>   the Into-the-Breach fairness lever. SKIPPED under AutoPlay (frame counts A/B-verified). `SIGHTLINE_INTENT`.
> - **Balance sweep** (audit-driven, 4 disjoint lanes + 2 orchestrator fixes): GIANT SLAYER (dead) ->
>   FIRST STRIKE + OPPORTUNIST flank-gated (`52eccc0`); enemy grenades require LoS + MORTAR dialed back
>   (`3e3685e`); Heat rung-8 NoReinforcements + cap 10->12 + intentional spawn tiers (`6cefead`); flank-kill
>   refund requires a genuine flank (de-snowball) + grenade fragile-floor (`c2ce0ee`).
> **WAVE 5 SHIPPED:** **DECAPITATE** objective (`fcaeb81`) -- kill the marked HVT (a buffed toughest-enemy
> with a gold ring/crown marker); win on its death regardless of the other hostiles. 8 objectives now.
> Append-only enum, `ObjectiveFor %7->%8`, autopilot branch (no TIMEOUT), `SIGHTLINE_OBJ=decapitate`.
> **WAVE 6 SHIPPED + SELF-MERGED:** the **CORPSMAN** (`bc2427d`) -- a 5th player class (medic/support) whose
> PATCH ability heals the most-wounded adjacent squadmate +4 for one action (the squad's first in-combat
> sustain). New `AbilityKind.Heal` (append-only, Cls-derived); recruit-pool entry; white-cross glyph.
> **STATUS: 6 waves shipped (~21 features + a balance pass), Waves 1-5 merged via PR #51, Wave 6 self-merged.**
> This is a you-owned project with no human review -- PRs are self-merged once green (see the autonomy rule).
> Next open ideas (any future wave): more objectives (hold-zones/extract-intel), a 6th class, an anti-turtle
> pressure clock, progressive-HUD/combat-log polish, audio tuning on a real device. Game/Hud are the per-wave
> bottleneck (one owner each). The multi-agent cadence (disjoint files, parallel reviewers, read-only audit
> agents, orchestrator wiring/fixes) is proven across 6 waves.

> **AUTONOMOUS DEV-TEAM SESSION — 7 SPRINTS, 21 FEATURES + a 4-FEATURE CODE RECOVERY (read first).**
> Ran the project as a multi-agent team (orchestrator/tech-lead + PM/research + architect +
> parallel developer agents in isolated git worktrees + a peer-reviewer each sprint). 7 peer-review
> rounds. **PR #47 (Sprints 1-6) and PR #48 (Sprint 7) were merged to `main`.** What shipped across 21
> features:
> - **S1:** 4.3 alert tiers · 4.4 concealment+ambush · 5.2 post-FX shader · 3 arenas.
> - **S2:** graze/partial-hit · real font (NotoMono OFL — ASCII-only limit LIFTED) · enemy-AI smoke/flash.
> - **S3:** bench/short-handed attrition · focal-point lighting · streak-breaker.
> - **S4:** staggered deployment formation · opt-in auto-cam · cover shape-cues (owner "two firing lines"
>   feedback; bigger maps deliberately DEFERRED — DEVLOG documents the NO-GO + flag-prototype path).
> - **S5:** campaign-map intel hints · procedural texturing + soft-glow particles · 2 arenas (11 total) + biome affinity.
> - **S6:** enemy AI seeks high ground/commanding-view LoS · procedural HUD action-bar icons.
> - **S7:** 3 new perks (EXECUTIONER/GUARDIAN/COOL-HEADED) · emissive cover/plateau edges.
> Reviews caught real issues autoplay can't — notably **bench silently destroying benched veterans**
> (fixed + guarded by `SIGHTLINE_BENCHTEST`) and **graze deleting true misses at high hit%** (GrazeMinMiss
> floor). New harness hooks: `SIGHTLINE_POSTFX/_CONCEAL/_CONCEALTEST/_BENCH/_BENCHTEST/_ALERT`. Full
> process log + per-sprint results/learnings + the larger-maps NO-GO live in **`docs/DEVLOG.md`**.
>
> **⚠️ 4-FEATURE RECOVERY (this session) — the git hiccup was worse than first reported.** A stray
> branch-rename + `reset --hard` mid-session truncated the integration merge and silently dropped FOUR
> fully-built, peer-reviewed features from the merged trunk (the commits survived only as DANGLING
> objects): **S5 procedural texturing** (`a610e3d`+seam-fix `d1521cd`), **S5 campaign-map intel hints**
> (`d5ebbbb`), **the 2 new arenas RUINS/THICKET + biome affinity** (`15394e2`), **S6 HUD action-bar
> icons** (`e5ce19b`), and the **S6 AI commanding-view tile-scoring** enhancement (`078a46b`). They were
> recovered by cherry-picking the dangling commits back onto the branch (2 small complementary conflicts
> in Renderer.cs = keep BOTH texturing + S7 emissive; a 3-region add/add in Hud.cs = take the icon
> methods but keep S7's Guardian→OVERWATCH specialty). Re-verified: Release 0/0, all 6 self-tests PASS,
> autoplay x5 clean (no TIMEOUT), screenshots confirm texturing grain + HUD icons render. **LESSON:**
> after ANY `reset --hard`/branch-rename, diff the trunk's TREE against the feature commits
> (`git log -S <marker> --all`, grep the src for each feature's marker) — do NOT trust the merge-commit
> message's feature count. `git checkout <ref> -- <file>` also STAGES (bundles per-feature commits); use
> one dev per hot file (Game/Hud/Renderer/Unit/Combat) per wave; review + autoplay together are the net.
> **NEXT (open Phase-5 polish, any future `.` session):** 5.1 visual style-guide doc; 5.5 full
> semantic-color enforcement; generated HUD objective/status icons; S2-C overwatch-camp soft-pressure;
> tune the procedural music on a real audio device; the flagged bigger-maps prototype only if
> formation+auto-cam playtests still want it. Multi-agent cadence proven.

> **SPRINT 1 (autonomous dev-team) — SHIPPED: 4.4 concealment + 5.2 post-FX shader + 3 arenas.**
> Ran as a multi-agent team (orchestrator + PM/research + architect + parallel devs in isolated
> worktrees + research). Three features landed on `claude/vigilant-faraday-4cjiff` (PR #47):
> (1) **4.4 Concealment + ambush** (marquee) — see the 4.4 roadmap entry + Current state. Files:
> Unit/Combat (FiredFromConcealment + AmbushAim/Crit + ShotOdds.Ambush), Game (SquadConcealed,
> BreakConcealment + triggers, CheckPodActivation gate, AutoStep break, ConcealSelfTest), Hud
> (CONCEALED pill + "+ AMBUSH"), Renderer (ghost ring), Program (SIGHTLINE_CONCEAL/CONCEALTEST).
> (2) **5.2 post-FX shader** — embedded GLSL in `Display` (vignette/bloom/grade/chroma); bloom fed
> by `Game.AddBloom` on hits/kills; headless OFF (byte-stable), `SIGHTLINE_POSTFX=1` to view.
> (3) **3 arenas** in `Maps.cs` (CROSSROADS/FOXHOLES/RIDGE; RIDGE uses tier-2 `=`).
> Process note / gotcha: a dev agent can spawn a sub-agent and "come to rest" with the child still
> running — check the worktree branch state (`git -C .claude/worktrees/agent-<id> ...`) before
> assuming done. Integration was file-level (`git checkout <branch> -- <files>`) to avoid
> merge-base churn, since the concealment dev based its work on a pre-post-FX trunk; Game.cs/
> Program.cs (touched by BOTH post-FX and concealment) were hand-merged. Verified end-to-end:
> Release 0/0, CONCEALTEST/COMBATTEST PASS, autoplay clean x5 + sabotage/rescue/defend.
> `docs/DEVLOG.md` tracks the team process. **Next:** Sprint 2 (graze/partial-hit S2-A, enemy AI
> utility-items S2-B, overwatch-camp soft-pressure S2-C) + the font (NotoMono, OFL, on-machine).

> **4.3 ALERT / AWARENESS TIERS (latest) — SHIPPED.** Replaces the binary
> dormant->instant-scatter pod model with a graded `AlertLevel` so first contact is
> telegraphed (DESIGN.md §5/§6: never a pure gotcha) and the "free scatter on reveal" is
> softened to surprise-only. Files touched: **Unit.cs** (new `enum AlertLevel { Unaware,
> Suspicious, Alert }`; `Active` is now a get-only `=> Alert == AlertLevel.Alert`, so all
> the read sites — music intensity, threat preview, `_aiUnits`, autoplay-stall — are
> unchanged; only the ~5 WRITE sites flipped to `e.Alert = ...`). **Mission.cs** (pod spawn
> `e.Alert = AlertLevel.Unaware`). **Util.cs** (`Pal.Suspect`/`SuspectDk` amber). **Game.cs**
> (the activation block: new `AlertRange=4`, `ClosestSightedDist`, `SetPodSuspicious`,
> `ResolveSuspicion`, `ActivatePod` = the surprise/scatter path; `ResolveSuspicion()` call in
> `EndPlayerTurn` before `_aiUnits`; `DebugAlertTiers`). **Renderer.cs** (3-way glyph:
> grey "?" / amber "!"+pulsing ring / live foe). **Program.cs** (`SIGHTLINE_ALERT=1` hook).
> The model: CheckPodActivation (runs on player tile-entry + in UpdatePlayer) — sighted
> within SightRange(9) but >AlertRange(4) => pod **Suspicious** (no act/scatter, "CONTACT?");
> sighted ≤AlertRange OR shot/pinned/grenaded => straight to **Alert** WITH the (4.2-capped
> single-move) scatter. At EndPlayerTurn, `ResolveSuspicion` turns every Suspicious pod into
> Alert (still in sight; **no scatter** — it acts on the coming enemy turn) or back to Unaware
> (contact broken). KEY INVARIANT: Suspicious only ever exists *within* a player turn (the
> telegraph window) — it's always resolved at the turn boundary, so there's no stuck/oscillating
> state and no new TIMEOUT risk (the existing `AutoStallCheck` still treats Suspicious as
> `!Active` and force-wakes after 10 stalled turns). Gotchas: (a) `Util.TileDist` returns
> **float** (Euclidean) — `ClosestSightedDist` is float, don't make it int. (b) shooting a
> Suspicious enemy is allowed (it's targetable like the old dormant ones) and the existing
> `if (!target.Active) ActivatePod(...)` correctly snaps it to Alert+scatter. (c) the amber
> Suspect color is near VipGold, but the "!" marker + enemy-side body distinguish them.
> Verified: Release 0/0, autoplay x5 clean (LOSE, no exceptions/TIMEOUT — expected for the
> weak smoke AI), `SIGHTLINE_ALERT=1` shot shows all three tiers. **Next Phase 4 step:** 4.4
> **concealment + ambush** (the marquee mechanic — squad starts concealed, player chooses
> when to break stealth; pairs with these tiers: breaking concealment = going red). Then 4.5
> is the DEFERRED fog-of-war prototype (only if 4.4 proves insufficient). Phase 5 (post-FX
> shader, font) is independent and can interleave.

> **4.2 ENCOUNTER GEOMETRY (latest) — SHIPPED.** Fixes the turn-1 forced-ambush problem
> (DESIGN.md §5) while keeping perfect information + the 18x11 full-bleed board (did NOT
> grow the map — "not raw size"; bigger needs a camera). Three levers:
> (1) **`Game.SightRange` 12->9** — pods wake on a closer sighting (they're always drawn,
> so only *activation* is delayed; readability untouched).
> (2) **Mid-field high-cover screen** in `Mission.Build`'s procedural branch — a staggered
> band at cols 7-11 (replaced the old 5 `PlaceBlock` central structures) that breaks the
> long cross-board sightlines. Designed connectivity-safe: no column fully walled, **row 5
> left open** as the one risky direct lane. So a soldier can advance into the midfield
> behind cover without auto-tripping a pod (the verify criterion), while the open lane is
> the deliberate high-risk route. Sprinkle ratio nudged to ~55% high (LoS-blocking).
> (3) **Capped reveal-scatter** in `Game.ActivatePod`: was the full `Ai.Plan` path (up to a
> dash) — the criticized "free move on reveal"; now accumulates step cost (ortho 2/diag 3,
> matching `CostMap`) and stops at one move (`Mobility*2`); an immobile turret (Mobility 0)
> gets 0. Plus `Mission.EnsureConnectivity` (new): floods from squad[0], carves an L-lane
> by clearing cover toward the squad for any unreachable hostile/evac/terminal/sabotage
> tile; runs for BOTH layout paths (authored maps are verified pre-`TryCover`, so this
> also catches the protective-cover edge case). Gotchas: (a) only HIGH cover + smoke block
> LoS (low cover/plateaus don't) — the screen is HIGH cover on purpose; (b) all cover
> blocks movement, hence the connectivity guard; (c) `MoveBudget` has a `Max(1,..)` floor
> so I cap the scatter on raw `Mobility*2` to keep turrets immobile. Verified: Release 0/0,
> autoplay x6 clean (no TIMEOUT => connectivity holds across random procedural maps), shot
> openings show the screen, `SIGHTLINE_MAP=2` (PILLARS) still applies. **Next:** 4.3 alert
> tiers (green/yellow/red, soften the binary dormant->scatter further), then 4.4 concealment.

> **4.1 FULL-BLEED UI — first Phase 4 code step, SHIPPED.** Reclaimed the
> ~40% chrome/margin so the board fills the frame. Two files only: **`Util.cs`** (`Cfg.Tile`
> 56->64, `Cfg.OriginY` 64->40; `BoardW/H` + `OriginX` are derived so they followed) and
> **`Hud.cs`** (bottom bar decoupled from the board: `barY = Cfg.ScreenH-106` instead of
> `OriginY+BoardH+14`, taller bottom scrim `ScreenH-150..ScreenH`; new `Hud.PanelShadow`
> drop-shadow behind the roster chips + unit card so they read as floating). NO Game/Renderer
> code needed changing — every tile<->px conversion already routes through `Cfg`/`Util`
> (TileCenter/TileRect/ScreenToTile), the camera uses `BoardCenter`, and HUD hit-testing
> uses stored Hud rects (checked before the board, so floating panels keep click priority).
> Tile 64 is deliberate: at OriginX=64 the left roster (x8..140) covers only board col0
> (no spawns) and just grazes col1, so player units stay clear. Gotchas / things I checked:
> (a) player spawns are rows 2/4/5/7/9 + cols 1-2 — all clear of the top (y40) & bottom
> (y694) HUD bands; only board row 10 + the bottom-left corner sit under the (translucent)
> bottom HUD, and nothing spawns there. (b) `Program.cs` `helpShot` parks the cursor at
> (592,740) — still on the ability button (button row unchanged at y720..760), no edit
> needed. (c) figure constants in `Renderer.DrawUnit` are still absolute px, so units are
> ~12% smaller *relative* to the bigger tiles — looks fine/cleaner, left as-is on purpose.
> Verified: Release 0/0, autoplay x3 clean (LOSE, no exceptions/TIMEOUT — expected), shots
> of Eliminate/Extract/biomes/pause/shop/campaign-map all read well. **Next Phase 4 step:**
> 4.2 (encounter geometry: spawn standoff + sightline-blocking terrain — that one DOES touch
> gameplay/`Mission`/`Maps`), then 4.3 alert tiers, 4.4 concealment. Phase 5 (post-FX shader,
> font) is independent and can interleave.

> **DESIGN SESSION pt.2 — asset policy + visual identity, DOCS ONLY.** The human
> clarified the **art policy**: the old "no external art/audio assets" overstated it. Real
> rule (now in the pillars block + DESIGN.md §3.H): **no *hand-made/human-authored*
> assets**, but **generated assets ARE allowed** — **procedural/in-engine/shader first, AI
> only where it clearly wins**, and **commit small generated files** (no large binaries;
> keep the repo lean). This lifts the ASCII-only `?` limitation **once a font ships**
> (Phase 5.3). Researched game **visual design** (squint test / visual hierarchy, limited
> palette + value contrast + 60-30-10, semantic color coding, Into-the-Breach "communicate
> not compel", post-processing/bloom, procedural texturing) → added a **§3.H Visual design**
> section + style guide to `docs/DESIGN.md` and a new **ROADMAP — PHASE 5 (visual identity &
> presentation)** above (5.1 style guide / 5.2 post-FX shaders / 5.3 font / 5.4 procedural
> texturing / 5.5 semantic+colorblind / 5.6 focal+lighting). Engine note: Raylib generates
> noise/gradient textures + bakes TTF fonts in-engine, so most visual upgrades need **no
> committed binaries** — a small font file is the main thing worth committing. Still
> DOCS-ONLY — no source changed. Build order: Phase **4.1** (full-bleed UI) is still the
> natural first code step; Phase 5 is independent and can interleave (5.2 post-FX + 5.3 font
> are the highest visual lift).

> **DESIGN SESSION pt.1 — DOCS ONLY, no code.** The human asked for an educated,
> deliberate look at the game's *principles & feel* (not more features). Ran a research
> pass into game-design fundamentals (MDA; game feel/juice — Swink/Vlambeer; Sid Meier
> "interesting decisions"; input vs output randomness; flow/difficulty; UX/readability +
> affordances; enemy telegraphing; roguelike meta-loops; onboarding) and wrote it up as
> **[`docs/DESIGN.md`](docs/DESIGN.md)** — a *rationale/decision* contract complementing
> CLAUDE.md's *build/continuity* contract: sharpened pillars (added **Reads clearly** +
> **Stakes that bite**), a principles library with **Do/Don't**, an **honest
> self-scorecard**, and §5 the **information-design decision**. Key finding (validated by
> the code's own numbers): the **encounter *opening* is the weakest link** — 18-wide map
> + ~8-tile moves + 12-tile pod sight range => almost any advance trips a pod on turn 1
> (anxiety + gotcha). Decision: fix it **while staying perfect-information** via full-bleed
> UI + spacing/density + alert tiers + **XCOM2-style concealment** (DESIGN.md §5 Option 3);
> **fog of war + restrictive camera DEFERRED** (identity pivot; flag-prototype only if
> concealment proves insufficient). Specced as **ROADMAP — PHASE 4** above (4.1 UI -> 4.2
> geometry -> 4.3 alert tiers -> 4.4 concealment -> 4.5 deferred fog). **No source files
> changed this session.** Next session: start **4.1** (full-bleed UI — safe, high value),
> then 4.2/4.3, then 4.4. (This was a design/planning pass per the human's instruction;
> nothing to build-verify — `main`/the harness are untouched.)

> **3.12 ONBOARDING TUTORIAL (finishes Phase 3).** Non-blocking first-run
> callout. `Game`: `TutStep` (-1 inactive) + `TutPrompts[4]` + `_tutMoved`/`_tutOver`/
> `_tutShot` (set in `IssueMove`/`DoOverwatch`/`IssueShoot`) + `_tutDoneTimer`.
> `StartTutorialMaybe()` (end of `SetupMission`) starts it only when `!NoPersist && Mission
> ==1 && !Display.TutorialSeen`, then `Display.MarkTutorialSeen()` (new flag in the Display
> Dto → display.json) so it shows once ever. `UpdateTutorial(dt)` (in `Game.Update`)
> advances 0→1→2 when the matching flag sets; step 3 auto-clears after 7s. `EndPlayerTurn`
> also calls `AdvanceTutorial` for steps 0-2 so it can't get stuck across turns.
> `Hud.DrawTutorial` (called in `Hud.Draw` on the player/enemy turn when `TutorialText!=null`)
> draws a word-wrapped "TRAINING x/4" card above the action bar (new `WrapText` greedy
> wrapper). Harness hook `SIGHTLINE_TUTORIAL=1` (sets `TutStep=0`; harness is NoPersist so it
> never auto-starts). Gotcha: ASCII-only — prompts use `-` not em dashes (the default font
> renders `—` as `?`). With this, ALL of Phase 3 (3.1-3.13) is complete.

> **3.13 ACCESSIBILITY (brightness + colorblind).** Two pause-menu options,
> persisted in `display.json`. **Brightness:** `Display.BrightLevels` {0.70..1.30}, idx
> default 2 (100% = neutral). `Display.DrawBrightness()` draws a fullscreen black (darken,
> α=1-b) or white (lighten, α=(b-1)·0.55) quad at the end of BOTH `RenderFrame` paths;
> neutral draws nothing so headless shots are byte-identical. `CycleBrightness`. **Colorblind:**
> `Pal.Foe`/`FoeDk`/`Good` changed from `readonly` to mutable; `Pal.SetColorblind(on)` swaps
> them to CB-safe hues (Foe→`(238,138,40)`, Good→`(40,200,168)`); `Display.ToggleColorblind`.
> Pause menu (`Hud.DrawPause`) grew to 9 buttons (h 504→612) with `PauseBright`/`PauseColorblind`;
> clicks in `Game.HandlePauseMenu`. `Display` Dto persists `BrightIdx`+`Colorblind`; `Load`
> applies `Pal.SetColorblind` (only in non-headless, since `Display.Load` runs only when
> Enabled). Screenshot hooks `SIGHTLINE_CB=1` + `SIGHTLINE_BRIGHT=<idx>` (both apply in the
> direct RenderFrame path, which the harness uses since Display is disabled). TODO: true
> contrast/gamma (shader); independent UI text scale (all DrawText sizes are hardcoded).

> **3.10 PROCEDURAL MUSIC (blind ship — Phase 2 D too).** `src/Audio.cs` gained a
> looping music layer alongside the SFX. `BuildAmbient` (A-minor sine pad + slow LFO
> tremolo) + `BuildCombat` (tenser pad + 2 Hz sub-bass pulse), each an 8-second buffer
> using **integer-Hz tones over an integer-second loop → seamless loop** (sin is 0 at both
> ends; LFO/pulse periods divide the loop). `InitMusic` (end of `Audio.Init`, so it's
> skipped when `!_ready`) loads both via `LoadMusicStreamFromMemory(".wav", …)`, sets
> `Looping=true`, `PlayMusicStream` both at volume 0, sets `_music`. `Audio.UpdateMusic(dt)`
> (new call in the `Program` window loop after `game.Update`) pumps `UpdateMusicStream` +
> lerps ambient/combat volumes toward targets from `_intensity`. `Game.MusicIntensity()`
> (called in `Game.Update` → `Audio.SetMusicIntensity`): 1 enemy turn / 0.5 live hostiles /
> 0.15 clear / 0 menus. Mute (M) → `Enabled=false` → both volumes 0. Unloaded in `Shutdown`.
> Gated behind `_music` so it's a **no-op with no audio device** (headless screenshots/
> autoplay unchanged — verified). **BLIND SHIP: not heard here.** TODO for whoever has audio:
> confirm it loops without clicks + isn't too loud (master is 0.6; music targets ~0.5/0.62),
> tune `Build*` recipes. Could add per-phase stingers / a win/lose musical resolve.

> **3.8 DEFEND OBJECTIVE (completes 3.8).** `Objective.Defend`: survive
> `Game.DefendTurns` (8) player turns vs mid-mission waves. `CheckEnd` Defend branch wins
> when `_turnCount > DefendTurns` (a squad wipe still loses via the alivePlayers==0 guard).
> `Game.SpawnDefendWave()` is called at the TOP of `EndPlayerTurn` (before the per-enemy
> `BeginTurn` loop + `_aiUnits` build, so the new wave acts that enemy turn): on odd turns
> below the limit, spawns `2 + missionNum/2` `Mission.MakeWaveHostile` (public; basic
> grunt/scout scaled by n) at the right edge (`W-2`/`W-1`), `Active=true PodId=-1`, capped
> at 12 alive. `Run.ObjectiveFor` is now %7 (+Defend); `GenerateOffers` pool +Defend.
> `RollSecondary` excludes SWIFT on Defend (impossible to finish before turn 8). HUD shows
> "DEFEND x/8" via `Game.Turn` (new public getter for `_turnCount`). Autopilot Defend
> branch: shoot/reload/overwatch/hunker to hold (no advance). Harness `SIGHTLINE_OBJ=defend`.
> Verify: autoplay survives → advances past the forced mission (confirmed on mission 1),
> else wipes; no exceptions/TIMEOUT. Gotcha: waves are real `Enemies` entries — they ride
> all the generic systems (overwatch/FX/AI); they're active immediately (no pod scatter).

> **3.8 RESCUE OBJECTIVE.** `Objective.Rescue` (rotation now %6, + `GenerateOffers`
> pool). Reuses the `Vip` unit as the captive (`Cls="VIP"`, renamed "CAPTIVE"); new
> `Game.CaptiveLocked`. Setup (`SetupMission`): make the Vip, add to `Players`, lock it,
> then AFTER `Mission.Build` re-seat it at `(W/2,H/2)`, clear its ring, Mobility 0,
> `SyncPos`. Invulnerable while locked: guarded in `CanTarget` (`d==Vip && CaptiveLocked`)
> and `GrenadeAnim.Explode` (skip the caged Vip). `TryFreeCaptive` (called from
> `CheckPodActivation` AND `AutoStep`) unlocks on a soldier Chebyshev≤1 → Mobility 6 +
> `BeginTurn` + "CAPTIVE FREED" banner. `CheckEnd` Rescue: lose if freed-then-dead, win if
> freed + in evac (shared top-right zone, same as Evac/Escort). Autopilot Rescue branch:
> non-VIP soldiers path to the captive to spring it then fight; the freed captive walks to
> evac (VIP path). Reserve+connectivity: the captive seat is passed as the `terminal` Build
> arg (HasTerminal stays false so no terminal renders/draws) so authored maps keep it
> reachable; `TryApplyLayout` now also verifies `sabotage` sites. Renderer: caged = gray +
> cage bars + "CAPTIVE", else gold "FREED". HUD "RESCUE/EXTRACT CAPTIVE". Harness
> `SIGHTLINE_OBJ=rescue`. Gotchas: (a) the captive rides in `Players` so `!IsVip` filters
> (squad counter, barracks) already exclude it; (b) `DebugForceObjective` only forces the
> FIRST mission. TODO: 3.8 DEFEND (wave spawner + survive-N-turns).

> **3.8 SABOTAGE OBJECTIVE.** New `Objective.Sabotage` (enum + `Run.ObjectiveFor`
> 5-cycle + the legacy `GenerateOffers` pool). `Game`: `SabotageSites` (List of 3 tiles
> seeded in `SetupMission`: mid-map spread), `SabotageBlown` (HashSet of done indices),
> `HasSabotage`/`HasHackAction`, `NearestSabotageSite`. The HACK action is generalised —
> `CanHack`/`DoHack` branch on `HasSabotage` (one PLANT per adjacent un-blown site →
> `SabotageBlown.Add`), HUD button label "PLANT", `ActionDesc` updated. `Mission.Build`
> took a `sabotage` param that reserves each site + its 8-ring as open floor (like the
> terminal). `CheckEnd` Sabotage branch wins when all sites blown. `Renderer.DrawSabotage`
> draws blinking red charge consoles (green ARMED once set), wired after `DrawTerminal`.
> Autopilot Sabotage branch walks to the nearest un-blown site + plants. Barricade
> placement excludes sites. Harness: `SIGHTLINE_OBJ=sabotage` (`Game.DebugForceObjective`,
> applies on shot OR autoplay) → autopilot WINs it. Gotcha: `DebugForceObjective` only
> forces the FIRST mission's objective (re-runs SetupMission); later missions revert to the
> map's card. TODO: 3.8 DEFEND (wave spawner + turn limit) + RESCUE (captive→escort).

> **3.9 SECONDARY OBJECTIVES.** Optional per-mission bonus goal worth +12 intel.
> `SecondaryKind` {None,NoLosses,Swift,CleanSweep} + `Game` fields `Secondary`/
> `SecondaryFailed` + consts `SwiftTurns=7`/`SecondaryIntel=12`. `RollSecondary(n)` in
> `SetupMission` (none on mission 1; CleanSweep only when Objective!=Eliminate, else it'd
> be automatic). Tracking: `KillUnit` sets `SecondaryFailed` on a non-VIP soldier death
> (NO LOSSES); SWIFT/CleanSweep evaluate at end. `SecondaryAchieved()` (NoLosses =
> `_missionKia.Count==0`, Swift = `_turnCount<=SwiftTurns`, CleanSweep = `AliveEnemies==0`)
> runs in `EnterBarracks` → +intel + a `Report` line ("BONUS: … cleared" / "Bonus missed").
> HUD: `Game.SecondaryHud` (live label incl. SWIFT turn count) + `SecondaryOnTrack` (green/
> red) drawn at x=812 in the top bar. Not persisted (per-mission, rebuilt each SetupMission).
> Verify: `SIGHTLINE_MISSION=3` shot (HUD "BONUS …") + autoplay. Gotcha: `SecondaryAchieved`
> reads `_missionKia` which is cleared in `SetupMission` (not DebriefSurvivors), so it's
> still valid at `EnterBarracks` time. TODO: more goal types (hack a side cache / no damage);
> autopilot doesn't optimise for the bonus (passive only).

> **3.7 SAPPER (latest, completes 3.7).** Cls `SAPPER` ("BREACH", Shotgun, mission 3+,
> no grenades). `EnemyPlan.SapTile` (nullable). `Ai.Plan`: `sapTarget =
> Grid.CoverTile(nearest, e)`; a scoring term pulls the sapper adjacent to it; after the
> tile loop, if it ended Chebyshev≤1 of a still-standing cover tile, set `plan.SapTile` and
> null `ShootTarget` (grenade + hunker/overwatch fallbacks gated on `SapTile==null`).
> `Game.UpdateEnemy` sap branch (first in the act chain) `DamageCover(HighCoverHp)`s the
> tile + `CoverHitFx` + "BREACH" pop. Renderer: demo-charge marker (square + red dot).
> Spawn re-banded (SAPPER r<0.59 on n≥3); excluded from the grenade-assignment roll.
> Verify: autoplay missions 3-6 clean. Gotcha: `CoverTile` is computed from the sapper's
> CURRENT position (approx); fine since it re-plans each turn.

> **3.7 NEW ENEMY ARCHETYPES (drone / shield / mid-boss).** Three new hostiles
> in `Mission.SpawnEnemies` (probability chain re-banded). **WASP** (`Cls="DRONE"`, SMG,
> low HP, mob 7, mission 2+): `Combat.ComputeOdds` sets `ignoresCover = a.Cls=="DRONE"`
> which folds into `seesOver` (negates the target's cover entirely — attacks from above);
> `Ai.Plan` advW 3.0 + subtracts its own cover value so it beelines; `Renderer.DrawUnit`
> floats it above its shadow (`hover`, diamond glyph). **AEGIS** (`Cls="SHIELD"`, Rifle,
> tanky, mob 4, mission 3+): new `Unit.ShieldDx/Dy` (set to -1,0 = faces west toward the
> squad); `Combat.ShieldedFrom(d,ax,ay)` → if the shot comes in on the barred side,
> override cover to full high (lvl2/40 def) regardless of terrain, unless `seesOver`
> (drone/commanding tier-2) bypasses it; `Renderer` draws a frontal `DrawRing` arc.
> **Mid-boss** (`Cls="ELITE"`): a named band on missions 3 (BREAKER) & 5 (WARDEN), HP
> `14+2n` (vs WARLORD `20+2n`), `midBoss = !finalMission && i==0 && (n==3||n==5)`; the
> renderer name tag + rage banner now read `u.Name` instead of a hardcoded "WARLORD".
> Verify: `SIGHTLINE_COMBATTEST` (added drone-ignores-cover + shield front/flank cases) +
> `SIGHTLINE_MISSION=3` shot. Gotcha: the elite rage in `Game.UpdateEnemy` keys on
> `Cls=="ELITE"`, so mid-bosses rage too (intended). TODO: SAPPER (cover-destroyer AI);
> AEGIS only ever faces west (no dynamic re-facing); enemy DRONE/commanding-view symmetry.

> **3.6b 2ND ELEVATION TIER.** `Grid.Height` now supports level 2. Combat is
> relative: `Combat.ComputeOdds` uses `heightAdv = HeightAt(a)-HeightAt(d)`; `seesOver`
> negates LOW cover for any height edge and ALSO HIGH cover when `heightAdv>=2`. New
> `Grid.HasLineOfSight(x0,y0,x1,y1, overHighCover)` overload (the old 4-arg signature
> delegates to it with `false`); when `overHighCover`, intermediate HIGH cover doesn't
> block but SMOKE still does. `Game.CanTarget` computes `commanding = heightAdv>=2` and
> passes it, so a tier-2 shooter can target a lower foe through high cover. Renderer:
> `ElevRect`/`ElevCenter`/`DrawCover` lift by `HeightAt*ElevLift` (was binary `IsHigh`);
> `DrawElevation` draws a wall of `(h-belowH)*ElevLift` and a slightly brighter top for
> tier 2. `Mission.RaisePlateau` gained a `level` param; procedural branch raises a tier-2
> 2x2 redoubt on missionNum>=4; `Maps.cs` legend `=` → Height 2 (no authored map uses it
> yet). No climb cost (Height is a pure positioning layer — plateaus are walkable floor),
> so reachability holds without ramps. Tests: `SIGHTLINE_COMBATTEST` tier-2 case +
> `SIGHTLINE_COVERTEST` `commandingSeesOverHigh` LoS-overload check; shot `SIGHTLINE_ELEV=1`
> (`DebugElevation` stamps a tier-2 redoubt + tier-1 step + a high-cover block). Gotcha:
> the AI's own LoS checks (Ai.cs / overwatch) still use the default blocking LoS, so the AI
> doesn't yet exploit the commanding view — fine/safe, just not symmetric. TODO: AI use of
> tier-2; a themed authored arena using `=`.

> **3.6a DESTRUCTIBLE COVER.** Cover tiles now degrade. `Grid`: `CoverHp[,]` +
> `HighCoverHp=2`/`LowCoverHp=1`, `CoverHit` enum, `MaxCoverHp`/`SetCoverHp`/`ResetCoverHp`/
> `DamageCover` (High→Low→Floor, resets HP to the lower level on downgrade) + `CoverTile`
> (frontal-block pick, mirrors `GetCover`'s side logic). `Mission.Build` calls
> `ResetCoverHp()` once terrain is final (after the per-unit cover pass). Sources:
> `GrenadeAnim.Explode` now `DamageCover(x,y,HighCoverHp)` per blast tile (high→low,
> low→gone — replaced the old instant low-clear); `ShotAnim.Apply` calls
> `Game.TryChipCover(A,D)` on a hit (LMG any range / shotgun ≤2 tiles, gated on the target
> actually having cover via `GetCover(...).Level>0`). `Game.CoverHitFx` = FX/sound per
> `CoverHit`; barricade (`IssueItem`) calls `SetCoverHp`. `Renderer.DrawCover` draws dark
> fissures when `CoverHp < MaxCoverHp` (only a chipped-but-not-degraded High block shows
> them; a downgraded tile is fresh Low). Test: `SIGHTLINE_COVERTEST=1` (window-free,
> `Game.CoverSelfTest`) + `SIGHTLINE_COVER=1` shot (`DebugCover` chips 8 high blocks).
> Gotcha: HP lives in the reused `Grid`, so the per-mission `ResetCoverHp()` is essential —
> don't remove it or cover starts a mission at 0 HP and dissolves on first contact.
> **REMAINING for 3.6:** part (b) the 2nd elevation tier (taller plateaus + ramps) is NOT
> done — see the roadmap checkbox.

> **3.4 UTILITY ITEMS.** Second throwable slot beyond grenades. `Unit`:
> `ItemKind` enum {None,Smoke,Flash,Barricade}, `Item` (derived from `Cls` via
> `ItemKindFor` — Ranger/Sharp=Smoke, Assault=Flash, Gunner=Barricade), `ItemCharge`
> (1/mission, refilled in `Mission.Build`; NOT persisted — derived like grenades).
> `Grid`: new `Smoke[,]` layer; `BlocksSight` now also blocks on `Smoke>0` (so LoS +
> overwatch are cut), `IsSmoke`/`AddSmoke`/`TickSmoke`/`ClearSmoke`. `Game`: `ItemMode`/
> `ItemValid`/`ItemTx,Ty`, `ToggleItem`/`ItemTargetOk`/`IssueItem` (mirrors the grenade
> input/cancel sites — added `ItemMode` to every `AimMode/GrenadeMode` reset incl. Esc/
> right-click/ToggleAim/DoAbility), key **6** + `DoAction("item")`; `Grid.TickSmoke()`
> ticks once per round in `StartPlayerTurn`. `Anim.cs`: `LobAnim` base (arc + one-shot
> `Effect`) → `SmokeAnim` (`Radius=1`,`Turns=3`) + `FlashAnim` (`Disoriented` 2 turns,
> breaks OW, wakes pods). Barricade = instant `Tiles=LowCover` in `IssueItem` (no anim).
> `Renderer`: `DrawSmoke` (drifting haze over units) + `DrawItem` (range ring + footprint);
> wired into `DrawBoard`. `Hud`: ITEM button (key 6, bw shrunk 112→104 to fit 8 buttons)
> + `ActionDesc`. Autopilot throws items ~30% in the Eliminate branch (covers all 3 anim
> paths). Test: `SIGHTLINE_ITEMTEST=1` (window-free, `Game.ItemSelfTest`) + `SIGHTLINE_ITEM=1`
> shot (`DebugItem` arms a smoke preview AND drops a live cloud). Gotchas: (a) smoke blinds
> BOTH ways — a unit standing in its own cloud can't shoot out through the adjacent smoke
> tile (intended); (b) flash/barricade reuse `ItemMode` targeting, so barricade validity
> is gated by `ItemTargetOk` (empty floor, not evac/terminal); (c) `LobAnim.BlastRadius`
> is virtual so the arc/footprint draw matches each kind. TODO: enemy AI item use; loadout
> UI; "+ SMOKED" shot-tooltip flag.

> **3.3 BRANCHING CAMPAIGN MAP.** Slay-the-Spire node path replaces the
> 3-card barracks pick. New in `Run.cs`: `NodeKind`, `MissionNode` (Col/Row/Kind/Card/
> Next/Visited), `Run.Map`/`MapSeed`/`MapPos`/`CurrentNode`/`NextNodes()`,
> `GenerateMap(seed)` (deterministic via `new Random(seed)`; START col0 / BOSS last /
> 2-3 mids, proportional edges + ~45% branch + a fix-up pass so every node is reachable),
> `CardForNode` (kind→MissionCard), `JumpTo(n)` (harness walk). `Run.Start` now generates
> the map + seats `MapPos=0`. `Game`: `ChooseNode`/`HandleNodeClick` replace the
> card-pick branch in the barracks `Update` (autopilot takes `NextNodes()[0]`);
> `StartMission` uses `JumpTo` for `SIGHTLINE_MISSION>1`; `DebugCampaignMap` +
> `SIGHTLINE_CAMPAIGN` screenshot hook. `Hud.DrawCampaignMap(run, region)` draws the DAG
> inside the barracks card (edges, current node ringed "you are here", reachable nodes
> glow + label their objective + cache rects in `Hud.NodeBtns`, hover tooltip). Persist:
> `SaveGame` stores only `MapSeed`+`MapPos`; `FromDto` regenerates the map from the seed
> (no big DTO). Legacy `Offers`/`ChooseCard`/`DrawDeployCard` kept as a fallback when the
> map is empty (`EnterBarracks` still calls `GenerateOffers`, harmless). Gotchas: (a)
> `EnterBarracks` finish/Win is gated on `_run.Mission >= MaxMissions` (independent of the
> map), so the boss node always resolves to Win when cleared; (b) BOSS node forces
> Eliminate so the WARLORD must actually fall; (c) `JumpTo` overwrites `CurrentCard` —
> set any explicit card AFTER it (see `SaveGame.SelfTest`). TODO/follow-ups: per-node
> biome (currently `Biome.For(mission)`); a visited-trail highlight after a CONTINUE
> (only the current node is re-marked visited on load); node-kind FX in-mission.

> **3.11 DEATH FEEDBACK.** KIA stamp + final-blow kill-cam shipped in
> `Game.KillUnit`. On a player (non-VIP) death: `Fx.Stamp` (new slow/low-rise text)
> draws `KIA  FullName`, `Game.DeathFlash` (new float, decayed in `Update` before the
> HitStop early-return, drawn under the HUD in `Game.Draw`) flashes red, and the name is
> pushed to `_missionKia` (new list, cleared in `SetupMission`) → inserted at the top of
> `_run.Report` in `EnterBarracks`. The mission-deciding death (`Game.IsMissionEndingKill`:
> last hostile on Eliminate / combatant wipe / lost VIP) adds 0.4s HitStop + a bigger
> zoom-punch/shake for a slow-mo finish. Screenshot hook `SIGHTLINE_KIA=1`. Gotcha:
> `IsMissionEndingKill` runs AFTER `d.Alive=false`, so the alive-counts already exclude
> the dying unit. NOTE: the wipe path still routes through `CheckEnd`→`LoseRun` (lose
> card), which does NOT list KIA names — only the survivable-mission debrief does.

> **3.5 STATUS EFFECTS (latest).** Transient combat statuses shipped. New on `Unit`:
> `Statuses` (`List<Status>`), `HasStatus`/`AddStatus`, magnitude consts
> (`BurnDamage=2`/`BleedDamage=1`/`DisorientAim=15`); `StatusKind`/`Status`/`StatusDef`
> in `Unit.cs`. Tick path: `Game.TickStatuses(u)` runs right after each `BeginTurn`
> (player: `StartPlayerTurn` + first-turn `SetupMission`; enemy: `EndPlayerTurn`) —
> applies Burning DoT + Stun (−1 action), decays every timer; Bleed ticks per step in
> `OnUnitEnteredTile`. All DoT routes through `Game.EnvDamage` (source-less damage + FX +
> kill/`MarkPlayerHurt`). Reads: `Combat.ComputeOdds` (Disoriented −aim), `DoOverwatch` +
> the AI overwatch branch both bail while Disoriented. Statuses cleared per mission in
> `SetupMission` (added to the feat-reset loop); never persisted. Renderer draws stacked
> BRN/BLD/STN/DAZ codes under each figure. **Live source so far = grenades** (survivors
> `AddStatus(Burning,2)` in `GrenadeAnim.Explode`). Verify: `SIGHTLINE_STATUSTEST=1` →
> `STATUSTEST: PASS`; `SIGHTLINE_STATUS=1` shot. **Next:** wire the remaining sources —
> 3.4 (incendiary/flash utility items → Burning/Disoriented) and 3.7 (status-inflicting
> enemies). The system + tooltips-on-unit are done; the shot tooltip does NOT yet flag
> a target's status.

> **3.2 SOLDIER IDENTITY (latest).** Nicknames + traits + bonds shipped. New data on
> `Unit`: `Nickname`/`FullName`, `Traits`+`HasTrait`, `Bonds`, plus transient per-mission
> feat flags (`FeatMultiKill`/`FeatClutch`/`FeatVengeful`/`WasNearDeath`/`AllyDown`/
> `KillsThisTurn`) and a per-frame `BondAura`. Feat detection lives in `Game.CreditKill`
> (now the single kill-credit path — replaced the inline `A.Kills++`/`Thrower.Kills++`
> in `ShotAnim`/`GrenadeAnim`) + `Game.MarkPlayerHurt`; `KillUnit` sets `AllyDown` on
> survivors. Resolution (feat→trait→nickname) + bond progression are in
> `Run.DebriefSurvivors` (`GrantTrait`/`AssignNickname`/`AdvanceBonds`, `Run.BondTally`+
> `BondThreshold=3`). Combat reads in `Combat.ComputeOdds`; magnitudes are consts on
> `Unit`. UI: `Hud.DrawDossier` (grown the perk-chooser dossier box 72→96 and pushed the
> perk cards down) + roster strip. Persisted in `SaveGame` (UnitDto nickname/traits/bonds
> + RunDto `BondTally`). Verify: `SIGHTLINE_TRAITTEST=1` (no window) and `SIGHTLINE_TRAITS=1`
> shot. Gotchas: feat flags reset in `SetupMission` AND cleared in `DebriefSurvivors`
> after resolving; `KillsThisTurn` resets in `Unit.BeginTurn`; bonds key pairs by name
> (`Run.BondKey`, ordinal-sorted) so recruit name reuse is the only collision risk
> (rare, benign). NOT done (follow-ups): trait/FEAT FX is minimal, no "+ BOND" tooltip
> flag, no pre-bond progress hint.

> **DISPLAY SETTINGS (latest).** `src/Display.cs` renders the fixed 1280x800 game to a
> letterboxed render-target scaled to the window (essential on 4K). Pause menu adds
> FULLSCREEN (key F) + a WINDOW size cycle; window is free-resizable; mouse mapped via
> `SetMouseOffset/Scale`; native size draws directly (keeps MSAA); settings persist to
> `display.json`. Off in the harness so screenshots are unchanged. Program wires it via
> `Display.Init/UpdateMouse/RenderFrame/Shutdown`.

> **PLAYTEST FIXES (latest).** (1) **Diagonal cover** — only the **point-blank
> (adjacent) diagonal** flanks (slips past the corner → ~100% at that range); a diagonal
> at range keeps **half** the cover bonus (`CoverInfo.Partial`, still partly obscured);
> a true corner (cover on both facing sides) keeps full cover. Verified by
> `SIGHTLINE_COMBATTEST` cases A-E. (2) **Action-button
> hover help** — hovering FIRE/GRENADE/abilities/etc. shows a tooltip explaining the
> action (`Hud.DrawActionHelp`/`ActionDesc`, `Unit.AbilityDesc`); `SIGHTLINE_HELP` hook
> parks the cursor on the ability button for screenshots.

> **DECISION-SCREEN INFO + DEATH VERIFICATION (latest).** Added soldier visibility
> where choices are made: the **shop** shows a squad HP strip + each item's concrete
> effect (`Game.ShopTarget/ShopEffect`, e.g. "VEGA: 4 -> 8 HP (+4)"); the **perk
> chooser** shows a full dossier (HP/AIM/MOB/weapon/grenades/ability + all current
> perks + derived strengths, `Hud.DrawDossier`); the **in-round roster strip** shows
> per-soldier strength tags (`Hud.Specialties`: OVERWATCH/SHARP/CLOSE/LONG/TOUGH/FAST
> + a class-role fallback), overridable by a player-authored **custom tag**
> (`Unit.CustomTag`, persisted; edit with key **T** in-mission or the **EDIT TAG**
> button in the perk chooser; modal `Game.EditingTag`/`UpdateTagEditor` +
> `Hud.DrawTagEditor`; custom = cyan, auto = amber, blank reverts to auto). Death
> mechanic was reported as "no consequence" but
> `SIGHTLINE_DEATHTEST` PROVES it's not a bug: dead soldiers are permanently lost and
> replaced by fresh rookies (losing rank/perks) — it only *felt* consequence-free
> because the squad always auto-refills to 4 (intentional anti-death-spiral) and the
> roster was hard to read. If harsher attrition is wanted, that's a deliberate design
> change to `Run.DebriefSurvivors` backfill — **now specced as ROADMAP item 3.1
> (Wounds & attrition)**.

> **PHASE 3 ROADMAP SPECCED (latest).** Concepts for the next big push are written up
> as **ROADMAP — PHASE 3** above (13 items, tiered): run-loop stakes (wounds, soldier
> identity, branching campaign map), tactical depth (utility items, status effects,
> destructible/2-tier terrain), content (new enemies, objectives, secondary goals), and
> feel/audio/accessibility (procedural music = the old item D, kill-cam, tutorial,
> brightness/colorblind). Each item names the hooks + a headless verification. Start
> with **3.1** (highest value, answers the death-consequence feedback). Nothing built
> yet — this was a planning pass.

> **LATEST SESSION SUMMARY (read this first).** **PHASE 3 IS NOW 100% COMPLETE (3.1-3.13),
> and Phase 2 is fully done too — the game is feature-complete against the entire spec.**
> This marathon session shipped **TWELVE features (PRs #29-#40, all merged to `main`):**
> **3.3** branching campaign map, **3.4** utility items (smoke/flash/barricade), **3.6a**
> destructible cover, **3.6b** 2nd elevation tier (commanding tier-2 sees over high cover),
> **3.7** new enemies (DRONE/SHIELD/mid-boss/SAPPER), **3.9** secondary objectives, **3.8**
> all three new objectives (SABOTAGE / RESCUE / DEFEND — objective rotation is now a 7-cycle,
> `Run.ObjectiveFor` %7), **3.10 / Phase 2 D** procedural music (BLIND SHIP — built + crash-
> safe but unheard here; tune once audible), **3.13** accessibility (brightness post-pass +
> colorblind palette), and **3.12** the onboarding tutorial. Build 0/0; SAVETEST/COMBATTEST/
> ITEMTEST/COVERTEST/STATUSTEST/TRAITTEST all PASS; autoplay clean.
> **No fixed roadmap remains — only open-ended polish** (see the "PHASE 3 IS COMPLETE" note
> above the WIP block): verify/tune the music audibly; contrast-gamma shader + UI text scale;
> enemy AI using utility items + the commanding-view LoS; themed-per-biome / `=`-tier-2
> authored arenas; mid-mission save granularity; a captive/VIP-EXTRACTED win flourish.
> Harness objective-force hook `SIGHTLINE_OBJ=sabotage|rescue|defend`; new shot hooks
> `SIGHTLINE_CB`/`SIGHTLINE_BRIGHT`/`SIGHTLINE_TUTORIAL`. Per-feature notes in WIP NOTES below.

Done: items 1 (audio), 2 (juice), 3 (campaign meta-loop), **4 (tactical depth —
grenades + pods + elevation)**, and **5 (map variety & objectives) is now COMPLETE**:
objectives cover Eliminate / Hack / Evac / **Escort (VIP)**, plus hand-authored map
layouts mixed in with the procedural generator (`src/Maps.cs` +
`Mission.TryApplyLayout`, connectivity-guarded). **Item 6 (Polish/UX) is also
COMPLETE** — threat preview, **camera zoom/pan**, a **keyboard tile cursor**, and a
**pause/settings menu** — and the enemy AI throws grenades (`Ai.BestGrenade`).
Items 1-7 are all done; **see "ROADMAP — PHASE 2" above for the next horizon.**
The game is feature-rich
and stable — autoplay across mission starts (`SIGHTLINE_MISSION`) resolves with no
exceptions and no TIMEOUTs. NOTE: the headless autopilot is a weak smoke-test AI
and LOSES most seeds (true on `main` too) — expected; the contract is "no
exceptions, no TIMEOUT", not a WIN/LOSE mix.

Also DONE (bonus item 7): **class signature abilities** — Assault Run&Gun, Ranger
Blitz, Sharpshooter Steady, Gunner Suppress (key 5, 1 charge/mission). Self-cast
only (no new mouse-targeting mode), so low-risk and headless-verifiable; the test
autopilot fires them on Eliminate missions. See item 7 for the wiring.

VIP escort (earlier): the VIP is just a Player-team `Unit` with `IsVip` added
to a per-mission COPY of the squad (`Game.SetupMission`), so all generic systems
(occupancy/targeting/overwatch/render/roster) work unchanged — the only special
cases are CheckEnd (win = VIP in evac, lose = VIP dead), `EnterBarracks` filtering
it out of the persistent squad, the AI target/advance bias, and gold rendering.
The 5th `PlayerSpawns` entry seats it. Escort reuses the Evac extraction zone.

Autopilot hardening added this session (test-only, in `Game.cs`): the Evac branch
now reloads/grenades a squatter instead of hunkering forever, and a turn-based
`AutoStallCheck` force-wakes a dormant pod if no progress is made for 10 player
turns — together these eliminate the rare deep-campaign TIMEOUT.

**Phase 2 progress:** A (perk promotions), B (enemy variety + elite boss),
C (deployment-choice cards), **E (run save/load)**, and F (biome palettes) are all
DONE and merged to `main`. **The ONLY remaining Phase 2 item is D. Procedural
music** — deferred because it CANNOT be verified in this sandbox (no audio device,
`InitAudioDevice` fails, so it'd be a blind ship); do it where you can actually
hear it, building on the PCM synth in `src/Audio.cs`. The previously-open
**intel currency + barracks requisition shop** (the unfinished half of C) is now
DONE (see item C). Smaller open follow-ups: more shop options (recruits/gear),
biome-tinted cover/plateaus + themed authored arenas (F), and a MEDIC enemy
archetype (B).

**Intel shop (this session).** `Run.Intel` (persisted in the save) accrues per
cleared mission in `Game.EnterBarracks`; the barracks now opens with a REQUISITION
spend screen (`Hud.DrawRequisition`, gated by `Game.ShopDone`; flow is shop →
promotions → deployment cards). `Game.CanBuy/DoPurchase/HandleShopClick/AutoShop`,
items in `Game.ShopName/ShopDesc/ShopCost`. Gotcha: `_shopDone` defaults true so the
first mission + the other barracks debug hooks skip the shop; it's set false only in
`EnterBarracks`.

**E (run save/load) — this session.** `src/SaveGame.cs` (System.Text.Json, compact
DTOs) persists the `Run` to `ApplicationData/Sightline/save.json` (NOT the repo).
Checkpoint = each mission start (`Game.SetupMission`); cleared on win/loss
(`EnterBarracks` / `Game.LoseRun`). Intro `CONTINUE RUN` button + key **C**
(`Hud.OverlayBtn2`, `Game.ContinueRun`) resumes the last-started mission from its
start (mid-mission progress is NOT saved — that's the intended granularity). All
disk I/O is gated by `Game.NoPersist` (true in the harness) so the smoke test is
unchanged. Gotcha: only persist *persistent* fields — ammo/grenades/ability/pos are
re-derived by `Mission.Build`, so don't add them to the DTOs.

Harness screenshot hooks (all `shot`-only, in `Program.cs`):
`SIGHTLINE_ZOOM`, `SIGHTLINE_PAUSE`, `SIGHTLINE_PERKSHOT`, `SIGHTLINE_CARDS`,
`SIGHTLINE_WAKE` (reveal dormant pods), **`SIGHTLINE_INTRO`** (intro with a save so
the CONTINUE button shows), **`SIGHTLINE_SHOP`** (barracks requisition screen),
**`SIGHTLINE_CAMPAIGN`** (branching campaign map mid-run), **`SIGHTLINE_ITEM`** (utility-
item smoke preview + a live cloud). Plus non-shot **`SIGHTLINE_SAVETEST=1`** → prints
`SAVETEST: PASS/FAIL` (save/load round-trip; no window), **`SIGHTLINE_ITEMTEST=1`** →
`ITEMTEST: PASS/FAIL` (smoke LoS / barricade / loadouts; no window),
**`SIGHTLINE_COVERTEST=1`** → `COVERTEST: PASS/FAIL` (destructible-cover degrade; no
window) + shot **`SIGHTLINE_COVER=1`** (cracked cover state) + shot **`SIGHTLINE_ELEV=1`**
(tier-2 plateau redoubt). Also non-shot
**`SIGHTLINE_WOUNDTEST=1`** (wound assign/decay/clear + aim/mob penalty), and shot
**`SIGHTLINE_WOUND=1`** (wounded roster/dossier).

**Shop FRAG CACHE option (this session).** 4th requisition item: a permanent +1
grenade/mission (`Unit.BonusGrenades`, read in `Mission.Build`, persisted in the
save, caps at +2). The requisition card now auto-sizes to `Game.ShopName.Length`, so
adding more items is just extending the `ShopName/Desc/Cost` arrays + a `DoPurchase`
case. Verified via `SIGHTLINE_SHOP` shot + `SIGHTLINE_SAVETEST` (round-trips it).

**Accurate run-over card (this session).** The lose screen now reads the real cause
via `Game.LoseTitle`/`LoseReason` (set in `LoseRun`): "RUN OVER / squad fell" on a
wipe vs "VIP LOST / asset was lost" on an escort failure (was always "squad fell").

**High ground sees over low cover (this session).** `Combat.ComputeOdds` now
negates a target's LOW cover when the attacker fires from high ground (high cover
still blocks); `ShotOdds.SeesOver` + tooltip "+ OVER LOW COVER". The AI benefits
automatically (it scores odds via `ComputeOdds`). Verified by
`SIGHTLINE_COMBATTEST=1` -> `COMBATTEST: PASS`.

**More arenas (this session).** `Maps.cs` grew from 2 to 5 hand-authored layouts
(PILLARS / CHEVRON / CITADEL added). Verified each applies (connectivity guard
passes) across Hack/Evac/Escort via a `SIGHTLINE_MAP=<index>` force hook
(`Mission.ForcedLayout`, env-gated in `Program.cs`). Keep authored maps' walkable
tiles to `.`/`^` only and leave the left/right spawn columns + center open.

**MEDIC enemy (this session).** New support archetype (ORDERLY, mission 3+) that
heals wounded allies. Wiring: `EnemyPlan.HealTarget`, `Ai.Plan` MEDIC branch +
`Ai.HealRange/HealAmount`, heal exec branch in `Game.UpdateEnemy`, `HealAnim`
(`src/Anim.cs`), green-cross glyph in `Renderer.DrawUnit`, spawn band in
`Mission.SpawnEnemies` (medics carry no grenades). Falls back to normal combat AI
when no ally is hurt, so it's never a dead turn.

- Gotchas: (a) elevation is a pure positioning layer — plateaus are walkable floor
  (no climb cost); per-tile draws that sit on a plateau go through
  `Renderer.ElevRect/ElevCenter` (else they render 8px low). (b) Hack: the terminal
  is walkable floor; `Mission.Build` keeps it + its 8-neighbour ring clear of cover.
  (c) Authored maps: walkable tiles are ONLY `.`/`^` (all cover blocks movement) —
  keep lanes open or the connectivity guard will reject the layout.
  (d) VIP escort: `Players` is a per-mission COPY of `_run.Squad` (NOT the same
  list) so the VIP can ride along without joining the squad — don't revert that to
  the old `Players = _run.Squad` alias or the VIP will persist/duplicate. The VIP
  is a real Player unit, so it counts in `AlivePlayers()` (filter `!IsVip` where a
  combatant-only view is needed, e.g. the squad counter / barracks roster).
  (e) Camera: board picking MUST go through `GetScreenToWorld2D(mouse,
  ViewCamera(false))` (done in `UpdateHoverAndAim`) so zoom/pan work; HUD hit-tests
  stay in raw screen space. `ViewCamera` defaults to identity (zoom 1 / pan 0), so
  the default mouse path and the headless harness are unchanged — keep it that way.

Conventions: drawn strings are ASCII for now (default font; a committed/generated font in Phase 5.3 lifts this). Build Release + run
`SIGHTLINE_AUTOPLAY=1` a few times before merging. Share screenshots in chat via
`SendUserFile` so the human can follow along.

### WIP NOTES — PROGRAM "ASCENDANT" (analytics-driven balance + visual identity + reward sink)

> **Fresh fully-autonomous session, run as orchestrator + parallel dev agents (isolated worktrees) +
> read-only research/audit agents. Develops on `claude/gifted-faraday-ftl23r` (PR #53). Full process log
> in `docs/DEVLOG.md`.** A 3-agent research pass converged on: *"an exceptionally well-engineered tactics
> skeleton with a thin skin and an invisible soul"* — master gaps: (1) balance UNMEASURABLE (the autopilot
> was a deliberately-dumb smoke test), (2) units are tokens that VANISH on death, (3) the meta has NO reward
> sink. Attacked in 4 waves, each verified headlessly (Release 0/0 + `SIGHTLINE_*TEST` + `SIGHTLINE_BALANCE`
> analytics + screenshots). All shipped to the branch.**
>
> **THE FLYWHEEL (Wave A) — the keystone:** `src/Stats.cs` + a competent autopilot (`Game.SmartStep`, gated
> by `Game.SmartPlay`) + a batch analytics harness **`SIGHTLINE_BALANCE=<N>`** (N headless campaigns ->
> win-rate by heat/objective/mission, turns, loss-causes, per-class lethality, threat ranking, JSON to the
> scratchpad). `SIGHTLINE_BALANCE_HEAT=<h>` pins a rung; `SIGHTLINE_BALANCE_DUMB=1` runs the smoke-test
> baseline. **Balance is now MEASURABLE — re-run a batch before/after any balance change to PROVE it.** The
> dumb `AutoStep` (`SIGHTLINE_AUTOPLAY`) stays the default path-coverage smoke test. New self-test
> `SIGHTLINE_AMBIENTTEST`; new screenshot hook `SIGHTLINE_UNITFX`.
>
> **Wave A.5 — data-driven balance pass** (the analytics redirected priorities away from the code-audit's
> speculative crit/perk worries): Hack/Sabotage no longer free stealth-wins (hack/plant "goes loud" — breaks
> concealment + rouses pods; terminal is a multi-turn hold, `HackRequired` 3->2); Escort VIP HP 6->14 +
> reduced anti-VIP AI bias; de-stacked the double-buffed WARLORD boss node; Heat ladder was too SHALLOW (not
> steep) -> now descends h0 69 / h4 67 / h8 32% with mutators pulled earlier; GUNNER 65->84% hit; dead perks
> HARDENED + COOLHEADED reworked. CleanSweep excluded on Decapitate+Defend. MORTAR no-op fix.
>
> **Wave B — visual identity leap:** 13 distinct per-class **silhouettes** (`Renderer.DrawSilhouette`,
> replacing the side-count glyph) + procedural **animation** (`Unit.RecoilAnim/FlinchAnim/WalkLean`) + a
> **death dissolve** (team-colored shatter + a lingering scorch decal `Game.Scorches`, replacing the instant
> vanish); an animated **title screen** + UI panel motion + cinematic counting-up **win/lose** cards
> (`Hud`); per-biome **ambient atmosphere** (`Fx.UpdateAmbient`/`DrawAmbient`: MAGMA embers / TUNDRA snow /
> ASH / NEON / ARID dust / VERDANT / VOID / STEEL), deterministic + bounded.
>
> **Wave C — meta reward sink (the attrition fix):** persistent per-soldier **weapon upgrades** bought with
> Intel, extending the requisition shop (`WeaponMod` {Scope/ExtendedMag/HollowPoint/Stabilizer}, APPEND-ONLY;
> `Unit.WeaponMods` persisted; shop auto-targets the soldier lacking the mod). + new enemy **SPOTTER (BEACON)**
> — a fragile back-line designator that paints the squad's priority target, amplifying focus-fire for all
> allies (kill it first) + 3 new arenas (Maps 18->21) + `Ai.Plan` focus-fire amplification. **Both Wave C dev
> agents finished their code (builds 0/0) but HUNG on a post-build verification bash command; the orchestrator
> recovered their work from the worktrees and verified it** (SAVETEST round-trips weapon-mods; AITEST/COMBATTEST
> PASS; no TIMEOUT). **Measured: the reward sink works — Evac 43->77% (squad power compounds on the survivability
> slog).**
>
> **REMAINING / NEXT (measured, for a future session):** full-run completion is still ~0% (a 6-mission ironman
> with a 4-soldier squad is inherently punishing) gated by **(a) Escort (m4, ~37%)** — gear upgrades soldiers'
> weapons, NOT the fragile VIP, so Escort needs a VIP-survivability or AI-bias tune; **(b) the m6 boss**; and
> the new SPOTTER + "loud" objectives added difficulty that offset some gear buff. The analytics harness is now
> the tool to tune these (force an objective via the rotation / `SIGHTLINE_OBJ`, measure, iterate). Other open
> ideas: verb-changing perks (toys, not just %), deliberate squad/loadout selection, the Wave-D strategy/
> readability items (combat log, campaign routing economy, anti-turtle clock), audio tuning on a real device.
>
> **PROCESS GOTCHAS (this session):** worktree agents sometimes branch off the near-empty default `main`
> (faf6b664) — every worktree dev's STEP 0 is `git reset --hard claude/gifted-faraday-ftl23r`. Worktree-agent
> commits RESET the shared `.git/config` `user.email` to the human's address — re-assert
> `git config --local user.email noreply@anthropic.com` before each integration commit. The env's signing key
> is an empty placeholder, so commits are correctly authored but not GitHub-verifiable. Integrate via FILE-COPY
> of the dev's (possibly UNCOMMITTED) worktree files + an orchestrator commit; strictly disjoint files per wave
> (one owner per hot file: Game.cs / Renderer.cs / Hud.cs). If an agent hangs on a verify command, its CODE is
> usually already on disk in its worktree — recover + verify it yourself rather than waiting.
