using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

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

    // ──── COMBAT LOG (Wave 4 readability) ────────────────────────────────────────
    // A rolling ledger of the last MaxLog consequential events (shots/kills/status/objective),
    // shown by Hud's combat-log panel. ALWAYS-ON (independent of Enabled / the balance harness) so
    // a player can audit "did the dice cheat me?" — the antidote to output-randomness rage. Cheap:
    // a bounded list of small structs. The Hud reads CombatLog read-only; nothing else depends on it.
    public struct LogEntry { public int Turn; public int Team; public string Text; public string Outcome; }
    public const int MaxLog = 40;
    public static readonly List<LogEntry> CombatLog = new();
    public static void ClearLog() => CombatLog.Clear();
    public static void Log(int turn, int team, string text, string outcome = "")
    {
        CombatLog.Add(new LogEntry { Turn = turn, Team = team, Text = text, Outcome = outcome });
        if (CombatLog.Count > MaxLog) CombatLog.RemoveAt(0);
    }

    public class MissionRec
    {
        public int Mission, Heat, Turns;
        public string Objective = "";
        public int SquadStart, SquadSurvived, EnemiesStart, EnemiesKilled;
        public int DamageDealt, DamageTaken;
        public bool Win;
        public string LossCause = "";
        // ── decision-richness + swing instrumentation (balance harness only) ──────────
        // PlayerTurns: number of player turns observed this mission. MeaningfulChoiceSum:
        // total count of "near-best" candidate actions the smart bot weighed across those
        // turns (a choice is "meaningful" when a runner-up's value is within ~12% of the
        // best — i.e. the turn presented a real decision, not a forced move). Their ratio
        // is a decision-richness proxy. LeadSwings: how many times the (sum-player-HP −
        // sum-enemy-HP) lead changed sign during the match (a tension proxy); MaxSwing:
        // the largest single-turn change in that lead.
        public int PlayerTurns, MeaningfulChoiceSum, LeadSwings, MaxSwing;
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
        // "greedy" (optimal smart policy) or "sloppy" (smart policy + human-like error).
        // Lets the report split win-rate by policy and surface the optimal-vs-sloppy GAP.
        public string Policy = "greedy";
        public readonly List<string> PerksPicked = new();
        public readonly List<string> SpecsPicked = new();  // W2: class-specialization fork picks
        public readonly List<string> Purchases = new();   // shop items bought (incl. weapon mods)
        public readonly List<string> BoonsPicked = new();  // run-scoped doctrine/boon picks
        public readonly List<string> ContractsPicked = new();  // W6 run-contract picks (usually 0-1/run)
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

    public static void BeginRun(int heat, string policy = "greedy")
    {
        if (!Enabled) return;
        _run = new RunRec { Heat = heat, Policy = string.IsNullOrEmpty(policy) ? "greedy" : policy };
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
        ResetLeadTracker();   // swings/lead are scoped to one match
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

    // A class-specialization fork pick (W2). Lets the flywheel measure win-rate by spec + balance forks.
    public static void RecordSpec(string code)
    {
        if (!Enabled || _run == null || string.IsNullOrEmpty(code)) return;
        _run.SpecsPicked.Add(code);
    }

    // A shop purchase (item display name). Lets the flywheel see what the reward sink actually buys.
    public static void RecordPurchase(string item)
    {
        if (!Enabled || _run == null || string.IsNullOrEmpty(item)) return;
        _run.Purchases.Add(item);
    }

    // A run-scoped boon / field-doctrine pick.
    public static void RecordBoon(string code)
    {
        if (!Enabled || _run == null || string.IsNullOrEmpty(code)) return;
        _run.BoonsPicked.Add(code);
    }

    // A run CONTRACT (W6) pick. Lets a future batch show contract usage / win-rate by contract.
    public static void RecordContract(string code)
    {
        if (!Enabled || _run == null || string.IsNullOrEmpty(code)) return;
        _run.ContractsPicked.Add(code);
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

    // Per-player-turn snapshot from the balance autopilot (Game.SmartTurnTelemetry).
    //   meaningfulChoices: how many near-best candidate actions the bot weighed this turn.
    //   lead: (sum player HP) − (sum live enemy HP) right now. We track the lead's sign
    //   changes (swings) and the largest per-turn delta across the mission.
    static bool _haveLead; static int _lastLead;
    public static void RecordPlayerTurn(int meaningfulChoices, int lead)
    {
        if (!Enabled || _mission == null) return;
        _mission.PlayerTurns++;
        _mission.MeaningfulChoiceSum += Math.Max(0, meaningfulChoices);
        if (_haveLead)
        {
            int delta = Math.Abs(lead - _lastLead);
            if (delta > _mission.MaxSwing) _mission.MaxSwing = delta;
            // sign change in the lead (excluding 0→±, which isn't a true reversal)
            if (Math.Sign(lead) != 0 && Math.Sign(_lastLead) != 0 && Math.Sign(lead) != Math.Sign(_lastLead))
                _mission.LeadSwings++;
        }
        _lastLead = lead; _haveLead = true;
    }
    // Reset the per-mission lead tracker at each mission start (called from BeginMission's caller
    // indirectly — we reset here to keep swings scoped to one match).
    public static void ResetLeadTracker() { _haveLead = false; _lastLead = 0; }

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

        // ── OPTIMAL vs SLOPPY GAP ────────────────────────────────────────────────────
        // If the batch ran both policies, the gap between a near-optimal "greedy" bot and a
        // human-error "sloppy" bot is a DIFFICULTY-SLACK signal: a large gap = swingy/unfair
        // (small mistakes lose runs); both-high-and-close = healthy slack for human error.
        var greedy = Runs.Where(r => r.Policy == "greedy").ToList();
        var sloppy = Runs.Where(r => r.Policy == "sloppy").ToList();
        if (greedy.Count > 0 && sloppy.Count > 0)
        {
            double gW = 100.0 * greedy.Count(r => r.Win) / greedy.Count;
            double sW = 100.0 * sloppy.Count(r => r.Win) / sloppy.Count;
            sb.AppendLine($"\nPOLICY GAP (optimal vs human-error):");
            sb.AppendLine($"  greedy win-rate {gW,4:0}%  (n={greedy.Count})");
            sb.AppendLine($"  sloppy win-rate {sW,4:0}%  (n={sloppy.Count})");
            // A small |gap| (either sign) = the game tolerates human error (healthy slack); a
            // large POSITIVE gap = sloppy play tanks the run (swingy/unforgiving). A large
            // negative gap is just small-sample noise (sloppy got lucky) — gather more runs.
            string verdict = gW - sW > 15 ? "swingy/unforgiving" : (Math.Abs(gW - sW) <= 15 ? "healthy slack" : "noisy (need more runs)");
            sb.AppendLine($"  GAP {gW - sW,4:0} pts  ({verdict})");
            // per-objective gap so a single brittle objective can't hide in the overall number
            sb.AppendLine("  by objective (greedy / sloppy / gap):");
            var objs = Runs.SelectMany(r => r.Missions.Select(m => m.Objective)).Distinct().OrderBy(o => o);
            foreach (var o in objs)
            {
                var gm = greedy.SelectMany(r => r.Missions).Where(m => m.Objective == o).ToList();
                var sm = sloppy.SelectMany(r => r.Missions).Where(m => m.Objective == o).ToList();
                if (gm.Count == 0 || sm.Count == 0) continue;
                double go = 100.0 * gm.Count(m => m.Win) / gm.Count;
                double so = 100.0 * sm.Count(m => m.Win) / sm.Count;
                sb.AppendLine($"    {o,-11}: {go,4:0}% / {so,4:0}% / gap {go - so,4:0}");
            }
        }

        // ── DECISION RICHNESS + SWING (texture, not just win/loss) ────────────────────
        // avg meaningful-choices/turn = how often the bot faced a real decision (a runner-up
        // within ~12% of the best action). avg lead-swings/match = how often the HP-lead flipped
        // (tension). High-and-textured > grindy-deterministic even at the same win-rate.
        var tMissions = missions.Where(m => m.PlayerTurns > 0).ToList();
        if (tMissions.Count > 0)
        {
            double choicesPerTurn = tMissions.Sum(m => (double)m.MeaningfulChoiceSum) / tMissions.Sum(m => (double)m.PlayerTurns);
            double swingsPerMatch = tMissions.Average(m => (double)m.LeadSwings);
            double maxSwing = tMissions.Average(m => (double)m.MaxSwing);
            sb.AppendLine($"\nDECISION RICHNESS / SWING:");
            sb.AppendLine($"  meaningful-choices/turn {choicesPerTurn:0.00}   lead-swings/match {swingsPerMatch:0.0}   avg max-swing {maxSwing:0.0}");
        }

        // Run-completion by heat — the metric the Heat ladder is supposed to bend. The
        // per-MISSION win-rate below conflates "harder rung" with "how far the run got"
        // (survivorship bias), so it can't show the ladder's shape; this one can.
        sb.AppendLine("\nRUN COMPLETION BY HEAT (full-campaign clears):");
        foreach (var g in Runs.GroupBy(r => r.Heat).OrderBy(g => g.Key))
            sb.AppendLine($"  heat {g.Key}: {Pct(g.Count(r => r.Win), g.Count())}  (n={g.Count()} runs, avg {g.Average(r => (double)r.MissionsCleared):0.0} missions)");

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

        // Shop purchase frequency (reward-sink instrumentation)
        var buys = new Dictionary<string, int>();
        foreach (var r in Runs) foreach (var p in r.Purchases) Bump(buys, p);
        if (buys.Count > 0)
        {
            sb.AppendLine("\nSHOP PURCHASE FREQUENCY:");
            foreach (var kv in buys.OrderByDescending(kv => kv.Value))
                sb.AppendLine($"  {kv.Key,-18}: {kv.Value}");
        }

        // Boon / doctrine pick frequency
        var boons = new Dictionary<string, int>();
        foreach (var r in Runs) foreach (var b in r.BoonsPicked) Bump(boons, b);
        if (boons.Count > 0)
        {
            sb.AppendLine("\nBOON PICK FREQUENCY:");
            foreach (var kv in boons.OrderByDescending(kv => kv.Value))
                sb.AppendLine($"  {kv.Key,-18}: {kv.Value}");
        }

        sb.AppendLine("═══════════════════════════════════════════════════════════════════════");
        return sb.ToString();
    }

    // ── JSON export ───────────────────────────────────────────────────────────
    // Writes the aggregate as a compact JSON document (same numbers as Report()) so a
    // balance batch can be diffed/plotted by external tooling. Mirrors SaveGame's
    // System.Text.Json pattern; failures are swallowed (telemetry must never crash a run).
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static void WriteJson(string path)
    {
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(BuildSummary(), JsonOpts));
        }
        catch { /* telemetry is best-effort; never throw out of a balance batch */ }
    }

    // A plain-data view of the aggregate, for JSON. Built from the same Runs list as Report().
    public static object BuildSummary()
    {
        var missions = Runs.SelectMany(r => r.Missions).ToList();

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
        var deaths = new Dictionary<string, int>();
        foreach (var m in missions)
            foreach (var kv in m.DeathsByEnemyClass) Bump(deaths, kv.Key, kv.Value);
        var perks = new Dictionary<string, int>();
        foreach (var r in Runs) foreach (var p in r.PerksPicked) Bump(perks, p);
        var specs = new Dictionary<string, int>();
        foreach (var r in Runs) foreach (var p in r.SpecsPicked) Bump(specs, p);
        var buys = new Dictionary<string, int>();
        foreach (var r in Runs) foreach (var p in r.Purchases) Bump(buys, p);
        var boons = new Dictionary<string, int>();
        foreach (var r in Runs) foreach (var b in r.BoonsPicked) Bump(boons, b);

        double WinRate(IEnumerable<MissionRec> ms)
        {
            var l = ms as IList<MissionRec> ?? ms.ToList();
            return l.Count == 0 ? 0.0 : Math.Round(100.0 * l.Count(m => m.Win) / l.Count, 1);
        }

        // policy gap (greedy vs sloppy) for the machine-readable artifact
        var greedy = Runs.Where(r => r.Policy == "greedy").ToList();
        var sloppy = Runs.Where(r => r.Policy == "sloppy").ToList();
        double greedyWin = greedy.Count == 0 ? 0.0 : Math.Round(100.0 * greedy.Count(r => r.Win) / greedy.Count, 1);
        double sloppyWin = sloppy.Count == 0 ? 0.0 : Math.Round(100.0 * sloppy.Count(r => r.Win) / sloppy.Count, 1);

        // decision-richness / swing aggregates
        var tMissions = missions.Where(m => m.PlayerTurns > 0).ToList();
        double choicesPerTurn = tMissions.Count == 0 ? 0.0
            : Math.Round(tMissions.Sum(m => (double)m.MeaningfulChoiceSum) / Math.Max(1, tMissions.Sum(m => m.PlayerTurns)), 3);
        double swingsPerMatch = tMissions.Count == 0 ? 0.0 : Math.Round(tMissions.Average(m => (double)m.LeadSwings), 2);
        double avgMaxSwing = tMissions.Count == 0 ? 0.0 : Math.Round(tMissions.Average(m => (double)m.MaxSwing), 2);

        return new
        {
            runs = Runs.Count,
            missions = missions.Count,
            runWinRate = Runs.Count == 0 ? 0.0 : Math.Round(100.0 * Runs.Count(r => r.Win) / Runs.Count, 1),
            avgMissionsCleared = Runs.Count == 0 ? 0.0 : Math.Round(Runs.Average(r => (double)r.MissionsCleared), 2),
            policyGap = new
            {
                greedyRuns = greedy.Count, greedyWinRate = greedyWin,
                sloppyRuns = sloppy.Count, sloppyWinRate = sloppyWin,
                gap = Math.Round(greedyWin - sloppyWin, 1)
            },
            decisionRichness = new
            {
                meaningfulChoicesPerTurn = choicesPerTurn,
                leadSwingsPerMatch = swingsPerMatch,
                avgMaxSwing = avgMaxSwing
            },
            // Run-completion grouped by heat (the ladder's true shape — distinct from the
            // survivorship-skewed per-mission byHeat below).
            byHeatRun = Runs.GroupBy(r => r.Heat).OrderBy(g => g.Key).Select(g => new
            {
                heat = g.Key, runs = g.Count(),
                runWinRate = Math.Round(100.0 * g.Count(r => r.Win) / g.Count(), 1),
                avgMissionsCleared = Math.Round(g.Average(r => (double)r.MissionsCleared), 2)
            }).ToList(),
            byHeat = missions.GroupBy(m => m.Heat).OrderBy(g => g.Key).Select(g => new
            {
                heat = g.Key, n = g.Count(), winRate = WinRate(g), avgTurns = Math.Round(g.Average(m => (double)m.Turns), 1)
            }).ToList(),
            byObjective = missions.GroupBy(m => m.Objective).OrderBy(g => g.Key).Select(g => new
            {
                objective = g.Key, n = g.Count(), winRate = WinRate(g), avgTurns = Math.Round(g.Average(m => (double)m.Turns), 1)
            }).ToList(),
            byMission = missions.GroupBy(m => m.Mission).OrderBy(g => g.Key).Select(g => new
            {
                mission = g.Key, n = g.Count(), winRate = WinRate(g)
            }).ToList(),
            lossCauses = missions.Where(m => !m.Win && !string.IsNullOrEmpty(m.LossCause))
                                 .GroupBy(m => m.LossCause).OrderByDescending(g => g.Count())
                                 .ToDictionary(g => g.Key, g => g.Count()),
            playerClasses = dmgByClass.Keys.Concat(shotsByClass.Keys).Distinct()
                                .OrderByDescending(c => dmgByClass.GetValueOrDefault(c)).Select(c => new
            {
                cls = c,
                shots = shotsByClass.GetValueOrDefault(c),
                hits = hitsByClass.GetValueOrDefault(c),
                hitRate = shotsByClass.GetValueOrDefault(c) == 0 ? 0.0
                          : Math.Round(100.0 * hitsByClass.GetValueOrDefault(c) / shotsByClass.GetValueOrDefault(c), 1),
                dmg = dmgByClass.GetValueOrDefault(c),
                kills = killsByClass.GetValueOrDefault(c)
            }).ToList(),
            soldierDeathsByEnemy = deaths.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            perkPicks = perks.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            specPicks = specs.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            shopPurchases = buys.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            boonPicks = boons.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
        };
    }
}
