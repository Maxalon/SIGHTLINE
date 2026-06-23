using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sightline;

// ─────────────────────────────────────────────────────────────────────────────
// Stats — balance telemetry. A no-op unless Enabled is set (so normal play and the
// byte-stable screenshot harness are completely unaffected). The competent autoplay
// AI (Game.SmartStep) drives many headless games; this module accumulates per-shot /
// per-kill / per-mission / per-run records and prints aggregate balance tables, so for
// the first time the game's balance is MEASURABLE instead of asserted.
//
// Recording API (called from Game.cs) is intentionally tiny + allocation-light, and
// every method early-outs when !Enabled. The batch runner lives in Program.cs and uses
// Reset()/BeginRun()/.../Report().
// ─────────────────────────────────────────────────────────────────────────────
public static class Stats
{
    public static bool Enabled = false;

    public class MissionRec
    {
        public int Mission, Heat, Turns;
        public string Objective = "";
        public int SquadStart, SquadSurvived, EnemiesStart, EnemiesKilled;
        public int DamageDealt, DamageTaken;
        public bool Win;
        public string LossCause = "";
        // attacker-class -> totals (player side only, for weapon/class balance)
        public readonly Dictionary<string, int> DamageByClass = new();
        public readonly Dictionary<string, int> ShotsByClass = new();
        public readonly Dictionary<string, int> HitsByClass = new();
        public readonly Dictionary<string, int> KillsByClass = new();
        // which enemy class dealt the killing blow to a soldier (cause-of-death)
        public readonly Dictionary<string, int> DeathsByEnemyClass = new();
    }

    public class RunRec
    {
        public int Heat, MissionsCleared;
        public bool Win;
        public string LossCause = "";
        public readonly List<string> PerksPicked = new();
        public readonly List<MissionRec> Missions = new();
    }

    static RunRec _run;
    static MissionRec _mission;
    public static readonly List<RunRec> Runs = new();

    static void Bump(Dictionary<string, int> d, string k, int n = 1)
    {
        if (string.IsNullOrEmpty(k)) k = "?";
        d.TryGetValue(k, out int v);
        d[k] = v + n;
    }

    public static void Reset() { Runs.Clear(); _run = null; _mission = null; }

    public static void BeginRun(int heat)
    {
        if (!Enabled) return;
        _run = new RunRec { Heat = heat };
        Runs.Add(_run);
    }

    public static void BeginMission(int mission, string objective, int heat, int squad, int enemies)
    {
        if (!Enabled) return;
        if (_run == null) BeginRun(heat);
        _mission = new MissionRec
        {
            Mission = mission, Objective = objective, Heat = heat,
            SquadStart = squad, EnemiesStart = enemies
        };
    }

    // A resolved shot. atkTeam: 0 = player, 1 = enemy (matches Team enum ordinals).
    public static void RecordShot(string atkClass, int atkTeam, bool hit, bool crit, bool graze, int dmg)
    {
        if (!Enabled || _mission == null) return;
        if (atkTeam == 0)
        {
            Bump(_mission.ShotsByClass, atkClass);
            if (hit || crit || graze) Bump(_mission.HitsByClass, atkClass);
            Bump(_mission.DamageByClass, atkClass, dmg);
            _mission.DamageDealt += dmg;
        }
        else
        {
            _mission.DamageTaken += dmg;
        }
    }

    // A kill credited. killerTeam / victimTeam follow Team ordinals (0 player, 1 enemy).
    public static void RecordKill(string killerClass, int killerTeam, string victimClass, int victimTeam)
    {
        if (!Enabled || _mission == null) return;
        if (killerTeam == 0 && victimTeam == 1)
        {
            Bump(_mission.KillsByClass, killerClass);
            _mission.EnemiesKilled++;
        }
        else if (victimTeam == 0)
        {
            // a soldier died — attribute to the enemy class that did it
            Bump(_mission.DeathsByEnemyClass, killerClass);
        }
    }

    public static void RecordPerk(string code)
    {
        if (!Enabled || _run == null || string.IsNullOrEmpty(code)) return;
        _run.PerksPicked.Add(code);
    }

    public static void EndMission(bool win, int turns, int survivors, int enemiesKilled, string lossCause)
    {
        if (!Enabled || _mission == null) return;
        _mission.Win = win;
        _mission.Turns = turns;
        _mission.SquadSurvived = survivors;
        if (enemiesKilled > _mission.EnemiesKilled) _mission.EnemiesKilled = enemiesKilled;
        _mission.LossCause = lossCause ?? "";
        _run?.Missions.Add(_mission);
        _mission = null;
    }

    public static void EndRun(bool win, int missionsCleared, string lossCause)
    {
        if (!Enabled || _run == null) return;
        _run.Win = win;
        _run.MissionsCleared = missionsCleared;
        _run.LossCause = lossCause ?? "";
        _run = null;
    }

    // ── aggregate report ─────────────────────────────────────────────────────
    static string Pct(int num, int den) => den == 0 ? "  -  " : $"{100.0 * num / den,4:0}%";

    public static string Report()
    {
        var sb = new StringBuilder();
        var missions = Runs.SelectMany(r => r.Missions).ToList();
        sb.AppendLine("══════════════════════ SIGHTLINE BALANCE REPORT ══════════════════════");
        sb.AppendLine($"runs={Runs.Count}  missions={missions.Count}");
        if (Runs.Count == 0) { sb.AppendLine("(no data)"); return sb.ToString(); }

        // Run-level
        int runWins = Runs.Count(r => r.Win);
        double avgCleared = Runs.Average(r => (double)r.MissionsCleared);
        sb.AppendLine($"\nRUN OUTCOMES:  win-rate {Pct(runWins, Runs.Count)}   avg missions cleared {avgCleared:0.0}");

        // Win-rate by heat
        sb.AppendLine("\nMISSION WIN-RATE BY HEAT:");
        foreach (var g in missions.GroupBy(m => m.Heat).OrderBy(g => g.Key))
            sb.AppendLine($"  heat {g.Key}: {Pct(g.Count(m => m.Win), g.Count())}  (n={g.Count()}, avg {g.Average(m => (double)m.Turns):0.0} turns)");

        // Win-rate by objective
        sb.AppendLine("\nMISSION WIN-RATE BY OBJECTIVE:");
        foreach (var g in missions.GroupBy(m => m.Objective).OrderByDescending(g => g.Count()))
            sb.AppendLine($"  {g.Key,-11}: {Pct(g.Count(m => m.Win), g.Count())}  (n={g.Count()}, avg {g.Average(m => (double)m.Turns):0.0} turns)");

        // Win-rate by mission number (difficulty curve)
        sb.AppendLine("\nMISSION WIN-RATE BY MISSION #:");
        foreach (var g in missions.GroupBy(m => m.Mission).OrderBy(g => g.Key))
            sb.AppendLine($"  m{g.Key}: {Pct(g.Count(x => x.Win), g.Count())}  (n={g.Count()})");

        // Cause of loss
        sb.AppendLine("\nLOSS CAUSES (missions):");
        foreach (var g in missions.Where(m => !m.Win && !string.IsNullOrEmpty(m.LossCause))
                                   .GroupBy(m => m.LossCause).OrderByDescending(g => g.Count()))
            sb.AppendLine($"  {g.Key,-22}: {g.Count()}");

        // Damage / accuracy by player class
        var dmgByClass = new Dictionary<string, int>();
        var shotsByClass = new Dictionary<string, int>();
        var hitsByClass = new Dictionary<string, int>();
        var killsByClass = new Dictionary<string, int>();
        foreach (var m in missions)
        {
            foreach (var kv in m.DamageByClass) Bump(dmgByClass, kv.Key, kv.Value);
            foreach (var kv in m.ShotsByClass) Bump(shotsByClass, kv.Key, kv.Value);
            foreach (var kv in m.HitsByClass) Bump(hitsByClass, kv.Key, kv.Value);
            foreach (var kv in m.KillsByClass) Bump(killsByClass, kv.Key, kv.Value);
        }
        sb.AppendLine("\nPLAYER CLASS PERFORMANCE:");
        sb.AppendLine("  class        shots   hit%   dmg   kills");
        foreach (var c in dmgByClass.Keys.Concat(shotsByClass.Keys).Distinct().OrderByDescending(c => dmgByClass.GetValueOrDefault(c)))
        {
            shotsByClass.TryGetValue(c, out int s);
            hitsByClass.TryGetValue(c, out int h);
            dmgByClass.TryGetValue(c, out int d);
            killsByClass.TryGetValue(c, out int k);
            sb.AppendLine($"  {c,-11} {s,6}  {Pct(h, s)}  {d,5}   {k,4}");
        }

        // Threat ranking: which enemy classes kill soldiers
        var deaths = new Dictionary<string, int>();
        foreach (var m in missions)
            foreach (var kv in m.DeathsByEnemyClass) Bump(deaths, kv.Key, kv.Value);
        if (deaths.Count > 0)
        {
            sb.AppendLine("\nSOLDIER DEATHS BY ENEMY CLASS (threat ranking):");
            foreach (var kv in deaths.OrderByDescending(kv => kv.Value))
                sb.AppendLine($"  {kv.Key,-11}: {kv.Value}");
        }

        // Perk pick frequency
        var perks = new Dictionary<string, int>();
        foreach (var r in Runs) foreach (var p in r.PerksPicked) Bump(perks, p);
        if (perks.Count > 0)
        {
            sb.AppendLine("\nPERK PICK FREQUENCY:");
            foreach (var kv in perks.OrderByDescending(kv => kv.Value))
                sb.AppendLine($"  {kv.Key,-14}: {kv.Value}");
        }

        sb.AppendLine("═══════════════════════════════════════════════════════════════════════");
        return sb.ToString();
    }
}
