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
        // W2 arena telemetry: the AUTHORED layout index actually applied by Mission.Build
        // (recorded only after TryApplyLayout's connectivity guard accepted it), or -1 for
        // the procedural fallback. Lets the report rank arenas and expose the fallback rate.
        public int Layout = -1;
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
        // APEX W4: the GAME MODE this run was played under — "campaign" (default), "endless"
        // (LAST STAND), "skirmish", or "daily". Previously the mode was smuggled through the
        // Policy slot (BeginEndless tagged Policy="endless"), which blocked a greedy/sloppy
        // split for endless stands and kept them out of campaign gap math only by tag-string
        // accident. Now the exclusion is EXPLICIT: gap/completion tables filter Mode=="campaign".
        public string Mode = "campaign";
        // W2 CRN pairing: the batch SLOT this run replayed (both policy legs of slot i share
        // Util.Reseed(50000+i), so their worlds are identical until the policies diverge).
        // -1 = unpaired (interactive/harness paths that don't set Stats.Slot).
        public int Slot = -1;
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

    // W2 CRN pairing: the batch runner stamps the current slot here before constructing each
    // Game; BeginRun copies it onto the RunRec. -1 outside a paired batch (the default).
    public static int Slot = -1;

    // ── W2: ACTION MIX (verb telemetry, split by policy) ─────────────────────────
    // Every committed player VERB (move/shoot/overwatch/focus/brace/hunker/item/patch/drag/...)
    // bumps its counter under the active run's policy, so the report can show what each policy
    // actually DOES — previously reactive verbs (FOCUS, BRACE) and support verbs (PATCH, DRAG,
    // REARM) were invisible to measurement. Batch-global (verbs are aggregate texture).
    static readonly Dictionary<string, Dictionary<string, int>> _actionsByPolicy = new();
    public static void RecordAction(string verb)
    {
        if (!Enabled || string.IsNullOrEmpty(verb)) return;
        string policy = _run != null ? _run.Policy : "greedy";
        if (!_actionsByPolicy.TryGetValue(policy, out var d)) _actionsByPolicy[policy] = d = new();
        Bump(d, verb);
    }

    static void Bump(Dictionary<string, int> d, string k, int n = 1)
    {
        if (string.IsNullOrEmpty(k)) k = "?";
        d.TryGetValue(k, out int v);
        d[k] = v + n;
    }

    // ── APEX W5: enemy-composition tally ─────────────────────────────────────────
    // Per-archetype spawn counts, tagged by roster source: FACTION (spawned while a
    // Combat.MissionFaction was active, i.e. a faction-stamped Combat/Elite node) vs DEFAULT
    // (the unstamped cascade: Start/Supply/Boss nodes, skirmish, endless). Counted at SPAWN
    // time (mission build + Defend/pressure waves + endless bodies) — the only signal Stats
    // had before was DeathsByEnemyClass, which rare/passive archetypes (SCREENER/SPOTTER)
    // essentially never register in. Batch-global (not per-mission): spawns during Mission.Build
    // land before Stats.BeginMission, and the reachability question is aggregate anyway.
    static readonly Dictionary<string, int> _spawnsFactionByClass = new();
    static readonly Dictionary<string, int> _spawnsDefaultByClass = new();
    public static void RecordSpawn(string cls, bool factionRoster)
    {
        if (!Enabled) return;
        Bump(factionRoster ? _spawnsFactionByClass : _spawnsDefaultByClass, cls);
    }

    public static void Reset()
    {
        Runs.Clear(); _run = null; _mission = null;
        _spawnsFactionByClass.Clear(); _spawnsDefaultByClass.Clear();
        _actionsByPolicy.Clear();
        Slot = -1;
    }

    public static void BeginRun(int heat, string policy = "greedy", string mode = "campaign")
    {
        if (!Enabled) return;
        _run = new RunRec
        {
            Heat = heat,
            Policy = string.IsNullOrEmpty(policy) ? "greedy" : policy,
            Mode = string.IsNullOrEmpty(mode) ? "campaign" : mode,
            Slot = Slot
        };
        Runs.Add(_run);
    }

    public static void BeginMission(int mission, string objective, int heat, int squad, int enemies, int layout = -1)
    {
        if (!Enabled) return;
        if (_run == null) BeginRun(heat);
        _mission = new MissionRec
        {
            Mission = mission, Objective = objective, Heat = heat,
            SquadStart = squad, EnemiesStart = enemies, Layout = layout
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

    // W2: paired per-slot outcomes over CAMPAIGN runs — a slot pairs when it has exactly one
    // greedy and one sloppy leg (the batch's normal shape). Shared by Report + BuildSummary.
    static (int pairs, int concordant, int greedyOnlyWon, int sloppyOnlyWon) PairedOutcomes(List<RunRec> campRuns)
    {
        var pairs = campRuns.Where(r => r.Slot >= 0)
            .GroupBy(r => r.Slot)
            .Select(g => (g: g.Where(r => r.Policy == "greedy").ToList(),
                          s: g.Where(r => r.Policy == "sloppy").ToList()))
            .Where(p => p.g.Count == 1 && p.s.Count == 1)
            .Select(p => (gWin: p.g[0].Win, sWin: p.s[0].Win))
            .ToList();
        return (pairs.Count,
                pairs.Count(p => p.gWin == p.sWin),
                pairs.Count(p => p.gWin && !p.sWin),
                pairs.Count(p => !p.gWin && p.sWin));
    }

    // APEX W4: small order stats for the endless wave-depth distribution. Nearest-rank
    // percentile over a pre-sorted list; median = P50 averaged across the middle pair.
    static double MedianOf(List<int> sorted)
    {
        if (sorted.Count == 0) return 0;
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
    static double PercentileOf(List<int> sorted, double pct)
    {
        if (sorted.Count == 0) return 0;
        int rank = (int)Math.Ceiling(pct / 100.0 * sorted.Count);   // nearest-rank method
        return sorted[Math.Min(sorted.Count - 1, Math.Max(0, rank - 1))];
    }

    public static string Report()
    {
        var sb = new StringBuilder();
        // APEX W4: split by MODE. Campaign-shaped tables (run completion, policy gap, per-mission
        // win-rates) aggregate CAMPAIGN runs only — an endless stand always "loses" and logs waves
        // in MissionsCleared, so mixing modes would corrupt every one of them. Combat-kernel tables
        // (class performance / threat ranking) keep ALL missions: a shot is a shot in any mode.
        var campRuns = Runs.Where(r => r.Mode == "campaign").ToList();
        var endlessRuns = Runs.Where(r => r.Mode == "endless").ToList();
        var allMissions = Runs.SelectMany(r => r.Missions).ToList();
        var missions = campRuns.SelectMany(r => r.Missions).ToList();
        sb.AppendLine("══════════════════════ SIGHTLINE BALANCE REPORT ══════════════════════");
        sb.AppendLine(Runs.Count == campRuns.Count
            ? $"runs={Runs.Count}  missions={allMissions.Count}"
            : $"runs={Runs.Count} (campaign {campRuns.Count} / endless {endlessRuns.Count} / other {Runs.Count - campRuns.Count - endlessRuns.Count})  missions={allMissions.Count}");
        if (Runs.Count == 0) { sb.AppendLine("(no data)"); return sb.ToString(); }

        // Run-level (campaign only — endless "waves survived" is not "missions cleared")
        if (campRuns.Count > 0)
        {
            int runWins = campRuns.Count(r => r.Win);
            double avgCleared = campRuns.Average(r => (double)r.MissionsCleared);
            sb.AppendLine($"\nRUN OUTCOMES (campaign):  win-rate {Pct(runWins, campRuns.Count)}   avg missions cleared {avgCleared:0.0}");
        }

        // ── OPTIMAL vs SLOPPY GAP ────────────────────────────────────────────────────
        // If the batch ran both policies, the gap between a near-optimal "greedy" bot and a
        // human-error "sloppy" bot is a DIFFICULTY-SLACK signal: a large gap = swingy/unfair
        // (small mistakes lose runs); both-high-and-close = healthy slack for human error.
        // CAMPAIGN RUNS ONLY (explicit Mode filter): endless stands have no "win" to gap.
        var greedy = campRuns.Where(r => r.Policy == "greedy").ToList();
        var sloppy = campRuns.Where(r => r.Policy == "sloppy").ToList();
        if ((greedy.Count == 0 || sloppy.Count == 0) && Runs.Count > campRuns.Count)
            sb.AppendLine($"\nPOLICY GAP (campaign runs only): n={campRuns.Count} — endless/skirmish runs are excluded from gap math");
        if (greedy.Count > 0 && sloppy.Count > 0)
        {
            double gW = 100.0 * greedy.Count(r => r.Win) / greedy.Count;
            double sW = 100.0 * sloppy.Count(r => r.Win) / sloppy.Count;
            sb.AppendLine($"\nPOLICY GAP (optimal vs human-error; campaign runs only):");
            sb.AppendLine($"  greedy win-rate {gW,4:0}%  (n={greedy.Count})");
            sb.AppendLine($"  sloppy win-rate {sW,4:0}%  (n={sloppy.Count})");
            // A small |gap| (either sign) = the game tolerates human error (healthy slack); a
            // large POSITIVE gap = sloppy play tanks the run (swingy/unforgiving). A large
            // negative gap is just small-sample noise (sloppy got lucky) — gather more runs.
            string verdict = gW - sW > 15 ? "swingy/unforgiving" : (Math.Abs(gW - sW) <= 15 ? "healthy slack" : "noisy (need more runs)");
            sb.AppendLine($"  GAP {gW - sW,4:0} pts  ({verdict})");
            // ── W2 CRN pairing: report the gap as PAIRED per-slot outcomes ─────────────
            // Both policy legs of a slot replayed the SAME world (Util.Reseed(50000+slot)), so
            // the slot-level comparison cancels the world-to-world variance that made unpaired
            // gap readings swing ±27-38 pts. Concordant = both legs same outcome; discordant
            // pairs are the signal (greedy-only wins − sloppy-only wins) / pairs.
            var (nPairs, conc, dPlus, dMinus) = PairedOutcomes(campRuns);
            if (nPairs > 0)
            {
                double pairedGap = 100.0 * (dPlus - dMinus) / nPairs;
                sb.AppendLine($"  PAIRED (same-seed slots): pairs={nPairs}  concordant={conc}  discordant greedy-only-won={dPlus} / sloppy-only-won={dMinus}  paired gap {pairedGap,4:0} pts");
            }
            // per-objective gap so a single brittle objective can't hide in the overall number
            sb.AppendLine("  by objective (greedy / sloppy / gap):");
            var objs = campRuns.SelectMany(r => r.Missions.Select(m => m.Objective)).Distinct().OrderBy(o => o);
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
        var tMissions = allMissions.Where(m => m.PlayerTurns > 0).ToList();
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
        if (campRuns.Count > 0)
        {
            sb.AppendLine("\nRUN COMPLETION BY HEAT (full-campaign clears):");
            foreach (var g in campRuns.GroupBy(r => r.Heat).OrderBy(g => g.Key))
                sb.AppendLine($"  heat {g.Key}: {Pct(g.Count(r => r.Win), g.Count())}  (n={g.Count()} runs, avg {g.Average(r => (double)r.MissionsCleared):0.0} missions)");
        }

        // ── APEX W4: ENDLESS WAVE DEPTH (LAST STAND) ─────────────────────────────────
        // The mode's tuning metric: how deep a stand gets before the wipe. Depth for these runs
        // is stored in MissionsCleared (= waves survived — logged from game.Wave at every exit:
        // wipe, wave-cap, frame-cap, abort). Cap hits are logged explicitly so a right-censored
        // p90 is always visible, never silent.
        if (endlessRuns.Count > 0)
        {
            sb.AppendLine("\nENDLESS WAVE DEPTH (LAST STAND, waves survived):");
            void DepthLine(string label, List<RunRec> rs)
            {
                if (rs.Count == 0) return;
                var d = rs.Select(r => r.MissionsCleared).OrderBy(x => x).ToList();
                sb.AppendLine($"  {label,-8}: n={d.Count,-3} mean {d.Average():0.0}   median {MedianOf(d):0.#}   p90 {PercentileOf(d, 90):0.#}");
            }
            DepthLine("all", endlessRuns);
            DepthLine("greedy", endlessRuns.Where(r => r.Policy == "greedy").ToList());
            DepthLine("sloppy", endlessRuns.Where(r => r.Policy == "sloppy").ToList());
            foreach (var g in endlessRuns.GroupBy(r => r.Heat).OrderBy(g => g.Key))
                DepthLine($"heat {g.Key}", g.ToList());
            int waveCaps = endlessRuns.Count(r => r.LossCause == "wave-cap");
            int frameCaps = endlessRuns.Count(r => r.LossCause == "frame-cap");
            sb.AppendLine(waveCaps + frameCaps > 0
                ? $"  CAP HITS: wave-cap {waveCaps}, frame-cap {frameCaps} — depth is right-censored for these stands"
                : "  cap hits: none (distribution uncensored)");
        }

        // Per-mission win-rate tables (campaign missions only — skipped in an endless-only batch)
        if (missions.Count > 0)
        {
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

            // ── W2: WIN/TURNS BY ARENA (authored-layout telemetry) ────────────────────
            // Layout is the authored template index Mission.Build actually APPLIED (recorded
            // only after the connectivity guard accepted it; -1 = procedural fallback). Rows
            // filtered to n>=3 so single-sight arenas don't read as 0%/100% outliers.
            int procN = missions.Count(m => m.Layout < 0);
            sb.AppendLine($"\nMISSION WIN-RATE BY ARENA (authored layouts, n>=3; procedural fallback {Pct(procN, missions.Count)} of {missions.Count} missions):");
            foreach (var g in missions.Where(m => m.Layout >= 0).GroupBy(m => m.Layout)
                                       .Where(g => g.Count() >= 3).OrderBy(g => g.Key))
                sb.AppendLine($"  arena {g.Key,2}: {Pct(g.Count(m => m.Win), g.Count())}  (n={g.Count()}, avg {g.Average(m => (double)m.Turns):0.0} turns)");
            if (procN >= 3)
                sb.AppendLine($"  procedural: {Pct(missions.Count(m => m.Layout < 0 && m.Win), procN)}  (n={procN}, avg {missions.Where(m => m.Layout < 0).Average(m => (double)m.Turns):0.0} turns)");
        }

        // ── W2: ACTION MIX (verbs issued, split by policy) ───────────────────────────
        // What each policy actually DOES — makes the reactive verbs (FOCUS/BRACE) and the
        // support verbs (PATCH/DRAG/REARM/items) visible to measurement, and shows how the
        // sloppy policy's slips shift the mix. Percentages are within each policy's total.
        if (_actionsByPolicy.Count > 0)
        {
            var policies = _actionsByPolicy.Keys.OrderBy(p => p).ToList();
            var verbs = _actionsByPolicy.Values.SelectMany(d => d.Keys).Distinct()
                .OrderByDescending(v => _actionsByPolicy.Values.Sum(d => d.GetValueOrDefault(v))).ToList();
            sb.AppendLine("\nACTION MIX (verbs issued, by policy):");
            sb.Append("  verb          ");
            foreach (var p in policies) sb.Append($"{p,14}");
            sb.AppendLine();
            var totals = policies.ToDictionary(p => p, p => _actionsByPolicy[p].Values.Sum());
            foreach (var v in verbs)
            {
                sb.Append($"  {v,-14}");
                foreach (var p in policies)
                {
                    int n = _actionsByPolicy[p].GetValueOrDefault(v);
                    sb.Append($"{n,8} {Pct(n, Math.Max(1, totals[p]))}");
                }
                sb.AppendLine();
            }
        }

        // Damage / accuracy by player class (ALL modes — combat-kernel data is mode-agnostic)
        var dmgByClass = new Dictionary<string, int>();
        var shotsByClass = new Dictionary<string, int>();
        var hitsByClass = new Dictionary<string, int>();
        var killsByClass = new Dictionary<string, int>();
        foreach (var m in allMissions)
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

        // Threat ranking: which enemy classes kill soldiers (ALL modes)
        var deaths = new Dictionary<string, int>();
        foreach (var m in allMissions)
            foreach (var kv in m.DeathsByEnemyClass) Bump(deaths, kv.Key, kv.Value);
        if (deaths.Count > 0)
        {
            sb.AppendLine("\nSOLDIER DEATHS BY ENEMY CLASS (threat ranking):");
            foreach (var kv in deaths.OrderByDescending(kv => kv.Value))
                sb.AppendLine($"  {kv.Key,-11}: {kv.Value}");
        }

        // ── APEX W5: ENEMY COMPOSITION (spawn tally, faction-stamped vs default cascade) ──────
        // The content-reachability metric: which archetypes the campaign actually FIELDS, split by
        // roster source. The interesting column is `faction` — a class at 0% there is authored
        // content the majority of the map never shows (the W5 bug this table exists to catch).
        if (_spawnsFactionByClass.Count > 0 || _spawnsDefaultByClass.Count > 0)
        {
            int fTot = _spawnsFactionByClass.Values.Sum();
            int dTot = _spawnsDefaultByClass.Values.Sum();
            sb.AppendLine($"\nENEMY COMPOSITION (spawns; faction-stamped fights n={fTot} / default-cascade fights n={dTot}):");
            sb.AppendLine("  class        faction         default");
            foreach (var c in _spawnsFactionByClass.Keys.Concat(_spawnsDefaultByClass.Keys).Distinct()
                         .OrderByDescending(c => _spawnsFactionByClass.GetValueOrDefault(c) + _spawnsDefaultByClass.GetValueOrDefault(c)))
            {
                int f = _spawnsFactionByClass.GetValueOrDefault(c);
                int d = _spawnsDefaultByClass.GetValueOrDefault(c);
                sb.AppendLine($"  {c,-11} {f,6} {Pct(f, fTot)}   {d,6} {Pct(d, dTot)}");
            }
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

        // ── APEX W4: WIN-RATE BY PICK (value telemetry, not just frequency) ──────────
        // Run-level association: for each code, the completion rate of the CAMPAIGN runs that
        // held it (deduped per run). These were recorded all along — contracts were recorded
        // and never reported at ALL — but only frequencies ever reached the report, so a pick's
        // VALUE was invisible. Small-n rows are noisy; read them against the pick count.
        void WinRateTable(string title, Func<RunRec, IEnumerable<string>> picks)
        {
            var agg = new Dictionary<string, (int n, int w)>();
            foreach (var r in campRuns)
                foreach (var code in picks(r).Distinct())
                {
                    agg.TryGetValue(code, out var t);
                    agg[code] = (t.n + 1, t.w + (r.Win ? 1 : 0));
                }
            if (agg.Count == 0) return;
            sb.AppendLine($"\n{title}:");
            foreach (var kv in agg.OrderByDescending(kv => kv.Value.n).ThenBy(kv => kv.Key))
                sb.AppendLine($"  {kv.Key,-18}: {Pct(kv.Value.w, kv.Value.n)}  (in {kv.Value.n} runs)");
        }
        WinRateTable("RUN WIN-RATE BY BOON (campaign runs holding it)", r => r.BoonsPicked);
        WinRateTable("RUN WIN-RATE BY SPEC (campaign runs fielding it)", r => r.SpecsPicked);
        WinRateTable("RUN WIN-RATE BY CONTRACT (campaign runs under it)", r => r.ContractsPicked);
        // W2: perk + purchase VALUE tables (frequency alone hid whether a pick actually helps —
        // the perk offer and the shop slate both got randomized exposure for exactly this table).
        WinRateTable("RUN WIN-RATE BY PERK (campaign runs holding it)", r => r.PerksPicked);
        WinRateTable("RUN WIN-RATE BY PURCHASE (campaign runs buying it)", r => r.Purchases);

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

    // A plain-data view of the aggregate, for JSON. Built from the same Runs list as Report(),
    // with the same APEX W4 mode split: win-rate/gap tables are CAMPAIGN-only, combat-kernel
    // tables (class/threat) span all modes, and endless stands get their own depth object.
    public static object BuildSummary()
    {
        var campRuns = Runs.Where(r => r.Mode == "campaign").ToList();
        var endlessRuns = Runs.Where(r => r.Mode == "endless").ToList();
        var allMissions = Runs.SelectMany(r => r.Missions).ToList();
        var missions = campRuns.SelectMany(r => r.Missions).ToList();

        var dmgByClass = new Dictionary<string, int>();
        var shotsByClass = new Dictionary<string, int>();
        var hitsByClass = new Dictionary<string, int>();
        var killsByClass = new Dictionary<string, int>();
        foreach (var m in allMissions)
        {
            foreach (var kv in m.DamageByClass) Bump(dmgByClass, kv.Key, kv.Value);
            foreach (var kv in m.ShotsByClass) Bump(shotsByClass, kv.Key, kv.Value);
            foreach (var kv in m.HitsByClass) Bump(hitsByClass, kv.Key, kv.Value);
            foreach (var kv in m.KillsByClass) Bump(killsByClass, kv.Key, kv.Value);
        }
        var deaths = new Dictionary<string, int>();
        foreach (var m in allMissions)
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

        // policy gap (greedy vs sloppy) for the machine-readable artifact — CAMPAIGN runs only
        // (APEX W4: endless/skirmish are excluded by the explicit Mode filter, not tag accident)
        var greedy = campRuns.Where(r => r.Policy == "greedy").ToList();
        var sloppy = campRuns.Where(r => r.Policy == "sloppy").ToList();
        double greedyWin = greedy.Count == 0 ? 0.0 : Math.Round(100.0 * greedy.Count(r => r.Win) / greedy.Count, 1);
        double sloppyWin = sloppy.Count == 0 ? 0.0 : Math.Round(100.0 * sloppy.Count(r => r.Win) / sloppy.Count, 1);

        // APEX W4: endless wave-depth aggregates (depth = MissionsCleared = waves survived)
        object DepthStats(List<RunRec> rs)
        {
            var d = rs.Select(r => r.MissionsCleared).OrderBy(x => x).ToList();
            return new
            {
                n = d.Count,
                mean = d.Count == 0 ? 0.0 : Math.Round(d.Average(), 2),
                median = MedianOf(d),
                p90 = PercentileOf(d, 90)
            };
        }

        // APEX W4: run-level win-rate by pick code (campaign only, deduped per run)
        Dictionary<string, object> WinRateBy(Func<RunRec, IEnumerable<string>> picks)
        {
            var agg = new Dictionary<string, (int n, int w)>();
            foreach (var r in campRuns)
                foreach (var code in picks(r).Distinct())
                {
                    agg.TryGetValue(code, out var t);
                    agg[code] = (t.n + 1, t.w + (r.Win ? 1 : 0));
                }
            return agg.OrderByDescending(kv => kv.Value.n).ToDictionary(
                kv => kv.Key,
                kv => (object)new { runs = kv.Value.n, winRate = Math.Round(100.0 * kv.Value.w / kv.Value.n, 1) });
        }

        // decision-richness / swing aggregates (all modes — texture data is mode-agnostic)
        var tMissions = allMissions.Where(m => m.PlayerTurns > 0).ToList();
        double choicesPerTurn = tMissions.Count == 0 ? 0.0
            : Math.Round(tMissions.Sum(m => (double)m.MeaningfulChoiceSum) / Math.Max(1, tMissions.Sum(m => m.PlayerTurns)), 3);
        double swingsPerMatch = tMissions.Count == 0 ? 0.0 : Math.Round(tMissions.Average(m => (double)m.LeadSwings), 2);
        double avgMaxSwing = tMissions.Count == 0 ? 0.0 : Math.Round(tMissions.Average(m => (double)m.MaxSwing), 2);

        return new
        {
            runs = Runs.Count,
            // APEX W4: run counts by mode, so a consumer can see at a glance what the batch mixed.
            runsByMode = Runs.GroupBy(r => r.Mode).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            missions = allMissions.Count,
            // campaign-only (endless "missions cleared" are waves — a different unit entirely)
            runWinRate = campRuns.Count == 0 ? 0.0 : Math.Round(100.0 * campRuns.Count(r => r.Win) / campRuns.Count, 1),
            avgMissionsCleared = campRuns.Count == 0 ? 0.0 : Math.Round(campRuns.Average(r => (double)r.MissionsCleared), 2),
            policyGap = new
            {
                greedyRuns = greedy.Count, greedyWinRate = greedyWin,
                sloppyRuns = sloppy.Count, sloppyWinRate = sloppyWin,
                gap = Math.Round(greedyWin - sloppyWin, 1)
            },
            // W2 CRN pairing: same-seed slot outcomes (variance-cancelled gap). pairedGap =
            // (greedyOnlyWon − sloppyOnlyWon) / pairs — the number the flywheel should trend.
            pairedPolicy = BuildPaired(campRuns),
            // APEX W4: LAST STAND depth distribution (empty/zeroed when the batch had no endless runs).
            endless = new
            {
                runs = endlessRuns.Count,
                depth = DepthStats(endlessRuns),
                depthGreedy = DepthStats(endlessRuns.Where(r => r.Policy == "greedy").ToList()),
                depthSloppy = DepthStats(endlessRuns.Where(r => r.Policy == "sloppy").ToList()),
                byHeat = endlessRuns.GroupBy(r => r.Heat).OrderBy(g => g.Key)
                    .Select(g => new { heat = g.Key, depth = DepthStats(g.ToList()) }).ToList(),
                waveCapHits = endlessRuns.Count(r => r.LossCause == "wave-cap"),
                frameCapHits = endlessRuns.Count(r => r.LossCause == "frame-cap")
            },
            decisionRichness = new
            {
                meaningfulChoicesPerTurn = choicesPerTurn,
                leadSwingsPerMatch = swingsPerMatch,
                avgMaxSwing = avgMaxSwing
            },
            // Run-completion grouped by heat (the ladder's true shape — distinct from the
            // survivorship-skewed per-mission byHeat below). Campaign runs only.
            byHeatRun = campRuns.GroupBy(r => r.Heat).OrderBy(g => g.Key).Select(g => new
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
            // W2 arena telemetry: authored layout index (-1 rows are folded into proceduralRate)
            byArena = missions.Where(m => m.Layout >= 0).GroupBy(m => m.Layout).OrderBy(g => g.Key).Select(g => new
            {
                arena = g.Key, n = g.Count(), winRate = WinRate(g), avgTurns = Math.Round(g.Average(m => (double)m.Turns), 1)
            }).ToList(),
            proceduralFallback = new
            {
                n = missions.Count(m => m.Layout < 0),
                rate = missions.Count == 0 ? 0.0 : Math.Round(100.0 * missions.Count(m => m.Layout < 0) / missions.Count, 1),
                winRate = WinRate(missions.Where(m => m.Layout < 0))
            },
            // W2 ACTION MIX: verb counts by policy (what each policy actually does)
            actionMix = _actionsByPolicy.OrderBy(kv => kv.Key).ToDictionary(
                kv => kv.Key,
                kv => kv.Value.OrderByDescending(v => v.Value).ToDictionary(v => v.Key, v => v.Value)),
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
            // APEX W5: per-archetype spawn tally, split by roster source (faction-stamped node vs
            // default cascade) — the machine-readable form of the ENEMY COMPOSITION table.
            enemyComposition = new
            {
                factionSpawns = _spawnsFactionByClass.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
                defaultSpawns = _spawnsDefaultByClass.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            },
            perkPicks = perks.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            specPicks = specs.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            shopPurchases = buys.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            boonPicks = boons.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            // APEX W4: pick VALUE, not just frequency — run win-rate by held boon/spec/contract
            // (campaign runs, deduped per run; contracts were recorded but never reported at all).
            winRateByBoon = WinRateBy(r => r.BoonsPicked),
            winRateBySpec = WinRateBy(r => r.SpecsPicked),
            winRateByContract = WinRateBy(r => r.ContractsPicked),
            // W2: pick VALUE for perks + shop purchases too (both got randomized exposure)
            winRateByPerk = WinRateBy(r => r.PerksPicked),
            winRateByPurchase = WinRateBy(r => r.Purchases),
        };
    }

    // W2: the paired-outcome object for the JSON artifact (mirrors the PAIRED report line).
    static object BuildPaired(List<RunRec> campRuns)
    {
        var (pairs, concordant, dPlus, dMinus) = PairedOutcomes(campRuns);
        return new
        {
            pairs, concordant,
            greedyOnlyWon = dPlus, sloppyOnlyWon = dMinus,
            pairedGap = pairs == 0 ? 0.0 : Math.Round(100.0 * (dPlus - dMinus) / pairs, 1)
        };
    }
}
