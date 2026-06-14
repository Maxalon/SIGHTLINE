using System;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// Draws the battlefield, units and tactical overlays.
public static class Renderer
{
    // how far raised terrain (and anything standing on it) lifts on screen
    public const float ElevLift = 8f;

    // tile draw rect/centre offset up onto the plateau top when elevated
    static Rectangle ElevRect(Game g, int x, int y)
    {
        var r = Util.TileRect(x, y);
        if (g.Grid.IsHigh(x, y)) r.Y -= ElevLift;
        return r;
    }
    static Vector2 ElevCenter(Game g, int x, int y)
    {
        var c = Util.TileCenter(x, y);
        if (g.Grid.IsHigh(x, y)) c.Y -= ElevLift;
        return c;
    }

    public static void DrawBoard(Game g)
    {
        // board backing
        var edge = new Rectangle(Cfg.OriginX - 6, Cfg.OriginY - 6, Cfg.BoardW + 12, Cfg.BoardH + 12);
        Raylib.DrawRectangleRounded(edge, 0.02f, 6, Pal.RGBA(7, 10, 14));
        Raylib.DrawRectangleLinesEx(edge, 2f, Pal.BoardEdge);

        // floor
        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
            {
                if (g.Grid.Tiles[x, y] != TileType.Floor) continue;
                var r = Util.TileRect(x, y);
                Raylib.DrawRectangleRec(r, ((x + y) & 1) == 0 ? Pal.FloorA : Pal.FloorB);
            }

        DrawElevation(g);
        DrawMoveOverlay(g);
        DrawThreat(g);
        DrawEvac(g);
        DrawTerminal(g);
        DrawGridLines(g);
        DrawPathPreview(g);
        DrawCover(g);
        DrawHoverAndShields(g);
        DrawUnits(g);
        DrawAim(g);
        DrawGrenade(g);

        g.ActiveAnim?.Draw(g);
        g.Fx.Draw();
        g.Fx.DrawText();
    }

    // Raised plateaus: faux-3D platform with a front wall + lit top edge so the
    // high ground reads clearly. Drawn back-to-front (top rows first).
    static void DrawElevation(Game g)
    {
        for (int y = 0; y < g.Grid.H; y++)
            for (int x = 0; x < g.Grid.W; x++)
            {
                if (!g.Grid.IsHigh(x, y)) continue;
                var r = Util.TileRect(x, y);
                // exposed front wall (only where the tile below isn't also raised)
                if (!g.Grid.IsHigh(x, y + 1))
                    Raylib.DrawRectangleRec(
                        new Rectangle(r.X, r.Y + r.Height - ElevLift, r.Width, ElevLift + 3),
                        Pal.HighSide);
                // raised top face
                var top = new Rectangle(r.X, r.Y - ElevLift, r.Width, r.Height);
                Raylib.DrawRectangleRec(top, ((x + y) & 1) == 0 ? Pal.HighA : Pal.HighB);
                // lit front edge of the top face
                Raylib.DrawLineEx(new Vector2(top.X, top.Y + top.Height - 1),
                                  new Vector2(top.X + top.Width, top.Y + top.Height - 1),
                                  2f, Raylib.Fade(Pal.HighEdge, 0.5f));
                // top-edge highlight where it meets open air above
                if (!g.Grid.IsHigh(x, y - 1))
                    Raylib.DrawLineEx(new Vector2(top.X, top.Y),
                                      new Vector2(top.X + top.Width, top.Y),
                                      1.5f, Raylib.Fade(Pal.HighEdge, 0.35f));
            }
    }

    static void DrawEvac(Game g)
    {
        if (g.EvacZone.Count == 0) return;
        float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 3f);
        int minx = int.MaxValue, miny = int.MaxValue;
        foreach (var (x, y) in g.EvacZone)
        {
            var r = Util.TileRect(x, y);
            Raylib.DrawRectangleRec(r, Raylib.Fade(Pal.Good, 0.10f + 0.10f * pulse));
            Raylib.DrawRectangleLinesEx(new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4),
                                        2f, Raylib.Fade(Pal.Good, 0.5f + 0.4f * pulse));
            minx = Math.Min(minx, x); miny = Math.Min(miny, y);
        }
        var at = Util.TileCenter(minx, miny);
        Raylib.DrawText("EVAC", (int)at.X - 4, (int)(at.Y - Cfg.Tile / 2 + 4), 14, Pal.Good);
    }

    // Hack objective: a console tile with a segmented progress ring.
    static void DrawTerminal(Game g)
    {
        if (!g.HasTerminal) return;
        var (tx, ty) = g.Terminal;
        var r = ElevRect(g, tx, ty);
        var c = ElevCenter(g, tx, ty);
        bool done = g.HackProgress >= Game.HackRequired;
        Color col = done ? Pal.Good : Pal.Accent;
        float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 3f);

        // pad
        Raylib.DrawRectangleRec(r, Raylib.Fade(col, 0.08f + 0.06f * pulse));
        Raylib.DrawRectangleLinesEx(new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6),
                                    2f, Raylib.Fade(col, 0.45f + 0.4f * pulse));

        // segmented hack-progress ring
        float seg = 360f / Game.HackRequired;
        for (int i = 0; i < Game.HackRequired; i++)
        {
            bool filled = i < g.HackProgress;
            Raylib.DrawRing(c, 15, 19, -90 + i * seg + 5, -90 + (i + 1) * seg - 5, 14,
                            Raylib.Fade(col, filled ? 0.95f : 0.18f));
        }

        // console box + screen blip
        Raylib.DrawRectangleRec(new Rectangle(c.X - 9, c.Y - 11, 18, 22), Pal.RGBA(14, 20, 28));
        Raylib.DrawRectangleLinesEx(new Rectangle(c.X - 9, c.Y - 11, 18, 22), 1.5f, col);
        Raylib.DrawRectangleRec(new Rectangle(c.X - 5, c.Y - 7, 10, 6), Raylib.Fade(col, 0.6f + 0.4f * pulse));

        Raylib.DrawText("TERMINAL", (int)c.X - 26, (int)r.Y - 13, 11, col);
    }

    static void DrawGridLines(Game g)
    {
        for (int x = 0; x <= g.Grid.W; x++)
            Raylib.DrawLineEx(new Vector2(Cfg.OriginX + x * Cfg.Tile, Cfg.OriginY),
                              new Vector2(Cfg.OriginX + x * Cfg.Tile, Cfg.OriginY + Cfg.BoardH),
                              1f, Pal.GridLine);
        for (int y = 0; y <= g.Grid.H; y++)
            Raylib.DrawLineEx(new Vector2(Cfg.OriginX, Cfg.OriginY + y * Cfg.Tile),
                              new Vector2(Cfg.OriginX + Cfg.BoardW, Cfg.OriginY + y * Cfg.Tile),
                              1f, Pal.GridLine);
    }

    static void DrawMoveOverlay(Game g)
    {
        if (g.Selected == null || !g.IsPlayerInteractive() || g.AimMode || g.GrenadeMode) return;
        if (g.Selected.Team != Team.Player || !g.Selected.CanAct) return;
        var cost = g.MoveCost;
        if (cost == null) return;
        int budget = g.Selected.MoveBudget;
        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
            {
                int c = cost[x, y];
                if (c <= 0) continue;
                bool dash = c > budget;
                if (dash && g.Selected.ActionsLeft < 2) continue; // can't dash with 1 action
                var r = ElevRect(g, x, y);
                Raylib.DrawRectangleRec(r, dash ? Pal.MoveYellow : Pal.MoveBlue);
            }
    }

    // Red warning pips on reachable tiles that a live enemy could fire on with no
    // cover — a quick read on which destinations leave the soldier exposed.
    static void DrawThreat(Game g)
    {
        if (g.Selected == null || !g.IsPlayerInteractive() || g.AimMode || g.GrenadeMode) return;
        if (g.Selected.Team != Team.Player || !g.Selected.CanAct) return;
        if (g.Threat == null || g.MoveCost == null) return;

        float pulse = 0.6f + 0.4f * MathF.Sin((float)Raylib.GetTime() * 4f);
        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
            {
                if (!g.Threat[x, y]) continue;
                bool here = x == g.Selected.X && y == g.Selected.Y;
                if (!here && g.MoveCost[x, y] <= 0) continue;
                var r = ElevRect(g, x, y);
                var pos = new Vector2(r.X + r.Width - 9, r.Y + 9);
                Raylib.DrawPoly(pos, 3, 5.5f, -90f, Raylib.Fade(Pal.Foe, 0.85f * pulse));
                Raylib.DrawPolyLinesEx(pos, 3, 5.5f, -90f, 1.5f, Raylib.Fade(Pal.RGBA(255, 220, 220), 0.8f));
                Raylib.DrawRectangle((int)pos.X - 1, (int)pos.Y - 1, 2, 2, Pal.RGBA(30, 6, 6));
            }
    }

    static void DrawPathPreview(Game g)
    {
        if (g.PathPreview == null || g.PathPreview.Count == 0 || g.Selected == null) return;
        Vector2 prev = ElevCenter(g, g.Selected.X, g.Selected.Y);
        foreach (var (x, y) in g.PathPreview)
        {
            var c = ElevCenter(g, x, y);
            Raylib.DrawLineEx(prev, c, 2.5f, Raylib.Fade(Pal.Accent, 0.55f));
            prev = c;
        }
        foreach (var (x, y) in g.PathPreview)
        {
            var c = ElevCenter(g, x, y);
            Raylib.DrawCircleV(c, 3.5f, Raylib.Fade(Pal.Accent, 0.8f));
        }
    }

    static void DrawCover(Game g)
    {
        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
            {
                var t = g.Grid.Tiles[x, y];
                if (t == TileType.Floor) continue;
                var r = Util.TileRect(x, y);
                if (g.Grid.IsHigh(x, y)) r.Y -= ElevLift;   // sit cover on the plateau top
                bool high = t == TileType.HighCover;
                float inset = 5f;
                float lift = high ? 16f : 8f;
                var baseRect = new Rectangle(r.X + inset, r.Y + inset + lift,
                                             r.Width - inset * 2, r.Height - inset * 2 - lift);
                var topRect = new Rectangle(r.X + inset, r.Y + inset,
                                            r.Width - inset * 2, r.Height - inset * 2 - lift);
                // drop shadow
                Raylib.DrawRectangleRounded(
                    new Rectangle(baseRect.X + 3, baseRect.Y + 4, baseRect.Width, baseRect.Height),
                    0.18f, 5, Raylib.Fade(Pal.RGBA(0, 0, 0), 0.35f));
                Raylib.DrawRectangleRounded(baseRect, 0.18f, 5, high ? Pal.CoverHi : Pal.CoverLo);
                Raylib.DrawRectangleRounded(topRect, 0.22f, 5, high ? Pal.CoverHiTop : Pal.CoverLoTop);
                // subtle top edge highlight
                Raylib.DrawLineEx(new Vector2(topRect.X + 4, topRect.Y + 2),
                                  new Vector2(topRect.X + topRect.Width - 4, topRect.Y + 2),
                                  1.5f, Raylib.Fade(Pal.RGBA(255, 255, 255), 0.12f));
            }
    }

    static void DrawHoverAndShields(Game g)
    {
        // current cover of selected
        if (g.Selected != null && g.Selected.Team == Team.Player)
            DrawShields(g, g.Selected.X, g.Selected.Y, 0.5f);

        if (!g.IsPlayerInteractive()) return;
        if (g.HoverValid && g.Grid.IsFloor(g.HoverX, g.HoverY))
        {
            var r = ElevRect(g, g.HoverX, g.HoverY);
            Raylib.DrawRectangleLinesEx(new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2),
                                        2f, Raylib.Fade(Pal.Txt, 0.5f));
            // preview cover the selected unit would gain here
            if (g.Selected != null && g.MoveCost != null && g.MoveCost[g.HoverX, g.HoverY] > 0)
                DrawShields(g, g.HoverX, g.HoverY, 0.9f);
        }
    }

    static void DrawShields(Game g, int tx, int ty, float alpha)
    {
        int[,] dirs = { { 0, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 } };
        var center = ElevCenter(g, tx, ty);
        for (int i = 0; i < 4; i++)
        {
            int nx = tx + dirs[i, 0], ny = ty + dirs[i, 1];
            if (!g.Grid.IsCover(nx, ny)) continue;
            bool high = g.Grid.Tiles[nx, ny] == TileType.HighCover;
            var pos = center + new Vector2(dirs[i, 0], dirs[i, 1]) * (Cfg.Tile * 0.42f);
            Color col = high ? Pal.Good : Pal.Accent;
            if (high)
                Raylib.DrawPoly(pos, 4, 7f, 45f, Raylib.Fade(col, alpha));
            else
                Raylib.DrawPolyLinesEx(pos, 4, 7f, 45f, 2f, Raylib.Fade(col, alpha));
        }
    }

    static void DrawUnits(Game g)
    {
        foreach (var u in g.Enemies) DrawUnit(g, u);
        foreach (var u in g.Players) DrawUnit(g, u);
    }

    static void DrawUnit(Game g, Unit u)
    {
        if (!u.Alive) return;
        bool friend = u.Team == Team.Player;
        bool vip = friend && u.IsVip;
        bool dormant = u.Team == Team.Enemy && !u.Active;
        Color main = vip ? Pal.VipGold : (friend ? Pal.Friend : (dormant ? Pal.RGBA(120, 96, 96) : Pal.Foe));
        Color dark = vip ? Pal.VipDk  : (friend ? Pal.FriendDk : (dormant ? Pal.RGBA(46, 38, 42) : Pal.FoeDk));

        // lift the figure when it stands on raised terrain
        float hlift = g.Grid.IsHigh(u.X, u.Y) ? ElevLift : 0f;
        var foot = u.Pos - new Vector2(0, hlift);

        float bob = MathF.Sin((float)Raylib.GetTime() * 2.2f + u.Bob) * 1.6f;
        Vector2 p = foot + new Vector2(0, bob) + u.Recoil;

        // shadow (sits on the platform top when elevated)
        Raylib.DrawEllipse((int)foot.X, (int)(foot.Y + 17), 15, 6, Raylib.Fade(Pal.RGBA(0, 0, 0), 0.35f));

        // selection ring
        if (g.Selected == u)
        {
            float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 5f);
            Raylib.DrawRing(foot + new Vector2(0, 17), 17, 21, 0, 360, 48,
                            Raylib.Fade(Pal.Accent, 0.4f + 0.4f * pulse));
        }

        // body
        Raylib.DrawCircleV(p, 16f, dark);
        Raylib.DrawCircleV(p, 16f, Raylib.Fade(Pal.RGBA(0, 0, 0), 0f)); // no-op keep
        Raylib.DrawRing(p, 13.5f, 16.5f, 0, 360, 40, main);
        Raylib.DrawCircleV(p, 13.5f, Raylib.Fade(main, 0.18f));

        // class glyph
        int sides = u.Cls switch
        {
            "ASSAULT" => 3, "RANGER" => 3, "SHARPSHOOTER" => 4,
            "GUNNER" => 4, "BRUISER" => 6, "SCOUT" => 3, _ => 5,
        };
        float rot = u.Cls == "SHARPSHOOTER" ? 45f : (sides == 3 ? -90f : 0f);
        Raylib.DrawPoly(p, sides, 7.5f, rot, main);

        // dormant enemies: show an "unaware" marker, no facing/pips/status
        if (dormant)
        {
            Raylib.DrawText("?", (int)(p.X - 4), (int)(p.Y - 32), 18, Pal.TxtDim);
            return;
        }

        // facing tick
        var fdir = new Vector2(MathF.Cos(u.Facing), MathF.Sin(u.Facing));
        Raylib.DrawLineEx(p + fdir * 13f, p + fdir * 20f, 3f, main);

        // damage flash
        if (u.Flash > 0.01f)
            Raylib.DrawCircleV(p, 17f, Raylib.Fade(Pal.RGBA(255, 255, 255), u.Flash * 0.8f));

        // hp pips
        DrawHpPips(u, p);

        // status icons
        float ix = p.X - 10, iy = p.Y - 27;
        if (u.OnOverwatch)
        {
            Raylib.DrawCircle((int)p.X, (int)(p.Y - 26), 6f, Raylib.Fade(Pal.Accent, 0.25f));
            Raylib.DrawText("OW", (int)(p.X - 9), (int)(p.Y - 31), 10, Pal.Accent);
        }
        if (u.Hunkered)
            Raylib.DrawPoly(new Vector2(p.X, p.Y - 27), 4, 6f, 45f, Pal.Good);

        // VIP marker: gold diamond + tag above the asset
        if (vip)
        {
            Raylib.DrawPoly(new Vector2(p.X, p.Y - 39), 4, 5.5f, 45f, Pal.VipGold);
            Raylib.DrawPolyLinesEx(new Vector2(p.X, p.Y - 39), 4, 5.5f, 45f, 1.5f, Pal.Txt);
            Raylib.DrawText("VIP", (int)(p.X - 9), (int)(p.Y - 53), 12, Pal.VipGold);
        }
    }

    static void DrawHpPips(Unit u, Vector2 p)
    {
        int max = u.MaxHp;
        int per = max > 10 ? 2 : 1; // group hp if large
        int segs = (int)MathF.Ceiling(max / (float)per);
        float totalW = segs * 6f - 2f;
        float sx = p.X - totalW / 2f;
        float y = p.Y - 23f;
        for (int i = 0; i < segs; i++)
        {
            int hpAtSeg = (i + 1) * per;
            bool full = u.Hp >= hpAtSeg;
            bool partial = !full && u.Hp > i * per;
            Color c = full || partial
                ? (u.Team == Team.Player ? Pal.Good : Pal.Foe)
                : Pal.RGBA(40, 48, 60);
            Raylib.DrawRectangle((int)(sx + i * 6f), (int)y, 4, 4, c);
        }
    }

    static void DrawGrenade(Game g)
    {
        if (!g.GrenadeMode || g.Selected == null) return;
        var origin = g.Selected.Pos;

        // throw-range ring
        Raylib.DrawCircleLines((int)origin.X, (int)origin.Y, Game.GrenadeRange * Cfg.Tile,
                               Raylib.Fade(Pal.Accent, 0.35f));

        if (!g.HoverValid) return;
        Color col = g.GrenValid ? Pal.Accent : Pal.TxtDim;

        // blast preview (Chebyshev radius 1)
        for (int x = g.GrenTx - GrenadeAnim.Radius; x <= g.GrenTx + GrenadeAnim.Radius; x++)
            for (int y = g.GrenTy - GrenadeAnim.Radius; y <= g.GrenTy + GrenadeAnim.Radius; y++)
            {
                if (!g.Grid.InBounds(x, y)) continue;
                Raylib.DrawRectangleRec(Util.TileRect(x, y), Raylib.Fade(col, 0.22f));
            }

        var target = Util.TileCenter(g.GrenTx, g.GrenTy);
        // arc preview
        if (g.GrenValid)
        {
            Vector2 prev = origin;
            for (int i = 1; i <= 12; i++)
            {
                float k = i / 12f;
                var p = Vector2.Lerp(origin, target, k);
                p.Y -= MathF.Sin(k * MathF.PI) * 60f;
                Raylib.DrawLineEx(prev, p, 2f, Raylib.Fade(Pal.Accent, 0.5f));
                prev = p;
            }
        }
        Raylib.DrawCircleLines((int)target.X, (int)target.Y, 14, col);
        Raylib.DrawCircleLines((int)target.X, (int)target.Y, 4, col);
    }

    static void DrawAim(Game g)
    {
        if (!g.AimMode || g.AimTarget == null || g.Selected == null) return;
        var a = g.Selected.Pos;
        var d = g.AimTarget.Pos;
        Color col = g.AimValid ? Pal.Foe : Pal.TxtDim;
        Raylib.DrawLineEx(a, d, 1.6f, Raylib.Fade(col, 0.55f));

        float t = (float)Raylib.GetTime();
        float ang = t * 90f;
        Raylib.DrawRing(d, 18, 21, ang, ang + 60, 16, col);
        Raylib.DrawRing(d, 18, 21, ang + 120, ang + 180, 16, col);
        Raylib.DrawRing(d, 18, 21, ang + 240, ang + 300, 16, col);
        Raylib.DrawCircleLines((int)d.X, (int)d.Y, 24, Raylib.Fade(col, 0.6f));
    }
}
