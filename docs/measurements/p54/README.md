# P54 — WHAT ACTUALLY ENDS A CAMPAIGN AT THE APEX

**No new compute.** This reads `campaigns[]` out of the archive that already existed. Every balance
chunk since THE HEAT PIN carries one record per campaign with `lossCause`, `endMission` and
`endObjective`, and **nothing in this project had ever cross-tabbed them by rung.**

Script: `apex_losses.py`. Sources: **l6, l7, p23, p24** — the UNFORCED ladder rounds. Chunks with
`levers.siteGlyphs` true are skipped, because P49-P53's forced content arms play one arena and one
objective and are not the shipped distribution.

## Why it was run

The h8 rung has declined to respond to three structurally unrelated levers:

    P24      hostile accuracy (-5 aim, one funnel)   -0.6 / +0.3 / -0.2, the round's tightest MDE
    L7/P23   finale bodies and stats                 a real defect, fixed, bought -1.9
    P53      room geometry                           -2.5 (ns) on SABOTAGE, and 1-of-1 only at HACK

Three levers, three non-responses. The roadmap made "at h8, what actually ends a campaign?" the top
item precisely because it is answerable for free, and three waves had been spent guessing.

## The answer, replicated on four independent rounds

    round    h0 npc-death %   h4      h8        h8 losses ending at the FINALE
    l6            0.4%       1.5%   26.4%              14.7%
    l7            0.0%       2.1%   26.4%              16.5%
    p23           0.0%       2.2%   27.1%              15.8%
    p24           0.9%       2.9%   27.0%              16.1%

**At heat 8, 26.8% of campaign losses are the PROTECTED NPC DYING — not a squad wipe. At heat 0 it
is 0.38%.** Pooled over 5,365 h8 losses:

    RUN OVER            877   72.1%     (the squad fell)
    CAPTIVE LOST        202   16.6%  ┐
    VIP LOST            125   10.3%  ├ 26.8% — the asset the mission is ABOUT
    CAPTIVE ABANDONED     2    0.2%  ┘
    STALEMATE-MISSION    11    0.9%

    NPC deaths by objective:  Rescue 63%   Escort 37%
    NPC deaths by mission:    m2 8%   m3 38%   m4 31%   m5 23%   m6 0%
    WIPES     by mission:     m1 9%   m2 16%  m3 26%   m4 15%   m5 12%   m6 22%

## THE FAILURE MODE CHANGES QUALITATIVELY WITH HEAT, AND EVERY LEVER WAS AIMED AT THE OTHER ONE

Two facts in that table explain all three non-responses:

**1. The finale is where the FEWEST campaigns end at h8** — 16%, down from ~30% at h4 — and it
carries **0%** of the NPC deaths (the finale is never a Rescue or an Escort). So L7/P23's finale
lever could reach at most a sixth of the apex's losses and none of the bucket that grew.

**2. A quarter of the apex's losses are one fragile unit being focused down**, which is not a
shot-exchange problem. P24 took five points off every hostile's aim; that is a small correction to
the 72% and close to nothing for an asset taking concentrated fire over several turns.

**The apex is not unresponsive. Three waves measured the 73% and the thing that grew from 0.4% to
27% was never measured at all.**

## THE CAUSE IS ONE FUNCTION, AND IT HAS NO HEAT TERM

`Mission.MakeVip` — the single funnel for BOTH protected assets (`Game.SetupMission` builds the
RESCUE captive from it too: `Vip = Mission.MakeVip(n); Vip.Name = "CAPTIVE";`):

    int depth = Math.Max(1, DepthFor(missionNum));
    int hp = 14 + 2 * depth;            // m2~18, m4~22, m6~26
    u.Armor = depth / 2;                // m2~1,  m4~2,  m6~3

**It is a pure function of mission depth. Heat does not appear in it.** Meanwhile heat 8 gives the
force around that asset roughly **+4 bodies, +4 stat (a force-wide +1 HP and +1 Aim per rung), +1
weapon damage from mission 3, and AI tier 2.** The asset the objective is about is the one thing on
the board whose survivability is constant in heat.

## AND THE SAME DEFECT WAS ALREADY FOUND ONCE, ON THE OTHER AXIS

`MakeVip`'s own comment records P14 fixing this exact shape for the MODES:

> `* MakeVip` — the escort asset **pinned at 16 HP / 0 armor on every rung**

P14 noticed that SKIRMISH and DAILY passed a literal `1` for mission depth, so the asset never grew
across heat rungs **in those modes**, and threaded `ModeDepth` to fix it. **The campaign has the
identical structure against HEAT, in the same function, and it was not noticed** — because the
campaign's `DepthFor(n) == n` made the depth axis look handled.

Two axes exist (depth, heat); one is wired.

## ITEM 2, RUN IMMEDIATELY: the per-objective win rates, and they are a CLIFF

P54's first half reports LOSS-CAUSE shares, which is not a win rate — W8's rule. `objective_rungs.py`
recomputes the thing actually at stake from `byObjectiveByMission`, n-weighted, same four rounds:

    objective          h0              h4              h8       h4 -> h8
    Rescue        97.9%           96.2%           40.0%          -56.2
    Escort        95.3%           91.5%           42.8%          -48.7
    Sabotage      75.0%           66.7%           43.6%          -23.1
    Evac          90.8%           83.6%           64.0%          -19.6
    Decapitate    55.8%           35.8%           18.4%          -17.4
    Hack          73.6%           64.6%           52.0%          -12.6
    Eliminate     83.1%           78.6%           76.4%           -2.2
    Defend        70.3%           59.9%           71.1%          +11.2

MID-RUN ONLY (m3-m5), where the NPC deaths land: **Rescue 97.3 -> 96.2 -> 30.4; Escort 95.6 -> 91.3
-> 36.2.**

**The two objectives that go through `Mission.MakeVip` are the two flattest on the ladder and then
fall off a cliff.** Every other objective degrades gradually. Rescue and Escort are essentially
FREE for two-thirds of the ladder — 96-98% is not an objective — and then a wall.

That is the signature of an asset whose survivability is constant in heat: nothing threatens it
until the force is big enough to delete it through the screen, and then everything does.

**It also reproduces W8's artifact as a control on the method**: `Eliminate` reads 83.1 / 78.6 /
76.4 pooled and **44.8 / 21.7 / 6.0** on mid-run cells alone. The pooled row is mission-1s. Never
quote the pooled row.

**And `Defend` is NON-MONOTONE** (70.3 / 59.9 / 71.1 — h8 above h4, on n>3,000 per cell). That is
its own anomaly, unexplained, and it is not this wave's.

## ⚠ THE OBVIOUS LEVER FIXES ONE END AND MUST NOT BE SOLD AS FIXING BOTH

The naive reading — "give the asset a heat term" — addresses the cliff and does **nothing** about
the 96-98%. Worse, it has to be checked that it does not make the low rungs *more* trivial. The
principled dose (`Heat.StatDelta` for HP, `Heat.DmgDelta` for armor, both clamped at 0) happens to
be nearly harmless there — `StatDelta` is **0 at h0 and +1 at h4**, and +1 HP against a 96.2% win
rate is not a change — but that is a property to VERIFY in the round, not to assume.

**These are two defects, not one.** The asset does not scale with heat (the cliff), and the
objective is uncontested at low heat (the 96-98%). A heat term is a candidate for the first only,
and a round that ships it must report the low rungs as data rather than waving at them.

## ITEM 4: THE AUDIT OF THE OTHER DEPTH-ONLY CONSUMERS — one of four, and it is this one

`MakeVip` was found because a loss cross-tab pointed at it, which is not a method. P14's own comment
lists FOUR consumers of "how deep is this fight" and fixed them all on the MODE axis; **the HEAT axis
had never been checked for any of them.** Checked now, by reading each call site rather than the
comment above it:

| consumer | depth | heat | verdict |
|---|---|---|---|
| `Mission.OpenerTrim` | `DepthFor(n)` | reaches it via the count it trims FROM | **fine** — it is a mission-1/2 grace that SUBTRACTS bodies; X2 gave the base force its ramp separately |
| `Mission.MakeVip` | `14 + 2*depth`, `armor depth/2` | **NOWHERE** | **THE DEFECT** |
| `Combat.HvtHpBonus` | `6 + 1*depth` | reaches the BODY via `Mission.MakeHostile`'s StatDelta | **defensible** — only the "this one is the target" premium is depth-scaled, and W8 made all three constants pinnable for exactly this question |
| `Game.SpawnReinforcements` / `SpawnDefendWave` | `DepthFor(_run.Mission)` | reads `Heat.StatDelta(_run.HeatLevel)` explicitly | **wired** |

**One of four is genuinely unwired, and it is the one the loss cross-tab found.** That closes the
hunt rather than opening it — a future session does not need to re-audit this list.

### And the ORDERING is why it was easy to miss

In `Game.SetupMission` the asset is built at the Escort/Rescue branches, and

    int heat = _run.HeatLevel;

is read **sixteen lines later**, under the comment *"Heat folds into the SAME difficulty params the
deployment cards use."* The heat the asset should answer is not even in scope where the asset is
constructed. That is P48's shape one level in — **a value read after the thing it should govern has
already been built is a value that does not govern it** — and it is the second time this session
that ordering, not logic, carried the defect.

## What this does NOT establish

- **It is not a priced lever.** Nothing here says how much a heat term on the asset would buy, or
  that it is the right correction. It says where the losses are.
- **The 73% is still the majority** at h8 and is not explained by this.
- **`byObjective` pooled rows remain dangerous** (W8's rule): these are LOSS-CAUSE shares, not
  per-objective win rates, and the two must not be conflated.
- Rescue and Escort win rates themselves were not recomputed here; the next round should.

## Reproducing

    python3 docs/measurements/p54/apex_losses.py            # rungs 0 4 8
    python3 docs/measurements/p54/apex_losses.py 0 2 4 6 8
    python3 docs/measurements/p54/objective_rungs.py       # the per-objective cliff
