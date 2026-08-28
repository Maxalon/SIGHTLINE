using System;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// Global layout + tuning constants.
public static class Cfg
{
    public const int ScreenW = 1280;
    public const int ScreenH = 800;
    public const int Tile = 64;                     // full-bleed board (Phase 4.1): bigger tiles reclaim the margin
    public const int GridW = 18;
    public const int GridH = 11;

    public static int BoardW => GridW * Tile;       // 1152
    public static int BoardH => GridH * Tile;       // 704
    public static int OriginX => (ScreenW - BoardW) / 2; // 64 — NOTE: the roster strip (x 8..140) still overlaps board column 0 (x 64..128); Hud.DrawRoster reflows occluded chips
    public const int OriginY = 40;                  // board floats near the top; translucent HUD overlays its edges

    // ---- Type (Phase 5.3 font; RESONANCE V1 two-atlas + display face) --------------------
    // Loaded in Program.cs after InitWindow; each falls back gracefully if its TTF is missing.
    //
    // V1: ONE 64px atlas used to serve every size from 11px to 92px. Most of the words in the
    // game are 11-14px labels, and minifying a 64px atlas by 5x with bilinear filtering and no
    // mip chain is exactly the case that turns type into grey mush. Bake a second atlas at the
    // size the body text is actually drawn at, and pick per call site by size.
    public static Font Font;        // 64px NotoMono — data/large text (> UiFontMax)
    public static Font FontUi;      // 20px NotoMono — body/label text (<= UiFontMax)
    public static Font FontTitle;   // 96px display face (Chakra Petch) — titles only

    /// Largest point size still served by the small UI atlas.
    public const float UiFontMax = 18f;

    static bool Has(Font f) => f.Texture.Id != 0;

    /// The NotoMono atlas whose bake size is closest to `size` (see the two-atlas note above).
    public static Font FontFor(float size) => size <= UiFontMax && Has(FontUi) ? FontUi : Font;

    /// The display face, for titles only. Falls back to NotoMono when the TTF is absent.
    public static Font TitleFontFor(float size) => Has(FontTitle) ? FontTitle : FontFor(size);

    // Size-routed text helpers. Every DrawTextEx/MeasureTextEx call site in the game goes
    // through these so the atlas choice is made in exactly one place.
    public static void Text(string t, Vector2 pos, float size, float spacing, Color tint) =>
        Raylib.DrawTextEx(FontFor(size), t, pos, size, spacing, tint);
    public static Vector2 Measure(string t, float size, float spacing) =>
        Raylib.MeasureTextEx(FontFor(size), t, size, spacing);

    /// Title text — routed to the display face. Use for headline/card titles only; numerals and
    /// data stay on NotoMono (a good data face) via Text/Measure.
    public static void TitleText(string t, Vector2 pos, float size, float spacing, Color tint) =>
        Raylib.DrawTextEx(TitleFontFor(size), t, pos, size, spacing, tint);
    public static Vector2 TitleMeasure(string t, float size, float spacing) =>
        Raylib.MeasureTextEx(TitleFontFor(size), t, size, spacing);

    /// Resolve a bundled asset next to the BINARY, not the current working directory.
    /// V1 ship-blocker: every asset path was relative to the cwd, so launching the built
    /// binary from anywhere but the project root silently fell back to Raylib's built-in
    /// bitmap font (and every em-dash rendered as `?`). Keeps a cwd fallback so a loose
    /// asset dropped next to a `dotnet run` still resolves.
    public static string AssetPath(string rel)
    {
        string baked = System.IO.Path.Combine(AppContext.BaseDirectory, rel);
        if (System.IO.File.Exists(baked)) return baked;
        return rel;   // fall back to cwd-relative (dev convenience / dropped-in files)
    }
}

/// Colour palette + helpers.
public static class Pal
{
    public static Color RGBA(int r, int g, int b, int a = 255) =>
        new Color((byte)r, (byte)g, (byte)b, (byte)a);

    /// Linear blend from a toward b by t (0..1); keeps a's alpha.
    public static Color Mix(Color a, Color b, float t)
    {
        if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
        return RGBA(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t),
            a.A);
    }

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
    // Foe / Good are swapped to colorblind-safe hues by SetColorblind (3.13)
    public static Color Foe       = RGBA(248, 113, 113);
    public static Color FoeDk      = RGBA(120, 30, 30);
    public static readonly Color Elite     = RGBA(255, 140, 90);   // capstone boss
    public static readonly Color EliteDk    = RGBA(120, 50, 20);
    // 4.3 awareness tiers: amber middle state between dormant grey and alert red ("!")
    public static readonly Color Suspect   = RGBA(245, 184, 64);
    public static readonly Color SuspectDk  = RGBA(110, 78, 22);
    public static readonly Color Accent    = RGBA(251, 191, 36);
    public static Color Good      = RGBA(74, 222, 128);

    // accessibility: a deuteranopia/protanopia-friendly remap of the threat/good hues
    // (blue friend vs vermillion-orange foe vs blue-green good — distinguishable across
    // common colour-blindness types). Toggled in the pause menu, persisted in display.json.
    public static bool Colorblind;
    static readonly Color FoeNorm = RGBA(248, 113, 113), FoeCb = RGBA(238, 138, 40);
    static readonly Color FoeDkNorm = RGBA(120, 30, 30), FoeDkCb = RGBA(122, 66, 14);
    static readonly Color GoodNorm = RGBA(74, 222, 128), GoodCb = RGBA(40, 200, 168);
    public static void SetColorblind(bool on)
    {
        Colorblind = on;
        Foe = on ? FoeCb : FoeNorm;
        FoeDk = on ? FoeDkCb : FoeDkNorm;
        Good = on ? GoodCb : GoodNorm;
    }

    // the escort VIP (warm gold, distinct from friendly cyan and accent)
    public static readonly Color VipGold   = RGBA(245, 200, 70);
    public static readonly Color VipDk     = RGBA(110, 80, 14);

    public static readonly Color Txt       = RGBA(226, 232, 240);
    public static readonly Color TxtDim    = RGBA(124, 138, 160);
    public static readonly Color Panel     = RGBA(16, 22, 30, 235);
    public static readonly Color PanelBd   = RGBA(38, 49, 63);

    // RESONANCE V2 — the MOVE RANGE is a BOUNDARY, not a wash.
    // These used to be per-tile FILLS at alpha 60/55, painted over every reachable tile: on an
    // 18x11 board with a 6-10 tile budget that is 60-120 tiles of flat colour, on screen for the
    // whole player turn. Measured, it collapsed all eight biomes into one cyan family, smeared
    // the friendly-reserved hue across half the room (DESIGN 3.H: one job per accent), and made
    // dash-yellow indistinguishable from a warm-biome plateau top. Now the region is drawn as an
    // OUTLINE + corner lattice + a whisper of inner tint, so the same information costs a
    // fraction of the pixels and the room keeps its own colour.
    //   MoveBlue / MoveYellow  — the region STROKE (walk solid, dash dashed)
    //   MoveWalkTint / MoveDashTint — the whisper-level inner lift (see DrawMoveOverlay)
    //   MoveTick               — the per-tile corner lattice (walk only)
    // The inner lift is WHITE, not cyan, and that is deliberate. Mixing white into a colour
    // preserves its HUE exactly and only drops saturation, so the region can be marked without
    // moving one degree of the biome's hue — measured, an alpha-22 CYAN tint still flipped ASH
    // (a near-neutral grey biome, saturation ~0.1) a full 170 degrees to cyan, because on an
    // almost-colourless floor even a whisper of blue decides the hue. A value lift is also the
    // channel DESIGN 3.H asks for: value carries, hue does not. The friendly-cyan identity of
    // the affordance rides on the stroke and the tick lattice, which are lines and points.
    public static readonly Color MoveBlue     = RGBA(56, 189, 248, 205);
    public static readonly Color MoveYellow   = RGBA(251, 191, 36, 190);
    public static readonly Color MoveWalkTint = RGBA(255, 255, 255, 15);
    public static readonly Color MoveDashTint = RGBA(255, 255, 255, 6);
    public static readonly Color MoveTick     = RGBA(120, 210, 250, 150);
}

/// The kind of ambient atmosphere a biome breathes — a small library of motions the
/// Fx ambient layer (Fx.UpdateAmbient/DrawAmbient) renders so each biome reads as a
/// distinct *place* (embers in MAGMA, snow in TUNDRA) rather than a flat colour multiply.
/// Each is a deterministic, bounded particle field; the visual recipe lives in Fx.cs.
public enum AmbientKind
{
    Dust,    // STEEL  — sparse industrial dust motes drifting on a slow lateral draft
    Gust,    // ARID   — blowing dust gusts streaking fast across the board
    Snow,    // TUNDRA — drifting snow falling with a gentle side-sway
    Spore,   // VERDANT— floating spores/pollen bobbing slowly upward
    Ash,     // ASH    — grey ash flakes fluttering down (tumbling, slower than snow)
    Mote,    // VOID   — slow rising star/void motes that twinkle
    Scan,    // NEON   — drifting cyber motes that pulse on a faint horizontal scan
    Ember,   // MAGMA  — rising embers that flicker and accelerate upward
}

/// A per-mission visual theme: floor checker + grid/edge tint, so each mission
/// reads as a distinct place rather than one recoloured arena.
public class Biome
{
    public string Name;
    public Color FloorA, FloorB, Grid, Edge;
    public Color Tint;   // representative hue cover + plateaus are blended toward

    // --- Ambient atmosphere signature (Fx ambient layer; subtle background texture) ---
    // Data-driven so each biome reads as a distinct *place*. Kept low-contrast + bounded
    // (see Fx.UpdateAmbient/DrawAmbient): ambient must pass the squint test and never be
    // mistaken for a threat/objective. AmbCount caps the per-biome pool for perf.
    public AmbientKind Ambient;   // which motion this biome breathes
    public Color AmbCol;          // ambient particle base colour (low-contrast, never threat-like)
    public int   AmbCount;        // bounded pool size for this biome (0 = none)
    public float AmbSpeed;        // base drift speed scalar (px/s; meaning per kind)
    public float AmbSize;         // base particle radius (px)
    public float AmbAlpha;        // peak alpha (kept low so it passes the squint test)

    public static readonly Biome[] All =
    {
        // HORIZON W6 — LAND THE BIOME. FloorA/FloorB pushed apart (wider value gap = a checker the
        // eye can still see after the mean is pulled toward Tint) and each Tint driven further into
        // its own hue corner so the 8 rooms read as DISTINCT PLACES at a glance. Kept DARK (mean
        // stays a low base after DrawBoard's darken) so units/objectives/cover keep the hierarchy.
        // AmbAlpha bumped a touch (Fx multiplies ×1.5, clamps ≤0.34) so motion is a FELT secondary cue.
        // STEEL — cold steel-blue.
        new Biome { Name = "STEEL",   FloorA = Pal.RGBA(20, 28, 39), FloorB = Pal.RGBA(30, 41, 54), Grid = Pal.RGBA(33, 43, 56), Edge = Pal.RGBA(30, 39, 51), Tint = Pal.RGBA(50, 74, 104),
                    Ambient = AmbientKind.Dust,  AmbCol = Pal.RGBA(120, 140, 165), AmbCount = 44, AmbSpeed = 10f, AmbSize = 1.6f, AmbAlpha = 0.18f },
        // ARID — warm dune sand.
        new Biome { Name = "ARID",    FloorA = Pal.RGBA(41, 33, 21), FloorB = Pal.RGBA(56, 46, 28), Grid = Pal.RGBA(62, 50, 33), Edge = Pal.RGBA(64, 52, 34), Tint = Pal.RGBA(120, 88, 40),
                    Ambient = AmbientKind.Gust,  AmbCol = Pal.RGBA(190, 160, 108), AmbCount = 54, AmbSpeed = 92f, AmbSize = 1.8f, AmbAlpha = 0.17f },
        // TUNDRA — pale cyan-white frost.
        new Biome { Name = "TUNDRA",  FloorA = Pal.RGBA(24, 36, 46), FloorB = Pal.RGBA(36, 51, 63), Grid = Pal.RGBA(42, 56, 70), Edge = Pal.RGBA(44, 58, 74), Tint = Pal.RGBA(96, 132, 158),
                    Ambient = AmbientKind.Snow,  AmbCol = Pal.RGBA(210, 226, 242), AmbCount = 64, AmbSpeed = 30f, AmbSize = 2.0f, AmbAlpha = 0.22f },
        // VERDANT — overgrown green.
        new Biome { Name = "VERDANT", FloorA = Pal.RGBA(19, 35, 24), FloorB = Pal.RGBA(28, 50, 33), Grid = Pal.RGBA(38, 58, 42), Edge = Pal.RGBA(38, 60, 44), Tint = Pal.RGBA(58, 108, 62),
                    Ambient = AmbientKind.Spore, AmbCol = Pal.RGBA(158, 208, 146), AmbCount = 40, AmbSpeed = 13f, AmbSize = 1.9f, AmbAlpha = 0.19f },
        // ASH — desaturated soot grey.
        new Biome { Name = "ASH",     FloorA = Pal.RGBA(31, 28, 27), FloorB = Pal.RGBA(45, 40, 39), Grid = Pal.RGBA(56, 42, 42), Edge = Pal.RGBA(58, 40, 40), Tint = Pal.RGBA(92, 78, 74),
                    Ambient = AmbientKind.Ash,   AmbCol = Pal.RGBA(158, 146, 140), AmbCount = 58, AmbSpeed = 22f, AmbSize = 2.1f, AmbAlpha = 0.21f },
        // VOID — violet-black.
        new Biome { Name = "VOID",    FloorA = Pal.RGBA(27, 22, 42), FloorB = Pal.RGBA(40, 32, 58), Grid = Pal.RGBA(50, 41, 68), Edge = Pal.RGBA(52, 42, 72), Tint = Pal.RGBA(96, 68, 140),
                    Ambient = AmbientKind.Mote,  AmbCol = Pal.RGBA(182, 158, 224), AmbCount = 42, AmbSpeed = 8f,  AmbSize = 1.8f, AmbAlpha = 0.22f },
        // NEON: a dim cyber-grid arcology — cool slate floor lit by teal grid lines; cover reads cyan-tinted.
        new Biome { Name = "NEON",    FloorA = Pal.RGBA(14, 28, 33), FloorB = Pal.RGBA(20, 42, 49), Grid = Pal.RGBA(34, 78, 92), Edge = Pal.RGBA(36, 90, 104), Tint = Pal.RGBA(38, 118, 130),
                    Ambient = AmbientKind.Scan,  AmbCol = Pal.RGBA(96, 208, 218), AmbCount = 50, AmbSpeed = 40f, AmbSize = 1.8f, AmbAlpha = 0.18f },
        // MAGMA: a volcanic foundry — dark basalt floor veined with a warm ember tint on cover/plateaus.
        new Biome { Name = "MAGMA",   FloorA = Pal.RGBA(30, 20, 18), FloorB = Pal.RGBA(48, 30, 24), Grid = Pal.RGBA(74, 44, 32), Edge = Pal.RGBA(96, 50, 30), Tint = Pal.RGBA(150, 70, 34),
                    Ambient = AmbientKind.Ember, AmbCol = Pal.RGBA(255, 156, 74),  AmbCount = 52, AmbSpeed = 34f, AmbSize = 2.0f, AmbAlpha = 0.24f },
    };

    public static Biome For(int missionNum) => All[(missionNum - 1 + All.Length) % All.Length];

    /// Per-run biome variety: a run-seeded offset rotates which biome each mission shows,
    /// so different runs surface different biomes (incl. the newer ones) across their
    /// missions while staying deterministic within a run. The 1-arg For() is kept for any
    /// caller that wants the fixed cycle; Game switches to this at integration.
    public static Biome For(int missionNum, int runSeed) => All[IndexFor(missionNum, runSeed)];

    /// FUL-9: the INDEX behind For(missionNum, runSeed) — the arena deck keys its theme hint
    /// off the biome the player actually SEES (was mission-number-cycled, which both mismatched
    /// the displayed room and re-coupled arena to mission number, the FUL-1 confound).
    public static int IndexFor(int missionNum, int runSeed)
    {
        // TEST HOOK (byte-stable no-op unless SIGHTLINE_FORCEBIOME is set): pin the biome to a fixed
        // index so the headless screenshot harness can sweep all 8 biomes deterministically despite
        // MapSeed being random per process. Gameplay/normal runs never set it, so this is inert.
        var force = System.Environment.GetEnvironmentVariable("SIGHTLINE_FORCEBIOME");
        if (force != null && int.TryParse(force, out int fi))
            return ((fi % All.Length) + All.Length) % All.Length;
        return (int)(((uint)runSeed + (uint)(missionNum - 1)) % (uint)All.Length);
    }
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

    // shared rng. Reseedable so a SEEDED mode (PROGRAM HORIZON W4 DAILY) can make the whole
    // procedural board (arena sprinkles / barrels / pod scatter) reproducible for a given day.
    // Default construction seeds from the system clock (unchanged for every other mode).
    public static Random Rng = new();
    /// Reseed the shared RNG deterministically (SEEDED DAILY). Pass 0 to return to a clock seed.
    public static void Reseed(int seed) => Rng = seed == 0 ? new Random() : new Random(seed);
    public static int   RandInt(int aIncl, int bIncl) => Rng.Next(aIncl, bIncl + 1);
    public static float RandF() => (float)Rng.NextDouble();
    public static bool  Roll(float pct) => Rng.NextDouble() * 100.0 < pct;
    public static float RandRange(float a, float b) => a + (b - a) * (float)Rng.NextDouble();
    public static T     Choice<T>(System.Collections.Generic.IList<T> a) => a[Rng.Next(a.Count)];

    // FUL-9: seed-keyed avalanche hash (the Run.cs W5 finale-kit mixer, parameterised). For
    // campaign structure that must derive from MapSeed WITHOUT touching Util.Rng or a .NET
    // Random stream — nearby seeds keep .NET Random correlated for many draws (the measured
    // W5 16/4/0 finale-kit collapse), and CRN pairing needs the derivation to take ZERO draws.
    public static uint Hash3(int a, int b, int c)
    {
        uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du;
        h ^= h >> 16; h *= 0x45d9f3bu; h ^= h >> 16; h *= 0x45d9f3bu; h ^= h >> 16;
        return h;
    }
}
