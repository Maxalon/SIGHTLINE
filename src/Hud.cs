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
    public static Rectangle PauseBright, PauseColorblind, PauseAutoCam;
    public static Rectangle PerkBtnA, PerkBtnB, PerkTagBtn;
    public static Rectangle HeatMinus, HeatPlus;   // intro Heat/Ascension +/- selector
    public static Rectangle[] MissionCards = new Rectangle[3];
    public static System.Collections.Generic.List<(int Id, Rectangle Rect)> NodeBtns = new();
    public static Rectangle[] ShopBtns = new Rectangle[Game.ShopName.Length];
    public static Rectangle ShopProceed;
    // bench mechanic (S3-A): toggled in the barracks debrief for wounded soldiers
    public static System.Collections.Generic.List<(Unit unit, Rectangle rect)> BenchBtns = new();

    public static void Draw(Game g)
    {
        DrawTopBar(g);
        if (g.Phase == Phase.PlayerTurn || g.Phase == Phase.EnemyTurn) DrawRoster(g);
        DrawBottomBar(g);
        DrawTooltip(g);
        if ((g.Phase == Phase.PlayerTurn || g.Phase == Phase.EnemyTurn) && g.TutorialText != null)
            DrawTutorial(g);
        DrawBanner(g);
        DrawOverlays(g);
        if (g.Paused) DrawPause(g);
        if (g.EditingTag) DrawTagEditor(g);
    }

    // Onboarding tutorial callout (3.12): a non-blocking tip card above the action bar.
    static void DrawTutorial(Game g)
    {
        string body = g.TutorialText;
        int step = g.TutStep + 1, total = Game.TutPrompts.Length;
        int w = 760, x = Cfg.ScreenW / 2 - w / 2, y = 600, pad = 16;
        // word-wrap the body at ~size 15
        var lines = WrapText(body, 15, w - pad * 2);
        int h = 40 + lines.Count * 20 + 10;
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
        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.RGBA(5, 8, 11), 0.82f));
        int w = 440, h = 665;
        int x = Cfg.ScreenW / 2 - w / 2, y = Cfg.ScreenH / 2 - h / 2;
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
        PauseColorblind = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseAutoCam    = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseAbandon    = new Rectangle(bx, by, bw, bh);

        DrawButtonRect(PauseResume, "RESUME", "ESC", true, false, Pal.Friend);
        DrawButtonRect(PauseFullscreen, Display.Fullscreen ? "FULLSCREEN: ON" : "FULLSCREEN: OFF", "F", true, !Display.Fullscreen, Pal.Accent);
        DrawButtonRect(PauseWindow, "WINDOW: " + Display.SizeLabel, "", true, false, Pal.Accent);
        DrawButtonRect(PauseMute, Audio.Enabled ? "AUDIO: ON" : "AUDIO: OFF", "M", true, !Audio.Enabled, Pal.Accent);
        DrawButtonRect(PauseShake, g.Fx.ShakeOn ? "SCREEN SHAKE: ON" : "SCREEN SHAKE: OFF", "", true, !g.Fx.ShakeOn, Pal.Accent);
        DrawButtonRect(PauseThreat, g.ShowThreatPref ? "THREAT PREVIEW: ON" : "THREAT PREVIEW: OFF", "", true, !g.ShowThreatPref, Pal.Accent);
        DrawButtonRect(PauseBright, "BRIGHTNESS: " + Display.BrightLabel, "", true, false, Pal.Accent);
        DrawButtonRect(PauseColorblind, Pal.Colorblind ? "COLORBLIND: ON" : "COLORBLIND: OFF", "", true, Pal.Colorblind, Pal.Accent);
        DrawButtonRect(PauseAutoCam, Display.AutoCam ? "AUTO-CAM: ON" : "AUTO-CAM: OFF", "", true, Display.AutoCam, Pal.Accent);
        DrawButtonRect(PauseAbandon, "ABANDON RUN", "", true, false, Pal.Foe);

        string ctl = "Wheel zoom  -  Middle-drag pan  -  [C] reset camera  -  Arrows/WASD + [Space]";
        Raylib.DrawTextEx(Cfg.Font, ctl, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, ctl, 11, 1f).X / 2, y + h - 24), 11, 1f, Pal.TxtDim);
    }

    static void DrawRoster(Game g)
    {
        RosterChips.Clear();
        int y = 70;
        foreach (var u in g.AlivePlayers())
        {
            var r = new Rectangle(8, y, 132, 58);
            bool sel = g.Selected == u;
            bool spent = g.Phase == Phase.PlayerTurn && !u.CanAct;
            float a = spent ? 0.5f : 1f;

            PanelShadow(r, a);
            Raylib.DrawRectangleRounded(r, 0.16f, 6, Raylib.Fade(sel ? Pal.RGBA(26, 36, 48) : Pal.Panel, a));
            Raylib.DrawRectangleLinesEx(r, 1.5f, Raylib.Fade(sel ? Pal.Accent : Pal.PanelBd, a));
            Raylib.DrawRectangle((int)r.X, (int)r.Y, 3, (int)r.Height, Raylib.Fade(sel ? Pal.Accent : Pal.Friend, a));

            Raylib.DrawTextEx(Cfg.Font, u.Name, new Vector2((int)r.X + 9, (int)r.Y + 5), 13, 1f, Raylib.Fade(Pal.Txt, a));
            int nameW = (int)Raylib.MeasureTextEx(Cfg.Font, u.Name, 13, 1f).X;
            if (!string.IsNullOrEmpty(u.Nickname))   // earned callsign, in quotes
                Raylib.DrawTextEx(Cfg.Font, $"\"{u.Nickname}\"", new Vector2((int)r.X + 9 + nameW + 5, (int)r.Y + 6), 11, 1f, Raylib.Fade(Pal.VipGold, a));
            // status marks (right): OW/HK, else a live BOND aura when a partner is adjacent
            if (u.OnOverwatch) Raylib.DrawTextEx(Cfg.Font, "OW", new Vector2((int)r.X + 96, (int)r.Y + 5), 11, 1f, Raylib.Fade(Pal.Accent, a));
            else if (u.Hunkered) Raylib.DrawTextEx(Cfg.Font, "HK", new Vector2((int)r.X + 96, (int)r.Y + 5), 11, 1f, Raylib.Fade(Pal.Good, a));
            else if (u.BondAura) Raylib.DrawTextEx(Cfg.Font, "BOND", new Vector2((int)r.X + 88, (int)r.Y + 5), 11, 1f, Raylib.Fade(Pal.VipGold, a));

            // hp bar
            var bar = new Rectangle(r.X + 9, r.Y + 23, 102, 6);
            Raylib.DrawRectangleRounded(bar, 0.5f, 4, Raylib.Fade(Pal.RGBA(10, 15, 21), a));
            float frac = u.MaxHp > 0 ? u.Hp / (float)u.MaxHp : 0;
            if (frac > 0)
            {
                Color hc = frac > 0.5f ? Pal.Good : (frac > 0.25f ? Pal.Accent : Pal.Foe);
                Raylib.DrawRectangleRounded(new Rectangle(bar.X, bar.Y, bar.Width * frac, bar.Height), 0.5f, 4, Raylib.Fade(hc, a));
            }
            // AP pips
            for (int i = 0; i < 2; i++)
            {
                var pip = new Rectangle(r.X + 9 + i * 22, r.Y + 34, 18, 6);
                bool on = i < u.ActionsLeft;
                Raylib.DrawRectangleRounded(pip, 0.5f, 4, Raylib.Fade(on ? Pal.Accent : Pal.RGBA(28, 39, 51), a));
            }
            Raylib.DrawTextEx(Cfg.Font, u.IsVip ? "ASSET" : u.RankName, new Vector2((int)r.X + 58, (int)r.Y + 33), 9, 1f, Raylib.Fade(u.IsVip ? Pal.VipGold : Pal.TxtDim, a));

            // role tag: WOUNDED (red) takes priority, else custom tag (cyan) / auto strengths (amber)
            if (!u.IsVip)
            {
                if (u.Wound > 0)
                    Raylib.DrawTextEx(Cfg.Font, $"WOUNDED ({u.Wound})", new Vector2((int)r.X + 9, (int)r.Y + 45), 9, 1f, Raylib.Fade(Pal.Foe, a));
                else
                {
                    var (tag, custom) = DisplayTag(u);
                    if (tag.Length > 0)
                        Raylib.DrawTextEx(Cfg.Font, tag, new Vector2((int)r.X + 9, (int)r.Y + 45), 9, 1f, Raylib.Fade(custom ? Pal.Friend : Pal.Accent, a));
                }
            }

            RosterChips.Add((r, u));
            y += 64;
        }
    }

    // ---------------- top bar ----------------
    static void DrawTopBar(Game g)
    {
        Raylib.DrawRectangleGradientV(0, 0, Cfg.ScreenW, 56, Pal.RGBA(8, 12, 17, 235), Pal.RGBA(8, 12, 17, 0));

        // turn pill
        bool playerTurn = g.Phase != Phase.EnemyTurn;
        string turnTxt = playerTurn ? "PLAYER TURN" : "ENEMY TURN";
        Color turnCol = playerTurn ? Pal.Friend : Pal.Foe;
        var pill = new Rectangle(16, 11, 168, 30);
        Raylib.DrawRectangleRounded(pill, 0.4f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(pill, 1.5f, Raylib.Fade(turnCol, 0.6f));
        CenterText(turnTxt, pill, 16, turnCol);

        // 4.4 concealment pill: while the squad is hidden it takes the MISSION slot (a
        // pulsing CONCEALED indicator); the mission counter returns the moment stealth breaks.
        bool showConcealed = g.SquadConcealed && (g.Phase == Phase.PlayerTurn || g.Phase == Phase.EnemyTurn);
        if (showConcealed)
        {
            float pulse = 0.55f + 0.45f * MathF.Sin((float)Raylib.GetTime() * 3.5f);
            var cpill = new Rectangle(192, 11, 120, 30);
            Raylib.DrawRectangleRounded(cpill, 0.4f, 8, Pal.Panel);
            Raylib.DrawRectangleLinesEx(cpill, 1.5f, Raylib.Fade(Pal.Friend, 0.5f * pulse));
            CenterText("CONCEALED", cpill, 14, Raylib.Fade(Pal.Friend, pulse));
        }
        else
        {
            Raylib.DrawTextEx(Cfg.Font, $"MISSION {g.RunState.Mission}/{Run.MaxMissions}", new Vector2(200, 19), 16, 1f, Pal.TxtDim);
        }
        string objTxt; Color objCol;
        switch (g.Objective)
        {
            case Objective.Evac: objTxt = "EXTRACT"; objCol = Pal.Good; break;
            case Objective.Hack: objTxt = $"HACK {g.HackProgress}/{Game.HackRequired}"; objCol = Pal.Accent; break;
            case Objective.Sabotage: objTxt = $"SABOTAGE {g.SabotageBlown.Count}/{g.SabotageSites.Count}"; objCol = Pal.Foe; break;
            case Objective.Escort: objTxt = "ESCORT VIP"; objCol = Pal.VipGold; break;
            case Objective.Rescue: objTxt = g.CaptiveLocked ? "RESCUE CAPTIVE" : "EXTRACT CAPTIVE"; objCol = Pal.VipGold; break;
            case Objective.Defend: objTxt = $"DEFEND {Math.Min(g.Turn, Game.DefendTurns)}/{Game.DefendTurns}"; objCol = Pal.Foe; break;
            case Objective.Decapitate: objTxt = "KILL HVT"; objCol = Pal.VipGold; break;
            default: objTxt = "ELIMINATE"; objCol = Pal.TxtDim; break;
        }
        Raylib.DrawTextEx(Cfg.Font, objTxt, new Vector2(360, 19), 16, 1f, objCol);
        // 5.4/5.5: a semantic glyph left of the objective text (shape redundancy, not hue alone)
        DrawObjectiveIcon(g.Objective, 344, 27, objCol);

        // counts (the VIP isn't a combatant, so it's excluded from the squad tally)
        int friends = g.AlivePlayers().Count(p => !p.IsVip);
        int foes = g.AliveEnemies().Count;
        DrawCounter(Cfg.ScreenW / 2 - 130, 20, Pal.Friend, $"{friends}  SQUAD");
        DrawCounter(Cfg.ScreenW / 2 + 20, 20, Pal.Foe, $"{foes}  HOSTILES");

        // optional secondary objective (3.9): green while on track, red once blown
        if (g.Secondary != SecondaryKind.None)
            Raylib.DrawTextEx(Cfg.Font, g.SecondaryHud, new Vector2(812, 19), 14, 1f, g.SecondaryOnTrack ? Pal.Good : Pal.Foe);

        // Heat/Ascension indicator (only at heat > 0, so heat 0 stays byte-identical)
        if (g.HeatLevel > 0)
        {
            var hp = new Rectangle(Cfg.ScreenW - 400, 11, 92, 30);
            Raylib.DrawRectangleRounded(hp, 0.4f, 8, Pal.Panel);
            Raylib.DrawRectangleLinesEx(hp, 1.5f, Raylib.Fade(Pal.Foe, 0.6f));
            CenterText($"HEAT {g.HeatLevel}", hp, 15, Pal.Foe);
        }

        // mute indicator
        if (!Audio.Enabled)
            Raylib.DrawTextEx(Cfg.Font, "MUTED (M)", new Vector2(Cfg.ScreenW - 290, 19), 15, 1f, Pal.TxtDim);

        // end turn (turns into a confirm prompt if soldiers still have actions)
        EndTurnRect = new Rectangle(Cfg.ScreenW - 170, 11, 150, 30);
        bool canEnd = g.IsPlayerInteractive();
        if (g.EndTurnArmed)
            DrawButtonRect(EndTurnRect, "CONFIRM?", "ENT", canEnd, true, Pal.Accent);
        else
            DrawButtonRect(EndTurnRect, "END TURN", "ENT", canEnd, false, Pal.Accent);
    }

    static void DrawCounter(int x, int y, Color dot, string text)
    {
        Raylib.DrawCircle(x, y + 7, 6, dot);
        Raylib.DrawTextEx(Cfg.Font, text, new Vector2(x + 14, y), 16, 1f, Pal.Txt);
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

        // hint
        string hint = "MOVE / FIRE by click  -  [Tab] next  -  [5] ability  -  [T] tag  -  [Esc] menu";
        int hw = (int)Raylib.MeasureTextEx(Cfg.Font, hint, 13, 1f).X;
        Raylib.DrawTextEx(Cfg.Font, hint, new Vector2(Cfg.ScreenW - hw - 24, Cfg.ScreenH - 30), 13, 1f, Pal.TxtDim);
    }

    static void DrawUnitCard(Unit u, int x, int y)
    {
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

        Add("shoot", "FIRE", "1", interactive && u != null && u.CanAct && u.Ammo > 0 && hasTargets, g.AimMode && !g.SnapShot);
        // SNAP: a 1-action, no-end-turn shot at an aim penalty (per-turn DEPTH). Selected
        // highlight only when the pending aim-mode shot is the snap variant.
        Add("snap", "SNAP", "7", interactive && u != null && u.CanAct && u.Ammo > 0 && hasTargets, g.AimMode && g.SnapShot);
        Add("grenade", "GRENADE", "4", interactive && u != null && u.CanAct && u.Grenades > 0, g.GrenadeMode);
        if (u != null && u.Ability != AbilityKind.None)
            Add("ability", u.AbilityName, "5", interactive && g.CanAbility(u), u.RunGun || u.Blitz || u.Steady);
        if (u != null && u.Item != ItemKind.None)
            Add("item", u.ItemName, "6", interactive && u.CanAct && u.ItemCharge > 0, g.ItemMode);
        Add("overwatch", "OVERWATCH", "2", interactive && u != null && u.CanAct && u.Ammo > 0, false);
        Add("hunker", "HUNKER", "3", interactive && u != null && u.CanAct, u != null && u.Hunkered);
        if (g.HasHackAction)
            Add("hack", g.HasSabotage ? "PLANT" : "HACK", "H", interactive && g.CanHack(u), false);
        Add("reload", "RELOAD", "R", interactive && u != null && u.CanAct && u.Ammo < u.Weapon.Clip, false);

        // Responsive width: fit `count` buttons (+gaps) into the bar span [bx0 .. right edge].
        float bx0 = 300, bh = 40, gap = 6;
        float right = Cfg.ScreenW - 26;                      // leave a small right margin
        int count = specs.Count;
        float bw = MathF.Min(104, (right - bx0 - gap * (count - 1)) / count);

        var btns = new System.Collections.Generic.List<UiButton>();
        float bx = bx0;
        foreach (var s in specs)
        {
            btns.Add(new UiButton
            {
                Rect = new Rectangle(bx, y + 26, bw, bh),
                Id = s.id, Label = s.label, Key = s.key, Enabled = s.enabled, Selected = s.sel,
                Accent = Pal.Friend,
            });
            bx += bw + gap;
        }

        ActionButtons = btns.ToArray();
        foreach (var b in ActionButtons)
            DrawActionButton(b);

        DrawActionHelp(g);
    }

    /// Draw one action button: background + border via DrawButtonRect, then overlay a
    /// small procedural icon in the left quarter of the button (14px zone) that uses the
    /// same text color so enabled/disabled/selected states and colorblind mode all work.
    static void DrawActionButton(UiButton b)
    {
        // First draw the standard background + border.  We still use DrawButtonRect for
        // the chrome; the label text is re-drawn below shifted right by the icon width.
        var r = b.Rect;
        bool enabled = b.Enabled;
        bool selected = b.Selected;
        string label = b.Label;
        string key   = b.Key;
        Color accent = b.Accent;

        bool hover = enabled && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Color bg = selected ? Pal.RGBA(40, 34, 12) : (hover ? Pal.RGBA(22, 32, 44) : Pal.Panel);
        Raylib.DrawRectangleRounded(r, 0.22f, 6, Raylib.Fade(bg, enabled ? 1f : 0.4f));
        Color bd = selected ? Pal.Accent : (hover ? accent : Pal.PanelBd);
        Raylib.DrawRectangleLinesEx(r, 1.5f, Raylib.Fade(bd, enabled ? 1f : 0.35f));

        // Text color (same as DrawButtonRect).
        Color tc = selected ? Pal.Accent : (enabled ? Pal.Txt : Pal.TxtDim);
        float a = enabled ? 1f : 0.5f;
        Color ic = Raylib.Fade(tc, a);   // icon color — tracks text so CB / disabled states work

        // Icon zone: left 18px of the button interior, vertically centred.
        float ix = r.X + 9f;
        float iy = r.Y + r.Height / 2f;

        DrawActionIcon(b.Id, ix, iy, ic);

        // Label + key: shifted right to clear the icon zone (left 18px).
        int fs = 16;
        int lw = (int)Raylib.MeasureTextEx(Cfg.Font, label, fs, 1f).X;
        int kw = string.IsNullOrEmpty(key) ? 0 : (int)Raylib.MeasureTextEx(Cfg.Font, key, 12, 1f).X + 8;
        // Available width after removing the icon zone (left 18px) and right padding (6px).
        int avail  = (int)r.Width - 18 - 6;
        int startX = (int)(r.X + 18 + avail / 2 - (lw + kw) / 2);
        int ty     = (int)(r.Y + r.Height / 2 - fs / 2);
        Raylib.DrawTextEx(Cfg.Font, label, new Vector2(startX, ty), fs, 1f, Raylib.Fade(tc, a));
        if (!string.IsNullOrEmpty(key))
        {
            int keyX = startX + lw + 8;
            var kr = new Rectangle(keyX, r.Y + r.Height / 2 - 8, (int)Raylib.MeasureTextEx(Cfg.Font, key, 12, 1f).X + 6, 16);
            Raylib.DrawRectangleLinesEx(kr, 1f, Raylib.Fade(tc, 0.4f));
            Raylib.DrawTextEx(Cfg.Font, key, new Vector2(keyX + 3, (int)(r.Y + r.Height / 2 - 6)), 12, 1f, Raylib.Fade(tc, 0.7f));
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
    static void DrawActionHelp(Game g)
    {
        if (ActionButtons == null) return;
        var m = Raylib.GetMousePosition();
        foreach (var b in ActionButtons)
        {
            if (!Raylib.CheckCollisionPointRec(m, b.Rect)) continue;
            string desc = ActionDesc(g, b.Id);
            if (string.IsNullOrEmpty(desc)) return;
            string title = b.Label;
            int w = Math.Max((int)Raylib.MeasureTextEx(Cfg.Font, title, 14, 1f).X, (int)Raylib.MeasureTextEx(Cfg.Font, desc, 12, 1f).X) + 20;
            int h = 50;
            int x = (int)(b.Rect.X + b.Rect.Width / 2 - w / 2);
            int y = (int)b.Rect.Y - h - 8;
            x = Util.Clamp(x, 8, Cfg.ScreenW - w - 8);
            var box = new Rectangle(x, y, w, h);
            Raylib.DrawRectangleRounded(box, 0.14f, 6, Pal.RGBA(10, 14, 19, 252));
            Raylib.DrawRectangleLinesEx(box, 1.2f, Pal.Friend);
            Raylib.DrawTextEx(Cfg.Font, title, new Vector2(x + 10, y + 8), 14, 1f, Pal.Accent);
            Raylib.DrawTextEx(Cfg.Font, desc, new Vector2(x + 10, y + 28), 12, 1f, Pal.Txt);
            return;
        }
    }

    static string ActionDesc(Game g, string id)
    {
        switch (id)
        {
            case "shoot": return "Aimed shot at a target in range + line of sight. Full aim, ends the turn.";
            case "snap": return $"Snap shot: costs 1 action and does NOT end the turn, but at {Game.SnapAim} aim. Fire and keep acting.";
            case "grenade": return "Lob a grenade: AoE that ignores cover, hits both teams, clears low cover.";
            case "overwatch": return "Watch: fire a reaction shot at the first foe that moves in sight.";
            case "hunker": return "Hunker down for extra cover defense; you can't be crit.";
            case "hack": return g.HasSabotage
                ? $"Plant a demolition charge on an adjacent site ({g.SabotageBlown.Count}/{g.SabotageSites.Count} set). Costs 1 action."
                : $"Work the terminal ({g.HackProgress}/{Game.HackRequired} done). Costs 1 action.";
            case "reload": return "Reload your weapon to full.";
            case "ability":
                return g.Selected != null && g.Selected.Ability != AbilityKind.None
                    ? g.Selected.AbilityDesc + "  (1 charge/mission)"
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
        var flags = new System.Collections.Generic.List<(string text, Color col)>();
        // — already in the odds struct —
        if (o.Flanked)   flags.Add(("! FLANKED", Pal.Accent));
        if (o.Hunkered)  flags.Add(("- HUNKERED", Pal.Foe));          // target dug in: -25 aim, no crit
        if (o.HighGround) flags.Add(("+ HIGH GROUND", Pal.Good));
        if (o.SeesOver)  flags.Add(("+ OVER LOW COVER", Pal.Good));
        if (o.Partial)   flags.Add(("~ PARTIAL COVER", Pal.TxtDim));
        if (o.Steady)    flags.Add(("+ STEADY", Pal.Good));
        if (o.Ambush)    flags.Add(("+ AMBUSH", Pal.Good));
        // snap-fire penalty: this aim-mode shot is the cheap 1-action variant (the HitChance
        // shown is already reduced by SnapAim). RUN&GUN is the free version, so no badge then.
        if (g.AimMode && g.SnapShot && a != null && !a.RunGun) flags.Add(($"SNAP {Game.SnapAim}", Pal.Foe));

        // — modifiers that read live attacker/target state (mirror Combat.ComputeOdds) —
        if (a != null && d != null)
        {
            float dist = Util.TileDist(a.X, a.Y, d.X, d.Y);
            bool tgtHurt    = d.MaxHp > 0 && d.Hp * 2 <= d.MaxHp;   // at/below half HP (Killer)
            bool tgtSubHalf = d.MaxHp > 0 && d.Hp * 2 <  d.MaxHp;   // strictly below half (Executioner)
            bool selfHurt   = a.MaxHp > 0 && a.Hp * 2 <= a.MaxHp;   // attacker bloodied (Cold Blood)

            // attacker advantages (green) — each shown only when its condition holds THIS shot
            if (a.BondAura)                                   flags.Add(("+ BOND", Pal.Good));
            if (a.HasTrait(Trait.Killer) && tgtHurt)          flags.Add(("+ KILLER", Pal.Good));
            if (a.HasTrait(Trait.Vengeful) && a.AllyDown)     flags.Add(("+ VENGEFUL", Pal.Good));
            if (a.HasTrait(Trait.ColdBlood) && selfHurt)      flags.Add(("+ COLD BLOOD", Pal.Good));
            if (a.HasPerk(Perk.LockOn) && o.CoverLevel == 0)  flags.Add(("+ LOCK-ON", Pal.Good));
            if (a.HasPerk(Perk.CloseQuarters) && dist <= Unit.CloseRange) flags.Add(("+ CLOSE QTRS", Pal.Good));
            if (a.HasPerk(Perk.Marksman) && dist >= Unit.LongRange)       flags.Add(("+ MARKSMAN", Pal.Good));
            if (a.HasPerk(Perk.Deadeye))                      flags.Add(("+ DEADEYE", Pal.Good));
            if (a.HasPerk(Perk.Executioner) && tgtSubHalf)    flags.Add(("+ EXECUTIONER", Pal.Good));
            if (a.HasPerk(Perk.Opportunist) && o.Flanked)     flags.Add(("+ OPPORTUNIST", Pal.Good));
            if (a.HasPerk(Perk.PointBlank) && dist <= Unit.PointBlankRange) flags.Add(("+ POINT BLANK", Pal.Good));
            if (a.HasPerk(Perk.GiantSlayer) && d.MaxHp > 0 && d.Hp >= d.MaxHp) flags.Add(("+ FIRST STRIKE", Pal.Good));

            // attacker penalties (red) — these quietly drag the hit% down
            if (a.Suppress > 0)                               flags.Add(("- SUPPRESSED", Pal.Foe));
            if (a.Wound > 0)                                  flags.Add(("- WOUNDED", Pal.Foe));
            if (a.HasStatus(StatusKind.Disoriented))          flags.Add(("- DISORIENTED", Pal.Foe));

            // target obscured in smoke (it's shootable — LoS clears the endpoint tile —
            // but harder to make out). Neutral tag: smoke is not in the hit% math.
            if (g.Grid.IsSmoke(d.X, d.Y))                     flags.Add(("~ SMOKED", Pal.TxtDim));
        }

        // Layout: one column when short, two when the badge list gets long, so a heavily
        // perked veteran's tooltip stays compact instead of running tall.
        const int hdr = 72, lineH = 17, fontFlag = 12, pad = 12;
        bool twoCol = flags.Count > 5;
        int rows = twoCol ? (flags.Count + 1) / 2 : flags.Count;
        int colW = 132;                                    // width that fits "+ OVER LOW COVER" at 12px
        int w = twoCol ? colW * 2 - 8 : 150;
        int h = hdr + rows * lineH + (flags.Count > 0 ? 6 : 2);

        var m = Raylib.GetMousePosition();
        int x = (int)m.X - w / 2;
        int y = (int)m.Y - h - 18;
        x = Util.Clamp(x, 8, Cfg.ScreenW - w - 8);
        y = Util.Clamp(y, 64, Cfg.ScreenH - h - 8);

        var box = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(box, 0.12f, 8, Pal.RGBA(10, 14, 19, 245));
        Raylib.DrawRectangleLinesEx(box, 1.5f, Pal.Foe);

        Raylib.DrawTextEx(Cfg.Font, "HIT", new Vector2(x + pad, y + 10), 12, 1f, Pal.TxtDim);
        string hit = $"{o.HitChance}%";
        Raylib.DrawTextEx(Cfg.Font, hit, new Vector2(x + w - (int)Raylib.MeasureTextEx(Cfg.Font, hit, 22, 1f).X - pad, y + 7), 22, 1f, Pal.Good);

        Raylib.DrawTextEx(Cfg.Font, "CRIT", new Vector2(x + pad, y + 34), 12, 1f, Pal.TxtDim);
        string crit = $"{o.CritChance}%";
        Raylib.DrawTextEx(Cfg.Font, crit, new Vector2(x + w - (int)Raylib.MeasureTextEx(Cfg.Font, crit, 16, 1f).X - pad, y + 32), 16, 1f, Pal.Accent);

        Raylib.DrawTextEx(Cfg.Font, "DMG", new Vector2(x + pad, y + 54), 12, 1f, Pal.TxtDim);
        string dmg = $"{o.DmgMin}-{o.DmgMax}";
        Raylib.DrawTextEx(Cfg.Font, dmg, new Vector2(x + w - (int)Raylib.MeasureTextEx(Cfg.Font, dmg, 16, 1f).X - pad, y + 52), 16, 1f, Pal.Foe);

        // a hairline above the badges separates them from the headline numbers
        if (flags.Count > 0)
            Raylib.DrawRectangle(x + pad, y + hdr - 6, w - pad * 2, 1, Pal.RGBA(38, 49, 63, 200));

        for (int i = 0; i < flags.Count; i++)
        {
            int col = twoCol ? i % 2 : 0;
            int row = twoCol ? i / 2 : i;
            int fx = x + pad + col * (colW - 4);
            int fyy = y + hdr + row * lineH;
            Raylib.DrawTextEx(Cfg.Font, flags[i].text, new Vector2(fx, fyy), fontFlag, 1f, flags[i].col);
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
        Raylib.DrawTextEx(Cfg.Font, g.BannerText, new Vector2(Cfg.ScreenW / 2 - tw / 2, bandY + 24), fs, 1f, Raylib.Fade(c, a));
    }

    // ---------------- overlays ----------------
    static void DrawOverlays(Game g)
    {
        if (g.Phase == Phase.Intro)
        {
            DrawCenterCard(g, "SIGHTLINE", "TURN-BASED SQUAD TACTICS", Pal.Friend,
                new[]{
                    $"Lead one squad through {Run.MaxMissions} escalating missions.",
                    "2 actions per soldier - move, then fire (firing ends the turn).",
                    "Stand beside cover to cut enemy aim. Get flanked and you're exposed.",
                    "Seize the high ground (raised tiles) for an aim + crit edge.",
                    "Each class has a signature ability (key 5): Run&Gun, Blitz,",
                    "   Steady, Suppress - one charge per mission.",
                    "Kills earn promotions: better aim, more HP, more mobility.",
                    "Survivors carry their wounds and ranks to the next mission.",
                }, "DEPLOY SQUAD", SaveGame.Exists ? "CONTINUE RUN" : null);
            DrawHeatSelector(g);
        }
        else if (g.Phase == Phase.Barracks)
            DrawBarracks(g);
        else if (g.Phase == Phase.Win)
            DrawCenterCard(g, "CAMPAIGN COMPLETE", $"All {Run.MaxMissions} missions cleared. The squad stands victorious.",
                Pal.Good, null, "NEW RUN");
        else if (g.Phase == Phase.Lose)
            DrawCenterCard(g, string.IsNullOrEmpty(g.LoseTitle) ? "RUN OVER" : g.LoseTitle,
                string.IsNullOrEmpty(g.LoseReason) ? $"The squad fell on mission {g.RunState.Mission}." : g.LoseReason,
                Pal.Foe, null, "NEW RUN");
    }

    static void DrawBarracks(Game g)
    {
        var run = g.RunState;
        BenchBtns.Clear();   // clear before the shop/perk early-returns so no stale rects linger
        if (!g.ShopDone) { DrawRequisition(g); return; }
        if (run.PendingPerks.Count > 0) { DrawPerkChooser(g, run.PendingPerks[0]); return; }
        var squad = run.Squad;
        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.RGBA(5, 8, 11), 0.85f));

        int w = 700;
        int rows = squad.Count;
        int h = 150 + rows * 46 + Math.Min(run.Report.Count, 5) * 22 + 220;
        int x = Cfg.ScreenW / 2 - w / 2;
        int y = Cfg.ScreenH / 2 - h / 2;
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

        int ry = y + 100;
        foreach (var u in squad)
        {
            DrawSquadRow(g, u, x + 30, ry, w - 60);
            ry += 46;
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
        if (run.Map.Count > 0 && run.NextNodes().Count > 0)
        {
            string pick = "CAMPAIGN MAP  >  SELECT NEXT OPERATION";
            Raylib.DrawTextEx(Cfg.Font, pick, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, pick, 15, 1f).X / 2, y + h - 162), 15, 1f, Pal.Accent);
            DrawCampaignMap(run, new Rectangle(x + 24, y + h - 140, w - 48, 124));
        }
        else  // fallback: legacy deployment cards (only if the map is unavailable)
        {
            string pick = $"SELECT DEPLOYMENT  >  MISSION {run.Mission + 1}";
            Raylib.DrawTextEx(Cfg.Font, pick, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, pick, 15, 1f).X / 2, y + h - 158), 15, 1f, Pal.Accent);
            int cw = (w - 60 - 32) / 3, ch = 118, cy = y + h - 134, gap = 16;
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
        _ => Pal.Friend,
    };

    static string NodeGlyph(NodeKind k) => k switch
    {
        NodeKind.Start => "S", NodeKind.Elite => "!", NodeKind.Supply => "+", NodeKind.Boss => "X", _ => "*",
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
        foreach (var n in run.Map)
        {
            Vector2 p = Center(n);
            bool isCur = cur != null && n.Id == cur.Id;
            bool canPick = reachable.Contains(n.Id);
            float rad = n.Kind == NodeKind.Boss ? 13f : 10f;
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
            Raylib.DrawTextEx(Cfg.Font, gly, new Vector2((int)(p.X - (int)Raylib.MeasureTextEx(Cfg.Font, gly, 14, 1f).X / 2), (int)(p.Y - 7)), 14, 1f, Pal.RGBA(8, 12, 18));

            if (canPick)  // label the choices with their objective + an intel hint
            {
                string lbl = ObjName(n.Card.Objective);
                Raylib.DrawTextEx(Cfg.Font, lbl, new Vector2((int)(p.X - (int)Raylib.MeasureTextEx(Cfg.Font, lbl, 10, 1f).X / 2), (int)(p.Y + rad + 3)), 10, 1f, Pal.Txt);
                // enemy intel hint: a short flavour line so the pick is informed
                string hint = Run.EnemyHint(n);
                Raylib.DrawTextEx(Cfg.Font, hint, new Vector2((int)(p.X - (int)Raylib.MeasureTextEx(Cfg.Font, hint, 9, 1f).X / 2), (int)(p.Y + rad + 15)), 9, 1f, Pal.TxtDim);
            }
        }

        // hover tooltip: the chosen op's flavour (mod / objective / force / reward / enemy hint)
        if (hovered != null)
        {
            var c = hovered.Card;
            string l1 = $"{c.ModName}  -  {ObjName(c.Objective)}";
            string l2 = c.EnemyDelta > 0 ? "Heavy resistance" : (c.EnemyDelta < 0 ? "Light resistance" : "Standard force");
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

        int items = Game.ShopName.Length;
        int ih = 80, gap = 10;
        int squadH = 40;
        int w = 560, h = 104 + squadH + items * (ih + gap) + 60;
        int x = Cfg.ScreenW / 2 - w / 2, y = Cfg.ScreenH / 2 - h / 2;
        var card = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(card, 0.04f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.5f, Pal.PanelBd);

        string title = "REQUISITION";
        Raylib.DrawTextEx(Cfg.Font, title, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, title, 36, 1f).X / 2, y + 24), 36, 1f, Pal.Accent);
        string intel = $"INTEL AVAILABLE: {run.Intel}";
        Raylib.DrawTextEx(Cfg.Font, intel, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, intel, 16, 1f).X / 2, y + 66), 16, 1f, Pal.Good);

        // squad HP strip so the player can judge whether a heal/stim is worth it
        DrawSquadHpStrip(run.Squad, x + 30, y + 96, w - 60);

        int iy = y + 104 + squadH;
        for (int i = 0; i < items; i++)
        {
            var r = new Rectangle(x + 30, iy, w - 60, ih);
            ShopBtns[i] = r;
            bool can = g.CanBuy(i);
            bool hover = can && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
            Raylib.DrawRectangleRounded(r, 0.1f, 6, hover ? Pal.RGBA(24, 34, 46) : Pal.RGBA(14, 20, 28));
            Raylib.DrawRectangleLinesEx(r, 1.5f, can ? (hover ? Pal.Accent : Pal.PanelBd) : Pal.RGBA(40, 46, 54));
            Color txt = can ? Pal.Txt : Pal.TxtDim;
            Raylib.DrawTextEx(Cfg.Font, Game.ShopName[i], new Vector2((int)r.X + 14, (int)r.Y + 10), 18, 1f, txt);
            Raylib.DrawTextEx(Cfg.Font, Game.ShopDesc[i], new Vector2((int)r.X + 14, (int)r.Y + 35), 12, 1f, Pal.TxtDim);
            Raylib.DrawTextEx(Cfg.Font, g.ShopEffect(i), new Vector2((int)r.X + 14, (int)r.Y + 55), 12, 1f, can ? Pal.Accent : Pal.TxtDim);  // concrete effect
            string cost = $"{Game.ShopCost[i]} INTEL";
            Color cc = run.Intel >= Game.ShopCost[i] ? Pal.Good : Pal.Foe;
            Raylib.DrawTextEx(Cfg.Font, cost, new Vector2((int)(r.X + r.Width - (int)Raylib.MeasureTextEx(Cfg.Font, cost, 16, 1f).X - 14), (int)r.Y + 12), 16, 1f, cc);
            if (!can)
                Raylib.DrawTextEx(Cfg.Font, "- unavailable -", new Vector2((int)(r.X + r.Width - (int)Raylib.MeasureTextEx(Cfg.Font, "- unavailable -", 11, 1f).X - 14), (int)r.Y + 52), 11, 1f, Pal.TxtDim);
            else
                Raylib.DrawTextEx(Cfg.Font, "[ BUY ]", new Vector2((int)(r.X + r.Width - (int)Raylib.MeasureTextEx(Cfg.Font, "[ BUY ]", 12, 1f).X - 14), (int)r.Y + 54), 12, 1f, Pal.Accent);
            iy += ih + gap;
        }

        ShopProceed = new Rectangle(x + w / 2 - 130, y + h - 60, 260, 44);
        bool ph = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), ShopProceed);
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
        var card = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(card, 0.04f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.5f, Pal.PanelBd);

        string title = "PROMOTION";
        Raylib.DrawTextEx(Cfg.Font, title, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, title, 36, 1f).X / 2, y + 22), 36, 1f, Pal.Accent);
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
        DrawPerkCard(PerkBtnA, off.A);
        DrawPerkCard(PerkBtnB, off.B);

        int left = g.RunState.PendingPerks.Count - 1;
        string foot = left > 0 ? $"{left} more promotion(s) to assign" : "Click a perk to continue";
        Raylib.DrawTextEx(Cfg.Font, foot, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, foot, 12, 1f).X / 2, y + h - 26), 12, 1f, Pal.TxtDim);
    }

    /// A soldier's stat line + current perks + derived strengths (for decision screens).
    static void DrawDossier(Unit u, int x, int y, int w)
    {
        string stats = $"HP {u.Hp}/{u.MaxHp}    AIM {u.Aim}    MOB {u.Mobility}    {u.Weapon.Name}    GREN {1 + u.BonusGrenades}/mission    {u.AbilityName}";
        Raylib.DrawTextEx(Cfg.Font, stats, new Vector2(x, y), 13, 1f, Pal.Txt);
        string perks = u.Perks.Count == 0 ? "Perks: none yet"
            : "Perks: " + string.Join(", ", u.Perks.ConvertAll(PerkDef.Name));
        Raylib.DrawTextEx(Cfg.Font, perks, new Vector2(x, y + 22), 13, 1f, Pal.Good);

        // earned traits + bonds (3.2): what makes this veteran distinct
        string traits = u.Traits.Count == 0 ? "Traits: none yet"
            : "Traits: " + string.Join(", ", u.Traits.ConvertAll(TraitDef.Name));
        if (u.Bonds.Count > 0) traits += "    Bonds: " + string.Join(", ", u.Bonds);
        Raylib.DrawTextEx(Cfg.Font, traits, new Vector2(x, y + 44), 12, 1f, u.Traits.Count == 0 && u.Bonds.Count == 0 ? Pal.TxtDim : Pal.VipGold);

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
        if (t.Count == 0)   // class-role fallback so every soldier reads with a strength
            t.Add(u.Cls switch
            {
                "GUNNER" => "SUPPRESS",
                "ASSAULT" => "ASSAULT",
                "RANGER" => "CLOSE",
                "SHARPSHOOTER" => "SHARP",
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
            Raylib.DrawTextEx(Cfg.Font, u.Name, new Vector2(cx, y), 11, 1f, Pal.Txt);
            var bar = new Rectangle(cx, y + 15, cw - 14, 7);
            Raylib.DrawRectangleRounded(bar, 0.5f, 4, Pal.RGBA(10, 15, 21));
            float frac = u.MaxHp > 0 ? u.Hp / (float)u.MaxHp : 0;
            if (frac > 0)
            {
                Color hc = frac > 0.5f ? Pal.Good : (frac > 0.25f ? Pal.Accent : Pal.Foe);
                Raylib.DrawRectangleRounded(new Rectangle(bar.X, bar.Y, bar.Width * frac, bar.Height), 0.5f, 4, hc);
            }
            Raylib.DrawTextEx(Cfg.Font, $"{u.Hp}/{u.MaxHp}", new Vector2(cx, y + 25), 10, 1f, Pal.TxtDim);
        }
    }

    static void DrawPerkCard(Rectangle r, Perk p)
    {
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Raylib.DrawRectangleRounded(r, 0.08f, 8, hover ? Pal.RGBA(24, 34, 46) : Pal.RGBA(14, 20, 28));
        Raylib.DrawRectangleLinesEx(r, 1.5f, hover ? Pal.Accent : Pal.PanelBd);
        Raylib.DrawRectangle((int)r.X, (int)r.Y, 4, (int)r.Height, hover ? Pal.Accent : Pal.Friend);

        string name = PerkDef.Name(p);
        Raylib.DrawTextEx(Cfg.Font, name, new Vector2((int)(r.X + r.Width / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, name, 22, 1f).X / 2), (int)r.Y + 28), 22, 1f, hover ? Pal.Accent : Pal.Txt);
        // word-wrapped one-line description (kept short by design)
        string desc = PerkDef.Desc(p);
        Raylib.DrawTextEx(Cfg.Font, desc, new Vector2((int)(r.X + r.Width / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, desc, 14, 1f).X / 2), (int)r.Y + 78), 14, 1f, Pal.TxtDim);

        Raylib.DrawTextEx(Cfg.Font, "SELECT", new Vector2((int)(r.X + r.Width / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, "SELECT", 13, 1f).X / 2), (int)(r.Y + r.Height - 32)), 13, 1f, hover ? Pal.Accent : Pal.TxtDim);
    }

    static void DrawSquadRow(Game g, Unit u, int x, int y, int w)
    {
        Raylib.DrawRectangleRounded(new Rectangle(x, y, w, 40), 0.2f, 6, Pal.RGBA(13, 19, 27));
        Raylib.DrawRectangle(x, y, 3, 40, Pal.Friend);

        Raylib.DrawTextEx(Cfg.Font, u.Name, new Vector2(x + 14, y + 5), 18, 1f, u.Benched ? Pal.TxtDim : Pal.Txt);
        string rankLine = $"{u.RankName}  -  {u.Cls}";
        if (u.Wound > 0) rankLine += $"  WOUNDED({u.Wound})";
        if (u.Benched)   rankLine += "  [SITTING OUT]";
        Raylib.DrawTextEx(Cfg.Font, rankLine, new Vector2(x + 14, y + 24), 11, 1f, u.Wound > 0 ? Pal.Foe : Pal.Accent);

        // earned perks (compact 3-letter codes)
        if (u.Perks.Count > 0)
        {
            string codes = string.Join(" ", u.Perks.ConvertAll(PerkDef.Code));
            Raylib.DrawTextEx(Cfg.Font, codes, new Vector2(x + 220, y + 27), 9, 1f, Pal.Good);
        }

        // HP bar
        var bar = new Rectangle(x + 220, y + 13, 150, 12);
        Raylib.DrawRectangleRounded(bar, 0.5f, 6, Pal.RGBA(10, 15, 21));
        float frac = u.MaxHp > 0 ? u.Hp / (float)u.MaxHp : 0;
        if (frac > 0)
        {
            Color hc = frac > 0.5f ? Pal.Good : (frac > 0.25f ? Pal.Accent : Pal.Foe);
            Raylib.DrawRectangleRounded(new Rectangle(bar.X, bar.Y, bar.Width * frac, bar.Height), 0.5f, 6, hc);
        }
        Raylib.DrawTextEx(Cfg.Font, $"{u.Hp}/{u.MaxHp} HP", new Vector2(x + 380, y + 13), 12, 1f, Pal.TxtDim);

        // kills + progress — shifted left to make room for BENCH button when wounded
        int killsX = u.Wound > 0 ? x + w - 260 : x + w - 170;
        Raylib.DrawTextEx(Cfg.Font, $"{u.Kills} kills", new Vector2(killsX, y + 6), 12, 1f, Pal.Txt);
        int toNext = g.RunState.KillsToNext(u);
        string prog = u.Rank >= Run.Ranks.Length - 1 ? "MAX RANK" : $"{toNext} to next rank";
        Raylib.DrawTextEx(Cfg.Font, prog, new Vector2(killsX, y + 23), 11, 1f, Pal.TxtDim);

        // BENCH toggle: shown only for wounded soldiers; benched = warm amber, unbenched = dim outline
        if (u.Wound > 0)
        {
            string benchLabel = u.Benched ? "BENCHED" : " BENCH ";
            Color benchBg   = u.Benched ? Pal.Accent : Pal.RGBA(20, 28, 40);
            Color benchFg   = u.Benched ? Pal.Panel  : Pal.TxtDim;
            Color benchBd   = u.Benched ? Pal.Accent : Pal.PanelBd;
            var benchR = new Rectangle(x + w - 84, y + 9, 76, 22);
            Raylib.DrawRectangleRounded(benchR, 0.3f, 6, benchBg);
            Raylib.DrawRectangleLinesEx(benchR, 1f, benchBd);
            Raylib.DrawTextEx(Cfg.Font, benchLabel,
                new Vector2(benchR.X + benchR.Width / 2 - Raylib.MeasureTextEx(Cfg.Font, benchLabel, 11, 1f).X / 2,
                            benchR.Y + 5), 11, 1f, benchFg);
            BenchBtns.Add((u, benchR));
        }
    }

    public static Rectangle OverlayBtn;
    public static Rectangle OverlayBtn2;   // intro CONTINUE-run button (when a save exists)

    static void DrawCenterCard(Game g, string title, string sub, Color titleCol, string[] rules, string btn,
                               string secondBtn = null)
    {
        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.RGBA(5, 8, 11), 0.84f));
        // size the card to the widest rule so text never clips (rules sit at x+58 + right pad)
        int w = 540;
        if (rules != null)
        {
            int maxRule = 0;
            foreach (var r in rules) maxRule = Math.Max(maxRule, (int)Raylib.MeasureTextEx(Cfg.Font, r, 15, 1f).X);
            w = Math.Max(w, maxRule + 58 + 30);
        }
        int h = rules != null ? 152 + rules.Length * 30 + 70 : 240;
        int x = Cfg.ScreenW / 2 - w / 2, y = Cfg.ScreenH / 2 - h / 2;
        var card = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(card, 0.06f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.5f, Pal.PanelBd);

        int tfs = 46;
        Raylib.DrawTextEx(Cfg.Font, title, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, title, tfs, 1f).X / 2, y + 34), tfs, 1f, titleCol);
        Raylib.DrawTextEx(Cfg.Font, sub, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, sub, 14, 1f).X / 2, y + 90), 14, 1f, Pal.TxtDim);

        if (rules != null)
        {
            int ry = y + 130;
            foreach (var r in rules)
            {
                Raylib.DrawTextEx(Cfg.Font, ">", new Vector2(x + 40, ry), 16, 1f, Pal.Friend);
                Raylib.DrawTextEx(Cfg.Font, r, new Vector2(x + 58, ry), 15, 1f, Pal.TxtDim);
                ry += 30;
            }
        }

        int by = y + h - 70;
        if (secondBtn != null)   // two side-by-side buttons: CONTINUE (left) + new run (right)
        {
            int bw = 210, gap = 20;
            OverlayBtn2 = new Rectangle(x + w / 2 - bw - gap / 2, by, bw, 48);
            OverlayBtn = new Rectangle(x + w / 2 + gap / 2, by, bw, 48);
            DrawOverlayButton(OverlayBtn2, secondBtn, Pal.Good, "C");
            DrawOverlayButton(OverlayBtn, btn, Pal.Friend, null);
        }
        else
        {
            OverlayBtn = new Rectangle(x + w / 2 - 110, by, 220, 48);
            OverlayBtn2 = new Rectangle(0, 0, 0, 0);
            DrawOverlayButton(OverlayBtn, btn, Pal.Friend, null);
        }
    }

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
        int h = 132 + rows * 26 + 30;
        var card = new Rectangle(x, y, w, h);
        PanelShadow(card, 1f, 0.06f);
        Raylib.DrawRectangleRounded(card, 0.06f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.5f, level > 0 ? Raylib.Fade(Pal.Foe, 0.7f) : Pal.PanelBd);

        Color heatCol = level > 0 ? Pal.Foe : Pal.TxtDim;
        Raylib.DrawTextEx(Cfg.Font, "HEAT / ASCENSION", new Vector2(x + 18, y + 14), 14, 1f, Pal.Accent);

        // big level readout + the -/+ stepper
        string val = level.ToString();
        Raylib.DrawTextEx(Cfg.Font, "HEAT", new Vector2(x + 18, y + 48), 16, 1f, Pal.TxtDim);
        Raylib.DrawTextEx(Cfg.Font, val, new Vector2(x + w / 2 - (int)Raylib.MeasureTextEx(Cfg.Font, val, 40, 1f).X / 2, y + 40), 40, 1f, heatCol);

        HeatMinus = new Rectangle(x + 18, y + 50, 34, 34);
        HeatPlus = new Rectangle(x + w - 52, y + 50, 34, 34);
        DrawStepper(HeatMinus, "-", level > 0);
        DrawStepper(HeatPlus, "+", level < unlocked);

        Raylib.DrawTextEx(Cfg.Font, $"MAX UNLOCKED: {unlocked}", new Vector2(x + 18, y + 92), 12, 1f, Pal.TxtDim);
        string hint = level > 0 ? $"+{Sightline.Heat.IntelBonus(level)} intel / mission" : "standard difficulty";
        Raylib.DrawTextEx(Cfg.Font, hint, new Vector2(x + 18, y + 110), 12, 1f, level > 0 ? Pal.Good : Pal.TxtDim);

        // active modifiers (cumulative rungs 1..level)
        int my = y + 132;
        if (level == 0)
            Raylib.DrawTextEx(Cfg.Font, "No modifiers active.", new Vector2(x + 18, my), 12, 1f, Pal.TxtDim);
        else
        {
            int i = 1;
            foreach (var mod in Sightline.Heat.Active(level))
            {
                Raylib.DrawTextEx(Cfg.Font, $"{i}.", new Vector2(x + 18, my), 12, 1f, Pal.Foe);
                Raylib.DrawTextEx(Cfg.Font, mod.Name, new Vector2(x + 40, my), 12, 1f, Pal.Txt);
                Raylib.DrawTextEx(Cfg.Font, mod.Desc, new Vector2(x + 40, my + 13), 11, 1f, Pal.TxtDim);
                my += 26; i++;
            }
        }

        Raylib.DrawTextEx(Cfg.Font, "[<] [>] to adjust", new Vector2(x + 18, y + h - 20), 11, 1f, Pal.TxtDim);
    }

    static void DrawStepper(Rectangle r, string sym, bool enabled)
    {
        bool hover = enabled && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Raylib.DrawRectangleRounded(r, 0.3f, 6, Raylib.Fade(hover ? Pal.RGBA(40, 30, 20) : Pal.Panel, enabled ? 1f : 0.4f));
        Raylib.DrawRectangleLinesEx(r, 1.5f, Raylib.Fade(enabled ? (hover ? Pal.Accent : Pal.PanelBd) : Pal.PanelBd, enabled ? 1f : 0.35f));
        CenterText(sym, r, 22, Raylib.Fade(enabled ? Pal.Txt : Pal.TxtDim, enabled ? 1f : 0.5f));
    }

    static void DrawOverlayButton(Rectangle r, string label, Color baseCol, string keyHint)
    {
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Color hi = Pal.RGBA(92, 200, 251);
        Raylib.DrawRectangleRounded(r, 0.3f, 8, hover ? hi : baseCol);
        CenterText(label, r, 18, Pal.RGBA(3, 18, 26));
        if (keyHint != null)
            Raylib.DrawTextEx(Cfg.Font, "[" + keyHint + "]", new Vector2((int)(r.X + r.Width - 30), (int)(r.Y + r.Height - 16)), 11, 1f, Pal.RGBA(3, 18, 26));
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
