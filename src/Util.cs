using System;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// Global layout + tuning constants.
public static class Cfg
{
    public const int ScreenW = 1280;
    public const int ScreenH = 800;
    public const int Tile = 56;
    public const int GridW = 18;
    public const int GridH = 11;

    public static int BoardW => GridW * Tile;       // 1008
    public static int BoardH => GridH * Tile;       // 616
    public static int OriginX => (ScreenW - BoardW) / 2; // 136
    public const int OriginY = 64;                  // top HUD band
}

/// Colour palette + helpers.
public static class Pal
{
    public static Color RGBA(int r, int g, int b, int a = 255) =>
        new Color((byte)r, (byte)g, (byte)b, (byte)a);

    public static readonly Color Bg        = RGBA(10, 14, 19);
    public static readonly Color BoardEdge = RGBA(30, 39, 51);
    public static readonly Color FloorA    = RGBA(22, 29, 38);
    public static readonly Color FloorB    = RGBA(26, 34, 44);
    public static readonly Color GridLine  = RGBA(33, 43, 56);
    public static readonly Color CoverHi   = RGBA(54, 66, 84);
    public static readonly Color CoverHiTop= RGBA(78, 94, 116);
    public static readonly Color CoverLo   = RGBA(40, 50, 64);
    public static readonly Color CoverLoTop= RGBA(58, 72, 90);

    // high-ground plateaus (raised, walkable floor)
    public static readonly Color HighA     = RGBA(46, 62, 80);
    public static readonly Color HighB     = RGBA(52, 69, 88);
    public static readonly Color HighSide  = RGBA(14, 19, 26);
    public static readonly Color HighEdge  = RGBA(120, 165, 190);

    public static readonly Color Friend    = RGBA(56, 189, 248);
    public static readonly Color FriendDk   = RGBA(12, 74, 110);
    public static readonly Color Foe       = RGBA(248, 113, 113);
    public static readonly Color FoeDk      = RGBA(120, 30, 30);
    public static readonly Color Elite     = RGBA(255, 140, 90);   // capstone boss
    public static readonly Color EliteDk    = RGBA(120, 50, 20);
    public static readonly Color Accent    = RGBA(251, 191, 36);
    public static readonly Color Good      = RGBA(74, 222, 128);

    // the escort VIP (warm gold, distinct from friendly cyan and accent)
    public static readonly Color VipGold   = RGBA(245, 200, 70);
    public static readonly Color VipDk     = RGBA(110, 80, 14);

    public static readonly Color Txt       = RGBA(226, 232, 240);
    public static readonly Color TxtDim    = RGBA(124, 138, 160);
    public static readonly Color Panel     = RGBA(16, 22, 30, 235);
    public static readonly Color PanelBd   = RGBA(38, 49, 63);

    public static readonly Color MoveBlue  = RGBA(56, 189, 248, 60);
    public static readonly Color MoveYellow= RGBA(251, 191, 36, 55);
}

/// A per-mission visual theme: floor checker + grid/edge tint, so each mission
/// reads as a distinct place rather than one recoloured arena.
public class Biome
{
    public string Name;
    public Color FloorA, FloorB, Grid, Edge;

    public static readonly Biome[] All =
    {
        new Biome { Name = "STEEL",   FloorA = Pal.RGBA(22, 29, 38), FloorB = Pal.RGBA(26, 34, 44), Grid = Pal.RGBA(33, 43, 56), Edge = Pal.RGBA(30, 39, 51) },
        new Biome { Name = "ARID",    FloorA = Pal.RGBA(40, 33, 23), FloorB = Pal.RGBA(46, 38, 27), Grid = Pal.RGBA(62, 50, 33), Edge = Pal.RGBA(64, 52, 34) },
        new Biome { Name = "TUNDRA",  FloorA = Pal.RGBA(23, 33, 42), FloorB = Pal.RGBA(28, 39, 49), Grid = Pal.RGBA(42, 56, 70), Edge = Pal.RGBA(44, 58, 74) },
        new Biome { Name = "VERDANT", FloorA = Pal.RGBA(21, 35, 26), FloorB = Pal.RGBA(25, 41, 30), Grid = Pal.RGBA(38, 58, 42), Edge = Pal.RGBA(38, 60, 44) },
        new Biome { Name = "ASH",     FloorA = Pal.RGBA(34, 27, 27), FloorB = Pal.RGBA(40, 31, 31), Grid = Pal.RGBA(56, 42, 42), Edge = Pal.RGBA(58, 40, 40) },
        new Biome { Name = "VOID",    FloorA = Pal.RGBA(28, 24, 41), FloorB = Pal.RGBA(33, 28, 48), Grid = Pal.RGBA(50, 41, 68), Edge = Pal.RGBA(52, 42, 72) },
    };

    public static Biome For(int missionNum) => All[(missionNum - 1 + All.Length) % All.Length];
}

public static class Util
{
    public static float Clamp(float v, float a, float b) => MathF.Max(a, MathF.Min(b, v));
    public static int   Clamp(int v, int a, int b)       => Math.Max(a, Math.Min(b, v));
    public static float Lerp(float a, float b, float t)  => a + (b - a) * t;
    public static int   Sign(int v) => v > 0 ? 1 : (v < 0 ? -1 : 0);
    public static int   ChebyDist(int ax, int ay, int bx, int by) =>
        Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));
    public static float TileDist(int ax, int ay, int bx, int by) =>
        MathF.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    // easings
    public static float EaseOutQuad(float t)  => 1f - (1f - t) * (1f - t);
    public static float EaseInOutQuad(float t)=> t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f;
    public static float EaseOutBack(float t)
    {
        const float c = 2.0f;
        return 1f + (c + 1f) * MathF.Pow(t - 1f, 3f) + c * MathF.Pow(t - 1f, 2f);
    }

    public static Vector2 TileCenter(int x, int y) =>
        new(Cfg.OriginX + x * Cfg.Tile + Cfg.Tile / 2f,
            Cfg.OriginY + y * Cfg.Tile + Cfg.Tile / 2f);

    public static Rectangle TileRect(int x, int y) =>
        new(Cfg.OriginX + x * Cfg.Tile, Cfg.OriginY + y * Cfg.Tile, Cfg.Tile, Cfg.Tile);

    public static bool ScreenToTile(Vector2 m, out int tx, out int ty)
    {
        tx = (int)MathF.Floor((m.X - Cfg.OriginX) / Cfg.Tile);
        ty = (int)MathF.Floor((m.Y - Cfg.OriginY) / Cfg.Tile);
        return tx >= 0 && ty >= 0 && tx < Cfg.GridW && ty < Cfg.GridH;
    }

    // shared rng
    public static readonly Random Rng = new();
    public static int   RandInt(int aIncl, int bIncl) => Rng.Next(aIncl, bIncl + 1);
    public static float RandF() => (float)Rng.NextDouble();
    public static bool  Roll(float pct) => Rng.NextDouble() * 100.0 < pct;
    public static float RandRange(float a, float b) => a + (b - a) * (float)Rng.NextDouble();
    public static T     Choice<T>(System.Collections.Generic.IList<T> a) => a[Rng.Next(a.Count)];
}
