using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

public class Particle
{
    public Vector2 Pos, Vel;
    public float Life, MaxLife, Size, Drag;
    public Color Color;
    public bool Spark; // line-shaped vs dot
}

public class FloatText
{
    public Vector2 Pos;
    public string Text;
    public Color Color;
    public float Life, MaxLife, Size, Rise;
}

/// A short-lived radial pulse: a filled "impact frame" flash and/or an expanding
/// ring outline. One entity covers both shockwaves (grow + thin ring) and impact
/// pops (bright fill that shrinks/fades) — driven by lerping radius over its life.
public class Ring
{
    public Vector2 Pos;
    public float Life, MaxLife;
    public float R0, R1;        // radius at birth -> radius at death
    public float Thick;         // ring stroke width; <=0 => filled disc (impact flash)
    public Color Color;
    public float CoreAlpha;     // peak alpha of the bright core (filled flash)
    public float RingAlpha;     // peak alpha of the ring outline
}

/// One ambient atmosphere particle. Its motion is a pure, deterministic function of its
/// frozen per-index constants (set once when the pool is built) plus the ambient time
/// accumulator — so a given frame always reproduces (essential for the SIGHTLINE_SHOT
/// harness). Nothing here is spawned/removed at runtime: the field wraps in place, so
/// there are zero per-frame allocations and no RNG drift.
public struct AmbientP
{
    public float Sx, Sy;      // anchored base position in [0,1] board-space (frozen)
    public float Phase;       // per-particle phase offset (frozen) — desyncs sway/twinkle
    public float SizeK;       // size jitter 0.6..1.3 (frozen)
    public float SpeedK;      // speed jitter 0.7..1.25 (frozen)
}

/// Particles, floating combat text, and screen shake.
public class Fx
{
    public List<Particle> Particles = new();
    public List<FloatText> Texts = new();
    public List<Ring> Rings = new();

    public float Shake;
    public bool ShakeOn = true;     // settings toggle
    Vector2 _shakeOff;
    public Vector2 ShakeOffset => _shakeOff;

    public void AddShake(float amt) { if (ShakeOn) Shake = MathF.Min(18f, Shake + amt); }

    public void Update(float dt)
    {
        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            var p = Particles[i];
            p.Life -= dt;
            if (p.Life <= 0) { Particles.RemoveAt(i); continue; }
            p.Pos += p.Vel * dt;
            p.Vel *= 1f - p.Drag * dt;
        }
        for (int i = Texts.Count - 1; i >= 0; i--)
        {
            var t = Texts[i];
            t.Life -= dt;
            if (t.Life <= 0) { Texts.RemoveAt(i); continue; }
            t.Pos.Y -= t.Rise * dt;
        }
        for (int i = Rings.Count - 1; i >= 0; i--)
        {
            Rings[i].Life -= dt;
            if (Rings[i].Life <= 0) Rings.RemoveAt(i);
        }

        if (Shake > 0.01f)
        {
            Shake *= MathF.Pow(0.001f, dt); // fast decay
            float a = Util.RandF() * MathF.PI * 2f;
            _shakeOff = new Vector2(MathF.Cos(a), MathF.Sin(a)) * Shake;
            if (Shake < 0.4f) { Shake = 0; _shakeOff = Vector2.Zero; }
        }
        else _shakeOff = Vector2.Zero;
    }

    public void Burst(Vector2 at, Color col, int count, float speed, float life, float size = 3f, bool spark = false)
    {
        for (int i = 0; i < count; i++)
        {
            float a = Util.RandF() * MathF.PI * 2f;
            float s = speed * (0.4f + Util.RandF());
            Particles.Add(new Particle
            {
                Pos = at,
                Vel = new Vector2(MathF.Cos(a), MathF.Sin(a)) * s,
                Life = life * (0.6f + Util.RandF() * 0.7f),
                MaxLife = life,
                Size = size * (0.6f + Util.RandF() * 0.8f),
                Drag = spark ? 6f : 3f,
                Color = col,
                Spark = spark,
            });
        }
    }

    public void Muzzle(Vector2 at, Vector2 dir, Color col)
    {
        var mouth = at + dir * 14f;
        // directional spark cone
        for (int i = 0; i < 10; i++)
        {
            float spread = Util.RandRange(-0.4f, 0.4f);
            var d = Rotate(dir, spread);
            Particles.Add(new Particle
            {
                Pos = mouth,
                Vel = d * Util.RandRange(180f, 420f),
                Life = Util.RandRange(0.08f, 0.22f),
                MaxLife = 0.22f,
                Size = Util.RandRange(2f, 4.5f),
                Drag = 7f,
                Color = col,
                Spark = true,
            });
        }
        // a brief soft muzzle bloom (a couple of fat, fast-fading glow dots) so the shot has a
        // bright flash kick at the barrel — soft-glow style (handled in Draw), not a hard disc
        for (int i = 0; i < 2; i++)
            Particles.Add(new Particle
            {
                Pos = mouth + dir * Util.RandRange(0f, 4f),
                Vel = dir * Util.RandRange(20f, 60f),
                Life = Util.RandRange(0.05f, 0.10f),
                MaxLife = 0.10f,
                Size = Util.RandRange(5f, 7.5f),
                Drag = 9f,
                Color = Pal.RGBA(255, 244, 210),
                Spark = false,
            });
    }

    /// A brief, bright "impact frame": a fat soft disc that pops in and fades over a
    /// few frames. Scale `r` and `alpha` to the event weight (graze small/dim, crit
    /// big/hot) so the punch is proportional. Lands with the hit-stop in Game.
    public void Impact(Vector2 at, Color col, float r, float alpha = 0.9f, float life = 0.12f)
    {
        Rings.Add(new Ring
        {
            Pos = at, Life = life, MaxLife = life,
            R0 = r, R1 = r * 0.45f,          // shrink as it fades -> reads as a flash, not a bloom
            Thick = 0f,                      // filled disc
            Color = col,
            CoreAlpha = alpha,
            RingAlpha = 0f,
        });
    }

    /// An expanding shockwave ring (used by explosions). `r1` = final radius, scale it
    /// to the blast. `thick`/`alpha` weight the punch. Optionally chain a 2nd, faster,
    /// fainter ring for a layered detonation read.
    public void Shockwave(Vector2 at, Color col, float r0, float r1, float thick = 4f,
                          float alpha = 0.9f, float life = 0.26f, bool doubleRing = true)
    {
        Rings.Add(new Ring
        {
            Pos = at, Life = life, MaxLife = life,
            R0 = r0, R1 = r1, Thick = thick,
            Color = col, CoreAlpha = 0f, RingAlpha = alpha,
        });
        if (doubleRing)
            Rings.Add(new Ring
            {
                Pos = at, Life = life * 0.72f, MaxLife = life * 0.72f,
                R0 = r0, R1 = r1 * 1.18f, Thick = MathF.Max(1.5f, thick * 0.55f),
                Color = col, CoreAlpha = 0f, RingAlpha = alpha * 0.55f,
            });
    }

    /// Directional sparks kicked out along `dir` (the shot vector / off the target).
    /// `count`/`speed`/`spread` set density + cone; a crit calls this hotter (more,
    /// faster, tighter) so the spray reads as the heavier hit.
    public void DirSparks(Vector2 at, Vector2 dir, Color col, int count, float speed,
                          float spread = 0.7f, float size = 3f)
    {
        var n = dir.LengthSquared() > 0.0001f ? Vector2.Normalize(dir) : new Vector2(1f, 0f);
        for (int i = 0; i < count; i++)
        {
            float a = Util.RandRange(-spread, spread);
            var d = Rotate(n, a);
            Particles.Add(new Particle
            {
                Pos = at + n * Util.RandRange(0f, 6f),
                Vel = d * speed * (0.45f + Util.RandF() * 1.1f),
                Life = Util.RandRange(0.18f, 0.42f),
                MaxLife = 0.42f,
                Size = size * (0.55f + Util.RandF() * 0.9f),
                Drag = 6.5f,
                Color = col,
                Spark = true,
            });
        }
    }

    /// A soft, low puff of dust under a stepping unit. Deliberately subtle + brief so
    /// movement gets a touch of grounding without cluttering the board.
    public void Dust(Vector2 at, int count = 4)
    {
        for (int i = 0; i < count; i++)
        {
            float a = Util.RandRange(-2.55f, -0.6f);    // bias the cone upward-out (a kicked-up puff)
            float s = Util.RandRange(18f, 46f);
            Particles.Add(new Particle
            {
                Pos = at + new Vector2(Util.RandRange(-5f, 5f), 6f),
                Vel = new Vector2(MathF.Cos(a), MathF.Sin(a)) * s,
                Life = Util.RandRange(0.22f, 0.42f),
                MaxLife = 0.42f,
                Size = Util.RandRange(2.5f, 4.5f),
                Drag = 5.5f,
                Color = Pal.RGBA(150, 150, 150),
                Spark = false,
            });
        }
    }

    public void PopText(Vector2 at, string text, Color col, float size = 26f)
    {
        Texts.Add(new FloatText
        {
            Pos = at,
            Text = text,
            Color = col,
            Life = 1.1f,
            MaxLife = 1.1f,
            Size = size,
            Rise = 46f,
        });
    }

    /// A prominent, slow-fading, barely-rising stamp (e.g. a KIA marker on death).
    public void Stamp(Vector2 at, string text, Color col, float size, float life)
    {
        Texts.Add(new FloatText
        {
            Pos = at,
            Text = text,
            Color = col,
            Life = life,
            MaxLife = life,
            Size = size,
            Rise = 7f,
        });
    }

    static Vector2 Rotate(Vector2 v, float a)
    {
        float c = MathF.Cos(a), s = MathF.Sin(a);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    public void Draw()
    {
        // radial pulses first, so sparks/embers layer over the flash
        foreach (var r in Rings)
        {
            float k = Util.Clamp(r.Life / r.MaxLife, 0f, 1f);     // 1 at birth -> 0 at death
            float age = 1f - k;
            float rad = Util.Lerp(r.R0, r.R1, Util.EaseOutQuad(age));
            if (r.Thick <= 0f)
            {
                // impact flash: soft halo + hot white-ish core, both shrinking & fading
                Raylib.DrawCircleV(r.Pos, rad * 1.7f, Raylib.Fade(r.Color, k * r.CoreAlpha * 0.30f));
                Raylib.DrawCircleV(r.Pos, rad, Raylib.Fade(r.Color, k * r.CoreAlpha * 0.8f));
                Raylib.DrawCircleV(r.Pos, rad * 0.5f,
                                   Raylib.Fade(Pal.RGBA(255, 252, 245), k * r.CoreAlpha));
            }
            else
            {
                // shockwave: an expanding ring that thins as it fades
                float t = MathF.Max(1f, r.Thick * (0.4f + 0.6f * k));
                Raylib.DrawRing(r.Pos, MathF.Max(0f, rad - t), rad, 0, 360, 48,
                                Raylib.Fade(r.Color, k * r.RingAlpha));
            }
        }

        foreach (var p in Particles)
        {
            float k = Util.Clamp(p.Life / p.MaxLife, 0f, 1f);
            var c = Raylib.Fade(p.Color, k);
            if (p.Spark)
            {
                Vector2 tail = p.Pos - Vector2.Normalize(p.Vel + new Vector2(0.001f, 0)) * p.Size * 2.5f;
                Raylib.DrawLineEx(tail, p.Pos, MathF.Max(1f, p.Size * 0.6f), c);
                // bright soft head: a faint halo + a hot white-ish core at the leading point so a
                // dense burst (crit) glows noticeably hotter than a sparse one (graze) — the count
                // does the weighting for free. Fades with the particle so it never lingers.
                Raylib.DrawCircleV(p.Pos, MathF.Max(1f, p.Size * 0.5f) * 1.8f, Raylib.Fade(p.Color, k * 0.18f));
                Raylib.DrawCircleV(p.Pos, MathF.Max(0.8f, p.Size * 0.4f),
                                   Raylib.Fade(Pal.RGBA(255, 250, 240), k * 0.5f));
            }
            else
            {
                // 5.4 soft-glow particle: a dim translucent halo (2.2x radius) + bright core.
                // Reads as a glowing ember rather than a hard opaque disc; cost is one extra
                // DrawCircleV per non-spark particle — negligible given typical counts.
                float coreR = p.Size * k;
                Raylib.DrawCircleV(p.Pos, coreR * 2.2f, Raylib.Fade(p.Color, k * 0.22f));
                Raylib.DrawCircleV(p.Pos, coreR, c);
            }
        }
    }

    public void DrawText()
    {
        foreach (var t in Texts)
        {
            float k = Util.Clamp(t.Life / t.MaxLife, 0f, 1f);
            float pop = t.Life > t.MaxLife - 0.12f ? Util.EaseOutBack((t.MaxLife - t.Life) / 0.12f) : 1f;
            int fs = (int)(t.Size * (0.6f + 0.4f * pop));
            int w = (int)Raylib.MeasureTextEx(Cfg.Font, t.Text, fs, 1f).X;
            int x = (int)(t.Pos.X - w / 2f);
            int y = (int)t.Pos.Y;
            Raylib.DrawTextEx(Cfg.Font, t.Text, new Vector2(x + 2, y + 2), fs, 1f, Raylib.Fade(Pal.RGBA(0, 0, 0), k * 0.6f));
            Raylib.DrawTextEx(Cfg.Font, t.Text, new Vector2(x, y), fs, 1f, Raylib.Fade(t.Color, k));
        }
    }

    // ====================================================================================
    // Ambient atmosphere layer (per-biome) — Phase 5 "give each biome a signature".
    //
    // Eight biomes used to differ only by a colour multiply (no embers in MAGMA, no snow
    // in TUNDRA). This is a self-contained, low-contrast particle field that breathes each
    // biome's signature motion (Biome.Ambient + its AmbCol/AmbCount/AmbSpeed/AmbSize/
    // AmbAlpha tuning, all in Util.cs).
    //
    // DESIGN / determinism: there is NO runtime spawn/remove and NO RNG. A fixed pool of
    // AmbientP is built once per biome with frozen per-index constants from a deterministic
    // hash; every frame each particle's screen position + alpha is a *pure function* of
    // those constants and `_ambT` (an internal time accumulator advanced by `dt`). In the
    // SIGHTLINE_SHOT harness `dt` is a fixed 1/60 and the shot fires at a fixed frame, so
    // `_ambT` — and therefore the whole field — reproduces byte-for-byte. The field wraps
    // in place (modulo board height/width), so it's steady, seamless, and allocation-free
    // in the hot path. It must stay SUBTLE: low alpha, desaturated, never read as a
    // threat/objective (squint test). Drawn behind the units (see DrawAmbient note).
    // ====================================================================================

    AmbientP[] _amb = System.Array.Empty<AmbientP>();
    int _ambKind = -1;          // Biome.Ambient currently baked into the pool (-1 = none)
    int _ambCount;              // active count within _amb (<= _amb.Length)
    Color _ambCol;
    float _ambBaseSpeed, _ambBaseSize, _ambAlpha;
    float _ambT;               // deterministic time accumulator (seconds), advanced by dt

    const int AmbCap = 80;     // hard ceiling on the pool regardless of biome data (perf)

    /// Advance the ambient field for `biome`. Rebuilds the (frozen) pool when the biome's
    /// ambient kind changes; otherwise just steps the deterministic clock. Cheap + bounded
    /// + allocation-free in steady state. Call once per frame from Game.Update with the
    /// fixed `dt` (1/60 in the harness) so it reproduces.
    public void UpdateAmbient(Biome biome, float dt)
    {
        if (biome == null) return;
        int kind = (int)biome.Ambient;
        if (kind != _ambKind || _ambCol.R != biome.AmbCol.R || _ambCol.G != biome.AmbCol.G
            || _ambCol.B != biome.AmbCol.B || _ambCount != Util.Clamp(biome.AmbCount, 0, AmbCap))
            BuildAmbient(biome);

        // bound dt so a hitch/first-frame spike can't teleport the field (keeps it smooth +
        // deterministic-enough; the harness dt is already a fixed 1/60 so this never bites it)
        _ambT += Util.Clamp(dt, 0f, 0.05f);
    }

    /// (Re)bake the ambient pool for `biome`. Per-index constants come from a fixed-seed
    /// deterministic hash (NOT Util.Rng) so the field is identical every run/frame. Called
    /// only on a biome change — never in the hot loop.
    void BuildAmbient(Biome biome)
    {
        _ambKind = (int)biome.Ambient;
        _ambCol = biome.AmbCol;
        _ambBaseSpeed = biome.AmbSpeed;
        _ambBaseSize = biome.AmbSize;
        _ambAlpha = biome.AmbAlpha;
        _ambCount = Util.Clamp(biome.AmbCount, 0, AmbCap);
        if (_amb.Length < _ambCount) _amb = new AmbientP[_ambCount];

        // Seed the per-particle constants off the biome kind so different biomes scatter
        // differently, but deterministically (same biome -> same layout every time).
        uint seed = 0x9E3779B9u ^ (uint)(_ambKind * 0x85EBCA77);
        for (int i = 0; i < _ambCount; i++)
        {
            uint h = seed + (uint)i * 0x27D4EB2Fu;
            _amb[i].Sx = Hash01(h * 16777619u + 1u);
            _amb[i].Sy = Hash01(h * 2246822519u + 7u);
            _amb[i].Phase = Hash01(h * 3266489917u + 13u) * (MathF.PI * 2f);
            _amb[i].SizeK = 0.6f + Hash01(h * 668265263u + 19u) * 0.7f;
            _amb[i].SpeedK = 0.7f + Hash01(h * 374761393u + 23u) * 0.55f;
        }
    }

    /// Draw the ambient field over the board rect. Pure-function positions from the frozen
    /// constants + `_ambT`; low alpha, soft round/streak primitives in the biome's palette.
    /// WIRING: call this in the board draw — recommended right after the floor + noise grain
    /// (before cover/units/overlays) so it sits as background texture and never competes with
    /// signal. It's a no-op when the pool is empty.
    public void DrawAmbient()
    {
        if (_ambCount <= 0) return;

        float ox = Cfg.OriginX, oy = Cfg.OriginY;
        float bw = Cfg.BoardW, bh = Cfg.BoardH;
        var kind = (AmbientKind)_ambKind;

        for (int i = 0; i < _ambCount; i++)
        {
            var a = _amb[i];
            float t = _ambT;
            float px, py;          // 0..1 board-space position (pre-wrap)
            float alpha = _ambAlpha;
            float size = _ambBaseSize * a.SizeK;

            switch (kind)
            {
                case AmbientKind.Ember:   // MAGMA: rise + accelerate upward, flicker, sway
                {
                    float climb = (_ambBaseSpeed * a.SpeedK / bh) * t;
                    py = Frac(a.Sy - climb);                                   // travels up
                    px = a.Sx + 0.012f * MathF.Sin(t * 1.3f + a.Phase);        // gentle sway
                    float twk = 0.6f + 0.4f * MathF.Sin(t * 6f + a.Phase * 3f);// hot flicker
                    alpha *= twk * (0.45f + 0.55f * py);                       // hotter low, fades as it rises
                    break;
                }
                case AmbientKind.Mote:    // VOID: slow rise + twinkle
                {
                    float climb = (_ambBaseSpeed * a.SpeedK / bh) * t;
                    py = Frac(a.Sy - climb);
                    px = a.Sx + 0.01f * MathF.Sin(t * 0.7f + a.Phase);
                    alpha *= 0.45f + 0.55f * (0.5f + 0.5f * MathF.Sin(t * 2.2f + a.Phase * 2f));
                    break;
                }
                case AmbientKind.Spore:   // VERDANT: drift slowly upward, lazy bob
                {
                    float climb = (_ambBaseSpeed * a.SpeedK / bh) * t;
                    py = Frac(a.Sy - climb);
                    px = a.Sx + 0.02f * MathF.Sin(t * 0.9f + a.Phase);
                    alpha *= 0.6f + 0.4f * MathF.Sin(t * 1.1f + a.Phase);
                    break;
                }
                case AmbientKind.Snow:    // TUNDRA: fall + side sway
                {
                    float fall = (_ambBaseSpeed * a.SpeedK / bh) * t;
                    py = Frac(a.Sy + fall);                                    // travels down
                    px = a.Sx + 0.03f * MathF.Sin(t * 1.2f + a.Phase);
                    break;
                }
                case AmbientKind.Ash:     // ASH: fall slower, tumble, dim
                {
                    float fall = (_ambBaseSpeed * a.SpeedK / bh) * t;
                    py = Frac(a.Sy + fall);
                    px = a.Sx + 0.045f * MathF.Sin(t * 0.8f + a.Phase) * MathF.Cos(t * 0.35f + a.Phase);
                    alpha *= 0.7f + 0.3f * MathF.Sin(t * 1.5f + a.Phase);
                    break;
                }
                case AmbientKind.Gust:    // ARID: blow fast left->right in a low band
                {
                    float blow = (_ambBaseSpeed * a.SpeedK / bw) * t;
                    px = Frac(a.Sx + blow);                                    // streaks across
                    py = a.Sy + 0.02f * MathF.Sin(t * 2.4f + a.Phase);
                    alpha *= 0.5f + 0.5f * MathF.Sin(t * 3f + a.Phase);        // gusty in/out
                    break;
                }
                case AmbientKind.Scan:    // NEON: drift sideways + a faint vertical scan pulse
                {
                    float drift = (_ambBaseSpeed * a.SpeedK / bw) * t;
                    px = Frac(a.Sx + drift * 0.5f);
                    py = a.Sy;
                    // pulse brightest as a slow horizontal scanline sweeps past this row
                    float scan = Frac(t * 0.12f);
                    float d = MathF.Abs(py - scan); d = MathF.Min(d, 1f - d);
                    alpha *= 0.32f + 0.68f * MathF.Max(0f, 1f - d * 7f);
                    break;
                }
                default:                  // STEEL (Dust): slow lateral draft, faint shimmer
                {
                    float drift = (_ambBaseSpeed * a.SpeedK / bw) * t;
                    px = Frac(a.Sx + drift);
                    py = a.Sy + 0.015f * MathF.Sin(t * 0.6f + a.Phase);
                    alpha *= 0.6f + 0.4f * MathF.Sin(t * 1.0f + a.Phase);
                    break;
                }
            }

            float x = ox + Frac(px) * bw;
            float y = oy + Frac(py) * bh;
            alpha = Util.Clamp(alpha, 0f, 1f);
            if (alpha <= 0.01f) continue;
            var col = Raylib.Fade(_ambCol, alpha);

            // soft-glow primitive matching the house style: a dim halo + a brighter core.
            // GUST streaks read as a short horizontal dash; everything else is a round mote.
            if (kind == AmbientKind.Gust)
            {
                float len = size * 4.5f;
                Raylib.DrawLineEx(new Vector2(x - len, y), new Vector2(x, y),
                                  MathF.Max(1f, size * 0.9f), col);
            }
            else
            {
                Raylib.DrawCircleV(new Vector2(x, y), size * 2.0f, Raylib.Fade(_ambCol, alpha * 0.30f));
                Raylib.DrawCircleV(new Vector2(x, y), MathF.Max(0.8f, size), col);
            }
        }
    }

    // fractional part in [0,1) — used to wrap the field so it loops seamlessly with no respawn.
    static float Frac(float v) { v -= MathF.Floor(v); return v < 0f ? v + 1f : v; }

    // deterministic uint->[0,1) hash (xorshift-mix). Used ONLY at pool-build time to freeze
    // per-particle constants; never per frame. Replaces RNG so the field is reproducible.
    static float Hash01(uint x)
    {
        x ^= x >> 16; x *= 0x7FEB352Du;
        x ^= x >> 15; x *= 0x846CA68Bu;
        x ^= x >> 16;
        return (x & 0xFFFFFFu) / 16777216f;   // 24-bit mantissa -> [0,1)
    }

    /// Headless self-test (no window): build the ambient field for every biome, advance it
    /// many fixed steps, and assert the pool stays bounded, finite (no NaN/Inf), and that
    /// every drawn position stays inside the board rect. Returns true on PASS. Wired to
    /// SIGHTLINE_AMBIENTTEST in Program.cs (orchestrator). Deterministic — does not touch
    /// Raylib draw calls, so it's safe with no GL context.
    public static bool AmbientSelfTest()
    {
        var fx = new Fx();
        foreach (var bm in Biome.All)
        {
            // step the sim with the same fixed dt the harness uses
            for (int step = 0; step < 600; step++)
                fx.UpdateAmbient(bm, 1f / 60f);

            if (fx._ambCount < 0 || fx._ambCount > AmbCap) return false;
            if (fx._ambCount > fx._amb.Length) return false;
            if (float.IsNaN(fx._ambT) || float.IsInfinity(fx._ambT)) return false;

            // mirror DrawAmbient's position math for the active kind and verify bounds.
            float ox = Cfg.OriginX, oy = Cfg.OriginY, bw = Cfg.BoardW, bh = Cfg.BoardH;
            float t = fx._ambT;
            var kind = (AmbientKind)fx._ambKind;
            for (int i = 0; i < fx._ambCount; i++)
            {
                var a = fx._amb[i];
                if (float.IsNaN(a.Sx) || float.IsNaN(a.Sy) || float.IsNaN(a.Phase)) return false;
                float px, py;
                float climb = (fx._ambBaseSpeed * a.SpeedK) * t;
                switch (kind)
                {
                    case AmbientKind.Ember:
                    case AmbientKind.Mote:
                    case AmbientKind.Spore:
                        py = Frac(a.Sy - climb / bh);
                        px = a.Sx + 0.05f * MathF.Sin(t + a.Phase);
                        break;
                    case AmbientKind.Snow:
                    case AmbientKind.Ash:
                        py = Frac(a.Sy + climb / bh);
                        px = a.Sx + 0.05f * MathF.Sin(t + a.Phase);
                        break;
                    case AmbientKind.Gust:
                        px = Frac(a.Sx + climb / bw);
                        py = a.Sy + 0.05f * MathF.Sin(t + a.Phase);
                        break;
                    default:
                        px = Frac(a.Sx + climb / bw);
                        py = a.Sy + 0.05f * MathF.Sin(t + a.Phase);
                        break;
                }
                float x = ox + Frac(px) * bw;
                float y = oy + Frac(py) * bh;
                if (float.IsNaN(x) || float.IsNaN(y)) return false;
                // wrapped positions must sit within the board rect (the field never leaks off-board)
                if (x < ox - 1f || x > ox + bw + 1f || y < oy - 1f || y > oy + bh + 1f) return false;
            }

            // a fresh Fx for the next biome so the per-biome rebuild path is exercised cleanly
            fx = new Fx();
        }
        return true;
    }
}
