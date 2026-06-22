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

## Sprint 1 — (in planning)

**Kickoff:** dispatched the PM/Research agent (prioritized backlog) and the Architect agent
(Phase 4.4 concealment + ambush design) in parallel. Sprint scope is set once both report;
items will be batched so parallel devs touch mostly disjoint files.

_Log entries are appended below as work lands._
