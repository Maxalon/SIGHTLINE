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
public static partial class Audio
{
    internal const int SR = 44100;

    // ── RESONANCE A1: the synth gets its OWN rng ──────────────────────────────
    // Noise()/Click() used to draw from the SHARED Util.Rng. Audio.Init() runs BEFORE
    // `new Game()` (Program.cs), so on a machine WITH an audio device every synthesised
    // cue silently advanced the gameplay RNG stream — and the draw count CHANGED when a
    // drop-in asset file was present (the file-first path skips the synth). Two machines
    // could therefore diverge on identical seeds. A private, fixed-seed stream that is
    // RESET per cue makes synthesis reproducible AND removes it from gameplay entirely.
    static Random _arng = new Random(AudSeedBase);
    const int AudSeedBase = 0x5A17;
    static float AudRandF() => (float)_arng.NextDouble();
    /// Reseed the synth rng from a cue id (FNV-1a — String.GetHashCode is per-process
    /// randomised in .NET Core and would break byte-reproducible dumps).
    static void SeedFor(string id)
    {
        uint h = 2166136261u;
        foreach (char c in id) { h ^= c; h *= 16777619u; }
        _arng = new Random(unchecked((int)(h ^ AudSeedBase)));
    }

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
        // FILE-FIRST, SYNTH-FALLBACK: for each cue, try a dropped-in CC0 sample
        // (assets/sfx/<id>.wav or .ogg) FIRST; only synthesise the procedural recipe if no
        // valid file is present. The sandbox ships NO asset files, so the procedural path stays
        // the active one (and all self-tests pass) — this just lets real audio drop in later
        // with zero call-site changes.
        foreach (var kv in _recipes)
        {
            if (LoadFile(kv.Key, $"assets/sfx/{kv.Key}.wav") || LoadFile(kv.Key, $"assets/sfx/{kv.Key}.ogg"))
                continue;
            LoadRecipe(kv.Key, kv.Value.dur, kv.Value.fill);
        }

        InitMusic();
    }

    /// Try to load a real sound FILE into the cue table. Returns true only if the file exists
    /// AND loads to a valid Sound (so a missing/corrupt file cleanly falls through to the synth
    /// fallback). Guarded — safe with no device (callers only reach here when _ready).
    static bool LoadFile(string id, string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            var s = Raylib.LoadSound(path);
            if (Raylib.IsSoundValid(s)) { _snd[id] = s; return true; }
        }
        catch { }
        return false;
    }

    /// Register every SFX recipe into _recipes WITHOUT touching the audio device. Called by
    /// Init (which then loads each into a Sound) and by the device-free self-test. Adding a
    /// new effect = one Reg(...) line here.
    internal static void BuildRecipes()
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

    // RIFLE = the baseline firing voice (shared by "shoot" + "w_rifle"): a sharp mechanism
    // CLICK + a bright powder CRACK + a saw BODY with a quick pitch-drop, now with a tiny
    // second transient click (the action cycling) + a faint low thump for more "weight".
    static void FillRifle(float[] b)
    {
        Click(b, 0, 0.34f);                                       // sharper attack transient
        Click(b, 0.012f, 0.16f);                                  // a faint 2nd click (action cycle)
        Noise(b, 0, 0.09f, 0.46f, lp: 0.8f);
        Tone(b, 200, 0, 0.10f, Wv.Saw, 0.34f, slideTo: 95, atk: 0.0008f, dec: 4.5f);
        Tone(b, 95, 0, 0.06f, Wv.Sine, 0.20f, slideTo: 60, atk: 0.0008f, dec: 5f);   // low body thump
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

    /// Play the firing voice for a given weapon kind (no-op headless / muted). Optional
    /// per-shot pitch jitter + stereo pan (defaults preserve existing call sites).
    public static void PlayWeapon(WeaponKind k, float pitchVar = 0f, float panX = -1f)
        => Play(WeaponSound(k), pitchVar, panX);

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

    /// Load a music bed FILE if present + valid, else synthesise the procedural bed from
    /// `synth` (a .wav byte buffer). Same file-first / synth-fallback contract as LoadFile,
    /// so a dropped-in assets/music/<name>.ogg overrides the synth with zero call-site change.
    static Music LoadMusicFile(string path, Func<byte[]> synth)
    {
        try
        {
            if (File.Exists(path))
            {
                var m = Raylib.LoadMusicStream(path);
                if (Raylib.IsMusicValid(m)) return m;
            }
        }
        catch { }
        return Raylib.LoadMusicStreamFromMemory(".wav", synth());
    }

    static void InitMusic()
    {
        try
        {
            _ambient = LoadMusicFile("assets/music/ambient.ogg", BuildAmbient);
            _combat = LoadMusicFile("assets/music/combat.ogg", BuildCombat);
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
    internal const int MusicSecs = 8;

    // AMBIENT: a fuller A-minor add9 pad (A C E B) with slow, gently-detuned tremolo movement
    // and a soft beating shimmer up top — atmospheric, never fatiguing under the whole game.
    internal static byte[] BuildAmbient() => MusicBytes(AmbientFloats());
    static float[] AmbientFloats()
    {
        var b = new float[MusicSecs * SR];
        PadTone(b, 110, 0.17f, 1);    // A2 root
        PadTone(b, 165, 0.12f, 2);    // E3 fifth (slow tremolo)
        PadTone(b, 220, 0.10f, 1);    // A3
        PadTone(b, 262, 0.075f, 3);   // C4 minor third (shimmer)
        PadTone(b, 330, 0.045f, 2);   // E4 octave fifth (air)
        PadTone(b, 494, 0.030f, 4);   // B4 add9 (sparkle, faster tremolo)
        Shimmer(b, 880, 0.022f, 1);   // very soft high beating layer (movement)
        return b;
    }

    // COMBAT: a darker, tenser bed (A C Eb — minor with a flat-five bite) over a DRIVING
    // sub-bass pulse + a faster mid pulse, so it reads as urgent without being loud.
    internal static byte[] BuildCombat() => MusicBytes(CombatFloats());
    static float[] CombatFloats()
    {
        var b = new float[MusicSecs * SR];
        PadTone(b, 110, 0.15f, 1);    // root
        PadTone(b, 156, 0.11f, 2);    // ~Eb3 (flat-five tension)
        PadTone(b, 220, 0.085f, 1);   // A3
        PadTone(b, 311, 0.045f, 3);   // ~Eb4 tension up top
        Pulse(b, 55, 2f, 0.24f);      // driving sub-bass pulse (16 hits / loop)
        Pulse(b, 110, 2f, 0.11f);     // octave reinforcement
        Pulse(b, 220, 4f, 0.06f);     // faster mid tick (32/loop) — adds urgency
        return b;
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

    static byte[] MusicBytes(float[] b) => EncodeWav(RenderMusicGain(b), 1f);

    // Apply the music bed's mastering gain IN PLACE and return the buffer (so the measurement
    // harness sees exactly the samples the stream plays).
    static float[] RenderMusicGain(float[] b)
    {
        float peak = 0.0001f;
        for (int i = 0; i < b.Length; i++) peak = MathF.Max(peak, MathF.Abs(b[i]));
        float g = 0.8f / peak;
        for (int i = 0; i < b.Length; i++) b[i] *= g;
        return b;
    }

    /// Device-free: the rendered float samples of a music bed ("ambient" | "combat").
    internal static float[] RenderMusic(string which)
        => RenderMusicGain(which == "combat" ? CombatFloats() : AmbientFloats());

    // a cheap rolling counter so successive shots get a deterministic, non-repeating pitch
    // jitter (NOT Random — keeps the headless harness reproducible + avoids a machine-gun
    // "exactly the same sample" feel). Bounded; wraps harmlessly.
    static int _pitchSeq;

    /// Play a sound. Optional `pitchVar` (±semitone-ish randomisation amount, 0=off) detunes
    /// the sample each call via a rolling counter; `panX` (0..1 = screen-x; <0 = centred/off)
    /// pans it in stereo. Defaults keep every existing call site unchanged + crash-safe headless.
    public static void Play(string id, float pitchVar = 0f, float panX = -1f)
    {
        if (!_ready || !Enabled) return;
        if (!_snd.TryGetValue(id, out var s)) return;
        if (pitchVar > 0f)
        {
            // map the rolling counter to a triangular-ish offset in [-1,1], scale to pitchVar.
            int q = _pitchSeq++ & 7;
            float u = (q / 7f) * 2f - 1f;                 // -1..1 deterministic sweep
            Raylib.SetSoundPitch(s, 1f + pitchVar * u);
        }
        else Raylib.SetSoundPitch(s, 1f);
        if (panX >= 0f)
            // Raylib pan: 0.5 = centre, 0 = right, 1 = left. Map screen-x so left of screen
            // pans left: panLeftFraction = 1 - screenXFraction.
            Raylib.SetSoundPan(s, Util.Clamp(1f - panX, 0f, 1f));
        else
            Raylib.SetSoundPan(s, 0.5f);
        Raylib.PlaySound(s);
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

    /// RENDER = the single source of truth for what a cue actually sounds like: synthesise
    /// the recipe, then apply the mastering stage. Both the device path (LoadRecipe) and the
    /// device-free measurement harness (AUDIODUMP/AUDIOGATE) go through here, so what the
    /// gate measures is byte-for-byte what the speaker gets.
    internal static float[] RenderCue(string id, float dur, Action<float[]> fill)
    {
        SeedFor(id);                       // reproducible noise/click for this cue
        var buf = BuildBuffer(dur, fill);
        // normalise to avoid clipping
        float peak = 0.0001f;
        for (int i = 0; i < buf.Length; i++) peak = MathF.Max(peak, MathF.Abs(buf[i]));
        float g = peak > 1f ? 1f / peak : 1f;
        if (g != 1f) for (int i = 0; i < buf.Length; i++) buf[i] *= g;
        return buf;
    }

    // Load a single recipe into a device Sound (device path only).
    static void LoadRecipe(string id, float dur, Action<float[]> fill)
    {
        byte[] wav = EncodeWav(RenderCue(id, dur, fill), 1f);
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

            // HORIZON W7 — validate any DROPPED-IN audio files (device-free: header magic + size
            // budget). The repo ships none, so this is a no-op today; once real CC0 files are added
            // it guards against a corrupt/oversized/mis-encoded drop-in slipping in.
            string aErr = ValidateDropInAssets();
            if (aErr != null) return "AUDIOTEST: FAIL " + aErr;

            return "AUDIOTEST: PASS";
        }
        catch (Exception e)
        {
            return "AUDIOTEST: FAIL " + e.GetType().Name + ": " + e.Message;
        }
    }

    // HORIZON W7 — the cue ids the FILE-FIRST loader will look for under assets/sfx/<id>.{ogg,wav}
    // (kept in sync with BuildRecipes + the CREDITS convention). Music beds are ambient/combat.
    static readonly string[] SfxCueIds =
    {
        "w_rifle","w_shotgun","w_sniper","w_lmg","w_smg",
        "shoot","hit","crit","miss","over","death",
        "select","move","reload","hunker","turn","win","lose",
        "st_kill","st_lastkill","st_victory","st_lose","st_squadwipe",
    };
    const long MaxSfxBytes   = 400 * 1024;    // keep the repo lean — reject an oversized SFX drop-in
    const long MaxMusicBytes = 4 * 1024 * 1024;

    // Device-free validation of any dropped-in audio FILE: correct extension, a valid container
    // magic (RIFF/WAVE for .wav, OggS for .ogg), non-empty, and within the size budget. Returns
    // null on OK/absent, or an error string. (A device would still validate it loads to a Sound.)
    static string ValidateAudioFile(string path, long maxBytes)
    {
        try
        {
            if (!File.Exists(path)) return null;              // absent -> synth fallback, fine
            var fi = new FileInfo(path);
            if (fi.Length < 16) return $"'{path}' too small ({fi.Length}B)";
            if (fi.Length > maxBytes) return $"'{path}' too large ({fi.Length}B > {maxBytes}B budget)";
            byte[] head = new byte[4];
            using (var fs = File.OpenRead(path)) { if (fs.Read(head, 0, 4) < 4) return $"'{path}' unreadable header"; }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            bool ok = ext == ".wav" ? (head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F')
                    : ext == ".ogg" ? (head[0] == 'O' && head[1] == 'g' && head[2] == 'g' && head[3] == 'S')
                    : false;
            if (!ok) return $"'{path}' bad/unsupported magic for {ext} (use CC0 .ogg or .wav)";
            return null;
        }
        catch (Exception e) { return $"'{path}' {e.GetType().Name}"; }
    }

    // Validate every possible drop-in location (SFX cues + the two music beds). No-op if none present.
    static string ValidateDropInAssets()
    {
        foreach (var id in SfxCueIds)
        {
            string e = ValidateAudioFile($"assets/sfx/{id}.ogg", MaxSfxBytes)
                    ?? ValidateAudioFile($"assets/sfx/{id}.wav", MaxSfxBytes);
            if (e != null) return e;
        }
        return ValidateAudioFile("assets/music/ambient.ogg", MaxMusicBytes)
            ?? ValidateAudioFile("assets/music/combat.ogg",  MaxMusicBytes);
    }

    /// Device-free report: which cues would resolve to a real dropped-in FILE vs the procedural
    /// synth. For the SIGHTLINE_AUDIOASSETS harness hook so the owner can confirm drop-ins are found.
    public static string AudioAssetsReport()
    {
        var sb = new System.Text.StringBuilder();
        int files = 0;
        sb.AppendLine("AUDIO ASSETS (file overrides vs procedural synth):");
        foreach (var id in SfxCueIds)
        {
            string f = File.Exists($"assets/sfx/{id}.ogg") ? $"assets/sfx/{id}.ogg"
                     : File.Exists($"assets/sfx/{id}.wav") ? $"assets/sfx/{id}.wav" : null;
            if (f != null) files++;
            sb.AppendLine($"  {id,-14} {(f != null ? "FILE  " + f : "synth")}");
        }
        foreach (var m in new[] { "ambient", "combat" })
        {
            bool has = File.Exists($"assets/music/{m}.ogg");
            if (has) files++;
            sb.AppendLine($"  music:{m,-8} {(has ? "FILE  assets/music/" + m + ".ogg" : "synth")}");
        }
        sb.AppendLine($"resolved files: {files} / {SfxCueIds.Length + 2}  (0 = fully procedural, the current default)");
        string v = ValidateDropInAssets();
        sb.AppendLine(v == null ? "validation: OK (all present files pass magic/size checks)" : "validation: FAIL " + v);
        return sb.ToString();
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

    // The pre-gain synth buffer is allowed to exceed +-1 (the render stage normalises it),
    // but a runaway layer sum is a real bug — anything past this is not "hot", it is broken.
    const float MaxRawAmp = 8f;

    /// Validate a synth buffer: non-empty, all finite, and IN RANGE (|x| <= MaxRawAmp).
    /// The caller's "in-range" claim used to be an over-claim — this now implements it.
    static string ValidateBuffer(string id, float[] buf)
    {
        if (buf == null || buf.Length == 0) return $"'{id}' produced an empty buffer";
        for (int i = 0; i < buf.Length; i++)
        {
            if (float.IsNaN(buf[i]) || float.IsInfinity(buf[i]))
                return $"'{id}' produced a non-finite sample at {i}";
            if (MathF.Abs(buf[i]) > MaxRawAmp)
                return $"'{id}' produced an out-of-range sample {buf[i]:0.###} at {i} (|x| > {MaxRawAmp})";
        }
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
            float raw = AudRandF() * 2f - 1f;
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
            b[idx] += (AudRandF() * 2f - 1f) * vol * env;
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

    internal static byte[] EncodeWav(float[] samples, float gain)
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
