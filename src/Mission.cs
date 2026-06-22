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
    // STEEL=0 ARID=1 TUNDRA=2 VERDANT=3 ASH=4 VOID=5) hints at a preferred arena
    // index. When an authored map is rolled, there is a 50% chance to pick the hinted
    // layout and a 50% chance to pick randomly — keeping variety while nudging theme.
    // -1 means no preference (always picks randomly). This is SOFT: ForcedLayout
    // overrides it completely, and the connectivity guard can still fall back to
    // procedural if a hinted layout fails (though the arenas are designed to pass).
    static readonly int[] BiomeLayoutHint =
    {
        2,   // STEEL   → PILLARS (industrial columns)
        8,   // ARID    → RIDGE (exposed slope, decisive high ground)
        7,   // TUNDRA  → FOXHOLES (defensive low-cover warren)
        10,  // VERDANT → THICKET (dense organic cover clusters)
        6,   // ASH     → CROSSROADS (ruined sightline lanes)
        9,   // VOID    → RUINS (open eerie arena, long sightlines)
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
        {
            // contested high ground: raised plateaus in the mid-field (more on later missions)
            RaisePlateau(grid, evacSet, 7, 3, 2, 2);
            RaisePlateau(grid, evacSet, 11, 7, 2, 2);
            if (missionNum >= 3) RaisePlateau(grid, evacSet, Util.RandInt(6, 11), Util.RandInt(1, 8), 2, 2);
            // a commanding tier-2 redoubt appears on later missions (sees over high cover)
            if (missionNum >= 4) RaisePlateau(grid, evacSet, Util.RandInt(7, 10), Util.RandInt(3, 6), 2, 2, 2);

            // 4.2 ENCOUNTER GEOMETRY: a staggered mid-field SCREEN of high cover breaks
            // the long cross-board sightlines, so the squad can advance into the midfield
            // under cover before tripping a pod (a deliberate approach, not a turn-1
            // ambush). No single column is ever fully walled and row 5 is left as an open
            // lane, so the board stays traversable — that lane is the one risky direct route.
            var screen = new (int x, int y)[]
            {
                (7, 1), (7, 2), (7, 3),   (8, 6), (8, 7), (8, 8),
                (9, 0), (9, 1), (9, 9), (9, 10),   (10, 3), (10, 4), (10, 7), (10, 8),
                (11, 1), (11, 2),
            };
            foreach (var (sx, sy) in screen)
                if (grid.InBounds(sx, sy) && !occupied.Contains((sx, sy)))
                { grid.Tiles[sx, sy] = TileType.HighCover; occupied.Add((sx, sy)); }
            // low cover flanking the open central lane, for cover-fighting on the direct route
            PlaceBlock(grid, occupied, 6, 5, TileType.LowCover, 1, 1);
            PlaceBlock(grid, occupied, 12, 5, TileType.LowCover, 1, 1);

            // random crates (a touch more clutter on later missions; biased toward LoS-blocking high cover)
            int sprinkles = 14 + Math.Min(6, missionNum);
            int guard = 0;
            while (sprinkles > 0 && guard++ < 500)
            {
                int x = Util.RandInt(3, grid.W - 4);
                int y = Util.RandInt(0, grid.H - 1);
                if (occupied.Contains((x, y)) || grid.Tiles[x, y] != TileType.Floor) continue;
                grid.Tiles[x, y] = Util.Roll(45) ? TileType.LowCover : TileType.HighCover;
                occupied.Add((x, y));
                sprinkles--;
            }
        }

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
        int count = Math.Clamp(4 + n + enemyDelta, 3, 10);   // deployment-card modifier
        int bump = Math.Max(0, n - 1 + statDelta);           // stat growth per mission +/- card
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
                e = MakeHostile("WARLORD", "ELITE", WeaponKind.Lmg, 20 + n * 2, 72, 6, x, y);
            else if (midBoss)                   // mid-campaign elite (lighter than the WARLORD)
                e = MakeHostile(n == 3 ? "BREAKER" : "WARDEN", "ELITE", WeaponKind.Lmg, 14 + n * 2, 68, 6, x, y);
            else if (n >= 3 && r < 0.11f)       // immobile overwatch nest
                e = MakeHostile("SENTRY", "TURRET", WeaponKind.Lmg, 6 + bump, 66 + bump, 0, x, y);
            else if (n >= 2 && r < 0.23f)       // long-range marksman
                e = MakeHostile("VIPER", "SNIPER", WeaponKind.Sniper, 4 + bump, 62 + bump, 5, x, y);
            else if (n >= 3 && r < 0.33f)       // charging melee bruiser
                e = MakeHostile("REAVER", "BERSERKER", WeaponKind.Shotgun, 12 + bump * 2, 58 + bump, 8, x, y);
            else if (n >= 2 && r < 0.43f)       // hovering drone: ignores cover, beelines
                e = MakeHostile("WASP", "DRONE", WeaponKind.Smg, 3 + bump, 60 + bump, 7, x, y);
            else if (n >= 3 && r < 0.52f)       // shield-bearer: full frontal cover, must be flanked
            {
                e = MakeHostile("AEGIS", "SHIELD", WeaponKind.Rifle, 10 + bump * 2, 56 + bump, 4, x, y);
                e.ShieldDx = -1; e.ShieldDy = 0;            // shield faces the squad (west)
            }
            else if (n >= 3 && r < 0.59f)       // demolition: tears down the squad's cover
                e = MakeHostile("BREACH", "SAPPER", WeaponKind.Shotgun, 7 + bump, 56 + bump, 6, x, y);
            else if (n >= 3 && r < 0.66f)       // field medic: heals wounded allies
                e = MakeHostile("ORDERLY", "MEDIC", WeaponKind.Smg, 6 + bump, 52 + bump, 6, x, y);
            else if (n >= 2 && r < 0.74f)
                e = MakeHostile("OGRE", "BRUISER", WeaponKind.Lmg, 9 + bump * 2, 56 + bump, 5, x, y);
            else if (r < 0.84f)
                e = MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);
            else
                e = MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);
            if (e.Cls != "ELITE") e.Aim = Math.Min(82, e.Aim);
            // grenades: bruisers + the elite always; some others from mission 2 on
            if (e.Cls == "ELITE") e.Grenades = 2;
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

    static readonly string[] Callsigns =
        { "HAWK", "ECHO", "RAVEN", "SLATE", "ONYX", "FOX", "WREN", "ASH", "CIPHER", "JINX", "ROOK", "DELTA", "MOTH", "QUILL" };

    /// A fresh rookie of a random class, for backfilling the squad between missions.
    public static Unit MakeRecruit()
    {
        string name = Util.Choice(Callsigns);
        switch (Util.RandInt(0, 3))
        {
            case 0: return MakeSoldier(name, "ASSAULT", WeaponKind.Rifle, 8, 66, 7);
            case 1: return MakeSoldier(name, "RANGER", WeaponKind.Shotgun, 7, 62, 8);
            case 2: return MakeSoldier(name, "SHARPSHOOTER", WeaponKind.Sniper, 6, 72, 6);
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
            Hp = 6, MaxHp = 6, Aim = 45, Mobility = 6,
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
