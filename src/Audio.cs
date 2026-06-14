using System;
using System.Collections.Generic;
using System.IO;
using Raylib_cs;

namespace Sightline;

enum Wv { Sine, Square, Saw, Tri }

/// Fully procedural SFX — no asset files. Each effect is synthesised into a
/// 16-bit PCM WAV in memory and loaded as a Raylib Sound. Safe on machines
/// with no audio device (everything becomes a no-op).
public static class Audio
{
    const int SR = 44100;
    static bool _ready;
    public static bool Enabled = true;
    static readonly Dictionary<string, Sound> _snd = new();

    public static void Init()
    {
        try
        {
            Raylib.InitAudioDevice();
            _ready = Raylib.IsAudioDeviceReady();
        }
        catch { _ready = false; }
        if (!_ready) return;
        Raylib.SetMasterVolume(0.6f);

        Add("select", 0.08f, b => Tone(b, 520, 0, 0.06f, Wv.Square, 0.30f));
        Add("move",   0.07f, b => Tone(b, 300, 0, 0.05f, Wv.Tri, 0.22f));
        Add("reload", 0.13f, b => { Tone(b, 700, 0, 0.05f, Wv.Square, 0.25f); Tone(b, 480, 0.06f, 0.05f, Wv.Square, 0.20f); });
        Add("hunker", 0.16f, b => Tone(b, 260, 0, 0.13f, Wv.Tri, 0.30f));
        Add("shoot",  0.16f, b => { Noise(b, 0, 0.12f, 0.45f); Tone(b, 165, 0, 0.09f, Wv.Saw, 0.32f, 80); });
        Add("hit",    0.18f, b => { Noise(b, 0, 0.15f, 0.50f); Tone(b, 120, 0, 0.12f, Wv.Square, 0.40f, 70); });
        Add("crit",   0.24f, b => { Noise(b, 0, 0.20f, 0.55f); Tone(b, 220, 0, 0.18f, Wv.Saw, 0.42f, 420); });
        Add("miss",   0.13f, b => Tone(b, 880, 0, 0.10f, Wv.Sine, 0.22f, 1500));
        Add("over",   0.18f, b => { Tone(b, 440, 0, 0.08f, Wv.Square, 0.28f); Tone(b, 660, 0.07f, 0.09f, Wv.Square, 0.24f); });
        Add("death",  0.34f, b => { Tone(b, 200, 0, 0.30f, Wv.Saw, 0.38f, 55); Noise(b, 0, 0.28f, 0.32f); });
        Add("turn",   0.28f, b => { Tone(b, 330, 0, 0.12f, Wv.Tri, 0.30f); Tone(b, 495, 0.10f, 0.14f, Wv.Tri, 0.26f); });
        Add("win",    0.70f, b => { float[] n = { 523, 659, 784, 1046 }; for (int i = 0; i < 4; i++) Tone(b, n[i], i * 0.11f, 0.20f, Wv.Tri, 0.30f); });
        Add("lose",   0.80f, b => { float[] n = { 392, 330, 262, 196 }; for (int i = 0; i < 4; i++) Tone(b, n[i], i * 0.13f, 0.24f, Wv.Saw, 0.28f); });
    }

    public static void Play(string id)
    {
        if (!_ready || !Enabled) return;
        if (_snd.TryGetValue(id, out var s)) Raylib.PlaySound(s);
    }

    public static void ToggleMute() { Enabled = !Enabled; }

    public static void Shutdown()
    {
        if (!_ready) return;
        foreach (var s in _snd.Values) Raylib.UnloadSound(s);
        _snd.Clear();
        Raylib.CloseAudioDevice();
    }

    // ---------------- synthesis ----------------
    static void Add(string id, float dur, Action<float[]> fill)
    {
        var buf = new float[(int)(dur * SR) + 8];
        fill(buf);
        // normalise to avoid clipping
        float peak = 0.0001f;
        for (int i = 0; i < buf.Length; i++) peak = MathF.Max(peak, MathF.Abs(buf[i]));
        float g = peak > 1f ? 1f / peak : 1f;
        byte[] wav = EncodeWav(buf, g);
        Wave w = Raylib.LoadWaveFromMemory(".wav", wav);
        _snd[id] = Raylib.LoadSoundFromWave(w);
        Raylib.UnloadWave(w);
    }

    static void Tone(float[] b, float freq, float start, float dur, Wv type, float vol, float slideTo = 0)
    {
        int n0 = (int)(start * SR);
        int len = (int)(dur * SR);
        int atk = Math.Max(1, (int)(0.004f * SR));
        float ph = 0f;
        for (int i = 0; i < len; i++)
        {
            int idx = n0 + i;
            if (idx >= b.Length) break;
            float t = i / (float)len;
            float f = slideTo > 0 ? Util.Lerp(freq, slideTo, t) : freq;
            ph += 2f * MathF.PI * f / SR;
            float env = MathF.Min(1f, i / (float)atk) * MathF.Exp(-3.5f * t);
            b[idx] += Shape(type, ph) * vol * env;
        }
    }

    static void Noise(float[] b, float start, float dur, float vol)
    {
        int n0 = (int)(start * SR);
        int len = (int)(dur * SR);
        for (int i = 0; i < len; i++)
        {
            int idx = n0 + i;
            if (idx >= b.Length) break;
            float t = i / (float)len;
            float env = MathF.Exp(-4.5f * t);
            b[idx] += (Util.RandF() * 2f - 1f) * vol * env;
        }
    }

    static float Shape(Wv type, float ph)
    {
        float p = ph % (2f * MathF.PI);
        if (p < 0) p += 2f * MathF.PI;
        switch (type)
        {
            case Wv.Square: return MathF.Sin(p) >= 0 ? 1f : -1f;
            case Wv.Saw: return p / MathF.PI - 1f;
            case Wv.Tri: return 2f / MathF.PI * MathF.Asin(MathF.Sin(p));
            default: return MathF.Sin(p);
        }
    }

    static byte[] EncodeWav(float[] samples, float gain)
    {
        int n = samples.Length;
        int dataLen = n * 2;
        using var ms = new MemoryStream(44 + dataLen);
        using var w = new BinaryWriter(ms);
        void Str(string s) { foreach (char c in s) w.Write((byte)c); }
        Str("RIFF"); w.Write(36 + dataLen); Str("WAVE");
        Str("fmt "); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(SR); w.Write(SR * 2); w.Write((short)2); w.Write((short)16);
        Str("data"); w.Write(dataLen);
        for (int i = 0; i < n; i++)
        {
            float v = Util.Clamp(samples[i] * gain, -1f, 1f);
            w.Write((short)(v * 32767f));
        }
        w.Flush();
        return ms.ToArray();
    }
}
