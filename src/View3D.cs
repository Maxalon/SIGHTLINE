using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// ════════════════════ P27 PROTOTYPE — THE PROJECTED VIEW ════════════════════
/// A 3D camera over the EXISTING flat board. This is the cheap half of the "should SIGHTLINE
/// become a 3D tactics game?" question, built to be looked at and thrown away if it does not
/// convince: it changes NO data model, NO gameplay, and nothing outside this file.
///
/// THE ARCHITECTURE THAT MAKES IT CHEAP. The frame splits in two:
///   * TERRAIN goes 3D — floor grid, cover, plateaus and barrels as extruded boxes inside a
///     Camera3D pass. That is the ~30 draw sites below.
///   * EVERYTHING ELSE STAYS 2D — units, glyphs, rings, labels — drawn AFTER EndMode3D at a
///     screen position from Raylib.GetWorldToScreen. Renderer.cs has ~480 raw 2D draw calls and
///     they survive a move to 3D unchanged; only the ~52 tile->pixel conversion sites become
///     world->screen ones. That is why this is a view change and not a renderer rewrite.
///
/// AND THE ART DIRECTION PAYS FOR THE HARD PART. 2D drawn after a 3D pass is not depth-tested, so
/// a unit behind a building draws THROUGH it. Under any other art direction that is a bug needing
/// manual occlusion. For a HOLOGRAM — a tactical projection of the mission ground — a contact
/// reading faintly through a wall is what the thing would actually do. The stalk under each marker
/// (DrawOverlay) exists for the same reason: on a projected view you cannot tell a unit standing
/// ON a plateau from one standing BEHIND it, and a vertical tether to the floor resolves it
/// instantly. That is the readability question this prototype is FOR.
///
/// ORTHOGRAPHIC, not perspective: a tactics grid wants every tile the same size wherever it is.
///
/// Nothing here runs unless View3D.Enabled is set, and only SIGHTLINE_VIEW3DSHOT sets it.
public static class View3D
{
    /// Master gate. Default FALSE and never set by normal play or by any other harness hook, so
    /// every existing screenshot, self-test and balance run is untouched by construction.
    public static bool Enabled;

    /// Camera elevation above the horizon, degrees. 90 = straight down (today's game), 0 = ground
    /// level. The whole point of the prototype is that we do not know what this should be.
    public static float PitchDeg = 40f;

    /// Rotation around the board's vertical axis, degrees. 0 = today's orientation (north up).
    public static float YawDeg = 0f;

    /// Framing slack. 1.0 fits the board's rotated bounding box exactly; >1 pulls back.
    public static float Margin = 1.04f;

    // ── World mapping ────────────────────────────────────────────────────────────────────────
    // Tile (x,y) occupies the unit square [x,x+1] x [y,y+1] on the XZ plane; +Y is up. One tile is
    // one world unit, so a height of 1.0 is exactly one tile wide — the proportion a person reads
    // as "chest high" on a grid this size.
    const float LowH = 0.45f;    // low cover: hip height, you can see over it
    const float HighH = 1.15f;   // high cover: taller than a soldier — the only sight blocker
    const float TierH = 0.5f;    // one elevation tier
    const float CapH = 0.06f;    // the lit top plate that makes a box read as a solid

    public static Vector3 TileWorld(int x, int y, float h = 0f) => new Vector3(x + 0.5f, h, y + 0.5f);

    /// Frame the whole board for the current pitch/yaw. Orthographic FovY is the VERTICAL extent in
    /// world units; the horizontal extent is FovY * aspect. The board's footprint rotates with yaw,
    /// so the bounding box has to be recomputed per angle or the board drifts out of frame at 45.
    public static Camera3D MakeCamera(Grid g, float aspect)
    {
        float pitch = PitchDeg * MathF.PI / 180f;
        float yaw = YawDeg * MathF.PI / 180f;
        var target = new Vector3(g.W * 0.5f, (HighH + TierH) * 0.35f, g.H * 0.5f);

        float cs = MathF.Abs(MathF.Cos(yaw)), sn = MathF.Abs(MathF.Sin(yaw));
        float spanX = g.W * cs + g.H * sn;        // screen-horizontal footprint after rotation
        float spanZ = g.W * sn + g.H * cs;        // footprint running away from the camera
        // Depth compresses by sin(pitch) on screen; add headroom for the tallest geometry.
        float needV = spanZ * MathF.Sin(pitch) + (HighH + TierH * 2f) * MathF.Cos(pitch) + 1.0f;
        float needH = spanX + 1.0f;
        float fovY = MathF.Max(needV, needH / MathF.Max(aspect, 0.01f)) * Margin;

        var dir = new Vector3(MathF.Cos(pitch) * MathF.Sin(yaw),
                              MathF.Sin(pitch),
                              MathF.Cos(pitch) * MathF.Cos(yaw));
        return new Camera3D
        {
            Position = target + dir * 40f,   // ortho: distance affects only clipping/depth precision
            Target = target,
            Up = new Vector3(0, 1, 0),
            FovY = fovY,
            Projection = CameraProjection.Orthographic,
        };
    }

    static Color Fade(Color c, float a) => Raylib.Fade(c, a);

    // ── The 3D pass: terrain only ────────────────────────────────────────────────────────────
    /// FACE SHADING IS FAKED ON PURPOSE. Raylib's DrawCube takes ONE colour and does no lighting, so
    /// a plain cube renders every face identically and reads as a flat card — which defeats the
    /// entire point of an angled camera. The fix is the same trick the 2D renderer already uses, and
    /// the palette was already built for it: every cover and plateau colour ships as a SIDE/TOP pair
    /// (CoverHi/CoverHiTop, HighSide/HighA). So each solid is drawn as a dark body in the side
    /// colour plus a thin bright CAP in the top colour, and the volume reads instantly.
    static void Solid(Vector3 centre, float w, float h, float d, Color side, Color top, float capA = 1f)
    {
        Raylib.DrawCube(centre, w, h, d, side);
        // The cap sits ENTIRELY ON TOP of the body rather than flush with its top face. A flush cap
        // shares a plane with the face beneath it and the depth buffer cannot order them, which
        // stripes every tall block with z-fighting bands — visible and ugly the first time it was
        // rendered. Half a cap-height of clearance costs nothing and removes the artifact.
        var cap = centre with { Y = centre.Y + h * 0.5f + CapH * 0.5f };
        Raylib.DrawCube(cap, w * 0.99f, CapH, d * 0.99f, Fade(top, capA));
        Raylib.DrawCubeWires(centre, w, h, d, Fade(top, 0.45f));
    }

    public static void DrawTerrain(Grid g)
    {
        // Floor: a slab per tile in the board's own checker, so the ground reads as TILES rather
        // than as a void with things standing in it. Same FloorA/FloorB alternation as the 2D board.
        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                if (Terrain.Enabled && g.Ground != null && g.Ground[x, y] == GroundKind.Rift) continue;
                Raylib.DrawCube(TileWorld(x, y, -0.05f), 1f, 0.1f, 1f, ((x + y) & 1) == 0 ? Pal.FloorA : Pal.FloorB);
            }
        for (int x = 0; x <= g.W; x++)
            Raylib.DrawLine3D(new Vector3(x, 0.006f, 0), new Vector3(x, 0.006f, g.H), Pal.GridLine);
        for (int y = 0; y <= g.H; y++)
            Raylib.DrawLine3D(new Vector3(0, 0.006f, y), new Vector3(g.W, 0.006f, y), Pal.GridLine);

        var e = Fade(Pal.Accent, 0.55f);
        Raylib.DrawLine3D(new Vector3(0, 0.03f, 0), new Vector3(g.W, 0.03f, 0), e);
        Raylib.DrawLine3D(new Vector3(g.W, 0.03f, 0), new Vector3(g.W, 0.03f, g.H), e);
        Raylib.DrawLine3D(new Vector3(g.W, 0.03f, g.H), new Vector3(0, 0.03f, g.H), e);
        Raylib.DrawLine3D(new Vector3(0, 0.03f, g.H), new Vector3(0, 0.03f, 0), e);

        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                // A RIFT is impassable but TRANSPARENT and gives no cover (P16), so it must read as
                // ABSENCE, never as an obstacle — a hole cut clean through the plate.
                if (Terrain.Enabled && g.Ground != null && g.Ground[x, y] == GroundKind.Rift)
                {
                    Raylib.DrawCube(TileWorld(x, y, -0.55f), 0.99f, 1.0f, 0.99f, Pal.Bg);
                    Raylib.DrawCubeWires(TileWorld(x, y, -0.05f), 0.99f, 0.02f, 0.99f, Fade(Pal.HighEdge, 0.30f));
                    continue;
                }

                int h = g.Height[x, y];
                float baseY = 0f;
                if (h > 0)
                {
                    float ht = TierH * h;
                    Solid(TileWorld(x, y, ht * 0.5f), 1f, ht, 1f,
                          Pal.HighSide, ((x + y) & 1) == 0 ? Pal.HighA : Pal.HighB);
                    baseY = ht;
                }

                var t = g.Tiles[x, y];
                if (t == TileType.LowCover)
                    Solid(TileWorld(x, y, baseY + LowH * 0.5f), 0.86f, LowH, 0.86f, Pal.CoverLo, Pal.CoverLoTop);
                else if (t == TileType.HighCover)
                    Solid(TileWorld(x, y, baseY + HighH * 0.5f), 0.92f, HighH, 0.92f, Pal.CoverHi, Pal.CoverHiTop);

                if (g.Barrel[x, y])
                    Solid(TileWorld(x, y, baseY + 0.32f), 0.52f, 0.64f, 0.52f,
                          Pal.RGBA(110, 82, 24), Pal.VipGold);
            }
    }

    // ── The 2D pass: everything else, projected ──────────────────────────────────────────────
    /// Draw one unit marker at its projected screen position. This is the half that proves the
    /// point: it is ordinary 2D drawing — the same rings and glyphs Renderer.cs already uses — and
    /// the ONLY thing that changed is where the Vector2 came from.
    public static void DrawOverlay(Grid g, List<Unit> units, Camera3D cam)
    {
        // On-screen size of one world unit, measured rather than assumed — under orthographic it is
        // constant across the board, which is exactly why orthographic is the right projection here.
        Vector2 o = Raylib.GetWorldToScreen(new Vector3(0, 0, 0), cam);
        Vector2 ux = Raylib.GetWorldToScreen(new Vector3(1, 0, 0), cam);
        float px = Vector2.Distance(o, ux);

        // far-to-near so nearer markers land on top
        var order = new List<Unit>(units);
        order.Sort((a, b) =>
        {
            float da = Vector3.Distance(cam.Position, TileWorld(a.X, a.Y));
            float db = Vector3.Distance(cam.Position, TileWorld(b.X, b.Y));
            return db.CompareTo(da);
        });

        foreach (var u in order)
        {
            if (!u.Alive) continue;
            float baseY = TierH * g.HeightAt(u.X, u.Y);
            Vector2 foot = Raylib.GetWorldToScreen(TileWorld(u.X, u.Y, baseY + 0.02f), cam);
            Vector2 head = Raylib.GetWorldToScreen(TileWorld(u.X, u.Y, baseY + 0.85f), cam);

            bool friend = u.Team == Team.Player;
            Color ring = u.IsVip ? Pal.VipGold : (friend ? Pal.Friend : Pal.Elite);
            Color dk = u.IsVip ? Pal.VipDk : (friend ? Pal.FriendDk : Pal.EliteDk);

            // THE STALK. Without it a projected view cannot distinguish "on the plateau" from
            // "behind the plateau" — the single worst readability failure of an angled grid.
            Raylib.DrawLineEx(foot, head, MathF.Max(1.5f, px * 0.035f), Fade(ring, 0.55f));
            // ground anchor: a flat ellipse reads as contact with the floor plane
            Raylib.DrawEllipse((int)foot.X, (int)foot.Y, px * 0.34f, px * 0.34f * MathF.Sin(PitchDeg * MathF.PI / 180f),
                               Fade(ring, 0.22f));

            float r = px * 0.30f;
            Raylib.DrawCircleV(head, r, Fade(dk, 0.92f));
            Raylib.DrawCircleLines((int)head.X, (int)head.Y, r, ring);
            string ini = string.IsNullOrEmpty(u.Name) ? "?" : u.Name.Substring(0, 1);
            int fs = (int)MathF.Max(12f, r * 1.05f);
            Vector2 m = Cfg.Measure(ini, fs, 1f);
            Cfg.Text(ini, head - m * 0.5f, fs, 1f, ring);
        }
    }

    /// One full projected frame. Caller owns BeginDrawing/EndDrawing.
    public static void DrawFrame(Grid g, List<Unit> units)
    {
        Raylib.ClearBackground(Pal.Bg);
        var cam = MakeCamera(g, (float)Cfg.ScreenW / Cfg.ScreenH);
        Raylib.BeginMode3D(cam);
        DrawTerrain(g);
        Raylib.EndMode3D();
        DrawOverlay(g, units, cam);

        string label = $"PITCH {PitchDeg:0} DEG   YAW {YawDeg:0} DEG";
        Cfg.Text(label, new Vector2(18, 14), 18, 1f, Pal.TxtDim);
    }
}
