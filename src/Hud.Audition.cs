using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

// ═══════════════════════════════════════════════════════════════════════════════════════════
//  AUDIO CHECK — the screen.  PROGRAM RESONANCE, WAVE A3.
//
//  Design brief: the owner sits down, sweeps EVERY sound in about two minutes, and walks away
//  able to say something specific. That makes SCANNABILITY the whole design, so:
//    * one row per cue, always the same shape — name, burst, what it is for, three numbers, a
//      level bar. The eye runs down a column, not around a card.
//    * the numbers sit BESIDE the button, not on a separate tab. "Thin" and "-31 dB RMS, 9%
//      above 1 kHz" have to be readable in one glance or the pairing is worthless.
//    * the RMS value is tinted against the budget band AUDIOGATE actually holds, so a cue that
//      has drifted out of its mix role is visible before it is described.
//    * everything that needs an A/B — the four faders, the music crossfade, mute — is on the
//      same screen, live, while cues are playing.
//  Draw-only: every rect published here is hit-tested by Game.HandleAudition.
//
//  NO new GetTime reads: the backdrop and the row glows run off Game.AudClock (a dt accumulator).
// ═══════════════════════════════════════════════════════════════════════════════════════════
public static partial class Hud
{
    /// Off-switch for W5-FIX blocker 1: puts the audition backdrop back in the CHROME pass, i.e.
    /// after BuildBloom, which is the shipped defect. BACKDROPTEST goes red with it set.
    static readonly bool AudBackdropInChrome =
        Environment.GetEnvironmentVariable("SIGHTLINE_AUDBACKDROP") == "1";

    // ---- published hit rects (read by Game.HandleAudition) ----
    public static Rectangle AudBack, AudMute, AudMusicSlider, AudMusicAmb, AudMusicComb;
    public static readonly Rectangle[] AudVol = new Rectangle[4];
    public static readonly List<Rectangle> AudCueBtns = new();
    public static readonly List<Rectangle> AudBurstBtns = new();
    public static readonly List<Rectangle> AudStackBtns = new();

    // ---- the width/size budget the AUDITIONTEST asserts against at 120% text scale ----
    public const int AudCueFontSize = 13;
    public const int AudRoleFontSize = 11;
    public const int AudCueLabelW = 116;   // text room inside a cue button
    public const int AudRoleW = 206;       // the "what it is for" column
    public const int AudStackW = 300;      // a stack button's label column

    static void DrawAudition(Game g)
    {
        // the screen's own clock — no GetTime read (CLAUDE.md: use the Fx pattern)
        float t = g.AudClock;
        // W5-FIX (review blocker 1): the backdrop is NOT drawn here any more. It moved into
        // Hud.DrawBackdropLayer (the bloom-source pass, still off g.AudClock) — painting it from
        // this, the CHROME pass, put it AFTER BuildBloom, so the composite added the live board's
        // glow on top of an opaque screen. The screen is reachable from the pause card with the
        // board fully lit behind it, and post-FX ships ON.
        // SIGHTLINE_AUDBACKDROP=1 restores the defect so SIGHTLINE_BACKDROPTEST is falsifiable
        // without reverting the tree.
        if (AudBackdropInChrome) DrawTacticalBackdrop(t, Pal.Accent, 0f);
        AudCueBtns.Clear(); AudBurstBtns.Clear(); AudStackBtns.Clear();

        int W = Cfg.ScreenW, H = Cfg.ScreenH;

        // ---- title ----
        float titleIn = PanelAnim("audTitle", 0.45f);
        string title = "AUDIO CHECK";
        int tfs = 44;
        Vector2 tm = Cfg.TitleMeasure(title, tfs, 4f);
        float tx = W / 2f - tm.X / 2f;
        float ty = 20f - (1f - Util.EaseOutBack(Util.Clamp(titleIn, 0f, 1f))) * 16f;
        Cfg.TitleText(title, new Vector2(tx, ty), tfs, 4f, Raylib.Fade(Pal.Txt, titleIn));
        DrawCornerBrackets(new Rectangle(tx - 18, ty + 4, tm.X + 36, tfs - 8),
                           Raylib.Fade(Pal.Accent, 0.5f * titleIn), 14f);

        // one live feedback line: what the last press fired
        string sub = string.IsNullOrEmpty(g.AudLast)
            ? "Click a cue to hear it once - BURST to hear five - the numbers are measured, not guessed."
            : "LAST: " + g.AudLast;
        Vector2 sm = Cfg.Measure(sub, 12, 1f);
        Cfg.Text(sub, new Vector2(W / 2f - sm.X / 2f, ty + tfs + 2), 12, 1f,
                 Raylib.Fade(string.IsNullOrEmpty(g.AudLast) ? Pal.TxtDim : Pal.Accent, titleIn));

        int top = (int)(ty + tfs + 24);
        int leftX = 40, leftW = 700;
        int rightX = 764, rightW = W - rightX - 40;

        DrawAudCueTable(g, leftX, top, leftW);
        DrawAudRightColumn(g, rightX, top, rightW);

        AudBack = new Rectangle(W / 2 - 110, H - 50, 220, 38);
        DrawGhostButton(AudBack, "BACK", "Esc", PanelAnim("audBack", 0.3f, 0.2f));
    }

    // ── the cue table ────────────────────────────────────────────────────────────────────

    static void DrawAudCueTable(Game g, int x, int top, int w)
    {
        float in_ = PanelAnim("audCues", 0.4f, 0.08f);
        // column x-positions, all derived from the panel origin so the row shape never drifts
        int btnX = x, btnW = AudCueLabelW + 14;
        int burstX = btnX + btnW + 8, burstW = 54;
        int roleX = burstX + burstW + 12;
        int peakR = roleX + AudRoleW + 66;      // right edge of the PEAK column
        int rmsR = peakR + 66;
        int hiR = rmsR + 54;
        int barX = hiR + 12, barW = x + w - barX;

        // header row
        int hy = top;
        Cfg.Text("CUE", new Vector2(btnX + 2, hy), 11, 1f, Raylib.Fade(Pal.TxtDim, in_));
        Cfg.Text("WHAT IT IS FOR", new Vector2(roleX, hy), 11, 1f, Raylib.Fade(Pal.TxtDim, in_));
        RightText("PEAK", peakR, hy, 11, Raylib.Fade(Pal.TxtDim, in_));
        RightText("RMS", rmsR, hy, 11, Raylib.Fade(Pal.TxtDim, in_));
        RightText(">1kHz", hiR, hy, 11, Raylib.Fade(Pal.TxtDim, in_));
        Cfg.Text("LEVEL", new Vector2(barX, hy), 11, 1f, Raylib.Fade(Pal.TxtDim, in_));
        Raylib.DrawLine(x, hy + 15, x + w, hy + 15, Raylib.Fade(Pal.PanelBd, in_));

        int y = hy + 21;
        var mouse = Raylib.GetMousePosition();
        foreach (var (group, ids) in Audio.AuditionGroups)
        {
            Cfg.Text(group, new Vector2(btnX, y + 3), 12, 1f, Raylib.Fade(Pal.Accent, 0.85f * in_));
            float gw = Cfg.Measure(group, 12, 1f).X;
            Raylib.DrawLine((int)(btnX + gw + 10), y + 9, x + w, y + 9, Raylib.Fade(Pal.PanelBd, 0.7f * in_));
            y += 22;

            foreach (var id in ids)
            {
                g.AudFlash.TryGetValue(id, out float glow);
                var row = new Rectangle(x, y - 1, w, 20);
                if (glow > 0f)
                    Raylib.DrawRectangleRec(row, Raylib.Fade(Pal.Accent, 0.16f * glow));

                var btn = new Rectangle(btnX, y, btnW, 18);
                var brt = new Rectangle(burstX, y, burstW, 18);
                AudCueBtns.Add(btn); AudBurstBtns.Add(brt);

                bool hb = Raylib.CheckCollisionPointRec(mouse, btn);
                bool hr = Raylib.CheckCollisionPointRec(mouse, brt);
                DrawAudChip(btn, id, AudCueFontSize, hb || glow > 0.35f, glow > 0.35f ? Pal.Accent : Pal.Friend, in_);
                DrawAudChip(brt, "BURST", AudRoleFontSize, hr, Pal.Good, in_);

                Cfg.Text(Audio.CueRole(id), new Vector2(roleX, y + 3), AudRoleFontSize, 1f,
                         Raylib.Fade(Pal.TxtDim, in_));

                if (Audio.CueMeasured(id))
                {
                    var (pk, rms, hi, _) = Audio.CueMeasure(id);
                    var (_, lo, hiB) = Audio.CueBand(id);
                    bool inBand = rms >= lo && rms <= hiB;
                    RightText($"{pk,6:0.0}", peakR, y + 3, 12, Raylib.Fade(Pal.Txt, 0.85f * in_));
                    // out-of-band is marked with a GLYPH as well as a colour — the whole screen has
                    // to survive SIGHTLINE_CB, and "green vs amber" alone would not.
                    RightText(inBand ? $"{rms,6:0.0}" : $"!{rms,5:0.0}", rmsR, y + 3, 12,
                              Raylib.Fade(inBand ? Pal.Good : Pal.Suspect, in_));
                    RightText($"{hi * 100f,3:0}%", hiR, y + 3, 12, Raylib.Fade(Pal.Txt, 0.7f * in_));
                    // level bar: RMS mapped over the -36..-14 dBFS window the whole mix lives in
                    float f = Util.Clamp((rms + 36f) / 22f, 0f, 1f);
                    var track = new Rectangle(barX, y + 6, barW, 6);
                    Raylib.DrawRectangleRec(track, Raylib.Fade(Pal.RGBA(30, 37, 47), in_));
                    if (f > 0f)
                        Raylib.DrawRectangleRec(new Rectangle(barX, y + 6, barW * f, 6),
                                                Raylib.Fade(inBand ? Pal.Friend : Pal.Suspect, in_));
                }
                else
                {
                    RightText("...", peakR, y + 3, 12, Raylib.Fade(Pal.TxtDim, 0.6f * in_));
                }
                y += 21;
            }
            y += 4;
        }
    }

    /// A compact clickable chip — the cue table needs a button 18px tall, which is below what
    /// DrawButtonRect/DrawGhostButton are shaped for.
    static void DrawAudChip(Rectangle r, string label, int fs, bool hot, Color accent, float anim)
    {
        Raylib.DrawRectangleRec(r, Raylib.Fade(hot ? Pal.RGBA(26, 36, 48) : Pal.RGBA(17, 23, 31), anim));
        Raylib.DrawRectangleLinesEx(r, 1f, Raylib.Fade(hot ? accent : Pal.PanelBd, anim));
        float lw = Cfg.Measure(label, fs, 1f).X;
        Cfg.Text(label, new Vector2((int)(r.X + r.Width / 2 - lw / 2), (int)(r.Y + r.Height / 2 - fs / 2 - 1)),
                 fs, 1f, Raylib.Fade(hot ? accent : Pal.Txt, anim));
    }

    static void RightText(string s, float rightX, float y, int fs, Color c)
    {
        float w = Cfg.Measure(s, fs, 1f).X;
        Cfg.Text(s, new Vector2((int)(rightX - w), (int)y), fs, 1f, c);
    }

    // ── mix / music / stacks ─────────────────────────────────────────────────────────────

    static void DrawAudRightColumn(Game g, int x, int top, int w)
    {
        float in_ = PanelAnim("audRight", 0.4f, 0.14f);
        int y = top;

        // ---- device state. The sandbox has no speaker; say so plainly rather than looking broken.
        bool dev = Audio.DeviceReady;
        var plate = new Rectangle(x, y, w, 54);
        Raylib.DrawRectangleRounded(plate, 0.08f, 6, Raylib.Fade(Pal.Panel, 0.95f * in_));
        Raylib.DrawRectangleLinesEx(plate, 1.2f, Raylib.Fade(dev ? Pal.Good : Pal.Foe, 0.7f * in_));
        Cfg.Text(dev ? "AUDIO DEVICE: PRESENT" : "NO AUDIO DEVICE",
                 new Vector2(x + 12, y + 8), 14, 1f, Raylib.Fade(dev ? Pal.Good : Pal.Foe, in_));
        string devSub = dev
            ? (Audio.MusicReady ? "Both music beds are streaming. Everything below is live."
                                : "SFX are live; the music beds did not load.")
            : "Nothing will be heard here. The controls and every measured number below are still real.";
        var devLines = WrapText(devSub, 11, w - 24);
        for (int i = 0; i < devLines.Count && i < 2; i++)
            Cfg.Text(devLines[i], new Vector2(x + 12, y + 28 + i * 13), 11, 1f, Raylib.Fade(Pal.TxtDim, in_));
        y += 66;

        // ---- MIX ----
        AudSectionHead("MIX", x, y, w, in_, "move a fader while a cue is playing");
        AudMute = new Rectangle(x + w - 108, y - 4, 108, 20);
        DrawAudChip(AudMute, Audio.Enabled ? "MUTE [M]" : "MUTED [M]",
                    11, !Audio.Enabled, Audio.Enabled ? Pal.Friend : Pal.Foe, in_);
        y += 20;
        for (int i = 0; i < 4; i++)
        {
            AudVol[i] = new Rectangle(x, y, w, 32);
            DrawVolSlider(AudVol[i], Display.VolNames[i], Display.Vol(i), Audio.Enabled);
            y += 38;
        }
        y += 10;

        // ---- MUSIC ----
        AudSectionHead("MUSIC BEDS", x, y, w, in_, "sweep the crossfade by hand");
        y += 22;
        int half = (w - 10) / 2;
        AudMusicAmb = new Rectangle(x, y, half, 26);
        AudMusicComb = new Rectangle(x + half + 10, y, half, 26);
        var lv = Audio.MusicLevels;
        DrawAudChip(AudMusicAmb, "AMBIENT", 12, lv.intensity < 0.5f, Pal.Friend, in_);
        DrawAudChip(AudMusicComb, "COMBAT", 12, lv.intensity >= 0.5f, Pal.Foe, in_);
        y += 32;
        AudMusicSlider = new Rectangle(x, y, w, 32);
        DrawVolSlider(AudMusicSlider, "INTENSITY", lv.intensity, Audio.Enabled);
        y += 38;
        // the two bed gains actually being pushed at Raylib — the crossfade made visible
        AudBedBar(x, y, w, "ambient bed", lv.amb, Pal.Friend, in_);
        AudBedBar(x, y + 15, w, "combat bed", lv.comb, Pal.Foe, in_);
        y += 40;

        // ---- STACKS ----
        AudSectionHead("CONCURRENT STACKS", x, y, w, in_, "the worst cases the limiter must survive");
        y += 22;
        for (int i = 0; i < Audio.StackCount; i++)
        {
            var r = new Rectangle(x, y, w, 26);
            AudStackBtns.Add(r);
            bool hot = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), r);
            Raylib.DrawRectangleRec(r, Raylib.Fade(hot ? Pal.RGBA(26, 36, 48) : Pal.RGBA(17, 23, 31), in_));
            Raylib.DrawRectangleLinesEx(r, 1f, Raylib.Fade(hot ? Pal.Accent : Pal.PanelBd, in_));
            Cfg.Text(Audio.StackName(i), new Vector2(x + 10, y + 6), AudRoleFontSize, 1f,
                     Raylib.Fade(hot ? Pal.Accent : Pal.Txt, in_));
            string np = Audio.StackParts(i).Length + " cues";
            RightText(np, x + w - 10, y + 6, AudRoleFontSize, Raylib.Fade(Pal.TxtDim, in_));
            y += 30;
        }
        y += 12;

        // ---- how to read it. Four lines, because a bench nobody can read is a bench nobody uses.
        AudSectionHead("HOW TO READ IT", x, y, w, in_, "");
        y += 20;
        string[] hints =
        {
            "PEAK / RMS are dBFS off the real mastered buffer - the same",
            "samples the speaker gets. RMS is tinted against the mix-role",
            "band AUDIOGATE holds it inside; a leading ! means it drifted out.",
            ">1kHz is the share of energy above 1 kHz - the read that caught",
            "the white-noise weapons and the beds with no top end at all.",
            "BURST fires five at 105 ms: the pitch/gain jitter and the",
            "six-voice round-robin are only audible under repeat fire.",
        };
        foreach (var ln in hints)
        { Cfg.Text(ln, new Vector2(x, y), 10, 1f, Raylib.Fade(Pal.TxtDim, 0.85f * in_)); y += 13; }
    }

    static void AudSectionHead(string label, int x, int y, int w, float anim, string hint)
    {
        Cfg.Text(label, new Vector2(x, y), 12, 1f, Raylib.Fade(Pal.Accent, 0.9f * anim));
        float lw = Cfg.Measure(label, 12, 1f).X;
        Cfg.Text(hint, new Vector2(x + lw + 12, y + 1), 10, 1f, Raylib.Fade(Pal.TxtDim, 0.8f * anim));
        Raylib.DrawLine(x, y + 16, x + w, y + 16, Raylib.Fade(Pal.PanelBd, 0.7f * anim));
    }

    static void AudBedBar(int x, int y, int w, string label, float v, Color c, float anim)
    {
        Cfg.Text(label, new Vector2(x, y), 10, 1f, Raylib.Fade(Pal.TxtDim, anim));
        int bx = x + 92, bw = w - 92;
        Raylib.DrawRectangleRec(new Rectangle(bx, y + 3, bw, 6), Raylib.Fade(Pal.RGBA(30, 37, 47), anim));
        float f = Util.Clamp(v, 0f, 1f);
        if (f > 0f) Raylib.DrawRectangleRec(new Rectangle(bx, y + 3, bw * f, 6), Raylib.Fade(c, anim));
    }
}
