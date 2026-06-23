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
}
