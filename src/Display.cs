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
/// smoke-test path carries no render-target or shader state (NOT because shots are
/// byte-identical - they are not; see the note on the Display class below).
///
/// Phase 5.2 adds an optional post-FX shader pass (bloom + vignette + biome colour
/// grading + chromatic aberration). The pass is always OFF when Display is disabled
/// (headless harness), which keeps the shot path free of shader state. NOTE (RESONANCE):
/// this comment used to claim plain SIGHTLINE_SHOT screenshots are byte-identical - they
/// are NOT. Two identical shot runs differ in ~30% of pixels (58 Raylib.GetTime() reads in
/// the renderer/HUD plus a clock-seeded Util.Rng). Never gate on a screenshot hash;
/// SIGHTLINE_PAIRTEST byte-identity is the real determinism gate.
/// Enable for verification with SIGHTLINE_POSTFX=1 (forces Display.Init(true) even
/// during shot mode and sets a strong demo bloom so the effect is clearly visible).
public static partial class Display
{
    public static bool Enabled;
    static RenderTexture2D _target;

    // ---- post-FX shader (Phase 5.2) ----
    // PostFX is enabled by default whenever Display is enabled (i.e. live game).
    // It is always OFF when Display is disabled, so the headless smoke-test path carries no
    // shader state. Toggle via the pause menu or set Display.PostFX = false.
    public static bool PostFX = true;
    static Shader _fx;
    static bool   _fxReady;

    // P1 two-pass half-res bloom + the film-grain tile. All of it lives behind `Enabled`, so
    // the headless harness (Display.Init(false)) allocates none of it and carries no shader state.
    static RenderTexture2D _bloomA, _bloomB;
    static Shader _bright, _blur;
    static bool   _bloomReady;
    static Texture2D _noise;
    static bool   _noiseReady;
    static int _locBloomTex, _locNoiseTex;
    static int _brLocTexel, _blLocDir;
    // Half-res bloom buffer size, and the blur radius expressed in HALF-RES texels. 1.6 puts the
    // outer gaussian tap at ~5.2 half-res texels = ~10.3 full-res px, roughly double the reach of
    // the old 5px single-pass ring.
    static int BloomW => Cfg.ScreenW / 2;
    static int BloomH => Cfg.ScreenH / 2;
    const float BlurRadius = 1.6f;

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

// PROGRAM RESONANCE P1 — the bloom is no longer computed here.
// It is pre-built by a TWO-PASS, HALF-RES chain (see FsBrightSrc / FsBlurSrc + BuildBloom):
//   pass 1  bright-extract + 4-tap box downsample   1280x800 -> 640x400
//   pass 2  separable gaussian, horizontal          640x400
//   pass 3  separable gaussian, vertical            640x400
// That is ~5.6M texel fetches against the old single-pass 12-tap-at-full-res 12.3M, and it
// buys a MUCH wider, smoother halo than a 5px radial ring could ever reach. The composite
// just reads the finished glow. The bright-pass KNEE IS UNCHANGED at 0.36 (V3 raised its
// cover-rim and lip alphas assuming that knee; moving it would blow those rims out), and the
// threshold is still applied PER TAP before the box average, so a 1px rim still crosses it.
uniform sampler2D uBloomTex;   // half-res, pre-blurred bright pass
uniform sampler2D uNoiseTex;   // 256x256 GenImageWhiteNoise, repeat-wrapped, for film grain

// ACES (Narkowicz fit). Applied ONLY over the highlight band — see the note at the call site.
vec3 acesFilm(vec3 x) {
    return clamp((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14), 0.0, 1.0);
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
    vec3 glow = texture(uBloomTex, uv).rgb;
    // HORIZON W5 — keep the resting halo restrained (not a baseline wash) but RAISE the
    // reactive ceiling so a KILL/crit (Game.AddBloom spikes uBloom, then decays) visibly
    // FLOODS the screen with light before settling. Reactive, not always-on.
    // P1: the gaussian conserves energy across a ~10px full-res radius instead of concentrating
    // it in a 5px ring, so the PEAK off a small source is lower — the amounts are scaled up to
    // land the resting halo where W5 tuned it. Knee untouched, so what blooms is unchanged.
    float bloomAmt = 1.45 + uBloom * 4.30;    // restrained baseline halo + a big reactive spike
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

    // --- ACES-ish filmic shoulder (P1) ---
    // Applied ONLY over the highlight band. A FULL-RANGE ACES is wrong for this game: the
    // Narkowicz fit expects scene-linear input, and against our already display-referred frame
    // it maps the board median (0.26) to 0.39 and white to 0.80 — i.e. it washes the dark board
    // out AND dims the UI, undoing V2's re-grade. Weighted to the highlights it does the one
    // job we actually want from a tonemap: the blown bloom core stops clipping to a flat white
    // disc and gets gradation back, while everything at or below 0.70 luma is bit-identical.
    vec3 tm = acesFilm(graded);
    graded = mix(clamp(graded, 0.0, 1.0), tm, smoothstep(0.85, 1.60, luma(graded)) * 0.75);

    // --- vignette: a clear frame around the busy board ---
    // Two-stage: a wide gentle darken across the outer frame + a sharper corner cinch.
    // Centre (edge<~0.45) is untouched; corners lose ~16-20% so the eye is drawn inward.
    float v1 = smoothstep(0.55, 1.05, edge);        // wide outer falloff
    float v2 = smoothstep(0.80, 1.25, edge);        // tight corner cinch
    float vig = 1.0 - v1 * 0.13 - v2 * 0.10;        // ~0.13 frame + extra ~0.10 at corners
    graded *= vig;

    // --- tactical-display framing: a faint horizontal scan + film grain (P1) ---
    // Both are deliberately at the edge of perception. The scan is a 3px-period cosine at
    // <=0.03 amplitude — enough to read as a CRT/tac-display surface, not enough to fight the
    // board. The grain is a repeat-wrapped white-noise tile scrolled by uTime (which is fed by
    // Display.AdvanceTime(dt) — no wall-clock read anywhere in the FX path) and is FADED OUT
    // in the darks, so the black board floor and the letterbox stay clean instead of speckling.
    float scan = 1.0 - 0.028 * (0.5 + 0.5 * cos(uv.y * uResolution.y * 2.0943951));
    graded *= scan;

    vec2 gUv = uv * (uResolution / 256.0) + vec2(fract(uTime * 11.0), fract(uTime * 7.0));
    float grain = texture(uNoiseTex, gUv).r - 0.5;
    graded += vec3(grain) * 0.025 * (0.25 + 0.75 * smoothstep(0.0, 0.30, luma(graded)));

    // Slight gamma to keep the output natural (not washed out).
    graded = pow(clamp(graded, 0.0, 1.0), vec3(0.96));

    finalColor = vec4(graded, 1.0);
}
";

    // ── P1 BLOOM CHAIN — pass 1: bright extract + 4-tap box downsample (full -> half res) ──
    // The threshold is applied PER TAP and only then averaged, so a 1px-wide bright rim still
    // crosses the knee. Averaging first would have quietly killed V3's thin cover rims.
    const string FsBrightSrc = @"#version 330 core
in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;
uniform sampler2D texture0;
uniform vec2 uTexel;          // 1.0 / full-resolution, in UV units

float luma(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

vec3 tap(vec2 uv) {
    vec3 s = texture(texture0, uv).rgb;
    // Knee at 0.36 — IDENTICAL to the pre-P1 single-pass bloom. V3 raised its cover-rim and
    // lip alphas against this exact value; do not move it without re-checking V3's captures.
    float b = smoothstep(0.36, 0.85, luma(s));
    return s * b * b;
}

void main() {
    vec2 uv = fragTexCoord;
    vec3 s = tap(uv + vec2(-uTexel.x, -uTexel.y))
           + tap(uv + vec2( uTexel.x, -uTexel.y))
           + tap(uv + vec2(-uTexel.x,  uTexel.y))
           + tap(uv + vec2( uTexel.x,  uTexel.y));
    finalColor = vec4(s * 0.25, 1.0);
}
";

    // ── P1 BLOOM CHAIN — passes 2 & 3: separable gaussian at half res ──
    // The classic 5-fetch linear-sampled kernel (equivalent to a 9-tap gaussian). Run once
    // horizontally and once vertically; uDir already carries the radius.
    const string FsBlurSrc = @"#version 330 core
in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;
uniform sampler2D texture0;
uniform vec2 uDir;            // per-pass blur axis, in UV units (radius baked in)

void main() {
    vec2 uv = fragTexCoord;
    vec3 c = texture(texture0, uv).rgb * 0.2270270270;
    c += (texture(texture0, uv + uDir * 1.3846153846).rgb
        + texture(texture0, uv - uDir * 1.3846153846).rgb) * 0.3162162162;
    c += (texture(texture0, uv + uDir * 3.2307692308).rgb
        + texture(texture0, uv - uDir * 3.2307692308).rgb) * 0.0702702703;
    finalColor = vec4(c, 1.0);
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

    // ---- W5 ON-RAMP: comfort controls (animation pacing + UI text size) ------------------
    // ANIMATION SPEED. A roguelike campaign replays the same ~0.5 s telegraph beat hundreds of
    // times; a playback multiplier is the single most-asked-for comfort control in the genre.
    // The value is a pure dt MULTIPLIER applied at exactly ONE place (Game.Update's
    // `a.Update(this, t * AnimSpeed)`): the queue's state machine is untouched, no activation is
    // skipped, and OnStart still fires only when an anim becomes ACTIVE. Anything that drained
    // the queue faster by other means would resurrect the movement-jitter bug.
    // Game.AnimSpeed pins 1x under AutoPlay/NoPersist, so the harness and the flywheel never see it.
    public static readonly float[] AnimSpeedLevels = { 1.00f, 1.50f, 2.00f, 3.00f };
    public static int AnimSpeedIdx;   // 0 = 1x (the shipped pacing)
    public static float AnimSpeed => AnimSpeedLevels[Math.Clamp(AnimSpeedIdx, 0, AnimSpeedLevels.Length - 1)];
    public static string AnimSpeedLabel => AnimSpeed == 1f ? "1x" : $"{AnimSpeed:0.##}x";
    public static void CycleAnimSpeed()
    {
        AnimSpeedIdx = (AnimSpeedIdx + 1) % AnimSpeedLevels.Length;
        Save();
    }

    // UI TEXT SIZE. Applied inside Cfg.Text/Cfg.Measure (and the title pair), which every draw
    // site in the game already routes through — so a label's MEASURE and its DRAW scale together
    // by construction. That symmetry is the whole safety argument: several call sites measure via
    // a wrap/clip/centre helper and paint separately, and a scale applied to only one of the two
    // wraps at one size and paints at another. The atlas choice still routes on the UNSCALED size
    // so body text keeps coming off the crisp 20px UI bake.
    // Never applied headless: Display.Init(false) skips Load(), so Cfg.UiScale stays 1.0 and every
    // screenshot / self-test / balance run measures the shipped layout.
    public static readonly float[] UiScaleLevels = { 0.90f, 1.00f, 1.10f, 1.20f };
    public static int UiScaleIdx = 1;   // 1.00 = the authored layout
    public static float UiScale => UiScaleLevels[Math.Clamp(UiScaleIdx, 0, UiScaleLevels.Length - 1)];
    public static string UiScaleLabel => $"{(int)MathF.Round(UiScale * 100f)}%";
    public static void CycleUiScale()
    {
        UiScaleIdx = (UiScaleIdx + 1) % UiScaleLevels.Length;
        ApplyUiScale();
        Save();
    }
    /// Push the chosen scale into Cfg (the one place text size is resolved).
    public static void ApplyUiScale() => Cfg.UiScale = UiScale;

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

    // ---- RESONANCE A2: per-category audio mix (persisted here alongside the other settings) ----
    // The whole game shipped with exactly one hard-coded SetMasterVolume(0.6f) and a binary
    // mute, so the owner could not rebalance music against SFX without a rebuild. These four
    // faders are read by Audio (master at the device, the rest per-cue / per-stream).
    // Defaults reproduce the old behaviour exactly: master 0.60, everything else unity.
    public static float VolMaster = 0.60f;
    public static float VolSfx    = 1.00f;
    public static float VolMusic  = 1.00f;
    public static float VolUi     = 1.00f;
    public static readonly string[] VolNames = { "MASTER", "SFX", "MUSIC", "UI" };

    public static float Vol(int bus) => bus switch { 0 => VolMaster, 1 => VolSfx, 2 => VolMusic, _ => VolUi };

    /// Live-set one fader (no disk write — a slider drag calls this every frame).
    public static void SetVol(int bus, float v)
    {
        v = Math.Clamp(v, 0f, 1f);
        switch (bus)
        {
            case 0: VolMaster = v; Audio.ApplyMasterVolume(); break;
            case 1: VolSfx = v; break;
            case 2: VolMusic = v; break;
            default: VolUi = v; break;
        }
    }
    /// Persist the faders — call once when the drag ends, not per frame.
    public static void CommitVol() => Save();

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
            _locBloomTex   = Raylib.GetShaderLocation(_fx, "uBloomTex");
            _locNoiseTex   = Raylib.GetShaderLocation(_fx, "uNoiseTex");
        }

        // P1: the two-pass bloom chain. If either shader fails to compile we simply leave
        // _bloomReady false — the composite then reads an unbound (black) uBloomTex, i.e. the
        // frame renders with no bloom rather than not rendering at all.
        _bright = Raylib.LoadShaderFromMemory(null, FsBrightSrc);
        _blur   = Raylib.LoadShaderFromMemory(null, FsBlurSrc);
        _bloomReady = Raylib.IsShaderValid(_bright) && Raylib.IsShaderValid(_blur);
        if (_bloomReady)
        {
            _brLocTexel = Raylib.GetShaderLocation(_bright, "uTexel");
            _blLocDir   = Raylib.GetShaderLocation(_blur, "uDir");
            _bloomA = Raylib.LoadRenderTexture(BloomW, BloomH);
            _bloomB = Raylib.LoadRenderTexture(BloomW, BloomH);
            Raylib.SetTextureFilter(_bloomA.Texture, TextureFilter.Bilinear);
            Raylib.SetTextureFilter(_bloomB.Texture, TextureFilter.Bilinear);
        }

        // P1: film-grain tile — generated in-engine (GenImageWhiteNoise), so nothing is
        // committed to the repo. Point-filtered + repeat-wrapped: crisp grain, free tiling.
        var noiseImg = Raylib.GenImageWhiteNoise(256, 256, 0.5f);
        _noise = Raylib.LoadTextureFromImage(noiseImg);
        Raylib.UnloadImage(noiseImg);
        _noiseReady = Raylib.IsTextureValid(_noise);
        if (_noiseReady)
        {
            Raylib.SetTextureWrap(_noise, TextureWrap.Repeat);
            Raylib.SetTextureFilter(_noise, TextureFilter.Point);
        }

        Load();
        Apply();
    }

    public static void Shutdown()
    {
        if (!Enabled) return;
        if (_fxReady) Raylib.UnloadShader(_fx);
        if (_bloomReady)
        {
            Raylib.UnloadShader(_bright);
            Raylib.UnloadShader(_blur);
            Raylib.UnloadRenderTexture(_bloomA);
            Raylib.UnloadRenderTexture(_bloomB);
        }
        if (_noiseReady) Raylib.UnloadTexture(_noise);
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

            // P1: build the half-res bloom from that frame before compositing.
            BuildBloom();

            // Upload uniforms.
            UploadFxUniforms();

            // Blit to screen (scaled + letterboxed if needed, or 1:1).
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
            Raylib.BeginShaderMode(_fx);
            // P1 extra samplers, bound AFTER BeginShaderMode on purpose. BeginShaderMode flushes
            // rlgl's batch, and that flush ZEROES the active-texture-slot table — binding these in
            // UploadFxUniforms (before the shader is made current) left uBloomTex reading black.
            BindFxSamplers();
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

    /// P1: bright-extract + separable-gaussian bloom, both blur passes at half resolution.
    /// EVERY intermediate blit uses a NEGATIVE source height. Both the source and the
    /// destination are bottom-up render textures, so a flipped blit is the one that PRESERVES
    /// raw-texel orientation — which is what lets the composite sample uBloomTex with exactly
    /// the same fragTexCoord it uses for texture0. Drop the flip and the glow lands upside-down.
    static void BuildBloom()
    {
        if (!_bloomReady) return;
        var fullSrc = new Rectangle(0, 0, Cfg.ScreenW, -Cfg.ScreenH);
        var halfSrc = new Rectangle(0, 0, BloomW, -BloomH);
        var halfDst = new Rectangle(0, 0, BloomW, BloomH);

        // pass 1 — bright extract, downsampled to half res.
        Raylib.SetShaderValue(_bright, _brLocTexel,
            new Vector2(1f / Cfg.ScreenW, 1f / Cfg.ScreenH), ShaderUniformDataType.Vec2);
        Raylib.BeginTextureMode(_bloomA);
        Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
        Raylib.BeginShaderMode(_bright);
        Raylib.DrawTexturePro(_target.Texture, fullSrc, halfDst, Vector2.Zero, 0f, Color.White);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();

        // pass 2 — horizontal gaussian.
        Raylib.SetShaderValue(_blur, _blLocDir,
            new Vector2(BlurRadius / BloomW, 0f), ShaderUniformDataType.Vec2);
        Raylib.BeginTextureMode(_bloomB);
        Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
        Raylib.BeginShaderMode(_blur);
        Raylib.DrawTexturePro(_bloomA.Texture, halfSrc, halfDst, Vector2.Zero, 0f, Color.White);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();

        // pass 3 — vertical gaussian, back into A (which the composite samples).
        Raylib.SetShaderValue(_blur, _blLocDir,
            new Vector2(0f, BlurRadius / BloomH), ShaderUniformDataType.Vec2);
        Raylib.BeginTextureMode(_bloomA);
        Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
        Raylib.BeginShaderMode(_blur);
        Raylib.DrawTexturePro(_bloomB.Texture, halfSrc, halfDst, Vector2.Zero, 0f, Color.White);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
    }

    /// Attach the two extra samplers the composite reads. MUST be called while _fx is the
    /// current shader and before the composite draw — see the call site.
    static void BindFxSamplers()
    {
        if (_bloomReady) Raylib.SetShaderValueTexture(_fx, _locBloomTex, _bloomA.Texture);
        if (_noiseReady) Raylib.SetShaderValueTexture(_fx, _locNoiseTex, _noise);
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
    // Neutral (100%) draws nothing, so the headless harness never takes this path.
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
    internal class Dto
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
        // RESONANCE A2 — additive fields; a display.json written before A2 has none of them,
        // so these JSON defaults are what an existing install keeps (== the old behaviour).
        public float VolMaster { get; set; } = 0.60f;
        public float VolSfx { get; set; } = 1.00f;
        public float VolMusic { get; set; } = 1.00f;
        public float VolUi { get; set; } = 1.00f;
        // W5 ON-RAMP — additive again; absent in an older display.json, so these defaults are
        // exactly the pre-W5 behaviour (1x playback, 100% text).
        public int AnimSpeedIdx { get; set; }        // absent = 0 = 1x
        public int UiScaleIdx { get; set; } = 1;     // absent = 1 = 100%
        // W6 KEY REBINDING — the keymap, as a plain `id=KeyName;...` string of OVERRIDES ONLY
        // (see Keymap.Encode). Deliberately NOT a new enum and NOT an ordinal table: keys are
        // stored by their Raylib enum NAME, so nothing here touches the APPEND-ONLY persisted-enum
        // fingerprints in SaveGame, and a human can read (and fix) the line in a text editor.
        // Absent in any display.json written before W6 = "" = the shipped defaults.
        public string Keys { get; set; } = "";
    }
    // Source-generated serializer (see SaveGame.SaveJson for the why): reflection-based
    // System.Text.Json loses its type metadata under `-p:PublishTrimmed=true`, which silently
    // breaks settings persistence in a trimmed distributable.
    [System.Text.Json.Serialization.JsonSerializable(typeof(Dto))]
    internal partial class DisplayJson : System.Text.Json.Serialization.JsonSerializerContext { }

    /// R1 REVIEW FIX — share SaveGame's guarded config root rather than re-deriving it. The old
    /// body was `Path.Combine(GetFolderPath(ApplicationData), "Sightline")`, which misses the
    /// hazard SaveGame.Dir documents: GetFolderPath uses SpecialFolderOption.None and returns ""
    /// when the resolved directory does not exist yet, so Path.Combine("", "Sightline") yields a
    /// RELATIVE path. Settings then scattered per launch directory (and looked to the player like
    /// a reset) while saves and meta went to the right place. One derivation, one guard.
    static string Dir => SaveGame.ConfigDir;
    static string FilePath => Path.Combine(Dir, "display.json");

    static void Save()
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(new Dto { Fullscreen = Fullscreen, SizeIdx = SizeIdx, BrightIdx = BrightIdx, GammaIdx = GammaIdx, Colorblind = Pal.Colorblind, TutorialSeen = TutorialSeen, PostFX = PostFX, AutoCam = AutoCam, BraceTipSeen = (TipsSeen & 1) != 0, TipsSeen = TipsSeen, TrainingSeen = TrainingSeen, ShowAllVerbs = ShowAllVerbs, VolMaster = VolMaster, VolSfx = VolSfx, VolMusic = VolMusic, VolUi = VolUi, AnimSpeedIdx = AnimSpeedIdx, UiScaleIdx = UiScaleIdx, Keys = Keymap.Encode() }, DisplayJson.Default.Dto)); }
        catch { }
    }

    /// Harness seam (SIGHTLINE_TUTTEST): the settings-file path plus explicit Save/Load, so the
    /// onboarding self-test can round-trip the seen-flags through REAL JSON (not a field copy) and
    /// then hand the player's file back byte-for-byte. Not used by gameplay code.
    public static string SettingsPathPublic => FilePath;
    public static void SaveForTest() => Save();
    public static void LoadForTest() => Load();

    /// W6: how many keymap overrides the last Load had to drop (unparseable, unknown, reserved, or
    /// conflicting). Non-zero means the CONTROLS screen shows a "settings were repaired" note
    /// instead of silently pretending the file was fine.
    public static int KeysDropped;

    /// Persist the keymap. Called by Keymap after every accepted rebind / reset — the whole
    /// settings blob is rewritten, which is what every other pause-menu control already does.
    /// A no-op when Display is disabled (headless), so no self-test or balance run writes a map.
    public static void SaveKeymap() { if (Enabled) Save(); }

    /// Set once a corrupt display.json has been stashed this session, so a later corrupt read
    /// can never overwrite that evidence with a fresher corpse (mirrors SaveGame's meta rule).
    static bool _settingsEvidenceStashed;

    static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var d = JsonSerializer.Deserialize(File.ReadAllText(FilePath), DisplayJson.Default.Dto);
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
                VolMaster = Math.Clamp(d.VolMaster, 0f, 1f);
                VolSfx    = Math.Clamp(d.VolSfx, 0f, 1f);
                VolMusic  = Math.Clamp(d.VolMusic, 0f, 1f);
                VolUi     = Math.Clamp(d.VolUi, 0f, 1f);
                AnimSpeedIdx = Math.Clamp(d.AnimSpeedIdx, 0, AnimSpeedLevels.Length - 1);
                UiScaleIdx   = Math.Clamp(d.UiScaleIdx, 0, UiScaleLevels.Length - 1);
                ApplyUiScale();
                // W6: Decode never trusts the string — it resets to defaults, then re-validates
                // every override through the same conflict check the CONTROLS screen uses, so a
                // hand-edited or newer-build file can never install a double-bind (and can never
                // move CANCEL/MENU off Escape, which is a fixed row Decode refuses to touch).
                KeysDropped = Keymap.Decode(d.Keys);
            }
        }
        catch
        {
            // R2 (LOW-3): PRESERVE THE EVIDENCE. save.json and meta.json both stash an unreadable
            // file as <name>.bak before moving on; display.json alone was silently discarded and
            // then overwritten by the next Save() with defaults. That is the player's whole
            // settings profile (volumes, text scale, brightness, tips-seen) gone with no trace of
            // what went wrong. Copy, don't move: the file is still the live path, and a stale .bak
            // from an older session may be overwritten but one stashed earlier THIS session may
            // not. Recovery I/O must never throw — we are already inside a failure path.
            try
            {
                if (!_settingsEvidenceStashed && File.Exists(FilePath))
                {
                    File.Copy(FilePath, FilePath + ".bak", true);
                    _settingsEvidenceStashed = true;
                }
            }
            catch { }
        }
    }
}
