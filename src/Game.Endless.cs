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
    const int EndlessBaseCount = 2;   // wave 1 spawns ~ EndlessBaseCount + wave bodies (gentle opener)

    /// Begin LAST STAND from the intro. Builds the default squad on a fresh Run (no draft), adopts
    /// the dialled-in Heat, seats the arena, and starts the first wave. Mirrors StartMission's
    /// setup contract (EnsureMetaLoaded, heat from PendingHeat / SIGHTLINE_HEAT under NoPersist).
    public void BeginEndless()
    {
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
        // balance telemetry tag (no-op unless Stats.Enabled)
        Stats.BeginRun(_run.HeatLevel, "endless");
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
        int want = EndlessWaveCount(w);
        // Escalate body toughness: MakeWaveHostile scales its stats off a "missionNum"-like arg
        // (bump = n-1). Ramp that with the wave AND with Heat so the horde gets meaner over time.
        int scaleN = EndlessWaveScale(w);
        SpawnEndlessBodies(want, scaleN);
        ShowBanner($"WAVE {w}", false);
        Audio.PlayStinger("kill");   // a short escalation cue as the next wave crashes in
    }

    /// How many bodies wave `w` wants (before the alive-cap clamp). Rises linearly, then flattens
    /// near the cap so late waves don't just pile bodies (they get tougher instead — EndlessWaveScale).
    static int EndlessWaveCount(int w) => Math.Min(EndlessAliveCap, EndlessBaseCount + w);

    /// The mission-scale ("missionNum"-like) value fed to MakeWaveHostile for wave `w`. Ramps ~1
    /// per two waves plus a Heat bump, so bodies get tougher as the horde deepens.
    int EndlessWaveScale(int w) => 1 + w / 2 + Sightline.Heat.StatDelta(_run?.HeatLevel ?? 0);

    /// Drop up to `want` active wave-hostiles in from the board edges (already engaged), honoring
    /// the alive-cap. Modeled on SpawnReinforcements, but (a) spawns from BOTH the left and right
    /// edges so a horde can pincer the squad, and (b) uses a caller-supplied scale so difficulty
    /// tracks the wave, not _run.Mission. Returns how many it actually added.
    int SpawnEndlessBodies(int want, int scaleN)
    {
        int added = 0;
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
            e.Alert = AlertLevel.Alert; e.PodId = -1;   // horde arrives already engaged (no pods)
            e.SyncPos();
            e.BeginTurn(); e.OnOverwatch = false;
            Enemies.Add(e);
            Fx.Burst(e.Pos, Pal.Foe, 14, 160f, 0.5f, 3f, true);
            added++;
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
        if (AliveEnemies().Count == 0 && _anims.Count == 0)
        {
            // BETWEEN-WAVE SUSTAIN: refill ammo + grenades and heal each survivor a little (+2, cap
            // MaxHp), so a long stand is about attrition/positioning, not a slow bleed to zero.
            foreach (var u in Players)
            {
                if (!u.Alive) continue;
                u.Ammo = u.Weapon.Clip;
                // grenade resupply every 3rd wave (a breather between escalations)
                if ((Wave + 1) % 3 == 0) u.Grenades = Math.Max(u.Grenades, 1 + u.BonusGrenades + (u.HasPerk(Perk.Bandolier) ? 1 : 0));
                // clearing a wave mends a meaningful chunk (rewards the clear; escalation still wins eventually)
                u.Hp = Math.Min(u.MaxHp, u.Hp + Math.Max(3, u.MaxHp / 4));
            }
            SpawnEndlessWave(Wave + 1);
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
        if (salvage > 0) { SaveGame.AddSalvage(salvage); _run.Report.Insert(0, $"SALVAGE +{salvage}"); }

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
            if (EndlessWaveCount(1) < EndlessWaveCount(2)) { /* strictly rising early */ }
            else fails.Add("countNotRisingEarly");
            // late waves should be pinned at the cap (escalation moves to toughness).
            if (EndlessWaveCount(40) != EndlessAliveCap) fails.Add("lateNotCapped");

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
            ? "HORDETEST: PASS (wave count/scale escalate + alive-cap holds; endless skips the pressure clock; meta BestWave round-trips)"
            : "HORDETEST: FAIL (" + string.Join(",", fails) + ")";
    }
}
