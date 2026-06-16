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
    public static Rectangle PerkBtnA, PerkBtnB, PerkTagBtn;
    public static Rectangle[] MissionCards = new Rectangle[3];
    public static System.Collections.Generic.List<(int Id, Rectangle Rect)> NodeBtns = new();
    public static Rectangle[] ShopBtns = new Rectangle[Game.ShopName.Length];
    public static Rectangle ShopProceed;

    public static void Draw(Game g)
    {
        DrawTopBar(g);
        if (g.Phase == Phase.PlayerTurn || g.Phase == Phase.EnemyTurn) DrawRoster(g);
        DrawBottomBar(g);
        DrawTooltip(g);
        DrawBanner(g);
        DrawOverlays(g);
        if (g.Paused) DrawPause(g);
        if (g.EditingTag) DrawTagEditor(g);
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
        Raylib.DrawText($"TAG  {who}", x + w / 2 - Raylib.MeasureText($"TAG  {who}", 22) / 2, y + 20, 22, Pal.Accent);

        var box = new Rectangle(x + 30, y + 64, w - 60, 40);
        Raylib.DrawRectangleRounded(box, 0.2f, 6, Pal.RGBA(10, 15, 21));
        Raylib.DrawRectangleLinesEx(box, 1.5f, Pal.PanelBd);
        string shown = g.TagBuffer + (((int)(Raylib.GetTime() * 2) % 2 == 0) ? "_" : " ");
        Raylib.DrawText(shown, (int)box.X + 12, (int)box.Y + 11, 20, Pal.Txt);
        if (g.TagBuffer.Length == 0)
            Raylib.DrawText("(blank = auto tags)", (int)box.X + 12, (int)box.Y + 46, 11, Pal.TxtDim);

        string hint = "Type a role  -  [Enter] save  -  [Esc] cancel  -  [Backspace] delete";
        Raylib.DrawText(hint, x + w / 2 - Raylib.MeasureText(hint, 12) / 2, y + h - 26, 12, Pal.TxtDim);
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
        int w = 440, h = 504;
        int x = Cfg.ScreenW / 2 - w / 2, y = Cfg.ScreenH / 2 - h / 2;
        var card = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(card, 0.05f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.5f, Pal.PanelBd);

        Raylib.DrawText("PAUSED", x + w / 2 - Raylib.MeasureText("PAUSED", 40) / 2, y + 22, 40, Pal.Friend);

        int bw = 320, bh = 42, bx = x + w / 2 - bw / 2, by = y + 84, gap = 11;
        PauseResume     = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseFullscreen = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseWindow     = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseMute       = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseShake      = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseThreat     = new Rectangle(bx, by, bw, bh); by += bh + gap;
        PauseAbandon    = new Rectangle(bx, by, bw, bh);

        DrawButtonRect(PauseResume, "RESUME", "ESC", true, false, Pal.Friend);
        DrawButtonRect(PauseFullscreen, Display.Fullscreen ? "FULLSCREEN: ON" : "FULLSCREEN: OFF", "F", true, !Display.Fullscreen, Pal.Accent);
        DrawButtonRect(PauseWindow, "WINDOW: " + Display.SizeLabel, "", true, false, Pal.Accent);
        DrawButtonRect(PauseMute, Audio.Enabled ? "AUDIO: ON" : "AUDIO: OFF", "M", true, !Audio.Enabled, Pal.Accent);
        DrawButtonRect(PauseShake, g.Fx.ShakeOn ? "SCREEN SHAKE: ON" : "SCREEN SHAKE: OFF", "", true, !g.Fx.ShakeOn, Pal.Accent);
        DrawButtonRect(PauseThreat, g.ShowThreatPref ? "THREAT PREVIEW: ON" : "THREAT PREVIEW: OFF", "", true, !g.ShowThreatPref, Pal.Accent);
        DrawButtonRect(PauseAbandon, "ABANDON RUN", "", true, false, Pal.Foe);

        string ctl = "Wheel zoom  -  Middle-drag pan  -  [C] reset camera  -  Arrows/WASD + [Space]";
        Raylib.DrawText(ctl, x + w / 2 - Raylib.MeasureText(ctl, 11) / 2, y + h - 24, 11, Pal.TxtDim);
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

            Raylib.DrawRectangleRounded(r, 0.16f, 6, Raylib.Fade(sel ? Pal.RGBA(26, 36, 48) : Pal.Panel, a));
            Raylib.DrawRectangleLinesEx(r, 1.5f, Raylib.Fade(sel ? Pal.Accent : Pal.PanelBd, a));
            Raylib.DrawRectangle((int)r.X, (int)r.Y, 3, (int)r.Height, Raylib.Fade(sel ? Pal.Accent : Pal.Friend, a));

            Raylib.DrawText(u.Name, (int)r.X + 9, (int)r.Y + 5, 13, Raylib.Fade(Pal.Txt, a));
            int nameW = Raylib.MeasureText(u.Name, 13);
            if (!string.IsNullOrEmpty(u.Nickname))   // earned callsign, in quotes
                Raylib.DrawText($"\"{u.Nickname}\"", (int)r.X + 9 + nameW + 5, (int)r.Y + 6, 11, Raylib.Fade(Pal.VipGold, a));
            // status marks (right): OW/HK, else a live BOND aura when a partner is adjacent
            if (u.OnOverwatch) Raylib.DrawText("OW", (int)r.X + 96, (int)r.Y + 5, 11, Raylib.Fade(Pal.Accent, a));
            else if (u.Hunkered) Raylib.DrawText("HK", (int)r.X + 96, (int)r.Y + 5, 11, Raylib.Fade(Pal.Good, a));
            else if (u.BondAura) Raylib.DrawText("BOND", (int)r.X + 88, (int)r.Y + 5, 11, Raylib.Fade(Pal.VipGold, a));

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
            Raylib.DrawText(u.IsVip ? "ASSET" : u.RankName, (int)r.X + 58, (int)r.Y + 33, 9,
                            Raylib.Fade(u.IsVip ? Pal.VipGold : Pal.TxtDim, a));

            // role tag: WOUNDED (red) takes priority, else custom tag (cyan) / auto strengths (amber)
            if (!u.IsVip)
            {
                if (u.Wound > 0)
                    Raylib.DrawText($"WOUNDED ({u.Wound})", (int)r.X + 9, (int)r.Y + 45, 9, Raylib.Fade(Pal.Foe, a));
                else
                {
                    var (tag, custom) = DisplayTag(u);
                    if (tag.Length > 0)
                        Raylib.DrawText(tag, (int)r.X + 9, (int)r.Y + 45, 9, Raylib.Fade(custom ? Pal.Friend : Pal.Accent, a));
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

        Raylib.DrawText($"MISSION {g.RunState.Mission}/{Run.MaxMissions}", 200, 19, 16, Pal.TxtDim);
        string objTxt; Color objCol;
        switch (g.Objective)
        {
            case Objective.Evac: objTxt = "EXTRACT"; objCol = Pal.Good; break;
            case Objective.Hack: objTxt = $"HACK {g.HackProgress}/{Game.HackRequired}"; objCol = Pal.Accent; break;
            case Objective.Escort: objTxt = "ESCORT VIP"; objCol = Pal.VipGold; break;
            default: objTxt = "ELIMINATE"; objCol = Pal.TxtDim; break;
        }
        Raylib.DrawText(objTxt, 340, 19, 16, objCol);

        // counts (the VIP isn't a combatant, so it's excluded from the squad tally)
        int friends = g.AlivePlayers().Count(p => !p.IsVip);
        int foes = g.AliveEnemies().Count;
        DrawCounter(Cfg.ScreenW / 2 - 130, 20, Pal.Friend, $"{friends}  SQUAD");
        DrawCounter(Cfg.ScreenW / 2 + 20, 20, Pal.Foe, $"{foes}  HOSTILES");

        // optional secondary objective (3.9): green while on track, red once blown
        if (g.Secondary != SecondaryKind.None)
            Raylib.DrawText(g.SecondaryHud, 812, 19, 14, g.SecondaryOnTrack ? Pal.Good : Pal.Foe);

        // mute indicator
        if (!Audio.Enabled)
            Raylib.DrawText("MUTED (M)", Cfg.ScreenW - 290, 19, 15, Pal.TxtDim);

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
        Raylib.DrawText(text, x + 14, y, 16, Pal.Txt);
    }

    // ---------------- bottom bar ----------------
    static void DrawBottomBar(Game g)
    {
        int barY = Cfg.OriginY + Cfg.BoardH + 14; // ~694
        Raylib.DrawRectangleGradientV(0, Cfg.ScreenH - 120, Cfg.ScreenW, 120,
                                      Pal.RGBA(8, 12, 17, 0), Pal.RGBA(8, 12, 17, 235));

        var u = g.Selected;
        if (u != null && u.Team == Team.Player)
            DrawUnitCard(u, 20, barY);

        DrawActionButtons(g, barY);

        // hint
        string hint = "MOVE / FIRE by click  -  [Tab] next  -  [5] ability  -  [T] tag  -  [Esc] menu";
        int hw = Raylib.MeasureText(hint, 13);
        Raylib.DrawText(hint, Cfg.ScreenW - hw - 24, Cfg.ScreenH - 30, 13, Pal.TxtDim);
    }

    static void DrawUnitCard(Unit u, int x, int y)
    {
        var card = new Rectangle(x, y, 250, 92);
        Raylib.DrawRectangleRounded(card, 0.12f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1f, Pal.PanelBd);
        Raylib.DrawRectangle((int)card.X, (int)card.Y, 3, (int)card.Height, Pal.Friend);

        Raylib.DrawText(u.Name, x + 14, y + 10, 20, Pal.Txt);
        int nw = Raylib.MeasureText(u.Name, 20);
        Raylib.DrawText(u.Cls, x + 20 + nw, y + 15, 12, u.IsVip ? Pal.VipGold : Pal.Friend);
        string rank = u.IsVip ? "ASSET" : u.RankName;
        Raylib.DrawText(rank, x + 250 - Raylib.MeasureText(rank, 11) - 14, y + 13, 11, u.IsVip ? Pal.VipGold : Pal.Accent);

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

        // action pips
        for (int i = 0; i < 2; i++)
        {
            var pip = new Rectangle(x + 14 + i * 26, y + 60, 22, 7);
            bool on = i < u.ActionsLeft;
            Raylib.DrawRectangleRounded(pip, 0.5f, 4, on ? Pal.Accent : Pal.RGBA(28, 39, 51));
        }
        // grenade count
        string gren = $"GREN x{u.Grenades}";
        Raylib.DrawText(gren, x + 72, y + 60, 11, u.Grenades > 0 ? Pal.Accent : Pal.TxtDim);
        // ammo
        string ammo = $"AMMO {u.Ammo}/{u.Weapon.Clip}";
        Raylib.DrawText(ammo, x + 250 - Raylib.MeasureText(ammo, 12) - 14, y + 60, 12,
                        u.Ammo == 0 ? Pal.Foe : Pal.TxtDim);
    }

    static void DrawActionButtons(Game g, int y)
    {
        var u = g.Selected;
        bool interactive = g.IsPlayerInteractive() && u != null && u.Team == Team.Player;
        bool hasTargets = interactive && g.HasAnyTarget(u);

        var btns = new System.Collections.Generic.List<UiButton>();
        float bx = 300, bw = 104, bh = 40, gap = 6;

        void Add(string id, string label, string key, bool enabled, bool sel)
        {
            btns.Add(new UiButton
            {
                Rect = new Rectangle(bx, y + 26, bw, bh),
                Id = id, Label = label, Key = key, Enabled = enabled, Selected = sel,
                Accent = Pal.Friend,
            });
            bx += bw + gap;
        }

        Add("shoot", "FIRE", "1", interactive && u != null && u.CanAct && u.Ammo > 0 && hasTargets, g.AimMode);
        Add("grenade", "GRENADE", "4", interactive && u != null && u.CanAct && u.Grenades > 0, g.GrenadeMode);
        if (u != null && u.Ability != AbilityKind.None)
            Add("ability", u.AbilityName, "5", interactive && g.CanAbility(u), u.RunGun || u.Blitz || u.Steady);
        if (u != null && u.Item != ItemKind.None)
            Add("item", u.ItemName, "6", interactive && u.CanAct && u.ItemCharge > 0, g.ItemMode);
        Add("overwatch", "OVERWATCH", "2", interactive && u != null && u.CanAct && u.Ammo > 0, false);
        Add("hunker", "HUNKER", "3", interactive && u != null && u.CanAct, u != null && u.Hunkered);
        if (g.HasTerminal)
            Add("hack", "HACK", "H", interactive && g.CanHack(u), false);
        Add("reload", "RELOAD", "R", interactive && u != null && u.CanAct && u.Ammo < u.Weapon.Clip, false);

        ActionButtons = btns.ToArray();
        foreach (var b in ActionButtons)
            DrawButtonRect(b.Rect, b.Label, b.Key, b.Enabled, b.Selected, b.Accent);

        DrawActionHelp(g);
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
            int w = Math.Max(Raylib.MeasureText(title, 14), Raylib.MeasureText(desc, 12)) + 20;
            int h = 50;
            int x = (int)(b.Rect.X + b.Rect.Width / 2 - w / 2);
            int y = (int)b.Rect.Y - h - 8;
            x = Util.Clamp(x, 8, Cfg.ScreenW - w - 8);
            var box = new Rectangle(x, y, w, h);
            Raylib.DrawRectangleRounded(box, 0.14f, 6, Pal.RGBA(10, 14, 19, 252));
            Raylib.DrawRectangleLinesEx(box, 1.2f, Pal.Friend);
            Raylib.DrawText(title, x + 10, y + 8, 14, Pal.Accent);
            Raylib.DrawText(desc, x + 10, y + 28, 12, Pal.Txt);
            return;
        }
    }

    static string ActionDesc(Game g, string id)
    {
        switch (id)
        {
            case "shoot": return "Fire at a target in range + line of sight. Ends the turn.";
            case "grenade": return "Lob a grenade: AoE that ignores cover, hits both teams, clears low cover.";
            case "overwatch": return "Watch: fire a reaction shot at the first foe that moves in sight.";
            case "hunker": return "Hunker down for extra cover defense; you can't be crit.";
            case "hack": return $"Work the terminal ({g.HackProgress}/{Game.HackRequired} done). Costs 1 action.";
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
    static void DrawTooltip(Game g)
    {
        if (!g.ShowOdds) return;
        var o = g.HoverOdds;
        var m = Raylib.GetMousePosition();
        int extras = (o.Flanked ? 1 : 0) + (o.HighGround ? 1 : 0) + (o.SeesOver ? 1 : 0) + (o.Partial ? 1 : 0) + (o.Steady ? 1 : 0);
        int w = 150, h = 74 + extras * 18;
        int x = (int)m.X - w / 2;
        int y = (int)m.Y - h - 18;
        x = Util.Clamp(x, 8, Cfg.ScreenW - w - 8);
        y = Util.Clamp(y, 64, Cfg.ScreenH - h - 8);

        var box = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(box, 0.12f, 8, Pal.RGBA(10, 14, 19, 245));
        Raylib.DrawRectangleLinesEx(box, 1.5f, Pal.Foe);

        Raylib.DrawText("HIT", x + 12, y + 10, 12, Pal.TxtDim);
        string hit = $"{o.HitChance}%";
        Raylib.DrawText(hit, x + w - Raylib.MeasureText(hit, 22) - 12, y + 7, 22, Pal.Good);

        Raylib.DrawText("CRIT", x + 12, y + 34, 12, Pal.TxtDim);
        string crit = $"{o.CritChance}%";
        Raylib.DrawText(crit, x + w - Raylib.MeasureText(crit, 16) - 12, y + 32, 16, Pal.Accent);

        Raylib.DrawText("DMG", x + 12, y + 54, 12, Pal.TxtDim);
        string dmg = $"{o.DmgMin}-{o.DmgMax}";
        Raylib.DrawText(dmg, x + w - Raylib.MeasureText(dmg, 16) - 12, y + 52, 16, Pal.Foe);

        int fy = y + 72;
        if (o.Flanked) { Raylib.DrawText("! FLANKED", x + 12, fy, 13, Pal.Accent); fy += 18; }
        if (o.HighGround) { Raylib.DrawText("+ HIGH GROUND", x + 12, fy, 13, Pal.Good); fy += 18; }
        if (o.SeesOver) { Raylib.DrawText("+ OVER LOW COVER", x + 12, fy, 13, Pal.Good); fy += 18; }
        if (o.Partial) { Raylib.DrawText("~ PARTIAL COVER", x + 12, fy, 13, Pal.TxtDim); fy += 18; }
        if (o.Steady) { Raylib.DrawText("+ STEADY", x + 12, fy, 13, Pal.Good); fy += 18; }
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
        int tw = Raylib.MeasureText(g.BannerText, fs);
        Raylib.DrawText(g.BannerText, Cfg.ScreenW / 2 - tw / 2, bandY + 24, fs, Raylib.Fade(c, a));
    }

    // ---------------- overlays ----------------
    static void DrawOverlays(Game g)
    {
        if (g.Phase == Phase.Intro)
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
        Raylib.DrawText(title, x + w / 2 - Raylib.MeasureText(title, 38) / 2, y + 26, 38, Pal.Good);
        string sub = $"BARRACKS - SQUAD DEBRIEF   |   INTEL {run.Intel}";
        Raylib.DrawText(sub, x + w / 2 - Raylib.MeasureText(sub, 13) / 2, y + 70, 13, Pal.TxtDim);

        int ry = y + 100;
        foreach (var u in squad)
        {
            DrawSquadRow(g, u, x + 30, ry, w - 60);
            ry += 46;
        }

        // promotions / heals report
        ry += 8;
        Raylib.DrawText("DEBRIEF", x + 30, ry, 12, Pal.Accent);
        ry += 20;
        int shown = 0;
        foreach (var line in run.Report)
        {
            if (shown++ >= 5) break;
            Raylib.DrawText("- " + line, x + 36, ry, 13, Pal.TxtDim);
            ry += 22;
        }
        if (run.Fallen.Count > 0)
        {
            string kia = "KIA: " + string.Join(", ", run.Fallen);
            Raylib.DrawText(kia, x + 36, ry, 13, Pal.Foe);
        }

        // next operation: pick a node on the branching campaign map (3.3).
        if (run.Map.Count > 0 && run.NextNodes().Count > 0)
        {
            string pick = "CAMPAIGN MAP  >  SELECT NEXT OPERATION";
            Raylib.DrawText(pick, x + w / 2 - Raylib.MeasureText(pick, 15) / 2, y + h - 162, 15, Pal.Accent);
            DrawCampaignMap(run, new Rectangle(x + 24, y + h - 140, w - 48, 124));
        }
        else  // fallback: legacy deployment cards (only if the map is unavailable)
        {
            string pick = $"SELECT DEPLOYMENT  >  MISSION {run.Mission + 1}";
            Raylib.DrawText(pick, x + w / 2 - Raylib.MeasureText(pick, 15) / 2, y + h - 158, 15, Pal.Accent);
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
            Raylib.DrawText(gly, (int)(p.X - Raylib.MeasureText(gly, 14) / 2), (int)(p.Y - 7), 14, Pal.RGBA(8, 12, 18));

            if (canPick)  // label the choices with their objective
            {
                string lbl = ObjName(n.Card.Objective);
                Raylib.DrawText(lbl, (int)(p.X - Raylib.MeasureText(lbl, 10) / 2), (int)(p.Y + rad + 3), 10, Pal.Txt);
            }
        }

        // hover tooltip: the chosen op's flavour (mod / objective / force / reward)
        if (hovered != null)
        {
            var c = hovered.Card;
            string l1 = $"{c.ModName}  -  {ObjName(c.Objective)}";
            string l2 = c.EnemyDelta > 0 ? "Heavy resistance" : (c.EnemyDelta < 0 ? "Light resistance" : "Standard force");
            string l3 = c.Reward != RewardKind.None ? "+ " + c.RewardText : null;
            int tw = Math.Max(Raylib.MeasureText(l1, 13), Math.Max(Raylib.MeasureText(l2, 11), l3 != null ? Raylib.MeasureText(l3, 11) : 0)) + 20;
            int th = l3 != null ? 60 : 44;
            float tx = Math.Min(mouse.X + 14, region.X + region.Width - tw);
            float ty = Math.Max(mouse.Y - th - 6, region.Y);
            var tip = new Rectangle(tx, ty, tw, th);
            Raylib.DrawRectangleRounded(tip, 0.12f, 6, Pal.RGBA(12, 18, 26));
            Raylib.DrawRectangleLinesEx(tip, 1.2f, NodeColor(hovered.Kind));
            Raylib.DrawText(l1, (int)tx + 10, (int)ty + 8, 13, NodeColor(hovered.Kind));
            Raylib.DrawText(l2, (int)tx + 10, (int)ty + 26, 11, Pal.TxtDim);
            if (l3 != null) Raylib.DrawText(l3, (int)tx + 10, (int)ty + 42, 11, Pal.Accent);
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
        Raylib.DrawText(title, x + w / 2 - Raylib.MeasureText(title, 36) / 2, y + 24, 36, Pal.Accent);
        string intel = $"INTEL AVAILABLE: {run.Intel}";
        Raylib.DrawText(intel, x + w / 2 - Raylib.MeasureText(intel, 16) / 2, y + 66, 16, Pal.Good);

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
            Raylib.DrawText(Game.ShopName[i], (int)r.X + 14, (int)r.Y + 10, 18, txt);
            Raylib.DrawText(Game.ShopDesc[i], (int)r.X + 14, (int)r.Y + 35, 12, Pal.TxtDim);
            Raylib.DrawText(g.ShopEffect(i), (int)r.X + 14, (int)r.Y + 55, 12, can ? Pal.Accent : Pal.TxtDim);  // concrete effect
            string cost = $"{Game.ShopCost[i]} INTEL";
            Color cc = run.Intel >= Game.ShopCost[i] ? Pal.Good : Pal.Foe;
            Raylib.DrawText(cost, (int)(r.X + r.Width - Raylib.MeasureText(cost, 16) - 14), (int)r.Y + 12, 16, cc);
            if (!can)
                Raylib.DrawText("- unavailable -", (int)(r.X + r.Width - Raylib.MeasureText("- unavailable -", 11) - 14), (int)r.Y + 52, 11, Pal.TxtDim);
            else
                Raylib.DrawText("[ BUY ]", (int)(r.X + r.Width - Raylib.MeasureText("[ BUY ]", 12) - 14), (int)r.Y + 54, 12, Pal.Accent);
            iy += ih + gap;
        }

        ShopProceed = new Rectangle(x + w / 2 - 130, y + h - 60, 260, 44);
        bool ph = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), ShopProceed);
        Raylib.DrawRectangleRounded(ShopProceed, 0.3f, 8, ph ? Pal.RGBA(92, 200, 251) : Pal.Friend);
        CenterText("PROCEED TO DEPLOYMENT", ShopProceed, 15, Pal.RGBA(3, 18, 26));
    }

    static string ObjName(Objective o) => o switch
    {
        Objective.Hack => "HACK", Objective.Evac => "EXTRACT", Objective.Escort => "ESCORT VIP", _ => "ELIMINATE",
    };

    static void DrawDeployCard(Rectangle r, MissionCard c)
    {
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Color tint = c.ModName == "RECON" ? Pal.Good : (c.ModName == "ONSLAUGHT" ? Pal.Foe : Pal.Friend);
        Raylib.DrawRectangleRounded(r, 0.1f, 6, hover ? Pal.RGBA(24, 34, 46) : Pal.RGBA(14, 20, 28));
        Raylib.DrawRectangleLinesEx(r, 1.5f, hover ? tint : Pal.PanelBd);
        Raylib.DrawRectangle((int)r.X, (int)r.Y, 4, (int)r.Height, tint);

        Raylib.DrawText(c.ModName, (int)r.X + 12, (int)r.Y + 10, 17, tint);
        Raylib.DrawText(ObjName(c.Objective), (int)r.X + 12, (int)r.Y + 34, 13, Pal.Txt);
        string force = c.EnemyDelta > 0 ? "Heavy resistance" : (c.EnemyDelta < 0 ? "Light resistance" : "Standard force");
        Raylib.DrawText(force, (int)r.X + 12, (int)r.Y + 56, 11, Pal.TxtDim);
        if (c.Reward != RewardKind.None)
            Raylib.DrawText("+ " + c.RewardText, (int)r.X + 12, (int)r.Y + 74, 11, Pal.Accent);
        Raylib.DrawText("DEPLOY", (int)(r.X + r.Width / 2 - Raylib.MeasureText("DEPLOY", 12) / 2),
                        (int)(r.Y + r.Height - 22), 12, hover ? tint : Pal.TxtDim);
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
        Raylib.DrawText(title, x + w / 2 - Raylib.MeasureText(title, 36) / 2, y + 22, 36, Pal.Accent);
        string sub = $"{off.Unit.FullName}  -  {off.Unit.RankName}  -  {off.Unit.Cls}  -  CHOOSE A PERK";
        Raylib.DrawText(sub, x + w / 2 - Raylib.MeasureText(sub, 14) / 2, y + 64, 14, Pal.TxtDim);

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
        Raylib.DrawText(foot, x + w / 2 - Raylib.MeasureText(foot, 12) / 2, y + h - 26, 12, Pal.TxtDim);
    }

    /// A soldier's stat line + current perks + derived strengths (for decision screens).
    static void DrawDossier(Unit u, int x, int y, int w)
    {
        string stats = $"HP {u.Hp}/{u.MaxHp}    AIM {u.Aim}    MOB {u.Mobility}    {u.Weapon.Name}    GREN {1 + u.BonusGrenades}/mission    {u.AbilityName}";
        Raylib.DrawText(stats, x, y, 13, Pal.Txt);
        string perks = u.Perks.Count == 0 ? "Perks: none yet"
            : "Perks: " + string.Join(", ", u.Perks.ConvertAll(PerkDef.Name));
        Raylib.DrawText(perks, x, y + 22, 13, Pal.Good);

        // earned traits + bonds (3.2): what makes this veteran distinct
        string traits = u.Traits.Count == 0 ? "Traits: none yet"
            : "Traits: " + string.Join(", ", u.Traits.ConvertAll(TraitDef.Name));
        if (u.Bonds.Count > 0) traits += "    Bonds: " + string.Join(", ", u.Bonds);
        Raylib.DrawText(traits, x, y + 44, 12, u.Traits.Count == 0 && u.Bonds.Count == 0 ? Pal.TxtDim : Pal.VipGold);

        if (u.Wound > 0)
            Raylib.DrawText($"WOUNDED ({u.Wound} mission{(u.Wound > 1 ? "s" : "")})  -{Unit.WoundAim} aim / -{Unit.WoundMob} mob", x, y + 66, 12, Pal.Foe);
        else if (!string.IsNullOrEmpty(u.CustomTag))
            Raylib.DrawText("Tag: " + u.CustomTag, x, y + 66, 12, Pal.Friend);
        else
        {
            var sp = Specialties(u);
            if (sp.Count > 0)
                Raylib.DrawText("Strengths: " + string.Join("  ", sp), x, y + 66, 12, Pal.Accent);
        }
    }

    /// Derived strength tags from class/weapon/stats/perks, so the player knows what
    /// each soldier is good at (in-round play and build planning).
    public static System.Collections.Generic.List<string> Specialties(Unit u)
    {
        var t = new System.Collections.Generic.List<string>();
        if (u.HasPerk(Perk.Reflexes)) t.Add("OVERWATCH");
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
            Raylib.DrawText(u.Name, cx, y, 11, Pal.Txt);
            var bar = new Rectangle(cx, y + 15, cw - 14, 7);
            Raylib.DrawRectangleRounded(bar, 0.5f, 4, Pal.RGBA(10, 15, 21));
            float frac = u.MaxHp > 0 ? u.Hp / (float)u.MaxHp : 0;
            if (frac > 0)
            {
                Color hc = frac > 0.5f ? Pal.Good : (frac > 0.25f ? Pal.Accent : Pal.Foe);
                Raylib.DrawRectangleRounded(new Rectangle(bar.X, bar.Y, bar.Width * frac, bar.Height), 0.5f, 4, hc);
            }
            Raylib.DrawText($"{u.Hp}/{u.MaxHp}", cx, y + 25, 10, Pal.TxtDim);
        }
    }

    static void DrawPerkCard(Rectangle r, Perk p)
    {
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Raylib.DrawRectangleRounded(r, 0.08f, 8, hover ? Pal.RGBA(24, 34, 46) : Pal.RGBA(14, 20, 28));
        Raylib.DrawRectangleLinesEx(r, 1.5f, hover ? Pal.Accent : Pal.PanelBd);
        Raylib.DrawRectangle((int)r.X, (int)r.Y, 4, (int)r.Height, hover ? Pal.Accent : Pal.Friend);

        string name = PerkDef.Name(p);
        Raylib.DrawText(name, (int)(r.X + r.Width / 2 - Raylib.MeasureText(name, 22) / 2), (int)r.Y + 28, 22,
                        hover ? Pal.Accent : Pal.Txt);
        // word-wrapped one-line description (kept short by design)
        string desc = PerkDef.Desc(p);
        Raylib.DrawText(desc, (int)(r.X + r.Width / 2 - Raylib.MeasureText(desc, 14) / 2), (int)r.Y + 78, 14, Pal.TxtDim);

        Raylib.DrawText("SELECT", (int)(r.X + r.Width / 2 - Raylib.MeasureText("SELECT", 13) / 2),
                        (int)(r.Y + r.Height - 32), 13, hover ? Pal.Accent : Pal.TxtDim);
    }

    static void DrawSquadRow(Game g, Unit u, int x, int y, int w)
    {
        Raylib.DrawRectangleRounded(new Rectangle(x, y, w, 40), 0.2f, 6, Pal.RGBA(13, 19, 27));
        Raylib.DrawRectangle(x, y, 3, 40, Pal.Friend);

        Raylib.DrawText(u.Name, x + 14, y + 5, 18, Pal.Txt);
        Raylib.DrawText($"{u.RankName}  -  {u.Cls}", x + 14, y + 24, 11, Pal.Accent);

        // earned perks (compact 3-letter codes)
        if (u.Perks.Count > 0)
        {
            string codes = string.Join(" ", u.Perks.ConvertAll(PerkDef.Code));
            Raylib.DrawText(codes, x + 220, y + 27, 9, Pal.Good);
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
        Raylib.DrawText($"{u.Hp}/{u.MaxHp} HP", x + 380, y + 13, 12, Pal.TxtDim);

        // kills + progress
        Raylib.DrawText($"{u.Kills} kills", x + w - 170, y + 6, 12, Pal.Txt);
        int toNext = g.RunState.KillsToNext(u);
        string prog = u.Rank >= Run.Ranks.Length - 1 ? "MAX RANK" : $"{toNext} to next rank";
        Raylib.DrawText(prog, x + w - 170, y + 23, 11, Pal.TxtDim);
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
            foreach (var r in rules) maxRule = Math.Max(maxRule, Raylib.MeasureText(r, 15));
            w = Math.Max(w, maxRule + 58 + 30);
        }
        int h = rules != null ? 152 + rules.Length * 30 + 70 : 240;
        int x = Cfg.ScreenW / 2 - w / 2, y = Cfg.ScreenH / 2 - h / 2;
        var card = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(card, 0.06f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.5f, Pal.PanelBd);

        int tfs = 46;
        Raylib.DrawText(title, x + w / 2 - Raylib.MeasureText(title, tfs) / 2, y + 34, tfs, titleCol);
        Raylib.DrawText(sub, x + w / 2 - Raylib.MeasureText(sub, 14) / 2, y + 90, 14, Pal.TxtDim);

        if (rules != null)
        {
            int ry = y + 130;
            foreach (var r in rules)
            {
                Raylib.DrawText(">", x + 40, ry, 16, Pal.Friend);
                Raylib.DrawText(r, x + 58, ry, 15, Pal.TxtDim);
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

    static void DrawOverlayButton(Rectangle r, string label, Color baseCol, string keyHint)
    {
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Color hi = Pal.RGBA(92, 200, 251);
        Raylib.DrawRectangleRounded(r, 0.3f, 8, hover ? hi : baseCol);
        CenterText(label, r, 18, Pal.RGBA(3, 18, 26));
        if (keyHint != null)
            Raylib.DrawText("[" + keyHint + "]", (int)(r.X + r.Width - 30), (int)(r.Y + r.Height - 16), 11, Pal.RGBA(3, 18, 26));
    }

    // ---------------- helpers ----------------
    public static void DrawButtonRect(Rectangle r, string label, string key, bool enabled, bool selected, Color accent)
    {
        bool hover = enabled && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
        Color bg = selected ? Pal.RGBA(40, 34, 12) : (hover ? Pal.RGBA(22, 32, 44) : Pal.Panel);
        Raylib.DrawRectangleRounded(r, 0.22f, 6, Raylib.Fade(bg, enabled ? 1f : 0.4f));
        Color bd = selected ? Pal.Accent : (hover ? accent : Pal.PanelBd);
        Raylib.DrawRectangleLinesEx(r, 1.5f, Raylib.Fade(bd, enabled ? 1f : 0.35f));

        Color tc = selected ? Pal.Accent : (enabled ? Pal.Txt : Pal.TxtDim);
        int fs = 16;
        int lw = Raylib.MeasureText(label, fs);
        int kw = string.IsNullOrEmpty(key) ? 0 : Raylib.MeasureText(key, 12) + 8;
        int startX = (int)(r.X + r.Width / 2 - (lw + kw) / 2);
        int ty = (int)(r.Y + r.Height / 2 - fs / 2);
        Raylib.DrawText(label, startX, ty, fs, Raylib.Fade(tc, enabled ? 1f : 0.5f));
        if (!string.IsNullOrEmpty(key))
        {
            int keyX = startX + lw + 8;
            var kr = new Rectangle(keyX, r.Y + r.Height / 2 - 8, Raylib.MeasureText(key, 12) + 6, 16);
            Raylib.DrawRectangleLinesEx(kr, 1f, Raylib.Fade(tc, 0.4f));
            Raylib.DrawText(key, keyX + 3, (int)(r.Y + r.Height / 2 - 6), 12, Raylib.Fade(tc, 0.7f));
        }
    }

    static void CenterText(string text, Rectangle r, int fs, Color c)
    {
        int w = Raylib.MeasureText(text, fs);
        Raylib.DrawText(text, (int)(r.X + r.Width / 2 - w / 2), (int)(r.Y + r.Height / 2 - fs / 2), fs, c);
    }
}
