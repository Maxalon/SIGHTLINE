using System;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// Draws the battlefield, units and tactical overlays.
public static class Renderer
{
    // how far raised terrain (and anything standing on it) lifts on screen
    public const float ElevLift = 8f;

    // --- 5.4 Procedural noise overlay -----------------------------------------
    // A 128x128 tiling Perlin-noise texture generated once after the GL context is
    // ready (lazy-init on the first DrawBoard call).  Drawn at low alpha over floor
    // tiles, cover tops and plateau top faces so each surface reads as a textured
    // material without fighting unit/threat/objective legibility.  Falls back to a
    // no-op if texture creation fails (never crashes).
    static Texture2D _noise;
    static bool _noiseReady;

    /// Free the GPU texture — call once after the window is closed.
    public static void UnloadNoise()
    {
        if (_noiseReady) { Raylib.UnloadTexture(_noise); _noiseReady = false; }
    }

    static void EnsureNoise()
    {
        if (_noiseReady) return;
        try
        {
            // scale ~4.5 gives medium-grain features across 128 texels — not too
            // fine (looks like static) and not too coarse (reads as splotchy).
            var img = Raylib.GenImagePerlinNoise(128, 128, 17, 43, 4.5f);
            _noise = Raylib.LoadTextureFromImage(img);
            Raylib.UnloadImage(img);
            Raylib.SetTextureWrap(_noise, TextureWrap.Repeat);
            _noiseReady = _noise.Id > 0;
        }
        catch { _noiseReady = false; }
    }

    // Tile the noise texture over a screen-space rectangle, anchored to the board
    // origin so adjacent tiles share the same underlying grain (no seams).
    // 'tint' is blended with white at 0.45 toward the biome colour; 'alpha' keeps
    // it subtle.  Using DrawTextureRec (CPU-side UV shift) rather than a sampler
    // because software GL (llvmpipe) doesn't honour TextureWrap in the shader path.
    static void DrawNoiseRect(Rectangle dst, Color tint, float alpha)
    {
        if (!_noiseReady) return;
        const float ts = 128f;
        // Offset relative to the BOARD ORIGIN (not absolute screen px): tile coords are
        // multiples of Cfg.Tile, so a board-aligned offset keeps the source rect inside the
        // texture (src + Tile <= 128) instead of overflowing on alternate rows -- which
        // software GL (llvmpipe) renders as a seam because it ignores TextureWrap on a
        // partial-rect sample. Still continuous across the board on real hardware. (Sprint 5 F3)
        float sx = (((dst.X - Cfg.OriginX) % ts) + ts) % ts;
        float sy = (((dst.Y - Cfg.OriginY) % ts) + ts) % ts;
        var src = new Rectangle(sx, sy, dst.Width, dst.Height);
        var col = Raylib.Fade(Pal.Mix(Color.White, tint, 0.45f), alpha);
        Raylib.DrawTextureRec(_noise, src, new Vector2(dst.X, dst.Y), col);
    }
    // --------------------------------------------------------------------------

    // 5.5: a tiny shape glyph for a status effect (drawn beside its code under the
    // figure) so the effect is distinguishable by SHAPE, not colour alone — flame /
    // droplet / star-burst / swirl. Centred at (gx, gy), ~5px, inherits the status colour.
    static void DrawStatusGlyph(StatusKind k, float gx, float gy, Color c)
    {
        switch (k)
        {
            case StatusKind.Burning:   // upward flame (triangle) with an inner flicker
                Raylib.DrawLineEx(new Vector2(gx, gy - 5f), new Vector2(gx + 4f, gy + 4f), 1.4f, c);
                Raylib.DrawLineEx(new Vector2(gx + 4f, gy + 4f), new Vector2(gx - 4f, gy + 4f), 1.4f, c);
                Raylib.DrawLineEx(new Vector2(gx - 4f, gy + 4f), new Vector2(gx, gy - 5f), 1.4f, c);
                Raylib.DrawLineEx(new Vector2(gx, gy), new Vector2(gx, gy + 4f), 1.2f, c);
                break;
            case StatusKind.Bleed:     // teardrop: rounded base + pointed top
                Raylib.DrawCircleV(new Vector2(gx, gy + 2f), 3f, c);
                Raylib.DrawLineEx(new Vector2(gx - 3f, gy + 1f), new Vector2(gx, gy - 5f), 1.3f, c);
                Raylib.DrawLineEx(new Vector2(gx + 3f, gy + 1f), new Vector2(gx, gy - 5f), 1.3f, c);
                break;
            case StatusKind.Stun:      // star-burst: four crossing strokes
                Raylib.DrawLineEx(new Vector2(gx, gy - 5f), new Vector2(gx, gy + 5f), 1.3f, c);
                Raylib.DrawLineEx(new Vector2(gx - 5f, gy), new Vector2(gx + 5f, gy), 1.3f, c);
                Raylib.DrawLineEx(new Vector2(gx - 3.5f, gy - 3.5f), new Vector2(gx + 3.5f, gy + 3.5f), 1.1f, c);
                Raylib.DrawLineEx(new Vector2(gx - 3.5f, gy + 3.5f), new Vector2(gx + 3.5f, gy - 3.5f), 1.1f, c);
                break;
            default:                   // Disoriented: an inward swirl (shrinking arc)
            {
                Vector2 prev = new Vector2(gx + 5f, gy);
                for (int i = 1; i <= 7; i++)
                {
                    float a = i * (MathF.PI * 1.6f / 7f);
                    float rr = 5f - i * 0.6f;
                    var pt = new Vector2(gx + MathF.Cos(a) * rr, gy + MathF.Sin(a) * rr);
                    Raylib.DrawLineEx(prev, pt, 1.3f, c);
                    prev = pt;
                }
                break;
            }
        }
    }

    // tile draw rect/centre offset up onto the plateau top when elevated (per height tier)
    static Rectangle ElevRect(Game g, int x, int y)
    {
        var r = Util.TileRect(x, y);
        r.Y -= g.Grid.HeightAt(x, y) * ElevLift;
        return r;
    }
    static Vector2 ElevCenter(Game g, int x, int y)
    {
        var c = Util.TileCenter(x, y);
        c.Y -= g.Grid.HeightAt(x, y) * ElevLift;
        return c;
    }

    public static void DrawBoard(Game g)
    {
        EnsureNoise();   // lazy-init the noise texture on first frame (no-op thereafter)
        var bm = g.Biome;
        // board backing
        var edge = new Rectangle(Cfg.OriginX - 6, Cfg.OriginY - 6, Cfg.BoardW + 12, Cfg.BoardH + 12);
        Raylib.DrawRectangleRounded(edge, 0.02f, 6, Pal.RGBA(7, 10, 14));
        Raylib.DrawRectangleLinesEx(edge, 2f, bm.Edge);

        // floor (biome-tinted checker)
        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
            {
                if (g.Grid.Tiles[x, y] != TileType.Floor) continue;
                var r = Util.TileRect(x, y);
                Raylib.DrawRectangleRec(r, ((x + y) & 1) == 0 ? bm.FloorA : bm.FloorB);
            }

        // 5.4: subtle noise grain over the floor so it reads as material, not flat colour.
        // Alpha 0.09 keeps it well below signal level — squint test still passes.
        if (_noiseReady)
            for (int x = 0; x < g.Grid.W; x++)
                for (int y = 0; y < g.Grid.H; y++)
                {
                    if (g.Grid.Tiles[x, y] != TileType.Floor) continue;
                    DrawNoiseRect(Util.TileRect(x, y), bm.Tint, 0.09f);
                }

        DrawElevation(g);
        DrawMoveOverlay(g);
        DrawOverwatchThreat(g);   // tiles each active overwatching enemy covers (reaction-fire danger)
        DrawThreat(g);
        DrawEvac(g);
        DrawTerminal(g);
        DrawSabotage(g);
        DrawGridLines(g);
        DrawPathPreview(g);
        DrawCover(g);
        DrawHoverAndShields(g);
        DrawKbCursor(g);
        DrawEnemyIntent(g);       // telegraph: the acting hostile's planned move + target + threat
        DrawUnits(g);
        DrawSmoke(g);
        DrawAim(g);
        DrawGrenade(g);
        DrawItem(g);

        g.ActiveAnim?.Draw(g);
        g.Fx.Draw();
        g.Fx.DrawText();
    }

    // Raised plateaus: faux-3D platform with a front wall + lit top edge so the
    // high ground reads clearly. Drawn back-to-front (top rows first).
    static void DrawElevation(Game g)
    {
        // biome-tinted plateau faces (keeps each mission reading as a distinct place)
        Color tint = g.Biome.Tint;
        Color hiA = Pal.Mix(Pal.HighA, tint, 0.34f);
        Color hiB = Pal.Mix(Pal.HighB, tint, 0.34f);
        for (int y = 0; y < g.Grid.H; y++)
            for (int x = 0; x < g.Grid.W; x++)
            {
                int h = g.Grid.HeightAt(x, y);
                if (h <= 0) continue;
                var r = Util.TileRect(x, y);
                float lift = h * ElevLift;
                // exposed front wall down to whatever the tile below sits at (taller for tier 2)
                int belowH = g.Grid.HeightAt(x, y + 1);
                if (belowH < h)
                    Raylib.DrawRectangleRec(
                        new Rectangle(r.X, r.Y + r.Height - lift, r.Width, (h - belowH) * ElevLift + 3),
                        Pal.HighSide);
                // raised top face — tier 2 reads a touch brighter so the height tier is legible
                var top = new Rectangle(r.X, r.Y - lift, r.Width, r.Height);
                Color ca = h >= 2 ? Pal.Mix(hiA, Pal.RGBA(255, 255, 255), 0.12f) : hiA;
                Color cb = h >= 2 ? Pal.Mix(hiB, Pal.RGBA(255, 255, 255), 0.12f) : hiB;
                Raylib.DrawRectangleRec(top, ((x + y) & 1) == 0 ? ca : cb);
                // contact shadow at the base of the front wall — grounds the plateau
                if (belowH < h)
                    Raylib.DrawRectangleRec(
                        new Rectangle(r.X + 2, r.Y + r.Height - lift + (h - belowH) * ElevLift + 2, r.Width - 4, 5),
                        Raylib.Fade(Pal.RGBA(0, 0, 0), 0.28f));
                // 5.4: noise grain on the plateau top so it reads as raised stone/metal
                DrawNoiseRect(top, tint, 0.11f);
                // lit front edge of the top face (base glow at alpha 0.50)
                Raylib.DrawLineEx(new Vector2(top.X, top.Y + top.Height - 1),
                                  new Vector2(top.X + top.Width, top.Y + top.Height - 1),
                                  2f, Raylib.Fade(Pal.HighEdge, 0.5f));
                // emissive rim: a narrow bright inner accent — the 5.2 bloom will catch this on hardware
                Raylib.DrawLineEx(new Vector2(top.X + 1, top.Y + top.Height - 2),
                                  new Vector2(top.X + top.Width - 1, top.Y + top.Height - 2),
                                  1f, Raylib.Fade(Pal.RGBA(200, 230, 255), h >= 2 ? 0.55f : 0.40f));
                // top-edge highlight where it meets a lower tile above
                if (g.Grid.HeightAt(x, y - 1) < h)
                {
                    Raylib.DrawLineEx(new Vector2(top.X, top.Y),
                                      new Vector2(top.X + top.Width, top.Y),
                                      1.5f, Raylib.Fade(Pal.HighEdge, 0.35f));
                    // emissive rim on the exposed top edge (slightly brighter, 1px inner)
                    Raylib.DrawLineEx(new Vector2(top.X + 1, top.Y + 1),
                                      new Vector2(top.X + top.Width - 1, top.Y + 1),
                                      1f, Raylib.Fade(Pal.RGBA(200, 230, 255), h >= 2 ? 0.45f : 0.30f));
                }
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
        Raylib.DrawTextEx(Cfg.Font, "EVAC", new Vector2((int)at.X - 4, (int)(at.Y - Cfg.Tile / 2 + 4)), 14, 1f, Pal.Good);
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

        Raylib.DrawTextEx(Cfg.Font, "TERMINAL", new Vector2((int)c.X - 26, (int)r.Y - 13), 11, 1f, col);
    }

    // SABOTAGE charge sites: a blinking demolition console per site; armed once planted.
    static void DrawSabotage(Game g)
    {
        if (!g.HasSabotage) return;
        for (int i = 0; i < g.SabotageSites.Count; i++)
        {
            var (tx, ty) = g.SabotageSites[i];
            bool blown = g.SabotageBlown.Contains(i);
            var r = ElevRect(g, tx, ty);
            var c = ElevCenter(g, tx, ty);
            Color col = blown ? Pal.Good : Pal.Foe;
            float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 3f + i);

            Raylib.DrawRectangleRec(r, Raylib.Fade(col, 0.07f + (blown ? 0f : 0.06f * pulse)));
            Raylib.DrawRectangleLinesEx(new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6),
                                        2f, Raylib.Fade(col, blown ? 0.35f : 0.45f + 0.4f * pulse));
            // charge box + light
            Raylib.DrawRectangleRec(new Rectangle(c.X - 8, c.Y - 9, 16, 18), Pal.RGBA(14, 20, 28));
            Raylib.DrawRectangleLinesEx(new Rectangle(c.X - 8, c.Y - 9, 16, 18), 1.5f, col);
            Raylib.DrawCircleV(new Vector2(c.X, c.Y), 3.5f, Raylib.Fade(col, blown ? 0.9f : 0.5f + 0.5f * pulse));

            Raylib.DrawTextEx(Cfg.Font, blown ? "ARMED" : "CHARGE", new Vector2((int)c.X - 18, (int)r.Y - 13), 10, 1f, col);
        }
    }

    static void DrawGridLines(Game g)
    {
        Color gl = g.Biome.Grid;
        for (int x = 0; x <= g.Grid.W; x++)
            Raylib.DrawLineEx(new Vector2(Cfg.OriginX + x * Cfg.Tile, Cfg.OriginY),
                              new Vector2(Cfg.OriginX + x * Cfg.Tile, Cfg.OriginY + Cfg.BoardH),
                              1f, gl);
        for (int y = 0; y <= g.Grid.H; y++)
            Raylib.DrawLineEx(new Vector2(Cfg.OriginX, Cfg.OriginY + y * Cfg.Tile),
                              new Vector2(Cfg.OriginX + Cfg.BoardW, Cfg.OriginY + y * Cfg.Tile),
                              1f, gl);
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

    // Enemy-overwatch danger overlay (player turn only): every Active enemy that is on
    // OVERWATCH will REACT-FIRE at the first soldier who moves into a tile it can see+hit.
    // That reaction is otherwise invisible, so wash the watched tiles in a faint danger-red
    // and mark each overwatcher with a reticle. A tile is "watched" iff it mirrors exactly
    // what Game.OnUnitEnteredTile / CanTarget test for a reaction: the enemy has ammo, the
    // tile is within its weapon MaxRange (Euclidean, matching Util.TileDist) and the enemy
    // has line of sight to it (with the same commanding-height-over-high-cover rule). So the
    // overlay never lies — a tile lit here is a tile that genuinely draws a reaction shot.
    //
    // Kept deliberately SUBTLE (low alpha) and visually DISTINCT from DrawThreat's corner
    // pips: this is a soft full-tile wash + a watcher reticle, not a per-tile triangle, so a
    // squint still reads the selected unit, the nearest foe and the objective first.
    static void DrawOverwatchThreat(Game g)
    {
        if (g.Phase != Phase.PlayerTurn) return;

        // collect the live overwatchers once (cheap; usually 0-2)
        System.Collections.Generic.List<Unit> watchers = null;
        foreach (var e in g.Enemies)
        {
            if (!e.Alive || !e.Active || !e.OnOverwatch || e.Ammo <= 0) continue;
            (watchers ??= new System.Collections.Generic.List<Unit>()).Add(e);
        }
        if (watchers == null) return;

        float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 3.2f);
        // soft red wash — well below signal level so it informs without dominating the board
        Color wash = Raylib.Fade(Pal.Foe, 0.055f + 0.045f * pulse);

        // wash every watched tile (a tile may be watched by more than one enemy — the
        // overlapping fills naturally read as a denser, more dangerous kill-zone)
        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
            {
                if (!g.Grid.IsFloor(x, y)) continue;   // only walkable tiles can be moved into
                foreach (var w in watchers)
                {
                    if (Util.TileDist(w.X, w.Y, x, y) > w.Weapon.MaxRange) continue;
                    bool commanding = g.Grid.HeightAt(w.X, w.Y) - g.Grid.HeightAt(x, y) >= 2;
                    if (!g.Grid.HasLineOfSight(w.X, w.Y, x, y, commanding)) continue;
                    var r = ElevRect(g, x, y);
                    Raylib.DrawRectangleRec(r, wash);
                    break;   // one wash per tile is enough; overlap is conveyed by adjacency
                }
            }

        // mark each overwatcher with a danger reticle so the SOURCE of the kill-zone reads
        foreach (var w in watchers)
        {
            float hlift = g.Grid.IsHigh(w.X, w.Y) ? ElevLift : 0f;
            var c = w.Pos - new Vector2(0, hlift + 30f);   // float the reticle just above the figure
            Color rc = Raylib.Fade(Pal.Foe, 0.45f + 0.35f * pulse);
            Raylib.DrawRing(c, 7.5f, 9f, 0, 360, 28, rc);
            // crosshair ticks
            Raylib.DrawLineEx(new Vector2(c.X - 11f, c.Y), new Vector2(c.X - 5f, c.Y), 1.6f, rc);
            Raylib.DrawLineEx(new Vector2(c.X + 5f, c.Y), new Vector2(c.X + 11f, c.Y), 1.6f, rc);
            Raylib.DrawLineEx(new Vector2(c.X, c.Y - 11f), new Vector2(c.X, c.Y - 5f), 1.6f, rc);
            Raylib.DrawLineEx(new Vector2(c.X, c.Y + 5f), new Vector2(c.X, c.Y + 11f), 1.6f, rc);
            Raylib.DrawCircleV(c, 1.6f, rc);
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

    // Enemy-intent telegraph (the Into-the-Breach fairness lever). During the brief beat before
    // a hostile acts, Game sets IntentUnit + IntentPlan (the SAME plan it then executes). We draw,
    // in semantic enemy-red:
    //   * the intended MOVE PATH as an animated dashed line from the unit through its plan Path;
    //   * a soft wash on the tiles the unit will THREATEN from its post-move (IntentDest) tile;
    //   * a bold reticle/X on its TARGET (the soldier it shoots, or the grenade/item tile).
    // It's deliberately bold (this is the "it's about to hit you" warning) but on-theme, and it
    // clears the instant the beat ends (Game.ClearIntent) so it never lingers into the action.
    static void DrawEnemyIntent(Game g)
    {
        var e = g.IntentUnit;
        var plan = g.IntentPlan;
        if (e == null || plan == null || !e.Alive) return;

        float t = (float)Raylib.GetTime();
        float pulse = 0.6f + 0.4f * MathF.Sin(t * 5f);
        Color danger = Pal.Foe;

        var (dx, dy) = g.IntentDest;            // where the unit will stand after moving

        // 1) THREATENED TILES — a faint red wash on every floor tile the unit could fire on from
        // its destination (same LoS+range test the renderer uses elsewhere). Shows the kill-zone
        // the move creates. Skipped for pure support plans (medic) where there's no shot threat.
        bool support = plan.HealTarget != null && plan.ShootTarget == null && !plan.Grenade;
        if (!support && e.Weapon != null)
        {
            Color wash = Raylib.Fade(danger, 0.05f + 0.04f * pulse);
            int maxR = e.Weapon.MaxRange;
            for (int x = 0; x < g.Grid.W; x++)
                for (int y = 0; y < g.Grid.H; y++)
                {
                    if (!g.Grid.IsFloor(x, y)) continue;
                    if (Util.TileDist(dx, dy, x, y) > maxR) continue;
                    bool commanding = g.Grid.HeightAt(dx, dy) - g.Grid.HeightAt(x, y) >= 2;
                    if (!g.Grid.HasLineOfSight(dx, dy, x, y, commanding)) continue;
                    Raylib.DrawRectangleRec(ElevRect(g, x, y), wash);
                }
        }

        // 2) MOVE PATH — an animated dashed red line from the unit through each planned tile,
        // ending in a chevron stack at the destination. Dashes scroll along the path to read as
        // motion/intent. Only drawn when the unit actually moves.
        if (plan.Path.Count > 0)
        {
            Vector2 prev = ElevCenter(g, e.X, e.Y);
            float phase = (t * 26f) % 16f;        // scrolling dash offset
            foreach (var (px, py) in plan.Path)
            {
                Vector2 cur = ElevCenter(g, px, py);
                DashedLine(prev, cur, 3f, Raylib.Fade(danger, 0.85f), phase, 9f, 7f);
                prev = cur;
            }
            // destination marker: a pulsing ring footprint where the unit ends up
            Vector2 dest = ElevCenter(g, dx, dy);
            Raylib.DrawRing(dest, 9f, 11f, 0, 360, 28, Raylib.Fade(danger, 0.5f + 0.35f * pulse));
            Raylib.DrawCircleV(dest, 3f, Raylib.Fade(danger, 0.85f));
        }

        // 3) TARGET MARKER — the most important read: WHO/WHERE the attack lands.
        bool haveTarget = false; Vector2 tc = default; float reach = 0f;
        if (plan.ShootTarget != null && plan.ShootTarget.Alive)
        {
            tc = ElevCenter(g, plan.ShootTarget.X, plan.ShootTarget.Y);
            reach = 17f; haveTarget = true;
        }
        else if (plan.Grenade)
        {
            tc = ElevCenter(g, plan.GrenX, plan.GrenY);
            reach = 19f; haveTarget = true;
            // grenade blast footprint (Chebyshev radius 1) so the player sees the splash
            for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                {
                    int bx = plan.GrenX + ox, by = plan.GrenY + oy;
                    if (!g.Grid.InBounds(bx, by)) continue;
                    Raylib.DrawRectangleRec(ElevRect(g, bx, by), Raylib.Fade(danger, 0.10f + 0.06f * pulse));
                }
        }
        else if (plan.UseItem && g.Grid.InBounds(plan.ItemTx, plan.ItemTy))
        {
            tc = ElevCenter(g, plan.ItemTx, plan.ItemTy);
            reach = 17f; haveTarget = true;
        }

        if (haveTarget)
        {
            // aim-line from the firer's post-move position to the mark, then a bold reticle + X
            Vector2 from = ElevCenter(g, dx, dy);
            Raylib.DrawLineEx(from, tc, 1.6f, Raylib.Fade(danger, 0.45f));
            float rr = reach + 2.5f * pulse;
            Raylib.DrawRing(tc, rr - 2f, rr, 0, 360, 32, Raylib.Fade(danger, 0.9f));
            float k = rr * 0.7f;
            Raylib.DrawLineEx(new Vector2(tc.X - k, tc.Y - k), new Vector2(tc.X + k, tc.Y + k), 2.4f, Raylib.Fade(danger, 0.95f));
            Raylib.DrawLineEx(new Vector2(tc.X - k, tc.Y + k), new Vector2(tc.X + k, tc.Y - k), 2.4f, Raylib.Fade(danger, 0.95f));
            Raylib.DrawCircleV(tc, 2.2f, Raylib.Fade(Pal.RGBA(255, 220, 220), 0.9f));
        }

        // 4) a small intent caption above the acting unit so the plan reads at a glance
        string verb = plan.SapTile != null ? "BREACH"
                    : plan.HealTarget != null ? "MEND"
                    : plan.Grenade ? "FRAG"
                    : plan.UseItem ? (e.EnemyItem == ItemKind.Smoke ? "SMOKE" : "FLASH")
                    : plan.ShootTarget != null ? "FIRING"
                    : plan.Overwatch ? "OVERWATCH"
                    : plan.Path.Count > 0 ? "MOVING"
                    : plan.Hunker ? "HUNKER" : "HOLD";
        Vector2 cap = e.Pos - new Vector2(0, (g.Grid.IsHigh(e.X, e.Y) ? ElevLift : 0f) + 44f);
        var sz = Raylib.MeasureTextEx(Cfg.Font, verb, 14f, 1f);
        Raylib.DrawRectangleRec(new Rectangle(cap.X - sz.X / 2f - 4, cap.Y - 1, sz.X + 8, sz.Y + 2),
                                Raylib.Fade(Pal.RGBA(20, 4, 4), 0.7f));
        Raylib.DrawTextEx(Cfg.Font, verb, new Vector2(cap.X - sz.X / 2f, cap.Y), 14f, 1f,
                          Raylib.Fade(Pal.RGBA(255, 210, 210), 0.95f));
    }

    // A scrolling dashed line between two points (used by the intent telegraph's move path).
    // `phase` shifts the dash pattern along the segment for an animated "marching ants" feel.
    static void DashedLine(Vector2 a, Vector2 b, float thick, Color col, float phase, float dash, float gap)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 0.001f) return;
        Vector2 dir = d / len;
        float period = dash + gap;
        float s = -((phase) % period);
        while (s < len)
        {
            float s0 = MathF.Max(s, 0f);
            float s1 = MathF.Min(s + dash, len);
            if (s1 > s0)
                Raylib.DrawLineEx(a + dir * s0, a + dir * s1, thick, col);
            s += period;
        }
    }

    static void DrawCover(Game g)
    {
        // blend the neutral cover palette toward the biome hue
        Color tint = g.Biome.Tint;
        Color cHi = Pal.Mix(Pal.CoverHi, tint, 0.28f), cHiTop = Pal.Mix(Pal.CoverHiTop, tint, 0.28f);
        Color cLo = Pal.Mix(Pal.CoverLo, tint, 0.28f), cLoTop = Pal.Mix(Pal.CoverLoTop, tint, 0.28f);
        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
            {
                var t = g.Grid.Tiles[x, y];
                if (t == TileType.Floor) continue;
                var r = Util.TileRect(x, y);
                r.Y -= g.Grid.HeightAt(x, y) * ElevLift;   // sit cover on the plateau top (per tier)
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
                Raylib.DrawRectangleRounded(baseRect, 0.18f, 5, high ? cHi : cLo);
                // contact shadow at the base of the cover block — grounds it against the floor
                Raylib.DrawRectangleRec(
                    new Rectangle(baseRect.X + 4, baseRect.Y + baseRect.Height - 1, baseRect.Width - 8, 4),
                    Raylib.Fade(Pal.RGBA(0, 0, 0), 0.22f));
                Raylib.DrawRectangleRounded(topRect, 0.22f, 5, high ? cHiTop : cLoTop);
                // 5.4: noise grain on the top face so cover reads as a physical object
                DrawNoiseRect(topRect, tint, 0.10f);
                // subtle top edge highlight (existing soft white gleam)
                Raylib.DrawLineEx(new Vector2(topRect.X + 4, topRect.Y + 2),
                                  new Vector2(topRect.X + topRect.Width - 4, topRect.Y + 2),
                                  1.5f, Raylib.Fade(Pal.RGBA(255, 255, 255), 0.12f));
                // emissive rim — a warm bright accent on the upper edge so cover glows subtly;
                // the 5.2 post-FX bloom will amplify this on hardware.  Keep alpha modest so
                // the △/— shape cues still dominate.
                Color rimCol = Pal.Mix(Pal.HighEdge, Pal.RGBA(255, 255, 255), 0.45f);
                Raylib.DrawLineEx(new Vector2(topRect.X + 5, topRect.Y + 3),
                                  new Vector2(topRect.X + topRect.Width - 5, topRect.Y + 3),
                                  1f, Raylib.Fade(rimCol, high ? 0.30f : 0.20f));
                // damage state (3.6): a chipped-but-not-yet-degraded block shows fissures
                if (g.Grid.CoverHp[x, y] > 0 && g.Grid.CoverHp[x, y] < g.Grid.MaxCoverHp(x, y))
                {
                    Color crack = Pal.RGBA(14, 17, 23);
                    float my = topRect.Y + topRect.Height * 0.55f;
                    float mx = topRect.X + topRect.Width * 0.5f;
                    Raylib.DrawLineEx(new Vector2(topRect.X + 5, topRect.Y + 6), new Vector2(mx, my), 1.6f, crack);
                    Raylib.DrawLineEx(new Vector2(mx, my), new Vector2(topRect.X + topRect.Width - 6, topRect.Y + 9), 1.6f, crack);
                    Raylib.DrawLineEx(new Vector2(mx, my), new Vector2(mx - 4, topRect.Y + topRect.Height - 4), 1.4f, crack);
                }

                // S4-B shape-redundancy cue: HIGH cover gets a small upward chevron/triangle on
                // its top face; LOW cover gets a short horizontal bar.  Both drawn at low alpha so
                // they stay subtle and don't clutter the board — but they let the two cover tiers
                // be distinguished by SHAPE alone (e.g. in colorblind mode or when squinting).
                // Peak-up triangle = tall/full shield; flat bar = low/half cover.
                {
                    float cx = topRect.X + topRect.Width  * 0.5f;
                    float cy = topRect.Y + topRect.Height * 0.72f;   // lower third of top face
                    Color cue = Raylib.Fade(Pal.RGBA(255, 255, 255), 0.19f);
                    if (high)
                    {
                        // Upward chevron: two lines from base corners meeting at a peak
                        float halfW = 7f, ht = 9f;
                        var peak   = new Vector2(cx,          cy - ht);
                        var bLeft  = new Vector2(cx - halfW,  cy);
                        var bRight = new Vector2(cx + halfW,  cy);
                        Raylib.DrawLineEx(bLeft,  peak,   1.8f, cue);
                        Raylib.DrawLineEx(peak,   bRight, 1.8f, cue);
                        Raylib.DrawLineEx(bLeft,  bRight, 1.4f, cue);  // base closes the triangle
                    }
                    else
                    {
                        // Single flat bar — low / half-cover
                        Raylib.DrawLineEx(new Vector2(cx - 9f, cy), new Vector2(cx + 9f, cy), 2.5f, cue);
                    }
                }
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

    // Keyboard tile cursor: animated corner-bracket reticle on the active tile.
    static void DrawKbCursor(Game g)
    {
        if (!g.KbCursor || !g.Grid.InBounds(g.CurX, g.CurY)) return;
        var r = ElevRect(g, g.CurX, g.CurY);
        float p = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 6f);
        Color c = Raylib.Fade(Pal.Accent, 0.55f + 0.45f * p);
        float L = 11f, m = 2f;
        float x0 = r.X + m, y0 = r.Y + m, x1 = r.X + r.Width - m, y1 = r.Y + r.Height - m;
        Raylib.DrawLineEx(new Vector2(x0, y0), new Vector2(x0 + L, y0), 2.5f, c);
        Raylib.DrawLineEx(new Vector2(x0, y0), new Vector2(x0, y0 + L), 2.5f, c);
        Raylib.DrawLineEx(new Vector2(x1, y0), new Vector2(x1 - L, y0), 2.5f, c);
        Raylib.DrawLineEx(new Vector2(x1, y0), new Vector2(x1, y0 + L), 2.5f, c);
        Raylib.DrawLineEx(new Vector2(x0, y1), new Vector2(x0 + L, y1), 2.5f, c);
        Raylib.DrawLineEx(new Vector2(x0, y1), new Vector2(x0, y1 - L), 2.5f, c);
        Raylib.DrawLineEx(new Vector2(x1, y1), new Vector2(x1 - L, y1), 2.5f, c);
        Raylib.DrawLineEx(new Vector2(x1, y1), new Vector2(x1, y1 - L), 2.5f, c);
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

    // Nearest living enemy to a player unit (by tile distance) — used to point the selected
    // soldier's aim-tick at who it's about to engage. Returns null if no foe is alive.
    static Unit NearestLiveFoe(Game g, Unit u)
    {
        Unit best = null; float bestD = float.MaxValue;
        var foes = u.Team == Team.Player ? g.Enemies : g.Players;
        foreach (var f in foes)
        {
            if (!f.Alive) continue;
            float d = Util.TileDist(u.X, u.Y, f.X, f.Y);
            if (d < bestD) { bestD = d; best = f; }
        }
        return best;
    }

    static void DrawUnit(Game g, Unit u)
    {
        if (!u.Alive) return;
        bool friend = u.Team == Team.Player;
        bool vip = friend && u.IsVip;
        bool elite = u.Team == Team.Enemy && u.Cls == "ELITE";
        // 4.3 awareness tiers: Unaware (grey "?") / Suspicious (amber "!") / Alert (live foe)
        bool unaware    = u.Team == Team.Enemy && u.Alert == AlertLevel.Unaware;
        bool suspicious = u.Team == Team.Enemy && u.Alert == AlertLevel.Suspicious;
        bool inactive   = unaware || suspicious;   // not yet a live combatant: no facing/pips
        Color main = vip ? Pal.VipGold : (friend ? Pal.Friend : (unaware ? Pal.RGBA(120, 96, 96) : (suspicious ? Pal.Suspect : (elite ? Pal.Elite : Pal.Foe))));
        Color dark = vip ? Pal.VipDk  : (friend ? Pal.FriendDk : (unaware ? Pal.RGBA(46, 38, 42) : (suspicious ? Pal.SuspectDk : (elite ? Pal.EliteDk : Pal.FoeDk))));

        // 5.3-B focal-point alpha: selected unit = full; spent players dimmed; enemies visible.
        // HP bar, rings, status codes, alert markers, VIP markers stay full-alpha (they are signal).
        float figAlpha;
        if (g.Selected == u)              figAlpha = 1.0f;          // selected: full brightness
        else if (friend && !u.CanAct)     figAlpha = 0.60f;          // spent player: visibly dimmed
        else if (friend)                  figAlpha = 0.82f;          // other player: gently dimmed
        else                              figAlpha = 0.85f;          // enemies: barely dimmed (must spot threats)

        // lift the figure when it stands on raised terrain
        float hlift = g.Grid.IsHigh(u.X, u.Y) ? ElevLift : 0f;
        var foot = u.Pos - new Vector2(0, hlift);

        // a DRONE hovers above its shadow (reads as airborne)
        bool drone = u.Team == Team.Enemy && u.Cls == "DRONE";
        float hover = drone ? 11f + MathF.Sin((float)Raylib.GetTime() * 3f + u.Bob) * 2f : 0f;

        float bob = MathF.Sin((float)Raylib.GetTime() * 2.2f + u.Bob) * 1.6f;
        Vector2 p = foot + new Vector2(0, bob - hover) + u.Recoil;

        // shadow (sits on the platform top when elevated)
        Raylib.DrawEllipse((int)foot.X, (int)(foot.Y + 17), 15, 6, Raylib.Fade(Pal.RGBA(0, 0, 0), 0.35f * figAlpha));

        // selection ring — full strength (signal), plus a faint extra glow on the selected unit
        if (g.Selected == u)
        {
            float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 5f);
            // soft brightening halo so the selected unit pops further
            Raylib.DrawRing(foot + new Vector2(0, 17), 22f, 27f, 0, 360, 48,
                            Raylib.Fade(Pal.Accent, 0.12f + 0.10f * pulse));
            Raylib.DrawRing(foot + new Vector2(0, 17), 17, 21, 0, 360, 48,
                            Raylib.Fade(Pal.Accent, 0.4f + 0.4f * pulse));
        }

        // 4.4 ghost ring: soft pulsing ring on friendly units while the squad is concealed
        if (friend && !vip && g.SquadConcealed)
        {
            float pulse = 0.3f + 0.3f * MathF.Sin((float)Raylib.GetTime() * 2.8f + u.Bob);
            Raylib.DrawRing(foot + new Vector2(0, 17), 20f, 23f, 0, 360, 40,
                            Raylib.Fade(Pal.Friend, pulse));
        }

        // body — apply figAlpha to the figure shape
        Raylib.DrawCircleV(p, 16f, Raylib.Fade(dark, figAlpha));
        Raylib.DrawCircleV(p, 16f, Raylib.Fade(Pal.RGBA(0, 0, 0), 0f)); // no-op keep
        Raylib.DrawRing(p, 13.5f, 16.5f, 0, 360, 40, Raylib.Fade(main, figAlpha));
        Raylib.DrawCircleV(p, 13.5f, Raylib.Fade(main, 0.18f * figAlpha));

        // class glyph
        int sides = u.Cls switch
        {
            "ASSAULT" => 3, "RANGER" => 3, "SHARPSHOOTER" => 4,
            "GUNNER" => 4, "BRUISER" => 6, "SCOUT" => 3,
            "SNIPER" => 4, "TURRET" => 4, "BERSERKER" => 6, "ELITE" => 8, "MEDIC" => 4,
            "DRONE" => 4, "SHIELD" => 6, "SAPPER" => 3, "HUNTER" => 3, "MORTAR" => 5, _ => 5,
        };
        float rot = (u.Cls == "SHARPSHOOTER" || u.Cls == "SNIPER" || u.Cls == "DRONE") ? 45f : (sides == 3 ? -90f : 0f);
        if (elite) Raylib.DrawRing(p, 18f, 20.5f, 0, 360, 40, Raylib.Fade(Pal.Elite, 0.55f * figAlpha));
        Raylib.DrawPoly(p, sides, elite ? 9f : 7.5f, rot, Raylib.Fade(main, figAlpha));

        // not-yet-engaged enemies: an awareness marker, no facing/pips/status
        if (inactive)
        {
            if (suspicious)
            {
                // pulsing amber ring + "!" so being spotted reads instantly as a warning
                float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 6f);
                Raylib.DrawRing(p, 18f, 21f, 0, 360, 40, Raylib.Fade(Pal.Suspect, 0.30f + 0.45f * pulse));
                Raylib.DrawTextEx(Cfg.Font, "!", new Vector2((int)(p.X - 2), (int)(p.Y - 33)), 20, 1f, Pal.Suspect);
            }
            else Raylib.DrawTextEx(Cfg.Font, "?", new Vector2((int)(p.X - 4), (int)(p.Y - 32)), 18, 1f, Pal.TxtDim);
            return;
        }

        // facing tick — a short "barrel" along the unit's facing. Active enemies get a small
        // arrowhead so which way a foe is pointing (and thus where its overwatch/fire faces)
        // reads at a glance; the selected soldier instead points its aim-tick at the nearest
        // live foe (a clear "who am I about to shoot" cue) regardless of its idle facing.
        float aimAng = u.Facing;
        if (g.Selected == u && friend && !vip)
        {
            var foe = NearestLiveFoe(g, u);
            if (foe != null)
            {
                var ad = foe.Pos - p;
                if (ad.LengthSquared() > 0.01f) aimAng = MathF.Atan2(ad.Y, ad.X);
            }
        }
        var fdir = new Vector2(MathF.Cos(aimAng), MathF.Sin(aimAng));
        var perp = new Vector2(-fdir.Y, fdir.X);
        Raylib.DrawLineEx(p + fdir * 13f, p + fdir * 21f, 3f, Raylib.Fade(main, figAlpha));
        if (g.Selected == u && friend && !vip)
        {
            // a soft directional aim chevron a little further out, pointing at the target
            var tip = p + fdir * 27f;
            Raylib.DrawLineEx(tip, tip - fdir * 6f + perp * 5f, 2f, Raylib.Fade(main, 0.85f * figAlpha));
            Raylib.DrawLineEx(tip, tip - fdir * 6f - perp * 5f, 2f, Raylib.Fade(main, 0.85f * figAlpha));
        }
        else if (u.Team == Team.Enemy)
        {
            // tiny arrowhead on the barrel so enemy facing is unmistakable
            var tip = p + fdir * 21f;
            Raylib.DrawLineEx(tip, tip - fdir * 4.5f + perp * 3.5f, 2f, Raylib.Fade(main, figAlpha));
            Raylib.DrawLineEx(tip, tip - fdir * 4.5f - perp * 3.5f, 2f, Raylib.Fade(main, figAlpha));
        }

        // medic: green cross marker so the support unit reads at a glance
        if (u.Team == Team.Enemy && u.Cls == "MEDIC")
        {
            Raylib.DrawRectangle((int)p.X - 1, (int)p.Y - 5, 3, 11, Raylib.Fade(Pal.Good, figAlpha));
            Raylib.DrawRectangle((int)p.X - 5, (int)p.Y - 1, 11, 3, Raylib.Fade(Pal.Good, figAlpha));
        }

        // sapper: a small demolition-charge marker so it reads as a cover-breaker
        if (u.Team == Team.Enemy && u.Cls == "SAPPER")
        {
            Raylib.DrawRectangleLines((int)p.X - 4, (int)p.Y - 4, 8, 8, Raylib.Fade(Pal.Accent, figAlpha));
            Raylib.DrawCircleV(new Vector2(p.X + 4, p.Y - 4), 2f, Raylib.Fade(Pal.Foe, figAlpha));
        }

        // hunter: twin forward "speed" chevrons along its facing so it reads as a fast flanker
        // (distinct from the plain scout/grunt triangle). They point the way it's curling.
        if (u.Team == Team.Enemy && u.Cls == "HUNTER")
        {
            var hdir = new Vector2(MathF.Cos(u.Facing), MathF.Sin(u.Facing));
            var hperp = new Vector2(-hdir.Y, hdir.X);
            for (int k = 0; k < 2; k++)
            {
                var bse = p + hdir * (4f + k * 5f);     // two stacked chevrons
                var nose = bse + hdir * 4.5f;
                Raylib.DrawLineEx(nose, bse + hperp * 4.5f, 2f, Raylib.Fade(main, figAlpha));
                Raylib.DrawLineEx(nose, bse - hperp * 4.5f, 2f, Raylib.Fade(main, figAlpha));
            }
        }

        // mortar: a lob-arc + shell marker above the figure so it reads as a back-line grenadier
        if (u.Team == Team.Enemy && u.Cls == "MORTAR")
        {
            // a small parabolic arc traced over the unit
            Vector2 a0 = new Vector2(p.X - 8, p.Y - 4), a1 = new Vector2(p.X + 8, p.Y - 4);
            Vector2 prev = a0;
            for (int i = 1; i <= 8; i++)
            {
                float k = i / 8f;
                var pt = Vector2.Lerp(a0, a1, k);
                pt.Y -= MathF.Sin(k * MathF.PI) * 9f;
                Raylib.DrawLineEx(prev, pt, 1.6f, Raylib.Fade(Pal.Accent, figAlpha));
                prev = pt;
            }
            // the lobbed shell at the arc's apex
            Raylib.DrawCircleV(new Vector2(p.X, p.Y - 12), 2.4f, Raylib.Fade(Pal.Foe, figAlpha));
        }

        // shield: a thick barrier arc on the barred (facing) side
        if (u.Team == Team.Enemy && u.Cls == "SHIELD" && (u.ShieldDx != 0 || u.ShieldDy != 0))
        {
            float ang = MathF.Atan2(u.ShieldDy, u.ShieldDx) * 180f / MathF.PI;
            Raylib.DrawRing(p, 18f, 22f, ang - 55, ang + 55, 24, Raylib.Fade(Pal.RGBA(150, 200, 240), figAlpha));
        }

        // damage flash — always at full strength (it's momentary feedback)
        if (u.Flash > 0.01f)
            Raylib.DrawCircleV(p, 17f, Raylib.Fade(Pal.RGBA(255, 255, 255), u.Flash * 0.8f));

        // hp pips
        DrawHpPips(u, p);

        // status icons
        float ix = p.X - 10, iy = p.Y - 27;
        if (u.OnOverwatch)
        {
            Raylib.DrawCircle((int)p.X, (int)(p.Y - 26), 6f, Raylib.Fade(Pal.Accent, 0.25f));
            Raylib.DrawTextEx(Cfg.Font, "OW", new Vector2((int)(p.X - 9), (int)(p.Y - 31)), 10, 1f, Pal.Accent);
        }
        if (u.Hunkered)
            Raylib.DrawPoly(new Vector2(p.X, p.Y - 27), 4, 6f, 45f, Pal.Good);

        // active ability stance tag (friendly) / suppression tag (enemy)
        if (u.RunGun) Raylib.DrawTextEx(Cfg.Font, "R&G", new Vector2((int)(p.X + 13), (int)(p.Y - 30)), 11, 1f, Pal.Accent);
        else if (u.Blitz) Raylib.DrawTextEx(Cfg.Font, "BLZ", new Vector2((int)(p.X + 13), (int)(p.Y - 30)), 11, 1f, Pal.Accent);
        else if (u.Steady) Raylib.DrawTextEx(Cfg.Font, "AIM", new Vector2((int)(p.X + 13), (int)(p.Y - 30)), 11, 1f, Pal.Good);
        if (u.Team == Team.Enemy && u.Suppress > 0)
            Raylib.DrawTextEx(Cfg.Font, "SUPP", new Vector2((int)(p.X + 12), (int)(p.Y - 30)), 11, 1f, Pal.Foe);

        // combat status effects (3.5): stacked codes below the figure
        if (u.Statuses.Count > 0)
        {
            int sx = (int)p.X - 14, sy = (int)p.Y + 18;
            foreach (var s in u.Statuses)
            {
                if (s.Turns <= 0) continue;
                Color sc = s.Kind switch
                {
                    StatusKind.Burning => Pal.RGBA(255, 140, 40),
                    StatusKind.Bleed => Pal.RGBA(210, 50, 50),
                    StatusKind.Stun => Pal.RGBA(225, 205, 95),
                    _ => Pal.RGBA(150, 120, 220),       // Disoriented
                };
                // 5.5: a small shape glyph so the effect reads without relying on hue or the code text
                DrawStatusGlyph(s.Kind, sx + 4f, sy + 5f, sc);
                Raylib.DrawTextEx(Cfg.Font, StatusDef.Code(s.Kind), new Vector2(sx + 10, sy), 10, 1f, sc);
                sx += 32;
            }
        }

        // elite boss name / rage tag (uses the unit's actual name so mid-bosses read right)
        if (elite)
        {
            string tag = u.Enraged ? u.Name + " ENRAGED" : u.Name;
            Raylib.DrawTextEx(Cfg.Font, tag, new Vector2((int)(p.X - (int)Raylib.MeasureTextEx(Cfg.Font, tag, 11, 1f).X / 2), (int)(p.Y - 42)), 11, 1f, Pal.Elite);
        }

        // VIP / captive marker: diamond + tag above the asset
        if (vip)
        {
            bool caged = g.CaptiveLocked && u == g.Vip;
            Color vc = caged ? Pal.RGBA(180, 184, 194) : Pal.VipGold;
            Raylib.DrawPoly(new Vector2(p.X, p.Y - 39), 4, 5.5f, 45f, vc);
            Raylib.DrawPolyLinesEx(new Vector2(p.X, p.Y - 39), 4, 5.5f, 45f, 1.5f, Pal.Txt);
            string vtag = caged ? "CAPTIVE" : (u.Name == "CAPTIVE" ? "FREED" : "VIP");
            Raylib.DrawTextEx(Cfg.Font, vtag, new Vector2((int)(p.X - (int)Raylib.MeasureTextEx(Cfg.Font, vtag, 12, 1f).X / 2), (int)(p.Y - 53)), 12, 1f, vc);
            if (caged)   // cage bars over the figure
                for (int i = -1; i <= 1; i++)
                    Raylib.DrawLineEx(new Vector2(p.X + i * 6, p.Y - 12), new Vector2(p.X + i * 6, p.Y + 12),
                                      1.5f, Raylib.Fade(Pal.Txt, 0.6f));
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

    // Smoke clouds: a drifting translucent haze drawn over the board (and units).
    static void DrawSmoke(Game g)
    {
        float t = (float)Raylib.GetTime();
        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
            {
                if (g.Grid.Smoke[x, y] <= 0) continue;
                var c = Util.TileCenter(x, y);
                // a couple of offset puffs per tile, gently drifting, fading as it expires
                float life = Util.Clamp(g.Grid.Smoke[x, y] / (float)SmokeAnim.Turns, 0.35f, 1f);
                float a = 0.55f * life;
                float drift = MathF.Sin(t * 0.8f + (x * 3 + y)) * 3f;
                Raylib.DrawCircleV(c + new Vector2(drift, -2), Cfg.Tile * 0.62f, Raylib.Fade(Pal.RGBA(176, 184, 194), a));
                Raylib.DrawCircleV(c + new Vector2(-drift, 4), Cfg.Tile * 0.5f, Raylib.Fade(Pal.RGBA(150, 158, 168), a * 0.9f));
            }
    }

    // Utility-item targeting preview: range ring + a per-kind footprint.
    static void DrawItem(Game g)
    {
        if (!g.ItemMode || g.Selected == null) return;
        var origin = g.Selected.Pos;
        var kind = g.Selected.Item;
        Raylib.DrawCircleLines((int)origin.X, (int)origin.Y, Game.ItemRange * Cfg.Tile, Raylib.Fade(Pal.Friend, 0.35f));
        if (!g.HoverValid) return;
        Color col = g.ItemValid ? Pal.Friend : Pal.TxtDim;

        if (kind == ItemKind.Barricade)
        {
            Raylib.DrawRectangleRec(Util.TileRect(g.ItemTx, g.ItemTy), Raylib.Fade(col, 0.3f));
            Raylib.DrawRectangleLinesEx(Util.TileRect(g.ItemTx, g.ItemTy), 2f, col);
        }
        else  // smoke / flash: 3x3 blast footprint
        {
            int rad = SmokeAnim.Radius;
            for (int x = g.ItemTx - rad; x <= g.ItemTx + rad; x++)
                for (int y = g.ItemTy - rad; y <= g.ItemTy + rad; y++)
                    if (g.Grid.InBounds(x, y))
                        Raylib.DrawRectangleRec(Util.TileRect(x, y), Raylib.Fade(col, 0.2f));
            var tc = Util.TileCenter(g.ItemTx, g.ItemTy);
            Raylib.DrawCircleLines((int)tc.X, (int)tc.Y, 14, col);
        }
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
