using System;
using System.Collections.Generic;

namespace Sightline;

/// Builds battlefields and squads. The player squad persists across a run
/// (see Run); each mission regenerates the map + a scaled hostile force.
public static class Mission
{
    // Staggered deployment footprint: four soldiers across cols 0-3 in a loose wedge
    // (upper-forward / back-left / lower-forward / back-right), plus a 5th slot for the
    // VIP/captive near squad centre. Spread deliberately avoids a single-column firing-line
    // while staying in the left third (cols 0-3) so standoff to the mid-field screen holds.
    static readonly (int x, int y)[] PlayerSpawns = { (2, 2), (0, 5), (3, 8), (1, 9), (2, 5) };

    // Per-pod column offsets for enemy spawns: vary across cols 14-17 so the right side
    // doesn't mirror a parallel firing line. Indexed by pod id (i/2), cycling if more pods.
    static readonly int[] EnemyPodColOffset = { 1, 3, 0, 2, 1, 3 };

    // test hook (SIGHTLINE_MAP): force a specific authored layout index; -1 = normal roll
    public static int ForcedLayout = -1;

    // Soft biome->layout affinity: each biome index (matching Biome.All order —
    // STEEL=0 ARID=1 TUNDRA=2 VERDANT=3 ASH=4 VOID=5 NEON=6 MAGMA=7) hints at a preferred
    // arena index. When an authored map is rolled, there is a 50% chance to pick the hinted
    // layout and a 50% chance to pick randomly — keeping variety while nudging theme.
    // -1 means no preference (always picks randomly). This is SOFT: ForcedLayout
    // overrides it completely, and the connectivity guard can still fall back to
    // procedural if a hinted layout fails (though the arenas are designed to pass).
    // Keep this array length-aligned with Biome.All (one entry per biome) so the two
    // newest biomes also theme; PickLayout falls back to a random arena past the end.
    static readonly int[] BiomeLayoutHint =
    {
        11,  // STEEL   → BASTION (industrial fortress, tier-2 keep)
        13,  // ARID    → SPUR (sun-baked diagonal high-ground spine)
        12,  // TUNDRA  → CHASM (a frozen ravine split by a cover river)
        10,  // VERDANT → THICKET (dense organic cover clusters)
        14,  // ASH     → HOOK (a ruined outpost with an asymmetric flank)
        9,   // VOID    → RUINS (open eerie arena, long sightlines)
        15,  // NEON    → GRID (orthogonal server-room rack lattice)
        16,  // MAGMA   → FORGE (commanding tier-2 foundry platform)
    };

    /// The four starting soldiers for a fresh run.
    public static List<Unit> NewRunSquad()
    {
        var squad = new List<Unit>();
        squad.Add(MakeSoldier("VEGA",   "ASSAULT",      WeaponKind.Rifle,   8, 70, 7));
        squad.Add(MakeSoldier("KRESS",  "RANGER",       WeaponKind.Shotgun, 7, 66, 8));
        squad.Add(MakeSoldier("NOX",    "SHARPSHOOTER", WeaponKind.Sniper,  6, 76, 6));
        squad.Add(MakeSoldier("BISHOP", "GUNNER",       WeaponKind.Lmg,    10, 62, 6));
        return squad;
    }

    /// Lay out a mission: regenerate terrain, place the (persistent) players,
    /// and spawn a hostile force scaled by missionNum.
    public static void Build(Grid grid, List<Unit> players, List<Unit> enemies, int missionNum,
                             List<(int x, int y)> evac = null, (int x, int y)? terminal = null,
                             int enemyDelta = 0, int statDelta = 0, List<(int x, int y)> sabotage = null)
    {
        enemies.Clear();
        grid.ClearSmoke();
        for (int x = 0; x < grid.W; x++)
            for (int y = 0; y < grid.H; y++)
            {
                grid.Tiles[x, y] = TileType.Floor;
                grid.Height[x, y] = 0;
            }

        // place players at left spawns, refresh per-mission state (HP persists)
        for (int i = 0; i < players.Count && i < PlayerSpawns.Length; i++)
        {
            var u = players[i];
            // the VIP/captive always takes the dedicated 5th slot, even when the squad is
            // short-handed (benched soldier) and the VIP would otherwise land on a soldier's
            // lower index and spawn far from the squad/extraction (review Major). Rescue
            // re-seats its captive at centre after Build, so this only matters for Escort.
            var sp = u.IsVip ? PlayerSpawns[PlayerSpawns.Length - 1] : PlayerSpawns[i];
            u.X = sp.x;
            u.Y = sp.y;
            u.Ammo = u.Weapon.Clip;
            u.Grenades = 1 + u.BonusGrenades + (u.HasPerk(Perk.Bandolier) ? 1 : 0);  // refill (+cache +Bandolier)
            u.AbilityCharge = 1 + (u.HasPerk(Perk.Adrenal) ? 1 : 0);// refill (+Adrenal)
            u.ItemCharge = u.Item != ItemKind.None ? 1 : 0;        // utility item: 1 charge/mission
            u.Suppress = 0;
            u.OnOverwatch = false;
            u.Hunkered = false;
            u.Recoil = System.Numerics.Vector2.Zero;
            u.Flash = 0;
        }

        var evacSet = new HashSet<(int, int)>(evac ?? new List<(int, int)>());
        SpawnEnemies(grid, enemies, missionNum, evacSet, enemyDelta, statDelta);

        var occupied = new HashSet<(int, int)>();
        foreach (var u in players) occupied.Add((u.X, u.Y));
        foreach (var u in enemies) occupied.Add((u.X, u.Y));
        foreach (var t in evacSet) occupied.Add(t);   // keep the extraction zone clear of cover
        if (terminal.HasValue)                         // keep the terminal + its ring open
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    occupied.Add((terminal.Value.x + dx, terminal.Value.y + dy));
        if (sabotage != null)                          // keep each sabotage site + its ring open
            foreach (var s in sabotage)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        occupied.Add((s.x + dx, s.y + dy));

        // Either lay down a hand-authored arena (with a connectivity guard) or fall
        // back to the procedural generator. Both keep reserved tiles open.
        bool authored;
        if (ForcedLayout >= 0 && ForcedLayout < Maps.Layouts.Length)
            authored = TryApplyLayout(grid, occupied, players, enemies, evacSet, terminal, Maps.Layouts[ForcedLayout], sabotage);
        else
            authored = Util.Roll(55) &&
                TryApplyLayout(grid, occupied, players, enemies, evacSet, terminal, PickLayout(missionNum), sabotage);
        if (!authored)
            BuildProcedural(grid, occupied, evacSet, missionNum);

        // protective cover beside each soldier and hostile (both layout paths)
        foreach (var u in players) TryCover(grid, occupied, u.X + 1, u.Y, TileType.LowCover);
        foreach (var u in enemies) TryCover(grid, occupied, u.X - 1, u.Y, TileType.HighCover);

        // 4.2 safety net: the denser mid-field cover (+ sprinkles + protective cover) must
        // never wall a hostile or objective off from the squad — carve a lane if it did.
        EnsureConnectivity(grid, players, enemies, evacSet, terminal, sabotage);

        grid.ResetCoverHp();   // charge every cover tile to full now the terrain is final (3.6)

        foreach (var u in players) u.SyncPos();
        foreach (var u in enemies) u.SyncPos();
    }

    // ---- Procedural map generation -----------------------------------------
    // The procedural path picks one of several mid-field cover ARCHETYPES at random so
    // generated missions don't all look the same. Every archetype preserves the 4.2
    // encounter-geometry intent: sightline-blocking HIGH cover concentrated in the
    // mid-field (cols ~6-12), at least one deliberately OPEN "risky direct" lane so the
    // board stays traversable, and no fully-walled column. Plateaus (incl. a tier-2
    // redoubt on later missions) are shared across archetypes, and `EnsureConnectivity`
    // (run by Build afterwards) is the final net should sprinkles/protective cover ever
    // pinch a path. Spawn columns (0-3) and enemy columns (14-17) are left clear.

    static void BuildProcedural(Grid grid, HashSet<(int, int)> occupied,
                                HashSet<(int, int)> evac, int missionNum)
    {
        // contested high ground: raised plateaus in the mid-field (more on later missions).
        // Shared by all archetypes so elevation play is always present.
        RaisePlateau(grid, evac, 7, 3, 2, 2);
        RaisePlateau(grid, evac, 11, 7, 2, 2);
        if (missionNum >= 3) RaisePlateau(grid, evac, Util.RandInt(6, 11), Util.RandInt(1, 8), 2, 2);
        // a commanding tier-2 redoubt appears on later missions (sees over high cover)
        if (missionNum >= 4) RaisePlateau(grid, evac, Util.RandInt(7, 10), Util.RandInt(3, 6), 2, 2, 2);

        // pick a mid-field cover archetype (variety); each leaves an open lane + no walled column
        switch (Util.RandInt(0, 3))
        {
            case 0:  ArchScreen(grid, occupied);        break;   // the 4.2 staggered screen
            case 1:  ArchRedoubt(grid, occupied);       break;   // a central bunker, flank lanes
            case 2:  ArchTwinCorridors(grid, occupied); break;   // two cover spines, a centre gap
            default: ArchDiagonalWall(grid, occupied);  break;   // a slanted wall with a breach
        }

        // random crates (a touch more clutter on later missions; biased toward LoS-blocking high cover)
        Sprinkle(grid, occupied, 14 + Math.Min(6, missionNum));
    }

    /// Archetype 0 — the original 4.2 staggered mid-field SCREEN of high cover: breaks the
    /// long cross-board sightlines so the squad can advance into the midfield under cover
    /// before tripping a pod. No column is fully walled; row 5 is the one open risky lane.
    static void ArchScreen(Grid grid, HashSet<(int, int)> occupied)
    {
        var screen = new (int x, int y)[]
        {
            (7, 1), (7, 2), (7, 3),   (8, 6), (8, 7), (8, 8),
            (9, 0), (9, 1), (9, 9), (9, 10),   (10, 3), (10, 4), (10, 7), (10, 8),
            (11, 1), (11, 2),
        };
        foreach (var (sx, sy) in screen) PlaceCover(grid, occupied, sx, sy, TileType.HighCover);
        // low cover flanking the open central lane, for cover-fighting on the direct route
        PlaceCover(grid, occupied, 6, 5, TileType.LowCover);
        PlaceCover(grid, occupied, 12, 5, TileType.LowCover);
    }

    /// Archetype 1 — a central REDOUBT: a compact high-cover bunker mid-board with a low-cover
    /// apron, leaving wide flanking lanes top and bottom. Rewards a flank rather than a frontal
    /// push; the bunker breaks the central sightline while the rims stay open.
    static void ArchRedoubt(Grid grid, HashSet<(int, int)> occupied)
    {
        // high-cover ring of a hollow bunker around the mid-field (rows 3-7, cols 8-10).
        // The WEST face at row 5 is left open as a doorway, so the interior (and a centre
        // terminal/captive, if the objective seats one there) stays reachable without the
        // connectivity net having to carve in.
        var ring = new (int x, int y)[]
        {
            (8, 3), (9, 3), (10, 3),
            (8, 4),                 (10, 4),
                                    (10, 5),   // west doorway (8,5) open; east slit (10,5)
            (8, 6),                 (10, 6),
            (8, 7), (9, 7), (10, 7),
        };
        foreach (var (sx, sy) in ring) PlaceCover(grid, occupied, sx, sy, TileType.HighCover);
        // low-cover apron on the approaches (covered fighting positions outside the bunker)
        PlaceCover(grid, occupied, 6, 4, TileType.LowCover);
        PlaceCover(grid, occupied, 6, 6, TileType.LowCover);
        PlaceCover(grid, occupied, 12, 4, TileType.LowCover);
        PlaceCover(grid, occupied, 12, 6, TileType.LowCover);
        // top/bottom flanking lanes (rows 0-1 and 9-10) are deliberately left open.
    }

    /// Archetype 2 — TWIN CORRIDORS: two vertical high-cover spines (a forward and a rear
    /// staggered wall), each gapped so a soldier can slip through, with an open central seam
    /// between them. Creates layered cover and channels movement into the gaps.
    static void ArchTwinCorridors(Grid grid, HashSet<(int, int)> occupied)
    {
        // forward spine at col 7, gap at rows 4-5 (the open seam)
        for (int y = 0; y < grid.H; y++)
            if (y < 4 || y > 5) PlaceCover(grid, occupied, 7, y, TileType.HighCover);
        // rear spine at col 11, gap at rows 5-6 (offset from the forward gap -> staggered)
        for (int y = 0; y < grid.H; y++)
            if (y < 5 || y > 6) PlaceCover(grid, occupied, 11, y, TileType.HighCover);
        // low cover bracketing the central seam (cover-fight in the gap between the spines)
        PlaceCover(grid, occupied, 9, 4, TileType.LowCover);
        PlaceCover(grid, occupied, 9, 6, TileType.LowCover);
    }

    /// Archetype 3 — a DIAGONAL WALL of high cover slashing across the mid-field with a single
    /// breach gap, plus a low-cover counter-diagonal. Strong sightline break on a slant; the
    /// breach is the contested crossing, and the wall's ends leave the rims open.
    static void ArchDiagonalWall(Grid grid, HashSet<(int, int)> occupied)
    {
        // a slanted high-cover wall from upper-mid to lower-mid, with a one-tile breach
        var wall = new (int x, int y)[]
        {
            (7, 1), (7, 2),
            (8, 3), (8, 4),
            (9, 5),                 // breach is the gap just below here (row 6 left open)
            (10, 7), (10, 8),
            (11, 9),
        };
        foreach (var (sx, sy) in wall) PlaceCover(grid, occupied, sx, sy, TileType.HighCover);
        // a short low-cover counter-diagonal giving the attacker covered footing to the breach
        PlaceCover(grid, occupied, 6, 6, TileType.LowCover);
        PlaceCover(grid, occupied, 9, 6, TileType.LowCover);   // flanks the breach, doesn't seal it
        PlaceCover(grid, occupied, 12, 6, TileType.LowCover);
    }

    /// Place a cover tile only on an unreserved, currently-empty floor tile (and mark it
    /// occupied). The shared primitive for all procedural archetypes.
    static void PlaceCover(Grid grid, HashSet<(int, int)> occupied, int x, int y, TileType t)
    {
        if (!grid.InBounds(x, y) || occupied.Contains((x, y)) || grid.Tiles[x, y] != TileType.Floor) return;
        grid.Tiles[x, y] = t;
        occupied.Add((x, y));
    }

    /// Scatter `count` random crates across the mid-board, biased toward LoS-blocking high cover.
    static void Sprinkle(Grid grid, HashSet<(int, int)> occupied, int count)
    {
        int guard = 0;
        while (count > 0 && guard++ < 500)
        {
            int x = Util.RandInt(3, grid.W - 4);
            int y = Util.RandInt(0, grid.H - 1);
            if (occupied.Contains((x, y)) || grid.Tiles[x, y] != TileType.Floor) continue;
            grid.Tiles[x, y] = Util.Roll(45) ? TileType.LowCover : TileType.HighCover;
            occupied.Add((x, y));
            count--;
        }
    }

    /// Stamp a hand-authored template onto the grid, then verify every spawn, the
    /// evac zone and the terminal stay mutually reachable over walkable terrain.
    /// Reverts and returns false if the layout is malformed or would wall anyone off.
    static bool TryApplyLayout(Grid g, HashSet<(int, int)> occupied, List<Unit> players,
                               List<Unit> enemies, HashSet<(int, int)> evac,
                               (int x, int y)? terminal, string[] tpl, List<(int x, int y)> sabotage)
    {
        if (tpl.Length != g.H) return false;
        for (int y = 0; y < g.H; y++) if (tpl[y].Length != g.W) return false;

        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                if (occupied.Contains((x, y))) continue;   // reserved -> stays open floor
                switch (tpl[y][x])
                {
                    case 'o': g.Tiles[x, y] = TileType.LowCover; break;
                    case '#': g.Tiles[x, y] = TileType.HighCover; break;
                    case '^': g.Height[x, y] = 1; break;   // walkable raised plateau (tier 1)
                    case '=': g.Height[x, y] = 2; break;   // walkable raised plateau (tier 2)
                    default:  break;                        // '.' open floor
                }
            }

        // connectivity: flood from the first soldier across walkable tiles (cover = wall)
        var cost = g.CostMap(players[0].X, players[0].Y, (x, y) => false, out _, 9999);
        bool Reachable(int x, int y) => g.InBounds(x, y) && cost[x, y] >= 0;

        bool ok = true;
        foreach (var u in players) if (!Reachable(u.X, u.Y)) ok = false;
        foreach (var u in enemies) if (!Reachable(u.X, u.Y)) ok = false;
        foreach (var t in evac) if (!Reachable(t.Item1, t.Item2)) ok = false;
        if (terminal.HasValue && !Reachable(terminal.Value.x, terminal.Value.y)) ok = false;
        if (sabotage != null) foreach (var s in sabotage) if (!Reachable(s.x, s.y)) ok = false;

        if (!ok)   // revert to a clean slate so the procedural path can run
        {
            for (int y = 0; y < g.H; y++)
                for (int x = 0; x < g.W; x++)
                    if (!occupied.Contains((x, y))) { g.Tiles[x, y] = TileType.Floor; g.Height[x, y] = 0; }
            return false;
        }
        return true;
    }

    static void SpawnEnemies(Grid grid, List<Unit> enemies, int n, HashSet<(int, int)> evac,
                             int enemyDelta = 0, int statDelta = 0)
    {
        // Headcount cap raised 10 -> 12 so the top-Heat "+enemy" rungs aren't silently wasted
        // (the +1/+1 from RELENTLESS/OVERWHELMING used to clip at 10 on later missions). 12 still
        // fits easily: spawns occupy cols 14-17 over grid.H rows (44 slots) and the collision loop
        // below relocates any overlap.
        int count = Math.Clamp(4 + n + enemyDelta, 3, 12);   // deployment-card + Heat modifier
        int bump = Math.Max(0, n - 1 + statDelta);           // stat growth per mission +/- card
        // Final mission (the WARLORD boss): de-stack the force. This was the core of the ~90% m6
        // loss wall -- the squad cleared m1-5 (m5 often wins ~100%, partly because it isn't always
        // forced Eliminate) then got alpha-struck on m6's forced full-clear. The compounding cause:
        // the BOSS campaign node's card adds EnemyDelta +2 / StatDelta +1 (Run.CardForNode) ON TOP
        // of the named boss itself, so the body count saturates the cap at 12 and every supporter
        // also gets +1 stat -- a double-counted "elite" mission. The boss IS the elite, so here we
        // (1) cut the supporting force HARD (boss is one slot, i==0) and (2) strip the rank-and-file
        // stat bump back to the plain per-mission growth (the boss keeps its own explicit stats set
        // below). Net at heat 0: 8 hostiles incl. the boss (was 12), supporters at +5 not +6.
        if (n >= Run.MaxMissions)
        {
            count = Math.Max(5, count - 4);
            bump = Math.Max(0, n - 1);                       // drop the boss-card/heat StatDelta for the screen
        }
        var rows = new List<int>();
        for (int y = 0; y < grid.H; y++) rows.Add(y);
        // shuffle rows
        for (int i = rows.Count - 1; i > 0; i--) { int j = Util.RandInt(0, i); (rows[i], rows[j]) = (rows[j], rows[i]); }

        var used = new HashSet<(int, int)>();
        for (int i = 0; i < count; i++)
        {
            int y = rows[i % rows.Count];
            int podId = i / 2;
            int colOff = EnemyPodColOffset[podId % EnemyPodColOffset.Length];
            int x = grid.W - 1 - colOff;              // stagger across cols 14-17
            int guard = 0;
            while ((used.Contains((x, y)) || evac.Contains((x, y))) && guard++ < 30)
            { y = Util.RandInt(0, grid.H - 1); x = grid.W - 2 - Util.RandInt(0, 2); }
            used.Add((x, y));

            bool finalMission = n >= Run.MaxMissions;
            bool midBoss = !finalMission && i == 0 && (n == 3 || n == 5);   // recurring named elite
            float r = Util.RandF();
            Unit e;
            if (finalMission && i == 0)         // capstone elite (named boss)
                // HP 20+2n -> 14+n, aim 72 -> 68: mission-6 was a ~90%-loss wall for a competent
                // squad (it cleared m1-5 then died on the boss). At n=6 this is 20 HP (was 32) and
                // 68 aim -- still the toughest single unit in the game (a mid-boss is 24 HP) but no
                // longer an unkillable, never-misses brick. Grenade count is also trimmed 2 -> 1
                // below, and the supporting force is lighter (see the count adjustment above).
                e = MakeHostile("WARLORD", "ELITE", WeaponKind.Lmg, 14 + n, 68, 6, x, y);
            else if (midBoss)                   // mid-campaign elite (lighter than the WARLORD)
                e = MakeHostile(n == 3 ? "BREAKER" : "WARDEN", "ELITE", WeaponKind.Lmg, 14 + n * 2, 68, 6, x, y);
            else                                // a tier-appropriate rank-and-file archetype
                e = SelectArchetype(n, r, bump, x, y);
            if (e.Cls != "ELITE") e.Aim = Math.Min(82, e.Aim);
            // grenades: bruisers + the elite always; some others from mission 2 on.
            // MORTAR already carries a deep frag pouch (set in SelectArchetype) — never overwrite it.
            // ELITE grenades: mid-bosses (BREAKER/WARDEN on m3/m5, which already win at high
            // rates) keep 2; the final WARLORD gets 1 -- two frags from the boss was a big part
            // of the m6 wall (it could AoE the whole squad before they closed). midBoss==true
            // only for the m3/m5 named elites; the final boss is finalMission && i==0.
            if (e.Cls == "ELITE") e.Grenades = midBoss ? 2 : 1;
            else if (e.Cls == "MORTAR") { /* keep MORTAR's 2-3 grenades from SelectArchetype */ }
            else if (n >= 2 && e.Cls != "MEDIC" && e.Cls != "SAPPER" && (e.Cls == "BRUISER" || Util.Roll(22))) e.Grenades = 1;
            // utility items (S2-B): snipers/scouts carry smoke to cover their movement;
            // some grunts get smoke from mission 3+. Flash given to berserkers (mission 3+)
            // to disorient the squad before charging. Never given to ELITE/MEDIC/TURRET/
            // DRONE/SHIELD/SAPPER (they each have a dedicated role already).
            if (e.Cls == "SNIPER" || e.Cls == "SCOUT")
                { e.EnemyItem = ItemKind.Smoke; e.ItemCharge = 1; }
            else if (n >= 3 && e.Cls == "BERSERKER")
                { e.EnemyItem = ItemKind.Flash; e.ItemCharge = 1; }
            else if (n >= 3 && e.Cls == "GRUNT" && Util.Roll(18))
                { e.EnemyItem = ItemKind.Smoke; e.ItemCharge = 1; }
            e.Alert = AlertLevel.Unaware;  // dormant until sighted (escalates via 4.3 tiers)
            e.PodId = i / 2;               // pods of ~2
            enemies.Add(e);
        }
    }

    /// Pick a rank-and-file hostile archetype for mission tier `n` from a uniform roll `r` in
    /// [0,1). Each mission TIER owns an explicit, non-overlapping set of probability windows so
    /// the composition reads deliberately — no archetype dominates a tier as an accidental
    /// fall-through (the old single cascade leaked the whole 0.49-0.85 band onto the BRUISER on
    /// mission 2, because every n>=3 branch was skipped). Archetype STATS are unchanged; only the
    /// gating/order moved. First-appearance tiers are preserved:
    ///   - Mission 1  (basic force):   SCOUT / GRUNT only.
    ///   - Mission 2  (light skirmish): SNIPER, DRONE, HUNTER, BRUISER, SCOUT, GRUNT — balanced,
    ///                                  none over ~20% (fixes the m2 BRUISER-dominance leak).
    ///   - Missions 3+ (full roster):  the complete pool incl. TURRET/BERSERKER/SHIELD/SAPPER/
    ///                                  MORTAR/MEDIC, weighted toward variety.
    /// Boss / mid-boss slots (i == 0 on the final / m3 / m5) are handled by the caller, not here.
    static Unit SelectArchetype(int n, float r, int bump, int x, int y)
    {
        if (n <= 1)   // MISSION 1 — basic force: a scout screen + grunts, nothing special.
            return r < 0.60f
                ? MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y)
                : MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);

        if (n == 2)   // MISSION 2 — light skirmishers (each ~15-20%; deliberately no dominant type).
        {
            if (r < 0.18f) return MakeHostile("VIPER", "SNIPER", WeaponKind.Sniper, 4 + bump, 62 + bump, 5, x, y); // 18% marksman
            if (r < 0.35f) return MakeHostile("WASP", "DRONE", WeaponKind.Smg, 3 + bump, 60 + bump, 7, x, y);      // 17% drone
            if (r < 0.50f) return MakeHostile("JACKAL", "HUNTER", WeaponKind.Smg, 5 + bump, 60 + bump, 9, x, y);   // 15% flanker
            if (r < 0.65f) return MakeHostile("OGRE", "BRUISER", WeaponKind.Lmg, 9 + bump * 2, 56 + bump, 5, x, y);// 15% bruiser
            if (r < 0.85f) return MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);   // 20% scout
            return MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);                 // 15% grunt
        }

        // MISSIONS 3+ — the full roster is available. Windows tuned for variety: every archetype
        // appears, with the specialists (TURRET/BERSERKER/SHIELD/SAPPER/MORTAR/MEDIC/SPOTTER)
        // collectively the bulk and the plain SCOUT/GRUNT now a small remainder (they carried too
        // much before). SPOTTER is a force-multiplier (see Ai.Plan): low priority body count but
        // high priority to KILL, so it's deliberately a single ~7% slot, not a swarm.
        if (r < 0.09f) return MakeHostile("SENTRY", "TURRET", WeaponKind.Lmg, 6 + bump, 66 + bump, 0, x, y);       //  9% immobile nest
        if (r < 0.19f) return MakeHostile("VIPER", "SNIPER", WeaponKind.Sniper, 4 + bump, 62 + bump, 5, x, y);     // 10% marksman
        if (r < 0.28f) return MakeHostile("REAVER", "BERSERKER", WeaponKind.Shotgun, 12 + bump * 2, 58 + bump, 8, x, y); // 9% rusher
        if (r < 0.37f) return MakeHostile("WASP", "DRONE", WeaponKind.Smg, 3 + bump, 60 + bump, 7, x, y);          //  9% drone
        if (r < 0.46f) return MakeHostile("JACKAL", "HUNTER", WeaponKind.Smg, 5 + bump, 60 + bump, 9, x, y);       //  9% flanker
        if (r < 0.54f)                                                                                              //  8% shield
        {
            var s = MakeHostile("AEGIS", "SHIELD", WeaponKind.Rifle, 10 + bump * 2, 56 + bump, 4, x, y);
            s.ShieldDx = -1; s.ShieldDy = 0;            // shield faces the squad (west)
            return s;
        }
        if (r < 0.61f) return MakeHostile("BREACH", "SAPPER", WeaponKind.Shotgun, 7 + bump, 56 + bump, 6, x, y);   //  7% demolition
        if (r < 0.68f)                                                                                              //  7% grenadier
        {
            var m = MakeHostile("MORTAR", "MORTAR", WeaponKind.Smg, 6 + bump, 50 + bump, 5, x, y);
            m.Grenades = n >= 5 ? 3 : 2;                // a deep frag pouch — the EXISTING grenade AI uses it
            return m;
        }
        if (r < 0.74f) return MakeHostile("ORDERLY", "MEDIC", WeaponKind.Smg, 6 + bump, 52 + bump, 6, x, y);       //  6% medic
        // SPOTTER (BEACON): a fragile back-line designator. It barely fights (poor SMG, low HP) but
        // while it lives it "paints" the squad's priority target — Ai.Plan amplifies focus-fire
        // convergence for ALL allies (see Ai.SpotterActive). Kill it first to break the crossfire.
        if (r < 0.81f) return MakeHostile("BEACON", "SPOTTER", WeaponKind.Smg, 5 + bump, 48 + bump, 6, x, y);      //  7% designator
        if (r < 0.89f) return MakeHostile("OGRE", "BRUISER", WeaponKind.Lmg, 9 + bump * 2, 56 + bump, 5, x, y);    //  8% bruiser
        if (r < 0.95f) return MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);       //  6% scout
        return MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);                     //  5% grunt
    }

    static readonly string[] Callsigns =
        { "HAWK", "ECHO", "RAVEN", "SLATE", "ONYX", "FOX", "WREN", "ASH", "CIPHER", "JINX", "ROOK", "DELTA", "MOTH", "QUILL" };

    /// A fresh rookie of a random class, for backfilling the squad between missions.
    /// Includes the CORPSMAN (5th class) so casualties can pull in in-combat sustain.
    public static Unit MakeRecruit()
    {
        string name = Util.Choice(Callsigns);
        switch (Util.RandInt(0, 4))
        {
            case 0: return MakeSoldier(name, "ASSAULT", WeaponKind.Rifle, 8, 66, 7);
            case 1: return MakeSoldier(name, "RANGER", WeaponKind.Shotgun, 7, 62, 8);
            case 2: return MakeSoldier(name, "SHARPSHOOTER", WeaponKind.Sniper, 6, 72, 6);
            case 3: return MakeSoldier(name, "CORPSMAN", WeaponKind.Smg, 7, 62, 8);
            default: return MakeSoldier(name, "GUNNER", WeaponKind.Lmg, 10, 58, 6);
        }
    }

    /// The escort asset: fragile, poor aim, carries only a panicky sidearm.
    /// Lives in the player roster for one mission and never joins the persistent squad.
    public static Unit MakeVip()
    {
        var u = new Unit
        {
            Name = "VIP", Cls = "VIP", Team = Team.Player,
            // HP 6 -> 14: balance data (competent AI) showed Escort at 34% win-rate with many
            // "VIP LOST" losses -- a 6-HP asset died in 1-2 turns once contact broke and the
            // enemy focus-fired it. 14 makes it a sturdier asset (still no cover-perks, weak aim,
            // no frags) that can eat a couple of hits while the squad screens for it. Paired with
            // the dialed-down anti-VIP AI bias in Ai.Plan so the VIP stays a priority without being
            // an instant focus-fire magnet. Measured: Escort 34% -> ~65% at heat 0, VIP-LOST losses
            // roughly halved.
            Hp = 14, MaxHp = 14, Aim = 45, Mobility = 6,
            Weapon = Weapon.Make(WeaponKind.Smg), IsVip = true,
        };
        u.Ammo = u.Weapon.Clip;
        u.Grenades = 0;
        return u;
    }

    static Unit MakeSoldier(string name, string cls, WeaponKind w, int hp, int aim, int mob)
    {
        var u = new Unit { Name = name, Cls = cls, Team = Team.Player, Hp = hp, MaxHp = hp, Aim = aim, Mobility = mob, Weapon = Weapon.Make(w) };
        u.Ammo = u.Weapon.Clip;
        u.Grenades = 1;
        u.AbilityCharge = 1;
        return u;
    }

    static Unit MakeHostile(string name, string cls, WeaponKind w, int hp, int aim, int mob, int x, int y)
    {
        var u = new Unit { Name = name, Cls = cls, Team = Team.Enemy, X = x, Y = y, Hp = hp, MaxHp = hp, Aim = aim, Mobility = mob, Weapon = Weapon.Make(w) };
        u.Ammo = u.Weapon.Clip;
        return u;
    }

    /// A reinforcement for the DEFEND objective: a basic grunt/scout, scaled by mission.
    public static Unit MakeWaveHostile(int n, int x, int y)
    {
        int bump = Math.Max(0, n - 1);
        var e = Util.Roll(50)
            ? MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 58 + bump, 6, x, y)
            : MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 56 + bump, 8, x, y);
        e.Aim = Math.Min(82, e.Aim);
        return e;
    }

    /// Procedural-path safety net: every hostile / objective tile must stay reachable
    /// from the squad over walkable terrain. If a generated structure walled one off,
    /// carve an L-shaped lane back toward the squad by clearing the blocking cover. Runs
    /// universally (authored maps are pre-verified, but the protective cover is added
    /// after that check, so this catches any edge case for both paths).
    static void EnsureConnectivity(Grid g, List<Unit> players, List<Unit> enemies,
                                   HashSet<(int, int)> evac, (int x, int y)? terminal, List<(int x, int y)> sabotage)
    {
        if (players.Count == 0) return;
        var from = players[0];
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var cost = g.CostMap(from.X, from.Y, (x, y) => false, out _, 9999);
            bool Stuck(int x, int y) => g.InBounds(x, y) && cost[x, y] < 0;

            var stuck = new List<(int x, int y)>();
            foreach (var e in enemies) if (Stuck(e.X, e.Y)) stuck.Add((e.X, e.Y));
            foreach (var t in evac) if (Stuck(t.Item1, t.Item2)) stuck.Add(t);
            if (terminal.HasValue && Stuck(terminal.Value.x, terminal.Value.y)) stuck.Add(terminal.Value);
            if (sabotage != null) foreach (var s in sabotage) if (Stuck(s.x, s.y)) stuck.Add(s);
            if (stuck.Count == 0) return;

            // carve toward the squad until we meet ground that was already reachable
            foreach (var (tx, ty) in stuck)
            {
                int cx = tx, cy = ty, guard = 0;
                while (g.InBounds(cx, cy) && cost[cx, cy] < 0 && guard++ < g.W + g.H)
                {
                    if (g.Tiles[cx, cy] != TileType.Floor) g.Tiles[cx, cy] = TileType.Floor;
                    if (cx != from.X) cx += Math.Sign(from.X - cx);
                    else if (cy != from.Y) cy += Math.Sign(from.Y - cy);
                    else break;
                }
            }
        }
    }

    static void PlaceBlock(Grid g, HashSet<(int, int)> occ, int x, int y, TileType t, int w, int h)
    {
        for (int dx = 0; dx < w; dx++)
            for (int dy = 0; dy < h; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if (!g.InBounds(nx, ny) || occ.Contains((nx, ny))) continue;
                g.Tiles[nx, ny] = t;
                occ.Add((nx, ny));
            }
    }

    /// Raise a rectangular patch of ground to high ground (walkable plateau).
    /// Skips the extraction zone and the left-edge spawn columns.
    static void RaisePlateau(Grid g, HashSet<(int, int)> evac, int x, int y, int w, int h, int level = 1)
    {
        for (int dx = 0; dx < w; dx++)
            for (int dy = 0; dy < h; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if (!g.InBounds(nx, ny) || nx < 3) continue;
                if (evac.Contains((nx, ny))) continue;
                g.Height[nx, ny] = level;
            }
    }

    /// Pick an authored layout to try, applying a soft biome affinity: 50% of the time
    /// choose the biome's hinted layout (if one is set), else pick uniformly at random.
    /// The connectivity guard in TryApplyLayout still validates the result regardless.
    static string[] PickLayout(int missionNum)
    {
        int biomeIdx = (missionNum - 1 + Biome.All.Length) % Biome.All.Length;
        int hint = (biomeIdx < BiomeLayoutHint.Length) ? BiomeLayoutHint[biomeIdx] : -1;
        if (hint >= 0 && hint < Maps.Layouts.Length && Util.Roll(50))
            return Maps.Layouts[hint];
        return Util.Choice(Maps.Layouts);
    }

    static void TryCover(Grid g, HashSet<(int, int)> occ, int x, int y, TileType t)
    {
        if (!g.InBounds(x, y) || occ.Contains((x, y)) || g.Tiles[x, y] != TileType.Floor) return;
        g.Tiles[x, y] = t;
        occ.Add((x, y));
    }
}
