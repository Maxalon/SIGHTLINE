using System;
using System.Collections.Generic;
using System.IO;
using Raylib_cs;

namespace Sightline;

enum Wv { Sine, Square, Saw, Tri }

/// Fully procedural SFX — no asset files. Each effect is synthesised into a
/// 16-bit PCM WAV in memory and loaded as a Raylib Sound. Safe on machines
/// with no audio device (everything becomes a no-op).
///
/// ── TUNING NOTES (for the owner, who has a real audio device) ─────────────
/// • Master volume is set in Init() = `MasterVol` (0.6). Raise/lower it there.
/// • Each SFX recipe lives in BuildRecipes() as a `Reg(id, dur, fill)` line —
///   the `fill` lambda layers Click/Tone/Noise calls. Per-layer `vol` args are
///   0..1 amplitudes; keep the SUM of simultaneous layers under ~1.0 so the
///   final normalise (in LoadRecipe) doesn't have to crush them.
/// • Envelope shape is the `EnvShape` constants below + the optional ADSR args
///   on Tone(); tweak attack/decay there to make a cue snappier or softer.
/// • Music beds: BuildAmbient()/BuildCombat() — chord notes + movement. The
///   loop is `MusicSecs` seconds of INTEGER-Hz tones (so sin==0 at both ends →
///   seamless). KEEP every freq an integer and every LFO/pulse period a whole
///   divisor of MusicSecs, or you reintroduce a loop click.
/// • The ambient↔combat crossfade levels are `AmbBaseVol`/`CombMaxVol` in
///   UpdateMusic().
public static class Audio
{
    const int SR = 44100;

    // ── headline tuning constants ──
    const float MasterVol  = 0.6f;   // global master (Raylib.SetMasterVolume)
    const float AmbBaseVol = 0.50f;  // ambient bed target at intensity 0
    const float AmbDuck    = 0.22f;  // how much the bed quiets under full combat
    const float CombMaxVol = 0.62f;  // combat bed target at intensity 1

    static bool _ready;
    public static bool Enabled = true;
    static readonly Dictionary<string, Sound> _snd = new();

    // Recipe registry: id -> (duration, fill). Populated by BuildRecipes() and shared by both
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
        Raylib.SetMasterVolume(MasterVol);

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

        // ───────── baseline action cues ─────────
        // SELECT: a crisp two-tone blip (clicky UI confirm).
        Reg("select", 0.09f, b => {
            Click(b, 0, 0.22f);
            Tone(b, 540, 0, 0.05f, Wv.Square, 0.26f, atk: 0.002f, dec: 5f);
            Tone(b, 810, 0.012f, 0.04f, Wv.Sine, 0.18f, atk: 0.002f, dec: 6f);
        });
        // MOVE: a soft, short footfall thud (low, rounded — fires a lot, stays gentle).
        Reg("move", 0.08f, b => {
            Tone(b, 220, 0, 0.06f, Wv.Tri, 0.22f, slideTo: 150, atk: 0.003f, dec: 5f);
            Noise(b, 0, 0.03f, 0.10f, lp: 0.5f);
        });
        // RELOAD: a mechanical two-click "cha-chk" (mag out, mag in + bolt).
        Reg("reload", 0.18f, b => {
            Click(b, 0, 0.30f);
            Tone(b, 520, 0.005f, 0.04f, Wv.Square, 0.22f, atk: 0.001f, dec: 7f);
            Click(b, 0.085f, 0.34f);
            Tone(b, 360, 0.090f, 0.05f, Wv.Square, 0.20f, atk: 0.001f, dec: 6f);
        });
        // HUNKER: a low settling thunk (dig in).
        Reg("hunker", 0.18f, b => {
            Tone(b, 230, 0, 0.15f, Wv.Tri, 0.30f, slideTo: 170, atk: 0.006f, dec: 3.2f);
            Noise(b, 0, 0.05f, 0.12f, lp: 0.4f);
        });

        // ───────── per-weapon firing voices (each short — these fire a LOT) ─────────
        // Construction pattern: a sharp transient CLICK (the mechanism) + a NOISE burst
        // (the powder crack) + a BODY tone with a quick pitch-drop (the report), tuned per
        // weapon so each is unmistakable. "shoot" stays the baseline RIFLE voice.
        Reg("shoot",   0.16f, FillRifle);
        Reg("w_rifle", 0.16f, FillRifle);
        // SHOTGUN: a fat low boom + a wide, long noise wash (heavy, blunt).
        Reg("w_shotgun", 0.24f, b => {
            Click(b, 0, 0.30f);
            Noise(b, 0, 0.20f, 0.62f, lp: 0.65f);                 // broad blast wash
            Tone(b, 110, 0, 0.18f, Wv.Saw, 0.46f, slideTo: 55, atk: 0.001f, dec: 3.0f);
            Tone(b, 150, 0, 0.06f, Wv.Square, 0.28f, slideTo: 70, atk: 0.001f, dec: 7f);
        });
        // SNIPER: a hard transient + a bright high crack + a long ringing metallic tail.
        Reg("w_sniper", 0.30f, b => {
            Click(b, 0, 0.40f);
            Noise(b, 0, 0.045f, 0.52f, lp: 1f);                   // tight bright crack
            Tone(b, 1020, 0, 0.05f, Wv.Square, 0.40f, slideTo: 520, atk: 0.0006f, dec: 8f);
            Tone(b, 300, 0.02f, 0.26f, Wv.Saw, 0.26f, slideTo: 150, atk: 0.002f, dec: 1.8f); // ringing tail
        });
        // SMG: a quick, bright, snappy crack (tight + punchy).
        Reg("w_smg", 0.11f, b => {
            Click(b, 0, 0.26f);
            Noise(b, 0, 0.06f, 0.42f, lp: 0.9f);
            Tone(b, 360, 0, 0.06f, Wv.Square, 0.30f, slideTo: 200, atk: 0.0008f, dec: 8f);
        });
        // LMG: a heavy chug — thick low body + a long rolling noise (big bore).
        Reg("w_lmg", 0.22f, b => {
            Click(b, 0, 0.32f);
            Noise(b, 0, 0.18f, 0.54f, lp: 0.55f);
            Tone(b, 135, 0, 0.16f, Wv.Saw, 0.44f, slideTo: 60, atk: 0.001f, dec: 3.2f);
            Tone(b, 90, 0, 0.12f, Wv.Square, 0.32f, slideTo: 48, atk: 0.001f, dec: 4.5f);
        });

        // ───────── impact cues (crit layered heavier than a normal hit) ─────────
        // HIT: a meaty thump — a noise smack + a short low body.
        Reg("hit", 0.18f, b => {
            Noise(b, 0, 0.10f, 0.50f, lp: 0.55f);
            Tone(b, 150, 0, 0.12f, Wv.Square, 0.42f, slideTo: 90, atk: 0.001f, dec: 4.5f);
        });
        // CRIT: the HIT smack PLUS a deeper sub-bass thud + a metallic ping (lands heavier).
        Reg("crit", 0.30f, b => {
            Noise(b, 0, 0.14f, 0.55f, lp: 0.7f);
            Tone(b, 240, 0, 0.16f, Wv.Saw, 0.40f, slideTo: 110, atk: 0.001f, dec: 3.2f);
            Tone(b, 70, 0, 0.26f, Wv.Sine, 0.46f, slideTo: 46, atk: 0.002f, dec: 2.0f);   // sub-bass thud
            Tone(b, 1200, 0.005f, 0.06f, Wv.Sine, 0.16f, atk: 0.0006f, dec: 9f);          // bright ping
        });
        // MISS: a quick zip past the ear (high, descending, airy).
        Reg("miss", 0.14f, b => {
            Tone(b, 1400, 0, 0.11f, Wv.Sine, 0.22f, slideTo: 520, atk: 0.003f, dec: 3.5f);
            Noise(b, 0, 0.05f, 0.07f, lp: 1f);
        });
        // OVERWATCH set: a tense rising two-note "ready" tone.
        Reg("over", 0.20f, b => {
            Tone(b, 440, 0, 0.08f, Wv.Square, 0.28f, atk: 0.004f, dec: 4f);
            Tone(b, 660, 0.075f, 0.10f, Wv.Square, 0.24f, atk: 0.004f, dec: 3.5f);
        });
        // DEATH: a downward collapse — a saw fall + a noise crumple.
        Reg("death", 0.36f, b => {
            Tone(b, 260, 0, 0.32f, Wv.Saw, 0.38f, slideTo: 60, atk: 0.004f, dec: 2.2f);
            Noise(b, 0, 0.26f, 0.30f, lp: 0.5f);
            Tone(b, 80, 0.02f, 0.22f, Wv.Sine, 0.30f, slideTo: 44, atk: 0.004f, dec: 2.4f);
        });
        // TURN: a clear rising two-note announce (the round changes hands).
        Reg("turn", 0.30f, b => {
            Tone(b, 330, 0, 0.13f, Wv.Tri, 0.30f, atk: 0.006f, dec: 3.2f);
            Tone(b, 494, 0.11f, 0.16f, Wv.Tri, 0.27f, atk: 0.006f, dec: 2.8f);
        });
        // WIN: a bright ascending major arpeggio (legacy cue alongside the victory stinger).
        Reg("win", 0.72f, b => Arp(b, new[] { 523, 659, 784, 1046 }, 0.11f, 0.20f, Wv.Tri, 0.30f));
        // LOSE: a sinking minor descent (legacy cue alongside the lose stinger).
        Reg("lose", 0.82f, b => Arp(b, new[] { 392, 330, 262, 196 }, 0.13f, 0.24f, Wv.Saw, 0.28f));

        // ───────── event stingers (short emphatic phrases; fired via PlayStinger) ─────────
        // KILL: a quick decisive down-flick + a noise crunch (a confirmed takedown).
        Reg("st_kill", 0.30f, b => {
            Tone(b, 587, 0, 0.07f, Wv.Square, 0.30f, atk: 0.002f, dec: 5f);
            Tone(b, 392, 0.06f, 0.13f, Wv.Saw, 0.30f, slideTo: 320, atk: 0.002f, dec: 3f);
            Noise(b, 0, 0.05f, 0.22f, lp: 0.7f);
            Tone(b, 80, 0, 0.16f, Wv.Sine, 0.26f, atk: 0.003f, dec: 3f);   // small thud
        });
        // LASTKILL: the blow that clears the field — a brighter rising flourish that resolves
        // up (heavier than a plain kill, lighter than full VICTORY) over a sub thud.
        Reg("st_lastkill", 0.58f, b => {
            Arp(b, new[] { 523, 659, 880 }, 0.10f, 0.18f, Wv.Tri, 0.32f);
            Tone(b, 70, 0, 0.34f, Wv.Sine, 0.40f, slideTo: 52, atk: 0.004f, dec: 1.6f);  // sub thud
            Noise(b, 0, 0.05f, 0.20f, lp: 0.8f);
        });
        // VICTORY: a fuller, longer major-add9 resolve (mission won) — arpeggio that lands on
        // a sustained tonic chord so it RESOLVES rather than just trailing off.
        Reg("st_victory", 1.05f, b => {
            Arp(b, new[] { 523, 659, 784, 1046, 1318 }, 0.11f, 0.22f, Wv.Tri, 0.28f);
            // sustained resolving C-major triad under the tail
            Tone(b, 523, 0.55f, 0.46f, Wv.Sine, 0.20f, atk: 0.02f, dec: 1.2f);
            Tone(b, 659, 0.55f, 0.46f, Wv.Sine, 0.16f, atk: 0.02f, dec: 1.2f);
            Tone(b, 784, 0.55f, 0.46f, Wv.Sine, 0.14f, atk: 0.02f, dec: 1.2f);
        });
        // LOSE: a sinking minor descent that settles on a low sustained minor chord (failed,
        // but the run goes on).
        Reg("st_lose", 0.95f, b => {
            Arp(b, new[] { 440, 349, 277, 220 }, 0.13f, 0.24f, Wv.Saw, 0.30f);
            Tone(b, 220, 0.55f, 0.40f, Wv.Sine, 0.20f, atk: 0.03f, dec: 1.4f);   // A
            Tone(b, 262, 0.55f, 0.40f, Wv.Sine, 0.15f, atk: 0.03f, dec: 1.4f);   // C (minor third)
            Tone(b, 165, 0.55f, 0.40f, Wv.Sine, 0.18f, atk: 0.03f, dec: 1.4f);   // E below
        });
        // SQUADWIPE: the run-ending gut-punch — a low ominous drop + noise wash + a dissonant
        // low tritone so it reads as final/wrong.
        Reg("st_squadwipe", 1.05f, b => {
            Tone(b, 196, 0, 0.55f, Wv.Saw, 0.40f, slideTo: 70, atk: 0.01f, dec: 1.3f);
            Tone(b, 98, 0, 0.75f, Wv.Sine, 0.42f, slideTo: 49, atk: 0.01f, dec: 1.0f);
            Tone(b, 138, 0.18f, 0.55f, Wv.Saw, 0.20f, atk: 0.02f, dec: 1.3f);   // tritone-ish dissonance
            Noise(b, 0.05f, 0.50f, 0.30f, lp: 0.45f);
        });
    }

    // RIFLE = the baseline firing voice (shared by "shoot" + "w_rifle"): click + crack + body.
    static void FillRifle(float[] b)
    {
        Click(b, 0, 0.30f);
        Noise(b, 0, 0.09f, 0.46f, lp: 0.8f);
        Tone(b, 200, 0, 0.10f, Wv.Saw, 0.34f, slideTo: 95, atk: 0.0008f, dec: 4.5f);
    }

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
        float ambT = master * (AmbBaseVol - AmbDuck * _intensity);   // bed quiets a touch under combat
        float combT = master * (CombMaxVol * _intensity);
        float k = Util.Clamp(dt * 2.2f, 0f, 1f);
        _ambVol = Util.Lerp(_ambVol, ambT, k);
        _combVol = Util.Lerp(_combVol, combT, k);
        Raylib.SetMusicVolume(_ambient, _ambVol);
        Raylib.SetMusicVolume(_combat, _combVol);
    }

    // ───── looping music beds (integer freqs over an integer-second buffer => seamless) ─────
    const int MusicSecs = 8;

    // AMBIENT: a fuller A-minor add9 pad (A C E B) with slow, gently-detuned tremolo movement
    // and a soft beating shimmer up top — atmospheric, never fatiguing under the whole game.
    static byte[] BuildAmbient()
    {
        var b = new float[MusicSecs * SR];
        PadTone(b, 110, 0.17f, 1);    // A2 root
        PadTone(b, 165, 0.12f, 2);    // E3 fifth (slow tremolo)
        PadTone(b, 220, 0.10f, 1);    // A3
        PadTone(b, 262, 0.075f, 3);   // C4 minor third (shimmer)
        PadTone(b, 330, 0.045f, 2);   // E4 octave fifth (air)
        PadTone(b, 494, 0.030f, 4);   // B4 add9 (sparkle, faster tremolo)
        Shimmer(b, 880, 0.022f, 1);   // very soft high beating layer (movement)
        return MusicBytes(b);
    }

    // COMBAT: a darker, tenser bed (A C Eb — minor with a flat-five bite) over a DRIVING
    // sub-bass pulse + a faster mid pulse, so it reads as urgent without being loud.
    static byte[] BuildCombat()
    {
        var b = new float[MusicSecs * SR];
        PadTone(b, 110, 0.15f, 1);    // root
        PadTone(b, 156, 0.11f, 2);    // ~Eb3 (flat-five tension)
        PadTone(b, 220, 0.085f, 1);   // A3
        PadTone(b, 311, 0.045f, 3);   // ~Eb4 tension up top
        Pulse(b, 55, 2f, 0.24f);      // driving sub-bass pulse (16 hits / loop)
        Pulse(b, 110, 2f, 0.11f);     // octave reinforcement
        Pulse(b, 220, 4f, 0.06f);     // faster mid tick (32/loop) — adds urgency
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

    // a soft high "shimmer" — two integer freqs a few Hz apart that beat slowly for movement.
    // Both freqs stay integer so the loop is still seamless.
    static void Shimmer(float[] b, int freq, float vol, int lfoK)
    {
        int freq2 = freq + lfoK;   // a few-Hz integer offset => slow beating
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)SR;
            float env = 0.6f + 0.4f * MathF.Sin(2f * MathF.PI * (1 / (float)MusicSecs) * t);
            b[i] += (MathF.Sin(2f * MathF.PI * freq * t) + MathF.Sin(2f * MathF.PI * freq2 * t))
                    * 0.5f * vol * env;
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

    // ──────────────── synthesis ────────────────

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

            // every SFX id the rest of the game plays must exist (catch a dropped recipe)
            string[] gameIds = { "select", "move", "reload", "hunker", "shoot", "hit", "crit",
                                 "miss", "over", "death", "turn", "win", "lose" };
            foreach (var id in gameIds)
                if (!_recipes.ContainsKey(id)) return $"AUDIOTEST: FAIL missing game cue '{id}'";

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

            // build + validate the two looping music beds (their own synth path), AND confirm
            // they loop click-free: first and last samples must be ~equal (seamless).
            string mErr = ValidateMusic("ambient", BuildAmbient())
                       ?? ValidateMusic("combat",  BuildCombat());
            if (mErr != null) return "AUDIOTEST: FAIL " + mErr;

            return "AUDIOTEST: PASS";
        }
        catch (Exception e)
        {
            return "AUDIOTEST: FAIL " + e.GetType().Name + ": " + e.Message;
        }
    }

    // Validate an encoded WAV is non-empty + the loop endpoints are continuous (no click).
    static string ValidateMusic(string id, byte[] wavBytes)
    {
        if (wavBytes == null || wavBytes.Length <= 44) return $"'{id}' empty/short WAV ({wavBytes?.Length ?? 0} bytes)";
        // decode first + last 16-bit sample; a seamless loop needs them close.
        int n = (wavBytes.Length - 44) / 2;
        if (n < 2) return $"'{id}' too few samples";
        short first = (short)(wavBytes[44] | (wavBytes[45] << 8));
        short last  = (short)(wavBytes[44 + (n - 1) * 2] | (wavBytes[44 + (n - 1) * 2 + 1] << 8));
        // both endpoints should be near zero crossings for a clean loop; allow a small margin.
        if (Math.Abs(first) > 3000 || Math.Abs(last) > 3000)
            return $"'{id}' loop endpoints not near zero (first={first}, last={last}) — would click";
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

    // ── envelope shaping ──
    // A one-shot envelope: a short linear ATTACK ramp (no DC click at the start) and an
    // exponential DECAY to silence, with a final tiny fade so the END is also click-free.
    // `dec` = decay rate (bigger = snappier); `t01` = normalised position 0..1 in the tone.
    static float Env(float t01, float atkFrac, float dec)
    {
        float a = atkFrac <= 0f ? 1f : MathF.Min(1f, t01 / atkFrac);     // attack ramp
        float d = MathF.Exp(-dec * t01);                                  // exponential decay
        float r = t01 > 0.92f ? (1f - t01) / 0.08f : 1f;                  // release fade-out (last 8%)
        return a * d * MathF.Max(0f, r);
    }

    // A pitched tone. `slideTo>0` glides freq->slideTo across the tone (pitch envelope).
    // `atk` = attack time in SECONDS; `dec` = exponential decay rate. Defaults give the old
    // ~0.004s attack / -3.5 decay behaviour so legacy callers are essentially unchanged.
    static void Tone(float[] b, float freq, float start, float dur, Wv type, float vol,
                     float slideTo = 0, float atk = 0.004f, float dec = 3.5f)
    {
        int n0 = (int)(start * SR);
        int len = (int)(dur * SR);
        if (len < 1) return;
        float atkFrac = (atk * SR) / len;                 // attack as a fraction of the tone
        float ph = 0f;
        for (int i = 0; i < len; i++)
        {
            int idx = n0 + i;
            if (idx >= b.Length) break;
            float t = i / (float)len;
            float f = slideTo > 0 ? Util.Lerp(freq, slideTo, t) : freq;
            ph += 2f * MathF.PI * f / SR;
            b[idx] += Shape(type, ph) * vol * Env(t, atkFrac, dec);
        }
    }

    // A noise burst with an attack ramp + exponential decay. `lp` (0..1) is a simple one-pole
    // low-pass mix (lower = darker/duller noise; 1 = raw white) so weapons can have body.
    static void Noise(float[] b, float start, float dur, float vol, float lp = 1f)
    {
        int n0 = (int)(start * SR);
        int len = (int)(dur * SR);
        if (len < 1) return;
        int atk = Math.Max(1, (int)(0.0015f * SR));
        float prev = 0f;
        float alpha = Util.Clamp(lp, 0.02f, 1f);          // one-pole coefficient
        for (int i = 0; i < len; i++)
        {
            int idx = n0 + i;
            if (idx >= b.Length) break;
            float t = i / (float)len;
            float raw = Util.RandF() * 2f - 1f;
            prev += alpha * (raw - prev);                 // low-pass toward `prev`
            float a = MathF.Min(1f, i / (float)atk);      // attack
            float env = a * MathF.Exp(-4.5f * t);
            float rel = t > 0.9f ? (1f - t) / 0.1f : 1f;  // release fade
            b[idx] += prev * vol * env * MathF.Max(0f, rel);
        }
    }

    // A very short noise transient — the mechanical "click"/attack of a gun or UI cue.
    // Adds bite at the onset; ~2.5ms, hard decay, so it never muddies the body.
    static void Click(float[] b, float start, float vol)
    {
        int n0 = (int)(start * SR);
        int len = Math.Max(2, (int)(0.0025f * SR));
        for (int i = 0; i < len; i++)
        {
            int idx = n0 + i;
            if (idx >= b.Length) break;
            float t = i / (float)len;
            float env = MathF.Exp(-22f * t) * (1f - t);   // fast decay + linear release
            b[idx] += (Util.RandF() * 2f - 1f) * vol * env;
        }
    }

    // An arpeggio helper: play `notes` in sequence, each `step` apart, each `dur` long.
    static void Arp(float[] b, int[] notes, float step, float dur, Wv type, float vol)
    {
        for (int i = 0; i < notes.Length; i++)
            Tone(b, notes[i], i * step, dur, type, vol, atk: 0.006f, dec: 3.2f);
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
