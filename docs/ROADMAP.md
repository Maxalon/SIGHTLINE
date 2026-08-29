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
      "DESIGN-QUESTION DOCKET"): skirmish numeric heat (recommend: exempt SKIRMISH from the m1
      grace, keep DAILY); founding-squad corpsman (recommend: first-backfill guarantee or
      keep-as-is — a founding-four identity choice, not a tune); grenade pre-frag bot arm
      (recommend: accept the human-vs-bot read gap as designed skill expression).
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
- [x] **THE BLOOM STOPS EATING THE TYPE** (`visual-2`). `Display.RenderFrame` is now two-target:
      board + overlay-screen backdrop keep the full grade, the HUD is drawn after the composite.
      Main-menu TRAINING OP measured **2.19:1 → 8.87:1** glyph-vs-plate with post-FX ON (method
      stated in DEVLOG §W5-2), and the board's own bloom is unchanged *within the screenshot
      harness's documented noise floor*, with both numbers recorded. `SIGHTLINE_CONTRASTTEST` is
      the standing gate; `SIGHTLINE_HUDINFX=1` falsifies it.
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
- [ ] **Residual (recorded, not fixed): `SIGHTLINE_POSTFX=1`'s "demo bloom" comment is stale.**
      The boot-time `BloomIntensity = 0.85` / `ChromaIntensity = 0.6` injection is overwritten on
      frame 1 by `Game.Update`'s own `SetPostFxParams(_postFxBloom = 0, …)`, so the hook has been
      photographing the RESTING configuration for waves. That is *better* for W5's purposes (the
      contrast numbers above are what every player sees on the menu, every time) but the hook does
      not do what its comment says. One-liner for whoever next touches that path.

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

- [ ] **SPEC (ready to dev, do NOT implement inside a tuning wave): re-specify
      `CountMeaningfulChoices` axis (b) as an ADDITIVE band.** Two waves (X1, W4) have now missed
      a decision-density gate that X2's baseline shows is not merely hard but *structurally
      unreachable*: axis (b) counts destinations scoring within **15% of the BEST** safety score
      (`24 − TileExposure + cover*8 + height*5`), so raising threat lowers the best score, shrinks
      the absolute window `0.15 × best`, and disqualifies tiles. Axis (a) ("which target?") and
      axis (b) ("where do I stand after?") therefore respond to threat with **opposite signs** and
      their sum is close to conserved — W4 measured 1.55-1.64 across five structurally different
      levers, and X2's baseline reads 1.44-1.78 across six *rungs*, which is the same invariance
      seen from the difficulty axis instead of the lever axis.
      **The fix:** replace the multiplicative window with an **additive** one — count a destination
      as a real alternative when it scores within a FIXED number of safety points of the best
      (start at 3, i.e. within roughly one cover step or half an elevation step), not within a
      fraction of it. An additive band measures "are there several places worth standing?" without
      being deflated by how dangerous the board is, which is what the metric was always trying to
      ask. It is a **pure instrument change**: it invalidates every archived `ch/ARMED` number, so
      it must ship with its own paired R0 re-baseline (the X1/W4/X2 `R0diag` pattern: run the
      instrumented tree lever-off on a pinned slot set and diff the per-slot records) and the
      DEVLOG must state that pre-change numbers are not comparable. Land it in a wave that is NOT
      also tuning difficulty, so the two effects can never be confused.
- [ ] **The heat ladder's MIDDLE does not measurably escalate.** X2's baseline (n=40/rung, ±6-8)
      reads mission-win 80.2 (h0) / 82.6 (h2) / 75.0 (h4) / 81.7 (h6) — a 2.8-point spread on
      n=263 vs n=266 pooled halves, i.e. nothing. Only RECRUIT (94.3) and heat 8 (68.4) separate.
      Rungs 1-7 add bodies and stat points that the measurement cannot see. Either the rungs need
      real teeth or the ladder needs fewer, bigger steps — but the first job is a **higher-N**
      measurement (n≥80/rung) so the question can be asked at a precision that can answer it.
