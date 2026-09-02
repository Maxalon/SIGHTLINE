using System;
using System.Collections.Generic;
using Raylib_cs;

namespace Sightline;

// ═══════════════════════════════════════════════════════════════════════════════════════════
//  AUDIO CHECK — PROGRAM RESONANCE, WAVE A3.  The last mile of the audio program.
//
//  A1 built the EAR (a device-free measurement rig over the exact float samples the synth hands
//  Raylib) and A2 rebuilt the mix against those numbers. Both were right and both were BLIND:
//  every judgement in the program so far is spectral, because this sandbox has no audio device
//  and the owner — the only person in the project with a speaker — never had a fast way to sit
//  down and sweep the whole layer.
//
//  This screen is the instrument, not the taste. It exists so that a two-minute pass produces a
//  FILEABLE report instead of a vague impression: every cue on demand, singly and as a burst
//  (repeated fire is exactly where a bad sound reveals itself), the two music beds with a
//  hand-swept crossfade, the four mix faders live while cues play, the realistic concurrent
//  stacks AUDIOGATE is written against, and — beside every row — the numbers A1's rig already
//  computes. "That sounds thin" becomes "that sounds thin AND it measures -31 dB RMS with 9%
//  above 1 kHz", which is a bug report rather than a mood.
//
//  DEVICE-FREE-SAFE. Audio.Init returns early with no device, so every Play here is a silent
//  no-op; the screen says so on its face (see Hud.DrawAudition's status plate) instead of
//  looking broken. Nothing on this screen touches run/meta/save state.
//
//  NO NEW GetTime() READS: the screen runs on its own dt accumulator (AudClock), the Fx pattern.
// ═══════════════════════════════════════════════════════════════════════════════════════════
public partial class Game
{
    // ── audition state (all presentation; nothing here is persisted) ─────────────────────
    /// Seconds since the screen was entered. Drives the scheduler, the row flashes and the
    /// backdrop — a dt accumulator, never Raylib.GetTime.
    public float AudClock;
    /// Pending plays: (absolute AudClock time, cue id). A burst and a stack are both just
    /// several entries in here, which is what lets the voice-pool round-robin be heard.
    readonly List<(float At, string Id)> _audQ = new();
    /// Per-cue "just fired" glow, 1 -> 0. A stack lights every row it touches at once.
    public readonly Dictionary<string, float> AudFlash = new();
    /// One line of feedback under the title: what the last press actually fired.
    public string AudLast = "";
    Phase _audPrior;                 // where BACK returns to
    bool _audFromPause;              // opened over the pause overlay: restore it on exit
    int _audVolDrag = -1;            // mix fader being dragged (index into Hud.AudVol), -1 = none
    bool _audMusicDrag;              // the intensity slider is being dragged
    float _audPriorIntensity;        // music intensity as found, restored on exit
    int _audWarm;                    // how many cue measurements have been computed so far
    bool _audHoldFlash;              // shot harness only: hold the "just played" glows for a fixed frame
    bool _audVolTouched;             // a fader actually moved on this visit (only then is display.json written)

    /// How many cue measurements to compute per frame while the screen warms up. Rendering a cue
    /// through the real mastering stage and running a 4096-pt Welch spectrum over it is ~10 ms;
    /// doing all 23 on entry would be a visible hitch on the owner's machine, so the table fills
    /// in over the first half-second and rows read "..." until their row is measured.
    const int AudWarmPerFrame = 3;

    /// Enter AUDIO CHECK. Remembers the phase (and the pause overlay) to come back to.
    public void BeginAudition()
    {
        _audPrior = Phase;
        _audFromPause = Paused;
        _audPriorIntensity = Audio.MusicLevels.intensity;
        AudClock = 0f;
        _audQ.Clear();
        AudFlash.Clear();
        AudLast = "";
        _audVolDrag = -1;
        _audMusicDrag = false;
        _audWarm = 0;
        _audHoldFlash = false;
        _audVolTouched = false;
        Phase = Phase.AudioCheck;
        Paused = false;
        Audio.Play("select");
    }

    /// Leave AUDIO CHECK. Silences anything still queued, restores the music intensity the fight
    /// was using, and — like the codex — never un-pauses a paused fight.
    void ExitAudition()
    {
        _audQ.Clear();
        Audio.SetMusicIntensity(_audPriorIntensity);
        // Only write display.json if a fader was actually moved. Merely LOOKING at the bench must
        // not touch disk (house rule: the harness paths stay disk-clean and byte-stable).
        if (_audVolTouched) { Display.CommitVol(); _audVolTouched = false; }
        // SETTINGS EVERYWHERE: same return contract as ExitCodex — back to whichever card-bearing
        // phase opened it (fight, intro or barracks), card restored if it was open.
        Phase = SettingsCardPhase(_audPrior) ? _audPrior : Phase.Intro;
        if (_audFromPause && SettingsCardPhase(Phase)) Paused = true;
        _audFromPause = false;
        Audio.Play("select");
    }

    // ── firing ───────────────────────────────────────────────────────────────────────────

    /// Queue one cue at `delay` seconds from now. Everything the screen plays goes through here,
    /// so a single shot, a burst and a stack all share one scheduler and one flash path.
    void AudFire(string id, float delay)
    {
        _audQ.Add((AudClock + MathF.Max(0f, delay), id));
    }

    /// One press of a cue's own button.
    void AudSingle(string id) { AudFire(id, 0f); AudLast = id + " - single"; }

    /// A rapid burst of the same cue. This is the read that matters most for a firing voice: the
    /// per-shot pitch/gain jitter and the six-voice round-robin only become audible under repeat
    /// fire, and a sound that is fine once can still machine-gun into a buzz.
    void AudBurst(string id)
    {
        for (int i = 0; i < 5; i++) AudFire(id, i * 0.105f);
        AudLast = id + " - burst x5 @ 105ms";
    }

    /// One of AUDIOGATE's realistic concurrent stacks, fired with the SAME offsets the budget is
    /// measured at — so the limiter can be judged by ear against the number the gate prints.
    void AudStack(int i)
    {
        var parts = Audio.StackParts(i);
        foreach (var (cue, at) in parts) AudFire(cue, at);
        AudLast = "stack: " + Audio.StackName(i);
    }

    // ── per-frame ────────────────────────────────────────────────────────────────────────

    /// AUDIO CHECK input + scheduler. Called from Update's phase switch with the clamped frame dt.
    void HandleAudition(float dt)
    {
        AudClock += dt;

        // warm the measurement table a few cues at a time (see AudWarmPerFrame)
        for (int k = 0; k < AudWarmPerFrame && _audWarm < Audio.AuditionCues.Length; k++)
            Audio.CueMeasure(Audio.AuditionCues[_audWarm++]);

        // fire everything whose time has come, and light its row
        for (int i = _audQ.Count - 1; i >= 0; i--)
            if (_audQ[i].At <= AudClock)
            {
                string id = _audQ[i].Id;
                _audQ.RemoveAt(i);
                Audio.Play(id);
                AudFlash[id] = 1f;
            }
        // decay the row glows (~0.45 s)
        if (AudFlash.Count > 0 && !_audHoldFlash)
        {
            var keys = new List<string>(AudFlash.Keys);
            foreach (var k in keys)
            {
                float v = AudFlash[k] - dt * 2.2f;
                if (v <= 0f) AudFlash.Remove(k); else AudFlash[k] = v;
            }
        }

        var m = Raylib.GetMousePosition();

        // A drag owns the mouse until it is released (same contract as the pause menu's faders:
        // Display.SetVol is live, the disk write happens once, on release).
        if (_audVolDrag >= 0)
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
            { Display.SetVol(_audVolDrag, VolFrac(Hud.AudVol[_audVolDrag], m.X)); return; }
            Display.CommitVol();
            _audVolTouched = false;   // just written; nothing left to flush on exit
            _audVolDrag = -1;
            return;
        }
        if (_audMusicDrag)
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
            { Audio.SetMusicIntensity(VolFrac(Hud.AudMusicSlider, m.X)); return; }
            _audMusicDrag = false;
            return;
        }

        // BACK: Esc, U (toggle out), or the button.
        if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.U)
            || (Raylib.IsMouseButtonPressed(MouseButton.Left)
                && Raylib.CheckCollisionPointRec(m, Hud.AudBack)))
        { ExitAudition(); return; }

        // M mirrors the pause menu's mute toggle — the fastest A/B on the screen.
        if (Raylib.IsKeyPressed(KeyboardKey.M)) { Audio.ToggleMute(); Audio.ApplyMasterVolume(); }

        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;

        for (int i = 0; i < Hud.AudVol.Length; i++)
            if (Raylib.CheckCollisionPointRec(m, Hud.AudVol[i]))
            { _audVolDrag = i; _audVolTouched = true; Display.SetVol(i, VolFrac(Hud.AudVol[i], m.X)); return; }

        if (Raylib.CheckCollisionPointRec(m, Hud.AudMusicSlider))
        { _audMusicDrag = true; Audio.SetMusicIntensity(VolFrac(Hud.AudMusicSlider, m.X)); return; }

        if (Raylib.CheckCollisionPointRec(m, Hud.AudMusicAmb))
        { Audio.SetMusicIntensity(0f); AudLast = "music: ambient bed (intensity 0)"; return; }
        if (Raylib.CheckCollisionPointRec(m, Hud.AudMusicComb))
        { Audio.SetMusicIntensity(1f); AudLast = "music: combat bed (intensity 1)"; return; }

        if (Raylib.CheckCollisionPointRec(m, Hud.AudMute)) { Audio.ToggleMute(); Audio.ApplyMasterVolume(); return; }

        for (int i = 0; i < Hud.AudStackBtns.Count; i++)
            if (Raylib.CheckCollisionPointRec(m, Hud.AudStackBtns[i])) { AudStack(i); return; }

        var cues = Audio.AuditionCues;
        for (int i = 0; i < Hud.AudCueBtns.Count && i < cues.Length; i++)
        {
            if (Raylib.CheckCollisionPointRec(m, Hud.AudCueBtns[i])) { AudSingle(cues[i]); return; }
            if (Raylib.CheckCollisionPointRec(m, Hud.AudBurstBtns[i])) { AudBurst(cues[i]); return; }
        }
    }

    // ── harness ──────────────────────────────────────────────────────────────────────────

    /// SIGHTLINE_AUDITION=1 (shot only): open AUDIO CHECK for a screenshot. Optionally stage a
    /// couple of lit rows (SIGHTLINE_AUDITIONFIRE=1) so the "just played" state is photographed
    /// too. Never persists anything (the harness runs NoPersist).
    public void DebugAudition()
    {
        BeginAudition();
        _audPrior = Phase.Intro;
        // measure everything up front: a fixed screenshot frame must not photograph a half-warm
        // table (the frame budget is irrelevant here — nothing is being timed).
        foreach (var id in Audio.AuditionCues) Audio.CueMeasure(id);
        _audWarm = Audio.AuditionCues.Length;
        if (Environment.GetEnvironmentVariable("SIGHTLINE_AUDITIONFIRE") == "1")
        {
            AudFlash["w_lmg"] = 1f; AudFlash["crit"] = 0.85f; AudFlash["death"] = 0.7f; AudFlash["st_kill"] = 0.6f;
            AudLast = "stack: w_lmg+crit+death+st_kill";
            _audHoldFlash = true;   // a fixed screenshot frame must photograph the lit state
        }
    }

    /// SIGHTLINE_AUDITIONTEST: the AUDIO CHECK contract. Device-free (a tiny window is opened by
    /// Program only so the width assertions can measure real glyphs).
    ///   (1) every listed cue is a REGISTERED recipe, and the listing covers SfxCueIds EXACTLY —
    ///       a cue added to BuildRecipes and forgotten here would otherwise become the one sound
    ///       nobody ever auditions;
    ///   (2) every cue has a role caption, and no label+caption overflows its row at 120% text
    ///       scale (the screen has to survive the comfort setting, not just the default);
    ///   (3) every gate stack resolves to known cues, so the stack buttons can never fire silence;
    ///   (4) the measurements the rows print are finite and inside the budget the gate holds.
    public static string AuditionSelfTest()
    {
        var fails = new List<string>();
        void Chk(bool ok, string msg) { if (!ok) fails.Add(msg); }

        // (1) coverage — listed vs registered
        var listed = new List<string>();
        foreach (var (grp, ids) in Audio.AuditionGroups)
        {
            Chk(!string.IsNullOrWhiteSpace(grp), "AUDITION group with an empty label");
            Chk(ids.Length > 0, $"AUDITION group {grp} is empty");
            listed.AddRange(ids);
        }
        var seen = new HashSet<string>();
        foreach (var id in listed)
        {
            Chk(seen.Add(id), $"AUDITION lists cue '{id}' twice");
            Chk(Audio.HasCue(id), $"AUDITION lists '{id}', which is not a registered recipe");
        }
        foreach (var id in Audio.OrderedCues)
            Chk(seen.Contains(id), $"AUDITION never lists cue '{id}' — it would be un-auditionable");
        Chk(listed.Count == Audio.AuditionCues.Length,
            $"AuditionCues ({Audio.AuditionCues.Length}) does not match the groups ({listed.Count})");

        // (2) captions + row widths, measured at the WIDEST comfort setting
        float uiWas = Cfg.UiScale;
        try
        {
            Cfg.UiScale = 1.2f;
            foreach (var id in listed)
            {
                string role = Audio.CueRole(id);
                Chk(!string.IsNullOrWhiteSpace(role), $"AUDITION cue '{id}' has no role caption");
                float w = Cfg.Measure(id, Hud.AudCueFontSize, 1f).X;
                Chk(w <= Hud.AudCueLabelW, $"AUDITION cue label overflows its button at 120% ({w:0}px > {Hud.AudCueLabelW}px): {id}");
                float rw = Cfg.Measure(role, Hud.AudRoleFontSize, 1f).X;
                Chk(rw <= Hud.AudRoleW, $"AUDITION role caption overflows at 120% ({rw:0}px > {Hud.AudRoleW}px): {id} - {role}");
            }
            for (int i = 0; i < Audio.StackCount; i++)
            {
                float sw = Cfg.Measure(Audio.StackName(i), Hud.AudRoleFontSize, 1f).X;
                Chk(sw <= Hud.AudStackW, $"AUDITION stack name overflows at 120% ({sw:0}px > {Hud.AudStackW}px): {Audio.StackName(i)}");
            }
        }
        finally { Cfg.UiScale = uiWas; }

        // (3) stacks
        Chk(Audio.StackCount > 0, "AUDITION has no concurrent stacks to fire");
        for (int i = 0; i < Audio.StackCount; i++)
        {
            var parts = Audio.StackParts(i);
            Chk(parts.Length > 0, $"AUDITION stack {i} has no parts");
            foreach (var (cue, at) in parts)
            {
                Chk(Audio.HasCue(cue), $"AUDITION stack '{Audio.StackName(i)}' references unknown cue '{cue}'");
                Chk(at >= 0f && at < 5f, $"AUDITION stack '{Audio.StackName(i)}' has an absurd offset {at}");
            }
        }

        // (4) the numbers the rows print
        foreach (var id in listed)
        {
            var (pk, rms, hi, dur) = Audio.CueMeasure(id);
            Chk(float.IsFinite(pk) && float.IsFinite(rms) && float.IsFinite(hi) && float.IsFinite(dur),
                $"AUDITION measurement for '{id}' is not finite");
            Chk(dur > 0f, $"AUDITION measurement for '{id}' has no duration");
            Chk(pk <= -1.0f + 0.05f, $"AUDITION reads '{id}' peak {pk:0.0} dBFS — over the -1 dBFS ceiling");
            Chk(rms <= pk + 0.001f, $"AUDITION reads '{id}' rms {rms:0.0} above its peak {pk:0.0}");
            Chk(hi >= 0f && hi <= 1f, $"AUDITION reads '{id}' >1kHz share {hi} outside 0..1");
        }

        return fails.Count == 0
            ? "AUDITIONTEST: PASS (" + Audio.AuditionCues.Length + " cues listed + measured, " +
              Audio.StackCount + " stacks, labels fit at 120%)"
            : "AUDITIONTEST: FAIL\n  " + string.Join("\n  ", fails);
    }
}
