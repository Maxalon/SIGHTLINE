# SIGHTLINE — Turn-Based Squad Tactics

A compact, XCOM-style tactics game built in **C# + [Raylib](https://www.raylib.com/)**.
Command a four-soldier squad on a grid battlefield: spend action points, use cover,
flank the enemy, set overwatch, and wipe the hostiles before they wipe you.

Almost everything is generated: the board is geometry, particles, procedural
textures and shaders, and every sound effect and music bed is **synthesised at
runtime** — no recorded audio ships. The only committed binaries are two text
fonts (Noto Mono and Chakra Petch, both SIL Open Font License 1.1, full licence
text in `assets/`) and the screenshot below. A whole distributable build is
**27 MB across 10 files**, self-contained: no .NET install needed on the target
machine. It runs natively on Linux, macOS and Windows.

![SIGHTLINE gameplay](docs/screenshot.png)

## The game loop

- **Two actions per soldier.** Firing costs one action and does *not* end the
  turn, so **where you stand after shooting is the real bet** — shoot then
  reposition, or spend the second action on a rushed follow-up shot. A
  double-move ("dash") spends both actions.
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
- Missions vary by **objective** — eliminate, extract, hack, escort, sabotage,
  rescue, hold, decapitate — dealt so every route mixes them, and the **opening
  geometry varies too**: a frontal push, a pincer, a crossfire, or an envelop
  that starts you surrounded.
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

Every verb is also a button on the action bar, labelled with its key — you never have to
memorise this table. The game teaches the verbs as they become relevant (there is a
**TRAINING OP** on the main menu, and a **FIELD MANUAL** you can open at any time with `K`).

| Input | Action |
|-------|--------|
| **Left-click** a soldier | Select |
| **Left-click** a tile | Move there (blue = 1 action, yellow = dash) |
| **Left-click** a hostile | Fire |
| **hold Shift** | Reveal the dash/sprint region in the move overlay |
| **1** / FIRE | Enter targeting mode |
| **2** / OVERWATCH | Hold a reaction shot |
| **3** / HUNKER | Defensive crouch |
| **4** / GRENADE | Throw a grenade (AoE, ignores cover, destroys low cover) |
| **5** | Class ability (Grapple / …, varies by class) |
| **6** / FLASH | Flashbang |
| **7** / DRAG · **8** / SHOVE · **9** / VAULT | Situational verbs; greyed when unavailable |
| **F** / FOCUS · **B** / BRACE | Aim buff · braced overwatch cone |
| **R** / RELOAD | Reload weapon |
| **T** | Rename the selected soldier |
| **Tab** | Cycle to next soldier |
| **Enter** | End turn |
| **Esc / Right-click** | Cancel targeting; Esc again opens the pause menu |
| **Wheel** / **Middle-drag** / **C** | Zoom · pan · reset camera |
| **Arrows / WASD + Space** | Keyboard cursor: move the cursor, Space confirms |
| **M** / **U** | Mute-unmute · AUDIO CHECK screen |
| **K** / **Q** | Field manual · quit to desktop |
| **F11** / **F2** | Fullscreen · animation speed |

The pause menu (`Esc`) carries the rest: window size, brightness, gamma, colourblind palette,
screen shake, threat preview, animation speed, **text size**, and the four-channel audio mix.
It also shows the build version, which is what to quote in a bug report.

## Build & run

Requires the **[.NET 8 SDK](https://dotnet.microsoft.com/download)** (cross-platform).

```bash
# from the project root
dotnet run -c Release
```

To produce a standalone distributable (no SDK needed to run it), use the publish script —
**not** a bare `dotnet publish`:

```bash
bash scripts/publish.sh                 # -> dist/linux-x64-release/  (27 MB, 10 files)
bash scripts/publish.sh --rid win-x64   # or osx-x64 / osx-arm64
```

The script exists because the recommended configuration is trimmed, and a trimmed build once
compiled with 0 errors, booted, played a whole campaign and **saved nothing at all**. So it
re-runs the persistence and shipping self-tests against the binary it just built and refuses to
report success if any of them fail. **Ship the whole output directory**, not just the executable:
`libraylib.so` is `dlopen()`ed at runtime and cannot be linked in, and `assets/`,
`THIRD-PARTY-NOTICES.txt` and `LICENSE` are licence obligations, not extras. Details, the measured
size/startup matrix and where player data lives: [`docs/DISTRIBUTION.md`](docs/DISTRIBUTION.md).

## Licence

The game's own source is **all rights reserved** — see [`LICENSE`](LICENSE), which explicitly does
*not* restrict distributing compiled builds. Bundled third-party components (raylib and Raylib-cs
under Zlib, the .NET runtime under MIT, Noto Mono and Chakra Petch under the SIL Open Font License)
keep their own terms; the notices are in `THIRD-PARTY-NOTICES.txt` and both fonts ship their full
OFL text beside them in `assets/`.

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

## Where it stands

The tactical layer, the campaign, the meta profile and the presentation are all
built. Recent work (PROGRAM RESONANCE) added a **TRAINING OP** and staged verb
teaching, a **RECRUIT** difficulty below standard, an **incoming-fire forecast**
that shows how many guns bear on a tile before you move there, generated
**briefings and a run epilogue**, per-biome cover materials and terrain, and a
real audio mix — plus a shippable self-contained build.

Verification is by hand, by design: there is **no CI and there never will be**.
`bash scripts/qa-sweep.sh --full` runs every self-test in the tree (it derives
the list from `src/`, so a test that exists but is never run gets reported), and
`SIGHTLINE_BALANCE=<N>` runs headless campaigns for balance telemetry.

Open work — including a post-merge difficulty re-baseline, which is currently
the biggest known gap — lives in [`docs/ROADMAP.md`](docs/ROADMAP.md); the
per-wave history and every measured number is in
[`docs/DEVLOG.md`](docs/DEVLOG.md).
