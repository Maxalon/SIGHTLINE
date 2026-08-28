using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    // ── RESONANCE A2: THE MIX LAYER ───────────────────────────────────────────
    // Until now the only audio controls in the entire game were one hard-coded
    // SetMasterVolume(0.6f) and a binary mute. The owner — the only person with an actual
    // audio device — could not rebalance music against SFX without editing code and
    // rebuilding, which is precisely the thing that has kept this layer untunable.
    // Four busses, persisted in Display's settings file, exposed as sliders in the pause menu.

    /// A cue's mix bus. Reuses the SAME category map the audio budget uses (Audio.Analysis
    /// CatOf), so "what counts as UI" is one decision in one place: select / reload / hunker /
    /// over / turn ride the UI fader, everything in-world rides SFX.
    static bool IsUiCue(string id) => CatOf(id) == "ui";
    static float BusVol(string id) => Util.Clamp(IsUiCue(id) ? Display.VolUi : Display.VolSfx, 0f, 1f);

    /// VOICE POOL. `Play` used to call PlaySound on a SINGLE shared Sound per cue, so two
    /// enemies firing the same weapon truncated each other — and worse, SetSoundPitch/
    /// SetSoundPan mutate that shared buffer, so an ALREADY-PLAYING shot's pitch and pan
    /// jumped the instant the next one started. LoadSoundAlias gives each voice its own
    /// stream state over shared sample data; a round-robin ring with oldest-steal fixes both.
    sealed class Voices { public Sound[] Ring; public int Next; }
    static readonly Dictionary<string, Voices> _voices = new();
    const int RingSize = 6;

    // Deterministic per-call variation (see Play). A counter, NOT a Random — the headless
    // harness has to stay reproducible — but hash-scrambled, unlike the old rolling counter.
    static uint _varSeq;

    // Recipe registry: id -> (duration, fill). Populated by BuildRecipes() and shared by both
    // the device-load path (Init) and the device-free self-test (BuildBuffer/SelfTest), so
    // the buffer-generation path is validated even where there is no audio device.
    static readonly Dictionary<string, (float dur, float target, Action<float[]> fill)> _recipes = new();

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
        ApplyMasterVolume();

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
            LoadRecipe(kv.Key, kv.Value.dur, kv.Value.target, kv.Value.fill);
        }
        BuildVoicePools();
        AttachLimiter();

        InitMusic();
    }

    /// Push Display's master fader at the device. Called from Init and whenever the owner
    /// moves the slider. No-op with no device.
    public static void ApplyMasterVolume()
    {
        if (!_ready) return;
        try { Raylib.SetMasterVolume(Util.Clamp(Display.VolMaster, 0f, 1f)); } catch { }
    }

    /// One alias ring per loaded cue. Aliases share sample data (cheap) but have their own
    /// stream state, which is what lets overlapping copies keep independent pitch/pan/volume.
    static void BuildVoicePools()
    {
        foreach (var kv in _snd)
        {
            var ring = new Sound[RingSize];
            ring[0] = kv.Value;
            for (int i = 1; i < RingSize; i++)
            {
                try { ring[i] = Raylib.LoadSoundAlias(kv.Value); }
                catch { ring[i] = kv.Value; }        // alias unavailable: degrade to the base voice
            }
            _voices[kv.Key] = new Voices { Ring = ring, Next = 0 };
        }
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

        // RESONANCE A1 — `target` is the cue's PEAK level in dBFS. RenderCue normalises
        // UNCONDITIONALLY to it, so this column IS the mix: what sits on top of what. It is
        // deliberately not flat (see the budget rationale in Audio.Analysis.cs).
        void Reg(string id, float dur, float target, Action<float[]> fill)
            => _recipes[id] = (dur, target, fill);

        // ───────── baseline action cues ─────────
        // SELECT: a crisp two-tone blip (clicky UI confirm).
        Reg("select", 0.09f, -14f, b => {
            Click(b, 0, 0.22f, tone: 5200, bright: 9000, len: 0.0016f, ring: 0.7f);  // glassy UI tick
            Tone(b, 540, 0, 0.05f, Wv.Square, 0.26f, atk: 0.002f, dec: 5f, tilt: 2200f);
            Tone(b, 810, 0.012f, 0.04f, Wv.Sine, 0.18f, atk: 0.002f, dec: 6f);
        });
        // MOVE: a soft, short footfall thud (low, rounded — fires a lot, stays gentle).
        Reg("move", 0.08f, -14.5f, b => {
            Tone(b, 220, 0, 0.06f, Wv.Tri, 0.22f, slideTo: 150, atk: 0.003f, dec: 5f, tilt: 2600f);
            Noise(b, 0, 0.03f, 0.11f, fc: 900, poles: 3, bodyHz: 190, bodyQ: 1.4f, bodyMix: 0.9f);
        });
        // RELOAD: a mechanical two-click "cha-chk" (mag out, mag in + bolt).
        Reg("reload", 0.18f, -11f, b => {
            Click(b, 0, 0.32f, tone: 1900, bright: 6000, len: 0.0040f);              // mag out (dull clack)
            Tone(b, 520, 0.005f, 0.055f, Wv.Square, 0.30f, atk: 0.001f, dec: 5.5f, tilt: 2600f);
            Click(b, 0.085f, 0.36f, tone: 2600, bright: 8000, len: 0.0035f);         // mag in + bolt (brighter)
            Tone(b, 360, 0.090f, 0.065f, Wv.Square, 0.28f, atk: 0.001f, dec: 5f, tilt: 2400f);
        });
        // HUNKER: a low settling thunk (dig in).
        Reg("hunker", 0.18f, -12.5f, b => {
            Tone(b, 230, 0, 0.15f, Wv.Tri, 0.30f, slideTo: 170, atk: 0.006f, dec: 3.2f, tilt: 2200f);
            Noise(b, 0, 0.05f, 0.13f, fc: 700, poles: 3, bodyHz: 240, bodyQ: 1.2f, bodyMix: 0.8f);
        });

        // ───────── per-weapon firing voices (each short — these fire a LOT) ─────────
        // Construction pattern: a sharp transient CLICK (the mechanism) + a NOISE burst
        // (the powder crack) + a BODY tone with a quick pitch-drop (the report), tuned per
        // weapon so each is unmistakable. "shoot" stays the baseline RIFLE voice.
        Reg("shoot",   0.16f, -6f, FillRifle);
        Reg("w_rifle", 0.16f, -6f, FillRifle);
        // SHOTGUN: a fat low boom + a wide, long noise wash (heavy, blunt).
        Reg("w_shotgun", 0.24f, -5f, b => {
            Click(b, 0, 0.34f, tone: 1150, bright: 4200, len: 0.0045f);   // heavy pump action
            // the blast: a LOW wash (fc 1.3 kHz) around a wide 300 Hz chamber resonance
            Noise(b, 0, 0.20f, 0.66f, fc: 1300, poles: 3, bodyHz: 300, bodyQ: 1.5f, bodyMix: 1.5f);
            Tone(b, 110, 0, 0.18f, Wv.Saw, 0.46f, slideTo: 55, atk: 0.001f, dec: 3.0f, tilt: 1600f);
            Tone(b, 150, 0, 0.06f, Wv.Square, 0.28f, slideTo: 70, atk: 0.001f, dec: 7f, tilt: 1800f);
        });
        // SNIPER: a hard transient + a bright high crack + a long ringing metallic tail.
        Reg("w_sniper", 0.30f, -5.5f, b => {
            Click(b, 0, 0.46f, tone: 3300, bright: 12000, len: 0.0022f);  // hard, bright bolt
            // the crack: still the BRIGHTEST weapon in the game (highest centroid of the five)
            // but a BAND at 3.8 kHz, not raw white noise to 22 kHz — with a tight 1.4 kHz
            // resonance for the whip-snap. The long saw tail below is deliberately held back:
            // at its old level it dragged the sniper's centroid UNDER the shotgun's.
            Noise(b, 0, 0.055f, 0.72f, fc: 3800, poles: 2, bodyHz: 1450, bodyQ: 3.0f, bodyMix: 1.2f, dec: 5f);
            Tone(b, 1020, 0, 0.06f, Wv.Square, 0.44f, slideTo: 620, atk: 0.0006f, dec: 7f, tilt: 5200f);
            // the TAIL is what makes a sniper a sniper: a long, BRIGHT ringing wash (a tight
            // 1.1 kHz resonance) over a metallic ring and a modest low report — not the dull
            // 300->150 Hz saw that used to drag its centroid below the shotgun's.
            Noise(b, 0.03f, 0.24f, 0.34f, fc: 2800, poles: 2, bodyHz: 1150, bodyQ: 4.5f, bodyMix: 2.4f, dec: 2.6f);
            Tone(b, 880, 0.02f, 0.26f, Wv.Sine, 0.15f, slideTo: 760, atk: 0.004f, dec: 2.4f);
            Tone(b, 220, 0.01f, 0.22f, Wv.Saw, 0.16f, slideTo: 130, atk: 0.002f, dec: 2.4f, tilt: 2200f);
        });
        // SMG: a quick, bright, snappy crack (tight + punchy).
        Reg("w_smg", 0.11f, -6.5f, b => {
            Click(b, 0, 0.28f, tone: 2900, bright: 8500, len: 0.0020f);   // light, fast bolt
            Noise(b, 0, 0.06f, 0.44f, fc: 2400, poles: 3, bodyHz: 700, bodyQ: 2.8f, bodyMix: 1.3f, dec: 6f);
            Tone(b, 360, 0, 0.06f, Wv.Square, 0.30f, slideTo: 200, atk: 0.0008f, dec: 8f, tilt: 2800f);
        });
        // LMG: a heavy chug — thick low body + a long rolling noise (big bore).
        Reg("w_lmg", 0.22f, -5.4f, b => {
            Click(b, 0, 0.36f, tone: 880, bright: 3400, len: 0.0055f);    // big, dull receiver
            Noise(b, 0, 0.18f, 0.58f, fc: 1050, poles: 3, bodyHz: 240, bodyQ: 1.3f, bodyMix: 1.6f);
            Tone(b, 135, 0, 0.16f, Wv.Saw, 0.44f, slideTo: 60, atk: 0.001f, dec: 3.2f, tilt: 1500f);
            Tone(b, 90, 0, 0.12f, Wv.Square, 0.32f, slideTo: 48, atk: 0.001f, dec: 4.5f, tilt: 1400f);
        });

        // ───────── impact cues (crit layered heavier than a normal hit) ─────────
        // HIT: a meaty thump — a noise smack + a short low body.
        Reg("hit", 0.18f, -9f, b => {
            Noise(b, 0, 0.10f, 0.52f, fc: 1400, poles: 3, bodyHz: 330, bodyQ: 1.8f, bodyMix: 1.2f);
            Tone(b, 150, 0, 0.12f, Wv.Square, 0.42f, slideTo: 90, atk: 0.001f, dec: 4.5f, tilt: 1700f);
        });
        // CRIT: the HIT smack PLUS a deeper sub-bass thud + a metallic ping (lands heavier).
        Reg("crit", 0.30f, -4f, b => {
            Noise(b, 0, 0.14f, 0.58f, fc: 1800, poles: 3, bodyHz: 420, bodyQ: 2.0f, bodyMix: 1.3f);
            Tone(b, 240, 0, 0.16f, Wv.Saw, 0.40f, slideTo: 110, atk: 0.001f, dec: 3.2f, tilt: 2200f);
            Tone(b, 70, 0, 0.26f, Wv.Sine, 0.46f, slideTo: 46, atk: 0.002f, dec: 2.0f);   // sub-bass thud
            Tone(b, 1200, 0.005f, 0.06f, Wv.Sine, 0.16f, atk: 0.0006f, dec: 9f);          // bright ping
        });
        // MISS: a quick zip past the ear (high, descending, airy).
        Reg("miss", 0.14f, -12f, b => {
            // A2: this was 94.6% of its energy above 1 kHz — a raw-white-noise hiss with a
            // whistle over it. A round going past the ear is a SHORT band-limited zip, so the
            // noise is now a sweeping band and the whistle starts lower.
            Tone(b, 1150, 0, 0.11f, Wv.Sine, 0.22f, slideTo: 430, atk: 0.003f, dec: 3.5f);
            Noise(b, 0, 0.05f, 0.11f, fc: 4200, poles: 2, bodyHz: 1600, bodyQ: 2.6f, bodyMix: 1.5f, dec: 7f);
        });
        // OVERWATCH set: a tense rising two-note "ready" tone.
        Reg("over", 0.20f, -15.5f, b => {
            Tone(b, 440, 0, 0.08f, Wv.Square, 0.28f, atk: 0.004f, dec: 4f, tilt: 2000f);
            Tone(b, 660, 0.075f, 0.10f, Wv.Square, 0.24f, atk: 0.004f, dec: 3.5f, tilt: 2400f);
        });
        // DEATH: a downward collapse — a saw fall + a noise crumple, now with a REAL TAIL.
        // (A1) every layer used to end by 320ms inside a 360ms buffer: the collapse just
        // stopped dead and 40ms of digital silence followed. The sub now rings on and a
        // soft body resonance decays under it, finishing ~60ms before the buffer ends.
        Reg("death", 0.52f, -7f, b => {
            Tone(b, 260, 0, 0.32f, Wv.Saw, 0.38f, slideTo: 60, atk: 0.004f, dec: 2.2f, tilt: 1600f);
            Noise(b, 0, 0.26f, 0.32f, fc: 850, poles: 3, bodyHz: 210, bodyQ: 1.3f, bodyMix: 1.1f);
            Tone(b, 80, 0.02f, 0.42f, Wv.Sine, 0.30f, slideTo: 40, atk: 0.004f, dec: 2.1f);  // sub rings on
            Tone(b, 150, 0.05f, 0.40f, Wv.Tri, 0.11f, slideTo: 68, atk: 0.020f, dec: 2.6f);  // body resonance
        });
        // TURN: a clear rising two-note announce (the round changes hands).
        Reg("turn", 0.30f, -14f, b => {
            Tone(b, 330, 0, 0.13f, Wv.Tri, 0.30f, atk: 0.006f, dec: 3.2f, tilt: 3200f);
            Tone(b, 494, 0.11f, 0.16f, Wv.Tri, 0.27f, atk: 0.006f, dec: 2.8f, tilt: 3600f);
        });
        // WIN: a bright ascending major arpeggio (legacy cue alongside the victory stinger).
        Reg("win", 0.72f, -10f, b => Arp(b, new[] { 523, 659, 784, 1046 }, 0.11f, 0.20f, Wv.Tri, 0.30f));
        // LOSE: a sinking minor descent (legacy cue alongside the lose stinger).
        Reg("lose", 0.82f, -10f, b => Arp(b, new[] { 392, 330, 262, 196 }, 0.13f, 0.24f, Wv.Saw, 0.28f));

        // ───────── event stingers (short emphatic phrases; fired via PlayStinger) ─────────
        // KILL: a quick decisive down-flick + a noise crunch (a confirmed takedown).
        Reg("st_kill", 0.30f, -6f, b => {
            Tone(b, 587, 0, 0.07f, Wv.Square, 0.30f, atk: 0.002f, dec: 5f);
            Tone(b, 392, 0.06f, 0.13f, Wv.Saw, 0.30f, slideTo: 320, atk: 0.002f, dec: 3f);
            Noise(b, 0, 0.05f, 0.24f, fc: 1800, poles: 3, bodyHz: 420, bodyQ: 2f, bodyMix: 1.2f);
            Tone(b, 80, 0, 0.16f, Wv.Sine, 0.26f, atk: 0.003f, dec: 3f);   // small thud
        });
        // LASTKILL: the blow that clears the field — a brighter rising flourish that resolves
        // up (heavier than a plain kill, lighter than full VICTORY) over a sub thud.
        Reg("st_lastkill", 0.58f, -7.5f, b => {
            Arp(b, new[] { 523, 659, 880 }, 0.10f, 0.18f, Wv.Tri, 0.32f);
            Tone(b, 70, 0, 0.34f, Wv.Sine, 0.40f, slideTo: 52, atk: 0.004f, dec: 1.6f);  // sub thud
            Noise(b, 0, 0.05f, 0.22f, fc: 2200, poles: 3, bodyHz: 620, bodyQ: 2.2f, bodyMix: 1.2f);
        });
        // VICTORY: a fuller, longer major-add9 resolve (mission won) — arpeggio that lands on
        // a sustained tonic chord so it RESOLVES rather than just trailing off.
        Reg("st_victory", 1.05f, -8f, b => {
            Arp(b, new[] { 523, 659, 784, 1046, 1318 }, 0.11f, 0.22f, Wv.Tri, 0.28f);
            // sustained resolving C-major triad under the tail
            Tone(b, 523, 0.55f, 0.46f, Wv.Sine, 0.20f, atk: 0.02f, dec: 1.2f);
            Tone(b, 659, 0.55f, 0.46f, Wv.Sine, 0.16f, atk: 0.02f, dec: 1.2f);
            Tone(b, 784, 0.55f, 0.46f, Wv.Sine, 0.14f, atk: 0.02f, dec: 1.2f);
        });
        // LOSE: a sinking minor descent that settles on a low sustained minor chord (failed,
        // but the run goes on).
        // (A1) the chord used to end at exactly 0.95s in a 0.95s buffer — the last real sample
        // was the only non-silent tail in the game. The buffer is now longer than the chord,
        // so the resolve decays into genuine silence instead of being cut at the edge.
        Reg("st_lose", 1.30f, -8.5f, b => {
            Arp(b, new[] { 440, 349, 277, 220 }, 0.13f, 0.24f, Wv.Saw, 0.30f);
            Tone(b, 220, 0.55f, 0.65f, Wv.Sine, 0.20f, atk: 0.03f, dec: 2.6f);   // A
            Tone(b, 262, 0.55f, 0.65f, Wv.Sine, 0.15f, atk: 0.03f, dec: 2.6f);   // C (minor third)
            Tone(b, 165, 0.55f, 0.65f, Wv.Sine, 0.18f, atk: 0.03f, dec: 2.6f);   // E below
        });
        // SQUADWIPE: the run-ending gut-punch — a low ominous drop + noise wash + a dissonant
        // low tritone so it reads as final/wrong.
        Reg("st_squadwipe", 1.05f, -7.5f, b => {
            Tone(b, 196, 0, 0.55f, Wv.Saw, 0.40f, slideTo: 70, atk: 0.01f, dec: 1.3f);
            Tone(b, 98, 0, 0.75f, Wv.Sine, 0.42f, slideTo: 49, atk: 0.01f, dec: 1.0f);
            Tone(b, 138, 0.18f, 0.55f, Wv.Saw, 0.20f, atk: 0.02f, dec: 1.3f);   // tritone-ish dissonance
            Noise(b, 0.05f, 0.50f, 0.32f, fc: 800, poles: 3, bodyHz: 180, bodyQ: 1.2f, bodyMix: 1.1f);
        });
    }

    // RIFLE = the baseline firing voice (shared by "shoot" + "w_rifle"): a sharp mechanism
    // CLICK + a bright powder CRACK + a saw BODY with a quick pitch-drop, now with a tiny
    // second transient click (the action cycling) + a faint low thump for more "weight".
    static void FillRifle(float[] b)
    {
        Click(b, 0, 0.36f, tone: 2400, bright: 7000, len: 0.0028f);   // the bolt face
        Click(b, 0.012f, 0.17f, tone: 1700, bright: 5000, len: 0.0030f); // the action cycling
        // the report: fc 1.9 kHz over a 520 Hz barrel resonance — the "crack" a rifle has and
        // an SMG does not, because the resonance sits an octave lower and is broader.
        Noise(b, 0, 0.09f, 0.48f, fc: 1900, poles: 3, bodyHz: 520, bodyQ: 2.2f, bodyMix: 1.4f);
        Tone(b, 200, 0, 0.10f, Wv.Saw, 0.34f, slideTo: 95, atk: 0.0008f, dec: 4.5f, tilt: 2000f);
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
    // stinger duck envelope (see UpdateMusic): how deep, and for how long
    static float _duck, _duckDepth, _duckHold;
    static void Duck(float depth, float hold)
    {
        if (depth > _duckDepth || _duckHold <= 0f) _duckDepth = depth;
        _duckHold = MathF.Max(_duckHold, hold);
    }

    public static void PlayStinger(string which)
    {
        switch (which)
        {
            case "kill":      Play("st_kill"); Duck(0.30f, 0.18f); break;
            case "lastkill":  Play("st_lastkill"); Duck(0.55f, 0.45f); break;
            case "victory":   Play("st_victory"); Duck(0.80f, 0.95f); break;
            case "lose":      Play("st_lose"); Duck(0.80f, 1.20f); break;
            case "squadwipe": Play("st_squadwipe"); Duck(0.85f, 1.00f); break;
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
        // STINGER DUCK (A2). st_victory and st_squadwipe used to land on top of a full-volume
        // bed and fight it. The bed now steps out of the way: fast in, slow back out, so the
        // ceremony reads as the loudest thing in the room without anyone touching a fader.
        _duckHold = MathF.Max(0f, _duckHold - dt);
        float want = _duckHold > 0f ? _duckDepth : 0f;
        _duck = Util.Lerp(_duck, want, Util.Clamp(dt * (want > _duck ? 12f : 1.8f), 0f, 1f));
        float master = (Enabled ? 1f : 0f) * Util.Clamp(Display.VolMusic, 0f, 1f) * (1f - _duck);
        float ambT = master * (AmbBaseVol - AmbDuck * _intensity);   // bed quiets a touch under combat
        float combT = master * (CombMaxVol * _intensity);
        float k = Util.Clamp(dt * 2.2f, 0f, 1f);
        _ambVol = Util.Lerp(_ambVol, ambT, k);
        _combVol = Util.Lerp(_combVol, combT, k);
        Raylib.SetMusicVolume(_ambient, _ambVol);
        Raylib.SetMusicVolume(_combat, _combVol);
    }

    // ───── looping music beds ─────────────────────────────────────────────────
    //
    // LOOP-SEAM DISCIPLINE (A1's rule, kept): every partial is an INTEGER Hz and every LFO
    // completes a WHOLE number of cycles per loop, so value AND derivative match exactly at
    // the wrap. A2 adds one thing to that contract: phases are accumulated in DOUBLE and
    // wrapped mod 2*pi. The old code evaluated MathF.Sin(2*pi*f*t) with a float `t`, and at
    // 16 s / 9 kHz that argument is ~900,000 radians — float carries ~7 digits, so the phase
    // error at the end of the loop would have been ~0.06 rad. In double it is ~1e-10.
    //
    // A2 also raises the loop from 8 s (audibly repetitive inside a minute) to 16 s.
    internal const int MusicSecs = 16;

    /// The shared oscillator for every music layer: an integer-Hz sine with a double phase
    /// accumulator (exactly periodic over MusicSecs) and an optional tremolo LFO that itself
    /// completes `lfoK` whole cycles per loop. `phase01` is a 0..1 starting phase offset —
    /// legal because ANY phase is seamless when the frequency is an integer Hz.
    /// `lfoPh01` is the TREMOLO's own starting phase (0..1). Passing the same value to a whole
    /// bank makes it breathe COHERENTLY — which is how the air bed gets a slow spectral sweep
    /// instead of 170 partials each wobbling on their own and averaging out to a flat texture.
    static void Sine(float[] b, int freq, float vol, int lfoK, float depth, float phase01,
                     float lfoPh01 = -1f)
    {
        if (freq <= 0 || freq >= SR / 2 || vol <= 0f) return;
        if (lfoPh01 < 0f) lfoPh01 = phase01 * 0.5f;
        double ph = 2.0 * Math.PI * phase01, dph = 2.0 * Math.PI * freq / SR;
        double lph = 2.0 * Math.PI * lfoPh01, dlph = 2.0 * Math.PI * (lfoK / (double)MusicSecs) / SR;
        float baseAmp = 1f - depth;
        for (int i = 0; i < b.Length; i++)
        {
            float lfo = lfoK <= 0 ? 1f : baseAmp + depth * (0.5f + 0.5f * MathF.Sin((float)lph));
            b[i] += MathF.Sin((float)ph) * vol * lfo;
            ph += dph; if (ph >= 2.0 * Math.PI) ph -= 2.0 * Math.PI;
            lph += dlph; if (lph >= 2.0 * Math.PI) lph -= 2.0 * Math.PI;
        }
    }

    /// A PAD VOICE — `parts` harmonic partials over `freq`, each with its OWN LFO rate and
    /// starting phase.
    ///
    /// A2: PadTone used to be a single bare sine with one tremolo. Both beds therefore
    /// measured as two or three thin lines on a black spectrogram: a chord of pure tones with
    /// literal silence between them, which reads to the ear as "the audio is off" rather than
    /// as an atmosphere. A harmonic stack with independent LFOs gives each note a timbre and
    /// makes the stack breathe instead of sit still.
    static void PadTone(float[] b, int freq, float vol, int lfoK, int parts = 4, float tilt = 0.85f)
    {
        for (int k = 1; k <= parts; k++)
        {
            int f = freq * k;
            if (f >= SR / 2) break;
            float a = vol * MathF.Pow(k, -tilt);
            // each partial gets a DIFFERENT integer LFO rate -> the composite amplitude never
            // repeats inside the loop, and higher partials breathe harder (they are the "air")
            int kk = lfoK + (k - 1) * 2;
            float depth = MathF.Min(0.62f, 0.24f + 0.09f * k);
            Sine(b, f, a, kk, depth, ((k * 0.37f) + lfoK * 0.11f) % 1f);
        }
    }

    // a soft high "shimmer" — two integer freqs a few Hz apart that beat slowly for movement.
    static void Shimmer(float[] b, int freq, float vol, int lfoK)
    {
        Sine(b, freq, vol * 0.5f, 1, 0.4f, 0.13f);
        Sine(b, freq + lfoK, vol * 0.5f, 1, 0.4f, 0.61f);
    }

    /// A LOOP-SEAMLESS BAND-LIMITED NOISE BED.
    ///
    /// Real white noise cannot loop — its last sample has no relationship to its first, so a
    /// dropped-in noise layer would put a click at every wrap and blow the seam gate. This is
    /// a bank of `count` sines at distinct INTEGER Hz spread log-uniformly over [loHz,hiHz]
    /// with fixed-seed pseudo-random phases: mathematically periodic at MusicSecs to the
    /// sample (value AND derivative match), while sounding like filtered noise because ~10
    /// partials land in every critical band. Amplitudes follow a 1/sqrt(f) (pink-ish) tilt and
    /// are scaled so the bank's total RMS is exactly `rms`.
    ///
    /// This is the layer that stops the beds being black rectangles: it is broadband, it is
    /// ABOVE 1 kHz, and it fills the silence between the chord partials with air.
    static void NoiseBed(float[] b, int loHz, int hiHz, float rms, int count, int seed, int lfoK,
                         float lfoPh01 = -1f, float depth = 0.35f)
    {
        var rng = new Random(seed);
        var used = new HashSet<int>();
        var fs = new List<int>();
        var amps = new List<float>();
        double lnLo = Math.Log(loHz), lnHi = Math.Log(hiHz);
        double sumSq = 0;
        for (int i = 0; i < count; i++)
        {
            // log-uniform placement + a jitter cell so the bank has no audible comb
            double u = (i + rng.NextDouble()) / count;
            int f = (int)Math.Round(Math.Exp(lnLo + u * (lnHi - lnLo)));
            while (!used.Add(f)) f++;
            if (f >= SR / 2) break;
            // 1/sqrt(f) pink tilt, then an extra 6 dB/oct roll above 5 kHz. Without that roll
            // the bank ends in a BRICK WALL at hiHz — visible on the contact sheet as a hard
            // horizontal edge and audible as a shelf of hiss rather than as air.
            float a = 1f / MathF.Sqrt(f) / MathF.Sqrt(1f + (f / 5000f) * (f / 5000f));
            fs.Add(f); amps.Add(a);
            sumSq += 0.5 * a * a;                    // incoherent sine power
        }
        if (fs.Count == 0 || sumSq <= 0) return;
        float g = (float)(rms / Math.Sqrt(sumSq));
        for (int i = 0; i < fs.Count; i++)
            Sine(b, fs[i], amps[i] * g, lfoK, depth, (float)rng.NextDouble(), lfoPh01);
    }

    /// A repeating plucked pulse at `rate` Hz (rate*MusicSecs must be a whole number of hits).
    ///
    /// A2: this used to be sin(2*pi*f*t) * exp(-8*phase) — a sine fading in from ZERO amplitude
    /// with no attack at all, i.e. a "boop". A percussive hit needs a transient: a fast attack
    /// ramp (so the onset is a step, not a swell) and high partials that decay far faster than
    /// the body. `clickHz` is the mechanism's own tick over the top. Everything is an integer
    /// Hz and the envelope is a function of the pulse phase only, so the loop stays seamless.
    static void Pulse(float[] b, int freq, float rate, float vol, int clickHz = 0)
    {
        double ph = 0, dph = 2.0 * Math.PI * freq / SR;
        double ph4 = 0, dph4 = 2.0 * Math.PI * (freq * 4) / SR;
        double ph7 = 0, dph7 = 2.0 * Math.PI * (freq * 7) / SR;
        double phc = 0, dphc = 2.0 * Math.PI * Math.Max(0, clickHz) / SR;
        const float AtkSecs = 0.0018f;
        // The hit phase is derived from the INTEGER sample index, not from `(t*rate) % 1f`.
        // Near the end of a 16 s loop t*rate is ~32, and a float subtracting 32 from 32.0018
        // has lost five digits — which is exactly the kind of sub-audible non-periodicity the
        // AUDIOGATE periodicity probe caught (1.4e-4 at the first pulse onset after the wrap).
        // `hits` divides the loop by contract, so `i % hitLen` is exact and repeats forever.
        int hits = Math.Max(1, (int)MathF.Round(rate * MusicSecs));
        int hitLen = Math.Max(1, MusicSecs * SR / hits);
        for (int i = 0; i < b.Length; i++)
        {
            int hs = i % hitLen;                              // samples since this hit's onset
            float pp = hs / (float)hitLen;                    // 0..1 within this hit
            float atk = MathF.Min(1f, (hs / (float)SR) / AtkSecs); // fast, but not a discontinuity
            float bodyE = atk * MathF.Exp(-8f * pp);
            float transE = atk * MathF.Exp(-70f * pp);        // the ONSET — gone in ~5 ms
            float s = MathF.Sin((float)ph) * bodyE;
            s += (MathF.Sin((float)ph4) * 0.42f + MathF.Sin((float)ph7) * 0.26f) * transE;
            if (clickHz > 0) s += MathF.Sin((float)phc) * 0.34f * transE;
            b[i] += s * vol;
            ph += dph; if (ph >= 2.0 * Math.PI) ph -= 2.0 * Math.PI;
            ph4 += dph4; if (ph4 >= 2.0 * Math.PI) ph4 -= 2.0 * Math.PI;
            ph7 += dph7; if (ph7 >= 2.0 * Math.PI) ph7 -= 2.0 * Math.PI;
            phc += dphc; if (phc >= 2.0 * Math.PI) phc -= 2.0 * Math.PI;
        }
    }

    // AMBIENT: an A-minor add9 pad (A C E B) voiced as HARMONIC STACKS rather than bare sines,
    // over a wide air bed — atmospheric, never fatiguing under the whole game.
    internal static byte[] BuildAmbient() => EncodeWav(Bed("ambient"), 1f);
    static float[] AmbientFloats(int n = 0)
    {
        var b = new float[n > 0 ? n : MusicSecs * SR];
        // low/mid chord — the fundamentals are deliberately restrained (a laptop speaker rolls
        // off hard under ~300 Hz and simply loses them; the harmonics carry the note instead)
        PadTone(b, 110, 0.095f, 1, parts: 6, tilt: 0.62f);   // A2 root
        PadTone(b, 165, 0.074f, 2, parts: 6, tilt: 0.66f);   // E3 fifth
        PadTone(b, 220, 0.066f, 3, parts: 5, tilt: 0.70f);   // A3
        PadTone(b, 262, 0.051f, 5, parts: 5, tilt: 0.74f);   // C4 minor third
        PadTone(b, 330, 0.039f, 4, parts: 4, tilt: 0.78f);   // E4
        PadTone(b, 494, 0.031f, 7, parts: 4, tilt: 0.82f);   // B4 add9
        // upper voicing — the chord an octave or two up, where a small speaker actually lives
        PadTone(b, 880, 0.055f, 3, parts: 3, tilt: 0.90f);   // A5
        PadTone(b, 1320, 0.058f, 6, parts: 2, tilt: 0.95f);  // E6
        PadTone(b, 1760, 0.048f, 8, parts: 2, tilt: 1.00f);  // A6
        Shimmer(b, 2093, 0.040f, 2);                         // C7 beating air
        Shimmer(b, 2640, 0.032f, 3);                         // E7 beating air
        Shimmer(b, 3520, 0.022f, 5);                         // A7 beating air
        // The AIR BED — see NoiseBed. Silence between the partials is what read as "the audio
        // is off". Deliberately kept as a FLOOR (~-30 dBFS inside the bed) rather than as the
        // source of the brightness: most of the top above is TONAL (chord tones an octave or
        // two up), because a bed that clears a brightness gate on broadband noise alone is
        // just hiss with a chord under it.
        // Split into two COHERENT, ANTI-PHASE bands: the air's centre of gravity sweeps up and
        // down once per loop, which is the only large-scale movement a sustained bed can have.
        // One incoherent bank of 170 partials averages out to a flat, dead texture.
        NoiseBed(b, 900, 3400, 0.021f, 70, 0x5A17A1, 1, lfoPh01: 0.00f, depth: 0.55f);
        NoiseBed(b, 3400, 14000, 0.021f, 100, 0x5A17A3, 1, lfoPh01: 0.50f, depth: 0.55f);
        NoiseBed(b, 240, 900, 0.016f, 40, 0x5A17A2, 3);      // a low "room" floor under it
        return b;
    }

    // COMBAT: a darker, tenser bed (A C Eb — minor with a flat-five bite) over a DRIVING
    // sub-bass pulse + a faster mid pulse, so it reads as urgent without being loud.
    internal static byte[] BuildCombat() => EncodeWav(Bed("combat"), 1f);
    static float[] CombatFloats(int n = 0)
    {
        var b = new float[n > 0 ? n : MusicSecs * SR];
        PadTone(b, 110, 0.088f, 1, parts: 6, tilt: 0.60f);   // root
        PadTone(b, 156, 0.069f, 2, parts: 6, tilt: 0.64f);   // ~Eb3 (flat-five tension)
        PadTone(b, 220, 0.058f, 3, parts: 5, tilt: 0.70f);   // A3
        PadTone(b, 311, 0.040f, 5, parts: 4, tilt: 0.76f);   // ~Eb4 tension up top
        PadTone(b, 622, 0.035f, 4, parts: 3, tilt: 0.86f);   // ~Eb5
        PadTone(b, 1244, 0.066f, 6, parts: 2, tilt: 0.95f);  // ~Eb6 — the flat-five, up two 8ves
        PadTone(b, 1760, 0.042f, 8, parts: 2, tilt: 1.00f);  // A6
        Shimmer(b, 3136, 0.030f, 4);                         // hard, bright air
        Shimmer(b, 2488, 0.024f, 6);                         // ~Eb7 — the tension, up top
        // the drive — raised so the hits still PUNCH through the air bed (at the first air
        // level the pulses vanished into it; on the contact sheet the vertical strikes had
        // gone from clearly visible to invisible)
        Pulse(b, 55, 2f, 0.30f, clickHz: 1650);              // sub-bass pulse (32 hits / loop)
        Pulse(b, 110, 2f, 0.14f);                            // octave reinforcement
        Pulse(b, 220, 4f, 0.085f, clickHz: 2200);            // faster mid tick (64/loop)
        Pulse(b, 1320, 4f, 0.072f);                          // bright top-end tick
        // air — tighter and harsher than the ambient bed's, so it reads as urgency
        // same anti-phase sweep, but twice as fast — restlessness rather than drift
        NoiseBed(b, 1100, 3800, 0.020f, 70, 0x5A17C1, 2, lfoPh01: 0.00f, depth: 0.55f);
        NoiseBed(b, 3800, 14000, 0.020f, 100, 0x5A17C3, 2, lfoPh01: 0.50f, depth: 0.55f);
        NoiseBed(b, 260, 1100, 0.013f, 40, 0x5A17C2, 5);
        return b;
    }

    // A2: the beds are now ~190 oscillators over a 16 s buffer, so synthesising one is real
    // work (~134M sine evaluations). Init, SelfTest, AUDIODUMP and AUDIOGATE all ask for the
    // same bytes repeatedly — cache the mastered float buffer and hand out copies. Purely a
    // speed cache: the synthesis is deterministic (fixed-seed phases), so the bytes are
    // identical whether they come from the cache or a fresh render.
    static readonly Dictionary<string, float[]> _bedCache = new();
    static float[] Bed(string which)
    {
        if (_bedCache.TryGetValue(which, out var c)) return c;
        var b = RenderMusicGain(which == "combat" ? CombatFloats() : AmbientFloats());
        _bedCache[which] = b;
        return b;
    }


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
    internal static float[] RenderMusic(string which) => (float[])Bed(which).Clone();

    /// PRE-GAIN bed samples at an ARBITRARY length — the hook the periodicity proof needs.
    ///
    /// Every music generator is a pure function of the sample index (fixed start phases,
    /// double accumulators, envelopes that depend only on the loop phase), so asking for
    /// MusicSecs*SR + K samples returns the loop PLUS its own genuine continuation. If the
    /// bed is truly periodic, those K extra samples must equal the first K, sample for
    /// sample. That is a proof of seamlessness rather than a heuristic about step sizes —
    /// see the AUDIOGATE "periodicity" check.
    internal static float[] BedRaw(string which, int n)
        => which == "combat" ? CombatFloats(n) : AmbientFloats(n);

    // ── PLAY ──────────────────────────────────────────────────────────────────

    /// FNV-1a over a cue id. String.GetHashCode is per-process randomised in .NET Core, so it
    /// cannot seed anything that has to be reproducible across runs.
    static uint IdHash(string id)
    {
        uint h = 2166136261u;
        foreach (char c in id) { h ^= c; h *= 16777619u; }
        return h;
    }

    /// A 32-bit integer hash (lowbias32). Deterministic, but SCRAMBLED — see Play.
    static float Hash01(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352du;
        x ^= x >> 15; x *= 0x846ca68bu;
        x ^= x >> 16;
        return (x & 0xFFFFFFu) / (float)0x1000000;
    }

    /// The per-cue DEFAULT variation depth. Only 4 of ~134 call sites ever passed `pitchVar`,
    /// so every UI cue in the game was byte-identical forever; pushing a sensible default in
    /// here means every call site gets variation for free. Ceremonial cues (the stingers, the
    /// win/lose jingles) deliberately get none — a victory fanfare that detunes is a bug.
    static float DefaultPitchVar(string id) => CatOf(id) switch
    {
        "weapon" => 0.055f,
        "impact" => 0.050f,
        "crit"   => 0.035f,
        "move"   => 0.070f,
        "ui"     => 0.022f,
        _        => 0f,          // stinger / win / lose: no detune, ever
    };
    /// Per-call gain jitter DEPTH in dB — and it is deliberately ONE-SIDED DOWNWARD.
    /// A symmetric +-1.5 dB would let a cue land 1.5 dB HOTTER than the level A1's per-cue
    /// peak targets were budgeted against, which is exactly the headroom the concurrent-stack
    /// check spends (the worst stack already measures -1.4 dBFS). Jittering only downward
    /// gives the same "these two shots have different weight" effect and cannot cost a decibel
    /// of headroom, so the audio budget stays true whether or not the limiter is engaged.
    static float DefaultGainVarDb(string id) => CatOf(id) == "stinger" ? 0f : 2.4f;

    /// Play a sound.
    ///
    /// `pitchVar` = detune depth; -1 (the default) means "use this cue's DefaultPitchVar",
    /// and an explicit 0 opts out. `panX` (0..1 = screen-x; <0 = centred) pans in stereo.
    ///
    /// A2 fixed the "randomisation". It used to be `_pitchSeq & 7` mapped linearly to
    /// [-1,1], i.e. the multipliers 0.940, 0.957, 0.974, 0.991, 1.009, 1.026, 1.043, 1.060
    /// and then a wrap — a MONOTONE RISING GLISSANDO. Perceptually that is a siren, not
    /// randomisation, and it is at its most obvious exactly where it matters most (automatic
    /// fire). The offset is now hash-scrambled off a counter, still fully deterministic (the
    /// headless harness needs reproducibility) but with no audible order, and gain gets its
    /// own independent jitter so repeated shots differ in weight as well as pitch.
    public static void Play(string id, float pitchVar = -1f, float panX = -1f)
    {
        if (!_ready || !Enabled) return;
        if (!_voices.TryGetValue(id, out var v) || v.Ring == null) return;

        // round-robin to a FREE voice; if every voice is busy, steal the oldest in the ring
        Sound s = v.Ring[v.Next];
        for (int i = 0; i < v.Ring.Length; i++)
        {
            int k = (v.Next + i) % v.Ring.Length;
            if (!Raylib.IsSoundPlaying(v.Ring[k])) { s = v.Ring[k]; v.Next = (k + 1) % v.Ring.Length; goto picked; }
        }
        Raylib.StopSound(s);
        v.Next = (v.Next + 1) % v.Ring.Length;
    picked:
        uint h = IdHash(id) ^ (_varSeq++ * 2654435761u);
        float pv = pitchVar < 0f ? DefaultPitchVar(id) : pitchVar;
        Raylib.SetSoundPitch(s, pv > 0f ? 1f + pv * (Hash01(h) * 2f - 1f) : 1f);

        float gdb = DefaultGainVarDb(id);
        float gain = BusVol(id);
        if (gdb > 0f) gain *= MathF.Pow(10f, -gdb * Hash01(h * 2246822519u + 1u) / 20f);   // [-gdb, 0] dB
        Raylib.SetSoundVolume(s, Util.Clamp(gain, 0f, 1f));

        // Raylib pan: 0.5 = centre, 0 = right, 1 = left. Map screen-x so left of screen
        // pans left: panLeftFraction = 1 - screenXFraction.
        Raylib.SetSoundPan(s, panX >= 0f ? Util.Clamp(1f - panX, 0f, 1f) : 0.5f);
        Raylib.PlaySound(s);
    }

    // ── MASTER LIMITER ────────────────────────────────────────────────────────
    // A1 bought stack headroom by trimming the per-cue peak targets; that is a budget, not a
    // guarantee, and it stops being true the moment the owner pushes the new master fader up.
    // A limiter on the mixed bus makes the ceiling STRUCTURAL: whatever the game throws at the
    // mixer, and wherever the faders sit, nothing leaves the pipeline over -1 dBFS.
    //
    // Peak follower with a ~1 ms attack and a ~150 ms release, then a cubic soft-clip as the
    // last line (a fast limiter without lookahead always lets a little transient through, and
    // a rounded knee is far less audible than a hard digital clip).

    const float LimCeil = 0.891f;         // -1.0 dBFS
    static float _limEnv;                 // audio-thread only
    static bool _limOn;

    static float SoftClip(float x)
        => x <= -1.5f ? -1f : x >= 1.5f ? 1f : x - x * x * x * (1f / 6.75f);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static unsafe void MixProcessor(void* buffer, uint frames)
    {
        // raylib hands the whole mixed bus over as interleaved stereo float32
        const float Atk = 0.022f;         // ~1 ms  at 44.1 kHz
        const float Rel = 0.00015f;       // ~150 ms
        float* f = (float*)buffer;
        float env = _limEnv;
        for (uint i = 0; i < frames; i++)
        {
            float l = f[i * 2], r = f[i * 2 + 1];
            float pk = MathF.Max(MathF.Abs(l), MathF.Abs(r));
            env += (pk - env) * (pk > env ? Atk : Rel);
            float g = env > LimCeil ? LimCeil / env : 1f;
            f[i * 2] = SoftClip(l * g);
            f[i * 2 + 1] = SoftClip(r * g);
        }
        _limEnv = float.IsNaN(env) || float.IsInfinity(env) ? 0f : env;
    }

    static unsafe void AttachLimiter()
    {
        if (!_ready || _limOn) return;
        try { Raylib.AttachAudioMixedProcessor(&MixProcessor); _limOn = true; }
        catch { _limOn = false; }        // binding/backend refused it: the budget still holds
    }

    static unsafe void DetachLimiter()
    {
        if (!_limOn) return;
        try { Raylib.DetachAudioMixedProcessor(&MixProcessor); } catch { }
        _limOn = false;
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
        DetachLimiter();
        // aliases first — they share the base sound's sample data, so unloading the base out
        // from under a live alias is a use-after-free.
        foreach (var v in _voices.Values)
            for (int i = 1; i < v.Ring.Length; i++)
                if (!v.Ring[i].Equals(v.Ring[0])) { try { Raylib.UnloadSoundAlias(v.Ring[i]); } catch { } }
        _voices.Clear();
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
    internal static float[] RenderCue(string id, float dur, float targetDb, Action<float[]> fill)
    {
        SeedFor(id);                       // reproducible noise/click for this cue
        var buf = BuildBuffer(dur, fill);
        DcBlock(buf);                      // 20 Hz one-pole HPF — see below
        NormalizeTo(buf, targetDb);        // MASTERING: unconditional, to the designed level
        return buf;
    }

    /// A 20 Hz one-pole DC-blocking highpass. The layered saw/square voices sum to a small
    /// but consistently POSITIVE bias (w_lmg measured +0.0037): a DC offset costs headroom
    /// on every stack it takes part in and thumps the speaker on cue start/stop. 20 Hz is
    /// below anything the synth deliberately produces, so nothing audible is touched.
    static void DcBlock(float[] b)
    {
        const float Fc = 20f;
        float r = MathF.Exp(-2f * MathF.PI * Fc / SR);   // ~0.99715
        float x1 = 0f, y1 = 0f;
        for (int i = 0; i < b.Length; i++)
        {
            float x = b[i];
            float y = x - x1 + r * y1;
            x1 = x; y1 = y;
            b[i] = y;
        }
    }

    /// MASTERING. The old code was a CLIP GUARD, not a mixer: `g = peak > 1 ? 1/peak : 1`
    /// only ever fired for one cue (crit), so 22 of 23 cues shipped at whatever level their
    /// layer amplitudes happened to sum to — a 16.2 dB RMS spread nobody designed, with the
    /// heaviest hit in the game only 2.0 dB over a normal one. This normalises EVERY cue to
    /// its registered target, which makes the per-cue target column the actual mix decision.
    static void NormalizeTo(float[] b, float targetDb)
    {
        float peak = 0f;
        for (int i = 0; i < b.Length; i++) peak = MathF.Max(peak, MathF.Abs(b[i]));
        if (peak < 1e-6f) return;                       // silent buffer: nothing to scale
        float g = MathF.Pow(10f, targetDb / 20f) / peak;
        for (int i = 0; i < b.Length; i++) b[i] *= g;
    }

    // Load a single recipe into a device Sound (device path only).
    static void LoadRecipe(string id, float dur, float targetDb, Action<float[]> fill)
    {
        byte[] wav = EncodeWav(RenderCue(id, dur, targetDb, fill), 1f);
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
                var (dur, target, fill) = kv.Value;
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

    /// Validate a music bed: the encoded WAV is real, and the loop is SEAMLESS.
    ///
    /// A2 REPLACED THE ENDPOINT TEST. This used to require |first| and |last| to both be near
    /// zero in 16-bit units ("both endpoints should be near zero crossings for a clean loop").
    /// That is the same naive-absolute trap A1 documented for the seam metric: a loop is
    /// seamless when b[0] CONTINUES from b[n-1], not when both happen to sit at zero. The old
    /// beds passed it only because every partial was a zero-phase sine over an integer-second
    /// buffer, which forced the endpoints to zero as a side effect. The A2 beds use per-partial
    /// phase offsets (legal — any phase is seamless at integer Hz) and so wrap at an arbitrary
    /// value; the ambient bed's first sample is -0.161, and it is provably click-free.
    ///
    /// What is checked instead is periodicity itself: every generator is a pure function of
    /// the sample index, so a bed rendered to N+probe samples must repeat its own first
    /// `probe` samples exactly. This is strictly stronger — it caught a 1.4e-4 float-precision
    /// break in Pulse()'s phase that the endpoint test sailed straight past.
    static string ValidateMusic(string id, byte[] wavBytes)
    {
        if (wavBytes == null || wavBytes.Length <= 44) return $"'{id}' empty/short WAV ({wavBytes?.Length ?? 0} bytes)";
        int n = (wavBytes.Length - 44) / 2;
        if (n < 2) return $"'{id}' too few samples";
        if (n != MusicSecs * SR) return $"'{id}' wrong length ({n} samples, expected {MusicSecs * SR})";

        int len = MusicSecs * SR;
        var ext = BedRaw(id, len + SeamProbe);
        float worst = 0f; int worstI = 0;
        for (int i = 0; i < SeamProbe; i++)
        {
            float d = MathF.Abs(ext[len + i] - ext[i]);
            if (d > worst) { worst = d; worstI = i; }
        }
        if (worst > MaxPeriodErr)
            return $"'{id}' loop is not periodic: |bed[N+{worstI}]-bed[{worstI}]| = {worst:0.0000000} " +
                   $"(> {MaxPeriodErr:0.0000}) — would click at the wrap";
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

    // ── RESONANCE A2 "THE VOICE" — the filter bench ───────────────────────────
    // A1 fixed LEVELS. This fixes VOICING. The old Noise() took an `lp` parameter that it
    // used DIRECTLY as a one-pole coefficient (alpha = clamp(lp, 0.02, 1)), so the implied
    // cutoffs were absurd: lp 0.8 -> 11.3 kHz, lp 1.0 -> no filtering at all. Every weapon
    // was therefore a flat broadband rectangle running to 22 kHz (w_smg measured 69.5% of
    // its energy above 1 kHz) — hiss, not gunfire. Real firearm energy concentrates roughly
    // 100 Hz - 2 kHz behind a sub-millisecond transient.
    //
    // So: `fc` is now a CUTOFF IN HZ with the correct one-pole coefficient, cascaded for a
    // real 12-18 dB/oct slope, plus a resonant band-pass "body" so a shotgun, a rifle and an
    // SMG are different OBJECTS rather than the same noise at different lengths.

    /// One-pole low-pass coefficient for a cutoff in Hz (the formula the old code was missing).
    static float PoleA(float fc) => 1f - MathF.Exp(-2f * MathF.PI * Util.Clamp(fc, 20f, SR * 0.49f) / SR);

    // Filtering a white-noise source costs level, and how much depends on the cutoff — which
    // would silently rescale every layer's `vol` against every other. These makeups restore
    // unit output RMS for a unit-variance white input (Parseval over the impulse response), so
    // a recipe's `vol` numbers keep meaning "how loud is this layer" after a re-voicing.
    static readonly Dictionary<long, float> _mkCache = new();
    static float LpMakeup(float a, int poles)
    {
        long key = ((long)BitConverter.SingleToInt32Bits(a) << 8) | (uint)poles;
        if (_mkCache.TryGetValue(key, out var c)) return c;
        double e = 0; var z = new double[poles];
        for (int i = 0; i < 8192; i++)
        {
            double v = i == 0 ? 1.0 : 0.0;
            for (int p = 0; p < poles; p++) { z[p] += a * (v - z[p]); v = z[p]; }
            e += v * v;
        }
        float g = e > 1e-12 ? (float)(1.0 / Math.Sqrt(e)) : 1f;
        _mkCache[key] = g;
        return g;
    }

    /// RBJ band-pass (constant 0 dB peak gain) coefficients + the same unit-RMS makeup.
    static (float b0, float b2, float a1, float a2, float mk) Bp(float f0, float q)
    {
        float w0 = 2f * MathF.PI * Util.Clamp(f0, 20f, SR * 0.45f) / SR;
        float alpha = MathF.Sin(w0) / (2f * MathF.Max(0.2f, q));
        float a0 = 1f + alpha;
        float b0 = alpha / a0, b2 = -alpha / a0;
        float a1 = -2f * MathF.Cos(w0) / a0, a2 = (1f - alpha) / a0;
        // impulse-response energy -> unit-RMS makeup
        double e = 0, x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < 8192; i++)
        {
            double x = i == 0 ? 1.0 : 0.0;
            double y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            e += y * y;
        }
        float mk = e > 1e-12 ? (float)(1.0 / Math.Sqrt(e)) : 1f;
        return (b0, b2, a1, a2, mk);
    }

    // A pitched tone. `slideTo>0` glides freq->slideTo across the tone (pitch envelope).
    // `atk` = attack time in SECONDS; `dec` = exponential decay rate.
    //
    // A2: `tilt` is a one-pole-pair low-pass in Hz over the OSCILLATOR OUTPUT. Shape()'s naive
    // Square/Saw carry an unrolled 1/n harmonic series all the way to Nyquist — that hard comb
    // to 22 kHz IS the cheap-chiptune buzz on select/move/turn. -1 = pick a sensible default
    // per waveform (square is the buzziest, sine needs nothing); 0 = bypass.
    static void Tone(float[] b, float freq, float start, float dur, Wv type, float vol,
                     float slideTo = 0, float atk = 0.004f, float dec = 3.5f, float tilt = -1f)
    {
        int n0 = (int)(start * SR);
        int len = (int)(dur * SR);
        if (len < 1) return;
        if (tilt < 0f) tilt = type switch { Wv.Square => 3000f, Wv.Saw => 4200f, Wv.Tri => 11000f, _ => 0f };
        bool filt = tilt > 0f;
        float a = filt ? PoleA(tilt) : 0f;
        float z0 = 0f, z1 = 0f;
        float atkFrac = (atk * SR) / len;                 // attack as a fraction of the tone
        float ph = 0f;
        for (int i = 0; i < len; i++)
        {
            int idx = n0 + i;
            if (idx >= b.Length) break;
            float t = i / (float)len;
            float f = slideTo > 0 ? Util.Lerp(freq, slideTo, t) : freq;
            ph += 2f * MathF.PI * f / SR;
            float s = Shape(type, ph);
            if (filt) { z0 += a * (s - z0); z1 += a * (z0 - z1); s = z1; }
            b[idx] += s * vol * Env(t, atkFrac, dec);
        }
    }

    /// A noise burst — the powder crack of a weapon, the smack of an impact, the scuff of a
    /// footfall. `fc` = low-pass cutoff IN HZ over `poles` cascaded one-poles (6 dB/oct each);
    /// `bodyHz`/`bodyQ` add a resonant band-pass layer mixed in at `bodyMix` (a ratio of RMS,
    /// because both paths are makeup-normalised) — that resonance is what gives a burst a
    /// SIZE, i.e. what makes a 12-gauge and a 9mm different objects instead of two hisses.
    static void Noise(float[] b, float start, float dur, float vol,
                      float fc = 16000f, int poles = 3,
                      float bodyHz = 0f, float bodyQ = 2f, float bodyMix = 0f,
                      float dec = 4.5f)
    {
        int n0 = (int)(start * SR);
        int len = (int)(dur * SR);
        if (len < 1) return;
        poles = Math.Clamp(poles, 1, 4);
        int atk = Math.Max(1, (int)(0.0015f * SR));
        float a = PoleA(fc);
        float mk = LpMakeup(a, poles);
        var z = new float[4];
        bool body = bodyMix > 0f && bodyHz > 0f;
        var (pb0, pb2, pa1, pa2, pmk) = body ? Bp(bodyHz, bodyQ) : (0f, 0f, 0f, 0f, 0f);
        float x1 = 0f, x2 = 0f, y1 = 0f, y2 = 0f;
        // both paths are unit-RMS, so keep the SUM unit-RMS too (incoherent sum)
        float norm = body ? 1f / MathF.Sqrt(1f + bodyMix * bodyMix) : 1f;
        for (int i = 0; i < len; i++)
        {
            int idx = n0 + i;
            if (idx >= b.Length) break;
            float t = i / (float)len;
            float raw = AudRandF() * 2f - 1f;
            float v = raw;
            for (int p = 0; p < poles; p++) { z[p] += a * (v - z[p]); v = z[p]; }
            float s = v * mk;
            if (body)
            {
                float y = pb0 * raw + pb2 * x2 - pa1 * y1 - pa2 * y2;
                x2 = x1; x1 = raw; y2 = y1; y1 = y;
                s += y * pmk * bodyMix;
            }
            float ae = MathF.Min(1f, i / (float)atk);     // attack
            float env = ae * MathF.Exp(-dec * t);
            float rel = t > 0.9f ? (1f - t) / 0.1f : 1f;  // release fade
            b[idx] += s * norm * vol * env * MathF.Max(0f, rel);
        }
    }

    /// The mechanical transient at the head of a cue — a gun's action, a UI tick.
    ///
    /// A2: this used to be 2.5 ms of UNFILTERED white noise, byte-identical at the head of
    /// every weapon AND every UI cue. One shared transient across fourteen cues is a big part
    /// of why nothing sounded like a distinct object. It is now voiced: `bright` low-passes the
    /// noise (a UI tick is glassy, an LMG's action is dull), and `tone` adds the mechanism's
    /// own damped resonance — a rifle bolt rings around 2.4 kHz, a shotgun pump around 1.1 kHz.
    static void Click(float[] b, float start, float vol,
                      float tone = 0f, float bright = 14000f, float len = 0.0025f, float ring = 0.9f)
    {
        int n0 = (int)(start * SR);
        int n = Math.Max(2, (int)(len * SR));
        float a = PoleA(bright);
        float mk = LpMakeup(a, 2);
        float z0 = 0f, z1 = 0f, ph = 0f;
        for (int i = 0; i < n; i++)
        {
            int idx = n0 + i;
            if (idx >= b.Length) break;
            float t = i / (float)n;
            float env = MathF.Exp(-22f * t) * (1f - t);   // fast decay + linear release
            float raw = AudRandF() * 2f - 1f;
            z0 += a * (raw - z0); z1 += a * (z0 - z1);
            float s = z1 * mk;
            if (tone > 0f)
            {
                ph += 2f * MathF.PI * tone / SR;
                s += MathF.Sin(ph) * ring * MathF.Exp(-5f * t);
            }
            b[idx] += s * vol * env;
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
