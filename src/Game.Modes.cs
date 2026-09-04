using System;
using System.Collections.Generic;
using System.Linq;
using Raylib_cs;

namespace Sightline;

// PROGRAM HORIZON — Wave 4: SKIRMISH + SEEDED DAILY.
//
// Two SINGLE-MISSION modes that complete the modes offering (DEPLOY / LAST STAND / SKIRMISH / DAILY).
// Both reuse the whole tactical kernel + the campaign per-objective win tests — they just swap the
// 6-mission campaign shell for ONE fight, with NO barracks / checkpoint valve / save.json.
//
//  - SKIRMISH: jump straight into one fight with a player-chosen OBJECTIVE + HEAT on a random arena.
//  - DAILY:    a deterministic date-seeded single-mission challenge (fixed objective/arena/heat derived
//              from the day's seed) with a persistent local BEST (fewest turns to win) in meta.json.
//
// Both share GameMode.Skirmish (a `_dailyMode` flag differentiates DAILY); the core Game.cs edits are
// intentionally tiny (an appended enum member + a CheckEnd branch + a couple `Mode == Campaign` guards
// where a `Mode != Endless` guard would otherwise leak campaign-only setup into a skirmish). All the
// mode's logic lives here.
public partial class Game
{
    // ── Skirmish/Daily state ────────────────────────────────────────────────────────────────────
    // True when this Skirmish instance is actually the SEEDED DAILY (drives the HUD readout + records
    // the daily best on a win). Ignored unless Mode == GameMode.Skirmish.
    public bool DailyMode;
    // The active daily stamp (yyyymmdd) for the HUD readout + meta record. 0 outside a daily.
    public int DailyStamp;
    // Cached DAILY BEST (fewest turns to a win; 0 = not yet cleared today) for the end card. -1 = unloaded.
    int _dailyBest = -1;

    // The FIXED daily stamp used under NoPersist when SIGHTLINE_DAILY is unset — keeps the headless/
    // deterministic path reproducible (NEVER DateTime.Now under NoPersist, per the spec).
    const int DefaultDailyStamp = 20260701;

    // ── SKIRMISH-setup screen state (Phase.SkirmishSetup) ───────────────────────────────────────
    // The intro SKIRMISH button opens a compact setup: cycle the objective, dial heat, then START.
    public Objective SkirmishObjective = Objective.Eliminate;
    public int SkirmishHeat;   // 0..UnlockedHeat, shares the campaign Heat ladder
    // THE MODES GET THE BESTIARY — the FACTION dial. null = ANY: dealt at deploy from the run's
    // MapSeed (a pure derivation, zero Util.Rng draws) among MIXED / SYNDICATE / LEGION / WARDENS.
    // UI state only — Faction is persisted-by-ordinal and APPEND-ONLY, so "ANY" is deliberately
    // NOT an enum member (nothing was appended; SAVETEST's fingerprint is unchanged).
    public Faction? SkirmishFaction;
    /// The faction the ACTIVE skirmish/daily fields. Game.SetupMission publishes it through
    /// Combat.BeginMission in place of the campaign node's stamp (a fresh Run sits on its Start
    /// node, which is Faction.None by design). None = the mixed cascade. Cleared by ResetModeState.
    public Faction ModeFaction = Faction.None;
    static readonly Faction[] NamedFactions = { Faction.Syndicate, Faction.Legion, Faction.Wardens };
    /// The dial's cycle order (ANY first, then the three named factions in enum order).
    static readonly Faction?[] SkirmishFactionDial = { null, Faction.Syndicate, Faction.Legion, Faction.Wardens };

    /// The 8 selectable skirmish objectives, in enum order (matches the campaign rotation).
    static readonly Objective[] SkirmishObjectives =
    {
        Objective.Eliminate, Objective.Evac, Objective.Hack, Objective.Escort,
        Objective.Sabotage, Objective.Rescue, Objective.Defend, Objective.Decapitate,
    };

    // ── Entry ───────────────────────────────────────────────────────────────────────────────────

    /// Open the SKIRMISH setup screen from the intro. Seeds the cycler defaults (Eliminate + the
    /// dialled-in intro Heat, clamped to what's unlocked). Interactive only — the harness calls
    /// BeginSkirmish directly, bypassing this screen.
    public void BeginSkirmishSetup()
    {
        EnsureMetaLoaded();
        SkirmishObjective = Objective.Eliminate;
        SkirmishFaction = null;   // ANY
        SkirmishHeat = Sightline.Heat.Clamp(Math.Min(PendingHeat, UnlockedHeat));
        Phase = Phase.SkirmishSetup;
        Audio.Play("select");
    }

    /// SKIRMISH-setup input: cycle the objective (arrows/click), dial heat, START (Enter/button), or
    /// BACK to the intro (Esc/button). Mouse rects are published by Hud.DrawSkirmishSetup.
    void HandleSkirmishSetup()
    {
        // BACK -> intro
        bool back = Raylib.IsKeyPressed(KeyboardKey.Escape)
                    || (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                        Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Hud.SkirmBack));
        if (back) { Phase = Phase.Intro; Audio.Play("select"); return; }

        int idx = Array.IndexOf(SkirmishObjectives, SkirmishObjective);
        if (idx < 0) idx = 0;
        int objDelta = 0;
        if (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressed(KeyboardKey.A)) objDelta = -1;
        else if (Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressed(KeyboardKey.D)) objDelta = 1;
        // W9 THE REPAIR: the screen's own footer legend reads "+/- heat" (Hud.DrawSkirmishSetup) and
        // this handler bound NEITHER. `grep -nE "KpAdd|KpSubtract|KeyboardKey.Equal|KeyboardKey.Minus"
        // src/*.cs` found exactly two hits in the whole codebase, both in the INTRO's heat stepper —
        // so a player who had just dialled difficulty with +/- on the intro found those keys dead one
        // screen later, with the screen still telling them to use them. Binding is the better half of
        // the fix than rewording: it makes the legend true AND makes the two heat dials consistent.
        // Equal/Minus come along because "+/-" most naturally means the main-row keys on a laptop
        // (neither was bound anywhere in the game, so nothing is displaced). Up/Down and W/S keep
        // working — this only ADDS keys.
        int heatDelta = 0;
        if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S)
            || Raylib.IsKeyPressed(KeyboardKey.KpSubtract) || Raylib.IsKeyPressed(KeyboardKey.Minus)) heatDelta = -1;
        else if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W)
            || Raylib.IsKeyPressed(KeyboardKey.KpAdd) || Raylib.IsKeyPressed(KeyboardKey.Equal)) heatDelta = 1;

        // THE MODES GET THE BESTIARY: the FACTION cycler. TAB steps it (the "next" idiom the
        // in-mission TAB already carries for unit cycling; this handler runs only in
        // Phase.SkirmishSetup, so no binding is displaced) — SHIFT+TAB steps back.
        int facDelta = 0;
        if (Raylib.IsKeyPressed(KeyboardKey.Tab))
            facDelta = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift) ? -1 : 1;

        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            var m = Raylib.GetMousePosition();
            if (Raylib.CheckCollisionPointRec(m, Hud.SkirmObjPrev)) objDelta = -1;
            else if (Raylib.CheckCollisionPointRec(m, Hud.SkirmObjNext)) objDelta = 1;
            else if (Raylib.CheckCollisionPointRec(m, Hud.SkirmFacPrev)) facDelta = -1;
            else if (Raylib.CheckCollisionPointRec(m, Hud.SkirmFacNext)) facDelta = 1;
            else if (Raylib.CheckCollisionPointRec(m, Hud.SkirmHeatMinus)) heatDelta = -1;
            else if (Raylib.CheckCollisionPointRec(m, Hud.SkirmHeatPlus)) heatDelta = 1;
        }
        if (facDelta != 0)
        {
            int fi = Array.IndexOf(SkirmishFactionDial, SkirmishFaction);
            if (fi < 0) fi = 0;
            SkirmishFaction = SkirmishFactionDial[(fi + facDelta + SkirmishFactionDial.Length) % SkirmishFactionDial.Length];
            Audio.Play("select");
        }

        if (objDelta != 0)
        {
            idx = (idx + objDelta + SkirmishObjectives.Length) % SkirmishObjectives.Length;
            SkirmishObjective = SkirmishObjectives[idx];
            Audio.Play("select");
        }
        if (heatDelta != 0)
        {
            // W5: the dial's floor is Heat.Min, so a skirmish can be set to RECRUIT too — the
            // intro seeds SkirmishHeat from PendingHeat, and a dial that snapped back to 0 on the
            // first press would silently discard the difficulty the player had already chosen.
            SkirmishHeat = Math.Clamp(SkirmishHeat + heatDelta, Sightline.Heat.Min, UnlockedHeat);
            Audio.Play("select");
        }

        // START -> begin the skirmish
        bool start = Raylib.IsKeyPressed(KeyboardKey.Enter)
                     || (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                         Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Hud.SkirmStart));
        if (start) { Audio.Cue(Audio.GameEvent.Turn); BeginSkirmish(SkirmishObjective, SkirmishHeat, SkirmishFaction); }
    }

    /// Display label for the FACTION dial (null = ANY).
    public static string SkirmishFactionLabel(Faction? f) => f.HasValue ? Run.FactionName(f.Value) : "ANY";

    /// The force the active skirmish/daily fields, as a short name for the HUD readout.
    public string ModeForceName => ModeFaction == Faction.None ? "MIXED" : Run.FactionName(ModeFaction);

    /// ANY, resolved: one of MIXED / SYNDICATE / LEGION / WARDENS off the run's MapSeed — bits 24+,
    /// which nothing else reads (the arena deck and the biome key on the seed as a whole through
    /// their own hashes). A pure derivation: zero Util.Rng draws, so the harness's reseed-then-
    /// BeginSkirmish legs deal the same world they always did, faction included.
    static Faction DealtFaction(int seed)
    {
        int k = (int)(((uint)seed >> 24) % 4u);
        return k == 0 ? Faction.None : NamedFactions[k - 1];
    }

    /// Begin a SKIRMISH: one fight with the chosen objective + heat on a random arena. Mirrors the
    /// BeginEndless setup contract (fresh Run, default squad, heat, telemetry) but forces a single
    /// objective and leaves Mode == Skirmish so CheckEnd routes to CheckSkirmish (single-mission end).
    public void BeginSkirmish(Objective obj, int heat, Faction? faction = null)
    {
        ResetModeState();   // W1 mode-seam: inherit nothing (incl. a daily-forced arena / leaked seed)
        Mode = GameMode.Skirmish;
        EnsureMetaLoaded();
        _run = new Run();
        _run.Start();                     // default founding squad + a campaign map we ignore (single mission)
        // THE MODES GET THE BESTIARY: the dialled faction, or ANY dealt off the map seed Start just rolled.
        ModeFaction = faction ?? DealtFaction(_run.MapSeed);
        _run.HeatLevel = Sightline.Heat.Clamp(heat);
        _run.StartHeat = _run.HeatLevel;   // P18: latch the DIALLED rung — see Run.AssistLevel
        RefreshMetaWidths();               // P18: COMBAT TRIALS is a PROFILE unlock, not a campaign one
        _run.LossStreak = _metaLossStreak;
        // force the chosen objective for mission 1 (DebugForceObjective-style, but WITHOUT re-running
        // SetupMission — we call it once below with everything staged).
        _run.CurrentCard = new MissionCard { Objective = obj, ModName = "SKIRMISH", Reward = RewardKind.None };
        // APEX W4: mode goes in RunRec.Mode (policy slot stays a real policy) — see BeginEndless.
        Stats.BeginRun(_run.HeatLevel, SmartPlay && SmartSloppy ? "sloppy" : "greedy", "skirmish");
        Players = _run.Squad;
        Wave = 0;
        SetupMission(1);
    }

    /// Begin the SEEDED DAILY: a deterministic single-mission challenge derived from the day's seed.
    /// LIVE play seeds from today's date; the HARNESS (NoPersist) reads SIGHTLINE_DAILY=<yyyymmdd>
    /// (falling back to a fixed constant — NEVER DateTime.Now under NoPersist) so autoplay/shots are
    /// reproducible. Objective + arena + heat are all derived from the seed, so the same day always
    /// plays the same board.
    public void BeginDaily()
    {
        ResetModeState();   // W1 mode-seam: inherit nothing from a prior mode
        Mode = GameMode.Skirmish;
        DailyMode = true;
        EnsureMetaLoaded();

        int stamp = ResolveDailyStamp();
        DailyStamp = stamp;
        int seed = DailySeed(stamp);

        // derive the fixed daily parameters from the seed (deterministic + reproducible)
        Objective obj = DailyObjective(seed);
        int arena = DailyArena(seed);
        int heat = DailyHeat(seed);

        _run = new Run();
        _run.Start();
        _run.HeatLevel = Sightline.Heat.Clamp(heat);
        _run.StartHeat = _run.HeatLevel;   // P18: latch the DIALLED rung — see Run.AssistLevel
        RefreshMetaWidths();               // P18: COMBAT TRIALS is a PROFILE unlock, not a campaign one
        _run.LossStreak = _metaLossStreak;
        // pin the map seed so the whole board (biome + arena selection driven off MapSeed) is
        // reproducible for the day — the same seed reproduces the same layout signature (verified twice).
        _run.MapSeed = seed;
        // THE MODES GET THE BESTIARY: the day's FACTION, derived from the seed like the objective,
        // arena and heat — always one of the three named factions, so a daily is never the mixed
        // cascade and the same stamp fields the same force (MODETEST leg 9 pins the composition).
        ModeFaction = DailyFaction(seed);
        _run.CurrentCard = new MissionCard { Objective = obj, ModName = "DAILY", Reward = RewardKind.None };
        // force the day's arena. Set ForcedLayout so SetupMission -> Mission.Build stamps it (both live
        // and harness — the daily's determinism is the point). It is cleared when leaving skirmish.
        Mission.ForcedLayout = arena;
        // APEX W4: mode goes in RunRec.Mode (policy slot stays a real policy) — see BeginEndless.
        Stats.BeginRun(_run.HeatLevel, SmartPlay && SmartSloppy ? "sloppy" : "greedy", "daily");
        Players = _run.Squad;
        Wave = 0;
        // SEEDED DAILY: reseed the shared RNG from the day's seed so the WHOLE procedural board (arena
        // sprinkles / barrels / pod scatter — all draw from Util.Rng) is reproducible for the day. The
        // seed is restored to a clock seed on leaving the daily so no other mode inherits it.
        Util.Reseed(seed);
        SetupMission(1);
    }

    /// Resolve the daily stamp (yyyymmdd). Under NoPersist (harness), honor SIGHTLINE_DAILY, else a
    /// fixed constant (deterministic). Only LIVE play reads DateTime.Now — and only live play reads
    /// the CLOCK: the env read is NoPersist-gated (W1 mode-seam) so a stale SIGHTLINE_DAILY in a
    /// live shell can never pin every "today" to one frozen stamp.
    int ResolveDailyStamp()
    {
        if (NoPersist)
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_DAILY"), out int envStamp) && envStamp > 0)
                return envStamp;
            return DefaultDailyStamp;   // deterministic headless fallback (NEVER DateTime.Now)
        }
        var d = DateTime.Now.Date;
        return d.Year * 10000 + d.Month * 100 + d.Day;
    }

    /// A stable non-zero seed from a yyyymmdd stamp (FNV-1a of its digits so consecutive days scatter).
    static int DailySeed(int stamp)
    {
        uint h = 2166136261u;
        // hash the stamp's digits so the seed varies wildly day to day (not just a linear stamp).
        foreach (char c in stamp.ToString())
        { h ^= c; h *= 16777619u; }
        // keep it a positive, non-zero int (Run.MapSeed of 0 is treated as "no map" on load).
        int s = (int)(h & 0x7fffffff);
        return s == 0 ? 1 : s;
    }

    /// The day's objective, derived from the seed (all 8 objectives, deterministic).
    static Objective DailyObjective(int seed) => SkirmishObjectives[(seed & 0x7fffffff) % SkirmishObjectives.Length];

    /// The day's arena index (an authored layout), derived from the seed.
    static int DailyArena(int seed) => (int)(((uint)seed >> 8) % (uint)Maps.Layouts.Length);

    /// The day's HEAT, derived from the seed. A modest fixed challenge band (0..3) so the daily is
    /// tough-but-fair regardless of the player's unlocked ceiling; the seed decides it, not the player.
    static int DailyHeat(int seed) => (int)(((uint)seed >> 16) % 4u);

    /// The day's FACTION (THE MODES GET THE BESTIARY), derived from the seed's next byte up. Always a
    /// NAMED faction — the daily's heat band (0..3) never reaches the mid-boss rung, so the faction is
    /// the whole of what makes one day's force different from the next.
    static Faction DailyFaction(int seed) => NamedFactions[(int)(((uint)seed >> 24) % 3u)];

    /// The force TODAY's daily fields, for the intro's DAILY caption — the daily has no setup card, so
    /// the caption is the one place a player learns the opposition before committing the day's single
    /// attempt. Same stamp resolution BeginDaily uses (env / constant under NoPersist, the clock live).
    public string TodayDailyForceName => Run.FactionName(DailyFaction(DailySeed(ResolveDailyStamp())));

    // ── PROGRAM RESONANCE T1 — the TRAINING OP ────────────────────────────────────────────────────
    // A fixed, scripted, NON-PERSISTENT, restartable drill. Every persistence seam in the codebase
    // is already keyed on `Mode == GameMode.Campaign` (the save checkpoint in SetupMission, the
    // barracks/debrief ladder, the secondary/intel-cache rolls) or on DailyMode (the meta bounty),
    // so Mode == Training writes NOTHING by construction — no save.json, no meta.json, no veteran
    // reserve, no salvage, no achievements, no daily best. TUTTEST asserts that emptiness rather
    // than trusting it. The only profile-level flag the drill ever touches is Display.TrainingSeen,
    // and only on COMPLETION (so the first-launch offer stops nagging).

    /// Begin (or restart) the TRAINING OP from the intro screen. Restart is the same call: the drill
    /// rebuilds itself from scratch every time, which is exactly the low-cost-failure contract.
    /// Fixed biome seed for the drill (see BeginTraining). Biome.IndexFor is
    /// (seed + mission-1) % Biome.All.Length, so 8 lands on STEEL — a cool neutral board, which is
    /// the right ground for a teaching frame: nothing in the terrain competes with the amber
    /// objective accent or the red threat accent the lessons are pointing at (DESIGN.md 3.H).
    public const int TrainingMapSeed = 8;

    public void BeginTraining()
    {
        ResetModeState();   // W1 mode-seam: inherit nothing (forced arena, daily seed, wave counter…)
        Mode = GameMode.Training;
        EnsureMetaLoaded();
        _run = new Run();
        _run.Start(Mission.TrainingSquad());   // the drill's own two recruits — never the campaign roster
        _run.HeatLevel = 0;                    // no heat, no ascension, no contract, no boon
        _run.MapSeed = TrainingMapSeed;        // pin the biome too: the drill looks the same every time
                                               // (Run.Start rolled a random seed; the drill's board is
                                               //  authored, so the only thing that seed still drives is
                                               //  Biome.For — and a fixed drill should be fixed on screen)
        _run.LossStreak = 0;
        _run.CurrentCard = new MissionCard { Objective = Objective.Eliminate, ModName = "TRAINING", Reward = RewardKind.None };
        Players = _run.Squad;
        Wave = 0;
        // NOT Stats.BeginRun: the drill is not a measured match and must never enter a balance batch.
        SetupMission(1);
    }

    /// TRAINING OP end check. Deliberately simpler than CheckSkirmish: the drill is always Eliminate,
    /// there is no VIP and no captive, so the only two outcomes are "field cleared" and "both recruits
    /// are down". A downed (bleeding-out) recruit is NOT a loss — the bleed-out clock is itself one of
    /// the things the drill can teach.
    void CheckTraining()
    {
        if (AlivePlayers().Count(p => !p.IsVip) == 0) { EndTraining(false); return; }
        if (AliveEnemies().Count == 0) EndTraining(true);
    }

    /// End the drill. No campaign side-effects of any kind; the ONLY write is the one-shot
    /// Display.TrainingSeen on a completion, and only when the profile is live (!NoPersist).
    void EndTraining(bool win)
    {
        Combat.EndRun();      // clear every mission-scoped combat static (mirrors LoseRun/EndSkirmish)
        TrainStep = -1;
        RevealedVerbs.Clear();
        CalloutText = null;
        if (win)
        {
            LoseTitle = null; LoseReason = null;
            Phase = Phase.Win;
            Audio.Play("win");
            Audio.PlayStinger("victory");
            if (!NoPersist) Display.MarkTrainingSeen();
        }
        else
        {
            LoseTitle = "DRILL ENDED";
            LoseReason = "Nothing was lost - the training op never touches your campaign. Run it again, or deploy for real.";
            Phase = Phase.Lose;
            Audio.Play("lose");
        }
    }

    // ── Single-mission end/advance (called from CheckEnd instead of the campaign ladder) ─────────

    /// SKIRMISH/DAILY end check: a wipe (or a lost VIP/captive) ends the mission as a loss; completing
    /// the objective ends it as a win. Reuses the SAME per-objective win tests as the campaign CheckEnd,
    /// but routes to EndSkirmish (Phase.Win/Lose) instead of the barracks/checkpoint/save flow.
    void CheckSkirmish()
    {
        var alivePlayers = AlivePlayers();
        if (alivePlayers.Count == 0) { EndSkirmish(false); return; }
        // APEX W2: RESCUE soft-lock (the same hole the campaign CheckEnd had): the invulnerable caged
        // captive keeps AlivePlayers() non-empty after a real wipe, and with no soldier left it can
        // never be freed — the fight would sit forever. Single-mission modes have no checkpoint
        // valve, so an abandoned cage is simply a loss.
        if (Objective == Objective.Rescue && CaptiveLocked && !alivePlayers.Any(p => !p.IsVip))
        { EndSkirmish(false); return; }

        switch (Objective)
        {
            case Objective.Eliminate:
                if (AliveEnemies().Count == 0) EndSkirmish(true);
                break;
            case Objective.Hack:
                if (HackProgress >= HackRequired) EndSkirmish(true);
                break;
            case Objective.Sabotage:
                if (SabotageBlown.Count >= SabotageSites.Count) EndSkirmish(true);
                break;
            case Objective.Escort:
                if (Vip == null || !Vip.Alive) { EndSkirmish(false); return; }
                if (EvacZone.Contains((Vip.X, Vip.Y))) EndSkirmish(true);
                break;
            case Objective.Rescue:
                // W4 (SIGNAL) belt-and-braces (mirrors the campaign CheckEnd): a dead captive is a
                // loss even while still caged — a caged death must never soft-lock the skirmish.
                if (Vip == null || !Vip.Alive) { EndSkirmish(false); return; }
                if (!CaptiveLocked && EvacZone.Contains((Vip.X, Vip.Y))) EndSkirmish(true);
                break;
            case Objective.Defend:
                if (_turnCount > DefendTurns) EndSkirmish(true);
                break;
            case Objective.Decapitate:
                if (Hvt == null || !Hvt.Alive) EndSkirmish(true);
                break;
            default: // Evac: every surviving soldier must stand in the extraction zone
                if (alivePlayers.All(p => EvacZone.Contains((p.X, p.Y)))) EndSkirmish(true);
                break;
        }
    }

    /// End a SKIRMISH/DAILY. Sets Phase.Win / Phase.Lose with a single-mission summary; for the DAILY,
    /// records the result to meta (best = fewest turns to a win). NO campaign side-effects (no save.json,
    /// no loss-streak, no barracks). Mirrors the shape of EndEndless.
    void EndSkirmish(bool win)
    {
        Combat.EndRun();   // clear every mission-scoped combat static (mirrors LoseRun/EndEndless)
        if (DailyMode) Util.Reseed(0);   // release the deterministic daily seed (back to a clock seed)

        // DAILY: persist today's best (fewest turns to a win). Recorded once at mission end.
        if (DailyMode && !NoPersist)
        {
            RecordDailyResult(win, _turnCount);
            _dailyBest = -1;   // force a fresh read for the end card

            // W9 (SIGNAL): the retention mode finally FEEDS the meta — a daily WIN pays a salvage
            // bounty of 10+heat, at most ONCE per stamp (keyed on the paid stamp in meta.json, so
            // replaying today's challenge can never farm it), and drives the consecutive-day streak
            // (+ the DAY SHIFT / DAWN PATROL achievements). All behind the same !NoPersist gate.
            // Review fix: the pay and the paid-mark are ONE atomic meta write inside RecordDailyWin —
            // a crash here can neither burn the bounty (marked-but-unpaid) nor double-pay it.
            if (win)
            {
                int bounty = 10 + (_run?.HeatLevel ?? 0);
                var (paidOut, streak) = SaveGame.RecordDailyWin(DailyStamp, bounty);
                if (paidOut)
                {
                    _run?.Report.Insert(0, streak > 1
                        ? $"DAILY BOUNTY +{bounty} SALVAGE   ({streak}-DAY STREAK)"
                        : $"DAILY BOUNTY +{bounty} SALVAGE");
                    TryAchievement("DAILY_WIN");
                    if (streak >= 5) TryAchievement("STREAK5");
                }
            }
        }

        if (win)
        {
            LoseTitle = null; LoseReason = null;
            Phase = Phase.Win;
            Audio.Play("win");
            Audio.PlayStinger("victory");
        }
        else
        {
            bool wiped = AlivePlayers().Count(p => !p.IsVip) == 0;
            LoseTitle = DailyMode ? "DAILY FAILED" : "SKIRMISH LOST";
            LoseReason = wiped ? "The squad fell." : "The objective was lost.";
            Phase = Phase.Lose;
            Audio.Play("lose");
            Audio.PlayStinger(wiped ? "squadwipe" : "lose");
        }

        // balance telemetry: close the single-mission record + the (one-mission) run.
        string tag = DailyMode ? "daily" : "skirmish";
        Stats.EndMission(win, _turnCount, AlivePlayers().Count(p => !p.IsVip),
                         Enemies.Count(e => !e.Alive), tag);
        Stats.EndRun(win, win ? 1 : 0, tag);
    }

    // ── DAILY best persistence (meta.json, append-only, gated by NoPersist at the call sites) ────

    /// Record today's DAILY result. Stores the stamp + the best outcome for that stamp: the fewest
    /// turns to a WIN (lower is better; 0 = not yet cleared). A new day resets the best. A loss on an
    /// already-cleared day keeps the win. Whole-DTO read-modify-write like every other meta field.
    void RecordDailyResult(bool win, int turns)
    {
        if (NoPersist) return;
        int prevBest = SaveGame.LoadDailyBest(DailyStamp);   // 0 if a different/unplayed day
        int newBest = prevBest;
        if (win)
            newBest = prevBest <= 0 ? turns : Math.Min(prevBest, turns);
        SaveGame.SaveDailyResult(DailyStamp, newBest);
    }

    /// The current DAILY best (fewest turns to win today; 0 = not yet cleared). Cached for the HUD/end
    /// card; 0 headless (no disk).
    public int DailyBest
    {
        get
        {
            if (_dailyBest < 0) _dailyBest = NoPersist ? 0 : SaveGame.LoadDailyBest(DailyStamp);
            return _dailyBest;
        }
    }

    // ── HUD readouts ─────────────────────────────────────────────────────────────────────────────

    /// Top-bar readout for SKIRMISH / DAILY (replaces the campaign objective text). e.g.
    /// "DAILY 20260701" or "SKIRMISH — HACK". Only meaningful when Mode == Skirmish.
    public string SkirmishHud
    {
        get
        {
            // THE MODES GET THE BESTIARY: the readout names the force too — the top bar is the
            // one line that outlives the banner, and a player mid-fight should not have to guess
            // which faction rule (Legion close-range, Warden long-range, Syndicate low-cover) is on.
            if (DailyMode)
            {
                int b = DailyBest;
                return b > 0 ? $"DAILY {DailyStamp} — {ModeForceName}   BEST {b}" : $"DAILY {DailyStamp} — {ModeForceName}";
            }
            return $"SKIRMISH — {SkirmishObjectiveLabel(Objective)} — {ModeForceName}";
        }
    }

    /// A short display label for an objective (skirmish/daily HUD + setup screen).
    public static string SkirmishObjectiveLabel(Objective o) => o switch
    {
        Objective.Eliminate => "ELIMINATE",
        Objective.Evac => "EXTRACT",
        Objective.Hack => "HACK",
        Objective.Escort => "ESCORT",
        Objective.Sabotage => "SABOTAGE",
        Objective.Rescue => "RESCUE",
        Objective.Defend => "DEFEND",
        Objective.Decapitate => "DECAPITATE",
        _ => o.ToString().ToUpperInvariant(),
    };

    // ── harness: screenshot the SKIRMISH setup screen (SIGHTLINE_SKIRMISHSETUP=1) ────────────────
    public void DebugSkirmishSetup() { BeginSkirmishSetup(); SkirmishObjective = Objective.Hack; SkirmishFaction = Faction.Legion; }

    /// A cheap deterministic fingerprint of the current board (terrain tiles + heights + every unit's
    /// spawn tile), used by ModeSelfTest to prove the SEEDED DAILY reproduces the same board twice.
    string BoardSignature()
    {
        uint h = 2166136261u;
        void Mix(int v) { unchecked { h ^= (uint)v; h *= 16777619u; } }
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            { Mix((int)Grid.Tiles[x, y]); Mix(Grid.Height[x, y]); }
        foreach (var u in Players) { Mix(u.X); Mix(u.Y); }
        foreach (var e in Enemies) { Mix(e.X); Mix(e.Y); }
        return h.ToString("x8");
    }

    /// THE MODES GET THE BESTIARY: a fingerprint of the fielded FORCE — every hostile's class, name,
    /// HP, aim and pod, in spawn order, plus the mission faction. BoardSignature pins WHERE the
    /// bodies stand; this pins WHAT they are. Used by ModeSelfTest leg (9).
    string ForceSignature()
    {
        uint h = 2166136261u;
        void Mix(int v) { unchecked { h ^= (uint)v; h *= 16777619u; } }
        Mix((int)Combat.MissionFaction);
        foreach (var e in Enemies)
        {
            foreach (char c in e.Cls + "|" + e.Name) Mix(c);
            Mix(e.MaxHp); Mix(e.Aim); Mix(e.PodId);
        }
        return h.ToString("x8");
    }

    // ── SIGHTLINE_MODEFORCEPROBE — a REPORT, not an assertion ────────────────────────────────────
    //  P14 THE UNVERIFIED. Findings 9-13 are all "what force does a SKIRMISH or DAILY actually
    //  field?", and until this hook the only headless answer was MODETEST's one printed headcount
    //  line. This dumps the whole composition per heat rung so a change to the single-mission
    //  modes can be shown rather than described: headcount, pod plan, the class histogram, how
    //  often the ENTIRE opposition is one uniform pod / immobile, the escort asset's statline, the
    //  Decapitate HVT's bonus, the mid-boss callsign and kit, and one DEFEND wave's composition.
    //
    //  It asserts nothing (it is named ...PROBE and sits in the sweep's _SWEEP_EXEMPT list for
    //  exactly that reason) — the assertions live in MODETEST.
    public string ModeForceProbe()
    {
        var sb = new System.Text.StringBuilder();
        NoPersist = true;
        int[] rungs = { 0, 2, 4, 6, 8 };
        sb.AppendLine("MODEFORCEPROBE: skirmish force composition per heat rung");
        foreach (int hh in rungs)
        {
            // pinned seed + arena, exactly like MODETEST legs 7-8, so the rungs are comparable
            Util.Reseed(4242); Sightline.Mission.ForcedLayout = 5;
            BeginSkirmish(Objective.Eliminate, hh);
            var pods = Enemies.GroupBy(e => e.PodId).OrderBy(g => g.Key).Select(g => g.Count()).ToList();
            string cls = string.Join(",", Enemies.GroupBy(e => e.Cls).OrderBy(g => g.Key)
                                                 .Select(g => g.Key + "x" + g.Count()));
            sb.AppendLine($"  h{hh} pinned: bodies={Enemies.Count} pods=[{string.Join("+", pods)}] {cls}");

            // 40 unpinned builds: how often is the WHOLE force one uniform pod, or entirely immobile?
            Util.Reseed(9001 + hh);
            int onePod = 0, allStatic = 0, allOneCls = 0; var seen = new SortedSet<string>();
            for (int b = 0; b < 40; b++)
            {
                BeginSkirmish(Objective.Eliminate, hh);
                foreach (var e in Enemies) seen.Add(e.Cls);
                if (Enemies.Select(e => e.PodId).Distinct().Count() == 1) onePod++;
                if (Enemies.Count > 0 && Enemies.All(e => e.Mobility == 0)) allStatic++;
                if (Enemies.Select(e => e.Cls).Distinct().Count() == 1) allOneCls++;
            }
            sb.AppendLine($"  h{hh} x40:    single-pod {onePod}/40  ALL-IMMOBILE {allStatic}/40  one-class {allOneCls}/40  roster={string.Join("/", seen)}");

            // the escort asset and the HVT — do they scale with the dial the enemy does?
            Util.Reseed(4242); Sightline.Mission.ForcedLayout = 5;
            BeginSkirmish(Objective.Escort, hh);
            string vip = Vip != null ? $"hp={Vip.MaxHp} armor={Vip.Armor}" : "none";
            Util.Reseed(4242); Sightline.Mission.ForcedLayout = 5;
            BeginSkirmish(Objective.Decapitate, hh);
            string hvt = Hvt != null ? $"{Hvt.Name} hp={Hvt.MaxHp} aim={Hvt.Aim} buffed={HvtBuffed}" : "none";
            sb.AppendLine($"  h{hh} asset:  VIP {vip}   HVT {hvt}");

            // the mid-boss slot (heat >= 4) and its kit
            if (hh >= 4)
            {
                Util.Reseed(4242); Sightline.Mission.ForcedLayout = 5;
                BeginSkirmish(Objective.Eliminate, hh, Faction.None);
                var el = Enemies.FirstOrDefault(e => e.Cls == "ELITE");
                sb.AppendLine(el == null
                    ? $"  h{hh} midboss: NONE (MIXED)"
                    : $"  h{hh} midboss: MIXED -> {el.Name} hp={el.MaxHp} rage={el.RagesTwice} shield={el.HasShieldArc} siege={el.HasSiege}");
            }

            // one DEFEND wave, staged exactly as the objective stages it
            Util.Reseed(4242); Sightline.Mission.ForcedLayout = 5;
            BeginSkirmish(Objective.Defend, hh);
            int before = Enemies.Count;
            _turnCount = 3;
            SpawnDefendWave();
            var wave = Enemies.Skip(before).ToList();
            sb.AppendLine($"  h{hh} wave:   initial={before} +{wave.Count} [" +
                          string.Join(" ", wave.Select(e => $"{e.Cls}/{e.MaxHp}hp/{e.Aim}aim")) + "]");
        }
        Sightline.Mission.ForcedLayout = -1;
        Util.Reseed(0);
        sb.Append("MODEFORCEPROBE: done (report only - no assertion)");
        return sb.ToString();
    }

    /// P14 — the DAILY SIGNATURE this process computes, for the cross-process leg. One line, no
    /// player data (NoPersist), no window beyond the tiny one Program already opened.
    public string DailySignatureLine()
    {
        NoPersist = true;
        BeginDaily();
        return DailyStamp + "|" + (int)Combat.MissionFaction + "|" + ForceSignature() + "|" + BoardSignature();
    }

    /// Run THIS binary again with SIGHTLINE_DAILYSIGPROBE=1 and read back the one line it prints.
    /// The same shape as SHIPTEST's second launch (Ship.cs) — bounded, and the child writes nothing
    /// (it never leaves NoPersist), so unlike SHIPTEST it can strand no player data at all.
    /// Returns null if the child could not be run or printed nothing recognisable.
    string RunDailySigChild()
    {
        try
        {
            string exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return null;
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = System.IO.Directory.GetCurrentDirectory(),
            };
            // a `dotnet run` harness launches through the host: re-launch the managed dll the same way
            var asm = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(asm) && System.IO.Path.GetFileNameWithoutExtension(exe) == "dotnet")
                psi.ArgumentList.Add(asm);
            // The child INHERITS this process's environment on purpose — every measurement dial
            // (MODEDEPTH / OPENERTRIM / PODUNIFORM / BIOMEMECH / ...) must apply to both or the two
            // processes are not the same game and the comparison is meaningless. Program's
            // DAILYSIGPROBE branch is the FIRST hook branch it reaches, so an inherited
            // SIGHTLINE_MODETEST cannot make the child re-run this suite.
            psi.Environment["SIGHTLINE_DAILYSIGPROBE"] = "1";
            // the child must derive the SAME day: pass this process's resolved stamp explicitly.
            psi.Environment["SIGHTLINE_DAILY"] = DailyStamp.ToString();
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return null;
            string outp = proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            if (!proc.WaitForExit(20000)) { try { proc.Kill(true); } catch { } return null; }
            foreach (var line in outp.Split('\n'))
                if (line.StartsWith("DAILYSIG:", StringComparison.Ordinal))
                    return line.Substring("DAILYSIG:".Length).Trim();
            return null;
        }
        catch { return null; }
    }

    // ── MODETEST self-test (SIGHTLINE_MODETEST) ──────────────────────────────────────────────────
    // Asserts: (1) the same daily seed reproduces identical objective+arena+heat (twice); (2) a
    // skirmish single-mission end sets Phase (Win/Lose), NOT Barracks; (3) the meta daily stamp/best
    // round-trips (preserving/restoring any real meta.json, like HORDETEST/METATEST); (4) ABANDON is
    // mode-aware (mode-true lose titles); (5) a campaign abandon NEVER deletes the checkpoint
    // (preserving/restoring any real save.json); (6) prints a daily->abandon->draft-pool fingerprint
    // for the cross-process daily-seed-leak check (fingerprints must differ between two processes).
    public string ModeSelfTest()
    {
        var fails = new List<string>();
        try
        {
            // (1) DETERMINISM: the same stamp -> the same seed -> identical derived params, twice.
            const int stamp = 20260701;
            int s1 = DailySeed(stamp), s2 = DailySeed(stamp);
            if (s1 != s2) fails.Add("seedNonDeterministic");
            if (s1 == 0) fails.Add("seedZero");
            var (o1, a1, h1) = (DailyObjective(s1), DailyArena(s1), DailyHeat(s1));
            var (o2, a2, h2) = (DailyObjective(s2), DailyArena(s2), DailyHeat(s2));
            if (o1 != o2 || a1 != a2 || h1 != h2) fails.Add("dailyParamsNonDeterministic");
            if (a1 < 0 || a1 >= Maps.Layouts.Length) fails.Add("dailyArenaOutOfRange");
            if (h1 < 0 || h1 > Sightline.Heat.Max) fails.Add("dailyHeatOutOfRange");
            // a different day should (usually) scatter to a different seed — assert it's not a constant.
            if (DailySeed(20260701) == DailySeed(20260702) && DailySeed(20260701) == DailySeed(20261225))
                fails.Add("seedNotScattering");

            // (2) SINGLE-MISSION END: begin a skirmish, kill every foe, and confirm CheckSkirmish
            //     drives Phase.Win — NOT Barracks (no campaign advance).
            NoPersist = true;
            BeginSkirmish(Objective.Eliminate, 0);
            if (Mode != GameMode.Skirmish) fails.Add("modeNotSkirmish");
            if (Phase != Phase.PlayerTurn) fails.Add("skirmishDidNotStart");
            foreach (var e in Enemies) { e.Hp = 0; e.Alive = false; }
            _anims.Clear();
            CheckEnd();   // routes to CheckSkirmish -> EndSkirmish(true)
            if (Phase == Phase.Barracks) fails.Add("skirmishEnteredBarracks");
            if (Phase != Phase.Win) fails.Add("skirmishWinNotSet");

            // a LOSS path: wipe the squad -> Phase.Lose (never Barracks / never the checkpoint valve).
            BeginSkirmish(Objective.Eliminate, 0);
            foreach (var p in Players) { p.Hp = 0; p.Alive = false; }
            _anims.Clear();
            CheckEnd();
            if (Phase == Phase.Barracks) fails.Add("skirmishLossEnteredBarracks");
            if (Phase != Phase.Lose) fails.Add("skirmishLoseNotSet");

            // BeginDaily sets the daily flags + a reproducible seed (deterministic under NoPersist).
            BeginDaily();
            if (!DailyMode) fails.Add("dailyFlagNotSet");
            if (DailyStamp != DefaultDailyStamp) fails.Add("dailyStampFallback");   // NoPersist + no env -> constant
            int seedA = _run.MapSeed;
            string boardA = BoardSignature();
            BeginDaily();
            int seedB = _run.MapSeed;
            string boardB = BoardSignature();
            if (seedA != seedB) fails.Add("dailyMapSeedNonDeterministic");
            // the WHOLE procedural board (terrain + spawns) is reproducible for the day (RNG reseeded).
            if (boardA != boardB) fails.Add("dailyBoardNonDeterministic");

            // (3) META ROUND-TRIP: daily stamp + best persist (preserve+restore any real meta.json).
            NoPersist = false;
            string metaSaved = System.IO.File.Exists(SaveGame.MetaPathPublic)
                ? System.IO.File.ReadAllText(SaveGame.MetaPathPublic) : null;
            try
            {
                SaveGame.SaveDailyResult(20260701, 9);
                if (SaveGame.LoadDailyBest(20260701) != 9) fails.Add("dailyBestRoundTrip");
                // a DIFFERENT day reads 0 (the stored stamp doesn't match).
                if (SaveGame.LoadDailyBest(20260702) != 0) fails.Add("dailyBestWrongDay");
                // overwriting the same day with a better (lower) turn count stores it.
                SaveGame.SaveDailyResult(20260701, 5);
                if (SaveGame.LoadDailyBest(20260701) != 5) fails.Add("dailyBestOverwrite");
                // the whole-DTO r-m-w must NOT clobber a sibling meta field (BestWave).
                SaveGame.SaveMetaBestWave(21);
                SaveGame.SaveDailyResult(20260701, 4);
                if (SaveGame.LoadMetaBestWave() != 21) fails.Add("dailyClobberedBestWave");
            }
            finally
            {
                if (metaSaved != null) { try { System.IO.File.WriteAllText(SaveGame.MetaPathPublic, metaSaved); } catch { } }
                else { try { if (System.IO.File.Exists(SaveGame.MetaPathPublic)) System.IO.File.Delete(SaveGame.MetaPathPublic); } catch { } }
            }

            // (4) W1 mode-seam: ABANDON routes per mode — each single-run mode ends through its own
            //     ender with a mode-true lose title (never the campaign's "RUN ABANDONED").
            NoPersist = true;
            BeginSkirmish(Objective.Eliminate, 0);
            AbandonRun();
            if (Phase != Phase.Lose) fails.Add("skirmishAbandonNoLose");
            if (LoseTitle != "SKIRMISH LOST") fails.Add($"skirmishAbandonTitle({LoseTitle})");
            BeginEndless();
            AbandonRun();
            if (Phase != Phase.Lose) fails.Add("endlessAbandonNoLose");
            if (LoseTitle != "LAST STAND") fails.Add($"endlessAbandonTitle({LoseTitle})");
            BeginDaily();
            AbandonRun();
            if (LoseTitle != "DAILY FAILED") fails.Add($"dailyAbandonTitle({LoseTitle})");

            // (5) W1 mode-seam: a CAMPAIGN abandon is CHECKPOINT-PRESERVING. With a real checkpoint
            //     on disk, abandoning must leave save.json in place (CONTINUE still offered) — only
            //     LoseRun (a real defeat) deletes it. Preserves/restores any real save.json.
            NoPersist = false;
            string saveSaved = System.IO.File.Exists(SaveGame.SavePathPublic)
                ? System.IO.File.ReadAllText(SaveGame.SavePathPublic) : null;
            try
            {
                var keep = new Run(); keep.Start(); keep.Mission = 2;
                SaveGame.Save(keep);
                if (!SaveGame.Exists) fails.Add("checkpointNotWritten");
                Mode = GameMode.Campaign; DailyMode = false;
                _run = keep; TutStep = -1;
                AbandonRun();
                if (!SaveGame.Exists) fails.Add("abandonDeletedCheckpoint");
                if (Phase != Phase.Lose) fails.Add("campaignAbandonNoLose");
                if (LoseTitle != "RUN ABANDONED") fails.Add($"campaignAbandonTitle({LoseTitle})");
            }
            finally
            {
                if (saveSaved != null) { try { System.IO.File.WriteAllText(SaveGame.SavePathPublic, saveSaved); } catch { } }
                else SaveGame.Delete();
            }

            // (6) W1 mode-seam: DAILY -> ABANDON must release the deterministic day seed (the abandon
            //     routes through EndSkirmish -> Util.Reseed(0)). A same-process differ check cannot
            //     see the leak (the RNG stream advances between runs regardless), so print a draft-
            //     pool fingerprint for a CROSS-PROCESS check: two fresh processes must print
            //     DIFFERENT fingerprints (with the leak both derive from the same daily stamp).
            NoPersist = true;
            BeginDaily();
            AbandonRun();
            uint pfp = 2166136261u;
            foreach (var u in Run.GenerateDraftPool())
                foreach (char c in u.Name + u.Cls) { unchecked { pfp ^= c; pfp *= 16777619u; } }
            Console.WriteLine($"MODETEST daily-abandon draft-pool fingerprint: {pfp:x8}  (must differ across processes)");

            // (7) W9 THE REPAIR — A SKIRMISH'S FORCE MUST RESPOND TO THE HEAT DIAL.
            // THE GAP: HEATLADDERTEST and OPENERTEST both pin the heat ramp against CAMPAIGN missions
            // (where n>=2 exists), and this test's own skirmish legs only assert PHASE ROUTING —
            // Win/Lose rather than Barracks. Nothing anywhere asserted that a skirmish's fielded
            // force reads _run.HeatLevel at all. The flywheel covers campaign and endless only, so no
            // measured rung has ever included a skirmish. Result: SetupMission's `n <= 1` grace, which
            // exists to protect a green CAMPAIGN opener, zeroed the entire numeric ladder in two of
            // the four shipped modes — measured at 4 HOSTILES on both heat 0 and heat 8, with the red
            // HEAT 8 chip the only difference on screen.
            // Same seed + same arena on both rungs, so the ONLY variable is the dial.
            NoPersist = true;
            int ForceAt(int heat)
            {
                Util.Reseed(4242);
                Sightline.Mission.ForcedLayout = 5;
                BeginSkirmish(Objective.Eliminate, heat);
                return Enemies.Count(e => e.Alive);
            }
            int fCold = ForceAt(0), fHot = ForceAt(Sightline.Heat.Max);
            Sightline.Mission.ForcedLayout = -1;
            if (fHot <= fCold) fails.Add($"skirmishHeatInert h0={fCold} h{Sightline.Heat.Max}={fHot}");
            // and the CAMPAIGN opener keeps its grace — the whole reason the gate is on MODE, not on n
            int CampaignM1(int heat)
            {
                Util.Reseed(4242);
                Sightline.Mission.ForcedLayout = 5;
                PendingHeat = heat;
                StartMission(1);
                PendingHeat = 0;
                return Enemies.Count(e => e.Alive);
            }
            int cCold = CampaignM1(0), cHot = CampaignM1(Sightline.Heat.Max);
            Sightline.Mission.ForcedLayout = -1;
            if (cHot != cCold) fails.Add($"campaignM1GraceLost h0={cCold} h{Sightline.Heat.Max}={cHot}");

            // (8) THE MODES GET THE BESTIARY — A SKIRMISH'S FORCE MUST BE DRAWN FROM THE ROSTER, NOT
            //     FROM MISSION 1'S TEACHING TIER.
            // THE GAP leg (7) left open: it proved the force's SIZE answers the dial and said nothing
            // about WHAT the force is. Both single-mission modes enter through SetupMission(1), and
            // that n went straight to Mission.Build -> SelectArchetype, whose `n <= 1` branch deals
            // SCOUT or GRUNT and nothing else; the mid-boss slot was `n == 3 || n == 5` and pods of 3
            // `n >= 3`, so neither could ever fire; and a fresh Run sits on its Start node, which
            // GenerateMap leaves Faction.None, so FactionRoster never ran. Measured on the pre-fix
            // tree: 50 heat-0 skirmish builds fielded exactly {GRUNT, SCOUT} — 20 of 22 archetypes,
            // all three factions, pods of 3 and every mid-boss kit unreachable in two of four modes.
            // Fixed seed so the 50 builds are one deterministic sequence, not a probability claim.
            NoPersist = true;
            Util.Reseed(9001);
            var seen = new HashSet<string>();
            int podOf3Builds = 0, coldElites = 0;
            for (int b = 0; b < 50; b++)
            {
                BeginSkirmish(Objective.Eliminate, 0);
                foreach (var e in Enemies) seen.Add(e.Cls);
                coldElites += Enemies.Count(e => e.Cls == "ELITE");
                if (Enemies.GroupBy(e => e.PodId).Any(gp => gp.Count() == 3)) podOf3Builds++;
            }
            var specialists = seen.Where(c => c != "SCOUT" && c != "GRUNT" && c != "ELITE").OrderBy(c => c).ToList();
            if (specialists.Count == 0)
                fails.Add($"skirmishRosterShallow(classes={string.Join("/", seen.OrderBy(c => c))})");
            else if (specialists.Count < 6)
                fails.Add($"skirmishRosterNarrow({specialists.Count}: {string.Join("/", specialists)})");
            // P14 moved this leg off heat 0. The pod PLAN is a function of the headcount: at 4 bodies
            // PodPlan deals {2,2} and a pod of 3 cannot form, so "no pod of 3 at heat 0" is now a
            // property of the arithmetic, not evidence that podsOf3 is off. It is asserted at heat 4
            // (7 bodies -> the pod trim -> 6 -> {3,3}) instead, which is what the leg was ever for.
            int podOf3AtH4 = 0;
            for (int b = 0; b < 20; b++)
            {
                BeginSkirmish(Objective.Eliminate, 4);
                if (Enemies.GroupBy(e => e.PodId).Any(gp => gp.Count() == 3)) podOf3AtH4++;
            }
            if (podOf3AtH4 == 0) fails.Add("skirmishNeverPodsOf3(h4)");
            if (coldElites != 0) fails.Add($"skirmishColdMidBoss({coldElites})");   // heat < 4 fields no named elite
            // heat >= 4: EXACTLY one mid-boss (Cls ELITE) in every build — the slot is i==0, not a roll
            foreach (int hh in new[] { 4, Sightline.Heat.Max })
            {
                int bad = 0;
                for (int b = 0; b < 10; b++)
                {
                    BeginSkirmish(Objective.Eliminate, hh);
                    if (Enemies.Count(e => e.Cls == "ELITE") != 1) bad++;
                }
                if (bad != 0) fails.Add($"skirmishMidBossMissing(h{hh}:{bad}/10)");
            }
            // measured, not inferred: the fielded headcount per rung (same seed + arena as leg 7)
            int Bodies(int hh) { Util.Reseed(4242); Sightline.Mission.ForcedLayout = 5; BeginSkirmish(Objective.Eliminate, hh); return Enemies.Count; }
            int b0 = Bodies(0), b4 = Bodies(4), b8 = Bodies(Sightline.Heat.Max);
            Sightline.Mission.ForcedLayout = -1;
            // LEAD REVIEW: the headcount is PINNED, because the roster opening moved it and that must
            // stay a declared fact. Pre-P4 this seed + arena fielded 4/6/8 at h0/h4/h8; pods of 3
            // bring FUL-6's trim with them (count-1, the pod package as measured in the campaign),
            // so P4 declared it 3/5/7.
            //
            // P14 MOVES IT BACK TO 4/6/8, DELIBERATELY, and this is the body of that decision.
            // The second body P4's note is describing is not the pod trim — it is `Mission.OpenerTrim`,
            // the CAMPAIGN cold-opener grace, which was still firing in both single-mission modes at
            // every rung. W9 had already made the call for the sibling grace ("a SKIRMISH or DAILY
            // player explicitly DIALLED the rung; there is no green squad to protect and no campaign
            // ahead to front-load anxiety into") and did not carry it here. So the trim is now gated
            // on Mission.DepthFor rather than on the raw mission number, the pod trim is untouched,
            // and the modes are back on the headcount they fielded before P4 — a RESTORATION, not an
            // escalation. It also removes the finding that sent this wave here: at 3 bodies the force
            // was ONE pod 40/40 builds and ONE archetype 38/40 (SIGHTLINE_MODEFORCEPROBE); at 4 it is
            // 0/40 and 4/40.
            if (b0 != 4 || b4 != 6 || b8 != 8) fails.Add($"skirmishHeadcountMoved({b0}/{b4}/{b8}, expected 4/6/8)");
            Console.WriteLine($"MODETEST skirmish h0 roster over 50 builds: {string.Join("/", seen.OrderBy(c => c))}  pods-of-3 in {podOf3Builds}/50  bodies h0={b0} h4={b4} h8={b8}");

            // (9) THE DAILY HAS A FACTION, AND THE SAME STAMP DEALS THE SAME FORCE.
            //     A "deterministic date-seeded challenge" whose force was the same two bodies every
            //     day was deterministic in the way a blank page is. The faction is derived from the
            //     day seed like the objective/arena/heat, so it is (a) never None and (b) part of the
            //     signature the second BeginDaily must reproduce — the COMPOSITION, not just the tiles
            //     that leg (2)'s BoardSignature already pins.
            NoPersist = true;
            BeginDaily();
            var facA = Combat.MissionFaction;
            string forceA = ForceSignature();
            BeginDaily();
            var facB = Combat.MissionFaction;
            string forceB = ForceSignature();
            if (facA == Faction.None) fails.Add("dailyFactionNone");
            if (facA != facB) fails.Add($"dailyFactionNonDeterministic({facA}/{facB})");
            if (forceA != forceB) fails.Add("dailyForceNonDeterministic");
            Console.WriteLine($"MODETEST daily {DailyStamp} faction={facA} force={forceA}  (must match across processes)");

            // (10) P12 THE CONFIRMED EIGHT (C1) — AN ENDED RUN'S MODE DIES AT *EVERY* END-CARD DOOR.
            // The end card has TWO exits that land on the main menu (MAIN MENU [Esc] and WAR ROOM
            // [W] -> BACK) and only the first cleared `Mode`. Take the other one after a TRAINING
            // OP and you arrive on Phase.Intro with Mode == Training; the intro's DEPLOY SQUAD
            // plate is the ONE door dispatched outside the ActIntro table, so it fell through to
            // ActPrimary's "re-run the drill" branch — the main menu's primary verb, captioned
            // "NEW CAMPAIGN - draft a squad...", launched the drill instead. Both doors, both
            // asserted, and the assertion is pressed through the same methods the player's click
            // dispatches to.
            NoPersist = true;
            foreach (bool viaWarRoom in new[] { true, false })
            {
                string door = viaWarRoom ? "warroom" : "mainmenu";
                BeginTraining();
                if (Mode != GameMode.Training) fails.Add($"endCard:{door}:trainingModeNotSet");
                EndTraining(true);
                if (Phase != Phase.Win) fails.Add($"endCard:{door}:trainingWinCardNotSet");
                if (viaWarRoom)
                {
                    ActEndWarRoom();
                    if (Phase != Phase.WarRoom) fails.Add("endCard:warroom:doorDidNotOpen");
                    ExitWarRoom();
                }
                else ActEndMainMenu();
                if (Phase != Phase.Intro) fails.Add($"endCard:{door}:landsOn:{Phase}");
                if (Mode != GameMode.Campaign) fails.Add($"endCard:{door}:leakedMode({Mode})");
                if (DailyMode) fails.Add($"endCard:{door}:leakedDailyFlag");
                // ...and the intro's PRIMARY verb (the DEPLOY SQUAD plate / [Enter]) opens a
                // CAMPAIGN rather than re-running the drill.
                ActPrimary();
                if (Mode != GameMode.Campaign) fails.Add($"endCard:{door}:introDeployLaunched({Mode})");
            }

            // (11) P12 THE CONFIRMED EIGHT (C6) — WHAT A SCREEN ADVERTISES, THE MANUAL CARRIES.
            // THE MODES GET THE BESTIARY added the [TAB] faction dial to SKIRMISH SETUP and painted
            // it into that screen's own legend and blurb — and left Hud.KeyTable, the SINGLE source
            // for the in-game FIELD MANUAL's VERBS & KEYS tab and for README's generated controls
            // block, on its pre-wave row. The screen therefore advertised a binding the manual a
            // player opens in-game did not enumerate, for a whole program. Note the gate has to be
            // SCOPED TO THE SCREEN'S OWN ROW: "Tab" already appears in an unrelated in-mission row
            // ("cycle to the next soldier"), so an any-row membership check would have passed on
            // the broken tree.
            var (skIn, skAct) = Hud.KeyTableRow("SKIRMISH SETUP");
            if (skIn == null) fails.Add("keyTable:noSkirmishSetupRow");
            else
                foreach (var (key, what, manual) in Hud.SkirmishLegend)
                {
                    if (!skIn.Contains(manual)) fails.Add($"keyTable:skirmishSetupRowOmits[{key}]as'{manual}'");
                    if (!skAct.Contains(what)) fails.Add($"keyTable:skirmishSetupActionOmits'{what}'");
                }

            // ── (12) P14 THE UNVERIFIED — EVERY CONSUMER OF "HOW DEEP IS THIS FIGHT" ANSWERS THE DIAL.
            //  P4 threaded a `rosterTier` into Mission.Build and legs (7)-(8) above pin what it bought:
            //  the force's SIZE and its ARCHETYPES. Four other consumers of the mission number were
            //  left reading the literal 1 these modes pass, and nothing here could see it. Measured
            //  pre-fix with SIGHTLINE_MODEFORCEPROBE and each one asserted below:
            //    * the ESCORT/RESCUE asset  — MakeVip(1): 16 HP / 0 armor from heat 0 to heat 8
            //    * the DECAPITATE HVT bonus — Combat.HvtHpBonus(1): a flat +7 HP on every rung
            //    * the DEFEND reinforcement wave — built at tier 1 with its heat stat hard-zeroed by
            //      the CAMPAIGN's m1-2 grace, so a heat-8 wave was the heat-0 wave (9 HP / 61 aim)
            //    * Mission.OpenerTrim — leg (8)'s headcount pin now carries that one
            //  and the fifth assertion is the one the finding was about: a mode force is never
            //  ENTIRELY IMMOBILE (three SENTRYs and nothing else was 7.5% of heat-0 skirmishes).
            NoPersist = true;
            (int hp, int armor) VipAt(int heat)
            {
                Util.Reseed(4242); Sightline.Mission.ForcedLayout = 5;
                BeginSkirmish(Objective.Escort, heat);
                return Vip == null ? (0, 0) : (Vip.MaxHp, Vip.Armor);
            }
            var vCold = VipAt(0); var vHot = VipAt(Sightline.Heat.Max);
            if (vCold.hp <= 0) fails.Add("skirmishEscortNoVip");
            else if (vHot.hp <= vCold.hp || vHot.armor < vCold.armor)
                fails.Add($"skirmishVipDialInert(h0 {vCold.hp}hp/{vCold.armor}armor h{Sightline.Heat.Max} {vHot.hp}hp/{vHot.armor}armor)");

            int HvtBonusAt(int heat)
            {
                Util.Reseed(4242); Sightline.Mission.ForcedLayout = 5;
                BeginSkirmish(Objective.Decapitate, heat);
                return Hvt == null ? -1 : Combat.HvtHpBonus(_run.Mission);
            }
            int hbCold = HvtBonusAt(0), hbHot = HvtBonusAt(Sightline.Heat.Max);
            if (hbCold < 0 || hbHot < 0) fails.Add("skirmishDecapNoHvt");
            else if (hbHot <= hbCold) fails.Add($"skirmishHvtDialInert(h0 +{hbCold} h{Sightline.Heat.Max} +{hbHot})");

            // the DEFEND wave: bodies AND their statline must both move with the dial
            (int n, int hp) WaveAt(int heat)
            {
                Util.Reseed(4242); Sightline.Mission.ForcedLayout = 5;
                BeginSkirmish(Objective.Defend, heat);
                int before = Enemies.Count;
                _turnCount = 3;
                SpawnDefendWave();
                var w = Enemies.Skip(before).ToList();
                return (w.Count, w.Count == 0 ? 0 : w.Max(e => e.MaxHp));
            }
            var wCold = WaveAt(0); var wHot = WaveAt(Sightline.Heat.Max);
            if (wCold.n == 0 || wHot.n == 0) fails.Add($"skirmishDefendNoWave(h0 {wCold.n} h{Sightline.Heat.Max} {wHot.n})");
            else if (wHot.hp <= wCold.hp)
                fails.Add($"skirmishDefendWaveDialInert(h0 {wCold.n}x{wCold.hp}hp h{Sightline.Heat.Max} {wHot.n}x{wHot.hp}hp)");

            // never an entirely immobile opposition (the SENTRY-only fight)
            int allStatic = 0, singlePod = 0;
            foreach (int hh in new[] { 0, 2 })
            {
                Util.Reseed(7700 + hh);
                for (int b = 0; b < 40; b++)
                {
                    BeginSkirmish(Objective.Eliminate, hh);
                    if (Enemies.Count > 0 && Enemies.All(e => e.Mobility == 0)) allStatic++;
                    if (Enemies.Count > 1 && Enemies.Select(e => e.PodId).Distinct().Count() == 1) singlePod++;
                }
            }
            if (allStatic != 0) fails.Add($"skirmishAllImmobileForce({allStatic}/80)");
            if (singlePod != 0) fails.Add($"skirmishSinglePodForce({singlePod}/80)");
            Sightline.Mission.ForcedLayout = -1;

            // and the CAMPAIGN is untouched by the whole funnel: outside the single-mission modes
            // Mission.ModeDepth is -1, so DepthFor is the identity on the mission number.
            Sightline.Mission.ModeDepth = -1;
            for (int m = 1; m <= Run.MaxMissions; m++)
                if (Sightline.Mission.DepthFor(m) != m) fails.Add($"depthFunnelNotIdentityInCampaign(m{m})");

            // ── (13) P14 — THE DAILY'S CROSS-PROCESS CONTRACT, ASSERTED RATHER THAN PRINTED.
            //  Leg (9) proves the same stamp deals the same force TWICE IN ONE PROCESS and then
            //  PRINTS "(must match across processes)". Nobody was comparing it: the sweep runs
            //  MODETEST once. A same-process check cannot see a force that folds in anything a
            //  fresh process would compute differently, which is the entire failure mode the daily
            //  has. So this leg runs the real thing — a CHILD PROCESS of this same binary with
            //  SIGHTLINE_DAILYSIGPROBE=1, which prints nothing but the signature — and compares.
            //  It touches no player data (the child sets NoPersist), and it is bounded (20 s).
            if (Environment.GetEnvironmentVariable("SIGHTLINE_DAILYSIGPROBE") != "1")
            {
                NoPersist = true;
                BeginDaily();
                string mine = DailyStamp + "|" + (int)Combat.MissionFaction + "|" + ForceSignature() + "|" + BoardSignature();
                string child = RunDailySigChild();
                if (child == null) fails.Add("dailyCrossProcessChildFailed");
                else if (child != mine) fails.Add($"dailyCrossProcessForceDiffers(self={mine} child={child})");
                else Console.WriteLine($"MODETEST daily cross-process: {mine} reproduced in a second process");
            }
        }
        catch (Exception e) { return "MODETEST: FAIL (exception " + e.Message + ")"; }
        return fails.Count == 0
            ? "MODETEST: PASS (daily seed deterministic; skirmish ends single-mission (Win/Lose, not Barracks); daily best round-trips; abandon is mode-aware + campaign-checkpoint-preserving; a skirmish's force answers the heat dial while the campaign's mission-1 grace is untouched; a skirmish fields the full roster, pods of 3 from heat 4 and one mid-boss from heat 4, at a pinned 4/6/8 bodies; the escort asset, the HVT bonus and the DEFEND wave all answer the dial too; no mode force is a single pod or entirely immobile; the depth funnel is the identity in the campaign; and the same daily stamp fields the same force IN A SECOND PROCESS; BOTH end-card doors to the main menu clear an ended run's mode, so the intro's DEPLOY plate opens a campaign and not the drill; and the FIELD MANUAL's SKIRMISH SETUP row names every key that screen's own legend advertises)"
            : "MODETEST: FAIL (" + string.Join(",", fails) + ")";
    }
}
