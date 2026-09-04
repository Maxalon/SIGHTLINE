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
- **Asset policy — RELAXED by the owner, 2026-08-29.** The old hard line ("no hand-made /
  human-authored art or audio") is **gone**. The aesthetic is still geometry + particles +
  shaders + screen shake and audio is still synthesised — that is a *style* choice we keep
  because it works, not a rule. **Third-party assets are now allowed** provided BOTH bars are
  cleared: (1) **zero cost** — nothing that costs money now or to distribute, ever; and
  (2) **zero legal risk** — the licence must permit free use *and redistribution in a shipped
  build*, in any context (CC0 / public domain / OFL / MIT-class). Record every added file and
  its source + licence in `assets/*/CREDITS.txt` **and** `THIRD-PARTY-NOTICES.txt`. The audio
  loader is already **file-first**: dropping `<cue-id>.ogg` into `assets/sfx/` overrides that
  cue's synth with no code change (`assets/sfx/CREDITS.txt`); `assets/music/{ambient,combat}.ogg`
  does the same for the beds. **Sandbox reality (measured 2026-08-29): the agent proxy BLOCKS
  freesound.org and opengameart.org (403). `raw.githubusercontent.com`, `api.github.com` and
  `nuget.org` DO resolve.** So free audio is largely unobtainable *from inside a session* — do
  not spend a wave discovering that again. Procedural/in-engine remains the default because it
  is the only path that always works here. Commit **small** files only; keep large binaries out. Raylib bakes noise/gradient textures (`GenImage*`)
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
# NOTE: the mission BRIEFING card holds for 11 s and covers the middle of the board, so any
# frame under ~700 photographs the card, not the board. Use SIGHTLINE_SHOT=760 for board shots.
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

> **FOURTEEN self-tests write the LIVE player-data directory, and `SIGHTLINE_SHIPTEST` also writes
> it FROM A SECOND PROCESS** (C6). The second process is what is unique to SHIPTEST — "quit the
> game, start it again, your progress is there" cannot be checked inside one process, and
> `Ship.cs` holds the project's only `Process.Start`. **The first half is not unique to it and never
> was**, which is why the isolation below is not optional: measured on this tree (P13, base
> `a933cfe`, one FRESH empty `XDG_CONFIG_HOME` per hook; 81 hooks enumerated from `src/`, 4 slow
> ones skipped, **77 measured**) **14 write there** — BRIEFTEST, CONTRASTTEST, EVENTTEST, HORDETEST, METATEST, MODETEST, ONRAMPTEST,
> QUITTEST, SAVEEDGETEST, SAVETEST, SETTINGSTEST, SHIPTEST, TUTTEST, VETTEST. **Re-derive it, don't
> quote it** — every writer calls `Directory.CreateDirectory` before it writes, so a hook that
> writes leaves the directory behind even after it restores:
> `for h in $(grep -ohE 'SIGHTLINE_[A-Z0-9_]+(TEST|GATE|PROBE)' src/*.cs | sort -u); do d=$(mktemp -d); env XDG_CONFIG_HOME=$d $h=1 xvfb-run -a bin/Release/net8.0/Sightline >/dev/null 2>&1; [ -d "$d/Sightline" ] && echo "$h WRITES"; rm -rf $d; done`
>
> Each of them moves your files aside and puts them back. **P13 made that restore safe**: the six
> that hand-rolled it — SETTINGSTEST, TUTTEST, SAVEEDGETEST, QUITTEST, BRIEFTEST, ONRAMPTEST — now
> go through `SaveGame.StashForSelfTest` / `RestoreForSelfTest` (rename out, rename back), and
> SAVETEST's three in-file restores go through `WriteAtomic`. The old
> `if (had) File.WriteAllText(path, stash)` wrote **ZERO BYTES** over the file it was protecting
> whenever `Exists()` had said yes and the read then threw.
> `SIGHTLINE_SETTINGSTEST` leg (S) is the gate. **Four writers still hand-roll a null-guarded
> truncating restore** — HORDETEST (`Game.Endless.cs`), MODETEST (`Game.Modes.cs`), the WAR ROOM
> leg (`Game.Meta.cs`) and two `Program.cs` shot paths. They cannot zero a file (the null guard is
> there), but they truncate in place; `docs/ROADMAP.md` carries the conversion.
>
> SHIPTEST stashes and restores on the way out —
> verified byte-identical, including any `.tmp` siblings it had to clobber — and the child is
> bounded. But a sweep **killed mid-SHIPTEST** can leave `4242` salvage and a `C6_SECOND_LAUNCH`
> achievement in whatever profile `XDG_CONFIG_HOME` points at. Export the isolation above and it is
> your worktree's throwaway `.xdg`, not your real one.
>
> Two file names that mean "a self-test died holding your data", and what to do:
> `<name>.json.selftest-stash` — **your profile, whole**; rename it back over the original.
> `<name>.json.tmp` — a write that never landed; the original is untouched, so just delete it.
> A THIRD name in that directory is not debris: `crash-<utc>-<pid>.txt` is a **crash report** (P11),
> the file a player attaches to a bug report. Newest 5 kept, 64 KB each, 3 per launch. It is written
> by the real handler, never by a self-test — `SIGHTLINE_CRASHTEST` redirects to a temp dir and
> proves it did (leg g diffs the real directory before/after). docs/DISTRIBUTION.md §6.

Run autoplay a few times (RNG varies); confirm **no exceptions and no TIMEOUT**. The
contract is "no exceptions, no TIMEOUT" — *not* a win, and not a loss either. **W9 THE REPAIR made
that contract TRUE rather than merely claimed**: it was violated at ~1% per run by two independent
causes (a per-mission stall cap that the checkpoint redeploy re-armed under a whole-campaign frame
budget, and a within-turn DEADLOCK the turn-boundary guard structurally could not see). The
backstop is now run-scoped (`Game.AutoMaxRunTurns`) plus a within-turn idle guard — and, since C5,
plus `Game.EnemyStallGuard`, which covers the ENEMY turn (W9's idle guard is player-turn only) and
runs in real play, not just autoplay: it fires from `Update` before the animation pump, prints the
stalled unit / stage / planner branch, and forfeits that unit so the turn ends. The harness
frame budget is `Game.AutoFrameCap` (raised 20000 -> 120000 — the old value right-censored the
longest campaigns as losses in the BALANCE batch), and `SIGHTLINE_STALLTEST` asserts all of it.
A `RESULT: TIMEOUT` today is a real regression, not a flake. **Note it cuts BOTH ways for anyone
re-measuring:** the frame cap loosened, but `AutoMaxRunTurns` is a NEW force-lose arm that is
STRICTER than the old per-mission `_turnCount > 50` for a long campaign — the change is a
loosening and a tightening in one. Measured over
15 Debug autoplays (F1): 3 WIN / 12 LOSE, finale reached on 5, earliest death mission 1,
zero TIMEOUTs. **A WIN is normal, not suspicious.** `sightline_shot.png` is gitignored;
`docs/screenshot.png` (README image) is committed.

**Reference timings** (this container; the whole suite is `bash scripts/qa-sweep.sh --full`,
~2 min 40 s, which is the mode to run before merging):

| | |
|---|---|
| `dotnet build -c Release`, no-op | 1.5 s (≈12 s after touching one source file) |
| one self-test, Release binary directly | 0.1–0.4 s |
| one self-test via `xvfb-run dotnet run -c Debug` | 1–2 s |
| `SIGHTLINE_PAIRTEST=1` | 38 s |
| `SIGHTLINE_AUTOPLAY=1` (Debug) | ~22 s |
| `SIGHTLINE_BALANCE=10` (Release binary, **under xvfb**) | **~5 s** (was 311-509 s before W1) |

> **W1: the batch stopped rendering and the numbers did not move.** The headless loops used to
> run a full llvmpipe frame per SIMULATED frame purely to pump the window event queue; they now
> call `Raylib.PollInputEvents()`. **The controlled speed-up is 17-33x, median ~23x** — same
> binary, same seed, identical frame counts, only the GL work removed
> (`docs/measurements/w1/autoplay_ab.txt`, four measured pairs; the spread is container load on a
> shared four-core box, not the change). The batch table above shows a larger ratio, but those
> rows ran on different binaries at unrecorded load — quote the controlled range, not that.
> Inertness is proven by a balance JSON **identical on every key once the `harness{}` block is
> excluded** — that block records nproc/loadavg/elapsed and is designed to vary, which is why
> `inert_diff.sh` strips it. `SIGHTLINE_BALANCE_DRAW=1` restores the old path.

**Free keys** — **DO NOT TRUST THE LIST BELOW; DERIVE IT.** The 2026 audit (wildcard-4)
found four of the nine letters this line advertised as free were already bound (N/P/U/V),
and the duplicate registry at `src/Game.cs` ("Free letters remaining…") disagreed with it.
Run `grep -ohE 'KeyboardKey\.[A-Z][a-z0-9]*' src/*.cs | sort -u` before binding anything.
As of wave SETTINGS EVERYWHERE the genuinely free letters are **`I J Z`** — W5 bound **Q** (QUIT TO
DESKTOP, pause card + main menu) and SETTINGS EVERYWHERE bound **O** (the main menu's SETTINGS
door). Everything else is claimed somewhere.

**Distribution** (publishing a build, the licence position, where saves live, and the
`PublishTrimmed` hazard): [`docs/DISTRIBUTION.md`](docs/DISTRIBUTION.md) +
`bash scripts/publish.sh`.

**Self-tests & measurement:** every feature that can be checked headlessly ships a
`SIGHTLINE_*TEST` hook (e.g. `COMBATTEST`, `SAVETEST`, `AITEST`, `ITEMTEST`) that prints
`PASS/FAIL`, plus `SIGHTLINE_*` screenshot hooks per feature. `bash scripts/qa-sweep.sh --full`
runs every one of them plus autoplay ×3 and is the pre-merge gate; without `--full` it skips the
38 s PAIRTEST.
**DO NOT WRITE A COUNT HERE.** It has been wrong six times — 41 / 46 / 49 / 51 / 53 each claimed
while a different number ran, and then two successive "derivations" that were themselves wrong.
The sweep derives both halves at runtime *from env-var NAMES rather than line shapes* (the last
break was a counter keyed on a line shape that a routing change invalidated), prints them in its
footer, and its **COVERAGE GUARD** block — which names any hook in `src/` the sweep never invokes
— is the real check. Run it and read the last lines. **The guard had two holes of its own until
PARALLAX, and both are the kind that make it go quiet rather than loud:** its alphabet was
`(TEST|GATE)`, so `SIGHTLINE_FUL11PROBE` — a real assertion hook the sweep really runs — was in
neither count and any future `*PROBE` was invisible by construction; and the "is it run?" side
grepped the WHOLE script, so merely NAMING a hook in a comment there marked it covered. The
alphabet is now `TEST|GATE|PROBE` on both sides over NON-COMMENT lines, with a named
`_SWEEP_EXEMPT` list for report-shaped probes (`BANDPROBE` today). **If you add a hook whose name
does not end in TEST, GATE or PROBE, the guard cannot see it** — end it in one of those three.
**Since W9 the sweep was SUPPOSED to EXIT NON-ZERO** on any FAIL line, a non-empty COVERAGE GAP, a
TIMEOUT or a missing RESULT line — **and until CONTOUR C3 it did not: the script accumulated
`_fail`/`_autofail` and then ended on an `echo`, so its status was always 0.** C3 found it when its
own new self-test FAILED inside a `--full` sweep that still reported `SWEEP-EXIT=0`, and wired the
`exit`. Every green-sweep claim in this repo dated before C3 was quoting the exit code of an echo;
the PASS/FAIL lines were real, the machine-checkable gate was not. **It is a gate now — check the
code, and if you add a self-test line route it through `verdict` or it is invisible to it.**
The `SIGHTLINE_BALANCE=<N>` flywheel runs N headless campaigns and reports
win-rate/decision-richness/policy-gap — **it needs a display**, so run it under `xvfb-run`;
since W1 a display-less batch REFUSES, writes nothing and exits 2 (see the contract below — it
does NOT report `runs=0` any more, and it leaves any stale JSON untouched). A fuller (but
non-exhaustive) list of hooks is scattered through `docs/DEVLOG.md`; grep `Program.cs` for
`SIGHTLINE_` for the authoritative set.

**The `SIGHTLINE_BALANCE` measurement contract (X2 — do not shortcut any of it):**
1. `SIGHTLINE_BALANCE=<N>` **requires `xvfb-run`.** Since W1 a display-less batch REFUSES —
   it prints `BALANCE: no display - run under xvfb-run. No data written.` on stderr, writes
   nothing, and **exits 2**. Before W1 it printed `runs=0 / (no data)`, still claimed N matches,
   still overwrote the previous chunk's file with zeroes, and exited 139.
   **THE ASSERTION CHANGED WITH IT — read this before copying an old chunk script.** The refusal
   deliberately leaves the previous chunk's file byte- and mtime-identical, so a bare
   `json.load(out)['runs'] == 2N` check now reads the PREVIOUS chunk's `runs` and prints OK for a
   batch that measured nothing. Pre-W1 that same check worked *because* the file got clobbered
   with `runs=0`. So a chunk script MUST do both, in this order:
   **(a) `rm -f` the target JSON before launching** — this is now mandatory, not tidiness; it is
   what makes a missing file mean "no data". **(b) check the process EXIT CODE** — 2 means "no
   display, nothing written", and it is the only signal that cannot be faked by a stale file.
   The `runs`-field assertion stays as a third line of defence (it still catches a short batch).
   **P15 amended layer (b) and REPLACED layer (c). Copy `docs/measurements/p15/run_chunk.sh`, NOT
   `w1/`'s or `c1/`'s** (both now carry a SUPERSEDED header; they are kept unchanged as provenance).
   * **exit 3 is a SECOND refusal**: an unparseable `SIGHTLINE_BALANCE` / `_HEAT` / `_BASE`. The
     batch prints what it could not read and writes nothing. Before P15 those fell through a bare
     `int.TryParse` **silently** — a typo'd `-h4` cycled `{0,2,4,6,8}` and was archived under the
     rung in its FILE NAME with `runs` correct, the file fresh and exit 0.
   * **layer (c) must not hard-code `runs == N*2`.** All fourteen archived runners do, which marks
     every legitimate single-policy batch (`SIGHTLINE_BALANCE_SLOPPY` / `_DUMB`) BAD. The artifact
     now carries a **`batch{}`** block — the request as issued, including `expectedRuns`,
     `heatRequested`/`heat` and `baseRequested`/`slotBase`. `p15/check_chunk.py` asserts `runs`
     against `batch.expectedRuns` and the rung/base against what the runner exported, so a chunk's
     FILE NAME is checkable. An artifact with no `batch{}` is pre-P15 and is `BAD` unless the
     caller passes `--legacy` explicitly.
   * `runWinRate` is **-1** for a batch with no campaign runs (it used to read 0.0, i.e. "lost
     every campaign"), matching `runWinRateExStalemate`'s existing sentinel.
   * **`l5/cluster.py`'s LEAK-CHECK is a gate again.** It used to downgrade to a note and exit 0 if
     ONE chunk in a pinned round ran unpinned. A MIXED round is now a FAIL; an all-unpinned round
     prints `NOT PERFORMED`.
2. Run the **Release binary directly**, and from a *snapshot* (`runbin/<tag>/`, gitignored) so
   the tree can keep building while a round is in flight.
3. Two disjoint CRN slot sets (`SIGHTLINE_BALANCE_BASE` 0 / 10) x greedy+sloppy = 40 campaigns
   per rung. Pin `XDG_CONFIG_HOME` and `SIGHTLINE_BALANCE_JSON` per chunk — several agents
   share the container. **A 40-campaign rung carries ±6-8 points**: differences smaller than
   that are not results.
4. **One lever per round**, always against a FRESH same-slot baseline on the same instrument;
   if the tree gained instrumentation, prove logic identity with an `R0diag` chunk first.
5. Archive every chunk's JSON + log under `docs/measurements/<wave>/` with a README giving the
   exact command lines — **and state the base commit.**
6. **P15: MISSION-LEVEL tables from before 2026-09-03 are survivorship-biased and are NOT
   comparable with anything measured since.** `Stats.BeginMission` overwrote the open `MissionRec`,
   so the mid-run checkpoint redeploy ERASED the attempt it retried: over the 421 archived chunks
   that carry a `campaigns[]` array (11,320 campaigns, 39,143 missions) there is **not one
   non-terminal mission loss** — mission losses equal campaign losses exactly, 8,252 = 8,252. The
   erased attempt is now filed as `REDEPLOYED` (`SIGHTLINE_MISSIONFLUSH=0` restores the drop).
   Priced CRN-paired at three rungs (`docs/measurements/p15/`): **every campaign-level field is
   identical** — `campaigns[]`, `pairedPolicy`, `runWinRate`, `policyGap` — so **the L5 ladder of
   record stands untouched**; but per-mission win rate falls up to 23 points on mid-run missions
   and `meaningfulChoicesPerTurn` by 5-12%. Missions 1-2 do not move at any rung, because the
   checkpoint valve does not open before mission 3.

---

## Architecture (file map)

```
Sightline.csproj     net8.0, Nullable disabled, Raylib-cs 8.0.0
src/
  Program.cs    entry + window loop + env-gated test harness
  Game.cs       state machine, input, turn flow, overwatch, AI staging (the biggest file;
                DON'T write a line count here - the last one sat at 4707 while the file
                held 8563, and a stale number is worse than none. `wc -l src/Game.cs`.)
                (`partial`; slices in Game.*.cs: Autopilot/Harness/Endless/Meta/Modes/Codex)
  Game.Autopilot.cs  SmartStep/AutoStep balance + smoke-test AI (headless-only)
  Game.Harness.cs    every Debug*/*SelfTest env-gated hook (headless-only)
  Game.Modes.cs      the non-campaign modes' setup/end: SKIRMISH, the seeded DAILY, TRAINING
  Game.Endless.cs    LAST STAND (endless horde) wave state + its self-test
  Game.Meta.cs       the WAR ROOM / cross-run profile screen's state + input
  Game.Codex.cs      the FIELD MANUAL's in-game paging state
  Game.Audition.cs   the AUDIO CHECK screen's state + input (Game.HandleAudition)
  Mission.cs    THE ONE ENEMY FUNNEL: Build/SpawnEnemies, MakeHostile (HostileToughness /
                HostileDamageTrim - see "Combat model"), OpenerTrim, the deployment shapes,
                pods and the per-objective board furniture. Every hostile in the game is
                built here; nothing else may construct one.
                P20: Build CLEARS the biome ground layer at its top (`Mission.ClearGroundOnBuild`).
                It wipes Tiles/Height/Smoke there and used not to wipe Ground, but it READS Ground
                through Grid.IsFloor / Grid.CostMap (TryApplyLayout's accept/reject, SpawnEnemies'
                scatter, PlaceBarrels, EnsureConnectivity) — and this mission's layer does not exist
                yet, because Game.StampBiomeGround runs AFTER Build. So a board was a function of
                the board before it. SIGHTLINE_STALEGROUND=1 restores that; never ship it on.
  Voice.cs      the squad's radio barks (line pools + the cooldown/priority picker)
  Grid.cs       tiles, line-of-sight (Bresenham), cover queries, 8-dir Dijkstra.
                EIGHT per-tile arrays (Tiles/Height/Smoke/CoverHp/CoverSeed/Fire/Barrel/Ground).
                L6: SIX are cleared by `Mission.Build` itself and leak nothing into its own
                terrain decisions; **Fire and Barrel are cleared by the CALLER**
                (`Game.SetupMission` -> `Grid.ClearHazards()`, the only production caller), and
                `Barrel` is read by `IsFloor`, so a stale one would move the board exactly as the
                stale Ground did. MODETEST leg (14a-2) gates the six at the Build seam and (14b)
                all eight at the SetupMission seam. Moving the clear into Build is ROADMAP's next
                item — until then, ANY new caller of Mission.Build must clear hazards first.
  Terrain.cs    C4 + P16: the per-tile biome GROUND layer. FIVE of eight biomes are mechanical
                on five axes - VERDANT undergrowth (cover) / TUNDRA ice (movement) / MAGMA
                vents (sight) / VOID RIFT (topology: impassable, TRANSPARENT, no cover) /
                ARID SOFT SAND (drag: a step costs 3 half-tiles, 5 diagonal). STEEL/ASH/NEON
                are paint and BIOMETEST asserts it. Stamped through Hash3 with ZERO Util.Rng
                draws (Stamp is pure; the BOARD also keys on the reserved set, so it is NOT a
                function of (MapSeed,mission) alone). NOT persisted. Read by Grid.GetCover /
                CostMap / HasLineOfSight / IsFloor, so both teams get it from one truth.
                THE RIFT IS THE ONE THAT VALIDATES: StampRift re-floods through Grid.CostMap
                after every candidate tile and reverts any that removes more than itself, so a
                chasm can never seal and the gaps ARE the bridges. SIGHTLINE_RIFTTEST proves it
                on 576 real boards; with the guard off the same sweep strands 69 tiles.
                SIGHTLINE_BIOMEMECH=0 = the pre-C4 board, exactly.
                SIGHTLINE_NEWGROUND=0 = the pre-P16 board, exactly (VOID/ARID paint again) -
                that, NOT BIOMEMECH, is the arm for a round that wants to price P16 alone.
  Unit.cs       Unit + Weapon + enums (Team/WeaponKind); per-weapon range curves
  Combat.cs     ComputeOdds (hit/crit/dmg) + Resolve (rolls a shot)
  Ai.cs         enemy planner: score reachable tiles for cover+LoF, flank, finish
  Anim.cs       Anim base; MoveStepAnim, ShotAnim (tracer+impact), WaitAnim
  Fx.cs         particles, floating combat text, screen shake, ambient atmosphere
  Renderer.cs   board, cover (faux-3D), units/silhouettes, overlays, aim reticle
  Hud.cs        top/bottom bars, action buttons (rects hit-tested by Game), tooltip,
                banner, intro/win/lose cards, barracks/shop/campaign-map screens.
                `Hud.KeyTable` + `Hud.VerbTable` are the ONE row-set behind both the in-game
                FIELD MANUAL and README's controls block (`SIGHTLINE_KEYTABLE=1` regenerates
                the block between README's KEYTABLE markers - never hand-edit it;
                `SIGHTLINE_KEYTABLEGATE` is the gate on both)
  Hud.Audition.cs    the AUDIO CHECK screen's drawing (draw-only; Game.Audition hit-tests it)
  Util.cs       Cfg (layout consts), Pal (palette), Util (math/rng/easing/tile<->px)
  Maps.cs       hand-authored ASCII arena templates (Mission stamps them in)
  Run.cs        persistent campaign run (squad, map, intel, boons, ...)
  Meta.cs       cross-run persistent profile (salvage, achievements, unlocks)
  Events.cs     between-mission field-event nodes
  Codex.cs      in-game field-manual content
  SaveGame.cs   run save/load (System.Text.Json, user-data dir)
  Audio.cs      procedural SFX + music (device-free-safe)
  Audio.CueMap.cs    THE CUE MAP: the injective event->cue table (one meaning, one sound)
  Audio.Analysis.cs  the measured numbers (peak/RMS/length) the AUDIO CHECK screen prints
  Display.cs    render-target, post-FX shader, brightness/colorblind, settings. P17: the
                FIRST-LAUNCH WINDOW FIT (Display.FitLaunchSize — pure, only ever SHRINKS; persisted
                as WinW/WinH). Gated on `Display.AllowLaunchFit`, which defaults to FALSE and is
                set true ONLY by the real launch in Program.RealMain — that is what keeps every
                headless window at exactly Cfg.ScreenW x Cfg.ScreenH.
  Stats.cs      SIGHTLINE_BALANCE analytics harness
  Ship.cs       C6: the DISTRIBUTABLE's contract — version stamp (Ship.Version, off the assembly),
                the bundled-file manifest (Ship.RequiredFiles) and SIGHTLINE_SHIPTEST.
                P17 added the procedural WINDOW ICON (Ship.IconPixels, pure; ApplyWindowIcon) and
                the RELEASE legs (archive / checksum / changelog), gated on SIGHTLINE_RELEASEDIR.
  Crash.cs      P11: the top-level crash handler. `Program.Main` is now nothing but
                `Crash.Guard(...)` around `RealMain` (+ Crash.Install for background-thread
                throws), so **the "MUST STAY FIRST IN Main" SHIPCHILD branch is now first in
                `RealMain`** — keep it there. Writes crash-<utc>-<pid>.txt into the PLAYER-DATA
                dir via SaveGame.WriteAtomic; exits 70. docs/DISTRIBUTION.md §6.
  Crash.SelfTest.cs  SIGHTLINE_CRASHTEST — the only self-test here that deliberately THROWS. It
                redirects Crash.DirOverride to a temp dir, so unlike SHIPTEST it can strand
                nothing; also runs in publish.sh against the published binary.
scripts/dev-setup.sh   sandbox setup
scripts/qa-sweep.sh    every self-test in src/ + autoplay x3 (--full adds PAIRTEST); counts DERIVED
scripts/publish.sh     hand-run distributable build + persistence re-proof + the RELEASE
                       artefact (versioned archive, .sha256, CHANGELOG.md; --tag makes a LOCAL
                       tag and NEVER pushes). docs/DISTRIBUTION.md §8.
scripts/changelog.sh   CHANGELOG.md derived from `git log --first-parent`. Never hand-written.
THIRD-PARTY-NOTICES.txt  raylib/Raylib-cs (Zlib) + .NET (MIT); copied to build output
LICENSE                the project's own terms (all rights reserved); ALSO copied to build output
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
  **overwatch** and injects reaction `ShotAnim`s at the front. **Enqueue a path ONLY through
  `Game.EnqueuePath`** (THE STRIDE): it tags the steps First/Mid/Last and shares the polyline that
  draws one stride per walk; a hand-rolled `foreach (...) Enqueue(new MoveStepAnim(...))` brings
  the per-tile caterpillar back, and `SIGHTLINE_FEELTEST` will not see it unless that path is the
  one it stages.
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
- **THE EXCHANGE (RESONANCE X1):** every hostile is built through `Mission.MakeHostile`,
  which adds `Mission.HostileToughness` (+3 HP) and applies `Mission.HostileDamageTrim`
  (−1 off both ends of its weapon band, via `Weapon.TrimBaseDamage`, which moves the
  PRISTINE base so `ApplyMods` can't undo it). That pair sets time-to-kill (~2 hits both
  ways). Change it only with a measured round per side — enemy-only durability was
  measured at −30 run completion. `Ai.cs`/`Game.Autopilot.cs` compare `Hp` to
  `Weapon.Dmg*`, so they re-price themselves; `HEATLADDERTEST` derives its damage pin
  through the trim.
- **Thirteen persisted-by-ordinal enums** (Objective, WeaponKind, Perk, WeaponMod, Trait,
  Boon, SecondaryKind, Faction, Spec, Scar, Contract, MetaUnlock, RewardKind) are
  **APPEND-ONLY** — the ordinal IS the save format. `SIGHTLINE_SAVETEST` pins each one with
  a golden fingerprint over its full ordinal→name mapping (`SaveGame.PersistedEnums`), so a
  reorder, removal, rename **or mid-enum insertion** fails loudly. **To add a member:**
  append it at the END, run SAVETEST, paste the "actual" hash it prints into
  `PersistedEnums`. Anything other than an append is a save-format break.
- **The MAP GENERATOR is the save format's other half** (W1). A save stores the campaign map as
  ONE int — `MapSeed` — and regenerates the whole DAG from it on load, so `Run.GenerateMap`'s
  DRAW ORDER is as load-bearing as any ordinal: one stray `rng.Next()` re-deals every existing
  save's node kinds, factions and edges and re-points `MapPos` at another mission. SAVETEST pins
  three seeds through `SaveGame.MapFingerprint` / `PersistedGenerators`; a `mapShape:` line means
  the generator moved. A deliberate map change ⇒ paste the printed hashes in **and re-measure**.
  `HEATLADDERTEST` pins the difficulty axis the same way: the cumulative
  `(EnemyDelta, StatDelta, DmgDelta, AiTier)` vector for all ten heat levels. **A CUMULATIVE pin
  cannot see a DEAD ROW** — C1 found rung 6 declaring `AiTier = 1` that could never fire (rung 4
  already published tier 1; the aggregation is `Math.Max`), inert for two programs under a green
  HEATLADDERTEST. `SIGHTLINE_MIDTOOTHTEST` adds the per-rung invariants: no rung may declare a
  value the rungs below already provide, and no rung may leave the cumulative vector unchanged.

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
  the font. **That cwd fallback is also a MASK** (C6): from the source tree it resolves a file the
  `.csproj` forgot to copy off the *repo* instead of off the *build output*, so the build is broken
  for a player and green for you. Anything bundled must be in `Ship.RequiredFiles` **and** the
  `.csproj` copy list; `SIGHTLINE_SHIPTEST` resolves the manifest strictly against
  `AppContext.BaseDirectory` and `scripts/publish.sh` runs it against the published directory,
  which is the only place that leg is testing the artifact a player receives.
- **A `win-*` RID publishes as `WinExe`** (P11) so a Windows player gets no console window behind
  the game — measured on the artifact, by the PE `Subsystem` byte (3 -> 2), which `publish.sh` now
  checks and fails on. It is scoped to Windows RIDs: no RID is set for the Linux build, the Debug
  build or the harness, so stdout there is untouched. The half that is NOT verified (does the
  harness still print on Windows) is a declared-open item in `docs/DISTRIBUTION.md` §7 with the
  exact command that closes it — do not claim it.
- **Publish with `bash scripts/publish.sh`, never a bare `dotnet publish`.** The old line here
  ("never publish with `-p:PublishTrimmed=true`") was **stale and actively harmful** — trimmed is
  the recommended default and has been since F1 fixed the hazard (source-generated JSON contexts +
  `TrimmerRootAssembly`; `docs/DISTRIBUTION.md` §3). What is true is that a *bare* publish skips
  the verification: the script re-runs `SAVETEST`/`METATEST`/`SHIPTEST` **against the binary it
  just built** and refuses to report success otherwise. C6 also made the two mitigations a build
  ERROR to remove (`C6GuardTrimmedPersistence` in `Sightline.csproj`).
- **Headless byte-stability:** the screenshot harness keeps `Display` (post-FX) OFF and
  never touches disk/meta (gated by `NoPersist`), so shots stay byte-identical and the
  balance flywheel is reproducible. Keep new persistent/random/post-FX work behind those
  gates.
- **Headless determinism — what is actually guaranteed.** The harness keeps `Display`
  (post-FX) OFF and never touches disk/meta (gated by `NoPersist`), and the flywheel
  reseeds explicitly (`Util.Reseed(50000+slot)`), so **paired measurement** is
  reproducible. **Screenshots are NOT byte-identical** and never were: two
  `SIGHTLINE_SHOT=90` runs measurably differ in ~30% of pixels (measured 303,065 of
  1,024,000 px), because dozens of wall-clock reads drive animation and `Util.Rng` is
  clock-seeded by default. **DO NOT WRITE THE COUNT HERE — DERIVE IT.** The last two attempts
  were both wrong in the same direction, and the second one dismissed the right answer: this
  line read "57 (46 + 11), the old 58 / 46 / 12 was off" while the tree measured **46 + 12 =
  58** — i.e. the triple it called off was the correct one, and `src/Display.cs`'s own comment
  ("58 Raylib.GetTime() reads") had been right all along. Every clock read routes through
  `Renderer.Now()` / `Hud.Now()` (measured: ZERO raw `Raylib.GetTime()` call sites outside
  those two definitions), so the count is one command:

  ```bash
  for f in src/Renderer.cs src/Hud.cs; do printf "%-18s " "$f"; \
    grep -vE '^\s*(//|///|\*)' "$f" | grep -v 'double Now() =>' | grep -o '\bNow()' | wc -l; done
  ```

  (46 / 12 at `a933cfe`; re-run it rather than quoting that.) Never gate anything on a screenshot
  hash — **`SIGHTLINE_PAIRTEST` byte-identity is the real determinism gate**. Keep new
  persistent/random/post-FX work behind the `NoPersist`/Display gates so that stays true.
  **If you write a test that reads PIXELS, pin the clock**: `Renderer.cs`'s reads all go
  through `Renderer.Now()`, which returns the real clock unless the harness-only
  `Renderer.TimePin` is set to a fixed t (`SIGHTLINE_BOARDTEST` does; it is the only reason
  that probe prints one number per run). Restore it to `-1` when you are done. **The CHROME has
  the same pin since C5** — `Hud.TimePin` (the wall-clock reads in `Hud.cs` route through
  `Hud.Now()`) plus `Hud.AnimPin`, which forces panel-entrance progress instead of waiting real
  seconds for a card to slide in. `Hud.MousePin` does the same for the POINTER, which 30 draw sites read live
  (hover fills, hover cards, and the threat card, which anchors itself at the cursor) — an unpinned
  pointer measured a ~3% flake in FITTEST and a 16-assertion disagreement between two machines on
  one commit. `SIGHTLINE_FITTEST`'s screen audit sets all four, and it also reseeds per staged
  screen: the shop slate and the event roll are clock-seeded, so an unseeded screen audit reports a
  different worst-case string on consecutive runs.

---

## Current state (short)

> **Keep this section SHORT.** It is a pointer for a fresh session, not a changelog.
> Per-program detail belongs in `docs/DEVLOG.md`; what exists belongs in `docs/FEATURES.md`.
> **It has now regrown and been cut back three times** — RESONANCE cut it from ~40 lines of program
> summaries, and PARALLAX cut it from **423**. The shape that keeps working is: the state paragraph,
> the ladder of record with its warnings, and ONE ROW PER WAVE naming the single fact a fresh session
> would otherwise re-discover. **If you are about to add a paragraph here, add a table row instead**,
> and put the paragraph in `docs/DEVLOG.md` where the reader who wants it will look.

Playable and feature-complete: four modes (campaign / endless / skirmish / daily), a cross-run
meta profile, a deep per-run loop, 8 objectives, ~21 enemy archetypes, 35 arenas, and a full
juice/audio/post-FX layer. Builds 0 warn / 0 err; `bash scripts/qa-sweep.sh --full` runs all
self-tests and must be green; autoplay must never TIMEOUT or throw.

Nine autonomous programs (through **FULCRUM**, closed 2026-08-28) built and balance-tuned the
game. The reference heat ladder and goal band of record are in `docs/DEVLOG.md` §FUL-13.

**PROGRAM RESONANCE** is the tenth (closed). Its thesis: the game had been tuned far past the
point where anyone verified how it actually *lands*. It found and fixed several things nine
win-rate-driven programs could not see — the audio had never been heard by anyone (a one-line
filter bug meant every weapon was raw white noise), the board's biome identity was erased by a
move overlay that flooded it, only 3 of ~14 verbs were ever taught, the displayed hit% was not
the hit probability, a pod scatter bug stacked units on one tile so the buried one could not be
clicked, and a published build launched from the wrong directory silently lost its font. Detail
in `docs/DEVLOG.md` §RESONANCE; open work in `docs/ROADMAP.md`. Wave **X1 THE EXCHANGE** then changed the
combat model's headline ratio: `Mission.HostileToughness` (+3 HP) and
`Mission.HostileDamageTrim` (−1 per weapon band end) in the single `Mission.MakeHostile`
funnel, so a trade takes roughly two hits instead of one. Its raw chunk logs live in
`docs/measurements/x1/`.

> **NUMBERS AND THEIR BASE COMMIT — read before quoting any balance figure.**
>
> > ### ⚠ EVERY LADDER BELOW WAS MEASURED ON A GAMEPLAY RNG STREAM THAT NO LONGER EXISTS.
> > Wave **W1 TRUE INSTRUMENT** severed presentation from the shared `Util.Rng` stream (screen-shake
> > jitter was rolled once per RENDERED FRAME; `Unit()`'s idle-bob phase was rolled on first draw).
> > That deliberately **INVALIDATES every archived CRN world in this repository**: the same slot seed
> > now plays a different world. Measured over the exact 10 slots X2 archived as `S1-h0-b0`, the tree
> > reproduced **10/10** slots up to W1's break and **3/10** after it.
> >
> > So every archive under `docs/measurements/` dated before W1 (`x1/`, `x2/`, `w4/`, `l1/`) is
> > **INCOMPARABLE with anything measured on the current tree**. (The ladder of record BELOW is
> > post-W1 and is not affected; this warning is about the pre-W1 archives it superseded.)
> > They remain valid as history — L1 in particular is the only n≥80 picture of the pre-repair tree
> > that will ever exist — but a number from them may not be **compared** with, or **rescaled** to, a
> > number from today's tree. **Re-measure. Do not rescale.** See `docs/DEVLOG.md` §W1.
>
>
> ### ⚠ THE LADDER OF RECORD IS **L6**. L5 AND EVERYTHING BELOW IT ARE PROVENANCE.
> **The "L5 is a pre-P20 ladder" warning that stood here is RESOLVED: L6 re-measured the composed
> tree.** It also re-priced P20 alone on SIXTEEN slot sets and found P20's own headline (−3.8 at
> h4, "resolved") **does not survive the doubling** — on eight sets P20 never saw, the same lever
> reads −0.6. The board does move (27.2% / 22.2% of paired campaigns take a different course, which
> reproduces P20's 25.3% / 16.6%); the WIN-RATE effect is not resolved at n=320/rung. Details below
> and in DEVLOG §L6.
>
> **L6 — base commit `6a6ebee` (`main` after PROGRAM PARALLAX milestone 12), heat PINNED
> (`EventCatalog.HeatPinned` — a rung means the rung), 6 rungs x **16** CRN slot bases (0..150),
> n=320/rung, 1,920 campaigns; 224 chunks over the whole round, every one asserted through
> `p15/run_chunk.sh` (THE RUNNER OF RECORD — not `c1`'s, which drove L4/L5 and is superseded),
> zero BAD, `LEAK-CHECK PASS` (0 of 7,928 missions off-rung). Raw round: `docs/measurements/l6/`
> (README has every table below in full).**
>
> | rung | RECRUIT | h0 | h2 | h4 | h6 | h8 |
> |---|---|---|---|---|---|---|
> | **win%** | **70.6** | **44.4** | **35.9** | **23.1** | **11.2** | **8.8** |
> | binomial SE | 2.55 | 2.78 | 2.68 | 2.36 | 1.77 | 1.58 |
> | **cluster SE** | 2.41 | 2.13 | 2.89 | 3.09 | 2.17 | 1.41 |
> | band | 67-83 | 47-63 | 32-48 | 22-38 | 12-28 | 5-15 |
> | verdict | IN (+3.6) | **OUT -2.6** | IN (+3.9) | IN (+1.1) | **OUT -0.8** | IN (+3.8) |
>
> **Monotone at every step; four of six in band.** Steps 26.2 / 8.4 / 12.8 / 11.9 / **2.5**. Neither
> OUT is a measured breach (h0 is 1.23 cluster-SE under its floor, h6 0.35) and no IN is a
> robustness claim (h4 clears its floor by 0.36 cluster-SE — inside the noise the other way).
> **The soft spot MOVED: h6->h8 buys 2.5, the smallest step in the table, and h0->h2 buys 8.4 where
> L5 read 14.1.** Locating a flat step is what C1's per-RUNG ladder is for; L6 sampled six rungs.
>
> **THE BRIDGE BROKE, AND WHERE IT BROKE IS THE FINDING.** The bridge arm — the same 96 cells with
> every post-L5 gameplay lever restored (`AILANE=0 NEWGROUND=0 SECONDAXIS=0 PERKPICK=0
> ASSISTLATCH=0 ELITEBOSS=0 ROSTERID=0 STALEGROUND=1`, a set DERIVED by diffing the env-var surface
> of `src/` at `7180374`) — reproduces L5 on **0/96 chunks**. Bisected by milestone merge, with
> L5's own base commit as a control that passes on 4/4 cells: the chain is intact through milestone
> 4 and **breaks at milestone 5, THE FORK PAYS**, which repriced the routing economy
> (`Run.DepthBase` 10->12, SUPPLY discount, PITCHED premium, ELITE premium) with every one a
> `const int` and **NO restore flag**. So no bridge to L5 can exist through it, by construction —
> **the one wave in this repository that broke its own house rule is the one the ladder had to
> cross.** Against MILESTONE 5 itself the bridge arm is **96/96 chunks, 1,920/1,920 outcomes**, so
> every wave from milestone 6 to 12 IS switchable and the CRN machinery is intact across seven
> merges.
>
> **NEITHER HALF OF THE L5->L6 DIFFERENCE IS A MEASURED LEVER MOVE.** CRN-paired on 1,920 worlds
> each: THE FORK PAYS **-0.36 pooled** (401 discordant, MDE 2.9, z=-0.35) and
> P10+P16+P18+P19+P20 composed **+0.73 pooled** (488 discordant, MDE 3.2, z=+0.63); every rung sits
> inside its own MDE (4.9-9.2). **Report `n_disc` and the MDE beside every paired row** — a flat row
> at low discordance is an absence of evidence, not neutrality (C2's rule), and neither of these is
> a zero. **Shape may be compared with L5; LEVEL may not** — the bridge that would license it is the
> broken one.
>
> **P20 RE-PRICED ON 16 SETS: h0 +2.2 (its own +2.2, reproduced); h4 -2.5, NOT RESOLVED** (n_disc 22,
> MDE 4.1). Split-half: P20's own 8 sets give **-4.4, z=-2.33**; 8 sets it never saw give **-0.6**.
> **Third time this project has measured that shape** (L5's split-half on L4; W2's four-vs-sixteen).
> **A rung is sixteen slot sets. So is a lever.**
>
> **STALEMATES: 34/1,920 = 1.77% on the MISSION arm; the RUN arm fired 0 times in all 4,480
> campaigns of the round** (runTurns 51-75 against 150) — three ladders running. Escort 10 /
> Eliminate 9 / Evac 8 / Rescue 6 / Decapitate 1; missions 2-4 hold 26 of 34. **Slot 46's mission-1
> Eliminate stalls at FOUR rungs on BOTH policies** (`SIGHTLINE_BALANCE_BASE=40`, slot 46) — L5 saw
> the same world stall on sloppy alone, so the deadlock belongs to that opener, not to a rung.
>
> **No corrective lever was shipped.** Two rungs under floor is a finding to publish, not to repair
> inside a measurement round — as in L4 and L5.
>
> ---
> **SUPERSEDED LADDERS ARE NOT HERE ANY MORE — they are in `docs/DEVLOG.md` and under
> `docs/measurements/`.** L5 (`7180374`, the first pinned 16-set ladder — `docs/measurements/l5/`,
> DEVLOG §THE HEAT PIN AND L5), L4 (`7315425`, n=160/rung), C1 (`17934ee`, the first per-RUNG
> ladder, 16,640 campaigns) and C3 all had full tables in this file. **L5 reproduces L4
> outcome-for-outcome through its own bridge, and L6 reproduces MILESTONE 5 through its own — but
> the two chains do not join**, because THE FORK PAYS sits between them with no restore flag. So:
> quote **L6** for this tree; quote L5 only for the tree it measured, and never subtract one table
> from another. The L4-vs-L5 caution stands unchanged (both halves pinned, L4's 8 slot sets read
> h0 54.4 against the 8 new sets' 39.4).
>
> **A pooled objective row can hide a 49.5-point artifact** — W8 proved it on `Eliminate`, whose
> 89.1% row is largely 960 mission-1s and reads ~40% over its mid-run cells. Use the
> `byObjectiveByNodeKind` / `byObjectiveByMission` cross-tab before concluding anything from a
> per-objective table.
>
> **The rule this program kept enforcing on itself: a balance number without a base commit and an
> n is not a number** — and a rung is four slot sets or it is not a rung. That rule decided a
> shipped default in W2, where four slot sets put a leg below the band floor and sixteen put it
> inside.


### The twelve programs, and the one fact each leaves that must not be re-discovered

Full write-ups are in `docs/DEVLOG.md` under the named section; open work is in `docs/ROADMAP.md`.
**This table exists because this section kept regrowing into a changelog.** A wave belongs here only
if a fresh session would otherwise repeat its mistake — everything else goes in the DEVLOG.

| wave | the fact | §DEVLOG |
|---|---|---|
| **W1** TRUE INSTRUMENT | Presentation was severed from the shared `Util.Rng` stream, so **every CRN world archived before it is incomparable with today's tree**. Re-measure; never rescale. | §W1 |
| **W4** THE SECOND AXIS | `choices/ARMED-soldier-turn` is a near-invariant across five structurally different levers — its two halves answer threat with **opposite signs**. Chase it with a POSITIONING lever at constant threat, never another threat lever. | §W4 |
| **X1** THE EXCHANGE | `Mission.HostileToughness` (+3 HP) and `HostileDamageTrim` (−1 per band end) in the single `Mission.MakeHostile` funnel set time-to-kill. Change either only with a measured round **per side**. | §X1 |
| **X2** TRUE NORTH II | Heat's ramp was gated on `heat > 0`, so the BASE force met the coldest squad with no ramp at all. `Mission.OpenerTrim` gives it one. Mission 1 is the arm that ends a quarter of all runs. | §X2 |
| **W9** THE REPAIR | 15 defects under 51 green tests. Its thesis: **the tests were aimed at the MODEL and not at the SEAM.** Also: three doc over-claims, which is why "do not cite a number you have not just re-measured" is a rule here. | §W9 |
| **TRUE BAND** | `CountMeaningfulChoices` bands ADDITIVELY and caps at 4. **Every `ch/ARMED` figure dated before 2026-08-29 is a multiplicative number and is not comparable.** W4's gates are VOID, not met. | §TRUE BAND |
| **C1** THE FLAT MIDDLE | A `Heat.Mods` row declared an `AiTier` the rung below already provided, dead for two programs under a green HEATLADDERTEST. **A cumulative pin cannot see a dead row**; `MIDTOOTHTEST` adds the per-rung invariant. | §C1 |
| **C2** THE OPPONENT DECLINES | The planner paid any positional price for a line of fire. `Ai.ShotTileValue` + a decline gate. **A CRN round's resolving power is its DISCORDANT count, not its n** — report both. | §C2 |
| **C3** THE TWO GAMES | The eight objectives are **two classes** (kill vs task) and that split was worth 43 points of win rate. `Run.IsKillObjective` is the single source of truth; the fork names the class. | §C3 |
| **C4** EIGHT BIOMES ARE PAINT | Three of eight biomes change the fight, on three axes; the rules live in `Grid.GetCover`/`CostMap`/`HasLineOfSight` so **both sides read one truth**. The rest are paint and `BIOMETEST` asserts it. | §C4 |
| **C6** SHIPS LIKE A PRODUCT | **Every self-test runs from the source tree**, where `Cfg.AssetPath`'s cwd fallback resolves a file the `.csproj` forgot to copy. `SHIPTEST` + `publish.sh` are the only legs testing the artifact a player receives. | §C6 |
| **HEAT PIN + L5** | Three field events raised a run's heat mid-campaign, so a rung was not the rung. `EventCatalog.HeatPinned`; `heatLeak{}` and `campaigns[]` in the JSON. | §THE HEAT PIN AND L5 |
| **P10** THE HELD LANE | The enemy overwatch chooses a cone. **It is INERT on win rate** (0 discordant in 320 at h4 and h8) because the branch fires on 0.15–0.46% of acts, and raising `Ai.DeclineWatchRatio` — the lever that feeds it — makes the opponent *weaker*. Do not re-derive this. | §THE HELD LANE |
| **P11** THE CRASH FILE | `Program.Main` is `Crash.Guard` around `RealMain`, so **the "MUST STAY FIRST IN Main" SHIPCHILD branch is now first in `RealMain`** — keep it there. | §THE CRASH FILE |
| **P12** THE CONFIRMED EIGHT | `Game.Frozen` owns "nothing below this ticks"; `Paused` is a property whose setter releases the fader. Both exist because state kept running behind a modal card. | §THE CONFIRMED EIGHT |
| **P15** THE INSTRUMENT | The checkpoint redeploy **erased the mission it retried** — 421 chunks, 39,143 missions, zero non-terminal mission losses. **Every per-mission and decision-density figure in the archive is biased upward**; campaign-level results and the L5 ladder are untouched. | §THE UNVERIFIED — THE INSTRUMENT |
| **P17** SHIPS AS v1.0.0 | The release artefact is DERIVED, not written: the archive is named from the version **the binary reports**, the changelog from `git log --first-parent`, and the checksum is recomputed in-process. `--tag` makes a LOCAL tag and never pushes. `Display.AllowLaunchFit` defaults **false** so the first-launch window fit can never reach the harness. | §SHIPS AS v1.0.0 |
| **P18** THE SECOND AXIS | The WAR ROOM's six salvage unlocks cost **330** against **61** income for a heat-0 clear, so it emptied in ~5 wins while `UnlockHeatOnWin` kept climbing to `Heat.Max = 8`. Three unlocks are now gated on a rung **CLEARED** (`MetaDto.BestHeatWon`, appended; `MaxHeat` cannot serve — it is a CEILING that stops rising at `Heat.Max`, so a heat-8 clear moves it not at all). **The WAR ROOM's UNLOCKS column has 8px of slack**: a heat-gated unlock is a 24px ledger row, never a card, or FITTEST ellipsizes four descriptions. Also: a BONUS perk's recipient is now chosen (the roll is KEPT as the default, so the RNG stream is untouched and a retarget spends zero draws), and `Run.AssistLevel` reads the **latched** `StartHeat` so an `AddHeat` event can no longer confiscate an assist the player never opted out of. **Campaign-inert at the flywheel by construction** — 240 CRN-paired campaigns, 3 rungs, 2,356 fields, zero diffs (`docs/measurements/p18/`); the width it sells is UNPRICED and unmeasurable until a staged-profile batch hook exists. | §THE SECOND AXIS |
| **P19** THE ROSTER CONTESTS | The named mid-boss now belongs to the map's **ELITE NODE** (`Mission.MidBossFor`), with the floor walked on the ROUTE (`Game.IsFinalApproach`) because an Event node can occupy a route's column-4 slot — **a `mission == 5` floor leaks on 8.1% of routes and the old `n == 3 \|\| n == 5` leaked on 2.7%.** Also: **BOMBARD/WARBRINGER's "0.8%/1.6%" are BODY rates and are the wrong denominator** — both are capped at one per mission, so exposure is **5.6% / 12.5% of missions**; and the arenas' "88% tile-identical" is the **85.3% floor-share baseline**, not duplication (one real near-duplicate: ZIGGURAT/FORGE, Jaccard 72.7%). | §P19 |
| **P20** THE STALE GROUND | `Mission.Build` wiped Tiles, Height and Smoke but **not the biome GROUND layer**, and it asks for that layer through `Grid.IsFloor` / `Grid.CostMap` before `Game.StampBiomeGround` runs — so a board was a function of **the board before it**. Latent since C4, armed by P16 (the rift is the first ground that stops a mover). `Mission.ClearGroundOnBuild`; MODETEST leg (14). The fix moves the board — **but L6 re-priced it on 16 slot sets and its "-3.8 at h4, resolved" does NOT survive the doubling** (-0.6 on eight sets it never saw). | §P20 |
| **L6** THE LADDER OF RECORD | Two things a fresh session must not re-derive. **(1) THE FORK PAYS has no restore flag**, so the CRN chain cannot cross milestone 5 and no bridge to L5 exists; ship a gameplay constant and its flag in the same commit. **(2) `Grid` has a SECOND stale layer: `Barrel`.** `IsFloor` reads it, so Build's connectivity floods read the previous mission's barrels — P20's defect, different array, same predicate. **Latent, not live**: `Game.SetupMission` clears hazards 28 lines before the Build call, and it is the only production caller. `BoardSignature()` was blind to it and now hashes all eight layers. | §L6 |

**Every gameplay lever above has a restore-the-old-behaviour flag**, because a wave that cannot be
switched off cannot be attributed. `SIGHTLINE_BIOMEMECH=0` (the pre-C4 board, exactly),
`SIGHTLINE_AIDECLINE=0` (the pre-C2 opponent), `SIGHTLINE_AILANE=0` (the pre-P10 overwatch),
`SIGHTLINE_MIDTOOTH=0` (the pre-C1 heat table), `SIGHTLINE_OPENERTRIM=0` (the pre-X2 opener),
`SIGHTLINE_KILLTREADMILL=1` (C3's clock arm back on), `SIGHTLINE_HEATPIN=0` (the heat leak back),
`SIGHTLINE_CHOICEBAND=mult` (the pre-TRUE-BAND instrument), `SIGHTLINE_MODEDEPTH=0` (the pre-P14
single-mission modes), `SIGHTLINE_SECONDAXIS=0` / `SIGHTLINE_PERKPICK=0` / `SIGHTLINE_ASSISTLATCH=0`
(the three halves of P18: the pre-P18 WAR ROOM, the random bonus-perk recipient, the live-heat assist),
`SIGHTLINE_ELITEBOSS=0` (the pre-P19 mission-number mid-boss), `SIGHTLINE_ROSTERID=0` (the pre-P19
SMG monoculture) and `SIGHTLINE_STALEGROUND=1` (the pre-P20 seam, in which `Mission.Build` read the
PREVIOUS mission's ground layer — never a shipping configuration; it makes the SEEDED DAILY's
headline contract false). **Grep `Program.cs` for `SIGHTLINE_` for the authoritative set** — that list
is derived, this one is written down, and written-down lists in this repository go stale.

> **⚠ ONE WAVE HAS NO FLAG, AND IT COST THE PROJECT A BRIDGE.** **THE FORK PAYS** (milestone 5,
> `54147dc`) repriced the routing economy — `Run.DepthBase` 10->12, `SupplyDiscount`,
> `PitchedPremium`, `ElitePremium` — as `const int`s in `src/Run.cs` with no environment switch.
> L6 found it by bisection when its bridge to L5 failed on 96 of 96 chunks: **the chain is intact
> on both sides of that one merge and cannot cross it.** L6 priced the wave anyway, by using
> milestone 5 as the bridge target (`-0.36 pooled over 1,920 CRN pairs, 401 discordant, MDE 2.9`),
> so the number exists — but only because a whole extra tree had to be built to get it. **If you
> ship a gameplay constant, ship its flag in the same commit.**

**PROGRAM PARALLAX is the twelfth and is current.** Its own thesis, earned twice over: the gates in
this project fail QUIET rather than loud. It found the sweep's coverage guard blind to a whole class
of hook name and defeatable by a comment; a defect hunt whose post-processing filed 33 unverified
findings as "refuted" with an empty reason; two self-tests that could zero a player's settings file;
audio censuses structurally unable to fail; and a measurement layer reporting numbers where it had
measured nothing. **When a check here says everything is fine, ask what it would have said if it
were not.** Wave **L6** earned it a third time, twice in one round: `BoardSignature()` — the gate
that caught P20's stale ground — could not see six of the eight layers it was trusted for, and the
one gameplay wave in this repository that shipped without a restore flag (THE FORK PAYS) was found
only when a ladder needed to cross it.


**PROGRAM PARALLAX — wave P16 "GROUND TRUTH" (2026-09-04, base `e57e151`)** ended C4's standing
declaration that five biomes were paint. **VOID -> RIFT** (impassable, but **TRANSPARENT** and giving
**no cover** - the only shape on this board that stops movement while hiding nothing, so it cuts open
floor into lanes you can still shoot across) and **ARID -> SOFT SAND** (a step costs 3 half-tiles, 5
diagonal - the exact inverse of ice, one line in `Grid.CostMap`). Five mechanical, three paint;
`Ai.cs` gained **zero lines**, because both rules live in `Grid.IsFloor` / `Grid.CostMap`.
**The rift is the first ground that can make a mission UNWINNABLE**, so `Terrain.StampRift` is the
only stamper that validates - it re-floods through `Grid.CostMap` after every candidate tile and
reverts any that removes more than itself, so a chasm can never seal and the gaps it leaves ARE the
bridges. `SIGHTLINE_RIFTTEST` proves it on **576 real VOID boards, every objective, zero stranded
tiles**; with the guard off the same sweep strands up to 69 tiles on 32 boards. Priced CRN-paired on
`ba34279` (`SIGHTLINE_NEWGROUND=0` is the arm - **not** BIOMEMECH, which restores the pre-C4 board and
would price C4 and P16 together), n=160/rung/arm, 960 campaigns: **-2.5 / -1.2 / +1.2** at h0/h4/h8,
pooled McNemar z=-0.43, 95% CI **[-4.7, +3.0]** - near-inert on win rate **at 18.3% discordance**
(88 of 480 worlds played out differently, so this is a bounded effect, not an absent one), and the
**opener cell is clean** (`byNodeKind` Start +0.0/+0.6/+0.0, the cell C4's layer failed at -4.37).
**The biome DEAL was verified and deliberately NOT changed:** it is a fixed 8-cycle reaching only
**8 of 56** adjacencies, but `Mission.DeckPick` reads it, so re-dealing re-deals the ARENA on **28.9%
of missions** and severs the CRN chain like W1 - and **`SIGHTLINE_SAVETEST` cannot see it** (all three
map goldens PASS with the deal hashed; `MapFingerprint` feeds `GenerateMap` only). Both are open items
in `docs/ROADMAP.md`. DEVLOG §GROUND TRUTH; raw round `docs/measurements/p16/`; rationale
`docs/DESIGN.md` §5.4.

## Handoff protocol (when context gets heavy)
You judge when context rot risks quality (don't wait for the 1M hard limit). Before stopping:
1. Make sure `main` builds and passes autoplay.
2. Update the relevant doc — **`docs/ROADMAP.md`** checkboxes and **`docs/DEVLOG.md`** for
   what you did this session. Refresh the short "Current state" here only if the one-paragraph
   summary is now wrong.
3. Leave mid-flight notes in `docs/DEVLOG.md` (what you were doing, the next concrete step,
   any gotcha) — **not** as a growing blob in CLAUDE.md.
4. Tell the human to open a fresh session (they'll send only `.`).
