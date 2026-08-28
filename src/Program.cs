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

        // APEX W4: SIGHTLINE_BALANCE_ENDLESS=<N> : the SAME flywheel pointed at LAST STAND — N
        // endless stands (greedy+sloppy paired, heats cycled/pinned exactly like SIGHTLINE_BALANCE)
        // through the existing BeginEndless entry; the report adds wave-depth mean/median/p90.
        // Checked after SIGHTLINE_BALANCE, so a plain campaign batch is unchanged.
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE_ENDLESS"), out int endlessBatchN) && endlessBatchN > 0)
        {
            BalanceBatch(endlessBatchN, endless: true);
            return;
        }

        // W2: SIGHTLINE_PAIRTEST=1 : the CRN-pairing IDENTITY check. Two GREEDY legs on the same
        // slot seed must produce byte-identical outcomes (result + missions cleared + turns) —
        // this proves Util.Reseed pairing AND doubles as the no-cross-leg-state-bleed check the
        // whole paired-gap methodology rests on. Needs the window/update loop (full campaigns).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_PAIRTEST") == "1")
        {
            PairTest();
            return;
        }

        // SIGHTLINE_SAVETEST=1 : headless round-trip check for run persistence (item E). No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_SAVETEST") == "1")
        {
            Console.WriteLine(SaveGame.SelfTest());
            return;
        }

        // FUL-9: SIGHTLINE_EXPOSURETEST=1 : 200-seed content-exposure histogram — the per-route
        // objective invariant (routes ENUMERATED, not sampled), the no-repeat arena deck, and
        // all-8-objectives + all-35-arenas reachability across seeds. Pure derivation. No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_EXPOSURETEST") == "1")
        {
            Console.WriteLine(Game.ExposureSelfTest());
            return;
        }

        // SIGHTLINE_DRAFTTEST=1 : run-opening squad-draft pool/seat/harness-bypass check (Wave 3). No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_DRAFTTEST") == "1")
        {
            Console.WriteLine(Game.DraftSelfTest());
            return;
        }
        // SIGHTLINE_VETTEST=1 : cross-run VETERAN reserve — enshrine/recall carries progression, dedupe,
        // cap, draft seats <= MaxDraftVeterans. Pure Run/SaveGame logic (preserves the real meta.json). No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_VETTEST") == "1")
        {
            Console.WriteLine(Game.VetSelfTest());
            return;
        }
        // SIGHTLINE_OWTEST=1 : FOCUSED-overwatch braced-cone geometry (in-arc covered, behind/perp/outside blind).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_OWTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "owtest");   // Game ctor uses tile math
            Console.WriteLine(new Game().OwSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_BEACONTEST=1 : UNDERTOW W6 — Evac forward-beacon (walkable 3x3 union + fallback corner
        // + graceful non-floor/Escort refusal + all-on-beacon win + 1/mission) and the Escort VIP leash
        // (converges toward the nearest soldier, never off-board/occupied/onto-soldier, holds when adjacent).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_BEACONTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "beacontest");   // Game ctor / Fx use tile math
            Console.WriteLine(new Game().BeaconSelfTest());
            Raylib.CloseWindow();
            return;
        }
        if (Environment.GetEnvironmentVariable("SIGHTLINE_COMBATTEST") == "1")
        {
            Console.WriteLine(Combat.SelfTest());
            return;
        }
        // SIGHTLINE_CODEXTEST=1 : CODEX / FIELD MANUAL content-completeness (W6) — every documented enum
        // has a non-empty Name+Desc and the bestiary covers every archetype. Tiny window (Game/Unit ctors).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_CODEXTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "codextest");
            Console.WriteLine(new Game().CodexSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_EVENTTEST=1 : between-mission FIELD EVENT selection/placement/outcomes + save round-trip (W4). No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_EVENTTEST") == "1")
        {
            Console.WriteLine(EventCatalog.SelfTest());
            return;
        }
        // SIGHTLINE_HAZARDTEST=1 : environmental-hazard mechanics (barrel blocking / fire / pathing). No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_HAZARDTEST") == "1")
        {
            Console.WriteLine(Game.HazardSelfTest());
            return;
        }
        // SIGHTLINE_SIEGETEST=1 : SIEGE/BOMBARD charge->telegraph->detonate->interrupt + no-target fallback.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_SIEGETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "siegetest");   // Game uses tile math + Fx; tiny window
            Console.WriteLine(new Game().SiegeSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_AUDIOTEST=1 : device-free validation that every weapon/stinger/baseline SFX
        // recipe + both music beds build a non-empty, finite buffer (audio identity pass). No window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_AUDIOTEST") == "1")
        {
            Console.WriteLine(Audio.SelfTest());
            return;
        }
        // SIGHTLINE_AUDIOASSETS=1 : device-free report of which cues resolve to a dropped-in CC0
        // FILE vs the procedural synth (HORIZON W7). No window; 0 files = the current default.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_AUDIOASSETS") == "1")
        {
            Console.Write(Audio.AudioAssetsReport());
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
        // SIGHTLINE_HEATLADDERTEST=1 : APEX W1 — the heat>=7 / IRON VETERANS zero-roster seam: a lone-VIP
        // Escort/Rescue win under a no-reinforcements regime must still field a squad next mission.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_HEATLADDERTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "heatladdertest");   // SetupMission/EnterBarracks use tile math
            Console.WriteLine(new Game().HeatLadderSelfTest());
            Raylib.CloseWindow();
            return;
        }
        if (Environment.GetEnvironmentVariable("SIGHTLINE_WOUNDTEST") == "1")
        {
            Console.WriteLine(WoundTest());
            return;
        }
        // SIGHTLINE_DKTEST=1 : UNDERTOW W1 — a death is processed exactly once (KillUnit idempotent +
        // surplus corpse-reaction purge). Needs a tiny window for tile math.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_DKTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "dktest");
            Console.WriteLine(new Game().DoubleKillTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_DOWNTEST=1 : FUL-7 LAST LIGHT — the DOWN/bleed-out state machine (entry clean
        // of death bookkeeping, expiry = the full death flow once, stabilize/revive/recovery, no
        // second down + AoE finishes, AI ignores the downed, VIP instant, drag/extract carry).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_DOWNTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "downtest");
            Console.WriteLine(new Game().DownSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_RESCUETEST=1 : APEX W2 — the caged Rescue captive is actionless until freed
        // (freeing restores actions/movement), and the all-soldiers-dead-while-caged soft-lock
        // resolves (checkpoint redeploy at m3+ / CAPTIVE ABANDONED loss / skirmish loss).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_RESCUETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "rescuetest");
            Console.WriteLine(new Game().RescueSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_STAGGERTEST=1 : UNDERTOW W2 — BRACE interrupt (disrupting reaction staggers on hit).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_STAGGERTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "staggertest");
            Console.WriteLine(new Game().StaggerSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_PIKETEST=1 : FUL-8 — the SARISSA/PIKEMAN lane-holder (plant / stagger-halving pin /
        // cone blindness / break legs). Needs a tiny window for tile math.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_PIKETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "piketest");
            Console.WriteLine(new Game().PikemanSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_MORALETEST=1 : UNDERTOW W3 — enemy pod morale / rout.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_MORALETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "moraletest");
            Console.WriteLine(new Game().MoraleSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_PODTEST=1 : FUL-6 CRITICAL MASS — PodPlan sizes + spawn cohesion, linked
        // activation (nearest-only / confirm-unseen / no chain), the 3-pod waver->rout arc,
        // endless wave sub-pods (elite exempt), and the FIELD DRILLS drill grant.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_PODTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "podtest");
            Console.WriteLine(new Game().PodSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_FUL11PROBE=<N> : FUL-11 — finale-kit spawn distribution probe (per-kit retinue
        // slots, Wardens banner aura coverage as spawned, banner cap) across N flywheel seeds.
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_FUL11PROBE"), out int ful11N) && ful11N > 0)
        {
            Raylib.InitWindow(64, 64, "ful11probe");   // StartMission -> Unit.SyncPos uses tile->px math
            Console.WriteLine(Game.Ful11ProbeTest(ful11N));
            Raylib.CloseWindow();
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
        // SIGHTLINE_CDTEST=1 : renewable signature-ability cooldown (set on use, ticks at BeginTurn).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_CDTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "cdtest");   // Game uses tile math + Fx; tiny window
            Console.WriteLine(new Game().CdSelfTest());
            Raylib.CloseWindow();
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
        // SIGHTLINE_CONTRACTTEST=1 : RUN CONTRACTS (IronVeterans no-backfill/fast-rank, HighStakes no-heal, ordinals).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_CONTRACTTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "contracttest");
            Console.WriteLine(new Game().ContractSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_SCARTEST=1 : SCARS & VENDETTAS (trauma-earned scars; -mob/status-immunity/burn-shy/bloodied-crit/vendetta reads).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_SCARTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "scartest");
            Console.WriteLine(new Game().ScarSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_FIELDTEST=1 : FIELD CRAFT verbs (DRAG pulls an ally one tile / VAULT crosses a cover tile / gating).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_FIELDTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "fieldtest");   // Unit.SyncPos + Move/Shove anims use tile->px math
            Console.WriteLine(new Game().FieldSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_HORDETEST=1 : LAST STAND endless horde — wave count/scale escalation + alive-cap + meta BestWave round-trip (HORIZON W2).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_HORDETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "hordetest");   // SpawnEndless* + SetupMission use tile->px math
            Console.WriteLine(new Game().HordeSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_METATEST=1 : WAR ROOM cross-run meta — salvage/achievements/unlocks/legends/totals
        // round-trip + the unlock byte-stability invariant (applies under !NoPersist, inert under NoPersist).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_METATEST") == "1")
        {
            Raylib.InitWindow(64, 64, "metatest");   // StartMission -> Unit.SyncPos uses tile->px math
            Console.WriteLine(new Game().MetaSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_MODETEST=1 : SKIRMISH + SEEDED DAILY (W4) — daily seed determinism, single-mission
        // end sets Phase (Win/Lose, not Barracks), daily stamp/best round-trips. Tiny window (tile math).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_MODETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "modetest");
            Console.WriteLine(new Game().ModeSelfTest());
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
            // RESONANCE V1 — TWO ATLASES, and asset paths resolved next to the BINARY.
            //
            // (1) A single 64px atlas served everything from 11px to 92px. The 11-14px body text
            //     is most of the words in the game, and minifying 64px glyphs ~5x with bilinear
            //     filtering and no mip chain is exactly what turns small type into grey mush.
            //     Bake a second atlas at 20px for text <= Cfg.UiFontMax and keep the 64px atlas
            //     for the big sizes; Cfg.FontFor(size) routes every call site.
            // (2) Ship-blocker: the path was relative to the CURRENT WORKING DIRECTORY. A player
            //     launching the built binary from anywhere but the project root silently got
            //     Raylib's built-in bitmap font and every em-dash rendered as '?'. Cfg.AssetPath
            //     resolves against AppContext.BaseDirectory (with a cwd fallback for dev).
            // Mipmaps + trilinear on both atlases so any residual off-size draw filters cleanly.
            string notoPath = Cfg.AssetPath("assets/NotoMono-Regular.ttf");
            Font loaded = Raylib.LoadFontEx(notoPath, 64, codepoints, codepoints.Length);
            if (loaded.Texture.Id != 0)
            {
                Raylib.GenTextureMipmaps(ref loaded.Texture);
                Raylib.SetTextureFilter(loaded.Texture, TextureFilter.Trilinear);
                Cfg.Font = loaded;
                Console.WriteLine($"FONT: NotoMono-Regular 64px atlas loaded ({notoPath})");

                Font ui = Raylib.LoadFontEx(notoPath, 20, codepoints, codepoints.Length);
                if (ui.Texture.Id != 0)
                {
                    Raylib.GenTextureMipmaps(ref ui.Texture);
                    Raylib.SetTextureFilter(ui.Texture, TextureFilter.Trilinear);
                    Cfg.FontUi = ui;
                    Console.WriteLine("FONT: NotoMono-Regular 20px UI atlas loaded");
                }
            }
            else
            {
                Cfg.Font = Raylib.GetFontDefault();
                Console.WriteLine($"FONT: NotoMono-Regular NOT FOUND at {notoPath} — falling back to default");
            }

            // Display face (Chakra Petch Bold, OFL-1.1) — titles only; NotoMono keeps the data.
            string dispPath = Cfg.AssetPath("assets/ChakraPetch-Bold.ttf");
            Font disp = Raylib.LoadFontEx(dispPath, 96, codepoints, codepoints.Length);
            if (disp.Texture.Id != 0)
            {
                Raylib.GenTextureMipmaps(ref disp.Texture);
                Raylib.SetTextureFilter(disp.Texture, TextureFilter.Trilinear);
                Cfg.FontTitle = disp;
                Console.WriteLine("FONT: ChakraPetch-Bold display atlas loaded");
            }
            else
            {
                Console.WriteLine($"FONT: ChakraPetch-Bold NOT FOUND at {dispPath} — titles stay on NotoMono");
            }
        }

        // Display is normally OFF in the headless harness (byte-identical screenshots).
        // SIGHTLINE_POSTFX=1 forces it ON (+ the post-FX demo bloom) for verification.
        Display.Init(!(shot || autoplay) || postFxShot);
        Raylib.SetTargetFPS(autoplay ? 0 : 60);   // uncapped during the smoke test
        Audio.Init();

        var game = new Game();
        game.NoPersist = shot || autoplay;   // the harness never reads/writes the save file
        // SIGHTLINE_CONTRACT=ironveterans|highstakes|spearhead|mrc|lgd (FUL-10) : force a run contract
        // on the headless run (honored only under NoPersist, since the draft never runs there); None otherwise.
        game.ForcedContract = ContractDef.Parse(Environment.GetEnvironmentVariable("SIGHTLINE_CONTRACT"));
        // FUL-1: SIGHTLINE_PERK=<code> (e.g. RFX) : the bot takes this perk whenever a rank-up
        // offers it (ChoosePerk override; NoPersist-only, draw-count neutral). Null otherwise.
        game.ForcedPerk = PerkDef.Parse(Environment.GetEnvironmentVariable("SIGHTLINE_PERK"));
        // SIGHTLINE_INTRO=1 (shot only): stay on the intro with a save present, to
        // screenshot the CONTINUE-run button.
        if ((shot || autoplay) && int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_MAP"), out int forcedMap))
            Mission.ForcedLayout = forcedMap;
        bool introShot = shot && Environment.GetEnvironmentVariable("SIGHTLINE_INTRO") == "1";
        // FUL-2: the staged CONTINUE save silently CLOBBERED a real campaign save when the intro
        // shot ran on a machine with one. Stash the player's save.json bytes and restore-or-delete
        // after the shot loop (the METATEST preserve/restore pattern).
        string introStash = null; bool introStaged = false;
        if (introShot)
        {
            introStash = System.IO.File.Exists(SaveGame.SavePathPublic)
                ? System.IO.File.ReadAllText(SaveGame.SavePathPublic) : null;
            introStaged = true;
            var r = new Run(); r.Start(); r.Mission = 3; SaveGame.Save(r);
        }
        // PROGRAM HORIZON W2: LAST STAND harness entry. SIGHTLINE_ENDLESS=1 boots straight into the
        // endless horde mode (BeginEndless) instead of a campaign mission. AutoPlay/SmartPlay/NoPersist
        // must be set BEFORE BeginEndless (it reads NoPersist for the heat dial-in).
        bool endless = Environment.GetEnvironmentVariable("SIGHTLINE_ENDLESS") == "1";
        // PROGRAM HORIZON W4: SKIRMISH / SEEDED DAILY harness entry.
        //   SIGHTLINE_SKIRMISH=<objective>  -> BeginSkirmish(objective, heat) — a single custom fight.
        //   SIGHTLINE_DAILY=<yyyymmdd>      -> BeginDaily() — the deterministic seeded challenge for
        //     that stamp (ResolveDailyStamp reads the same env under NoPersist -> reproducible).
        string skirmishObj = Environment.GetEnvironmentVariable("SIGHTLINE_SKIRMISH");
        bool daily = int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_DAILY"), out int _dailyStamp) && _dailyStamp > 0;
        if (autoplay) game.AutoPlay = true;
        if (smartplay) game.SmartPlay = true;
        if ((shot || autoplay) && !introShot)
        {
            if (daily) game.BeginDaily();
            else if (!string.IsNullOrEmpty(skirmishObj))
            {
                int skHeat = int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_HEAT"), out int _sh) ? _sh : 0;
                game.BeginSkirmish(ParseObjective(skirmishObj), skHeat);
            }
            else if (endless) game.BeginEndless();
            else game.StartMission(startMission);
        }
        // SIGHTLINE_SKIRMISHSETUP=1 (shot only): screenshot the skirmish objective/heat picker screen.
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_SKIRMISHSETUP") == "1") game.DebugSkirmishSetup();
        // force an objective for verification (e.g. SIGHTLINE_OBJ=sabotage|rescue), shot or autoplay.
        // W2: the pin is now WHOLE-RUN — ForcedObjective re-applies inside SetupMission for every
        // subsequent mission (NoPersist-gated), while DebugForceObjective still rebuilds mission 1
        // immediately so a SHOT frame shows the pinned objective. Unknown/empty values stay unpinned
        // (strict TryParseObjective — a typo must never silently pin the wrong objective).
        string objEnvMain = Environment.GetEnvironmentVariable("SIGHTLINE_OBJ");
        Objective? objPin = TryParseObjective(objEnvMain);
        if (objPin.HasValue)
        {
            game.ForcedObjective = objPin;
            game.DebugForceObjective(objPin.Value);
        }
        else if (!string.IsNullOrEmpty(objEnvMain))
            Console.WriteLine($"HARNESS: unknown SIGHTLINE_OBJ '{objEnvMain}' — running unpinned");
        // screenshot-only hooks for verifying the camera + pause overlay
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_BEACON") == "1") game.DebugBeacon();
        if (shot && float.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_ZOOM"), out float z)) game.CamZoom = z;
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PAUSE") == "1") game.Paused = true;
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PERKSHOT") == "1") game.DebugBarracksPerk();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WAKE") == "1") game.DebugWakeAll();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_CONTENT") == "1") Mission.DebugContentShowcase(game);
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_ALERT") == "1") game.DebugAlertTiers();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PRESSURE") == "1") game.DebugPressure();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PODSHOT") == "1") game.DebugPodShot();   // FUL-6: pair with SIGHTLINE_MISSION=3
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WAVEBANNER") == "1") game.DebugWaveTelegraph();   // FUL-4: pair with SIGHTLINE_OBJ=defend
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PIKESHOT") == "1") game.DebugPikemanLane();       // FUL-8: planted PIKEMAN lane (pair with SIGHTLINE_CB=1 for the second pass)
        string downShot = Environment.GetEnvironmentVariable("SIGHTLINE_DOWNSHOT");
        if (shot && (downShot == "1" || downShot == "2")) game.DebugDownShot(downShot == "2");   // FUL-7: downed soldier + rescuer (=2 mid-rescue STABLE; pair with SIGHTLINE_CB=1 for the second pass)
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_CONCEAL") == "1") game.DebugConcealment();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_INTENT") == "1") game.DebugIntent();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_SIEGE") == "1") game.DebugSiege();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_CARDS") == "1") game.DebugDeployCards();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_CAMPAIGN") == "1") game.DebugCampaignMap();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_ITEM") == "1") game.DebugItem();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_SHOVE") == "1") game.DebugShove();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_MARK") == "1") game.DebugMark();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_VERB2") == "1") game.DebugVerbs();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_COVER") == "1") game.DebugCover();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_UNITFX") == "1") game.DebugUnitFx();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_ELEV") == "1") game.DebugElevation();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_SHOP") == "1") game.DebugShop();
        if (shot && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SIGHTLINE_PREP"))) game.DebugPrep();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_ARMORY") == "1") game.DebugArmory();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_BOON") == "1") game.DebugBoon();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_ENDLESSOFFER") == "1") game.DebugEndlessOffer();   // W7: pair with SIGHTLINE_ENDLESS=1

        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_EVENT") == "1") game.DebugEvent();   // + SIGHTLINE_EVENTID=<id> pins the staged event (FUL-10)
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_DRAFT") == "1") game.BeginDraft();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_VETDRAFT") == "1") game.DebugVetDraft();   // draft w/ recalled veterans
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_FOCUSOW") == "1") game.DebugFocusOw();      // focused-overwatch cone
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WARROOM") == "1") game.DebugWarRoom();   // W3 cross-run meta screen
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_CODEX") == "1") game.DebugCodex();       // W6 field-manual reference screen
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_HAZARD") == "1") game.DebugHazards();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_TAGEDIT") == "1") game.DebugTagEditor();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WOUND") == "1") game.DebugWound();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_TOOLTIP") == "1") game.DebugTooltip();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_BENCH") == "1") game.DebugBench();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_TRAITS") == "1") game.DebugTraits();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_STATUS") == "1") game.DebugStatus();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_KIA") == "1") game.DebugKia();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_SUMMARY") == "1") game.DebugSummary();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_SUMMARY") == "lose") game.DebugSummary(true);
        // SIGHTLINE_TUTORIAL=<n>: show tutorial step n-1 (FUL-12 numbering: =1 the NEW concealment/
        // AMBUSH lesson, =2 MOVE, =3 OVERWATCH, =4 the FIRE-rule copy, =5 the FIELD MANUAL wrap-up).
        // NoPersist is already set, so MarkTutorialSeen can never fire from a shot run.
        if (shot && int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_TUTORIAL"), out int _tut) && _tut > 0)
            game.ShowTutorialStep(_tut - 1);
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_CB") == "1") Pal.SetColorblind(true);
        if (shot && int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_BRIGHT"), out int _bi)) Display.BrightIdx = _bi;
        if (shot && int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_GAMMA"), out int _gi)) Display.GammaIdx = _gi;   // W9: gamma level 0-4 (pair with SIGHTLINE_POSTFX=1)
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
                // PROGRAM HORIZON W2: LAST STAND reports WAVES SURVIVED. A hard wave cap (30) plus the
                // Lose/frame-cap paths guarantee the endless autopilot always terminates (no TIMEOUT).
                if (game.Mode == GameMode.Endless)
                {
                    if (game.Phase == Phase.Lose || game.Wave >= 30 || frame >= autoCap)
                    { Console.WriteLine($"RESULT: ENDLESS waves={game.Wave} frame={frame}"); break; }
                    continue;   // still surviving — keep fighting
                }
                if (game.Phase == Phase.Win) { Console.WriteLine($"RESULT: WIN mission={game.RunState.Mission} frame={frame}"); break; }
                if (game.Phase == Phase.Lose) { Console.WriteLine($"RESULT: LOSE mission={game.RunState.Mission} frame={frame}"); break; }
                if (frame >= autoCap) { Console.WriteLine($"RESULT: TIMEOUT mission={game.RunState.Mission} frame={frame}"); break; }
            }
        }

        if (introStaged)   // FUL-2: hand the player back exactly the save they had (or none)
        {
            try
            {
                if (introStash != null) System.IO.File.WriteAllText(SaveGame.SavePathPublic, introStash);
                else SaveGame.Delete();
            }
            catch { /* best effort — never let restore kill the shutdown path */ }
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
    // balance report. Heat is cycled over the LADDER-SPANNING default set {0,2,4,6,8} across
    // the batch (or pinned via SIGHTLINE_BALANCE_HEAT) so the report shows a difficulty curve.
    // Fast + headless: one window, minimal per-frame draw (the autoplay path), uncapped FPS,
    // hard per-match frame cap so it can never hang.
    //
    // Each campaign is now played under BOTH a near-optimal "greedy" policy and a human-error
    // "sloppy" policy (the report shows the optimal-vs-sloppy GAP = difficulty slack), plus the
    // bot now exercises utility items + every class verb-ability so item/ability balance is
    // measured, and per-turn decision-richness + lead-swing texture is instrumented. Knobs:
    //   SIGHTLINE_BALANCE_HEAT=<h>  pin a heat rung (else cycle the default {0,2,4,6,8} set)
    //   SIGHTLINE_BALANCE_DUMB=1    use the dumb smoke-test autopilot (single policy, baseline)
    //   SIGHTLINE_BALANCE_SLOPPY=1  run ONLY the sloppy policy (else greedy+sloppy paired)
    //   SIGHTLINE_BALANCE_JSON=<p>  override the JSON artifact path (else <tmp>/balance.json)
    //
    // APEX W4 — `endless: true` (SIGHTLINE_BALANCE_ENDLESS=<N>) points the same machinery at
    // LAST STAND: each "campaign" slot becomes one endless stand via BeginEndless, and depth
    // (waves survived) is logged from game.Wave at EVERY exit — wipe, wave-cap, frame-cap,
    // abort — NEVER from RunState.Mission (endless keeps Mission==1, so the old campaign
    // fallback would log every capped deep stand as depth 0 and corrupt the p90).
    static void BalanceBatch(int runs, bool endless = false)
    {
        // Cumulative telemetry across the whole batch (NOT reset per match).
        Stats.Reset();
        Stats.Enabled = true;

        // Keep batch-wide static state deterministic across matches.
        Mission.ForcedLayout = -1;       // no forced arena
        Pal.SetColorblind(false);        // default palette (irrelevant headless, set defensively)

        // Optional pinned heat; otherwise cycle the ladder-spanning default set so the curve shows.
        // APEX W4: the default re-baseline now SPANS THE LADDER — {0,2,4,6,8} instead of i%5 —
        // so routine batches measure the mutator rungs (EXPOSED@6 / NO QUARTER@8) instead of only
        // heats 0-4 (6/8 were previously measured only in ad-hoc pinned runs).
        int[] heatCycle = { 0, 2, 4, 6, 8 };
        bool pinHeat = int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE_HEAT"), out int fixedHeat);
        // W2: SIGHTLINE_BALANCE_BASE=<n> offsets the slot index, so CHUNKED batches (the 10-min
        // shell ceiling forces N<=10 per invocation) can cover DISJOINT paired worlds — without
        // it, two combined N=10 chunks replay the SAME 10 seeds and halve the effective sample.
        int slotBase = int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE_BASE"), out int sb) ? sb : 0;
        // W2: whole-run objective pin — SIGHTLINE_OBJ under the batch pins EVERY mission of every
        // run (Game.ForcedObjective, honoured in SetupMission under NoPersist). Null = no pin.
        // Review fix: STRICT parse — a typo'd value must run UNPINNED with a loud warning, never
        // silently pin the sweep to Eliminate and enter the DEVLOG as a false baseline.
        string objEnv = Environment.GetEnvironmentVariable("SIGHTLINE_OBJ");
        Objective? forcedObj = TryParseObjective(objEnv);
        if (forcedObj == null && !string.IsNullOrEmpty(objEnv))
            Console.WriteLine($"BALANCE: unknown SIGHTLINE_OBJ '{objEnv}' — running unpinned");
        // SIGHTLINE_BALANCE_DUMB=1 runs the smoke-test autopilot instead of the competent AI,
        // so the same batch can produce a baseline to compare the smart AI (and balance changes) against.
        bool dumb = Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE_DUMB") == "1";
        // FUL-1: SIGHTLINE_PERK=<code> — the perk-probe leg of a paired A/B batch: the bot takes
        // this perk whenever a rank-up offers it (ChoosePerk override, draw-count neutral, so the
        // probe leg replays the baseline leg's exact worlds). Strict parse (SIGHTLINE_OBJ
        // precedent): a typo runs UNPROBED with a loud warning, never a silently wrong A/B.
        string perkEnv = Environment.GetEnvironmentVariable("SIGHTLINE_PERK");
        Perk? forcedPerk = PerkDef.Parse(perkEnv);
        if (forcedPerk == null && !string.IsNullOrEmpty(perkEnv))
            Console.WriteLine($"BALANCE: unknown SIGHTLINE_PERK '{perkEnv}' — running unprobed");

        // One window for the whole batch (the autoplay smoke path uses Display.RenderFrame).
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "SIGHTLINE — balance batch");
        Raylib.SetExitKey(KeyboardKey.Null);
        Cfg.Font = Raylib.GetFontDefault();   // no draw of game content in autoplay; default font is enough
        Display.Init(false);                  // headless render-frame path (no post-FX / no save)
        Raylib.SetTargetFPS(0);               // uncapped — run as fast as the sim allows

        const int frameCap = 20000;           // per-match safety cap; a hit cap counts as a loss
        // APEX W4 — explicit ENDLESS CAP POLICY (so the wave-depth p90 is never silently censored):
        //   * wave cap 30 — mirrors the SIGHTLINE_ENDLESS autoplay cap in Main. A stand that deep is
        //     a deliberate right-censor: it's logged as LossCause "wave-cap" and the report calls out
        //     every hit next to the depth distribution.
        //   * frame cap 60000 (3x the campaign cap) — a 30-wave stand plays FAR more turns than a
        //     6-mission campaign, so the campaign cap would censor exactly the deep stands the p90
        //     measures. Safety net only; logged as "frame-cap" and equally called out.
        const int endlessWaveCap = 30;
        const int endlessFrameCap = 60000;
        int wins = 0, losses = 0, capped = 0, waveCapped = 0;
        bool aborted = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // POLICY MIX: by default the competent batch runs BOTH the near-optimal "greedy" policy
        // and a human-error "sloppy" policy on the SAME heat schedule, so the report can show the
        // optimal-vs-sloppy GAP (difficulty slack). SIGHTLINE_BALANCE_SLOPPY=1 forces sloppy-only;
        // the dumb smoke-test baseline (SIGHTLINE_BALANCE_DUMB) never has a meaningful policy split.
        bool sloppyOnly = Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE_SLOPPY") == "1";
        bool[] sloppyModes = dumb ? new[] { false }
                            : sloppyOnly ? new[] { true }
                            : new[] { false, true };   // greedy then sloppy

        // Run a single campaign / endless stand (heat `heat`, policy `sloppy`, batch slot `slot`)
        // to a decision. Returns false if the window closed mid-match (abort the batch). Depth
        // for the defensive EndRun closes is MODE-AWARE: game.Wave for endless (Mission stays 1
        // there), missions-cleared for the campaign.
        bool RunOne(int heat, bool sloppy, int slot)
        {
            // StartMission/BeginEndless read SIGHTLINE_HEAT when NoPersist is set — dial it in first.
            Environment.SetEnvironmentVariable("SIGHTLINE_HEAT", heat.ToString());
            // ── W2 CRN PAIRING (the compass fix) ──────────────────────────────────────────
            // Reseed the SHARED RNG deterministically per slot BEFORE the Game is constructed,
            // so both policy legs of slot i replay the IDENTICAL world (map, spawns, combat
            // rolls) until the policies themselves diverge — the slot-level comparison cancels
            // the world-to-world variance that made unpaired gap readings swing ±27-38 pts.
            // Base 50000+slot, deliberately NOT 1000+slot: SeedSloppy(1000+slot) below seeds the
            // sloppy perturbation stream, and giving Util.Rng the SAME System.Random sequence
            // would correlate the slip pattern with the game's dice.
            Util.Reseed(50000 + slot);
            Stats.Slot = slot;       // stamp the pair id onto the RunRec (BeginRun reads it)
            var game = new Game { NoPersist = true, AutoPlay = true, SmartPlay = !dumb, SmartSloppy = !dumb && sloppy,
                                  ForcedObjective = forcedObj, ForcedPerk = forcedPerk };
            game.SeedSloppy(1000 + slot);   // reproducible per-run perturbation (no-op unless sloppy)
            // both entries fire Stats.BeginRun internally (tagging policy + mode)
            if (endless) game.BeginEndless(); else game.StartMission(1);

            int frame = 0;
            while (!Raylib.WindowShouldClose())
            {
                game.Update(1f / 60f);
                Display.RenderFrame(() => Raylib.ClearBackground(Pal.Bg));   // minimal draw
                frame++;
                if (endless)
                {
                    // A wipe routes through EndEndless -> Phase.Lose, which already closed the
                    // Stats run with depth = waves survived. That's the normal, uncensored exit.
                    if (game.Phase == Phase.Lose) { wins++; return true; }
                    if (game.Wave >= endlessWaveCap)
                    {
                        waveCapped++;
                        Stats.EndRun(false, game.Wave, "wave-cap");   // logged right-censor at 30
                        return true;
                    }
                    if (frame >= endlessFrameCap)
                    {
                        capped++;
                        Stats.EndRun(false, game.Wave, "frame-cap");  // safety net; logged
                        return true;
                    }
                    continue;
                }
                if (game.Phase == Phase.Win) { wins++; return true; }
                if (game.Phase == Phase.Lose) { losses++; return true; }
                if (frame >= frameCap)
                {
                    // Treat a frame-cap as a loss so the batch never hangs. EndRun is no-op if
                    // the run already finalised; defensively close the run record for the report.
                    capped++; losses++;
                    Stats.EndRun(false, game.RunState != null ? game.RunState.Mission - 1 : 0, "frame-cap");
                    return true;
                }
            }
            // window closed mid-match (Xvfb teardown / Ctrl-C): close the run record and stop.
            Stats.EndRun(false, endless ? game.Wave : (game.RunState != null ? game.RunState.Mission - 1 : 0), "aborted");
            return false;
        }

        int done = 0, totalMatches = runs * sloppyModes.Length;
        for (int i = 0; i < runs && !aborted && !Raylib.WindowShouldClose(); i++)
        {
            int heat = pinHeat ? Sightline.Heat.Clamp(fixedHeat) : heatCycle[i % heatCycle.Length];
            foreach (bool sloppy in sloppyModes)
            {
                if (aborted || Raylib.WindowShouldClose()) break;
                // W2: the slot index seeds BOTH streams inside RunOne (Util.Reseed pairs the
                // world across the policy legs; SeedSloppy makes the perturbation reproducible).
                if (!RunOne(heat, sloppy, slotBase + i)) { aborted = true; break; }
                done++;
                if (done % 5 == 0 || done == totalMatches)
                    Console.WriteLine(endless
                        ? $"stand {done}/{totalMatches}  (wiped:{wins} wave-cap:{waveCapped} frame-cap:{capped})  {sw.Elapsed.TotalSeconds:0.0}s"
                        : $"match {done}/{totalMatches}  (W:{wins} L:{losses} cap:{capped})  {sw.Elapsed.TotalSeconds:0.0}s");
            }
        }

        sw.Stop();
        // W2: return the shared RNG to a clock seed + clear the pair stamp — the batch must not
        // leave a deterministic stream behind for any later interactive/harness code in-process.
        Util.Reseed(0);
        Stats.Slot = -1;
        Console.WriteLine();
        Console.WriteLine(Stats.Report());
        Console.WriteLine(endless
            ? $"batch wall-time: {sw.Elapsed.TotalSeconds:0.0}s  ({totalMatches} stands across {runs} slots × {sloppyModes.Length} policy, wave-cap hits: {waveCapped}, frame-cap hits: {capped})"
            : $"batch wall-time: {sw.Elapsed.TotalSeconds:0.0}s  ({totalMatches} matches across {runs} campaigns × {sloppyModes.Length} policy, frame-cap hits: {capped})");

        // Optional machine-readable aggregate alongside the printed report. The path honours
        // SIGHTLINE_BALANCE_JSON if set, else lands in the system temp dir (a stable, always-
        // present location) — never a stale per-session scratchpad. WriteJson swallows IO errors.
        string jsonPath = Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE_JSON");
        if (string.IsNullOrEmpty(jsonPath))
            jsonPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "balance.json");
        Stats.WriteJson(jsonPath);
        Console.WriteLine($"aggregate JSON -> {jsonPath}");

        Display.Shutdown();
        Renderer.UnloadNoise();
        Raylib.CloseWindow();
    }

    // ── W2: the A/A CRN-pairing identity test (SIGHTLINE_PAIRTEST=1) ────────────────────
    // Replays the SAME slot seed twice under the GREEDY policy (one pair at heat 0, one at
    // heat 4) and demands IDENTICAL outcomes — result, missions cleared, mission count, and
    // total turns. If this fails, un-paired randomness is leaking into the legs (a clock-
    // seeded draw before Reseed, sloppy-RNG bleed into greedy paths, cross-run static state)
    // and no paired-gap number from the flywheel can be trusted. Deliberately NOT a screenshot
    // byte-diff: Util.Rng is clock-seeded at startup and the shot harness never reseeds it, so
    // two shot invocations differ even on unchanged code. Prints "PAIRTEST: PASS|FAIL".
    static void PairTest()
    {
        Stats.Reset();
        Stats.Enabled = true;
        Mission.ForcedLayout = -1;
        Pal.SetColorblind(false);

        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "SIGHTLINE — pair test");
        Raylib.SetExitKey(KeyboardKey.Null);
        Cfg.Font = Raylib.GetFontDefault();
        Display.Init(false);
        Raylib.SetTargetFPS(0);

        const int frameCap = 20000;
        // one greedy leg on (heat, slot): the EXACT seeding sequence BalanceBatch.RunOne uses.
        (string result, int cleared, int missions, int turns) Leg(int heat, int slot)
        {
            Environment.SetEnvironmentVariable("SIGHTLINE_HEAT", heat.ToString());
            Util.Reseed(50000 + slot);
            Stats.Slot = slot;
            var game = new Game { NoPersist = true, AutoPlay = true, SmartPlay = true };
            game.SeedSloppy(1000 + slot);   // greedy never draws from it; seeded for parity anyway
            game.StartMission(1);
            int frame = 0;
            while (!Raylib.WindowShouldClose())
            {
                game.Update(1f / 60f);
                Display.RenderFrame(() => Raylib.ClearBackground(Pal.Bg));
                if (game.Phase == Phase.Win || game.Phase == Phase.Lose || ++frame >= frameCap) break;
            }
            // natural exits already finalised the run record; the frame-cap close is defensive.
            Stats.EndRun(false, game.RunState != null ? game.RunState.Mission - 1 : 0, "frame-cap");
            var run = Stats.Runs[Stats.Runs.Count - 1];
            string result = game.Phase == Phase.Win ? "WIN" : game.Phase == Phase.Lose ? "LOSE" : "CAP";
            return (result, run.MissionsCleared, run.Missions.Count, run.Missions.Sum(m => m.Turns));
        }

        bool pass = true;
        foreach (var (heat, slot) in new[] { (0, 0), (4, 1) })
        {
            var a = Leg(heat, slot);
            var b = Leg(heat, slot);
            bool match = a == b;
            pass &= match;
            // FUL-5 review hardening: with no display WindowShouldClose() is true before frame
            // one, both legs return CAP 0/0/0, and identical-zeros "matched" — a vacuous PASS
            // on zero gameplay. Identity must be proven on real missions.
            pass &= a.missions > 0;
            Console.WriteLine($"PAIRTEST: h{heat} slot{slot}  legA {a.result} cleared={a.cleared} missions={a.missions} turns={a.turns}  " +
                              $"legB {b.result} cleared={b.cleared} missions={b.missions} turns={b.turns}  -> {(match ? "MATCH" : "MISMATCH")}");
        }
        Console.WriteLine(pass ? "PAIRTEST: PASS" : "PAIRTEST: FAIL");

        Util.Reseed(0);
        Stats.Slot = -1;
        Stats.Enabled = false;
        Display.Shutdown();
        Renderer.UnloadNoise();
        Raylib.CloseWindow();
    }

    // W2 (review fix): STRICT objective parse — null on empty/unknown instead of a silent
    // Eliminate default. Used by both SIGHTLINE_OBJ pin paths (balance batch + main dispatch),
    // where a typo silently pinning a whole sweep to the wrong objective would enter the
    // program record as a false baseline. ParseObjective below keeps its Eliminate default
    // for the SKIRMISH entry, where "some objective" is the right degradation for a smoke run.
    static Objective? TryParseObjective(string s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "eliminate" or "elim" => Objective.Eliminate,
        "evac" or "extract" => Objective.Evac,
        "hack" => Objective.Hack,
        "escort" => Objective.Escort,
        "sabotage" => Objective.Sabotage,
        "rescue" => Objective.Rescue,
        "defend" => Objective.Defend,
        "decapitate" or "decap" => Objective.Decapitate,
        _ => null,
    };

    // PROGRAM HORIZON W4: parse a SIGHTLINE_SKIRMISH=<objective> string into an Objective (case-
    // insensitive; a few aliases). Defaults to Eliminate on an empty/unknown value.
    static Objective ParseObjective(string s)
    {
        switch ((s ?? "").Trim().ToLowerInvariant())
        {
            case "eliminate": case "elim": return Objective.Eliminate;
            case "evac": case "extract": return Objective.Evac;
            case "hack": return Objective.Hack;
            case "escort": return Objective.Escort;
            case "sabotage": return Objective.Sabotage;
            case "rescue": return Objective.Rescue;
            case "defend": return Objective.Defend;
            case "decapitate": case "decap": return Objective.Decapitate;
            default: return Objective.Eliminate;
        }
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
