using System;
using System.Collections.Generic;
using System.Numerics;

namespace Sightline;

public class EnemyPlan
{
    public List<(int x, int y)> Path = new();
    public int MoveActions;       // 0,1,2
    public Unit ShootTarget;      // null if no shot planned
    public bool Overwatch;
    public bool Hunker;
    public bool Grenade;          // lob a grenade instead of shooting
    public int GrenX, GrenY;      // grenade aim tile
    public Unit HealTarget;       // medic: mend this wounded ally instead of fighting
    public (int x, int y)? SapTile; // sapper: demolish this player cover tile instead of shooting
    public bool UseItem;          // use a utility item (smoke/flash) this turn
    public Unit ShoveTarget;      // rusher/Legion: shove this soldier OUT of cover to expose it (Wave 5)
    public int ItemTx, ItemTy;    // item aim tile
    public (int x, int y)? SiegeCharge; // BOMBARD: charge a telegraphed strike centered here (else null)
    public (int x, int y)? RelockTile;  // CUSTODIAN (W8): re-lock/re-arm the objective at this site (else null)
}

/// Tactical decision-making for a single enemy. Greedy, but reads as competent:
/// seek cover + line of fire, prefer flanking/finishing, advance when blind.
public static class Ai
{
    public const int HealRange = 4;    // tiles a medic can mend across
    public const int HealAmount = 4;   // HP restored per heal

    // W6b — COORDINATION TIER (0..2): the apex of the Heat ladder scales by PLAYING BETTER,
    // not just by piling stats onto the saturating StatDelta/88-aim clamp. Published
    // UNCONDITIONALLY by Game.SetupMission every mission (from the Heat rows' data-only
    // AiTier field: 0 below EXPOSED, 1 at rungs 6-7, 2 at NO QUARTER) and raised by the
    // LAST STAND wave path as a stand deepens; CLEARED by the Combat.EndMission mirror so a
    // NO QUARTER run's tier can never leak into a subsequent heat-0 SKIRMISH/DAILY.
    // SAFETY INVARIANT: Tier 0 == today's constants EXACTLY (every tiered read below
    // collapses to its pre-W6b value), and no SIGHTLINE_*TEST path sets it, so the harness
    // and default screenshots stay byte-stable by construction.
    public static int Tier = 0;

    // W6b — tiered item-roll damper: the small random damper on smoke/flash use shrinks as
    // the tier rises (a coordinated force screens/blinds more RELIABLY), but the rise is
    // CAPPED at 75 — never certainty — per the "fires often-but-not-always, so it stays a
    // threat not a tic" rationale at the smoke reasons below. A base chance already at/above
    // the cap does not rise at all: those (endsWatched 90, SCREENER 88, flash 80) are
    // strong-reason/identity rolls, not difficulty knobs. Tier 0 returns the base unchanged.
    // Internal so the AITEST harness can pin the tier-0 identity + tier-2 cap directly.
    internal static int Damp(int baseChance)
        => baseChance >= 75 ? baseChance : Math.Min(baseChance + 10 * Tier, 75);

    public static EnemyPlan Plan(Game g, Unit e)
    {
        var plan = new EnemyPlan();
        var players = g.AlivePlayers();
        if (players.Count == 0) return plan;

        // movement reachability (other units block)
        Func<int, int, bool> blocked = (x, y) => g.IsOccupiedByOther(x, y, e);
        var cost = g.Grid.CostMap(e.X, e.Y, blocked, out var cameFrom, e.MoveBudget * 2);

        // gather reachable tiles incl. current position
        var reach = new List<(int x, int y, int c)>();
        reach.Add((e.X, e.Y, 0));
        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
                if (cost[x, y] > 0) reach.Add((x, y, cost[x, y]));

        (int x, int y) bestTile = (e.X, e.Y);
        int bestCost = 0;
        float bestScore = float.NegativeInfinity;
        Unit bestShotTarget = null;

        Unit nearest = null; int nd = int.MaxValue;
        foreach (var p in players)
        {
            int d = Util.ChebyDist(e.X, e.Y, p.X, p.Y);
            if (d < nd) { nd = d; nearest = p; }
        }
        Unit vip = players.Find(p => p.IsVip);   // escort: hunt the asset

        // HOUND (swarmer): hunt the most ISOLATED soldier — the squad member with the FEWEST other
        // soldiers within 2 tiles (ties broken by proximity to this hound). A lone soldier away from
        // the pack is the prey; the squad's counter is to stay massed so no one is the obvious mark.
        Unit prey = null;
        if (e.Cls == "HOUND")
        {
            int bestIso = int.MaxValue; int bestPd = int.MaxValue;
            foreach (var p in players)
            {
                if (p.IsVip) continue;                          // hounds chase soldiers, not the asset
                int near = 0;
                foreach (var q in players)
                    if (q != p && !q.IsVip && Util.ChebyDist(p.X, p.Y, q.X, q.Y) <= 2) near++;
                int pd = Util.ChebyDist(e.X, e.Y, p.X, p.Y);
                if (near < bestIso || (near == bestIso && pd < bestPd)) { bestIso = near; bestPd = pd; prey = p; }
            }
        }

        // SPOTTER force-multiplier (3.x): a live BEACON on the field "paints" the squad's
        // priority target, so every ally's focus-fire convergence is amplified below. Computed
        // once per plan; kill the SPOTTER to break the crossfire (it doesn't fight much itself).
        bool spotterActive = SpotterActive(g, e);

        // SAPPER: the nearest covered soldier's frontal cover tile — the demolition target
        (int x, int y)? sapTarget = null;
        if (e.Cls == "SAPPER" && nearest != null) sapTarget = g.Grid.CoverTile(nearest.X, nearest.Y, e.X, e.Y);

        // MEDIC: prefer patching up the most-wounded active ally (incl. itself) over
        // fighting. Move to a covered tile within heal range + LoS of the patient. If
        // no patient or no reachable heal spot, fall through to normal combat AI.
        if (e.Cls == "MEDIC")
        {
            Unit patient = null; int worst = 0;
            foreach (var a in g.AliveEnemies())
                if (a.Active && a.Hp < a.MaxHp) { int miss = a.MaxHp - a.Hp; if (miss > worst) { worst = miss; patient = a; } }
            if (patient != null)
            {
                (int x, int y) ht = (-1, -1); int htCost = 0; float htScore = float.NegativeInfinity;
                foreach (var (tx, ty, c) in reach)
                {
                    int acts = c <= e.MoveBudget ? (c == 0 ? 0 : 1) : 2;
                    if (acts >= 2) continue;                                       // keep an action to heal
                    if (Util.TileDist(tx, ty, patient.X, patient.Y) > HealRange) continue;
                    if (!g.Grid.HasLineOfSight(tx, ty, patient.X, patient.Y)) continue;
                    var cov = g.Grid.GetCover(tx, ty, nearest.X, nearest.Y);
                    float s = cov.Level * 18 + g.Grid.HeightAt(tx, ty) * 6 - acts * 6
                              + Util.ChebyDist(tx, ty, nearest.X, nearest.Y) * 0.6f  // hang back from the front
                              + Util.RandRange(0f, 3f);
                    if (s > htScore) { htScore = s; ht = (tx, ty); htCost = c; }
                }
                if (ht.x >= 0)
                {
                    var hp = new EnemyPlan { HealTarget = patient };
                    if (ht != (e.X, e.Y))
                    {
                        hp.Path = g.Grid.ReconstructPath(cameFrom, e.X, e.Y, ht.x, ht.y);
                        hp.MoveActions = htCost <= e.MoveBudget ? 1 : 2;
                    }
                    return hp;
                }
            }
        }

        // CUSTODIAN (W8): the objective KEEPER — a dedicated archetype path on the MEDIC pattern.
        // When the player has objective progress to undo (a partially-hacked terminal / a blown
        // sabotage charge), it walks to the site and re-locks/re-arms ONE step per adjacent turn
        // (executed by Game.DoRelock, telegraphed with a banner line). Priorities:
        //   (1) already adjacent -> spend the turn working the site;
        //   (2) a reachable site-adjacent tile with an action to spare -> move there + work it;
        //   (3) too far -> dash toward the site;
        //   (4) nothing to undo (or boxed out) -> fall through to the normal combat loop, so the
        //       turn always spends an action (same no-dead-turn/no-TIMEOUT safety as MEDIC/MORTAR).
        if (e.Cls == "CUSTODIAN")
        {
            (int x, int y)? site = null;
            if (g.HasTerminal && g.HackProgress > 0 && g.HackProgress < Game.HackRequired)
                site = g.Terminal;
            else if (g.HasSabotage)
            {
                float bd = float.MaxValue;                        // nearest BLOWN charge (re-armable)
                for (int i = 0; i < g.SabotageSites.Count; i++)
                    if (g.SabotageBlown.Contains(i))
                    {
                        float dd = Util.ChebyDist(e.X, e.Y, g.SabotageSites[i].x, g.SabotageSites[i].y);
                        if (dd < bd) { bd = dd; site = g.SabotageSites[i]; }
                    }
            }
            if (site != null)
            {
                var (sx, sy) = site.Value;
                if (Util.ChebyDist(e.X, e.Y, sx, sy) <= 1)
                    return new EnemyPlan { RelockTile = site };   // at the site — work it
                // best reachable tile ADJACENT to the site, keeping an action to work it
                (int x, int y) rt = (-1, -1); int rtCost = 0; float rtScore = float.NegativeInfinity;
                foreach (var (tx, ty, c) in reach)
                {
                    int acts = c <= e.MoveBudget ? (c == 0 ? 0 : 1) : 2;
                    if (acts >= 2) continue;                      // keep an action to re-lock
                    if (Util.ChebyDist(tx, ty, sx, sy) > 1) continue;
                    var cov = g.Grid.GetCover(tx, ty, nearest.X, nearest.Y);
                    float s = cov.Level * 12 - acts * 4 + Util.RandRange(0f, 2f);
                    if (s > rtScore) { rtScore = s; rt = (tx, ty); rtCost = c; }
                }
                if (rt.x >= 0)
                {
                    var rp = new EnemyPlan { RelockTile = site };
                    rp.Path = g.Grid.ReconstructPath(cameFrom, e.X, e.Y, rt.x, rt.y);
                    rp.MoveActions = rtCost <= e.MoveBudget ? 1 : 2;
                    return rp;
                }
                // adjacency out of reach this turn — DASH toward the site (closest reachable tile)
                (int x, int y) dt = (-1, -1); int dtCost = 0; float dtScore = float.NegativeInfinity;
                foreach (var (tx, ty, c) in reach)
                {
                    float s = -Util.ChebyDist(tx, ty, sx, sy) * 3f + Util.RandRange(0f, 1.5f);
                    if (s > dtScore) { dtScore = s; dt = (tx, ty); dtCost = c; }
                }
                if (dt.x >= 0 && dt != (e.X, e.Y))
                {
                    var rp = new EnemyPlan();
                    rp.Path = g.Grid.ReconstructPath(cameFrom, e.X, e.Y, dt.x, dt.y);
                    rp.MoveActions = dtCost <= e.MoveBudget ? 1 : 2;
                    return rp;
                }
                // else: boxed in — fall through to the generic loop (shoot/hunker; never a dead turn)
            }
        }

        // BOMBARD (SIEGE artillery): a dedicated archetype path, like MEDIC. It does NOT fire — on its
        // turn it CHARGES a telegraphed 3x3 strike (resolved next enemy turn by Game.TickSiegeStrikes).
        //   (1) If it's ALREADY charging (ChargeTurns>0): a shell is in flight; don't stack a second
        //       one. Fall through to the generic tile loop so the frail piece ducks to cover / holds.
        //   (2) Else pick the strike center that catches the most soldiers in a 3x3 (BestSiege).
        //       Indirect fire -> NO LoS requirement (it can shell a soldier behind high cover, forcing
        //       MOVEMENT, not just an LoS-break). Charge from the CURRENT tile (v1: stand and shell).
        //   (3) If nothing's worth shelling, fall through to the normal loop (move/shoot SMG/hunker) so
        //       the turn always spends an action -> NO dead turn / NO TIMEOUT (same safety as MORTAR).
        // (SIGNAL W5: keyed on the HasSiege capability flag — defaults to Cls=="BOMBARD", so
        // rank-and-file artillery is unchanged; a siege-armed BOSS elite runs this path too and
        // falls through to the full ELITE combat loop when nothing is worth shelling.)
        if (e.HasSiege && e.ChargeTurns == 0)
        {
            var (bx, by, hits) = BestSiege(g, e);
            // A siege-armed BOSS (an ELITE carrying the flag) only shells a genuine CLUSTER (2+
            // soldiers): unlike the 7-HP rank-and-file BOMBARD — whose fairness is that it dies to
            // one focused turn — a 20-HP guarded boss raining a no-LoS shell EVERY turn taxed
            // position relentlessly (measured: the first-cut Legion finale sank to a 37%
            // conditional). The cluster gate makes SPREAD OUT the counter-verb, and on non-shelling
            // turns the boss fights its real ELITE turn (move/Lmg/frag) instead of standing
            // statically at the board edge. Rank-and-file keeps its hits>=1 gate exactly.
            int need = e.Cls == "BOMBARD" ? 1 : 2;
            if (hits >= need)
            {
                var sp = new EnemyPlan { SiegeCharge = (bx, by) };   // no move, no shot — the charge is the action
                return sp;
            }
            // else: fall through to the generic combat loop (advance / fallback shot / hunker).
        }

        // COORDINATION 2 — SELF-PRESERVATION / FIGHTING RETREAT (decision):
        // a hurt, non-suicidal enemy that can't get a worthwhile shot this turn prefers to
        // fall back into cover / out of line-of-sight rather than trade into death. BERSERKER
        // and ELITE never break off (their whole identity is pressing the attack); DRONE/SAPPER
        // are mission-committed too. We *decide* retreat here by scanning whether ANY reachable
        // tile offers a decent shot; if none does and the unit is low, the tile loop below flips
        // its advance term into a fall-back term. This stays progress-safe: it's a bias, the unit
        // still spends its action (a safe tile that happens to have a shot still shoots, and the
        // standard overwatch/hunker fallback still fires), so it re-engages the moment it can.
        bool canRetreat = e.Cls != "BERSERKER" && e.Cls != "ELITE"
                       && e.Cls != "DRONE" && e.Cls != "SAPPER" && e.Cls != "TURRET"
                       && e.Cls != "HOUND"      // a swarmer commits — it never breaks off (its identity is the rush)
                       && e.Cls != "STRIKER";   // a leaper commits to the flank too (fragile, but never disengages)
        bool lowHp = e.Hp <= Math.Max(1, e.MaxHp * 3 / 10);   // <= ~30% MaxHp
        // UNDERTOW W3 — a ROUTED unit (its pod's morale broke, Game.BreakPodMorale) flees the fall-back way
        // REGARDLESS of archetype or HP: even a berserker breaks when its pod cascades. The archetype
        // "never retreats" exemption is overridden by an actual rout — that's the whole point of morale.
        bool routing = e.Routed > 0;
        bool retreatMode = routing;
        if (!retreatMode && canRetreat && lowHp)
        {
            float bestReachHit = -1f;
            foreach (var (tx, ty, c) in reach)
            {
                if (c > e.MoveBudget) continue;                 // must keep an action to fire
                foreach (var p in players)
                {
                    if (Util.TileDist(tx, ty, p.X, p.Y) > e.Weapon.MaxRange) continue;
                    // W6a truthfulness: mirror Game.CanTarget — a commanding (>=2-tier) height
                    // advantage sees over high cover, so a reachable plateau's REAL shot counts
                    // here and a unit that could climb-and-fire doesn't wrongly break off.
                    bool cmdR = g.Grid.HeightAt(tx, ty) - g.Grid.HeightAt(p.X, p.Y) >= 2;
                    if (!g.Grid.HasLineOfSight(tx, ty, p.X, p.Y, cmdR)) continue;
                    int h = OddsFrom(g, e, tx, ty, p).HitChance;
                    if (h > bestReachHit) bestReachHit = h;
                }
            }
            // only break off when no reachable tile yields a meaningful shot (<55% best);
            // if it can still hit hard it stands and fights (a trade may be worth it).
            // W6b deliberately does NOT tier this 55: W6a's commanding retreat scan above
            // already trims false break-offs (plateau shots now count), and UNDERTOW W3 rout
            // adds its own break-off pressure — raising the threshold with the tier would
            // stack all three toward apex passivity. Revisit only on flywheel retreat data.
            retreatMode = bestReachHit < 55f;
        }

        // tiles a player overwatch currently covers (computed once per turn by the squad
        // coordinator) — used by COORDINATION 3 below to route around the kill zone.
        var owTiles = g.PlayerOverwatchTiles;

        // active allies, gathered ONCE for the per-tile anti-cluster term (review #4: avoid
        // re-allocating g.AliveEnemies() inside the reachable-tile loop).
        var activeAllies = new List<Unit>();
        foreach (var a in g.AliveEnemies()) if (a != e && a.Active) activeAllies.Add(a);

        foreach (var (tx, ty, c) in reach)
        {
            int actionsToReach = c <= e.MoveBudget ? (c == 0 ? 0 : 1) : 2;

            // best shootable target from this tile (must keep an action to fire)
            Unit shoot = null;
            float bestHit = -1f;
            if (actionsToReach <= 1)
            {
                foreach (var p in players)
                {
                    if (Util.TileDist(tx, ty, p.X, p.Y) > e.Weapon.MaxRange) continue;
                    // W6a truthfulness: mirror Game.CanTarget — a commanding (>=2-tier) height
                    // advantage sees over high cover. Without this the planner filtered out the
                    // exact shots the resolver would allow from the authored '=' tier-2 plateaus,
                    // so snipers/elites never sought them; ComputeOdds' seesOver already prices
                    // the payoff (cover fully negated), the shot just has to survive this filter.
                    bool cmd = g.Grid.HeightAt(tx, ty) - g.Grid.HeightAt(p.X, p.Y) >= 2;
                    if (!g.Grid.HasLineOfSight(tx, ty, p.X, p.Y, cmd)) continue;
                    var odds = OddsFrom(g, e, tx, ty, p);
                    bool canFinish = p.Hp <= e.Weapon.DmgMax;
                    // The VIP gets a much smaller "finish it" frenzy than a soldier: balance data
                    // showed Escort gated (~52%) because once the fragile asset was chipped into the
                    // killable band, the WHOLE force piled on (+30 each) and deleted it in a turn.
                    // A milder VIP finish bonus + the VIP's HP/Armor scaling (Mission.MakeVip) keeps
                    // it a priority without an unstoppable execution swarm; soldier focus is unchanged.
                    float finishVal = canFinish ? (p.IsVip ? 12 : 30) : 0;
                    float val = odds.HitChance + (odds.CoverLevel == 0 ? 25 : 0)
                                + finishVal
                                + (p.IsVip ? 10 : 0);                    // prioritise the VIP (was 40)
                    // VIP bias dialed 40 -> 10: balance data (Escort 34% win, many "VIP LOST")
                    // showed the +40 made the whole hostile force focus-fire the fragile asset and
                    // delete it in 1-2 turns. 10 keeps it a mild tiebreaker priority (a hostile
                    // already looking at the VIP shoots it over an equally-good soldier shot)
                    // without the whole pod converging on it. Measured: Escort 34% -> 65% at heat 0
                    // with this + the VIP HP buff in Mission.MakeVip + the halved closing-bias below.
                    // COORDINATION 1 — FOCUS FIRE: the squad converges on a shared priority
                    // target (chosen once per turn in Game.PlanEnemySquad). Reward shooting it
                    // so enemies collapse one soldier rather than spreading chip damage; the
                    // bonus is larger when this shot would be a likely killing blow (high hit %
                    // AND lethal damage), so the squad actually closes the kill. Advisory: it
                    // layers on top of hit/cover/finish, never replacing the "good shot" core.
                    if (g.EnemyFocus != null && p == g.EnemyFocus)
                    {
                        // SPOTTER amplifies the convergence: a painted target is worth collapsing
                        // on even harder, so the squad genuinely focuses while the BEACON lives.
                        // W6b: the coordination tier sharpens the squad's convergence — the focus
                        // bias climbs 30 -> 35 -> 40 across tiers (the SPOTTER's painted 45 is an
                        // archetype force-multiplier, not a difficulty knob, so it stays fixed).
                        val += spotterActive ? 45 : 30 + 5 * Tier;       // concentrate fire here
                        if (canFinish && odds.HitChance >= 50) val += 35; // press a likely kill
                        // COORDINATION 6 — CROSSFIRE (AI improvement): prefer hitting the focus
                        // from an angle its cover DOESN'T protect (a genuine flank) or where it's
                        // simply exposed, so the squad attacks the priority target from converging,
                        // unprotected lines rather than all battering its frontal cover. Read off the
                        // SAME GetCover the resolver uses (truthful), and only when this shot already
                        // exists, so it's a pure tie-break among focus shots — never a no-progress move.
                        if (odds.Flanked) val += spotterActive ? 22 : 14; // out-positioned its cover
                        else if (odds.CoverLevel == 0) val += 4;          // already exposed: minor nudge
                    }

                    bool isFocus = g.EnemyFocus != null && p == g.EnemyFocus;

                    // CROSSFIRE SEEKING (AI improvement 1): reward ending on a tile that puts THIS
                    // soldier in a pincer with another living enemy that already has line-of-sight
                    // to it from a meaningfully DIFFERENT angle. The new CROSSFIRE combat mechanic is
                    // symmetric (a target shot by 2+ same-team attackers from diverging vectors —
                    // > ~72deg — takes +aim/+crit), so the squad benefits from collapsing on a
                    // soldier from converging lines instead of stacking one approach. CrossfireWith
                    // is pinned term-by-term to Combat.InCrossfire (W6a) so the prediction is truthful.
                    // Advisory: it layers onto the existing hit/cover/finish/focus core, only when a
                    // shot already exists, so it biases POSITIONING and never forces a worse shot.
                    if (CrossfireWith(g, e, tx, ty, p))
                    {
                        // moderate, in the band of a cover/flank term (cover.Level*18, flank 14/-25),
                        // amplified for the painted FOCUS so the squad genuinely pincers the BEACON's
                        // mark; a NON-focus crossfire is a smaller nudge so it never out-votes the
                        // squad's deliberate focus choice. Never large enough to override "can I
                        // shoot at all / am I safe". W6b: the focus-crossfire pull climbs
                        // 16 -> 19 -> 22 with the coordination tier (a tier-2 force genuinely
                        // pincers); the SPOTTER 22 and non-focus 8 stay fixed.
                        val += isFocus ? (spotterActive ? 22f : 16f + 3f * Tier) : 8f;
                    }

                    // TARGET SHARPENING (AI improvement 3): among shootable soldiers prefer, in order,
                    // (a) a likely KILL this turn (lethal EV: in the finish band AND a real chance to
                    // connect), then (b) the squad's focus (the big +val above), then (c) the lowest
                    // effective HP / most exposed. These are small tie-breakers folded onto the
                    // hit-based core. CRUCIALLY they DEFER to focus: the squad's focus is its
                    // coordinated decision, so a non-focus target's sharpeners stay modest and never
                    // out-vote an in-range focus (autoplay + AITEST both rely on focus driving choice
                    // when a focus exists). When NO focus is set, they cleanly sharpen the pick.
                    if (isFocus || g.EnemyFocus == null)
                    {
                        if (p.Hp <= e.Weapon.DmgMax && odds.HitChance >= 50) val += 18;   // (a) close the kill
                        val += Util.Clamp((12 - p.Hp) * 0.6f, 0f, 7f);                    // (c) softer target first
                        if (odds.CoverLevel == 0 && !isFocus) val += 3;                   // (c) exposed nudge
                    }
                    else
                    {
                        // a focus exists but this isn't it: only a tiny softer-target tie-break,
                        // capped well under the focus margin, so focus discipline holds.
                        val += Util.Clamp((12 - p.Hp) * 0.25f, 0f, 3f);
                    }

                    if (val > bestHit) { bestHit = val; shoot = p; }
                }
            }

            // cover quality at this tile vs the nearest player
            var cover = g.Grid.GetCover(tx, ty, nearest.X, nearest.Y);
            int distNearest = Util.ChebyDist(tx, ty, nearest.X, nearest.Y);
            float score = 0;
            // UNDERTOW W3: a ROUTING unit is panicking — a shot is a minor opportunistic bonus, NOT "king",
            // so the flee/distance terms below dominate and it actually breaks contact (it may still take a
            // wild potshot if one lines up). A steady unit values having a shot above all else.
            if (shoot != null) score += routing ? bestHit * 0.25f : 100 + bestHit;
            score += cover.Level * 18;                           // value cover
            score += g.Grid.HeightAt(tx, ty) * 14;               // seize the high ground
            if (cover.Flanked) score -= 25;
            score -= actionsToReach * 6;                         // prefer cheaper moves slightly
            if (g.Grid.IsFire(tx, ty)) score -= 60;              // never voluntarily stand in fire (hazards)
            // wariness of an explosive barrel the squad could shoot to catch it in the blast
            for (int bdx = -1; bdx <= 1; bdx++)
                for (int bdy = -1; bdy <= 1; bdy++)
                    if (g.Grid.IsBarrel(tx + bdx, ty + bdy)) { score -= 14; goto barrelDone; }
            barrelDone:;

            // ELEVATION EXPLOITATION: when this tile gives a height advantage over the best
            // target, reward it by how much the shot quality actually improves. This captures
            // the HighGroundAim/Crit bonus AND the sees-over-low/high-cover payoff — so enemies
            // specifically prefer height tiles that unlock a meaningfully better shot, not just
            // any plateau. Gate on shoot != null so blind-advance (no target in sight) falls back
            // to the flat HeightAt*14 bonus above. Cap the delta bonus so it never overrides the
            // cover+advance fundamentals; scale by archetype so snipers love it, berserkers don't.
            if (shoot != null && e.Cls != "DRONE")   // drone ignores cover/elevation; no benefit
            {
                var oddsHere = OddsFrom(g, e, tx, ty, shoot);
                var oddsFrom = OddsFrom(g, e, e.X, e.Y, shoot);  // odds from the current standing spot
                int hitDelta  = oddsHere.HitChance  - oddsFrom.HitChance;
                int critDelta = oddsHere.CritChance - oddsFrom.CritChance;
                // Combined shot-quality delta: hit improvement weighted more than crit.
                float qdelta = hitDelta * 0.5f + critDelta * 0.25f;
                // Per-archetype multiplier: snipers/elites care most, berserkers/sappers least.
                // W5: a RagesTwice breaker charges like a berserker — it doesn't perch (checked
                // first: it IS an ELITE, but the rush identity wins over the elite's vantage-seeking).
                float elevMult = (e.RagesTwice)                          ? 0.3f
                               : (e.Cls == "SNIPER" || e.Cls == "ELITE") ? 1.4f
                               : (e.Cls == "BERSERKER")                  ? 0.3f
                               : (e.Cls == "SAPPER")                     ? 0.2f
                               :                                            0.8f;   // grunt/scout/medic/shield
                score += Util.Clamp(qdelta * elevMult, -10f, 30f);   // cap: bonus, not override
            }

            // archetype movement temperament
            if (retreatMode)
            {
                // COORDINATION 2 (apply): fall back — reward distance from the nearest soldier,
                // strongly reward breaking line-of-sight to ALL players (true safety), and lean
                // on cover. Caps the distance term so it doesn't sprint blindly into a corner.
                // UNDERTOW W3: a ROUTING unit panics HARDER than a wounded-but-composed retreater — it
                // really breaks contact (a much stronger distance pull that overrides the shot/move-cost
                // terms, so the rout is a VISIBLE flight, the felt comeback beat), and ignores the move
                // cost while running for its own edge.
                score += Math.Min(distNearest, 10) * (routing ? 6f : 2.6f);
                if (routing) score += actionsToReach * 6;        // cancel the move-cost penalty — commit to the run
                bool seenHere = false;
                foreach (var p in players)
                    if (g.Grid.HasLineOfSight(tx, ty, p.X, p.Y)) { seenHere = true; break; }
                if (!seenHere) score += 45;                      // out of sight = out of the trade
                score += cover.Level * 14;                       // hug cover while withdrawing
            }
            else if (e.Cls == "SNIPER")                          // kite: hold distance, love height
            {
                score += Math.Min(distNearest, e.Weapon.MaxRange) * 2.0f;
                score += g.Grid.HeightAt(tx, ty) * 12;
            }
            else if (e.Cls == "MORTAR" && e.Grenades > 0)       // back-line grenadier: hold off, lob frags
            {
                // hang back toward grenade range so it stays out of the brawl and keeps
                // line-of-throw on clusters; mild height preference. The actual frag toss
                // is handled by the shared grenade AI after the tile loop. Keep a soft pull
                // toward staying reasonably near (so it doesn't flee off the board), capped.
                // ONLY while it still has frags (e.Grenades > 0). Once the pouch is empty a
                // MORTAR has nothing to lob and a poor SMG, so holding the far standoff just
                // wasted turns at the edge of the map -- it now falls through to the generic
                // advance block below (default advW 1.4, i.e. closes to a normal SCOUT-ish
                // fighting range and uses its gun).
                int want = Math.Max(3, Game.GrenadeRange - 1);   // ideal standoff ~ grenade range
                score -= Math.Abs(distNearest - want) * 1.6f;    // settle around the standoff band
                score += g.Grid.HeightAt(tx, ty) * 8;
            }
            else if (e.Cls == "SPOTTER")                        // designator: hang back in cover, stay in contact
            {
                // The BEACON is a fragile force-multiplier — its value is staying ALIVE on the field
                // (it amplifies the squad's focus fire), not trading shots. So it holds a mid
                // standoff well out of the brawl, prizes cover/height hard, and keeps line of sight to
                // the nearest soldier (it must "see" the squad to paint it) without ever charging in.
                int want = 6;                                    // a comfortable observation standoff
                score -= Math.Abs(distNearest - want) * 1.4f;    // settle around the standoff band
                score += cover.Level * 16;                       // value cover heavily (it's frail)
                score += g.Grid.HeightAt(tx, ty) * 10;           // a vantage point reads the field
                if (nearest != null && g.Grid.HasLineOfSight(tx, ty, nearest.X, nearest.Y))
                    score += 10;                                 // stay in contact to keep painting
                if (distNearest <= 2) score -= 24;               // never let the squad close on it
            }
            else if (e.Cls == "SCREENER")                       // area-denial: hold a smoke standoff, keep LoS to screen
            {
                // A SCREENER (HAZE) is a ZONER — its whole value is the SMOKE it lays (handled by the
                // shared UseItem AI below), not its gun. It plays exactly like a SPOTTER positionally:
                // holds a mid standoff out of the brawl, hugs cover (it's frail), and keeps line of
                // sight to the nearest soldier so it can actually place a screen on the squad's lane.
                // It never charges in — the counter-play is to push through / around the cloud (or kill
                // it), NOT to trade with a body that hangs back. Distinct from a SNIPER's kite (it does
                // NOT want max distance — it wants smoke range) and from the SPOTTER (which paints, not
                // screens); both keep-LoS, but only the SCREENER converts that LoS into a blinding cloud.
                int want = Math.Max(3, Game.ItemRange - 2);      // sit within throwing range of the squad's lane
                score -= Math.Abs(distNearest - want) * 1.4f;    // settle around the standoff band
                score += cover.Level * 16;                       // value cover heavily (it's frail)
                score += g.Grid.HeightAt(tx, ty) * 8;
                if (nearest != null && g.Grid.HasLineOfSight(tx, ty, nearest.X, nearest.Y))
                    score += 10;                                 // must SEE the lane it means to screen
                if (distNearest <= 2) score -= 22;               // never let the squad close on it
            }
            else
            {
                float advW = (e.RagesTwice) ? 3.6f                      // W5 BREAKER: the berserker rush temperament — presses like a hound
                           : (e.Cls == "BERSERKER" || e.Cls == "ELITE") ? 3.4f
                           : (e.Cls == "HOUND") ? 3.6f                  // swarmer: hardest charger in the game (low HP, fast)
                           : (e.Cls == "STRIKER") ? 3.5f                // leaper: rushes hard THROUGH overwatch to end flanking
                           : (e.Cls == "DRONE") ? 3.0f                  // drone beelines (ignores cover anyway)
                           : (e.Cls == "HUNTER") ? 2.8f                 // fast flanker: presses hard to curl around cover
                           : (e.Cls == "LANCER") ? 2.4f                 // formation trooper: advances in lockstep with the line
                           : (e.Cls == "SHIELD") ? 2.2f : 1.4f;         // shield pushes the line behind its barrier
                // a HOUND beelines its PREY (the isolated soldier), not the generic nearest target.
                if (e.Cls == "HOUND" && prey != null)
                    score -= Util.ChebyDist(tx, ty, prey.X, prey.Y) * advW;
                else
                    score -= nd > 0 ? distNearest * advW : 0;
            }

            // HUNTER — FLANK SEEKER: actively reward ending on a tile from which the nearest
            // soldier loses the protection of its cover (flanked) or never had cover from this
            // angle (exposed). This makes the hunter curl AROUND a cover block to hit the soft
            // side rather than trade frontally. A genuine flank (was covered, now isn't) is worth
            // most; plain "no cover from here" still earns a smaller pull. Read straight off the
            // same GetCover the shot resolver uses, so the bias is truthful.
            if (e.Cls == "HUNTER" && nearest != null)
            {
                var tgtCov = g.Grid.GetCover(nearest.X, nearest.Y, tx, ty);
                if (tgtCov.Flanked)      score += 34;            // soldier's cover doesn't protect from here
                else if (tgtCov.Level == 0) score += 16;         // soldier simply has no cover from this angle
            }
            // STRIKER — LEAPER / FLANK FINISHER: a fast, fragile repositioner (WRAITH) whose identity is
            // to END the turn on the soldier's SOFT side, no matter what. It seeks the flank even harder
            // than the HUNTER (bigger flank/expose rewards) AND is drawn to end ADJACENT so it slips past
            // a diagonal corner into a point-blank flank. Combined with its overwatch discount below
            // (it accepts reaction fire to close, like a BERSERKER), this makes it the archetype that
            // punishes turtling behind cover + overwatch: you can't just camp a lane — it curls around.
            if (e.Cls == "STRIKER" && nearest != null)
            {
                var tgtCov = g.Grid.GetCover(nearest.X, nearest.Y, tx, ty);
                if (tgtCov.Flanked)      score += 42;            // hardest flank-seeker in the game
                else if (tgtCov.Level == 0) score += 20;         // no cover from here is still good
                if (distNearest == 1)    score += 10;            // end adjacent: point-blank slips the corner
            }
            if (e.Cls == "DRONE") score -= cover.Level * 18;            // drone doesn't value cover (cancels the bonus above)

            // LANCER — FORMATION FIGHTER: it is strongest IN A LINE, so reward ending adjacent to
            // another active hostile (the squad's wall forms up and presses in lockstep). This is the
            // INVERSE of the anti-cluster term below (LANCER is exempt from it), so a phalanx of lancers
            // bunches DELIBERATELY — making the pack a juicy GRENADE / AoE target. Capped at +1 neighbour's
            // worth so the whole pod doesn't infinite-collapse onto one tile.
            if (e.Cls == "LANCER")
            {
                int adjLine = 0;
                foreach (var a in activeAllies)
                    if (Util.ChebyDist(tx, ty, a.X, a.Y) <= 1) adjLine++;
                score += Math.Min(adjLine, 2) * 11f;             // hold the line: each shoulder-to-shoulder ally is worth holding
            }

            if (vip != null) score -= Util.ChebyDist(tx, ty, vip.X, vip.Y) * 0.5f;     // lean toward the asset (was 1.0)
            if (sapTarget != null)                               // sapper: get adjacent to the cover
                score -= Util.ChebyDist(tx, ty, sapTarget.Value.x, sapTarget.Value.y) * 3.0f;

            // COORDINATION 3 — OVERWATCH-AWARE ROUTING (anti-turtle): don't feed a player
            // overwatch camp. Ending a move on a watched tile is heavily penalised; merely
            // passing through one is penalised lightly (movement still happens, but a route
            // that skirts the kill zone wins when it exists). Mirrors the real reaction test
            // (see Game.PlanEnemySquad), so the AI's threat model is truthful. Berserkers/
            // elites/drones discount it — they accept reaction fire to close. This makes player
            // overwatch an area-denial tool instead of a free kill farm.
            if (owTiles.Count > 0)
            {
                // STRIKER (leaper) discounts overwatch like the other rushers: it "phases" through the
                // kill-zone to reach the flank, so camping a lane on overwatch does NOT deter it — the
                // squad must body-block or kill it, not just watch. This is the whole point of the archetype.
                float owEnd = (e.Cls == "BERSERKER" || e.Cls == "ELITE" || e.Cls == "DRONE" || e.Cls == "STRIKER") ? 9f : 26f;
                if (owTiles.Contains((tx, ty))) score -= owEnd;        // end here = eat the shot
                if (c > 0)                                             // only an actual move has a route to skirt
                {
                    int passWatched = 0;
                    foreach (var (rx, ry) in g.Grid.ReconstructPath(cameFrom, e.X, e.Y, tx, ty))
                        if ((rx, ry) != (tx, ty) && owTiles.Contains((rx, ry))) passWatched++;
                    if (passWatched > 0) score -= passWatched * 5f;    // light: skirt the lane
                }
            }

            // COORDINATION 4 — ANTI-CLUSTER: don't gift-wrap a grenade. Small penalty for
            // ending adjacent to many allies so the squad doesn't bunch into one AoE. Shields
            // are exempt (they intentionally anchor a wall the line forms behind); LANCERS are
            // exempt too — bunching into a phalanx IS their identity (and the player's AoE lure).
            if (e.Cls != "SHIELD" && e.Cls != "LANCER")
            {
                int adjAllies = 0;
                foreach (var a in activeAllies)
                    if (Util.ChebyDist(tx, ty, a.X, a.Y) <= 1) adjAllies++;
                if (adjAllies > 1) score -= (adjAllies - 1) * 6f;      // 1 neighbour is fine; 2+ clumps
            }

            // COORDINATION 5 — KITE TO IDEAL RANGE BAND (light): each archetype plays to its
            // gun. Reward a post-move tile whose shot distance sits near the weapon's peak
            // effectiveness (read straight off Weapon.RangeMod), so SMGs/shotguns press in and
            // snipers/LMGs hold off — without overriding cover/advance. Only when a shot exists.
            if (shoot != null && !retreatMode)
            {
                float sd = Util.TileDist(tx, ty, shoot.X, shoot.Y);
                int rm = e.Weapon.RangeMod(sd);                        // this weapon's range aim mod here
                score += Util.Clamp(rm * 0.30f, -6f, 8f);              // modest pull toward the sweet spot
            }
            score += Util.RandRange(0f, 3f);                     // tie-break jitter

            if (score > bestScore)
            {
                bestScore = score;
                bestTile = (tx, ty);
                bestCost = c;
                bestShotTarget = shoot;
            }
        }

        // build path
        if (bestTile != (e.X, e.Y))
        {
            plan.Path = g.Grid.ReconstructPath(cameFrom, e.X, e.Y, bestTile.x, bestTile.y);
            plan.MoveActions = bestCost <= e.MoveBudget ? 1 : 2;
        }

        plan.ShootTarget = bestShotTarget;

        // COORDINATION 2 (feedback): telegraph a genuine fall-back — the unit was low, found
        // no worthwhile shot, and chose to withdraw to a new tile. Pop it so the player reads
        // the squad breaking off. (If a safe tile still happened to offer a shot, it isn't a
        // retreat — the unit re-engaged — so no pop.)
        if (retreatMode && bestShotTarget == null && bestTile != (e.X, e.Y))
            g.Fx.PopText(e.Pos + new Vector2(0, -30), "FALLING BACK", Pal.Suspect, 14f);

        // sapper: if it can reach the cover tile, demolish it instead of shooting
        if (e.Cls == "SAPPER" && sapTarget != null &&
            g.Grid.IsCover(sapTarget.Value.x, sapTarget.Value.y) &&
            Util.ChebyDist(bestTile.x, bestTile.y, sapTarget.Value.x, sapTarget.Value.y) <= 1)
        {
            plan.SapTile = sapTarget;
            plan.ShootTarget = null;       // demolition takes the action
        }

        // grenade option: lob from the post-move tile at the best cluster. Prefer it
        // over shooting when it catches 2+ soldiers, or flushes a single well-covered
        // one we can't shoot well. Never throw if it would catch an ally.
        if (e.Grenades > 0 && plan.SapTile == null)
        {
            var (gx, gy, hits, allies) = BestGrenade(g, e, bestTile.x, bestTile.y);
            if (hits >= 1 && allies == 0)
            {
                bool throwIt = hits >= 2;
                if (!throwIt)   // single target: only if our shot would be weak
                {
                    int shotHit = plan.ShootTarget != null
                        ? OddsFrom(g, e, bestTile.x, bestTile.y, plan.ShootTarget).HitChance : 0;
                    // a MORTAR is a dedicated grenadier with a poor gun and a deep pouch — it
                    // lobs more readily (its whole identity is raining frags), so its
                    // single-target threshold is higher than a line trooper's opportunistic
                    // toss — but dialed back from 70 so it's a threat, not a spammer.
                    throwIt = shotHit < (e.Cls == "MORTAR" ? 55 : 45);
                }
                if (throwIt)
                {
                    plan.Grenade = true; plan.GrenX = gx; plan.GrenY = gy;
                    plan.ShootTarget = null;   // grenade takes the action instead
                }
            }
        }

        // utility item (smoke / flash): PROACTIVE, reason-driven tactical use (AI improvement 2),
        // never hits allies. Gate: must have a charge + no sap/grenade planned. Rather than a flat
        // random roll, each item fires when there's a CONCRETE reason (see below), with only a
        // small random damper so it isn't perfectly predictable/exploitable. A used item ends the
        // turn (UpdateEnemy zeroes ActionsLeft), so this still always spends the action -> no
        // dead turn / no TIMEOUT, and it doesn't disturb the focus-fire/retreat logic above (it
        // only supersedes a planned SHOT, which wouldn't get to fire this turn anyway).
        if (e.EnemyItem != ItemKind.None && e.ItemCharge > 0 && plan.SapTile == null && !plan.Grenade)
        {
            if (e.EnemyItem == ItemKind.Smoke)
            {
                // SMOKE — blind a player OVERWATCH lane the enemy would otherwise cross/feed, or
                // screen an advance across open ground toward the squad. Concrete reasons:
                //  (1) the post-move tile sits IN a player overwatch kill-zone (it would eat
                //      reaction fire ending there), OR the move's ROUTE threads an overwatch tile;
                //  (2) the enemy is closing on the squad with NO shot from cover this turn and is
                //      exposed in the open (a screened advance), which BestSmoke's flanked-tile
                //      branch covers;
                //  (3) BestSmoke otherwise finds an overwatcher-vs-post-move-tile lane to cut.
                // SNIPER/SCOUT/GRUNT are the smoke carriers (Mission.cs); any of them benefit.
                bool endsWatched = owTiles.Count > 0 && owTiles.Contains((bestTile.x, bestTile.y));
                bool routeWatched = false;
                if (!endsWatched && owTiles.Count > 0 && bestTile != (e.X, e.Y))
                    foreach (var (rx, ry) in g.Grid.ReconstructPath(cameFrom, e.X, e.Y, bestTile.x, bestTile.y))
                        if (owTiles.Contains((rx, ry))) { routeWatched = true; break; }
                // crossing open ground: it moved toward the squad, has no shot, and ends exposed.
                bool exposedAdvance = plan.ShootTarget == null && bestTile != (e.X, e.Y)
                    && nearest != null
                    && Util.ChebyDist(bestTile.x, bestTile.y, nearest.X, nearest.Y)
                       < Util.ChebyDist(e.X, e.Y, nearest.X, nearest.Y)
                    && g.Grid.GetCover(bestTile.x, bestTile.y, nearest.X, nearest.Y).Level == 0;

                bool wantSmoke = endsWatched || routeWatched || exposedAdvance;
                // SCREENER (HAZE) — AREA-DENIAL ZONER: this archetype's PRIMARY action is a PROACTIVE
                // smoke on the squad's own firing lane (not just a reactive self-screen). Rather than
                // wait to eat overwatch, it drops a cloud ON the frontline soldier(s) to blind their
                // sightlines, forcing the squad to abandon the tile / reposition to re-acquire targets.
                // BestScreen picks the soldier tile (in throw range, LoS clear so it isn't a blind lob,
                // catching NO fellow enemy's shot) that screens the most soldiers. This layers on top of
                // the reactive reasons above; a SCREENER prefers to screen even when not personally
                // threatened. Progress-safe: if BestScreen finds nothing it falls through to shoot/hunker.
                if (e.Cls == "SCREENER")
                {
                    var (zx, zy, zGood) = BestScreen(g, e, bestTile.x, bestTile.y);
                    if (zGood && Util.Roll(Damp(88)))   // a zoner screens aggressively (small damper only; >= the W6b cap, so tier-fixed)
                    {
                        plan.UseItem = true; plan.ItemTx = zx; plan.ItemTy = zy;
                        if (plan.ShootTarget != null &&
                            Util.ChebyDist(zx, zy, plan.ShootTarget.X, plan.ShootTarget.Y) <= SmokeAnim.Radius)
                            plan.ShootTarget = null;
                    }
                }
                if (!plan.UseItem && wantSmoke)
                {
                    var (sx, sy, smokeGood) = BestSmoke(g, e, bestTile.x, bestTile.y);
                    // strong reasons (about to eat overwatch) fire almost always; a softer
                    // open-ground screen fires often-but-not-always, so it stays a threat not a tic.
                    // W6b: Damp raises the two softer reasons with the coordination tier
                    // (70/55 -> capped 75) — a tier-2 force screens its advances reliably —
                    // while the strong 90 stays fixed and NOTHING ever reaches certainty.
                    bool fire = endsWatched ? Util.Roll(Damp(90)) : routeWatched ? Util.Roll(Damp(70)) : Util.Roll(Damp(55));
                    if (smokeGood && fire)
                    {
                        plan.UseItem = true; plan.ItemTx = sx; plan.ItemTy = sy;
                        // if the smoke would also blind our own planned shot, drop that shot.
                        if (plan.ShootTarget != null &&
                            Util.ChebyDist(sx, sy, plan.ShootTarget.X, plan.ShootTarget.Y) <= SmokeAnim.Radius)
                            plan.ShootTarget = null;
                    }
                }
            }
            else if (e.EnemyItem == ItemKind.Flash)
            {
                // FLASH — a BERSERKER (or any flash-carrier) blinds a CLUSTER of 2+ soldiers to
                // strip their overwatch/aim right before charging in. Only when it genuinely hits
                // 2+ and catches NO ally (BestFlash counts the thrower among allies, so a blast
                // adjacent to e itself is vetoed). It fires reliably when the cluster exists — a
                // pre-charge tool, not a coin flip — with a small damper so it isn't fully scripted.
                var (fx, fy, flashHits, flashAllies) = BestFlash(g, e, bestTile.x, bestTile.y);
                if (flashHits >= 2 && flashAllies == 0 && Util.Roll(Damp(80)))   // 80 >= the W6b cap: tier-fixed (a pre-charge tool, not a knob)
                {
                    plan.UseItem = true; plan.ItemTx = fx; plan.ItemTy = fy;
                    plan.ShootTarget = null;   // flash takes the action (like grenade)
                }
            }
        }

        // AI uses SHOVE (Wave 5): a rusher (BERSERKER/BRUISER/HUNTER) or any LEGION-faction enemy that
        // ends adjacent to a soldier in COVER can shove it OUT of cover -- exposing it for the pod to
        // finish, or slamming it for collision damage if it's pinned. Turns the player's own forced-
        // movement verb against them; thematically the Legion rush. A setup play: it REPLACES a (weak,
        // cover-reduced) shot at that target only when the shove meaningfully exposes it (slides it to a
        // less-covered tile) or is blocked (collision). Bounded -- the exec spends the action (no loop /
        // no TIMEOUT); never the VIP/captive. Considered only with a spare action after moving.
        if (plan.ShoveTarget == null && plan.SapTile == null && !plan.Grenade && !plan.UseItem
            && plan.MoveActions < 2)
        {
            bool rusher = e.Cls == "BERSERKER" || e.Cls == "BRUISER" || e.Cls == "HUNTER"
                          || Combat.MissionFaction == Faction.Legion;
            if (rusher)
            {
                foreach (var p in g.AlivePlayers())
                {
                    if (p.Cls == "VIP") continue;                                  // never shove the asset
                    if (Util.ChebyDist(bestTile.x, bestTile.y, p.X, p.Y) != 1) continue;   // adjacent only
                    var cur = g.Grid.GetCover(p.X, p.Y, bestTile.x, bestTile.y);
                    if (cur.Level == 0) continue;                                  // already exposed -> just shoot it
                    int sdx = Math.Sign(p.X - bestTile.x), sdy = Math.Sign(p.Y - bestTile.y);
                    int nx = p.X + sdx, ny = p.Y + sdy;
                    bool inb = g.Grid.InBounds(nx, ny);
                    bool slides = inb && g.Grid.IsFloor(nx, ny)
                                  && g.Grid.GetCover(nx, ny, bestTile.x, bestTile.y).Level < cur.Level;  // shove exposes it
                    bool pinned = !inb || !g.Grid.IsFloor(nx, ny);                                        // pinned -> collision
                    if (slides || pinned)
                    {
                        plan.ShoveTarget = p;
                        plan.ShootTarget = null;   // the shove takes the action
                        break;
                    }
                }
            }
        }

        // if no shot is possible and we still have an action after moving, hunker/overwatch
        if (plan.ShootTarget == null && !plan.Grenade && plan.SapTile == null && !plan.UseItem && plan.ShoveTarget == null)
        {
            int spent = plan.MoveActions;
            if (spent < 2)
            {
                var coverHere = g.Grid.GetCover(bestTile.x, bestTile.y, nearest.X, nearest.Y);
                // overwatch if we have a clear sightline toward enemy approach, else hunker. A ROUTED unit
                // (UNDERTOW W3) is too rattled to hold a steady watch — it just keeps its head down.
                // W6a truthfulness: the sightline read mirrors Game.CanTarget's commanding overload —
                // a unit holding a >=2-tier vantage watches over high cover (the reaction it would
                // actually take, via CanTarget, sees over it too), so it no longer hunkers on a
                // commanding perch it genuinely controls.
                bool cmdOw = g.Grid.HeightAt(bestTile.x, bestTile.y) - g.Grid.HeightAt(nearest.X, nearest.Y) >= 2;
                bool sees = g.Grid.HasLineOfSight(bestTile.x, bestTile.y, nearest.X, nearest.Y, cmdOw);
                if (sees && e.Ammo > 0 && !routing) plan.Overwatch = true;
                else if (coverHere.Level > 0) plan.Hunker = true;
            }
        }

        return plan;
    }

    // Best smoke tile thrown from (fx,fy): find an overwatching player with LoS to the
    // enemy's post-move tile and place smoke halfway between them to blind the lane.
    // Also considers lobbing at the enemy's own forward tile when badly exposed (flanked).
    // Returns (tx, ty, worthDoing).
    static (int x, int y, bool good) BestSmoke(Game g, Unit e, int fx, int fy)
    {
        // primary: find an overwatching player who can see the post-move tile
        foreach (var p in g.AlivePlayers())
        {
            if (!p.OnOverwatch) continue;
            if (!g.Grid.HasLineOfSight(p.X, p.Y, fx, fy)) continue;
            // aim halfway between the enemy's post-move tile and the overwatcher
            int tx = (fx + p.X) / 2;
            int ty = (fy + p.Y) / 2;
            if (!g.Grid.InBounds(tx, ty)) { tx = fx; ty = fy; }
            if (Util.TileDist(fx, fy, tx, ty) > Game.ItemRange) { tx = fx; ty = fy; }
            // don't land smoke in a tile occupied by a friendly
            bool allyBlocked = false;
            foreach (var a in g.AliveEnemies())
                if (a != e && Util.ChebyDist(tx, ty, a.X, a.Y) <= SmokeAnim.Radius) { allyBlocked = true; break; }
            if (!allyBlocked) return (tx, ty, true);
        }
        // secondary: the enemy is flanked/exposed — smoke its own forward tile to
        // cover its current spot (useful when retreating or holding a thin position).
        var eCover = g.Grid.GetCover(fx, fy,
            g.AlivePlayers().Count > 0 ? g.AlivePlayers()[0].X : 0,
            g.AlivePlayers().Count > 0 ? g.AlivePlayers()[0].Y : 0);
        if (eCover.Flanked)   // smoke lands on the unit's own tile, so range is trivially ok
            return (fx, fy, true);
        return (0, 0, false);
    }

    // Best PROACTIVE SCREEN tile thrown from (fx,fy) for a SCREENER (HAZE) zoner: land a smoke cloud
    // ON the squad's firing lane to blind it and force a reposition. We aim at a soldier's own tile
    // (the radius-1 cloud then also covers its neighbours), choosing the soldier whose cloud screens
    // the MOST soldiers. Constraints for FAIRNESS + no self-harm:
    //   - in throw range of the post-move tile, and LoS from it (no blind lob over a wall);
    //   - the cloud must NOT sit on/adjacent to a fellow enemy (it would blind our OWN sightlines);
    //   - the cloud must NOT blind a fellow enemy's EXISTING shot on that soldier (don't screen our
    //     own kill). A screen that only cuts THIS screener's weak SMG shot is fine (that's the trade).
    // Returns (tx, ty, worthDoing). worthDoing == false -> caller falls through to shoot/hunker (no
    // dead turn / no TIMEOUT). Never targets the fragile VIP (screening the asset wastes the cloud).
    static (int x, int y, bool good) BestScreen(Game g, Unit e, int fx, int fy)
    {
        int bx = -1, by = -1, best = 0;
        foreach (var p in g.AlivePlayers())
        {
            if (p.IsVip) continue;
            if (Util.TileDist(fx, fy, p.X, p.Y) > Game.ItemRange) continue;
            if (!g.Grid.HasLineOfSight(fx, fy, p.X, p.Y)) continue;         // must see the lane it screens
            // don't drop the cloud on/next to a fellow enemy (it would blind our own team's sightlines)
            bool allyInCloud = false;
            foreach (var a in g.AliveEnemies())
                if (a != e && Util.ChebyDist(p.X, p.Y, a.X, a.Y) <= SmokeAnim.Radius) { allyInCloud = true; break; }
            if (allyInCloud) continue;
            // don't screen a shot a fellow enemy already has on this soldier (don't smoke our own kill)
            bool screensAllyShot = false;
            foreach (var a in g.AliveEnemies())
            {
                if (a == e || !a.Active) continue;
                if (Util.TileDist(a.X, a.Y, p.X, p.Y) <= a.Weapon.MaxRange
                    && g.Grid.HasLineOfSight(a.X, a.Y, p.X, p.Y)) { screensAllyShot = true; break; }
            }
            if (screensAllyShot) continue;
            // score: how many soldiers this radius-1 cloud would blind (a lane through a cluster is best)
            int hits = 0;
            foreach (var q in g.AlivePlayers())
                if (!q.IsVip && Util.ChebyDist(p.X, p.Y, q.X, q.Y) <= SmokeAnim.Radius) hits++;
            if (hits > best) { best = hits; bx = p.X; by = p.Y; }
        }
        return best > 0 ? (bx, by, true) : (0, 0, false);
    }

    // Best flash aim tile thrown from (fx,fy): pick a tile where 2+ players cluster
    // within FlashAnim.Radius. Report ally splash count for safety gating.
    static (int x, int y, int hits, int allies) BestFlash(Game g, Unit e, int fx, int fy)
    {
        int bx = -1, by = -1, bestHits = 0, bestAllies = 99;
        foreach (var p in g.AlivePlayers())
        {
            if (Util.TileDist(fx, fy, p.X, p.Y) > Game.ItemRange) continue;
            int hits = 0, allies = 0;
            foreach (var q in g.AlivePlayers()) if (Util.ChebyDist(p.X, p.Y, q.X, q.Y) <= FlashAnim.Radius) hits++;
            // count the THROWER too (review Mi4): a flash that lands adjacent to e would
            // disorient e itself - that's self-harm, so it must veto the throw.
            foreach (var a in g.AliveEnemies()) if (Util.ChebyDist(p.X, p.Y, a.X, a.Y) <= FlashAnim.Radius) allies++;
            if (hits > bestHits || (hits == bestHits && allies < bestAllies))
            { bestHits = hits; bestAllies = allies; bx = p.X; by = p.Y; }
        }
        return (bx, by, bestHits, bestAllies);
    }

    // Best 3x3 SIEGE strike center: the soldier-tile whose SiegeRadius block catches the MOST
    // soldiers, catching NO active SIEGE ally in the blast (it's friendly-fire AoE, so the AI must
    // not shell its own). INDIRECT fire -> NO LoS requirement (the whole point: it can shell a
    // soldier hiding behind high cover, forcing them to RELOCATE, not just break LoS). Skips the
    // fragile VIP (don't waste the shell on the soft asset). Returns (cx, cy, hits); hits==0 when
    // nothing is worth shelling (the caller then falls through to the generic loop -> no dead turn).
    static (int x, int y, int hits) BestSiege(Game g, Unit e)
    {
        int bx = -1, by = -1, best = 0;
        foreach (var p in g.AlivePlayers())
        {
            if (p.IsVip) continue;                               // don't waste the shell on the fragile asset
            int hits = 0, allies = 0;
            foreach (var q in g.AlivePlayers())
                if (Util.ChebyDist(p.X, p.Y, q.X, q.Y) <= Game.SiegeRadius) hits++;
            foreach (var a in g.AliveEnemies())
                if (a != e && a.Active && Util.ChebyDist(p.X, p.Y, a.X, a.Y) <= Game.SiegeRadius) allies++;
            if (allies > 0) continue;                            // never shell our own
            if (hits > best) { best = hits; bx = p.X; by = p.Y; }
        }
        return (bx, by, best);
    }

    // Best grenade aim tile thrown from (fx,fy): pick a soldier's tile in range that
    // catches the most players (blast = Chebyshev radius 1); report ally splash too.
    // FAIRNESS: the thrower must have LINE OF SIGHT from its post-move tile to the
    // aim soldier — no lobbing blindly over a wall or through smoke. This makes enemy
    // grenades counterable by breaking LoS (cover / smoke), consistent with the game's
    // perfect-information contract. Applies to every enemy that throws (MORTAR, BRUISER,
    // WARLORD, ...). Note we only require sight of the *aim* soldier; a clustered second
    // soldier behind cover still gets caught by the AoE, which is fair (the throw was
    // earned by a visible target and the blast spreads).
    static (int x, int y, int hits, int allies) BestGrenade(Game g, Unit e, int fx, int fy)
    {
        int bx = -1, by = -1, bestHits = 0, bestAllies = 99;
        foreach (var p in g.AlivePlayers())
        {
            if (Util.TileDist(fx, fy, p.X, p.Y) > Game.GrenadeRange) continue;
            if (!g.Grid.HasLineOfSight(fx, fy, p.X, p.Y)) continue;   // can't blind-lob over walls / through smoke
            if (Util.ChebyDist(fx, fy, p.X, p.Y) <= GrenadeAnim.Radius) continue;  // don't catch the thrower in its own blast
            int hits = 0, allies = 0;
            foreach (var q in g.AlivePlayers()) if (Util.ChebyDist(p.X, p.Y, q.X, q.Y) <= GrenadeAnim.Radius) hits++;
            foreach (var a in g.AliveEnemies()) if (a != e && Util.ChebyDist(p.X, p.Y, a.X, a.Y) <= GrenadeAnim.Radius) allies++;
            if (hits > bestHits || (hits == bestHits && allies < bestAllies))
            { bestHits = hits; bestAllies = allies; bx = p.X; by = p.Y; }
        }
        return (bx, by, bestHits, bestAllies);
    }

    // CROSSFIRE test (AI improvement 1): true when firing on target `tgt` from candidate tile
    // (cx,cy) forms a crossfire with at least one OTHER living enemy that already has line-of-
    // sight to `tgt` from a meaningfully DIFFERENT angle. W6a: this predicate is PINNED to
    // Combat.InCrossfire — the resolver that actually pays the bonus — term by term:
    //  * ally gate = dist <= Combat.CrossfireAllyRange ALONE. InCrossfire has NO ally-weapon-range
    //    term (Combat.cs "credible threat" check), so the old min-with-e2.Weapon.MaxRange gate made
    //    the planner stricter than the resolver for shotgun allies (MaxRange 8): a BERSERKER ally at
    //    dist 9-10 grants the real +CrossfireAim but the planner predicted none.
    //  * angle = Combat.CrossfireCosMax (same constant, not a local copy).
    //  * LoS = the PLAIN (non-commanding) HasLineOfSight, exactly as InCrossfire's ally-credibility
    //    read. Deliberately NOT the commanding overload even when the ally holds a tier-2 perch:
    //    the resolver doesn't grant commanding sight to the converging ally, so adding it here
    //    would predict crossfires the resolver never pays (the opposite untruthfulness).
    //  * alertness: NO !e2.Active skip. InCrossfire counts every alive same-team non-VIP unit in
    //    Combat.AllUnits INCLUDING a dormant pod-mate, so the old skip under-predicted the
    //    shooter's own real odds near a sleeping pod. If dormant allies should ever stop granting
    //    crossfire, fix the RESOLVER first and this predicate follows.
    // (The old "computed locally / no Combat reference" note was stale — Ai already calls
    // Combat.ComputeOdds and reads Combat.MissionFaction.) Degenerate zero-length vectors (an ally
    // or the candidate sharing the target's tile) are skipped — they have no defined angle.
    // Internal (not private) so the AITEST harness can pin planner==resolver agreement directly.
    internal static bool CrossfireWith(Game g, Unit self, int cx, int cy, Unit tgt)
    {
        float v1x = tgt.X - cx, v1y = tgt.Y - cy;
        float m1 = MathF.Sqrt(v1x * v1x + v1y * v1y);
        if (m1 < 0.001f) return false;                       // candidate on the target: no angle
        foreach (var e2 in g.AliveEnemies())
        {
            if (e2 == self) continue;
            if (Util.TileDist(e2.X, e2.Y, tgt.X, tgt.Y) > Combat.CrossfireAllyRange) continue;
            if (!g.Grid.HasLineOfSight(e2.X, e2.Y, tgt.X, tgt.Y)) continue;
            float v2x = tgt.X - e2.X, v2y = tgt.Y - e2.Y;
            float m2 = MathF.Sqrt(v2x * v2x + v2y * v2y);
            if (m2 < 0.001f) continue;                       // ally on the target: no angle
            float cos = (v1x * v2x + v1y * v2y) / (m1 * m2);
            if (cos < Combat.CrossfireCosMax) return true;   // vectors diverge > ~72deg -> crossfire
        }
        return false;
    }

    // odds as if attacker stood at (ax,ay)
    static ShotOdds OddsFrom(Game g, Unit a, int ax, int ay, Unit d)
    {
        int ox = a.X, oy = a.Y;
        a.X = ax; a.Y = ay;
        try { return Combat.ComputeOdds(g.Grid, a, d); }
        finally { a.X = ox; a.Y = oy; }  // always restore the live position even if scoring throws
    }

    // True when a live, ALERT SPOTTER (BEACON) is on the field other than `self` — the
    // force-multiplier condition. While one survives it "paints" the squad's priority target,
    // so every planning ally amplifies its focus-fire + crossfire bias (see Ai.Plan). A SPOTTER
    // never amplifies for itself (it's a fragile designator, not a shooter), and a Suspicious/
    // Unaware (not-yet-engaged) one doesn't count — only an active beacon is coordinating.
    static bool SpotterActive(Game g, Unit self)
    {
        foreach (var a in g.AliveEnemies())
            if (a != self && a.Active && a.Cls == "SPOTTER") return true;
        return false;
    }
}
