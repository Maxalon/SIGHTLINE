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
  **W9 THE REPAIR:** a skirmish's/daily's heat dial is NUMERICALLY REAL — both enter through
  `SetupMission(1)`, so the mission-1 heat grace (a CAMPAIGN-opener protection) used to zero every extra body,
  stat and damage point the dial promises, leaving only the qualitative flags. The grace is now gated on
  `Mode != GameMode.Skirmish`; MODETEST pins that a skirmish's force answers the dial AND that the campaign's
  mission-1 grace is untouched.
- **CROSS-RUN META-PROGRESSION — WAR ROOM (HORIZON W3):** the game finally has LEGS beyond one sitting. A persistent
  profile (meta.json, append-only) banks SALVAGE currency, 7 ACHIEVEMENTS, a HALL OF FAME (fallen KIA + won-run
  legends), lifetime totals, and 3 additive UNLOCKS (StartIntel/StartBoon/StartArmor) bought with salvage — all
  strictly gated behind `!NoPersist` so the flywheel/harness stay byte-stable. `src/Meta.cs`, `src/Game.Meta.cs`.
- **ONBOARDING — TRAINING OP + STAGED VERBS + FIELD TIPS (PROGRAM RESONANCE T1):** three pieces, replacing a
  5-card strip that taught 3 of ~14 verbs. (A) **TRAINING OP** — a fixed, scripted, NON-PERSISTENT, restartable
  drill (`GameMode.Training`, intro button / key **N**, **[P]** restarts) on its own authored arena
  (`Maps.TrainingArena`, deliberately outside `Maps.Layouts` so the arena deck/daily are unmoved): two recruits,
  four dormant targets, and 8 well-ordered problems — MOVE, COVER, FLANK, FIRE, OVERWATCH, GRENADE, ABILITY,
  CLEAR — each solved by DOING it, each with a turn-budget fallback so no lesson can strand you. Writes nothing:
  no save.json, no meta.json, no veteran reserve, no salvage, no achievements (asserted, not assumed).
  (B) **STAGED VERBS** — during the drill and campaign mission 1 the action bar carries only what has been
  taught and grows as lessons land, with a permanent **SHOW ALL** escape (**[V]**, remembered per profile);
  staging is capped to those two places and never hides STABILIZE. (C) **JUST-IN-TIME FIELD TIPS** — 10 cards
  (BRACE / STABILIZE / RELOAD / GRENADE / HUNKER / SHOVE / VAULT / DRAG / FOCUS / ITEM), each fired once per
  profile the first time its precondition is actually true in play, priority-ordered so a bleeding-out ally
  outranks a nicety. Seen-flags persist as a bitmask in display.json (FUL-12's `BraceTipSeen` migrates into
  bit 0). The intro's six-bullet rules wall is now one line. Hook: `SIGHTLINE_TUTTEST`; screenshots via
  `SIGHTLINE_TRAINING` / `SIGHTLINE_TRAINLESSON` / `SIGHTLINE_SHOWALL` / `SIGHTLINE_TIP`.
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
- **RUN CONTRACTS (PROGRAM VANTAGE II / W6; +2 FULCRUM FUL-10):** opt-in run-modifier rulesets chosen at the draft
  (default None = zero base-balance change) — IRON VETERANS (no recruit backfill, faster veterancy), HIGH STAKES
  (+50% Intel, no field-heal), SPEARHEAD (open unconcealed, turn-1 +1-action alpha), MERCENARY CLAUSE (veteran
  recalls half price, survivors never enshrine), LIVING LEGENDS (double kill credit + Rank>=2 run-end pensions,
  but a KIA erases their reserve record). `Run.Contract` persisted (append-only, dual tail pins); `SIGHTLINE_CONTRACT`.
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
- **HVT statline, pinnable (CROSSCUT W8):** the buff `Game.DesignateHvt` puts on a rank-and-file HVT
  (`+6 + mission` HP, `+6` aim; an ELITE keeps its own stats) now lives on `Combat.HvtHpBonusBase` /
  `HvtHpBonusPerMission` / `HvtAimBonus` so a measured round can pin it — `SIGHTLINE_HVTBUFF`,
  `SIGHTLINE_HVTDEPTH`, `SIGHTLINE_HVTAIM`, all default-identical to the pre-W8 arithmetic.
  `SIGHTLINE_HVTTEST` pins the selection rule, the ELITE exemption, the magnitudes and the defaults.
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
- **FIELD EVENTS (VANGUARD W4; +7 FULCRUM FUL-10):** roguelike "?" nodes on the campaign map — a situation +
  2-3 trade-off choices that mutate persistent run state (17 events, every choice a trade-off/gamble), so runs
  branch and feel different. The FUL-10 seven cross salvage/scar/veteran/faction/heat via six new outcome kinds
  (GrantScar/CureScar/Salvage/GrantPrep/RankKills/ReleaseSoldier; seeded ChancePct arms ride GambleSucceeds —
  reload-stable); event salvage pends in `Run.PendingSalvageReward` and pays at run end (win or loss).
  `NodeKind.Event` (append-only), `src/Events.cs`, `Hud.DrawEventScreen`; deterministic+save-safe;
  `SIGHTLINE_EVENTTEST`; `SIGHTLINE_EVENTID=<id>` pins the staged screenshot event.
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
- **Visible randomness mitigation (AGENCY W1):** the shot tooltip surfaces the graze floor + streak-breaker
  (`DMG GRAZE n / min-max`, `+N STEADYING`) via `ShotOdds.GrazeFloor/StreakBonus` — missing a high-% shot reads
  as less of a betrayal. `SIGHTLINE_TOOLTIP`. **RESONANCE Q1 (D3):** the STEADYING bonus is now folded into
  `Combat.ComputeOdds` itself, so the headline HIT% *is* the roll's probability (it under-reported by up to 12
  points: displayed 66 / rolled 77.89% over 40k seeded rolls); the badge is now the explanation, not the
  disclosure of a hidden loader. Pinned by `SIGHTLINE_COMBATTEST` (`steadyNotInHit` / `steadyRollNotDisplayed`
  / `steadyLeakedToEnemy`). **Q1 (D4):** the `RUSHED 2ND SHOT` / `DOUBLE-TAP` badge is no longer gated on aim
  mode — both odds paths apply the −15 penalty, so both now explain it.
- **Run-end payoff (AGENCY W1):** rich VICTORY/RUN OVER summary card (`Hud.DrawEndScreen`) — stat slabs +
  SURVIVING SQUAD (MVP) + KIA MEMORIAL (`Run.Memorial`/`FallenRec`, not persisted) + `Fx.VictoryBurst`
  flourish on the final win. `SIGHTLINE_SUMMARY`.
- **ARMORY + meaningful ATTRITION (AGENCY W2):** spend Intel at the requisition ARMORY sub-screen to re-arm a
  soldier from their class's thematic weapon pool (`Weapon.ArmoryOptions`, `Game.DoRearm`); chosen weapon
  persists on the `Run.Squad` unit. Recruits TRICKLE (1/barracks above a floor of 3 — `Run.RecruitsPerBarracks/
  AttritionFloor`) so a wipe genuinely shrinks strength for a mission or two without death-spiralling.
  `SIGHTLINE_ARMORY`.
- **VERB abilities — every class has a TOY (AGENCY W2+W4):** **Sharpshooter MARK** (squad focus-fire designator;
  `Unit.Marked`, `Combat.MarkAim/MarkCrit`), **Assault GRAPPLE** (yank a foe out of cover; reuses `ShoveAnim` — an ADJACENT
  foe has nowhere to be pulled to, so the verb resolves as a SLAM: collision damage plus a broken stance, and
  W9 THE REPAIR stopped it damaging the GRAPPLER, which was 100% of a JUGGERNAUT's grapples;
  `SIGHTLINE_GRAPPLETEST` pins it),
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
  **FUL-6:** missions 3+ group the initial force into **pods of 3** (pure `Mission.PodPlan`
  greedy split — 9 -> {3,3,3}; m1-2 and the finale keep the classic pairs) that spawn as
  **visible clumps** (members anchor to the pod lead's row/column band), and **gunfire
  carries**: waking a pod puts the nearest dormant pod within 6 tiles on the telegraphed
  Suspicious track ("HEARD THE GUNS"), confirming to Alert one turn later even unseen —
  one link per wake, never chains, zero RNG. Codex LINKED ALERTS row; `SIGHTLINE_PODTEST`.
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
  VIP is a loss; the enemy AI prioritises it). Campaign objectives are dealt by a
  **hashed column plan** (FUL-9: every route gets >=1 Eliminate, >=1 Defend-or-
  Rescue, <=1 Escort; boss always Decapitate); the 8-objective rotation remains
  the SKIRMISH/offer fallback. Shown in the HUD.
- **Map variety:** procedural scatter OR a hand-authored arena (`src/Maps.cs`,
  ~80% of missions) dealt from a **per-run no-repeat deck** derived purely from
  the run's MapSeed (FUL-9: an arena never repeats within a run; the displayed
  biome's themed arena is pulled forward at reduced weight), with a connectivity
  guard so spawns/evac/terminal are always reachable. Plus **per-mission biomes**
  (`Biome`: STEEL/ARID/TUNDRA/VERDANT/ASH/VOID) that retint the floor/grid so
  each mission reads as a distinct place.
- **Elevation / high ground:** raised plateaus (`Grid.Height`) grant +15 aim /
  +10 crit firing down on lower targets AND **see over LOW cover** (negate the
  target's low cover; high cover still blocks); faux-3D platforms, height-aware
  overlays, AI seizes the high ground. Tooltip shows "+ HIGH GROUND" / "+ OVER LOW
  COVER".
- **Incoming-fire forecast** (RESONANCE T2; `Game.ComputeThreat` -> `ThreatCell[,]`,
  `Renderer.DrawThreat`, `Hud.DrawThreatCard`): for every reachable tile, how many live
  hostiles can shoot you there, the best enemy hit%, the expected post-armor damage,
  whether you would be **flanked**, and whether the tile is in a live **overwatch / BRACE**
  reaction lane. Derived from `Combat.ComputeOdds` with the mover placed on the candidate
  tile (so the read can never disagree with the shot that fires) and modelling the states
  moving clears (hunker drops, exposed-by-fire ends). Shown as a **danger meter** (1-3 bars
  = gun count, shape-redundant and colorblind-safe; intensity = best hit%; a foot-rule marks
  a flank), an **INCOMING FIRE hover card** with the detail, and a **move-path preview tinted
  by the worst danger the route crosses**. Signature-cached (rebuilds on change, ~0.3-1.1 ms,
  not per frame). Pause toggle is three-state: OFF / SIMPLE (the pre-T2 minimal tick) / FULL.
  Self-test: `SIGHTLINE_THREATTEST`; screenshots: `SIGHTLINE_THREATSHOT` (+ `SIGHTLINE_THREATPREF`).
- **UX:** squad roster strip, end-turn confirmation, mute indicator, the incoming-fire
  forecast above, **camera zoom/pan** (wheel + middle-drag, C to reset), a **keyboard tile
  cursor** (arrows/WASD + Space), and a **pause/settings menu** (Esc: display, audio, screen
  shake, threat-preview OFF/SIMPLE/FULL, abandon run).
- **Accessibility** (`src/Display.cs` + `Pal`): a **brightness** post-pass (70–130%,
  `Display.DrawBrightness`) + a **colorblind palette** toggle (`Pal.SetColorblind`, Foe→
  orange / Good→teal), both in the pause menu + persisted. (Phase 3 item 3.13.)
- **Display settings** (`src/Display.cs`): the fixed 1280x800 game is rendered to a
  letterboxed render-target scaled to the window, so it stays readable on big/4K
  screens. Pause menu offers **FULLSCREEN** (key **F11** — it was `F`, which the player
  turn also binds to FOCUS; see the keymap note in `Game.Update`) + a **WINDOW** size cycle
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
- **PIKEMAN "SARISSA" — the enemy-side BRACE (FUL-8):** a Wardens lane-holder (10% m2+ faction slot;
  ~3% default-cascade m3+ tail) that plants a braced focus cone over a movement lane and STAGGERS the
  first soldier through — halved, no-crit damage via the IDENTICAL team-symmetric reaction path, so it
  teaches the player's own BRACE by mirroring it. Foe-red cone wash + edge rays + chevron reuse the
  player's gold FOCUS vocabulary; the enemy threat wash is cone-truth-gated; the STAGGERED pop colors
  by victim team. Counters: kill it, stagger it back, FLASH/SHOVE it, rout its pod, smoke/LoS-break
  the lane, walk outside the 90° cone, or feed it ONE cheap step (one reaction/round). The autopilot
  prices live lanes at +18 TileExposure and routes around them. (`Ai.Plan` PIKEMAN branch,
  `Game.InEnemyBraceLane`; `SIGHTLINE_PIKETEST` / `SIGHTLINE_PIKESHOT`; codex row SARISSA.)
- **Enemy pod MORALE / ROUT:** pods carry shared morale; killed down to ≤ half their spawn strength, the survivors
  BREAK and ROUT for ~2 turns — flee toward their own edge, drop overwatch, and shoot wild (−18 aim) — then rally.
  Focus-firing a pod down is a genuine comeback: the second kill panics the pod. Green "ROUT" tag + "POD ROUTED"
  banner. (`Unit.Routed`, `Game.BreakPodMorale`; `SIGHTLINE_MORALETEST`.) **FUL-6:** pods of 3 (m3+) give the
  arc its full staging — kill 1 of 3 flags WAVERING, kill 2 breaks the survivor — and **LAST STAND waves join
  morale**: each wave's landed bodies split into sub-pods (ids 100+, `_podOrig`-sealed), so routs play mid-stand
  (the injected deep-wave ELITE stays morale-exempt); TERROR is live in endless boon offers again.
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
- **Move range as a BOUNDARY (RESONANCE V2):** the reachable/dash regions are drawn as a marching-squares
  outline (**solid** walk / **dashed** dash — stroke style, so the two never read as one another or as a
  plateau, in either palette), a per-tile corner-tick lattice, and a whisper of **white** inner lift (hue-
  preserving). It replaced a flat per-tile alpha-60/55 cyan+gold fill that covered 60-120 tiles for the whole
  player turn and collapsed all eight biomes into one cyan family. (`Renderer.DrawMoveOverlay`; QA hook
  `SIGHTLINE_NOMOVE=1`; measurement `scripts/board-metrics.py`.)
- **The board grade (RESONANCE V2):** one coordinated value pass — floor mean un-darkened and pulled harder
  toward the biome tint, key-light throw widened to x0.32 lit / x0.45 shadow, cover tops +16 / walls -8 with a
  raised rim, plateau tops +64 with a darker front wall and a stronger lit lip, and a per-biome AO vignette on
  the board rect (under terrain, so it never dims a unit). Board median luma 75 -> 67 with the p50->p95 span
  more than doubled; luma >180 stays reserved for units/objectives/FX. (`Renderer.DrawBoard`/`DrawElevation`/
  `DrawCover`/`DrawBoardVignette`.)
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

## PROGRAM SIGNAL additions (seams, signal, stakes, economy)

- **Mode-seam integrity:** end card offers MAIN MENU beside NEW RUN (`(overwrites save)` warning
  when a checkpoint exists); draft BACK; abandon is mode-aware (campaign keeps its checkpoint —
  no LossStreak, no salvage; skirmish/daily/endless end true); LAST STAND mid-stand boon picks
  republish to combat; the daily's env seed can't leak into campaign drafts.
- **Boss identity:** capability flags (`HasShieldArc`/`HasSiege`/`HasBanner` — getters mirror Cls)
  let bosses carry signature mechanics while staying ELITE. Faction mid-bosses: Legion BREAKER
  (two-beat ENRAGED→FRENZY + rush), Syndicate BULWARK (re-facing shield arc), Wardens WARDEN
  (siege telegraph, demote-cap-exempt). Three finale kits — Legion SIEGELORD, Syndicate SPYMASTER,
  Wardens WARLORD — chosen per run by an avalanche hash of MapSeed and surfaced via EnemyHint;
  the finale's extra body is heat-gated to heat 4+.
- **Morale, visible & contested:** amber WVR crack tag on pods one kill from routing (HELD BY
  BANNER when anchored); WARBRINGER banner (diamond ring, Chebyshev-4 aura: in-aura pods can't
  rout and rally twice as fast; 1/mission); CUSTODIAN walks to the terminal/blown charge and
  re-locks/re-arms one step per adjacent turn, telegraphed. Routed specialists all flee.
- **Salvage economy:** veteran recall costs 10+8×rank salvage, charged once and atomically at
  draft confirm; repeatable sinks (draft-pool re-roll 10, scar REHAB 30 with a true undo, shop
  slate re-roll 5) settle via a pending ledger beside the mission-start checkpoint (quit-safe);
  MetaUnlocks: CROSS-TRAINING (sidegrade recruit weapons), QUARTERMASTER (+1 requisition slot),
  STANDING RESERVE (3rd recall slot); heat multiplies the win bounty; daily wins pay 10+heat
  once per calendar stamp with a streak counter and two achievements.
- **Pools:** boons SHOCK DOCTRINE (braced interrupts deal full damage), TERROR (routs last +2
  turns), FIELD DRILLS (FUL-6 rework: a DRAG or VAULT *drills* the soldier — +1 tile of movement
  for the rest of that turn, and DRAG/VAULT ×2 per turn), PYROMANIACS (own fire +2 turns, squad
  burn-immune), FIELD STORES (utility items ×2 charges), RECLAIMER (focused-cone overwatch kills
  refund the reaction); weapon mods BIPOD (+10 aim if unmoved) and SUPPRESSOR (a suppressed shot
  wakes only the target's pod); secondaries GHOST / DEMOLITION / BOUNTY; the INTEL CACHE (an
  expiring gold diamond worth 8-10 intel, mid/far-field).
- **Teaching layer:** FIELD CRAFT codex tab (12 code-verified rule entries + 6 status rows);
  enemy ID + behavior blurb in the aim tooltip; NEW CONTACT banners on first sightings; honest
  loss cards (CAUSE OF DEATH + counterplay tip; FIELD SUPPORT disclosure); objective/boon hover
  cards; wrapped action-bar help; roster chips collapse to an edge rail instead of garbling;
  the action bar fades when it hides a unit.
- **Strategic-layer look:** the campaign map sizes to its panel (labeled reachable nodes +
  legend), class silhouettes stamp the draft/barracks/roster/promotion screens, the intro has a
  real button hierarchy with mode captions, WAR ROOM shows per-achievement progress bars and a
  NEXT UNLOCK preview, promotion cards show soldier-specific before→after deltas, and a first
  run pre-selects a RECOMMENDED squad+boon.
- **Board reads:** high ground inherits its biome's hue; the focused-overwatch cone is clearly
  visible (wash + rays + chevron); status codes are 13px pills drawn above all bodies;
  TURRET/BRUISER/SCOUT read by ring shape; the EVAC label anchors to a real zone tile.
- **The compass (dev-facing):** CRN-paired greedy/sloppy legs over identical worlds, positional
  error injection, ACTION MIX + BY PERK/PURCHASE/ARENA tables, whole-run objective pins,
  SIGHTLINE_PAIRTEST.

## PROGRAM FULCRUM — death gets a window (FUL-7 LAST LIGHT)
- **DOWN / bleed-out:** lethal damage on a non-VIP soldier opens a **3-turn DOWN window** instead of an
  instant kill — the whole state machine enters as one guard at the top of `Game.KillUnit` (the single
  lethal seam every damage path funnels through). While down: Hp 0 but ALIVE, prone 0.6x body + pulsing
  red ground ring + a red `DOWN 3/2/1` pill (amber `STABLE` once stabilized), HP bar hidden, red roster-
  chip state, never selectable, and never targeted by enemy **direct** fire (one filter at the top of
  `Ai.Plan`, mirrored by the aim helpers + the dragged-body overwatch guard) — but **AoE stays blind**:
  a shell/frag/barrel/fire field that catches the body kills it outright (the telegraphed-weapons
  honesty valve), and **nobody goes down twice in one mission** (`WasDownedThisMission`).
- **The rescue kit:** **STABILIZE** (universal verb, key E; adjacent, 1 action, doesn't end the turn)
  freezes the timer — the soldier stays down but stops dying; the corpsman's **PATCH revives** (PatchHeal
  HP, up-but-actionless that turn; CombatMedic reach-2 and FieldSurgeon triage ride along; same Cd 3);
  **DRAG/EXTRACT carry the body** (pinned against regression); a **WON field recovers** every downed
  survivor at Hp 1 / Wound 3 + the near-death scar track ("recovered from the field - gravely wounded");
  LAST STAND's wave-clear breather revives the downed at the mend value. A bleed-out runs the FULL death
  flow — Fallen + Memorial + KIA stamp, and the honest loss card names the DOWNING archetype.
- **Honest bounds + vocabulary:** timers tick on the squad's clock (StartPlayerTurn) and UNFREEZE when no
  soldier is left standing, so an all-downed board always resolves in <= 3 turns; the soldier true-death
  pop is renamed **KIA** (DOWN now means the window) and the combat log logs `DOWN`, not `KILL`, for a
  lethal blow the soldier survives. Transient end to end — nothing persists (DTO whitelist; SAVETEST leg).
  (`Game.EnterDowned/ExpireDowned/DoStabilize`; autopilot revive/stabilize arm + generalized
  `TryMoveToPatch` + the Evac no-corpsman freeze guard; `SIGHTLINE_DOWNTEST` (legs a-h) /
  `SIGHTLINE_DOWNSHOT` (=2 mid-rescue); codex row DOWN (BLEEDING OUT); down telemetry in the balance
  report: downs -> revived/recovered/bled-out/finished + honest save-rate + corpsman-fielded
  missions. Review round F1-F6: downed bodies exit EVERY enemy-attention seam — the squad focus
  pick, shield facing, reposition exposure — plus the honest ledger/banner/pill wording.)

## PROGRAM RESONANCE — WAVE C1 "VOICE" (the game's words)
- **Mission briefings:** a 3-line card at the start of every CAMPAIGN node, composed from data the
  game already had — the named **region** × the authored **arena's** terrain clause × the enemy
  **faction** and its real combat rule × the **objective** said in a commander's voice. Rides the
  shared FIELD TIP card chrome (Friend-blue accent) and yields the slot ABSOLUTELY to wave T1's
  lesson cards and field tips. Never hit-tested (cannot swallow a click); dismissed by any key or
  click, auto-fades after 11s, HOLDS its clock while a teaching card owns the slot (giving up after
  45s), and **clears itself the moment the combat log has an entry** — it is a pre-fight object and
  the ledger is load-bearing. Deterministic: a reloaded save briefs identically.
- **Faction dossiers + region names:** a **FACTIONS** codex tab (3rd, after ENEMIES) with a
  three-paragraph dossier per faction — who they are / **FIELD RULE** / **COUNTER** — each rule line
  interpolating the REAL `Combat` constant (`LegionCloseAim/Crit`, `WardenLongAim`, the Syndicate
  see-over-low rule) and each counter naming the real counter-prep item and capstone boss.
  `Faction.None` ("LOCAL FORCES", UNALIGNED) is documented too. The campaign map's six columns are
  now labelled with **named regions** — 64 curated, biome-true place names (8 per biome) derived
  from `MapSeed`; a run's six missions always land on six distinct biomes, so a region name can
  never repeat inside a run. Cleared/current columns read brighter than the ones ahead.
- **Soldier barks:** six beats only — first blood, a bonded squadmate going down, a pod routing, a
  clutch STABILIZE, a vendetta kill, last-soldier-standing — written into the combat log with the
  outcome tag `VOICE` (a cooler, quieter tint than every mechanical line). **Four hard rate limits:**
  never while a lesson/tip card is up, at most one per game turn, never the same speaker twice in a
  row, each beat kind at most once per mission. Ceiling six lines a mission. A beat needing a second
  name that is handed none never fires, so a bondless soldier can never draw a bond line.
- **Run epilogue:** exactly five lines under the dossier panels on the CAMPAIGN end card, generated
  from the numbers the card already computes — where the file closed and what fell there, the count
  of the dead, ONE death told properly (the costliest loss, named, with its region and kill count),
  what the run turned on (the archetype that did most of the killing, or the MVP), and where it
  leaves the survivors. Every slot has a non-empty fallback and every count is grammatical and true
  at every N. The dossier panels yield height to it, so a loss card with a cause line, a heat unlock
  and three achievements still lands its buttons on screen.
- **The determinism contract:** all of the above lives in `src/Voice.cs` and takes **ZERO draws from
  the shared `Util.Rng`** — regions/briefings/epilogue are pure `Util.Hash3` derivations of
  `MapSeed`; only bark variety uses a dedicated `Random` re-seeded per mission. `SIGHTLINE_VOICETEST`
  proves the separation (with a sensitivity probe so it cannot pass vacuously), asserts every
  template slot resolves, walks all four bark gates, and measures every generated string against the
  real pixel width of the chrome that draws it. `SIGHTLINE_BALANCE=10` is byte-identical to base.
  (`src/Voice.cs`; `SIGHTLINE_VOICETEST` / `SIGHTLINE_VOICEDUMP` (read the copy as prose) /
  `SIGHTLINE_SHOTONBARK` / `SIGHTLINE_CODEXTAB=2`;
  `docs/DESIGN.md` §1.1 records the pillar amendment that authorises any of it.)

## PROGRAM RESONANCE — WAVE W4 "THE SECOND AXIS" (the opening geometry becomes a variable)

- **Four deployment SHAPES**, dealt per mission from `(MapSeed, mission)` with zero extra RNG
  draws (`Mission.DeployFor`; `Mission.AppliedDeploy` is the telemetry stamp):
  - **FRONTAL** — the historical opening: squad cols 0-3, the whole force on the east edge.
  - **PINCER** — a front pair plus two flank pairs in the open rim lanes (cols 12-13,
    rows 0-1 / 9-10). Contact comes from three bearings; the fastest of the four openings.
  - **CROSSFIRE** — two dense masses on the NE and SE bearings with the middle rows empty.
  - **ENVELOP** — the **surrounded opening**: the squad deploys at board CENTRE (cols 7-10)
    and pods hold all four rims. Legal only on Eliminate, Decapitate and **Defend** (an
    objective-gate that keeps every extraction / hack / sabotage route untouched).
  Shipped mix 3/3/1/3; `SIGHTLINE_DEPLOYMIX=1,0,0,0` restores the pre-W4 all-FRONTAL board.
- **Pods field one kind of body** (`Mission.PodUniform`) — members past the pod lead reuse the
  lead's archetype roll. Three RAIDERS, not a trio of strangers; measured ladder-neutral.
- **Protective cover faces the nearest threat** on the dominant axis, whatever bearing the
  mission deployed on (reproduces the historical +1 / −1 column for a frontal opening), and
  **barrels never spawn within two tiles of a soldier's deployment tile**.
- **Decision-density instrumentation** — the `[choice-split]` report line and four JSON fields
  break `meaningful-choices` into `los-targets` / `target-choices` / `position-choices` per
  ARMED soldier-turn, and a `DEPLOYMENT GEOMETRY` block reports win / turns / density per
  opening shape. `SIGHTLINE_EXPOSURETEST` now enumerates shape x arena x objective.
- **Harness pins**: `SIGHTLINE_DEPLOY` (`frontal|pincer|crossfire|envelop`),
  `SIGHTLINE_DEPLOYMIX`, `SIGHTLINE_PODUNIFORM`, `SIGHTLINE_PODMASS`, `SIGHTLINE_RIMWAVES`,
  `SIGHTLINE_ESCORTFIX`.

## PROGRAM RESONANCE — WAVE X2 "TRUE NORTH II" (the cold opener stops ending runs)

- **THE COLD-OPENER GRACE** (`Mission.OpenerTrim`, shipped at **1**). `Game.SetupMission` has
  long ramped HEAT's escalation in over missions 1-2 ("the measured ~20% mission-1 loss, which
  hard-caps run completion"), but that grace is gated on `heat > 0`, so the BASE force met the
  coldest squad in the game with no ramp at all: **5 hostiles against 4 rookies** with no
  promotion, perk, mod or boon, each hostile carrying X1's +3 HP. The base force now gets the
  same ramp: **one body off mission 1, one off mission 2** (full trim on m1, half rounded up on
  m2, nothing from m3). `SIGHTLINE_OPENERTRIM=0` restores the pre-X2 opener exactly.
  Measured (n=40 campaigns, base `a61ef42`): mission 1 **75% → 100%** win, heat-0 run completion
  **35.0% → 57.5%**, shots-per-kill **3.22 → 3.30** (the two-hit trade is not clawed back).
- **Two more balance knobs, both default-OFF, for the next tuning wave**:
  `Mission.HostileAimTrim` (`SIGHTLINE_AIMTRIM`, flat points off every hostile's aim in the
  MakeHostile funnel — measured at **+7.5 completion per 5 points** at heat 0, with Eliminate's
  turn budget untouched) and `Mission.EnemyBaseCount` (`SIGHTLINE_ENEMYBASE`, the constant in
  `count = base + missionNum`). `Mission.HostileToughness` / `HostileDamageTrim` became static
  fields pinnable from `SIGHTLINE_TOUGH` / `SIGHTLINE_TRIM`, so one binary serves every round.

## PROGRAM RESONANCE — WAVE W4 "THE BOARD BECOMES A PLACE" (the board stops being a texture)

Rendering only; provably gameplay-inert (`PAIRTEST` byte-identical + a pinned-slot balance chunk
field-for-field identical to the branch point). Details + every number in `docs/DEVLOG.md`.

- **Cover is drawn as MERGED VOLUMES.** A union-find pass groups 4-connected tiles of the same
  `TileType` at the same elevation tier and drops the inset, the corner rounding and every edge
  decoration (cast shadow, contact AO, front-face gradient, top-edge highlight, structural rim,
  corner chip) on any side that is a group seam; the top face extends over the wall band on an
  interior south edge, so a north-south run reads as one slab with one wall face. Footprint
  jitter/lift/radius are per VOLUME, not per tile. 19.0 drawn boxes/map → 10.6 volumes/map.
- **A per-biome FORM vocabulary.** Each merged volume takes one of CRATE (per-tile cross-brace +
  strap — a strapped stack), WALL (masonry courses in running bond across the run), BOULDER (dome
  catch + hashed pits, no chip) or WRECK (diagonal shear, torn lip, a strut past the outer edge),
  hashed off the volume's root tile from a three-form table per biome. 4–6 object types per map.
  The root is a **stable identity** (`Grid.CoverSeed`), assigned when a cover tile first exists and
  never re-derived from the live tile set, so shooting part of a wall away cannot re-roll the
  material or the footprint of what is still standing. New cover adopts the identity of a volume it
  is touching, so a deployed barricade joins the wall it is built against.
- **The archetype form IS the token.** The 25 hand-designed silhouettes are drawn as the opaque
  high-value figure with their own dark outline; the enclosing ring is demoted to a 2px state
  indicator that keeps its full topology (closed-doubled friendly / broken hostile / square TURRET
  / hex BRUISER / dashed SCOUT / diamond banner-bearer). 0% → 87% of a token's brightest pixels
  now lie in the figure rather than the ring.
- **The squint value ladder is stated as numbers** (`Renderer.ToLuma`): selected soldier 202 > VIP
  196 > other soldier 190 > boss 184 > live hostile 172 > suspicious 162 > dormant 150, each ring
  one step below its own figure. Because the rungs are target luminances rather than mix fractions,
  the ordering is identical in the colourblind palette.
- **Dormant contacts read by outline, not brightness.** Body at roughly floor+20, a crisp cold
  rotating dashed ring, and the `?` glyph moved off the body into the same marker slot the
  SUSPICIOUS `!` uses, so the pod's chassis is no longer obscured by its own label. That slot is
  clamped to the board's top edge and drops into the pod's own tile (on a dark lozenge) when a unit
  stands on the tile above — the two cases where "above the token" is off screen or under someone
  else's feet.
- **The move overlay is one region and one contour.** The DASH region draws only on demand (hold
  SHIFT, or hover a tile outside walk range — the dash *action* is unchanged); the boundary is
  stitched by marching squares into closed loops stroked as single polylines with welded joints and
  a continuous dash phase; the dash stroke moved off `Pal.Accent`'s bytes (the reserved objective
  gold) onto `Pal.MoveDash`, a value variant of the friendly cyan.
- **`SIGHTLINE_BOARDTEST`** — the project's only self-test that measures rendered pixels: it draws
  real frames on a **pinned animation clock** (`Renderer.TimePin`) and asserts the value ladder in
  **both palettes** (mean and peak, ≥8 luma margin), the absence of a floor gutter inside a cover
  volume, that destroying one tile of a volume leaves a surviving tile byte-identical, the dash
  palette and the boundary's edges-per-stroke ratio. In `scripts/qa-sweep.sh`.
- **A/B dials:** `SIGHTLINE_COVERMERGE=0`, `SIGHTLINE_TOKENSTYLE=0`, `SIGHTLINE_MOVESTYLE=0`,
  `SIGHTLINE_MOVEDASH=1`, `SIGHTLINE_COVERSEED=0` (re-derive the volume identity every frame — the
  pre-review behaviour), `SIGHTLINE_COVER=1 [+SIGHTLINE_COVERKILL=1]` (the cover-destruction A/B),
  `SIGHTLINE_MARKERS=1` (the occluded awareness-marker cases).

## PROGRAM RESONANCE — WAVE W5 "THE FIRST HOUR AND THE FRONT DOOR"

Six things a first-time player meets and the bot never can. **Balance-inert:** `PAIRTEST`
byte-identical, and a pinned-slot `SIGHTLINE_BALANCE=5` JSON field-for-field identical to the
branch point.

- **The mission-1 briefing is a PRE-FIGHT BEAT.** On a first-ever campaign run the briefing card
  could not draw at all — `BriefAllowed` requires `TutorialText == null`, the lesson strip armed
  on the same frame the card composed, and `UpdateBriefing` destroys the card the instant
  `Stats.CombatLog` fills. Measured at **0.00 s of 11 s**. The strip now arms PENDING
  (`Game.TutPending`) and opens the frame the card retires; verb staging counts the pending state
  so the action bar does not flicker whole-then-staged. `TutStepFire` gained the turn-count
  patience fallback (9) its three siblings already had.
  (`SIGHTLINE_BRIEFTEST` — the one self-test that drives the LIVE persisting path;
  `SIGHTLINE_BRIEFFIRST=0` restores the old order; `SIGHTLINE_FIRSTRUN=1` arms the strip under
  the screenshot harness so a first-ever mission 1 can be photographed.)
- **The HUD is out of the BLOOM.** `Display.RenderFrame(board, hud)` renders the board, the
  death-flash and the overlay screens' animated backdrop (`Hud.DrawBackdropLayer`, split out of
  the six screen builders) into `_target`, runs `BuildBloom` on **that**, and only then paints the
  chrome into the same target — so no plate can flood its own label, while brightness, gamma, the
  biome grade and the vignette stay uniform across the whole frame. `Hud.BackdropOwnsFrame` skips
  the in-mission chrome on the **eight** screens with an opaque backdrop — Intro, Win, Lose,
  Skirmish setup, War Room, Codex, Draft and **AUDIO CHECK**, which W5 missed and W5-FIX added
  (it drew its own backdrop from the chrome pass, so the composite added the live board's glow
  straight through it: 17,010 px brightened >20 luma against a `d350416` build, now 0).
  Main-menu TRAINING OP:
  **2.19:1 → 10.76:1** glyph-vs-plate with post-FX ON. (`SIGHTLINE_CONTRASTTEST` boots a real
  1280x800 window and reads the framebuffer back; `SIGHTLINE_HUDINFX=1` puts the chrome back in
  the bloom source and turns it red.)
- **Both end cards have a third door.** **WAR ROOM [W]** (`Hud.EndWarRoomBtn`, its own rect so a
  stale end-card rect can never alias the intro's LAST STAND), a "spend it in the WAR ROOM" line
  under the SALVAGE slab, and a **"N JOIN THE RESERVE - recallable at the next draft"** header on
  SURVIVING SQUAD with each survivor's `MetaProg.RecallCost`. `Game.EndReserve` is the DELTA of
  `SaveGame.VeteranCount()` across the award, so the card can never over-claim.
- **QUIT TO DESKTOP.** `[Q]` on the pause card (arm-then-confirm, with "the current mission
  restarts from its start") and on the main menu (no confirm). `Game.QuitRequested` breaks the
  frame loop; the path writes nothing, deletes nothing and leaves the mission-start checkpoint and
  `meta.json` untouched. The mid-mission-checkpoint question is answered on the record in
  `docs/DESIGN.md` §5.1 (deferred, with the argument and the revisit conditions).
  (`SIGHTLINE_QUITTEST`.)
- **RECRUIT is the DEFAULT on a never-played profile.** `Game.FirstTimeProfile` (from
  `SaveGame.LoadRunTotals`) dials the intro to rung −1 and rewrites level 0's hint; "< RECRUIT"
  names the rung below zero on every profile. A default, not a rung — no measured heat number
  moves, and the harness sets heat explicitly under `NoPersist`.
- **A fixed-slot action bar with one backing plate.** Verbs whose PRESENCE can change between two
  turns of one mission (BEACON, STABILIZE, SHOW ALL) live at the tail, where the greedy
  bottom-row-first wrap means appending cannot move anything already placed; the ability slot
  reserves its " (N)" cooldown suffix. A single quiet plate under the whole bar.
- **The CONCEALED pill breathes 0.56–1.00** instead of 0.10–1.00 (`Hud.ConcealPulse`, read by both
  the renderer and the test).
- **Doctrine cards size to their content** (`Hud.DraftBoonCardHeight`) and the operator blurb's row
  clears the class-glyph disc. The whole draft screen is laid out by **one** clamped stack
  (`Hud.DraftLayout`), which reclaims a taller card's height from the gaps rather than pushing the
  DEPLOY row off the bottom, and the doctrine row spans the same width as the RUN CONTRACT row
  beneath it. `[R]` re-rolls the pool. (`SIGHTLINE_CHROMETEST` covers all three chrome items and
  asserts the DEPLOY row is on screen for all 16 boons × all 4 text sizes;
  `SIGHTLINE_OLDCHROME=1` restores the pre-W5 chrome and turns it red.)
- **The whole shipped TEXT SIZE range is a tested surface** (wave THE FIT). `SIGHTLINE_FITTEST`
  asserts one contract — *no string is painted outside the box that owns it, and no two independent
  strings are painted into the same pixels* — over five surfaces at **all four** scales
  {0.90, 1.00, 1.10, 1.20}: the mid-run FIELD DOCTRINE card (all 16 boons), the ARMORY weapon rows
  (5 weapons × 3 right-hand tags), the WAR ROOM HALL OF FAME legend rows (all 8 ranks × 5 classes,
  not the 5 short staged ones), the draft's BACK/DEPLOY/RE-ROLL plates (all 5 DEPLOY state labels),
  and the draft operator card's blurb + ABILITY columns. It prints its own tightest margin per leg.
  `SIGHTLINE_OLDFIT=1` restores all five pre-fix geometries and turns it red (40 violations).
  The corresponding chrome all sizes to its content now: `Hud.BoonOfferCardH`, `Hud.ArmoryTagY`
  (the price/EQUIPPED tag shares the weapon NAME's band, never the blurb's), `Hud.WarLegendScore`
  (kills + heat right-align into their own column), `Hud.DraftConfirmW`/`DraftRerollW` (sized from
  the widest label the button can ever show) and `Hud.DraftCardW` (== the RUN CONTRACT row / 3).
- **One registry owns the full-screen backdrops** (`Hud.BackdropPhase` + `Hud.DrawBackdropLayer`'s
  switch — every `DrawTacticalBackdrop` call in the project lives in that switch).
  `SIGHTLINE_BACKDROPTEST` drives every `Phase` through the chrome pass and fails if any screen
  builder paints a backdrop of its own, then checks the registry and the switch are the same set,
  then that the modal scrim doubles only where the composite runs. (`SIGHTLINE_AUDBACKDROP=1`
  restores the AUDIO CHECK defect and turns it red.)
- **Teaching layer:** both "glowing tile" prompts rewritten to name the CYAN OUTLINE, the corner
  ticks and the DASHED outer ring; **FIELD CRAFT** gains **SHOVE** and **UTILITY ITEMS** rows (the
  two verbs whose only explanation was a one-shot 9-second tip); and a new **VERBS & KEYS** codex
  tab lists 16 verbs with hotkeys plus selection/camera/global bindings — generated from
  `Hud.VerbTable` + `Hud.VerbHelp`, i.e. the same `ActionDesc` switch the action bar's hover
  tooltip reads, so help and manual cannot drift. CODEXTEST asserts every verb has a home.
