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
