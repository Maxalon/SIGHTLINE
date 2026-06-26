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

    // Recipe registry: id -> (duration, fill). Populated by Recipes() and shared by both
    // the device-load path (Init) and the device-free self-test (BuildBuffer/SelfTest), so
    // the buffer-generation path is validated even where there is no audio device.
    static readonly Dictionary<string, (float dur, Action<float[]> fill)> _recipes = new();

    // procedural music (3.10): a looping ambient bed + a combat layer that ducks in on
    // the enemy turn / when hostiles are active. Crossfaded by volume; mute kills both.
    static Music _ambient, _combat;
    static bool _music;
    static float _ambVol, _combVol, _intensity;
    public static void SetMusicIntensity(float x) => _intensity = Util.Clamp(x, 0f, 1f);

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

        BuildRecipes();
        foreach (var kv in _recipes) LoadRecipe(kv.Key, kv.Value.dur, kv.Value.fill);

        InitMusic();
    }

    /// Register every SFX recipe into _recipes WITHOUT touching the audio device. Called by
    /// Init (which then loads each into a Sound) and by the device-free self-test. Adding a
    /// new effect = one Reg(...) line here.
    static void BuildRecipes()
    {
        if (_recipes.Count > 0) return;   // idempotent (Init or SelfTest, whichever runs first)

        void Reg(string id, float dur, Action<float[]> fill) => _recipes[id] = (dur, fill);

        // --- baseline action cues ---
        Reg("select", 0.08f, b => Tone(b, 520, 0, 0.06f, Wv.Square, 0.30f));
        Reg("move",   0.07f, b => Tone(b, 300, 0, 0.05f, Wv.Tri, 0.22f));
        Reg("reload", 0.13f, b => { Tone(b, 700, 0, 0.05f, Wv.Square, 0.25f); Tone(b, 480, 0.06f, 0.05f, Wv.Square, 0.20f); });
        Reg("hunker", 0.16f, b => Tone(b, 260, 0, 0.13f, Wv.Tri, 0.30f));

        // --- per-weapon firing voices (each short — these fire a LOT) ---
        // "shoot" stays the baseline RIFLE voice (legacy callers + a fallback in PlayWeapon).
        Reg("shoot",   0.16f, FillRifle);
        Reg("w_rifle", 0.16f, FillRifle);
        // SHOTGUN: a low boom + a fat noise burst (a wide, heavy blast).
        Reg("w_shotgun", 0.22f, b => {
            Noise(b, 0, 0.18f, 0.60f);
            Tone(b, 95, 0, 0.16f, Wv.Saw, 0.42f, 55);
            Tone(b, 140, 0, 0.06f, Wv.Square, 0.30f, 70);
        });
        // SNIPER: a sharp high crack + a short ringing tail (a precise, cutting report).
        Reg("w_sniper", 0.26f, b => {
            Noise(b, 0, 0.05f, 0.50f);
            Tone(b, 900, 0, 0.04f, Wv.Square, 0.40f, 320);
            Tone(b, 240, 0.02f, 0.22f, Wv.Saw, 0.30f, 120);   // ringing tail
        });
        // SMG: a short, bright, snappy crack (quick + tight).
        Reg("w_smg", 0.10f, b => {
            Noise(b, 0, 0.07f, 0.38f);
            Tone(b, 320, 0, 0.06f, Wv.Square, 0.30f, 180);
        });
        // LMG: a heavy chug — a thicker low body + a longer rolling noise (a big bore).
        Reg("w_lmg", 0.20f, b => {
            Noise(b, 0, 0.16f, 0.52f);
            Tone(b, 120, 0, 0.14f, Wv.Saw, 0.40f, 60);
            Tone(b, 80, 0, 0.10f, Wv.Square, 0.32f, 50);
        });

        // --- impact cues (crit layered heavier than a normal hit) ---
        Reg("hit",  0.18f, b => { Noise(b, 0, 0.15f, 0.50f); Tone(b, 120, 0, 0.12f, Wv.Square, 0.40f, 70); });
        // crit: the old recipe PLUS a subtle low-end "thud" layer so it lands heavier.
        Reg("crit", 0.28f, b => {
            Noise(b, 0, 0.20f, 0.55f);
            Tone(b, 220, 0, 0.18f, Wv.Saw, 0.42f, 420);
            Tone(b, 70, 0, 0.24f, Wv.Sine, 0.45f, 48);   // sub-bass thud
        });
        Reg("miss", 0.13f, b => Tone(b, 880, 0, 0.10f, Wv.Sine, 0.22f, 1500));
        Reg("over", 0.18f, b => { Tone(b, 440, 0, 0.08f, Wv.Square, 0.28f); Tone(b, 660, 0.07f, 0.09f, Wv.Square, 0.24f); });
        Reg("death", 0.34f, b => { Tone(b, 200, 0, 0.30f, Wv.Saw, 0.38f, 55); Noise(b, 0, 0.28f, 0.32f); });
        Reg("turn",  0.28f, b => { Tone(b, 330, 0, 0.12f, Wv.Tri, 0.30f); Tone(b, 495, 0.10f, 0.14f, Wv.Tri, 0.26f); });
        Reg("win",   0.70f, b => { float[] n = { 523, 659, 784, 1046 }; for (int i = 0; i < 4; i++) Tone(b, n[i], i * 0.11f, 0.20f, Wv.Tri, 0.30f); });
        Reg("lose",  0.80f, b => { float[] n = { 392, 330, 262, 196 }; for (int i = 0; i < 4; i++) Tone(b, n[i], i * 0.13f, 0.24f, Wv.Saw, 0.28f); });

        // --- event stingers (short emphatic phrases; fired via PlayStinger) ---
        // KILL: a quick two-note down-flick + a noise crunch (a confirmed takedown).
        Reg("st_kill", 0.30f, b => {
            Tone(b, 520, 0, 0.08f, Wv.Square, 0.30f);
            Tone(b, 392, 0.07f, 0.12f, Wv.Saw, 0.28f, 300);
            Noise(b, 0, 0.06f, 0.22f);
        });
        // LASTKILL: the decisive blow that clears the field — a brighter rising flourish
        // that resolves up (a beat heavier than a plain kill, lighter than full VICTORY).
        Reg("st_lastkill", 0.55f, b => {
            float[] n = { 523, 659, 880 };
            for (int i = 0; i < 3; i++) Tone(b, n[i], i * 0.10f, 0.18f, Wv.Tri, 0.32f);
            Tone(b, 70, 0, 0.30f, Wv.Sine, 0.40f, 50);   // sub thud under the flourish
            Noise(b, 0, 0.05f, 0.20f);
        });
        // VICTORY: a fuller, longer major-triad resolve (mission won).
        Reg("st_victory", 0.95f, b => {
            float[] n = { 523, 659, 784, 1046, 1318 };
            for (int i = 0; i < 5; i++) Tone(b, n[i], i * 0.12f, 0.26f, Wv.Tri, 0.30f);
        });
        // LOSE: a sinking minor descent (mission failed but the run goes on).
        Reg("st_lose", 0.90f, b => {
            float[] n = { 440, 349, 277, 220 };
            for (int i = 0; i < 4; i++) Tone(b, n[i], i * 0.14f, 0.26f, Wv.Saw, 0.30f);
        });
        // SQUADWIPE: the run-ending gut-punch — a low, ominous drop + noise wash.
        Reg("st_squadwipe", 1.00f, b => {
            Tone(b, 196, 0, 0.55f, Wv.Saw, 0.40f, 70);
            Tone(b, 98, 0, 0.70f, Wv.Sine, 0.42f, 49);
            Noise(b, 0.05f, 0.45f, 0.30f);
        });
    }

    // RIFLE = the baseline firing voice (shared by "shoot" + "w_rifle").
    static void FillRifle(float[] b) { Noise(b, 0, 0.12f, 0.45f); Tone(b, 165, 0, 0.09f, Wv.Saw, 0.32f, 80); }

    /// Map a weapon kind to its firing-voice sound id. Falls back to the rifle voice.
    static string WeaponSound(WeaponKind k) => k switch
    {
        WeaponKind.Shotgun => "w_shotgun",
        WeaponKind.Sniper  => "w_sniper",
        WeaponKind.Lmg     => "w_lmg",
        WeaponKind.Smg     => "w_smg",
        _                  => "w_rifle",
    };

    /// Play the firing voice for a given weapon kind (no-op headless / muted).
    public static void PlayWeapon(WeaponKind k) => Play(WeaponSound(k));

    /// Play an event stinger by name: "kill" / "lastkill" / "victory" / "lose" / "squadwipe".
    /// Unknown names are a safe no-op. (No-op headless / muted.)
    public static void PlayStinger(string which)
    {
        switch (which)
        {
            case "kill":      Play("st_kill"); break;
            case "lastkill":  Play("st_lastkill"); break;
            case "victory":   Play("st_victory"); break;
            case "lose":      Play("st_lose"); break;
            case "squadwipe": Play("st_squadwipe"); break;
        }
    }

    static void InitMusic()
    {
        try
        {
            _ambient = Raylib.LoadMusicStreamFromMemory(".wav", BuildAmbient());
            _combat = Raylib.LoadMusicStreamFromMemory(".wav", BuildCombat());
            _ambient.Looping = true;
            _combat.Looping = true;
            Raylib.PlayMusicStream(_ambient);
            Raylib.PlayMusicStream(_combat);
            Raylib.SetMusicVolume(_ambient, 0f);
            Raylib.SetMusicVolume(_combat, 0f);
            _music = true;
        }
        catch { _music = false; }
    }

    /// Pump the music streams and crossfade ambient<->combat by the current intensity.
    /// Called once per frame from the window loop; a no-op when there is no audio device.
    public static void UpdateMusic(float dt)
    {
        if (!_music) return;
        Raylib.UpdateMusicStream(_ambient);
        Raylib.UpdateMusicStream(_combat);
        float master = Enabled ? 1f : 0f;
        float ambT = master * (0.50f - 0.22f * _intensity);   // bed quiets a touch under combat
        float combT = master * (0.62f * _intensity);
        float k = Util.Clamp(dt * 2.2f, 0f, 1f);
        _ambVol = Util.Lerp(_ambVol, ambT, k);
        _combVol = Util.Lerp(_combVol, combT, k);
        Raylib.SetMusicVolume(_ambient, _ambVol);
        Raylib.SetMusicVolume(_combat, _combVol);
    }

    // ---- looping music beds (integer freqs over an integer-second buffer => seamless) ----
    const int MusicSecs = 8;

    static byte[] BuildAmbient()
    {
        var b = new float[MusicSecs * SR];
        PadTone(b, 110, 0.18f, 1);   // A2 root
        PadTone(b, 165, 0.12f, 2);   // E3 fifth, slow tremolo
        PadTone(b, 220, 0.10f, 1);   // A3
        PadTone(b, 262, 0.07f, 3);   // C4 minor third (gentle shimmer)
        return MusicBytes(b);
    }

    static byte[] BuildCombat()
    {
        var b = new float[MusicSecs * SR];
        PadTone(b, 110, 0.15f, 1);   // root
        PadTone(b, 156, 0.12f, 2);   // ~Eb3 tension
        PadTone(b, 220, 0.09f, 1);
        Pulse(b, 55, 2f, 0.22f);     // driving sub-bass pulse (16 hits / loop)
        Pulse(b, 110, 2f, 0.10f);
        return MusicBytes(b);
    }

    // a sustained sine with a seamless LFO tremolo (lfoK whole cycles per loop)
    static void PadTone(float[] b, int freq, float vol, int lfoK)
    {
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)SR;
            float lfo = 0.7f + 0.3f * MathF.Sin(2f * MathF.PI * (lfoK / (float)MusicSecs) * t);
            b[i] += MathF.Sin(2f * MathF.PI * freq * t) * vol * lfo;
        }
    }

    // a repeating plucked pulse at `rate` Hz (rate must divide evenly into the loop)
    static void Pulse(float[] b, int freq, float rate, float vol)
    {
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)SR;
            float ph = (t * rate) % 1f;
            float env = MathF.Exp(-8f * ph);
            b[i] += MathF.Sin(2f * MathF.PI * freq * t) * vol * env;
        }
    }

    static byte[] MusicBytes(float[] b)
    {
        float peak = 0.0001f;
        for (int i = 0; i < b.Length; i++) peak = MathF.Max(peak, MathF.Abs(b[i]));
        return EncodeWav(b, 0.8f / peak);
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
        if (_music)
        {
            Raylib.UnloadMusicStream(_ambient);
            Raylib.UnloadMusicStream(_combat);
            _music = false;
        }
        foreach (var s in _snd.Values) Raylib.UnloadSound(s);
        _snd.Clear();
        Raylib.CloseAudioDevice();
    }

    // ---------------- synthesis ----------------

    /// Generate a recipe's raw float sample buffer WITHOUT touching the audio device.
    /// This is the path the self-test validates (sample count > 0, all finite).
    static float[] BuildBuffer(float dur, Action<float[]> fill)
    {
        var buf = new float[(int)(dur * SR) + 8];
        fill(buf);
        return buf;
    }

    // Load a single recipe into a device Sound (device path only).
    static void LoadRecipe(string id, float dur, Action<float[]> fill)
    {
        var buf = BuildBuffer(dur, fill);
        // normalise to avoid clipping
        float peak = 0.0001f;
        for (int i = 0; i < buf.Length; i++) peak = MathF.Max(peak, MathF.Abs(buf[i]));
        float g = peak > 1f ? 1f / peak : 1f;
        byte[] wav = EncodeWav(buf, g);
        Wave w = Raylib.LoadWaveFromMemory(".wav", wav);
        _snd[id] = Raylib.LoadSoundFromWave(w);
        Raylib.UnloadWave(w);
    }

    /// Device-free validation of the whole synth path: builds every SFX recipe + both music
    /// beds in memory and asserts each produces a non-empty, finite (no NaN/Inf), in-range
    /// buffer. Returns "AUDIOTEST: PASS" or "AUDIOTEST: FAIL <reason>". No audio device needed.
    public static string SelfTest()
    {
        try
        {
            BuildRecipes();
            if (_recipes.Count == 0) return "AUDIOTEST: FAIL no recipes registered";

            // every weapon kind must map to a registered recipe
            foreach (WeaponKind k in Enum.GetValues(typeof(WeaponKind)))
            {
                string id = WeaponSound(k);
                if (!_recipes.ContainsKey(id))
                    return $"AUDIOTEST: FAIL weapon {k} -> missing recipe '{id}'";
            }
            // every stinger name must resolve to a registered recipe
            string[] stingers = { "st_kill", "st_lastkill", "st_victory", "st_lose", "st_squadwipe" };
            foreach (var s in stingers)
                if (!_recipes.ContainsKey(s)) return $"AUDIOTEST: FAIL missing stinger recipe '{s}'";

            // build + validate every SFX buffer
            foreach (var kv in _recipes)
            {
                var (dur, fill) = kv.Value;
                if (dur <= 0f) return $"AUDIOTEST: FAIL '{kv.Key}' non-positive duration {dur}";
                float[] buf;
                try { buf = BuildBuffer(dur, fill); }
                catch (Exception e) { return $"AUDIOTEST: FAIL '{kv.Key}' threw {e.GetType().Name}"; }
                string err = ValidateBuffer(kv.Key, buf);
                if (err != null) return "AUDIOTEST: FAIL " + err;
            }

            // build + validate the two looping music beds (their own synth path)
            string mErr = ValidateBuffer("ambient", BuildAmbient(), wav: true)
                       ?? ValidateBuffer("combat",  BuildCombat(),  wav: true);
            if (mErr != null) return "AUDIOTEST: FAIL " + mErr;

            return "AUDIOTEST: PASS";
        }
        catch (Exception e)
        {
            return "AUDIOTEST: FAIL " + e.GetType().Name + ": " + e.Message;
        }
    }

    // Validate a synthesised buffer (or an encoded WAV's PCM if wav=true) is non-empty + finite.
    static string ValidateBuffer(string id, byte[] wavBytes, bool wav)
    {
        if (wavBytes == null || wavBytes.Length <= 44) return $"'{id}' empty/short WAV ({wavBytes?.Length ?? 0} bytes)";
        return null;
    }

    static string ValidateBuffer(string id, float[] buf)
    {
        if (buf == null || buf.Length == 0) return $"'{id}' produced an empty buffer";
        for (int i = 0; i < buf.Length; i++)
            if (float.IsNaN(buf[i]) || float.IsInfinity(buf[i]))
                return $"'{id}' produced a non-finite sample at {i}";
        return null;
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
