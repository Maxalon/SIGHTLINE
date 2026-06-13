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

        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();
            game.Update(dt);

            Raylib.BeginDrawing();
            game.Draw();
            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
    }
}
