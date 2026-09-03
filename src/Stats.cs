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
        // W4 THE SECOND AXIS: the deployment SHAPE this mission opened with
        // (Mission.DeployFrontal/Pincer/Crossfire/Envelop). Lets a report split turn count,
        // win rate and decision density by opening geometry.
        public int Deploy = Sightline.Mission.DeployFrontal;
        public int SquadStart, SquadSurvived, EnemiesStart, EnemiesKilled;
        // W1 TRUE INSTRUMENT: the SURVIVORSHIP coordinates of this mission — the campaign-map
        // node kind that produced it, and the squad's HP as a percentage of its own maximum at
        // deploy. An objective's cross-rung row mixes two effects that pull the same direction:
        // how hard the objective is, and how battered the only squads that ever REACH it are
        // (X2 caught Escort reading 8.03t purely because sick runs died before mission 4).
        // These two fields are what let byObjectiveByBucket hold survivorship still.
        public string NodeKind = "";
        public int SquadHpPct = 100;
        // C4 "EIGHT BIOMES ARE PAINT": the biome this mission was FOUGHT IN, and how many tiles
        // of mechanical ground (fern / drift / vent) were stamped on it. Until C4 the biome was
        // paint, so there was nothing to split by and Stats never recorded it — which is exactly
        // why C4's first round could see a −3.7 whole-campaign move at heat 0 and not say WHICH
        // of the three biomes bought it. Read-only telemetry: two pure reads of state that
        // already exists, no draw, no mutation (CROSSCUT rule 1 — never conclude from a pooled row).
        public string Biome = "";
        public int GroundTiles;
        // W8 THE HALF WALL: the DECAPITATE punch-through target. HvtKind is -1 on every other
        // objective, 0 when the HVT was an ELITE (DesignateHvt's exemption — the campaign finale's
        // named boss, or the m3/m5 mid-boss) and 1 when a rank-and-file body took the statline
        // buff. HvtMaxHp is the target's MaxHp AFTER any buff. Without these the objective's row
        // pools a capstone with a mid-run node — which is how a 23.7-point gap survived every wave
        // until L1 spent 480 campaigns on a ladder and stumbled over it.
        public int HvtKind = -1;
        public int HvtMaxHp;
        public int DamageDealt, DamageTaken;
        // C3 THE TWO GAMES — the ENCOUNTER-COMPLETION decomposition. EnemiesStart is the force at
        // deploy; nothing recorded what the force GREW to, or how much of it the squad actually had
        // to beat. Without these three, "kill objectives are 43 points harder" is a bare win-rate
        // and every mechanism for it is equally plausible.
        //   EnemiesAdded : bodies spawned into this mission AFTER deploy by any reinforcement path
        //                  (the anti-turtle pressure clock, DEFEND's wave schedule, LAST STAND's
        //                  horde). EnemiesStart does NOT include them, so EnemiesKilled can exceed
        //                  EnemiesStart — which is itself the treadmill's fingerprint.
        //   MaxPressure  : the highest anti-turtle rung this mission reached (0 when the clock did
        //                  not run — it only runs on Eliminate/Hack/Decapitate; see
        //                  Game.PressureClockObjective).
        // Read-only bookkeeping: no RNG draws, no gameplay effect, harness-gated like the rest.
        public int EnemiesAdded, MaxPressure;
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
        // X1 THE EXCHANGE — the shot-gate decomposition. meaningful-choices/turn is an average
        // over PLAYER TURNS, but CountMeaningfulChoices only scores a soldier that (a) is alive
        // and able to act and (b) has at least one legal shot from where it stands. So the ratio
        // conflates three different things: how many soldiers are still standing, how often they
        // are in contact at all, and how rich the decision is when they ARE. These three counters
        // split them so a lever that lengthens fights can be told apart from one that flattens
        // decisions. Read-only bookkeeping — no RNG draws, no gameplay effect.
        //   ActingSoldierTurns: soldier-turns where the soldier was alive + CanAct (roster size).
        //   ArmedSoldierTurns : the subset of those that had >=1 legal shot (in contact).
        //   ArmedTurns        : player turns where at least one soldier was armed.
        public int ActingSoldierTurns, ArmedSoldierTurns, ArmedTurns;
        // W4 THE SECOND AXIS — the CHOICE decomposition beneath the shot gate. X1 proved
        // choices/ARMED-soldier-turn (~1.5) is the binding constraint but could not say WHICH of
        // CountMeaningfulChoices' two axes was starved. These three split it:
        //   LosTargetSum   : sum over ARMED soldier-turns of how many foes were in range+LoS at
        //                    all (the raw simultaneous-presentation number this wave targets).
        //   TargetChoiceSum: the (a) "which target" contribution only (rival shots within 12%).
        //   PosChoiceSum   : the (b) "where do I stand after firing" contribution only (capped 2).
        // TargetChoiceSum + PosChoiceSum == MeaningfulChoiceSum by construction. Read-only
        // bookkeeping — no RNG draws, no gameplay effect.
        public int LosTargetSum, TargetChoiceSum, PosChoiceSum;
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
        // THE HEAT PIN: `Heat` is stamped ONCE at BeginRun and is the rung the batch pinned; it was
        // never updated when a field event raised the run's HeatLevel, so the archive could not say
        // how many campaigns had leaked. HeatEnd is the run's HeatLevel at EndRun (== Heat unless a
        // leak happened, or the caller did not stamp it); RunTurns is Game.RunTurns at EndRun (-1
        // when the caller did not stamp it); EndMissionNo / EndObjective are the LAST mission the
        // run played (derived from Missions), so a STALEMATE row says WHERE the bot stalled.
        public int HeatEnd = -1, RunTurns = -1;
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
        public readonly List<string> EventChoices = new();     // FUL-1: field-event picks as "id:arm"
        // FUL-13: intel cash-flow — signed deltas recorded at every Run.Intel mutation site.
        // IntelHeatBonus is the Heat.IntelBonus component of mission-clear income (the
        // "does heat refund its own difficulty through the shop?" read).
        public int IntelEarned, IntelHeatBonus, IntelSpent;
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

    // ── C3 THE TWO GAMES: mid-mission REINFORCEMENTS + the anti-turtle rung ──────
    // Per-mission (unlike RecordSpawn, which is batch-global) because the whole question is
    // whether ONE objective class gets a growing force while the other does not. Both are pure
    // counters written from sites that have already decided everything; neither takes a draw.
    public static void RecordReinforce(int n)
    {
        if (!Enabled || _mission == null || n <= 0) return;
        _mission.EnemiesAdded += n;
    }
    public static void RecordPressure(int rung)
    {
        if (!Enabled || _mission == null) return;
        if (rung > _mission.MaxPressure) _mission.MaxPressure = rung;
    }

    // ── FUL-1: BOON PROC counters ────────────────────────────────────────────────
    // Fires-at-the-effect-site telemetry for the verb boons. Pick frequency alone can't
    // say whether a boon ever DOES anything — the FUL research suspicion is that several
    // (SHK/FDR/RCL) are picked and then never fire on the boards we generate. A 0 in this
    // table for a picked code IS the finding FUL-5/FUL-6 consume. Batch-global (procs are
    // aggregate texture, same rationale as the action mix); '-' rows = not instrumented.
    static readonly Dictionary<string, int> _boonProcs = new();
    public static readonly string[] ProcInstrumented = { "SHK", "TRR", "FDR", "PYR", "FST", "RCL" };
    public static void RecordProc(string code)
    {
        if (!Enabled || string.IsNullOrEmpty(code)) return;
        Bump(_boonProcs, code);
    }
    /// FUL-6 PODTEST read hook: current proc tally for a code (0 if never fired). Test-only read;
    /// the balance report keeps printing from the dictionary directly.
    public static int ProcCount(string code) => _boonProcs.GetValueOrDefault(code, 0);

    // ── FUL-1: ARENA FUNNEL ──────────────────────────────────────────────────────
    // Mission.Build's layout decision, split into the three exits that sum to 100% of
    // builds: the authored roll won AND the connectivity guard accepted (applied), the
    // roll won but the guard REJECTED the template (falls to procedural — previously
    // indistinguishable from a lost roll), or the roll itself chose procedural. Batch-
    // global: Build runs before BeginMission (the RecordSpawn precedent).
    public const int ArenaAuthored = 0, ArenaReject = 1, ArenaProcRoll = 2;
    static readonly int[] _arenaFunnel = new int[3];
    public static void RecordArenaFunnel(int stage)
    {
        if (!Enabled || stage < 0 || stage > 2) return;
        _arenaFunnel[stage]++;
    }

    // ── FUL-1: FIELD-EVENT choice telemetry ──────────────────────────────────────
    // A resolved event node — id + chosen arm index, run-scoped like boons so the report
    // can associate run outcomes with arms (the table FUL-10's new forks will consume).
    public static void RecordEvent(string id, int arm)
    {
        if (!Enabled || _run == null || string.IsNullOrEmpty(id)) return;
        _run.EventChoices.Add($"{id}:{arm}");
    }

    // ── FUL-13: INTEL CASH-FLOW ──────────────────────────────────────────────────
    // Signed deltas recorded at the Run.Intel mutation sites (mission-clear award incl. its
    // heat-bonus component, secondaries, cache pickups, event arms/gambles, shop/armory
    // spends) so the report can answer the FUL-13 economy question: does heat REFUND its
    // own difficulty through the shop? Run-scoped (report keys by heat); telemetry-only,
    // zero draws — CRN-safe by construction (the FUL-1 precedent).
    public static void RecordIntel(int delta, int heatBonus = 0)
    {
        if (!Enabled || _run == null) return;
        if (delta >= 0) _run.IntelEarned += delta; else _run.IntelSpent -= delta;
        _run.IntelHeatBonus += heatBonus;
    }

    // ── FUL-7: DOWN / bleed-out telemetry ────────────────────────────────────────
    // Downs staged and how each resolved: revived (a corpsman's PATCH), recovered (a won
    // field / the endless breather), expired (bled out), or FINISHED (killed while down —
    // AoE/fire/second-lethal, review F2). save-rate = 1 - (expired+finished)/downs — the
    // measured number DESIGN §4's death-stakes re-grade reads. CorpsmanMissions counts
    // missions with a corpsman fielded (the PATCH per-presence denominator). Batch-global.
    static int _downs, _downExpired, _downFinished, _downRevived, _downRecovered, _corpsmanMissions;
    public static void RecordDown()            { if (Enabled) _downs++; }
    public static void RecordDownExpired()     { if (Enabled) _downExpired++; }
    public static void RecordDownFinished()    { if (Enabled) _downFinished++; }
    public static void RecordDownRevived()     { if (Enabled) _downRevived++; }
    public static void RecordDownRecovered()   { if (Enabled) _downRecovered++; }
    public static void RecordCorpsmanFielded() { if (Enabled) _corpsmanMissions++; }
    public static int DownCount => _downs;     // DOWNTEST read hook

    // ── W1 TRUE INSTRUMENT: SHOT-GAP deciles ─────────────────────────────────────
    // For every ARMED soldier-turn (Game.Autopilot.CountMeaningfulChoices already gathers the
    // ShotValue of every legal shot from where the soldier stands), the normalised dominance of
    // the best option over the runner-up: gap = (best - second) / best, in [0,1]. Bucketed into
    // ten deciles; a lone legal target scores second=0, i.e. gap 1.0, decile 9.
    //
    // WHY: W4 measured choices/ARMED-soldier-turn as a near-invariant ~1.6 across five
    // structurally different levers and concluded the metric's two halves cancel. That
    // diagnosis assumes the SPEC is aimed at the right axis. CountMeaningfulChoices answers
    // "how many options are within 12% of best?" — a threshold, so it cannot distinguish "two
    // shots, both 40 value" from "two shots, 40 and 39". This histogram is the same population
    // measured as a CONTINUOUS distribution, which can: a mass piled at decile 9 means the shot
    // picks itself and no threshold could ever have found a decision there.
    // Batch-global (the arena-funnel precedent). ZERO RNG draws, zero allocation: it reads values
    // the caller already computed for its own count, and the runner-up costs one extra linear
    // scan of that same short list.
    static readonly int[] _shotGapDeciles = new int[10];
    static int _shotGapArmedTurns;
    static double _shotGapSum;
    public static void RecordShotGap(float best, float second)
    {
        if (!Enabled || best <= 0f) return;
        float gap = (best - Math.Max(0f, second)) / best;
        int d = (int)(gap * 10f);
        if (d < 0) d = 0; else if (d > 9) d = 9;
        _shotGapDeciles[d]++;
        _shotGapArmedTurns++;
        _shotGapSum += gap;
    }

    // ── C2 THE OPPONENT DECLINES: the ENEMY DECISION MIX ─────────────────────────
    // What the opponent actually DID with each contested act-opportunity, and — for the acts
    // where a shot was on the table — how good that shot was. Before this wave the enemy's
    // decision surface was measured by exactly one number (W2's "did the act end unspent?"),
    // which cannot distinguish "took a 12% shot" from "took an 82% shot" and cannot see a
    // shot that was DECLINED at all, because none ever were.
    //
    // CONTESTED ONLY, the W2 rule: an act with every soldier down is Ai.Plan's empty-plan
    // early return and idles by design (docs/DESIGN.md §5.1). Those are counted separately as
    // `allDowned` so the denominator here is honest and never inflated by the bleed-out window.
    //
    // Two histograms over the SAME five hit-chance bands, so taken and declined shots are
    // directly comparable: 0-19 / 20-39 / 40-59 / 60-79 / 80+.
    // Expected damage (Combat.ExpectedDamage — graze-aware, armor-aware, PURE) is summed per
    // band so the report can price a band in damage rather than in probability: it is the unit
    // the decline bars are actually set in.
    // Batch-global (the arena-funnel / action-mix precedent). ZERO RNG draws.
    public const int ShotBands = 5;
    public static int ShotBand(int hitPct)
    {
        int b = hitPct / 20;
        return b < 0 ? 0 : b > ShotBands - 1 ? ShotBands - 1 : b;
    }
    static readonly Dictionary<string, int> _enemyDecisions = new();
    static readonly int[] _enemyShotTaken = new int[ShotBands];
    static readonly int[] _enemyShotDeclined = new int[ShotBands];
    static readonly double[] _enemyShotTakenExp = new double[ShotBands];
    static readonly double[] _enemyShotDeclinedExp = new double[ShotBands];
    static int _enemyActsContested, _enemyActsAllDowned, _enemyActsWithShot, _enemyShotPreempted;

    /// One contested enemy act-opportunity. `verb` is the branch that actually FIRED in
    /// Game.UpdateEnemy's ActAfterMove chain (assigned at each branch — never re-derived from
    /// state afterwards, which is how a "shoot then reposition" act would misclassify).
    /// `shotHit`/`shotExp` describe the shot the planner had on the table from the tile it
    /// chose: taken when verb=="shoot", DECLINED when it dropped one (verb is then whatever it
    /// did instead). shotHit < 0 means no shot was available at all.
    public static void RecordEnemyDecision(string verb, bool contested, int shotHit, float shotExp, bool declined)
    {
        if (!Enabled) return;
        if (!contested) { _enemyActsAllDowned++; return; }
        _enemyActsContested++;
        Bump(_enemyDecisions, verb);
        if (shotHit < 0) return;
        _enemyActsWithShot++;
        int b = ShotBand(shotHit);
        // Three exits, and conflating any two of them is how a decline rate lies:
        //   TAKEN     — the shot branch fired.
        //   DECLINED  — the planner had it and dropped it for a better use of the action (C2).
        //   PREEMPTED — a grenade / smoke / shove / sap / heal claimed the action instead, or
        //               the exec refused a stale plan. Neither a decline nor a shot; counted
        //               so the two histograms sum to `actsWithShot` and nothing hides.
        if (verb == "shoot") { _enemyShotTaken[b]++; _enemyShotTakenExp[b] += shotExp; }
        else if (declined)   { _enemyShotDeclined[b]++; _enemyShotDeclinedExp[b] += shotExp; }
        else                 _enemyShotPreempted++;
    }
    /// C2: an ENEMY overwatch/brace lane that actually FIRED. The decline gate's one judgement
    /// constant (DeclineWatchRatio) is a bet on how often a held lane pays off; this is the
    /// counter that lets the bet be checked instead of asserted. Counted at the reaction site in
    /// Game.OnUnitEnteredTile, so it counts SHOTS FIRED, not lanes that merely existed.
    static int _enemyReactions;
    public static void RecordEnemyReaction() { if (Enabled) _enemyReactions++; }

    /// Band totals, used by the report and the aggregate JSON.
    public static int EnemyShotsDeclined { get { int n = 0; foreach (int v in _enemyShotDeclined) n += v; return n; } }
    public static int EnemyShotsTaken    { get { int n = 0; foreach (int v in _enemyShotTaken)    n += v; return n; } }

    // ── W1: HARNESS HEALTH ───────────────────────────────────────────────────────
    // The machine the batch ran on, stamped into the artifact itself. Every measurement wave
    // in docs/ so far has had to reconstruct "was the container busy?" from the wall-clock line
    // in the log — and X2's own contract warns that several agents share this container. A
    // 40-campaign rung whose loadavg went 2 -> 30 mid-round is a different instrument at the
    // end than at the start, and nothing in the JSON recorded that.
    static DateTime _batchStartUtc = DateTime.UtcNow;
    static string _loadAtStart = "";
    static string ReadLoadAvg()
    {
        try { return System.IO.File.ReadAllText("/proc/loadavg").Trim(); }
        catch { return ""; }   // non-Linux / restricted: absent, never fatal
    }

    public static void Reset()
    {
        Runs.Clear(); _run = null; _mission = null;
        _spawnsFactionByClass.Clear(); _spawnsDefaultByClass.Clear();
        _actionsByPolicy.Clear();
        _boonProcs.Clear();
        _arenaFunnel[0] = _arenaFunnel[1] = _arenaFunnel[2] = 0;
        _downs = _downExpired = _downFinished = _downRevived = _downRecovered = _corpsmanMissions = 0;   // FUL-7
        Array.Clear(_shotGapDeciles, 0, _shotGapDeciles.Length); _shotGapArmedTurns = 0; _shotGapSum = 0; // W1
        _enemyDecisions.Clear();                                                           // C2
        Array.Clear(_enemyShotTaken, 0, ShotBands); Array.Clear(_enemyShotDeclined, 0, ShotBands);
        Array.Clear(_enemyShotTakenExp, 0, ShotBands); Array.Clear(_enemyShotDeclinedExp, 0, ShotBands);
        _enemyActsContested = _enemyActsAllDowned = _enemyActsWithShot = _enemyShotPreempted = 0;
        _enemyReactions = 0;
        _batchStartUtc = DateTime.UtcNow; _loadAtStart = ReadLoadAvg();                    // W1
        Slot = -1;
    }

    public static void BeginRun(int heat, string policy = "greedy", string mode = "campaign")
    {
        if (!Enabled) return;
        _run = new RunRec
        {
            Heat = heat, HeatEnd = heat,
            Policy = string.IsNullOrEmpty(policy) ? "greedy" : policy,
            Mode = string.IsNullOrEmpty(mode) ? "campaign" : mode,
            Slot = Slot
        };
        Runs.Add(_run);
    }

    public static void BeginMission(int mission, string objective, int heat, int squad, int enemies, int layout = -1,
                                    int deploy = Sightline.Mission.DeployFrontal,
                                    string nodeKind = "", int squadHpPct = 100,
                                    int hvtKind = -1, int hvtMaxHp = 0,
                                    string biome = "", int groundTiles = 0)
    {
        if (!Enabled) return;
        if (_run == null) BeginRun(heat);
        _mission = new MissionRec
        {
            Mission = mission, Objective = objective, Heat = heat,
            SquadStart = squad, EnemiesStart = enemies, Layout = layout, Deploy = deploy,
            NodeKind = nodeKind ?? "", SquadHpPct = squadHpPct,
            HvtKind = hvtKind, HvtMaxHp = hvtMaxHp,
            Biome = biome ?? "", GroundTiles = groundTiles
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
    public static void RecordPlayerTurn(int meaningfulChoices, int lead,
                                        int actingSoldiers = 0, int armedSoldiers = 0,
                                        int losTargets = 0, int targetChoices = 0, int posChoices = 0)
    {
        if (!Enabled || _mission == null) return;
        _mission.PlayerTurns++;
        _mission.MeaningfulChoiceSum += Math.Max(0, meaningfulChoices);
        // X1 shot-gate decomposition (see MissionRec) — pure bookkeeping.
        _mission.ActingSoldierTurns += Math.Max(0, actingSoldiers);
        _mission.ArmedSoldierTurns  += Math.Max(0, armedSoldiers);
        if (armedSoldiers > 0) _mission.ArmedTurns++;
        // W4 choice decomposition (see MissionRec) — also pure bookkeeping.
        _mission.LosTargetSum    += Math.Max(0, losTargets);
        _mission.TargetChoiceSum += Math.Max(0, targetChoices);
        _mission.PosChoiceSum    += Math.Max(0, posChoices);
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

    // THE HEAT PIN: heatEnd / runTurns are optional so the mode seams (endless, skirmish, daily)
    // and the batch's defensive frame-cap closes need not change; a caller that has them (the
    // campaign's three exits in Game.cs, the batch's caps in Program.cs) passes them.
    public static void EndRun(bool win, int missionsCleared, string lossCause, int heatEnd = -1, int runTurns = -1)
    {
        if (!Enabled || _run == null) return;
        _run.Win = win;
        _run.MissionsCleared = missionsCleared;
        _run.LossCause = lossCause ?? "";
        if (heatEnd >= 0) _run.HeatEnd = heatEnd;
        if (runTurns >= 0) _run.RunTurns = runTurns;
        _run = null;
    }

    /// The last mission a run played, or null. Where a STALEMATE row stalled; what a leaked run
    /// was doing when its heat rose.
    static MissionRec LastMission(RunRec r) => r.Missions.Count == 0 ? null : r.Missions[r.Missions.Count - 1];
    static int HeatRaisingPicks(RunRec r)
    {
        int n = 0;
        foreach (var e in r.EventChoices) if (HeatArms.Contains(e)) n++;
        return n;
    }
    static readonly HashSet<string> HeatArms = new(EventCatalog.HeatRaisingArms());

    // ── aggregate report ─────────────────────────────────────────────────────
    static string Pct(int num, int den) => den == 0 ? "  -  " : $"{100.0 * num / den,4:0}%";

    // FUL-1: binomial ±SE in percentage points — appended to n<30 win-rate rows so a
    // small-sample cell can't masquerade as signal (the Defend-23%-at-n=26 lesson).
    // Plain binomial (100·√(p(1−p)/n)); note it degenerates to ±0 at p∈{0,1} — an n<30
    // row printing ±0 means "all one outcome so far", not "certain".
    static string Se(int wins, int n)
    {
        if (n <= 0 || n >= 30) return "";
        double p = (double)wins / n;
        return $" ±{100.0 * Math.Sqrt(p * (1 - p) / n):0}";
    }
    static double SeVal(int wins, int n)
    {
        if (n <= 0) return 0.0;
        double p = (double)wins / n;
        return Math.Round(100.0 * Math.Sqrt(p * (1 - p) / n), 1);
    }

    // W2: paired per-slot outcomes over CAMPAIGN runs — a slot pairs when it has exactly one
    // greedy and one sloppy leg (the batch's normal shape). Shared by Report + BuildSummary.
    // Review fix: keyed by (Slot, Heat), not Slot alone — chunks sharing a BALANCE_BASE across
    // different heat pins would otherwise collide slot ids and dissolve into 4-run non-pairs.
    // FUL-1: returns the full per-slot pair records (not just the tallies) so the report can
    // print them and compute the all-pairs missions-cleared margin.
    static List<(int slot, int heat, RunRec g, RunRec s)> PairList(List<RunRec> campRuns)
        => campRuns.Where(r => r.Slot >= 0)
            .GroupBy(r => (r.Slot, r.Heat))
            .Select(gr => (slot: gr.Key.Slot, heat: gr.Key.Heat,
                           g: gr.Where(r => r.Policy == "greedy").ToList(),
                           s: gr.Where(r => r.Policy == "sloppy").ToList()))
            .Where(p => p.g.Count == 1 && p.s.Count == 1)
            .Select(p => (p.slot, p.heat, p.g[0], p.s[0]))
            .OrderBy(p => p.heat).ThenBy(p => p.slot)
            .ToList();

    static (int pairs, int concordant, int greedyOnlyWon, int sloppyOnlyWon) PairedOutcomes(List<RunRec> campRuns)
    {
        var pairs = PairList(campRuns);
        return (pairs.Count,
                pairs.Count(p => p.g.Win == p.s.Win),
                pairs.Count(p => p.g.Win && !p.s.Win),
                pairs.Count(p => !p.g.Win && p.s.Win));
    }

    // FUL-1: the all-pairs MISSIONS-CLEARED margin (greedy − sloppy per slot, averaged over
    // EVERY pair — win/win and loss/loss pairs contribute too, unlike the discordant-only
    // binary gap). A continuous paired outcome uses far more of each slot's information, so
    // its CI is roughly half the binary gap's at the same n. SE = sd/√n over the margins.
    // Review nano: at n=1 pair the sd (hence SE) degenerates to 0.00 — like the Se helper's
    // p∈{0,1} rows, a printed ±0.00 there means "one sample", not "certain".
    static (int n, double mean, double se) PairedMargin(List<(int slot, int heat, RunRec g, RunRec s)> pairs)
    {
        if (pairs.Count == 0) return (0, 0.0, 0.0);
        var margins = pairs.Select(p => (double)(p.g.MissionsCleared - p.s.MissionsCleared)).ToList();
        double mean = margins.Average();
        double sd = margins.Count < 2 ? 0.0
            : Math.Sqrt(margins.Sum(m => (m - mean) * (m - mean)) / (margins.Count - 1));
        return (margins.Count, Math.Round(mean, 2), Math.Round(sd / Math.Sqrt(margins.Count), 2));
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
            var slotPairs = PairList(campRuns);
            var (nPairs, conc, dPlus, dMinus) = PairedOutcomes(campRuns);
            if (nPairs > 0)
            {
                double pairedGap = 100.0 * (dPlus - dMinus) / nPairs;
                sb.AppendLine($"  PAIRED (same-seed slots): pairs={nPairs}  concordant={conc}  discordant greedy-only-won={dPlus} / sloppy-only-won={dMinus}  paired gap {pairedGap,4:0} pts");
                // FUL-1: the raw per-slot records the paired tallies summarize — auditable at a
                // glance (which world flipped which way), and the margin column feeds the
                // all-pairs missions-cleared margin below (every pair contributes, ~halves CI).
                sb.AppendLine("  PER-SLOT RECORDS (greedy | sloppy: outcome + missions cleared; margin = g−s):");
                foreach (var p in slotPairs)
                    sb.AppendLine($"    slot {p.slot,3} @h{p.heat}: {(p.g.Win ? "W" : "L")} {p.g.MissionsCleared} | {(p.s.Win ? "W" : "L")} {p.s.MissionsCleared}   margin {p.g.MissionsCleared - p.s.MissionsCleared:+0;-0;+0}");
                var (mn, mMean, mSe) = PairedMargin(slotPairs);
                sb.AppendLine($"  PAIRED MARGIN (missions cleared, all pairs): {mMean:+0.00;-0.00;+0.00} ±{mSe:0.00} SE  (n={mn} pairs)");
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
            // X1 — the shot-gate decomposition of that first number (see MissionRec).
            double actingT = tMissions.Sum(m => (double)m.ActingSoldierTurns);
            double armedT  = tMissions.Sum(m => (double)m.ArmedSoldierTurns);
            double turnsT  = tMissions.Sum(m => (double)m.PlayerTurns);
            double armedTurns = tMissions.Sum(m => (double)m.ArmedTurns);
            if (actingT > 0)
                sb.AppendLine($"  [shot-gate] acting-soldiers/turn {actingT / turnsT:0.00}"
                    + $"   armed-soldiers/turn {armedT / turnsT:0.00}"
                    + $"   armed-fraction {100.0 * armedT / actingT:0}%"
                    + $"   turns-with-a-shot {100.0 * armedTurns / turnsT:0}%"
                    + $"   choices/ARMED-soldier-turn {(armedT > 0 ? choicesPerTurn * turnsT / armedT : 0):0.00}");
            // W4 — which of the two choice axes is actually starved (see MissionRec).
            double losT = tMissions.Sum(m => (double)m.LosTargetSum);
            double tgtC = tMissions.Sum(m => (double)m.TargetChoiceSum);
            double posC = tMissions.Sum(m => (double)m.PosChoiceSum);
            if (armedT > 0)
                sb.AppendLine($"  [choice-split] los-targets/ARMED {losT / armedT:0.00}"
                    + $"   target-choices/ARMED {tgtC / armedT:0.00}"
                    + $"   position-choices/ARMED {posC / armedT:0.00}");
        }

        // Run-completion by heat — the metric the Heat ladder is supposed to bend. The
        // per-MISSION win-rate below conflates "harder rung" with "how far the run got"
        // (survivorship bias), so it can't show the ladder's shape; this one can.
        if (campRuns.Count > 0)
        {
            sb.AppendLine("\nRUN COMPLETION BY HEAT (full-campaign clears):");
            foreach (var g in campRuns.GroupBy(r => r.Heat).OrderBy(g => g.Key))
                sb.AppendLine($"  heat {g.Key}: {Pct(g.Count(r => r.Win), g.Count())}{Se(g.Count(r => r.Win), g.Count())}  (n={g.Count()} runs, avg {g.Average(r => (double)r.MissionsCleared):0.0} missions)");
            // THE HEAT PIN: does "heat N" mean heat N in this batch?
            int raised = campRuns.Count(r => r.HeatEnd > r.Heat);
            int offRung = campRuns.Sum(r => r.Missions.Count(m => m.Heat != r.Heat));
            sb.AppendLine($"  HEAT LEAK: pinned={(EventCatalog.HeatPinned ? "yes" : "NO")}  heat-raising picks={campRuns.Sum(HeatRaisingPicks)}  campaigns raised={raised}  missions off-rung={offRung}/{campRuns.Sum(r => r.Missions.Count)}");
            int stM = campRuns.Count(r => r.LossCause == StalemateMission), stR = campRuns.Count(r => r.LossCause == StalemateRun);
            if (stM + stR > 0)
                sb.AppendLine($"  STALEMATES: mission-cap {stM}  run-cap {stR}   "
                              + string.Join("  ", campRuns.Where(r => IsStalemate(r.LossCause)).Select(r => $"[slot {r.Slot} {r.Policy[0]} m{LastMission(r)?.Mission} {LastMission(r)?.Objective} {(r.LossCause == StalemateRun ? "run" : "mis")} rt={r.RunTurns}]")));
        }

        // ── FUL-13: INTEL ECONOMY BY HEAT ────────────────────────────────────────────
        // Cash-flow per run, keyed by heat. heat-bonus share of income is the flood signal:
        // if the ladder's higher rungs bank MORE unspent intel than h0, heat is refunding
        // its own difficulty through the shop and the economy needs a drain.
        if (campRuns.Count > 0 && campRuns.Any(r => r.IntelEarned > 0 || r.IntelSpent > 0))
        {
            sb.AppendLine("\nINTEL ECONOMY BY HEAT (campaign runs; heat-bonus = the Heat.IntelBonus share of income):");
            foreach (var g in campRuns.GroupBy(r => r.Heat).OrderBy(g => g.Key))
                sb.AppendLine($"  heat {g.Key}: earned {g.Average(r => (double)r.IntelEarned),6:0.0}/run"
                    + $"   heat-bonus {g.Average(r => (double)r.IntelHeatBonus),5:0.0} ({Pct(g.Sum(r => r.IntelHeatBonus), Math.Max(1, g.Sum(r => r.IntelEarned)))} of income)"
                    + $"   spent {g.Average(r => (double)r.IntelSpent),6:0.0}"
                    + $"   unspent {g.Average(r => (double)(r.IntelEarned - r.IntelSpent)),6:0.0}");
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
            // Win-rate by heat (FUL-1: n<30 rows carry a binomial ±SE — see Se)
            sb.AppendLine("\nMISSION WIN-RATE BY HEAT:");
            foreach (var g in missions.GroupBy(m => m.Heat).OrderBy(g => g.Key))
                sb.AppendLine($"  heat {g.Key}: {Pct(g.Count(m => m.Win), g.Count())}{Se(g.Count(m => m.Win), g.Count())}  (n={g.Count()}, avg {g.Average(m => (double)m.Turns):0.0} turns)");

            // Win-rate by objective
            sb.AppendLine("\nMISSION WIN-RATE BY OBJECTIVE:");
            foreach (var g in missions.GroupBy(m => m.Objective).OrderByDescending(g => g.Count()))
                sb.AppendLine($"  {g.Key,-11}: {Pct(g.Count(m => m.Win), g.Count())}{Se(g.Count(m => m.Win), g.Count())}  (n={g.Count()}, avg {g.Average(m => (double)m.Turns):0.0} turns)");

            // ── W8 THE HALF WALL: OBJECTIVE x CAMPAIGN NODE KIND ─────────────────────────────
            // The row above pools a CAPSTONE with a mid-run node whenever the map deals the same
            // objective to both, and for Decapitate it always does: Run.cs makes exactly one Boss
            // node (the map's last) and CardForNode makes the Boss ALWAYS Decapitate. Over the L2
            // archive's 960 campaigns the flat row read 63.7% while its two halves read 69.7%
            // (finale, n=479) and 46.0% (mid-run, n=163) — a 23.7-point gap invisible above and
            // costing 480 campaigns to find. This cross-tab makes it a column you cannot miss.
            // The DEPTH cells beside them exist because node kind is not the only thing an
            // objective row pools. `Run.DeckObjective` deals a fight objective from a hash of the
            // COLUMN, and a column IS a mission number — so an objective's win rate carries a
            // mission-depth mix as well as a difficulty, and the two are not separable from the
            // flat row either. Read the cells, not the row.
            sb.AppendLine("\nMISSION WIN-RATE BY OBJECTIVE x NODE KIND (a capstone must never pool with a mid-run node):");
            foreach (var og in missions.GroupBy(m => m.Objective).OrderByDescending(g => g.Count()))
            {
                string cells = string.Join("  ", og.GroupBy(m => string.IsNullOrEmpty(m.NodeKind) ? "?" : m.NodeKind)
                    .OrderByDescending(g => g.Count())
                    .Select(g => $"{g.Key}:{Pct(g.Count(m => m.Win), g.Count())}(n={g.Count()})"));
                string depth = string.Join(" ", og.GroupBy(m => m.Mission).OrderBy(g => g.Key)
                    .Select(g => $"m{g.Key}:{(int)Math.Round(100.0 * g.Count(m => m.Win) / g.Count())}%({g.Count()})"));
                sb.AppendLine($"  {og.Key,-11}: {cells}");
                sb.AppendLine($"  {"",-11}  depth: {depth}");
            }

            // ── C3 THE TWO GAMES: the ENCOUNTER-COMPLETION decomposition ─────────────────────
            // W8's cross-tab established a 43-point win-rate gap on mid-run nodes between the two
            // objectives that end when bodies fall and the six that end when a tile is reached, a
            // timer is held or a charge is set. A win-rate cannot say WHY. These columns can:
            //   force  = the force at deploy (is one class simply outnumbered?)
            //   +rf    = bodies added AFTER deploy by any reinforcement path (the treadmill)
            //   killed = bodies the squad actually put down
            //   clear% = killed / (force + reinforcements) — the share of the encounter FOUGHT.
            //            This is the decline-the-fight number: a class that wins by walking away
            //            reads low here and high in win%, and no other column can show that.
            //   prs    = mean high-water anti-turtle rung (0 where the clock does not run)
            // Restricted to mid-run node kinds (Combat + Elite) for exactly W8's reason: pooling
            // Start (every campaign's 97%-win mission 1, always Eliminate) or Boss (the de-stacked
            // finale, always Decapitate) puts the two classes on different populations and the
            // comparison stops meaning anything.
            var midrun = missions.Where(m => m.NodeKind == "Combat" || m.NodeKind == "Elite").ToList();
            if (midrun.Count > 0)
            {
                sb.AppendLine("\nENCOUNTER COMPLETION on MID-RUN nodes (Combat+Elite) — clear% is the share of the fight actually fought:");
                sb.AppendLine("  objective     n   win%   turns   force    +rf  killed  clear%   prs  WONclear%");
                void EncRow(string lbl, List<MissionRec> rows)
                {
                    if (rows.Count == 0) return;
                    double force = rows.Average(m => (double)m.EnemiesStart);
                    double rf = rows.Average(m => (double)m.EnemiesAdded);
                    double kill = rows.Average(m => (double)m.EnemiesKilled);
                    int tot = rows.Sum(m => m.EnemiesStart + m.EnemiesAdded);
                    var won = rows.Where(m => m.Win).ToList();
                    int wtot = won.Sum(m => m.EnemiesStart + m.EnemiesAdded);
                    sb.AppendLine($"  {lbl,-11} {rows.Count,4} {Pct(rows.Count(m => m.Win), rows.Count),5}  {rows.Average(m => (double)m.Turns),6:0.0}"
                        + $"  {force,6:0.00} {rf,6:0.00}  {kill,6:0.00}  {(tot > 0 ? 100.0 * rows.Sum(m => m.EnemiesKilled) / tot : 0),6:0.0}"
                        + $"  {rows.Average(m => (double)m.MaxPressure),4:0.00}"
                        + $"  {(wtot > 0 ? 100.0 * won.Sum(m => m.EnemiesKilled) / wtot : 0),7:0.0} (n={won.Count})");
                }
                foreach (var og in midrun.GroupBy(m => m.Objective).OrderByDescending(g => g.Count()))
                    EncRow(og.Key, og.ToList());
                sb.AppendLine("  ---- the two classes ----");
                EncRow("KILL", midrun.Where(m => Run.IsKillObjective(m.Objective)).ToList());
                EncRow("NON-KILL", midrun.Where(m => !Run.IsKillObjective(m.Objective)).ToList());
            }

            // ── W8: the DECAPITATE punch-through target, split by whether it took the buff ────
            // DesignateHvt gives a rank-and-file HVT +(base + perMission*depth) HP and +aim, but
            // EXEMPTS an ELITE because "an ELITE is ALREADY a tuned boss". The finale is always an
            // ELITE, so the exemption removed the stat-check wall from the boss and left it in
            // every mid-run Decapitate. These two rows are that comparison, held side by side.
            var hvtMissions = missions.Where(m => m.HvtKind >= 0).ToList();
            if (hvtMissions.Count > 0)
            {
                sb.AppendLine($"\nDECAPITATE HVT (buff = +{Combat.HvtHpBonusBase}{(Combat.HvtHpBonusPerMission >= 0 ? "+" : "")}{Combat.HvtHpBonusPerMission}*mission HP, +{Combat.HvtAimBonus} aim; ELITE exempt):");
                foreach (var g in hvtMissions.GroupBy(m => m.HvtKind).OrderBy(g => g.Key))
                    sb.AppendLine($"  {(g.Key == 1 ? "BUFFED  " : "EXEMPT  ")}: {Pct(g.Count(m => m.Win), g.Count())}{Se(g.Count(m => m.Win), g.Count())}"
                        + $"  (n={g.Count()}, avg {g.Average(m => (double)m.Turns):0.0} turns, HVT {g.Average(m => (double)m.HvtMaxHp):0.0} max HP)"
                        + $"  [{string.Join(" ", g.GroupBy(m => m.Mission).OrderBy(x => x.Key).Select(x => $"m{x.Key}:{(int)Math.Round(100.0 * x.Count(m => m.Win) / x.Count())}%({x.Count()})"))}]");
            }

            // Win-rate by mission number (difficulty curve)
            sb.AppendLine("\nMISSION WIN-RATE BY MISSION #:");
            foreach (var g in missions.GroupBy(m => m.Mission).OrderBy(g => g.Key))
                sb.AppendLine($"  m{g.Key}: {Pct(g.Count(x => x.Win), g.Count())}{Se(g.Count(x => x.Win), g.Count())}  (n={g.Count()})");

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
            // FUL-1: each row is STRATIFIED BY MISSION # — an arena's aggregate win-rate
            // silently mixes difficulty rungs (pre-FUL-9 the hint even COUPLED arena to
            // mission number); the per-mission cells expose the mix (cell = m<N>:win%(n)).
            // n<30 rows carry ±SE.
            string ByMissionCells(IEnumerable<MissionRec> ms) => string.Join(" ",
                ms.GroupBy(m => m.Mission).OrderBy(g => g.Key)
                  .Select(g => $"m{g.Key}:{(int)Math.Round(100.0 * g.Count(m => m.Win) / g.Count())}%({g.Count()})"));
            sb.AppendLine($"\nMISSION WIN-RATE BY ARENA (authored layouts, n>=3; procedural fallback {Pct(procN, missions.Count)} of {missions.Count} missions):");
            foreach (var g in missions.Where(m => m.Layout >= 0).GroupBy(m => m.Layout)
                                       .Where(g => g.Count() >= 3).OrderBy(g => g.Key))
                sb.AppendLine($"  arena {g.Key,2}: {Pct(g.Count(m => m.Win), g.Count())}{Se(g.Count(m => m.Win), g.Count())}  (n={g.Count()}, avg {g.Average(m => (double)m.Turns):0.0} turns)  [{ByMissionCells(g)}]");
            if (procN >= 3)
                sb.AppendLine($"  procedural: {Pct(missions.Count(m => m.Layout < 0 && m.Win), procN)}{Se(missions.Count(m => m.Layout < 0 && m.Win), procN)}  (n={procN}, avg {missions.Where(m => m.Layout < 0).Average(m => (double)m.Turns):0.0} turns)  [{ByMissionCells(missions.Where(m => m.Layout < 0))}]");
        }

        // ── W4 THE SECOND AXIS: WIN / TURNS / DECISION DENSITY BY DEPLOYMENT SHAPE ──
        // The opening geometry this wave made variable. Every row carries the two numbers the
        // wave is judged on side by side, because a shape that shortens fights can lift
        // choices/turn without any soldier facing a richer decision.
        if (missions.Count > 0)
        {
            string ShapeName(int d) => d switch
            {
                Mission.DeployPincer => "PINCER",
                Mission.DeployCrossfire => "CROSSFIRE",
                Mission.DeployEnvelop => "ENVELOP",
                _ => "FRONTAL",
            };
            sb.AppendLine("\nDEPLOYMENT GEOMETRY (opening shape; choices/ARMED is the wave's real gate):");
            foreach (var g in missions.GroupBy(m => m.Deploy).OrderBy(g => g.Key))
            {
                double gTurns = g.Sum(m => (double)m.PlayerTurns);
                double gArmed = g.Sum(m => (double)m.ArmedSoldierTurns);
                double gCh = g.Sum(m => (double)m.MeaningfulChoiceSum);
                sb.AppendLine($"  {ShapeName(g.Key),-10}: {Pct(g.Count(m => m.Win), g.Count())}{Se(g.Count(m => m.Win), g.Count())}"
                    + $"  (n={g.Count()}, avg {g.Average(m => (double)m.Turns):0.0} turns)"
                    + $"  choices/turn {(gTurns > 0 ? gCh / gTurns : 0):0.00}"
                    + $"  choices/ARMED {(gArmed > 0 ? gCh / gArmed : 0):0.00}"
                    + $"  los-targets/ARMED {(gArmed > 0 ? g.Sum(m => (double)m.LosTargetSum) / gArmed : 0):0.00}");
            }
        }

        // ── FUL-1: ARENA FUNNEL (Mission.Build's three exits — the lines sum to 100%) ──
        // The reject line is the new signal: an authored roll the connectivity guard threw
        // away used to be indistinguishable from a lost roll, understating authored demand.
        // All modes (the build path is mode-agnostic); counted per Build while enabled.
        int funTot = _arenaFunnel[0] + _arenaFunnel[1] + _arenaFunnel[2];
        if (funTot > 0)
        {
            sb.AppendLine($"\nARENA FUNNEL (Mission.Build exits, n={funTot} builds, all modes):");
            sb.AppendLine($"  authored-applied    : {100.0 * _arenaFunnel[0] / funTot,5:0.0}%  (n={_arenaFunnel[0]})");
            sb.AppendLine($"  connectivity-reject : {100.0 * _arenaFunnel[1] / funTot,5:0.0}%  (n={_arenaFunnel[1]})");
            sb.AppendLine($"  procedural-roll     : {100.0 * _arenaFunnel[2] / funTot,5:0.0}%  (n={_arenaFunnel[2]})");
        }

        // ── FUL-9: DECK EXPOSURE (the deck's two run-level promises, on PLAYED runs) ──
        // Authored-arena variety within a run (the no-repeat deck) and Defend actually
        // reaching routes (the old rotation left it absent from whole batches). Campaign
        // runs only — endless holds one arena for the whole stand.
        var deckRuns = Runs.Where(r => r.Mode == "campaign" && r.Missions.Count > 0).ToList();
        if (deckRuns.Count > 0)
        {
            double Distinct(RunRec r) => r.Missions.Where(m => m.Layout >= 0).Select(m => m.Layout).Distinct().Count();
            double meanDistinct = deckRuns.Average(Distinct);
            int defendRuns = deckRuns.Count(r => r.Missions.Any(m => m.Objective == "Defend"));
            // full-depth = the run reached the boss column: early deaths truncate routes (2-3
            // fights), and an EVENT node on the route replaces a fight entirely — so the all-runs
            // mean under-reads deck variety. The full-depth line is the apples-to-apples read
            // (its own ceiling is fights/run x authored share, both printed for the arithmetic).
            var full = deckRuns.Where(r => r.Win).ToList();
            sb.AppendLine($"\nDECK EXPOSURE (campaign runs n={deckRuns.Count}):");
            sb.AppendLine($"  distinct authored arenas/run : {meanDistinct:0.00} mean (all runs)");
            if (full.Count > 0)
                sb.AppendLine($"  ... full-depth runs only     : {full.Average(Distinct):0.00} mean over {full.Average(r => (double)r.Missions.Count):0.0} fights/run (n={full.Count})");
            sb.AppendLine($"  Defend dealt on the route    : {Pct(defendRuns, deckRuns.Count)} of runs (n={defendRuns})");
        }

        // ── C2 THE OPPONENT DECLINES: ENEMY DECISION MIX ────────────────────────
        // The other side of the ACTION MIX below. Contested acts only (an all-downed board is
        // Ai.Plan's empty-plan early return and idles by design — W2/DESIGN §5.1); the
        // all-downed count is printed beside it so the denominator is never in doubt.
        if (_enemyActsContested > 0)
        {
            sb.AppendLine($"\nENEMY DECISION MIX (contested acts={_enemyActsContested}; all-downed acts not counted={_enemyActsAllDowned}):");
            // ThenBy(Key): ties would otherwise break by Dictionary INSERTION order, which is not
            // stable across runs — enough to make a paired inertness diff show a spurious non-empty
            // `mix` block and send someone hunting a gameplay change that never happened.
            foreach (var kv in _enemyDecisions.OrderByDescending(k => k.Value).ThenBy(k => k.Key))
                sb.AppendLine($"  {kv.Key,-12}{kv.Value,7}  {Pct(kv.Value, _enemyActsContested)}");
            int taken = EnemyShotsTaken, declined = EnemyShotsDeclined;
            sb.AppendLine($"\n  the shot on the table (acts where the chosen tile HAD a shot = {_enemyActsWithShot}):");
            sb.AppendLine("    hit% band     taken   share   E[dmg]/shot   declined   share   E[dmg]/shot");
            string[] bands = { "  0-19", " 20-39", " 40-59", " 60-79", "  80+ " };
            for (int b = 0; b < ShotBands; b++)
            {
                double te = _enemyShotTaken[b] == 0 ? 0 : _enemyShotTakenExp[b] / _enemyShotTaken[b];
                double de = _enemyShotDeclined[b] == 0 ? 0 : _enemyShotDeclinedExp[b] / _enemyShotDeclined[b];
                sb.AppendLine($"    {bands[b]}     {_enemyShotTaken[b],7}  {Pct(_enemyShotTaken[b], Math.Max(1, taken)),6}"
                            + $"        {te,6:0.00}    {_enemyShotDeclined[b],7}  {Pct(_enemyShotDeclined[b], Math.Max(1, declined)),6}        {de,6:0.00}");
            }
            sb.AppendLine($"    TOTAL       {taken,7}                        {declined,7}");
            sb.AppendLine($"  taken {Pct(taken, Math.Max(1, _enemyActsWithShot))} / DECLINED {Pct(declined, Math.Max(1, _enemyActsWithShot))}"
                        + $" / preempted by another verb {Pct(_enemyShotPreempted, Math.Max(1, _enemyActsWithShot))}");
            int lanes = _enemyDecisions.GetValueOrDefault("overwatch", 0) + _enemyDecisions.GetValueOrDefault("brace", 0);
            sb.AppendLine($"  enemy lanes held (overwatch+brace) {lanes}; reaction shots fired {_enemyReactions}"
                        + $"  -> {Pct(_enemyReactions, Math.Max(1, lanes))} of lanes paid off"
                        + "   [C2: DeclineWatchRatio is a bet on this number]");
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

        // ── FUL-7: the DOWN ledger — downs staged and how each resolved. save-rate =
        // 1 - (bled-out + finished)/downs (review F2: a body killed while down is a death, not
        // a save; a down still open at run end counts saved — the run decided first).
        // The corpsman-fielded count is the PATCH per-presence denominator.
        if (_downs > 0)
        {
            int saved = _downs - _downExpired - _downFinished;
            sb.AppendLine($"\nSOLDIER DOWNS (FUL-7 bleed-out): {_downs} downs -> revived {_downRevived} / recovered {_downRecovered} / bled out {_downExpired} / finished {_downFinished}  (save-rate {Pct(saved, _downs)})");
            sb.AppendLine($"  corpsman fielded in {_corpsmanMissions} missions");
        }
        else if (_corpsmanMissions > 0)
            sb.AppendLine($"\nSOLDIER DOWNS (FUL-7 bleed-out): 0  (corpsman fielded in {_corpsmanMissions} missions)");

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

        // Boon / doctrine pick frequency. FUL-1: + a PROCS column — effect fires counted at
        // the instrumented sites (SHK brace-full-dmg, TRR rout-start, FDR 2nd drag/vault,
        // PYR +2-turn burn, FST double-charge grant, RCL cone re-arm). picks>0 with procs=0
        // means the boon was HELD but its effect never reached play — the FUL-5 finding.
        // '-' = code not instrumented (frequency-only, as before).
        // Review fix: PYR counts the +2-turn-burn half ONLY; its second effect (squad Burning
        // immunity, the Unit.AddStatus chokepoint) is deliberately uninstrumented — it fires
        // per BLOCKED application and would swamp the column. The caption says so.
        var boons = new Dictionary<string, int>();
        foreach (var r in Runs) foreach (var b in r.BoonsPicked) Bump(boons, b);
        if (boons.Count > 0 || _boonProcs.Count > 0)
        {
            sb.AppendLine("\nBOON PICK FREQUENCY (PROCS = effect fires at instrumented sites; '-' = not instrumented;");
            sb.AppendLine("                      PYR counts the +2-turn-burn half only — the Burning-immunity half is uninstrumented):");
            sb.AppendLine("  code                 picks   PROCS");
            foreach (var k in boons.Keys.Concat(_boonProcs.Keys).Distinct()
                         .OrderByDescending(k => boons.GetValueOrDefault(k)).ThenBy(k => k))
            {
                string procs = ProcInstrumented.Contains(k) || _boonProcs.ContainsKey(k)
                    ? _boonProcs.GetValueOrDefault(k).ToString() : "-";
                sb.AppendLine($"  {k,-18}: {boons.GetValueOrDefault(k),5}   {procs,5}");
            }
        }

        // ── APEX W4: WIN-RATE BY PICK (value telemetry, not just frequency) ──────────
        // Run-level association: for each code, the completion rate of the CAMPAIGN runs that
        // held it (deduped per run). These were recorded all along — contracts were recorded
        // and never reported at ALL — but only frequencies ever reached the report, so a pick's
        // VALUE was invisible. Small-n rows are noisy; read them against the pick count.
        // FUL-1: when the batch SPANS heats (the {0,2,4,6,8} cycling default), rows are keyed
        // (code, heat) — "CODE@h<N>" — so a pick's win-rate is never a disguised heat mix (a
        // code over-drawn at heat 0 would otherwise read as strong). Pinned batches keep the
        // plain code key. n<30 rows carry the binomial ±SE.
        bool spansHeats = campRuns.Select(r => r.Heat).Distinct().Count() > 1;
        void WinRateTable(string title, Func<RunRec, IEnumerable<string>> picks)
        {
            var agg = new Dictionary<string, (int n, int w)>();
            foreach (var r in campRuns)
                foreach (var code in picks(r).Distinct())
                {
                    string key = spansHeats ? $"{code}@h{r.Heat}" : code;
                    agg.TryGetValue(key, out var t);
                    agg[key] = (t.n + 1, t.w + (r.Win ? 1 : 0));
                }
            if (agg.Count == 0) return;
            sb.AppendLine($"\n{title}:");
            foreach (var kv in agg.OrderByDescending(kv => kv.Value.n).ThenBy(kv => kv.Key))
                sb.AppendLine($"  {kv.Key,-18}: {Pct(kv.Value.w, kv.Value.n)}{Se(kv.Value.w, kv.Value.n)}  (in {kv.Value.n} runs)");
        }
        WinRateTable("RUN WIN-RATE BY BOON (campaign runs holding it)", r => r.BoonsPicked);
        WinRateTable("RUN WIN-RATE BY SPEC (campaign runs fielding it)", r => r.SpecsPicked);
        WinRateTable("RUN WIN-RATE BY CONTRACT (campaign runs under it)", r => r.ContractsPicked);
        // W2: perk + purchase VALUE tables (frequency alone hid whether a pick actually helps —
        // the perk offer and the shop slate both got randomized exposure for exactly this table).
        WinRateTable("RUN WIN-RATE BY PERK (campaign runs holding it)", r => r.PerksPicked);
        WinRateTable("RUN WIN-RATE BY PURCHASE (campaign runs buying it)", r => r.Purchases);
        // FUL-1: field-event arms ("id:arm") — the trade-off table FUL-10's new forks consume.
        WinRateTable("RUN WIN-RATE BY EVENT-CHOICE (campaign runs taking id:arm)", r => r.EventChoices);

        sb.AppendLine("═══════════════════════════════════════════════════════════════════════");
        return sb.ToString();
    }

    // ── JSON export ───────────────────────────────────────────────────────────
    // Writes the aggregate as a compact JSON document (same numbers as Report()) so a
    // balance batch can be diffed/plotted by external tooling. Mirrors SaveGame's
    // System.Text.Json pattern; failures are swallowed (telemetry must never crash a run).
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // IL2026 (trim analysis): BuildSummary returns a tree of ANONYMOUS types, which no source
    // generator can be pointed at, so this one call site stays reflection-based. It is safe under
    // `-p:PublishTrimmed=true` only because Sightline.csproj roots our own assembly
    // (TrimmerRootAssembly) and re-enables the JSON reflection fallback for trimmed publishes --
    // verified by publishing trimmed and diffing this file against the untrimmed build's (byte
    // identical). If either csproj setting is removed, this silently stops writing; the catch below
    // now prints the reason instead of swallowing it. Nothing in the shipped GAME reaches here --
    // WriteJson is only called from the SIGHTLINE_BALANCE headless flywheel.
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Anonymous-type telemetry; app assembly is rooted via TrimmerRootAssembly and JSON reflection is re-enabled for trimmed publishes. Dev-harness only (SIGHTLINE_BALANCE).")]
    public static void WriteJson(string path)
    {
        // W1 TRUE INSTRUMENT: a batch that recorded NOTHING must not leave a file behind that
        // looks like an answer. The pre-W1 behaviour was to serialise the empty aggregate —
        // runs=0, every rate 0.0, every table [] — over whatever was at `path`, so a chunk that
        // silently ran on no display destroyed the previous chunk's data and reported a full
        // set of zeroes. Refuse, name it, and leave the existing file's bytes AND mtime alone.
        if (Runs.Count == 0)
        {
            Console.WriteLine($"stats JSON export REFUSED ({path}): runs=0 — nothing was measured, existing file left untouched");
            return;
        }
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(BuildSummary(), JsonOpts));
        }
        catch (Exception e)
        {
            // Best-effort (telemetry must never throw out of a balance batch) but NOT silent: a
            // bare `catch {}` here meant a trimmed build wrote no file at all and said nothing,
            // which is how a broken publish config survives a "0 Errors" review.
            Console.WriteLine($"stats JSON export FAILED ({path}): {e.GetType().Name}: {e.Message}");
        }
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
        var evChoices = new Dictionary<string, int>();   // FUL-1: "id:arm" frequency
        foreach (var r in Runs) foreach (var e in r.EventChoices) Bump(evChoices, e);

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

        // APEX W4: run-level win-rate by pick code (campaign only, deduped per run).
        // FUL-1: keys become "CODE@h<N>" when the batch spans heats (mirrors the report
        // table — a pick's win-rate must not be a disguised heat mix), and every row
        // carries its binomial se (percentage points).
        bool spansHeats = campRuns.Select(r => r.Heat).Distinct().Count() > 1;
        Dictionary<string, object> WinRateBy(Func<RunRec, IEnumerable<string>> picks)
        {
            var agg = new Dictionary<string, (int n, int w)>();
            foreach (var r in campRuns)
                foreach (var code in picks(r).Distinct())
                {
                    string key = spansHeats ? $"{code}@h{r.Heat}" : code;
                    agg.TryGetValue(key, out var t);
                    agg[key] = (t.n + 1, t.w + (r.Win ? 1 : 0));
                }
            return agg.OrderByDescending(kv => kv.Value.n).ToDictionary(
                kv => kv.Key,
                kv => (object)new { runs = kv.Value.n, winRate = Math.Round(100.0 * kv.Value.w / kv.Value.n, 1), se = SeVal(kv.Value.w, kv.Value.n) });
        }

        // decision-richness / swing aggregates (all modes — texture data is mode-agnostic)
        var tMissions = allMissions.Where(m => m.PlayerTurns > 0).ToList();
        double choicesPerTurn = tMissions.Count == 0 ? 0.0
            : Math.Round(tMissions.Sum(m => (double)m.MeaningfulChoiceSum) / Math.Max(1, tMissions.Sum(m => m.PlayerTurns)), 3);
        double swingsPerMatch = tMissions.Count == 0 ? 0.0 : Math.Round(tMissions.Average(m => (double)m.LeadSwings), 2);
        double avgMaxSwing = tMissions.Count == 0 ? 0.0 : Math.Round(tMissions.Average(m => (double)m.MaxSwing), 2);

        return new
        {
            // TRUE BAND: the DECISION-DENSITY INSTRUMENT this batch was measured on. "mult-v1" is
            // the multiplicative near-best window every number archived before 2026-08-29 used;
            // "add-v2" is the additive band shipped by wave TRUE BAND. The two are NOT comparable
            // on any `choices*` field, so a comparison script must refuse a diff across them —
            // which is why this is emitted as data instead of trusted to a doc banner.
            instrument = Game.InstrumentTag,
            runs = Runs.Count,
            // APEX W4: run counts by mode, so a consumer can see at a glance what the batch mixed.
            runsByMode = Runs.GroupBy(r => r.Mode).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
            missions = allMissions.Count,
            // campaign-only (endless "missions cleared" are waves — a different unit entirely)
            runWinRate = campRuns.Count == 0 ? 0.0 : Math.Round(100.0 * campRuns.Count(r => r.Win) / campRuns.Count, 1),
            // W1 TRUE INSTRUMENT: the same rate with the AUTOPILOT'S OWN FAILURES taken out of the
            // denominator. Game.Autopilot.AutoStallCheck force-loses any match still going at the
            // turn cap with LossCause "STALEMATE" — that is the bot failing to find a finishing
            // line, not the game beating a player, and it has been folded into every published
            // ladder rung as an ordinary campaign loss. Both numbers are reported; if they differ
            // by more than a point or two the ladder is partly a measurement of the harness.
            runWinRateExStalemate = ExStalemateRate(campRuns),
            instrumentHealth = InstrumentHealth(campRuns),
            // THE HEAT PIN: how much of this batch was played at a heat other than the one it
            // claims. `pinned` says whether EventCatalog.HeatPinned held for the batch;
            // `heatRaisingPicks` counts the arms the bot took (they are still taken under the pin —
            // the pin nulls the OUTCOME, not the choice, so the event economy is unchanged);
            // `campaignsRaised` is runs whose HeatEnd > Heat and `missionsAbovePin` the missions
            // those runs played off-rung. Under the pin the last two MUST read 0 — l5/cluster.py
            // asserts it per chunk.
            heatLeak = new
            {
                pinned = EventCatalog.HeatPinned,
                heatRaisingPicks = campRuns.Sum(HeatRaisingPicks),
                campaignsRaised = campRuns.Count(r => r.HeatEnd > r.Heat),
                missionsAbovePin = campRuns.Sum(r => r.Missions.Count(m => m.Heat != r.Heat)),
                maxHeatEnd = campRuns.Count == 0 ? 0 : campRuns.Max(r => r.HeatEnd)
            },
            // THE HEAT PIN: one row per RunRec, every mode. pairedPolicy.slots keeps a (slot, heat)
            // only when it has exactly one greedy AND one sloppy leg, so a single-policy batch
            // (SIGHTLINE_BALANCE_SLOPPY=1, or a future camping policy) left NO per-slot rows and
            // could not be CRN-paired against anything. These rows can: l5/rows.py pairs two
            // chunks by (slot, policy, heat) from this array and reproduces the pairedPolicy table
            // from a greedy+sloppy chunk exactly.
            campaigns = Runs.Select(r => new
            {
                slot = r.Slot, policy = r.Policy, mode = r.Mode, heat = r.Heat, heatEnd = r.HeatEnd,
                win = r.Win, missionsCleared = r.MissionsCleared, lossCause = r.LossCause,
                runTurns = r.RunTurns, endMission = LastMission(r)?.Mission ?? 0,
                endObjective = LastMission(r)?.Objective ?? "", heatRaisingPicks = HeatRaisingPicks(r)
            }).ToList(),
            // W1: the machine the batch actually ran on (see ReadLoadAvg). Excluded from any
            // before/after byte-diff by construction — it is the one block that MUST vary.
            harness = new
            {
                nproc = Environment.ProcessorCount,
                startedUtc = _batchStartUtc.ToString("o"),
                loadAtStart = _loadAtStart,
                loadAtEnd = ReadLoadAvg(),
                elapsedToDataReadySec = Math.Round((DateTime.UtcNow - _batchStartUtc).TotalSeconds, 1)
            },
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
                avgMaxSwing = avgMaxSwing,
                // X1 shot-gate decomposition (see MissionRec) — lets a consumer tell a lever
                // that shrinks the roster apart from one that flattens the decision itself.
                actingSoldiersPerTurn = tMissions.Count == 0 ? 0.0 : Math.Round(
                    tMissions.Sum(m => (double)m.ActingSoldierTurns) / Math.Max(1, tMissions.Sum(m => m.PlayerTurns)), 3),
                armedSoldiersPerTurn = tMissions.Count == 0 ? 0.0 : Math.Round(
                    tMissions.Sum(m => (double)m.ArmedSoldierTurns) / Math.Max(1, tMissions.Sum(m => m.PlayerTurns)), 3),
                turnsWithAShotPct = tMissions.Count == 0 ? 0.0 : Math.Round(
                    100.0 * tMissions.Sum(m => (double)m.ArmedTurns) / Math.Max(1, tMissions.Sum(m => m.PlayerTurns)), 1),
                choicesPerArmedSoldierTurn = tMissions.Count == 0 || tMissions.Sum(m => m.ArmedSoldierTurns) == 0 ? 0.0 : Math.Round(
                    tMissions.Sum(m => (double)m.MeaningfulChoiceSum) / tMissions.Sum(m => (double)m.ArmedSoldierTurns), 3),
                // W4 — the two axes CountMeaningfulChoices sums, split (see MissionRec).
                losTargetsPerArmedSoldierTurn = tMissions.Count == 0 || tMissions.Sum(m => m.ArmedSoldierTurns) == 0 ? 0.0 : Math.Round(
                    tMissions.Sum(m => (double)m.LosTargetSum) / tMissions.Sum(m => (double)m.ArmedSoldierTurns), 3),
                targetChoicesPerArmedSoldierTurn = tMissions.Count == 0 || tMissions.Sum(m => m.ArmedSoldierTurns) == 0 ? 0.0 : Math.Round(
                    tMissions.Sum(m => (double)m.TargetChoiceSum) / tMissions.Sum(m => (double)m.ArmedSoldierTurns), 3),
                positionChoicesPerArmedSoldierTurn = tMissions.Count == 0 || tMissions.Sum(m => m.ArmedSoldierTurns) == 0 ? 0.0 : Math.Round(
                    tMissions.Sum(m => (double)m.PosChoiceSum) / tMissions.Sum(m => (double)m.ArmedSoldierTurns), 3)
            },
            // Run-completion grouped by heat (the ladder's true shape — distinct from the
            // survivorship-skewed per-mission byHeat below). Campaign runs only.
            // FUL-1: win-rate rows carry their binomial se (percentage points) so a consumer
            // never has to reconstruct the sample-size caveat from n by hand.
            byHeatRun = campRuns.GroupBy(r => r.Heat).OrderBy(g => g.Key).Select(g => new
            {
                heat = g.Key, runs = g.Count(),
                runWinRate = Math.Round(100.0 * g.Count(r => r.Win) / g.Count(), 1),
                se = SeVal(g.Count(r => r.Win), g.Count()),
                avgMissionsCleared = Math.Round(g.Average(r => (double)r.MissionsCleared), 2)
            }).ToList(),
            // FUL-13: intel cash-flow by heat (see RecordIntel — the heat-flood read).
            intelByHeat = campRuns.GroupBy(r => r.Heat).OrderBy(g => g.Key).Select(g => new
            {
                heat = g.Key, runs = g.Count(),
                earned = Math.Round(g.Average(r => (double)r.IntelEarned), 1),
                heatBonus = Math.Round(g.Average(r => (double)r.IntelHeatBonus), 1),
                spent = Math.Round(g.Average(r => (double)r.IntelSpent), 1),
                unspent = Math.Round(g.Average(r => (double)(r.IntelEarned - r.IntelSpent)), 1)
            }).ToList(),
            byHeat = missions.GroupBy(m => m.Heat).OrderBy(g => g.Key).Select(g => new
            {
                heat = g.Key, n = g.Count(), winRate = WinRate(g), se = SeVal(g.Count(m => m.Win), g.Count()), avgTurns = Math.Round(g.Average(m => (double)m.Turns), 1)
            }).ToList(),
            byObjective = missions.GroupBy(m => m.Objective).OrderBy(g => g.Key).Select(g => new
            {
                objective = g.Key, n = g.Count(), winRate = WinRate(g), se = SeVal(g.Count(m => m.Win), g.Count()), avgTurns = Math.Round(g.Average(m => (double)m.Turns), 1)
            }).ToList(),
            // W1 TRUE INSTRUMENT: byObjective, stratified by the condition the squad arrived in.
            // The flat row above is a survivorship trap and X2 was bitten by it in public — Escort
            // read 8.03t only because the runs sick enough to make Escort slow had already died
            // before mission 4, and its "true" figure turned out to be 12.81t. Each row here holds
            // squad size AND HP band still, so a cross-rung comparison compares like with like.
            // Rows are deliberately fine-grained and sparse; pool them, do not read a cell of n=2.
            byObjectiveByBucket = missions
                .GroupBy(m => new { m.Objective, m.SquadStart, Hp = HpBand(m.SquadHpPct) })
                .OrderBy(g => g.Key.Objective).ThenBy(g => g.Key.SquadStart).ThenBy(g => g.Key.Hp)
                .Select(g => new
                {
                    objective = g.Key.Objective, squad = g.Key.SquadStart, hp = g.Key.Hp,
                    n = g.Count(), winRate = WinRate(g), se = SeVal(g.Count(m => m.Win), g.Count()),
                    avgTurns = Math.Round(g.Average(m => (double)m.Turns), 1)
                }).ToList(),
            // W1: the campaign-map NODE KIND a mission came from (Combat/Elite/Supply/Boss/Start,
            // or the mode name outside the campaign). The routing policy decides this mix — see
            // SIGHTLINE_ROUTE — and nothing in the artifact recorded it before.
            byNodeKind = missions.GroupBy(m => string.IsNullOrEmpty(m.NodeKind) ? "?" : m.NodeKind)
                .OrderBy(g => g.Key).Select(g => new
            {
                nodeKind = g.Key, n = g.Count(), winRate = WinRate(g), se = SeVal(g.Count(m => m.Win), g.Count()),
                avgTurns = Math.Round(g.Average(m => (double)m.Turns), 1)
            }).ToList(),
            // C4 "EIGHT BIOMES ARE PAINT": the BIOME a mission was fought in, with the count of
            // mechanical-ground tiles it carried. Three of the eight now change the fight (VERDANT
            // undergrowth / TUNDRA slick ice / MAGMA thermal vents) and five are still paint, so a
            // pooled rung mixes two populations that are no longer the same game. This is the row
            // that says WHICH. `groundTiles` is 0 on the paint biomes and with SIGHTLINE_BIOMEMECH=0,
            // which also makes it the cheapest possible check that an A/B arm really was what it
            // claimed to be.
            byBiome = missions.GroupBy(m => string.IsNullOrEmpty(m.Biome) ? "?" : m.Biome)
                .OrderBy(g => g.Key).Select(g => new
            {
                biome = g.Key, n = g.Count(), winRate = WinRate(g), se = SeVal(g.Count(m => m.Win), g.Count()),
                avgTurns = Math.Round(g.Average(m => (double)m.Turns), 1),
                avgGroundTiles = Math.Round(g.Average(m => (double)m.GroundTiles), 1)
            }).ToList(),
            // W8 THE HALF WALL: objective x campaign NODE KIND. byObjective alone pools a capstone
            // with a mid-run node — for Decapitate it always does (one Boss node, always the map's
            // last, always Decapitate), which hid a 23.7-point split until L1 stumbled over it.
            // Sparse by construction; pool cells, never read an n=2 one.
            byObjectiveByNodeKind = missions
                .GroupBy(m => new { m.Objective, NodeKind = string.IsNullOrEmpty(m.NodeKind) ? "?" : m.NodeKind })
                .OrderBy(g => g.Key.Objective).ThenBy(g => g.Key.NodeKind)
                .Select(g => new
                {
                    objective = g.Key.Objective, nodeKind = g.Key.NodeKind,
                    n = g.Count(), winRate = WinRate(g), se = SeVal(g.Count(m => m.Win), g.Count()),
                    avgTurns = Math.Round(g.Average(m => (double)m.Turns), 1)
                }).ToList(),
            // W8: objective x MISSION DEPTH. Node kind is not the only mix an objective's flat row
            // hides: Run.DeckObjective deals a fight objective from a hash of the map COLUMN, and a
            // column is a mission number, so every objective row also carries a depth mix. W8 found
            // the two mid-run KILL objectives (Eliminate, Decapitate) far below the extract/hold
            // ones on the same node kinds, which is only interpretable with these cells in hand.
            byObjectiveByMission = missions
                .GroupBy(m => new { m.Objective, m.Mission })
                .OrderBy(g => g.Key.Objective).ThenBy(g => g.Key.Mission)
                .Select(g => new
                {
                    objective = g.Key.Objective, mission = g.Key.Mission,
                    n = g.Count(), winRate = WinRate(g), se = SeVal(g.Count(m => m.Win), g.Count()),
                    avgTurns = Math.Round(g.Average(m => (double)m.Turns), 1)
                }).ToList(),
            // C3 THE TWO GAMES: the ENCOUNTER-COMPLETION decomposition, as JSON. Same population and
            // same columns as the console block of that name — mid-run node kinds only (Combat +
            // Elite), because Start is 960 mission-1 Eliminates and Boss is the de-stacked finale,
            // and pooling either puts the two objective CLASSES on different populations. `clearPct`
            // is killed / (start + reinforcements): the share of the encounter actually fought, and
            // the only column that can separate "this class wins the fight" from "this class never
            // has the fight". Rows: one per objective, then the two class aggregates.
            encounterMidrun = missions.Where(m => m.NodeKind == "Combat" || m.NodeKind == "Elite")
                .GroupBy(m => m.Objective).OrderBy(g => g.Key)
                .Select(g => EncounterRow(g.Key, g.ToList()))
                .Concat(new[]
                {
                    EncounterRow("KILL", missions.Where(m => (m.NodeKind == "Combat" || m.NodeKind == "Elite") && Run.IsKillObjective(m.Objective)).ToList()),
                    EncounterRow("NONKILL", missions.Where(m => (m.NodeKind == "Combat" || m.NodeKind == "Elite") && !Run.IsKillObjective(m.Objective)).ToList())
                }).ToList(),
            // W8: the DECAPITATE HVT split by DesignateHvt's ELITE exemption. buffed=false is the
            // named boss (finale or m3/m5 mid-boss) that keeps its own statline; buffed=true is a
            // rank-and-file body carrying + (base + perMission*depth) HP and + aim on top of the
            // +3 Mission.HostileToughness already gives it. The dials that set those three
            // magnitudes are echoed so a chunk's JSON records the tree it was measured on.
            hvt = new
            {
                hpBonusBase = Combat.HvtHpBonusBase,
                hpBonusPerMission = Combat.HvtHpBonusPerMission,
                aimBonus = Combat.HvtAimBonus,
                rows = missions.Where(m => m.HvtKind >= 0).GroupBy(m => m.HvtKind == 1)
                    .OrderBy(g => g.Key).Select(g => new
                    {
                        buffed = g.Key, n = g.Count(), winRate = WinRate(g),
                        se = SeVal(g.Count(m => m.Win), g.Count()),
                        avgTurns = Math.Round(g.Average(m => (double)m.Turns), 1),
                        avgHvtMaxHp = Math.Round(g.Average(m => (double)m.HvtMaxHp), 1)
                    }).ToList(),
                // ...and the same split held at each depth. The buff scales UP with mission
                // (+6+mission), so this is where a "the target out-bulks the campaign's own final
                // boss two missions early" claim has to be checked rather than asserted.
                byMission = missions.Where(m => m.HvtKind >= 0)
                    .GroupBy(m => new { m.Mission, Buffed = m.HvtKind == 1 })
                    .OrderBy(g => g.Key.Mission).ThenBy(g => g.Key.Buffed)
                    .Select(g => new
                    {
                        mission = g.Key.Mission, buffed = g.Key.Buffed, n = g.Count(),
                        winRate = WinRate(g), se = SeVal(g.Count(m => m.Win), g.Count()),
                        avgHvtMaxHp = Math.Round(g.Average(m => (double)m.HvtMaxHp), 1)
                    }).ToList()
            },
            // W1: the SHOT-GAP distribution (see RecordShotGap). deciles[i] counts armed
            // soldier-turns whose best-vs-runner-up gap fell in [i/10, (i+1)/10); deciles[9]
            // is "the shot picks itself" (a lone legal target, or a runner-up worth <10% of it).
            shotGap = new
            {
                armedSoldierTurns = _shotGapArmedTurns,
                meanGap = _shotGapArmedTurns == 0 ? 0.0 : Math.Round(_shotGapSum / _shotGapArmedTurns, 3),
                soleOrDominantPct = _shotGapArmedTurns == 0 ? 0.0
                    : Math.Round(100.0 * _shotGapDeciles[9] / _shotGapArmedTurns, 1),
                deciles = (int[])_shotGapDeciles.Clone()
            },
            // C2: the ENEMY DECISION MIX. `mix` is the branch that fired on each CONTESTED
            // act-opportunity; `shotTaken`/`shotDeclined` are the five hit-chance bands
            // (0-19/20-39/40-59/60-79/80+) of the shot the planner had on the table, and the
            // *Exp arrays the summed graze-aware expected damage of those same shots.
            enemyDecisions = new
            {
                contestedActs = _enemyActsContested,
                allDownedActs = _enemyActsAllDowned,
                actsWithShot  = _enemyActsWithShot,
                declinedPct = _enemyActsWithShot == 0 ? 0.0
                    : Math.Round(100.0 * EnemyShotsDeclined / _enemyActsWithShot, 2),
                preempted = _enemyShotPreempted,
                lanesHeld = _enemyDecisions.GetValueOrDefault("overwatch", 0) + _enemyDecisions.GetValueOrDefault("brace", 0),
                reactionShots = _enemyReactions,
                mix = _enemyDecisions.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
                        .Select(kv => new { verb = kv.Key, n = kv.Value }).ToList(),
                shotTaken = (int[])_enemyShotTaken.Clone(),
                shotDeclined = (int[])_enemyShotDeclined.Clone(),
                shotTakenExp = _enemyShotTakenExp.Select(v => Math.Round(v, 2)).ToArray(),
                shotDeclinedExp = _enemyShotDeclinedExp.Select(v => Math.Round(v, 2)).ToArray()
            },
            byMission = missions.GroupBy(m => m.Mission).OrderBy(g => g.Key).Select(g => new
            {
                mission = g.Key, n = g.Count(), winRate = WinRate(g), se = SeVal(g.Count(m => m.Win), g.Count())
            }).ToList(),
            // W2 arena telemetry: authored layout index (-1 rows are folded into proceduralRate).
            // FUL-1: + per-mission stratification (an arena's aggregate mixes difficulty
            // rungs — the cells expose the mix; pre-FUL-9 the hint even coupled arena to mission).
            // W4 — the same split in machine-readable form.
            byDeploy = missions.GroupBy(m => m.Deploy).OrderBy(g => g.Key).Select(g => new
            {
                deploy = g.Key, n = g.Count(),
                winRate = Math.Round(100.0 * g.Count(m => m.Win) / g.Count(), 1),
                avgTurns = Math.Round(g.Average(m => (double)m.Turns), 2),
                choicesPerTurn = g.Sum(m => m.PlayerTurns) == 0 ? 0.0 : Math.Round(
                    g.Sum(m => (double)m.MeaningfulChoiceSum) / g.Sum(m => (double)m.PlayerTurns), 3),
                choicesPerArmed = g.Sum(m => m.ArmedSoldierTurns) == 0 ? 0.0 : Math.Round(
                    g.Sum(m => (double)m.MeaningfulChoiceSum) / g.Sum(m => (double)m.ArmedSoldierTurns), 3),
                losTargetsPerArmed = g.Sum(m => m.ArmedSoldierTurns) == 0 ? 0.0 : Math.Round(
                    g.Sum(m => (double)m.LosTargetSum) / g.Sum(m => (double)m.ArmedSoldierTurns), 3)
            }).ToList(),
            byArena = missions.Where(m => m.Layout >= 0).GroupBy(m => m.Layout).OrderBy(g => g.Key).Select(g => new
            {
                arena = g.Key, n = g.Count(), winRate = WinRate(g), se = SeVal(g.Count(m => m.Win), g.Count()), avgTurns = Math.Round(g.Average(m => (double)m.Turns), 1),
                byMission = g.GroupBy(m => m.Mission).OrderBy(x => x.Key)
                    .Select(x => new { mission = x.Key, n = x.Count(), winRate = WinRate(x) }).ToList()
            }).ToList(),
            proceduralFallback = new
            {
                n = missions.Count(m => m.Layout < 0),
                rate = missions.Count == 0 ? 0.0 : Math.Round(100.0 * missions.Count(m => m.Layout < 0) / missions.Count, 1),
                winRate = WinRate(missions.Where(m => m.Layout < 0))
            },
            // FUL-1 ARENA FUNNEL: Mission.Build's three exits (authored-applied / connectivity-
            // reject / procedural-roll) — counts sum to `builds`. All modes; see RecordArenaFunnel.
            arenaFunnel = new
            {
                builds = _arenaFunnel[0] + _arenaFunnel[1] + _arenaFunnel[2],
                authoredApplied = _arenaFunnel[0],
                connectivityReject = _arenaFunnel[1],
                proceduralRoll = _arenaFunnel[2]
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
            // FUL-1: effect fires at the instrumented boon sites (0 for a picked code = the
            // boon never reached play; codes absent from ProcInstrumented are not counted).
            // Review fix: PYR counts the +2-turn-burn half only — the Burning-immunity half
            // (Unit.AddStatus) is uninstrumented (per-blocked-application, would swamp it).
            boonProcs = ProcInstrumented.OrderBy(k => k).ToDictionary(k => k, k => _boonProcs.GetValueOrDefault(k)),
            // FUL-1: field-event arm frequency ("id:arm"), all modes.
            eventChoicePicks = evChoices.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value),
            // APEX W4: pick VALUE, not just frequency — run win-rate by held boon/spec/contract
            // (campaign runs, deduped per run; contracts were recorded but never reported at all).
            winRateByBoon = WinRateBy(r => r.BoonsPicked),
            winRateBySpec = WinRateBy(r => r.SpecsPicked),
            winRateByContract = WinRateBy(r => r.ContractsPicked),
            // W2: pick VALUE for perks + shop purchases too (both got randomized exposure)
            winRateByPerk = WinRateBy(r => r.PerksPicked),
            winRateByPurchase = WinRateBy(r => r.Purchases),
            // FUL-1: event-arm VALUE ("id:arm") — the table FUL-10's new forks consume.
            winRateByEventChoice = WinRateBy(r => r.EventChoices),
        };
    }

    // ── W1 TRUE INSTRUMENT helpers ───────────────────────────────────────────────────────
    /// The autopilot's own turn-cap force-loss (Game.Autopilot.AutoStallCheck -> LoseRun("STALEMATE")).
    /// It is a HARNESS failure wearing a campaign loss's clothes.
    public const string StalemateCause = "STALEMATE";
    /// THE HEAT PIN: the guard's two arms, NAMED. Game.Autopilot.AutoStallCheck fires on EITHER
    /// `_turnCount > AutoMaxTurns` (one mission dragging — the bot cannot find a finishing line)
    /// OR `RunTurns > AutoMaxRunTurns` (a whole campaign that is long but not stuck — W9's
    /// backstop), and both used to be logged as the one word "STALEMATE". A 2.1% harness-loss
    /// floor nobody owned could not be split into "the bot's Escort finishing line" and "the
    /// run-budget arm censoring long campaigns" until the loss cause said which. Every consumer
    /// matches on the PREFIX (IsStalemate), so a reader of an older archive still works.
    public const string StalemateMission = "STALEMATE-MISSION";
    public const string StalemateRun = "STALEMATE-RUN";
    public static bool IsStalemate(string cause)
        => cause != null && cause.StartsWith(StalemateCause, StringComparison.Ordinal);

    /// Run win-rate with STALEMATE runs removed from the DENOMINATOR entirely (they are neither
    /// a win nor evidence of a loss). Returns -1.0 when nothing is left to divide by, so a
    /// consumer can tell "no data" from "0%".
    static double ExStalemateRate(List<RunRec> campRuns)
    {
        var clean = campRuns.Where(r => !IsStalemate(r.LossCause)).ToList();
        return clean.Count == 0 ? -1.0 : Math.Round(100.0 * clean.Count(r => r.Win) / clean.Count, 1);
    }

    /// How much of this batch is the harness rather than the game. Every count here is a run
    /// the flywheel scored as a campaign LOSS without a player ever being beaten.
    static object InstrumentHealth(List<RunRec> campRuns)
    {
        int stale = campRuns.Count(r => IsStalemate(r.LossCause));
        int staleMission = campRuns.Count(r => r.LossCause == StalemateMission);
        int staleRun = campRuns.Count(r => r.LossCause == StalemateRun);
        int frameCap = campRuns.Count(r => r.LossCause == "frame-cap");
        int aborted = campRuns.Count(r => r.LossCause == "aborted");
        return new
        {
            campaignRuns = campRuns.Count,
            stalemateLosses = stale,
            stalematePct = campRuns.Count == 0 ? 0.0 : Math.Round(100.0 * stale / campRuns.Count, 1),
            // THE HEAT PIN: the split (see StalemateMission / StalemateRun). An older archive
            // reads 0 / 0 here with stalemateLosses > 0 — that is "unsplit", not "neither".
            stalemateMissionLosses = staleMission,
            stalemateRunLosses = staleRun,
            // ...and WHERE each one stalled, so the README can say what the split showed.
            stalemates = campRuns.Where(r => IsStalemate(r.LossCause)).Select(r => new
            {
                slot = r.Slot, policy = r.Policy, heat = r.Heat, arm = r.LossCause,
                mission = LastMission(r)?.Mission ?? 0, objective = LastMission(r)?.Objective ?? "",
                missionTurns = LastMission(r)?.Turns ?? 0, runTurns = r.RunTurns
            }).ToList(),
            frameCapLosses = frameCap,
            abortedRuns = aborted,
            // The one number to read: everything the batch counted as a loss that the GAME did
            // not actually cause. Anything above a couple of points makes the rung suspect.
            harnessLossPct = campRuns.Count == 0 ? 0.0
                : Math.Round(100.0 * (stale + frameCap + aborted) / campRuns.Count, 1)
        };
    }

    /// Coarse squad-condition band for byObjectiveByBucket. Four bands, chosen so a full-health
    /// squad and a squad one hit from a death spiral never share a cell.
    static string HpBand(int pct) => pct >= 90 ? "hp90+" : pct >= 70 ? "hp70-89" : pct >= 50 ? "hp50-69" : "hp<50";

    /// C3 THE TWO GAMES: one ENCOUNTER-COMPLETION row for the JSON artifact. `label` is either an
    /// objective name or one of the two class aggregates ("KILL" / "NONKILL"). `clearPct` pools the
    /// numerator and denominator across the rows rather than averaging per-mission ratios — a
    /// mission that fields 3 hostiles and one that fields 11 must not carry equal weight in a
    /// statement about how much of the force gets beaten. An empty row is emitted with n=0 rather
    /// than dropped, so a lever that empties a cell is visible instead of silently absent.
    static object EncounterRow(string label, List<MissionRec> rows)
    {
        int n = rows.Count;
        int tot = rows.Sum(m => m.EnemiesStart + m.EnemiesAdded);
        var wins = rows.Where(m => m.Win).ToList();
        int winTot = wins.Sum(m => m.EnemiesStart + m.EnemiesAdded);
        return new
        {
            row = label,
            n,
            winRate = n == 0 ? 0.0 : Math.Round(100.0 * rows.Count(m => m.Win) / n, 1),
            se = n == 0 ? 0.0 : SeVal(rows.Count(m => m.Win), n),
            avgTurns = n == 0 ? 0.0 : Math.Round(rows.Average(m => (double)m.Turns), 2),
            enemiesStart = n == 0 ? 0.0 : Math.Round(rows.Average(m => (double)m.EnemiesStart), 2),
            enemiesAdded = n == 0 ? 0.0 : Math.Round(rows.Average(m => (double)m.EnemiesAdded), 2),
            enemiesKilled = n == 0 ? 0.0 : Math.Round(rows.Average(m => (double)m.EnemiesKilled), 2),
            clearPct = tot == 0 ? 0.0 : Math.Round(100.0 * rows.Sum(m => m.EnemiesKilled) / tot, 1),
            reinforcedPct = n == 0 ? 0.0 : Math.Round(100.0 * rows.Count(m => m.EnemiesAdded > 0) / n, 1),
            avgPressure = n == 0 ? 0.0 : Math.Round(rows.Average(m => (double)m.MaxPressure), 2),
            squadLoss = n == 0 ? 0.0 : Math.Round(rows.Average(m => (double)(m.SquadStart - m.SquadSurvived)), 2),
            dmgTaken = n == 0 ? 0.0 : Math.Round(rows.Average(m => (double)m.DamageTaken), 1),
            // ...and the same three columns restricted to WON missions. Without this restriction a
            // low clearPct has two readings that point in opposite directions — "the squad walked
            // past the force and won" and "the squad died before it could kill anything" — and the
            // first is the finding while the second is its refutation. On a WON kill objective
            // clearPct is 100 by construction (Eliminate) or gated on one body (Decapitate); on a
            // WON non-kill objective it is the share of the fight the squad chose to take.
            winN = wins.Count,
            winClearPct = winTot == 0 ? 0.0 : Math.Round(100.0 * wins.Sum(m => m.EnemiesKilled) / winTot, 1),
            // RAW SUMS, not just the ratios above. A chunk holds 20 campaigns and a round holds 48
            // chunks; pooling a round by AVERAGING 48 per-chunk percentages weights a chunk with 3
            // mid-run Hacks the same as one with 30. Every rate in this row is re-derivable from
            // these four integers, so the pooled figure is exact rather than approximately right.
            killedSum = rows.Sum(m => m.EnemiesKilled), forceSum = tot,
            winKilledSum = wins.Sum(m => m.EnemiesKilled), winForceSum = winTot,
            winSquadLoss = wins.Count == 0 ? 0.0 : Math.Round(wins.Average(m => (double)(m.SquadStart - m.SquadSurvived)), 2),
            winTurns = wins.Count == 0 ? 0.0 : Math.Round(wins.Average(m => (double)m.Turns), 2)
        };
    }

    // W2: the paired-outcome object for the JSON artifact (mirrors the PAIRED report line).
    // FUL-1: + the raw per-slot records and the all-pairs missions-cleared margin — the
    // continuous paired stat uses EVERY slot (concordant pairs included), so its CI is
    // roughly half the discordant-only binary gap's at the same n.
    static object BuildPaired(List<RunRec> campRuns)
    {
        var (pairs, concordant, dPlus, dMinus) = PairedOutcomes(campRuns);
        var slotPairs = PairList(campRuns);
        var (mn, mMean, mSe) = PairedMargin(slotPairs);
        return new
        {
            pairs, concordant,
            greedyOnlyWon = dPlus, sloppyOnlyWon = dMinus,
            pairedGap = pairs == 0 ? 0.0 : Math.Round(100.0 * (dPlus - dMinus) / pairs, 1),
            slots = slotPairs.Select(p => new
            {
                slot = p.slot, heat = p.heat,
                greedyWin = p.g.Win, greedyMissions = p.g.MissionsCleared,
                sloppyWin = p.s.Win, sloppyMissions = p.s.MissionsCleared,
                marginMissions = p.g.MissionsCleared - p.s.MissionsCleared
            }).ToList(),
            pairedMarginMissions = new { n = mn, mean = mMean, se = mSe }
        };
    }
}
