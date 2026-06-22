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

/// Particles, floating combat text, and screen shake.
public class Fx
{
    public List<Particle> Particles = new();
    public List<FloatText> Texts = new();

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
        for (int i = 0; i < 10; i++)
        {
            float spread = Util.RandRange(-0.4f, 0.4f);
            var d = Rotate(dir, spread);
            Particles.Add(new Particle
            {
                Pos = at + dir * 14f,
                Vel = d * Util.RandRange(180f, 420f),
                Life = Util.RandRange(0.08f, 0.22f),
                MaxLife = 0.22f,
                Size = Util.RandRange(2f, 4.5f),
                Drag = 7f,
                Color = col,
                Spark = true,
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
        foreach (var p in Particles)
        {
            float k = Util.Clamp(p.Life / p.MaxLife, 0f, 1f);
            var c = Raylib.Fade(p.Color, k);
            if (p.Spark)
            {
                Vector2 tail = p.Pos - Vector2.Normalize(p.Vel + new Vector2(0.001f, 0)) * p.Size * 2.5f;
                Raylib.DrawLineEx(tail, p.Pos, MathF.Max(1f, p.Size * 0.6f), c);
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
