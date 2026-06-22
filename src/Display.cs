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
    static int _locResolution, _locBloom, _locChroma, _locGrade, _locTime;

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
uniform float uBloom;        // 0..1 bloom strength
uniform float uChroma;       // 0..1 chromatic aberration strength
uniform vec3  uGrade;        // per-biome colour multiply (default 1,1,1)
uniform float uTime;         // accumulated time (for very slow drift; optional)

// Cheap single-pass bloom via a 5-tap radial blur.
// Sample offset scale in texel units.
// radiusPx is in PIXELS; divide by uResolution per-axis so the kernel is square
// regardless of aspect ratio (review Mi3).
vec3 bloom(vec2 uv, float radiusPx) {
    vec3 col = texture(texture0, uv).rgb;
    float w = 1.0;
    vec2 r1 = vec2(radiusPx) / uResolution;   // aspect-correct UV offset
    vec2 r2 = r1 * 0.55;
    // 4 diagonal samples
    col += texture(texture0, uv + vec2( r1.x,  r1.y)).rgb; w += 1.0;
    col += texture(texture0, uv - vec2( r1.x,  r1.y)).rgb; w += 1.0;
    col += texture(texture0, uv + vec2(-r1.x,  r1.y)).rgb; w += 1.0;
    col += texture(texture0, uv - vec2(-r1.x,  r1.y)).rgb; w += 1.0;
    // 4 axis samples at half-radius
    col += texture(texture0, uv + vec2(r2.x, 0.0)).rgb; w += 1.0;
    col += texture(texture0, uv - vec2(r2.x, 0.0)).rgb; w += 1.0;
    col += texture(texture0, uv + vec2(0.0, r2.y)).rgb; w += 1.0;
    col += texture(texture0, uv - vec2(0.0, r2.y)).rgb; w += 1.0;
    return col / w;
}

void main() {
    vec2 uv = fragTexCoord;

    // --- chromatic aberration ---
    // Displace R and B channels by a small offset toward/away from centre.
    float caStr = uChroma * 0.006;
    vec2 dir = uv - 0.5;
    float caLen = length(dir);
    vec2 caOff = normalize(dir + vec2(0.001)) * caLen * caStr;
    float r = texture(texture0, uv + caOff).r;
    float g = texture(texture0, uv).g;
    float b = texture(texture0, uv - caOff).b;
    vec3 base = vec3(r, g, b);

    // --- bloom ---
    // Bloom radius in pixels (converted to aspect-correct UV inside bloom()).
    float bloomRadius = 3.0;
    // Bloom extracts only the bright part of the blurred sample (soft threshold > 0.55).
    vec3 blurred = bloom(uv, bloomRadius);
    vec3 bright  = max(blurred - 0.55, 0.0);   // soft threshold — only near-white areas glow
    bright *= 1.6;                              // boost so the extracted glow is visible
    vec3 withBloom = base + bright * uBloom * 0.55;  // subtle additive blend

    // --- biome colour grading (multiplicative) ---
    vec3 graded = withBloom * uGrade;

    // --- soft vignette ---
    // Feathered from centre, darkens toward corners. Strength ~0.35 at the corner.
    float vigRadius = length(uv - 0.5) * 1.44;  // 0..1 at the corner
    float vig = 1.0 - vigRadius * vigRadius * 0.40;
    vig = clamp(vig, 0.55, 1.0);               // floor 0.55: corners are never pitch-black
    graded *= vig;

    // Slight gamma correction to keep the output looking natural (not washed out).
    graded = pow(clamp(graded, 0.0, 1.0), vec3(0.95));

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

    public static string SizeLabel => Fullscreen ? "FULLSCREEN" : $"{Sizes[SizeIdx].w} x {Sizes[SizeIdx].h}";

    public static void CycleBrightness()
    {
        BrightIdx = (BrightIdx + 1) % BrightLevels.Length;
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
            DrawBrightness();
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
    }

    // Brightness post-pass: a translucent darken/lighten quad over the final frame.
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
        public bool Colorblind { get; set; }
        public bool TutorialSeen { get; set; }
        public bool PostFX { get; set; } = true;
        public bool AutoCam { get; set; }
    }
    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sightline");
    static string FilePath => Path.Combine(Dir, "display.json");

    static void Save()
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(new Dto { Fullscreen = Fullscreen, SizeIdx = SizeIdx, BrightIdx = BrightIdx, Colorblind = Pal.Colorblind, TutorialSeen = TutorialSeen, PostFX = PostFX, AutoCam = AutoCam })); }
        catch { }
    }

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
                Pal.SetColorblind(d.Colorblind);
                TutorialSeen = d.TutorialSeen;
                PostFX = d.PostFX;
                AutoCam = d.AutoCam;
            }
        }
        catch { }
    }
}
