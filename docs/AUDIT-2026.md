# SIGHTLINE — Systems Audit (PROGRAM "RECKONING")

> A foundational audit, not a feature sprint. The brief: *question everything that
> isn't basic math; be willing to flip the project on its head.* Five independent
> read-only auditors interrogated the combat math, the run/meta loop, content breadth,
> per-turn decision quality, and the architecture + verification methodology. Every
> finding below is grounded in code (file:line), not in the devlog's self-report.

## The meta-diagnosis (where all five audits converge)

SIGHTLINE is a **well-engineered tactics skeleton that eight prior autonomous
"programs" made wide and complex by piling features and patching symptoms — while
steering by a win-rate proxy that is blind to fun.** The recurring failure mode is
**accretion**: when a system misbehaved, a *new* system was bolted on top (an
anti-turtle clock, a crit-damping curve, a rotating shop slate) instead of fixing the
root. The result is a game that is *broad* in its enum counts and *thin* in its
decision space. Program #9's job is the opposite of #1–#8: **cut, re-legibilize, and
fix roots.**

Concretely, the five audits found:

1. **Combat math no human can model.** `ComputeOdds` stacks ~22 hit modifiers and ~17
   crit modifiers, the latter fed through a 5-tier diminishing-returns curve
   (`DampedCritStack`, `Combat.cs:380`). The displayed crit% is genuinely unpredictable
   to the player because each optional bonus is silently rescaled by how many *other*
   bonuses are present. This violates pillar 3 ("Reads clearly"). The crit-damping curve
   *exists only because there are too many crit perks to fit under the 100 clamp* — a
   bandaid on a bloat problem.
2. **A dominant rote line, patched three times.** The flat `+35` exposed-crit
   (`Combat.cs:234`) plus ambush plus high-ground plus crossfire makes
   "break concealment with a flanking crossfire shot from high ground" an always-optimal
   *checklist*, not a decision. Overwatch-camping was so dominant it required **three**
   escalating countermeasures (pressure clock, flank-kill refund, MOMENTUM perk). When
   you need three taxes on a strategy, the base loop rewards it.
3. **Content bloat: breadth ≠ depth.** 8 objectives (only ~3 — Hack/Sabotage/Defend —
   genuinely change *how* you play; Evac/Escort/Rescue are the same "march to the corner"
   loop, measured as 13–15-turn drags; Decapitate is a difficulty patch in an objective
   costume). ~16 enemy archetypes (~5 real identities — DRONE/SHIELD/SAPPER/SNIPER/TURRET;
   the rest are one `advW` constant apart). ~30 arenas (~10 distinct templates, you see ~5
   per run). 18 perks (13 are `+N% aim/crit` sliders; only ~3–5 are build-defining verbs).
4. **A flat per-turn gradient.** The "average turn" is a fixed priority cascade
   ("shuffle into cover, fire the best-EV shot, end") — proven by the fact that the
   *competent* autopilot is literally a 7-line greedy cascade with no lookahead and it
   clears most missions. A turn-ending aimed shot (`IssueShoot` → `ActionsLeft = 0`)
   collapses "where do I stand" into a lookup the tooltip answers. The enemy AI is the
   strongest pillar — more sophisticated than the player's decision space, which is *why*
   the player loop feels flat.
5. **A geometric-collapse run container + degenerate economy.** A 6-mission single-life
   ironman is a 6-link product: even at a generous 0.85/mission it's ~38% completion, and
   per-mission tuning can't fix the container. The Intel economy still seats BALLISTIC
   PLATING first in every shop slate, so the autopilot buys it down to zero every run —
   the "rotating slate" hid the dead economy rather than fixing it.
6. **The compass is broken (the deepest finding).** The project has **never verified the
   game is fun.** It optimizes a single bit — does an aggressive greedy bot win ~50% of
   the time without crashing — and that bot (a) is a flawless EV optimizer, not a human
   model; (b) **never throws a utility item or uses most verbs** in the `SmartStep` path
   that drives all balance data, so item/ability "balance" is unmeasured noise; (c) falls
   back to `DoHunker` — it *embodies* the turtle anti-pattern the design fights. Win-rate
   is a guardrail (no soft-locks), not a fun signal.

## Verdicts & the program

Ordered by leverage × corroboration × safety. Each wave is verified headlessly (build +
self-tests + the flywheel) and reviewed before merge.

### Wave 0 — Fix the compass (verification first)
The prerequisite. You cannot tune texture you cannot see.
- A **"sloppy" autopilot policy** (injects human-like error) alongside the existing
  greedy one; report the **optimal-vs-sloppy win-rate gap** as a *swinginess* proxy (a
  mission flawless-play wins 95% / sloppy wins 20% is unfair; both at 55–70% has slack).
- Per-turn **meaningful-choice count** (how many actions were within ~10% EV of the best)
  and **lead-swing** tracking — texture, not just outcome.
- Make the smart autopilot **actually use items/verbs** so balance stops being blind to
  half the toolkit.
- Fix the orphaned balance-JSON path (`Stats.cs:297` hardcodes a *different session's*
  scratchpad dir → the artifact is silently lost).

### Wave 1 — Combat legibility & kill the dominant line (keystone; corroborated by 2 audits)
- Cut the flat `+35` exposed-crit hard (it drives the rote expose-crit line and forces
  the damping curve).
- **Collapse the 6 overlapping conditional-crit perks** (Deadeye/Executioner/Opportunist/
  PointBlank/GiantSlayer/Vanguard) into ~2 real choices (a *finisher* vs an *opener*);
  **delete `DampedCritStack`** (no longer needed with fewer crit sources).
- **Honest tooltip:** show signed per-badge magnitudes; surface or cut the hidden
  streak-breaker (a hidden dice-loader contradicting the shown % is the worst legibility
  offender).

### Wave 2 — Run container + economy (root fixes)
- Defuse the geometric collapse: a **mid-run checkpoint** (survive one wipe) or a shorter
  run, so one bad mission isn't terminal and the (well-built) attrition system matters on
  the failure path.
- **De-seat always-first PLATING**; price survivability as a real choice.

### Wave 3 — De-bloat content (corroborated by 3 audits)
- **Merge enemy reskins** (HOUND/HUNTER/LANCER → one rusher identity; fold/scratch MORTAR).
- **Trim arenas** toward the distinct set.
- Objectives: tread carefully (cutting one touches CheckEnd/rotation/autopilot/HUD);
  prefer merging the draggiest rather than deleting wholesale.

### Deferred (informed, not yet executed)
- **Architecture:** extract a `MissionContext` owning the 5 `Combat.*` statics
  (`RunBoons/AllUnits/MissionFaction/PrepFaction/PressureAim`) → collapses ~14 scattered
  manual resets to two lifecycle points and makes "stale static bleed" structurally
  impossible. Bounded and high-value for future throughput, but lower player-facing value;
  scheduled only if a wave's risk budget allows.
- **The radical per-turn redesign** ("firing doesn't end the turn → position-after-acting
  becomes the core bet") is the highest-ceiling idea but the riskiest; recorded for a
  future, dedicated program.

*Hard rules unchanged: NO CI/Actions, ship compiling code to `main`, verify headlessly.*

---

## Results (shipped — Waves 0–2, PR #61, independently reviewed SHIP)

Measured with the **new** compass (heat 0, N=14, greedy+sloppy):

| Metric | Before-ish (post-W1) | After W2 | Note |
|--------|----------------------|----------|------|
| Run completion | 64% | **68%** | checkpoint gives a recovery surface |
| RUN OVER losses | 9 | **7** | fewer terminal wipes |
| Policy GAP (greedy−sloppy) | (noisy) | **+7 pts (healthy slack)** | human error isn't punished harshly |
| Shop purchases | PLATING **425** vs ~0 | PLATING 234 / **MAG 66 / STIMS 59 / MEDKIT 52 / FRAG 27** | the dead economy is now a real spread |
| Perk picks | crit cluster | **First Strike 18 / Executioner 13** + spread; cut perks gone | false choices removed, kept pair healthy |

The compass also *empirically confirmed* three audit hypotheses it was built to detect:
- **SHARPSHOOTER dominance** (89% hit, 120 kills vs ~76 for others) — the dominant-class finding.
- **Evac/Escort drag** (~9–10 turns/mission vs ~3 for Eliminate/Hack) — the draggy-objective finding.
- **Flat per-turn gradient** (meaningful-choices/turn ≈ 1.7–2.1, below the 3–5 "interesting decisions" band).

All three waves: build 0/0; COMBATTEST/SAVETEST/AITEST/ITEMTEST PASS; autoplay clean; honest
tooltip confirmed by screenshot; independent review = **SHIP** (no CRITICAL/HIGH/MEDIUM).

## What remains (evidence-backed roadmap for the next program)

The compass now *measures* these, so the next program can fix them and prove it:
1. **Per-turn decision flatness** (the deepest finding; meaningful-choices/turn ≈ 1.7). The
   high-ceiling fix is structural — "firing doesn't end the turn → position-after-acting
   becomes the core bet" — and deserves its own dedicated, carefully-verified program.
2. **Class dominance** — SHARPSHOOTER's Sniper crit/range edge + the squad-wide MARK
   multiplier make it the first pick; normalize toward a real composition decision.
3. **Content de-bloat** — the audit's third thrust. Evac/Escort/Rescue are one objective in
   three costumes; Decapitate ≈ Eliminate; ~5 enemy archetypes are `advW`-constant reskins;
   ~20 arenas are near-duplicates. Concentrate toward the distinct, but verify it doesn't
   just strip replay variety.
4. **Architecture** — extract a `MissionContext` owning the 5 `Combat.*` statics to kill the
   recurring stale-static-bleed bug class (bounded, high future-throughput value).

Known LOW (harness-only, cosmetic): `TrySmartItem` smoke branch computes a unit's distance
to itself (always 0) — a vacuous condition with no behavioral impact.

---

## Status: PROGRAM "TEMPO" addressed #1, #2, #4 (see CLAUDE.md WIP NOTES)

- **#1 Per-turn decision flatness — FIXED (keystone).** "Firing no longer ends the turn": a shot
  is 1 action, so position-after-acting (shoot→reposition) is the new core bet; a 2nd shot is a
  rushed follow-up (preserves the load-bearing ~2-shots/turn ceiling). MEASURED: meaningful-choices/
  turn **1.76 → 6.15** at IDENTICAL run-completion (67.9 → 68.8%), holds at heat-4 (7.43 / 50%).
- **#2 Class dominance — PARTIAL.** Sniper crit/dmg/range trimmed, MARK crit amp removed, Ranger
  given a close-range niche. The crit-trim hypothesis here proved INSUFFICIENT — Sharpshooter's
  lead is damage+aim driven, so it remains the top single-target dealer (a sniper, thematically)
  while the other three classes are tightly grouped with clear niches. Finishing it (a measured
  damage/aim pass) is the open follow-up.
- **#3 Content de-bloat — DEFERRED (deliberate).** Removing enemies/arenas risks stripping replay
  variety for marginal clarity; recommend doing it as a VALUE-ADD instead (shorten the Evac/Escort
  ~10-turn drag via a closer win tolerance), not a removal.
- **#4 Architecture — DONE.** `Combat.BeginMission/EndMission/EndRun` own the 5 mission statics;
  the ~14 scattered defensive resets are gone, stale-static bleed is structurally impossible.
