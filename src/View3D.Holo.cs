using System;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// ════════════════════ P70 — THE HOLOGRAM ════════════════════
/// The owner, 2026-10-01: "the game should look like a holographic projection of the battlefield.
/// Details in shapes, not entities. See it as a LiDAR scanner on every soldier that scans the
/// environment. The soldiers themselves are represented, not rendered." And, one message later:
/// "be careful with the visual style — if we limit colours and styling too much it will look bland
/// and boring very fast. Visual identity and colours help a lot."
///
/// So the projected view stops drawing a LIT BOARD (opaque bevelled blocks under a fake key light,
/// which is a tabletop seen from above and reads as 2D at any zoom that fits a big board) and draws
/// a RECONSTRUCTION: a dark projection plate, scan returns on the ground, every structure as bright
/// edges over a translucent body. Nothing is lit; everything EMITS.
///
/// COLOUR IS INFORMATION, NOT DECORATION, AND THERE IS A LOT OF IT. Every biome gets its own
/// hologram set — four roles, chosen so the eight rooms do not share a key hue:
///   KEY     structures: high cover, walls, the instrument's main read of "something solid".
///   SECOND  low cover and props: a contrasting hue so "waist-high" and "head-high" never share a
///           colour, which a single-hue hologram would make them do.
///   GROUND  the plate and its scan returns.
///   GLOW    the highest thing on the board; plateaus ramp from GROUND toward it by tier, so height
///           reads as a colour gradient as well as a shape (the storeys item will lean on this).
/// Red/orange stay the HOSTILE's, gold stays the OBJECTIVE's and cyan the SQUAD's — terrain sets
/// are picked around those, except where a biome's identity IS that hue (MAGMA).
///
/// The tiers keep their meaning: VISIBLE is this set at full strength, scaled by how close the
/// observer stood (`Vision.Confidence`); REMEMBERED keeps P38/P39's line pass, recoloured from a
/// fixed instrument blue to this biome's KEY pulled toward grey, so a remembered room is still
/// that room; UNSEEN is still nothing at all.
///
/// PRESENTATION ONLY: no rule reads anything here, it spends no RNG draw (every scatter is
/// `Util.Hash3`) and moves no CRN stream. `SIGHTLINE_HOLO=0` restores the lit board exactly.
public static partial class View3D
{
    /// `SIGHTLINE_HOLO=0` draws the pre-P70 lit board.
    public static bool Holo = true;

    public readonly struct HoloSet
    {
        public readonly Color Key, Second, Ground, Glow;
        public HoloSet(Color key, Color second, Color ground, Color glow)
        { Key = key; Second = second; Ground = ground; Glow = glow; }
    }

    /// One set per biome, by NAME so a reorder of `Biome.All` cannot swap two rooms' identities.
    public static HoloSet HoloFor(Biome b) => b?.Name switch
    {
        "STEEL"   => new HoloSet(Pal.RGBA(118, 182, 255), Pal.RGBA(176, 150, 255), Pal.RGBA(52, 92, 150),  Pal.RGBA(214, 236, 255)),
        "ARID"    => new HoloSet(Pal.RGBA(255, 182, 140), Pal.RGBA(140, 214, 196), Pal.RGBA(136, 98, 50),  Pal.RGBA(255, 236, 190)),
        "TUNDRA"  => new HoloSet(Pal.RGBA(196, 238, 255), Pal.RGBA(126, 160, 255), Pal.RGBA(70, 116, 150), Pal.RGBA(240, 250, 255)),
        "VERDANT" => new HoloSet(Pal.RGBA(118, 236, 140), Pal.RGBA(110, 200, 255), Pal.RGBA(44, 112, 66),  Pal.RGBA(206, 255, 196)),
        "ASH"     => new HoloSet(Pal.RGBA(224, 212, 204), Pal.RGBA(130, 200, 210), Pal.RGBA(96, 84, 80),   Pal.RGBA(255, 240, 228)),
        "VOID"    => new HoloSet(Pal.RGBA(196, 140, 255), Pal.RGBA(110, 226, 236), Pal.RGBA(84, 56, 136), Pal.RGBA(236, 210, 255)),
        "NEON"    => new HoloSet(Pal.RGBA(76, 244, 232),  Pal.RGBA(255, 108, 204), Pal.RGBA(30, 104, 116), Pal.RGBA(206, 255, 250)),
        "MAGMA"   => new HoloSet(Pal.RGBA(255, 140, 70),  Pal.RGBA(196, 160, 255), Pal.RGBA(128, 52, 30), Pal.RGBA(255, 236, 200)),
        _         => new HoloSet(Pal.RGBA(118, 182, 255), Pal.RGBA(176, 150, 255), Pal.RGBA(52, 92, 150),  Pal.RGBA(214, 236, 255)),
    };

    static HoloSet HS => HoloFor(Scene);

    // ── The pass a primitive is being drawn in ──────────────────────────────────────────────
    /// `Solid` and `CoverProp` read this, the same one-seam discipline as `_wire`: every site that
    /// sets it clears it on the same straight line.
    const int HoloOff = 0, HoloLines = 1, HoloFill = 2;
    static int _holoPass;
    static Color _holoCol;

    /// How opaque a structure's body is. Low: the body is there so a wall reads as a surface and
    /// not a cage, but what is behind it must still read through, because that is what a
    /// projection does and what an operator needs.
    const float FillA = 0.16f;

    /// A colour at the strength the scan supports. Visible terrain is never drawn below 55%:
    /// the far edge of sight is still IN sight, and must not be mistaken for memory.
    static Color HoloStrength(Color c, float conf) =>
        Pal.Mix(Pal.Mix(c, Pal.Bg, 0.45f), c, Util.Clamp(conf, 0f, 1f));

    /// The REMEMBERED tier's colour under the hologram: the room's KEY, cooled and greyed, with
    /// confidence still carried by brightness AND saturation (two cues, as P39 wanted).
    static Color HoloMemory(float conf)
    {
        float k = Util.Clamp(conf, 0f, 1f);
        var hot = Pal.Mix(HS.Key, Pal.RGBA(160, 182, 204), 0.45f);
        var cold = Pal.Mix(hot, Pal.RGBA(86, 96, 110), 0.6f);
        return Raylib.Fade(Pal.Mix(cold, hot, k), 0.30f + 0.55f * k);
    }

    // ── THE PLATE ────────────────────────────────────────────────────────────────────────────
    /// The ground as the instrument has it: a dark plate where ground is known, a lattice in the
    /// room's ground hue, and scan RETURNS — short ticks scattered per tile, denser and brighter
    /// where the observer stood close. A return is a fact ("the beam hit floor here"), which is
    /// why the plate carries texture without carrying a single painted pixel of material.
    static void HoloPlate(Grid g)
    {
        var hs = HS;
        Color plateHot = Pal.Mix(Pal.Bg, hs.Ground, 0.30f), plateCold = Pal.Mix(Pal.Bg, hs.Ground, 0.12f);
        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                if (Terrain.Enabled && g.Ground != null && g.Ground[x, y] == GroundKind.Rift) continue;
                byte st = Vision.At(x, y); if (st == Vision.Unseen) continue;
                float conf = Vision.Confidence(x, y);
                var c = st == Vision.Visible ? Pal.Mix(plateCold, plateHot, conf) : plateCold;
                Raylib.DrawCube(TileWorld(x, y, -0.05f), 1f, 0.1f, 1f, c);
            }

        bool bound = Wire.Begin(g.W, Renderer.NowPublic);
        Color grid = Pal.Mix(hs.Ground, hs.Key, 0.25f);
        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                byte st = Vision.At(x, y); if (st == Vision.Unseen) continue;
                if (Terrain.Enabled && g.Ground != null && g.Ground[x, y] == GroundKind.Rift) continue;
                bool vis = st == Vision.Visible;
                float conf = Vision.Confidence(x, y);
                float y0 = 0.004f;
                var gc = Raylib.Fade(grid, vis ? 0.30f + 0.35f * conf : 0.16f);
                Raylib.DrawLine3D(new Vector3(x, y0, y), new Vector3(x + 1, y0, y), gc);
                Raylib.DrawLine3D(new Vector3(x, y0, y), new Vector3(x, y0, y + 1), gc);
                if (x == g.W - 1) Raylib.DrawLine3D(new Vector3(x + 1, y0, y), new Vector3(x + 1, y0, y + 1), gc);
                if (y == g.H - 1) Raylib.DrawLine3D(new Vector3(x, y0, y + 1), new Vector3(x + 1, y0, y + 1), gc);

                // Returns. More of them where the look was close: that is what range costs a scan.
                int n = vis ? 3 + (int)(conf * 5f) : 2;
                var rc = Raylib.Fade(Pal.Mix(hs.Ground, hs.Glow, vis ? 0.35f + 0.35f * conf : 0.15f),
                                     vis ? 0.45f + 0.45f * conf : 0.22f);
                float hgt = g.Height[x, y] > 0 ? TierH * g.Height[x, y] + 0.006f : 0.008f;
                for (int i = 0; i < n; i++)
                {
                    float px = x + 0.1f + Hu(x, y, i * 2 + 1) * 0.8f, pz = y + 0.1f + Hu(x, y, i * 2 + 2) * 0.8f;
                    const float s = 0.035f;
                    Raylib.DrawLine3D(new Vector3(px - s, hgt, pz), new Vector3(px + s, hgt, pz), rc);
                    Raylib.DrawLine3D(new Vector3(px, hgt, pz - s), new Vector3(px, hgt, pz + s), rc);
                }
            }
        // the plate's own boundary, in the room's key — the projection has an edge
        var e = Raylib.Fade(hs.Key, 0.26f);
        Raylib.DrawLine3D(new Vector3(0, 0.03f, 0), new Vector3(g.W, 0.03f, 0), e);
        Raylib.DrawLine3D(new Vector3(g.W, 0.03f, 0), new Vector3(g.W, 0.03f, g.H), e);
        Raylib.DrawLine3D(new Vector3(g.W, 0.03f, g.H), new Vector3(0, 0.03f, g.H), e);
        Raylib.DrawLine3D(new Vector3(0, 0.03f, g.H), new Vector3(0, 0.03f, 0), e);
        if (bound) Wire.End();
    }

    static float Hu(int x, int y, int salt) =>
        (Util.Hash3(x * 73856093, y * 19349663, salt + 0x4011) & 0xFFFFu) / 65536f;

    // ── THE STRUCTURES ───────────────────────────────────────────────────────────────────────
    /// The VISIBLE tier's objects in one of the two hologram passes. Called once for LINES (under
    /// the scan shader, depth-tested) and once for FILL (translucent, depth writes off), so the
    /// bodies never hide the edges behind them.
    static void HoloObjects(Grid g, int pass)
    {
        var hs = HS;
        int maxH = 1;
        for (int y = 0; y < g.H; y++) for (int x = 0; x < g.W; x++) maxH = Math.Max(maxH, g.Height[x, y]);
        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                if (Terrain.Enabled && g.Ground != null && g.Ground[x, y] == GroundKind.Rift) continue;
                if (Vision.At(x, y) != Vision.Visible) continue;
                float conf = Vision.Confidence(x, y);
                _holoPass = pass;
                int h = g.Height[x, y];
                float baseY = 0f;
                if (h > 0)
                {
                    // HEIGHT IS A COLOUR TOO: ground hue at the first tier, ramping to GLOW at the top.
                    float t = maxH <= 1 ? 0.55f : 0.30f + 0.70f * (h - 1) / (float)(maxH - 1);
                    _holoCol = HoloStrength(Pal.Mix(Pal.Mix(hs.Ground, hs.Key, 0.6f), hs.Glow, t * 0.6f), conf);
                    float ht = TierH * h;
                    Solid(TileWorld(x, y, ht * 0.5f), 1f, ht, 1f, _holoCol, _holoCol);
                    baseY = ht;
                }
                var tt = g.Tiles[x, y];
                if (tt == TileType.HighCover) { _holoCol = HoloStrength(hs.Key, conf); CoverProp(g, x, y, baseY, true, 1f); }
                else if (tt == TileType.LowCover) { _holoCol = HoloStrength(hs.Second, conf); CoverProp(g, x, y, baseY, false, 1f); }
                if (g.Barrel[x, y])
                {
                    _holoCol = HoloStrength(Pal.RGBA(255, 186, 64), conf);   // explosive: hazard amber everywhere
                    Solid(TileWorld(x, y, baseY + 0.32f), 0.52f, 0.64f, 0.52f, _holoCol, _holoCol);
                }
                _holoPass = HoloOff;
            }
    }

    /// The biome's mechanical ground (C4/P16), as returns in its own colour rather than as painted
    /// slabs: fern blades are vertical green ticks, a drift is a pale outlined sheet, sand is a
    /// hatch, a vent is a hot cross-cut. These hues are the ground's identity and they are what
    /// tells the operator WHY a tile costs what it costs.
    static void HoloGround(Grid g)
    {
        if (!Terrain.Enabled || g.Ground == null) return;
        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                byte st = Vision.At(x, y); if (st == Vision.Unseen) continue;
                if (g.Height[x, y] > 0) continue;
                float a = st == Vision.Visible ? 0.55f + 0.4f * Vision.Confidence(x, y) : 0.25f;
                switch (g.Ground[x, y])
                {
                    case GroundKind.Undergrowth:
                    {
                        var c = Raylib.Fade(Pal.RGBA(110, 236, 120), a);
                        for (int i = 0; i < 6; i++)
                        {
                            float bx = x + 0.15f + Hu(x, y, i * 7 + 1) * 0.7f, bz = y + 0.15f + Hu(x, y, i * 7 + 2) * 0.7f;
                            float bh = 0.14f + Hu(x, y, i * 7 + 3) * 0.22f;
                            Raylib.DrawLine3D(new Vector3(bx, 0.01f, bz), new Vector3(bx + 0.03f, bh, bz), c);
                        }
                        break;
                    }
                    case GroundKind.Ice:
                    {
                        var c = Pal.RGBA(170, 220, 255);
                        Raylib.DrawCube(TileWorld(x, y, 0.012f), 0.94f, 0.012f, 0.94f, Raylib.Fade(c, a * 0.22f));
                        Raylib.DrawCubeWires(TileWorld(x, y, 0.014f), 0.94f, 0.0f, 0.94f, Raylib.Fade(c, a * 0.8f));
                        break;
                    }
                    case GroundKind.Sand:
                    {
                        var c = Raylib.Fade(Pal.RGBA(236, 190, 110), a * 0.7f);
                        for (int i = 0; i < 4; i++)
                        {
                            float o = 0.12f + i * 0.25f;
                            Raylib.DrawLine3D(new Vector3(x + o, 0.012f, y + 0.08f), new Vector3(x + o + 0.12f, 0.012f, y + 0.92f), c);
                        }
                        break;
                    }
                    case GroundKind.Vent:
                    {
                        var c = Raylib.Fade(Pal.RGBA(255, 120, 50), Math.Min(1f, a + 0.2f));
                        Raylib.DrawLine3D(new Vector3(x + 0.15f, 0.02f, y + 0.2f), new Vector3(x + 0.85f, 0.02f, y + 0.8f), c);
                        Raylib.DrawLine3D(new Vector3(x + 0.85f, 0.02f, y + 0.2f), new Vector3(x + 0.15f, 0.02f, y + 0.8f), c);
                        Raylib.DrawCube(TileWorld(x, y, 0.01f), 0.6f, 0.01f, 0.6f, Raylib.Fade(c, a * 0.25f));
                        break;
                    }
                }
            }
    }

    /// The whole terrain under the hologram. Order: plate, ground, LINES (scan shader on), the
    /// remembered tier's lines, then every translucent BODY last with depth writes off — a body
    /// that wrote depth would swallow the edges behind it, and the edges are the information.
    static void DrawTerrainHolo(Grid g)
    {
        HoloPlate(g);
        HoloGround(g);

        bool bound = Wire.Begin(g.W, Renderer.NowPublic);
        HoloObjects(g, HoloLines);
        if (g.AnyEdges && Edges.Enabled) { _holoPass = HoloLines; DrawEdgePass(g, false); _holoPass = HoloOff; }
        if (bound) Wire.End();

        if (Vision.Enabled) WirePass(g, () => DrawObjects(g, true));
        if (Vision.Enabled && g.AnyEdges && Edges.Enabled) WirePass(g, () => DrawEdgePass(g, true));

        // rifts: a cut through the plate, drawn as its rim
        if (Terrain.Enabled && g.Ground != null)
            for (int y = 0; y < g.H; y++)
                for (int x = 0; x < g.W; x++)
                    if (g.Ground[x, y] == GroundKind.Rift && Vision.At(x, y) != Vision.Unseen)
                        Raylib.DrawCubeWires(TileWorld(x, y, -0.05f), 0.98f, 0.02f, 0.98f, Raylib.Fade(HS.Second, 0.35f));

        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthMask();
        HoloFill_();
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();

        // ...and then the same bodies again into DEPTH ONLY. A projection is see-through to the
        // eye, but what stands on the floor must still hide the PAINT on the floor behind it (P44:
        // a threat zone must not draw over the wall in front of it) and the chips behind it. The
        // bodies blend freely with one another above; this pass only teaches the depth buffer
        // where they are, for everything drawn after the terrain.
        Rlgl.ColorMask(false, false, false, false);
        HoloFill_();
        Rlgl.DrawRenderBatchActive();
        Rlgl.ColorMask(true, true, true, true);

        void HoloFill_()
        {
            HoloObjects(g, HoloFill);
            if (g.AnyEdges && Edges.Enabled) { _holoPass = HoloFill; DrawEdgePass(g, false); _holoPass = HoloOff; }
        }
    }
}
