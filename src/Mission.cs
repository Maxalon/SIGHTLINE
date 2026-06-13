using System;
using System.Collections.Generic;

namespace Breach;

/// Builds a battlefield + spawns both squads.
public static class Mission
{
    public static void Build(Grid grid, List<Unit> players, List<Unit> enemies)
    {
        players.Clear();
        enemies.Clear();
        for (int x = 0; x < grid.W; x++)
            for (int y = 0; y < grid.H; y++)
                grid.Tiles[x, y] = TileType.Floor;

        // ---- spawn squads first so we can salt cover around them ----
        AddPlayer(players, "VEGA",   "ASSAULT",      WeaponKind.Rifle,   1, 2, 8, 70, 7);
        AddPlayer(players, "KRESS",  "RANGER",       WeaponKind.Shotgun, 1, 4, 7, 66, 8);
        AddPlayer(players, "NOX",    "SHARPSHOOTER", WeaponKind.Sniper,  2, 7, 6, 76, 6);
        AddPlayer(players, "BISHOP", "GUNNER",       WeaponKind.Lmg,     1, 9, 10, 62, 6);

        AddEnemy(enemies, "RAIDER",  "GRUNT",   WeaponKind.Rifle,   16, 1, 5, 60, 6);
        AddEnemy(enemies, "RAIDER",  "GRUNT",   WeaponKind.Rifle,   16, 4, 5, 60, 6);
        AddEnemy(enemies, "STALKER", "SCOUT",   WeaponKind.Smg,     16, 6, 4, 60, 8);
        AddEnemy(enemies, "RAIDER",  "GRUNT",   WeaponKind.Rifle,   16, 8, 5, 60, 6);
        AddEnemy(enemies, "OGRE",    "BRUISER", WeaponKind.Lmg,     15, 10, 10, 56, 5);

        var occupied = new HashSet<(int, int)>();
        foreach (var u in players) occupied.Add((u.X, u.Y));
        foreach (var u in enemies) occupied.Add((u.X, u.Y));

        // ---- scatter cover ----
        // central structures for interesting sightlines
        PlaceBlock(grid, occupied, 8, 2, TileType.HighCover, 1, 3);
        PlaceBlock(grid, occupied, 9, 6, TileType.HighCover, 1, 3);
        PlaceBlock(grid, occupied, 5, 5, TileType.LowCover, 3, 1);
        PlaceBlock(grid, occupied, 12, 4, TileType.LowCover, 1, 3);
        PlaceBlock(grid, occupied, 13, 8, TileType.HighCover, 2, 1);

        // protective cover near each soldier so opening turns aren't suicidal
        foreach (var u in players) TryCoverNear(grid, occupied, u.X + 1, u.Y, TileType.LowCover);
        foreach (var u in enemies) TryCoverNear(grid, occupied, u.X - 1, u.Y, TileType.HighCover);

        // random sprinkle of crates
        int sprinkles = 14;
        int guard = 0;
        while (sprinkles > 0 && guard++ < 400)
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

    static void TryCoverNear(Grid g, HashSet<(int, int)> occ, int x, int y, TileType t)
    {
        if (!g.InBounds(x, y) || occ.Contains((x, y)) || g.Tiles[x, y] != TileType.Floor) return;
        g.Tiles[x, y] = t;
        occ.Add((x, y));
    }

    static void AddPlayer(List<Unit> list, string name, string cls, WeaponKind w,
                          int x, int y, int hp, int aim, int mob)
    {
        var u = new Unit
        {
            Name = name, Cls = cls, Team = Team.Player,
            X = x, Y = y, Hp = hp, MaxHp = hp, Aim = aim, Mobility = mob,
            Weapon = Weapon.Make(w),
        };
        u.Ammo = u.Weapon.Clip;
        list.Add(u);
    }

    static void AddEnemy(List<Unit> list, string name, string cls, WeaponKind w,
                         int x, int y, int hp, int aim, int mob)
    {
        var u = new Unit
        {
            Name = name, Cls = cls, Team = Team.Enemy,
            X = x, Y = y, Hp = hp, MaxHp = hp, Aim = aim, Mobility = mob,
            Weapon = Weapon.Make(w),
        };
        u.Ammo = u.Weapon.Clip;
        list.Add(u);
    }
}
