# SIGHTLINE — DEVLOG (autonomous dev-team sprints)

This log records the **process** of the autonomous, multi-agent dev team: sprint
goals, who (which agent role) did what, review/QA outcomes, and merge decisions.
It complements `CLAUDE.md` (the build/continuity contract) and `docs/DESIGN.md`
(the rationale contract). The orchestrator (tech lead) maintains this file.

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
