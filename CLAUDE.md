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

**BREACH** — a turn-based, XCOM-style squad tactics game.

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

- **NO CI.** Do not add `.github/workflows/*` or any CI config. Keeping CI out
  of the repo is an explicit, standing responsibility.
- **Full autonomy:** build, commit, and **merge to `main`** freely. No PR/review
  ceremony is required (no reviewers exist). PRs are optional.
- **Always ship compiling code to `main`.** Before merging, it must (a) build
  clean in Release and (b) pass the headless autoplay smoke test (see below).
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

# Screenshot a frame -> breach_shot.png  (then Read it to inspect visuals)
BREACH_SHOT=90 xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug

# Full-match autopilot smoke test -> prints "RESULT: WIN|LOSE|TIMEOUT"
BREACH_AUTOPLAY=1 xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug
```

Run autoplay a few times (RNG varies) and confirm no exceptions and no TIMEOUT.
`breach_shot.png` is gitignored; `docs/screenshot.png` (README image) is committed.

---

## Architecture (file map)

```
Breach.csproj     net8.0, Nullable disabled, Raylib-cs 8.0.0
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
  promotions (+Aim/+HP/+Mobility), between-mission barracks debrief + field-heal.
- **Grenades:** AoE that ignores cover, hits both teams, destroys low cover
  (key 4, 1 charge/mission) with range/blast/arc preview.
- **Activation pods:** enemies dormant (dimmed, "?") until a soldier sights them,
  then the pod wakes + scatters to cover ("CONTACT!"). Scouting carries risk.
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
- [~] **4. Tactical depth.** IN PROGRESS.
      - [x] **Grenades.** `GrenadeAnim` (src/Anim.cs): lobbed arc → AoE explosion,
            Chebyshev radius 1, ignores cover, hits BOTH teams (friendly fire),
            destroys low cover in the blast. 1 charge/soldier, refilled each
            mission (`Unit.Grenades`). Action key **4**; targeting mode in `Game`
            (`GrenadeMode`/`GrenValid`) with range ring + blast + arc preview
            (`Renderer.DrawGrenade`). AI does NOT use grenades yet.
      - [x] **Enemy activation pods.** DONE. Enemies spawn dormant (`Unit.Active`
            false, grouped by `Unit.PodId`). `Game.CheckPodActivation` (called each
            player frame + on player tile-entry) wakes a whole pod when any soldier
            gets LoS within `SightRange` (12); `ActivatePod` gives a free scatter
            (Ai.Plan move) + "CONTACT!" banner. Shooting/grenading a dormant enemy
            also wakes its pod. Dormant enemies are skipped in `UpdateEnemy` and
            drawn dimmed with a "?" (`Renderer.DrawUnit`).
      - [ ] Elevation / high-ground aim bonus (bigger change; deferred — needs a
            height layer on tiles + LoS/aim tweaks).
- [ ] **5. Map variety.** A couple of hand-tuned layouts and/or better procedural
      generation with guaranteed connectivity + cover balance; objective types
      (e.g. reach-the-evac, VIP).
- [ ] **6. Polish/UX.** Camera pan/zoom for larger maps, end-of-turn confirmation
      when actions remain, keyboard tile cursor, settings.

When you finish an item: verify (build + autoplay + a screenshot), commit, merge
to `main`, tick the box, and update "Current state".

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
Items 1 (audio), 2 (juice), 3 (campaign meta-loop) done & merged. Item 4
(tactical depth): **grenades + activation pods done & merged**; only elevation
remains (deferred as a larger change). The game is feature-rich and stable —
autoplay reaches a clean result across many seeds with no exceptions/timeouts.

**Next up: ROADMAP item 5 — map variety & objectives.** Suggested:
- Add mission **objectives** beyond "kill all": e.g. reach-the-evac (get any
  soldier to a marked tile), or hold/hack a terminal for N turns. Add an enum to
  `Mission`/`Run` and check it in `Game.CheckEnd`.
- A couple of hand-authored map layouts mixed with the procedural generator;
  ensure connectivity + balanced cover. `Mission.Build` is the single entry point.
- Keep the per-mission scaling; maybe vary objective by mission number.

Smaller open items / good warmups:
- Persist a run to a save file under the OS user-data dir (NOT in the repo).
- Elevation / high-ground aim bonus (item 4 leftover; needs a tile height layer).

Conventions: drawn strings must be ASCII. Always build Release + run
`BREACH_AUTOPLAY=1` (a few times — RNG varies) before merging to `main`.
