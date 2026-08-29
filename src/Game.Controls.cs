using System;
using System.Linq;
using Raylib_cs;

namespace Sightline;

// PROGRAM RESONANCE W6 — the CONTROLS screen: state + input for key rebinding.
//
// Reached from the pause menu (CONTROLS, or its key) and from the intro (CONTROLS / [O]), so a
// player who cannot use the default layout can fix it BEFORE they deploy, not only mid-mission.
// The prior phase is remembered exactly like the codex does, including the "opened over the pause
// overlay" case — reading the key list mid-fight must not un-pause the fight.
//
// THE STRAND GUARANTEE, restated at the input layer: `Escape` is a fixed row in Keymap. It is not
// assignable, so no rebind can take it; it BACKS OUT of this screen; and while a key capture is
// armed it CANCELS the capture instead of being captured. The route
//     Escape -> pause menu -> CONTROLS -> RESET DEFAULTS
// therefore exists from any keymap the player or a corrupt settings file can produce.
public partial class Game
{
    Phase _ctlPrior;              // phase to return to on BACK
    bool  _ctlFromPause;          // opened over the pause overlay — restore the pause on exit
    /// The action id currently waiting for a key, or null. While set, the whole screen is modal:
    /// the next key press either binds, is refused, or (Escape) cancels.
    public string KeyCapture;
    /// The last refusal, shown under the list until the player does something else.
    public string KeyError;
    public float  KeyErrorAge;
    /// Scroll offset (px) into the two-column list; clamp bound published by Hud.DrawControls.
    public float  CtlScroll;

    /// Enter the CONTROLS screen.
    public void BeginControls()
    {
        _ctlPrior = Phase;
        _ctlFromPause = Paused;
        KeyCapture = null;
        KeyError = null;
        KeyErrorAge = 0f;
        CtlScroll = 0f;
        Phase = Phase.Controls;
        Paused = false;
        Audio.Play("select");
    }

    void ExitControls()
    {
        KeyCapture = null;
        Phase = (_ctlPrior == Phase.PlayerTurn || _ctlPrior == Phase.EnemyTurn) ? _ctlPrior : Phase.Intro;
        if (_ctlFromPause && (Phase == Phase.PlayerTurn || Phase == Phase.EnemyTurn)) Paused = true;
        _ctlFromPause = false;
        Audio.Play("select");
    }

    /// CONTROLS input. Two modes:
    ///   * BROWSING — click a row to arm a capture, click RESET DEFAULTS, click/Esc BACK, scroll.
    ///   * CAPTURING — the next key is the answer. Escape cancels (it can never be captured,
    ///     because Keymap.IsAssignable refuses it, but cancelling first makes that explicit and
    ///     means the player never has to discover the refusal to get out).
    void HandleControlsInput(float dt)
    {
        if (KeyError != null)
        {
            KeyErrorAge += dt;
            if (KeyErrorAge > 6f) { KeyError = null; KeyErrorAge = 0f; }
        }

        // ---- capture mode ----
        if (KeyCapture != null)
        {
            // Escape ALWAYS gets you out. Checked before the queue is drained so it can never be
            // consumed as a candidate binding.
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            { KeyCapture = null; KeyError = null; Audio.Play("select"); return; }
            // A right-click also cancels — the same "get me out of here" reflex as cancelling aim.
            if (Raylib.IsMouseButtonPressed(MouseButton.Right))
            { KeyCapture = null; KeyError = null; Audio.Play("select"); return; }

            // GetKeyPressed drains the key QUEUE, which is what a rebind wants: it catches a key
            // that went down and up inside one frame, where IsKeyPressed would miss it entirely.
            int raw = Raylib.GetKeyPressed();
            while (raw != 0)
            {
                var k = (KeyboardKey)raw;
                string err = Keymap.Set(KeyCapture, k);
                if (err == null)
                {
                    KeyCapture = null;
                    KeyError = null;
                    KeyErrorAge = 0f;
                    Audio.Play("select");
                    return;
                }
                // Refused. Say why, out loud, and STAY in capture so the next key can be tried.
                KeyError = err;
                KeyErrorAge = 0f;
                Audio.Play("deny");
                return;
            }
            return;   // capture is modal: nothing else this frame
        }

        // ---- browsing ----
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { ExitControls(); return; }

        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0f) CtlScroll -= wheel * 48f;
        if (Raylib.IsKeyDown(KeyboardKey.Down)) CtlScroll += 8f;
        if (Raylib.IsKeyDown(KeyboardKey.Up))   CtlScroll -= 8f;
        CtlScroll = Math.Clamp(CtlScroll, 0f, MathF.Max(0f, Hud.CtlScrollMax));

        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var m = Raylib.GetMousePosition();
        if (Raylib.CheckCollisionPointRec(m, Hud.CtlBack)) { ExitControls(); return; }
        if (Raylib.CheckCollisionPointRec(m, Hud.CtlReset))
        {
            Keymap.ResetAll();
            KeyError = null;
            Audio.Play("select");
            return;
        }
        foreach (var (id, r) in Hud.CtlRows)
            if (Raylib.CheckCollisionPointRec(m, r))
            {
                var b = Keymap.Get(id);
                if (b == null) return;
                if (b.Fixed)
                {
                    KeyError = b.Label + " is reserved - it is the guaranteed way back to this menu";
                    KeyErrorAge = 0f;
                    Audio.Play("deny");
                    return;
                }
                KeyCapture = id;
                KeyError = null;
                KeyErrorAge = 0f;
                Audio.Play("select");
                return;
            }
    }

    // ---- harness: stage the CONTROLS screen for a screenshot ----------------------------------
    /// SIGHTLINE_CONTROLS=1 — open the rebinding surface.
    ///   SIGHTLINE_CONTROLS=capture  arms a capture on FIRE (the "press a key" state)
    ///   SIGHTLINE_CONTROLS=conflict arms a capture on FULLSCREEN and plays the historical bug at
    ///                               it (F, already FOCUS in a mission) so the refusal is on screen
    ///   SIGHTLINE_CONTROLS=remap    shows a map with several rows moved off their defaults
    /// Shot-mode only and NoPersist-gated by the caller, so nothing here can write a settings file.
    public void DebugControls(string mode)
    {
        BeginControls();
        _ctlPrior = Phase.Intro;
        if (mode == "capture") KeyCapture = Keymap.Shoot;
        else if (mode == "conflict")
        {
            KeyCapture = Keymap.Fullscreen;
            KeyError = Keymap.Set(Keymap.Fullscreen, Keymap.KeyOf(Keymap.FocusOw));
        }
        else if (mode == "remap")
        {
            Keymap.Set(Keymap.Shoot, KeyboardKey.Q);
            Keymap.Set(Keymap.Overwatch, KeyboardKey.Z);
            Keymap.Set(Keymap.Grenade, KeyboardKey.J);
            Keymap.Set(Keymap.CamReset, KeyboardKey.I);
        }
    }
}
