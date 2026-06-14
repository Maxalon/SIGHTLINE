using System;
using System.Numerics;
using Raylib_cs;

namespace Breach;

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

    public static void Draw(Game g)
    {
        DrawTopBar(g);
        DrawBottomBar(g);
        DrawTooltip(g);
        DrawBanner(g);
        DrawOverlays(g);
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

        // counts
        int friends = g.AlivePlayers().Count;
        int foes = g.AliveEnemies().Count;
        DrawCounter(Cfg.ScreenW / 2 - 130, 20, Pal.Friend, $"{friends}  SQUAD");
        DrawCounter(Cfg.ScreenW / 2 + 20, 20, Pal.Foe, $"{foes}  HOSTILES");

        // end turn
        EndTurnRect = new Rectangle(Cfg.ScreenW - 170, 11, 150, 30);
        bool canEnd = g.IsPlayerInteractive();
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
        string hint = "Click tile to MOVE  -  click hostile to FIRE  -  [Tab] next  -  [Space] center";
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
        Raylib.DrawText(u.Cls, x + 20 + nw, y + 15, 12, Pal.Friend);
        Raylib.DrawText(u.RankName, x + 250 - Raylib.MeasureText(u.RankName, 11) - 14, y + 13, 11, Pal.Accent);

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
        float bx = 300, bw = 118, bh = 40, gap = 8;

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
        Add("overwatch", "OVERWATCH", "2", interactive && u != null && u.CanAct && u.Ammo > 0, false);
        Add("hunker", "HUNKER", "3", interactive && u != null && u.CanAct, u != null && u.Hunkered);
        Add("reload", "RELOAD", "R", interactive && u != null && u.CanAct && u.Ammo < u.Weapon.Clip, false);

        ActionButtons = btns.ToArray();
        foreach (var b in ActionButtons)
            DrawButtonRect(b.Rect, b.Label, b.Key, b.Enabled, b.Selected, b.Accent);
    }

    // ---------------- tooltip ----------------
    static void DrawTooltip(Game g)
    {
        if (!g.ShowOdds) return;
        var o = g.HoverOdds;
        var m = Raylib.GetMousePosition();
        int w = 150, h = o.Flanked ? 96 : 78;
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

        if (o.Flanked)
            Raylib.DrawText("! FLANKED", x + 12, y + 76, 13, Pal.Accent);
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
            DrawCenterCard(g, "BREACH", "TURN-BASED SQUAD TACTICS", Pal.Friend,
                new[]{
                    $"Lead one squad through {Run.MaxMissions} escalating missions.",
                    "2 actions per soldier - move, then fire (firing ends the turn).",
                    "Stand beside cover to cut enemy aim. Get flanked and you're exposed.",
                    "Kills earn promotions: better aim, more HP, more mobility.",
                    "Survivors carry their wounds and ranks to the next mission.",
                }, "DEPLOY SQUAD");
        else if (g.Phase == Phase.Barracks)
            DrawBarracks(g);
        else if (g.Phase == Phase.Win)
            DrawCenterCard(g, "CAMPAIGN COMPLETE", $"All {Run.MaxMissions} missions cleared. The squad stands victorious.",
                Pal.Good, null, "NEW RUN");
        else if (g.Phase == Phase.Lose)
            DrawCenterCard(g, "RUN OVER", $"The squad fell on mission {g.RunState.Mission}.", Pal.Foe, null, "NEW RUN");
    }

    static void DrawBarracks(Game g)
    {
        var run = g.RunState;
        var squad = run.Squad;
        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.RGBA(5, 8, 11), 0.85f));

        int w = 700;
        int rows = squad.Count;
        int h = 150 + rows * 46 + Math.Min(run.Report.Count, 5) * 22 + 90;
        int x = Cfg.ScreenW / 2 - w / 2;
        int y = Cfg.ScreenH / 2 - h / 2;
        var card = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRounded(card, 0.04f, 8, Pal.Panel);
        Raylib.DrawRectangleLinesEx(card, 1.5f, Pal.PanelBd);

        string title = $"MISSION {run.Mission} COMPLETE";
        Raylib.DrawText(title, x + w / 2 - Raylib.MeasureText(title, 38) / 2, y + 26, 38, Pal.Good);
        string sub = "BARRACKS - SQUAD DEBRIEF";
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

        OverlayBtn = new Rectangle(x + w / 2 - 130, y + h - 64, 260, 46);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), OverlayBtn);
        Raylib.DrawRectangleRounded(OverlayBtn, 0.3f, 8, hover ? Pal.RGBA(92, 200, 251) : Pal.Friend);
        CenterText($"DEPLOY  >  MISSION {run.Mission + 1}", OverlayBtn, 16, Pal.RGBA(3, 18, 26));
    }

    static void DrawSquadRow(Game g, Unit u, int x, int y, int w)
    {
        Raylib.DrawRectangleRounded(new Rectangle(x, y, w, 40), 0.2f, 6, Pal.RGBA(13, 19, 27));
        Raylib.DrawRectangle(x, y, 3, 40, Pal.Friend);

        Raylib.DrawText(u.Name, x + 14, y + 5, 18, Pal.Txt);
        Raylib.DrawText($"{u.RankName}  -  {u.Cls}", x + 14, y + 24, 11, Pal.Accent);

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

    static void DrawCenterCard(Game g, string title, string sub, Color titleCol, string[] rules, string btn)
    {
        Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.RGBA(5, 8, 11), 0.84f));
        int w = 540, h = rules != null ? 360 : 240;
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

        OverlayBtn = new Rectangle(x + w / 2 - 110, y + h - 70, 220, 48);
        bool hover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), OverlayBtn);
        Raylib.DrawRectangleRounded(OverlayBtn, 0.3f, 8, hover ? Pal.RGBA(92, 200, 251) : Pal.Friend);
        CenterText(btn, OverlayBtn, 18, Pal.RGBA(3, 18, 26));
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
