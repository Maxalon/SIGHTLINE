using System;
using Raylib_cs;

namespace Sightline;

public static class Program
{
    public static void Main()
    {
        // ---- Headless verification harness (env-gated; no effect in normal play) ----
        // SIGHTLINE_SHOT=<frame>  : skip intro, run to <frame>, write sightline_shot.png, exit.
        // SIGHTLINE_AUTOPLAY=1    : skip intro, let an autopilot play full matches to a result.
        // Used to smoke-test the whole loop under Xvfb + software GL. See CLAUDE.md.
        bool shot = int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_SHOT"), out int shotFrame);
        bool autoplay = Environment.GetEnvironmentVariable("SIGHTLINE_AUTOPLAY") == "1";

        ConfigFlags flags = ConfigFlags.Msaa4xHint;
        if (!autoplay) flags |= ConfigFlags.VSyncHint;
        Raylib.SetConfigFlags(flags);
        Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "SIGHTLINE — Tactical Squad Combat");
        Raylib.SetTargetFPS(autoplay ? 0 : 60);   // uncapped during the smoke test
        Audio.Init();

        var game = new Game();
        if (shot || autoplay) game.StartMission();
        if (autoplay) game.AutoPlay = true;
        int frame = 0;
        const int autoCap = 20000;

        while (!Raylib.WindowShouldClose())
        {
            float dt = (shot || autoplay) ? 1f / 60f : Raylib.GetFrameTime();
            game.Update(dt);

            Raylib.BeginDrawing();
            if (autoplay) Raylib.ClearBackground(Pal.Bg);  // skip heavy draw during smoke test
            else game.Draw();
            Raylib.EndDrawing();

            if (shot || autoplay) frame++;
            if (shot)
            {
                if (frame == shotFrame) Raylib.TakeScreenshot("sightline_shot.png");
                if (!autoplay && frame >= shotFrame + 2) break;
            }
            if (autoplay)
            {
                if (game.Phase == Phase.Win) { Console.WriteLine($"RESULT: WIN frame={frame}"); break; }
                if (game.Phase == Phase.Lose) { Console.WriteLine($"RESULT: LOSE frame={frame}"); break; }
                if (frame >= autoCap) { Console.WriteLine($"RESULT: TIMEOUT frame={frame}"); break; }
            }
        }

        Audio.Shutdown();
        Raylib.CloseWindow();
    }
}
