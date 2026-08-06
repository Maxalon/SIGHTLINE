using System;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

public struct UiButton
{
    public Rectangle Rect;
    public string Id;
    public string Label;
    public string Key;
    public bool Enabled;
    public bool Selected;
    public Color Accent;
}

/// All on-screen UI: top/bottom bars, action buttons, shot tooltip, overlays.
/// Button rects are stored after Draw so Game can hit-test clicks.
public static class Hud
{
    public static Rectangle EndTurnRect;
    public static UiButton[] ActionButtons = Array.Empty<UiButton>();
    public static System.Collections.Generic.List<(Rectangle rect, Unit unit)> RosterChips = new();
    public static Rectangle PauseResume, PauseMute, PauseShake, PauseThreat, PauseFullscreen, PauseWindow, PauseAbandon;
    public static Rectangle PauseBright, PauseGamma, PauseColorblind, PauseAutoCam, PauseCodex;
    // CODEX / FIELD MANUAL (W6): category tab rects + BACK, published by DrawCodex for hit-testing.
    public static readonly System.Collections.Generic.List<Rectangle> CodexTabBtns = new();
    public static Rectangle CodexBack;
    public static float CodexScrollMax;   // clamp bound for Game.CodexScroll (content overflow px)
    public static Rectangle PerkBtnA, PerkBtnB, PerkTagBtn;
    public static Rectangle SpecBtnA, SpecBtnB;   // W2: class-specialization fork chooser buttons
    public static Rectangle HeatMinus, HeatPlus;   // intro Heat/Ascension +/- selector
    public static Rectangle[] MissionCards = new Rectangle[3];
    public static System.Collections.Generic.List<(int Id, Rectangle Rect)> NodeBtns = new();
    public static Rectangle[] ShopBtns = new Rectangle[Game.ShopName.Length];
    public static Rectangle ShopProceed;
    public static Rectangle[] EventBtns = new Rectangle[3];   // W4: FIELD EVENT choice buttons
    // ARMORY sub-screen (re-arm a soldier): a toggle button + per-soldier rows + per-weapon rows.
    public static Rectangle ArmoryToggle;
    public static System.Collections.Generic.List<Rectangle> ArmorySoldierBtns = new();
    public static System.Collections.Generic.List<Rectangle> ArmoryWeaponBtns = new();
    // bench mechanic (S3-A): toggled in the barracks debrief for wounded soldiers
    public static System.Collections.Generic.List<(Unit unit, Rectangle rect)> BenchBtns = new();
    // run-scoped boon offer (Wave 3): the pick-1-of-3 boon cards in the barracks
    public static System.Collections.Generic.List<(Boon boon, Rectangle rect)> BoonBtns = new();
    // run-opening squad DRAFT (Wave 3): candidate cards + starting-boon cards + the DEPLOY button
    public static System.Collections.Generic.List<(Unit unit, Rectangle rect)> DraftCardBtns = new();
    public static System.Collections.Generic.List<(Boon boon, Rectangle rect)> DraftBoonBtns = new();
    public static System.Collections.Generic.List<(Contract contract, Rectangle rect)> DraftContractBtns = new();
    public static Rectangle DraftConfirm;
    public static Rectangle DraftBack;   // W1 mode-seam: BACK to the intro without founding a run
    public static Rectangle DraftReroll; // W9: paid draft-pool re-roll (salvage sink)
    public static Rectangle ShopReroll;  // W9: paid requisition-slate re-roll (salvage sink)
    // W9: per-soldier REHAB chips (buy off a scar), published by DrawSquadRow for hit-testing.
    public static System.Collections.Generic.List<(Unit unit, Rectangle rect)> RehabBtns = new();

    // ---------------- UI motion (panel pop-in juice) ----------------
    // Panels/cards animate in (slide + fade + scale) the first time they appear, instead
    // of popping. We key each animated element by a stable string and record the wall-clock
    // time it was first drawn (this frame); progress is (now - firstSeen)/duration eased.
    // Keys that go untouched for a moment are pruned so re-appearing panels re-animate
    // (e.g. re-entering the barracks). Deterministic w.r.t. when a phase becomes active:
    // by a fixed screenshot frame the intro/HUD has been visible long enough to settle.
    static readonly System.Collections.Generic.Dictionary<string, float> _animSeen = new();
    static readonly System.Collections.Generic.HashSet<string> _animTouched = new();
    static double _animLastPrune;

    /// Eased 0..1 entrance progress for the element identified by `key`. `dur` is the
    /// settle time (seconds). `delay` staggers the start (e.g. roster rows cascade).
    static float PanelAnim(string key, float dur = 0.22f, float delay = 0f)
    {
        float now = (float)Raylib.GetTime();
        _animTouched.Add(key);
        if (!_animSeen.TryGetValue(key, out float t0)) { t0 = now; _animSeen[key] = now; }
        float t = (now - t0 - delay) / MathF.Max(0.0001f, dur);
        return Util.Clamp(t, 0f, 1f);
    }

    /// Call once per frame (from Draw) AFTER all PanelAnim() calls for the frame: forget
    /// keys that weren't drawn this frame so they re-animate next time they appear.
    static void PruneAnims()
    {
        double now = Raylib.GetTime();
        // Prune a few times a second — cheap and avoids churn within a single frame.
        if (now - _animLastPrune < 0.05) { _animTouched.Clear(); return; }
        _animLastPrune = now;
        if (_animSeen.Count != _animTouched.Count)
        {
            var stale = new System.Collections.Generic.List<string>();
            foreach (var k in _animSeen.Keys) if (!_animTouched.Contains(k)) stale.Add(k);
            foreach (var k in stale) _animSeen.Remove(k);
        }
        _animTouched.Clear();
    }

    /// Translate a panel rect upward-into-place by `slidePx` as t goes 0->1 (ease-out).
    static Rectangle SlideIn(Rectangle r, float t, float slidePx = 18f)
    {
        float dy = (1f - Util.EaseOutQuad(t)) * slidePx;
        return new Rectangle(r.X, r.Y + dy, r.Width, r.Height);
    }

    /// Scale a rect about its centre by `s` (1 = identity) — used for a subtle pop.
    static Rectangle ScaleAbout(Rectangle r, float s)
    {
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        return new Rectangle(cx - r.Width * s / 2f, cy - r.Height * s / 2f, r.Width * s, r.Height * s);
    }

    public static void Draw(Game g)
    {
        DrawTopBar(g);
        if (g.Phase == Phase.PlayerTurn || g.Phase == Phase.EnemyTurn)
        {
            DrawRoster(g);
            DrawBoonStrip(g);   // active run boons, just under the top bar
            DrawCombatLog(g);   // rolling combat ledger, lower-right above the action bar
        }
        DrawBottomBar(g);
        DrawTooltip(g);
        DrawHudHovers(g);   // W11: objective-readout + boon-chip hover tooltips
        if ((g.Phase == Phase.PlayerTurn || g.Phase == Phase.EnemyTurn) && g.TutorialText != null)
            DrawTutorial(g);
        DrawBanner(g);
        DrawOverlays(g);
        if (g.Paused) DrawPause(g);
        if (g.EditingTag) DrawTagEditor(g);
        PruneAnims();   // forget panel-entrance keys not drawn this frame (re-animate on re-show)
    }

    // Onboarding tutorial callout (3.12): a non-blocking tip card above the action bar.
    static void DrawTutorial(Game g)
    {
        string body = g.TutorialText;
        int step = g.TutStep + 1, total = Game.TutPrompts.Length;
        int w = 760, x = Cfg.ScreenW / 2 - w / 2, pad = 16;
        // word-wrap the body at ~size 15
        var lines = WrapText(body, 15, w - pad * 2);
        int h = 40 + lines.Count * 20 + 10;
        // W11: anchor ABOVE the action bar's top row (_barTop) — the W10 bar can wrap into extra
        // upward rows, and the old fixed y=600 card sat on top of them. Never lower than 600.
        int y = Math.Min(600, (int)_barTop - h - 8);
        var card = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(card, 0.08f, 8, Raylib.Fade(Pal.RGBA(10, 16, 24), 0.96f));
        Raylib.DrawRectangleLinesEx(card, 1.8f, Pal.Accent);
        Raylib.DrawRectangle(x, y, 5, h, Pal.Accent);

        string head = $"TRAINING  {step}/{total}";
        Raylib.DrawTextEx(Cfg.Font, head, new Vector2(x + pad, y + 10), 14, 1f, Pal.Accent);
        int ty = y + 36;
        foreach (var ln in lines) { Raylib.DrawTextEx(Cfg.Font, ln, new Vector2(x + pad, ty), 15, 1f, Pal.Txt); ty += 20; }
    }

    // Greedy word-wrap to a pixel width.
    static System.Collections.Generic.List<string> WrapText(string s, int size, int maxW)
    {
        var outl = new System.Collections.Generic.List<string>();
        var words = s.Split(' ');
        string cur = "";
        foreach (var word in words)
        {
            string trial = cur.Length == 0 ? word : cur + " " + word;
            if ((int)Raylib.MeasureTextEx(Cfg.Font, trial, size, 1f).X > maxW && cur.Length > 0) { outl.Add(cur); cur = word; }
            else cur = trial;
        }
        if (cur.Length > 0) outl.Add(cur);
        return outl;
    }

    // ---------------- custom tag editor ----------------
    static void DrawTagEditor(Game g)
    {
        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.RGBA(5, 8, 11), 0.7f));
        int w = 460, h = 180;
        int x = Cfg.ScreenW / 2 - w / 2, y = Cfg.ScreenH / 2 - h / 2;
        var card = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(card, 0.06f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.5f, Pal.Accent);

        string who = g.TagTarget != null ? g.TagTarget.Name : "";
        Raylib.DrawTextEx(Cfg.Font, $"TAG  {who}", new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, $"TAG  {who}", 22, 1f).X / 2, y + 20), 22, 1f, Pal.Accent);

        var box = new Rectangle(x + 30, y + 64, w - 60, 40);
        Raylib.DrawRectangleRounded(box, 0.2f, 6, Pal.RGBA(10, 15, 21));
        Raylib.DrawRectangleLinesEx(box, 1.5f, Pal.PanelBd);
        string shown = g.TagBuffer + (((int)(Raylib.GetTime() * 2) % 2 == 0) ? "_" : " ");
        Raylib.DrawTextEx(Cfg.Font, shown, new Vector2((int)box.X + 12, (int)box.Y + 11), 20, 1f, Pal.Txt);
        if (g.TagBuffer.Length == 0)
            Raylib.DrawTextEx(Cfg.Font, "(blank = auto tags)", new Vector2((int)box.X + 12, (int)box.Y + 46), 11, 1f, Pal.TxtDim);

        string hint = "Type a role  -  [Enter] save  -  [Esc] cancel  -  [Backspace] delete";
        Raylib.DrawTextEx(Cfg.Font, hint, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, hint, 12, 1f).X / 2, y + h - 26), 12, 1f, Pal.TxtDim);
    }

    /// The label to show for a soldier: a player-set custom tag if present, else the
    /// top auto-derived strengths. Returns (text, isCustom).
    static (string text, bool custom) DisplayTag(Unit u)
    {
        if (!string.IsNullOrEmpty(u.CustomTag)) return (u.CustomTag, true);
        var sp = Specialties(u);
        return sp.Count == 0 ? ("", false) : (string.Join(" ", sp.GetRange(0, Math.Min(2, sp.Count))), false);
    }

    // ---------------- pause / settings ----------------
    static void DrawPause(Game g)
    {
        // scrim fades in with the card so the pause lands rather than snaps
        float in_ = PanelAnim("pause", 0.13f);
        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.RGBA(5, 8, 11), 0.82f * Util.EaseOutQuad(in_)));
        int w = 440, h = 771;   // W9: grew for the GAMMA row (12 buttons)
        int x = Cfg.ScreenW / 2 - w / 2, y = Cfg.ScreenH / 2 - h / 2;
        y -= (int)((1f - Util.EaseOutQuad(in_)) * 14f);
        var card = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(card, 0.05f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.5f, Pal.PanelBd);

        Raylib.DrawTextEx(Cfg.Font, "PAUSED", new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, "PAUSED", 40, 1f).X / 2, y + 22), 40, 1f, Pal.Friend);

        int bw = 320, bh = 42, bx = x + w / 2 - bw / 2, by = y + 84, gap = 11;
        PauseResume     = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseFullscreen = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseWindow     = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseMute       = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseShake      = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseThreat     = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseBright     = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseGamma      = new Rectangle(bx, by, bw, bh); by += bh + gap;   // W9: true gamma (post-FX)
        PauseColorblind = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseAutoCam    = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseCodex      = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseAbandon    = new Rectangle(bx, by, bw, bh);

        DrawButtonRect(PauseResume, "RESUME", "ESC", true, false, Pal.Friend);
        DrawButtonRect(PauseFullscreen, Display.Fullscreen ? "FULLSCREEN: ON" : "FULLSCREEN: OFF", "F", true, !Display.Fullscreen, Pal.Accent);
        DrawButtonRect(PauseWindow, "WINDOW: " + Display.SizeLabel, "", true, false, Pal.Accent);
        DrawButtonRect(PauseMute, Audio.Enabled ? "AUDIO: ON" : "AUDIO: OFF", "M", true, !Audio.Enabled, Pal.Accent);
        DrawButtonRect(PauseShake, g.Fx.ShakeOn ? "SCREEN SHAKE: ON" : "SCREEN SHAKE: OFF", "", true, !g.Fx.ShakeOn, Pal.Accent);
        DrawButtonRect(PauseThreat, g.ShowThreatPref ? "THREAT PREVIEW: ON" : "THREAT PREVIEW: OFF", "", true, !g.ShowThreatPref, Pal.Accent);
        DrawButtonRect(PauseBright, "BRIGHTNESS: " + Display.BrightLabel, "", true, false, Pal.Accent);
        DrawButtonRect(PauseGamma, "GAMMA: " + Display.GammaLabel, "", true, false, Pal.Accent);
        DrawButtonRect(PauseColorblind, Pal.Colorblind ? "COLORBLIND: ON" : "COLORBLIND: OFF", "", true, Pal.Colorblind, Pal.Accent);
        DrawButtonRect(PauseAutoCam, Display.AutoCam ? "AUTO-CAM: ON" : "AUTO-CAM: OFF", "", true, Display.AutoCam, Pal.Accent);
        DrawButtonRect(PauseCodex, "FIELD MANUAL", "K", true, false, Pal.Good);
        // W1 mode-seam: the abandon verb is mode-true — a stand/fight is not a campaign "run".
        string abandonLbl = g.Mode == GameMode.Endless ? "END STAND"
                          : g.Mode == GameMode.Skirmish ? "ABANDON FIGHT"
                          : "ABANDON RUN";
        DrawButtonRect(PauseAbandon, abandonLbl, "", true, false, Pal.Foe);

        string ctl = "Wheel zoom  -  Middle-drag pan  -  [C] reset camera  -  Arrows/WASD + [Space]";
        Raylib.DrawTextEx(Cfg.Font, ctl, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, ctl, 11, 1f).X / 2, y + h - 24), 11, 1f, Pal.TxtDim);
    }

    /// W9: true when any alive unit's on-screen figure overlaps `chip` — the roster strip
    /// covers board column 0 (and grazes col 1), so a soldier standing there used to vanish
    /// under the HUD. The camera transform (pan/zoom, no shake — same as mouse picking) maps
    /// the unit's tweened world pos to screen space; the half-extent approximates the figure.
    static bool ChipOccluded(Game g, Rectangle chip, bool ignoreDormant = false)
    {
        var cam = g.ViewCamera(false);
        float half = 26f * cam.Zoom;
        bool Hits(Unit v)
        {
            if (!v.Alive) return false;
            var sp = Raylib.GetWorldToScreen2D(v.Pos, cam);
            return Raylib.CheckCollisionRecs(chip, new Rectangle(sp.X - half, sp.Y - half, half * 2, half * 2));
        }
        foreach (var v in g.Players) if (Hits(v)) return true;
        foreach (var v in g.Enemies)
        {
            if (ignoreDormant && !v.Active) continue;   // a quiet slate marker isn't worth hiding a verb for
            if (Hits(v)) return true;
        }
        return false;
    }

    static void DrawRoster(Game g)
    {
        RosterChips.Clear();
        // FUL-3: chips span x 8..140 and the board starts at Cfg.OriginX=64, so board column 0
        // lives UNDER the strip — and squads spawn in the left columns, which made the W11
        // in-place collapse fire on the squad's own formation from turn 1 in most missions.
        // An occluded chip now REFLOWS full-size into the unused strip below the roster (the
        // 12px gap marks it displaced); the 20px rail collapse survives only as the last resort
        // when every overflow slot is blocked too.
        var alive = new List<Unit>(g.AlivePlayers());
        int n = alive.Count;
        var slot = new Rectangle[n];
        var collapsed = new bool[n];
        float overflowY = 70 + n * 64 + 12;
        float bottom = Cfg.OriginY + Cfg.BoardH;
        for (int i = 0; i < n; i++)
        {
            var rest = new Rectangle(8, 70 + i * 64, 132, 58);
            slot[i] = rest;
            if (!ChipOccluded(g, rest)) continue;
            collapsed[i] = true;
            for (float oy = overflowY; oy + 58 <= bottom; oy += 64)
            {
                var cand = new Rectangle(8, oy, 132, 58);
                if (ChipOccluded(g, cand)) continue;
                slot[i] = cand; collapsed[i] = false; overflowY = oy + 64; break;
            }
        }
        for (int idx = 0; idx < n; idx++)
        {
            var u = alive[idx];
            // assigned rect (natural or overflow slot; used for click hit-testing) + a one-time
            // staggered slide-in from the left edge so the strip assembles itself when combat opens.
            var rest = slot[idx];
            float slideIn = PanelAnim("roster:" + u.Name, 0.28f, idx * 0.05f);
            var r = new Rectangle(rest.X - (1f - Util.EaseOutQuad(slideIn)) * 26f, rest.Y, rest.Width, rest.Height);
            bool sel = g.Selected == u;
            bool spent = g.Phase == Phase.PlayerTurn && !u.CanAct;
            float a = spent ? 0.5f : 1f;
            if (collapsed[idx])
            {
                int y = (int)rest.Y;
                var rail = new Rectangle(0, y, 20, 58);
                Raylib.DrawRectangleRounded(rail, 0.25f, 4, Raylib.Fade(sel ? Pal.RGBA(26, 36, 48) : Pal.Panel, 0.9f * a));
                Raylib.DrawRectangleLinesEx(rail, 1f, Raylib.Fade(sel ? Pal.Accent : Pal.PanelBd, a));
                Raylib.DrawRectangle(0, y, 2, 58, Raylib.Fade(sel ? Pal.Accent : Pal.Friend, a));
                string ini = string.IsNullOrEmpty(u.Name) ? "?" : u.Name.Substring(0, 1);
                int iw = (int)Raylib.MeasureTextEx(Cfg.Font, ini, 13, 1f).X;
                Raylib.DrawTextEx(Cfg.Font, ini, new Vector2(10 - iw / 2, y + 5), 13, 1f, Raylib.Fade(Pal.Txt, a));
                // vertical HP sliver, filling bottom-up, same banding as the full bar
                var vbar = new Rectangle(7, y + 24, 6, 28);
                Raylib.DrawRectangleRounded(vbar, 0.5f, 4, Raylib.Fade(Pal.RGBA(10, 15, 21), a));
                float vfrac = u.MaxHp > 0 ? u.Hp / (float)u.MaxHp : 0;
                if (vfrac > 0)
                {
                    Color vc = vfrac > 0.5f ? Pal.Good : (vfrac > 0.25f ? Pal.Accent : Pal.Foe);
                    float fh = vbar.Height * vfrac;
                    Raylib.DrawRectangleRounded(new Rectangle(vbar.X, vbar.Y + vbar.Height - fh, vbar.Width, fh), 0.5f, 4, Raylib.Fade(vc, a));
                }
                RosterChips.Add((rail, u));   // hit-test the rail, not the vacated chip footprint
                continue;
            }
            float fillA = a;
            float txtA  = a;

            PanelShadow(r, fillA);
            Raylib.DrawRectangleRounded(r, 0.16f, 6, Raylib.Fade(sel ? Pal.RGBA(26, 36, 48) : Pal.Panel, fillA));
            Raylib.DrawRectangleLinesEx(r, 1.5f, Raylib.Fade(sel ? Pal.Accent : Pal.PanelBd, fillA));
            Raylib.DrawRectangle((int)r.X, (int)r.Y, 3, (int)r.Height, Raylib.Fade(sel ? Pal.Accent : Pal.Friend, fillA));

            Raylib.DrawTextEx(Cfg.Font, u.Name, new Vector2((int)r.X + 9, (int)r.Y + 5), 13, 1f, Raylib.Fade(Pal.Txt, txtA));
            int nameW = (int)Raylib.MeasureTextEx(Cfg.Font, u.Name, 13, 1f).X;
            if (!string.IsNullOrEmpty(u.Nickname))   // earned callsign, in quotes
                Raylib.DrawTextEx(Cfg.Font, $"\"{u.Nickname}\"", new Vector2((int)r.X + 9 + nameW + 5, (int)r.Y + 6), 11, 1f, Raylib.Fade(Pal.VipGold, txtA));
            // status marks (right): OW/HK, else a live BOND aura when a partner is adjacent
            if (u.OnOverwatch) Raylib.DrawTextEx(Cfg.Font, "OW", new Vector2((int)r.X + 96, (int)r.Y + 5), 11, 1f, Raylib.Fade(Pal.Accent, txtA));
            else if (u.Hunkered) Raylib.DrawTextEx(Cfg.Font, "HK", new Vector2((int)r.X + 96, (int)r.Y + 5), 11, 1f, Raylib.Fade(Pal.Good, txtA));
            else if (u.BondAura) Raylib.DrawTextEx(Cfg.Font, "BOND", new Vector2((int)r.X + 88, (int)r.Y + 5), 11, 1f, Raylib.Fade(Pal.VipGold, txtA));

            // hp bar
            var bar = new Rectangle(r.X + 9, r.Y + 23, 102, 6);
            Raylib.DrawRectangleRounded(bar, 0.5f, 4, Raylib.Fade(Pal.RGBA(10, 15, 21), txtA));
            float frac = u.MaxHp > 0 ? u.Hp / (float)u.MaxHp : 0;
            if (frac > 0)
            {
                Color hc = frac > 0.5f ? Pal.Good : (frac > 0.25f ? Pal.Accent : Pal.Foe);
                Raylib.DrawRectangleRounded(new Rectangle(bar.X, bar.Y, bar.Width * frac, bar.Height), 0.5f, 4, Raylib.Fade(hc, txtA));
            }
            // AP pips
            for (int i = 0; i < 2; i++)
            {
                var pip = new Rectangle(r.X + 9 + i * 22, r.Y + 34, 18, 6);
                bool on = i < u.ActionsLeft;
                Raylib.DrawRectangleRounded(pip, 0.5f, 4, Raylib.Fade(on ? Pal.Accent : Pal.RGBA(28, 39, 51), txtA));
            }
            Raylib.DrawTextEx(Cfg.Font, u.IsVip ? "ASSET" : u.RankName, new Vector2((int)r.X + 58, (int)r.Y + 33), 11, 1f, Raylib.Fade(u.IsVip ? Pal.VipGold : Pal.TxtDim, txtA));
            // W12: the class silhouette in the chip's lower-right corner — the same shape the board
            // draws, so chip -> soldier matching is instant even before names are learned.
            Renderer.DrawCodexGlyph(u.Cls, new Vector2(r.X + 118, r.Y + 39),
                Raylib.Fade(u.IsVip ? Pal.VipGold : Pal.Friend, (sel ? 1f : 0.75f) * txtA), 0.8f);

            // role tag: WOUNDED (red) takes priority, else custom tag (cyan) / auto strengths (amber)
            if (!u.IsVip)
            {
                if (u.Wound > 0)
                    Raylib.DrawTextEx(Cfg.Font, $"WOUNDED ({u.Wound})", new Vector2((int)r.X + 9, (int)r.Y + 45), 11, 1f, Raylib.Fade(Pal.Foe, txtA));
                else
                {
                    var (tag, custom) = DisplayTag(u);
                    if (tag.Length > 0)
                        Raylib.DrawTextEx(Cfg.Font, tag, new Vector2((int)r.X + 9, (int)r.Y + 45), 11, 1f, Raylib.Fade(custom ? Pal.Friend : Pal.Accent, txtA));
                }
            }

            RosterChips.Add((rest, u));   // hit-test the resting position, not the mid-slide rect
        }
    }

    // ---------------- top bar ----------------
    // W10 (owner feedback — "looks terrible"): the bar is three DELIBERATE zones sharing one
    // vertical center (cy 26): LEFT turn/phase + concealment, CENTER mission + objective (+
    // secondary bonus below), RIGHT squad/hostile counts + labeled pressure meter + heat + mute
    // + END TURN. The right zone lays out right-to-left from the screen edge and the center
    // group clamps into the remaining span, so the zones can never collide. Subtle hairline
    // separators mark the zone boundaries. Every pre-W10 readout survives — just regrouped.
    static void DrawTopBar(Game g)
    {
        Raylib.DrawRectangleGradientV(0, 0, Cfg.ScreenW, 64, Pal.RGBA(8, 12, 17, 235), Pal.RGBA(8, 12, 17, 0));
        const int cy = 26;   // shared vertical center for the whole bar

        // ---- LEFT ZONE: turn/phase + concealment ------------------------------------------
        bool playerTurn = g.Phase != Phase.EnemyTurn;
        string turnTxt = playerTurn ? "PLAYER TURN" : "ENEMY TURN";
        Color turnCol = playerTurn ? Pal.Friend : Pal.Foe;
        float lx = 16;
        float pillW = Raylib.MeasureTextEx(Cfg.Font, turnTxt, 16, 1f).X + 30;
        var pill = new Rectangle(lx, cy - 15, pillW, 30);
        Raylib.DrawRectangleRounded(pill, 0.4f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(pill, 1.5f, Raylib.Fade(turnCol, 0.6f));
        CenterText(turnTxt, pill, 16, turnCol);
        lx += pillW + 10;

        // 4.4 concealment pill: while the squad is hidden, a pulsing CONCEALED pill rides next
        // to the turn pill (same stealth-state family); it vanishes the moment stealth breaks.
        bool showConcealed = g.SquadConcealed && (g.Phase == Phase.PlayerTurn || g.Phase == Phase.EnemyTurn);
        if (showConcealed)
        {
            float pulse = 0.55f + 0.45f * MathF.Sin((float)Raylib.GetTime() * 3.5f);
            float cw = Raylib.MeasureTextEx(Cfg.Font, "CONCEALED", 14, 1f).X + 26;
            var cpill = new Rectangle(lx, cy - 15, cw, 30);
            Raylib.DrawRectangleRounded(cpill, 0.4f, 8, Pal.Panel);
            Raylib.DrawRectangleLinesEx(cpill, 1.5f, Raylib.Fade(Pal.Friend, 0.5f * pulse));
            CenterText("CONCEALED", cpill, 14, Raylib.Fade(Pal.Friend, pulse));
            lx += cw + 10;
        }
        float leftEnd = lx;

        // ---- RIGHT ZONE (laid out right-to-left): END TURN, mute, heat, pressure, counts ----
        float rx = Cfg.ScreenW - 20;
        // end turn (turns into a confirm prompt if soldiers still have actions)
        EndTurnRect = new Rectangle(Cfg.ScreenW - 170, cy - 15, 150, 30);
        bool canEnd = g.IsPlayerInteractive();
        if (g.EndTurnArmed)
            DrawButtonRect(EndTurnRect, "CONFIRM?", "ENT", canEnd, true, Pal.Accent);
        else
            DrawButtonRect(EndTurnRect, "END TURN", "ENT", canEnd, false, Pal.Accent);
        rx = EndTurnRect.X - 16;

        // mute indicator (small, dim — a persistent state, not a signal)
        if (!Audio.Enabled)
        {
            float mw = Raylib.MeasureTextEx(Cfg.Font, "MUTED (M)", 12, 1f).X;
            Raylib.DrawTextEx(Cfg.Font, "MUTED (M)", new Vector2(rx - mw, cy - 6), 12, 1f, Pal.TxtDim);
            rx -= mw + 16;
        }

        // Heat/Ascension indicator (only at heat > 0, so heat 0 stays byte-identical)
        if (g.HeatLevel > 0)
        {
            string ht = $"HEAT {g.HeatLevel}";
            float hw = Raylib.MeasureTextEx(Cfg.Font, ht, 14, 1f).X + 22;
            var hp = new Rectangle(rx - hw, cy - 13, hw, 26);
            Raylib.DrawRectangleRounded(hp, 0.4f, 8, Pal.Panel);
            Raylib.DrawRectangleLinesEx(hp, 1.5f, Raylib.Fade(Pal.Foe, 0.6f));
            CenterText(ht, hp, 14, Pal.Foe);
            rx -= hw + 16;
        }

        // anti-turtle PRESSURE meter (camp-friendly campaign objectives only): a LABELED stack —
        // the word above, the rung pips below — so the pips are never a mystery row of boxes.
        if (g.Mode != GameMode.Endless && g.Mode != GameMode.Skirmish && g.PressureClockHud)
        {
            float pw = PressureMeterWidth(g);
            DrawPressureMeter(g, rx - pw, cy);
            rx -= pw + 18;
        }

        // squad / hostile counts (the VIP isn't a combatant, so it's excluded from the tally).
        // Wave 4: a faction mission names its enemy by FACTION (a persistent reminder of who
        // you're fighting + which positional rule is in effect); otherwise the generic HOSTILES.
        int friends = g.AlivePlayers().Count(p => !p.IsVip);
        int foes = g.AliveEnemies().Count;
        string foeLabel = Combat.MissionFaction != Faction.None ? Run.FactionName(Combat.MissionFaction) : "HOSTILES";
        rx -= DrawCounterR(rx, cy, Pal.Foe, $"{foes}  {foeLabel}") + 18;
        rx -= DrawCounterR(rx, cy, Pal.Friend, $"{friends}  SQUAD") + 18;
        float rightStart = rx + 2;

        // subtle zone separators (hairlines, not chrome)
        Raylib.DrawRectangle((int)leftEnd + 2, cy - 12, 1, 24, Raylib.Fade(Pal.PanelBd, 0.9f));
        Raylib.DrawRectangle((int)rightStart - 2, cy - 12, 1, 24, Raylib.Fade(Pal.PanelBd, 0.9f));

        // ---- CENTER ZONE: mission + objective (+ secondary bonus on a second line) ----------
        // Accent discipline (60-30-10): the objective readout uses ONE primary accent (amber, the
        // "objective" role) so it doesn't compete with the genuine state colors (green/red) used
        // for the secondary-bonus tracker + the pressure meter. The exception is a gold ASSET/HVT
        // objective (escort/rescue/decapitate), where gold is the semantic role for the thing you
        // protect or hunt — and the objective glyph carries the type by shape regardless of hue.
        string preTxt; Color preCol; string objTxt; Color objCol; bool glyph = false;
        if (g.Mode == GameMode.Endless)
        {
            // PROGRAM HORIZON W2: LAST STAND replaces the objective readout with the WAVE/BEST counter.
            preTxt = "LAST STAND"; preCol = Raylib.Fade(Pal.Foe, 0.85f);
            objTxt = g.EndlessHud; objCol = Pal.Foe;
        }
        else if (g.Mode == GameMode.Skirmish)
        {
            // PROGRAM HORIZON W4: SKIRMISH/DAILY show "SKIRMISH — <OBJ>" or "DAILY <stamp>  BEST n".
            preTxt = ""; preCol = Pal.TxtDim;
            objTxt = g.SkirmishHud; objCol = g.DailyMode ? Pal.Accent : Pal.Friend;
        }
        else
        {
            preTxt = $"MISSION {g.RunState.Mission}/{Run.MaxMissions}"; preCol = Pal.TxtDim;
            glyph = true;
            switch (g.Objective)
            {
                case Objective.Evac: objTxt = "EXTRACT"; objCol = Pal.Accent; break;
                case Objective.Hack: objTxt = $"HACK {g.HackProgress}/{Game.HackRequired}"; objCol = Pal.Accent; break;
                case Objective.Sabotage: objTxt = $"SABOTAGE {g.SabotageBlown.Count}/{g.SabotageSites.Count}"; objCol = Pal.Accent; break;
                case Objective.Escort: objTxt = "ESCORT VIP"; objCol = Pal.VipGold; break;
                case Objective.Rescue: objTxt = g.CaptiveLocked ? "RESCUE CAPTIVE" : "EXTRACT CAPTIVE"; objCol = Pal.VipGold; break;
                case Objective.Defend: objTxt = $"DEFEND {Math.Min(g.Turn, Game.DefendTurns)}/{Game.DefendTurns}"; objCol = Pal.Accent; break;
                case Objective.Decapitate:
                    // W4 GUARDED HVT: read the guarded state at a glance — danger-red "HVT GUARDED"
                    // while a bodyguard shields it (peel the guards first), gold "HVT EXPOSED" once
                    // it's open to a kill. Falls back to plain "KILL HVT" if the HVT is somehow null.
                    if (g.HasHvt && g.Hvt.HvtGuarded) { objTxt = "HVT GUARDED"; objCol = Pal.Foe; }
                    else if (g.HasHvt)                { objTxt = "HVT EXPOSED"; objCol = Pal.VipGold; }
                    else                              { objTxt = "KILL HVT"; objCol = Pal.VipGold; }
                    break;
                default: objTxt = "ELIMINATE"; objCol = Pal.Accent; break;
            }
        }
        // measure the group: [prefix]  [glyph] OBJECTIVE — centered on the screen, clamped into
        // the span the left/right zones leave free so it can never collide with either.
        float preW = string.IsNullOrEmpty(preTxt) ? 0 : Raylib.MeasureTextEx(Cfg.Font, preTxt, 14, 1f).X + 14;
        float glyphW = glyph ? 20 : 0;
        float objW = Raylib.MeasureTextEx(Cfg.Font, objTxt, 16, 1f).X;
        float total = preW + glyphW + objW;
        float cx = Cfg.ScreenW / 2f - total / 2f;
        cx = Util.Clamp(cx, leftEnd + 14, rightStart - total - 14);
        if (!string.IsNullOrEmpty(preTxt))
            Raylib.DrawTextEx(Cfg.Font, preTxt, new Vector2((int)cx, cy - 7), 14, 1f, preCol);
        // 5.4/5.5: a semantic glyph left of the objective text (shape redundancy, not hue alone)
        if (glyph) DrawObjectiveIcon(g.Objective, cx + preW + 8, cy, objCol);
        Raylib.DrawTextEx(Cfg.Font, objTxt, new Vector2((int)(cx + preW + glyphW), cy - 8), 16, 1f, objCol);
        // W11: publish the objective group's rect so hovering the readout explains the goal
        // (DrawHudHovers → Codex.ObjectiveDesc — the codex line, one source of truth).
        _objectiveRect = new Rectangle(cx - 4, cy - 15, total + 8, 30);

        // optional secondary objective (3.9): green while on track, red once blown — a smaller
        // second line centered under the objective group so mission + bonus read as one block.
        // W10: the INTEL CACHE clock rides the SAME line, right of the bonus (gold — it matches the
        // board diamond), so the whole "extra value on this map" story reads in one glance.
        {
            string sec = g.Secondary != SecondaryKind.None ? g.SecondaryHud : "";
            string cache = g.CachePresent ? $"CACHE {g.CacheTurnsLeft}T" : "";
            float sw = string.IsNullOrEmpty(sec) ? 0 : Raylib.MeasureTextEx(Cfg.Font, sec, 12, 1f).X;
            float cw = string.IsNullOrEmpty(cache) ? 0 : Raylib.MeasureTextEx(Cfg.Font, cache, 12, 1f).X;
            float gap = (sw > 0 && cw > 0) ? 14f : 0f;
            float total2 = sw + gap + cw;
            if (total2 > 0)
            {
                float sx = Util.Clamp(cx + preW + (glyphW + objW) / 2f - total2 / 2f, leftEnd + 14, rightStart - total2 - 14);
                if (sw > 0)
                    Raylib.DrawTextEx(Cfg.Font, sec, new Vector2((int)sx, 44), 12, 1f, g.SecondaryOnTrack ? Pal.Good : Pal.Foe);
                if (cw > 0)
                    Raylib.DrawTextEx(Cfg.Font, cache, new Vector2((int)(sx + sw + gap), 44), 12, 1f,
                                      g.CacheTurnsLeft <= 2 ? Pal.Foe : Pal.VipGold);
            }
        }
    }

    /// Right-zone counter: a team dot + count/label at 14px, vertically centered on `cy`,
    /// RIGHT-aligned so callers can flow the top bar right-to-left. Returns the drawn width.
    static float DrawCounterR(float rightX, int cy, Color dot, string text)
    {
        float tw = Raylib.MeasureTextEx(Cfg.Font, text, 14, 1f).X;
        float w = 16 + tw;
        Raylib.DrawCircle((int)(rightX - w + 5), cy, 5.5f, dot);
        Raylib.DrawTextEx(Cfg.Font, text, new Vector2((int)(rightX - tw), cy - 7), 14, 1f, Pal.Txt);
        return w;
    }

    /// Width of the labeled pressure stack (max of the word + the pip row) for right-to-left layout.
    static float PressureMeterWidth(Game g)
    {
        string lbl = g.Pressure > 0 ? "ALERT" : "PRESSURE";
        float lw = Raylib.MeasureTextEx(Cfg.Font, lbl, 10, 1f).X;
        float pips = Game.PressureMax * 12 - 3;   // pipW 9 + gap 3
        return MathF.Max(lw, pips);
    }

    // Anti-turtle PRESSURE meter: a labeled stack — the word ("PRESSURE" graced / "ALERT" once
    // the clock escalates) above N rung pips that fill hollow -> solid red. The active rung
    // pulses, and at max the frames flash, so the rising threat reads at a glance. Tinted red
    // only when live (a genuine threat state). Shape-redundant (filled vs hollow), so it works
    // in the colorblind palette too.
    static void DrawPressureMeter(Game g, float x, int cy)
    {
        int rung = g.Pressure, max = Game.PressureMax;
        bool maxed = rung >= max;
        bool live = rung > 0;
        string lbl = live ? "ALERT" : "PRESSURE";
        float w = PressureMeterWidth(g);
        float lw = Raylib.MeasureTextEx(Cfg.Font, lbl, 10, 1f).X;
        Raylib.DrawTextEx(Cfg.Font, lbl, new Vector2((int)(x + w / 2 - lw / 2), cy - 14), 10, 1f, live ? Pal.Foe : Pal.TxtDim);
        const int pipW = 9, pipH = 12, gap = 3;
        float px = x + w / 2 - (max * (pipW + gap) - gap) / 2f;
        float pulse = 0.6f + 0.4f * (float)Math.Sin(Raylib.GetTime() * 5.0);
        for (int i = 0; i < max; i++)
        {
            var r = new Rectangle(px + i * (pipW + gap), cy + 1, pipW, pipH);
            bool filled = i < rung;
            if (filled)
            {
                // solid red fill; the leading (newest) rung pulses + gets a bright top edge so the
                // "current level" reads at a glance.
                bool lead = i == rung - 1;
                Raylib.DrawRectangleRec(r, lead ? Raylib.Fade(Pal.Foe, pulse) : Pal.Foe);
                if (lead) Raylib.DrawRectangle((int)r.X, (int)r.Y, (int)r.Width, 2, Pal.RGBA(255, 210, 200));
            }
            else
            {
                // empty rungs get a faint filled body so the hollow vs solid contrast is obvious
                // (and shape-redundant for colorblind mode), then an outline.
                Raylib.DrawRectangleRec(r, Pal.RGBA(26, 22, 24));
            }
            Raylib.DrawRectangleLinesEx(r, 1f, Raylib.Fade(maxed ? Pal.Foe : Pal.TxtDim, maxed ? pulse : 0.6f));
        }
    }

    // ---------------- active-boons strip ----------------
    // Small gold chips of each run-scoped boon's short code, right-anchored just under the
    // top bar so the player always sees which run modifiers are live. Draws nothing when the
    // run has no boons (so heat-0 / boon-less runs stay byte-identical). Render-only.
    static void DrawBoonStrip(Game g)
    {
        _boonChips.Clear();   // W11: republished every frame for the hover tooltip (DrawHudHovers)
        var boons = g.RunState?.ActiveBoons;
        if (boons == null || boons.Count == 0) return;

        const float size = 12f, padX = 7f, h = 18f, gap = 5f, y = 46f;
        // measure right-to-left so the strip hugs the screen's right edge (clear of the
        // left roster strip, the heat pill and the end-turn button above it)
        float x = Cfg.ScreenW - 20f;
        for (int i = boons.Count - 1; i >= 0; i--)
        {
            string code = BoonDef.Code(boons[i]);
            float tw = Raylib.MeasureTextEx(Cfg.Font, code, size, 1f).X;
            float w = tw + padX * 2f;
            x -= w;
            var chip = new Rectangle(x, y, w, h);
            Raylib.DrawRectangleRounded(chip, 0.4f, 6, Raylib.Fade(Pal.Panel, 0.85f));
            Raylib.DrawRectangleLinesEx(chip, 1f, Raylib.Fade(Pal.VipGold, 0.55f));
            Raylib.DrawTextEx(Cfg.Font, code, new Vector2(x + padX, y + 3f), size, 1f, Pal.VipGold);
            _boonChips.Add((boons[i], chip));
            x -= gap;
        }
    }

    // W11: hover-tooltip anchors for the passive readouts (objective line + boon chips).
    static Rectangle _objectiveRect;
    static readonly System.Collections.Generic.List<(Boon boon, Rectangle rect)> _boonChips = new();

    /// W11: lightweight hover tooltips for the passive readouts — the objective line ("what am I
    /// actually doing?") and the gold boon codes ("what does STK mean?"). Live phases only; the
    /// LAST STAND wave counter is self-describing, so Endless skips the objective card.
    static void DrawHudHovers(Game g)
    {
        if (g.Phase != Phase.PlayerTurn && g.Phase != Phase.EnemyTurn) return;
        var m = Raylib.GetMousePosition();

        if (g.Mode != GameMode.Endless
            && (Raylib.CheckCollisionPointRec(m, _objectiveRect) || (g.NoPersist && _forcedHover == "obj")))
        {
            DrawHoverCard(Codex.ObjectiveName(g.Objective), Codex.ObjectiveDesc(g.Objective),
                          _objectiveRect.X, _objectiveRect.Y + _objectiveRect.Height + 6, Pal.Accent);
            return;
        }
        foreach (var (boon, chip) in _boonChips)
        {
            if (!Raylib.CheckCollisionPointRec(m, chip) && !(g.NoPersist && _forcedHover == "boon")) continue;
            DrawHoverCard(BoonDef.Name(boon), BoonDef.Desc(boon),
                          chip.X, chip.Y + chip.Height + 6, Pal.VipGold);
            return;
        }
    }

    // W11 harness seam for DrawHudHovers (screenshot only): SIGHTLINE_HOVERHUD=obj|boon frames the
    // objective / first-boon-chip hover card headless. Read once; null in every normal run, and
    // the use sites gate on g.NoPersist so a stray env var can never force a card in live play.
    static readonly string _forcedHover = Environment.GetEnvironmentVariable("SIGHTLINE_HOVERHUD");

    /// A small anchored hover card: accent title + wrapped body (~300px column), clamped on-screen.
    static void DrawHoverCard(string title, string body, float ax, float ay, Color accent)
    {
        var lines = WrapText(body ?? "", 12, 300);
        int w = (int)Raylib.MeasureTextEx(Cfg.Font, title, 14, 1f).X;
        foreach (var ln in lines) w = Math.Max(w, (int)Raylib.MeasureTextEx(Cfg.Font, ln, 12, 1f).X);
        w += 20;
        int h = 28 + lines.Count * 16 + 8;
        int x = Util.Clamp((int)ax, 8, Cfg.ScreenW - w - 8);
        int y = Util.Clamp((int)ay, 8, Cfg.ScreenH - h - 8);
        var box = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(box, 0.14f, 6, Pal.RGBA(10, 14, 19, 252));
        Raylib.DrawRectangleLinesEx(box, 1.2f, Raylib.Fade(accent, 0.8f));
        Raylib.DrawTextEx(Cfg.Font, title, new Vector2(x + 10, y + 8), 14, 1f, accent);
        int ty = y + 28;
        foreach (var ln in lines) { Raylib.DrawTextEx(Cfg.Font, ln, new Vector2(x + 10, ty), 12, 1f, Pal.Txt); ty += 16; }
    }

    // ---------------- in-mission combat log ----------------
    // A compact, always-visible ledger of the last few consequential events (Stats.CombatLog,
    // always-on backend) anchored lower-right above the action bar. Team-colored + outcome-tinted,
    // very low-alpha background so it never fights the board (readability is sacred: it sits over
    // the lower-right board corner but never occludes units/threat pips meaningfully). Render-only.
    static void DrawCombatLog(Game g)
    {
        var log = Stats.CombatLog;
        // Never draw an empty labeled void: with no events there's nothing to ledger, so the
        // panel is simply hidden until the first shot lands. The chrome height also tracks the
        // actual entry count (capped) so early in a mission it's a small 1-2 line strip, not a
        // tall dark box with one line at the top.
        if (log.Count == 0) return;
        const int cap = 6;
        const float w = 296f, lh = 14f, padX = 9f, headH = 18f, padY = 6f;
        int shown = Math.Min(cap, log.Count);
        float bodyH = shown * lh;
        float h = headH + bodyH + padY;
        // bottom edge sits just above the action bar's TOP row (the W10 bar can wrap to extra
        // rows that grow upward; _barTop tracks it) and clear of the unit card (x 20..270)
        float x = Cfg.ScreenW - w - 14f;
        float y = MathF.Min(712f, _barTop - 8f) - h;
        var panel = new Rectangle(x, y, w, h);

        // low-alpha frame so the board reads through it
        Raylib.DrawRectangleRounded(panel, 0.10f, 6, Raylib.Fade(Pal.RGBA(8, 12, 17), 0.62f));
        Raylib.DrawRectangleLinesEx(panel, 1f, Raylib.Fade(Pal.PanelBd, 0.6f));
        // tiny header
        Raylib.DrawTextEx(Cfg.Font, "LOG", new Vector2(x + padX, y + 4f), 11, 1f, Pal.TxtDim);

        float ty = y + headH;
        int start = Math.Max(0, log.Count - shown);
        for (int i = start; i < log.Count; i++)
        {
            var e = log[i];
            // base color by team (Player=0 -> Friend, else Foe), then tint by outcome
            Color c = e.Team == 0 ? Pal.Friend : Pal.Foe;
            switch (e.Outcome)
            {
                case "KILL":  c = Pal.VipGold; break;            // bright: a death
                case "CRIT":  c = Pal.Accent;  break;            // crit pop
                case "GRAZE":
                case "MISS":  c = Pal.TxtDim;  break;            // dim: low-consequence
            }
            string line = e.Text ?? "";
            // clip to the panel width so long lines never spill
            line = Clip(line, 11, (int)(w - padX * 2f));
            Raylib.DrawTextEx(Cfg.Font, line, new Vector2(x + padX, ty), 11, 1f, c);
            ty += lh;
        }
    }

    /// Truncate `text` (with an ellipsis) so it fits within `maxW` px at `size`.
    static string Clip(string text, int size, int maxW)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (Raylib.MeasureTextEx(Cfg.Font, text, size, 1f).X <= maxW) return text;
        while (text.Length > 1 && Raylib.MeasureTextEx(Cfg.Font, text + "…", size, 1f).X > maxW)
            text = text.Substring(0, text.Length - 1);
        return text + "…";
    }

    // ---------------- bottom bar ----------------
    static void DrawBottomBar(Game g)
    {
        int barY = Cfg.ScreenH - 106; // floating panel anchored to the screen bottom (decoupled from the board)
        // a taller scrim so the board reading under the floating bar stays legible
        Raylib.DrawRectangleGradientV(0, Cfg.ScreenH - 150, Cfg.ScreenW, 150,
                                      Pal.RGBA(8, 12, 17, 0), Pal.RGBA(8, 12, 17, 238));

        var u = g.Selected;
        if (u != null && u.Team == Team.Player)
            DrawUnitCard(u, 20, barY);

        DrawActionButtons(g, barY);

        // hint — pinned to the strip BELOW the bar's bottom row (the bar grows upward, so this
        // baseline never collides with buttons at any row count)
        string hint = "MOVE / FIRE by click  -  [Tab] next  -  [5] ability  -  [T] tag  -  [Esc] menu";
        int hw = (int)Raylib.MeasureTextEx(Cfg.Font, hint, 13, 1f).X;
        Raylib.DrawTextEx(Cfg.Font, hint, new Vector2(Cfg.ScreenW - hw - 24, Cfg.ScreenH - 26), 13, 1f, Pal.TxtDim);
    }

    static void DrawUnitCard(Unit u, int x, int y)
    {
        // Pop the card in (slide up) when the selection changes — keyed per unit so
        // switching soldiers re-triggers the entrance (old key prunes when it stops drawing).
        float in_ = PanelAnim("unitcard:" + u.Name, 0.16f);
        y += (int)((1f - Util.EaseOutBack(in_)) * 14f);

        var card = new Rectangle(x, y, 250, 92);
        PanelShadow(card, 1f, 0.12f);
        Raylib.DrawRectangleRounded(card, 0.12f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1f, Pal.PanelBd);
        Raylib.DrawRectangle((int)card.X, (int)card.Y, 3, (int)card.Height, Pal.Friend);

        Raylib.DrawTextEx(Cfg.Font, u.Name, new Vector2(x + 14, y + 10), 20, 1f, Pal.Txt);
        int nw = (int)Raylib.MeasureTextEx(Cfg.Font, u.Name, 20, 1f).X;
        Raylib.DrawTextEx(Cfg.Font, u.Cls, new Vector2(x + 20 + nw, y + 15), 12, 1f, u.IsVip ? Pal.VipGold : Pal.Friend);
        string rank = u.IsVip ? "ASSET" : u.RankName;
        Raylib.DrawTextEx(Cfg.Font, rank, new Vector2(x + 250 - (int)Raylib.MeasureTextEx(Cfg.Font, rank, 11, 1f).X - 14, y + 13), 11, 1f, u.IsVip ? Pal.VipGold : Pal.Accent);

        // hp bar
        var bar = new Rectangle(x + 14, y + 38, 222, 13);
        Raylib.DrawRectangleRounded(bar, 0.5f, 6, Pal.RGBA(10, 15, 21));
        float frac = u.MaxHp > 0 ? u.Hp / (float)u.MaxHp : 0;
        if (frac > 0)
        {
            var fill = new Rectangle(bar.X, bar.Y, bar.Width * frac, bar.Height);
            Color hc = frac > 0.5f ? Pal.Good : (frac > 0.25f ? Pal.Accent : Pal.Foe);
            Raylib.DrawRectangleRounded(fill, 0.5f, 6, hc);
        }
        CenterText($"{u.Hp}/{u.MaxHp}", bar, 11, Pal.RGBA(8, 14, 10));

        // action pips (a flank-kill refund tops a soldier back up to — never above — its
        // 2-action budget, so two pips still cover every state)
        for (int i = 0; i < 2; i++)
        {
            var pip = new Rectangle(x + 14 + i * 26, y + 60, 22, 7);
            bool on = i < u.ActionsLeft;
            Raylib.DrawRectangleRounded(pip, 0.5f, 4, on ? Pal.Accent : Pal.RGBA(28, 39, 51));
        }
        // grenade count
        string gren = $"GREN x{u.Grenades}";
        Raylib.DrawTextEx(Cfg.Font, gren, new Vector2(x + 72, y + 60), 11, 1f, u.Grenades > 0 ? Pal.Accent : Pal.TxtDim);
        // ammo
        string ammo = $"AMMO {u.Ammo}/{u.Weapon.Clip}";
        Raylib.DrawTextEx(Cfg.Font, ammo, new Vector2(x + 250 - (int)Raylib.MeasureTextEx(Cfg.Font, ammo, 12, 1f).X - 14, y + 60), 12, 1f, u.Ammo == 0 ? Pal.Foe : Pal.TxtDim);
    }

    static void DrawActionButtons(Game g, int y)
    {
        var u = g.Selected;
        bool interactive = g.IsPlayerInteractive() && u != null && u.Team == Team.Player;
        bool hasTargets = interactive && g.HasAnyTarget(u);

        // Collect the button specs first, then size them to fit the bar (the count varies:
        // base 6, +ability/+item per class, +hack on hack/sabotage objectives, +snap = up to 9).
        var specs = new System.Collections.Generic.List<(string id, string label, string key, bool enabled, bool sel)>();
        void Add(string id, string label, string key, bool enabled, bool sel)
            => specs.Add((id, label, key, enabled, sel));

        // TEMPO: FIRE is 1 action and does NOT end the turn. The soldier keeps its second action to
        // reposition, take a rushed FOLLOW-UP shot (at -aim), or a support act. SNAP is retired (the
        // default full-aim non-ending shot replaces it; the 2nd shot/turn carries the penalty).
        Add("shoot", "FIRE", "1", interactive && u != null && u.CanAct && u.Ammo > 0 && hasTargets, g.AimMode);
        Add("grenade", "GRENADE", "4", interactive && u != null && u.CanAct && u.Grenades > 0, g.GrenadeMode);
        if (u != null && u.Ability != AbilityKind.None)
        {
            string abLabel = u.AbilityCd > 0 ? $"{u.AbilityName} ({u.AbilityCd})" : u.AbilityName;   // append remaining cooldown
            Add("ability", abLabel, "5", interactive && g.CanAbility(u), u.RunGun || u.Blitz || u.Steady || u.Slipstreaming || g.MarkMode || g.GrappleMode || g.PinMode);
        }
        if (u != null && u.Item != ItemKind.None)
            Add("item", u.ItemName, "6", interactive && u.CanAct && u.ItemCharge > 0, g.ItemMode);
        // SHOVE: forced-movement verb (1 action, no end-turn, 1/turn). Enabled only when an
        // enemy is adjacent (CanShove), so it surfaces exactly when it's usable.
        Add("shove", "SHOVE", "8", interactive && g.CanShove(u), g.ShoveMode);
        // FIELD CRAFT (W1): two universal positioning verbs. DRAG pulls an adjacent ally toward you;
        // VAULT leaps an adjacent cover tile. Both surface only when usable (CanDrag/CanVault).
        Add("drag", "DRAG", "7", interactive && g.CanDrag(u), g.DragMode);
        Add("vault", "VAULT", "9", interactive && g.CanVault(u), g.VaultMode);
        Add("overwatch", "OVERWATCH", "2", interactive && u != null && u.CanAct && u.Ammo > 0, false);
        Add("focusow", "FOCUS", "F", interactive && u != null && u.CanAct && u.Ammo > 0, false);   // braced cone watch
        Add("brace", "BRACE", "B", interactive && u != null && u.CanAct && u.Ammo > 0, false);      // UNDERTOW W2: disrupting interrupt watch
        Add("hunker", "HUNKER", "3", interactive && u != null && u.CanAct, u != null && u.Hunkered);
        if (g.HasHackAction)
            Add("hack", g.HasSabotage ? "PLANT" : "HACK", "H", interactive && g.CanHack(u), false);
        if (g.HasBeaconAction && !g.BeaconPlanted)
            Add("beacon", "BEACON", "G", interactive && g.CanBeacon(u), false);
        if (g.HasExtractAction)
            Add("extract", "EXTRACT", "X", interactive && g.CanExtract(u), false);
        Add("reload", "RELOAD", "R", interactive && u != null && u.CanAct && u.Ammo < u.Weapon.Clip, false);

        // W10 (owner feedback): every button sizes to its RENDERED content (icon zone + measured
        // label + hotkey tag), and the row WRAPS into extra rows that grow UPWARD when the sum
        // overflows the bar span — an ellipsized verb is impossible by construction at any count.
        const float bh = 40, gapX = 6, gapY = 6;
        const int iconZone = ActionIconZone, rightPad = 10;
        float bx0 = 300;
        float right = Cfg.ScreenW - 20;                      // small right margin

        var widths = new float[specs.Count];
        for (int i = 0; i < specs.Count; i++)
        {
            float lw = Raylib.MeasureTextEx(Cfg.Font, specs[i].label, ActionLabelFs, 1f).X;
            float kw = string.IsNullOrEmpty(specs[i].key)
                ? 0
                : Raylib.MeasureTextEx(Cfg.Font, specs[i].key, 12, 1f).X + 6 + 8;   // tag box + gap
            widths[i] = iconZone + lw + kw + rightPad;
        }

        // Greedy wrap into rows. The FIRST row (fire/strike verbs) stays anchored at the classic
        // bar y — so FIRE/GRENADE/OVERWATCH never jump vertically as the verb count changes
        // between units — and each overflow row becomes a tray stacked ABOVE it (bar grows upward).
        var rowOf = new int[specs.Count];
        int rows = 0;
        {
            float bx = bx0;
            for (int i = 0; i < specs.Count; i++)
            {
                if (bx > bx0 && bx + widths[i] > right) { rows++; bx = bx0; }
                rowOf[i] = rows;
                bx += widths[i] + gapX;
            }
            rows++;
        }

        float yBase = y + 26;                                // bottom row (same y the bar always had)
        _barTop = yBase - (rows - 1) * (bh + gapY);          // combat log anchors above this

        var btns = new System.Collections.Generic.List<UiButton>();
        {
            float bx = bx0; int row = 0;
            for (int i = 0; i < specs.Count; i++)
            {
                if (rowOf[i] != row) { row = rowOf[i]; bx = bx0; }
                float by = yBase - row * (bh + gapY);
                btns.Add(new UiButton
                {
                    Rect = new Rectangle(bx, by, widths[i], bh),
                    Id = specs[i].id, Label = specs[i].label, Key = specs[i].key,
                    Enabled = specs[i].enabled, Selected = specs[i].sel,
                    Accent = Pal.Friend,
                });
                bx += widths[i] + gapX;
            }
        }

        ActionButtons = btns.ToArray();
        // W11 de-occlusion, FUL-3-corrected: when a living unit stands under an actual BUTTON
        // (incl. the unit an active anim is walking/shooting through that strip), THAT button —
        // not the whole bar — fades so the fight stays visible through it. One scalar used to
        // ghost every verb to 0.3 because a single dormant pod idled under one corner; now the
        // dim is truly per-button, the floor is 0.45 (labels stay legible), and dormant pods
        // don't count as cover-worthy. Mousing over the bar restores it instantly — it never
        // stops being interactive; it just yields visually while the board needs the pixels.
        var barRect = new Rectangle(bx0, _barTop, right - bx0, (yBase + bh) - _barTop);
        bool mouseOnBar = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), barRect);
        foreach (var b in ActionButtons)
        {
            float dim = !mouseOnBar && ChipOccluded(g, b.Rect, true) ? 0.45f : 1f;
            DrawActionButton(b, dim);
        }

        DrawActionHelp(g);
    }

    /// Action-bar metrics shared by the sizer and the renderer: the icon gutter width and the
    /// one fixed label size (buttons are sized to fit it, so it never shrinks or ellipsizes).
    const int ActionIconZone = 22;
    const int ActionLabelFs = 15;
    /// Top edge of the (possibly multi-row) action bar this frame — the combat log and any
    /// panel that must stay clear of the bar anchors above it. Defaults to the one-row top.
    static float _barTop = 720f;

    /// Draw one action button: background + border via DrawButtonRect, then overlay a
    /// small procedural icon in the left quarter of the button (14px zone) that uses the
    /// same text color so enabled/disabled/selected states and colorblind mode all work.
    static void DrawActionButton(UiButton b, float dim = 1f)
    {
        // First draw the standard background + border.  We still use DrawButtonRect for
        // the chrome; the label text is re-drawn below shifted right by the icon width.
        // W11: `dim` (0.3 when a unit stands under the bar) scales every layer's alpha.
        var r = b.Rect;
        bool enabled = b.Enabled;
        bool selected = b.Selected;
        string label = b.Label;
        string key   = b.Key;
        Color accent = b.Accent;

        bool hover = enabled && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Color bg = selected ? Pal.RGBA(40, 34, 12) : (hover ? Pal.RGBA(22, 32, 44) : Pal.Panel);
        Raylib.DrawRectangleRounded(r, 0.22f, 6, Raylib.Fade(bg, (enabled ? 1f : 0.4f) * dim));
        Color bd = selected ? Pal.Accent : (hover ? accent : Pal.PanelBd);
        Raylib.DrawRectangleLinesEx(r, 1.5f, Raylib.Fade(bd, (enabled ? 1f : 0.35f) * dim));

        // Text color (same as DrawButtonRect).
        Color tc = selected ? Pal.Accent : (enabled ? Pal.Txt : Pal.TxtDim);
        float a = (enabled ? 1f : 0.5f) * dim;
        Color ic = Raylib.Fade(tc, a);   // icon color — tracks text so CB / disabled states work

        // Icon zone: left 22px of the button interior, vertically centred. The glyph itself sits
        // at ~x+9 (it's ~12px wide, so its right edge is ~x+15); reserving 22px leaves a clear
        // ~7px gutter before any label text so the glyph never kisses its word.
        const int iconZone = ActionIconZone;
        float ix = r.X + 9f;
        float iy = r.Y + r.Height / 2f;

        DrawActionIcon(b.Id, ix, iy, ic);

        // Label + key: the button was SIZED to this content (DrawActionButtons W10), so the full
        // word always fits — no shrink loop, no ellipsis, one consistent label size bar-wide.
        int fs = ActionLabelFs;
        int lw = (int)Raylib.MeasureTextEx(Cfg.Font, label, fs, 1f).X;
        int startX = (int)r.X + iconZone;
        int ty     = (int)(r.Y + r.Height / 2 - fs / 2);
        Raylib.DrawTextEx(Cfg.Font, label, new Vector2(startX, ty), fs, 1f, Raylib.Fade(tc, a));
        if (!string.IsNullOrEmpty(key))
        {
            int keyX = startX + lw + 8;
            var kr = new Rectangle(keyX, r.Y + r.Height / 2 - 8, (int)Raylib.MeasureTextEx(Cfg.Font, key, 12, 1f).X + 6, 16);
            Raylib.DrawRectangleLinesEx(kr, 1f, Raylib.Fade(tc, 0.4f * dim));
            Raylib.DrawTextEx(Cfg.Font, key, new Vector2(keyX + 3, (int)(r.Y + r.Height / 2 - 6)), 12, 1f, Raylib.Fade(tc, 0.7f * dim));
        }
    }

    /// Draw a small (~12-14px) procedural icon centred at (cx, cy) in color ic.
    /// Each icon is made of 2D primitives (lines, polys, circles, rects) — no textures.
    /// Designs:
    ///   shoot    — a right-pointing chevron (bullet tip) with a short muzzle line
    ///   grenade  — a small circle (body) + a short stub fuse at the top
    ///   overwatch — two concentric arcs forming an eye/sector
    ///   hunker   — a downward chevron inside a thin shield arc
    ///   reload   — a three-quarter arc with an arrowhead tail
    ///   hack     — two interlocked squares (circuitry)
    ///   ability  — a four-point star (spark)
    ///   item     — a small canister (rect + cap line)
    static void DrawActionIcon(string id, float cx, float cy, Color c)
    {
        switch (id)
        {
            case "shoot":
            {
                // Right-pointing chevron (arrowhead): two lines meeting at a tip
                float tip = cx + 7f, mid = cy;
                float backY = 6f;
                Raylib.DrawLineEx(new Vector2(cx - 1f, mid - backY), new Vector2(tip, mid), 1.8f, c);
                Raylib.DrawLineEx(new Vector2(cx - 1f, mid + backY), new Vector2(tip, mid), 1.8f, c);
                // Short barrel line behind the chevron
                Raylib.DrawLineEx(new Vector2(cx - 7f, mid), new Vector2(cx - 1f, mid), 1.8f, c);
                break;
            }
            case "snap":
            {
                // Double right chevron (>>) — conveys a fast, lighter "snap" shot vs FIRE's single chevron.
                float mid = cy, backY = 5.5f;
                foreach (float ox in new[] { -5f, 1f })
                {
                    Raylib.DrawLineEx(new Vector2(cx + ox, mid - backY), new Vector2(cx + ox + 5f, mid), 1.7f, c);
                    Raylib.DrawLineEx(new Vector2(cx + ox, mid + backY), new Vector2(cx + ox + 5f, mid), 1.7f, c);
                }
                break;
            }
            case "grenade":
            {
                // Oval body + short fuse line at top
                Raylib.DrawCircleLines((int)cx, (int)(cy + 2f), 5f, c);
                Raylib.DrawLineEx(new Vector2(cx, cy - 3f), new Vector2(cx, cy - 8f), 1.8f, c);
                // Small angled cap
                Raylib.DrawLineEx(new Vector2(cx - 2f, cy - 8f), new Vector2(cx + 2f, cy - 8f), 1.5f, c);
                break;
            }
            case "overwatch":
            {
                // Eye-shaped sector: two short arcs (approximated as poly lines)
                // Draw an outward arc (upper) and inward arc (lower) to suggest an eye
                int segs = 8;
                float r1 = 7f, r2 = 3.5f;
                float startA = -MathF.PI * 0.55f, endA = -MathF.PI * -0.55f; // roughly left-to-right
                // outer arc
                for (int i = 0; i < segs; i++)
                {
                    float t0 = startA + (endA - startA) * i / segs;
                    float t1 = startA + (endA - startA) * (i + 1) / segs;
                    Raylib.DrawLineEx(
                        new Vector2(cx + MathF.Cos(t0) * r1, cy + MathF.Sin(t0) * r1),
                        new Vector2(cx + MathF.Cos(t1) * r1, cy + MathF.Sin(t1) * r1),
                        1.5f, c);
                }
                // inner arc (mirrored vertically for the lower lid)
                float startB = MathF.PI * 0.55f, endB = MathF.PI * -0.55f;
                for (int i = 0; i < segs; i++)
                {
                    float t0 = startB + (endB - startB) * i / segs;
                    float t1 = startB + (endB - startB) * (i + 1) / segs;
                    Raylib.DrawLineEx(
                        new Vector2(cx + MathF.Cos(t0) * r1, cy + MathF.Sin(t0) * r1),
                        new Vector2(cx + MathF.Cos(t1) * r1, cy + MathF.Sin(t1) * r1),
                        1.5f, c);
                }
                // pupil dot
                Raylib.DrawCircleV(new Vector2(cx, cy), r2, c);
                break;
            }
            case "brace":
            {
                // Interrupt glyph: two facing brackets clamping a centre bar (a "hold/stagger" cue).
                float hw = 6f, hh = 6f;
                // left bracket [
                Raylib.DrawLineEx(new Vector2(cx - hw, cy - hh), new Vector2(cx - hw, cy + hh), 1.8f, c);
                Raylib.DrawLineEx(new Vector2(cx - hw, cy - hh), new Vector2(cx - hw + 3f, cy - hh), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx - hw, cy + hh), new Vector2(cx - hw + 3f, cy + hh), 1.6f, c);
                // right bracket ]
                Raylib.DrawLineEx(new Vector2(cx + hw, cy - hh), new Vector2(cx + hw, cy + hh), 1.8f, c);
                Raylib.DrawLineEx(new Vector2(cx + hw, cy - hh), new Vector2(cx + hw - 3f, cy - hh), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx + hw, cy + hh), new Vector2(cx + hw - 3f, cy + hh), 1.6f, c);
                // centre bar being clamped
                Raylib.DrawLineEx(new Vector2(cx - 2.5f, cy), new Vector2(cx + 2.5f, cy), 2.2f, c);
                break;
            }
            case "hunker":
            {
                // Downward-pointing chevron (duck-down arrow)
                float tip = cy + 6f;
                float hw = 6f, top = cy - 3f;
                Raylib.DrawLineEx(new Vector2(cx - hw, top), new Vector2(cx, tip), 1.8f, c);
                Raylib.DrawLineEx(new Vector2(cx + hw, top), new Vector2(cx, tip), 1.8f, c);
                // Short shield-cap line across the top
                Raylib.DrawLineEx(new Vector2(cx - hw, top), new Vector2(cx + hw, top), 1.5f, c);
                break;
            }
            case "reload":
            {
                // Three-quarter circular arc with an arrowhead at one end
                int segs = 9;
                float rad = 6f;
                float startA = MathF.PI * 0.3f; // start angle (slightly past bottom-right)
                float sweep  = MathF.PI * 1.6f; // about 290 degrees
                for (int i = 0; i < segs; i++)
                {
                    float t0 = startA + sweep * i / segs;
                    float t1 = startA + sweep * (i + 1) / segs;
                    Raylib.DrawLineEx(
                        new Vector2(cx + MathF.Cos(t0) * rad, cy + MathF.Sin(t0) * rad),
                        new Vector2(cx + MathF.Cos(t1) * rad, cy + MathF.Sin(t1) * rad),
                        1.8f, c);
                }
                // arrowhead at the end of the arc
                float eA = startA + sweep;
                var ep = new Vector2(cx + MathF.Cos(eA) * rad, cy + MathF.Sin(eA) * rad);
                // tangent direction: perpendicular to radius at eA
                float tang = eA + MathF.PI / 2f;
                var t1v = new Vector2(ep.X + MathF.Cos(tang) * 4f, ep.Y + MathF.Sin(tang) * 4f);
                var t2v = new Vector2(ep.X - MathF.Cos(tang) * 4f, ep.Y - MathF.Sin(tang) * 4f);
                // Move arrow tip slightly further along the arc direction
                var tip2 = new Vector2(ep.X + MathF.Cos(eA) * 3.5f, ep.Y + MathF.Sin(eA) * 3.5f);
                Raylib.DrawLineEx(t1v, tip2, 1.5f, c);
                Raylib.DrawLineEx(t2v, tip2, 1.5f, c);
                break;
            }
            case "hack":
            {
                // Two small interlocked squares (circuitry / terminal)
                float s = 4.5f;
                // left square
                var r1 = new Rectangle(cx - 8f, cy - s, s * 2f, s * 2f);
                Raylib.DrawRectangleLinesEx(r1, 1.2f, c);
                // right square, partially overlapping
                var r2 = new Rectangle(cx + 1f, cy - s, s * 2f, s * 2f);
                Raylib.DrawRectangleLinesEx(r2, 1.2f, c);
                // short connecting line at center
                Raylib.DrawLineEx(new Vector2(cx - 1f, cy), new Vector2(cx + 1f, cy), 1.5f, c);
                break;
            }
            case "extract":
            {
                // Up-arrow lifting into a landing-zone bracket (haul aboard)
                Raylib.DrawLineEx(new Vector2(cx, cy + 6f), new Vector2(cx, cy - 5f), 1.8f, c);
                Raylib.DrawLineEx(new Vector2(cx, cy - 5f), new Vector2(cx - 3.5f, cy - 1f), 1.8f, c);
                Raylib.DrawLineEx(new Vector2(cx, cy - 5f), new Vector2(cx + 3.5f, cy - 1f), 1.8f, c);
                // LZ bracket under the arrow
                Raylib.DrawLineEx(new Vector2(cx - 6f, cy + 6f), new Vector2(cx + 6f, cy + 6f), 1.6f, c);
                break;
            }
            case "beacon":
            {
                // A beacon mast with two broadcast arcs (a forward extraction signal being raised).
                Raylib.DrawLineEx(new Vector2(cx, cy + 6f), new Vector2(cx, cy - 3f), 1.8f, c);   // the mast
                Raylib.DrawCircleV(new Vector2(cx, cy - 4f), 1.6f, c);                            // the emitter
                Raylib.DrawLineEx(new Vector2(cx - 6f, cy + 6f), new Vector2(cx + 6f, cy + 6f), 1.6f, c); // base
                // two rising signal arcs off the emitter (left + right)
                foreach (int s in new[] { -1, 1 })
                {
                    Raylib.DrawLineEx(new Vector2(cx + s * 2f, cy - 6f), new Vector2(cx + s * 4f, cy - 8f), 1.3f, c);
                    Raylib.DrawLineEx(new Vector2(cx + s * 4f, cy - 3f), new Vector2(cx + s * 6f, cy - 5f), 1.3f, c);
                }
                break;
            }
            case "ability":
            {
                // Four-point star / spark: two crossing lines at different angles
                float len = 7f, lenD = 5f;
                // cardinal arms
                Raylib.DrawLineEx(new Vector2(cx, cy - len), new Vector2(cx, cy + len), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx - len, cy), new Vector2(cx + len, cy), 1.6f, c);
                // diagonal arms (shorter)
                Raylib.DrawLineEx(new Vector2(cx - lenD, cy - lenD), new Vector2(cx + lenD, cy + lenD), 1.2f, c);
                Raylib.DrawLineEx(new Vector2(cx + lenD, cy - lenD), new Vector2(cx - lenD, cy + lenD), 1.2f, c);
                // center dot
                Raylib.DrawCircleV(new Vector2(cx, cy), 1.5f, c);
                break;
            }
            case "item":
            {
                // Small canister: a thin rectangle body + a cap line on top
                float w2 = 4f, h2 = 7f;
                var body = new Rectangle(cx - w2, cy - h2 + 3f, w2 * 2f, h2 * 2f - 3f);
                Raylib.DrawRectangleLinesEx(body, 1.2f, c);
                // top cap
                Raylib.DrawLineEx(new Vector2(cx - w2 + 1f, cy - h2 + 3f), new Vector2(cx + w2 - 1f, cy - h2 + 3f), 1.5f, c);
                Raylib.DrawLineEx(new Vector2(cx - w2 + 1f, cy - h2 + 3f), new Vector2(cx - w2 + 1f, cy - h2), 1.5f, c);
                Raylib.DrawLineEx(new Vector2(cx + w2 - 1f, cy - h2 + 3f), new Vector2(cx + w2 - 1f, cy - h2), 1.5f, c);
                Raylib.DrawLineEx(new Vector2(cx - w2 + 1f, cy - h2), new Vector2(cx + w2 - 1f, cy - h2), 1.5f, c);
                break;
            }
            case "shove":
            {
                // A vertical "hand/plate" bar shoving a box to the right (forced movement).
                Raylib.DrawLineEx(new Vector2(cx - 6f, cy - 6f), new Vector2(cx - 6f, cy + 6f), 2f, c);   // the pushing plate
                var box = new Rectangle(cx - 3f, cy - 4f, 5f, 8f);                                        // the shoved block
                Raylib.DrawRectangleLinesEx(box, 1.3f, c);
                // motion arrow off the box's right edge
                Raylib.DrawLineEx(new Vector2(cx + 3f, cy), new Vector2(cx + 8f, cy), 1.7f, c);
                Raylib.DrawLineEx(new Vector2(cx + 5f, cy - 3f), new Vector2(cx + 8f, cy), 1.7f, c);
                Raylib.DrawLineEx(new Vector2(cx + 5f, cy + 3f), new Vector2(cx + 8f, cy), 1.7f, c);
                break;
            }
            case "drag":
            {
                // A box being pulled toward a hook on the left (arrow points back toward the dragger).
                var box = new Rectangle(cx + 1f, cy - 4f, 7f, 8f);                                          // the ally being pulled
                Raylib.DrawRectangleLinesEx(box, 1.3f, c);
                // a tug line + leftward arrow toward the dragger
                Raylib.DrawLineEx(new Vector2(cx + 1f, cy), new Vector2(cx - 8f, cy), 1.7f, c);
                Raylib.DrawLineEx(new Vector2(cx - 8f, cy), new Vector2(cx - 5f, cy - 3f), 1.7f, c);
                Raylib.DrawLineEx(new Vector2(cx - 8f, cy), new Vector2(cx - 5f, cy + 3f), 1.7f, c);
                break;
            }
            case "vault":
            {
                // An up-arc leaping over a low bar (the cover tile being vaulted).
                Raylib.DrawLineEx(new Vector2(cx - 7f, cy + 5f), new Vector2(cx + 7f, cy + 5f), 1.8f, c);   // the cover bar
                // a leaping arc over it
                var p0 = new Vector2(cx - 7f, cy + 3f);
                var p1 = new Vector2(cx,      cy - 7f);
                var p2 = new Vector2(cx + 7f, cy + 3f);
                Raylib.DrawLineEx(p0, p1, 1.6f, c);
                Raylib.DrawLineEx(p1, p2, 1.6f, c);
                // arrowhead at the landing
                Raylib.DrawLineEx(p2, new Vector2(cx + 4f, cy + 1f), 1.5f, c);
                Raylib.DrawLineEx(p2, new Vector2(cx + 9f, cy + 1f), 1.5f, c);
                break;
            }
        }
    }

    /// A small primitive-drawn glyph for the mission objective, drawn left of the
    /// objective text in the top bar (5.4/5.5: shape redundancy so the objective reads
    /// by icon as well as colour). Inherits the objective's accent colour.
    static void DrawObjectiveIcon(Objective o, float cx, float cy, Color c)
    {
        switch (o)
        {
            case Objective.Hack:   // terminal brackets [ ] with a centre node
                Raylib.DrawLineEx(new Vector2(cx - 6f, cy - 5f), new Vector2(cx - 6f, cy + 5f), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx - 6f, cy - 5f), new Vector2(cx - 3f, cy - 5f), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx - 6f, cy + 5f), new Vector2(cx - 3f, cy + 5f), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx + 6f, cy - 5f), new Vector2(cx + 6f, cy + 5f), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx + 6f, cy - 5f), new Vector2(cx + 3f, cy - 5f), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx + 6f, cy + 5f), new Vector2(cx + 3f, cy + 5f), 1.6f, c);
                Raylib.DrawCircleV(new Vector2(cx, cy), 2f, c);
                break;
            case Objective.Evac:   // extraction: up-arrow rising out of a baseline
                Raylib.DrawLineEx(new Vector2(cx - 6f, cy + 6f), new Vector2(cx + 6f, cy + 6f), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx, cy + 5f), new Vector2(cx, cy - 6f), 1.8f, c);
                Raylib.DrawLineEx(new Vector2(cx - 4f, cy - 2f), new Vector2(cx, cy - 6f), 1.8f, c);
                Raylib.DrawLineEx(new Vector2(cx + 4f, cy - 2f), new Vector2(cx, cy - 6f), 1.8f, c);
                break;
            case Objective.Escort: // VIP diamond (matches the on-board VIP marker)
                Raylib.DrawLineEx(new Vector2(cx, cy - 7f), new Vector2(cx + 6f, cy), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx + 6f, cy), new Vector2(cx, cy + 7f), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx, cy + 7f), new Vector2(cx - 6f, cy), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx - 6f, cy), new Vector2(cx, cy - 7f), 1.6f, c);
                break;
            case Objective.Sabotage: // demolition charge: body + radiating spark
            {
                Raylib.DrawCircleLines((int)cx, (int)(cy + 1f), 4f, c);
                for (int i = 0; i < 6; i++)
                {
                    float a = i * MathF.PI / 3f;
                    Raylib.DrawLineEx(new Vector2(cx + MathF.Cos(a) * 5f, cy + 1f + MathF.Sin(a) * 5f),
                                      new Vector2(cx + MathF.Cos(a) * 7.5f, cy + 1f + MathF.Sin(a) * 7.5f), 1.4f, c);
                }
                break;
            }
            case Objective.Rescue: // cage: a box with two vertical bars
                Raylib.DrawRectangleLinesEx(new Rectangle(cx - 6f, cy - 6f, 12f, 12f), 1.4f, c);
                Raylib.DrawLineEx(new Vector2(cx - 2f, cy - 6f), new Vector2(cx - 2f, cy + 6f), 1.3f, c);
                Raylib.DrawLineEx(new Vector2(cx + 2f, cy - 6f), new Vector2(cx + 2f, cy + 6f), 1.3f, c);
                break;
            case Objective.Defend: // shield: flat top, sides taper to a bottom point
                Raylib.DrawLineEx(new Vector2(cx - 6f, cy - 6f), new Vector2(cx + 6f, cy - 6f), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx - 6f, cy - 6f), new Vector2(cx - 6f, cy + 1f), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx + 6f, cy - 6f), new Vector2(cx + 6f, cy + 1f), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx - 6f, cy + 1f), new Vector2(cx, cy + 7f), 1.6f, c);
                Raylib.DrawLineEx(new Vector2(cx + 6f, cy + 1f), new Vector2(cx, cy + 7f), 1.6f, c);
                break;
            case Objective.Decapitate: // HVT: a reticle ring with crosshair ticks + two target "eyes"
                Raylib.DrawCircleLines((int)cx, (int)cy, 7f, c);
                Raylib.DrawCircleLines((int)cx, (int)cy, 7.5f, c);   // thicker ring (single-mark)
                Raylib.DrawLineEx(new Vector2(cx - 9f, cy), new Vector2(cx - 5f, cy), 1.5f, c);
                Raylib.DrawLineEx(new Vector2(cx + 5f, cy), new Vector2(cx + 9f, cy), 1.5f, c);
                Raylib.DrawLineEx(new Vector2(cx, cy - 9f), new Vector2(cx, cy - 5f), 1.5f, c);
                Raylib.DrawLineEx(new Vector2(cx, cy + 5f), new Vector2(cx, cy + 9f), 1.5f, c);
                Raylib.DrawCircleV(new Vector2(cx - 2.5f, cy - 1f), 1.4f, c);   // marked "eyes"
                Raylib.DrawCircleV(new Vector2(cx + 2.5f, cy - 1f), 1.4f, c);
                break;
            default:               // Eliminate: crosshair (target reticle)
                Raylib.DrawCircleLines((int)cx, (int)cy, 6f, c);
                Raylib.DrawLineEx(new Vector2(cx - 8f, cy), new Vector2(cx - 3f, cy), 1.5f, c);
                Raylib.DrawLineEx(new Vector2(cx + 3f, cy), new Vector2(cx + 8f, cy), 1.5f, c);
                Raylib.DrawLineEx(new Vector2(cx, cy - 8f), new Vector2(cx, cy - 3f), 1.5f, c);
                Raylib.DrawLineEx(new Vector2(cx, cy + 3f), new Vector2(cx, cy + 8f), 1.5f, c);
                break;
        }
    }

    // Hover help for the action buttons (explains FIRE/GRENADE/abilities/etc.).
    // W11: the body word-wraps to a ~400px column and the box grows 17px per line — the ~270-char
    // BEACON desc used to be laid out as ONE line (~1600px wide on a 1280px screen: mostly off-
    // screen, unreadable). Every verb's full help now fits on screen by construction.
    static void DrawActionHelp(Game g)
    {
        if (ActionButtons == null) return;
        var m = Raylib.GetMousePosition();
        foreach (var b in ActionButtons)
        {
            // Harness seam (screenshot only): SIGHTLINE_HELPBTN=<id> treats that button as hovered,
            // so a SPECIFIC verb's help card can be framed headless (Program.cs's cursor park can
            // only hit whichever button happens to sit at its fixed point). Inert when unset.
            bool hover = Raylib.CheckCollisionPointRec(m, b.Rect) || (g.NoPersist && _forcedHelpId != null && b.Id == _forcedHelpId);
            if (!hover) continue;
            string desc = ActionDesc(g, b.Id);
            if (string.IsNullOrEmpty(desc)) return;
            string title = b.Label;
            var lines = WrapText(desc, 12, 380);
            int bodyW = 0;
            foreach (var ln in lines) bodyW = Math.Max(bodyW, (int)Raylib.MeasureTextEx(Cfg.Font, ln, 12, 1f).X);
            int w = Math.Max((int)Raylib.MeasureTextEx(Cfg.Font, title, 14, 1f).X, bodyW) + 20;
            int h = 34 + lines.Count * 17 + 8;
            int x = (int)(b.Rect.X + b.Rect.Width / 2 - w / 2);
            int y = (int)b.Rect.Y - h - 8;
            x = Util.Clamp(x, 8, Cfg.ScreenW - w - 8);
            var box = new Rectangle(x, y, w, h);
            Raylib.DrawRectangleRounded(box, 0.14f, 6, Pal.RGBA(10, 14, 19, 252));
            Raylib.DrawRectangleLinesEx(box, 1.2f, Pal.Friend);
            Raylib.DrawTextEx(Cfg.Font, title, new Vector2(x + 10, y + 8), 14, 1f, Pal.Accent);
            int ty = y + 28;
            foreach (var ln in lines) { Raylib.DrawTextEx(Cfg.Font, ln, new Vector2(x + 10, ty), 12, 1f, Pal.Txt); ty += 17; }
            return;
        }
    }

    // W11 harness seam for DrawActionHelp (read once; null in every normal run, and the use site
    // gates on g.NoPersist so a stray env var can never pin a help card open in live play).
    static readonly string _forcedHelpId = Environment.GetEnvironmentVariable("SIGHTLINE_HELPBTN");

    static string ActionDesc(Game g, string id)
    {
        switch (id)
        {
            case "shoot": return "Aimed shot at a target in range + line of sight. Full aim, costs 1 action and does NOT end the turn — keep your other action to reposition (one shot/turn).";
            case "grenade": return "Lob a grenade: AoE that ignores cover, hits both teams, clears low cover.";
            case "shove": return "Shove an adjacent enemy 1 tile back (breaks its overwatch + exposes it). Blocked = collision damage. 1 action, won't end your turn, once/turn.";
            case "drag": return "Pull an adjacent ally 1 tile toward you (saves wounded, speeds the march to evac). 1 action, won't end your turn, once/turn.";
            case "vault": return "Leap an adjacent cover tile to the open floor beyond it - cross an impassable screen to flank or escape. 1 action, won't end your turn, once/turn.";
            case "overwatch": return "Watch: fire a reaction shot at the first foe that moves in sight.";
            case "focusow": return "Braced kill-lane: reaction fire only inside a 90-degree cone toward the aimed tile, but at +aim. Blind outside the cone.";
            case "brace": return "Brace a DISRUPTING reaction: on a hit it STAGGERS the mover (denies its action this turn) for reduced damage. Deny the enemy's alpha instead of going for the kill.";
            case "hunker": return "Hunker down for extra cover defense; you can't be crit.";
            case "hack": return g.HasSabotage
                ? $"Plant a demolition charge on an adjacent site ({g.SabotageBlown.Count}/{g.SabotageSites.Count} set). Costs 1 action."
                : $"Work the terminal ({g.HackProgress}/{Game.HackRequired} done). Costs 1 action.";
            case "beacon": return g.Objective == Objective.Escort
                ? "Deploy a forward evac beacon for the VIP: opens a 3x3 extraction zone right here (in addition to the far corner). ESCORT: needs the FAR THIRD of the map and a COLD LZ (no living enemy within 3 tiles - dormant counts). One per mission. Costs 1 action, won't end your turn."
                : "Deploy a forward evac beacon on your tile: opens a 3x3 extraction zone right here (in addition to the far corner). One per mission. Costs 1 action, won't end your turn.";
            case "extract": return "Haul an adjacent ally / asset aboard - pulls them into the extraction zone. Costs 1 action.";
            case "reload": return "Reload your weapon to full.";
            case "ability":
                return g.Selected != null && g.Selected.Ability != AbilityKind.None
                    ? g.Selected.AbilityDesc + (g.Selected.AbilityCd > 0
                        ? $"  (cooldown: {g.Selected.AbilityCd} turn{(g.Selected.AbilityCd == 1 ? "" : "s")})"
                        : $"  (cooldown {Unit.AbilityCooldownFor(g.Selected.Ability)} turn{(Unit.AbilityCooldownFor(g.Selected.Ability) == 1 ? "" : "s")})")
                    : "";
            case "item":
                return g.Selected != null && g.Selected.Item != ItemKind.None
                    ? g.Selected.ItemDesc + "  (1 charge/mission)"
                    : "";
            default: return "";
        }
    }

    // ---------------- tooltip ----------------
    // Perfect-information contract (DESIGN.md): the shot odds must explain WHY the number
    // is what it is. We surface EVERY modifier Combat.ComputeOdds applies — the struct
    // flags (cover/flank/high-ground/steady/ambush) PLUS the ones that ride on live
    // attacker/target state (bonds, earned traits, range/exposure perks, suppression,
    // wounds, daze, target hunker/smoke). Each badge's condition mirrors ComputeOdds
    // EXACTLY so the explanation always matches the math. Display-only; no rule changes.
    static void DrawTooltip(Game g)
    {
        if (!g.ShowOdds) return;
        var o = g.HoverOdds;

        // Recover the same attacker/target pair ComputeOdds was called with (see
        // Game.UpdateHoverAndAim): attacker is always the selected soldier; the target is
        // the locked aim target in aim mode, else the enemy under the cursor.
        Unit a = g.Selected;
        Unit d = g.AimMode ? g.AimTarget : g.UnitAt(g.HoverX, g.HoverY);

        // Build the badge list. Order: target-cover/state, then attacker buffs, then
        // attacker penalties — so advantages and warnings stay visually grouped.
        // W10 (owner feedback — "penalties and bonuses are basically unreadable"): each badge is
        // a (label, value) PAIR. The label draws in plain Pal.Txt; only the signed value draws in
        // its role color (Foe = penalty, Good = bonus), right-aligned in one consistent column.
        var flags = new System.Collections.Generic.List<(string label, string val, Color col)>();
        // — already in the odds struct —
        // Every badge shows its SIGNED MAGNITUDE from the real Combat/Unit constants (now exact:
        // crit is summed flat, no damping), so the player can read WHY the odds are what they are.
        // Cover shows the HIT penalty it imposes (cover.Defense): LOW -20 / HIGH -40, halved when
        // PARTIAL. EXPOSED (no cover) shows the +18 situational crit it grants the attacker.
        if (o.Flanked)   flags.Add(("FLANKED", "no cover", Pal.Accent));       // cover negated + EXPOSED crit (below)
        if (o.Hunkered)  flags.Add(("HUNKERED", "-25 aim, no crit", Pal.Foe)); // target dug in
        if (o.CoverLevel == 2 && !o.Partial) flags.Add(("HIGH COVER", "-40 aim", Pal.Foe));
        else if (o.CoverLevel == 2 && o.Partial) flags.Add(("PARTIAL HIGH COVER", "-20 aim", Pal.Foe));
        else if (o.CoverLevel == 1 && !o.Partial) flags.Add(("LOW COVER", "-20 aim", Pal.Foe));
        else if (o.CoverLevel == 1 && o.Partial)  flags.Add(("PARTIAL LOW COVER", "-10 aim", Pal.Foe));
        if (o.CoverLevel == 0 && !o.Hunkered) flags.Add(("EXPOSED", "+18 crit", Pal.Good));
        if (o.HighGround) flags.Add(("HIGH GROUND", $"+{Combat.HighGroundAim} aim, +{Combat.HighGroundCrit} crit", Pal.Good));
        if (o.SeesOver)  flags.Add(("OVER LOW COVER", "ignores low", Pal.Good));
        if (o.Steady)    flags.Add(("STEADY", $"+{Combat.SteadyAim} aim, +{Combat.SteadyCrit} crit", Pal.Good));
        if (o.Ambush)    flags.Add(("AMBUSH", $"+{Combat.AmbushAim} aim, +{Combat.AmbushCrit} crit", Pal.Good));
        if (o.ExposedFire) flags.Add(("EXPOSED BY FIRE", $"+{Combat.ExposedFireAim} aim, +{Combat.ExposedFireCrit} crit", Pal.Good));   // HORIZON: target fired last turn + stayed put
        // Surface the hidden streak-breaker: after consecutive misses this soldier's next
        // shot quietly aims truer (the bonus is in the roll, NOT in the HIT% shown). Naming it
        // "STEADYING" tells the player the safety net is working so a miss streak feels recoverable.
        if (o.StreakBonus > 0) flags.Add(("STEADYING", $"+{o.StreakBonus} aim", Pal.Good));
        // TEMPO: a SECOND shot in the same turn is a rushed follow-up at the SnapAim penalty (the
        // HitChance shown already reflects it). RUN&GUN's bonus shot + the GUNSLINGER perk are full
        // aim — GUNSLINGER instead gets a "DOUBLE-TAP" confirmation badge.
        if (g.AimMode && a != null && a.FiredThisTurn && !a.RunGun)
            flags.Add(a.HasPerk(Perk.Gunslinger) ? ("DOUBLE-TAP", "full aim", Pal.Good) : ("RUSHED 2ND SHOT", $"{Game.SnapAim} aim", Pal.Foe));

        // — modifiers that read live attacker/target state (mirror Combat.ComputeOdds) —
        if (a != null && d != null)
        {
            float dist = Util.TileDist(a.X, a.Y, d.X, d.Y);
            bool tgtHurt    = d.MaxHp > 0 && d.Hp * 2 <= d.MaxHp;   // at/below half HP (Killer)
            bool tgtSubHalf = d.MaxHp > 0 && d.Hp * 2 <  d.MaxHp;   // strictly below half (Executioner)
            bool selfHurt   = a.MaxHp > 0 && a.Hp * 2 <= a.MaxHp;   // attacker bloodied (Cold Blood)

            // attacker advantages (green) — each shown only when its condition holds THIS shot, with
            // its real signed magnitude. NOTE: the redundant crit perks (Deadeye/Opportunist/Point
            // Blank/Vanguard) are no longer offered AND no longer read by ComputeOdds, so they're
            // intentionally absent here — only the kept Executioner / First Strike crit pair shows.
            if (a.BondAura)                                   flags.Add(("BOND", $"+{Unit.BondAim} aim", Pal.Good));
            if (a.HasTrait(Trait.Killer) && tgtHurt)          flags.Add(("KILLER", $"+{Unit.KillerAim} aim", Pal.Good));
            if (a.HasTrait(Trait.Vengeful) && a.AllyDown)     flags.Add(("VENGEFUL", $"+{Unit.VengefulAim} aim", Pal.Good));
            if (a.HasTrait(Trait.ColdBlood) && selfHurt)      flags.Add(("COLD BLOOD", $"+{Unit.ColdBloodCrit} crit", Pal.Good));
            if (a.HasPerk(Perk.LockOn) && o.CoverLevel == 0)  flags.Add(("LOCK-ON", $"+{Unit.PerkAim} aim", Pal.Good));
            if (a.HasPerk(Perk.CloseQuarters) && dist <= Unit.CloseRange) flags.Add(("CLOSE QUARTERS", $"+{Unit.PerkAim} aim", Pal.Good));
            if (a.HasPerk(Perk.Marksman) && dist >= Unit.LongRange)       flags.Add(("MARKSMAN", $"+{Unit.PerkAim} aim", Pal.Good));
            if (a.HasPerk(Perk.Executioner) && tgtSubHalf)    flags.Add(("EXECUTIONER", $"+{Unit.ExecutionerCrit} crit", Pal.Good));
            if (a.HasPerk(Perk.GiantSlayer) && d.MaxHp > 0 && d.Hp >= d.MaxHp) flags.Add(("FIRST STRIKE", $"+{Unit.FirstStrikeCrit} crit", Pal.Good));
            if (o.Crossfire)                                  flags.Add(("CROSSFIRE", $"+{Combat.CrossfireAim} aim, +{Combat.CrossfireCrit} crit", Pal.Good));   // a squadmate threatens this target from a converging angle
            if (o.Marked)                                     flags.Add(("MARKED", $"+{Combat.MarkAim} aim", Pal.Good));      // a sharpshooter has designated this foe (squad-wide focus-fire bonus)
            if (d.Pinned > 0)                                 flags.Add(("TARGET SUPPRESSED", "shoots wild", Pal.Good));  // a gunner has pinned this foe (it shoots wild + can't dash)

            // attacker penalties (red) — these quietly drag the hit% down (signed magnitudes)
            if (a.Suppress > 0)                               flags.Add(("SUPPRESSED", $"-{a.Suppress} aim", Pal.Foe));
            if (a.Wound > 0)                                  flags.Add(("WOUNDED", $"-{Unit.WoundAim} aim", Pal.Foe));
            if (a.HasStatus(StatusKind.Disoriented))          flags.Add(("DISORIENTED", $"-{Unit.DisorientAim} aim", Pal.Foe));

            // target obscured in smoke (it's shootable — LoS clears the endpoint tile —
            // but harder to make out). Neutral tag: smoke is not in the hit% math.
            if (g.Grid.IsSmoke(d.X, d.Y))                     flags.Add(("SMOKED", "sight only", Pal.TxtDim));

            // SIGNAL W8 — pod-morale telegraph: the hovered foe's pod is ONE KILL from the rout
            // threshold. "POD alive/orig | NEXT KILL ROUTS" makes the breaking kill a PLAN; if a
            // WARBRINGER's banner holds this member, say THAT instead (the mark must never lie —
            // and the line points the player at the counter: the banner).
            if (d.Team == Team.Enemy && g.PodAtWaverPoint(d))
            {
                var (alive, orig) = g.PodStrength(d.PodId);
                if (g.BannerNear(d)) flags.Add(($"POD {alive}/{orig}", "HELD BY BANNER", Pal.Foe));
                else                 flags.Add(($"POD {alive}/{orig}", "NEXT KILL ROUTS", Pal.Suspect));
            }
        }

        // Layout (W10, owner feedback): ONE modifier per line at a legible 13px with real line
        // spacing; labels left in plain text, signed values right-aligned in one consistent
        // column. The panel grows to honestly fit its content (placement logic unchanged).
        const int lineH = 19, fontFlag = 13, pad = 12, colGap = 22;
        bool graze = o.GrazeFloor > 0;
        int hdr = graze ? 100 : 80;                        // HIT / CRIT / DMG (+ GRAZE) header rows

        // W11 ENEMY ID: a hostile under the cursor is NAMED — "VIPER — SNIPER" + the bestiary's
        // first clause — above the odds, so the roster is learned where it's fought. Shares
        // Codex.BlurbFor with the codex/banner/lose-card, so the ID text can never drift.
        string idTitle = null;
        System.Collections.Generic.List<string> idLines = null;
        if (d != null && d.Team == Team.Enemy)
        {
            string clause = Codex.BlurbClause(d.Cls);
            if (!string.IsNullOrEmpty(clause))
            {
                idTitle = $"{Codex.NameFor(d.Cls)} — {d.Cls}";
                idLines = WrapText(clause, 11, 300);
            }
        }
        int idH = idTitle != null ? 26 + idLines.Count * 14 + 6 : 0;

        int maxLab = 0, maxVal = 0;
        foreach (var f in flags)
        {
            maxLab = Math.Max(maxLab, (int)Raylib.MeasureTextEx(Cfg.Font, f.label, fontFlag, 1f).X);
            maxVal = Math.Max(maxVal, (int)Raylib.MeasureTextEx(Cfg.Font, f.val, fontFlag, 1f).X);
        }
        int w = Math.Max(190, maxLab + colGap + maxVal) + pad * 2;
        if (idTitle != null)
        {
            w = Math.Max(w, (int)Raylib.MeasureTextEx(Cfg.Font, idTitle, 14, 1f).X + pad * 2);
            foreach (var ln in idLines)
                w = Math.Max(w, (int)Raylib.MeasureTextEx(Cfg.Font, ln, 11, 1f).X + pad * 2);
        }
        int h = idH + hdr + flags.Count * lineH + (flags.Count > 0 ? 10 : 4);

        var m = Raylib.GetMousePosition();
        int x = (int)m.X - w / 2;
        int y = (int)m.Y - h - 18;
        x = Util.Clamp(x, 8, Cfg.ScreenW - w - 8);
        y = Util.Clamp(y, 64, Cfg.ScreenH - h - 8);

        var box = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(box, 0.12f, 8, Pal.RGBA(10, 14, 19, 251));
        // W9 color roles (DESIGN.md §3.H — one job per accent): the frame is a neutral panel
        // border, so RED returns to threat-only duty and an 8% desperation shot no longer sits
        // inside a decorative red box that means nothing.
        Raylib.DrawRectangleLinesEx(box, 1.5f, Pal.PanelBd);

        // the ID header block (name in threat-red — it's a hostile — clause in dim text), then a
        // hairline; the odds rows below all shift down by idH.
        if (idTitle != null)
        {
            Raylib.DrawTextEx(Cfg.Font, idTitle, new Vector2(x + pad, y + 9), 14, 1f, Pal.Foe);
            int iy = y + 27;
            foreach (var ln in idLines) { Raylib.DrawTextEx(Cfg.Font, ln, new Vector2(x + pad, iy), 11, 1f, Pal.TxtDim); iy += 14; }
            Raylib.DrawRectangle(x + pad, y + idH - 3, w - pad * 2, 1, Pal.RGBA(38, 49, 63, 200));
        }
        int oy = y + idH;   // top of the odds block

        // HIT is banded by CONFIDENCE (mirrors the HP-bar banding precedent in DrawUnitCard):
        // >=70 good / 40-69 caution / <40 threat — so a desperation shot reads red, not
        // reassuring green. The % text itself is the redundant channel (5.1 rule), so the
        // meaning survives the colorblind palette.
        Raylib.DrawTextEx(Cfg.Font, "HIT", new Vector2(x + pad, oy + 12), 13, 1f, Pal.TxtDim);
        string hit = $"{o.HitChance}%";
        Color hitCol = o.HitChance >= 70 ? Pal.Good : (o.HitChance >= 40 ? Pal.Accent : Pal.Foe);
        Raylib.DrawTextEx(Cfg.Font, hit, new Vector2(x + w - (int)Raylib.MeasureTextEx(Cfg.Font, hit, 24, 1f).X - pad, oy + 8), 24, 1f, hitCol);

        Raylib.DrawTextEx(Cfg.Font, "CRIT", new Vector2(x + pad, oy + 38), 13, 1f, Pal.TxtDim);
        string crit = $"{o.CritChance}%";
        Raylib.DrawTextEx(Cfg.Font, crit, new Vector2(x + w - (int)Raylib.MeasureTextEx(Cfg.Font, crit, 16, 1f).X - pad, oy + 36), 16, 1f, Pal.Accent);

        // DMG is a neutral fact, not a threat — plain text hue. The damage range and the graze
        // band are SEPARATE fields (W10): "DMG 3-5" then "GRAZE 3" on its own line, so the
        // consolation floor never reads as part of the full-hit range.
        Raylib.DrawTextEx(Cfg.Font, "DMG", new Vector2(x + pad, oy + 58), 13, 1f, Pal.TxtDim);
        string dmg = $"{o.DmgMin}-{o.DmgMax}";
        Raylib.DrawTextEx(Cfg.Font, dmg, new Vector2(x + w - (int)Raylib.MeasureTextEx(Cfg.Font, dmg, 16, 1f).X - pad, oy + 56), 16, 1f, Pal.Txt);
        // Graze safety net: a near-miss still hits for this guaranteed floor instead of whiffing
        // (so missing is never *nothing*). Its own row, in the Accent hue used for graze FX so it
        // reads as "the consolation hit", not the full damage.
        if (graze)
        {
            Raylib.DrawTextEx(Cfg.Font, "GRAZE", new Vector2(x + pad, oy + 78), 13, 1f, Pal.TxtDim);
            string gz = $"{o.GrazeFloor} on near miss";
            Raylib.DrawTextEx(Cfg.Font, gz, new Vector2(x + w - (int)Raylib.MeasureTextEx(Cfg.Font, gz, 13, 1f).X - pad, oy + 77), 13, 1f, Pal.Accent);
        }

        // a hairline above the badges separates them from the headline numbers
        if (flags.Count > 0)
            Raylib.DrawRectangle(x + pad, oy + hdr - 6, w - pad * 2, 1, Pal.RGBA(38, 49, 63, 200));

        for (int i = 0; i < flags.Count; i++)
        {
            int fyy = oy + hdr + i * lineH;
            // label: plain readable text (FLANKED keeps its warning accent — it's a keyword, not a number)
            Color labCol = flags[i].label == "FLANKED" ? Pal.Accent : Pal.Txt;
            Raylib.DrawTextEx(Cfg.Font, flags[i].label, new Vector2(x + pad, fyy), fontFlag, 1f, labCol);
            // value: right-aligned signed magnitude in its role color (Foe penalty / Good bonus)
            int vw = (int)Raylib.MeasureTextEx(Cfg.Font, flags[i].val, fontFlag, 1f).X;
            Raylib.DrawTextEx(Cfg.Font, flags[i].val, new Vector2(x + w - pad - vw, fyy), fontFlag, 1f, flags[i].col);
        }
    }

    // ---------------- turn banner sweep ----------------
    static void DrawBanner(Game g)
    {
        if (g.BannerTimer <= 0) return;
        float a = Util.Clamp(g.BannerTimer / 0.5f, 0f, 1f);
        if (g.BannerTimer > g.BannerMax - 0.3f) a = Util.Clamp((g.BannerMax - g.BannerTimer) / 0.3f, 0f, 1f);
        Color c = g.BannerEnemy ? Pal.Foe : Pal.Friend;
        int bandY = Cfg.ScreenH / 2 - 46;
        Raylib.DrawRectangle(0, bandY, Cfg.ScreenW, 92, Raylib.Fade(Pal.RGBA(8, 12, 17), a * 0.72f));
        Raylib.DrawRectangle(0, bandY, Cfg.ScreenW, 3, Raylib.Fade(c, a));
        Raylib.DrawRectangle(0, bandY + 89, Cfg.ScreenW, 3, Raylib.Fade(c, a));
        int fs = 44;
        int tw = (int)Raylib.MeasureTextEx(Cfg.Font, g.BannerText, fs, 1f).X;
        // W11: a banner with a sub-line (NEW CONTACT's archetype + behaviour clause) lifts the main
        // text a touch and sets the ID line beneath it; ordinary banners are unchanged (null sub).
        bool hasSub = !string.IsNullOrEmpty(g.BannerSub);
        int mainY = bandY + (hasSub ? 14 : 24);
        Raylib.DrawTextEx(Cfg.Font, g.BannerText, new Vector2(Cfg.ScreenW / 2 - tw / 2, mainY), fs, 1f, Raylib.Fade(c, a));
        if (hasSub)
        {
            int sw = (int)Raylib.MeasureTextEx(Cfg.Font, g.BannerSub, 15, 1f).X;
            Raylib.DrawTextEx(Cfg.Font, g.BannerSub, new Vector2(Cfg.ScreenW / 2 - sw / 2, mainY + fs + 4), 15, 1f, Raylib.Fade(Pal.Txt, a * 0.92f));
        }
    }

    // ---------------- overlays ----------------
    static void DrawOverlays(Game g)
    {
        if (g.Phase == Phase.Intro)
        {
            DrawIntro(g);
            DrawHeatSelector(g);
        }
        else if (g.Phase == Phase.Barracks)
            DrawBarracks(g);
        else if (g.Phase == Phase.Win)
            DrawEndScreen(g, true);
        else if (g.Phase == Phase.Lose)
            DrawEndScreen(g, false);
        else if (g.Phase == Phase.Draft)
            DrawDraft(g);
        else if (g.Phase == Phase.WarRoom)
            DrawWarRoom(g);
        else if (g.Phase == Phase.Codex)
            DrawCodex(g);
        else if (g.Phase == Phase.SkirmishSetup)
            DrawSkirmishSetup(g);
    }

    // ============================================================================
    //  TITLE / INTRO  — an animated geometric title screen (replaces the flat card).
    //  A drifting tactical grid + a horizontal scanning "sightline" sweep + slow
    //  parallax reticle motifs behind a stylized SIGHTLINE wordmark, with the rules
    //  set as an elegant left-railed briefing. All motion is driven by GetTime() so
    //  a fixed screenshot frame is reproducible (content may differ frame-to-frame,
    //  which is fine + intended); no per-frame RNG.
    // ============================================================================
    static void DrawIntro(Game g)
    {
        float t = (float)Raylib.GetTime();
        DrawTacticalBackdrop(t, Pal.Friend, 0.0f);

        int W = Cfg.ScreenW, H = Cfg.ScreenH;

        // ---- wordmark ----
        // A large letter-spaced SIGHTLINE with a crosshair "I"-tick motif, a soft glow,
        // and a scan line sweeping vertically through the glyphs.
        float titleIn = PanelAnim("introTitle", 0.5f);
        int tfs = 92;
        string word = "SIGHTLINE";
        float spacing = 6f;
        Vector2 wm = Raylib.MeasureTextEx(Cfg.Font, word, tfs, spacing);
        float wx = W / 2f - wm.X / 2f;
        float wy = 150f - (1f - Util.EaseOutBack(titleIn)) * 26f;   // settle down + slight overshoot
        // soft glow halo (a few offset dim copies)
        Color glow = Raylib.Fade(Pal.Friend, 0.12f * titleIn);
        for (int i = 1; i <= 3; i++)
            Raylib.DrawTextEx(Cfg.Font, word, new Vector2(wx, wy - i), tfs, spacing, glow);
        // a subtle vertical accent gradient over the wordmark via a clipped scan band
        Raylib.DrawTextEx(Cfg.Font, word, new Vector2(wx, wy), tfs, spacing, Raylib.Fade(Pal.Txt, titleIn));
        // scan line passing down through the wordmark (loops every ~3.5s)
        float scanT = (t * 0.30f) % 1f;
        float scanY = wy + scanT * tfs;
        Raylib.DrawRectangleGradientH((int)wx - 10, (int)scanY, (int)wm.X + 20, 2,
            Raylib.Fade(Pal.Friend, 0f), Raylib.Fade(Pal.Friend, 0.5f * titleIn));
        Raylib.DrawRectangleGradientH((int)(wx + wm.X / 2), (int)scanY, (int)(wm.X / 2) + 10, 2,
            Raylib.Fade(Pal.Friend, 0.5f * titleIn), Raylib.Fade(Pal.Friend, 0f));
        // bracket ticks framing the wordmark (target-reticle motif)
        float bo = 18f + 6f * MathF.Sin(t * 1.6f);   // breathing offset
        DrawCornerBrackets(new Rectangle(wx - bo, wy + 6, wm.X + bo * 2, tfs - 4), Raylib.Fade(Pal.Friend, 0.55f * titleIn), 16f);

        // subtitle
        float subIn = PanelAnim("introSub", 0.4f, 0.18f);
        string sub = "TURN-BASED SQUAD TACTICS";
        int sfs = 18;
        Vector2 sm = Raylib.MeasureTextEx(Cfg.Font, sub, sfs, 3f);
        Raylib.DrawTextEx(Cfg.Font, sub, new Vector2(W / 2f - sm.X / 2f, wy + tfs + 8), sfs, 3f,
            Raylib.Fade(Pal.TxtDim, subIn));
        // thin divider under the subtitle that wipes outward
        float divW = (sm.X + 80) * Util.EaseOutQuad(subIn);
        Raylib.DrawRectangle((int)(W / 2f - divW / 2f), (int)(wy + tfs + 36), (int)divW, 1, Raylib.Fade(Pal.Friend, 0.4f * subIn));

        // ---- briefing rules (elegant left-railed list, progressively revealed) ----
        string[] rules =
        {
            $"Lead one squad through {Run.MaxMissions} escalating missions.",
            "2 actions per soldier — firing is 1 action (one shot/turn), so move AND shoot, in either order.",
            "Hug cover to cut enemy aim; get flanked and you're exposed.",
            "Seize the high ground for an aim and crit edge.",
            "Each class wields a signature ability (key 5) on a short cooldown.",   // W11: abilities are cooldown-based, not 1/mission
            "Kills earn promotions; survivors carry wounds and rank onward.",
        };
        int ry0 = (int)(wy + tfs + 70);
        int rx = W / 2 - 320;
        int rowH = 30;
        // a faint vertical rail the bullets hang off
        float railIn = PanelAnim("introRail", 0.5f, 0.25f);
        Raylib.DrawRectangle(rx - 16, ry0 + 2, 2, (int)(rules.Length * rowH * Util.EaseOutQuad(railIn)), Raylib.Fade(Pal.Friend, 0.45f));
        for (int i = 0; i < rules.Length; i++)
        {
            float in_ = PanelAnim($"introRule{i}", 0.32f, 0.30f + i * 0.07f);
            if (in_ <= 0f) continue;
            float a = Util.EaseOutQuad(in_);
            int yy = ry0 + i * rowH + (int)((1f - a) * 8f);
            // a small diamond node on the rail
            Raylib.DrawRectanglePro(new Rectangle(rx - 16, yy + 9, 6, 6), new Vector2(3, 3), 45f, Raylib.Fade(Pal.Friend, a));
            Raylib.DrawTextEx(Cfg.Font, rules[i], new Vector2(rx, yy), 15, 1f, Raylib.Fade(Pal.Txt, 0.92f * a));
        }

        // ---- buttons ----
        // W12 coherent hierarchy: the CAMPAIGN verbs (CONTINUE/DEPLOY) are the only filled
        // Pal.Friend primaries; LAST STAND keeps the sole Foe-red plate (danger mode); the four
        // utility modes sit in a neutral-outline 2x2 grid of EQUAL width. One shared CAPTION SLOT
        // (the old LAST STAND blurb line) explains whichever button the mouse is over.
        var introMouse = Raylib.GetMousePosition();
        float btnIn = PanelAnim("introBtns", 0.3f, 0.55f);
        int by = ry0 + rules.Length * rowH + 28;
        by += (int)((1f - Util.EaseOutQuad(btnIn)) * 14f);
        string btn = "DEPLOY SQUAD";
        string secondBtn = SaveGame.Exists ? "CONTINUE RUN" : null;
        if (secondBtn != null)
        {
            int bw = 220, gap = 22;
            OverlayBtn2 = new Rectangle(W / 2 - bw - gap / 2, by, bw, 50);
            OverlayBtn  = new Rectangle(W / 2 + gap / 2, by, bw, 50);
            DrawOverlayButton(OverlayBtn2, secondBtn, Pal.Friend, "C", btnIn);
            DrawOverlayButton(OverlayBtn, btn, Pal.Friend, null, btnIn);
        }
        else
        {
            OverlayBtn = new Rectangle(W / 2 - 130, by, 260, 50);
            OverlayBtn2 = new Rectangle(0, 0, 0, 0);
            DrawOverlayButton(OverlayBtn, btn, Pal.Friend, null, btnIn);
        }

        // PROGRAM HORIZON W2: LAST STAND (endless horde survival) — the danger mode keeps the
        // screen's ONLY red plate, with a persisted BEST WAVE label so the run has a target to beat.
        float lsIn = PanelAnim("introLastStand", 0.3f, 0.62f);
        int lsBy = by + 62;
        OverlayBtn3 = new Rectangle(W / 2 - 130, lsBy, 260, 44);
        DrawOverlayButton(OverlayBtn3, "LAST STAND", Pal.Foe, "L", lsIn);

        // 2x2 utility grid: WAR ROOM / FIELD MANUAL / SKIRMISH / DAILY — same width, same
        // neutral outline (no semantic fill: none of these is a danger or a primary verb).
        float wrIn = PanelAnim("introWarRoom", 0.3f, 0.68f);
        float cxIn = PanelAnim("introCodex", 0.3f, 0.72f);
        float smIn = PanelAnim("introSkirmish", 0.3f, 0.76f);
        int miniW = 172, miniGap = 12;
        int wrBy = lsBy + 74;
        OverlayBtn4 = new Rectangle(W / 2 - miniW - miniGap / 2, wrBy, miniW, 40);
        DrawGhostButton(OverlayBtn4, "WAR ROOM", "W", wrIn);
        OverlayBtn5 = new Rectangle(W / 2 + miniGap / 2, wrBy, miniW, 40);
        DrawGhostButton(OverlayBtn5, "FIELD MANUAL", "K", cxIn);
        int smBy = wrBy + 48;
        OverlayBtn6 = new Rectangle(W / 2 - miniW - miniGap / 2, smBy, miniW, 40);
        DrawGhostButton(OverlayBtn6, "SKIRMISH", "S", smIn);
        OverlayBtn7 = new Rectangle(W / 2 + miniGap / 2, smBy, miniW, 40);
        DrawGhostButton(OverlayBtn7, "DAILY", "Y", smIn);

        // ---- shared caption slot (between LAST STAND and the grid) ----
        // Hovering ANY mode button explains it here; at rest it carries LAST STAND's best-wave
        // target (the line's historical job), so the slot is never empty chrome.
        int bestWave = g.EndlessBestWave;
        string caption; Color capCol = Pal.TxtDim;
        if (Raylib.CheckCollisionPointRec(introMouse, OverlayBtn))
            caption = "NEW CAMPAIGN - draft a squad, pick a doctrine, survive 6 operations";
        else if (secondBtn != null && Raylib.CheckCollisionPointRec(introMouse, OverlayBtn2))
            caption = "CONTINUE - resume your saved campaign run";
        else if (Raylib.CheckCollisionPointRec(introMouse, OverlayBtn3))
        { caption = "LAST STAND - endless horde survival; how many waves can you hold?"; capCol = Pal.Foe; }
        else if (Raylib.CheckCollisionPointRec(introMouse, OverlayBtn4))
            caption = "WAR ROOM - spend salvage on unlocks; achievements + hall of fame";
        else if (Raylib.CheckCollisionPointRec(introMouse, OverlayBtn5))
            caption = "FIELD MANUAL - every enemy, class and rule in one reference";
        else if (Raylib.CheckCollisionPointRec(introMouse, OverlayBtn6))
            caption = "SKIRMISH - one custom fight; pick the objective and the heat";
        else if (Raylib.CheckCollisionPointRec(introMouse, OverlayBtn7))
            caption = "DAILY - today's seeded run, one attempt, ranked by turns";
        else
        {
            caption = bestWave > 0 ? $"LAST STAND BEST: {bestWave} WAVE{(bestWave == 1 ? "" : "S")}" : "ENDLESS HORDE SURVIVAL";
            if (bestWave > 0) capCol = Pal.Foe;
        }
        Vector2 bwm = Raylib.MeasureTextEx(Cfg.Font, caption, 12, 1f);
        Raylib.DrawTextEx(Cfg.Font, caption, new Vector2((int)(W / 2f - bwm.X / 2f), lsBy + 50), 12, 1f,
            Raylib.Fade(capCol, 0.9f * Util.EaseOutQuad(Util.Clamp(lsIn, 0f, 1f))));

        // a faint version/footer stamp
        Raylib.DrawTextEx(Cfg.Font, "GEOMETRY · PARTICLES · NO QUARTER", new Vector2(W / 2f - 150, H - 30), 11, 1f, Raylib.Fade(Pal.TxtDim, 0.6f));
    }

    /// A reusable animated geometric backdrop: an opaque graded fill, a slow-drifting
    /// perspective-ish grid, a horizontal scanning sightline, drifting reticle rings,
    /// and a few primitive "tracer" streaks. Used by the intro + the win/lose screens
    /// (tinted by `accent`). `warp` (0..1) bends the grid toward the horizon for variety.
    static void DrawTacticalBackdrop(float t, Color accent, float warp)
    {
        int W = Cfg.ScreenW, H = Cfg.ScreenH;

        // 1) Opaque vertical graded base so the live board/HUD underneath is hidden.
        Raylib.DrawRectangleGradientV(0, 0, W, H, Pal.RGBA(8, 11, 16), Pal.RGBA(5, 7, 11));
        // a soft radial-ish vignette via two corner darkenings
        Raylib.DrawRectangleGradientV(0, H - 220, W, 220, Pal.RGBA(5, 7, 11, 0), Pal.RGBA(2, 3, 5, 180));

        // 2) Drifting grid. Vertical + horizontal lines scroll slowly; a subtle parallax
        // brightness ripple sweeps across so it reads as alive, not static.
        int cell = 56;
        float driftX = (t * 9f) % cell;
        float driftY = (t * 6f) % cell;
        Color gl = Raylib.Fade(accent, 0.05f);
        for (float x = -driftX; x < W; x += cell)
        {
            // brightness ripple based on horizontal position + time
            float rip = 0.5f + 0.5f * MathF.Sin((x / W) * 6.28318f + t * 0.8f);
            Raylib.DrawLine((int)x, 0, (int)x, H, Raylib.Fade(accent, 0.035f + 0.035f * rip));
        }
        for (float y = -driftY; y < H; y += cell)
            Raylib.DrawLine(0, (int)y, W, (int)y, gl);

        // 3) Scanning sightline: a bright horizontal beam sweeping top->bottom on a slow
        // loop, with a soft falloff above/below and a moving reticle node riding it.
        float sweep = (t * 0.07f) % 1f;
        int sy = (int)(sweep * H);
        Raylib.DrawRectangle(0, sy - 1, W, 2, Raylib.Fade(accent, 0.22f));
        Raylib.DrawRectangleGradientV(0, sy - 60, W, 60, Raylib.Fade(accent, 0f), Raylib.Fade(accent, 0.06f));
        Raylib.DrawRectangleGradientV(0, sy, W, 60, Raylib.Fade(accent, 0.06f), Raylib.Fade(accent, 0f));
        // reticle node sweeping horizontally along the beam (independent phase)
        float nodeX = (0.5f + 0.5f * MathF.Sin(t * 0.5f)) * W;
        Raylib.DrawCircleLines((int)nodeX, sy, 9f, Raylib.Fade(accent, 0.5f));
        Raylib.DrawLine((int)nodeX - 16, sy, (int)nodeX - 11, sy, Raylib.Fade(accent, 0.6f));
        Raylib.DrawLine((int)nodeX + 11, sy, (int)nodeX + 16, sy, Raylib.Fade(accent, 0.6f));

        // 4) Parallax reticle motifs: a few slowly-rotating concentric rings drifting in
        // the background (deterministic from fixed seeds + time — no RNG).
        DrawDriftRing(W * 0.16f, H * 0.30f, 70f, t * 0.20f, accent, 0.10f);
        DrawDriftRing(W * 0.84f, H * 0.66f, 96f, -t * 0.14f, accent, 0.09f);
        DrawDriftRing(W * 0.70f, H * 0.20f, 48f, t * 0.30f, accent, 0.08f);

        // 5) Drifting particle dust (primitive points on deterministic sinusoidal paths).
        for (int i = 0; i < 26; i++)
        {
            float px = (MathF.Sin(i * 12.9898f) * 0.5f + 0.5f) * W;
            float baseY = (MathF.Sin(i * 78.233f) * 0.5f + 0.5f) * H;
            float py = (baseY + t * (8f + (i % 5) * 4f)) % H;     // slow downward drift
            float tw = 0.3f + 0.7f * (0.5f + 0.5f * MathF.Sin(t * 1.7f + i));   // twinkle
            float r = 1f + (i % 3) * 0.6f;
            Raylib.DrawCircleV(new Vector2(px, py), r, Raylib.Fade(accent, 0.10f * tw));
        }

        // 6) A couple of long diagonal "tracer" streaks crossing the field on a loop.
        for (int s = 0; s < 2; s++)
        {
            float phase = (t * 0.13f + s * 0.5f) % 1f;
            float sxp = phase * (W + 400) - 200;
            float syp = H * (0.2f + 0.5f * s) + MathF.Sin(t * 0.6f + s) * 30f;
            Raylib.DrawLineEx(new Vector2(sxp, syp), new Vector2(sxp + 120, syp + 26), 1.5f, Raylib.Fade(accent, 0.10f));
            Raylib.DrawCircleV(new Vector2(sxp + 120, syp + 26), 2f, Raylib.Fade(accent, 0.18f));
        }
    }

    /// A slowly-rotating concentric-ring reticle motif at (cx,cy), used as parallax decor.
    static void DrawDriftRing(float cx, float cy, float rad, float rot, Color c, float alpha)
    {
        Raylib.DrawCircleLines((int)cx, (int)cy, rad, Raylib.Fade(c, alpha));
        Raylib.DrawCircleLines((int)cx, (int)cy, rad * 0.6f, Raylib.Fade(c, alpha * 0.8f));
        // 4 rotating tick marks
        for (int i = 0; i < 4; i++)
        {
            float a = rot + i * MathF.PI / 2f;
            float c0 = MathF.Cos(a), s0 = MathF.Sin(a);
            Raylib.DrawLineEx(new Vector2(cx + c0 * rad, cy + s0 * rad),
                              new Vector2(cx + c0 * (rad + 12), cy + s0 * (rad + 12)), 1.4f, Raylib.Fade(c, alpha * 1.4f));
        }
    }

    /// Draw four L-shaped corner brackets just outside `r` (a target-frame motif).
    static void DrawCornerBrackets(Rectangle r, Color c, float len)
    {
        float x0 = r.X, y0 = r.Y, x1 = r.X + r.Width, y1 = r.Y + r.Height;
        // TL
        Raylib.DrawLineEx(new Vector2(x0, y0), new Vector2(x0 + len, y0), 2f, c);
        Raylib.DrawLineEx(new Vector2(x0, y0), new Vector2(x0, y0 + len), 2f, c);
        // TR
        Raylib.DrawLineEx(new Vector2(x1, y0), new Vector2(x1 - len, y0), 2f, c);
        Raylib.DrawLineEx(new Vector2(x1, y0), new Vector2(x1, y0 + len), 2f, c);
        // BL
        Raylib.DrawLineEx(new Vector2(x0, y1), new Vector2(x0 + len, y1), 2f, c);
        Raylib.DrawLineEx(new Vector2(x0, y1), new Vector2(x0, y1 - len), 2f, c);
        // BR
        Raylib.DrawLineEx(new Vector2(x1, y1), new Vector2(x1 - len, y1), 2f, c);
        Raylib.DrawLineEx(new Vector2(x1, y1), new Vector2(x1, y1 - len), 2f, c);
    }

    // ============================================================================
    //  VICTORY / DEFEAT  — a cinematic end screen with an animated reveal, a held
    //  emphatic title, counting-up stats, and a colour-graded geometric backdrop.
    // ============================================================================
    static void DrawEndScreen(Game g, bool win)
    {
        float t = (float)Raylib.GetTime();
        Color accent = win ? Pal.Good : Pal.Foe;
        DrawTacticalBackdrop(t, accent, 0f);
        // an extra colour wash to grade the whole frame toward win-green / lose-red
        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(accent, win ? 0.06f : 0.08f));

        int W = Cfg.ScreenW;
        var run = g.RunState;

        // ---- in-card victory confetti (win only) — a tasteful drifting fountain that reads
        //      as celebration behind the title; deterministic per index (no state), low alpha.
        if (win) DrawCardConfetti(t);

        // ---- big held title ----
        float titleIn = PanelAnim("endTitle", 0.6f);
        string title = win ? "VICTORY"
                           : (string.IsNullOrEmpty(g.LoseTitle) ? "RUN OVER" : g.LoseTitle);
        int tfs = 92;
        Vector2 tm = Raylib.MeasureTextEx(Cfg.Font, title, tfs, 4f);
        float tx = W / 2f - tm.X / 2f;
        float ty = 64f - (1f - Util.EaseOutBack(Util.Clamp(titleIn, 0f, 1f))) * 30f;
        // emphatic glow that pulses (a held, "earned" feel)
        float pulse = 0.5f + 0.5f * MathF.Sin(t * 2.2f);
        for (int i = 1; i <= 4; i++)
            Raylib.DrawTextEx(Cfg.Font, title, new Vector2(tx, ty - i), tfs, 4f, Raylib.Fade(accent, (0.10f + 0.05f * pulse) * titleIn));
        Raylib.DrawTextEx(Cfg.Font, title, new Vector2(tx, ty), tfs, 4f, Raylib.Fade(win ? Pal.Txt : Pal.Foe, titleIn));
        DrawCornerBrackets(new Rectangle(tx - 24, ty + 8, tm.X + 48, tfs - 10), Raylib.Fade(accent, 0.5f * titleIn), 20f);

        // ---- subtitle / reason ----
        float subIn = PanelAnim("endSub", 0.4f, 0.2f);
        int mission = run?.Mission ?? 1;
        string sub;
        if (g.Mode == GameMode.Endless)
        {
            // PROGRAM HORIZON W2: LAST STAND end card — waves survived + the persisted best.
            int best = g.EndlessBestWave;
            sub = $"SURVIVED {g.Wave} WAVE{(g.Wave == 1 ? "" : "S")}"
                + (best > 0 ? $"   ·   BEST {best}" : "");
        }
        else if (g.Mode == GameMode.Skirmish)
        {
            // PROGRAM HORIZON W4: SKIRMISH/DAILY end card — the single-mission result + (daily) the best.
            string label = g.DailyMode ? $"DAILY {g.DailyStamp}" : "SKIRMISH";
            if (win)
            {
                sub = $"{label} — {Game.SkirmishObjectiveLabel(g.Objective)} cleared in {g.Turn} turn{(g.Turn == 1 ? "" : "s")}";
                if (g.DailyMode && g.DailyBest > 0) sub += $"   ·   BEST {g.DailyBest}";
            }
            else sub = string.IsNullOrEmpty(g.LoseReason) ? $"{label} failed." : $"{label} — {g.LoseReason}";
        }
        else sub = win
            ? $"All {Run.MaxMissions} missions cleared. The squad stands victorious."
            : (string.IsNullOrEmpty(g.LoseReason) ? $"The squad fell on mission {mission}." : g.LoseReason);
        Vector2 sm = Raylib.MeasureTextEx(Cfg.Font, sub, 16, 1f);
        Raylib.DrawTextEx(Cfg.Font, sub, new Vector2(W / 2f - sm.X / 2f, ty + tfs + 2), 16, 1f, Raylib.Fade(Pal.TxtDim, subIn));

        // W11 HONEST LOSSES — name the run-killer: the archetype with the most soldier kills this
        // run (Game.DeathsByClass, bumped in KillUnit) plus the bestiary's counterplay clause, so a
        // loss teaches the counter instead of just stinging. Silent when nothing died to a known
        // archetype ("?" buckets DoT/environment/friendly). The blocks below shift down to make room.
        int causeShift = 0;
        if (!win)
        {
            string topCls = null; int topN = 0;
            foreach (var kv in g.DeathsByClass)
                if (kv.Key != "?" && (kv.Value > topN ||
                    (kv.Value == topN && topCls != null && string.CompareOrdinal(kv.Key, topCls) < 0)))
                { topCls = kv.Key; topN = kv.Value; }
            if (topCls != null && topN > 0)
            {
                string head = $"CAUSE OF DEATH: {Codex.NameFor(topCls)} ({topCls}) x{topN}";
                string tip = Codex.TipFor(topCls);
                string tail = string.IsNullOrEmpty(tip) ? "" : $" — {tip}";
                float hw2 = Raylib.MeasureTextEx(Cfg.Font, head, 14, 1f).X;
                float tw2 = tail.Length > 0 ? Raylib.MeasureTextEx(Cfg.Font, tail, 14, 1f).X : 0;
                float cx0 = W / 2f - (hw2 + tw2) / 2f;
                float cy0 = ty + tfs + 24;
                Raylib.DrawTextEx(Cfg.Font, head, new Vector2((int)cx0, (int)cy0), 14, 1f, Raylib.Fade(Pal.Foe, subIn));
                if (tail.Length > 0)
                    Raylib.DrawTextEx(Cfg.Font, tail, new Vector2((int)(cx0 + hw2), (int)cy0), 14, 1f, Raylib.Fade(Pal.Txt, 0.85f * subIn));
                causeShift = 24;
            }
        }

        // FUL-12 SIGNPOSTS — the meta payoff, read from FIELDS (AwardMetaRunEnd/UnlockHeatOnWin),
        // never re-parsed out of Report strings. All-zero under the harness/autoplay (NoPersist
        // keeps the award path dark), so plain end-card shots are unchanged. HEAT UNLOCKED leads
        // (the ladder opening is the headline), then one gold line per fresh achievement.
        {
            float metaY = ty + tfs + 24 + causeShift;
            if (g.EndHeatUnlocked > 0)
            {
                string hl = $"HEAT {g.EndHeatUnlocked} UNLOCKED — a harder ladder rung is open";
                float hw3 = Raylib.MeasureTextEx(Cfg.Font, hl, 15, 1f).X;
                Raylib.DrawTextEx(Cfg.Font, hl, new Vector2((int)(W / 2f - hw3 / 2f), (int)metaY), 15, 1f, Raylib.Fade(Pal.Foe, subIn));
                metaY += 21; causeShift += 21;
            }
            int shownAch = 0;
            foreach (var name in g.EndAchievements)
            {
                if (shownAch++ >= 3) break;   // cap: a monster run-end can't push the dossier off-screen
                string al = $"ACHIEVEMENT — {name}  (+{MetaProg.AchievementSalvage} SALVAGE)";
                float aw2 = Raylib.MeasureTextEx(Cfg.Font, al, 13, 1f).X;
                Raylib.DrawTextEx(Cfg.Font, al, new Vector2((int)(W / 2f - aw2 / 2f), (int)metaY), 13, 1f, Raylib.Fade(Pal.VipGold, subIn));
                metaY += 19; causeShift += 19;
            }
        }

        // ---- counting-up stat slabs (missions / intel / kills / heat) ----
        int totalKills = 0;
        if (run?.Squad != null) foreach (var u in run.Squad) totalKills += u.Kills;
        if (run?.Memorial != null) foreach (var f in run.Memorial) totalKills += f.Kills;   // count the fallen's lifetime kills too
        int missionsShown = win ? Run.MaxMissions : Math.Max(0, mission - 1);

        var stats = new System.Collections.Generic.List<(string label, int value, Color col)>
        {
            ("MISSIONS CLEARED", missionsShown, accent),
            ("INTEL BANKED",     run?.Intel ?? 0, Pal.Accent),
            ("CONFIRMED KILLS",  totalKills, Pal.Friend),
        };
        stats.Add(("HEAT / ASCENSION", run?.HeatLevel ?? 0, (run?.HeatLevel ?? 0) > 0 ? Pal.Foe : Pal.TxtDim));
        // FUL-12: the SALVAGE bounty gets a real slab (gold — it's the persistent currency). Only
        // when the meta path actually banked some, so harness/autoplay cards keep their 4-slab row.
        if (g.EndSalvage > 0) stats.Add(("SALVAGE BANKED", g.EndSalvage, Pal.VipGold));

        float statsIn = PanelAnim("endStats", 0.3f, 0.45f);
        int n = stats.Count;
        int slabW = 200, gap = 16;
        int totalW = n * slabW + (n - 1) * gap;
        int sx0 = W / 2 - totalW / 2;
        int sy = (int)(ty + tfs + 36) + causeShift;   // W11: everything below yields to the cause line
        // count-up factor: ramps 0->1 over ~0.9s after the slabs appear
        float countF = Util.EaseOutQuad(PanelAnim("endCount", 0.9f, 0.5f));
        for (int i = 0; i < n; i++)
        {
            float in_ = Util.Clamp((statsIn - i * 0.10f) / 0.6f, 0f, 1f);
            if (in_ <= 0f) continue;
            var slab = new Rectangle(sx0 + i * (slabW + gap), sy + (int)((1f - Util.EaseOutQuad(in_)) * 16f), slabW, 82);
            float a = Util.EaseOutQuad(in_);
            Raylib.DrawRectangleRounded(slab, 0.10f, 8, Raylib.Fade(Pal.Panel, 0.92f * a));
            Raylib.DrawRectangleLinesEx(slab, 1.4f, Raylib.Fade(stats[i].col, 0.55f * a));
            Raylib.DrawRectangle((int)slab.X, (int)slab.Y, 3, (int)slab.Height, Raylib.Fade(stats[i].col, a));
            // big counted number
            int shownVal = (int)MathF.Round(stats[i].value * countF);
            string num = shownVal.ToString();
            Vector2 nmz = Raylib.MeasureTextEx(Cfg.Font, num, 42, 1f);
            Raylib.DrawTextEx(Cfg.Font, num, new Vector2(slab.X + slab.Width / 2 - nmz.X / 2, slab.Y + 14), 42, 1f, Raylib.Fade(stats[i].col, a));
            Vector2 lz = Raylib.MeasureTextEx(Cfg.Font, stats[i].label, 11, 1f);
            Raylib.DrawTextEx(Cfg.Font, stats[i].label, new Vector2(slab.X + slab.Width / 2 - lz.X / 2, slab.Y + 62), 11, 1f, Raylib.Fade(Pal.TxtDim, a));
        }

        // ---- two-column dossier: SURVIVING SQUAD (+ MVP) | KIA MEMORIAL ------------------
        int dy = sy + 100;
        int colGap = 28;
        int colW = (totalW - colGap) / 2;
        int lx = sx0, rx = sx0 + colW + colGap;
        int dh = 254;
        float rosterIn = PanelAnim("endRoster", 0.45f, 0.55f);
        float kiaIn = PanelAnim("endKia", 0.45f, 0.7f);

        // identify the MVP (top kills among the surviving squad) so it can be highlighted.
        Unit mvp = null;
        if (run?.Squad != null)
            foreach (var u in run.Squad) if (u != null && !u.IsVip && (mvp == null || u.Kills > mvp.Kills)) mvp = u;

        DrawSurvivorPanel(g, run, mvp, lx, dy, colW, dh, rosterIn);
        DrawMemorialPanel(run, rx, dy, colW, dh, kiaIn);

        // ---- NEW RUN / MAIN MENU buttons (the intro's two-button pattern) ----
        // W1 mode-seam: the end card is no longer a one-way door — MAIN MENU returns to the intro
        // without founding a run, so a finished LAST STAND / SKIRMISH / DAILY can't strong-arm the
        // player into overwriting a live campaign. NEW RUN says so when it WILL overwrite one; the
        // disk read is NoPersist-gated so headless shots stay byte-stable (harness never touches disk).
        float btnIn = PanelAnim("endBtn", 0.3f, 0.85f);
        int by = dy + dh + 16;
        string newRun = !g.NoPersist && SaveGame.Exists ? "NEW RUN (overwrites save)" : "NEW RUN";
        int bgap = 22;
        int bw1 = Math.Max(200, (int)Raylib.MeasureTextEx(Cfg.Font, newRun, 18, 1f).X + 36);
        int bw2 = 200;
        OverlayBtn  = new Rectangle(W / 2 - (bw1 + bgap + bw2) / 2, by, bw1, 46);
        OverlayBtn2 = new Rectangle(OverlayBtn.X + bw1 + bgap, by, bw2, 46);
        // W12 hierarchy: the forward verb keeps the filled Pal.Friend primary plate (matching the
        // intro's CONTINUE/DEPLOY); MAIN MENU drops to the neutral-outline ghost.
        DrawOverlayButton(OverlayBtn, newRun, Pal.Friend, null, btnIn);
        DrawGhostButton(OverlayBtn2, "MAIN MENU", "Esc", btnIn);
    }

    // ============================================================================
    //  SKIRMISH SETUP  — pick ONE fight's OBJECTIVE + HEAT, then START (PROGRAM HORIZON W4).
    //  A compact centred panel over the tactical backdrop: an objective cycler (◀ label ▶),
    //  a heat dial (- N +), and START / BACK. Rects are published for Game.HandleSkirmishSetup.
    // ============================================================================
    static void DrawSkirmishSetup(Game g)
    {
        float t = (float)Raylib.GetTime();
        DrawTacticalBackdrop(t, Pal.Friend, 0.15f);
        int W = Cfg.ScreenW;

        // title
        float titleIn = PanelAnim("skTitle", 0.5f);
        string title = "SKIRMISH";
        int tfs = 64;
        Vector2 tm = Raylib.MeasureTextEx(Cfg.Font, title, tfs, 4f);
        float tx = W / 2f - tm.X / 2f;
        float ty = 120f - (1f - Util.EaseOutBack(Util.Clamp(titleIn, 0f, 1f))) * 22f;
        for (int i = 1; i <= 3; i++)
            Raylib.DrawTextEx(Cfg.Font, title, new Vector2(tx, ty - i), tfs, 4f, Raylib.Fade(Pal.Friend, 0.10f * titleIn));
        Raylib.DrawTextEx(Cfg.Font, title, new Vector2(tx, ty), tfs, 4f, Raylib.Fade(Pal.Txt, titleIn));
        DrawCornerBrackets(new Rectangle(tx - 20, ty + 6, tm.X + 40, tfs - 8), Raylib.Fade(Pal.Friend, 0.5f * titleIn), 16f);

        string blurb = "One custom fight — pick the objective and the heat, then deploy.";
        Vector2 bm = Raylib.MeasureTextEx(Cfg.Font, blurb, 15, 1f);
        Raylib.DrawTextEx(Cfg.Font, blurb, new Vector2(W / 2f - bm.X / 2f, ty + tfs + 4), 15, 1f, Raylib.Fade(Pal.TxtDim, titleIn));

        // centred panel
        float pIn = PanelAnim("skPanel", 0.4f, 0.2f);
        int pw = 460, ph = 260;
        int px = W / 2 - pw / 2, py = (int)(ty + tfs + 40);
        var panel = new Rectangle(px, py, pw, ph);
        Raylib.DrawRectangleRounded(panel, 0.06f, 8, Raylib.Fade(Pal.Panel, 0.94f * pIn));
        Raylib.DrawRectangleLinesEx(panel, 1.5f, Raylib.Fade(Pal.Friend, 0.5f * pIn));

        // --- OBJECTIVE cycler ---
        int rowY = py + 34;
        Raylib.DrawTextEx(Cfg.Font, "OBJECTIVE", new Vector2(px + 28, rowY), 13, 1f, Raylib.Fade(Pal.TxtDim, pIn));
        int cyc = rowY + 26, cycH = 44;
        // W12: steppers are utilities, not verbs — neutral ghosts (red stays LAST STAND's).
        SkirmObjPrev = new Rectangle(px + 28, cyc, 44, cycH);
        SkirmObjNext = new Rectangle(px + pw - 28 - 44, cyc, 44, cycH);
        DrawGhostButton(SkirmObjPrev, "<", null, pIn);
        DrawGhostButton(SkirmObjNext, ">", null, pIn);
        var objBox = new Rectangle(px + 84, cyc, pw - 84 * 2, cycH);
        Raylib.DrawRectangleRounded(objBox, 0.16f, 8, Raylib.Fade(Pal.Bg, 0.6f * pIn));
        Raylib.DrawRectangleLinesEx(objBox, 1.2f, Raylib.Fade(Pal.Accent, 0.4f * pIn));
        string objLabel = Game.SkirmishObjectiveLabel(g.SkirmishObjective);
        DrawObjectiveIcon(g.SkirmishObjective, objBox.X + 26, objBox.Y + objBox.Height / 2, Pal.Accent);
        Vector2 om = Raylib.MeasureTextEx(Cfg.Font, objLabel, 22, 1f);
        Raylib.DrawTextEx(Cfg.Font, objLabel, new Vector2(objBox.X + objBox.Width / 2 - om.X / 2 + 12, objBox.Y + objBox.Height / 2 - om.Y / 2), 22, 1f, Raylib.Fade(Pal.Txt, pIn));

        // --- HEAT dial ---
        int hRowY = cyc + cycH + 28;
        Raylib.DrawTextEx(Cfg.Font, "HEAT / ASCENSION", new Vector2(px + 28, hRowY), 13, 1f, Raylib.Fade(Pal.TxtDim, pIn));
        int hy = hRowY + 26, hH = 40;
        SkirmHeatMinus = new Rectangle(px + 28, hy, 44, hH);
        SkirmHeatPlus  = new Rectangle(px + 28 + 44 + 8 + 120, hy, 44, hH);
        DrawGhostButton(SkirmHeatMinus, "-", null, pIn);
        DrawGhostButton(SkirmHeatPlus, "+", null, pIn);
        var heatBox = new Rectangle(px + 28 + 44 + 8, hy, 120, hH);
        Raylib.DrawRectangleRounded(heatBox, 0.2f, 8, Raylib.Fade(Pal.Bg, 0.6f * pIn));
        string heatTxt = g.SkirmishHeat > 0 ? $"HEAT {g.SkirmishHeat}" : "STANDARD";
        Color heatCol = g.SkirmishHeat > 0 ? Pal.Foe : Pal.TxtDim;
        CenterText(heatTxt, heatBox, 18, Raylib.Fade(heatCol, pIn));
        // W12: the unlock hint brightened a step — it is the ladder's call to action, not chrome.
        string cap = g.UnlockedHeat > 0 ? $"unlocked to {g.UnlockedHeat}" : "win at heat to unlock more";
        Raylib.DrawTextEx(Cfg.Font, cap, new Vector2(px + 28 + 44 + 8 + 120 + 44 + 14, hy + 12), 12, 1f,
            Raylib.Fade(g.UnlockedHeat > 0 ? Pal.RGBA(164, 178, 198) : Pal.Accent, pIn));

        // --- START / BACK ---
        // W12 one-button baseline: DEPLOY is the screen's single filled primary (Pal.Friend, the
        // same verb-plate the intro uses); BACK drops to the neutral-outline ghost.
        float btnIn = PanelAnim("skBtns", 0.3f, 0.35f);
        int bY = py + ph - 56;
        SkirmStart = new Rectangle(px + pw / 2 - 8 - 150, bY, 150, 44);
        SkirmBack  = new Rectangle(px + pw / 2 + 8, bY, 130, 44);
        DrawOverlayButton(SkirmStart, "DEPLOY", Pal.Friend, null, btnIn);
        DrawGhostButton(SkirmBack, "BACK", "Esc", btnIn);

        Raylib.DrawTextEx(Cfg.Font, "< > objective   ·   +/- heat   ·   ENTER deploy",
            new Vector2(W / 2f - 170, py + ph + 18), 12, 1f, Raylib.Fade(Pal.TxtDim, 0.6f));
    }

    // ============================================================================
    //  WAR ROOM  — cross-run meta-progression (PROGRAM HORIZON W3). Persistent SALVAGE,
    //  lifetime stats, achievements, a HALL OF FAME, and an additive unlock shop. All
    //  data comes from the cached g.WarRoom snapshot (loaded on entry; no per-frame I/O).
    // ============================================================================
    static void DrawWarRoom(Game g)
    {
        float t = (float)Raylib.GetTime();
        DrawTacticalBackdrop(t, Pal.Accent, 0f);
        var p = g.WarRoom;
        if (p == null) return;

        int W = Cfg.ScreenW;

        // ---- title + SALVAGE readout ----
        float titleIn = PanelAnim("warTitle", 0.5f);
        string title = "WAR ROOM";
        int tfs = 64;
        Vector2 tm = Raylib.MeasureTextEx(Cfg.Font, title, tfs, 4f);
        float tx = W / 2f - tm.X / 2f;
        float ty = 40f - (1f - Util.EaseOutBack(Util.Clamp(titleIn, 0f, 1f))) * 22f;
        for (int i = 1; i <= 3; i++)
            Raylib.DrawTextEx(Cfg.Font, title, new Vector2(tx, ty - i), tfs, 4f, Raylib.Fade(Pal.Accent, 0.10f * titleIn));
        Raylib.DrawTextEx(Cfg.Font, title, new Vector2(tx, ty), tfs, 4f, Raylib.Fade(Pal.Txt, titleIn));
        DrawCornerBrackets(new Rectangle(tx - 20, ty + 6, tm.X + 40, tfs - 8), Raylib.Fade(Pal.Accent, 0.5f * titleIn), 16f);

        // SALVAGE bank + lifetime stat strip, centred under the title
        string salv = $"SALVAGE  {p.Salvage}";
        Vector2 svm = Raylib.MeasureTextEx(Cfg.Font, salv, 26, 1f);
        Raylib.DrawTextEx(Cfg.Font, salv, new Vector2(W / 2f - svm.X / 2f, ty + tfs + 6), 26, 1f, Raylib.Fade(Pal.VipGold, titleIn));
        // W9: the DAILY STREAK joins the lifetime strip (gold while alive so the habit loop reads)
        string life = $"RUNS {p.Runs}   ·   WINS {p.Wins}   ·   BEST MISSION {p.BestMissions}   ·   BEST WAVE {p.BestWave}   ·   VETERANS {p.Veterans}/{SaveGame.MaxVeterans}   ·   DAILY STREAK {p.DailyStreak}";
        Vector2 lfm = Raylib.MeasureTextEx(Cfg.Font, life, 13, 1f);
        Raylib.DrawTextEx(Cfg.Font, life, new Vector2(W / 2f - lfm.X / 2f, ty + tfs + 40), 13, 1f, Raylib.Fade(Pal.TxtDim, titleIn));
        if (p.DailyStreak > 0)
        {
            // re-draw just the streak segment in gold over the dim strip (right-aligned tail)
            string tail = $"DAILY STREAK {p.DailyStreak}";
            Vector2 tlm = Raylib.MeasureTextEx(Cfg.Font, tail, 13, 1f);
            Raylib.DrawTextEx(Cfg.Font, tail, new Vector2(W / 2f - lfm.X / 2f + lfm.X - tlm.X, ty + tfs + 40), 13, 1f, Raylib.Fade(Pal.VipGold, titleIn));
        }

        // ---- three-column layout: ACHIEVEMENTS | HALL OF FAME | UNLOCKS ----
        int top = (int)(ty + tfs + 66);
        int colGap = 24;
        int marginX = 60;
        int colW = (W - marginX * 2 - colGap * 2) / 3;
        int colH = Cfg.ScreenH - top - 92;
        int c0 = marginX, c1 = marginX + colW + colGap, c2 = marginX + (colW + colGap) * 2;

        DrawWarAchievements(p, c0, top, colW, colH, PanelAnim("warAch", 0.4f, 0.15f));
        DrawWarHallOfFame(p, c1, top, colW, colH, PanelAnim("warHof", 0.4f, 0.25f));
        DrawWarUnlocks(g, p, c2, top, colW, colH, PanelAnim("warUnl", 0.4f, 0.35f));

        // ---- BACK button (centred, bottom) ----
        float backIn = PanelAnim("warBack", 0.3f, 0.5f);
        int by = Cfg.ScreenH - 66;
        WarRoomBack = new Rectangle(W / 2 - 120, by, 240, 44);
        DrawOverlayButton(WarRoomBack, "BACK", Pal.Friend, "Esc", backIn);
    }

    // ============================================================================
    //  CODEX / FIELD MANUAL (PROGRAM HORIZON W6) — a browsable read-only reference.
    //  Left: a category tab column (selected highlighted). Right: a scrollable panel
    //  of the current category's entries (title + code chip + wrapped desc; a
    //  DrawCodexGlyph silhouette left of the text for ENEMIES/CLASSES). All content
    //  is assembled by Codex.Build() from the existing Def strings — no new data.
    // ============================================================================
    static void DrawCodex(Game g)
    {
        float t = (float)Raylib.GetTime();
        DrawTacticalBackdrop(t, Pal.Good, 0f);
        CodexTabBtns.Clear();

        var cats = g.CodexCats;
        int W = Cfg.ScreenW;

        // ---- title ----
        float titleIn = PanelAnim("codexTitle", 0.5f);
        string title = "FIELD MANUAL";
        int tfs = 56;
        Vector2 tm = Raylib.MeasureTextEx(Cfg.Font, title, tfs, 4f);
        float tx = W / 2f - tm.X / 2f;
        float ty = 34f - (1f - Util.EaseOutBack(Util.Clamp(titleIn, 0f, 1f))) * 20f;
        for (int i = 1; i <= 3; i++)
            Raylib.DrawTextEx(Cfg.Font, title, new Vector2(tx, ty - i), tfs, 4f, Raylib.Fade(Pal.Good, 0.10f * titleIn));
        Raylib.DrawTextEx(Cfg.Font, title, new Vector2(tx, ty), tfs, 4f, Raylib.Fade(Pal.Txt, titleIn));
        DrawCornerBrackets(new Rectangle(tx - 20, ty + 6, tm.X + 40, tfs - 8), Raylib.Fade(Pal.Good, 0.5f * titleIn), 16f);
        string subtitle = "The whole vocabulary — enemies, classes, and every earned edge.";
        Vector2 sm = Raylib.MeasureTextEx(Cfg.Font, subtitle, 13, 1f);
        Raylib.DrawTextEx(Cfg.Font, subtitle, new Vector2(W / 2f - sm.X / 2f, ty + tfs + 4), 13, 1f, Raylib.Fade(Pal.TxtDim, titleIn));

        if (cats == null || cats.Count == 0)
        {
            CodexBack = new Rectangle(W / 2 - 120, Cfg.ScreenH - 66, 240, 44);
            DrawOverlayButton(CodexBack, "BACK", Pal.Friend, "Esc", 1f);
            return;
        }
        int tab = Math.Clamp(g.CodexTab, 0, cats.Count - 1);

        // ---- layout: tab column (left) + content panel (right) ----
        int top = (int)(ty + tfs + 34);
        int marginX = 60;
        int tabW = 190, colGap = 20;
        int panelX = marginX + tabW + colGap;
        int panelW = W - panelX - marginX;
        int panelH = Cfg.ScreenH - top - 82;

        // tab column
        float tabsIn = PanelAnim("codexTabs", 0.4f, 0.12f);
        int tabH = 34, tabGap = 6;
        int tabTotal = cats.Count * (tabH + tabGap);
        // if the tab list ever grows past the column, shrink the row height to fit (defensive).
        if (tabTotal > panelH) { tabH = Math.Max(22, (panelH - cats.Count * tabGap) / cats.Count); }
        int tyy = top;
        for (int i = 0; i < cats.Count; i++)
        {
            var r = new Rectangle(marginX, tyy, tabW, tabH);
            CodexTabBtns.Add(r);
            bool sel = i == tab;
            bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
            Color bg = sel ? Pal.RGBA(24, 40, 32) : (hover ? Pal.RGBA(20, 30, 40) : Pal.Panel);
            Raylib.DrawRectangleRounded(r, 0.16f, 6, Raylib.Fade(bg, tabsIn));
            Raylib.DrawRectangleLinesEx(r, 1.4f, Raylib.Fade(sel ? Pal.Good : Pal.PanelBd, tabsIn));
            if (sel) Raylib.DrawRectangle((int)r.X, (int)r.Y, 3, (int)r.Height, Raylib.Fade(Pal.Good, tabsIn));
            Color tc = sel ? Pal.Good : (hover ? Pal.Txt : Pal.TxtDim);
            Raylib.DrawTextEx(Cfg.Font, cats[i].Name, new Vector2(r.X + 14, r.Y + tabH / 2 - 7), 14, 1f, Raylib.Fade(tc, tabsIn));
            // entry count chip on the right
            string cnt = cats[i].Entries.Count.ToString();
            Vector2 cw = Raylib.MeasureTextEx(Cfg.Font, cnt, 11, 1f);
            Raylib.DrawTextEx(Cfg.Font, cnt, new Vector2(r.X + tabW - cw.X - 12, r.Y + tabH / 2 - 6), 11, 1f, Raylib.Fade(Pal.TxtDim, tabsIn));
            tyy += tabH + tabGap;
        }

        // content panel
        float panIn = PanelAnim("codexPanel", 0.4f, 0.18f);
        var panel = new Rectangle(panelX, top, panelW, panelH);
        Raylib.DrawRectangleRounded(panel, 0.03f, 8, Raylib.Fade(Pal.Panel, 0.92f * panIn));
        Raylib.DrawRectangleLinesEx(panel, 1.2f, Raylib.Fade(Pal.Good, 0.4f * panIn));

        var cat = cats[tab];
        bool glyph = cat.HasGlyph;
        int pad = 18;
        int contentX = panelX + pad;
        int contentW = panelW - pad * 2;
        int glyphCol = glyph ? 52 : 0;   // width reserved for the silhouette preview
        int textX = contentX + glyphCol;
        int textW = contentW - glyphCol;

        // Scissor the panel so scrolled rows clip cleanly at the panel edges.
        Raylib.BeginScissorMode((int)panel.X + 1, (int)panel.Y + 1, (int)panel.Width - 2, (int)panel.Height - 2);
        float scroll = g.CodexScroll;
        int rowY = top + pad - (int)scroll;
        int drawnBottom = rowY;
        foreach (var en in cat.Entries)
        {
            // measure the wrapped body first so we can size + skip the row.
            var lines = WrapText(en.Desc.Replace("\n", " • "), 13, textW);
            int rowH = 24 + lines.Count * 17 + 12;   // header + body + spacing
            int rowBottom = rowY + rowH;

            // only draw rows that intersect the panel viewport (cheap culling).
            if (rowBottom >= top && rowY <= top + panelH)
            {
                // header: title + optional code chip
                Raylib.DrawTextEx(Cfg.Font, en.Title, new Vector2(textX, rowY), 17, 1f, Pal.Txt);
                if (!string.IsNullOrEmpty(en.Code))
                {
                    int titW = (int)Raylib.MeasureTextEx(Cfg.Font, en.Title, 17, 1f).X;
                    var chip = new Rectangle(textX + titW + 10, rowY + 1, Raylib.MeasureTextEx(Cfg.Font, en.Code, 11, 1f).X + 14, 16);
                    Raylib.DrawRectangleRounded(chip, 0.4f, 6, Raylib.Fade(Pal.Good, 0.18f));
                    Raylib.DrawRectangleLinesEx(chip, 1f, Raylib.Fade(Pal.Good, 0.5f));
                    CenterText(en.Code, chip, 11, Pal.Good);
                }
                // body
                int by2 = rowY + 24;
                foreach (var ln in lines) { Raylib.DrawTextEx(Cfg.Font, ln, new Vector2(textX, by2), 13, 1f, Pal.TxtDim); by2 += 17; }
                // silhouette preview (ENEMIES/CLASSES), centred in the reserved glyph column
                if (glyph && !string.IsNullOrEmpty(en.Glyph))
                {
                    var gp = new Vector2(contentX + glyphCol / 2f - 4f, rowY + rowH / 2f - 6f);
                    // team-tinted: classes friendly-blue, enemies foe-red (ELITE gold).
                    Color gc = cat.Name == "CLASSES" ? Pal.Friend : (en.Glyph == "ELITE" ? Pal.VipGold : Pal.Foe);
                    Renderer.DrawCodexGlyph(en.Glyph, gp, gc, 1.4f, 0f);
                }
                // thin divider under the row
                Raylib.DrawLine(textX, rowBottom - 6, textX + textW, rowBottom - 6, Raylib.Fade(Pal.PanelBd, 0.5f));
            }
            rowY = rowBottom;
            drawnBottom = rowBottom;
        }
        Raylib.EndScissorMode();

        // scroll clamp bound: total content height beyond the viewport (consumed by Game.HandleCodexInput).
        int contentHeight = (drawnBottom + (int)scroll) - (top + pad);
        CodexScrollMax = MathF.Max(0f, contentHeight - (panelH - pad * 2));

        // a subtle scrollbar when the content overflows
        if (CodexScrollMax > 1f)
        {
            float frac = panelH / (float)(contentHeight + pad);
            float barH = MathF.Max(30f, panelH * Util.Clamp(frac, 0.05f, 1f));
            float barT = (scroll / MathF.Max(1f, CodexScrollMax)) * (panelH - barH);
            var bar = new Rectangle(panel.X + panel.Width - 6, panel.Y + 2 + barT, 4, barH);
            Raylib.DrawRectangleRounded(bar, 0.5f, 4, Raylib.Fade(Pal.Good, 0.5f));
        }

        // ---- footer hint + BACK ----
        Raylib.DrawTextEx(Cfg.Font, "Up/Down select  ·  Wheel scroll  ·  [Esc]/[K] back",
            new Vector2(panelX, Cfg.ScreenH - 70), 12, 1f, Raylib.Fade(Pal.TxtDim, 0.85f));
        float backIn = PanelAnim("codexBack", 0.3f, 0.3f);
        CodexBack = new Rectangle(W - marginX - 200, Cfg.ScreenH - 74, 200, 44);
        DrawOverlayButton(CodexBack, "BACK", Pal.Friend, "Esc", backIn);
    }

    static void DrawWarPanel(Rectangle panel, string header, Color accent, float anim)
    {
        Raylib.DrawRectangleRounded(panel, 0.05f, 8, Raylib.Fade(Pal.Panel, 0.90f * anim));
        Raylib.DrawRectangleLinesEx(panel, 1.2f, Raylib.Fade(accent, 0.40f * anim));
        Raylib.DrawRectangle((int)panel.X, (int)panel.Y, 3, (int)panel.Height, Raylib.Fade(accent, anim));
        Raylib.DrawTextEx(Cfg.Font, header, new Vector2(panel.X + 14, panel.Y + 12), 15, 1f, Raylib.Fade(accent, anim));
    }

    /// W12: measurable progress toward an achievement, read purely from the profile snapshot.
    /// Binary feats (FLAWLESS, DAILY_WIN) read 0-or-done; heat feats read the best WON heat that
    /// the (capped) legends list still remembers — a preview, not a ledger.
    static (int cur, int max) AchProgress(Game.WarRoomProfile p, string id)
    {
        int wonHeat = 0;
        if (p.Legends != null)
            foreach (var l in p.Legends)
                if (l.Won && l.Heat > wonHeat) wonHeat = l.Heat;
        return id switch
        {
            "FIRST_WIN" => (Math.Min(p.Wins, 1), 1),
            "HEAT3" => (Math.Min(wonHeat, 3), 3),
            "HEAT6" => (Math.Min(wonHeat, 6), 6),
            "FLAWLESS" => (0, 1),
            "DEEP" => (Math.Min(p.BestMissions, 6), 6),
            "STAND5" => (Math.Min(p.BestWave, 5), 5),
            "STAND10" => (Math.Min(p.BestWave, 10), 10),
            "DAILY_WIN" => (p.DailyStreak > 0 ? 1 : 0, 1),
            "STREAK5" => (Math.Min(p.DailyStreak, 5), 5),
            _ => (0, 1),
        };
    }

    static void DrawWarAchievements(Game.WarRoomProfile p, int x, int y, int w, int h, float anim)
    {
        if (anim <= 0f) return;
        DrawWarPanel(new Rectangle(x, y, w, h), "ACHIEVEMENTS", Pal.Accent, anim);
        int rowY = y + 44;
        foreach (var a in MetaProg.All)
        {
            if (rowY > y + h - 40) break;
            bool got = p.Achievements.Contains(a.Id);
            Color nameCol = got ? Pal.VipGold : Pal.Txt;
            float rowA = anim * (got ? 1f : 0.7f);
            // a small filled/empty marker
            var mk = new Rectangle(x + 14, rowY + 2, 12, 12);
            if (got) Raylib.DrawRectangleRounded(mk, 0.3f, 4, Raylib.Fade(Pal.VipGold, rowA));
            else Raylib.DrawRectangleLinesEx(mk, 1.2f, Raylib.Fade(Pal.TxtDim, rowA));
            Raylib.DrawTextEx(Cfg.Font, a.Name, new Vector2(x + 34, rowY), 14, 1f, Raylib.Fade(nameCol, rowA));
            Raylib.DrawTextEx(Cfg.Font, Clip(a.Desc, 11, w - 34 - 112), new Vector2(x + 34, rowY + 16), 11, 1f, Raylib.Fade(Pal.TxtDim, rowA));
            // W12: a per-achievement PROGRESS BAR (right lane) — earned = full gold; in-progress
            // = amber fill with the cur/max fraction, so "how close am I?" reads at a glance.
            // Review fix: an UNEARNED achievement caps its shown progress at max-1 — a full bar
            // beside an empty checkbox (possible when the profile stat outran a stale/demo award
            // set) would read as a contradiction, and only the award itself may fill the bar.
            var (cur, max) = got ? (1, 1) : AchProgress(p, a.Id);
            if (!got) cur = Math.Min(cur, max - 1);
            string fracTxt = got ? "DONE" : $"{cur}/{max}";
            float ftw = Raylib.MeasureTextEx(Cfg.Font, fracTxt, 11, 1f).X;
            Raylib.DrawTextEx(Cfg.Font, fracTxt, new Vector2((int)(x + w - 12 - ftw), rowY + 2), 11, 1f,
                Raylib.Fade(got ? Pal.VipGold : Pal.TxtDim, rowA));
            var bar = new Rectangle(x + w - 12 - 88, rowY + 18, 88, 7);
            Raylib.DrawRectangleRounded(bar, 0.5f, 4, Raylib.Fade(Pal.RGBA(10, 15, 21), rowA));
            float frac = max > 0 ? cur / (float)max : 0f;
            if (frac > 0f)
                Raylib.DrawRectangleRounded(new Rectangle(bar.X, bar.Y, bar.Width * frac, bar.Height), 0.5f, 4,
                    Raylib.Fade(got ? Pal.VipGold : Pal.Accent, rowA));
            rowY += 42;
        }
    }

    static void DrawWarHallOfFame(Game.WarRoomProfile p, int x, int y, int w, int h, float anim)
    {
        if (anim <= 0f) return;
        DrawWarPanel(new Rectangle(x, y, w, h), "HALL OF FAME", Pal.Friend, anim);
        int rowY = y + 44;
        if (p.Legends == null || p.Legends.Count == 0)
        {
            Raylib.DrawTextEx(Cfg.Font, "- no legends yet -", new Vector2(x + 14, rowY), 13, 1f, Raylib.Fade(Pal.TxtDim, anim));
            Raylib.DrawTextEx(Cfg.Font, "Finish a run to enshrine them.", new Vector2(x + 14, rowY + 18), 11, 1f, Raylib.Fade(Pal.TxtDim, anim));
            return;
        }
        foreach (var l in p.Legends)
        {
            if (rowY > y + h - 30) break;
            Color tag = l.Won ? Pal.VipGold : Pal.TxtDim;
            string status = l.Won ? "WON" : "KIA";
            Raylib.DrawTextEx(Cfg.Font, status, new Vector2(x + 14, rowY + 2), 11, 1f, Raylib.Fade(tag, anim));
            Raylib.DrawTextEx(Cfg.Font, l.Name ?? "", new Vector2(x + 48, rowY), 14, 1f, Raylib.Fade(l.Won ? Pal.Txt : Pal.TxtDim, anim));
            string sub = $"{l.Rank} {l.Cls}  ·  {l.Kills} K  ·  H{l.Heat}";
            Raylib.DrawTextEx(Cfg.Font, sub, new Vector2(x + 48, rowY + 16), 11, 1f, Raylib.Fade(Pal.TxtDim, anim));
            rowY += 34;
        }
    }

    static void DrawWarUnlocks(Game g, Game.WarRoomProfile p, int x, int y, int w, int h, float anim)
    {
        WarRoomBuyBtns.Clear();
        if (anim <= 0f) return;
        DrawWarPanel(new Rectangle(x, y, w, h), "UNLOCKS", Pal.Good, anim);
        int rowY = y + 44;

        // W12: the column leads with a NEXT UNLOCK preview — the cheapest unowned unlock, with a
        // live salvage-progress bar toward its price. The rest of the catalogue (incl. W9's
        // CROSS-TRAINING / QUARTERMASTER / STANDING RESERVE) lists below; OWNED entries collapse
        // to one-line receipts so the column stays a shop, not a ledger.
        MetaUnlock? next = null;
        int nextCost = int.MaxValue;
        foreach (var u in MetaProg.AllUnlocks)
            if (!p.Unlocks.Contains((int)u) && MetaProg.UnlockCost(u) < nextCost)
            { next = u; nextCost = MetaProg.UnlockCost(u); }

        if (next.HasValue)
        {
            var u = next.Value;
            bool afford = p.Salvage >= nextCost;
            var card = new Rectangle(x + 12, rowY, w - 24, 96);
            Raylib.DrawRectangleRounded(card, 0.08f, 6, Raylib.Fade(Pal.RGBA(24, 22, 12), 0.92f * anim));
            Raylib.DrawRectangleLinesEx(card, 1.4f, Raylib.Fade(Pal.VipGold, 0.75f * anim));
            Raylib.DrawTextEx(Cfg.Font, "NEXT UNLOCK", new Vector2(card.X + 12, card.Y + 7), 11, 1f, Raylib.Fade(Pal.VipGold, anim));
            Raylib.DrawTextEx(Cfg.Font, MetaProg.UnlockName(u), new Vector2(card.X + 12, card.Y + 22), 16, 1f, Raylib.Fade(Pal.Txt, anim));
            var descLines = WrapText(MetaProg.UnlockDesc(u), 11, (int)card.Width - 24);
            for (int li = 0; li < descLines.Count && li < 2; li++)
                Raylib.DrawTextEx(Cfg.Font, descLines[li], new Vector2(card.X + 12, card.Y + 44 + li * 13), 11, 1f, Raylib.Fade(Pal.TxtDim, anim));
            // salvage progress toward the price + the BUY chip
            var bar = new Rectangle(card.X + 12, card.Y + 76, card.Width - 116, 8);
            Raylib.DrawRectangleRounded(bar, 0.5f, 4, Raylib.Fade(Pal.RGBA(10, 15, 21), anim));
            float bfrac = Util.Clamp(p.Salvage / (float)Math.Max(1, nextCost), 0f, 1f);
            if (bfrac > 0f)
                Raylib.DrawRectangleRounded(new Rectangle(bar.X, bar.Y, bar.Width * bfrac, bar.Height), 0.5f, 4,
                    Raylib.Fade(afford ? Pal.Good : Pal.VipGold, anim));
            string bank = afford ? "READY" : $"{p.Salvage}/{nextCost}";
            var chip = new Rectangle(card.X + card.Width - 92, card.Y + card.Height - 28, 80, 20);
            bool hover = afford && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), chip);
            Color chipCol = afford ? (hover ? Pal.Good : Pal.RGBA(30, 44, 34)) : Pal.RGBA(26, 22, 16);
            Raylib.DrawRectangleRounded(chip, 0.3f, 6, Raylib.Fade(chipCol, anim));
            Raylib.DrawRectangleLinesEx(chip, 1f, Raylib.Fade(afford ? Pal.Good : Pal.VipGold, 0.6f * anim));
            CenterText(afford ? $"BUY {nextCost}" : bank, chip, 12, Raylib.Fade(afford ? Pal.Txt : Pal.VipGold, anim));
            WarRoomBuyBtns.Add((u, chip));
            rowY += 106;
        }

        // remaining unowned unlocks: compact cards
        foreach (var u in MetaProg.AllUnlocks)
        {
            if (p.Unlocks.Contains((int)u) || (next.HasValue && u == next.Value)) continue;
            if (rowY > y + h - 58) break;
            int cost = MetaProg.UnlockCost(u);
            bool afford = p.Salvage >= cost;
            var card = new Rectangle(x + 12, rowY, w - 24, 62);
            Raylib.DrawRectangleRounded(card, 0.10f, 6, Raylib.Fade(Pal.RGBA(14, 20, 28), 0.9f * anim));
            Raylib.DrawRectangleLinesEx(card, 1f, Raylib.Fade(Pal.PanelBd, 0.6f * anim));
            Raylib.DrawTextEx(Cfg.Font, MetaProg.UnlockName(u), new Vector2(card.X + 12, card.Y + 7), 14, 1f, Raylib.Fade(Pal.Txt, anim));
            var dl = WrapText(MetaProg.UnlockDesc(u), 11, (int)card.Width - 24);
            for (int li = 0; li < dl.Count && li < 2; li++)
                Raylib.DrawTextEx(Cfg.Font, dl[li], new Vector2(card.X + 12, card.Y + 27 + li * 13), 11, 1f, Raylib.Fade(Pal.TxtDim, anim));
            var chip = new Rectangle(card.X + card.Width - 82, card.Y + 5, 70, 18);
            bool hover = afford && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), chip);
            Color chipCol = afford ? (hover ? Pal.Good : Pal.RGBA(30, 44, 34)) : Pal.RGBA(30, 24, 24);
            Raylib.DrawRectangleRounded(chip, 0.3f, 6, Raylib.Fade(chipCol, anim));
            Raylib.DrawRectangleLinesEx(chip, 1f, Raylib.Fade(afford ? Pal.Good : Pal.Foe, 0.6f * anim));
            CenterText($"BUY {cost}", chip, 11, Raylib.Fade(afford ? Pal.Txt : Pal.TxtDim, anim));
            WarRoomBuyBtns.Add((u, chip));   // hit-testable regardless of affordability (Game refuses)
            rowY += 70;
        }

        // owned unlocks: one-line receipts
        foreach (var u in MetaProg.AllUnlocks)
        {
            if (!p.Unlocks.Contains((int)u)) continue;
            if (rowY > y + h - 26) break;
            var mk = new Rectangle(x + 16, rowY + 3, 10, 10);
            Raylib.DrawRectangleRounded(mk, 0.3f, 4, Raylib.Fade(Pal.Good, 0.9f * anim));
            Raylib.DrawTextEx(Cfg.Font, MetaProg.UnlockName(u), new Vector2(x + 34, rowY), 13, 1f, Raylib.Fade(Pal.Txt, 0.8f * anim));
            string own = "OWNED";
            float ow = Raylib.MeasureTextEx(Cfg.Font, own, 11, 1f).X;
            Raylib.DrawTextEx(Cfg.Font, own, new Vector2((int)(x + w - 16 - ow), rowY + 2), 11, 1f, Raylib.Fade(Pal.Good, 0.75f * anim));
            rowY += 24;
        }
    }

    /// Left dossier column: the SURVIVING SQUAD roster (name/nickname, rank, kills, a trait),
    /// with the top-kills soldier flagged MVP. Part of the run-summary payoff card.
    static void DrawSurvivorPanel(Game g, Run run, Unit mvp, int x, int y, int w, int h, float anim)
    {
        if (anim <= 0f) return;
        var panel = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(panel, 0.06f, 8, Raylib.Fade(Pal.Panel, 0.90f * anim));
        Raylib.DrawRectangleLinesEx(panel, 1.2f, Raylib.Fade(Pal.Good, 0.40f * anim));
        Raylib.DrawTextEx(Cfg.Font, "SURVIVING SQUAD", new Vector2(x + 14, y + 12), 15, 1f, Raylib.Fade(Pal.Good, anim));

        var squad = run?.Squad;
        int rowY = y + 40;
        int shown = 0;
        if (squad != null)
        {
            foreach (var u in squad)
            {
                if (u == null || u.IsVip) continue;
                if (rowY > y + h - 26) break;
                bool isMvp = u == mvp && u.Kills > 0;
                float a = anim;
                // FUL-12: the board's class glyph leads the row (same silhouette language as the
                // roster/draft), so WHO came home reads by shape before the name is even parsed.
                Renderer.DrawCodexGlyph(u.Cls, new Vector2(x + 26, rowY + 13), Raylib.Fade(isMvp ? Pal.VipGold : Pal.Friend, a), 0.9f);
                // name + nickname
                string nm = u.FullName;
                Raylib.DrawTextEx(Cfg.Font, nm, new Vector2(x + 44, rowY), 15, 1f, Raylib.Fade(isMvp ? Pal.VipGold : Pal.Txt, a));
                float nmw = Raylib.MeasureTextEx(Cfg.Font, nm, 15, 1f).X;
                if (isMvp)
                    Raylib.DrawTextEx(Cfg.Font, "MVP", new Vector2(x + 44 + nmw + 8, rowY + 2), 12, 1f, Raylib.Fade(Pal.VipGold, a));
                // rank + a trait code on a dim sub-line
                string sub = $"{u.RankName} {u.Cls}";
                if (u.Traits != null && u.Traits.Count > 0) sub += "  " + TraitDef.Name(u.Traits[0]);
                Raylib.DrawTextEx(Cfg.Font, sub, new Vector2(x + 44, rowY + 17), 11, 1f, Raylib.Fade(Pal.TxtDim, a));
                // kills, right-aligned
                string ks = $"{u.Kills} K";
                float kw = Raylib.MeasureTextEx(Cfg.Font, ks, 14, 1f).X;
                Raylib.DrawTextEx(Cfg.Font, ks, new Vector2(x + w - 14 - kw, rowY + 4), 14, 1f, Raylib.Fade(Pal.Friend, a));
                rowY += 34;
                shown++;
            }
        }
        if (shown == 0)
            Raylib.DrawTextEx(Cfg.Font, "- no survivors -", new Vector2(x + 14, rowY), 13, 1f, Raylib.Fade(Pal.TxtDim, anim));
    }

    /// Right dossier column: the KIA MEMORIAL — the soldiers lost across the whole run, with
    /// rank/class + the mission they fell on. Honours attrition; reads from Run.Memorial.
    static void DrawMemorialPanel(Run run, int x, int y, int w, int h, float anim)
    {
        if (anim <= 0f) return;
        var panel = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(panel, 0.06f, 8, Raylib.Fade(Pal.Panel, 0.90f * anim));
        Raylib.DrawRectangleLinesEx(panel, 1.2f, Raylib.Fade(Pal.Foe, 0.40f * anim));
        var mem = run?.Memorial;
        int count = mem?.Count ?? 0;
        Raylib.DrawTextEx(Cfg.Font, $"KIA MEMORIAL  ({count})", new Vector2(x + 14, y + 12), 15, 1f, Raylib.Fade(Pal.Foe, anim));

        int rowY = y + 40;
        if (count == 0)
        {
            Raylib.DrawTextEx(Cfg.Font, "- no losses -", new Vector2(x + 14, rowY), 13, 1f, Raylib.Fade(Pal.Good, 0.85f * anim));
            Raylib.DrawTextEx(Cfg.Font, "The whole squad came home.", new Vector2(x + 14, rowY + 20), 11, 1f, Raylib.Fade(Pal.TxtDim, anim));
            return;
        }
        // Show the most recent fallen first; cap to what fits, with an overflow tally.
        int maxRows = (h - 50) / 34;
        int start = Math.Max(0, count - maxRows);
        for (int i = count - 1; i >= start; i--)
        {
            var f = mem[i];
            // FUL-12: class glyph in memorial red — the fallen keep their silhouette identity.
            Renderer.DrawCodexGlyph(f.Cls, new Vector2(x + 26, rowY + 13), Raylib.Fade(Pal.Foe, 0.75f * anim), 0.9f);
            Raylib.DrawTextEx(Cfg.Font, f.Name, new Vector2(x + 44, rowY), 15, 1f, Raylib.Fade(Pal.Txt, 0.92f * anim));
            string sub = $"{f.Rank} {f.Cls}  -  fell on mission {f.Mission}";
            Raylib.DrawTextEx(Cfg.Font, sub, new Vector2(x + 44, rowY + 17), 11, 1f, Raylib.Fade(Pal.TxtDim, anim));
            rowY += 34;
        }
        if (start > 0)
            Raylib.DrawTextEx(Cfg.Font, $"+ {start} more", new Vector2(x + 14, rowY), 11, 1f, Raylib.Fade(Pal.Foe, 0.8f * anim));
    }

    /// In-card celebratory confetti (win only): a deterministic drifting fountain of cheerful
    /// specks rendered straight from time (no particle state) so it animates without touching
    /// the live Fx system. Tasteful: low count, low alpha, behind the title text.
    static readonly Color[] _endConfetti =
    {
        Pal.Good, Pal.Friend, Pal.VipGold, Pal.Accent, Pal.RGBA(120, 220, 255),
    };
    static void DrawCardConfetti(float t)
    {
        int W = Cfg.ScreenW;
        const int N = 60;
        for (int i = 0; i < N; i++)
        {
            // frozen per-speck constants from the index -> deterministic, no allocation
            float fx = (i * 97 % 100) / 100f;           // 0..1 horizontal slot
            float phase = (i * 53 % 100) / 100f;        // fall phase offset
            float speed = 0.5f + (i * 31 % 100) / 100f * 0.7f;
            float sway = 18f + (i * 17 % 40);
            float prog = (t * speed * 0.18f + phase) % 1f;       // 0 (top) -> 1 (bottom)
            float yy = prog * (Cfg.ScreenH + 40) - 20;
            float xx = fx * W + MathF.Sin(t * 1.3f + i) * sway;
            float fade = 0.16f * (1f - prog);                    // brightest at top, fades down
            var col = _endConfetti[i % _endConfetti.Length];
            float sz = 2.5f + (i % 3);
            Raylib.DrawCircleV(new Vector2(xx, yy), sz, Raylib.Fade(col, fade));
        }
    }

    /// The run-scoped BOON pick (Wave 3): a pick-1-of-3 doctrine card shown in the barracks before
    /// the campaign-map node choice. Boons last the whole run (discarded at run end) and stack, so
    /// every run develops a different character. Cards are clickable (rects cached in BoonBtns).
    static void DrawBoonOffer(Game g, Run run)
    {
        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.RGBA(5, 8, 11), 0.9f));
        int n = run.BoonOffer.Count;
        int cw = 300, gap = 22, ch = 188;
        int totalW = n * cw + (n - 1) * gap;
        int x0 = Cfg.ScreenW / 2 - totalW / 2;
        int y0 = Cfg.ScreenH / 2 - ch / 2 - 10;

        string title = "FIELD DOCTRINE";
        Raylib.DrawTextEx(Cfg.Font, title, new Vector2(Cfg.ScreenW / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, title, 34, 1f).X / 2, y0 - 92), 34, 1f, Pal.VipGold);
        string sub = "CHOOSE A BOON  -  it lasts the whole run";
        Raylib.DrawTextEx(Cfg.Font, sub, new Vector2(Cfg.ScreenW / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, sub, 14, 1f).X / 2, y0 - 54), 14, 1f, Pal.TxtDim);

        var mouse = Raylib.GetMousePosition();
        for (int i = 0; i < n; i++)
        {
            var boon = run.BoonOffer[i];
            var r = new Rectangle(x0 + i * (cw + gap), y0, cw, ch);
            bool hover = Raylib.CheckCollisionPointRec(mouse, r);
            PanelShadow(r, 1f);
            Raylib.DrawRectangleRounded(r, 0.06f, 8, hover ? Pal.RGBA(26, 36, 48) : Pal.Panel);
            Raylib.DrawRectangleLinesEx(r, hover ? 2.5f : 1.5f, hover ? Pal.VipGold : Pal.PanelBd);
            // code chip
            Raylib.DrawTextEx(Cfg.Font, BoonDef.Code(boon), new Vector2((int)r.X + 18, (int)r.Y + 16), 16, 1f, Pal.VipGold);
            // name
            Raylib.DrawTextEx(Cfg.Font, BoonDef.Name(boon), new Vector2((int)r.X + 18, (int)r.Y + 46), 22, 1f, Pal.Txt);
            // description (word-wrapped)
            foreach (var (line, dy) in WrapLines(BoonDef.Desc(boon), cw - 36, 14, 0))
                Raylib.DrawTextEx(Cfg.Font, line, new Vector2((int)r.X + 18, (int)r.Y + 86 + dy), 14, 1f, Pal.TxtDim);
            Raylib.DrawTextEx(Cfg.Font, "[ CHOOSE ]", new Vector2((int)r.X + 18, (int)r.Y + ch - 30), 14, 1f, hover ? Pal.Good : Pal.Accent);
            BoonBtns.Add((boon, r));
        }

        // active boons so far (a small strip beneath)
        if (run.ActiveBoons.Count > 0)
        {
            var codes = new System.Collections.Generic.List<string>();
            foreach (var b in run.ActiveBoons) codes.Add(BoonDef.Code(b));
            string active = "ACTIVE: " + string.Join("  ", codes);
            Raylib.DrawTextEx(Cfg.Font, active, new Vector2(Cfg.ScreenW / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, active, 13, 1f).X / 2, y0 + ch + 24), 13, 1f, Pal.Accent);
        }
    }

    /// Greedy word-wrap helper: returns (line, yOffset) pairs for `text` within `width` px at `size`.
    static System.Collections.Generic.List<(string, int)> WrapLines(string text, int width, int size, int _)
    {
        var lines = new System.Collections.Generic.List<(string, int)>();
        var words = text.Split(' ');
        string cur = "";
        int dy = 0, lh = size + 6;
        foreach (var w in words)
        {
            string test = cur.Length == 0 ? w : cur + " " + w;
            if (Raylib.MeasureTextEx(Cfg.Font, test, size, 1f).X > width && cur.Length > 0)
            { lines.Add((cur, dy)); dy += lh; cur = w; }
            else cur = test;
        }
        if (cur.Length > 0) lines.Add((cur, dy));
        return lines;
    }

    // ============================================================================
    //  W4 — FIELD EVENT screen ("?" beat). Mirrors DrawBoonOffer: scrim + panel + title +
    //  word-wrapped flavor + 2-3 stacked choice buttons (label + outcome preview sub-line).
    //  Illegal choices (unaffordable / roster full) are greyed out + non-clickable. Rects are
    //  cached in EventBtns; Game.HandleEventClick hit-tests them.
    // ============================================================================
    static void DrawEventScreen(Game g)
    {
        for (int i = 0; i < EventBtns.Length; i++) EventBtns[i] = new Rectangle(0, 0, 0, 0);
        var ev = g.ActiveEvent;
        if (ev == null) return;

        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.RGBA(5, 8, 11), 0.9f));

        int n = ev.Choices.Length;
        int w = 620;
        int btnH = 64, btnGap = 14;
        // panel sizes to fit the (wrapped) flavor + the choice stack
        var flavorLines = WrapText(ev.Flavor, 15, w - 64);
        int flavorH = flavorLines.Count * 21;
        int h = 150 + flavorH + n * (btnH + btnGap) + 30;
        int x = Cfg.ScreenW / 2 - w / 2;
        int y = Cfg.ScreenH / 2 - h / 2;
        y -= (int)((1f - Util.EaseOutQuad(PanelAnim("event", 0.15f))) * 16f);
        var card = new Rectangle(x, y, w, h);
        PanelShadow(card, 1f);
        Raylib.DrawRectangleRounded(card, 0.04f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.8f, Pal.Suspect);

        // "?" chip + title
        Raylib.DrawTextEx(Cfg.Font, "?", new Vector2(x + 34, y + 26), 40, 1f, Pal.Suspect);
        Raylib.DrawTextEx(Cfg.Font, ev.Title, new Vector2(x + 72, y + 32), 30, 1f, Pal.Txt);
        string sub = $"FIELD EVENT   |   INTEL {g.RunState.Intel}";
        Raylib.DrawTextEx(Cfg.Font, sub, new Vector2(x + 72, y + 66), 12, 1f, Pal.TxtDim);

        // flavor (word-wrapped)
        int fy = y + 96;
        foreach (var ln in flavorLines)
        {
            Raylib.DrawTextEx(Cfg.Font, ln, new Vector2(x + 32, fy), 15, 1f, Pal.TxtDim);
            fy += 21;
        }

        // choice buttons
        var mouse = Raylib.GetMousePosition();
        int by = fy + 16;
        for (int i = 0; i < n && i < EventBtns.Length; i++)
        {
            var ch = ev.Choices[i];
            bool legal = g.ChoiceLegal(ch);
            var r = new Rectangle(x + 32, by, w - 64, btnH);
            EventBtns[i] = r;
            bool hover = legal && Raylib.CheckCollisionPointRec(mouse, r);
            Color fill = !legal ? Pal.RGBA(18, 22, 28) : (hover ? Pal.RGBA(30, 40, 52) : Pal.RGBA(20, 28, 38));
            Color bd = !legal ? Pal.PanelBd : (hover ? Pal.Suspect : Pal.PanelBd);
            Raylib.DrawRectangleRounded(r, 0.10f, 6, fill);
            Raylib.DrawRectangleLinesEx(r, hover ? 2.2f : 1.4f, bd);
            Color lblCol = !legal ? Pal.TxtDim : (hover ? Pal.Suspect : Pal.Txt);
            Raylib.DrawTextEx(Cfg.Font, ch.Label, new Vector2((int)r.X + 16, (int)r.Y + 12), 18, 1f, lblCol);
            string prev = legal ? ch.Preview : ch.Preview + "   (need more intel / roster full)";
            Raylib.DrawTextEx(Cfg.Font, prev, new Vector2((int)r.X + 16, (int)r.Y + 38), 13, 1f, legal ? Pal.TxtDim : Pal.Foe);
            by += btnH + btnGap;
        }
    }

    // ============================================================================
    //  RUN-OPENING SQUAD DRAFT (Wave 3) — "ASSEMBLE STRIKE TEAM".
    //  Pick DraftCap recruits of 6 (each card shows name/class/weapon/HP-AIM-MOB + a
    //  role one-liner + its signature ability) AND one of 3 starting boons, then DEPLOY.
    //  Perfect-information: every stat + the mission-1 objective + chosen Heat are shown.
    //  All clickable rects cached in DraftCardBtns / DraftBoonBtns / DraftConfirm.
    // ============================================================================
    static void DrawDraft(Game g)
    {
        DraftCardBtns.Clear();
        DraftBoonBtns.Clear();
        DraftContractBtns.Clear();

        float t = (float)Raylib.GetTime();
        DrawTacticalBackdrop(t, Pal.Friend, 0f);
        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.RGBA(6, 9, 13), 0.82f));

        int W = Cfg.ScreenW;
        var mouse = Raylib.GetMousePosition();

        // ---- title ----
        string title = "ASSEMBLE STRIKE TEAM";
        var tm = Raylib.MeasureTextEx(Cfg.Font, title, 40, 2f);
        Raylib.DrawTextEx(Cfg.Font, title, new Vector2(W / 2f - tm.X / 2f, 26), 40, 2f, Pal.Txt);
        // W12 first-run onboarding: when a RECOMMENDED loadout is pre-selected, the subtitle says
        // so — a brand-new player can press DEPLOY immediately, or re-pick anything. Reverts to the
        // standard line once nothing is picked any more (e.g. after a paid pool re-roll).
        bool recActive = g.DraftHasRecommendation && g.DraftPicked.Count > 0;
        string sub = recActive
            ? "A recommended first squad is pre-selected - press DEPLOY, or re-pick anything."
            : $"Pick {Game.DraftCap} operators + a starting doctrine — this is your run's thesis.";
        var sm = Raylib.MeasureTextEx(Cfg.Font, sub, 15, 1f);
        Raylib.DrawTextEx(Cfg.Font, sub, new Vector2(W / 2f - sm.X / 2f, 74), 15, 1f, recActive ? Pal.Good : Pal.TxtDim);

        // selection counter
        int picked = g.DraftPicked.Count;
        string cnt = $"{picked} / {Game.DraftCap} SELECTED";
        Color cntCol = picked == Game.DraftCap ? Pal.Good : Pal.Accent;
        var cm = Raylib.MeasureTextEx(Cfg.Font, cnt, 18, 1f);
        Raylib.DrawTextEx(Cfg.Font, cnt, new Vector2(W / 2f - cm.X / 2f, 98), 18, 1f, cntCol);

        // W9: the SALVAGE bank + the live recall bill, whenever the pool carries a priced veteran.
        // Reads only the cached g.DraftSalvage (loaded at BeginDraft) — never disk, never per frame.
        bool anyVet = false;
        foreach (var u0 in g.DraftPool) if (u0.FromReserve) { anyVet = true; break; }
        if (anyVet)
        {
            int bill = g.DraftRecallCost;
            string bank = bill > 0 ? $"SALVAGE  {g.DraftSalvage}   ·   RECALL BILL  {bill}" : $"SALVAGE  {g.DraftSalvage}";
            var bkm = Raylib.MeasureTextEx(Cfg.Font, bank, 14, 1f);
            Color bankCol = bill > 0 && !g.DraftRecallAffordable ? Pal.Foe : Pal.VipGold;
            Raylib.DrawTextEx(Cfg.Font, bank, new Vector2(W - bkm.X - 40, 100), 14, 1f, bankCol);
        }

        // ---- candidate cards: 6 in two rows of 3 ----
        int cols = 3, cw = 300, chH = 150, gx = 24, gy = 18;
        int gridW = cols * cw + (cols - 1) * gx;
        int x0 = W / 2 - gridW / 2;
        int y0 = 134;
        for (int i = 0; i < g.DraftPool.Count; i++)
        {
            var u = g.DraftPool[i];
            int col = i % cols, row = i / cols;
            var r = new Rectangle(x0 + col * (cw + gx), y0 + row * (chH + gy), cw, chH);
            bool sel = g.DraftPicked.Contains(u);
            bool hover = Raylib.CheckCollisionPointRec(mouse, r);
            // a card is "blocked" (can't add more) only matters visually when not already picked
            bool full = !sel && picked >= Game.DraftCap;

            bool vet = u.FromReserve;   // COUNTERPLAY: a recalled veteran carrying earned progression
            PanelShadow(r, 1f);
            Color body = sel ? Pal.RGBA(20, 38, 30) : (hover && !full ? Pal.RGBA(24, 34, 46) : Pal.Panel);
            if (vet && !sel) body = Pal.Mix(body, Pal.VipGold, 0.10f);   // warm the veteran card
            Raylib.DrawRectangleRounded(r, 0.07f, 8, full ? Raylib.Fade(body, 0.55f) : body);
            Color bd = sel ? Pal.Good : (vet ? Pal.VipGold : (hover && !full ? Pal.Friend : Pal.PanelBd));
            Raylib.DrawRectangleLinesEx(r, sel ? 3f : (vet ? 2f : 1.5f), full ? Raylib.Fade(bd, 0.5f) : bd);

            float a = full ? 0.55f : 1f;
            int px = (int)r.X + 16, py = (int)r.Y + 12;
            // VETERAN ribbon (top-right corner). A small gold diamond marker (drawn, not a font glyph —
            // the baked atlas has no star) + the word, so it reads in any palette.
            // W9: an individually unaffordable veteran greys like a full-team card (still clickable —
            // only CONFIRM refuses, so picks stay rearrangeable toward what the bank can cover).
            int recall = vet ? MetaProg.RecallCost(u.Rank) : 0;
            bool broke = vet && recall > g.DraftSalvage;
            if (broke && !sel) a = Math.Min(a, 0.55f);
            if (vet)
            {
                string vtag = "VETERAN";
                var vm = Raylib.MeasureTextEx(Cfg.Font, vtag, 12, 1f);
                float vx = r.X + cw - vm.X - 12;
                Raylib.DrawTextEx(Cfg.Font, vtag, new Vector2(vx, py + 2), 12, 1f, Raylib.Fade(Pal.VipGold, a));
                float dcy = py + 8;
                Raylib.DrawPoly(new Vector2(vx - 8, dcy), 4, 4f, 45f, Raylib.Fade(Pal.VipGold, a));
                // W9 priced recall: the fee (10+8xRank), right-aligned under the ribbon. Red when the
                // bank can't cover this card alone; charged only at CONFIRM (never at pick time).
                string fee = $"RECALL {recall}";
                var fm = Raylib.MeasureTextEx(Cfg.Font, fee, 12, 1f);
                Raylib.DrawTextEx(Cfg.Font, fee, new Vector2(r.X + cw - fm.X - 12, py + 18), 12, 1f,
                    Raylib.Fade(broke ? Pal.Foe : Pal.VipGold, a));
            }
            // name — nickname shown for veterans who earned one; class label drawn inline ONLY for fresh
            // recruits (a veteran's longer FullName + the corner ribbon would collide; its class goes in
            // the dossier line below instead).
            string nm = vet ? u.FullName : u.Name;
            Raylib.DrawTextEx(Cfg.Font, nm, new Vector2(px, py), 22, 1f, Raylib.Fade(Pal.Txt, a));
            if (!vet)
            {
                int nw = (int)Raylib.MeasureTextEx(Cfg.Font, nm, 22, 1f).X;
                Raylib.DrawTextEx(Cfg.Font, u.Cls, new Vector2(px + nw + 8, py + 5), 13, 1f, Raylib.Fade(Pal.Friend, a));
            }
            // weapon
            string wpn = u.Weapon != null ? u.Weapon.Name : "-";
            Raylib.DrawTextEx(Cfg.Font, wpn, new Vector2(px, py + 30), 13, 1f, Raylib.Fade(Pal.TxtDim, a));
            // stat line
            string stats = $"HP {u.MaxHp}    AIM {u.Aim}    MOB {u.Mobility}";
            Raylib.DrawTextEx(Cfg.Font, stats, new Vector2(px, py + 52), 15, 1f, Raylib.Fade(Pal.Txt, a));
            // role one-liner — for veterans, a dossier of class + earned progression instead of the class blurb
            if (vet)
            {
                string dossier = $"{u.Cls} · {u.RankName} · {u.Kills}k · {u.Perks.Count}P/{u.Traits.Count}T";
                Raylib.DrawTextEx(Cfg.Font, dossier, new Vector2(px, py + 76), 12, 1f, Raylib.Fade(Pal.VipGold, a));
            }
            else
                Raylib.DrawTextEx(Cfg.Font, ClassBlurb(u.Cls), new Vector2(px, py + 76), 12, 1f, Raylib.Fade(Pal.TxtDim, a));
            // signature ability
            Raylib.DrawTextEx(Cfg.Font, "ABILITY: " + u.AbilityName, new Vector2(px, py + 96), 12, 1f, Raylib.Fade(Pal.Accent, a));
            // pick state line
            string tag = sel ? "[ SELECTED ]" : (full ? "TEAM FULL" : "[ SELECT ]");
            Color tagCol = sel ? Pal.Good : (full ? Pal.TxtDim : (hover ? Pal.Friend : Pal.Accent));
            Raylib.DrawTextEx(Cfg.Font, tag, new Vector2(px, (int)r.Y + chH - 22), 13, 1f, Raylib.Fade(tagCol, a));

            // W12: the board's class silhouette anchors the card's bottom-right corner — the same
            // shape the battlefield (and now the barracks/roster) draws, on a faint backing disc.
            var glyP = new Vector2(r.X + cw - 34, r.Y + chH - 36);
            Color glyC = vet ? Pal.VipGold : Pal.Friend;
            Raylib.DrawCircleV(glyP, 22f, Raylib.Fade(glyC, 0.08f * a));
            Renderer.DrawCodexGlyph(u.Cls, glyP, Raylib.Fade(glyC, 0.9f * a), 1.35f);

            // W12 first-run RECOMMENDED badge (fresh recruits only, so it never fights the
            // VETERAN ribbon that owns the same corner on recalled cards).
            if (!vet && g.DraftRecommended.Contains(u))
            {
                string rec = "RECOMMENDED";
                var rm = Raylib.MeasureTextEx(Cfg.Font, rec, 11, 1f);
                var chipR = new Rectangle(r.X + cw - rm.X - 24, py + 2, rm.X + 14, 17);
                Raylib.DrawRectangleRounded(chipR, 0.4f, 6, Raylib.Fade(Pal.Good, 0.16f * a));
                Raylib.DrawRectangleLinesEx(chipR, 1f, Raylib.Fade(Pal.Good, 0.7f * a));
                CenterText(rec, chipR, 11, Raylib.Fade(Pal.Good, a));
            }

            DraftCardBtns.Add((u, r));
        }

        // ---- starting boon (pick 1 of 3) ----
        int boonY = y0 + 2 * (chH + gy) + 10;
        string bh = "STARTING DOCTRINE";
        var bhm = Raylib.MeasureTextEx(Cfg.Font, bh, 18, 1f);
        Raylib.DrawTextEx(Cfg.Font, bh, new Vector2(W / 2f - bhm.X / 2f, boonY - 4), 18, 1f, Pal.VipGold);

        int bn = g.DraftBoonOffer.Count, bcw = 296, bgap = 22, bch = 74;
        int btotal = bn * bcw + (bn - 1) * bgap;
        int bx0 = W / 2 - btotal / 2;
        int by = boonY + 20;
        for (int i = 0; i < bn; i++)
        {
            var boon = g.DraftBoonOffer[i];
            var r = new Rectangle(bx0 + i * (bcw + bgap), by, bcw, bch);
            bool sel = g.DraftSelectedBoon.HasValue && g.DraftSelectedBoon.Value == boon;
            bool hover = Raylib.CheckCollisionPointRec(mouse, r);
            PanelShadow(r, 1f);
            Raylib.DrawRectangleRounded(r, 0.08f, 8, sel ? Pal.RGBA(40, 34, 12) : (hover ? Pal.RGBA(26, 36, 48) : Pal.Panel));
            Raylib.DrawRectangleLinesEx(r, sel ? 3f : 1.5f, sel ? Pal.VipGold : (hover ? Pal.VipGold : Pal.PanelBd));
            Raylib.DrawTextEx(Cfg.Font, BoonDef.Name(boon), new Vector2((int)r.X + 14, (int)r.Y + 10), 18, 1f, sel ? Pal.VipGold : Pal.Txt);
            foreach (var (line, dy) in WrapLines(BoonDef.Desc(boon), bcw - 28, 13, 0))
                Raylib.DrawTextEx(Cfg.Font, line, new Vector2((int)r.X + 14, (int)r.Y + 38 + dy), 13, 1f, Pal.TxtDim);
            // W12 first-run RECOMMENDED badge on the pre-selected safe doctrine (top-right corner).
            if (g.DraftRecommendedBoon.HasValue && g.DraftRecommendedBoon.Value == boon)
            {
                string rec = "RECOMMENDED";
                var rm = Raylib.MeasureTextEx(Cfg.Font, rec, 11, 1f);
                var chipR = new Rectangle(r.X + bcw - rm.X - 24, r.Y + 8, rm.X + 14, 17);
                Raylib.DrawRectangleRounded(chipR, 0.4f, 6, Raylib.Fade(Pal.Good, 0.16f));
                Raylib.DrawRectangleLinesEx(chipR, 1f, Raylib.Fade(Pal.Good, 0.7f));
                CenterText(rec, chipR, 11, Pal.Good);
            }
            DraftBoonBtns.Add((boon, r));
        }

        // ---- run CONTRACT (W6): a compact selector row (STANDARD opt-out + the 3 contracts) ----
        // Mirrors the boon row but more compact. STANDARD (= Contract.None) is the default if nothing
        // is clicked, so the row never blocks the deploy. The selected card highlights.
        int conY = by + bch + 10;
        string ch = "RUN CONTRACT  (optional)";
        var chm = Raylib.MeasureTextEx(Cfg.Font, ch, 16, 1f);
        Raylib.DrawTextEx(Cfg.Font, ch, new Vector2(W / 2f - chm.X / 2f, conY - 2), 16, 1f, Pal.Accent);

        // cards: STANDARD then the 3 contracts (None == STANDARD opt-out)
        var conCards = new Contract[] { Contract.None, Contract.IronVeterans, Contract.HighStakes, Contract.Spearhead };
        int cn = conCards.Length, ccw = 222, cgap = 16, cch = 64;
        int ctotal = cn * ccw + (cn - 1) * cgap;
        int cx0 = W / 2 - ctotal / 2;
        int cy = conY + 22;
        // selected? null DraftSelectedContract means STANDARD (Contract.None) is the effective pick.
        Contract effSel = g.DraftSelectedContract ?? Contract.None;
        for (int i = 0; i < cn; i++)
        {
            var c = conCards[i];
            var r = new Rectangle(cx0 + i * (ccw + cgap), cy, ccw, cch);
            bool sel = effSel == c;
            bool hover = Raylib.CheckCollisionPointRec(mouse, r);
            PanelShadow(r, 1f);
            Raylib.DrawRectangleRounded(r, 0.10f, 8, sel ? Pal.RGBA(14, 34, 44) : (hover ? Pal.RGBA(24, 34, 46) : Pal.Panel));
            Raylib.DrawRectangleLinesEx(r, sel ? 3f : 1.5f, sel ? Pal.Accent : (hover ? Pal.Friend : Pal.PanelBd));
            Raylib.DrawTextEx(Cfg.Font, ContractDef.Name(c), new Vector2((int)r.X + 12, (int)r.Y + 8), 16, 1f, sel ? Pal.Accent : Pal.Txt);
            foreach (var (line, dy) in WrapLines(ContractDef.Desc(c), ccw - 22, 11, 0))
                Raylib.DrawTextEx(Cfg.Font, line, new Vector2((int)r.X + 12, (int)r.Y + 30 + dy), 11, 1f, Pal.TxtDim);
            // STANDARD card is the implicit opt-out: still clickable (deselects back to None), but
            // the input handler only registers the 3 real contracts -> clicking STANDARD is a no-op
            // selection-wise; we add it to the rects anyway so a future tweak can wire it.
            if (c != Contract.None) DraftContractBtns.Add((c, r));
        }

        // ---- mission-1 + heat preview ----
        string m1 = $"FIRST OP: {ObjectiveLabel(Run.ObjectiveFor(1))}   ·   HEAT {g.PendingHeat}";
        var m1m = Raylib.MeasureTextEx(Cfg.Font, m1, 14, 1f);
        int infoY = cy + cch + 10;
        Raylib.DrawTextEx(Cfg.Font, m1, new Vector2(W / 2f - m1m.X / 2f, infoY), 14, 1f, Pal.TxtDim);

        // ---- DEPLOY button (greyed until exactly DraftCap soldiers + a boon are chosen) ----
        // W9: a complete draft whose recall bill exceeds the bank shows the SHORTFALL instead —
        // ConfirmDraft refuses it, so the button is honest about why nothing will happen.
        bool ready = g.DraftReady;
        bool payable = g.DraftRecallAffordable;
        int recallBill = g.DraftRecallCost;
        int dbw = 280, dbh = 46;
        DraftConfirm = new Rectangle(W / 2 - dbw / 2, infoY + 22, dbw, dbh);
        bool dhover = ready && payable && Raylib.CheckCollisionPointRec(mouse, DraftConfirm);
        Color deployCol = ready && payable ? (dhover ? Pal.RGBA(92, 200, 251) : Pal.Good)
                        : (ready ? Pal.RGBA(64, 34, 34) : Pal.RGBA(40, 50, 63));
        if (dhover) Raylib.DrawRectangleRounded(new Rectangle(DraftConfirm.X - 3, DraftConfirm.Y - 3, dbw + 6, dbh + 6), 0.3f, 8, Raylib.Fade(deployCol, 0.25f));
        Raylib.DrawRectangleRounded(DraftConfirm, 0.3f, 8, Raylib.Fade(deployCol, ready && payable ? 1f : 0.5f));
        string dl = ready ? (recallBill > 0 ? $"DEPLOY  (PAY {recallBill} SALVAGE)" : "DEPLOY") : $"SELECT {Game.DraftCap - picked} MORE";
        if (ready && !payable) dl = $"NEED {recallBill - g.DraftSalvage} MORE SALVAGE";
        // when all 4 are picked but no doctrine chosen, the "SELECT 0 MORE" default is wrong -> prompt the doctrine
        if (!ready && picked == Game.DraftCap && !g.DraftSelectedBoon.HasValue) dl = "PICK A DOCTRINE";
        var dlm = Raylib.MeasureTextEx(Cfg.Font, dl, 18, 1f);
        Raylib.DrawTextEx(Cfg.Font, dl, new Vector2((int)(DraftConfirm.X + dbw / 2 - dlm.X / 2), (int)(DraftConfirm.Y + dbh / 2 - 9)), 18, 1f,
            ready && payable ? Pal.RGBA(3, 18, 26) : (ready ? Pal.Foe : Pal.TxtDim));
        if (ready && payable)
            Raylib.DrawTextEx(Cfg.Font, "[ENTER]", new Vector2((int)(DraftConfirm.X + dbw - 56), (int)(DraftConfirm.Y + dbh - 16)), 11, 1f, Pal.RGBA(3, 18, 26));

        // ---- BACK to the intro (W1 mode-seam: the skirmish setup's escape hatch, mirrored) ----
        // W12: neutral-outline ghost — BACK is never a primary verb, so it never gets a filled plate.
        int bkw = 120;
        DraftBack = new Rectangle(DraftConfirm.X - bkw - 14, DraftConfirm.Y, bkw, dbh);
        DrawGhostButton(DraftBack, "BACK", "Esc", 1f);

        // ---- W9: paid pool RE-ROLL (repeatable salvage sink; Game refuses the click when broke) ----
        int rrw = 190;
        DraftReroll = new Rectangle(DraftConfirm.X + dbw + 14, DraftConfirm.Y, rrw, dbh);
        bool rrCan = g.DraftSalvage >= MetaProg.DraftRerollCost;
        bool rrHov = rrCan && Raylib.CheckCollisionPointRec(mouse, DraftReroll);
        Raylib.DrawRectangleRounded(DraftReroll, 0.3f, 8, rrHov ? Pal.RGBA(30, 44, 34) : Pal.RGBA(14, 20, 28));
        Raylib.DrawRectangleLinesEx(DraftReroll, 1.4f, rrCan ? (rrHov ? Pal.VipGold : Pal.PanelBd) : Pal.RGBA(40, 46, 54));
        string rrl = $"RE-ROLL POOL  ({MetaProg.DraftRerollCost} SALV)";
        var rrm = Raylib.MeasureTextEx(Cfg.Font, rrl, 13, 1f);
        Raylib.DrawTextEx(Cfg.Font, rrl, new Vector2((int)(DraftReroll.X + rrw / 2 - rrm.X / 2), (int)(DraftReroll.Y + dbh / 2 - 7)), 13, 1f,
            rrCan ? (rrHov ? Pal.VipGold : Pal.Txt) : Pal.TxtDim);
    }

    /// A short prose role one-liner per class, for the draft candidate cards.
    static string ClassBlurb(string cls) => cls switch
    {
        "ASSAULT" => "Aggressive rifleman; closes and clears.",
        "RANGER" => "Shotgun flanker; brutal up close.",
        "SHARPSHOOTER" => "Long-range sniper; picks off threats.",
        "GUNNER" => "Heavy LMG; suppresses and pins.",
        "CORPSMAN" => "Field medic; in-combat sustain.",
        _ => "Versatile operator.",
    };

    /// Objective enum -> a short readout label (mirrors the top bar), for the draft preview.
    static string ObjectiveLabel(Objective o) => o switch
    {
        Objective.Evac => "EXTRACT",
        Objective.Hack => "HACK TERMINAL",
        Objective.Sabotage => "SABOTAGE",
        Objective.Escort => "ESCORT VIP",
        Objective.Rescue => "RESCUE",
        Objective.Defend => "DEFEND",
        Objective.Decapitate => "KILL HVT",
        _ => "ELIMINATE",
    };

    static void DrawBarracks(Game g)
    {
        var run = g.RunState;
        BenchBtns.Clear();   // clear before the shop/perk early-returns so no stale rects linger
        BoonBtns.Clear();
        RehabBtns.Clear();   // W9: scar buy-off chips are re-published per frame by DrawSquadRow
        if (!g.ShopDone) { DrawRequisition(g); return; }
        if (run.PendingPerks.Count > 0) { DrawPerkChooser(g, run.PendingPerks[0]); return; }
        if (run.PendingSpecs.Count > 0) { DrawSpecChooser(g, run.PendingSpecs[0]); return; }   // W2: fork pick
        if (run.BoonOffer.Count > 0) { DrawBoonOffer(g, run); return; }
        if (g.EventPending) { DrawEventScreen(g); return; }   // W4: a "?" FIELD EVENT takes over the barracks frame
        var squad = run.Squad;
        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.RGBA(5, 8, 11), 0.85f));

        int w = 700;
        int rows = squad.Count;
        const int rowPitch = 44;   // tightened so a full 6-soldier roster + debrief + map fits 800px tall
        // W12 — the campaign map is SIZED TO FIT: the fixed content above it is measured, and the
        // map region gets ALL the panel height left inside the screen (clamped 150..250). A short
        // roster/debrief earns a ~250px strategic map; the full 6-soldier + 5-report + KIA worst
        // case still gets ~170px (>=14px nodes + labels + legend) inside a <=776px panel. The KIA
        // line now RESERVES height too (it used to draw into the map header's slot when present).
        int reportRows = Math.Min(run.Report.Count, 5);
        int kiaRow = run.Fallen.Count > 0 ? 22 : 0;
        bool hasMap = run.Map.Count > 0 && run.NextNodes().Count > 0;
        int contentH = 100 /*title+sub*/ + 22 /*deploy header*/ + rows * rowPitch
                     + 28 /*debrief header*/ + reportRows * 22 + kiaRow;
        const int mapChrome = 24 /*section header*/ + 20 /*legend*/ + 14 /*bottom pad*/;
        int mapH = hasMap ? Math.Clamp(Cfg.ScreenH - 24 - contentH - mapChrome, 150, 250)
                          : 128;   // legacy deploy-card fallback keeps its fixed footprint
        int h = contentH + mapChrome + mapH;
        int x = Cfg.ScreenW / 2 - w / 2;
        int y = Cfg.ScreenH / 2 - h / 2;
        // quick slide-down entrance (≈0.15s, settles well before any click on the map/bench)
        y -= (int)((1f - Util.EaseOutQuad(PanelAnim("barracks", 0.15f))) * 16f);
        var card = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(card, 0.04f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.5f, Pal.PanelBd);

        string title = $"MISSION {run.Mission} COMPLETE";
        Raylib.DrawTextEx(Cfg.Font, title, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, title, 38, 1f).X / 2, y + 26), 38, 1f, Pal.Good);
        string sub = run.HeatLevel > 0
            ? $"BARRACKS - SQUAD DEBRIEF   |   INTEL {run.Intel}   |   HEAT {run.HeatLevel}"
            : $"BARRACKS - SQUAD DEBRIEF   |   INTEL {run.Intel}";
        Raylib.DrawTextEx(Cfg.Font, sub, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, sub, 13, 1f).X / 2, y + 70), 13, 1f, run.HeatLevel > 0 ? Pal.Foe : Pal.TxtDim);
        if (run.HeatLevel > 0)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var mod in Sightline.Heat.Active(run.HeatLevel)) names.Add(mod.Name);
            string modLine = string.Join("  -  ", names);
            Raylib.DrawTextEx(Cfg.Font, modLine, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, modLine, 11, 1f).X / 2, y + 86), 11, 1f, Pal.TxtDim);
        }

        // DEPLOY-PICKER header: how many soldiers field next mission vs the cap (which GROWS
        // as the campaign deepens: 4 -> 5 (m3) -> 6 (m5)). Click a row's pill to deploy/bench.
        int ry = y + 100;
        int deployed = run.Deployed.Count, cap = run.NextDeployCap;
        bool atCap = deployed >= cap;
        string deployHdr = $"DEPLOY  {deployed}/{cap}";
        Color hdrCol = atCap ? Pal.Accent : Pal.Suspect;
        Raylib.DrawTextEx(Cfg.Font, deployHdr, new Vector2(x + 30, ry), 15, 1f, hdrCol);
        string capNote = atCap ? "(squad at capacity - bench a soldier to swap)" : "(cap grows over the campaign - field up to it)";
        Raylib.DrawTextEx(Cfg.Font, capNote, new Vector2(x + 30 + (int)Raylib.MeasureTextEx(Cfg.Font, deployHdr, 15, 1f).X + 12, ry + 2), 11, 1f, Pal.TxtDim);
        ry += 22;
        foreach (var u in squad)
        {
            DrawSquadRow(g, u, x + 30, ry, w - 60);
            ry += rowPitch;
        }

        // promotions / heals report
        ry += 8;
        Raylib.DrawTextEx(Cfg.Font, "DEBRIEF", new Vector2(x + 30, ry), 12, 1f, Pal.Accent);
        ry += 20;
        int shown = 0;
        foreach (var line in run.Report)
        {
            if (shown++ >= 5) break;
            Raylib.DrawTextEx(Cfg.Font, "- " + line, new Vector2(x + 36, ry), 13, 1f, Pal.TxtDim);
            ry += 22;
        }
        if (run.Fallen.Count > 0)
        {
            string kia = "KIA: " + string.Join(", ", run.Fallen);
            Raylib.DrawTextEx(Cfg.Font, kia, new Vector2(x + 36, ry), 13, 1f, Pal.Foe);
        }

        // next operation: pick a node on the branching campaign map (3.3).
        int mapTop = y + contentH;
        if (hasMap)
        {
            string pick = "CAMPAIGN MAP  >  SELECT NEXT OPERATION";
            Raylib.DrawTextEx(Cfg.Font, pick, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, pick, 15, 1f).X / 2, mapTop + 4), 15, 1f, Pal.Accent);
            DrawCampaignMap(run, new Rectangle(x + 24, mapTop + 26, w - 48, mapH));
            // W12: node-kind legend, one quiet centred row under the map (shape + colour redundant,
            // so the map's coding reads without hovering every node — and survives SIGHTLINE_CB=1).
            (string gly, string lbl, Color col)[] legend =
            {
                ("+", "SUPPLY", Pal.Good), ("!", "ELITE", Pal.Elite), ("?", "EVENT", Pal.Suspect), ("X", "BOSS", Pal.Foe),
            };
            float lw = 0f;
            foreach (var it in legend)
                lw += Raylib.MeasureTextEx(Cfg.Font, it.gly, 12, 1f).X + 5 + Raylib.MeasureTextEx(Cfg.Font, it.lbl, 11, 1f).X + 22;
            float lx = x + w / 2f - (lw - 22) / 2f;
            int ly = mapTop + 26 + mapH + 5;
            foreach (var it in legend)
            {
                Raylib.DrawTextEx(Cfg.Font, it.gly, new Vector2((int)lx, ly), 12, 1f, it.col);
                lx += Raylib.MeasureTextEx(Cfg.Font, it.gly, 12, 1f).X + 5;
                Raylib.DrawTextEx(Cfg.Font, it.lbl, new Vector2((int)lx, ly + 1), 11, 1f, Pal.TxtDim);
                lx += Raylib.MeasureTextEx(Cfg.Font, it.lbl, 11, 1f).X + 22;
            }
        }
        else  // fallback: legacy deployment cards (only if the map is unavailable)
        {
            string pick = $"SELECT DEPLOYMENT  >  MISSION {run.Mission + 1}";
            Raylib.DrawTextEx(Cfg.Font, pick, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, pick, 15, 1f).X / 2, mapTop + 4), 15, 1f, Pal.Accent);
            int cw = (w - 60 - 32) / 3, ch = 118, cy = mapTop + 28, gap = 16;
            for (int i = 0; i < run.Offers.Count && i < 3; i++)
            {
                MissionCards[i] = new Rectangle(x + 30 + i * (cw + gap), cy, cw, ch);
                DrawDeployCard(MissionCards[i], run.Offers[i]);
            }
        }
    }

    static Color NodeColor(NodeKind k) => k switch
    {
        NodeKind.Start => Pal.TxtDim,
        NodeKind.Elite => Pal.Elite,
        NodeKind.Supply => Pal.Good,
        NodeKind.Boss => Pal.Foe,
        NodeKind.Event => Pal.Suspect,    // W4: amber "?" — the interactive choice beat
        _ => Pal.Friend,
    };

    static string NodeGlyph(NodeKind k) => k switch
    {
        NodeKind.Start => "S", NodeKind.Elite => "!", NodeKind.Supply => "+", NodeKind.Boss => "X",
        NodeKind.Event => "?", _ => "*",
    };

    /// Draw the branching campaign DAG inside `region`: columns left-to-right (one per
    /// mission), edges as lines, the current position ringed, the reachable next nodes
    /// glowing + clickable (rects cached in NodeBtns), everything else dimmed.
    static void DrawCampaignMap(Run run, Rectangle region)
    {
        NodeBtns.Clear();
        int cols = Run.MaxMissions;
        var cur = run.CurrentNode;
        var reachable = new System.Collections.Generic.HashSet<int>();
        if (cur != null) foreach (var idn in cur.Next) reachable.Add(idn);

        Vector2 Center(MissionNode n)
        {
            float cx = region.X + (n.Col + 0.5f) * (region.Width / cols);
            float cy = region.Y + (n.Row + 0.5f) * (region.Height / Math.Max(1, n.RowCount));
            return new Vector2(cx, cy);
        }

        // edges first, so nodes sit on top
        foreach (var a in run.Map)
            foreach (var nid in a.Next)
            {
                var b = run.Map[nid];
                bool live = cur != null && a.Id == cur.Id;        // outgoing from the current node
                Color ec = live ? Pal.Accent : Pal.RGBA(48, 56, 66);
                Raylib.DrawLineEx(Center(a), Center(b), live ? 2.2f : 1.3f, ec);
            }

        var mouse = Raylib.GetMousePosition();
        MissionNode hovered = null;
        // W12: node size follows the region — the sized-to-fit map (150..250px tall) affords
        // bigger markers than the old fixed 124px strip did (base 10px radius grows to 12px
        // once the region clears 200px; BOSS keeps its +3 emphasis).
        float baseRad = region.Height >= 200 ? 12f : 10f;
        int glyFs = region.Height >= 200 ? 16 : 14;
        // W9: labels only exist on the 1-2 reachable nodes, so remembering ONE previously
        // drawn label rect is enough to dodge every possible overprint at RowCount <= 3.
        Rectangle prevLabel = default;
        bool hasPrevLabel = false;
        foreach (var n in run.Map)
        {
            Vector2 p = Center(n);
            bool isCur = cur != null && n.Id == cur.Id;
            bool canPick = reachable.Contains(n.Id);
            float rad = n.Kind == NodeKind.Boss ? baseRad + 3f : baseRad;
            Color col = NodeColor(n.Kind);

            // dim nodes that are neither visited, current, nor a current choice
            Color fill = (n.Visited || isCur || canPick) ? col : Pal.Mix(col, Pal.Panel, 0.7f);
            if (canPick)
            {
                bool hov = Raylib.CheckCollisionPointRec(mouse, new Rectangle(p.X - rad - 4, p.Y - rad - 4, rad * 2 + 8, rad * 2 + 8));
                if (hov) hovered = n;
                Raylib.DrawCircleV(p, rad + (hov ? 6f : 4f), Raylib.Fade(Pal.Accent, hov ? 0.45f : 0.25f));  // glow
                NodeBtns.Add((n.Id, new Rectangle(p.X - rad - 4, p.Y - rad - 4, rad * 2 + 8, rad * 2 + 8)));
            }
            Raylib.DrawCircleV(p, rad, fill);
            Raylib.DrawCircleLinesV(p, rad, isCur ? Pal.Txt : Pal.RGBA(10, 14, 20));
            if (isCur) Raylib.DrawCircleLinesV(p, rad + 4, Pal.Accent);  // "you are here"

            string gly = NodeGlyph(n.Kind);
            Raylib.DrawTextEx(Cfg.Font, gly, new Vector2((int)(p.X - (int)Raylib.MeasureTextEx(Cfg.Font, gly, glyFs, 1f).X / 2), (int)(p.Y - glyFs / 2)), glyFs, 1f, Pal.RGBA(8, 12, 18));

            if (canPick)  // label the choices with their objective (one clean line, readable size)
            {
                // W9: the inline label is the OBJECTIVE NAME ONLY — the old 3-line stack
                // (objective + intel + enemy hint at 9px) guaranteed overprint at the 41px row
                // pitch. Intel + hint live in the hover tooltip, which already duplicates them.
                // W4: an Event node is a "?" choice beat, not a fight — label it EVENT.
                string lbl = n.Kind == NodeKind.Event ? "EVENT" : ObjName(n.Card.Objective);
                int lw = (int)Raylib.MeasureTextEx(Cfg.Font, lbl, 11, 1f).X;
                var lr = new Rectangle(p.X - lw / 2f, p.Y + rad + 3, lw, 12);   // default: below the node
                // collision nudge: if it would overprint the previously drawn label, flip above.
                if (hasPrevLabel && Raylib.CheckCollisionRecs(lr, prevLabel))
                    lr.Y = p.Y - rad - 15;
                Raylib.DrawTextEx(Cfg.Font, lbl, new Vector2((int)lr.X, (int)lr.Y), 11, 1f, n.Kind == NodeKind.Event ? Pal.Suspect : Pal.Txt);
                prevLabel = lr; hasPrevLabel = true;
            }
        }

        // hover tooltip: the chosen op's flavour (mod / objective / force / reward / enemy hint)
        if (hovered != null)
        {
            var c = hovered.Card;
            string l1 = $"{c.ModName}  -  {ObjName(c.Objective)}";
            string force = c.EnemyDelta > 0 ? "Heavy resistance" : (c.EnemyDelta < 0 ? "Light resistance" : "Standard force");
            string l2 = $"{force}   +{hovered.Intel} intel";   // routing economy: payout shown alongside risk
            string l3 = c.Reward != RewardKind.None ? "+ " + c.RewardText : null;
            string l4 = Run.EnemyHint(hovered);   // enemy intel hint (S4-A)
            int tw = Math.Max((int)Raylib.MeasureTextEx(Cfg.Font, l1, 13, 1f).X,
                     Math.Max((int)Raylib.MeasureTextEx(Cfg.Font, l2, 11, 1f).X,
                     Math.Max(l3 != null ? (int)Raylib.MeasureTextEx(Cfg.Font, l3, 11, 1f).X : 0,
                              (int)Raylib.MeasureTextEx(Cfg.Font, l4, 11, 1f).X))) + 20;
            int th = (l3 != null ? 76 : 60);   // extra row for the hint
            float tx = Math.Min(mouse.X + 14, region.X + region.Width - tw);
            float ty = Math.Max(mouse.Y - th - 6, region.Y);
            var tip = new Rectangle(tx, ty, tw, th);
            Raylib.DrawRectangleRounded(tip, 0.12f, 6, Pal.RGBA(12, 18, 26));
            Raylib.DrawRectangleLinesEx(tip, 1.2f, NodeColor(hovered.Kind));
            Raylib.DrawTextEx(Cfg.Font, l1, new Vector2((int)tx + 10, (int)ty + 8), 13, 1f, NodeColor(hovered.Kind));
            Raylib.DrawTextEx(Cfg.Font, l2, new Vector2((int)tx + 10, (int)ty + 26), 11, 1f, Pal.TxtDim);
            if (l3 != null) Raylib.DrawTextEx(Cfg.Font, l3, new Vector2((int)tx + 10, (int)ty + 42), 11, 1f, Pal.Accent);
            int hintY = l3 != null ? (int)ty + 58 : (int)ty + 42;
            Raylib.DrawTextEx(Cfg.Font, l4, new Vector2((int)tx + 10, hintY), 11, 1f, Pal.Foe);
        }
    }

    static void DrawRequisition(Game g)
    {
        var run = g.RunState;
        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.RGBA(5, 8, 11), 0.85f));

        // ROTATING OFFER: only the items in this barracks' slate are shown (a smaller, prioritised
        // set), so the player chooses among ~5 rather than buying the one obvious item out of ~10.
        // ShopBtns are indexed by SLOT; the underlying item id is offer[slot].
        var offer = g.ShopOffer();
        int items = offer.Count;
        int ih = 78, gap = 10;
        int squadH = 40;
        // Lay the slate out as a 2-COLUMN grid so every row keeps a comfortable, legible height and
        // the whole card still clears ScreenH (the slate is ~5-6 items).
        int chrome = 104 + squadH + 60;
        const int cols = 2;
        int rowsPerCol = (items + cols - 1) / cols;
        int shopH = chrome + rowsPerCol * (ih + gap);
        // The armory sub-screen is a single shorter column, so size the card to the active view —
        // otherwise the taller shop card clips off the top/bottom of the screen.
        int armoryH = 104 + 28 + Run.RosterMax * 52 + 64;
        int w = g.ArmoryMode ? 560 : 760, h = g.ArmoryMode ? armoryH : shopH;
        int x = Cfg.ScreenW / 2 - w / 2, y = Cfg.ScreenH / 2 - h / 2;
        y -= (int)((1f - Util.EaseOutQuad(PanelAnim("requisition", 0.15f))) * 16f);  // slide-down entrance
        var card = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(card, 0.04f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.5f, Pal.PanelBd);

        string title = "REQUISITION";
        Raylib.DrawTextEx(Cfg.Font, title, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, title, 36, 1f).X / 2, y + 24), 36, 1f, Pal.Accent);
        // W9: the salvage bank shares the header — the slate re-roll below spends it (not Intel)
        string intel = $"INTEL AVAILABLE: {run.Intel}   |   SALVAGE: {g.BarracksSalvage}";
        Raylib.DrawTextEx(Cfg.Font, intel, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, intel, 16, 1f).X / 2, y + 66), 16, 1f, Pal.Good);

        // ARMORY toggle (top-right of the card): swap to the re-arm sub-screen and back.
        ArmoryToggle = new Rectangle(x + w - 132, y + 24, 108, 30);
        bool ath = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), ArmoryToggle);
        Raylib.DrawRectangleRounded(ArmoryToggle, 0.3f, 6, g.ArmoryMode ? Pal.Accent : (ath ? Pal.RGBA(24, 34, 46) : Pal.RGBA(14, 20, 28)));
        Raylib.DrawRectangleLinesEx(ArmoryToggle, 1.4f, g.ArmoryMode ? Pal.Accent : Pal.PanelBd);
        CenterText(g.ArmoryMode ? "< SHOP" : "ARMORY [A]", ArmoryToggle, 13, g.ArmoryMode ? Pal.RGBA(3, 18, 26) : Pal.Txt);

        if (g.ArmoryMode) { DrawArmory(g, x, y, w, h); return; }

        // squad HP strip so the player can judge whether a heal/stim is worth it
        DrawSquadHpStrip(run.Squad, x + 30, y + 96, w - 60);

        int gridTop = y + 104 + squadH;
        int colGap = 16;
        int colW = (w - 60 - colGap) / 2;
        for (int slot = 0; slot < items && slot < ShopBtns.Length; slot++)
        {
            int i = offer[slot];   // underlying item id for this slate slot
            int col = slot / rowsPerCol, rowInCol = slot % rowsPerCol;
            var r = new Rectangle(x + 30 + col * (colW + colGap), gridTop + rowInCol * (ih + gap), colW, ih);
            ShopBtns[slot] = r;
            bool can = g.CanBuy(i);
            bool hover = can && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
            Raylib.DrawRectangleRounded(r, 0.1f, 6, hover ? Pal.RGBA(24, 34, 46) : Pal.RGBA(14, 20, 28));
            Raylib.DrawRectangleLinesEx(r, 1.5f, can ? (hover ? Pal.Accent : Pal.PanelBd) : Pal.RGBA(40, 46, 54));
            Color txt = can ? Pal.Txt : Pal.TxtDim;
            Raylib.DrawTextEx(Cfg.Font, g.ShopNameAt(i), new Vector2((int)r.X + 14, (int)r.Y + 10), 18, 1f, txt);
            // W11: the desc WRAPS to (max) two 11px lines inside the card — several descs (FRAG
            // CACHE, BALLISTIC PLATING, the prep rows) measured wider than the card and ran under
            // the neighbouring column. Two lines cover every current desc; Clip is the backstop.
            var descLines = WrapText(g.ShopDescAt(i), 11, (int)r.Width - 28);
            if (descLines.Count > 2)
            {
                descLines[1] = Clip(descLines[1] + " " + string.Join(" ", descLines.GetRange(2, descLines.Count - 2)), 11, (int)r.Width - 28);
                descLines.RemoveRange(2, descLines.Count - 2);
            }
            for (int li = 0; li < descLines.Count; li++)
                Raylib.DrawTextEx(Cfg.Font, descLines[li], new Vector2((int)r.X + 14, (int)r.Y + 32 + li * 13), 11, 1f, Pal.TxtDim);
            Raylib.DrawTextEx(Cfg.Font, g.ShopEffect(i), new Vector2((int)r.X + 14, (int)r.Y + 59), 12, 1f, can ? Pal.Accent : Pal.TxtDim);  // concrete effect
            int icost = g.ShopCostAt(i);
            string cost = $"{icost} INTEL";
            Color cc = run.Intel >= icost ? Pal.Good : Pal.Foe;
            Raylib.DrawTextEx(Cfg.Font, cost, new Vector2((int)(r.X + r.Width - (int)Raylib.MeasureTextEx(Cfg.Font, cost, 16, 1f).X - 14), (int)r.Y + 12), 16, 1f, cc);
            if (!can)
                Raylib.DrawTextEx(Cfg.Font, "- unavailable -", new Vector2((int)(r.X + r.Width - (int)Raylib.MeasureTextEx(Cfg.Font, "- unavailable -", 11, 1f).X - 14), (int)r.Y + 52), 11, 1f, Pal.TxtDim);
            else
                Raylib.DrawTextEx(Cfg.Font, "[ BUY ]", new Vector2((int)(r.X + r.Width - (int)Raylib.MeasureTextEx(Cfg.Font, "[ BUY ]", 12, 1f).X - 14), (int)r.Y + 54), 12, 1f, Pal.Accent);
        }

        ShopProceed = new Rectangle(x + w / 2 - 130, y + h - 60, 260, 44);
        bool ph = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), ShopProceed);
        Raylib.DrawRectangleRounded(ShopProceed, 0.3f, 8, ph ? Pal.RGBA(92, 200, 251) : Pal.Friend);
        CenterText("PROCEED TO DEPLOYMENT", ShopProceed, 15, Pal.RGBA(3, 18, 26));

        // ---- W9: paid slate RE-ROLL (salvage sink; Game refuses the click when broke) ----
        ShopReroll = new Rectangle(x + w - 30 - 178, y + h - 56, 178, 36);
        bool srCan = g.BarracksSalvage >= MetaProg.ShopRerollCost;
        bool srHov = srCan && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), ShopReroll);
        Raylib.DrawRectangleRounded(ShopReroll, 0.3f, 8, srHov ? Pal.RGBA(30, 44, 34) : Pal.RGBA(14, 20, 28));
        Raylib.DrawRectangleLinesEx(ShopReroll, 1.4f, srCan ? (srHov ? Pal.VipGold : Pal.PanelBd) : Pal.RGBA(40, 46, 54));
        CenterText($"RE-ROLL SLATE ({MetaProg.ShopRerollCost} SALV)", ShopReroll, 12, srCan ? (srHov ? Pal.VipGold : Pal.Txt) : Pal.TxtDim);
    }

    /// The ARMORY sub-screen of REQUISITION: re-arm a soldier with a different weapon their class
    /// can carry (Weapon.ArmoryOptions). Two steps: pick a soldier, then pick a weapon. A flat
    /// Intel cost; the choice persists on the soldier across the run.
    static void DrawArmory(Game g, int x, int y, int w, int h)
    {
        var run = g.RunState;
        ArmorySoldierBtns.Clear();
        ArmoryWeaponBtns.Clear();
        var mouse = Raylib.GetMousePosition();

        string sub = $"ARMORY  -  re-arm a soldier ({Game.ArmoryCost} INTEL each)";
        Raylib.DrawTextEx(Cfg.Font, sub, new Vector2(x + 30, y + 92), 14, 1f, Pal.TxtDim);

        int iy = y + 118;
        if (g.ArmorySoldier == null)
        {
            // STEP 1: choose a soldier
            Raylib.DrawTextEx(Cfg.Font, "SELECT A SOLDIER", new Vector2(x + 30, iy), 13, 1f, Pal.Accent);
            iy += 24;
            var roster = g.ArmoryRoster;
            foreach (var u in roster)
            {
                var r = new Rectangle(x + 30, iy, w - 60, 44);
                ArmorySoldierBtns.Add(r);
                bool hov = Raylib.CheckCollisionPointRec(mouse, r);
                Raylib.DrawRectangleRounded(r, 0.12f, 6, hov ? Pal.RGBA(24, 34, 46) : Pal.RGBA(14, 20, 28));
                Raylib.DrawRectangleLinesEx(r, 1.3f, hov ? Pal.Accent : Pal.PanelBd);
                Raylib.DrawTextEx(Cfg.Font, $"{u.Name}  ({u.Cls})", new Vector2((int)r.X + 14, (int)r.Y + 8), 16, 1f, Pal.Txt);
                string cur = $"carrying: {u.Weapon?.Name}";
                Raylib.DrawTextEx(Cfg.Font, cur, new Vector2((int)r.X + 14, (int)r.Y + 27), 12, 1f, Pal.TxtDim);
                int nopt = Weapon.ArmoryOptions(u.Cls).Length;
                string opt = $"{nopt} option{(nopt > 1 ? "s" : "")} >";
                Raylib.DrawTextEx(Cfg.Font, opt, new Vector2((int)(r.X + r.Width - (int)Raylib.MeasureTextEx(Cfg.Font, opt, 13, 1f).X - 14), (int)r.Y + 15), 13, 1f, hov ? Pal.Accent : Pal.TxtDim);
                iy += 52;
            }
        }
        else
        {
            // STEP 2: choose a weapon for the selected soldier
            var u = g.ArmorySoldier;
            Raylib.DrawTextEx(Cfg.Font, $"RE-ARM  {u.Name} ({u.Cls})", new Vector2(x + 30, iy), 14, 1f, Pal.Accent);
            iy += 24;
            var opts = Weapon.ArmoryOptions(u.Cls);
            foreach (var k in opts)
            {
                var r = new Rectangle(x + 30, iy, w - 60, 56);
                ArmoryWeaponBtns.Add(r);
                bool current = u.Weapon != null && u.Weapon.Kind == k;
                bool can = g.CanRearm(u, k);
                bool hov = can && Raylib.CheckCollisionPointRec(mouse, r);
                Color bg = current ? Pal.RGBA(20, 40, 30) : (hov ? Pal.RGBA(24, 34, 46) : Pal.RGBA(14, 20, 28));
                Raylib.DrawRectangleRounded(r, 0.1f, 6, bg);
                Raylib.DrawRectangleLinesEx(r, 1.4f, current ? Pal.Good : (can ? (hov ? Pal.Accent : Pal.PanelBd) : Pal.RGBA(40, 46, 54)));
                var probe = Weapon.Make(k);
                Raylib.DrawTextEx(Cfg.Font, probe.Name, new Vector2((int)r.X + 14, (int)r.Y + 8), 17, 1f, current ? Pal.Good : (can ? Pal.Txt : Pal.TxtDim));
                Raylib.DrawTextEx(Cfg.Font, Weapon.KindBlurb(k), new Vector2((int)r.X + 14, (int)r.Y + 31), 12, 1f, Pal.TxtDim);
                if (current)
                    Raylib.DrawTextEx(Cfg.Font, "EQUIPPED", new Vector2((int)(r.X + r.Width - (int)Raylib.MeasureTextEx(Cfg.Font, "EQUIPPED", 13, 1f).X - 14), (int)r.Y + 20), 13, 1f, Pal.Good);
                else if (can)
                    Raylib.DrawTextEx(Cfg.Font, $"[ {Game.ArmoryCost} INTEL ]", new Vector2((int)(r.X + r.Width - (int)Raylib.MeasureTextEx(Cfg.Font, $"[ {Game.ArmoryCost} INTEL ]", 13, 1f).X - 14), (int)r.Y + 20), 13, 1f, Pal.Accent);
                else
                    Raylib.DrawTextEx(Cfg.Font, "- need intel -", new Vector2((int)(r.X + r.Width - (int)Raylib.MeasureTextEx(Cfg.Font, "- need intel -", 12, 1f).X - 14), (int)r.Y + 21), 12, 1f, Pal.Foe);
                iy += 64;
            }
            Raylib.DrawTextEx(Cfg.Font, "[Esc] back to soldier list", new Vector2(x + 30, iy + 4), 12, 1f, Pal.TxtDim);
        }

        ShopProceed = new Rectangle(x + w / 2 - 130, y + h - 60, 260, 44);
        bool ph = Raylib.CheckCollisionPointRec(mouse, ShopProceed);
        Raylib.DrawRectangleRounded(ShopProceed, 0.3f, 8, ph ? Pal.RGBA(92, 200, 251) : Pal.Friend);
        CenterText("PROCEED TO DEPLOYMENT", ShopProceed, 15, Pal.RGBA(3, 18, 26));
    }

    static string ObjName(Objective o) => o switch
    {
        Objective.Hack => "HACK", Objective.Evac => "EXTRACT", Objective.Escort => "ESCORT VIP",
        Objective.Sabotage => "SABOTAGE", Objective.Rescue => "RESCUE", Objective.Defend => "DEFEND",
        Objective.Decapitate => "DECAPITATE", _ => "ELIMINATE",
    };

    static void DrawDeployCard(Rectangle r, MissionCard c)
    {
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Color tint = c.ModName == "RECON" ? Pal.Good : (c.ModName == "ONSLAUGHT" ? Pal.Foe : Pal.Friend);
        Raylib.DrawRectangleRounded(r, 0.1f, 6, hover ? Pal.RGBA(24, 34, 46) : Pal.RGBA(14, 20, 28));
        Raylib.DrawRectangleLinesEx(r, 1.5f, hover ? tint : Pal.PanelBd);
        Raylib.DrawRectangle((int)r.X, (int)r.Y, 4, (int)r.Height, tint);

        Raylib.DrawTextEx(Cfg.Font, c.ModName, new Vector2((int)r.X + 12, (int)r.Y + 10), 17, 1f, tint);
        Raylib.DrawTextEx(Cfg.Font, ObjName(c.Objective), new Vector2((int)r.X + 12, (int)r.Y + 34), 13, 1f, Pal.Txt);
        string force = c.EnemyDelta > 0 ? "Heavy resistance" : (c.EnemyDelta < 0 ? "Light resistance" : "Standard force");
        Raylib.DrawTextEx(Cfg.Font, force, new Vector2((int)r.X + 12, (int)r.Y + 56), 11, 1f, Pal.TxtDim);
        if (c.Reward != RewardKind.None)
            Raylib.DrawTextEx(Cfg.Font, "+ " + c.RewardText, new Vector2((int)r.X + 12, (int)r.Y + 74), 11, 1f, Pal.Accent);
        Raylib.DrawTextEx(Cfg.Font, "DEPLOY", new Vector2((int)(r.X + r.Width / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, "DEPLOY", 12, 1f).X / 2), (int)(r.Y + r.Height - 22)), 12, 1f, hover ? tint : Pal.TxtDim);
    }

    // Rank-up perk choice: the soldier + two perk cards (pick one).
    static void DrawPerkChooser(Game g, PerkOffer off)
    {
        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.RGBA(5, 8, 11), 0.88f));
        int w = 660, h = 440;
        int x = Cfg.ScreenW / 2 - w / 2, y = Cfg.ScreenH / 2 - h / 2;
        y -= (int)((1f - Util.EaseOutQuad(PanelAnim("perkchooser", 0.15f))) * 16f);  // slide-down entrance
        var card = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(card, 0.04f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.5f, Pal.PanelBd);

        string title = "PROMOTION";
        int titW = (int)Raylib.MeasureTextEx(Cfg.Font, title, 36, 1f).X;
        Raylib.DrawTextEx(Cfg.Font, title, new Vector2(x + w / 2 - titW / 2, y + 22), 36, 1f, Pal.Accent);
        // W12: the promoted soldier's class silhouette flanks the header (board-matching glyph),
        // so WHO is ranking up reads before the text does. The left copy is mirrored (ang=PI) so
        // the pair reads symmetric around the title.
        Renderer.DrawCodexGlyph(off.Unit.Cls, new Vector2(x + w / 2f - titW / 2f - 36, y + 42), Pal.Friend, 1.5f, MathF.PI);
        Renderer.DrawCodexGlyph(off.Unit.Cls, new Vector2(x + w / 2f + titW / 2f + 36, y + 42), Pal.Friend, 1.5f);
        string sub = $"{off.Unit.FullName}  -  {off.Unit.RankName}  -  {off.Unit.Cls}  -  CHOOSE A PERK";
        Raylib.DrawTextEx(Cfg.Font, sub, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, sub, 14, 1f).X / 2, y + 64), 14, 1f, Pal.TxtDim);

        // full dossier so perks can be chosen for synergy
        var dossier = new Rectangle(x + 20, y + 88, w - 40, 96);
        Raylib.DrawRectangleRounded(dossier, 0.08f, 6, Pal.RGBA(13, 19, 27));
        Raylib.DrawRectangleLinesEx(dossier, 1f, Pal.PanelBd);
        DrawDossier(off.Unit, x + 34, y + 98, w - 68);

        // edit-tag affordance (also editable in-mission with key T)
        PerkTagBtn = new Rectangle(x + w - 20 - 120, y + 94, 120, 24);
        bool th = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), PerkTagBtn);
        Raylib.DrawRectangleRounded(PerkTagBtn, 0.3f, 6, th ? Pal.RGBA(24, 34, 46) : Pal.RGBA(14, 20, 28));
        Raylib.DrawRectangleLinesEx(PerkTagBtn, 1.2f, th ? Pal.Accent : Pal.PanelBd);
        CenterText("EDIT TAG", PerkTagBtn, 12, th ? Pal.Accent : Pal.TxtDim);

        int cw = (w - 60) / 2, ch = 154, cy = y + 196, gap = 20;
        PerkBtnA = new Rectangle(x + 20, cy, cw, ch);
        PerkBtnB = new Rectangle(x + 20 + cw + gap, cy, cw, ch);
        DrawPerkCard(PerkBtnA, off.A, off.Unit);
        DrawPerkCard(PerkBtnB, off.B, off.Unit);

        int left = g.RunState.PendingPerks.Count - 1;
        string foot = left > 0 ? $"{left} more promotion(s) to assign" : "Click a perk to continue";
        Raylib.DrawTextEx(Cfg.Font, foot, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, foot, 12, 1f).X / 2, y + h - 26), 12, 1f, Pal.TxtDim);
    }

    /// A soldier's stat line + current perks + derived strengths (for decision screens).
    static void DrawDossier(Unit u, int x, int y, int w)
    {
        // installed weapon upgrades read right next to the weapon name (e.g. "Rifle [SCP MAG]")
        string wpn = u.Weapon.Name + (u.WeaponMods.Count > 0
            ? " [" + string.Join(" ", u.WeaponMods.ConvertAll(WeaponModDef.Code)) + "]" : "");
        string stats = $"HP {u.Hp}/{u.MaxHp}    AIM {u.Aim}    MOB {u.Mobility}    {wpn}    GREN {1 + u.BonusGrenades}/mission    {u.AbilityName}";
        Raylib.DrawTextEx(Cfg.Font, stats, new Vector2(x, y), 13, 1f, Pal.Txt);
        string perks = u.Perks.Count == 0 ? "Perks: none yet"
            : "Perks: " + string.Join(", ", u.Perks.ConvertAll(PerkDef.Name));
        if (u.IsSpecialized) perks += "    [" + SpecDef.Name(u.Spec) + "]";   // W2: show the chosen fork
        Raylib.DrawTextEx(Cfg.Font, perks, new Vector2(x, y + 22), 13, 1f, Pal.Good);

        // earned traits + bonds (3.2): what makes this veteran distinct
        string traits = u.Traits.Count == 0 ? "Traits: none yet"
            : "Traits: " + string.Join(", ", u.Traits.ConvertAll(TraitDef.Name));
        if (u.Bonds.Count > 0) traits += "    Bonds: " + string.Join(", ", u.Bonds);
        Raylib.DrawTextEx(Cfg.Font, traits, new Vector2(x, y + 44), 12, 1f, u.Traits.Count == 0 && u.Bonds.Count == 0 ? Pal.TxtDim : Pal.VipGold);

        // SCARS (W5): the cost side of identity, in a distinct rust-red next to the gold traits.
        if (u.Scars.Count > 0)
        {
            float tw = Raylib.MeasureTextEx(Cfg.Font, traits + "    ", 12, 1f).X;
            string scars = "Scars: " + string.Join(", ", u.Scars.ConvertAll(ScarDef.Name));
            if (u.VendettaFaction != Faction.None && u.HasScar(Scar.Vendetta))
                scars += $" (vs {Run.FactionName(u.VendettaFaction)})";
            Raylib.DrawTextEx(Cfg.Font, scars, new Vector2(x + tw, y + 44), 12, 1f, Pal.FoeDk);
        }

        if (u.Wound > 0)
            Raylib.DrawTextEx(Cfg.Font, $"WOUNDED ({u.Wound} mission{(u.Wound > 1 ? "s" : "")})  -{Unit.WoundAim} aim / -{Unit.WoundMob} mob", new Vector2(x, y + 66), 12, 1f, Pal.Foe);
        else if (!string.IsNullOrEmpty(u.CustomTag))
            Raylib.DrawTextEx(Cfg.Font, "Tag: " + u.CustomTag, new Vector2(x, y + 66), 12, 1f, Pal.Friend);
        else
        {
            var sp = Specialties(u);
            if (sp.Count > 0)
                Raylib.DrawTextEx(Cfg.Font, "Strengths: " + string.Join("  ", sp), new Vector2(x, y + 66), 12, 1f, Pal.Accent);
        }
    }

    /// Derived strength tags from class/weapon/stats/perks, so the player knows what
    /// each soldier is good at (in-round play and build planning).
    public static System.Collections.Generic.List<string> Specialties(Unit u)
    {
        var t = new System.Collections.Generic.List<string>();
        if (u.HasPerk(Perk.Reflexes) || u.HasPerk(Perk.Guardian)) t.Add("OVERWATCH");
        if (u.HasPerk(Perk.Deadeye) || u.Aim >= 72) t.Add("SHARP");
        if (u.HasPerk(Perk.LockOn)) t.Add("FLANKER");
        if ((u.Weapon != null && (u.Weapon.Kind == WeaponKind.Shotgun || u.Weapon.Kind == WeaponKind.Smg)) || u.HasPerk(Perk.CloseQuarters)) t.Add("CLOSE");
        if ((u.Weapon != null && u.Weapon.Kind == WeaponKind.Sniper) || u.HasPerk(Perk.Marksman)) t.Add("LONG");
        if (u.HasPerk(Perk.Tank) || u.HasPerk(Perk.Hardened) || u.MaxHp >= 11) t.Add("TOUGH");
        if (u.HasPerk(Perk.Sprinter) || u.Mobility >= 8) t.Add("FAST");
        if (u.Cls == "CORPSMAN") t.Insert(0, "MEDIC");   // the support role leads its tag list
        if (t.Count == 0)   // class-role fallback so every soldier reads with a strength
            t.Add(u.Cls switch
            {
                "GUNNER" => "SUPPRESS",
                "ASSAULT" => "ASSAULT",
                "RANGER" => "CLOSE",
                "SHARPSHOOTER" => "SHARP",
                "CORPSMAN" => "MEDIC",
                _ => "SOLDIER",
            });
        return t;
    }

    /// Compact HP-per-soldier row used on the shop screen.
    static void DrawSquadHpStrip(System.Collections.Generic.List<Unit> squad, int x, int y, int w)
    {
        if (squad.Count == 0) return;
        int cw = w / squad.Count;
        for (int i = 0; i < squad.Count; i++)
        {
            var u = squad[i];
            int cx = x + i * cw;
            int barW = cw - 14;
            Raylib.DrawTextEx(Cfg.Font, u.Name, new Vector2(cx, y), 11, 1f, Pal.Txt);
            // HP readout right-aligned over the bar's right edge so the name line carries the value
            // and the bar below it is a clean, slightly taller gauge (no stray third text line).
            string hpTxt = $"{u.Hp}/{u.MaxHp}";
            int hpW = (int)Raylib.MeasureTextEx(Cfg.Font, hpTxt, 10, 1f).X;
            Raylib.DrawTextEx(Cfg.Font, hpTxt, new Vector2(cx + barW - hpW, y + 1), 10, 1f, Pal.TxtDim);
            var bar = new Rectangle(cx, y + 16, barW, 9);
            Raylib.DrawRectangleRounded(bar, 0.5f, 4, Pal.RGBA(10, 15, 21));
            float frac = u.MaxHp > 0 ? u.Hp / (float)u.MaxHp : 0;
            if (frac > 0)
            {
                Color hc = frac > 0.5f ? Pal.Good : (frac > 0.25f ? Pal.Accent : Pal.Foe);
                Raylib.DrawRectangleRounded(new Rectangle(bar.X, bar.Y, bar.Width * frac, bar.Height), 0.5f, 4, hc);
            }
        }
    }

    static void DrawPerkCard(Rectangle r, Perk p, Unit u = null)
    {
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Raylib.DrawRectangleRounded(r, 0.08f, 8, hover ? Pal.RGBA(24, 34, 46) : Pal.RGBA(14, 20, 28));
        Raylib.DrawRectangleLinesEx(r, 1.5f, hover ? Pal.Accent : Pal.PanelBd);
        Raylib.DrawRectangle((int)r.X, (int)r.Y, 4, (int)r.Height, hover ? Pal.Accent : Pal.Friend);

        string name = PerkDef.Name(p);
        Raylib.DrawTextEx(Cfg.Font, name, new Vector2((int)(r.X + r.Width / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, name, 22, 1f).X / 2), (int)r.Y + 24), 22, 1f, hover ? Pal.Accent : Pal.Txt);
        // W12: the concrete BEFORE > AFTER stat line for THIS soldier (null when a perk has no
        // clean numeric read — the prose below still carries it), so a pick is a visible delta.
        string delta = u != null ? PerkDeltaLine(u, p) : null;
        if (delta != null)
            Raylib.DrawTextEx(Cfg.Font, delta,
                new Vector2((int)(r.X + r.Width / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, delta, 13, 1f).X / 2), (int)r.Y + 50), 13, 1f, Pal.Good);
        // description, word-wrapped to the card width so a long perk text never spills into the
        // neighbouring card (each line centred, stacked under the name).
        string desc = PerkDef.Desc(p);
        var dlines = WrapText(desc, 14, (int)r.Width - 28);
        for (int li = 0; li < dlines.Count; li++)
            Raylib.DrawTextEx(Cfg.Font, dlines[li],
                new Vector2((int)(r.X + r.Width / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, dlines[li], 14, 1f).X / 2), (int)r.Y + 72 + li * 18),
                14, 1f, Pal.TxtDim);

        Raylib.DrawTextEx(Cfg.Font, "SELECT", new Vector2((int)(r.X + r.Width / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, "SELECT", 13, 1f).X / 2), (int)(r.Y + r.Height - 32)), 13, 1f, hover ? Pal.Accent : Pal.TxtDim);
    }

    /// W12: a soldier-specific before > after stat readout for a perk offer. Returns null for
    /// perks whose benefit has no clean numeric framing (pure-behaviour perks like SKIRMISHER).
    /// Only OFFERED perks (PerkDef.All) get an arm — the retired crit cluster (Deadeye /
    /// Opportunist / PointBlank / Vanguard) is no longer read by ComputeOdds, so it must never
    /// show an authoritative-looking number if some future change re-offers it.
    static string PerkDeltaLine(Unit u, Perk p) => p switch
    {
        Perk.Tank => $"HP {u.MaxHp} > {u.MaxHp + 3}",
        Perk.Sprinter => $"MOB {u.Mobility} > {u.Mobility + 1}",
        Perk.LockOn => $"AIM {u.Aim} > {u.Aim + 15} vs flanked",   // review fix: fires on FLANKED, not merely exposed
        Perk.CloseQuarters => $"AIM {u.Aim} > {u.Aim + 15} inside 4 tiles",
        Perk.Marksman => $"AIM {u.Aim} > {u.Aim + 15} at 7+ tiles",
        Perk.Siegebreaker => $"AIM {u.Aim} > {u.Aim + 15} vs hunkered",
        Perk.Bandolier => $"GRENADES {1 + u.BonusGrenades} > {2 + u.BonusGrenades} / mission",
        Perk.Executioner => "CRIT +25 vs sub-half-HP",
        Perk.GiantSlayer => "CRIT +15 vs full-HP",
        Perk.Vantage => "CRIT +15 from high ground",
        Perk.Breaker => "CRIT +20 vs suppressed / pinned",
        Perk.Hardened => "DMG TAKEN -1  (crits -4)",
        Perk.Bulwark => "DMG TAKEN -2 at half HP or above",
        Perk.CoolHeaded => "ENEMY AIM -8 against you",
        _ => null,
    };

    /// W2 CLASS SPECIALIZATION FORK chooser (a one-time pick at Corporal). Near-copy of DrawPerkChooser:
    /// a framed card, the soldier dossier, and two fork cards (Name + flavour + word-wrapped mechanics).
    static void DrawSpecChooser(Game g, SpecOffer off)
    {
        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.RGBA(5, 8, 11), 0.88f));
        int w = 680, h = 452;
        int x = Cfg.ScreenW / 2 - w / 2, y = Cfg.ScreenH / 2 - h / 2;
        y -= (int)((1f - Util.EaseOutQuad(PanelAnim("specchooser", 0.15f))) * 16f);  // slide-down entrance
        var card = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(card, 0.04f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.5f, Pal.Accent);

        string title = "SPECIALIZE";
        int stw = (int)Raylib.MeasureTextEx(Cfg.Font, title, 36, 1f).X;
        Raylib.DrawTextEx(Cfg.Font, title, new Vector2(x + w / 2 - stw / 2, y + 22), 36, 1f, Pal.Accent);
        // W12: class silhouette flanking the header — same treatment as PROMOTION (left mirrored).
        Renderer.DrawCodexGlyph(off.Unit.Cls, new Vector2(x + w / 2f - stw / 2f - 36, y + 42), Pal.Friend, 1.5f, MathF.PI);
        Renderer.DrawCodexGlyph(off.Unit.Cls, new Vector2(x + w / 2f + stw / 2f + 36, y + 42), Pal.Friend, 1.5f);
        string sub = $"{off.Unit.FullName}  -  {off.Unit.Cls}  -  CHOOSE A PERMANENT FORK";
        Raylib.DrawTextEx(Cfg.Font, sub, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, sub, 14, 1f).X / 2, y + 64), 14, 1f, Pal.TxtDim);

        // dossier so the fork can be picked for the soldier's build
        var dossier = new Rectangle(x + 20, y + 88, w - 40, 96);
        Raylib.DrawRectangleRounded(dossier, 0.08f, 6, Pal.RGBA(13, 19, 27));
        Raylib.DrawRectangleLinesEx(dossier, 1f, Pal.PanelBd);
        DrawDossier(off.Unit, x + 34, y + 98, w - 68);

        int cw = (w - 60) / 2, ch = 178, cy = y + 196, gap = 20;
        SpecBtnA = new Rectangle(x + 20, cy, cw, ch);
        SpecBtnB = new Rectangle(x + 20 + cw + gap, cy, cw, ch);
        DrawSpecCard(SpecBtnA, off.A);
        DrawSpecCard(SpecBtnB, off.B);

        string foot = "This choice is permanent for this soldier";
        Raylib.DrawTextEx(Cfg.Font, foot, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, foot, 12, 1f).X / 2, y + h - 26), 12, 1f, Pal.TxtDim);
    }

    static void DrawSpecCard(Rectangle r, Spec s)
    {
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Raylib.DrawRectangleRounded(r, 0.08f, 8, hover ? Pal.RGBA(24, 34, 46) : Pal.RGBA(14, 20, 28));
        Raylib.DrawRectangleLinesEx(r, 1.5f, hover ? Pal.Accent : Pal.PanelBd);
        Raylib.DrawRectangle((int)r.X, (int)r.Y, 4, (int)r.Height, hover ? Pal.Accent : Pal.Friend);

        string name = SpecDef.Name(s);
        Raylib.DrawTextEx(Cfg.Font, name, new Vector2((int)(r.X + r.Width / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, name, 22, 1f).X / 2), (int)r.Y + 18), 22, 1f, hover ? Pal.Accent : Pal.Txt);
        // flavour line (italic-feel via dim accent), then the mechanical description word-wrapped.
        string fant = SpecDef.Fantasy(s);
        var flines = WrapText(fant, 12, (int)r.Width - 24);
        int fy = (int)r.Y + 50;
        for (int li = 0; li < flines.Count; li++)
            Raylib.DrawTextEx(Cfg.Font, flines[li],
                new Vector2((int)(r.X + r.Width / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, flines[li], 12, 1f).X / 2), fy + li * 16),
                12, 1f, hover ? Pal.Accent : Pal.VipGold);

        string desc = SpecDef.Desc(s);
        var dlines = WrapText(desc, 13, (int)r.Width - 24);
        int dy0 = fy + flines.Count * 16 + 10;
        for (int li = 0; li < dlines.Count; li++)
            Raylib.DrawTextEx(Cfg.Font, dlines[li],
                new Vector2((int)(r.X + r.Width / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, dlines[li], 13, 1f).X / 2), dy0 + li * 17),
                13, 1f, Pal.TxtDim);

        Raylib.DrawTextEx(Cfg.Font, "SELECT", new Vector2((int)(r.X + r.Width / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, "SELECT", 13, 1f).X / 2), (int)(r.Y + r.Height - 28)), 13, 1f, hover ? Pal.Accent : Pal.TxtDim);
    }

    static void DrawSquadRow(Game g, Unit u, int x, int y, int w)
    {
        // benched soldiers read as "on the bench": dimmer plate + a grey accent stripe.
        bool benched = u.Benched;
        Raylib.DrawRectangleRounded(new Rectangle(x, y, w, 40), 0.2f, 6, benched ? Pal.RGBA(11, 15, 20) : Pal.RGBA(13, 19, 27));
        Raylib.DrawRectangle(x, y, 3, 40, benched ? Pal.TxtDim : Pal.Friend);

        // ---- left block: class silhouette + name + rank/class (+wound) ----
        // W12: the board's class glyph (Renderer.DrawCodexGlyph) leads every row, so a soldier
        // reads by SHAPE before text — same silhouette the battlefield draws, never a new icon.
        Renderer.DrawCodexGlyph(u.Cls, new Vector2(x + 21, y + 20), benched ? Raylib.Fade(Pal.Friend, 0.45f) : Pal.Friend, 0.95f);
        Raylib.DrawTextEx(Cfg.Font, u.Name, new Vector2(x + 40, y + 5), 18, 1f, benched ? Pal.TxtDim : Pal.Txt);
        // class is always on the row so benching is an informed choice; WOUND is flagged in red.
        string rankLine = $"{u.RankName}  -  {u.Cls}";
        if (u.Wound > 0) rankLine += $"  WOUNDED({u.Wound})";
        Raylib.DrawTextEx(Cfg.Font, rankLine, new Vector2(x + 40, y + 23), 11, 1f, u.Wound > 0 ? Pal.Foe : (benched ? Pal.TxtDim : Pal.Accent));
        // earned perks (compact 3-letter codes) trail the rank line on the same baseline so they
        // never collide with the stat columns to the right.
        if (u.Perks.Count > 0)
        {
            int rlw = (int)Raylib.MeasureTextEx(Cfg.Font, rankLine, 11, 1f).X;
            string codes = string.Join(" ", u.Perks.ConvertAll(PerkDef.Code));
            // W9: 9px -> 11px floor. Budget re-fit: the left block runs from x+40 (past the W12
            // glyph) to the HP lane at x+w-400 (w=640 rows), so the codes get what remains.
            codes = Clip(codes, 11, Math.Max(0, 182 - rlw));
            Raylib.DrawTextEx(Cfg.Font, codes, new Vector2(x + 40 + rlw + 8, y + 24), 11, 1f, benched ? Pal.RGBA(60, 92, 70) : Pal.Good);
        }

        // ---- three clean stat lanes on the right (HP | KILLS | STATUS+PILL) ----
        // lane anchors are defined relative to the pill on the far right so they can't drift into it.
        const int pillW = 76, statusW = 64, killsW = 96, hpW = 132;
        int pillX   = x + w - pillW - 8;
        int statusX = pillX - statusW - 8;
        int killsX  = statusX - killsW - 8;
        int hpX     = killsX - hpW - 8;

        // HP lane: a labeled bar with the n/n readout on a second line so it never sits on the bar.
        var bar = new Rectangle(hpX, y + 9, hpW, 11);
        Raylib.DrawRectangleRounded(bar, 0.5f, 6, Pal.RGBA(10, 15, 21));
        float frac = u.MaxHp > 0 ? u.Hp / (float)u.MaxHp : 0;
        if (frac > 0)
        {
            Color hc = frac > 0.5f ? Pal.Good : (frac > 0.25f ? Pal.Accent : Pal.Foe);
            if (benched) hc = Raylib.Fade(hc, 0.45f);
            Raylib.DrawRectangleRounded(new Rectangle(bar.X, bar.Y, bar.Width * frac, bar.Height), 0.5f, 6, hc);
        }
        Raylib.DrawTextEx(Cfg.Font, $"{u.Hp}/{u.MaxHp} HP", new Vector2(hpX, y + 23), 11, 1f, Pal.TxtDim);

        // KILLS lane: kills count over rank progress, stacked with proper line spacing.
        Raylib.DrawTextEx(Cfg.Font, $"{u.Kills} kills", new Vector2(killsX, y + 6), 12, 1f, benched ? Pal.TxtDim : Pal.Txt);
        int toNext = g.RunState.KillsToNext(u);
        string prog = u.Rank >= Run.Ranks.Length - 1 ? "MAX RANK" : $"{toNext} to rank up";
        Raylib.DrawTextEx(Cfg.Font, prog, new Vector2(killsX, y + 23), 11, 1f, Pal.TxtDim);

        // STATUS lane: DEPLOYED green / BENCHED grey, vertically centered next to the pill.
        // W9: a SCARRED soldier's lane shows the REHAB chip instead (buy one scar off for salvage) —
        // the deploy/bench pill on the right already carries the deploy state as its action verb.
        if (u.Scars.Count > 0)
        {
            var rr = new Rectangle(statusX - 4, y + 9, statusW + 6, 22);
            bool rCan = g.BarracksSalvage >= MetaProg.ScarRehabCost;
            Raylib.DrawRectangleRounded(rr, 0.3f, 6, rCan ? Raylib.Fade(Pal.VipGold, 0.16f) : Pal.RGBA(20, 18, 16));
            Raylib.DrawRectangleLinesEx(rr, 1f, rCan ? Pal.VipGold : Pal.RGBA(60, 54, 40));
            string rTag = $"REHAB {MetaProg.ScarRehabCost}";
            Raylib.DrawTextEx(Cfg.Font, rTag,
                new Vector2(rr.X + rr.Width / 2 - Raylib.MeasureTextEx(Cfg.Font, rTag, 11, 1f).X / 2, rr.Y + 5),
                11, 1f, rCan ? Pal.VipGold : Pal.TxtDim);
            RehabBtns.Add((u, rr));
        }
        else
        {
            string stTag = benched ? "BENCHED" : "DEPLOYED";
            Color stCol  = benched ? Pal.TxtDim : Pal.Good;
            Raylib.DrawTextEx(Cfg.Font, stTag, new Vector2(statusX, y + 14), 11, 1f, stCol);
        }

        // DEPLOY/BENCH toggle — now on EVERY soldier (Game.ToggleBench enforces >=1 deployed and
        // the deploy cap). The verb is the ACTION the click performs: a deployed soldier shows
        // "BENCH", a benched one shows "DEPLOY". Registered in BenchBtns for hit-testing.
        string pillLabel = benched ? "DEPLOY" : "BENCH";
        Color pillBg = benched ? Raylib.Fade(Pal.Good, 0.22f) : Pal.RGBA(20, 28, 40);
        Color pillFg = benched ? Pal.Good : Pal.Suspect;
        Color pillBd = benched ? Pal.Good : Pal.Suspect;
        var benchR = new Rectangle(x + w - 84, y + 9, 76, 22);
        Raylib.DrawRectangleRounded(benchR, 0.3f, 6, pillBg);
        Raylib.DrawRectangleLinesEx(benchR, 1f, pillBd);
        Raylib.DrawTextEx(Cfg.Font, pillLabel,
            new Vector2(benchR.X + benchR.Width / 2 - Raylib.MeasureTextEx(Cfg.Font, pillLabel, 11, 1f).X / 2,
                        benchR.Y + 5), 11, 1f, pillFg);
        BenchBtns.Add((u, benchR));
    }

    public static Rectangle OverlayBtn;
    public static Rectangle OverlayBtn2;   // intro CONTINUE-run button (when a save exists)
    public static Rectangle OverlayBtn3;   // intro LAST STAND (endless) button (PROGRAM HORIZON W2)
    public static Rectangle OverlayBtn4;   // intro WAR ROOM (cross-run meta) button (PROGRAM HORIZON W3)
    public static Rectangle OverlayBtn5;   // intro CODEX (field manual) button (PROGRAM HORIZON W6)
    public static Rectangle OverlayBtn6;   // intro SKIRMISH (one custom fight) button (PROGRAM HORIZON W4)
    public static Rectangle OverlayBtn7;   // intro DAILY (seeded challenge) button (PROGRAM HORIZON W4)

    // SKIRMISH setup (W4): objective cycler + heat dial + START/BACK, published by DrawSkirmishSetup.
    public static Rectangle SkirmObjPrev, SkirmObjNext, SkirmHeatMinus, SkirmHeatPlus, SkirmStart, SkirmBack;

    // WAR ROOM (W3): the BACK button + per-unlock BUY buttons, published by DrawWarRoom for hit-testing.
    public static Rectangle WarRoomBack;
    public static readonly System.Collections.Generic.List<(MetaUnlock unlock, Rectangle rect)> WarRoomBuyBtns = new();

    /// Intro Heat/Ascension selector: a side panel with a HEAT dial (+/- buttons, arrows/A-D),
    /// the unlocked ceiling, and the live list of modifiers active at the dialled level. Heat
    /// raises difficulty for a bigger intel payout; the cap rises when you WIN at it. Only
    /// affects a fresh DEPLOY (CONTINUE keeps the saved run's heat).
    static void DrawHeatSelector(Game g)
    {
        int level = Sightline.Heat.Clamp(g.PendingHeat);
        int unlocked = Sightline.Heat.Clamp(g.UnlockedHeat);

        int w = 320, x = Cfg.ScreenW - w - 40, y = 150;
        // height grows with the active-modifier list (always tall enough for the ceiling's worth)
        int rows = Math.Max(1, level);
        int assist = g.AssistPreview;
        int h = 132 + rows * 26 + 30;
        var card = new Rectangle(x, y, w, h);
        PanelShadow(card, 1f, 0.06f);
        Raylib.DrawRectangleRounded(card, 0.06f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.5f, level > 0 ? Raylib.Fade(Pal.Foe, 0.7f) : Pal.PanelBd);

        // W12 (owner-legibility): the panel's informational text sits ONE CONTRAST STEP above
        // TxtDim — this card is a decision surface on a dark backdrop, not passive chrome.
        Color heatTxt2 = Pal.RGBA(164, 178, 198);
        Color heatCol = level > 0 ? Pal.Foe : heatTxt2;
        Raylib.DrawTextEx(Cfg.Font, "HEAT / ASCENSION", new Vector2(x + 18, y + 14), 14, 1f, Pal.Accent);

        // big level readout + the -/+ stepper
        string val = level.ToString();
        Raylib.DrawTextEx(Cfg.Font, "HEAT", new Vector2(x + 18, y + 48), 16, 1f, heatTxt2);
        Raylib.DrawTextEx(Cfg.Font, val, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, val, 40, 1f).X / 2, y + 40), 40, 1f, heatCol);

        HeatMinus = new Rectangle(x + 18, y + 50, 34, 34);
        HeatPlus = new Rectangle(x + w - 52, y + 50, 34, 34);
        DrawStepper(HeatMinus, "-", level > 0);
        DrawStepper(HeatPlus, "+", level < unlocked);

        Raylib.DrawTextEx(Cfg.Font, $"MAX UNLOCKED: {unlocked}", new Vector2(x + 18, y + 92), 12, 1f, heatTxt2);
        // W11 HONEST LOSSES: the adaptive assist (repeated losses ease hostile stats at heat 0)
        // was invisible — surface it as a FIELD SUPPORT chip so the player knows help is active
        // and that a win (or dialling heat up) stands it down. Review fix: the chip REPLACES the
        // hint row — assist only exists at heat 0, where the hint is the static "standard
        // difficulty" — so the card's footprint is unchanged and it can't creep over the intro
        // briefing bullets to its left (the +26px growth used to clip the second rule's tail).
        if (assist > 0)
        {
            string fsLbl = $"FIELD SUPPORT ACTIVE ({assist})";
            float fw = Raylib.MeasureTextEx(Cfg.Font, fsLbl, 12, 1f).X + 18;
            var chip = new Rectangle(x + 18, y + 106, fw, 20);
            Raylib.DrawRectangleRounded(chip, 0.4f, 6, Raylib.Fade(Pal.Good, 0.15f));
            Raylib.DrawRectangleLinesEx(chip, 1f, Raylib.Fade(Pal.Good, 0.6f));
            CenterText(fsLbl, chip, 12, Pal.Good);
            Raylib.DrawTextEx(Cfg.Font, "wins clear it", new Vector2(x + 18 + fw + 8, y + 110), 11, 1f, heatTxt2);
        }
        else
        {
            string hint = level > 0 ? $"+{Sightline.Heat.IntelBonus(level)} intel / mission" : "standard difficulty";
            Raylib.DrawTextEx(Cfg.Font, hint, new Vector2(x + 18, y + 110), 12, 1f, level > 0 ? Pal.Good : heatTxt2);
        }

        // active modifiers (cumulative rungs 1..level)
        int my = y + 132;
        if (level == 0)
            Raylib.DrawTextEx(Cfg.Font, "No modifiers active.", new Vector2(x + 18, my), 12, 1f, heatTxt2);
        else
        {
            int i = 1;
            foreach (var mod in Sightline.Heat.Active(level))
            {
                Raylib.DrawTextEx(Cfg.Font, $"{i}.", new Vector2(x + 18, my), 12, 1f, Pal.Foe);
                Raylib.DrawTextEx(Cfg.Font, mod.Name, new Vector2(x + 40, my), 12, 1f, Pal.Txt);
                Raylib.DrawTextEx(Cfg.Font, mod.Desc, new Vector2(x + 40, my + 13), 11, 1f, heatTxt2);
                my += 26; i++;
            }
        }

        Raylib.DrawTextEx(Cfg.Font, "[<] [>] to adjust", new Vector2(x + 18, y + h - 20), 11, 1f, heatTxt2);
    }

    static void DrawStepper(Rectangle r, string sym, bool enabled)
    {
        bool hover = enabled && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Raylib.DrawRectangleRounded(r, 0.3f, 6, Raylib.Fade(hover ? Pal.RGBA(40, 30, 20) : Pal.Panel, enabled ? 1f : 0.4f));
        Raylib.DrawRectangleLinesEx(r, 1.5f, Raylib.Fade(enabled ? (hover ? Pal.Accent : Pal.PanelBd) : Pal.PanelBd, enabled ? 1f : 0.35f));
        CenterText(sym, r, 22, Raylib.Fade(enabled ? Pal.Txt : Pal.TxtDim, enabled ? 1f : 0.5f));
    }

    static void DrawOverlayButton(Rectangle r, string label, Color baseCol, string keyHint, float anim = 1f)
    {
        float a = Util.EaseOutQuad(Util.Clamp(anim, 0f, 1f));
        // entrance: fade + a few px of upward slide so it lands rather than pops
        float dy = (1f - a) * 12f;
        var rr = new Rectangle(r.X, r.Y + dy, r.Width, r.Height);
        bool hover = a > 0.7f && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);  // hit-test stays on the resting rect
        Color hi = Pal.RGBA(92, 200, 251);
        // a soft halo when hovered (premium affordance)
        if (hover) Raylib.DrawRectangleRounded(new Rectangle(rr.X - 3, rr.Y - 3, rr.Width + 6, rr.Height + 6), 0.3f, 8, Raylib.Fade(hi, 0.25f));
        Raylib.DrawRectangleRounded(rr, 0.3f, 8, Raylib.Fade(hover ? hi : baseCol, a));
        var lz = Raylib.MeasureTextEx(Cfg.Font, label, 18, 1f);
        Raylib.DrawTextEx(Cfg.Font, label, new Vector2((int)(rr.X + rr.Width / 2 - lz.X / 2), (int)(rr.Y + rr.Height / 2 - 9)), 18, 1f, Raylib.Fade(Pal.RGBA(3, 18, 26), a));
        if (keyHint != null)
        {
            // right-align the hint inside the button (measured, 6px inset) — the old fixed
            // rr.Width - 30 offset bled multi-char hints ("[Esc]") past narrow buttons' edge.
            string kh = "[" + keyHint + "]";
            float khw = Raylib.MeasureTextEx(Cfg.Font, kh, 11, 1f).X;
            Raylib.DrawTextEx(Cfg.Font, kh, new Vector2((int)(rr.X + rr.Width - khw - 6), (int)(rr.Y + rr.Height - 16)), 11, 1f, Raylib.Fade(Pal.RGBA(3, 18, 26), a));
        }
    }

    /// W12: the NEUTRAL-OUTLINE sibling of DrawOverlayButton — a dark plate + hairline border +
    /// plain-text label, for secondary/utility actions (the intro's 2x2 mode grid, BACK buttons).
    /// Same entrance behaviour + resting-rect hit-test as DrawOverlayButton; hover brightens the
    /// border/label to Friend instead of swapping the fill, so filled = primary stays unambiguous.
    static void DrawGhostButton(Rectangle r, string label, string keyHint, float anim = 1f)
    {
        float a = Util.EaseOutQuad(Util.Clamp(anim, 0f, 1f));
        float dy = (1f - a) * 12f;
        var rr = new Rectangle(r.X, r.Y + dy, r.Width, r.Height);
        bool hover = a > 0.7f && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Raylib.DrawRectangleRounded(rr, 0.3f, 8, Raylib.Fade(Pal.RGBA(13, 19, 27), 0.88f * a));
        Raylib.DrawRectangleLinesEx(rr, 1.4f, Raylib.Fade(hover ? Pal.Friend : Pal.PanelBd, a));
        var lz = Raylib.MeasureTextEx(Cfg.Font, label, 16, 1f);
        Raylib.DrawTextEx(Cfg.Font, label, new Vector2((int)(rr.X + rr.Width / 2 - lz.X / 2), (int)(rr.Y + rr.Height / 2 - 8)), 16, 1f,
            Raylib.Fade(hover ? Pal.Friend : Pal.Txt, a));
        if (keyHint != null)
        {
            string kh = "[" + keyHint + "]";
            float khw = Raylib.MeasureTextEx(Cfg.Font, kh, 11, 1f).X;
            Raylib.DrawTextEx(Cfg.Font, kh, new Vector2((int)(rr.X + rr.Width - khw - 6), (int)(rr.Y + rr.Height - 16)), 11, 1f,
                Raylib.Fade(Pal.TxtDim, 0.8f * a));
        }
    }

    // ---------------- helpers ----------------
    // Soft drop-shadow so a floating panel reads as hovering above the board terrain.
    static void PanelShadow(Rectangle r, float alpha = 1f, float round = 0.16f)
    {
        Raylib.DrawRectangleRounded(new Rectangle(r.X + 3, r.Y + 6, r.Width, r.Height), round, 6,
                                    Raylib.Fade(Pal.RGBA(0, 0, 0), 0.42f * alpha));
    }

    public static void DrawButtonRect(Rectangle r, string label, string key, bool enabled, bool selected, Color accent)
    {
        bool hover = enabled && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Color bg = selected ? Pal.RGBA(40, 34, 12) : (hover ? Pal.RGBA(22, 32, 44) : Pal.Panel);
        Raylib.DrawRectangleRounded(r, 0.22f, 6, Raylib.Fade(bg, enabled ? 1f : 0.4f));
        Color bd = selected ? Pal.Accent : (hover ? accent : Pal.PanelBd);
        Raylib.DrawRectangleLinesEx(r, 1.5f, Raylib.Fade(bd, enabled ? 1f : 0.35f));

        Color tc = selected ? Pal.Accent : (enabled ? Pal.Txt : Pal.TxtDim);
        int fs = 16;
        int lw = (int)Raylib.MeasureTextEx(Cfg.Font, label, fs, 1f).X;
        int kw = string.IsNullOrEmpty(key) ? 0 : (int)Raylib.MeasureTextEx(Cfg.Font, key, 12, 1f).X + 8;
        int startX = (int)(r.X + r.Width / 2 - (lw + kw) / 2);
        int ty = (int)(r.Y + r.Height / 2 - fs / 2);
        Raylib.DrawTextEx(Cfg.Font, label, new Vector2(startX, ty), fs, 1f, Raylib.Fade(tc, enabled ? 1f : 0.5f));
        if (!string.IsNullOrEmpty(key))
        {
            int keyX = startX + lw + 8;
            var kr = new Rectangle(keyX, r.Y + r.Height / 2 - 8, (int)Raylib.MeasureTextEx(Cfg.Font, key, 12, 1f).X + 6, 16);
            Raylib.DrawRectangleLinesEx(kr, 1f, Raylib.Fade(tc, 0.4f));
            Raylib.DrawTextEx(Cfg.Font, key, new Vector2(keyX + 3, (int)(r.Y + r.Height / 2 - 6)), 12, 1f, Raylib.Fade(tc, 0.7f));
        }
    }

    static void CenterText(string text, Rectangle r, int fs, Color c)
    {
        int w = (int)Raylib.MeasureTextEx(Cfg.Font, text, fs, 1f).X;
        Raylib.DrawTextEx(Cfg.Font, text, new Vector2((int)(r.X + r.Width / 2 - w / 2), (int)(r.Y + r.Height / 2 - fs / 2)), fs, 1f, c);
    }
}
