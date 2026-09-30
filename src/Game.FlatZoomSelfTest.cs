using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// SIGHTLINE_FLATZOOMTEST — P58. The flat camera zooms out far enough to see the whole board, and
/// the shipped 18x11 board is untouched.
///
/// Four properties, the last being the one most likely to break quietly: (A) the floor is exactly
/// 1 on the shipped board; (B) at the floor a big board fits the screen and (C) sits centred on it;
/// (D) at the floor, MOUSE PICKING still lands on the tile under the pointer — checked by projecting
/// tile centres to the screen through the live `ViewCamera` and picking them back through
/// `PickTile`, the one picking seam every click goes through; and (E) the zoom limits the input
/// handler applies actually reach the floor (the old anti-drift snap would have pinned it at 1).
public partial class Game
{
    public static string FlatZoomSelfTest()
    {
        var fails = new List<string>();
        var detail = new System.Text.StringBuilder();
        int savedW = Cfg.GridW, savedH = Cfg.GridH, savedTile = Cfg.Tile;
        bool saved3d = View3D.Enabled;
        try
        {
            View3D.Enabled = false;

            // ── (A) THE SHIPPED BOARD: floor is exactly 1 ──────────────────────────────────
            Cfg.SetBoard(18, 11, 64);
            if (FlatZoomFloor != 1f) fails.Add($"(A) 18x11@64 floor is {FlatZoomFloor}, must be exactly 1");

            foreach (var (w, h) in new[] { (36, 22), (48, 30), (72, 44) })
            {
                Cfg.SetBoard(w, h);                          // the tile a player actually gets
                var g = new Game { NoPersist = true };
                float floor = FlatZoomFloor;
                g.CamZoom = floor; g.CamPan = Vector2.Zero; g.ClampCamPan();
                var cam = g.ViewCamera(false);

                // ── (B) at the floor the whole board fits on screen ─────────────────────────
                var tl = Raylib.GetWorldToScreen2D(new Vector2(Cfg.OriginX, Cfg.OriginY), cam);
                var br = Raylib.GetWorldToScreen2D(new Vector2(Cfg.OriginX + Cfg.BoardW, Cfg.OriginY + Cfg.BoardH), cam);
                bool fits = tl.X >= -0.5f && tl.Y >= -0.5f && br.X <= Cfg.ScreenW + 0.5f && br.Y <= Cfg.ScreenH + 0.5f;
                if (floor >= 1f) fails.Add($"(B) {w}x{h}@{Cfg.Tile}: floor {floor} is not below 1 on a board bigger than the screen");
                if (!fits) fails.Add($"(B) {w}x{h}@{Cfg.Tile}: at the floor the board spans ({tl.X:F0},{tl.Y:F0})-({br.X:F0},{br.Y:F0}), not inside {Cfg.ScreenW}x{Cfg.ScreenH}");

                // ── (C) and centred ──────────────────────────────────────────────────────────
                var mid = (tl + br) / 2f;
                if (MathF.Abs(mid.X - Cfg.ScreenW / 2f) > 1.5f || MathF.Abs(mid.Y - Cfg.ScreenH / 2f) > 1.5f)
                    fails.Add($"(C) {w}x{h}: board centre lands at ({mid.X:F1},{mid.Y:F1}), not the screen centre");

                // ── (D) picking round-trips at the floor ─────────────────────────────────────
                int bad = 0, tried = 0;
                var probes = new List<(int x, int y)> { (0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1), (w / 2, h / 2) };
                for (int k = 0; k < 40; k++) probes.Add(((k * 7919) % w, (k * 104729) % h));
                foreach (var (tx, ty) in probes)
                {
                    var sp = Raylib.GetWorldToScreen2D(Util.TileCenter(tx, ty), cam);
                    tried++;
                    if (!g.PickTile(sp, out int px, out int py) || px != tx || py != ty) bad++;
                }
                if (bad > 0) fails.Add($"(D) {w}x{h}: {bad} of {tried} tile centres did not pick back to themselves at the floor");

                // ── (E) the limits the input handler applies reach the floor ─────────────────
                g.CamZoom = floor * 0.5f; g.ApplyZoomLimits();
                if (MathF.Abs(g.CamZoom - floor) > 1e-5f) fails.Add($"(E) {w}x{h}: zoom below the floor settled at {g.CamZoom}, expected {floor}");
                g.CamZoom = (floor + 1f) / 2f; g.ApplyZoomLimits();
                if (MathF.Abs(g.CamZoom - (floor + 1f) / 2f) > 1e-5f)
                    fails.Add($"(E) {w}x{h}: a zoom between the floor and 1 was not kept ({g.CamZoom}) — the anti-drift snap is eating it");

                detail.Append($"{w}x{h}@{Cfg.Tile} floor {floor:F3} fits, picks {tried - bad}/{tried}; ");
            }

            // (A, again) the snap still behaves exactly as before on the shipped board
            Cfg.SetBoard(18, 11, 64);
            var g0 = new Game { NoPersist = true };
            g0.CamZoom = 1.0004f; g0.ApplyZoomLimits();
            if (g0.CamZoom != 1f) fails.Add($"(A) 18x11: the anti-drift snap no longer snaps 1.0004 to 1 ({g0.CamZoom})");
            g0.CamZoom = 0.6f; g0.ApplyZoomLimits();
            if (g0.CamZoom != 1f) fails.Add($"(A) 18x11: zoom 0.6 settled at {g0.CamZoom}, must be held at 1 on a board that fits");
        }
        catch (Exception e) { fails.Add($"threw: {e.Message}"); }
        finally { View3D.Enabled = saved3d; Cfg.SetBoard(savedW, savedH, savedTile); }

        return fails.Count == 0
            ? "FLATZOOMTEST: PASS (the flat zoom floor is exactly 1 on the shipped 18x11 board and the anti-drift snap is "
              + "unchanged there; on 36x22, 48x30 and 72x44 at the tile a player actually gets, the floor is below 1, the "
              + "whole board fits the screen at it, sits centred, every probed tile centre picks back to itself through "
              + "PickTile, and the input handler's limits reach the floor and keep zooms between it and 1) [" + detail + "]"
            : "FLATZOOMTEST: FAIL (" + string.Join(" | ", fails.Distinct()) + ") [" + detail + "]";
    }
}
