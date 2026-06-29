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

        // floor (biome-tinted checker) — CALM the checker so units/cover pop, but LAND the
        // biome so the room recolours distinctly. Two levers: (1) collapse the value gap
        // between the two checker colours toward their mean (flatter, quieter floor), then
        // darken the mean a touch so it's a low base; (2) push that mean toward the biome
        // Tint hue so STEEL/ARID/TUNDRA/etc. read as a coloured place, not grey.
        Color floorMean = Pal.Mix(bm.FloorA, bm.FloorB, 0.5f);
        floorMean = Pal.Mix(floorMean, Pal.RGBA(6, 9, 13), 0.18f);       // slightly darker base
        floorMean = Pal.Mix(floorMean, bm.Tint, 0.22f);                  // land the biome hue
        Color fa = Pal.Mix(floorMean, bm.FloorA, 0.32f);                 // keep only a faint checker
        Color fb = Pal.Mix(floorMean, bm.FloorB, 0.32f);
        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
            {
                if (g.Grid.Tiles[x, y] != TileType.Floor) continue;
                var r = Util.TileRect(x, y);
                Raylib.DrawRectangleRec(r, ((x + y) & 1) == 0 ? fa : fb);
            }

        // 5.4: noise grain over the floor so it reads as material, not flat colour. Bumped to
        // 0.13 + biome-tinted to push the biome identity into the floor texture (still subtle).
        if (_noiseReady)
            for (int x = 0; x < g.Grid.W; x++)
                for (int y = 0; y < g.Grid.H; y++)
                {
                    if (g.Grid.Tiles[x, y] != TileType.Floor) continue;
                    DrawNoiseRect(Util.TileRect(x, y), bm.Tint, 0.13f);
                }

        g.Fx.DrawAmbient();   // per-biome ambient atmosphere, under terrain/units (Wave B)

        DrawElevation(g);
        DrawMoveOverlay(g);
        DrawOverwatchThreat(g);   // tiles each active overwatching enemy covers (reaction-fire danger)
        DrawThreat(g);
        DrawSiegeZones(g);        // persistent pulsing 3x3 danger zone of any charging SIEGE artillery
        DrawEvac(g);
        DrawTerminal(g);
        DrawSabotage(g);
        DrawGridLines(g);
        DrawPathPreview(g);
        DrawCover(g);
        DrawBarrels(g);           // explosive drums — objects at cover/terrain level (under the figures)
        DrawHoverAndShields(g);
        DrawKbCursor(g);
        DrawEnemyIntent(g);       // telegraph: the acting hostile's planned move + target + threat
        DrawScorch(g);            // lingering burn decals where units fell (under the figures)
        DrawFire(g);              // burning floor — deny-ground hazard, on the floor under the figures
        DrawUnits(g);
        DrawSmoke(g);
        DrawAim(g);
        DrawBarrelAimReticle(g);  // targeting reticle + blast preview when aiming a shootable barrel
        DrawCrossfire(g);         // pincer telegraph: converging-fire prongs when the aimed/hovered shot is a crossfire
        DrawGrenade(g);
        DrawItem(g);
        DrawShove(g);
        DrawMarkIndicators(g);
        DrawMark(g);
        DrawGrapple(g);
        DrawPinIndicators(g);     // always-on: pinned (suppressed) foes get a bracket cage marker
        DrawPin(g);               // gunner SUPPRESSING FIRE targeting preview (zone footprint)

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
        // the WIN-CONDITION must be the 2nd-most-salient thing on the board: a strong animated
        // pulsing fill + a chevron sweep, not a thin outline. Uses the friendly Good role colour.
        float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 3f);
        int minx = int.MaxValue, miny = int.MaxValue;
        foreach (var (x, y) in g.EvacZone)
        {
            var r = Util.TileRect(x, y);
            // glowing fill — much stronger than before so the zone reads as "go here to win"
            Raylib.DrawRectangleRec(r, Raylib.Fade(Pal.Good, 0.16f + 0.16f * pulse));
            // animated upward chevrons inside the tile (the "extract / lift-out" cue)
            float ph = ((float)Raylib.GetTime() * 0.8f) % 1f;
            for (int c = 0; c < 2; c++)
            {
                float cy = r.Y + r.Height * (0.85f - ((ph + c * 0.5f) % 1f) * 0.7f);
                float cx = r.X + r.Width * 0.5f;
                Color cc = Raylib.Fade(Pal.Good, 0.45f);
                Raylib.DrawLineEx(new Vector2(cx - 9, cy + 5), new Vector2(cx, cy - 4), 2f, cc);
                Raylib.DrawLineEx(new Vector2(cx, cy - 4), new Vector2(cx + 9, cy + 5), 2f, cc);
            }
            Raylib.DrawRectangleLinesEx(new Rectangle(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4),
                                        2.5f, Raylib.Fade(Pal.Good, 0.6f + 0.4f * pulse));
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

        // pad — strong pulsing glow + a radial bloom so the hack objective reads at 2nd-salience
        Raylib.DrawRectangleRec(r, Raylib.Fade(col, 0.13f + 0.12f * pulse));
        Raylib.DrawCircleV(c, 24f, Raylib.Fade(col, 0.06f + 0.07f * pulse));
        Raylib.DrawRectangleLinesEx(new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6),
                                    2.5f, Raylib.Fade(col, 0.5f + 0.4f * pulse));

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
            // OBJECTIVE accent (amber) — NOT red — so a charge site can never be misread as an
            // enemy. Armed (done) flips to the friendly Good colour.
            Color col = blown ? Pal.Good : Pal.Accent;
            float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 3f + i);

            // a strong pulsing glow fill so the win-condition site reads at 2nd-salience
            Raylib.DrawRectangleRec(r, Raylib.Fade(col, 0.12f + (blown ? 0f : 0.14f * pulse)));
            // a soft radial bloom centred on the charge so it glows off the muted floor
            if (!blown) Raylib.DrawCircleV(c, 22f, Raylib.Fade(col, 0.06f + 0.07f * pulse));
            Raylib.DrawRectangleLinesEx(new Rectangle(r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6),
                                        2.5f, Raylib.Fade(col, blown ? 0.4f : 0.5f + 0.4f * pulse));
            // charge box + light
            Raylib.DrawRectangleRec(new Rectangle(c.X - 8, c.Y - 9, 16, 18), Pal.RGBA(14, 20, 28));
            Raylib.DrawRectangleLinesEx(new Rectangle(c.X - 8, c.Y - 9, 16, 18), 1.5f, col);
            Raylib.DrawCircleV(new Vector2(c.X, c.Y), 3.8f, Raylib.Fade(col, blown ? 0.9f : 0.55f + 0.45f * pulse));

            Raylib.DrawTextEx(Cfg.Font, blown ? "ARMED" : "CHARGE", new Vector2((int)c.X - 18, (int)r.Y - 13), 10, 1f, col);
        }
    }

    static void DrawGridLines(Game g)
    {
        // back the grid off so it's a quiet substrate, not a competing mesh — fade the biome
        // grid colour rather than leaving it at full strength (it was adding muddy mid-value
        // clutter that fought the units). Thin + low-alpha = present but recessive.
        Color gl = Raylib.Fade(g.Biome.Grid, 0.5f);
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
                // a single small subtle danger tick (was a loud filled triangle + bright outline)
                // — informs "this tile is exposed" without a field of red pips drowning the units.
                Raylib.DrawPoly(pos, 3, 4.5f, -90f, Raylib.Fade(Pal.Foe, 0.32f + 0.18f * pulse));
                Raylib.DrawPolyLinesEx(pos, 3, 4.5f, -90f, 1.2f, Raylib.Fade(Pal.Foe, 0.45f));
            }
    }

    // SIEGE artillery danger zone: a persistent, bold pulsing-red 3x3 over each charging BOMBARD's
    // aimed center, shown for the WHOLE player turn (and as the shell lands) so the squad has a full
    // turn to react. Reads the LIVE charge state straight off the enemies (NOT a cached list), so a
    // killed SIEGE's zone clears on the next frame (the kill-interrupt). Bolder than DrawThreat's
    // corner pips — this is an "it WILL hit here" warning. Uses Pal.Foe (colorblind-safe danger hue)
    // + a rotating warning triangle (shape redundancy) + a dashed source->zone line.
    static void DrawSiegeZones(Game g)
    {
        float t = (float)Raylib.GetTime();
        float pulse = 0.55f + 0.45f * MathF.Sin(t * 5f);
        foreach (var e in g.Enemies)
        {
            if (!e.Alive || e.Cls != "BOMBARD" || e.ChargeTurns <= 0) continue;   // live charge only
            for (int dx = -Game.SiegeRadius; dx <= Game.SiegeRadius; dx++)
                for (int dy = -Game.SiegeRadius; dy <= Game.SiegeRadius; dy++)
                {
                    int x = e.ChargeX + dx, y = e.ChargeY + dy;
                    if (!g.Grid.InBounds(x, y)) continue;
                    var r = ElevRect(g, x, y);
                    Raylib.DrawRectangleRec(r, Raylib.Fade(Pal.Foe, 0.16f + 0.12f * pulse));        // pulsing red fill
                    Raylib.DrawRectangleLinesEx(r, 1.5f, Raylib.Fade(Pal.Foe, 0.55f + 0.30f * pulse));
                }
            // a rotating warning triangle at the center (non-color shape cue) + a dashed line gun->zone
            var cc = ElevCenter(g, e.ChargeX, e.ChargeY);
            Raylib.DrawPoly(cc, 3, 9f, -90f + t * 40f, Raylib.Fade(Pal.Foe, 0.7f));
            DashedLine(e.Pos, cc, 2.4f, Raylib.Fade(Pal.Foe, 0.8f), (t * 30f) % 14f, 8f, 6f);
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
        // soft red wash — well below signal level so it informs without dominating the board, but
        // nudged up to a reliably-perceptible floor so "this tile is in a reaction kill-zone" reads.
        Color wash = Raylib.Fade(Pal.Foe, 0.07f + 0.05f * pulse);

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

        // mark each overwatcher with a danger reticle so the SOURCE of the kill-zone reads, plus a
        // slow expanding "watching" pulse ring that draws the eye to the threat without occluding it.
        float t = (float)Raylib.GetTime();
        foreach (var w in watchers)
        {
            float hlift = g.Grid.IsHigh(w.X, w.Y) ? ElevLift : 0f;
            var c = w.Pos - new Vector2(0, hlift + 30f);   // float the reticle just above the figure
            Color rc = Raylib.Fade(Pal.Foe, 0.5f + 0.4f * pulse);
            // expanding sweep ring (the "actively watching" cue)
            float sweep = (t * 0.9f + w.Bob) % 1f;
            Raylib.DrawRing(c, 9f + sweep * 7f, 10.4f + sweep * 7f, 0, 360, 28, Raylib.Fade(Pal.Foe, (1f - sweep) * 0.35f));
            Raylib.DrawRing(c, 7.5f, 9.2f, 0, 360, 28, rc);
            // crosshair ticks
            Raylib.DrawLineEx(new Vector2(c.X - 11f, c.Y), new Vector2(c.X - 5f, c.Y), 1.8f, rc);
            Raylib.DrawLineEx(new Vector2(c.X + 5f, c.Y), new Vector2(c.X + 11f, c.Y), 1.8f, rc);
            Raylib.DrawLineEx(new Vector2(c.X, c.Y - 11f), new Vector2(c.X, c.Y - 5f), 1.8f, rc);
            Raylib.DrawLineEx(new Vector2(c.X, c.Y + 5f), new Vector2(c.X, c.Y + 11f), 1.8f, rc);
            Raylib.DrawCircleV(c, 1.8f, rc);
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
            // destination marker: a bold pulsing double-ring footprint where the unit ends up, so
            // the "it moves to HERE" read is unmistakable amid the cover.
            Vector2 dest = ElevCenter(g, dx, dy);
            Raylib.DrawRing(dest, 13f, 16f, 0, 360, 32, Raylib.Fade(danger, 0.18f + 0.18f * pulse));  // soft outer halo
            Raylib.DrawRing(dest, 9f, 12f, 0, 360, 32, Raylib.Fade(danger, 0.65f + 0.30f * pulse));   // crisp inner ring
            Raylib.DrawCircleV(dest, 3f, Raylib.Fade(danger, 0.9f));
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
            // aim-line from the firer's post-move position to the mark — a bold animated dashed red
            // beam (the dashes flow toward the target so "fire travels THIS way at THAT unit" reads
            // instantly), backed by a faint solid underlay so it never breaks up against dark tiles.
            Vector2 from = ElevCenter(g, dx, dy);
            Raylib.DrawLineEx(from, tc, 1.4f, Raylib.Fade(danger, 0.22f));                 // faint continuous underlay
            DashedLine(from, tc, 2.6f, Raylib.Fade(danger, 0.9f), (t * 34f) % 14f, 8f, 6f); // flowing dashed beam
            // bold pulsing reticle ring + a crisp X on the mark
            float rr = reach + 2.5f * pulse;
            Raylib.DrawRing(tc, rr, rr + 2.5f, 0, 360, 36, Raylib.Fade(danger, 0.22f + 0.18f * pulse)); // outer glow
            Raylib.DrawRing(tc, rr - 2.5f, rr, 0, 360, 36, Raylib.Fade(danger, 0.95f));                  // crisp ring
            float k = rr * 0.7f;
            Raylib.DrawLineEx(new Vector2(tc.X - k, tc.Y - k), new Vector2(tc.X + k, tc.Y + k), 2.6f, Raylib.Fade(danger, 0.95f));
            Raylib.DrawLineEx(new Vector2(tc.X - k, tc.Y + k), new Vector2(tc.X + k, tc.Y - k), 2.6f, Raylib.Fade(danger, 0.95f));
            Raylib.DrawCircleV(tc, 2.4f, Raylib.Fade(Pal.RGBA(255, 220, 220), 0.95f));
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
        // a definitive bordered pill (opaque dark fill + thin danger outline) so the verb reads as a
        // hard label, not a wash — the player can name the threat at a glance.
        var pill = new Rectangle(cap.X - sz.X / 2f - 5, cap.Y - 2, sz.X + 10, sz.Y + 4);
        Raylib.DrawRectangleRounded(pill, 0.5f, 6, Raylib.Fade(Pal.RGBA(24, 6, 6), 0.92f));
        Raylib.DrawRectangleLinesEx(pill, 1f, Raylib.Fade(danger, 0.7f + 0.25f * pulse));   // square outline (RoundedLines is version-volatile)
        Raylib.DrawTextEx(Cfg.Font, verb, new Vector2(cap.X - sz.X / 2f, cap.Y), 14f, 1f,
                          Raylib.Fade(Pal.RGBA(255, 215, 215), 1f));
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
        // blend the neutral cover palette toward the biome hue, then DARKEN the whole
        // block so terrain RECEDES behind the units/objectives (visual-hierarchy invert).
        // Cover must read as solid, grounded and QUIET — never the loudest thing on screen.
        Color tint = g.Biome.Tint;
        // pull each cover colour ~24% toward near-black so the blocks sit back as a low,
        // muted base layer; the faux-3D shape + shadow still carry the silhouette.
        Color shade = Pal.RGBA(8, 11, 15);
        Color cHi = Pal.Mix(Pal.Mix(Pal.CoverHi, tint, 0.28f),    shade, 0.24f);
        Color cHiTop = Pal.Mix(Pal.Mix(Pal.CoverHiTop, tint, 0.28f), shade, 0.24f);
        Color cLo = Pal.Mix(Pal.Mix(Pal.CoverLo, tint, 0.28f),    shade, 0.24f);
        Color cLoTop = Pal.Mix(Pal.Mix(Pal.CoverLoTop, tint, 0.28f), shade, 0.24f);
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
                // front-face shade gradient: a soft darkening toward the bottom of the wall so the
                // block reads as a lit 3D volume (consistent top-light), and a thin lighter catch on
                // the upper-left of the face. Cheap (a handful of thin bands), subtle (squint holds).
                {
                    int bands = 4;
                    for (int b = 0; b < bands; b++)
                    {
                        float fy = baseRect.Y + baseRect.Height * (0.45f + 0.55f * b / bands);
                        float fh = baseRect.Height * 0.55f / bands + 1f;
                        Raylib.DrawRectangleRec(new Rectangle(baseRect.X + 1, fy, baseRect.Width - 2, fh),
                                                Raylib.Fade(Pal.RGBA(0, 0, 0), 0.05f + 0.05f * b));
                    }
                    // upper-left vertical light catch on the face
                    Raylib.DrawLineEx(new Vector2(baseRect.X + 2.5f, baseRect.Y + 2f),
                                      new Vector2(baseRect.X + 2.5f, baseRect.Y + baseRect.Height * 0.6f),
                                      1.5f, Raylib.Fade(Pal.RGBA(255, 255, 255), high ? 0.10f : 0.07f));
                }
                // contact shadow at the base of the cover block — grounds it against the floor
                Raylib.DrawRectangleRec(
                    new Rectangle(baseRect.X + 4, baseRect.Y + baseRect.Height - 1, baseRect.Width - 8, 4),
                    Raylib.Fade(Pal.RGBA(0, 0, 0), 0.22f));
                Raylib.DrawRectangleRounded(topRect, 0.22f, 5, high ? cHiTop : cLoTop);
                // 5.4: noise grain on the top face so cover reads as a physical object
                DrawNoiseRect(topRect, tint, 0.10f);
                // top edge highlight — dropped to a whisper so cover stays quiet (was 0.12).
                Raylib.DrawLineEx(new Vector2(topRect.X + 4, topRect.Y + 2),
                                  new Vector2(topRect.X + topRect.Width - 4, topRect.Y + 2),
                                  1.5f, Raylib.Fade(Pal.RGBA(255, 255, 255), 0.05f));
                // emissive rim — kept only as a faint structural catch on the upper edge so the
                // block still reads as a lit volume, but well below signal so it can't compete
                // with units/objectives or trip the bloom into making cover glow (was 0.30/0.20).
                Color rimCol = Pal.Mix(Pal.HighEdge, Pal.RGBA(255, 255, 255), 0.45f);
                Raylib.DrawLineEx(new Vector2(topRect.X + 5, topRect.Y + 3),
                                  new Vector2(topRect.X + topRect.Width - 5, topRect.Y + 3),
                                  1f, Raylib.Fade(rimCol, high ? 0.11f : 0.08f));
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

    // Death scorch decals: a dark burn blob + a faint team-tinted scorch ring at each spot a unit
    // fell, fading out over its life. Drawn on the ground (under the figures) so a kill reads as a
    // lingering mark on the battlefield rather than an instant disappearance. Cheap (a couple of
    // ellipses/rings per decal) and bounded (Game caps the list).
    static void DrawScorch(Game g)
    {
        foreach (var s in g.Scorches)
        {
            float k = Util.Clamp(s.Life / s.MaxLife, 0f, 1f);    // 1 at birth -> 0 at death
            float spread = Util.Lerp(0.6f, 1f, 1f - k);          // the burn settles/spreads slightly as it ages
            // dark charred core (flattened to read as on-the-ground)
            float rx = 16f * spread, ry = 7f * spread;
            Raylib.DrawEllipse((int)s.Pos.X, (int)(s.Pos.Y + 16f), rx, ry, Raylib.Fade(Pal.RGBA(12, 12, 14), 0.55f * k));
            Raylib.DrawEllipse((int)s.Pos.X, (int)(s.Pos.Y + 16f), rx * 0.6f, ry * 0.6f, Raylib.Fade(Pal.RGBA(4, 4, 6), 0.6f * k));
            // faint team-tinted ember ring around the edge of the scorch (the side of the team that fell)
            Raylib.DrawRing(s.Pos + new Vector2(0, 16f), rx * 0.9f, rx, 0, 360, 28, Raylib.Fade(s.Tint, 0.30f * k));
        }
    }

    static void DrawUnits(Game g)
    {
        foreach (var u in g.Enemies) DrawUnit(g, u);
        foreach (var u in g.Players) DrawUnit(g, u);
    }

    // ---- Environmental hazards (Wave 2) -----------------------------------------------------
    // Explosive barrels + burning floor tiles. Both are primitive-drawn in the game's
    // geometric aesthetic and tinted to a warning hue so the squint test flags them as
    // "dangerous / interactable" — they must never read as ordinary cover or floor.

    // Explosive drums sitting on the board. A chunky rounded body with a hazard-stripe
    // shoulder band + a soft pulsing danger glow so the volatility reads instantly. Drawn
    // at cover/terrain z-order (objects on the board, under the units).
    static void DrawBarrels(Game g)
    {
        float t = (float)Raylib.GetTime();
        // warning palette — orange/red, distinct from cover's cool blues
        Color drum    = Pal.RGBA(150, 64, 24);     // body
        Color drumTop = Pal.RGBA(196, 92, 34);     // lit top
        Color band    = Pal.RGBA(248, 196, 60);    // hazard stripe
        Color glow    = Pal.RGBA(255, 120, 40);

        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
            {
                if (!g.Grid.IsBarrel(x, y)) continue;
                var r = ElevRect(g, x, y);
                var c = ElevCenter(g, x, y);

                // pulsing danger glow halo under the drum — volatility cue
                float pulse = 0.5f + 0.5f * MathF.Sin(t * 3.2f + (x * 5 + y));
                float gr = Cfg.Tile * (0.40f + 0.05f * pulse);
                Raylib.DrawCircleV(c + new Vector2(0, 4f), gr, Raylib.Fade(glow, 0.10f + 0.10f * pulse));

                // ground contact shadow (flattened ellipse), grounds the drum
                Raylib.DrawEllipse((int)c.X, (int)(r.Y + r.Height - 8f), 16f, 6f, Raylib.Fade(Pal.RGBA(0, 0, 0), 0.40f));

                // drum body: a tall rounded rectangle
                float bw = r.Width * 0.46f, bh = r.Height * 0.62f;
                var body = new Rectangle(c.X - bw * 0.5f, r.Y + r.Height * 0.5f - bh * 0.55f, bw, bh);
                Raylib.DrawRectangleRounded(body, 0.42f, 8, drum);
                // lit top cap (a thin lighter rounded band)
                var cap = new Rectangle(body.X, body.Y, bw, bh * 0.22f);
                Raylib.DrawRectangleRounded(cap, 0.9f, 8, drumTop);
                // left light catch / right shade for volume
                Raylib.DrawLineEx(new Vector2(body.X + 2.5f, body.Y + bh * 0.20f),
                                  new Vector2(body.X + 2.5f, body.Y + bh * 0.85f),
                                  1.6f, Raylib.Fade(Pal.RGBA(255, 255, 255), 0.12f));
                Raylib.DrawLineEx(new Vector2(body.X + bw - 2.5f, body.Y + bh * 0.20f),
                                  new Vector2(body.X + bw - 2.5f, body.Y + bh * 0.90f),
                                  1.8f, Raylib.Fade(Pal.RGBA(0, 0, 0), 0.22f));

                // two hazard rings around the belly (the warning band)
                float b1 = body.Y + bh * 0.40f, b2 = body.Y + bh * 0.66f;
                Raylib.DrawRectangleRec(new Rectangle(body.X, b1, bw, bh * 0.10f), band);
                Raylib.DrawRectangleRec(new Rectangle(body.X, b2, bw, bh * 0.08f), band);
                // a hazard "!" mark on the belly between the bands — flags it as interactable
                float mcx = c.X, mcy = (b1 + b2) * 0.5f + bh * 0.04f;
                Raylib.DrawLineEx(new Vector2(mcx, mcy - 4f), new Vector2(mcx, mcy + 1.5f), 2f, band);
                Raylib.DrawCircleV(new Vector2(mcx, mcy + 4.5f), 1.3f, band);

                // outline so the drum pops off busy terrain (square lines — avoid version-volatile
                // DrawRectangleRoundedLines per the Raylib gotchas)
                Raylib.DrawRectangleLinesEx(body, 1.4f, Raylib.Fade(Pal.RGBA(20, 8, 4), 0.6f));
            }
    }

    // Burning floor tiles — animated flickering flames with a warm core and darker smoke
    // edges. Alpha + height scale with the remaining fire turns (guttering as it expires).
    // Drawn on the floor, UNDER the units (deny-ground hazard).
    static void DrawFire(Game g)
    {
        float t = (float)Raylib.GetTime();
        Color core  = Pal.RGBA(255, 224, 120);   // hot inner
        Color flame = Pal.RGBA(244, 132, 40);    // main tongue
        Color deep  = Pal.RGBA(176, 52, 20);     // outer/cooler
        Color smoke = Pal.RGBA(40, 32, 30);

        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
            {
                int turns = g.Grid.Fire[x, y];
                if (turns <= 0) continue;
                var c = Util.TileCenter(x, y);
                // life 0..1 from remaining turns: near 0 = guttering/dimmer/shorter
                float life = Util.Clamp(turns / (float)Grid.FireTurns, 0.0f, 1f);
                float a = 0.45f + 0.45f * life;

                // a warm glow wash on the tile so the deny-ground reads even at a glance
                Raylib.DrawCircleV(c + new Vector2(0, 6f), Cfg.Tile * 0.5f, Raylib.Fade(deep, 0.10f * a));

                // deterministic-ish per-tile phase so adjacent tiles flicker out of sync
                float phase = (x * 7 + y * 13) * 0.7f;
                // base of the flames sits a touch below tile centre
                float baseY = c.Y + Cfg.Tile * 0.22f;
                float h0 = Cfg.Tile * (0.42f + 0.18f * life);   // tongue height scaled by life

                // 3 layered tongues: outer deep, mid flame, inner core — each a flickering triangle
                for (int layer = 0; layer < 3; layer++)
                {
                    float lf = 1f - layer * 0.30f;                          // inner layers shorter
                    float flick = 0.78f + 0.22f * MathF.Sin(t * (7f + layer * 2.3f) + phase + layer);
                    float sway  = MathF.Sin(t * 4f + phase + layer * 1.7f) * (3f - layer);
                    float hh = h0 * lf * flick;
                    float hw = Cfg.Tile * (0.20f - layer * 0.045f);
                    Color col = layer == 0 ? deep : (layer == 1 ? flame : core);
                    var tip = new Vector2(c.X + sway, baseY - hh);
                    var bL  = new Vector2(c.X - hw,   baseY);
                    var bR  = new Vector2(c.X + hw,   baseY);
                    // Raylib back-face-culls clockwise triangles in screen space (y-down), so the
                    // winding MUST be counter-clockwise: bottom-left -> bottom-right -> tip. (See the
                    // CLAUDE.md DrawTriangle gotcha.) The earlier bL->tip->bR order was culled.
                    Raylib.DrawTriangle(bL, bR, tip, Raylib.Fade(col, a));
                }

                // a rising smoke puff above the flame, fading out
                float sUp = (t * 16f + phase * 9f) % 26f;
                Raylib.DrawCircleV(new Vector2(c.X + MathF.Sin(t * 2f + phase) * 4f, baseY - h0 - sUp),
                                   3f + sUp * 0.10f, Raylib.Fade(smoke, 0.22f * a * (1f - sUp / 26f)));

                // tiny ember sparks flicking off the top
                for (int s = 0; s < 2; s++)
                {
                    float sp = (t * 1.4f + phase + s * 3.1f) % 1f;
                    var ep = new Vector2(c.X + MathF.Sin((phase + s) * 2.3f + t) * 6f, baseY - h0 * (0.6f + sp));
                    Raylib.DrawCircleV(ep, 1.3f, Raylib.Fade(core, (1f - sp) * 0.7f * a));
                }
            }
    }

    // When the selected soldier is aiming at a shootable barrel, draw a distinct red
    // targeting reticle on it + a faint Chebyshev-1 blast-radius preview so the player
    // sees they can detonate it and roughly what the blast will catch.
    static void DrawBarrelAimReticle(Game g)
    {
        if (!g.AimMode || !g.BarrelAimValid) return;
        var c = ElevCenter(g, g.BarrelAimX, g.BarrelAimY);
        float t = (float)Raylib.GetTime();
        Color col = Pal.Foe;

        // faint blast-radius wash over the Chebyshev-1 footprint
        for (int x = g.BarrelAimX - 1; x <= g.BarrelAimX + 1; x++)
            for (int y = g.BarrelAimY - 1; y <= g.BarrelAimY + 1; y++)
                if (g.Grid.InBounds(x, y))
                    Raylib.DrawRectangleRec(ElevRect(g, x, y), Raylib.Fade(col, 0.10f));
        // blast ring around the footprint
        Raylib.DrawCircleLines((int)c.X, (int)c.Y, Cfg.Tile * 1.35f, Raylib.Fade(col, 0.45f));

        // a spinning reticle on the barrel itself (4 ticking arcs — distinct from the round enemy reticle)
        float ang = t * 70f;
        float rr = 16f;
        for (int i = 0; i < 4; i++)
        {
            float a0 = ang + i * 90f;
            Raylib.DrawRing(c, rr, rr + 3f, a0, a0 + 50f, 8, col);
        }
        // crosshair ticks
        Raylib.DrawLineEx(c + new Vector2(-22, 0), c + new Vector2(-12, 0), 2f, col);
        Raylib.DrawLineEx(c + new Vector2( 12, 0), c + new Vector2( 22, 0), 2f, col);
        Raylib.DrawLineEx(c + new Vector2(0, -22), c + new Vector2(0, -12), 2f, col);
        Raylib.DrawLineEx(c + new Vector2(0,  12), c + new Vector2(0,  22), 2f, col);
        // a small inner diamond (rotated square) to read as "explosive target"
        float d = 6f;
        Raylib.DrawLineEx(c + new Vector2(0, -d), c + new Vector2(d, 0), 1.8f, col);
        Raylib.DrawLineEx(c + new Vector2(d, 0), c + new Vector2(0, d), 1.8f, col);
        Raylib.DrawLineEx(c + new Vector2(0, d), c + new Vector2(-d, 0), 1.8f, col);
        Raylib.DrawLineEx(c + new Vector2(-d, 0), c + new Vector2(0, -d), 1.8f, col);
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

    // ---- per-class silhouettes (the figure's primary identifying shape) ------------------------
    // Each class gets a distinctive primitive-drawn cue inside its body radius so a SNIPER, GUNNER,
    // TURRET, BERSERKER, DRONE, etc. read apart at a glance — NOT just an N-sided poly. Meaning rides
    // on shape (colorblind-safe); everything inherits the team colour `c` + the focal `a` (figAlpha),
    // and `s` is the flinch body-scale so the silhouette pops with the body. `ang` is the unit facing.
    //
    // Small oriented primitives are built from a forward vector (fdir) + its perpendicular (perp), so
    // a wedge/barrel points where the unit faces. All offsets are in px, scaled by `s`.
    static void DrawSilhouette(Unit u, Vector2 p, Color c, float a, float s, float ang)
    {
        Color col = Raylib.Fade(c, a);
        var fdir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        var perp = new Vector2(-fdir.Y, fdir.X);
        // local helpers (capture col/p/fdir/perp/s) -------------------------------------------------
        Vector2 At(float fwd, float side) => p + fdir * (fwd * s) + perp * (side * s);
        // a filled forward wedge (triangle): nose ahead, base behind — the "pointing somewhere" cue.
        // DrawTriangle backface-culls by winding, so order the verts by signed area (robust at any facing).
        void Wedge(float nose, float backFwd, float halfW, float alpha)
            => FillTri(At(nose, 0), At(backFwd, -halfW), At(backFwd, halfW), Raylib.Fade(c, a * alpha));
        void Bar(float fwd, float halfLen, float thick, float alpha)    // a line across the facing (perp bar)
            => Raylib.DrawLineEx(At(fwd, -halfLen), At(fwd, halfLen), thick * s, Raylib.Fade(c, a * alpha));
        void Barrel(float from, float to, float thick) // a line along the facing (a gun barrel)
            => Raylib.DrawLineEx(At(from, 0), At(to, 0), thick * s, col);

        switch (u.Cls)
        {
            // ---------------- players ----------------
            case "ASSAULT":            // aggressive forward wedge (rifleman pushing up)
                Wedge(9f, -5f, 7f, 1f);
                Barrel(2f, 12f, 2.2f);                       // a short rifle barrel out the nose
                break;
            case "RANGER":             // a slim forward dart (fast flanker) + a blade tick
                Wedge(9f, -3f, 4.5f, 1f);
                Raylib.DrawLineEx(At(-1f, 4.5f), At(6f, 1.5f), 2f * s, col);   // angled blade
                break;
            case "SHARPSHOOTER":       // a compact body + a LONG barrel line (marksman)
                Raylib.DrawCircleV(At(-2f, 0), 4f * s, col);
                Barrel(-2f, 16f, 2.2f);
                Raylib.DrawCircleV(At(16f, 0), 1.8f * s, col);   // muzzle bead
                break;
            case "GUNNER":             // a WIDE bipod stance: a heavy cross-bar + two splayed legs
                Bar(3f, 9f, 3.2f, 1f);                       // the weapon, held broad
                Raylib.DrawLineEx(At(3f, -7f), At(-4f, -10f), 2f * s, col);  // left leg
                Raylib.DrawLineEx(At(3f,  7f), At(-4f,  10f), 2f * s, col);  // right leg
                Barrel(3f, 12f, 2.4f);
                break;
            case "CORPSMAN":           // a rounded medic body (the white cross is drawn separately)
                Raylib.DrawCircleV(p, 5.5f * s, col);
                break;

            // ---------------- enemies ----------------
            case "GRUNT":              // basic forward triangle
                Wedge(8f, -5f, 6.5f, 1f);
                break;
            case "SCOUT":              // small fast dart
                Wedge(8f, -3f, 4.5f, 1f);
                break;
            case "BRUISER":            // a bulky wide hexagon (tanky LMG)
                Raylib.DrawPoly(p, 6, 8.5f * s, MathF.Atan2(fdir.Y, fdir.X) * 180f / MathF.PI, col);
                Barrel(2f, 12f, 3f);                         // a fat barrel
                break;
            case "SNIPER":             // compact body + long barrel (like the sharpshooter, foe-tinted)
                Raylib.DrawCircleV(At(-2f, 0), 4f * s, col);
                Barrel(-2f, 16f, 2.2f);
                Raylib.DrawCircleV(At(16f, 0), 1.8f * s, col);
                break;
            case "TURRET":             // an EMPLACED block base + a stubby swivel barrel (immobile nest)
                Raylib.DrawRectanglePro(new Rectangle(p.X, p.Y, 13f * s, 13f * s), new Vector2(6.5f * s, 6.5f * s), 0f, col);
                Barrel(2f, 13f, 3.4f);
                break;
            case "BERSERKER":          // a SPIKY crown — short spikes radiating (a frenzied rusher)
            {
                Raylib.DrawCircleV(p, 4.5f * s, col);
                for (int i = 0; i < 8; i++)
                {
                    float aa = i * (MathF.PI / 4f);
                    var d = new Vector2(MathF.Cos(aa), MathF.Sin(aa));
                    Raylib.DrawLineEx(p + d * (4.5f * s), p + d * (9.5f * s), 1.8f * s, col);
                }
                break;
            }
            case "ELITE":              // a bold 8-point star (the boss reads as a crown)
                Raylib.DrawPoly(p, 8, 9f * s, 0f, col);
                Raylib.DrawPolyLines(p, 4, 10f * s, 45f, col);
                break;
            case "MEDIC":              // a rounded body (the green cross is drawn separately)
                Raylib.DrawCircleV(p, 5.5f * s, col);
                break;
            case "DRONE":              // a hovering ROTOR: a small core + two rotor blades (an X)
                Raylib.DrawCircleV(p, 3.2f * s, col);
                Raylib.DrawLineEx(p + new Vector2(-8f, -8f) * s, p + new Vector2(8f, 8f) * s, 1.8f * s, col);
                Raylib.DrawLineEx(p + new Vector2(-8f,  8f) * s, p + new Vector2(8f, -8f) * s, 1.8f * s, col);
                Raylib.DrawCircleV(p + new Vector2(-8f, -8f) * s, 1.6f * s, col);
                Raylib.DrawCircleV(p + new Vector2( 8f, -8f) * s, 1.6f * s, col);
                Raylib.DrawCircleV(p + new Vector2(-8f,  8f) * s, 1.6f * s, col);
                Raylib.DrawCircleV(p + new Vector2( 8f,  8f) * s, 1.6f * s, col);
                break;
            case "SHIELD":             // a compact body biased BEHIND its arc (the arc is drawn separately)
                Raylib.DrawCircleV(At(-3f, 0), 5.5f * s, col);
                break;
            case "SAPPER":             // a sturdy square breacher body (the demo charge is drawn separately)
                Raylib.DrawRectanglePro(new Rectangle(p.X, p.Y, 11f * s, 11f * s), new Vector2(5.5f * s, 5.5f * s),
                                        MathF.Atan2(fdir.Y, fdir.X) * 180f / MathF.PI, col);
                break;
            case "HUNTER":             // a lean forward dart (the twin speed chevrons are drawn separately)
                Wedge(9f, -4f, 4f, 1f);
                break;
            case "MORTAR":             // a stout body + a back-tilted tube (the lob arc is drawn separately)
                Raylib.DrawCircleV(At(-2f, 0), 5f * s, col);
                Raylib.DrawLineEx(At(-3f, 0), At(-3f, 0) - fdir * (10f * s) + new Vector2(0, -9f * s), 3f * s, col);  // tube angled up-back
                break;
            case "SPOTTER":            // a SENSOR/DESIGNATOR: a small core + an antenna mast topped by a
                                       // forward-facing dish bracket (the scan rings are drawn separately).
                                       // Reads as "equipment, not a shooter" — distinct from the medic circle.
            {
                Raylib.DrawCircleV(At(-2f, 0), 4f * s, col);                       // compact core (sits back)
                var mastTop = At(-2f, 0) + new Vector2(0, -11f * s);              // antenna mast straight up
                Raylib.DrawLineEx(At(-2f, 0), mastTop, 1.8f * s, col);
                // a small parabolic dish bracket at the mast top, opening along the facing
                Raylib.DrawLineEx(mastTop, mastTop + fdir * (5f * s) + perp * (3.5f * s), 1.8f * s, col);
                Raylib.DrawLineEx(mastTop, mastTop + fdir * (5f * s) - perp * (3.5f * s), 1.8f * s, col);
                Raylib.DrawCircleV(mastTop, 1.6f * s, col);                       // dish hub
                break;
            }
            case "LANCER":             // a PHALANX trooper: a compact body behind a raised LANCE (a long
                                       // forward spear) + a short shoulder bar (the shield-wall shoulder).
                                       // Reads as "formation / polearm", distinct from the rifleman wedge.
                Raylib.DrawCircleV(At(-3f, 0), 4.5f * s, col);   // body sits back behind the lance
                Barrel(-3f, 15f, 2.4f);                          // the long lance reaching forward
                Raylib.DrawCircleV(At(15f, 0), 1.8f * s, col);   // the spear tip
                Bar(-3f, 6f, 2.4f, 0.9f);                        // a shoulder bar across the back (the wall edge)
                break;
            case "HOUND":              // a SWARM beast: a low lean body with TWIN forward fangs/prongs (a
                                       // predator's open maw), angrier + more angular than the HUNTER dart.
                Wedge(7f, -4f, 4f, 1f);                          // a small lean body wedge
                Raylib.DrawLineEx(At(7f, 0), At(12f, 3.5f), 2f * s, col);   // upper fang
                Raylib.DrawLineEx(At(7f, 0), At(12f, -3.5f), 2f * s, col);  // lower fang
                break;
            case "BOMBARD":            // a STOUT howitzer: a heavy squat body + a short fat tube angled
                                       // up-forward (artillery), distinct from the MORTAR's thin back-tube.
                                       // The pulsing charging core is drawn separately while ChargeTurns>0.
            {
                Raylib.DrawRectanglePro(new Rectangle(p.X, p.Y, 13f * s, 9f * s), new Vector2(6.5f * s, 4.5f * s),
                                        MathF.Atan2(fdir.Y, fdir.X) * 180f / MathF.PI, col);   // squat carriage
                // a short fat muzzle, tilted up (artillery elevation): along facing but lifted
                var muzBase = At(2f, 0);
                var muzTip = At(8f, 0) + new Vector2(0, -7f * s);
                Raylib.DrawLineEx(muzBase, muzTip, 4f * s, col);
                Raylib.DrawCircleV(muzTip, 2.2f * s, col);                                      // muzzle mouth
                break;
            }
            default:                   // fallback: a neutral pentagon
                Raylib.DrawPoly(p, 5, 7.5f * s, 0f, col);
                break;
        }
    }

    // A filled triangle that is robust to vertex winding: Raylib's DrawTriangle backface-culls by
    // winding order, so we compute the signed area and swap two verts if needed. Lets the oriented
    // silhouette wedges fill correctly no matter which way a unit is facing.
    static void FillTri(Vector2 a, Vector2 b, Vector2 cc, Color col)
    {
        float area = (b.X - a.X) * (cc.Y - a.Y) - (cc.X - a.X) * (b.Y - a.Y);
        if (area < 0f) Raylib.DrawTriangle(a, b, cc, col);
        else           Raylib.DrawTriangle(a, cc, b, col);
    }

    static void DrawUnit(Game g, Unit u)
    {
        if (!u.Alive) return;
        bool friend = u.Team == Team.Player;
        bool vip = friend && u.IsVip;
        bool elite = u.Team == Team.Enemy && u.Cls == "ELITE";
        bool hvt = u.Team == Team.Enemy && g.HasHvt && u == g.Hvt;   // DECAPITATE target
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
        else if (friend && !u.CanAct)     figAlpha = 0.66f;          // spent player: "done" but still clearly readable
        else if (friend)                  figAlpha = 0.92f;          // other player: nearly full — units must read first
        else                              figAlpha = 1.0f;           // enemies: full — threats are top priority signal
        // when a soldier IS selected, push the gap a touch further so the active unit stands out
        // against the rest of the squad — but never touch enemy alpha (threats stay fully legible).
        if (g.Selected != null && g.Selected != u && friend && u.CanAct) figAlpha -= 0.05f;

        // lift the figure when it stands on raised terrain
        float hlift = g.Grid.IsHigh(u.X, u.Y) ? ElevLift : 0f;
        var foot = u.Pos - new Vector2(0, hlift);

        // a DRONE hovers above its shadow (reads as airborne)
        bool drone = u.Team == Team.Enemy && u.Cls == "DRONE";
        float hover = drone ? 11f + MathF.Sin((float)Raylib.GetTime() * 3f + u.Bob) * 2f : 0f;

        float bob = MathF.Sin((float)Raylib.GetTime() * 2.2f + u.Bob) * 1.6f;
        Vector2 p = foot + new Vector2(0, bob - hover) + u.Recoil;

        // ---- procedural unit animation pose (render-only; Unit fields decay in Game.Update) ----
        // Translate the figure for a FIRE-RECOIL kick (rocks back along the barrel), a HIT-FLINCH
        // (a quick shove back + a brief scale pop), and a WALK-LEAN (leans into the step). Facing
        // already points along travel during a move, so the lean reads as striding forward. These
        // fold into the body centre + a scale, so the silhouette + body deform together.
        var fwd = new Vector2(MathF.Cos(u.Facing), MathF.Sin(u.Facing));
        var fperp = new Vector2(-fwd.Y, fwd.X);
        Vector2 pose = -fwd * (u.RecoilAnim * 4.2f)                                  // recoil: back along the barrel
                     - fwd * (u.FlinchAnim * 2.4f)                                   // flinch: shoved back from the hit
                     + fperp * (MathF.Sin(u.FlinchAnim * 22f) * u.FlinchAnim * 1.3f) // flinch: a small lateral shudder
                     + fwd * (u.WalkLean * 3.2f)                                     // walk: lean into the step
                     + new Vector2(0, -u.WalkLean * 1.5f);                           // walk: a slight stride lift
        p += pose;
        // a brief size pop on flinch (recoil compresses a touch) so a hit reads as a jolt
        float bodyScale = Util.Clamp(1f + u.FlinchAnim * 0.12f - u.RecoilAnim * 0.05f, 0.85f, 1.18f);

        // ground contact shadow (sits on the platform top when elevated). A two-layer ellipse —
        // a wider soft penumbra + a tighter darker core, nudged toward bottom-right (consistent
        // top-left key light) — so the figure reads as a solid object grounded on the board rather
        // than a flat token. Drones cast a smaller, fainter shadow that shrinks as they rise (sells
        // the hover). Kept dark+subtle so it never competes with signal.
        {
            float sg = drone ? Util.Clamp(1f - hover / 22f, 0.45f, 1f) : 1f;   // drones: smaller/fainter when high
            int scx = (int)(foot.X + 1.5f), scy = (int)(foot.Y + 18);
            Raylib.DrawEllipse(scx, scy, 17f * sg, 7f * sg, Raylib.Fade(Pal.RGBA(0, 0, 0), 0.18f * figAlpha * sg));
            Raylib.DrawEllipse(scx, scy, 12f * sg, 4.6f * sg, Raylib.Fade(Pal.RGBA(0, 0, 0), 0.34f * figAlpha * sg));
        }

        // *** team-colour UNDER-GLOW — the keystone of the inverted hierarchy ***
        // A soft saturated radial halo beneath every LIVE unit (cyan friendly / hot red enemy /
        // gold VIP) so units are the brightest, most-saturated objects on the board and pop off
        // the now-muted cover. Drawn UNDER the figure so it reads as the unit being lit, not an
        // overlay. Dormant/suspicious pods are skipped (they keep their faint awareness markers).
        if (!inactive)
        {
            // hotter, more saturated glow colour for enemies so live foes burn red; the friendly
            // glow rides the cyan team colour. Boss/HVT pick up a touch more reach.
            Color glow = vip ? Pal.VipGold
                       : friend ? Pal.Friend
                       : (elite ? Pal.Elite : Pal.Foe);
            float gpulse = 0.85f + 0.15f * MathF.Sin((float)Raylib.GetTime() * 2.4f + u.Bob);
            float gA = (friend ? 0.30f : 0.34f) * figAlpha * gpulse;   // enemies a hair hotter
            float gR = (elite || hvt) ? 30f : 25f;
            // a wide soft bloom + a tighter brighter core radial (two rings read as a glow on llvmpipe)
            Raylib.DrawCircleV(p, gR,        Raylib.Fade(glow, gA * 0.45f));
            Raylib.DrawCircleV(p, gR * 0.68f, Raylib.Fade(glow, gA * 0.85f));
        }

        // selection ring — full strength (signal), plus a layered glow so the eye snaps to who's
        // acting. A wide soft bloom halo (the 5.2 post-FX amplifies it), a brighter mid ring, and
        // four short cardinal "feet" tick-marks that make the active footprint unmistakable even
        // against busy cover. All in the friendly Accent role colour; pulses gently.
        if (g.Selected == u)
        {
            float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 5f);
            var sc = foot + new Vector2(0, 17);
            // wide soft bloom — reads as the unit being "lit"
            Raylib.DrawRing(sc, 24f, 32f, 0, 360, 56, Raylib.Fade(Pal.Accent, 0.07f + 0.07f * pulse));
            Raylib.DrawRing(sc, 21f, 26f, 0, 360, 56, Raylib.Fade(Pal.Accent, 0.16f + 0.12f * pulse));
            // crisp mid ring (the primary "this one is selected" signal)
            Raylib.DrawRing(sc, 17f, 21f, 0, 360, 56, Raylib.Fade(Pal.Accent, 0.55f + 0.40f * pulse));
            // four cardinal corner ticks just outside the ring, like a target bracket on the floor
            for (int k = 0; k < 4; k++)
            {
                float aa = k * (MathF.PI / 2f) + MathF.PI / 4f;
                var dd = new Vector2(MathF.Cos(aa), MathF.Sin(aa) * 0.5f);   // squashed to read flat on the ground
                Raylib.DrawLineEx(sc + dd * 22f, sc + dd * 28f, 2.2f, Raylib.Fade(Pal.Accent, 0.5f + 0.35f * pulse));
            }
        }

        // 4.4 ghost ring: soft pulsing ring on friendly units while the squad is concealed
        if (friend && !vip && g.SquadConcealed)
        {
            float pulse = 0.3f + 0.3f * MathF.Sin((float)Raylib.GetTime() * 2.8f + u.Bob);
            Raylib.DrawRing(foot + new Vector2(0, 17), 20f, 23f, 0, 360, 40,
                            Raylib.Fade(Pal.Friend, pulse));
        }

        // DECAPITATE marker: a bold gold double HVT ring on the ground so the target reads out of
        // the pack at a glance (full-alpha signal, drawn regardless of alert state / dimming).
        if (hvt)
        {
            float pulse = 0.55f + 0.45f * MathF.Sin((float)Raylib.GetTime() * 4.2f + u.Bob);
            var hc = Pal.VipGold;
            Raylib.DrawRing(foot + new Vector2(0, 17), 20f, 24f, 0, 360, 48, Raylib.Fade(hc, 0.85f));
            Raylib.DrawRing(foot + new Vector2(0, 17), 26f, 28f, 0, 360, 48, Raylib.Fade(hc, 0.30f + 0.40f * pulse));
        }

        // body — apply figAlpha to the figure shape (bodyScale gives a brief flinch pop).
        // Figures were ~12% small for the Tile-64 board; enlarged ~16% so units out-mass the
        // (now-muted) cover blocks. A brighter rim makes the saturated outline read at a glance.
        float fig = 1.16f;                                  // overall figure upscale
        float bodyR = 18.5f * bodyScale;
        Raylib.DrawCircleV(p, bodyR, Raylib.Fade(dark, figAlpha));
        Raylib.DrawRing(p, bodyR - 2.8f, bodyR + 0.8f, 0, 360, 40, Raylib.Fade(main, figAlpha));
        Raylib.DrawCircleV(p, bodyR - 2.8f, Raylib.Fade(main, 0.22f * figAlpha));

        // class silhouette — a recognizable primitive cue per class (shape-redundant, colorblind-
        // safe: meaning rides on the SHAPE, inheriting the team colour + focal figAlpha).
        if (elite) Raylib.DrawRing(p, 20.5f * bodyScale, 23f * bodyScale, 0, 360, 40, Raylib.Fade(Pal.Elite, 0.55f * figAlpha));
        DrawSilhouette(u, p, main, figAlpha, bodyScale * fig, u.Facing);

        // DECAPITATE: a gold crown chevron + "HVT" tag above the target (full-alpha signal, drawn
        // in every alert state so the mark reads even on a dormant target). The body ring above
        // already pulses; this names the priority. Placed before the inactive early-return.
        if (hvt)
        {
            var hc = Pal.VipGold;
            float cyT = p.Y - (elite ? 54f : 44f);          // sit above any elite name tag
            Raylib.DrawLineEx(new Vector2(p.X - 7f, cyT + 4f), new Vector2(p.X - 3.5f, cyT - 3f), 2f, hc);
            Raylib.DrawLineEx(new Vector2(p.X - 3.5f, cyT - 3f), new Vector2(p.X, cyT + 2f), 2f, hc);
            Raylib.DrawLineEx(new Vector2(p.X, cyT + 2f), new Vector2(p.X + 3.5f, cyT - 3f), 2f, hc);
            Raylib.DrawLineEx(new Vector2(p.X + 3.5f, cyT - 3f), new Vector2(p.X + 7f, cyT + 4f), 2f, hc);
            float tw = Raylib.MeasureTextEx(Cfg.Font, "HVT", 11, 1f).X;
            Raylib.DrawTextEx(Cfg.Font, "HVT", new Vector2((int)(p.X - tw / 2), (int)(cyT - 18f)), 11, 1f, hc);
        }

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
            else
            {
                // Unaware pod: a DELIBERATE clean dashed ring + a clear "?" — dim but unambiguous
                // (reads as "dormant contact here", not a muddy brown blob). Dashes = "not yet live".
                float t = (float)Raylib.GetTime();
                Color dim = Pal.RGBA(150, 132, 120);
                for (int k = 0; k < 8; k++)
                {
                    float a0 = k * 45f + t * 14f;          // slow rotation so it reads as "scanning"
                    Raylib.DrawRing(p, 17f, 19.5f, a0, a0 + 24f, 6, Raylib.Fade(dim, 0.55f));
                }
                float qw = Raylib.MeasureTextEx(Cfg.Font, "?", 22, 1f).X;
                Raylib.DrawTextEx(Cfg.Font, "?", new Vector2((int)(p.X - qw / 2), (int)(p.Y - 12)), 22, 1f, Raylib.Fade(dim, 0.95f));
            }
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

        // CORPSMAN: a bright WHITE medical cross on the cyan body so "the medic" reads at a glance.
        // White (not green) keeps it distinct from the enemy MEDIC's green cross AND from the other
        // friendly classes; the contrast is value-based, so it survives the colorblind palette too.
        if (u.Team == Team.Player && u.Cls == "CORPSMAN")
        {
            var cw = Pal.RGBA(245, 252, 255);
            Raylib.DrawRectangle((int)p.X - 6, (int)p.Y - 2, 13, 4, Raylib.Fade(cw, figAlpha));
            Raylib.DrawRectangle((int)p.X - 2, (int)p.Y - 6, 4, 13, Raylib.Fade(cw, figAlpha));
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

        // spotter: an outward radar "scan" pulse + a painted-target line so the force-multiplier
        // reads — the player can SEE it designating the squad's priority target (kill it to break
        // the crossfire). Signal-level, so drawn at a readable alpha regardless of focal dimming.
        if (u.Team == Team.Enemy && u.Cls == "SPOTTER")
        {
            float t = (float)Raylib.GetTime();
            // an expanding scan ring (radar sweep) that breathes outward from the unit
            float scan = (t * 0.6f + u.Bob) % 1f;                     // 0..1 sweep phase
            float rad = 14f + scan * 16f;
            Raylib.DrawRing(p, rad, rad + 1.6f, 0, 360, 36, Raylib.Fade(Pal.Foe, (1f - scan) * 0.45f));
            // a thin, dashed "painting" beam to the designated target while this beacon is live
            if (u.Active && g.EnemyFocus != null && g.EnemyFocus.Alive)
            {
                var tgt = g.EnemyFocus.Pos - new Vector2(0, g.Grid.IsHigh(g.EnemyFocus.X, g.EnemyFocus.Y) ? ElevLift : 0f);
                var dir = tgt - p;
                float len = dir.Length();
                if (len > 1f)
                {
                    dir /= len;
                    float pulse = 0.18f + 0.16f * (0.5f + 0.5f * MathF.Sin(t * 5f + u.Bob));
                    for (float d = 18f; d < len - 14f; d += 11f)      // dashed segments toward the target
                        Raylib.DrawLineEx(p + dir * d, p + dir * Math.Min(d + 5f, len - 14f), 1.4f, Raylib.Fade(Pal.Foe, pulse));
                    // a small reticle tick on the painted target
                    Raylib.DrawRing(tgt, 13f, 14.6f, 0, 360, 24, Raylib.Fade(Pal.Foe, pulse + 0.12f));
                }
            }
        }

        // bombard: a pulsing CHARGING CORE while a strike is winding up (ChargeTurns>0) so the
        // "it's about to fire" reads on the unit itself. Signal-level (full alpha) — a danger cue.
        if (u.Team == Team.Enemy && u.Cls == "BOMBARD" && u.ChargeTurns > 0)
        {
            float ct = (float)Raylib.GetTime();
            float cp = 0.5f + 0.5f * MathF.Sin(ct * 7f);
            Raylib.DrawCircleV(p, (3.5f + 2.5f * cp), Raylib.Fade(Pal.RGBA(255, 180, 120), 0.55f + 0.35f * cp));
            Raylib.DrawRing(p, 10f, 12f, 0, 360, 28, Raylib.Fade(Pal.Foe, 0.35f + 0.45f * cp));
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

    // SHOVE targeting preview: ring every shovable adjacent enemy, and for the hovered valid
    // target draw the push direction + destination (or a collision indicator if it's blocked).
    static void DrawShove(Game g)
    {
        if (!g.ShoveMode || g.Selected == null) return;
        var u = g.Selected;
        float t = (float)Raylib.GetTime();

        // ring all valid (adjacent, alive) enemy targets so the player sees what's shovable.
        foreach (var e in g.Enemies)
        {
            if (!e.Alive || Util.ChebyDist(u.X, u.Y, e.X, e.Y) > 1) continue;
            float pulse = 20f + MathF.Sin(t * 6f) * 2.5f;
            Raylib.DrawCircleLines((int)e.Pos.X, (int)e.Pos.Y, pulse, Raylib.Fade(Pal.Friend, 0.7f));
        }

        var tgt = g.ShoveTarget;
        if (tgt == null || !g.ShoveValid) return;

        int dx = Util.Sign(tgt.X - u.X), dy = Util.Sign(tgt.Y - u.Y);
        int destX = tgt.X + dx, destY = tgt.Y + dy;
        bool clear = g.Grid.IsFloor(destX, destY) && !g.IsOccupiedByOther(destX, destY, tgt);

        var from = tgt.Pos;
        var arrowEnd = from + new Vector2(dx, dy) * (Cfg.Tile * (clear ? 0.95f : 0.55f));
        Color col = clear ? Pal.Friend : Pal.Foe;

        // push arrow from the target toward the destination
        Raylib.DrawLineEx(from, arrowEnd, 2.4f, Raylib.Fade(col, 0.9f));
        var perp = new Vector2(-dy, dx);
        Raylib.DrawLineEx(arrowEnd, arrowEnd - new Vector2(dx, dy) * 8f + perp * 6f, 2.2f, Raylib.Fade(col, 0.9f));
        Raylib.DrawLineEx(arrowEnd, arrowEnd - new Vector2(dx, dy) * 8f - perp * 6f, 2.2f, Raylib.Fade(col, 0.9f));

        if (clear)
        {
            // destination tile highlight
            Raylib.DrawRectangleRec(Util.TileRect(destX, destY), Raylib.Fade(Pal.Friend, 0.25f));
            Raylib.DrawRectangleLinesEx(Util.TileRect(destX, destY), 2f, Pal.Friend);
        }
        else
        {
            // collision indicator: a red burst marker where it would slam (off the target edge)
            var slam = from + new Vector2(dx, dy) * (Cfg.Tile * 0.55f);
            float r = 9f + MathF.Sin(t * 9f) * 2f;
            Raylib.DrawCircleLines((int)slam.X, (int)slam.Y, r, Pal.Foe);
            // a small "X" to read as "blocked / impact"
            Raylib.DrawLineEx(slam + new Vector2(-5, -5), slam + new Vector2(5, 5), 2f, Pal.Foe);
            Raylib.DrawLineEx(slam + new Vector2(-5, 5), slam + new Vector2(5, -5), 2f, Pal.Foe);
        }
    }

    // Always-on MARK indicator: a rotating diamond reticle + "MARKED" tag over every designated
    // foe, so the squad-wide focus-fire target reads on the board (not just in the tooltip). Drawn
    // for every marked enemy regardless of mode, since the mark persists through the enemy turn.
    static void DrawMarkIndicators(Game g)
    {
        float t = (float)Raylib.GetTime();
        foreach (var e in g.Enemies)
        {
            if (!e.Alive || !e.Marked) continue;
            var c = e.Pos;
            float ang = t * 70f;
            float r = 26f + MathF.Sin(t * 5f) * 2.5f;
            // a spinning open diamond reticle (focus-fire target)
            for (int k = 0; k < 4; k++)
            {
                float a0 = ang + k * 90f;
                var p0 = c + AngVec(a0) * r;
                var p1 = c + AngVec(a0 + 90f) * r;
                Raylib.DrawLineEx(p0, p1, 2.2f, Raylib.Fade(Pal.Foe, 0.9f));
            }
            // four corner ticks
            for (int k = 0; k < 4; k++)
            {
                float a0 = k * 90f + 45f;
                var pa = c + AngVec(a0) * (r - 5f);
                var pb = c + AngVec(a0) * (r + 5f);
                Raylib.DrawLineEx(pa, pb, 2f, Raylib.Fade(Pal.Foe, 0.85f));
            }
            Raylib.DrawTextEx(Cfg.Font, "MARKED", new Vector2(c.X - 22, c.Y - r - 16), 12, 1f, Pal.Foe);
        }
    }

    static Vector2 AngVec(float deg) { float r = deg * MathF.PI / 180f; return new Vector2(MathF.Cos(r), MathF.Sin(r)); }

    // MARK targeting preview: while the sharpshooter is in MarkMode, ring every legal target and
    // draw a designator line from the soldier to the hovered foe (green=valid, dim=invalid).
    static void DrawMark(Game g)
    {
        if (!g.MarkMode || g.Selected == null) return;
        var u = g.Selected;
        float t = (float)Raylib.GetTime();
        foreach (var e in g.Enemies)
        {
            if (!e.Alive || e.Marked) continue;
            if (!g.Grid.HasLineOfSight(u.X, u.Y, e.X, e.Y)) continue;
            float pulse = 22f + MathF.Sin(t * 6f) * 2.5f;
            Raylib.DrawCircleLines((int)e.Pos.X, (int)e.Pos.Y, pulse, Raylib.Fade(Pal.Good, 0.6f));
        }
        var tgt = g.MarkTarget;
        if (tgt == null) return;
        Color col = g.MarkValid ? Pal.Good : Pal.TxtDim;
        Raylib.DrawLineEx(u.Pos, tgt.Pos, 1.8f, Raylib.Fade(col, 0.7f));
        float ang = t * 90f;
        Raylib.DrawRing(tgt.Pos, 20, 23, ang, ang + 70, 16, col);
        Raylib.DrawRing(tgt.Pos, 20, 23, ang + 180, ang + 250, 16, col);
        Raylib.DrawCircleLines((int)tgt.Pos.X, (int)tgt.Pos.Y, 26, Raylib.Fade(col, 0.6f));
    }

    // GRAPPLE targeting preview: ring every foe in reach, and on the hovered target draw a pull
    // arrow from the foe TOWARD the assault (the tile it'll be yanked to), green=valid.
    static void DrawGrapple(Game g)
    {
        if (!g.GrappleMode || g.Selected == null) return;
        var u = g.Selected;
        float t = (float)Raylib.GetTime();
        foreach (var e in g.Enemies)
        {
            if (!e.Alive || Util.ChebyDist(u.X, u.Y, e.X, e.Y) > Game.GrappleReach) continue;
            float pulse = 20f + MathF.Sin(t * 6f) * 2.5f;
            Raylib.DrawCircleLines((int)e.Pos.X, (int)e.Pos.Y, pulse, Raylib.Fade(Pal.Friend, 0.7f));
        }
        var tgt = g.GrappleTarget;
        if (tgt == null || !g.GrappleValid) return;

        int dx = Util.Sign(u.X - tgt.X), dy = Util.Sign(u.Y - tgt.Y);   // pull direction = toward the assault
        int destX = tgt.X + dx, destY = tgt.Y + dy;
        var from = tgt.Pos;
        var arrowEnd = from + new Vector2(dx, dy) * (Cfg.Tile * 0.9f);
        Raylib.DrawLineEx(from, arrowEnd, 2.4f, Raylib.Fade(Pal.Friend, 0.9f));
        var perp = new Vector2(-dy, dx);
        Raylib.DrawLineEx(arrowEnd, arrowEnd - new Vector2(dx, dy) * 8f + perp * 6f, 2.2f, Raylib.Fade(Pal.Friend, 0.9f));
        Raylib.DrawLineEx(arrowEnd, arrowEnd - new Vector2(dx, dy) * 8f - perp * 6f, 2.2f, Raylib.Fade(Pal.Friend, 0.9f));
        if (g.Grid.IsFloor(destX, destY) && !g.IsOccupiedByOther(destX, destY, tgt))
        {
            Raylib.DrawRectangleRec(Util.TileRect(destX, destY), Raylib.Fade(Pal.Friend, 0.25f));
            Raylib.DrawRectangleLinesEx(Util.TileRect(destX, destY), 2f, Pal.Friend);
        }
    }

    // Always-on SUPPRESSED indicator (gunner SUPPRESSING FIRE): a downward "pinned" bracket cage over
    // every pinned foe, so area denial reads on the board (shape-redundant — it's a distinct cage glyph,
    // not just a colour). Drawn for every pinned enemy regardless of mode (the pin holds through the enemy
    // turn). Pulses gently so it reads as a live debuff.
    static void DrawPinIndicators(Game g)
    {
        float t = (float)Raylib.GetTime();
        foreach (var e in g.Enemies)
        {
            if (!e.Alive || e.Pinned <= 0) continue;
            var c = e.Pos;
            float r = 22f, jit = MathF.Sin(t * 7f) * 1.5f;
            Color col = Raylib.Fade(Pal.Foe, 0.85f);
            // four corner brackets pressing inward (a "held down" cage)
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                var corner = c + new Vector2(sx * r, sy * (r + jit));
                Raylib.DrawLineEx(corner, corner - new Vector2(sx * 8f, 0), 2.2f, col);
                Raylib.DrawLineEx(corner, corner - new Vector2(0, sy * 8f), 2.2f, col);
            }
            Raylib.DrawTextEx(Cfg.Font, "PINNED", new Vector2(c.X - 22, c.Y - r - 16), 11, 1f, Pal.Foe);
        }
    }

    // SUPPRESSING FIRE targeting preview: while the gunner is in PinMode, ring every legal target and
    // paint the 3x3 zone footprint under the hovered foe (everyone in it gets pinned).
    static void DrawPin(Game g)
    {
        if (!g.PinMode || g.Selected == null) return;
        var u = g.Selected;
        float t = (float)Raylib.GetTime();
        foreach (var e in g.Enemies)
        {
            if (!e.Alive || Util.TileDist(u.X, u.Y, e.X, e.Y) > Game.PinRange) continue;
            if (!g.Grid.HasLineOfSight(u.X, u.Y, e.X, e.Y)) continue;
            float pulse = 22f + MathF.Sin(t * 6f) * 2.5f;
            Raylib.DrawCircleLines((int)e.Pos.X, (int)e.Pos.Y, pulse, Raylib.Fade(Pal.Foe, 0.55f));
        }
        var tgt = g.PinTarget;
        if (tgt == null) return;
        Color col = g.PinValid ? Pal.Foe : Pal.TxtDim;
        Raylib.DrawLineEx(u.Pos, tgt.Pos, 1.8f, Raylib.Fade(col, 0.6f));
        // 3x3 zone footprint (the suppression area)
        for (int oy = -1; oy <= 1; oy++)
        for (int ox = -1; ox <= 1; ox++)
        {
            int zx = tgt.X + ox, zy = tgt.Y + oy;
            if (!g.Grid.InBounds(zx, zy)) continue;
            Raylib.DrawRectangleRec(Util.TileRect(zx, zy), Raylib.Fade(col, 0.16f));
            Raylib.DrawRectangleLinesEx(Util.TileRect(zx, zy), 1.4f, Raylib.Fade(col, 0.55f));
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

    // CROSSFIRE telegraph (Wave 2 mechanic made visible). The crossfire bonus (+aim/+crit when a
    // target is converged on from two diverging angles) is surfaced in the shot tooltip but is
    // INVISIBLE on the board, so the player can't SEE the pincer while positioning. When the player
    // is actively AIMING at an enemy, OR hovering an enemy the selected soldier could shoot, AND that
    // shot is a genuine crossfire, draw a faint converging-fire prong from EACH threatening squadmate
    // to the target so the pincer geometry reads at a glance.
    //
    // GATING (harness byte-stability): only the INTERACTIVE aim/hover path. The headless SIGHTLINE_SHOT
    // autopilot never sets AimMode and isn't hovering a shootable enemy on a normal frame, so this is
    // naturally inert there; we also require g.AimMode OR g.ShowOdds (set only by UpdateHoverAndAim's
    // hover-to-shoot branch) on the player turn with a live selected soldier, which the shot harness
    // doesn't satisfy. Never draws on the enemy turn or with no selection.
    //
    // COLOUR: the friendly/good accent (Pal.Good), NOT the enemy red of the aim line — crossfire is a
    // PLAYER advantage, and the red aim line already points at the foe, so the green prongs read
    // distinctly as "your other guns are also on this target". Thin + low-alpha (squint test).
    static void DrawCrossfire(Game g)
    {
        var aimer = g.Selected;
        if (aimer == null || g.Phase != Phase.PlayerTurn || !g.IsPlayerInteractive()) return;
        if (aimer.Team != Team.Player) return;

        // Resolve the single target under consideration on the interactive path:
        //  - aim mode: the locked-in aim target (mirrors DrawAim's gate),
        //  - otherwise: the hovered enemy the selected soldier could shoot (mirrors the
        //    UpdateHoverAndAim hover-to-shoot branch, which is the only other thing that sets ShowOdds).
        Unit target = null;
        if (g.AimMode)
        {
            if (g.AimTarget != null && g.AimTarget.Alive && g.AimValid) target = g.AimTarget;
        }
        else if (g.ShowOdds && g.HoverValid)
        {
            var hov = g.UnitAt(g.HoverX, g.HoverY);
            if (hov != null && hov.Alive && hov.Team == Team.Enemy && g.CanTarget(aimer, hov)) target = hov;
        }
        if (target == null) return;

        // The mechanic itself decides whether this is a crossfire (so the indicator can never lie /
        // drift from Combat's thresholds). Only light up when it's genuinely a pincer.
        if (!Combat.InCrossfire(g.Grid, aimer, target)) return;

        var d = target.Pos;
        float t = (float)Raylib.GetTime();
        // gentle travelling dash so the converging fire reads as live/active without flashing.
        float phase = t * 2.2f;

        int prongs = 0;
        var all = Combat.AllUnits;
        if (all != null)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var ally = all[i];
                if (ally == null || ally == aimer) continue;       // the aimer's own line is the red aim line
                if (!ally.Alive) continue;
                if (ally.Team != aimer.Team) continue;             // same squad only
                if (ally.Cls == "VIP") continue;                   // non-combatant asset (VIP / caged captive)
                if (ally.X == target.X && ally.Y == target.Y) continue;
                // credible converging threat = an actual line of sight to the target (the dominant gate in
                // Combat.InCrossfire). Drawing only LoS allies keeps every prong an honest second gun.
                if (!g.Grid.HasLineOfSight(ally.X, ally.Y, target.X, target.Y)) continue;

                DrawCrossfireProng(ally.Pos, d, phase);
                prongs++;
            }
        }
        if (prongs == 0) return;   // nothing to show (defensive; InCrossfire implies >=1)

        // converging-arrows glyph + label at the target so the pincer is named, not just inferred.
        float pulse = 0.55f + 0.45f * MathF.Sin(t * 4f);
        Color lab = Raylib.Fade(Pal.Good, 0.85f * pulse + 0.15f);
        // two small chevrons pointing inward toward the target centre (a >< convergence cue)
        Raylib.DrawLineEx(new Vector2(d.X - 16, d.Y - 6), new Vector2(d.X - 9, d.Y), 1.8f, lab);
        Raylib.DrawLineEx(new Vector2(d.X - 16, d.Y + 6), new Vector2(d.X - 9, d.Y), 1.8f, lab);
        Raylib.DrawLineEx(new Vector2(d.X + 16, d.Y - 6), new Vector2(d.X + 9, d.Y), 1.8f, lab);
        Raylib.DrawLineEx(new Vector2(d.X + 16, d.Y + 6), new Vector2(d.X + 9, d.Y), 1.8f, lab);
        var lp = new Vector2((int)d.X - 30, (int)(d.Y + 22));
        Raylib.DrawTextEx(Cfg.Font, "CROSSFIRE", lp, 11, 1f, lab);
    }

    // One converging-fire prong: a thin low-alpha line from a squadmate to the target, with a short
    // brighter travelling dash sliding toward the target (reads as live, directional fire) and a small
    // arrowhead at the target end. All Pal.Good, all faint — reinforces the pincer without clutter.
    static void DrawCrossfireProng(Vector2 from, Vector2 to, float phase)
    {
        var seg = to - from;
        float len = seg.Length();
        if (len < 1f) return;
        var dir = seg / len;

        // base line: very faint full-length connector.
        Raylib.DrawLineEx(from, to, 1.4f, Raylib.Fade(Pal.Good, 0.32f));

        // travelling highlight: a short bright dash that slides from the ally toward the target.
        float dashLen = 18f;
        float headRoom = 22f;                                   // stop short so it doesn't fight the target reticle
        float travel = ((phase % 1f) + 1f) % 1f * MathF.Max(1f, len - headRoom);
        var ds = from + dir * travel;
        var de = from + dir * MathF.Min(len - headRoom, travel + dashLen);
        Raylib.DrawLineEx(ds, de, 2.2f, Raylib.Fade(Pal.Good, 0.5f));

        // arrowhead near the target end (pointing inward) — the "fire arrives here" cue.
        var tip = to - dir * (headRoom - 4f);
        var perp = new Vector2(-dir.Y, dir.X);
        Raylib.DrawLineEx(tip, tip - dir * 7f + perp * 4f, 1.8f, Raylib.Fade(Pal.Good, 0.55f));
        Raylib.DrawLineEx(tip, tip - dir * 7f - perp * 4f, 1.8f, Raylib.Fade(Pal.Good, 0.55f));
    }
}
