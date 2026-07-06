using System;
using System.Collections.Generic;
using Raylib_cs;

namespace Sightline;

// PROGRAM HORIZON — Wave 6: CODEX / FIELD MANUAL screen state + input.
//
// The CODEX is a read-only reference screen reached off the intro (button / key K) or the pause menu.
// It surfaces the game's whole vocabulary (bestiary, classes, perks, boons, contracts, specs, traits,
// scars, weapon mods, statuses, objectives) so a new player can learn the systems. Entirely
// presentation/data — it reads only the existing Def strings + Renderer.DrawCodexGlyph, and writes no
// run/save/meta state. The prior phase is remembered so BACK returns to wherever the player opened it
// (Intro or the paused mission).
public partial class Game
{
    /// The assembled categories, cached on entry so the per-frame draw never rebuilds them.
    public List<CodexCategory> CodexCats;
    public int CodexTab;        // selected category index
    public float CodexScroll;   // vertical scroll offset (px) into the current category's list
    Phase _codexPrior;          // phase to return to on BACK (Intro, or PlayerTurn/EnemyTurn if paused)

    /// Enter the CODEX: assemble entries + remember where we came from, then switch phase.
    public void BeginCodex()
    {
        _codexPrior = Phase;
        CodexCats = Codex.Build();
        CodexTab = 0;
        CodexScroll = 0f;
        Phase = Phase.Codex;
        Paused = false;   // if opened from the pause menu, the pause overlay steps aside for the codex
        Audio.Play("select");
    }

    /// Leave the CODEX, returning to the phase it was opened from (Intro by default).
    void ExitCodex()
    {
        Phase = (_codexPrior == Phase.PlayerTurn || _codexPrior == Phase.EnemyTurn) ? _codexPrior : Phase.Intro;
        Audio.Play("select");
    }

    /// CODEX input: tab clicks (mouse) + up/down arrow tab cycling, wheel/arrow scroll, and BACK (Esc /
    /// button). No gameplay state is touched; scroll is clamped to the drawn list height published by Hud.
    void HandleCodexInput()
    {
        if (CodexCats == null || CodexCats.Count == 0) { ExitCodex(); return; }

        // BACK: Esc, K (toggle out), or the BACK button.
        bool back = Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.K)
                    || (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                        Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Hud.CodexBack));
        if (back) { ExitCodex(); return; }

        int prevTab = CodexTab;

        // tab selection: click a tab, or Up/Down (and W/S) to cycle categories.
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            var m = Raylib.GetMousePosition();
            for (int i = 0; i < Hud.CodexTabBtns.Count; i++)
                if (Raylib.CheckCollisionPointRec(m, Hud.CodexTabBtns[i])) { CodexTab = i; break; }
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W))
            CodexTab = (CodexTab - 1 + CodexCats.Count) % CodexCats.Count;
        if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S))
            CodexTab = (CodexTab + 1) % CodexCats.Count;
        CodexTab = Math.Clamp(CodexTab, 0, CodexCats.Count - 1);
        if (CodexTab != prevTab) { CodexScroll = 0f; Audio.Play("select"); }

        // scroll: wheel + Left/Right (and A/D) nudge. Clamp to the content the Hud measured last frame.
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0f) CodexScroll -= wheel * 48f;
        if (Raylib.IsKeyDown(KeyboardKey.Right) || Raylib.IsKeyDown(KeyboardKey.D)) CodexScroll += 8f;
        if (Raylib.IsKeyDown(KeyboardKey.Left)  || Raylib.IsKeyDown(KeyboardKey.A)) CodexScroll -= 8f;
        CodexScroll = Math.Clamp(CodexScroll, 0f, MathF.Max(0f, Hud.CodexScrollMax));
    }

    // ---- harness: open the CODEX for a screenshot (SIGHTLINE_CODEX=1) ----
    /// Assemble + show the CODEX (defaults to the ENEMIES bestiary so the shot includes silhouettes).
    public void DebugCodex()
    {
        BeginCodex();
        // W11: select ENEMIES by NAME — tab 0 is now FIELD CRAFT, so a fixed index would frame
        // the rules tab instead of the bestiary silhouettes this hook exists to verify.
        // SIGHTLINE_CODEXTAB=<i> overrides (e.g. 0 frames FIELD CRAFT); shot-mode only like the hook.
        CodexTab = Math.Max(0, CodexCats.FindIndex(c => c.Name == "ENEMIES"));
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_CODEXTAB"), out int tabEnv))
            CodexTab = Math.Clamp(tabEnv, 0, CodexCats.Count - 1);
        _codexPrior = Phase.Intro;
    }

    /// CODEXTEST (SIGHTLINE_CODEXTEST): content-completeness — every documented enum has Name+Desc and
    /// the bestiary covers every archetype. Also assemble the categories to prove Build() doesn't throw.
    public string CodexSelfTest()
    {
        string r = Codex.SelfTest();
        try
        {
            var cats = Codex.Build();
            if (cats == null || cats.Count == 0) return "CODEXTEST: FAIL\n  Build() produced no categories";
            foreach (var c in cats)
                if (c.Entries == null || c.Entries.Count == 0)
                    return "CODEXTEST: FAIL\n  category " + c.Name + " has no entries";
        }
        catch (Exception ex) { return "CODEXTEST: FAIL\n  Build() threw: " + ex.Message; }
        return r;
    }
}
