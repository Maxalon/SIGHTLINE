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
    // Up to 6 soldier spawns (deploy size grows to 6 in the back half) + the VIP slot LAST
    // (the seating loop always seats the VIP at PlayerSpawns[Length-1]). A loose left-side wedge
    // in cols 0-3, all distinct, clear of the VIP's (2,5) seat.
    static readonly (int x, int y)[] PlayerSpawns = { (2, 2), (0, 4), (3, 5), (1, 7), (0, 9), (3, 8), (2, 5) };

    // Per-pod column offsets for enemy spawns: vary across cols 14-17 so the right side
    // doesn't mirror a parallel firing line. Indexed by pod id (i/2), cycling if more pods.
    static readonly int[] EnemyPodColOffset = { 1, 3, 0, 2, 1, 3 };

    // test hook (SIGHTLINE_MAP): force a specific authored layout index; -1 = normal roll
    public static int ForcedLayout = -1;

    // W2 arena telemetry: the authored layout index the LAST Build actually applied, or -1 for
    // the procedural fallback. Recorded only AFTER TryApplyLayout's connectivity guard accepted
    // the template (PickLayout merely PROPOSES one — a rejected proposal falls back procedural,
    // and logging the proposal would misattribute those missions). Read by Game.SetupMission
    // when it stamps Stats.BeginMission. Static like ForcedLayout (one Build at a time).
    public static int AppliedLayout = -1;

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
        34,  // STEEL   → DONJON (walled tier-2 keep taken by a single ramp)
        31,  // ARID    → ENTRENCHED (asymmetric dug-in trench network)
        32,  // TUNDRA  → CAUSEWAY (a frozen ford: an elevated land-bridge crossing)
        10,  // VERDANT → THICKET (dense organic cover clusters)
        33,  // ASH     → REDANS (a ruined earthworks line: diagonal sawtooth gauntlet)
        30,  // VOID    → COLONNADE (cavernous long-sightline pillar gallery)
        15,  // NEON    → GRID (orthogonal server-room rack lattice)
        28,  // MAGMA   → CRUCIBLE (barrel-rigged refinery throat chokepoint)
    };

    /// The four starting soldiers for a fresh run.
    public static List<Unit> NewRunSquad()
    {
        var squad = new List<Unit>();
        squad.Add(MakeSoldier("VEGA",   "ASSAULT",      WeaponKind.Rifle,   8, 70, 7));
        squad.Add(MakeSoldier("KRESS",  "RANGER",       WeaponKind.Shotgun, 7, 70, 8));
        squad.Add(MakeSoldier("NOX",    "SHARPSHOOTER", WeaponKind.Sniper,  6, 72, 6));
        squad.Add(MakeSoldier("BISHOP", "GUNNER",       WeaponKind.Lmg,    10, 62, 6));
        return squad;
    }

    /// Lay out a mission: regenerate terrain, place the (persistent) players,
    /// and spawn a hostile force scaled by missionNum.
    public static void Build(Grid grid, List<Unit> players, List<Unit> enemies, int missionNum,
                             List<(int x, int y)> evac = null, (int x, int y)? terminal = null,
                             int enemyDelta = 0, int statDelta = 0, List<(int x, int y)> sabotage = null,
                             int dmgDelta = 0)
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
            u.AbilityCd = 0;                                       // signature ability ready (off cooldown)
            u.ItemCharge = u.Item != ItemKind.None ? 1 : 0;        // utility item: 1 charge/mission
            u.Suppress = 0;
            u.OnOverwatch = false;
            u.Hunkered = false;
            u.Recoil = System.Numerics.Vector2.Zero;
            u.Flash = 0;
        }

        var evacSet = new HashSet<(int, int)>(evac ?? new List<(int, int)>());
        // SABOTAGE is the weakest objective (~65% vs ~90% peers, the m5 gate): unlike Hack/Evac
        // which let the squad mass at ONE zone, its 3 charge sites are spread across the mid-field,
        // so the squad must SPLIT and cross open ground while every PLANT "goes loud" (rouses pods +
        // breaks stealth). That triple tax compounds with the full force, so we ease the ENCOUNTER:
        // a lighter hostile force (the loud-tempo IS the difficulty) + covered fighting positions
        // at each site (below) so the split squad can hold.
        bool sabotageObj = sabotage != null && sabotage.Count > 0;
        SpawnEnemies(grid, enemies, missionNum, evacSet, enemyDelta, statDelta, sabotageObj, dmgDelta);

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
        // W2 telemetry: AppliedLayout records the template index only once TryApplyLayout has
        // ACCEPTED it (the connectivity guard can reject a proposal); -1 = procedural fallback.
        // The Roll(55)-before-PickLayout order is preserved exactly (same Util.Rng draw order).
        bool authored = false;
        AppliedLayout = -1;
        if (ForcedLayout >= 0 && ForcedLayout < Maps.Layouts.Length)
        {
            authored = TryApplyLayout(grid, occupied, players, enemies, evacSet, terminal, Maps.Layouts[ForcedLayout], sabotage);
            if (authored) AppliedLayout = ForcedLayout;
        }
        else if (Util.Roll(55))
        {
            int pick = PickLayout(missionNum);
            authored = TryApplyLayout(grid, occupied, players, enemies, evacSet, terminal, Maps.Layouts[pick], sabotage);
            if (authored) AppliedLayout = pick;
        }
        if (!authored)
            BuildProcedural(grid, occupied, evacSet, missionNum);

        // protective cover beside each soldier and hostile (both layout paths)
        foreach (var u in players) TryCover(grid, occupied, u.X + 1, u.Y, TileType.LowCover);
        foreach (var u in enemies) TryCover(grid, occupied, u.X - 1, u.Y, TileType.HighCover);

        // SABOTAGE: drop covered fighting positions just OUTSIDE each charge site's reserved ring,
        // on the squad-facing (west) side, so a split planter isn't planting in the open. Two low
        // blocks per site (NW/SW of the site) — they don't seal the ring (it stays open floor), and
        // EnsureConnectivity below guarantees reachability if they ever pinch a lane.
        if (sabotage != null)
            foreach (var s in sabotage)
            {
                TryCover(grid, occupied, s.x - 2, s.y - 1, TileType.LowCover);
                TryCover(grid, occupied, s.x - 2, s.y + 1, TileType.LowCover);
            }

        // Environmental hazards: scatter a few explosive barrels on open floor (both the
        // procedural AND authored-layout paths), biased toward the contested mid-field /
        // enemy-half so they're worth shooting (a barrel where a pod scatters is gold). A
        // barrel tile is non-floor (Grid.IsFloor false), so PlaceBarrels' own flood check
        // removes any barrel that would wall an objective/spawn off; the EnsureConnectivity
        // net below is the final safeguard for both cover and barrels.
        PlaceBarrels(grid, occupied, players, enemies, evacSet, terminal, sabotage, missionNum);

        // 4.2 safety net: the denser mid-field cover (+ sprinkles + protective cover + barrels)
        // must never wall a hostile or objective off from the squad — carve a lane if it did.
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
        // defense-in-depth (matches EnsureConnectivity/PlaceBarrels): the connectivity flood
        // starts from players[0], so an empty deploy must refuse the layout, not crash. The
        // real guarantee is upstream — DebriefSurvivors never leaves the squad at zero.
        if (players.Count == 0) return false;

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
                    case 'B': g.Barrel[x, y] = true; break;// explosive barrel (tile stays floor underneath)
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
                    if (!occupied.Contains((x, y))) { g.Tiles[x, y] = TileType.Floor; g.Height[x, y] = 0; g.Barrel[x, y] = false; }
            return false;
        }
        return true;
    }

    static void SpawnEnemies(Grid grid, List<Unit> enemies, int n, HashSet<(int, int)> evac,
                             int enemyDelta = 0, int statDelta = 0, bool sabotage = false,
                             int dmgDelta = 0)
    {
        // Headcount cap raised 10 -> 12 so the top-Heat "+enemy" rungs aren't silently wasted
        // (the +1/+1 from RELENTLESS/OVERWHELMING used to clip at 10 on later missions). 12 still
        // fits easily: spawns occupy cols 14-17 over grid.H rows (44 slots) and the collision loop
        // below relocates any overlap.
        // Difficulty RECALIBRATED to the grown squad: the curve was softened (3+n / (n-1)*2/3) back
        // when the squad was a struggling 4-strong. Since then deploy-growth (5-6 bodies), run boons,
        // Armor, and the Evac fix stacked huge squad power -> heat-0 hit ~97%/mission (too trivial).
        // Restored the enemy headcount (4+n, cap 12) and the full per-mission stat bump (n-1) so the
        // now-strong squad faces a real fight; Heat's deltas still stack for the mastery ladder.
        int count = Math.Clamp(4 + n + enemyDelta, 3, 12);   // deployment-card + Heat modifier
        int bump = Math.Max(0, (n - 1) + statDelta);         // stat growth per mission +/- card
        // SABOTAGE relief (the weakest objective / m5 gate, ~65% -> aiming ~85%): the difficulty of
        // this objective IS the 3x split-and-go-loud tempo, not raw bodies, so trim the force by 2
        // (floored at 3) so a divided squad isn't also out-gunned. Stat bump is untouched and the
        // Heat ladder still applies on top, so the mastery curve is preserved.
        if (sabotage) count = Math.Max(3, count - 2);
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
            // SIGNAL W5 — m6 bite, MEASURED SIZE (paired flywheel, h0 slots 0-19): the finale is
            // startlingly body-count sensitive. With the kits live: restore +2 bodies (count-2) ->
            // m6 70-76% conditional and h0 run completion 75% -> 60% (3x the -5pt dip budget);
            // restore 0 (count-4) -> m6 100% (a formality again — the kit retinues are support
            // pieces and the faction rosters run softer than the mixed m6 cascade). Restore +1
            // (count-3) is the measured middle: m6 ~85-88%, completion ~70% (dip ~-5, on budget).
            // With m1-m5 untouched, the dip budget pins m6 to the TOP of the 80-88 band by
            // construction (h0 completion >= 70% requires m6 >= ~85%). An UNSTAMPED finale (the
            // Faction.None safety fallback) keeps the old count-4 exactly.
            count = Math.Max(5, count - (Combat.MissionFaction != Faction.None ? 3 : 4));
            bump = Math.Max(0, n - 1);                       // drop the boss-card/heat StatDelta for the screen
        }
        var rows = new List<int>();
        for (int y = 0; y < grid.H; y++) rows.Add(y);
        // shuffle rows
        for (int i = rows.Count - 1; i > 0; i--) { int j = Util.RandInt(0, i); (rows[i], rows[j]) = (rows[j], rows[i]); }

        var used = new HashSet<(int, int)>();
        bool siegeSpawned = false;    // hard cap: at most ONE SIEGE/BOMBARD artillery per mission (fairness)
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
            // SIGNAL W5 — BOSS IDENTITY: the finale boss (i==0) + its explicit kit retinue
            // (i==1/2 on Legion/Syndicate finales) and the m3/m5 mid-boss are all keyed off
            // Combat.MissionFaction (see MakeFinaleBoss/MakeFinaleRetinue/MakeMidBoss below),
            // so each faction's climax forces a DIFFERENT verb. Faction.None falls back to
            // today's plain WARLORD / mission-keyed mid-boss (the safety invariant). The RandF
            // draw above stays unconditional so the RNG stream is unchanged for every slot.
            Unit e = null;
            if (finalMission)
                e = i == 0 ? MakeFinaleBoss(n, x, y) : MakeFinaleRetinue(i, n, bump, x, y);
            if (e == null)
                e = midBoss ? MakeMidBoss(n, x, y)
                            : SelectArchetype(n, r, bump, x, y);   // tier-appropriate rank-and-file
            // FAIRNESS CAP: at most one SIEGE/BOMBARD per mission. SelectArchetype is stateless, so a
            // second roll could yield another -> demote any extra BOMBARD to a plain GRUNT here.
            // SIGNAL W5: this cap DELIBERATELY keys on Cls (not HasSiege) — a siege-armed BOSS elite
            // (WARDEN mid-boss / SIEGELORD finale) is EXEMPT: it never sets siegeSpawned and never
            // demotes the force's one real BOMBARD (the Legion finale retinue fields both by design).
            if (e.Cls == "BOMBARD")
            {
                if (siegeSpawned) e = MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);
                else siegeSpawned = true;
            }
            // Aim clamp raised 82 -> 88: the old 82 cap silently ATE the top-rung Heat StatDelta (+aim)
            // for any archetype whose base + bump + Heat exceeded 82, flattening the ladder's apex. 88
            // lets high-Heat aim bonuses land (the ladder stays meaningful at the top) while still
            // leaving the squad some miss chance. Low Heat is unaffected (its small StatDelta keeps
            // non-elite aim well under 88, so this is a no-op there).
            if (e.Cls != "ELITE") e.Aim = Math.Min(88, e.Aim);
            // grenades: bruisers + the elite always; some others from mission 2 on.
            // MORTAR already carries a deep frag pouch (set in SelectArchetype) — never overwrite it.
            // ELITE grenades: mid-bosses (BREAKER/WARDEN on m3/m5, which already win at high
            // rates) keep 2; the final WARLORD gets 1 -- two frags from the boss was a big part
            // of the m6 wall (it could AoE the whole squad before they closed). midBoss==true
            // only for the m3/m5 named elites; the final boss is finalMission && i==0.
            if (e.Cls == "ELITE") e.Grenades = midBoss ? 2 : 1;
            else if (e.Cls == "MORTAR") { /* keep MORTAR's 2-3 grenades from SelectArchetype */ }
            else if (n >= 2 && e.Cls != "MEDIC" && e.Cls != "SAPPER" && e.Cls != "BOMBARD" && (e.Cls == "BRUISER" || Util.Roll(22))) e.Grenades = 1;
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
            // W6c — NO QUARTER bites: the rung-8 Heat row's +1 enemy damage, applied to the
            // per-unit Weapon instance (Weapon.Make returns a FRESH Weapon per unit, so this
            // never mutates a shared template; default 0 == today's spawns byte-for-byte).
            // Applied at this single chokepoint so EVERY spawned body — archetype, demoted
            // BOMBARD, mid-boss, WARLORD — carries it. Two known side effects, both deliberate:
            //  (1) +1 DmgMax WIDENS the AI finish band (Ai.Plan's `p.Hp <= e.Weapon.DmgMax`
            //      reads), so apex enemies also press kills on soldiers one HP point earlier —
            //      a coordination sharpening beyond the raw +1 per hit;
            //  (2) it leans AGAINST the BRACE comeback lever (the stagger's reduced-damage
            //      trade claws back relatively less at the apex) — watched via the heat-8
            //      flywheel; the comeback economy is the first re-tune if lead-swings collapse.
            // Scope: the INITIAL force only — pressure-clock/Defend reinforcement waves stay
            // deliberately light bodies (see MakeWaveHostile's do-not-upgrade note).
            if (dmgDelta != 0) { e.Weapon.DmgMin += dmgDelta; e.Weapon.DmgMax += dmgDelta; }
            e.Alert = AlertLevel.Unaware;  // dormant until sighted (escalates via 4.3 tiers)
            e.PodId = i / 2;               // pods of ~2
            // APEX W5: composition telemetry — count the FINAL pick (post demote/clamp) at spawn
            // time, tagged faction-roster vs default-cascade (no-op unless the balance harness runs).
            Stats.RecordSpawn(e.Cls, Combat.MissionFaction != Faction.None);
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
        // ENEMY FACTIONS (Phase 4 foundation): when a faction is active, a mission's rank-and-file
        // is drawn from THAT faction's roster instead of the default tier cascade, so the force reads
        // as one named opponent. Boss / mid-boss slots are handled by the caller (SpawnEnemies), not
        // here, so the campaign's named elites are untouched. Game.SetupMission wires the campaign
        // node's stamp into Combat.BeginMission (Run.GenerateMap stamps EVERY Combat/Elite node), so
        // this branch runs on the majority of campaign fights; None (Start/Supply/Boss nodes,
        // skirmish/endless, and the harness default) falls through to the unchanged cascade below —
        // the SAFETY INVARIANT: spawns are byte-identical to the unstamped build while no faction is set.
        if (Combat.MissionFaction != Faction.None)
            return FactionRoster(Combat.MissionFaction, n, r, bump, x, y);

        if (n <= 1)   // MISSION 1 — basic force: a scout screen + grunts, nothing special.
            return r < 0.60f
                ? MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y)
                : MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);

        if (n == 2)   // MISSION 2 — light skirmishers (each ~11-14%; deliberately no dominant type).
        {
            if (r < 0.13f) return MakeHostile("VIPER", "SNIPER", WeaponKind.Sniper, 4 + bump, 62 + bump, 5, x, y); // 13% marksman
            if (r < 0.26f) return MakeHostile("WASP", "DRONE", WeaponKind.Smg, 3 + bump, 60 + bump, 7, x, y);      // 13% drone
            if (r < 0.38f) return MakeHostile("JACKAL", "HUNTER", WeaponKind.Smg, 5 + bump, 60 + bump, 9, x, y);   // 12% flanker
            // STRIKER (WRAITH): a fast, fragile LEAPER — see Ai.Plan. It rushes THROUGH player overwatch
            // (discounts the kill-zone like a berserker) to end on your soldier's flanked/soft side.
            // Appears from m2 as the light-skirmish flank threat. Counter: don't camp overwatch alone —
            // body-block or focus it (it's glass). Spawns here at ~11%.
            if (r < 0.49f) return MakeHostile("WRAITH", "STRIKER", WeaponKind.Smg, 4 + bump, 60 + bump, 9, x, y);  // 11% leaper
            if (r < 0.62f) return MakeHostile("HOPLITE", "LANCER", WeaponKind.Rifle, 6 + bump, 58 + bump, 5, x, y);// 13% formation trooper
            if (r < 0.74f) return MakeHostile("FERAL", "HOUND", WeaponKind.Smg, 3 + bump, 56 + bump, 9, x, y);     // 12% swarmer
            if (r < 0.86f) return MakeHostile("OGRE", "BRUISER", WeaponKind.Lmg, 9 + bump, 56 + bump, 5, x, y);// 12% bruiser
            return MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);                  // 14% scout
        }

        // MISSIONS 3+ — the full roster is available. Windows tuned for variety: every archetype
        // appears, with the specialists (TURRET/BERSERKER/SHIELD/SAPPER/MORTAR/MEDIC/SPOTTER)
        // collectively the bulk and the plain SCOUT/GRUNT now a small remainder (they carried too
        // much before). SPOTTER is a force-multiplier (see Ai.Plan): low priority body count but
        // high priority to KILL, so it's deliberately a single ~7% slot, not a swarm.
        if (r < 0.07f) return MakeHostile("SENTRY", "TURRET", WeaponKind.Lmg, 6 + bump, 66 + bump, 0, x, y);       //  7% immobile nest
        if (r < 0.15f) return MakeHostile("VIPER", "SNIPER", WeaponKind.Sniper, 4 + bump, 62 + bump, 5, x, y);     //  8% marksman
        if (r < 0.22f) return MakeHostile("REAVER", "BERSERKER", WeaponKind.Shotgun, 12 + bump, 58 + bump, 8, x, y); // 7% rusher
        if (r < 0.29f) return MakeHostile("WASP", "DRONE", WeaponKind.Smg, 3 + bump, 60 + bump, 7, x, y);          //  7% drone
        if (r < 0.36f) return MakeHostile("JACKAL", "HUNTER", WeaponKind.Smg, 5 + bump, 60 + bump, 9, x, y);       //  7% flanker
        // STRIKER (WRAITH): a fast, fragile LEAPER — see Ai.Plan. Highest-tier flank threat: it rushes
        // THROUGH player overwatch (it discounts the kill-zone like a berserker) and seeks to END on the
        // soldier's flanked/soft side even harder than the HUNTER. Counter: overwatch-camping does NOT
        // stop it — body-block the flank or focus it down (it's glass). ~7% slot.
        if (r < 0.43f) return MakeHostile("WRAITH", "STRIKER", WeaponKind.Smg, 4 + bump, 60 + bump, 9, x, y);      //  7% leaper
        // LANCER (HOPLITE): a formation trooper — see Ai.Plan. It is sturdier in a line (the AI rewards
        // ending adjacent to another hostile, so a pod forms a wall and presses forward in lockstep),
        // which makes it a tempting GRENADE / AoE target. Counter by breaking the formation up.
        if (r < 0.50f) return MakeHostile("HOPLITE", "LANCER", WeaponKind.Rifle, 7 + bump, 58 + bump, 5, x, y);    //  7% formation trooper
        // HOUND (FERAL): a fast, low-HP swarmer that hunts the ISOLATED soldier (see Ai.Plan: very high
        // advance weight + targets the squad member with the FEWEST nearby allies, beelining to it).
        // They spawn in pairs (the caller pods them ~2 each). Counter by staying massed / overwatching.
        if (r < 0.57f) return MakeHostile("FERAL", "HOUND", WeaponKind.Smg, 3 + bump, 56 + bump, 9, x, y);         //  7% swarmer
        if (r < 0.63f)                                                                                              //  6% shield
        {
            var s = MakeHostile("AEGIS", "SHIELD", WeaponKind.Rifle, 10 + bump * 2, 56 + bump, 4, x, y);
            s.ShieldDx = -1; s.ShieldDy = 0;            // shield faces the squad (west)
            return s;
        }
        if (r < 0.69f) return MakeHostile("BREACH", "SAPPER", WeaponKind.Shotgun, 7 + bump, 56 + bump, 6, x, y);   //  6% demolition
        if (r < 0.74f)                                                                                              //  5% grenadier
        {
            var m = MakeHostile("MORTAR", "MORTAR", WeaponKind.Smg, 6 + bump, 50 + bump, 5, x, y);
            m.Grenades = n >= 5 ? 3 : 2;                // a deep frag pouch — the EXISTING grenade AI uses it
            return m;
        }
        if (r < 0.79f) return MakeHostile("ORDERLY", "MEDIC", WeaponKind.Smg, 6 + bump, 52 + bump, 6, x, y);       //  5% medic
        // SPOTTER (BEACON): a fragile back-line designator. It barely fights (poor SMG, low HP) but
        // while it lives it "paints" the squad's priority target — Ai.Plan amplifies focus-fire
        // convergence for ALL allies (see Ai.SpotterActive). Kill it first to break the crossfire.
        if (r < 0.84f) return MakeHostile("BEACON", "SPOTTER", WeaponKind.Smg, 5 + bump, 48 + bump, 6, x, y);      //  5% designator
        // SCREENER (HAZE): a fragile back-line AREA-DENIAL zoner. It barely fights — its action is a
        // PROACTIVE SMOKE dropped ON your firing lane (see Ai.Plan/BestScreen), blinding your soldiers'
        // sightlines and FORCING you to reposition to re-acquire targets. Reuses the enemy smoke exec.
        // Counter: push through / around the cloud, or kill it before it screens. Carries the smoke
        // charge (set in SpawnEnemies). ~6% slot.
        if (r < 0.88f)                                                                                              //  4% zoner
        {
            var z = MakeHostile("HAZE", "SCREENER", WeaponKind.Smg, 5 + bump, 46 + bump, 6, x, y);
            z.EnemyItem = ItemKind.Smoke; z.ItemCharge = 2;   // a deep smoke pouch — the EXISTING smoke AI uses it
            return z;
        }
        // SIEGE (BOMBARD): a fragile back-line artillery piece. It does NOT fire — it CHARGES a
        // telegraphed 3x3 strike (shown for a full player turn) that lands cover-ignoring next enemy
        // turn (see Ai.Plan/Game.TickSiegeStrikes). Forces RELOCATION (a non-shoot tactical axis).
        // Rare (~4%); capped at 1 per mission by the post-pick guard in SpawnEnemies.
        if (r < 0.92f) return MakeHostile("SIEGE", "BOMBARD", WeaponKind.Smg, 7 + bump, 48 + bump, 4, x, y);       //  4% artillery
        // WARBRINGER (SIGNIFER, W8): the Legion standard-bearer — a mid-HP banner anchor: pods with
        // a living banner within Chebyshev Game.BannerRange cannot rout and rally a turn faster
        // (Game.BreakPodMorale / BeginEnemyUnitTurn). A priority-target decision: the comeback
        // lever (focus a pod down to break it) is CONTESTED until the banner falls. ~3% slot.
        if (r < 0.95f) return MakeHostile("SIGNIFER", "WARBRINGER", WeaponKind.Rifle, 8 + bump, 56 + bump, 5, x, y); // 3% banner anchor
        // CUSTODIAN (SEXTON, W8): the objective KEEPER — a low-threat unit that walks to the
        // terminal / a blown sabotage charge and undoes ONE step of progress per adjacent turn
        // (Ai.Plan -> Game.DoRelock, banner-telegraphed). Screen it out or shoot it first. ~2% slot.
        if (r < 0.97f) return MakeHostile("SEXTON", "CUSTODIAN", WeaponKind.Smg, 5 + bump, 48 + bump, 6, x, y);     // 2% keeper
        if (r < 0.98f) return MakeHostile("OGRE", "BRUISER", WeaponKind.Lmg, 9 + bump, 56 + bump, 5, x, y);    //  1% bruiser
        if (r < 0.99f) return MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);       //  1% scout
        return MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);                     //  1% grunt
    }

    /// FACTION-GATED rank-and-file pick (Phase 4 foundation). Returns one archetype drawn from the
    /// given faction's roster by the uniform roll `r` in [0,1). STATS are copied VERBATIM from the
    /// default cascade in SelectArchetype (same name/cls/weapon/hp/aim/mob lines, incl. SHIELD facing,
    /// MORTAR's frag pouch and SCREENER's smoke pouch) — only the gating moved, so a faction force is
    /// the SAME units, just grouped. Each roster ends with a GRUNT (Wardens: SCOUT) filler so any roll
    /// resolves. APEX W5: the four setup-verb archetypes (STRIKER/LANCER/HOUND/SCREENER) were authored
    /// into the default cascade but unreachable on faction-stamped nodes (the majority of the campaign);
    /// they're folded in here — Legion += STRIKER/LANCER/HOUND, Syndicate/Wardens += SCREENER — each
    /// tier-gated at its cascade first-appearance mission (r-window && n>=N, exactly like Wardens'
    /// existing BOMBARD gate; a failed gate falls through to the next window, so any roll resolves).
    /// Only ever called when Combat.MissionFaction != None (the None default uses the unchanged cascade).
    static Unit FactionRoster(Faction f, int n, float r, int bump, int x, int y)
    {
        switch (f)
        {
            // SYNDICATE (tech/mechanized) — DRONE, SHIELD, TURRET, SAPPER, SPOTTER, SCREENER m3+
            // (+ GRUNT filler). The SCREENER's proactive lane-smoke pairs with the SPOTTER's
            // focus-paint: the tech faction fights your INFORMATION, not just your HP bar.
            case Faction.Syndicate:
                if (r < 0.22f) return MakeHostile("WASP", "DRONE", WeaponKind.Smg, 3 + bump, 60 + bump, 7, x, y);
                if (r < 0.40f)
                {
                    var s = MakeHostile("AEGIS", "SHIELD", WeaponKind.Rifle, 10 + bump * 2, 56 + bump, 4, x, y);
                    s.ShieldDx = -1; s.ShieldDy = 0;            // shield faces the squad (west)
                    return s;
                }
                if (r < 0.56f) return MakeHostile("SENTRY", "TURRET", WeaponKind.Lmg, 6 + bump, 66 + bump, 0, x, y);
                if (r < 0.72f) return MakeHostile("BREACH", "SAPPER", WeaponKind.Shotgun, 7 + bump, 56 + bump, 6, x, y);
                if (r < 0.82f) return MakeHostile("BEACON", "SPOTTER", WeaponKind.Smg, 5 + bump, 48 + bump, 6, x, y);
                if (r < 0.92f && n >= 3)                        // 10% zoner (m3+ — cascade first appearance)
                {
                    var z = MakeHostile("HAZE", "SCREENER", WeaponKind.Smg, 5 + bump, 46 + bump, 6, x, y);
                    z.EnemyItem = ItemKind.Smoke; z.ItemCharge = 2;   // a deep smoke pouch — the EXISTING smoke AI uses it
                    return z;
                }
                // W8: the CUSTODIAN keeper suits the tech faction — it contests your PROGRESS
                // (re-locks the terminal / re-arms blown charges), like the SPOTTER/SCREENER
                // contest your information. m3+, matching the SCREENER's full-roster tier.
                if (r < 0.97f && n >= 3) return MakeHostile("SEXTON", "CUSTODIAN", WeaponKind.Smg, 5 + bump, 48 + bump, 6, x, y); // 5% keeper (m3+)
                return MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);              // filler

            // LEGION (shock assault) — BERSERKER, BRUISER, HUNTER + the m2+ skirmish tier:
            // STRIKER (overwatch-defying leaper), LANCER (formation line), HOUND (isolation
            // swarmer) — the coordination showcase, at home in the rush faction (+ SCOUT, GRUNT filler).
            case Faction.Legion:
                if (r < 0.22f) return MakeHostile("REAVER", "BERSERKER", WeaponKind.Shotgun, 12 + bump, 58 + bump, 8, x, y);
                if (r < 0.42f) return MakeHostile("OGRE", "BRUISER", WeaponKind.Lmg, 9 + bump, 56 + bump, 5, x, y);
                if (r < 0.60f) return MakeHostile("JACKAL", "HUNTER", WeaponKind.Smg, 5 + bump, 60 + bump, 9, x, y);
                if (r < 0.68f && n >= 2) return MakeHostile("WRAITH", "STRIKER", WeaponKind.Smg, 4 + bump, 60 + bump, 9, x, y);   // 8% leaper (m2+)
                if (r < 0.76f && n >= 2) return MakeHostile("HOPLITE", "LANCER", WeaponKind.Rifle, (n >= 3 ? 7 : 6) + bump, 58 + bump, 5, x, y); // 8% formation trooper (m2+; HP 6->7 at m3, like the cascade)
                if (r < 0.84f && n >= 2) return MakeHostile("FERAL", "HOUND", WeaponKind.Smg, 3 + bump, 56 + bump, 9, x, y);      // 8% swarmer (m2+)
                // W8: the WARBRINGER banner anchor is Legion-native — the shock faction's pods hold
                // the line under its standard (no rout + faster rally within Chebyshev BannerRange).
                if (r < 0.90f && n >= 3) return MakeHostile("SIGNIFER", "WARBRINGER", WeaponKind.Rifle, 8 + bump, 56 + bump, 5, x, y); // 6% banner anchor (m3+)
                if (r < 0.93f) return MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);
                return MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);              // filler

            // WARDENS (precision/control / area-denial) — SNIPER, MORTAR, SIEGE artillery, SCREENER
            // m3+, MEDIC, GRUNT (+ SCOUT filler). The standoff faction: thematically perfect for the
            // telegraphed artillery (capped at 1/mission by the SpawnEnemies post-pick guard) and the
            // lane-blinding smoke zoner (area denial in both directions).
            case Faction.Wardens:
            default:
                if (r < 0.24f) return MakeHostile("VIPER", "SNIPER", WeaponKind.Sniper, 4 + bump, 62 + bump, 5, x, y);
                if (r < 0.42f)
                {
                    var m = MakeHostile("MORTAR", "MORTAR", WeaponKind.Smg, 6 + bump, 50 + bump, 5, x, y);
                    m.Grenades = n >= 5 ? 3 : 2;                // a deep frag pouch — the EXISTING grenade AI uses it
                    return m;
                }
                // The two m3+ gates route their FAILED (m2) rolls to the SCOUT filler, not the next
                // window — falling through would hand MEDIC their combined 22% and make mission-2
                // Wardens pods a 36%-medic heal-loop slog (W5 review LOW-3).
                if (r < 0.54f) return n >= 3
                    ? MakeHostile("SIEGE", "BOMBARD", WeaponKind.Smg, 7 + bump, 48 + bump, 4, x, y)   // 12% artillery (m3+ only — fairness tier)
                    : MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);
                if (r < 0.64f)                                  // 10% zoner (m3+ — cascade first appearance)
                {
                    if (n < 3) return MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);
                    var z = MakeHostile("HAZE", "SCREENER", WeaponKind.Smg, 5 + bump, 46 + bump, 6, x, y);
                    z.EnemyItem = ItemKind.Smoke; z.ItemCharge = 2;   // a deep smoke pouch — the EXISTING smoke AI uses it
                    return z;
                }
                if (r < 0.78f) return MakeHostile("ORDERLY", "MEDIC", WeaponKind.Smg, 6 + bump, 52 + bump, 6, x, y);
                // W8: the CUSTODIAN keeper is Wardens-native — the control faction contests your
                // objective PROGRESS itself (re-locks the terminal / re-arms blown charges). m3+
                // like SIEGE/SCREENER; the failed (m2) gate routes to the GRUNT window's pick, so
                // any roll still resolves and mission-2 Wardens pods are unchanged.
                if (r < 0.86f && n >= 3) return MakeHostile("SEXTON", "CUSTODIAN", WeaponKind.Smg, 5 + bump, 48 + bump, 6, x, y); // 8% keeper (m3+)
                if (r < 0.92f) return MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);
                return MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);              // filler
        }
    }

    // APEX W5: 14 -> 40 callsigns. Rosters plus a draft pool can hold ~14 soldiers across a run,
    // so the old pool made duplicate names routine — and bond/memorial/veteran records key on the
    // name, silently merging two soldiers' histories. Kept terse/ASCII in the game's codename
    // voice; deliberately avoids every enemy archetype name, the fixed NewRunSquad names and the
    // Nicknames.Pool entries so "KRESS 'VIPER'" style overlaps can't read as two different units.
    static readonly string[] Callsigns =
    {
        "HAWK", "ECHO", "RAVEN", "SLATE", "ONYX", "FOX", "WREN", "ASH", "CIPHER", "JINX",
        "ROOK", "DELTA", "MOTH", "QUILL", "TALON", "FLINT", "GALE", "SABLE", "PIKE", "VESPER",
        "COBALT", "DUSK", "EMBER", "GARNET", "HALO", "IBIS", "KESTREL", "LYNX", "MICA", "NOVA",
        "PRISM", "RUNE", "SPARK", "VECTOR", "WOLF", "ZEPHYR", "LARK", "FROST", "BRIAR", "CREED",
    };

    /// A fresh rookie of a random class, for backfilling the squad between missions.
    /// Includes the CORPSMAN (5th class) so casualties can pull in in-combat sustain.
    /// `taken` (optional, APEX W5): callsigns already in use (current squad / draft pool) — the
    /// name re-rolls away from them with the same bounded-guard pattern GenerateDraftPool uses
    /// for class variety, so duplicate soldier names stop silently merging bond/memorial records.
    /// With 40 callsigns and rosters <= ~14 names the re-roll never realistically exhausts; if it
    /// somehow does, the duplicate is accepted (a recruit must always be produced — never blocks).
    /// `mission` (optional, APEX W8): DEPTH-SCALED backfill — a recruit drafted mid-run arrives
    /// with (mission-1)/2 banked kills, so the casualty valve stops handing a mission-7 squad a
    /// 0-kill ROOKIE that drags the whole roster's power (the flagged sloppy-policy failure path;
    /// by construction only casualty-taking runs change). The caller runs Run.PromoteEligible on
    /// the recruit so the seeded kills rank it (SQUADDIE ~m3-4, CORPORAL + spec offer m7+ — the
    /// intended ceiling) in the SAME barracks visit. Default 1 == 0 kills: the run-opening draft
    /// pool, StartRun and Events.cs recruit grants deliberately stay unscaled.
    public static Unit MakeRecruit(HashSet<string> taken = null, int mission = 1)
    {
        string name = Util.Choice(Callsigns);
        if (taken != null)
        {
            int guard = 0;
            while (taken.Contains(name) && guard++ < 400) name = Util.Choice(Callsigns);
        }
        Unit u = Util.RandInt(0, 4) switch
        {
            0 => MakeSoldier(name, "ASSAULT", WeaponKind.Rifle, 8, 66, 7),
            1 => MakeSoldier(name, "RANGER", WeaponKind.Shotgun, 7, 66, 8),
            2 => MakeSoldier(name, "SHARPSHOOTER", WeaponKind.Sniper, 6, 68, 6),
            3 => MakeSoldier(name, "CORPSMAN", WeaponKind.Smg, 7, 62, 8),
            _ => MakeSoldier(name, "GUNNER", WeaponKind.Lmg, 10, 58, 6),
        };
        u.Kills = Math.Max(0, (mission - 1) / 2);
        return u;
    }

    /// The escort asset: fragile, poor aim, carries only a panicky sidearm.
    /// Lives in the player roster for one mission and never joins the persistent squad.
    public static Unit MakeVip(int missionNum = 1)
    {
        // HP 6 -> 14 (Wave A.5) -> now scales with mission depth: balance data showed Escort still
        // gating runs (~55%, many "VIP LOST") because the fragile asset carries ZERO persistent
        // progression while the enemy force climbs every mission. A flat 14 HP that's sturdy at m2
        // is glass by m6. Scale it (14 + 2*mission: m2~18, m4~22, m6~26) so the asset stays a
        // believable survivor against the late force, while still having no cover-perks, weak aim,
        // and no frags — the squad must still screen for it.
        int hp = 14 + 2 * Math.Max(1, missionNum);
        var u = new Unit
        {
            Name = "VIP", Cls = "VIP", Team = Team.Player,
            Hp = hp, MaxHp = hp, Aim = 45, Mobility = 6,
            Weapon = Weapon.Make(WeaponKind.Smg), IsVip = true,
        };
        u.Ammo = u.Weapon.Clip;
        u.Grenades = 0;
        // The escort asset also gets light ARMOR that scales with mission depth (the squad's
        // bought plating doesn't help the VIP, so it carries its own): every incoming hit -armor,
        // floored at 1. Paired with the HP scaling + the reduced anti-VIP AI finish-frenzy, this
        // stops the fragile asset getting deleted in one focus-fire volley over a long escort.
        u.Armor = Math.Max(1, missionNum) / 2;   // m2~1, m4~2, m6~3
        return u;
    }

    static Unit MakeSoldier(string name, string cls, WeaponKind w, int hp, int aim, int mob)
    {
        var u = new Unit { Name = name, Cls = cls, Team = Team.Player, Hp = hp, MaxHp = hp, Aim = aim, Mobility = mob, Weapon = Weapon.Make(w) };
        u.Ammo = u.Weapon.Clip;
        u.Grenades = 1;
        u.AbilityCd = 0;
        // (Gunner's niche is its top HP (10) + PIN area-denial; an innate armor on top inflated the
        // overall win-rate well past redistribution-neutral, so it's intentionally NOT granted.)
        return u;
    }

    static Unit MakeHostile(string name, string cls, WeaponKind w, int hp, int aim, int mob, int x, int y)
    {
        var u = new Unit { Name = name, Cls = cls, Team = Team.Enemy, X = x, Y = y, Hp = hp, MaxHp = hp, Aim = aim, Mobility = mob, Weapon = Weapon.Make(w) };
        u.Ammo = u.Weapon.Clip;
        return u;
    }

    // ─── SIGNAL W5 — BOSS IDENTITY (mid-boss signatures + finale kits) ─────────────────────
    // All three named bosses used to be the IDENTICAL unit (ELITE + Lmg); runs climaxed in the
    // same fight every time. Now each faction's named elite carries a SIGNATURE mechanic via
    // the Unit capability flags (HasShieldArc / HasSiege / RagesTwice) — mechanics the engine
    // already ships for rank-and-file SHIELD/BOMBARD/the enrage — so each climax forces a
    // DIFFERENT verb (DESIGN.md §A: no two kits may play the same):
    //   LEGION   — rage/rush:   kill it FAST or its low-HP tiers snowball (burst-down verb).
    //   SYNDICATE— shield arc:  its front is a wall; FLANK or take commanding height.
    //   WARDENS  — siege clock: telegraphed 3x3 strikes force RELOCATION every turn.
    // Every boss keeps Cls=="ELITE": the nameplate, enrage trigger, aim-clamp exemption, elite
    // grenade pouch and AI temperament are ELITE identity and stay Cls-keyed. Faction.None
    // (an unstamped fight: SKIRMISH/DAILY-style paths) falls back to today's plain bosses.

    // The three SIGNATURE arms (capability flags; Cls stays "ELITE" on every armed boss):
    static Unit ArmRage(Unit b)   { b.RagesTwice = true; return b; }                       // second rage tier at <=25% + the Ai rush temperament
    static Unit ArmShield(Unit b) { b.HasShieldArc = true; b.ShieldDx = -1; b.ShieldDy = 0; return b; }  // frontal barrier arc, opens facing the squad; FaceShields re-faces it each enemy turn
    static Unit ArmSiege(Unit b)  { b.HasSiege = true; return b; }                         // telegraphed 3x3 strikes; EXEMPT from the siegeSpawned cap by construction (the cap keys on Cls=="BOMBARD"); the Ai falls through to the ELITE gun when nothing is worth shelling

    /// The m3/m5 recurring named elite, keyed by the node's faction (a faction-signature fight).
    /// Unstamped (SKIRMISH/DAILY-style paths): today's plain mission-keyed BREAKER/WARDEN exactly.
    static Unit MakeMidBoss(int n, int x, int y)
    {
        Unit Mk(string name) => MakeHostile(name, "ELITE", WeaponKind.Lmg, 14 + n * 2, 68, 6, x, y);
        return Combat.MissionFaction switch
        {
            Faction.Legion    => ArmRage(Mk("BREAKER")),    // the rush: burst it down before the frenzy
            Faction.Syndicate => ArmShield(Mk("BULWARK")),  // the wall: flank-or-elevate puzzle
            Faction.Wardens   => ArmSiege(Mk("WARDEN")),    // the clock: relocate under telegraphed fire
            _                 => Mk(n == 3 ? "BREAKER" : "WARDEN"),
        };
    }

    /// The capstone named boss (m6), keyed by the Boss node's stamped faction (the FINALE KIT).
    /// Wardens keeps today's WARLORD fight (the reference kit: the enrage brick — burst/focus);
    /// Legion fields a siege-armed SIEGELORD whose strikes force RELOCATION while the rush faction
    /// closes; Syndicate a shield-arced SPYMASTER that must be FLANKED behind its screen cell.
    /// Faction.None == today's WARLORD exactly.
    ///
    /// MEASURED TUNE (flywheel, h0+h2 paired slots 0-9, vs the 80-88%-conditional target):
    ///  * the spec's first-cut Legion kit (WARDEN-stat 14+2n boss + LANCER/BOMBARD retinue)
    ///    measured 37% — the every-turn boss strike, a SECOND real artillery and the Legion
    ///    close-range warp taxed the same resource (position) three times over. The shipped kit
    ///    keeps the identity (one telegraphed strike per turn to dodge) on the standard boss
    ///    statline, escorted by a LANCER pair instead of the BOMBARD.
    ///  * the SPYMASTER runs one HP step lighter (12+n): behind a re-facing shield arc + the HVT
    ///    guards + a screen cell it measured 73% at 14+n — and a spymaster is a skulker, not a brick.
    static Unit MakeFinaleBoss(int n, int x, int y)
    {
        // (On the WARLORD statline history: HP 20+2n -> 14+n, aim 72 -> 68 — mission-6 was a
        // ~90%-loss wall; every kit boss keeps 68 aim and a 1-frag pouch via the ELITE branches.)
        return Combat.MissionFaction switch
        {
            Faction.Legion    => ArmSiege(MakeHostile("SIEGELORD", "ELITE", WeaponKind.Lmg, 14 + n, 68, 6, x, y)),
            Faction.Syndicate => ArmShield(MakeHostile("SPYMASTER", "ELITE", WeaponKind.Lmg, 12 + n, 68, 6, x, y)),
            _                 => MakeHostile("WARLORD", "ELITE", WeaponKind.Lmg, 14 + n, 68, 6, x, y),
        };
    }

    /// The finale kit's EXPLICIT retinue (slots i==1/2, right behind the boss). Legion escorts its
    /// siege-lord with a LANCER phalanx pair (measured tune — see MakeFinaleBoss: pairing the boss's
    /// strikes with a second real artillery piece sank the kit to a 37% conditional; the boss IS the
    /// kit's artillery); Syndicate screens its spymaster with a lane-blinding zoner + a leaper.
    /// Wardens (today's fight) and None return null — the roster/cascade fills every slot as before.
    static Unit MakeFinaleRetinue(int i, int n, int bump, int x, int y)
    {
        if (i > 2) return null;
        switch (Combat.MissionFaction)
        {
            case Faction.Legion:                       // a phalanx pair (both retinue slots)
                return MakeHostile("HOPLITE", "LANCER", WeaponKind.Rifle, 7 + bump, 58 + bump, 5, x, y);
            case Faction.Syndicate:
                if (i == 1)
                {
                    var z = MakeHostile("HAZE", "SCREENER", WeaponKind.Smg, 5 + bump, 46 + bump, 6, x, y);
                    z.EnemyItem = ItemKind.Smoke; z.ItemCharge = 2;   // a deep smoke pouch — the EXISTING smoke AI uses it
                    return z;
                }
                return MakeHostile("WRAITH", "STRIKER", WeaponKind.Smg, 4 + bump, 60 + bump, 9, x, y);
            default:
                return null;
        }
    }

    /// A reinforcement wave hostile, scaled by mission. Two tiers (APEX W5):
    ///   rich == false (the default; anti-turtle PRESSURE CLOCK waves): the original cheap
    ///     GRUNT/SCOUT coin flip. DELIBERATE — the clock's punishment must stay light bodies,
    ///     not roster threats, or sloppy/slow play eats BRUISER-class waves and the already-
    ///     over-band policy gap widens further. Do not upgrade this path.
    ///   rich == true (DEFEND objective waves only — flagged by Game.SpawnDefendWave): draws
    ///     from the battle-tested endless roster (MakeEndlessHostile at tier = mission) so the
    ///     one enemy-forced-tempo objective fields real variety instead of a conveyor of grunts.
    ///     Two fairness guards: a bounded re-roll away from TURRET (Mobility 0 at the spawn edge
    ///     is a dead body; bounded like GenerateDraftPool's guard<400 re-rolls — a bare skip
    ///     would under-fill waves ~1-in-5 rolls on Syndicate Defend nodes) and a demote of any
    ///     rolled BOMBARD to a plain wave grunt (waves arrive already Alert — an off-screen
    ///     artillery telegraph the player never saw spawn is unfair).
    public static Unit MakeWaveHostile(int n, int x, int y, bool rich = false)
    {
        int bump = Math.Max(0, n - 1);
        if (rich)
        {
            var h = MakeEndlessHostile(n, x, y);
            int guard = 0;
            while (h.Cls == "TURRET" && guard++ < 400) h = MakeEndlessHostile(n, x, y);
            if (h.Cls != "TURRET" && h.Cls != "BOMBARD") return h;   // MakeEndlessHostile already clamps aim
            // fall through: demote BOMBARD (or a pathological all-TURRET streak) to a plain wave grunt
        }
        var e = rich || Util.Roll(50)
            ? MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 58 + bump, 6, x, y)
            : MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 56 + bump, 8, x, y);
        e.Aim = Math.Min(82, e.Aim);
        return e;
    }

    /// LAST STAND (HORIZON W2): a horde hostile at difficulty `tier` (the endless wave-scale). Low
    /// tiers are light skirmishers; tier >= 3 unlocks the FULL archetype roster (snipers/shields/
    /// drones/berserkers/siege/etc.), and HP/aim rise with the tier (bump capped + aim clamped so
    /// bodies stay killable). Reuses the campaign archetype cascade so the horde has real variety,
    /// not just grunts. `tier` is Game.EndlessWaveScale(wave) (= 1 + wave/4 + softened Heat —
    /// see that method's tuning note) for LAST STAND waves, or the raw mission number for
    /// DEFEND's rich campaign waves (MakeWaveHostile), which never see the endless curve.
    public static Unit MakeEndlessHostile(int tier, int x, int y)
    {
        int n = Math.Clamp(tier, 1, 6);           // roster depth: n>=3 opens the full cascade in SelectArchetype
        int bump = Math.Min(tier, 12);            // HP/aim bump rises with the tier (capped so it stays killable)
        var e = SelectArchetype(n, Util.RandF(), bump, x, y);
        e.Aim = Math.Min(88, e.Aim);              // clamp: escalation comes from numbers + toughness, not auto-hits
        return e;
    }

    /// APEX W7 "an ending" (LAST STAND, waves past saturation): the extra ELITE injected each
    /// deep wave so stands statistically terminate. Mirrors the campaign mid-boss stat line
    /// (14 + 2*tier HP, tier-capped; fixed 68 aim — ELITEs are exempt from the rank-and-file
    /// clamp and 68 sits below it anyway; LMG). Grenades set EXPLICITLY here: the campaign's
    /// ELITE-grenade branch lives in SpawnEnemies, which the endless spawner never runs through,
    /// so without this line a rolled elite would arrive frag-less by accident.
    public static Unit MakeEndlessElite(int tier, int x, int y)
    {
        var e = MakeHostile("REAPER", "ELITE", WeaponKind.Lmg, 14 + 2 * Math.Min(tier, 10), 68, 6, x, y);
        e.Grenades = 1;
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
                    g.Barrel[cx, cy] = false;   // a barrel keeps a tile non-walkable (IsFloor false) — clear it so the carve actually opens the lane
                    if (cx != from.X) cx += Math.Sign(from.X - cx);
                    else if (cy != from.Y) cy += Math.Sign(from.Y - cy);
                    else break;
                }
            }
        }
    }

    /// Scatter a SMALL number of explosive barrels (2-5, scaling gently with mission size) on
    /// open floor, biased toward the contested mid-field / enemy half so they reward a shot
    /// (a barrel where a pod scatters to cover is a free area-denial / chain kill). A barrel
    /// makes its tile non-floor (Grid.IsFloor false), so it behaves as an obstacle for ALL
    /// pathing/connectivity automatically. SAFETY: each candidate is placed only after a flood
    /// from the squad confirms every spawn / hostile / objective tile stays reachable WITH the
    /// barrel down; any barrel that would pinch a required lane is reverted immediately. (The
    /// Build-level EnsureConnectivity below is a second net, but we never rely on it carving a
    /// barrel out — barrels are non-floor and that net only clears cover, so we keep the map
    /// connected here.)
    static void PlaceBarrels(Grid g, HashSet<(int, int)> occupied, List<Unit> players,
                             List<Unit> enemies, HashSet<(int, int)> evac,
                             (int x, int y)? terminal, List<(int x, int y)> sabotage, int missionNum)
    {
        if (players.Count == 0) return;

        // gentle count scaling: m1 -> 2, growing to a cap of 5 on later missions
        int target = Math.Clamp(2 + missionNum / 2, 2, 5);
        var from = players[0];

        // the set of tiles that MUST remain reachable from the squad after each placement
        var required = new List<(int x, int y)>();
        foreach (var u in players) required.Add((u.X, u.Y));
        foreach (var u in enemies) required.Add((u.X, u.Y));
        foreach (var t in evac) required.Add(t);
        if (terminal.HasValue) required.Add(terminal.Value);
        if (sabotage != null) foreach (var s in sabotage) required.Add(s);

        bool AllReachable()
        {
            var cost = g.CostMap(from.X, from.Y, (x, y) => false, out _, 9999);
            foreach (var (rx, ry) in required)
                if (!g.InBounds(rx, ry) || cost[rx, ry] < 0) return false;
            return true;
        }

        int placed = 0, guard = 0;
        while (placed < target && guard++ < 400)
        {
            // bias toward the contested mid-field / enemy half (cols 6-15), all rows.
            int x = Util.RandInt(6, 15);
            int y = Util.RandInt(0, g.H - 1);

            // only an unreserved, currently-empty FLOOR tile is a candidate (never on cover,
            // a spawn, the evac zone, the terminal ring, or a sabotage ring).
            if (occupied.Contains((x, y))) continue;
            if (!g.IsFloor(x, y)) continue;                       // cover / existing barrel / OOB
            if (g.Barrel[x, y]) continue;

            // tentatively drop the barrel, then verify connectivity; revert if it walls anything off.
            g.Barrel[x, y] = true;
            if (!AllReachable())
            {
                g.Barrel[x, y] = false;                           // would pinch a lane — skip it
                continue;
            }
            occupied.Add((x, y));                                 // commit (keeps later passes off it)
            placed++;
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

    /// Pick an authored layout INDEX to try, applying a soft biome affinity: 50% of the time
    /// choose the biome's hinted layout (if one is set), else pick uniformly at random.
    /// The connectivity guard in TryApplyLayout still validates the result regardless.
    /// (W2: returns the index — not the template — so the caller can record WHICH arena
    /// was applied; Rng draw order matches the old template-returning version exactly.)
    static int PickLayout(int missionNum)
    {
        int biomeIdx = (missionNum - 1 + Biome.All.Length) % Biome.All.Length;
        int hint = (biomeIdx < BiomeLayoutHint.Length) ? BiomeLayoutHint[biomeIdx] : -1;
        if (hint >= 0 && hint < Maps.Layouts.Length && Util.Roll(50))
            return hint;
        return Util.Rng.Next(Maps.Layouts.Length);   // == Util.Choice's draw (same Next(count) call)
    }

    static void TryCover(Grid g, HashSet<(int, int)> occ, int x, int y, TileType t)
    {
        if (!g.InBounds(x, y) || occ.Contains((x, y)) || g.Tiles[x, y] != TileType.Floor) return;
        g.Tiles[x, y] = t;
        occ.Add((x, y));
    }

    /// Screenshot-only debug (SIGHTLINE_CONTENT=1): replace the hostile force with one ALERT
    /// copy of each NEW content archetype (a LANCER phalanx + a HOUND pack) plus a reference
    /// pair, all in the mid-field, so the new silhouettes/AI read clearly in a single frame.
    /// Harness-gated in Program.cs; never runs in normal play. Mirrors how other Debug* hooks
    /// stage a clean showcase. Builds two LANCERs side-by-side (the phalanx wall) and two HOUNDs
    /// (the pack), wired Alert so they're drawn as live foes.
    public static void DebugContentShowcase(Game g)
    {
        g.Enemies.Clear();
        void Add(string name, string cls, WeaponKind w, int hp, int aim, int mob, int x, int y)
        {
            var e = MakeHostile(name, cls, w, hp, aim, mob, x, y);
            e.Alert = AlertLevel.Alert; e.PodId = -1; e.SyncPos();
            g.Enemies.Add(e);
        }
        // LANCER phalanx (shoulder-to-shoulder) mid-field
        Add("HOPLITE", "LANCER", WeaponKind.Rifle, 7, 60, 5, 9, 3);
        Add("HOPLITE", "LANCER", WeaponKind.Rifle, 7, 60, 5, 9, 4);
        // HOUND pack (the swarmers) lower mid-field
        Add("FERAL", "HOUND", WeaponKind.Smg, 3, 56, 9, 10, 7);
        Add("FERAL", "HOUND", WeaponKind.Smg, 3, 56, 9, 11, 8);

        // W8 — WARBRINGER banner anchor (diamond ring + pennant + aura outline) with a held pod
        // beside it: pod 5 is staged at its waver point (3 alive of an original 4) but sits inside
        // the banner's aura, so it draws NO WAVERING tag (the banner holds it — the honest read).
        Add("SIGNIFER", "WARBRINGER", WeaponKind.Rifle, 8, 56, 5, 15, 3);
        Add("RAIDER", "GRUNT", WeaponKind.Rifle, 5, 60, 6, 14, 2); g.Enemies[^1].PodId = 5;
        Add("RAIDER", "GRUNT", WeaponKind.Rifle, 5, 60, 6, 16, 2); g.Enemies[^1].PodId = 5;
        Add("RAIDER", "GRUNT", WeaponKind.Rifle, 5, 60, 6, 14, 4); g.Enemies[^1].PodId = 5;
        g.DebugPodOrig(5, 4);

        // W8 — a WAVERING pod far from any banner (Chebyshev > BannerRange from the SIGNIFER):
        // pod 6, 3 alive of an original 4 — exactly one kill from the rout threshold, so all three
        // draw the amber WVR crack tag (the telegraph screenshot's subject).
        Add("STALKER", "SCOUT", WeaponKind.Smg, 4, 58, 8, 3, 7); g.Enemies[^1].PodId = 6;
        Add("STALKER", "SCOUT", WeaponKind.Smg, 4, 58, 8, 4, 8); g.Enemies[^1].PodId = 6;
        Add("STALKER", "SCOUT", WeaponKind.Smg, 4, 58, 8, 3, 9); g.Enemies[^1].PodId = 6;
        g.DebugPodOrig(6, 4);

        // W8 — CUSTODIAN objective keeper (padlock silhouette), lower right, clear of both pods
        Add("SEXTON", "CUSTODIAN", WeaponKind.Smg, 5, 48, 6, 15, 8);
    }
}
