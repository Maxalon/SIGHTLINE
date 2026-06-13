using System;
using Raylib_cs;

namespace Breach;

public static class Program
{
    public static void Main()
    {
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint);
        Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "BREACH — Tactical Squad Combat");
        Raylib.SetTargetFPS(60);

        var game = new Game();

        // ---- Headless verification harness (env-gated; no effect in normal play) ----
        // BREACH_SHOT=<frame>  : skip intro, run to <frame>, write breach_shot.png, exit.
        // BREACH_AUTOPLAY=1    : skip intro, let an autopilot play full matches to a result.
        // Used to smoke-test the whole loop under Xvfb + software GL. See CLAUDE.md.
        bool shot = int.TryParse(Environment.GetEnvironmentVariable("BREACH_SHOT"), out int shotFrame);
        bool autoplay = Environment.GetEnvironmentVariable("BREACH_AUTOPLAY") == "1";
        if (shot || autoplay) game.StartMission();
        if (autoplay) game.AutoPlay = true;
        int frame = 0;
        const int autoCap = 20000;

        while (!Raylib.WindowShouldClose())
        {
            float dt = (shot || autoplay) ? 1f / 60f : Raylib.GetFrameTime();
            game.Update(dt);

            Raylib.BeginDrawing();
            game.Draw();
            Raylib.EndDrawing();

            if (shot || autoplay) frame++;
            if (shot)
            {
                if (frame == shotFrame) Raylib.TakeScreenshot("breach_shot.png");
                if (!autoplay && frame >= shotFrame + 2) break;
            }
            if (autoplay)
            {
                if (game.Phase == Phase.Win) { Console.WriteLine($"RESULT: WIN frame={frame}"); break; }
                if (game.Phase == Phase.Lose) { Console.WriteLine($"RESULT: LOSE frame={frame}"); break; }
                if (frame >= autoCap) { Console.WriteLine($"RESULT: TIMEOUT frame={frame}"); break; }
            }
        }

        Raylib.CloseWindow();
    }
}
