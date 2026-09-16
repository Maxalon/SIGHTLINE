using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// SIGHTLINE_DECALTEST — P44. The board's REGION feedback, as paint on the floor.
///
/// Every leg here measures PIXELS, because every claim this wave makes is about pixels: that the
/// sheet lands on the right tile the right way up, that geometry in front of it hides it, that it
/// climbs a plateau with the floor, that discovery gates it, and that binding a second framebuffer
/// in the middle of a frame does not strand the rest of that frame on the screen.
///
/// It drives the bake with a SYNTHETIC decal (`View3D.DecalProbeX/Y` paints one tile solid
/// magenta) rather than with a live threat zone. That is the difference between a test whose
/// discriminator is exactly known and one that measures whatever an overlay happened to paint —
/// P43's leg (H) is the reason this file does not make that mistake twice.
public static partial class View3D
{
    static bool IsProbe(Color c) => c.R > 180 && c.G < 90 && c.B > 180;

    public static string DecalSelfTest()
    {
        var fails = new List<string>();

        float savedP = PitchDeg, savedY = YawDeg, savedZ = Zoom;
        var savedPan = Pan;
        bool savedLayer = DecalLayer, savedVision = Vision.Enabled, savedEnabled = Enabled;
        int savedPx = DecalProbeX, savedPy = DecalProbeY;
        float aspect = (float)Cfg.ScreenW / Cfg.ScreenH;

        var game = new Game { NoPersist = true };
        game.StartMission(1);
        var grid = game.Grid;
        // A flat, empty board: every feature below is one this test PUTS there, so nothing the
        // mission generator happened to roll can mask a failure or fake a pass.
        for (int x = 0; x < grid.W; x++)
            for (int y = 0; y < grid.H; y++) { grid.Tiles[x, y] = TileType.Floor; grid.Height[x, y] = 0; }
        if (grid.Ground != null)
            for (int x = 0; x < grid.W; x++)
                for (int y = 0; y < grid.H; y++) grid.Ground[x, y] = GroundKind.None;

        const int PX = 5, PY = 4;          // asymmetric in BOTH axes: a v-flip or a u/v swap moves it

        // One frame of terrain + decal layer, read back. Deliberately NOT the whole playable frame:
        // chips, markers and the Fx bridge would all paint over the probe and the counts would stop
        // meaning anything.
        (int count, Vector2 centroid) Shoot(bool viaBridge = false)
        {
            var cam = MakeCamera(grid, aspect);
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
            BakeDecalsPublic(game);
            Raylib.BeginMode3D(cam);
            DrawTerrain(grid);
            if (!viaBridge) DrawDecalLayerPublic(grid);
            Raylib.EndMode3D();
            if (viaBridge)
            {
                // The PRE-P44 arm, reproduced: the same ink through the affine bridge after the 3D
                // pass, at chip height and with no depth test. It is the control leg (C) needs.
                BeginBridge(cam);
                Raylib.DrawRectangle(Cfg.OriginX + PX * Cfg.Tile, Cfg.OriginY + PY * Cfg.Tile,
                                     Cfg.Tile, Cfg.Tile, Pal.RGBA(255, 0, 255, 255));
                EndBridge();
            }
            Raylib.EndDrawing();

            var img = Raylib.LoadImageFromScreen();
            int n = 0; double sx = 0, sy = 0;
            for (int y = 0; y < img.Height; y++)
                for (int x = 0; x < img.Width; x++)
                    if (IsProbe(Raylib.GetImageColor(img, x, y))) { n++; sx += x; sy += y; }
            Raylib.UnloadImage(img);
            return (n, n == 0 ? new Vector2(-1, -1) : new Vector2((float)(sx / n), (float)(sy / n)));
        }

        try
        {
            Enabled = true; DecalLayer = true; Vision.Enabled = false;
            DecalProbeX = PX; DecalProbeY = PY;
            PitchDeg = 24f; YawDeg = 0f; Zoom = 1f; Pan = Vector2.Zero; ClampPan(grid);

            // ── (A) the target is the board's size, and it is REUSED ────────────────────────────
            var camA = MakeCamera(grid, aspect);
            Raylib.BeginDrawing(); BakeDecalsPublic(game); Raylib.EndDrawing();
            uint id1 = DecalTextureId;
            if (DecalW != grid.W * Cfg.Tile || DecalH != grid.H * Cfg.Tile)
                fails.Add($"(A) the sheet is {DecalW}x{DecalH}, the board is {grid.W * Cfg.Tile}x{grid.H * Cfg.Tile}");
            Raylib.BeginDrawing(); BakeDecalsPublic(game); Raylib.EndDrawing();
            if (id1 == 0) fails.Add("(A) no render target was allocated");
            else if (DecalTextureId != id1) fails.Add("(A) the sheet was re-allocated on the second frame");

            // ── (B) ORIENTATION. The ink must land on the probe TILE — not its mirror, not its
            // transpose. A render texture is stored bottom-up, so the v flip is a real hazard and
            // an asymmetric probe tile is the only thing that can see it.
            var flat = Shoot();
            if (flat.count < 400)
                fails.Add($"(B) only {flat.count} probe pixels reached the screen — the decal layer drew nothing to measure");
            else
            {
                var want = Raylib.GetWorldToScreen(TileWorld(PX, PY, DecalLiftPublic), MakeCamera(grid, aspect));
                float off = Vector2.Distance(flat.centroid, want);
                if (off > 4f)
                    fails.Add($"(B) the sheet landed at {flat.centroid.X:0},{flat.centroid.Y:0}; tile {PX},{PY} projects to {want.X:0},{want.Y:0} ({off:0.0}px out)");
                // and it is CONFINED to that tile: nothing outside the tile's own projected box.
                var c0 = Raylib.GetWorldToScreen(new Vector3(PX, DecalLiftPublic, PY), MakeCamera(grid, aspect));
                var c1 = Raylib.GetWorldToScreen(new Vector3(PX + 1, DecalLiftPublic, PY + 1), MakeCamera(grid, aspect));
                float w = MathF.Abs(c1.X - c0.X), h = MathF.Abs(c1.Y - c0.Y);
                if (flat.count > (w + 4f) * (h + 4f))
                    fails.Add($"(B) {flat.count} probe pixels for a tile that projects to {w:0}x{h:0} — the sheet is bleeding past its tile");
            }

            // ── (C) OCCLUSION, WHICH IS THE WHOLE WAVE ──────────────────────────────────────────
            // At yaw 0 the camera sits at +Z, so the NEARER tile is the one with the larger y. Put
            // a high wall there and the paint behind it must go away.
            grid.Tiles[PX, PY + 1] = TileType.HighCover;
            var walled = Shoot();
            var walledBridge = Shoot(viaBridge: true);
            grid.Tiles[PX, PY + 1] = TileType.Floor;

            if (flat.count >= 400)
            {
                float kept = walled.count / (float)flat.count;
                if (kept > 0.5f)
                    fails.Add($"(C) a high wall in front of the decal hid only {(1 - kept) * 100:0}% of it ({walled.count} of {flat.count} px survived)");
                // THE CONTROL: the same ink through the pre-P44 bridge is NOT hidden by that wall.
                // Without this the leg would pass on a build where the wall simply covered the
                // screen — it is the difference between "occlusion works" and "something is dark".
                if (walledBridge.count < flat.count * 0.85f)
                    fails.Add($"(C) the bridged control lost {(1 - walledBridge.count / (float)flat.count) * 100:0}% too — the wall is hiding the probe by geometry, not by depth");
            }

            // ── (D) ELEVATION. Paint climbs the plateau with the floor it is on.
            grid.Height[PX, PY] = 1;
            var raised = Shoot();
            grid.Height[PX, PY] = 0;
            if (flat.count >= 400 && raised.count >= 400)
            {
                var camD = MakeCamera(grid, aspect);
                var wantFlat = Raylib.GetWorldToScreen(TileWorld(PX, PY, DecalLiftPublic), camD);
                var wantUp = Raylib.GetWorldToScreen(TileWorld(PX, PY, TierH + DecalLiftPublic), camD);
                var moved = raised.centroid - flat.centroid;
                var expect = wantUp - wantFlat;
                if (expect.Length() < 4f)
                    fails.Add("(D) a tier is worth less than 4px on screen at this camera — the leg cannot discriminate");
                else if (Vector2.Distance(moved, expect) > 4f)
                    fails.Add($"(D) a tier moved the decal by {moved.X:0.0},{moved.Y:0.0}; the tile top moved by {expect.X:0.0},{expect.Y:0.0}");
            }

            // ── (E) DISCOVERY GATES IT. Paint on ground nobody has scanned is paint on nothing.
            Vision.Enabled = true;
            Vision.Reset(grid);
            var unseen = Shoot();
            Vision.Enabled = false;
            if (unseen.count > 0)
                fails.Add($"(E) {unseen.count} probe pixels drew on a board with nothing discovered");

            // ── (F) THE TEXT SINK records instead of drawing, and disarms cleanly.
            {
                var sink = new List<(string, Vector2, float, float, Color, bool)>();
                var saveSink = Cfg.TextSink;
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
                Cfg.TextSink = sink;
                Cfg.Text("SINK", new Vector2(100, 100), 20, 1f, Pal.RGBA(255, 0, 255));
                Cfg.TitleText("SINK", new Vector2(100, 200), 26, 1f, Pal.RGBA(255, 0, 255));
                Cfg.TextSink = saveSink;
                Raylib.EndDrawing();
                var img = Raylib.LoadImageFromScreen();
                int ink = 0;
                for (int y = 60; y < 240; y++)
                    for (int x = 60; x < 400; x++)
                        if (IsProbe(Raylib.GetImageColor(img, x, y))) ink++;
                Raylib.UnloadImage(img);
                if (sink.Count != 2) fails.Add($"(F) the sink recorded {sink.Count} of 2 strings");
                if (ink > 0) fails.Add($"(F) {ink} pixels of type were drawn while the sink was armed");
                if (Cfg.TextSink != null) fails.Add("(F) the sink was still armed after the bake");
            }

            // ── (H) PAINT DOES NOT WRITE DEPTH. The sheet covers every tile, so if its quads
            // write the depth buffer they sit above `DrawOverlays`' ground plates and swallow the
            // ENTIRE interaction layer — move range, path preview, hover box — with nothing drawn
            // on top to show for it. That is what the first build of this wave did.
            {
                var camH = MakeCamera(grid, aspect);
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
                BakeDecalsPublic(game);
                Raylib.BeginMode3D(camH);
                DrawTerrain(grid);
                DrawDecalLayerPublic(grid);
                // exactly what DrawOverlays does for a reachable tile, on the probe's own tile
                GroundQuadPublic(PX, PY, TierH * grid.HeightAt(PX, PY) + 0.03f, 0.08f, Pal.RGBA(0, 255, 0, 255));
                Raylib.EndMode3D();
                Raylib.EndDrawing();

                var img = Raylib.LoadImageFromScreen();
                int green = 0;
                for (int y = 0; y < img.Height; y++)
                    for (int x = 0; x < img.Width; x++)
                    { var c = Raylib.GetImageColor(img, x, y); if (c.G > 180 && c.R < 90 && c.B < 90) green++; }
                Raylib.UnloadImage(img);
                if (green < 200)
                    fails.Add($"(H) only {green} pixels of the interaction overlay survived the decal sheet — it is writing depth");
            }

            // ── (G) THE NESTED FRAMEBUFFER. `EndTextureMode` unbinds to the DEFAULT framebuffer,
            // not to whatever was bound before it — so a bake nested inside Display's target would
            // silently redirect the rest of the frame to the screen, and the blit of an empty
            // target would paint over it. Bake inside a real bound target, draw a mark AFTER the
            // bake, and read the TARGET back: the mark has to be in there.
            {
                Display.SelfTestBeginTarget();
                Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
                BakeDecalsPublic(game);
                Raylib.DrawRectangle(40, 40, 60, 60, Pal.RGBA(255, 0, 255, 255));
                Display.SelfTestEndTarget();

                var img = Raylib.LoadImageFromTexture(Display.ActiveTarget.Texture);
                int ink = 0;
                for (int y = 0; y < img.Height; y++)
                    for (int x = 0; x < img.Width; x++)
                        if (IsProbe(Raylib.GetImageColor(img, x, y))) ink++;
                Raylib.UnloadImage(img);
                if (ink < 2000)
                    fails.Add($"(G) only {ink} of ~3600 marked pixels landed in the render target — the bake stranded the frame on the default framebuffer");
                if (Display.TargetBound)
                    fails.Add("(G) the target was still marked bound after the leg ended");
            }
        }
        finally
        {
            DecalProbeX = savedPx; DecalProbeY = savedPy;
            DecalLayer = savedLayer; Vision.Enabled = savedVision; Enabled = savedEnabled;
            PitchDeg = savedP; YawDeg = savedY; Zoom = savedZ; Pan = savedPan;
        }

        return fails.Count == 0
            ? "DECALTEST: PASS (the sheet is board-sized and reused; it lands on its own tile the right way up and does not "
              + "bleed past it; a high wall in front hides it while the pre-P44 bridged control keeps it; it climbs a "
              + "tier with the floor; nothing draws on undiscovered ground; it does not write depth, so the interaction "
              + "overlay still draws over it; the text sink records instead of drawing and "
              + "disarms; a bake nested in a bound render target leaves the frame in that target)"
            : "DECALTEST: FAIL (" + string.Join(" | ", fails.Distinct()) + ")";
    }
}
