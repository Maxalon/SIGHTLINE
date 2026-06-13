# BREACH — Turn-Based Squad Tactics

A compact, XCOM-style tactics game built in **C# + [Raylib](https://www.raylib.com/)**.
Command a four-soldier squad on a grid battlefield: spend action points, use cover,
flank the enemy, set overwatch, and wipe the hostiles before they wipe you.

No art or audio asset files — the whole game is drawn from geometry, particles
and screen-shake, and every sound effect is **synthesised procedurally at
runtime**. It stays small, fast, and runs natively on Linux, macOS and Windows.

![BREACH gameplay](docs/screenshot.png)

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
- Win by eliminating all hostiles; lose if the whole squad goes down.

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
# the executable lands in bin/Release/net8.0/linux-x64/publish/Breach
```

Use `osx-x64` / `osx-arm64` / `win-x64` for other targets.

### In Rider

Open `Breach.csproj` (or the folder) and hit **Run**. The Raylib native libraries
are pulled in automatically via the `Raylib-cs` NuGet package.

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

- Sound (procedural synth, or sample-based via Raylib audio)
- Enemy "pods" that activate on sighting, height/elevation, destructible cover
- Grenades / abilities, ammo types, a soldier XP + meta-progression layer
- Multiple hand-authored or procedurally generated maps and mission types
