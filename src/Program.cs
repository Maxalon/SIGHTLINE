using System;
using Raylib_cs;

namespace Sightline;

public static class Program
{
    public static void Main()
    {
        // TRUE BAND (review fix): SIGHTLINE_CHOICEBAND selects the DECISION-DENSITY INSTRUMENT,
        // and a typo used to select the new rule silently — a batch a shell history calls "mult"
        // but that was measured on "add" is exactly the corruption this wave exists to prevent.
        // Refuse to start on anything but "mult", "add" or unset. This sits at the very top so it
        // covers every mode, not just the balance batch.
        if (!Game.ChoiceBandValid)
        {
            Console.Error.WriteLine($"SIGHTLINE_CHOICEBAND: unknown value '{Game.ChoiceBandEnv}' — "
                + "expected 'mult' (the pre-TRUE-BAND multiplicative instrument), 'add' (the "
                + "current additive one) or unset. Refusing to run rather than guess which "
                + "instrument you meant.");
            Environment.Exit(2);
            return;
        }
        // ---- Headless verification harness (env-gated; no effect in normal play) ----
        // SIGHTLINE_SHOT=<frame>  : skip intro, run to <frame>, write sightline_shot.png, exit.
        // SIGHTLINE_AUTOPLAY=1    : skip intro, let an autopilot play full matches to a result.
        // Used to smoke-test the whole loop under Xvfb + software GL. See CLAUDE.md.
        bool shot = int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_SHOT"), out int shotFrame);
        // SIGHTLINE_SEED=<n> : pin Util.Rng so two harness runs stage the SAME arena/roster. The
        // renderer still reads the wall clock in ~50 places, so frames are not byte-identical — but
        // this makes a before/after screenshot pair show the same BOARD, which is what a visual
        // A/B actually needs. 0 / unset = today's clock seed (every existing path unchanged).
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_SEED"), out int seedPin) && seedPin != 0)
            Util.Reseed(seedPin);
        // SIGHTLINE_SMARTPLAY=1 : like AUTOPLAY, but routes the autopilot through the
        // competent SmartStep() so a single headless game is played to win (balance gauge).
        // W4 THE SECOND AXIS — deployment-geometry measurement pins (no effect unset).
        //   SIGHTLINE_DEPLOY=frontal|pincer|crossfire|envelop|<0-3> : pin ONE opening shape.
        //   SIGHTLINE_DEPLOYMIX=a,b,c,d                             : set the shipped weight mix.
        // Both are pure statics on Mission read at Build time; the shape itself is derived from
        // (MapSeed, mission) with zero RNG draws, so CRN pairing survives either pin.
        {
            string dep = Environment.GetEnvironmentVariable("SIGHTLINE_DEPLOY");
            if (!string.IsNullOrEmpty(dep))
                Mission.ForcedDeploy = dep.Trim().ToLowerInvariant() switch
                {
                    "frontal" => Mission.DeployFrontal,
                    "pincer" => Mission.DeployPincer,
                    "crossfire" => Mission.DeployCrossfire,
                    "envelop" => Mission.DeployEnvelop,
                    _ => int.TryParse(dep, out int dv) && dv >= 0 ? dv : -1,
                };
            string mix = Environment.GetEnvironmentVariable("SIGHTLINE_DEPLOYMIX");
            if (!string.IsNullOrEmpty(mix))
            {
                var parts = mix.Split(',');
                var w = new int[Mission.DeployShapes];
                for (int i = 0; i < w.Length && i < parts.Length; i++) int.TryParse(parts[i].Trim(), out w[i]);
                Mission.DeployMix = w;
            }
        }

        // W4 — SIGHTLINE_PODMASS=<n>: enemy formation mass (3 = the FUL-6 pods-of-3 plan).
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_PODMASS"), out int pm) && pm >= 2)
            Mission.PodMass = pm;
        // W4 — SIGHTLINE_PODUNIFORM=1: a pod fields one kind of body (comparable targets).
        string uni = Environment.GetEnvironmentVariable("SIGHTLINE_PODUNIFORM");
        if (uni == "1") Mission.PodUniform = true; else if (uni == "0") Mission.PodUniform = false;
        // W4 — SIGHTLINE_RIMWAVES=1: under an ENVELOP opening, rotate the rim reinforcement
        // waves arrive from (a surrounded hold that keeps being surrounded).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_RIMWAVES") == "1") Mission.EnvelopRimWaves = true;
        // W4 — SIGHTLINE_ESCORTFIX=0 restores the pre-fix SmartEscort lone-VIP test (a DOWNED
        // soldier counted as still standing) so the instrument fix has a paired measurement.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_ESCORTFIX") == "0") Game.EscortDownedFix = false;
        // X2 TRUE NORTH II — the X1 durability pair, pinnable per measured round.
        //   SIGHTLINE_TOUGH=<n> : Mission.HostileToughness (flat HP surcharge; X1 shipped 3)
        //   SIGHTLINE_TRIM=<n>  : Mission.HostileDamageTrim (flat points off both ends; shipped 1)
        // Unset = the shipped defaults, so an unpinned batch is unchanged.
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_TOUGH"), out int xtough) && xtough >= 0)
            Mission.HostileToughness = xtough;
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_TRIM"), out int xtrim) && xtrim >= 0)
            Mission.HostileDamageTrim = xtrim;
        //   SIGHTLINE_AIMTRIM=<n>    : Mission.HostileAimTrim (flat points off every hostile's aim)
        //   SIGHTLINE_ENEMYBASE=<n>  : Mission.EnemyBaseCount (the `count = base + mission` constant)
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_AIMTRIM"), out int xaim) && xaim >= 0)
            Mission.HostileAimTrim = xaim;
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_ENEMYBASE"), out int xbase) && xbase >= 0)
            Mission.EnemyBaseCount = xbase;
        //   SIGHTLINE_OPENERTRIM=<n> : Mission.OpenerTrim (bodies off the m1 / half off m2 force)
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_OPENERTRIM"), out int xopen) && xopen >= 0)
            Mission.OpenerTrim = xopen;

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

        // W1: SIGHTLINE_RNGFRAMETEST=1 : gameplay must be a function of the SEED — not of the
        // frame rate, the animation-speed setting or the screen-shake comfort toggle. Four pinned
        // seeds x {1x, 8x} x {shake on, off}; all four legs of a seed must agree. See RngFrameTest.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_RNGFRAMETEST") == "1")
        {
            RngFrameTest();
            return;
        }

        // Q1: SIGHTLINE_STACKTEST=1 : the NO-TWO-IN-ONE-PLACE invariant. Drives real missions across
        // all 8 objectives at heat 0/2 with two detectors running at once — (a) a hook on every
        // MoveStepAnim ACTIVATION asserting the destination tile is empty-or-self, and (b) a
        // per-frame sweep for two living units sharing a tile (episode-counted, with the longest
        // episode's duration). PASS requires BOTH at zero over a non-vacuous sample.
        // SIGHTLINE_STACKTEST=2 widens the same sweep to FULL campaigns under BOTH the dumb
        // smoke bot and the competent one (the QA-scale measurement; several minutes).
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_STACKTEST"), out int stackMode) && stackMode > 0)
        {
            StackTest(stackMode >= 2);
            return;
        }

        // R2 FIX 1: SIGHTLINE_GEOMTEST=1 : the NOBODY-IS-WALLED-OUT invariant. Builds thousands of
        // fresh boards across all 4 deployment shapes x 8 objectives x every mission x 2 heats and
        // asserts every soldier can reach the squad and has a legal turn-1 move, and every hostile /
        // objective tile stays reachable. STACKTEST could not have caught this: it is a fixed
        // 16-board sample that never varies the deployment shape. SIGHTLINE_GEOMTEST=<N> widens the
        // seed count. Needs a window only because Unit.SyncPos does tile->px math.
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_GEOMTEST"), out int geomN) && geomN > 0)
        {
            Raylib.InitWindow(64, 64, "geomtest");
            Console.WriteLine(Game.GeomSelfTest(geomN == 1 ? 8 : geomN));
            Raylib.CloseWindow();
            return;
        }

        // W4 "THE BOARD BECOMES A PLACE": SIGHTLINE_BOARDTEST=1 — the board's RENDERED value
        // hierarchy and the cover merge, measured on real pixels rather than on game state. This
        // is the only self-test in the project that draws a frame and reads it back, and it has to
        // be: the defect it guards (a dormant pod out-shining the selected soldier) is invisible to
        // every state assertion in the suite. Needs the FULL-SIZE window + the baked fonts +
        // Display, because it photographs the shipped Game.Draw path.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_BOARDTEST") == "1")
        {
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
            Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "boardtest");
            Raylib.SetExitKey(KeyboardKey.Null);
            // W4 REVIEW FIX: SIGHTLINE_CB is read ~600 lines below, inside the SHOT path, so it
            // could never reach a self-test that returns from up here — the wave's claim that the
            // value rungs survive the colourblind palette had zero coverage. BOARDTEST now asserts
            // the ladder in BOTH palettes on its own and restores whichever it started in, and
            // this line makes SIGHTLINE_CB=1 pick which one that is.
            if (Environment.GetEnvironmentVariable("SIGHTLINE_CB") == "1") Pal.SetColorblind(true);
            LoadGameFonts();
            Display.Init(false);          // post-FX OFF: the rungs are authored values, not bloom
            Raylib.SetTargetFPS(0);
            Audio.Init();
            var bt = new Game { NoPersist = true };
            Console.WriteLine(bt.BoardSelfTest());
            Display.Shutdown();
            Audio.Shutdown();
            Renderer.UnloadNoise();
            Raylib.CloseWindow();
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
        // SIGHTLINE_TUTTEST=1 : RESONANCE T1 onboarding — the training-op arena/script, every lesson
        // trigger predicate (reachable + fires exactly once), the verb-staging cap + SHOW ALL escape,
        // the field-tip table's bit/prio integrity, and the Display seen-flag round-trip. Tiny window
        // (Game/Unit ctors + tile math). Preserves and restores the real display.json.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_TUTTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "tuttest");
            Console.WriteLine(new Game().TutorialSelfTest());
            Raylib.CloseWindow();
            return;
        }
        if (Environment.GetEnvironmentVariable("SIGHTLINE_COMBATTEST") == "1")
        {
            Console.WriteLine(Combat.SelfTest());
            return;
        }
        // SIGHTLINE_THREATTEST=1 : RESONANCE T2 — the incoming-fire FORECAST pinned against
        // Combat.ComputeOdds on a synthetic board (gun count, best hit%, expected damage, cover /
        // flank angle, out-of-range / dormant / dry / no-LoS exclusion, overwatch + focused cones,
        // unreachable-tile skip, caged captive, non-mutation of the mover, signature cache) plus a
        // measured worst-case rebuild cost. Tiny window (Game/Unit ctors).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_THREATTEST") == "1")
        {
            Raylib.SetTraceLogLevel(TraceLogLevel.Error);
            Raylib.InitWindow(64, 64, "sightline-threattest");
            Console.WriteLine(new Game().ThreatSelfTest());
            Raylib.CloseWindow();
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
        // SIGHTLINE_VOICETEST=1 : RESONANCE C1 (VOICE) — the game's WORDS as a contract. Asserts
        // (a) generating every region / briefing / dossier / bark / epilogue consumes ZERO draws
        // from the shared Util.Rng — the CRN-pairing guarantee every measurement in this project
        // rests on — with a sensitivity probe so the check cannot pass vacuously; (b) every
        // template slot resolves non-empty and no beat can produce a nonsensical combination
        // (a bondless soldier can never draw a bond line); (c) every bark trigger is reachable
        // through TryBark and all four rate-limit gates actually bite; (d) no generated line
        // overflows the chrome that draws it. Needs a window + the real atlases: the width
        // assertions measure actual glyphs through Cfg.Measure.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_VOICETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "voicetest");
            LoadGameFonts();
            Console.WriteLine(Voice.SelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_VOICEDUMP=1 : RESONANCE C1 — print every text type Voice generates (regions,
        // briefings, faction dossiers, all bark variants, four epilogue shapes) so the COPY can be
        // read and judged as prose without walking six missions. No window, changes nothing.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_VOICEDUMP") == "1")
        {
            Console.Write(Voice.SampleReport());
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
        // RESONANCE A1: SIGHTLINE_AUDIODUMP=1 : render every SFX cue + both music beds to
        // audio_dump/*.wav and print the full measurement table (level / spectrum / tails /
        // loop seams / concurrent-stack headroom). Device-free, no window. Feed the WAVs to
        // scripts/audio-report.py for a spectrogram contact sheet.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_AUDIODUMP") == "1")
        {
            Console.Write(Audio.DumpReport(Environment.GetEnvironmentVariable("SIGHTLINE_AUDIODIR") ?? "audio_dump"));
            return;
        }
        // RESONANCE A1: SIGHTLINE_AUDIOGATE=1 : the committed audio budget as a PASS/FAIL
        // contract (peak ceiling, per-role RMS bands, spread, crit/hit separation, DC,
        // clipping incl. concurrent stacks, real tails, music brightness, loop seams).
        // Device-free, no window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_AUDIOGATE") == "1")
        {
            Console.WriteLine(Audio.GateReport());
            return;
        }
        // RESONANCE A3: SIGHTLINE_AUDITIONTEST=1 : the AUDIO CHECK screen's contract — every cue
        // in SfxCueIds is listed exactly once and is a registered recipe, every cue has a role
        // caption, no label overflows its column at 120% text scale, every gate stack resolves to
        // known cues, and the numbers the rows print are finite and inside the budget. Needs a tiny
        // window + the real atlases (the width assertions measure actual glyphs).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_AUDITIONTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "auditiontest");
            LoadGameFonts();
            Console.WriteLine(Game.AuditionSelfTest());
            Raylib.CloseWindow();
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
        // SIGHTLINE_OPENERTEST=1 : RESONANCE X2 — the COLD-OPENER GRACE (Mission.OpenerTrim): the
        // base force's m1 / m2 ramp, its floor, its shipped default and its determinism.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_OPENERTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "openertest");   // SetupMission uses tile math
            Console.WriteLine(new Game().OpenerSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_ONRAMPTEST=1 : RESONANCE W5 — the RECRUIT rung (a real difficulty below standard)
        // and the comfort settings (anim speed / UI text scale) incl. the harness-pinning guard.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_ONRAMPTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "onramptest");   // SetupMission / Cfg.Measure need a GL context
            // The text-scale assertions MEASURE, so Cfg needs a real atlas; the bundled TTFs are
            // irrelevant to what is being asserted (a ratio), so the built-in font is enough.
            Cfg.Font = Cfg.FontUi = Cfg.FontTitle = Raylib.GetFontDefault();
            Console.WriteLine(new Game().OnRampSelfTest());
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
        // SIGHTLINE_ROUTETEST=1 : W1 — measure the AUTOPILOT'S ROUTE through the campaign DAG (the
        // sampling frame every published balance number was drawn through). Window-free.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_ROUTETEST") == "1")
        {
            Console.WriteLine(Game.RouteSelfTest());
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
        // SIGHTLINE_BANDTEST=1 : TRUE BAND — the decision-density INSTRUMENT as a contract.
        // Pins the choice-band constants, proves SIGHTLINE_CHOICEBAND=mult still reproduces the
        // pre-wave counts exactly against a literal transcription over 120 constructed boards,
        // and proves the counter mutates no state and draws zero Util.Rng (with a sensitivity
        // probe on the purity detector itself). Window only for Unit.SyncPos's tile->px math.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_BANDTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "bandtest");
            Console.WriteLine(new Game().BandSelfTest());
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
        LoadGameFonts();

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
        // W5 ON-RAMP (shot + the hand-run autoplay smoke test): SIGHTLINE_ANIMSPEED=<x> names the
        // playback multiplier and SIGHTLINE_LONGMOVE=1 stages a multi-tile walk to film. Autoplay is
        // included so the smoke test can be re-run AT the fastest setting (the pace change alters
        // the frame budget a match takes, and that is exactly what needs proving safe). Both are
        // inert when unset — and BalanceBatch has its own Main branch that never reaches here — so
        // the flywheel and every default autoplay/screenshot run are unchanged.
        if ((shot || autoplay) && float.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_ANIMSPEED"),
                                   System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float aspd) && aspd > 0f)
            game.AnimSpeedOverride = aspd;
        // W5 (shot only): SIGHTLINE_UISCALE=<idx into Display.UiScaleLevels> photographs the UI at a
        // text size other than 100%. Set on Cfg directly — Display never Loads headless — and inert
        // when unset, so every other screenshot keeps measuring the authored layout.
        if (shot && int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_UISCALE"), out int uiIdx))
        {
            Display.UiScaleIdx = Math.Clamp(uiIdx, 0, Display.UiScaleLevels.Length - 1);
            Display.ApplyUiScale();
        }
        bool longMove = shot && Environment.GetEnvironmentVariable("SIGHTLINE_LONGMOVE") == "1";
        if (longMove) Console.WriteLine($"LONGMOVE: staged {game.DebugLongMove()} steps at {game.AnimSpeed:0.##}x");
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PERKSHOT") == "1") game.DebugBarracksPerk();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WAKE") == "1") game.DebugWakeAll();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_CONTENT") == "1") Mission.DebugContentShowcase(game);
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_ALERT") == "1") game.DebugAlertTiers();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_MARKERS") == "1") game.DebugMarkers();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PRESSURE") == "1") game.DebugPressure();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PODSHOT") == "1") game.DebugPodShot();   // FUL-6: pair with SIGHTLINE_MISSION=3
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WAVEBANNER") == "1") game.DebugWaveTelegraph();   // FUL-4: pair with SIGHTLINE_OBJ=defend
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PIKESHOT") == "1") game.DebugPikemanLane();       // FUL-8: planted PIKEMAN lane (pair with SIGHTLINE_CB=1 for the second pass)
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_THREATSHOT") == "1") game.DebugThreatShot();      // RESONANCE T2: incoming-fire pips + tinted path + card (pair with SIGHTLINE_CB=1)
        if (shot && int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_THREATPREF"), out int _tp)) game.ThreatPref = Util.Clamp(_tp, Game.ThreatOff, Game.ThreatFull);   // 0 off / 1 simple (pre-T2 read) / 2 full
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
        // RESONANCE T1 harness entries:
        //   SIGHTLINE_TRAINING=1        -> boot straight into the TRAINING OP (scripted drill)
        //   SIGHTLINE_TRAINLESSON=<n>   -> park it on lesson n (1-based) for a staged-bar screenshot
        //   SIGHTLINE_SHOWALL=1         -> flip the SHOW ALL escape on (staging bypass, before/after shot)
        // All shot/autoplay-only and NoPersist, so nothing here can write a profile.
        if ((shot || autoplay) && Environment.GetEnvironmentVariable("SIGHTLINE_TRAINING") == "1")
        {
            game.BeginTraining();
            if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_TRAINLESSON"), out int _tl) && _tl > 0)
                game.ShowTrainingLesson(_tl - 1);
            if (Environment.GetEnvironmentVariable("SIGHTLINE_SHOWALL") == "1") game.ToggleShowAllVerbs();
        }
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_DRAFT") == "1") game.BeginDraft();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_VETDRAFT") == "1") game.DebugVetDraft();   // draft w/ recalled veterans
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_FOCUSOW") == "1") game.DebugFocusOw();      // focused-overwatch cone
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WARROOM") == "1") game.DebugWarRoom();   // W3 cross-run meta screen
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_CODEX") == "1") game.DebugCodex();       // W6 field-manual reference screen
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_AUDITION") == "1") game.DebugAudition();  // A3 AUDIO CHECK screen (+ SIGHTLINE_AUDITIONFIRE=1 lights the just-played rows)
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_HAZARD") == "1") game.DebugHazards();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_TAGEDIT") == "1") game.DebugTagEditor();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WOUND") == "1") game.DebugWound();
        // Q1: =1 stages the AIM-mode tooltip, =hover the plain hover-an-enemy tooltip (D4).
        bool tooltipHover = shot && Environment.GetEnvironmentVariable("SIGHTLINE_TOOLTIP") == "hover";
        if (shot && (Environment.GetEnvironmentVariable("SIGHTLINE_TOOLTIP") == "1" || tooltipHover))
            game.DebugTooltip(tooltipHover);
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
        int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_SHOTSEQ"), out int seqCount);   // Q1: consecutive-frame dump
        // RESONANCE C1: SIGHTLINE_SHOTONBARK=1 — do not shoot a fixed frame; wait until a soldier
        // BARK has actually landed in the combat log during live play, then shoot 40 frames later
        // (long enough for the line to settle into the ledger, short enough that it is still one of
        // the last six entries the panel shows). Pair with SIGHTLINE_AUTOPLAY=1 so a real fight is
        // driving. Shot-mode only; inert everywhere else, so nothing measured changes.
        bool shotOnBark = shot && Environment.GetEnvironmentVariable("SIGHTLINE_SHOTONBARK") == "1";
        if (shotOnBark) shotFrame = int.MaxValue;
        int frame = 0;
        // W1: the autoplay smoke test's frame budget, DERIVED rather than guessed. It had been a
        // round 20000 since it was written, with nothing in the repo saying where that came from
        // or how close a real campaign gets. Measured over 30 fresh Release autoplays
        // (docs/measurements/w1/framecount.txt): min 2461, median 9492, p90 12530, MAX 13589 —
        // so the old cap was 1.47x the observed maximum, a much thinner margin than anyone
        // had reason to believe, for a contract whose whole point is "never a TIMEOUT".
        //
        // NAMING, corrected by the W1 review: an earlier version of this called the reference
        // `autoP99` and quoted "p99 = 13536". You cannot estimate a 99th percentile from n=30 —
        // the top two samples are 13589 and 13407, so any "p99" is an interpolation between the
        // two largest observations and carries no more information than the max itself. The
        // honest statistic at this n is the OBSERVED MAXIMUM, and that is what this now is.
        // TO RE-DERIVE: bash docs/measurements/w1/framecount.sh 30   (raise n for a real quantile)
        const int autoMax = 13589;             // observed max over n=30, Release, base commit 5ae9149
        const int autoCap = 3 * autoMax;       // 40767

        while (!Raylib.WindowShouldClose())
        {
            float dt = (shot || autoplay) ? 1f / 60f : Raylib.GetFrameTime();
            Display.UpdateMouse();
            if (helpShot) Raylib.SetMousePosition(592, 740);   // park cursor on the ability button
            // RESONANCE T2: a staged hover for the forecast screenshot — the card + path preview are
            // hover-driven, so the harness has to hold the cursor on the tile every frame.
            if (shot && game.DebugMousePark.HasValue)
                Raylib.SetMousePosition((int)game.DebugMousePark.Value.X, (int)game.DebugMousePark.Value.Y);
            if (tooltipHover) game.KbCursor = true;            // Q1: hold the board cursor on the foe (a mouse
                                                               // delta from the Xvfb pointer clears it otherwise)
            game.Update(dt);
            Audio.UpdateMusic(dt);

            // Q1: autoplay normally skips the heavy draw (it's a smoke test), but a shot frame
            // requested ON TOP of autoplay is asking for a picture of live play — the only way to
            // photograph a unit MID-MOVE — so draw for real in that combination.
            // W1: when the draw is a bare ClearBackground, skip the frame entirely and just pump
            // the event queue (see BatchPump). Same reasoning as the three batch loops: the clear
            // and the buffer swap were the whole cost of an autoplay smoke run.
            if (autoplay && !shot) BatchPump();
            else Display.RenderFrame(game.Draw);

            if (shot || autoplay) frame++;
            // W5: dump the filmed unit's tweened board position every frame, so "positions advance
            // monotonically, no backwards step" is a MEASURED claim rather than an eyeball on PNGs.
            if (longMove && game.DebugFilmUnit != null)
                Console.WriteLine($"FILM {frame} {game.DebugFilmUnit.Pos.X:0.000} {game.DebugFilmUnit.Pos.Y:0.000}");
            if (shot)
            {
                if (shotOnBark && shotFrame == int.MaxValue && frame > 60
                    && Stats.CombatLog.Exists(e => e.Outcome == Voice.LogTag))
                    shotFrame = frame + 40;
                if (frame == shotFrame) Raylib.TakeScreenshot("sightline_shot.png");
                // Q1 SIGHTLINE_SHOTSEQ=<n>: also dump the n consecutive frames from shotFrame as
                // sightline_seq_NN.png. Pair with SIGHTLINE_AUTOPLAY=1 to film a multi-tile move —
                // the eyes-only check for the MoveStepAnim OnStart-at-enqueue jitter landmine
                // (CLAUDE.md: a step that captures _from at the ORIGINAL tile snaps back every
                // tile, and no test catches it).
                if (seqCount > 0 && frame >= shotFrame && frame < shotFrame + seqCount)
                    Raylib.TakeScreenshot($"sightline_seq_{frame - shotFrame:00}.png");
                if (!autoplay && frame >= shotFrame + Math.Max(2, seqCount)) break;
                if (autoplay && seqCount > 0 && frame >= shotFrame + seqCount) break;
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
                // W1: a TIMEOUT now says how far past normal it got. "frame=20000" alone told you
                // nothing about whether the cap was tight or the match was genuinely stuck.
                if (frame >= autoCap) { Console.WriteLine($"RESULT: TIMEOUT mission={game.RunState.Mission} frame={frame} cap={autoCap} observedMax={autoMax} ({(double)frame / autoMax:0.0}x the longest campaign measured)"); break; }
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
    //   SIGHTLINE_BALANCE_DRAW=1    W1: restore the pre-W1 per-frame GL clear (see BatchPump).
    //                               Provably inert on the numbers; it only costs wall-clock.
    //
    // APEX W4 — `endless: true` (SIGHTLINE_BALANCE_ENDLESS=<N>) points the same machinery at
    // LAST STAND: each "campaign" slot becomes one endless stand via BeginEndless, and depth
    // (waves survived) is logged from game.Wave at EVERY exit — wipe, wave-cap, frame-cap,
    // abort — NEVER from RunState.Mission (endless keeps Mission==1, so the old campaign
    // fallback would log every capped deep stand as depth 0 and corrupt the p90).
    // ── W1 TRUE INSTRUMENT: the headless batch frame pump ───────────────────────────────
    // Every headless batch loop (balance / pairtest / stacktest) used to call
    // `Display.RenderFrame(() => Raylib.ClearBackground(Pal.Bg))` once per SIMULATED frame.
    // Nothing in that call draws game content — its only job was to make raylib pump the
    // window's event queue so `WindowShouldClose()` stays honest — but it still cost a full
    // llvmpipe clear + buffer swap per frame, and a 20 000-frame campaign pays it 20 000
    // times for a picture nobody looks at. `PollInputEvents()` IS that pump on its own
    // (raylib's `EndDrawing()` is `SwapScreenBuffer() + PollInputEvents()`), so the close
    // semantics are preserved exactly and the swap is dropped.
    // SIGHTLINE_BALANCE_DRAW=1 restores the old path verbatim for an A/B.
    static readonly bool BatchDraw = Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE_DRAW") == "1";
    static void BatchPump()
    {
        if (BatchDraw) Display.RenderFrame(() => Raylib.ClearBackground(Pal.Bg));
        else Raylib.PollInputEvents();
    }

    // W1: a headless batch with NO DISPLAY was indistinguishable from a completed one.
    // InitWindow fails, `WindowShouldClose()` returns true before frame one, every match loop
    // falls straight through, and the process STILL printed a full-looking report, still wrote
    // an aggregate JSON (with runs=0 in it), still claimed N matches — and then exited 139 out
    // of the GL teardown, which is easy to read as "finished, then crashed on the way out".
    // X2 had to bolt an external `runs`-field assertion onto every chunk script because of it.
    // Refuse at the door instead: name the cause, write nothing, exit non-zero.
    static void RequireWindow(string what)
    {
        if (Raylib.IsWindowReady()) return;
        Console.Error.WriteLine($"{what}: no display - run under xvfb-run. No data written.");
        Console.Error.Flush();
        Environment.Exit(2);
    }

    static void BalanceBatch(int runs, bool endless = false)
    {
        // Cumulative telemetry across the whole batch (NOT reset per match).
        Stats.Reset();
        Stats.Enabled = true;

        // Keep batch-wide static state deterministic across matches.
        Mission.ForcedLayout = -1;       // no forced arena
        Pal.SetColorblind(false);        // default palette (irrelevant headless, set defensively)
        // TRUE BAND: SIGHTLINE_BANDPROBE=1 dumps the CHOICE-BAND score distributions alongside
        // the report (see ChoiceProbe). Read-only, default OFF, no RNG draw — the batch it runs
        // under is byte-identical to the same batch without it.
        ChoiceProbe.On = Environment.GetEnvironmentVariable("SIGHTLINE_BANDPROBE") == "1";

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
        RequireWindow("BALANCE");   // W1: no display => refuse, write nothing, exit 2
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
                BatchPump();   // W1: event pump only (SIGHTLINE_BALANCE_DRAW=1 restores the GL clear)
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
        if (ChoiceProbe.On) Console.WriteLine(ChoiceProbe.Report());
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
        RequireWindow("PAIRTEST");  // W1: a display-less PAIRTEST proves nothing; do not pretend
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
                BatchPump();
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

    // ── W1 TRUE INSTRUMENT: SIGHTLINE_RNGFRAMETEST — gameplay is a function of the SEED ───────
    // The headline defect this wave exists to fix: Fx.Update rolled Util.RandF() once per
    // RENDERED FRAME for as long as a screen shake was decaying, on the SHARED GAMEPLAY stream.
    // So the dice a campaign rolled depended on how many frames were drawn while the screen was
    // wobbling — which depends on the frame rate, on the animation-speed comfort setting, and on
    // whether the player has screen shake switched on at all (Fx.ShakeOn is a shipped
    // accessibility toggle). Two players on the same seed with different comfort settings played
    // different fights. The measurement harness never saw it because it pins dt to 1/60 AND pins
    // AnimSpeed to 1x — it was surviving by holding the frame rate still.
    //
    // This test refuses to hold it still. It replays four pinned seeds under the cross product of
    // {AnimSpeed 1x, 8x} x {shake ON, shake OFF} and demands the SAME outcome, the same missions
    // and the same total turns from all four. It has a vacuity guard: the 1x and 8x legs must
    // differ in FRAME COUNT, or the animation-speed lever is not wired and the test proves nothing.
    //
    // It FAILS by design on the pre-W1 tree — and it still can, on demand, in the shipped binary:
    // SIGHTLINE_FXRNG=0 re-couples Fx to the gameplay stream (see Util.FxRng).
    static void RngFrameTest()
    {
        Stats.Reset();
        Stats.Enabled = true;
        Mission.ForcedLayout = -1;
        Pal.SetColorblind(false);

        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "SIGHTLINE — rng frame test");
        RequireWindow("RNGFRAMETEST");
        Raylib.SetExitKey(KeyboardKey.Null);
        Cfg.Font = Raylib.GetFontDefault();
        Display.Init(false);
        Raylib.SetTargetFPS(0);

        const int frameCap = 40000;

        // House stash-and-restore: this hook dials SIGHTLINE_HEAT and must not leave it dialled
        // for anything later in the process (the SIGHTLINE_OBJ / Mission.ForcedLayout precedent).
        string savedHeat = Environment.GetEnvironmentVariable("SIGHTLINE_HEAT");

        (string result, int missions, int turns, int frames, int shakes) Leg(int seed, float animSpeed, bool shake)
        {
            Environment.SetEnvironmentVariable("SIGHTLINE_HEAT", "2");
            Util.Reseed(seed);                 // the ONLY thing that may determine the fight
            Stats.Slot = seed;
            var game = new Game { NoPersist = true, AutoPlay = true, SmartPlay = true,
                                  AnimSpeedOverride = animSpeed };
            game.Fx.ShakeOn = shake;           // the shipped comfort toggle, exercised as a variable
            game.SeedSloppy(1000 + seed);      // greedy never draws from it; seeded for parity
            game.StartMission(1);
            int frame = 0;
            while (!Raylib.WindowShouldClose())
            {
                game.Update(1f / 60f);
                BatchPump();
                if (game.Phase == Phase.Win || game.Phase == Phase.Lose || ++frame >= frameCap) break;
            }
            Stats.EndRun(false, game.RunState != null ? game.RunState.Mission - 1 : 0, "frame-cap");
            var run = Stats.Runs[Stats.Runs.Count - 1];
            string result = game.Phase == Phase.Win ? "WIN" : game.Phase == Phase.Lose ? "LOSE" : "CAP";
            return (result, run.Missions.Count, run.Missions.Sum(m => m.Turns), frame, game.Fx.ShakeApplied);
        }

        bool pass = true;
        foreach (int seed in new[] { 99, 777, 4242, 31337 })
        {
            var refLeg = Leg(seed, 1f, true);                       // the shipped conditions
            var fast = Leg(seed, 8f, true);                          // anim speed 8x
            var noShake = Leg(seed, 1f, false);                      // comfort: shake off
            var fastNoShake = Leg(seed, 8f, false);                  // both at once

            bool Same((string result, int missions, int turns, int frames, int shakes) x)
                => x.result == refLeg.result && x.missions == refLeg.missions && x.turns == refLeg.turns;

            bool ok = Same(fast) && Same(noShake) && Same(fastNoShake);
            // VACUITY GUARD (a): if 8x did not change the frame budget, AnimSpeedOverride is not
            // reaching the anim queue and an "identical" result would mean nothing.
            bool animLever = fast.frames != refLeg.frames;
            // VACUITY GUARD (b): the SHAKE lever must actually have been thrown. The W1 review
            // deleted `game.Fx.ShakeOn = shake;` from this test and it still printed MATCH x4 and
            // PASS with byte-identical output — it was certifying an invariance it never varied.
            // Fx.ShakeApplied counts the AddShake calls that actually moved the screen, so the
            // shake legs must show some and the no-shake legs must show none.
            bool shakeLever = refLeg.shakes > 0 && noShake.shakes == 0 && fastNoShake.shakes == 0;
            if (!animLever || !shakeLever) ok = false;
            pass &= ok;
            string why = ok ? "MATCH"
                       : !animLever ? "VACUOUS (anim-speed lever inert)"
                       : !shakeLever ? $"VACUOUS (shake lever inert: shk {refLeg.shakes}/{noShake.shakes})"
                       : "MISMATCH";
            Console.WriteLine(
                $"RNGFRAMETEST: seed{seed}  1x/shake {refLeg.result} m={refLeg.missions} t={refLeg.turns} f={refLeg.frames} shk={refLeg.shakes}"
              + $" | 8x/shake {fast.result} m={fast.missions} t={fast.turns} f={fast.frames} shk={fast.shakes}"
              + $" | 1x/noshake {noShake.result} m={noShake.missions} t={noShake.turns} f={noShake.frames} shk={noShake.shakes}"
              + $" | 8x/noshake {fastNoShake.result} m={fastNoShake.missions} t={fastNoShake.turns} f={fastNoShake.frames} shk={fastNoShake.shakes}"
              + $"  -> {why}");
            // a leg that never played a mission proves nothing either (the FUL-5 vacuous-PASS trap)
            if (refLeg.missions <= 0) { pass = false; Console.WriteLine($"RNGFRAMETEST: seed{seed} VACUOUS — no mission played"); }
        }

        // PHASE 2: RENDER PURITY.
        // Phase 1 certifies FRAME-COUNT invariance, and it is honest about exactly that. It could
        // not, and did not, catch the defect the W1 review found: Unit()'s idle-bob phase drew
        // from the shared gameplay stream, and Renderer holds a `static readonly Unit` stub whose
        // initializer fires on the first DrawBoard. That is NOT a frame-count coupling — it is a
        // FIXED ONE-DRAW OFFSET between a process that renders and one that does not, and after
        // W1/1 and W1/4 the whole measurement harness is a process that does not render. Phase 1
        // runs every leg through the same non-rendering path, so all four legs agreed.
        //
        // This phase pins the RULE instead of the symptom: PRESENTATION TAKES ZERO DRAWS FROM
        // Util.Rng. (a) constructing a Unit costs nothing — the exact defect; (b) drawing real
        // frames of a real board costs nothing — the class; (c) the probe is proven SENSITIVE by
        // running it once with a deliberate draw (the VOICETEST precedent), so it cannot pass
        // vacuously. A grep for Util.Rand* in Fx.cs would NOT have caught Bob — it is in Unit.cs.
        {
            const int K = 24;
            int[] Draws() { var a = new int[K]; for (int i = 0; i < K; i++) a[i] = Util.Rng.Next(1 << 20); return a; }
            bool SeqEq(int[] a, int[] b) { for (int i = 0; i < K; i++) if (a[i] != b[i]) return false; return true; }
            bool Clean(Action body)
            {
                Util.Reseed(31337);
                var pre = Draws();
                Util.Reseed(31337);
                body();
                return SeqEq(pre, Draws());
            }

            Environment.SetEnvironmentVariable("SIGHTLINE_HEAT", "0");
            Util.Reseed(4242);
            var stage = new Game { NoPersist = true, AutoPlay = true, SmartPlay = true };
            stage.StartMission(1);
            for (int i = 0; i < 120; i++) stage.Update(1f / 60f);   // let a real board settle

            int bobSink = 0;
            bool unitClean = Clean(() => { var stub = new Unit(); bobSink += stub.Bob > 0f ? 1 : 0; });
            bool drawClean = Clean(() => { for (int i = 0; i < 30; i++) Display.RenderFrame(stage.Draw); });
            bool sensitive = !Clean(() => { Util.Rng.Next(); });     // the probe must SEE one draw
            if (bobSink < 0) Console.Write("");                      // keep the ctor call observable

            bool phase2 = unitClean && drawClean && sensitive;
            pass &= phase2;
            Console.WriteLine($"RNGFRAMETEST: render-purity  newUnit={(unitClean ? "clean" : "DREW")}"
                            + $"  draw30Frames={(drawClean ? "clean" : "DREW")}"
                            + $"  probeSensitive={(sensitive ? "yes" : "NO — VACUOUS")}"
                            + $"  -> {(phase2 ? "PASS" : "FAIL")}");
        }

        Console.WriteLine(pass ? "RNGFRAMETEST: PASS" : "RNGFRAMETEST: FAIL");

        Util.Reseed(0);
        Stats.Slot = -1;
        Stats.Enabled = false;
        // restore the harness environment exactly as found (house stash-and-restore)
        Environment.SetEnvironmentVariable("SIGHTLINE_HEAT", savedHeat);
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

    // ── Q1 "NO TWO IN ONE PLACE" — SIGHTLINE_STACKTEST ────────────────────────────────────
    // Two living units must never occupy the same tile. They used to: Game.ActivatePod planned
    // EVERY dormant pod member against the live board and enqueued all their reveal-scatter
    // steps before any of them ran, so member 2 planned blind to where member 1 was going (and
    // blind to the player's own still-queued path steps, which sit AHEAD of the scatter in the
    // queue). Two bodies on one tile is not cosmetic: Game.UnitAt returns the FIRST match
    // (Players before Enemies), and both hover and click route through it, so the buried unit
    // cannot be hovered, cannot show odds, and cannot be clicked to target — a direct hit on the
    // "reads clearly" pillar — while both read cover=0/flanked at range 0.
    //
    // Two independent detectors, both live for the whole sweep:
    //   (a) STEP detector — MoveStepAnim.StackProbe fires when a step becomes the ACTIVE anim.
    //       Unit.X/Y is still the origin there and only one anim is ever active, so a non-null
    //       UnitAt(Tx,Ty) that isn't the mover is a proven imminent collision, not an artifact.
    //   (b) FRAME detector — a per-frame sweep over the living roster for a shared tile,
    //       collapsed into EPISODES (a tile+pair overlap that persists across frames counts
    //       once) so the report shows how long a stack actually lingers.
    // Non-vacuity guard (the PAIRTEST precedent): a run with no display returns before frame one
    // and would "pass" on zero gameplay, so PASS also requires a real sample of move steps.
    static void StackTest(bool wide = false)
    {
        Stats.Reset();
        Stats.Enabled = false;         // pure invariant sweep; no telemetry needed
        Mission.ForcedLayout = -1;
        Pal.SetColorblind(false);

        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "SIGHTLINE — stack test");
        RequireWindow("STACKTEST"); // W1: ditto — zero sampled frames is not a clean sweep
        Raylib.SetExitKey(KeyboardKey.Null);
        Cfg.Font = Raylib.GetFontDefault();
        Display.Init(false);
        Raylib.SetTargetFPS(0);

        long steps = 0, stepHits = 0;
        int episodes = 0, longestFrames = 0, framesWithOverlap = 0;
        var openEpisodes = new System.Collections.Generic.Dictionary<string, int>();
        var seenThisFrame = new System.Collections.Generic.HashSet<string>();
        var examples = new System.Collections.Generic.List<string>();
        int missionsSeen = 0;

        MoveStepAnim.StackProbe = (g, m) =>
        {
            steps++;
            var occ = g.UnitAt(m.Tx, m.Ty);
            if (occ == null || occ == m.Unit) return;
            stepHits++;
            if (examples.Count < 6)
                examples.Add($"{m.Unit.Team}/{m.Unit.Cls} -> ({m.Tx},{m.Ty}) already held by {occ.Team}/{occ.Cls} [phase={g.Phase}]");
        };

        // one leg = the first mission of a campaign pinned to (objective, heat), dumb autopilot
        // (the reveal-scatter path is policy-independent, and the dumb bot blunders into pods
        // more often, which is exactly the trigger we want to sample).
        void Leg(Objective obj, int heat, int slot, bool smart)
        {
            Environment.SetEnvironmentVariable("SIGHTLINE_HEAT", heat.ToString());
            Util.Reseed(70000 + slot);
            var game = new Game { NoPersist = true, AutoPlay = true, SmartPlay = smart, ForcedObjective = obj };
            game.StartMission(1);
            int frame = 0;
            int legCap = wide ? 20000 : 6000;
            int missionsThisLeg = 0;
            while (!Raylib.WindowShouldClose())
            {
                game.Update(1f / 60f);
                BatchPump();

                // ---- (b) per-frame shared-tile sweep ----
                seenThisFrame.Clear();
                var all = new System.Collections.Generic.List<Unit>();
                foreach (var u in game.Players) if (u.Alive) all.Add(u);
                foreach (var u in game.Enemies) if (u.Alive) all.Add(u);
                for (int i = 0; i < all.Count; i++)
                    for (int j = i + 1; j < all.Count; j++)
                        if (all[i].X == all[j].X && all[i].Y == all[j].Y)
                            seenThisFrame.Add($"{obj}h{heat}:{all[i].X},{all[i].Y}:{all[i].Cls}|{all[j].Cls}");
                if (seenThisFrame.Count > 0) framesWithOverlap++;
                foreach (var k in seenThisFrame)
                {
                    if (openEpisodes.TryGetValue(k, out int n)) openEpisodes[k] = n + 1;
                    else { openEpisodes[k] = 1; episodes++; }
                }
                var stale = new System.Collections.Generic.List<string>();
                foreach (var kv in openEpisodes)
                    if (!seenThisFrame.Contains(kv.Key)) { if (kv.Value > longestFrames) longestFrames = kv.Value; stale.Add(kv.Key); }
                foreach (var k in stale) openEpisodes.Remove(k);

                frame++;
                if (game.Phase == Phase.Lose || game.Phase == Phase.Win) break;
                // narrow sweep: stop the moment the FIRST mission resolves (a win advances
                // RunState.Mission). Wide sweep: play the whole campaign out.
                int cleared = game.RunState != null ? game.RunState.Mission - 1 : 0;
                if (cleared > missionsThisLeg) missionsThisLeg = cleared;
                if (!wide && missionsThisLeg >= 1) break;
                if (frame >= legCap) break;
            }
            foreach (var kv in openEpisodes) if (kv.Value > longestFrames) longestFrames = kv.Value;
            openEpisodes.Clear();
            missionsSeen += Math.Max(1, missionsThisLeg + (game.Phase == Phase.Lose ? 1 : 0));
        }

        var objs = new[] { Objective.Eliminate, Objective.Hack, Objective.Evac, Objective.Escort,
                           Objective.Sabotage, Objective.Rescue, Objective.Defend, Objective.Decapitate };
        int s2 = 0;
        foreach (bool smart in wide ? new[] { false, true } : new[] { false })
            foreach (int heat in new[] { 0, 2 })
                foreach (var o in objs)
                {
                    if (Raylib.WindowShouldClose()) break;
                    Leg(o, heat, s2++, smart);
                }

        MoveStepAnim.StackProbe = null;
        Util.Reseed(0);
        Display.Shutdown();
        Renderer.UnloadNoise();
        Raylib.CloseWindow();

        Console.WriteLine($"STACKTEST: mode={(wide ? "wide" : "narrow")} missions={missionsSeen} moveSteps={steps} " +
                          $"stepCollisions={stepHits} ({(steps > 0 ? 100.0 * stepHits / steps : 0):0.000}%) " +
                          $"overlapEpisodes={episodes} overlapFrames={framesWithOverlap} longestEpisodeFrames={longestFrames}");
        foreach (var e in examples) Console.WriteLine("STACKTEST:   e.g. " + e);
        var fails = new System.Collections.Generic.List<string>();
        if (stepHits > 0) fails.Add($"stepCollisions={stepHits}");
        if (episodes > 0) fails.Add($"overlapEpisodes={episodes}");
        if (steps < 500) fails.Add($"vacuous(moveSteps={steps})");   // no display / no gameplay
        Console.WriteLine(fails.Count == 0
            ? "STACKTEST: PASS (no move step ever entered an occupied tile; no two living units ever shared one)"
            : "STACKTEST: FAIL (" + string.Join(",", fails) + ")");
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

    /// RESONANCE C1 — extracted verbatim from the inline block that used to live in Main, so a
    /// window-free-ish self-test hook (SIGHTLINE_VOICETEST measures real glyph widths) can bake
    /// the same atlases the game uses. Idempotent enough for the harness: call it once, after
    /// InitWindow. Behaviour is unchanged for the normal launch path.
    static void LoadGameFonts()
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
}
