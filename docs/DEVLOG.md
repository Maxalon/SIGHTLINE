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
headless screenshot harness stays byte-stable; always ship code that builds clean in Release
and passes the autoplay smoke test (no exceptions / no TIMEOUT).

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
