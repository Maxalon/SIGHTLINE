# SIGHTLINE — DEVLOG (autonomous dev-team sprints)

This log records the **process** of the autonomous, multi-agent dev team: sprint
goals, who (which agent role) did what, review/QA outcomes, and merge decisions.
It complements `CLAUDE.md` (the build/continuity contract) and `docs/DESIGN.md`
(the rationale contract). The orchestrator (tech lead) maintains this file.

> **Raw history:** the unabridged per-session WIP notes that used to live in `CLAUDE.md`
> were migrated to [`docs/DEVLOG-ARCHIVE.md`](DEVLOG-ARCHIVE.md) on 2026-07-01. This is
> the curated log; append session write-ups here, not to `CLAUDE.md`.

**Team roles** (realised as subagents):
- **PM / Research** — surveys the codebase + design docs, produces the prioritized backlog.
- **Architect** — turns a backlog item into a precise, file-by-file implementation plan.
- **Developers** — implement features (in isolated git worktrees for parallel, conflict-free work).
- **Reviewers** — read each dev's diff for correctness + quality, request changes.
- **QA** — build (Release, 0/0), run the `SIGHTLINE_AUTOPLAY` smoke test, capture screenshots.
- **Orchestrator** (tech lead) — plans sprints, dispatches/sequences agents, arbitrates
  reviews, integrates to the working branch, and decides what merges.

**Ground rules** (inherited from `CLAUDE.md`, non-negotiable): NO GitHub Actions / CI /
test-runner ever; **nothing that would cost money or create legal/licensing trouble if the
game is distributed** — procedural / in-engine / shader-generated content is the default,
and free assets are allowed *only* when the license clearly permits free use AND free
redistribution (favor CC0 / public-domain; OFL is fine for fonts), they fit perfectly, and
the file is small (no large binaries); drawn text ASCII-only until a real font ships; the
headless harness stays deterministic **where it claims to be** — `SIGHTLINE_PAIRTEST`
byte-identity, not screenshot hashes (see §F1: shots were never byte-stable); always ship
code that builds clean in Release and passes the autoplay smoke test (no exceptions / no
TIMEOUT).

---

## PROGRAM "VANTAGE" — player decision-space ≥ the AI's, horizontal progression, striking feel

**Thesis (from a 3-agent read-only research fan-out + DESIGN §4 / AUDIT #3-4):** SIGHTLINE's enemy AI is more
positionally sophisticated than the *player's* verb-set, and progression is almost entirely a POWER axis, not an
identity/strategy axis. The program closes both gaps and lifts the game's feel. Branch
`claude/game-dev-orchestration-7riqqm`.

**Team / process.** Orchestrator (tech lead) + 3 parallel read-only research agents (gameplay-depth opportunities,
code/architecture audit, visual/audio critique) → per-wave design-spec agents → developers (the disjoint audio wave
in an isolated worktree; the Game.cs waves sequentially in the main tree) → two independent reviewer passes → the
`SIGHTLINE_BALANCE` flywheel, measured centrally by the orchestrator. Game.cs is the single serialization point, so
the four Game.cs-touching waves ran one-owner-at-a-time; the audio/light wave (disjoint Audio/Fx/Anim) ran in
parallel and integrated by clean file-copy.

**Waves (all on the branch, each built 0/0 + self-tested + reviewed + measured):**
- **W0 hardening (`e94ecbb`).** Extended the append-only SAVETEST ordinal guard from `Objective` alone to all six
  persisted-by-ordinal enums (the prerequisite for the enum-adding waves) + banners; LoS fail-closed; KillUnit
  `_run` null-guard; dead crit-constant cull. (Deferred the big Game.cs harness-code split — sequential waves don't
  need it.)
- **W1 FIELD CRAFT verbs (`46891b2`).** Universal DRAG (reach-2 ally pull) + VAULT (cross an impassable cover tile)
  — proactive positioning the AI already does, both anti-turtle. `SIGHTLINE_FIELDTEST`. *QA catch:* wiring + running
  the new self-test myself surfaced that the dev's first DRAG was dead (adjacent-only → landing tile = the dragger);
  bounced back to the dev, fixed to reach-2.
- **W2 class specialization forks (`496c435`).** The horizontal-progression keystone: a one-time pick-1-of-2 `Spec`
  per soldier (10 forks) that changes HOW a class plays. Append-only enum, persisted + guarded.
- **W3 audio + light (`e4e7981`, parallel worktree).** Transient muzzle/impact lights, trails, damage-number juice,
  audio pan/pitch, and a sample-asset loader with synth fallback (real CC0 audio can drop in later, no call-site
  changes). All deterministic; audio blind-but-crash-safe.
- **W4 Decapitate teeth (`d25a2a5`).** The GUARDED HVT: reduced-not-zero damage while a bodyguard is near, turning a
  turn-1 snipe into a peel-then-execute puzzle (telegraphed; TIMEOUT-safe).

**Reviews.** Two independent read-only passes (review-by-committed-sha, no build, to not race the live tree): the
first (W0/W1/W3) found one MED — DRAG could move the locked RESCUE captive — fixed (M1) + a vault-test coverage nit
(L2); the second (W2/W4) = **SHIP, no CRIT/HIGH/MED**.

**Measured (flywheel, heat-0, N=24 greedy+sloppy).** Run-completion 79%→**66.7%** (still the healthy/winnable band —
heat-0 was arguably too easy; RECKONING shipped 68%). The headline: **policy gap −16.7 → +8.3** — the baseline had an
INVERTED gap (sloppy play beat greedy, a real pathology); the new verbs + forks give skilled play more to leverage,
landing the gap squarely in the audit's healthy +7-12 band. Choices/turn 5.57 (healthy). All 10 specialization forks
reachable + chosen. Decapitate 100%→94% (teeth bite; the optimal bot's guard-peeling masks it in avg-turns — a
bot-metric-undersells-human-design case). Build 0/0; SAVETEST/COMBATTEST/CDTEST/AITEST/FIELDTEST PASS; autoplay clean.

**Process learnings.** (1) Wiring + running a dev's self-test *yourself* is worth it — it caught a dead verb the
dev's own (unwired) verification missed. (2) Reviewing committed shas (not the working tree) lets a reviewer run
fully parallel with the next wave's live edits. (3) The bot-measured `avgTurns` undersells human-facing teeth when
the smart bot adapts (W4) — read win-rate + the design intent, not just the turn count. (4) The inverted policy gap
was the most valuable thing the flywheel surfaced — a win-rate that *looks* fine (79%) hid that skill was being
punished; the program's real win is fixing that, not the headline completion %.

### VANTAGE II (continuation, new PR) — deepen the run-to-run loop

After VANTAGE I merged, two more waves on the campaign/run-to-run layer (branch restarted from the merged main):
- **W5 — SCARS & VENDETTAS** (`b9ffc11`): the COST side of soldier identity (Pillar 5 stakes). An append-only `Scar`
  enum mirroring the feat→trait system — trauma leaves lasting marks (shell-shock, burns, hard-bitten grit, a
  faction vendetta), each a drawback + a defiant upside.
- **W6 — RUN CONTRACTS** (`591b196`): opt-in run-modifier rulesets picked at the draft (IRON VETERANS / HIGH STAKES /
  SPEARHEAD). Default `None` + the headless path never runs the draft → **zero base-balance regression** by
  construction — the safest possible kind of addition.

Independent review of W5+W6 = **SHIP** (no CRIT/HIGH/MED). Build 0/0; SCARTEST/CONTRACTTEST + the full suite PASS;
autoplay clean. Measured (heat-0 N=20, contracts at default None): run-completion **67.5%** (≈ VANTAGE I's 66.7% —
no regression), policy gap +25 (skill beats sloppy), choices/turn 5.17. Learning: a default-None opt-in modifier is
the lowest-risk way to add variety — it's provably inert on the measured path, so it can't regress the base.

---

## Backlog (from PM/Research, sprint-ordered by file-disjointness)

| # | Title | Impact | Effort | Primary files |
|---|-------|--------|--------|--------------|
| S1-A | Post-processing shader (bloom/vignette/grade) | HIGH | M | `Display.cs`, embedded GLSL |
| S1-B | Real font (kills ASCII-`?` limit) | HIGH | M | `Program.cs`, `Hud.cs`, `Renderer.cs` |
| S1-C | Procedural floor/cover noise | MED | S | `Renderer.cs` |
| S2-A | Graze / partial-hit + damage floor | HIGH | S | `Combat.cs`, `Anim.cs` |
| S2-B | Enemy AI uses utility items | MED | M | `Ai.cs`, `Game.cs` (light) |
| S2-C | Overwatch-camp soft pressure | MED | S | `Game.cs`, `Hud.cs` (light) |
| S3-A | Bench / deploy short-handed (attrition) | HIGH | M | `Run.cs`, `Game.cs`, `Hud.cs`, `SaveGame.cs` |
| S3-B | Focal-point unit lighting | MED | S | `Renderer.cs` |
| S3-C | 3 new authored arenas | MED | S | `Maps.cs` |
| S4-A | Intel hints on campaign map | MED | S | `Hud.cs`, `Run.cs` |
| S4-B | Semantic color + shape redundancy | MED | M | `Renderer.cs` |
| S4-C | Streak-breaker + damage bell curve | MED | S | `Combat.cs`, `Unit.cs` |
| 4.4 | Concealment + ambush (marquee) | HIGH | L | `Game.cs`, `Unit.cs`, `Combat.cs`, `Hud.cs`, `Renderer.cs` |

Trap items (rejected): fog of war (identity pivot, deferred), voiced lines (no audio device),
bigger maps (needs a camera; standoff already tuned), per-biome enemy skins (near-zero value),
mid-mission save granularity (high effort, defer).

`Renderer.cs` and `Game.cs` are the two hot files — visual items that touch `Renderer.cs`
(S1-B/S1-C/S3-B/S4-B) are sequenced into one batch; `Game.cs` items are sequenced too.

## Sprint 1 — visual lift + randomness fix (Wave 1)

Dispatched 3 developer agents into isolated worktrees, **disjoint files** for conflict-free parallelism:
- **Dev A — S1-A post-processing shader** (`Display.cs` + embedded GLSL, `Program.cs` hook).
- **Dev B — S3-C three new authored arenas** (`Maps.cs`).
- **Dev C — S2-A graze / partial-hit + guaranteed-damage floor** (`Combat.cs`, `Anim.cs`).

4.4 concealment is being architected in parallel; it runs solo in a later wave (it touches the
hot files Game/Hud/Renderer). Each feature is peer-reviewed + QA'd before integration.

### Sprint 1 — RESULT (all shipped to PR #47)

| Item | Owner | Outcome |
|---|---|---|
| S3-C 3 arenas (`Maps.cs`) | Dev C | Clean; integrated `b92abfe`. RIDGE/CROSSROADS/FOXHOLES, all pass connectivity guard. |
| S1-A post-FX shader (`Display.cs`) | Dev B | Clean; integrated `f3f4541`. Embedded GLSL, headless byte-stable, `SIGHTLINE_POSTFX=1` to view. |
| 4.4 concealment | Dev A (+ child agent) + orchestrator | Dev A stalled mid-task; a child agent it spawned finished a full version (`011770f`). Orchestrator integrated: hand-merged Game/Program onto the post-FX trunk, adopted the child's verified Hud/Renderer + Combat/Unit, fixed a top-bar HUD collision. Integrated `005f1b5`. |

**Process learnings:** (1) a dev agent can delegate to a sub-agent and report "still running" while
coming to rest — always inspect the worktree branch before treating it as done. (2) `SendMessage`
to resume a stalled agent was not available, so the orchestrator finished the marquee feature
directly (reliable, full quality control). (3) Parallel devs MUST be on disjoint files; the one
overlap (a dev based its branch on a pre-integration trunk) was resolved by hand-merging only the
two shared files and file-level-checking-out the rest. (4) Integrated incrementally (one commit per
feature) rather than a big-bang merge.

QA (on the integrated trunk): Release 0/0; CONCEALTEST + COMBATTEST PASS; autoplay clean x5 +
sabotage/rescue/defend (no exceptions, no TIMEOUT); screenshots verified for each feature.

## Sprint 2 — combat feel + AI + typography (planned)

- **S2-A** graze / partial-hit + guaranteed-damage floor (`Combat.cs`, `Anim.cs`) — output-randomness fix.
- **S2-B** enemy AI uses utility items (smoke to reposition, flash to break overwatch) (`Ai.cs`, `Game.cs` light).
- **S1-B** real font — NotoMono (OFL-1.1, already on the machine, no network) baked via `LoadFontEx`,
  migrate `DrawText`->`DrawTextEx` (`Program.cs`, `Hud.cs`, `Renderer.cs`, `Fx.cs`); lifts the ASCII limit.
- (S2-C overwatch-camp soft pressure queued behind these.)

Batching: S2-A (Combat/Anim) and S1-B (Program/Hud/Renderer/Fx) are file-disjoint -> parallel.
S2-B touches Ai + Game(light); sequenced against other Game.cs work.

### Sprint 1 peer review — findings (to apply as one batch AFTER Sprint 2 integrates)

Reviewer verdict: **no blockers**; risky parts (shader compile guard, headless byte-stability,
one-shot ambush discipline, captive decoupling, arena connectivity) all validated. To fix
(all touch Game.cs/Display.cs, which Sprint 2 devs are editing — so batched, not applied mid-flight):
- **M1 (major):** overwatch reactions fire while `SquadConcealed` without breaking stealth — a free
  reaction round with no reveal (reachable with pre-active/Defend-wave enemies). Fix: in
  `OnUnitEnteredTile`'s overwatch-fire loop, `if (w.Team==Player && SquadConcealed) BreakConcealment();`
  (no actor -> no ambush bonus on a reaction) before the shot resolves.
- **Mi1:** `DoAbility` Suppress passes the actor to `BreakConcealment` but has no shot to consume the
  flag -> change to `BreakConcealment()` (no actor).
- **Mi2:** add a RevealRange-proximity case to `ConcealSelfTest` (active foe at distance 3 -> auto-break).
- **Mi3/N1 (shader):** bloom radius is aspect-agnostic + a dead `texel` var in the GLSL — make it
  texel-relative. (N2 is a doc-comment nit; optional.)

### Sprint 2 — RESULT (all shipped to PR #47)

| Item | Owner | Outcome |
|---|---|---|
| S2-A graze / partial-hit + damage floor (`Combat`/`Anim`) | Dev D | Clean; integrated `e0feaa6`. Correct roll convention, preserved the post-FX bloom line. |
| S1-B / 5.3 real font NotoMono (`Program/Hud/Renderer/Fx/Util/csproj/assets`) | Dev E | Clean; integrated `50caa57`. ~154 call sites migrated, OFL font committed, ASCII limit lifted. |
| S2-B enemy AI utility items (`Ai/Game/Mission/Unit`) | Dev F | Clean; integrated `f1af741`. 11 autoplay runs clean; throttled, no ally-splash, no TIMEOUT. |
| Sprint 1 review fixes (M1/Mi1/Mi2/Mi3/N1) | Orchestrator | Applied in `f1af741` (batched onto the integrated trunk after the Game.cs-touching dev landed, to avoid conflict). |

All Sprint 2 devs ran in parallel on disjoint files (Combat/Anim | Program/Hud/Renderer/Fx/Util/csproj | Ai/Game/Mission/Unit) — zero merge conflicts. The Sprint 1 peer review ran in parallel (read-only). Integration was incremental, one commit per feature; the review fixes (Game.cs/Display.cs) were applied last so they didn't collide with Dev F's Game.cs work. QA on the integrated trunk: build 0/0, COMBATTEST + CONCEALTEST PASS, post-FX shader recompiles after the GLSL fix, autoplay clean x5 + sabotage/rescue/defend.

## Sprint 3 — run-loop stakes + combat consistency + focal visuals (planned)

Disjoint-file wave (parallel-safe): **S3-A** bench / deploy short-handed (attrition bites —
`Run`/`Game`/`Hud`/`SaveGame`); **S4-C** streak-breaker + damage bell-curve (`Combat`/`Unit`);
**S3-B** focal-point unit lighting (`Renderer`). Plus a peer review of the Sprint 2 gameplay
logic (graze + AI items). Deferred to a later wave (share hot files with S3-A/S3-B):
S4-A intel hints (Hud/Run), S4-B semantic color+shape (Renderer), S2-C overwatch-camp pressure (Game/Hud).

### Sprint 3 — RESULT (all shipped to PR #47)

| Item | Owner | Outcome |
|---|---|---|
| S4-C streak-breaker (`Combat`/`Unit`) | Dev I | Clean; integrated `2412432`. Hidden +6/miss cap +12, composes with graze. |
| S3-B focal-point lighting (`Renderer`) | Dev H | Clean; integrated `159306c`. Selected pops, signal kept full-alpha, squint test holds. |
| S3-A bench / short-handed (`Run/Game/Hud/SaveGame/Unit/Program`) | Dev G | Clean; integrated `04a5e78`. SAVETEST PASS, BENCH only on wounded, autopilot never benches. |
| Sprint 2 review fixes (M1 graze miss-floor + Mi2/Mi3/Mi4/Mi5/N1) | Orchestrator | `62d4edd`, applied to the free files (Combat/Anim/Ai) while Dev G ran. |

**Process learnings:** (1) A planning miss — Dev I (streak) and Dev G (bench) both added a field
to `Unit.cs`. Resolved by integrating Dev I's `Unit.cs` first, then hand-adding Dev G's `Benched`
field (non-overlapping additions). LESSON: assign each hot file (Unit.cs included) to ONE dev per
wave. (2) The Sprint-2 peer review ran in parallel and found a real MAJOR (graze deleting true
misses at >=85% hit) — fixed with a `GrazeMinMiss` floor. Review-in-parallel keeps catching things
autoplay can't. (3) Review fixes were applied to files NOT under active development (Combat/Anim/Ai),
so they never collided with the in-flight bench dev (Run/Game/Hud/SaveGame/Unit).

## Sprint 4 — strategic info + readability + dominant-strategy guard (planned)

Disjoint wave: **S2-C** overwatch-camp soft pressure (`Game`/`Hud`); **S4-B** semantic color +
shape redundancy (`Renderer`); **themed-per-biome arena selection + 2 new arenas** (`Maps`/`Mission`).
Plus a peer review of Sprint 3 (bench lifecycle is the most logic-heavy). S4-A intel-hints
(Hud/Run) deferred behind S2-C (shares Hud).

### Owner feedback (mid-Sprint-3) → re-prioritized Sprint 4

The owner playtested and said it "feels weird with all characters on screen in one line on each
side of the field," and asked about larger maps + a camera with character focus. Architect analysis:
- **Root cause = spawn FORMATION, not map size (80-90%).** `PlayerSpawns` packs all 4 soldiers into
  cols 1-2; `SpawnEnemies` packs enemies into cols 16-17 → two vertical firing lines. Fix is a ~6-line
  `Mission.cs` change (stagger both sides across 3-4 columns), zero risk. → **Dev (this sprint).**
- **Auto-focus camera: default OFF, togglable.** Full-board readability is the game's identity
  (DESIGN.md §1/§5); auto-zoom trades it away, so make it opt-in via the existing CamZoom/CamPan rig,
  forced off in the harness. → **Dev (this sprint).**
- **Larger maps: NO-GO (for now).** Hard blockers: at 24x14/Tile64 the board (1536x896) overflows the
  1280x800 window → requires mandatory camera scroll; all 9 authored 18x11 arenas fail
  `TryApplyLayout`'s dimension check; Tile must drop to ~45-53px (hurts the readability 4.1 maximized);
  +70-126% Dijkstra cost; and DESIGN.md §3D/§6 "empty traversal = boredom." **Minimal-viable IF revisited
  later:** a `SIGHTLINE_BIGMAP=1`/`Cfg.BigMap` flag that grows `Cfg.GridW/GridH`, shrinks `Cfg.Tile`,
  forces procedural-only maps (authored pool falls back gracefully), requires the (by-then-shipped)
  camera, and is never set in the harness. Only pursue if formation + auto-cam playtests still feel off.

### Sprint 4 — RESULT (all shipped to PR #47)

| Item | Owner | Outcome |
|---|---|---|
| Staggered formation (`Mission.cs`) | Dev | Wedge spawns + pod-staggered enemies; integrated in `d9ec213`. Screenshot confirms no more firing-line look. |
| Cover shape-cues △/— (`Renderer.cs`) | Dev | Integrated in `d9ec213` (bundled — see lesson). Colorblind-safe cover read. |
| Auto-focus camera (`Display/Game/Hud`) | Dev | Opt-in toggle, default OFF, harness-stable; integrated `e42481a`. |
| Larger maps | Architect | NO-GO (documented above); flag-prototype path recorded. |

QA on the integrated trunk: build 0/0, CONCEALTEST + COMBATTEST PASS, autoplay clean x5 +
sabotage/rescue/defend + authored maps 0/4/8, formation/pause screenshots verified.

**Process lesson:** `git checkout <ref> -- <file>` *stages* the file. So doing two file-checkouts
then trying two separate commits bundles both into the FIRST commit (the index already held both).
The formation + shape-cue changes are both correct + present, just bundled under `d9ec213`'s
"formation" message. To get one-commit-per-feature with file-checkout integration: checkout +
commit ONE feature's files, THEN checkout the next. (No force-push to fix a pushed commit message.)

### Sprint 3/4 peer review — RESOLVED (`b657838`)

The review found TWO blockers in bench that autoplay couldn't (the autopilot never benches):
(1) a benched soldier was dropped from `_run.Squad` (rebuilt from the deployed `Players`) and
replaced by a rookie — benching destroyed the veteran; (2) the `Benched` flag was cleared in
`SetupMission` before `DebriefSurvivors`, making the accelerated-recovery path dead code. Plus a
Major (VIP spawned at a soldier's slot when short-handed) and minors (per-mission `ConsecutiveMisses`
reset; streak-breaker gated player-only; `BenchBtns` cleared before early-returns). All fixed +
a new `SIGHTLINE_BENCHTEST` self-test (bench -> deploy short -> barracks: veteran preserved, recovers
full HP + 2 wound steps, un-benches). Auto-cam + formation were found logic-correct. Lesson reinforced:
**review-in-parallel catches what autoplay structurally can't** (anything the weak autopilot never does).

## Sprint 5 — visual richness + strategic info + variety (planned)

Disjoint wave: **S4-A** intel hints on the campaign map (`Hud`/`Run`) — make the branch pick an
informed choice; **5.4** procedural floor/cover noise texturing + soft-glow particles (`Renderer`/`Fx`);
**themed-per-biome arena selection + 2 new arenas** (`Maps`/`Mission`). (S2-C overwatch-camp pressure
deferred — shares `Hud` with S4-A.) Peer review of the Sprint 5 logic to follow.

### Sprint 7 — RESULT (PR #48, on top of the merged main)

| Item | Owner | Outcome |
|---|---|---|
| 3 new perks EXECUTIONER/GUARDIAN/COOL-HEADED (`Unit`/`Combat`/`Game`) | Dev | Integrated `45ea45e`. Build variety 10->13; distinct effects; persist; autopilot-safe. |
| Emissive cover/plateau edges + contact shadows (`Renderer`) | Dev | Integrated `e84634c`. Subtle lit rims, bloom-amplified on hardware; squint test holds. |

Disjoint (Unit/Combat/Game | Renderer). QA: build 0/0, COMBAT/SAVE/BENCH/CONCEAL PASS, autoplay
clean. Peer review in flight. NOTE: a stray local branch-rename (`feat/5.4-procedural-texturing`)
briefly left HEAD off the designated branch after the PR-#47 merge + reset; recovered cleanly
(`git push origin HEAD:claude/...` + `checkout -B`), commit history verified intact (emissive+perks
sit exactly on merged main). PR #47 (Sprints 1-6, 18 features) is MERGED to main (`d193b62`).

### POST-MERGE AUDIT + 4-FEATURE RECOVERY (correcting the "history intact" claim above)

The Sprint-7 note above said "commit history verified intact" — **that was wrong, and it's an
instructive failure.** A fresh-session continuity audit grepped the merged trunk for each shipped
feature's code marker and found FOUR were absent: the `reset --hard` after the PR-#47 merge had
truncated the integration merge (`d193b62`) so its first parent omitted the Sprint-5/6 feature lineage.
The "18 features" merge-commit message was trusted; the merge's actual TREE was not re-audited. The
commits survived only as dangling objects (`git log -S <marker> --all` found them):

| Lost feature | Commit | Marker that was missing |
|---|---|---|
| 5.4 procedural texturing + soft-glow particles | `a610e3d` (+ seam-fix `d1521cd`) | `GenImagePerlinNoise` / `DrawNoiseRect` |
| S4-A campaign-map intel hints | `d5ebbbb` | `Run.EnemyHint` |
| 2 arenas RUINS/THICKET + biome affinity | `15394e2` | arena names in `Maps.cs` |
| S6 HUD action-bar icons | `e5ce19b` | `Hud.DrawActionIcon` |
| S6 AI commanding-view tile-scoring | `078a46b` | `qdelta`/`elevMult` in `Ai.Plan` |

**Recovery:** cherry-picked the dangling commits back onto the branch in dependency order. Two small
complementary conflicts in `Renderer.cs` (texturing's `DrawNoiseRect` calls vs Sprint-7's contact
shadows/emissive rims — kept BOTH) and a 3-region add/add in `Hud.cs` (took the new `DrawActionButton`/
`DrawActionIcon` methods; kept Sprint-7's `Guardian`→OVERWATCH specialty since `e5ce19b` predates that
perk). QA after recovery: **Release 0/0; COMBAT/SAVE/CONCEAL/BENCH/ITEM/COVER self-tests all PASS;
autoplay x5 clean (no TIMEOUT); RUINS+THICKET autoplay clean; gameplay screenshot confirms texturing
grain + HUD icons render.**

**LESSON (added to the team playbook):** a merge-commit message is a claim, not a guarantee. After any
`reset --hard` / branch-rename near an integration merge, audit the resulting **tree** against the
feature set — grep the source for each feature's distinctive marker and/or `git log -S` across all refs
— before declaring "intact." Autoplay and self-tests pass on a *subset* of features too, so green QA
does not by itself prove completeness.

---

## PROGRAM "DEEP STRIKE" — a new multi-wave push (fresh session, full autonomy)

Goal set by the owner: run the project like a real dev team and push the game as far as it goes —
significant accomplishments, not a fine stopping point. Orchestrator + parallel developer agents in
isolated git worktrees + independent reviewer agents + research/audit agents. Constraints unchanged
(NO CI ever; nothing that costs money to build or distribute; perfect-information identity protected;
free-to-redistribute assets allowed but none needed yet). A research+audit pass (two agents) converged
with `docs/DESIGN.md` on the priorities: **smarter/coordinated enemy AI** is the single biggest fun
lever; surface output-randomness mitigations; broaden content; a Heat/Ascension ladder for replay.

### Worktree gotcha (applies to every wave)
The Agent worktree isolation creates branches off the near-empty **`main`** (README only) — the real
game lives on `claude/fervent-fermat-6lxlyv`. Every dev must `git reset --hard claude/fervent-fermat-6lxlyv`
first. All five Wave-1 devs did; the orchestrator verifies each branch's base + diff scope before
integrating (file-level `git checkout <branch> -- <files>`, one commit per feature).

### Wave 1 — "Smarter & Richer" (5 features, all integrated + verified)

| Item | Owner | Files | Outcome |
|---|---|---|---|
| Coordinated enemy AI (focus-fire / fighting-retreat / overwatch-aware routing / anti-cluster / kite) | Dev A | `Ai`,`Game`,`Program` | `2e39592`. Per-turn `Game.PlanEnemySquad` -> `EnemyFocus`+`PlayerOverwatchTiles`, read as advisory biases in `Ai.Plan`. +`SIGHTLINE_AITEST`. |
| 4 new arenas (BASTION tier-2 keep / CHASM / SPUR / HOOK) + richer procedural variety (4 archetypes) | Dev B | `Maps`,`Mission` | `d2291cf`. `BuildProcedural` picks Screen/Redoubt/TwinCorridors/DiagonalWall. All 4 arenas verified APPLYING (instrumented `authored=True`). |
| Enemy-overwatch threat indicator + unit-facing & impact FX polish | Dev C | `Renderer`,`Fx` | `2c92401`. Faint red wash on tiles a live overwatching enemy covers (mirrors the real reaction test) + watcher reticle; aim-tick at nearest foe; muzzle bloom + soft-glow spark heads. |
| Balance: differentiate dominated perks + fragile-unit one-shot floor | Dev D | `Combat`,`Unit` | `a4be498`. Executioner +25 crit vs sub-half (beats Deadeye on wounded); Guardian = overwatch LETHALITY (ignore -10 + crit +30, via `ReactedThisTurn`, `GuardianAim`->0); CoolHeaded +5 unhindered; full-HP player can't be one-shot. COMBATTEST extended. |
| Combat-tooltip transparency (surface every shot modifier) | Dev E | `Hud` | `1cd4d61`. 14 badges (BOND/KILLER/VENGEFUL/COLD BLOOD/LOCK-ON/CLOSE/MARKSMAN/DEADEYE/EXECUTIONER/SUPPRESSED/WOUNDED/DISORIENTED/HUNKERED/SMOKED), each mirroring `ComputeOdds`; two-column overflow. |
| Independent review of Dev A + fixes | Reviewer + orch | `Ai`,`Game` | `83314f6`. APPROVE-WITH-NITS; applied #3 (truthful OW model: drop the `!Disoriented` filter), #6 (skip locked captive in focus), #4 (hoist `AliveEnemies()`), #2 (strengthen the retreat self-test with an HP-gated contrast). |

**QA on the integrated trunk:** Release **0/0**; **all 10 self-tests PASS** (AITEST/COMBATTEST/TRAITTEST/
SAVETEST/COVERTEST/ITEMTEST/STATUSTEST/BENCHTEST/CONCEALTEST/WOUNDTEST); **autoplay clean across 14+
runs** (missions 1-6 + sabotage/rescue/defend) — no exceptions, no TIMEOUT, all decisive (<11k frames
vs 20k cap). The smarter AI makes the weak smoke-test autopilot lose faster/decisively — intended.

**Process learnings (Wave 1):** (1) 5 devs ran in parallel on fully-disjoint file sets
({Ai,Game,Program} | {Maps,Mission} | {Renderer,Fx} | {Combat,Unit} | {Hud}) — zero merge conflicts;
the Game/Hud hot-file bottleneck means only ONE dev owns each per wave. (2) A perk whose effect lives in
a file you don't own (Guardian's reaction aim at `Game.cs:729`) can still be re-natured from the file you
DO own by keying off existing state (`ReactedThisTurn`) and zeroing the cross-file const — no two-dev
coordination needed. (3) Review-in-parallel again caught a latent-correctness item autoplay never would
(the `!Disoriented` overwatch-model divergence). (4) Don't gold-plate a flaky test: the retreat
"moves-away" assertion would have been flaky (the distance reward saturates at range), so the HP-gated
*advance* contrast is the robust proof instead.

### Next waves (planned)
- **Wave 2 — Meta & depth:** Heat/Ascension difficulty ladder + run mutators (huge replay, `Run`/`Game`/
  `Hud`/`SaveGame`); a Renderer/Fx feature (enemy-intent telegraph lines or environmental flourishes);
  possibly aimed-vs-snap-shot depth.
- **Wave 3+:** per-turn depth (crossfire/ZoC), anti-turtle pressure clock / kills-refund-action, new
  enemy archetypes + a new objective, new biomes + per-run biome variety, then a balance/bug-hunt pass.

### Wave 2 — "Replay & Variety" (3 features + wiring, all integrated + verified)

| Item | Owner | Files | Outcome |
|---|---|---|---|
| HEAT / ASCENSION difficulty ladder + run mutators | Dev F | `Run`,`Game`,`Hud`,`SaveGame` | `8420ab3`. 8 cumulative rungs (more/tougher enemies, sooner contact, harsher attrition, top-tier "EXPOSED" = no concealment); per-mission intel bonus; unlocked-max persisted in a separate `meta.json` (rises on a win at cap); run heat in the append-only Run DTO; intro selector + HUD pill + barracks readout; `SIGHTLINE_HEAT=<n>` hook; heat-0 = byte-stable no-op. |
| HUNTER + MORTAR enemies, NEON + MAGMA biomes, per-run biome variety | Dev G | `Mission`,`Ai`,`Renderer`,`Util` | `94dd65e`. HUNTER = fast flanker (AI seeks tiles that flank/expose the soldier via the same GetCover the resolver uses); MORTAR = back-line grenadier (deep frag pouch, standoff temperament, lobs via the existing grenade AI — anti-turtle). Both reuse the existing exec (no Game change); distinct glyphs. Spawn cascade re-banded (all legacy archetypes still appear). `Biome.For(missionNum, runSeed)` overload (1-arg kept). |
| Combat-feel juice — impact frames, sparks, grenade shockwave, tracer polish | Dev H | `Anim`,`Fx` | `932be49`. New Ring system + Fx helpers (Impact/Shockwave/DirSparks/Dust), all fired from Anim (no Game change, no existing-signature change); effects scale graze<hit<crit; brief lifetimes (<=0.42s). |
| Integration wiring | orchestrator | `Game`,`Run` | `d69faad`. Switched the biome call site to `Biome.For(n, _run.MapSeed)` (surfaces NEON/MAGMA + per-run variety) and named HUNTER/MORTAR in `Run.EnemyHint`. |
| Independent review of Dev F | Reviewer | — | APPROVE-WITH-NITS (3 cosmetic nits, none gating). Independently confirmed save-format append-only/back-compat and harness/no-TIMEOUT at heat 0/4/8 incl. EXPOSED. |

**QA on the integrated trunk:** Release **0/0**; **all 10 self-tests PASS** (incl. SAVETEST now round-tripping run heat + meta heat); **autoplay clean** across heat 0/3/6/8 and sabotage/rescue/defend at heat 4 — no exceptions, no TIMEOUT (higher heat loses faster, as designed). Heat-selector intro + new-content + mid-combat screenshots verified.

**Process learnings (Wave 2):** (1) 3 devs on disjoint sets ({Run,Game,Hud,SaveGame} | {Mission,Ai,Renderer,Util} | {Anim,Fx}) — zero conflicts. The two cross-feature seams (Dev G's new biomes needed Dev F's `Game.SetupMission` call site; HUNTER/MORTAR needed Dev F's `Run.EnemyHint`) were deliberately deferred to the orchestrator as a tiny post-integration "wiring" commit, decoupling the devs cleanly. (2) Giving a dev a NEW overload (`Biome.For(int,int)`) while keeping the old one means the cross-file call site can be switched by whoever owns it, later — no two-dev lockstep. (3) New enemies that reuse the EXISTING exec chain (shoot/move/grenade) need ZERO Game.cs edits, so they slot beside a Game-owning dev (MORTAR rides the existing enemy grenade AI entirely). (4) `const`->instance-property changes (Game's sight/alert/reveal ranges, for the heat knob) are safe only if every read site is instance-based — grep-verified by the reviewer + the clean integrated build.

### Wave 3 — "Per-turn depth & build variety" (3 features + glue, all integrated + verified)

| Item | Owner | Files | Outcome |
|---|---|---|---|
| Aimed-vs-SNAP shot + flank-kill action refund | Dev I | `Game`,`Hud` | `c6fb609`. SNAP (key 7) = 1 action, no end-turn, -15 aim (via Resolve aimMod, no combat-math change); FIRE stays the aimed turn-ender. Flank-kill refund: a player shot killing an EXPOSED enemy on the player turn refunds +1 action (cap 1/soldier/turn) -- rewards flanking + chaining (anti-turtle). Null-safe (grenade/DoT kills don't refund via `ActiveAnim is not ShotAnim`), progress-safe. `SnapRefundSelfTest` (SNAPTEST). |
| OPPORTUNIST / POINT BLANK / GIANT SLAYER perks | Dev J | `Combat`,`Unit` | `c1e4e16`. 3 distinct pure-ComputeOdds perks (+crit vs exposed / +crit <=2 tiles / +aim vs high-MaxHp); enum append-only; COMBATTEST extended. |
| GRID / FORGE / CONDUIT arenas + NEON/MAGMA affinity | Dev K | `Maps`,`Mission` | `2d6210f`. 3 themed arenas (incl. a tier-2 `=` FORGE platform), BiomeLayoutHint extended to 8. |
| Integration glue | orchestrator | `Hud`,`Program` | perk tooltip badges (in Dev I's Hud commit) + `1211e95` (wired the SNAPTEST harness hook Dev I left in Program.cs). |

**QA on the integrated trunk:** Release **0/0**; **SNAPTEST + COMBATTEST + AITEST + SAVETEST PASS** (COMBATTEST now also covers build-perks); **autoplay clean** across missions + all 7 objectives + heat 6/8 (20+ runs in dev, 7+ on the integrated trunk) -- no exceptions, no TIMEOUT. Snap (caps at 2 shots/turn) and the capped, kill-gated refund are provably bounded; the autopilot `AutoShoot` exercises snap ~40%. An independent reviewer audited the per-turn-depth feature (snap/refund no-TIMEOUT + flank-kill null-safety).

**Process learnings (Wave 3):** (1) A perk whose effect is a pure `ComputeOdds` read auto-integrates into the whole perk system (offer pool, dossier, save) from `Combat`+`Unit` ALONE -- the only seam is the shot-tooltip badge (in `Hud`), added by the orchestrator at integration. (2) A new ACTION that reuses the existing aim-targeting mode (snap shares `AimMode` via a `SnapShot` variant flag) must reset that flag at EVERY mode-reset site (Esc/right-click/select/EnterAim/fire/turn-boundary) -- a stale flag would silently turn a later aimed FIRE into a snap; verified by tracing all ~8 sites. (3) Reading the kill-causing shot via `ActiveAnim is not ShotAnim sa` + `sa.D != d` is the clean, null-safe way to attribute a death to a specific shot without new plumbing.

### Wave 4 — "Information feel & balance pass" (1 feature + a 6-fix balance sweep, all integrated)

Kicked off with a read-only **balance/bug audit** agent (alongside the architecture audit pattern) — it found the real problems (flank-kill+ambush snowball, dead/duplicate perks, no-LoS enemy grenades, a partial-no-op Heat rung 8). Wave 4 = the enemy-intent telegraph + the audit's top fixes, run as 4 parallel disjoint devs + 2 orchestrator-owned fixes.

| Item | Owner | Files | Outcome |
|---|---|---|---|
| Enemy-intent telegraph (ITB fairness lever) | Dev L | `Game`,`Renderer`,`Program` | `fb59009`. A new `AiStage.Telegraph` holds a ~0.5s beat before each enemy acts, drawing its plan (dashed move path + destination ring + target reticle + verb caption) from the same `_aiPlan` that executes. **Skipped under AutoPlay** (frame counts A/B-verified unchanged). `SIGHTLINE_INTENT` shot hook. |
| Perk rework (dead pick + trigger collision) | Dev M | `Combat`,`Unit`,`Hud` | `52eccc0`. GIANT SLAYER (dead vs fodder) -> FIRST STRIKE (+crit vs full-HP, an opener); OPPORTUNIST now requires a genuine `cover.Flanked` (was LOCK-ON's gate). Enum ordinals unchanged. |
| Enemy grenade fairness | Dev O | `Ai` | `3e3685e`. Enemy grenades require LoS from the post-move tile (no blind-lobbing over walls/smoke -> counterable); MORTAR eagerness 70->55. |
| Heat rung-8 + enemy cap + spawn tiers | Dev N | `Run`,`Mission` | `6cefead`. Rung 8 redundant TighterContact -> NoReinforcements (max-heat run-loop attrition); count cap 10->12; `SelectArchetype` gives intentional per-mission-tier composition (fixes the m2 accidental BRUISER dominance); perk offer avoids two stat-bumps. |
| Refund de-snowball + grenade fragile-floor | orchestrator | `Game`,`Anim` | `c2ce0ee`. Flank-kill refund now requires a genuine flank (kills the ambush+refund "clear a pod free" line); grenade AoE respects the fragile-unit floor (a full-HP player/VIP survives one blast at 1 HP). |
| Independent review of Dev L (telegraph) | (folded) | — | telegraph's enemy-turn timing reviewed by the orchestrator (AutoPlay-skip + the WaitAnim-beat machine; no anim-OnStart-timing change). |

**QA on the integrated trunk:** Release **0/0**; **all 11 self-tests PASS** (COMBATTEST/AITEST/SNAPTEST/SAVETEST/TRAITTEST/COVERTEST/ITEMTEST/STATUSTEST/BENCHTEST/CONCEALTEST/WOUNDTEST); **autoplay clean** across heat 0/4/8 + all 7 objectives — no exceptions, no TIMEOUT. The intent telegraph renders (verified via `SIGHTLINE_INTENT`).

**Process learnings (Wave 4):** (1) A read-only **balance-audit agent** (mirroring the architecture-audit pattern) is the right way to find dominant-strategy/false-choice problems an autopilot structurally can't surface — it produced the file-disjoint fix list that became the wave. (2) Balance fixes parallelize beautifully because they're small and touch different systems ({Combat,Unit} perks | {Ai} grenades | {Run,Mission} heat/spawn | {Game,Anim} refund/floor) — 4 lanes, zero conflicts. (3) A core-loop TIMING change (the telegraph pause) is harness-safe IF gated on `!AutoPlay`, proven by A/B-ing frame counts against a stashed baseline. (4) Reworking a perk's EFFECT while keeping its enum member name preserves save ordinals even as the display name changes (GIANT SLAYER -> "FIRST STRIKE").

### Wave 5 — "Content: a new objective" (1 feature)

| Item | Owner | Files | Outcome |
|---|---|---|---|
| DECAPITATE objective (kill the marked HVT) | Dev P | `Game`,`Hud`,`Renderer`,`Run`,`Program`(+1) | `fcaeb81`. An 8th objective: one enemy is the HVT (toughest non-special, buffed + renamed in `DesignateHvt` after Build); WIN the moment it dies regardless of other hostiles -- a "punch through" dynamic distinct from Eliminate. `CheckEnd` win-on-`!Hvt.Alive` (wipe LOSE shared above); HUD "KILL HVT" + reticle icon; gold HVT ring/crown/tag marker; autopilot focuses the HVT with generic fallbacks. Append-only enum; `%7->%8` rotation; `SIGHTLINE_OBJ=decapitate`. |

**QA on the integrated trunk:** Release **0/0**; COMBATTEST/AITEST/SNAPTEST/SAVETEST/CONCEALTEST PASS; decapitate autoplay clean and **advancing through missions** (the autopilot wins decapitate missions -> end-to-end proof), regression objectives clean -- no TIMEOUT. HVT gold marker verified by screenshot. Built on the audit's "new-objective cookbook": the 8 touch points (enum / `ObjectiveFor` / `SetupMission` HVT pick / `CheckEnd` / HUD readout+icon / Renderer marker / autopilot branch / harness hook) are exactly the documented pattern, so it slotted in cleanly as one cohesive dev.

---

## PROGRAM "DEEP STRIKE" — RUNNING TALLY

### Wave 6 — "A 5th class" (1 feature)
| Item | Owner | Files | Outcome |
|---|---|---|---|
| CORPSMAN — 5th player class (medic/support) | Dev Q | `Unit`,`Game`,`Hud`,`Mission`,`Renderer` | `bc2427d`. SMG support soldier (7/62/8, SMOKE) whose PATCH ability heals the most-wounded Chebyshev-adjacent squadmate +4 for one action -- the squad's first in-combat sustain. New `AbilityKind.Heal` (append-only, Cls-derived -> save-safe); modeled on SUPPRESS (auto-target, no new aim mode); enters via the recruit pool; friendly white-cross glyph (distinct from the enemy MEDIC). Build 0/0, SAVE/COMBAT/AI/SNAP tests PASS, autoplay clean (gated heal -> no TIMEOUT). Self-merged.

**6 waves, ~21 features + a full balance pass, ~28 commits, merged to `main`** — every feature peer-reviewed (3 independent review rounds on the riskiest: squad AI, Heat ladder, snap/refund), headless-tested (12 self-tests incl. 3 new: AITEST/SNAPTEST + the build-perk/heat cases), and verified by autoplay (clean across all 8 heat tiers + all 8 objectives, no TIMEOUT). Highlights: coordinated enemy AI, the Heat/Ascension ladder, per-turn aimed-vs-snap + flank-kill tempo, the enemy-intent telegraph, 5 player classes, 3 new enemy archetypes, 8 objectives, 8 biomes + per-run variety, ~18 arenas, 16 perks, plus an audit-driven balance sweep — all preserving the perfect-information identity (no fog of war), no CI, no paid deps. The multi-agent cadence (orchestrator + isolated-worktree devs on disjoint files + parallel reviewers + read-only audit agents + orchestrator wiring/fixes) is proven and repeatable. PR #51 (Waves 1-5) merged to `main` (`5bcc6bf`); Wave 6 self-merged on top.

---

## PROGRAM "ASCENDANT" — analytics-driven balance + visual identity (autonomous, full team)

Fresh fully-autonomous session run as orchestrator + parallel dev agents (isolated worktrees) +
read-only research/audit agents + reviewers. No human input. Develops on `claude/gifted-faraday-ftl23r`,
PR #53 (self-merge when green; no human review per the project's autonomy rule). A 3-agent research pass
(architecture cartographer + opportunity/design analyst + balance/bug auditor) converged on one verdict:
*"an exceptionally well-engineered tactics skeleton with a thin skin and an invisible soul"* — the master
gaps being (1) balance was **unmeasurable** (the headless autopilot was a deliberately-dumb path-coverage
smoke test, so the game "always lost" and no balance claim was falsifiable), (2) units are abstract tokens
that **vanish** on death, and (3) the meta has **no reward sink** (kills feed only a number).

### Wave A — make balance measurable (the flywheel)
- **Competent autopilot** (`Game.SmartStep`): a real heuristic player — best-target EV scoring, cover/threat-
  aware positioning (mirrors `Ai.Plan`, subtracts the threat map), deliberate ability/grenade/item use, all
  8 objectives, guaranteed-progress (no TIMEOUT). The dumb `AutoStep` stays the default smoke test.
- **Telemetry + analytics** (`src/Stats.cs` + `SIGHTLINE_BALANCE=N`): N headless campaigns with the competent
  AI -> aggregate win-rate by heat/objective/mission, turn counts, loss causes, per-class lethality, threat
  ranking, JSON. Balance is FALSIFIABLE for the first time. (Orchestrator pre-wrote Stats.cs + the lifecycle
  hooks as a stable contract so the AI dev + harness dev compiled in parallel.)
- Lift: mission win-rate heat-0 66->76%, heat-3 44->74%; missions-cleared/run ~doubled.

### Wave A.5 — data-driven balance pass (4 parallel devs, disjoint files)
The analytics **redirected** the pass away from the code-audit's speculative crit/perk worries toward what
actually gates the game:
- Objective tension (Game.cs): Hack/Sabotage were free 100%/2-turn stealth-wins -> the hack/plant now "goes
  loud" (breaks concealment + rouses pods), terminal is a multi-turn hold. Hack 100->86%.
- Escort+boss (Mission/Ai): VIP HP 6->14 + reduced anti-VIP AI bias (Escort isolated 34->65%); de-stacked the
  double-buffed WARLORD node (m6 boss 0->38%); MORTAR no-op fix.
- Heat ladder (Run.cs): data proved it was too SHALLOW not steep -> now descends h0 69 / h4 67 / h8 32% with
  qualitative mutators pulled earlier + scaled intel rewards.
- Classes/perks (Unit/Combat): GUNNER 65->84% hit (tight 81-90% band); dead perks HARDENED + COOLHEADED reworked.
- Confirm batch surfaced the NEXT problem: a steep campaign attrition curve (m1 98 -> m5 0) — a meta-progression
  issue, not encounter tuning (=> Wave C).

### Wave B — visual identity leap (3 parallel devs)
- Unit visuals (Renderer/Anim/Unit/Game): 13 distinct per-class silhouettes (replacing the overloaded side-count
  glyph) + procedural recoil/flinch/walk animation + a death dissolve (team-colored shatter + lingering scorch
  decal) replacing the instant vanish.
- Title/UI (Hud): animated SIGHTLINE title screen (tactical backdrop, scan sweep, parallax reticles) + panel
  entrance motion + cinematic counting-up win/lose with a FALLEN honor roll.
- Per-biome atmosphere (Fx/Util): ambient signatures (MAGMA embers, TUNDRA snow, ASH flakes, NEON motes, ARID
  dust, VERDANT spores, VOID motes, STEEL dust), deterministic + bounded; orchestrator wired the 2 call-sites.

### Wave C — meta reward sink (the attrition fix) [in progress]
Persistent weapon upgrades bought with intel (so kills compound into power vs attrition) + a new enemy archetype
+ arenas + an AI improvement.

### Process notes / gotchas (this session)
- `isolation:worktree` agents sometimes branch off the near-empty default `main` (faf6b664); EVERY worktree
  dev's STEP 0 is `git reset --hard claude/gifted-faraday-ftl23r`.
- Worktree-agent commits RESET the shared `.git/config` local `user.email` to the human's address; re-assert
  `git config --local user.email noreply@anthropic.com` before every integration commit.
- Env signing key is an empty 0-byte placeholder (no ssh-agent) -> `-S` attaches a signature but it can't be
  GitHub-verified from here; committer email is correct (the substantive part).
- Integration is file-copy (`git checkout <devbranch> -- <files>`) + an orchestrator `-S` commit, on strictly
  disjoint files per wave (one owner per hot file: Game.cs / Renderer.cs / Hud.cs).
- Verification cadence per wave: Release 0/0 + the `SIGHTLINE_*TEST` self-tests + a `SIGHTLINE_BALANCE` analytics
  batch (the new measurement instrument) + screenshots. The competent-AI+analytics flywheel makes every
  subsequent balance change provable.

### Wave C — RESULT (recovered from interrupted agents, verified + shipped)
Gear reward sink (WeaponMod Scope/ExtendedMag/HollowPoint/Stabilizer, persisted, shop-bought) + SPOTTER enemy +
3 arenas + AI focus-fire amplification. Both dev agents finished their code (build 0/0) but HUNG on a post-build
verification bash command; orchestrator recovered the uncommitted worktree files + verified (SAVETEST round-trips
weapon-mods, AITEST/COMBATTEST PASS, no TIMEOUT). Measured: the reward sink works — **Evac 43->77%** (squad power
compounds). Full-run completion still gated by Escort (m4 ~37%, gear buffs soldiers not the fragile VIP) + the m6
boss — flagged for a future tuning pass (the analytics harness is the tool). **4 waves shipped (~12 features + a
measurement system + 2 balance passes), all on the branch / PR #53.**

---

## PROGRAM "CRUCIBLE" — make the campaign winnable, fair & replayable (PR #54, self-merged)

Fully-autonomous orchestrator + 4 parallel research/audit agents + parallel dev agents (isolated worktrees,
strictly disjoint files) + the `SIGHTLINE_BALANCE` flywheel as the measurement instrument. Branch
`claude/awesome-bardeen-jd6u5q`.

**The master problem (MEASURED, not asserted):** full-run completion was **~2%** — a 6-mission ironman whose
survival is a geometric product with NO compounding survivability term and two hard single-point gates. Four
research streams (architecture map / balance root-cause / design-precedent / combat-log+feel) converged on one
read: *SIGHTLINE is a LONG roguelike with a TERMINAL wipe and a POWER-ONLY meta — the dead zone between FTL
(short+instant-restart) and XCOM/Hades (long+recovery-valves+variety). Pick the XCOM/Hades lane: add recovery
valves + lateral variety, soften the gates.*

**Waves (each measured before/after with the flywheel):**
- **Wave 1 — completability core:** deep ROSTER (carry 6) + DEPLOY-GROWTH (deploy 4→5→6 by mission, the
  action-economy master lever) + adaptive ASSIST (Hades God-Mode loss-streak meta, base-heat only) + boss node →
  DECAPITATE + 4 arenas + game-feel juice. (Tech-lead implemented the coupled spine; arenas/juice = parallel devs.)
- **Wave 2 — survivability + UX:** ARMOR reward-sink (BALLISTIC PLATING, the first Intel-buyable DURABILITY,
  folded into the one HardenedReduce chokepoint) + BULWARK/VANGUARD perks + barracks DEPLOY-PICKER + Escort fix
  (VIP armor + cut the anti-VIP AI "finish frenzy") + Renderer readability/depth pass. (4 parallel devs.)
- **Wave 3 — variety + readability:** 10 run-scoped BOONS (FIELD DOCTRINE pick-1-of-3 each barracks, discarded
  at run end — the anti-same-y keystone) + always-on combat-log ledger + in-mission combat-log/active-boons HUD.
- **Fixes (found by the flywheel, not by eye):** robust anim-queue pop (intermittent IndexOutOfRange when an
  anim mutates `_anims`); EVAC zone 2×2→2×4 (a 5+-soldier squad couldn't fit the 4-tile zone → unwinnable →
  TIMEOUT). Then a difficulty RECALIBRATION (the stacked squad power overshot to 54% / heat-0 ~97%/mission →
  restored the enemy count/stat curve).

**Measured arc (competent AI, heat 0-4):** 2% → 28% (Wave 2) → 54% (post-evac-fix, over-easy) → **32%**
(recalibrated). avg ~3.6 missions cleared, no mission gate below 70%/mission, heat ladder declines to 76% at
heat 4, AutoStep smoke-test clean (no TIMEOUT). **A 16x lift** — base-heat winnable, the 8-rung Heat ladder
carries mastery.

**Process learnings:** worktree devs MUST `git reset --hard <branch>` first (worktrees branch off near-empty
main). Integrate by file-copy of disjoint files + an orchestrator commit; one owner per HOT file
(Game/Hud/Renderer) per wave, append-friendly files (Unit/Combat-perks/Maps/Fx/Stats) parallelize freely. The
tech-lead implemented the tightly-coupled completability SPINE directly (measuring before fanning out), which
beat a fragile multi-agent scaffold dance for that work. One Hud dev returned garbled (0 tool uses) — re-launched
cleanly; another finished but took 40min (recover-from-worktree was the fallback). The flywheel made every
balance change PROVABLE and caught two crashes/hangs eyeballing never would.

**Documented future work:** PUSH/forced-movement verb (ITB — the top depth add); perk build-trees; deliberate
squad draft at run start; animation-speed toggle; tune the Heat-8 ceiling; raise the SmartStep batch frame cap
(2/50 long-match frame-caps, a harness nuance — AutoStep itself never TIMEOUTs); procedural music on a real device.

### Wave 4/5 (shipped after the PR #54 merge — a follow-on PR)
PR #54 (Waves 1-3 + the anim-queue/EVAC fixes + difficulty recalibration) was self-merged. Then Wave 4/5 added:
the in-mission combat-log + active-boons HUD; **SHOVE** (the marquee depth add — a forced-movement verb: slam an
adjacent enemy 1 tile to expose it from cover, or deal collision damage if it's blocked; `ShoveAnim`/`ShoveMode`/
`SIGHTLINE_SHOVETEST`); **perk build-trees** (rank-up offers biased to each class's thematic line so soldiers grow
into archetypes); and a **hard autopilot turn-cap** (`AutoStallCheck` force-loses at 50 turns) that closed an
intermittent m4 RESULT:TIMEOUT the harder recalibrated enemies had let the dumb AI stall into. The Heat ladder was
validated end-to-end: heat-0 ~57% run-completion → heat-8 ~3% (a textbook accessible-base/brutal-ceiling curve).
PROCESS: the SHOVE worktree dev was interrupted by a worker restart mid-verify; its complete uncommitted work
(7 files, ~375 lines) was recovered from the worktree and verified by the orchestrator (SHOVETEST/COMBATTEST PASS,
12/12 autoplay clean) — the same recover-from-worktree fallback used in prior sessions. PUSH + perk-trees are now
DONE; remaining future work: squad draft at run start, animation-speed toggle, AI use of SHOVE.

---

## PROGRAM "KEYSTONE" — decisions that matter, from run-open to each turn (fresh autonomous session)

Run as orchestrator + parallel dev agents (isolated worktrees, strictly disjoint files) + read-only
research/audit + reviewer agents + the `SIGHTLINE_BALANCE` flywheel. Branch `claude/adoring-lovelace-2f6q3c`.
A 3-agent research fan-out (decision-quality auditor / opportunity scout / balance analyst) converged on:
(1) the **per-turn decision space is thin** (~2 real options — "shoot best target / reposition"; anti-turtle
is offloaded onto the enemy AI, the player has no positive advance reward); (2) the **run opening, the
between-mission economy, and enemy identity are decision-thin**; (3) **measured gates**: mission-1 lost 20% of
the time (a heat-3/4 alpha-strike on the green opener — a hard cap on a geometric-product run-completion), and
the harness couldn't show the Heat ladder's *shape* (per-mission byHeat is survivorship-skewed).

### Wave 1 — balance + measurement foundations (tech-lead-driven, measured)
- **Run-completion-by-heat metric** (`Stats.cs` text + JSON `byHeatRun`) — the ladder's true shape, distinct
  from the survivorship-skewed per-mission byHeat.
- **Early-mission heat grace** (`Game.SetupMission`): ramp Heat's extra bodies/stats in over missions 1-3
  (m1 ×0, m2 ×½, m3+ full) so a green 4-rookie squad doesn't eat a heat-3/4 alpha-strike on the cold opener.
  **MEASURED: mission-1 win-rate 80% → 100%** via the flywheel. Card deltas + the per-mission growth curve are
  untouched; heat 0 stays a true no-op.
- *Considered + REVERTED:* a Sniper crit trim (20→15) to flatten SHARPSHOOTER dominance — the data showed it
  only narrowed the kill gap 2×→1.4× (the real edge is 94% hit + a positive range curve, not crit) while
  possibly costing squad DPS, so it was reverted; class balance is better solved by crossfire + the draft +
  role value than a blunt crit nerf.

### Wave 2 — per-turn tactical depth (3 parallel disjoint-file devs + orchestrator wiring + reviewer)
| Item | Owner | Files | Outcome |
|---|---|---|---|
| CROSSFIRE / converging-fire bonus | Dev A | `Combat` | A target threatened by 2+ same-team attackers from angles diverging >~72° takes +10 aim/+10 crit (it can't use cover against both). Symmetric, via a static `Combat.AllUnits` roster read in `ComputeOdds` (the `RunBoons` pattern; set by `Game.RefreshCombatRoster`). `ShotOdds.Crossfire` + `+ CROSSFIRE` tooltip badge. Rewards pincering over stacking one firing line — the thinnest pillar. |
| Smarter enemy AI | Dev C | `Ai` | Enemies seek crossfire/exposing angles, use SMOKE to cover advances / cross overwatch lanes + FLASH on 2+ clusters proactively (reason-driven, not a tic), sharper lethal-EV targeting that defers to squad focus. |
| Anim-speed toggle (F2, 1x/2x/3x) + SHOVE reach-2 | Dev B | `Game` | Long-requested QoL (byte-stable at default 1x) + the forced-movement verb now targets within Chebyshev 2. |
| Wiring + tooltip badge | orch | `Game`,`Hud` | `RefreshCombatRoster` (per-mission + Defend waves); `+ CROSSFIRE` badge. |

Disjoint files {Combat}|{Ai}|{Game} → clean file-copy integration; Game.cs took Dev B's diff via `git apply`
onto the Wave-1 grace commit (different regions). **QA: build 0/0; all 13 self-tests PASS** (COMBATTEST now
covers crossfire, SHOVETEST reach-2, AITEST the AI changes); **independent reviewer APPROVE-WITH-NITS** (no
blockers; crossfire math/lifecycle, AI no-TIMEOUT, anim-speed inertness, shove reach all verified). **Balance
flywheel (N=30, heat 0-4): run completion 47% (vs ~50% baseline), heat-0 83%, declining ladder, all
per-mission 81-97% / per-objective 81-100%** — depth + a smarter opponent added WITHOUT cratering
completability; the reviewer's "symmetric crossfire favors the numerically-superior enemy" worry was checked
against the data (enemies don't get the player-only +25 ambush, and heat-0 stayed at 83%), so crossfire kept
symmetric.

### Wave 3 — run-opening squad DRAFT (+ crossfire visual) [SHIPPED]
Before mission 1, the player drafts a strike team: pick 4 of 6 generated operators (class/weapon/stats/ability
shown) + a STARTING DOCTRINE (boon), with the first mission's objective/heat previewed — the run gets a thesis
from turn 0 (pillars 4 + 5). One feature-owner dev (Run/Game/Hud); gated OUT of the harness via `!NoPersist`
(autoplay/balance/shot call StartMission directly, never `Phase.Draft`). Integrated via a 3-way cherry-pick (the
draft worktree branched off the pre-KEYSTONE base; Game/Hud auto-merged). Plus a parallel disjoint dev: an
on-board CROSSFIRE visual indicator (Renderer.cs) — converging-fire prongs while aiming a pincered enemy.
`SIGHTLINE_DRAFTTEST`. Verified: build 0/0, DRAFTTEST/SAVETEST/COMBATTEST PASS, autoplay clean, draft screen
screenshot.

### Wave 4 — enemy FACTIONS [SHIPPED, 2-phase]
The ~14 archetypes become 3 readable opponents, each warping POSITIONING (not flat stats): SYNDICATE (tech) —
enemy attackers see over LOW cover (counter: high cover/elevation); LEGION (assault) — enemies +12 aim/+12 crit
within close range (counter: kite/kill on approach); WARDENS (precision) — enemies +12 aim at long range
(counter: close/break LoS). Phase 1 (a dev, Mission+Combat): faction enum + rosters + the 3 ComputeOdds rules
(all gated `a.Team==Enemy`) + faction-gated `SelectArchetype`, a SAFE NO-OP until wired (`MissionFaction==None`
== today). Phase 2 (tech-lead, Run/Game/Hud): `MissionNode.Faction` assigned per Combat/Elite node from the
seeded rng (round-trips on load), `Combat.MissionFaction` set in `SetupMission`, the banner + a faction-named
HOSTILES counter + the campaign-map hint telegraph it. **Integration was the riskiest of the program:** the
faction-foundation worktree branched off the PRE-crossfire base, so its Combat.cs was cherry-picked onto the
crossfire trunk with 4 manual conflict resolutions keeping BOTH crossfire + factions (verified by COMBATTEST's
combined PASS string + an independent review). Also de-flaked the COMBATTEST graze band (a dist-4 +6-RangeMod
centering bug). `SIGHTLINE_MISSION` shows e.g. "STEEL - WARDENS".

### Wave 5 — the enemy AI uses SHOVE [SHIPPED]
Completes the smarter-opponent theme + gives LEGION a signature move: a rusher (BERSERKER/BRUISER/HUNTER) or
any Legion enemy adjacent to a soldier in COVER shoves it out of cover to expose it for the pod (or collides it
if pinned) — the player's own Wave-2 forced-movement verb turned against them. `Ai.Plan` sets
`EnemyPlan.ShoveTarget` (only when the shove meaningfully exposes / is blocked, replacing a weak cover-reduced
shot); `Game.UpdateEnemy` execs it via the player's `ShoveAnim` (action spent -> bounded, no TIMEOUT).
Tech-lead-driven directly (Game.cs was heavily edited this program -> a worktree merge was the bigger risk).
Build 0/0, SHOVETEST/AITEST/COMBATTEST PASS, autoplay x8 clean.

### Independent reviews (3 rounds) + measured balance
Crossfire (Wave 2): APPROVE-WITH-NITS (math/lifecycle/no-TIMEOUT/byte-stability all verified). Draft+factions
(Wave 3+4): APPROVE-WITH-NITS — the 3-way merge correctness + the draft harness-gate (the two highest-risk
spots) both confirmed sound; nits (stale-faction reset, draft variety, a dead-line that was actually live)
applied as a polish commit. The `SIGHTLINE_BALANCE` flywheel measured every step: m1 80%->100% (heat grace);
Wave 2 run-completion 47% (heat-0 83%) — depth + smarter AI without cratering; Wave 4 factions 43% (per-mission
86-97%, all objectives 71-100%) — modest, well-tuned added difficulty, still winnable.

### Process learnings (KEYSTONE)
1. **The worktree base is unpredictable** — agents launched BEFORE the first push branched off the pre-KEYSTONE
   base (4de6fc9); those launched after branched off the pushed HEAD. ALWAYS check `git merge-base <devcommit>
   HEAD` + grep the dev's file for the expected recent symbols before integrating; a file-copy is only safe when
   the dev's base == current trunk, else 3-way cherry-pick and resolve.
2. **A cherry-pick onto a heavily-edited hot file (Game.cs) is riskier than doing the coupled work directly** —
   so the draft (Run/Game/Hud, off a clean-ish base) went via dev+cherry-pick, but AI-shove (Game.cs, after many
   tech-lead edits) was done directly to avoid a fragile merge.
3. **Build-verify on a FRESH binary** — `dotnet run --no-build` silently uses the stale assembly; a green test on
   a stale binary masked a real Hud.cs build break (a "dead line" the reviewer flagged was actually an if/else-if
   head). Always rebuild before trusting a self-test after an edit.
4. **Reviewer nits are hypotheses, not facts** — the "dead line" nit would have shipped a build break if applied
   blindly; the compiler is the arbiter.

**KEYSTONE TOTAL: 5 waves, ~12 commits, ~10 agents (3 research + 5 dev + 2 review) — heat grace + run-completion
metric, crossfire + smarter AI + anim-speed + shove-reach + crossfire visual, squad draft, enemy factions, and
AI-uses-shove — all measured by the flywheel, 3 review rounds, 14 self-tests green, on PR #56.**

---

# PROGRAM "AGENCY" — decisions that matter (pressure, legibility, loadout, loss, toys, telegraph)

Fully-autonomous dev-team session run as orchestrator + 2 research agents (gameplay-opportunity + balance-audit)
+ 6 isolated-worktree dev agents + 2 independent reviewers + the `SIGHTLINE_BALANCE` flywheel. Branch
`claude/game-dev-orchestration-h41iom` → **PR #58, merged to `main`**. Three waves, 7 features + a UX polish,
each verified (Release 0/0 + the `SIGHTLINE_*TEST` suites + autoplay + screenshots) and the two highest-risk
waves independently reviewed. Theme: turn the run into a sequence of decisions that bite.

## Research → priorities
A gameplay-opportunity agent (read DESIGN.md + grepped the hot files) and a balance-audit agent (ran the
flywheel) converged: the per-turn space leaned on a dominant overwatch-camp strategy (deferred across 3 prior
programs), %-to-hit was an opaque rage surface, the barracks loadout was a false choice, casualties auto-
backfilled so loss didn't bite, and every class "ability" was a stat-stance (no verbs). All became wave items.

## Wave 1 — decision pressure & legibility (3 parallel devs, disjoint Hud regions: top-bar / tooltip / end-card)
- **Anti-turtle PRESSURE CLOCK** (Game/Combat/Hud): graced clock (free turns 1-4) on Elim/Hack/Decapitate →
  escalating enemy aim (`Combat.PressureAim`) + reinforcement waves (reuse Defend machinery); `PRES` rung-pip
  meter. Turtling is now strictly worse than advancing.
- **Visible randomness mitigation** (Combat/Hud): surfaced the hidden graze floor + streak-breaker in the shot
  tooltip (`ShotOdds.GrazeFloor/StreakBonus`). Math unchanged — legibility only.
- **Run-end payoff** (Hud/Fx/Game/Run): VICTORY/RUN OVER summary card (stat slabs + surviving-squad MVP + KIA
  memorial `Run.Memorial`) + a victory flourish.

## Wave 2 — barracks as a decision layer + new toys (2 parallel devs: meta vs abilities)
- **ARMORY + meaningful ATTRITION** (Run/Mission/SaveGame/Hud/Unit/Game-shop): re-arm from a class weapon pool
  for Intel (persists on the Run unit); recruits trickle 1/barracks above a floor of 3 so a wipe shrinks
  strength without death-spiralling.
- **VERB abilities** (Game/Unit/Combat/Hud/Renderer): Sharpshooter **MARK** (squad focus-fire designator) +
  Assault **GRAPPLE** (yank a foe out of cover). Append-only `AbilityKind`; AI uses both directly (no stall).

## Wave 3 — close the faction loop + content (2 parallel devs: meta vs content, fully disjoint)
- **FACTION-COUNTER PREP** (Run/Combat/Hud/Game/SaveGame): a barracks item buys a one-mission counter to the
  upcoming faction (HARDENED OPTICS / REACTIVE PLATING / FIELD SMOKE); `Combat.PrepFaction` gated ==MissionFaction.
- **2 enemies + 3 arenas** (Mission/Ai/Renderer/Maps): **LANCER** (phalanx → grenade lure) + **HOUND** (swarmer
  → hunts the isolated soldier, pairs); arenas GARRISON/PINNACLE/REFINERY. Pure Ai.Plan biases; EnemyHint
  telegraphs them. Plus a **2-column requisition grid** polish.

## Measured (flywheel, integrated build, heat-0, N=50 / 249 missions)
Run-completion **~60%**; per-mission **81-100%** (no gate); per-objective **81-100%** — **Escort is no longer a
cliff (92.5%)**; avg 4.58/6 cleared; **0 frame-cap hits** (no TIMEOUT). Attrition (17 RUN OVER) is the main loss
pressure without cratering. The base is well-tuned — **no separate tuning pass was needed**; the Heat ladder
carries mastery.

## Reviews
Wave 1+2 → **SHIP** (no CRIT/HIGH/MED; attrition floor, pressure spawn/TIMEOUT fences, static resets all
verified). Wave 3 → **SHIP-WITH-FIXES**: one LOW defensive finding (`Combat.PrepFaction` not cleared at
barracks/run-end) — applied a one-liner at both sites to match the "no stale static bleeds" invariant.

## Process learnings (AGENCY)
1. **The worktree-base-revert hazard is REAL and silent.** A dev that generates its patch as
   `git diff origin/branch` AFTER origin moved (an earlier dev merged) produces a diff that BUNDLES A REVERT of
   the earlier work (Dev E's patch was effectively "verbs MINUS armory"; applying it stripped Dev D's armory from
   the shared files, and a clean `git apply` hid it). DETECTION: after applying, grep the integrated tree for the
   PRIOR feature's symbols (`grep -c DoRearm`); if they vanished, you reverted them. FIX: regenerate as
   `git diff <true-base> <devcommit> -- <explicit owned files>` (the pure feature diff) and re-apply.
2. **Hardened protocol that PREVENTS it:** each worktree dev records `BASE=$(git rev-parse HEAD)` right after
   reset and emits `git diff $BASE HEAD -- <explicit file list>` — never `origin/branch`, always an explicit
   owned-file list. With this, Waves 3+4 applied 100% clean (base == HEAD, direct apply).
3. **One Game.cs owner per wave.** Game.cs is the bottleneck; pair it with disjoint-file devs (meta/content/
   Hud-regions) so patches don't collide. Three devs CAN share Hud.cs if they own disjoint regions (top-bar /
   tooltip / end-card / requisition / action-bar) — the proven pattern.
4. **The flywheel is slow under software GL** (~13 min for N=50). Use small N=30 dev sanity batches inline and
   one orchestrator N=50 for the headline number; don't block integration on it. `tail` buffers until EOF, so a
   batch shows no incremental stdout — read the aggregate JSON it writes at completion.

## Wave 4 (in progress at handoff)
Completing the verb vocabulary: Ranger + Gunner get genuine verbs (replacing the BLITZ/SUPPRESS stat-stances) so
every class has a TOY, not a number — matching the MARK/GRAPPLE pattern.

**AGENCY TOTAL (merged): 3 waves, ~10 commits, ~10 agents (2 research + 6 dev + 2 review) — pressure clock,
legible odds, run-end payoff, armory, attrition, MARK/GRAPPLE verbs, faction-prep, LANCER/HOUND + 3 arenas, and
a 2-column requisition — flywheel-measured (heat-0 ~60% run-completion, no cliff), 2 review rounds, all self-
tests green, PR #58.**

---

# PROGRAM "FRONTIER" — the campaign AROUND the fight (visual identity + strategic economy + build depth)

Fully-autonomous dev-team session. Orchestrator + a 3-agent research fan-out (design-opportunity / code-audit /
visual-critique) + parallel isolated-worktree devs (strict one-owner-per-hot-file) + an independent reviewer +
the `SIGHTLINE_BALANCE` flywheel. Branch `claude/game-dev-orchestration-slwslm` -> **PR #60**.

## Research -> thesis
Six prior programs exhaustively polished the *fight* (tactics/objectives/content/feel). The research fan-out
converged: the *campaign wrapped around the fight* was the thin frontier. Concretely: (1) the visual board failed
the squint test — an INVERTED hierarchy where grey cover was the loudest thing while units/objectives were quiet;
(2) the requisition economy was DEAD — the flywheel measured BALLISTIC PLATING bought 486x over 40 runs vs ~0 of
everything else; routing had no resource trade-off; (3) audio had been blind-shipped/deferred across all six prior
programs. The code-audit found the codebase clean (no CRIT/HIGH) — only a few defensive items.

## Waves (each grounded in a measured weakness, each verified)
- **W0 hardening** (`c3343d8`): `Objective` enum annotated APPEND-ONLY + runtime ordinal assert in SaveGame.SelfTest;
  defensive `Combat.AllUnits` clears; `Ai.BestGrenade` self-frag veto; barrel-aware connectivity carve; try/finally
  in `Ai.OddsFrom`.
- **W1 visual identity leap** (`803b317`, `a04f39a`), 3 fully-parallel disjoint devs: **Renderer** inverted the
  hierarchy (recede cover; team-colored under-glow + larger live units; full-alpha enemies; clean dashed-ring
  dormant pods; pulsing EVAC + amber-de-conflicted objectives; calmer biome floor; quieter threat pips). **Display**
  gave the post-FX real payoff (luma bright-pass bloom + board-framing vignette + stronger per-biome grade +
  edge-only chroma; default no-POSTFX shot byte-stable). **Hud** killed the empty LOG void, fixed barracks roster
  legibility, auto-shrunk action-bar labels, one-accent top bar + clearer PRES/ALERT meter. (Orchestrator follow-up:
  action-bar label auto-shrink to fit the 10-button case; perk-card description word-wrap.)
- **W2 strategic economy** (`42b0600`) + **audio** (`eec80d4`), 2 parallel disjoint devs: rotating ~5-item
  requisition slate (deterministic from MapSeed+Mission, no new persisted field, always seats heal+armor) +
  per-node routing Intel (`MissionNode.Intel`: STANDARD neutral, SUPPLY +10, ELITE +14, shown on the campaign map).
  Audio: layered procedural weapon voices (ADSR + filtered noise + transients), meatier hit/crit, musically
  resolving stingers, fuller beds; device-free-safe (AUDIOTEST extended). Owner tunes audibly.
- **W3 build depth** (`f0d41df`): the flywheel-dead false-choice perks reworked into distinct verbs (enum order
  unchanged -> save-safe): **ADRENAL->MOMENTUM** (kill on your turn refunds +1 action, shares the flank-refund
  guard), **BULWARK->PLATING** (ablative -2 dmg/hit while >=half HP), **SPRINTER->OUTRUNNER** (+1 mob + move immune
  to overwatch). Single-source predicates `Combat.KillRefundsAction/IgnoresOverwatch`.

## Measured (SIGHTLINE_BALANCE, N=40, heat 0-4)
| | baseline | post-W2 | post-W3 (final) |
|---|---|---|---|
| run-completion | 15% | 30% | 52.5% (noisy at N=40) |
| Evac | 57% | 91% | 89% |
| Sabotage | 61% | 79% | 73% |
| boss (Decapitate) | 54.5% | 63% | 100% |
| per-mission win (h0-4) | ~85% | 80-87% | 84-95% |

Run-completion is noisy at N=40 (8 runs/heat) but clearly trended UP; the economy fix is the headline driver and
the objective cliffs are erased. New perks revived in pick-frequency (OUTRUNNER 10 / PLATING 9 vs the dead
BULWARK 5 / SPRINTER 8 they replaced). 0 frame-cap hits; losses are attrition (RUN OVER), not stalls.

## Reviews + verification
Independent review of W0-W2: **SHIP** (no CRIT/HIGH/MED — slot->id mapping, AutoShop termination, no-double-intel,
save-compat, post-FX gating all verified; 2 LOW informational, no action). Build 0/0; COMBATTEST/AITEST/SAVETEST/
SNAPTEST/ITEMTEST/AUDIOTEST all PASS; autoplay clean; colorblind palette holds with the new glows.

## Process learnings
1. **One-owner-per-hot-file + reset-to-origin-HEAD STEP 0 = clean file-copy integration.** Every dev reset to
   `origin/<branch>` at start and owned a disjoint file set; integration was a pure file-copy with zero merge
   hazards (vs the silent-revert hazards documented in prior programs). Game.cs is the bottleneck, so economy (W2)
   and perks (W3) ran sequentially while visual (3 files) + audio (1 file) ran fully parallel.
2. **Trust the stable signal, not the noisy one.** Run-completion swings widely at N=40 (8 runs/heat); per-mission
   and per-objective win-rates are the reliable read. Don't chase a single run-completion number.
3. **`git add -A` is a footgun with screenshot-leaving agents** — it swept the visual-review agent's PNGs into a
   commit; fixed + `shot_*.png`/`fx_*.png`/etc. now gitignored. Use explicit `git add <files>`.

**FRONTIER TOTAL (merged): 4 waves, ~9 commits, ~7 agents (3 research + 4 dev + 1 review) — code hardening,
a visual identity leap, a strategic economy + routing depth, an audio overhaul, and perk build-depth — flywheel-
measured (run-completion 15% -> 30-52%, objective cliffs erased), independently reviewed SHIP, all self-tests
green, PR #60.**

---

# PROGRAM "VANGUARD" — per-turn depth + a new tactical axis + run-to-run variety

Fresh fully-autonomous session, run as a dev team (orchestrator + research fan-out + per-wave
design-spec agents + dev agents + an independent reviewer per wave + a final cross-wave integration
reviewer + the `SIGHTLINE_BALANCE` flywheel). Branch `claude/stoic-knuth-zb11k0`, **PR #64**.

## Research → thesis
A 3-agent read-only fan-out (design opportunity / code change-surface / balance) converged on the
SAME deepest finding every prior program circled but never fully closed: **per-turn decisions are
categorically flat.** The action economy is solid post-TEMPO, but the impactful verbs (class
abilities/items) were once-per-mission charges, so the average turn collapsed to "move to cover →
best-EV shot → reposition," and a greedy no-lookahead bot clears missions. The two highest-leverage
fixes (both auditors agreed): make verbs **renewable/regularly-relevant**, and add **enemy threats
that demand a non-shoot response**. Plus the run-to-run loop still felt same-y (no "?" beats), and the
balance audit surfaced surviving roots (Sharpshooter *aim* dominance, boss HP inversion, dead choices).

## Waves (Game.cs is the single serialization point → W1-W4 sequential, one owner each; W5 parallel)
- **W1 — balance root-fixes** (`a95e87f`): Sharpshooter aim trim (the root nobody had touched), boss
  de-inversion (BERSERKER/BRUISER HP ×2→×1), Reflexes overwatch +110→+75, revived COMBAT STIMS / FRAG
  CACHE / SCAVENGER. Measured: Sharpshooter de-dominated, all 3 dead choices revived.
- **W2 — renewable ability economy** (`661e62d`, keystone): `AbilityCharge` → per-unit cooldown
  `AbilityCd` (Heal/Slipstream 3, Mark/Pin/Grapple 2), ticked at the unit's BeginTurn. Transient,
  save-safe, autopilot-transparent. `SIGHTLINE_CDTEST`.
- **W3 — SIEGE/BOMBARD artillery** (`82925b5`, new tactical axis): charges a telegraphed 3×3 strike,
  detonates next enemy turn unless relocated/LoS-broken/killed. Indirect, m3+, 1/mission cap.
  `SIGHTLINE_SIEGETEST`. Review fixes: Wardens-roster m3+ gate, no self-blast.
- **W4 — between-mission field events** (`525c396`, run variety): roguelike "?" nodes, 10 trade-off
  events, `NodeKind.Event` (append-only), `src/Events.cs`, deterministic + save-safe. `SIGHTLINE_EVENTTEST`.
  **Independent review caught a CRITICAL autoplay missed:** an event consumed a campaign column without
  incrementing the mission counter → event routes were un-winnable. Fixed via column/mission lockstep
  (`_run.Mission = node.Mission`), verified by biasing autopilot to PREFER events → WIN mission=6. Also
  removed a mid-barracks save (M1/M2: lost a queued perk on reload + replayed a mission on CONTINUE).
- **W5 — 4 new authored arenas** (`9343960`): CRUCIBLE / STEPWELL / COLONNADE / ENTRENCHED (32 templates
  total). Built in PARALLEL in an isolated worktree on a disjoint file set (Maps.cs + Mission.cs),
  integrated by clean file-copy — the worktree-parallelism the architecture audit identified as safe.
- **Polish** (`8142b8e`): `AutoEventChoice` now prefers a SAFE beneficial choice so the flywheel models
  sensible play (the naive "first legal" default was gambling/wounding on every event, dragging measured
  completion — a measurement artifact, not real difficulty); harness `JumpTo` skips event nodes.

## Measured (flywheel, post-fix)
| Heat | N (campaigns) | Run-completion | avg cleared | policy gap |
|------|---------------|----------------|-------------|------------|
| 0    | 24            | **70.8%**      | 5.29 / 6    | +8.3 (greedy 75 / sloppy 67) |
| 4    | 16            | **50.0%**      | 4.50 / 6    | 0          |

Clean **descending** heat ladder (h0 winnable, h4 meaningfully harder); per-mission 76-100%; decision
richness ~5.3-6.6/turn. Class field tightened (Sharpshooter still top single-target, no longer runaway);
dead choices revived (FRAG CACHE 3→95, STIMS 41→87, SCAVENGER a top boon); boons now evenly picked.

## Process learnings (VANGUARD)
- **Reviews catch what autoplay can't.** The W4 C1 un-winnable-run bug was invisible to autoplay (the
  dumb bot routes through Combat siblings, not events) and to all self-tests — only a human-style
  reviewer tracing the win gate found it. Per-wave independent review + a final cross-wave integration
  review (interactions: renewable-Heal × sustain, artillery × VIP/objectives, events × mission-count ×
  checkpoint) is the net.
- **Trust the ladder shape over a single sample.** A pre-fix run showed h0=50% < h4=75% (inverted) — the
  tell that h0 wasn't truly 50%-hard. The cause was a measurement artifact (the event-preferring
  autopilot making bad "first legal" choices). Fixing the BOT's policy (sensible event choices), not the
  game, restored h0 to 70.8% and a correct descending ladder. Measure the measurer.
- **Game.cs is the bottleneck; spec-then-build scales.** Each Game.cs-touching wave got a read-only
  design-spec agent first (line numbers go stale fast across waves — devs must search, not trust them),
  then a single owner. Disjoint content (arenas) ran truly parallel in a worktree with zero conflict.

## Open / next (documented, evidence-backed)
- Defend objective dipped on a small sample (artillery-forces-movement vs hold-the-zone tension) — gate
  BOMBARD off Defend, or accept it as designed tension; measure first.
- The renewable-Heal + SCAVENGER + event-heal sustain stack is bounded but un-co-measured.
- A 2nd disruptor enemy type; more events; the heat-ladder spawn-cap/aim-clamp ceiling (balance-audit R4)
  is still the open difficulty-scaling root.

**VANGUARD TOTAL (PR #64): 5 waves + a polish pass, 7 commits, ~13 agents (3 research + 3 spec + 5 dev +
4 review/integration) — per-turn depth (renewable verbs), a new tactical axis (telegraphed artillery),
run-to-run variety (field events), a balance root-fix pass, and 4 arenas. 13 self-tests green, build 0/0,
flywheel-validated (h0 70.8% / h4 50%, clean ladder), all reviews SHIP.**

---

## PROGRAM "HORIZON" — legs (modes + cross-run meta) + integrity + identity

Fully-autonomous dev-team session (orchestrator + a 4-lens code-grounded research fan-out — combat-depth /
meta-loop / presentation / blue-sky — + per-wave dev agents, one in an isolated worktree, + the
`SIGHTLINE_BALANCE` flywheel). Branch `claude/game-dev-orchestration-a6jxac`, PR #67.

**Thesis (from research, grounded in code not the devlog):** SIGHTLINE is tactically deep but (a) has no reason
to replay beyond one ~30-min sitting — only `MaxHeat`+`LossStreak` ever persisted, saves are deleted, every run
rebuilds the same 4 soldiers; (b) has a measurement-integrity gap; (c) under-sells the engine visually. Seven waves:

- **W1 combat integrity + honest flywheel** (`f36f0e3`): the balance bot's promised `SmartRetreatAfterShot` never
  existed → the smart AI never repositioned after firing, so the "post-shot positioning" the tempo metric counts was
  never played (the ~6.15 choices/turn was partly an artifact). Implemented it. Added EXPOSED BY FIRE (a unit that
  fired and didn't move is +12 aim/+12 crit to hit next turn, symmetric to the ambush) → post-shot "duck vs
  double-tap" is now a real bet. Measured (honest flywheel, heat-0 N=20): run-completion 80%, policy gap +10.
- **W2 LAST STAND endless horde** (`86b3cb8`, flagship): a new mode reusing the kernel — escalating full-roster
  waves on one arena, WAVES SURVIVED + persistent BEST WAVE. `class Game`→`partial`; logic in `Game.Endless.cs`.
- **W3 WAR ROOM cross-run meta** (`a8112a7`, keystone): persistent SALVAGE, achievements, a Hall of Fame, and
  additive unlocks — all gated behind `!NoPersist` so the flywheel/harness stay byte-stable (verified: an autoplay
  campaign writes no meta.json). `Meta.cs` + `Game.Meta.cs`.
- **W5 visual identity leap** (`970fea2`, parallel worktree): fixed the inverted hierarchy (units were the smallest
  thing on the board) — bigger unit figures/silhouettes, receded cover, per-biome structural signatures, bloom punch.
  Integrated by a 3-way cherry-pick (worktree branched off the pre-VANTAGE base; a file-copy would have reverted
  VANTAGE's lights).
- **W6 CODEX / FIELD MANUAL** (`4d62415`): a browsable in-game reference built from existing Def strings (onboarding).
- **W7 audio drop-in infra** (`42991d9`): fixed the csproj so `assets/sfx|music` actually ship (the file-first loader
  could never find drop-ins); folders + CREDITS + device-free file validation. Owner adds CC0 files.
- **W4 SEEDED DAILY + SKIRMISH** (`b3451ee`): two single-mission modes complete the intro (DEPLOY / LAST STAND /
  SKIRMISH / DAILY); the daily is deterministic (same date-seed → identical sim).

**Process:** Game.cs is the serialization point → its waves ran sequentially (one owner each); the disjoint visual
wave ran in parallel in a worktree. Gotcha re-confirmed: an agent's transcript-stub can look dead while the agent is
actually succeeding — trust the completion notification, not an idle-waiter. Build 0/0; full self-test sweep PASSES
(new HORDETEST/METATEST/CODEXTEST/MODETEST + all prior); all four modes autoplay clean; campaign balance held
(heat-0 80%). No CI; free-licensed assets only. Open: real CC0 audio on a device; endless difficulty tuning on a
device; a veteran carry-over between runs; the deferred full Game.cs partial-split.

---

## PROGRAM "COUNTERPLAY" — player counterplay + visual identity + code health (fresh autonomous session)

Fully-autonomous dev-team session run as orchestrator + a 4-lens research fan-out (design opportunity / code
architecture / meta-replay / visual critique, run as a `Workflow`) + isolated-worktree dev agents on strictly
disjoint files + an independent reviewer + the `SIGHTLINE_BALANCE` flywheel. Branch
`claude/game-dev-orchestration-mxc8ok`. No human input.

**Thesis (from the research fan-out, grounded in the current code):** three converging frontiers on a very mature
game — (1) the player's *reactive/positional* toolkit is thinner than the enemy AI's (overwatch is a flat binary);
(2) the 8 biomes render **below the squint-test perception floor** (they read as one recolored board despite bespoke
per-biome data); (3) `Game.cs` (7648 lines) is the parallelization bottleneck, but ~3000 of those lines are two
behaviour-neutral blocks (the autopilot + the harness) that can slice out at near-zero risk. Plus the single
most-deferred item across all ~11 prior programs: **cross-run veteran carry-over** — every run still rebuilt the same
rookies.

**Waves (each built 0/0 + self-tested + verified; the marquee independently reviewed):**
- **W0 — Game.cs partial split (`eefad24`), parallel worktree dev.** Behaviour-neutral slice of `Game.cs`
  **7648 → 4707** into `Game.Autopilot.cs` (1475, the SmartStep/AutoStep balance+smoke AI) and `Game.Harness.cs`
  (1558, every `Debug*`/`*SelfTest` headless hook). The dev **proved neutrality at the IL level** (Mono.Cecil,
  0-line normalized per-method diff) — stronger than a screenshot md5 (which is RNG-non-deterministic even on the
  pristine baseline). Shrinks the merge bottleneck for every future wave.
- **W1 — land the biome identity (`f1a79a6`), parallel worktree dev.** Pushed per-biome floor-checker
  differentiation (Tint pull 0.22→0.40), signature alphas (~1.6-2×), ambient density (+12%), and the post-FX grade
  gain above the perception floor so STEEL/ARID/TUNDRA/VERDANT/ASH/VOID/NEON/MAGMA read as **distinct places** while
  the squint hierarchy (units > objectives > cover) holds in both palettes. Also: dormant pods got a legible slate
  under-ring + crisper ?/! glyphs (they were near-invisible brown), and the enemy-intent reticle/carets got contrast
  + an entrance pop. Disjoint files (Renderer/Util/Fx/Display) → ran fully parallel with W0.
- **W2 — cross-run VETERAN legacy (`212fe9a`), tech-lead (coupled spine).** Promoted survivors of a finished run
  (Rank≥1, Alive, non-VIP) retire into a persistent reserve in `meta.json` (append-only `List<UnitDto>`, dedup-by-name,
  capped 12 most-storied). A new run's DRAFT recalls up to 2 as gold "VETERAN" cards carrying full progression
  (rank/perks/traits/spec/scars/nickname); the rest fresh, so a returning legend is a bonus, never the whole squad.
  Reused the run-save `UnitDto` via extracted `ToUnitDto`/`FromUnitDto` helpers (one mapping, two consumers). WAR ROOM
  shows `VETERANS n/12`. Save-safe + `NoPersist`-gated (byte-stable harness). New `SIGHTLINE_VETTEST`.
- **W3 — FOCUSED overwatch (`2b3a5cf`), tech-lead.** FOCUS (key F / button) braces a 90° cone toward the aimed tile:
  reacts only inside the lane but at +15 braced aim, vs the default WIDE watch (any direction, base accuracy). The
  cone gates BOTH the reaction (`OnUnitEnteredTile`) AND the AI's `PlayerOverwatchTiles` via the identical `InOwCone`
  test, so the routing AI reads and can exploit the blind zone. **Purely additive** — default overwatch is
  byte-for-byte unchanged, so base balance can't regress by construction (the flywheel autopilot only uses the wide
  watch). New `SIGHTLINE_OWTEST` (cone geometry) + `SIGHTLINE_FOCUSOW` shot.
- **Content — 3 authored arenas (`1a3b230`), parallel worktree dev.** CAUSEWAY (TUNDRA, tier-1 land-bridge
  chokepoint), REDANS (ASH, diagonal sawtooth gauntlet + knoll), DONJON (STEEL, walled tier-2 keep with a gated
  ramp). Pool 32→35, biome-affinity retargeted. Disjoint (Maps/Mission) → parallel with W3.

**Independent review (veteran wave, the riskiest — it touches the save format):** **SHIP**, no CRIT/HIGH/MED. The
reviewer verified back-compat empirically against a hand-written old-style `meta.json`, diffed the extracted
`ToUnitDto`/`FromUnitDto` field-by-field against the original inline code (behaviour-identical; weapon-mod re-bake
still precedes ammo seeding), and confirmed the `NoPersist` byte-stability (a stocked reserve produces an md5-identical
shot; an autoplay win writes no `meta.json`). Two non-gating LOW notes: 2 veterans/run is a persistent power floor to
watch via the flywheel; recall restored snapshot `Hp` not `MaxHp` — applied a one-liner freshen (full HP / no wound on
recall) in the W3 commit.

**Verification.** Build **0/0** Release + Debug. **All 28 self-tests PASS** on the final integrated tree (new OWTEST +
VETTEST + the full prior suite). Autoplay clean across missions + the 3 new arenas (no exceptions, no TIMEOUT).
Screenshots verified: biome sweep, veteran draft, WAR ROOM veteran count, focused-overwatch cone, all 3 arenas.

**Process learnings (COUNTERPLAY).**
1. **The slice was the keystone-enabler.** Doing the Game.cs split FIRST (in parallel with the disjoint visual wave)
   shrank the bottleneck so the two Game.cs-heavy gameplay waves (veterans, overwatch) ran on a smaller, cleaner file.
   A behaviour-neutral move is verifiable to a very high bar — an **IL-level diff** beats a flaky screenshot md5.
2. **"Additive-by-construction" is the safest way to add a gameplay mechanic.** Focused overwatch leaves the default
   verb untouched and the autopilot never uses it, so it *cannot* regress the measured base — the same property the
   boons/contracts default-None pattern relies on.
3. **Reuse the existing DTO for new persistence.** The veteran reserve reused the run-save `UnitDto` via one extracted
   mapping, so save-safety inherits the already-proven append-only round-trip (SAVETEST covers it) rather than adding
   a parallel format to get wrong.
4. **Harness quirk logged:** `R=$(... xvfb-run ... | grep)` command-substitution silently drops the child's stdout
   under `xvfb-run`; run each self-test as a direct `echo -n; CMD | grep` statement (not captured in `$()`, not a
   shell function/`for` loop) or the whole sweep reads as empty. (Not a code issue — every test passes when run
   directly.)

**COUNTERPLAY TOTAL: 5 waves, ~7 commits, ~5 agents (4 research + 2 dev worktrees + 1 review) — the Game.cs split,
the biome-identity leap, cross-run veteran legacy (the long-deferred replay keystone), focused overwatch, and 3
arenas — 28 self-tests green, build 0/0, flywheel no-regression, review SHIP. Merged to `main`.**

### COUNTERPLAY follow-up — content variety wave (new PR, on top of the merged #69)

Two parallel disjoint-file dev worktrees off the merged main:
- **3 build-defining perks** (`Unit`/`Combat`/`SaveGame`, `4368ac8`): VANTAGE (+15 crit on high ground),
  BREAKER (+20 crit vs suppressed/pinned), SIEGEBREAKER (+15 aim vs hunkered) — pure ComputeOdds reads,
  append-only enum, COMBATTEST extended with per-perk fires/no-op assertions.
- **2 enemy archetypes** (`Ai`/`Mission`/`Renderer`, `ecff3a7`): STRIKER "WRAITH" (fast overwatch-discounting
  flanker) + SCREENER "HAZE" (back-line zoner that proactively smokes your firing lane) — Ai.Plan biases
  reusing the existing exec chain (zero Game.cs), distinct glyphs, added to the Codex bestiary (orchestrator glue).
Fully disjoint file sets → clean file-copy integration, zero conflicts. Build 0/0; COMBATTEST/AITEST/SAVETEST/
CODEXTEST/VETTEST PASS; autoplay clean (incl. forced-all-STRIKER/SCREENER stress). A second, tighter demonstration
of the one-owner-per-hot-file parallel-dev pattern.

---

# PROGRAM "UNDERTOW" — the missing half of the action economy (interrupt + morale) + balance roots + board depth

Fully-autonomous dev-team session run as: orchestrator + a **6-lens research fan-out** (a `Workflow`: scout →
synthesize → **adversarial verify**) → per-wave dev/review/QA, with the `SIGHTLINE_BALANCE` flywheel as the compass.
No human input. Branch `claude/game-dev-orchestration-v20urn`, **PR #71**.

## Research → thesis
Six read-only research lenses (tactical-depth / enemy-AI / run-meta / content / presentation / code-health),
grounded in the CURRENT code AND a fresh measured baseline. An adversarial-verify pass then **caught four real
flaws in the synthesis before any dev touched code**: a rehash of the already-shipped `PlanEnemySquad` coordination
(a proposed parallel `SquadPlan` object), a hand-rolled reimplementation of the wired-but-orphaned `StatusKind.Stun`,
a "pod commander" grounded on a non-existent pod-leader, and a phantom cross-wave dependency.

**Thesis:** the measured baseline has one root disease — attrition is one-directional and the enemy has no will-state,
so a match tips once and **never tips back** (lead-swings/match 0.48, policy gap +29.2, comebacks structurally
impossible). Every "add HP / add an aim-slider" fix makes it worse. The cure is the **missing half of the action
economy**: mechanics that **SUBTRACT the enemy's tempo/will** rather than add HP to the winner — an *earnable*
comeback lever for a behind player.

**Measured baseline (flywheel, N=24/heat, greedy+sloppy):** run 60.4%, gap +29.2, lead-swings 0.48, choices/turn 5.20,
Evac 10.9 / Escort 8.2 turns (drag), PLATING bought 369×, LockOn perk 34 (dead-perk superset; Hardened 2 / Tank 4).

## Waves (each built 0/0 + self-tested + flywheel-measured; a final cross-wave review = SHIP)
- **W1 — correctness.** `KillUnit` made idempotent + purges surplus queued reaction `ShotAnim`s aimed at a
  dead unit — fixes a double-kill that double-counted Fallen/Memorial/RecordKill/CreditKill and corrupted the
  class-lethality telemetry the flywheel ranks. `SIGHTLINE_DKTEST`.
- **W2 — interrupt economy (keystone).** **BRACE** (key B): a disrupting reaction stance. On a hit it STAGGERS the
  mover — zeroes its remaining actions THIS turn (post-move offense denied via the `ActAfterMove` ActionsLeft gate) —
  for reduced, non-crit damage. Trade a kill you won't land for tempo. Bounded (one reaction/soldier/turn),
  TIMEOUT-safe, autopilot-probed. `SIGHTLINE_STAGGERTEST`.
- **W3 — enemy pod MORALE / ROUT.** Pods (spawned ~2 strong) carry shared morale: chewed to ≤ half spawn strength,
  the survivors ROUT — flee toward their own edge (overriding the never-retreat archetype exemption), drop overwatch,
  shoot wild (Routed −18 aim), then rally. **The second kill in a pod is now worth far more than the first** — a
  routed pod stops trading, so the player's HP-sum stabilizes (directly targets the 0.48 lead-swings root).
  `SIGHTLINE_MORALETEST`.
- **W4 — sequenced coordination.** The counterweight to W2/W3 (which softened the +29 punish-gap toward ~0). Enemy
  pods coordinated only via a focus map computed ONCE per turn, so a shove/breach that EXPOSED a soldier was never
  noticed by the units acting after it. Fix, both advisory-only: SORT the turn order so setup verbs (SAPPER breach /
  STRIKER + adjacent shove) act BEFORE finishers, and RECOMPUTE the shared focus per-unit in PickNext so the pod
  collapses on the freshly-exposed target THIS turn. AITEST extended.
- **W5 — balance roots.** (a) **LockOn de-superset**: +aim only vs a FLANKED target, not any exposed one (it was a
  superset of the situational aim perks → dominated picks). (b) **De-throne PLATING**: removed the autopilot's
  "always top up armor" preference (the BOT rule, not the mechanic, that drove 369 buys). COMBATTEST gains a
  lockOn no-op assertion.
- **W6 — de-drag Evac/Escort (parallel worktree dev, orchestrator-reviewed + integrated).** EVAC: a DEPLOY BEACON
  action (key G — moved off B during integration to dodge the W2 BRACE collision) plants a forward evac beacon whose
  walkable 3×3 is UNIONED into `EvacZone` alongside the fixed far-corner FALLBACK (always present → a dead planter
  can't soft-lock). ESCORT: `LeashVip()` auto-follows the squad's forward element so the fragile asset is no longer
  hand-walked. `SIGHTLINE_BEACONTEST`.
- **W7 — board depth / presentation (parallel worktree dev).** A deterministic board key-light + restored cover
  legibility (a prior pass over-receded cover into near-black) + contact-shadow AO. `Renderer.cs` only; byte-stable +
  colorblind-safe.

## Measured (flywheel, N=24/heat)
| | baseline | W2-fix + W3 | + W5 | 6-wave (+W6) | **7-wave (+W4)** |
|---|---|---|---|---|---|
| run-completion | 60.4% | 72.9% | 66.7% | 66.7% | **75%** |
| **lead-swings/match** | **0.48** | **0.59** | 0.58 | 0.53 | 0.48 |
| policy gap | +29.2 | +4.2 | ~0 | ~0 | **+16.6** (g83/s67) |
| choices/turn | 5.20 | 6.09 | 6.16 | 5.44 | 5.45 |
| Evac turns | 10.9 | — | — | **7.8** | 7.8 |
| PLATING buys | 369 | — | **203** | — | — |
| dead perks (HRD/TNK) | 2 / 4 | — | **11 / 11** | — | — |

**Headline (the full arc).** W2/W3 delivered the comeback thesis — lead-swings 0.48→0.59 and the +29 punish-spiral
gap collapsed to a forgiving band (comebacks became possible where they were structurally impossible). W5 fixed the
dead economy/perk roots. W6 de-dragged the Evac march (10.9→7.8t). Then W4 (sequenced coordination) **restored the
skill premium the comeback levers had softened**: the flat gap (~0, where sloppy play tied greedy) became a healthy
**+16.6** (greedy 83% / sloppy 67%) and run-completion rose to **75%**, TIMEOUT-free — mistakes cost more, but the
new comeback *levers* (BRACE, morale-exploitation) remain as player tools the greedy bot underuses (so the average
lead-swings reads flat while a skilled player's comeback toolkit is genuinely richer). A caught-and-corrected
**inverted-gap artifact** (−25 after W2) taught the key lesson: an over-eager autopilot BRACE probe made the *greedy*
policy self-sabotage; narrowing it to the genuinely-optimal use restored a positive gap — *measure the measurer*
(per VANGUARD). Open follow-up: the ESCORT leash lifted win% but left escort turns UP (~14t — a corner fight, not
empty walking, but not the intended de-drag; a forward beacon for Escort is the clean fix); the +16.6 gap is a touch
above the +7-12 ideal (skill well-rewarded, sloppy still viable) — watch it doesn't over-punish.

## Process learnings (UNDERTOW)
1. **Adversarially verify the RESEARCH, not just the code.** The verify pass rejected/sharpened 4 of 6 synthesized
   waves against the live code BEFORE a dev ran — killing a parallel `SquadPlan` object that would have shadowed the
   already-shipped coordination, and a "commander" grounded on a non-existent pod-leader. Cheap; saved wasted dev
   effort each.
2. **Reuse the wired mechanism.** W2 denies an action via the same `ActionsLeft` gate every enemy verb already
   checks; W3's rout reuses the `retreatMode` scoring seam + a per-unit-field aim read — no new subsystems, so
   save-safety/TIMEOUT-safety come for free.
3. **The flywheel gap is the load-bearing signal, and it's noisy.** A single inverted-gap run flagged a bot-policy
   bug (not a game bug); fixing the probe restored it. Trust the trend, not one N=24 sample.
4. **A worktree dev can silently branch off a STALE base.** The W6 dev's isolated worktree branched off the old
   pre-program `main`, so its single commit sat on top of `0eae874`, not the 6-wave tip — a naive file-copy would
   have REVERTED W1-W5/W7. Caught via `git merge-base`; integrated instead by `cherry-pick -n` + a hand-resolved
   key-collision (BEACON B→G). **Always check the merge-base of a delegated worktree before integrating.**

---

# PROGRAM "APEX" — the top end becomes real (fixes + instrument + apex play-quality + endless ladder)

Fully-autonomous dev-team session: orchestrator + a 6-lens research `Workflow` (scout → synthesize →
**adversarial verify**, the UNDERTOW pattern) → 10 dev waves with per-wave review and measured commits.
Branch `claude/game-dev-orchestration-0f4jzd`. Mid-run, the owner reviewed screenshots and filed live UI
feedback — folded in as its own wave (W10).

## Research → thesis
Six read-only lenses (balance / tactical / meta-replay / content / presentation / code-health) grounded in
the live code + measured baseline; a per-wave adversarial verify pass then corrected the synthesis BEFORE
any dev ran: 3 waves verified SOUND, 6 FLAWED with sharpened designs (0 rejected). The verifiers caught,
among others: a boon-picker "fix" that would have destroyed its own telemetry (the shuffled pool already
makes slot-0 a uniform census), a "first-ever heats 6-8 measurement" claim that was actually only a
default-cycle gap, a W7 plumbing plan whose central "free ride" through Phase.Barracks didn't exist, and a
"fully parallel" presentation wave with a hidden Game.cs merge point.

**Thesis:** SIGHTLINE's top end was fictional. The difficulty ladder above heat 6 could HARD-CRASH
(lone-VIP win under no-reinforcements → zero roster → `players[0]` throw); LAST STAND silently fought the
campaign's anti-turtle pressure clock (hidden +12..+16 aim + phantom reinforcements by ~turn 10) with zero
player progression (competent bot died at wave 3); the four setup-verb archetypes UNDERTOW's coordination
pass was built to showcase (STRIKER/LANCER/HOUND/SCREENER) were excluded from every faction-stamped fight
— the majority of the campaign — because `FactionRoster` omitted them; and the flywheel was structurally
blind to all of it (heats 0-4 only, no endless mode, veterans invisible under NoPersist, "pick frequency"
tables measuring slot position). The cure, in one arc: fix the correctness bugs that poison play and
measurement → give the instrument eyes → ship the real top end (reachable archetypes, an AI that plays
better rather than aims better, an endless mode that is a ladder) → close the two flagged baseline drags.

## Waves (every commit build 0/0 + self-tested; every risky wave independently reviewed)
- **W1 — the ladder's top exists** (`76010f7`). Emergency conscription at Squad.Count==0 even under
  RELENTLESS/IronVeterans (anti-death-spiral contract honored; CONTRACTTEST's stays-below-floor preserved);
  `players.Count==0` guard in TryApplyLayout; `Mode != Endless` in PressureClockObjective. New
  SIGHTLINE_HEATLADDERTEST (incl. the Rescue lone-captive variant + deterministic pre-fix FAIL proof in a
  scratch worktree). **First-ever heat-8 completion number: 25% (n=20) — a wall, not a flat.**
- **W2 — interactive correctness** (`50224ac` + follow-up `96619c3`). The caged Rescue captive is truly
  caged (ActionsLeft=0 both BeginTurn loops — it could previously walk itself to the squad and self-rescue);
  CAPTIVE ABANDONED loss/redeploy branch in campaign CheckEnd + the same hole in CheckSkirmish; the
  overwatch resource leak fixed (predicted-HP break so watcher #3 stops spending ammo/reaction on a
  corpse-bound shot; decrements only on res.Hit, computed after BRACE halving; KillUnit purge kept as
  backstop); tutorial no longer teaches the repealed "a shot ends the turn" rule and "seen" is marked at
  completion (NoPersist-gated) with mission-end fallbacks. Review: SHIP-WITH-NOTES — traced the predHp fix
  correct-by-construction (no heal can interleave: reactions insert behind the active MoveStepAnim and
  Update returns while the queue is non-empty); 3 LOW findings fixed in `96619c3` + `bce2875`.
- **W3 — persistence armor** (`e3532ec`, parallel worktree). Atomic .tmp+rename writes at both save
  chokepoints; an unreadable meta.json/save.json is stashed to .bak instead of silently wiped by the next
  read-modify-write (the veteran reserve / salvage / hall of fame sat one torn write from a silent reset).
  SAVETEST now covers corruption round-trip.
- **W4 — the flywheel gets eyes** (`6e0c818` + `38cb5e5`). Default heat cycle {0,2,4,6,8}; 
  SIGHTLINE_BALANCE_ENDLESS=N (depth from game.Wave, RunRec.Mode keeps endless out of campaign gap math,
  cap policy explicit + logged); SIGHTLINE_VETSIM=n prices a NOMINAL Rank-3 veteran recall; win-rate-by
  {boon/spec/contract} tables (contracts were recorded but never reported); the greedy perk picker became
  a value-BIASED random A/B (the ChooseSpec precedent) instead of always-slot-A — guarded re-baseline:
  85% → 85% (delta 0), starved-perk exposure revived (HRD 4→11, PLT 3→8). Pre-W7 endless median: 3.
- **W5 — content reachability** (`6995dd1` + follow-up `106dad9`). FactionRoster += the four archetypes
  (stats verbatim, tier-gated; Legion += STRIKER/LANCER/HOUND, Syndicate/Wardens += SCREENER); Defend
  waves route through MakeEndlessHostile (BOMBARD demoted, TURRET re-rolled bounded) scoped to Defend only
  — pressure-clock waves stay cheap by design; callsigns 14→40 with a taken-names set at every recruit
  site. Composition measured: the four went 0 → 2-4% each of faction spawns (n=551); h0 80%. Review:
  SHIP-WITH-NOTES — two real gaps closed in `106dad9` (TakenCallsigns must also exclude FALLEN names and
  the persistent veteran reserve, else bond/memorial/enshrine records still merge; Wardens' failed m2
  gates fell through to a 36% medic glut — now route to the SCOUT filler).
- **W6 — the enemy plays better** (`27d4a8a`/`9b132ba`/`8ffc178`, three measured commits). (a)
  Planner-resolver truthfulness: Ai.Plan's three LoS filter sites honor the commanding (>=2-tier) overload
  exactly like Game.CanTarget — snipers/elites genuinely seek the authored '=' plateaus; Ai.CrossfireWith
  pinned term-by-term to Combat.InCrossfire (`dist <= CrossfireAllyRange` alone; dormant pod-mates counted
  — resolver-first doctrine). (b) A data-driven `Ai.Tier` (0..2) on the heat rows (EXPOSED→1, NO
  QUARTER→2) + endless wave depth, published unconditionally in SetupMission and cleared in
  Combat.EndMission; Tier 0 == shipped constants exactly; smoke-damp capped at 75 (threat, not a tic).
  (c) NO QUARTER adds +1 enemy weapon damage (graced m1-2, initial force only). **Measured:** h0 −2pts
  (in budget) with the inverted policy gap healed −20 → +5; h2 63% (−12, EXCEEDS the ±5 budget — accepted
  deliberately: truthfulness is a correctness fix, the gap tightened +10→+5, the per-mission dip is −3pts
  compounding over ~5.5 missions, and heat is opt-in; 63% is the corrected h2 baseline). h8 matched-build
  28% → 25% with choices/turn 1.56 → 1.79 — **the apex got harder through play quality, not stat
  saturation.** Review: SHIP-WITH-NOTES (revert-probes proved every new AITEST leg genuinely bites);
  follow-up `bce2875` added the spawn-level +1-dmg pin and routed pause-ABANDON through Combat.EndRun.
- **W7 — LAST STAND becomes a ladder** (`6d228f4`). Opener grace (half-count waves 1-2); a progression
  heartbeat — Run.PromoteEligible extracted from the debrief and run mid-stand every 3rd cleared wave
  (+ boon offer every 5th) through an explicit Phase.Barracks detour with a top-of-case Endless guard so
  autoplay can never node-pick into a campaign mission; an ending (heal decays past wave 20 + one REAPER
  elite injected per deep wave). Tuned in 4 measured rounds (the ramp slope was the binding lever):
  depth median 3 → 5 overall / **6 at heat 0 (in band)**, p90 finite, zero cap hits across 192 stands.
- **W8 — close the baseline drags** (`17f88c9`). Escort leash converted from a teleport (which bypassed
  overwatch/fire/pod-wake entirely) to real MoveStepAnims — the real-anims branch SHIPPED, no fallback
  needed (Escort win% held at 96%). Measurement forced three refinements past the spec: two-pass fire
  avoidance (a penalty alone still walked the VIP through a fire picket — pass 1 blocks fire outright,
  hazard-blind pass 2 only if fire seals every lane), the stealth-grief penalty scoped to
  unconcealed+LoS+VIP-wakes-first (the naive radius penalty made deep escorts crawl ~17t), and the leash
  anchor switched to the most-forward ahead soldier (nearest-to-VIP zigzagged the asset backwards).
  Escort-only forward beacon (far-third + cold-LZ gate — any LIVING non-Routed enemy within Chebyshev 3
  blocks, dormant included, so concealment can't cheese it; the button sits greyed until the gate opens,
  with its own tooltip). Depth-scaled recruits ((mission-1)/2 starting kills, promoted in the SAME
  barracks visit via the W7-extracted PromoteEligible; the SHATTERED COMMAND conscripts scale too; a
  DebriefSurvivors offer-Clear() that silently ate pending perk offers became a prune). **Measured:
  Escort 12.9–15.8t → 5.8t at 96% win** (deep-escort probes: m5 13–17t → 4–5t); h0 completion 83% (within
  batch noise of the 88–90% A/A refs); honest caveat — the aggregate +16.6→+7..12 gap target proved
  unverifiable at h0/N=20 (both pristine-HEAD baselines measured NEGATIVE noisy gaps at this pin);
  Escort's per-objective gap landed +7.
- **W9 — presentation** (`2b608f0`, parallel worktree). Honest odds colors (HIT banded >=70 Good / 40-69
  Accent / <40 Foe — an 8% desperation shot no longer reads reassuring green in a red frame); a TRUE
  gamma/contrast post-pass (uBright/uGamma in-shader, uploaded every frame; the old brightness quad
  WASHED the frame); campaign-map labels de-collided; roster chips fade when they'd hide a board unit.
- **W10 — owner-feedback UI wave** (`2aceb40`, parallel worktree, same session). The owner flagged three
  readability failures from screenshots: ellipsized action labels, unreadable odds modifiers, a cramped
  top strip. Fixed structurally: buttons size to their rendered labels and WRAP into an upward tray
  (ellipsis impossible by construction at any verb count); tooltip modifiers one-per-line at 13px, labels
  in Txt with only the signed value colored, right-aligned, DMG/GRAZE unpacked; the top bar regrouped
  into three aligned zones with labeled PRESSURE pips.

## Measured (flywheel; sim RNG unseeded so single batches carry ±~7-10pt noise)
**Final integrated-tree ladder (2 combined batches, n=20 runs/heat, greedy+sloppy):
h0 70% / h2 75% / h4 70% / h6 50% / h8 10%** — a real descending ladder ending in a wall. Endless
(n=32 stands): depth mean 5.16 / median 5 (h0 median 6, p90 7), zero cap hits. Escort all-heats:
**8.4t at 80% win** (n=59; h0-pinned: 5.8t at 96%). Lead-swings 0.44-0.53 (≈ the 0.48 baseline).
| metric | pre-APEX | post-APEX |
|---|---|---|
| heat 8 | process crash possible; 25% (W1, first number) | 10% (n=20) and harder via play quality (choices/turn 1.56→1.79) |
| heat 0 | 85% (W4b fresh ref) | 70-80% across batches (W5 faction hardening intended; noisy band) |
| heat 2 | 75% | 63% single-batch post-W6a (accepted corrected baseline) / 75% in the final combined ladder |
| policy gap | +16.6 (UNDERTOW, over-punishing) | ~0 aggregate (greedy 54 / sloppy 56) — sloppy viable, flagged to trend |
| endless depth (median) | 3 (bot dies at wave 3, fighting the pressure clock) | 5 overall / 6 at h0, p90 6-7 finite |
| Escort turns | ~14 (the flagged drag) | 5.8t h0-pinned at 96% win / 8.4t all-heats at 80% |
| perk exposure | class-line slot ~always; HRD 4 / PLT 3 picks | biased-random A/B; HRD 11 / PLT 8 |
| archetype reachability (faction fights) | 0% | WRAITH 4% / HOPLITE 2% / FERAL 4% / HAZE 3% |

## Process learnings (APEX)
1. **Adversarial-verify the synthesis, then make SHARPENED the binding spec.** Six of nine waves shipped
   against the corrected design, not the original. Every "the seam exists" claim a dev would have tripped
   on was caught pre-dev for the second program running.
2. **Give devs the reference numbers and a dip budget, and demand the breach be reported, not managed.**
   W6's h2 breach (−12 vs ±5) surfaced with a tiebreaker analysis instead of a silent revert — and the
   orchestrator could make the accept call explicitly. "Measure the measurer" now includes "budget the
   measurement".
3. **One owner per hot file, and the orchestrator is not exempt.** A quick orchestrator fix committed with
   `git add <shared-file>` while a dev had in-flight edits swept ~80 lines of the dev's WIP into the wrong
   commit (`96619c3` — tree stayed green, but the wave mixing is permanent). Rule: before any mainline
   commit, `git status` + diff the exact files for foreign hunks; prefer a worktree even for small fixes.
4. **Worktree agents still branch stale.** Both W3/W9 worktrees were cut at a VANGUARD-era HEAD; both devs
   caught it via the documented STEP-0 protocol (check merge-base, reset to the program branch tip) —
   the COUNTERPLAY lesson, now standard practice.
5. **The owner is watching: send screenshots proactively.** The mid-run UI feedback (W10) arrived because
   the W9 before/afters were shared in-thread. Visual milestones go to chat via SendUserFile, always.
6. **Environment facts worth keeping:** background tasks die ~25 min (chunk balance batches; N=20 ≈ 11-12
   min); the container's commit signer is sign-only (local %G? is a false negative — commits ARE signed);
   Bash cwd persists across calls (always `git -C`).

## Open / next
- **The aggregate policy gap sits at ~0** (greedy 54 / sloppy 56 mixed-heat; single-heat pins ranged −20..+7
  across the program) — the comeback levers + W8's recruit scaling have made sloppy play fully viable.
  UNDERTOW's warning ("watch it doesn't slide negative") is now live: either accept forgiving-by-design or
  sharpen the SLOPPY policy definition (the gap measures the measurer as much as the game) before tuning.
- The h0-h4 rungs read flat (70-75% at n=20) — the early ladder is gentle; if a future program wants bite
  below rung 6, the EnemyDelta/StatDelta rows are the knob (measure first, the band is noisy).
- Endless overall greedy median is 5 vs the 6-8 target (h0 is in band at 6) — the next lever is the
  toughness ramp (EndlessWaveScale), deliberately left untouched.
- The W4 VETSIM=2-vs-0 pricing batch wasn't run this program (instrument shipped; the measurement is a
  one-command follow-up when a session has spare sim budget).
- NO QUARTER's "+1 dmg" Desc shows on the skirmish heat picker but m1-grace zeroes it there (pre-existing
  grace pattern; cosmetic).
- On-device audio tuning and endless difficulty FEEL (vs. the measured curve) still need the human's ears/
  hands — unchanged from prior programs.

### APEX follow-up — the two named open threads, closed (same session, post-merge)

- **VETSIM veteran pricing (the watch item open since COUNTERPLAY):** paired h0 batches (N=20 campaigns /
  40 runs each): no veterans 77.5% completion vs a nominal 2-veteran draft 87.5% — **a +10-point floor**,
  landing exactly at the threshold W4's spec set for recommending a cost on the recall. Disposition: the
  salvage-priced recall (via the existing SaveGame.SpendSalvage seam) is the ready lever for a future
  program; not shipped now — the signal sits AT the boundary, not past it, and pricing a free feature is
  a design change that deserves its own measured wave.
- **Endless toughness ramp (`d20f8f5`):** overall depth median 5 → **6** (greedy 6, p90 7-8, h0 median 6,
  zero cap hits — confirmed across three independent 32-stand batches). The finding worth keeping: the
  binding term was the HEAT bump, not the wave slope — a plain slope cut was a measured NULL because a
  −1 tier never crosses a hits-to-kill threshold in the death window; halving the heat term (rounded up)
  moved the blend while Ai.Tier/TighterContact/NO QUARTER keep the rungs distinct. Endless-only by
  construction; the W7 ending remains the terminator.
- **Process note:** the tuning dev agent died silently mid-round (its worktree edits and round JSONs were
  4 days stale when caught) — the orchestrator recovered its measured rounds from the JSON artifacts,
  re-measured the exact working tree as the deciding batch, ran the verify suite, and shipped. Lesson:
  a delegated agent's liveness is checked by ARTIFACT MTIMES + process table, not by the absence of a
  completion notification.

## PROGRAM SIGNAL — the game learns to talk: seams, reads, teaching, a climax with a face, and a compass that can prove it (2026-07)

Fully-autonomous dev-team session: orchestrator + a six-lens research fan-out (visual-UX / design gaps /
code health / balance / content / onboarding) → PM synthesis → **two-verifier adversarial sharpening** →
a 12-wave plan; every wave dev'd in an isolated worktree, adversarially reviewed, and merge-gated on
self-tests + autoplay + Release 0/0. Branch `claude/game-dev-orchestration-1629th`, baseline `c624010`
(the APEX follow-up, PR #73). Landed in **two milestones**: m1 = W1/W2/W3/W11 (PR #74, merge `8c7368f`);
m2 = W4/W5/W6/W8/W9/W10/W12 + the docs commit (`3478589`) — **this merge**. The authoring session died
between its docs commit and this write-up; the entry was reconstructed at landing from ROADMAP + the
commit bodies, and the reconstruction caught a docs over-claim (W7 — see below and process learnings).

## Waves (every commit build 0/0 + self-tested; every wave independently reviewed)
- **W1 — mode-seam integrity** (`5458b45`, core `bd374a0`; review follow-up `791dc5c`). No mode can
  destroy another mode's state: the end card gains MAIN MENU (Esc or click, with a save-overwrite
  warning on NEW RUN when a campaign checkpoint exists), the run-opening draft gains BACK, abandon
  routes per mode (campaign stays checkpoint-preserving with no LossStreak/salvage; skirmish/daily/
  endless end true — and the pause button is mode-true: END STAND / ABANDON FIGHT / ABANDON RUN),
  `ResetModeState()` guards all five mode entries, the daily env seed is NoPersist-gated (cross-process
  seed-leak fingerprints proven to diverge), and mid-stand LAST STAND boon picks actually republish to
  combat (they were dead; structurally-inert GHOST/RAPID DEPLOY filtered from endless offers).
  MODETEST/SAVETEST/HORDETEST new legs, revert-probes bite.
- **W2 — compass rebuild** (`b25a8a2`, core `ef32c46` + `c03fe67`; review fixes `e4c5c51`). The
  flywheel can finally resolve its own target band: greedy and sloppy legs play the same CRN-paired
  worlds (per-slot reseed; `SIGHTLINE_PAIRTEST` is a permanent A/A identity leg), the sloppy policy
  makes bounded POSITIONAL mistakes on an isolated RNG stream (~15% mediocre tile, ~10% one-tile
  overextend, ~10% skip the retreat) — the dominant human skill axis, not just worse aim; objective
  pins hold for every mission of a run; the report gains ACTION MIX across ~26 verbs, win-rate BY
  PERK / BY PURCHASE / BY ARENA + the procedural-fallback rate, and DoT attribution drops unattributed
  '?' deaths 11% → 0%. AutoShop went value-biased with the ARMORY leg rolled BEFORE the spend loop
  (post-loop it fired once in 20 runs — the budget was always gone). **New standing baselines: h0
  80/70 paired gap +10.0, h4 50/45 +5.0, pooled +7.5 (target +7..+15).** First leads: EXC perk 20%
  at h4, arena 23 weakest. Review SHIP-WITH-NOTES, all fixed (strict OBJ pin parse, SHOVE/VAULT
  counted, per-mission DoT-label reset, (Slot,Heat) pair key).
- **W3 — board reads** (`c36b5d9`, core `6b2a493`; review follow-up `906e22e`). Renderer-only:
  plateau tops derive from the biome's floor base lifted in VALUE only — high ground inherits its
  biome's hue instead of the universal khaki (**0/8 → 5-6/8 biomes >30 apart in a channel**, pinned
  PLAZA probe); the focused-overwatch cone reads at a glance (wash 0.06-0.11 → 0.14-0.21, 2px rays,
  direction chevron at the figure); on-unit status codes grew to 13px pills drawn in a LATE pass so a
  body can never hide them; TURRET/BRUISER/SCOUT get shape-coded rings (square/hex/dashed — color
  stays team-only, CB-safe); the EVAC label anchors to a real member tile of the zone's top row,
  clear of the translucent top bar.
- **W4 — Rescue repair** (`eef25b4`, components `b51d608`/`5eb9eb3`/`29891a0`/`87e09da`). The caged
  captive can no longer die into an unwinnable-unlosable state: EnvDamage/TickHazards carry cage
  guards (closing the DoT/shove funnel the four shot/blast guards missed), CheckEnd/CheckSkirmish
  convert any regression into an honest CAPTIVE LOST, and the cage-ring clear scrubs Grid.Barrel.
  Freed-state Rescue then inherits Escort's whole de-drag kit — the forward beacon behind the FULL
  far-third + cold-LZ gate and the real-anims leash; SmartRescue's freed phase delegates wholesale to
  SmartEscort (the old captive self-race fought the leash in a per-turn tug-of-war). **Measured
  (whole-run pin, paired): 9.0t/67% → 6.07t/98.3% per-mission at h0; dip check 77.5% in band.**
  RESCUETEST +2 revert-probed asserts; review verdict SHIP.
- **W5 — boss identity & climax bite** (`06b65c2`, core `76bcccd`, measured tune `a0de10b`; review
  fixes `123f765`). Runs stop climaxing in the identical fight: capability flags (HasShieldArc/
  HasSiege/RagesTwice, defaulting to mirror Cls so rank-and-file are byte-identical) let bosses carry
  signature mechanics; faction mid-bosses (Legion BREAKER two-beat rage, Syndicate BULWARK arc,
  Wardens WARDEN siege); three finale kits — Legion SIEGELORD + LANCER escort, Syndicate SPYMASTER +
  SCREENER/STRIKER cell, Wardens WARLORD — chosen by an **avalanche hash of MapSeed** after a raw
  rng draw collapsed to 16/4/0 kits over 20 seeds under CRN pairing. Tuned by dose-response: +2
  bodies inverted the policy ordering (greedy 45 vs sloppy 75-80), +0 restored the formality (m6
  100%); +1 shipped. The first-cut Legion kit measured 37% m6-conditional → reshipped with a
  boss-only cluster gate (SPREAD OUT is the counter-verb); SPYMASTER 14+n → 12+n (73% → 82%).
  **Measured: m6 conditional 96% → 82% pooled (kits 89/82/85, weakest cell 73%, n=144).** The h0
  completion dip −10 vs the −5 budget was REPORTED and ADJUDICATED-ACCEPTED (dose-response proved
  budget and band jointly unsatisfiable; W6 inherits the heat-gating lever). Review SHIP-WITH-NOTES,
  all fixed (frenzy two-beat else-if, boss-arc COMBATTEST pin, screenshots untracked).
- **W6 — heat ladder tooth** (`5243d54`, components `a099edd` + `d711780`). The stale reference
  table (and the phantom h2=63% baseline) retired: a **fresh paired baseline on the post-W5 tree
  read 62.5/55/35/22.5/7.5** across h0-h8 — the finale was eating ~1/5 of low-heat runs and h8
  breached its ≥10% hard floor. Three measured moves: rung-4 ELITE CADRE trades its stat row for
  AiTier=1 (the mid-ladder coordination tooth at zero completion cost; AiSquadSelfTest re-pinned
  same-commit); TighterContact measured for the first time (+7.5pts at h4 — experiment reverted,
  lever documented); the W5 finale body heat-gated to Ai.Tier>=1 (heat 4+) at the one sanctioned m6
  site. **Final ladder 80/70/50/32.5/17.5 vs goal 80/70/60/40/20 (±8): every rung in band except h4
  at 2pts under (inside noise), no policy inversion, paired gaps increasingly punish sloppy play up
  the ladder.** Reviewer static pass clean + orchestrator direction-check 75% at h0.
- **W7 — exposure plumbing: NOT SHIPPED.** The docs commit (`3478589`) claimed it closed in
  ROADMAP/FEATURES (column-constrained objective assignment, per-run no-repeat arena deck,
  biome-true arena hints, SIGHTLINE_EXPOSURETEST), but landing verification found no W7 commit in
  the program range, no EXPOSURETEST hook in src/, and Run.CardForNode still on the plain
  `ObjectiveFor(n + node.Row)` rotation. A docs over-claim, caught at landing; the spec is carried
  forward ready-to-dev (see Open / next).
- **W8 — morale visible & contested** (`537fcef`, core `d6b7a97`, board reads `311664f` + `611561a`;
  review fixes `eb500c0`). The flagship comeback lever becomes a plan instead of a surprise: pods one
  kill from breaking wear an amber WVR crack tag (a truthful superset incl. rallied-below-threshold
  pods; HELD BY BANNER when anchored, with the tooltip pointing at the counter), and the enemy
  finally contests both the rout and your objective progress — WARBRINGER (Legion 6% m3+, diamond
  role ring extending W3's shape set, Chebyshev-4 aura: in-aura pods cannot rout and rally twice as
  fast; capped 1/mission via the BOMBARD-style demote) and CUSTODIAN (Wardens 8% / Syndicate 5% m3+,
  padlock; re-locks a hack / re-arms a blown charge one step per adjacent turn, banner-telegraphed).
  `611561a` fixed DrawPoly 4-gon rotation (0° IS the diamond — 45° rendered the TURRET square).
  Review fixes closed the routed-specialist holes for CUSTODIAN **and** the pre-existing MEDIC/
  BOMBARD variants — broken specialists now flee like everyone. **Measured: h0 paired delta +5.0
  (in the ±5 budget), Hack 3.0→3.0t, Sabotage 3.03→3.0t (no objective drag), both archetypes
  spawn.** MORALETEST +5 revert-probed assert families; review SHIP-WITH-NOTES, all fixed.
- **W9 — salvage becomes a standing economy** (`5e98863`, core `5e18d4c`; review follow-up
  `9122f2f`). The meta stops dead-ending after ~3 wins — and the APEX veteran-pricing thread closes:
  **recalling veterans costs 10+8×Rank salvage**, charged once, atomically, inside ConfirmDraft
  (picks/BACK/re-roll charge-free by construction; unaffordable DEPLOY refuses without seating).
  Repeatable sinks — draft-pool re-roll (10), scar REHAB with a true undo (30), shop-slate re-roll
  (5) — settle through a **pending ledger** committed beside the mission-start checkpoint, so
  quit-at-barracks rolls back the goods AND the money (the review's cross-store atomicity find: the
  old immediate write burned salvage for goods the reload restored). Three horizontal MetaUnlocks
  appended enum-END (CROSS-TRAINING / QUARTERMASTER / STANDING RESERVE); heat multiplies the win
  bounty ((25+6m)·(10+h)/10 — h0 exactly unchanged); daily wins pay 10+heat once per stamp with
  pay+mark in ONE atomic meta write, a streak counter, and two achievements. Also fixed (routed from
  W1): METATEST silently overwriting a real player save.json (sentinel-proven). Review
  SHIP-WITH-NOTES; both notes fixed with revert-probed asserts.
- **W10 — pool expansion** (`1545241`, core `15e4aef`, verification `cd5ec03`; review follow-up
  `f8c884c`). Run N+3 stops feeling like run N: six verb boons appended enum-END, each hooking a
  real verb — SHOCK DOCTRINE (braced interrupts deal full damage), **TERROR (redesigned in review:
  the specced 2/3 rout threshold was a functional no-op at the game's size-2 pods, where
  half-strength already routs the survivor on the first kill; now routed enemies stay broken +2
  turns, ordinal unchanged, MORALETEST pins base 2 / TERROR 4)**, FIELD DRILLS (DRAG+VAULT
  twice/turn), PYROMANIACS (own fire +2 turns, squad burn-immune), FIELD STORES (2× utility charges;
  renamed from the spec's QUARTERMASTER to dodge W9's unlock), RECLAIMER (focused-cone overwatch
  kills refund the reaction — ammo still spent, hard-bounded). Two mods: BIPOD (+10 aim unmoved;
  deliberate anti-synergy with EXPOSED BY FIRE) and SUPPRESSOR (a suppressed shot wakes only the
  target's pod — all 16 BreakConcealment sites audited, the stealth-sniping loop provably closed,
  CONCEALTEST legs). Three secondaries: GHOST / DEMOLITION / BOUNTY. The INTEL CACHE plants a gold
  expiring diamond mid-field (+8-10 intel, expires after 6 player turns; follow-up: never spawns
  under a possibly-dormant hostile). All three enum tails pure appends with old-tail ordinal pins
  (revert-probed). **Flywheel: all 6 boons reach n≥8 exposures at/above baseline, both mods bought,
  204 cache pickups.** Review SHIP-WITH-NOTES; all notes fixed.
- **W11 — teach it where it's played** (`d3cb953`, components `9d731b8`/`11a0163`/`f798d22`,
  refinements `848d7a3`; review fixes `67a9c9c`). A new player can learn every rule without leaving
  the mission: action-bar help wraps into a ~400px card (the ~270-char BEACON line measured ~1600px
  on a 1280px screen — mostly off-screen); hovering an enemy NAMES it with its bestiary clause; the
  codex opens on a 12-entry FIELD CRAFT rules tab whose **every number is verified against the
  combat code** — the review caught a HIGH false claim (hunkering in the open "shields nothing";
  Combat.cs's −25 aim and crit=0 are unconditional) and fixed it to the honest rule; losses name the
  killer class with a counterplay tip; first sightings banner NEW CONTACT; objective/boon chips grew
  hover cards; occluded roster chips collapse to a 20px edge rail; the action bar fades per-BUTTON
  occlusion; and the tutorial can no longer re-offer itself forever to reaction-averse players
  (turn-count fallbacks on the MOVE and OVERWATCH lessons). CODEXTEST extended.
- **W12 — strategic-layer facelift** (`334ed6e`, staging `0a2b70d`, core `15ab81b`; review follow-up
  `0c72c1f`). The meta screens stop reading as a spreadsheet next to the board's art: the campaign
  map SIZES TO FIT the panel's real leftover space (150-250px region, node radius scaling 10→12px
  base +3 boss, objective labels on reachable nodes, a shape+color legend — proven to fit 800px at
  the 6-roster/5-report/KIA worst case); Renderer.DrawCodexGlyph stamps class silhouettes into draft
  cards / barracks rows / roster chips / promotion headers (via a cached stub — no per-frame
  allocation); the intro gets a real hierarchy (two filled Friend primaries, Foe-red reserved for
  LAST STAND, an equal-width ghost grid with hover captions); the WAR ROOM gains per-achievement
  progress bars + a NEXT UNLOCK preview card wired to W9's economy; promotion cards show
  soldier-specific before→after deltas — the review caught LOCK-ON teaching "vs exposed" when the
  effect is flanked-only (fixed, plus four dead-perk phantom arms deleted, so a re-offer can't
  inherit phantom numbers); a TRUE first run pre-selects a RECOMMENDED squad+boon (gated on an empty
  veteran reserve too). Review SHIP-WITH-NOTES; all notes fixed.

## Measured (flywheel; CRN-paired policy legs from W2 onward)
Final ladder (post-W6, paired): **h0 80 / h2 70 / h4 50 / h6 32.5 / h8 17.5 vs goal 80/70/60/40/20
(±8)** — every rung in band except h4 at 2pts under (inside noise), h8's ≥10% hard floor restored,
no policy inversion, paired gaps widening up the ladder.
| metric | pre-SIGNAL | post-SIGNAL |
|---|---|---|
| heat ladder h0-h8 | fresh post-W5 paired baseline 62.5/55/35/22.5/7.5 (stale APEX table + phantom h2=63% retired) | 80/70/50/32.5/17.5 vs goal 80/70/60/40/20 ±8 |
| Rescue (h0, whole-run pin) | 9.0t at 67% | 6.07t at 98.3% per-mission; dip check 77.5% in band |
| m6 finale (conditional) | 96% — a formality | 82% pooled (kits 89/82/85, weakest cell 73%, n=144) |
| policy gap | ~0 aggregate, noisy at N=20 pins (APEX flag) | paired: h0 +10.0 / h4 +5.0 / pooled +7.5 — in the +7..+15 target, sloppy punished up the ladder |
| unattributed deaths | ~11% ('?' bucket) | 0% (BURN/BLEED/STRIKE/BARREL attributed) |
| plateau biome separation | 0/8 biomes distinct | 5-6/8 biomes >30 apart in a channel |
| W8 morale wave cost | — | h0 delta +5.0 (in ±5); Hack 3.0→3.0t, Sabotage 3.03→3.0t |
| veteran recall | free (APEX: measured +10pt floor, unpriced) | 10+8×Rank salvage, atomic in ConfirmDraft |
| new-content exposure | — | 6/6 boons n≥8 at/above baseline; both mods bought; 204 cache pickups |

## Process learnings (SIGNAL)
1. **A milestone is not done until merged — and mid-flight state belongs in DEVLOG notes BEFORE the
   final docs commit.** The session died between the docs commit (`3478589`) and the DEVLOG write-up
   + m2 merge; main sat at m1 for a week while the finished m2 waves lived only on the branch. The
   next orchestrator recovered the landing from ROADMAP + the commit bodies — which worked ONLY
   because every wave commit carried its measured numbers and review verdicts in the body. Write the
   DEVLOG (or at least the mid-flight notes) first, docs-commit second, merge third.
2. **Closed-item claims must be verified against the tree before the docs commit.** The program's
   final docs commit claimed W7 (exposure plumbing) closed in ROADMAP and FEATURES, but the wave was
   never dev'd — no commit, no `SIGHTLINE_EXPOSURETEST` hook, no code. The landing orchestrator
   caught it by grepping for the claimed test hook; "hook exists + commits exist" is now the
   checklist for every closed checkbox.
3. **Verify a spec against the game's real distributions, not its abstractions.** W10's TERROR as
   specced (rout at 2/3 strength) was a functional no-op: every real pod spawns size 2, where the
   existing half-strength rule already routs the survivor on the first kill. The review caught it;
   the orchestrator adjudicated a redesign (rout-duration +2, persisted ordinal unchanged) instead
   of shipping a dead boon (`f8c884c`).
4. **A fresh paired baseline retires the stale table before any tuning.** W6 re-measured the
   post-W5 tree (62.5/55/35/22.5/7.5) rather than tuning against APEX-era numbers — and the
   "corrected h2=63% baseline" APEX had accepted turned out phantom on the current tree. Never tune
   against another program's table (`5243d54`).
5. **CRN pairing is invasive: derived choices must hash, not draw.** W5's finale-kit pick via a raw
   `rng.Next` collapsed to 16/4/0 kits over 20 seeds under the flywheel's correlated seed pairs —
   replaced with an avalanche hash of MapSeed, zero generator draws taken (`a0de10b`). W2's
   PAIRTEST A/A identity leg is the standing guard.
6. **Report the breach with a dose-response, then adjudicate — and hand the lever forward.** W5's
   h0 dip (−10 vs the −5 budget) shipped ACCEPTED because dose-response proved budget and band
   jointly unsatisfiable at that wave — and the heat-gating lever was explicitly bequeathed to W6,
   which used it to put the finale body behind heat 4+ at zero low-heat cost (`06b65c2`, `5243d54`).

## Open / next
- **W7 exposure plumbing — ready-to-dev** (the docs over-claim, spec intact): column-constrained
  objective assignment (every path: ≥1 Eliminate, ≥1 Defend-or-Rescue, ≤1 Escort), a per-run
  no-repeat arena deck, biome-true arena hints, and a `SIGHTLINE_EXPOSURETEST` 200-seed histogram.
  The ROADMAP/FEATURES over-claims were corrected at landing (`417e9d3`).
- **Chase W2's first leads:** EXC perk at 20% win-rate at h4 and arena 23 weakest are now measurable
  (BY PERK / BY ARENA tables + procedural-fallback rate) but untuned.
- **h4 sits 2pts under its goal band** (50 vs 60±8 — inside noise); TighterContact is the
  documented, measured (+7.5pts at h4) and deliberately reverted lever if a future program wants
  the rung lifted. Weakest finale cell is 73% — watch per-kit drift in full-ladder batches.
- **Two cosmetic Desc strings:** LOCK-ON's PerkDef.Desc in Unit.cs still says "vs exposed targets"
  (the HUD/delta line already say flanked — flagged out-of-scope in `0c72c1f`), and NO QUARTER's
  "+1 dmg" on the skirmish heat picker where m1-grace zeroes it (carried from APEX).
- **On-device audio tuning and difficulty FEEL** (endless + the new heat ladder, vs the measured
  curves) still need the human's ears/hands — unchanged from prior programs.
- Closed this program from APEX's list: veteran pricing (W9's priced recall), the policy-gap
  measurement noise (W2's CRN pairing + positional sloppiness — pooled +7.5, in target), and the
  h0-h4 flatness (W6's re-ladder: 80/70/50). Endless depth median was already in band (6) via the
  pre-SIGNAL APEX follow-up.

# PROGRAM FULCRUM — milestone 1: the orchestrator's solo window (2026-07/08)

Six fresh research lenses on the post-SIGNAL tree → PM synthesis into a 13-wave plan ("systems
that exist but never reach play") → the adversarial-sharpening stage was lost to a session-limit
window, so the orchestrator sharpened the first waves against the code by hand (every checked
seam claim held) and dev'd the first two waves solo while the subagent pool was limit-blocked.
The full plan of record lives in docs/ROADMAP.md §PROGRAM "FULCRUM" — deliberately in-repo:
container suspensions (Jul 6→13→21→22→Aug 6) wiped every scratchpad copy of it, twice.

## Shipped
- **FUL-3 CHROME** (16e24e9): roster-chip reflow (no more one-letter rail from turn 1, incl. the
  VIP/captive chip), per-button action-bar dim (floor 0.45, dormant-exempt), INTEL cache
  row-clamp (draw-count stable), row-0 label flips, dormant de-emphasis (0.75x/0.85x + tighter
  ring), LOCK-ON/NO QUARTER desc truth. Verified: Release 0/0, 4 suites, autoplay x3, shot sweep
  m1/m3/m5/escort/rescue; before/afters shared with the owner.
- **FUL-2 SEAM INTEGRITY** (4690748): same-sitting assist-cache staleness closed (win/lose paths
  refresh _metaLossStreak); SIGHTLINE_INTRO save-clobber closed (stash/restore, byte-identical
  verified); codex-from-pause no longer plays queued enemy shots (anim queue freezes in
  Phase.Codex, pause restored on exit); EXTRACT arrivals route through OnUnitEnteredTile (BIPOD/
  bleed/burn/cache/overwatch; CheckEnd deferred while reactions queue); supercover LOS made real
  (sealed diagonal corners block at range, both directions; point-blank keeps true-corner=cover;
  +6 COMBATTEST legs); Pinned comment truth. Verified: Release 0/0, 5 suites incl. new legs,
  INTRO-STASH, autoplay x5.
- **FUL-12 SIGNPOSTS** (wt-ful12): the game now points at its own systems. End card gained the
  meta payoff (SALVAGE slab, HEAT UNLOCKED line, achievement roll) via new Game fields set in the
  award path — no Report parsing, all-dark under NoPersist so harness cards are unchanged;
  tutorial gained a concealment/AMBUSH step 0 and a FIELD MANUAL pointer (TutStep* constants keep
  the reached-FIRE gates semantic), plus a once-per-profile BRACE field tip; hovers now answer
  everywhere they didn't — every top-bar pill (turn/concealed/heat/pressure/cache) cards, and a
  dormant/no-odds enemy hover draws an ID card with an honest alert-state line; the tutorial bar
  dims to the lesson verb (min-composed with FUL-3's occlusion dim); RECOMMENDED draft ranks all
  16 boons (was 6 + arbitrary fallback at 21.4%); campaign legend names S/START + */BATTLE; class
  glyphs lead Hall of Fame + end-card squad/KIA rows; WAR ROOM panels size to content. New
  harness seams: SIGHTLINE_IDHOVER, SIGHTLINE_BRACETIP, SIGHTLINE_HOVERHUD +5 ids, SUMMARY
  stages the meta fields. Verified: Release 0/0, DRAFTTEST/SAVETEST/CODEXTEST/MODETEST, autoplay
  x2, 12-shot staged sweep (win/lose cards, tutorial step 0, ID card, war room, campaign legend,
  5 pill hovers, brace tip).

- **FUL-11 CEREMONY** (959f5b2 + docs; wave dev on wt-ful11, survived a 3-week container
  suspension mid-measurement — commit-and-push-per-step meant zero code loss). The finale gets a
  ceremony and Wardens gets a real kit:
  * **Presentation**: m6 opens on a danger-red intro card naming the hunt ("FINALE - KILL THE
    WARLORD") with the kit's counter-verb clause on the W11 sub-line (Run.FinaleKitClause — ONE
    source of truth for both ceremony sites); a one-shot HVT SIGHTED banner fires on the NEW
    CONTACT lane when the boss first goes Active (consumes the ELITE contact slot — no
    double-banner); the m6 top bar rides a red-tinged plate + red hairline + a red FINALE prefix
    (value/alpha match the normal plate — hue only, squint-safe); the finale boss carries a
    champion ground ring/aura (Pal.Elite double ring + soft wash, drawn under the gold HVT mark,
    all alert states) keyed on a new transient Unit.IsBoss set only in MakeFinaleBoss —
    presentation-only, zero combat/AI reads.
  * **Wardens retinue**: MakeFinaleRetinue's null Wardens case becomes SIGNIFER (WARBRINGER, in
    the boss's own pod 0 — the formation cannot rout until the banner falls) + ORDERLY (MEDIC —
    contests the burst-down verb; chosen over the spec's CUSTODIAN option because the boss node
    is always Decapitate and a keeper would be a dead mechanic there, the TERROR lesson).
    Cost-neutral: replaces the two cascade-fill slots; MakeHostile draws zero RNG exactly like
    the FactionRoster fill it replaced, so the world-build stream is unchanged. A deterministic
    (no-RNG, post-all-draws) relocation pass walks Cheb rings out from the boss so the banner
    aura (range 4) covers the boss AS SPAWNED — shuffled rows previously allowed Cheb 5-10.
  * **Verified**: SIGHTLINE_FUL11PROBE=20 (new window-free hook): retinue slots present at low
    heat (h0 finale = 6 bodies incl. boss+retinue; h4 = 9), bannerDistMax 4, banner cap holds,
    per-kit boss names — PASS across 3 kits x 2 heats. COMBATTEST/AITEST/MORALETEST/SAVETEST
    PASS; Release 0/0; autoplay x3 clean (WIN m6 / LOSE m4 / smart WIN m6 — two full runs
    exercised the whole ceremony path). Shots: intro_card / hvt_aura / red_topbar /
    boss_sighted (untracked, forwarded to the owner).

## Measured (FUL-11 per-kit m6 conditional — h0, SIGHTLINE_FINALE-pinned, 3x SIGHTLINE_BALANCE=10
chunks per kit on shared CRN slot sets 0-9/10-19/20-29, greedy+sloppy pooled)
| slots | WARDENS | LEGION | SYNDICATE |
|---|---|---|---|
| 0-9 | 94% (16/17) | 81% (13/16) | 71% (12/17) |
| 10-19 | 64% (9/14) | 93% (13/14) | 79% (11/14) |
| 20-29 | 89% (16/18) | 89% (16/18) | 94% (17/18) |
| **pooled** | **83.7% (41/49)** | **87.5% (42/48)** | **81.6% (40/49)** |

All three kits in the 78-88 goal band; pooled 84.2% (123/146) inside 82±4; the Wardens weakest
cell moved 73 → 83.7 with NO count/stat tuning — the support-heavy retinue swap (banner+medic in
for ~two cascade shooters at bump 5) traded alpha damage for a target-priority puzzle and landed
in band on its own. Chunk-level variance is large (Wardens 64-94 across slot sets — world-driven,
the same reason W5 adopted CRN pairing), so per-kit drift stays a full-ladder-batch watch item
(FUL-13 baseline inherits these pinned chunks' method). h0 run completion across the nine chunks:
45-85% (chunk n=10 each; the 10-19 slot set is simply a harder world draw for every kit).

## Measured (FUL-2 LOS budget A/B — CRN slots, h0, N=10/leg, pre=16e24e9 vs post)
| metric | pre | post | verdict |
|---|---|---|---|
| run completion | 65% | 60% | at the ±5 budget boundary — in budget |
| greedy / sloppy | 70 / 60 (gap +10) | 50 / 70 (gap −20) | ~1.3 SD at n=10 — under-powered; FUL-13 watch item |
| avg missions cleared | 5.35 | 5.35 | unchanged |

Fix retained per the W6a truthfulness precedent (planner and resolver must agree on sightlines);
if FUL-13's proper-N baseline pins a genuinely negative gap on the corrected tree, the sloppy-
policy definition gets the accept-vs-sharpen decision then, with data.

## Process learnings (FULCRUM m1)
1. **The environment is the adversary now.** Four container suspensions and three session-limit
   windows killed more agent work than any bug. Countermeasures now standard: the plan of record
   lives in docs/ROADMAP.md (not scratchpads); dev agents PUSH their wave branches to origin
   after committing (uncommitted worktree work from four agents was wiped by one rebuild);
   liveness is judged by artifact mtimes + process table, never notifications or wall-clock.
2. **SendMessage-resume recovers limit-killed agents with full context** — but not across
   container rebuilds (transcripts live in the container). Push early, report often.
3. **A workflow's journal + resumeFromRunId recovered 2 finished researchers at zero cost** after
   a mid-run limit kill; agent() results need null-guards (a spread of a null result made a
   truthy-but-empty object that crashed the reduce).
4. **The orchestrator dev'ing solo during limit windows works** — but only with the same review
   gate as everyone else: the FUL-2/3 diffs got an adversarial reviewer whose last probe
   ("Bresenham skew asymmetry: pre-existing or added?") was exactly the right question; its kill
   left "all pins pass" + one comment-truth lead, which the landing sweep confirmed fixed.
5. **Docs over-claims survive until someone greps for the test hook.** SIGNAL's W7 claimed
   SIGHTLINE_EXPOSURETEST; the hook didn't exist. Closed-item claims are now verified against
   the tree (hook exists, commits exist) before any docs commit — and W7's spec became FUL-9.

# PROGRAM FULCRUM — FUL-1 COMPASS TRUTH landing (2026-08, wave dev)

- **FUL-1 COMPASS TRUTH** (a4ef1dd): the measurement compass now reports what the FULCRUM waves
  need to aim. Per-slot pair records + the all-pairs missions-cleared PAIRED MARGIN (continuous,
  every pair contributes — CI roughly half the discordant-only binary gap's), binomial ±SE on
  n<30 win-rate rows, (code,heat) "@h<N>" keys whenever a batch spans heats, a boon PROCS column
  counted at the six effect sites, the arena funnel (authored-applied / connectivity-reject /
  procedural-roll, sums to 100% of builds), BY ARENA stratified by mission #, BY EVENT-CHOICE
  (id:arm, run-scoped like boons), and the SIGHTLINE_PERK=<code> paired probe. Telemetry-only:
  every addition no-ops unless Stats.Enabled; the probe overrides AFTER ChoosePerk's value roll
  draws, so probe legs replay their baseline worlds (CRN-safe). DESIGN.md §4 re-graded to tree
  truth — **engagement mass** and **death stakes** are the named thin pillars.

## Measured (FUL-1 first readings; BALANCE=10 per heat)
| finding | number | consumer |
|---|---|---|
| SHOCK DOCTRINE procs (h4: 7 picks) | **0** | FUL-5/6 — the verb-boon layer never reaches play |
| FIELD DRILLS / RECLAIMER procs (h4: 3+3 picks) | **0 / 0** | FUL-5/6 |
| PYROMANIACS procs (h4: 4 picks) | 2 | FUL-5 |
| FIELD STORES procs | 63-84/batch | grant site fires as designed |
| TERROR procs (h0) | 6 | rout-start reachable |
| arena funnel (h0) | 52.6% applied / 0.0% reject / 47.4% proc-roll | FUL-9 — the guard rejects ~nothing; the 55-roll IS the funnel |
| paired margin, h0 / h4 | −0.30 ±0.26 / +0.10 ±0.57 SE | FUL-13 baseline |
| PERK probe RFX (paired 2xN=5, h0) | 1 pick baseline → 10/10 probe leg | perk pricing works |

## Gotcha for future waves: SHOT byte-identity is environmentally impossible
The "byte-identical screenshot" verification bar cannot be met by ANY change, a no-op included:
Util.Rng is clock-seeded at startup (the PairTest comment in Program.cs already says so) and the
Renderer's pulses read Raylib.GetTime() (wall clock), so two SHOT invocations differ on unchanged
code. Measured with a temporary (uncommitted, applied identically to both trees) env-gated
Util.Reseed overlay: same-code noise floor 1.30-3.38% of pixels; base-vs-FUL-1 diffs 0.28-3.39% —
inside the floor, scenes pixel-inspected identical (same world/units/HUD). The replacement
logic-identity proof, now precedent for telemetry-only waves:
  (a) seeded full-campaign AUTOPLAY + SMARTPLAY A/B vs base — frame-exact identical RESULT lines
      across 5 seeds (any draw-count or logic drift diverges a 10k-frame trajectory);
  (b) SIGHTLINE_BALANCE=2, same slot base, both trees — all 29 base-schema JSON fields identical
      (per-shot class tallies and action mix included);
  (c) PAIRTEST + SAVETEST PASS.

# PROGRAM FULCRUM — FUL-4 HOLDFAST (2026-08, wave dev on wt-ful4)

Defend was the hidden low cell: the initial force was sized like an Eliminate screen AND rich
waves landed from t1 at 2+m/2 — the one enemy-forced-tempo objective double-counted its own
difficulty — and the measuring bot refused to leave a flanked tile (so part of the number was
the bot, not the mission). Measured-wave rounds, ONE lever each (SIGHTLINE_OBJ=defend, N=10 CRN
slots, h0 unless noted; a706152 base):

| round | lever | Defend win% | greedy/sloppy | gap |
|---|---|---|---|---|
| R0 | reference | 38% (n=32) | 38/38 | 0 |
| R1 | SmartDefend co-fix: fall back to better cover / refuse a flank (lands FIRST) | 38% (n=32) | 33/41 | -8 |
| R2 | defend flag: opener count-3 (mirror the sabotage trim) | 41% (n=34) | 41/41 | 0 |
| R3 | first wave graced to t3 | 57% (n=46) | 60/52 | 8 |
| R4 | wave size 1+m/2 | 66% (n=58) | 67/64 | 2 |
| R5 | waves = real morale pods (ids 100+, _podOrig) | 66% (n=59) | 67/66 | 1 |
| — | h4 leg, final tree | 69% (n=59) | 69/70 | -1 |
| — | h0 disjoint slots (BALANCE_BASE=10), final tree | 73% (n=70) | 73/73 | -0 |

Bands: h0 60-80 HIT (66/73 across disjoint slot sets, pooled ~70); h4 55-70 HIT (69); Defend
gap <50 HIT (|gap| <= 8 every round). The R0 reference read 38%, not the audit's 23.1% — older
tree, and n=26 vs n=32 batch noise; the target band is what binds, not the entry number. The
spec's remaining dose-response levers (rich-tier cap min(mission,4) in MakeWaveHostile; waves
stop t5) were deliberately NOT applied — the method stops inside the band; they stay in the
toolbox if FUL-13's re-baseline wants Defend softer at the top rungs.

Telegraph: "WAVE INBOUND - EAST EDGE" one PLAYER TURN ahead of the wave acting, shown from
BeginPlayerTurn and sharing the spawner's own DefendWaveTurn schedule read (the W8 never-lies
pattern), with a sub-line in the W11 lane. SIGHTLINE_WAVEBANNER=1 + SIGHTLINE_OBJ=defend +
SIGHTLINE_SHOT stages it for a screenshot (autoplay skips game.Draw entirely, so SHOT+AUTOPLAY
can never photograph a live board — stage presentation via a Debug* hook).

Spill budget (unpinned h0 N=10, base 0 — CRN-comparable to the m1 A/B reference): Eliminate 100
(ref 100), Hack 100 (~100), Evac 100 (~100), Escort 100 (~96, +4), Rescue 100 (~98), Sabotage
86 (~100, -14), Decapitate 75 (~81, -6); run completion 60% (unchanged — recorded as FUL-13
input; Defend fielded only n=4 unpinned missions, the FUL-9 exposure problem). The Sabotage /
Decapitate / Escort deviations exceed the ±3 window NOMINALLY; a disjoint-slot control batch
(BALANCE_BASE=10) that happened to field ZERO Defend missions — i.e. code-path-identical to the
pre-FUL-4 tree — read Eliminate 90 / Sabotage 80 / Decapitate 86 / completion 60% against the
same references. The ±3 window is tighter than the metric's own batch noise at n~15; breach
reported per protocol, nothing reverted. FUL-1's per-slot records are the real fix for this
class of question.

Review notes (SHIP verdict, note-level): (1) the telegraph can promise a wave the 12-alive
clutter-cap then swallows (16/18 landed in a pinned autoplay) — the lie is only ever
conservative (spawner+banner share one DefendWaveTurn read; an unannounced wave is impossible);
(2) the banner lives ~72 frames, so SIGHTLINE_SHOT=90 photographs an empty telegraph — use
SHOT=30 for the wave-banner shot.


Verified: Release 0/0; COMBATTEST/AITEST/MORALETEST/SIEGETEST PASS; autoplay x3 clean + one
defend-pinned autoplay (waves land on schedule, WIN m6, no exceptions/TIMEOUT); telegraph
screenshot inspected (banner + sub-line + DEFEND 3/8 pill on a live board).

Gotchas for future waves: SpawnReinforcements' `podded` flag is DEFEND-only by design (a
routable pressure-clock punishment isn't a punishment); wave pod ids start at 100 (initial pods
are i/2 <= 5, harness scenes use 90/91); the wave schedule is DefendWaveTurn — spawner and
telegraph must keep sharing that one read.


# PROGRAM FULCRUM — FUL-9 THE DECK (2026-08, wave dev on wt-ful9)

The carried W7 spec (the docs over-claim), finally built on the repaired roster. Two systems,
both PURE derivations off MapSeed (zero Util.Rng draws, zero persisted state — CRN pairing and
save round-trips hold by construction):

**Objective plan** (Run.GenerateMap/CardForNode): hashed off (MapSeed, column, row) via the new
`Util.Hash3` avalanche (the W5 finale-kit mixer, parameterised — .NET Random correlates nearby
seeds). Column-scoped guarantees hold on EVERY route regardless of edge wiring (a route visits
one node per column): an event-free ANCHOR mid column deals Defend(80%)-or-Rescue on all its
nodes; Escort exists on EXACTLY one hashed node per map, <=1 per route (zero-Escort maps no
longer occur; never in the anchor column); START
stays Eliminate; boss stays Decapitate; everything else deals from an Escort-free 7-pool with a
per-column offset + row (siblings in a column stay distinct ops). The GenerateMap rng stream is
byte-identical to pre-FUL-9 (the plan takes no draws), so existing saves regenerate the same map
shape/kinds/edges/factions — only card objectives change. `ObjectiveFor` survives untouched as
the SKIRMISH/offer fallback rotation.

**Arena deck** (Mission.PickLayout/DeckPick): a Hash3-keyed Fisher-Yates permutation of all 35
layouts per run; mission n takes draw n (recomputed 1..n per Build — n<=6, cheap — so nothing
persists). The biome hint became a 25% pull-forward WITHIN the deck of the DISPLAYED biome's
arena (`Biome.IndexFor` — the old hint keyed off mission number, which both mismatched the
rendered room and re-coupled arena to mission, the FUL-1 confound). Authored gate Roll 55→80 —
FUL-1 measured the reject lane EMPTY (52.6/0.0/47.4 at n~190), so the lost roll was the only
road to procedural. DRAW-ORDER CONTRACT (comment at the gate, load-bearing): exactly ONE
Util.Roll in the gate, ZERO draws in the pick. The Mission.Build draw-stream change (Roll 55→80
+ PickLayout's draw removal) is accepted, consequence-free version skew for in-flight saves:
mission terrain was never save-deterministic — normal play clock-seeds Util.Rng, and DAILY pins
ForcedLayout and bypasses the deck entirely.

**SIGHTLINE_EXPOSURETEST** (windowless, 200 seeds): enumerates all 1098 routes (mid columns
hold 2-3 rows — sampling could miss a branch) and asserts the invariant on each; asserts zero
in-run deck repeats (1200 draws); all 8 objectives dealt (Defend 528 / Rescue 200 / Escort
exactly 200 = 1/map); all 35 arenas dealt (min 17 / mean 34.3 / max 69 — the 8 hint arenas sit
at 46-69, reduced weight but still themed). PASS.

Measured (h0, N=10 CRN slots; R0 = base 1e504f9 on slots 0-9):

| batch | funnel auth/rej/proc | completion | Defend fielded | Defend on route | distinct arenas |
|---|---|---|---|---|---|
| R0 base, slots 0-9 | 56.3 / 0.0 / 43.8 | 60% (g50/s70) | n=4 @ 50% | (n/a, base) | 19 distinct/batch |
| R1 deck, lean 75 | 77.4 / 0.0 / 22.6 | 50% (g50/s50) | n=15 @ 53% | 75% of runs | 2.75 all-runs mean |
| R2 deck, lean 80 | 76.2 / 0.0 / 23.8 | 50% (g50/s50) | n=17 @ 59% | 85% of runs | 2.70 / 3.40 full-depth |
| R2b disjoint slots 10-19 | 76.7 / 0.0 / 23.3 | 45% (g40/s50) | n=19 @ 74% | 80% of runs | 2.85 / 3.44 full-depth |

Targets: procedural 20-25% HIT (23.3-23.8). Defend on >=80% of runs met on 2 of 3 slot sets
(80/85/65 — the reviewer's unseen BASE=30 set read 65%): the structural guarantee holds (the
anchor is on every route), but PLAYED reach is early-death-sensitive — a run that dies before
the anchor column never fields its Defend. The 75→80 anchor lean lifted the floor (R1 read 75%
at lean 75); the residual sensitivity is FUL-13 input. Defend win pooled 67% (24/36) — inside
FUL-4's 60-80
band; the exposure did not break the repair. Distinct-authored-arenas/run >=4.5 MISSED as
specified but structurally unreachable: the target arithmetic assumed 6 authored fights/run,
and real full-depth routes play 4.5-5.0 fights (an EVENT node replaces a fight; the boss is 1)
× 77% authored = a 3.5-3.9 ceiling, of which 3.40-3.44 (~90%) is delivered — with repeats now
IMPOSSIBLE (the old with-replacement sampling put 57% of authored missions on 8 hint arenas).
The honest variety win is the deck guarantee + all-35 exposure, not the 4.5 number.

BUDGET BREACH (reported, not hidden): h0 completion 60 → 50 (same slots) / 45 (disjoint),
−10/−15 vs the ±7 window. Attribution is unambiguous in the per-objective tables: every other
objective holds 90-100%, while Defend goes from n=4 fielded per 20-run batch to n=17-19 at
59-74% and mid-run Decapitate (never dealt to mids by the old rotation) fields n=13-15 at
67-85%. The drop is the PRICE OF EXPOSURE — routes now actually contain the roster's contested
cells — exactly the FUL-13 TRUE NORTH re-baseline input the roadmap anticipates. Nothing was
reverted; tuning Defend down would re-hide what FUL-4 repaired.

Verified: Release 0/0; EXPOSURETEST / SAVETEST / EVENTTEST / COMBATTEST / MODETEST / PAIRTEST
PASS; autoplay x3 clean (no exceptions/TIMEOUT); campaign-map screenshot inspected (an anchor
column showing its RESCUE/DEFEND split renders + labels correctly).

Gotchas for future waves: Mission.DeckSeed is PUBLISHED BY Game.SetupMission (all five mode
entries route through it) — a bare harness Mission.Build sees whatever was last published (0 if
none), fine for the empty-deploy guard but pin it if a new hook needs a specific deck. DAILY
still bypasses the deck via ForcedLayout (its determinism contract predates FUL-9). The
objective plan runs AFTER event stamping in GenerateMap and must stay there (the anchor column
must be provably event-free). Keep the gate's draw-order comment intact: exactly one Util.Roll,
zero draws in PickLayout/DeckPick — a second draw anywhere in that path breaks CRN pairing.

# PROGRAM FULCRUM — FUL-5 HANDS landing (2026-08, wave dev)

- **FUL-5 HANDS** (wt-ful5, base 5b7ac98): the EV bot learned the verbs, so FUL-1's compass
  prices real play instead of no-ops. Measured one lever per round (paired h0 N=10 = 20
  campaigns, CRN slots 0-9; R0 = own base reference on the same slots). Every probe stays an
  honest EV argument — no scripted quotas; two rounds (R1, R2-greedy) came back byte-identical
  to base and were treated as the finding ("the gate is unreachable"), not padded.
  - **BRACE** 1 → **82**/batch (+ FOCUS 5 → 24): three iterations — a rusher arm in
    HoldOverwatch (R1: never fired — unreachable), a step-5a combat-brain probe + a duck veto
    for the shoot-then-brace turn (R3: sloppy-only ~5), and the real stage (R7): Defend/Escort
    zone-holds route their watch through HoldOverwatch, whose rusher arm (committed charger
    inbound — BERSERKER/HOUND/STRIKER/BRUISER — that a lethal reaction can't remove) now fires
    where waves actually charge. SHOCK DOCTRINE procs 0 → **6** (5 picks) — the headline dead
    verb-boon now reaches play.
  - **ITEM** 0 → **19**/batch: TrySmokeCover — smoke the most-exposed sub-half-HP squadmate
    (self incl.), probed objective-agnostically from SmartStep (the old ">=2 guns AND no shot
    AND it's me" conjunction, buried where objective routines never reach, measured 0/500
    missions). Self-bounded by the 1-charge/mission budget.
  - **PATCH** ~1/500-missions → 4-6/batch: heal gate missing>=4 → >=3, an objective-agnostic
    corpsman block in SmartStep (the objective routines bypassed SmartCombatStep — PATCH was
    structurally dead on 5 of 8 objectives), and a bounded move-to-patch (hurt ally at Cheby
    2-3 → step adjacent, Cd-gated).
  - **DRAG** 0 → 5-7/batch: the Escort march + zone-hold gained SmartEvac's straggler pull
    (incl. reeling the leashed VIP from Cheby 2 into extract range).
  - **GRENADE** 8 → 6-8/batch (plateau): grenade-first on covered 2+ clusters (step 2a +
    SmartDefend) is honest but thin — see the verdict below.
  - **AutoEventChoice** rebuilt: 70/30 value-biased (EventChoiceValue competent-play prior),
    randomness HASHED off (MapSeed, node id) — never an Rng draw (CRN; Events.cs GambleSucceeds
    precedent; replaced the always-safe rule + its IsSafeChoice/HasDownside pair). BY
    EVENT-CHOICE went from safe-arms-only to 9 populated arms (defector all three, medic:1,
    drill:1 ...), value-driven (medic:1 taken when nobody is hurt).
  - **COUNTER-PREP** 0 → 10-12 buys/batch (AutoShop buyable set + a modest prior 4f — the slot
    only exists when a faction is telegraphed, CanBuy re-gates).
  - **Mod priors de-flattened**: SUPPRESSOR 45/165 = 27% of mod buys (~2x slate share, an
    affordability artifact of the flat 6f prior) → **13/142 = 9%**; SCOPE/HOLLOW POINT lead as
    a competent player installs.

## Measured (rounds; paired h0 N=10 = 20 campaigns each, slots 0-9; R0 = base 5b7ac98)
| round | lever | completion (greedy/sloppy) | key counters |
|---|---|---|---|
| R0 | base reference | 60% ±11 (50/70) | BRACE 1, PATCH 1, GREN 8, ITEM 0, DRAG 0; SUP 27% of mods; safe event arms only |
| R1 | HoldOverwatch rusher arm | 60% — byte-identical batch | the arm never fired: HoldOverwatch unreachable in open combat |
| R2 | + PATCH >=3 + move-to-patch | 60% (50/70) | greedy leg still byte-identical; sloppy BRACE 5; PATCH 0 (objective routes bypass the brain) |
| R3 | + grenade-first, duck veto, PATCH pre-routing | 55% (40/70) | BRACE 6, PATCH 4, GREN 7 |
| R4 | + smoke-on-wounded, Escort DRAG, patch r2-3 | 55% (50/60) | DRAG 7, ITEM 1 (2b unreachable too), BRACE 6 |
| R5 | + hashed event choice, COUNTER-PREP, smoke pre-routing | 65% (80/50) | ITEM 14, PREP 12, 9 event arms |
| R6 | + mod prior de-flatten | 65% (70/60) | SUP 9.6% of mod buys |
| R7 | + Defend frag + holds route through HoldOverwatch | **75% ±10 (80/70)** | BRACE 82, FOCUS 24, ITEM 19, SHK procs 6, PATCH 5, GREN 8 |
| R7b | same tree, FRESH slots 100-109 (robustness) | 50% (40/60) — different worlds, not budget-comparable | BRACE 31, ITEM 15, **PATCH 10**, PREP 11, SUP 5.6%, SHK 1 proc, FDR 0; event arms incl. the profiteer:0 gamble + cache/distress |

Budget: R7 = R0 +15 on the same slots — the bot got BETTER (allowed; recorded as FUL-13 input:
the h0 baseline for the finished tree is now ~75 under this bot ON THESE SLOTS; the fresh-slot
50% shows world-to-world variance still dominates absolute levels — only paired same-slot
readings are level-comparable). PAIRED MARGIN drifted -0.30 ±0.26 → 0.00 ±0.39 (n=10 — noise).

Verified at landing: Release 0/0; COMBATTEST / AITEST / SNAPTEST / SAVETEST / **PAIRTEST** all
PASS (PAIRTEST is the load-bearing one — the event-choice 70/30 and every new probe had to keep
A/A CRN identity, hence hash-not-draw everywhere); autoplay x3 clean (no exception, no TIMEOUT).

## Design verdicts (per the FUL-5 decision rule — recorded, not tuned around)
1. **BRACE in clean greedy play is structurally rare; its home is holds and the behind-game.**
   Shoot-twice beats shoot-brace whenever the charger is exposed (hit >= 45), so the combat
   brain braces ~1-2/batch and the sloppy leg 4-5 (via its shot-skip slips) — coherent with
   UNDERTOW's "losing-position tool" intent. The volume lives where the design said it should:
   zone/line holds (R7). **Watch item (FUL-13):** on holds the rusher arm now largely REPLACES
   the wide watch (OVERWATCH 54 → 8/batch) and completion rose — if brace-over-watch is strictly
   dominant there, the reaction economy's lethality-vs-denial pricing deserves a check.
2. **FIELD DRILLS (FDR) procs are structurally ~unreachable: VERDICT, do not price the boon on
   this counter.** The proc = a SECOND drag/vault by one soldier in one turn. A first drag
   consumes the geometry the second needs (the pulled ally lands adjacent = no longer a legal
   target), so it needs TWO separate Cheby-2 stragglers in one turn — the greedy bot's spacing
   produces whole batches with drag totals of 2-7 spread across turns; vaults are 0. **FUL-6
   rework brief:** count a drag + a vault as the drill (sum, not per-verb), or replace the
   second-use effect with "+1 MoveBudget on any turn the soldier dragged/vaulted" — both make
   the boon's effect fire on play that actually occurs.
3. **RECLAIMER (RCL) procs still 0** at FOCUS 24/batch — a cone-kill while the boon is held
   remains a thin coincidence; same family as FDR (effect site narrower than real play).
   Candidate for the same FUL-6 pass; not a named FUL-5 target, so recorded only.
4. **GRENADE's honest ceiling under this bot is ~6-8/batch (target 10).** The pre-shot window
   (covered 2+ cluster, no ally in blast, in range+LoS, no likely kill available) anti-correlates
   with the gun: shot declines happen at range, grenade range is short, and active enemies
   de-cluster under the pod AI. The remaining volume would have to come from pre-fragging
   dormant pods (a real player line, but a perfect-info-flavored one for the bot) — declined
   as quota-chasing. FUL-6's pods-of-3 + linked activation is exactly the stage this verb waits
   for; re-measure there.
5. **PATCH's ceiling is roster presence, not gates (target 10, measured 4-6).** The founding
   squad has NO corpsman (Mission.NewRunSquad = ASSAULT/RANGER/SHARPSHOOTER/GUNNER); the class
   enters via casualty backfill only (~2-3 campaigns of 20, ~15-20% of soldier-missions), so
   even honest per-presence rates (~2/corpsman-campaign) cannot reach 10/batch. FUL-7 (downed
   soldiers) and any founding-roster change re-open this; the gates are ready.

## Gotchas (process)
- **The heat pin is SIGHTLINE_BALANCE_HEAT, not SIGHTLINE_HEAT** — the first reference batch
  silently cycled {0,2,4,6,8} (SIGHTLINE_HEAT is read per-run by StartMission, but the BATCH
  schedule variable is separate). Re-ran; kept only as texture.
- **A byte-identical paired batch is a legitimate probe result** — it proves the gate never
  fired (R1, R2-greedy) and locates WHERE the cascade eats the decision. Cheaper than tracing.
- **The paired slots fix the event-node sample**: slots 0-9 reuse the same 10 MapSeeds every
  round, so BY EVENT-CHOICE arms are world-locked across rounds — cross-check arm exposure on a
  fresh slot base (SIGHTLINE_BALANCE_BASE) before reading it as policy.
- Never rebuild while a batch runs — dotnet's in-place DLL overwrite races the mapped image of
  the running process (observed surviving, not guaranteed).

# PROGRAM FULCRUM — FUL-10 FORKS (2026-08, wave dev on wt-ful10)

The strategic layer got its forks: **seven trade-off field events** (catalog 10 → 17) crossing
salvage / scars / veteran-rank+slots / faction prep+vendetta+heat / wounds+intel — ids
`warpension fieldhospital informant quartermaster bloodfeud reservecall warchest`, ids + arm
ORDER frozen forever (the FUL-1 compass keys `id:arm`). Six new `EventOutcomeKind`s (appended;
the kind is never persisted): GrantScar / CureScar / Salvage / GrantPrep / RankKills /
ReleaseSoldier, plus a seeded-arm gate (`ChancePct` + `OnFail` riding the existing
`GambleSucceeds` node hash — reload-stable, zero Util.Rng draws; an OnFail pair fires exactly
one side of a gamble off the one roll). `EventChoice` gained a third outcome slot (C3) for the
reservecall triple. Event salvage NEVER touches meta/disk (EventCatalog.Apply stays pure):
it pends in the new persisted `Run.PendingSalvageReward` (append-only DTO tail + SAVETEST leg)
and `AwardMetaRunEnd` commits it win OR loss, folded into the FUL-12 `EndSalvage` slab.
`HasDownside` learned GrantScar + ReleaseSoldier so the safe-first bot never reads a scarring
arm as "safe" (honest arm-uptake waits on FUL-5's chooser, per plan). [Integration note: FUL-5
landed first and REPLACED IsSafeChoice/HasDownside with the hashed 70/30 value chooser; the
downside judgments above live on as signed EventOutcomeValue cases (GrantScar -3*chance,
ReleaseSoldier -6, etc.) composed at the FUL-10 merge.]

**Two veteran-economy contracts** (Contract append — BOTH tail pins moved, SaveGame SelfTest +
CONTRACTTEST, each now also pinning Spearhead's ordinal POSITION [3]): **MERCENARY CLAUSE**
(MRC) — recalls half price, round up, halved in exactly one seam (`Game.DraftRecallFee`, which
the per-card fee, the bill row and ConfirmDraft's charge all read → the discount is honest by
construction) but survivors never enshrine; **LIVING LEGENDS** (LGD) — kills credit DOUBLE at
CreditKill (feats stay single), Rank>=2 survivors pension +6xRank at run end, and a KIA whose
name matches a reserve record ERASES it (`SaveGame.RemoveVeterans`, name-keyed like
EnshrineVeterans' dedupe). Both `Contract == X`-gated, inert at None.

**Orphaned-perk fix:** the HORIZON-W6 trio joined real class lines (Vantage → SHARPSHOOTER +
GUNNER; Breaker → ASSAULT + SHARPSHOOTER; Siegebreaker → RANGER + ASSAULT) — the ClassLine
table's own "every perk appears in >=1 line" doc rule is TRUE again and now ENFORCED by
enumeration (`Run.PerksInNoClassLine()`, asserted empty in CONTRACTTEST).

**Hud:** the draft contract row re-fits SIX cards (width shrinks to the row, height grows to
the tallest WrapLines-wrapped desc — wrap, never truncate; first cut used a wrong 13px line
height and LGD's 4th desc line spilled the border — WrapLines' lh is size+6). DebugVetDraft now
demos the MRC bill (NOX picked at RECALL 17, VEGA greyed at 21, bank 20). New
`SIGHTLINE_EVENTID=<id>` stager pins DebugEvent's staged event (default unchanged).

## Verified
Release 0/0. EVENTTEST (a mutation leg per new kind; CureScar newest-first + BurnScarred MaxHp
revert; Vendetta fallback-Legion brand; RankKills+GrantScar same-soldier coupling; seeded scar
double-apply idempotent; Salvage/Wound OnFail pair exclusive; PendingSalvageReward §4 save
round-trip) / SAVETEST / CONTRACTTEST (All==5, new tail pin, LGD double-credit + None single,
MRC fee halved exactly once + restored on deselect, perk-line coverage) / METATEST (leg 11:
MRC bill 26 = 17+9 charged once at ConfirmDraft, enshrine skipped; LGD pensions 30 paid once
with the 25 event claim in one commit, RemoveVeterans erased exactly NOX and nobody else) /
CODEXTEST / DRAFTTEST — all PASS. `SIGHTLINE_CONTRACT=mrc|lgd` autoplay x3 each: clean RESULT
lines, no exceptions, no TIMEOUT. Shots: 4 staged events (fieldhospital shows the greyed
illegal arm), the six-card draft row, the MRC-discounted vet-draft bill.

## Measured (SIGHTLINE_BALANCE=20 ladder {0,2,4,6,8}, CRN slots shared with a base-bce2cbd
scratch-clone control — a paired A/B, the FUL-2 method)
| metric | base | FUL-10 | verdict |
|---|---|---|---|
| pooled run completion (40 runs) | 40% | 45% | +5 — at the ±5 budget boundary, in budget |
| mission win-rate by heat | 92/84/95/79/64 | 97/87/92/78/69 | deltas +5/+3/−3/−1/+5 — stable, no cliff |
| h0 run-completion cell (n=8) | 63% | 88% | +25 nominal, 2 runs of 8 — small-n noise (FUL-11's chunks swung 45-85 at n=10); the mission-level row is the reliable read |
| policy gap (greedy−sloppy) | −10 | 0 | both "healthy slack"; FUL-2/13 watch item unchanged |
| VNT/BRK/SGE picks | 9/6/4 (slot-B only) | 13/15/9 | slot-A reachability lifted trio exposure ~2x; per-heat BY PERK rows now exist for all three |
| BY EVENT-CHOICE new ids | — | warpension:2, fieldhospital:2, quartermaster:1, bloodfeud:1, warchest:1 | 5 of 7 fielded in 20 worlds (~1-in-9 exposure each; informant/reservecall await bigger batches) |
| contract telemetry | none | none | Contract==None inertness held — zero contract records in both reports |

## Accepted version skew (note, not corruption)
Growing `EventCatalog.All` 10 → 17 shifts `IndexForNode`'s hash-and-probe assignment, so an
IN-FLIGHT save's **unvisited** "?" node shows a different event after upgrading. Resolved
events are already baked into Run state and MapPos/Visited semantics hold, so nothing corrupts
— EVENTTEST's determinism legs compare two regenerations under the SAME catalog and still pass.
Same class of skew is why the "?"-node stamp clamp was deliberately NOT widened this wave
(GenerateMap re-runs from MapSeed on load); the `Clamp(mids/4, 1, 3)` exposure lever is parked
in ROADMAP's FUL-13 entry with that caveat attached.

Gotchas for future waves: the BALANCE flywheel ignores SIGHTLINE_HEAT and always spans the
ladder itself on deterministic CRN slots — two same-N batches on one tree are IDENTICAL, which
is exactly what makes a scratch-clone base control an honest paired A/B; `WrapLines`' line
height is `size + 6`, not the font size — size any wrap-fitted panel from its dy values;
`GambleSucceeds`' roll is pct-independent per node, so a ChancePct pair (success arm +
OnFail arm) resolves exclusively off one roll by construction.

# PROGRAM FULCRUM — FUL-8 PIKEMAN (2026-08-28, wave dev on wt-ful8)

**Goal.** The SARISSA/"PIKEMAN" — a Wardens lane-holder that plants a braced foe-red focus cone
over a movement lane and STAGGERS the first soldier through. The 21-archetype roster contested HP,
information, morale and progress; nothing contested MOVEMENT. This piece does, and it teaches the
player's own BRACE [B] by mirroring it exactly. Binding spec:
docs/plans/FUL-8-pikeman-FUL-10-forks.md §FUL-8 (every seam pre-verified against the tree).

## What shipped (base e3bcb3b, includes FUL-5)
- **Zero new combat machinery, as specced.** The enemy plant arms the exact player flag set
  (OnOverwatch+OwBrace+OwFocused+OwDir) and the existing OnUnitEnteredTile reaction path does the
  rest — halved no-crit stagger (Combat.BraceFullDamage is Team.Player-gated, so an enemy brace
  ALWAYS takes the halving), cone gate, one-reaction cap, BeginTurn one-round lifetime.
- **Ai.cs** — dedicated PIKEMAN branch after CUSTODIAN: opportunism first (a >=65% shot on an
  EXPOSED soldier beats planting — holder, not statue), lane anchor = nearest non-VIP soldier,
  plant only within cone reach (MaxRange+2), plant tile from `reach` keeping the action, scored
  SPOTTER-style (cover*16 + height*8 − |dn−4|*1.4 − 22 if dn<=2) with a hard LoS-to-anchor filter
  (a blind plant holds nothing). Gated e.Routed==0 (W8 rule) + !Disoriented (FLASH counterplay)
  + Ammo>0; every failure falls through to the generic loop — never a dead turn.
- **Game.cs exec** (ActAfterMove, before ShootTarget, SiegeCharge template): arms the flags,
  sets Facing down the lane (the silhouette's pike IS the direction read), "BRACED" pop.
- **Renderer** — DrawOverwatchThreat gained the cone TRUTH GATE (`w.OwFocused && !InOwCone →
  skip`): zero-regression today, and the red wash now mirrors the reaction gate exactly. The
  focus-cone edge-rays + chevron factored into shared DrawConeRays — drawn gold for the player,
  foe-red for a braced+focused enemy, one vocabulary that can't drift. New PIKEMAN silhouette
  (squat planted body + raised diagonal pike + crossbar lugs; flows into DrawCodexGlyph). Honesty
  ride-along: the STAGGERED pop was Pal.Good unconditionally — now colored by victim team.
- **Codex** — SARISSA/PIKEMAN bestiary row (counterplay: break its watch, go around the cone, or
  feed it a cheap step) + "PIKEMAN" in the CODEXTEST required array. NEW CONTACT banner + enemy-ID
  hovers automatic off the row.
- **Spawns** (CRN draw-count neutral — exactly one RandF per spawn, only windows moved):
  Wardens re-slice SNIPER 24→20 / MEDIC 14→12 / CUSTODIAN 8→6, **PIKEMAN 10% m2+** (m1 routes
  SCOUT); default cascade m3+ ~3% mid-tail carved from SCREENER/BOMBARD/WARBRINGER (4/4/3 →
  3/3/2), the 1% GRUNT/SCOUT/BRUISER tails and all first-appearance tiers unchanged. Defend rich
  waves + LAST STAND inherit via SelectArchetype (observed in wave logs); no demote — a wave
  PIKEMAN plants in plain sight. Stats: HP 7 / Aim 58 / Mob 5 / SMG (the cone is the pike, the
  gun is flavour; W8 Wardens-support band).
- **Bot** — `InEnemyBraceLane(x,y)` mirrors the reaction gate exactly (alive+active, armed watch,
  unspent reaction, ammo, range+LoS(commanding), cone); TileExposure +18 (between an exposed gun
  and the siege 30 — a stagger costs a turn, not a life). SmartApproach/ScoreDestTile/SmartStep
  route around lanes with no other bot change.
- **Harness** — SIGHTLINE_PIKETEST (plant emits Brace toward the anchor + exec arms the flag set;
  the ==2 halving pin — mover HUNKERS so crit is structurally 0, making Math.Max(1,4/2) exact and
  the no-crit assert honest rather than re-staged-away; cone blindness; a player stagger-back
  drops the plant; Disoriented/Routed never plant) + SIGHTLINE_PIKESHOT (stages a planted lane
  over the squad's approach).

## Measured (paired slots; R0 = base e3bcb3b in a scratch clone, same SIGHTLINE_BALANCE_BASE slots)
- **Completion** (h0 pooled n=40 matches/side, slots 0-19): R0 30% → R1 27.5% (chunks 40%/15% —
  high slot variance, pooled inside the ±5 gate). h4 (n=20, slots 0-9): 35% → 40%.
- **Composition** (R1): PIKEMAN 3%/3%/4% of faction-stamped spawns (h0a/h0b/h4) ≈ **10% of
  Wardens fights** (Wardens ≈ 1/3 of stamped spawns), 1-3% of the default cascade (m3+ window
  dilluted by m1-2 fights). R0 logs contain ZERO PIKEMAN lines — the baseline is honest.
- **Route-tax gate** (the FUL-5 worry: the bot now paths AROUND enemy lanes): unpinned legs
  showed Escort +1.2t pooled on n=6-9 — re-measured with whole-run objective pins at n≈90
  missions/side: **Escort 5.9t/99% → 5.6t/100%, Evac 5.5t/97% → 5.8t/98%** — both inside the
  +1t budget. The lane taxes routes; it does not stall them. The unpinned spike was slot noise.
- Gates: Release 0/0; PIKETEST+STAGGERTEST+COMBATTEST+AITEST+SAVETEST+CODEXTEST PASS; autoplay
  x7 no-exception/no-TIMEOUT. Save-compat: NONE touched (enemies never serialized; Cls is a
  string; no enum appended). Callsign SARISSA (soldier pool owns "PIKE" — verified collision).

## Gotchas / notes
- **The ==2 halving pin needs crit structurally impossible, not re-rolled away.** The brace
  halving OVERWRITES res.Crit at queue time, so a test can't tell a halved crit (4*1.5/2 = 3)
  from a bug by the flag — the mover HUNKERS (Combat zeroes crit vs hunkered) so any connect is
  exactly 2 and the assert never selects its own evidence.
- **Screenshot staging needs the lane ON the squad** — a live-position plant reads as a distant
  red thread; DebugPikemanLane teleports the watcher ~6 tiles off a soldier (WAVEBANNER staging
  precedent) so wash+rays+chevron+BRC all land in one frame.
- **A live-board Wardens shot is findable without new code:** the top-bar hostiles label reads
  the faction name (Hud.cs), so loop SIGHTLINE_MISSION=3 SIGHTLINE_WAKE=1 shots until "WARDENS"
  + a pike silhouette shows (dormant pods draw as "?" — WAKE is required to see archetypes).
- Optional spec ride-along NOT taken: force-showing the BRACE field tip on first PIKEMAN
  sighting (UpdateBraceCallout force path) — left for a teaching pass; the codex row + NEW
  CONTACT banner already carry the mirror lesson.

# PROGRAM FULCRUM — FUL-6 CRITICAL MASS landing (2026-08-28, wave dev on wt-ful6)

- **FUL-6 CRITICAL MASS** (wt-ful6, base 588d781 — the full program tip): pods of 3 + linked
  activation in mid/late missions — one real multi-pod battle per mission instead of six 2-enemy
  executions, so the comeback economy (BRACE/morale/verb boons/grenades) gets its stage;
  morale/rout reaches LAST STAND's horde; the FUL-5 FIELD DRILLS verdict consumed (rework, not
  retire).
  - **PodPlan (Mission.cs):** pure greedy split, no RNG (7->{3,2,2}, 8->{3,3,2}, 9->{3,3,3},
    12->{3,3,3,3}; never a pod of 1 from count>=2), missions 3+ only; m1-2 keep i/2 pairs and
    the finale keeps i/2 EXACTLY (FUL-11 kit geometry — FUL11PROBE green by construction).
    **Cohesion:** members 2-3 anchor to the pod lead's post-relocate row (+1/+2, flipped at the
    board edge), sharing the pod's column band — pods land as visible clumps, ZERO extra draws
    (the collision-relocate loop stays the only conditional draw source). The m3/m5 mid-boss
    joins a pod of 3 (its screen can rout out from under it) — accepted, watch item.
  - **Linked activation (Game.cs):** ActivatePod rider (m3+, real pods, inside the `any` gate):
    the nearest OTHER pod with a dormant member within LinkRange=6 (closest member to closest
    member, TileDist) goes Suspicious + `_linkedPods`; ResolveSuspicion gains one arm — a linked
    pod confirms to Alert even UNSEEN (no scatter, the telegraphed 4.3 path) and the set clears
    after the pass. One link per wake; never chains (ResolveSuspicion never calls ActivatePod);
    zero RNG — position-derived (PAIRTEST green every round). Telegraph: HEARD THE GUNS pop +
    CONTACT! BannerSub "a nearby pod is moving to the sound"; LINKED ALERTS codex row + CODEXTEST
    entry (the counterplay list IS the row).
  - **LAST STAND morale (Game.Endless.cs):** SpawnEndlessBodies splits each wave's LANDED bodies
    into sub-pods via the shared PodPlan (ids _nextWavePod++ from 100, _podOrig sealed to what
    landed — the FUL-4 seal pattern); elite stays PodId -1 (HORDETEST pin); pressure-clock waves
    stay podded:false. TERROR un-excluded from endless boon offers (its exclusion comment went
    false this wave) — endless-only pool composition change, campaign CRN untouched.
  - **FIELD DRILLS rework:** the old proc (a second drag/vault by one soldier in one turn) was
    self-consuming (0 procs, every batch ever). New: *a DRAG or VAULT drills the soldier forward
    — +1 tile of movement for the rest of that turn* (transient Unit.DrilledThisTurn, reset in
    BeginTurn, never persisted; MoveBudget +2 half-steps after the *2), granted once/soldier/turn
    at IssueDrag/IssueVault via GrantFieldDrill — also the honest RecordProc("FDR") site (the TRR
    grant-site precedent; BOTH old >=2 proc lines deleted). FieldCraftLimit stays 2 (COMBATTEST
    pins untouched); Boon ordinal untouched (Desc + codex copy only; new DRAG & VAULT rules row).
  - **Escalation lever 1 (R5, measured breach):** the full stack ran -12.5 pts h0 completion vs
    the fresh same-slot R0 (budget <=8) -> the spec's first lever landed: initial force -1 on
    3-pod missions (floor 3, the FUL-4 defend-trim precedent). Result: combined h0 == R0 (dip 0).
    Levers 2 (LinkRange 6->4) and 3 (one-link-per-mission latch) were NOT needed.

## Measured (paired h0; each round = two N=10 chunks, slots 0-9 "a" + slots 10-19 "b" via
## SIGHTLINE_BALANCE_BASE; R0 = FRESH base-588d781 reference on the same slots, run first.
## Completion = greedy/sloppy % per chunk; counters greedy/sloppy per chunk)
| round | lever | a: g/s | b: g/s | key counters (a; b) |
|---|---|---|---|---|
| R0 | base 588d781 reference | 40/60 | 30/30 | BRACE 36/43; 79/36 · GREN 2/7; 0/5 · PATCH 3/6; 3/5 · DRAG 0/3; 0/0 · FDR 0 procs (4 picks); 0 (2) · RCL 2 (2); 1 (3) · TRR 13 (4); 9 (1) |
| R1 | PodPlan + cohesion | 30/40 | 30/20 | BRACE 32/46; 127/165 · GREN 2/5; 0/2 · FDR 0 (1); 0 (3) · RCL 3 (4); 0 (2) · TRR 31 (6); 6 (1) · greedy-a OVERWATCH 38->343 (hold-heavy vs 3-gun contacts) |
| R2 | + linked activation | 40/50 | 10/10 | BRACE 32/55; 117/152 · GREN 3/5; 1/1 · FDR 0; 0 · RCL 3 (2); 1 (2) · TRR 29 (5); 6 (1) |
| R3 | + endless wave pods | 40/50 | 10/10 | campaign chunks BYTE-IDENTICAL to R2 — the lever is endless-only by construction (CRN discipline visible); endless leg below |
| R4 | + FDR rework | 40/50 | 10/10 | BYTE-IDENTICAL to R3: zero boon-held drags occurred in these 40 worlds -> zero grants (the mechanism procs deterministically — PODTEST leg f) |
| R5 | escalation lever 1 (count-1 on all m3+ non-finale missions) | 50/80 | 10/20 | BRACE 62/28; 63/51 · GREN 1/8; 1/4 · PATCH 1/2; 4/3 · FDR 0 (4); 0 (1) · RCL 2 (4); 0 (2) · TRR 20 (4); 18 (2) |
| h4 | close leg, final stack (slots 0-9) | 20/20 | — | BRACE 55/22 · GREN 3/4 · PATCH 4/2 · FDR 0 (5) · RCL 0 (2) · TRR 11 (3) · h4 mission win-rate 76%, paired margin +0.10 ±0.64 |

Combined h0 (40 matches): R0 40% -> R1 30% (-10) -> R2/R3/R4 27.5% (-12.5, BREACH of the <=8
budget) -> R5 40% (dip 0, IN BUDGET; chunk split +15/-15 — world-to-world variance dominates
absolute chunk levels, the FUL-5 R7b lesson; the paired greedy-sloppy margins stayed -0.6..-1.1
throughout). PAIRTEST PASS on every round's tree.

**Endless depth leg (R3 stack, 32 stands, default heat cycle {0,2,4,6,8}, chunks of
SIGHTLINE_BALANCE_ENDLESS=8 at BASE 0/8):** slots 0-7 mean 6.0 / median 6 / p90 7 (h0 median
6.5); slots 8-15 mean 5.4 / median 5.5 / p90 6 (h0 median 6). **IN the APEX 5-6 band**; zero
wave-cap/frame-cap hits; wave sub-pods live (routs play mid-stand; the ending elite stays
morale-exempt).

## Design verdicts (recorded, not tuned around)
1. **GRENADE >=10 did NOT materialize (measured 2-9/batch combined, R0-level).** The FUL-5
   verdict predicted pods-of-3 as the frag stage; measured, the bot's window (covered 2+ ACTIVE
   cluster, pre-shot) still anti-correlates: dormant pods now CLUMP (the stage exists on the
   board) but woken pods scatter-to-cover and de-cluster before the bot's frag gate re-fires,
   and the low count is emergent geometry (approach crosses SightRange first; woken pods scatter), not a coded decline as perfect-info bot play. The stage is real
   for HUMANS (the clump is visible pre-fight); the bot cannot price it honestly. FUL-13 input.
2. **FDR: 0 procs in the wave batches — an honest zero, not a dead mechanism.** The rework's
   proc surface now equals drag/vault-under-boon frequency; in these 100 campaigns drag volume
   was 0-3/batch (Escort straggler pulls) and never overlapped a FIELD DRILLS pick. PODTEST leg
   f pins the grant (drill + exactly one proc + MoveBudget +2). FUL-7's recurring drag stage
   (downed-soldier carry chains) is where this boon prices — as the FUL-5 brief expected.
3. **RCL is no longer structurally dead: 0 -> 1-3 procs/batch** (R1a 3, R2a 3+1, R5a 2) — more
   movers through focused cones at 3-pod contacts re-arm the watch occasionally. Volume still
   thin; keep the FUL-13 retire-or-rework question open but with a live baseline now.
4. **TRR procs 6-31/batch (was 9-13):** the rout economy is livelier — a 3-pod break routs more
   survivors at once. BRACE budget held (>=30/batch every round; up to 165 on hold-heavy worlds).
5. **The b-chunk (slots 10-19) is structurally harsher under the pod stack** (R0b 30% -> stack
   10-15%) while the a-chunk recovered fully (50 -> 65 at R5). Same-slot pairing shows the dip
   concentrates where R0 was already losing — bigger contacts punish already-marginal worlds.
   FUL-13 re-baseline input.

## Gotchas (process)
- **A worktree COPY (`cp -r`) shares the original's .git worktree metadata** — `git checkout`
  inside the copy detaches the REAL worktree's shared HEAD (files stay put; symbolic-ref +
  reset recovers). Use `git archive <commit> | tar -x` for scratch measurement trees.
- **R3/R4 coming back byte-identical to R2 is the CRN discipline working**, and it localizes
  each lever's true surface: endless-only (R3) and grant-only (R4) levers cannot move campaign
  batches. Cheap self-verification, same family as FUL-5's "byte-identical is a finding".
- The balance harness's Combat.RunBoons is the FieldCraftLimit read, not Run.ActiveBoons — a
  harness scene granting a boon must publish to both (PODTEST leg f does).

# PROGRAM FULCRUM — FUL-7 LAST LIGHT (2026-08-28, wave dev on wt-ful7)

**Goal.** Lethal damage on a soldier becomes a 3-turn BLEED-OUT with stabilize/carry/revive
counterplay instead of an instant, decision-free cut — the DESIGN §4 "death stakes: Thin" fix, and
the stage FUL-5's verdict reserved for PATCH. Binding spec:
docs/plans/FUL-6-critical-mass-FUL-7-last-light.md §FUL-7 (seams re-verified against c74378e —
the post-FUL-6/8/10 program tip).

## What shipped (base c74378e)
- **The single lethal seam, as specced.** `CanGoDown` guards the TOP of KillUnit: soldiers
  (never VIP/captive — DEATHTEST semantics; never enemies — rout is their drama) enter a 3-turn
  DOWN instead of dying, ONCE per soldier per mission (`WasDownedThisMission` — the second lethal
  event, incl. ANY damage on a body already down, kills outright: the AoE/fire honesty valve).
  `EnterDowned` stays clean of death bookkeeping (no Fallen/Memorial/KIA-stamp/NoLosses/
  RecordKill; Vengeful DOES stage at the fall); `ExpireDowned` runs the FULL death flow through
  KillUnit with cause = the DOWNING archetype (`DownedByCls` — snapshot of the same ActiveAnim
  attribution; DoT-downs bucket "?" like DoT deaths, BlurbFor-gated), so a bleed-out KIA reaches
  Run.Fallen identically to an instant KIA (FUL-10 LGD's veteran-erase needs no special case).
- **The clock:** timers tick in StartPlayerTurn on the squad's clock; STABILIZE (universal verb,
  key E — T was the tag editor; adjacent, 1 action, never ends the turn) freezes it; the freeze
  needs a STANDING squad — with every soldier down or dead, stabilized timers run too, so the
  all-downed board resolves in <= 3 bounded turns (DOWNTEST-pinned; closes the stabilized-orphan
  infinite stall the spec's bound argument assumed away).
- **REVIVE:** the PATCH executor's Downed arm — up at the heal value (PatchHeal 4 / CombatMedic 3
  at reach 2), actionless that turn, FieldSurgeon triage rides, Cd 3 unchanged; same RecordAction
  chokepoint so the FUL-5 PATCH counter measures the stage for free.
- **Recovery:** EnterBarracks (before the squad rebuild) recovers every downed survivor — Hp 1,
  Wound 3 (the debrief attrition machinery owns it from there), WasNearDeath forced true (a
  full-HP one-shot down never reached MarkPlayerHurt — without this the near-death scar track
  skipped exactly the survivors it's for), report line. Endless: the wave-clear breather revives
  the downed at the mend value (floored 1) and resets the per-wave down budget.
- **The AI rule:** enemies never DIRECT-target the downed — the one `Ai.Plan` players filter, plus
  the same skip in the aim helpers (BestScreen/BestFlash/BestSiege/BestGrenade — a downed body
  neither attracts nor counts in aim decisions; a blast aimed at standing soldiers still kills it)
  and the shove pick, plus `mover.Downed` returns before the overwatch watcher loop (a DRAGGED
  body is a tile entry the spec's "downed never move" claim missed — hazards still apply, so
  hauling a body through fire still kills it, honestly).
- **UI:** prone 0.6x sunk body + pulsing red ground ring; leading DOWN 3/2/1 chip pill (red,
  falling-chevron glyph) flipping to amber STABLE (level-bar glyph); HP pips hidden while down;
  red roster-chip state ("BLEEDING OUT (n)" / "STABILIZED - HOLDING ON"); STABILIZE button +
  tooltip + icon; SOLDIER DOWN banner names the timer; never selectable. Vocabulary honesty:
  soldier true-death pop renamed KIA; the combat log logs DOWN (not KILL) for a survivable
  lethal; the enemy brace stagger skips a body already down. Codex row DOWN (BLEEDING OUT) +
  CODEXTEST required entry.
- **Autopilot:** objective-agnostic rescue block ABOVE the corpsman PATCH slot (corpsman-ready →
  revive, else STABILIZE once); TryMoveToPatch generalized — Cd-ready corpsman closes on
  hurt-or-downed, EVERY other soldier closes on downed-to-stabilize (the founding squad has no
  corpsman); `AutoShouldStabilize` guards the ONE unresolvable freeze: Evac + no live corpsman +
  body away from the zone → let the timer run (the bot has no drag-chain carry in v1 — the
  accepted, recorded bot-vs-player gap; zone-adjacent bodies stay stabilizable via the EXTRACT
  pull; a corpsman dying after a far-body freeze still bounds at AutoMaxTurns → STALEMATE loss).
- **Persistence: NONE** — all five Unit fields transient (ToUnitDto whitelist), single
  mission-START checkpoint verified again on this tree, EnterBarracks resolves every Downed
  before it; SAVETEST gained the belt-and-suspenders leg (a hand-built downed-and-recovered
  soldier round-trips ONLY Hp/Wound/scars). No enum touched anywhere.
- **Harness:** SIGHTLINE_DOWNTEST legs a-h (entry clean of bookkeeping + surplus-reaction purge;
  expiry = the full death flow exactly once, cause = downing archetype, NoLosses failed;
  stabilize-freeze + won-field recovery incl. the near-death track; revive incl. CombatMedic
  reach-2; no-second-down + grenade-finishes-the-body; AI ignores + all-downed bounded even
  stabilized; VIP instant; DRAG@Cheby-2 + EXTRACT-from-zone-adjacent pinned) +
  SIGHTLINE_DOWNSHOT (=1 down + rescuer + lit STABILIZE; =2 executes the real STABILIZE for the
  mid-rescue STABLE frame); existing tests that stage true squad deaths (DEATHTEST/HEATLADDER/
  RESCUETEST/DKTEST/DebugKia) set WasDownedThisMission first — the real second-lethal rule, not
  a bypass. New telemetry: SOLDIER DOWNS report line (downs → revived/recovered/bled-out +
  save-rate) + corpsman-fielded missions (the PATCH per-presence denominator).

## Measured (paired h0; chunks = slots 0-9 "a" + 10-19 "b" via SIGHTLINE_BALANCE_BASE, N=10 each;
## R0 = FRESH base-c74378e reference in a git-archive scratch tree, same slots, run FIRST; h4 leg
## last, both trees, slots 0-9)
| leg | completion g/s | true KIA | downs -> saved (rate) | STABILIZE | PATCH | key notes |
|---|---|---|---|---|---|---|
| R0a | 40/60 | 125 | — | — | 1/0 | BRACE 60/27 · GREN 6 · DRAG 2 · margin -0.70±0.67 |
| R0b | 30/30 | 126 | — | — | 4/3 | BRACE 53/77 · GREN 6 · margin -0.90±0.90 |
| R1a | 50/40 | **81 (-35%)** | 179 -> 84 (47%) | 37/21 | 5/4 | corpsman in 37/82 missions · revived 5 · BRACE 69/66 · margin +0.40±0.64 |
| R1b | 50/50 | **70 (-44%)** | 157 -> 60 (38%) | 22/27 | 1/6 | corpsman in 22/74 missions · revived 2 · BRACE 68/78 · margin -0.90±0.87 |
| R0h4 | 20/30 | 150 | — | — | — | margin +0.20±0.59 |
| R1h4 | 30/30 | **110 (-27%)** | 219 -> 81 (37%) | 23/35 | 6/4 | corpsman in 24 missions · revived 8 · BRACE 102/69 · margin -0.20±0.25 |

**h4 close leg:** completion 25% → 30% (+5); true-KIA -27% — just under the h0 band, exactly the
"downs concentrate where deaths do" prediction: h4 stages MORE downs (219 vs ~168/chunk at h0)
and bleeds more of them out under pressure (63% vs ~57%), so the save-rate compresses to 37%.
PATCH 10 at h4 (0.42/corpsman-fielded-mission — the revive stage strengthens where wounds do).

**Verdicts (pooled h0, 40 matches/side):**
1. **Soldier true-KIA 251 → 151 (-40%) — inside the 30-50% intent band.** The death ledger moved
   to the attrition ledger: 336 downs staged, 144 saved (43% save-rate: 7 revived, 89 recovered
   on won fields, the rest = downs still open when a loss ended the run), 192 bled out.
2. **Completion 40% → 47.5% (+7.5, inside the +10/-5 budget)** — a body saved IS the bot playing
   better, as the spec predicted (chunk split -5/+20; pooled is the binding read, the FUL-6
   variance lesson). Mission win-rate 88/80 → 88/86; paired margins statistically unchanged.
3. **STABILIZE 107 uses across the two chunks vs the spec's 3-8 guess** — the guess undercounted
   downs (~8 per campaign: every old KIA is now a down, plus the saved fight on). The verb is a
   live, first-class part of play, not quota-chased — the bot stabilizes exactly when adjacent.
4. **PATCH 8 → 16 (the >= 10 target met in aggregate), 0.27 uses per corpsman-fielded mission —
   and the corpsman was fielded in only 59/156 (38%) of missions.** The FUL-5 cap has flipped
   from "no stage" to "no presence": the stage now exists (7 revives measured); what caps PATCH
   is that the corpsman only enters via backfill. **Roster-presence verdict recorded for FUL-13's
   founding-squad question — do not quota-chase.**
5. **DRAG stayed ~1-2/batch** — the drag-chain carry is real for humans but the bot doesn't probe
   DRAG-toward-zone (v1 accepted gap). FDR consequently unmoved (0 procs); still FUL-13's row.

## Review fixes (adversarial integration review: SHIP-WITH-FIXES — all applied)
- **F1 (the one that mattered): a downed body won the ENEMY FOCUS pick.** PlanEnemySquad scored
  Hp 0 as the 60-pt near-dead base + exposure, so a visible downed body usually became
  EnemyFocus — and since Ai.Plan excludes the downed from candidates, every focus bonus
  (kill-press, crossfire pulls) then applied to NOBODY: the coordination layer silently
  switched off while a body was down. Fixed (downed skip beside the caged-captive skip) and
  **re-measured** (paired h0 N=10, slots 0-9, CRN worlds identical): completion 50% → **60%**
  (g70/s50 — +10 vs R0a, at the budget boundary; the pre-fix 45% was NOT propped up by the
  dumb enemy — with coordination restored the squad does BETTER, because enemy attention also
  stops leaking onto bodies), true-KIA 75 (**-40% vs R0a's 125** — the band holds), STABILIZE
  60, PATCH 5 (corpsman in 35/85 missions), mission win-rate 91%.
- **F2 (ledger honesty): a body killed WHILE down counted as "saved."** KillUnit now closes the
  down state on the corpse (after the cause read; ExpireDowned flags itself so a bleed-out
  isn't double-counted) and a new `finished` counter joins the report; save-rate =
  1 - (bled-out + finished)/downs. **Restatement: the published pooled 43% save-rate was
  generous** — AoE/fire finishes (uninstrumented then) counted as saves; the honest re-measured
  chunk reads **33%** (186 downs → 59 recovered + 2 revived, 101 bled out, 24 finished).
  Also kills the BLED-OUT-pops-on-a-burned-corpse corner (a dead body can't reach the tick).
- **F5:** two lesser enemy-attention seams stopped treating a body as a threat — FaceShields no
  longer turns an AEGIS shield toward a downed body (free flanks denied), and
  TryEnemyReposition's ExposureAt no longer counts a downed body's fire lanes.
- **F6:** EnterDowned sets WasNearDeath — a PATCH-revived soldier now earns the near-death scar
  track too (barracks recovery already forced it; the revive path didn't).
- **F3 (banner honesty):** "3 TURNS TO REACH THEM" promised three but the player acts on pills
  2 and 1 — reworded to "THEY HOLD FOR 3, TWO TURNS TO ACT" (tick unchanged).
- **F4 (pill honesty):** during the orphan tick (no soldier standing, stabilized timers run)
  the pill/chip now show the countdown — "STABLE 2" / "STABILIZED - FADING (2)" — never a
  lying steady STABLE. DOWNTEST leg e gained the F2 corpse-state pin.

## Gotchas (process)
- **EnterDowned clears Statuses INSIDE TickStatuses' enumeration** — a lethal burn tick threw
  Collection-was-modified (caught by escort autoplay). Fix: snapshot the list + stop ticking a
  unit that just went down (a second DoT in the same pass would kill the fresh body outright).
- **The scratchpad is shared across sessions** — a prior wave's `r1b.log` shadowed this wave's
  chunk file and nearly got read as data. Fresh, wave-unique log names + provenance checks
  (slot ranges + the SOLDIER DOWNS line only the new build prints) before trusting any log.
- **Key T was taken** (tag editor) — STABILIZE ships on E; both firing on IsKeyPressed(T) would
  have stabilized AND opened the editor in one press.

# PROGRAM FULCRUM — FUL-13 TRUE NORTH + the program close (2026-08-28, balance lead on wt-ful13)

The program's last wave: make the published numbers TRUE for the finished game, resolve every
carried watch item at proper N, and close the FULCRUM ledger. Binding input:
docs/plans/FUL-13-true-north-docket.md, executed top to bottom on base c4ef42e (all twelve
content waves). Method discipline: NOTHING was tuned until the reference existed; every lever
got its own measured round with a dip budget; every docket item ends this entry
resolved-with-data, tuned, or recorded-for-the-owner.

## The fresh baseline (200 campaigns, one build)
Intel cash-flow telemetry landed first (61fbccd — Stats.RecordIntel at every Run.Intel
mutation site; INTEL ECONOMY BY HEAT report table + intelByHeat JSON). Logic-identity vs base
per the FUL-1 precedent: BALANCE=2 same-slot JSON — all 33 base-schema fields identical (only
intelByHeat added); PAIRTEST PASS. Then the ladder: per heat TWO pinned chunks
(SIGHTLINE_BALANCE=10, SIGHTLINE_BALANCE_HEAT, BASE 0/10 = disjoint CRN slot sets 0-9/10-19),
greedy+sloppy paired = 40 campaigns/heat, 200 total, zero frame-caps, every chunk's JSON+log
archived.

## THE REFERENCE LADDER (the pre-tuning BASELINE — retire every earlier number)

> Review annotation: this table was measured BEFORE the R1/R2 Defend rounds below. On the
> SHIPPED tree the tuned rungs last read: h8 **5%** (at the 10±5 band floor), h6 **27.5%**,
> h4-b0 35% (single re-run chunk); h0/h2 are untouched by the rounds (byte-identical by
> construction). Quote THOSE for the shipped top rungs — quoting this baseline's h8=10 for
> the shipped game misstates it by 2x.
| heat | run completion | greedy | sloppy | paired gap | margin (missions) | avg cleared | mission win |
|---|---|---|---|---|---|---|---|
| 0 | **52.5% ±7.9** | 45% | 60% | −15 | −0.75 ±0.57 | 4.58 | 88.0% |
| 2 | **35.0% ±7.5** | 40% | 30% | +10 | −0.10 ±0.45 | 4.00 | 82.6% |
| 4 | **30.0% ±7.2** | 20% | 40% | −20 | −0.30 ±0.45 | 4.15 | 83.4% |
| 6 | **22.5% ±6.6** | 30% | 15% | +15 | +0.65 ±0.53 | 4.03 | 79.6% |
| 8 | **10.0% ±4.7** | 10% | 10% | 0 | +0.20 ±0.34 | 3.05 | 70.0% |

Chunk completions (g/s) — h0 70/50·20/70, h2 50/30·30/30, h4 30/50·10/30, h6 30/20·30/10,
h8 20/10·0/10: world-set variance still dominates chunk levels (the FUL-5 R7b lesson); the
pooled rows are the record. Funnel at scale: 79.2/0.0/20.8 (n=847 builds — FUL-9's 20-25
procedural target, stable).

## THE GOAL BAND — RE-SET (owner-facing)
The research-era band (80/70/60/40/20 ±8) was written for a game whose routes DODGED their own
hardest content: before FUL-9, Defend fielded n=4 missions per 20-run batch, mid-run Decapitate
never reached mids, ~47% of missions skipped the authored arenas, and a lethal hit was a
decision-free KIA. The finished game deals Defend-or-Rescue on an anchor column of EVERY route,
mid Decapitate on ~every route, pods of 3 with linked activation from m3, and prices every
death as a 3-turn rescue problem. FUL-9 measured the exposure alone at −10..−15 completion;
FUL-6's pods cost the marginal worlds more; FUL-7 gave back +7..+10. The old band cannot be
reached from here without un-repairing exposure (out of authority, and wrong) or inflating the
squad. The re-set band fits the finished game's design story — "stakes that bite" wants an h0
campaign where losses are common and deep (the tuned BOT clears 52.5% — human first attempts
sit below the tuned bot; losses average 4.58/6 missions, m1 95%); each paid rung takes a real
visible bite (−15/−10/−10/−10); the apex stays
beatable-not-farmable. **Published band: h0 55 / h2 40 / h4 30 / h6 20 / h8 10, ±8 (h8 ±5,
hard floor >=5).** Every fresh rung measures inside it. The docket's "h4 lift if low" resolves
against the re-set band: 30.0 vs 30±8 is ON target — no rung-average lever was spent; the
wave's levers went to the one measured SHAPE defect instead (below).

## PER-OBJECTIVE x HEAT (baseline; win% (n))
| objective | h0 | h2 | h4 | h6 | h8 |
|---|---|---|---|---|---|
| Eliminate | 95 (42) | 95 (42) | 95 (42) | 93 (42) | 90 (42) |
| Defend | 82 (34) | 65 (31) | 72 (32) | **97 (30)** | **91 (23)** |
| Decapitate | 70 (33) | 75 (24) | 61 (28) | 46 (24) | 36 (11) |
| Escort | 100 (17) | 81 (16) | 100 (16) | 67 (21) | **29 (17)** |
| Hack | 92 (12) | 83 (12) | 85 (13) | 90 (10) | 64 (11) |
| Sabotage | 100 (10) | 90 (10) | 90 (10) | 80 (10) | 75 (8) |
| Evac | 100 (4) | 100 (5) | 100 (4) | 40 (5) | 50 (4) |
| Rescue | 100 (6) | 100 (4) | 100 (6) | 100 (5) | 25 (4) |

Shape findings: (1) **Defend INVERTED at the top** — the tuning rounds below. (2) Escort 29%
at h8 is the apex's killer cell (+4 stat/+1 dmg vs a fragile asset) — recorded, not tuned:
a wall rung is allowed a hardest cell and it still fields winners; seeded to open/next for
the owner.

## TUNING ROUNDS — the Defend top-rung inversion (one lever per round)
The mechanism, located by the pinned batches: Defend's difficulty comes from the opener + the
waves; waves were heat-BLIND (MakeWaveHostile bump = mission only) AND FUL-4's flat opener
trim (count−3) silently ATE the heat ladder's EnemyDelta (+2..+4 bodies) — with the timer
bounding total exposure, Defend became the top rungs' free square: defend-pinned h8 read 96%
(n=89) with 80% ALL-DEFEND run completion at a rung whose real completion is 10%.

| round | lever | pinned h8 Defend | unpinned h8 rung | unpinned h6 rung | h4 spot (b0) | verdict |
|---|---|---|---|---|---|---|
| R0 | baseline | 96% (n=89) | 10.0% | 22.5% | 40% | the inversion, measured |
| R1 | waves inherit Heat.StatDelta (7139a2f) | 95% (n=86) | 10% (b 10/10) | 25% (30/20) | 45% | truthful, not binding — a set line shreds 2-4 wave bodies regardless; KEPT (heat now reaches wave stats) |
| R2 | defendKeep: opener keeps half the GRACED heat bodies (03f02dc) | **87% (n=76)** | 5% (5/5) | 27.5% (35/20) | 35% | the binding lever — h8 parity with h0 (87 vs 83 pinned); h6 cell still 97 (keep floored to 1) |
| R3 | PROBE: keep rounded UP (h6 keep 1→2; h4/h8 m3+ keeps unchanged) | pinned h6 97% (n=89) — UNMOVED | (unchanged by construction) | 22.5% (35/10) | 25% (Defend 50, n=18) | **REVERTED** — zero movement at its target and a −15 h4 spot cost (the m2-ripple): at h6 extra bodies FEED the rout economy instead of pressuring the hold; it is h8's +4 stats that bite |

Shipped state = R1+R2 (R3 reverted). Dip budgets (≤8/rung vs baseline; h8 floor ≥5):
h8 10→5 (−5, in budget but AT the 10±5 band's floor — reported), h6 22.5→27.5 (+5),
h4 b0 40→35 (−5 chunk-level). NO breaches. Defend cells shipped: pinned h0 83 (n=77,
byte-identical through the rounds — StatDelta(0)=0 and keep=0 at h0 by construction),
pinned h8 **87** (was 96 — parity with h0, the inversion closed), h4 **61** (n=18, FUL-4's
60-80 band). RESIDUAL, recorded: the h6 Defend cell stays soft (97 pinned n=89 / 100-93
unpinned n=30) — two structural levers plus the R3 probe did not move it; its mechanism
(rout-economy body absorption at +2 stats) is named in the Game.cs comment and the cell is
seeded to open/next rather than chased with blunter levers. Closing test MET at the apex:
Defend no longer rises with heat (82 h0 / 87 h8 pinned-parity); h6 is the one recorded bump.

## WATCH-ITEM DISPOSITIONS (docket §2 — one line each)
- **LOS policy-gap flip**: RESOLVED-WITH-DATA — zero at N=100 pairs (binary −2.0; margin
  −0.06 ±0.21; sign test p=0.87 on 36 discordant). ACCEPT forgiving-by-design; sloppy
  definition unchanged. Why accept: the sloppy model's errors are bounded-rational by
  construction (15% shot-skip, mediocre-tile within a −40 score band, 10% overextend) —
  exactly the mistakes the comeback economy (bleed-out saves, BRACE denial, morale routs,
  assist) was BUILT to absorb; a +15 gap would re-open the punish-gap failure UNDERTOW
  closed, and sharpening the error model to manufacture one would measure a worse bot, not
  a better game. Per-heat gap rows (±15-20 at n=20/leg) are retired as signals; the pooled
  margin is the metric of record. The FUL-2 −20 was n=10 noise, as suspected.
- **Defend m5 all-Defend 12.5% (n=8)**: RESOLVED — 89% (n=9) defend-pinned h0; noise.
- **Per-kit finale drift**: CLOSED world-driven — paired slots 0-9: Wardens 81 / Legion 69 /
  Syndicate 94 (n=16 each), an ordering FLIP vs FUL-11's pooled L>W>S on overlapping method —
  worlds, not kits; unpinned ladder m6 h0 78% (n=27) corroborates band health. No kit tune.
- **Defend softness at top rungs**: RESOLVED as the INVERSE — see the tuning rounds.
- **Escort-reach floor early-death-sensitivity**: CONFIRMED at scale — Defend-on-route
  degrades with heat exactly as run depth does (h0 90/70% → h8 50/50% per chunk; avg cleared
  4.58→3.05). Structural guarantee intact; played reach tracks survival. Recorded.
- **HoldOverwatch rusher arm vs wide watch**: RESOLVED no-dominance — at ladder scale
  OVERWATCH 101-443 / FOCUS 145-196 / BRACE 171-303 per heat batch (totals 1360/843/1148);
  the wide watch keeps real volume everywhere; the FUL-5 h0-hold collapse was one stage, not
  the economy. Reaction pricing left alone.
- **RCL retire-or-rework**: DECIDED keep-as-is — 4 procs / 27 picks / 200 campaigns; the
  cone-kill proc is a deliberate combo line the bot rarely stages (FOCUS 843 shows the verb
  is alive) — the bot floor understates the human line (the FUL-6 grenade lesson). Sweeten
  path ("any overwatch kill re-arms, once/turn") seeded to open/next for an owner call.
- **FDR honest zero**: CLOSED alive — 11 procs / 37 picks at ladder scale; FUL-7's
  downed-drag stage priced it exactly as the FUL-5 brief predicted.
- **PATCH roster-presence**: corpsman fielded 36-42% of missions h0-h6 but **13% at h8**
  (RELENTLESS kills the backfill lane); PATCH 5-26/batch, revives 2-16/batch. Verdict to
  the owner docket (§4 below) + the h8 blackout seeded to open/next.

## INTEL ECONOMY (the flood question — RESOLVED, no drain)
| heat | earned/run | heat-bonus | hb share | spent | unspent |
|---|---|---|---|---|---|
| 0 | 129.6 | 0.0 | 0% | 115.4 | 14.2 |
| 2 | 140.1 | 21.0 | 15% | 126.3 | 13.8 |
| 4 | 187.0 | 57.0 | 31% | 172.1 | 14.9 |
| 6 | 217.2 | 97.2 | 45% | 201.6 | 15.6 |
| 8 | 197.6 | 112.0 | 57% | 178.4 | 19.2 |
Heat's accelerating kicker reaches 57% of income at the apex and ALL of it converts to shop
spend: unspent holds flat (14-19) at every rung and the ladder keeps its full slope. The
per-barracks slate is capacity-bounded, so heat pays POWER, not bank — the carrot works as
Heat.IntelBonus intends and the difficulty survives it. No drain shipped.

## ECONOMY FIXES (FUL-10 review leads; one measured round, 03f02dc)
**EventCatalog.FireWeight** — a ChancePct outcome now prices at the probability it FIRES
(success partner ×p, OnFail partner ×(1−p); GambleIntel's EV formula and GrantScar's
self-scale exempt), so warchest arm0 values 1.54 (its true EV) instead of 2.25 ("both
fire"). **EventCatalog.PrepDead** — a GrantPrep arm with no telegraphed faction is now
ILLEGAL (Game.ChoiceLegal): the HUD greys informant's 12-intel dossier and the bot never
spends into a report line; one rule shared by the gate and its test. EVENTTEST grew legs
3c (5 FireWeight pins + 3 PrepDead legs) — PASS; PAIRTEST/SAVETEST PASS. Budget A/B
(h0 BALANCE=10 slots 0-9 vs the baseline chunk — the lever stack is h0-inert by
construction, so this isolates the events change): **BYTE-IDENTICAL** — outcomes AND arm
picks unchanged in these 20 worlds (warchest's flat arm already out-priced the gamble, so
no bot pick flips on the current catalog; the FUL-5 lesson — a byte-identical A/B is the
finding). The fix binds on future arms where a gamble could out-price a flat arm, and
PrepDead protects HUMAN players today.
- **reservecall release-arm prior**: AUDITED, record-only — the arm never fired in 200
  campaigns (BY EVENT-CHOICE has nothing to distort); the prior's arithmetic (−6 release +
  4.5 salvage + 3 intel = +1.5 vs −4 keep) is coherent but rank-blind (a rank-4 release
  prices like rank-1); with zero fielded exposure no change is warranted — noted for any
  future event-exposure wave.

## "?"-NODE EXPOSURE LEVER (docket §3 — measured, recorded, NOT applied)
The parked Clamp(mids/4,1,3) was measured in a scratch tree (git-archive + patch, same
build chain) against the ladder's h0 chunks on the same slots: **18 of 20 slot-pairs came
back byte-identical** — only 2 maps in 20 have >=12 mid nodes, so the widened ceiling stamps
a third "?" node on ~10% of maps — and fielded event volume did NOT rise (27/27 and 17/17
resolutions, identical id sets). RECOMMENDATION: do not ship it — near-zero exposure
benefit for a real cost (GenerateMap re-runs on load, so the clamp reshapes in-flight
saves' unvisited nodes). The honest exposure levers, if the owner wants event variety, are
floor-2 stamping (Clamp(mids/4,2,3) — same save skew, adjudicate it) or a cross-run catalog
dedupe (profile-side, no skew). At 17 catalog entries over 1-2 nodes/run, each event is a
~1-in-9-run sight: informant and reservecall fielded ZERO times in 200 campaigns.

## DOWN LEDGER AT SCALE (FUL-7 under the ladder)
| heat | downs | revived | recovered | bled out | finished | save |
|---|---|---|---|---|---|---|
| 0 | 363 | 4 | 104 | 212 | 43 | 30% |
| 2 | 370 | 11 | 71 | 232 | 51 | 24% |
| 4 | 413 | 12 | 85 | 239 | 65 | 26% |
| 6 | 448 | 16 | 88 | 284 | 51 | 25% |
| 8 | 289 | 2 | 42 | 199 | 23 | 23% |
Save-rate compresses 30→23% up the ladder ("downs concentrate where deaths do" — FUL-7's h4
prediction, measured to the apex).

## ENDLESS DEPTH (final tree)
32 stands (2x BALANCE_ENDLESS=8, BASE 0/8, default heat cycle): pooled median 6, means
6.19/6.44, p90 8, ZERO cap hits — the APEX 5-6 band's top edge, unchanged from FUL-6's
6/6.25. A stable identity; recorded, no tune.

## BOON PROCS AT SCALE (200 campaigns)
FST 384 · TRR 104 · SHK 44 · FDR 11 (37 picks) · PYR 11 (41) · RCL 4 (27). ITEM 303 uses
(smoke 295), GRENADE 59, STABILIZE 552, PATCH 84, DRAG 63 — the verb layer FUL-5 opened is
alive at every rung.

## DESIGN-QUESTION DOCKET (§4 — recorded with recommendations, owner decides)
**Skirmish/daily opener grace.** Skirmish and Daily are single missions, and every mission-1
fight takes the early-mission heat grace (SetupMission zeroes heatEnemy/heatStat/heatDmg at
n<=1), so a "heat 8" skirmish fields NO numeric heat delta — only the qualitative flags
(TighterContact/EXPOSED/HarshAttrition) and the ungraced coordination tier bite. The picker
desc has been honest since FUL-3; the open question is intent. RECOMMENDATION: exempt
SKIRMISH from the numeric grace — the grace protects a green campaign opener that never
chose its heat, while a skirmish player explicitly dialed the rung and is owed the wall
(one single-mission flag at SetupMission's grace line). Keep DAILY as-is: the day's board
is a shared dated comparison and mid-stream re-tuning breaks best-score comparability.
Not applied — a difficulty promise in a mode picker is owner-visible.

**Grenade pre-frag (human-vs-bot read gap).** FUL-6's verdict stands at ladder scale
(GRENADE 59/200 campaigns, 7-14 per heat batch): the bot's frag window (covered 2+ ACTIVE
cluster, pre-shot) anti-correlates with real geometry — dormant pods clump (the human stage
is visible pre-fight) but woken pods scatter before the gate re-fires; SmartGrenade has no
Active filter, so the low count is emergent, not a coded decline. RECOMMENDATION: accept
the gap as designed skill expression — the measurement contract records the bot floor, and
the human pre-frag line sits above it; teaching the bot a conservative pre-frag arm would
price a perfect-info line the bot's honesty contract avoids.

**Founding-squad corpsman (FUL-7's verdict consumed).** PATCH's cap is roster PRESENCE, not
gates: corpsman fielded in 36-42% of missions (13% at h8 — RELENTLESS kills backfill);
per-presence PATCH is healthy (revives 2-16/batch). OPTIONS: (a) swap the founding GUNNER
or RANGER for a CORPSMAN — raises PATCH and the save-rate but deletes a damage seat from
the teaching squad; (b) guarantee a corpsman in the first backfill offer — presence without
touching the founding four; (c) keep as-is — the class stays a mid-run acquisition and the
early game stays lethal-feeling. The bleed-out economy works without one (STABILIZE is
universal, 552 uses at ladder scale); a founding corpsman mainly buys REVIVES.
RECOMMENDATION: (b) or (c) — this is a founding-four identity choice, not a tune.

**NO QUARTER heat-picker Desc** (FUL-3): verified unregressed on the close tree (Run.cs
Mods[7] still admits the m1-2 grace: "+1 dmg from mission 3").

═══════════════════════════════════════════════════════════════════════════════
## PROGRAM FULCRUM — THE CLOSE (13 waves, all landed)
═══════════════════════════════════════════════════════════════════════════════

Through-line: **systems that existed but never reached play** — found by six research lenses,
built across 13 waves on parallel worktrees, measured at every landing, closed with this
wave's proper-N re-baseline.

**Per-wave (hash · the measured headline):**
- **FUL-1 COMPASS TRUTH** (a4ef1dd): per-slot CRN pair records + all-pairs PAIRED MARGIN,
  PROCS at effect sites, arena funnel, BY EVENT-CHOICE — the compass that priced everything
  after it. First readings: SHK/FDR/RCL procs 0/0/0 (the program's thesis, measured).
- **FUL-2 SEAM INTEGRITY** (4690748): assist-cache staleness, INTRO save-clobber,
  codex-from-pause shot leak, EXTRACT reactions, supercover LOS made real (+6 COMBATTEST
  legs). LOS budget A/B: completion 65→60 (boundary, in budget); its −20 gap read at n=10
  became this wave's N=100 zero.
- **FUL-3 CHROME** (16e24e9): roster-chip reflow, per-button dim, INTEL row-clamp, dormant
  de-emphasis, LOCK-ON/NO QUARTER desc truth.
- **FUL-4 HOLDFAST** (wt-ful4 → 021a84b): Defend 38% h0 → 66-73% (bands HIT), SmartDefend
  co-fix first, wave telegraph on the spawner's own read.
- **FUL-5 HANDS** (wt-ful5, R2-R7 596c913..b8ceb1a; integrated
  7d31c8c, review e3bcb3b): the EV bot learned the verbs — BRACE 1→82, ITEM 0→19, PATCH
  gates opened, hashed 70/30 event chooser, mod priors de-flattened (SUP 27→9%). h0 60→75
  same-slot; two byte-identical probe batches became the "gate unreachable" method.
- **FUL-6 CRITICAL MASS** (wt-ful6 7a41332..1b2ff29; integrated 72d8657, review c74378e):
  pods of 3 + cohesion + linked activation ("HEARD THE GUNS"), endless wave sub-pods, FDR
  rework, escalation lever 1 (dip −12.5 breach → 0). TRR 18-31; RCL off zero.
- **FUL-7 LAST LIGHT** (wt-ful7 1cc60bf..8cfad71, review 7c04213 → c4ef42e): the 3-turn
  bleed-out at the KillUnit seam — true-KIA −40%, honest 33% save-rate (review F2 restated
  it down from 43%), STABILIZE a first-class verb, review F1 restored enemy focus while a
  body is down (completion 50→60 re-measured).
- **FUL-8 PIKEMAN** (wt-ful8 d5c20f2+c866aef; integrated 3001197, review 52489e3): the
  SARISSA lane-holder — the roster's first piece contesting MOVEMENT, the player's own
  BRACE mirrored back (zero new combat machinery); route-tax gate: Escort 5.9→5.6t at n≈90.
- **FUL-9 THE DECK** (wt-ful9 2650c5b + 72f9a62 + 1359309): the carried W7 spec BUILT —
  column-constrained objective plan (Defend-or-Rescue anchor on EVERY route, <=1 Escort),
  no-repeat arena deck, EXPOSURETEST enumerates all 1098 routes. The honest price: h0 60→
  50/45 — this wave's re-baseline consumed it.
- **FUL-10 FORKS** (wt-ful10 b71b821 + 78fa859; integrated af61314, review a26cfe4): seven
  trade-off events, MRC/LGD veteran contracts, orphaned-perk trio into class lines; ladder
  A/B +5 (boundary, in budget).
- **FUL-11 CEREMONY** (959f5b2; survived a 3-week container suspension mid-measurement):
  finale ceremony + Wardens SIGNIFER/ORDERLY retinue — Wardens 73→83.7, all kits in 78-88,
  pooled 84.2 (n=146), zero count/stat tuning.
- **FUL-12 SIGNPOSTS** (wt-ful12 through 677a6e7; merged 0605e6e): end-card SALVAGE/HEAT/achievement slabs, tutorial step 0
  + BRACE tip, pill hovers, dormant ID cards, 16-boon RECOMMENDED draft, WAR ROOM sizing.
- **FUL-13 TRUE NORTH** (this entry, wt-ful13): the reference ladder + re-set band, the
  Defend inversion levers, the gap thread closed, the intel verdict, the economy fixes,
  the owner docket.

**Process learnings (the program's, for the next program):**
1. **The environment is the adversary** — the arc held from m1 (four container suspensions,
   three limit windows, two scratchpad wipes) to the close (this wave's only incident: a
   monitor timeout). The countermeasures are now house style: the plan of record lives
   in-repo; dev agents push per commit; scratch trees come from `git archive`, never cp -r;
   liveness is judged by artifact mtimes; wave-unique log names + provenance checks before
   trusting any batch file.
2. **Integrate-then-review**: FUL-5/6/8/10 landed on parallel worktrees, were integrated
   onto the moving tip as CANDIDATES, and the adversarial review ran AT the integration
   (a26cfe4/52489e3/e3bcb3b/c74378e/7c04213) — review the composed tree, not the branch.
   Every review found something (F1's enemy-focus leak was worth +10 completion).
3. **Byte-identical batches are instrumentation**: FUL-5 R1/R2 located unreachable gates;
   FUL-6 R3/R4 proved lever scope; FUL-13's qnode A/B (18/20 pairs identical) killed a
   parked lever with two chunks. CRN discipline turns "nothing changed" into a finding.
4. **One lever per measured round, fresh same-slot R0 first, dip budgets, breaches
   reported** — the FUL-4/6/13 tuning pattern; and a lever that doesn't bind (FUL-13 R1)
   stays if it's TRUE (heat now reaches wave stats) with the verdict written.
5. **Pinned batches locate mechanisms; unpinned batches price them.** The Defend inversion
   was invisible in rung averages, obvious in the per-objective table, and its mechanism
   (the flat trim eating EnemyDelta) only fell out of the defend-PINNED 96%-at-h8 read.
6. **Docs over-claims die at the tree** (hook-exists + commit-exists before docs commits) —
   held from the W7/FUL-9 lesson through this close.

**Verified at the close** (the wt-ful13 close tree): Release 0/0; the FULL suite battery — every
SIGHTLINE_*TEST hook in src (40 hooks, the authoritative grep) — PASS; autoplay ×5 clean
(no exceptions, no TIMEOUT); PAIRTEST green after every code round; README screenshot
retaken (the FULCRUM board: pod clumps, WAVERING pills, full verb bar — the old frame
predated pods/morale/downs).

**Open/next**: seeded in ROADMAP §OPEN/NEXT (post-FULCRUM) — the owner-decision docket
(skirmish heat / founding corpsman / grenade pre-frag), Escort-at-apex, the h6 Defend
residual, event-exposure levers, RCL sweeten option, h8 corpsman blackout, on-device audio.

---

## PROGRAM RESONANCE — WAVE A1 "THE EAR" (audio measurement + the mastering pass)

**Premise**: nobody has ever *heard* SIGHTLINE. The sandbox has no audio device, the owner
has never tuned the layer, and every audio decision since Phase 3 was made blind. A1 builds
the instrument first, proves it detects the known defects, and only then touches the mix.

**Two new device-free, windowless hooks** (`src/Audio.Analysis.cs`, `Audio` is now `partial`):

| Hook | What it does |
|---|---|
| `SIGHTLINE_AUDIODUMP=1` | renders 23 SFX cues + both music beds to `audio_dump/*.wav` and prints per-cue dur/peak/rms/crest/dc/clip/spectral-centroid/4-band split/tail/zcr, a loop-seam block, and a SUM-STACK block that mixes the realistic concurrent combinations at the real `MasterVol` |
| `SIGHTLINE_AUDIOGATE=1` | the same measurements as a committed budget: per-check lines + `AUDIOGATE: PASS\|FAIL` |

`scripts/audio-report.py` (sandbox-only; **the game has no Python dependency**) turns the dump
into a spectrogram contact sheet. `scripts/dev-setup.sh` installs numpy/matplotlib/soundfile
best-effort and never fails setup if it can't.

**The gate was red on arrival — 9 of 12 checks.** That was the point: a gate that passes on
day one measures nothing. Measured before → after:

| | before | after |
|---|---|---|
| RMS spread across 23 cues | **16.2 dB** (`move` -30.3 → `st_squadwipe` -14.0) | **10.8 dB** (`move` -30.8 → `crit` -20.0) |
| crit vs hit separation | **2.0 dB** | **5.1 dB** |
| worst cue peak | `crit` **-0.0 dBFS**, 1 clipped sample | `crit` -4.0, zero clipped |
| worst |DC| | `hit` **+0.00303** | ~0 (every cue) |
| kill-shot stack `w_lmg+crit+death+st_kill` | **+1.8 dBFS, 8 clipped** | **-1.1 dBFS, 0 clipped** |
| music energy above 1 kHz | **0.000% / 0.000%** | **6.00% / 6.46%** |

**What changed in the mix.** `Reg(id, dur, targetDb, fill)` now carries a per-cue **peak
target in dBFS**, and the old `g = peak > 1 ? 1/peak : 1` line — a clip guard that fired for
exactly one cue — is an **unconditional normalise-to-target**. The target column IS the mix
(crit -4, weapons -5..-6.5, stingers -6..-10, UI -11..-15.5, `move` -14.5). A 20 Hz one-pole
DC blocker runs before normalisation. `death` got a real 200 ms decaying tail instead of a
hard cut at 320 ms into 40 ms of dead air; `st_lose`'s chord no longer ends on the last
sample of its own buffer.

**Two defects fixed in passing.** (1) `Audio.Init()` runs *before* `new Game()` and `Noise()`/
`Click()` drew from the shared `Util.Rng` — on a real device every synthesised cue perturbed
the gameplay stream, and the draw count *changed* when a drop-in asset file was present. Audio
now owns a private stream reseeded per cue (FNV-1a of the id, since `String.GetHashCode` is
per-process randomised), which also makes the WAV dumps byte-reproducible. (2) `ValidateBuffer`
now implements the in-range check its caller's doc comment already claimed.

**Gotcha worth keeping.** `BuildBuffer` allocates `(int)(dur*SR) + 8` samples, so the last 8
are *always* zero — any "does the tail reach silence" test that reads `buf[^1]` passes
vacuously. The gate samples the tail at index `(int)(dur*SR)-1`.

**Metric correction (documented in the gate's rationale block).** A literal
`|b[0]-b[n-1]| <= 0.005` loop-seam test is wrong: a provably seamless bed (integer Hz over an
integer-second buffer — the design already in use) still steps one sample across the wrap, and
that step scales with the top frequency present, so it is in direct tension with the brightness
floor. The pre-A1 beds measured 0.023 / 0.009 on it while being click-free. The gate's primary
seam check is scale-free — the wrap step against the **largest step the waveform takes anywhere
inside the loop** (+5%) — with `|1st-diff delta| <= 0.005` kept as a secondary absolute bound.

**Could not reproduce**: the reported `st_lose` tail of -46.1 dBFS. Measured -91.6 dBFS
(the loudest last-real-sample in the game, but 30 dB under the gate ceiling). It was still
fixed on structural grounds — a cue whose sound ends on the final sample of its own buffer has
zero decay margin.

**Verified**: Release 0/0; AUDIOTEST / AUDIOGATE / SAVETEST / COMBATTEST PASS; PAIRTEST PASS
(the CRN identity gate — the valid byte-identity check); autoplay ×2 clean; WAV dumps
byte-identical across runs. Tests run under `XDG_CONFIG_HOME=$PWD/.xdg` +
`SIGHTLINE_BALANCE_JSON=$PWD/balance.json` (parallel-agent isolation).

**Left for a later wave**: nothing here has been heard on hardware — the budget is a
*measurement* contract, not a taste judgement, and the owner still needs to audition it and
move the target column. `st_victory` has the same "chord release lands in the last 8% of its
own tone" shape as `st_lose` did (tail -148.9 dBFS, well inside budget) and was deliberately
left alone rather than re-voiced blind. No perceptual weighting (LUFS/ITU-R BS.1770) — the
budget is in dBFS RMS, which under-weights the low-heavy cues; a real loudness model is the
obvious next instrument.
## PROGRAM RESONANCE — WAVE T2: "READ THE DANGER" (incoming-fire forecast)

**The finding.** SIGHTLINE's information model was excellent on offence and absent on defence.
Everything about *the shot you take* was surfaced (banded odds, graze floor, streak-breaker,
FLANKED badges, combat log, status pills, role rings, enemy-ID hovers). Everything about *the
fire you stand in* was a single boolean: `Game.ComputeThreat` marked a reachable tile only when
some active enemy had LoS **and** the tile's cover level was exactly 0, and `Renderer.DrawThreat`
drew one small identical red tick. So a tile covered from one gun but enfiladed by four others
read **completely clean**, and the player could not see how many guns bore on a tile, how hard
they hit, whether they would be flanked there, or which archetype would do it. Enemy intent was
telegraphed only ~0.5 s before the unit acted, during the enemy turn — drama, zero planning value.

**What shipped.**

* **`ThreatCell` (src/Game.cs)** — the `bool[,]` became a per-tile struct grid: `Guns` (how many
  live/active/armed hostiles can actually shoot you there), `BestHit`, `ExpDmg` (post-armor
  expected damage if every bearing gun fires once), `Flanked`, `Exposed` (the pre-T2 bool, kept),
  `Watched` (a live overwatch / braced PIKEMAN reaction lane), `WorstCls`, and a `Tier` 0-3.
* **Truthfulness by construction.** Every number comes from `Combat.ComputeOdds` with the mover
  TEMPORARILY placed on the candidate tile — the exact call `Resolve` would make — gated by the
  same range + commanding-LoS test as `Game.CanTarget`. The forecast also models the two defensive
  states that *moving* clears (`Hunkered` drops, `MovedAfterFire` sets), so a tile you must walk to
  is not priced as if you were still dug in. The mover's real X/Y/Hunkered/MovedAfterFire are
  restored in a `finally` and asserted untouched by the self-test.
* **A signature cache.** `ComputeThreat` runs off `RecomputeMoveCost`, which fires EVERY frame of
  the player turn. A 64-bit signature over selection, both rosters' positions/state, the mutable
  terrain layers and the pref means a real change always misses and nothing else ever does.
* **Graded danger meter (src/Renderer.cs `DrawThreat`).** 1-3 bottom-aligned bars of rising height
  in the tile's top-right corner, read like signal strength. **Count** = guns (shape-redundant, so
  it survives greyscale and `SIGHTLINE_CB=1`); **intensity** = `BestHit` heat, so a tile in cover
  from two distant rifles sits at the floor and a tile three flankers can hit at 90% burns. A FLANK
  adds a foot-rule the bars stand on. Static alpha — no `Raylib.GetTime` pulse (standing terrain
  information, not an alarm; also one fewer clock read in the renderer).
* **Noise floor.** The overlay skips the ambient case (one covered gun, `BestHit < 50`). It still
  draws on exactly the pre-T2 trigger (a gun with a clean shot), plus 2+ guns and good covered
  shots. The hover card reports the suppressed case in full, so nothing is hidden from a player
  who asks — it is only kept off the board.
* **Hover card (src/Hud.cs `DrawThreatCard`).** "INCOMING FIRE · 4 hostiles bear · best 95% ·
  ~12 dmg", plus `worst gun: REAVER — BERSERKER`, a FLANKED line when the tile is a flank, and an
  OVERWATCH LANE line. It says NOTHING on a clean tile (the absent meter already says that) and
  does not print the modal EXPOSED state — a panel that pops on all ~130 reachable tiles with a
  line every player reads on every tile is chrome, not information.
  Echoes the board's meter glyph on its title row (vocabulary learned without a legend). Yields to
  the two cards that already own the hover (shot tooltip, enemy-ID card) and only ever speaks about
  an empty reachable tile.
* **Danger-tinted path preview.** The connecting line takes the WORST tier along the route (a safe
  destination reached through a crossfire is no longer free); each step node is drawn in ITS OWN
  tier and hot steps become the meter's triangle glyph, so the exact hot stretch reads without hue.
* **Three-state pref.** The pause toggle now cycles OFF / SIMPLE / FULL. SIMPLE restores the
  pre-T2 minimal read for players who want the quiet board back: one small pulsing tick on any
  tile where a gun has a clean shot, same glyph, same alpha, same pulse. Not bit-for-bit identical
  to pre-T2 — the trigger is now `ComputeOdds`'s `CoverLevel == 0`, so it also respects high-ground
  see-over, DRONE cover-ignoring and SHIELD arcs, which the old raw `GetCover` test missed.

**Verification.** `dotnet build -c Release` 0/0. New `SIGHTLINE_THREATTEST=1` (10 assertion
groups: gun count; BestHit/WorstCls/ExpDmg pinned against a hand-recomputed `ComputeOdds` pass;
the pre-T2 blind spot — a tile in cover from EVERY bearing gun still reports `Guns==2`; cover
level + flank ANGLE; out-of-range / no-LoS / dormant / dry / dead exclusion, each proven to bite;
overwatch + focused-cone lanes; unreachable-tile skip; caged captive; mover non-mutation; the
post-move HUNKER model; cache hit/miss) → **PASS**. `COMBATTEST / SAVETEST / AITEST / OWTEST /
ITEMTEST` PASS. `PAIRTEST` **PASS** (both legs MATCH). Autoplay x5 clean, no exceptions, no
TIMEOUT. `SIGHTLINE_BALANCE=10` on branch vs merge-base 2dec210 — `runs=20 missions=81` on both (asserted,
not a zero-data batch), and a full diff of the two reports shows **only wall-clock timings and the
output path** differing. Every measured statistic — per-heat win rates, per-objective tables, perk/
purchase/proc/event telemetry, decision richness, policy gap — is byte-identical.

**Perf.** Measured, not estimated. Real board (mission 2, 135 reachable tiles, 6 armed hostiles):
**1.06 ms per rebuild**, and rebuilds now happen only on a real change instead of 60x/s. Smaller
real selections measure 0.28-0.53 ms (52-86 reachable tiles, 3-6 guns). Synthetic worst case
(all 198 tiles reachable x 8 guns): **2.0 ms**. Against the tech-lead's baseline (total game logic
0.007 ms median / 0.34 ms p99 per frame, `RecomputeMoveCost` 0.106-0.242 ms EVERY frame), the
cached forecast costs less per second than the uncached pre-T2 bool grid did.

**Honesty about measurement.** This is a **read-side-only** change: no combat constant, no AI
weight, no spawn table moved. It should make the game easier for a *human*, but the balance bot's
policy does not consult the forecast, so **the flywheel cannot see the improvement** — and a
byte-identical `SIGHTLINE_BALANCE` batch versus base is the correct expected result and the proof
that gameplay was not disturbed. No win-rate claim is made or implied for this wave.

**New harness hooks.** `SIGHTLINE_THREATTEST=1` (assertions + perf), `SIGHTLINE_THREATSHOT=1`
(stages a fight, parks the cursor on the hottest reachable tile, prints `HARNESS THREATPERF`),
`SIGHTLINE_THREATPREF=0|1|2` (pin off/simple/full for A/B captures), and `SIGHTLINE_SEED=<n>`
(pin `Util.Rng` so two harness runs stage the same arena — note that frames are still NOT
byte-identical: the renderer reads the wall clock in ~50 places; CLAUDE.md's byte-identical
screenshot claim is false and was verified false on this tree).

**Own squint verdict (both palettes inspected).** The card and the tinted path are unambiguous
wins — the pre-T2 capture shows a benign gold path running straight through four fields of fire.
The meter field is roughly as DENSE as the pre-T2 tick field (on an open arena with six alerted
hostiles nearly every reachable tile already carried a tick), but each mark now carries a count
and a heat instead of being identical, and the calm corners of the board are visibly calm. So:
not busier than before, materially more informative. The first draft (1-3 stacked triangles,
count-driven alpha) DID read as uniform speckle and was rejected on my own capture — the meter
glyph and the heat channel are the fix.

**Deliberately left for a later wave.** (a) No aggregated "danger heat-map" wash — the per-tile
meter is the read; a full-board gradient is a bigger information-design decision. (b) Enemy INTENT
is still telegraphed only during the enemy beat; a player-turn "who is likely to shoot whom"
forecast is a separate wave. (c) The forecast covers direct fire only — grenades, SIEGE zones and
board fire keep their existing dedicated overlays and are not folded into `ExpDmg`. (d) Crits and
the graze floor are not modelled in `ExpDmg` (first-order hit% x post-armor average, labelled
"expected"). (e) The bot still uses its own `TileExposure` weighting; unifying it with `ThreatCell`
would change bot policy and therefore the ladder, so it was left out of a read-side wave.
## PROGRAM RESONANCE — WAVE V1 "GROUND AND TYPE" (visual foundation)

**Goal.** The two cheapest, highest-finish visual defects in the tree: the board didn't have a
floor, and the type didn't have an atlas. Nothing gameplay-coupled — the whole wave is
presentation plus one genuine ship-blocker.

### A — "give the board a floor"

`Renderer.DrawBoard` painted the biome floor and its noise grain only on `TileType.Floor` tiles.
Under every cover block sat bare board backing (`Pal.RGBA(7,10,14)`), and since the block is
drawn with `inset = 5f`, that left a **5px hard-black gutter around all ~45 cover blocks on
every map**. Squint at a pre-fix frame and the loudest thing on the board is a grid of black
holes, not the squad. Both `continue`s are gone; the ground plane is continuous and cover sits
on it.

The "drop shadow" under cover was a fixed `+3,+4` offset — the same in every direction, i.e. an
emboss. `Renderer.LightOrigin` (board-fraction `0.28, 0.10`) and `FloorLight` had declared a key
light since UNDERTOW W7 that **nothing cast from**. New `ShadowVec(g,x,y,len)` returns the
on-screen fall direction for a tile — away from the light, with a `+0.34` downward bias so a
block at the light's own foot still drops a short shadow (the key is elevated, not on the deck)
— scaled `Clamp(0.42 + dist*0.85, 0.42, 1.30)` so grazing corners throw longer. `CastShadow`
sweeps the footprint along that vector in 5 overlapping steps, so the pool is darkest at contact
and feathers to the tip. Length `high ? 16 : 8`. Same treatment on the plateau front-wall
contact shadow (`DrawElevation`). Both are pure functions of tile coords + frozen constants — no
new `Raylib.GetTime()` reads.

One follow-on the research pass didn't call: the plateau **side wall** was flat `Pal.HighSide`
(14,19,26), effectively black. That was invisible while the board was full of black gutters; on
a continuous lit floor it read as a hole punched in the ground. It now takes the biome hue plus
the key light, kept clearly darker than the top face so the step still reads as a step.

### B — "type that reads" (and a live distribution bug)

**Two atlases.** One 64px NotoMono atlas served every size from 11px to 92px. 11–14px body text
is most of the words in the game, and minifying a 64px atlas ~5× with bilinear filtering and no
mip chain is exactly the case that turns type into grey mush. The proof is not subjective: in
the WAR ROOM hall of fame, the 11px result column rendered **"WON" as "NON"**. Now `Cfg.FontUi`
bakes at 20px and serves text ≤ `Cfg.UiFontMax` (18px), `Cfg.Font` keeps 64px above that, both
get `GenTextureMipmaps` + `TextureFilter.Trilinear`, and all 280 `DrawTextEx` + every
`MeasureTextEx` call site route through `Cfg.Text` / `Cfg.Measure` so the atlas choice is made
in exactly one place (`Cfg.FontFor`).

Hud.cs's 10/11px sizes were raised to a 12px floor. **Gotcha worth carrying:** several call
sites *measure* through a helper and *draw* separately — `Clip(desc, 11, w)` then
`Cfg.Text(line, …, 12, …)`. Bumping only the draw size silently wraps at 11 and paints at 12,
which overflowed the shop descriptions. Every `Clip` / `WrapText` / `WrapLines` / `CenterText`
size argument was bumped in the same pass; a scan for measure/draw mismatches on the same
statement now returns nothing.

**The ship-blocker.** Asset paths resolved against the **current working directory**. Evidence,
from a real `dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
-p:DebugType=none`:

```
# base commit, run from /tmp/foreign_base
FONT: NotoMono-Regular not found, falling back to default
# base commit, run from inside the publish dir
FONT: NotoMono-Regular loaded (glyph atlas ok)
# this wave, run from /tmp/foreign_cwd
FONT: NotoMono-Regular 64px atlas loaded (/tmp/pub_v1/assets/NotoMono-Regular.ttf)
FONT: NotoMono-Regular 20px UI atlas loaded
FONT: ChakraPetch-Bold display atlas loaded
```

In the fallback the intro rule "2 actions per soldier — firing is 1 action" renders the em-dash
as `?`, in Raylib's built-in bitmap face. New `Cfg.AssetPath(rel)` resolves against
`AppContext.BaseDirectory` and falls back to cwd for dev; the font and the (latent, identical)
audio drop-in paths in `Audio.cs` both use it. Note for whoever publishes: **never**
`-p:PublishTrimmed=true` — it silently destroys save/load.

### C — a display voice

`assets/ChakraPetch-Bold.ttf`, 78,384 bytes, committed with its licence text alongside, mirroring
how `assets/NotoMono-Regular.ttf` + `NotoMono-LICENSE.txt` are already handled (both added to
`Sightline.csproj` with `CopyToOutputDirectory="PreserveNewest"`).

**Licensing, stated accurately: this is SIL Open Font License 1.1, which is *not* CC0.** It is
zero-cost, zero-royalty and zero-legal-risk for a bundled game font, but it requires shipping the
licence text and forbids selling the font on its own. That is the same footing the repo is
already on with NotoMono. Provenance verified three independent ways before committing: fetched
from `google/fonts` `ofl/chakrapetch/ChakraPetch-Bold.ttf` (HTTP 200, 78,384 bytes,
sha256 `65fbf76d…78a0`); the directory's `METADATA.pb` reads `license: "OFL"`; and the font's own
name table IDs 13/14 read "SIL Open Font License, Version 1.1" / `http://scripts.sil.org/OFL`.
Every codepoint the game bakes (em-dash, en-dash, bullet, ellipsis, ×, ·, the quote pairs) was
confirmed present in the cmap.

Baked at 96px and routed to titles ≥ 24px **only**, via `Cfg.TitleText` / `Cfg.TitleMeasure`:
the SIGHTLINE wordmark, VICTORY / RUN OVER, WAR ROOM, FIELD MANUAL, SKIRMISH, ASSEMBLE STRIKE
TEAM, MISSION n COMPLETE, REQUISITION, PROMOTION, SPECIALIZE, FIELD DOCTRINE, event titles,
PAUSED. Numerals and data stay on NotoMono — it is a good data face and mixing them is the
point. The corner-bracket rects at the wordmark and the end card derive from the *measured*
title width, so they re-fit the proportional face with no hand-tuning (confirmed in captures).

### D — one live defect

The requisition card drew its title with no width limit against a right-aligned price:
"COUNTER-PREP: SYNDICATE" + "12 INTEL" rendered as `SYNDICATE2 INTEL`. The card now measures the
price first, reserves that column, and picks the largest title size in 18→14 that fits (new
`Hud.FitSize`); `Clip` remains only as the backstop, so in practice the whole name survives
rather than being ellipsised. `SIGHTLINE_PREP` now accepts a faction name
(`SIGHTLINE_PREP=syndicate|legion|wardens`, default Wardens as before) so the longest title is
shootable on demand: `SIGHTLINE_SHOT=60 SIGHTLINE_PREP=syndicate`.

### Verified at the close (wt-v1)

- `dotnet build -c Release` → **0 warnings / 0 errors**.
- **Full self-test battery, 39 hooks** (every `SIGHTLINE_*TEST` in `src/Program.cs`) → all PASS,
  rc=0, no exceptions.
- `SIGHTLINE_PAIRTEST=1` → **PASS** (legA/legB MATCH).
- `SIGHTLINE_BALANCE=10` under xvfb on this branch vs. a baseline worktree at `2dec210`:
  `runs=20 missions=81` on both, and the two `balance.json` aggregates are **identical** field
  for field (wall-time excluded). Balance is untouched, as a visual wave should be.
- Autoplay ×5: LOSE m1 / WIN m6 / LOSE m3 / WIN m6 / WIN m6 — **zero exceptions, zero TIMEOUT**.
- Captures inspected by eye: 8 biomes before/after (fixed `SIGHTLINE_MAP` per biome so terrain is
  comparable), WAR ROOM, FIELD MANUAL, VICTORY, intro, shop, prep/SYNDICATE, barracks, draft,
  event, tooltip, skirmish, plus `SIGHTLINE_CB=1` on both a board and the WAR ROOM.

**Measurement-flag note carried forward:** `SIGHTLINE_BALANCE` needs `xvfb-run`; without a
display it prints `runs=0 / (no data)`, still claims "N matches" and exits 139. Always assert the
`runs=` line. And run every harness shell with `XDG_CONFIG_HOME` and `SIGHTLINE_BALANCE_JSON`
pointed inside your own worktree — several self-tests stash and restore the real
`~/.config/Sightline`, and concurrent agents will corrupt each other's restore.

**Left for a later wave, deliberately:** `Renderer.cs` still draws board labels at 10/11px (the
two-atlas fix already sharpens them a great deal; raising tile-constrained text needs its own
layout pass, and other waves own parts of that file). `DrawBiomeSignature` stays floor-tile-only
so its emissive cues don't creep around cover bases. The plain-`SIGHTLINE_SHOT` non-determinism
(45 `Raylib.GetTime()` reads in Renderer.cs, 11 in Hud.cs, a clock-seeded `Util.Rng`) is
chartered elsewhere and was not touched; this wave added no new `GetTime()` reads.
# PROGRAM "RESONANCE" — WAVE T1 · BASIC TRAINING

**Goal.** Close the onboarding over-claim. `docs/DESIGN.md` §4 graded Onboarding
**"Addressed (W11)"**. A research pass on this tree found that grade to be false, and the
finding re-confirmed at the start of this wave:

- `Game.TutPrompts` was a **5-card callout strip**, mission 1 only, once per profile
  (`Display.TutorialSeen`): CONCEAL → MOVE → OVERWATCH → FIRE → wrap-up. **Three of ~14
  verbs taught.**
- During tutorial card **1 of 5** the action bar already showed **twelve** verbs (BRACE,
  HUNKER, RELOAD, FIRE, GRENADE, GRAPPLE, FLASH, SHOVE, DRAG, VAULT, OVERWATCH, FOCUS) plus
  contextual STABILIZE/HACK/PLANT/BEACON/EXTRACT. FUL-12 **dimmed** the eleven non-lesson
  buttons to 45%. Dimming is not staging — every verb was still introduced, all at once, by
  being present.
- `Hud.DrawIntro` opened with a **six-bullet rules wall** — the exact artefact §3.G says not
  to ship — in front of a player who has not taken a turn.
- The one good counter-example, the one-shot BRACE field tip (FUL-12), was exactly the right
  pattern used exactly once.

## What shipped

**A. TRAINING OP** — `GameMode.Training` (appended; GameMode is not persisted anywhere,
verified against SaveGame/Run/Stats). `BeginTraining()` in `src/Game.Modes.cs`; entered from
the intro (button / key **N**, always present, green-plated until the profile has finished it
once), restarted in-drill with **[P]** or from the drill's end card.

- Board: `Maps.TrainingArena` + `Mission.BuildTraining`. **The arena is deliberately NOT
  appended to `Maps.Layouts`** — that array's length feeds `DailyArena(seed) % Layouts.Length`
  and the FUL-9 no-repeat deck, so appending would have silently moved the whole measured
  campaign. `SIGHTLINE_EXPOSURETEST` still reports **35 arenas** after this wave.
- Script: two 12-HP recruits (ASSAULT + SHARPSHOOTER, so GRAPPLE and MARK are both on the
  board) vs **four dormant aim-45 targets** in two pods. Low-cost failure by construction, and
  restartable in one keystroke.
- Lessons (`Game.TrainLessons`, 8 well-ordered problems, each a thing to DO):
  MOVE → COVER → FLANK → FIRE → OVERWATCH → GRENADE → ABILITY → CLEAR. The arena was authored
  *around* the lessons: low cover two steps from the deploy tiles (COVER is solvable on move
  one), the front pair at (12,4)/(12,6) behind high cover on their **west** side only — so the
  FLANK lesson has one clean answer, walking the open column x=12 to (12,1)/(12,9), which are
  themselves beside high cover. Every lesson carries a **turn-budget patience fallback** so a
  player who solves it another way is never stranded.
- Non-persistence: every persistence seam in the codebase is already keyed on
  `Mode == GameMode.Campaign` or `DailyMode`, so Training writes nothing **by construction**.
  TUTTEST asserts that rather than trusting it: it snapshots save.json + meta.json, plays a
  live (non-NoPersist) drill to a win, and compares. The only profile flag the drill may touch
  is `Display.TrainingSeen`, on completion.
- The drill's biome is pinned (`TrainingMapSeed = 8` → STEEL) so the teaching frame is fixed
  and cool-neutral — nothing in the terrain competes with the amber objective accent or the
  red threat accent the lessons point at (DESIGN §3.H).
- Implementation seam: `SetupMission` gains **one** `if (Mode == GameMode.Training)` after
  `Mission.Build`, exactly mirroring how LAST STAND swaps its force in. Everything downstream
  (anim queue reset, BeginTurn, combat roster, concealment, FX) is reused unchanged.

**B. STAGED VERBS** — `Game.OnboardingActive` / `VerbStagingActive` / `VerbRevealed`, applied
as one contiguous block at the **end** of the spec-collection sequence in
`Hud.DrawActionButtons` (that method is the repo's hottest merge-conflict range; the diff is
one `RemoveAll` + one `Add`).

- During the drill and campaign **mission 1** the bar carries only what has been taught, and
  grows as each lesson opens (`TrainLessons[].Reveal` / `TutReveal[]`, index-aligned with
  `TutPrompts` through the named `TutStep*` constants).
- RELOAD is revealed **with** FIRE: a staged-away RELOAD could strand a dry soldier.
- **SHOW ALL** ([**V**], persisted in `Display.ShowAllVerbs`) is always on the bar while
  onboarding runs and bypasses staging in both directions — a returning player is never locked
  out of a verb they know.
- STABILIZE is **exempt**: it only surfaces at all while a squadmate is bleeding out, and
  hiding the answer to that is the failure the escape exists to prevent.
- Staging is **capped**: from mission 2, and in every other mode, the bar is always whole.
- The FUL-12 lesson-focus dim is left exactly as it was — with staging on it now dims a
  one-or-two-button bar toward the lesson verb, which is the behaviour it always wanted.

**C. JUST-IN-TIME FIELD TIPS** — `Game.FieldTips`, 10 cards, replacing `UpdateBraceCallout`
with `UpdateFieldTips`. Each fires **once per profile**, the first time its precondition is
actually true in play:

| bit | prio | tip | precondition |
|-----|------|-----|--------------|
| 0 | 2 | BRACE | a live (Active) hostile — FUL-12's original |
| 1 | 0 | STABILIZE | an ally is DOWN |
| 2 | 1 | RELOAD | a soldier is dry with a live threat |
| 3 | 3 | GRENADE | a soldier with a grenade sees an Active foe **in cover** |
| 4 | 4 | HUNKER | a soldier with actions left stands in the open, seen by an Active foe |
| 5 | 5 | SHOVE | `CanShove` — an enemy is adjacent |
| 6 | 6 | VAULT | `CanVault` with a live threat |
| 7 | 7 | DRAG | `CanDrag` with a live threat |
| 8 | 8 | FOCUS | the player has armed a plain overwatch and 2+ Active foes are alive |
| 9 | 9 | ITEM | a soldier holds a charged utility item with a live threat |

`Prio` (not table order) resolves simultaneous candidates, so a bleeding-out ally outranks a
nicety. Seen-flags are a **bitmask** (`Display.TipsSeen`) — one new DTO field for the whole
table — and FUL-12's `BraceTipSeen` bool migrates into **bit 0** on load and is still written
from bit 0 on save, so the bridge holds in both directions. Interactive-only: under `NoPersist`
the whole scan returns before touching anything unless `SIGHTLINE_TIP=<bit>` (or the legacy
`SIGHTLINE_BRACETIP=1`) stages one. Tips never fire *during* a drill lesson or a mission-1
lesson card — the card owns the slot.

**D. The intro** — the six-bullet rules wall is one line ("One squad. 6 escalating missions.
They carry it all."), kept short on purpose so it clears the HEAT/ASCENSION panel that occupies
the right ~28% of that row. The rail motif survives as two diamond end-caps.

## Verification

- `dotnet build -c Release` → **0 warnings / 0 errors**.
- **`SIGHTLINE_TUTTEST=1` → PASS.** New hook. Five parts: (1) arena rows/cols, deploy+foe seats
  are floor and collision-free, every foe walk-reachable, the lesson-critical tiles exist;
  (2) the drill is driven lesson by lesson — each predicate asserted **false → true**, then
  `UpdateTraining` ticked twice to prove it advances **exactly one** step (reachable AND fires
  once), plus the terminal lesson never self-advances and the patience fallback works;
  (3) staging — monotonic reveals, the FIRE lesson reveals shoot+reload and does NOT leak
  grenade, the terminal lesson turns staging off, the SHOW ALL escape round-trips,
  **mission 2 is never staged**, and the load-bearing `TutStep*` constants + `TutPrompts`
  length + `TutReveal` alignment are pinned (through an array, so the check isn't const-folded);
  (4) tip bits/prios/codes unique and in range, bit 0 is BRACE, and every predicate is driven
  to true on a real board (GRENADE by sweeping every floor tile — a stronger claim than one
  hand-picked stance, and it survives an arena edit); (5) seen-flags round-trip through real
  JSON, the mask is precise (an unseen bit stays unseen), a legacy `{"BraceTipSeen":true}` file
  folds into bit 0, an empty file defaults everything to unseen, and a **live** drill played to
  a win leaves save.json and meta.json byte-identical. The real display.json is stashed and
  restored. (The test earned its keep immediately: its first run found four genuine bugs.)
- **Full battery PASS** (Release binary, isolated `XDG_CONFIG_HOME`): TUTTEST, DKTEST,
  RESCUETEST, STAGGERTEST, MORALETEST, BEACONTEST, COMBATTEST, SAVETEST, AITEST, ITEMTEST,
  STATUSTEST, COVERTEST, TRAITTEST, WOUNDTEST, CDTEST, FIELDTEST, SIEGETEST, EVENTTEST,
  VETTEST, OWTEST, SCARTEST, CONTRACTTEST, SHOVETEST, CONCEALTEST, HAZARDTEST, BENCHTEST,
  DRAFTTEST, METATEST, CODEXTEST, MODETEST, HORDETEST, DEATHTEST, HEATLADDERTEST, SNAPTEST,
  AUDIOTEST, AMBIENTTEST, EXPOSURETEST, DOWNTEST, PIKETEST, PODTEST — 40/40.
  (`scripts/qa-sweep.sh` misses six of these; they were run separately.)
- **`SIGHTLINE_PAIRTEST=1` → PASS** (byte-identical CRN legs).
- **Autoplay ×5** clean: LOSE/WIN/WIN/WIN/WIN, mission 6, no exceptions, no TIMEOUT.
  **Drill autoplay ×3**: WIN in 619-1011 frames — the drill is completable by the weak
  smoke-test AI, so it cannot be a wall.
- **`SIGHTLINE_BALANCE=10` byte-identical to base** (asserted `runs=20`), which is the proof
  this is an interactive-only change: every new code path is `!NoPersist`-gated or
  `Mode == Training`-gated, and `Maps.Layouts.Length` is untouched.
- Screenshots inspected in **both** palettes (`SIGHTLINE_CB=1`): the drill at lessons 1/3/7/8,
  the staged bar at the FIRE lesson (FIRE + RELOAD + SHOW ALL, where twelve buttons used to
  sit), the SHOW ALL bypass (twelve buttons return, toggle reads ALL VERBS), the mission-1
  OVERWATCH lesson (OVERWATCH + SHOW ALL only), a field-tip card, and the trimmed intro.

## Notes for the next wave

- **The top bar was lying.** In the drill it read `MISSION 1/6 · ELIMINATE`; it now reads
  `TRAINING OP · <LESSON> n/8`. Worth remembering that mode-shaped HUD text defaults to the
  campaign branch — a new mode has to claim its own readout or it inherits a false one.
- **Deliberately left undone.** The drill teaches **tactics only** — nothing about the
  barracks, perks, the campaign map or requisition. There is no per-lesson replay beyond the
  whole-drill restart. And because a lesson card owns the tip slot, the tips never fire inside
  the drill: a player who only ever runs the drill meets 8 verbs, and meets the other ten on
  their first real deployment. All three are scope choices, not oversights.
- **The `TutStep >= TutStepFire` completion gates** (`EnterBarracks` / `LoseRun`) were **not
  touched** — the mission-1 track kept its exact semantics and constants, and TUTTEST now pins
  them so a future renumber trips a test instead of silently re-offering onboarding forever.
# PROGRAM "RESONANCE" — Wave Q1 "NO TWO IN ONE PLACE" (defect wave, adversarial-QA driven)

Branch `wt-q1`. Three reproduced QA findings, all fixed with a guard that was watched to fail
first. No feature work.

## D1 (HIGH) — two living units on one tile

**Mechanism.** `Game.ActivatePod` (the surprise reveal-scatter) planned EVERY dormant pod
member against one board snapshot and enqueued all their move steps before any executed.
`Ai.Plan`'s blocked predicate reads live `Unit.X/Y`, but `MoveStepAnim` only commits `Unit.X/Y`
when the step FINISHES — so member 2 planned blind to member 1's destination. The same
append-at-the-end enqueue also landed enemies on PLAYER tiles, because the player's remaining
path steps sit AHEAD of the scatter in the queue and therefore land first.

**Why HIGH, not cosmetic.** `Game.UnitAt` returns the FIRST match (Players before Enemies) and
both hover and click route through it, so the buried unit could not be hovered, could not show
odds, and could not be clicked to target — with no target-cycle key. Both units also read
cover=0 / flanked at range 0. Directly against the "reads clearly" pillar.

**Fix.** `Ai.Plan` gains an optional reserved-tile set (`null` = the exact pre-Q1 predicate, so
every one-unit-at-a-time caller is untouched). `ActivatePod` seeds a claim set from the final
destination of every already-queued move (any unit, any team) and adds each member's post-cap
landing tile as it is decided. Current tiles need no entry — `IsOccupiedByOther` covers those.

**Guard — `SIGHTLINE_STACKTEST=1`** (new, permanent; `=2` widens it to full campaigns under both
bots). Two detectors run at once: a `MoveStepAnim.StackProbe` hook firing at ACTIVATION (X/Y is
still the origin and only one anim is ever active, so a non-self `UnitAt(Tx,Ty)` is a proven
imminent collision) and a per-frame shared-tile sweep collapsed into episodes.

| sweep | before | after |
|---|---|---|
| narrow (16 missions, all 8 objectives x h0/h2, dumb bot) | 20/1688 steps = **1.185%**, 21 episodes, longest **598 frames** | 0/1602 = **0.000%**, **0** episodes |
| wide (134-135 missions, full campaigns, dumb + competent) | 105/16037 = **0.655%**, 108 episodes, longest **1799 frames** (~30 s) | 0/17136 = **0.000%**, **0** episodes |

Every logged collision was `phase=PlayerTurn`, exactly as QA measured, and the wide sweep caught
the enemy-onto-player case (`HUNTER -> (17,9) already held by Player/CORPSMAN`).

**Other batch-plan sites audited.** The enemy turn is the only other `Ai.Plan` production
caller and it is strictly one unit at a time (`PickNext` -> `_aiPlan` -> `ActAfterMove`, with the
queue draining between), which the 100%-PlayerTurn distribution corroborates. `ShoveAnim` (the
other relocating anim) resolves occupancy in its own `OnStart`, and the endless wave spawner
checks `IsOccupiedByOther` at placement. The wide sweep spans Escort's VIP leash and Defend's
reinforcements and found zero. **Only `ActivatePod` was changed.**

## D3 (LOW, legibility) — the headline HIT% was not the real probability

`Combat.Resolve` folded the player-only STEADYING streak bonus into `effHit`; `ComputeOdds` did
not, so the tooltip's number under-reported by up to 12 points and disclosed the delta only as a
separate badge. Folded into `ComputeOdds` instead (player-only as before, now under the same
3..95 clamp as every other aim source rather than riding Resolve's 1..99); `Resolve` no longer
adds it a second time. Measured, rifle vs an open target, 40k seeded rolls: **before displayed 66
/ rolled 77.89%; after displayed 78 / rolled 77.89%**. The STEADYING badge stays as the
explanation. Doc drift fixed in the same pass: the `Combat.cs` "HIDDEN from the ComputeOdds
tooltip" comments, `Codex.cs`'s "banks a hidden +6 aim", `docs/FEATURES.md`, and the
`docs/AUDIT-2026.md` honest-tooltip item (now closed).

New `SIGHTLINE_COMBATTEST` asserts — `steadyNotInHit`, `steadyBadge0/Max`, `steadyLeakedToEnemy`,
`steadyRollNotDisplayed` (the roll-vs-display one is measured, not read from source) — plus the
old `streakVisibleInOdds` assert inverted. All four verified FAILING on the pre-fix code.

## D4 (LOW) — `RUSHED 2ND SHOT` badge missing on the plain-hover odds path

Both odds paths apply the -15 `SnapAim` penalty to the displayed hit% and the tooltip renders on
both, but the badge was gated on `g.AimMode`. Ungated. `SIGHTLINE_TOOLTIP=hover` (new) stages
that path for a shot; before/after screenshots show the same HIT 7% with and without the
explaining line.

## Harness additions
- `SIGHTLINE_STACKTEST=1|2` (above).
- `SIGHTLINE_TOOLTIP=hover` — the plain-hover odds tooltip (keyboard board cursor, no live mouse).
- `SIGHTLINE_SHOTSEQ=<n>` — dump n consecutive frames; a shot frame requested alongside
  `SIGHTLINE_AUTOPLAY` now draws for real, so the pair films live play. Used for the jitter
  check the D1 fix sits next to: a SCOUT crossing three tiles advanced 992.0 -> 1056.0 -> 1120.0 px
  strictly monotonically, with no snap-back at either commit frame (`q1-move-nojitter.png`).

## Verified at the close
Release **0 warn / 0 err**; the full self-test battery PASS (the 35 in `scripts/qa-sweep.sh` plus
`EXPOSURETEST`, `DOWNTEST`, `PIKETEST`, `PODTEST`, `FUL11PROBE`, and the new `STACKTEST`);
`PAIRTEST` PASS (both slots MATCH — the D1 fix changes planning order but not CRN identity);
autoplay x5 clean (WIN/WIN/WIN, LOSE m5, LOSE m4 — no exceptions, no TIMEOUT).

**Balance delta, `SIGHTLINE_BALANCE=10` (20 matches), base 2dec210 vs the Q1 tree** — both legs
run under xvfb with `runs=20` confirmed:

| | before | after |
|---|---|---|
| campaign win-rate | 50% | 45% |
| greedy / sloppy | 70% / 30% | 60% / 30% |
| paired gap | 40 pts | 30 pts |
| paired margin (missions) | +1.00 ±0.42 | +0.90 ±0.81 |
| completion h0/h2/h4/h6/h8 | 50/100/75/25/0 | 50/75/50/25/25 |

One run of 20 separates the headline rates, against a per-policy SE of roughly ±15 pts and
per-rung n=4 (±25) — i.e. inside noise, with h8 moving the other way (0 -> 25). Two mechanisms
legitimately perturb the world stream: enemy scatter destinations differ (D1) and the greedy bot
now sees the true, higher hit% while on a miss streak (D3). Nothing here justifies a tuning
change; a real re-baseline wants the N=50 flywheel.
## PROGRAM RESONANCE — F1 "FOUNDATIONS" (2026-08-28)

**Goal**: harden what a shipping game needs and close two real save-corruption holes.
Mostly cold files. Everything below has a command behind it; numbers are ones this wave
measured in this container, not inherited from the brief. Where a briefed number did not
reproduce, the measured one is recorded instead and flagged.

### 1. The append-only enum contract is now actually enforced

`SaveGame.SelfTest` already pinned persisted enums by ordinal — better than CLAUDE.md
claimed — but with two holes.

* **Hole A**: `RewardKind` is persisted by ordinal (`CardDto.Reward`, a raw int) and was
  completely unguarded. Latent today (3 members) but exactly the enum a "more mission
  rewards" wave edits.
* **Hole B**: 8 of the 12 guarded enums pinned only head and tail, so a **mid-enum
  insertion passed silently**. `Perk` (23 members, the likeliest to grow) never got a
  mid-pin at all.

Replaced the positional pins with a **golden FNV-1a fingerprint** over each enum's full
ordinal→name sequence (`SaveGame.EnumFingerprint` + `PersistedEnums`), covering all 12
plus `RewardKind` = **13**. Any reorder, insertion, removal or rename now fails loudly with
both hashes and a remediation note.

**Proof the guard bites** (inserted `ProofOfGuard` at index 5 of `Perk`, shifting 18
ordinals — a change the OLD guard passed, since `Length>=20`, `[0]==LockOn` and
`[^1]==Siegebreaker` all still held):

```
SAVETEST: FAIL (enumShape:Perk (golden 0xEADD48BA, actual 0xB7206DFC))
  >> A persisted enum changed shape. Ordinals ARE the save format: appending a member at
  the END is safe (old saves keep their meaning); inserting, reordering, removing or
  renaming one silently re-points every existing save and every meta.json profile. If you
  appended, paste the actual hash above into SaveGame.PersistedEnums. If you did anything
  else, undo it.
```

Reverted; SAVETEST PASS again.

Also added `SchemaVersion` to `RunDto` and `MetaDto` (stamped `CurrentSchema = 1` on every
write, asserted on disk by SAVETEST). Purely additive fields still need no bump; the hook
exists for a change defaults cannot rescue.

### 2. Two save-corruption defects (from the adversarial QA pass)

* **D2 — a dead CONTINUE button, forever.** `save.json` containing `null`, `{}`, or an
  object with a renamed/empty `Squad` **parses fine**, so `Load` returned null and left the
  file in place. `ContinueRun` refused it (empty squad) while `Hud` kept drawing the button
  off `SaveGame.Exists` — click, nothing, no banner, no stash, no delete. Fixed entirely
  inside `SaveGame.cs` (no touch to the Game.cs/Hud.cs hot spots): "parsed but unusable" is
  now routed through the same stash-and-remove path as unparseable, and `Exists` validates
  by loading, memoised on the file's (write-time, length) so the per-frame intro poll costs
  one `stat`.
* **D5 — unvalidated enum ordinals from disk.** Casts of persisted ints are legal C#, so
  nothing threw; `Objective: 99` loaded and ran, and `CheckEnd`'s final `else` treated it as
  **Evac** — the mission ran with an unreachable win condition until the squad wiped. Reads
  now go through `EnumOr` / `AddDefined`: a scalar falls back (unknown Objective → Eliminate,
  the always-reachable goal), and unknown list members are **dropped** rather than defaulted
  (a bogus perk silently becoming `Perk[0]` would be a stealth buff).

Both are covered by a new `StructureSelfTest` inside SAVETEST. Verified it bites: with the
fixes temporarily reverted, `SAVETEST: FAIL (deadSaveStillOffered:null)`.

### 3. Save location — the comment was wrong

Resolved empirically with a throwaway probe rather than trusting either source. The tech
lead was right and QA's harness path was not:
`SpecialFolder.ApplicationData` → `$XDG_CONFIG_HOME` (only if the directory **already
exists**) else `$HOME/.config`. So it is **`~/.config/Sightline`**, never `~/.local/share`
(that is `LocalApplicationData`, which this game does not use).

Edge case found while probing: `GetFolderPath` uses `SpecialFolderOption.None`, which
returns `""` when the directory does not exist — `Path.Combine("", "Sightline")` would then
put saves in a **relative** dir next to the process CWD, scattering them per launch
directory. `SaveGame.Dir` now falls back to `$HOME/.config/Sightline`. (This also explains
the harness-isolation footgun: `XDG_CONFIG_HOME="$PWD/.xdg"` without `mkdir -p` isolates
nothing.)

### 4. PublishTrimmed silently destroyed all persistence

Reproduced: the trimmed build compiled 0-error, booted, played and **finished a full
autoplay campaign** while saving nothing — no `save.json`, no meta profile (salvage,
veterans, achievements, hall of fame). SAVETEST and METATEST both FAILED against it. A
publish wave grepping for "0 Errors" ships this.

Cause: reflection-based `System.Text.Json` loses the metadata trimming strips, **and**
trimming turns the reflection fallback off by default — `InvalidOperationException:
Reflection-based serialization has been disabled for this application` — which
`Stats.WriteJson`'s bare `catch {}` swallowed whole.

Fix, three parts:
1. `SaveGame` and `Display` serialise through **source-generated
   `JsonSerializerContext`s** (`SaveJson`, `DisplayJson`) — no reflection, nothing to strip.
   Removed 6 of the 7 IL2026 sites. Cost: both classes became `partial`, their DTOs
   `internal`. No behaviour change.
2. The 7th site (`Stats.WriteJson`) serialises **anonymous types**, which no generator can
   see. Kept reflective but made safe: the csproj roots our own assembly
   (`TrimmerRootAssembly`) and re-enables `JsonSerializerIsReflectionEnabledByDefault` **for
   trimmed publishes only**. Verified: the trimmed binary writes a balance JSON identical to
   the untrimmed one (1777 bytes; it previously wrote **nothing**). IL2026 suppressed at that
   one site with the reasoning — trimmed publish is now 0 IL warnings.
3. `Stats.WriteJson` prints its exception. A silent total failure is how a broken publish
   config survives review.

**Proof** (`scripts/publish.sh` runs this on every publish and fails the build if either
line is not PASS):

```
>> output: 25M
Sightline  THIRD-PARTY-NOTICES.txt  assets  libraylib.so
>> verifying persistence against the published binary
   SAVETEST: PASS (run round-trips squad/perks/weapon-mods/card/heat; schema stamped;
     13 persisted-enum fingerprints match; ...; unusable saves stashed + un-offered;
     junk ordinals clamped)
   METATEST: PASS (salvage/achievements/unlocks/legends/totals round-trip; ...)
```

**Measured publish matrix** (linux-x64 self-contained, SDK 8.0.130; `start` = median of 10
window-free SAVETEST launches after a warm run):

| mode | flags | size | start | files |
|---|---|---|---|---|
| **release** (new default) | Trimmed + ReadyToRun + SingleFile | **25 MB** | **95 ms** | 6 |
| small | Trimmed + SingleFile | 18 MB | 350 ms | 6 |
| no-trim | ReadyToRun + SingleFile | 80 MB | 115 ms | 6 |
| plain | SingleFile | 68 MB | 168 ms | 6 |
| — | folder | 75 MB | — | 193 |

The brief expected R2R-only as the recommendation. Measurement changed it: trimming strips
the framework's precompiled R2R code, which is why plain-trimmed is the **slowest** config
(350 ms); adding R2R back costs 7 MB and gives the **fastest** start of all four. Trimmed +
R2R is strictly better than R2R alone on both axes, so it is the default.

### 5. Assets were resolved against the CWD (found by the foreign-cwd check)

The brief's "run the published binary from a foreign working directory" check caught a real
distribution bug: launched from anywhere but its own folder, the game printed
`WARNING: FILEIO: [assets/NotoMono-Regular.ttf] Failed to open file` and silently fell back
to raylib's built-in font (dropped-in audio would likewise never load). `Util.Asset` now
resolves against `AppContext.BaseDirectory`, falling back to the bare relative path so
`dotnet run` from the repo root is unchanged. Verified: full autoplay from `/tmp` →
`RESULT: WIN mission=6`, font loaded.

### 6. Licence compliance

`THIRD-PARTY-NOTICES.txt` at the repo root, copied into every build and publish output:
raylib 6.0 (Zlib), Raylib-cs 8.0.0 (Zlib), .NET 8 runtime (MIT). Each text pulled from the
package or tag this build actually consumes (`raylib-cs.nuspec`'s recorded commit, the
runtime pack's `LICENSE.TXT`, the `raylib` `6.0` tag) — sources cited inside the file.
Noto Mono (OFL-1.1) was already compliant. A FONTS section is structured so a second OFL
font is one entry plus its committed `-LICENSE.txt`.

**No root `LICENSE` was invented.** How the owner's own code is licensed is theirs to
decide; an unlicensed private repo already defaults to all-rights-reserved, so the status
quo is safe. `docs/DISTRIBUTION.md` records it as an **open owner decision** with a
one-read comparison (all-rights-reserved / MIT / source-available) and the recommendation:
leave it, and write an explicit proprietary LICENSE the moment a build goes to anyone
outside the project — the decision is one-way in only one direction.

### 7. The verification contract is now truthful

`scripts/qa-sweep.sh` ran **35 of 41** self-tests. It missed the bleed-out state machine
(`DOWNTEST`), the pikeman (`PIKETEST`), pods (`PODTEST`), the content-exposure invariant
(`EXPOSURETEST`), the finale-kit probe (`FUL11PROBE`) and — worst — `PAIRTEST`, the CRN
identity check every measurement in this project rests on. All six added. `PAIRTEST` (38 s
measured) is gated behind `--full`, which the header and CLAUDE.md name as the pre-merge
mode. Full sweep: **41/41 PASS**, autoplay ×3 clean.

**CLAUDE.md corrections** (each replaced a wrong line; nothing appended as an essay):

* **"shots stay byte-identical" — FALSE, and never was true.** Measured: two
  `SIGHTLINE_SHOT=90` runs differ in **303,065 of 1,024,000 pixels (~30%)**. Cause
  confirmed by count: **58** `Raylib.GetTime()` wall-clock reads drive animation (46 in
  `Renderer.cs`, 12 in `Hud.cs`) and `Util.Rng` is clock-seeded by default
  (`Util.cs:228`). Rewritten to state what IS true: post-FX off, `NoPersist` keeps the
  harness off disk, the flywheel reseeds explicitly (`Util.Reseed(50000+slot)`), and
  **`SIGHTLINE_PAIRTEST` byte-identity is the real determinism gate**. The same claim in
  this file's ground-rules preamble was corrected too.
* **"the autopilot LOSES most seeds" — the brief said this was stale ("reaches m6 on every
  seed, wins roughly half; 3W/2L over 5"). It does not reproduce.** Measured over **15**
  Debug autoplays: **3 WIN / 12 LOSE**, finale reached on 5 of 15, earliest death mission 1,
  zero TIMEOUTs. The original line is substantially right; what it lacked is that a WIN is
  *normal*. CLAUDE.md now carries the 15-run distribution and "a WIN is normal, not
  suspicious" instead of either claim. **A 5-run sample was not enough to overturn it.**
* Added the **harness-isolation** block (`XDG_CONFIG_HOME` — with `mkdir -p`, see §3 — and
  `SIGHTLINE_BALANCE_JSON`) as house procedure.
* Added the **free-key list**, verified by grepping every `KeyboardKey.*` in `src/`: bound
  are `A B C D E F G H K L M R S T W X Y`, `1`–`9`, arrows,
  Tab/Space/Enter/Escape/Backspace/F2/Kp±. **FREE: `I J N O P Q U V Z`.**
* Added **reference timings** (all measured here): no-op Release build 1.5 s (~12 s after
  touching one file), one self-test 0.1–0.4 s via the Release binary / 1–2 s via
  `xvfb-run dotnet run -c Debug`, PAIRTEST 38 s, autoplay ~22 s, `SIGHTLINE_BALANCE=10`
  **311 s** (~31 s/slot, Release binary under xvfb — the brief's 294 s, close).
* Corrected "**Six** persisted-by-ordinal enums" → thirteen, named, with the
  append-and-repaste-the-hash procedure.
* Noted that **`SIGHTLINE_BALANCE` needs a display**: run without `xvfb-run` it silently
  reports `runs=0` and writes an empty aggregate. (Cost this wave a wasted measurement.)

### 8. Dead conditionals

Only the two verified-genuine ones, kept surgical (`Game.cs` is a merge hot spot):
`Game.cs:3193` `Vip != null` (always true after the guard directly above) and
`Game.cs:~3584` `next ?? Selected` (unreachable inside `next != null`; the duplicated
`Selected` re-test went with it). The four other CA1508 hits were confirmed false positives
and left alone. `AnalysisMode=All` NOT enabled (~969 warnings, ~99% contradicting this
codebase's deliberate design).

### Verified at the close
Release **0 warnings / 0 errors**; trimmed publish **0 IL warnings**; `qa-sweep.sh --full`
**41/41 PASS**; autoplay ×3 clean; PAIRTEST PASS; enum guard watched to FAIL and revert;
SAVETEST + METATEST PASS against the **trimmed** binary; published binary plays a full
campaign from a foreign working directory.

### Left deliberately
The root `LICENSE` (owner's decision, §6). The `catch {}` swallow-everything pattern
elsewhere in the codebase (deliberate house style; only the one that hid a total silent
failure was changed). The other ~969 analyzer findings. `Nullable enable` (measured ~359
warnings across 20 files, including both merge hot spots).

---

## PROGRAM RESONANCE — WAVE A2 "THE VOICE" (audio voicing, music, mix layer)

A1 built the ear and fixed *levels*. A2 fixed *voicing*, rebuilt the music, and gave the
owner a mix they can actually turn. Every number below came from a command run at landing
(`SIGHTLINE_AUDIOGATE=1`, `SIGHTLINE_AUDIODUMP=1`, `scripts/audio-report.py`).

### 1. The weapons were hiss, not gunfire — a one-line unit bug

`Noise()`'s `lp` parameter was used **directly as the one-pole filter coefficient**
(`alpha = Clamp(lp, 0.02, 1)`). The implied cutoffs: `lp 0.55` = 5.6 kHz, `lp 0.8` = 11.3 kHz,
`lp 1.0` = **no filtering at all**. Every weapon was a flat broadband rectangle to 22 kHz.

`fc` is now a cutoff **in Hz** (`alpha = 1 - exp(-2*pi*fc/SR)`) over 2-3 cascaded poles, plus
an RBJ resonant band-pass "body" layer. Both paths are makeup-normalised from their
impulse-response energy, so a recipe's `vol` numbers still mean "how loud is this layer".
`Click()` (2.5 ms of unfiltered white noise, byte-identical at the head of fourteen cues) took
`tone`/`bright`/`len`/`ring`. `Tone()` took a `tilt` one-pole-pair over the oscillator output,
because `Shape()`'s naive Square/Saw ran an unrolled harmonic comb to Nyquist.

Measured band split, before -> after (AUDIODUMP, % of energy):

| cue | 200 Hz-1 kHz | 1k-5k | >5k |
|---|---|---|---|
| w_rifle   | 16.5 -> **58.7** | 17.2 -> 17.2 | 34.1 -> **0.8** |
| w_shotgun | 17.7 -> **65.5** | 13.6 -> 8.8  | 19.8 -> **0.5** |
| w_sniper  | 80.3 -> 26.9     | 13.0 -> **67.9** | 3.4 -> 2.7 |
| w_lmg     | 19.3 -> **54.9** | 12.0 -> 6.0  | 12.3 -> **0.4** |
| w_smg     | 18.1 -> **67.5** | 18.9 -> 28.2 | 61.7 -> **1.2** |
| miss      | 16.9 -> **84.2** | 82.9 -> 15.8 | 0.2 -> 0.0 |

Spectral centroid now orders the five weapons the way the fiction does:
**sniper 1439 > smg 993 > rifle 739 > shotgun 519 > lmg 445 Hz** (it used to be sniper 933 and
lmg 1873 — the sniper was the *dullest* weapon in the game). Getting there needed the sniper's
long 300->150 Hz saw tail replaced with a bright ringing wash; at its old level it dragged the
centroid below the shotgun's.

### 2. Music: rebuilt, and the gate raised to 15%

`PadTone` was a single bare sine with one tremolo, so both beds were a chord of pure tones with
literal silence between them — black spectrograms. They cleared A1's ">=5% above 1 kHz" check at
5.5 / 6.4%, a threshold set by the same wave that had to pass it. Rebuilt: harmonic pad stacks
with per-partial LFO rates and phases; a **loop-seamless band-limited air bed** (a bank of
integer-Hz sines with fixed-seed phases — real noise cannot loop) split into two coherent
anti-phase bands so the air's centre of gravity sweeps once per loop; `Pulse` given a real onset
transient (it was `sin()*exp(-8*phase)`, a sine swelling from zero with no attack); loop 8 s ->
**16 s**. Most of the new top is *tonal* (chord tones an octave or two up), with the noise bed
held as a floor — a bed that clears a brightness gate on broadband noise alone is just hiss.

Floor raised to **>=15%**; measured **23.1% (ambient) / 25.2% (combat)**.

### 3. The loop-seam gate was measuring the wrong thing (twice)

Raising brightness immediately tripped A1's secondary `|1st-diff delta| <= 0.005` bound (measured
0.016 / 0.035). That constant was calibrated against beds whose worst interior sample step was
0.076; the A2 beds' is 0.20, and a wider-band waveform bends harder *everywhere*. Made it
scale-free (`CurvRatio`, against the largest interior second difference), the same argument A1
used for the value delta.

**Then a negative test showed both ratios are too coarse to be a guarantee.** Adding a
deliberately de-tuned 333.37 Hz partial (does not divide the loop) at amplitude 0.05 left
ratio 0.03 / curv ratio 0.38 — the gate said PASS on a bed that genuinely clicks. Broadband
content hides a small discontinuity inside its own worst case.

So the guarantee is now a **proof**: every music generator is a pure function of the sample
index, so rendering `N + 2048` samples yields the loop plus its own true continuation, and a
seamless loop repeats itself there exactly. That check flags the same de-tuned partial at
**0.0250** against a 1e-4 tolerance — and on its first run caught a real **1.4e-4** break in
`Pulse`'s phase (`(t*rate) % 1f` loses five digits at t~16 s), now exact integer-index
arithmetic. `ValidateMusic`'s "both endpoints near zero" test was retired for the same reason:
it is the naive-absolute trap, and the A2 beds legitimately wrap at a non-zero value.

### 4. The mix layer (what makes this tunable by ear)

- **Four persisted faders** — MASTER / SFX / MUSIC / UI in `display.json` (additive fields;
  their JSON defaults reproduce the old hard-coded 0.60 master + unity), exposed as
  click-and-drag sliders in the pause menu. The card went **two-column**: the old single stack
  was already 771 px inside an 800 px window with nowhere to put them. A cue's bus reuses the
  audio budget's own `CatOf` map, so "what counts as UI" is one decision in one place.
- **Master limiter** on the mixed bus via `AttachAudioMixedProcessor` (peak follower, ~1 ms
  attack / ~150 ms release, cubic soft-clip behind it). A1 bought stack headroom out of the cue
  targets; that is a budget, and it stops being true the moment the owner raises the master.
- **Voice pool** — 6 `LoadSoundAlias` voices per cue, round-robin with oldest-steal. `Play` used
  a single shared `Sound`, so two enemies firing the same weapon truncated each other *and*
  `SetSoundPitch`/`SetSoundPan` mutated an already-playing shot mid-flight.
- **Real variation** — the old `_pitchSeq & 7` produced 0.940, 0.957 ... 1.060 and wrapped: a
  monotone rising glissando, perceptually a siren. Now hash-scrambled off a counter (still
  deterministic — the harness needs it), with a sensible per-category default pushed into `Play`
  so all ~134 call sites get variation for free, and ceremonial stingers opted out. Gain jitter
  is **one-sided downward** (2.4 dB): symmetric jitter would let a cue land hotter than the level
  the peak targets were budgeted against.
- **Stinger ducking** — `PlayStinger` ducks the bed (fast in, slow out) instead of playing over it.
- **Per-tile footfalls** — `Audio.Play("move")` fired once per move *command*, so a six-tile
  sprint got one 80 ms thud. Moved into `MoveStepAnim.OnStart`, panned to the step's position.
  That is the only legal site: `OnStart` runs when an anim becomes ACTIVE, never at `Enqueue`.
  Verified with a temporary per-frame position trace over a full autoplay — **113/113** genuine
  in-path step transitions chain exactly (each step's `_from` == the previous step's `_to`), no
  snap-back. (Three apparent outliers were two *different* enemies sharing an archetype name.)

### Honest verdict on the contact sheet, and what is still wrong

Weapons stopped being flat rectangles: each is now a bright transient collapsing into a
low-frequency body, and the five are visibly different objects. The beds stopped being black.
But:

- The weapons still differ mostly by **duration and centroid, not by shape** — rifle / shoot /
  hit / crit all read as "bright wedge decaying to low". Real object-level identity would want
  convolved impulse-response bodies, which this synth has no notion of.
- A faint broadband haze remains to 20 kHz on the weapons. It is 50-70 dB down (the `>5k` column
  is 0.4-2.7%) and it comes from the sub-millisecond transient, which is physically correct — but
  it is visible on the sheet and worth knowing about.
- `select` / `over` still show a traceable harmonic ladder. Energetically the comb collapsed
  (select 12.5% -> 4.0% above 1 kHz, over 18.9% -> 5.7%); the lines survive on a panel normalised
  to its own peak with an 80 dB floor.
- `miss` disagrees between the two instruments — the C# gate reads 15.8% in 1k-5k, the Python
  contact sheet 70.2% above 1 kHz. The C# spectrum zero-pads a 140 ms cue into one 4096-pt Hann
  window and so weights its middle; Python uses 1024/256 across the whole cue. Neither is wrong;
  the sheet is the better read for short cues.
- **The beds still lean "room tone with a chord in it" rather than "music."** There is real
  breathing across the 16 s loop, but at contact-sheet scale (first 2 s) they read as a static
  striped rectangle. Whether the air/tonal balance is right is an ear call nobody in this
  sandbox can make.
- Nothing here has been *heard*. Every judgement above is spectral.

### Not reached

Nothing on the brief was dropped; items 1-10 all landed. Left open: perceptual (LUFS) weighting
of the budget, which A1 also flagged; reverb/impulse-response bodies for real weapon identity;
and on-device audition of the new mix and the four fader defaults — which needs the human.

## PROGRAM RESONANCE — WAVE V2 "LIGHT ON THE BOARD" (the overlay stops repainting the room; the re-grade)

**Goal.** Two measured problems, one of them tactical. (A) The move-range overlay was a flat
per-tile fill and it was repainting a third of the board in the friendly accent for the whole
player turn. (B) The board had no highlight tier and no deep shadow — 95% of pixels sat in the
bottom 40% of the range. (C) High ground, one of the three or four load-bearing tactical facts
in this game, was among the least legible things on screen.

### The measurement tool

`scripts/board-metrics.py` (manual, not wired to anything — **NO CI**). Two modes over the
board rect **minus the three HUD panels drawn on top of it** (x 145..1216, y 62..660 — the
roster strip, the action bar and the top bar all overlap `Cfg`'s board rect, and measuring
them reports chrome instead of board):

- `hue` — **hueL / hueR**: chroma-weighted circular mean hue of the board's LEFT third (where
  the overlay lives; the squad deploys left) vs its RIGHT third (clean board). **dHue** is the
  circular distance. **dMed** is the same distance on the *circular median* hue, which is
  unweighted and cannot be swung by a handful of saturated strokes on a near-grey floor — the
  ASH case, where the mean misreports. **cyan%** = share of board pixels at hue 175-215 with
  S>0.25.
- `luma` — Rec.601 percentiles over the same rect.

Captures are pinned with `SIGHTLINE_SEED=4242 SIGHTLINE_FORCEBIOME=0..7 SIGHTLINE_SHOT=90`
(the exact loop is in the tool's docstring). New QA hook **`SIGHTLINE_NOMOVE=1`** suppresses the move overlay entirely
so a capture pair can be measured against the *bare room* — the ground truth a convergence
claim needs. Read once at static init (no per-frame env read, no clock read).

### A — the move range is a BOUNDARY, not a wash

`Renderer.DrawMoveOverlay` was one line: fill every reachable tile with `Pal.MoveBlue` (a=60)
and every dash tile with `Pal.MoveYellow` (a=55). On an 18x11 board with a 6-10 tile budget
that is 60-120 tiles of flat colour, on screen for the entire player turn. Three costs, all
measured or provable:

1. it collapsed the biomes (see the table);
2. it smeared the **friendly-reserved** cool accent over half the room — `DESIGN.md` 3.H:
   one job per accent colour;
3. **dash-gold sat at almost exactly the hue AND value of a warm-biome plateau top**, so in
   ASH and ARID you could not tell dash range from high ground. That is a tactical read.

What shipped:
- a whisper-level **inner lift** (a=15 walk / a=6 dash, was 60/55) that is **WHITE, not cyan**.
  Mixing white into a colour preserves its HUE exactly and only drops saturation, so the mark
  costs zero degrees of biome. This was not a style choice — an alpha-**22 cyan** tint was
  measured first and it still flipped ASH (a near-neutral grey biome, S~0.1) a full **170
  degrees** to cyan, because on an almost-colourless floor a whisper of blue decides the hue.
- a **marching-squares outline** around each region: **solid** on the walk boundary, **dashed**
  on the dash ring. The two regions are separated by stroke STYLE, so the ASH/ARID
  dash-vs-plateau ambiguity closes on *shape* and survives `SIGHTLINE_CB=1` and a greyscale
  squint. Edges are drawn on each tile's own `ElevRect`, so a boundary climbing a plateau steps
  up with it instead of cutting through the wall.
- an edge **weighting**: an edge against a cover block is drawn thin and at ~a third alpha,
  full weight is reserved for edges against open-but-unreachable floor. Dropping the
  cover-adjacent edges *entirely* was tried first and it shatters the silhouette on a
  cover-dense arena — the region stops reading as a region. Kept, quiet.
- a **corner-tick lattice** (four 3px nubs per walk tile, ~0.6% of the tile) so per-tile
  granularity — "how far is four tiles?" — survives the loss of the fill.

No wall-clock reads; the class buffer is reused frame to frame (no per-frame allocation).

**Hue convergence, 8 biomes, seed 4242** (`python3 scripts/board-metrics.py hue`):

```
              BEFORE (a=60/55 fill)        AFTER (V2 boundary)      GROUND TRUTH (NOMOVE=1)
biome      hueL hueR dHue dMed cyan%    hueL hueR dHue dMed cyan%   hueL hueR dHue dMed
STEEL       199  212   13   10  75.5     208  212    4    0  80.4    209  213    3    0
ARID        104   36   68  135   3.2      41   36    5    0   1.6     40   36    4    0
TUNDRA      197  206    9    5  78.2     203  206    3    0  74.4    203  206    3    5
VERDANT     162  135   26   45  28.3     139  136    3    0   4.5    137  137    0    5
ASH         188  359  172  170  20.7      27  356   31   10   1.9     12  354   18    5
VOID        222  256   35   40   4.6     251  257    6    5   1.8    253  256    3    0
NEON        190  191    2    0  79.0     189  191    2    0  96.3    190  192    1    0
MAGMA        44   16   29  180   1.0      19   15    4    0   1.6     18   15    2    0
MEAN                44.3 73.1            ---       7.3  1.9         ---       4.3  1.9
```

The overlaid third now reads as the **same room** as the clean third, to within the arena's own
left/right asymmetry: mean dHue 44.3 -> 7.3 against a no-overlay floor of 4.3, and mean dMed
73.1 -> 1.9 against a floor of **1.9** (i.e. on the median statistic the overlay is now
indistinguishable from not drawing it at all). Warm-biome cyan coverage: ASH 20.7% -> 1.9%,
VERDANT 28.3% -> 4.5%. STEEL/TUNDRA/NEON cyan% is high in every column — those biomes *are*
cyan; their ground truth is 93/96/97%, and the old fill actually *lowered* it by painting
gold over blue floor.

### B — the re-grade (done after A, so the grade was tuned against the fixed overlay)

The muddiness was cumulative, not one bad constant: the floor mean was deliberately darkened,
cover was deliberately receded, plateau lift was +30, and the key light's positive throw was
capped at x0.16 against x0.34 shadow. Four separate "tune it down to protect unit readability"
decisions whose SUM is a flat dark plate with a few bright dots. One coordinated pass:

| lever | before | after |
|---|---|---|
| floor mean pull toward near-black | 0.16 | 0.06 |
| floor biome-tint pull / flat lift | 0.40 / — | 0.50 / +7 |
| key light throw (lit / shadow) | x0.16 / x0.34 | x0.32 / x0.45 |
| plateau top lift | +30 | +64 |
| plateau key throw | x0.14 / x0.14 | x0.22 / x0.26 |
| plateau front wall | — | -12, key x0.10 -> x0.16 |
| plateau lit lip | a0.50, 2.0px | a0.72, 2.4px |
| cover top / wall | — | +16 / -8 |
| cover key throw (wall/top) | x0.13 / x0.16 | x0.20 / x0.24 |
| cover rim alpha (high/low) | 0.13 / 0.10 | 0.22 / 0.17 |
| board AO vignette | none | 88px cubic ramp, a<=0.22, biome-tinted, **under terrain** |

The floor lift is deliberately **split** between a stronger tint pull and a small flat lift:
`Lift()` adds the same amount to R/G/B, which raises value but DESATURATES, and the floor's
biome hue is a marquee lever V1/W6 paid for. All-flat measured a 12% saturation loss; the split
brings it back to within ~10% (0.348 vs 0.385) while keeping the value.

The vignette is drawn **under terrain and units** on purpose: it darkens the floor (the
majority of board pixels, which is what the median measures) and can never dim a soldier
standing at the board edge.

**Luma, 8 biomes, seed 4242** (`python3 scripts/board-metrics.py luma`):

```
          BEFORE (base)              AFTER (V2)
biome     p5  med  p75  p90  p95     p5  med  p75  p90  p95
STEEL     38   71   79   85   90     41   60   85   98  108
ARID      46   77   86   92   97     48   70   92  106  115
TUNDRA    53   84   93  100  106     55   80  100  115  125
VERDANT   43   75   83   89   95     46   66   90  103  113
ASH       41   73   82   88   93     43   64   88  102  111
VOID      41   73   81   87   92     44   63   88  101  112
NEON      42   75   83   89   94     46   66   90  104  114
MAGMA     40   73   81   88   96     44   64   89  103  115
MEAN    42.9 75.2 83.7 89.9 95.3   45.9 66.6 90.2 104.2 114.1
```

The BEFORE median is itself inflated by the flood: with `SIGHTLINE_NOMOVE=1` the same base
build measures **p5 42.3 / med 54.9 / p75 67.0 / p90 82.2 / p95 89.8**. Either way the shape
of the result is the same — the **p50->p95 span goes from 20 (or 35 clean) to 47.5**, and the
histogram stops being two spikes at 40-50 and 70-80 and becomes a continuous ramp out to 130.
Pixels above luma 180 are unchanged at **0.11%** — the reserved band still belongs to units,
objectives and FX.

**The p95 target was 150 and this pass landed 114. That is a real miss and here is the
measured reason.** Sampled on the STEEL capture: friendly unit bodies top out at **p90 146 /
max 155**; enemy discs at max 183. At an intermediate setting (cover tops +32) the board hit
p95 116 — and cover top faces measured **mean 117 / p90 127**, i.e. *brighter than the mean
of a friendly soldier*. That is the hierarchy inversion `DESIGN.md` 3.H forbids, and it is
exactly the failure mode this wave was warned about, so the cover lift was pulled back to +16
(tops land ~108-116, p90 ~115). **Getting p95 to 150 requires 5% of board pixels above 150,
and with the unit tier peaking at ~150 there is nowhere to put them that is not a soldier.**
The prerequisite for the 150 target is therefore raising the UNIT tier into the >180 band the
grade already reserves for it — a change to `DrawUnit`, deliberately NOT made here: it is
outside this wave's surface, and it would be the third consecutive program to move this axis
without measuring the other side of it first. **Recorded as the target for whoever does it:
median ~65 (hit: 66.6), p95 ~150 (at 114, ceilinged by units at ~150).**

### C — elevation legibility

Under the old overlay a raised plateau was a ~10-luma bump *under a gold wash of the same
value*, which is why it was unreadable rather than merely subtle. A and B both help; C adds
the top lift (+64), the darker front wall (-12) and the stronger lit lip (a0.72 / 2.4px) so
the step reads as wall-dark / top-light. Verified against a stashed base build on the same
seed with `SIGHTLINE_ELEV=1`: in the BEFORE crop the plateau is invisible inside the dash
wash; in the AFTER crop it is an unmistakable raised slab. Sampled on the STEEL capture, a
plateau top now measures **mean 101** against **47 (far-corner floor) / 84 (lit floor)** in the
same room — a 17-to-54 luma step where it used to be ~10. It sits just under cover tops
(107-110: a block standing on the ground is a lit object, bare raised ground is a surface) and
well under units (146).

### Verification

- `dotnet build -c Release` — **0 warnings / 0 errors**.
- **Self-test battery 41/41 PASS** (every `SIGHTLINE_*TEST` in `Program.cs` except PAIRTEST,
  which is run separately), each under `xvfb-run` with an isolated `XDG_CONFIG_HOME`.
- **`SIGHTLINE_PAIRTEST=1` -> PASS** (byte-identical CRN legs).
- **Autoplay x5** clean — LOSE/LOSE/LOSE/WIN/WIN, no exceptions, no TIMEOUT.
- **`SIGHTLINE_BALANCE=10` identical to base.** Both batches asserted `runs=20 missions=81`;
  the two 532-line reports `diff` clean once the five wall-clock timing lines are stripped —
  same 50% win rate, same greedy/sloppy 70/30 split, same action mix, same per-perk /
  purchase / arena tables. Expected: `BalanceBatch` returns before the window is ever
  created, so the renderer is not on that path at all. The batch is the proof that nothing
  gameplay-shaped moved.
- Captures **read and judged**: all 8 biomes before/after, before/after pair sheets for
  ARID / ASH / MAGMA / VOID, an `SIGHTLINE_ELEV` plateau close-up against a stashed base
  build, a `SIGHTLINE_CB=1` pass on STEEL and ASH, and a 4-biome downscaled+blurred
  squint sheet.

**Squint verdict (honest).** Before: the eye lands on the overlay. The teal slab and the gold
slab are the largest, most saturated shapes on the board, and the soldiers are small cyan discs
sitting *inside a field of their own hue* — they do not win the squint. After: the four cyan
discs are the only saturated cyan left and they win it outright, and each biome reads as its
own room at squint distance. The one thing I watched closely is that plateau tops are now large
light shapes; they attract at squint distance on the dark biomes (VOID especially). They lose to
the units on saturation and on value (107 vs 146), and high ground *should* be noticeable, but
that is the constant I would look at first if the owner thinks terrain is shouting — it was
trimmed once already (+72 -> +64) for exactly this reason.

**Was the fill load-bearing?** Partly, and the honest answer is that the *outline alone* is
NOT a sufficient replacement. The first cut suppressed every cover-adjacent edge for a clean
look and the region stopped reading on a cover-dense arena. What carries "where can I go" now
is the **corner-tick lattice** more than the outline — the outline gives the silhouette, the
lattice gives the fill's per-tile texture at 0.6% of its ink. The inner lift is the weakest of
the three and could go to zero if the owner wants an even quieter board.

### Notes for the next wave

- **`SIGHTLINE_NOMOVE=1`** now exists. Any future overlay claim should be measured against it
  rather than against a neighbouring region of the same shot.
- **`Lift()` desaturates.** It is a clamped additive on R/G/B, so it preserves hue and kills
  chroma. Anywhere it is used to brighten a *biome-carrying* surface, pair it with a tint pull
  (the floor mean does this now) or the biome quietly leaves.
- **Do not raise cover tops past ~120 luma** while units peak at ~150. Measured, +32 put cover
  above the mean of a friendly soldier.
- **Left undone, deliberately:** the p95->150 target (needs the unit tier raised first, see
  above); `DrawThreat` / `DrawPathPreview` untouched (T2 owns them); `Display` post-FX
  untouched, so the bloom knee was NOT re-tuned against the new grade — the rim/lip alphas were
  raised on the assumption the knee is still ~0.36 luma and that is worth a look on hardware.

---

# PROGRAM RESONANCE — WAVE X1 "THE EXCHANGE" (2026-08-28, senior dev on wt-x1)

**The charter.** Make a trade take more than one shot, so cover, flanking, suppression,
morale, BRACE, the bleed-out window and the held boons all have turns in which to matter —
without turning fights into drags. Nine prior programs built a comeback economy and tuned it
for battles that lasted three and a half turns and tipped exactly once.

## The finding — re-measured on this tree tip before any lever
Fresh baseline on `2100858` (RESONANCE T1), method per FUL-13: `SIGHTLINE_BALANCE=10` per
chunk under `xvfb-run` on the **Release binary run directly**, two disjoint CRN slot sets per
rung (`SIGHTLINE_BALANCE_BASE` 0 / 10) x greedy+sloppy = **40 campaigns per rung**;
`runs=20` asserted in every chunk log before the chunk was used; `XDG_CONFIG_HOME` and
`SIGHTLINE_BALANCE_JSON` pinned into the worktree (the container is shared with other waves).
Every chunk's JSON + log is archived under `docs/measurements/x1/`.

| rung | run completion | mission win | mean turns | choices/turn | lead-swings/match |
|---|---|---|---|---|---|
| h0 | **52.5%** (n=40) | 88.0% (n=158) | 5.38 | 2.33 | 0.60 |
| h4 | **22.5%** (n=40) | 79.1% (n=144) | 5.59 | 2.90 | 0.57 |
| h8 | **10.0%** (n=40) | 71.4% (n=126) | 5.45 | 1.20 | 0.59 |

h0 reproduced FUL-13's 52.5% to the decimal, and the brief's texture numbers reproduced
exactly: choices/turn 2.33, lead-swings 0.60, Eliminate 3.59 turns. Time-to-kill was ~1.4
hits: 5.1 damage per SHOT (5.8 per hit) into an ~8 HP body.

## The lever
`Mission.HostileToughness` (flat HP surcharge) + `Mission.HostileDamageTrim` (flat points off
both ends of the band, DmgMin floored at 1), both applied in **`Mission.MakeHostile`** — the
single funnel for every hostile (rank-and-file cascade, faction rosters, Defend/LAST STAND
waves, finale retinue, mid-boss, finale boss). `Weapon.TrimBaseDamage` moves the PRISTINE base
so `ApplyMods` can never resurrect the untrimmed band. **Flat, not multiplicative**: the
one-shot victims are the 3-5 HP light bodies, and player damage grows through mods/perks while
enemy HP grows through `bump`, so a flat surcharge holds hits-to-kill near 2 at both ends of a
campaign where a multiplier would leave m1 one-shot and turn the m6 boss into a drag.

Nothing downstream went stale: `Ai.cs`'s finish bands and `Game.Autopilot`'s `ShotValue` /
kill heuristics all compare `Hp` against `Weapon.DmgMax`/`DmgMin`, so they re-price themselves.
`HEATLADDERTEST`'s NO QUARTER damage pin was the one assertion that had to move — it now
derives its reference from a trimmed reference weapon instead of a hardcoded band.

## THE ROUND TABLE — one lever per round, h0, n=40 each, `runs=20` asserted per chunk

| round | lever | compl | mis-win | mean t | Elim t | Escort t | Defend t | ch/turn | swings | verdict |
|---|---|---|---|---|---|---|---|---|---|---|
| R0 | baseline | 52.5% | 88.0% | 5.38 | 3.59 | 6.89 | 8.90 | 2.33 | 0.60 | reference |
| R1 | T **+4** / D 0 | **22.5%** | 74.1% | 6.46 | 5.75 | 10.23 | 8.50 | 2.13 | 0.79 | **BREACH −30.** The symmetry warning, confirmed by measurement |
| R2 | T +4 / D **−1** | 37.5% | 83.0% | 6.63 | 5.64 | 13.21 | 8.70 | 2.10 | 0.83 | half the ladder back; still −15, Escort dragging |
| R3 | T +4 / D **−2** | 42.5% | 84.7% | 6.89 | 5.18 | **16.00** | 8.90 | 2.29 | 0.88 | **REVERTED** — the trim's 2nd point bought ~5 pts (inside noise) and cost the worst Escort drag of the wave |
| R4 | T **+2** / D −1 | **52.5%** | 88.2% | 5.94 | 4.92 | 7.55 | 8.90 | 2.75 | 0.75 | ladder-neutral, but Elim 4.92 misses the 5-7 band |
| R5 | T **+3** / D −1 | **52.5%** | 87.2% | 5.85 | **5.59** | 6.00 | 8.90 | 2.09 | 0.84 | **SHIPPED** |

R1's collapse is the wave's central measured fact: enemy-only durability hands the enemy ~40%
more shooting turns at an unchanged 6-10 HP squad, and the squad cannot absorb it. The
give-back had to come out of hostile per-shot lethality, NOT out of soldier HP — soldier HP is
the other side of the lead metric this wave targets (lead = sum player HP − sum ACTIVE enemy
HP), so raising it would restore the pool ratio and undo the swing gain by construction.

R3 vs R2 also settled the give-back's shape: a second trim point mostly disarms the SMG
hostiles (the most common gun: 2-4 -> 1-2) without touching the Shotgun/Sniper/LMG/ELITE bodies
that actually kill soldiers, so it bought little and cost turns.

## THE LADDER — shipped state (T+3 / D−1), n=40 per rung

| rung | R0 | SHIPPED | delta | FUL-13 band | in band? |
|---|---|---|---|---|---|
| h0 | 52.5% | **52.5%** | 0.0 | 55 ±8 (47-63) | YES |
| h4 | 22.5% | **27.5%** | +5.0 | 30 ±8 (22-38) | YES (better-centred than the baseline, which sat on the floor) |
| h8 | 10.0% | **15.0%** | +5.0 | 10 ±5 (5-15) | YES, **at the ceiling** |

No rung moved by more than the ±8 dip budget, and every measured rung is inside the band —
including h4 and h8, which the baseline sat at the *edges* of. h2 and h6 were not measured
(budget); the shipped state moves the apex UP, so the untested rungs are the ones to check
first if anyone re-baselines.

## THE GATES — every one, with its number

| gate | target | baseline | shipped | verdict |
|---|---|---|---|---|
| lead-swings/match | >= 1.00 | 0.60 (h0) / 0.59 pooled | **0.84 (h0) / 0.80 pooled** | **MISSED** — +40%, 60% of the way |
| meaningful-choices/turn | >= 3.50 | 2.33 (h0) / 2.19 pooled | **2.09 (h0) / 1.84 pooled** | **MISSED and REGRESSED** — see the decomposition below |
| kill objectives 5-7 turns | 5-7 | Eliminate 3.60, Decapitate 5.05 | **Eliminate 5.30, Decapitate 5.24** | **MET** |
| nothing above ~10 turns | <= ~10 | max 8.70 (Defend) | h0 max 9.00, h4 max 8.47 (ex a 3-sample Evac cell); **h8 Escort 15.61** | **BREACHED at h8 only** |
| Defend must not grow | <= 8.9 | 8.70 | **8.78** | **MET** |
| completion rungs in band ±8 | all | h0/h4/h8 in band | h0/h4/h8 **all in band** | **MET** |
| fewer than 6 of 8 objectives at 100% (h0) | < 6 | 4 (Escort, Sabotage, Rescue, Evac) | **3** (Hack, Escort, Rescue) | **MET** (the baseline already read 4, not the briefed 6) |

## WHY meaningful-choices/turn CANNOT BE REACHED BY THIS LEVER (new instrumentation)
`meaningful-choices/turn` is an average over PLAYER TURNS, but `CountMeaningfulChoices` only
scores a soldier that is alive, able to act, carrying ammo AND holding a legal shot. The ratio
therefore conflates three different things. X1 added a read-only decomposition
(`Stats.MissionRec.ActingSoldierTurns / ArmedSoldierTurns / ArmedTurns`, a `[shot-gate]`
report line and four `decisionRichness` JSON fields; logic-identity vs the pre-instrumentation
build verified by re-running pinned chunks to identical per-slot records, plus PAIRTEST):

| state | acting/turn | armed/turn | armed-frac | turns-with-a-shot | choices/ARMED | ch/turn |
|---|---|---|---|---|---|---|
| R0 h0 | 3.85 | 1.41 | 37% | 62% | 1.71 | 2.40 |
| SHIP h0 | 3.49 | 1.42 | 41% | 64% | 1.46 | 2.09 |
| R0 h4 | 3.85 | 1.69 | 44% | 63% | 1.62 | 2.75 |
| SHIP h4 | 3.31 | 1.41 | 43% | 59% | 1.59 | 2.25 |
| R0 h8 | 2.88 | 0.82 | 29% | 44% | 1.46 | 1.20 |
| SHIP h8 | 2.42 | 0.79 | 32% | 43% | 1.42 | 1.12 |

Three things fall out:
1. **The melt hypothesis is wrong.** Roster size barely moves (3.85 -> 3.49 at h0).
2. **The lever does what it was supposed to do to CONTACT**: the armed FRACTION rises
   (37% -> 41% at h0) — more soldiers hold a live target because targets live longer.
3. **The binding constraint is `choices/ARMED-soldier-turn`, and it is ~1.5**, i.e. the
   typical armed soldier sees exactly ONE worthwhile target and banks 1-2 points from the
   post-shot positioning axis. Part (a) of the count (rival TARGETS within 12% of the best
   shot) contributes almost nothing, because *how many enemies a soldier can see at once* is
   a **map / pod-geometry** property, not a lethality property. To reach 3.5 from 2.3 the
   game needs ~2.4 armed soldiers per turn at today's per-soldier richness, or ~1.7 choices
   per armed soldier at today's contact. **Durability moves neither.** Comparing rungs makes
   the point cleanly: h4 out-scores h0 (2.90 vs 2.33) purely because heat fields MORE bodies,
   not tougher ones.
4. A second-order effect explains the small regression: with kills off the table, `ShotValue`'s
   stepped finisher bonuses (+14 / +9 / +4) stop firing and target values are separated by the
   `PriorityWeight` term instead, so rival targets cluster LESS. The instrument is not stale —
   a kill genuinely is worth more — but the metric is non-monotonic in the HP/damage ratio
   (h0 ch/turn read 2.33 at T0, 2.75 at T+2, 2.09 at T+3, 2.10 at T+4).

**Recommendation for the next wave:** meaningful-choices/turn is a *contact-density* metric.
Chase it with simultaneous-target geometry (pod placement / arena sightlines / activation
overlap), not with lethality, and quote `choices/ARMED-soldier-turn` alongside it so a
turn-count change can never be mistaken for a decision-quality change.

## PER-OBJECTIVE TURN BUDGET (pooled h0+h4+h8, n=40 campaigns per rung)

| objective | R0 turns | R0 win | SHIP turns | SHIP win | n |
|---|---|---|---|---|---|
| Eliminate | 3.60 | 93.7% | **5.30** | 81.0% | 126 |
| Defend | 8.70 | 78.6% | **8.78** | 85.7% | 77 |
| Decapitate | 5.05 | 53.3% | **5.24** | 66.7% | 66 |
| Escort | 8.06 | 80.0% | **10.42** | 70.3% | 37 |
| Hack | 3.80 | 79.4% | **3.52** | 93.5% | 31 |
| Sabotage | 3.12 | 92.9% | **3.64** | 76.9% | 26 |
| Rescue | 4.57 | 78.6% | **3.08** | 76.9% | 13 |
| Evac | 4.60 | 91.7% | **11.32** | 88.9% | 9 |

Escort's pooled 10.42 is **entirely the apex**: h0 6.89 -> **6.00** and h4 10.30 -> **6.01**
(both BETTER than baseline — the wave de-dragged Escort at the two rungs players actually
live at), against h8 7.12 -> **15.61** (n=17). Evac's 11.32 rests on n=9 and is dominated by a
3-sample h4 cell at 18.30t; h0 Evac reads 9.00 (n=4). Both are recorded, neither is tuned.

## THE ONE HONEST BREACH — Escort at heat 8
Mechanism: NO QUARTER already adds +1 body, +1 stat and +1 damage from m3 and lifts the AI
tier; add +3 HP per body and the escort march stops being able to clear its route. The squad
holds the zone and grinds (acting soldiers/turn falls to 2.42 at h8), the leashed asset waits,
and the mission runs long. It is the same cell FUL-13 already recorded as "the apex's killer"
(Escort 29% at h8) — the wave roughly held its win-rate there (41.2% -> 35.3%, n=17 each) but
doubled its length. NOT tuned, because tuning it would have meant landing an unmeasured change
after the last measured round. Two concrete candidates for whoever picks it up, in order:
1. **`SmartEscort`'s downed-squad hole** (`src/Game.Autopilot.cs`): the lone-VIP self-race
   fallback tests `!Players.Any(p => p.Alive && !p.IsVip)`, but a DOWNED soldier is still
   `Alive` — so with the whole squad bleeding out the asset neither leashes (LeashVip skips
   downed anchors) nor races; it hunkers until the timers expire. Bounded (<=3 turns) but pure
   drag, and it fires exactly in the h8 state. Add `&& !p.Downed`. This is an INSTRUMENT fix,
   so it invalidates the CRN comparison and needs its own paired re-measure.
2. **The cold-LZ gate** (`Game.EscortBeaconOk`, Chebyshev 3): the forward beacon is the
   shipped de-drag and it needs a pocket with no living non-routed hostile within 3 tiles —
   a condition that got materially rarer when bodies stopped dying to one shot. Note the
   measured caveat before spending a round on it: BEACON plant counts were **unchanged**
   between R0 and R2 (7/8 per chunk), so the gate was not the binding constraint at h0.

## VERIFICATION
- `dotnet build -c Release` — **0 warnings / 0 errors**.
- Self-test battery, 42 hooks — **all PASS** (`HEATLADDERTEST` needed its NO QUARTER damage
  pin re-derived through the trim; every other hook was green untouched, including
  COMBATTEST / AITEST / SNAPTEST / DOWNTEST / MORALETEST / PODTEST / SAVETEST).
- `SIGHTLINE_PAIRTEST=1` under `xvfb-run` — **PASS** (h0 slot0 and h4 slot1 both byte-MATCH).
- **Autoplay x10** — no exceptions, no TIMEOUT (4 WIN / 6 LOSE, max 14579 frames vs the
  20000 cap). Frame-cap hits across all measured chunks: 2 in 240 shipped-state campaigns vs
  1 in 240 baseline campaigns — same order, no new failure mode.
- Screenshots taken at m1 and m5: board, HP pips and action bar read normally. `DrawHpPips`
  already groups at MaxHp > 10, so the wider bodies stay legible with no renderer change.

## HONEST ASSESSMENT — is the fight more tactical?
Partly, and measurably so. A trade now takes about two hits instead of one and a bit
(Eliminate 3.60 -> 5.30 turns, +47%, inside the 5-7 budget; shots-per-kill 2.34 -> 3.17);
the lead flips 36% more often (0.59 -> 0.80/match); Eliminate stopped being a 95% free square
(-> 81%); and none of it cost the ladder — all three measured rungs sit inside the FUL-13 band,
with h4 and h8 better-centred than the baseline was. Defend did not grow.

What did NOT happen: the decision COUNT per turn did not rise, and the wave's own new
instrumentation says why — the number of enemies a soldier can shoot at once is set by map and
pod geometry, and lethality cannot touch it. Anyone reading `meaningful-choices/turn = 2.09`
as "the wave made the game flatter" would be reading it wrong; `choices/ARMED-soldier-turn`
(1.71 -> 1.46 at h0, 1.62 -> 1.59 at h4, 1.46 -> 1.42 at h8) is the honest per-decision read,
and the armed FRACTION went up at every rung. The fight is longer, tips more, and stopped
resolving on the alpha strike — but it is not yet *denser*, and density is a different wave.
## PROGRAM RESONANCE — WAVE C1 "VOICE" (the game finally says something)

**The finding.** SIGHTLINE ships more player-attachment machinery than most indie tactics
games: callsigns, ranks, player-editable tags, earned traits and nicknames, scars, faction
vendettas, bonds with specific squadmates, persistent wounds, a 3-turn bleed-out with
STABILIZE/revive, a memorial, a cross-run veteran reserve and a hall of fame. **And the game
never said a word about any of it.** Measured over 16 campaigns: **146 soldiers went down, 74
bled out, 22 were finished while down, 1 was revived.** ~96 dying people with names, traits and
scars, and the whole telling was a floating damage number and a name on an end-card list. Three
factions, eight biomes and a branching campaign map shipped with **zero words of world**. The
stakes were *implemented and unnarrated* — which is exactly why they read thinner in play than
in the changelog.

**The scope decision, recorded.** `docs/DESIGN.md` §1 listed *Narrative* as something the
project deliberately does not pursue. This wave amends it — see the new **§1.1 AMENDMENT — the
light frame**, which states what changed, why, and (the load-bearing half) the limits: not a
sixth pillar; no story/arcs/dialogue/cutscene; nothing the player must read to play well;
readability wins automatically; barks rate-limited by design; determinism a hard constraint.

### What shipped

| Piece | Where | Shape |
|---|---|---|
| **Briefings** | `Hud.DrawBriefCard` + `Game.BeginBriefing/UpdateBriefing` | 3 lines per campaign node: region × arena terrain, faction × its real combat rule, objective in the commander's voice. |
| **Faction dossiers** | `Codex` FACTIONS tab (3rd, after ENEMIES) | 3 paragraphs each — who they are / FIELD RULE / COUNTER — with the real `Combat` constant interpolated. `Faction.None` documented too. |
| **Region names** | `Voice.RegionName`, drawn as campaign-map column headers | 64 curated biome-true names, 8 per biome. A run's six missions always land on six distinct biomes, so a run can never repeat a region name. |
| **Barks** | `Game.Bark` at six existing event sites | first blood, a bond partner going down, a pod routing, a clutch STABILIZE, a vendetta kill, last-soldier-standing. Logged with outcome tag `VOICE`. |
| **Run epilogue** | `Hud.BuildEpilogue` → `Voice.Epilogue` | exactly 5 lines on the campaign end card, generated from the numbers the card already computes. |

### The hard constraint, and how it was met

Every measurement in this project rests on CRN pairing: two runs on the same slot seed must be
byte-identical. An earlier wave (audio) shipped synthesis that drew from `Util.Rng` and
perturbed gameplay. So:

- Region names, briefings and the epilogue are **pure `Util.Hash3` derivations of `MapSeed`** —
  zero draws by construction, and they round-trip on load with the map.
- Bark variety uses a **dedicated `Random`**, re-seeded per mission from the same hash
  (`Voice.BeginMission`). Nothing it produces is read by combat, AI, mission gen or the save.
- **`SIGHTLINE_VOICETEST=1`** proves it: it snapshots the shared stream, runs every generator,
  and requires the next 24 shared draws to be unchanged — **plus a sensitivity probe** that runs
  the same body with one deliberate `Util.Rng.Next()` and requires the check to FAIL, so the
  assertion cannot pass vacuously.
- **Mutation-verified by hand:** injecting a `Util.Rng.Next(1)` into `Voice.Roll` and lengthening
  one bark produced `RNG SEPARATION: generating voice content consumed draws from Util.Rng` and
  `BARK overflows the log (517px > 322px)`. Both restored.

### Rate limits (the barks are the risky part)

Four gates, all asserted: **(1)** never while a T1 lesson card or field tip is on screen — the
teaching layers win absolutely; **(2)** at most one bark per game turn; **(3)** never the same
speaker twice in a row; **(4)** each beat kind at most once per mission. Ceiling six lines a
mission; typical is two or three. A beat that needs a second name (BondDown) and is handed none
simply does not fire — `Game.BondPartnerOf` returns null unless a *real* bonded squadmate is on
their feet, so a bondless soldier can never draw a bond line.

### Two things the screenshots caught that the tests could not

1. **The briefing card sat on the combat log.** The card slot is the centred 760px tip/lesson
   chrome (x 260..1020); the log panel starts at x 970. In a live-fire shot the briefing was
   drawing over the ledger. Fix: **the briefing clears itself the instant `Stats.CombatLog` has
   an entry** — it is a pre-fight object, and the ledger is load-bearing.
2. **The log widening was the wrong trade.** C1 briefly widened the log 296→340 to fit barks.
   That pushes the panel *further* under the same centred card. Reverted; the barks were written
   to the historic 296 instead, and VOICETEST measures every composed line against
   `Hud.LogTextWidth` with the widest callsign (`KESTREL`) in both name slots.

### Verification (all run by hand, no CI)

- `dotnet build -c Release` → **0 warnings / 0 errors**.
- `bash scripts/qa-sweep.sh --full` → **43/43 PASS** (42 pre-existing + VOICETEST), autoplay
  ×5 clean across two sweeps + two extra runs (LOSE m3, WIN m6, WIN m6, LOSE m1, LOSE m1 — no
  exceptions, no TIMEOUT). **Count correction:** the sweep's own footer claimed "41 self-tests"
  while actually running 42 — an off-by-one that predates this wave. Counted by hand off the
  `echo -n` lines and corrected in the script rather than carried forward.
- `SIGHTLINE_PAIRTEST=1` → **PASS**.
- `SIGHTLINE_BALANCE=10` → `runs=20  missions=76`, and a `diff` of the full report against the
  pre-change baseline is **empty once the four wall-clock progress lines and the wall-time footer
  are stripped** — every table, every rate, `W:9 L:11` identical. That is the expected result for
  this wave, and it is the proof the RNG separation actually holds end to end.
- Screenshots read and judged in **both palettes**: briefing card, campaign map with region
  names, codex FACTIONS dossiers, a bark in the log mid-fight (new `SIGHTLINE_SHOTONBARK=1`
  hook — shoots 40 frames after a bark actually lands in live play), win and loss end cards.

### Hooks added

- `SIGHTLINE_VOICETEST=1` — the content + RNG-separation contract (in `scripts/qa-sweep.sh`).
- `SIGHTLINE_VOICEDUMP=1` — print every text type Voice generates (regions, briefings, dossiers,
  all bark variants, four epilogue shapes) so the COPY can be read and judged as prose without
  walking six missions. Window-free, device-free, changes nothing.
- `SIGHTLINE_SHOTONBARK=1` — pair with `SIGHTLINE_AUTOPLAY=1`; screenshots live play once a bark
  is in the ledger, instead of guessing a frame number.
- `SIGHTLINE_CODEXTAB=2` now frames the new FACTIONS tab.
- `Program.LoadGameFonts()` extracted from `Main`'s inline block so a self-test hook can bake the
  real atlases and measure real glyph widths. Behaviour on the normal launch path is unchanged.

### Keyboard keys

**None claimed.** The briefing is dismissed by *any* key or click (a passive read — the click
still does its normal job), so the wave needs no binding of its own.

### Honest verdict on the writing

Good, not great, and deliberately small. The strongest lines are the epilogue's third slot (the
named death with its region and kill count — the sentence this whole wave exists for) and the
faction FIELD RULE lines, which are load-bearing information wearing a voice. The briefing's
opposition line is the weakest: it does real work but three of them are structurally identical
("X ground: a, b, c. <rule>."), which will read as a template by the fourth run. The barks are
short enough to survive repetition but there are only three variants per beat; a second pass
should widen the pools before it widens the beat list. Full sample in the wave report.

### Left undone

- **Skirmish / Daily / Last Stand get no briefing.** Regions and operation numbers are campaign
  vocabulary and those modes carry no `MapSeed` route. A one-line variant is cheap if wanted.
- **Bark pools are 3 deep.** Widening them is pure content work with a test already in place.
- **Region names are decoration, not information.** They label the map but nothing keys off them
  (no per-region modifier, no returning to a region). That is the honest scope of a *frame*.
- **No epilogue for a run abandoned mid-campaign** — only the Win/Lose end cards narrate.

## PROGRAM RESONANCE — W5 "ON-RAMP" (RECRUIT rung + comfort controls)

**The finding.** The game had learned to teach (milestone 1's training op + JIT tips) but still
had no difficulty below standard and no comfort controls. `Heat.Min` was 0, so the dial only went
UP; the tuned bot cleared ~52-55% of heat-0 campaigns and a wipe before mission 3 ended the run
outright. There was no animation-speed control and no UI text scale.

### A — the RECRUIT rung (shipped)

`Heat.Min` is now `-1`, and rung -1 is **RECRUIT**. It ships as a **range extension**, not a
table change: `Heat.Mods` is untouched, no enum moved, and only the chosen LEVEL is persisted, so
`SAVETEST`'s golden FNV enum fingerprints are unaffected (verified — SAVETEST PASS).

What it does, all through the existing plumbing:

| Lever | Where |
|---|---|
| -1 hostile per mission, -1 HP / -1 aim force-wide | `Heat.RecruitMod`, via the normal `EnemyDelta`/`StatDelta` accessors |
| bleed-out clock 3 -> 5 turns | `Game.DownedTimerTurnsNow` (the `DownedTimerTurns` const stays the baseline) |
| the one-time REINFORCEMENTS checkpoint opens at mission 1 | `Game.TryReinforcements` |
| no intel bonus, no heat-ceiling unlock | `Heat.IntelBonus` returns 0 at n<=0; the unlock check is `>= UnlockedHeat`, which -1 never clears |

One real bug was found on the way: the **early-mission heat grace** (`m1 x0, m2 x1/2`) would have
zeroed RECRUIT's relief on exactly the mission a first-timer meets first. It is now gated on
`heat > 0`, so heats 1-8 are bit-for-bit unchanged and the relief applies from m1. `ONRAMPTEST`
asserts the built mission at BOTH m1 and m3, and the assertion bites (deliberately re-broken: it
reported `m1:count 5 vs 5`).

**MEASURED (the wave's real gate).** Paired flywheel, CRN slots 0-19, greedy+sloppy, two N=10
chunks per leg (`SIGHTLINE_BALANCE=10` x `SIGHTLINE_BALANCE_BASE={0,10}`):

| rung | completion | greedy | sloppy | avg missions cleared |
|---|---|---|---|---|
| heat 0 | **55.0%** (22/40) | 60% (12/20) | 50% (10/20) | 4.65 |
| RECRUIT (-1) | **75.0%** (30/40) | 75% (15/20) | 75% (15/20) | 5.55 |

+20 points, and the **sloppy** (human-error) policy gains the most: 50% -> 75%. That is the
on-ramp working as designed — it forgives mistakes rather than lowering the ceiling.

**Copy.** The intro card is now a **DIFFICULTY** picker (RECRUIT - 0 - 8), green at RECRUIT and
red above 0. RECRUIT prints as a WORD, never "-1" (a negative reads as a penalty, not a name), and
its three relief lines name real mechanics. Heat 0's hint is now "standard difficulty - the
designed fight" so the two are tellable apart at a glance. A green RECRUIT chip rides the top bar
in-mission, the barracks subtitle, and the run-end DIFFICULTY slab.

Also removed: a vestigial "HEAT" caption at `x+18,y+48` on that card — the minus stepper is drawn
over that exact rect, so it had never been visible; it only surfaced at RECRUIT, where the
disabled stepper is 40% opaque and the word bled through a button.

### B — animation speed (shipped)

`Game.AnimSpeed` already existed as an undocumented `[F2]` toggle with no persistence and no UI.
It is now a real setting: `Display.AnimSpeedLevels = {1x, 1.5x, 2x, 3x}`, persisted in
`display.json`, cycled from the pause menu (or `[F2]`).

The landmine was respected exactly: **only `dt` is multiplied**, at the single existing site
`a.Update(this, t * AnimSpeed)`. No activation is skipped, nothing bypasses the queue, and
`Anim.OnStart` still fires only when an anim becomes ACTIVE.

`Game.AnimSpeed` **hard-pins 1x under `AutoPlay || NoPersist`**, and `Display.Init(false)` never
`Load()`s, so a headless process cannot pick a speed up off disk either.

**Filmstrip evidence** (`SIGHTLINE_LONGMOVE=1` stages a straight multi-tile walk;
`SIGHTLINE_ANIMSPEED=<x>` names the speed; the loop dumps the unit's tweened `Pos` every frame):

| speed | frames | travelled | arrived at frame | backwards steps |
|---|---|---|---|---|
| 1x | 48 | 253.5px | 32 | **0** |
| 1.5x | 48 | 250.4px | 20 | **0** |
| 2x | 48 | 246.1px | 16 | **0** |
| 3x | 48 | 233.8px | 12 | **0** |

All four end at exactly the same board position (`y = 456.000`), so every step activated and no
step snapped back. The visual filmstrip agrees.

**Harness isolation, proved by construction.** `SIGHTLINE_BALANCE=10` was run on this branch and
on the integration tip: the two reports differ only in the working-directory path, and
`balance.json` is **byte-identical** (`md5 754432d8abca6db0ac82bf904b92eaf6`).

### C — UI text scale (shipped)

`Display.UiScaleLevels = {90%, 100%, 110%, 120%}`, persisted, in the pause menu, applied at ONE
place: `Cfg.Text` / `Cfg.Measure` / `Cfg.TitleText` / `Cfg.TitleMeasure`. Measure and draw share
the multiplier by construction — which is the whole answer to V1's "several sites measure via a
wrap/clip/centre helper and draw separately" gotcha.

Two deliberate design decisions:

- **The scale TAPERS with size** (`Cfg.Scaled`): full multiplier at <=18px, eased to 1.0 by 40px,
  identity above. The readability problem is the 11-14px label layer; the 40-92px headline layer
  is already legible, lives in fixed-size cards, and is what overflows first.
- **The atlas routes on the AUTHORED size**, not the scaled one, so body text keeps coming off
  V1's crisp 20px UI bake instead of falling past the 18px cliff onto the 64px atlas.

Reflow actually needed (found by screenshot, not by reasoning):

- **Roster chips** — the role tag is drawn on the chip's last row at `y+45` in a fixed 58px box,
  so at 110/120% its baseline crossed the border. `Hud.ChipH`/`ChipPitch` now grow with the scale
  (exactly 58/64 at <=100%).
- **Shop cards** — the effect line was drawn with NO width limit and at 100% already stopped a
  couple of px short of `[ BUY ]`; any scale drove it straight through. It now reserves the
  measured right column and clips. Row pitches inside the card go through `Hud.TextRow(...)`
  (identity at <=100%), and the card grows a few px.

Everything is identity at 100%, and `Display.Init(false)` means `Cfg.UiScale` is 1.0 in every
headless path — screenshots, self-tests and the flywheel all measure the authored layout.

Verified at 4 scales x 2 palettes (`SIGHTLINE_UISCALE=<idx>` + `SIGHTLINE_CB=1`), plus the
barracks and requisition screens at 120%.

### D — key rebinding: NOT DONE

Deliberately dropped on scope. A remap layer means routing ~40 `Raylib.IsKeyPressed` sites in
`Game.cs` through an indirection — the exact opposite of the surgical touch `Game.cs` needs while
it is shared with two other live waves — plus a remap surface and a persisted map. It is the
least valuable of the four and was traded for finishing A, B and C properly. **Open, ready to
dev.** Free keys remain `I J O Q U Z`; this wave claimed **none** (`[F2]` was already bound to the
animation-speed cycle and keeps that job).

### New / changed harness hooks

- `SIGHTLINE_ONRAMPTEST=1` — the wave's self-test (in `qa-sweep.sh`, which is now **42** tests).
- `SIGHTLINE_LONGMOVE=1` (shot) — stage a straight multi-tile walk and dump `FILM <frame> <x> <y>`.
- `SIGHTLINE_ANIMSPEED=<x>` (shot **or** autoplay) — name the playback multiplier. Autoplay is
  included so the smoke test can be re-run at the fastest setting; `BalanceBatch` has its own
  `Main` branch and never reaches it.
- `SIGHTLINE_UISCALE=<idx>` (shot) — photograph the UI at a text size other than 100%.
- `SIGHTLINE_HEAT` now accepts `-1` (it used to ignore anything `<= 0`), so the intro DIFFICULTY
  card can be photographed at RECRUIT. Unset/0 is still a no-op.

### Left undone / watch list

- **Key rebinding (D)** — see above.
- At **120%** the shop's one-line effect summary clips on the longest rows (e.g. "counters
  SYNDICATE for one …"). The full sentence is still in the card's description above it, so no
  information is lost, but a two-row card at large scales would be the proper fix.
- The **Defend wave** heat ramp (`SpawnDefendWave`'s own `m1 x0 / m2 x1/2` grace) is NOT gated on
  `heat > 0` the way `SetupMission`'s is, so RECRUIT's -1 stat does not reach Defend waves on
  missions 1-2. Cosmetically inconsistent, measured as immaterial; left alone rather than widen
  the diff in a file three waves are touching.
- RECRUIT is selectable in SKIRMISH too (the dial floor moved with `Heat.Min`), because the intro
  seeds `SkirmishHeat` from `PendingHeat` and a dial that snapped back to 0 would silently discard
  the player's choice. The owner docket's "skirmish numeric heat" question is untouched.
## PROGRAM RESONANCE — WAVE P1 "PRESENTATION" (the post chain; the meta layer stops looking like a spreadsheet)

Branched from the integration tip `764055a` (V3 SURFACES) on `wt-p1`. Two parts: the post-FX
chain in `src/Display.cs`, then the strategic-layer screens in `src/Hud.cs`. Presentation only —
no gameplay, no data, no persisted field.

### Part A — the post-FX chain (`src/Display.cs`)

**The bloom is now two-pass and half-res.** It was one 12-tap radial ring at 5px, computed at
full res inside the composite. It is now a 3-pass chain built before the composite:

| pass | shader | target | work |
|---|---|---|---|
| 1 | `FsBrightSrc` | 640x400 | per-tap soft threshold, then a 4-tap box downsample |
| 2 | `FsBlurSrc` | 640x400 | separable gaussian, horizontal (5 fetches = a 9-tap kernel) |
| 3 | `FsBlurSrc` | 640x400 | the same, vertical, back into buffer A |

That is **~5.6M texel fetches against the old ~12.3M**, and the outer tap now reaches ~10.3
full-res px against the old 5px ring — cheaper *and* wider, which is the whole point.

**The knee did NOT move.** It is still `smoothstep(0.36, 0.85, luma)` squared, and — critically —
it is still applied **per tap, before** the box average. Averaging four pixels first and *then*
thresholding would have dropped V3's 1px cover rims below the knee and quietly deleted them.
Checked against V3's own board metric (`scripts/board-metrics.py luma`, seed 7, mission 1,
post-FX forced on, resting bloom):

| | min | median | p95 | max | >180 band |
|---|---|---|---|---|---|
| base `764055a` | 9 | 59 | 114 | 255 | 1.05% |
| P1 | 8 | 59 | 117 | 254 | 1.19% |

Median holds exactly; p95 +3; the >180 band that V3 reserved for unit rings **grows** 1.05 ->
1.19% rather than shrinking. Read at 2x on the same tile block: the old halo had a visible hard
ring edge at 5px (the kernel's outer tap showing through); the new one is a smooth falloff, and
the cover-block rims and lips are pixel-for-pixel the same shape. Nothing blew out.

Bloom **amount** was retuned because a wide gaussian conserves energy over ~4x the area, so the
peak off a small source drops: `0.5 + uBloom*1.7` -> `1.45 + uBloom*4.30`. Three settings were
measured (1.30/4.20, 2.05/5.60, 1.45/4.30); 1.45 is the one that keeps the ring cores crisp
instead of veiling them.

**A tonemap, but an honest one.** The brief asked for "ACES-ish". A FULL-RANGE Narkowicz ACES is
the wrong tool here and the arithmetic says so: it expects scene-linear input, and against our
already display-referred frame it maps the board median (0.26) to **0.39** and white to **0.80** —
it washes the dark board out *and* dims the UI, undoing V2's re-grade. What shipped is the real
ACES curve blended in **only over the 0.85..1.60 luma band** (`smoothstep(0.85,1.60,luma)*0.75`),
against the clamped frame. Below 0.70 luma the output is bit-identical; a blown bloom core stops
clipping to a flat white disc and gets gradation back. Measured cost: frame max 255 -> 254.

**Film grain and scan.** Grain is a 256x256 `GenImageWhiteNoise` tile generated at `Display.Init`
(repeat-wrapped, point-filtered, **zero committed bytes**), alpha 0.025, scrolled from `uTime` and
faded out below 0.30 luma so the black board floor and the letterbox stay clean. Scan is a 3px-period
cosine at 0.028 amplitude. Both are driven by `uTime`, which `Display.AdvanceTime(dt)` accumulates —
**no new `Raylib.GetTime()` read** (the count is unchanged at 59).

**Everything stays behind `Display.Enabled`.** A plain `SIGHTLINE_SHOT` run logs exactly **one**
`Program shader loaded` line (raylib's default); the P1 chain would add three. Verified twice.

**A real bug found on the way:** `SetShaderValueTexture` was being called from `UploadFxUniforms`,
*before* `BeginShaderMode(_fx)`. `BeginShaderMode` flushes rlgl's batch, and that flush zeroes the
active-texture-slot table — so `uBloomTex` read black and the whole bloom silently vanished (two
consecutive tunings produced byte-identical metrics, which is what gave it away). The binds now
happen in `BindFxSamplers()` immediately after `BeginShaderMode`.

### Part B — the strategic layer (`src/Hud.cs`)

**Campaign map — a theatre of operations, not a debug graph.** Ground first: alternating per-region
column bands, hairline dividers, and four seeded contour lines from `MapHash` (pure arithmetic off
`MapSeed` — no `Random` allocation, no RNG draw, so the seeded campaign cannot desync). Routes are a
dark casing under a coloured core, with a direction chevron at the midpoint of the edges you can
actually take. Nodes replace their single letter with `DrawNodeIcon` geometry in the codex's own
vocabulary: launch chevron / crosshair / depot cross / warning delta / choice fork / boss diamond.
The current node gains corner brackets. The legend now draws the map's **real** markers instead of
stand-in letters, so `NodeGlyph` has no caller and is deleted.

**WAR ROOM — the L-shaped void is gone.** The three content-sized columns ended at three different
heights above ~25% of empty screen with BACK floating alone in it. A full-width **CAREER** footer
(7 stat cells: runs / wins / win rate / best mission / best wave / veterans / daily streak) now
grounds them on a common baseline, the columns are capped so they always clear it, and BACK sits
under the footer. The cramped 13px lifetime run-on that used to hide under the title moved into
the footer as real cells; every value and label routes through `FitSize`, so 120% fits.

**Victory card — five accents become two.** Green / cyan / blue / red / gold collapse to neutral
chrome plus the card accent on exactly one headline slab (MISSIONS CLEARED). DIFFICULTY stops being
red — a high heat is the most impressive number on the card, not a warning — and gains a filled /
hollow **rung-pip strip** under the numeral, so the heat played reads by shape and survives
`SIGHTLINE_CB=1`.

**Event card — sized to its content, and risk is telegraphed.** The card reserved a constant that
left ~82px of dead slab under the last option (`150 + ... + 30` against a real content bottom of
`98 + ...`); it is now `118 + ...` for a 22px pad. Each option carries a risk tier derived **in Hud**
from the outcomes the choice already holds — no change to `Events.cs`, nothing new persisted — and
signals it three ways: a coloured left rail, a drawn ring mark (pip / minus / cross) and a word
(CLEAR / COST / GAMBLE / WALK AWAY). Shape carries it, so CB is a no-op.

**Shop / armory — icons.** `DrawShopIcon` transcribes simple geometry into the existing
`DrawActionIcon` primitives: aid cross, ampoule, rank chevrons, grenade, shield, shield-with-slash,
crosshair. **Nothing was fetched or committed.** The slate card widens 760 -> 808 to pay for the
24px gutter so the text column keeps *exactly* its prior width (366-28-24 == 342-28) — verified
against a base capture at 120%, where both builds clip the same three desc lines identically.
The ARMORY borrows the codex's own class silhouette (`Renderer.DrawCodexGlyph`) for soldier rows
and adds a per-`WeaponKind` receiver mark to weapon rows.

### Verification

- `dotnet build -c Release` -> **0 warnings / 0 errors**
- `bash scripts/qa-sweep.sh --full` -> **45 self-tests ran, 0 FAIL**, PAIRTEST PASS, no
  COVERAGE GAP block, autoplay x3 inside the sweep clean
- `SIGHTLINE_PAIRTEST=1` -> **PASS** (h0 and h4 legs both MATCH)
- autoplay x5 -> WIN/WIN/WIN/LOSE/LOSE, no exception, no TIMEOUT
- `SIGHTLINE_BALANCE=10` -> **runs=20 missions=69**, **byte-identical to base `764055a`** (0 diff lines after stripping the wall-clock stamps and the worktree path) — the proof this wave is presentation-only
- plain `SIGHTLINE_SHOT` -> **one** shader program loaded (raylib's default): post-FX off
- captures read and judged: post-FX on/off on seed 7 at 1x and 2x; every touched meta screen
  before/after; the whole set again under `SIGHTLINE_CB=1` and again at `SIGHTLINE_UISCALE=3`
  (120%); plus a base-vs-P1 120% shop capture to prove the gutter cost nothing

### Honest verdict / left undone

- **Better:** the bloom is genuinely nicer *and* cheaper — the old 5px ring had a visible hard
  edge that is simply gone. The campaign map went from "debug graph" to somewhere. WAR ROOM and
  the victory card are both calmer and read faster. The event card telegraphing risk is the
  change most likely to alter how someone plays.
- **Judgement call, stated plainly:** the tonemap is a *shoulder*, not a full-range ACES, and it
  is deliberately weak (frame max moved one value, 255 -> 254). A stronger filmic look is available
  but costs the dark board its contrast, and V2/V3 spent two waves earning that contrast.
- **Slightly worse:** the wider bloom veils the very core of a bright ring a hair more than the
  tight 5px kernel did. Three amounts were tried; 1.45 is the best trade found, but it is a trade.
- The HALL OF FAME column is short, so a gap remains between it and the CAREER footer. Filling it
  needs more content in that panel, not more chrome.
- **Not reached:** nothing in the brief was skipped. Not attempted beyond it: the boon-offer and
  perk-chooser cards were left alone (they were not in the art-direction findings), and the
  ARMORY weapon marks are the weakest of the new icons — rifle and SMG are differentiable but
  not instantly so at 20px.

## PROGRAM RESONANCE — WAVE V3 "SURFACES" (cover as material, biomes as places, silhouettes)

**Branch** `wt-v3` off the integration tip. Owner: V3. Files touched: `src/Renderer.cs` only.
`DrawThreat` / `DrawPathPreview` (T2) untouched; the V2 move-overlay boundary work extended in
no way and its hue table re-run to prove it.

### What was wrong (measured, not asserted)

1. **Cover was a whitebox widget.** Two variants (high/low), identical geometry every tile:
   same 5px inset, same 0.18/0.22 corner radius, same lift, same `△`/`—`. Forty-five clones.
2. **Cover never joined its biome.** The tint pull was 0.28 over a strongly slate base. Exact
   from the colour math: six of eight biomes' cover tops sat at hue 190-235 — *blue* — no
   matter the room. ARID cover: hue **204, saturation 0.03**. MAGMA: hue 320, sat 0.05.
3. **Biomes had no terrain structure.** `DrawBiomeSignature` ran per tile and drew everything
   *inside* that tile, so MAGMA was a field of ~45 identical orange squiggles rather than a
   fissure that goes somewhere.
4. **Team rode on hue alone.** Player ASSAULT and enemy GRUNT were the same wedge 1px apart;
   GRUNT/SCOUT/HUNTER differed by 2px of half-width. Dormant contacts were near-invisible.

### What shipped

**A — cover as material.** A per-biome `GenImageCellular(256², biome cell size)` baked once on
the CPU (**no committed bytes**), `ImageColorInvert`ed at bake, drawn through a light biome
stone on the cover top and wall. Purely-visual footprint jitter (±3px), hash-picked corner
radius 0.12–0.32, ±1.2px lift jitter, and a chipped corner on ~35% of tops. Tint pull
0.28 → **0.55**. Cover tops are **value-targeted** (new `Renderer.LiftTo`) rather than
flat-lifted.

> **The bug worth writing down.** The first cut drew a *dark* colour through the *un-inverted*
> field, on the theory that the bright cell boundaries would come out as grout. They did not:
> a texture scales the colour you draw **with**, never the surface underneath, so a dark colour
> can only ever lay down a flat wash. Measured, cover-top interior std *fell* 3.63 → 1.46 vs
> the old build — the pass made cover **flatter**. Inverting the field and drawing a light
> stone through it puts the structure back: std 3.5 → **5.1 (ASH) / 5.5 (TUNDRA)**.

`GenImageCellular` seeds its cells from raylib's global `rand()`, which `InitWindow` seeds from
the clock. Left alone that makes every screenshot differ. `Raylib.SetRandomSeed` is pinned per
biome before the bake; nothing else in the game reads raylib's RNG. Verified: two identical
capture runs differ **only** in the pre-existing animated elements (units, barrel, objective
pip) — the diff map over floor, cover and features is empty.

**B — board-scale features.** `DrawBiomeFeatures`: 6–12 features per mission that span tiles —
fissure + pool (MAGMA), frost drift (TUNDRA), soot fan (ASH), dune ridge (ARID), lattice trunk
(VOID/NEON), moss patch (VERDANT), plate seam (STEEL) — drawn under the terrain. Everything
derives from `Run.MapSeed` through `Util.Hash3`: **no `Random`, zero draws from `Util.Rng`**,
built once per (seed, biome, grid) into fixed-size static buffers, so the per-frame cost is the
draw only and there is no allocation. Time-varying pulses reuse `DrawBiomeSignature`'s single
existing `GetTime` read — **no new `Raylib.GetTime()` calls**. MAGMA's per-tile squiggle drops
from 40% to 16% of tiles now that a real fissure carries the structure.

*Colorblind:* V2 caught MAGMA's veins landing on the CB foe orange (238,138,40). A board-scale
version of that hue would be worse, so under `SIGHTLINE_CB` the fissure gives up saturated
warmth and works in **value** (dark crevasse, pale hot core), and the surviving per-tile vein
goes to a dull brown with a near-white core. Terrain wearing the CB-foe hue band on MAGMA:
**0.83% → 0.61%** of board pixels; ARID unchanged at 0.37% (its ridges are below the
saturation threshold).

**C — silhouettes.** A **team chassis carried by topology**: player = a closed ring doubled by
an outer hairline; enemy = a ring **broken** into three arcs with three notches (the four
special enemy rings — TURRET square / BRUISER hex / SCOUT dash / banner diamond — get the same
three notches). A gap survives greyscale and `SIGHTLINE_CB`; a hue does not.
GRUNT / SCOUT / HUNTER re-cut by **topology**: solid wedge / hollow wedge with a sensor pip /
twin chevrons with no body. ASSAULT gains a shoulder bar so it cannot be read as a GRUNT.
Every unit gets a dark **keyline** contour (value contrast, palette-free) and a white specular
catch on the upper-left. Dormant contacts: pale slate body, tighter dashed ring, and a **dark
backing arc under each dash** so the read no longer depends on which biome the pod is standing
in.

*Codex sync.* The HUNTER's twin chevrons used to be drawn in `DrawUnit` only, on top of a plain
dart silhouette — so the field manual showed a dart and the board showed a dart with chevrons.
They are now the silhouette itself. `DrawCodexGlyph` also wears the team chassis (at scale
≥ 1.2 only: the 0.8–0.95 row glyphs sit in 26px rows beside their own label).

### The grade — what moved, and what deliberately did not

V2 hit board median 66.6 but missed its p95 150 target at 114, and recorded the real blocker:
cover top faces were level with soldier bodies, so the prerequisite was **raising the unit tier
into the >180 band the grade reserves**. That was this wave's job and it is done:

| | base | V3 |
|---|---|---|
| unit ring stroke (analytic, Friend/Foe/Foe-CB) | 153–156 | **179–182** |
| specular catch over the ring | — | **215** |
| board pixels above luma 180 | 0.06–0.15% | **0.49–0.66%** |
| worst possible cover-top pixel (high cover, cell face, klit=+1, +grain) | ~142 | **~149** |
| cover-top luma spread across the 8 biomes | 12 | **0** (value-targeted) |

Board median is **identical to base** on all eight biomes; p95 is within ±2 (106/114/124/111/
110/110/112/112 → 107/114/126/111/110/110/112/111). **The grade itself was not moved.** Cover
tops were re-targeted only to pay back what the cellular pass costs and to put the eight biomes
on one rung; the ceiling was chosen from the constraint, not from taste — the brightest pixel a
cover top can produce must stay under a soldier's body fill (153), which with the key light's
0.24 gain works out to a flat base of ≤114, hence targets of 112/100 pre-grout, 92/80 with the
lifting cellular pass.

**The p95 = 150 target is still unmet and was not chased.** Cover cannot get there: a 5-luma
board-p95 gain costs roughly 10 luma on cover tops, and ~35 more luma would put terrain through
the soldier body. p95 150 needs either a brighter floor mid-tone or more high-value *area*
(plateaus), which is a floor/grade decision, not a surfaces one. Three programs have now pushed
on this axis; V2 stopped on evidence and so does V3.

### Verification

- `dotnet build -c Release` → **0 warnings / 0 errors**
- `bash scripts/qa-sweep.sh --full` → **41/41 PASS**, 0 FAIL (PAIRTEST included)
- autoplay ×5 → clean, no exceptions, no TIMEOUT
- `SIGHTLINE_BALANCE=10` → **runs=20 missions=76**, output **byte-identical to base**
  (0 diff lines after stripping wall-clock stamps) — the proof this wave is visual-only
- V2's hue-convergence table re-run (`scripts/board-metrics.py hue`, seed 4242, 8 biomes):
  no regression — dHue equal or better everywhere (ASH 30 → 24, dMed 10 → 5), cyan% within
  1.5pp (VERDANT improved 4.5 → 2.1)
- captures read and judged: 8-biome before/after (with and without `SIGHTLINE_NOMOVE`), cover
  close-ups at 3–6×, the board-scale features, `SIGHTLINE_ALERT` silhouettes, `SIGHTLINE_CODEX`
  glyph strip, and `SIGHTLINE_CB=1` on MAGMA and ARID with live hostiles

### Honest verdict / left undone

- **Better:** every biome now has a floor *and* cover of its own colour; terrain has structure
  that goes somewhere; friend/foe is readable with the colour turned off; GRUNT/SCOUT/HUNTER are
  three different things; dormant pods are findable.
- **Worse, and it is a real trade:** on VERDANT and VOID, cover now sits closer to the floor in
  hue and value than it did when it was slate, so the blocks *pop* slightly less. Value
  separation still carries it (cast shadow + lifted top + rim), but a room that reads as one
  material is a room where cover is less shouty. If that costs tactical reading in play, the
  lever is the tint pull (0.55) and the cover-top targets (112/100), both in one place.
- TUNDRA's drift lobes read slightly bubbly at 1:1 (overlapping discs). Fine at play distance;
  a noise-warped outline would fix it properly.
- The dormant-contact brightness is a judgement call: the first tune had them out-shouting the
  live foe and was pulled back twice. They are now clearly findable and clearly subordinate.
- MAGMA in colorblind mode is improved but not solved: the floor hue is fundamentally warm, so
  a CB foe still shares a hue family with the room. Value (keyline + >180 ring) is what carries
  it now. Fixing it at the root means moving MAGMA's floor hue, which is a biome decision.
- Not attempted: cellular material on the plateau tops (they are still Perlin-only), and any
  floor-tier grade move.
# PROGRAM RESONANCE — WAVE W4 "THE SECOND AXIS" (2026-08-28, senior dev on wt-w4)

**The charter.** X1 proved the binding decision-density constraint is
`choices/ARMED-soldier-turn` — the typical armed soldier sees exactly ONE worthwhile
target — and named the cause as map and pod GEOMETRY, not lethality. Underneath it sat a
structural fact nobody had touched: `Mission.PlayerSpawns` puts the squad in cols 0-3,
`EnemyPodColOffset` puts every pod in cols 14-17, and the evac zone is always the right
edge. **35 arenas x 8 objectives x 4 modes, and every single fight opened as a
left-to-right push.** W4's job was to make the opening geometry a variable and see whether
simultaneous target presentation follows.

## Fresh baseline on THIS tree tip (measure first, never inherit a number)
Method per FUL-13/X1: `SIGHTLINE_BALANCE=10` per chunk under `xvfb-run` on a **snapshot of
the Release binary** (so the tree can keep building while a round is in flight), two disjoint
CRN slot sets (`SIGHTLINE_BALANCE_BASE` 0 / 10) x greedy+sloppy = **40 campaigns per rung**,
`runs=20` asserted per chunk. Every chunk's JSON + report extract is archived under
`docs/measurements/w4/`.

| rung | run completion | mission win | mean turns | ch/turn | **ch/ARMED** | lead-swings |
|---|---|---|---|---|---|---|
| h0 | **32.5%** (n=40) | 78.4% (n=125) | 6.79 | 2.19 | **1.55** | 0.72 |
| h4 | **12.5%** (n=40) | 70.1% (n=117) | 6.52 | 2.46 | **1.71** | 0.64 |

**Three of the brief's numbers were already wrong on this tip, and all three matter:**
1. **The ladder has drifted BELOW its band.** X1 shipped h0 52.5 / h4 27.5; eleven waves
   later the same measurement reads **32.5 / 12.5** against the FUL-13 band 55±8 / 30±8.
   Both rungs start OUTSIDE the band, low. So "stay inside the band" was not a gate W4
   could pass or fail on its own — what it could do is not make it worse, and it in fact
   made it much better (below).
2. `choices/ARMED` is **1.55**, not X1's 1.46; `choices/turn` is 2.19, not 2.09.
3. **Two objectives already breach the ~10-turn budget at h0**: Escort **12.57t** and
   Rescue **15.33t** (X1 had left h0 Escort at 6.00). Whatever else W4 did, this was the
   drag that needed removing.

## The new instrument, and how it reframed the wave
X1 could measure that `choices/ARMED` was ~1.5 but not WHICH of `CountMeaningfulChoices`'
two axes was starved. W4 added a read-only split (`Stats.MissionRec.LosTargetSum /
TargetChoiceSum / PosChoiceSum`, a `[choice-split]` report line and three JSON fields):

* `los-targets/ARMED` — foes in range+LoS per armed soldier-turn (raw simultaneous presentation)
* `target-choices/ARMED` — axis (a): rival shots within 12% of the best
* `position-choices/ARMED` — axis (b): near-best places to stand after firing (capped 2)

Identity verified the X1 way: `R0diag-h0-b0` re-ran the *instrumented, lever-off* tree on
the pinned slot set and reproduced `R0-h0-b0` to every decimal (45.0% completion, 73
missions, ch/turn 2.61, ch/ARMED 1.76, los 2.41, tgt 0.27, pos 1.49).

**Baseline h0: los-targets/ARMED 2.42, target-choices/ARMED 0.28, position-choices/ARMED 1.27.**

That kills the wave's premise as written. An armed soldier does **not** see one target — it
sees ~2.4. What it almost never has is two targets *worth choosing between*: the metric only
counts a rival whose `ShotValue` is within 12% of the best, and three independently-rolled
archetypes differ so much in HP, gun and `PriorityWeight` that their shots are never
comparable. **The starved axis is comparability, not count** — and 82% of the score is
actually coming from the positioning axis.

## THE ROUND TABLE — one lever per measured round, h0, `runs=20` asserted per chunk

| round | lever | n | compl | mean t | ch/turn | **ch/ARMED** | los/ARM | tgt/ARM | pos/ARM | armed/turn | swings | Escort t |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| R0 | baseline | 40 | 32.5% | 6.79 | 2.19 | **1.55** | 2.42 | 0.28 | 1.27 | 1.39 | 0.72 | 12.57 |
| P1 | `DEPLOY=pincer` | 40 | **47.5%** | **5.30** | 2.49 | **1.62** | 2.50 | 0.27 | 1.35 | 1.54 | 0.72 | **5.65** |
| C1 | `DEPLOY=crossfire` | 40 | 35.0% | 6.56 | 2.09 | **1.57** | 2.44 | **0.34** | 1.23 | 1.32 | 0.74 | 13.40 |
| E1 | `DEPLOY=envelop` | 40 | **60.0%** | 5.82 | **2.71** | **1.59** | 2.45 | 0.27 | 1.32 | **1.71** | **0.78** | 9.66 |
| M1 | `PODMASS=4` | 20 | 35.0% (vs 45.0 same-slot) | — | 2.31 | **1.64** | **2.61** | 0.22 | 1.42 | 1.41 | 0.64 | — |
| S1 | **the shipped combination** (mix 3/3/1/3 + uniform pods) | 40 | 35.0% | **5.69** | 2.36 | **1.53** | 2.34 | 0.28 | 1.24 | **1.55** | **0.79** | 8.19 |
| S2 | the heavier deal (mix 1/4/1/4 + uniform pods) — **rejected** | 40 | **40.0%** | 6.56 | 2.03 | **1.53** | 2.22 | 0.26 | 1.27 | 1.34 | **0.80** | 8.30 |
| U1 | `PODUNIFORM=1` | 40 | **32.5%** (= baseline exactly) | 5.85 | 2.38 | **1.62** | 2.43 | **0.34** | 1.29 | 1.47 | 0.70 | **8.75** |

(M1 is the one single-chunk round — paired against the *same* slot set's baseline chunk
(45.0% -> 35.0%), so the comparison is exact but the sample is half. Everything else is n=40.)

## THE FINDING — `choices/ARMED` is a near-invariant of this game at ~1.6

Line the two axes up against each other across all six states:

| state | target-choices/ARMED | position-choices/ARMED | **sum = ch/ARMED** |
|---|---|---|---|
| M1 pod-mass 4 (n=20) | 0.22 | 1.42 | **1.64** |
| P1 pincer | 0.27 | 1.35 | **1.62** |
| E1 envelop | 0.27 | 1.32 | **1.59** |
| R0 baseline | 0.28 | 1.27 | **1.55** |
| C1 crossfire | 0.34 | 1.23 | **1.57** |
| U1 uniform pods | 0.34 | 1.29 | **1.62** |

Sorted by the target axis, the position axis falls monotonically (U1, the one lever that
moved both, aside), and **the sum never leaves 1.55-1.64 across five structurally different
levers** — a 6% spread against a gate that asked for +29%. The mechanism is in the
instrument: axis (b) counts destinations scoring within **15% of the BEST** safety score, and
safety is `24 − TileExposure + cover*8 + height*5`. Raise the threat — which every lever that
puts more comparable guns in view necessarily does — and the best score falls, the *absolute*
window `0.15 x best` narrows with it, and fewer tiles qualify. The two halves of
`meaningful-choices` are coupled through threat with opposite signs, so the total is close to
conserved.

This also retro-explains X1: its durability lever raised threat, lost position choices, and
`choices/turn` regressed 2.33 → 2.09 while every intuition said it should rise.

**So `choices/ARMED >= 2.0` is not reachable by a geometry or formation lever, and probably
not by any threat-side lever at all.** Two honest routes remain, and they belong to a
different wave: (1) add POSITIONING OPTIONS AT CONSTANT THREAT — a terrain-grammar pass that
puts more equally-good destinations near contact (more LOW cover, which also does not block
the sightlines axis (a) needs), or (2) re-specify axis (b) with an ADDITIVE band so it stops
reading "the fight got safer" as "the decision got richer".

## What DID move: contact breadth
`meaningful-choices/turn = ch/ARMED x armed-soldiers/turn`, and the second factor is very
movable. The baseline fields **1.39** armed soldiers per player turn out of 3.53 acting; a
surrounded opening fields **1.71** (+23%). That is the real, shippable density result: not a
richer decision per soldier, but **more of the squad in contact every turn** — ch/turn
2.19 → 2.71 at ENVELOP, +24%.

## WHAT SHIPPED

**1. Deployment SHAPE as a per-mission variable** (`src/Mission.cs`, the
`W4 THE SECOND AXIS — DEPLOYMENT GEOMETRY` block). Four openings:

| shape | squad | force | notes |
|---|---|---|---|
| `DeployFrontal` | cols 0-3 | east edge, cols 14-17 | today's opening, byte-for-byte |
| `DeployPincer` | cols 0-3 | one front pair + two flank pairs in the rim lanes (cols 12-13, rows 0-1 / 9-10) | the de-drag shape |
| `DeployCrossfire` | cols 0-3 | two dense masses on the NE and SE bearings, middle rows empty | the only shape that moved the target axis |
| `DeployEnvelop` | **board centre**, cols 7-10 | all four rims | the surrounded opening |

**The hard contract**: the shape derives PURELY from `(DeckSeed, missionNum)` through an
FNV-1a mix with an avalanche — **zero `Util.Rng` draws**, exactly like the FUL-9 arena deck,
so every CRN pairing in the project survives. `SIGHTLINE_PAIRTEST=1` PASSES with the whole
W4 surface enabled (h0 slot0 and h4 slot1 both byte-MATCH). Pods keep the historical COHESION
stack for the three directional shapes (identical placement code for FRONTAL); ENVELOP's rim
pods stack ALONG their own edge so a north-rim pod does not march into the squad's lap.

**ENVELOP is objective-gated, which is what kept the wave cheap.** A centre deployment would
trivialise any objective with placed geography, so it is legal only where there is none:
Eliminate, Decapitate and Defend. Evac / Escort / Rescue (evac zone), Hack (centre terminal)
and Sabotage (mid-field sites) always get a directional opening — so **no extraction, hack,
beacon or sabotage routing changed at all**, and `Game.EscortBeaconOk`'s far-third test
(`u.X >= Grid.W * 2 / 3`, a left-to-right assumption) is never reached by a centre deploy.

Two supporting changes, both no-ops for a frontal opening by construction:
* protective cover now faces each body's NEAREST opponent on the dominant axis. For a frontal
  opening |dx| >= 11 always beats |dy| <= 10, so it reproduces the historical `+1` (soldiers)
  / `−1` (hostiles) column exactly.
* barrels never land within Chebyshev 2 of a soldier's deployment tile — a centre-deployed
  squad would otherwise open the mission sitting next to a live barrel. The barrel bias is
  cols 6-15 and the squad is in cols 0-3 on every directional opening, so nothing else moves.

**2. Pod UNIFORMITY** — members past the pod lead field the LEAD's archetype. The per-body
`Util.RandF()` still happens (the shared stream keeps its draw count); the member just reuses
the lead's value. Applies m1-m5; the finale is excluded (explicit kit slots, FUL11PROBE
geometry). Measured **exactly ladder-neutral** (32.5% = 32.5%, n=40) for +0.06 on the target
axis, +0.19 on `choices/turn`, and Escort 12.57t -> 8.75t. It also simply reads better:
"three RAIDERS", not a trio of strangers.

**3. The `SmartEscort` instrument fix** (the brief's secondary task) — see its own round below.

## WHAT WAS BUILT AND NOT SHIPPED (measured out, or gated pending measurement)
* **`PodMass` 4** (`SIGHTLINE_PODMASS`) — bigger, fewer pods. It did exactly what it was
  supposed to do to raw presentation (`los-targets/ARMED` 2.41 -> 2.61, the largest move of
  the wave) and the target axis went **DOWN** (0.27 -> 0.22): a bigger mixed pod presents more
  bodies that are *less* alike, not more comparable shots. Paired completion 45.0 -> 35.0
  (n=20). **NOT shipped**; the code stays behind `PodMass = 3` (the FUL-6 plan, reproduced
  exactly — PODTEST pins its splits).
* **ENVELOP rim waves** (`SIGHTLINE_RIMWAVES`) — under a surrounded opening, rotate the rim
  that `SpawnReinforcements` waves arrive from, so a Defend hold-out stays surrounded instead
  of quietly reverting to an east-facing fight after the opening pods die. Deterministic (a
  per-mission wave counter, no RNG draw) and it survives PAIRTEST, but it went in after the
  round budget was spent. **Shipped OFF**, ready-to-dev with a one-flag round.

## THE SECONDARY TASK — the `SmartEscort` instrument fix, as its own paired round

X1 handed this over: `SmartEscort`'s lone-VIP self-race tests
`!Players.Any(p => p.Alive && !p.IsVip)`, but **a DOWNED soldier is still `Alive`**. So with
the whole squad bleeding out the asset neither leashes (`LeashVip` skips downed anchors) nor
races — it hunkers until the timers expire. Fixed to `p.Alive && !p.Downed && !p.IsVip`, and
because it is a *measurement instrument* defect the old behaviour is reproducible on demand:
**`SIGHTLINE_ESCORTFIX=0`** restores the broken test so the fix can be paired.

Measured on its own, `SIGHTLINE_OBJ=escort` pinned so the sample is Escort missions rather
than one-in-six of a mixed campaign, at **heat 8** (the rung X1 flagged), same CRN slot set,
same shipped mix, 20 campaigns per leg:

| leg | Escort missions | win | mean turns | loss causes |
|---|---|---|---|---|
| `ESCORTFIX=0` (X1's broken instrument) | 61 | 72.1% ±6.7 | **6.5** | VIP LOST 15, RUN OVER 2 |
| fixed (shipped) | 52 | 61.5% ±6.7 | **6.8** | VIP LOST 18, STALEMATE 1, RUN OVER 1 |

**Honest reading: the fix is neutral within noise, and it did not buy the de-drag it was
predicted to.** The win-rate difference is 10.6pp against a ±9.5pp standard error on the
difference — about 1.1 SE, not a result — and the turn count moved +0.3. What the round DOES
establish is that **X1's 15.61-turn h8 Escort cell is not present on this tip**: both legs run
~6.5 turns. The hunker-vs-race hole was never the mechanism behind that number.

The trend, such as it is, points the other way: racing a lone VIP across an h8 board usually
just gets it killed (VIP LOST 15 -> 18), so the broken predicate was accidentally playing the
safer line. **Shipped anyway**, for two reasons that do not depend on the win-rate: the
predicate is simply wrong as written (a downed soldier cannot act, and the leash already
treats it as absent), and the behaviour it produced was an unbounded hunker — the shape of a
stall, which is what the frame cap exists to catch. If the negative trend is real it is an
argument about the *policy* (a lone VIP should race only when it can actually reach the zone),
not about the predicate; that is recorded as a follow-up, not guessed at here.

## THE SHIPPED STATE — round S1, `DEPLOYMIX=3,3,1,3` + `PODUNIFORM=1`, n=40 per rung

*(One lever per round applies to the LEVER rounds — every shape was pinned and measured alone
(P1/C1/E1), and uniformity was measured alone (U1), each against R0 on the same CRN slot sets.
S1 is the COMBINATION round: the state actually being shipped, measured end to end at two rungs,
so nothing is published by extrapolating from the singles. Every round including S1 carries
`SIGHTLINE_ESCORTFIX=0`, i.e. R0's instrument, so the Escort fix cannot contaminate the ladder
numbers; it has its own pair above.)*

| rung | metric | R0 baseline | **S1 shipped** | delta |
|---|---|---|---|---|
| h0 | run completion | 32.5% | **35.0%** | +2.5 |
| h0 | mission win | 78.4% | 81.2% | +2.8 |
| h0 | mean turns | 6.79 | **5.69** | **−1.10** |
| h0 | meaningful-choices/turn | 2.19 | **2.36** | +0.17 |
| h0 | **choices/ARMED** | 1.55 | **1.53** | −0.02 |
| h0 | armed-soldiers/turn | 1.39 | **1.55** | +0.16 |
| h0 | lead-swings/match | 0.72 | **0.79** | +0.07 |
| h0 | worst objective | **15.33t** (Rescue) | **8.90t** (Defend) | **−6.43** |
| h4 | run completion | 12.5% | **20.0%** | +7.5 |
| h4 | mission win | 70.1% | 75.0% | +4.9 |
| h4 | mean turns | 6.52 | 6.31 | −0.21 |
| h4 | meaningful-choices/turn | 2.46 | **2.61** | +0.15 |
| h4 | **choices/ARMED** | 1.71 | **1.67** | −0.04 |
| h4 | armed-soldiers/turn | 1.44 | **1.57** | +0.13 |
| h4 | lead-swings/match | 0.64 | **0.68** | +0.04 |
| h4 | worst objective | 11.34t (Escort) | 11.85t (Escort) | +0.51 |

### Per-objective turn budget, h0 (n=40 campaigns per state)

| objective | R0 turns / win | S1 turns / win |
|---|---|---|
| Eliminate | 4.96 / 81% | 5.10 / 75% |
| Defend | 8.80 / 72% | **8.90 / 82%** |
| Decapitate | 5.18 / 64% | 4.68 / 60% |
| **Escort** | **12.57** / 92% | **8.19 / 100%** |
| Hack | 3.71 / 86% | 3.00 / 100% |
| Sabotage | 3.15 / 88% | 2.80 / 100% |
| **Rescue** | **15.33** / 83% | **3.40 / 100%** |
| Evac | 4.30 / 100% | 2.50 / 100% |

### The mix that was measured and NOT shipped
`S2 = DEPLOYMIX 1,4,1,4` (a much heavier PINCER/ENVELOP deal, since pinned they ran 47.5% and
60.0% completion against a 32.5% baseline). At h0, n=40: run completion **40.0%** (+7.5 over
baseline, +5.0 over the shipped mix — the best ladder number of the wave) but
`meaningful-choices/turn` **2.03**, *below the baseline's 2.19*, `armed-soldiers/turn` 1.34, and
Eliminate stretched to 7.37t with ENVELOP missions averaging 8.83t. At h4, n=40, it gives the
ladder back nothing at all — run completion **20.0%**, identical to the shipped mix — while mean
turns run **7.20** against the shipped 6.31 and `choices/turn` **2.45** against 2.61.
**Rejected**: it buys the
ladder by making fights longer and thinner, which is the opposite of the wave's charter, and
this wave has already spent two programs' worth of effort on drag — and its one real gain, +5.0
completion, exists only at h0. It is recorded in ROADMAP for whoever picks the LADDER up.

## THE GATES — every one, with its number

| gate | target | baseline (this tip) | shipped | verdict |
|---|---|---|---|---|
| `choices/ARMED-soldier-turn` | >= 2.00 | 1.55 (h0) / 1.71 (h4) | **1.53 / 1.67** | **MISSED — and shown to be near-invariant at ~1.6 under every lever tested** |
| `meaningful-choices/turn` | >= 3.00 | 2.19 (h0) / 2.46 (h4) | **2.36 / 2.61** | **MISSED** (+8% / +6%) |
| completion rungs in FUL-13 band ±8 | h0 47-63, h4 22-38 | **32.5 / 12.5 — already outside, low** | **35.0 / 20.0** | **MISSED at both rungs, but the wave moved BOTH toward the band (+2.5 / +7.5) and neither moved more than the ±8 dip budget** |
| no objective mean past ~10 turns | <= ~10 | **BREACHED at baseline**: Escort 12.57, Rescue 15.33 (h0); Escort 11.34 (h4) | h0 max **8.90** (Defend); h4 Escort **11.85** | **MET at h0** (a 6.4-turn repair), **BREACHED at h4** (Escort, +0.5 on a cell that was already breaching) |
| lead-swings/match not below 0.84 | >= 0.84 | 0.72 (h0) / 0.64 (h4) | **0.79 / 0.68** | **MISSED vs X1's published 0.84** — which this tip had already lost before W4 touched it; the wave improved both rungs (+0.07 / +0.04) |
| `SIGHTLINE_PAIRTEST` | PASS | PASS | **PASS** | **MET** — the wave's critical gate |
| autoplay x10 | no exception, no TIMEOUT | — | **no exception, no TIMEOUT; max 13484 frames vs the 20000 cap** | **MET** |

## VERIFICATION
- `dotnet build -c Release` — **0 warnings / 0 errors**.
- `bash scripts/qa-sweep.sh --full` — **44/44 PASS, 0 FAIL** at the shipped defaults (it caught
  one real defect first — see the ONRAMPTEST note below), autoplay x3 clean. PODTEST /
  ONRAMPTEST / EXPOSURETEST / PAIRTEST re-run individually after the last (signature-only)
  refactor: all four PASS.
- **`SIGHTLINE_PAIRTEST=1` under `xvfb-run` — PASS** with the whole W4 surface enabled
  (`DEPLOYMIX=3,3,1,3 PODUNIFORM=1 RIMWAVES=1`): h0 slot0 and h4 slot1 both byte-MATCH. This is
  the gate the wave lived or died on — a shape derived with one extra RNG draw would have broken
  every paired measurement in the project.
- **`SIGHTLINE_EXPOSURETEST` extended to a third exposure axis** and passing at the shipped mix:
  shape x arena over 4000 seeds (35/35 arenas for every weighted shape), shape x objective over
  the 200 map-generating seeds (8/8 for the three directional shapes, 3/8 for ENVELOP by design),
  plus explicit assertions that `DeployFor` consumes **zero `Util.Rng` draws**, is deterministic,
  and never deals ENVELOP to an objective that forbids it.
- **Autoplay x10**, twice. First with the full surface pinned by env (`DEPLOYMIX=3,3,1,3
  PODUNIFORM=1 RIMWAVES=1`): 4 WIN / 6 LOSE, max 13216 frames. Then again on the **shipped
  defaults** with no env at all: 1 WIN / 9 LOSE, max 13484 frames. Twenty matches, **no
  exceptions and no TIMEOUT** in either set, against a 20000-frame cap. (The win split is the
  weak smoke-test autopilot's, not a balance number — the contract is "no exception, no
  TIMEOUT".) New geometry was the wave's biggest pathfinder risk; it stranded nothing.
- **Screenshots** (archived downscaled in `docs/measurements/w4/shots/`): all four openings on
  one seed/mission/arena, plus DEFEND under ENVELOP. Read and judged — the board reads correctly
  in every shape, and the difference is legible at a glance: FRONTAL opens concealed with no
  shot, PINCER opens with a flank pod already in the squad's line, ENVELOP puts the squad in the
  middle with hostiles on three rims.

### A defect the sweep caught: `ONRAMPTEST` was passing on composition luck
Turning the shipped defaults on made W5's `ONRAMPTEST` fail. It was not a W4 regression. Its A2
probe compared the two heat legs' **force-wide per-enemy averages** to prove RECRUIT fields
"one fewer body, every survivor a point weaker" — but the legs field different body COUNTS, so
they sit at different positions in the shared RNG stream and roll different archetypes, whose
base HP/aim differ by far more than the one point RECRUIT removes. It passed at the old spawn
geometry and failed at the new one for the same reason: luck. Repaired to a
composition-CONTROLLED comparison (each archetype CLASS against itself across the legs, which is
exactly what `bump` moves); verified PASS both at the W4 defaults and at
`SIGHTLINE_PODUNIFORM=0 SIGHTLINE_DEPLOYMIX=1,0,0,0` (the pre-W4 board).

**It also exposed a real W5 fact the old form hid:** `Mission.SpawnEnemies` computes
`bump = Math.Max(0, (n - 1) + statDelta)`, so on **mission 1** the standard bump is already 0 and
RECRUIT's −1 stat has nothing to take off — **the RECRUIT stat relief is a no-op on the very
mission a first-timer meets first**; the body relief is the whole of it there. The test now
asserts what is actually true (stat relief checked from m2) and its PASS banner no longer
over-claims. Whether m1 should carry a −1 floor is an owner call, not a W4 change.

## HONEST ASSESSMENT — does an armed soldier now face a choice of targets, or a queue?

**No — and the wave can now say why, which is worth more than the gate would have been.**

The premise handed down was that an armed soldier sees exactly one target. It does not: the
baseline instrument reads **2.42 foes in range and line of sight per armed soldier-turn**. What
it lacks is two targets worth *choosing* between, and W4 measured, across five structurally
different levers, that the score cannot be moved that way: `choices/ARMED-soldier-turn` sat in
**1.55-1.64** at every single state, because `CountMeaningfulChoices`' two halves are coupled
through threat with opposite signs. Push more comparable guns into a soldier's arc and the
"which target?" axis rises exactly as far as the "where do I stand after?" axis falls. Pod
uniformity — the one lever aimed squarely at comparability — bought +0.06 on the target axis for
free, and that is the largest honest move available on that axis.

What DID change is real and shows up in every other number. **More of the squad fights every
turn**: armed soldiers per player turn 1.39 → 1.55 at h0 and 1.44 → 1.57 at h4, carrying
`meaningful-choices/turn` up 8% and 6%. The lead flips more often at both rungs. And the opening
is no longer one thing: a pinned PINCER runs 47.5% completion in 5.30 turns and a pinned ENVELOP
60.0% — against a 32.5% baseline — so **the shape of the opening is now one of the strongest
difficulty levers in the game**, which is exactly the kind of knob a tuning wave wants and did
not have. Escort and Rescue, the two objectives this tip was dragging worst, came back from
12.57t and 15.33t to 8.19t and 3.40t.

The two things I would tell the next wave, in order:
1. **The ladder, not the density metric, is the emergency.** h0 32.5% and h4 12.5% before any
   lever, against a 55±8 / 30±8 band. `DEPLOYMIX=1,4,1,4` is a measured +7.5 at h0 sitting on
   the shelf; it costs turn count, which is a trade someone should make deliberately.
2. **Do not point another threat-side lever at `choices/ARMED`.** Two waves have now been spent
   discovering the same conservation law from opposite directions. Either add positioning
   options at constant threat (a terrain-grammar pass — more LOW cover, which raises the
   position axis without blocking the sightlines the target axis needs), or re-specify axis (b)
   with an additive band so it stops reading "the fight got safer" as "the decision got richer".

---

## PROGRAM RESONANCE — Wave R1 "REVIEW FIXES" (dev; worktree `wt-r1`)

Four defects an adversarial review of the composed tree reproduced with probes. Each was
re-reproduced here before being fixed, and each fix is measured against the reproduction.

**FIX 1 — RECRUIT's advertised stat relief did not exist on mission 1.**
`Mission.SpawnEnemies` floored the force-wide stat bump at 0: `Math.Max(0, (n-1) + statDelta)`.
At `n == 1` the growth term is 0, so heat 0 gave `max(0, 0) = 0` and RECRUIT (`statDelta -1`)
gave `max(0, -1) = 0` — identical. W5 had correctly stopped the early-mission *heat grace* from
eating the relief; this floor ate it anyway, on the one mission the on-ramp exists for. Both
`Hud.RecruitLines` and `Heat.RecruitMod.Desc` promise "each −1 HP and aim". Measured
(4-seed rank-and-file totals, ELITEs excluded):

| | m1 heat 0 | m1 RECRUIT before | m1 RECRUIT after |
|---|---|---|---|
| per-body HP  | 7.25 | **7.25** | **6.25** |
| per-body aim | 58.50 | **58.50** | **57.50** |

The floor is now −1: one point of force-wide relief may go below the base and no more, so a
deeper stack (RECRUIT + a multi-tier adaptive assist) still bottoms out at −1. **Heats 1–8 are
bit-for-bit unchanged**, proved with a 720-row heat×mission×seed roster fingerprint
(heat −1..8 × 6 missions × 12 seeds, hashing class/name/HP/aim/pos/grenades/weapon): 15 RECRUIT
rows move, all 648 rows at heats 0–8 are byte-identical. `SIGHTLINE_BALANCE=10` (runs=20) is
report-identical to the pre-branch tip apart from wall-clock. Heat −1 pinned, n=20 paired
campaigns: completion 85% → 85%, mission win-rate 97% (n=87) → 98% (n=89).

**FIX 1b — `ONRAMPTEST`'s RECRUIT probe.** It asserted `recHp < stdHp` on the single hard-coded
seed 4242; the reviewer's 12-seed sweep of that form scored 5 pass / 7 fail. Wave W4 landed a
repair from the other direction (per-CLASS comparison, because the legs field different body
counts and roll different archetypes) mid-wave. The two were **reconciled, not duplicated**:
W4's composition control is the base, with R1's 12-seed sweep and a *strict* per-class row on
top — within a mission every member of a class shares one base, so a class mean is exactly
`base + bump`, and the claim is "every shared rank-and-file class moved, on every seed", not
"at least one moved". Named ELITEs stay exempt (explicit stats, never read `bump`). W4 recorded
the m1 no-op as an open owner question; FIX 1 closes it, so m1 is asserted like m3 rather than
excused. Against the merged tip: without the floor change the sweep fails on all 12 seeds at m1
(37 assertions, m3 clean); with it, PASS.

**FIX 2 — the tip/lesson card occluded the combat log.** `DrawTipCard` is 760px centred
(x 260..1020) and the log panel starts at x 970; both anchor to `_barTop`, so the card's
0.96-alpha background covered ~50px — the speaker's name — on every log line. Not an edge case:
most `FieldTip.When` predicates require a live threat, so tips fire mid-fight, exactly when the
ledger is populated. C1's briefing guard (drop the card once the log has an entry) is right for
flavour and wrong for teaching, so the tip card **yields space** instead: new pure
`Hud.TipCardBox(logVisible)` slides it left until its right edge clears `Hud.LogPanelX`
(202..962 at 1280), narrowing only if the slide runs out of room, and stays exactly centred when
no log is drawn. `DrawCombatLog` reads `LogPanelX` too, so there is one source of truth.
`VOICETEST` gains the no-overlap contract. Reproduced and re-shot at
`SIGHTLINE_TIP=0 SIGHTLINE_AUTOPLAY=1 SIGHTLINE_SHOT=1100/1200`.

**FIX 3 — `Display` settings could be written to a relative path.** `SaveGame.Dir` documents and
guards the hazard: `GetFolderPath(ApplicationData)` returns `""` when the resolved directory does
not exist, so it falls back to `$HOME/.config/Sightline`. `Display.Dir` re-derived the path
without that guard, so `Path.Combine("", "Sightline")` was **relative** — settings scattered per
launch directory and read back to the player as a reset while saves and meta went to the right
place. Every field the recent waves added (tutorial tips, four volume faders, anim speed, text
scale) inherited it. Now one derivation, one guard: `SaveGame.ConfigDir` is public and
`Display.Dir` reads it; `ONRAMPTEST` asserts the settings path is rooted AND shares the save
directory. Under `XDG_CONFIG_HOME=/nonexistent/...`, before:
`displayPathRelative:Sightline/display.json` plus a real `Sightline/display.json` appearing next
to the CWD; after: PASS and nothing written next to the CWD.

**FIX 4 — `F` was double-bound: fullscreen AND focused overwatch.** `Game.Update` bound `F` to
`Display.ToggleFullscreen` near the top of every frame; `HandlePlayerInput` bound `F` to
`DoFocusOverwatch`. `IsKeyPressed` is true for both reads in the same frame, so pressing F during
the player turn spent the soldier's action *and* toggled fullscreen, while the action bar
advertises "FOCUS F". The HUD advertises the verb, so the verb wins: **fullscreen claims F11** —
the platform convention, and besides F2 the only function key this game binds. Pause-menu key
hint and `docs/FEATURES.md` corrected. Measured end to end under Xvfb with xdotool
(keydown/hold/keyup ×3, in PlayerTurn with a soldier selected and interactive): before, F → 3
FOCUS + 3 FULLSCREEN; after, F → 3 FOCUS + 0 FULLSCREEN; F11 → 0 FOCUS + 3 FULLSCREEN.
*(Method note: `xdotool key` presses and releases inside one frame, so `IsKeyPressed` never sees
it — the queue does. Hold the key across a frame or the experiment lies.)*

**Key audit (asked for either way): `F` was the only double-binding.** The full in-mission
player-turn map is now recorded as a comment at the global-key block in `Game.Update` — globals
`M` mute / `F11` fullscreen / `F2` anim speed / `Esc` cancel-target-or-pause / `C` cam reset;
verbs `1 2 F B 3 4 5 6 7 8 9 E G H X R T V P`, `Tab`, `Enter`, `Space`, WASD/arrows. No other key
appears twice in one context. The other contexts (Intro, skirmish setup, codex, barracks/shop,
tag editor) are each internally unique and are reached only when `UpdatePlayer` is not, so a
letter may safely mean different things across them. **Free letters remaining: I J O Q U Z.**

**Verification.** Release 0/0; `qa-sweep.sh --full` 45/45 PASS with no COVERAGE GAP block;
`SIGHTLINE_PAIRTEST` PASS; autoplay ×5 clean (LOSE m5 / LOSE m6 / WIN m6 / WIN m6 / LOSE m5 — no
exceptions, no TIMEOUT); `SIGHTLINE_BALANCE=10` runs=20, report-identical to the tip.

**Left deliberately.** No key-rebinding UI (still descoped); the R1 fixes claim F11 by fiat.
`Mission.cs` was touched on the single `bump` line only — W4 owns that file's deployment shapes;
`Maps.cs`, `Ai.cs`, `Game.Autopilot.cs` and `Renderer.cs` untouched (W4 / V3).
# PROGRAM RESONANCE — WAVE X2 "TRUE NORTH II" (2026-08-29, senior dev on wt-x2)

**The charter.** Fourteen waves merged into this program and **every balance number in it was
measured on the tree its wave branched from, never on the merged tree.** Each wave held its own
base's ladder; the composition was never measured. X2 is a measurement-and-correction wave: run
the definitive post-merge ladder, decide what the target should be and say why, then correct
toward it one lever per round.

**THE BASE COMMIT OF EVERY NUMBER BELOW IS `a61ef42`** (RESONANCE W4 "THE SECOND AXIS", the
integration tip) plus X2's own default-off measurement scaffolding. Omitting that line is what
created this wave; it will not be omitted again.

## Method
`SIGHTLINE_BALANCE=10` per chunk under `xvfb-run` on a **snapshot of the Release binary**
(`runbin/<tag>/`, so the tree can keep building while a round is in flight), two disjoint CRN
slot sets (`SIGHTLINE_BALANCE_BASE` 0 / 10) x greedy+sloppy = **40 campaigns per rung**. The
chunk runner asserts the JSON's own `runs` field (it cannot be half-written) and prints OK/BAD;
**every chunk quoted here printed OK with `runs=20`.** `XDG_CONFIG_HOME` and
`SIGHTLINE_BALANCE_JSON` are pinned per chunk (several dev agents share the container). Every
chunk's JSON, report extract and raw log is archived under `docs/measurements/x2/`, with the
exact command lines in its README.

## 1. THE DEFINITIVE POST-MERGE LADDER (no lever; n=40 per rung; `runs=20` asserted x12 chunks)

| rung | run completion | ±SE | mission win (n) | mean turns | ch/turn | ch/ARMED | armed/turn | swings | shots/kill |
|---|---|---|---|---|---|---|---|---|---|
| RECRUIT | **75.0%** | 6.8 | 94.3 (175) | 5.65 | 2.45 | 1.52 | 1.61 | 0.77 | 3.26 |
| heat 0 | **35.0%** | 7.5 | 80.2 (131) | 5.66 | 2.38 | 1.53 | 1.56 | 0.79 | 3.22 |
| heat 2 | **40.0%** | 7.7 | 82.6 (132) | 5.71 | 2.76 | 1.78 | 1.55 | 0.63 | 3.07 |
| heat 4 | **20.0%** | 6.3 | 75.0 (124) | 6.34 | 2.63 | 1.67 | 1.58 | 0.69 | 2.99 |
| heat 6 | **32.5%** | 7.4 | 81.7 (142) | 5.83 | 1.69 | 1.44 | 1.18 | 0.85 | 3.10 |
| heat 8 | **7.5%** | 4.2 | 68.4 (117) | 6.01 | 1.36 | 1.50 | 0.91 | 0.73 | 3.06 |

This supersedes every ladder published before it, including X1's 52.5/27.5/15.0 (base `2100858`),
W5's on-ramp pair (base `b68f38a`) and FUL-13's 52.5/35/30/22.5/10.

**Three facts fall out, and only the first was expected.**

**(a) The published band is missed at exactly ONE rung.** Against FUL-13's
55 / 40 / 30 / 20 / 10 ±8 (h8 ±5): h2 **IN**, h8 **IN**, h4 2.0 low, h6 4.5 **ABOVE**, and
**h0 12.0 low** — the only rung outside by more than noise. The "20+ points below the band at
h0 and h4" the brief inherited from W4 is half right: h0 is genuinely low, h4 is a rounding
error from its floor, and the top of the ladder is fine.

**(b) The ladder is not monotonic, and at n=40 it cannot be.** h2 (40.0) reads *above* h0
(35.0) and h6 (32.5) reads *above* h4 (20.0). The standard error on a 40-campaign rung is
**±6-8 points**, which is the same size as the ±8 band tolerance and larger than the 10-point
step the band asks between rungs. Every wave in this program has been resolving the ladder at a
precision that cannot see it. The honest statement of this measurement is: *RECRUIT is clearly
easiest, h8 is clearly hardest, and heats 0-6 are one flat 20-40% plateau that n=40 cannot
order.*

**(c) The real defect is the COLD OPENER, and RECRUIT already ran the experiment.**
Mission 1 is always Eliminate (`Run.CardForNode`'s Start node → `ObjectiveFor(1)`), so
"Eliminate mean turns" and "mission 1" are very nearly the same measurement on this tree. At
heat 0 the per-mission curve is **U-shaped**:

| | m1 | m2 | m3 | m4 | m5 | m6 |
|---|---|---|---|---|---|---|
| heat 0 win% (n) | **75 (40)** | 79 (14) | 89 (19) | 91 (22) | 76 (17) | 74 (19) |
| RECRUIT win% (n) | **100 (40)** | 89 (18) | 92 (26) | 94 (32) | 100 (25) | 88 (34) |

The opener is as lethal as the finale and 15 points harder than the middle of the run — the
front-loaded anxiety `docs/DESIGN.md` §3.D explicitly forbids ("Don't front-load anxiety… give
the player a beat to find footing before the spike"). `Game.SetupMission` already carries a fix
for this exact failure mode — the EARLY-MISSION HEAT GRACE, whose comment reads "the measured
~20% mission-1 loss (which hard-caps run completion, a geometric product)" — but it is **gated
on `heat > 0`**, so it protects rungs 1-8 from *their* extra bodies and leaves the base force
untouched at the rung that needs it most.

And the size of the effect is not a guess. On the **same 40 worlds**, RECRUIT's only
mission-1 difference from heat 0 is **one hostile body** (its −1 stat is a no-op at m1, where
`bump = Math.Max(0, (n-1) + statDelta)` is already 0 — the fact W4's ONRAMPTEST repair
surfaced) plus the 5-turn bleed-out valve. Mission 1 goes **75% → 100%, zero losses in 40
campaigns.** Ten of heat 0's twenty-six lost runs die on the opening mission.

## 2. THE TARGET — I am KEEPING the band, adding the rung it is missing, and fixing its stated precision

The brief offered the option of adjusting the band rather than the game, and named the
strongest argument for it: the game now has a **RECRUIT rung below heat 0** that did not exist
when the band was written, so heat 0 no longer has to be the on-ramp. **I am not taking it, and
the reason is a measurement.**

FUL-13 set h0 = 55 *before* RECRUIT existed. W5 then added RECRUIT and measured the pair on its
own base at **RECRUIT 75 / h0 55** — i.e. the on-ramp was designed as a **+20 step above an h0
of 55**, with RECRUIT present. On this tree RECRUIT measures **75.0** (n=40): the on-ramp has
not moved at all. What has moved is heat 0, from 55 to 35 — so the step a player takes when
they leave the on-ramp is now **40 points, double the one that was designed**. The RECRUIT
argument, followed honestly, argues for restoring h0, not for lowering the band to meet it.
Lowering h0's target to ~40 would make the first paid rung a 35-point cliff off a tutorial
setting, which is the anxiety side of DESIGN §3.D, not the flow channel.

The second reason is that the band is **not** broadly missed. Only h0 is out by more than one
standard error. FUL-13's re-set was justified because the game had *changed identity* (routes
that dodged their own hardest content started dealing it); nothing comparable happened here.
Fourteen waves of accumulation moved ONE rung and left the other four where they were. That is
a correction, not a re-specification.

**Two amendments I am proposing, both from measurement, neither of them a difficulty change:**

**(i) Publish the RECRUIT rung in the band: `RECRUIT 75 ±8`, with a standing floor of
`RECRUIT − h0 ≥ 15`.** The band has never included the rung below zero even though the game has
shipped it for two waves. RECRUIT measures 75.0 here and 75 at W5's own base — the only number
in this project that has reproduced across a re-baseline — so it is the safest anchor the ladder
has, and pinning it is what makes "the on-ramp is too steep" a *gate* instead of an observation.

**(ii) State the band's precision, and stop reading rung ORDER off it at n=40.** A 40-campaign
rung carries **±6-8 points of standard error** — the same size as the ±8 tolerance and larger
than the 10-point step the band asks between rungs. That is why this baseline reads h2 above h0
and h6 above h4: those inversions are noise, and no wave should spend a lever on them. Pooling
adjacent rungs (n=80) gives back a monotone ladder and is the granularity this harness can
actually resolve:

| pooled rung pair | measured | ±SE | band target (mean of the two rungs) | verdict |
|---|---|---|---|---|
| RECRUIT | 75.0 (n=40) | 6.8 | *(unpublished — proposed 75)* | anchor |
| heat 0-2 | **37.5** (n=80) | 5.4 | 47.5 | **10.0 low** |
| heat 4-6 | **26.3** (n=80) | 4.9 | 25.0 | **on target** |
| heat 8 | 7.5 (n=40) | 4.2 | 10 (±5) | in band |

**So the correction this wave owes the game is +10 completion points at the BOTTOM of the
ladder and nothing anywhere else** — which is a much smaller and much better-aimed job than the
"20+ points everywhere" the brief inherited, and it is the exact shape a cold-opener repair
produces: relief on missions 1-2 multiplies every rung's completion by the same factor, and the
same multiplier is worth the most absolute points where completion is highest.

## 3. THE ROUND TABLE — one lever per measured round, h0, n=40 each, `runs=20` asserted per chunk

| round | lever | compl | ±SE | mis-win | mean t | Elim t | Escort t | ch/turn | ch/ARMED | armed/t | swings | s/kill | m1 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| R0 | baseline (shipped defaults) | 35.0% | 7.5 | 80.2 | 5.66 | 5.10 | 8.03 | 2.38 | 1.53 | 1.56 | 0.79 | 3.22 | 75% |
| A1 | `HostileAimTrim=5` | **42.5%** | 7.8 | 82.9 | 5.46 | **5.10** | 6.30 | 2.61 | 1.68 | 1.56 | 0.74 | 3.12 | 75% |
| A2 | `HostileAimTrim=10` | **50.0%** | 7.9 | 86.4 | 6.36 | **4.80** | **13.66** | 3.01 | 1.76 | 1.71 | 0.76 | 3.18 | 82% |

## 4. THE ROUND TABLE, CONTINUED — the lever that was shipped

| round | lever | compl | ±SE | mis-win | mean t | Elim t | Escort t | ch/turn | ch/ARMED | armed/t | swings | s/kill | m1 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| O1 | **`OpenerTrim=1`** — one body off m1, one off m2 | **57.5%** | 7.8 | 89.2 | 5.90 | **3.25** | **12.81** | 2.57 | 1.60 | 1.60 | 0.61 | **3.30** | **100%** |

`R0diag-h0-b0` (the X2 tree with every new knob OFF, same pinned slot set) reproduces
`R0-h0-b0` **exactly** — runs, missions, completion, `decisionRichness`, `byObjective`,
`byMission`, `playerClasses` and all ten per-slot paired records MATCH — so every round above
is a comparison against the same instrument. `SIGHTLINE_PAIRTEST=1` is **PASS** with the
shipped default on (h0 slot0 and h4 slot1 both byte-MATCH): `OpenerTrim` is integer arithmetic
on the mission number and consumes **zero `Util.Rng` draws**.

### Why `OpenerTrim` and not the aim trim

Both work. `HostileAimTrim` is a clean, linear dial — **+7.5 completion per 5 aim points** at
heat 0 (35.0 / 42.5 / 50.0 at trims 0 / 5 / 10), it leaves Eliminate's turn budget untouched at
the 5-point dose, and it *raises* every decision-density number (ch/turn 2.38 → 3.01 at the
10-point dose, armed/turn 1.56 → 1.71) because soldiers who survive keep shooting. But it is a
**global difficulty dial with no diagnosis behind it**: it makes the whole game easier by the
same amount everywhere, which is precisely the kind of undirected change that produced this
wave's problem in the first place. And the dose that reaches the band (10) breaks two turn
budgets (Eliminate 4.80, Escort 13.66).

`OpenerTrim` is a **repair of a named, measured, design-doc-violating defect** — the U-shaped
difficulty curve whose left arm ends a quarter of all runs before the player has earned a single
promotion — and it lands heat 0 at 57.5% against a target of 55%. It is shipped; the aim trim
stays in the tree, default 0, as a measured and priced dial for whoever needs one next.

### The three gates O1 moves, and what is actually true underneath

1. **`Eliminate mean turns` 5.10 → 3.25 (gate ≥ 5.0): BREACHED, and the gate is measuring the
   wrong thing on this tree.** Mission 1 is *always* Eliminate (`Run.CardForNode`'s Start node
   → `ObjectiveFor(1)`), and at heat 0 the baseline's Eliminate sample is **n=40 with m1 n=40**
   — the two are the same measurement. R0's 5.10 turns is not a two-hit trade; it is a losing
   grind, 25% of which ends in a wipe with the last two soldiers trading shots. The metric that
   actually guards X1's purchase is **shots-per-kill, and it goes UP: 3.22 → 3.30.** Each body
   still takes three shots; there is one fewer body and a full squad shooting it. X1 bought
   "a trade takes two hits" and that is intact; what it also inadvertently bought was
   "mission 1 takes two extra turns *because you are losing it*", and that is what O1 gives back.
2. **`lead-swings/match` 0.79 → 0.61 (gate ≥ 0.79): BREACHED.** Honest mechanism, not an
   artifact: a 4-body opener against a full squad is not a contested fight, and mission 1 is
   ~26% of all matches played. Lead-swings and "the opener should not be a coin flip" are in
   direct tension, and this wave chose the opener. Note the aim trim breaches it too (0.74 at
   −5), so does every lever measured here — the baseline's 0.79 is the number a *broken* opener
   produces.
3. **`Escort` 8.03 → 12.81 turns at h0 (gate ≤ ~10): BREACHED — and it was hidden, not caused.**
   Escort's h0 sample grows from n=13 to n=19 because more runs now reach the missions that deal
   it. The 8.03 was a **survivorship-biased** number: only runs that were already winning got to
   play Escort at heat 0. W4's celebrated "Escort 12.57 → 8.19" repair is partly the same
   artifact. Escort's real h0 cost is ~13 turns and it is still the game's drag objective.

## 5. THE SHIPPED LADDER — round S1, `OpenerTrim=1`, n=40 per rung, all 12 chunks `runs=20`

**Base commit `a61ef42`.** The band is FUL-13's, with X2's proposed RECRUIT row added.

| rung | R0 baseline | **S1 SHIPPED** | delta | band | in band? |
|---|---|---|---|---|---|
| RECRUIT | 75.0% | **75.0%** | 0.0 | *(proposed)* 75 ±8 | **YES** — unchanged, exactly as predicted (its m1 was already 100%) |
| heat 0 | 35.0% | **57.5%** | **+22.5** | 55 ±8 (47-63) | **YES** — 2.5 above target |
| heat 2 | 40.0% | **35.0%** | −5.0 | 40 ±8 (32-48) | **YES** |
| heat 4 | 20.0% | **30.0%** | +10.0 | 30 ±8 (22-38) | **YES** — exactly on target |
| heat 6 | 32.5% | **20.0%** | −12.5 | 20 ±8 (12-28) | **YES** — exactly on target |
| heat 8 | 7.5% | **17.5%** | +10.0 | 10 ±5 (5-15) | **NO — 2.5 over the ceiling** (0.4 SE) |

**The ladder is monotone for the first time this program: 75.0 / 57.5 / 35.0 / 30.0 / 20.0 /
17.5.** Five of six rungs are in band and two of them (h4, h6) land on the target to the
decimal. The rungs that moved in the "wrong" direction (h2 −5.0, h6 −12.5) and the apex's +10.0
are all inside ±1.5 SE of their baselines — the same n=40 noise §1(b) warned about, now
visible from the other side. Do not read those three deltas as effects of the lever; read the
shape.

**The one out-of-band rung, stated straight: heat 8 measures 17.5% against a 5-15% band, +2.5
over the ceiling, ±6.0.** The measurement cannot distinguish it from the ceiling and the wave
did not spend a lever on it. The apex's own m1 was already 98%, so the cold-opener repair has
almost nothing to do there; most of the 7.5 → 17.5 is the noise band. What is real at the apex
is unchanged and still bad: **Escort 33% (n=15), Evac 0% (n=4), Rescue 33% (n=3), Decapitate
41%** — heat 8 is a wall made of four specific objectives, which is where a future apex wave
should aim rather than at the rung average.

### Per-objective x heat, shipped (win% (n) / mean turns) — the two archived tables

| objective | RECRUIT | h0 | h2 | h4 | h6 | h8 |
|---|---|---|---|---|---|---|
| Eliminate | 100 (42) | **100 (41)** | **100 (41)** | 95 (42) | 93 (42) | 93 (42) |
| Defend | 97 (38) | 82 (34) | 71 (34) | 75 (36) | 91 (32) | 85 (26) |
| Decapitate | 85 (41) | 79 (33) | 54 (26) | 59 (29) | **34 (29)** | 41 (17) |
| Escort | 85 (20) | 89 (19) | 100 (16) | 94 (18) | 95 (19) | **33 (15)** |
| Hack | 100 (14) | 92 (12) | 75 (12) | 85 (13) | 85 (13) | 100 (12) |
| Sabotage | 100 (12) | 91 (11) | 90 (10) | 91 (11) | 92 (12) | 100 (10) |
| Evac | 100 (6) | 100 (4) | 100 (4) | 67 (3) | 75 (4) | **0 (4)** |
| Rescue | 100 (6) | 100 (3) | 100 (5) | 100 (4) | 83 (6) | 33 (3) |

Mean turns (same order): Eliminate 2.74 / 3.25 / 3.30 / 3.45 / 4.20 / 4.15 · Defend 9.00 / 8.90
/ 8.65 / 8.55 / 8.90 / 8.70 · Decapitate 4.50 / 4.32 / 5.12 / 4.85 / 5.88 / 4.94 · **Escort
13.48 / 12.81 / 10.50 / 10.55 / 8.86 / 11.35** · Hack 3.66 / 3.50 / 4.40 / 3.95 / 3.88 / 3.33 ·
Sabotage 3.42 / 2.69 / 3.40 / 3.37 / 3.75 / 2.64 · Evac 11.70 / 5.20 / 6.80 / 8.30 / 3.80 /
5.50 · Rescue 4.70 / 2.67 / 5.42 / 3.25 / 4.97 / 4.00. Full tables in
`docs/measurements/x2/{R0,S1}-BYOBJECTIVE.txt`.

## 6. THE GATES — every one, with its number

| gate | target | R0 baseline | **S1 shipped** | verdict |
|---|---|---|---|---|
| ladder inside the band at every rung | all 6 | h0 12 low; h6 4.5 high; **non-monotonic** | RECRUIT/h0/h2/h4/h6 **IN**; **h8 17.5 vs 5-15** | **5 of 6 — h8 out by +2.5 (0.4 SE)** |
| RECRUIT stays meaningfully easier than h0 | real gap | +40.0 (the defect: double the designed step) | **+17.5** (75.0 vs 57.5) | **MET** — and back to roughly W5's designed +20 |
| shots-per-kill (the two-hit trade) | ≥ 3.00 | 3.22 (h0); 2.99 at h4 | **3.30 (h0)**; 3.22 / 3.23 / 3.27 / 3.29 / 3.56 | **MET at every rung, and up at every rung** |
| armed soldiers / turn | ≥ 1.50 | 1.56 (h0) | **1.60 (h0)**, 1.58 (h2), 1.61 (h4) | **MET** at h0-h4 (h6 0.96 / h8 0.89 / RECRUIT 1.44 — h6 fell from 1.18) |
| Eliminate mean turns | ≥ 5.0 | 5.10 (h0) | **3.25 (h0)** | **BREACHED −1.85** — see §4; on this tree Eliminate *is* mission 1, and 5.10 was a losing grind |
| lead-swings / match | ≥ 0.79 | 0.79 (h0), 0.74 pooled | **0.61 (h0)**, 0.70 pooled | **BREACHED −0.18** — a 4-body opener against a full squad is not contested, and m1 is ~26% of matches |
| no objective mean past ~10 turns at h0 or h4 | ≤ ~10 | h0 max 8.90; **h4 Escort 11.70 already breaching** | **h0 Escort 12.81**, h4 Escort 10.55 | **BREACHED** — Escort's h0 8.03 was survivorship bias (n 13 → 19); its real cost was always ~13 turns |
| `SIGHTLINE_PAIRTEST` | PASS | PASS | **PASS** (h0 slot0 + h4 slot1 byte-MATCH with the shipped default on) | **MET** |
| `HEATLADDERTEST` | PASS | PASS | **PASS** (untouched — the lever is a body count, not a damage row) | **MET** |
| autoplay x10 | no exception, no TIMEOUT | — | **5 WIN / 5 LOSE, 0 exceptions, max 14022 frames vs the 20000 cap** | **MET** |

**Reported, NOT chased** (the brief's forbidden metric): `choices/ARMED-soldier-turn` reads
1.82 / 1.60 / 1.73 / **1.91** / 1.44 / 1.46 (RECRUIT→h8) against the baseline's 1.52 / 1.53 /
1.78 / 1.67 / 1.44 / 1.50, and `meaningful-choices/turn` 2.61 / 2.57 / 2.73 / **3.07** / 1.38 /
1.29 against 2.45 / 2.38 / 2.76 / 2.63 / 1.69 / 1.36. **No lever was pointed at either.** Two
observations for the record, both refinements of W4's law rather than contradictions of it:
the h4 cell at 1.91 is the highest `choices/ARMED` this project has recorded, and the aim-trim
rounds moved it too (1.53 → 1.68 → 1.76 at trims 0/5/10). W4's conservation held across levers
that changed *geometry at constant lethality*; a lever that lowers how much enemy fire LANDS
raises both axes at once, because more soldiers survive to hold targets AND the board is safer
to stand on. That is consistent with the mechanism W4 identified and is the strongest argument
yet for re-specifying axis (b) additively (spec in ROADMAP).

## 7. VERIFICATION
- `dotnet build -c Release` — **0 warnings / 0 errors**.
- `bash scripts/qa-sweep.sh --full` — **46/46 PASS, 0 FAIL**, and the **COVERAGE GAP block is
  empty**. Includes the wave's new `OPENERTEST` and the three tests most exposed to a
  body-count change (`ONRAMPTEST`, `PODTEST`, `HEATLADDERTEST`), plus its autoplay x3
  (WIN/WIN/WIN). The sweep's derived footer is now 46.
- **`SIGHTLINE_PAIRTEST=1` under `xvfb-run` — PASS** with the shipped default on: h0 slot0
  (WIN, 6 cleared, 42 turns) and h4 slot1 (LOSE, 4 cleared, 32 turns) both byte-MATCH.
- **Autoplay x10 on the shipped defaults** — 5 WIN / 5 LOSE, **zero exceptions, zero TIMEOUTs**,
  max 14022 frames against the 20000 cap. (The win split is the weak smoke-test autopilot's,
  not a balance number; the contract is "no exception, no TIMEOUT".)
- **Instrument identity**: `R0diag-h0-b0` — the X2 tree with every new knob OFF, on the pinned
  slot set — reproduces `R0-h0-b0` **exactly**: runs, missions, completion, avg-missions,
  `decisionRichness`, `byObjective`, `byMission`, `playerClasses` and all ten per-slot paired
  records MATCH. Every round in this write-up is therefore a comparison on one instrument.
- **`OPENERTEST`** (new, in the sweep): pins the ramp's shape (full trim at m1, half rounded up
  at m2, none from m3), the shipped default of 1, the 3-body floor, determinism, and that
  RECRUIT still fields exactly one fewer body than heat 0 at m1 with the trim on.

## 8. HONEST ASSESSMENT — is this tree tuned, or merely measured?

**It is measured, and one real defect in it is fixed. It is not yet tuned, and the difference
matters.**

What this wave can defend. There is now a ladder of record with a base commit, run at n=40 per
rung across six rungs including the one below zero, on the composed tree, with the raw chunks
archived. It is monotone, five of its six rungs sit inside the published band and two land on
target to the decimal, and the correction that got it there is a **repair of a named defect**
rather than a difficulty dial: the game was ending a quarter of its runs on mission 1, against
a squad with nothing earned yet, because the opener grace the codebase already contains was
gated on `heat > 0`. RECRUIT had been running the control experiment for two waves and nobody
had read it. Fixing it cost one hostile body on two missions and no combat math at all —
shots-per-kill went *up* at every single rung.

What it cannot defend, in order of how much it bothers me:

1. **The instrument is too coarse for the question the band asks.** A 40-campaign rung carries
   ±6-8 points; the band's tolerance is ±8 and its rung steps are 10. Three of the six deltas
   in the shipped table (h2 −5.0, h6 −12.5, h8 +10.0) are almost certainly noise, and I cannot
   prove otherwise from this data. **The single highest-value thing the next wave can do is not
   another lever — it is n≥80 per rung on the state that is already shipped.** Everything else
   in this program is built on a measurement whose error bar is the size of the answer.
2. **Two non-regression gates are breached and one of them is a real cost.** The
   Eliminate-turns breach I will defend (§4: on this tree that metric is mission 1's length,
   and shots-per-kill — the metric that actually guards X1's purchase — improved). The
   **lead-swings breach is a genuine cost**: 0.79 → 0.61 at heat 0, because a 4-body opener
   against a full squad is not a contested fight and mission 1 is a quarter of all matches
   played. The game traded some of its swing for a first mission that is not a coin flip. I
   think that is the right trade and I do not think it is free.
3. **Escort is still the drag objective and the old numbers were flattering it.** 12.81 turns
   at heat 0, 13.48 at RECRUIT, 33% win at heat 8. Its previously-celebrated 8.03/8.19 came
   from a sample of only the runs healthy enough to reach it. This is the clearest example in
   the project of a metric improving because the game got *worse* around it.
4. **Heat 8 is out of band at 17.5% and the middle rungs still have no measurable teeth.**
   Mission win-rate barely separates heats 0-6 even in the shipped state (89.2 / 82.4 / 82.1 /
   80.2), and the apex is a wall made of four objectives (Escort 33, Evac 0, Rescue 33,
   Decapitate 41), not a rung average. Both are recorded as ROADMAP items.

**The one thing I would say to the next wave.** This wave's finding was not produced by a
lever; it was produced by looking at `byMission` instead of the rung average, and by noticing
that a rung the project already ships (RECRUIT) was a controlled experiment nobody had read.
The rung average hid a 25% mission-1 failure behind a plausible-looking 35%. Before spending
another twenty minutes of CPU on a dial, read the decomposition you already have.
# PROGRAM RESONANCE — WAVE A3 "AUDITION" (2026-08-29, dev on wt-a3)

**The charter.** A1 built the ear (`SIGHTLINE_AUDIODUMP` / `AUDIOGATE` — a device-free
measurement rig over the exact float samples the synth hands Raylib) and A2 rebuilt the mix
against those numbers: the white-noise weapons, the beds with 0.000% of their energy above
1 kHz, the undesigned 16 dB spread, the clipping kill stack. All of it was fixed **by
measurement**, and all of it was decided **blind** — there is no audio device in this sandbox
and nobody has ever heard this game. A3's job was the last mile: not to judge the sound, which
this session cannot do, but to build the instrument the owner uses to judge it.

## PART A — the AUDIO CHECK screen
Reached from the intro (`[U]`, third utility row) and from the pause menu (right column, under
the mix faders). One screen, designed for a two-minute sweep:

- **23 cues, one row each**, grouped WEAPONS / COMBAT / UI / STINGERS, each with a one-line
  "what it is for" so a cue is judged against its JOB and not its filename.
- **Single and BURST.** BURST fires five at 105 ms — the per-shot pitch/gain jitter and the
  six-voice round-robin only become audible under repeat fire, which is where a bad firing
  voice actually reveals itself (A2's monotone-glissando bug would have been obvious here).
- **The numbers beside the button**: peak dBFS, RMS dBFS and the >1 kHz energy share, computed
  through `Audio.CueMeasure` — the same render + Welch spectrum `AUDIOGATE` uses, so what the
  row prints is what the gate measures. RMS is tinted against the cue's mix-role band and
  marked with a leading `!` when it has drifted out (a glyph, not only a colour, for CB mode).
- **The mix, live**: all four faders on the same screen as the cues, movable while sound is
  playing, persisted on release (and only then — glancing at the bench does not touch disk).
- **The music beds**: AMBIENT / COMBAT snaps plus a hand-swept INTENSITY slider, with the two
  bed gains actually being pushed at Raylib drawn as bars, so the crossfade is visible as well
  as audible.
- **The four concurrent stacks** the gate is written against (`w_lmg+crit+death+st_kill`, the
  3-shot overwatch chain, …) fired at the SAME offsets, so the limiter gets an ear test to go
  with its clipped-sample count.

**Device-free-safe.** With no device every `Play` is a silent no-op, and the screen says
`NO AUDIO DEVICE` on its face with a line explaining that the controls and the numbers are
still real — rather than looking broken. The measurement table warms 3 cues/frame so entering
the screen is never a hitch on a real machine. **No new `Raylib.GetTime()` reads**: the screen
runs on its own dt accumulator (`Game.AudClock`).

`SIGHTLINE_AUDITION=1` (+ `SIGHTLINE_AUDITIONFIRE=1` to light the just-played rows) shoots it.
`SIGHTLINE_AUDITIONTEST=1` is the contract: the listing covers `SfxCueIds` **exactly** (a cue
added to `BuildRecipes` and forgotten here would otherwise become the one sound nobody ever
auditions), every cue has a role caption, nothing overflows its column at **120% text scale**,
every stack resolves to known cues, and every printed number is finite and inside budget.
Wired into `qa-sweep.sh` (now 46).

## PART B — the writing C1 flagged
C1 shipped the fiction frame and named its own two weaknesses. Both are closed.

1. **Bark pools 3 → 6 variants per beat, no new beats** (C1's own recommendation; the rate
   limit is the feature). With a ceiling of six lines a mission and one firing per beat, three
   variants meant a returning player heard the same sentence on the same trigger every other
   run. `VOICETEST` now pins depth `>= 6`, asserts the variants inside a beat are **distinct**
   (widening can never mean padding with repeats) and rejects an exclamation mark outright.
2. **The opposition line stopped being a template.** Three of four factions read
   `"<X> ground: a, b, c. <rule>."` — one shape, in the middle slot of a three-line card whose
   other two lines are fixed in form. Each line now has its own sentence shape: a prohibition
   (Syndicate), a thesis (Legion), an observation (Wardens), a shrug (unaligned). **The
   load-bearing half is untouched** — every number is still interpolated from the constant the
   resolver applies, and low cover's `20` is now read off `Grid.CoverInfo.Defense` itself
   rather than retyped, so a cover retune cannot leave the briefing lying. `VOICETEST` asserts
   each value is literally present in its line, that no line uses the old template, and that no
   two open on the same word.

## VERIFICATION
- Release **0 warnings / 0 errors**.
- `qa-sweep.sh --full`: **46/46 PASS**, empty COVERAGE GAP block, PAIRTEST PASS.
- `AUDIOGATE` PASS (14/14 checks) · `VOICETEST` PASS · `AUDITIONTEST` PASS.
- **The RNG-separation probe was falsified by hand**: one `Util.Rng.Next()` injected into
  `LowCoverDefense` turns VOICETEST into `FAIL — RNG SEPARATION`, then restored. The check is
  not passing vacuously.
- `SIGHTLINE_BALANCE=10` (`runs=20` asserted) on wt-a3 vs the branch point: the **entire
  aggregate JSON is identical** field-for-field after dropping timing. Seeded autoplay
  (`SIGHTLINE_SEED` 11/22/33/44/55) is **frame-identical** to base on every seed. The wave is
  gameplay-inert, which is the proof the new text takes zero shared draws.
- Autoplay: 19 runs on this branch, one TIMEOUT at frame 20000 on an unseeded clock seed. It is
  **not attributable to this wave** — the seeded pairing above shows the two trees produce the
  same frame counts, and base independently produced a 17,845-frame run against the same 20,000
  cap. It is a tail flake of the weak smoke-test autopilot, pre-existing.
- Screenshots read and judged: the screen at the default palette, under `SIGHTLINE_CB=1`, and
  at `SIGHTLINE_UISCALE=3` (120%), plus the pause menu and the intro. **Every one of them is
  the no-audio-device state** — this sandbox has no other state to photograph.

## LEFT UNDONE / FOR THE OWNER
- **The taste.** Nothing here judges a sound. The screen exists so the owner can, and the
  useful output is a list of cue ids with a sentence each.
- The two music beds are auditioned by crossfade, not measured on-screen (rendering 16 s of bed
  per frame is not a thing a screen can do); their numbers stay in `AUDIODUMP` / `AUDIOGATE`.
- The `>1 kHz` column is deliberately **not** banded or colour-graded: the program has a
  committed target for the music beds (>= 15%) and none for SFX, and inventing one on the
  screen would be a judgement this wave has not earned.

---

## PROGRAM RESONANCE — Wave R2 "QA FIXES" (dev; worktree `wt-r2`)

Four defects a second adversarial QA pass reproduced on the composed tree, plus the four LOWs
QA listed as optional. Every one was re-reproduced here with QA's own probes before being
fixed, and every fix is measured against its reproduction. Base: `3a20d18` (A3 AUDITION).

**FIX 1 (HIGH) — the ENVELOP opening could wall a soldier out of the mission.**
`Mission.EnsureConnectivity` floods from `players[0]` and repaired enemies, evac tiles, the
terminal and sabotage sites — but never the OTHER PLAYERS. Invisible while every deployment
shape seated the squad in cols 0-3 (which `BuildProcedural` deliberately keeps clear); W4's
ENVELOP centre seat (cols 7-10, rows 3-6) drops soldiers into the mid-field HIGH-cover band,
and `Grid.CostMap`'s no-corner-cutting rule seals pockets around them. The intent already
existed 55 lines away: `PlaceBarrels` puts every player in its `required` set.

Isolated soldiers over 5760 fresh boards per heat (4 shapes × 8 objectives × 6 missions ×
30 seeds), all of them under ENVELOP:

| heat | RECRUIT | 0 | 2 | 4 | 6 | 8 |
|---|---|---|---|---|---|---|
| before | 25 | 23 | — | 11 | — | 7 |
| after  | 0 | 0 | 0 | 0 | 0 | 0 |

34,560 boards after the fix: zero isolations, zero entombments, zero unreachable
hostiles/objectives. Deterministic repro (`Probe2 dump 3 Defend 3 3`): NOX at (9,6) had
neighbours `# # # / o . # / # # #`, no legal move and no path to the squad — cost `-1` before,
`8` after. **FIX 1b ELBOW ROOM**: a soldier can be reachable and still be frozen on turn 1
(every neighbour cover or a teammate, diagonals killed by the corner rule), so one adjacent
cardinal cover tile is opened. Measured firing rate 22 / 1536 boards (1.4%) and **every one
under ENVELOP** — 15 procedural, 7 on authored arenas 28 and 3; it never fires under
FRONTAL/PINCER/CROSSFIRE, so no pre-W4 opening's geometry is touched. Zero extra RNG draws
(PAIRTEST PASS).

**New standing guard `SIGHTLINE_GEOMTEST`** (wired into `scripts/qa-sweep.sh`): 3072 fresh
boards across 4 shapes × 8 objectives × 6 missions × heats {0,8}, asserting soldier
reachability + elbow room + hostile/objective reachability, with a per-heat ENVELOP
non-vacuity guard. QA flagged that `STACKTEST` structurally could not have caught this — it
is a fixed 16-board sample that never varies the deployment shape. GEOMTEST fails loudly on
the pre-fix tree.

**FIX 2 (MEDIUM) — the incoming-fire forecast under-read real damage by 31-44%.**
It was `hit% × mean(post-armor band)`, whose comment claimed crits (up) and "the graze floor
(down)" cancelled. The graze term is not down: a graze deals `max(1, reduce(DmgMin))` on a
roll that would otherwise deal **zero**, so both omissions pushed the same way. Measured over
200k `Combat.Resolve` rolls per weapon on the post-X1 bands:

| weapon | old card | real mean | old ratio | new card | new ratio |
|---|---|---|---|---|---|
| Rifle | 2.13 | 2.96 | 1.392× | 2.960 | 1.002× |
| Shotgun | 3.29 | 4.58 | 1.394× | 4.578 | 1.000× |
| Sniper | 3.40 | 4.80 | 1.412× | 4.798 | 1.001× |
| Lmg | 2.59 | 3.41 | 1.315× | 3.401 | 1.001× |
| Smg | 1.42 | 2.04 | 1.435× | 2.034 | 1.002× |

New `Combat.ExpectedDamage` enumerates the real roll (uniform band with per-roll crit, plus
the graze leg at Resolve's exact `grazeTop`) and is the single source of truth for the card.
`Combat.FragileFloor` stays excluded, deliberately and one-directionally: it fires on at most
the first shot of a volley at full HP, so folding it into a per-tile sum over every bearing
gun would under-read. **Why the test missed it**: `THREATTEST` hand-recomputed the same
formula, so a wrong formula agreed with itself. New leg (11) rolls 100k real `Resolve` shots
per weapon and asserts the forecast matches within 2% (~6σ). It fails on the old formula at
23.9-29.2%. The autopilot's own `(DmgMin+DmgMax)*0.5` EV heuristic (`Game.Autopilot.cs:1008`)
was deliberately left alone — it is not a displayed number, and changing it moves the
flywheel's policy legs.

**FIX 3 (MEDIUM) — the chrome-fit contract was only asserted at one text scale.**
`VOICETEST` asserted "no generated line overflows the chrome that draws it" at `Cfg.UiScale
== 1` while W5 ships {0.90, 1.00, 1.10, 1.20} against **fixed-pixel** chrome. Measured at the
real game font (QA's probe used the Raylib default face and under-counted): 4 of 36 barks over
the log column at 110%, 14 at 120% (worst 311px vs 278px); the combat log's row pitch was a
hard-coded 14px against a 14.4px glyph box, so rows touched; the shop ellipsized card bodies
mid-word (`"…installed on a soldi…"`, `"counters SYNDICATE for one …"`), the WAR ROOM silently
dropped a row's words behind an `li < 2` cap, and `RE-ROLL SLATE (5 SALV)` overran its fixed
178px button.

Fixed by growing the chrome rather than shrinking the writing: `Hud.LogPanelW` /
`LogTextWidth` / `LogPanelX` scale with the setting (never below the authored 296px; the FIELD
TIP card already yields this column and just slides further left — at 120% it needs 780px of
room and has 911). `Hud.LogRowPitch()` is measured from the glyph box (identical 14px at every
scale ≤ 100%). New `Hud.FitWrap` shrinks one type step instead of ellipsizing, applied to the
shop desc + effect line, both WAR ROOM unlock bodies and the achievement descs; the RE-ROLL
button is sized to its measured label. Five barks were trimmed anyway and DAWN PATROL's desc
shortened (it needed two rows at the 10px floor in a 224px lane). `VOICETEST` leg (8) re-runs
barks + briefs + row pitch + tip-card no-overlap + **every card body in the game** at all four
scales, failing if the body fitter reaches its floor: 19 violations before, PASS after.
Screenshots at 120% (shop / WAR ROOM / in-mission log) confirm no ellipsis, no touching rows,
no label outside its frame.

**FIX 4 (LOW-MEDIUM) — a corrupt meta could lock the difficulty picker on RECRUIT.**
`SaveGame.LoadMetaHeat` clamped `MaxHeat` through `Heat.Clamp`, whose floor W5 moved to −1.
But `MaxHeat` is an unlock **ceiling**, not a dialled level. `{"MaxHeat":-9}` loaded as −1 →
`UnlockedHeat = -1` → `PendingHeat` pinned to −1 → both intro steppers dead (minus needs
`level > Heat.Min`, plus needs `level < unlocked`), with no way out but deleting `meta.json`.
`Game.cs:1727` already guarded the env path with `Math.Max(0, …)`; the disk path was missed.
Both load and save now `Math.Clamp(_, 0, Heat.Max)`. `METATEST` leg (12) asserts the floor,
the write path, and that the picker's own predicates leave a direction live; it fails pre-fix.

**LOWs, all four addressed.** (1) `SaveGame.FromUnitDto` clamps the scalars F1 left verbatim —
`Mobility:1000000` gave a MoveBudget of 2,000,000 (and a Dijkstra flood over the whole grid on
every hover), `Mobility:-9` a MoveBudget of 2, plus `Aim:100000`, `Armor:-50` (armor that
*added* damage through `HardenedReduce`) and `Rank:99`. Generous envelopes, not gameplay caps:
a legitimate save round-trips byte-identically and SAVETEST's 13 enum fingerprints are
untouched. (2) `SchemaVersion` is now **read**: a file newer than `CurrentSchema` is refused
and stashed to `.bak` instead of being silently misread. (3) a corrupt `display.json` is
copied to `display.json.bak` before being discarded, latched once per session like the meta
rule — it used to take the player's whole settings profile with it. (4) `Run.AssistLevel`'s
RECRUIT stack was reviewed and **kept**, now documented as a decision: the assist answers a
loss streak rather than a rung, the player most likely to have one is the player on the
on-ramp, and the stack is worth one point (RECRUIT −1 on top of an assist already capped at −5
at heat 0) while the gate that matters — nothing above standard — is intact.

**Verification.** Release 0 warn / 0 err. `qa-sweep.sh --full`: 49/49 PASS (47 run), COVERAGE
GAP block empty, PAIRTEST PASS. Autoplay ×10 clean (3 WIN / 7 LOSE, no TIMEOUT).
`SIGHTLINE_BALANCE=10` → runs=20; against the same batch on base `3a20d18` the flywheel is
unmoved: win-rate 50→50, missions 83→83, policy gap 20→20, paired gap 20→20, concordant 8→8;
only micro-jitter in decision richness (meaningful choices/turn 2.757→2.737, lead swings/match
0.60→0.63, avg max swing 51.13→50.70). FIX 1 changes procedural ENVELOP geometry in ~1-4% of
builds, which is the size of that jitter.

---

# PROGRAM RESONANCE — WAVE W5 "THE FIRST HOUR AND THE FRONT DOOR"

**Branch** `wave/first-hour`, base `d350416`. Source: the 67-finding audit dossier —
`newplayer-1..7`, `visual-2`, `visual-7`, `wildcard-3`. **Balance-inert by construction and by
measurement** (see §W5-9); this wave takes no balance round and that identity is the proof.

**Thesis.** Ten programs tuned what the bot could measure. This wave fixed six things a person
would meet in their first hour and a bot never can: a story card that could not draw, a bloom
that ate the type, a difficulty on-ramp nobody was shown, a bar that moved under the cursor,
teaching that pointed at a cue the renderer no longer draws, and a game with no way out.

## W5-1 — THE HEADLINE DEFECT: mission 1's briefing could not draw. Measured at 0.00 s of 11 s.

The auditor traced the chain by code-read and said explicitly that they could not reproduce it.
**It reproduces.** The first job of this wave was to build the instrument, and the instrument is
`SIGHTLINE_BRIEFTEST=1` — the one self-test in the project that deliberately drives the LIVE,
persisting path, because `NoPersist` is exactly what hid the defect.

The chain, all four sites verified:

| site | fact |
|---|---|
| `Game.BriefAllowed` (Game.cs) | requires `TutorialText == null` |
| `Game.SetupMission` | arms the mission-1 strip on the same frame `BeginBriefing` composes the card |
| `Game.UpdateBriefing` | `if (Stats.CombatLog.Count > 0) { BriefLines = null; return; }` |
| `Stats.Log` / `Anim.cs` | ALWAYS-ON, called from every shot resolution by either side |

So the card **held** — never burning its 11 s clock — for the whole strip, and was then destroyed
by the first exchange, or by `BriefHoldMax` at 45 s, whichever came first.

One correction to the auditor's account, and it makes the defect *broader*, not narrower.
They wrote that `TutStepFire`'s only exit is `_tutShot`, so the strip cannot end without a shot.
`Game.EndPlayerTurn` also advances it — `if (TutStep >= 0 && TutStep < TutStepDone)
AdvanceTutorial()` — so the strip clears after four END TURNs regardless. The card still never
draws: four player turns plus four enemy turns is not free, and by then either the log has an
entry or the 45 s hold has expired. The defect is real; the mechanism is the *duration* of the
strip, not an infinite one.

**Why ten programs never saw it.** Every harness path sets `NoPersist`, `StartTutorialMaybe`
returns early under `NoPersist`, so **every mission-1 screenshot ever taken showed the briefing
precisely because the tutorial was not running.**

**Measured, on the live path:** `briefShownOnlyFor0.00sOf11s`. Not "reduced". Zero.

**The fix is ORDERING, not content** (DESIGN §1.1 caps the narrative frame; this makes an
existing card reachable, it does not write more of it). The board is not contested on turn 1, so
the briefing is a genuine pre-fight beat: `StartTutorialMaybe` arms the strip **pending**
(`Game.TutPending`), `UpdateTutorial` opens it the frame the card retires. `OnboardingActive`
counts `_tutPending`, so the action bar does not flicker whole-then-staged across the 11 s. Any
key or click still dismisses the card, so a player who wants to move reaches the lesson in one
input. `TutStepFire` also gains the turn-count patience fallback its three siblings have had for
waves (CONCEAL 2 / MOVE 3 / OVERWATCH 6 / now FIRE 9).

`SIGHTLINE_FIRSTRUN=1` is the new screenshot seam: it arms the strip under `NoPersist` so a frame
can show what a first-ever player actually sees. Screenshots before/after confirm it — before,
`TRAINING 1/5`; after, `BRIEFING - ELIMINATE` with the bar still staged; at frame 760, the strip.

## W5-2 — THE BLOOM WAS EATING THE TYPE. The HUD comes out of the BLOOM SOURCE.

`Display.RenderFrame` rendered the **entire** frame into the post-FX target, so bloom, vignette
and chromatic aberration ran over every button, label and panel — and a saturated UI plate
bloomed into its own label. It was also quietly undoing wave V1's two-atlas font work.

Re-measured with an explicitly stated method (inset the button rect 8 px so the plate border is
excluded; glyph core = p2 of a 3×3 MIN-filtered relative luminance, plate fill = p98 of a 3×3
MAX-filtered one; WCAG ratio between them — the min/max filter erases antialiased edge pixels so
the two readings are the glyph INTERIOR and the plate INTERIOR):

| main-menu label | FX off | **FX on, the shipped defect** |
|---|---|---|
| **TRAINING OP** (the on-ramp button) | 8.67 | **2.19** |
| CONTINUE RUN | 7.07 | 5.77 |
| DEPLOY SQUAD | 7.07 | 5.66 |
| LAST STAND | 5.79 | 6.38 |

(The post-fix column is further down, with the two candidate fixes side by side.)

The absolute numbers differ from the auditor's (4.51 → 1.35 on TRAINING OP, 5.34 → 3.42 on
CONTINUE RUN) because the sampling methods differ; they agree exactly on the headline — TRAINING
OP collapses ~4× under post-FX and fails 4.5:1 outright — and disagree at the margin, where the
auditor's method also failed CONTINUE RUN and mine does not. **The method is stated so the number
is quotable.**

**And this is the SHIPPED resting configuration, not a combat spike.** `SIGHTLINE_POSTFX=1`'s
boot-time "demo bloom" injection (`BloomIntensity = 0.85`) is overwritten on frame 1 by
`Game.Update`'s own `SetPostFxParams(_postFxBloom, …)`, which rests at 0. What was measured is the
fragment shader's always-on `bloomAmt = 1.45` baseline halo. **That comment has been stale for
waves** — the hook does not do what it says.

**The fix** is a split — but on the **bloom source**, not on the render target, and the
distinction is load-bearing. `Display.RenderFrame(board, hud)` now runs:

1. `board` — the board, its death-flash, and the overlay screens' animated backdrop
   (`Hud.DrawBackdropLayer`, split out of the six screen builders) — into `_target`;
2. `BuildBloom()` on **that**, so the bright-pass sees only atmosphere;
3. `hud` into the **same** target, on top, contributing nothing to the glow;
4. one composite, exactly as before.

**The first version of this wave did what the brief literally asked** — HUD drawn after the
composite, straight onto the backbuffer, through a `Camera2D` carrying the blit's scale+offset for
the letterboxed path. It measured beautifully (TRAINING OP 8.87:1) and **it broke accessibility.**
`uBright` and `uGamma` live in the composite shader, so the pause menu's BRIGHTNESS and GAMMA
applied to the board and not to the chrome. Screenshot at BRIGHTNESS 70%: the board dimmed and the
HUD did not — a regression aimed squarely at the player those settings exist for. The colour-grade
seam and the bloom seam are not the same seam, and only the bloom one was ever the defect.

Painting the chrome **into** the target after the bright-pass fixes the defect and keeps
brightness, gamma, the biome grade and the vignette uniform across the whole frame. It also needs
no second render texture (so no double-applied source alpha on translucent panels) and **no
letterbox-blit camera** — the `Camera2D` the abandoned first attempt would have needed to place
the chrome on the scaled backbuffer was never written, not deleted. *(W5-FIX, correcting this
paragraph's own wording: the board's shake/zoom camera is UNTOUCHED and still there —
`Game.ViewCamera` and the `BeginMode2D(ViewCamera(true))` around `Renderer.DrawBoard` at
`Game.cs:7629/7658`. Do not go hunting for a removed camera; nothing was removed.)* And it
measures **better** than the version that left the frame entirely — the composite's tonemap
deepens a dark glyph against a bright plate rather than washing it:

| main-menu label | FX off | FX on, BEFORE | HUD outside FX entirely | **shipped (out of the bloom)** |
|---|---|---|---|---|
| **TRAINING OP** (the on-ramp button) | 8.67 | **2.19** | 8.87 | **10.76** |
| CONTINUE RUN | 7.07 | 5.77 | 7.24 | 8.99 |
| DEPLOY SQUAD | 7.07 | 5.66 | 7.07 | 8.99 |
| LAST STAND | 5.79 | 6.38 | 5.79 | 6.51 |

`Hud.BackdropOwnsFrame` skips the in-mission chrome on the screens with an opaque backdrop.
Those screens used to draw the top/bottom bars and then bury them under the backdrop; with the
backdrop in the bloom-source pass they painted straight over the main menu until this was added.
BARRACKS is deliberately absent — it draws no backdrop (it scrims the live board), so its frame
order is unchanged.

> **CORRECTION (W5-FIX, review blocker 1).** This paragraph originally read "BARRACKS **and AUDIO
> CHECK** are deliberately absent — neither draws a backdrop". That was false of AUDIO CHECK and
> the false premise WAS the bug: `Hud.DrawAudition` opened with its own `DrawTacticalBackdrop`
> call, in the CHROME pass, i.e. after `BuildBloom` — so the composite added the LIVE BOARD's glow
> straight through an opaque screen that ships post-FX ON and is reachable from the pause card
> mid-mission. AUDIO CHECK is now in the registry with the rest; see §W5-FIX-1.

**And one more thing the split broke, found by looking at a screenshot rather than by a test.**
PAUSE, the tag editor and the whole BARRACKS modal family (requisition / perk / spec / boon /
field event) dim the live board with a full-screen wash drawn from the CHROME pass — which now
runs *after* `BuildBloom`, while the composite ADDs `glow * 1.45` on top of whatever the chrome
laid down. So the scrim darkened the board and the board's own glow punched straight back through
it: the pause card ended up with the squad's cyan halos blooming over its own scrim, which is the
exact opposite of what a scrim is for. `Hud.BoardScrimAlpha` now lays the same wash in the
bloom-source pass, so the bright pass never sees the glow. The board consequently takes the wash
twice and reads darker under a modal than it did pre-W5 — deliberate, and the better of the two
available errors, because the card is the focus. Three-way crop (pre-split / split-without-this /
shipped) confirmed it by eye.

**One accepted side effect, recorded rather than hidden.** `PruneAnims` forgets a panel's entrance
key when it is not drawn in a frame ("re-animate on re-show" — its own comment). The in-mission
chrome used to be drawn *and buried* under an overlay screen's backdrop, so its keys stayed warm;
now it is skipped, so returning to the board from the FIELD MANUAL mid-mission re-runs the
roster/top-bar entrance tween (~0.2 s). That is the same transition every other screen return
already plays, and it reads as a transition rather than a pop — but it is a behaviour change this
wave introduced, and it is here so the next person does not have to rediscover why.

**THE BOARD IS UNCHANGED, and the measurement had to be done twice to say so honestly.** Paired
comparison — *same binary*, only `SIGHTLINE_HUDINFX` toggled, same seed, post-FX on, seven board
patches. Every patch reads within its own frame-to-frame animation swing, and the two largest
apparent deltas are that swing, not a render change:

- The gold objective tile first read **−7.0 mean** against the pre-W5 leg, which looked like a
  result against a same-build run-to-run floor of ±2.9. It is not. That marker *breathes*: over
  four adjacent frames of the **same build** (86 / 90 / 90-again / 94) its patch mean spans
  **93.9 → 112.8, a range of 18.9**, and the pre-W5 leg's 103.8 sits inside it. The harness fixes
  `dt = 1/60` but every animation is driven by wall-clock `GetTime()`, so a build that reaches
  frame 90 a few milliseconds later samples a different phase. **A screenshot delta is not a
  result until you have measured the animation's own swing at the same pixels.**
- The selected soldier's token: +6.6 against a same-build frame swing of 5.9. Same story.
- Peaks on all four glowing objects — soldier token, gold objective, supply crate, dormant pod —
  are **identical** (245.8 / 254.0 / 242.1 / 254.0), and bare floor and the cover run move by
  ±0.03 mean.

## W5-3 — THE DOORS.

**The end cards banked salvage and never mentioned the War Room** (`newplayer-2`). The loss card
is the highest-leverage retention moment in the product: the player has just lost their first
squad and is deciding whether there is a second run, and it showed them the number 18 with no
meaning and no route. Now: a third plate **WAR ROOM [W]**; *"spend it in the WAR ROOM"* centred
under the SALVAGE slab it explains; and the SURVIVING SQUAD panel headed **"N JOIN THE RESERVE -
recallable at the next draft"** with each survivor's recall price.

`Game.EndReserve` is the **delta** of `SaveGame.VeteranCount()` across `EnshrineVeterans`, never
`vets.Count`, so the card structurally cannot over-claim: a name already in the reserve does not
re-join, and a MERCENARY CLAUSE run (which enshrines nobody) reads 0 with no special case.
METATEST asserts both legs.

The button gets its own `Hud.EndWarRoomBtn` rect rather than reusing the intro's `OverlayBtn3`
(LAST STAND). The two screens publish into the same statics, input runs before draw, and a stale
end-card rect surviving one frame into the intro would turn the door the player just used into an
accidental LAST STAND.

**There was no way to quit the game** (`wildcard-3`). 18 pause controls, 9 menu entries, no exit —
and `SetExitKey(KeyboardKey.Null)` is load-bearing (ESC cancels a targeting mode and opens the
pause card), so ESC could not do it either. The only sanctioned ways out were ABANDON RUN, which
destroys the run, or alt-F4. Now **QUIT TO DESKTOP [Q]** on the pause card, arm-then-confirm, with
the honest line *"the current mission restarts from its start"*; and **QUIT [Q]** on the main menu
with no confirm (nothing is in flight on the title screen). It also completes the intro's utility
grid into a 3×2 instead of a lone centred AUDIO CHECK. `[Q]` was verified unbound by grep before
being claimed — note that CLAUDE.md's free-key list and the `Game.cs` keymap comment are both
stale (`wildcard-4`, not this wave's fix).

The harder half is now **a written decision**: `docs/DESIGN.md` §5.1 records mission-restart-on-
quit as chosen deliberately, with the argument (a mission is a coffee break; a board DTO is a new
persisted format in a project burned by persistence twice; and quit-anywhere-resume-anywhere is a
save-scum surface that prices against the "stakes that bite" pillar) and the conditions under which to
revisit it.

## W5-4 — THE ON-RAMP. A zero-run profile now opens on RECRUIT.

`newplayer-4`: RECRUIT (rung −1) was always selectable and always **unlabelled** — nothing at level
0 hinted anything existed below it, and the copy *"standard difficulty - the designed fight"*
framed 0 as the floor. The archived X2 ladder (n=40/rung, base `a61ef42`,
`docs/measurements/x2/`) puts RECRUIT at **75.0%** run completion against heat 0's **57.5%** — a
17.5-point gap, outside the ±6–8 error bar. Roughly two in five first campaigns were ending in a
loss the on-ramp exists to prevent.

`Game.FirstTimeProfile` (from `SaveGame.LoadRunTotals`) defaults `PendingHeat` to RECRUIT and
rewrites level 0's hint to *"[<] for a gentler first run"*; **"< RECRUIT"** names the rung below
zero on every profile. **This moves a DEFAULT, not a rung.** Every heat number in `docs/` is
untouched, and the measurement harness sets heat explicitly under `NoPersist`, which returns from
`EnsureMetaLoaded` before the default can be read. ONRAMPTEST asserts both the fresh-profile
default *and* the control: a profile that has finished a run keeps heat 0.

## W5-5 — THE CHROME.

**The action bar re-flowed between turns** (`visual-7`), so no verb had a stable position — the
auditor measured OVERWATCH moving from bottom-row slot 8 to **top-row slot 1** purely because a
squadmate went down and STABILIZE appeared ahead of it in the list. The layout is a greedy wrap
that fills row 0 and stacks later rows *above* it, so **appending never moves anything already
placed**. That makes the fixed slot map cheap: everything whose *presence* can change between two
turns of one mission moves to the tail (BEACON, one-way once planted; STABILIZE, with a downed
mate; SHOW ALL, with the onboarding); everything ahead of it is per-mission / per-soldier
constant. No empty ghost cells, no permanently dead buttons. The ability slot also reserves its
`" (N)"` cooldown suffix — a slot that changes **width** shifts its neighbours just as surely as
one that appears, and that one moved ten buttons. And the bar gets **one backing plate**: twelve
chips floating over the battlefield with board texture and gold overlay lines running between them
was the untidiest composition in the in-mission UI.

**The CONCEALED pill faded to 10% alpha** (5% on the border), so the opening state read as *off*
for part of every 1.8 s cycle. Concealment is the first rule the onboarding teaches and the one
that decides where the whole first fight starts; the auditor and a reviewer before them both
misread a trough-phase frame as "not concealed". Now 0.56–1.00 (a 1.79× swing against 10.0×). The
renderer and the test read **one** expression, `Hud.ConcealPulse` — a test that re-implements the
curve it guards proves nothing.

**Doctrine card text overflowed its box** on the first screen a new player touches: the height was
FIXED at 74 while the body wrapped from y+38 in 19 px steps. Now sized to content and evened
across the row. **Measured: ten of the sixteen boons in the catalogue overflowed the old fixed
height**, not just the one the auditor caught. Its second half — the class-glyph disc eating the
operator blurb's last words on FLINT/NOX/BRIAR — is fixed by dropping the disc into the true
corner so the blurb row clears it entirely, rather than by clipping the sentence, which is the
point of the row. One blurb (ASSAULT's) was four characters too wide for its column even then and
was trimmed.

## W5-6 — THE WORDS.

**The first sentence of instruction in the game pointed at a cue that no longer exists**
(`newplayer-5`). Both the Training Op's first lesson and the campaign strip's MOVE card said
*"click a glowing tile"*. Wave V deliberately replaced the flood-fill with a thin cyan contour and
a corner-tick lattice the code itself calls ~0.6% of a tile's area — a 5/255 modal inner lift.
Both strings now name the **outline**, the **corner ticks** and the **dashed** outer ring.

**Three verbs were taught only by a 9-second card that burns forever** (`newplayer-7`).
`UpdateFieldTips` marks a tip seen the instant it shows, per-profile and permanent, with no replay
surface — and SHOVE appeared **nowhere** in `src/Codex.cs`, nor did any of the four utility items,
and a grep for `CONTROLS|KEYBIND` over `src/` returned nothing. Now: two FIELD CRAFT rows (SHOVE,
with `Game.ShoveReach` and both collision damages interpolated from the real constants like every
other row in that tab; and UTILITY ITEMS, all four kinds by class), and a **VERBS & KEYS** tab —
16 verbs with hotkeys plus three rows for the bindings that live nowhere else. It is **generated**
from `Hud.VerbTable` + `Hud.VerbHelp`, i.e. from the same `ActionDesc` switch the bar's hover
tooltip reads, so a verb's help and its manual entry cannot drift apart. `ActionDesc` is now
null-safe: its four situational branches fall back to a generic sentence with no live game.

## W5-7 — Four new gates, and each one FAILS on the pre-W5 tree.

A test that cannot fail is not a test. Every one below was falsified by flipping its off-switch:

| hook | falsifier | what it printed |
|---|---|---|
| `SIGHTLINE_BRIEFTEST` | `SIGHTLINE_BRIEFFIRST=0` | `stripNotArmedPending, stripOpenedOverTheBriefing, briefShownOnlyFor0.00sOf11s, briefNeverRetired, fireStepHasNoPatienceFallback` |
| `SIGHTLINE_CONTRASTTEST` | `SIGHTLINE_HUDINFX=1` | `TRAINING_OP@2.21` |
| `SIGHTLINE_CHROMETEST` | `SIGHTLINE_OLDCHROME=1` | `moved(mateDown):overwatch,focusow,brace,hunker,reload` · `abilitySlotGrewOnCooldown` + 10 more `moved(abilityCd)` · `pillFloor=0.10 pillSwing=10.00x` · `boonOverflow` ×10 |
| `SIGHTLINE_QUITTEST` | (new surface; no pre-W5 form) | — |

CONTRASTTEST boots a real 1280×800 window with Display and PostFX on and reads the framebuffer
back; a screen read is the only honest instrument, because the whole defect lived in the
composite. CHROMETEST **loads the real font atlases before measuring** — a gotcha worth recording:
without them `Cfg.Measure` falls back to raylib's default face, whose narrower metrics fitted
every doctrine description on one line, and the overflow leg silently could not fail.

CODEXTEST gained the audit's assertion (every id in the verb table has help text, a hotkey and a
manual entry; FIELD CRAFT carries SHOVE and UTILITY ITEMS — `shove` and `item` failed it before).
METATEST gained the reserve-delta anti-over-claim. ONRAMPTEST gained the zero-run default.

## W5-8 — Verification.

`dotnet build -c Release` 0 warn / 0 err. `bash scripts/qa-sweep.sh --full`: **51/51 PASS**,
COVERAGE GAP block empty, `PAIRTEST: PASS`. *(That 51 was itself the miscounted footer — see
§W5-FIX-3. The shipped tree runs 55; the sweep prints its own number.)* Autoplay ×3: LOSE m3 / LOSE m5 / LOSE m6 — no TIMEOUT,
no exception. Screenshots read and judged: first-run mission 1 before/after (+ the strip at frame
760), intro with FX before/after, board with FX before/after, both end cards, pause card, draft
(cards + doctrine row), FIELD MANUAL's new tab, WAR ROOM, barracks.

## W5-9 — The inertness proof.

This is a UI/teaching wave and it must not move a gameplay number. Proved two ways:

1. **`SIGHTLINE_PAIRTEST: PASS`** — byte-identical CRN pairing. The render split takes no draws.
2. **A pinned-slot balance batch is FIELD-FOR-FIELD IDENTICAL to the branch point.**
   `SIGHTLINE_BALANCE=5` (greedy+sloppy → `runs=10`, asserted in both), `SIGHTLINE_BALANCE_BASE=120`,
   `SIGHTLINE_BALANCE_HEAT=0`, Release binaries run directly under `xvfb-run` with per-tree
   `XDG_CONFIG_HOME` and `SIGHTLINE_BALANCE_JSON`. Baseline built from `git archive d350416`.
   Both JSONs: `runs=10, missions=44, runWinRate=60, avgMissionsCleared=5.1` — and a
   key-by-key diff over all **670 leaf fields** of the two documents reports **0 differing**,
   including every nested `byHeat` / `byMission` / `byObjective` / `byArena` / `byDeploy` /
   `decisionRichness` / `policyGap` / `lossCauses` block. Re-run after the render was
   re-architected mid-wave (see §W5-2); still 0.

The one behavioural default this wave *does* move is the fresh-profile difficulty rung (W5-4),
and it cannot reach the instrument: the batch sets heat explicitly, and `EnsureMetaLoaded` returns
under `NoPersist` before the default is read. That is why the two JSONs are identical rather than
merely close.

## W5-10 — WHAT I DID NOT DO, and what it costs.

- **CUT: `visual-6` / brief item (g) — unifying the intro's four button families onto one system**
  (dark plate + coloured left rule, one filled treatment for the single primary verb, hotkey
  badges in a fixed column, LAST STAND's reserved danger red demoted to a rule). The brief marked
  it droppable-last and I dropped it. Two reasons, one good and one honest: the half of it that
  was *measurable* — the labels washing out — is fixed by W5-2 and now reads 6.51–13.65:1 with FX
  on; and the rest is a substantial aesthetic redesign of the storefront screen whose only
  reviewer this session is my own screenshot, where the downside (flat saturated primaries are
  loud, but they are also the clearest call-to-action on the page) is a judgement I would be
  making alone. **Cost: the intro still stacks three button styles across four widths, and the
  frame's top-1% chroma is still ~189 against a board at ~100.** The finding stands, unfixed.
- **The `EndReserve` count and the priced rows can disagree in one narrow case.** The header shows
  new joiners (the delta); the per-row prices are shown for every Rank ≥ 1 survivor. A run whose
  survivors were *all* already reserve records reads "0 join" with priced rows above it — correct
  on both counts, and the header is simply omitted at 0, but it is not the same number. I chose
  the anti-over-claim delta the gate asked for over the friendlier count.
- **The board's own bloom was verified INSIDE the measurement noise, not proven identical.** The
  screenshot harness has never been byte-stable (CLAUDE.md documents why), so "unchanged" here
  means "smaller than the same build's own frame-to-frame swing at the same pixels", with both
  numbers stated. A stronger claim would need a deterministic render path this project does not
  have. I also got this wrong once before getting it right: the first reading looked like a real
  −14.9 on the gold objective tile, and only measuring that patch across four adjacent frames of
  one build showed its natural range is 18.9.
- **The HUD is still inside the colour grade, so the audit's SECONDARY visual-2 finding stands.**
  Chromatic fringing on HUD text edges (+16-66%) and the ~14% edge-luminance drop are untouched;
  they come from the composite's CA and vignette, not the bloom. Taking the chrome fully out of
  the grade is what broke BRIGHTNESS/GAMMA in this wave's first attempt, so doing it properly
  needs a third pass (grade-only shader over a second target) that costs a full-screen RT and a
  blit per frame — on a game with no frame-time instrument (`wildcard-7`). Left open in ROADMAP
  with the shape of the fix written down.
- **A modal now sits over a DARKER board than it did pre-W5.** PAUSE / the tag editor / the
  BARRACKS family take their full-screen wash twice — once in the bloom-source pass so the glow
  cannot punch back through it, once in the chrome pass so the HUD still dims. Reproducing the
  old brightness exactly would need the two alphas re-solved so their product is the original
  (0.576 each for 0.82), which would leave the HUD brighter behind the card than before. I took
  the darker board: the card is the focus, and the alternative error was visible.
- **`SIGHTLINE_POSTFX=1`'s stale demo-bloom comment is documented above but not fixed.** Removing
  the dead injection is a one-liner; it belongs with whoever next touches that hook, and changing
  it now would have made my before/after contrast pair non-comparable.
- **No human has played any of this.** Every judgement here is a screenshot read by the agent that
  wrote the code, which is exactly the gap `wildcard-6` names. The gates are real; the *taste*
  calls (the bar's backing-plate alpha, the pill's new floor, the disc's new corner) are not
  measured and are not claimed to be.

# W5-FIX — THE REVIEW BLOCKERS (2026-08-29, same branch `wave/first-hour`)

Four independent reviewers plus an adjudicator re-verified the wave above and passed it with
**five blockers**. The load-bearing safety claims all held and none of them was touched:
brightness/gamma uniform across board and chrome (swept BRIGHT 0/2/4, GAMMA 0/4), screen shake
still moving the board and not the HUD (39 frame pairs by SAD registration), colorblind on both
paths, no new render target, every new self-test stash-and-restoring a real profile
byte-identically, PAIRTEST PASS, inertness reproduced twice. What follows is the repair of the
five, plus four cheap evidenced items the review also raised.

**Base for every number below:** `d350416` (the branch point) built fresh from `git archive`, and
`1444213` (the wave's own HEAD) built the same way as the "pre-fix" leg. Both under
`xvfb-run -s "-screen 0 1280x800x24"`, Release binaries run directly.

## W5-FIX-1 — THE RENDER SPLIT LEFT AUDIO CHECK BEHIND, and three comments asserted the opposite.

`Hud.DrawAudition` opened with its own `DrawTacticalBackdrop(g.AudClock, Pal.Accent, 0f)`. That
call sits in the CHROME pass — which W5-2 moved to run **after** `Display.BuildBloom` — so the
bright pass never saw the audition screen at all. It saw the **live board**, and the composite
then added `glow * 1.45` from it straight through an opaque screen. This is precisely the
punch-through that commit `439b386` fixed for PAUSE and the BARRACKS family, applied to none of
AUDIO CHECK. The screen ships reachable from the intro `[U]` **and** from the pause card
mid-mission, and `Display.PostFX` defaults true.

**Why nothing caught it: the false premise WAS the bug.** `src/Hud.cs:207-208`, `src/Hud.cs:2551`
and `docs/DEVLOG.md:5540` all said BARRACKS and AUDIO CHECK "are deliberately absent — neither
draws a backdrop". One of those two names was wrong, and every reader of that sentence — including
the wave that wrote it — took it on trust.

**Measured.** The audition screen is deliberately clock-free (it runs off `Game.AudClock`, a dt
accumulator, not `GetTime`), which makes it the one screen in the game where two builds are
directly comparable frame-for-frame:

| `SIGHTLINE_AUDITION=1 SIGHTLINE_SHOT=90`, base `d350416` vs the tree | px > +20 luma | px > +40 | peak |
|---|---|---|---|
| post-FX **OFF**, pre-fix | 0 (**byte-identical**, mean \|dL\| 0.000) | 0 | — |
| post-FX **ON**, pre-fix — *the defect* | **17,010** | **8,640** | **+154.1** |
| post-FX **ON**, fixed | **0** | **0** | **+7.0** |
| post-FX **OFF**, fixed | 0 (**still byte-identical**) | 0 | — |

Opened from the pause card mid-mission (`SIGHTLINE_MISSION=1`) the defect is the same size —
17,214 / 8,899 / +166.0 before, 0 / 0 / +7.0 after. The residual +7.0 is the intended W5-2 change
(the screen's plates and type are legitimately out of the bloom now); the >20 luma column going
to zero is the fix. By eye it is unmistakable: cyan and amber blobs — soldier tokens, the
objective marker, a supply crate — floating over the MASTER and MUSIC fader rows.

**The fix, and the gate that makes it structural.** `Phase.AudioCheck` joins the registry and
`DrawBackdropLayer`'s switch, drawn off `g.AudClock` and **not** `GetTime` (reading the clock here
would have cost the screen its determinism, and with it the byte-identity that made this
measurable). The call in `Hud.Audition.cs` is gone. All three false sentences now name BARRACKS
alone.

That closes the instance. **`SIGHTLINE_BACKDROPTEST` closes the class**, and it is the gate the
review asked for by construction rather than by inspection. CONTRASTTEST reads nine main-menu
labels; it could not have caught this and cannot catch the next phase added without a
`DrawBackdropLayer` entry. BACKDROPTEST does not check a screen, it checks the invariant, over
every `Phase` that exists:

- **(A)** no phase paints a full-screen backdrop from the CHROME pass — `Hud.BackdropPaints`, a
  counter incremented inside `DrawTacticalBackdrop` itself, must read 0 after a real `Hud.Draw`;
- **(B)** `Hud.BackdropPhase` (the registry `BackdropOwnsFrame` now derives from) and
  `DrawBackdropLayer`'s switch are the **same set** — in-registry-but-paints-nothing loses a
  screen its top bar, paints-but-not-in-registry paints the in-mission chrome over the main menu;
- **(C)** the modal scrim doubles up only where the composite runs.

`SIGHTLINE_AUDBACKDROP=1` restores the defect: `BACKDROPTEST: FAIL (backdropFromChromePass:AudioCheck)`.

## W5-FIX-2 — THE CONTENT-SIZED DOCTRINE CARD PUSHED THE DEPLOY ROW OFF THE SCREEN.

W5-5 made the doctrine card size to its text (74 → 105 px for a 3-line boon, which FIELD DRILLS
is) and every row below it moved down by the difference, because `DraftConfirm` derives from the
contract row which derives from the boon row. At the **default 100% text size**,
`SIGHTLINE_DRAFT=1 SIGHTLINE_SEED=808`: BACK / SELECT 4 MORE / RE-ROLL POOL sliced through the
middle by y=800, `[Esc]` gone. At `SIGHTLINE_UISCALE=3` (120%) the entire row was off-screen while
`d350416` still rendered it. **One overflow was traded for a worse one** — and RE-ROLL POOL had no
keyboard route at all, so at 120% a real control was unreachable, not merely awkward.

**The fix reclaims the height instead of paying for it.** `Hud.DraftLayout(boonLines, contractH)`
computes the whole vertical stack **before anything is drawn**, so the candidate grid participates
in the reclaim rather than pinning everything below it, and gives up the eight squeezable gaps in
a fixed order of least harm. The floors are not taste: each is set by the type that gap CARRIES at
120% — g[0] clears the "N / 4 SELECTED" counter, g[3] "STARTING DOCTRINE", g[5] "RUN CONTRACT",
g[7] the FIRST OP / HEAT preview line. The first attempt squeezed g[5] to 14 and collided "RUN
CONTRACT" with the top border of its own cards on the very next screenshot; the shipped floors are
that screenshot's answer.

The doctrine row also **widened to exactly the RUN CONTRACT row's width** (296 → 395 px cards).
That is a composition fix in its own right — 932 px against the row directly beneath it at 1230 px
was the one mismatched width on the screen — and it pays for itself, because a 395 px card wraps
FIELD DRILLS in three lines where 296 px needed four at 120%, and four lines is what put the row
off the bottom. The height came out of horizontal slack the screen was already wasting.

**The gate.** CHROMETEST now asserts the DEPLOY row is on screen for **all 16 boons × all 4 text
sizes**, plus that the doctrine card clears the contract header and the contract row clears the
info line. It reports its own headroom rather than merely asserting there is some: *tightest
FDR@90% with 0 px to spare, max gap squeeze 46 px* — and note that "0 px to spare" is against
`ScreenH − DraftBottomPad`, i.e. there are still 8 real px below it. `Overflow > 0` (the stack
cannot fit even fully squeezed) is a hard fail, so the failure mode of a future copy edit is a red
test, not a sliced button; a last-resort clamp keeps the row on screen even then.

`[R]` now re-rolls the pool. The draft phase read only Esc and Enter, so R was free there, and it
is the same mnemonic RELOAD uses in the fight.

**The review's third item on this leg was right too: CHROMETEST leg (C) was TAUTOLOGICAL.** It
compared `need = BodyTop + lines*LineH + PadB` against `DraftBoonCardHeight(lines)`, which *is*
`Math.Max(74, need)` — the assertion could not fail for any string whatsoever, which is why "ten
of sixteen boons overflowed" was found by a human reading a screenshot and not by the test that
claimed to cover it. It now measures where the last line's **ink** actually lands, through the
real font at the live UI scale (`Hud.DraftBoonInkBottom`), against the height the renderer uses.

## W5-FIX-3 — THE DERIVED SWEEP COUNT WAS WRONG IN BOTH MODES.

W5 replaced a hand-typed footer with a derived one and got the derivation wrong: the anchor was
`^echo`, but PAIRTEST's invocation is **indented** inside the `--full` block, so the grep returned
53 where 54 invocation lines existed. `--full` printed 53 while 54 ran; plain printed 52 while 53
ran. A derived counter that skips indented lines is a hand-maintained counter wearing a grep —
this footer's number has now been wrong **five** times in this project's history.

Anchor is `^ *echo`. Proven by adding a hook (BACKDROPTEST) and re-running both modes: the file
holds **55** matching invocation lines, `--full` prints **55** and lists 55, plain prints **54**
and lists 54.

## W5-FIX-4 — THE §1.1 PRIORITY CHANGE IS NOW A RECORDED AMENDMENT (`docs/DESIGN.md` §1.2).

DESIGN §1.1 states as a non-negotiable limit that the briefing card yields the shared card slot to
the teaching layers **absolutely**. W5-1 arms the mission-1 lesson strip PENDING and makes teaching
wait up to 11 s behind the flavour card. The literal never-simultaneous invariant survives — the
`else if` chain in `Hud.Draw`, `BriefAllowed`'s `TutorialText == null`, and `BarksAllowed` are all
untouched — but the stated PRIORITY is inverted for a first-time player's first eleven seconds,
which is the exact window that limit exists to protect. W5 wrote a full recorded amendment (§5.1)
for the much smaller mid-mission-checkpoint decision and left this one as a code comment.

**The change is right and §1.2 argues it on its merits:** the rule as written produced **zero**
briefings, not a delayed one (`briefShownOnlyFor0.00sOf11s`), so an absolute yield to a layer that
never ends is a deletion rather than a priority; the two layers are not competing for the same
moment, because on turn 1 of mission 1 nothing is contested; and any key or click reaches the
lesson in one input, so teaching is deferred and never withheld. §1.2's limits are the load-bearing
half — **mission 1 of a first-ever campaign only**, the never-simultaneous invariant still
absolute, ORDER bought and not CONTENT, and `SIGHTLINE_BRIEFFIRST=0` keeping it falsifiable. §1.1's
fourth bullet now carries the cross-reference so the rule cannot be read without the amendment.

## W5-FIX-5 — BRIEFTEST DID NOT OBSERVE WHAT IT CERTIFIED.

`Watch()` read `Game.BriefTimer` / `Game.BriefLines` — the model's own `BriefAllowed` predicate,
read back — and never touched `Hud.DrawBriefCard`. A reviewer put a one-line `&& false` on the
dispatch at `src/Hud.cs:250` so the card can never be drawn; it built clean, 0 warnings, and the
test **still PASSed with the full 11 s**. The shipped behaviour was never in dispute; the point is
that this wave's whole thesis is that a defect hid because nothing observed the draw side, and its
own headline gate had the same shape.

Every watched frame now paints a **real frame** through `Hud.Draw` inside the test's existing
64×64 window (which is why `SIGHTLINE_BRIEFTEST` now loads the font atlases) and counts
`Hud.BriefCardDraws`, a counter incremented inside `DrawBriefCard` itself. Both the first-ever leg
and the returning-player control assert it. Re-running the reviewer's mutant:

```
BRIEFTEST: FAIL (briefCardDrawnOnOnly0framesOf630,controlBriefCardDrawnOnOnly0frames)
```

Note what did **not** fire: every model-side assertion still passed under the mutant. That is the
measurement of how blind the old form was. Cost: the test went from ~1.5 s to ~2.7 s.

## W5-FIX-6 — the four cheap evidenced items.

- **The double scrim now applies only where its justification does.** `Hud.BloomScrimAlpha` gates
  the bloom-source wash on `Display.Enabled && Display.PostFX`. With post-FX off there is no
  bright pass to attenuate and nothing to punch back through, so the second wash was pure loss.
  Measured on a pure-board strip under the pause card (x 40-300, y 200-620, `SIGHTLINE_PAUSE=1
  SIGHTLINE_MISSION=1 SIGHTLINE_SEED=4242`): base `d350416` **18.56** → pre-fix **12.08** (−35%)
  → fixed **18.55**. Post-FX ON is deliberately unchanged: 10.09 → 10.08. W5-2's "the board takes
  the wash twice" trade still stands where the composite runs, and nowhere else.
- **`Display.RenderFrame` no longer allocates the combining `draw` closure on the post-FX path**,
  where it was constructed every frame and never invoked (the split path calls `board()` and
  `hud()` separately). Two managed allocations per frame for nothing; it is now built after the
  post-FX early return. Unmeasured as a frame-time win — this game has no frame-time instrument
  (`wildcard-7`) — and claimed only as the removal of dead work.
- **The structural gate** is `SIGHTLINE_BACKDROPTEST`, described under W5-FIX-1.
- **The DEVLOG's camera sentence is corrected.** §W5-2 said the shipped fix "needs no second
  render texture … and no camera path", which reads as *the Camera2D path was deleted*. It was
  not. `Game.ViewCamera` and the `BeginMode2D(ViewCamera(true))` around `Renderer.DrawBoard` are
  intact at `Game.cs:7629/7658` and always were; what never existed is the **letterbox-blit**
  camera the abandoned first attempt would have needed. Both the DEVLOG paragraph and
  `Display.RenderFrame`'s header now say so, so a future session does not go hunting for a removed
  camera.

## W5-FIX-7 — Verification.

`dotnet build -c Release` 0 warn / 0 err. `bash scripts/qa-sweep.sh --full`: **55/55 PASS**,
COVERAGE GAP block empty, `PAIRTEST: PASS`, footer count **55** matching the 55 invocation lines.
Plain mode: 54/54, footer **54**. Autoplay ×3 on each run — WIN m6 / LOSE m1 / WIN m6 and
LOSE m2 / WIN m6 / LOSE m6, no TIMEOUT, no exception. Screenshots read and judged: AUDIO CHECK
post-FX on/off × three builds (base / pre-fix / fixed), mid-mission AUDIO CHECK the same way,
draft at 100% and 120% before and after, pause card post-FX off.

## W5-FIX-8 — WHAT I DID NOT DO, and what it costs.

- **`visual-6` is still open**, and so are both W5-2 residuals (the HUD is still inside the colour
  grade; `SIGHTLINE_POSTFX=1`'s demo-bloom comment is still stale). Nothing in this pass touched
  them and nothing here changes their argument.
- **The 120% draft screen still has fixed-pixel chrome under scaled type.** With the row back on
  the screen, "RE-ROLL POOL (10 SALV)" now *fills* its plate at 120% and the candidate-card blurbs
  still ellipsize there. That is DEVLOG §5364's known W5-era limitation (`Cfg.Scaled` moves the
  type, the plates are authored in pixels), not something this fix introduced or repaired — the
  blocker was reachability, and reachability is what is now asserted.
- **The draft stack's headroom at 90% text is 0 px against its own limit.** That is the authored
  layout landing exactly on `ScreenH − 8` with no squeeze at all, not a squeezed near-miss, and
  the 8 px pad is real screen. It is stated rather than smoothed over because the next person to
  add a line of doctrine copy needs to know how much slack they have: at 100% it is 0 px too, and
  the reclaim capacity is 51 px of gaps, of which the worst case already spends 46.
- **BACKDROPTEST drives `Hud.Draw` per phase with one Game in one state.** It proves no phase
  paints a backdrop from the chrome pass, which is the defect class; it does not exercise every
  branch inside every screen builder (a backdrop drawn only in some sub-state of BARRACKS would
  slip past it). The stronger form would drive each screen's real sub-states, and it is not built.
- **No human has played any of this either.** Same gap as W5-10's last bullet, unchanged.

## PROGRAM RESONANCE — Wave W9 "THE REPAIR" (dev; worktree `agent-ae64b077e9a007d39`, branch `wave/the-repair`)

**The thesis, and why it is uncomfortable.** This tree has 51 self-tests, all 51 pass, and one
adversarial QA pass found 20 reproducible defects. The tests are not bad — they are aimed at the
MODEL and not at the SEAM. Every entry in the brief names its own structural gap, and the gaps
rhyme: a rule written twice and tested once; a file the suite only ever reads after writing it
itself; a contract asserted in a comment; a verb whose two siblings are pinned and which has no
coverage at all. **A fix that does not close its gap is half a fix**, so every defect below ships
with a test that FAILS on the pre-fix tree — proven by reintroducing the defect, rebuilding, and
recording the failure tag.

Base: `d350416` (RESONANCE milestone 2). Fifteen defects assigned; **15 of 15 reproduced, 15 of 15
fixed**, plus one hard hang found while calibrating a fix that the brief did not have.

### REPRODUCTION RATE: 15/15

Every repro ran verbatim before any fix, from a wave-private data dir (the brief warned that one QA
script hard-codes a shared `/tmp` path and got cross-contaminated by another agent; this wave's
driver lives under the worktree). No phantoms.

| # | defect | reproduced |
|---|---|---|
| 1 | `{"Veterans":[null]}` kills NEW CAMPAIGN | NRE at `SaveGame.cs:474`, stack-for-stack |
| 2 | veteran with `Cls:null` kills NEW CAMPAIGN | ArgumentNullException at `Run.cs:1065` |
| 3 | `{"Legends":[null]}` kills the WAR ROOM | NRE at `Hud.cs:3118`, in the DRAW path |
| 4 | `MetaDto.SchemaVersion` written, never read | source-confirmed: 8 hits, no read |
| 5 | DMG row shows the RAW band | source-confirmed + ground-truthed by 40k rolls |
| 6 | LOCK-ON badge on any uncovered target | source-confirmed: `o.CoverLevel == 0` vs `flanked` |
| 7 | `ComputeOdds` arms the GUARDED telegraph | source-confirmed via `GrazeFloor` -> `HardenedReduce` |
| 8 | GRAPPLE self-rams the grappler | seed 3406: `VEGA hp=1->0 SLAM`, verbatim |
| 9 | a downed soldier fires its queued shot | seed 3406: `BUG: ShotAnim ... downed=True` |
| 10 | autoplay TIMEOUTs | seeds 2001 + 3001, both at frame 20000 |
| 11 | event recruit fields cap+1 | BENCHTEST leg pre-fix: `eventRecruitDeployed=5 want=4` |
| 12 | release strands a benched soldier | BENCHTEST leg pre-fix: `eventReleaseDeployed=3 want=4` |
| 13 | SKIRMISH/DAILY heat inert | MODETEST leg pre-fix: `skirmishHeatInert h0=4 h8=4` |
| 14 | WAR ROOM drops STANDING RESERVE | METATEST leg pre-fix: `drew=4/5`; screenshot confirms |
| 15 | SKIRMISH legend names unbound keys | grep: 2 hits in the codebase, both on the INTRO |

Defects 11-14 were reproduced by writing the missing test first and watching it fail with exactly
the numbers the brief reported in the wild (5/4, 3/4, 4-vs-4 hostiles, 4 of 5 cards) — which is a
stronger reproduction than a screenshot, because it is repeatable and it is now permanent.

### THE CRASHES (1-4) — `meta.json` holds ALL permanent progress and had no structural guard

`SaveGame.Load` has applied "parses fine but is unusable is corruption too" to `save.json` since
D2. `meta.json` — salvage, unlocks, achievements, the veteran reserve, the hall of fame, the heat
ceiling — had no analogue, so three hand-edit / disk-damage shapes each ended the process with an
UNHANDLED exception **on a primary entry button**, with no stash and no in-game recovery. The
asymmetry was the bug. Fixed at the single choke point (`LoadMetaDto` -> `SanitiseMeta`) so every
consumer is covered by one guard, plus cheap local guards for defence in depth.

**The SchemaVersion policy is now a DECISION, stated and pinned.** meta is deliberately
FORWARD-TOLERANT — the OPPOSITE of the run save's refuse-and-stash. A run save is one campaign;
`meta.json` is every campaign, and refusing it would hand a player who merely downgrades a build a
BLANK CAREER. Every MetaDto field is append-only and defaults inert, so an older build reads its
own fields correctly. The one real cost is the WRITE-BACK, which cannot round-trip fields this
build does not know about — so a future-stamped profile is **copied** (not moved) to
`meta.json.bak` before the first read-modify-write can drop them.

**Gap closed:** no self-test had ever read a `meta.json` it did not itself WRITE. VETTEST /
METATEST / SAVETEST all delete-or-stash the real file and then write a fixture through
`EnshrineVeterans`/`AddLegends`, which structurally cannot emit a null element;
`CorruptionSelfTest` fed exactly one hostile shape, an UNPARSEABLE string. `SaveGame.
MetaStructureSelfTest` (6 legs, dispatched from SAVETEST) writes RAW BYTES and drives the REAL
consumers.

### THE DISPLAYED NUMBER LIES, AGAIN (5-7)

Three defects in the panel whose own header comment promises "each badge's condition mirrors
ComputeOdds EXACTLY so the explanation always matches the math".

* **DMG row.** It printed the RAW weapon band. Against a guarded HVT — the natural state of the
  target an entire mission type is about — it read `DMG 3-5` for a shot that deals 1-2, with its
  own `GRAZE 1` row directly beneath it, computed from the same defender, disagreeing by 3x.
  `ShotOdds` gains `DmgMinEff/DmgMaxEff`; `DmgMin/DmgMax` stay RAW on purpose, because
  `Combat.ExpectedDamage` and the threat card's tie-break both want the raw band and apply
  reduction themselves.
* **LOCK-ON badge.** UNDERTOW W5 de-supersetted the perk (`CoverLevel==0` -> `flanked`) and
  updated only the math. The badge was a SECOND, INDEPENDENT COPY of the rule, and it went four
  waves stale — promising +15 aim on the modal targeting situation for a shot whose hit% moved by
  0. `Combat.LockOnAim(a, flanked)` is now the single source of truth (the `KillRefundsAction`
  pattern already in that file): ComputeOdds ADDS it, the badge SHOWS it, there is one condition
  left to drift.
* **`ComputeOdds` was not side-effect free.** Its GrazeFloor read called `HardenedReduce`, which
  arms `HvtGuardReducePending`, which `Game.Update` drains into a floating "GUARDED" pop — so
  merely HOVERING the guarded HVT popped it once per frame (QA's in-game count, not re-measured
  here: 181 pops over 181 frames of aiming with zero shots fired) against a comment promising "a single float when a hit was actually softened (not
  spammy)". `HardenedReduce` takes `telegraph = true` by default; the five READ sites pass false.

**Gap closed:** every existing test reads the MATH and none reads the DISPLAYED QUANTITY.
COMBATTEST pins the graze floor and THREATTEST ground-truths `ExpectedDamage` — both correct —
and nothing had ever read `ShotOdds.DmgMin/DmgMax` as the number the tooltip prints, because no
test executes HUD drawing code. PAIRTEST is structurally blind to the third defect: it compares
two runs of the SAME code, so a deterministic defect matches itself. **`SIGHTLINE_TRUTHTEST`** —
"what the UI says is what the dice do" — rolls 40,000 real shots per defender and asserts the
displayed band brackets AND tightly matches observed non-crit damage on a plain foe and a guarded
HVT, that GRAZE agrees with it, that the raw band was left raw, that armor moves the shown band,
that ComputeOdds/ExpectedDamage leave the telegraph unarmed while Resolve still arms it, and that
the badge predicate and the hit% delta are the same number.

### VERBS AND FLOW (8-10)

**GRAPPLE could never pull an adjacent foe — it self-rammed the grappler, every time.** The pull
vector is `sign(u - target)`, so a Chebyshev-1 target's destination tile IS the grappler's own;
`ShoveAnim` takes its blocked branch and `rammed` resolves to the GRAPPLER, which eats
`ShoveRammedDamage` from its own verb. Reproduced here on seed 3406 exactly as filed (`GRAPPLE
by=VEGA@(13,5) hp=1 target=STALKER@(14,6) cheby=1` -> `ENV VEGA(Player) hp=1->0 SLAM`, i.e. the
grappler killed itself); the wider rate — 3 of 3 Chebyshev-1 grapples across 11 campaigns — is
QA's verifier's count, not re-measured here. It is also **100% of a JUGGERNAUT's grapples** (`GrappleReachFor` pins that
fork at reach 1), so the fork's signature verb could never once do what `Unit.cs:414` advertises.

> **I did not take the brief's first fix option, and this is the disagreement worth recording.**
> Rejecting the adjacent target (mirroring `DragTargetOk`) would leave JUGGERNAUT with **no legal
> grapple at all** and force a redesign of the fork inside a bug-fix wave. Instead `ShoveAnim`
> never rams its own INITIATOR, which makes the adjacent case a clean SLAM — the foe takes the
> collision damage and loses overwatch/hunker, the grappler takes nothing. The verb stays legal and
> useful at every reach, the fork keeps a working ability, and the fix is one predicate rather than
> a redesign. The brief's own EXPECTED sanctions this ("or the adjacent case is a deliberate
> designed slam that does not damage the grappler"); it is simply the second option, and it is the
> better one.

**A soldier downed mid-queue still fired its own queued shot.** `PurgeAnimsFor` dropped shots AT a
felled unit and had no clause for shots BY it, so a body at Hp 0 resolved a shot: full damage, a
credited kill, `Stats.RecordShot` under the downed soldier's class, the takedown stinger. Three
guards: the purge gains `s2.A == d` (same ActiveAnim exemption); `ShotAnim.Update` self-cancels for
a dead/downed attacker; `TryFlankKillRefund` now checks `Downed` as well as `!Alive`. Root cause
too — the autopilot queued a SECOND action behind a GRAPPLE's not-yet-started ShoveAnim, decided
from a board that anim was about to change; `AutoStep` and `SmartStep` now return after
`IssueGrapple`. MARK deliberately still falls through: it resolves immediately and queues nothing.

**"Never a RESULT: TIMEOUT" was false for TWO independent reasons, and only one is in the brief.**

* *(a) THE BUDGET.* `AutoStallCheck`'s cap is per-MISSION (`_turnCount`) and re-armed by
  `SetupMission` — including the mid-mission checkpoint redeploy — while the harness budget is a
  whole-CAMPAIGN frame count. 20,000 frames bought a 6-mission campaign ~30-40 turns against a
  50-turn PER-MISSION cap. Fixed with a run-scoped `RunTurns` that `SetupMission` never resets.
* *(b) A HARD DEADLOCK, found here while calibrating (a).* Seed 3001 does not run out of budget: it
  **hangs**. Traced to 38,000 consecutive frames in mission 5 DEFEND with every hostile dead, an
  empty anim queue and `_turnCount` frozen at 2 — because `AutoStep`'s DEFEND branch does
  `if (ActionsLeft > 0 && Ammo > 0) { DoOverwatch(); return; }` and **DoOverwatch REFUSES for a
  DISORIENTED soldier**, spending nothing. Head-of-line blocking makes it total (`AutoStep` always
  picks `Players.FirstOrDefault(CanAct)`). `AutoStallCheck` can never see it: that guard runs in
  `StartPlayerTurn`, i.e. at a boundary the game can no longer reach. `SmartStep` already carried
  the guard at both of its overwatch sites; the smoke-test policy did not. Fixed at source, and
  backed by a general WITHIN-TURN idle guard so the NEXT one costs a forced turn instead of a
  phantom pre-merge failure.

| seed | before | after |
|---|---|---|
| 2001 | `TIMEOUT mission=6 frame=20000` | `LOSE mission=6 frame=20661 turns=44` |
| 3001 | `TIMEOUT mission=5 frame=20000` | `WIN mission=6 frame=12813 turns=36` |

**Caps calibrated on a MEASURED 20-seed census of the FINAL tree** (RESULT lines now carry
`turns=`): all 20 finish, longest campaign **75 run-turns / 18,992 frames**, next longest 38.
`AutoMaxRunTurns = 150` — 2x the longest measured campaign, because firing this cap on a
LEGITIMATE run would score it a LOSS and put back the same downward ladder bias the old frame
budget had. `AutoFrameCap = 100000` against `AutoFramesPerTurn = 600` (measured 253 frames/turn at
75 turns — a long campaign is long because it is ATTRITED, and a small squad takes cheap turns;
short full-squad runs cost more per turn but come nowhere near the cap), so 150 x 600 = 90,000
<= 100,000 and the turn cap always bites first. The frame cap now lives in `Game` beside the turn
cap it must dominate, and STALLTEST pins the inequality. BalanceBatch and PAIRTEST read the same constant: **at 20,000 the batch
RIGHT-CENSORED the longest campaigns as losses** (the archived x2 chunks log `frame-cap hits: 1`),
a small unattributed downward bias in the ladder of record that is now gone.

**Gaps closed:** GRAPPLE had ZERO coverage — `grep -i grapple src/Game.Harness.cs` returned nothing
— while both siblings were pinned (SHOVETEST's vector points away so its blocked case can never ram
the shover; FIELDTEST asserts DRAG's Chebyshev-1 exclusion, the very guard GRAPPLE lacked). Every
purge leg in the suite (DKTEST (2), DOWNTEST (a), OWTEST) builds a queue of shots AT the unit about
to fall, because in a hand-built scenario the SHOOTER is never harmed. And nothing asserted the
backstop's contract while qa-sweep only PRINTED the RESULT line. New: **`SIGHTLINE_GRAPPLETEST`**
(6 legs, the first the verb has ever had) and **`SIGHTLINE_STALLTEST`** (5 legs), plus a
shooter-side purge leg in DKTEST and a downed-shooter leg in DOWNTEST. **qa-sweep.sh now EXITS
NON-ZERO on a TIMEOUT or a missing RESULT line** and derives its own footer count.

### MODES AND THE RUN ECONOMY (11-13, 15)

**SKIRMISH and DAILY heat was numerically inert** — the dial added zero bodies, zero stats, zero
damage in two of the four shipped modes, because both enter through `SetupMission(1)` and the
mission-1 heat grace swallowed the whole ramp. Measured: skirmish eliminate, seed 4242, arena 5
reads `4 SQUAD 4 HOSTILES` at heat 0 and the same at heat 8 — the red chip was the only difference
on screen — against a campaign control of 6 vs 10 on the same seed and arena. ROADMAP:1066
recommends exactly this fix and calls it an owner decision. **THE CALL, MADE:** the grace is gated
on `Mode != GameMode.Skirmish` (which covers DAILY). A skirmish player explicitly DIALLED the rung;
there is no green squad to protect and no campaign ahead to front-load anxiety into, only the fight
they asked for. CAMPAIGN and ENDLESS keep the grace byte-for-byte.

**A field event broke the deploy cap in BOTH directions** — `DebriefSurvivors`' AutoDeploy has
already run when `ResolveEvent` fires, so a free recruit fielded cap+1 (`DEPLOY 5/4` over five
deployed soldiers) and a release stranded a healthy benched soldier at cap−1 (`DEPLOY 3/4` with a
6/6-HP body sitting out while the header printed its "field up to it" hint). One call to
`_run.AutoDeploy()` fixes both; it is a no-op for every event that does not touch the roster.

**The SKIRMISH legend named keys it did not bind.** Bound rather than reworded: Kp+/Kp− (matching
the INTRO's stepper) plus Equal/Minus, which were bound nowhere in the game. Up/Down and W/S keep
working — and no `Hud.cs` edit was needed.

**Gaps closed:** HEATLADDERTEST and OPENERTEST pin the ramp against CAMPAIGN missions only, and
MODETEST's skirmish legs assert phase routing; nothing asserted a skirmish's force reads
`_run.HeatLevel`, and the flywheel covers campaign + endless only. BENCHTEST asserted the cap only
immediately after `DebriefSurvivors()` — the one moment AutoDeploy has just run — and EVENTTEST
exercises `EventCatalog.Apply` against a bare test Run with no bench state. New legs: MODETEST (7)
proves the skirmish force answers the dial **and** that the campaign's mission-1 force is identical
across rungs, so the grace is proven still intact where it belongs; BENCHTEST (4a/4b) drives the
REAL `Game.ResolveEvent` (new `DebugResolveEventOutcome` hook, so the CALLER is under test) and
asserts `Deployed.Count == min(roster, NextDeployCap)` after a recruit and after a release.

### THE WAR ROOM DROPPED AN UNLOCK (14)

STANDING RESERVE was invisible AND unbuyable on a fresh profile. Six entries want 508px in a 446px
column, so the overflow `break` fired on the fifth compact card — no card, no BUY chip, no scroll
bar, no "+N more"; the panel border closed flush so nothing signalled a sixth entry. And because
the break preceded `WarRoomBuyBtns.Add`, no hit-rect was published: **unclickable, not merely
off-screen**, and there is no keyboard path to an unlock. Third-cheapest unlock in the game, in the
one state every new player is in.

`Hud.WarUnlockPlan` is now a pure function returning the panel height and the row plan — full 62px
cards with two description rows while they fit, then one row, then a name+BUY ledger row — read by
both DrawWarRoom's panel-height call and DrawWarUnlocks' loop. Rows shrink; the catalogue never
truncates. A shrunk row ellipsizes through `Clip` rather than cutting a word in half. The settled
column geometry is now named constants that DrawWarRoom itself derives from, so the test asserts
against THE numbers the screen uses.

**Gap closed:** METATEST covers the unlock MODEL exhaustively and never asked whether the screen
can DRAW them, and the only WAR ROOM screenshot hook hard-codes a 2-owned demo profile — which is
exactly the configuration that FITS. *The one state that overflows is the one state the harness
cannot photograph.* New METATEST leg walks EVERY owned/unowned split and asserts the plan paints
every unowned entry, so the next appended `MetaUnlock` fails loudly.

**Screenshots, read and judged.** Fresh profile: all six render with BUY chips, even pitch, the
border closing cleanly under STANDING RESERVE; three of five compact descriptions fit whole, two
ellipsize — an honest trade against an entry that used to not exist. The shipped 2-owned demo
profile is unchanged (full two-row cards at the 70px pitch), because the plan is a no-op whenever
the catalogue fits.

### PROOF EVERY NEW TEST FAILS PRE-FIX

Each defect was reintroduced ALONE, rebuilt, run, and reverted.

| defect reintroduced | result |
|---|---|
| all meta guards off | `SAVETEST: FAIL (metaStructureException:NullReferenceException)` |
| schema stash off | `SAVETEST: FAIL (metaFutureSchemaNotPreserved)` |
| veteran string guard off | `SAVETEST: FAIL (metaNullClsNotDropped)` |
| eff band -> raw band | `TRUTHTEST: FAIL (truthDmgRowOverstatesGuarded, truthDmgRowLooseGuarded, truthGrazeDisagreesGuarded, truthGuardNotShownInBand, truthArmorNotShownInBand)` |
| telegraph on reads | `TRUTHTEST: FAIL (truthComputeOddsArmsTelegraph, truthExpectedDamageArmsTelegraph)` |
| LOCK-ON superset | `TRUTHTEST: FAIL (truthLockOnBadgeLiesOpen)` + `COMBATTEST: FAIL (lockOnAimExposedZero)` |
| shove rams the initiator | `GRAPPLETEST: FAIL (SELF-RAM grapplerHp 8->7, SELF-RAM diagonalGrapplerHp=0, SELF-RAM diagonalGrapplerFelled, SELF-RAM juggernautHp 8->7)` |
| purge without the `A` clause | `DKTEST: FAIL (shotsByCorpse=2)` |
| + no ShotAnim self-cancel | `DOWNTEST: FAIL (i:downedShotKept, i:downedShotResolved 6->3)` |
| per-mission cap only | `STALLTEST: FAIL (armDidNotFire phase=PlayerTurn runTurns=91)` |
| SetupMission resets RunTurns | `STALLTEST: FAIL (runTurnsResetBySetup=1 was=7)` |
| DEFEND overwatch unguarded | `STALLTEST: FAIL (deadlockNotDrained)` |
| grace ungated by mode | `MODETEST: FAIL (skirmishHeatInert h0=4 h8=4)` |
| no post-event re-derive | `BENCHTEST: FAIL (eventRecruitDeployed=5 want=4, eventRecruitOverCap, eventReleaseDeployed=3 want=4, eventReleaseStrandedHealthyBench)` |
| fixed pitch + old break | `METATEST: FAIL (warUnlockDropped owned=0 unowned=6 drew=4/5)` |

### GAMEPLAY-AFFECTING — THE LADDER MUST BE RE-MEASURED

State it plainly: **this wave moves the numbers and does not price them.** That is a later wave's
job, and no balance figure is claimed here.

**Changes RNG DRAW ORDER** (so any archived comparison against this tree is void):
1. the grappler's `EnvDamage` — and its FX draws — no longer happens on an adjacent grapple;
2. a downed unit's queued shot no longer rolls;
3. the autopilot takes an extra step after a GRAPPLE (an extra `Util.Roll(45)` on the next step).

**Changes composition without changing draw order:** the skirmish/daily heat gate (arithmetic on
already-drawn values) and the post-event `AutoDeploy` (a deterministic sort).

**PAIRTEST stays green** and must: it asserts that two identical legs match EACH OTHER, not that
they match an archived number.

**Not gameplay-affecting at all:** the meta.json guards, the tooltip fixes (no dealt-damage path
changed — the only behaviour change outside the HUD is that a pure read no longer pops a float),
the WAR ROOM layout, and the skirmish key bindings.

### WHAT I DID NOT FIX, AND WHY

* **The five pure UI-overflow defects** were explicitly held for after the first-hour/UI wave lands.
  Untouched here by instruction.
* **`Mission.OpenerTrim` still applies to SKIRMISH and DAILY** (one body off an n<=1 force). It is
  UNIFORM across every heat rung, so it does not flatten the dial and is not the defect that was
  filed — but a skirmish's absolute difficulty is one body lighter than a campaign mission 1 with
  the same parameters. Flagged, not changed: changing it is a balance lever, not a repair.
* **The CODEX footer drift** the brief mentions in passing (its legend says "Up/Down select · Wheel
  scroll" while keyboard scrolling is bound to Left/Right and A/D) is real and out of scope here.
* **The tooltip's other unbadged modifiers** — Siegebreaker, Bipod, the defender's CoolHeaded,
  Routed, Vantage/Breaker/Guardian crit, the Marksmen/Fervor/Executioners boons, PressureAim and
  the faction aim rules. These are OMISSIONS, not false statements, so they are a different (and
  larger) job than the three lies this wave was sent to fix — but the panel's header comment claims
  it surfaces EVERY modifier, so either the badges or the comment is still over-claiming.
* **No balance measurement was run.** With three draw-order changes in the tree a batch would only
  tell us that the numbers moved, which we already know.

### VERIFICATION

* **Release build:** 0 warn / 0 err.
* **`bash scripts/qa-sweep.sh --full`:** **52/52 PASS**, PAIRTEST **PASS**, COVERAGE GAP block
  empty, `SWEEP-EXIT=0`. The footer count is now DERIVED from `src/` rather than hand-maintained
  (it had been wrong twice before).
* **Autoplay ×3 inside the sweep** (final tree, after the cap recalibration): `LOSE mission=6
  frame=12517 turns=32` / `LOSE mission=3 frame=7900 turns=20` / `LOSE mission=5 frame=11652
  turns=31`. No TIMEOUT, no blank — and the sweep would now have exited 1 if there had been.
* **PAIRTEST is green and must be** — it asserts that two identical legs match EACH OTHER, not
  that they match an archived number, so the draw-order changes above do not and cannot break it.
* **Seeded census, the same 20 campaigns, before and after (base `d350416` vs the final tree).**
  Before: **2 TIMEOUTs** (seeds 2001 and 3001, both at the 20,000-frame cap), longest FINISHED run
  14,507 frames. After: **zero TIMEOUTs — all 20 finish**, 11 WIN / 9 LOSE, longest 18,992 frames /
  **75 run-turns** (seed 2001), next longest 38. The two "TIMEOUTs" turned out to have different
  causes: 2001 was a campaign that simply needed more budget than it had, 3001 was the deadlock.
* **The caps are calibrated on THAT census, not on a guess.** `AutoMaxRunTurns = 150` is 2x the
  longest campaign measured — the headroom is the point, because firing this cap on a LEGITIMATE
  run would score it a LOSS and put back exactly the downward bias the old frame cap had.
  `AutoFrameCap = 120000` with `AutoFramesPerTurn = 700` — the measured WORST frames-per-turn (681),
  not a regime average, after the review pointed out that the first cut's 600 came from the
  long-campaign regime and left the claim EMPIRICAL: a campaign sustaining its worst observed rate
  would have hit the frame cap ~3 turns before the turn cap. 150 x 700 = 105,000 <= 120,000 with no
  regime assumption left in it. STALLTEST pins the inequality — true by construction, which is the
  point: the three constants live in two files and only mean anything together.
* **Residual, stated honestly:** the within-turn idle guard covers the PLAYER turn only
  (`UpdatePlayer`). A deadlock inside `UpdateEnemy` would still be bounded only by the frame cap.
  Nothing in 40 seeded campaigns showed one, and adding an untested guard to the enemy stager
  looked riskier than the hole; it is a known gap, not an oversight. **And the raised frame cap makes
  it 6x slower to surface** (120,000 frames instead of 20,000) — the censoring went down and the
  detection cost went up, which the first write-up did not say.
* **Screenshots** read and judged: the WAR ROOM unlocks column on a FRESH profile (all six entries
  present and buyable) and on the shipped 2-owned demo profile (unchanged). **This judgement missed
  a real defect** — five of the six descriptions were rendering at 10px, under the project's 12px
  floor. A reviewer caught it by measuring through the public geometry rather than by looking. See
  R3 below; "read and judged" is not a measurement, and this is what that costs.

### W9 REVIEW FIXES — what four reviewers found that this wave had got wrong

The review upheld all four of the wave's deviations from the brief and independently reintroduced
11 of the 15 defects, each producing the recorded failure string. It also found six things wrong
with the wave itself. Three of them are the same failure mode the wave was written to attack — **a
claim in a comment that the tree does not honour** — which is worth recording plainly rather than
quietly patching.

**R1 (BLOCKING) — TRUTHTEST could not fail on EITHER display defect it was written for.**
The adjudicator reverted only `Hud.cs:1896` back to `(a.HasPerk(Perk.LockOn) && o.CoverLevel == 0)`
and `Hud.cs:2008` back to `$"{o.DmgMin}-{o.DmgMax}"`, changed nothing else, and got
**TRUTHTEST: PASS, COMBATTEST: PASS, THREATTEST PASS**. He is right, and the diagnosis is exact:
the test asserted `ShotOdds.DmgMinEff/DmgMaxEff` and `Combat.LockOnAim(...)` — the values the HUD is
*supposed* to read — and **nothing bound the HUD to them**. It re-derived the right answer instead
of observing the panel. That is a pin, not a test, and it is precisely the seam this wave exists to
close: the flagship "what the UI says is what the dice do" test did not read the UI.

The fix observes the draw call. `Cfg.CaptureText` (null in every normal run) records every string
the game paints, with the size it was painted at. `Game.TooltipTruthFails` stages a controlled
board, drives the REAL hover/aim path (`Update` → `UpdateHoverAndAim` → `ComputeOdds`), renders the
REAL tooltip through `Hud.DebugDrawTooltip`, and asserts on the captured strings — so whatever the
panel says is what the test reads, *however it was computed*. SIGHTLINE_TRUTHTEST is now two halves
(`Combat.TruthFails` for the math and purity, `Game.TooltipTruthFails` for the UI) composed into one
line. Both reverts now fail, with the defect's own signature:

| the adjudicator's revert | result |
|---|---|
| `Hud.cs` LOCK-ON badge → `o.CoverLevel == 0` | `TRUTHTEST: FAIL (uiLockOnBadgeLiesOpen painted=True hitDelta=0, uiLockOnValueOpen=+15 aim delta=0)` |
| `Hud.cs` DMG row → `{o.DmgMin}-{o.DmgMax}` | `TRUTHTEST: FAIL (uiDmgRowLiesGuarded shows=3-5 deals=1-2, uiGrazeDisagreesGuarded dmg=3-5 graze=1 on near miss, uiDmgRowIgnoresDefender both=3-5)` |
| restored | `TRUTHTEST: PASS` |

`shows=3-5 deals=1-2` is the original dossier entry, now reproduced by the test itself.

**R2 (BLOCKING) — the post-event `_run.AutoDeploy()` discarded the player's manual bench choice on
EVERY event, and the comment I shipped with it asserted the opposite.** A reviewer proved it on an
Intel-only outcome: `MANUAL VEGA[B],KRESS[D],NOX[D],BISHOP[D],LYNX[D]` came back
`VEGA[D],KRESS[D],NOX[B],BISHOP[D],LYNX[D]`. `AutoDeploy` re-derives `Benched` for the WHOLE roster
from a fixed rule — right at a debrief, wrong afterwards — and `ResolveEvent` is the checkpoint
site, so the clobbered deployment is what gets persisted. The dossier's own sketch said to gate it
and I generalised past it. Now gated on a real roster change, and reconciling BY EXCEPTION:
`Run.ReconcileDeployment` benches only what the event added, fills only slots the event freed, trims
only over the cap, and leaves every deliberate choice alone. `AutoDeploy` and it share one
comparator so they cannot diverge.

**And the lesson the reviewer drew is the important part.** BENCHTEST could not catch this because
it asserts a COUNT invariant — `Deployed.Count == min(Squad.Count, NextDeployCap)` — which **a
clobbering implementation satisfies exactly as well as a preserving one**. The new leg asserts
IDENTITY, by name, after an outcome with no roster change at all. It fails on the shipped
implementation: `BENCHTEST: FAIL (4a2:benchClobbered manual=VEGA[B],KRESS[D],NOX[B],... after=VEGA[D],KRESS[B],NOX[B],...)`.

**R3 (BLOCKING) — the WAR ROOM fix breached the 12px small-text floor on a FRESH PROFILE, and its
comment claimed it did not.** At 0 owned / 6 unowned the plan lands `cardH = 49 → descRows = 1`, and
the old code then asked `FitWrap` to squeeze a whole sentence into one row. **FitWrap's floor is
`CardBodyMinSize = 10`, not 12** — so five of six unlock descriptions rendered at 10px, on the exact
screen the repair exists for, in the one profile state every new player is in. My DEVLOG recorded
that screenshot as "read and judged" and did not notice. A compressed row now paints at
`CardBodySize` (12) and ellipsizes through `Clip` instead of shrinking; when not even one 12px row
fits, the body is dropped rather than painted sub-floor. `descRows` is derived from the real row
pitch (`27 + n * TextRow(13)`) instead of hard-coded pixel bands. METATEST asserts
`bodySize >= 12` at every owned/unowned split, and fails on the old sizing:
`METATEST: FAIL (warUnlockSubFloorBody owned=0 size=10 rows=1)`. Re-screenshotted: all six entries
present and buyable, bodies now uniform with the hero card's, four of five ellipsized.

**R4 (BLOCKING) — three shipped numbers stated the opposite of the tree**, in the wave whose own
commit is titled "attribute every number to whoever measured it". `Program.cs` said
"AutoMaxRunTurns (90 run-turns)" twice while the constant is 150; commit `2664349` recalibrated it
and updated DEVLOG and CLAUDE.md but missed both source comments. Fixed.

**R5 — `qa-sweep.sh` exited non-zero only on `_autofail`.** A self-test line reading FAIL, or a
non-empty COVERAGE GAP block, still exited 0 — so `SWEEP-EXIT=0` read as a whole-sweep verdict while
it was only an autoplay verdict, and the lead relies on that code at every merge. This is the same
defect class as the wave's own D10 (a gate that prints instead of failing), shipped by the wave that
fixed D10. All 38 self-test captures now route through one `verdict` helper that records a FAIL —
and a BLANK capture, which is how a crashed self-test used to read as a quiet empty line — and the
COVERAGE GAP block sets the flag too. Verified: `verdict` sets the flag on FAIL and on blank
(`A: PASS → 0, B: FAIL → 1, C: <no result line> → 1`), and the exit block's truth table is
`(0,0)→0, (1,0)→1, (0,1)→1, (1,1)→1`. A self-test FAIL alone now blocks the merge; it did not before.

**R6 — STALLTEST's budget leg was resting on a regime assumption.** `AutoFramesPerTurn = 600` came
from the long-campaign regime (253 frames/turn at 75 turns), but the measured spread runs to 681, so
a campaign sustaining its worst observed rate would hit the frame cap at ~147 run-turns — three
turns before the turn cap. "TIMEOUT is unreachable" would have been EMPIRICAL, and that matters more
now the sweep hard-fails on a TIMEOUT, because a rare false positive becomes a merge block. Set to
the measured WORST case instead: `AutoFramesPerTurn = 700`, `AutoFrameCap = 120000`, so
150 × 700 = 105,000 ≤ 120,000 with no regime assumption left in the argument. The leg's own comment
now says plainly what it is: **true by construction, and that is the point** — the three constants
live in two files and only mean anything together, so it fails the moment one is edited alone. It is
a coupling check, not a proof.

**Two comment corrections, no behaviour change.** (a) `PurgeAnimsFor`'s new shooter clause claimed
its `ActiveAnim` exemption means "the blow in flight still finishes" — true for the target clause,
FALSE for this one, because `ShotAnim.Update` self-cancels for a dead/downed attacker a frame later
anyway. The exemption is inert on that clause and is now documented as such; a body does not shoot,
in flight or not, which is the intended rule. (b) The frame-cap raise makes the wave's own declared
residual — a deadlock inside `UpdateEnemy`, which the within-turn idle guard does not cover — **6×
slower to surface** (120,000 frames instead of 20,000). The censoring went down and the detection
cost went up; both are real and neither was noted before.

### VERIFICATION AFTER THE REVIEW FIXES

* **Release build:** 0 warn / 0 err (full `--no-incremental` rebuild).
* **`bash scripts/qa-sweep.sh --full`:** **52/52 PASS**, PAIRTEST **PASS**, COVERAGE GAP block
  empty, `SWEEP-EXIT=0` — and that exit code now covers the WHOLE sweep, not just autoplay.
* **Autoplay ×3:** `WIN mission=6 frame=7384 turns=20` / `LOSE mission=4 frame=8535 turns=26` /
  `LOSE mission=5 frame=8938 turns=62`. No TIMEOUT, no blank. The 62-turn run is the longest seen
  on this tree and sits well inside the 150-turn cap.
* **The two reverts the adjudicator used to falsify the first TRUTHTEST now FAIL** (table in R1
  above), and restoring them returns PASS.
* **Every new assertion was proven against the implementation it rejects:** the bench-identity leg
  fails on the unconditional `AutoDeploy` (`4a2:benchClobbered`), the floor leg fails on the old
  sizing (`warUnlockSubFloorBody owned=0 size=10 rows=1`), and the sweep's `verdict` helper plus its
  exit block were driven through their full truth tables.
* **Not re-measured, and not claimed:** the balance ladder. W9's declared draw-order changes stand;
  the review fixes add none.

## PROGRAM RESONANCE — Wave W4 "THE BOARD BECOMES A PLACE" (dev; branch `wave/board-as-place`)

**Base commit `d350416`.** Rendering only: every line changed is inside `src/Renderer.cs`'s draw
path or the `Pal` block of `src/Util.cs`, plus the new self-test. No `Grid`, no `Combat`, no
`Util.TileRect`, no tile geometry. The inertness proof is at the bottom and it is a real
measurement, not an assertion.

### The thesis, and the number that carried it

The board is ~90% of the frame and it read as a greybox level: a rigid grid of near-identical
pale lozenges carrying near-identical coloured discs. The single most damning number the
auditor produced is a **value-hierarchy inversion** — a DORMANT POD measured peak luminance
**219.9** while the SELECTED SOLDIER measured **166.8**. The thing the player is explicitly told
to ignore was the brightest object on screen. `docs/DESIGN.md` §3.H's own role table says the
opposite in two places ("make the focal element the brightest"; "Neutral/inactive:
desaturated/dimmed toward the background"), and its acceptance check — *squint; can you instantly
find the selected unit?* — was failing against the game's own dormant pods.

**The dormant pod's figure reproduces exactly on this tree; the soldier's did not reproduce
STABLY.** `SIGHTLINE_BOARDTEST` run against the pre-wave path (`SIGHTLINE_TOKENSTYLE=0`) prints
DORMANT pod PEAK **219.9** — the auditor's number, to one decimal, from an independent probe. The
SELECTED-soldier figure quoted here as 166.8 was **phase-dependent and this write-up was wrong to
present it as deterministic output**: ten runs of one Release binary gave 166.8 three times and
188.1 seven times, because the probe sampled an unpinned animation phase (45 wall-clock reads in
`Renderer.cs` drive animation — CLAUDE.md's "screenshots are not byte-identical" warning is exactly
this). The clock is now pinned inside the test (`Renderer.TimePin`, set to t = 3π/10), and the
legacy path prints **188.1**, ten runs out of ten — see §W4 BOARD REVIEW FIXES. Either way the
inversion the wave exists to fix is unaffected: the dormant pod at 219.9 out-shines the selected
soldier at both of its modes.

### 1. Cover is MERGED VOLUMES, not one box per tile (audit `visual-1`)

`DrawCover` looped per tile and inset **every** tile on **all four** sides by `5 + jIn` with no
neighbour test anywhere in the function, so a four-tile wall drew as four separate boxes with a
~10px gutter of visible floor between them. V3's ±3px jitter, hashed corner radius and 35% corner
chip are all sub-threshold at a 64px tile and never changed the silhouette.

A union-find pass now groups 4-connected tiles of the **same `TileType` at the same elevation
tier** (the drawn rect is lifted by `HeightAt * ElevLift`, so merging across a step would weld two
rects that do not line up). Every per-tile decoration is then gated on which of its four sides is
a group **seam**:

* inset and corner rounding dropped on a shared edge, so the rects **abut**;
* the **top face extends down over the wall band** when the south neighbour is in the group, so a
  north-south run is one continuous slab with **one** wall face at its southern end instead of a
  light/dark ladder of tops and walls;
* cast shadow, contact AO, front-face gradient, top-edge highlight and structural rim draw only on
  the volume's **outer** sides — inside a run they would paint seam lines across the middle of the
  object, which is the same defect moved to a different pixel;
* the corner **chip** only fires on a corner whose two sides are both outer edges.

Footprint jitter, lift and corner radius moved from per **tile** to per **group** (hashed off the
group's root tile) — a per-tile jitter is precisely what stops two rects abutting.

**Measured.** Parsing all 36 authored layouts in `src/Maps.cs`: **684 cover tiles forming 383
4-connected same-type groups → 19.0 drawn boxes/map becomes 10.6 volumes/map, a 44% cut in
silhouette count.** (This reproduces the auditor's figure exactly.) On the rendered frame, same
seed 4242, a colour-key mask (`82 < luma < 150`, saturation < 0.42) plus a connected-components
pass over the board region counts **19 cover silhouettes before → 12 after**; running the new
build with `SIGHTLINE_COVERMERGE=0` reproduces **19**, so the difference is the merge and not the
seed.

**Cover's value budget survived it.** Board-region luma over the same seed: mean 76.7 → 77.6,
p90 105.0 → 106.8, **p99 158.8 → 144.0**. The merge shows slightly more top face and slightly less
wall (mean +3.1 attributable to the merge alone, measured against `COVERMERGE=0`), and the
hierarchy actually *tightened* at the top of the distribution because the dormant pods came down.

### 2. A per-biome FORM vocabulary (audit `visual-1b`)

One form per merged **volume**, hashed off the group root, drawn from a per-biome table of three:

| form | treatment | reads as |
|---|---|---|
| CRATE | per-tile cross-brace + a strap rectangle | a **stack** of separate boxes |
| WALL | two masonry courses in running bond across the whole run + a course on the wall face | **one** long wall |
| BOULDER | dome catch up-left + two hashed pits, chip suppressed | weathered rock |
| WRECK | diagonal shear across the top face, bright torn lip, vertical rip on the wall face, a strut past the outer edge | torn metal |

Three forms × two cover tiers = **4–6 distinguishable object types per map** instead of one widget
stamped forty-five times. Every form is a surface + edge treatment **inside the volume's own
rect** — the drawn footprint is untouched, so none of it can move a hitbox. The CRATE deliberately
keeps per-tile articulation (that is what makes a 2×2 read as four strapped crates rather than one
panel); WALL and WRECK deliberately span the run.

### 3. The value hierarchy, taken as ONE decision (audit `visual-3` + `visual-4`)

The brief was explicit that these are one decision and not two waves fighting over the same
channel, and the measurement says why. Before, on a `SIGHTLINE_ALERT=1` frame at seed 4242
(14px patches, Rec.709 luma):

| | mean | PEAK |
|---|---|---|
| SELECTED soldier | 124.3 | **188.1** |
| ACTIVE hostile | 96.4 | **211.1** |
| SUSPICIOUS pod | 133.8 | **205.1** |
| DORMANT pod | 137.0 | **219.9** |

That is a **complete inversion**: dormant > active > suspicious > selected on peak. The audit only
measured pod-vs-soldier; with all four tiers on one frame the ladder is upside-down end to end.
Three separate authored decisions produced it — V3's near-white dormant body fill, a white
specular catch at 0.48 alpha that lands hottest over the *enemy's* warm ring, and a
`Mix(main, white, 0.26)` ring stroke whose result depends entirely on which hue you feed it.

**What shipped:**

* The ring is **demoted to a 2px state indicator** at 0.74 alpha. It keeps its full topology —
  closed-doubled friendly, broken-three-arc hostile, square TURRET, hex BRUISER, dashed SCOUT,
  diamond banner-bearer — because that is the colourblind-safe team + role channel; it stops being
  the body's boundary.
* The **white specular catch is retired**. It was the pixel that put a live hostile at 211.
* The **archetype form is drawn twice**: a 1.17× copy in the dark contour colour (the form's own
  outline, so it holds against any biome floor or overlay wash), then the figure on an explicit
  **value rung**.
* The rungs are stated as a **target luminance**, not as a mix fraction — `Renderer.ToLuma(c, n)`
  solves `lum(Mix(c, white, k)) = (1-k)·lum(c) + k·255` exactly. This is the load-bearing choice:
  it is what makes the ordering survive `Pal.SetColorblind`, where `Pal.Foe` changes hue **and**
  luma (248,113,113 lum 153 → 238,138,40 lum 152) and a fixed mix lands somewhere different in
  each palette.

  `selected soldier 202 > VIP 196 > other soldier 190 > boss 184 > live hostile 172 > suspicious 162 > dormant 150`, with each token's ring one step (−14) below its own figure.
* The dormant pod comes **off the value channel entirely**: body fill dropped to roughly floor+20,
  its under-glow dropped with it (an under-glow brighter than the token it seats is the same
  inversion one radius further out), and the "something is standing here" signal is carried by
  **outline weight and shape** — the crisp cold rotating dashed ring, the `?` glyph, and the
  token's undiminished size. Its `?` also **moved off the body** into the SUSPICIOUS tier's marker
  slot above the token: with the archetype form now filling the token, a glyph stamped across the
  middle of the figure was obscuring exactly what it was there to label, and both awareness
  markers are now directly comparable in the same place.

**After, same frame, same probe:**

| | mean | PEAK |
|---|---|---|
| SELECTED soldier | **120.5** | **201.7** |
| ACTIVE hostile | 92.2 | 171.0 |
| SUSPICIOUS pod | 102.1 | 161.7 |
| DORMANT pod | **81.1** | **149.3** |

Monotone on peak across all four tiers; monotone on the gated three (selected > active > dormant)
on mean as well. **In the colorblind palette the same probe gives 201.7 / 171.5 / 161.7 / 149.3** —
within 0.5 luma of the normal palette at every rung, which is the payoff of stating rungs as
numbers.

**The archetype vocabulary is now visible, measured.** Taking the brightest 12% of each token's
pixels and asking where they lie — inside the figure radius (r ≤ 13) or in the enclosing ring band
(15 < r ≤ 26) — over five archetypes on a `SIGHTLINE_CONTENT=1` frame:

| | bright px in FIGURE | in RING | figure share |
|---|---|---|---|
| before | 0 | 2077 | **0.00** |
| after | 1280 | 190 | **0.87** |

Zero per cent to eighty-seven per cent. The p95/median value ratio inside the token moved 2.05–2.46
→ 2.47 uniformly, but the ratio was never the interesting number: what changed is **which element
owns the bright tier**. Read at 25% downsample, LANCER / WARBRINGER / GRUNT / HOUND / SCOUT /
CUSTODIAN are individually identifiable after and are six red discs before.

### 4. The move overlay: one region, one contour, and off the objective gold (audit `visual-5`)

Three things, all in the same 100 lines:

* **Dash on demand.** `canDash` is true whenever `ActionsLeft >= 2`, i.e. at every turn start, so
  the default board always carried two nested regions. The dash region now draws only when asked
  for: hold **SHIFT** (the first key this game reads as a *modifier* — held to change what another
  element shows. The original claim here, that "there was not one `IsKeyDown` call in the codebase
  before this one", is **false**: `src/Game.Codex.cs` lines 80-81 have read held Right/D and Left/A
  to scroll the field manual since long before this wave), or hover a tile that is outside walk
  range. The dash
  **action** is untouched: clicking a far tile still dashes. This is the affordance, not the rule.
* **A closed contour, not per-tile edges.** The boundary is stitched by a marching-squares walk
  over the reachability mask into closed loops, each stroked as one polyline with welded joints
  and a **continuous dash phase**. Measured by the new test on a live frame: **58 boundary edges
  stroked as 5 closed contours = 11.6 edges per primitive**, against the pre-wave path's **1.0**.
  That ratio is a **code-structure metric, not a visual result** — it says the boundary is now one
  stitched primitive per loop instead of 58 independent ones, which is what makes a welded joint
  and a continuous dash phase possible at all. A reviewer compared the two frames and the strokes
  land on the same pixels; the visible payoff is the dash phase and the joints, not the count.
* **Off the reserved gold.** `Pal.MoveYellow` was `RGBA(251,191,36)` — byte-identical to
  `Pal.Accent`, the objective gold `docs/DESIGN.md` §3.H reserves for goals and which already
  carries 18 separate jobs in `Renderer.cs` alone. It is **renamed** `Pal.MoveDash` (renamed, not
  merely re-valued, so nothing can quietly reintroduce the gold) and moved to `RGBA(148,221,252)`,
  a paler value variant of the friendly cyan. Walk and dash are now separated on **value + stroke
  style**, not hue.

### The new self-test: `SIGHTLINE_BOARDTEST`

This is the **only self-test in the project that measures rendered pixels**, and it has to be: the
defect it guards is invisible to every state assertion in the suite — nine win-rate-driven programs
did not see it. It stages three tokens (selected soldier / live hostile / dormant pod, identical
class, facing and bob phase, so the patches differ only in the thing under test) plus a **stamped**
two-tile cover volume on a seed-pinned board, draws one real frame through the shipped `Game.Draw`
path, screenshots it and probes:

* **Gate A** — the value ladder, on mean **and** peak, with ≥ 8 luma of margin so the move
  overlay's alpha-15 whisper tint cannot flip a verdict.
* **Gate B** — the cover seam. The discriminator is deliberately **not** "is the seam
  floor-coloured": a cover top standing in the key light's far corner measures *darker* than a lit
  floor tile, so an absolute threshold reads the wrong answer at one end of the board. It is the
  **trough** — an unmerged pair puts a gutter of floor plus both blocks' hairline edges and contact
  shadows between the two top faces, a deep wide dip against the *same material* a few pixels
  either side. Merged measures 1 dark-trough pixel; `COVERMERGE=0` measures 10. The gate also
  re-draws with nothing selected first, because a move-overlay stroke sitting on the seam under
  test would mask an unmerged gutter.
* **Gate C/D** — `Pal.MoveDash` must not share `Pal.Accent`'s RGB bytes; the boundary must be
  stitched (≥ 4 edges per stroke primitive); the dash region must be off on a default frame.

**It fails on the pre-wave tree — proved by flipping each dial:**

| dial | what it reports | verdict |
|---|---|---|
| `SIGHTLINE_TOKENSTYLE=0` | selected PEAK 166.8, hostile 158.1, dormant **219.9**; mean 121.6 / 100.3 / 128.4 | FAIL (2 violations) |
| `SIGHTLINE_COVERMERGE=0` | seam dark-trough **10px** | FAIL (1 violation) |
| `SIGHTLINE_MOVESTYLE=0` | **1.0** edges/stroke, dash region shown | FAIL (1 violation) |
| shipped | 201.7 / 171.0 / 149.3, 1px trough, 11.6 edges/stroke, dash off | **PASS** |

**The TOKENSTYLE=0 row's `selected PEAK 166.8` is the unpinned figure and is superseded** — with
the clock pinned it is **188.1** and the row FAILs with 5 violations, not 2 (the colourblind repeat
of gate A, added by the review fixes, contributes three more). The current table is in
§W4 BOARD REVIEW FIXES.

Wired into `scripts/qa-sweep.sh` (50 self-tests exist; the sweep runs 48 with `--full`).

### A/B dials

`SIGHTLINE_COVERMERGE=0` (pre-wave per-tile boxes) · `SIGHTLINE_TOKENSTYLE=0` (pre-wave tokens) ·
`SIGHTLINE_MOVESTYLE=0` (pre-wave overlay, gold and all) · `SIGHTLINE_MOVEDASH=1` (force the dash
region on, for filming it). Capture line used throughout:

```
SIGHTLINE_SEED=4242 SIGHTLINE_SHOT=760 [dial=...] xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug
```

Frame **760**, not 90: the mission briefing card holds for 11 s and sits directly over the probe
region, so every capture before ~frame 700 in this project photographs the briefing rather than the
board.

### VERIFICATION

* Release build **0 warn / 0 err**.
* `bash scripts/qa-sweep.sh --full`: **48/48 PASS** including the new `BOARDTEST`, COVERAGE GAP
  block empty, **PAIRTEST PASS** (byte-identical CRN legs).
* Autoplay ×3: WIN m6 / LOSE m4 / LOSE m3 — no exception, no TIMEOUT.
* **Balance inertness, measured two ways.** (a) `SIGHTLINE_PAIRTEST=1` byte-identical. (b) A
  pinned-slot `SIGHTLINE_BALANCE=5 SIGHTLINE_BALANCE_BASE=140 SIGHTLINE_BALANCE_HEAT=0` chunk, run
  as the Release binary from a gitignored snapshot under `xvfb-run` with `XDG_CONFIG_HOME` and
  `SIGHTLINE_BALANCE_JSON` pinned per chunk, **`runs=10` asserted from the JSON's own field in both
  chunks** — on base `d350416` and on this wave's commit, the two summary documents are
  **field-for-field identical** (a full recursive diff, not a spot-check of headline numbers).
* Screenshots read and judged: plain board before/after, alert tiers before/after in **both**
  palettes, the content showcase before/after at 25% squint, forced MAGMA and ARID (form vocabulary
  varies by biome), `SIGHTLINE_ELEV=1` (cover on plateaus — the merge refuses to cross an elevation
  step, and does), `SIGHTLINE_COVER=1` (damaged cover), `SIGHTLINE_UISCALE=3` (the shipped text
  scales survive), and `SIGHTLINE_MOVEDASH=1`.

### PERFORMANCE — honestly, unresolved

Three 900-frame `SIGHTLINE_SHOT` runs of the Release binary under xvfb/llvmpipe, shipped path vs
all three dials off: **86.9 / 92.0 / 120.4 ms/frame** against **84.7 / 103.8 / 123.4**. The
medians favour the new path, but the spread within each configuration is ±20% (the container is
shared and was running a 24-chunk measurement), which is **larger than any effect**. The honest
statement is: **no measurable difference at n=3 on this container, and these are software-rasteriser
numbers that say nothing about the human's GPU.** Reasoned bound rather than measurement: per cover
tile the wave adds ≤ 8 primitives (the form) and removes 1–4 (shadows/rims skipped on interior
seams); per unit it adds one `DrawSilhouette` pass (~5–12 primitives). With 19–45 cover tiles and
~10 units that is on the order of **+200 primitives per frame** against a board already drawing
several thousand. The union-find rebuild is 198 tiles × two passes per frame and is not
measurable. **Unmeasured on real hardware.**

### WHAT I DID NOT FIX, AND WHAT IT COST

* **The board is better, but it is not "grounded terrain".** Read at 25%, the after frame is
  fewer, larger, differently-textured volumes with the focal hierarchy the right way up. It is not
  a photograph of a place. The merged volumes are still **rectangles on a square grid**, because
  the footprint is the tile grid and this wave was forbidden from touching tile geometry (rightly:
  that is a gameplay change). Making the board read as terrain rather than as well-dressed blocks
  needs the footprint itself to stop being axis-aligned, which is a different wave with a
  gameplay-inertness argument this one cannot make.
* **The S4-B cover-tier cue (△ / —) is still drawn per tile, not per volume.** A four-tile wall
  therefore carries four identical △ stamps, which is exactly the "one widget stamped five times"
  reading the wave exists to kill. It stays because cover tier is a **per-tile gameplay fact** and
  §3.H names this glyph as the tier's non-colour channel; one cue per volume would cost the far end
  of a long wall its tier read. Judgement call, recorded as one.
* **`SUSPICIOUS` sits above `ACTIVE` on the patch MEAN** (102.1 vs 92.2) even though it is below on
  peak. That is an artefact of area, not value: the suspicious tier's heavy pulsing double ring
  fills more of a 14px patch than a live hostile's three thin arcs. The gate is stated on the three
  tiers the audit named and I did not widen it to cover a metric it would fail for the wrong reason.
* **The CRATE form's cross-brace is prominent.** At 100% it can read as a strike-through rather
  than bracing. It stays at 0.42 alpha because it is the strongest per-tile articulation cue and it
  is what makes a 2×2 read as four strapped crates instead of one panel. If a reviewer disagrees,
  the alpha is one number.
* **Nothing in `Hud.cs` or `Display.cs`.** The post-FX split (audit `visual-2`, the bloom eating
  the menu type) and the intro/action-bar findings (`visual-6`, `visual-7`) are W5's, untouched.
* **Biome is still pure paint mechanically** — `grep -ci biome` still returns 0 in `Combat.cs`,
  `Ai.cs`, `Grid.cs` and `Unit.cs`. This wave made biome read as a *material* on the board; making
  it a rule is `content-1` and a later wave.
* **The dash-on-demand modifier is undiscoverable.** SHIFT reveals the sprint region and nothing
  tells the player that. The hover trigger covers the case that matters (reaching for a far tile),
  and the HUD still shows the move budget, so no information is lost — but the modifier itself is
  unlabelled and should get a line in the FIELD MANUAL. Not done.
* **The renderer now reads input.** `DrawMoveOverlay` calls `Raylib.IsKeyDown` directly, which is
  off-architecture for a draw path. It is presentation-only (the same function already reads
  `Raylib.GetTime`), and the wave's file scope was `Renderer.cs` + `Pal`, so routing it through
  `Game` was out of bounds. Deterministic under the harness (no input), so it costs nothing
  measurable — but it is a wart.

### W4 BOARD REVIEW FIXES (dev, same branch; base `d122ea5`)

Four independent reviewers plus an adjudicator went over the wave. **The substance held** and none
of it was touched: the value-hierarchy inversion and its fix reproduce (shipped ladder 201.7 >
171.0 > 149.3, bit-identical across 14 runs), the authored cover arithmetic was recomputed exactly
by three reviewers, gameplay-inertness was proven twice over, Release is 0/0 and an independent
`qa-sweep --full` came back 48/48. Five blockers were raised against it. All five are fixed here,
plus three cheap evidenced items. Two more findings the lead accepted as **costs, not defects**,
are recorded in `docs/ROADMAP.md` instead of fixed.

#### 1. The cover volume re-rolled its MATERIAL when you shot part of it away

The wave grouped 4-connected same-type cover with a union-find rebuilt **from the live grid every
frame**, took the MIN linear index as the group root, and hashed the volume's material `form` and
its footprint `jx/jy/jRad/jIn/lift` off that root. Cover is destructible — `Grid.DamageCover` walks
a tile High → Low → Floor during play — so **destroying or downgrading a volume's north/west-most
tile moved the root and re-rolled the material for every surviving tile.** A wall turned from
crates into rock mid-mission, reading as a rendering glitch rather than as damage. Grenades,
barrels and sustained fire all trigger it.

**Reproduced first, on the reviewer's own probe tree** (`/home/user/rev-w4-probe`, a 3-tile
HighCover run stamped on clean floor, `SIGHTLINE_W4KILL=1` deleting its NW tile): over the two
surviving tiles, two runs of the *same* configuration differ in **755 px** (the animation noise
floor — screenshots are not byte-identical), while intact-vs-killed differs in **4387 px**. Read as
images the survivors go from a masonry WALL face to a WRECK's diagonal shear, and the whole volume
shifts by the re-rolled jitter.

**The fix: the identity stops being a function of the live tile set.** `Grid.CoverSeed[x,y]` is a
per-tile volume identity assigned **once**, when a cover tile first exists (`Grid.SeedCoverVolumes`,
called from `ResetCoverHp` at mission build and idempotently by `DrawCover` so no stamping site has
to remember), and never re-derived. `DamageCover` clears the seed of a tile it destroys. Two
adjacent tiles draw as one volume when TYPE, ELEVATION TIER **and** SEED all match, so a High tile
shot down to rubble splits off from its run instead of dragging a mismatched jitter into it. Cover
that appears mid-mission **adopts** the identity of a volume it is touching rather than relabelling
it — a barricade deployed against a wall joins that wall *and takes its material*, and the standing
wall can never re-roll because something was built next to it. At mission build nothing is seeded,
so the partition and the roots are exactly what the per-frame union-find used to produce: **the
pristine board is unchanged** (plain-board capture, same seed, against the pre-fix tree: 17,939
differing px over the board region against a 16,052-px same-tree animation noise floor, i.e. inside
the noise; and BOARDTEST's shipped ladder is the same 201.7 / 171.0 / 149.3 to one decimal).

**Proved by a new gate and by a same-binary A/B.** `SIGHTLINE_BOARDTEST` gate E stamps a four-tile
HighCover run, photographs its FAR tile, destroys the run's NW tile through the real
`Grid.DamageCover` path (High → rubble → gone) and photographs it again. With the clock pinned the
two frames differ by exactly one tile, so the far tile must come back byte-identical:

| | far-tile pixels changed by destroying the NW tile |
|---|---|
| shipped (stable seed) | **0 of 3360** |
| `SIGHTLINE_COVERSEED=0` (the pre-fix identity, re-derived every frame) | **1886 of 3360** |

`SIGHTLINE_COVERSEED=0` is a real negative control and not a mock: it clears the seeds each frame
before reseeding, which *is* "min linear index over the live 4-connected run". The visual A/B on one
binary is `SIGHTLINE_COVER=1 [+ SIGHTLINE_COVERKILL=1]` (a clean four-tile run at (5..8,5), the NW
tile destroyed): with `COVERSEED=0` the survivors gain a WRECK shear after the kill, with the
shipped path they are the same masonry as before it. Both frames read and judged.

#### 2. BOARDTEST was non-deterministic on the legacy path — the clock is now pinned

Ten runs of `SIGHTLINE_TOKENSTYLE=0 SIGHTLINE_BOARDTEST=1` on ONE Release binary gave a SELECTED
peak of 166.8 ×3 and 188.1 ×7. A pixel probe cannot sample an unpinned animation phase and call the
number a result. `Renderer` now routes every wall-clock read through `Now()`, which returns
`Raylib.GetTime()` unless the harness-only `Renderer.TimePin` is set; `BoardSelfTest` pins it to
**t = 3π/10** for the whole test and restores −1 on the way out. That is a stated constant, not a
claim about which of the file's 45 clock reads owned the swing — the selection halo's
`0.55 + 0.40·sin(t·5)` on `Pal.Accent` is *a* candidate, and pinning it to its minimum did not
produce the dimmer mode, so the diagnosis is explicitly not claimed. Ten runs, one line:

```
  SELECTED soldier  mean  121.6  PEAK  188.1
  ACTIVE hostile    mean  101.2  PEAK  170.3
  DORMANT pod       mean  131.7  PEAK  219.9      <- x10, identical
```

The shipped path was never flaky (14/14 in review) and is also **phase-independent**: 201.7 at
seven pins swept over 0–12 s. Gate B (the cover seam) is phase-independent too, measured over the
same sweep.

#### 3. The doc over-claim that followed from it

`docs/DEVLOG.md` presented 166.8/158.1 as deterministic output ("Both audit figures reproduce
exactly on this tree"); they reproduce about 30% of the time. The claim is corrected in place, in
both the thesis section and the dial table, and in `docs/ROADMAP.md`. The pinned figure is
**188.1**, and it is quoted as the pinned figure — *not* swapped in silently, because 188.1 was
equally unstable before the pin. `src/Game.Harness.cs`'s header comment carries the same correction.
The dormant pod's **219.9** was never in doubt and is unchanged, so the inversion the wave exists to
fix is untouched by any of this.

#### 4. The dormant pod's `?` was occluded — the wave's own compensating cue

The wave moved the glyph from `p.Y-13` to `p.Y-44`. With `Cfg.OriginY=40` and `Cfg.Tile=64` a row-0
token centre is y=72, putting a 23px glyph at y 28..51 behind a top HUD bar whose lower edge
measures ~y45 — **entirely gone**. It was also swallowed whenever a unit stood on the tile above,
which is the NORMAL case since hostiles spawn in pods. `Renderer.MarkerY` now clamps the high slot
to the board's top edge and drops it to `p.Y-26` (inside the pod's OWN tile, where a neighbour's
token cannot paint over it) when the tile above is occupied; in either fallback the glyph is seated
on a dark lozenge, because in a fallback slot it lands on the token's own ring instead of on clean
floor and a 1px drop shadow is not enough separation there. The clean high slot is unchanged and
keeps its light, unbacked look. The same treatment is applied to the SUSPICIOUS `!`, which had the
identical bug and pre-dates the wave (it has sat at `p.Y-42` since before `main`).

New harness hook `SIGHTLINE_MARKERS=1` stages exactly the three cases. Screenshots re-shot and read:
a row-0 dormant pod's `?` is fully on the board and legible; a row-0 suspicious `!` likewise; a pod
with a soldier on the tile above shows the `?` on its lozenge at the top of its own token (it was
invisible before). **Residual, stated plainly:** in the stacked case the soldier's ground arc paints
over the top of the lozenge, so the glyph reads at maybe 70% — better than gone, not as good as the
clean slot.

#### 5. A false factual claim in three places

`src/Renderer.cs` asserted "no `IsKeyDown` call existed anywhere before this one"; the DEVLOG
repeated it; and `CLAUDE.md` — the continuity contract — said SHIFT is "the only held modifier in
the game". `git show main:src/Game.Codex.cs` lines 80–81 hold **four** pre-existing
`Raylib.IsKeyDown` calls (Right/D, Left/A, scrolling the field manual). All three are corrected. The
defensible claim, and the one now written, is that SHIFT is the first key read as a **modifier** —
held to change what another element shows — not the first key read while held.

#### Also taken (cheap, evidenced)

* **`SIGHTLINE_CB=1` had no effect on BOARDTEST** — the BOARDTEST block returns from `Program.cs`
  ~line 160 while `Pal.SetColorblind` is applied ~line 770, so the wave's most load-bearing
  robustness claim (rungs stated as target luma survive the colourblind palette) had **zero**
  self-test coverage. The env read now also happens inside the BOARDTEST block, and more to the
  point **gate A runs twice, in both palettes**, and asserts the ladder in each:

  | | SELECTED | ACTIVE | DORMANT |
  |---|---|---|---|
  | normal palette | 201.7 | 171.0 | 149.3 |
  | colourblind palette | 201.7 | **171.5** | 149.3 |

  A reviewer had verified this by hand; it is now a gate. On the legacy path the colourblind repeat
  adds three more violations, which is why `TOKENSTYLE=0` now reports FAIL (5) where the wave's
  table says FAIL (2).
* **`bal/w4after.json` and `bal/w4base.json` are out of the index.** Commit `d3cc4d8` added `bal/`
  to `.gitignore` while committing both files in the same commit — 2064 lines byte-identical to the
  canonical copies under `docs/measurements/w4-board/` (verified by diff before removing).
* **The "11.6 edges/primitive vs 1.0" contour figure is relabelled a CODE-STRUCTURE metric**, in
  both the DEVLOG and the ROADMAP. A reviewer compared the frames and the strokes land on the same
  pixels; what the ratio buys is the welded joint and the continuous dash phase, not a different
  silhouette.

#### One number moved that is worth stating

Gate B's stamped-pair top-face median went **85.9 → 90.2** luma (its dark-trough count is 1 either
way, and `COVERMERGE=0` still measures 10). Cause: the gate stamps its cover pair *after* mission
build, i.e. after the board is seeded, and one of the pair's neighbours — (13,8) — is pre-existing
mission cover. Under the adoption rule the stamped pair now joins that standing volume and inherits
its root (157) instead of rooting on itself (139), so the pair draws with a different hash. This is
an artefact of a harness that stamps terrain into a live board, not a change to how an authored map
renders; a pristine arena has nothing pre-seeded and is bit-identical.

#### WHAT I DID NOT FIX

* **The two accepted costs** — merged volumes reading flatter on deep runs, and four identical △
  tier glyphs reading as surface pattern — are ROADMAP items with the reviewers' proposed
  counter-measures (a stronger outer rim or a north-edge occlusion band; per-volume glyphs at BOTH
  ENDS of a run). The lead's call, and I agree with it: both are trades the wave made knowingly, and
  both want a measurement rather than a reflex.
* **The stacked-pod `?` is still partly overpainted** by the unit above's ground decorations. Fixing
  it properly means the marker owning a z-layer above the unit pass, which is a draw-order change to
  `Game.Draw` this fix's scope does not include.
* **`DrawMoveOverlay` still reads `Raylib.IsKeyDown` in the draw path.** Unchanged and still a wart;
  it stays on the ROADMAP.
* **Nothing was re-balanced and nothing was re-measured on the flywheel.** Every change here is in
  `Renderer.cs`, `Grid.cs` (a visual-only field plus its seeding), the harness and the docs.
  `Grid.CoverSeed` is read by exactly one file — `src/Renderer.cs` — takes zero draws from
  `Util.Rng`, and is derived from the tile layout alone; `SIGHTLINE_PAIRTEST` is byte-identical.

#### VERIFICATION

* Release **0 warn / 0 err**.
* `bash scripts/qa-sweep.sh --full`: every self-test **PASS** including BOARDTEST and **PAIRTEST**,
  COVERAGE GAP block empty.
* Autoplay ×3: WIN m6 / WIN m6 / LOSE m3 — no exception, no TIMEOUT.
* `SIGHTLINE_TOKENSTYLE=0 SIGHTLINE_BOARDTEST=1` ×10 on one Release binary: **ten identical lines**.
* Negative controls all fire: `TOKENSTYLE=0` FAIL (5), `COVERMERGE=0` FAIL (1, 10px trough),
  `MOVESTYLE=0` FAIL (1, 1.0 edges/stroke), `COVERSEED=0` FAIL (1, 1886px re-roll).
* Screenshots read and judged: the cover-destruction A/B in both identity modes, a row-0 dormant
  pod, a row-0 suspicious pod, a stacked pod, and the open-field control.

## PROGRAM RESONANCE — Wave W1 "TRUE INSTRUMENT" (dev; worktree `wt-w1`, branch `wave/true-instrument`)

**Base commit: `d350416`** (RESONANCE milestone 2). Four code commits (`d336016`, `7b9878d`,
`5ae9149`, `e264335`) plus docs, deliberately kept separately bisectable, because the third one
breaks something on purpose.

> ### ⚠ `5ae9149` (W1/3) IS THE INVALIDATING COMMIT — AND IT IS THE ONLY ONE
>
> **Every archived CRN world in this repository is incomparable across it.** Any balance number
> measured at or before `d350416` and any number measured at or after `5ae9149` are measurements
> of different worlds, even on the identical slot seed. This is not a regression; it is the fix.
> Every pre-W1 archive — `docs/measurements/x1/`, `x2/`, `w4/`, `l1/` — was measured on a gameplay
> stream that no longer exists. They stay valid as history; they may not be compared with, or
> rescaled to, a number from the current tree. **Re-measure. Do not rescale.** CLAUDE.md's
> ladder-of-record block now carries this warning too — the W1 review found it said nothing about
> the break at all, and that is the one block a fresh session is guaranteed to read.
>
> The measured size of the break, over the exact 10 slots X2 archived as `S1-h0-b0`:
>
> | | slots reproduced | chunk win-rate |
> |---|---|---|
> | archive `x2/S1-h0-b0` (base `a61ef42`) vs W1 commits 1-2 | **10 / 10** | 65% / 65% |
> | archive `x2/S1-h0-b0` vs W1/3 as first written (`6bcdde6`, Fx only) | **3 / 10** | 65% / 55% |
> | archive `x2/S1-h0-b0` vs W1/3 as shipped (`5ae9149`, Fx + the bob draw) | **3 / 10** | 65% / 60% |
> | W1/3-as-first-written vs W1/3-as-shipped (the bob draw alone) | **0 / 10** | 55% / 60% |
>
> Read that top row first: the X2 archive was still bit-for-bit live on this tree right up to
> W1/3 — a base commit later, through a whole milestone merge. The bottom row's 10-point
> win-rate move is **not** a claim that the game got harder; at n=20 the SE is ~11 points. It is
> a claim that **seven of ten worlds are different worlds now**.
> (The three "reproduced" slots are the ones whose win/loss pattern happens to survive a reshuffle;
> at this granularity ~3/10 is about what chance gives you, so read the row as "the worlds changed",
> not "30% of them held.")
>
> The last row is why the missed `Unit()` bob draw had to ride THIS commit rather than a later one:
> on its own it moves **every one of the ten slots**. Shipped separately it would have been a second
> full invalidation, thrown at a post-W1 ladder that had just been measured.
>
> **The dev-instrument agent's L1 n=80 ladder (`docs/measurements/l1/`, base `8dd5e90` per that
> wave's own brief — its own write-up is the authority on the commit) is THE PRE-REPAIR
> LADDER.** It is the only n≥80 picture of the frame-coupled tree that will ever exist, and it is
> the reference W7 needs in order to say how much of the ladder's shape was the dice being a
> function of the frame rate. It was in flight while this wave was built and must be archived
> before W1 lands.

### THE DEFECT — gameplay was a function of the frame rate

`src/Fx.cs:155` rolled `Util.RandF()` **once per RENDERED FRAME**, on the **shared gameplay
stream**, for as long as a screen shake was decaying:

```csharp
if (Shake > 0.01f) { Shake *= …; float a = Util.RandF() * MathF.PI * 2f; … }
```

So the dice a campaign rolled depended on how many frames were drawn while the screen was
wobbling — i.e. on the frame rate, on the animation-speed comfort setting, and on whether the
player has screen shake switched on at all. **`Fx.ShakeOn` is a shipped accessibility toggle.**
A player who turns screen shake off to avoid motion sickness was playing a different game from
the same seed. Measured, same binary, `SIGHTLINE_FXRNG=0` (the old coupling) against the default:

```
PRE   seed99   1x/shake WIN  m=5 t=28 f=9713  | 8x/shake LOSE m=3 t=23 | 1x/noshake WIN  m=5 t=30
PRE   seed4242 1x/shake LOSE m=2 t=10 f=5490  | 8x/shake WIN  m=4 t=14 | 1x/noshake LOSE m=2 t=20
POST  seed99   1x/shake LOSE m=3 t=25 f=14241 | 8x/shake LOSE m=3 t=25 f=4081 | 1x/noshake LOSE m=3 t=25
POST  seed4242 1x/shake LOSE m=6 t=24 f=11548 | 8x/shake LOSE m=6 t=24 f=3232 | 1x/noshake LOSE m=6 t=24
```

(`docs/measurements/w1/gate3.txt`, re-run against the SHIPPED W1/3. The POST column differs from
the first draft of this write-up because W1/3 now also carries the `Unit()` bob draw — see "THE
DRAW I MISSED". The PRE column is unchanged: `SIGHTLINE_FXRNG=0` restores both couplings at once.)

and the raw reproduction the brief asked for, one seed through the ordinary autoplay entry:

```
PRE   SIGHTLINE_SEED=99 SIGHTLINE_ANIMSPEED=1   -> RESULT: WIN  mission=6
PRE   SIGHTLINE_SEED=99 SIGHTLINE_ANIMSPEED=20  -> RESULT: LOSE mission=3
POST  SIGHTLINE_SEED=99 SIGHTLINE_ANIMSPEED=1   -> RESULT: WIN  mission=6
POST  SIGHTLINE_SEED=99 SIGHTLINE_ANIMSPEED=20  -> RESULT: WIN  mission=6
```

One seed, one comfort setting, a six-mission win or a mission-three washout.

**The harness never saw it because it was holding the frame rate still.** `dt` is pinned to
1/60 in every batch loop and `Game.AnimSpeed` hard-pins 1x under `AutoPlay || NoPersist`.
PAIRTEST passed for years not because gameplay was independent of presentation but because two
legs of the same pinned harness draw the same number of frames.

**Fix:** `Util.FxRng` — a separate clock-seeded stream that `Util.Reseed` deliberately does NOT
touch (a SEEDED DAILY board must replay; nobody wants the sparks to replay). All 29 draw sites in
`Fx.cs` route through `Util.FxRandF/FxRandInt/FxRandRange`. `SIGHTLINE_FXRNG=0` restores the
coupling, so the defect stays reproducible in the shipped binary instead of by archaeology.

One line beyond the wave's file list, flagged: `Anim.cs`'s miss-scatter (where a missed tracer's
endpoint lands) also drew from the gameplay stream. It is event-driven, not frame-driven, so it
never made gameplay frame-dependent — but it is the same defect one "reduce visual clutter"
toggle away from becoming the same bug, so it moved too.

**New gate — `SIGHTLINE_RNGFRAMETEST`** (wired into `qa-sweep.sh`): four pinned seeds ×
{AnimSpeed 1x, 8x} × {shake on, off}; all four legs of a seed must agree on result, missions and
turns. It carries a **vacuity guard**: the 1x and 8x legs must differ in FRAME COUNT, or the
anim-speed lever is not reaching the queue and "identical" would mean nothing. It prints FAIL
under `SIGHTLINE_FXRNG=0` and PASS by default (both shown above).

### THE BATCH WAS 99% GL AND 1% GAME

Every headless batch loop called `Display.RenderFrame(() => Raylib.ClearBackground(Pal.Bg))` once
per **simulated** frame. Nothing in that call draws game content; its only job was to make raylib
pump the window event queue so `WindowShouldClose()` stays honest. `Raylib.PollInputEvents()` is
that pump on its own (`EndDrawing()` *is* `SwapScreenBuffer() + PollInputEvents()`), so the close
semantics are preserved exactly and the llvmpipe clear + buffer swap are dropped.

`SIGHTLINE_BALANCE=10`, heat 0, `SIGHTLINE_BALANCE_BASE=100`, Release binaries from snapshots,
same container (`nproc=4`):

| tree | batch wall-time | s / campaign slot | data-ready | `runs` asserted |
|---|---|---|---|---|
| `d350416` (pre) | **509.0 s** | 50.9 | 511 s | 20 |
| W1/1 | **5.3 s** | 0.53 | 10 s | 20 |
| W1/2 | **4.2 s** | 0.42 | 5 s | 20 |

**THE LOAD ON THE 509 s ROW IS UNKNOWN, and the review was right to press on it.** An earlier
draft of this section wrote "loadavg 20–30 for all of them" to reconcile the 509 s baseline with
CLAUDE.md's 311 s reference. My own new `harness{}` block refutes that: the three chunks that
carry one read `loadAtStart` **8.55, 13.00 and 27.80** — one of the three is in that range — and
**`G1-pre` and `G1-c1`, the two rows the claim existed to explain, predate the block entirely and
have no load record at all.** So the honest statement is: the pre/post rows above are three
separate binaries run at three unrecorded moments on a shared four-core container, and the ratio
between them is **not a controlled measurement**. It is quoted here only as the raw wall-clock of
the chunks as they ran.

**The controlled number is 17-33x (median ~23x over four measured pairs)**, and it is in the
autoplay A/B below: one binary, one seed,
identical frame counts, only the GL work removed. That is the figure to cite; CLAUDE.md cites it.
(Being able to catch this at all is the point of the `harness{}` block — the first thing it did
was contradict its own author.) Either way the conclusion holds: the
number a balance round cost was almost entirely a software rasteriser clearing a window nobody
looks at. A 40-campaign rung is now under a minute; the 12-chunk X2-shaped round is minutes, not
an afternoon. `SIGHTLINE_BALANCE_DRAW=1` restores the old path for an A/B.

The same change applies to the AUTOPLAY smoke path in W1/4 (`autoplay && !shot` only; a shot
frame requested on top of autoplay still draws for real). That one A/Bs cleanly on a **single
binary and a single seed**, because after W1/3 the GL work cannot touch the dice — so the frame
counts and the results are identical and only the clock moves (`autoplay_ab.txt`):

```
seed99     DRAW=1 (pre-W1 GL clear)       19.8s  RESULT: WIN mission=6 frame=11680
seed99     default  (PollInputEvents)      0.9s  RESULT: WIN mission=6 frame=11680
seed4242   DRAW=1 (pre-W1 GL clear)       22.0s  RESULT: WIN mission=6 frame=12389
seed4242   default  (PollInputEvents)      0.9s  RESULT: WIN mission=6 frame=12389
```

Same binary, same seed, same frame count, same result — only the GL work removed. This is the
wave's only CONTROLLED timing measurement and therefore the only speed-up worth quoting.
**Across four measured pairs it is 17x to 33x (22.0 and 24.4 above; 32.8 and 17.4 on an earlier
run at different container load), median ~23x.** Quote the RANGE, not a point — the spread is
load on a shared four-core box, not the change. CLAUDE.md's "`SIGHTLINE_AUTOPLAY=1` (Debug)
~22 s" was almost entirely llvmpipe.

### A DISPLAY-LESS BATCH USED TO LOOK LIKE A FINISHED ONE

With no display `InitWindow` fails, `WindowShouldClose()` is true before frame one, every match
loop falls straight through — and the process still printed a full report, still **wrote an
aggregate JSON with `runs=0` over the previous chunk's data**, still claimed N matches, then
exited 139 out of the GL teardown, which reads as "finished, crashed on the way out". X2 had to
bolt an external `runs`-field assertion onto every chunk script because of it, and CLAUDE.md
carries a five-point contract whose first point is this hazard.

Now: `RequireWindow()` refuses at the door on the balance, PAIRTEST and STACKTEST entries —
named message on stderr, exit 2, nothing written — and `Stats.WriteJson` refuses to serialise a
zero-run aggregate at all. Measured (`gate2.sh`), with a sentinel file standing in for the
previous chunk's data:

```
before: mtime=1788029404  md5=b42c19653c703857ef0313bbdc313072
BALANCE: no display - run under xvfb-run. No data written.
exit code: 2
after : mtime=1788029404  md5=b42c19653c703857ef0313bbdc313072
GATE2a: PASS (non-zero exit, JSON untouched)   GATE2b: PASS (same command under xvfb -> runs=4)
```

### THE INERTNESS PROOF (commits 1 and 2)

Commit 1 is inert on the **whole document**, no exclusions:

```
$ bash docs/measurements/w1/inert_diff.sh G1-pre.json G1-c1.json
(empty diff — IDENTICAL)
```

Commit 2 adds read-only keys, so its diff deletes exactly those keys and nothing else:

```
$ bash docs/measurements/w1/inert_diff.sh G1-c1.json G1-c2.json \
      harness runWinRateExStalemate instrumentHealth byObjectiveByBucket byNodeKind shotGap
(empty diff — IDENTICAL)
```

Every per-slot RunRec, every win, loss, turn count, shot and kill identical across both. And the
strongest form of the same claim: **the commit-2 binary reproduces X2's archived `S1-h0-b0`
chunk exactly, 10 of 10 slots** (`gate4.py`), across a base-commit change.

### WHAT THE NEW INSTRUMENT SAYS ON ITS FIRST BATCH

n=20 campaigns, heat 0 — small, so these are *shapes*, not results.

**Shot-gap deciles** — for every ARMED soldier-turn, `(best − runner-up) / best`:

```
decile   0    1    2    3    4    5    6    7    8    9
count  684  189  230  148   99   90   80   31   11  850   (POOLED n=2412, mean gap 0.490)
```

Pooled over all three instrumented chunks (the individual chunks agree: mean gap 0.458 / 0.510 /
0.502, d9 share 33 / 35 / 38%). It is **bimodal**. 35.2% of armed soldier-turns are decile 9 —
the shot picks itself (a lone legal target, or a runner-up worth under a tenth of the best).
28.4% are decile 0 — a genuine near-tie. The middle is thin. So "which target?" is almost never a *graded* judgement; it is
either forced or a coin-flip. W4's `CountMeaningfulChoices` asks "how many options are within
12% of best?", which is a threshold, and a threshold cannot tell a 40-vs-40 tie from a 40-vs-39
one. This is the same population as a continuous distribution, and it says W4's near-invariant
at ~1.6 may be measuring a bimodal population's mean — **which is exactly the statistic that is
least informative about a bimodal population.** (Same batches, same population, for the avoidance
of doubt: on `W1post-h0-b0` `choicesPerArmedSoldierTurn` reads **1.895** and that chunk's deciles
sum to exactly its 800 armed soldier-turns — these are two views of one denominator, not two
metrics.) W4's open item ("chase it with a positioning lever, not another threat lever") should be
re-argued against this histogram first. **Caveat, stated because the ELITE row above is a lesson
in not stating it:** these three chunks straddle the W1/3 break, so the pooled histogram mixes
pre- and post-break worlds. The bimodality is visible in each chunk separately, which is why it
survives the caveat; the exact shares do not.

**`byNodeKind`** — the first record of which campaign-map nodes the flywheel actually played.
**Pooled over all three instrumented chunks**, which is the correction the W1 review forced: an
earlier draft of this section printed only `W1post-h0-b0` and called it "the first time", when
three chunks carry the block and W1post is merely the LAST by `harness.startedUtc`.

| node | pooled n | pooled mission win% | the three chunks, in start order |
|---|---|---|---|
| Start | 60 | 100.0 | 100 / 100 / 100 |
| Combat | 74 | 89.2 | 88.5 (26) / 87.0 (23) / 92.0 (25) |
| Supply | 36 | 94.4 | 85.7 (14) / 100 (11) / 100 (11) |
| Boss | 44 | 79.5 | 91.7 (12) / 86.7 (15) / 64.7 (17) |
| **Elite** | **29** | **79.3** | **57.1 (7) / 80.0 (10) / 91.7 (12)** |

**I withdraw the ELITE finding.** The earlier draft read the last chunk alone (Elite 91.7 vs
Combat 92.0) and concluded ELITE "measures identical to an ordinary Combat node" — a claim the
other two chunks flatly contradict (57.1% and 80.0%). The pooled figure points the *other* way,
Elite 79.3% against Combat 89.2%, and even that is not a result: n=29 carries roughly ±7.5 points,
so a 10-point gap is about 1.3 SE. Two further caveats make it weaker still — the Elite cells
range over 35 points across three 20-run chunks, and the three chunks **straddle the W1/3 break**
(the first two are pre-break worlds, the third post-break), so they are not strictly poolable.
The honest reading is: **ELITE's difficulty relative to Combat is unmeasured at this n**, and the
cherry-picked version of this paragraph was exactly the kind of claim this project punishes.

**`instrumentHealth` — and it has already fired.** An earlier draft of this section said the one
batch it ran on had zero STALEMATEs, so `runWinRateExStalemate` had never differed from
`runWinRate`. **That was wrong, and my own archive contained the counter-example when I wrote it.**
Across the three instrumented chunks:

| chunk | runWinRate | runWinRateExStalemate | STALEMATE losses | harnessLossPct |
|---|---|---|---|---|
| `G1-c2` | 55.0 | 55.0 | 0 | 0.0 |
| **`R0diag-h0-b0`** | **65.0** | **68.4** | **1 (5%)** | **5.0** |
| `W1post-h0-b0` | 55.0 | 55.0 | 0 | 0.0 |

So the **first reading is a 3.4-point gap at heat 0** — one campaign in twenty that every
published ladder would have counted as the game beating the player, when it was the autopilot
failing to find a finishing line at the turn cap. One run is not a rate; what it establishes is
that the correction is **non-zero on ordinary batches at the easiest rung**, and that a rung
measured at n=40 can carry two of these. Whether the higher rungs — where a stalled autopilot is
likelier — are worse is still **unmeasured**.

**`harness`** — `nproc=4`, loadavg **27.80 at batch start, 30.30 at end**. Seven times the core
count. It does not bias a deterministic sim, but it is why the wall-clock table above disagrees
with CLAUDE.md's 311 s, and it is exactly the context the artifact never recorded.

### THE ROUTE NOBODY CHOSE

Every balance number this project has published was drawn through **one** campaign-routing policy
that nobody selected: *prefer an Event node when one is reachable, else take `nn[0]`* — and
`nn[0]` is simply the lowest row of the next column, because `MissionNode.Next` was appended in
row order. The bot walks one edge of the DAG for ever. `SIGHTLINE_ROUTETEST` measures it over 400
maps and 678 **real** (k≥2) branch choices:

| | branch-0 rate | Combat | Event | Elite | Supply | Boss |
|---|---|---|---|---|---|---|
| a fair deal would be | 45.1% | — | — | — | — | — |
| `first` (SHIPPED) | **81.4%** | 29.7% | **23.1%** | **11.7%** | 15.6% | 20.0% |
| `hash` | 51.5% | 33.9% | 15.6% | 13.8% | 16.8% | 20.0% |

The flywheel plays **48% more "?" beats and 15% fewer ELITE fights** than a fair deal would.
`SIGHTLINE_ROUTE=hash` deals the branch from `Util.Hash3(MapSeed, 13, mission)` — zero draws from
`Util.Rng`, so CRN pairing survives it (ROUTETEST asserts that directly by snapshotting the
shared stream around the whole sweep). **The default stays `first`**: this wave measured the
sampling frame, it did not change it.

### THE SAVE FORMAT HAD A SECOND HALF NOBODY WAS GUARDING

CLAUDE.md's append-only enum guard covers thirteen enums persisted by ordinal. But a save also
stores the entire campaign map as **one int** — `MapSeed` — and regenerates the whole DAG from it
on load. `Run.GenerateMap` draws from a local `new Random(seed)`, so its **draw order is as much
a part of the save format as any ordinal**, and it is far easier to disturb: one stray
`rng.Next()` before the mid-column shuffle re-deals every existing save's node kinds, factions and
edges, and silently re-points `MapPos` at a different mission. `SaveGame.MapFingerprint` +
`PersistedGenerators` now pin three seeds, checked by SAVETEST. Demonstrated both ways:

```
# one rng.Next() inserted before the shuffle in Run.GenerateMap:
SAVETEST: FAIL (mapShape:seed1 (golden 0xC169C99E, actual 0x3977AAFC), mapShape:seed424242 …)
# reverted:
SAVETEST: PASS (… 13 persisted-enum fingerprints match; 3 map-generator fingerprints match; …)
```

`HEATLADDERTEST` gained the matching guard for the difficulty axis: the cumulative
`(EnemyDelta, StatDelta, DmgDelta, AiTier)` vector for all ten levels is pinned. The published
ladder is a table of win-rates **by heat level**; if a rung moves, the column headings still say
"heat 4" and every archived number silently changes meaning.

### THE FRAME CAP BECAME A MEASURED NUMBER

It had been a round `20000` since it was written, with nothing saying where it came from. Over 30
fresh Release autoplays (`docs/measurements/w1/framecount.txt`):

```
n=30  min=2461  median=9492  p90=12530  MAX=13589   (second-largest 13407)
```

The old cap was **1.47x the observed maximum** — a much thinner margin than anyone had reason to
believe, given the contract is "never a TIMEOUT". Now `3 x autoMax` = **40767**, with the max kept
beside it as a named constant, and a TIMEOUT line that reports the multiple of it instead of just
echoing the cap.

**NAMING, corrected by the W1 review.** The first version of this shipped the constant as
`autoP99` and quoted "p99 = 13536". **You cannot estimate a 99th percentile from n=30.** The top
two samples are 13589 and 13407, so any "p99" is an interpolation between the two largest
observations and carries nothing the maximum does not — it was a quantile-shaped word wrapped
round a max, in a wave whose entire thesis is that instruments should not overstate what they
know. Renamed `autoMax`, described as the observed maximum, and the `framecount.sh` header now
says to raise n if a real quantile is ever wanted.

The **balance batch's** own `frameCap` is deliberately untouched: it decides whether a censored
match is scored a loss, so moving it would be a measurement change.

### THE DRAW I MISSED, AND WHAT IT SAYS ABOUT THE TEST I WROTE

The W1 review found a second draw on the gameplay stream that I had not: **`Unit()`'s constructor
set the idle-bob phase from `Util.RandF()`** (`src/Unit.cs`). All twelve `Bob` read sites are
cosmetic phase terms in `Renderer`, and the field's own comment already said "render-only". The
reviewer found it by instrumenting `Util.Rng` as a counting property with `StackTrace` capture and
catching exactly one draw-side stack over a complete campaign.

**Describe it precisely, because the obvious description is wrong.** This is *not* a live
frame-count coupling. `Bob` is drawn once per unit construction — a deterministic gameplay event —
so it introduces no frame dependence of its own. It is worse in a quieter way: `Renderer` holds
`static readonly Unit _codexGlyphStub = new Unit()`, whose initialiser fires lazily on the first
`DrawBoard`. So **a process that RENDERS took one gameplay draw that a process that does not
render never took** — a fixed one-draw offset between the shipped binary and the instrument. And
after W1/1 and W1/4 the instrument renders nothing at all: the flywheel, PAIRTEST, autoplay and
RNGFRAMETEST are all now non-rendering processes.

**Attribution, and a correction to my own first draft of this paragraph.** The split
`no-draw WIN mission=6 frame=6008` / `forced-draw LOSE mission=4 frame=3962` is **the reviewer's
measurement, not mine** — I have written it as mine once already and that is exactly the habit
this project punishes. I tried to reproduce it end to end with
`SIGHTLINE_FXRNG=0 SIGHTLINE_BALANCE_DRAW=1` and **it does not reproduce**, for a reason worth
recording: `SIGHTLINE_BALANCE_DRAW=1` restores a bare `ClearBackground`, never `game.Draw()`, so
it never calls `Renderer.DrawBoard` and never fires the static initialiser. There is no shipped
dial that makes an autoplay run draw the board for a whole campaign at a usable speed.

**What I did measure, twice, is the same claim from both ends:**
* `RNGFRAMETEST` phase 2, with the bob fix reverted and rebuilt:
  `newUnit=DREW draw30Frames=DREW probeSensitive=yes -> FAIL`, while phase 1 printed MATCH x4 and
  would have passed. Constructing a Unit, and drawing thirty real frames, each moved the gameplay
  stream.
* The CRN chunk table above: the bob draw **on its own** changes **10 of 10** slots
  (`W1post-h0-b0` vs `W1final-h0-b0`, same slots, same heat, the only difference being this one
  line). It is a real gameplay-affecting draw, not a rounding artefact.

It is folded into **W1/3**, the one authorised break, and not shipped separately. That is an
economic decision and it is worth stating: `new Unit{...}` is the gameplay spawn path, so moving
its draw shifts every downstream draw. Landing it in a later commit would have been a **second
full CRN invalidation** — and it would have invalidated the post-W1 ladder that is about to be
measured. One break, both defects.

**The test I shipped could not have caught it, and I should have seen that.** RNGFRAMETEST phase 1
certifies FRAME-COUNT invariance: four seeds × {1x, 8x} × {shake on, off}. Every one of those legs
runs the same non-rendering path, so all four agreed and the test passed. With `Bob` reverted it
still passes phase 1 — verified, MATCH ×4 — while the new phase 2 prints
`newUnit=DREW draw30Frames=DREW probeSensitive=yes -> FAIL`.

(The adjudicator did **not** uphold the stronger form of this — that RNGFRAMETEST "cannot detect
the defect class it certifies". It certifies frame-count invariance, that invariance is real, and
a reviewer independently confirmed it holds in the RENDERING path too: seed 4242 LOSE m=4 at 1x,
8x and 20x on frame counts 3962/1318/1218. Phase 1 was narrow, not wrong. Phase 2 widens it.)

**Two more things the review broke that I had built.** RNGFRAMETEST's SHAKE lever had no vacuity
guard: deleting `game.Fx.ShakeOn = shake;` left the test printing MATCH ×4 and PASS with
byte-identical output — certifying an invariance it never varied. `Fx.ShakeApplied` now counts the
`AddShake` calls that actually moved the screen; sabotaged, the test prints
`VACUOUS (shake lever inert: shk 264/264)` and FAIL. And `RngFrameTest` dialled `SIGHTLINE_HEAT=2`
without restoring it, against the house stash-and-restore pattern; it restores it now.

**A grep would not have saved me.** `qa-sweep.sh` gains `FXSTREAM`, a free static tripwire
asserting `src/Fx.cs` contains zero `Util.Rand*`. Its scope is stated honestly in the script
itself: **it would not have caught `Bob`, which lives in `Unit.cs`.** The runtime assertion —
"constructing a Unit and drawing thirty real frames both leave `Util.Rng` untouched, and the probe
is proven sensitive by a deliberate draw" — is the guard that catches this class. The grep is a
backstop that also runs when the binary will not build.

### VERIFICATION

Release **0 warn / 0 err**. `qa-sweep.sh --full` (through `docs/measurements/w1/sweep.sh`, which
adds the house isolation exports the sweep inherits but never sets): every self-test PASS,
including the three new lines `ROUTETEST`, `RNGFRAMETEST` and `FXSTREAM`, plus `PAIRTEST: PASS`
and an **empty COVERAGE GAP block** (transcript: `docs/measurements/w1/qa-sweep-full.txt`).
Autoplay ×3 clean, no TIMEOUT.

The sweep's hand-maintained "N self-tests exist" footer had drifted a **fourth** time — and W1's
first attempt at fixing it made it a fifth, by hardcoding 52 while wave TRUE BAND was adding
BANDTEST on a parallel branch. Both waves wrote down the correct derivation and then pasted its
answer as a literal, which is precisely how it keeps drifting. It now RUNS the derivation (two
greps, one over `src/`, one over the sweep itself) so it cannot go stale, and it points at the
coverage guard as the real check.

**FXSTREAM caught itself on its first run, which is worth recording.** The static tripwire I added
for the "presentation never draws from `Util.Rng`" rule grepped the raw file and FAILed on
`Fx.cs:653` — a *doc comment* reading `deterministic hash (NOT Util.Rng)`. A tripwire that fires
on the word rather than the call is worse than none, because the second person to see it will
ignore it. It now strips `//` comments first, is verified to still catch a real violation (three
hits when one `Util.FxRandF()` is reverted to `Util.RandF()`), and says so in the script.

Gate by gate: **G1** the two empty diffs above, plus 10/10 slot reproduction of the X2 archive.
**G2** `gate2.sh`, both halves PASS. **G3** `gate3.sh`, FAIL under `SIGHTLINE_FXRNG=0` and PASS by
default, both pasted above — and now covering the render-purity phase in both directions. **G4**
`gate4.py`, 10/10 before the break and 3/10 after; the invalidation is measured, not asserted.
**G5** SAVETEST demonstrated failing on a deliberately sabotaged `Run.GenerateMap` (one extra
`rng.Next()`) and passing after the revert. **G6** the sweep above. **G7 (new, from the review)**
`gate6.sh` — the stale-file trap the refusal path created, demonstrated and then refused.

Every self-test that this wave added or changed has been shown FAILING on a deliberate sabotage
and PASSING clean: RNGFRAMETEST phase 1 (`SIGHTLINE_FXRNG=0`), its anim-speed guard, its shake
guard (lever deleted → `VACUOUS (shake lever inert: shk 264/264)`), its render-purity phase (bob
reverted → `newUnit=DREW draw30Frames=DREW`), SAVETEST's `mapShape:` pin (stray `rng.Next()`),
HEATLADDERTEST's rung-shape pin (wrong golden), ROUTETEST's skew and uniformity assertions, and
FXSTREAM. A test that cannot fail is not a test.

### MERGE NOTE — the conflict surface against wave TRUE BAND

W1 branches from `d350416` and so does TRUE BAND; neither is merged as this is written, and the
integration tip that also carries L1 is not visible from this worktree, so **W1 is NOT rebased
onto it** — rebasing onto a tip I cannot read would produce a branch the lead cannot use. What I
did instead was measure the conflict surface with `git merge-tree wave/true-band HEAD` and shrink
it. **Five files conflict, all of them trivially, and every one is semantically independent** —
no resolution requires choosing between the two waves' behaviour:

* **`src/Game.Autopilot.cs`** — inside `CountMeaningfulChoices`, on adjacent lines. TRUE BAND
  replaces `foreach (var v in vals) if (v >= best * 0.88f) comparable++;` with its additive
  `AdmitNearBest(...)`; W1 inserts a `Stats.RecordShotGap(best, second)` block immediately above
  it. **Keep both.** W1's shot-gap computes `best`/`second` directly and never reads the band, so
  the additive rule does not change what the histogram measures (it does change
  `choicesPerArmedSoldierTurn`, which is TRUE BAND's point, not W1's).
* **`src/Game.Harness.cs`** — both waves append a new self-test at the end of the file.
  **Keep both**, in either order.
* **`scripts/qa-sweep.sh`** — at the FOOTER only, and the resolution is **take W1's**. Both waves
  independently patched the same hand-maintained "N self-tests exist" line (the fourth time it has
  been wrong), and both wrote down the correct DERIVATION and then pasted its answer as a literal —
  TRUE BAND's `51 / 51 / 50` and W1's first attempt at `52 / 51`, each of which the other wave
  invalidates on contact. W1 now RUNS the derivation at runtime (one grep over `src/`, one over the
  sweep itself), so the footer is right whatever either wave added and it cannot drift a fifth
  time. TRUE BAND's `BANDTEST` line itself is elsewhere in the file and **does not conflict**.
* **`docs/DEVLOG.md`, `docs/ROADMAP.md`** — both-append-at-the-end. Keep both.
  `CLAUDE.md` auto-merges.

(Measured with `git merge-tree wave/true-band HEAD` against the finished branch, not guessed. An
earlier draft of this note claimed qa-sweep.sh "no longer conflicts" — that was true when I
measured it at W1/4, before the docs commit rewrote the footer, and it is corrected here rather
than left standing.)

### WHAT I DID NOT DO, AND WHAT IT COST

**The `AnimSpeed = 1` harness pin at `Game.cs:82` STAYS.** The brief asked for it to be removed
once RNGFRAMETEST was green. It is green, and the pin is no longer load-bearing for
*correctness* — RNGFRAMETEST now proves outcome-invariance across 1x and 8x directly, which is
the property the pin was silently defending. But the pin has a second job that is still real:
`Display.AnimSpeed` is a **persisted user setting**, and removing the pin would let a value read
off the player's `display.json` into a measurement path. That is precisely the class of coupling
this wave exists to delete. Removing it would also require rewriting `ONRAMPTEST`, which
explicitly asserts the pin (`noPersistNotPinned` / `autoPlayNotPinned`) — i.e. deleting a live
green invariant to satisfy a brief item, for no measured gain. Kept, and now *justified* rather
than merely inherited. **Cost:** none I can measure; the harness-only escape hatch
(`AnimSpeedOverride`, which RNGFRAMETEST drives) already provides everything a test needs.

**`runWinRateExStalemate` has one reading and it is non-zero** — see the table above; an earlier
draft of this write-up claimed the opposite and its own archive refuted it. What is still
**unmeasured** is the stalemate share at the rungs above heat 0, where a stalled autopilot is
likelier.

**`byObjectiveByBucket` is shipped and unread.** It is deliberately fine-grained (objective ×
squad size × HP band) and therefore sparse at n=20; nothing in this write-up quotes a cell from
it, because at these n every cell is n≤4. It is for the n≥80 wave.

**I did not rebase onto the integration tip** — see the merge note above; it is not reachable
from this worktree. The conflict surface is measured and the resolutions are written down instead.

**No ladder.** W1 measured no rung and publishes no win-rate. The tables above are instrument
readings on n=20, labelled as such. The ladder is W7's, and W7 must re-measure from scratch —
see the invalidation box at the top.

**The route finding is measured but unspent.** `SIGHTLINE_ROUTE=hash` exists, is proven
draw-free and near-uniform, and is **off**. Switching it would be a second archive invalidation
in one wave, and it is a balance decision, not an instrument one.

**`Anim.cs:354` was moved off the gameplay stream; nothing else outside `Fx.cs` was audited
exhaustively.** A file-by-file census (`Util.Rand*`/`Util.Rng` by file) shows `Voice.cs`,
`Renderer.cs`, `Audio.cs` and `Display.cs` reference the shared stream **only in comments and
their own self-tests** — those separations hold. `Game.cs`, `Mission.cs`, `Run.cs`, `Ai.cs`,
`Combat.cs` etc. are gameplay and belong on the gameplay stream. I did not audit whether every
one of those is event-ordered rather than frame-ordered; RNGFRAMETEST would catch a frame-ordered
one at the four seeds it drives, and it is green.

# PROGRAM RESONANCE — WAVE "TRUE BAND" (2026-08-29, dev on `wave/true-band`)

**The spec.** ROADMAP carried a ready-to-dev spec, written by X2 out of W4's finding: re-specify
`Game.Autopilot.cs :: CountMeaningfulChoices` axis (b) as an **additive** band. The stated
mechanism was that axis (b) counts destinations scoring within **15% of the BEST** safety score
`24 − TileExposure + cover*8 + height*5`, so raising board threat lowers the best score, shrinks
the *absolute* window `0.15 × pbest`, and disqualifies tiles — which would make axis (a) (rises
with threat) and axis (b) (falls with it) anti-correlated, their sum near-conserved, and the
decision-density gate structurally unreachable. Two waves (X1, W4) had already missed against it.

**BASE COMMIT OF EVERY NUMBER BELOW: `d350416`** (PROGRAM RESONANCE milestone 2 — the
integration tip, with X2's ladder already merged), plus this wave's own instrument change, which
is proven gameplay-inert in §4. Raw chunks: `docs/measurements/tb/` (README there gives the exact
command lines and the `runs=10` assertions).

> **⚠ THIS WAVE CHANGED THE INSTRUMENT. Every `ch/turn`, `ch/ARMED`, `target-choices/ARMED` and
> `position-choices/ARMED` number published anywhere in this repo before today — FUL-13, X1, W4,
> X2, and the `[choice-split]` blocks in every archived report — was measured on the
> MULTIPLICATIVE instrument and is NOT comparable to any number measured after it.** The
> historical write-ups are left exactly as they are (they are provenance, and rewriting them
> would be worse than the mismatch); this note is the single place that says so.
> `SIGHTLINE_CHOICEBAND=mult` reproduces the old rule exactly if one ever has to be re-derived,
> and `SIGHTLINE_BANDTEST` pins that reproduction against a literal transcription of the old
> method. **`docs/DEVLOG.md` §W4, §X1 and §X2 and `docs/ROADMAP.md`'s W4/X2 sections all carry
> pre-wave `ch/ARMED` figures. Read them as history, not as a baseline.**

## 0. MEASURE THE OLD INSTRUMENT FIRST — and the spec's premise turns out to be FALSE

Before changing a line I built `SIGHTLINE_BANDPROBE=1` (`ChoiceProbe` in `Game.Autopilot.cs`):
a default-off, allocation-free, read-only probe that records, over real balance batches, the
distribution of the score each axis bands against, the distribution of every candidate's GAP to
it, and how many candidates a multiplicative window and four additive ones would each admit over
the identical sample. It is the answer to "is 3 the right constant, or 2, or 5?" — the brief's
first question, and one nobody had ever asked of this metric.

**The best safety score `pbest`, over real armed soldier-turns (slot base 50, n=10 campaigns per
rung):**

| rung | soldier-turns | mean | p5 | p25 | **p50** | p75 | p95 |
|---|---|---|---|---|---|---|---|
| heat 0 | 414 | 37.69 | 24 | 32 | **40** | 40 | 45 |
| heat 4 | 306 | 38.59 | 32 | 40 | **40** | 40 | 45 |
| heat 8 | 128 | 38.82 | 32 | 40 | **40** | 40 | 50 |

**`pbest` does not fall with threat. It is pinned at a median of 40 at every rung** — confirmed
in all five measured chunks, and the load-bearing fact of this section. 40 is exactly
`24 + 2×8 − 0`: full (level-2) cover, zero exposure, ground level.

*(The table's MEAN also rises with heat — 37.69 / 38.59 / 38.82 — and the first version of this
section claimed that as a finding. It is not one: off the common base the same rungs read 35.40
at base 60 and 36.55 at base 70, a 3.2-point world-set swing against a 1.1-point "rise". Review
caught it; §1 of this write-up refuses exactly this kind of n=10 directional claim, so it is
withdrawn here rather than defended.)* The maximum of the safety score over one-action-reachable tiles is a
property of the **map grammar** — is there a high-cover tile within one move? — and this game's
arenas nearly always provide one. Threat does not lower the best tile; it lowers all the *other*
tiles. So the multiplicative window `0.15 × pbest` was **≈6 points wide at every rung**, and was
never shrinking with difficulty.

The second half of the premise fares no better. I expected the `pbest > 0` guard to be a
threat-coupled disqualifier that silently deletes the whole axis on a hot board. Measured, it
fires on **0.5% / 1.0% / 0.0%** of soldier-turns at heat 0 / 4 / 8. It is nearly inert.

**This is a legitimate and valuable outcome, and it is stated first because it reassigns the
blame.** The spec's *fix* is still right — banding by a fraction of a magnitude nobody chose is
the wrong shape, and §2 shows axis (a) is genuinely damaged by it — but the spec's *mechanism*
is not what is happening here, and a wave that shipped the fix while repeating the story would
have written a fourth over-claim into this file.

**The gap distribution, `pbest − s` over every candidate destination:**

| rung | candidate tiles | mean | p5 | p25 | p50 | p75 | p95 |
|---|---|---|---|---|---|---|---|
| heat 0 | 16,159 | 21.31 | 0 | 16 | 16 | 27 | 54 |
| heat 4 | 12,726 | 22.45 | 0 | 16 | 16 | 27 | 60 |
| heat 8 | 6,300 | 24.65 | 0 | 16 | 24 | 32 | 60 |

The gaps live on a **lattice** — 16 is two cover levels, 8 is one, 5 is an elevation step, 6-10 is
one exposed gun — which is why the median sits on 16 and barely moves. This is the fact that makes
an additive band expressible at all: the score has natural units, so a band can be stated in them.

## 1. WHAT ACTUALLY FLATTENS AXIS (b): the anti-inflation CAP, not the window

The shipped contribution of axis (b) is `min(cap, admitted − 1)`, and the cap was **2**. Against
the measured admitted-count distribution that is not a tail-clip:

| rung | admitted/turn (additive band 3): p50 | p75 | p95 | mean |
|---|---|---|---|---|
| heat 0 | 2 | 4 | 15 | 3.64 |
| heat 4 | 3 | 5 | 9 | 3.40 |
| heat 8 | 2 | 4 | 7 | 2.99 |

**The arithmetic, exactly — because the first version of this section got it wrong and review
caught it.** The cap applies as `Math.Min(cap, admitted - 1)` inside `if (admitted >= 2)`. So
cap 2 first BINDS at `admitted >= 4` and cap 4 at `admitted >= 6`. **Neither clips the median
turn** (`admitted` 2-3) at any rung: cap 2 starts clipping around p70-p75 of the distribution,
cap 4 around p80-p88. The claim published here first — that cap 2 "sits between the p25 and the
p50" and "clips the median turn" — was **false**, and it was false in the shipped source comment
too. It is recorded rather than silently corrected because this wave's entire thesis is that
numbers get published and never re-checked.

What survives, and what review reproduced exactly: **cap 2 retains only 41-55% of the uncapped
signal** (1.082 of 2.640 at heat 0, 1.242 of 2.399 at heat 4, 1.094 of 1.992 at heat 8) where
**cap 4 retains 60-84%**. Losing half a metric's dynamic range to a clip that begins inside its
third quartile is too much compression for a number whose only job is to distinguish states. That
— not any claim about the median — is the argument for the raise, and it is a distribution
argument, not a "which cap moves the number most" one.

**The cap is kept and raised to 4**, which sits at the measured **p75-p80**: it preserves the
median and the interquartile body and clips only the tail, and retains **60-84%** of the signal.
The cap itself is right and worth defending — the tail is the open field, and a soldier with
fifteen identically-safe tiles around it is facing one shrug, not fourteen decisions. A cap at
the p75 clips shrugs; a cap at the p50 clips the metric.

**What I will NOT claim:** I priced caps 2 / 4 / uncapped against the heat rungs as well, and
that comparison resolves nothing. On slot bases 50/60/70 the capped-4 axis read 1.59 / 1.86 /
2.11 (monotone, tidy); re-run on a **common** base 50 the same three rungs read 1.59 / 1.84 /
1.66 (not monotone). A ±0.4 swing from the world set alone at n=10 is larger than every rung
difference in the table. The cap value is chosen from the **distribution**, which n=10 measures
precisely (12,726 candidate tiles in the heat-4 chunk alone), and not from a rung ordering, which
n=10 cannot measure at all. That distinction is the entire lesson of X2's ±SE section and it
applies here with more force, not less.

## 2. AXIS (a) NEEDED THE ADDITIVE BAND TOO, on its own evidence

The brief asked for a decision on axis (a) with evidence rather than a guess. Here it is: the
best shot value spans **p5 = 2-4 to p95 = 20-25** with a median of 11-13, an eight-fold range. A
12% window is therefore **0.36 points wide against a weak best shot and 3.0 wide against a strong
one** — it widens exactly when one target is obviously the right one (a finisher, a flank), which
is backwards for a metric asking "did this soldier face a real choice of target?".

The gap to the best shot is bimodal (p50 = 0-1, p75 = 4-5, p95 = 9-13): a rival is either a
near-duplicate or a different proposition, because the finisher and flank bonuses in `ShotValue`
are 4-14 points. **`ShotBand = 2`** admits the duplicates and not the different propositions, and
lands close to the old window at the *median* best (0.12 × 11 ≈ 1.3), so the axis-(a) level barely
moves. The point of changing it is not to move it — it is that both axes now band on the same
terms, in the units their scores are made of.

## 3. WHAT SHIPPED

**1. The additive band, default on** (`Game.Autopilot.cs`). One factored-out rule,
`AdmitNearBest(vals, best, mult, frac, band)`, used by both axes, so they provably cannot drift
apart again. Constants, all `static readonly` rather than `const` **so BANDTEST's pins are not
folded away by the compiler**:

| constant | value | in the units of |
|---|---|---|
| `ShotBand` | 2 | shot-value points (a finisher bonus is 4-14) |
| `PosBand` | 3 | safety points (cover 8, elevation 5, one exposed gun 6-10) |
| `PosChoiceCap` | 4 | near-best destinations, at the p75-p80 of the measured distribution |
| `MultShotFrac` / `MultPosFrac` | 0.88 / 0.85 | the pre-wave windows, kept for reproduction |

**2. `SIGHTLINE_CHOICEBAND=mult`** restores the pre-wave rule *exactly* — both windows, the
`pbest > 0` guard and the cap of 2 — so any archived number can be re-derived on any future tree.

**3. The `pbest > 0` guard is dropped in additive mode and kept in mult mode.** It exists because
`0.85 × pbest` is **degenerate** at a non-positive best: for `pbest < 0` the cut sits *above*
`pbest`, so nothing at all qualifies. The additive band has no such degeneracy. Measured, the
guard is nearly inert (§0), so this is not what moves the number; it goes because it is a
threat-coupled disqualifier that deletes the axis exactly when the board is worst.

**4. `SIGHTLINE_BANDPROBE=1`** — the instrument-design probe, kept (default off, allocation-free,
read-only, reachable only from the balance batch). It is how the next person re-derives a
constant instead of arguing about one.

**5. `SIGHTLINE_BANDTEST=1`**, wired into `scripts/qa-sweep.sh`. Its footer count is re-derived —
and while re-deriving it I found the *recipe* wrong too: the "DERIVED" grep in the sweep's header
matched only `...TEST|FUL11PROBE`, so it never counted `AUDIOGATE`, and it counted over
`qa-sweep.sh` while the label said "exist". Both halves now derive from the right place (`src/*.cs`
for "exist", this file for "run") and the header says they must be EQUAL; the honest numbers are
**51 exist, 51 run with `--full`, 50 without** (the one skipped is PAIRTEST), not the 49/47/46 the
footer had been carrying.
It pins, in order: the five constants; the RULE on the pure helper — the additive band is
invariant under a uniform shift of the score scale and the multiplicative one is not, and the
multiplicative one is degenerate at a non-positive best; that `CHOICEBAND=mult` still reproduces
the PRE-WAVE counts **exactly**, checked field-by-field against a literal transcription of the old
method (`OldChoiceReference`) over **120 constructed boards**; that the band never moves the shot
GATE (`acting` / `armed` / `losTargets` are identical in both modes) and that the two halves sum
to the total by construction; that the counter **mutates no game state** (a full board+unit
fingerprint before and after); and that it takes **zero `Util.Rng` draws** — the last with a
**sensitivity probe**: the same detector, handed the same body plus one hand-injected draw, must
FAIL, so a green purity result can never be the detector failing to detect.

**6. POST-REVIEW HARDENING.** Review broke the shipped rule three ways that all still printed
`BANDTEST: PASS`, because the test pinned each constant's VALUE and the mult-mode reproduction but
**no additive-mode behaviour at all** — and additive is the rule every future archived number will
be measured with. Fixed by pinning the USE, not the constant:
* `ChoiceReference(...)` is now a **parameterised** literal transcription, and the shipped counter
  must equal it field-for-field in BOTH modes over the 120 fixed-seed scenes. Every knob is also
  shown NON-VACUOUS (swap it in the reference and the answer must move on >= 10 scenes), because a
  pin that never differs from its variant cannot fail.
* A **golden FNV-1a fingerprint** over the 120 add-mode `(total, tgt, pos)` triples, as a second
  anchor that also catches the reference being edited in lockstep with a broken implementation.
* A **negative-safety board** (`BuildNegativeSafetyScene`): one soldier ringed by eight guns on an
  empty map, every reachable tile scoring about −24. The 120 random scenes never produce
  `pbest <= 0`, which is precisely why reinstating the dropped guard had been invisible. Here the
  additive rule must contribute the capped **4** and the pre-wave rule exactly **0**.
* A **behavioural cap pin** on that board: 48 near-best destinations on offer, contribution must be
  exactly `PosChoiceCap`. A hardcoded `Math.Min(2, ...)` at the use site now fails.
* `ShotValue > 0` asserted over 200+ real shots — the invariant the axis-(a) zero floor rests on.

**7. THE ARCHIVE SPLIT, ENFORCED IN DATA.** The banner was correctly called weak, so it is no
longer the mitigation. `SIGHTLINE_CHOICEBAND` is STRICTLY parsed — anything but `mult`/`add`/unset
makes the binary refuse to start with exit 2, where before a typo silently selected the NEW rule —
every `SIGHTLINE_BALANCE` aggregate carries an `instrument` field (`"mult-v1"`/`"add-v2"`), and
`diff_chunks.py` REFUSES a cross-instrument diff unless given `--cross`.

**8. Axis (a)'s cut is floored at zero** (`floorAtZero`, axis (a) only — axis (b) must stay signed
or the dropped guard comes back). Honest scope: this is arithmetically a **no-op** on everything
measured, because every `ShotValue` is strictly positive so `[best-2, 0)` is empty of candidates —
the `v2-*` chunks confirm it, field-identical to the chunks they replicate. It does **not** fix the
underlying wart (at `best = 1.5` the band is still 133% of the best); that is now a ROADMAP spec
for its own round rather than a third lever in this one.

## 4. THE INERTNESS PROOF — the change is bookkeeping and nothing else

`CountMeaningfulChoices` is read-only telemetry; if the band change moved anything in the game,
the instrument would be measuring its own footprint. `diff_chunks.py` flattens two
`SIGHTLINE_BALANCE` aggregates to dotted scalar paths and reports every field that differs.
Paired `mult` vs `add` batches, each pair on the **same slot base**:

| pair | fields compared | choice fields moved | **non-choice fields moved** | verdict |
|---|---|---|---|---|
| heat 0, base 50 | 686 | 10 | **0** | INERT |
| heat 4, base 50 | 662 | 12 | **0** | INERT |
| heat 8, base 50 | 600 | 10 | **0** | INERT |
| heat 4, base 60 | 641 | 10 | **0** | INERT |
| heat 8, base 70 | 642 | 12 | **0** | INERT |

The field and choice-field counts differ **legitimately** between pairs: `byDeploy`, `byObjective`
and `byArena` are arrays whose length depends on what the batch actually dealt, so a shorter batch
flattens to fewer dotted paths and carries fewer `byDeploy` choice mirrors. Only the last column
is a constant, and it is the one that matters.

> **Review caught this table as originally published quoting `686 / 10` on all five rows** — the
> heat-0 pair's numbers, copied down. The verdict column was right and the conclusion was right,
> but four of the five MEASURED cells were not measured. In a wave whose whole thesis is that
> nine programs shipped numbers nobody re-checked, that is the exact failure, committed by the
> wave that was complaining about it. Recorded here rather than quietly corrected.

Five pairs across three world sets, because the first three were run before it was noticed that
the *rung* comparison in §5 needs a common slot base; the extra world sets are free extra
confirmations of inertness.

Run completion, missions, `runWinRate`, `byObjective`, `byMission`, `byHeat`, `lossCauses`,
`actionMix`, `armedSoldiersPerTurn`, `losTargetsPerArmedSoldierTurn` and every per-slot paired
record are **byte-identical**. The only fields that move are the four `decisionRichness` choice
fields plus their six `byDeploy` mirrors. `SIGHTLINE_PAIRTEST=1` is **PASS** (h0 slot0 and h4
slot1 both MATCH).

## 5. THE RE-BASELINE — the new instrument, and whether it separates the rungs

`SIGHTLINE_BALANCE=5` per chunk (**10 campaigns**, `runs=10` asserted in all ten `add-*`/`mult-*`
chunks; the two exploratory `probe-*` chunks were `SIGHTLINE_BALANCE=3`, `runs=6` asserted), common
slot base 50, so the three rungs replay the same worlds and the two rules replay them identically.
**These are instrument reads, not a ladder.** A run-completion figure at n=10 carries roughly ±15
points; nothing here supersedes X2's ladder or should ever be quoted as a rung.

| rung | instrument | run compl (n=10) | **ch/ARMED** | tgt/ARMED | pos/ARMED | armed/turn | los/ARMED |
|---|---|---|---|---|---|---|---|
| heat 0 | mult (pre-wave) | 40.0% | 1.926 | 0.724 | 1.202 | 1.564 | 2.741 |
| heat 0 | **add (shipped)** | 40.0% | **2.389** | 0.815 | 1.574 | 1.564 | 2.741 |
| heat 4 | mult (pre-wave) | 20.0% | 1.765 | 0.446 | 1.319 | 1.236 | 2.785 |
| heat 4 | **add (shipped)** | 20.0% | **2.307** | 0.534 | 1.773 | 1.236 | 2.785 |
| heat 8 | mult (pre-wave) | 0.0% | 1.393 | 0.119 | 1.274 | 0.683 | 1.750 |
| heat 8 | **add (shipped)** | 0.0% | **1.976** | 0.179 | 1.798 | 0.683 | 1.750 |

`armed/turn` and `los/ARMED` are identical down the pairs — the band moved the near-best count and
nothing else, exactly as §4 requires.

**The paired mult → add lift, which carries NO sampling error** (identical worlds, identical play):

| rung | ch/ARMED | tgt/ARMED | pos/ARMED |
|---|---|---|---|
| heat 0 | +0.463 | +0.091 | +0.372 |
| heat 4 | +0.542 | +0.088 | +0.454 |
| heat 8 | +0.583 | +0.060 | +0.524 |

The lift grows monotonically with heat and almost all of it is axis (b). **The first version of
this section then attributed it to the BAND — "the old instrument really was under-reading the
positioning decision on a dangerous board... by an amount that grows with the danger". That is
refuted by this wave's own probe, and review caught it.** It is corrected here in full, because
attributing a lever's effect to the wrong lever is the same class of error the wave was formed to
fix.

Nothing in the original decomposition was measurable, because the two halves were quoted against
two different denominators (the probe's `PosTurns` vs `Stats`' `ArmedSoldierTurns`). The probe now
accumulates the **full 2x2 — {mult window, additive band} x {cap 2, cap 4} — over the same
soldier-turns**, so the split is exact. Re-measured on `runbin/tb3` (chunks `v2-add-h0/h4/h8`,
common base 50, field-identical to the `add-*` chunks they replicate):

| rung | mult+cap2 (PRE-WAVE) | add+cap2 | mult+cap4 | add+cap4 (SHIPPED) | **band effect** @cap2 | **cap effect** @add |
|---|---|---|---|---|---|---|
| heat 0 | 1.232 | 1.082 | 1.841 | 1.592 | **−0.150** | **+0.510** |
| heat 4 | 1.340 | 1.242 | 2.052 | 1.843 | **−0.098** | **+0.601** |
| heat 8 | 1.234 | 1.094 | 1.883 | 1.664 | **−0.141** | **+0.570** |

**The additive band ALONE admits strictly FEWER near-best destinations at every rung, at either
cap.** It is narrower than the window it replaced (3 points against the multiplicative window's
effective ~6), so of course it is: `mult.85` admits 3.97 / 3.75 / 3.24 per turn against `add3`'s
3.64 / 3.40 / 2.99. **Every point of the lift, and more, is the cap raise.** The band's
contribution to the headline number is negative.

That also disposes of the monotonicity: `pos/ARMED` being monotone in heat under the new rule
(1.574 → 1.773 → 1.798) where the old one wandered (1.202 → 1.319 → 1.274) is **a coincidence of
two non-monotone components** — a band effect of −0.150 / −0.098 / −0.141 and a cap effect of
+0.510 / +0.601 / +0.570, neither of which is monotone — not evidence that either lever tracks
threat. At n=10 it should not be read as a property of the instrument at all.

What the band change *is* defensible on stands unchanged and is not a number: the window is now
stated in the units the score is made of instead of as a fraction of a magnitude nobody chose, it
cannot be deflated by the score's absolute level (BANDTEST pins that invariance), and it is not
degenerate at a non-positive best. Those are reasons to prefer it. **"It raised the number" is
not one of them, and this wave will not claim it.**

**Does the new instrument separate the rungs where the old one did not?** **No — and it makes the conservation WORSE.** Across heats 0/4/8 on the common base, the
old instrument's `ch/ARMED` spread is **0.533** (31.5% of its mean) and the new one's is **0.413**
(18.6%). The new band separates the rungs *less*.

The reason is the wave's second correction to the record, and it is bigger than the first:

| axis | mult h0 / h4 / h8 | add h0 / h4 / h8 |
|---|---|---|
| tgt/ARMED ("which target?") | 0.724 / 0.446 / **0.119** | 0.815 / 0.534 / **0.179** |
| pos/ARMED ("where do I stand?") | 1.202 / 1.319 / 1.274 | 1.574 / 1.773 / **1.798** |

**Axis (a) does not rise with threat on the heat axis — it collapses**, by 78-84% from heat 0 to
heat 8, tracking `los/ARMED` (2.74 → 2.79 → 1.75) and `armed/turn` (1.56 → 1.24 → 0.68). Heat does
not put more guns in front of a soldier; it kills the soldiers and shortens the fight, so fewer of
them are in contact at all and each sees fewer targets. Meanwhile axis (b) rises. The two halves
are still anti-correlated — with the **opposite signs** to the ones W4 named — and because the
additive band amplifies axis (b)'s rise, the sum is *more* conserved than before, not less.

So the standing conclusion stands, on new evidence and for a new reason: **a decision-density gate
stated on `ch/ARMED` is structurally hard, and no band shape fixes it.** It is hard because the
two axes are driven in opposite directions by the same variable (contact breadth), and the metric
adds them. If a future wave wants a gate it can move, it should gate the two halves *separately*
— `target-choices/ARMED` and `position-choices/ARMED` are both shipped and both move a lot — or
gate `meaningful-choices/turn`, whose other factor (`armed-soldiers/turn`, 1.56 → 0.68 here) W4
already showed is very movable.

**All of this is n=10 per rung and the rung spreads are not resolvable at that N** (§7.1). The
paired lift table above *is* exact; the spread comparison is a direction, not a measurement.

### THE GOALPOST — this wave crosses two published gates, and they are VOID, not met

Said plainly, because the next wave will otherwise read the table above as a pass. On the new
instrument at heat 0 this tree reads **`ch/ARMED` 2.389** and **`meaningful-choices/turn` 3.738**
(both straight out of `add-h0.json`). W4 published exactly those two as its decision-density
gates — `ch/ARMED >= 2.00` and `meaningful-choices/turn >= 3.00` — and recorded both as **MISSED**
at 1.53 and 2.36 (§W4, THE GATES). Read across the change, this wave clears both, one of them by
25%.

**It is not a pass. Both gates are VOID.** Their thresholds were chosen against the multiplicative
instrument — a number produced by a different rule with a cap that discarded 45-59% of axis (b) —
so "2.00" and "3.00" name quantities that no longer exist. A threshold survives an instrument
change only if someone restates it on the new instrument and argues the new value, and nobody has.
**No wave may claim these gates until they are restated**, and restating them is a judgement call
(what *is* a rich turn?) that wants its own round, not a footnote in the wave that moved the ruler.

This is also the sharpest illustration of the archive split in the banner at the top: the two
numbers sit 25% and 58% above their old thresholds purely because the ruler changed, and nothing
about the game moved at all — §4 proves that to the field.

## 6. VERIFICATION

* `dotnet build -c Release` — **0 warnings / 0 errors**.
* `bash scripts/qa-sweep.sh --full` — **all 51 self-tests PASS** (BANDTEST among them), COVERAGE
  GAP block empty, PAIRTEST PASS, autoplay x3 — no TIMEOUT, no blank line, no exception. Re-run
  end to end on the post-review tree: **51/51 PASS, COVERAGE GAP empty, PAIRTEST PASS**, autoplay
  `LOSE mission=3 / WIN mission=6 / LOSE mission=3`. **The autoplay results are a RECORD, not a
  pin**: autoplay is clock-seeded, so they vary by design (the pre-review sweep read
  `WIN / LOSE / WIN`) and only "no TIMEOUT, no exception" is the contract (CLAUDE.md). Do not treat
  any triple here as a regression baseline.
* `SIGHTLINE_PAIRTEST=1` — **PASS**, both legs MATCH.
* `SIGHTLINE_BANDTEST=1` — **PASS** (`boards=120 bandMoved=45 posMoved=40 posBand=3 shotBand=2
  cap=4`).
* **BANDTEST proven to fail on the pre-change behaviour.** Two one-line reversions of the
  production code, rebuilt and re-run:
  * `AdmitNearBest`'s cut forced back to `best * frac` (no additive band):
    `BANDTEST: FAIL additiveNotShiftInvariant:3/3/2, additiveBrokenAtNegativeBest`
  * that plus `PosChoiceCap` back to 2 (the full pre-wave rule):
    `BANDTEST: FAIL posChoiceCap=2, additiveNotShiftInvariant:3/3/2,
    additiveBrokenAtNegativeBest, bandDialInert:0/120, posAxisDialInert:0/120`

  `additiveNotShiftInvariant:3/3/2` is the defect itself, printed: the same board structure, shifted
  down 20 points on the score scale, reads as strictly fewer choices under the old rule.
* **The three breaks review found, each re-run against the HARDENED test** (they all printed PASS
  before it):
  * (a) `Math.Min(2, pComparable - 1)` hardcoded at the use site while `PosChoiceCap` still reads 4
    → `FAIL scene0:addNotNewRule got(2,2,2,4,0,2) want(4,2,2,4,0,4)` +11 more scenes.
  * (b) `PosBand` passed where `ShotBand` belongs on axis (a)
    → `FAIL scene0:addNotNewRule got(5,2,2,4,1,4) want(4,2,2,4,0,4)` +11 more scenes.
  * (c) the `pbest > 0` guard reinstated in additive mode
    → `FAIL negSceneAddPos=0 want 4 (uncapped 48)` — caught by the new negative-safety board,
    which is the only construction in the suite that reaches `pbest <= 0` at all.
* **The post-review source changes re-measured and proven inert**: `v2-add-h0/h4/h8` on
  `runbin/tb3` vs the `add-h0`/`add-h4b`/`add-h8b` chunks they replicate — **686 / 662 / 600 fields
  compared, 0 moved, including the choice fields.** The axis-(a) zero floor, the `AdmitNearBest`
  extraction, the `const` -> `static readonly` change, the strict `CHOICEBAND` parse and the probe's
  2x2 accumulators together change nothing measurable, so every `add-*` number above stands.
* `SIGHTLINE_CHOICEBAND=MULT` (a deliberate typo) → the binary prints `unknown value 'MULT' ...
  Refusing to run rather than guess which instrument you meant` and exits **2**.
* No screenshots — this wave changes no pixel. `CountMeaningfulChoices` is called only from
  `SmartStep` under `Stats.Enabled`, i.e. only inside the balance harness; interactive play never
  reaches it.

## 7. WHAT I DID NOT FIX, AND WHAT IT COST

1. **The rung-separation question is NOT answered, and n=10 cannot answer it.** The wave's
   headline deliverable was supposed to be "does the new instrument separate the rungs?", and the
   honest answer is that at n=10 per rung the world set moves the metric by ±0.4 — more than any
   rung difference on the table. The paired mult→add delta *is* exact (identical worlds, identical
   play), so the instrument comparison stands; the rung comparison does not. **Anyone who wants
   the rung answer must re-run this at n≥40** — 4x the campaigns across six rung/instrument
   cells, i.e. ~24 chunks, several hours of wall time on a container this contended. I chose to
   spend the wave's runtime on the distributions (which n=10 measures precisely — 16,159 candidate
   tiles at heat 0) rather than on rungs it could not resolve.
2. **The archive is now split across two instruments and I did not migrate it.** Every
   `ch/ARMED` in this file older than today is a multiplicative number. Re-measuring them would
   cost more machine time than the whole program has spent on decision density, and rewriting
   them in place would destroy provenance. The banner at the top of this section is the entire
   mitigation, and it is a weak one: someone will quote 1.55 next to 2.39 and draw a conclusion.
3. **The two band constants are NOT equally evidenced, and only one of them is even a choice.**
   * **`ShotBand = 2` is load-bearing and "defensible, not derived" is the right description of
     it**: the admitted count moves at every step — add1 / add2 / add3 = **1.529 / 1.756 / 1.865**
     per armed soldier-turn at heat 0. The distributions rule out 1 (excludes near-duplicates at
     the p50 gap of 0-1) and 4+ (admits a whole finisher bonus), but 2 versus 3 is a judgement.
   * **`PosBand = 3` is NOT load-bearing at all**, and the first write-up implied it was. add2 /
     add3 / add4 = **3.56 / 3.64 / 3.64** at heat 0: 3 and 4 are *identical*, because the safety
     score lives on a lattice (cover 8, elevation 5, a gun 6-10) with nothing in the gap between
     3 and 4. The band only changes behaviour at 2 (drops the gap-3 tiles) and at 5 (add5 jumps
     to 4.49 as the elevation step enters). So the honest statement is that the evidence pins
     `PosBand` to the **interval [3, 5)** and any value in it is the same instrument; 3 is the
     bottom of that interval, chosen for the "same tactical class" reading, not measured against 4.
4. **The near-invariance claim itself was never re-tested at power.** W4's "1.55-1.64 across five
   levers" and X2's "1.44-1.78 across six rungs" are the reason this wave exists, and both were
   measured on the old instrument at n=40 and n=40. I re-measured the *mechanism* they blamed and
   found it false; I did **not** re-measure the *phenomenon* at n=40 on the new instrument. §5's
   n=10 read says the sum is *more* conserved under the new band, not less, and gives a new reason
   for it (the two axes are driven in opposite directions by contact breadth) — but that is a
   direction at n=10, not a measurement, and it is the single most important thing left open here.
5. **I shipped TWO levers in one wave, against CLAUDE.md's "one lever per round" contract.**
   The ROADMAP spec asked for the additive BAND. I also raised the CAP from 2 to 4, in the same
   commit, with no round separating them — and §5's decomposition shows the cap is the half that
   moves the number while the band alone moves it slightly the *other* way. Had they been separate
   rounds, the goalpost question above would not even arise: the band round would have landed at
   roughly the old level and the cap round would have been argued on its own merits and its own
   evidence. The mitigating facts, for what they are worth: the contract is written for BALANCE
   levers and this wave shipped no gameplay lever at all (§4 proves it to the field), and both
   halves are separable after the fact through `SIGHTLINE_CHOICEBAND` plus the probe's 2x2
   decomposition. They do not make it the right call. It was one commit that should have been two
   rounds, and review was right to name it.
6. **I did not touch `SafetyAt` itself**, and it deserves the same scrutiny the band just got: it
   consults only the **nearest** foe for cover, it does not consider whether a destination keeps a
   shot, and its `24` base is arbitrary. Any of those could matter more than the band. Out of
   scope for a wave whose whole point was to change one thing and prove it inert.

# PROGRAM CROSSCUT — L1 "THE LADDER OF RECORD AT n=80" (2026-08-29, lead)

**Base commit `636112c`** (documentation-only on top of `d350416`, RESONANCE milestone 2).
Raw data, method and the full tables: `docs/measurements/l1/README.md`.

## The charter

X2 closed by naming its own successor: *"The single highest-value thing the next wave can do is
not another lever — it is n>=80 per rung on the state that is already shipped. Everything else in
this program is built on a measurement whose error bar is the size of the answer."* Nobody had
done it. This round did: 24 CRN chunks, 6 rungs x 4 disjoint slot sets x greedy+sloppy,
**480 campaigns**, `runs=20` asserted in every chunk, run from a snapshot binary so the tree could
keep building. No lever was spent. Nothing in the game changed.

## THE LADDER

| rung | L1 (n=80) | +-SE | X2 (n=40) | band | verdict | step |
|---|---|---|---|---|---|---|
| RECRUIT | **73.8** | 4.9 | 75.0 | 75 +-8 | in band | - |
| heat 0 | **48.8** | 5.6 | 57.5 | 55 +-8 | in band | -25.0 |
| heat 2 | **33.8** | 5.3 | 35.0 | 40 +-8 | in band | -15.0 |
| heat 4 | **21.2** | 4.6 | 30.0 | 30 +-8 | BELOW by 0.8 | -12.5 |
| heat 6 | **17.5** | 4.2 | 20.0 | 20 +-8 | in band | **-3.8** |
| heat 8 | **10.0** | 3.4 | 17.5 | 10 +-5 | in band, on target | -7.5 |

**This table supersedes X2's**, which supersedes X1's, W5's, W4's and FUL-13's. Halving the error
bar bought three things no lever could have:

1. **The ladder is monotone at every step, first try.** X2 recorded that "rung ORDER is not
   resolvable at n=40" and that two waves had argued over inversions no data could settle. There
   are none.
2. **Heat 8 lands on its published target to the decimal.** X2 measured it **out of band at 17.5%**
   and left it as a standing ROADMAP defect with a named cause ("the apex is a wall made of four
   objectives"). It is 10.0%. **The defect was the measurement.** Every hour a future wave would
   have spent tuning the apex is saved.
3. **The middle of the ladder is flat and the top is a cliff.** Steps: -25.0, -15.0, -12.5,
   **-3.8**, -7.5. Heat 5 and 6 together buy 3.8 points. `Heat.Mods` explains it exactly: rung 8
   is the ONLY entry carrying `DmgDelta` or `AiTier`, so the middle rungs add bodies and stats
   while only the apex changes KIND. That is now a measured shape, not a suspicion.

heat 4's miss is 0.8 points under the floor at 1.9 sigma from the band centre. Recorded, not
repaired: it is a boundary case and repairing it inside a measurement round would be exactly the
mistake this round exists to stop.

## THE FINDING: twenty worlds have been standing in for the game

Every wave since W2 has measured on `SIGHTLINE_BALANCE_BASE` **0 and 10**, and only those. This
round added bases 20 and 30, which had never been run. Pooled over all six rungs the old sets
read **92/240 = 38.3%** and the new ones **72/240 = 30.0%** - a **+8.3 point** gap, SE 4.3,
**z = 1.93, two-sided p = 0.053**, 5 of 6 rungs positive. At heat 0 it is 60.0 vs 37.5; at heat 8,
17.5 vs 2.5.

**Stated honestly: p = 0.053 is not established, and this write-up does not claim it is.** But the
mechanism is not mysterious - `Util.Reseed(50000 + slot)` makes slot *i* a fixed world forever, so
twenty fixed worlds became the operational definition of "the game" and every lever since W2 was
priced against them. The response is a method rule, which costs nothing:

> **No wave may measure a ladder on bases 0 and 10 alone.** A rung is four slot sets or it is not
> a rung. A wave that can only afford 40 campaigns must draw its two sets from a rotating pool and
> must say which two it drew.

## THE TREE HAS NOT DRIFTED (and two waves' inertness claims are confirmed)

On X2's own slot set (bases 0+10, n=40, directly comparable to its archived S1 chunks): RECRUIT
72.5 vs 75.0, heat 0 60.0 vs 57.5, heat 2 35.0 vs 35.0 - **within 2.5 points at every rung.**
A3 AUDITION and R2 QA FIXES both claimed to be gameplay-inert against their own batches; neither
checked the ladder. They were right, and now it is on the record.

## DECAPITATE IS THE WORST OBJECTIVE IN THE GAME

Pooled over all 480 campaigns:

| objective | n | win% | +-SE | turns |
|---|---|---|---|---|
| Escort | 156 | 83.3 | 3.0 | **10.84** |
| Evac | 56 | 78.6 | 5.5 | **10.45** |
| Defend | 368 | 81.5 | 2.0 | 8.73 |
| **Decapitate** | **326** | **64.1** | **2.7** | 4.79 |
| Rescue | 110 | 85.5 | 3.4 | 4.68 |
| Hack | 130 | 88.5 | 2.8 | 3.68 |
| Eliminate | 536 | 92.5 | 1.1 | 3.66 |
| Sabotage | 115 | 85.2 | 3.3 | 3.64 |

### ...but the pooled row is misleading, and the decomposition is the real finding

`Run.cs:556-558` sets exactly one Boss node, always the map's last, and `CardForNode` makes the
Boss **always Decapitate**. So all 234 mission-6 attempts are Decapitates and the objective's row
pools the campaign finale with the mid-run ones. Split:

| Decapitate | n | win% | +-SE |
|---|---|---|---|
| finale (BOSS node, m6) | 234 | **70.1** | 3.0 |
| **mid-run (every other node)** | **92** | **48.9** | **5.2** |

**A mid-run Decapitate is 21 points HARDER than the campaign's climactic boss fight**, and at
48.9% it is the worst mission of any kind in the game against a per-mission average near 80%.

**I published the pooled 64.1% first and it was the same error this round exists to catch** - the
survivorship/composition trap X2 found in Escort's 8.03t, made by the person who had just written
it up. It is corrected here rather than quietly amended: the pooled row is real but it is not the
finding.

The mechanism is named in `Game.DesignateHvt` (src/Game.cs:2059) by its own comment. On the Boss
node the HVT is the WARLORD, an ELITE, and the buff is deliberately skipped: *"an ELITE is ALREADY
a tuned boss - double-buffing it would re-create the stat-check wall we're removing."* On every
OTHER Decapitate the HVT is the toughest rank-and-file body and takes `+6 + mission` HP (+8 to +11
at missions 2-5) **and** +6 aim, on top of X1's `HostileToughness` +3 that every hostile carries.
**The stat-check wall was removed from the boss and left in the mid-run case.** That is a
hypothesis with a named mechanism, not a proven cause - it needs its own CRN-paired round - but it
is specific, it is one expression to test, and nothing in this project has ever looked at it.

The ROADMAP names *Escort* as "the drag objective"; Escort is 83.3%.

What is true of Escort and Evac is that they are **slow** - 10.84 and 10.45 turns against
Eliminate's 3.66 - not that they are lost. **Slow and lost are different defects with different
repairs, and the project has been conflating them for three waves.** Evac at n=56 is finally large
enough to read at all (X2 had it at n=4 per rung).

## WHAT THIS ROUND DID NOT DO

- **It repaired nothing.** No lever, no tuning constant, no game code. That is deliberate: the
  point was to find out what is true before spending anything.
- **The slot-set effect is not established at p=0.053** and the method rule is proposed on cost
  grounds (it is free) rather than on proof.
- **heat 4's 0.8-point miss is unexplained.** It could be the flat middle, it could be the boundary.
- **The decision-richness columns are on a superseded instrument.** Wave TRUE BAND re-specified
  `CountMeaningfulChoices` from multiplicative to additive windows; L1's `ch/ARMED` numbers are
  pre-TRUE-BAND and are not comparable to anything measured after it.
- **THE STANDING WARNING.** Wave W1 TRUE INSTRUMENT severs `src/Fx.cs`'s shake jitter from the
  shared gameplay `Util.Rng` - today `Fx.Update` draws `Util.RandF()` once per RENDERED FRAME, so
  the dice are a function of the frame count, and screen shake is a shipped comfort toggle, which
  means a player who turns it off is playing different dice. That repair **invalidates every CRN
  world in `docs/measurements/l1/`**. L1 is therefore the **pre-repair ladder** and the only n>=80
  picture of the pre-repair tree that will ever exist.

---

# PROGRAM CROSSCUT — L2 "THE POST-REPAIR LADDER AT n=160" (2026-08-29, lead)

**Base commit `4784803`** (the integration tip carrying W1 TRUE INSTRUMENT + TRUE BAND).
960 campaigns. 6 rungs x EIGHT disjoint slot sets x greedy+sloppy = **160 per rung**, all 48
chunks `OK runs=20` and re-asserted from the JSON afterwards. Full tables:
`docs/measurements/l2/README.md`.

W1 re-rolled every CRN world in the project, so L1 and everything before it are formally
incomparable to this table. W1 also made the instrument ~20x faster, so this round cost minutes.

| rung | L2 (n=160) | +-SE | L1 (n=80) | delta | band | step |
|---|---|---|---|---|---|---|
| RECRUIT | **72.5** | 3.5 | 73.8 | -1.3 | 75+-8 | - |
| heat 0 | **46.9** | 3.9 | 48.8 | -1.9 | 55+-8 | -25.6 |
| heat 2 | **36.9** | 3.8 | 33.8 | +3.1 | 40+-8 | -10.0 |
| heat 4 | **23.1** | 3.3 | 21.2 | +1.9 | 30+-8 | -13.8 |
| heat 6 | **20.6** | 3.2 | 17.5 | +3.1 | 20+-8 | **-2.5** |
| heat 8 | **8.8** | 2.2 | 10.0 | -1.2 | 10+-5 | -11.9 |

Monotone at every step; five of six in band; heat 0 sits 0.1 under its floor, which at SE 3.9 is
the floor.

## 1. Severing the frame-coupled dice did NOT change the difficulty

The largest per-rung move between L1 and L2 is **3.1 points** against a combined SE of ~5. W1's
repair changed WHICH worlds you get, not how hard they are. The archive it invalidated was
mis-INDEXED, not mis-CALIBRATED. That was not obvious in advance and is worth having on the record.

## 2. The flat middle REPLICATES on disjoint worlds

`h4 -> h6` is the smallest step on BOTH ladders: **-3.8 (L1) and -2.5 (L2)**, against neighbours of
-12.5/-13.8 and -7.5/-11.9. `Heat.Mods` explains it exactly — rung 8 is the ONLY entry carrying
`DmgDelta` or `AiTier`, so the middle rungs add bodies and stats and only the apex changes KIND.
This is now replicated, not suspected, and it is the clearest open balance target in the project.

## 3. L1's slot-set finding FAILED TO REPLICATE — RETRACTED

L1 reported the two slot sets every wave has used since W2 running **+8.3 points easier** than
fresh ones (z=1.93, p=0.053) and recorded it as "suggestive and NOT established". L2 tests the same
hypothesis with SIX fresh sets against those two over 960 campaigns: old 89/240 = 37.1%, new
245/720 = 34.0%, **+3.1 points, SE 3.6, z=0.85, p=0.394**, with three of six rungs now going the
other way.

**The effect is not there, and this entry retracts it** rather than leaving a p=0.053 to be quoted
by a future wave as though it were a result. This is exactly the outcome L1's hedge existed to
permit, and the hedge is why the retraction costs nothing.

**The method rule stays** — a rung is four slot sets or it is not a rung — but it is now justified
on COST (free insurance against a world-set artefact) rather than on evidence of one.

## 4. The mid-run Decapitate REPLICATES and strengthens

| | L1 | L2 |
|---|---|---|
| finale (BOSS node, m6) | 70.1% (n=234) | **69.7%** (n=479) |
| **mid-run Decapitate** | 48.9% +-5.2 (n=92) | **46.0% +-3.9** (n=163) |

**A mid-run Decapitate is 23.7 points harder than the campaign's climactic boss fight**, measured
twice on disjoint worlds. Pooled over 960 campaigns Decapitate is 63.7% +-1.9 (n=642) — the worst
objective in the game; the next worst is Sabotage at 83.5%. `Game.DesignateHvt` skips the buff for
an ELITE by its own comment ("double-buffing it would re-create the stat-check wall we're
removing") and applies `+6 + mission` HP and +6 aim everywhere else. **The wall was removed from
the boss and left in the mid-run case.** Still a hypothesis with a named mechanism; it is one
expression to test and it is now the best-evidenced open defect in the project.

## What this round did NOT do

- **No lever, no game code.** Same discipline as L1.
- **The decision-richness columns are on a NEW instrument** (TRUE BAND's additive band) and read
  2.0-2.6 against L1's 1.5-1.8. That is the instrument, not the game. Never compare them.
- **Three waves are still unmerged** (W4 board, W5 first hour, W9 repair) and W9 explicitly changes
  RNG draw order and composition. **This table will need re-running once they land** — it is the
  post-W1 ladder, not the final one.

# PROGRAM CROSSCUT — W8 "THE HALF WALL" (2026-08-30)

**Base commit `dfa7c0f`** — the integration tip carrying W1 TRUE INSTRUMENT, TRUE BAND and the
L1/L2 ladders. The wave's instrument is `ee5a85c`, its autopilot probe `95b127a`. Raw data,
method and every command line: `docs/measurements/w8/README.md`.

## The charter

L2 handed this wave a defect with a named mechanism: a **mid-run Decapitate wins 46.0% ±3.9
(n=163) against the boss finale's 69.7% (n=479)** — 23.7 points harder than the campaign's climax,
replicated across two disjoint world sets. The suspect was `Game.DesignateHvt`, which buffs the
punch-through target by `+6 + mission` HP and `+6` aim **unless it is an ELITE** — and the finale's
boss always is. The exemption's own comment says why: *"an ELITE is ALREADY a tuned boss —
double-buffing it would re-create the stat-check wall we're removing."* The wall was removed from
the boss and left in the mid-run case.

**The decomposition verified. The mechanism did not** — the buffed half of the mid-run population
turned out to be the EASIER half. A second probe asked how much of the gap belongs to the measuring
bot rather than the game, and returned a clean null: the bot's HVT-focus policy costs about nine
points on a Decapitate but costs them on BOTH sides, leaving the gap at 20.3 → 20.4. And the
cross-tab built to check all this found a larger defect of exactly the same shape one row above.
The wave therefore ships **an instrument, a self-test, four default-off dials (one of them
priced), and a finding** — and no balance change.

## 1. THE DECOMPOSITION VERIFIES — from data, not only from reading the code

`Run.GenerateMap` sets `Map[Map.Count-1].Kind = Boss` and the last column holds exactly one node;
`CardForNode` makes a Boss node **always** Decapitate. So mission 6 ⟺ Boss node ⟺ Decapitate, and
subtracting the mission-6 row from the Decapitate row recovers the mid-run half. That is the
reading. **The data says the same thing exactly**: pooled over L2's 48 archived chunks
(960 campaigns), `byNodeKind` Boss is **n=479, 334 wins** and `byMission` m6 is **n=479, 334 wins**
— identical counts, not merely identical rates. There is no mission 6 that is not a Boss node and
no Boss node that is not mission 6, in 960 campaigns. The subtraction is valid and the lead's
46.0% (n=163) is confirmed.

## 2. THE INSTRUMENT — and a 960-campaign inertness proof

`Stats` gained three read-only blocks, zero RNG draws:

- **`byObjectiveByNodeKind`** — the cross-tab the wave was chartered on. An objective's row can no
  longer pool a capstone with a mid-run node.
- **`byObjectiveByMission`** — the *other* mix a flat objective row hides, and the one that turned
  out to matter more. `Run.DeckObjective` deals a fight objective from a hash of the map **column**,
  and a column *is* a mission number, so every objective row carries a depth mix as well.
- **`hvt{}`** — the Decapitate target split by `DesignateHvt`'s ELITE exemption (buffed
  rank-and-file vs exempt named boss), per mission, with its average `MaxHp` and with the three
  buff magnitudes echoed so a chunk's JSON records the tree it was measured on.

Inertness, three ways. **(a)** `I1-pre` vs `I1-post`, the same three (rung, slot-set) chunks on the
pre-wave and instrumented binaries, 60 campaigns per arm: **42 of 43 aggregate fields byte-identical
on all three pairs**, the only mover being `harness{}` (timestamps and loadavg, designed to vary).
**(b)** `R0diag` vs `P0` — the same check for the §7 policy-dial binary at its default, so a probe
round may be paired against the lever round's baseline: **45 of 46 fields byte-identical**, again
only `harness{}`. That is CLAUDE.md's measurement contract, item 4, done rather than asserted.
**(c)** the strong one: round **B** re-ran L2's exact 48-chunk grid on the instrumented binary and
reproduced the archive **with zero differing rows** — every `byObjective`, `byNodeKind`, `byMission`
count and win total, and all six ladder rungs (72.5 / 46.9 / 36.9 / 23.1 / 20.6 / 8.8). **L2 is
therefore this wave's baseline, re-measured rather than quoted.**

## 3. THE NAMED MECHANISM IS NOT THE CAUSE

The cross-tab splits the 163 mid-run Decapitates by whether the HVT actually took the buff.
`DesignateHvt` picks the toughest non-special body — and on missions 3 and 5 that is the recurring
named **mid-boss** (`Mission.MakeMidBoss`, `Cls == "ELITE"`), so those are **exempt**. Missions 2
and 4 have no named elite, so their HVT is rank-and-file and **buffed**. Round B, n=960 campaigns:

| mid-run Decapitate | win% | ±SE | n | HVT MaxHp |
|---|---|---|---|---|
| m2 **BUFFED** | 63.6 | 10.3 | 22 | 17.5 |
| m4 **BUFFED** | 53.8 | 8.0 | 39 | 24.7 |
| m3 EXEMPT (mid-boss) | 38.3 | 7.1 | 47 | 23.0 |
| m5 EXEMPT (mid-boss) | 40.0 | 6.6 | 55 | 27.0 |
| **m6 EXEMPT (the finale)** | **69.7** | 2.1 | 479 | 22.4 |

Pooled, the **buffed** half of the mid-run population reads **57.4% (n=61)** and the **exempt** half
**39.2% (n=102)** — the buffed half is **18.2 points EASIER**, ±8.0. The buff is on the wrong side of
the gap.

And the line that settles it: **the m3 HVT (23.0 HP) and the m6 HVT (22.4 HP) are the same size of
body, and the two missions read 38.3% and 69.7%.** The same target, 31.4 points apart. Whatever is
making a mid-run Decapitate hard, it is not the size of the thing you have to kill.

## 4. WHAT THE ASYMMETRY ACTUALLY IS — the FORCE, not the target

`Mission.Build` has a de-stack branch gated on `n >= Run.MaxMissions`: the finale drops **3-4
bodies** from the force and resets `bump` to `max(0, n-1)`, dropping the boss card's and heat's
`StatDelta` off every supporting body. Nothing equivalent exists mid-run — and an **ELITE campaign
node adds +2 bodies and +1 stat**, the exact inverse. So the campaign's climax is the *lightest*
escort a boss gets all game, and a mid-run Decapitate is the same punch-through against a force
that was never trimmed.

Round B's node-kind cells price that directly. Decapitate by node kind: **Supply 67.3% (n=52)** (a
node that fields **one fewer body** and −1 stat), **Combat 32.9% (n=73)**, **Elite 42.1% (n=38)**,
**Boss 69.7% (n=479)**. One body and one stat point separate the Supply cell from the Combat cell
and they are **34 points apart**. That is the same order as the 25-point mission-1 move X2 got from
one body — quoted as a prior on the SIZE of a body, not as a current number (X2's figures predate
W1 and are formally incomparable to this tree).

## 5. THE BIGGER DEFECT THE CROSS-TAB FOUND: the two KILL objectives

The wave was chartered on Decapitate because its flat row (63.7%) is the worst in the game. The
cross-tab shows that row was not the worst thing hiding in `byObjective` — **`Eliminate` was**, and
it was hiding behind a 89.6% headline:

| objective | flat row | Start (m1) | Combat | Elite | Supply | Boss |
|---|---|---|---|---|---|---|
| Eliminate | **89.6** (n=1158) | 97.5 (n=960) | **42.3** (n=104) | **33.3** (n=33) | 77.0 (n=61) | — |
| Decapitate | **63.7** (n=642) | — | **32.9** (n=73) | **42.1** (n=38) | 67.3 (n=52) | 69.7 (n=479) |
| Defend | 84.2 | — | 81.2 (n=329) | 72.5 (n=109) | 91.9 (n=297) | — |
| Escort | 86.9 | — | 91.4 (n=163) | 83.1 (n=83) | 80.0 (n=60) | — |
| Evac | 88.2 | — | 87.5 (n=64) | 84.9 (n=33) | 93.3 (n=30) | — |
| Hack | 85.3 | — | 80.0 (n=55) | 93.9 (n=49) | 75.0 (n=12) | — |
| Rescue | 85.0 | — | 82.4 (n=125) | 86.6 (n=97) | 87.0 (n=77) | — |
| Sabotage | 83.5 | — | 84.7 (n=118) | 73.5 (n=34) | 100.0 (n=12) | — |

**Eliminate's flat row is 89.6% because 960 of its 1158 rows are mission 1.** Every campaign opens
on a Start node, a Start node is always Eliminate (`Run.ObjectiveFor(1)`), and mission 1 wins 97.5%
of the time. Pooled over its mid-run cells Eliminate reads **40.1% ±4.2 (n=137)** — strip the opener
and the game's best-looking objective is its **worst**. That is the same composition artifact the
wave was chartered to fix for Decapitate, one row up, in the opposite direction, and **larger**:
**49.5 points of it against Decapitate's 23.7.**

Pooled over the mid-run node kinds (Combat + Elite), the split is not by objective at all — it is
by **whether the objective can be won without winning the fight**:

| on Combat + Elite nodes | win% | ±SE | n |
|---|---|---|---|
| **KILL** (Eliminate, Decapitate) | **38.3** | 3.1 | 248 |
| **NON-KILL** (the other six) | **83.4** | 1.0 | 1259 |

**45.1 points, SE 3.3.** At mission 5, `Eliminate` reads **25.6% (n=43)** and `Sabotage` reads
**96.8% (n=31)** — the same mission depth, a 71.2-point spread. Six of the game's eight
objectives end when you reach a tile, hold a timer or set a charge; two end only when bodies fall.
`docs/DESIGN.md` §A wants objectives that "force movement" and break the turtle — they do, and the
measurement says they also let a squad **decline the encounter entirely**. Whether that is the
design working or the design leaking is a call for a design wave, not a measurement one, but
nobody could see it before this cross-tab existed.

## 6. THE LEVER, PRICED — and shipped OFF

**One lever, one round, against a fresh same-slot baseline on this wave's own tree.** The dial is
`SIGHTLINE_HVTDEPTH`, the brief's option (b): the buff's depth coefficient, `+6 + 1*mission` HP,
inverted to `+6 − 1*mission`. The mechanism argument for picking that term over the flat one is
that a rank-and-file body's HP **already** scales with depth — `Mission.SpawnEnemies` sets
`bump = (n-1) + statDelta` and every archetype's HP is `base + bump` — so `+_run.Mission` scales the
HVT with depth a second time.

**The instrument had to be the objective pin.** Unpinned, a buffed HVT appears in **61 of the 3,547
missions round B played — 1.72%, or 0.064 per campaign.** No campaign-level round can price a term
that rare. `SIGHTLINE_OBJ=decapitate`
pins every mission of every run, which puts the buffed body on missions 1, 2 and 4 (3, 5 and 6 have
a named ELITE and stay exempt) and raises the population to **824 of 1,233 missions**. **The price
of the pin is representativeness**: a run of six Decapitates is not a run anyone plays — no Defend
anchor, no Escort, and mission 1 becomes a Decapitate, which never happens naturally. Pinned run
completion is 8.3% against the unpinned ladder's 46.9% at h0. **P0/P1 measure the MISSION, not the
campaign.**

Rounds **P0** (defaults) and **P1** (`HVTDEPTH=-1`), 6 rungs × 4 slot sets × N=10 = **480 campaigns
each on identical CRN worlds**, all 48 chunks `OK ... runs=20`:

| pinned Decapitate | P0 (buff `6+m`) | P1 (buff `6−m`) | Δ | HVT MaxHp |
|---|---|---|---|---|
| **m1 BUFFED** (every campaign plays it — no survivorship at all) | 89.8% (n=480) | **92.9%** (n=480) | **+3.1** | 14.4 → 12.4 |
| m2 BUFFED | 54.2% (n=201) | **60.9%** (n=207) | +6.7 | 19.5 → 15.2 |
| m4 BUFFED | 47.6% (n=143) | **57.3%** (n=150) | +9.7 | 23.2 → 15.4 |
| m3 EXEMPT (buff cannot touch it) | 40.9% (n=257) | 42.2% (n=277) | +1.3 | 23.0 → 23.0 |
| m5 EXEMPT (buff cannot touch it) | 43.6% (n=94) | 50.0% (n=120) | +6.4 | 27.0 → 27.0 |
| all buffed missions | 73.8% (n=824) | 78.6% (n=837) | **+4.8 ±2.1** | 17.2 → 13.7 |
| pinned run completion | 8.3% (n=480) | 13.1% (n=480) | +4.8 ±2.0 | — |

**The lever is real and it behaves exactly as the arithmetic predicts** — the HP it removes at m1/m2/m4
is −2.0 / −4.3 / −7.8 against a predicted −2 / −4 / −8, and the win rate moves monotonically with it.
The EXEMPT rows are the control: their HVT MaxHp is **identical to the tenth of a point** across the two
arms, so their +1.3 / +6.4 is downstream carry-over (healthier squads arriving at m3/m5), not the dial.
Any single-arm reading of P1 that ignores that would over-credit the lever.

**It is shipped OFF, for three reasons, in order of weight.**

1. **It is on the wrong side of the gap.** Section 3: the buffed half of the mid-run population is
   already the EASIER half (57.4% vs 39.2%). This lever makes the easy half easier.
2. **It cannot close the gap it was chartered to close.** *Extrapolation, not a measurement*: apply
   P1's largest measured buffed-mission effect (+9.7) to round B's 61 buffed missions and the mid-run
   Decapitate figure moves from 46.0% to roughly 49.7% — still ~20 points under the finale's 69.7%.
3. **The population is 1.72% of missions played.** A 0.064-missions-per-campaign term cannot move a
   ladder rung, and this project has spent waves discovering that after the fact.

`SIGHTLINE_HVTBUFF` / `SIGHTLINE_HVTDEPTH` / `SIGHTLINE_HVTAIM` therefore ship **default-identical to
the pre-W8 arithmetic**, priced and unspent — the same disposition X2 gave `SIGHTLINE_AIMTRIM` and
friends. The numbers above are what a future wave needs to decide differently without re-running the
round.
## 7. THE PROBE: how much of "Decapitate is hard" is the measuring bot?

`SmartDecapitate` is a **hard focus policy**: while the HVT lives, every soldier peels its guards,
shoots it, grenades it, or **walks at it**, ignoring the rest of the force except a blocker within 3
tiles. On the finale that is fine — `Mission.Build` has already de-stacked the boss force by 3-4
bodies. On a mid-run Decapitate the force is at full strength and the same policy marches a squad
across an intact firing line. So every published Decapitate figure was a joint property of the
**game** and of one `if` in `Game.Autopilot.cs`, and nothing had ever separated them.

`SIGHTLINE_HVTPOLICY=0` (autopilot-only; `Game.SmartHvtFocus`) demotes the HVT to an ordinary
target, except when it is the last active hostile so a batch can never stall. It runs from
`runbin/W8inst3`, which `R0diag` proves logic-identical to the rounds' binary at the dial's default
(**45 of 46 fields byte-identical on three chunks**).

**Pinned (P0 vs P2, 480 CRN-paired campaigns per arm).** The HVT is untouched — its average MaxHp
is 17.2 against 17.4 — so everything below is the bot:

| pinned Decapitate | focus ON (shipped) | focus OFF | Δ |
|---|---|---|---|
| **m1** (every campaign plays it — no survivorship, HVT MaxHp identical at 14.4) | 89.8% (n=480) | **94.6%** (n=480) | **+4.8** |
| m2 | 54.2% (n=201) | 62.7% (n=220) | +8.5 |
| m4 | 47.6% (n=143) | 56.5% (n=170) | +8.9 |
| all buffed missions | 73.8% (n=824) | 79.1% (n=870) | **+5.3 ±2.1** |
| pinned run completion | 8.3% (n=480) | **15.8%** (n=480) | **+7.5 ±2.1** |

At mission 1 — identical worlds, identical squad, an identical 14.4-HP target, zero survivorship —
**telling the bot to stop charging the HVT is worth +4.8 points, more than the entire game lever
the brief proposed (+3.1 at the same cell).**

**Unpinned (B vs BP, the same 24 chunks / 480 campaigns per arm on bases 0-30) — and this is the
result that matters.** The B column here is round B's four-slot-set SUBSET, so its mid-run cell reads
47.7% (n=86) rather than the full round's 46.0% (n=163); the pairing is chunk-for-chunk and only
over chunks both rounds completed.

| unpinned | focus ON | focus OFF | Δ |
|---|---|---|---|
| Decapitate, **mid-run** | 47.7% (n=86) | 57.0% (n=86) | +9.3 ±7.6 |
| Decapitate, **finale** | 68.0% (n=234) | 77.4% (n=239) | +9.4 ±4.1 |
| **the gap between them** | **20.3 ±6.2** | **20.4 ±6.0** | **+0.1 ±8.6** |
| run completion | 33.1% (n=480) | 38.5% (n=480) | +5.4 ±3.1 |

**The policy costs about nine points on a Decapitate — and it costs them on BOTH sides.
The mid-run/finale gap does not move at all.** So the bot is a real, previously invisible tax on
this objective's measured difficulty, and it is **not** the source of the asymmetry the wave was
chartered on. That asymmetry is structural, and §4 names what it is.

*(An interim reading of this round at 13 of 24 chunks showed the mid-run cell moving +22.5 while
the finale moved +8.3, which would have been the opposite conclusion. It was a rung-mix artefact of
an unfinished round — the easy rungs land first. It is recorded here because this program has been
burned by exactly that shape of mistake, and because the fix is trivial: the pairing script now
intersects the two rounds' completed chunk sets before pooling anything.)*

The dial stays **default ON** — it is today's behaviour and every archived number was measured
through it. `HVTTEST` pins that default, so it cannot drift silently.

## 8. VERIFICATION

- `dotnet build -c Release`: **0 warnings / 0 errors.**
- `bash scripts/qa-sweep.sh --full`: every self-test PASS, **COVERAGE GAP block empty**, PAIRTEST
  PASS, autoplay ×3 clean (no TIMEOUT, no exception).
- **`SIGHTLINE_HVTTEST` (new, wired into the sweep) PASSES, and it was proven able to FAIL five
  ways** — a test that cannot fail is not a test. Two of the five are code reverts, applied,
  observed and restored:
  - `SIGHTLINE_HVTBUFF=3` → `FAIL shippedHpBase=3, bonus(m1)=4 … hpDelta s1301 m4 7!=10 …`
  - `SIGHTLINE_HVTAIM=0` → `FAIL shippedAim=0, aimDelta s1301 m2 0!=6 …`
  - the ELITE exemption reverted in `DesignateHvt` (`HvtBuffed = true`) →
    `FAIL exemptFlag s1301 m3 ELITE, hpDelta s1301 m3 9!=0, marker s1301 m3 HVT-BREAKER, …,
    noExemptCaseSeen, finaleNotExempt s1301 …` (restored, re-run, PASS)
  - the `IsSpecial` exclusion removed from the selection → `FAIL specialHvt s1301 m4 SHIELD,
    specialHvt s4242 m4 TURRET` (restored, re-run, PASS)
  - `SIGHTLINE_HVTPOLICY=0` → `FAIL hvtPolicyDefaultOff`
- **205 measurement chunks run, zero `BAD`** — every one asserted on all three layers (JSON
  removed first, exit code checked, `runs == 2N` re-read from the file). **153 chunks / 3,060
  campaigns are archived**: B (48 / 960), P0, P1, P2 and BP (24 / 480 each), plus the `I1` and
  `R0diag` inertness pairs. The 52 pruned chunks are a mid-wave diagnostic grid and inertness pair
  run on an earlier build of the same instrument; `B` and `I1` supersede them exactly and the
  archive is 4 MB smaller without them.

## 9. WHAT I DID NOT FIX, AND WHAT IT COST

- **The mechanism that is actually there is unspent.** §4 names it — the finale is de-stacked by
  3-4 bodies and has its stat bump reset, and no mid-run Decapitate is. A trim gated to mid-run
  Decapitates is one dial and one paired round, and the Supply-vs-Combat cell (67.3% vs 32.9% on
  one body and one stat point) is a prior on its size. W8 did not spend it because CLAUDE.md's
  contract is one lever per round and this wave's round went to the HVT buff the brief named.
  **That was the right call for the brief and the wrong lever for the defect; the next wave should
  spend its round on the force, not the target.**
- **The KILL/NON-KILL gap is a design question and I did not touch it.** Six of eight objectives
  end when the squad reaches a tile, holds a clock or sets a charge, and the measurement says a
  squad can decline the encounter and still win. Whether that is `docs/DESIGN.md` §A's anti-turtle
  design working as intended or leaking is not something a balance round can settle.
- **Two caveats on the KILL/NON-KILL number, stated plainly.** (a) It holds node kind and mission
  depth constant (both cross-tabs agree) but **not squad condition** — `byObjectiveByBucket` is the
  instrument for that and is still too sparse at these n. (b) The autopilot's non-kill policies are
  written to skip the fight (`SmartEvac`: *"EXTRACTION IS A RACE … beeline to the zone FIRST"*), so
  part of the 83.4% is the bot exploiting an exit the game offers. A human would too — but the
  number is a joint property of the game and the bot and must not be quoted as the game alone.
- **The pinned rounds are not campaigns.** P0/P1/P2 pin every mission to Decapitate; pinned run
  completion is 8.3% against the ladder's 46.9% at h0. They measure a mission, not a campaign —
  which is why §7's conclusion rests on the UNPINNED B/BP pair and the pinned P0/P2 table is only
  the mechanism check beside it.
- **`SIGHTLINE_HVTAIM` was never spent.** The aim half of the buff (+6, which partly duplicates
  what `bump` already gives) is dialled and priced at zero rounds. One lever, one round, whenever
  someone wants it.

---

# PROGRAM RESONANCE — WAVE "THE FIT" (2026-08-30, dev on `wave/the-fit`)

**Branch** `wave/the-fit`, base = the W5/W8/W9 integration tip (`6e55f8d`). Source: the five
UI/layout defects held back from W9 because W5 was rewriting `src/Hud.cs` at the same time. Each
had been found by an adversarial QA pass and then independently reproduced by a second agent.
**Balance-inert by construction and by measurement** (§7).

## 1. THE THESIS — the defects were one thing, and it was not five call sites

Four of the five were the same structural gap, and it is worth stating plainly because it is the
kind of gap this project keeps re-discovering under different names:

> **No self-test in this project ran at any text size but 100%.**

`SIGHTLINE_UISCALE` is a screenshot-only hook (`Program.cs`, gated on `shot`) that exists so a
human can *photograph* the UI at another size; nothing asserts anything there. Meanwhile W5 ships
**four** text scales — {0.90, 1.00, 1.10, 1.20} — against a lot of literally-typed pixel chrome.
Two waves had already stepped on the edge of this: W5's `CHROMETEST` gated the draft's DEPLOY row
across all four scales, and R2's `VOICETEST` leg 8 gated every *card body* across all four. Both
were surface-specific. Everything else in the game was measured once, at 1.00, or not at all.

So the deliverable is not five patches. It is **`SIGHTLINE_FITTEST`** — a standing gate that makes
the whole shipped scale range a tested surface for the screens involved.

## 2. RE-REPRODUCTION FIRST — and W5 had already fixed two of them

The brief said to re-reproduce all five before touching anything, and that "already fixed by W5"
is a perfectly good outcome. It was, for two of them:

| # | defect as filed | status on this tree |
|---|---|---|
| 1 | FIELD DRILLS doctrine card overflows its 74 px frame **on the DRAFT screen** | **FIXED by W5-FIX-2** — the card is content-sized and CHROMETEST asserts it |
| 1b | the same string on the **mid-run FIELD DOCTRINE screen** (`ch = 188`) | **REPRODUCES, at the DEFAULT text size** |
| 2 | the draft's BACK/DEPLOY/RE-ROLL row runs off the bottom at 120% | **FIXED by W5-FIX-2** — `Hud.DraftLayout`; probed (640, 798) and (640, 799) = background |
| 2b | RE-ROLL POOL's label spills past both edges of its 190 px plate | **REPRODUCES** — 201 px label at 120% |
| 3 | ARMORY blurb overprinted by the right-aligned price at 110%/120% | **REPRODUCES** at 100%, 110% and 120% |
| 4 | draft operator blurb drawn under the class-glyph disc at 100% | **FIXED by W5** (the disc dropped to the corner) — but **CHANGED SHAPE**: at 110%/120% the blurb now *ellipsizes* instead |
| 5 | HALL OF FAME legend sub-line reaches the panel border at 120% | **REPRODUCES** — last ink x=821, cyan border x=823 |

**Reproduction rate: 3 of 5 verbatim, 2 of 5 fixed by W5 with a live residue in the same family.**
Both residues are worth having found, because both are the SAME defect one step over: #1's second
call site draws the same sixteen strings from a box W5 never touched, and #2's plates were never
measured at all — W5 fixed *where* that row sits, not *how wide* its buttons are.

**#4 deserves its own note, because it is the thesis in miniature.** W5 fixed the overlap and added
the assertion — `if (Cfg.Measure(bl, DraftBlurbFs, 1f).X > DraftBlurbWidth()) fails.Add(...)`. But
it wrote that line **outside** the `foreach (float ui in Display.UiScaleLevels)` loop three lines
above it. So the one guard on the longest sentence on the squad-selection screen ran at 100% only,
where the measurement is **266.4 px in a 268 px column — two pixels**. At 110% it is 293 px and at
120% 320 px, and the shipped `Clip()` quietly ate the last words. A correct fix, a correct test, and
the test in the wrong scope.

## 3. THE FIXES — grow the chrome, never drop below 12 px

The house pattern (W5 sized the doctrine cards to content rather than ellipsizing; W9's review
caught a "fix" that quietly pushed five WAR ROOM descriptions to 10 px). Nothing here shrinks a
type size. Nothing here truncates a string that was not already truncated.

**(a) The mid-run FIELD DOCTRINE card sizes to its text.** `Hud.BoonOfferCardH(lines)` mirrors
`DraftBoonCardHeight`, and the row takes the height its tallest description needs. Measured: at
100% FIELD DRILLS' body ink bottom is **160 px against a `[ CHOOSE ]` top of 158** — the collision
the QA report described, at the default setting, arithmetically. Now 214 px of card and 15 px of
clear air at the tightest scale.

**(b) The ARMORY row's two strings stop sharing a band.** The diagnosis matters more than the fix:
the blurb's origin was a literal `r.X + 48` while the tag's was `r.Width - 14 - Cfg.Measure(tag)`,
i.e. **one end scaled and the other did not**, so they converged as the setting rose. At 100% that
left ~20 px of slack, which is inside the noise of a font change. The tag moves onto the NAME's
band (where the longest weapon name leaves ~240 px of air) and the card widens 560 -> 600 so the
49-char SNIPER blurb keeps 69 px rather than 15. **Widening alone was not enough** — a source revert
of just the band move, with the wider card in place, still fails FITTEST at 8 violations.

**(c) The HALL OF FAME legend row splits.** Identity (`RANK CLASS`) left, score (`n K · Hn`)
right-aligned to the panel's content edge. This is a reflow, not a shrink, and it pays a dividend:
the score columnises down the list, so kills are scannable. **With the worst case actually staged**
(LIEUTENANT SHARPSHOOTER, 327 px in a 290 px column) the pre-fix line is not "flush against the
border" — it is painted *outside the panel*, on the War Room background.

**(d) The draft's bottom-row plates are measured.** RE-ROLL POOL 190 -> 229 px, DEPLOY 280 -> 316 px
at 120%. Both from the **widest label the button can ever show**, never the one it is currently
showing: a plate that resized as the draft filled would move BACK and RE-ROLL under the player's
cursor, which is precisely what CHROMETEST leg (A) exists to forbid on the action bar.

**(e) The draft operator card widens 300 -> 394.** `DraftCardW()` now derives from the RUN CONTRACT
row the same way `DraftBoonCardW()` does — 3 cards + 2 gaps == that row's 1230 px. The blurb column
goes 268 -> 362 px, so 320 px fits with 42 to spare at 120%. This is also a composition fix in its
own right and it is W5-FIX-2's own argument applied one row higher: the candidate grid was 948 px
under two rows of 1230, the one mismatched width left on the screen.

## 4. THE GATE — `SIGHTLINE_FITTEST`

One contract, five legs, every leg run at **all four** shipped scales:

> **No string is painted outside the box that owns it, and no two independent strings are painted
> into the same pixels.**

- **(A)** every boon's wrapped body clears `[ CHOOSE ]` and the card border; the block fits the canvas.
- **(B)** every weapon row x every one of its three right-hand tags: the tag and the blurb may share
  a *band* or a *column*, never both; the blurb fits its budget; the tag clears the name.
- **(C)** every **rank x class** (8 x 5 — not the five short staged legends) with a three-digit kill
  count: identity and score clear each other and the panel's inner border.
- **(D)** all five DEPLOY state labels + RE-ROLL + BACK fit their plates; the row fits the canvas.
- **(E)** every class blurb and every ABILITY line fits its column; the grid fits the canvas.

It measures through the **real font atlases** (`LoadGameFonts()` before the first `Cfg.Measure`) for
the same reason CHROMETEST does — raylib's default face is narrower and every overflow vanishes —
and it measures through the renderer's **own** `WrapLines` (`Hud.WrapLinesForTest`), not a copy of
it, because W5-FIX's lesson is that a test which re-implements the sizer's arithmetic cannot fail.

The PASS line **states its own headroom** rather than merely asserting there is some:

```
FITTEST: PASS (16 doctrine cards, 5 weapon rows x 3 tags, 8x5 legend rows, 5 deploy labels and the
operator card fit their chrome at all 4 shipped text sizes - tightest margins: doctrine 15px@FDR@120%,
armory 69px@Sniperblurb@120%, legend 24px@RAname@120%, draftRow 24px@DEPLOY@110%, operatorCard 60px@blurb@120%)
```

**Proof it can fail.** `SIGHTLINE_OLDFIT=1` restores all five pre-fix geometries (the literal 188,
the tag at `r.Y+20`, the joined legend line, the 190/280 plates, the 300 px card):

```
FITTEST: FAIL (40 violations; first 14: boonBodyHitsChoose:FDR@100%(160>158),
armoryTagOverprintsBlurb:Sniper/- need intel -@100%, ... legendSubOverruns:LIEUTENANT
SHARPSHOOTER@110%(303>290), rerollLabelOverruns@110%(186+8>190),
draftBlurbEllipsizes@110%:Assault rifl(273>268), ... deployLabelOverruns@120%:DEPLOY  (PAY 999
SALVAGE)(294>280))
```

40 distinct violations, every leg represented, at 100 / 110 / 120% (90% is clean, as it should be).
`SIGHTLINE_OLDFIT=1` **also fails CHROMETEST now** (5 x `blurbEllipsizes@110%/@120%`), which is the
proof that moving that assertion into the scale loop did something. And because a flag-driven revert
is not the same as a real one, a **direct source revert** of one fix alone — `ArmoryTagY => 20`,
leaving everything else fixed — fails FITTEST at 8 violations. Restored, PASS.

## 5. THE INSTRUMENT DEFECT UNDERNEATH ALL FIVE: the hooks staged the EASY case

The QA report for #5 said it plainly and it generalises: *"the staged Hall of Fame data is
hard-coded to five short legends, so even a human eyeballing the shot at 100% sees ~50 px of slack
and no reason to suspect the row is one text-size step from touching the frame."* The same is true
of the other two hooks. So all three now stage the **worst** case:

- `DebugBoon` leads the offer with the longest description in the catalogue. FIELD DRILLS is 98
  chars against 60 for the next longest and is **1 of 16**, so an unseeded glance at that screen
  showed it ~19% of the time — which is how it survived ten programs.
- `DebugWarRoom`'s NOX is now `LIEUTENANT SHARPSHOOTER` with a nickname: the longest rank+class
  pair the game can produce, on the longest name shape.
- `DebugArmory` picks the SHARPSHOOTER (49-char SNIPER blurb) instead of `Squad.First(!IsVip)`,
  which was an ASSAULT carrying the second-*shortest* blurb in the game.

A screenshot hook that photographs the easy case is worse than no hook, because it produces
evidence of a fit that was never tested.

## 6. SCREENSHOTS — read and judged

All at 1280x800, Release binary under xvfb, `SIGHTLINE_OLDFIT=1` for the BEFORE half so the two
frames differ only in the geometry under test.

- **FIELD DOCTRINE, 100%, before:** FIELD DRILLS wraps to four lines and the fourth, "turn)", sits
  directly on `[ CHOOSE ]` with zero leading — the `)` descender crosses the `[`. Both strings
  unreadable. **After:** the card is 214 px, the fourth line has its own row, `[ CHOOSE ]` has real
  air above it, and all three cards keep a common height so the row still reads as a row. At 120%
  the card grows again and the same thing holds.
- **ARMORY, 120%, before (SHARPSHOOTER staged):** "high crit" is painted through "EQUIPPED" and
  "4-round clip" through "[ 7 INTEL ]"; on both rows the two strings' strokes cross and neither
  reads. **After:** the tag sits on the weapon name's baseline, right-aligned — which also reads
  *better*, because a price belongs beside the item name — and both blurbs render in full.
- **WAR ROOM, 120%, before:** NOX's sub-line runs through the cyan panel border and "H3" is painted
  outside the panel entirely. **After:** the score column right-aligns and lines up across all five
  legends; gold for run-winners, dim for the fallen, so it carries a second channel as well.
- **DRAFT, 120%, before:** three blurbs read "…picks off th…", "…brutal up clos…", "…closes and
  cl…" and RE-ROLL POOL's label hangs past both edges of its plate. **After:** every blurb complete,
  the label inside its plate, and the whole screen now reads as one 1230 px column instead of a
  narrow grid stacked on two wide rows. Checked at 90% and 100% too: clean.

## 7. VERIFICATION

- Release **0 warn / 0 err**.
- `bash scripts/qa-sweep.sh --full`: **63/63 PASS** (63 exist in `src/`, 63 ran), COVERAGE GAP block
  empty, `PAIRTEST: PASS`.
- Autoplay x3: LOSE m4 / WIN m6 / LOSE m6. No TIMEOUT, no exception.
- **Balance inertness.** `SIGHTLINE_BALANCE=5 SIGHTLINE_BALANCE_BASE=950`, Release binaries from
  gitignored snapshots (`runbin/fit`, `runbin/base` built from the branch point), target JSON
  `rm -f`'d first, **exit 0** and **`runs=10`** asserted on both sides.
  `docs/measurements/w1/inert_diff.sh ... harness` -> **empty diff, IDENTICAL**: every per-slot
  RunRec, win, loss, turn count and shots-per-kill byte-for-byte the same.

## 8. WHAT I DID NOT FIX, AND WHAT IT COSTS

- **The gate covers five surfaces, not the game.** Every other screen is still asserted at 100%
  only (or, for card bodies, by VOICETEST leg 8). FITTEST is deliberately written as five
  independent legs inside one scale loop so a sixth is an addition rather than a rewrite; the next
  wave that touches a screen should add one. **Unmeasured: how many other screens overflow at
  120%.** I did not sweep them, and I am not going to claim they are clean.
- **The vet operator card is unexercised.** The recall-fee ribbon (`RECALL nn`, right-aligned on
  the name row) and the veteran dossier line only appear when the reserve has veterans, which no
  screenshot hook stages. The card got 94 px wider so both got *more* room, but FITTEST does not
  assert them and I did not photograph them.
- **The REQUISITION card is still sized for the whole roster in ARMORY step 2** (`armoryH` uses
  `Run.RosterMax` regardless of how many weapon rows the chosen soldier has), so a SHARPSHOOTER
  leaves ~250 px of dead panel. Pre-existing and cosmetic; out of scope for a wave about overflow.
  It is now *visible* in every armory screenshot because the hook stages a SHARPSHOOTER.
- **`WrapLines`' line pitch is still `size + 6`, unscaled.** At 120% a 14 px body is 16.8 px of
  glyph in a 20 px step — 3.2 px of leading where 100% gets 6. It is tight, not broken, and it is
  the convention every shipped card already uses (`DraftBoonLineH = 19` for 13 px type does the
  same). Changing it would move every wrapped block in the game; it deserves its own pass.
- **The 90% scale finds nothing, by construction.** Smaller type in fixed chrome cannot overflow.
  FITTEST runs it anyway so that a future fix which *shrinks* chrome is caught, but the four-scale
  headline is really a three-scale result.

## 9. TWO THINGS I THINK THE BRIEF / THE BRANCH GOT WRONG

1. **The `bugs-ui.md` dossier's "VERIFIER SAID" blocks are shuffled and truncated.** Entry #2's
   verifier text describes the *armory* measurement (bug #3); entry #3's describes the *draft
   class-glyph* (bug #4); entry #4's describes a `ShotAnim` downed-shooter bug that is not in the
   dossier at all (it is W9's). Every block also ends mid-sentence. The *claims* were still worth
   testing and four of the five measurements I could check reproduced within a pixel or two — but
   the attribution in that file cannot be trusted, and I verified each entry against the tree
   rather than against its verifier note.
2. **The integration branch has committed, unresolved merge-conflict markers in three docs.**
   `CLAUDE.md` (lines 214-236 and 302-307), `docs/ROADMAP.md` (1726-1938) and `docs/FEATURES.md`
   (682-801) all carry live conflict blocks on
   `origin/claude/game-dev-team-orchestration-5bbxg8`. The build is unaffected (docs only), but
   `CLAUDE.md` is the continuity contract and it currently states the self-test count two
   contradictory ways in the same section. I did **not** resolve them — that is the lead's merge to
   arbitrate and a dev worktree resolving it would fight the next wave — but a fresh session
   reading `CLAUDE.md` top to bottom will read a conflicted file.

Also worth recording for the next agent in this container: the worktree isolation guard **refuses
any command that sets `XDG_CONFIG_HOME` or `HOME`**, which is exactly what CLAUDE.md's house
procedure tells every agent to export. Every run in this wave was made without it. Nothing
collided, but that is luck, not procedure — the two rules contradict each other and one of them
needs to change.

# PROGRAM RESONANCE — Wave W2 "THE OPPONENT ACTS" (dev; worktree `agent-aec483dc62bf01706`, branch `wave/opponent-acts`)

**Base commit: `4784803`** (the integration tip carrying W1 TRUE INSTRUMENT and TRUE BAND — NOT
`main`).

> ### ⚠ THIS WAVE'S FIRST WRITE-UP WAS WRONG ABOUT ITS OWN HEADLINE NUMBER
>
> It published **"32.4% of enemy act-opportunities end with an unspent action"** as combat
> paralysis, and built its "REAL FINDING" and its default-ON argument on top of that. **86% of
> that number was the bleed-out window** — act-opportunities on boards where every surviving
> soldier was already on the floor. The review caught it; I reproduced the split independently
> and it is exactly right. The corrected number is **6.2% (n=16 campaigns) / 3.8% (n=32)** during
> live contact. Everything below is the re-measured version. The wave is not wrong that the enemy
> idles; **it was wrong about when, why, and by how much**, and it shipped a feel regression into
> the game's most dramatic beat while looking the other way.

## THE DEFECT, MEASURED PROPERLY

`Game.UpdateEnemy`'s `ActAfterMove` stage is a twelve-branch else-if chain (eleven before this
wave added RELOAD). **Every branch in it changes `ActionsLeft`** — the shot and the reload decrement
it, the other ten zero it — so "the unit still holds an action AND `ActionsLeft` is unchanged" is an
exact structural test for *no branch fired*. That detector is sound and an independent probe
reproduced it act-for-act. Two things about it, both from review:

- **It tests "no branch fired", NOT "no action left".** An earlier comment claimed the stronger
  invariant. It is false: 30 of 963 acts (3.1%) end holding exactly one action, via the SHOT branch
  decrementing 2 → 1 and `TryEnemyReposition` declining at `curExp < 2.0f`. A covered shooter
  holding its ground is intended behaviour — but it is a spent *branch*, not a spent *action*.
- What the detector lacked was the only context that makes an idle count readable:

**When every surviving soldier is DOWNED, `Ai.Plan` returns an empty plan by design.** `Ai.cs:78`
removes downed soldiers (the FUL-7 LAST LIGHT rule that enemies do not execute bodies), and
`players.Count == 0` returns immediately. Every hostile then idles, on a board where no soldier can
act either and the bleed-out timers are running the mission out. **That is not a defect and it must
not be "fixed".** So the invariant this wave actually enforces is *no **CONTESTED** act-opportunity
ends unspent*, and every counter is split on standing-soldier count.

`SIGHTLINE_AIIDLETEST` output, both frames (deterministic; reproduced byte-for-byte):

**n=16 campaigns (`AIIDLETEST=1`, 86 missions)**

| leg | regime | acts | actsDry | idle | idleDry | noTgt | acted |
|---|---|---|---|---|---|---|---|
| OFF | **contested** | 755 | 69 (9.1%) | **47 (6.2%)** | 42 | 5 | — |
| OFF | all-downed | 293 | — | 293 (100%) | — | 293 | **0** |
| ON | **contested** | 644 | 22 (3.4%) | **0 (0.0%)** | 0 | 0 | — |
| ON | all-downed | 319 | — | 319 (100%) | — | 319 | **0** |

**n=32 campaigns (`AIIDLETEST=2`, 168 missions)**

| leg | regime | acts | actsDry | idle | idleDry | noTgt | acted |
|---|---|---|---|---|---|---|---|
| OFF | **contested** | 1195 | 39 (3.3%) | **45 (3.8%)** | 24 | 21 | — |
| OFF | all-downed | 400 | — | 400 (100%) | — | 400 | **0** |
| ON | **contested** | 1168 | 19 (1.6%) | **0 (0.0%)** | 0 | 0 | — |
| ON | all-downed | 444 | — | 444 (100%) | — | 444 | **0** |

The unsplit rates (32.4% / 27.9%) are printed too, labelled *do NOT quote this as a paralysis
rate*. **`AIIDLETEST=1` is 16 campaigns, not 32** — the loop is `{heat 0, heat 4} × 8 objectives ×
n`, and the first write-up mislabelled it in three documents.

**The two contested causes, and the first write-up had their sizes backwards.** `idleDry` and
`noTgt` partition the contested idles exactly (42+5 = 47; 24+21 = 45):

- **A dry weapon** — `Ai.cs` touched `.Ammo` at exactly two lines, neither of them the
  reachable-tile shot search, so a dry hostile planned a shot; a non-null `ShootTarget` suppresses
  the whole no-shot fallback; `ActAfterMove`'s own `e.Ammo > 0` gate then refused it. **89% of
  contested idles at n=16, 53% at n=32.**
- **The missing terminal else** — no line of sight AND no cover got neither overwatch nor hunker.
  **11% at n=16, 47% at n=32.**

**The split is not resolvable at these samples.** Both causes are real; neither dominates; and the
published "87.6% were units with no planned target" was an artifact of counting the bleed-out
window, where the plan is empty by construction.

## WHAT SHIPPED (`SIGHTLINE_AIIDLEFIX`, default ON)

- **`src/Ai.cs` — the ammo gate.** The tile loop's shot search is gated on `e.Ammo > 0`.
- **`src/Ai.cs` — the terminal else, WITH the guard review forced onto it.** When neither overwatch
  nor hunker qualifies, the plan is re-targeted at `bestDash`, the best tile among those needing the
  **full two-action budget**, tracked by the *same* per-tile scorer in the *same* pass — no new
  policy and **no new `Util.Rng` draw**. But the first version dashed unconditionally, and that was
  wrong by construction: this arm is only reachable when `bestTile` cost 0-1 actions, and `bestTile`
  is the argmax over *all* tiles including the two-action ones, so `bestScore >= bestDashScore`
  **always**. Measured: **13 of 13 dashes strictly worse, mean −12.7 points**, against terrain terms
  bounded under ~64. The unit moved to a tile its own scorer ranked lower, every time, while a
  comment claimed it moved "exactly as its archetype terms already say it should".
  The comparison is now **move-cost-neutral**: every tile's score carries `-actionsToReach * 6`, a
  term pricing an action that in *this* branch has no alternative use, so the differential is
  refunded before comparing and a dash that still loses digs in instead. It now fires **5 of 9
  times it is offered** (n=32 campaigns).
- **`src/Ai.cs` — the plan mirrors the exec.** Overwatch is planned only when the exec would accept
  it (`!Disoriented`). A plan the exec refuses is an idle by another name.
- **`src/Game.cs` — the RELOAD branch.** One action, full clip, mirroring `Game.DoReload`.
- **`src/Game.cs` — the terminal guarantee**, for the exec's side (a target that died, a `PINNED`
  clamp that shortened the move out of range, a plan gone stale between planning and acting).
- **`src/Game.cs` — the STANDING GATE, and it is the review's fix, not mine.** The terminal else,
  the terminal guarantee and the new HUNKER pop/SFX are all gated on `standing > 0`. See the next
  section for what happened without it.
- **`src/Renderer.cs` + `DrawUnitStatusChips` — the ammo read.** Pip row in the 4px band between
  the HP pips and the top of the body; **DRY is a status CHIP** (empty-magazine glyph + the word),
  in the late opaque pass. Gated on `Game.AiIdleFix`. Honest size: a 2-line call site plus a
  28-line helper, plus ~18 lines inside the chip row — an earlier draft called it "~15 lines in the
  token draw". `src/Renderer.cs` **auto-merges clean against W4**; the real conflicts this wave
  leaves are the docs, `scripts/qa-sweep.sh`, and an additive block in `Program.cs`.
- **`docs/DESIGN.md` §5.1 — the ammo economy, decided.** Reload verb over per-turn clip refresh.

## THE REGRESSION I SHIPPED AND THE REVIEW CAUGHT — and why no gate in this wave could see it

The first build gated the terminal else on nothing. **319 of the 320 acts it produced were on
all-downed boards**, each popping `HUNKERED` and playing the hunker cue. The player watched their
whole squad bleed out while five to eight hostiles barked at them, in tails measured up to 28
consecutive act-opportunities. That is a feel regression in the game's single most dramatic beat,
straight against pillars 2 and 3.

**And it is invisible to the price measurement by construction.** I re-ran the entire 800-campaign
paired round on the corrected binary (`R3-*`) and diffed it against the first build's round
(`R1-*`): **all 40 chunk pairs are byte-identical once the `harness` block is stripped.** Removing
320 hostile actions and 319 audio/text pops changed the outcome of exactly zero of 800 campaigns —
because no soldier can act in that window, so nothing the enemy does there can move a win rate.

**The methodological lesson, and it generalises past this wave:** a CRN win-rate round prices
*consequences*, and is structurally blind to any change confined to a state where the outcome is
already determined. Feel changes in decided states need their own assertion. `AIIDLETEST` now
carries one — `actedDuringBleedOut == 0` on **both** legs — and it fails the build I shipped.

## THE HONEST SIZE OF THIS WAVE, STATED PLAINLY

The adjudicator asked for this number and they were right to. Over 32 campaigns (`AIIDLETEST=2`,
1589 act-opportunities on the ON leg), what the wave actually changes in live play is:

| | count | share |
|---|---|---|
| RELOAD issued | 14 | |
| terminal-else fired (5 dashes + 20 dig-ins) | 25 | |
| **total behaviour change** | **39** | **2.5% of all acts, 3.5% of contested acts** |

That is the wave. It is *not* "a quarter of the opposition's turns were invisible paralysis" — that
claim was 86% bleed-out window and it is withdrawn. A contested idle rate of 6.2%/3.8% going to
zero, plus an ammo economy that was never decided, is a real repair at a real but small scale, and
it is the version that survives contact with the measurement.

## GATE 3 — THE PRICE (round R4, the final binary)

**Instrument first.** `R0diag`: the base commit's own binary (`git archive 4784803`) against this
wave's at `SIGHTLINE_AIIDLEFIX=0`, at h0/b0 and h4/b10 — **empty diff** outside `harness`. The
review's adjudicator independently rebuilt from `git archive` and reproduced it at slot base 940, a
base nobody had used (their run, not mine). The dial-off leg is the pre-wave game.

**The round.** 5 heat rungs × four disjoint CRN slot sets (bases 0/10/20/30, N=10) × greedy+sloppy
= **80 campaigns per rung per leg, 800 campaigns**; all 40 chunks asserted their own `runs` field.

```
  rung    OFF%     ON%   delta     n  0->1  1->0  p(2-sided)
    h0    47.5    43.8    -3.8    80     5     2       0.453
    h2    26.2    23.8    -2.5    80     4     2       0.688
    h4    22.5    23.8    +1.2    80     6     7       1.000
    h6    17.5    20.0    +2.5    80     4     6       0.754
    h8    12.5    10.0    -2.5    80     4     2       0.688
  POOL    25.2    24.2    -1.0   400    23    19       0.644
```

### heat 0, taken seriously: sixteen slot sets, 320 campaigns per leg

The review pooled four independent 80-campaign h0 sets and got three negatives (−5.0 / +0.0 / −6.2
/ −7.5, discordant 30/15 ⇒ p≈0.036) and asked whether a 5-point cost at the rung most players are
on was being filed as noise because no single set can resolve it. **That was the right challenge**
and a 4-set answer was not good enough. h0 therefore gets three more slot-set families, all
measured by me on the final binary (`queue_h0.sh`, `analyse_h0.py`):

```
                family     n    OFF%     ON%   delta  0->1  1->0        p
    main round   b0-30    80    47.5    43.8    -3.8     5     2    0.453
   extension    b40-70    80    46.2    48.8    +2.5     4     6    0.754
   family C   b900-930    80    51.2    48.8    -2.5     8     6    0.791
   family D   b940-970    80    56.2    48.8    -7.5    10     4    0.180
  POOLED (all 16 sets)   320    50.3    47.5    -2.8    27    18    0.233

  paired difference -2.8 points, SE 2.1, 95% CI [-6.9, +1.3]
```

**The answer: −2.8 points at heat 0, not distinguishable from zero (p=0.233), but a cost as large
as ~7 points is not excluded.** Three of four families are negative and 60% of the informative
worlds went against the fix, so the direction is more consistent than the significance — I am not
going to call the sign noise, only the magnitude unresolved.

Two things make it shippable anyway, and they are the reason the default stays ON:

1. **Both legs are inside the band at h0.** Pooled over 320 campaigns the control reads **50.3%**
   and the shipped leg **47.5%**, against a band of 55±8 (floor 47.0). The fix does not move any
   rung out of its band. (My own earlier report had the ON leg at 42.5% and below the floor — that
   was four slot sets; sixteen say otherwise, which is exactly why the deep round was needed.)
2. **The finer-grained statistics agree with the null on ~10× the sample.** Mission win-rate
   78.94% → 78.56% (n = 1410 / 1404 missions); soldier deaths per mission **1.340 → 1.340**.

If the lead disagrees with that trade, `SIGHTLINE_AIIDLEFIX=0` is the whole revert — and since the
review it genuinely reverts all of it, the ammo read included.

## THE NUMBER I AM HANDING TO W7 — with the band arithmetic done correctly this time

The dial-OFF control is a fresh n=80/rung read of the composition on the post-W1 tree. **The first
write-up said "h4/h6/h8 are IN band and h0/h2 are not". That is wrong: the band is ±8, so h0's
floor is 47.0 and the control reads 47.5 — inside.**

| | heat 0 | heat 2 | heat 4 | heat 6 | heat 8 |
|---|---|---|---|---|---|
| **control (dial OFF)** | **50.3** (n=320) | 26.2 | 22.5 | 17.5 | 12.5 |
| **shipped (dial ON)** | **47.5** (n=320) | 23.8 | 23.8 | 20.0 | 10.0 |
| band (centre ± tol) | 55±8 | 40±8 | 30±8 | 20±8 | 10±5 |
| control vs band | in | **OUT** (−5.8 under the floor) | in | in | in |

**Only h2 is convincingly outside**, at −13.8 from centre ≈ 2.5 rung-SE (a rung's binomial SE at
n=80 is ~5.6). Every other rung is in band on both legs — including h0, once it is measured deeply
enough: at four slot sets the control read 47.5 and the shipped leg 42.5 (below the 47.0 floor); at
**sixteen** they read 50.3 and 47.5, both inside. That reversal is the single best argument in this
write-up for the L1 method rule, and against reading a rung off one slot-set family.

**"The curve is too FLAT" is a hypothesis, not a result.** The shortfall from band centre runs
−4.7 / −13.8 / −7.5 / −2.5 / **+2.5** across the rungs, which is consistent with a curve flatter
than the band — but only h2 clears 2 SE, so that is one rung out of band and a suggestive trend,
nothing more. W7 owns the band; this is a control leg, not a ladder of record.

## WHAT I DID NOT FIX, AND WHAT IT COST

- **The enemy still cannot CHOOSE.** `Ai.cs` still scores any available shot at `100 + bestHit`
  against terrain terms bounded under 64, so `plan.Overwatch` remains reachable only when no
  reachable tile has any shot. **W3's, deliberately untouched** so this round stayed attributable.
- **The idle repair is a floor, not a policy.** The terminal else spends the action on ground
  scored by the existing function; the exec guarantee spends it on HUNKER. Neither is claimed to be
  the *right* action — only that it is a real one.
- **The dry/no-target split is unresolved** (89/11 at n=16, 53/47 at n=32). Both causes are real;
  which dominates is not established, and I am not going to claim it from two frames.
- **THE ENEMY OVERWATCH BRANCH IS ESSENTIALLY DEAD, and I touched it without noticing.** The review
  found it firing zero times; re-measured here it is **0 of 1595 act-opportunities pre-wave and 3 of
  1589 post-wave** (the 3 are a cascade of the ammo gate changing which boards occur, not a designed
  effect). So the `!Disoriented` plan/exec mirror this wave added to that branch is essentially
  **unexercised** — it is correct, and it is untested by any real play. Pre-existing, not caused
  here, and it is W3's whole premise: `Ai.cs` scores any available shot at `100 + bestHit` against
  terrain terms bounded under ~64, so a lane-hold is only reachable when no reachable tile has any
  shot at all. Logged in the ROADMAP for W3.
- **The ammo read is unmeasured as an affordance.** It draws correctly and no longer collides
  (proved on a staged DRY+BRN token, below) — but nothing shows a player or the autopilot ever
  *baits* a hostile dry. `Game.Autopilot.cs` has no term for enemy ammo at all.
- **The all-downed window is now explicitly out of scope.** Hostiles stand silent over a dying
  squad exactly as they did pre-wave. Whether that window should have *any* presentation is a real
  design question and this wave does not answer it — it only refuses to answer it with a chorus.
- **Container isolation was partial.** This agent's shell refused `XDG_CONFIG_HOME` and `HOME`, so
  persistence self-tests ran against the shared `~/.config/Sightline`. The measured round is
  unaffected (`run_chunk.sh` sets it itself).

## VERIFICATION

- `dotnet build -c Release` — 0 warnings / 0 errors.
- `bash scripts/qa-sweep.sh --full` — every self-test PASS, COVERAGE GAP empty, FXSTREAM PASS,
  **PAIRTEST PASS**, autoplay ×3 with no TIMEOUT and no exception.
- `SIGHTLINE_AIIDLETEST=1` and `=2` — PASS. Four assertions, and three of them fail the build this
  wave first shipped: contested idles == 0, contested dry idles == 0, **nothing acts during the
  bleed-out window on either leg**, and the pre-wave leg must still idle in contested play (so the
  probe cannot pass vacuously). It also now REPORTS, rather than asserts, the three numbers the
  review had to derive by hand: the scale of the live repair, the dash guard's accept rate, and the
  enemy overwatch count — so the next wave does not have to rebuild the probe to check this one.
- Screenshots read and judged. `SIGHTLINE_AIIDLESHOT=1` now **stages the collision the review
  found**: it walks each hostile's clip down and puts BRN/BLD/DAZ on the first three, so one frame
  shows a DRY+BRN token. Both chips render side by side, fully opaque, with the ammo pip row clear
  above the body — the claim is checkable from the frame instead of taken on trust.

---

# PROGRAM CROSSCUT — L3 "THE COMPOSED-TREE LADDER" (2026-08-30, lead) — THE PROGRAM'S CLOSE

**Base commit `d814f0c`** — all eight waves merged. 960 campaigns, 6 rungs x 8 disjoint slot sets
x greedy+sloppy = **160 per rung**, all 48 chunks `OK runs=20`. Full tables and method:
`docs/measurements/l3/README.md`.

**The first ladder this project has measured on a tree carrying every wave of its own program.**
X2 closed by finding that fourteen consecutive waves had each published a ladder measured on their
own branch point and nobody had ever measured the composition. This is the standing answer.

| rung | L3 | +-SE | L2 | delta | band | verdict | step |
|---|---|---|---|---|---|---|---|
| RECRUIT | **71.2** | 3.6 | 72.5 | -1.2 | 75+-8 | in | - |
| heat 0 | **47.5** | 3.9 | 46.9 | +0.6 | 55+-8 | in (at the floor) | -23.8 |
| heat 2 | **31.2** | 3.7 | 36.9 | -5.6 | 40+-8 | below by 0.8 | -16.2 |
| heat 4 | **23.8** | 3.4 | 23.1 | +0.6 | 30+-8 | in | -7.5 |
| heat 6 | **20.0** | 3.2 | 20.6 | -0.6 | 20+-8 | in, on target | -3.8 |
| heat 8 | **6.9** | 2.0 | 8.8 | -1.9 | 10+-5 | in | -13.1 |

## 1. The headline is how LITTLE moved

Five of six rungs in band, monotone at every step, and **the largest move at any rung across eight
waves is 5.6 points** — four of six moved by under 2. A program that repaired sixteen defects,
severed the gameplay RNG from the frame rate, restructured the post-FX pipeline, changed enemy
behaviour and reshaped four screens moved the difficulty ladder almost not at all.

That is not luck. Every wave that touched gameplay priced itself against a fresh same-slot baseline
on its own tree, and every wave that claimed inertness proved it. The composition confirms each of
them. **The discipline was the deliverable; the ladder is the receipt.**

## 2. The flat middle, replicated a THIRD time

`h4 -> h6` is the smallest step on all three ladders (-3.8 / -2.5 / -3.8), measured on three
disjoint world sets by two different instruments, and on the composed tree `h2 -> h4` joins it at
-7.5 while the ends buy -23.8, -16.2 and -13.1. `Heat.Mods`: **rung 8 is the only entry carrying
`DmgDelta` or `AiTier`.** The ladder's LEVEL is fine; its SHAPE is not, and the cause is a static
table. This is the best-replicated open finding in the project and the obvious next wave.

## 3. The within-run curve became a ramp

m1 98 / m2 83 / m3 77 / m4 78 / m5 73 / **m6 68** — monotone apart from a 1-point wobble inside its
own error. L1's was **U-shaped**; X2's entire finding was a mission-1 failure hiding behind a rung
average. The opening is safe and difficulty climbs to the finale, which is the shape DESIGN 3.D
asks for.

## 4. The mid-run Decapitate, replicated a third time

Finale 68.0% (n=472) against mid-run **44.7% +-3.9** (n=161) — **23.3 points harder than the
climax**, after L1's 48.9/70.1 and L2's 46.0/69.7. W8 refuted the mechanism the lead proposed (the
buffed half is *easier*) and located it in the FORCE: `Mission.Build` de-stacks the finale by 3-4
bodies and resets `bump`; no mid-run Decapitate gets that. **Unspent, and it is the next lever.**

## 5. What this round did NOT do

- **No lever, no game code.** Same discipline as L1 and L2.
- **A pooled objective row is not safe to read.** W8 proved one can hide a 49.5-point artifact
  (`Eliminate` reads 89.1% pooled and ~40% over its mid-run cells). Use the cross-tab.
- **heat 2's 0.8-point miss is unexplained** and is a fifth of a standard error. Recorded, not
  repaired — repairing it inside a measurement round is the mistake this round exists to avoid.
- **The first attempt produced ZERO chunks** because a shared-scratchpad copy of the runner had
  been overwritten by another agent. It failed loudly and wrote no data. The runner now lives in
  the repo, and that is the wider lesson: a shared path is not storage.

---

# PROGRAM CONTOUR — wave C6 "SHIPS LIKE A PRODUCT" (2026-08-30, branch `wave/ships`, base `17934ee`)

## THE THESIS

"Builds clean and passes autoplay" is a **development** standard. A **product** standard is a
fresh machine, an empty profile, a double-clicked binary in a directory nobody chose, and no
forgiveness. Nine autonomous programs made this game good; none of them walked that path. The one
time anyone checked a neighbouring case (RESONANCE F1) they found a published build launched from
the wrong directory **silently lost its font** — a total-failure bug that every self-test in the
suite passed straight through.

The mechanism behind that, and behind most of what this wave found, is one sentence:
**every self-test in this project runs from the source tree.** `Cfg.AssetPath` falls back to a
cwd-relative path when the baked path is missing — deliberate (dev convenience, dropped-in audio)
and also a **mask**: from the repo root, a file the `.csproj` forgot to copy still resolves, off
the repo instead of off the build output. The build is broken for a player and green for you.

## WHAT THE WALK FOUND — six defects, none of which any of the 64 existing self-tests could see

**1. Every distributable this repo has ever produced shipped with NO statement of its own terms.**
PROGRAM CROSSCUT decided the licence (all rights reserved, explicitly not restricting compiled
builds) and committed a root `LICENSE`. Nothing ever copied it into the output. A recipient of the
zip could read what raylib and Noto Mono permit and had **nothing at all** telling them what *this*
permits. One `<None Include="LICENSE" .../>` line; the manifest leg of the new test is what keeps
it fixed (it reads `FAIL (missing:LICENSE)` without it — that is how the defect was found).

**2. `display.json` was not written atomically**, while `save.json` and `meta.json` were.
`docs/DISTRIBUTION.md` §5 has claimed ".tmp then rename, so a crash mid-write cannot tear a save"
for the whole player-data directory since W5. It was true of two files out of three, and false of
the one written most often — every volume drag, every toggle, every one-shot tip dismissed.
All three now go through one writer, `SaveGame.WriteAtomic`.

**3. `SIGHTLINE_SAVETEST` fails on any machine whose profile owns `MetaUnlock` ordinal 1**, and
SAVETEST is on the shipping gate. Its meta block stashed the player's `meta.json` but never
*cleared* it, then asserted `if (HasUnlock(1)) fails.Add("metaUnlockPhantom")` — a claim about the
**absence** of an unlock, read off whatever profile happened to be on the machine. Ordinal 1 is
`StartBoon` (STANDING ORDERS, 70 salvage, the second-cheapest unlock in the game). So a maintainer
who has bought it runs `scripts/publish.sh`, gets `SAVETEST: FAIL (metaUnlockPhantom)`, and the
script **refuses to publish a perfectly correct build** because of their own save file. `METATEST`
already deleted `meta.json` first; SAVETEST now does the same. *Found the way a player would find
it: with a pre-existing profile sitting in the config directory.*

**4. There was no version anywhere.** A bug report could not name a build. `Hud.cs` even carried
the comment "a faint version/footer stamp" above a line with no version in it. `<Version>` in the
`.csproj` is now the single source, read back off the assembly by `Ship.Version` (never a
hard-coded second copy) and painted on the main-menu footer and the pause card's top-right corner.
Stamped **v1.0.0**.

**5. The two screens a new player meets first had never been photographed.** Every screenshot hook
stages a rich profile: `SIGHTLINE_INTRO` **fabricates a mission-3 save** so the CONTINUE button can
be framed, and `DebugWarRoom` hard-codes a twelve-run career with five legends and two owned
unlocks. So the cold main menu and the zero-state WAR ROOM had no photograph and no coverage.
`SIGHTLINE_COLD=1` now selects the genuine first-launch state of both.

**6. `.gitignore` reached `main` as an unresolved merge conflict.** Commit `17934ee` literally
contains `<<<<<<< HEAD` / `=======` / `>>>>>>> wave/first-hour` as committed lines. Git reads them
as three harmless patterns so nothing ever failed — but it is the second file a stranger opens, and
the wave whose job is "what does a stranger receive" is the right one to fix it. Both sides kept.

## THE PublishTrimmed HAZARD — and a stale line that pointed the wrong way

CLAUDE.md said **"Never publish with `-p:PublishTrimmed=true`: it destroys save/load while the game
still boots."** That was **stale and actively harmful.** F1 fixed the hazard (source-generated JSON
contexts + `TrimmerRootAssembly` + re-enabled reflection fallback), trimmed has been the
*recommended default* ever since, and following that line would have cost 57 MB and the fastest
start in the matrix. Corrected.

What IS true is that a **bare `dotnet publish` skips the verification** — which is exactly how a
totally broken build once looked green. Three things now stand where a comment used to:

- `scripts/publish.sh` runs `SAVETEST` + `METATEST` + **`SHIPTEST`** against the binary it just
  built and refuses to report success otherwise.
- `Sightline.csproj`'s `C6GuardTrimmedPersistence` target makes removing either mitigation a
  **build error**. Proven non-vacuous: `dotnet publish -p:PublishTrimmed=true
  -p:JsonSerializerIsReflectionEnabledByDefault=false` now fails with the reason and a §3 pointer.
- SHIPTEST's TRIMSAFE leg asks each source-generated context whether it can actually see
  `RunDto` / `MetaDto` / `Display.Dto` — the half of the hazard MSBuild cannot possibly know about,
  checked in the *untrimmed* build where everyone develops.

**Honest scope:** the guard catches *removal of the mitigation*, not every way persistence can
break. A DTO reachable from a root but with an unsupported member shape still needs the publish
script's printed lines. Do not judge a publish by the build log.

## THE NEW SELF-TEST — `SIGHTLINE_SHIPTEST` (wired into `qa-sweep.sh` AND `publish.sh`)

Six legs, all aimed at the seam between the artifact and the machine it lands on:

| leg | what it asserts |
|---|---|
| MANIFEST | all 8 entries of `Ship.RequiredFiles` resolve **strictly** against `AppContext.BaseDirectory` — the cwd fallback is explicitly not allowed to carry them — non-empty, and the two `.ttf`s carry a real sfnt magic. (**Six** at first draft, omitting both `CREDITS.txt`; see the review section.) |
| NOTICES | the shipped `THIRD-PARTY-NOTICES.txt` actually **names** all 5 redistributed components — each keyed on a string unique to its own section — and all 3 of their licences, not merely exists. (First draft called all eight "components" and keyed raylib on the bare substring `"raylib"`, trivially satisfied by `"Raylib-cs"`.) |
| PROFILE | the player-data dir is an absolute path; a profile written through the real public API reads back off disk **and appears in the file's raw bytes**; and (3b) **a second process of this same binary** banks a sentinel that this one reads back |
| ATOMIC | `save.json` / `meta.json` / `display.json` are each written by rename, proven **by mechanism** (below) — safe against process death and concurrent readers, **not** power loss (no `fsync`), Unix-only probe — plus a forced-failure probe that a failed write sweeps its own `.tmp` |
| TRIMSAFE | every persisted DTO is reachable from a source-generated JSON context |
| VERSION | the assembly carries a `MAJOR.MINOR.PATCH` stamp and the HUD's label contains it |

**How the atomicity leg works, because "I read the code and it looks atomic" is not a test.** Put a
marker in the target, hold an open read handle across a real save, then read *through that handle*.
A rename streams into a NEW inode and swaps the directory entry, so the old handle keeps seeing the
old bytes; a truncate-in-place writer rewrites the SAME inode and the handle sees the new bytes.
That difference **is** atomicity. Unix-only mechanism, so it self-skips on Windows rather than
reporting a verdict it cannot reach.

**The second-launch leg is a real fork of the shipped binary.** No hook in this project could ever
check "quit the game, start it again, your progress is there", because the house rule (never touch
the player's real profile) makes every hook stash-and-restore *inside one process*. SHIPTEST forks
`Environment.ProcessPath` with `SIGHTLINE_SHIPCHILD=1`; the child banks a sentinel through the
ordinary public API and exits; the parent reads it back off disk. Two processes, one player-data
directory, the shipped code on both sides — and it works from the published **single-file** binary.

> **I fork-bombed the shared container doing this and it is worth writing down.** The first version
> put the child branch *below* the SHIPTEST branch in `Main`, and the child inherited
> `SIGHTLINE_SHIPTEST=1`. The child ran SHIPTEST, which forked a grandchild, which ran SHIPTEST…
> **184 processes** on a box shared with five other agents before it was killed. Two independent
> guards now stop it and both are commented as load-bearing: the child branch is **first in
> `Main`**, and the probe **strips every `SIGHTLINE_*` variable** from the child's environment
> (`XDG_CONFIG_HOME` is not one, so the child stays in the same isolated directory). A self-test
> that spawns itself needs two guards, not one.

### Proof it FAILS pre-fix — five independent reverts, all run

| revert | SHIPTEST said |
|---|---|
| remove `<None Include="LICENSE">` **and** revert `Display.Save` to `File.WriteAllText` | `FAIL (missing:LICENSE,displayNotAtomic:handleSawNewBytes)` |
| remove the version from `Hud.IntroFooter` | `FAIL (versionNotPainted)` |
| delete `LICENSE` + `assets/NotoMono-LICENSE.txt` from a **published directory** | `FAIL (missing:assets/NotoMono-LICENSE.txt,missing:LICENSE)` |
| delete `assets/NotoMono-Regular.ttf` from a **published directory** | `FAIL (missing:assets/NotoMono-Regular.ttf)` — **the exact F1 bug**; the game still boots, prints one `WARNING:` line and silently falls back to the bitmap font |
| stop the second-launch child writing | `FAIL (secondLaunch:salvageNotCarried)` |

And separately, for fix 3: with a profile owning `StartBoon`, pre-fix
`SAVETEST: FAIL (metaUnlockPhantom)`; post-fix `SAVETEST: PASS`, and `meta.json` byte-identical
afterwards.

## THE MEASURED PUBLISH MATRIX (re-measured from scratch, 2026-08-30)

Interleaved — one launch of each mode per round, nine rounds — so all four see the same load on a
container shared with five other agents. A *sequential* pass twenty minutes apart put `no-trim` at
117 ms and then 184 ms, which is why the ORDERING is the result and the absolute numbers carry
"on a loaded four-core box".

> **SIZES SUPERSEDED — see "the corrected matrix" at the end of this section.** The review fixes
> added ~0.9 MB of code, and this draft never stated whether MB meant 10^6 or 2^20 (it was 10^6 for
> the directory and MiB in the README, which is the F6 defect). The START column below is still the
> one of record and is attached to commit `d3feb90`.

| mode | exe | whole directory | files | start (median of 9, min–max) |
|---|---|---|---|---|
| **release** (default) | 25.6 MB | 28.4 MB | 10 | **169 ms** (102–323) |
| small | 15.8 MB | 18.5 MB | 10 | 545 ms (397–696) |
| no-trim | 82.3 MB | 85.1 MB | 10 | 206 ms (144–288) |
| plain | 68.3 MB | 71.0 MB | 10 | 317 ms (256–405) |
| win-x64 release | 24.3 MB (`.exe`) | 26.4 MB | 10 | not runnable here |

On a quiet box the default measured **108–114 ms** median-of-10. File count went **6 → 10** since
F1 (the Chakra Petch pair and two `CREDITS.txt` ledgers from later waves, `LICENSE` from this one),
and F1's `size` column was the *executable* where this table carries both that and the directory
you actually hand someone. `--rid win-x64` cross-publishes cleanly from Linux (10 files,
`Sightline.exe` + `raylib.dll` + the same asset and licence set); **its self-tests cannot be run
here, so the Windows build is unverified beyond "it produces the right files."**

## THE PLAYER'S PATH, WALKED

Installed the published directory at `/home/user/player path/SIGHTLINE Game` — outside the source
tree, **with a space in the path** — against an **empty** player-data directory:

- Both font atlases load **by absolute path from the install directory** (`INFO: FILEIO:
  [/home/user/player path/SIGHTLINE Game/assets/NotoMono-Regular.ttf] File loaded successfully`).
- A full campaign runs: `RESULT: WIN mission=6 frame=7654 turns=19`.
- `SHIPTEST` PASSes from that install, and afterwards the profile directory is **empty** —
  every file the test wrote was restored away.

### Crash and data safety, measured rather than asserted

**Full disk** (48 KB tmpfs at 100%, a good `meta.json` already in it, live non-`NoPersist` mission 1
through the published binary): **no exception, the game plays on, and `meta.json` is byte-identical
afterwards.** A second pass with SHIPTEST on the same filesystem shows every write refused
(`IOException` / "did not land") with the process still standing. The game degrades to *cannot
save*, not to *lost your profile*.

That pass also found a real thing: on `ENOSPC` the old code leaves a **0-byte `<name>.json.tmp`
behind forever**, contradicting the "should never persist" line in DISTRIBUTION §5. Measured side
by side — old shape leaves `old.json.tmp len=0`, `WriteAtomic`'s sweep leaves nothing.

**Corrupt / truncated / future-version saves and meta** are covered by SAVETEST's three corruption
legs, which `publish.sh` now runs **against the published binary**; all PASS. **`kill -9` mid-write
is NOT directly tested** — the write window is sub-millisecond and racing it from a shell is a coin
flip, not a test. What is tested is the property that makes the outcome safe (the rename), on all
three files. Stated as a limit, not dressed up as a pass.

## SCREENSHOTS — taken from the PUBLISHED binary, cold profile, and judged

- **Cold main menu** (`SIGHTLINE_INTRO=1 SIGHTLINE_COLD=1`). Correct zero-state: no CONTINUE RUN,
  DEPLOY SQUAD is the lit primary, caption reads "NEW CAMPAIGN – draft a squad, pick a doctrine,
  survive 6 operations". `SIGHTLINE v1.0.0` sits in the footer, faint, centred by measurement.
  Nothing broken or empty. **Judged good.**
- **Cold WAR ROOM** (`SIGHTLINE_WARROOM=1 SIGHTLINE_COLD=1`). Real zero-states everywhere: HALL OF
  FAME collapses to "– no legends yet – / Finish a run to enshrine them", achievements show `0/N`,
  CAREER's WIN RATE is `–` rather than `0%`, BUY chips are unaffordable-red. **But four of the six
  unlock descriptions are ellipsised mid-word** — see "what I did not fix".
- **Pause card at TEXT SIZE 120%.** The version sits right-aligned in the card's empty top-right
  corner, clear of the title and every row. **Judged good.**
- **Cold main menu at 120%.** Footer stays centred and fits. **But the DIFFICULTY panel's body
  text overflows its own panel** — see below.
- **Barracks debrief + campaign map after mission 1** (the genuine first-run state). Roster with
  HP bars, DEPLOY/BENCH toggles, DEBRIEF lines, a legible DAG with a legend. **Judged good.**

## VERIFICATION

- `dotnet build -c Release` — **0 warnings / 0 errors**.
- `bash scripts/qa-sweep.sh --full` — **SWEEP-EXIT=0**, **65/65** self-tests (64 + SHIPTEST), zero
  `FAIL`, COVERAGE GAP block empty, `PAIRTEST: PASS` (CRN byte-identity intact).
- Autoplay ×3 in the sweep: no TIMEOUT, no blank RESULT.
- `bash scripts/publish.sh` green on all four Linux modes plus `--rid win-x64`, with all three
  self-tests PASSing against each published binary.

## WHAT I DID NOT FIX, AND WHAT IT COSTS

**1. The cold WAR ROOM ellipsises four of six unlock descriptions.** With 0 owned, the six-entry
catalogue wants 508 px in a 446 px column, so W9's `WarUnlockPlan` shrinks each compact row to ONE
12 px line and clips with an ellipsis. That is a **deliberate, documented W9 decision** and the
alternatives it rejected are worse (10 px type breaks the small-text floor; `break`ing off the list
made an unlock invisible *and unbuyable*). The cost is real and lands on exactly the wrong person:
**the only player who sees all six unowned is the brand-new one, and they are the one player who
cannot read what any of them do.** METATEST checks the font size and the hit rect, not
*readability*. Not fixed here because it is a HUD layout change on a screen other CONTOUR waves may
be touching, and this wave's lane is the artifact. It is now **photographable**
(`SIGHTLINE_COLD=1`), which it was not before — that is the durable half.

**2. The intro screen is not fit-tested, and it overflows at TEXT SIZE 120%.** The DIFFICULTY
panel's body line ("standard difficulty – the designed fight") paints to x≈1266 against a panel
edge at x≈1239 — **outside its own panel** — and the `[K]` / `[U]` key chips overlap their labels.
`FITTEST` covers the doctrine / armory / hall-of-fame / draft screens; **the intro is not in its
list**, so the first screen in the game has never been checked at any scale but 100%. At 100% it is
clean. Reported precisely rather than fixed, same reason as (1).

**3. A genuine two-process check of `display.json` is impossible headlessly.** `Display.Init` calls
`Load()` *after* its `if (!enabled) return`, and every shot/autoplay path runs `Init(false)` — by
design, for headless byte-stability. So settings load-back cannot be verified without a human at a
keyboard. SHIPTEST's second-launch leg closes the equivalent gap for `meta.json`, which is where
permanent progress lives; `display.json` is covered only up to "the file is written correctly".

**4. `docs/screenshot.png` was NOT regenerated.** It is current — mission 4/6, HVT GUARDED, a
board this build still produces — so the brief's condition ("regenerate it if the game no longer
looks like it") is not met. It is a *worse* hero image than it could be (the 11-second briefing
card covers the middle of the board), but swapping 700 KB of committed binary for a taste
preference is not this wave's call to make silently.

**5. The Windows build is unverified beyond its file list.** It cross-publishes cleanly and carries
the right ten files; nothing here can run it. `Ship.SecondLaunchProbe` and `AtomicityProbe` both
have Windows-aware paths (the latter self-skips), but neither has been executed on Windows.

**6. Nothing here was balance-measured, and nothing should have been.** No gameplay code changed —
`PAIRTEST` byte-identity confirms it. The `SIGHTLINE_COLD` hook is shot-only and `NoPersist`-gated;
the version string is presentation. Quoting a ladder number from this wave would be inventing one.

## WHAT STILL STANDS BETWEEN THIS AND A BUILD YOU WOULD HAND A STRANGER

Ordered by how likely a stranger is to hit it.

1. **No installer, no icon, no window-title art, no `.desktop` file.** A Linux player unzips a
   directory and runs a file called `Sightline`. Windows will show an unsigned binary and
   SmartScreen will warn. **Code signing costs money** and is therefore permanently out of scope
   under this project's rules, but it is the single biggest "is this safe to run?" barrier there.
2. **The cold WAR ROOM's unreadable unlock list** (above) — the first-run information defect.
3. **The intro at non-100% text size** (above) — a shipped comfort setting with an unfit screen.
4. **No crash reporter and no log file.** If the game throws on a player's machine the exception
   goes to a stdout nobody is reading. The version stamp now lets them *name* a build; there is
   still nothing to attach to the report.
5. **Audio has never been heard on real hardware** (RESONANCE's standing item). Every session here
   runs with `WARNING: AUDIO: Failed to initialize playback device`.
6. **macOS is entirely untested** — not even a cross-publish was attempted.
7. **`meta.json` has no export or backup path.** It is the only permanent thing the player owns;
   the `.bak` beside it is corruption evidence, not a restore.

## C6 — SENT BACK TWICE, AND WHAT THE TWO REVIEWS FOUND

Two independent reviews. **Neither could break the test design** — reviewer 1 reproduced all five
named reverts plus three more of their own and could not defeat the fork-bomb guards under
`ulimit -u 250`, from multiple working directories, from an install path with a space, under
`dotnet run -c Debug`, or nested; reviewer 2 verified trim-safety at the metadata level and upheld
the `PublishTrimmed` reversal more strongly than the wave had argued it. What failed was
**precision and bookkeeping**, which is this project's stated worst sin, plus two functional gaps.
Twelve items, all fixed. The ones worth remembering:

**The manifest did not match the document it guards — found independently by BOTH reviewers.**
`docs/DISTRIBUTION.md` §1, written by this wave in the same commit, called the output "ten files,
and you must ship all of them" and listed both `CREDITS.txt` audio ledgers. `Ship.RequiredFiles`
had **six entries and omitted both**, so a build output with both deleted read `SHIPTEST: PASS` —
while the array's own doc comment said "the two halves disagreeing is precisely the failure this
manifest exists to name." **They already disagreed at merge time.** A manifest that does not match
its document is worse than no manifest, because it gets quoted as evidence. Now eight entries.

**I shipped a leg that could not fail — rule 5, in the wave that quotes rule 5.** The
`<label>StaleTmp` check ran only after a *successful* `WriteAtomic`, whose `File.Move` has already
consumed the tmp; it asserted a tautology, a pre-seeded stale `.tmp` still read PASS, and the PASS
string advertised "no stale `.tmp`" on the strength of it. The sweep is a property of the **failure**
path and had to be probed there: `SweepProbe` makes the target a **directory**, so the tmp write
succeeds and the rename cannot, landing control in the catch with a corpse on disk. Remove the
sweep and it reads `sweep:tmpNotSwept` — verified. The old line survives only as a shape guard,
renamed `TmpSurvivedSuccessfulWrite`, and the PASS string no longer claims anything for it.

**My probe destroyed a file it did not create.** `Ship.Restore` deleted `<path>.tmp`
unconditionally, so running `qa-sweep.sh` silently erased a `.tmp` left behind by a real crash —
the one artefact a maintainer could reason about that crash from. It now stashes the `.tmp`
siblings alongside the files and puts their bytes back (mtime is necessarily rewritten: the
production writer uses that exact path, so the test cannot avoid clobbering it and can only restore
the content). Verified: two pre-seeded crash `.tmp` files come back byte-identical.

**I reintroduced the bug class this wave exists to kill.** `SecondLaunchProbe` used
`Environment.ProcessPath` unconditionally. Under `dotnet Sightline.dll` that is the **dotnet
muxer**, so it spawned a bare `dotnet`, which printed usage, exited 0, wrote no sentinel, and gave
`SHIPTEST: FAIL (secondLaunch:salvageNotCarried)` **on a completely correct build** — the same
shape as the `metaUnlockPhantom` defect I had just fixed on the same gate, and defended with the
same excuse ("no current caller trips it"). It now identifies the launcher: apphost/single-file →
use it; muxer → relaunch as `<muxer> <our .dll>`; neither identifiable → **skip with the reason
printed in the PASS string**, never fail. Verified PASSing under `dotnet Sightline.dll`,
`dotnet run`, and the published single-file binary.

**My hermeticity fix made a failure mode worse.** SAVETEST's meta stash lived only in a local
string while the real file was deleted, so a `kill -9` in that window lost the profile outright.
Pre-C6 the file was merely *mutated*; post-C6 it was *absent*. Trading "polluted" for "gone", in
the wave whose thesis is that player data survives a crash. The stash is now a real file moved
aside by rename (`<name>.json.selftest-stash`, recoverable by hand) and moved back the same way,
with `WriteAtomic` as the fallback restore instead of a bare `File.WriteAllText` — the exact
pattern this wave removed everywhere else.

**Two over-claims, both on the sentence next to the thing I did not cover.**
- §5 said `*.json.tmp` "should never persist, **and now does not**". The sweep is in the `catch`, so
  a `kill -9` or power cut *between* the tmp write and the rename still leaves one, and there is no
  startup sweep — deliberately, because that corpse is crash evidence. Corrected to name the limit.
- The atomicity probe proves the **inode swap**, which covers concurrent readers and process death.
  It is **not** power-loss durability: that needs an `fsync` of the tmp and of the directory, and
  this code does neither. Now stated in the PASS string itself, not just the docs.

**My guard's error text was factually false** — and it is the text someone reads at their worst
moment. It said removing `TrimmerRootAssembly` meant a trimmed build "saves nothing". Reviewer 2
measured it: with the root removed, `SAVETEST` **passes** and runs round-trip correctly; with
**both** knobs removed, `SAVETEST`/`METATEST`/`SHIPTEST` all still pass. **Player persistence is
protected by source generation alone.** What the knobs keep alive is the `SIGHTLINE_BALANCE`
telemetry export (`NotSupportedException: parameter names have been trimmed by ILLink` /
`InvalidOperationException: Reflection-based serialization has been disabled`). The guard was right
to exist and fails safe; its message described the *pre-source-generation* failure and would have
sent the next maintainer down the wrong road.

**"Bounded wait" was stronger than my code.** `SecondLaunchProbe` called `ReadToEnd()` on two
redirected pipes *before* `WaitForExit(60_000)`, and `ReadToEnd` has no timeout — a child that
filled a pipe would have hung the sweep forever. It cannot happen today (the child writes ~15 bytes
and exits), but the comment claimed more than the code delivered. Both pipes now drain on
background threads before a deadlined wait.

**A false comment on a line that deletes a save.** The `SIGHTLINE_COLD` block said it was
"NoPersist-gated … so it can never touch a real profile". The cold path calls `SaveGame.Delete()`,
which has **no** `NoPersist` guard. What actually protects the player is FUL-2's `introStash`
capture-and-restore — which already had to exist because the *non*-cold path clobbers the same file
by writing a staged mission-3 run over it. Net risk unchanged, stated reason wrong.

**Two of my own documents disagreed on a number readers quote.** README said "27 MB" (MiB, from
`du -sh`); §2 and `publish.sh` said 28.4 MB (decimal) for the same directory. All three now say
**MB = 10^6 bytes**, explicitly, with exact byte counts.

### The corrected matrix (final C6 binary, MB = 10^6 bytes)

| mode | executable | whole directory | files |
|---|---|---|---|
| **release** (default) | 26.5 MB | **29.3 MB** (29,284,496 B) | 10 |
| small | 16.1 MB | 18.8 MB | 10 |
| no-trim | 82.3 MB | 85.1 MB | 10 |
| plain | 68.3 MB | 71.0 MB | 10 |
| win-x64 release | 24.8 MB (`.exe`) | 26.9 MB | 10 |

**The start-time column from the first draft still stands and is NOT reproduced here as if it were
fresh.** Those timings were measured on the pre-review binary (`d3feb90`); they were deliberately
not re-run, because the container was under nine concurrent reviewers at load ~60 and that produces
a worse number, not a truer one. `docs/DISTRIBUTION.md` §2 carries them with that attribution.

### Also now stated as unverified rather than quietly implied

- The `display.json` mechanism change applies on **all** platforms; `AtomicityProbe` self-skips on
  Windows, so the Windows behaviour of that change is **unverified**.
- **`PublishAot` was not tested.** By inspection it sets `PublishTrimmed` and therefore trips the
  same guard — inspection, not measurement.
- `SIGHTLINE_SHIPTEST` is the first hook here that writes the live profile **from a second
  process**. It restores cleanly on success, but a sweep killed mid-SHIPTEST can leave `4242`
  salvage and a `C6_SECOND_LAUNCH` achievement in whatever profile `XDG_CONFIG_HOME` points at.
  CLAUDE.md's isolation section now says so, and names the two "a self-test died holding your data"
  filenames and what to do with each.
- Dispatch-order quirk, noted not changed: `SIGHTLINE_BALANCE` is handled before `SHIPTEST` in
  `Main`, so setting both silently runs a balance batch and prints no SHIPTEST line. First-match-
  wins is the house convention for every hook in that file; `qa-sweep.sh`'s `verdict` already treats
  a blank capture as a failure.

# PROGRAM "CONTOUR" — wave C1 "THE FLAT MIDDLE"

**Branch `wave/flat-middle`, base `17934ee`.** Brief: the `h4 -> h6` step is the smallest on three
measured ladders while the ends buy 10-24 points apiece; give the middle of the heat ladder a tooth
the player could describe in one sentence after the mission, measure it CRN-paired, and do not
break the LEVEL while fixing the SHAPE.

> **SUPERSEDED IN PART BY §C1-R (the send-back). READ BOTH.** This section is the original wave
> write-up and it concludes for **mode 1**; the shipped default is **mode 3**, changed on review
> evidence recomputed from this same archive. Two claims below are formally withdrawn there
> ("tier 2 is not a difficulty lever", "nothing that was in band left it"), the measurement and
> mechanism sections stand unchanged, and the shape verdict is restated. Quoting §C1-3/§C1-4 without
> §C1-R will give you the wrong shipped mode and the wrong shape conclusion.

**Shipped (as revised in §C1-R):** `Heat.MidTooth` (default 3) — BOTH of NO QUARTER's qualitative
teeth, the +1 per-hit damage and coordination tier 2, move down from rung 8 to EXPOSED (rung 6),
and rung 6's dead `AiTier = 1` declaration is deleted. `SIGHTLINE_MIDTOOTH=0` restores the pre-C1
table. New hook `SIGHTLINE_MIDTOOTHTEST`. Raw round and full tables:
[`docs/measurements/c1/`](measurements/c1/README.md). **416 chunks, every one asserted `runs=40`,
zero `BAD` — 16,640 campaigns.**

## C1-1. Sample the ladder per RUNG and the finding changes shape

Every published ladder in this project samples `{RECRUIT, 0, 2, 4, 6, 8}`. C1 measured **all ten
rungs** at n=320 (n=640 at h6), eight disjoint CRN slot sets each, on its own tree:

| rung | RECRUIT | h0 | h1 | h2 | h3 | h4 | h5 | h6 | h7 | h8 |
|---|---|---|---|---|---|---|---|---|---|---|
| win% | 70.0 | 44.4 | 37.5 | 32.5 | 27.5 | 20.9 | 20.9 | 18.6 | 12.2 | 8.1 |
| +-SE | 2.6 | 2.8 | 2.7 | 2.6 | 2.5 | 2.3 | 2.3 | 1.5 | 1.8 | 1.5 |
| **rung N buys** | - | 25.6 | 6.9 | 5.0 | 5.0 | 6.6 | **0.0** | **2.3** | 6.4 | 4.1 |

"`h4 -> h6` is flat" is really **two rungs, and one of them is exactly zero**. Rung 5 (LINGERING
WOUNDS: +1 enemy, HarshAttrition) buys **0.0 +-3.2** — the only rung on the ladder that cannot be
distinguished from doing nothing. Rung 6 buys 2.3 +-2.7 against a ladder whose other six rungs
average 5.7.

## C1-2. The mechanism: a declaration that could never fire

Rung 6 read `Exposed = true, StatDelta = 1, AiTier = 1`. `Heat.AiTier` aggregates with `Math.Max`
and **rung 4 (ELITE CADRE) already publishes tier 1** — so EXPOSED's advertised coordination tooth
was a **dead declaration** for two whole programs. W6b wrote it; its own comment ("rungs 6-7 stay
tier 1") records the fact without noticing the consequence. Nothing caught it because
`HEATLADDERTEST` pins the CUMULATIVE vector, which a dead declaration by definition does not move.
Rung 6 shipped +1 body, +1 stat and a concealment flag, and the player climbed two rungs for a stat
point.

## C1-3. Five candidates, all provably apex-neutral, priced at heat 6

`Heat.MidTooth` is a bitfield (1 = +1 damage moves 8 -> 6, 2 = coordination tier 2 moves 8 -> 6,
4 = rung 6's stat point moves 6 -> 7), so one binary measured every candidate against one control.
**DmgDelta sums and AiTier is a Math.Max, so the cumulative vector at heat 8 is IDENTICAL in every
mode** — the dial cannot push the apex under its >=5 hard floor, by construction rather than by
hope. Verified, not asserted: the h8 chunks are byte-identical control-vs-lever.

| mode | what moves | n | h6 ctl -> lev | paired delta | +-SE | z |
|---|---|---|---|---|---|---|
| **1** | **+1 dmg -> rung 6** | **640** | **18.6 -> 12.0** | **-6.6** | **1.6** | **-4.16** |
| 3 | +1 dmg AND tier 2 -> rung 6 | 640 | 18.6 -> 13.9 | -4.7 | 1.7 | -2.69 |
| 2 | tier 2 -> rung 6 alone | 320 | 18.4 -> 20.0 | **+1.6** | 1.9 | +0.80 |
| 4 | rung 6's stat point -> rung 7 | 320 | 18.4 -> 23.1 | +4.7 | 2.7 | +1.76 |
| 5 | mode 1 + mode 4 (the "trade") | 320 | 18.4 -> 10.6 | -7.8 | 2.5 | -3.10 |

**The negative result is the more interesting half.** Moving the apex's coordination tier down a
rung is **not a difficulty lever**: alone it measured **+1.6 in the PLAYER's favour**, and mode 3
(which bundles it with the damage tooth) is 1.9 points *softer* than mode 1 alone. Two programs of
table comments call `AiTier` an escalation; over 320 CRN pairs at heat 6 it does not read as one.
Caveat, per the program's own rule 3: **a CRN round prices CONSEQUENCES and is blind to FEEL.**
Tier 2 may well change how a fight reads without changing who wins it. Nobody has looked.

Mode 4 is the useful by-product: it **prices a single `StatDelta` rung at 4.7 points**, the first
time one has been isolated on this ladder.

**Why mode 1 and not mode 3**, which lands a more even 7.0/5.8 split across rungs 5-6 / 7-8: mode 3
strips NO QUARTER of *every* qualitative tooth (leaving the apex "+1 enemy, +1 stat" — the exact
complaint the wave opened with), and the component that buys the softening measured with the wrong
sign on its own. Mode 1 is the only candidate that leaves rungs 6, 7 AND 8 each with a tooth a
player could name. **The design thesis decided it; the numbers only narrowed the field.**

## C1-4. The shipped ladder — and what did NOT move

| rung | RECRUIT | h0 | h1 | h2 | h3 | h4 | h5 | h6 | h7 | h8 |
|---|---|---|---|---|---|---|---|---|---|---|
| control | 70.0 | 44.4 | 37.5 | 32.5 | 27.5 | 20.9 | 20.9 | 18.6 | 12.2 | 8.1 |
| **C1** | **70.0** | **44.4** | **37.5** | **32.5** | **27.5** | **20.9** | **20.6** | **12.0** | **9.4** | **8.1** |
| +-SE | 2.6 | 2.8 | 2.7 | 2.6 | 2.5 | 2.3 | 2.3 | 1.3 | 1.6 | 1.5 |
| discordant pairs | 0/320 | 0/320 | 0/320 | 0/320 | 0/320 | 0/320 | 5/320 | 102/640 | 35/320 | 0/320 |

**Six rungs are EXACTLY unchanged — zero discordant pairs out of 320 at each.** `h4 -> h6` goes
from **-2.3 +-2.7 to -8.9 +-2.6**. Band: nothing that was in band left it — h6 moves from mid-band
to **exactly its floor (12.0, band 12-28)**, and h0's 2.6-point and h4's 1.1-point misses are the
control's, untouched. The first n=320 round put h6 at 11.2, i.e. 0.8 *under* the floor; the doubled
n=640 round is the only reason the lever shipped, and it is why the second eight slot sets were run.

## C1-5. THE HONEST PART — the flat spot moved, it did not vanish

Rungs 7 and 8 now buy 2.7 and 1.2 where they bought 6.4 and 4.1. That is **arithmetic, not a design
failure, and it is the wave's real finding**: an apex-neutral lever pins h4 (20.9) and h8 (8.1), so
there are **12.8 points of win-rate for four rungs — 3.2 each.** No redistribution inside that
window can give rungs 5-8 the ~5.7 points per rung that rungs 1-4 buy. What a lever CAN do, and
did, is stop one rung taking almost none of it.

**The deficit that causes it sits ABOVE heat 4, not in the middle.** Against the band centres, h0
is 10.6 points low and h4 is 9.1 low, while h6 and h8 are within 2. The ladder is flat at the
bottom because its top half has sunk onto it. That is a BASE-difficulty lever, not a `Heat.Mods`
lever, and it was out of C1's scope. **It is the next thing to price, and it is now the biggest
open number.**

## C1-6. Two things measured on the way that nobody had written down

**A "heat-N rung" is not a fixed rung.** `Events.cs:460` (`EventOutcomeKind.AddHeat`) lets three
field-event choices raise a run's HeatLevel mid-campaign, +1 each — so a heat-5 cell contains some
heat-6 missions. It is the *only* reason the lever is not perfectly inert below rung 6: h5 shows 5
discordant pairs in 320 (-0.3, z=-0.45) and h4 shows zero, because from h4 the escalation must fire
twice and in 320 campaigns it never did. Every past "this rung is unaffected" claim on this ladder
has had this hole in it.

**Two rungs' player-facing copy was painting off the panel.** `Hud.DrawHeatSelector` renders a
rung's `Desc` at 12px into a 320px card with the body column at x+40, and it neither clips nor
wraps — it just keeps painting. Measured off a heat-8 intro screenshot: ~7.07 px/char, 278px inside
the border, **38 characters**. NO QUARTER's shipped line was **57** and LINGERING WOUNDS' was 42;
both were visibly cut off, and NO QUARTER's clipped clause meant the apex **never named its
coordination peak at all**. Found by looking at the screenshot, which is what the screenshot rule is
for. Both now fit, `Heat.DescBudget` records the measurement, and MIDTOOTHTEST asserts the budget
for every rung of every dial mode so it cannot come back.

## C1-7. The self-test — `SIGHTLINE_MIDTOOTHTEST`, and the proof it can fail

Seven legs, aimed at the SEAM rather than the model (W9's thesis):

- **(A) NO DEAD DECLARATION** — a rung declaring `AiTier = t` must actually RAISE the cumulative
  tier. *This is the defect.* **Under `SIGHTLINE_MIDTOOTH=0` it FAILS:**
  `MIDTOOTHTEST: FAIL deadAiTier@rung6 (declares 1, cumulative below is already 1)`.
- **(B) NO SILENT RUNG** — every rung must move the cumulative (enemy, stat, dmg, tier, flags).
- **(C) APEX NEUTRALITY** — all eight dial modes must leave the heat-8 vector identical. Proven able
  to fail by scratch-editing rung 8 to KEEP its DmgDelta (the add-instead-of-move mistake):
  `FAIL apexMoved(mode1)=(4, 4, 2, 2, 15) vs (4, 4, 1, 2, 15)`, on four modes at once.
- **(D) THE OFF-SWITCH IS A TRUE CONTROL** — `MIDTOOTH=0` reproduced field-for-field against a
  literal transcription (the TRUE BAND precedent). One deviation is named inside the test itself:
  rung 5's copy fix, which is copy-only and applies in every mode.
- **(E)** per-mode rung shapes as goldens. **(F)** the +1 damage reaching every heat-6 m3 hostile
  through the whole `SetupMission -> Build -> SpawnEnemies` thread, none at heat 5, exactly one at
  heat 8. **(G)** the 38-character panel budget, every rung, every mode.

`HEATLADDERTEST`'s golden and `AITEST`'s `DmgDelta(7) != 0` pin both had to move, and both are
re-aimed rather than deleted: AITEST now asserts the invariant that actually matters — the ladder
carries the damage point **exactly once**, so the apex cumulative is 1 and never 2.

## C1-8. What C1 did NOT do

- **Rung 5 is untouched and still buys 0.0.** C1 shipped ONE lever and it was rung 6's. LINGERING
  WOUNDS' +1 body is partly eaten by the 12-hostile spawn cap on late missions, and `HarshAttrition`
  compounds over a run length the bot rarely reaches (avgMis 3.66 at h5) — **hypotheses, unmeasured.**
- **The LEVEL was not addressed.** h0 (-10.6) and h4 (-9.1) against band centre are the control's
  and are untouched. C1 diagnosed them as the cause of the flat bottom and did not spend a lever on
  them, because the brief scoped the wave to `Heat.Mods` and a base-difficulty lever is not that.
- **No FEEL measurement of the tier.** Mode 2's +1.6 says tier 2 does not change who wins. It says
  nothing about whether the fight reads differently, and this instrument structurally cannot.
- **`avgMis` and `ch/ARMED` are reported but not analysed.** `ch/ARMED` at h6 moves 2.103 -> 2.087,
  which is noise; C1 makes no decision-density claim.
- **Objective cross-tabs were not run on the lever.** Every C1 claim is rung-level win-rate, so it
  did not need one — but a follow-up that wants to say *which fights* got harder at heat 6 must run
  `byObjectiveByMission` first (rule 1).
- **The 34 MB raw round is not committed.** It was distilled to a 428 KB per-campaign CSV that
  re-derives every published number exactly (one 0.01 rounding difference in `avgMis` at h3, where
  the CSV is the more accurate of the two). The aggregate blocks are gone: `inert_diff.py` cannot be
  re-run from the archive, and its results are recorded rather than reproducible.

---

# PROGRAM "CONTOUR" — C1-R: the send-back, and the mode changed on the evidence

C1 shipped mode 1 and was returned `merge-after-fixes` by two review lenses. The instrument,
archive and guard were upheld — a reviewer reproduced the h6 chunks **bit-exactly** from a fresh
build, corroborated apex-neutrality from the committed samples (h8 pair: 1535 leaf fields, **0
differ**; h6 pair: 1608 fields, **984 differ** — the h6 difference is what makes the h8 zero mean
something), and confirmed all seven MIDTOOTHTEST legs provably able to fail. **What failed was the
conclusion drawn from the numbers.** This section records what changed and why.

## C1-R1. THE SHIPPED MODE CHANGED: 1 → 3

Recomputed from C1's own `chunks.csv`, the rung 1–8 step profiles:

```
             h1    h2    h3    h4    h5    h6    h7    h8    sum
control     6.88  5.00  5.00  6.56  0.00  2.34  6.41  4.06  36.25
mode 1      6.88  5.00  5.00  6.56  0.31  8.59  2.66  1.25  36.25
mode 3      6.88  5.00  5.00  6.56  0.31  6.72  4.22  1.56  36.25
```

| metric | control | mode 1 (was shipped) | **mode 3 (now shipped)** |
|---|---|---|---|
| SD of the 8 steps (sample, n−1) | 2.36 | 2.89 | 2.44 |
| L1 deviation from even spacing | 14.38 | 18.75 | 15.00 |
| L1 deviation from the band's implied profile | 14.69 | 19.06 | 15.31 |
| L1 from even, inside the rungs 5–8 window | 8.12 | 10.78 | 9.06 |
| steps < 2.0 points | 1 | 2 | 2 |
| **sum of rungs 1–8** | **36.25** | **36.25** | **36.25** |

**The sums are identical to the decimal, which confirms C1's zero-sum claim — and is exactly what
makes the allocation a design choice rather than arithmetic.** C1's original write-up used
zero-sum-ness to explain away the flattened top; that was the wrong inference from a correct fact.
The ranking control > mode 3 > mode 1 is identical on all four dispersion metrics.

**The decision, and the four reasons:**

1. **Mode 3 wins every shape metric mode 1 loses**, and shape is what the wave is for.
2. **The band verdict.** Mode 1's h6 is 12.03 ±1.29 against a floor of 12: **P(true value below the
   floor) ≈ 0.49**, a coin flip. Mode 3's is 13.91 ±1.37 — 1.4 SE clear (P ≈ 0.08) — and in band at
   **both** n=320 (13.4) and n=640 (13.9).
3. **Mode 1's band verdict came from optional stopping.** The first n=320 round read 11.2 (out of
   band); the round was extended to n=640, read 12.0, and shipped. C1 disclosed the extension — but
   **disclosure makes optional stopping auditable, not unbiased.** Mode 3 never needed the rule.
4. **The tension C1 left implicit, resolved.** Mode 1's only advantage was leaving a qualitative
   tooth on the apex — and that tooth is `AiTier 2`, the one component C1 itself measured as doing
   nothing. **C1 cannot call tier 2 "not a difficulty lever" and simultaneously pay four shape
   metrics and a band verdict to keep it at the apex.** Mode 3 is also weakly DOMINANT across that
   component's full CI: at the bottom (−0.15) mode 3 ≈ mode 1; at the top (+2.33) it is clearly
   better on band and shape; **it is never worse.**

**The best argument FOR mode 1 — which C1 never made, and the review supplied.** With h4 measured
at 20.94 against a band asking 30, the band's implied `h4 → h6` step of −10 puts h6 at **10.9
relative to measured h4**; on that shape-relative reading mode 1's 12.03 is better positioned than
mode 3's 13.91 and the control's 18.59 is nowhere near either. It is a real argument. It is **not
self-consistent on this tree**: the same −10-per-two-rungs slope applied to `h6 → h8` demands
h8 = 0.9 against a measured 8.12 and a hard floor of 5. The shape-relative target can be honoured
for one step at a time only, and honouring it at `h4 → h6` while ignoring `h6 → h8` is precisely the
cherry-pick that produced mode 1's flat top. That is why it did not carry the decision — but it
does replace C1's original, too-easy framing of "nothing that was in band left it".

**What mode 3 costs, recorded not hidden:** NO QUARTER becomes a quantitative row —
`+1 enemy; +1 stat; the ceiling`. The apex is a wall because of the STACK beneath it, which the
panel lists in full at heat 8, not because of its own row. C1 pays that legibility cost knowingly.

## C1-R2. Two claims withdrawn, one strengthened

**WITHDRAWN — "moving coordination tier 2 down a rung is not a difficulty lever."** That rested on
one cell: +1.56 ±1.95, z = +0.80, **95% CI [−2.26, +5.38]** — which does not exclude tier 2 buying
2.3 points, *more* than rung 6 was buying in the first place. C1 had better evidence in its own
archive and did not use it. **Four contrasts isolate the same component:**

| contrast | n | lev-only | ctl-only | Δ | ±SE |
|---|---|---|---|---|---|
| m2 vs ctl, h6 | 320 | 22 | 17 | +1.56 | 1.95 |
| m2 vs ctl, h7 | 320 | 13 | 10 | +0.94 | 1.50 |
| m3 vs m1, h6 | 640 | 30 | 18 | +1.88 | 1.08 |
| m3 vs m1, h7 | 320 | 6 | 5 | +0.31 | 1.04 |
| **pooled, inverse-variance (1600 CRN pairs)** | | | | **+1.09** | **0.63** |

z = +1.73, 95% CI **[−0.15, +2.33]**, **wrong-signed in all four cells**. The claim on the record is
now: *indistinguishable from zero, with a point estimate slightly in the player's favour.*

**WITHDRAWN — "nothing that was in band left it."** True but self-serving. h6 moves from 1.4 points
ABOVE the band centre to 6.1 BELOW it. The honest reading is the shape-relative one: h6 is now far
better positioned relative to a measured h4 of 20.9, and **it is the h4 LEVEL that is broken** —
which is C1's own closing diagnosis, and it now appears in the ladder-of-record entry instead.

**STRENGTHENED — the AddHeat leak is DIRECTIONAL, and it points at C1's own headline.** Heat 8 is
clamped and cannot leak upward, so every rung below it is contaminated TOWARD the rung above, and
**the instrument systematically compresses the top of the ladder it is being used to diagnose.**
Some part of the flat middle/top this program exists to fix may be the instrument, not the design.
Quantified from the archive: h5's 1.56% flip rate under a lever that can only bite at heat ≥ 6
implies **≈10% of h5 campaigns reach heat ≥ 6**. C1's campaign-level inertness claims survive it
(the six 0/320 rungs are exact). ROADMAP now carries it with a cheap fix: a harness pin that
suppresses `AddHeat` for a measured batch, to be done BEFORE the base-difficulty round.

## C1-R3. Two undeclared effects, now on the record

**The damage tooth is not purely numeric.** `Mission.cs:890-902` says so in its own comment: +1
`DmgMax` **widens the AI finish band** (`Ai.Plan` scores a kill on `p.Hp <= e.Weapon.DmgMax`), so
moving it down two rungs moves a **coordination sharpening** down two rungs too. The CRN round
prices it; C1 never named it. It cuts *toward* the wave's thesis — rung 6 partly re-earns, through
the finish band, the coordination identity its dead `AiTier = 1` never delivered — and it belongs
on the record either way, especially next to a headline about coordination.

**The same comment names a watch item C1 did not report:** a wider finish band leans AGAINST the
BRACE comeback lever, and "the comeback economy is the first re-tune if lead-swings collapse". In
C1's committed h6 sample pair lead-swings/match holds at 0.9, but average max-swing goes 59.6 → 55.8
and greedy BRACE usage **146 → 96 (−34%)**. n=40 and not conclusive — but the code told us to look,
and it is now a ROADMAP item.

**SKIRMISH at heat 6–7 is changed and unmeasured.** `Game.cs` gates the m1–2 heat grace on
`Mode != GameMode.Skirmish` (W9), so a heat-6 skirmish now takes the +1 enemy damage **from turn
one**, where before only heat 8 did. `SIGHTLINE_BALANCE` measures campaigns only. DAILY is safe
(`DailyHeat` is `% 4u`, capped at 3); ENDLESS is safe (`EndlessWaveScale` reads `StatDelta`).

## C1-R4. THE ARCHIVE TOOLING FAILED OPEN — in the wave built to find that

`inert_diff.py` globs the raw chunk JSONs, which C1 deliberately did not commit. Run exactly as
C1's own README documented it, it printed:

```
h0: 0 chunk pairs, 0 leaf fields compared, 0 DIFFER  <- INERT
TOTAL: 0 fields, 0 differ
```

**A green inertness verdict on zero comparisons** — the same pathology the wave's own test-design
section warns about, sitting in the wave's own tooling, while the README claimed "nothing published
here is unreproducible" and the DEVLOG (correctly) said the opposite. Fixed: the script now exits
non-zero with `NO CHUNKS` and names the reason, and the README states plainly which numbers are
reproducible (every win-rate figure, from `chunks.csv`) and which are recorded-only (the
`inert_diff` rounds, the `ch/ARM` column, all per-chunk aggregates).

**And the fixed runner was not in the repo.** `git check-ignore` →
`.gitignore:57:run_chunk.sh` — a bare filename from an earlier wave's scratch cleanup, which
matches at any depth and silently swallowed it; L3's copy had been force-added, C1's `git add -A`
dropped it without a word, leaving **both committed runners inoperable** (they call
`bash "$HERE/run_chunk.sh"`). The rule now carries explicit `!docs/measurements/**/…` negations for
all four swallowed names, so the next wave's archive cannot lose its runner either. **C1's own
headline process lesson was "a shared path is not storage — the runner lives in the repo now", and
the runner was the one file missing.**

## C1-R5. THE COPY BUDGET COULD NOT EXPRESS A SCALE — and the panel is now structurally safe

C1 measured a 38-character budget off a 100% screenshot and asserted it in a test. The review found
the hole: `Cfg.Scaled` multiplies any size ≤ `Cfg.UiFontMax` by `Cfg.UiScale` (the settings offer
0.90/1.00/1.10/**1.20**) while the panel geometry does not scale, so the real budget at 120% is
~32 characters. Measured at 120% against the border at x=1240: HARDENED (38 chars) reached 1268,
**28px off the card**; LINGERING WOUNDS (37) reached 1261, **21px off** — *a row C1 had shortened
from 43 for exactly this reason.* A character count cannot express a scale.

**Fixed structurally rather than by more copy-trimming.** `Hud.DrawHeatSelector` now WRAPS each
rung's Desc to the measured body column via `WrapText` (which measures through `Cfg.Measure`, so it
is scale-correct) and sizes the card by the LINE COUNT, with the row step on the house `TextRow`
scaler so lines cannot overlap either. At `UiScale == 1.0` every number is arithmetically identical
to the old `132 + rows * 26 + 30`, so the shipped 100% layout is unchanged — verified by
screenshot. `Heat.DescBudget` survives, correctly labelled: a COPY-QUALITY budget ("should read as
one line at 100%"), explicitly not the thing that keeps ink inside the border.

C1's original char counts were also off by one: NO QUARTER was **58**, not 57; LINGERING WOUNDS
**43**, not 42. No conclusion changes, and this project treats stated measurements as load-bearing.

## C1-R6. Test hygiene, on the wave's own rules

- **PASS-banner over-claims removed.** "Every rung of every mode fits the 38-char column" was false
  (mode 0 is exempt by design and its rung 8 is 58 chars — the defect itself), and "per-mode shapes
  pinned" covered 0/1/2/3/5 while **4, 6 and 7 had no pin at all**. Shapes are now pinned for all
  eight modes and the banner says what it actually checked, including *at 100% text size*.
- **Three assertions that could not fail are gone or made real.** `dialNotRestored` was a tautology
  (`SetMidTooth` clamps to [0,7], so the `finally` restore always satisfied it) — exactly the
  pattern C1 invoked against others. `recruitMoved` ran eight identical times inside a loop it did
  not depend on; it is now one assertion, kept as a guard against a future edit folding `RecruitMod`
  into `Mods`. The mode-0 `untouchedRung1..5` loop was strictly implied by the mode-1..7 loop.
- **The shipped-default check was keyed on an ARRAY INDEX** (`shapeGolden[1]`), not on the mode; it
  now looks up `mt == Heat.ShippedMidTooth`, so reordering the golden list cannot silently make it
  check a different mode.
- **`DmgOnBoard` passed vacuously on an empty spawn** (a `foreach` over an empty force asserts
  nothing). It now fails with `noEnemiesSpawned`.
- **Two comments the lever falsified** are corrected: `Heat.DmgDelta`'s "today only NO QUARTER
  carries it, so this is 0 below the rung-8 apex" and `Game.SetupMission`'s "W6c: rung-8 +1 enemy
  damage (0 below the apex)". Both were false at heats 6–7 the moment the lever landed, three lines
  from the code that falsified them. **That is the exact defect class this wave was created to
  find**, and it is the second time in one wave (the first being the dead `AiTier`) that a comment
  outlived the fact it described.
- `AITEST`'s `aiTierExposedNot1` tag was a misnomer even before C1 — heat 6's tier came from rung 4,
  which IS the defect — and is now `aiTierExposedNot2`, asserting what it actually asserts.

## C1-R7. Still not done, after the send-back

- **Rung 5 is untouched and still buys 0.31 — indistinguishable from zero.** C1 fixed the
  second-flattest rung and left the flattest. Its `+1 enemy` is likely partly eaten by the
  12-hostile spawn cap and `HarshAttrition` compounds over a run length the bot rarely reaches:
  **hypotheses, unmeasured.** ROADMAP names the instrumentation to do first.
- **No lever beat the CONTROL on dispersion.** Mode 3 is the least-bad redistribution, not an
  improvement over doing nothing on that metric, and the ladder-of-record entry now says so.
- **Nothing was re-measured for this send-back**, as instructed: mode 3's h5 cell is substituted
  from mode 1's measured h5 (20.62) and flagged in the archive README, and mode 3's heats −1..3 are
  unmeasured (cumulative-identical to control below rung 6, and measured exactly the control under
  mode 1 at six rungs).
- **SKIRMISH at heat 6–7, the BRACE comeback economy, and the AddHeat directional bias are all
  unmeasured** and are now ROADMAP items rather than footnotes.

# PROGRAM CONTOUR — Wave C2 "THE OPPONENT DECLINES" (dev; worktree `agent-a1c26e3c14d98312b`, branch `wave/opponent-declines`)

**Base commit `17934ee`** (PROGRAM CROSSCUT composed). The CROSSCUT handoff calls this "the
single biggest remaining gap in the fight": `Ai.cs:537` scored any tile that had a shot at
`100 + bestHit`, while every terrain term in the same function is bounded well under ~64 —
cover 36, height ~28, flank −25, fire −60, overwatch −26. A flat +100 for "a shot exists"
dominated the whole planner.

## 1. THE DEFECT IS REAL, BUT IT IS NOT THE ONE THE BRIEF DESCRIBES — and the difference decided the wave

The brief's statement of the consequence is that the opponent "ALWAYS shoots if it can see
anything, **at any hit chance**, from any position." The first thing this wave did was
instrument that claim, because you cannot fix a decision surface you have never measured.

**Pre-change enemy decision mix**, `SIGHTLINE_AIDECLINE=0`, heat 0, slot set b0, 20 campaigns /
89 missions, 1076 CONTESTED act-opportunities (the W2 rule: an all-downed board is `Ai.Plan`'s
empty-plan early return and idles by design — those 376 acts are counted separately and never
enter the denominator):

| verb | n | share |
|---|---|---|
| shoot | 649 | 60.3% |
| hunker | 158 | 14.7% |
| move (reposition, no shot) | 114 | 10.6% |
| heal 47 / grenade 28 / siege 26 / reload 19 / item 16 / brace 10 / staleplan 5 / sap 3 / shove 1 | 155 | 14.4% |
| **overwatch** | **0** | **0.0%** |

and the shot that was on the table, by hit-chance band:

| hit% band | 0-19 | 20-39 | 40-59 | 60-79 | 80+ |
|---|---|---|---|---|---|
| taken | **0** | 17 | 59 | 248 | 325 |
| E\[dmg]/shot | — | 1.44 | 1.97 | 2.56 | 3.74 |

**The "at any hit chance" half is not supported.** 88% of the opponent's shots land in the top
two bands and **not one** of 649 was under 20%. On the full 960-campaign round below the bottom
band is not literally empty but it is close: **54 of 15407 pre-change shots (0.4%)** were under
20% and 83.7% were at 60%+. The reason is that `bestHit` already contains the hit chance, so the
term that dominates tile choice also ranks targets sensibly once you are standing somewhere. (The
smaller probe's exact zero is quoted as what that chunk measured; the pooled 0.4% is the number to
use.)

**The "from any position" half is exactly right, and it is the whole defect.** The +100 is a
constant paid for *having* a line of fire, so it is invisible to the target's cover, to the
shooter's own cover, to height, to a flank and to a player overwatch lane. The opponent will step
out of full cover into the open for a marginal shot, every time, because 100 > 36.

And the corroborating number the handoff supplied is confirmed dead-on: **the enemy OVERWATCH
branch fired 0 times in 1076 contested acts.** Not because the branch is wrong — because a shot
scores a constant and a branch that only runs when there is *no* shot can never compete with one.

## 2. WHAT SHIPPED

`SIGHTLINE_AIDECLINE` (default **ON**; `=0` restores the pre-C2 opponent exactly).

**(a) The tile term.** `Ai.ShotTileValue(bestHit, hitPct)` replaces the constant:

```
ShotSeat + bestHit * (hitPct / 100)        //  ShotSeat = 18f
```

`bestHit` is a target-CHOICE comparator (hit chance plus what CONNECTING is worth — exposure,
the finish band, the squad's focus, crossfire), so weighting it by the probability of actually
connecting weights it by how likely that is. **It is NOT an expected value and an earlier draft
of this section said it was.** `bestHit` already contains the hit chance, so the term expands to
`ShotSeat + hit²/100 + bonuses·hit/100` — a hit-SQUARED weighting. A true EV term would weight
only the consequence of connecting by `hit`, and would not re-multiply the hit chance by itself.
The squaring is kept on purpose: it makes the opponent more hit-greedy than an EV maximiser,
which is what a fight where a soldier dies in ~two connections wants (an EV maximiser is
indifferent between one 80% shot and four 20% shots; an HP bar is not). But the honest name for
it is the arithmetic, not "expectation". `ShotSeat` is the option value of holding a line of
fire at all, priced deliberately at **~one level of cover** (`cover.Level * 18` in the same
scorer), because that is the trade the term has to arbitrate: *stand in the open with a shot* vs
*stand behind that wall without one*. **Target selection is untouched** — the loop above still
ranks targets by `val` exactly as before, which is why AITEST's focus-fire legs still pass.

**(b) The decline gate**, run once per plan, after the sap/grenade/item/shove blocks (those
already price themselves against the real shot, so they must see it) and before W2's no-shot
fallback. Dropping `ShootTarget` hands the unit straight to that fallback, which always assigns
overwatch / hunker / reload / dash — so **a decline can never produce a dead turn**; the no-idle
invariant is inherited structurally rather than re-argued, and `SIGHTLINE_AIIDLETEST` still covers
it.

The shot is priced against `Combat.AsIfExposed` — the same shot with the defender's cover taken
away, which is what an overwatch reaction actually catches (the reaction resolves on every tile
ENTERED, and a soldier crossing between cover blocks is uncovered on the way). HUNKER is
deliberately **not** stripped: a hunkered soldier is one that chose not to move, so holding a lane
against it buys nothing.

```
worth  = E[dmg now] * (finish band ? 1.6 : 1)
bar    = max(canWatch ? 0.45 : 0, canDig ? 0.30 : 0) * (1 + 0.20 * min(guns, 3))
decline if  bar > 0  &&  worth < 3.00 (abs keep)  &&  worth < bar * E[dmg if exposed]
```

`guns` is the number of soldiers already holding a firing solution on the tile — range and line
of sight only, no `ComputeOdds`, a handful of Bresenham walks once per plan. It is there because
**firing and standing still is not free**: Combat's EXPOSED BY FIRE rule hands every soldier that
can see the unit +12 aim and +12 crit against it until it moves. Holding a lane or digging in does
not. Under **two or more** guns with cover to hand, the freed action buys SURVIVAL — the one place
the fallback's overwatch-first order is overridden. Rushers (BERSERKER / HOUND / STRIKER / DRONE /
the Legion BREAKER's second rage) never decline; that is identity, not tactics, and it is also the
tempo guard.

**(c) `Combat.ShotOdds.CoverDef`** — the aim `ComputeOdds` actually subtracted for cover. Recorded
so `AsIfExposed` is a *reconstruction from the model's own numbers* rather than a re-derivation of
the cover rules that could silently drift from them.

## 3. THE FIRST PRICING WAS WRONG AND THE INSTRUMENT CAUGHT IT

The gate's first version used **absolute expected-damage bars** (1.45 / 0.90), set from the
pre-wave band means above. On a 20-run probe it declined **35 shots at 80%+ hit chance**, because
`Combat.ExpectedDamage` is armour-aware: a popgun against a hardened soldier is ~1.0 expected
damage at *any* hit chance. Declining a clean 90% shot does not read as a smarter opponent, it
reads as a broken one — and it is also wrong on the merits, because that unit's ALTERNATIVE is
worth ~1.0 too. **The bar has to move with the unit's own ceiling**, which is what
`AsIfExposed` gives it. That is why the shipped rule is a ratio.

## 4. THE ONE JUDGEMENT CONSTANT, AND THE TWO MEASUREMENTS THAT BRACKET IT

`Ai.cs` says of `DeclineWatchRatio = 0.45` that it is "the ONE number in this block that is a
judgement rather than a measurement… declared, not hidden." **That is the accurate framing and
this section now matches it.** An earlier heading here read "…AND THE MEASUREMENT THAT PRICES IT",
which claims more than the evidence supports: the measurement prices the **lane** (~0.20), not the
bar, and the second probe supplies a **ceiling** (1.10 is too high), not a value. The two
**bracket** 0.45 from opposite sides. Neither pins it, and nothing in this wave does.

**The floor: what a held lane is worth in damage the flywheel can see.**
`Stats.RecordEnemyReaction` counts enemy lanes held against enemy reaction shots actually FIRED,
and a deliberate **maximal-decline diagnostic** (`CAL-diag`: watch ratio 1.10, dig 0.00, no
absolute guard — decline every shot whenever a lane exists) generated a population big enough to
read:

| chunk | lanes held | of which generic overwatch | reaction shots | **fired** |
|---|---|---|---|---|
| CAL-diag h0 b0 | 457 | **442** (15 brace) | 111 | **24%** |
| CAL-diag h4 b0 | 461 | **455** (6 brace) | 124 | **27%** |

**"Fired", not "paid off"** — the first draft used the latter in this table and in
`ladder_table.py`, and `Stats.cs`'s own comment was the one that had it right: the counter counts
*shots taken*, and at the reaction's −10 aim mod a fired reaction often misses. **True payoff is
strictly below 24%**, which cuts in favour of the shipped bar and is exactly why the wrong word
had to go.

The diagnostic ran on an interim binary (before the threat-scaled bar and the dig-in preference —
both change WHICH shots are declined, never what a lane is worth). Times the reaction aim mod, a
held lane is worth roughly **0.20 of the open shot** in damage the flywheel can see. 0.45 is
about twice that, and the excess is declared, not hidden. Two things push the other way and only
one of them is arguable:

1. **Measured, and a real property of the instrument.** `Game.Autopilot.TileExposure` has a term
   for an enemy PIKEMAN BRACE lane (`InEnemyBraceLane`, +18) and **no term at all for an ordinary
   enemy overwatch** — its generic "exposed to this gun" +6 is identical whether the hostile is
   watching or not. The bot does not route around enemy overwatch, so the AREA-DENIAL half of a
   lane is invisible to the flywheel by construction. Rule 3, checkable in eight lines of
   `Game.Autopilot.cs`.
2. **A judgement.** Against a human who reads the board, a held lane also costs tempo and routing.
   Unmeasured, and stated as unmeasured.

**Why the shipped round cannot corroborate any of this, and the first draft wrongly implied it
could.** An earlier version of the table above carried two "for scale" rows from the ladder
(L-BASE 53/194 = 27%, L-DECL 57/267 = 21%) as though they agreed with the diagnostic. **They are a
different population and I did not disclose it.** `lanesHeld` is `overwatch + brace`, and in the
shipped round the composition inverts: **L-BASE is 20 overwatch + 174 BRACE**, L-DECL is
111 + 156. The BASE arm held **20 generic lanes in 26,841 contested acts** — it cannot corroborate
anything about generic overwatch, and the rows were mostly measuring the PIKEMAN. They are removed
rather than re-labelled; `CAL-diag`, where 442 of 457 lanes are genuine overwatch, is the only
place in this wave that measures the thing the bar is about.

**The ceiling: a decline rate large enough to be felt is a worse opponent — suggestive, not
established.** At a 51% decline rate `CAL-diag` read run completion 55% → 75% on the same 20
worlds, mission length 5.36 → 5.59 turns, shots/kill 3.211 → 3.307. **Two disclosures the first
draft owed and did not pay.** (i) On 20 paired worlds that is b = 2, c = 6 discordant, **exact
two-sided p = 0.29** — a 20-point swing is not even close to significant at that n, and I quoted
it as though it were a finding. (ii) It is **confounded**: the BEFORE leg is the pre-change binary
while `CAL-diag` carries BOTH the new tile term AND the maximal-decline gate, so the comparison
cannot separate them. I disclosed the interim binary for the lane-payoff number and not for this
one, which is inconsistent. Treat it as a **suggestive probe that a very high decline rate hurts
the opponent**, direction only, and note that it agrees with the mechanism rather than proving it.

**So the honest summary of this section:** the lane is worth ~0.20 or less (measured, n = 918
lanes), 1.10 is too high (suggested, p = 0.29), and 0.45 sits between them as a declared
judgement. The ladder is what prices whether the judgement was affordable, and §5 is that price.

## 5. THE PRICE — the CRN-paired ladder, base `17934ee`, 960 campaigns

One binary. Two arms. The **only** difference between them is `SIGHTLINE_AIDECLINE`, so the
pairing is as tight as this project can make it: identical executable, identical slot seeds, one
environment variable. 6 rungs x **4 disjoint slot sets** (`SIGHTLINE_BALANCE_BASE` 0/10/20/30) x
greedy+sloppy x N=10 = **80 campaigns per rung per arm**. All 48 chunks printed `OK ... runs=20`;
zero `BAD`. Raw data `docs/measurements/c2/`, table `ladder_table.py`, paired test `paired.py`.

| rung | BASE (`=0`) | DECL (`=1`, shipped) | delta | McNemar p | band | verdict (DECL) |
|---|---|---|---|---|---|---|
| RECRUIT | 72.5 ±5.0 | **71.2** ±5.1 | −1.2 | 1.000 | 75 ±8 | in |
| heat 0 | 42.5 ±5.5 | **41.2** ±5.5 | −1.2 | 1.000 | 55 ±8 | **below by 5.8** |
| heat 2 | 21.2 ±4.6 | **36.2** ±5.4 | **+15.0** | **0.031** | 40 ±8 | in |
| heat 4 | 25.0 ±4.8 | **23.8** ±4.8 | −1.2 | 1.000 | 30 ±8 | in |
| heat 6 | 20.0 ±4.5 | **18.8** ±4.4 | −1.2 | 1.000 | 20 ±8 | in |
| heat 8 | 10.0 ±3.4 | **7.5** ±2.9 | −2.5 | 0.617 | 10 ±5 | in |

**The aggregate is near-inert. The per-world picture is not, and the second sentence is the
interesting one.** Four rungs net exactly one discordant pair (−1.2 points); heat 8 nets two. But
a NET is a difference of two counts, and the counts are large: **124 of 480 paired worlds (25.8%)
came out differently** — RECRUIT 23/80, h0 29/80, h2 26/80, h4 25/80, h6 17/80, h8 4/80. This
change is nowhere near inert per world; it is near-inert in aggregate **through cancellation**,
which is a different and more honest claim. (An earlier draft of this line said "five of six rungs
move by one discordant pair or less". That is false on its own terms — h8 nets two — and it
described the net while reading as though it described the worlds.)

**What this round could actually have detected.** McNemar's power is set by the DISCORDANT count,
not by n, and the minimum detectable |b−c| is about 1.96·√n_d + 1 pairs:

| rung | discordant | flipped | MDE (α=.05) | MDE (family-wise, α=.05/6) |
|---|---|---|---|---|
| RECRUIT | 23 | 28.8% | 13.0 pts | 17.1 pts |
| heat 0 | 29 | 36.2% | 14.4 pts | 19.0 pts |
| heat 2 | 26 | 32.5% | 13.7 pts | 18.1 pts |
| heat 4 | 25 | 31.2% | 13.5 pts | 17.7 pts |
| heat 6 | 17 | 21.2% | 11.4 pts | 14.8 pts |
| **heat 8** | **4** | 5.0% | **nothing** | **nothing** |

At heat 8 the exact two-sided minimum is 2·(1/2)^4 = **0.125**, so no result at that rung is
reachable at α = 0.05 whatever the effect. **Every "−1.2, p = 1.000" row above is therefore an
absence of evidence, not evidence of absence** — this round can exclude a double-digit swing and
nothing smaller. The brief asked for the ladder to stay in band and it does; it did not, and could
not, establish that the change is neutral.

**The heat-2 cell, on the one ground that holds.** 19 DECL-only wins against 7 BASE-only,
p = 0.031 nominal. **It is one of six tests, and 0.031 x 6 = 0.19 — it does not survive
multiplicity, and I am not claiming it.** That is the whole argument, and it is sufficient.

An earlier draft offered two further reasons and **both were wrong**, so they are recorded here
rather than quietly deleted. (a) *"The BASE arm's own 21.2 is ten points below L3's h2, so it is
the baseline that is odd on these slot sets."* A non-sequitur: **McNemar conditions entirely on
the pairs.** A slot set that is hard hits BOTH arms in the SAME worlds, so it cancels exactly in
the discordant counts and carries zero evidential weight against a paired result. (b) *"The
missions-cleared margin is only +0.33."* Backwards. On these 80 h2 pairs a win averages **6.00**
missions and a loss **2.89**, so a genuine net +12 wins PREDICTS a margin of
12/80 × 3.11 = **+0.47**. The measured +0.33 is consistent with the effect being real, not
evidence against it. The honest sentence is just: *one of six rungs produced a nominally
significant cell that does not survive Bonferroni.*

**The BASE arm is not a re-measurement of L3 — it IS L3, replayed.** `src/` is byte-identical
between `d814f0c` (L3's base) and `17934ee` (mine), and L3 ran bases 0/10/…/70 while this round
ran 0/10/20/30. Checked, not assumed: all **24 BASE chunks are byte-identical to L3's
corresponding chunks** on every field outside `harness` and the new `enemyDecisions` block —
every per-slot record, win, turn count and shots-per-kill. So all 480 BASE campaigns are L3's own
first four slot sets, bit for bit.

**An earlier draft claimed the BASE arm "reproduces L3 within 1.6 SE at every rung." That
sentence is withdrawn.** Quoting a standard error on the difference between a set and its own
subset is a category error — the difference is deterministic, not sampled, and no SE applies. It
also failed on its own terms: the h2 gap is 1.71 SE treating the halves as independent, 2.79 SE
computed correctly for the overlap, and 1.67 even under the draft's own rounded "~6"; 1.6 is the
one threshold that would have made the sentence true.

**And the deterministic fact exposes a project-level finding about the INSTRUMENT, which is worth
more than the sentence it replaces.** If the BASE arm is L3's b0-b30, then L3's own two halves can
be compared directly, and at heat 2 they disagree: **b0-b30 reads 21.2% and b40-b70 reads 41.2%**
(17/80 vs 33/80). Difference +20.0, SE 7.16, **z = 2.79, p = 0.0052**, and 0.031 after Bonferroni
×6. **L3's slot space is heterogeneous at heat 2 beyond binomial noise** — the ladder-of-record's
h2 figure of 31.2% is an average over two populations that differ by twenty points. That is a
result about the measuring instrument, not about this wave, and it belongs to whoever next quotes
a four-slot-set rung as if slot sets were interchangeable. It does NOT explain this wave's h2 cell
(see (a) above: a slot-set effect cancels inside a paired test), and I am not offering it as one.

**heat 0 is below band in BOTH arms** (42.5 and 41.2 against a 47 floor), and the delta between
them is −1.2 (one discordant pair, p = 1.000). **That miss predates this wave and is now provable
rather than asserted**: the 42.5 is L3's own b0-b30 replayed bit for bit, and L3's eight-slot-set
h0 reads 47.5, exactly at the floor. Recorded, not repaired: repairing a rung inside a wave that
also changes gameplay is the mistake the measurement contract exists to prevent.

## 5b. THE PASSIVITY TRIPWIRE — the fight did not get longer

The brief's item 3 is the real risk in this wave: an opponent that dithers in cover is worse than
one that shoots badly, and W2 already had to fix an enemy that did not act. Pooled over all 960
campaigns / 3452 missions:

| | BASE | DECL | delta |
|---|---|---|---|
| mission length (turns) | 5.738 | **5.563** | **−0.175** |
| player shots per kill | 3.153 | **3.157** | +0.004 |
| player shots / kills | 15911 / 5046 | 15443 / 4891 | — |
| pooled run completion | 31.9% | 33.1% | +1.25 |

**Fights are DIRECTIONALLY shorter, and I over-stated this in the first draft.** The pooled
"−0.175 turns, 3% shorter" above treats 3452 missions as independent, and they are not: missions
nest in campaigns, campaigns are paired, and the independent units are the **24 rung × slot
cells**. Tested properly on those: **−0.186 ± 0.116 turns, t(23) = −1.60, p ≈ 0.12**, sign test
14 of 24 cells shorter (p = 0.54). So the correct sentence is *directionally shorter by about
0.18 turns, not significant at n = 24 cells* — and shots-per-kill is flat at +0.004.

**The tripwire conclusion is untouched, because the tripwire was one-sided.** The failure mode the
brief named is fights getting LONGER and less decisive; the point estimate is negative on both
axes and the confidence interval excludes anything like the +0.23-turn lengthening the
maximal-decline probe showed. Nor did the harness tripwire trip: zero `TIMEOUT`, zero `BAD` chunk,
and `instrumentHealth.stalemateLosses` stayed 0 across all 48 chunks.

## 5c. THE DECISION MIX, BEFORE AND AFTER, on the full 960-campaign round

Pooled over all six rungs (contested acts only):

| verb | BASE | | DECL | | |
|---|---|---|---|---|---|
| | n | share | n | share | |
| shoot | 15407 | 57.4% | 14510 | **55.4%** | −2.0 pts |
| hunker | 6176 | 23.0% | 6539 | **25.0%** | +2.0 pts |
| move (reposition, no shot) | 2255 | 8.4% | 2143 | 8.2% | — |
| **overwatch** | **20** | **0.07%** | **111** | **0.42%** | **x5.6** |
| everything else | 2983 | 11.1% | 2869 | 11.0% | — |
| contested acts | 26841 | | 26172 | | |
| **acts where the chosen tile HAD a shot** | **17030** | **63.4%** | **16110** | **61.6%** | **−920** |
| shots DECLINED | 0 | 0.00% | **105** | **0.65%** | — |
| enemy lanes held / reaction shots | 194 / 53 | 27% | 267 / 57 | 21% | — |

And the SHOT BANDS over the same round — the opponent's own shots got measurably better, which is
the positional change showing up on the offensive side rather than a decline effect:

| hit% band | 0-19 | 20-39 | 40-59 | 60-79 | 80+ | total |
|---|---|---|---|---|---|---|
| BASE taken | 54 (0.4%) | 480 (3.1%) | 1967 (12.8%) | 5750 (37.3%) | 7156 (**46.4%**) | 15407 |
| DECL taken | 20 (0.1%) | 301 (2.1%) | 1524 (10.5%) | 5083 (35.0%) | 7582 (**52.3%**) | 14510 |
| DECL declined | 16 | 52 | 33 | 4 | 0 | 105 |

Every band under 60% shrinks and the 80%+ band gains **5.9 points**. E\[dmg] per shot within each
band is unchanged to two decimals (0.63/1.13/1.75/2.51/3.64 vs 0.70/1.17/1.77/2.55/3.65), which is
the check that this is a shift in WHICH shots get taken and not a change to the combat model. The
declines sit where they should: 85 of 105 in the 20-59% bands, four in the 60-79% band (a shot
whose open-cover reference was far better), none at 80%+.

**The wave's real effect is HUNKERING, and the ×5.6 overwatch figure is the smaller story.** Only
**105 of the 897 fewer shots** are gate declines; the rest come from the tile term steering units
onto cover tiles that have no shot at all, where they dig in. So the honest headline is
**+2.0 points of hunkering**, not +0.35 of overwatch — the overwatch number has the bigger
multiplier and the smaller effect, and leading with it (as an earlier draft did) inverts their
importance.

**That matters because DESIGN §3.A's anti-turtle rule cuts at this wave.** "Kill the dominant
defensive strategy" is written about the player, but an opponent that hunkers 2 points more is
moving in the direction the section warns about. The mission-length tripwire says it has not cost
tempo (§5b: directionally shorter, and certainly not longer), so this is not a defect today — but
**"the opponent hunkers more" is the line a feel review should watch**, not the decline rate. The
DECLINE itself is 0.65% of the shots on the table; §4 is the argument that small is correct.

## 6. SCREENSHOTS — Rule 3, and what the frames actually show

A CRN batch prices consequences and is blind to feel, so `SIGHTLINE_DECLINESHOT` stages the
behaviour on the live board and runs the **real** `Ai.Plan`, applying exactly what
`Game.UpdateEnemy`'s ActAfterMove would apply — the frame is a decision the shipped planner made,
not a hand-set flag. One soldier behind one high-cover block; one hostile; everything else parked
behind a high-cover wall down column 16 so the hostile's only shot in the world is the covered
one. Both frames are `SIGHTLINE_SHOT=760 SIGHTLINE_MISSION=1`; the only difference is the dial.

**Both frames are `SIGHTLINE_SEED=4242`**, so the pair is a genuine controlled contrast rather
than two similar pictures — same arena, same roster, same shot, differing in the dial and nothing
else. (An earlier version of these frames was unseeded and the two shots landed on different
biomes; the write-up then carried a caveat about clock-seeded RNG that was entirely avoidable.
`SIGHTLINE_SEED` existed the whole time.)

`docs/measurements/c2/shots/decline-on.png` (`SIGHTLINE_AIDECLINE=1`, shipped) —
`STALKER@(6,0) vs VEGA@(12,3) shotHit=25% E[dmg]=0.71 declined=True -> DECLINED - HOLDING THE LANE`.
**What I see:** the blue industrial arena; the STALKER top-centre inside its low-cover ring; VEGA
right-of-centre, gold-ringed, with the grey high-cover block hard against its west face; the
parked units sealed behind the column-16 wall on the right edge. And the read that matters — the
INCOMING FIRE card carries a **third** line: *"OVERWATCH LANE — entering draws a reaction."* The
game itself tells the player the hostile is holding ground it now denies.

`docs/measurements/c2/shots/decline-off.png` (`SIGHTLINE_AIDECLINE=0`) — `shotHit=25%
E[dmg]=0.71 declined=False -> TOOK THE SHOT`. **Pixel-for-pixel the same board**: same arena, same
STALKER on the same tile, same VEGA, same card header (`1 hostile bears · best 67% · ~2 dmg /
worst gun: STALKER — SCOUT`). The single difference in the entire frame is that the third line is
**gone**. The hostile spent its action on a shot worth 0.71 expected damage and holds nothing.

**Two honest notes.** **(1)** That tooltip line is **pre-existing** — `Hud.cs` has drawn it since
before this wave (`git diff 17934ee..HEAD -- src/Hud.cs` is empty). This wave did not add the
read; it created the enemy state that makes the read appear, which is the better half of the deal
but not the same claim, and the lead's review brief had it the other way round. **(2)** The
hostile had to be PINNED (a ring of impassable low cover) to stage this at all. The first version
of the hook left it free and it did something better than declining: **it walked around the block
and took a 77% flanking shot.** That is the wave working — the tile term is what makes a flank
outrank a frontal potshot — but it is a different frame, and it is worth recording that on open
ground the planner's first answer to a covered target is now to out-position it, not to hold fire.

**What the frames do NOT show, contrary to the hook's own former doc comment.**
`Renderer`'s enemy kill-zone wash is 7-12% alpha and is **below perceptual threshold** on these
biomes — the code review measured it invisible even at 2.6x contrast. The comment claimed it
"lights the ground it now denies"; that claim is withdrawn in the code. Worse and more
interesting: because a plain enemy overwatch has no lane selection, the wash covers nearly the
whole open board, so even if it were visible it would carry no information. **That is this wave's
no-lane-selection finding arriving from the render side**, and it is in ROADMAP as its own item.

## 7. THE SELF-TEST, AND THE PROOF IT CAN FAIL

`SIGHTLINE_DECLINETEST`, wired into `scripts/qa-sweep.sh`. Six groups of legs:

1. **`Combat.AsIfExposed` ground-truthed against a REAL `ComputeOdds`** on a board whose cover
   block has been *physically deleted* — low, high, diagonal-partial, plus an identity leg on an
   already-exposed shot. This is the reconstruction the whole gate rests on; if it drifts from the
   cover model the gate silently starts pricing against a fiction. The scene SEARCHES for an
   attacker tile that both keeps line of sight and reads the requested cover level, and fails
   loudly (`scene:...`) if it cannot find one.
2. **`Ai.ShotTileValue` on BOTH sides of the dial.** The pre-C2 branch is pinned to the literal
   `100 + bestHit`, *and* to the fact that it beat high cover (36) — the defect itself, pinned.
   The shipped branch must put a 12% shot BELOW that wall, keep a strong shot dominant, and be
   monotone in the hit chance.
3. **The gate declines a bad shot** on a live `Ai.Plan` and still spends the action (the W2
   invariant), on a pinned shooter looking at a high-cover soldier at 21% / E\[dmg] 1.36.
4. **It does NOT decline** the same shot from the same tile when the target is simply not in cover.
5. **A rusher never declines** (`Ai.NeverDeclines`).
6. **Under two or more guns it DIGS IN** rather than offering a lane (`DeclineDigIn` + `Hunker`).

Plus a forced pre-C2 contrast leg that exists only to prove the scene is a genuine decision and
not a board where nobody would shoot anyway.

**Every shipped-behaviour leg reads the AMBIENT `Game.AiDecline`, so the test fails when the
feature is off.** Measured, not asserted:

```
$ SIGHTLINE_DECLINETEST=1 ...
DECLINETEST: ambient SIGHTLINE_AIDECLINE=1; gate scene [...] shot hit=21% E[dmg]=1.36 -> OVERWATCH
DECLINETEST: PASS (...)

$ SIGHTLINE_AIDECLINE=0 SIGHTLINE_DECLINETEST=1 ...
DECLINETEST: ambient SIGHTLINE_AIDECLINE=0; gate scene [...] shot hit=21% E[dmg]=1.36 -> SHOOT
  gate:didNotDecline(hit=21 exp=1.36)
  gate:declinedButStillShoots
  gate:killBoxDidNotDecline
DECLINETEST: FAIL (gate:didNotDecline(hit=21 exp=1.36),gate:declinedButStillShoots,gate:killBoxDidNotDecline)
```

**A second review then showed the test was much weaker than "it fails when the feature is off".**
It perturbed each constant one at a time, rebuilding for each, and found **only two of seven were
pinned at all**: `ShotSeat` could be set to **0** — deleting the "a line of fire has option value"
concept DESIGN §5.2 point 1 is entirely about — and the whole suite stayed green. Worse, the
kill-box leg did not test the kill box: `bar *= 1 + DeclineThreatScale * min(guns, cap)` could be
**deleted outright** and all three of its assertions still passed, because they only exercised
`DeclineDigIn = guns >= 2 && canDig`. **The most-argued piece of model in this wave had zero
coverage.** Four legs were added in response (7-10 above), and every one was proven able to fail:

| constant | lower pin | upper pin |
|---|---|---|
| `ShotSeat` 18 | **0 → FAIL** (leg 8) | **35 → FAIL** (leg 2) |
| `DeclineThreatScale` 0.20 | **0.00 → FAIL** (leg 7) | **2.00 → FAIL** (leg 7) |
| `DeclineWatchRatio` 0.45 | **0.35 → FAIL** (leg 3) | none |
<!-- every cell above re-measured on this branch, not taken from the review -->
| `DeclineDigRatio` 0.30 | **0.00 → FAIL** (leg 10) | none |
| `DeclineThreatCap` 3 | **0 → FAIL** (leg 7) | none |
| `DeclineAbsKeep` 3.00 | **0.50 → FAIL** (leg 3) | none — 10.00 passes |
| `FinishPress` 1.6 | **1.0 → FAIL** (leg 9) | none — 9.0 passes |

**Seven of seven now have at least a lower pin; two have both sides.** Every cell in that table
was re-run on this branch rather than copied from the review — including the two the review had
already measured, because a table published under this wave's name should be this wave's
measurement. The straddle (leg 7) is the
one that matters most: one scene, evaluated at one gun and at three, with the aim SEARCHED until
the shot's ratio lands between the two bars. It reports what it found —
`straddle: aim=82 1gun=SHOOT(58%,exp=2.96) 3gun=DECLINE` — so the archive shows the leg was live
and not vacuous. Setting the scale to 0 or 2, or the cap to 0, collapses the window and the leg
fails with `scene:noThreatStraddle` rather than passing quietly.

**The test's own scene was wrong twice before it could fail for the right reason**, and both are
the program's rule 6 (a correct assertion in the wrong scope is indistinguishable from no
assertion): the first hand-picked shooting tile had **no line of sight at all**, so there was no
shot to decline and four legs were failing vacuously; and the exec-site decision labelling put the
terminal-else's `staleplan` tag inside its dry-weapon arm, so its hunker arm fell through
unlabelled and reported as `idle`. Both were found by making the test PRINT its scene rather than
trusting it.

## 8. VERIFICATION

- **Release build 0 warnings / 0 errors.**
- **`bash scripts/qa-sweep.sh --full` — SWEEP-EXIT=0**, **65/65** self-tests PASS (incl. the new
  `DECLINETEST`), COVERAGE GAP block empty, PAIRTEST byte-identical, autoplay x3 WIN/LOSE with no
  TIMEOUT. Full output in `docs/measurements/c2/qa-sweep-full.txt`. (**Two earlier full sweeps
  were run and DISCARDED**, as `qa-sweep-full.interim.txt` and `.interim2.txt`. Both were green,
  and both are worthless as a gate for the same reason: `run()` is `dotnet run -c Debug`, which
  REBUILDS per test, so a source edit landing mid-sweep — even a comment-only one — makes the run
  span two binaries. The sweep of record was started only after the source tree was frozen, and
  nothing but documentation changed after it began. A gate that spanned two builds is not a gate,
  and noticing that twice cost two sweeps. A THIRD run was started after the review's ten
  documentation fixes and then deliberately KILLED at the lead's instruction: those fixes changed
  **no build input** —
  `git diff <pre-fix>..HEAD -- src scripts/qa-sweep.sh Sightline.csproj assets` is empty — so the
  binary under test was byte-identical to the one that produced the result above, and eleven
  concurrent sweeps were saturating a four-core box. **No post-fix sweep result exists and none is
  claimed.** Killing it also truncated `qa-sweep-full.txt` in place, which a routine `git add -A`
  had already committed as a five-line stub; the log was restored from git and the trap is written
  up in `docs/measurements/c2/README.md`. A FOURTH sweep then ran after the code review's D1/D3
  fixes — which DID touch `src/` — and is the log archived above: **SWEEP-EXIT=0, 65/65 PASS,
  COVERAGE GAP empty, PAIRTEST byte-identical, autoplay x3 LOSE/WIN/LOSE with no TIMEOUT.** It was
  written to a scratch path and copied into place on success, which is the rule the truncation
  trap taught.)
  Notable passes for this diff specifically: `COMBATTEST` and `TRUTHTEST` (the new
  `ShotOdds.CoverDef` field and the tooltip), `SAVETEST` (no persisted-enum or map-generator
  movement), `AIIDLETEST` (the no-idle invariant survives declining), `HEATLADDERTEST` (the
  difficulty axis is untouched), and `AITEST` (focus fire and target selection are untouched).
- **`SIGHTLINE_DECLINETEST` FAILS with `SIGHTLINE_AIDECLINE=0`** — output in §7.
- **The INSTRUMENT does not share this code path** — the brief's explicit WATCH OUT. Checked
  rather than assumed: `grep -nE '\bAi\.[A-Z]' src/Game.Autopilot.cs` returns exactly three
  hits, **all of them doc comments** ("the Ai.Plan trick", "Ai.Plan-style"). The player-side
  autopilot re-implements its own scorer and calls nothing in `Ai.cs`, so changing enemy scoring
  changes the WORLD but not the measuring stick, and the before/after are comparable. (The
  R0diag above is the second, empirical half of the same check.)
- **R0diag**: this tree with the dial off vs the base-commit binary, two disjoint slot sets
  (h0/b0 and h4/b10), diffed to **EMPTY** on both — **1304 and 1216 leaf scalars** respectively
  (1916 and 1786 lines of normalised JSON), covering every per-slot RunRec, win, loss, turn count
  and shots-per-kill. The telemetry this wave adds — including the extra `ComputeOdds` +
  `ExpectedDamage` per plan — is gameplay-inert. (An earlier draft said "2750 aggregate fields on
  both". The diffs were and are empty, but that number reproduces under no convention I can find:
  it is neither chunk's leaf count, and quoting one figure for two differently-sized artifacts was
  wrong twice over.)
- **48/48 ladder chunks `OK ... runs=20`**, three-layer completion contract on every one.
- **The ladder binary is the shipped binary.** Three post-ladder tidies landed (comment
  corrections, a redundant local in `ComputeOdds`, and the gate reusing the odds it had already
  computed instead of calling `OddsFrom` twice). All three are provably behaviour-neutral, but
  "provably" is the word this project asks you to check: two ladder chunks — one per ARM
  (`L-DECL-h2-b0`, `L-BASE-h4-b10`) — were re-run on the final binary and diff **EMPTY** against
  their archived JSON outside the `harness` block. Archived as `POST-*`.

## 9. WHAT I DID NOT DO, AND WHAT IT COSTS

- **I did not make declining common, and I will not pretend the shipped rate is large.** It is
  **0.65%** of the shots on the table pooled over the 960-campaign round (1.0-1.4% on the heat-0
  probe chunks, where the calibration was done). The board carries **15.53 / 15.18 contested enemy
  acts per mission** (BASE / DECL), and 105 declines fall over 1724 missions — so a player sees
  the opponent hold fire about **once every sixteen missions**. (An earlier draft wrote "~11.8
  acts per mission" and "every several missions"; both were wrong and both flattered the wave.)
  The rate is a deliberate consequence of pricing the gate against a lane measured to FIRE 24% of
  the time rather than against a target rate; §4's ceiling probe points the same way but is
  suggestive only (p = 0.29, and confounded). **If a later wave wants a felt decline rate, the
  thing to fix first is the LANE, not the gate** — see the two open items below.
- **The enemy overwatch has no lane selection at all.** It is a 360° watch held from wherever the
  unit happens to be standing (only the PIKEMAN's BRACE picks a cone). That is very likely why
  only 24% of held lanes ever FIRE — and fewer still connect — which makes the wave's own
  alternative a weak one. A
  hostile that chose *where* to watch — a chokepoint, the tile a soldier must cross to reach the
  objective — would be worth several times what this one is, and would justify a much higher
  `DeclineWatchRatio`. **Unstarted.**
- **The instrument cannot see area denial, and I did not fix that either.**
  `Game.Autopilot.TileExposure` has a `+18` term for an enemy BRACE lane and nothing for an
  ordinary enemy overwatch. Teaching the bot to route around enemy overwatch would make the
  flywheel able to price the lane properly — but it would also change the INSTRUMENT, which
  invalidates every CRN world in the repository, so it is a wave of its own and must not be
  smuggled into one that also changes gameplay.
- **`DeclineDigIn` is unexercised in play, as far as I measured.** It is pinned by DECLINETEST
  leg 6 on a constructed board, but the two probe chunks run with and without it
  (`CAL-e` / `CAL-f`, same slots, same seeds) came back **identical on every field**, so the
  "two or more guns AND cover to hand AND a bad shot" conjunction did not occur once in those 20
  campaigns. **I did NOT instrument it separately over the 960-campaign round**, so "never fires"
  is not a claim I can make — what I can say is that it did not move a paired probe and that no
  counter exists for it. A follow-up that cares should add one.
- **Four of seven constants are still pinned from ONE side only.** `DeclineWatchRatio`,
  `DeclineDigRatio`, `DeclineThreatCap`, `DeclineAbsKeep` and `FinishPress` have lower pins but no
  upper one: `DeclineAbsKeep` can be raised to 10.00 and `FinishPress` to 9.0 with the suite green.
  Both are inert in that direction at the shipped ratios (a 0.45-ratio shot is weak by
  construction, so the absolute guard cannot bind; and a finish-band target is rare), which is why
  the missing pin costs nothing today — but a wave that RAISES the ratios inherits an untested
  guard, and should add the upper legs before it does.
- **NONE of the seven scoring constants is a measured optimum, `ShotSeat` least of all.** An
  earlier draft said "the three decline constants were calibrated"; that over-states what the
  `CAL-*` chunks did. They were a **swept ladder of candidate settings scored on a single
  20-campaign slot set** — enough to reject an absolute-damage bar and to bound the rate, not
  enough to locate a value, and no chunk was replicated across slot sets. `DeclineWatchRatio` is
  the declared judgement §4 now frames correctly; `DeclineDigRatio` and `DeclineAbsKeep` were set
  by the same reasoning and are, on the shipped round, largely inert (`AbsKeep` cannot bind at a
  0.45 ratio); `ShotSeat = 18` was never swept at all — it is set to one level of cover because
  that is the trade the term arbitrates. A wave that wants to move the positional behaviour
  further should sweep `ShotSeat` across four slot sets before touching the gate.
- **I did not touch the shot term for a ROUTING unit** (`bestHit * 0.25`, UNDERTOW W3) or the
  target-selection loop. Both are load-bearing for other tests and neither is the defect.
- **`byObjectiveByNodeKind` was not consulted for this wave's conclusions.** Nothing here is a
  per-objective claim, so rule 1 does not bite — but if a follow-up wants to argue that declining
  helps or hurts a particular objective, it must use the cross-tab.
- **No CI, no test framework, no new dependency, no asset.** Nothing added that costs money.

# PROGRAM CONTOUR — C5 "THE HARD EDGES" (2026-08-30, dev on `wave/hard-edges`, base `17934ee`)

A robustness / coverage / defect-hunt wave. **No balance lever, and no balance number is quoted
that this wave did not itself produce.** Its job was the four gaps CROSSCUT declared honestly
rather than closed, plus a fresh hunt on the composed tree.

## THE THESIS

Every gap on the list has the same shape: **the tests are aimed at a MODEL and the failure lives at
a SEAM.** W10 asserted five surfaces' layout arithmetic and the game draws forty screens. W9's idle
guard asserts the player turn and the enemy turn has its own state machine. Every AI test asserts
that a decision is CORRECT GIVEN A BOARD, and a branch that is never reached is correct on every
board it never reaches. `save.json`'s guard asks three questions of a file that has thirty fields.
So this wave's instruments all move the assertion to the seam: audit the DRAW, not the layout;
guard the frame loop, not the stage machine; census what the opponent DID, not what it should do;
resume a hostile save and then PLAY it.

## GAP 1 — the text-scale gate now covers the game, not five surfaces

`FITTEST` gains leg **(F), THE SCREEN AUDIT**: forty staged screens, drawn for real at all four
shipped text sizes, with the geometry read back from the draw calls themselves
(`Cfg.InkProbe`, `Hud.PlateProbe`, `Hud.ClipProbe`, `Hud.FloorProbe`). It asserts, per screen per
scale:

1. **every control plate contains its label** (tightest measured margin **1.4px**, the in-mission
   ammo pip `8/8` at 120%);
2. **no visible string leaves the canvas** (nearest **8.6px**, the draft's `[R]` hint at 120%);
3. **nothing is ellipsized** — a geometry audit is structurally blind to this, because a clipped
   string's painted box FITS; losing the tail is what made it fit;
4. **nothing paints below the shipped type floor** (measured smallest: 10px authored / 12px
   rendered);
5. **the screen drew, and drew ITS OWN frame** — a fingerprint collision between two screen cases
   means one of them never staged.

Legs A–E are unchanged in substance. What changed for them is **THE SCOPE GUARD**, and it took two
review rounds to make it mean what it says. CROSSCUT rule 6 asks for a mechanical answer to "is the
assertion inside the loop?"; the first version counted `Bump` calls placed *next to* the
assertions, and **the review defeated it in two edits**: create a real 120%-only defect
(`PlateSlack 1.5 → 1.0`, which fails with 18 violations, all at `@120%`), then gate only the
ASSERTION on `S == "@100%"` while leaving its bump — **PASS, with a byte-identical count, while 18
real violations went unreported.**

So the guard is now three things, and the third is the one that earns the claim:

1. **`Check(leg, cond, msg)` bumps and asserts in one statement**, so the number is a count of
   assertions *evaluated*, and moving, gating or deleting one moves the number with it.
2. **Every leg must record checks at every scale** (the original guard), which still catches the
   W5 defect verbatim — demonstrated by gating leg F to `ui == 1f`:
   `legOutsideScaleLoop:scr:INTRO@90%(0 checks)`, 120 of them.
3. **A SENSITIVITY CONTROL runs on every invocation.** Binding still cannot see a *narrowed
   condition*, so FITTEST re-runs the whole screen audit at a deliberately **unshipped 200% text
   scale**, where fixed-pixel chrome must break, and FAILS if the assertions do not fire there
   (today: **490 plate / 11 off-canvas / 57 ellipsis** violations, counted and discarded). Against
   that, the review's step-2 mutation reads
   `sensitivityDead:labelLeavesPlate never fired at the 200% control`.

**And the residual limit, stated rather than implied:** this proves an assertion RAN at each scale.
It cannot prove the CONDITION was not narrowed — nothing short of mutation testing can — and the
200% control is an empirical backstop for that, not a proof. Current run: **14,132 assertions over
45 legs**, every leg at all four scales, plus the control.

**What leg F does NOT see, stated because "it IS the draw" is the claim that sells it.**
`Hud.PlateProbe` covers the four button helpers plus `CenterText` — **7 call sites**, not "every
button in the game"; a control that draws its own plate and label by hand is invisible to it. And
`Cfg.InkProbe` only sees text routed through `Cfg.Text`/`Cfg.TitleText`: review found **four raw
`Raylib.DrawTextEx` sites** — the volume fader's label and percentage, and the THREAT CARD's title
and body — which were therefore invisible to the audit *and* frozen while the rest of the UI scaled,
on two screens leg F claims to audit. **Fixed** (they now go through `Cfg.Text`/`Cfg.Measure`, which
is what CLAUDE.md requires); the point that survives is that the probe is only as complete as the
funnel it sits on, and `grep -c "Raylib.DrawTextEx" src/*.cs` is the check.

Supporting mechanics, all inert in play: `Hud.AnimPin` (audit the settled frame without waiting
real seconds), `Hud.TimePin` (the chrome's twelve wall-clock reads now route through one `Now()`,
mirroring `Renderer.TimePin` — CLAUDE.md has said "if you write a test that reads pixels, pin the
clock" since W4 and only the board could), and one fixed RNG seed per staged screen (the shop slate
and the event roll are clock-seeded), and **`Hud.MousePin`** — added after review measured the gate
**flaking at ~3% (1 run in 32)** on a TOOLTIP-HOVER/TOOLTIP-AIM fingerprint collision. Thirty draw
sites read the live cursor (hover fills, hover cards, and the threat card, which anchors itself at
it): the clock, the animations and the RNG were pinned and the POINTER was not. The same gap
explains why this entry's own assertion count differed by 16 between two machines on one commit.
The audit now parks the pointer off-canvas. Determinism verified 3× on the shipped tree.

## GAP 2 — the enemy turn has a deadlock guard, and it names what stalled

`Game.EnemyStallGuard` runs from `Update` **before the animation pump**. That placement is the whole
design: while `_anims` is non-empty `Update` returns before the phase switch, so a guard inside
`UpdateEnemy` is structurally blind to the "an animation never completes" half of the deadlock —
the same shape of blindness W9 found in the turn-boundary guard. Progress = staging index, stage,
queue depth, queue head type, and every unit's position/HP/actions/ammo/state; a banner beat resets
the counter, so a telegraph is never mistaken for a stall. After 480 updates (8 s) with nothing
moving it prints

```
ENEMY-STALL: enemy turn made no progress for 480 updates. stage=PickNext
unit=STALKER/SCOUT #0 at (12,7) hp=7 act=2 ammo=4 plan=none anims=0 head=- turn=1 mission=1 mode=Campaign
```

and then gets out: the queue is dropped, the stalled unit forfeits its turn, the index advances,
and if that exhausts the staging list the turn ends. **It runs in real play, not only in autoplay** —
a batch loses a run, a player loses the session.

`SIGHTLINE_ENEMYSTALLTEST` wedges a real enemy turn (`Game.DebugEnemyWedge`) and asserts: the guard
fires within its bound (**520 updates measured**, bound 480); the line names unit/stage/plan/queue;
the wedged turn ENDS (962 further updates); **108 clean enemy turns fire it zero times**; and
480 x 24 units < the 120,000-frame harness budget, so this door cannot re-open the TIMEOUT. Its
non-vacuity leg is the one that makes the rest mean anything: **the same wedge with the guard off
still hangs** after 2929 updates.

## GAP 3 — the enemy DECISION CENSUS, and a correction to the finding it guards

Every branch of the enemy exec chain now tags itself (`Game.ActBranches`, one string assignment per
enemy act). `SIGHTLINE_AICOVTEST` walks 48 campaigns across heats {0,4,8} x all eight objectives and
counts what the opponent actually did.

**A ZERO GATE WOULD NOT HAVE CAUGHT THE DEFECT IT IS WRITTEN FOR, and that is the finding.** Measured
here over 144 campaigns / **8083 enemy acts**: `overwatch` fires **8 times, 0.10%**. The ROADMAP
records it as "0 of 1595 pre-W2 and 3 of 1589 post". On this tree, at this sample, it is not zero —
it is once per thousand acts, which is a verb no player will ever see. So the gate is a **RATE**
(once per 1000 acts) and the effectively-dead branches are a **declared registry**
(`AiCovKnownRare` = overwatch, relock, shove, with the rates measured here). An *undeclared* branch
that falls below the rate fails by name; a declared one that climbs back is reported as a stale
declaration (a note, not a failure — the wave that revives a verb should be told to delete the
entry, not blocked by it). `SIGHTLINE_AICOVSTRICT=1` treats every declared entry as a failure.

Census at n=2740 acts: `shoot 40.18% · hunker 17.08% · grenade 2.52% · heal 1.90% · siege 1.28% ·
reload 0.95% · sap 0.47% · brace 0.47% · item 0.36% · shove 0.11% · relock 0.04% · overwatch 0.00% ·
terminal-hunker 0.22% · terminal-reload 0.04% · none 34.38%`.

**Read the two sample sizes carefully, because they say different things.** The headline
"overwatch fires 8 times in 8083 acts" is a **144-campaign** run (`SIGHTLINE_AICOVTEST=6`). The
sweep runs `=2` — **48 campaigns, 2740 acts — and at that n overwatch is 0**. The gate is a rate of
one per 1000 acts, so at the sweep's n the threshold is **2.74 events**: it is asking "does every
undeclared branch fire at least 3 times in 2740 acts?", and `item` (10), `sap` (13) and `brace` (13)
are the marginal ones at 3.6–4.7× the bar. That is a deliberate trade — a bigger n in the pre-merge
sweep costs minutes — but it means the sweep's version of this gate is a coarse one, and a wave that
suspects a verb has gone quiet should run `=6` by hand.

The three **backstops** (`terminal-reload`, `terminal-hunker`, `none`) are censused and reported,
never gated: they exist so W2's no-idle-act invariant holds structurally when a plan goes stale.
`terminal-reload` measured **0 in 8083 acts**, which is the design working rather than a hole.

**Lane discipline:** C2 owns `Ai.cs`'s shot scoring, the CAUSE of the dead overwatch branch. This
wave changed no scoring; it owns the test that would have caught it.

## GAP 4 — the defect hunt

### FIXED

1. **The wrapper and the clipper disagreed by less than a pixel, and the player paid three
   characters.** `WrapText` decides a line fits with `(int)Measure(...) > maxW` — the int cast lets
   up to a pixel through — while `Clip` decided with the raw float. A 224.7px string in a 224px
   column was therefore ONE line to `FitWrap` (which stopped shrinking) and TOO WIDE to `Clip`
   (which ate the tail). Live at the 110% text size: the WAR ROOM painted **"Win a run with no
   soldiers los…"**. VOICETEST asserts exactly this contract for achievement descriptions and could
   not see it, because its assertion goes through `WrapCount` — the same int-cast path that called
   the string fine. Both now ask the width question the same way. Screenshots before/after taken at
   110%.
2. **A `null` element in a save's Squad array threw a NullReferenceException on resume.**
   `FromUnitDto` returns null for a null element and its own comment says "callers skip nulls" —
   the caller that builds the RUN's roster did not.
3. **An all-benched roster resumed into a mission with an EMPTY BOARD.** `Game.ToggleBench` enforces
   ">= 1 deployed" at the UI, so no click can empty the field; `Benched` is also a persisted
   per-soldier flag with no guard on the way in, and `SetupMission` builds `Players` straight from
   `Squad.Where(!Benched)`. `Run.EnsureFieldable` now holds the invariant at setup using the
   deployment rule that already exists (best `NextDeployCap`, healthy and senior first), so a
   player's own bench choices are never touched.
4. **`SIGHTLINE_BENCH=1` staged nothing.** `DebugBench` set `Wound = 2` and then called
   `DebriefSurvivors`, whose recovery step decrements a survivor's wound (`u.Wound--`, `Run.cs`) —
   so the flag was spent before anything drew and the hook photographed the plain barracks. Found as
   a frame byte-identical to the audit's CAMPAIGNMAP case. (Two corrections from review: the
   decrement is the `u.Wound--` arm, not the unhurt-branch's −2; and the hook's docstring premise —
   "so the BENCH toggle buttons are visible" — is stale, because the DEPLOY/BENCH pill draws on
   EVERY roster row. What it actually stages now is the WOUNDED(n) line.)
5. **`AIIDLETEST` was the one self-test in `qa-sweep.sh` not routed through `verdict`** — real, and
   **the consequence I first published for it was false**, which is worth more than the fix. I wrote
   that a FAIL there "left `SWEEP-EXIT=0`". It does — but so does *every other* FAIL, because
   `qa-sweep.sh` **contains no `exit` statement at all**: `_fail` and `_autofail` are set and never
   read (verified here: assignments at 49/51/53/175/199/208/215, zero reads in a condition that
   exits). The provenance, established by the lead: W9 shipped `exit 0` as a review fix
   (`3304c41`), the **W5 merge (`4ba1ed3`) deleted it**, and CLAUDE.md has asserted the gate for two
   programs since. So this fix is a *precondition* for the exit code to mean the whole sweep — when
   wave **C3** lands the `exit` (its half; C5 deliberately did not duplicate it), an AIIDLETEST FAIL
   will now be counted. **CLAUDE.md's "Since W9 the sweep EXITS NON-ZERO on any FAIL line" is false
   until C3 merges** — left for C3 rather than edited into a conflict.
   C5's own half of the same defect: `scripts/qa-isolated.sh` ended its `--sweep` branch with
   `echo "SWEEP-EXIT=$?"`, so the *wrapper* exited 0 whatever the sweep returned. Fixed and proven
   with a fake sweep returning 7 — old wrapper `WRAPPER-EXIT=0`, new wrapper `WRAPPER-EXIT=7`.
6. **The PERK CHOOSER's fourteen numbers were transcriptions — and this one is stated carefully,
   because it is a SEAM and not a live lie.** Every "before > after" line on the card
   (`HP 8 > 11`, `AIM 68 > 83 vs flanked`, `DMG TAKEN -1 (crits -4)`, ...) was a literal typed
   beside the resolver's constant. **All fourteen were CORRECT when measured** — nothing shipped a
   wrong number. What was wrong is that nothing bound them: the card is the only place in the game
   those numbers appear, so a wave retuning `Unit.PerkAim` to 12 would have left fourteen cards
   promising +15 with no test in the project able to notice. That is the
   displayed-hit%-was-not-the-hit-probability class, one screen over, waiting. The lines now read
   the constants (`Unit.TankHp` / `SprinterMob` are new, and `Run.ApplyPerk` reads the same two),
   and `TRUTHTEST` gained a leg that MEASURES each claim through the shipped path that grants it —
   `Run.ApplyPerk` for the stat bumps, a real mission refill for BANDOLIER, `Combat.ComputeOdds`
   for the aim and crit perks (including SCANNING the board for CLOSE QUARTERS' and MARKSMAN's
   range gates, so "inside 4 tiles" is measured too), `Combat.HardenedReduce` for the damage cuts —
   and then asserts the chooser PAINTS its own formatter's string. Proven to fail both ways: set
   `Unit.PerkAim = 12` with the old literal in place and it reads
   `perkCardLies:LockOn:'AIM 70 > 85 vs flanked' has no 82`; stop painting the line and it reads
   `perkCardDidNotPaintItsOwnLine`.

### FOUND, NOT FIXED — with the repro

- **The 12px small-text floor is not met by the shipped UI, at the default scale.** CLAUDE.md
  declares 12px the floor. Measured at 100%: **10px** on the AUDIO CHECK screen (12 strings at 10px
  + 63 at 11px = 75 below the floor on one frame) and **11px** on seventeen other screens including
  the in-mission HUD, the end cards, the WAR ROOM and the EVENT card — **109 strings over exactly 18
  screens**. Repro: `SIGHTLINE_FITTEST=1 SIGHTLINE_FITDUMP=small` lists every one with its authored
  and rendered size. **Scope, stated accurately after review:** no PRIMARY information is affected —
  hit%, damage, ammo, objective, unit names and action-bar labels are all ≥12px — and the 11px cases
  are ~4 call sites repeated across 11 staged screens, so "a re-layout of half the chrome" is fair
  for AUDIO CHECK and overstated for the rest. **Not fixed** because those call sites sit inside
  other waves' surfaces and this wave's gate would then enforce a design change nobody has priced.
  What ships instead is a REGRESSION BOUND **at the measured worst — 10px authored / 9.0px
  rendered** — so any further shrink fails. (It was first set at 9px authored, one point of slack
  below the measurement, which would have let a 10→9 regression on AUDIO CHECK pass in silence;
  review B3.) **Setting a bound at the worst entrenches the breach**, which is the disclosure this
  rule asks for: the next wave that wants the rule met starts from the list, not from the bound.
- **Six shrink-to-fit calls reach their floor at 120%** — three WAR ROOM achievement descriptions,
  one shop body, one prep body, and the same shop body again on the worst-case SHOP-WORST staging
  (the sixth, which the first draft of this list omitted while quoting the count of six). They still
  fit — nothing is lost — but they are one authored character from losing a word. Counted in the
  FITTEST PASS line on every run.
- **THE SETTINGS ARE BEHIND A DOOR YOU CANNOT OPEN UNTIL YOU ARE IN A FIGHT — and this is an
  ACCESSIBILITY gap, not a comfort one.** `Update`'s Escape handler is gated on
  `Phase == PlayerTurn || EnemyTurn`, and the pause card is the sole home of **TEXT SIZE,
  COLORBLIND, BRIGHTNESS, GAMMA, ANIM SPEED, SCREEN SHAKE, THREAT PREVIEW, AUTO-CAM and
  FULLSCREEN** (verified against `DrawPause`). The INTRO — the screen a player meets first — offers
  CONTINUE / DEPLOY / TRAINING / LAST STAND / WAR ROOM / FIELD MANUAL / SKIRMISH / DAILY /
  AUDIO CHECK / QUIT and a difficulty dial, and **no settings entry of any kind**. So on first
  launch a player cannot set the text size or turn on colourblind mode until they have started a
  mission and pressed Escape. **It compounds the finding above exactly**: the 120% setting that
  would lift every sub-12px string to ≥12px is behind the door that cannot be opened.
  **BARRACKS is worse:** `case Phase.Barracks` has no Escape handler at all — no pause card, no
  route back to the intro, no field manual (`K` is gated on `Phase.Intro`) — so on the screen where
  a player deliberates over perks, the shop and the node pick, the only exits are forward or the
  window's close button. (Not a soft-lock: forward always exists. Draft, SkirmishSetup, WarRoom,
  Codex, AudioCheck and the end cards all take Escape — walked and confirmed.)
  **Correction to my first write-up:** VOLUME *is* reachable outside a mission — the four faders
  live on AUDIO CHECK (`Hud.Audition.cs`), which the intro opens with `U`.
  Not fixed here — it is a keymap and menu-structure change across five phases, on surfaces this
  wave does not own — but it is filed as a real open item rather than "a UX decision", and
  `QUITTEST`, which asserts the arm/confirm/checkpoint contract, **asserts nothing about
  reachability from any phase**, which is the half that is broken.
- **`plan=none` is what the stall line prints when the stall is at `PickNext`**, because the plan
  for that unit has not been made yet. Truthful, but the diagnosis is weaker at that stage than at
  `ActAfterMove`. Left as is rather than fabricating a plan to name.
- **String-vs-string overprint is not asserted, and my first reason for that was wrong.** I wrote
  "no z-order signal"; `Cfg.InkProbe` fires **in draw order**, so z-order is exactly what it does
  have. What is missing is an **occlusion** signal — whether an opaque fill landed between two
  strings — and acquiring one means funnelling **230** raw `Raylib.DrawRectangle*` calls in
  `Hud.cs` and **87** in `Renderer.cs` through a `Cfg.Rect` seam. That is a real refactor and
  declining it is a scope call, not an impossibility: the project already asserts overprint in two
  hand-scoped places (FITTEST's own armory tag-vs-blurb leg and the hall-of-fame score-vs-name leg),
  so this is a gap with a known price.

## VERIFICATION

- Release build **0 warnings / 0 errors**.
- `bash scripts/qa-isolated.sh --sweep --full` (the house sweep with the isolation exports the
  contract requires, now a committed script instead of four lines a reader has to remember):
  every line PASS, COVERAGE GAP empty, autoplay x3 clean (LOSE m3 / LOSE m1 / LOSE m5, no TIMEOUT),
  PAIRTEST byte-identical. FITTEST's final line: 40 screens, **14,132 assertions over 45 legs**,
  every leg at all four scales, plus the 200% sensitivity control. **On the sweep's exit code, read
  fix 5 above and do not quote `SWEEP-EXIT=0` as a whole-sweep verdict**: `qa-sweep.sh` has no
  `exit`, so the code is the last `echo`'s. C5's wrapper now propagates whatever the sweep returns;
  C3 restores the sweep's own `exit`. Coverage, DERIVED (never typed — this footer has been
  wrong six times): **67 hooks exist in `src/`, 67 run**, up from 64 at the base commit — this wave
  adds ENEMYSTALLTEST, AICOVTEST and SAVEEDGETEST. COVERAGE GAP block empty.
- **Every fix ships a test proven to FAIL on the pre-fix tree** (reverted, run, output pasted in the
  handoff): the ellipsis leg, the staging fingerprint, the scope guard, the enemy stall guard, both
  save shapes, and both AICOVTEST modes.

## THE INSTRUMENTS THIS WAVE LEAVES BEHIND

| hook | what it holds |
|---|---|
| `SIGHTLINE_FITTEST` (leg F) | 40 screens x 4 text scales, audited on a live frame |
| `SIGHTLINE_FITDUMP=1\|small` | per-screen ink/plate census; every string below the 12px floor |
| `SIGHTLINE_ENEMYSTALLTEST` | the enemy-turn deadlock guard, wedge and all |
| `SIGHTLINE_AICOVTEST=<N>` | the enemy decision census (`SIGHTLINE_AICOVSTRICT=1` drops the waivers) |
| `SIGHTLINE_SAVEEDGETEST` | eight hostile `save.json` shapes through the real resume path |
| `Hud.TimePin` / `Hud.AnimPin` | the chrome's clock and entrance animations, pinnable for any pixel test |
# PROGRAM CONTOUR — WAVE C3 "THE TWO GAMES"

**Branch `wave/two-games`. Base commit `17934ee`** (CROSSCUT composed — the L3 ladder's tree).
Raw data, runners and readouts: `docs/measurements/c3/`. Gate: `SIGHTLINE_CLASSTEST`.

## THE FINDING, RE-ESTABLISHED ON THIS TREE

960 campaigns, 48 chunks, all `runs=20`, `byObjectiveByNodeKind` and `byObjectiveByMission`:

| on mid-run campaign nodes (Combat + Elite) | win% | ±SE | n |
|---|---|---|---|
| **KILL** — Eliminate, Decapitate | **38.5** | 3.1 | 247 |
| **NON-KILL** — the other six | **81.5** | 1.1 | 1243 |
| **gap** | **43.0** | 3.3 | |

`byObjectiveByMission` says the same thing at fixed depth: at **m5**, `Eliminate` 23.8% (n=42)
against `Sabotage` 83.9% (n=31) and `Evac` 93.8% (n=16). The brief's figures (38.3 / 83.4, and
Sabotage 96.8 at m5) came from W8's own round on a different composition; they replicate in
direction and magnitude, and the cells with n<40 differ inside their own error. **W8's trap is
real and it is worth restating: `Eliminate`'s POOLED row on this tree is 89.1%, because 960 of its
1155 rows are mission 1.** The pooled table is in `agg.py`'s output and it is the wrong table.

## 1. THE INSTRUMENT — because a win rate cannot say WHY

Nothing recorded what the enemy force GREW to, or how much of it a squad actually had to beat, so
every mechanism for the gap was equally plausible. `Stats.MissionRec` gains two per-mission
counters — `EnemiesAdded` (bodies spawned after deploy by ANY reinforcement path) and
`MaxPressure` (the high-water anti-turtle rung) — and the report gains an **ENCOUNTER COMPLETION**
block, console and JSON, restricted to mid-run node kinds for W8's exact reason. Its load-bearing
column is `clear%` = killed / (deploy force + reinforcements): the share of the encounter actually
FOUGHT, and the only column that can tell "this class wins the fight" from "this class never has
the fight". The same columns are repeated restricted to **won** missions, because a low `clear%`
otherwise has two readings that point in opposite directions.

**Proven inert before it was used**: `inert.py` diffs every pre-existing field of 8 paired chunks
(2 rungs × 4 slot sets, same worlds, pristine vs instrumented binary) — **9,848 aggregate fields,
zero moved.**

## 2. THE MECHANISM — three candidates refuted by measurement, one survives

`enc.py B1`, mid-run nodes, 960 campaigns:

| row | n | win% | turns | force | +rf | clear% | rf% | prs | WON n | WON clear% |
|---|---|---|---|---|---|---|---|---|---|---|
| Eliminate | 139 | 37.4 | 8.53 | 7.78 | 1.69 | 48.8 | 41.0 | 1.55 | 52 | 100.0 |
| Decapitate | 108 | 39.8 | 5.42 | 9.06 | 0.55 | 14.4 | 20.4 | 0.79 | 43 | 25.3 |
| Defend | 425 | 78.1 | 8.64 | 5.56 | 6.48 | 35.5 | 100.0 | 0.00 | 332 | 40.2 |
| Escort | 247 | 83.0 | 8.12 | 8.41 | 0.00 | 17.4 | 0.0 | 0.00 | 205 | 14.0 |
| Evac | 98 | 90.8 | 5.68 | 8.14 | 0.00 | 4.1 | 0.0 | 0.00 | 89 | **3.3** |
| Hack | 102 | 81.4 | 3.67 | 8.96 | 0.04 | 14.6 | 2.0 | 0.16 | 83 | 14.5 |
| Rescue | 220 | 83.6 | 5.44 | 8.35 | 0.00 | 14.5 | 0.0 | 0.00 | 184 | 12.2 |
| Sabotage | 151 | 79.5 | 3.76 | 5.71 | 0.00 | 24.1 | 0.0 | 0.00 | 120 | 25.8 |
| **KILL** | 247 | 38.5 | 7.17 | 8.34 | 1.19 | 33.6 | 32.0 | 1.22 | 95 | 62.0 |
| **NON-KILL** | 1243 | 81.5 | 6.73 | 7.12 | 2.22 | 24.3 | 34.4 | 0.01 | 1013 | **25.1** |

**(a) FORCE SIZE — refuted.** KILL deploys 8.34 bodies, NON-KILL 7.12 — but the NON-KILL average
is dragged down entirely by the two objectives the game *already* eases on purpose (`Defend` 5.56
via FUL-4's opener trim, `Sabotage` 5.71 via `Mission.Build`'s explicit easing). The four
untrimmed non-kill objectives deploy **8.14 – 8.96**, at or above the kill class. `Escort` deploys
8.41 and wins 83.0%; `Eliminate` deploys 7.78 and wins 37.4%. Force size does not order the classes.

**(b) TURN PRESSURE / MISSION LENGTH — refuted.** KILL 7.17 turns, NON-KILL 6.73. `Defend` is the
LONGEST objective in the game at 8.64 turns and wins 78.1%. And `Hack` carries *the same
anti-turtle clock as Eliminate* and wins 81.4%.

**(c) REINFORCEMENT VOLUME — refuted, and this is the one worth stating loudly.** `Defend` takes
**6.48** added bodies per mission and is reinforced on **100%** of missions; `Eliminate` takes 1.69
on 41%. Defend wins 78.1%, Eliminate 37.4%. **Nearly four times the reinforcements and twice the
win rate.** Volume is not it.

**(d) WHAT SURVIVES: the win condition's dependence on beating the force.** A WON non-kill mission
kills **25.1%** of the force it deployed against — `Evac` **3.3%** (0.27 bodies of 8.14),
`Rescue` 12.2%, `Escort` 13.3%, `Hack` 14.5%, `Sabotage` 25.8%. **FIVE of the eight objectives are
routinely won by declining three quarters of the encounter.** The two kill objectives cannot
decline any of it.

**FIVE, NOT SIX — `DEFEND` DOES NOT DECLINE ANYTHING, AND THE REVIEW WAS RIGHT TO CATCH IT.**
Defend's won-mission clear% is 40.2 and its raw body count is **4.28 kills per mission — 92% of
Eliminate's 4.65, and more than any other non-kill objective in the game.** Its low percentage is a
DENOMINATOR artifact: it fields 5.59 at deploy and is then replenished with 6.47 more, so its
denominator is 12.04, the largest on the board. Defend does not walk past its encounter, it
**outlasts a bigger one**. And Defend is 34% of the whole non-kill cell (425 of 1243), so the
pooled `NONKILL` clear% of 25.1 is itself dragged UP by the one objective that fights hardest.
Strip Defend out and the remaining five read 0.32–1.41 kills a mission against deploy forces of
5.7–8.5. The finding survives — it is just five objectives, and the honest version is stronger
about which five.

That is not a difficulty bug inside an objective. It is two different games sharing a UI, and the
only thing separating them is whether the mission can end while the enemy is still standing.

**Where (c) DOES bite, and it is an interaction, not a volume.** On `Eliminate` — and on no other
objective in the game — a reinforcement is also **win condition**. The anti-turtle clock's second
arm therefore does not raise the price of the finish line there; it MOVES it. 41.0% of mid-run
Eliminates took at least one wave, and over those missions the waves averaged ~4.1 bodies on top
of a 7.78-body deploy force. On `Hack` and `Decapitate` the same clock adds the same bodies and
the win condition does not move — which is exactly why Hack reads 81.4% with `prs` 0.16 and
`Eliminate` reads 37.4% with `prs` 1.55.

**A fifth candidate, named and NOT ruled out: the measuring bot.** `Game.SmartStep` is what chose
to walk past those fights. A player who fights anyway on an `Evac` would see a much smaller gap.
That does not rescue the design — the point is that declining is *available and dominant*, and the
flywheel is the thing that found it — but this wave did not measure a fight-anyway policy, and
`clear%` is a joint property of the game and of the bot. Said plainly rather than buried.

## 3. THE DESIGN CALL — it is a defect, and it is TWO defects

`docs/DESIGN.md` was the deciding document, not the win rate.

**The variety is a FEATURE and it is kept.** §3.F wants runs to differ by *combinations*, and a
campaign that alternates "clear the room" with "get in and out" is exactly that. Making all eight
objectives demand attrition would delete the best thing about the objective roster. **Any lever
that closes this gap by making the six harder was rejected on that ground alone.**

**Defect one: the fork is a FALSE CHOICE (§3.A).** "A decision is interesting only if no option
dominates, the options are asymmetric, and *the player can make it informed*." The campaign map
named the objective and nothing named the class, so the single largest predictor of a node's
difficulty — bigger than any step on the heat ladder — was the one property the player could not
read. A fork whose dominant term is invisible is a non-decision wearing a decision's costume.

**Defect two: it is a difficulty SPIKE the player cannot see coming (§3.D).** The within-run curve
is 98 / 83 / 77 / 78 / 73 / 68 — the stair-step §3.D asks for. A mid-run kill node sits at 38.5%
*inside* that curve. §3.D's rule is "pace spikes; give the player a beat to find footing" and
"don't front-load anxiety"; an unreadable 43-point step is the same failure one layer up.

So the answer is **one information change and one mechanical lever**, and they do different jobs:
the information change makes the choice real, the lever pulls the spike toward the curve. Neither
alone is the answer, and the wave claims neither is sufficient.

## 4. THE LEVER — the anti-turtle clock stops moving ELIMINATE's finish line

`Game.ClockMayReinforce`. The clock has two arms and they are not the same thing:

- the **AIM ramp** (+3/+4 per rung to 4 rungs) makes a slow squad's position worse — untouched,
  banner and HUD meter and all;
- the **REINFORCEMENT wave** (rungs 2–4) adds bodies — suppressed on `Eliminate` **only**.

`Hack` and `Decapitate` carry the same clock and keep both arms, because their win condition does
not count bodies. `SIGHTLINE_KILLTREADMILL=1` restores the pre-C3 clock exactly.

**`Decapitate` is therefore this round's WITHIN-ROUND CONTROL on the class**, and `Hack` is the
control on the clock. If either had moved, something other than the lever moved it.

### The measurement

Two rounds on ONE binary — `B1` = `SIGHTLINE_KILLTREADMILL=1`, `L1` = defaults — 6 rungs × 8
disjoint CRN slot sets × greedy+sloppy = **960 campaigns per arm on identical worlds**, 96 chunks,
all `OK ... runs=20`.

**THE LEVER'S OFF PATH IS THE LADDER OF RECORD'S OWN TREE**, and the proof is now against COMMITTED
data. `l3repro.py` compares B1 to the **L3 archive** (`docs/measurements/l3/`, the tree the ladder
of record was measured on, and the same eight slot sets) at two levels: **48 chunk pairs, 59,010
pre-existing aggregate fields (1,075–1,335 per chunk), zero differences**, and — the level that
actually matters — **960 of 960 campaigns identical on BOTH outcome and missions-cleared.** So B1
is not merely "a baseline I ran"; it is L3, reproduced campaign-for-campaign, and `L1 − B1` prices
the lever and nothing else.

*This replaces the proof an earlier draft cited.* That draft pointed at `samearm.py D0 B1` (67,956
fields) — but `trim.sh` had deleted the D0 round as redundant, so **the documented command matched
zero files and printed a pass over no data**. That is precisely the failure CLAUDE.md's W1 contract
exists to eliminate, and the second time this program has shipped it. Both `samearm.py` and
`inert.py` now **exit 2 on an empty comparison**, and the claim above rests on data that is in the
repository.

| rung | B1 | L1 | Δ | ±cluster SE (L1) | band | verdict |
|---|---|---|---|---|---|---|
| RECRUIT | 71.2 | **73.1** | +1.9 | 4.90 | 75±8 | in |
| heat 0 | 47.5 | **55.0** | **+7.5** | 4.53 | 55±8 | in — on target |
| heat 2 | 31.2 | **34.4** | +3.2 | 5.04 | 40±8 | in, **but see below** |
| heat 4 | 23.8 | **25.6** | +1.8 | 2.58 | 30±8 | in |
| heat 6 | 20.0 | **20.0** | 0.0 | 3.78 | 20±8 | in — on target |
| heat 8 | 6.9 | **7.5** | +0.6 | 2.31 | 10±5 | in |

**All six rungs are inside their bands.** Two qualifications, both of which the review supplied and
both of which belong next to the table rather than in a footnote:

- **"Monotone at every step" is not something this lever achieved.** The BASELINE arm is already
  monotone (71.2 > 47.5 > 31.2 > 23.8 > 20.0 > 6.9). It is a pre-existing property of the tree.
- **±3.5–3.9 understates a rung's sampling error, and the eight slot sets are not exchangeable
  draws.** A rung is 8 clusters of 20 campaigns, and the clusters disagree: L1's heat-2 slot sets
  read **45, 25, 20, 10, 50, 40, 45, 40**. The cluster SE (SD of the eight slot-set means / √8) is
  **5.04** against a binomial 3.75 — and **the cluster SE exceeds the binomial at five of six
  rungs** (ratios 1.40 / 1.15 / 1.34 / 0.75 / 1.20 / 1.11), so the binomial figure is 11–40% too
  small everywhere but heat 4. The same is true of B1 (ratios 1.08–1.48, heat 4 again the
  exception), i.e. this is a property of the slot sets, not of the lever.
  **So heat 2 is not robust.** It clears its floor by 2.38 points = **0.47 cluster SE**; the
  jackknife worst case (drop the strongest slot set) is **32.14 against a floor of 32**; its paired
  p is 0.0625. The honest sentence, and the one that replaces "L3's one miss is closed": **heat 2
  moved from 0.8 below the floor to 2.4 above it, which is inside the rung's own slot-set noise.**

**±SE of any kind is the WRONG error bar for the DIFFERENCE between two CRN arms.** B1 and L1
played the same 960 worlds, so the statistic is the paired one (`paired.py`, a McNemar table built
from each chunk's own PER-SLOT RECORDS block):

| rung | pairs | lever-only wins | baseline-only wins | p (2-sided) | Δ missions cleared |
|---|---|---|---|---|---|
| RECRUIT | 160 | 4 | 1 | 0.375 | +0.05 |
| heat 0 | 160 | **13** | 1 | **0.0018** | +0.37 |
| heat 2 | 160 | 5 | 0 | 0.0625 | +0.35 |
| heat 4 | 160 | 3 | 0 | 0.250 | +0.21 |
| heat 6 | 160 | 0 | 0 | 1.000 | +0.09 |
| heat 8 | 160 | 1 | 0 | 1.000 | +0.08 |
| **ALL** | **960** | **26** | **2** | **<0.0001** | **+0.19** |

### THE LADDER GAIN AND THE MISSION-1 CHANGE ARE ONE TRANSACTION, NOT TWO

**This is the review's F1 and it is the most important correction in this write-up.** An earlier
draft of this section claimed the ladder result here and booked the mission-1 change fifty lines
later as an unintended cost, as though they were separate ledger entries. They are the same entry:
mission 1 is always an `Eliminate`, so it is the largest population the lever touches, and **roughly
half the headline ladder gain is the opener.**

Stratifying the paired test on whether the **baseline** survived mission 1 is a legitimate CRN
conditional — the two arms play identical worlds and the lever is the only difference, so the
stratum is defined on a pre-lever fact:

| stratum | pairs | B1 | L1 | Δ | L-only : B-only | p |
|---|---|---|---|---|---|---|
| all pairs (the headline) | 960 | 33.44 | 35.94 | **+2.50** | 26 : 2 | 3×10⁻⁶ |
| **baseline SURVIVED m1** | 936 | 34.29 | 35.68 | **+1.39** | 15 : 2 | **0.0024** |
| baseline LOST m1 | 24 | 0.00 | 45.83 | +45.83 | 11 : 0 | 0.00098 |

**Eleven of the 24 extra run wins come from 2.5% of campaigns** — the ones the opener was killing.
Per rung, holding the opener fixed:

| rung | Δ all pairs | Δ opener held fixed | opener's share |
|---|---|---|---|
| RECRUIT | +1.88 | +1.88 | 0% |
| heat 0 | +7.50 | **+4.58** (p=0.039) | 39% |
| heat 2 | +3.12 | **+1.31** (p=0.50) | 58% |
| heat 4 | +1.88 | **+0.00** | **100%** |
| heat 6 | 0.00 | 0.00 | — |
| heat 8 | +0.62 | +0.64 | 0% |

So: **heat 2's move above its floor is majority opener, and heat 4's gain is entirely opener.**
The claim this wave is entitled to make is the +1.39 pooled, p=0.0024, concentrated at heat 0 and
RECRUIT — a real, significant mid-run effect, measured the right way. It is not "the ladder went up
7.5 points at heat 0 and, separately, the opener got easier".

### The post-lever cross-tab

| on mid-run nodes | B1 | L1 |
|---|---|---|
| KILL | 38.5 ±3.1 (n=247) | **45.3 ±3.1** (n=256) |
| NON-KILL | 81.5 ±1.1 (n=1243) | 81.8 ±1.1 (n=1280) |
| **gap** | **43.0 ±3.3** | **36.5 ±3.3** |

`byObjectiveByNodeKind`, the cells that moved: `Eliminate` Combat 39.6 → **53.3** (n≈106), Elite
30.3 → 32.4 (n≈33), Supply 73.2 → 82.8, Start 97.5 → 99.8.
`byObjectiveByMission`, `Eliminate`: m2 63.3 → 76.7, m3 40.9 → 54.5, m4 55.1 → 64.7, m5 23.8 → 29.5.

**The control held — but only one of the two I claimed is a control.**

**`Decapitate` is the real one** and it does its job: it carries the clock at `prs` 0.79 and takes
a wave on 20.4% of its mid-run missions, so the arm this lever removes is genuinely live on it, and
it still moves only 39.8 → 41.7, inside its own ±4.6. Its BOSS cell — n≈472/509, the largest cell
in the game — is flat at 68.0 → 67.8. That is a control with something to lose.

**`Hack` is not, and I should not have called it one.** `PressureGrace` is 4 turns and Hack's mean
mid-run mission is **3.67 turns**, so the clock essentially never engages on it: `prs` 0.16,
reinforced on **2.0%** of missions. "Identical to the last digit across the arms" is therefore very
nearly a tautology — the lever removes an arm Hack had all but never fired. Restated as what it
actually is: **evidence that the change is scoped to the objective it names, not evidence that the
clock's second arm is harmless where it fires.** Only Decapitate carries the latter.

The other five non-kill rows move by −0.2 to +0.6.

**An independent cross-check, from a channel that is not the Stats block.** `SpawnReinforcements`
echoes every wave to the console under AutoPlay. On the same 20 campaigns at `h0-b0`, the baseline
log carries **13** `REINFORCEMENTS:` lines and the lever log **5** — the residual five are Hack and
Decapitate, which keep the arm. DEFEND's own `WAVE:` schedule reads 82 → 85 across the two, which
is **not** the lever reaching Defend but more Defend missions being played at all because runs
survive longer; per mission `enc.py` holds it still at 6.48 → 6.47. Four raw logs are archived for
this (`docs/measurements/c3/{B1,L1}-h{0,4}-b0.log`).

**And the mechanism reads back exactly as designed**: `Eliminate`'s reinforcements go 1.69 → 0.00
and its reinforced share 41.0% → 0.0%, while its mean anti-turtle rung is 1.55 → **1.50** — the aim
arm is alive and the clock still bites. Turns barely move (8.53 → 8.63): the squad is not finishing
*faster*, it is finishing *at all*.

**One number in an earlier draft of this paragraph was read the wrong way round, and the review
caught it.** `Eliminate`'s mid-run `clear%` goes 48.8 → 59.5, and I wrote that as "more of the
force gets killed". It is not: **the numerator is flat (4.62 → 4.65 bodies killed per mission) and
the DENOMINATOR shrank** (7.78 + 1.69 = 9.47 → 7.82 + 0.00 = 7.82). The squad kills the same number
of hostiles; there are simply fewer of them to kill. That is the same denominator artifact that
makes `Defend` look like it declines its encounter, one row up — and it is a good reason to read
`clear%` and the raw `killed` column together, always.

**A finding in the lever's favour that this wave did not claim and the review supplied.** The worry
that removing the wave would make mid-run `Eliminate` a shorter, thinner fight is refuted by the
data: it got **longer** (8.53 → 8.63 turns) at a flat body count, and against the full mid-run field
it is the **second-longest objective, kills more bodies per mission than anything else in the
game** (4.65 vs Defend's 4.28 and everything else under 1.5), and is the **second most lethal to
the squad**. Whatever else is wrong with the kill class, "boring" is not it.

### The within-run curve

| mission | 1 | 2 | 3 | 4 | 5 | 6 |
|---|---|---|---|---|---|---|
| B1 | 97.5 | 82.7 | 77.1 | 77.7 | 73.5 | 68.0 |
| **L1** | **99.8** | **84.8** | **78.7** | **78.7** | **75.5** | **67.8** |
| n (L1) | 960 | 442 | 718 | 572 | 437 | 509 |

Still the monotone ramp §3.D asks for (the m3/m4 tie is inside its own error, as it was before).

### MISSION 1: I OVERSHOT, AND THE DESIGN DEFENCE I FIRST REACHED FOR IS NOT AVAILABLE

An earlier draft called this "nearly unlosable" and filed it under costs. Both halves were wrong.

**It was not "nearly" — at the difficulties people play, it was never lost.** Mission-1 losses per
160 campaigns, baseline → lever:

| | RECRUIT | heat 0 | heat 2 | heat 4 | heat 6 | heat 8 |
|---|---|---|---|---|---|---|
| B1 | 0 | 7 | 7 | 5 | 2 | 3 |
| **L1** | **0** | **0** | **0** | **0** | 1 | 1 |

**640 consecutive campaigns at RECRUIT / h0 / h2 / h4 with zero mission-1 losses.** And it is
structural rather than lucky: **22 of the 24 baseline mission-1 losses were the reinforcement
wave** (the two arms play identical worlds, so the 24 → 2 difference IS the wave). After X2's
`OpenerTrim` takes a body off the opening force and this wave takes the wave off it, the opener is
a **fixed force that cannot grow against a full squad** — its entire failure mode was one mechanic
and that mechanic is now absent from it.

**The §3.D / §3.G defence does not hold, and I am recording that rather than arguing it.**

- §3.D forbids front-loading anxiety — but **X2 already discharged that**, and 97.5% is not
  front-loaded anxiety by any reading. §3.D's *other* half ("too easy → boredom") is the live risk
  now, and **pillar 5, "stakes that bite", is on the far side of a 100.0%-over-640 opener.**
- §3.G's "scripted, low-stakes first mission" was a real justification while onboarding lived in
  mission 1. **RESONANCE T1 moved it into a dedicated `GameMode.Training` drill.** An unlosable
  *campaign* opener therefore buys nothing §3.G asks for and only spends pillar 5.
- And the control X2 itself cited: it justified `OpenerTrim` by pointing at RECRUIT's "100%, zero
  losses in 40" as the **easiest-difficulty control**. Every rung up to heat 4 now matches that
  control.

**The fix is the next wave's, and it is named, not vague.** `Mission.OpenerTrim` is an existing,
measured dial (shipped at 1; `SIGHTLINE_OPENERTRIM=0` is the pre-X2 opener). Backing some of it
out is one lever — which is why it is not in this round — and it **will move heat 2 and heat 4
down**, because the table above shows those rungs' gains are 58% and 100% opener. So it is a lever
*plus a re-measured ladder*, not a tweak. It is the top item in ROADMAP.

### The side effects, checked and recorded straight (`top.py`, 960 vs 960)

`choices/ARMED-soldier-turn` 2.305 → 2.290, `meaningful-choices/turn` 3.197 → 3.200,
`lead-swings/match` 0.726 → 0.731, `avg max-swing` 49.25 → 49.64, `turns-with-a-shot` 57.4% →
57.8%, policy-gap mean shot 0.542 → 0.542. **Nothing moved outside noise.** That matters because
X2's opener trim bought its win rate partly by making the opening fight less contested
(lead-swings 0.79 → 0.61); this lever did not.

## 5. THE INFORMATION CHANGE — the fork says which game it is

`Run.IsKillObjective` is the single source of truth; every surface asks it, so the game can never
tell the player one thing and score another. Two words, used identically everywhere:

- **PITCHED** — the field must be cleared; the fight IS the objective.
- **TASKED** — the objective ends it, *and the force can be left standing*.

The second clause is deliberate and it is the honest half. The flywheel's optimal policy already
declines those fights (a won `Evac` kills 3.3% of its force); hiding that only taxed the players
who had not worked it out. §3.A's complaint about a dominant line is that it exists, not that it
is known — and §3.B says telegraph.

Four surfaces, all reading the same predicate:

1. **The campaign-map node label** gains a drawn class mark in a 12px gutter left of the objective
   name (the name is unchanged — W9 cut this label to one line for overprint reasons and it stays
   one line).
2. **The hover tooltip** gains a class row directly under the node name, above force/payout/reward
   — the class is the headline property, so it goes first. `EVENT` nodes get no row: they are not
   fights and their `Card.Objective` is a placeholder that must not be read as one.
3. **A two-entry class key** on its own row under the node-kind legend. Deliberately not merged
   into that row: two taxonomies answering different questions ("what node is this" / "what ends
   this mission") must not share a key. FUL-12's rule binds — a mark the map draws and the key does
   not name is an unexplained glyph.
4. **The legacy deploy card** (shown only when the map is unavailable) gains the tag, so the two
   forks can never disagree.

**THE SHAPES WERE CHOSEN AGAINST `DrawNodeIcon`, AND THE FIRST PAIR WAS KILLED BY A SCREENSHOT.**
The first pass used a filled diamond (PITCHED) and a hollow ring (TASKED). Reading the shot:
`NodeKind.Boss` is *already* "a solid diamond inside a ring tick", and the boss node is **always**
Decapitate — i.e. always PITCHED — so on the one node where glyph and mark are guaranteed to
co-occur the mark would have read as a duplicate of the node icon. `NodeKind.Combat`'s crosshair
already carries a hollow circle, so the ring collided too. The shipped marks are **crossed blades**
and an **empty tile-square**: diagonal strokes and an orthogonal outline are the two silhouettes
the node vocabulary (chevron, cross, delta, fork, diamond, crosshair) does not use.

**Colour is redundant, and the two colours were picked on purpose.** `Pal.Foe` and `Pal.Good` are
the only two palette entries `SetColorblind` remaps, so the marks stay separable under
`SIGHTLINE_CB=1` — and shape carries them regardless (§3.H: never hue alone).

**Layout arithmetic, since the barracks card height is derived.** `mapChrome` goes 58 → 74 (a 19px
class-key row, bottom pad 14 → 11). The card is `contentH + mapChrome + mapH` with
`mapH = Clamp(800 − 24 − contentH − mapChrome, 150, 250)`; the clamp only binds above
`contentH = 552`, and the worst case is a `RosterMax = 6` roster + the 5-line report cap + a KIA
line = **546**, so `h = 776` for every reachable roster. A screenshot at the worst case confirms it.
`RosterMax` is a hard constant, so this is arithmetic, not hope — but the margin is **6 points of
contentH**, down from 22, and a seventh roster row would break it (it would have broken the pre-C3
card too, at 798 of 800).

### Screenshots read and judged

All at 1280×800 under Xvfb/llvmpipe:

- **the fork, default staging** (`SIGHTLINE_CAMPAIGN=1 SIGHTLINE_SHOT=90`) — the class key reads as
  its own row; the reachable node's square sits cleanly left of `RESCUE`. First pass with the
  diamond/ring pair was rejected here (see above) and the row gap went 16 → 19px because the two
  keys read as one four-item row.
- **the TASKED tooltip** (`+ SIGHTLINE_MAPHOVER=1`) — "STANDARD − EXTRACT" / green square "TASKED −
  it ends when the task is done" / "Standard force +26 intel" / the enemy hint. Hierarchy correct:
  class above economy.
- **the PITCHED tooltip** (`+ SIGHTLINE_MAPCOL=5`, which stages the boss column — the only node
  kind guaranteed to be a kill objective) — "BOSS − DECAPITATE" / red X "PITCHED − it ends when the
  field is clear" / "+34 intel" / "BOSS: WARLORD".
- **a colourblind pass** of that same frame (`SIGHTLINE_CB=1`) — orange X / teal square, both still
  separable, and the silhouettes carry it independently of hue.
- **the worst-case card** (`SIGHTLINE_ROSTER=6 SIGHTLINE_REPORT=5`) — six soldier rows, five report
  lines, both legend rows, nothing clipped.

Two new shot hooks: `SIGHTLINE_MAPHOVER=<k>` parks the cursor on the k-th reachable node (it reads
`Hud.NodeBtns`, which the map publishes as it draws, so it necessarily lags a frame); and
`SIGHTLINE_MAPCOL=<n>` moves `DebugCampaignMap`'s staged column off its hardcoded 3.

## 6. THE GATE — `SIGHTLINE_CLASSTEST`, and it FAILS on the pre-C3 tree

Four legs; wired into `scripts/qa-sweep.sh` (derived counts balance at 65 in `src/` and 65 run).

- **(A) MODEL** — over the whole `Objective` enum, `IsKillObjective` is true for exactly
  `{Eliminate, Decapitate}`, by enum value AND by telemetry name; an unparseable name is not a kill
  objective; and the member count is pinned at 8 so appending a ninth objective cannot silently
  inherit "not a kill objective".
- **(B) DRAW — the fork** — paints the REAL barracks frame and reads strings at the draw call
  (`Cfg.CaptureText`) and class marks at the draw site (`Hud.CaptureClassMarks`, null in normal
  play). Both classes are staged deterministically by overwriting the reachable nodes' cards, so
  the two passes differ only in the class. Asserts the key names both marks, that the mark census
  is `choices + 1` of the staged class and exactly 1 of the other, that the objective label is
  still painted beside the mark, and the 12px floor.
- **(C) DRAW — the tooltip** — parks the real cursor on a real `NodeBtns` rect and redraws, so the
  tooltip is reached through the shipped hover predicate. Asserts it paints THIS node's class line
  and NOT the other one, and that it still carries the intel row it carried before.
- **(D) LEVER** — runs the real `UpdatePressure` at turn 7 (rung 2, the first wave rung) and counts
  bodies: `Eliminate` adds none while `Combat.PressureAim` still rises and the rung still reaches
  2; `Hack` and `Decapitate` add bodies and their aim/rung are identical to Eliminate's; `Evac` has
  no clock at all; and `ClockWavesOnEliminate = true` restores the waves.

**Proof it can fail** (`prefix_revert.py apply` — reverts the four behaviours and leaves the test
wired, then `git checkout -- src/`):

```
CLASSTEST: FAIL pitched:noKeyPitched, pitched:noKeyTasked, pitched:marks=0 want2,
pitched:otherMarks=0, pitched:tooltipMissingClass, tasked:noKeyPitched, tasked:noKeyTasked,
tasked:marks=0 want2, tasked:otherMarks=0, tasked:tooltipMissingClass, elimAdded=2
```

Eleven failures across every leg the wave added, including the lever (`elimAdded=2` — the pre-C3
clock put two bodies into an `Eliminate`).

**The test caught FOUR of its own bugs before it caught anything else.** Two are the recorded
CROSSCUT rules, and two are new and worth adding to them:

- *Rule 6, an assertion in the wrong scope.* Leg D read `Pressure` after the LAST of five staged
  clocks (Evac's 0) instead of Eliminate's, so `elimRung` failed for a reason unrelated to the
  lever. Each arm now carries its own rung out.
- *A gate that fails for the wrong reason is noise.* Leg B's first version swept the 12px floor
  over the whole frame and failed on `WHITE FORD` at 11px — the campaign map's region-name strip,
  which is `FitSize(11, 8)`. Scoped to the strings this wave paints; the pre-existing breach is
  recorded below and NOT fixed.
- **A DRAW test must not leave state on the screen for the next one.** The hover step of the
  PITCHED pass left the cursor parked on a node; the TASKED pass's first draw then sometimes drew
  a tooltip — which paints a class mark of its own — and the mark census came back one too high.
  Whether it did depended on the seed-dealt map layout, so it failed intermittently. The run is now
  `Util.Reseed`ed, the cursor is parked off the map before each pass, and a new leg asserts that an
  un-hovered draw paints **no** tooltip, which is what makes the census a statement about the
  labels and the key.
- **A DRAW test must not assume a rect it read from a PREVIOUS frame is still there.** `DrawBarracks`
  gives the card a 0.15 s slide-down entrance (`PanelAnim("barracks")`), so the whole map — and
  every rect `NodeBtns` publishes — moves by up to 16 px between two consecutive draws taken inside
  that window. Parking the cursor on a rect read from the previous draw therefore missed the node
  about half the time. **It missed 3 runs in 4 under `dotnet run -c Debug`** (where the first draw
  is slow enough to land mid-entrance) **while passing 8 of 8 on the Release binary** — so "it
  passes" was a statement about which binary you ran it on. The hover now re-reads the rect,
  re-parks and re-draws until the tooltip appears, exiting on the *intel* row that predates this
  wave so the loop can never be satisfied by the thing under test. Now 6/6 under the sweep's exact
  invocation and 8/8 on the Release binary (`classtest_x8.sh`).

## 6a. A DEFECT IN THE PRE-MERGE GATE ITSELF — `qa-sweep.sh` had no exit statement

This was found the only way it was ever going to be: **this wave's own new self-test failed inside
a `--full` sweep and the sweep still reported `SWEEP-EXIT=0`.**

`scripts/qa-sweep.sh` has a long header, written at the W9 merge, explaining that a FAIL line, a
non-empty COVERAGE GAP or a TIMEOUT must make the sweep exit non-zero *"so it is a gate rather than
a report for a reader to notice"*, and CLAUDE.md repeats the claim in its own words. Every path in
the script faithfully accumulates `_fail` / `_autofail` — **and the script then ends on an `echo`,
so its exit status was that echo's. Zero. Always.** `grep -n 'exit ' scripts/qa-sweep.sh` returned
three comment lines and no statement.

So **every "qa-sweep --full green, SWEEP-EXIT=0" claim made in this repository before this wave was
reporting the exit code of an echo.** The PASS/FAIL lines printed above it were real and a human
reading them would have caught a failure; the machine-checkable gate the lead relies on at merge
was not there. Fixed: `_fail` and `_autofail` now decide a real `exit`, with a `!! SWEEP FAILED`
line naming it. Verified in both directions — a tree with a failing CLASSTEST exits 1 and prints
the line; the shipped tree exits 0.

While wiring it, one more hole: **`AIIDLETEST` was the only self-test line not routed through
`verdict`**, so a FAIL there printed and was never recorded. Harmless while nothing consumed
`_fail`; load-bearing now. Routed.

## 7. VERIFICATION

- `dotnet build -c Release` — **0 warnings / 0 errors**.
- `bash scripts/qa-sweep.sh --full` — **SWEEP-EXIT=0** (and for the first time in this repo that
  statement means something; see §6a), 65 of 65 self-tests PASS (CLASSTEST among them), COVERAGE
  GUARD block empty, PAIRTEST byte-identical, autoplay ×3 `LOSE m3 / WIN m6 / WIN m6` — no TIMEOUT,
  no blank. Verified in the other direction too: a tree with the wave reverted exits **1** and
  prints `!! SWEEP FAILED`.
- `SIGHTLINE_CLASSTEST` run **8× on the Release binary and 6× under the sweep's own
  `dotnet run -c Debug`** — 14 PASS, 0 FAIL. Both were needed: the flake §6 describes only ever
  showed up on the Debug path.
- **A flake in someone else's test, recorded rather than hidden.** One `--full` sweep out of four
  printed `CONTRASTTEST: FAIL`. Run standalone it PASSES (`glyph-vs-plate >= 4.5:1 on all 9
  labels`, 6.51–13.50) and the other three sweeps passed it. CONTRASTTEST reads real pixels through
  the post-FX shader under llvmpipe on a shared four-core box; that is the most plausible cause and
  this wave did not touch the main menu, `Pal`, or `Display`. **Flagged, not explained** — and now
  that the sweep has a real exit code, a 1-in-4 pixel-test flake is a merge-blocker rather than a
  line someone skims past, so it is worth someone's wave.
- 96 measurement chunks, every one `OK ... runs=20`; zero `BAD`.

## 8. WHAT I DID NOT DO, AND WHAT IT COST

- **I OVERSHOT ON MISSION 1, and the first draft of this write-up disguised it by splitting one
  transaction into two.** The full accounting is in §4 (the stratified paired table) and §4's
  mission-1 subsection; the short version is that ~half the headline ladder gain is the opener, the
  opener is now unlosable at every rung up to heat 4 (0 losses in 640 campaigns), and the design
  defence I first reached for (§3.G's low-stakes first mission) stopped being available when
  RESONANCE T1 moved onboarding into its own drill mode. **The claim this wave is entitled to is
  the opener-held-fixed +1.39 (p=0.0024), not the +2.50 headline.** Backing the opener out is the
  next wave's top item and it needs a re-measured ladder, because heat 2 and heat 4 will move down.
- **The gap is 36.5 points, not zero, and this wave does not claim to have closed it.** It moved
  43.0 → 36.5 — **6.5 points of a 43-point structural difference**, or 15%. The lever addresses
  ELIMINATE's half of the kill class and nothing else.
- **DECAPITATE's half is untouched, on purpose.** Its mid-run row is 41.3 / 42.5 (Combat / Elite)
  and its mechanism was already located by W8 and L3 — `Mission.Build` de-stacks the FINALE by 3–4
  bodies and resets `bump`, and no mid-run Decapitate gets that. That lever is still unspent and it
  is still the right next one. I kept off it deliberately so this round would have a within-class
  control; using it here would have left nothing to check the lever against.
- **I did not price the reward.** The brief's own framing is that a 43-point spread "might be
  correct if the game tells the player which one they are choosing **and prices the reward
  accordingly**". This wave did the first half. `MissionNode.Intel` is depth-scaled and
  kind-scaled and is blind to the objective class, so a PITCHED node pays a TASKED node's rate for
  a much harder fight. That is a small change and a full round to price, and it is the obvious
  next-wave lever. **Unmeasured, not shipped.**
- **The bot's declining policy is not separated from the game's.** `clear%` is a joint property of
  the design and of `Game.SmartStep`. A `SIGHTLINE_*POLICY`-style dial that forces the bot to
  engage on non-kill objectives (the shape W8 used for `SIGHTLINE_HVTPOLICY`) would split them.
  Not built.
- **A 2.1% harness-forced-loss floor that nobody has ever mentioned.** `instrumentHealth.
  stalemateLosses` reads **20 of 960 in BOTH arms and 20 of 960 in the L3 archive** — identical,
  because it is a property of the worlds and not of any lever. Every ladder figure this project has
  published sits on top of it. Not this wave's to fix; nobody owns it, and it should be somebody's.
- **The anti-turtle clock is now weaker on ELIMINATE, and turtling is not merely unmeasured — it
  is UNMEASURABLE with this harness.** That is a stronger and more uncomfortable statement than the
  draft's "I did not measure it", and it is the correct one. The flywheel has exactly two policies,
  `greedy` and `sloppy`, and `sloppy` is an **error** model, not a **passivity** model: neither
  camps, and `SmartStep` hunkers only as a terminal fallback. **No policy in the instrument could
  reveal a turtle exploit**, so no number in this write-up — or in any previous one — bears on it.
  Three things make that worse rather than better here: the lever touches only the **41% slowest**
  Eliminates (the other 59% were never reinforced at all), i.e. **precisely the missions where a
  player was already taking their time**; what remains as a disincentive is the aim arm alone,
  whose measured mean high-water rung is **1.50**, below the rung 2 at which the wave arm fired;
  and the design argument in §4 is an argument (*a slow Eliminate already punishes itself, because
  the force stays on the board*), not a measurement. `SIGHTLINE_KILLTREADMILL=1` is the switch back.
  **A camping policy in the flywheel is a prerequisite for anyone pricing this, and it is now its
  own ROADMAP item rather than a line in mine.**
- **A pre-existing 12px-floor breach, found and left.** `Hud.DrawCampaignMap`'s region-name strip is
  `FitSize(rn, 11, 8, ...)` and paints at 8–11px; CLAUDE.md's floor is 12. It is not alone —
  the WAR ROOM footer's stat labels are `FitSize(11, 8)` and the requisition slate's effect line
  and reroll label are `FitSize(12, 9)` — so the "floor" is in practice a guideline that FITTEST
  enforces where it looks.
  Recorded here as the breach it is; out of this wave's charter to move, because raising those
  strips to 12px risks overflowing the columns they were fitted to.
- **`heat 6` moved by exactly nothing at the RUN level — and my first explanation for it was
  wrong.** I wrote "at h6 the runs that reach a mid-run Eliminate are already lost". The archive
  refutes that outright: **h6 has the LARGEST mission-level gain of any rung — mid-run `Eliminate`
  19.0 → 36.4, +17.3 (n=21/22).** The missions are won; they just do not convert. The real reason is
  small numbers: **only 6 of 160 h6 campaigns differ between the arms at all**, none of the six
  crossed the win line, and at h6's ~20% base rate zero conversions out of six touched campaigns is
  the *expected* outcome. The discordance sequence across the rungs is 5 / 14 / 5 / 3 / **0** / 1,
  and P(0 | Poisson mean 2.0) = 0.135. **Unremarkable, not mysterious** — and it is a good example
  of why a run-completion ladder is a blunt instrument for a per-mission lever.
- **I did not put mission 1's stakes back.** The opener went 97.5% → 99.8% (24 losses in 960 → 2)
  because it is always an `Eliminate` and therefore the largest population the lever touches. See
  the within-run curve above. One lever per round is the rule that kept this measurable, so the
  fix — if it is one — belongs to the next wave and belongs on `Mission.OpenerTrim`.
- **The `EVENT`-node tooltip has no class row and no mark.** Correct (an event is not a fight), but
  it means the map's marked/unmarked distinction now carries two meanings: "TASKED" and "not a
  fight at all". The legend does not say so.
- **No onboarding for the two words.** PITCHED/TASKED appear on the map, the tooltip, the key and
  the deploy card, but the FIELD MANUAL (`Codex.cs`) does not define them and T1's just-in-time
  tips do not introduce them. The key row is the only teaching surface.

# PROGRAM CONTOUR — WAVE C4 "EIGHT BIOMES ARE PAINT" (2026-08-30, dev on wave/biome-mechanical)

**Branch** `wave/biome-mechanical` off `main` at **`17934ee`** (CROSSCUT composed — the tree the
L3 ladder of record was measured on). Files touched: `src/Terrain.cs` (new), `Grid.cs`,
`Combat.cs`, `Ai.cs`, `Game.cs`, `Game.Autopilot.cs`, `Game.Harness.cs`, `Renderer.cs`, `Hud.cs`,
`Codex.cs`, `Stats.cs`, `Program.cs`, `scripts/qa-sweep.sh`.

## The finding

`grep -ci biome` returned **0** in `Combat.cs`, `Ai.cs`, `Grid.cs` and `Unit.cs`. The game had
eight biomes and **not one of them changed how the fight worked.** Wave V3 "SURFACES" and HORIZON
W6 had given them a real visual identity — a fissure that snakes across six tiles, a frost drift,
a moss patch — and every one of those motifs was a lie: the fissure went nowhere, the drift did
nothing, the patch was a colour. This wave is the other half.

## What shipped — THREE biomes, THREE axes, five still paint

| biome | mechanic | the one sentence the player gets |
|---|---|---|
| **VERDANT** | **UNDERGROWTH** (cover axis) | *"The ferns give LOW COVER from every angle — but only against fire from more than 2 tiles away. Close in to strip it."* |
| **TUNDRA** | **SLICK ICE** (movement axis) | *"Crossing a frost drift costs HALF a step, so the drift is a fast lane — for both sides."* |
| **MAGMA** | **THERMAL VENTS** (sight axis) | *"No one can see across a steaming fissure, forcing a crossing costs movement, and touching one sets you alight."* |

STEEL / ARID / ASH / VOID / NEON are **still paint**. That is a decision, not an omission: three
mechanics the player can name beat eight they cannot tell apart, and the five are the round's
built-in control (see the cross-tab below). `Terrain.Tag()` returns null for them and BIOMETEST
**asserts** they stamp nothing, so a future wave that gives one of them a mechanic has to change
that line and say so.

## The mechanism: symmetry is STRUCTURAL, not promised

Every rule lives in one of the three functions **both sides already ask for the truth**:

| mechanic | seam | consequence |
|---|---|---|
| undergrowth | `Grid.GetCover` | a cover LEVEL, so high ground / a DRONE / a SYNDICATE optic see over it, the exposed-crit bonus and the LOCK-ON flank perk price it, `Ai`'s `cover.Level * 18` scores it and the HUD's pip draws it — with no second implementation to drift |
| slick ice | `Grid.CostMap` | the one movement model: `Ai.Plan`'s reachable set, the player's move overlay, the path preview and the VIP leash all widen together |
| thermal vents | `Grid.HasLineOfSight` (via a new `IsVapor`, joining smoke rather than high cover) + `CostMap` + `OnUnitEnteredTile` | every sight read on both sides routes around it; the crossing toll is in half-tiles and the burn is the existing 3.6 fire sear |

Nothing in `src/Terrain.cs` reads `a.Team`. The layer is stamped once per mission through
`Util.Hash3` — **zero `Util.Rng` draws, no `System.Random`** — so CRN pairing is untouched.
(The stronger claim this paragraph originally made, "from `(MapSeed, mission)`", is **false** and
is corrected in the addendum: `Terrain.Stamp` is pure, but the BOARD also keys on the reserved set,
which `Mission.Build` drew from `Util.Rng`.) It is **not persisted** (`SaveGame` never serialises a `Grid`; verified
`Tiles`/`Height`/`Smoke`/`Fire`/`Barrel` appear nowhere in it), so `GroundKind` is **not** a
fourteenth persisted-by-ordinal enum and SAVETEST's hashes are unchanged.

### What Ai.cs actually needed

The honest answer is **less than expected, and that is the design working.** Because the three
rules live in the three truth functions, the planner re-prices itself: `OddsFrom(g, e, tx, ty, p)`
already computes odds *from the candidate tile*, so a hostile facing a soldier in the undergrowth
automatically prefers closing inside 2 tiles (where the ferns are worth nothing); `reach` is the
shared cost map, so ice widens the enemy's options with no code; `HasLineOfSight` is the same
function, so vents cut the planner's shots, its overwatch and its focus cones.

What the AI genuinely could **not** derive is a preference about *ending* a move on hot ground.
Three explicit terms were added:

* `Ai.cs` — `if (g.Grid.IsVent(tx, ty)) score -= 34;` beside the existing `IsFire → −60`.
  **Deliberately smaller than fire's and smaller than the `100 + bestHit` a shot is worth**: a
  vent is a price, not a wall, so a hostile that can only reach a killing angle by standing on the
  crack takes it and eats the burn. That is the same bet the player is offered.
* `Game.Autopilot.cs` — `TileExposure += 12` on a vent, on the same scale as its `IsFire → 20`,
  so the balance bot does not walk the squad into the fissure and hand the wave a win-rate drop
  that is a *bot* defect rather than a design consequence.
* `Game.cs` VIP leash — the escorted asset is neither parked on a vent nor routed through one when
  a cooler lane exists (mirroring the existing fire-route term).

## Legibility — five surfaces, because a rule you cannot see is invisible unfairness

1. **The board.** `Renderer.DrawGround` gives each mechanic a material that carries on **VALUE**
   (so it survives greyscale and `SIGHTLINE_CB`) **plus a rim** (so a patch reads as a *region
   with a boundary*, not as scattered decoration): a dark fern mat with light blades, a pale drift
   plate with a bright fracture and a travelling specular, a dark crevasse with a hot seam.
   `DrawVentSteam` draws the vent's steam **above the figures beside `DrawSmoke`**, in the same
   soft-grey visual family — because what it is telling you is exactly what smoke tells you.
2. **The banner** — `MISSION 3 - VERDANT - UNDERGROWTH`.
3. **The briefing card** — the one-sentence rule, appended in `Game.BeginBriefing` (NOT in
   `Voice.Brief`, which is under a zero-`Util.Rng` contract and whose VOICETEST pre-measures its
   own three lines; the card auto-sizes to its row count).
4. **The shot tooltip** — a soldier in the ferns reads **`UNDERGROWTH  -20 aim past 2`**, not
   `LOW COVER`. "LOW COVER" on a tile with no block beside it reads as a bug.
5. **The hover threat card + the CODEX** (FIELD CRAFT tab, beside COVER and FLANKING — they are
   cover/movement/sight rules and that is the tab that teaches cover, movement and sight).

### THE ART FOLLOWS THE MECHANIC — the capture that forced it

The first MAGMA screenshot is the most useful artifact this wave produced. V3's **decorative**
board-scale fissure and per-tile veins were still drawn, in the same orange, on the same board, as
the new **real** vents. The result was a field of orange squiggles in which **no player could have
told which line burns them.** That is worse than no mechanic at all.

The rule that came out of it, and it is general: **when a biome's mechanic is live, the biome's
DECORATIVE version of that same motif is retired.** `DrawBiomeFeatures` returns early on the three
mechanical biomes; the MAGMA per-tile vein is suppressed entirely when the board carries vents;
and the ambient per-tile signature skips any tile that has mechanical ground. Orange on a MAGMA
board now means exactly one thing. The other five biomes keep their V3 features unchanged.

Two more capture-driven repairs, both caught by looking rather than by a test:
* **The fern had no edge.** First capture: blades, no rim — at a squint that photographs as
  texture, not as a place you can decide to stand in. Added the boundary rim.
* **The fissure was ten scattered singles.** The walker wandered at 0.9 rad/step and cover tiles
  ate a third of its steps. Dropped to 0.55 and forced two cracks per board; it now reads as a
  line with **fords** — and the fords are load-bearing, not cosmetic: a *continuous* 1-wide crack
  is a total sight barrier (Bresenham supercover always passes through a 1-wide wall), which would
  cut the board in half for shooting and stall the fight.

Judged captures: VERDANT (three dark fern regions with bright rims, cover keeps the top of the
value hierarchy), TUNDRA (narrow outlined lanes after the drift budget was cut 42 -> 34 — the
first capture was a *lake*, not a lane), MAGMA (a broken vertical crack down the right third),
and MAGMA under `SIGHTLINE_CB=1` — where "**more** legible than in colour" was **wrong** and is
corrected in the addendum: value legibility is identical (+145.5/−28.9 vs +141.0/−28.9); what CB
actually buys the vent is HUE SEPARATION from MAGMA's own warm palette.

## THE MEASUREMENT — declared, including the part that is bad

Full round in `docs/measurements/c4/` with the exact command lines. **Base `17934ee`. One lever
(`SIGHTLINE_BIOMEMECH`), same binary, same snapshot, same slots. 3 rungs x 2 arms x 8 disjoint CRN
slot sets x greedy+sloppy = 160 campaigns per (rung, arm), 960 per round, twice (uninstrumented +
instrumented). All 96 chunks printed `OK ... runs=20`; zero `BAD`.**

| rung | A (mechanic ON) | B (pre-C4 board) | delta | ±SE(delta) | L3 of record | floor | verdict |
|---|---|---|---|---|---|---|---|
| heat 0 | **43.8%** | 47.5% | −3.7 | 5.6 | 47.5 | 47 | ~~3.2 BELOW the floor~~ **⚠ see addendum: chunk-paired t = −1.07, NOT a measured breach** |
| heat 2 | **30.0%** | 31.2% | −1.3 | 5.2 | 31.2 | 32 | t = −0.19 |
| heat 4 | **25.6%** | 23.8% | +1.9 | 4.8 | 23.8 | 22 | t = +0.51 |

**Arm B reproduces the L3 ladder to the decimal on all three rungs.** With the flag off this
branch is the same game as `17934ee`, so every point of difference in column A is the ground layer
and nothing else. **None of the three deltas reaches its own standard error** and the ladder stays
monotone — but heat 0 was sitting *exactly* on its band floor beforehand, so the point estimate
puts it under. **The wave did not stay in band at heat 0.** It also cannot claim the move is
noise: the round could not resolve it at n=160, which is not the same statement.

### The cross-tab, and the finding worth keeping

A pooled rung now mixes two populations that are no longer the same game. `Stats` gained a
read-only `byBiome` block (proven inert: **1304 and 1256 fields diffed on the R0diag pair, zero
differing**, with only `harness{}` and the new block excluded — the same exclusion W1 makes).
Pooled across all three rungs, **mission** win rate:

| population | A n | A win | B n | B win | delta | ±SE(delta) |
|---|---|---|---|---|---|---|
| MAGMA * | 219 | 80.4% | 220 | 84.5% | −4.2 | 3.6 |
| TUNDRA * | 218 | 81.2% | 223 | 84.8% | −3.6 | 3.6 |
| VERDANT * | 212 | 83.5% | 226 | 85.4% | −1.9 | 3.5 |
| **MECHANICAL (3)** | 649 | **81.7%** | 669 | **84.9%** | **−3.2** | **2.1** |
| **PAINT (5, control)** | 1070 | 81.1% | 1124 | 80.9% | **+0.3** | 1.7 |

> ~~**A SYMMETRIC RULE IS NOT A NEUTRAL RULE.** All three mechanics point the same way and cost
> the player 2-4 points of mission win rate on the boards that carry them, while the five paint
> biomes read **+0.3 ± 1.7** — flat.~~
> **⚠ SUPERSEDED — see the C4 ADDENDUM below.** Chunk-clustered the DiD is −3.19 ± 1.98,
> **t = −1.61** (sign test 17/24): a DIRECTION, not a result. "All three point the same way" is a
> pooling artifact — per rung only MAGMA is consistently negative; TUNDRA flips at h2 and VERDANT
> at h4. The correct sentence is: *the three mechanical boards moved −3.2 ± 2.1, direction
> consistent, MAGMA the only biome consistent across rungs, not resolved at n=160.*

*Hypothesised mechanism — **REFUTED in this same archive**, see the addendum:* each rule makes the
exchange harder to resolve, and a longer exchange favours the side with more bodies. Per-biome
`avgTurns` says otherwise (MAGMA 6.36 / TUNDRA 6.18 / VERDANT 5.58 against paint 5.36-6.47 — the
longest-fight biome in the batch has no mechanic). *Caveat on the control:* the paint rows are not clean, because a run mauled on a
MAGMA mission arrives at the next (paint) mission weaker. They read flat anyway.

**Decision density did not move** at heat 0 (`meaningful-choices/turn` 3.581 -> 3.502,
`ch/ARMED` 2.314 -> 2.269 — TRUE BAND instrument, not comparable to anything before 2026-08-29).
W4's law predicted this: the wave shipped a positioning lever *and* a threat lever at once, and
`CountMeaningfulChoices`' two halves respond with opposite signs.

## The test — and it fails on the pre-feature tree

`SIGHTLINE_BIOMETEST` (`Game.BiomeSelfTest`, wired into `scripts/qa-sweep.sh`). It pins the
**mechanic's effect** on constructed boards — a cover level, a hit%, a Dijkstra cost, a
line-of-sight verdict, an HP total, an AI destination — never the presence of a field. Six blocks:
the stamper (3 mechanical biomes stamp their own kind, **5 paint biomes stamp nothing**,
determinism per `(seed, mission)`, reserved tiles untouched, and a **density sweep over 24 seeds x
3 missions per biome** with per-biome floor/ceiling); undergrowth; ice; vents; the AI; the
off switch.

Two of its assertions found real defects during development, which is the point of writing a test
that measures rather than one that checks:
* the **sparsity floor** caught a drift that laid down **two** tiles and a fissure that laid down
  **one** — the walkers were breaking out of their loop at the board edge instead of reflecting.
  A mechanic that silently vanishes on some seeds is worse than one that is merely small.
* the **flood ceiling** caught VERDANT covering 73 of 198 tiles on its first tuning, a board where
  cover has stopped meaning anything. Hence the hard per-biome tile budget.

The AI leg is the one worth describing, because there is only one honest way to test "the enemy
understands the new board": run the SAME scene twice, record the tile `Ai.Plan` chose, make **that
exact tile** a vent, re-plan, and assert it abandons it.

**Proof it can fail** — the same binary with the flag off (`SIGHTLINE_BIOMEMECH=0`, which is the
pre-C4 board exactly):

```
BIOMETEST: FAIL (noGround[TUNDRA],noTag[2],noGround[VERDANT],noTag[3],noGround[MAGMA],noTag[7],
groundTooSparse[TUNDRA]=0,groundTooSparse[VERDANT]=0,groundTooSparse[MAGMA]=0,stampIgnoresMission,
foliageNoCoverAtRange,foliageDefense=0,foliageDiagonal,foliageFromWest,oddsMissedFoliage,
highGroundBlindToFoliage,foliageAimDelta=0,iceStep=2,iceReachCost=12,iceLaneNoExtraReach,
ventDidNotBlockSight,commandingSawThroughVent,ventNotVapor,ventStep=2,ventNoSear=0,
ventDidNotIgnite,ventParkedNotReignited,aiParkedOnVent,iceGaveTheAiNothing)
```

29 distinct assertions across all three biomes and both AI legs.

## Gates

* `dotnet build -c Release` — **0 warn / 0 err**.
* `bash scripts/qa-sweep.sh --full` — **SWEEP-EXIT=0**, 65/65 self-tests PASS (was 64; +BIOMETEST),
  COVERAGE GAP empty, autoplay x3 clean.
* `SIGHTLINE_PAIRTEST=1` — **PASS**, both legs byte-identical (`h0 slot0 WIN/WIN 28 turns`,
  `h4 slot1 LOSE/LOSE 8 turns`). The layer takes zero `Util.Rng` draws, as designed.
* Autoplay x3 — LOSE m6 / LOSE m3 / WIN m6. No TIMEOUT, no exception.

## What I did NOT do — read this before quoting anything above

1. **Five of the eight biomes are still paint.** STEEL, ARID, ASH, VOID and NEON change nothing.
   Declared, asserted by BIOMETEST, and deliberate.
2. ~~**The heat-0 rung is 3.2 points below its band floor**~~ — **⚠ corrected in the addendum**:
   that is a point estimate crossing a threshold (chunk-paired **t = −1.07**), not a measured
   breach. What IS resolved is that the cost sits on **mission 1** (`Start` t = −4.53). The wave
   did not repair either. Levers, all **unpriced**: thinning the layer on m1 (the targeted one the
   wave never found), the per-biome tile budgets, `Terrain.FoliageMinDist`, `Terrain.VentStepExtra`.
3. **The mechanism behind the −3.2 mission-level cost is a hypothesis, not a measurement.** "A
   longer exchange favours the side with more bodies" was not tested.
4. **The autopilot's vent weight (12) is below fire's (20) even though a vent is PERMANENT and
   fire gutters out in 3 turns.** That asymmetry is an unpriced defect in the *instrument*. I did
   not change it after the round and then publish the better of two arms; at n=160 the round could
   not have resolved 12 from 20 anyway.
5. **No per-biome ladder.** `byBiome` splits MISSION win rate, not campaign win rate; a
   campaign-level split would need a biome-forced batch mode that does not exist.
6. **RECRUIT, heat 6 and heat 8 were not measured.** Three rungs at n=160/arm was the budget.
7. **The mechanics are not taught by the tutorial/drill.** The drill is pinned to STEEL, so a new
   player meets a mechanic for the first time on a live mission with only the briefing card, the
   banner and the codex. W5's onboarding work is the right home for that and this wave did not
   touch it.
8. ~~**The ambient particle layer was not re-cut.**~~ — **⚠ the wrong layer.** The reviewer
   measured it: the ambient particles are round motes and are harmless. The real collision was
   TUNDRA's per-tile Snow SIGNATURE — paired pale diagonal hairlines, the drift's own fracture
   motif at ~half the alpha, on ~55% of floor tiles — which this wave's skip only suppressed *on*
   mechanical tiles. Fixed in the addendum (M3), board-wide, like the MAGMA vein.
9. **One new wall-clock read in `Renderer.cs`** (shared by `DrawGround` and `DrawVentSteam`), so
   CLAUDE.md's count of 45 is now 46. Screenshots were never byte-stable; `PAIRTEST` is the
   determinism gate and it is green.
10. **`GroundKind` is not persisted and that is load-bearing.** If a future wave ever serialises a
    Grid mid-mission, this enum joins the append-only set and needs a SAVETEST fingerprint.

## C4 ADDENDUM — TWO INDEPENDENT REVIEWS, EIGHT + FOUR FIXES (2026-08-30)

Both reviewers passed the engineering (0/0, SWEEP-EXIT=0, PAIRTEST byte-identical, no save-format
break, no unwinnable board in 800 stamped missions, no stall in 17 forced-biome autoplays, no
hot-path cost) and both sent the wave back on **claims and coverage**. Everything below is either a
correction to something C4 asserted, or a defect its own tests could not see.

### THE BIG ONE — the cost is on MISSION 1, and C4 never asked where

`byNodeKind` has been in every chunk of this archive since W1 and C4 never published it. `Start` is
mission 1 — exactly ONE per campaign, so it carries no within-campaign clustering, which is why it
resolves when the pooled rung cannot. Chunk-paired over all 24 chunks (reproduced from C4's own
archive with the committed `aggregate_nodekind.py`):

| node kind | A | B | paired Δ | SE | **t** | n/arm |
|---|---|---|---|---|---|---|
| **Start** (m1) | **91.7%** | **96.0%** | **−4.37** | **0.97** | **−4.53** | 480 |
| Combat | 78.2% | 77.5% | +1.13 | 1.89 | +0.60 | ~490 |
| Elite | 72.3% | 77.0% | −3.98 | 3.43 | −1.16 | ~240 |
| Supply | 89.2% | 89.0% | −0.27 | 1.55 | −0.17 | ~280 |
| Boss | 66.5% | 64.3% | +4.10 | 4.31 | +0.95 | ~245 |

h0 alone: `Start` −4.37, SE 1.75, **t = −2.50**. `byObjective` agrees — **Eliminate −3.55 ± 1.12,
t = −3.16**, and Eliminate is m1's objective in the baseline rotation.

> **The ground layer's cost is concentrated on the opening mission.** That is the front-loaded
> anxiety `DESIGN.md` §3.D forbids and the exact failure X2's `Mission.OpenerTrim` exists to
> prevent. C4 declared "h0 is under the floor" and never asked WHERE. It also points at a cheaper,
> more targeted lever than any of the three the wave named: **suppress or thin the layer on
> mission 1.** Unpriced; ROADMAP.

### THE CLAIMS C4 GOT WRONG, corrected at every site

1. **"A SYMMETRIC RULE IS NOT A NEUTRAL RULE" was bold in DEVLOG, ROADMAP *and* CLAUDE.md, and it
   is a direction, not a result.** Chunk-clustered DiD **−3.19 ± 1.98, t = −1.61**, sign test
   17/24. And "all three point the same way" is a POOLING ARTIFACT: per rung only MAGMA is
   consistently negative; TUNDRA flips at h2 (+1.9), VERDANT at h4 (+5.7), and individually both
   are indistinguishable from zero. Restated everywhere as: *the three mechanical boards moved
   −3.2 ± 2.1, direction consistent, MAGMA the only biome consistent across rungs, not resolved at
   n=160.*
2. **"3.2 BELOW the floor" was a bolded verdict on a point estimate.** Chunk-paired h0 is
   **t = −1.07**. The README's own "the round could not resolve it at n=160" was right; the verdict
   column was not. The table now prints the paired SE and t instead of a verdict.
3. **The hypothesised mechanism is REFUTED in C4's own archive.** It proposed "a longer exchange
   favours the side with more bodies". Per-biome `avgTurns`, instrumented A arm: MAGMA 6.36,
   TUNDRA 6.18, VERDANT 5.58 — against paint biomes 5.36 (STEEL) to **6.47 (VOID)**. The
   longest-fight biome in the batch has no mechanic. Whatever is happening, it is not that.
4. **"Every tile is derived from (MapSeed, mission)" is false.** `StampBiomeGround` also passes a
   `reserved` set built from unit and fixture positions, which come out of `Util.Rng` in
   `Mission.Build`; holding (MapSeed=424242, mission=3) fixed and varying only the ambient stream
   gives 10-11 distinct boards. **Nothing depends on the stronger claim** — zero draws is what CRN
   needs and that is independently proven — but the sentence was wrong in `Terrain.cs`, `Grid.cs`,
   `Game.cs`, `CLAUDE.md` and `FEATURES.md`. All five now say the true half: `Terrain.Stamp` is
   pure and takes zero draws; the BOARD keys on the reserved set too.
5. **The colourblind claim was refuted by measurement.** Seam-vs-floor luminance on the same vent
   tile and seed: colour **+145.5 / −28.9**, `SIGHTLINE_CB=1` **+141.0 / −28.9** — value legibility
   is identical and marginally *lower* in CB, so "more legible in CB" was wrong. What is true is
   **hue separation**: in colour the seam sits inside MAGMA's own warm palette near the barrel and
   cache amber, while the CB cream is a hue the room does not otherwise contain. VERDANT is the
   biome that genuinely improves under CB. The code comment also cited a CB advantage that no
   longer exists (the per-tile vein giving up orange) — the vein is now suppressed outright.
6. **`docs/measurements/c4/run_chunk.sh` did not exist.** The README promised the round was
   re-runnable and a bare `run_chunk.sh` pattern at `.gitignore:57` had silently swallowed it —
   **the third consecutive wave hit by the same line.** Committed, and `.gitignore` now carries
   `!docs/measurements/**/run_chunk.sh` (plus the four sibling patterns) so an archive's runner can
   never be dropped again.

### THE TESTS THAT DID NOT TEST WHAT THEY SAID

**The AI leg was a COMPOSITE, and C4 singled it out as the opposite.** The reviewer mutated
`Ai.cs`'s vent weight one value at a time: `−34 → 0` **PASS**, `−34 → −1` **PASS**, `−34 → +200`
(inverted incentive!) **PASS**. It only failed when `VentStepExtra` was zeroed too — so
`Grid.CostMap`'s toll was carrying the whole result, with `Ai.Plan`'s `Util.RandRange(0,3)`
tie-break jitter as a further confound. There are now **two** legs with two different claims:

* **(a) composite** — the behavioural claim, which is real: with everything the game ships, nothing
  lets a hostile end its move on hot ground. Jitter now pinned with `Util.Reseed` on both plans, so
  a "different tile" cannot be noise.
* **(a2) isolated** — `Terrain.VentStepExtra` temporarily zeroed so CostMap gives no signal, jitter
  pinned, and the tile asserted still REACHABLE at the same cost (otherwise "declined" is
  indistinguishable from "could not get there"). A single vent changes no sightline that starts or
  ends on it — `HasLineOfSight` tests neither endpoint — so the only thing left that can move the
  planner is the term in `Ai.cs`.

Mutation results after the fix — all four now fail, and this is the proof that matters:

| mutation | before | after |
|---|---|---|
| `Ai.cs` vent weight `−34 → 0` | PASS | **FAIL** (`aiTermDoesNothing`) |
| `Ai.cs` vent weight `−34 → +200` | PASS | **FAIL** (`aiTermDoesNothing`) |
| `Terrain.VentStepExtra 6 → 7` | PASS | **FAIL** (`ventStep=9,ventUncrossable=9`) |
| `Terrain.VentBurnTurns 2 → 1` | PASS | **FAIL** (`ventBurnTurns=1,ventBurnConst=1`) |

Two of those were **self-referential assertions** — `vc[9,5] != 2 + Terrain.VentStepExtra` and
`ventBurn != Terrain.VentBurnTurns` pass for *any* value of the constant. Both now pin the LITERAL
as well as the constant: the literal catches a changed constant, the constant catches a code path
that ignores it and hardcodes a number. `VentStepExtra = 7` mattered particularly: it makes a vent
**uncrossable by every unit in the game**, silently turning the fissure into a wall, and the test
waved it through. A new invariant leg asserts `2 + VentStepExtra <= 8` and says why.
`aiEndedOnAVent` was redundant with `aiParkedOnVent` (one vent tile in the scene) and is dropped.
**`Terrain.VentBurnTurns` was dead code** — `TickHazards` hardcoded `2` — so the vent now re-ignites
for its own constant. Both tuning numbers became `static` rather than `const`: a `const` folds at
compile time, which made the literal pin *unreachable code* and (caught by the 0-warning build) it
could never have failed.

**The density guard measured a board that never occurs in play.** The sweep ran on `OpenGrid()` —
every tile floor, `reserved = null`. On real `SetupMission` boards the layer fell below C4's own
floor on **43/200 MAGMA, 25/200 TUNDRA, 21/200 VERDANT**, MAGMA's minimum was **1 vent tile**, and
13/200 boards had ≤4 — i.e. C4's own stated failure ("a mechanic that silently vanishes on some
seeds is worse than one that is merely small") was still happening on ~1 MAGMA mission in 5, while
the budgets 44/34/24 barely bound anything. The sweep now builds **real missions** through the real
`SetupMission` with the biome pinned by `SIGHTLINE_FORCEBIOME`, and pins mean, min, flood and a
thin-board share. The open-grid bound is kept as a separate, weaker, clearly-scoped claim about the
stamper's own ceiling. **BIOMETEST prints the measured densities in its PASS line every sweep**, so
a comment can never drift from the shipped value again — which is exactly how this defect survived.

### THE FOUR DESIGN/LEGIBILITY MUST-FIXES

**M1 — MECHANICAL GROUND ON A PLATEAU RENDERED AS ZERO PIXELS.** `Terrain.Free` gated on
`grid.IsFloor`, which is `Tiles==Floor && !Barrel` and has never looked at `Height`. `DrawGround`
runs before `DrawElevation`, which paints the plateau top with a **fully opaque** rect offset by
`-lift`. So fern and ice on raised ground were invisible: a soldier could stand on raised
undergrowth and take omnidirectional low cover **with no mark on the board at all**, and a raised
vent kept only its steam, drawn at the un-lifted tile centre. Two of ~nine vents on the
photographed board. Fixed at the SOURCE — the layer never stamps on `Height > 0` — because a
plateau already carries its own rule (high ground sees over low cover) and stacking a second one on
it is muddier than keeping them apart. **BIOMETEST structurally could not see this**: `OpenGrid()`
sets `Height = 0` everywhere. There is now a direct leg that builds a plateau and asserts nothing
lands on it, plus a `groundOnPlateau` assertion on the real-board sweep.

**M2 — THE SHIPPED DENSITY WAS NOT THE STATED DENSITY.** `Terrain.cs` claimed VERDANT targeted
"~18-22% of the 198-tile board"; C4's own `byBiome.avgGroundTiles` over 24 instrumented chunks said
**12.1%** (23.99 tiles). MAGMA's 9.66 tiles across *two* walkers is ~4.8 per crack — on some seeds a
scatter of singles rather than a line, which makes the fords story true on some boards and
meaningless on others; and a captured VERDANT "patch" was five separate single tiles, contradicting
this file's own rationale that a patch "has to be several tiles wide or it is just a decorated
tile". Re-tuned against real boards (more patches, more lobes, larger lobes; the fissure's
*deliberate* ford rate cut 18% → 8% because the involuntary gaps from cover already supply them,
and the walk lengthened). Measured after, on real boards: **VERDANT 35.0 (19-44) = 17.7%, TUNDRA
18.3 (10-34) = 9.2%, MAGMA 13.0 (7-20) = 6.6%** — MAGMA's real minimum goes 1 → 7. The comment now
states the measured shipped numbers and BIOMETEST prints them.

**M3 — C4 BROKE ITS OWN NEW RULE, ON ITS OWN BIOME.** The wave's headline rule was "when a biome's
mechanic is live, the biome's decorative version of that motif is retired" — and TUNDRA's Snow
signature draws **paired pale diagonal hairlines** (210,232,248 @0.18) on ~55% of floor tiles while
the drift's mechanical fracture is **a pale diagonal hairline** (226,244,255 @0.34). Identical
motif, ~2× alpha apart. C4's skip only stopped the double-draw *on* a mechanical tile and left the
decoy everywhere else. Now suppressed board-wide when `AnyIce`, exactly as the MAGMA vein is
suppressed when `AnyVent`. Two lesser collisions fixed with it: an **isolated ice tile** drew all
four rim edges — a complete bright box on the tile grid, which is the game's cursor/selection
language — and now gives up its outline (a rim traces a LANE; one tile has no lane); and the fern
rim wore **(142,210,130)**, sitting on `Pal.Good` (74,222,128), the hue this palette reserves for
"bonus / available action" against §3.H's one-accent-one-job — pulled to a desaturated olive
(178,198,108) with the value contrast unchanged. *And the layer C4 apologised for in its own "what
I did not do" — the ambient particles — was the wrong one: those are round motes and are harmless.*

**M4 — TWO MODES NEVER ANNOUNCED THE RULE.** `BeginBriefing` returns early for anything but
CAMPAIGN, so SKIRMISH, DAILY, LAST STAND and TRAINING stamp the ground layer and never compose the
card that states it. The LAST STAND banner had no `BiomeMechTag()` at all — endless stamps the
ground and announced it nowhere — and the campaign **FINALE** banner dropped the tag too. Both
banners patched; the non-campaign modes now get the one-sentence rule on the banner's SUB-line,
the surface they already have, and only when nothing else has claimed it.

### Smaller items, all declared

* **Double sear.** `OnUnitEnteredTile` ran the fire block and the vent block as two unconditional
  `if`s, so a tile that was both on fire and a vent charged `BurnDamage` **twice** in one step (a
  grenade or barrel can light a vent tile: `LightFire` only requires Floor, and a vent is floor).
  One sear per step now, fire named first. The round was measured WITH the double charge, so its
  published cost is an upper bound; the fix only ever reduces damage. Pinned by a `doubleSear` leg.
* **The intel cache is reserved at radius 0** while the doc comment said "all with their rings".
  Comment corrected rather than the code: the cache is an optional pickup, not a win condition, and
  a soldier detouring for it may reasonably pay for the ground around it.
* **No `avoidVent` leash pass.** Fire gets a two-pass VIP leash (an `avoidFire` pass first); vents
  get score penalties only, and a penalty is not a veto, so the Escort asset can be routed through
  a vent and seared. C4's archive shows no measurable harm (Escort −0.74 ± 1.99) but "mirroring the
  existing fire-route term" understated the asymmetry. Recorded, unrepaired.
* **"The gaps are the fords" is overstated, and there is a mobility cliff under it.** Treating
  vents as walls, **47/200 (23.5%) of MAGMA boards** have an objective fixture or hostile with no
  vent-free route — on a quarter of MAGMA missions crossing is *mandatory*. And a vent step costs
  **8 half-tiles**, exactly a full-mobility soldier's entire single-action walk, so a **wounded**
  soldier (budget 6) cannot enter one at all; 15 tiles across 200 boards have every walkable
  neighbour a vent, where a wounded soldier can never move again (it can still shoot, so no stall).
  The codex now states the real number instead of "costs extra movement".
* **The CODEX documented three mechanics that did not exist** when `SIGHTLINE_BIOMEMECH=0` — the
  entries were added unconditionally. Gated. A fourth entry now names the five rooms that have NO
  ground rule, because a player could not otherwise tell "no rule" from "undocumented".
* **The briefing card's rule line looked exactly like the three flavour lines** and sat last. It
  gets a `GROUND — ` prefix and the em dash its neighbours use.
* **No screenshot hook.** Every comparable visual feature ships one; judging C4 meant hunting
  seeds. `SIGHTLINE_BIOMESHOT` added (pair with `SIGHTLINE_FORCEBIOME` + `SIGHTLINE_SHOT=760`).
* **CLAUDE.md's clock-read count was fixed in one place and stale in another** (line 393 still said
  45). Both now say 46.
* **Flagged for C5, not mine:** `Hud.DrawThreatCard` draws with raw `Raylib.DrawTextEx` at fixed
  12/14px, bypassing `Cfg.Text` and the TEXT SIZE setting, which CLAUDE.md forbids explicitly.
  Pre-existing — W10's FITTEST does not cover this card — but C4 put its longest, most rule-dense
  strings on it.

### What this addendum does NOT do

1. **It does not re-measure the round.** Instructed not to, and the numbers all reproduced exactly.
   The consequence is stated plainly and in three places: **the shipped layer is no longer the
   measured layer** (M1 moved it off plateaus, M2 raised density), so `docs/measurements/c4/` is a
   pre-fix price and C5 owes it a re-measure before anyone quotes it as the cost of what ships.
2. **It does not repair the mission-1 concentration**, the mobility cliff, the mandatory-crossing
   share, or the leash asymmetry. All four are in ROADMAP with their numbers.
3. **It does not give the five paint biomes a mechanic.** Still declared, still asserted.

# WAVE "SETTINGS EVERYWHERE" (2026-09-02, dev on `wave/settings-everywhere`, base `cee3cba`)

## Thesis

The pause card is the SOLE home of TEXT SIZE, COLORBLIND, BRIGHTNESS, GAMMA, ANIM SPEED, SCREEN
SHAKE, THREAT PREVIEW, AUTO-CAM and FULLSCREEN, and `Game.Update` read Escape only under
`Phase == PlayerTurn || Phase == EnemyTurn`. So the 120% text size that would lift every sub-12px
string in the game to the floor, and the colourblind palette, could not be reached until the
player was already in a fight. The INTRO had ten doors and no settings entry; `case Phase.Barracks`
had no Escape handler at all, no route to the FIELD MANUAL (`K` was Intro-gated) and no way to
the card. C5 found it, reproduced it and left it (ROADMAP "Left open by C5"). This wave closes it.

**Confirmed on the base tree before touching anything:** `src/Game.cs` line 3960 read
`if (!AutoPlay && (Phase == Phase.PlayerTurn || Phase == Phase.EnemyTurn))` around the only
`KeyboardKey.Escape` read that toggles `Paused`; `grep -n 'KeyboardKey.K' src/Game.cs` gave one
site, inside the `Phase == Phase.Intro` block of `HandleOverlayClick`; `case Phase.Barracks` in
`Update` contained no `Escape` read and `HandleShopClick`'s only Escape read is gated on `ArmoryMode`.

## What shipped

- **`Game.SettingsCardPhase(p)`** is the ONE gate `Update` reads before it looks at Escape, and it
  now names `PlayerTurn, EnemyTurn, Intro, Barracks`. The Escape read was lifted into
  **`Game.OnEscape()`** (cancels a targeting mode first, else toggles the card and disarms QUIT —
  the W5 contract, unchanged), and the pause card's click dispatch was split into
  **`PauseHit(m)`** (which rect) + **`ActPause(id)`** (what it does) so a self-test can hit the SAME
  rect the mouse would. `HandlePauseMenu` is now `ActPause(PauseHit(m))`; the in-fight behaviour is
  byte-for-byte the same list of controls in the same order.
- **INTRO:** a **SETTINGS** door in the utility grid, paired with AUDIO CHECK on the third row
  (`Hud.IntroSettingsBtn`, key **`[O]`**), with a hover caption. QUIT moved to a fourth row at the
  grid's full width, so the exit reads as the exit. Escape on the intro opens the same card.
  `O` was derived free with `grep -ohE 'KeyboardKey\.[A-Z][a-z0-9]*' src/*.cs | sort -u` before
  binding: the free letters were `I J O Z`; they are now **`I J Z`**.
- **BARRACKS:** Escape opens the card — unless the ARMORY sub-screen is open, where Escape already
  means "back out one level" and the card must not steal it (`OnEscape` yields to `ArmoryMode`).
  **`[K]`** opens the FIELD MANUAL from the barracks. `ExitCodex` / `ExitAudition` now return to
  whichever `SettingsCardPhase` opened them (they returned to the intro from anything that was not a
  fight), and restore the card if it was open — the FUL-2 rule, extended by the same predicate.
- **The card outside a fight is a SETTINGS card:** title SETTINGS, first row **BACK** instead of
  RESUME (`Hud.PauseResumeLabel` / `Hud.PauseTitle` key off `Game.CardInFight`), footer hint
  "Every change is saved as you make it - [Esc] back" instead of the camera legend, **no ABANDON
  row** (the two remaining exits stay bottom-aligned with the left column — the card was not
  re-laid out), QUIT TO DESKTOP kept with a **phase-true armed sentence** (`Game.QuitWarning`).
- **ABANDON in the BARRACKS — decided NO, and why.** `AbandonRun` promises "The checkpoint is
  kept - CONTINUE resumes it". `SetupMission` is the only GAMEPLAY `SaveGame.Save` caller
  (`src/Game.cs` ~line 2300, written at mission START; `grep -n 'SaveGame.Save('` gives seven
  sites, and the other six are harness stashes — `Events.cs`'s self-test, `Game.Modes.cs`'s,
  `Program.cs` x2 and `Ship.cs`), so in the debrief the file on disk is the start of the mission
  the player just CLEARED. CONTINUE would replay a won mission and drop
  every debrief pick, and `AbandonRun` would call `Stats.EndMission` on a mission already closed.
  QUITTEST's contract (the quit path writes nothing, deletes nothing, keeps the mission-start
  checkpoint byte-identical) is the one that IS safe there, so the barracks card offers BACK and
  QUIT TO DESKTOP, and QUIT's armed sentence there says what it costs — the exact wording was
  re-cut in review round 1 (below) after the first drafts overflowed the card; the shipped set is
  in `Game.QuitWarning`. The in-fight CAMPAIGN sentence is unchanged; the other four modes got
  their own in round 1.
- **Persistence is the same writer.** The card's controls call the same `Display.CycleUiScale` /
  `ToggleColorblind` / … as in-mission; each ends in `Display.Save()` (the atomic writer C6 proved).
  Nothing new writes anywhere; the harness paths stay `NoPersist` (a screenshot run never clicks).

## The test — and it FAILED on the pre-fix tree

`SIGHTLINE_SETTINGSTEST=1` (`Game.SettingsSelfTest`, `src/Game.Harness.cs`), routed through
`verdict` in `scripts/qa-sweep.sh`. Four legs: **(A) INTRO** — the door exists, overlaps no other
door, sits above the footer; Escape opens the card on the intro, the card publishes TEXT SIZE /
COLORBLIND / QUIT rects and NO abandon rect, the first row reads BACK, TEXT SIZE changes through
`PauseHit` -> `ActPause` on the rect's centre, `Cfg.UiScale` follows, `display.json` re-read
through `Display.LoadForTest` carries the new index, Escape closes it and the phase is still
Intro; FIELD MANUAL and AUDIO CHECK opened from the card come back TO the card on the intro.
**(B) BARRACKS** (`DebugShop`) — the same round trip, no abandon, BACK; Escape with the ARMORY
open does NOT open the card; `[K]`'s `BeginCodex` returns to the barracks with no card invented.
**(C) PLAYER TURN** — the existing home pinned: RESUME, abandon offered, Escape in AIM cancels aim
and does not open the card, closing the card disarms QUIT. **(D)** Escape leaves the card closed
on WarRoom / Codex / Draft / SkirmishSetup / AudioCheck / Win / Lose, and never opens under
`AutoPlay`. Stashes and restores `display.json` and the scale statics; pins `Hud.MousePin` off-card.

Pre-fix (the test in the tree, the fix not yet):

    SETTINGSTEST: FAIL (intro:noSettingsDoor,intro:escapeDoesNotOpenCard,intro:codex:noCard,intro:audio:noCard,barracks:escapeDoesNotOpenCard,barracks:codex:noCard,barracks:audio:noCard,barracks:manualBackLandsOn:Intro)

The mission leg passed pre-fix, which is the proof that the `OnEscape` / `PauseHit` / `ActPause`
refactor is behaviour-preserving for the fight. Post-fix: PASS. CONTRASTTEST gained the SETTINGS
door in its main-menu list (10 labels; SETTINGS reads ~12:1 — it floats with the clock-animated
backdrop: 12.14, 11.88 and 11.99 on three runs, all far above the 4.5 floor). QUITTEST, CODEXTEST,
AUDITIONTEST, FITTEST, CHROMETEST, BACKDROPTEST re-run green.

## Screenshots

`SIGHTLINE_SETTINGS=1` (alias of the existing `SIGHTLINE_PAUSE=1`) composes with
`SIGHTLINE_INTRO=1` and `SIGHTLINE_SHOP=1`: the card opened from the intro and from the barracks
were photographed at frame 90 and inspected — title SETTINGS, BACK, two exits, footer hint, every
label through `Cfg.Text` at >= 12px. The intro with the door was photographed with
`SIGHTLINE_INTRO=1 SIGHTLINE_SHOT=90`.

## What this wave did NOT do

1. **No re-layout of the pause card.** Two exits instead of three outside a fight is a row omitted,
   not a layout; every rect the in-fight card publishes is where it was.
2. **The 12px floor at the default text size** (the item above this one in ROADMAP) is untouched.
   This wave only makes the 120% setting reachable before the first fight.
3. **No route from the BARRACKS back to the intro.** The only candidate (`AbandonRun`) would lie
   there, for the reason above; QUIT TO DESKTOP is the honest exit and it is now offered. A
   "MAIN MENU" plate that costs exactly what QUIT costs is a separate decision.
4. **No balance change.** Nothing in `Combat`, `Ai`, `Mission`, `Heat` or the map generator was
   touched; autoplay `RESULT: WIN mission=6 frame=12410 turns=29` on one Debug run, no exception.
5. `CardInFight` treats an in-fight TRAINING OP / SKIRMISH / LAST STAND like a campaign fight
   (RESUME, mode-true abandon verb) — unchanged from before.

## Review round 1 (2026-09-02) — what the reviewer found, what changed

Ten findings; 1-8 required, 9-10 taken because they were cheap. Every code fix below ships an
assertion in `SIGHTLINE_SETTINGSTEST` that was **proven to fail** by temporarily breaking the fix,
building, running, and restoring (`cmp` byte-identical each time); the FAIL lines are quoted verbatim.

1. **MAJOR — Escape went dead in the barracks after ARMORY + Enter.** `HandleShopClick` read Enter
   before its armory branch and set `_shopDone` without clearing `ArmoryMode`; `Hud` stops drawing
   the requisition once `ShopDone`, so the stale flag was invisible, but `OnEscape`'s armory
   exception (`ArmoryMode && !Paused`) kept swallowing Escape for the rest of the visit. **Two
   locks:** Enter and the PROCEED plate now leave the shop through one seam, `Game.ProceedFromShop`,
   which clears `ArmoryMode`/`ArmorySoldier`; and the exception is gated on `!_shopDone`, mirroring
   the reachability of the armory's own Escape read (`if (!_shopDone)` in `Update`). Either lock alone
   closes the hole, which the break-tests show — breaking the predicate alone trips only
   `barracks:staleArmoryFlagSwallowsEscape`, breaking the clear alone only
   `barracks:proceedLeftArmoryOpen`, and breaking BOTH reproduces the reviewer's scenario:
   `SETTINGSTEST: FAIL (barracks:proceedLeftArmoryOpen,barracks:escapeDeadAfterArmoryProceed,barracks:staleArmoryFlagSwallowsEscape`.
2. **MAJOR — the intro / barracks armed-QUIT sentences overflowed the card.** 74-75 chars centred on
   a plate whose centre is 200 px from the card's right edge. Every sentence is now **<= 46 chars**,
   and the new `ArmedFits` leg measures each one against `Hud.PauseCard` (published by `DrawPause`)
   — **at all four shipped TEXT SIZES**, because the leg's first cut found what the reviewer's 100%
   arithmetic could not: `CardRoundTrip` cycles TEXT SIZE, so the barracks sentence was measured at
   120% and a 50-char sentence read **409 px** there against 400 px of room (the sentence scales,
   the card does not). 12px NotoMono is ~6.8 px/char at 100% and ~8.2 at 120%. Break-test with the
   original intro sentence:
   `intro:armedSentenceOverflows@90%:478px,…@100%:524px,…@110%:569px,…@120%:614px`.
   `SIGHTLINE_QUITARMED=1` composes with `SIGHTLINE_PAUSE`/`SIGHTLINE_SETTINGS` to photograph the
   armed state; all three homes were shot and inspected (the sentence ends at x≈977 inside a card
   edge at 1019). The 12px line sits one row above the footer with a ~1-2 px gap in all three — that
   is the geometry W5 shipped for the fight card, unchanged, and it was checked at 3x zoom for
   overlap (none). Placement stays under the plate it explains rather than moving to the footer slot.
3. **MAJOR — a click on nothing no longer disarmed QUIT.** The `PauseHit`/`ActPause` split had put
   `if (id == null) return;` above the disarm. Order is now quit -> disarm -> null-return, which is
   `HandlePauseMenu`'s original contract. Asserted through the same seam the mouse uses
   (`PauseHit` at an off-card point returns null, `ActPause(null)` disarms). Break-test:
   `mission:clickOnNothingDidNotDisarmQuit,mission:quitFired`. The "byte-for-byte the same" claim
   in "What shipped" above was FALSE for this one path between round 0 and this fix.
4. **MAJOR — "[K] FIELD MANUAL" on the card was a dead hint** in all three homes (only Q was read
   while the card was open) and README promised it. `Game.PauseKeys` / `PauseKeyId` is the card's
   key table; `HandlePauseMenu` walks it. `CardDetour` now routes the codex id through
   `PauseKeyId(K)` and asserts K is in `PauseKeys`. Break-test (K removed from the table):
   `intro:codex:keyKNotRead,barracks:codex:keyKNotRead,mission:codex:keyKNotRead`. `Codex.cs`'s
   "THE REST" entry no longer says "[K] opens this manual from anywhere" — it names the three
   places (main menu, barracks, pause card) and the [O] / Esc settings routes.
5. **MINOR — the PASS sentence over-claimed.** The intro's nine `bool x = (click && rect) || key`
   doors are now one table, `Game.IntroKeys` + `IntroHit(m)` / `IntroKeyId(k)` / `ActIntro(id)`
   (the `PauseHit`/`ActPause` shape; click resolved before key, as before; `ActIntro("continue")`
   falls through when `ContinueRun` refuses, as `if (cont && ContinueRun()) return;` did). Leg A now
   opens the card through `IntroHit` on the door's centre and through `IntroKeyId(O)` — break-test
   (settings row removed): `intro:settingsRectHits:nothing,intro:keyOMaps:nothing`. `OnEscape`
   itself refuses under `AutoPlay` — break-test: `autoplayEscapeOpenedCard,autoplayUpdateOpenedCard`.
   The PASS sentence lists only what is asserted.
6. **MINOR — the in-fight sentence lied in four of five modes.** LAST STAND / SKIRMISH / DAILY /
   TRAINING never write `save.json` (`SetupMission`'s checkpoint is Campaign-only) and their one
   persisted result is recorded at the END of the fight, so a mid-fight quit loses it.
   `QuitWarning` is now mode-aware: endless "the stand ends here - its waves are not saved",
   training "the drill is not saved - run it again any time", skirmish "nothing is saved - the fight
   simply ends here", daily "today's run is not recorded - retry any time"; campaign unchanged. The
   mission leg asserts each fits and that none of the four repeats the campaign sentence.
7. **MINOR** — the duplicate key registry in `src/Game.cs` now reads I J Z and points at the grep.
8. **MINOR** — the FEATURES bullet moved below QUIT TO DESKTOP's `(SIGHTLINE_QUITTEST.)` tail.
9. **NIT** — "12.14:1" is now "~12:1, floats with the animated backdrop (12.14 / 11.88 / 11.99)";
   "ONLY `SaveGame.Save` caller (one site)" is now "only GAMEPLAY writer" with the six harness sites named.
10. **NIT** — ROADMAP's "deliberately not fixed" heading reworded over its ticked item; FITTEST gained
    `SETTINGS-INTRO`, `SETTINGS-INTRO-ARMED`, `SETTINGS-SHOP`, `SETTINGS-SHOP-ARMED` and `PAUSE-ARMED`
    (45 screens / 50 legs now), so the new footer copy and every armed sentence are inside the fit audit.

**After:** `dotnet build -c Release` 0 warnings / 0 errors; `SETTINGSTEST: PASS`, `QUITTEST: PASS`,
`FITTEST: PASS`, `CODEXTEST: PASS`, `CONTRASTTEST: PASS`; derived hook counts exist = run = 74; one
Debug autoplay `RESULT: LOSE mission=6 frame=12235 turns=33`, no exception.
# PROGRAM PARALLAX — GATE FIXES FOUND IN PASSING (2026-09-02, lead, working branch)

## BIOMETEST sampled the wall clock and failed one sweep in six

The C4 review moved BIOMETEST's density guard onto REAL boards built through `SetupMission`, and
pinned `MapSeed` per board — but the ground stamp only decides *what* lands where the board is
free, and *where the board is free* (cover, barrels, plateaus, the reserved rings) is rolled off
the shared `Util.Rng`, which that loop never reseeded. So the "40-board" sample was a different
forty boards every run, and its `realMin < 4` floor tripped on the tail. Measured on the untouched
base binary (`cee3cba`, Release, six runs): **5 PASS, 1 FAIL (`realMin[MAGMA]=2`)**, and the PASS
lines' densities drifted run to run (`TUNDRA~18.2(8-31)` … `18.0(4-34)`). It first showed as a red
line in a green wave's pre-merge sweep, which is exactly the cost of a random gate.

Fix: `Util.Reseed(70200 + sd * 4 + m)` before each `SetupMission` in the loop. Three consecutive
runs now print the identical line (`TUNDRA~17.7(8-28) VERDANT~34.0(17-44) MAGMA~12.9(5-20)`), so
the densities in the PASS line are comparable across commits for the first time.

## FITTEST un-staged TOOLTIP-HOVER on one run in six — the pin came after the settle frames

Two earlier commits (`4f91da0`, `28c6b63`) fixed this flake "by construction" and at "its actual
root cause"; it still fired once in the merged P1+P2 gate and reproduced **1 in 6** on the Release
binary under load: `screenNotStaged:TOOLTIP-HOVER@100%(identical frame to TOOLTIP-AIM)`. The
mechanism this time: the screen audit stages a screen, runs **three `Update` frames** to let it
settle, and only THEN parks `Hud.MousePin` — but `Game.UpdateHoverAndAim` resolved the hover tile
from `Raylib.GetMousePosition()` (the live pointer, never the pin) and handed the keyboard cursor
back to the mouse on any `GetMouseDelta()`. A spurious pointer event from Xvfb, whose timing
depends on load, flipped `KbCursor` off during the settle, the hover fell back to the live
pointer, the threat card never staged, and the HOVER frame collapsed onto the AIM frame.

Fix, in three lines and a move: `Hud.Mouse()` is public and `Update` reads the pointer through it;
`Hud.MouseDelta()` reports zero while pinned, so a pinned cursor cannot be handed back; and the
audit parks the pointer BEFORE `Stage`, so a stager's own pin (the tooltip's foe seat) survives the
settle and the draw instead of being overwritten by the park. In real play `MousePin` is NaN and
every read is the live pointer, exactly as before. Measured after: 6/6 PASS on the same binary
under the same load. The sweep now also quotes a FAIL line's detail (`qa-sweep.sh` keeps each
hook's full output), which is how the next flake gets its mechanism named on the first sighting.

**What this does NOT fix, recorded in ROADMAP:** the tail is real. On roughly one board in ~240
MAGMA still stamps fewer than 4 vent tiles — C4's own "a mechanic that silently vanishes on some
seeds" — and a pinned sample that happens to clear the floor does not make that go away. The
floor assertion is now a regression guard on a fixed sample, not a claim about the population.

# WAVE "THE STRIDE" — PILLAR 2 GETS A GATE (2026-09-02, dev on `wave/the-stride`, base `cee3cba`)

**Branch** `wave/the-stride` off `main` at **`cee3cba`**. Files touched: `src/Anim.cs`, `src/Game.cs`,
`src/Game.Harness.cs`, `src/Fx.cs`, `src/Unit.cs`, `src/Renderer.cs`, `src/Program.cs`,
`scripts/qa-sweep.sh`. A first developer wrote the self-test and the scaffolding (the threshold
constants on `MoveStepAnim`, `Fx.TextSep`, an `EnqueuePath` helper that was still a plain foreach)
and was cut off; this write-up covers the whole wave.

## THE STRIDE — the thesis

Every pillar in `docs/DESIGN.md` had a line in the sweep except the second one. "Feels good" was
graded on tracers, shake, hit-stop and floating numbers, and nothing ever measured the MOST
FREQUENT action in the game: a soldier walking six tiles. It caterpillared. `MoveStepAnim` is one
tile of a path, and every tile eased in AND out (`Util.EaseInOutQuad` per step) and re-kicked
`WalkLean`, so a six-tile move was six separate lunges with the figure coming to rest 0.1 px short
of every centre — and on top of the easing, a 0.12 s step at 60 Hz needs `ceil(7.2) = 8` frames to
commit, so 7.2 frames of motion were squeezed into 8 at every boundary. A VAULT, the one verb whose
whole meaning is "over the wall", was a straight 0.12 s slide through the cover tile. And a kill
printed its three strings — the number, KIA, the name stamp — inside 24 px of one anchor, while two
equal overwatch hits printed one number exactly on top of the other.

None of that is the sim. All of it is what the player sees the sim do, forty times a mission.

## The gate, and its FAIL on the pre-fix tree — verbatim

`SIGHTLINE_FEELTEST=1` drives the REAL anim queue the way `Game.Update`'s pump does (activation
`OnStart`, one `Update` per 1/60 s frame) and measures the tween on `Unit.Pos`. Three legs:
a six-tile walk through `EnqueuePath` (per-frame speed over the mid-path with the departure ramp and
arrival brake excluded, `MoveStepAnim.FeelMidBand` = half a tile at each end; stall frames; lean
kicks; backward steps; the 48-frame commit cadence PINNED), a VAULT through `IssueVault` (peak lift,
where the peak sits, landing on the exact centre), and stacked floating text after one `Fx.Update`
(pairwise anchor distance against `Fx.TextSep`; the twins must arc in opposite directions).

On `cee3cba` plus the scaffolding it FAILED on all nine assertions:

```
FEELTEST: walk: 48 frames, mid-path px/frame min 0.1 mean 7.8 max 16.5 (min/max 0.01), stalls<0.40xmean 10, leanKicks 6, backSteps 0; profile [2.5 7.4 12.3 16.5 13.3 8.4 3.5 0.1 x6]; vault: 8 frames, peak lift 0.0px at x=416 (cover spans 448-512), landed (7,5) pos (544.0,392.0); text: kill-trio min sep 7.2px, brace-pair 4.0px, twin numbers 0.0px, twins diverge False
FEELTEST: FAIL (walkMidSpeedDip(0.01<0.60),walkStalls(10),walkLeanKicks(6!=1),vaultLift(0.0px<20px),vaultPeakNotOverCover,killTrioOverprint(7.2px),bracePairOverprint(4.0px),twinNumbersOverprint(0.0px),twinNumbersSameArc)
```

Read the profile: `2.5 7.4 12.3 16.5 13.3 8.4 3.5 0.1`, six times. The 0.1 is the quantisation
frame (the commit clock's last 2.8% of the tile), the 16.5 → 2.5 is the easing. Ten of the 40
mid-path frames were stalls.

## What shipped

**1. One stride per walk, not one per tile.** `MoveStepAnim` gained a segment flag
(`StepSeg` Single / First / Mid / Last), a `Path` polyline shared by every step of one walk, and its
`Index` in it — all set by `Game.EnqueuePath`, which is now the ONLY way a path is enqueued
(player move, enemy planned move, enemy tempo reposition, the pod reveal-scatter, the VIP leash,
`DebugLongMove`; `grep 'new MoveStepAnim'` finds the funnel itself, `IssueVault` and one harness
purge probe, all Single). The drawn position is one arc-length profile over the WHOLE walk —
a linear speed ramp over the first `StrideRamp` = 0.5 step-times, a constant stride at
`n/(n − 0.5)` tiles per step-time, and the mirror-image brake — evaluated at a stride clock the
steps hand to each other through `Unit.StrideTau`. For n = 1 that profile IS `EaseInOutQuad`
(same triangle of speed), so a single step still draws exactly as it did. `WalkLean` is kicked on
First/Single only: it is the push-off, not a per-tile pump.

The part that took thought is the clock. The commit needs `ceil(_dur/dt)` frames, so a pure
linear tween on `_t/_dur` still stalls on every 8th frame (1.8 px against 8.9) — the frame the
old profile shows as 0.1. The drawn stride therefore runs on the PREDICTED commit period
(`MoveStepAnim.PredictPeriod`: `ceil(_dur/dt)·dt`, taken from the first frame's dt) and reaches
each centre ON the commit frame; a frame-time jitter that makes the prediction miss by a frame
becomes a one-frame lead or lag the next step absorbs through the carried clock — never a snap
back, never a stall. The commit clock (`_t/_dur → k ≥ 1 → tile entry → OnUnitEnteredTile →
overwatch`) is untouched, and so is `_dur` (0.12 / 0.155 s).

Consequences the reader should know: the figure now TRAILS the commit clock by up to a quarter
tile during the push-off and LEADS it by the same during the brake (derived: `(1 − ramp/2)` of the
first step; 11.6 px on a six-tile walk, 16 px in the limit) — so an overwatch reaction on the first
tile fires with the figure a few px short of that tile's centre. A Mid step whose walk is cut short
at its own commit (the reaction killed or downed the mover and `PurgeAnimsFor` dropped its later
steps) sets the figure down on the tile (`Game.HasQueuedStep`), and `PurgeAnimsFor` itself now sets
a felled mover on its tile if it is within half a tile of it (a body further off is inside a
shove/drag tween and is left to that anim). A corner is faced along the segment
(`Path[i+1] − Path[i]`), not from the drawn position, so the trail cannot turn it short.

**Diagonals — derived, not measured.** A diagonal step commits in 0.155 s over 90.5 px against
0.12 s over 64 px: the commit cadence ITSELF is 9.5% faster on a diagonal (584 vs 533 px/s), and at
60 Hz it quantises to 10 frames against 8, so a diagonal draws at 9.87 px/frame in a walk whose
straights draw at 8.73 — a 0.88 ratio against the 0.60 gate. Making them identical would need
either a 0.170 s diagonal commit (a sim-timing change, out of scope) or letting the figure lag its
committed tile by 15 ms per diagonal, which accumulates to a visible pop on the Last step. So the
drawn speed follows the commit speed of each step type, and a mixed path has a 13% step at a
straight→diagonal boundary. FEELTEST's walk is straight; nobody has filmed a mixed one.

**2. The VAULT arcs.** `MoveStepAnim.Hop` (px of lift) and `VisDur` (the drawn duration);
`IssueVault` sets 26 px and 0.24 s. The lift is `sin(k·π)·Hop` taken off the DRAWN Y — `Unit.Pos`,
which is what `Renderer.DrawUnit` reads — with the horizontal on `EaseInOutQuad` (crouch, spring,
settle). `Unit.HopLift` carries the lift to the renderer so the ground shadow stays on the deck and
shrinks a touch at the top. The commit stays on `_dur`: the tile entry, and any overwatch it draws,
fires on frame 8 of 15, with the figure over the cover — a vaulter can be shot out of the air, and
if it dies there `PurgeAnimsFor` sets the body down on the landing tile. Landing: `Pos = _to`
exactly, `HopLift = 0`, a 5-particle `Fx.Dust` puff and a heavier `move` footfall. The autopilot
never vaults (`grep -i vault src/Game.Autopilot.cs` is empty), so the longer anim cannot move a
flywheel number; a PLAYER's vault holds the queue 15 frames instead of 8.

**3. Floating text climbs a ladder.** `Fx.PopText` and `Fx.Stamp` place a new text on the first
rung above its anchor whose text box is clear of every LIVE text (`Fx.TextRung`: rungs 18 px up,
odd rungs 18 px to one side; a box is `0.6 em × length` wide — NotoMono's advance, no font needed
headless — and `size` tall; live texts are tested where they ARE now, so a number that has risen
out of the way costs nothing). The brief said "count texts within 22 px and offset by n × 16 px";
that rule mis-places a text against a neighbour that has ALREADY risen 16 px (it lands on top of
it), which is why it became a clearance search instead. The arc direction hash is salted with a
plain per-call counter (`_textSerial`, not `Util.Rng`), and a number popping within 48 px of a live
ARCING number arcs the other way — structurally, so twin overwatch hits diverge every time rather
than at the mercy of a hash. `Fx.Texts` is read by nothing in the sim.

# WAVE "THE MODES GET THE BESTIARY" — TWO OF FOUR MODES SEE THE ROSTER (2026-09-02, dev on `wave/modes-bestiary`, base `3f3e478`; PARALLAX P4)

## The thesis

SKIRMISH and DAILY both enter the fight through `SetupMission(1)`, and `n` has always carried two
jobs at once: the NUMERIC ramp (headcount, the `(n-1)` stat bump, the opener trim, W9's heat
arithmetic) and the ROSTER gates — `SelectArchetype`'s tier (`n <= 1` deals SCOUT or GRUNT and
nothing else), `podsOf3 = n >= 3`, the named mid-boss at `n == 3 || n == 5`. W9 THE REPAIR made
the heat dial NUMERICALLY real for these modes and stopped there. A fresh `Run` sits on its Start
node, which `GenerateMap` deliberately leaves `Faction.None`, so `FactionRoster` never ran either.
The result, measured on `3f3e478`: **a heat-8 skirmish was a dozen SCOUT/GRUNTs with bigger
numbers, and the DAILY was the same two archetypes every day.** Twenty of twenty-two archetypes,
all three factions, pods of 3 and every mid-boss kit were unreachable in two of the game's four
modes — the docket's content-breadth finding 0, research confidence 5.

## The gate, and its FAIL on the pre-fix tree — verbatim

`SIGHTLINE_MODETEST` gains legs (8) and (9). Leg 8 reseeds (`Util.Reseed(9001)`, so the fifty
builds are one deterministic sequence, not a probability claim), runs `BeginSkirmish(Eliminate, 0)`
fifty times and demands at least six classes outside SCOUT/GRUNT/ELITE, at least one build with a
pod of three, and NO named elite at heat 0; then ten builds each at heat 4 and heat 8 must field
EXACTLY one `ELITE`. Leg 9 runs `BeginDaily()` twice for the harness stamp and demands a non-`None`
faction and an identical `ForceSignature()` — every hostile's class, name, HP, aim and pod in spawn
order plus the mission faction; `BoardSignature` already pinned WHERE the bodies stand, this pins
WHAT they are.

Reproduced on the base worktree (`3f3e478`, clean) by porting ONLY `ForceSignature` and the two
legs into its `Game.Modes.cs` — they compile against the old two-argument `BeginSkirmish` — then
building Release, running, and restoring the file (`git checkout`, tree clean again):

```
MODETEST skirmish h0 roster over 50 builds: GRUNT/SCOUT  pods-of-3 in 0/50  bodies h0=4 h4=6 h8=8
MODETEST daily 20260701 faction=None force=03cdcca8  (must match across processes)
MODETEST: FAIL (skirmishRosterShallow(classes=GRUNT/SCOUT),skirmishNeverPodsOf3,skirmishMidBossMissing(h4:10/10),skirmishMidBossMissing(h8:10/10),dailyFactionNone)
```

Five independent assertions, all five failing, and the readout line says the whole finding in one
row: two classes, no pods, no elite, no faction.

## What shipped

**1. ROSTER DEPTH is its own axis.** `Mission.Build` and `SpawnEnemies` take
`int rosterTier = -1, bool midBossSlot = false`. `-1` means "the mission number", and every roster
read that keyed on `n` — `SelectArchetype`'s tier, `MakeMidBoss`'s tier, `podsOf3`, the grenade
gate at `n >= 2`, the BERSERKER flash / GRUNT smoke gates at `n >= 3` — now reads `rosterTier`.
Headcount, the `bump`, the opener trim, the finale gates and W9's heat arithmetic stay on `n`.
`SetupMission` passes `Mode == Skirmish ? Math.Clamp(3 + heat / 3, 3, 5) : n` — heat 0-2 deals the
campaign's mission-3 tier (the full roster), 3-5 its mission-4 tier, 6-8 its mission-5 tier, so
nothing here is a new archetype table — and `midBossSlot = Mode == Skirmish && heat >= 4` (ELITE
CADRE, the rung that also opens `Ai.Tier` 1). The campaign's `n == 3 || n == 5` is kept verbatim
(`|| midBossSlot`). DAILY is `Mode == Skirmish` with `DailyMode` set; its heat band is 0..3, so it
deals tier 3 or 4 and never the mid-boss.

A cost, declared: pods of 3 carry FUL-6's count−1 trim ("trim the initial force by 1 on 3-pod
missions"), so **a skirmish now fields ONE BODY FEWER at every rung** — MODETEST's own readout
went from `bodies h0=4 h4=6 h8=8` to `h0=3 h4=5 h8=7` — in exchange for the roster. That is the
campaign's own trade from mission 3 on; whether it is the right trade for a single fight is
unmeasured (below).

**2. A FACTION.** SKIRMISH gets an OPPOSITION row on the setup card between OBJECTIVE and HEAT —
the objective row's own chrome one size down: ghost `< >` steppers, a bordered box with the dial's
label, and a 12px caption. The dial cycles ANY / SYNDICATE / LEGION / WARDENS (`< >`, or TAB /
SHIFT+TAB — this handler runs only in `Phase.SkirmishSetup`, where TAB was unbound; in-mission TAB
still cycles units). A named faction's caption is its roster clause; ANY's says "dealt at deploy: a
mixed force, or one of the three factions". ANY resolves in `BeginSkirmish` off `Run.MapSeed`'s top
byte (`DealtFaction`: `% 4` → MIXED / SYNDICATE / LEGION / WARDENS), a pure derivation with zero
`Util.Rng` draws, so the harness's reseed-then-`BeginSkirmish` legs deal the same world they always
did. The DAILY derives its faction from the date seed like its objective, arena and heat
(`DailyFaction`: the seed's next byte `% 3`), always a NAMED faction — with heat capped at 3 the
faction is the whole of what makes one day's force differ from the next, so a daily is never the
mixed cascade. The panel grew 260 → 350 px; the blurb and the key legend name the new row.

**"ANY" is UI state, not an enum member.** `Game.SkirmishFaction` is a `Faction?` whose `null` is
ANY. `Faction` is persisted by ordinal and APPEND-ONLY; nothing was appended and SAVETEST's
fingerprint is unchanged. The `Run.FactionRosterLine` clause ("SYNDICATE: drones, shields +
screeners" …) was lifted out of `Run.EnemyHint` so the campaign fork's hover, the skirmish card's
caption and the banner say the same sentence.

**The seam, and why this one.** `Game.ModeFaction` is published in `SetupMission`:
`Combat.BeginMission(boons, Mode == Skirmish ? ModeFaction : (_run.CurrentNode?.Faction ?? None), prep)`.
The alternative — stamping `_run.CurrentNode.Faction` — was rejected because the Start node's
`None` is a generator decision and a `MissionNode` is regenerated from `MapSeed` on load (the map
generator is the save format's other half), so writing a faction onto the node would put mode
state on a campaign object and make the in-memory graph disagree with what its seed regenerates.
The ternary is one line; campaign, endless and training read the node exactly as before, and
`ResetModeState` clears `ModeFaction` at every mode entry so a skirmish's faction dies with it.
Harness: `SIGHTLINE_FACTION=syndicate|legion|wardens|mixed` pins a `SIGHTLINE_SKIRMISH` fight's
opposition (unset = ANY, like the card).

**3. LEGIBILITY.** The top bar (`SkirmishHud`) reads `SKIRMISH — ELIMINATE — LEGION` /
`DAILY 20260908 — SYNDICATE` for the whole fight, and the hostile count already wore the faction
name. The banner's SUB-line is the roster clause — on the five paint biomes. On VERDANT / TUNDRA /
MAGMA the ground rule keeps it: C4 REVIEW M4 put that sentence there because these modes have no
briefing card, and one 15px line holds one of them, not both (VERDANT's rule alone is 129
characters; the clauses are 33-40; the band is 92 px with no room for a second line, and the 44 px
main line — `DAILY 20260906 - HACK - TUNDRA - SLICK ICE` — has no shrink-to-fit). The intro's DAILY
caption names today's force (`DAILY - today's seeded run against the SYNDICATE, one attempt, ranked
by turns`, via `Game.TodayDailyForceName`, the same stamp resolution `BeginDaily` uses) — the daily
has no setup card, so the caption is the one place a player learns the opposition before committing
the day's single attempt. The SKIRMISH caption mentions the opposition.

## The gate on the shipped tree — verbatim

```
FEELTEST: walk: 48 frames, mid-path px/frame min 8.7 mean 8.7 max 8.7 (min/max 1.00), stalls<0.40xmean 0, leanKicks 1, backSteps 0; profile [1.1 3.3 5.5 7.6 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 8.7 7.6 5.5 3.3 1.1]; vault: 15 frames, peak lift 26.0px at x=476 (cover spans 448-512), landed (7,5) pos (544.0,392.0), backSteps 0; text: kill-trio min sep 42.1px, brace-pair 28.4px, twin numbers 36.0px, twins diverge True
FEELTEST: PASS (6-tile walk: mid-path speed never dips under 0.60x its max, zero stall frames, one lean kick, 48-frame cadence pinned; VAULT lifts >= 20px over the cover and lands on the tile centre; stacked floating text keeps >= 14px separation and twin numbers arc apart)
```

Four frames of push-off, forty at 8.7 px, four of brake; the cadence is still 48 frames because the
commit clock never moved. `FEELTEST` is routed through `qa-sweep.sh`'s `verdict` (the derived
counts read `exist=74 run=74` with the FUL11PROBE convention; the sweep's own footer says
`73 self-tests exist in src/; this sweep ran 73`).

## Inertness — the acceptance criteria, measured

Presentation only, so the SAME seed must play the SAME campaign to the frame:

| `SIGHTLINE_AUTOPLAY=1 SIGHTLINE_SEED=` | base `cee3cba` (throwaway worktree, Debug) | `wave/the-stride` (Debug) |
|---|---|---|
| 101 | `RESULT: WIN mission=6 frame=13803 turns=38` | `RESULT: WIN mission=6 frame=13803 turns=38` |
| 202 | `RESULT: LOSE mission=4 frame=8782 turns=21` | `RESULT: LOSE mission=4 frame=8782 turns=21` |

`SIGHTLINE_PAIRTEST=1`: `h0 slot0 legA WIN cleared=6 missions=5 turns=29 / legB same → MATCH`,
`h4 slot1 legA LOSE cleared=0 missions=1 turns=8 / legB same → MATCH`, **PASS**. `STACKTEST`,
`OWTEST`, `FIELDTEST` (whose vault leg pumps at dt = 0.05 s and still lands on (7,5)) and
`SNAPTEST` PASS on the Release binary. `bash scripts/qa-sweep.sh --full`: exit status 0, zero FAIL lines,
no COVERAGE GAP block, autoplay ×3 `LOSE m1 frame=2059 / LOSE m2 frame=2615 / LOSE m5 frame=15774`, no
TIMEOUT. Release build 0 warnings / 0 errors.

## Looked at, not just measured

`SIGHTLINE_SHOT=1 SIGHTLINE_SHOTSEQ=50 SIGHTLINE_LONGMOVE=1` filmed a walk (the first soldier's
longest clear run at that deploy was four tiles, so the strip is a four-tile walk; the six-tile
profile is FEELTEST's): the `FILM` trace reads y = `1.1 4.6 10.3 18.3 27.4 36.6 … 237.7 245.7 251.4
254.9 256.0` — 9.1 px per frame between a three-frame push-off and a three-frame brake, which is
`4/3.5 × 8` exactly, zero backward steps, and it ends on `456.000`. `SIGHTLINE_LONGMOVE=vault` is
new: it stamps HIGH COVER beside the first soldier and runs `IssueVault` through the player's
path; its trace reads lift `5.6 11.0 15.8 19.9 23.1 25.1 26.0 25.6 24.0 21.3 17.6 13.0 7.8 2.3 0.0`
over x `223 → 96`, and in the frames the figure is visibly above the cover block from frame 5 to 9
with its shadow still on the deck under it. Contact sheets (not committed): the wave's scratchpad
`walk_sheet.png` / `vault_sheet.png`, built from `sightline_seq_NN.png`. The briefing card's dark
band covers the walk strip's middle rows, as CLAUDE.md says it will below frame ~700.

## What this wave did NOT do

- **No gameplay.** `_dur` is 0.12 / 0.155 s as before, the commit is on the same frame, no
  `Util.Rng` draw was added or moved (the landing puff is `Fx.Dust` on the presentation stream and
  the landing footfall's pitch is a hash). The seed table above is the proof.
- **The lean is a push-off, not a held stride pose.** `WalkLean` decays to zero within a tile
  (`DecayUnitFx`, 7.5/s), so a six-tile walk leans for the first tile only. A sustained lean is a
  renderer/Unit change and would need FEELTEST's `leanKicks` contract restated.
- **Diagonal speed is 13% off straight speed at 60 Hz** (derived above). Fixing it is a
  sim-timing change.
- **Nobody has filmed a vault under overwatch.** The reaction fires with the figure at the apex;
  whether that reads as "shot out of the air" or as a glitch is unjudged. The film was unopposed.
- **The text ladder's width is an estimate** (0.6 em). Floating text is NotoMono through
  `Cfg.Text`, so it holds; if a display face ever carries a floating string it needs `Cfg.Measure`.
- **Kill-cam, the reaction beat, the explosion cue** — other waves. This one is the walk, the
  leap and the overprint.
- **No screenshots were sent into the thread** — this session has no file-sending tool; the paths
  are in the report and the sheets are in the scratchpad.

MODETEST skirmish h0 roster over 50 builds: BERSERKER/BRUISER/CUSTODIAN/DRONE/GRUNT/HOUND/HUNTER/LANCER/MEDIC/MORTAR/PIKEMAN/SAPPER/SCOUT/SCREENER/SHIELD/SNIPER/SPOTTER/TURRET  pods-of-3 in 50/50  bodies h0=3 h4=5 h8=7
MODETEST daily 20260701 faction=Syndicate force=b4af3e00  (must match across processes)
MODETEST: PASS (... a skirmish fields the full roster, pods of 3 and one mid-boss from heat 4; the daily has a named faction and the same stamp deals the same force)
```

Eighteen classes over fifty heat-0 builds where there were two; pods of three in 50/50; exactly one
named elite in 10/10 builds at heat 4 and at heat 8; `force=b4af3e00` printed identically by three
separate processes.

## Inertness — the campaign path, measured

The campaign must not gain or lose a single `Util.Rng` draw. By construction it does not:
`rosterTier` defaults to `n`, so every switched read sees the value it always saw; `DealtFaction`
and `DailyFaction` are hash-free bit reads of a seed; the campaign's faction expression is the same
expression. Measured anyway:

* `SIGHTLINE_PAIRTEST=1` — **PASS**.
* `SIGHTLINE_BALANCE=10 SIGHTLINE_BALANCE_BASE=0` under `xvfb-run`, Release binaries, XDG pinned
  per worktree, target JSON `rm -f`'d first, exit code 0 and `runs=20` asserted on both sides:
  the `3f3e478` binary in `/home/user/wt/modes-base` against this branch's.
  `bash docs/measurements/w1/inert_diff.sh bal-base.json bal-branch.json harness` →
  **`(empty diff — IDENTICAL)`**, and again after the legibility edits against the final binary.
  With the `harness{}` block kept, the only differing keys are `elapsedToDataReadySec`, `loadAt*`
  and `startedUtc`, which are designed to vary.
* `SIGHTLINE_SAVETEST` PASS — no enum, no generator, no draw moved.
* `bash scripts/qa-sweep.sh --full` → **`EXIT=0`**, `72 self-tests exist in src/; this sweep ran
  72`, autoplay ×3 `LOSE mission=4 / WIN mission=6 / LOSE mission=5`, no TIMEOUT. BIOMETEST passed
  on the first run (no re-run needed). Release build 0 warnings / 0 errors.
* Skirmish autoplay smoke (not a rate): `SIGHTLINE_SKIRMISH=eliminate SIGHTLINE_HEAT=6
  SIGHTLINE_AUTOPLAY=1` three times → `RESULT: LOSE mission=1` at frames 2249 / 1670 / 3013, no
  exception, no stall.

## Looked at, not just measured

* The skirmish card with the OPPOSITION row set to LEGION (`SIGHTLINE_SKIRMISHSETUP=1`), a heat-6
  LEGION skirmish at frame 760 (dormant pods still wear the `?` silhouette, so the class glyphs read
  only partly until contact) and mid-autoplay (WRAITH leapers on the squad, a hex pod still dormant),
  and two dailies at frame 45: STEEL/`20260908` with the SYNDICATE clause on the sub-line, and
  TUNDRA/`20260906` with the SLICK ICE rule there and LEGION on the top bar.

## What this wave did NOT do

1. **No difficulty claim for a skirmish or a daily.** No flywheel policy plays one, and the
   count−1 that came with pods of 3 is unpriced in win-rate terms. The `3/5/7` bodies line is a
   count, not a verdict. Price it before touching the tier map.
2. **No second banner sub-line.** On the three mechanical biomes the faction reads only on the top
   bar; growing the 92 px band is a shared-chrome change this wave did not make. The end card does
   not name the faction either.
3. **No MIXED option on the dial.** The brief asked for ANY plus the real faction names; the mixed
   cascade is a quarter of ANY's deal and `SIGHTLINE_FACTION=mixed` pins it for the harness.
4. **ENDLESS untouched** — LAST STAND already fields the full roster through `SpawnEndlessWave`.
5. **Nothing on the ladder re-measured** — the campaign is byte-identical, so there is nothing to
   re-measure; the L4 ladder of record stands.
6. **The PARALLAX docket's status cell** for P4 is the lead's to flip.
