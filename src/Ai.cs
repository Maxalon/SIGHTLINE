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
    public int ItemTx, ItemTy;    // item aim tile
}

/// Tactical decision-making for a single enemy. Greedy, but reads as competent:
/// seek cover + line of fire, prefer flanking/finishing, advance when blind.
public static class Ai
{
    public const int HealRange = 4;    // tiles a medic can mend across
    public const int HealAmount = 4;   // HP restored per heal

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
                       && e.Cls != "DRONE" && e.Cls != "SAPPER" && e.Cls != "TURRET";
        bool lowHp = e.Hp <= Math.Max(1, e.MaxHp * 3 / 10);   // <= ~30% MaxHp
        bool retreatMode = false;
        if (canRetreat && lowHp)
        {
            float bestReachHit = -1f;
            foreach (var (tx, ty, c) in reach)
            {
                if (c > e.MoveBudget) continue;                 // must keep an action to fire
                foreach (var p in players)
                {
                    if (Util.TileDist(tx, ty, p.X, p.Y) > e.Weapon.MaxRange) continue;
                    if (!g.Grid.HasLineOfSight(tx, ty, p.X, p.Y)) continue;
                    int h = OddsFrom(g, e, tx, ty, p).HitChance;
                    if (h > bestReachHit) bestReachHit = h;
                }
            }
            // only break off when no reachable tile yields a meaningful shot (<55% best);
            // if it can still hit hard it stands and fights (a trade may be worth it).
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
                    if (!g.Grid.HasLineOfSight(tx, ty, p.X, p.Y)) continue;
                    var odds = OddsFrom(g, e, tx, ty, p);
                    bool canFinish = p.Hp <= e.Weapon.DmgMax;
                    float val = odds.HitChance + (odds.CoverLevel == 0 ? 25 : 0)
                                + (canFinish ? 30 : 0)                   // can finish?
                                + (p.IsVip ? 40 : 0);                    // prioritise the VIP
                    // COORDINATION 1 — FOCUS FIRE: the squad converges on a shared priority
                    // target (chosen once per turn in Game.PlanEnemySquad). Reward shooting it
                    // so enemies collapse one soldier rather than spreading chip damage; the
                    // bonus is larger when this shot would be a likely killing blow (high hit %
                    // AND lethal damage), so the squad actually closes the kill. Advisory: it
                    // layers on top of hit/cover/finish, never replacing the "good shot" core.
                    if (g.EnemyFocus != null && p == g.EnemyFocus)
                    {
                        val += 30;                                       // concentrate fire here
                        if (canFinish && odds.HitChance >= 50) val += 35; // press a likely kill
                    }
                    if (val > bestHit) { bestHit = val; shoot = p; }
                }
            }

            // cover quality at this tile vs the nearest player
            var cover = g.Grid.GetCover(tx, ty, nearest.X, nearest.Y);
            int distNearest = Util.ChebyDist(tx, ty, nearest.X, nearest.Y);
            float score = 0;
            if (shoot != null) score += 100 + bestHit;          // having a shot is king
            score += cover.Level * 18;                           // value cover
            score += g.Grid.HeightAt(tx, ty) * 14;               // seize the high ground
            if (cover.Flanked) score -= 25;
            score -= actionsToReach * 6;                         // prefer cheaper moves slightly

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
                float elevMult = (e.Cls == "SNIPER" || e.Cls == "ELITE") ? 1.4f
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
                score += Math.Min(distNearest, 10) * 2.6f;
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
            else
            {
                float advW = (e.Cls == "BERSERKER" || e.Cls == "ELITE") ? 3.4f
                           : (e.Cls == "DRONE") ? 3.0f                  // drone beelines (ignores cover anyway)
                           : (e.Cls == "SHIELD") ? 2.2f : 1.4f;         // shield pushes the line behind its barrier
                score -= nd > 0 ? distNearest * advW : 0;
            }
            if (e.Cls == "DRONE") score -= cover.Level * 18;            // drone doesn't value cover (cancels the bonus above)
            if (vip != null) score -= Util.ChebyDist(tx, ty, vip.X, vip.Y) * 1.0f;     // close on the asset
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
                float owEnd = (e.Cls == "BERSERKER" || e.Cls == "ELITE" || e.Cls == "DRONE") ? 9f : 26f;
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
            // are exempt (they intentionally anchor a wall the line forms behind).
            if (e.Cls != "SHIELD")
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
                    throwIt = shotHit < 45;
                }
                if (throwIt)
                {
                    plan.Grenade = true; plan.GrenX = gx; plan.GrenY = gy;
                    plan.ShootTarget = null;   // grenade takes the action instead
                }
            }
        }

        // utility item (smoke / flash): occasional tactical use, never hits allies.
        // Gate: must have a charge, SapTile must be null, and ~25% base probability.
        if (e.EnemyItem != ItemKind.None && e.ItemCharge > 0 && plan.SapTile == null
            && !plan.Grenade && Util.Roll(25))
        {
            if (e.EnemyItem == ItemKind.Smoke)
            {
                // Smoke: use when an overwatching player has LoS to the enemy's post-move tile,
                // or the enemy is badly exposed (flanked). Lay smoke on a tile between the
                // enemy and the nearest overwatching player to blind the reaction lane.
                // Smoke hurts BOTH sides equally, so only use it to cover a move, never when
                // the enemy needs to shoot through it (it can still shoot after a smoke that
                // landed away from the target).
                var (sx, sy, smokeGood) = BestSmoke(g, e, bestTile.x, bestTile.y);
                if (smokeGood)
                {
                    plan.UseItem = true; plan.ItemTx = sx; plan.ItemTy = sy;
                    // Using an item ends the enemy's turn (UpdateEnemy zeroes ActionsLeft),
                    // so a planned shot won't fire this turn regardless; still null ShootTarget
                    // when the smoke lands on the target tile so the AI doesn't "plan" a shot
                    // it would have blinded anyway.
                    if (plan.ShootTarget != null &&
                        Util.ChebyDist(sx, sy, plan.ShootTarget.X, plan.ShootTarget.Y) <= SmokeAnim.Radius)
                        plan.ShootTarget = null;
                }
            }
            else if (e.EnemyItem == ItemKind.Flash)
            {
                // Flash: disorient 2+ players OR break an overwatching cluster.
                // Never catch allies in the blast radius.
                var (fx, fy, flashHits, flashAllies) = BestFlash(g, e, bestTile.x, bestTile.y);
                if (flashHits >= 2 && flashAllies == 0)
                {
                    plan.UseItem = true; plan.ItemTx = fx; plan.ItemTy = fy;
                    plan.ShootTarget = null;   // flash takes the action (like grenade)
                }
            }
        }

        // if no shot is possible and we still have an action after moving, hunker/overwatch
        if (plan.ShootTarget == null && !plan.Grenade && plan.SapTile == null && !plan.UseItem)
        {
            int spent = plan.MoveActions;
            if (spent < 2)
            {
                var coverHere = g.Grid.GetCover(bestTile.x, bestTile.y, nearest.X, nearest.Y);
                // overwatch if we have a clear sightline toward enemy approach, else hunker
                bool sees = g.Grid.HasLineOfSight(bestTile.x, bestTile.y, nearest.X, nearest.Y);
                if (sees && e.Ammo > 0) plan.Overwatch = true;
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

    // Best grenade aim tile thrown from (fx,fy): pick a soldier's tile in range that
    // catches the most players (blast = Chebyshev radius 1); report ally splash too.
    static (int x, int y, int hits, int allies) BestGrenade(Game g, Unit e, int fx, int fy)
    {
        int bx = -1, by = -1, bestHits = 0, bestAllies = 99;
        foreach (var p in g.AlivePlayers())
        {
            if (Util.TileDist(fx, fy, p.X, p.Y) > Game.GrenadeRange) continue;
            int hits = 0, allies = 0;
            foreach (var q in g.AlivePlayers()) if (Util.ChebyDist(p.X, p.Y, q.X, q.Y) <= GrenadeAnim.Radius) hits++;
            foreach (var a in g.AliveEnemies()) if (a != e && Util.ChebyDist(p.X, p.Y, a.X, a.Y) <= GrenadeAnim.Radius) allies++;
            if (hits > bestHits || (hits == bestHits && allies < bestAllies))
            { bestHits = hits; bestAllies = allies; bx = p.X; by = p.Y; }
        }
        return (bx, by, bestHits, bestAllies);
    }

    // odds as if attacker stood at (ax,ay)
    static ShotOdds OddsFrom(Game g, Unit a, int ax, int ay, Unit d)
    {
        int ox = a.X, oy = a.Y;
        a.X = ax; a.Y = ay;
        var odds = Combat.ComputeOdds(g.Grid, a, d);
        a.X = ox; a.Y = oy;
        return odds;
    }
}
