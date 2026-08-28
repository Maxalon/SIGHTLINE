using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

// PROGRAM HORIZON — Wave 2: LAST STAND endless horde survival.
//
// A NEW game mode that reuses the whole tactical kernel (Grid/Combat/Ai/anims/HUD) but swaps the
// 6-mission campaign shell for an endless wave-survival loop on ONE arena. The squad fights
// escalating waves of hostiles until wiped; the game reports WAVES SURVIVED and persists a BEST
// WAVE across sessions (meta.json, append-only, gated by NoPersist like all other meta I/O).
//
// Design notes:
//  - Reuses the battle-tested SpawnReinforcements/Mission.MakeWaveHostile spawner (already proven
//    by the Defend objective + the anti-turtle pressure clock).
//  - Endless is TRANSIENT: it never writes save.json (not resumable). Only the BestWave meta is
//    persisted. Everything else (Wave, squad HP) lives in memory for the session.
//  - The campaign checkpoint valve (TryReinforcements) MUST NOT fire in endless — a wipe simply
//    ends the run. This is enforced by CheckEnd routing to CheckEndless BEFORE the wipe branch.
//  - The core Game.cs edits are intentionally tiny (class->partial, a GameMode enum + Mode/Wave
//    fields, a CheckEnd branch, a few `Mode != Endless` guards in SetupMission, an intro key/button
//    hook). All the mode's logic lives here.
public partial class Game
{
    // Tuning knobs for the horde escalation.
    const int EndlessAliveCap = 13;   // max live hostiles on the board at once (perf + fairness)
    const int EndlessBaseCount = 0;   // flat offset on the body ramp (2 -> 0 in W7 round 2: the flywheel
                                      // put every pre-W7 stand's death at the wave-3/4 body cliff —
                                      // 5-6 hostiles vs the 4-soldier squad before any progression
                                      // beat landed)
    const int EndlessSaturationWave = 20;   // past this: heal decays + one extra ELITE per wave (APEX W7 "an ending")

    // APEX W7 — wave-clear latch. The between-wave sustain (heal/ammo/grenade resupply) and the
    // progression heartbeat must fire exactly ONCE per cleared wave, but the mid-stand Barracks
    // detour splits "wave cleared" from "next wave spawned" across frames. Set when the sustain
    // fires; re-armed by SpawnEndlessWave when the next wave actually lands, so no path — detour
    // or straight-through — can double-fire the heal/resupply.
    bool _waveClearHandled;

    /// Begin LAST STAND from the intro. Builds the default squad on a fresh Run (no draft), adopts
    /// the dialled-in Heat, seats the arena, and starts the first wave. Mirrors StartMission's
    /// setup contract (EnsureMetaLoaded, heat from PendingHeat / SIGHTLINE_HEAT under NoPersist).
    public void BeginEndless()
    {
        ResetModeState();   // W1 mode-seam: inherit nothing from a prior mode (daily seed/arena, flags)
        Mode = GameMode.Endless;
        EnsureMetaLoaded();
        _run = new Run();
        _run.Start();                       // default founding squad; builds a campaign map we ignore
        // adopt Heat exactly like StartMission (the harness can't touch the intro, so it reads
        // SIGHTLINE_HEAT here under NoPersist; default 0 keeps a plain run byte-stable).
        int heat = PendingHeat;
        if (NoPersist && int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_HEAT"), out int hEnv)) heat = hEnv;
        _run.HeatLevel = Sightline.Heat.Clamp(heat);
        _run.LossStreak = _metaLossStreak;
        // balance telemetry tag (no-op unless Stats.Enabled). APEX W4: the MODE ("endless") lives
        // in RunRec.Mode, not the policy slot — so endless stands split greedy/sloppy exactly like
        // the campaign batch AND are excluded from campaign policy-gap/completion math by an
        // explicit Mode filter in Stats, not by tag-string accident.
        Stats.BeginRun(_run.HeatLevel, SmartPlay && SmartSloppy ? "sloppy" : "greedy", "endless");
        Players = _run.Squad;
        Wave = 0;
        // Build the arena + spawn wave 1. SetupMission branches on Mode==Endless: it forces
        // Eliminate, starts unconcealed, skips objective/secondary/pressure setup, and calls
        // SpawnEndlessWave(1) after clearing the campaign force. It leaves Phase = PlayerTurn.
        SetupMission(1);
    }

    /// Spawn wave `w`: an escalating, already-engaged hostile force at the board edges. Reuses the
    /// SpawnReinforcements/MakeWaveHostile machinery. Count rises with the wave (capped by the
    /// alive-cap for perf/fairness); difficulty escalates by feeding a rising stat "bump" into
    /// MakeWaveHostile's mission-scaling (tougher bodies as the horde deepens). Sets Wave = w.
    void SpawnEndlessWave(int w)
    {
        Wave = w;
        _waveClearHandled = false;   // re-arm the wave-clear latch for THIS wave's clear (APEX W7)
        // W6b — the endless path raises the AI coordination tier as the stand deepens (wave 8
        // -> tier 1, wave 16 -> tier 2), never dropping below the run's Heat-derived tier
        // (SetupMission published that at stand start). LAST STAND always ends via EndEndless
        // -> Combat.EndRun -> EndMission, which clears the tier, so a deep stand can never
        // leak Tier 2 into a later mode. Waves 1-7 at heat < 6 stay tier 0 (harness-stable).
        Ai.Tier = Math.Max(Sightline.Heat.AiTier(_run?.HeatLevel ?? 0), w >= 16 ? 2 : w >= 8 ? 1 : 0);
        int want = EndlessWaveCount(w);
        // Escalate body toughness: MakeWaveHostile scales its stats off a "missionNum"-like arg
        // (bump = n-1). Ramp that with the wave AND with Heat so the horde gets meaner over time.
        int scaleN = EndlessWaveScale(w);
        SpawnEndlessBodies(want, scaleN);
        // APEX W7 "an ending": under the W7-era ramp every scaling lever saturated by ~wave 22
        // (bump cap 12, count cap, aim clamp 88, roster depth cap 6) while the between-wave heal
        // kept coming, so a stand past that point used to be a flat immortal equilibrium. The
        // post-APEX toughness retune (see EndlessWaveScale) pushes bump-cap saturation past the
        // measurement wave-cap, which makes this ending MORE binding, not less: one extra ELITE
        // per wave past 20 (paired with the heal decay in CheckEndless) makes deep stands
        // statistically terminate regardless of where the stat ramp tops out. Injected AFTER the rank-and-file fill and deliberately allowed to exceed
        // the alive-cap by this one body — the cap is a perf/fairness valve for the horde, and
        // the ending's escalation must never be silently swallowed by a full board.
        if (w > EndlessSaturationWave) SpawnEndlessElite(scaleN);
        ShowBanner($"WAVE {w}", false);
        Audio.PlayStinger("kill");   // a short escalation cue as the next wave crashes in
    }

    /// The full (un-graced) body count for wave `w`. W7 flywheel retune (measured rounds at
    /// slopes +1/wave, +2/3/wave): the old +1 body/wave slope put every measured stand's death
    /// at the first waves the squad was outnumbered — squad power grows sublinearly, so a steep
    /// linear body ramp always outran it by ~wave 5 no matter where the curve started. The ramp
    /// now adds a body every 2 waves, reaching the alive-cap at wave ~24; escalation past that is
    /// toughness (EndlessWaveScale), the deepening AI tier (W6b) and the post-saturation ELITE
    /// injections, not body count.
    static int EndlessWaveCountFull(int w) => Math.Min(EndlessAliveCap, EndlessBaseCount + 1 + w / 2);

    /// How many bodies wave `w` wants (before the alive-cap clamp inside the spawner).
    /// APEX W7 OPENER GRACE: waves 1-2 arrive at HALF strength (never zero) — pre-W7 the full
    /// count landed the instant the board cleared; the opener now teaches the arena before the
    /// horde arrives in force (full count from wave 3 on).
    static int EndlessWaveCount(int w)
        => w <= 2 ? Math.Max(1, EndlessWaveCountFull(w) / 2) : EndlessWaveCountFull(w);

    /// The mission-scale ("missionNum"-like) value fed to MakeWaveHostile for wave `w`. Ramps ~1
    /// per FOUR waves plus a softened Heat bump, so bodies get tougher as the horde deepens.
    /// Post-APEX followup: W7 tuned the BODY ramp and left this toughness ramp at the original
    /// 1 + w/2 + StatDelta; the overall greedy depth median landed at 5 vs the 6-8 target.
    /// Measured tuning (32-stand batches): a plain slope cut (w/3) was a NULL result — at the
    /// heat-0 death window (waves 4-7) a −1 tier never crosses a hits-to-kill threshold (a
    /// 4-avg rifle 2-shots a 6 or 7 HP scout alike). The binding term was HEAT: StatDelta 2-4
    /// pushed common bodies past 8 HP into 3-shot territory from wave 1, dragging the h2-h8
    /// stands (3/4 of the blend) down. So: slope w/4 AND the heat term halved ROUNDED UP —
    /// {0,1,2,3,4} -> {0,1,1,2,2} keeps every rung's toughness ordered while heat keeps its
    /// distinct teeth via Ai.Tier 1/2 at rungs 6/8 (W6b), TighterContact, and NO QUARTER's
    /// +1 dmg. Endless-only by construction: the sole callers are SpawnEndlessWave/
    /// SpawnEndlessElite — Defend's rich waves feed _run.Mission into MakeWaveHostile and
    /// never see this curve.
    int EndlessWaveScale(int w) => 1 + w / 4 + (Sightline.Heat.StatDelta(_run?.HeatLevel ?? 0) + 1) / 2;

    /// Drop up to `want` active wave-hostiles in from the board edges (already engaged), honoring
    /// the alive-cap. Modeled on SpawnReinforcements, but (a) spawns from BOTH the left and right
    /// edges so a horde can pincer the squad, and (b) uses a caller-supplied scale so difficulty
    /// tracks the wave, not _run.Mission. Returns how many it actually added.
    int SpawnEndlessBodies(int want, int scaleN)
    {
        int added = 0;
        var landed = new List<Unit>();   // FUL-6: collected for the wave-pod split below
        // spawn from the RIGHT edge only (the squad deploys far-left at cols 1-2, so left-edge spawns
        // would drop hostiles point-blank on the squad — unfair). Matches the reinforcement pattern.
        int[] cols = { Grid.W - 2, Grid.W - 1 };
        var rows = Enumerable.Range(0, Grid.H).OrderBy(_ => Util.RandF()).ToList();
        foreach (int y in rows)
        {
            if (added >= want || AliveEnemies().Count >= EndlessAliveCap) break;
            int placeX = -1;
            foreach (int cx in cols)
                if (Grid.IsFloor(cx, y) && !IsOccupiedByOther(cx, y, null)) { placeX = cx; break; }
            if (placeX < 0) continue;
            // Full-roster horde variety scaled by the wave tier (snipers/shields/drones/berserkers/
            // siege deepen the swarm as it escalates). SelectArchetype (via MakeEndlessHostile) already
            // ramps toughness with the tier; no manual body-swap needed.
            var e = Mission.MakeEndlessHostile(scaleN, placeX, y);
            e.Alert = AlertLevel.Alert; e.PodId = -1;   // arrives engaged; FUL-6 assigns a morale pod below
            e.SyncPos();
            e.BeginTurn(); e.OnOverwatch = false;
            Stats.RecordSpawn(e.Cls, Combat.MissionFaction != Faction.None);   // APEX W5 composition tally
            Enemies.Add(e);
            landed.Add(e);
            Fx.Burst(e.Pos, Pal.Foe, 14, 160f, 0.5f, 3f, true);
            added++;
        }
        // FUL-6 CRITICAL MASS — morale reaches the horde: split each wave's LANDED bodies into
        // sub-pods via the shared Mission.PodPlan (ids _nextWavePod++ per sub-pod, 100+ so they
        // can never collide with the campaign's i/2 pods or harness scenes; _podOrig sealed to
        // what landed — the FUL-4 SpawnReinforcements seal pattern), so killing a sub-pod down
        // routs its survivors mid-stand. Bodies still spawn fully Alert (no dormant pods), so
        // the linked-activation rider stays inert here by construction. SpawnEndlessElite keeps
        // PodId = -1 — the "ending" must not be routable (HORDETEST's elitePodJoined pin).
        if (added > 0)
        {
            int[] plan = Mission.PodPlan(added);
            for (int p = 0, idx = 0; p < plan.Length; p++)
            {
                int id = _nextWavePod++;
                for (int m = 0; m < plan[p] && idx < landed.Count; m++, idx++) landed[idx].PodId = id;
                _podOrig[id] = plan[p];
            }
        }
        if (added > 0) RefreshCombatRoster();   // expose the grown roster to Combat.ComputeOdds
        return added;
    }

    /// LAST STAND end/advance check (called from CheckEnd instead of the campaign objective ladder).
    /// A squad wipe ends the run; clearing every hostile refills the survivors + spawns the next wave.
    void CheckEndless()
    {
        if (AlivePlayers().Count == 0) { EndEndless(); return; }
        // wave cleared: all hostiles down and the board is idle (no anims mid-death).
        if (AliveEnemies().Count == 0 && _anims.Count == 0 && !_waveClearHandled)
        {
            _waveClearHandled = true;   // exactly once per cleared wave (see the field's comment)
            // BETWEEN-WAVE SUSTAIN: refill ammo + grenades and heal each survivor a chunk, so a
            // long stand is about attrition/positioning, not a slow bleed to zero.
            foreach (var u in Players)
            {
                if (!u.Alive) continue;
                u.Ammo = u.Weapon.Clip;
                // grenade resupply every 3rd wave (a breather between escalations)
                if ((Wave + 1) % 3 == 0) u.Grenades = Math.Max(u.Grenades, 1 + u.BonusGrenades + (u.HasPerk(Perk.Bandolier) ? 1 : 0));
                // FUL-7: the breather gets DOWNED survivors back up at the mend value (floored at
                // 1 — deep-stand mend decay must never revive at 0), and the once-per-battle down
                // budget resets with the wave (each wave is a fresh battle; the in-wave anti-
                // revive-tank rule is untouched).
                if (u.Downed)
                {
                    u.Downed = false; u.Stabilized = false; u.DownedTurns = 0;
                    u.Hp = Math.Min(u.MaxHp, Math.Max(1, EndlessWaveHeal(u.MaxHp, Wave)));
                    Stats.RecordDownRecovered();
                    Fx.PopText(u.Pos + new Vector2(0, -30), "BACK UP", Pal.Good, 18f);
                }
                else
                    // clearing a wave mends a meaningful chunk (rewards the clear) — but past bump-
                    // saturation the mend decays toward zero (APEX W7 "an ending": see EndlessWaveHeal)
                    u.Hp = Math.Min(u.MaxHp, u.Hp + EndlessWaveHeal(u.MaxHp, Wave));
                u.WasDownedThisMission = false;
            }
            // APEX W7 PROGRESSION HEARTBEAT: every 3rd cleared wave, banked kills cash in as FIELD
            // PROMOTIONS (the campaign's rank-up perk/spec offers, via the shared Run.PromoteEligible);
            // every 5th, a run-scoped boon offer. If anything is pending, detour through
            // Phase.Barracks — the existing chooser UI + AutoPlay resolution paths run unchanged,
            // and the guard at the TOP of the Barracks case in Game.Update returns here and spawns
            // the next wave the moment every offer is resolved (it can never fall through to the
            // campaign shop/event/node branches).
            bool beatPromote = Wave % 3 == 0;
            bool beatBoon = Wave % 5 == 0;
            if (beatPromote || beatBoon) _run.Report.Clear();   // fresh mid-stand report (no stale debrief)
            if (beatPromote) _run.PromoteEligible();
            if (beatBoon) _run.GenerateBoonOffer(endless: true);   // W1: no structurally-inert picks mid-stand
            if (_run.PendingPerks.Count > 0 || _run.PendingSpecs.Count > 0 || _run.BoonOffer.Count > 0)
            {
                _shopDone = true;         // never the requisition shop mid-stand (offers only)
                Phase = Phase.Barracks;
                Audio.Play("turn");
                return;                   // the Barracks guard spawns the next wave when done
            }
            SpawnEndlessWave(Wave + 1);
        }
    }

    /// The between-wave mend for a survivor with `maxHp` after clearing wave `wave`. Full value
    /// (Max(3, MaxHp/4) — the pre-W7 formula, unchanged through wave 20) until bump-saturation,
    /// then a gradual decay (-1 per 3 waves) to zero, so a deep stand becomes a real attrition
    /// race instead of an immortal equilibrium. Static + pure so HORDETEST can pin the curve.
    static int EndlessWaveHeal(int maxHp, int wave) =>
        Math.Max(0, Math.Max(3, maxHp / 4) - Math.Max(0, (wave - EndlessSaturationWave) / 3));

    /// APEX W7 "an ending": drop ONE elite in from the right edge (same placement contract as
    /// SpawnEndlessBodies). Called only for waves past EndlessSaturationWave; deliberately allowed
    /// to exceed EndlessAliveCap by this one body (see SpawnEndlessWave). PodId=-1 keeps it
    /// morale-exempt like the rest of the horde; grenade load is set explicitly by the maker
    /// (the campaign's ELITE-grenade branch lives in SpawnEnemies, which this path never runs).
    void SpawnEndlessElite(int scaleN)
    {
        int[] cols = { Grid.W - 2, Grid.W - 1 };
        var rows = Enumerable.Range(0, Grid.H).OrderBy(_ => Util.RandF()).ToList();
        foreach (int y in rows)
        {
            foreach (int cx in cols)
            {
                if (!Grid.IsFloor(cx, y) || IsOccupiedByOther(cx, y, null)) continue;
                var e = Mission.MakeEndlessElite(scaleN, cx, y);
                e.Alert = AlertLevel.Alert; e.PodId = -1;   // engaged, morale-exempt (no pod)
                e.SyncPos();
                e.BeginTurn(); e.OnOverwatch = false;
                Stats.RecordSpawn(e.Cls, Combat.MissionFaction != Faction.None);
                Enemies.Add(e);
                Fx.Burst(e.Pos, Pal.Elite, 20, 180f, 0.6f, 3.5f, true);
                RefreshCombatRoster();
                return;
            }
        }
    }

    /// End the LAST STAND run. Record the waves survived, persist the best (append-only meta, gated
    /// by NoPersist), and drop into the shared Lose end card (no campaign side-effects: no save.json
    /// delete, no loss-streak record — endless is separate from the campaign).
    void EndEndless()
    {
        Combat.EndRun();   // clear every mission-scoped combat static (mirrors LoseRun)
        int wavesSurvived = Wave;
        int best = wavesSurvived;
        if (!NoPersist)
        {
            best = Math.Max(SaveGame.LoadMetaBestWave(), wavesSurvived);
            SaveGame.SaveMetaBestWave(best);
            // W3 WAR ROOM: bank endless salvage, enshrine the fallen + surviving squad, check STAND*.
            AwardMetaEndless(wavesSurvived);
        }
        EndlessBestWave = best;
        LoseTitle = "LAST STAND";
        LoseReason = $"Survived {wavesSurvived} wave{(wavesSurvived == 1 ? "" : "s")}.";
        Phase = Phase.Lose;
        Audio.Play("lose");
        Audio.PlayStinger("squadwipe");
        // balance telemetry: close the record (waves survived stands in for missions cleared).
        Stats.EndMission(false, _turnCount, AlivePlayers().Count(p => !p.IsVip),
                         Enemies.Count(e => !e.Alive), "last-stand");
        Stats.EndRun(false, wavesSurvived, "last-stand");
    }

    /// PROGRAM HORIZON W3 (WAR ROOM): bank endless salvage + enshrine legends + check STAND achievements.
    /// Called only from EndEndless under !NoPersist (the guard is at the call site + re-asserted here).
    void AwardMetaEndless(int wavesSurvived)
    {
        if (NoPersist || _run == null) return;
        int heat = _run.HeatLevel;
        // 1) SALVAGE — scales with depth + heat
        int salvage = 3 * wavesSurvived + 3 * heat;
        if (salvage > 0) { SaveGame.AddSalvage(salvage); _run.Report.Insert(0, $"SALVAGE +{salvage}"); EndSalvage = salvage; }   // FUL-12: end-card slab field

        // 2) HALL OF FAME — the fallen (KIA) + any survivors (Won iff it was a deep stand, wave>=10).
        var legends = new List<SaveGame.LegendDto>();
        bool deepStand = wavesSurvived >= 10;
        foreach (var u in _run.Squad)
            if (u.Alive && !u.IsVip)
                legends.Add(new SaveGame.LegendDto { Name = u.FullName, Cls = u.Cls, Rank = u.RankName, Kills = u.Kills, Heat = heat, Won = deepStand });
        foreach (var f in _run.Memorial)
            legends.Add(new SaveGame.LegendDto { Name = f.Name, Cls = f.Cls, Rank = f.Rank, Kills = f.Kills, Heat = heat, Won = false });
        if (legends.Count > 0) SaveGame.AddLegends(legends);

        // 3) STAND achievements (one-time salvage bounty on first unlock)
        if (wavesSurvived >= 5) TryAchievement("STAND5");
        if (wavesSurvived >= 10) TryAchievement("STAND10");
    }

    // Cached BEST WAVE for HUD/end-card readouts. Loaded lazily (once) so the intro can show it
    // without disk churn each frame; refreshed on run end. -1 = not yet loaded.
    int _endlessBest = -1;
    public int EndlessBestWave
    {
        get
        {
            if (_endlessBest < 0) _endlessBest = NoPersist ? 0 : SaveGame.LoadMetaBestWave();
            return _endlessBest;
        }
        private set => _endlessBest = value;
    }

    /// Top-bar readout for LAST STAND (replaces the objective text). e.g. "WAVE 4   BEST 7".
    public string EndlessHud
    {
        get
        {
            int best = EndlessBestWave;
            return best > 0 ? $"WAVE {Wave}   BEST {best}" : $"WAVE {Wave}";
        }
    }

    // ── HORDETEST self-test (SIGHTLINE_HORDETEST): asserts the wave escalation is monotone + capped,
    // that a deep stand never inherits the campaign pressure clock (APEX W1), and that the meta
    // BestWave round-trips. A tiny 64x64 window is created by the harness so the tile math in
    // SpawnEndless* is valid. Returns a one-line report. ──────────────────────────
    public string HordeSelfTest()
    {
        var fails = new List<string>();
        try
        {
            // (1) wave count escalates monotonically and never exceeds the alive cap.
            int prev = -1;
            for (int w = 1; w <= 40; w++)
            {
                int c = EndlessWaveCount(w);
                if (c < prev) fails.Add($"countNonMonotone@w{w}");
                if (c > EndlessAliveCap) fails.Add($"countOverCap@w{w}");
                prev = c;
            }
            // early ramp: never shrinking through the opener grace, and strictly rising into the
            // first full-count wave (w3). (Pre-W7 this asserted w1 < w2 strictly; the grace
            // halves both opener waves, so at a low EndlessBaseCount they legitimately plateau.)
            if (EndlessWaveCount(1) < 1) fails.Add("wave1WantsNothing");
            if (EndlessWaveCount(1) > EndlessWaveCount(2)) fails.Add("countShrinksEarly");
            if (EndlessWaveCount(2) >= EndlessWaveCount(3)) fails.Add("countNotRisingEarly");
            // late waves should be pinned at the cap (escalation moves to toughness).
            if (EndlessWaveCount(40) != EndlessAliveCap) fails.Add("lateNotCapped");
            // (1b) APEX W7 OPENER GRACE: waves 1-2 arrive at half strength; full count from wave 3.
            if (EndlessWaveCount(1) != Math.Max(1, EndlessWaveCountFull(1) / 2)) fails.Add("noOpenerGrace@w1");
            if (EndlessWaveCount(2) != Math.Max(1, EndlessWaveCountFull(2) / 2)) fails.Add("noOpenerGrace@w2");
            if (EndlessWaveCount(3) != EndlessWaveCountFull(3)) fails.Add("graceLeaks@w3");

            // (2) toughness scale escalates monotonically with the wave (Heat 0 baseline).
            NoPersist = true;   // never touch disk in the scale-read path
            _run = new Run(); _run.Start(); _run.HeatLevel = 0;
            int ps = -1;
            for (int w = 1; w <= 40; w++)
            {
                int s = EndlessWaveScale(w);
                if (s < ps) fails.Add($"scaleNonMonotone@w{w}");
                ps = s;
            }
            if (EndlessWaveScale(40) <= EndlessWaveScale(1)) fails.Add("scaleNotRising");

            // (3) an actual spawn respects the alive cap and produces live, engaged enemies.
            Mode = GameMode.Endless;
            Players = _run.Squad;
            Wave = 0;
            SetupMission(1);                                   // arena + wave 1
            if (Wave != 1) fails.Add("wave1NotSet");
            if (AliveEnemies().Count == 0) fails.Add("wave1Empty");
            if (AliveEnemies().Count > EndlessAliveCap) fails.Add("wave1OverCap");
            if (AliveEnemies().Any(e => !e.Active)) fails.Add("wave1HasDormant");
            // force many waves' worth of bodies onto the board; the cap must hold.
            for (int i = 0; i < 30; i++) SpawnEndlessBodies(EndlessAliveCap, 5);
            if (AliveEnemies().Count > EndlessAliveCap) fails.Add("spawnBreaksCap");

            // (3b) APEX W1: LAST STAND must never inherit the campaign anti-turtle pressure clock.
            // Endless forces Eliminate (a clock objective) and never resets _turnCount, so a deep
            // stand used to accrue a permanent +12..+16 hidden enemy aim ramp + phantom campaign
            // reinforcement waves. Poison the statics, run the clock deep into a stand: the
            // Mode != Endless gate in PressureClockObjective must zero both and spawn nothing.
            _turnCount = 12;                       // well past grace (4) + enough steps for max rung
            Pressure = 3; Combat.PressureAim = 9;
            int foesBefore = AliveEnemies().Count;
            UpdatePressure();
            if (Pressure != 0) fails.Add($"endlessPressure={Pressure}");
            if (Combat.PressureAim != 0) fails.Add($"endlessPressureAim={Combat.PressureAim}");
            if (AliveEnemies().Count != foesBefore) fails.Add("endlessPhantomWave");

            // (3c) APEX W7 PROGRESSION HEARTBEAT: banked kills field-promote at the wave-3
            // boundary and the offer resolves HEADLESSLY through the Phase.Barracks detour —
            // the top-of-case guard must return the game to PlayerTurn AND spawn the next wave,
            // never falling through to the campaign node-pick (Run.Start built a real map, so a
            // fall-through would ChooseNode into a campaign mission from inside the stand).
            foreach (var e in Enemies) { e.Hp = 0; e.Alive = false; }   // wave "cleared"
            var vet = _run.Squad[0];                    // VEGA (ASSAULT): 2-fork spec table
            vet.Kills = 3;                              // banks ROOKIE -> CORPORAL (KillReq 1, 3)
            vet.Hp = 1;
            int rank0 = vet.Rank, mission0 = _run.Mission;
            int expectHp = Math.Min(vet.MaxHp, 1 + Math.Max(3, vet.MaxHp / 4));   // one sustain, full pre-saturation heal
            Wave = 3;                                   // the every-3rd-cleared-wave beat
            _waveClearHandled = false;
            AutoPlay = true;                            // offers must resolve via the EXISTING autoplay paths
            CheckEndless();                             // sustain once + PromoteEligible -> detour
            if (Phase != Phase.Barracks) fails.Add("noBarracksDetour");
            if (vet.Rank < 2) fails.Add($"noFieldPromotion(rank={vet.Rank})");
            if (_run.PendingPerks.Count == 0) fails.Add("noPerkOfferQueued");
            if (_run.PendingSpecs.Count == 0) fails.Add("noSpecOfferQueued");
            if (vet.Hp != expectHp) fails.Add($"sustainHeal={vet.Hp}(want{expectHp})");
            CheckEndless();                             // stray second call mid-detour: latched -> full no-op
            if (vet.Hp != expectHp) fails.Add("sustainDoubleFire");
            if (AliveEnemies().Count != 0) fails.Add("strayMidDetourSpawn");
            int pump = 0;
            while (Phase == Phase.Barracks && pump++ < 600) Update(1f / 60f);
            AutoPlay = false;
            if (Phase != Phase.PlayerTurn) fails.Add($"detourStuck(phase={Phase})");
            if (Wave != 4) fails.Add($"detourWave={Wave}(want4)");
            if (AliveEnemies().Count == 0) fails.Add("detourNoNextWave");
            if (_run.Mission != mission0) fails.Add("detourNodePickFired");
            if (Mode != GameMode.Endless) fails.Add($"detourModeLeak={Mode}");
            if (_run.PendingPerks.Count > 0 || _run.PendingSpecs.Count > 0 || _run.BoonOffer.Count > 0)
                fails.Add("offersUnresolved");
            if (vet.Rank <= rank0) fails.Add("rankLost");

            // (3c2) W1 mode-seam: a MID-STAND BOON PICK must reach the static combat reads. Stage the
            // wave-5 boon beat with a pinned FORTIFIED offer, resolve it through the detour, and
            // assert Combat.RunBoons carries it the moment the next wave spawns (pre-W1 nothing
            // republished between waves, so the pick was cosmetic until the stand ended). Also pin
            // the endless offer filter: GHOST / RAPID DEPLOY are structurally inert mid-stand.
            foreach (var e in Enemies) { e.Hp = 0; e.Alive = false; }
            Wave = 5;                                   // the every-5th-cleared-wave boon beat
            _waveClearHandled = false;
            AutoPlay = true;
            CheckEndless();                             // queues a boon offer -> Barracks detour
            if (Phase != Phase.Barracks) fails.Add("noBoonDetour");
            if (_run.BoonOffer.Count == 0) fails.Add("noBoonOffered");
            if (_run.BoonOffer.Contains(Boon.Ghost) || _run.BoonOffer.Contains(Boon.RapidDeploy))
                fails.Add("inertBoonOffered");
            _run.BoonOffer.Clear(); _run.BoonOffer.Add(Boon.Fortified);   // pin the autoplay pick
            pump = 0;
            while (Phase == Phase.Barracks && pump++ < 600) Update(1f / 60f);
            AutoPlay = false;
            if (Phase != Phase.PlayerTurn) fails.Add($"boonDetourStuck(phase={Phase})");
            if (!_run.HasBoon(Boon.Fortified)) fails.Add("fortifiedNotAdopted");
            if (!Combat.RunBoons.Contains(Boon.Fortified)) fails.Add("fortifiedNotPublished");
            for (int i = 0; i < 20; i++)                // the filter holds across many rolls
            {
                _run.GenerateBoonOffer(endless: true);
                if (_run.BoonOffer.Contains(Boon.Ghost) || _run.BoonOffer.Contains(Boon.RapidDeploy))
                { fails.Add("inertBoonRolled"); break; }
            }
            _run.BoonOffer.Clear();                     // leave no live offer for the later legs

            // (3d) APEX W7 "an ending": waves past saturation inject one extra ELITE (morale-
            // exempt PodId=-1, explicit grenade load) and the between-wave mend decays to zero.
            foreach (var e in Enemies) { e.Hp = 0; e.Alive = false; }
            SpawnEndlessWave(EndlessSaturationWave + 1);
            var elite = AliveEnemies().FirstOrDefault(e => e.Cls == "ELITE");
            if (elite == null) fails.Add("noEliteAt21");
            else
            {
                if (elite.PodId != -1) fails.Add("elitePodJoined");
                if (elite.Grenades != 1) fails.Add($"eliteGrenades={elite.Grenades}(want1)");
            }
            foreach (var e in Enemies) { e.Hp = 0; e.Alive = false; }
            SpawnEndlessWave(5);                        // pre-saturation wave: NO elite injected
            if (AliveEnemies().Any(e => e.Cls == "ELITE")) fails.Add("eliteBeforeSaturation");
            foreach (int mhp in new[] { 8, 16 })        // rookie + veteran HP pools
            {
                if (EndlessWaveHeal(mhp, EndlessSaturationWave) != Math.Max(3, mhp / 4)) fails.Add($"healDecaysEarly@hp{mhp}");
                int prevHeal = int.MaxValue;
                for (int w = 1; w <= 40; w++)
                {
                    int h = EndlessWaveHeal(mhp, w);
                    if (h > prevHeal) fails.Add($"healNonMonotone@w{w}");
                    prevHeal = h;
                }
                if (EndlessWaveHeal(mhp, 40) != 0) fails.Add($"healNeverZero@hp{mhp}");
            }

            // (4) meta BestWave round-trips (read-modify-write, append-only). Preserve any real meta.
            NoPersist = false;
            string metaSaved = System.IO.File.Exists(SaveGame.MetaPathPublic)
                ? System.IO.File.ReadAllText(SaveGame.MetaPathPublic) : null;
            try
            {
                SaveGame.SaveMetaBestWave(17);
                if (SaveGame.LoadMetaBestWave() != 17) fails.Add("metaBestWaveRoundTrip");
                // a lower write must NOT be clamped up, but our EndEndless takes Max — verify the
                // raw setter stores what it's given (Max is applied at the call site).
                SaveGame.SaveMetaBestWave(9);
                if (SaveGame.LoadMetaBestWave() != 9) fails.Add("metaBestWaveOverwrite");
                // and that it doesn't clobber the heat field (append-only, whole-DTO r-m-w).
                SaveGame.SaveMetaHeat(3);
                SaveGame.SaveMetaBestWave(21);
                if (SaveGame.LoadMetaHeat() != 3) fails.Add("metaHeatClobbered");
            }
            finally
            {
                if (metaSaved != null) { try { System.IO.File.WriteAllText(SaveGame.MetaPathPublic, metaSaved); } catch { } }
                else { try { if (System.IO.File.Exists(SaveGame.MetaPathPublic)) System.IO.File.Delete(SaveGame.MetaPathPublic); } catch { } }
            }
        }
        catch (Exception e) { return "HORDETEST: FAIL (exception " + e.Message + ")"; }
        return fails.Count == 0
            ? "HORDETEST: PASS (wave count/scale escalate + alive-cap holds; opener grace; endless skips the pressure clock; mid-stand promotions resolve through the Barracks detour w/o double-sustain or node-picks; mid-stand boon picks republish to Combat.RunBoons + inert boons filtered; deep waves inject an ELITE + heal decays to zero; meta BestWave round-trips)"
            : "HORDETEST: FAIL (" + string.Join(",", fails) + ")";
    }
}
