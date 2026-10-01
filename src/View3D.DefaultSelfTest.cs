using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// SIGHTLINE_VIEWDEFAULTTEST — item A (2026-10-01). The projected view is the player's default, so
/// its opening framing has to put the board where it can be READ.
///
/// (A) At Zoom 1, on 18x11, 36x22 and 48x30, at four yaws and three pitches, every corner of the
///     board, at the floor AND at the tallest geometry's height, projects inside the band between
///     the top bar and the action bar. `FitHudBand = false` is the control, and it must FAIL that
///     on at least one board, or the leg cannot tell the fix from the defect.
/// (B) The launch rule: projected unless the player chose flat, and SIGHTLINE_VIEW3D=0 forces flat.
/// (C) `Display.FlatView` round-trips through the settings DTO.
///
/// WHAT IT CANNOT SEE: whether the projected view PLAYS better as the default. That is the owner's
/// call (`docs/DESIGN.md` §6.6), not a measurement.
public static partial class View3D
{
    public static string ViewDefaultSelfTest()
    {
        var fails = new List<string>();
        var detail = new System.Text.StringBuilder();
        int w0 = Cfg.GridW, h0 = Cfg.GridH, t0 = Cfg.Tile;
        float p0 = PitchDeg, y0 = YawDeg, z0 = Zoom; var pan0 = Pan; bool fit0 = FitHudBand;
        float aspect = (float)Cfg.ScreenW / Cfg.ScreenH;
        float top = Cfg.HudTopInset, bot = Cfg.ScreenH - Cfg.HudBotInset;
        try
        {
            // returns the worst overshoot (px) of any corner past the band, 0 if all inside
            float Worst(int w, int h)
            {
                Cfg.SetBoard(w, h);
                var g = new Grid();
                var cam = MakeCamera(g, aspect);
                float worst = 0f;
                foreach (float hy in new[] { 0f, HighH + TierH * 2f })
                    foreach (var (cx, cz) in new[] { (0f, 0f), (w, 0f), (0f, h), ((float)w, (float)h) })
                    {
                        var sp = Raylib.GetWorldToScreenEx(new Vector3(cx, hy, cz), cam, Cfg.ScreenW, Cfg.ScreenH);
                        worst = MathF.Max(worst, MathF.Max(top - sp.Y, sp.Y - bot));
                        worst = MathF.Max(worst, MathF.Max(-sp.X, sp.X - Cfg.ScreenW));
                    }
                return worst;
            }

            // ── (A) the board fits the band; the control does not ──────────────────────────────
            int cells = 0; float controlWorst = 0f;
            foreach (var (w, h) in new[] { (18, 11), (36, 22), (48, 30) })
            {
                float boardWorst = 0f;
                foreach (float pitch in new[] { 52f, PitchMin, PitchMax })
                    foreach (float yaw in new[] { 0f, 45f, 90f, 180f })
                    {
                        PitchDeg = pitch; YawDeg = yaw; Zoom = 1f; Pan = Vector2.Zero;
                        FitHudBand = true;  float on = Worst(w, h);
                        FitHudBand = false; float off = Worst(w, h);
                        FitHudBand = true;
                        cells++;
                        boardWorst = MathF.Max(boardWorst, on);
                        controlWorst = MathF.Max(controlWorst, off);
                        if (on > 1f)
                            fails.Add($"(A) {w}x{h} pitch {pitch} yaw {yaw}: a board corner lands {on:F0}px outside the band between the bars");
                    }
                detail.Append($"{w}x{h} worst {boardWorst:F1}px; ");
            }
            detail.Append($"control (whole-screen framing) worst {controlWorst:F0}px over {cells} cells; ");
            if (controlWorst <= 1f)
                fails.Add("(A) the whole-screen control never put a corner under the bars — the leg cannot tell the fix from the defect");

            // ── (B) the launch rule ──────────────────────────────────────────────────────────────
            if (!LaunchEnabled(null, false)) fails.Add("(B) a fresh player does not open in the projected view");
            if (LaunchEnabled(null, true)) fails.Add("(B) a player who chose flat is put back in the projected view");
            if (LaunchEnabled("0", false)) fails.Add("(B) SIGHTLINE_VIEW3D=0 does not force flat");

            // ── (C) the setting round-trips ──────────────────────────────────────────────────────
            foreach (bool v in new[] { true, false })
            {
                var dto = new Display.Dto { FlatView = v };
                string json = System.Text.Json.JsonSerializer.Serialize(dto, Display.DisplayJson.Default.Dto);
                var back = System.Text.Json.JsonSerializer.Deserialize(json, Display.DisplayJson.Default.Dto);
                if (back == null || back.FlatView != v) fails.Add($"(C) FlatView={v} did not survive the settings DTO");
            }
            var old = System.Text.Json.JsonSerializer.Deserialize("{}", Display.DisplayJson.Default.Dto);
            if (old == null || old.FlatView) fails.Add("(C) a display.json written before A does not default to the projected view");
        }
        catch (Exception e) { fails.Add($"threw: {e.Message}"); }
        finally
        {
            PitchDeg = p0; YawDeg = y0; Zoom = z0; Pan = pan0; FitHudBand = fit0;
            Cfg.SetBoard(w0, h0, t0);
        }
        return fails.Count == 0
            ? "VIEWDEFAULTTEST: PASS (at its opening framing the projected view puts every board corner, floor and "
              + "tallest-geometry height, inside the band between the top bar and the action bar on 18x11, 36x22 and "
              + "48x30 at four yaws and three pitches, and the whole-screen control does not; a fresh player opens in "
              + "the projected view, a player who chose flat stays flat, SIGHTLINE_VIEW3D=0 forces flat; FlatView "
              + "round-trips and an old settings file defaults to projected) [" + detail + "]"
            : "VIEWDEFAULTTEST: FAIL (" + string.Join(" | ", fails.Distinct()) + ") [" + detail + "]";
    }

    /// The real launch's view rule, pure so the self-test can read it (Program.RealMain calls it).
    public static bool LaunchEnabled(string envView3d, bool flatView) => envView3d == "0" ? false : !flatView;
}
