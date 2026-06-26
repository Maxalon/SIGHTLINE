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
        // SIGHTLINE_SMARTPLAY=1 : like AUTOPLAY, but routes the autopilot through the
        // competent SmartStep() so a single headless game is played to win (balance gauge).
        bool smartplay = Environment.GetEnvironmentVariable("SIGHTLINE_SMARTPLAY") == "1";
        bool autoplay = Environment.GetEnvironmentVariable("SIGHTLINE_AUTOPLAY") == "1" || smartplay;

        // SIGHTLINE_BALANCE=<N> : run N full headless campaigns with the competent AI, aggregate
        // balance telemetry (Stats), and print Stats.Report(). A measurement harness — takes over
        // completely when set; leaves AUTOPLAY/SHOT/the *TEST modes untouched when unset.
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE"), out int balanceN) && balanceN > 0)
        {
            BalanceBatch(balanceN);
            return;
        }

        // SIGHTLINE_SAVETEST=1 : headless round-trip check for run persistence (item E). No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_SAVETEST") == "1")
        {
            Console.WriteLine(SaveGame.SelfTest());
            return;
        }

        // SIGHTLINE_DRAFTTEST=1 : run-opening squad-draft pool/seat/harness-bypass check (Wave 3). No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_DRAFTTEST") == "1")
        {
            Console.WriteLine(Game.DraftSelfTest());
            return;
        }
        if (Environment.GetEnvironmentVariable("SIGHTLINE_COMBATTEST") == "1")
        {
            Console.WriteLine(Combat.SelfTest());
            return;
        }
        // SIGHTLINE_AUDIOTEST=1 : device-free validation that every weapon/stinger/baseline SFX
        // recipe + both music beds build a non-empty, finite buffer (audio identity pass). No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_AUDIOTEST") == "1")
        {
            Console.WriteLine(Audio.SelfTest());
            return;
        }
        // SIGHTLINE_AMBIENTTEST=1 : per-biome ambient field stays bounded/finite/on-board (Phase 5). No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_AMBIENTTEST") == "1")
        {
            Console.WriteLine(Fx.AmbientSelfTest() ? "AMBIENTTEST: PASS" : "AMBIENTTEST: FAIL");
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
        // SIGHTLINE_ITEMTEST=1 : utility-item mechanics (smoke LoS / barricade / loadouts) (item 3.4). No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_ITEMTEST") == "1")
        {
            Console.WriteLine(Game.ItemSelfTest());
            return;
        }
        // SIGHTLINE_CONCEALTEST=1 : concealment gating + ambush break check (item 4.4).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_CONCEALTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "concealtest");   // Game/Mission use tile math; tiny window
            Console.WriteLine(new Game().ConcealSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_BENCHTEST=1 : bench/short-handed lifecycle (S3-A + review fixes).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_BENCHTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "benchtest");
            Console.WriteLine(new Game().BenchSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_COVERTEST=1 : destructible-cover degrade chain (item 3.6). No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_COVERTEST") == "1")
        {
            Console.WriteLine(Game.CoverSelfTest());
            return;
        }
        // SIGHTLINE_AITEST=1 : squad-coordination check (focus fire / overwatch map / retreat).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_AITEST") == "1")
        {
            Raylib.InitWindow(64, 64, "aitest");   // Unit.SyncPos uses tile->px math; tiny window
            Console.WriteLine(new Game().AiSquadSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_SNAPTEST=1 : snap-shot cost/turn-end + flank-kill action-refund check.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_SNAPTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "snaptest");
            Console.WriteLine(new Game().SnapRefundSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_SHOVETEST=1 : SHOVE forced-movement verb (slide+break-overwatch / collision / gating).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_SHOVETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "shovetest");   // Unit.SyncPos + ShoveAnim use tile->px math
            Console.WriteLine(new Game().ShoveSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_MISSION=<n> : start the harness on mission n (verify Hack/Evac maps).
        int startMission = int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_MISSION"), out int sm) ? sm : 1;

        // SIGHTLINE_POSTFX=1 : force Display.Init(true) even in shot mode so the post-FX
        // shader is active; sets a strong demo bloom so the effect is clearly visible in
        // the screenshot. Plain SIGHTLINE_SHOT (without POSTFX) stays byte-identical.
        bool postFxShot = shot && Environment.GetEnvironmentVariable("SIGHTLINE_POSTFX") == "1";

        ConfigFlags flags = ConfigFlags.Msaa4xHint;
        if (!autoplay) flags |= ConfigFlags.VSyncHint;
        Raylib.SetConfigFlags(flags);
        Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "SIGHTLINE — Tactical Squad Combat");
        Raylib.SetExitKey(KeyboardKey.Null);       // ESC cancels aim/grenade & opens pause; never quits the app

        // Phase 5.3 — real bitmap font (NotoMono-Regular, OFL-1.1).
        // Bake ASCII 32-126 plus a selection of useful non-ASCII codepoints so the
        // font supports them once we start using them.
        {
            int[] codepoints = new int[]
            {
                // ASCII printable range 32..126
                32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,
                48,49,50,51,52,53,54,55,56,57,58,59,60,61,62,63,64,
                65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,
                81,82,83,84,85,86,87,88,89,90,
                91,92,93,94,95,96,
                97,98,99,100,101,102,103,104,105,106,107,108,109,110,
                111,112,113,114,115,116,117,118,119,120,121,122,
                123,124,125,126,
                // useful non-ASCII
                0x2013, // en-dash
                0x2014, // em-dash
                0x2018, // left single quote
                0x2019, // right single quote
                0x201C, // left double quote
                0x201D, // right double quote
                0x2022, // bullet
                0x2026, // ellipsis
                0x00D7, // multiply sign
                0x00B7, // middle dot
            };
            Font loaded = Raylib.LoadFontEx("assets/NotoMono-Regular.ttf", 64, codepoints, codepoints.Length);
            if (loaded.Texture.Id != 0)
            {
                Raylib.SetTextureFilter(loaded.Texture, TextureFilter.Bilinear);
                Cfg.Font = loaded;
                Console.WriteLine("FONT: NotoMono-Regular loaded (glyph atlas ok)");
            }
            else
            {
                Cfg.Font = Raylib.GetFontDefault();
                Console.WriteLine("FONT: NotoMono-Regular not found, falling back to default");
            }
        }

        // Display is normally OFF in the headless harness (byte-identical screenshots).
        // SIGHTLINE_POSTFX=1 forces it ON (+ the post-FX demo bloom) for verification.
        Display.Init(!(shot || autoplay) || postFxShot);
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
        if (smartplay) game.SmartPlay = true;
        // force an objective for verification (e.g. SIGHTLINE_OBJ=sabotage|rescue), shot or autoplay
        switch (Environment.GetEnvironmentVariable("SIGHTLINE_OBJ"))
        {
            case "sabotage": game.DebugForceObjective(Objective.Sabotage); break;
            case "rescue": game.DebugForceObjective(Objective.Rescue); break;
            case "defend": game.DebugForceObjective(Objective.Defend); break;
            case "decapitate": game.DebugForceObjective(Objective.Decapitate); break;
        }
        // screenshot-only hooks for verifying the camera + pause overlay
        if (shot && float.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_ZOOM"), out float z)) game.CamZoom = z;
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PAUSE") == "1") game.Paused = true;
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PERKSHOT") == "1") game.DebugBarracksPerk();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WAKE") == "1") game.DebugWakeAll();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_ALERT") == "1") game.DebugAlertTiers();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_CONCEAL") == "1") game.DebugConcealment();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_INTENT") == "1") game.DebugIntent();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_CARDS") == "1") game.DebugDeployCards();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_CAMPAIGN") == "1") game.DebugCampaignMap();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_ITEM") == "1") game.DebugItem();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_SHOVE") == "1") game.DebugShove();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_COVER") == "1") game.DebugCover();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_UNITFX") == "1") game.DebugUnitFx();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_ELEV") == "1") game.DebugElevation();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_SHOP") == "1") game.DebugShop();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_BOON") == "1") game.DebugBoon();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_DRAFT") == "1") game.BeginDraft();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_TAGEDIT") == "1") game.DebugTagEditor();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WOUND") == "1") game.DebugWound();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_BENCH") == "1") game.DebugBench();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_TRAITS") == "1") game.DebugTraits();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_STATUS") == "1") game.DebugStatus();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_KIA") == "1") game.DebugKia();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_TUTORIAL") == "1") game.TutStep = 0;
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_CB") == "1") Pal.SetColorblind(true);
        if (shot && int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_BRIGHT"), out int _bi)) Display.BrightIdx = _bi;
        // SIGHTLINE_POSTFX=1: inject a strong demo bloom + chroma so the shader effect
        // is clearly visible in the screenshot without needing a live combat event.
        if (postFxShot)
        {
            Display.BloomIntensity = 0.85f;
            Display.ChromaIntensity = 0.6f;
        }
        bool helpShot = shot && Environment.GetEnvironmentVariable("SIGHTLINE_HELP") == "1";  // hover the ability button
        int frame = 0;
        const int autoCap = 20000;

        while (!Raylib.WindowShouldClose())
        {
            float dt = (shot || autoplay) ? 1f / 60f : Raylib.GetFrameTime();
            Display.UpdateMouse();
            if (helpShot) Raylib.SetMousePosition(592, 740);   // park cursor on the ability button
            game.Update(dt);
            Audio.UpdateMusic(dt);

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
        Renderer.UnloadNoise();   // 5.4: free the procedural noise texture
        if (Cfg.Font.Texture.Id != 0 && Cfg.Font.Texture.Id != Raylib.GetFontDefault().Texture.Id)
            Raylib.UnloadFont(Cfg.Font);
        Raylib.CloseWindow();
    }

    // SIGHTLINE_BALANCE=<N>: run N full headless campaigns through the competent autopilot
    // (SmartPlay), accumulate Stats telemetry across all of them, and print the aggregate
    // balance report. Heat is cycled 0..4 across the batch (or pinned via SIGHTLINE_BALANCE_HEAT)
    // so the report shows a difficulty curve. Fast + headless: one window, minimal per-frame
    // draw (the autoplay path), uncapped FPS, hard per-match frame cap so it can never hang.
    static void BalanceBatch(int runs)
    {
        // Cumulative telemetry across the whole batch (NOT reset per match).
        Stats.Reset();
        Stats.Enabled = true;

        // Keep batch-wide static state deterministic across matches.
        Mission.ForcedLayout = -1;       // no forced arena
        Pal.SetColorblind(false);        // default palette (irrelevant headless, set defensively)

        // Optional pinned heat; otherwise cycle 0..4 so the curve shows.
        bool pinHeat = int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE_HEAT"), out int fixedHeat);
        // SIGHTLINE_BALANCE_DUMB=1 runs the smoke-test autopilot instead of the competent AI,
        // so the same batch can produce a baseline to compare the smart AI (and balance changes) against.
        bool dumb = Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE_DUMB") == "1";

        // One window for the whole batch (the autoplay smoke path uses Display.RenderFrame).
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "SIGHTLINE — balance batch");
        Raylib.SetExitKey(KeyboardKey.Null);
        Cfg.Font = Raylib.GetFontDefault();   // no draw of game content in autoplay; default font is enough
        Display.Init(false);                  // headless render-frame path (no post-FX / no save)
        Raylib.SetTargetFPS(0);               // uncapped — run as fast as the sim allows

        const int frameCap = 20000;           // per-match safety cap; a hit cap counts as a loss
        int wins = 0, losses = 0, capped = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < runs && !Raylib.WindowShouldClose(); i++)
        {
            int heat = pinHeat ? Sightline.Heat.Clamp(fixedHeat) : (i % 5);
            // StartMission reads SIGHTLINE_HEAT when NoPersist is set — dial it in before starting.
            Environment.SetEnvironmentVariable("SIGHTLINE_HEAT", heat.ToString());

            var game = new Game { NoPersist = true, AutoPlay = true, SmartPlay = !dumb };
            game.StartMission(1);   // fires Stats.BeginRun internally

            int frame = 0;
            bool decided = false;
            while (!Raylib.WindowShouldClose())
            {
                game.Update(1f / 60f);
                Display.RenderFrame(() => Raylib.ClearBackground(Pal.Bg));   // minimal draw
                frame++;
                if (game.Phase == Phase.Win) { wins++; decided = true; break; }
                if (game.Phase == Phase.Lose) { losses++; decided = true; break; }
                if (frame >= frameCap)
                {
                    // Treat a frame-cap as a loss so the batch never hangs. EndRun is no-op if
                    // the run already finalised; defensively close the run record for the report.
                    capped++; losses++;
                    Stats.EndRun(false, game.RunState != null ? game.RunState.Mission - 1 : 0, "frame-cap");
                    break;
                }
            }
            if (!decided && frame < frameCap)
            {
                // window closed mid-match (Xvfb teardown / Ctrl-C): close the run record and stop.
                Stats.EndRun(false, game.RunState != null ? game.RunState.Mission - 1 : 0, "aborted");
                break;
            }

            if ((i + 1) % 5 == 0 || i + 1 == runs)
                Console.WriteLine($"run {i + 1}/{runs}  (W:{wins} L:{losses} cap:{capped})  {sw.Elapsed.TotalSeconds:0.0}s");
        }

        sw.Stop();
        Console.WriteLine();
        Console.WriteLine(Stats.Report());
        Console.WriteLine($"batch wall-time: {sw.Elapsed.TotalSeconds:0.0}s  ({runs} runs, frame-cap hits: {capped})");

        // Optional machine-readable aggregate alongside the printed report.
        string jsonPath = "/tmp/claude-0/-home-user-temporary-name/809199e3-983c-51d2-b8f8-28bff90d918a/scratchpad/balance.json";
        Stats.WriteJson(jsonPath);
        Console.WriteLine($"aggregate JSON -> {jsonPath}");

        Display.Shutdown();
        Renderer.UnloadNoise();
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
