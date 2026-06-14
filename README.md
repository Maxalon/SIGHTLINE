# SIGHTLINE — Turn-Based Squad Tactics

A compact, XCOM-style tactics game built in **C# + [Raylib](https://www.raylib.com/)**.
Command a four-soldier squad on a grid battlefield: spend action points, use cover,
flank the enemy, set overwatch, and wipe the hostiles before they wipe you.

No art or audio asset files — the whole game is drawn from geometry, particles
and screen-shake, and every sound effect is **synthesised procedurally at
runtime**. It stays small, fast, and runs natively on Linux, macOS and Windows.

![SIGHTLINE gameplay](docs/screenshot.png)

## The game loop

- **Two actions per soldier.** Move, then fire — firing ends that soldier's turn.
  A double-move ("dash", shown in yellow) spends both actions.
- **Cover is everything.** Stand beside a wall and incoming aim drops sharply
  (low cover −20, high cover −40). Get **flanked** — hit from a side your cover
  doesn't block — and you're exposed *and* far more likely to be crit.
- **Percentage-to-hit** based on aim, weapon, range profile, and the target's cover.
  Hover any hostile to see hit %, crit % and damage before you commit.
- **Overwatch** holds a reaction shot: any enemy that moves through your sights
  gets fired on automatically (and vice-versa).
- **Hunker** trades your turn for extra defence and crit immunity.
- **Grenades** lob over cover for guaranteed AoE damage and blow apart low cover —
  but they hit your own soldiers too, so mind the blast.
- **Hostiles lurk in pods**: a group stays dormant (shown dimmed, marked `?`)
  until a soldier spots it, then it wakes and scrambles to cover. Scouting ahead
  is a real risk — push too far and you can wake two pods at once.
- Clear all hostiles to win the mission; lose a mission if the whole squad falls.

### The campaign (run-to-run loop)

A run is **six escalating missions** played with a single, persistent squad:

- Survivors carry their **wounds and ranks** into the next mission (a partial
  field-heal happens between missions — damage matters).
- **Kills earn promotions.** Rookie → Squaddie → Corporal → … → Colonel, each
  rank granting a stat bump (+Aim / +HP / +Mobility).
- A **barracks debrief** between missions shows survivors, promotions, heals and
  the fallen — and **fresh rookies backfill** any empty slots so a bad mission
  doesn't doom the run — then deploys you to a tougher fight.
- Missions vary by **objective**: most are *eliminate all hostiles*, but every
  third is an *extraction* — get the whole squad to the marked EVAC zone (fight
  through, or sneak past dormant pods).
- Lose the whole squad and the run ends; clear all six and the campaign is won.

### Soldiers & weapons

| Class | Weapon | Identity |
|-------|--------|----------|
| Assault | Rifle | Balanced all-rounder |
| Ranger | Shotgun | Brutal up close, useless at range |
| Sharpshooter | Marksman rifle | Rewards distance, high crit |
| Gunner | LMG | Tanky, big magazine |

Each weapon has its own range curve, crit rate, and clip size, so positioning
and reloads matter.

## Controls

| Input | Action |
|-------|--------|
| **Left-click** a soldier | Select |
| **Left-click** a tile | Move there (blue = 1 action, yellow = dash) |
| **Left-click** a hostile | Fire |
| **1** / FIRE | Enter targeting mode |
| **4** / GRENADE | Throw a grenade (AoE, ignores cover, destroys low cover) |
| **2** / OVERWATCH | Hold a reaction shot |
| **3** / HUNKER | Defensive crouch |
| **R** / RELOAD | Reload weapon |
| **Tab** | Cycle to next soldier |
| **Enter** | End turn |
| **Esc / Right-click** | Cancel targeting |
| **M** | Mute / unmute |

## Build & run

Requires the **[.NET 8 SDK](https://dotnet.microsoft.com/download)** (cross-platform).

```bash
# from the project root
dotnet run -c Release
```

To produce a standalone native binary (no SDK needed to run it):

```bash
# Linux
dotnet publish -c Release -r linux-x64 --self-contained
# the executable lands in bin/Release/net8.0/linux-x64/publish/Sightline
```

Use `osx-x64` / `osx-arm64` / `win-x64` for other targets.

### In Rider (recommended IDE)

Use **Rider** — this is a C#/.NET project. (Don't use CLion: that's for C/C++ via
CMake and there's no `CMakeLists.txt` here.)

1. **Open** the project folder (or `Sightline.csproj`) — not "New CMake project".
2. A shared **Sightline** run configuration is committed under `.run/`, so the
   green ▶ Run button is ready immediately on a fresh clone — just hit Run
   (Shift+F10). It builds then launches the game.

The Raylib native libraries are pulled in automatically via the `Raylib-cs`
NuGet package (needs internet on first restore). Requires the .NET 8 SDK, which
Rider can install/detect for you.

## Project layout

```
src/
  Program.cs    entry point + window loop
  Game.cs       state machine, input, turn flow, overwatch, AI staging
  Grid.cs       battlefield, line-of-sight, cover, 8-dir pathfinding
  Unit.cs       soldiers, weapons, stats
  Combat.cs     hit/crit/damage math
  Ai.cs         enemy decision-making (seek cover + line of fire, flank, finish)
  Anim.cs       sequential move / shot animations
  Fx.cs         particles, floating combat text, screen shake
  Renderer.cs   battlefield + unit drawing
  Hud.cs        bars, action buttons, tooltips, overlays
  Util.cs       layout constants, palette, math/rng helpers
```

## Ideas for where to take it next

- More abilities + ammo types, additional enemy archetypes
- Elevation / high ground and destructible high cover
- Hand-authored maps and mission objectives (evac, VIP escort, hack-the-terminal)
- Saving runs to disk; a wider roster with recruits replacing the fallen
