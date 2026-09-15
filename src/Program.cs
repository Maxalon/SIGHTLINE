using System;
using Raylib_cs;

namespace Sightline;

public static class Program
{
    /// PARALLAX P11 "THE CRASH FILE" — the whole process, inside one handler.
    ///
    /// `Main` is deliberately nothing but this. Everything the game does now runs inside
    /// `Crash.Guard`, so an exception that would previously have printed a stack trace to a stdout
    /// nobody reads (C6's open item: "there is nothing to attach") instead writes a report the
    /// player can send, into the same directory their save already lives in, and exits cleanly
    /// with 70 (EX_SOFTWARE) rather than as an unhandled-exception abort.
    ///
    /// `Crash.Install` additionally covers what `Guard` structurally cannot: a throw on a
    /// background thread or an unobserved task, which unwinds past Main entirely.
    ///
    /// SCOPE, STATED HONESTLY (docs/DISTRIBUTION.md §6): this covers MANAGED exceptions. A raylib
    /// ABI mismatch that faults inside native code kills the process without unwinding, and no
    /// handler here runs. The load-time family — missing / wrong-architecture / wrong-version
    /// native library — IS catchable, and is turned into plain English by `Crash.NativeDiagnosis`
    /// instead of a P/Invoke stack trace.
    public static void Main()
    {
        Crash.AttachWindowsConsole();   // no-op off Windows; see Crash.AttachWindowsConsole
        Crash.Install();
        int rc = Crash.Guard("game loop", RealMain);
        if (rc != 0) Environment.Exit(rc);
    }

    static void RealMain()
    {
        // ══ C6: THE SECOND-LAUNCH CHILD. THIS BRANCH MUST STAY FIRST IN Main. ══════════════════
        // SIGHTLINE_SHIPTEST forks this same binary with SIGHTLINE_SHIPCHILD=1 so that "quit the
        // game, start it again, your progress is there" can be checked by TWO PROCESSES rather
        // than asserted by one — no other hook in this project can check it, because the house
        // rule (never touch the player's real profile) makes every hook stash-and-restore inside a
        // single run.
        //
        // IT IS FIRST FOR A REASON, MEASURED THE HARD WAY: the first version of this branch sat
        // below the SHIPTEST branch, and the child inherited SIGHTLINE_SHIPTEST=1 from its parent.
        // The child therefore ran SHIPTEST, which forked a grandchild, which ran SHIPTEST... a
        // fork bomb that reached 184 processes on a container shared with five other agents before
        // it was killed. TWO independent guards now stop that, and both must stay:
        //   (a) this branch is FIRST, so a process carrying SHIPCHILD can never reach SHIPTEST; and
        //   (b) Ship.SecondLaunchProbe strips EVERY SIGHTLINE_* variable from the child's
        //       environment before setting SHIPCHILD, so the child inherits no test mode at all.
        // The child writes to the LIVE player-data directory on purpose; it is SHIPTEST's
        // stash/restore that cleans up, so never set SIGHTLINE_SHIPCHILD by hand.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_SHIPCHILD") == "1")
        {
            Ship.SecondLaunchChild();
            return;
        }

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
        // ══ P27 PROTOTYPE — SIGHTLINE_VIEW3DSHOT ══
        // Photograph the SAME staged board through the projected 3D camera at a sweep of angles, so
        // the "what pitch does this game want?" question is answered by looking rather than by
        // argument. Value is a comma-separated list of pitch:yaw pairs in degrees, e.g.
        //   SIGHTLINE_VIEW3DSHOT=25:0,40:0,55:0,90:0,40:45
        // Writes view3d_p<pitch>_y<yaw>.png per entry and exits. Pair with SIGHTLINE_SHOT=760
        // (the briefing card covers the board before ~700) and SIGHTLINE_SEED=<n> to hold the board
        // fixed across a sweep. View3D.Enabled is set ONLY here, so no other path can reach it.
        List<(float pitch, float yaw)> view3dSweep = null;
        {
            string v3 = Environment.GetEnvironmentVariable("SIGHTLINE_VIEW3DSHOT");
            if (!string.IsNullOrWhiteSpace(v3))
            {
                view3dSweep = new List<(float, float)>();
                foreach (var pair in v3.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pq = pair.Split(':');
                    float.TryParse(pq[0].Trim(), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float pd);
                    float yd = 0f;
                    if (pq.Length > 1)
                        float.TryParse(pq[1].Trim(), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out yd);
                    view3dSweep.Add((pd, yd));
                }
                if (view3dSweep.Count > 0) View3D.Enabled = true;
            }
        }
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
        // P14 THE UNVERIFIED — SIGHTLINE_MODEDEPTH=0 restores the pre-P14 single-mission modes
        // EXACTLY: Mission.ModeDepth stays -1, so DepthFor is the identity everywhere and the
        // opener trim / escort asset / HVT bonus / DEFEND waves go back to reading the literal
        // mission 1 that SKIRMISH and DAILY pass, and the "never an entirely immobile force" guard
        // is off with it. It is what MODETEST's P14 legs were shown to FAIL against.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_MODEDEPTH") == "0") Mission.ModeDepthOn = false;
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
        //   SIGHTLINE_AIMTRIM=<n>    : Mission.HostileAimTrim (flat points off every hostile's aim).
        //                              **P24 THE TOP OF THE LADDER ships 5, so `=0` is this wave's
        //                              RESTORE FLAG** — the pre-P24 force exactly, and the arm its
        //                              CRN round used as the baseline. It is a LEVEL lever: it
        //                              moves every rung, and no rung is an inertness control for it.
        //   SIGHTLINE_ENEMYBASE=<n>  : Mission.EnemyBaseCount (the `count = base + mission` constant)
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_AIMTRIM"), out int xaim) && xaim >= 0)
            Mission.HostileAimTrim = xaim;
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_ENEMYBASE"), out int xbase) && xbase >= 0)
            Mission.EnemyBaseCount = xbase;
        //   SIGHTLINE_OPENERTRIM=<n> : Mission.OpenerTrim (bodies off the m1 / half off m2 force)
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_OPENERTRIM"), out int xopen) && xopen >= 0)
            Mission.OpenerTrim = xopen;
        // P23 "THE APEX BITES" — TWO INDEPENDENT DIALS, and their independence is the point.
        // L7 located the apex rung's dead teeth in Mission.SpawnEnemies and priced them with a
        // COMBINED arm (SIGHTLINE_ENEMYBASE=2) that could only ease the ceiling; its +4.1 could
        // not resolve because the stat half was not in it. These two must be switchable alone.
        //   SIGHTLINE_CLAMPLAST=0   : LEVER A off — the board ceiling is applied to the REQUEST
        //                             rather than to the force that is seated, so every trim below
        //                             it subtracts from an already-clipped number (the pre-P23
        //                             order, in which the finale's bodies stop growing at heat 4).
        //   SIGHTLINE_FINALESTAT=0  : LEVER B off — the finale discards heat's StatDelta along
        //                             with the deployment card's (the pre-P23 strip).
        //   SIGHTLINE_FORCECEILING=<n> : Mission.ForceCeiling, the board-SEATING limit (shipped
        //                             12, unchanged by P23). Priced by FORCETEST leg (E), unspent.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_CLAMPLAST") == "0") Mission.ClampLast = false;
        // ── P28 RESTORE FLAGS ────────────────────────────────────────────────────────────────
        //   SIGHTLINE_EDGES=0      : the EDGE layer off. Gates every edge QUERY rather than the
        //                            arrays, so even a hand-stamped wall is inert and the board is
        //                            the pre-P28 board EXACTLY.
        //   SIGHTLINE_BUILDINGS=1  : buildings ON. They default OFF only because Renderer.cs
        //                            cannot draw a wall yet — an invisible obstacle is worse than
        //                            no obstacle. Flip the default with the renderer, not before.
        //   SIGHTLINE_BUILDINGS=0  : the only thing that PLACES an edge, off. Spends zero
        //                            Util.Rng draws when off, so it is a free arm and not merely
        //                            a faithful one — a flag that moves the RNG stream cannot be
        //                            used to isolate anything.
        // The pair is what "restore the pre-P28 board" means: EDGES alone leaves the draws spent.
        // ── P29 SIGHTLINE_BIGMAP=<W>x<H>[x<TILE>] ────────────────────────────────────────────
        // Grows the board. MUST be read before any Grid is constructed (Grid captures W/H at
        // construction) and before InitWindow (Tile drives the layout), which is why it lives up
        // here with the other pre-flight switches rather than in SetupMission.
        //
        // IT ORPHANS THE 35 AUTHORED ARENAS, DELIBERATELY AND LOUDLY. They are drawn for 18x11;
        // at any other size Mission.PlanBoard refuses them ONCE with a named reason instead of
        // silently falling back per mission, and SIGHTLINE_TEMPLATEGATE reports them unusable.
        // That was P28's whole purpose in making a dimension mismatch loud — this is the change
        // it was landed ahead of.
        {
            string bm = Environment.GetEnvironmentVariable("SIGHTLINE_BIGMAP");
            if (!string.IsNullOrWhiteSpace(bm))
            {
                var parts = bm.Split('x', 'X');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int bw) && int.TryParse(parts[1], out int bh))
                {
                    int bt = parts.Length >= 3 && int.TryParse(parts[2], out int t) ? t : 0;
                    Cfg.SetBoard(bw, bh, bt);
                    Console.WriteLine($"SIGHTLINE_BIGMAP: board {Cfg.GridW}x{Cfg.GridH} @ {Cfg.Tile}px "
                                    + $"({Cfg.BoardW}x{Cfg.BoardH} px vs {Cfg.ScreenW}x{Cfg.ScreenH} screen)");
                }
                else Console.Error.WriteLine($"SIGHTLINE_BIGMAP: could not read \"{bm}\" - want <W>x<H> or <W>x<H>x<TILE>. Board unchanged.");
            }
        }
        if (Environment.GetEnvironmentVariable("SIGHTLINE_EDGES") == "0") Edges.Enabled = false;
        if (Environment.GetEnvironmentVariable("SIGHTLINE_DISCOVERY") == "1") Vision.Enabled = true;
        // P32 — start in the PROJECTED view. Unlike SIGHTLINE_VIEW3DSHOT (which photographs the
        // board through a bypass path and exits), this sets the same flag the I key toggles, so the
        // game runs normally: full HUD, full input, the 3D board underneath.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_VIEW3D") == "1") View3D.Enabled = true;
        // P33 — SIGHTLINE_VIEW3DCAM=pitch:yaw:zoom[:panX:panY] stages the projected camera before
        // the first frame, so SIGHTLINE_SHOT can photograph a state that otherwise needs a hand on
        // the keyboard. Parse-or-leave-alone: a field that does not read is skipped rather than
        // defaulted, because a silently defaulted camera photographs the wrong thing and looks
        // right. Verification aid only — nothing in normal play reads it.
        string v3cam = Environment.GetEnvironmentVariable("SIGHTLINE_VIEW3DCAM");
        if (!string.IsNullOrEmpty(v3cam))
        {
            var f = v3cam.Split(':');
            if (f.Length > 0 && float.TryParse(f[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float cp))
                View3D.PitchDeg = Util.Clamp(cp, View3D.PitchMin, View3D.PitchMax);
            if (f.Length > 1 && float.TryParse(f[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float cy))
                View3D.YawDeg = Util.Wrap360(cy);
            if (f.Length > 2 && float.TryParse(f[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float cz))
                View3D.Zoom = Util.Clamp(cz, View3D.ZoomMin, View3D.ZoomMax);
            float px = 0f, py = 0f;
            if (f.Length > 3) float.TryParse(f[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out px);
            if (f.Length > 4) float.TryParse(f[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out py);
            View3D.Pan = new System.Numerics.Vector2(px, py);
            Console.WriteLine($"VIEW3DCAM: pitch {View3D.PitchDeg:0} yaw {View3D.YawDeg:0} zoom {View3D.Zoom:0.00} pan {px:0.0},{py:0.0}");
        }
        if (Environment.GetEnvironmentVariable("SIGHTLINE_BUILDINGS") == "0") Mission.Buildings = false;
        if (Environment.GetEnvironmentVariable("SIGHTLINE_BUILDINGS") == "1") Mission.Buildings = true;
        if (Environment.GetEnvironmentVariable("SIGHTLINE_FINALESTAT") == "0") Mission.FinaleHeatStat = false;
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_FORCECEILING"), out int xceil) && xceil >= 3)
            Mission.ForceCeiling = xceil;
        // P19 THE ROSTER CONTESTS — SIGHTLINE_ELITEBOSS=0 restores the pre-P19 mid-boss rule
        // (`n == 3 || n == 5`, blind to the node the player routed through) EXACTLY. It is what
        // HORDETEST's P19 legs were shown to FAIL against.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_ELITEBOSS") == "0") Mission.EliteBoss = false;
        // P19 THE ROSTER CONTESTS — SIGHTLINE_ROSTERID=0 restores the pre-P19 SMG monoculture:
        // every hostile SMG carrier back on the one shared range curve (Weapon.SmgProfile).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_ROSTERID") == "0") Mission.RosterIdentity = false;
        // C1 THE FLAT MIDDLE — SIGHTLINE_MIDTOOTH=<n> : Heat.MidTooth, which of NO QUARTER's two
        // qualitative teeth ride EXPOSED (rung 6) instead. Bitfield: 1 = the +1 per-hit damage,
        // 2 = coordination tier 2, 3 = both. **0 restores the pre-C1 table exactly** — that is
        // the control the wave was measured against and the off-switch that keeps the lever
        // falsifiable (docs/measurements/c1/, docs/DEVLOG.md §C1). Unset = the shipped 1.
        // Parsed BEFORE any Run/Game exists, so no mission can be built off a half-applied table.
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_MIDTOOTH"), out int xmid) && xmid >= 0)
            Sightline.Heat.SetMidTooth(xmid);
        // W8 THE HALF WALL — the DECAPITATE HVT statline buff (Game.DesignateHvt), pinnable so the
        // mid-run/finale asymmetry can be priced one lever at a time. Unset = the shipped defaults
        // 6 / 1 / 6, which are the pre-W8 arithmetic exactly, so an unpinned batch is unchanged.
        //   SIGHTLINE_HVTBUFF=<n>  : Combat.HvtHpBonusBase       (flat HP on a non-ELITE HVT)
        //   SIGHTLINE_HVTDEPTH=<n> : Combat.HvtHpBonusPerMission (HP per mission of depth; may be
        //                            NEGATIVE — "-1" is accepted, so this one is not >=0 gated)
        //   SIGHTLINE_HVTAIM=<n>   : Combat.HvtAimBonus          (aim points on a non-ELITE HVT)
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_HVTBUFF"), out int xhvt) && xhvt >= 0)
            Combat.HvtHpBonusBase = xhvt;
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_HVTDEPTH"), out int xhvtd))
            Combat.HvtHpBonusPerMission = xhvtd;
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_HVTAIM"), out int xhvta) && xhvta >= 0)
            Combat.HvtAimBonus = xhvta;
        // C3 THE TWO GAMES — SIGHTLINE_KILLTREADMILL=1 restores the pre-C3 anti-turtle clock, in
        // which the REINFORCEMENT arm also fired on ELIMINATE. It is the one objective whose win
        // condition counts bodies, so there the wave moved the finish line instead of raising the
        // price of reaching it. Default (unset) = suppressed on Eliminate only; Hack and
        // Decapitate keep both arms. See Game.ClockWavesOnEliminate.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_KILLTREADMILL") == "1") Game.ClockWavesOnEliminate = true;
        //   SIGHTLINE_HVTPOLICY=0 : an INSTRUMENT dial (autopilot only, no player-facing effect).
        //   Demotes the HVT from "every soldier charges it" to an ordinary target, so a Decapitate
        //   win rate can be split into what the MISSION costs and what the BOT's focus policy costs.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_HVTPOLICY") == "0") Game.SmartHvtFocus = false;
        // W2 THE OPPONENT ACTS — SIGHTLINE_AIIDLEFIX=0/1: the enemy stops spending a quarter of its
        // act-opportunities on nothing (planner ammo gate + terminal-else reposition + a reload verb).
        // It is a DIFFICULTY change and is priced as one; see docs/measurements/w2/ for the round and
        // docs/DEVLOG.md §W2 for the shipped default. =0 restores the pre-W2 opponent exactly.
        string aiIdleEnv = Environment.GetEnvironmentVariable("SIGHTLINE_AIIDLEFIX");
        if (aiIdleEnv == "1") Game.AiIdleFix = true; else if (aiIdleEnv == "0") Game.AiIdleFix = false;
        // C2 THE OPPONENT DECLINES — SIGHTLINE_AIDECLINE=0/1: the enemy's shot competes on its
        // expected value instead of on a flat +100 that dominated every terrain term in the
        // planner, and it may drop a bad shot for an overwatch lane or for cover. =0 restores the
        // pre-C2 opponent exactly (constant term, no decline gate). See docs/measurements/c2/.
        string aiDeclineEnv = Environment.GetEnvironmentVariable("SIGHTLINE_AIDECLINE");
        if (aiDeclineEnv == "1") Game.AiDecline = true; else if (aiDeclineEnv == "0") Game.AiDecline = false;
        // P10 THE HELD LANE — SIGHTLINE_AILANE=0/1: an ordinary enemy overwatch picks a 90-degree
        // cone (Ai.ChooseLane) instead of holding a 360-degree watch from wherever it stopped, and
        // therefore takes the player's own FOCUS trade — +Combat.FocusOwAim inside the lane, blind
        // outside it. =0 restores the pre-P10 opponent exactly (no axis planned, no cone armed).
        // See docs/measurements/p10/ for the CRN round and docs/DEVLOG.md §THE HELD LANE.
        string aiLaneEnv = Environment.GetEnvironmentVariable("SIGHTLINE_AILANE");
        if (aiLaneEnv == "1") Game.AiLane = true; else if (aiLaneEnv == "0") Game.AiLane = false;
        // P10, PRICED AND NOT SPENT — SIGHTLINE_DECLINEWATCH=<ratio> overrides Ai.DeclineWatchRatio
        // (shipped 0.45), the bar a hostile's marginal shot must clear before it drops the shot for
        // a watch. ROADMAP's standing claim was that a real lane would justify raising it. Unset =
        // the shipped value, so this is inert by default; the measured price is in
        // docs/measurements/p10/ and docs/DEVLOG.md §THE HELD LANE.
        if (float.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_DECLINEWATCH"),
                           System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out float dwRatio)
            && dwRatio >= 0f) Ai.DeclineWatchRatio = dwRatio;
        // C4 "EIGHT BIOMES ARE PAINT" — SIGHTLINE_BIOMEMECH=0/1: the biome GROUND layer (VERDANT
        // undergrowth / TUNDRA slick ice / MAGMA thermal vents). =0 restores the pre-C4 board
        // EXACTLY (Terrain.Enabled gates the stamper AND every Grid predicate), which is both the
        // A/B lever for the CRN round and the "watch your own test fail" proof for BIOMETEST.
        // P16 "GROUND TRUTH" — SIGHTLINE_NEWGROUND=0/1: JUST the two grounds P16 added (VOID's
        // RIFT and ARID's SOFT SAND). This is the A/B lever P16's CRN round was measured on, and
        // it exists because SIGHTLINE_BIOMEMECH=0 below is the WRONG arm for it: that restores the
        // pre-C4 board, so a round against it would price C4's three biomes and P16's two together
        // and report the sum as P16's. Off = the pre-P16 board exactly (VOID and ARID paint again).
        string newGroundEnv = Environment.GetEnvironmentVariable("SIGHTLINE_NEWGROUND");
        if (newGroundEnv == "0" || newGroundEnv == "1") Terrain.NewGround = newGroundEnv == "1";

        string biomeMechEnv = Environment.GetEnvironmentVariable("SIGHTLINE_BIOMEMECH");
        if (biomeMechEnv == "1") Terrain.Enabled = true; else if (biomeMechEnv == "0") Terrain.Enabled = false;
        // P20 "THE STALE GROUND" — SIGHTLINE_STALEGROUND=0/1. =1 restores the pre-fix seam, in
        // which Mission.Build's floor / cost / connectivity queries read the PREVIOUS mission's
        // ground layer (Build wiped Tiles, Height and Smoke but not Ground, and this mission's
        // layer is not stamped until after Build returns). That made the arena a function of the
        // board before it: measured at 8-15% of processes, the SEEDED DAILY dealt a different
        // board on its first build than on its second. Kept as an off switch so the board change
        // is attributable and priceable, and because MODETEST leg (14) flips it to prove its own
        // detector can fail. Never ship it on.
        string staleGroundEnv = Environment.GetEnvironmentVariable("SIGHTLINE_STALEGROUND");
        if (staleGroundEnv == "1") Mission.ClearGroundOnBuild = false;
        else if (staleGroundEnv == "0") Mission.ClearGroundOnBuild = true;
        // P21 "BUILD OWNS THE BOARD" — SIGHTLINE_STALEHAZARDS=0/1, the same switch for the two
        // layers L6 found next door. =1 restores the pre-fix seam, in which Mission.Build did not
        // clear Fire or Barrel and its connectivity floods could read the previous mission's
        // barrels (Grid.IsFloor tests !Barrel in the same predicate as the rift). UNLIKE
        // STALEGROUND this one is INERT ON EVERY SHIPPED PATH — Game.SetupMission's own
        // Grid.ClearHazards() is kept, so the arrays are already zero when Build runs — which is
        // why L6 called that defect LATENT, not live, and why P21's balance round expects a
        // byte-identical JSON on either setting. MODETEST leg (14a-2) flips it to prove its dirt
        // still bites.
        string staleHazEnv = Environment.GetEnvironmentVariable("SIGHTLINE_STALEHAZARDS");
        if (staleHazEnv == "1") Mission.ClearHazardsOnBuild = false;
        else if (staleHazEnv == "0") Mission.ClearHazardsOnBuild = true;
        // P26 "THE ARENA OWNS THE FIGHT" — SIGHTLINE_ARENASITES=0 restores the PRE-WAVE ORDER as
        // well as the literal sites: no Mission.PlanBoard call, no roll before SpawnEnemies, the
        // arena gate back inside Build, Game.SetupMission's four literal site blocks live again,
        // and the 3x3 ring punched through the authored terrain at every objective. That is a
        // restore by CONSTRUCTION -- the pre-wave gate block is kept verbatim in an else branch.
        // ⚠ This wave MOVES THE CRN STREAM (same draw COUNT per build, different position, and
        // three loops in Build consume draws conditional on board content), so every archived CRN
        // world is invalidated -- the W1 class of break. =0 is the BRIDGE ARM for re-measuring.
        string arenaSitesEnv = Environment.GetEnvironmentVariable("SIGHTLINE_ARENASITES");
        if (arenaSitesEnv == "0") Mission.ArenaSites = false;
        else if (arenaSitesEnv == "1") Mission.ArenaSites = true;
        // ...and the deployment half on its own dial (P23's two-lever precedent): =0 ignores the
        // 'A' anchor glyph while keeping arena SITES, so a round can price objective geometry
        // without also re-pricing the deployment geometry W4 measured.
        string arenaAnchEnv = Environment.GetEnvironmentVariable("SIGHTLINE_ARENAANCHORS");
        if (arenaAnchEnv == "0") Mission.ArenaAnchors = false;
        else if (arenaAnchEnv == "1") Mission.ArenaAnchors = true;
        // P21 "BUILD OWNS THE BOARD", second half — SIGHTLINE_FORKPRICES=0/1, the off switch
        // THE FORK PAYS (milestone 5) should have shipped and did not. That wave repriced the
        // campaign routing economy — Run.DepthBase 10 -> 12, a SUPPLY discount, a PITCHED class
        // price, the ELITE premium — as four bare `const int`s, so L6's bridge to the ladder of
        // record could not be constructed across it: 0 of 96 chunks reproduced, and the bisect put
        // the break on exactly that milestone with L5's own base commit passing as a control.
        // =0 restores all four pre-wave prices AS A SET (Run.SetForkPrices; partially undoing them
        // yields an economy that never shipped). It does NOT repair that bridge — L5's worlds are
        // gone — it makes a FUTURE round able to isolate the wave. Never a shipping configuration:
        // with it off, FORKTEST leg (A) fails by design and SAVETEST's map fingerprints move.
        string forkPricesEnv = Environment.GetEnvironmentVariable("SIGHTLINE_FORKPRICES");
        if (forkPricesEnv == "0") Run.SetForkPrices(false);
        else if (forkPricesEnv == "1") Run.SetForkPrices(true);
        // P22 "NOTHING WITHOUT A SWITCH" — SIGHTLINE_HEALFIRST=0/1, the OTHER half of THE FORK
        // PAYS, which P21 named and could not cover. =1 restores the pre-milestone-5 ORDERING of
        // the SUPPLY/RECON card's full squad heal: it lands BEFORE Run.DebriefSurvivors' fresh-wound
        // gauge, so nobody who ends a cleared SUPPLY node on their feet can be wounded by it. Live,
        // not latent — SUPPLY was 828 of 5,413 played nodes (15.3%) in P21's own census.
        // A SEPARATE dial from SIGHTLINE_FORKPRICES on purpose: the prices move the routing ECONOMY
        // and the map fingerprints, this moves squad ATTRITION, and a round may want one alone.
        // TO RESTORE MILESTONE 4 WHOLE, SET BOTH: SIGHTLINE_FORKPRICES=0 SIGHTLINE_HEALFIRST=1.
        // Never a shipping configuration: with it on, FORKTEST leg (C) fails by design (the subsidy
        // is back). Neither flag repairs L6's bridge — L5's worlds are gone.
        string healFirstEnv = Environment.GetEnvironmentVariable("SIGHTLINE_HEALFIRST");
        if (healFirstEnv == "1") Run.SupplyHealFirst = true;
        else if (healFirstEnv == "0") Run.SupplyHealFirst = false;
        // ---- P18 "THE SECOND AXIS" — the wave's three off switches (house pattern: =0 restores
        // the pre-P18 behaviour EXACTLY, so every change is attributable). ----
        // SIGHTLINE_ASSISTLATCH=0/1: the adaptive assist reads the heat the run STARTED at instead
        // of its live heat, so a mid-run AddHeat field event can no longer silently revoke it.
        string assistLatchEnv = Environment.GetEnvironmentVariable("SIGHTLINE_ASSISTLATCH");
        if (assistLatchEnv == "1") Run.AssistLatch = true; else if (assistLatchEnv == "0") Run.AssistLatch = false;
        // SIGHTLINE_PERKPICK=0/1: a BONUS perk (ELITE/ONSLAUGHT reward, ADV. TRAINING, the field
        // event) lets the player choose its RECIPIENT instead of landing on a random survivor.
        string perkPickEnv = Environment.GetEnvironmentVariable("SIGHTLINE_PERKPICK");
        if (perkPickEnv == "1") Run.BonusPerkPick = true; else if (perkPickEnv == "0") Run.BonusPerkPick = false;
        // SIGHTLINE_SECONDAXIS=0/1: the WAR ROOM's HEAT-GATED second column (COMBAT TRIALS / DEEP
        // RESERVE / DEEP STORES). =0 hides all three and applies none of them, which is the pre-P18
        // WAR ROOM exactly. Every read is !NoPersist-gated, so a batch never sees it either way.
        string secondAxisEnv = Environment.GetEnvironmentVariable("SIGHTLINE_SECONDAXIS");
        if (secondAxisEnv == "1") MetaProg.SecondAxis = true; else if (secondAxisEnv == "0") MetaProg.SecondAxis = false;

        bool smartplay = Environment.GetEnvironmentVariable("SIGHTLINE_SMARTPLAY") == "1";
        bool autoplay = Environment.GetEnvironmentVariable("SIGHTLINE_AUTOPLAY") == "1" || smartplay;

        // SIGHTLINE_BALANCE=<N> : run N full headless campaigns with the competent AI, aggregate
        // balance telemetry (Stats), and print Stats.Report(). A measurement harness — takes over
        // completely when set; leaves AUTOPLAY/SHOT/the *TEST modes untouched when unset.
        // P15: a NON-EMPTY value that does not parse is a typo, not "off". Falling through to the
        // interactive game meant the chunk runner's watch loop waited out its whole timeout on a
        // batch that had never started. Refuse at the door (RefuseBatch: name it, write nothing,
        // exit 3). An ABSENT/empty value is still "not a batch" and falls through as before.
        string balanceRaw = Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE");
        if (Stats.ReadIntEnv(balanceRaw, out int balanceN) == Stats.EnvRead.Bad)
            RefuseBatch($"SIGHTLINE_BALANCE='{balanceRaw}' is not an integer");
        if (balanceN > 0)
        {
            BalanceBatch(balanceN);
            return;
        }

        // APEX W4: SIGHTLINE_BALANCE_ENDLESS=<N> : the SAME flywheel pointed at LAST STAND — N
        // endless stands (greedy+sloppy paired, heats cycled/pinned exactly like SIGHTLINE_BALANCE)
        // through the existing BeginEndless entry; the report adds wave-depth mean/median/p90.
        // Checked after SIGHTLINE_BALANCE, so a plain campaign batch is unchanged.
        string endlessRaw = Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE_ENDLESS");
        if (Stats.ReadIntEnv(endlessRaw, out int endlessBatchN) == Stats.EnvRead.Bad)
            RefuseBatch($"SIGHTLINE_BALANCE_ENDLESS='{endlessRaw}' is not an integer");
        if (endlessBatchN > 0)
        {
            BalanceBatch(endlessBatchN, endless: true);
            return;
        }

        // SIGHTLINE_DAILYSIGPROBE=1 : P14 — print ONE line, this process's DAILY SIGNATURE
        // (stamp|faction|force|board), and exit. It is the CHILD half of MODETEST leg (11): the
        // daily's headline contract is "the same stamp fields the same force", and a same-process
        // check cannot see anything a fresh process would compute differently, which is the entire
        // failure mode a date-seeded challenge has. A report, not an assertion — MODETEST compares.
        //
        // IT IS THE FIRST HOOK BRANCH IN THIS METHOD, DELIBERATELY. The child inherits the parent's
        // whole environment so that every measurement dial parsed ABOVE this line (MODEDEPTH,
        // OPENERTRIM, PODUNIFORM, BIOMEMECH, ...) applies to it exactly as it applies to the parent
        // — the two processes have to be the same game or the comparison means nothing. Being first
        // is what keeps the inherited SIGHTLINE_MODETEST (or any other hook) from preempting it.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_DAILYSIGPROBE") == "1")
        {
            Raylib.InitWindow(64, 64, "dailysig");
            Console.WriteLine("DAILYSIG:" + new Game().DailySignatureLine());
            Raylib.CloseWindow();
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

        // W2 THE OPPONENT ACTS: SIGHTLINE_AIIDLETEST=1 : the NO-IDLE-ENEMY-TURN invariant. Drives
        // real missions across all 8 objectives at heat 0/4 TWICE on the same seeds — once with
        // SIGHTLINE_AIIDLEFIX off, once on — and asserts the OFF leg still idles (the probe cannot
        // pass vacuously, and the pre-wave rate is printed) while the ON leg idles exactly zero
        // times and never leaves a dry weapon holding an action. SIGHTLINE_AIIDLETEST=<N> widens
        // the sample. See docs/DEVLOG.md §W2.
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_AIIDLETEST"), out int idleN) && idleN > 0)
        {
            AiIdleTest(idleN);
            return;
        }

        // P16 GROUND TRUTH: SIGHTLINE_RIFTTEST=1 : the VOID chasm's BLOCKER gate — a rift may not
        // strand anything. Builds every objective x mission x seed with the biome pinned to VOID and
        // asserts the DIFFERENTIAL (the squad's reachable set shrinks by exactly the rift tiles,
        // which also covers spawns that do not exist yet) plus every named fixture. =<N> widens the
        // per-cell seed count (default 6 -> ~576 boards).
        // P26 THE ARENA OWNS THE FIGHT: SIGHTLINE_ARENASITETEST=1 : the site-glyph layer's gate.
        // Asserts all 35 templates are 11x18 and well-formed, that Maps.AnySiteTemplates agrees with
        // a direct scan (the inertness precondition that stops PlanBoard double-spending the arena
        // gate roll), that the parser honours row-major scan order (the LAST 'P' is the VIP seat)
        // and rejects every illegal cardinality, and that a sealed-in site is detectable. Leg (E) —
        // "authored terrain SURVIVES at an arena-declared site" — is the only one that can fail if
        // the wave ships as a no-op, and it announces itself as SKIPPED until a template declares.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_ARENASITETEST") == "1")
        {
            Console.WriteLine(Game.ArenaSiteSelfTest());
            return;
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_RIFTTEST"), out int riftN) && riftN > 0)
        {
            Console.WriteLine(Game.RiftSelfTest(riftN == 1 ? 6 : riftN));
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

        // C6 SHIPS LIKE A PRODUCT: SIGHTLINE_SHIPTEST=1 — the DISTRIBUTABLE's contract, not the
        // game model's. The bundled-file manifest resolved STRICTLY next to the binary (the cwd
        // fallback that hid RESONANCE F1's lost font is explicitly not allowed to carry it), the
        // licence obligations present AND non-empty AND naming every redistributed component, the
        // player-data directory rooted, a profile written and read back off disk, all three
        // player-data writers proven atomic by an open-handle inode probe, every persisted DTO
        // reachable from a source-generated JSON context, and the build stamped and painted.
        // Runs against the PUBLISHED binary too (scripts/publish.sh invokes it there, which is the
        // only place the manifest leg is testing the artifact a player receives).
        // Tiny window: leg (4) builds a real Run, whose Unit ctors do tile math.
        //
        // DISPATCH-ORDER QUIRK, noted rather than "fixed" (C6 review): SIGHTLINE_BALANCE is handled
        // EARLIER in this method, so `SIGHTLINE_SHIPTEST=1 SIGHTLINE_BALANCE=5` silently runs a
        // balance batch and prints no SHIPTEST line at all. That is the house convention here —
        // first matching branch wins, and every hook in this file behaves that way — so reordering
        // for one test would be the surprise, not the fix. Set one mode at a time. (The one place
        // this matters is a script that greps for a PASS line: a missing line is a mode collision,
        // not a crash. qa-sweep.sh's `verdict` already treats a blank capture as a FAILURE.)
        if (Environment.GetEnvironmentVariable("SIGHTLINE_SHIPTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "shiptest");
            Console.WriteLine(Ship.SelfTest());
            Raylib.CloseWindow();
            return;
        }

        // P17 SHIPS AS v1.0.0 — SIGHTLINE_ICONSHOT=1: write the generated window icon out as a PNG
        // so a human can LOOK at it. The icon itself is unobservable from here (no window manager
        // under Xvfb, and GLFW ignores window icons on Wayland by design), so SHIPTEST asserts the
        // PIXELS and this hook photographs them. A *SHOT hook, like SIGHTLINE_SHOT / PODSHOT /
        // BIOMESHOT — it prints a path, never a verdict, so it is correctly outside qa-sweep.sh's
        // TEST|GATE|PROBE coverage alphabet. Writes 64x64 (true size) plus a 256x256
        // nearest-neighbour blow-up, which is the one that shows whether the mark is crisp.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_ICONSHOT") == "1")
        {
            Raylib.InitWindow(64, 64, "iconshot");
            var px = Ship.IconPixels();
            var icon = Raylib.GenImageColor(Ship.IconSize, Ship.IconSize, Pal.Bg);
            for (int y = 0; y < Ship.IconSize; y++)
                for (int x = 0; x < Ship.IconSize; x++)
                    Raylib.ImageDrawPixel(ref icon, x, y, px[y * Ship.IconSize + x]);
            Raylib.ExportImage(icon, "sightline_icon.png");
            Raylib.ImageResizeNN(ref icon, 256, 256);
            Raylib.ExportImage(icon, "sightline_icon_256.png");
            Raylib.UnloadImage(icon);
            Console.WriteLine("ICONSHOT: sightline_icon.png (64x64) + sightline_icon_256.png");
            Raylib.CloseWindow();
            return;
        }

        // PARALLAX P11 "THE CRASH FILE" — SIGHTLINE_CRASHTEST=1: the crash reporter's own contract.
        // Same family as SHIPTEST (both are about the artifact a player receives rather than the
        // game model), and the only self-test in this project that deliberately THROWS.
        //
        // It stages a REAL GAME first and publishes it as Crash.Live, exactly the way the normal
        // launch below does, because the half of the report that is worth anything is the half
        // that says what was happening — and a test with no game running would assert the empty
        // shape and pass over a state block that never worked. Tiny window: `new Game()` +
        // StartMission do tile math. NoPersist is set BEFORE StartMission so the staging cannot
        // touch a real profile (house rule), and the report itself goes to a temp directory —
        // see CrashTest.SelfTest's isolation note.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_CRASHTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "crashtest");
            var cg = new Game { NoPersist = true };
            cg.StartMission(1);
            Crash.Live = cg;
            Console.WriteLine(CrashTest.SelfTest());
            Crash.Live = null;
            Raylib.CloseWindow();
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
        // SIGHTLINE_BRIEFTEST=1 : W5 THE FIRST HOUR — drives a LIVE (non-NoPersist) first-ever
        // campaign mission 1 and asserts the briefing card actually plays before the lesson strip
        // opens. This is the ONE self-test that deliberately runs the persisting path (every other
        // harness hook sets NoPersist, and NoPersist is exactly what hid this defect), so it
        // stashes and restores display.json / save.json / meta.json around its body.
        // SIGHTLINE_BRIEFFIRST=0 restores the pre-W5 ordering and turns this test red.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_BRIEFTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "brieftest");   // StartMission -> Unit.SyncPos uses tile math
            LoadGameFonts();   // W5-FIX: the test now DRAWS the real HUD, so it needs the atlases
            Console.WriteLine(new Game().BriefingSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_BACKDROPTEST=1 : W5-FIX — the backdrop registry. Drives EVERY Phase through the
        // chrome pass and through DrawBackdropLayer and asserts (a) no screen builder paints a
        // full-screen backdrop from the chrome pass (the AUDIO CHECK defect), (b) the registry and
        // the switch are the same set, (c) the modal scrim doubles only when the composite runs.
        // Draws the real HUD, so it needs a context + the real font atlases.
        // SIGHTLINE_AUDBACKDROP=1 restores the defect and turns this red.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_BACKDROPTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "backdroptest");
            LoadGameFonts();
            Console.WriteLine(new Game().BackdropSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_CONTRASTTEST=1 : W5 THE FIRST HOUR — main-menu type contrast through the
        // shipped post-FX composite. Boots a REAL 1280x800 window with Display + PostFX ON and
        // reads the framebuffer back; a screen read is the only honest instrument, because the
        // whole defect lived in the composite. Stashes/restores save.json (it stages a CONTINUE).
        // SIGHTLINE_QUITTEST=1 : W5 THE FIRST HOUR — the two exits the audit found missing.
        // Drives the LIVE (persisting) path to prove the quit is persistence-inert, so it stashes
        // and restores save.json / meta.json. Draws the end cards, so it needs a context + fonts.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_QUITTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "quittest");
            LoadGameFonts();
            Console.WriteLine(new Game().QuitSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_KEYTABLE=1 : THE FRONT DOOR — print README's controls tables (markdown) from
        // Hud.VerbTable + Hud.KeyTable + Hud.IntroDoors / Game.IntroKeys. HAND-RUN, not a test: it
        // prints no verdict, and its name deliberately ends in neither TEST nor GATE so the sweep's
        // COVERAGE GUARD does not count it as an unrun self-test. Paste the output over the block
        // between the `KEYTABLE:BEGIN` / `KEYTABLE:END` markers in README.md.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_KEYTABLE") == "1")
        {
            Console.Write(Hud.KeyTableMarkdown());
            return;
        }
        // SIGHTLINE_TEMPLATEGATE=1 : P28 — every hand-authored arena still fits the board. A
        // wrong-sized template is refused by Mission.TryApplyLayout with the same `return false`
        // as a connectivity rejection, so without this a board-size change orphans all 35 arenas
        // in total silence. Needs no window: it only measures string lengths.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_TEMPLATEGATE") == "1")
        {
            Console.WriteLine(Game.TemplateGate());
            return;
        }
        // SIGHTLINE_KEYTABLEGATE=1 : P13 — the generator above had no gate, and the table it reads
        // from had no gate either. Asserts (a) README's KEYTABLE block is byte-identical to what
        // the generator prints, and (b) every key src/Game.Audition.cs actually reads is named in
        // KeyTable's AUDIO CHECK row. Reads the repo's source; run it from the source tree.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_KEYTABLEGATE") == "1")
        {
            Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
            Raylib.InitWindow(64, 64, "keytablegate");
            LoadGameFonts();
            Console.WriteLine(Game.KeyTableGate());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_SETTINGSTEST=1 : SETTINGS EVERYWHERE — the settings card is reachable from the
        // INTRO and the BARRACKS, not just a fight (ROADMAP "Left open by C5"). Draws the intro and
        // the card to publish their rects, so it needs a context + fonts; stashes display.json.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_SETTINGSTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "settingstest");
            LoadGameFonts();
            Console.WriteLine(new Game().SettingsSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_CHROMETEST=1 : W5 THE FIRST HOUR — the action bar's fixed slot map, the
        // CONCEALED pill's pulse envelope, and the doctrine cards fitting their own text.
        // Needs a real draw context (the bar's layout and its paint are one pass), so it runs
        // inside a tiny window and draws into it.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_CHROMETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "chrometest");
            // LOAD-BEARING: this test measures TEXT (button widths, wrapped line counts), and
            // without the real atlases Cfg.Measure falls back to raylib's default font, whose
            // metrics are narrower — every doctrine description fitted on one line and the
            // overflow leg silently could not fail.
            LoadGameFonts();
            Console.WriteLine(new Game().ChromeSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_FITTEST=1 : wave THE FIT — the whole shipped TEXT SIZE range is a tested
        // surface. Asserts that no string on the five screens this wave touched is painted outside
        // its own chrome, or into another string's pixels, at ANY of the four scales the pause
        // menu can select. Pure measurement, but it MUST have the real atlases loaded for the same
        // reason CHROMETEST does — raylib's default face is narrower and every overflow vanishes.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_FITTEST") == "1")
        {
            // C5 THE HARD EDGES: leg (F) DRAWS every screen the game can put up, so this needs the
            // FULL-SIZE window (the layouts are authored against Cfg.ScreenW/H) plus Display and
            // Audio, exactly like BOARDTEST — the other self-test that runs the shipped draw path.
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
            Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "fittest");
            // C5: leg (F) DRAWS. Without a display that is a crash deep in raylib rather than an
            // answer, so refuse the same way every other drawing self-test does (exit 2).
            RequireWindow("FITTEST");
            Raylib.SetExitKey(KeyboardKey.Null);
            LoadGameFonts();
            Display.Init(false);          // post-FX OFF: this leg measures geometry, not bloom
            Raylib.SetTargetFPS(0);
            Audio.Init();                 // staging a screen can pop a cue; the device may be absent
            Console.WriteLine(Game.FitSelfTest());
            Display.Shutdown();
            Audio.Shutdown();
            Renderer.UnloadNoise();
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_CLASSTEST=1 : C3 THE TWO GAMES — the objective-CLASS gate. Model + DRAW +
        // lever; see Game.ClassSelfTest. Needs the FULL-SIZE window and the real atlases: it
        // paints the actual barracks/campaign-map frame and reads the strings and class marks at
        // the draw call, and the map's layout is derived from Cfg.ScreenW/H.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_CLASSTEST") == "1")
        {
            Raylib.SetTraceLogLevel(TraceLogLevel.Error);
            Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "sightline-classtest");
            Raylib.SetExitKey(KeyboardKey.Null);
            LoadGameFonts();
            Console.WriteLine(new Game { NoPersist = true }.ClassSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_REWARDTEST=1 : P18 THE SECOND AXIS — WHO gets a bonus perk and how wide the
        // offer is (Game.RewardSelfTest). Pure model: no window, no disk.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_REWARDTEST") == "1")
        {
            Console.WriteLine(Game.RewardSelfTest());
            return;
        }
        if (Environment.GetEnvironmentVariable("SIGHTLINE_CONTRASTTEST") == "1")
        {
            Console.WriteLine(ContrastSelfTest());
            return;
        }
        // SIGHTLINE_BUILDINGTEST=1 : P28 — buildings on REAL boards through the REAL Build:
        // they appear, they never strand a tile, and they leave the tile board untouched.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_BUILDINGTEST") == "1")
        {
            Console.WriteLine(Mission.BuildingSelfTest());
            return;
        }
        // SIGHTLINE_BOARDSIZETEST=1 : P29 — the board is a runtime size now. Asserts the SHIPPED
        // default did not drift and that the camera's pan bounds follow the board.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_BOARDSIZETEST") == "1")
        {
            Console.WriteLine(BoardSize.SelfTest());
            return;
        }
        // SIGHTLINE_VISIONTEST=1 : P30 — the DISCOVERY layer. What HQ knows vs what is there.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_VISIONTEST") == "1")
        {
            Console.WriteLine(Vision.SelfTest());
            return;
        }
        // SIGHTLINE_PICKTEST=1 : P32 — the projected view's INPUT path. Projects every tile to a
        // screen pixel and picks it back, at four camera angles. Needs a window (GetWorldToScreen
        // reads the live framebuffer size).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_PICKTEST") == "1")
        {
            Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
            Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "picktest");
            Console.WriteLine(View3D.PickSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_FXBRIDGETEST=1 : P34 — the board-pixel -> projected-screen bridge that carries
        // the whole Fx/Anim layer into the 3D view. Draws through the real rlgl matrix stack and
        // reads the framebuffer back, so it needs a window AND a drawn frame.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_FXBRIDGETEST") == "1")
        {
            Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
            Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "fxbridgetest");
            Console.WriteLine(View3D.FxBridgeSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_EDGETEST=1 : P28 — the EDGE layer's contract (a wall lives on the boundary
        // between two tiles, consumes no floor, and is directional). Pure grid logic, no window.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_EDGETEST") == "1")
        {
            Console.WriteLine(Edges.SelfTest());
            return;
        }
        if (Environment.GetEnvironmentVariable("SIGHTLINE_COMBATTEST") == "1")
        {
            Console.WriteLine(Combat.SelfTest());
            return;
        }
        // SIGHTLINE_TRUTHTEST=1 : W9 — "what the UI says is what the dice do". TWO halves, and the
        // second is the one the review sent this wave back for:
        //   MATH  (Combat.TruthFails)        — the effective band, the graze agreement, the shared
        //                                      LockOn predicate, and ComputeOdds/ExpectedDamage purity.
        //   UI    (Game.TooltipTruthFails)   — drives the REAL hover/aim path, RENDERS the REAL
        //                                      tooltip, and asserts on the strings it PAINTS
        //                                      (captured at the draw call). The math half alone still
        //                                      passed with Hud.DrawTooltip reverted to the raw DMG
        //                                      band and the stale LOCK-ON predicate: it re-derived the
        //                                      right answer instead of observing the panel.
        // Needs a real window (it draws).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_TRUTHTEST") == "1")
        {
            Raylib.SetTraceLogLevel(TraceLogLevel.Error);
            Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "sightline-truthtest");
            Raylib.SetExitKey(KeyboardKey.Null);
            Cfg.Font = Raylib.GetFontDefault();
            LoadGameFonts();      // C5: the perk-card leg reads PAINTED strings; the default face
                                  // measures narrower and its own draw path must be the real one.
            string mathFails = Combat.TruthFails();
            string uiFails = new Game().TooltipTruthFails();
            // C5: the PERK CHOOSER's fourteen "before > after" lines, measured against the shipped
            // code path for each perk rather than re-derived from the constants they print.
            string perkFails = new Game().PerkCardTruthFails();
            Raylib.CloseWindow();
            string all = string.Join(",", System.Linq.Enumerable.Where(new[] { mathFails, uiFails, perkFails }, x => !string.IsNullOrEmpty(x)));
            Console.WriteLine(all.Length == 0
                ? "TRUTHTEST: PASS (UI-OBSERVED: the tooltip's PAINTED DMG row equals the damage Resolve "
                  + "deals to that same defender on a plain foe AND a guarded HVT, moves when the defender "
                  + "does, and agrees with the GRAZE row beneath it; the PAINTED LOCK-ON badge appears iff "
                  + "the perk moved the hit% and shows that exact delta; no tooltip string is painted below "
                  + "12px. MATH: the raw band stays raw for ExpectedDamage/threat; armor moves the shown "
                  + "band; ComputeOdds + ExpectedDamage are side-effect free while Resolve still telegraphs. "
                  + "PERK CARD (C5): every painted before>after line agrees with the shipped path that "
                  + "grants it — ApplyPerk for the stat bumps, a real mission refill for BANDOLIER, "
                  + "ComputeOdds for the aim/crit perks, HardenedReduce for the damage cuts)"
                : "TRUTHTEST: FAIL (" + all + ")");
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
        // THE CUE MAP (wave "cue-map"): SIGHTLINE_CUETEST=1 — the event->cue table is INJECTIVE,
        // no opponent telegraph resolves to a UI-bus cue, the real ShowBanner puts an enemy banner
        // on the SFX fader, and the src/Game.cs call-site census is under its caps. Needs a tiny
        // window (Game's ctor uses tile math); device-free otherwise.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_CUETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "cuetest");
            Console.WriteLine(new Game().CueSelfTest());
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
        // SIGHTLINE_HVTTEST=1 : RESONANCE W8 — DECAPITATE's punch-through target (Game.DesignateHvt):
        // the selection rule, the ELITE exemption AND its mid-run counterpart, the buff's exact
        // magnitude at every depth, the shipped defaults, and that the new dials are not no-ops.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_HVTTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "hvttest");   // SetupMission uses tile math
            Console.WriteLine(new Game().HvtSelfTest());
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
        // SIGHTLINE_MIDTOOTHTEST=1 : CONTOUR C1 — the mid-ladder tooth (Heat.MidTooth): no DEAD
        // AiTier declaration and no SILENT rung on the shipped table, the dial's apex neutrality
        // across all eight modes, MIDTOOTH=0 as a field-for-field control, the per-mode rung
        // shapes, and the +1 damage arriving on every heat-6 m3 hostile (none at heat 5).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_MIDTOOTHTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "midtoothtest");   // SetupMission uses tile math
            Console.WriteLine(new Game().MidToothSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_FORCETEST=1 : PARALLAX P23 "THE APEX BITES" — THE FORCE THE BOARD ACTUALLY
        // BUILDS. HEATLADDERTEST and MIDTOOTHTEST pin the heat table's cumulative and per-rung
        // vectors, and both were green while the apex rung's body and stat were being clamped and
        // discarded one level up in Mission.SpawnEnemies (L7). This asks the other end of the pipe:
        // per mission x rung, does the declared body/stat reach the board, is the opener still
        // flat, is every hostile seated on a distinct reachable tile, and are the two P23 dials
        // real and independent. SIGHTLINE_FORCEDUMP=1 prints the whole matrix.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_FORCETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "forcetest");   // SetupMission uses tile math
            Console.WriteLine(new Game().ForceSelfTest());
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
        // SIGHTLINE_FORKTEST=1 : THE FORK PAYS — the campaign fork's ECONOMY gate (kind ordering,
        // the PITCHED class premium and its printing, the Event hover's honesty, and the SUPPLY
        // heal no longer eating the fresh-wound gauge). Window-free: bare Run maps + pure Hud
        // string composition, no GL context and no mission build.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_FORKTEST") == "1")
        {
            Console.WriteLine(Game.ForkSelfTest());
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
        // SIGHTLINE_LANETEST=1 : P10 THE HELD LANE — the ordinary enemy overwatch's cone. Arms the
        // player's own OwFocused flag set with an axis the planner chose (Ai.ChooseLane); the cone
        // covers approach ground; the red wash / Threat[].Watched predicate (Game.WatchCovers)
        // agrees TILE FOR TILE with the real OnUnitEnteredTile reaction in both directions; the
        // marked fraction of the board collapses with lane selection on. It reads the AMBIENT dial,
        // so `SIGHTLINE_AILANE=0 SIGHTLINE_LANETEST=1` FAILS — that is the proof it can.
        // Walks real campaigns for its (b3)/(e) legs, so it needs the full window (Renderer's
        // tile->px math and the headless frame pump).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_LANETEST") == "1")
        {
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
            Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "lanetest");
            RequireWindow("LANETEST");
            Raylib.SetExitKey(KeyboardKey.Null);
            Cfg.Font = Raylib.GetFontDefault();
            Display.Init(false);
            Raylib.SetTargetFPS(0);
            Console.WriteLine(new Game().LaneSelfTest());
            Display.Shutdown();
            Renderer.UnloadNoise();
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_ROSTERTEST=1 : P19 THE ROSTER CONTESTS — (a) the named mid-boss belongs to the
        // campaign map's ELITE NODE rather than to a mission number (Mission.MidBossFor), proven on
        // real built forces, with the mission-1 opener asserted identical across the dial and every
        // enumerated route asserted to still meet one; (b) the SMG monoculture's three range bands
        // (Weapon.SmgProfile), proven to reach the built force, to leave the player half alone, and
        // to FLIP the hit% ordering of two archetypes that used to differ by a constant.
        // It reads the AMBIENT dials, so `SIGHTLINE_ELITEBOSS=0 SIGHTLINE_ROSTERTEST=1` and
        // `SIGHTLINE_ROSTERID=0 SIGHTLINE_ROSTERTEST=1` both FAIL — that is the proof they can.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_ROSTERTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "rostertest");
            Console.WriteLine(new Game().RosterSelfTest());
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
        // SIGHTLINE_BIOMETEST=1 : C4 "EIGHT BIOMES ARE PAINT" — the biome GROUND layer. Pins the
        // MECHANIC'S EFFECT (cover level, hit%, Dijkstra cost, line-of-sight verdict, HP, the AI's
        // chosen destination) on constructed boards, not the presence of a field. Reads the ambient
        // Terrain.Enabled on purpose, so SIGHTLINE_BIOMEMECH=0 makes it FAIL.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_BIOMETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "biometest");   // Unit.SyncPos + Ai.Plan use tile->px math
            Console.WriteLine(new Game().BiomeSelfTest());
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
        // SIGHTLINE_DECLINETEST=1 : C2 THE OPPONENT DECLINES. Ground-truths Combat.AsIfExposed
        // against a physically uncovered board, pins Ai.ShotTileValue on both sides of the
        // SIGHTLINE_AIDECLINE dial (the pre-C2 branch is the literal `100 + bestHit` constant),
        // and drives the decline gate on a live Ai.Plan. It reads the AMBIENT dial, so
        // `SIGHTLINE_AIDECLINE=0 SIGHTLINE_DECLINETEST=1` FAILS — that is the proof it can.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_DECLINETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "declinetest");   // Unit.SyncPos uses tile->px math
            Console.WriteLine(new Game().DeclineSelfTest());
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
        // SIGHTLINE_STALLTEST=1 : W9 THE REPAIR — the autopilot's "never a RESULT: TIMEOUT" contract,
        // asserted instead of asserted-in-a-comment. Run-scoped turn counter, its force-lose arm, the
        // turn-cap-vs-frame-cap arithmetic, and the measured DEFEND/disoriented within-turn deadlock.
        // SIGHTLINE_SAVEEDGETEST=1 : C5 THE HARD EDGES — the HOSTILE SAVE. W9 asked this of
        // meta.json and found three killers; save.json had never been asked. Drives eight edited /
        // truncated / older-build save shapes through the real resume path and then plays and
        // draws them. Needs a window: it draws a frame per shape.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_SAVEEDGETEST") == "1")
        {
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
            Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "saveedgetest");
            RequireWindow("SAVEEDGETEST");
            Raylib.SetExitKey(KeyboardKey.Null);
            LoadGameFonts();
            Display.Init(false);
            Raylib.SetTargetFPS(0);
            Console.WriteLine(Game.SaveEdgeSelfTest());
            Display.Shutdown();
            Renderer.UnloadNoise();
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_AICOVTEST=<N> : C5 THE HARD EDGES — the ENEMY DECISION CENSUS. Walks N
        // campaigns per (heat x objective) cell and asserts every branch of the enemy exec chain
        // is REACHED at least once; the effectively-dead branches are waived BY NAME through
        // Game.AiCovKnownRare and every branch's count is printed on every run. (`overwatch` was
        // on that list at C5 and is NOT any more — C2's decline gate revived it, and P10 THE HELD
        // LANE re-measured it; the PASS line carries today's rate.) SIGHTLINE_AICOVSTRICT=1 drops the waiver (and fails on
        // this tree, which is the proof the gate can fail). Needs a window: it drives real play.
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_AICOVTEST"), out int covN) && covN > 0)
        {
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
            Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "aicovtest");
            RequireWindow("AICOVTEST");
            Raylib.SetExitKey(KeyboardKey.Null);
            Cfg.Font = Raylib.GetFontDefault();
            Display.Init(false);
            Raylib.SetTargetFPS(0);
            Console.WriteLine(Game.AiCoverageSelfTest(covN));
            Display.Shutdown();
            Renderer.UnloadNoise();
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_ENEMYSTALLTEST=1 : C5 THE HARD EDGES — the ENEMY-turn deadlock guard. W9's
        // idle guard covers the player turn; this one wedges a real enemy turn and asserts the
        // guard names the stalled unit, ends the turn, stays silent in clean play, and that the
        // SAME wedge with the guard off still hangs. Needs a window: it drives real missions.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_ENEMYSTALLTEST") == "1")
        {
            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
            Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "enemystalltest");
            RequireWindow("ENEMYSTALLTEST");
            Raylib.SetExitKey(KeyboardKey.Null);
            Cfg.Font = Raylib.GetFontDefault();
            Display.Init(false);
            Raylib.SetTargetFPS(0);
            Console.WriteLine(Game.EnemyStallSelfTest());
            Display.Shutdown();
            Renderer.UnloadNoise();
            Raylib.CloseWindow();
            return;
        }
        if (Environment.GetEnvironmentVariable("SIGHTLINE_STALLTEST") == "1")
        {
            Raylib.SetTraceLogLevel(TraceLogLevel.Error);
            Raylib.InitWindow(64, 64, "stalltest");
            Console.WriteLine(new Game().StallSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_HEATPINTEST=1 : wave THE HEAT PIN AND L5 — the balance INSTRUMENT's three new
        // contracts: EventCatalog.HeatPinned nulls the three heat-raising field-event arms (and
        // they DO raise heat with it off), RunRec.HeatEnd / RunTurns / campaigns[] / heatLeak land
        // in the JSON, and the autopilot's STALEMATE guard names its arm. Same 64x64 window as
        // STALLTEST (the STALEMATE leg starts a real mission).
        if (Environment.GetEnvironmentVariable("SIGHTLINE_HEATPINTEST") == "1")
        {
            Raylib.SetTraceLogLevel(TraceLogLevel.Error);
            Raylib.InitWindow(64, 64, "heatpintest");
            Console.WriteLine(Game.HeatPinSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_INSTRUMENTTEST=1 : PARALLAX P15 "THE UNVERIFIED" — the four places the BALANCE
        // INSTRUMENT reported a number where it had none: a silently-defaulted _HEAT/_BASE (so a
        // typo'd chunk was archived under a rung it never measured), a `runWinRate` of 0.0 for a
        // batch with no campaign runs, the checkpoint redeploy ERASING the mission it retries, and
        // the LAST STAND harness stop logged as a genuine wipe. Needs a window (legs D and E start
        // real missions / stands), like HEATPINTEST.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_INSTRUMENTTEST") == "1")
        {
            Raylib.SetTraceLogLevel(TraceLogLevel.Error);
            Raylib.InitWindow(64, 64, "instrumenttest");
            Console.WriteLine(InstrumentTest.SelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_GRAPPLETEST=1 : W9 THE REPAIR — the assault GRAPPLE verb, which had ZERO coverage
        // (its two siblings SHOVE and DRAG were both pinned). Reach-2 pull, the adjacent SLAM, and the
        // invariant that a soldier NEVER takes damage from its own grapple — incl. as a JUGGERNAUT,
        // whose reach-1 fork makes the adjacent case 100% of its grapples.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_GRAPPLETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "grappletest");   // Unit.SyncPos + ShoveAnim use tile->px math
            Console.WriteLine(new Game().GrappleSelfTest());
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
        // SIGHTLINE_FEELTEST=1 : THE STRIDE — pillar 2 probe: a 6-tile walk's per-frame speed profile (no
        // caterpillar), the VAULT arc (lifts over the cover, lands on the centre), floating-text separation.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_FEELTEST") == "1")
        {
            Raylib.InitWindow(64, 64, "feeltest");   // Unit.SyncPos + MoveStepAnim use tile->px math
            Console.WriteLine(new Game().FeelSelfTest());
            Raylib.CloseWindow();
            return;
        }
        // SIGHTLINE_JUICETEST=1 : P25 "NOBODY HAS LOOKED" — pillar 2's SECOND instrument. FEELTEST
        // measures the TWEEN; this measures the ANSWER: the feedback footprint (seen / heard / felt /
        // read) of every shot outcome, every action-bar verb, and every route by which a unit takes
        // damage or dies. Read the "WHAT IT CANNOT SEE" block above Game.JuiceSelfTest before quoting it.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_JUICETEST") == "1")
        {
            Raylib.InitWindow(64, 64, "juicetest");   // Unit.SyncPos + every anim use tile->px math
            Console.WriteLine(new Game().JuiceSelfTest());
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
        // SIGHTLINE_MODEFORCEPROBE=1 : P14 — a REPORT of the force a SKIRMISH/DAILY actually fields
        // per heat rung (headcount, pods, roster, the escort asset, the HVT, the mid-boss kit, one
        // DEFEND wave). Asserts nothing — MODETEST owns the assertions; this is the before/after
        // evidence a change to the single-mission modes has to show.
        if (Environment.GetEnvironmentVariable("SIGHTLINE_MODEFORCEPROBE") == "1")
        {
            Raylib.InitWindow(64, 64, "modeforceprobe");
            Console.WriteLine(new Game().ModeForceProbe());
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
        // P17 SHIPS AS v1.0.0 — the window icon, generated in engine from the palette (no committed
        // binary, no licence entry). Only on the real launch path: the harness windows are 64x64
        // scratch contexts nobody looks at, and an icon they never show is pure startup cost.
        Ship.ApplyWindowIcon();
        // P17 — THE FIRST-LAUNCH WINDOW FIT, and the ONE place it is ever enabled. Display.Init
        // then asks the monitor whether the authored 1280x800 fits and shrinks (never enlarges) if
        // it does not; see the block comment on Display.FitLaunchSize. Every headless path leaves
        // this false, so SIGHTLINE_SHOT / AUTOPLAY / PAIRTEST / BALANCE keep exactly the window
        // InitWindow just made, and window size stays independent of the machine.
        Display.AllowLaunchFit = !(shot || autoplay);

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
        // P11 THE CRASH FILE: publish the live game so a crash report can say what was happening
        // (mode / phase / mission / heat / turn / roster). Read defensively by Crash.Compose —
        // at crash time this object is by definition in a state nobody designed.
        Crash.Live = game;
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
        // C6 SHIPS LIKE A PRODUCT — SIGHTLINE_COLD=1: photograph the FIRST-EVER-LAUNCH state of a
        // profile-driven screen instead of the staged demo one. Every screenshot hook in this
        // project stages a rich profile (SIGHTLINE_INTRO fabricates a mid-campaign save so the
        // CONTINUE button can be framed; DebugWarRoom hard-codes a twelve-run career), which means
        // the two screens a NEW PLAYER actually meets first have never been photographed at all.
        //
        // REVIEW FIX (C6, sent back) — WHAT ACTUALLY PROTECTS THE PLAYER'S SAVE HERE, stated
        // correctly. This comment used to read "shot-only and NoPersist-gated like the rest, so it
        // can never touch a real profile". **That is false.** The cold intro path below calls
        // SaveGame.Delete(), which has NO NoPersist guard (see SaveGame.Delete) and removes the
        // real save.json. What makes it safe is the introStash capture-and-restore immediately
        // below — FUL-2's fix, which already had to exist because the non-cold path CLOBBERS the
        // same file the same way by writing a staged mission-3 run over it. Net risk is unchanged
        // by this flag; the stated reason was simply wrong, on a line that deletes a save, which is
        // exactly the comment somebody trusts later instead of reading the code.
        bool coldShot = shot && Environment.GetEnvironmentVariable("SIGHTLINE_COLD") == "1";
        // FUL-2: the staged CONTINUE save silently CLOBBERED a real campaign save when the intro
        // shot ran on a machine with one. Stash the player's save.json bytes and restore-or-delete
        // after the shot loop (the METATEST preserve/restore pattern).
        string introStash = null; bool introStaged = false;
        if (introShot)
        {
            introStash = System.IO.File.Exists(SaveGame.SavePathPublic)
                ? System.IO.File.ReadAllText(SaveGame.SavePathPublic) : null;
            introStaged = true;
            // C6: cold = the FIRST launch. Remove the save entirely (the stash above already holds
            // the player's bytes, and the same restore path below puts them back), so CONTINUE RUN
            // renders in its real never-played state instead of the fabricated mission-3 one.
            if (coldShot) SaveGame.Delete();
            else { var r = new Run(); r.Start(); r.Mission = 3; SaveGame.Save(r); }
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
                // THE MODES GET THE BESTIARY: SIGHTLINE_FACTION=syndicate|legion|wardens|mixed pins the
                // skirmish's opposition (unset = ANY, dealt off the map seed like the card's default).
                Faction? skFac = Environment.GetEnvironmentVariable("SIGHTLINE_FACTION")?.Trim().ToLowerInvariant() switch
                {
                    "syndicate" => Faction.Syndicate,
                    "legion"    => Faction.Legion,
                    "wardens"   => Faction.Wardens,
                    "mixed" or "none" => Faction.None,
                    _ => (Faction?)null,
                };
                game.BeginSkirmish(ParseObjective(skirmishObj), skHeat, skFac);
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
        // SETTINGS EVERYWHERE: the card now has a home on the INTRO and in the BARRACKS, so this
        // composes with SIGHTLINE_INTRO=1 / SIGHTLINE_SHOP=1 to photograph the card opened there
        // (SIGHTLINE_SETTINGS=1 is the same switch under the name a reader will grep for).
        if (shot && (Environment.GetEnvironmentVariable("SIGHTLINE_PAUSE") == "1"
                     || Environment.GetEnvironmentVariable("SIGHTLINE_SETTINGS") == "1")) game.Paused = true;
        // Review round 1: SIGHTLINE_QUITARMED=1 photographs the card with QUIT ARMED — the phase- and
        // mode-true warning sentence under the plate — in whichever home the shot is staged in.
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_QUITARMED") == "1") game.QuitArmed = true;
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
        // THE FRONT DOOR (shot only): SIGHTLINE_MOUSEPARK=x,y parks the cursor for the whole shot,
        // so a hover-driven surface can be photographed AT REST. Xvfb spawns the pointer at the
        // screen centre, which on the intro is DEPLOY SQUAD, so every menu shot so far photographed
        // that door's hover caption in the shared slot rather than the resting line. Inert unset.
        {
            string park = Environment.GetEnvironmentVariable("SIGHTLINE_MOUSEPARK");
            if (shot && !string.IsNullOrEmpty(park))
            {
                var xy = park.Split(',');
                if (xy.Length == 2 && int.TryParse(xy[0], out int px) && int.TryParse(xy[1], out int py))
                    game.DebugMousePark = new System.Numerics.Vector2(px, py);
            }
        }
        string longMoveEnv = Environment.GetEnvironmentVariable("SIGHTLINE_LONGMOVE");
        bool longMove = shot && (longMoveEnv == "1" || longMoveEnv == "vault");   // THE STRIDE: =vault films a leap instead
        if (longMove && longMoveEnv == "vault") Console.WriteLine($"LONGMOVE: vault staged {game.DebugVaultMove()} at {game.AnimSpeed:0.##}x");
        else if (longMove) Console.WriteLine($"LONGMOVE: staged {game.DebugLongMove()} steps at {game.AnimSpeed:0.##}x");
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PERKSHOT") == "1") game.DebugBarracksPerk();
        // P18 THE SECOND AXIS: SIGHTLINE_BONUSSHOT=1 (or =wide) stages the BONUS perk card with its
        // RECIPIENT row (and COMBAT TRIALS' third option), which is what the wave actually changed.
        {
            string bs = Environment.GetEnvironmentVariable("SIGHTLINE_BONUSSHOT");
            if (shot && !string.IsNullOrEmpty(bs)) game.DebugBonusPerk(bs == "wide");
        }
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WAKE") == "1") game.DebugWakeAll();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_CONTENT") == "1") Mission.DebugContentShowcase(game);
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_ALERT") == "1") game.DebugAlertTiers();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_MARKERS") == "1") game.DebugMarkers();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PRESSURE") == "1") game.DebugPressure();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PODSHOT") == "1") game.DebugPodShot();   // FUL-6: pair with SIGHTLINE_MISSION=3
        // C4: the biome GROUND layer. Pair with SIGHTLINE_FORCEBIOME=2|3|7 (TUNDRA/VERDANT/MAGMA)
        // and SIGHTLINE_SHOT=760; add SIGHTLINE_CB=1 for the colorblind pass.
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_BIOMESHOT") == "1") game.DebugBiomeShot();
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WAVEBANNER") == "1") game.DebugWaveTelegraph();   // FUL-4: pair with SIGHTLINE_OBJ=defend
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_PIKESHOT") == "1") game.DebugPikemanLane();       // FUL-8: planted PIKEMAN lane (pair with SIGHTLINE_CB=1 for the second pass)
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_THREATSHOT") == "1") game.DebugThreatShot();      // RESONANCE T2: incoming-fire pips + tinted path + card (pair with SIGHTLINE_CB=1)
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_AIIDLESHOT") == "1") game.DebugAmmoShot();        // W2: the enemy ammo row + the DRY read (pair with SIGHTLINE_CB=1)
        if (shot && int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_THREATPREF"), out int _tp)) game.ThreatPref = Util.Clamp(_tp, Game.ThreatOff, Game.ThreatFull);   // 0 off / 1 simple (pre-T2 read) / 2 full
        string downShot = Environment.GetEnvironmentVariable("SIGHTLINE_DOWNSHOT");
        if (shot && (downShot == "1" || downShot == "2")) game.DebugDownShot(downShot == "2");   // FUL-7: downed soldier + rescuer (=2 mid-rescue STABLE; pair with SIGHTLINE_CB=1 for the second pass)
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_ELITESHOT") == "1") game.DebugEliteNodeShot();   // P19: the ELITE node's named opponent (pair with SIGHTLINE_SHOT=760; flip SIGHTLINE_ELITEBOSS for the contrast)
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_BANDSHOT") == "1") game.DebugRosterBandShot();   // P19: the SMG bands in the INCOMING FIRE card (flip SIGHTLINE_ROSTERID for the contrast)
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_LANESHOT") == "1") game.DebugEnemyLane();   // P10: an ordinary enemy overwatch holding a CHOSEN lane (pair with SIGHTLINE_SHOT=760; flip SIGHTLINE_AILANE for the contrast)
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_DECLINESHOT") == "1") game.DebugDeclineShot();   // C2: the opponent declines (pair with SIGHTLINE_SHOT=760 and flip SIGHTLINE_AIDECLINE for the contrast)
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
        // P34: SIGHTLINE_FXSHOT is staged INSIDE the loop, a few frames before the capture — see
        // the fxShot branch below. Staging it here (as every other *SHOT hook does) photographs
        // nothing at all: this layer is transient by definition, and at SIGHTLINE_SHOT=760 every
        // particle it spawns has been dead for eleven seconds.
        bool fxShot = shot && Environment.GetEnvironmentVariable("SIGHTLINE_FXSHOT") == "1";
        int fxShotFrame = Math.Max(1, shotFrame - 6);
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
        // W3 cross-run meta screen. C6: + SIGHTLINE_COLD=1 renders the ZERO state instead of the
        // staged twelve-run demo career — the screen a first-time player actually opens.
        if (shot && Environment.GetEnvironmentVariable("SIGHTLINE_WARROOM") == "1") game.DebugWarRoom(coldShot);
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
        string mapHoverSel = Environment.GetEnvironmentVariable("SIGHTLINE_MAPHOVER") ?? "";
        int.TryParse(mapHoverSel, out int mapHover);   // C3: hover map choice k (1-based)
        bool mapHoverNamed = mapHover <= 0 && mapHoverSel.Length > 0;
        bool mapHoverSaid = false;
        int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_SHOTSEQ"), out int seqCount);   // Q1: consecutive-frame dump
        // THE BEAT: SIGHTLINE_KILLCAM=<frame> — at that frame (before Update) the last hostile falls
        // by Game.DebugKillCam, arming the LIVE kill-cam window (AutoPlay off, so it is the slow-mo a
        // player sees, not autoplay's 0.4 s freeze). Pair with SIGHTLINE_SHOT=<same frame> and
        // SIGHTLINE_SHOTSEQ=<n> to film the window; the mission-1 briefing covers the board until
        // ~frame 700, so use 750+. Shot-only, so nothing measured moves.
        int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_KILLCAM"), out int killCamFrame);
        if (!shot) killCamFrame = 0;
        // RESONANCE C1: SIGHTLINE_SHOTONBARK=1 — do not shoot a fixed frame; wait until a soldier
        // BARK has actually landed in the combat log during live play, then shoot 40 frames later
        // (long enough for the line to settle into the ledger, short enough that it is still one of
        // the last six entries the panel shows). Pair with SIGHTLINE_AUTOPLAY=1 so a real fight is
        // driving. Shot-mode only; inert everywhere else, so nothing measured changes.
        bool shotOnBark = shot && Environment.GetEnvironmentVariable("SIGHTLINE_SHOTONBARK") == "1";
        if (shotOnBark) shotFrame = int.MaxValue;
        int frame = 0;
        // W1 derived this cap from measurement rather than guesswork; W9 then RE-derived it
        // against a run-scoped turn cap and moved the constant into Game so STALLTEST can pin
        // the relationship. W1's derivation is kept below because it is why the number exists,
        // and because its naming correction is a recorded lesson (lead, at the W9 merge).
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
        // W9 THE REPAIR: 20000 frames bought the WHOLE 6-mission campaign only ~30-40 turns (measured
        // 427-681 frames per run-turn, including the between-mission screens), so the harness budget — not any stall — was ending ~1% of
        // runs as RESULT: TIMEOUT. Game.AutoMaxRunTurns (150 run-turns) is now the binding backstop and
        // force-loses well under this; the cap stays purely as a hang guard. The number lives in Game
        // beside the turn cap it must dominate, and STALLTEST pins that relationship.
        // The observed max is retained as the SCALE for the TIMEOUT message: a bare frame count
        // says nothing about whether the cap was tight or the match genuinely stuck (lead, W9 merge).
        const int autoMax = 13589;             // observed max over n=30 Release autoplays, base 5ae9149
        const int autoCap = Game.AutoFrameCap; // W9: pinned against AutoMaxRunTurns by STALLTEST

        while (!Raylib.WindowShouldClose())
        {
            float dt = (shot || autoplay) ? 1f / 60f : Raylib.GetFrameTime();
            Display.UpdateMouse();
            if (helpShot) Raylib.SetMousePosition(592, 740);   // park cursor on the ability button
            // RESONANCE T2: a staged hover for the forecast screenshot — the card + path preview are
            // hover-driven, so the harness has to hold the cursor on the tile every frame.
            if (shot && game.DebugMousePark.HasValue)
                Raylib.SetMousePosition((int)game.DebugMousePark.Value.X, (int)game.DebugMousePark.Value.Y);
            // C3: SIGHTLINE_MAPHOVER=<k> parks the cursor on the k-th REACHABLE campaign-map node
            // (1-based), so the node hover tooltip — where the objective CLASS is spelled out —
            // can be photographed. The rects come from Hud.NodeBtns, which the map publishes as it
            // DRAWS, so this necessarily lags one frame; a shot at frame 90 has ~89 to settle.
            // Pair with SIGHTLINE_CAMPAIGN=1 + SIGHTLINE_SHOT. Shot-only, so nothing measured moves.
            // THE FORK PAYS adds the by-NAME forms, so the three economy screenshots (an economy
            // stop, a PITCHED fight, an unknown signal) are reproducible without hunting for the
            // index a particular seed happens to deal:
            //   SIGHTLINE_MAPHOVER=supply|combat|elite|event|boss|start  first reachable node of that KIND
            //   SIGHTLINE_MAPHOVER=pitched|tasked                        first reachable node of that CLASS
            // Unresolvable (this fork deals no such node) parks nothing and says so once — a
            // silently un-hovered shot is how a staging bug becomes a published screenshot.
            if (shot && (mapHover > 0 || mapHoverNamed) && Hud.NodeBtns.Count > 0)
            {
                int hi = mapHover > 0 ? mapHover - 1 : ResolveMapHover(game, mapHoverSel);
                if (hi >= 0 && hi < Hud.NodeBtns.Count)
                {
                    var hr = Hud.NodeBtns[hi].Rect;
                    Raylib.SetMousePosition((int)(hr.X + hr.Width / 2), (int)(hr.Y + hr.Height / 2));
                    if (mapHoverNamed && !mapHoverSaid)
                    {
                        var hn = game.RunState.Map[Hud.NodeBtns[hi].Id];
                        Console.WriteLine($"MAPHOVER: '{mapHoverSel}' -> btn {hi + 1}/{Hud.NodeBtns.Count} "
                                        + $"kind={hn.Kind} obj={(hn.Card != null ? hn.Card.Objective.ToString() : "-")} intel={hn.Intel}");
                        mapHoverSaid = true;
                    }
                }
                else if (mapHoverNamed && !mapHoverSaid)
                { Console.WriteLine($"MAPHOVER: '{mapHoverSel}' -> NO MATCH among {Hud.NodeBtns.Count} reachable nodes"); mapHoverSaid = true; }
            }
            if (tooltipHover) game.KbCursor = true;            // Q1: hold the board cursor on the foe (a mouse
                                                               // delta from the Xvfb pointer clears it otherwise)
            if (killCamFrame > 0 && frame == killCamFrame) game.DebugKillCam();   // THE BEAT: film the live kill-cam
            game.Update(dt);
            Audio.UpdateMusic(dt);

            // W5: two-pass frame — the board (and the overlay screens' backdrop) goes through the
            // post-FX grade, the HUD is drawn on top of the composite so its type stays crisp.
            // Q1: autoplay normally skips the heavy draw (it's a smoke test), but a shot frame
            // requested ON TOP of autoplay is asking for a picture of live play — the only way
            // to photograph a unit MID-MOVE — so draw for real in that combination.
            // W1: when nothing is being photographed there is no frame worth drawing at all —
            // pump the event queue instead (BatchPump). The clear and the buffer swap were the
            // whole cost of an autoplay smoke run; keeping W5's two-pass call on the draw side
            // preserves the crisp-HUD split without paying for it in the smoke test.
            // (lead, at the W5 merge: W1 supplies the fast path, W5 the drawn one.)
            // P27: the projected-camera sweep. Deliberately does NOT go through Display.RenderFrame
            // (no render target, no post-FX) — this is a look-at-it prototype, and the fewer layers
            // between the geometry and the PNG the more honestly it answers the question.
            if (view3dSweep != null && shot && frame >= shotFrame)
            {
                var all = new List<Unit>(game.Players);
                all.AddRange(game.Enemies);
                foreach (var (pd, yd) in view3dSweep)
                {
                    View3D.PitchDeg = pd; View3D.YawDeg = yd;
                    Raylib.BeginDrawing();
                    View3D.Scene = game.Biome;   // P31: the projected view joins its biome
                    View3D.DrawFrame(game.Grid, all);
                    Raylib.EndDrawing();
                    Raylib.TakeScreenshot($"view3d_p{pd:00}_y{yd:000}.png");
                }
                break;
            }
            // P34 — stage the feedback layer a few frames BEFORE the capture, so the shot catches
            // it mid-life: the tracer wake still travelling, the numbers past their pop and into
            // their rise, the shake at most of its amplitude. Six frames is a tenth of a second,
            // which is where this layer actually lives.
            if (fxShot && frame == fxShotFrame) game.DebugFxShot();
            if (autoplay && !shot) BatchPump();
            else Display.RenderFrame(game.DrawBoardLayer, game.DrawHudLayer);

            // W5 THE DOORS: the player asked to leave. Nothing to flush — the quit path writes
            // nothing (the campaign checkpoint was written at mission start), so break straight
            // into the normal shutdown below.
            if (game.QuitRequested) break;
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
                if (game.Phase == Phase.Win) { Console.WriteLine($"RESULT: WIN mission={game.RunState.Mission} frame={frame} turns={game.RunTurns}"); break; }
                if (game.Phase == Phase.Lose) { Console.WriteLine($"RESULT: LOSE mission={game.RunState.Mission} frame={frame} turns={game.RunTurns}"); break; }
                // W1: a TIMEOUT now says how far past normal it got. "frame=20000" alone told you
                // nothing about whether the cap was tight or the match was genuinely stuck.
                if (frame >= autoCap) { Console.WriteLine($"RESULT: TIMEOUT mission={game.RunState.Mission} frame={frame} turns={game.RunTurns} cap={autoCap} observedMax={autoMax} ({(double)frame / autoMax:0.0}x the longest campaign measured)"); break; }
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
    /// THE FORK PAYS (shot staging only): resolve a NAMED SIGHTLINE_MAPHOVER selector to an index
    /// into Hud.NodeBtns. Pure lookup over the run's own map — no draw, no RNG, and only ever
    /// reached on a screenshot frame.
    static int ResolveMapHover(Game game, string sel)
    {
        var run = game.RunState;
        if (run == null) return -1;
        sel = sel.Trim().ToLowerInvariant();
        for (int i = 0; i < Hud.NodeBtns.Count; i++)
        {
            int id = Hud.NodeBtns[i].Id;
            if (id < 0 || id >= run.Map.Count) continue;
            var n = run.Map[id];
            bool kill = n.Kind != NodeKind.Event && n.Card != null && Run.IsKillObjective(n.Card.Objective);
            bool hit = sel switch
            {
                "supply"  => n.Kind == NodeKind.Supply,
                "combat"  => n.Kind == NodeKind.Combat,
                "elite"   => n.Kind == NodeKind.Elite,
                "event"   => n.Kind == NodeKind.Event,
                "boss"    => n.Kind == NodeKind.Boss,
                "start"   => n.Kind == NodeKind.Start,
                "pitched" => kill,
                "tasked"  => n.Kind != NodeKind.Event && !kill,
                _         => false,
            };
            if (hit) return i;
        }
        return -1;
    }

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

    // P15 THE UNVERIFIED: the SECOND refusal, one layer under W1's.
    // W1 made a display-less batch refuse (exit 2) because a batch that measured nothing must not
    // leave something that looks like an answer. A batch asked for a rung it cannot parse is the
    // same failure with the file WRITTEN: `SIGHTLINE_BALANCE_HEAT=$H` fell through a bare
    // `int.TryParse` to "cycle {0,2,4,6,8}" in total silence, so a typo'd chunk was archived under
    // a rung it did not measure with `runs` correct, the file fresh, the exit code 0, and every
    // layer of the measurement contract reporting OK. There is no honest recovery from that — the
    // chunk's whole identity is the value that failed to parse — so refuse the same way, with a
    // DISTINCT code (3, vs 2 for "no display") the runner can tell apart.
    static void RefuseBatch(params string[] reasons)
    {
        foreach (string r in reasons) Console.Error.WriteLine($"BALANCE: {r}");
        Console.Error.WriteLine("BALANCE: refusing to run a batch it cannot name. No data written.");
        Console.Error.Flush();
        Environment.Exit(3);
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
        // THE HEAT PIN: a measured rung means the rung. Field events could raise a campaign's
        // HeatLevel mid-run (Events.cs AddHeat, three arms, and the bot PREFERS one of them), so
        // every rung below 8 was contaminated upward. Pinned by default in a batch;
        // SIGHTLINE_HEATPIN=0 restores the leaky instrument (the bridge arm in
        // docs/measurements/l5/). Nothing outside a batch sets it — EVENTTEST and real play see the
        // shipped outcome.
        EventCatalog.HeatPinned = Environment.GetEnvironmentVariable("SIGHTLINE_HEATPIN") != "0";
        Console.WriteLine(EventCatalog.HeatPinned
            ? "BALANCE: heat PINNED for this batch (field-event AddHeat is a no-op; SIGHTLINE_HEATPIN=0 restores the leak)"
            : "BALANCE: heat NOT pinned (SIGHTLINE_HEATPIN=0) - a heat-N rung may contain heat-N+1 missions");

        // Optional pinned heat; otherwise cycle the ladder-spanning default set so the curve shows.
        // APEX W4: the default re-baseline now SPANS THE LADDER — {0,2,4,6,8} instead of i%5 —
        // so routine batches measure the mutator rungs (EXPOSED@6 / NO QUARTER@8) instead of only
        // heats 0-4 (6/8 were previously measured only in ad-hoc pinned runs).
        int[] heatCycle = { 0, 2, 4, 6, 8 };

        // ── P15 THE UNVERIFIED: the batch RECORDS AND VALIDATES ITS OWN REQUEST ───────────
        // These two used to be bare `int.TryParse`s with a silent fallback, next to two vars
        // (SIGHTLINE_OBJ / SIGHTLINE_PERK, below) that already warn loudly on a typo — and unlike
        // those, a mis-parsed _HEAT/_BASE does not merely leave a probe unset, it renames the
        // chunk. `Stats.ParseBatchEnv` is the one place the request is read and checked, and
        // `Stats.Batch` puts it in the artifact so a runner can assert the JSON against what it
        // exported instead of trusting the file name. Anything unparseable REFUSES (exit 3).
        // The policy reads move up here from further down so the request knows its own shape —
        // ExpectedRuns = N x legs is the number the chunk runners have been hard-coding as N*2.
        bool dumb = Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE_DUMB") == "1";
        bool sloppyOnly = Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE_SLOPPY") == "1";
        string policies = dumb ? "dumb" : sloppyOnly ? "sloppy" : "greedy+sloppy";
        var batch = Stats.ParseBatchEnv(
            Environment.GetEnvironmentVariable(endless ? "SIGHTLINE_BALANCE_ENDLESS" : "SIGHTLINE_BALANCE"),
            Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE_HEAT"),
            Environment.GetEnvironmentVariable("SIGHTLINE_BALANCE_BASE"),
            endless ? "endless" : "campaign", policies, (dumb || sloppyOnly) ? 1 : 2,
            EventCatalog.HeatPinned);
        if (batch.EnvErrors.Count > 0) RefuseBatch(batch.EnvErrors.ToArray());
        Stats.Batch = batch;
        Console.WriteLine($"BALANCE: request n={batch.N} legs={batch.PolicyLegs} ({batch.Policies}) mode={batch.Mode} "
                        + $"heat={(batch.Heat.HasValue ? batch.Heat.Value.ToString() : "CYCLED " + string.Join(",", heatCycle))} "
                        + $"base={batch.SlotBase} -> expect runs={batch.ExpectedRuns}");

        bool pinHeat = batch.Heat.HasValue;
        int fixedHeat = batch.Heat ?? 0;
        // W2: SIGHTLINE_BALANCE_BASE=<n> offsets the slot index, so CHUNKED batches (the 10-min
        // shell ceiling forces N<=10 per invocation) can cover DISJOINT paired worlds — without
        // it, two combined N=10 chunks replay the SAME 10 seeds and halve the effective sample.
        int slotBase = batch.SlotBase;   // P15: parsed + validated above, never a silent fallback
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
        // (P15: read above, with SIGHTLINE_BALANCE_SLOPPY, so the batch request knows its own shape.)
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

        // W9 THE REPAIR: raised 20000 -> Game.AutoFrameCap with the autoplay cap. A frame-cap
        // hit is scored as a LOSS below, so at 20000 the batch RIGHT-CENSORED exactly the longest
        // campaigns (the archived x2 chunks show it firing: one 20-match chunk logs "frame-cap hits:
        // 1"), putting a small unattributed downward bias into the ladder of record.
        // Game.AutoMaxRunTurns (150 run-turns) now force-loses a genuinely dragging campaign long
        // before this, so the cap is a hang guard only.
        const int frameCap = Game.AutoFrameCap;   // per-match safety cap; a hit cap counts as a loss
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
        // (P15: `sloppyOnly` is read up with the batch request so ExpectedRuns can be stamped.)
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
                    Stats.EndRun(false, game.RunState != null ? game.RunState.Mission - 1 : 0, "frame-cap",
                                 game.RunState != null ? game.RunState.HeatLevel : -1, game.RunTurns);
                    return true;
                }
            }
            // window closed mid-match (Xvfb teardown / Ctrl-C): close the run record and stop.
            Stats.EndRun(false, endless ? game.Wave : (game.RunState != null ? game.RunState.Mission - 1 : 0), "aborted",
                         game.RunState != null ? game.RunState.HeatLevel : -1, game.RunTurns);
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

        const int frameCap = Game.AutoFrameCap;   // W9: matched to BalanceBatch's cap (a censored leg is not a leg)
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

    // ── W2 THE OPPONENT ACTS — SIGHTLINE_AIIDLETEST ───────────────────────────────────────
    // The invariant: NO enemy act-opportunity ever ends with an unspent action and no branch
    // fired. The auditor measured the violation at 111/465 = 23.9% of act-opportunities over 6
    // instrumented campaigns and 171/641 = 26.7% over 10 — roughly one enemy turn in four
    // produced a banner, a telegraph, a move, and then nothing at all.
    //
    // The probe is Game.ActProbe, fired once per act-opportunity right after ActAfterMove's
    // branch chain. Every branch in that chain changes ActionsLeft (ten zero it, the shot
    // decrements it), so "still holds an action AND ActionsLeft is unchanged" is an exact
    // structural test for "no branch fired" — it needs no cooperation from the branches and it
    // cannot be fooled by a future branch that forgets to spend (that IS an idle).
    //
    // NON-VACUITY, and the reason this test cannot pass by accident: it runs the SAME seeds
    // TWICE — leg A with Game.AiIdleFix off (the pre-wave opponent) and leg B with it on. PASS
    // requires idle == 0 AND dry-idle == 0 in leg B *and* idle > 0 in leg A. A test that cannot
    // fail is not a test, so the failing tree is part of the assertion, not a footnote.
    static void AiIdleTest(int n)
    {
        Stats.Reset();
        Stats.Enabled = false;
        Mission.ForcedLayout = -1;
        Pal.SetColorblind(false);

        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
        Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "SIGHTLINE — ai idle test");
        RequireWindow("AIIDLETEST");   // zero sampled enemy turns is not a clean sweep
        Raylib.SetExitKey(KeyboardKey.Null);
        Cfg.Font = Raylib.GetFontDefault();
        Display.Init(false);
        Raylib.SetTargetFPS(0);

        bool shipped = Game.AiIdleFix;

        // One leg's counters, and EVERY ONE OF THEM IS SPLIT BY WHETHER ANY SOLDIER IS STILL
        // STANDING. That split is not a refinement, it is the measurement: on an all-downed board
        // Ai.Plan returns an empty plan by design (the FUL-7 downed filter empties `players`), so
        // every hostile idles and always did. The wave's first write-up reported the UNSPLIT rate
        // as combat paralysis; 86% of it was the bleed-out window. Contested = at least one alive,
        // non-downed soldier — the only regime in which "the enemy did nothing" is a defect.
        //   acts/actsDry  — act-opportunities, and those made holding an empty weapon
        //   idle/idleDry  — ended with an action unspent and no branch fired
        //   noTgt         — idles where the planner had planned no shot at all
        //   actedDown     — acts that DID something on an all-downed board. Pre-wave this is 0
        //                   (nothing can fire off an empty plan); the wave's first build made it
        //                   320, of which 319 popped HUNKERED into the player's death scene.
        (long acts, long actsDry, long idle, long idleDry, long noTgt,
         long actsC, long actsDryC, long idleC, long idleDryC, long noTgtC,
         long actedDown, int campaigns, int missions,
         long dashOffered, long dashTaken, long reloads, long repairs, long owHeld,
         double dashLoss) Leg(bool fix)
        {
            Game.AiIdleFix = fix;
            long acts = 0, actsDry = 0, idle = 0, idleDry = 0, noTgt = 0;
            long actsC = 0, actsDryC = 0, idleC = 0, idleDryC = 0, noTgtC = 0, actedDown = 0;
            int campaigns = 0, missions = 0;
            long dashOffered = 0, dashTaken = 0, reloads = 0, repairs = 0, owHeld = 0;
            double dashLoss = 0;
            Ai.DashProbe = (dashScore, bestScore, spent, win) =>
            {
                dashOffered++;
                if (win) dashTaken++;
                dashLoss += bestScore - dashScore;   // raw, BEFORE the move-cost refund
            };
            Game.ActProbe = (u, plan, actionsBefore, ammoBefore, standing) =>
            {
                if (plan != null && plan.Reload) reloads++;
                if (plan != null && plan.IdleRepair) repairs++;
                // W3 hand-off: how often does the enemy OVERWATCH branch actually fire? (Not
                // "is it planned" — planned AND accepted by the exec, i.e. the unit ended its
                // turn holding a lane.) The review claims zero; this makes it my own number.
                if (u.OnOverwatch && !u.OwBrace) owHeld++;
                bool contested = standing > 0;
                acts++;
                if (contested) actsC++;
                if (ammoBefore <= 0) { actsDry++; if (contested) actsDryC++; }
                bool isIdle = u.ActionsLeft > 0 && u.ActionsLeft == actionsBefore;
                if (!isIdle)
                {
                    if (!contested) actedDown++;   // the bleed-out chorus guard
                    return;
                }
                idle++;
                if (contested) idleC++;
                if (ammoBefore <= 0) { idleDry++; if (contested) idleDryC++; }
                if (plan == null || plan.ShootTarget == null) { noTgt++; if (contested) noTgtC++; }
            };

            var objs = new[] { Objective.Eliminate, Objective.Hack, Objective.Evac, Objective.Escort,
                               Objective.Sabotage, Objective.Rescue, Objective.Defend, Objective.Decapitate };
            int slot = 0;
            foreach (int heat in new[] { 0, 4 })
                foreach (var o in objs)
                    for (int rep = 0; rep < n; rep++)
                    {
                        if (Raylib.WindowShouldClose()) break;
                        Environment.SetEnvironmentVariable("SIGHTLINE_HEAT", heat.ToString());
                        // paired: leg A and leg B replay the identical worlds until the opponent's
                        // own behaviour diverges, so the two counter sets are comparable.
                        Util.Reseed(90000 + slot++);
                        var game = new Game { NoPersist = true, AutoPlay = true, SmartPlay = true,
                                              ForcedObjective = o };
                        game.StartMission(1);
                        int frame = 0;
                        // WHOLE campaigns, not single missions — this is the auditor's own sampling
                        // frame. Mission 1 is the shortest, coldest fight in the game (a trimmed
                        // opener the greedy bot clears in two turns); measuring only mission 1 samples
                        // ~2 enemy act-opportunities per leg entry and would report an idle rate drawn
                        // almost entirely from the opening exchange.
                        while (!Raylib.WindowShouldClose())
                        {
                            game.Update(1f / 60f);
                            BatchPump();
                            if (++frame >= 20000) break;
                            if (game.Phase == Phase.Lose || game.Phase == Phase.Win) break;
                        }
                        campaigns++;
                        missions += game.RunState != null ? Math.Max(1, game.RunState.Mission) : 1;
                    }

            Game.ActProbe = null;
            Ai.DashProbe = null;
            return (acts, actsDry, idle, idleDry, noTgt,
                    actsC, actsDryC, idleC, idleDryC, noTgtC, actedDown, campaigns, missions,
                    dashOffered, dashTaken, reloads, repairs, owHeld, dashLoss);
        }

        var off = Leg(false);
        var on  = Leg(true);

        Game.AiIdleFix = shipped;
        Util.Reseed(0);
        Display.Shutdown();
        Renderer.UnloadNoise();
        Raylib.CloseWindow();

        string Row(string tag, (long acts, long actsDry, long idle, long idleDry, long noTgt,
                                long actsC, long actsDryC, long idleC, long idleDryC, long noTgtC,
                                long actedDown, int campaigns, int missions,
                                long dashOffered, long dashTaken, long reloads, long repairs,
                                long owHeld, double dashLoss) r)
            => $"AIIDLETEST: {tag} campaigns={r.campaigns} missions={r.missions}\n" +
               $"AIIDLETEST:   CONTESTED (>=1 soldier standing — the regime an idle is a DEFECT in): " +
               $"acts={r.actsC} actsDry={r.actsDryC} " +
               $"idle={r.idleC} ({(r.actsC > 0 ? 100.0 * r.idleC / r.actsC : 0):0.0}%) " +
               $"idleDry={r.idleDryC} noTgt={r.noTgtC}\n" +
               $"AIIDLETEST:   ALL-DOWNED (bleed-out window — Ai.Plan returns an empty plan BY DESIGN): " +
               $"acts={r.acts - r.actsC} idle={r.idle - r.idleC} " +
               $"({(r.acts - r.actsC > 0 ? 100.0 * (r.idle - r.idleC) / (r.acts - r.actsC) : 0):0.0}%) " +
               $"acted={r.actedDown}\n" +
               $"AIIDLETEST:   UNSPLIT (do NOT quote this as a paralysis rate): " +
               $"acts={r.acts} actsDry={r.actsDry} " +
               $"idle={r.idle} ({(r.acts > 0 ? 100.0 * r.idle / r.acts : 0):0.0}%) " +
               $"idleDry={r.idleDry} noTgt={r.noTgt}\n" +
               $"AIIDLETEST:   SCALE OF THE LIVE REPAIR (what the wave actually changes in play): " +
               $"reload={r.reloads} terminal-else={r.repairs} (dash {r.dashTaken} of {r.dashOffered} offered, " +
               $"rawScoreLoss/offer={(r.dashOffered > 0 ? r.dashLoss / r.dashOffered : 0):0.0}) " +
               $"=> {(r.acts > 0 ? 100.0 * (r.reloads + r.repairs) / r.acts : 0):0.0}% of ALL acts, " +
               $"{(r.actsC > 0 ? 100.0 * (r.reloads + r.repairs) / r.actsC : 0):0.0}% of CONTESTED acts\n" +
               $"AIIDLETEST:   FOR W3: enemy act-opportunities that ended holding an OVERWATCH lane = {r.owHeld}";
        Console.WriteLine(Row("AIIDLEFIX=0", off));
        Console.WriteLine(Row("AIIDLEFIX=1", on));

        var idleFails = new System.Collections.Generic.List<string>();
        // (1) the invariant: no CONTESTED act-opportunity ends unspent.
        if (on.idleC > 0)    idleFails.Add($"contestedIdleActs={on.idleC}");
        if (on.idleDryC > 0) idleFails.Add($"contestedIdleDryActs={on.idleDryC}");
        // (2) the bleed-out guard, and it is as load-bearing as (1). A squad on the floor must not be
        //     narrated at by the hostiles standing over it; the first W2 build did exactly that 320
        //     times, and no balance measurement can see it because no soldier can act.
        if (on.actedDown > 0)  idleFails.Add($"actedDuringBleedOut={on.actedDown}");
        if (off.actedDown > 0) idleFails.Add($"actedDuringBleedOut(pre-wave)={off.actedDown}");
        // (3) non-vacuity, on the CONTESTED counter — the split is what makes the probe honest, so
        //     the sensitivity check has to be on the split number, not on the flattering unsplit one.
        if (off.idleC == 0)  idleFails.Add("probeInsensitive(the pre-wave tree never idled in a contested fight)");
        if (on.actsC < 200)  idleFails.Add($"vacuous(contestedActs={on.actsC})");   // no display / no gameplay
        Console.WriteLine(idleFails.Count == 0
            ? "AIIDLETEST: PASS (no CONTESTED act-opportunity ends unspent; no dry weapon holds one; nothing acts or speaks during the bleed-out window; the pre-wave tree still fails the same probe)"
            : "AIIDLETEST: FAIL (" + string.Join(",", idleFails) + ")");
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
    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  W5 THE FIRST HOUR — SIGHTLINE_CONTRASTTEST=1
    //
    //  Pins the repair for audit visual-2. `Display.RenderFrame` used to put the ENTIRE frame
    //  through the bright-pass, so a saturated UI plate bloomed into its own label: the main
    //  menu's TRAINING OP — the on-ramp button for a first-time player — measured 2.19:1
    //  glyph-vs-fill with post-FX on, against 8.67:1 with it off. W5 split the frame in two
    //  (board + overlay backdrop become the BLOOM SOURCE; the chrome is painted on top of the
    //  bright pass, into the same target so the colour grade stays uniform), and this test is the
    //  standing gate that keeps it that way. Shipped reading: 10.76:1.
    //
    //  It boots a REAL 1280x800 window with Display and PostFX ON — a screen read is the only
    //  honest instrument here, because the whole defect lived in the composite — drives the intro
    //  for 90 frames so the panel-entrance animations settle, then reads the framebuffer back and
    //  measures each named button.
    //
    //  METHOD (mirrored exactly by the offline probe quoted in DEVLOG): inset the button rect by
    //  8 px so the plate border is excluded; glyph core = 2nd percentile of a 3x3 MIN-filtered
    //  relative luminance, plate fill = 98th percentile of a 3x3 MAX-filtered one; WCAG ratio
    //  between them. The min/max filter erases antialiased edge pixels (an edge pixel always has
    //  both a brighter and a darker neighbour), so the two readings are the glyph INTERIOR and
    //  the plate INTERIOR — which is what "glyph vs fill" means, and what the authored colours
    //  were picked against (Pal.Good vs the RGBA(3,18,26) label is 10.90:1 on paper).
    //
    //  NOTE ON THE BLOOM LEVEL, because it changes what this number means: SIGHTLINE_POSTFX=1's
    //  boot-time "demo bloom" injection is OVERWRITTEN on frame 1 by Game.Update's own
    //  SetPostFxParams(_postFxBloom, ...), which rests at 0. So this measures the SHIPPED resting
    //  configuration — the fragment shader's always-on `bloomAmt = 1.45` baseline halo — not a
    //  combat spike. The defect was in what every player sees on the menu, every time.
    // ═══════════════════════════════════════════════════════════════════════════════════════
    static string ContrastSelfTest()
    {
        var fails = new List<string>();

        // Stage a save so the CONTINUE RUN plate exists (the FUL-2 stash/restore pattern — never
        // clobber a real campaign), then boot the window the game actually ships.
        string stash = System.IO.File.Exists(SaveGame.SavePathPublic)
            ? System.IO.File.ReadAllText(SaveGame.SavePathPublic) : null;
        try
        {
            var r = new Run(); r.Start(); r.Mission = 3; SaveGame.Save(r);

            Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
            Raylib.InitWindow(Cfg.ScreenW, Cfg.ScreenH, "contrasttest");
            Raylib.SetExitKey(KeyboardKey.Null);
            LoadGameFonts();
            Display.Init(true);
            Display.PostFX = true;
            Raylib.SetTargetFPS(0);

            var g = new Game { NoPersist = true };
            for (int i = 0; i < 90; i++)
            {
                g.Update(1f / 60f);
                Display.RenderFrame(g.DrawBoardLayer, g.DrawHudLayer);
            }
            if (g.Phase != Phase.Intro) fails.Add("notOnIntro:" + g.Phase);

            var shot = Raylib.LoadImageFromScreen();
            var boxes = new (string name, Rectangle r)[]
            {
                ("CONTINUE_RUN", Hud.OverlayBtn2), ("DEPLOY_SQUAD", Hud.OverlayBtn),
                ("TRAINING_OP",  Hud.OverlayBtn8), ("LAST_STAND",   Hud.OverlayBtn3),
                ("WAR_ROOM",     Hud.OverlayBtn4), ("FIELD_MANUAL", Hud.OverlayBtn5),
                ("SKIRMISH",     Hud.OverlayBtn6), ("DAILY",        Hud.OverlayBtn7),
                ("AUDIO_CHECK",  Hud.OverlayBtn9), ("SETTINGS",     Hud.IntroSettingsBtn),
            };
            var report = new List<string>();
            foreach (var (name, rect) in boxes)
            {
                if (rect.Width < 20 || rect.Height < 20) { fails.Add("noRect:" + name); continue; }
                float ratio = LabelContrast(shot, rect, 8);
                report.Add($"{name} {ratio:0.00}");
                if (ratio < 4.5f) fails.Add($"{name}@{ratio:0.00}");
            }
            Raylib.UnloadImage(shot);
            Raylib.CloseWindow();

            return fails.Count == 0
                ? "CONTRASTTEST: PASS (post-FX ON, glyph-vs-plate >= 4.5:1 on all "
                  + boxes.Length + " main-menu labels: " + string.Join(" ", report) + ")"
                : "CONTRASTTEST: FAIL (" + string.Join(",", fails) + ")";
        }
        catch (Exception ex) { return "CONTRASTTEST: FAIL (threw:" + ex.GetType().Name + ":" + ex.Message + ")"; }
        finally
        {
            try
            {
                if (stash != null) System.IO.File.WriteAllText(SaveGame.SavePathPublic, stash);
                else if (System.IO.File.Exists(SaveGame.SavePathPublic)) System.IO.File.Delete(SaveGame.SavePathPublic);
            }
            catch { }
        }
    }

    /// WCAG glyph-core-vs-plate-fill contrast inside `rect` of a screen grab. See ContrastSelfTest.
    static float LabelContrast(Image img, Rectangle rect, int inset)
    {
        int x0 = (int)rect.X + inset, y0 = (int)rect.Y + inset;
        int w = (int)rect.Width - 2 * inset, h = (int)rect.Height - 2 * inset;
        var L = new float[h, w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = Raylib.GetImageColor(img, x0 + x, y0 + y);
                L[y, x] = 0.2126f * Srgb(c.R) + 0.7152f * Srgb(c.G) + 0.0722f * Srgb(c.B);
            }
        var mins = new List<float>(w * h);
        var maxs = new List<float>(w * h);
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                float lo = 2f, hi = -1f;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    { float v = L[y + dy, x + dx]; if (v < lo) lo = v; if (v > hi) hi = v; }
                mins.Add(lo); maxs.Add(hi);
            }
        mins.Sort(); maxs.Sort();
        float glyph = mins[Math.Clamp((int)(mins.Count * 0.02f), 0, mins.Count - 1)];
        float plate = maxs[Math.Clamp((int)(maxs.Count * 0.98f), 0, maxs.Count - 1)];
        float dark = MathF.Min(glyph, plate), light = MathF.Max(glyph, plate);
        return (light + 0.05f) / (dark + 0.05f);
    }

    static float Srgb(byte b)
    {
        float c = b / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

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
