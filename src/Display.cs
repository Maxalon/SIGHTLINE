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
public static class Display
{
    public static bool Enabled;
    static RenderTexture2D _target;

    public static readonly (int w, int h)[] Sizes =
        { (1280, 800), (1600, 1000), (1920, 1200), (2560, 1600), (3200, 2000) };
    public static int SizeIdx;
    public static bool Fullscreen;

    public static string SizeLabel => Fullscreen ? "FULLSCREEN" : $"{Sizes[SizeIdx].w} x {Sizes[SizeIdx].h}";

    public static void Init(bool enabled)
    {
        Enabled = enabled;
        if (!enabled) return;
        _target = Raylib.LoadRenderTexture(Cfg.ScreenW, Cfg.ScreenH);
        Raylib.SetTextureFilter(_target.Texture, TextureFilter.Bilinear);
        Raylib.SetWindowState(ConfigFlags.ResizableWindow);   // let the user free-resize too
        Load();
        Apply();
    }

    public static void Shutdown()
    {
        if (Enabled) Raylib.UnloadRenderTexture(_target);
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

    /// Run the frame's drawing. Direct when at native size (crisp + MSAA), otherwise
    /// rendered to the virtual target and blitted scaled with letterbox bars.
    public static void RenderFrame(Action draw)
    {
        if (!Scaled)
        {
            Raylib.BeginDrawing();
            draw();
            Raylib.EndDrawing();
            return;
        }

        Raylib.BeginTextureMode(_target);
        draw();
        Raylib.EndTextureMode();

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
        float s = Scale(); var o = Offset();
        var src = new Rectangle(0, 0, Cfg.ScreenW, -Cfg.ScreenH);   // flip Y (render textures are upside-down)
        var dst = new Rectangle(o.X, o.Y, Cfg.ScreenW * s, Cfg.ScreenH * s);
        Raylib.DrawTexturePro(_target.Texture, src, dst, Vector2.Zero, 0f, Color.White);
        Raylib.EndDrawing();
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
    class Dto { public bool Fullscreen { get; set; } public int SizeIdx { get; set; } }
    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sightline");
    static string FilePath => Path.Combine(Dir, "display.json");

    static void Save()
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(new Dto { Fullscreen = Fullscreen, SizeIdx = SizeIdx })); }
        catch { }
    }

    static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var d = JsonSerializer.Deserialize<Dto>(File.ReadAllText(FilePath));
            if (d != null) { Fullscreen = d.Fullscreen; SizeIdx = Math.Clamp(d.SizeIdx, 0, Sizes.Length - 1); }
        }
        catch { }
    }
}
