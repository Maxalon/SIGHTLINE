using System;
using System.Collections.Generic;

namespace Sightline;

/// A rank-up perk choice presented in the barracks: pick A or B.
public class PerkOffer
{
    public Unit Unit;
    public Perk A, B;
}

public enum RewardKind { None, Heal, BonusPerk }

/// Run-scoped BOONS (Hades boons / StS relics): a pick-1-of-3 modifier offered each barracks
/// that warps THIS run only (discarded at run end — the inverse of persistent perks). They are
/// combinatorial and lateral (not a power ladder), so every run plays differently. APPEND-ONLY
/// (the ordinal is the save key). Read in Combat.ComputeOdds (the aim/crit/armor ones, via a
/// static Combat.RunBoons set each mission) and in Game (the on-kill / concealment / deploy ones).
public enum Boon
{
    Marksmen,      // +aim at long range, squad-wide
    Fervor,        // overwatch reactions crit
    Executioners,  // +crit vs sub-half-HP targets, squad-wide
    Fortified,     // +1 effective armor, squad-wide
    Grenadier,     // a kill refreshes the killer's grenade
    Scavenger,     // a kill refills +2 ammo to the killer
    Adrenaline,    // a kill grants the killer +1 action this turn (cap 1/turn)
    Venom,         // a player hit applies Bleed to the target
    Ghost,         // moving near a foe does not break concealment
    RapidDeploy,   // +1 deploy slot this run
}

public static class BoonDef
{
    public static readonly Boon[] All =
    {
        Boon.Marksmen, Boon.Fervor, Boon.Executioners, Boon.Fortified, Boon.Grenadier,
        Boon.Scavenger, Boon.Adrenaline, Boon.Venom, Boon.Ghost, Boon.RapidDeploy,
    };

    public static string Name(Boon b) => b switch
    {
        Boon.Marksmen => "MARKSMEN", Boon.Fervor => "FERVOR", Boon.Executioners => "EXECUTIONERS",
        Boon.Fortified => "FORTIFIED", Boon.Grenadier => "GRENADIER", Boon.Scavenger => "SCAVENGER",
        Boon.Adrenaline => "ADRENALINE", Boon.Venom => "VENOM", Boon.Ghost => "GHOST",
        Boon.RapidDeploy => "RAPID DEPLOY", _ => "BOON",
    };

    public static string Desc(Boon b) => b switch
    {
        Boon.Marksmen => "Squad +12 aim at long range",
        Boon.Fervor => "Overwatch reaction shots crit",
        Boon.Executioners => "Squad +20 crit vs targets below half HP",
        Boon.Fortified => "Whole squad gains +1 armor (-1 damage/hit)",
        Boon.Grenadier => "A kill refreshes the killer's grenade",
        Boon.Scavenger => "A kill refills +2 ammo to the killer",
        Boon.Adrenaline => "A kill grants the killer +1 action (once/turn)",
        Boon.Venom => "Your hits make the target bleed",
        Boon.Ghost => "Moving near foes never breaks concealment",
        Boon.RapidDeploy => "Deploy one extra soldier all run",
        _ => "",
    };

    // short tag for the active-boons strip
    public static string Code(Boon b) => b switch
    {
        Boon.Marksmen => "MRK", Boon.Fervor => "FVR", Boon.Executioners => "EXE", Boon.Fortified => "FRT",
        Boon.Grenadier => "GRN", Boon.Scavenger => "SCV", Boon.Adrenaline => "ADR", Boon.Venom => "VNM",
        Boon.Ghost => "GHO", Boon.RapidDeploy => "RPD", _ => "?",
    };
}

/// A pickable next-mission deployment: objective + a risk/reward modifier.
public class MissionCard
{
    public Objective Objective;
    public string ModName;       // RECON / STANDARD / ONSLAUGHT
    public int EnemyDelta;       // +/- to the hostile count
    public int StatDelta;        // +/- to the hostile stat bump
    public RewardKind Reward;
    public string RewardText;
}

/// A node on the branching campaign map (3.3). Each node is one mission: an
/// objective + difficulty + reward (carried in Card) plus a node "kind" that
/// flavours the encounter. Nodes are laid out in columns (one per mission) and
/// connected to 1-2 nodes in the next column, FTL/Slay-the-Spire style.
public enum NodeKind { Start, Combat, Elite, Supply, Boss }

/// A run-end MEMORIAL entry: a snapshot of a soldier at the moment they fell, captured for the
/// run-summary card's KIA roll. PRESENTATION ONLY — populated from Game.KillUnit, read by Hud.
public struct FallenRec
{
    public string Name;     // FullName (incl. earned nickname)
    public string Cls;      // class
    public string Rank;     // rank name at death
    public int Kills;       // confirmed kills earned over the run
    public int Mission;     // mission number on which they fell
}

public class MissionNode
{
    public int Id;            // index into Run.Map
    public int Col;           // 0-based column; mission number = Col + 1
    public int Row;           // 0-based vertical slot within its column
    public int RowCount;      // nodes in this column (for layout)
    public NodeKind Kind;
    public Faction Faction = Faction.None;   // enemy faction for this fight (Wave 4); None = mixed force
    public MissionCard Card;  // objective + deltas + reward derived from Kind
    public List<int> Next = new();  // outgoing edges (node ids in the next column)
    public bool Visited;
    public int Mission => Col + 1;
    // Intel paid for CLEARING this node -- the routing economy / opportunity cost. SUPPLY pays an
    // economy premium (rest stop that also banks intel), ELITE pays a risk-for-reward premium. Set
    // deterministically in GenerateMap (depth-scaled), so it round-trips on load (the map is
    // regenerated from MapSeed) -- NOT a persisted field.
    public int Intel;
}

/// One rung of the Heat / Ascension ladder (Hades' Pact of Punishment / StS Ascension).
/// Heat level H applies modifiers 1..H cumulatively, escalating difficulty for a bigger
/// requisition payout. The KNOBS reuse the existing difficulty plumbing wherever possible:
/// EnemyDelta/StatDelta thread into the SAME Mission.Build/SpawnEnemies params the
/// deployment cards already use; the Game-side flags (tighter first contact / no
/// concealment / harsher attrition) are read where those systems live. Definitions are
/// data, never persisted (only the chosen LEVEL is saved) — so re-tuning is safe.
public class HeatModifier
{
    public string Name;       // short ALL-CAPS callsign shown in the UI
    public string Desc;       // one-line "what it does"
    public int EnemyDelta;    // extra hostiles this rung adds (folds into Mission.Build enemyDelta)
    public int StatDelta;     // extra hostile stat bump this rung adds (folds into statDelta)
    public bool TighterContact;  // shrink sight/alert/reveal ranges by 1 (first contact comes sooner)
    public bool Exposed;         // squad deploys NOT concealed (no free ambush opener)
    public bool HarshAttrition;  // wounds last +1 mission and field-heal is halved
    public bool NoReinforcements; // the barracks stops backfilling fallen soldiers — losses shrink the squad
}

/// The Heat ladder: a static data table + cumulative-effect accessors. The MAX selectable
/// level grows as the player wins runs at their current cap (persisted as meta, separate
/// from the deletable run save).
public static class Heat
{
    public const int Min = 0;
    public const int Max = 8;                 // ladder ceiling
    public const int IntelPerLevel = 3;       // extra requisition intel per cleared mission, per heat level

    // Rung i (1-based) is Mods[i-1]. Heat H applies rungs 1..H. Re-tuned against real
    // competent-AI batch data: the prior table leaned on +bodies, but enemy headcount
    // SATURATES at the spawn cap (12) on later missions, so the top rungs barely moved the
    // win-rate (heat 8 was ~57%, nearly flat vs heat 0's ~76%). The fix leans on the lever
    // that does NOT saturate -- StatDelta, a force-wide +1 HP & +1 Aim to EVERY hostile --
    // and folds the already-wired qualitative knobs (tighter contact, harsh attrition,
    // EXPOSED no-concealment opener, no reinforcements) in EARLIER so each rung adds real
    // texture, not just a number. Cumulative at the milestones the balance pass targets:
    //   heat 4 -> +2 enemy, +2 stat, tighter contact
    //   heat 6 -> +3 enemy, +3 stat, +harsh attrition, +EXPOSED (no free ambush opener)
    //   heat 8 -> +4 enemy, +5 stat, +no reinforcements (every prior flag too) = a real wall.
    // A force-wide +5 HP/+5 Aim at the top is the bulk of the difficulty (it scales with the
    // whole enemy count); the mutator flags supply the qualitative "no mercy" feel. Heat 0
    // stays a true no-op. Re-tuning the deltas/flags is SAVE-SAFE -- only the chosen LEVEL is
    // persisted, and "apply rungs 1..level cumulatively" (the meaning of a saved level) is
    // unchanged; rung indices keep their escalating-difficulty concept (no reorder/removal).
    public static readonly HeatModifier[] Mods =
    {
        new HeatModifier { Name = "REINFORCED",   Desc = "+1 enemy per mission",                 EnemyDelta = 1 },
        new HeatModifier { Name = "HARDENED",      Desc = "Enemies hit harder & tougher (+1 stat)", StatDelta = 1 },
        // SHORT FUSE now also brings a body -- the qualitative "spotted sooner" twist plus volume.
        new HeatModifier { Name = "SHORT FUSE",    Desc = "+1 enemy; enemies spot you sooner",    EnemyDelta = 1, TighterContact = true },
        new HeatModifier { Name = "ELITE CADRE",   Desc = "Enemies even deadlier (+1 stat)",      StatDelta = 1 },
        // LINGERING WOUNDS arrives earlier (rung 5) and carries a body -- run-loop attrition
        // pressure starts compounding in the mid-ladder instead of only near the top.
        new HeatModifier { Name = "LINGERING WOUNDS", Desc = "+1 enemy; wounds linger, less field healing", EnemyDelta = 1, HarshAttrition = true },
        // EXPOSED is the marquee mid-ladder MUTATOR: from heat 6 the squad loses its free
        // concealment ambush opener AND every hostile gets another stat point.
        new HeatModifier { Name = "EXPOSED",       Desc = "No concealment opener; +1 stat",       Exposed = true, StatDelta = 1 },
        // RELENTLESS: the run-loop screw -- fallen soldiers are NOT replaced (the squad shrinks
        // for the rest of the run) and the survivors face yet tougher enemies.
        new HeatModifier { Name = "RELENTLESS",    Desc = "No replacement recruits; +1 stat",     NoReinforcements = true, StatDelta = 1 },
        // NO QUARTER (rung 8, the ceiling): the final escalation -- one more body and the force
        // hits its peak durability/accuracy (+5 stat cumulative). With every flag above also
        // active, the top of the ladder is a genuine wall, beatable only by excellent play.
        new HeatModifier { Name = "NO QUARTER",    Desc = "+1 enemy; the deadliest force (+1 stat)", EnemyDelta = 1, StatDelta = 1 },
    };

    public static int Clamp(int level) => Math.Clamp(level, Min, Max);

    /// The modifiers ACTIVE at this heat level (rungs 1..level), in ladder order.
    public static IEnumerable<HeatModifier> Active(int level)
    {
        int n = Clamp(level);
        for (int i = 0; i < n && i < Mods.Length; i++) yield return Mods[i];
    }

    // ---- cumulative effect accessors (sum/any over the active rungs) ----
    public static int EnemyDelta(int level) { int s = 0; foreach (var m in Active(level)) s += m.EnemyDelta; return s; }
    public static int StatDelta(int level)  { int s = 0; foreach (var m in Active(level)) s += m.StatDelta;  return s; }
    public static bool TighterContact(int level) { foreach (var m in Active(level)) if (m.TighterContact) return true; return false; }
    public static bool Exposed(int level)        { foreach (var m in Active(level)) if (m.Exposed) return true; return false; }
    public static bool HarshAttrition(int level) { foreach (var m in Active(level)) if (m.HarshAttrition) return true; return false; }
    public static bool NoReinforcements(int level) { foreach (var m in Active(level)) if (m.NoReinforcements) return true; return false; }

    /// Bonus requisition intel per cleared mission at this heat level. ACCELERATING (not
    /// linear): a flat per-level base PLUS a quadratic kicker, so the now-genuinely-hard top
    /// rungs pay disproportionately more -- the carrot keeps pace with the steeper difficulty.
    /// heat 4 -> +20, heat 6 -> +36, heat 8 -> +56 (vs the old flat 3/level: 12/18/24).
    /// Strictly increasing; heat 0 stays a true no-op (0).
    public static int IntelBonus(int level)
    {
        int n = Clamp(level);
        return n * IntelPerLevel + n * n / 2;
    }
}

/// Holds the persistent squad across a campaign run, plus XP/rank progression.
public class Run
{
    public const int MaxMissions = 6;

    public static readonly string[] Ranks =
        { "ROOKIE", "SQUADDIE", "CORPORAL", "SERGEANT", "LIEUTENANT", "CAPTAIN", "MAJOR", "COLONEL" };

    // cumulative kills required to REACH each rank index
    public static readonly int[] KillReq = { 0, 1, 3, 6, 10, 15, 21, 28 };

    public const int BondThreshold = 3;       // missions two soldiers must survive together to bond

    // ---- WAVE 1: deep roster + deliberate deployment + adaptive assist ----
    // The persistent squad is now a ROSTER of up to RosterMax soldiers; each mission DEPLOYS a
    // subset of up to DeployCap (the rest sit on the bench and recover faster). This is the
    // XCOM/Long-War recovery valve: a wounded veteran benches and a fresh one deploys, so the
    // squad fields its best healthy DeployCap every mission instead of limping in chipped — the
    // single biggest counter to the campaign's geometric attrition collapse. DeployCap stays 4 so
    // the battlefield (spawns/autopilot/render) is unchanged; only the META roster grows.
    public const int RosterMax = 6;           // soldiers carried in the roster (deploy + bench; UI fits 6)

    // ---- ATTRITION: recruits TRICKLE, they don't instantly backfill ----
    // Losing soldiers must COST you. The barracks used to refill the roster to RosterMax every
    // mission, so a wipe-to-1 was erased by the next debrief and casualties had no teeth. Now at
    // most RecruitsPerBarracks fresh rookies join per barracks, so a bad mission leaves you
    // genuinely short-handed (fielding 3-4 instead of 5-6) for a mission or two while the roster
    // rebuilds. A HARD FLOOR (AttritionFloor) still guarantees a deployable squad — you can never
    // death-spiral below it (the run is only lost on a true wipe), so attrition bites without
    // becoming unfair. Rookies carry no rank/perks/mods — that's the price of the loss.
    public const int RecruitsPerBarracks = 1;  // max fresh rookies the barracks supplies per mission
    public const int AttritionFloor = 3;       // the roster is always topped up to at least this many

    // Deployed squad size GROWS as the campaign deepens. Balance data: with a flat 4-soldier
    // deploy, the run is a geometric product that collapses (4 soldiers x 2 actions vs 9-12
    // enemies by m5) — per-mission tweaks barely moved the 2% run-completion. Reinforcing the
    // collapse zone with extra BODIES (the only lever that scales with the enemy headcount) is
    // the master fix from the balance audit. Early missions stay at 4 (they're already ~85%+);
    // the squad grows to 5 mid-run and 6 for the brutal back half.
    public const int DeployCapBase = 4;       // m1-2
    public const int DeployCapMax = 6;        // m5-6
    public static int DeployCapFor(int mission) => mission >= 5 ? 6 : (mission >= 3 ? 5 : 4);

    // Adaptive assist (Hades God-Mode): consecutive LOST runs grant a small, capped, fully
    // reversible difficulty relief — but ONLY at base Heat (Heat is the opt-in hard mode, so the
    // assist never touches it). Persisted in meta.json (survives run-end, unlike the run save).
    public int LossStreak;                    // consecutive lost runs (meta; seeded at run start)
    public const int AssistMax = 5;           // ceiling on assist tiers

    public List<Unit> Squad = new();
    public int Mission;                       // current mission number (1-based)
    public int Intel;                         // requisition currency spent in the barracks shop
    // ---- one-time mid-run checkpoint ("REINFORCEMENTS") ----
    // The 6-mission single-life ironman is a geometric-collapse container: one bad mission ends the
    // whole run with zero recovery surface. This flag grants ONE emergency redeploy of fresh rookies
    // to retry the current mission after a squad wipe (at/after a threshold mission) — a bad mission
    // becomes survivable-but-costly (you lose your veterans) instead of run-ending. Set true the
    // moment the checkpoint fires, so a SECOND wipe is a real loss. Persisted (append-only DTO field;
    // old saves default false). Reset in Start().
    public bool CheckpointUsed;
    public int HeatLevel;                     // chosen Heat/Ascension difficulty (0..Heat.Max); persisted in the run save
    public List<string> Fallen = new();       // names of KIA soldiers
    // Run-end MEMORIAL (presentation only): a richer KIA record (full identity + rank/class/
    // kills + the mission they fell on) accumulated across the WHOLE run, so the run-summary
    // card can honour the fallen with more than a bare name. Read by Hud's end screen; NOT
    // persisted (a CONTINUE resumes mid-run, rebuilding it as soldiers fall). Appended one
    // entry per soldier death from the existing Game.KillUnit hook (see the one-line addition).
    public readonly List<FallenRec> Memorial = new();
    // co-survival tally per soldier pair ("A|B"); a bond forms at BondThreshold
    public Dictionary<string, int> BondTally = new();
    public List<string> Report = new();       // promotion/heal lines for the barracks
    public List<PerkOffer> PendingPerks = new(); // rank-up perk choices awaiting the player
    public List<MissionCard> Offers = new();  // next-mission deployment choices (fallback)
    public MissionCard CurrentCard;           // the card the active mission was launched from

    // ---- faction COUNTER-PREP (one-mission, bought at the barracks requisition) ----
    // The player can spend Intel to buy a one-mission counter to the faction they're about to
    // face (telegraphed on the campaign map). Stored here, PERSISTED in the save, APPLIED + CLEARED
    // at the next Game.SetupMission (which copies it into Combat.PrepFaction). None = no prep bought.
    public Faction PrepFaction = Faction.None;

    /// The faction the squad is about to face, as best known at the BARRACKS shop step (the node
    /// hasn't been chosen yet). We surface the first non-None faction among the reachable next nodes
    /// so the prep is offered for a real upcoming threat. If every reachable node is mixed-force
    /// (None), prep is unavailable. The prep is keyed to THIS faction and only takes effect next
    /// mission if Combat.MissionFaction actually matches (an honest, telegraphed bet).
    public Faction UpcomingFaction()
    {
        foreach (var n in NextNodes())
            if (n.Faction != Faction.None) return n.Faction;
        return Faction.None;
    }

    // ---- run-scoped boons (Wave 3 variance) ----
    public List<Boon> ActiveBoons = new();    // boons chosen this run (persisted within the run)
    public List<Boon> BoonOffer = new();      // the current pick-1-of-3 awaiting the player
    public bool HasBoon(Boon b) => ActiveBoons.Contains(b);

    // ---- branching campaign map (3.3) ----
    public List<MissionNode> Map = new();     // the generated DAG of mission nodes
    public int MapSeed;                       // seed the map is regenerated from on load
    public int MapPos = -1;                   // id of the current (last-cleared / start) node

    public MissionNode CurrentNode =>
        (MapPos >= 0 && MapPos < Map.Count) ? Map[MapPos] : null;

    /// The reachable next-column nodes from the current position (the player's choices).
    public List<MissionNode> NextNodes()
    {
        var list = new List<MissionNode>();
        var n = CurrentNode;
        if (n != null) foreach (var id in n.Next) if (id >= 0 && id < Map.Count) list.Add(Map[id]);
        return list;
    }

    /// Build the campaign DAG deterministically from a seed: MaxMissions columns
    /// (mission 1 = single START, last = single BOSS, middles 2-3 nodes), each
    /// node wired to 1-2 nodes in the next column with a connectivity fix-up so
    /// every node is reachable and every non-boss node leads onward.
    public void GenerateMap(int seed)
    {
        Map.Clear();
        var rng = new Random(seed);
        int cols = MaxMissions;
        var byCol = new List<List<MissionNode>>();
        int id = 0;
        for (int c = 0; c < cols; c++)
        {
            int count = (c == 0 || c == cols - 1) ? 1 : rng.Next(2, 4);  // 2 or 3 in the middle
            var colNodes = new List<MissionNode>();
            for (int r = 0; r < count; r++)
            {
                var node = new MissionNode { Id = id++, Col = c, Row = r, RowCount = count };
                colNodes.Add(node);
                Map.Add(node);
            }
            byCol.Add(colNodes);
        }

        // node kinds: START / BOSS fixed; sprinkle a few ELITE/SUPPLY through the middle
        foreach (var node in Map) node.Kind = NodeKind.Combat;
        Map[0].Kind = NodeKind.Start;
        Map[Map.Count - 1].Kind = NodeKind.Boss;
        var mids = Map.FindAll(n => n.Col > 0 && n.Col < cols - 1);
        for (int i = mids.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (mids[i], mids[j]) = (mids[j], mids[i]); }
        int elites = Math.Max(1, mids.Count / 5);
        int supplies = Math.Max(1, mids.Count / 4);
        int k = 0;
        for (int e = 0; e < elites && k < mids.Count; e++, k++) mids[k].Kind = NodeKind.Elite;
        for (int s = 0; s < supplies && k < mids.Count; s++, k++) mids[k].Kind = NodeKind.Supply;

        foreach (var node in Map) node.Card = CardForNode(node);

        // ROUTING ECONOMY: per-node Intel reward. Base scales with depth (the rising difficulty),
        // then SUPPLY and ELITE pay premiums so the route is a real trade-off -- a SUPPLY node is the
        // "economy stop" (rest + bank intel for the shop), an ELITE is "risk for reward" (heavier
        // force, but the biggest payout + a bonus perk). Deterministic from the node alone, so it
        // round-trips on load (the map is regenerated from MapSeed). NOT persisted.
        foreach (var node in Map) node.Intel = NodeIntel(node);

        // Enemy FACTIONS (Wave 4): give the main fights (Combat/Elite nodes) a faction identity so each
        // reads as a distinct opponent the player pre-plans against (the EnemyHint + banner show it; the
        // faction-gated roster + combat rule warp the encounter). START / SUPPLY / BOSS stay a mixed force
        // (None) so the opener, rest stops, and the WARLORD finale aren't themed. Deterministic from the
        // seeded rng, so it round-trips on load (the map is regenerated from MapSeed).
        var facPool = new[] { Faction.Syndicate, Faction.Legion, Faction.Wardens };
        foreach (var node in Map)
            if (node.Kind == NodeKind.Combat || node.Kind == NodeKind.Elite)
                node.Faction = facPool[rng.Next(facPool.Length)];

        // edges: wire each column to the next, then guarantee every next node has an entry
        for (int c = 0; c < cols - 1; c++)
        {
            var cur = byCol[c]; var nxt = byCol[c + 1];
            foreach (var a in cur)
            {
                int tgt = nxt.Count == 1 ? 0
                    : (int)Math.Round((double)a.Row / Math.Max(1, cur.Count - 1) * (nxt.Count - 1));
                tgt = Math.Clamp(tgt, 0, nxt.Count - 1);
                AddEdge(a, nxt[tgt]);
                if (nxt.Count > 1 && rng.NextDouble() < 0.45)           // sometimes branch
                {
                    int alt = tgt + (rng.Next(2) == 0 ? -1 : 1);
                    if (alt >= 0 && alt < nxt.Count) AddEdge(a, nxt[alt]);
                }
            }
            foreach (var b in nxt)
                if (!cur.Exists(a => a.Next.Contains(b.Id)))            // fix-up: ensure reachable
                {
                    MissionNode best = cur[0]; int bestd = int.MaxValue;
                    foreach (var a in cur) { int d = Math.Abs(a.Row - b.Row); if (d < bestd) { bestd = d; best = a; } }
                    AddEdge(best, b);
                }
        }
    }

    static void AddEdge(MissionNode a, MissionNode b) { if (!a.Next.Contains(b.Id)) a.Next.Add(b.Id); }

    /// Derive a deployment card from a node's kind: STANDARD combat, a tougher ELITE
    /// (+force, bonus perk), a lighter SUPPLY (-force, full heal), or the capstone
    /// BOSS (always Eliminate so the WARLORD must actually fall). Objective varies
    /// per row so branching nodes in a column offer different ops.
    MissionCard CardForNode(MissionNode node)
    {
        int n = node.Mission;
        Objective obj = ObjectiveFor(n + node.Row);
        switch (node.Kind)
        {
            case NodeKind.Start:
                return new MissionCard { Objective = ObjectiveFor(n), ModName = "STANDARD", Reward = RewardKind.None, RewardText = "-" };
            case NodeKind.Boss:
                // DECAPITATE, not Eliminate: balance data showed the boss mission as a forced
                // full-clear against a buffed brick reached by an already-attrited squad (~0% of
                // runs cleared it). "Kill the WARLORD, the rest don't matter" is a punch-through
                // the squad can actually pull off — the WARLORD is the natural HVT (DesignateHvt
                // picks the toughest non-special body, and skips its extra buff for an ELITE).
                return new MissionCard { Objective = Objective.Decapitate, ModName = "BOSS", Reward = RewardKind.None, RewardText = "Warlord" };
            case NodeKind.Elite:
                return new MissionCard { Objective = obj, ModName = "ELITE", EnemyDelta = 2, StatDelta = 1, Reward = RewardKind.BonusPerk, RewardText = "Bonus perk" };
            case NodeKind.Supply:
                return new MissionCard { Objective = obj, ModName = "SUPPLY", EnemyDelta = -1, StatDelta = -1, Reward = RewardKind.Heal, RewardText = "Full squad heal" };
            default:
                return new MissionCard { Objective = obj, ModName = "STANDARD", Reward = RewardKind.None, RewardText = "-" };
        }
    }

    /// Intel paid for clearing a node (the routing economy). Base tracks the old flat grant's
    /// depth term (10 + 4*mission) so the overall economy is unchanged on a STANDARD route; SUPPLY
    /// and ELITE add premiums so the branch pick trades survivability/risk for economy. START/BOSS
    /// keep the base (the opener + finale aren't economy decisions).
    public static int NodeIntel(MissionNode node)
    {
        int n = node.Mission;
        int baseIntel = 10 + 4 * n;
        return node.Kind switch
        {
            NodeKind.Supply => baseIntel + 10,   // economy route: rest + a meaningful intel bonus
            NodeKind.Elite  => baseIntel + 14,   // risk-for-reward: heavier force, the biggest payout
            _               => baseIntel,
        };
    }

    /// Harness jump: walk the map greedily to a node in the target mission's column,
    /// marking the path visited and adopting that node's card (so the post-mission
    /// barracks shows the correct downstream choices).
    public void JumpTo(int mission)
    {
        if (Map.Count == 0) return;
        int targetCol = Math.Clamp(mission - 1, 0, MaxMissions - 1);
        MapPos = 0; Map[0].Visited = true;
        var node = Map[0];
        while (node.Col < targetCol && node.Next.Count > 0)
        {
            node = Map[node.Next[0]];
            node.Visited = true;
            MapPos = node.Id;
        }
        CurrentCard = node.Card;
    }

    /// Objective rotation baseline: an 8-objective cycle (Eliminate / Hack / Evac / Escort /
    /// Sabotage / Rescue / Defend / Decapitate), repeating.
    public static Objective ObjectiveFor(int n) => ((n - 1) % 8) switch
    {
        1 => Objective.Hack,
        2 => Objective.Evac,
        3 => Objective.Escort,
        4 => Objective.Sabotage,
        5 => Objective.Rescue,
        6 => Objective.Defend,
        7 => Objective.Decapitate,
        _ => Objective.Eliminate,
    };

    public static MissionCard StandardCard(int n) => new MissionCard
    {
        Objective = ObjectiveFor(n), ModName = "STANDARD",
        EnemyDelta = 0, StatDelta = 0, Reward = RewardKind.None, RewardText = "-",
    };

    /// Build three distinct next-mission deployments: a safer RECON, a STANDARD,
    /// and a high-risk ONSLAUGHT, each with its own objective + reward.
    public void GenerateOffers(int n)
    {
        Offers.Clear();
        Objective def = ObjectiveFor(n);
        var pool = new List<Objective> { Objective.Eliminate, Objective.Hack, Objective.Evac, Objective.Escort, Objective.Sabotage, Objective.Rescue, Objective.Defend, Objective.Decapitate };
        Objective Other(params Objective[] avoid)
        {
            var picks = pool.FindAll(o => System.Array.IndexOf(avoid, o) < 0);
            return picks[Util.RandInt(0, picks.Count - 1)];
        }
        var recon = Other(def);
        var onslaught = Other(def, recon);
        Offers.Add(new MissionCard { Objective = recon, ModName = "RECON", EnemyDelta = -1, StatDelta = -1, Reward = RewardKind.Heal, RewardText = "Full squad heal" });
        Offers.Add(StandardCard(n));
        Offers.Add(new MissionCard { Objective = onslaught, ModName = "ONSLAUGHT", EnemyDelta = 2, StatDelta = 1, Reward = RewardKind.BonusPerk, RewardText = "Bonus perk" });
    }

    /// Reward: grant a bonus perk choice to a random survivor who has perks left.
    public void AddBonusPerk() => TryQueueBonusPerk("ONSLAUGHT");

    /// Queue a pick-1-of-2 bonus perk for a random survivor with >=2 perks left.
    /// Returns false (and queues nothing) if no soldier is eligible.
    public bool TryQueueBonusPerk(string reason)
    {
        var eligible = Squad.FindAll(u => CountAvail(u) >= 2);
        if (eligible.Count == 0) return false;
        var u = eligible[Util.RandInt(0, eligible.Count - 1)];
        var avail = new List<Perk>();
        foreach (var p in PerkDef.All) if (!u.HasPerk(p)) avail.Add(p);
        PickPerkPair(avail, out Perk a, out Perk b);
        PendingPerks.Add(new PerkOffer { Unit = u, A = a, B = b });
        Report.Add($"{u.Name} earns a bonus perk ({reason})");
        return true;
    }

    // A pure stat-bump perk applies once on grant and is otherwise a passive non-decision
    // (Tank +HP / Sprinter +mob). Offering TWO of these together is the dullest possible pick,
    // so PickPerkPair nudges away from it (below) when a more interesting alternative exists.
    static bool IsStatBump(Perk p) => p == Perk.Tank || p == Perk.Sprinter;

    /// Choose two distinct perks from `avail` for a pick-1-of-2 offer (caller guarantees
    /// avail.Count >= 2). Light curation for variety: if the rolled pair is BOTH pure stat
    /// bumps and a non-stat-bump perk is available, re-roll the second pick among the
    /// interesting perks so every offer presents at least one real tactical decision. The
    /// happy path consumes the same two RNG draws as before, so behaviour only changes for
    /// the rare all-stat-bump pair (keeps things deterministic-friendly).
    static void PickPerkPair(List<Perk> avail, out Perk a, out Perk b)
    {
        int i = Util.RandInt(0, avail.Count - 1);
        int j = Util.RandInt(0, avail.Count - 2); if (j >= i) j++;
        a = avail[i]; b = avail[j];
        if (IsStatBump(a) && IsStatBump(b))
        {
            Perk first = a;   // local copy (an out-param can't be captured by the lambda below)
            var interesting = avail.FindAll(p => !IsStatBump(p) && p != first);
            if (interesting.Count > 0) b = interesting[Util.RandInt(0, interesting.Count - 1)];
        }
    }

    static int CountAvail(Unit u)
    {
        int c = 0;
        foreach (var p in PerkDef.All) if (!u.HasPerk(p)) c++;
        return c;
    }

    /// Begin a fresh campaign. `drafted` (when non-null) seats a player-chosen founding squad
    /// from the run-opening DRAFT; otherwise the fixed default core is used. Every existing
    /// caller (the harness path StartMission -> Start()) compiles unchanged via the default.
    public void Start(List<Unit> drafted = null)
    {
        Squad = drafted ?? Sightline.Mission.NewRunSquad();
        Mission = 0;
        Intel = 0;
        CheckpointUsed = false;   // the one-time REINFORCEMENTS redeploy is fresh each run
        Fallen.Clear();
        Report.Clear();
        PendingPerks.Clear();
        ActiveBoons.Clear();      // boons are run-scoped: a fresh run starts with none
        BoonOffer.Clear();
        // generate the branching campaign map and seat the squad at its START node
        MapSeed = Util.RandInt(1, int.MaxValue - 1);
        GenerateMap(MapSeed);
        MapPos = 0;
        Map[0].Visited = true;
        CurrentCard = Map[0].Card;
    }

    // ---- adaptive assist (meta) ----
    /// The active assist tier (0..AssistMax). Disabled above Heat 0 — Heat is the hard mode, so a
    /// struggling player on base difficulty gets help, while anyone climbing the ladder never does.
    public int AssistLevel => HeatLevel > 0 ? 0 : Math.Min(AssistMax, LossStreak);

    /// Enemy stat-bump relief from the assist (subtracted from statDelta in SetupMission): one
    /// point of force-wide -HP/-Aim per tier. Small + capped so it eases, never trivialises.
    public int AssistStatRelief => AssistLevel;

    /// Record a finished run for the assist meta: a win wipes the streak, a loss grows it (capped).
    public void RecordRunResult(bool win) { LossStreak = win ? 0 : Math.Min(99, LossStreak + 1); }

    // ---- deployment selection ----
    /// The deploy cap for the NEXT mission (the one the barracks is preparing for). The RapidDeploy
    /// boon adds a body, capped at DeployCapMax (= the battlefield spawn capacity), so it boosts the
    /// early/mid game without ever overflowing PlayerSpawns.
    public int NextDeployCap => Math.Min(DeployCapMax, DeployCapFor(Mission + 1) + (HasBoon(Boon.RapidDeploy) ? 1 : 0));

    /// Choose the DEFAULT deployment for the next mission: field the best NextDeployCap soldiers
    /// (healthy + senior first; the wounded sink to the bench when there are healthy alternatives),
    /// benching the rest. Sets Unit.Benched across the whole roster. The player may override this
    /// in the barracks; the autopilot just takes the default. Deterministic (stable ordering).
    public void AutoDeploy()
    {
        int cap = NextDeployCap;
        var ordered = new List<Unit>(Squad);
        ordered.Sort((a, b) =>
        {
            int aw = a.Wound > 0 ? 1 : 0, bw = b.Wound > 0 ? 1 : 0;
            if (aw != bw) return aw - bw;                       // healthy before wounded
            if (a.Rank != b.Rank) return b.Rank - a.Rank;       // senior before junior
            if (a.Hp != b.Hp) return b.Hp - a.Hp;               // healthier before hurt
            if (a.Kills != b.Kills) return b.Kills - a.Kills;   // bloodier before green
            return string.CompareOrdinal(a.Name, b.Name);       // stable tiebreak
        });
        for (int i = 0; i < ordered.Count; i++) ordered[i].Benched = i >= cap;
    }

    /// Soldiers that will deploy next mission (Benched == false), for UI/queries.
    public List<Unit> Deployed => Squad.FindAll(u => !u.Benched);

    // ---- boon offers ----
    /// Build a fresh pick-1-of-3 boon offer from the boons not yet taken this run (fewer if the
    /// pool is nearly exhausted; empty if all are owned). Deterministic-friendly (Util.RandInt).
    public void GenerateBoonOffer()
    {
        BoonOffer.Clear();
        var pool = new List<Boon>();
        foreach (var b in BoonDef.All) if (!HasBoon(b)) pool.Add(b);
        for (int i = pool.Count - 1; i > 0; i--) { int j = Util.RandInt(0, i); (pool[i], pool[j]) = (pool[j], pool[i]); }
        for (int i = 0; i < pool.Count && i < 3; i++) BoonOffer.Add(pool[i]);
    }

    /// Adopt a boon from the current offer (no-op if not on offer or already owned).
    public void ChooseBoon(Boon b)
    {
        if (!BoonOffer.Contains(b) || HasBoon(b)) return;
        ActiveBoons.Add(b);
        BoonOffer.Clear();
        Sightline.Stats.RecordBoon(BoonDef.Code(b));
        Report.Insert(0, $"BOON: {BoonDef.Name(b)}  ({BoonDef.Desc(b)})");
    }

    // ---- run-opening squad draft (Wave 3) ----
    /// The number of recruits offered in the run-opening draft (pick DraftCap of these).
    public const int DraftPoolSize = 6;

    /// Build the run-opening DRAFT candidate pool: DraftPoolSize fresh recruits with class
    /// VARIETY (no more than 2 of any single class) so the pick is a real "what squad thesis"
    /// decision, not a random dump. Deterministic-friendly (Mission.MakeRecruit -> Util.RandInt).
    /// Pure construction — does NOT touch run state, so it's safe to call from the self-test.
    public static List<Unit> GenerateDraftPool()
    {
        var pool = new List<Unit>();
        var classCount = new Dictionary<string, int>();
        // Phase 1 — seed DISTINCT classes first (cap 1 each), so the draft always offers a broad spread
        // (with 5 classes and a 6-card pool, every class appears at least once: the choice is which to
        // DOUBLE up + who to leave behind, not "which 3 classes did the dice give me"). Bounded re-roll.
        int guard = 0;
        while (pool.Count < DraftPoolSize && guard++ < 400)
        {
            var u = Sightline.Mission.MakeRecruit();
            classCount.TryGetValue(u.Cls, out int c);
            if (c >= 1) continue;                 // phase 1: at most one of each class
            classCount[u.Cls] = c + 1;
            pool.Add(u);
            if (classCount.Count >= 5) break;     // covered every class -> move to the fill phase
        }
        // Phase 2 — fill the remaining slots allowing a SECOND of any class (cap 2) for some duplication.
        guard = 0;
        while (pool.Count < DraftPoolSize && guard++ < 400)
        {
            var u = Sightline.Mission.MakeRecruit();
            classCount.TryGetValue(u.Cls, out int c);
            if (c >= 2) continue;
            classCount[u.Cls] = c + 1;
            pool.Add(u);
        }
        // Safety: if the (bounded) re-rolls somehow under-filled, top up so the pool is always exactly
        // DraftPoolSize (never blocks the draft).
        while (pool.Count < DraftPoolSize) pool.Add(Sightline.Mission.MakeRecruit());
        return pool;
    }

    /// Build a fresh pick-1-of-3 STARTING boon offer for the draft (distinct boons from the
    /// full pool — a fresh run owns none yet). Returned as a list; the draft single-selects one.
    public static List<Boon> GenerateDraftBoonOffer()
    {
        var pool = new List<Boon>(BoonDef.All);
        for (int i = pool.Count - 1; i > 0; i--) { int j = Util.RandInt(0, i); (pool[i], pool[j]) = (pool[j], pool[i]); }
        var offer = new List<Boon>();
        for (int i = 0; i < pool.Count && i < 3; i++) offer.Add(pool[i]);
        return offer;
    }

    /// Apply promotions (from accumulated kills) and field-heal to the survivors,
    /// then backfill empty squad slots with fresh rookie recruits.
    /// Each rank-up queues a perk choice (PendingPerks) the player resolves in the
    /// barracks; if a soldier already owns every perk it falls back to a stat bump.
    /// Builds the barracks Report; mutates Squad in place.
    public void DebriefSurvivors()
    {
        Report.Clear();
        PendingPerks.Clear();
        // Heat "LINGERING WOUNDS": wounds bite a mission longer and field medicine is halved.
        bool harsh = Heat.HarshAttrition(HeatLevel);
        foreach (var u in Squad.ToList())
        {
            // attrition: a wound from a previous mission recovers one step, then fresh
            // damage from THIS mission (gauged before the field-heal below) can add a new
            // one. Ending near-death wounds worse. -Aim/-Mobility apply while Wound > 0.
            // EXCEPTION: a benched soldier sat out the mission — they recover 2 steps and
            // get a full heal (no fresh-wound gauge, since they weren't in the field).
            int w0 = u.Wound;
            if (u.Benched)
            {
                // accelerated recovery: bench trades a mission's firepower for faster healing
                if (u.Wound > 0) u.Wound = Math.Max(0, u.Wound - 2);
                u.Hp = u.MaxHp;
                Report.Add($"{u.Name} benched -- recovering (full heal)");
                if (w0 > 0 && u.Wound == 0) Report.Add($"{u.Name} fully recovered from wounds");
            }
            else
            {
                if (u.Wound > 0) u.Wound--;
                int sev = u.Hp <= u.MaxHp / 4 ? 2 : (u.Hp <= u.MaxHp / 2 ? 1 : 0);
                if (sev > 0 && harsh) sev++;          // Heat: wounds linger an extra mission
                if (sev > u.Wound) u.Wound = sev;
                if (u.Wound > w0) Report.Add($"{u.Name} is WOUNDED ({u.Wound} mission{(u.Wound > 1 ? "s" : "")})");
                else if (w0 > 0 && u.Wound == 0) Report.Add($"{u.Name} recovered from wounds");
            }

            // FEATS -> earned traits (only survivors reach the barracks). Grant before
            // the field-heal so IronWill's +max HP is included in the patch-up.
            if (u.FeatMultiKill) GrantTrait(u, Trait.Killer);
            if (u.FeatClutch)    GrantTrait(u, Trait.ColdBlood);
            if (u.FeatVengeful)  GrantTrait(u, Trait.Vengeful);
            if (u.WasNearDeath)  GrantTrait(u, Trait.IronWill);
            u.FeatMultiKill = u.FeatClutch = u.FeatVengeful = u.WasNearDeath = u.AllyDown = false;

            // promotions: advance rank while kills clear the next threshold
            while (u.Rank < Ranks.Length - 1 && u.Kills >= KillReq[u.Rank + 1])
            {
                u.Rank++;
                var offer = MakePerkOffer(u);
                if (offer != null)
                {
                    PendingPerks.Add(offer);
                    Report.Add($"{u.Name} promoted to {Ranks[u.Rank]}  (choose a perk)");
                }
                else
                {
                    string buff = ApplyStatBoost(u, u.Rank);
                    Report.Add($"{u.Name} promoted to {Ranks[u.Rank]}  ({buff})");
                }
            }

            // field medicine: partial heal between missions (halved under Heat harsh attrition).
            // Raised 0.4 -> 0.55: balance data showed the squad limping into the mid-campaign
            // already chipped, turning each mission into a degrading roll instead of a fresh one.
            int before = u.Hp;
            int heal = (int)MathF.Ceiling(u.MaxHp * (harsh ? 0.25f : 0.55f));
            u.Hp = Math.Min(u.MaxHp, u.Hp + heal);
            if (u.Hp > before) Report.Add($"{u.Name} patched up  (+{u.Hp - before} HP)");
        }

        // bonds: every pair of survivors that shared this mission grows closer
        AdvanceBonds();

        // ATTRITION backfill (see RecruitsPerBarracks / AttritionFloor). Recruits TRICKLE in
        // rather than instantly refilling to RosterMax, so a wipe genuinely shrinks your strength
        // for a mission or two. A hard floor still guarantees a deployable squad (no death-spiral).
        // Heat "RELENTLESS" (rung 8) turns OFF all reinforcements — casualties permanently shrink
        // the roster for the run.
        if (Heat.NoReinforcements(HeatLevel))
        {
            if (Squad.Count < NextDeployCap)
                Report.Add($"No reinforcements (HEAT) -- deploying {Squad.Count} strong");
        }
        else
        {
            // 1) emergency floor: if a bad mission dropped the roster below AttritionFloor, top it
            //    straight back up to the floor (anti-death-spiral — you always have a squad to field).
            while (Squad.Count < AttritionFloor)
            {
                var rec = Sightline.Mission.MakeRecruit();
                Squad.Add(rec);
                Report.Add($"{rec.Name} drafted to fill the ranks  (ROOKIE {rec.Cls})");
            }
            // 2) normal trickle: above the floor, at most RecruitsPerBarracks rookie joins per
            //    barracks, so the roster rebuilds gradually toward RosterMax (losses still bite).
            int added = 0;
            while (Squad.Count < RosterMax && added < RecruitsPerBarracks)
            {
                var rec = Sightline.Mission.MakeRecruit();
                Squad.Add(rec);
                Report.Add($"{rec.Name} joins the roster  (ROOKIE {rec.Cls})");
                added++;
            }
            if (Squad.Count < RosterMax)
                Report.Add($"Roster understrength: {Squad.Count}/{RosterMax} (recruits trickle in)");
        }

        // pick the default deployment for next mission (best healthy DeployCap; bench the rest).
        AutoDeploy();

        if (Report.Count == 0) Report.Add("No changes this mission.");
    }

    /// Grant an earned trait, assign a nickname on the soldier's first feat, apply
    /// any immediate stat effect (IronWill), and log it to the barracks report.
    void GrantTrait(Unit u, Trait t)
    {
        if (u.HasTrait(t)) return;
        u.Traits.Add(t);
        if (t == Trait.IronWill) { u.MaxHp += Unit.IronWillHp; u.Hp += Unit.IronWillHp; }
        AssignNickname(u);   // no-op if already nicknamed
        Report.Add($"{u.Name} earned {TraitDef.Name(t)}  ({TraitDef.Feat(t)})");
    }

    /// Give an un-nicknamed soldier a callsign not already used in the squad.
    void AssignNickname(Unit u)
    {
        if (!string.IsNullOrEmpty(u.Nickname)) return;
        var used = new HashSet<string>();
        foreach (var s in Squad) if (!string.IsNullOrEmpty(s.Nickname)) used.Add(s.Nickname);
        var avail = new List<string>();
        foreach (var n in Nicknames.Pool) if (!used.Contains(n)) avail.Add(n);
        if (avail.Count == 0) return;
        u.Nickname = avail[Util.RandInt(0, avail.Count - 1)];
        Report.Add($"{u.Name} is now known as \"{u.Nickname}\"");
    }

    /// Increment co-survival for each pair of surviving soldiers; forge a bond when a
    /// pair has fought through BondThreshold missions together. (Called pre-backfill,
    /// so only true mission survivors tally.)
    void AdvanceBonds()
    {
        var alive = Squad.FindAll(u => u.Alive && !u.IsVip);
        for (int i = 0; i < alive.Count; i++)
            for (int j = i + 1; j < alive.Count; j++)
            {
                var a = alive[i]; var b = alive[j];
                string key = BondKey(a.Name, b.Name);
                int c = (BondTally.TryGetValue(key, out var v) ? v : 0) + 1;
                BondTally[key] = c;
                if (c >= BondThreshold && !a.Bonds.Contains(b.Name))
                {
                    a.Bonds.Add(b.Name); b.Bonds.Add(a.Name);
                    Report.Add($"{a.Name} & {b.Name} forged a BOND  (+{Unit.BondAim} aim when adjacent)");
                }
            }
    }

    public static string BondKey(string a, string b) =>
        string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;

    // Class-flavoured perk build-trees: each class has a thematic perk LINE so a soldier grows
    // into its archetype over a run (sharpshooter -> precision/crit, gunner -> tanky/overwatch,
    // ranger -> mobile/flanker, assault -> close-range bruiser, corpsman -> durable support).
    // Every perk appears in >=1 line; many appear in several (build flavour overlaps, not silos).
    // Used only to BIAS the offer (see MakePerkOffer) -- it never restricts what can be granted.
    static Perk[] ClassLine(string cls) => (cls ?? "").ToUpperInvariant() switch
    {
        // Precision marksmen: long-range aim + crit + a defensive overwatch lean + double-tap.
        "SHARPSHOOTER" => new[] { Perk.Marksman, Perk.LockOn, Perk.Executioner,
                                  Perk.Guardian, Perk.Reflexes, Perk.Gunslinger },
        // Close-range bruisers: alpha-strike finisher + mobility to close + shoot-then-slip.
        "ASSAULT"      => new[] { Perk.CloseQuarters, Perk.GiantSlayer, Perk.Sprinter,
                                  Perk.Bandolier, Perk.Adrenal, Perk.Skirmisher },
        // Heavy weapons: durability + reaction-fire control to anchor the line + double-tap.
        "GUNNER"       => new[] { Perk.Tank, Perk.Bulwark, Perk.Hardened, Perk.Reflexes,
                                  Perk.Guardian, Perk.LockOn, Perk.CoolHeaded, Perk.Gunslinger },
        // Skirmishers: speed + first-contact alpha + closing aim + shoot-then-slip.
        "RANGER"       => new[] { Perk.Sprinter, Perk.GiantSlayer, Perk.CloseQuarters,
                                  Perk.LockOn, Perk.Adrenal, Perk.Skirmisher },
        // Field medics: stay alive + keep the kit topped up to support the squad.
        "CORPSMAN"     => new[] { Perk.Hardened, Perk.Tank, Perk.CoolHeaded, Perk.Bandolier,
                                  Perk.Adrenal },
        _              => System.Array.Empty<Perk>(),
    };

    /// Offer two distinct perks the soldier doesn't already own (null if <2 left). The pick-1-of-2
    /// is BIASED toward the soldier's class line so it develops a coherent archetype over a run:
    /// option A is a random unowned perk from the class line (if any remain), option B is a random
    /// unowned perk from the WHOLE pool (so there's always an off-archetype option -- a real choice).
    /// Falls back to the flat any-2 PickPerkPair when the class line is exhausted (or class unknown).
    static PerkOffer MakePerkOffer(Unit u)
    {
        var avail = new List<Perk>();
        foreach (var p in PerkDef.All) if (!u.HasPerk(p)) avail.Add(p);
        if (avail.Count < 2) return null;

        // A = a still-available perk from this soldier's class line (the on-archetype option).
        var line = new List<Perk>();
        foreach (var p in ClassLine(u.Cls)) if (!u.HasPerk(p)) line.Add(p);
        if (line.Count == 0)
        {
            // class line exhausted / unknown -> keep the original flat, lightly-curated behaviour.
            PickPerkPair(avail, out Perk fa, out Perk fb);
            return new PerkOffer { Unit = u, A = fa, B = fb };
        }
        Perk a = line[Util.RandInt(0, line.Count - 1)];

        // B = a random unowned perk from the whole pool, distinct from A (the off-archetype option,
        // so the choice stays real). Nudge away from a dull two-pure-stat-bump pair when we can.
        var other = avail.FindAll(p => p != a);
        var spicy = other.FindAll(p => !(IsStatBump(a) && IsStatBump(p)));
        var bPool = spicy.Count > 0 ? spicy : other;   // other is non-empty (avail.Count >= 2)
        Perk b = bPool[Util.RandInt(0, bPool.Count - 1)];

        return new PerkOffer { Unit = u, A = a, B = b };
    }

    /// Grant a chosen perk, applying any immediate stat effect.
    public static void ApplyPerk(Unit u, Perk p)
    {
        if (u.HasPerk(p)) return;
        u.Perks.Add(p);
        switch (p)
        {
            case Perk.Tank: u.MaxHp += 3; u.Hp += 3; break;
            case Perk.Sprinter: u.Mobility += 1; break;
            // the rest are passive modifiers read at combat/refill time
        }
    }

    // fallback when a maxed-out soldier ranks up with no perks left to offer
    static string ApplyStatBoost(Unit u, int rank)
    {
        switch (rank % 3)
        {
            case 0: u.Aim += 5; return "+5 Aim";
            case 1: u.MaxHp += 1; u.Hp += 1; return "+1 HP";
            default: u.Mobility += 1; return "+1 Mobility";
        }
    }

    public int KillsToNext(Unit u)
    {
        if (u.Rank >= Ranks.Length - 1) return 0;
        return Math.Max(0, KillReq[u.Rank + 1] - u.Kills);
    }

    /// A short ASCII intel hint for a mission node — shown on the campaign map so the
    /// player can make an informed pick. Derived purely from Kind + Mission column (i.e.
    /// the real spawn gating in Mission.SpawnEnemies), so it's always deterministic and
    /// roughly accurate. Kept <= ~22 chars so it fits beneath a node label.
    /// Display name of an enemy faction (Wave 4), used by the campaign-map hint + the mission banner.
    public static string FactionName(Faction f) => f switch
    {
        Faction.Syndicate => "SYNDICATE",
        Faction.Legion    => "LEGION",
        Faction.Wardens   => "WARDENS",
        _ => "",
    };

    public static string EnemyHint(MissionNode node)
    {
        // Faction nodes read by their faction + signature units (the roster is faction-gated), so the
        // branch pick telegraphs the encounter's personality (counter-build before you commit).
        if (node.Faction != Faction.None)
            return node.Faction switch
            {
                Faction.Syndicate => "SYNDICATE: drones + shields",
                Faction.Legion    => "LEGION: berserkers rush",
                Faction.Wardens   => "WARDENS: snipers + mortars",
                _ => FactionName(node.Faction),
            };
        int m = node.Mission;   // 1-based column == mission number
        switch (node.Kind)
        {
            case NodeKind.Boss:
                // GenerateMap only ever places ONE Boss node, at the final column, so the
                // capstone WARLORD is the boss. (BREAKER m3 / WARDEN m5 appear as mid-bosses
                // on Combat/Elite nodes, not as Boss-kind nodes — see Mission.midBoss.)
                return "BOSS: WARLORD";
            case NodeKind.Supply:
                // Supply only thins the force (fewer enemies / lower stats); the archetype
                // pool is unchanged, so don't overclaim composition (review Sprint 5 F1).
                return "LIGHT FORCE";
            case NodeKind.Elite:
                // Same tier as Combat but heavier (+2 enemy delta)
                if (m <= 2) return "BRUISER + HUNTER";
                if (m <= 4) return "MORTAR + SHIELD";
                return "BERSERKER + MEDIC";
            default: // Combat / Start — normal force for this mission tier
                if (m == 1)    return "GRUNTS + SCOUTS";
                if (m == 2)    return "HOUND PACK + HUNTER";
                if (m == 3)    return "LANCER LINE + MORTAR";
                if (m == 4)    return "BERSERKER + DRONE";
                if (m == 5)    return "SHIELD + MEDIC";
                return                 "ELITE FORCE";
        }
    }

    static Unit TestSoldier(string name) => new Unit
    {
        Name = name, Cls = "ASSAULT", Team = Team.Player,
        Hp = 8, MaxHp = 8, Aim = 65, Mobility = 7, Weapon = Weapon.Make(WeaponKind.Rifle),
    };

    /// Headless self-test (SIGHTLINE_TRAITTEST): feats resolve into traits + a
    /// nickname at the barracks, the traits read correctly in combat, and bonds form
    /// after enough shared missions. Returns a one-line report. No window required.
    public static string TraitSelfTest()
    {
        var fails = new List<string>();

        // (1) FEATS -> traits + nickname at debrief (survivors only reach the barracks)
        var r = new Run();
        var v = TestSoldier("ALPHA");
        int hp0 = v.MaxHp;
        v.FeatMultiKill = true; v.FeatClutch = true; v.WasNearDeath = true;
        r.Squad = new List<Unit> { v };
        r.DebriefSurvivors();
        if (!v.HasTrait(Trait.Killer)) fails.Add("killerTrait");
        if (!v.HasTrait(Trait.ColdBlood)) fails.Add("coldTrait");
        if (!v.HasTrait(Trait.IronWill)) fails.Add("ironTrait");
        if (v.MaxHp != hp0 + Unit.IronWillHp) fails.Add("ironHp");
        if (string.IsNullOrEmpty(v.Nickname)) fails.Add("nickname");
        if (v.FeatMultiKill || v.FeatClutch || v.WasNearDeath) fails.Add("featsNotCleared");

        // (2) trait effects in ComputeOdds (open ground, no cover)
        var grid = new Grid();
        Unit Atk() => new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 5, Y = 5, Hp = 8, MaxHp = 8 };
        Unit Def() => new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 9, Y = 5, Hp = 8, MaxHp = 8 };

        var a1 = Atk(); var d1 = Def(); d1.Hp = 3;                 // wounded target
        int baseHit = Combat.ComputeOdds(grid, a1, d1).HitChance;
        a1.Traits.Add(Trait.Killer);
        if (Combat.ComputeOdds(grid, a1, d1).HitChance - baseHit != Unit.KillerAim) fails.Add("killerAim");

        var a2 = Atk(); a2.Hp = 3; var d2 = Def();                // self bloodied
        int baseCrit = Combat.ComputeOdds(grid, a2, d2).CritChance;
        a2.Traits.Add(Trait.ColdBlood);
        if (Combat.ComputeOdds(grid, a2, d2).CritChance - baseCrit != Unit.ColdBloodCrit) fails.Add("coldCrit");

        var a3 = Atk(); a3.Traits.Add(Trait.Vengeful); var d3 = Def();
        int noVeng = Combat.ComputeOdds(grid, a3, d3).HitChance;
        a3.AllyDown = true;
        if (Combat.ComputeOdds(grid, a3, d3).HitChance - noVeng != Unit.VengefulAim) fails.Add("vengefulAim");

        var a4 = Atk(); var d4 = Def();
        int noBond = Combat.ComputeOdds(grid, a4, d4).HitChance;
        a4.BondAura = true;
        if (Combat.ComputeOdds(grid, a4, d4).HitChance - noBond != Unit.BondAim) fails.Add("bondAim");

        // (3) bonds form after BondThreshold shared missions
        var r2 = new Run();
        var pa = TestSoldier("PAXTON"); var pb = TestSoldier("QUINN");
        for (int m = 0; m < BondThreshold; m++)
        {
            r2.Squad = new List<Unit> { pa, pb };                 // only the two truly survive
            pa.Hp = pa.MaxHp; pb.Hp = pb.MaxHp;
            r2.DebriefSurvivors();
            if (m < BondThreshold - 1 && (pa.Bonds.Count > 0 || pb.Bonds.Count > 0)) fails.Add("bondEarly");
        }
        if (!pa.Bonds.Contains("QUINN") || !pb.Bonds.Contains("PAXTON")) fails.Add("bondForm");

        return fails.Count == 0
            ? "TRAITTEST: PASS (feats->traits+nickname, combat reads, bonds form)"
            : "TRAITTEST: FAIL (" + string.Join(",", fails) + ")";
    }
}
