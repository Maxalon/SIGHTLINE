using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Raylib_cs;

namespace Sightline;

/// Presents the fixed-layout 1280x800 game scaled + letterboxed to the actual window,
/// so it stays readable on big / high-DPI / 4K displays (window size + fullscreen).
/// At exactly 1280x800 windowed it draws directly (keeping MSAA crispness); scaling
/// only kicks in once the window is enlarged. Disabled in the headless harness so the
/// smoke-test screenshots stay byte-identical.
///
/// Phase 5.2 adds an optional post-FX shader pass (bloom + vignette + biome colour
/// grading + chromatic aberration). The pass is always OFF when Display is disabled
/// (headless harness), so plain SIGHTLINE_SHOT screenshots remain byte-identical.
/// Enable for verification with SIGHTLINE_POSTFX=1 (forces Display.Init(true) even
/// during shot mode and sets a strong demo bloom so the effect is clearly visible).
public static class Display
{
    public static bool Enabled;
    static RenderTexture2D _target;

    // ---- post-FX shader (Phase 5.2) ----
    // PostFX is enabled by default whenever Display is enabled (i.e. live game).
    // It is always OFF when Display is disabled so the headless smoke-test screenshots
    // are byte-identical. Toggle via the pause menu or set Display.PostFX = false.
    public static bool PostFX = true;
    static Shader _fx;
    static bool   _fxReady;

    // Uniforms fed each frame from Game via Display.SetPostFxParams(...)
    static int _locResolution, _locBloom, _locChroma, _locGrade, _locTime, _locBright, _locGamma;

    // Current values written by Game every Update (or in POSTFX demo mode).
    public static float BloomIntensity;          // 0 = none, 1 = strong
    public static float ChromaIntensity;         // 0 = none, 1 = max
    public static Vector3 GradeTint = Vector3.One; // per-biome multiplicative tint (r,g,b 0..1+)
    public static float FxTime;                  // accumulated time (for subtle animated effects)

    // Call from Game.Update; safe no-op when Display is disabled.
    public static void SetPostFxParams(float bloom, float chroma, Vector3 grade)
    {
        BloomIntensity = bloom;
        ChromaIntensity = chroma;
        GradeTint = grade;
    }

    // The fragment shader: bloom (3-tap radial blur sampled from the render texture),
    // a soft vignette, per-biome multiplicative colour grading, and chromatic aberration.
    // Written for GLSL 3.30 (core) — compatible with Raylib's OpenGL 3.3 / Mesa llvmpipe.
    // Raylib provides: texture0 (the frame), fragTexCoord (0..1), colDiffuse (tint, always White here).
    const string FsSrc = @"#version 330 core
in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;
uniform sampler2D texture0;
uniform vec2  uResolution;   // render-target size (pixels)
uniform float uBloom;        // 0..1 event-reactive bloom strength (spikes on hits/kills)
uniform float uChroma;       // 0..1 chromatic aberration strength (impact-reactive)
uniform vec3  uGrade;        // per-biome colour multiply (default 1,1,1)
uniform float uTime;         // accumulated time (for very slow drift; optional)
uniform float uBright;       // user brightness (0.7..1.3, 1.0 = neutral) — true in-shader scale
uniform float uGamma;        // user gamma (0.8..1.3, 1.0 = neutral) — midtone lift/sink

// Rec.709 luminance.
float luma(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

// Bright-pass bloom: a weighted radial blur of ONLY the bright/emissive part of the
// frame. SIGHTLINE is dark geometric art, so the only bright pixels are the things we
// WANT to halo — objective glows (EVAC green / VIP gold), unit under-glows (cyan/red),
// muzzle/impact flashes, HP bars. A soft luma threshold keeps the dark board clean.
// radiusPx is in PIXELS; divided per-axis by uResolution so the kernel stays square
// regardless of aspect ratio.
vec3 brightBlur(vec2 uv, float radiusPx) {
    vec2 r1 = vec2(radiusPx) / uResolution;   // aspect-correct UV offset
    vec2 r2 = r1 * 0.5;
    vec3 sum = vec3(0.0);
    float wsum = 0.0;
    // 12-tap: a centre + ring at r2 + ring at r1, each soft-thresholded so only
    // genuinely bright sources contribute. Cheap enough for a single full-screen pass.
    vec2 offs[12] = vec2[12](
        vec2( 0.0,  0.0),
        vec2( r2.x, 0.0), vec2(-r2.x, 0.0), vec2(0.0,  r2.y), vec2(0.0, -r2.y),
        vec2( r1.x, r1.y), vec2(-r1.x, r1.y), vec2(r1.x, -r1.y), vec2(-r1.x, -r1.y),
        vec2( r1.x*1.6, 0.0), vec2(-r1.x*1.6, 0.0), vec2(0.0, r1.y*1.6)
    );
    for (int i = 0; i < 12; i++) {
        vec3 s = texture(texture0, uv + offs[i]).rgb;
        // Soft bright-pass: knee at ~0.36 luma (HORIZON W5: lowered from 0.42 so the
        // fattened tracer core + unit under-glows + objective glows reliably cross the
        // knee and BLOOM, while the (now further-receded) dark board floor + muted cover
        // stay below it and never wash. Square it for a punchier, less-smeary falloff.
        float b = smoothstep(0.36, 0.85, luma(s));
        b = b * b;
        // weight inner taps slightly higher for a tighter core + soft outer halo.
        float w = (i == 0) ? 1.6 : (i < 5 ? 1.0 : 0.6);
        sum += s * b * w;
        wsum += w;
    }
    return sum / wsum;
}

void main() {
    vec2 uv = fragTexCoord;
    vec2 dir = uv - 0.5;
    float edge = length(dir) * 1.4142;   // 0 at centre, ~1.0 at the corner

    // --- chromatic aberration (impact-reactive, edges only) ---
    // Displace R/B channels outward, scaled by distance from centre so the centre of
    // the board stays crisp and only the framed edges fringe on a hit. Stays at 0 when
    // uChroma is 0 (no recent impact) so it never reads as ""broken"".
    float caStr = uChroma * 0.0085 * edge * edge;
    vec2 caOff = normalize(dir + vec2(0.0001)) * caStr;
    float r = texture(texture0, uv + caOff).r;
    float g = texture(texture0, uv).g;
    float b = texture(texture0, uv - caOff).b;
    vec3 base = vec3(r, g, b);

    // --- bloom (always-on soft glow + event spike) ---
    // A constant gentle glow makes emissive accents (objective rings, unit under-glows)
    // halo softly at all times — this is the ""shippable indie"" payoff. Combat events
    // (uBloom) push it brighter for a punchy hit/kill flash that then decays.
    vec3 glow = brightBlur(uv, 5.0);
    // HORIZON W5 — keep the resting halo restrained (0.5, not a baseline wash) but RAISE the
    // reactive ceiling so a KILL/crit (Game.AddBloom spikes uBloom, then decays) visibly
    // FLOODS the screen with light before settling. Reactive, not always-on.
    float bloomAmt = 0.5 + uBloom * 1.7;      // restrained baseline halo + a big reactive spike
    vec3 withBloom = base + glow * bloomAmt;

    // --- colour grade: saturation + contrast + per-biome tint ---
    // 1) biome tint (uGrade is near 1.0); amplify its deviation from neutral so missions
    //    feel like distinct places (cool steel / warm arid / icy tundra ...).
    //    W6: grade amp 2.2 -> 2.6 (biome tint reads a touch harder on-device); still
    //    readability-clamped by the 0.35 blend below + the final clamp/gamma, and OFF in the
    //    plain SIGHTLINE_SHOT harness so it never affects headless byte-stability.
    vec3 tint = vec3(1.0) + (uGrade - vec3(1.0)) * 2.6;
    vec3 graded = withBloom * tint;
    // 2) saturation lift — the geometric palette pops a little more.
    float lum = luma(graded);
    graded = mix(vec3(lum), graded, 1.22);
    // 3) gentle S-curve contrast around mid-grey: deepen shadows, keep highlights.
    graded = clamp(graded, 0.0, 1.0);
    graded = graded * graded * (3.0 - 2.0 * graded);   // smoothstep contrast
    graded = mix(withBloom * tint, graded, 0.35);       // blend so it stays subtle
    graded = clamp(graded, 0.0, 2.0);

    // --- user brightness / gamma (accessibility, W9) ---
    // A TRUE post-grade correction, applied before the vignette so the frame border keeps
    // its shape at every setting. Replaces the old translucent white/black overlay quad,
    // which desaturated ('washed') the whole frame when brightening — turning brightness
    // UP used to make the game LESS readable. uBright scales linearly; uGamma lifts or
    // sinks the midtones without clipping blacks/whites. Neutral (1.0 / 1.0) is a no-op.
    // Both uniforms are uploaded EVERY frame (an unset uniform reads 0 -> 1/0 -> black frame).
    graded = pow(clamp(graded * uBright, 0.0, 1.0), vec3(1.0 / uGamma));

    // --- vignette: a clear frame around the busy board ---
    // Two-stage: a wide gentle darken across the outer frame + a sharper corner cinch.
    // Centre (edge<~0.45) is untouched; corners lose ~16-20% so the eye is drawn inward.
    float v1 = smoothstep(0.55, 1.05, edge);        // wide outer falloff
    float v2 = smoothstep(0.80, 1.25, edge);        // tight corner cinch
    float vig = 1.0 - v1 * 0.13 - v2 * 0.10;        // ~0.13 frame + extra ~0.10 at corners
    graded *= vig;

    // Slight gamma to keep the output natural (not washed out).
    graded = pow(clamp(graded, 0.0, 1.0), vec3(0.96));

    finalColor = vec4(graded, 1.0);
}
";

    // ---- window / size settings ----
    public static readonly (int w, int h)[] Sizes =
        { (1280, 800), (1600, 1000), (1920, 1200), (2560, 1600), (3200, 2000) };
    public static int SizeIdx;
    public static bool Fullscreen;

    // accessibility (3.13): a screen brightness post-pass + a colorblind palette toggle
    public static readonly float[] BrightLevels = { 0.70f, 0.85f, 1.00f, 1.15f, 1.30f };
    public static int BrightIdx = 2;   // 1.00 = neutral (no overlay)
    public static float Brightness => BrightLevels[Math.Clamp(BrightIdx, 0, BrightLevels.Length - 1)];
    public static string BrightLabel => $"{(int)(Brightness * 100)}%";

    // accessibility (W9): a TRUE gamma correction, applied in-shader (uGamma). Neutral = 1.00.
    // Gamma defaults to 1.0f by construction (GammaIdx 2) — the shader divides by uGamma, so a
    // 0 value would black the frame; keep the default neutral and always upload it (see
    // UploadFxUniforms). With PostFX off gamma has no effect (no quad can approximate it).
    public static readonly float[] GammaLevels = { 0.80f, 0.90f, 1.00f, 1.15f, 1.30f };
    public static int GammaIdx = 2;    // 1.00 = neutral
    public static float Gamma => GammaLevels[Math.Clamp(GammaIdx, 0, GammaLevels.Length - 1)];
    public static string GammaLabel => $"{Gamma:0.00}";

    public static string SizeLabel => Fullscreen ? "FULLSCREEN" : $"{Sizes[SizeIdx].w} x {Sizes[SizeIdx].h}";

    public static void CycleBrightness()
    {
        BrightIdx = (BrightIdx + 1) % BrightLevels.Length;
        Save();
    }

    public static void CycleGamma()
    {
        GammaIdx = (GammaIdx + 1) % GammaLevels.Length;
        Save();
    }

    public static void ToggleColorblind()
    {
        Pal.SetColorblind(!Pal.Colorblind);
        Save();
    }

    public static void TogglePostFX()
    {
        PostFX = !PostFX;
        Save();
    }

    // auto-cam: optional character-focus camera that follows the selected/acting unit
    public static bool AutoCam;
    public static void ToggleAutoCam() { AutoCam = !AutoCam; Save(); }

    // onboarding tutorial (3.12): a one-time "seen" flag so it only shows on the first run
    public static bool TutorialSeen;
    public static void MarkTutorialSeen() { if (!TutorialSeen) { TutorialSeen = true; Save(); } }

    // FUL-12: one-shot BRACE field-tip flag (same lifecycle as TutorialSeen — the callout fires
    // once per profile, the first time a live fight makes the reaction verb relevant).
    // T1: superseded by the TipsSeen bitmask below (bit 0 IS the brace tip). The field is kept as
    // the on-disk migration bridge in BOTH directions: Load folds an old profile's true into bit 0,
    // and Save keeps writing it from bit 0 so a downgrade doesn't re-show a tip the player has read.
    public static bool BraceTipSeen;
    public static void MarkBraceTipSeen() { MarkTipSeen(0); }

    // ── PROGRAM RESONANCE T1 — just-in-time field tips ──────────────────────────────────────
    // One bit per tip in Game.FieldTips (index == bit). A bitmask rather than a bool-per-tip so
    // the DTO grows by ONE field for the whole table; absent in an old display.json = 0 = unseen.
    // Cap is 32 tips — TipCount asserts against it in TUTTEST so a 33rd tip can't silently no-op.
    public const int MaxTips = 32;
    public static int TipsSeen;
    public static bool TipSeen(int i) => i >= 0 && i < MaxTips && (TipsSeen & (1 << i)) != 0;
    public static void MarkTipSeen(int i)
    {
        if (i < 0 || i >= MaxTips || TipSeen(i)) return;
        TipsSeen |= 1 << i;
        if (i == 0) BraceTipSeen = true;   // keep the legacy field in step for the downgrade bridge
        Save();
    }

    // T1: the TRAINING OP has been completed (or explicitly declined) at least once. Drives the
    // first-launch offer only — the drill itself stays reachable from the intro forever.
    public static bool TrainingSeen;
    public static void MarkTrainingSeen() { if (!TrainingSeen) { TrainingSeen = true; Save(); } }

    // T1: the permanent SHOW ALL escape. Verb staging (training op + mission 1) never locks a
    // returning player out of a verb they already know — one toggle, remembered per profile.
    public static bool ShowAllVerbs;
    public static void ToggleShowAllVerbs() { ShowAllVerbs = !ShowAllVerbs; Save(); }

    public static void Init(bool enabled)
    {
        Enabled = enabled;
        if (!enabled) return;
        _target = Raylib.LoadRenderTexture(Cfg.ScreenW, Cfg.ScreenH);
        Raylib.SetTextureFilter(_target.Texture, TextureFilter.Bilinear);
        Raylib.SetWindowState(ConfigFlags.ResizableWindow);   // let the user free-resize too

        // Load the post-FX shader (embedded GLSL; null vertex = use Raylib default).
        _fx = Raylib.LoadShaderFromMemory(null, FsSrc);
        _fxReady = Raylib.IsShaderValid(_fx);
        if (_fxReady)
        {
            _locResolution = Raylib.GetShaderLocation(_fx, "uResolution");
            _locBloom      = Raylib.GetShaderLocation(_fx, "uBloom");
            _locChroma     = Raylib.GetShaderLocation(_fx, "uChroma");
            _locGrade      = Raylib.GetShaderLocation(_fx, "uGrade");
            _locTime       = Raylib.GetShaderLocation(_fx, "uTime");
            _locBright     = Raylib.GetShaderLocation(_fx, "uBright");
            _locGamma      = Raylib.GetShaderLocation(_fx, "uGamma");
        }

        Load();
        Apply();
    }

    public static void Shutdown()
    {
        if (!Enabled) return;
        if (_fxReady) Raylib.UnloadShader(_fx);
        Raylib.UnloadRenderTexture(_target);
    }

    static bool Scaled => Enabled && (Fullscreen ||
        Raylib.GetScreenWidth() != Cfg.ScreenW || Raylib.GetScreenHeight() != Cfg.ScreenH);

    static float Scale()
    {
        float ww = Raylib.GetScreenWidth(), wh = Raylib.GetScreenHeight();
        return MathF.Max(0.1f, MathF.Min(ww / Cfg.ScreenW, wh / Cfg.ScreenH));
    }

    static Vector2 Offset()
    {
        float s = Scale();
        return new Vector2((Raylib.GetScreenWidth() - Cfg.ScreenW * s) / 2f,
                           (Raylib.GetScreenHeight() - Cfg.ScreenH * s) / 2f);
    }

    /// Map the OS cursor into virtual 1280x800 space so all GetMousePosition() callers
    /// work unchanged. Raylib returns (real + offset) * scale.
    public static void UpdateMouse()
    {
        if (!Scaled) { Raylib.SetMouseScale(1f, 1f); Raylib.SetMouseOffset(0, 0); return; }
        float s = Scale(); var o = Offset();
        Raylib.SetMouseScale(1f / s, 1f / s);
        Raylib.SetMouseOffset((int)(-o.X), (int)(-o.Y));
    }

    /// Advance the shader time uniform (call from the game loop, same dt as Update).
    /// Safe no-op when Display is disabled.
    public static void AdvanceTime(float dt) { if (Enabled) FxTime += dt; }

    /// Run the frame's drawing. When PostFX is active the game always renders to the
    /// render-target first, then the shader blit is applied to the screen.
    /// When PostFX is off the original Scaled / non-Scaled paths are preserved exactly.
    public static void RenderFrame(Action draw)
    {
        bool applyFx = Enabled && PostFX && _fxReady;

        if (applyFx)
        {
            // Always render into the render-target so the shader has a full-res source.
            Raylib.BeginTextureMode(_target);
            draw();
            Raylib.EndTextureMode();

            // Upload uniforms.
            UploadFxUniforms();

            // Blit to screen (scaled + letterboxed if needed, or 1:1).
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
            Raylib.BeginShaderMode(_fx);
            if (Scaled)
            {
                float s = Scale(); var o = Offset();
                var src = new Rectangle(0, 0, Cfg.ScreenW, -Cfg.ScreenH);   // flip Y
                var dst = new Rectangle(o.X, o.Y, Cfg.ScreenW * s, Cfg.ScreenH * s);
                Raylib.DrawTexturePro(_target.Texture, src, dst, Vector2.Zero, 0f, Color.White);
            }
            else
            {
                // 1:1 — flip Y for render texture convention.
                var src = new Rectangle(0, 0, Cfg.ScreenW, -Cfg.ScreenH);
                var dst = new Rectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH);
                Raylib.DrawTexturePro(_target.Texture, src, dst, Vector2.Zero, 0f, Color.White);
            }
            Raylib.EndShaderMode();
            // W9: no brightness quad here — the shader's uBright/uGamma pass IS the
            // brightness/gamma correction when PostFX is active (no more white wash).
            Raylib.EndDrawing();
            return;
        }

        // --- original paths (no post-FX) ---
        if (!Scaled)
        {
            Raylib.BeginDrawing();
            draw();
            DrawBrightness();
            Raylib.EndDrawing();
            return;
        }

        Raylib.BeginTextureMode(_target);
        draw();
        Raylib.EndTextureMode();

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
        float sc = Scale(); var off = Offset();
        var sr = new Rectangle(0, 0, Cfg.ScreenW, -Cfg.ScreenH);   // flip Y
        var dr = new Rectangle(off.X, off.Y, Cfg.ScreenW * sc, Cfg.ScreenH * sc);
        Raylib.DrawTexturePro(_target.Texture, sr, dr, Vector2.Zero, 0f, Color.White);
        DrawBrightness();
        Raylib.EndDrawing();
    }

    static void UploadFxUniforms()
    {
        // Resolution uniform — always the render target size (1280x800).
        var res = new Vector2(Cfg.ScreenW, Cfg.ScreenH);
        Raylib.SetShaderValue(_fx, _locResolution, res,      ShaderUniformDataType.Vec2);
        Raylib.SetShaderValue(_fx, _locBloom,      BloomIntensity, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_fx, _locChroma,     ChromaIntensity, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_fx, _locGrade,      GradeTint,  ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_fx, _locTime,       FxTime,     ShaderUniformDataType.Float);
        // W9: brightness/gamma ride the shader now (a real correction, not a washing quad).
        // BOTH must be uploaded EVERY frame — an uninitialized uniform reads 0, and the
        // shader computes 1/uGamma, so a skipped upload would render a black frame.
        Raylib.SetShaderValue(_fx, _locBright,     Brightness, ShaderUniformDataType.Float);
        Raylib.SetShaderValue(_fx, _locGamma,      Gamma,      ShaderUniformDataType.Float);
    }

    // Brightness FALLBACK (W9): survives only for the !PostFX paths — when the shader is
    // active, brightness/gamma are applied in-shader (uBright/uGamma) instead, because this
    // translucent lighten quad WASHES the frame (raising brightness lowered readability).
    // Neutral (100%) draws nothing, so the headless harness stays byte-identical.
    static void DrawBrightness()
    {
        float b = Brightness;
        if (b > 0.99f && b < 1.01f) return;
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        if (b < 1f) Raylib.DrawRectangle(0, 0, w, h, Raylib.Fade(Pal.RGBA(0, 0, 0), 1f - b));
        else        Raylib.DrawRectangle(0, 0, w, h, Raylib.Fade(Pal.RGBA(255, 255, 255), (b - 1f) * 0.55f));
    }

    public static void ToggleFullscreen()
    {
        if (!Enabled) return;
        Fullscreen = !Fullscreen;
        Apply();
        Save();
    }

    public static void CycleSize()
    {
        if (!Enabled) return;
        if (Fullscreen) Fullscreen = false;                 // leaving fullscreen lands on the current size
        else SizeIdx = (SizeIdx + 1) % Sizes.Length;
        Apply();
        Save();
    }

    static void Apply()
    {
        if (!Enabled) return;
        if (Fullscreen)
        {
            int mon = Raylib.GetCurrentMonitor();
            Raylib.SetWindowSize(Raylib.GetMonitorWidth(mon), Raylib.GetMonitorHeight(mon));
            if (!Raylib.IsWindowFullscreen()) Raylib.ToggleFullscreen();
        }
        else
        {
            if (Raylib.IsWindowFullscreen()) Raylib.ToggleFullscreen();
            var (w, h) = Sizes[SizeIdx];
            Raylib.SetWindowSize(w, h);
            int mon = Raylib.GetCurrentMonitor();
            Raylib.SetWindowPosition((Raylib.GetMonitorWidth(mon) - w) / 2,
                                     (Raylib.GetMonitorHeight(mon) - h) / 2);
        }
    }

    // ---- persistence (alongside the save file, not in the repo) ----
    class Dto
    {
        public bool Fullscreen { get; set; }
        public int SizeIdx { get; set; }
        public int BrightIdx { get; set; } = 2;
        public int GammaIdx { get; set; } = 2;   // W9: JSON default keeps old display.json neutral (back-compat)
        public bool Colorblind { get; set; }
        public bool TutorialSeen { get; set; }
        public bool PostFX { get; set; } = true;
        public bool AutoCam { get; set; }
        public bool BraceTipSeen { get; set; }   // FUL-12 (JSON field: absent in old files = false, back-compat)
        public int TipsSeen { get; set; }        // T1 just-in-time tip bitmask (absent = 0 = all unseen)
        public bool TrainingSeen { get; set; }   // T1 training op completed/declined once
        public bool ShowAllVerbs { get; set; }   // T1 permanent staging escape
    }
    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sightline");
    static string FilePath => Path.Combine(Dir, "display.json");

    static void Save()
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(new Dto { Fullscreen = Fullscreen, SizeIdx = SizeIdx, BrightIdx = BrightIdx, GammaIdx = GammaIdx, Colorblind = Pal.Colorblind, TutorialSeen = TutorialSeen, PostFX = PostFX, AutoCam = AutoCam, BraceTipSeen = (TipsSeen & 1) != 0, TipsSeen = TipsSeen, TrainingSeen = TrainingSeen, ShowAllVerbs = ShowAllVerbs })); }
        catch { }
    }

    /// Harness seam (SIGHTLINE_TUTTEST): the settings-file path plus explicit Save/Load, so the
    /// onboarding self-test can round-trip the seen-flags through REAL JSON (not a field copy) and
    /// then hand the player's file back byte-for-byte. Not used by gameplay code.
    public static string SettingsPathPublic => FilePath;
    public static void SaveForTest() => Save();
    public static void LoadForTest() => Load();

    static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var d = JsonSerializer.Deserialize<Dto>(File.ReadAllText(FilePath));
            if (d != null)
            {
                Fullscreen = d.Fullscreen;
                SizeIdx = Math.Clamp(d.SizeIdx, 0, Sizes.Length - 1);
                BrightIdx = Math.Clamp(d.BrightIdx, 0, BrightLevels.Length - 1);
                GammaIdx = Math.Clamp(d.GammaIdx, 0, GammaLevels.Length - 1);
                Pal.SetColorblind(d.Colorblind);
                TutorialSeen = d.TutorialSeen;
                PostFX = d.PostFX;
                AutoCam = d.AutoCam;
                BraceTipSeen = d.BraceTipSeen;
                // T1 migration bridge: an old profile only has the single BraceTipSeen bool — fold
                // it into bit 0 so a player who already read the BRACE tip never sees it again.
                TipsSeen = d.TipsSeen | (d.BraceTipSeen ? 1 : 0);
                BraceTipSeen = (TipsSeen & 1) != 0;
                TrainingSeen = d.TrainingSeen;
                ShowAllVerbs = d.ShowAllVerbs;
            }
        }
        catch { }
    }
}
