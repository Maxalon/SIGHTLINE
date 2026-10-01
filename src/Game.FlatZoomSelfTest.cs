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
/// 0d adds (F) the edge rows can be panned out from under the HUD at the floor and (G) auto-cam
/// follows a soldier into every corner — it had its own pre-P29 clamp, half the legal range.
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

                // ── (F) 0d: at the floor the edge rows can be panned OUT FROM UNDER the HUD ──
                //     The board fits the SCREEN at the floor, but the top bar and the action bar sit
                //     on its first and last rows; the clamp must let the view travel exactly far
                //     enough to put each edge on the band's edge — and pick correctly there.
                g.CamZoom = floor;
                foreach (var (dir, want, edgeName) in new[] { (99999f, Cfg.ScreenH - Cfg.HudBotInset, "bottom"),
                                                              (-99999f, Cfg.HudTopInset, "top") })
                {
                    g.CamPan = new Vector2(0f, dir); g.ClampCamPan();
                    var camF = g.ViewCamera(false);
                    float edgeY = dir > 0
                        ? Raylib.GetWorldToScreen2D(new Vector2(Cfg.OriginX, Cfg.OriginY + Cfg.BoardH), camF).Y
                        : Raylib.GetWorldToScreen2D(new Vector2(Cfg.OriginX, Cfg.OriginY), camF).Y;
                    if (MathF.Abs(edgeY - want) > 0.75f)
                        fails.Add($"(F) {w}x{h}: panned fully {edgeName}, the board's {edgeName} edge lands at y {edgeY:F1}, not on the HUD band's edge {want}");
                    int row = dir > 0 ? h - 1 : 0, badF = 0;
                    for (int x = 0; x < w; x += Math.Max(1, w / 9))
                    {
                        var sp = Raylib.GetWorldToScreen2D(Util.TileCenter(x, row), camF);
                        if (!g.PickTile(sp, out int px, out int py) || px != x || py != row) badF++;
                    }
                    if (badF > 0) fails.Add($"(F) {w}x{h}: {badF} {edgeName}-row tiles did not pick back to themselves panned to the band");
                }
                g.CamPan = Vector2.Zero; g.ClampCamPan();

                // ── (G) 0d: auto-cam can FOLLOW a soldier to every corner of a big board ──────
                //     It used its own pre-P29 clamp (±Board*0.5*(1-1/zoom)), which on 36x22 held it
                //     to about half the legal range — the corner soldier was framed off-screen.
                foreach (var (cx, cy) in new[] { (0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1) })
                {
                    g.CamZoom = AutoCamZoom; g.CamPan = AutoCamPan(cx, cy, g.BoardCenter);
                    var sp = Raylib.GetWorldToScreen2D(Util.TileCenter(cx, cy), g.ViewCamera(false));
                    float half = Cfg.Tile * AutoCamZoom / 2f;
                    bool inBand = sp.X - half >= -0.5f && sp.X + half <= Cfg.ScreenW + 0.5f
                               && sp.Y - half >= Cfg.HudTopInset - 0.5f && sp.Y + half <= Cfg.ScreenH - Cfg.HudBotInset + 0.5f;
                    if (!inBand) fails.Add($"(G) {w}x{h}: auto-cam on corner ({cx},{cy}) frames its tile at ({sp.X:F0},{sp.Y:F0}), not inside the band between the bars");
                }

                detail.Append($"{w}x{h}@{Cfg.Tile} floor {floor:F3} fits, picks {tried - bad}/{tried}; ");
            }

            // (A, again) the snap still behaves exactly as before on the shipped board
            Cfg.SetBoard(18, 11, 64);
            var g0 = new Game { NoPersist = true };
            g0.CamZoom = 1.0004f; g0.ApplyZoomLimits();
            if (g0.CamZoom != 1f) fails.Add($"(A) 18x11: the anti-drift snap no longer snaps 1.0004 to 1 ({g0.CamZoom})");
            g0.CamZoom = 0.6f; g0.ApplyZoomLimits();
            if (g0.CamZoom != 1f) fails.Add($"(A) 18x11: zoom 0.6 settled at {g0.CamZoom}, must be held at 1 on a board that fits");
            // (A, again) 0d is a big-board change: the board that FITS keeps the screen bounds, so
            // it does not pan at zoom 1 and its bottom row stays where it has always been.
            g0.CamZoom = 1f; g0.CamPan = new Vector2(0f, 99999f); g0.ClampCamPan();
            if (g0.CamPan != Vector2.Zero) fails.Add($"(A) 18x11: pans to {g0.CamPan} at zoom 1 — the HUD bands leaked onto a board that fits");
            // and auto-cam on it still frames every corner tile on screen
            foreach (var (cx, cy) in new[] { (0, 0), (17, 0), (0, 10), (17, 10) })
            {
                g0.CamZoom = AutoCamZoom; g0.CamPan = AutoCamPan(cx, cy, g0.BoardCenter);
                var sp = Raylib.GetWorldToScreen2D(Util.TileCenter(cx, cy), g0.ViewCamera(false));
                if (sp.X < 0 || sp.X > Cfg.ScreenW || sp.Y < 0 || sp.Y > Cfg.ScreenH)
                    fails.Add($"(A) 18x11: auto-cam on corner ({cx},{cy}) puts its centre off screen at ({sp.X:F0},{sp.Y:F0})");
            }
        }
        catch (Exception e) { fails.Add($"threw: {e.Message}"); }
        finally { View3D.Enabled = saved3d; Cfg.SetBoard(savedW, savedH, savedTile); }

        return fails.Count == 0
            ? "FLATZOOMTEST: PASS (the flat zoom floor is exactly 1 on the shipped 18x11 board and the anti-drift snap is "
              + "unchanged there; on 36x22, 48x30 and 72x44 at the tile a player actually gets, the floor is below 1, the "
              + "whole board fits the screen at it, sits centred, every probed tile centre picks back to itself through "
              + "PickTile, the input handler's limits reach the floor and keep zooms between it and 1; 0d: at the floor the "
              + "view pans exactly far enough to lift the bottom row above the action bar and drop the top row below the top "
              + "bar, picking there too, auto-cam frames all four corner tiles inside that band, and 18x11 still does not pan "
              + "at zoom 1) [" + detail + "]"
            : "FLATZOOMTEST: FAIL (" + string.Join(" | ", fails.Distinct()) + ") [" + detail + "]";
    }
}
