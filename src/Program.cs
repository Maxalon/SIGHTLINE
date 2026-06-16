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

        // SIGHTLINE_SAVETEST=1 : headless round-trip check for run persistence (item E). No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_SAVETEST") == "1")
        {
            Console.WriteLine(SaveGame.SelfTest());
            return;
        }
        if (Environment.GetEnvironmentVariable("SIGHTLINE_COMBATTEST") == "1")
        {
            Console.WriteLine(Combat.SelfTest());
            return;
        }
        if (Environment.GetEnvironmentVariable("SIGHTLINE_DEATHTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "deathtest");   // a Game/Audio-free path still needs tile math; window is tiny
            Console.WriteLine(new Game().DeathConsequenceTest());
            Raylib.CloseWindow();
            return;
        }
        if (Environment.GetEnvironmentVariable("SIGHTLINE_WOUNDTEST") == "1")
        {
            Console.WriteLine(WoundTest());
            return;
        }
        // SIGHTLINE_TRAITTEST=1 : feats -> traits/nicknames + bonds round-trip (item 3.2). No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_TRAITTEST") == "1")
        {
            Console.WriteLine(Run.TraitSelfTest());
            return;
        }
        // SIGHTLINE_STATUSTEST=1 : status-effect tick/decay/read check (item 3.5).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_STATUSTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "statustest");   // Game uses tile math; window is tiny
            Console.WriteLine(new Game().StatusSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_MISSION=<n> : start the harness on mission n (verify Hack/Evac maps).
        int startMission = int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_MISSION"), out int sm) ? sm : 1;

        ConfigFlags flags = ConfigFlags.Msaa4xHint;
        if (!autoplay) flags |= ConfigFlags.VSyncHint;
        Raylib.SetConfigFlags(flags);
        Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "SIGHTLINE — Tactical Squad Combat");
        Raylib.SetExitKey(KeyboardKey.Null);       // ESC cancels aim/grenade & opens pause; never quits the app
        Display.Init(!(shot || autoplay));         // window scaling/fullscreen (off for the headless harness)
        Raylib.SetTargetFPS(autoplay ? 0 : 60);   // uncapped during the smoke test
        Audio.Init();

        var game = new Game();
        game.NoPersist = shot || autoplay;   // the harness never reads/writes the save file
        // SIGHTLINE_INTRO=1 (shot only): stay on the intro with a save present, to
        // screenshot the CONTINUE-run button.
        if ((shot || autoplay) && int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_MAP"), out int forcedMap))
            Mission.ForcedLayout = forcedMap;
        bool introShot = shot && Environment.GetEnvironmentVariable("SIGHTLINE_INTRO") == "1";
        if (introShot) { var r = new Run(); r.Start(); r.Mission = 3; SaveGame.Save(r); }
        if ((shot || autoplay) && !introShot) game.StartMission(startMission);
        if (autoplay) game.AutoPlay = true;
        // screenshot-only hooks for verifying the camera + pause overlay
        if (shot && float.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_ZOOM"), out float z)) game.CamZoom = z;
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PAUSE") == "1") game.Paused = true;
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PERKSHOT") == "1") game.DebugBarracksPerk();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WAKE") == "1") game.DebugWakeAll();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_CARDS") == "1") game.DebugDeployCards();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_SHOP") == "1") game.DebugShop();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_TAGEDIT") == "1") game.DebugTagEditor();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WOUND") == "1") game.DebugWound();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_TRAITS") == "1") game.DebugTraits();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_STATUS") == "1") game.DebugStatus();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_KIA") == "1") game.DebugKia();
        bool helpShot = shot && Environment.GetEnvironmentVariable("SIGHTLINE_HELP") == "1";  // hover the ability button
        int frame = 0;
        const int autoCap = 20000;

        while (!Raylib.WindowShouldClose())
        {
            float dt = (shot || autoplay) ? 1f / 60f : Raylib.GetFrameTime();
            Display.UpdateMouse();
            if (helpShot) Raylib.SetMousePosition(592, 740);   // park cursor on the ability button
            game.Update(dt);

            Display.RenderFrame(() =>
            {
                if (autoplay) Raylib.ClearBackground(Pal.Bg);  // skip heavy draw during smoke test
                else game.Draw();
            });

            if (shot || autoplay) frame++;
            if (shot)
            {
                if (frame == shotFrame) Raylib.TakeScreenshot("sightline_shot.png");
                if (!autoplay && frame >= shotFrame + 2) break;
            }
            if (autoplay)
            {
                if (game.Phase == Phase.Win) { Console.WriteLine($"RESULT: WIN mission={game.RunState.Mission} frame={frame}"); break; }
                if (game.Phase == Phase.Lose) { Console.WriteLine($"RESULT: LOSE mission={game.RunState.Mission} frame={frame}"); break; }
                if (frame >= autoCap) { Console.WriteLine($"RESULT: TIMEOUT mission={game.RunState.Mission} frame={frame}"); break; }
            }
        }

        Display.Shutdown();
        Audio.Shutdown();
        Raylib.CloseWindow();
    }

    // SIGHTLINE_WOUNDTEST: a survivor that ends a mission badly hurt carries a Wound
    // (−Aim/−Mobility), which decays over missions and is cleared by a medkit. Pure
    // Run logic — no window needed.
    static string WoundTest()
    {
        var fails = new System.Collections.Generic.List<string>();
        var r = new Run(); r.Start();
        var u = r.Squad[0];
        int baseBudget = u.MoveBudget;

        // (1) end a mission nearly downed -> heavy wound
        u.Hp = 1;
        r.DebriefSurvivors();
        if (u.Wound <= 0) fails.Add("noWoundAfterNearDeath");
        if (u.MoveBudget >= baseBudget) fails.Add("noMobilityPenalty");
        var g = new Grid();
        var atk = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle) };
        var def = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), X = 3, Y = 0, Hp = 6, MaxHp = 6 };
        atk.X = 0; atk.Y = 0; atk.Wound = 0; int healthyHit = Combat.ComputeOdds(g, atk, def).HitChance;
        atk.Wound = 1; int woundedHit = Combat.ComputeOdds(g, atk, def).HitChance;
        if (woundedHit >= healthyHit) fails.Add("noAimPenalty");

        // (2) wound decays over healthy missions
        int w1 = u.Wound;
        u.Hp = u.MaxHp;            // a clean mission
        r.DebriefSurvivors();
        if (u.Wound >= w1) fails.Add("woundDidNotDecay");

        // (3) heal it to full and run clean missions until it clears
        for (int i = 0; i < 4 && u.Wound > 0; i++) { u.Hp = u.MaxHp; r.DebriefSurvivors(); }
        if (u.Wound != 0) fails.Add("woundNeverCleared");

        return fails.Count == 0
            ? "WOUNDTEST: PASS (wound assigned, penalises aim+mobility, decays, clears)"
            : "WOUNDTEST: FAIL (" + string.Join(",", fails) + ")";
    }
}
