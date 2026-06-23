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
- **Art policy (clarified):** **no *hand-made / human-authored* art or audio** — the
  human won't be making assets by hand. The aesthetic is built from geometry +
  particles + shaders + screen shake, and audio is synthesised. **Generated assets ARE
  allowed:** make them **procedurally / in-engine / via shaders first**, and reach for
  an external **AI** generator only where procedural genuinely can't get the look;
  **commit small, optimised generated files** (e.g. a font) but keep large binaries out
  so the repo stays lean. Raylib can generate noise/gradient textures (`GenImage*`) and
  bake TTF/OTF fonts (`LoadFontEx`) in-engine, so most upgrades need **no committed
  binaries**. Rationale + visual style guide: [`docs/DESIGN.md`](docs/DESIGN.md) §3.H;
  visual roadmap: **PHASE 5**. (Drawn text is ASCII-only **only until a font ships**,
  Phase 5.3 — a current limitation, not a permanent rule.)
- The human plays/compiles on their own Linux machine with the JetBrains suite
  (**Rider**). Don't assume a display is available *here* — see testing below.

### Design pillars (what "good" means here)
1. **Looks good** via a strong, consistent geometric aesthetic (see `Pal` palette).
2. **Feels good** via game-feel/juice: tweened motion, tracers, particles,
   floating damage numbers, screen shake, snappy readable UI.
3. **Well-designed loop:** second-to-second (move/aim/shoot), minute-to-minute
   (cover/flank/overwatch decisions), and — the current frontier — run-to-run
   (mission-to-mission squad progression). See ROADMAP.

> **Design rationale lives in [`docs/DESIGN.md`](docs/DESIGN.md)** — the *why* behind
> these pillars: researched game-design principles, a candid self-assessment, and the
> reasoned encounter-design decision (incl. why fog of war is **deferred, not
> rejected**). Read it before any change that touches the game's **feel** or
> **information design**.

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
- **This is a fully autonomous, you-owned project. Do NOT ask the human "should I
  continue?" or wait for approval to take the next step.** Keep picking up the top
  unchecked ROADMAP item and building it, verify it, commit + merge to `main`, then move
  to the next — looping until you hit a real stopping point (context rot risking quality,
  a milestone worth a handoff, or something genuinely blocked that only the human can do,
  e.g. audio that needs a real device). The human only chimes in with occasional feedback
  / playtests; every other decision (what to build, scope, design, when to ship) is
  yours. The one hard gate is below: **NO GitHub Actions / CI, ever.**

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

# Start the harness on a specific mission. Objectives now come from the branching
# campaign map (per-node), baseline rotation Elim/Hack/Evac/Escort/Sabotage (Run.ObjectiveFor,
# (n-1)%5). Force one for testing with SIGHTLINE_OBJ=sabotage. SHOT or AUTOPLAY, e.g.:
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
  turn + empty queue. **`Anim.OnStart` runs when an anim becomes ACTIVE (first frame
  it's `_anims[0]`), NOT at enqueue** — otherwise every queued step of a multi-tile
  path captures its `_from` at the original tile and the unit snaps back to the start
  each step (the old movement-jitter bug). Don't call `OnStart` in `Enqueue`.
- Player issues actions (move/shoot/overwatch/hunker/reload) → enqueues anims.
- Movement is per-tile `MoveStepAnim`s; on each tile entry `Game.OnUnitEnteredTile`
  checks **overwatch** reactions and injects reaction `ShotAnim`s at the front.
- Enemy turn is staged in `UpdateEnemy` (PickNext → ActAfterMove) using `Ai.Plan`;
  it enqueues the same anims, so overwatch/feel are shared.
- `KillUnit` purges a dead unit's queued moves and spawns death FX.

### Combat model (tuning lives in code)
- Hit% = aim + weapon.AimBonus + weapon.RangeMod(dist) − cover.Defense
  (− hunker), clamped 3..95. Cover: low −20 / high −40. Flanked = had adjacent
  cover but not protecting from this angle → exposed (+35 crit). `Grid.GetCover`:
  for a dominant-axis (cardinal) attack the facing side covers fully. For a **diagonal**
  attack: a TRUE corner (cover on BOTH facing sides) = full cover (weaker level);
  a single facing-side cover = **half cover at range** (`CoverInfo.Partial` → ½ Defense,
  not flanked, `ShotOdds.Partial`/"~ PARTIAL COVER") but **no cover when adjacent**
  (point-blank diagonal slips past the corner → flank). High ground also negates the
  target's LOW cover (see elevation). Verified end-to-end by `SIGHTLINE_COMBATTEST`.
- Each `WeaponKind` has its own `RangeMod` curve + `MaxRange` + clip + crit base.

---

## Raylib-cs 8.0 gotchas (learned the hard way)
- **Raylib's default exit key is ESC** — it sets `WindowShouldClose()` and quits the
  app before any of our handling runs. We call `Raylib.SetExitKey(KeyboardKey.Null)`
  after `InitWindow` so ESC instead cancels aim/grenade targeting and opens the pause
  menu. Don't remove it or ESC will close the game mid-action.
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
  **CoolHeaded** divert incoming aim) make each soldier a build; autopilot auto-picks.
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
      dim) screenshots. **TODO:** a true contrast/gamma post-pass (needs a shader) + an
      independent UI text scale (invasive — all DrawText sizes are fixed).

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

## Handoff protocol (when context gets heavy)
You judge when context rot risks quality (don't wait for the 1M hard limit).
Before stopping:
1. Make sure `main` builds and passes autoplay.
2. Update **Current state** and the **ROADMAP** checkboxes here.
3. Leave any mid-flight notes in a `### WIP NOTES` block at the bottom of this
   file (what you were doing, the next concrete step, any gotcha).
4. Tell the human to open a fresh session (they'll send only `.`).

### WIP NOTES

> **PROGRAM "DEEP STRIKE" — NEW MULTI-WAVE PUSH (read first; full process log in `docs/DEVLOG.md`).**
> Fresh fully-autonomous session running the project as a dev team (orchestrator + parallel dev agents in
> isolated worktrees + independent reviewers + research/audit agents). Develops on
> `claude/fervent-fermat-6lxlyv` (PR open; NOT pushing main this session). A research+audit pass converged
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
> **NEXT: Wave 4** = enemy-intent telegraph (J1: show each hostile's planned move/threat before it acts --
> `Game`/`Renderer`) + a NEW objective and/or anti-turtle pressure clock; plus a balance/bug-hunt pass over
> the now-large content (16 perks, 14 enemy archetypes, 8 biomes, Heat ladder). Game/Hud are the bottleneck
> (one owner per wave). Then: consolidation QA + closing summary.

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
