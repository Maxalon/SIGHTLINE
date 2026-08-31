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
    public bool Brace;                  // PIKEMAN (FUL-8): plant a braced focus cone over a movement lane
    public int BraceDirX, BraceDirY;    // FUL-8: cone axis = anchor - plant tile (Math.Sign per component)
    public bool Reload;                 // W2: the gun is empty — spend the action changing the mag
    public bool IdleRepair;             // W2 (harness accounting): this plan's action exists only
                                        // because of the wave's terminal else — dash or dig-in.
    // ── C2 THE OPPONENT DECLINES ─────────────────────────────────────────────────────────
    // The shot that was ON THE TABLE from the tile this plan chose, whether or not it is taken.
    // ShotHit is its hit chance (-1 = the chosen tile had no shot at all); ShotExp is the
    // graze/armor-aware expected damage of that same shot (Combat.ExpectedDamage). Declined is
    // set only when the planner had that shot and deliberately dropped it for a better use of
    // the action. Read by Game.UpdateEnemy for the Stats decision mix and by DECLINETEST; the
    // pair is computed once per plan, never per reachable tile.
    public int ShotHit = -1;
    public float ShotExp;
    public bool Declined;
    // C2: this decline was taken BECAUSE the unit is under several guns, so the action it frees
    // should buy SURVIVAL (dig in) rather than a lane. Read by the no-shot fallback below, which
    // otherwise always prefers overwatch to hunker.
    public bool DeclineDigIn;
}

/// Tactical decision-making for a single enemy. Greedy, but reads as competent:
/// seek cover + line of fire, prefer flanking/finishing, advance when blind.
public static class Ai
{
    public const int HealRange = 4;    // tiles a medic can mend across
    public const int HealAmount = 4;   // HP restored per heal

    // ── C2 THE OPPONENT DECLINES — the prices ────────────────────────────────────────────
    // SHOTSEAT: what a line of fire is worth on its own, before any expectation of hitting.
    // Deliberately ~one level of cover (cover.Level * 18), because that is the trade the term
    // has to arbitrate: "stand in the open with a shot" vs "stand behind that wall without
    // one". The pre-C2 value of this seat was 100, against terrain terms bounded under ~64.
    //
    // The two BARS are RATIOS, not absolute damage, and the first draft of this wave got that
    // wrong in a way worth recording. An absolute expected-damage bar (1.45 / 0.90) declined
    // 35 shots at 80%+ hit chance in a 20-run probe, because Combat.ExpectedDamage is
    // armour-aware: a popgun against a hardened soldier is ~1.0 expected damage at ANY hit
    // chance. Declining a clean 90% shot does not read as a smarter opponent, it reads as a
    // broken one — and it is also wrong on the merits, because that unit's ALTERNATIVE is worth
    // ~1.0 too. The bar has to move with the unit's own ceiling.
    //
    // So the shot is priced against Combat.AsIfExposed — the SAME shot with the defender's
    // cover taken away, which is what an overwatch reaction actually catches (the reaction
    // resolves on every tile ENTERED, and a soldier crossing between cover blocks is uncovered
    // on the way). ratio = E[dmg now] / E[dmg if they were caught in the open], in (0, 1].
    //   WATCH ratio 0.45 (the unit can hold a lane from this tile): the higher of the two, and
    //   the ONE number in this block that is a judgement rather than a measurement. What the
    //   flywheel can see says LOWER: a held enemy lane was measured firing 24-27% of the time
    //   (n=457/461 lanes, the maximal-decline diagnostic in docs/measurements/c2/), which times
    //   the reaction's -10 aim mod makes a lane worth ~0.20 of the open shot. The excess is the
    //   part the flywheel is structurally blind to — Game.Autopilot.TileExposure carries +18 for
    //   an enemy BRACE lane and NOTHING for an ordinary enemy overwatch, so the bot walks into
    //   enemy kill-zones and the AREA-DENIAL half of a lane cannot be priced. 0.45 is roughly
    //   twice the visible value and no more; it is declared, not hidden, and the ladder is what
    //   prices whether it was affordable (it moved five of six rungs by one discordant pair).
    //   DIG ratio 0.30 (in cover, no lane): lower. Hunkering buys -25 to be hit and no crit; it
    //   is real, but it is damage DENIED, not damage dealt, so it displaces less.
    // With NEITHER available the shot is always taken — there is nothing to prefer to it.
    //
    // ABSKEEP 3.00 is the feel guard the ratio alone does not give: a shot already worth this
    // much expected damage is taken whatever the ratio says, so a genuinely damaging shot is
    // never passed up. It sits just above the pre-wave 60-79% band's measured mean (2.51), i.e.
    // above "a solid shot", and it is inert at the shipped ratios (a 0.45-ratio shot is weak by
    // construction) — it is there so that raising the ratios later cannot produce an opponent
    // that passes up real damage.
    //
    // THE GUNS ON ME. Firing and then standing still is not free: Combat's EXPOSED BY FIRE rule
    // (ExposedFireAim/Crit, +12/+12, symmetric for both teams) hands every soldier that can see
    // this unit a sharper shot at it until it moves again. Holding a lane or digging in does not.
    // So the more soldiers already have a firing solution on the tile the unit is standing on,
    // the more a MARGINAL shot costs it — and the bar it has to clear rises with them. Capped at
    // three guns; one soldier is a duel, three is a kill box, and past three it saturates.
    internal const float DeclineThreatScale = 0.20f;
    internal const int DeclineThreatCap = 3;
    internal const float ShotSeat = 18f;
    internal const float DeclineWatchRatio = 0.45f;
    internal const float DeclineDigRatio = 0.30f;
    internal const float DeclineAbsKeep = 3.00f;
    // A killing blow is worth more than its damage number — it takes a gun off the board for
    // the rest of the fight — so a shot in the finish band is pressed at a discount.
    internal const float FinishPress = 1.6f;

    /// C2: what a line of fire from this tile is worth to the per-tile scorer. NOT an expected
    /// value despite the shape — `bestHit` already contains `hitPct`, so this expands to
    /// `ShotSeat + hitPct^2/100 + bonuses*hitPct/100`. See the call site for why the squaring is
    /// deliberate. Extracted so
    /// SIGHTLINE_DECLINETEST can pin the arithmetic of the one line this wave changed, on both
    /// sides of its dial, without reconstructing a whole board — the pre-C2 branch is the literal
    /// constant it replaced.
    ///   `bestHit` — the winning target's CHOICE value from this tile (hit chance plus what
    ///               connecting is worth: exposure, the finish band, focus, crossfire).
    ///   `hitPct`  — that same target's REAL hit chance from this tile.
    internal static float ShotTileValue(float bestHit, int hitPct)
        => Game.AiDecline
             ? ShotSeat + bestHit * (Util.Clamp(hitPct, 0, 100) / 100f)
             : 100 + bestHit;                                        // the pre-C2 constant

    /// C2: the archetypes that never decline, because declining is not what they ARE. The
    /// rushers (BERSERKER/HOUND/STRIKER/DRONE and the Legion BREAKER's second rage) are the
    /// game's pressure valve — Ai.cs already gives them a 3.0-3.6 advance weight and an
    /// overwatch discount so they eat reaction fire to close. An opponent whose chargers
    /// suddenly took cover would not read as smarter, it would read as broken, and it would
    /// remove the tempo the patient archetypes are measured against.
    ///
    /// ELITE IS DELIBERATELY NOT ON THIS LIST, and the review was right that the rationale above
    /// describes it (advW 3.4, the same owEnd 9f discount, never broken off). The exclusion is a
    /// judgement, not an oversight: an ELITE is a BOSS, it is the archetype most likely to survive
    /// long enough for a held lane to pay, and a boss that visibly picks its moment reads as
    /// competent where a charger doing the same reads as broken. It is also the one body whose
    /// damage output most rewards waiting for a better shot. If a later wave wants chargers and
    /// bosses to behave alike, change the SET — but change this comment with it.
    static bool NeverDeclines(Unit e)
        => e.RagesTwice || e.Cls == "BERSERKER" || e.Cls == "HOUND"
        || e.Cls == "STRIKER" || e.Cls == "DRONE";

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

    /// `reserved` (Q1): tiles that are logically taken even though nobody is STANDING on them
    /// yet — the destinations of moves that are already planned/queued but have not executed.
    /// Unit.X/Y only commits when a MoveStepAnim FINISHES, so any caller that plans several
    /// units against one board snapshot before running any of them (Game.ActivatePod's reveal
    /// scatter) must pass its running claim set here, or the second planner walks onto the
    /// first one's destination. Null (the default) restores the exact pre-Q1 predicate, so
    /// every one-unit-at-a-time caller — the enemy turn, the harness intent previews — is
    /// bit-for-bit unchanged.
    public static EnemyPlan Plan(Game g, Unit e) => Plan(g, e, null);

    public static EnemyPlan Plan(Game g, Unit e, HashSet<(int x, int y)> reserved)
    {
        var plan = new EnemyPlan();
        // FUL-7 LAST LIGHT — the single AI seam: enemies do NOT target downed (bleeding-out)
        // soldiers with direct fire. Filtering here means the shoot-target loop, the finish
        // band, HOUND prey, advance/nearest and the shove pick all inherit it. Rationale: a
        // down already costs the squad a body plus the rescue actions; an executing AI would
        // convert the drama into a guaranteed double-loss and make STABILIZE a trap verb. The
        // honesty valve that keeps stakes: AoE stays blind — a shell/frag/barrel/fire field
        // that catches the body kills it, and all of those are telegraphed (XCOM's rule).
        // All-downed squad -> empty players -> empty plans; the bleed-out timers bound it.
        var players = g.AlivePlayers();
        players.RemoveAll(p => p.Downed);
        if (players.Count == 0) return plan;

        // movement reachability (other units block)
        Func<int, int, bool> blocked = reserved == null
            ? (x, y) => g.IsOccupiedByOther(x, y, e)
            : (x, y) => g.IsOccupiedByOther(x, y, e) || reserved.Contains((x, y));
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
        int bestShotHit = 0;             // C2: hit chance of bestShotTarget from bestTile
        // W2 THE OPPONENT ACTS — the best FULL-BUDGET (two-action) destination, tracked by the same
        // per-tile scorer in the same pass. It is the terminal else of the no-shot fallback: when a
        // unit has a spare action and nothing to spend it on, it spends it on GROUND. Tracking it
        // here rather than re-scoring later means no second traversal and — critically — no extra
        // Util.Rng draw, so the CRN pairing the whole measurement methodology rests on is unmoved.
        (int x, int y) bestDash = (-1, -1);
        float bestDashScore = float.NegativeInfinity;

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
        // W8 review: gated on Routed == 0 — the specialist branches precede the routed-flee
        // logic below, so without the gate a BROKEN medic kept calmly working its job,
        // contradicting the "routed units flee regardless of archetype" morale invariant.
        // (Same gate on the CUSTODIAN and BOMBARD branches.) A routed specialist falls
        // through to the generic loop, where retreatMode makes it flee like everyone else.
        if (e.Cls == "MEDIC" && e.Routed == 0)
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
        // W8 review: Routed == 0 gate — a BROKEN keeper flees like everyone else instead of
        // working the objective (see the MEDIC branch note; the comeback beat must hold here most
        // of all, since this unit contests the objective itself).
        if (e.Cls == "CUSTODIAN" && e.Routed == 0)
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

        // PIKEMAN (SARISSA, FUL-8): the LANE-HOLDER — nothing else in the roster contests WHERE the
        // squad may walk. It plants a braced focus cone (the exact enemy-side mirror of the player's
        // own BRACE [B]: OnOverwatch+OwBrace+OwFocused, armed by Game's ActAfterMove exec) over a
        // movement lane and STAGGERS the first soldier through. Zero new combat machinery — the
        // OnUnitEnteredTile reaction path is already team-symmetric. Gates, per the W8 routed-
        // specialist rule + the FLASH counterplay: a BROKEN pikeman flees like everyone else, a
        // Disoriented one cannot re-plant (mirrors the enemy-overwatch exec gate), a dry one has
        // nothing to threaten the lane with. All three fall through to the generic loop — never a
        // dead turn / no TIMEOUT (the MORTAR safety).
        if (e.Cls == "PIKEMAN" && e.Routed == 0 && !e.HasStatus(StatusKind.Disoriented) && e.Ammo > 0)
        {
            // Opportunism first (identity: a holder, not a statue — mirrors BOMBARD's fall-through
            // when nothing is worth shelling): an exposed soldier it can already punish >= 65% is a
            // better use of the action than a plant the squad will simply route around.
            bool opp = false;
            foreach (var p in players)
            {
                if (Util.TileDist(e.X, e.Y, p.X, p.Y) > e.Weapon.MaxRange) continue;
                bool cmdP = g.Grid.HeightAt(e.X, e.Y) - g.Grid.HeightAt(p.X, p.Y) >= 2;
                if (!g.Grid.HasLineOfSight(e.X, e.Y, p.X, p.Y, cmdP)) continue;
                if (g.Grid.GetCover(p.X, p.Y, e.X, e.Y).Level > 0) continue;   // covered: hold the lane instead
                if (OddsFrom(g, e, e.X, e.Y, p).HitChance >= 65) { opp = true; break; }
            }
            // Lane anchor = the nearest non-VIP soldier (the asset doesn't trip reactions worth a plant;
            // denying the SQUAD's movement is the job). Only plant while the squad is within cone reach
            // (MaxRange + 2) — beyond that the generic loop advances it like anyone else.
            Unit anchor = null; int adist = int.MaxValue;
            foreach (var p in players)
            {
                if (p.IsVip) continue;
                int d = Util.ChebyDist(e.X, e.Y, p.X, p.Y);
                if (d < adist) { adist = d; anchor = p; }
            }
            if (!opp && anchor != null && adist <= e.Weapon.MaxRange + 2)
            {
                // pick a plant tile from reach, keeping the action to plant, scored SPOTTER-style;
                // a plant with no line of sight to the anchor holds nothing (the reaction runs
                // through CanTarget), so blind tiles are filtered, not merely penalised.
                (int x, int y) pt = (-1, -1); int ptCost = 0; float ptScore = float.NegativeInfinity;
                foreach (var (tx, ty, c) in reach)
                {
                    int acts = c <= e.MoveBudget ? (c == 0 ? 0 : 1) : 2;
                    if (acts >= 2) continue;                       // keep the action to plant
                    bool cmdT = g.Grid.HeightAt(tx, ty) - g.Grid.HeightAt(anchor.X, anchor.Y) >= 2;
                    if (!g.Grid.HasLineOfSight(tx, ty, anchor.X, anchor.Y, cmdT)) continue;
                    var cov = g.Grid.GetCover(tx, ty, anchor.X, anchor.Y);
                    int dn = Util.ChebyDist(tx, ty, anchor.X, anchor.Y);
                    float s = cov.Level * 16 + g.Grid.HeightAt(tx, ty) * 8
                              - Math.Abs(dn - 4) * 1.4f            // a lane is held at a short standoff
                              + Util.RandRange(0f, 2f);
                    if (dn <= 2) s -= 22;                          // never plant in shove/point-blank reach
                    if (s > ptScore) { ptScore = s; pt = (tx, ty); ptCost = c; }
                }
                if (pt.x >= 0)
                {
                    var bp = new EnemyPlan
                    {
                        Brace = true,
                        BraceDirX = Math.Sign(anchor.X - pt.x),
                        BraceDirY = Math.Sign(anchor.Y - pt.y),
                    };
                    if (pt != (e.X, e.Y))
                    {
                        bp.Path = g.Grid.ReconstructPath(cameFrom, e.X, e.Y, pt.x, pt.y);
                        bp.MoveActions = ptCost <= e.MoveBudget ? 1 : 2;
                    }
                    return bp;
                }
                // else: boxed out — fall through to the generic loop (never a dead turn)
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
        // W8 review: Routed == 0 gate — a BROKEN artillery piece does not calmly charge a strike;
        // it falls through and flees with the rest of its pod (see the MEDIC branch note).
        if (e.HasSiege && e.ChargeTurns == 0 && e.Routed == 0)
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
            int shootHit = 0;            // C2: the REAL hit chance of `shoot` from this tile
            // W2 THE OPPONENT ACTS — the AMMO GATE. `Ammo` appeared in this file at exactly two
            // lines (the PIKEMAN plant gate and the overwatch fallback), NEITHER of them here, so a
            // hostile with an empty weapon still planned a ShootTarget. A non-null ShootTarget then
            // suppressed the whole no-shot fallback block below, and Game.ActAfterMove's own
            // `e.Ammo > 0` gate refused the shot — the unit stood there having spent nothing. Measured
            // on this tree, counting only CONTESTED acts (at least one soldier still standing; an
            // all-downed board idles every hostile by design and must not be counted): 69 of 755
            // acts at n=16 campaigns and 39 of 1195 at n=32 were made on an empty weapon, and ~61%
            // of those produced no action at all in BOTH frames.
            if (actionsToReach <= 1 && (!Game.AiIdleFix || e.Ammo > 0))
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

                    if (val > bestHit) { bestHit = val; shoot = p; shootHit = odds.HitChance; }
                }
            }

            // cover quality at this tile vs the nearest player
            var cover = g.Grid.GetCover(tx, ty, nearest.X, nearest.Y);
            int distNearest = Util.ChebyDist(tx, ty, nearest.X, nearest.Y);
            float score = 0;
            // UNDERTOW W3: a ROUTING unit is panicking — a shot is a minor opportunistic bonus, NOT "king",
            // so the flee/distance terms below dominate and it actually breaks contact (it may still take a
            // wild potshot if one lines up).
            //
            // C2 THE OPPONENT DECLINES — the shot competes on its MERITS instead of on a constant.
            // The pre-C2 term was `100 + bestHit`, and every terrain term in this same function is
            // bounded well under ~64 (cover 36, height ~28, flank -25, fire -60, overwatch -26).
            // A flat +100 for "a shot exists" therefore DOMINATED the whole scorer: any tile with a
            // 3% shot outranked any tile without one, so the opponent always shot if it could see
            // anything, from anywhere, at any odds. It never declined, never repositioned for a
            // better angle, and its overwatch branch was measurably dead.
            //
            // The replacement weights the shot by the probability of actually connecting. Say what
            // that arithmetic IS, because "expected value" is not literally true and an earlier
            // version of this comment claimed it: `bestHit` is `odds.HitChance + bonuses`, so
            // `ShotSeat + bestHit * hit/100` expands to
            //
            //     18  +  hit^2/100  +  bonuses * hit/100
            //
            // — a hit-SQUARED term, not an expectation. A true EV term would weight only the
            // consequence of connecting (`bonuses`) by `hit`, and would not re-multiply the hit
            // chance by itself. The squaring is deliberate and kept: it makes the planner more
            // hit-greedy than an EV maximiser, which is what a fight this short wants (an EV
            // maximiser is indifferent between one 80% shot and four 20% shots; a soldier's HP bar
            // is not). But it is a WEIGHTING, not an expectation, and the honest name is the
            // arithmetic. `ShotSeat` is the option value of holding a line of fire at all, priced
            // at roughly one level of cover. Target SELECTION is untouched: the loop above still
            // ranks targets by `val` exactly as before.
            if (shoot != null)
                score += routing ? bestHit * 0.25f : ShotTileValue(bestHit, shootHit);
            score += cover.Level * 18;                           // value cover
            score += g.Grid.HeightAt(tx, ty) * 14;               // seize the high ground
            if (cover.Flanked) score -= 25;
            score -= actionsToReach * 6;                         // prefer cheaper moves slightly
            if (g.Grid.IsFire(tx, ty)) score -= 60;              // never voluntarily stand in fire (hazards)
            // C4 / MAGMA — the enemy must not park on a thermal vent. The penalty is deliberately
            // SMALLER than fire's 60 and smaller than the 100+bestHit a shot is worth: a vent is a
            // price, not a wall, so a hostile that can only reach a killing angle by standing on
            // the crack will still take it and eat the burn. That is the same bet the player is
            // offered. (The MOVEMENT half of the toll is already in Grid.CostMap, so `reach` here
            // has priced the crossing before this loop ever sees the tile; this term is only about
            // ENDING the move there.)
            if (g.Grid.IsVent(tx, ty)) score -= 34;
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
                bestShotHit = shootHit;
            }
            // W2: best tile that costs BOTH actions to reach (same score, no re-scoring, no new draw).
            if (actionsToReach >= 2 && score > bestDashScore) { bestDashScore = score; bestDash = (tx, ty); }
        }

        // build path
        if (bestTile != (e.X, e.Y))
        {
            plan.Path = g.Grid.ReconstructPath(cameFrom, e.X, e.Y, bestTile.x, bestTile.y);
            plan.MoveActions = bestCost <= e.MoveBudget ? 1 : 2;
        }

        plan.ShootTarget = bestShotTarget;

        // C2: the shot ON THE TABLE from the tile this plan chose — captured here, BEFORE the
        // sap/grenade/item/shove blocks below can claim the action, so the decision mix can tell
        // "the opponent declined it" from "something better came up". UNCONDITIONAL: one extra
        // ComputeOdds + ExpectedDamage per PLAN (the tile loop above already runs hundreds), and
        // gating it on the dial made the pre-C2 arm report "no shot on the table" for a shot that
        // was plainly there. PURE: ComputeOdds has been side-effect free since W9 and
        // ExpectedDamage takes no RNG draw, so this cannot move a CRN-paired world (proven by the
        // C2 R0diag chunks, which diff 2750 aggregate fields to empty).
        ShotOdds shotOdds = default;     // the odds behind ShotHit/ShotExp; reused by the gate below
        if (bestShotTarget != null)
        {
            shotOdds = OddsFrom(g, e, bestTile.x, bestTile.y, bestShotTarget);
            plan.ShotHit = bestShotHit;
            plan.ShotExp = Combat.ExpectedDamage(bestShotTarget, shotOdds);
        }

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
                foreach (var p in players)   // FUL-7: the filtered list — never shove a downed body
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

        // ── C2 THE OPPONENT DECLINES — the decision itself ───────────────────────────────
        // Everything above chose WHERE to stand. This chooses whether the shot from there is
        // worth the action, judged against what else the action can buy from that same tile.
        //
        // Dropping ShootTarget hands the unit straight to the (W2-hardened) fallback block
        // below, which ALWAYS assigns overwatch / hunker / reload / dash. So a decline can
        // never produce a dead turn: the no-idle invariant is inherited structurally rather
        // than re-argued here, and SIGHTLINE_AIIDLETEST still covers it.
        //
        // Ordered AFTER the sap/grenade/item/shove blocks on purpose: those already price
        // themselves against the real shot (the grenade block reads its hit chance), so they
        // must see the shot the planner actually had, not a declined null.
        if (Game.AiDecline && plan.ShootTarget != null && !routing
            && !plan.Grenade && plan.SapTile == null && !plan.UseItem && plan.ShoveTarget == null
            && plan.RelockTile == null && plan.HealTarget == null && !plan.Brace
            && plan.SiegeCharge == null && plan.MoveActions < 2 && !NeverDeclines(e))
        {
            // The alternatives, read from the tile the unit will actually be standing on and
            // gated exactly as the fallback block gates them — a bar the exec would refuse is
            // not an alternative. (W2's lesson: plan what the exec will accept.)
            bool cmdW = g.Grid.HeightAt(bestTile.x, bestTile.y) - g.Grid.HeightAt(nearest.X, nearest.Y) >= 2;
            bool canWatch = e.Ammo > 0 && !e.HasStatus(StatusKind.Disoriented)
                            && g.Grid.HasLineOfSight(bestTile.x, bestTile.y, nearest.X, nearest.Y, cmdW);
            bool canDig = g.Grid.GetCover(bestTile.x, bestTile.y, nearest.X, nearest.Y).Level > 0;
            float bar = Math.Max(canWatch ? DeclineWatchRatio : 0f, canDig ? DeclineDigRatio : 0f);
            // ...raised by the guns already trained on this tile (see DeclineThreatScale). Range
            // and line of sight only — no ComputeOdds, so this is a handful of Bresenham walks
            // once per plan, not per reachable tile.
            int guns = 0;
            foreach (var p in players)
                if (p.Ammo > 0 && Util.TileDist(p.X, p.Y, bestTile.x, bestTile.y) <= p.Weapon.MaxRange
                    && g.Grid.HasLineOfSight(p.X, p.Y, bestTile.x, bestTile.y)) guns++;
            bar *= 1f + DeclineThreatScale * Math.Min(guns, DeclineThreatCap);
            // The reference: this same shot with the target's cover taken away. Built from
            // `shotOdds`, the odds already computed above for the decision mix — the gate adds no
            // ComputeOdds of its own (the plan pays exactly one, whether or not it declines).
            float expOpen = Combat.ExpectedDamage(plan.ShootTarget, Combat.AsIfExposed(shotOdds));
            float worth = plan.ShotExp
                        * (plan.ShootTarget.Hp <= e.Weapon.DmgMax ? FinishPress : 1f);
            if (bar > 0f && expOpen > 0f && worth < DeclineAbsKeep && worth < bar * expOpen)
            {
                plan.Declined = true;
                plan.ShootTarget = null;
                // Under two or more guns, with cover to hand, the freed action buys SURVIVAL.
                // A watch is an offer to trade next turn; hunkering (-25 to be hit, no crit) is a
                // refusal to trade at all, and refusing is the right answer in a kill box. This is
                // the ONE place the no-shot fallback's overwatch-first order is overridden, and it
                // is overridden only on a tile that actually has cover.
                plan.DeclineDigIn = guns >= 2 && canDig;
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
                // W2 THE OPPONENT ACTS — the AMMO ECONOMY (docs/DESIGN.md §5.1). A dry gun is the
                // FIRST thing worth an action: every other branch here either needs ammo (overwatch)
                // or is a way of surviving until the unit has some. Ordered ahead of hunker on
                // purpose — a hostile digging in behind cover with an empty weapon is the statue
                // this wave exists to remove.
                if (Game.AiIdleFix && e.Ammo <= 0 && e.Weapon.Clip > 0) plan.Reload = true;
                // W2: mirror ActAfterMove's own overwatch gate. A Disoriented unit's watch is refused
                // by the exec, and the plan had no way of knowing — so it planned a watch, the exec
                // declined it, and the unit stood still. Plan what the exec will actually accept.
                // C2: a decline taken under two or more guns digs in instead of watching.
                else if (plan.DeclineDigIn && coverHere.Level > 0) plan.Hunker = true;
                else if (sees && e.Ammo > 0 && !routing
                         && (!Game.AiIdleFix || !e.HasStatus(StatusKind.Disoriented))) plan.Overwatch = true;
                else if (coverHere.Level > 0) plan.Hunker = true;
                // W2 TERMINAL ELSE. `if (watch) ... else if (cover) ...` with no final branch meant a
                // hostile that had lost line of sight to the squad AND was standing on open floor got
                // neither, and froze in the open holding a live action. This is the SMALLER of the two
                // contested causes and the wave's first write-up had it backwards: of the pre-wave
                // contested idles it accounts for 5 of 47 at n=16 campaigns and 21 of 45 at n=32 (the
                // rest are the dry weapon above). The split is not resolvable at these samples — both
                // causes are real, neither dominates.
                // It spends that action on GROUND — re-targeting the move at the best tile the same
                // per-tile scorer ranked among those needing the FULL two-action budget — but ONLY
                // when that tile is genuinely no worse.
                //
                // REVIEW FIX, and the first version had this exactly backwards. This arm is only
                // reachable when `spent < 2`, i.e. `bestTile` cost 0 or 1 actions — and `bestTile`
                // is the argmax over ALL tiles including the two-action ones, so `bestScore >=
                // bestDashScore` BY CONSTRUCTION. The unconditional dash therefore moved the unit
                // to a tile its own scorer ranked LOWER, every single time: measured 13 of 13
                // dashes strictly worse, mean -12.7 points, against terrain terms bounded under
                // ~64. The comment claimed the unit "closes, withdraws or seeks cover exactly as
                // its archetype terms already say it should"; its archetype terms said the opposite.
                //
                // The comparison has to be MOVE-COST-NEUTRAL to mean anything here. Every tile's
                // score carries `-actionsToReach * 6`, a term that exists to express "prefer the
                // cheaper move, slightly" — a preference that is meaningless in this branch, where
                // the spare action's only alternative use is nothing at all. So refund the
                // differential (the dash paid 12, `bestTile` paid `spent * 6`) and require the dash
                // to win on the terms that actually differ. If it does not, dig in instead: HUNKERED
                // is a real mechanical state (-25 to hit against it, no crit) and the player's own
                // HUNKER has no cover requirement either, so it is symmetric, not a consolation prize.
                else if (Game.AiIdleFix && bestDash.x >= 0 && bestDash != bestTile
                         && DashWins(bestDashScore, bestScore, spent))
                {
                    plan.Path = g.Grid.ReconstructPath(cameFrom, e.X, e.Y, bestDash.x, bestDash.y);
                    plan.MoveActions = 2;
                    plan.IdleRepair = true;
                }
                // no two-action tile exists, or none that survives the comparison above.
                else if (Game.AiIdleFix) { plan.Hunker = true; plan.IdleRepair = true; }
            }
        }

        return plan;
    }

    /// W2 (review fix): is the best FULL-BUDGET tile worth the spare action, judged move-cost-
    /// neutrally? Both scores carry a `-actionsToReach * 6` term; the dash paid 12 and the chosen
    /// tile paid `spent * 6`. That term prices an action which, in this branch, has no other use —
    /// so refund the difference and compare what is left. Ties go to the dash (a unit with nothing
    /// else to do should take the ground); anything worse digs in where it stands.
    /// `DashProbe` is harness-only and ALWAYS null in normal play.
    public static Action<float, float, int, bool> DashProbe;
    internal const float MoveCostPerAction = 6f;
    static bool DashWins(float dashScore, float bestScore, int spent)
    {
        bool win = dashScore + (2 - spent) * MoveCostPerAction >= bestScore;
        DashProbe?.Invoke(dashScore, bestScore, spent, win);
        return win;
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
            if (p.IsVip || p.Downed) continue;   // FUL-7: a downed body holds no lane to screen
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
                if (!q.IsVip && !q.Downed && Util.ChebyDist(p.X, p.Y, q.X, q.Y) <= SmokeAnim.Radius) hits++;
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
            if (p.Downed) continue;   // FUL-7: never AIM at a downed body (AoE stays blind, not deliberate)
            if (Util.TileDist(fx, fy, p.X, p.Y) > Game.ItemRange) continue;
            int hits = 0, allies = 0;
            foreach (var q in g.AlivePlayers()) if (!q.Downed && Util.ChebyDist(p.X, p.Y, q.X, q.Y) <= FlashAnim.Radius) hits++;
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
            if (p.IsVip || p.Downed) continue;                   // don't waste the shell on the asset / a downed body (FUL-7)
            int hits = 0, allies = 0;
            foreach (var q in g.AlivePlayers())
                if (!q.Downed && Util.ChebyDist(p.X, p.Y, q.X, q.Y) <= Game.SiegeRadius) hits++;
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
            if (p.Downed) continue;   // FUL-7: never AIM at a downed body (a blast aimed at standing soldiers still catches it — AoE stays blind)
            if (Util.TileDist(fx, fy, p.X, p.Y) > Game.GrenadeRange) continue;
            if (!g.Grid.HasLineOfSight(fx, fy, p.X, p.Y)) continue;   // can't blind-lob over walls / through smoke
            if (Util.ChebyDist(fx, fy, p.X, p.Y) <= GrenadeAnim.Radius) continue;  // don't catch the thrower in its own blast
            int hits = 0, allies = 0;
            foreach (var q in g.AlivePlayers()) if (!q.Downed && Util.ChebyDist(p.X, p.Y, q.X, q.Y) <= GrenadeAnim.Radius) hits++;
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
