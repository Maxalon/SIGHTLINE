using System;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// Draws the battlefield, units and tactical overlays.
public static class Renderer
{
    // how far raised terrain (and anything standing on it) lifts on screen
    public const float ElevLift = 8f;

    // UNDERTOW W7 — board-space key light. A single fixed, deterministic light source
    // placed off the upper-left of the board (matching the top-left face-catch convention
    // used on cover/plateaus) turns the flat checker into a LIT space: tiles nearer the
    // light read brighter/warmer, far tiles fall off toward the corner shadow. Pure
    // function of tile centre -> constant, so the SIGHTLINE_SHOT harness stays byte-stable
    // and the balance flywheel is unaffected (it never renders). NOT hue-only: it moves
    // VALUE, so it survives the colorblind palette and can't carry meaning by itself.
    // Light origin in board-fraction space (0,0 = top-left tile centre .. 1,1 = bottom-right).
    static readonly Vector2 LightOrigin = new(0.28f, 0.10f);
    // Returns a light factor in ~[-1, +1]: +1 fully lit (at the origin), 0 at mid-fall,
    // negative in the far corner shadow. Deterministic; depends only on tile coords.
    static float FloorLight(Game g, int x, int y)
    {
        float fx = g.Grid.W > 1 ? x / (float)(g.Grid.W - 1) : 0.5f;
        float fy = g.Grid.H > 1 ? y / (float)(g.Grid.H - 1) : 0.5f;
        // squared radial falloff from the light origin, plus a mild directional term so
        // the gradient has a consistent "sun" direction rather than a flat bullseye.
        float dx = fx - LightOrigin.X, dy = fy - LightOrigin.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);          // 0 at origin .. ~1.2 far corner
        float radial = 1f - Util.Clamp(dist / 1.05f, 0f, 1f);// 1 near light -> 0 far
        radial = radial * radial;                            // squared falloff = softer core, deeper corners
        // directional bias: down-right of the origin sits a touch darker (raking light).
        float dirBias = Util.Clamp((dx + dy) * 0.5f + 0.5f, 0f, 1f); // 0 up-left .. 1 down-right
        float lit = radial * 1.15f - dirBias * 0.35f;        // combine; corner goes slightly negative
        return Math.Clamp(lit, -0.55f, 1f);
    }

    // Apply a key-light factor (from FloorLight) to a surface colour: lift toward white on the
    // lit side, sink toward near-black in shadow. Moves VALUE only (colorblind-safe). `amt` caps
    // how far the light can push so terrain stays QUIET relative to units.
    static Color KeyLit(Color c, float lit, float amt)
    {
        return lit >= 0f
            ? Pal.Mix(c, Pal.RGBA(255, 252, 244), lit * amt)
            : Pal.Mix(c, Pal.RGBA(3, 5, 9), -lit * amt);
    }

    // SIGNAL W3 — lift a colour in VALUE only (clamped additive). Unlike a mix toward white,
    // this keeps the hue deltas between biomes intact, so a lifted floor colour still reads
    // as that biome. Used to raise plateau tops above their own floor.
    static Color Lift(Color c, int d) =>
        Pal.RGBA(Math.Clamp(c.R + d, 0, 255), Math.Clamp(c.G + d, 0, 255), Math.Clamp(c.B + d, 0, 255), c.A);

    // --- 5.4 Procedural noise overlay -----------------------------------------
    // A 128x128 tiling Perlin-noise texture generated once after the GL context is
    // ready (lazy-init on the first DrawBoard call).  Drawn at low alpha over floor
    // tiles, cover tops and plateau top faces so each surface reads as a textured
    // material without fighting unit/threat/objective legibility.  Falls back to a
    // no-op if texture creation fails (never crashes).
    static Texture2D _noise;
    static bool _noiseReady;

    // W6 Task 3 — enemy-intent ENTRANCE POP. When a NEW hostile's telegraph appears (IntentUnit
    // changes), snap the eye to the mark: the target reticle enters with a brief scale/brightness
    // pop that eases out over ~POP_DUR seconds. We can't touch Game.cs, so we detect the change
    // renderer-side (a ref compare) and stamp the start time. Deterministic in the fixed-time
    // screenshot harness (GetTime is fixed per frame) — does NOT change the beat length; the beat
    // still clears on Game.ClearIntent. Purely a visual entrance.
    static object _lastIntentUnit;
    static float _intentPopStart = -100f;
    const float PopDur = 0.35f;

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

    // deterministic uint->[0,1) hash (xorshift-mix). Used ONLY for the frozen per-tile
    // biome-signature constants below — a pure function of tile coords, no RNG in the draw
    // path, so the SIGHTLINE_SHOT harness stays byte-reproducible.
    static float SHash(int a, int b, int salt)
    {
        uint x = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(salt * 83492791);
        x ^= x >> 16; x *= 0x7FEB352Du;
        x ^= x >> 15; x *= 0x846CA68Bu;
        x ^= x >> 16;
        return (x & 0xFFFFFFu) / 16777216f;
    }

    // HORIZON W5 — one BOLD, cheap, DETERMINISTIC structural cue per biome, drawn over the
    // FLOOR tiles so each mission reads as a distinct *place*. All positions/alphas are pure
    // functions of tile coords + frozen constants (SHash) or a slow global time (for a gentle
    // shimmer that reproduces at the harness's fixed frame). Kept restrained so the squint test
    // holds (units still dominate); the emissive cues (magma/void-neon) are what the bloom
    // catches. Runs once per floor tile — a handful of primitives each, no allocation.
    static void DrawBiomeSignature(Game g, Biome bm)
    {
        float t = (float)Raylib.GetTime();
        for (int gx = 0; gx < g.Grid.W; gx++)
            for (int gy = 0; gy < g.Grid.H; gy++)
            {
                if (g.Grid.Tiles[gx, gy] != TileType.Floor) continue;
                var r = Util.TileRect(gx, gy);
                float cx = r.X + r.Width * 0.5f, cy = r.Y + r.Height * 0.5f;

                switch (bm.Ambient)
                {
                    // W6 — LAND THE SIGNATURE. Each biome's structural cue was near-invisible
                    // (alpha 0.06–0.30, sparse). Alphas/coverage ~1.6–2× so the cue actually READS
                    // as a secondary "this is a place" texture — still low-saturation, still below
                    // the unit/objective/cover hierarchy, and kept clear of the red/amber/cyan
                    // SIGNAL hues (the teal/violet lattice stays a dim structural grid, not a halo).
                    case AmbientKind.Ember:   // MAGMA — glowing emissive fissures across ~40% of tiles
                    {
                        if (SHash(gx, gy, 11) > 0.40f) break;
                        float ph = SHash(gx, gy, 13) * 6.28f;
                        float pulse = 0.55f + 0.45f * MathF.Sin(t * 2.2f + ph);   // veins breathe
                        var lava = Pal.RGBA(255, 120, 40);
                        // a jagged crack: 3 segments zig-zagging across the tile
                        float ax = r.X + 6 + SHash(gx, gy, 1) * (r.Width - 12);
                        Vector2 pa = new(ax, r.Y + 4);
                        for (int k = 1; k <= 3; k++)
                        {
                            float side = ((k & 1) == 0 ? -1f : 1f) * (5f + SHash(gx, gy, k * 7) * 9f);
                            Vector2 pb = new(cx + side, r.Y + 4 + k * (r.Height - 8) / 3f);
                            Raylib.DrawLineEx(pa, pb, 2.6f, Raylib.Fade(lava, 0.44f * pulse));   // hot glow
                            Raylib.DrawLineEx(pa, pb, 1.2f, Raylib.Fade(Pal.RGBA(255, 220, 150), 0.72f * pulse)); // core
                            pa = pb;
                        }
                        break;
                    }
                    case AmbientKind.Snow:    // TUNDRA — pale frost sheen (cool diagonal light streaks)
                    {
                        if (SHash(gx, gy, 21) > 0.55f) break;
                        var frost = Pal.RGBA(210, 232, 248);
                        float off = SHash(gx, gy, 23) * r.Width * 0.5f;
                        for (int k = 0; k < 2; k++)
                        {
                            float sx = r.X + off + k * 10f;
                            Raylib.DrawLineEx(new Vector2(sx, r.Y + r.Height - 4),
                                              new Vector2(sx + 12f, r.Y + 4), 1.5f, Raylib.Fade(frost, 0.18f));
                        }
                        // a few frost crystals (tiny bright dots) on some tiles
                        if (SHash(gx, gy, 25) < 0.32f)
                            Raylib.DrawCircleV(new Vector2(cx, cy), 1.5f, Raylib.Fade(frost, 0.44f));
                        break;
                    }
                    case AmbientKind.Mote:    // VOID — faint glowing violet grid lines (a lattice)
                    case AmbientKind.Scan:    // NEON — faint glowing teal grid lines
                    {
                        var glow = bm.Ambient == AmbientKind.Scan ? Pal.RGBA(60, 200, 214) : Pal.RGBA(150, 120, 220);
                        float pulse = 0.5f + 0.5f * MathF.Sin(t * 1.3f + (gx + gy) * 0.5f);
                        float a = (bm.Ambient == AmbientKind.Scan ? 0.24f : 0.22f) * (0.6f + 0.4f * pulse);
                        // left + top tile borders form a continuous lattice across the board
                        Raylib.DrawLineEx(new Vector2(r.X, r.Y), new Vector2(r.X, r.Y + r.Height), 1f, Raylib.Fade(glow, a));
                        Raylib.DrawLineEx(new Vector2(r.X, r.Y), new Vector2(r.X + r.Width, r.Y), 1f, Raylib.Fade(glow, a));
                        // a brighter node dot at ~1/4 of intersections (the bloom catches these)
                        if (((gx + gy) & 3) == 0)
                            Raylib.DrawCircleV(new Vector2(r.X, r.Y), 1.8f, Raylib.Fade(glow, a * 2.4f));
                        break;
                    }
                    case AmbientKind.Ash:     // ASH — darker soot streaks/smudges
                    {
                        if (SHash(gx, gy, 31) > 0.52f) break;
                        var soot = Pal.RGBA(18, 12, 12);
                        float sx = r.X + 4 + SHash(gx, gy, 33) * (r.Width - 8);
                        float sy = r.Y + 4 + SHash(gx, gy, 35) * (r.Height - 8);
                        Raylib.DrawCircleV(new Vector2(sx, sy), 4.5f + SHash(gx, gy, 37) * 5f, Raylib.Fade(soot, 0.34f));
                        break;
                    }
                    case AmbientKind.Gust:    // ARID — warm horizontal dune banding
                    {
                        var sand = Pal.RGBA(150, 118, 66);
                        int band = (gy & 1);
                        // two thin warm bands per tile, offset by row parity, so the board reads as strata
                        float y0 = r.Y + r.Height * (band == 0 ? 0.34f : 0.62f);
                        Raylib.DrawLineEx(new Vector2(r.X, y0), new Vector2(r.X + r.Width, y0), 1.7f, Raylib.Fade(sand, 0.18f));
                        float y1 = r.Y + r.Height * (band == 0 ? 0.72f : 0.20f);
                        Raylib.DrawLineEx(new Vector2(r.X, y1), new Vector2(r.X + r.Width, y1), 1.3f, Raylib.Fade(sand, 0.12f));
                        break;
                    }
                    case AmbientKind.Spore:   // VERDANT — mossy green speckle
                    {
                        var moss = Pal.RGBA(90, 150, 80);
                        for (int k = 0; k < 3; k++)
                        {
                            if (SHash(gx, gy, 41 + k) > 0.58f) continue;
                            float mx = r.X + 5 + SHash(gx, gy, 51 + k) * (r.Width - 10);
                            float my = r.Y + 5 + SHash(gx, gy, 61 + k) * (r.Height - 10);
                            Raylib.DrawCircleV(new Vector2(mx, my), 1.8f + SHash(gx, gy, 71 + k) * 1.5f, Raylib.Fade(moss, 0.30f));
                        }
                        break;
                    }
                    default:                  // STEEL (Dust) — faint industrial panel seams
                    {
                        var seam = Pal.RGBA(120, 140, 165);
                        // a subtle rivet/seam cross on a scattered subset of tiles
                        if (SHash(gx, gy, 91) > 0.55f) break;
                        Raylib.DrawLineEx(new Vector2(r.X + 6, cy), new Vector2(r.X + r.Width - 6, cy), 1f, Raylib.Fade(seam, 0.13f));
                        Raylib.DrawCircleV(new Vector2(r.X + 6, cy), 1.3f, Raylib.Fade(seam, 0.26f));
                        Raylib.DrawCircleV(new Vector2(r.X + r.Width - 6, cy), 1.3f, Raylib.Fade(seam, 0.26f));
                        break;
                    }
                }
            }
    }

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

    // W8: the WAVERING mark's shape — a jagged vertical CRACK (lightning zigzag), the "about to
    // break" cue beside the WVR code. Same doctrine as DrawStatusGlyph: meaning rides on the
    // SHAPE, the amber hue is reinforcement only, so the tag reads in the colorblind palette.
    static void DrawCrackGlyph(Vector2 c, Color col)
    {
        Raylib.DrawLineEx(new Vector2(c.X + 2.5f, c.Y - 6f), new Vector2(c.X - 2f, c.Y - 1f), 1.6f, col);
        Raylib.DrawLineEx(new Vector2(c.X - 2f, c.Y - 1f), new Vector2(c.X + 2f, c.Y + 1f), 1.6f, col);
        Raylib.DrawLineEx(new Vector2(c.X + 2f, c.Y + 1f), new Vector2(c.X - 2.5f, c.Y + 6f), 1.6f, col);
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
        // biome so the room recolours distinctly. Two levers: (1) keep a readable value gap
        // between the two checker colours (the floor has TEXTURE, not a flat wash), darken the
        // mean a touch so it's a low base; (2) push that mean STRONGLY toward the biome Tint hue
        // so STEEL/ARID/TUNDRA/… read as distinct coloured PLACES, not one recolored grey board.
        // W6: Tint pull 0.22 -> 0.40 (the marquee lever — biomes now diverge in hue at a glance);
        // checker retention 0.32 -> 0.40 so the strengthened FloorA/FloorB pair still reads as a
        // checker after the tint. Mean stays dark enough that units/objectives keep the hierarchy.
        Color floorMean = Pal.Mix(bm.FloorA, bm.FloorB, 0.5f);
        floorMean = Pal.Mix(floorMean, Pal.RGBA(6, 9, 13), 0.16f);       // slightly darker base
        floorMean = Pal.Mix(floorMean, bm.Tint, 0.40f);                  // LAND the biome hue (marquee)
        Color fa = Pal.Mix(floorMean, bm.FloorA, 0.40f);                 // keep a readable checker
        Color fb = Pal.Mix(floorMean, bm.FloorB, 0.40f);
        // UNDERTOW W7 — bake the board key light into the floor value so the room reads as a
        // lit space, not a flat wash. Lit tiles lift toward a warm-white; shadowed corner tiles
        // sink toward the biome-tinted deep. The tint on BOTH endpoints keeps the biome hue
        // (STEEL cool / ARID warm / …) intact — the light only reshapes VALUE across the board.
        Color litCol = Pal.Mix(Pal.RGBA(255, 250, 236), bm.Tint, 0.30f); // warm key, tinted toward biome
        Color shadeCol = Pal.Mix(Pal.RGBA(4, 6, 10), bm.Tint, 0.18f);    // cool deep, tinted toward biome
        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
            {
                if (g.Grid.Tiles[x, y] != TileType.Floor) continue;
                var r = Util.TileRect(x, y);
                Color baseCol = ((x + y) & 1) == 0 ? fa : fb;
                float lit = FloorLight(g, x, y);
                // positive light -> lift toward the warm key (capped so the floor never rivals
                // units); negative -> sink toward the cool deep so far corners genuinely recede.
                Color lc = lit >= 0f
                    ? Pal.Mix(baseCol, litCol, lit * 0.16f)
                    : Pal.Mix(baseCol, shadeCol, -lit * 0.34f);
                Raylib.DrawRectangleRec(r, lc);
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

        // HORIZON W5: one BOLD structural signature per biome so a mission reads as a distinct
        // *place*, not just a colour tint (magma fissures / frost sheen / void-neon grid glow /
        // ash soot / arid dune banding / verdant speckle / steel seams). Deterministic (a pure
        // function of tile coords + frozen constants — no RNG), on the floor under terrain/units.
        DrawBiomeSignature(g, bm);

        g.Fx.DrawAmbient();   // per-biome ambient atmosphere, under terrain/units (Wave B)

        DrawElevation(g);
        DrawMoveOverlay(g);
        DrawOverwatchThreat(g);   // tiles each active overwatching enemy covers (reaction-fire danger)
        DrawFocusCones(g);        // COUNTERPLAY: the player's braced FOCUSED-overwatch kill-lanes (gold)
        DrawThreat(g);
        DrawSiegeZones(g);        // persistent pulsing 3x3 danger zone of any charging SIEGE artillery
        DrawBannerAuras(g);       // W8: each live WARBRINGER's no-rout aura boundary (subtle outline)
        DrawEvac(g);
        DrawTerminal(g);
        DrawSabotage(g);
        DrawIntelCache(g);        // W10: the optional gold-diamond intel pickup (objective-level object)
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
        DrawBountyMark(g);        // W10: gold chevron over the BOUNTY secondary's specialist
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
        // biome-tinted plateau faces (keeps each mission reading as a distinct place).
        // HORIZON W5: the raised top faces are a big bright surface — kept below the units
        // in the squint hierarchy (units > terrain).
        // SIGNAL W3: plateaus INHERIT the biome floor hue instead of a universal khaki — hiA/hiB
        // are derived from the same FloorA/FloorB base DrawBoard cooks the floor from, pulled
        // HARDER toward the biome Tint (0.58 vs the floor's 0.40 — high ground is the biome's
        // saturated showcase surface) and lifted in VALUE only (+30). High ground now reads as
        // "the same material, raised, catching the light", and its hue tracks the floor across
        // all 8 biomes (the old Pal.HighA/B slate base with a 0.34 tint pull clustered every
        // biome around one khaki-grey). Checker retention is lighter (0.30) so the stronger
        // tint stays saturated; the noise grain + edge glows carry the plateau texture.
        var bm = g.Biome;
        Color tint = bm.Tint;
        Color fmean = Pal.Mix(bm.FloorA, bm.FloorB, 0.5f);
        fmean = Pal.Mix(fmean, Pal.RGBA(6, 9, 13), 0.16f);   // same low base as DrawBoard's floor
        fmean = Pal.Mix(fmean, tint, 0.58f);                 // stronger biome-hue pull than the floor
        Color hiA = Lift(Pal.Mix(fmean, bm.FloorA, 0.30f), 30);
        Color hiB = Lift(Pal.Mix(fmean, bm.FloorB, 0.30f), 30);
        // per-biome warm key endpoint (matches DrawBoard's litCol) so the key light warms plateau
        // tops toward the biome hue the same way it warms the floor — not a universal white.
        Color litCol = Pal.Mix(Pal.RGBA(255, 250, 236), tint, 0.30f);
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
                // UNDERTOW W7 — same board key light on the plateau top so raised ground reads as
                // a lit surface consistent with the floor/cover (VALUE only; colorblind-safe).
                // SIGNAL W3: the warm endpoint is the biome-tinted litCol (see above), not white.
                float plit = FloorLight(g, x, y);
                Color topBase = ((x + y) & 1) == 0 ? ca : cb;
                Raylib.DrawRectangleRec(top, plit >= 0f
                    ? Pal.Mix(topBase, litCol, plit * 0.14f)
                    : Pal.Mix(topBase, Pal.RGBA(3, 5, 9), -plit * 0.14f));
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
                // emissive rim: a narrow bright inner accent — the 5.2 bloom will catch this on
                // hardware. HORIZON W5: dimmed (was 0.55/0.40) so the front edge still reads the
                // height tier but sits below the (lowered) bloom knee — cover/terrain never floods.
                Raylib.DrawLineEx(new Vector2(top.X + 1, top.Y + top.Height - 2),
                                  new Vector2(top.X + top.Width - 1, top.Y + top.Height - 2),
                                  1f, Raylib.Fade(Pal.RGBA(200, 230, 255), h >= 2 ? 0.34f : 0.24f));
                // top-edge highlight where it meets a lower tile above
                if (g.Grid.HeightAt(x, y - 1) < h)
                {
                    Raylib.DrawLineEx(new Vector2(top.X, top.Y),
                                      new Vector2(top.X + top.Width, top.Y),
                                      1.5f, Raylib.Fade(Pal.HighEdge, 0.35f));
                    // emissive rim on the exposed top edge (1px inner) — HORIZON W5 dimmed
                    // (was 0.45/0.30) to keep terrain below the bloom knee.
                    Raylib.DrawLineEx(new Vector2(top.X + 1, top.Y + 1),
                                      new Vector2(top.X + top.Width - 1, top.Y + 1),
                                      1f, Raylib.Fade(Pal.RGBA(200, 230, 255), h >= 2 ? 0.28f : 0.18f));
                }
            }
    }

    static void DrawEvac(Game g)
    {
        if (g.EvacZone.Count == 0) return;
        // the WIN-CONDITION must be the 2nd-most-salient thing on the board: a strong animated
        // pulsing fill + a chevron sweep, not a thin outline. Uses the friendly Good role colour.
        float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 3f);
        // SIGNAL W3: the label anchors to ACTUAL member tiles of the zone's topmost row — the old
        // (minx,miny) bounding-box corner may not be a zone tile on L-shaped beacon-extended zones.
        int minY = int.MaxValue;
        foreach (var (_, y) in g.EvacZone) if (y < minY) minY = y;
        float lxSum = 0f; int lxCnt = 0;
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
            if (y == minY)
            {
                lxSum += r.X + r.Width * 0.5f; lxCnt++;
            }
        }
        // SIGNAL W3: the label sits VERTICALLY CENTRED in the topmost zone row on a dark pill —
        // the old placement (4px below the tile top) was occluded by the translucent top-bar HUD,
        // which fades out over the board's first ~24px (Cfg.OriginY=40 < the bar's 64px fade).
        // The x-anchor is the MEMBER tile nearest the row's mean x (a min/max midpoint can land
        // over non-zone tiles when a forward beacon splits the top row into two clusters).
        {
            float lxMean = lxSum / Math.Max(1, lxCnt), lax = lxMean, best = float.MaxValue;
            foreach (var (x, y) in g.EvacZone)
            {
                if (y != minY) continue;
                float cx0 = Cfg.OriginX + x * Cfg.Tile + Cfg.Tile * 0.5f;
                float d = Math.Abs(cx0 - lxMean);
                if (d < best) { best = d; lax = cx0; }
            }
            float lay = Cfg.OriginY + minY * Cfg.Tile + Cfg.Tile * 0.5f;
            float tw = Raylib.MeasureTextEx(Cfg.Font, "EVAC", 14, 1f).X;
            Raylib.DrawRectangleRounded(new Rectangle(lax - tw / 2f - 8f, lay - 11f, tw + 16f, 22f),
                                        0.5f, 8, Pal.RGBA(9, 13, 18, 210));
            Raylib.DrawTextEx(Cfg.Font, "EVAC", new Vector2((int)(lax - tw / 2f), (int)(lay - 7f)), 14, 1f, Pal.Good);
        }

        // Forward BEACON marker: a raised mast + pulsing broadcast rings on its centre tile, so the
        // player-planted extraction point reads as a distinct, deliberate object (not just more zone).
        if (g.BeaconPlanted)
        {
            var bc = Util.TileCenter(g.BeaconTile.x, g.BeaconTile.y);
            float bp = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 4f);
            // expanding broadcast rings
            for (int ring = 0; ring < 2; ring++)
            {
                float rr = 8f + ((float)Raylib.GetTime() * 22f + ring * 14f) % 26f;
                Raylib.DrawCircleLines((int)bc.X, (int)bc.Y, rr, Raylib.Fade(Pal.Good, 0.4f * (1f - rr / 34f)));
            }
            // the mast + emitter
            Raylib.DrawLineEx(new Vector2(bc.X, bc.Y + 8f), new Vector2(bc.X, bc.Y - 10f), 2.4f, Pal.Good);
            Raylib.DrawCircleV(new Vector2(bc.X, bc.Y - 11f), 3f + 1.5f * bp, Raylib.Fade(Pal.Good, 0.6f + 0.4f * bp));
            Raylib.DrawTextEx(Cfg.Font, "BEACON", new Vector2((int)bc.X - 20, (int)(bc.Y + Cfg.Tile / 2 - 6)), 12, 1f, Pal.Good);
        }
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

        // FUL-3: a row-0 marker label would sit under the top bar — flip it below the tile.
        float tly = r.Y - 13 < 30 ? r.Y + r.Height + 2 : r.Y - 13;
        Raylib.DrawTextEx(Cfg.Font, "TERMINAL", new Vector2((int)c.X - 26, (int)tly), 11, 1f, col);
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

            // FUL-3: a row-0 marker label would sit under the top bar — flip it below the tile.
            float cly = r.Y - 13 < 30 ? r.Y + r.Height + 2 : r.Y - 13;
            Raylib.DrawTextEx(Cfg.Font, blown ? "ARMED" : "CHARGE", new Vector2((int)c.X - 18, (int)cly), 10, 1f, col);
        }
    }

    // W10 INTEL CACHE: a pulsing VipGold DIAMOND on the pickup tile (the asset colour — gold ==
    // "worth walking to", matching the VIP/HVT-exposed read). Blinks urgently once the expiry
    // clock is nearly out, so the routing bet stays honest at a glance.
    static void DrawIntelCache(Game g)
    {
        if (!g.CachePresent) return;
        var r = ElevRect(g, g.CacheX, g.CacheY);
        var c = ElevCenter(g, g.CacheX, g.CacheY);
        float t = (float)Raylib.GetTime();
        bool expiring = g.CacheTurnsLeft <= 2;
        // expiring: a harder, faster blink; fresh: a soft pulse
        float pulse = expiring ? (MathF.Sin(t * 8f) > 0f ? 1f : 0.25f) : 0.5f + 0.5f * MathF.Sin(t * 3f);
        Color col = Pal.VipGold;

        // tile wash + soft radial bloom (2nd-salience, like the terminal/charge sites)
        Raylib.DrawRectangleRec(r, Raylib.Fade(col, 0.10f + 0.10f * pulse));
        Raylib.DrawCircleV(c, 20f, Raylib.Fade(col, 0.05f + 0.06f * pulse));

        // the gold diamond: a filled 4-gon (45-degree square) + a bright core + a thin outline ring
        float rad = 9f + 1.5f * pulse;
        Raylib.DrawPoly(c, 4, rad, 45f, Raylib.Fade(col, 0.85f));
        Raylib.DrawPoly(c, 4, rad * 0.45f, 45f, Pal.RGBA(255, 250, 230));
        Raylib.DrawPolyLinesEx(c, 4, rad + 3f, 45f, 1.6f, Raylib.Fade(col, 0.45f + 0.4f * pulse));

        // label + the remaining-turns clock (the expiry is a promise, so print it)
        // FUL-3: a row-0 label would sit under the top bar — flip it below the tile (the turns
        // clock steps down with it). Placement now avoids row 0, but stay robust to old saves.
        bool flip = r.Y - 13 < 30;
        Raylib.DrawTextEx(Cfg.Font, "INTEL", new Vector2((int)c.X - 15, (int)(flip ? r.Y + r.Height + 2 : r.Y - 13)), 11, 1f, col);
        string tt = $"{g.CacheTurnsLeft}T";
        float tw = Raylib.MeasureTextEx(Cfg.Font, tt, 10, 1f).X;
        Raylib.DrawTextEx(Cfg.Font, tt, new Vector2((int)(c.X - tw / 2), (int)(r.Y + r.Height + (flip ? 15 : 1))), 10, 1f,
                          expiring ? Pal.Foe : Raylib.Fade(col, 0.8f));
    }

    // W10 BOUNTY secondary: a small gold chevron + tag over the marked specialist so the bonus
    // target reads on the board, not just in the top bar. Gold (asset/objective), never red.
    static void DrawBountyMark(Game g)
    {
        if (g.Secondary != SecondaryKind.Bounty || g.BountyTarget == null || !g.BountyTarget.Alive) return;
        var u = g.BountyTarget;
        float t = (float)Raylib.GetTime();
        float bob = 2f * MathF.Sin(t * 4f);
        var p = u.Pos + new Vector2(0, -34 + bob);
        Color col = Pal.VipGold;
        // downward chevron (two strokes) + a tiny diamond above it
        Raylib.DrawLineEx(p + new Vector2(-6, -5), p + new Vector2(0, 1), 2.4f, col);
        Raylib.DrawLineEx(p + new Vector2(6, -5), p + new Vector2(0, 1), 2.4f, col);
        Raylib.DrawPoly(p + new Vector2(0, -10), 4, 3.5f, 45f, Raylib.Fade(col, 0.9f));
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
            if (!e.Alive || !e.HasSiege || e.ChargeTurns <= 0) continue;   // live charge only (W5: HasSiege flag — covers a siege-armed boss too)
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

    // W8 — WARBRINGER banner aura: the Chebyshev BannerRange square around each LIVE, ACTIVE
    // banner-bearer, inside which pods cannot rout and rally faster. Drawn as a SUBTLE amber
    // outline (clamped to the board) + small diamond ticks at the corners — the shape echo of the
    // bearer's diamond ring, so the zone reads back to its source without hue (DESIGN.md 3.H).
    // Deliberately far quieter than the siege zone: it is standing terrain-of-the-fight info, not
    // an "it WILL hit here" warning. Reads live state, so a killed banner's aura clears next frame.
    static void DrawBannerAuras(Game g)
    {
        float t = (float)Raylib.GetTime();
        float pulse = 0.75f + 0.25f * MathF.Sin(t * 2.2f);
        foreach (var e in g.Enemies)
        {
            if (!e.Alive || !e.Active || !e.HasBanner) continue;
            int x0 = Math.Max(0, e.X - Game.BannerRange), x1 = Math.Min(g.Grid.W - 1, e.X + Game.BannerRange);
            int y0 = Math.Max(0, e.Y - Game.BannerRange), y1 = Math.Min(g.Grid.H - 1, e.Y + Game.BannerRange);
            var tl = Util.TileRect(x0, y0);
            var br = Util.TileRect(x1, y1);
            var rect = new Rectangle(tl.X + 2, tl.Y + 2, br.X + br.Width - tl.X - 4, br.Y + br.Height - tl.Y - 4);
            Raylib.DrawRectangleLinesEx(rect, 2.2f, Raylib.Fade(Pal.Suspect, 0.34f * pulse));
            // corner diamonds (the ring-shape echo; rotation 0 = diamond, see the diaRing note)
            foreach (var c in new[] { new Vector2(rect.X, rect.Y), new Vector2(rect.X + rect.Width, rect.Y),
                                      new Vector2(rect.X, rect.Y + rect.Height), new Vector2(rect.X + rect.Width, rect.Y + rect.Height) })
                Raylib.DrawPoly(c, 4, 5.5f, 0f, Raylib.Fade(Pal.Suspect, 0.60f * pulse));
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
    // COUNTERPLAY: the player's FOCUSED overwatch braced cones — a friendly gold kill-lane wash over the
    // tiles a focused watcher actually covers (mirrors the reaction gate: range + LoS + InOwCone), plus the
    // two cone-edge rays from the soldier so the "braced this way" read is unmistakable.
    static void DrawFocusCones(Game g)
    {
        if (g.Phase != Phase.PlayerTurn) return;
        System.Collections.Generic.List<Unit> watchers = null;
        foreach (var p in g.Players)
            if (p.Alive && p.OnOverwatch && p.OwFocused && p.Ammo > 0)
                (watchers ??= new System.Collections.Generic.List<Unit>()).Add(p);
        if (watchers == null) return;

        float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 3.0f);
        // SIGNAL W3: wash lifted 0.06-0.11 -> 0.14-0.21 — the old floor was below llvmpipe/monitor
        // perceptibility over a dark biome floor. Deliberately a tier ABOVE the enemy threat wash
        // (DrawOverwatchThreat runs 0.07-0.12): a friendly braced lane is a plan the player made
        // and must read at a glance, while staying under the objective/selection signal tier.
        Color wash = Raylib.Fade(Pal.VipGold, 0.14f + 0.07f * pulse);
        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
            {
                if (!g.Grid.IsFloor(x, y)) continue;
                foreach (var w in watchers)
                {
                    if (Util.TileDist(w.X, w.Y, x, y) > w.Weapon.MaxRange) continue;
                    bool commanding = g.Grid.HeightAt(w.X, w.Y) - g.Grid.HeightAt(x, y) >= 2;
                    if (!g.Grid.HasLineOfSight(w.X, w.Y, x, y, commanding)) continue;
                    if (!g.InOwCone(w, x, y)) continue;
                    Raylib.DrawRectangleRec(ElevRect(g, x, y), wash);
                    break;
                }
            }
        // cone-edge rays + a braced direction chevron at the soldier
        foreach (var w in watchers)
            DrawConeRays(g, w, Pal.VipGold, pulse);
    }

    /// FUL-8: the cone-edge rays + direction chevron for a FOCUSED watcher — factored out of
    /// DrawFocusCones so the enemy PIKEMAN's foe-red lane draws the SAME vocabulary as the player's
    /// gold brace (it IS the same verb) and the two reads can never drift.
    static void DrawConeRays(Game g, Unit w, Color baseCol, float pulse)
    {
        float hlift = g.Grid.IsHigh(w.X, w.Y) ? ElevLift : 0f;
        var c = w.Pos - new Vector2(0, hlift);
        float ang = MathF.Atan2(w.OwDirY, w.OwDirX);
        float len = w.Weapon.MaxRange * Cfg.Tile;
        Color edge = Raylib.Fade(baseCol, 0.30f + 0.15f * pulse);
        for (int s = -1; s <= 1; s += 2)
        {
            float a = ang + s * 0.7853982f;   // +-45 degrees
            Raylib.DrawLineEx(c, c + new Vector2(MathF.Cos(a) * len, MathF.Sin(a) * len), 2f, edge);
        }
        // SIGNAL W3: a short double chevron just past the figure, pointing down the cone axis,
        // so "braced THIS way" reads at the soldier even when the edge rays run off-board.
        var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        var cperp = new Vector2(-dir.Y, dir.X);
        Color chev = Raylib.Fade(baseCol, 0.65f + 0.25f * pulse);
        for (int i = 0; i < 2; i++)
        {
            var tip = c + dir * (38f + i * 9f);
            Raylib.DrawLineEx(tip - dir * 8f + cperp * 7f, tip, 2.4f, chev);
            Raylib.DrawLineEx(tip, tip - dir * 8f - cperp * 7f, 2.4f, chev);
        }
    }

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
                    // FUL-8 truth gate: a FOCUSED enemy watcher (the PIKEMAN's plant) only reacts
                    // inside its cone — the red wash must mirror the OnUnitEnteredTile gate exactly,
                    // or the board lies about where walking is safe.
                    if (w.OwFocused && !g.InOwCone(w, x, y)) continue;
                    var r = ElevRect(g, x, y);
                    Raylib.DrawRectangleRec(r, wash);
                    break;   // one wash per tile is enough; overlap is conveyed by adjacency
                }
            }

        // FUL-8 PIKEMAN: a braced+focused enemy watcher shows its cone edges + chevron in foe-red
        // over the wash — the same lane vocabulary as the player's own BRACE, because it IS the
        // player's own BRACE pointed back at the squad.
        foreach (var w in watchers)
            if (w.OwBrace && w.OwFocused) DrawConeRays(g, w, Pal.Foe, pulse);

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

        // W6 Task 3: detect a NEW telegraph (unit changed) and stamp the entrance-pop start. `pop`
        // is 1 at entry and eases to 0 over PopDur — the reticle uses it for a scale + brightness
        // punch so the eye SNAPS to the mark the instant the enemy beat begins.
        if (!ReferenceEquals(e, _lastIntentUnit)) { _lastIntentUnit = e; _intentPopStart = t; }
        float popT = Util.Clamp((t - _intentPopStart) / PopDur, 0f, 1f);
        float pop = 1f - Util.EaseOutQuad(popT);           // 1 -> 0 over PopDur (fast in, settles)

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
            Raylib.DrawLineEx(from, tc, 1.8f, Raylib.Fade(danger, 0.28f));                 // faint continuous underlay
            DashedLine(from, tc, 3.0f, Raylib.Fade(danger, 0.95f), (t * 34f) % 14f, 8f, 6f); // flowing dashed beam
            // W6 Task 3 — BOLD reticle so the mark snaps out during the enemy beat. Bigger base
            // radius + a heavier crisp ring + brighter X, plus an ENTRANCE POP: on appear the ring
            // flares out ~10px and the whole mark brightens, then eases back to the settled size.
            // Four crosshair tick-marks (cardinal) added for a clear "locked on target" read that
            // survives the colorblind palette (shape, not just hue).
            float rr = (reach + 3.5f) + 2.5f * pulse + 12f * pop;                          // pop expands the ring
            float ptA = 0.35f * pop;                                                        // extra brightness on entry
            Raylib.DrawRing(tc, rr + 2.5f, rr + 6f + 4f * pop, 0, 360, 40, Raylib.Fade(danger, (0.20f + 0.16f * pulse) + ptA)); // wide glow
            Raylib.DrawRing(tc, rr, rr + 3.2f, 0, 360, 40, Raylib.Fade(danger, 1.0f));      // heavy crisp ring
            float k = rr * 0.62f;
            Raylib.DrawLineEx(new Vector2(tc.X - k, tc.Y - k), new Vector2(tc.X + k, tc.Y + k), 3.2f, Raylib.Fade(danger, 1.0f));
            Raylib.DrawLineEx(new Vector2(tc.X - k, tc.Y + k), new Vector2(tc.X + k, tc.Y - k), 3.2f, Raylib.Fade(danger, 1.0f));
            // cardinal crosshair ticks just outside the ring (a target-lock bracket)
            for (int c = 0; c < 4; c++)
            {
                float aa = c * (MathF.PI / 2f);
                var dd = new Vector2(MathF.Cos(aa), MathF.Sin(aa));
                Raylib.DrawLineEx(tc + dd * (rr + 3f), tc + dd * (rr + 8f + 4f * pop), 2.6f, Raylib.Fade(danger, 0.9f + 0.1f * pop));
            }
            Raylib.DrawCircleV(tc, 3.0f + 2f * pop, Raylib.Fade(Pal.RGBA(255, 225, 225), 1.0f));   // bright centre dot
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
        // UNDERTOW W7 — the HORIZON W5 pass over-receded cover into near-invisibility (stacked
        // ~0.24 wall / ~0.36 top mixes toward black). Cover is a real tactics-readability need:
        // it must read as a CLEAR, distinct solid — quiet, but never lost against the floor.
        // Dial the recede WAY back (walls 0.24->0.10, tops 0.36->0.14) so the blocks read as
        // grounded volumes, then let the rim/edge light (below) do the "pop", and keep the
        // squint hierarchy with units by NOT letting the top faces cross the bloom knee.
        Color shade = Pal.RGBA(8, 11, 15);
        Color cHi = Pal.Mix(Pal.Mix(Pal.CoverHi, tint, 0.28f),    shade, 0.10f);
        Color cHiTop = Pal.Mix(Pal.Mix(Pal.CoverHiTop, tint, 0.28f), shade, 0.14f);   // top face reads clearly (was 0.36)
        Color cLo = Pal.Mix(Pal.Mix(Pal.CoverLo, tint, 0.28f),    shade, 0.10f);
        Color cLoTop = Pal.Mix(Pal.Mix(Pal.CoverLoTop, tint, 0.28f), shade, 0.14f);   // top face reads clearly (was 0.36)
        for (int x = 0; x < g.Grid.W; x++)
            for (int y = 0; y < g.Grid.H; y++)
            {
                var t = g.Grid.Tiles[x, y];
                if (t == TileType.Floor) continue;
                var r = Util.TileRect(x, y);
                // UNDERTOW W7 — ground the block: a soft AO pool under the cover's footprint,
                // drawn on the FLOOR (before the lift) so the block reads as sitting IN the room,
                // not floating over a flat plane. Cheap (2 rounded rects), deterministic.
                {
                    var foot = Util.TileRect(x, y);
                    foot.Y -= g.Grid.HeightAt(x, y) * ElevLift;
                    Raylib.DrawRectangleRounded(
                        new Rectangle(foot.X + 3, foot.Y + foot.Height - 12, foot.Width - 6, 14),
                        0.6f, 6, Raylib.Fade(Pal.RGBA(0, 0, 0), 0.20f));
                }
                r.Y -= g.Grid.HeightAt(x, y) * ElevLift;   // sit cover on the plateau top (per tier)
                bool high = t == TileType.HighCover;
                // per-tile key light [-.55,1]: cover on the lit side reads a touch brighter, far
                // corner blocks sink — so the 3D forms pop consistently with the floor gradient.
                float klit = FloorLight(g, x, y);
                float inset = 5f;
                float lift = high ? 16f : 8f;
                var baseRect = new Rectangle(r.X + inset, r.Y + inset + lift,
                                             r.Width - inset * 2, r.Height - inset * 2 - lift);
                var topRect = new Rectangle(r.X + inset, r.Y + inset,
                                            r.Width - inset * 2, r.Height - inset * 2 - lift);
                // apply the key light to the wall + top faces (VALUE only — survives colorblind).
                Color wallCol = KeyLit(high ? cHi : cLo, klit, 0.13f);
                Color topCol  = KeyLit(high ? cHiTop : cLoTop, klit, 0.16f);
                // drop shadow
                Raylib.DrawRectangleRounded(
                    new Rectangle(baseRect.X + 3, baseRect.Y + 4, baseRect.Width, baseRect.Height),
                    0.18f, 5, Raylib.Fade(Pal.RGBA(0, 0, 0), 0.35f));
                Raylib.DrawRectangleRounded(baseRect, 0.18f, 5, wallCol);
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
                Raylib.DrawRectangleRounded(topRect, 0.22f, 5, topCol);
                // 5.4: noise grain on the top face so cover reads as a physical object
                DrawNoiseRect(topRect, tint, 0.10f);
                // top edge highlight — a clear (but quiet) catch on the light-facing upper edge so
                // the top face reads as a distinct lit plane. UNDERTOW W7: with cover no longer
                // over-receded, this can lift back toward a legible whisper (0.035 -> 0.06).
                Raylib.DrawLineEx(new Vector2(topRect.X + 4, topRect.Y + 2),
                                  new Vector2(topRect.X + topRect.Width - 4, topRect.Y + 2),
                                  1.5f, Raylib.Fade(Pal.RGBA(255, 255, 255), 0.06f));
                // structural rim — a narrow bright accent on the top's light edge (upper + left)
                // so the 3D form pops. UNDERTOW W7: restored toward the pre-W5 catch (0.13/0.10)
                // now that cover reads as a solid — still tuned to sit just UNDER the bloom knee
                // (~0.36 luma) so cover never floods; only units/objectives cross it.
                Color rimCol = Pal.Mix(Pal.HighEdge, Pal.RGBA(255, 255, 255), 0.45f);
                float rimA = high ? 0.13f : 0.10f;
                Raylib.DrawLineEx(new Vector2(topRect.X + 5, topRect.Y + 3),
                                  new Vector2(topRect.X + topRect.Width - 5, topRect.Y + 3),
                                  1f, Raylib.Fade(rimCol, rimA));
                // left vertical rim on the top face — completes the "lit from upper-left" read.
                Raylib.DrawLineEx(new Vector2(topRect.X + 3, topRect.Y + 4),
                                  new Vector2(topRect.X + 3, topRect.Y + topRect.Height - 4),
                                  1f, Raylib.Fade(rimCol, rimA * 0.7f));
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
        // SIGNAL W3 (review): status chips draw AFTER every figure — an opaque chip pill on a
        // bottom unit must never be buried under a vertically-adjacent body drawn later in list
        // order; decision-critical state outranks silhouettes.
        foreach (var u in g.Enemies) DrawUnitStatusChips(g, u);
        foreach (var u in g.Players) DrawUnitStatusChips(g, u);
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
            case "STRIKER":            // a LEAPER (WRAITH): a slim forward dart with two BACKWARD motion-
                                       // streaks (a blur of speed), so it reads as "fast repositioner",
                                       // distinct from the HUNTER dart (plain) and HOUND (fanged maw).
                Wedge(9f, -3f, 3.5f, 1f);                        // a sharp forward dart body
                Raylib.DrawLineEx(At(-3f,  2.5f), At(-10f,  4.5f), 1.6f * s, Raylib.Fade(c, a * 0.8f));  // trailing streak
                Raylib.DrawLineEx(At(-3f, -2.5f), At(-10f, -4.5f), 1.6f * s, Raylib.Fade(c, a * 0.8f));  // trailing streak
                Raylib.DrawLineEx(At(-3f,  0f),   At( -8f,  0f),   1.4f * s, Raylib.Fade(c, a * 0.5f));  // faint center wake
                break;
            case "SCREENER":           // a ZONER (HAZE): a compact CANISTER body + a small forward EMITTER
                                       // nozzle venting a puff of three haze dots — reads as "smoke/gas
                                       // dispenser", distinct from the SPOTTER's antenna + the MEDIC circle.
                Raylib.DrawRectanglePro(new Rectangle(p.X, p.Y, 8f * s, 10f * s), new Vector2(4f * s, 5f * s),
                                        MathF.Atan2(fdir.Y, fdir.X) * 180f / MathF.PI, col);   // upright canister
                Raylib.DrawLineEx(At(3f, 0), At(7f, 0), 2.2f * s, col);                        // emitter nozzle
                Raylib.DrawCircleV(At(9.5f,  0.5f), 2.0f * s, Raylib.Fade(c, a * 0.55f));      // venting haze puff
                Raylib.DrawCircleV(At(11.5f, 2.5f), 1.6f * s, Raylib.Fade(c, a * 0.40f));
                Raylib.DrawCircleV(At(11.5f, -2f),  1.4f * s, Raylib.Fade(c, a * 0.30f));
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
            case "WARBRINGER":         // a STANDARD-BEARER (SIGNIFER, W8): a compact body gripping a
                                       // tall banner pole flying a triangular pennant — reads as
                                       // "carries the standard", nothing like the SPOTTER's dish mast.
            {
                Raylib.DrawCircleV(At(-2f, 0), 4.5f * s, col);                    // bearer body
                var pole = At(-2f, 0);
                var poleTop = pole + new Vector2(0, -13f * s);
                Raylib.DrawLineEx(pole, poleTop, 2f * s, col);                    // the standard pole
                FillTri(poleTop, poleTop + new Vector2(9f * s, 2.5f * s),
                        poleTop + new Vector2(0, 5f * s), col);                   // the pennant
                Raylib.DrawCircleV(poleTop, 1.7f * s, col);                       // finial
                break;
            }
            case "CUSTODIAN":          // an OBJECTIVE KEEPER (SEXTON, W8): a PADLOCK — squat lock
                                       // body under a shackle arc, keyhole punched out — reads as
                                       // "re-locks your progress" at a glance (shape-only meaning).
            {
                Raylib.DrawRectanglePro(new Rectangle(p.X, p.Y + 2.5f * s, 11f * s, 8f * s),
                                        new Vector2(5.5f * s, 4f * s), 0f, col);  // lock body
                Raylib.DrawRing(p + new Vector2(0, -1.5f * s), 3f * s, 5f * s, 180f, 360f, 12, col); // shackle
                Raylib.DrawCircleV(p + new Vector2(0, 2.5f * s), 1.6f * s,
                                   Raylib.Fade(Pal.RGBA(8, 10, 14), a));          // keyhole
                break;
            }
            case "PIKEMAN":            // a LANE-HOLDER (SARISSA, FUL-8): a squat braced body under a LONG
                                       // pike set diagonally up the lane, with a crossbar (lugs) near the
                                       // base — at squint: "a line pointing down a lane". Distinct from
                                       // the LANCER's level spear + shoulder bar (its pike is RAISED).
            {
                Raylib.DrawRectanglePro(new Rectangle(p.X, p.Y, 10f * s, 7f * s), new Vector2(5f * s, 3.5f * s),
                                        MathF.Atan2(fdir.Y, fdir.X) * 180f / MathF.PI, col);   // squat planted body
                var butt = At(-4f, 0);
                var tip  = At(13f, 0) + new Vector2(0, -8f * s);   // the pike, angled up-forward
                Raylib.DrawLineEx(butt, tip, 2.2f * s, col);
                Raylib.DrawCircleV(tip, 1.8f * s, col);            // pike head
                var pdir  = Vector2.Normalize(tip - butt);
                var pperp = new Vector2(-pdir.Y, pdir.X);
                var lug   = butt + pdir * (9f * s);                // crossbar lugs across the shaft
                Raylib.DrawLineEx(lug + pperp * (3.5f * s), lug - pperp * (3.5f * s), 2f * s, col);
                break;
            }
            default:                   // fallback: a neutral pentagon
                Raylib.DrawPoly(p, 5, 7.5f * s, 0f, col);
                break;
        }
    }

    /// CODEX (W6) preview glyph: draw a class/archetype silhouette in isolation (bestiary / class
    /// cards) — presentation only, reuses the exact same DrawSilhouette shapes the board uses so the
    /// codex art can never drift from the in-game art. `cls` is the archetype string (e.g. "SNIPER"),
    /// `ang` the facing in radians (default east). No Unit/board state is touched.
    // W12 review: one cached stub instead of a fresh Unit (+6 backing Lists) per call — the glyph
    // now runs per-frame in the roster chips (x6), so the allocation was hot-path. DrawSilhouette
    // reads ONLY u.Cls, and rendering is single-threaded, so per-call reassignment is safe.
    static readonly Unit _codexGlyphStub = new Unit();
    public static void DrawCodexGlyph(string cls, Vector2 p, Color c, float scale, float ang = 0f)
    {
        _codexGlyphStub.Cls = cls;
        DrawSilhouette(_codexGlyphStub, p, c, 1f, scale, ang);
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

    // combat status effects (3.5): stacked chips below the figure — W5: dropped to clear the
    // bigger body. SIGNAL W3: decision-critical codes grew 10 -> 13px and sit on a dark pill
    // backing (with a faint status-coloured rim) so BRN/BLD/STN/DAZ read at play distance over
    // any biome floor or overlay wash; the chip row centres under the figure. Runs as a LATE
    // pass from DrawUnits, recomputing DrawUnit's base anchor (the per-frame GetTime() drift
    // between the two computations is sub-pixel).
    static void DrawUnitStatusChips(Game g, Unit u)
    {
        if (!u.Alive || (u.Statuses.Count == 0 && !u.Downed)) return;
        float hlift = g.Grid.IsHigh(u.X, u.Y) ? ElevLift : 0f;
        bool drone = u.Team == Team.Enemy && u.Cls == "DRONE";
        float hover = drone ? 11f + MathF.Sin((float)Raylib.GetTime() * 3f + u.Bob) * 2f : 0f;
        float bob = MathF.Sin((float)Raylib.GetTime() * 2.2f + u.Bob) * 1.6f;
        Vector2 p = u.Pos - new Vector2(0, hlift) + new Vector2(0, bob - hover) + u.Recoil;
        const float chipH = 18f;
        // FUL-7: the DOWN countdown pill leads the row — red "DOWN 3/2/1" while the timer runs,
        // amber "STABLE" once frozen (Pal.Foe/Pal.Suspect: both palette-safe; the glyph carries
        // the state without hue per DESIGN 3.H — a falling chevron vs a level bar).
        string downCode = u.Downed ? (u.Stabilized ? "STABLE" : $"DOWN {u.DownedTurns}") : null;
        Color downCol = u.Stabilized ? Pal.Suspect : Pal.Foe;
        float rowW = 0f;
        if (downCode != null)
            rowW += 17f + Raylib.MeasureTextEx(Cfg.Font, downCode, 13, 1f).X + 8f + 3f;
        foreach (var s in u.Statuses)
            if (s.Turns > 0)
                rowW += 17f + Raylib.MeasureTextEx(Cfg.Font, StatusDef.Code(s.Kind), 13, 1f).X + 8f + 3f;
        if (rowW <= 0f) return;
        float cxs = p.X - (rowW - 3f) / 2f;
        float cys = p.Y + 24f;
        if (downCode != null)
        {
            float tw0 = Raylib.MeasureTextEx(Cfg.Font, downCode, 13, 1f).X;
            float w0 = 17f + tw0 + 8f;
            Raylib.DrawRectangleRounded(new Rectangle(cxs - 1f, cys - 1f, w0 + 2f, chipH + 2f),
                                        0.5f, 6, Raylib.Fade(downCol, 0.55f));
            Raylib.DrawRectangleRounded(new Rectangle(cxs, cys, w0, chipH), 0.5f, 6, Pal.RGBA(9, 13, 18, 216));
            float gx = cxs + 9f, gy = cys + chipH * 0.5f;
            if (u.Stabilized)
            {   // level bar = the bleeding stopped, state held
                Raylib.DrawLineEx(new Vector2(gx - 4f, gy), new Vector2(gx + 4f, gy), 2f, downCol);
                Raylib.DrawLineEx(new Vector2(gx - 1f, gy - 3f), new Vector2(gx + 1f, gy - 3f), 2f, downCol);
            }
            else
            {   // falling chevron = going down, clock running
                Raylib.DrawLineEx(new Vector2(gx - 4f, gy - 3f), new Vector2(gx, gy + 3f), 2f, downCol);
                Raylib.DrawLineEx(new Vector2(gx + 4f, gy - 3f), new Vector2(gx, gy + 3f), 2f, downCol);
            }
            Raylib.DrawTextEx(Cfg.Font, downCode, new Vector2((int)(cxs + 17f), (int)(cys + 2f)), 13, 1f, downCol);
            cxs += w0 + 3f;
        }
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
            string code = StatusDef.Code(s.Kind);
            float tw = Raylib.MeasureTextEx(Cfg.Font, code, 13, 1f).X;
            float w = 17f + tw + 8f;
            // faint coloured rim = a slightly larger rounded rect UNDER the dark pill
            // (DrawRectangleRoundedLines is version-volatile — never use it)
            Raylib.DrawRectangleRounded(new Rectangle(cxs - 1f, cys - 1f, w + 2f, chipH + 2f),
                                        0.5f, 6, Raylib.Fade(sc, 0.40f));
            Raylib.DrawRectangleRounded(new Rectangle(cxs, cys, w, chipH), 0.5f, 6, Pal.RGBA(9, 13, 18, 216));
            // 5.5: the shape glyph so the effect reads without relying on hue or the code text
            DrawStatusGlyph(s.Kind, cxs + 9f, cys + chipH * 0.5f, sc);
            Raylib.DrawTextEx(Cfg.Font, code, new Vector2((int)(cxs + 17f), (int)(cys + 2f)), 13, 1f, sc);
            cxs += w + 3f;
        }
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
        // W6 Task 2: Unaware pod body shifted from a muddy warm brown to a COLD DESATURATED SLATE
        // (matches the new slate under-ring + "?" glyph) so a dormant contact reads as a quiet,
        // neutral "sleeping threat" — distinct from the amber SUSPICIOUS body and the hot LIVE red.
        Color main = vip ? Pal.VipGold : (friend ? Pal.Friend : (unaware ? Pal.RGBA(150, 164, 180) : (suspicious ? Pal.Suspect : (elite ? Pal.Elite : Pal.Foe))));
        Color dark = vip ? Pal.VipDk  : (friend ? Pal.FriendDk : (unaware ? Pal.RGBA(44, 52, 62) : (suspicious ? Pal.SuspectDk : (elite ? Pal.EliteDk : Pal.FoeDk))));

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
        // FUL-3: dormant contacts kept full body mass and read as equal-weight tokens next to
        // live combatants — shrink them (unaware 0.75x, suspicious 0.85x); information kept,
        // emphasis cut. Alert state, not scale, carries the threat signal.
        if (unaware) bodyScale *= 0.75f; else if (suspicious) bodyScale *= 0.85f;
        // FUL-7: a DOWNED soldier reads PRONE at a squint — the FUL-3 dormant-scale vocabulary
        // pushed further (0.6x) with the figure sunk to the ground (no upright silhouette);
        // the pulsing red ground ring below carries the danger signal in both palettes.
        bool downed = friend && u.Downed;
        if (downed) { bodyScale *= 0.6f; p.Y += 9f; }

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
        // overlay. Inactive pods get a SEPARATE, subordinate slate under-glow below (Task 2).
        if (!inactive)
        {
            // hotter, more saturated glow colour for enemies so live foes burn red; the friendly
            // glow rides the cyan team colour. Boss/HVT pick up a touch more reach.
            Color glow = vip ? Pal.VipGold
                       : friend ? Pal.Friend
                       : (elite ? Pal.Elite : Pal.Foe);
            float gpulse = 0.85f + 0.15f * MathF.Sin((float)Raylib.GetTime() * 2.4f + u.Bob);
            float gA = (friend ? 0.32f : 0.36f) * figAlpha * gpulse;   // enemies a hair hotter (W5: nudged up)
            float gR = (elite || hvt) ? 37f : 31f;                     // W5: haloes the enlarged body
            // a wide soft bloom + a tighter brighter core radial (two rings read as a glow on llvmpipe)
            Raylib.DrawCircleV(p, gR,        Raylib.Fade(glow, gA * 0.45f));
            Raylib.DrawCircleV(p, gR * 0.68f, Raylib.Fade(glow, gA * 0.85f));
        }
        else
        {
            // W6 Task 2 — a DORMANT/SUSPICIOUS pod is REAL, planned-around information (perfect-info
            // pillar), but with no under-glow it vanished into the dark board as a muddy brown blob.
            // Give it a DESATURATED SLATE under-ring so "sleeping threat here" reads at a glance —
            // deliberately DIM + de-saturated (a cold slate, NOT the hot-red live-foe halo) and
            // clearly subordinate: much lower alpha/reach than a live foe's burn. Suspicious pods get
            // a faint warm bias (their amber ring/ ! carries the tier); Unaware stays cold slate.
            Color podGlow = suspicious ? Pal.RGBA(150, 120, 92) : Pal.RGBA(96, 108, 124);
            float pR = suspicious ? 26f : 22f;   // FUL-3: dormant ring tightens with the smaller body
            Raylib.DrawCircleV(p, pR,        Raylib.Fade(podGlow, 0.16f));   // soft seat so it doesn't vanish
            Raylib.DrawCircleV(p, pR * 0.66f, Raylib.Fade(podGlow, 0.24f));
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

        // FUL-7: a DOWNED soldier's pulsing red ground ring (the role-ring vocabulary in the
        // danger colour — Pal.Foe survives the colorblind palette) so the prone body reads at
        // a squint: "a soldier is on the ground HERE, and the clock is running."
        if (downed)
        {
            float dpls = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 3.4f);
            var dcr = foot + new Vector2(0, 17);
            Raylib.DrawRing(dcr, 21f, 25f, 0, 360, 48, Raylib.Fade(Pal.Foe, 0.30f + 0.35f * dpls));
            Raylib.DrawRing(dcr, 16.5f, 18.5f, 0, 360, 48, Raylib.Fade(Pal.Foe, 0.16f + 0.16f * dpls));
        }

        // FUL-11 CEREMONY — the FINALE BOSS reads as the apex of the force from the ground up:
        // a broad slow-pulsing champion aura + a heavy double ring in the ELITE role colour (the
        // W3 role-ring vocabulary scaled to "champion" — same hue as the elite body ring, so no
        // new colour job). Drawn BEFORE the gold HVT mark so goal-gold still sits on top, and in
        // every alert state (the capstone must read even while the pod sleeps). Presentation only.
        if (u.Team == Team.Enemy && u.IsBoss)
        {
            float bp = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 2.2f + u.Bob);
            var bc = foot + new Vector2(0, 17);
            Raylib.DrawRing(bc, 30f, 41f, 0, 360, 56, Raylib.Fade(Pal.Elite, 0.09f + 0.07f * bp));   // soft aura wash
            Raylib.DrawRing(bc, 33f, 36.5f, 0, 360, 56, Raylib.Fade(Pal.Elite, 0.40f + 0.25f * bp)); // heavy outer ring
            Raylib.DrawRing(bc, 29.5f, 31f, 0, 360, 56, Raylib.Fade(Pal.Elite, 0.28f + 0.14f * bp)); // inner hairline
        }

        // DECAPITATE marker: a bold gold double HVT ring on the ground so the target reads out of
        // the pack at a glance (full-alpha signal, drawn regardless of alert state / dimming).
        if (hvt)
        {
            float pulse = 0.55f + 0.45f * MathF.Sin((float)Raylib.GetTime() * 4.2f + u.Bob);
            var hc = Pal.VipGold;
            Raylib.DrawRing(foot + new Vector2(0, 17), 20f, 24f, 0, 360, 48, Raylib.Fade(hc, 0.85f));
            Raylib.DrawRing(foot + new Vector2(0, 17), 26f, 28f, 0, 360, 48, Raylib.Fade(hc, 0.30f + 0.40f * pulse));

            // GUARDED telegraph (W4): while the HVT is protected, ring it in a pulsing SHIELD aura
            // (danger colour) + draw a faint link line to each living in-range guard so the player can
            // read "kill these to expose it." No gotcha — the bodyguard relationship is fully visible.
            if (u.HvtGuarded)
            {
                float gp = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 5.5f);
                // a domed shield arc above the HVT (reads as a protective bubble)
                Raylib.DrawRing(p, 22f, 26f, 200, 340, 28, Raylib.Fade(Pal.Foe, 0.35f + 0.45f * gp));
                Raylib.DrawRing(p, 26f, 28.5f, 200, 340, 28, Raylib.Fade(Pal.Foe, 0.18f + 0.18f * gp));
                foreach (var gd in g.Enemies)
                {
                    if (gd == null || !gd.Alive || !gd.IsHvtGuard) continue;
                    if (Util.ChebyDist(gd.X, gd.Y, u.X, u.Y) > Combat.HvtGuardRange) continue;
                    Raylib.DrawLineEx(p, gd.Pos, 1.8f, Raylib.Fade(Pal.Foe, 0.38f + 0.28f * gp));   // guard link
                    // W6 Task 3 — a BOLDER downward guard caret: "kill me to peel the screen". Bigger,
                    // heavier, pulsing + a thin dark backing stroke so it reads against the dark board.
                    Vector2 mk = gd.Pos + new Vector2(0, -42f);
                    float cw = 8f, ch = 6f, thk = 3.0f;
                    Raylib.DrawLineEx(mk + new Vector2(-cw, -ch - 1f), mk + new Vector2(0, 1f), thk + 1.4f, Raylib.Fade(Pal.RGBA(20, 4, 4), 0.8f)); // shadow
                    Raylib.DrawLineEx(mk + new Vector2(cw, -ch - 1f), mk + new Vector2(0, 1f), thk + 1.4f, Raylib.Fade(Pal.RGBA(20, 4, 4), 0.8f));
                    Raylib.DrawLineEx(mk + new Vector2(-cw, -ch), mk, thk, Raylib.Fade(Pal.Foe, 0.9f + 0.1f * gp));
                    Raylib.DrawLineEx(mk + new Vector2(cw, -ch), mk, thk, Raylib.Fade(Pal.Foe, 0.9f + 0.1f * gp));
                }
            }
        }

        // body — apply figAlpha to the figure shape (bodyScale gives a brief flinch pop).
        // HORIZON W5 — units DOMINATE their tile: bodyR 18.5 -> 24 and the class silhouette
        // scaled up ~1.6x (fig) so the per-class SHAPE reads as the body OUTLINE at play
        // distance, not a tiny inner detail. Paired with the receded cover (DrawCover/
        // DrawElevation), this restores the squint hierarchy: units > objectives > enemies >
        // cover. A slightly heavier, brighter rim makes the saturated outline read at a glance.
        float fig = 1.85f;                                  // overall silhouette upscale (was 1.16)
        float bodyR = 24f * bodyScale;                      // grown body disc (was 18.5)
        // SIGNAL W3 — the body RING carries the enemy ROLE as a shape (colour stays team-only:
        // colorblind-safe shape redundancy per DESIGN.md 3.H): SENTRY/TURRET = an axis-aligned
        // SQUARE ring (an emplacement, bolted down); OGRE/BRUISER = a heavy HEXAGON ring (an
        // armoured brute — echoes its hex silhouette); STALKER/SCOUT = a SMALLER DASHED ring
        // (light, fast, evasive). Everyone else keeps the circle so the special shapes stay rare
        // and meaningful, and the role reads at any zoom from the ring alone.
        bool sqRing   = u.Team == Team.Enemy && u.Cls == "TURRET";
        bool hexRing  = u.Team == Team.Enemy && u.Cls == "BRUISER";
        bool dashRing = u.Team == Team.Enemy && u.Cls == "SCOUT";
        // W8: banner-bearer (WARBRINGER, or a bannered boss) = a DIAMOND ring — the anchor that
        // holds pods steady. Keyed on the capability flag like the W5 mechanics; echoed by the
        // aura outline's corner diamonds so ring and zone read as one system without hue.
        bool diaRing  = u.Team == Team.Enemy && u.HasBanner;
        if (sqRing)
        {
            float hs = bodyR - 2f;                          // half-side: matches the disc footprint
            Raylib.DrawRectangleRec(new Rectangle(p.X - hs, p.Y - hs, hs * 2f, hs * 2f), Raylib.Fade(dark, figAlpha));
            Raylib.DrawRectangleLinesEx(new Rectangle(p.X - hs, p.Y - hs, hs * 2f, hs * 2f), 4f, Raylib.Fade(main, figAlpha));
            Raylib.DrawRectangleRec(new Rectangle(p.X - hs + 4f, p.Y - hs + 4f, hs * 2f - 8f, hs * 2f - 8f),
                                    Raylib.Fade(main, 0.22f * figAlpha));
        }
        else if (hexRing)
        {
            Raylib.DrawPoly(p, 6, bodyR + 2.5f, 0f, Raylib.Fade(dark, figAlpha));
            Raylib.DrawPolyLinesEx(p, 6, bodyR + 2.5f, 0f, 4.4f, Raylib.Fade(main, figAlpha));
            Raylib.DrawPoly(p, 6, bodyR - 2.5f, 0f, Raylib.Fade(main, 0.22f * figAlpha));
        }
        else if (dashRing)
        {
            float dr = bodyR * 0.86f;                       // smaller: a light, fast frame
            Raylib.DrawCircleV(p, dr, Raylib.Fade(dark, figAlpha));
            for (int k = 0; k < 8; k++)                     // dashed ring: 8 arcs with clear gaps
                Raylib.DrawRing(p, dr - 3.4f, dr + 1f, k * 45f + 5f, k * 45f + 33f, 10, Raylib.Fade(main, figAlpha));
            Raylib.DrawCircleV(p, dr - 3.4f, Raylib.Fade(main, 0.22f * figAlpha));
        }
        else if (diaRing)
        {
            // diamond ring: DrawPoly's 4-gon puts its FIRST vertex at rotation° along +X, so
            // rotation 0 IS the diamond (45 would render the TURRET's axis-aligned square —
            // learned from the screenshot). The "anchor" frame around the standard-bearer.
            Raylib.DrawPoly(p, 4, bodyR + 4f, 0f, Raylib.Fade(dark, figAlpha));
            Raylib.DrawPolyLinesEx(p, 4, bodyR + 4f, 0f, 4.4f, Raylib.Fade(main, figAlpha));
            Raylib.DrawPoly(p, 4, bodyR - 2f, 0f, Raylib.Fade(main, 0.22f * figAlpha));
        }
        else
        {
            Raylib.DrawCircleV(p, bodyR, Raylib.Fade(dark, figAlpha));
            Raylib.DrawRing(p, bodyR - 3.4f, bodyR + 1f, 0, 360, 48, Raylib.Fade(main, figAlpha));
            Raylib.DrawCircleV(p, bodyR - 3.4f, Raylib.Fade(main, 0.22f * figAlpha));
        }

        // class silhouette — a recognizable primitive cue per class (shape-redundant, colorblind-
        // safe: meaning rides on the SHAPE, inheriting the team colour + focal figAlpha).
        if (elite) Raylib.DrawRing(p, 26.5f * bodyScale, 29.5f * bodyScale, 0, 360, 48, Raylib.Fade(Pal.Elite, 0.55f * figAlpha));
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
                // (W5: sized to sit around the enlarged body). W6: crisper amber ring (double band)
                // + a shadow-backed "!" so the SUSPICIOUS tier snaps out — but still below a live foe.
                float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 6f);
                Raylib.DrawRing(p, 24f, 28f, 0, 360, 44, Raylib.Fade(Pal.Suspect, 0.42f + 0.48f * pulse));
                Raylib.DrawRing(p, 28f, 29.5f, 0, 360, 44, Raylib.Fade(Pal.Suspect, 0.18f + 0.20f * pulse));
                var qp = new Vector2((int)(p.X - 2), (int)(p.Y - 42));
                Raylib.DrawTextEx(Cfg.Font, "!", qp + new Vector2(1.2f, 1.2f), 22, 1f, Raylib.Fade(Pal.RGBA(8, 6, 2), 0.85f)); // drop shadow for contrast
                Raylib.DrawTextEx(Cfg.Font, "!", qp, 22, 1f, Pal.Suspect);
            }
            else
            {
                // Unaware pod: a DELIBERATE clean dashed ring + a clear "?" — dim but unambiguous
                // (reads as "dormant contact here", not a muddy brown blob). Dashes = "not yet live".
                // W6: brighter, slightly heavier desaturated-slate dashed ring + a shadow-backed "?"
                // so the DORMANT tier reads at a glance against any biome floor — still cold/quiet,
                // still clearly subordinate to the amber SUSPICIOUS ring and the hot-red LIVE halo.
                float t = (float)Raylib.GetTime();
                Color dim = Pal.RGBA(176, 190, 205);   // cold desaturated slate (was warm brown)
                for (int k = 0; k < 8; k++)
                {
                    float a0 = k * 45f + t * 14f;          // slow rotation so it reads as "scanning"
                    Raylib.DrawRing(p, 23f, 26f, a0, a0 + 26f, 6, Raylib.Fade(dim, 0.68f));
                }
                float qw = Raylib.MeasureTextEx(Cfg.Font, "?", 23, 1f).X;
                var qp = new Vector2((int)(p.X - qw / 2), (int)(p.Y - 13));
                Raylib.DrawTextEx(Cfg.Font, "?", qp + new Vector2(1.2f, 1.2f), 23, 1f, Raylib.Fade(Pal.RGBA(6, 8, 12), 0.85f)); // drop shadow
                Raylib.DrawTextEx(Cfg.Font, "?", qp, 23, 1f, Raylib.Fade(dim, 1.0f));
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
        // W5: pushed past the enlarged (24px) body so the facing tick reads beyond the silhouette
        Raylib.DrawLineEx(p + fdir * 20f, p + fdir * 28f, 3f, Raylib.Fade(main, figAlpha));
        if (g.Selected == u && friend && !vip)
        {
            // a soft directional aim chevron a little further out, pointing at the target
            var tip = p + fdir * 34f;
            Raylib.DrawLineEx(tip, tip - fdir * 6f + perp * 5f, 2f, Raylib.Fade(main, 0.85f * figAlpha));
            Raylib.DrawLineEx(tip, tip - fdir * 6f - perp * 5f, 2f, Raylib.Fade(main, 0.85f * figAlpha));
        }
        else if (u.Team == Team.Enemy)
        {
            // tiny arrowhead on the barrel so enemy facing is unmistakable
            var tip = p + fdir * 28f;
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

        // shield: a thick barrier arc on the barred (facing) side (W5: on the enlarged body edge)
        // SIGNAL W5: HasShieldArc flag (mirrors Cls=="SHIELD") — a shield-arc boss draws its arc too.
        if (u.Team == Team.Enemy && u.HasShieldArc && (u.ShieldDx != 0 || u.ShieldDy != 0))
        {
            float ang = MathF.Atan2(u.ShieldDy, u.ShieldDx) * 180f / MathF.PI;
            Raylib.DrawRing(p, 24f, 28f, ang - 55, ang + 55, 28, Raylib.Fade(Pal.RGBA(150, 200, 240), figAlpha));
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
        // SIGNAL W5: HasSiege flag (mirrors Cls=="BOMBARD") — a siege-armed boss pulses too.
        if (u.Team == Team.Enemy && u.HasSiege && u.ChargeTurns > 0)
        {
            float ct = (float)Raylib.GetTime();
            float cp = 0.5f + 0.5f * MathF.Sin(ct * 7f);
            Raylib.DrawCircleV(p, (3.5f + 2.5f * cp), Raylib.Fade(Pal.RGBA(255, 180, 120), 0.55f + 0.35f * cp));
            Raylib.DrawRing(p, 10f, 12f, 0, 360, 28, Raylib.Fade(Pal.Foe, 0.35f + 0.45f * cp));
        }

        // damage flash — always at full strength (it's momentary feedback). Grown with the
        // W5 body so the whole figure whites-out on a hit (reads as a solid jolt).
        if (u.Flash > 0.01f)
            Raylib.DrawCircleV(p, 22f, Raylib.Fade(Pal.RGBA(255, 255, 255), u.Flash * 0.8f));

        // HORIZON W5 — EXPOSED-BY-FIRE on-board marker (the on-board half of the W1 telegraph):
        // a unit that FIRED this turn but still has an action banked is sitting where it fired,
        // exposed, and could reposition — firing-and-staying-put is punishable, so telegraph it.
        // A small pulsing team-coloured downward chevron above the figure (full-alpha SIGNAL,
        // shape-distinct from the OW dot / stance tags / status glyphs). Uses the exact W1 exposure
        // condition (FiredThisTurn && !MovedAfterFire): the cue clears the instant the unit ducks
        // to a new tile after firing. Never shown on dormant/suspicious pods.
        if (u.FiredThisTurn && !u.MovedAfterFire && !inactive)
        {
            float ep = 0.55f + 0.45f * MathF.Sin((float)Raylib.GetTime() * 5.2f + u.Bob);
            Color ec = friend ? Pal.Friend : (elite ? Pal.Elite : Pal.Foe);
            float cx = p.X + 15f, cyt = p.Y - 15f;          // upper-right of the figure
            // an open downward chevron (▽ outline) — "pinned in the open" cue
            Raylib.DrawLineEx(new Vector2(cx - 5f, cyt - 3.5f), new Vector2(cx, cyt + 3f), 2f, Raylib.Fade(ec, 0.55f + 0.4f * ep));
            Raylib.DrawLineEx(new Vector2(cx, cyt + 3f), new Vector2(cx + 5f, cyt - 3.5f), 2f, Raylib.Fade(ec, 0.55f + 0.4f * ep));
            // a small emissive dot at the vertex so it reads even when squinting
            Raylib.DrawCircleV(new Vector2(cx, cyt + 3.5f), 1.6f, Raylib.Fade(ec, 0.7f + 0.3f * ep));
        }

        // hp pips
        // FUL-7: the HP bar hides while DOWNED — a 0-HP bar under a countdown pill would lie
        // twice (the pill row below owns the read: DOWN n / STABLE).
        if (!u.Downed) DrawHpPips(u, p);

        // status icons — W5: lifted to clear the enlarged (24px) body + the raised HP pips.
        float ix = p.X - 10, iy = p.Y - 35;
        if (u.OnOverwatch)
        {
            // UNDERTOW W2 — a BRACED watcher reads distinctly (green "BRC") from a lethal watch (accent "OW"):
            // it disrupts rather than kills, so its badge shouldn't imply a kill-lane.
            Color owc = u.OwBrace ? Pal.Good : Pal.Accent;
            Raylib.DrawCircle((int)p.X, (int)(p.Y - 34), 6f, Raylib.Fade(owc, 0.25f));
            Raylib.DrawTextEx(Cfg.Font, u.OwBrace ? "BRC" : "OW", new Vector2((int)(p.X - (u.OwBrace ? 11 : 9)), (int)(p.Y - 39)), 10, 1f, owc);
        }
        if (u.Hunkered)
            Raylib.DrawPoly(new Vector2(p.X, p.Y - 35), 4, 6f, 45f, Pal.Good);

        // active ability stance tag (friendly) / suppression tag (enemy) — pushed out past the wider body
        if (u.RunGun) Raylib.DrawTextEx(Cfg.Font, "R&G", new Vector2((int)(p.X + 18), (int)(p.Y - 34)), 11, 1f, Pal.Accent);
        else if (u.Blitz) Raylib.DrawTextEx(Cfg.Font, "BLZ", new Vector2((int)(p.X + 18), (int)(p.Y - 34)), 11, 1f, Pal.Accent);
        else if (u.Steady) Raylib.DrawTextEx(Cfg.Font, "AIM", new Vector2((int)(p.X + 18), (int)(p.Y - 34)), 11, 1f, Pal.Good);
        if (u.Team == Team.Enemy && u.Suppress > 0)
            Raylib.DrawTextEx(Cfg.Font, "SUPP", new Vector2((int)(p.X + 17), (int)(p.Y - 34)), 11, 1f, Pal.Foe);
        // UNDERTOW W3 — a ROUTED (broken) enemy reads clearly: it's fleeing + shooting wild, so the
        // player knows this threat is temporarily neutralized (the earned comeback beat).
        if (u.Team == Team.Enemy && u.Routed > 0)
            Raylib.DrawTextEx(Cfg.Font, "ROUT", new Vector2((int)(p.X + 17), (int)(p.Y - 34)), 11, 1f, Pal.Good);
        // SIGNAL W8 — WAVERING: this pod is ONE KILL from breaking (Game.PodWavering — banner-held
        // members are excluded so the mark never lies). Amber "WVR" + a jagged CRACK glyph on the
        // figure's left (mutually exclusive with ROUT by definition; shape carries the meaning
        // without hue per DESIGN.md 3.H, same doctrine as DrawStatusGlyph). The comeback lever,
        // telegraphed: the player can PLAN the breaking kill instead of being surprised by it.
        else if (u.Team == Team.Enemy && g.PodWavering(u))
        {
            Raylib.DrawTextEx(Cfg.Font, "WVR", new Vector2((int)(p.X - 39), (int)(p.Y - 39)), 11, 1f, Pal.Suspect);
            DrawCrackGlyph(new Vector2(p.X - 46f, p.Y - 33f), Pal.Suspect);
        }

        // combat status effects: drawn in DrawUnitStatusChips as a LATE pass over all figures
        // (SIGNAL W3 review) — an opaque chip pill must never be buried under an adjacent body.

        // elite boss name / rage tag (uses the unit's actual name so mid-bosses read right).
        // W5: a FRENZIED (second rage tier) breaker outranks the plain ENRAGED tag.
        if (elite)
        {
            string tag = u.Frenzied ? u.Name + " FRENZIED" : (u.Enraged ? u.Name + " ENRAGED" : u.Name);
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
        float y = p.Y - 30f;   // W5: lifted to clear the enlarged (24px) body disc
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
            if (!e.Alive || Util.ChebyDist(u.X, u.Y, e.X, e.Y) > g.GrappleReachFor(u)) continue;   // JUGGERNAUT: reach 1
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
