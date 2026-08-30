# SIGHTLINE — Roadmap (history + open work)

> Extracted from `CLAUDE.md`. Phases 1–5 are **complete**; what remains is
> open-ended polish (listed at the end of Phase 5 and in the "OPEN/NEXT" notes of
> `docs/DEVLOG.md`). Kept for provenance — the checkbox history shows how the game
> was built and why each system exists.

## ROADMAP — pick up here (ordered by impact)

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
      with extra `HitStop` (0.4s slow-mo) + `AddZoomPunch` + shake. Per-mission state
      cleared in `SetupMission`. Verify: `SIGHTLINE_KIA=1` screenshot + autoplay clean.

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
      **96.8% (n=31)**. Six of eight objectives let a squad decline the encounter and still win.
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

- [x] **THE FLAT MIDDLE — addressed by PROGRAM CONTOUR wave C1 (`docs/measurements/c1/`, DEVLOG
      §C1). One lever shipped, and the finding was sharpened, not just closed.** Measuring all TEN
      rungs (n=320, n=640 at h6, base `17934ee`) showed "`h4 → h6` is flat" is really **two rungs,
      one of them exactly zero**: rung 5 buys **0.0 ±3.2**, rung 6 buys **2.3 ±2.7**, against six
      other rungs averaging 5.7. Mechanism found: **rung 6 declared `AiTier = 1`, which could never
      fire** — rung 4 already published tier 1 and the aggregation is `Math.Max` — so EXPOSED's
      advertised coordination tooth was dead for two programs. Shipped `Heat.MidTooth` (default 1):
      NO QUARTER's +1 per-hit damage moves down to rung 6. **`h4 → h6` −2.3 ±2.7 → −8.9 ±2.6**;
      six rungs exactly unchanged (0/320 discordant each); h6 lands at its band floor, 12.0.
      `SIGHTLINE_MIDTOOTH=0` restores the pre-C1 table; `SIGHTLINE_MIDTOOTHTEST` pins it.

- [ ] **THE LADDER'S TOP HALF HAS SUNK ONTO ITS BOTTOM — C1's closing finding, and now the
      biggest open number.** Against band centres, **h0 is 10.6 points low and h4 is 9.1 low**,
      while h6 and h8 are within 2. An apex-neutral `Heat.Mods` lever pins h4 (20.9) and h8 (8.1),
      leaving **12.8 points of win-rate for four rungs — 3.2 each** — so no redistribution inside
      the table can give rungs 5–8 the ~5.7 points per rung that rungs 1–4 buy. C1 could stop one
      rung taking almost none of it, and did; it could not create room that is not there.
      **This is a BASE-difficulty lever, not a heat-table lever.** Price it against the band
      (RECRUIT 75 / h0 55 / h2 40 / h4 30 / h6 20 / h8 10) and expect the whole ladder to move.

- [ ] **RUNG 5 (LINGERING WOUNDS) BUYS EXACTLY ZERO — 0.0 ±3.2 at n=320.** The deadest rung on
      the ladder, and C1 left it alone (one lever per wave). Two unmeasured hypotheses to test
      first: its `+1 enemy` is partly eaten by the **12-hostile spawn cap**
      (`Mission.cs:653`, `Math.Clamp(EnemyBaseCount + n + enemyDelta, 3, 12)`) on late missions,
      and `HarshAttrition` compounds over a run length the autopilot rarely reaches (avgMis 3.66
      at h5). **Instrument the delivered headcount per (heat, mission) before choosing a lever.**

- [ ] **A "heat-N rung" IS NOT A FIXED RUNG.** `Events.cs:460` (`EventOutcomeKind.AddHeat`) lets
      three field-event choices raise a run's HeatLevel mid-campaign, +1 each, so every heat-N cell
      contains some heat-N+1 missions. C1 measured the leak (5 discordant pairs in 320 at h5 for a
      lever that touches only rungs 6+; zero at h4, where it would have to fire twice). Harmless at
      C1's magnitudes, but **every past "this rung is unaffected" claim on this ladder has had this
      hole in it**, and a future wave making a tighter inertness claim must account for it.

- [ ] **THE MID-RUN DECAPITATE — replicated three times, mechanism located, lever unspent.**
      44.7% ±3.9 (n=161) against the finale's 68.0% (n=472): **23.3 points harder than the
      climax.** W8 refuted the first hypothesis (the HVT buff — the buffed half is *easier*) and
      located it in the **force**: `Mission.Build` de-stacks the finale by 3-4 bodies and resets
      `bump`; no mid-run Decapitate gets that, and an ELITE node adds +2 bodies and +1 stat.
      **The next lever belongs on the force de-stack, not on the target.**

- [ ] **KILL OBJECTIVES ARE A DIFFERENT GAME FROM THE REST.** On mid-run node kinds, kill
      objectives read **38.3% ±3.1 (n=248)** against **83.4% ±1.0 (n=1259)** for the six with a
      non-combat win condition; at mission 5, Eliminate 25.6% and Sabotage 96.8%. That is a
      45-point gap between objective *classes*, not between objectives.

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

### Standing gaps, honestly declared

- [ ] **W10's text-scale gate covers five surfaces, not the game.** Every other screen is still
      asserted at 100% only. `FITTEST` is written so a sixth leg is an addition, not a rewrite.
- [ ] **The enemy OVERWATCH branch is effectively dead** — 0 of 1595 pre-W2 and 3 of 1589 post.
      W3's premise (overwatch as a real enemy choice) is therefore unexercised.
- [ ] **W3 THE OPPONENT CHOOSES was never started.** `Ai.cs` still scores any shot at
      `100 + bestHit` against terrain terms bounded under ~64, so the opponent now always ACTS but
      still never DECLINES. That is the single biggest remaining gap in the fight.
- [ ] **W6 (biome mechanical) and W7 (ships-like-a-product) were never started.** `grep -ci biome`
      still returns 0 in `Combat.cs`, `Ai.cs`, `Grid.cs` and `Unit.cs` — eight biomes are paint.
- [ ] **A deadlock inside `UpdateEnemy`** would still be bounded only by the frame cap; W9's idle
      guard covers the player turn only.
- [ ] **On-device audio** still needs the owner: nobody has heard this game.
