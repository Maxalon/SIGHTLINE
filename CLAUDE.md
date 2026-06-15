# CLAUDE.md — project brief & continuity doc

> **If you are a fresh session that was started with just `.` and no other
> instructions:** this is your project. You have full ownership of this repo
> (the human is the only other writer and has delegated control). Read this
> file top to bottom, run the dev setup, then continue from the **ROADMAP**
> section below — pick the top unchecked item and build it. Commit to the
> working branch and **merge to `main` yourself** at each milestone.

This file is the single source of truth for what we're building and where we
are. **Keep it updated** — when you finish work, tick the roadmap and refresh
"Current state" so the next session inherits an accurate picture.

---

## What this is

**SIGHTLINE** — a turn-based, XCOM-style squad tactics game.

- **Stack:** C# / .NET 8 + [Raylib-cs](https://github.com/raylib-cs/raylib-cs) 8.0.0 (NuGet).
- **Platform:** native; primary target **Linux** (also macOS/Windows). Compiles to a
  real ELF binary via `dotnet` — there is no `.exe` involved on Linux.
- **Art policy:** **no external art/audio assets.** Everything is drawn from
  geometry + particles + screen shake. Procedural audio is allowed (synthesised
  at runtime). This keeps the repo tiny and fully self-contained.
- The human plays/compiles on their own Linux machine with the JetBrains suite
  (**Rider**). Don't assume a display is available *here* — see testing below.

### Design pillars (what "good" means here)
1. **Looks good** via a strong, consistent geometric aesthetic (see `Pal` palette).
2. **Feels good** via game-feel/juice: tweened motion, tracers, particles,
   floating damage numbers, screen shake, snappy readable UI.
3. **Well-designed loop:** second-to-second (move/aim/shoot), minute-to-minute
   (cover/flank/overwatch decisions), and — the current frontier — run-to-run
   (mission-to-mission squad progression). See ROADMAP.

---

## Hard constraints / ground rules

- **NO CI. NO automated tests.** Do not add `.github/workflows/*`, any CI config,
  or a test framework/test runner. This is a private repo and the human does not
  want to spend Action minutes or run automated suites. Verify by **running the
  game locally yourself** (the env-gated harness below is launched by hand, not
  automated). If you write a check that can't run in this sandbox, describe it so
  the human can run it on their machine — don't wire it to run automatically.
- **Share screenshots in the chat.** The human follows progress visually. Every
  time you take a screenshot (e.g. `sightline_shot.png`), send it into the message
  thread with the `SendUserFile` tool so they can see the progression. Capture a
  frame for any notable visual change and surface it.
- **Full autonomy:** build, commit, and **merge to `main`** freely. No PR/review
  ceremony is required (no reviewers exist). PRs are optional.
- **Always ship compiling code to `main`.** Before merging, it must (a) build
  clean in Release and (b) pass the headless autoplay smoke test (see below),
  which you run manually.
- **Keep `CLAUDE.md` current** — it is the continuity contract.

---

## Build / run / test

**This container is ephemeral and starts WITHOUT the .NET SDK.** First thing in
a new session:

```bash
bash scripts/dev-setup.sh          # installs dotnet 8 SDK + Xvfb + software GL
export PATH="$PATH:/usr/lib/dotnet"
```

Normal build/run (run needs a real display — fine on the human's machine):

```bash
dotnet build -c Release
dotnet run   -c Release
```

**Headless verification in this sandbox** (no display) uses Xvfb + llvmpipe and
an env-gated harness baked into `Program.cs`:

```bash
export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe

# Screenshot a frame -> sightline_shot.png  (then Read it to inspect visuals)
SIGHTLINE_SHOT=90 xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug

# Full-match autopilot smoke test -> prints "RESULT: WIN|LOSE|TIMEOUT mission=N"
SIGHTLINE_AUTOPLAY=1 xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug

# Start the harness on a specific mission. Objective rotation is now
# Elim / Hack / Evac / Escort: Hack=2/6, Evac=3, Escort=4 (VIP).
# Works with SHOT or AUTOPLAY, e.g. screenshot the Hack mission:
SIGHTLINE_MISSION=2 SIGHTLINE_SHOT=80 xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug
```

Run autoplay a few times (RNG varies) and confirm no exceptions and no TIMEOUT.
`sightline_shot.png` is gitignored; `docs/screenshot.png` (README image) is committed.

---

## Architecture (file map)

```
Sightline.csproj     net8.0, Nullable disabled, Raylib-cs 8.0.0
src/
  Program.cs    entry + window loop + env-gated test harness
  Game.cs       state machine, input, turn flow, overwatch, AI staging, autopilot
  Grid.cs       tiles, line-of-sight (Bresenham), cover queries, 8-dir Dijkstra
  Unit.cs       Unit + Weapon + enums (Team/WeaponKind); per-weapon range curves
  Combat.cs     ComputeOdds (hit/crit/dmg) + Resolve (rolls a shot)
  Ai.cs         enemy planner: score reachable tiles for cover+LoF, flank, finish
  Anim.cs       Anim base; MoveStepAnim, ShotAnim (tracer+impact), WaitAnim
  Fx.cs         particles, floating combat text, screen shake
  Renderer.cs   board, cover (faux-3D), units, overlays, cover shields, aim reticle
  Hud.cs        top/bottom bars, action buttons (rects hit-tested by Game), tooltip,
                banner, intro/win/lose cards
  Util.cs       Cfg (layout consts), Pal (palette), Util (math/rng/easing/tile<->px)
  Maps.cs       hand-authored ASCII arena templates (Mission stamps them in)
scripts/dev-setup.sh   sandbox setup
docs/screenshot.png    README image
```

### How a turn flows
- `Phase`: Intro → PlayerTurn ⇄ EnemyTurn → Win/Lose.
- An **animation queue** (`_anims` in Game) gates interactivity: while non-empty,
  input is locked and anims play one at a time. `IsPlayerInteractive()` = player
  turn + empty queue.
- Player issues actions (move/shoot/overwatch/hunker/reload) → enqueues anims.
- Movement is per-tile `MoveStepAnim`s; on each tile entry `Game.OnUnitEnteredTile`
  checks **overwatch** reactions and injects reaction `ShotAnim`s at the front.
- Enemy turn is staged in `UpdateEnemy` (PickNext → ActAfterMove) using `Ai.Plan`;
  it enqueues the same anims, so overwatch/feel are shared.
- `KillUnit` purges a dead unit's queued moves and spawns death FX.

### Combat model (tuning lives in code)
- Hit% = aim + weapon.AimBonus + weapon.RangeMod(dist) − cover.Defense
  (− hunker), clamped 3..95. Cover: low −20 / high −40. Flanked = had adjacent
  cover but not on the attacker's dominant-axis side → exposed (+35 crit).
- Each `WeaponKind` has its own `RangeMod` curve + `MaxRange` + clip + crit base.

---

## Raylib-cs 8.0 gotchas (learned the hard way)
- Construct colours via `Pal.RGBA(r,g,b,a)` (casts to byte) — don't rely on int
  Color ctors.
- `DrawRectangleRoundedLines` signature is version-volatile; **avoid it**. Use
  `DrawRectangleLinesEx` (square) or a manual highlight line.
- Enums are PascalCase, unprefixed: `KeyboardKey.One/Two/Three/R/Tab/Enter/Escape`,
  `MouseButton.Left/Right`, `ConfigFlags.Msaa4xHint`.
- Screen shake is implemented by drawing the board inside a `Camera2D` whose
  `Offset = Fx.ShakeOffset`; HUD is drawn outside the camera.
- `DrawPoly`/`DrawPolyLinesEx` are safe for glyphs (winding handled internally);
  be careful with raw `DrawTriangle` winding.

---

## Current state — DONE ✅
Playable vertical slice, builds clean (0 warn/0 err), autoplay-verified across
seeds (mix of WIN/LOSE, no exceptions):
- Grid battlefield w/ high+low cover, LoS, 8-dir pathfinding (corner-cut safe).
- 2-action combat: move, dash (yellow), fire (ends turn), overwatch reaction
  fire (both sides), hunker, reload.
- Cover + flanking + %-to-hit with hover tooltip (hit/crit/dmg, FLANKED warning).
- 4 player classes (Assault/Ranger/Sharpshooter/Gunner) + 5 hostiles incl. a
  scout (SMG) and bruiser (LMG); distinct weapons.
- Enemy AI: seeks cover + line of fire, advances when blind, flanks, finishes.
- Juice: move/shot anims, muzzle+tracer, particles, floating text, shake,
  selection ring, cover shields, turn banner.
- **Procedural audio** (src/Audio.cs) for all actions; mute = M.
- **Game-feel pass:** hit-stop on impacts/kills, camera zoom-punch on kills,
  weapon recoil + target knockback.
- **Campaign meta-loop:** 6 escalating missions, one persistent squad, kills→
  promotions, between-mission barracks debrief + field-heal, and a **deployment
  choice** (RECON/STANDARD/ONSLAUGHT cards: objective + risk/reward) each mission.
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
- **Enemy variety:** Grunt / Scout / Bruiser plus **Sniper** (kites to range),
  **Turret** (immobile overwatch nest), **Berserker** (tanky shotgun rusher), and a
  capstone **Elite boss** (WARLORD) on the final mission with 2 grenades and a
  one-time low-HP RAGE. Distinct AI temperaments in `Ai.Plan`; distinct glyphs.
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
  +10 crit firing down on lower targets; faux-3D platforms, height-aware overlays,
  AI seizes the high ground. Shown in the shot tooltip ("+ HIGH GROUND").
- **UX:** squad roster strip, end-turn confirmation, mute indicator, threat
  preview (red pips on exposed reachable tiles while positioning), **camera
  zoom/pan** (wheel + middle-drag, C to reset), a **keyboard tile cursor**
  (arrows/WASD + Space), and a **pause/settings menu** (Esc: audio, screen
  shake, threat-preview toggles, abandon run).
- **Recruits:** the barracks backfills empty squad slots with fresh rookies
  (`Mission.MakeRecruit`, `Run.DebriefSurvivors`) so casualties don't death-spiral.
- **Perk-based promotions:** each rank-up is a pick-1-of-2 perk choice in the
  barracks (`Perk`/`Unit.Perks`/`PerkDef`, `Run.PendingPerks`, `Hud.DrawPerkChooser`).
  10 perks (LockOn/Hardened/Reflexes/Bandolier/CloseQuarters/Marksman/Deadeye/Tank/
  Sprinter/Adrenal) make each soldier a build; autopilot auto-picks.
- **Class signature abilities:** each class has one self-cast signature (key **5**,
  1 charge/mission, refilled like grenades): Assault **RUN&GUN** (next shot costs 1
  action instead of ending the turn), Ranger **BLITZ** (next move costs one action
  less), Sharpshooter **STEADY** (next shot +25 aim/+20 crit), Gunner **SUPPRESS**
  (pin the nearest foe: -30 aim + train overwatch on it). `Unit.AbilityKind`/
  `Unit.Ability`/`Game.DoAbility`/`Game.CanAbility`; HUD ability button + on-unit
  stance tags (R&G/BLZ/AIM, SUPP on pinned foes); tooltip shows "+ STEADY".
- Full HUD + intro/barracks/win/lose; per-mission generator (scaled by mission #).
- Text is ASCII-only (Raylib's default font lacks fancy glyphs → they render `?`).

---

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
            ("+ HIGH GROUND"). `Mission.RaisePlateau` carves 2-3 walkable plateaus
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
            Two arenas so far: PLAZA (central plateau) + GAUNTLET (lane spine).
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
      a larger orange figure with a ring + name/rage tag (`Pal.Elite`). A
      **Medic** archetype is still open (would need an enemy heal action). Screenshot
      hook `SIGHTLINE_WAKE` reveals dormant pods.
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
      max HP to the frailest, permanent, 10), or ADV. TRAINING (a bonus perk choice,
      16). `Game.CanBuy/DoPurchase/HandleShopClick`; autopilot buys a medkit then
      proceeds (`AutoShop`). Hook `SIGHTLINE_SHOP`.
- [ ] **D. Procedural music + ambience.** Audio is SFX-only. A synthesised, layered
      ambient/combat track (allowed: procedural only) would lift "feels good"
      enormously. Build on `src/Audio.cs` (it already synth's PCM in memory).
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

Supporting polish (any time): distinct "VIP EXTRACTED/LOST" end cards; a 2nd
elevation tier; high ground seeing over LOW cover; secondary objectives; themed
authored arenas per biome; more requisition options (recruits/gear) for the shop.

---

## Handoff protocol (when context gets heavy)
You judge when context rot risks quality (don't wait for the 1M hard limit).
Before stopping:
1. Make sure `main` builds and passes autoplay.
2. Update **Current state** and the **ROADMAP** checkboxes here.
3. Leave any mid-flight notes in a `### WIP NOTES` block at the bottom of this
   file (what you were doing, the next concrete step, any gotcha).
4. Tell the human to open a fresh session (they'll send only `.`).

### WIP NOTES
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
the CONTINUE button shows), **`SIGHTLINE_SHOP`** (barracks requisition screen). Plus
non-shot **`SIGHTLINE_SAVETEST=1`** → prints `SAVETEST: PASS/FAIL` (save/load
round-trip; no window).

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

Conventions: drawn strings must be ASCII (default font). Build Release + run
`SIGHTLINE_AUTOPLAY=1` a few times before merging. Share screenshots in chat via
`SendUserFile` so the human can follow along.
