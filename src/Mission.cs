using System;
using System.Collections.Generic;

namespace Breach;

/// Builds battlefields and squads. The player squad persists across a run
/// (see Run); each mission regenerates the map + a scaled hostile force.
public static class Mission
{
    static readonly (int x, int y)[] PlayerSpawns = { (1, 2), (1, 4), (2, 7), (1, 9) };

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
                             List<(int x, int y)> evac = null)
    {
        enemies.Clear();
        for (int x = 0; x < grid.W; x++)
            for (int y = 0; y < grid.H; y++)
                grid.Tiles[x, y] = TileType.Floor;

        // place players at left spawns, refresh per-mission state (HP persists)
        for (int i = 0; i < players.Count && i < PlayerSpawns.Length; i++)
        {
            var u = players[i];
            u.X = PlayerSpawns[i].x;
            u.Y = PlayerSpawns[i].y;
            u.Ammo = u.Weapon.Clip;
            u.Grenades = 1;                  // refill grenade each mission
            u.OnOverwatch = false;
            u.Hunkered = false;
            u.Recoil = System.Numerics.Vector2.Zero;
            u.Flash = 0;
        }

        var evacSet = new HashSet<(int, int)>(evac ?? new List<(int, int)>());
        SpawnEnemies(grid, enemies, missionNum, evacSet);

        var occupied = new HashSet<(int, int)>();
        foreach (var u in players) occupied.Add((u.X, u.Y));
        foreach (var u in enemies) occupied.Add((u.X, u.Y));
        foreach (var t in evacSet) occupied.Add(t);   // keep the extraction zone clear of cover

        // central structures for sightlines
        PlaceBlock(grid, occupied, 8, 2, TileType.HighCover, 1, 3);
        PlaceBlock(grid, occupied, 9, 6, TileType.HighCover, 1, 3);
        PlaceBlock(grid, occupied, 5, 5, TileType.LowCover, 3, 1);
        PlaceBlock(grid, occupied, 12, 4, TileType.LowCover, 1, 3);
        PlaceBlock(grid, occupied, 13, 8, TileType.HighCover, 2, 1);

        // protective cover beside each soldier and hostile
        foreach (var u in players) TryCover(grid, occupied, u.X + 1, u.Y, TileType.LowCover);
        foreach (var u in enemies) TryCover(grid, occupied, u.X - 1, u.Y, TileType.HighCover);

        // random crates (a touch more clutter on later missions)
        int sprinkles = 14 + Math.Min(6, missionNum);
        int guard = 0;
        while (sprinkles > 0 && guard++ < 500)
        {
            int x = Util.RandInt(3, grid.W - 4);
            int y = Util.RandInt(0, grid.H - 1);
            if (occupied.Contains((x, y)) || grid.Tiles[x, y] != TileType.Floor) continue;
            grid.Tiles[x, y] = Util.Roll(55) ? TileType.LowCover : TileType.HighCover;
            occupied.Add((x, y));
            sprinkles--;
        }

        foreach (var u in players) u.SyncPos();
        foreach (var u in enemies) u.SyncPos();
    }

    static void SpawnEnemies(Grid grid, List<Unit> enemies, int n, HashSet<(int, int)> evac)
    {
        int count = Math.Min(4 + n, 9);
        int bump = n - 1;                 // stat growth per mission
        var rows = new List<int>();
        for (int y = 0; y < grid.H; y++) rows.Add(y);
        // shuffle rows
        for (int i = rows.Count - 1; i > 0; i--) { int j = Util.RandInt(0, i); (rows[i], rows[j]) = (rows[j], rows[i]); }

        var used = new HashSet<(int, int)>();
        for (int i = 0; i < count; i++)
        {
            int y = rows[i % rows.Count];
            int x = grid.W - 2 - (i / rows.Count);   // pack into right columns
            if (x < grid.W - 4) x = grid.W - 2;
            int guard = 0;
            while ((used.Contains((x, y)) || evac.Contains((x, y))) && guard++ < 30)
            { y = Util.RandInt(0, grid.H - 1); x = grid.W - 2 - Util.RandInt(0, 2); }
            used.Add((x, y));

            float r = Util.RandF();
            Unit e;
            if (n >= 2 && r < 0.20f)
                e = MakeHostile("OGRE", "BRUISER", WeaponKind.Lmg, 9 + bump * 2, 56 + bump, 5, x, y);
            else if (r < 0.32f)
                e = MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);
            else
                e = MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);
            e.Aim = Math.Min(82, e.Aim);
            e.Active = false;          // dormant until sighted
            e.PodId = i / 2;           // pods of ~2
            enemies.Add(e);
        }
    }

    static Unit MakeSoldier(string name, string cls, WeaponKind w, int hp, int aim, int mob)
    {
        var u = new Unit { Name = name, Cls = cls, Team = Team.Player, Hp = hp, MaxHp = hp, Aim = aim, Mobility = mob, Weapon = Weapon.Make(w) };
        u.Ammo = u.Weapon.Clip;
        u.Grenades = 1;
        return u;
    }

    static Unit MakeHostile(string name, string cls, WeaponKind w, int hp, int aim, int mob, int x, int y)
    {
        var u = new Unit { Name = name, Cls = cls, Team = Team.Enemy, X = x, Y = y, Hp = hp, MaxHp = hp, Aim = aim, Mobility = mob, Weapon = Weapon.Make(w) };
        u.Ammo = u.Weapon.Clip;
        return u;
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

    static void TryCover(Grid g, HashSet<(int, int)> occ, int x, int y, TileType t)
    {
        if (!g.InBounds(x, y) || occ.Contains((x, y)) || g.Tiles[x, y] != TileType.Floor) return;
        g.Tiles[x, y] = t;
        occ.Add((x, y));
    }
}
