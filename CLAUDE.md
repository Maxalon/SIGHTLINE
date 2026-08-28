# CLAUDE.md — project brief & continuity doc

> **If you are a fresh session started with just `.`:** this is your project. You
> own this repo (the human is the only other writer and has delegated control).
> Read this file top to bottom, run the dev setup, then pick up the top open item
> in [`docs/ROADMAP.md`](docs/ROADMAP.md) and build it. Commit to the working
> branch and **merge to `main` yourself** at each milestone.

This file is the **lean continuity contract** — what we're building, how to build
and verify it, the ground rules, and where everything else is documented. Keep it
**short**. Detailed history, feature inventories, and per-session notes live in
`docs/` (see the **Documentation map** below) — **not everything belongs here.**

---

## What this is

**SIGHTLINE** — a turn-based, XCOM-style squad tactics game. It is
**feature-complete** against its original spec; current work is open-ended polish
and balance (see `docs/ROADMAP.md` and the "OPEN/NEXT" notes in `docs/DEVLOG.md`).

- **Stack:** C# / .NET 8 + [Raylib-cs](https://github.com/raylib-cs/raylib-cs) 8.0.0 (NuGet).
- **Platform:** native; primary target **Linux** (also macOS/Windows). Compiles to a
  real ELF binary via `dotnet` — no `.exe` on Linux.
- **Art policy:** **no *hand-made / human-authored* art or audio.** The aesthetic is
  geometry + particles + shaders + screen shake; audio is synthesised. **Generated
  assets ARE allowed** — procedurally / in-engine / via shaders first, AI generators
  only where procedural can't get the look. Commit **small** generated files (e.g. a
  font); keep large binaries out. Raylib bakes noise/gradient textures (`GenImage*`)
  and TTF/OTF fonts (`LoadFontEx`) in-engine, so most upgrades need **no committed
  binaries**. Drawn text was ASCII-only until a font shipped (Phase 5.3) — that limit
  is now **lifted**. Rationale + style guide: [`docs/DESIGN.md`](docs/DESIGN.md) §3.H.
- The human plays/compiles on their own Linux machine with Rider. **Don't assume a
  display is available *here*** — verify headlessly (see below).

### Design pillars (what "good" means here)
1. **Looks good** — a strong, consistent geometric aesthetic (see `Pal` palette).
2. **Feels good** — game-feel/juice: tweened motion, tracers, particles, floating
   damage numbers, screen shake, snappy readable UI.
3. **Well-designed loop** — second-to-second (move/aim/shoot), minute-to-minute
   (cover/flank/overwatch), and run-to-run (mission-to-mission progression).

Two more pillars — **Reads clearly** and **Stakes that bite** — and the full *why*
behind all of them live in [`docs/DESIGN.md`](docs/DESIGN.md). **Read it before any
change that touches the game's *feel* or *information design*.**

---

## Hard constraints / ground rules

- **NO CI. NO automated tests.** Never add `.github/workflows/*`, any CI config, or a
  test framework/test runner. Private repo; the human won't spend Action minutes or run
  suites. Verify by **running the game yourself** via the env-gated harness (below),
  launched by hand. If a check can't run in this sandbox, describe it for the human —
  don't wire it to run automatically.
- **Share screenshots in chat.** The human follows progress visually. Send every
  screenshot into the thread with `SendUserFile` for any notable visual change.
- **Full autonomy — no human review, ever.** Build, commit, and **merge to `main`**
  yourself. A PR is optional (a review surface for you/the agent team); if it's green
  (Release build clean + headless self-tests + autoplay pass), merge it. Never block on
  "should the human look first?" — the answer is always no. The only hard gate is the
  constraints in this file (above all: **NO CI**).
- **Always ship compiling code to `main`.** Before merging: (a) builds clean in Release,
  (b) passes the headless autoplay smoke test.
- **Keep the docs current** — but put things where they belong (see **Documentation
  map**). CLAUDE.md stays lean; process notes go to `docs/DEVLOG.md`.
- **Don't ask "should I continue?"** Pick the top open item, build it, verify, commit +
  merge, move on — until a real stopping point (context rot, a milestone worth a handoff,
  or something only the human can do, e.g. audio on a real device).

---

## Documentation map (where things go — read this before you write docs)

**Not everything goes in CLAUDE.md.** It ballooned to ~2,500 lines because every
session appended feature lists and blow-by-blow notes here. Keep it lean by routing
content to the right home:

| Doc | Purpose | What goes here |
|-----|---------|----------------|
| **`CLAUDE.md`** (this file) | Lean continuity contract | Project brief, pillars, hard rules, build/run/test, architecture map, key gotchas, doc map, a **short** current-state pointer. |
| **`docs/DESIGN.md`** | Rationale contract — the *why* | Game-design principles, self-assessment, feel/information-design decisions (incl. fog-of-war deferral), the visual style guide (§3.H). |
| **`docs/ROADMAP.md`** | History + open work | Completed Phase 1–5 items (provenance) and the current open/next polish backlog. |
| **`docs/FEATURES.md`** | Feature inventory | The exhaustive "what's built" reference (systems, enemies, objectives, UX). |
| **`docs/DEVLOG.md`** | Curated process log | Per-program/sprint goals, who did what, review/QA outcomes, measured balance, gotchas. **Session write-ups go here, not CLAUDE.md.** |
| **`docs/DEVLOG-ARCHIVE.md`** | Raw migrated WIP notes | The old CLAUDE.md WIP-NOTES blob, kept verbatim for provenance. Append here only if a note doesn't fit the curated DEVLOG. |
| **`docs/AUDIT-2026.md`** | Standing audit | The independent-auditor findings that drive balance priorities. |
| **`docs/DISTRIBUTION.md`** | Shipping contract | How to publish, the measured publish matrix, the `PublishTrimmed` hazard, third-party licence status, the open root-LICENSE decision, and where player data lives. |

**Rule of thumb:** if it's *what to do next* → ROADMAP; *why the game is this way* →
DESIGN; *what happened in a session* → DEVLOG; *what exists* → FEATURES. CLAUDE.md
only gets a fact when a **fresh session needs it to start working.**

---

## Build / run / test

**This container is ephemeral and starts WITHOUT the .NET SDK.** First thing in a new
session:

```bash
bash scripts/dev-setup.sh          # installs dotnet 8 SDK + Xvfb + software GL
export PATH="$PATH:/usr/lib/dotnet"
```

Normal build/run (run needs a real display — fine on the human's machine):

```bash
dotnet build -c Release
dotnet run   -c Release
```

**Headless verification in this sandbox** (no display) uses Xvfb + llvmpipe and an
env-gated harness baked into `Program.cs`:

```bash
export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe

# Screenshot a frame -> sightline_shot.png  (then Read it to inspect visuals)
SIGHTLINE_SHOT=90 xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug

# Full-match autopilot smoke test -> prints "RESULT: WIN|LOSE|TIMEOUT mission=N"
SIGHTLINE_AUTOPLAY=1 xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug

# Start on a specific mission (objectives come from the campaign map per-node;
# baseline rotation Elim/Hack/Evac/Escort/Sabotage/Rescue/Defend/Decap via Run.ObjectiveFor, (n-1)%8).
# Force one with SIGHTLINE_OBJ=sabotage. Combine with SHOT or AUTOPLAY, e.g.:
SIGHTLINE_MISSION=2 SIGHTLINE_SHOT=80 xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug
```

**Harness isolation (house procedure — export these in EVERY shell).** The persistence
self-tests stash-and-restore the real user-data dir and `SIGHTLINE_BALANCE` writes a
shared `/tmp/balance.json`, so parallel agents corrupt each other's runs without it:

```bash
mkdir -p "$PWD/.xdg"                       # must EXIST: an absent dir makes GetFolderPath
export XDG_CONFIG_HOME="$PWD/.xdg"         # return "" and saves land in a relative ./Sightline
export SIGHTLINE_BALANCE_JSON="$PWD/balance.json"
```

Run autoplay a few times (RNG varies); confirm **no exceptions and no TIMEOUT**. The
contract is "no exceptions, no TIMEOUT" — *not* a win, and not a loss either. Measured over
15 Debug autoplays (F1): 3 WIN / 12 LOSE, finale reached on 5, earliest death mission 1,
zero TIMEOUTs. **A WIN is normal, not suspicious.** `sightline_shot.png` is gitignored;
`docs/screenshot.png` (README image) is committed.

**Self-tests & measurement:** many features ship a window-free `SIGHTLINE_*TEST` hook
(e.g. `COMBATTEST`, `SAVETEST`, `AITEST`, `ITEMTEST`, `STACKTEST`) that prints `PASS/FAIL`, and there
are `SIGHTLINE_*` screenshot hooks per feature. The `SIGHTLINE_BALANCE=<N>` flywheel runs
N headless campaigns and reports win-rate/decision-richness/policy-gap. A fuller (but
non-exhaustive) list of hooks is scattered through `docs/DEVLOG.md`; grep `Program.cs`
for `SIGHTLINE_` for the authoritative set.
**Reference timings** (this container; the whole suite is `bash scripts/qa-sweep.sh --full`,
~2 min 40 s, which is the mode to run before merging):

| | |
|---|---|
| `dotnet build -c Release`, no-op | 1.5 s (≈12 s after touching one source file) |
| one self-test, Release binary directly | 0.1–0.4 s |
| one self-test via `xvfb-run dotnet run -c Debug` | 1–2 s |
| `SIGHTLINE_PAIRTEST=1` | 38 s |
| `SIGHTLINE_AUTOPLAY=1` (Debug) | ~22 s |
| `SIGHTLINE_BALANCE=10` (Release binary, **under xvfb**) | 311 s (~31 s/slot) |

**Free keys** (nothing is bound to them — check here before adding a shortcut):
**`I J N O P Q U V Z`**. Bound today: `A B C D E F G H K L M R S T W X Y`, `1`–`9`, the
arrows, Tab/Space/Enter/Escape/Backspace/F2/Kp+/Kp−.

**Distribution** (publishing a build, the licence position, where saves live, and the
`PublishTrimmed` hazard): [`docs/DISTRIBUTION.md`](docs/DISTRIBUTION.md) +
`bash scripts/publish.sh`.

**Self-tests & measurement:** 41 features ship a window-free `SIGHTLINE_*TEST` hook
(e.g. `COMBATTEST`, `SAVETEST`, `AITEST`, `ITEMTEST`) that prints `PASS/FAIL`, and there
are `SIGHTLINE_*` screenshot hooks per feature. `bash scripts/qa-sweep.sh --full` runs all
41 plus autoplay ×3 and is the pre-merge gate; without `--full` it skips the 38 s PAIRTEST.
The `SIGHTLINE_BALANCE=<N>` flywheel runs N headless campaigns and reports
win-rate/decision-richness/policy-gap — **it needs a display**, so run it under `xvfb-run`;
without one it silently reports `runs=0`. A fuller (but non-exhaustive) list of hooks is
scattered through `docs/DEVLOG.md`; grep `Program.cs` for `SIGHTLINE_` for the
authoritative set.

---

## Architecture (file map)

```
Sightline.csproj     net8.0, Nullable disabled, Raylib-cs 8.0.0
src/
  Program.cs    entry + window loop + env-gated test harness
  Game.cs       state machine, input, turn flow, overwatch, AI staging (4707 lines)
                (`partial`; slices in Game.*.cs: Autopilot/Harness/Endless/Meta/Modes/Codex)
  Game.Autopilot.cs  SmartStep/AutoStep balance + smoke-test AI (headless-only)
  Game.Harness.cs    every Debug*/*SelfTest env-gated hook (headless-only)
  Grid.cs       tiles, line-of-sight (Bresenham), cover queries, 8-dir Dijkstra
  Unit.cs       Unit + Weapon + enums (Team/WeaponKind); per-weapon range curves
  Combat.cs     ComputeOdds (hit/crit/dmg) + Resolve (rolls a shot)
  Ai.cs         enemy planner: score reachable tiles for cover+LoF, flank, finish
  Anim.cs       Anim base; MoveStepAnim, ShotAnim (tracer+impact), WaitAnim
  Fx.cs         particles, floating combat text, screen shake, ambient atmosphere
  Renderer.cs   board, cover (faux-3D), units/silhouettes, overlays, aim reticle
  Hud.cs        top/bottom bars, action buttons (rects hit-tested by Game), tooltip,
                banner, intro/win/lose cards, barracks/shop/campaign-map screens
  Util.cs       Cfg (layout consts), Pal (palette), Util (math/rng/easing/tile<->px)
  Maps.cs       hand-authored ASCII arena templates (Mission stamps them in)
  Run.cs        persistent campaign run (squad, map, intel, boons, ...)
  Meta.cs       cross-run persistent profile (salvage, achievements, unlocks)
  Events.cs     between-mission field-event nodes
  Codex.cs      in-game field-manual content
  SaveGame.cs   run save/load (System.Text.Json, user-data dir)
  Audio.cs      procedural SFX + music (device-free-safe)
  Display.cs    render-target, post-FX shader, brightness/colorblind, settings
  Stats.cs      SIGHTLINE_BALANCE analytics harness
scripts/dev-setup.sh   sandbox setup
scripts/qa-sweep.sh    all 41 self-tests + autoplay x3 (--full adds PAIRTEST)
scripts/publish.sh     hand-run distributable build + persistence re-proof
THIRD-PARTY-NOTICES.txt  raylib/Raylib-cs (Zlib) + .NET (MIT); copied to build output
docs/screenshot.png    README image
```

### How a turn flows
- `Phase`: Intro → PlayerTurn ⇄ EnemyTurn → Win/Lose.
- An **animation queue** (`_anims` in Game) gates interactivity: while non-empty, input
  is locked and anims play one at a time. `IsPlayerInteractive()` = player turn + empty
  queue. **`Anim.OnStart` runs when an anim becomes ACTIVE (first frame it's `_anims[0]`),
  NOT at enqueue** — otherwise every queued step of a multi-tile path captures its `_from`
  at the original tile and the unit snaps back each step (the old movement-jitter bug).
  Don't call `OnStart` in `Enqueue`.
- Player actions (move/shoot/overwatch/hunker/reload) → enqueue anims.
- Movement is per-tile `MoveStepAnim`s; on each tile entry `Game.OnUnitEnteredTile` checks
  **overwatch** and injects reaction `ShotAnim`s at the front.
- Enemy turn is staged in `UpdateEnemy` (PickNext → ActAfterMove) using `Ai.Plan`; it
  enqueues the same anims, so overwatch/feel are shared.
- `KillUnit` purges a dead unit's queued moves and spawns death FX.

### Combat model (tuning lives in code)
- Hit% = aim + weapon.AimBonus + weapon.RangeMod(dist) − cover.Defense (− hunker),
  clamped 3..95. Cover: low −20 / high −40. Flanked = had adjacent cover but not
  protecting from this angle → exposed (+crit). `Grid.GetCover`: a cardinal attack's
  facing side covers fully; a **diagonal** attack: a TRUE corner (cover on BOTH facing
  sides) = full cover (weaker level); a single facing-side cover = **half cover at range**
  (`CoverInfo.Partial`) but **no cover when adjacent** (point-blank diagonal slips past the
  corner → flank). High ground negates the target's LOW cover. Verified by
  `SIGHTLINE_COMBATTEST`.
- Each `WeaponKind` has its own `RangeMod` curve + `MaxRange` + clip + crit base.
- **Thirteen persisted-by-ordinal enums** (Objective, WeaponKind, Perk, WeaponMod, Trait,
  Boon, SecondaryKind, Faction, Spec, Scar, Contract, MetaUnlock, RewardKind) are
  **APPEND-ONLY** — the ordinal IS the save format. `SIGHTLINE_SAVETEST` pins each one with
  a golden fingerprint over its full ordinal→name mapping (`SaveGame.PersistedEnums`), so a
  reorder, removal, rename **or mid-enum insertion** fails loudly. **To add a member:**
  append it at the END, run SAVETEST, paste the "actual" hash it prints into
  `PersistedEnums`. Anything other than an append is a save-format break.

---

## Raylib-cs 8.0 gotchas (learned the hard way)
- **Raylib's default exit key is ESC** — it sets `WindowShouldClose()` and quits before
  our handling runs. We call `Raylib.SetExitKey(KeyboardKey.Null)` after `InitWindow` so
  ESC instead cancels aim/grenade targeting and opens the pause menu. Don't remove it.
- Construct colours via `Pal.RGBA(r,g,b,a)` (casts to byte) — don't rely on int Color ctors.
- `DrawRectangleRoundedLines` signature is version-volatile; **avoid it**. Use
  `DrawRectangleLinesEx` (square) or a manual highlight line.
- Enums are PascalCase, unprefixed: `KeyboardKey.One/Two/Three/R/Tab/Enter/Escape`,
  `MouseButton.Left/Right`, `ConfigFlags.Msaa4xHint`.
- Screen shake draws the board inside a `Camera2D` whose `Offset = Fx.ShakeOffset`; HUD is
  drawn outside the camera.
- `DrawPoly`/`DrawPolyLinesEx` are safe for glyphs (winding handled internally); be careful
  with raw `DrawTriangle` winding.
- **Text goes through `Cfg.Text` / `Cfg.Measure`, never `Raylib.DrawTextEx` directly.** Two
  NotoMono atlases are baked (20px for sizes ≤ `Cfg.UiFontMax` = 18, 64px above); `Cfg.FontFor`
  picks. Titles ≥24px use the Chakra Petch display face via `Cfg.TitleText`/`Cfg.TitleMeasure`;
  numerals/data stay on NotoMono. **12px is the small-text floor.** When a call site *measures*
  through `Clip`/`WrapText`/`WrapLines`/`CenterText` and *draws* separately, the two sizes must
  match or the text wraps at one size and paints at another.
- **Bundled assets resolve via `Cfg.AssetPath(rel)`** (`AppContext.BaseDirectory`, cwd fallback),
  never a bare relative path — a binary launched from another directory otherwise silently loses
  the font. Never publish with `-p:PublishTrimmed=true`: it destroys save/load while the game
  still boots.
- **Headless byte-stability:** the screenshot harness keeps `Display` (post-FX) OFF and
  never touches disk/meta (gated by `NoPersist`), so shots stay byte-identical and the
  balance flywheel is reproducible. Keep new persistent/random/post-FX work behind those
  gates.
- **Headless determinism — what is actually guaranteed.** The harness keeps `Display`
  (post-FX) OFF and never touches disk/meta (gated by `NoPersist`), and the flywheel
  reseeds explicitly (`Util.Reseed(50000+slot)`), so **paired measurement** is
  reproducible. **Screenshots are NOT byte-identical** and never were: two
  `SIGHTLINE_SHOT=90` runs measurably differ in ~30% of pixels (measured 303,065 of
  1,024,000 px), because 58 `Raylib.GetTime()` wall-clock reads drive animation
  (46 in `Renderer.cs`, 12 in `Hud.cs`) and `Util.Rng` is clock-seeded by default. Never
  gate anything on a screenshot hash — **`SIGHTLINE_PAIRTEST` byte-identity is the real
  determinism gate**. Keep new persistent/random/post-FX work behind the `NoPersist`/
  Display gates so that stays true.

---

## Current state (short)

> **Keep this section SHORT.** It is a pointer for a fresh session, not a changelog.
> Per-program detail belongs in `docs/DEVLOG.md`; what exists belongs in `docs/FEATURES.md`.
> (It had grown to ~40 lines of accreted program summaries again; RESONANCE cut it back.)

Playable and feature-complete: four modes (campaign / endless / skirmish / daily), a cross-run
meta profile, a deep per-run loop, 8 objectives, ~21 enemy archetypes, 35 arenas, and a full
juice/audio/post-FX layer. Builds 0 warn / 0 err; `bash scripts/qa-sweep.sh --full` runs all
self-tests and must be green; autoplay must never TIMEOUT or throw.

Nine autonomous programs (through **FULCRUM**, closed 2026-08-28) built and balance-tuned the
game. The reference heat ladder and goal band of record are in `docs/DEVLOG.md` §FUL-13.

**PROGRAM RESONANCE** (current) is the tenth. Its thesis: the game had been tuned far past the
point where anyone verified how it actually *lands*. It found and fixed several things nine
win-rate-driven programs could not see — the audio had never been heard by anyone (a one-line
filter bug meant every weapon was raw white noise), the board's biome identity was erased by a
move overlay that flooded it, only 3 of ~14 verbs were ever taught, the displayed hit% was not
the hit probability, a pod scatter bug stacked units on one tile so the buried one could not be
clicked, and a published build launched from the wrong directory silently lost its font. Detail
in `docs/DEVLOG.md` §RESONANCE; open work in `docs/ROADMAP.md`.

**Three doc over-claims were found and corrected** — they are the reason this project needs the
"no over-claims" rule enforced hard: juice was graded "Strong" partly on audio nobody had heard;
onboarding was graded "Addressed" when 12 of 14 verbs were untaught; and a published
`meaningful-choices/turn = 6.15` measured **2.25** on a fresh batch. **Do not cite a number you
have not just re-measured.**

## Handoff protocol (when context gets heavy)
You judge when context rot risks quality (don't wait for the 1M hard limit). Before stopping:
1. Make sure `main` builds and passes autoplay.
2. Update the relevant doc — **`docs/ROADMAP.md`** checkboxes and **`docs/DEVLOG.md`** for
   what you did this session. Refresh the short "Current state" here only if the one-paragraph
   summary is now wrong.
3. Leave mid-flight notes in `docs/DEVLOG.md` (what you were doing, the next concrete step,
   any gotcha) — **not** as a growing blob in CLAUDE.md.
4. Tell the human to open a fresh session (they'll send only `.`).
