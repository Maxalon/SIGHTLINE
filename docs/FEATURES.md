# SIGHTLINE — Feature inventory (what's built)

> Extracted from `CLAUDE.md` to keep the continuity contract lean. This is the
> **reference of shipped features** — the game is feature-complete against the
> Phase 1–5 spec (see `docs/ROADMAP.md`). Process/history lives in
> `docs/DEVLOG.md`; rationale in `docs/DESIGN.md`.

## Current state — DONE ✅
Playable vertical slice, builds clean (0 warn/0 err), autoplay-verified across
seeds (mix of WIN/LOSE, no exceptions):
- **FOUR GAME MODES (PROGRAM HORIZON):** the intro now offers DEPLOY (the 6-mission campaign) / **LAST STAND**
  (endless horde survival, W2 — escalating full-roster waves on one arena, persistent BEST WAVE) / **SKIRMISH**
  (W4 — one fight with a chosen objective+heat) / **DAILY** (W4 — a deterministic date-seeded challenge with a
  local best). Modes share the tactical kernel; endless/skirmish/daily are single-session (no campaign wrapper).
- **CROSS-RUN META-PROGRESSION — WAR ROOM (HORIZON W3):** the game finally has LEGS beyond one sitting. A persistent
  profile (meta.json, append-only) banks SALVAGE currency, 7 ACHIEVEMENTS, a HALL OF FAME (fallen KIA + won-run
  legends), lifetime totals, and 3 additive UNLOCKS (StartIntel/StartBoon/StartArmor) bought with salvage — all
  strictly gated behind `!NoPersist` so the flywheel/harness stay byte-stable. `src/Meta.cs`, `src/Game.Meta.cs`.
- **CODEX / FIELD MANUAL (HORIZON W6):** a browsable in-game reference (bestiary + classes + perks/boons/contracts/
  specs/traits/scars/weapon-mods/status/objectives) from the intro (key K) + pause menu — closes the onboarding gap.
- **EXPOSED BY FIRE + honest flywheel (HORIZON W1):** a unit that fires and doesn't move is easier to hit next turn
  (the real "duck vs double-tap" bet); the balance bot now actually repositions after firing (the metric was an
  artifact before). **Visual identity leap (HORIZON W5):** units DOMINATE the board (bigger figures + silhouettes),
  cover recedes, biomes gained structural signatures, kills flood the bloom. **Audio drop-in ready (HORIZON W7):**
  csproj ships `assets/sfx|music`; drop in CC0 files to override the synth (see assets/*/CREDITS.txt).
- **SCARS & VENDETTAS (PROGRAM VANTAGE II / W5):** the COST side of soldier identity — surviving trauma leaves
  lasting marks (append-only `Scar` enum, persisted): SHELL-SHOCKED (-1 mob, immune to Disorient/Stun),
  BURN-SCARRED (+3 HP, -aim while burning), HARD-BITTEN (+crit bloodied, -aim at full HP), VENDETTA (+aim/+crit
  vs the faction that nearly killed you). Earned in `Run.DebriefSurvivors` from trauma flags; `SIGHTLINE_SCARTEST`.
- **RUN CONTRACTS (PROGRAM VANTAGE II / W6):** opt-in run-modifier rulesets chosen at the draft (default None =
  zero base-balance change) — IRON VETERANS (no recruit backfill, faster veterancy), HIGH STAKES (+50% Intel, no
  field-heal), SPEARHEAD (open unconcealed, turn-1 +1-action alpha). `Run.Contract` persisted; `SIGHTLINE_CONTRACT`.
- **FIELD CRAFT positioning verbs (PROGRAM VANTAGE W1):** two UNIVERSAL "play the geometry" verbs — **DRAG**
  (key 7, reach-2: pull a lagging ally one tile toward you — rescue/accelerate the corner-march) + **VAULT**
  (key 9: leap an adjacent cover tile to the far floor in one action — cross an otherwise-impassable cover
  screen). 1 action, never end the turn, once/turn; arrival routes through OnUnitEnteredTile. `SIGHTLINE_FIELDTEST`.
- **CLASS SPECIALIZATION FORKS (PROGRAM VANTAGE W2):** horizontal progression — a one-time pick-1-of-2 `Spec`
  at a soldier's first Corporal promotion that swaps/augments its signature verb or a core rule (10 forks, 2/class:
  Breacher/Juggernaut, Phantom/Pathfinder, Sentinel/Headhunter, AreaDenial/Anchor, FieldSurgeon/CombatMedic).
  Append-only `Unit.Spec`, persisted + SAVETEST-guarded; offered in the barracks after perks; `specPicks` telemetry.
- **GUARDED HVT — Decapitate teeth (PROGRAM VANTAGE W4):** the HVT takes reduced (never zero) damage while a
  designated bodyguard lives within 2 tiles, so the kill is a peel-then-execute positioning puzzle (telegraphed
  shield-dome aura + "HVT GUARDED" readout). `Game.UpdateHvtGuard`, `Combat.HvtGuardReduce`.
- **AUDIO + LIGHT polish (PROGRAM VANTAGE W3):** transient additive muzzle/impact lights (bloom haloes them),
  tracer/grenade arc trails, damage-number arc+punch, audio pan/pitch, + a sample-asset loader (assets/sfx,
  assets/music tried first; procedural synth fallback) so real CC0 audio can drop in with no call-site changes.
- **RENEWABLE ABILITY ECONOMY (PROGRAM VANGUARD W2):** class signature verbs (Mark/Grapple/Slipstream/Suppress/
  Heal) are no longer 1-charge-per-mission — they run on a per-unit COOLDOWN (`Unit.AbilityCd`, ticked at the
  unit's `BeginTurn`; Heal/Slipstream 3, Mark/Pin/Grapple 2), so using your verb is a recurring per-turn decision.
  Transient (not persisted); HUD shows `(N)` cooldown. `SIGHTLINE_CDTEST`.
- **SIEGE/BOMBARD artillery (VANGUARD W3):** a telegraphed disruptor enemy that demands a NON-SHOOT response —
  it charges a 3×3 strike, shows the danger zone for your whole next turn, then detonates cover-ignoring AoE unless
  you relocate / break LoS / kill it. m3+, 1/mission cap. (`Unit.ChargeTurns`, `Ai.BestSiege`, `Game.TickSiegeStrikes/
  DetonateSiege`, `Renderer.DrawSiegeZones`; `SIGHTLINE_SIEGETEST`/`SIGHTLINE_SIEGE`.)
- **FIELD EVENTS (VANGUARD W4):** roguelike "?" nodes on the campaign map — a situation + 2-3 trade-off choices that
  mutate persistent run state (10 events, every choice a trade-off/gamble), so runs branch and feel different.
  `NodeKind.Event` (append-only), `src/Events.cs`, `Hud.DrawEventScreen`; deterministic+save-safe; `SIGHTLINE_EVENTTEST`.
- **32 authored arenas (VANGUARD W5):** +CRUCIBLE/STEPWELL/COLONNADE/ENTRENCHED (indices 28-31), biome-themed,
  connectivity-guarded. `SIGHTLINE_MAP=<idx>` forces one.
- **BALANCE root-fixes (VANGUARD W1):** Sharpshooter aim de-domination, boss de-inversion (BERSERKER/BRUISER HP),
  Reflexes overwatch +110→+75, and revived dead choices (COMBAT STIMS/FRAG CACHE/SCAVENGER boon now real picks).
- **PER-TURN TEMPO (PROGRAM TEMPO W1):** firing no longer ends the turn — a shot is 1 action, so a soldier
  **moves-then-shoots OR shoots-then-repositions** (ducks to cover / breaks LoS — the new "where do I end up after
  firing?" bet); a 2nd shot the same turn is a rushed follow-up (SnapAim penalty), RUN&GUN is a free bonus shot,
  and a kill-refund re-enables firing for aggressive chains. The enemy AI mirrors it (a shooter caught exposed ducks
  to cover after firing). MEASURED: per-turn meaningful-choices 1.76 → 6.15 at neutral run-completion (~68%). SNAP
  retired. (`Game.IssueShoot`/`Unit.FiredThisTurn`/`Game.TryEnemyReposition`; `SIGHTLINE_SNAPTEST`.)
- **CLASS NICHES (TEMPO W2):** Sharpshooter sharpened long / punished point-blank (Sniper crit 20→14, dmg 8→7,
  harsher close RangeMod); Ranger owns close range (+4 aim, pairs with the Shotgun's close bonus); MARK is now an
  aim-only designator (squad-wide crit amp removed). Each class has a clearer niche (close/long/tanky-area/flex).
  (`Unit.RangeMod`/`Weapon.Make`/`Combat.MarkCrit`.)
- **MISSION-STATIC LIFECYCLE (TEMPO W4):** the 5 per-mission `Combat` statics (RunBoons/AllUnits/MissionFaction/
  PrepFaction/PressureAim) are owned by `Combat.BeginMission/EndMission/EndRun` (one set + one clear per lifecycle),
  replacing ~14 scattered defensive resets — stale-static bleed is now structurally impossible.
- **VISUAL IDENTITY LEAP (FRONTIER W1):** the board now reads by hierarchy — cover recedes (dark, low-emissive),
  live units carry a team-colored under-glow + larger figures, objectives (pulsing EVAC, amber charges/terminal)
  are 2nd-most-salient, dormant pods are clean dashed "?" rings, the floor is calmer + biome-distinct. Post-FX has
  real payoff (bright-pass bloom + board-framing vignette + per-biome grade). HUD polished (no empty LOG void,
  legible barracks roster, auto-fit action labels, one-accent top bar). (`Renderer`/`Display`/`Hud`.)
- **STRATEGIC ECONOMY (FRONTIER W2):** the requisition is a ROTATING ~5-item slate per barracks (always seats
  heal+armor; deterministic from MapSeed+Mission, not persisted) instead of a flat always-buy-armor list, and
  campaign ROUTING is now a resource decision — each node shows its **+N INTEL** reward (SUPPLY +10 economy stop /
  ELITE +14 risk-for-reward). MEASURED: fixed the dead economy + roughly doubled run-completion (15%→30%) and
  lifted the Evac/Sabotage/boss cliffs. (`Game.ShopOffer`/`Run.NodeIntel`.)
- **AUDIO OVERHAUL (FRONTIER W2, blind):** richer layered procedural weapon voices (ADSR + filtered noise +
  transients), meatier hit/crit, musically-resolving stingers, fuller ambient/combat beds — still device-free-safe
  (`SIGHTLINE_AUDIOTEST`). Owner tunes audibly on a real device.
- **PERK BUILD-DEPTH (FRONTIER W3):** the deadest false-choice perks are now distinct verbs — **MOMENTUM** (kill on
  your turn refunds +1 action), **PLATING** (ablative -2 dmg/hit while ≥half HP), **OUTRUNNER** (+1 mob + move
  immune to overwatch). (`Combat.KillRefundsAction/IgnoresOverwatch`; enum stayed append-only.)
- **Anti-turtle PRESSURE CLOCK (AGENCY W1):** on camp-friendly objectives (Elim/Hack/Decapitate) a graced
  clock escalates after turn 4 — enemy aim creep (`Combat.PressureAim`) + reinforcement waves — so turtling is
  strictly worse than advancing. `PRES` rung-pip meter in the top bar; `Game.PressureRungFor/UpdatePressure/
  SpawnReinforcements/PressureClockObjective`. `SIGHTLINE_PRESSURE`.
- **Visible randomness mitigation (AGENCY W1):** the shot tooltip surfaces the hidden graze floor + streak-
  breaker (`DMG GRAZE n / min-max`, `+N STEADYING`) via `ShotOdds.GrazeFloor/StreakBonus` — missing a high-%
  shot reads as less of a betrayal (math unchanged). `SIGHTLINE_TOOLTIP`.
- **Run-end payoff (AGENCY W1):** rich VICTORY/RUN OVER summary card (`Hud.DrawEndScreen`) — stat slabs +
  SURVIVING SQUAD (MVP) + KIA MEMORIAL (`Run.Memorial`/`FallenRec`, not persisted) + `Fx.VictoryBurst`
  flourish on the final win. `SIGHTLINE_SUMMARY`.
- **ARMORY + meaningful ATTRITION (AGENCY W2):** spend Intel at the requisition ARMORY sub-screen to re-arm a
  soldier from their class's thematic weapon pool (`Weapon.ArmoryOptions`, `Game.DoRearm`); chosen weapon
  persists on the `Run.Squad` unit. Recruits TRICKLE (1/barracks above a floor of 3 — `Run.RecruitsPerBarracks/
  AttritionFloor`) so a wipe genuinely shrinks strength for a mission or two without death-spiralling.
  `SIGHTLINE_ARMORY`.
- **VERB abilities — every class has a TOY (AGENCY W2+W4):** **Sharpshooter MARK** (squad focus-fire designator;
  `Unit.Marked`, `Combat.MarkAim/MarkCrit`), **Assault GRAPPLE** (yank a foe out of cover; reuses `ShoveAnim`),
  **Ranger SLIPSTREAM** (free, overwatch-immune long move; `Unit.Slipstreaming`), **Gunner SUPPRESSING FIRE**
  (AoE PIN — a foe + its neighbours can't aim/DASH next turn; `Unit.Pinned`/`ClearPins`), and **Corpsman PATCH**
  (heal adjacent ally). Append-only `AbilityKind`, save-safe; AI uses all via direct helpers (no TIMEOUT).
  `SIGHTLINE_MARK`/`SIGHTLINE_VERB2`.
- **FACTION-COUNTER PREP (AGENCY W3):** a barracks item (12 Intel) buys a one-mission counter to the upcoming
  faction (SYNDICATE→HARDENED OPTICS / LEGION→REACTIVE PLATING / WARDENS→FIELD SMOKE). `Run.PrepFaction`
  (persisted), `Combat.PrepFaction` static gated `==MissionFaction`. `SIGHTLINE_PREP`.
- **New enemies + arenas (AGENCY W3):** **LANCER** (phalanx — AI bunches into a wall, a grenade lure) +
  **HOUND** (swarmer — beelines the most-isolated soldier, spawns in pairs); pure `Ai.Plan` biases + distinct
  silhouettes. Arenas GARRISON/PINNACLE/REFINERY (Maps 27/28/29). `SIGHTLINE_CONTENT`.
- **2-column requisition grid (AGENCY W3):** the grown shop list (~10 items) now lays out as a clean 2-column
  grid in `Hud.DrawRequisition`.
- **Environmental hazards (RESONANCE W2):** explosive **barrels** on the battlefield —
  shoot one (aim over it), catch it in a grenade, or let fire reach it and it detonates
  for cover-ignoring AoE + cover demolition, chain-reacts neighbouring barrels, and leaves
  a **fire** field that denies ground + ignites anyone who steps in (`Grid.Barrel/Fire`,
  `Game.DetonateBarrel/TickHazards`, `BarrelShotAnim`, `Renderer` drum+flame, `Ai` avoidance,
  connectivity-guarded `Mission.PlaceBarrels` + `'B'` arena legend). `SIGHTLINE_HAZARDTEST`.
- **Weapon-distinct audio + stingers (RESONANCE W1):** each WeaponKind has its own firing
  voice (`Audio.PlayWeapon`); kill/lastkill/victory/lose/squadwipe `PlayStinger`s; heavier
  crit thud; device-free `Audio.SelfTest`/`SIGHTLINE_AUDIOTEST`.
- **EXTRACT lift-out verb (RESONANCE W3):** a soldier in the evac zone hauls an adjacent
  ally / VIP / freed captive aboard (key **X**, 1 action) — cuts the Evac/Escort/Rescue drag.
- **Economy/balance (RESONANCE W1):** intel decoupled from survivor count (anti-death-spiral);
  cheaper front-loaded armor; crit-perk diminishing returns (`Combat.DampedCritStack`); heat
  aim-clamp 82->88; Sabotage relief. Measured run-completion 40% -> ~47-53%, monotonic heat ladder.
- **Full-bleed UI (Phase 4.1):** the board fills the frame (Tile 64, ~79% of the
  window, dead margins reclaimed) with the HUD floating over it — screen-anchored
  bottom bar, gradient scrims, and drop-shadowed roster/unit-card panels.
- **Encounter geometry (Phase 4.2):** sight range 9 (was 12), a staggered mid-field
  high-cover screen that breaks cross-board sightlines (procedural maps) with one open
  "risky" lane, and the pod reveal-scatter capped to a single move — so first contact
  is a deliberate approach, not a turn-1 ambush. Connectivity-guarded.
- **Alert / awareness tiers (Phase 4.3):** pods escalate Unaware ("?") -> Suspicious
  (amber "!" + pulsing ring, "CONTACT?") -> Alert (red, live foe) instead of waking
  instantly. Being spotted at range only makes a pod *suspicious* (it doesn't act/scatter);
  it confirms to Alert at the player's turn end if still in sight (no free scatter — it had
  a warning) or loses interest if the squad broke contact. Blundering in close (≤4) or any
  aggression snaps it straight to Alert with the capped reaction scatter. `Unit.AlertLevel`
  (`Active => Alert==Alert`), `Game.CheckPodActivation`/`SetPodSuspicious`/`ResolveSuspicion`.
- **Concealment + ambush (Phase 4.4):** the squad opens every mission **concealed**
  (`Game.SquadConcealed`); it scouts/repositions freely and pods can't wake by sight. The
  player picks when to break stealth (first shot/grenade/flash/pin, or stepping within
  `RevealRange` 3 of an active foe) -> `Game.BreakConcealment` springs an **ambush** (the
  breaking shot gets +20 aim/+25 crit via `Unit.FiredFromConcealment`/`Combat.AmbushAim/Crit`,
  one shot only) and wakes sighted pods; then 4.3 tiers resume. CONCEALED HUD pill + friendly
  ghost rings; "+ AMBUSH" tooltip. (`SIGHTLINE_CONCEAL`/`SIGHTLINE_CONCEALTEST`.)
- **Post-processing shader (Phase 5.2):** an embedded-GLSL post pass over the final frame
  (`Display`): soft vignette, event-reactive **bloom** (spikes on hits/kills via
  `Game.AddBloom`, decays), subtle per-biome color grade, impact chroma. Headless harness
  keeps Display OFF (byte-stable shots); `SIGHTLINE_POSTFX=1` forces it on to verify.
- **9 authored arenas** (`src/Maps.cs`): the original 6 + CROSSROADS / FOXHOLES / RIDGE
  (RIDGE is the first to use the tier-2 `=` legend). `SIGHTLINE_MAP=<index>` forces one.
- **Real font (Phase 5.3):** `assets/NotoMono-Regular.ttf` (OFL-1.1) baked via `LoadFontEx`
  (`Cfg.Font`, default-font fallback); all HUD/board text is `DrawTextEx` now. **ASCII-only is
  lifted** — non-ASCII glyphs (em-dash, curly quotes, etc.) are baked and usable.
- **Graze / partial-hit (S2-A):** a shot that misses by <=15 GRAZES (hit for min weapon
  damage, no crit) instead of a clean miss; every hit deals >=1 (`Combat.GrazeBand`,
  `ShotResult.Graze`, lighter "GRAZE" FX). Softens the output-randomness tail (DESIGN.md 3B).
- **Enemy AI utility items (S2-B):** SNIPER/SCOUT throw SMOKE to blind a player overwatch
  lane; BERSERKER throws FLASH to disorient a cluster; late GRUNTs get smoke. `EnemyPlan.UseItem`
  + `Ai.BestSmoke/BestFlash` + a `Game.UpdateEnemy` item branch + `Unit.EnemyItem`. Never
  splashes allies (incl. the thrower for flash), always spends the action (no TIMEOUT).
- **Streak-breaker (S4-C):** after consecutive clean misses a soldier's next shot gets a
  hidden +6/miss aim bonus (cap +12), reset on any connect (hit/graze). Composes with graze,
  hidden from the tooltip. (`Unit.ConsecutiveMisses`, in `Combat.Resolve`.)
- **Focal-point lighting (Phase 5.6):** selected unit full-bright + glow, others gently dimmed,
  signal kept full-alpha (`Renderer.DrawUnit` figure-alpha).
- **Bench / deploy short-handed (S3-A):** at the barracks a WOUNDED soldier can be benched to
  sit out the next mission (deploy 3-strong) for faster recovery (Wound -2 + full heal).
  `Unit.Benched` (persisted), `Game.ToggleBench` (wounded-only, never <1 deployable, never by
  autopilot), `SetupMission` excludes + auto-clears. Attrition now actually shrinks strength.
- **Staggered deployment (owner feedback):** spawns are no longer two parallel firing lines —
  soldiers deploy as a loose diagonal wedge (cols 0-3, `Mission.PlayerSpawns`), enemies scatter
  3-4 columns deep (per-pod `EnemyPodColOffset`, cols 14-17). Standoff preserved.
- **Auto-focus camera (owner feedback):** an opt-in pause-menu AUTO-CAM (`Display.AutoCam`,
  default OFF) smoothly zooms+pans to follow the selected/acting unit (clamped to board);
  manual pan/zoom hands control back; forced off in the harness. (Larger maps deliberately
  deferred — see DEVLOG; the formation fix targets the actual "one line" cause.)
- **Cover shape-cues (S4-B):** high cover draws a small △, low cover a — on its top face
  (subtle white, alpha ~0.19), so cover type reads by shape (colorblind-safe), not color/height.
- **Campaign-map intel hints (S4-A):** each reachable node shows a short enemy-composition
  hint (`Run.EnemyHint`: e.g. "SNIPER + DRONE", "BOSS: WARLORD", "LIGHT FORCE") + objective,
  so the branch pick is an informed choice. Deterministic per node; autopilot unaffected.
- **Procedural texturing (5.4):** biome-tinted Perlin-noise grain on floor/cover/plateaus
  (`Renderer.DrawNoiseRect` over a 128x128 `GenImagePerlinNoise` tile, lazy-init, board-origin
  anchored so there are no seams) + soft-glow particles (`Fx`). Subtle; squint test holds.
- **11 authored arenas + biome affinity:** + RUINS / THICKET; `Mission.PickLayout` softly
  prefers a biome-themed arena (50%) while keeping variety. `SIGHTLINE_MAP` still forces one.
- **AI exploits high ground (Sprint 6):** `Ai.Plan` scores `Combat.ComputeOdds` from candidate
  tiles vs the current standing spot (`OddsFrom`), so enemies seek elevation/commanding-view LoS
  when it meaningfully improves the shot — capped as a bias, archetype-tuned (SNIPER/ELITE love
  it, BERSERKER/SAPPER barely; DRONE ignores it).
- **HUD action-bar icons (Sprint 6):** each action button has a small primitive-drawn glyph
  (bullet/grenade/eye/shield/refresh/circuitry/star/canister) left of its label; icons inherit
  the button text color so disabled/selected/colorblind states all work (`Hud.DrawActionIcon`).
- **Objective + status glyphs (5.1/5.5 — shape redundancy):** a semantic icon left of the
  top-bar objective text (`Hud.DrawObjectiveIcon`: crosshair=ELIM, brackets=HACK, up-arrow=EVAC,
  diamond=ESCORT, spark=SABOTAGE, cage=RESCUE, shield=DEFEND) and a shape beside each on-unit
  status code (`Renderer.DrawStatusGlyph`: flame=burn, droplet=bleed, star=stun, swirl=dazed),
  so coded state reads by shape, not hue alone (verified in default + colorblind palettes). The
  §3.H style-guide doc locks the rule. Both inherit the role colour; primitive-drawn (no assets).
- Grid battlefield w/ high+low cover, LoS, 8-dir pathfinding (corner-cut safe).
- 2-action combat: move, dash (yellow), fire (ends turn), overwatch reaction
  fire (both sides), hunker, reload.
- Cover + flanking + %-to-hit with hover tooltip (hit/crit/dmg, FLANKED warning).
- 4 player classes (Assault/Ranger/Sharpshooter/Gunner) + 5 hostiles incl. a
  scout (SMG) and bruiser (LMG); distinct weapons.
- Enemy AI: seeks cover + line of fire, advances when blind, flanks, finishes.
- Juice: move/shot anims, muzzle+tracer, particles, floating text, shake,
  selection ring, cover shields, turn banner.
- **Procedural audio** (src/Audio.cs) for all actions; mute = M. Plus a **procedural
  music** layer — a looping ambient bed + a combat layer that crossfades by intensity
  (enemy turn / live hostiles). Blind-shipped (no audio device in the sandbox).
- **Game-feel pass:** hit-stop on impacts/kills, camera zoom-punch on kills,
  weapon recoil + target knockback.
- **Campaign meta-loop:** 6 escalating missions, one persistent squad, kills→
  promotions, between-mission barracks debrief + field-heal.
- **Branching campaign map:** the barracks shows a Slay-the-Spire-style node path
  (`Run.Map` of `MissionNode`, generated from `Run.MapSeed`); each node is a mission
  (Combat / Elite / Supply / Boss) with its own objective + risk/reward, and the player
  picks the next reachable node (`Hud.DrawCampaignMap`/`Game.ChooseNode`). Position
  persists (seed+index). Legacy RECON/STANDARD/ONSLAUGHT deploy cards remain a fallback.
- **Grenades:** AoE that ignores cover, hits both teams, destroys low cover
  (key 4, 1 charge/mission) with range/blast/arc preview. The enemy AI also
  frags clustered/covered soldiers (from mission 2; never hits its own).
- **Activation pods:** enemies dormant (dimmed, "?") until a soldier sights them,
  then the pod wakes + scatters to cover ("CONTACT!"). Scouting carries risk.
- **Run save/load:** the campaign is checkpointed to the OS user-data dir at each
  mission start (`src/SaveGame.cs`); the intro offers **CONTINUE RUN** (key C) to
  resume. The save is cleared when a run ends.
- **Intel currency + requisition shop:** each cleared mission grants `Intel`
  (`Run.Intel`, persisted); the barracks opens with a REQUISITION screen to spend it
  on a medkit, +2 max HP, or a bonus perk before choosing the next deployment.
- **Enemy variety:** Grunt / Scout / Bruiser / Medic plus **Sniper** (kites to range),
  **Turret** (immobile overwatch nest), **Berserker** (tanky shotgun rusher), **Drone**
  (WASP — hovers, ignores cover/elevation, beelines), **Shield** (AEGIS — full frontal
  cover that re-faces the nearest soldier each turn, must be flanked or hit from above),
  **Sapper** (BREACH — demolishes the squad's
  cover), a recurring **mid-boss** (BREAKER m3 / WARDEN m5), and a capstone **Elite boss**
  (WARLORD) on the final mission with 2
  grenades + a one-time low-HP RAGE. Distinct AI temperaments in `Ai.Plan`; distinct glyphs.
- **Secondary objectives:** an optional per-mission bonus goal (NO LOSSES / SWIFT ≤7
  turns / CLEAN SWEEP) worth +12 intel, shown live in the HUD and reported at the
  barracks (`SecondaryKind`, `Game.RollSecondary`/`SecondaryAchieved`). (Phase 3 item 3.9.)
- **Sabotage objective:** plant demolition charges on all 3 sites (`Game.SabotageSites`,
  PLANT action) to win; part of the objective rotation. (Phase 3 item 3.8.)
- **Rescue objective:** free a caged, invulnerable CAPTIVE mid-map (reach it with a
  soldier), then escort the freed asset to extraction; losing it after freeing fails the
  mission (`Game.CaptiveLocked`/`TryFreeCaptive`). (Phase 3 item 3.8.)
- **Defend objective:** hold out for 8 player turns while reinforcement waves spawn at
  the right edge each enemy turn (`Game.DefendTurns`/`SpawnDefendWave`). (Phase 3 item 3.8.)
- **Mission objectives:** Eliminate, Hack (reach the TERMINAL and hack it down,
  HACK action / key H), Evac (get the whole squad to the extraction zone), and
  **Escort** (walk a fragile gold VIP to the extraction zone alive — losing the
  VIP is a loss; the enemy AI prioritises it). Rotation is Elim / Hack / Evac /
  Escort per 4-mission cycle; shown in the HUD.
- **Map variety:** procedural scatter OR a hand-authored arena (`src/Maps.cs`,
  ~55% of missions) chosen with a connectivity guard so spawns/evac/terminal are
  always reachable. Plus **per-mission biomes** (`Biome`: STEEL/ARID/TUNDRA/VERDANT/
  ASH/VOID) that retint the floor/grid so each mission reads as a distinct place.
- **Elevation / high ground:** raised plateaus (`Grid.Height`) grant +15 aim /
  +10 crit firing down on lower targets AND **see over LOW cover** (negate the
  target's low cover; high cover still blocks); faux-3D platforms, height-aware
  overlays, AI seizes the high ground. Tooltip shows "+ HIGH GROUND" / "+ OVER LOW
  COVER".
- **UX:** squad roster strip, end-turn confirmation, mute indicator, threat
  preview (red pips on exposed reachable tiles while positioning), **camera
  zoom/pan** (wheel + middle-drag, C to reset), a **keyboard tile cursor**
  (arrows/WASD + Space), and a **pause/settings menu** (Esc: display, audio, screen
  shake, threat-preview toggles, abandon run).
- **Accessibility** (`src/Display.cs` + `Pal`): a **brightness** post-pass (70–130%,
  `Display.DrawBrightness`) + a **colorblind palette** toggle (`Pal.SetColorblind`, Foe→
  orange / Good→teal), both in the pause menu + persisted. (Phase 3 item 3.13.)
- **Display settings** (`src/Display.cs`): the fixed 1280x800 game is rendered to a
  letterboxed render-target scaled to the window, so it stays readable on big/4K
  screens. Pause menu offers **FULLSCREEN** (key **F**) + a **WINDOW** size cycle
  (1280x800 → 3200x2000); the window is also free-resizable. Mouse is mapped back to
  virtual space via `SetMouseOffset/Scale`. At native 1280x800 windowed it draws
  directly (keeps MSAA). Settings persist to `display.json` in the user-data dir.
  Disabled in the headless harness (`Display.Init(!(shot||autoplay))`) so screenshots
  stay byte-identical.
- **Wounds & attrition:** survivors that end a mission badly hurt carry a temporary
  `Unit.Wound` (−12 aim / −1 mobility) that decays over missions; a FIELD MEDKIT cures
  it. Shown as red "WOUNDED (n)" in the roster + dossier. (Phase 3 item 3.1 core.)
- **Soldier identity (nicknames / traits / bonds):** veterans earn FEATS in combat —
  a multi-kill turn, a clutch kill while bloodied, avenging a fallen squadmate, or
  surviving near death — which resolve at the barracks into permanent **traits**
  (KILLER INSTINCT / COLD BLOOD / VENGEFUL / IRON WILL; `Unit.Traits`, read in
  `Combat.ComputeOdds`) plus a one-time **nickname** (`Unit.FullName` → `NAME "NICK"`).
  Two soldiers who survive 3 missions together forge a **bond** (`Unit.Bonds`,
  +10 aim while adjacent). Dossier + roster surface all of it; persisted in the save.
  (Phase 3 item 3.2.)
- **Destructible cover:** cover tiles have HP (`Grid.CoverHp`); grenades crack
  High→Low→gone in the blast and LMG/shotgun fire chews a hit target's frontal cover
  (`Game.TryChipCover`), with a cracked renderer state. (Phase 3 item 3.6a.)
- **2nd elevation tier:** `Grid.Height` goes to level 2; high ground is relative, and a
  **commanding 2-tier advantage** sees over the target's HIGH cover (negates it + targets
  through it via the `HasLineOfSight(...,overHighCover)` overload). Taller faux-3D render;
  `=` map legend; a procedural tier-2 redoubt on missions 4+. (Phase 3 item 3.6b.)
- **Utility items:** a second throwable slot beyond grenades (`Unit.Item`, 1 charge/
  mission, by class; key **6**): **SMOKE** (a 3x3 `Grid.Smoke` cloud that blocks LoS +
  overwatch for 3 turns, `SmokeAnim`/`Renderer.DrawSmoke`), **FLASH** (`FlashAnim` AoE
  that Disorients both teams + breaks overwatch), **BARRICADE** (drop a LowCover tile).
  Ranger/Sharpshooter carry smoke, Assault a flash, Gunner a barricade.
- **Status effects:** transient per-mission `Unit.Statuses` — **Burning** (DoT at turn
  start), **Bleed** (DoT per step), **Stun** (lose an action), **Disoriented** (−aim, no
  overwatch). Ticked in `Game.TickStatuses` (DoT via `Game.EnvDamage`), read in
  `Combat.ComputeOdds`/`DoOverwatch`; on-unit BRN/BLD/STN/DAZ codes. Grenade survivors
  catch fire (live source); the rest get their sources in 3.4/3.7. (Phase 3 item 3.5.)
- **Death feedback:** a fallen soldier gets a prominent `KIA  NAME "NICK"` stamp
  (`Fx.Stamp`) + a red screen death-flash (`Game.DeathFlash`) and is listed at the top
  of the barracks debrief; the mission-deciding blow (last hostile / wipe / lost VIP,
  `Game.IsMissionEndingKill`) lingers in a slow-mo kill-cam (extra HitStop + zoom-punch).
  (Phase 3 item 3.11.)
- **Recruits:** the barracks backfills empty squad slots with fresh rookies
  (`Mission.MakeRecruit`, `Run.DebriefSurvivors`) so casualties don't death-spiral.
- **Perk-based promotions:** each rank-up is a pick-1-of-2 perk choice in the
  barracks (`Perk`/`Unit.Perks`/`PerkDef`, `Run.PendingPerks`, `Hud.DrawPerkChooser`).
  13 perks (LockOn/Hardened/Reflexes/Bandolier/CloseQuarters/Marksman/Deadeye/Tank/
  Sprinter/Adrenal + S7 **Executioner** +crit vs ½-HP / **Guardian** +overwatch aim /
  **CoolHeaded** divert incoming aim + TEMPO W5 **Skirmisher** (post-fire move ignores
  overwatch) / **Gunslinger** (full-aim double-tap)) make each soldier a build; autopilot
  auto-picks. New perks must be added to a `Run.ClassLine` to actually surface in offers.
- **Class signature abilities:** each class has one self-cast signature (key **5**,
  1 charge/mission, refilled like grenades): Assault **RUN&GUN** (next shot costs 1
  action instead of ending the turn), Ranger **BLITZ** (next move costs one action
  less), Sharpshooter **STEADY** (next shot +25 aim/+20 crit), Gunner **SUPPRESS**
  (pin the nearest foe: -30 aim + train overwatch on it). `Unit.AbilityKind`/
  `Unit.Ability`/`Game.DoAbility`/`Game.CanAbility`; HUD ability button + on-unit
  stance tags (R&G/BLZ/AIM, SUPP on pinned foes); tooltip shows "+ STEADY".
- Full HUD + intro/barracks/win/lose; per-mission generator (scaled by mission #).
- Text is **currently** ASCII-only (default Raylib font renders other glyphs as `?`); a
  small committed/generated font (ROADMAP Phase 5.3) lifts this — not a permanent rule.

---


## PROGRAM COUNTERPLAY additions

- **Cross-run VETERAN reserve.** Promoted survivors (Rank≥1) of a finished run retire into a persistent
  reserve (`meta.json`, append-only `UnitDto` list, dedup-by-name, capped 12 most-storied). A new run's DRAFT
  recalls up to 2 as gold "VETERAN" cards carrying their full rank/perks/traits/spec/scars/nickname; the rest
  are fresh recruits. WAR ROOM shows `VETERANS n/12`. `SaveGame.LoadVeterans/EnshrineVeterans`,
  `Run.GenerateDraftPool(veterans)`, `Game.AwardMetaRunEnd`. Save-safe + NoPersist-gated. `SIGHTLINE_VETTEST`.
- **Focused (cone) overwatch.** FOCUS (key F / action button) braces a 90° kill-lane toward the aimed tile:
  reacts only inside the cone but at +15 braced aim, vs the default WIDE watch. The cone gates both the
  reaction (`Game.OnUnitEnteredTile`) and the AI's `PlayerOverwatchTiles` via `Game.InOwCone`, so the enemy AI
  reads and can exploit the blind zone. Renderer draws the gold kill-lane. Additive (default overwatch
  unchanged). `SIGHTLINE_OWTEST` / `SIGHTLINE_FOCUSOW`.
- **Biome visual identity** pushed above the squint-test floor (per-biome floor/signature/ambient/grade), plus
  legible dormant pods (slate ring + ?/! glyph) and a punchier enemy-intent reticle/carets.
- **3 new authored arenas** — CAUSEWAY (tier-1 land-bridge chokepoint), REDANS (diagonal sawtooth gauntlet),
  DONJON (walled tier-2 keep with a gated ramp). Arena pool 32→35.
- **Game.cs sliced** into `Game.Autopilot.cs` (balance/smoke AI) + `Game.Harness.cs` (Debug/SelfTest hooks) —
  behaviour-neutral, 7648→4707 lines.

### COUNTERPLAY follow-up (content variety)
- **Perks (16→19):** VANTAGE (+crit on high ground), BREAKER (+crit vs suppressed/pinned), SIEGEBREAKER
  (+aim vs hunkered) — situational, orthogonal to the HP/cover crit axes.
- **Enemies (→19 archetypes):** STRIKER "WRAITH" (fast overwatch-discounting flanker; body-block or focus it)
  and SCREENER "HAZE" (back-line zoner smoking your firing lane; reposition or kill it first). Both in the Codex.

## PROGRAM UNDERTOW additions (interrupt economy + morale + de-drag + board depth)
- **BRACE — the interrupt half of the action economy (key B):** a reaction stance distinct from OVERWATCH/FOCUS.
  Instead of a lethal watch, a braced soldier holds a DISRUPTING reaction: on a hit it STAGGERS the mover — zeroes
  its remaining actions THIS turn (its post-move shot/grenade is denied) — for reduced, non-crit damage. Trade a
  kill you won't land for tempo — an earnable comeback lever. One reaction/soldier/turn; green "BRC" badge + bracket
  icon. (`Unit.OwBrace`, `ShotAnim.Stagger`, `Game.DoBrace`; `SIGHTLINE_STAGGERTEST`.)
- **Enemy pod MORALE / ROUT:** pods carry shared morale; killed down to ≤ half their spawn strength, the survivors
  BREAK and ROUT for ~2 turns — flee toward their own edge, drop overwatch, and shoot wild (−18 aim) — then rally.
  Focus-firing a pod down is a genuine comeback: the second kill panics the pod. Green "ROUT" tag + "POD ROUTED"
  banner. (`Unit.Routed`, `Game.BreakPodMorale`; `SIGHTLINE_MORALETEST`.)
- **Sequenced enemy coordination:** setup verbs (SAPPER breach / STRIKER + adjacent shove) act BEFORE the finishers,
  and the squad's shared focus is recomputed live per unit — so a shove/breach that exposes a soldier redirects the
  pod onto that opening the same turn (setup-then-collapse). Advisory-only, TIMEOUT-safe. (`Game.IsSetupUnit` + a
  per-unit `PlanEnemySquad` recompute; AITEST-covered.)
- **Forward EVAC beacon + ESCORT leash (de-drag):** DEPLOY BEACON (key G) plants a forward extraction 3×3 that's
  unioned with the fixed far-corner FALLBACK (always present → no soft-lock), so the squad fights to a defensible
  mid-field spot and extracts there instead of a 14-tile stroll (Evac ~10.9t → ~7.8t). The ESCORT VIP now auto-follows
  the squad's forward element (`LeashVip`) — no more hand-shuffling the asset. (`Game.DoBeacon`/`BeaconZone`;
  `SIGHTLINE_BEACONTEST`.)
- **Board-space depth (presentation):** a deterministic board key-light + restored cover legibility + contact-shadow
  AO so the arena reads as a lit, dimensional space instead of a flat checkerboard, units still dominant
  (colorblind-safe, byte-stable). (`Renderer` FloorLight/KeyLit/DrawCover/DrawEvac.)
- **Balance roots:** LockOn narrowed to a FLANK reward (was any-exposed, a superset that killed the situational
  perks); BALLISTIC PLATING de-throned from the autopilot's always-buy slot so requisition purchases spread (PLATING
  369→203 buys; dead perks Hardened/Tank 2/4 → 11/11). Plus a double-kill correctness fix that makes the
  class-lethality telemetry honest (`SIGHTLINE_DKTEST`).

## PROGRAM APEX — the top end becomes real
- **The ladder's top exists:** the heat 7-8 / IRON VETERANS zero-roster crash is fixed (a shattered command
  drafts emergency conscripts to the AttritionFloor — the rung still shrinks a surviving roster, never zeroes
  it), and heat 8 has its first measured completion (~25%, a wall not a flat). **NO QUARTER now adds +1 enemy
  weapon damage** (m3+, initial force). Heat rungs 6+ raise a data-driven **`Ai.Tier`** — at the apex the AI
  coordinates harder (tighter smoke discipline capped short of certainty, stronger focus/crossfire pull)
  instead of just aiming better; Tier 0 is byte-identical to the shipped constants. (`SIGHTLINE_HEATLADDERTEST`.)
- **Planner-resolver truthfulness:** the enemy planner now sees the commanding (2-tier) LoS the resolver
  already paid for — snipers/elites genuinely seek the authored `=` plateaus — and its crossfire prediction is
  pinned term-by-term to `Combat.InCrossfire`. Policy gap at h0 healed from an inverted −20 to +5. (AITEST
  legs proven to fail on reverted code.)
- **LAST STAND is a ladder:** waves 1-2 arrive at half count (opener grace); every 3rd cleared wave banks
  promotions mid-stand (perk pick-1-of-2 + spec forks via the barracks chooser, autoplay-resolvable), every
  5th offers a boon; past wave 20 the between-wave heal decays and a REAPER elite joins each wave, so deep
  stands statistically terminate. Depth median 3 → 5-6, p90 finite. (`SIGHTLINE_ENDLESSOFFER` shot hook.)
- **Escort de-dragged (12.9–15.8t → 5.8t at 96% win):** the VIP leash moves through real MoveStepAnims —
  enemy overwatch, fire and pod-wakes apply (two-pass fire avoidance; anchor = the most-forward soldier) —
  and Escort gains its own forward beacon behind a hard gate: planter in the far third AND a cold LZ (no
  living enemy within Chebyshev 3, dormant included). Evac's shipped half-line beacon is untouched.
- **Rescue is honest:** the caged captive is truly caged (no self-rescue walking), and an abandoned cage
  (whole squad down) is a clean loss or checkpoint redeploy instead of a soft-lock — in campaign AND
  skirmish/daily. (`SIGHTLINE_RESCUETEST`.) **Overwatch resource leak fixed:** stacked watchers no longer
  spend ammo/reactions on a mover already predicted dead by earlier queued hits.
- **Content reachability:** STRIKER/LANCER/HOUND/SCREENER joined the faction rosters (stats verbatim,
  tier-gated) — they were previously unreachable in the majority of the campaign; Defend waves draw from the
  full endless roster (BOMBARD demoted, TURRET re-rolled; pressure-clock waves stay cheap); 40 callsigns with
  dedup across squad + fallen + the veteran reserve, so bond/memorial/legend records can't merge. Backfill
  recruits arrive depth-scaled ((mission-1)/2 kills, promoted at draft) so casualty runs stop compounding.
- **Persistence armor:** save/meta writes are atomic (.tmp + rename) and an unreadable file is stashed to
  .bak instead of silently wiped by the next write — the veteran reserve/salvage/hall of fame no longer sit
  one torn write from a reset. SAVETEST covers the corruption round-trip.
- **The flywheel has eyes:** the default batch spans heats {0,2,4,6,8}; `SIGHTLINE_BALANCE_ENDLESS=N`
  measures wave-depth (mean/median/p90, mode-tagged out of campaign gap math); `SIGHTLINE_VETSIM=n` prices
  the veteran-recall floor; the report adds win-rate-by-boon/spec/contract and an ENEMY COMPOSITION table;
  the greedy perk picker measures value (biased-random A/B), not slot position.
- **Presentation honesty (W9/W10, owner-feedback-driven):** the HIT number is banded by confidence (red <40 /
  amber 40-69 / green >=70) in a neutral frame; a TRUE in-shader brightness/gamma pass replaces the washing
  white quad (pause GAMMA row, persisted); the action bar sizes buttons to their labels and wraps into an
  upward tray (ellipsis structurally impossible); odds modifiers are 13px label/value rows with right-aligned
  colored numbers; the top bar reads as three aligned zones with labeled PRESSURE pips; campaign-map labels
  can't overprint; roster chips fade when they'd hide a unit.
