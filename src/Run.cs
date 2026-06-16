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

public class MissionNode
{
    public int Id;            // index into Run.Map
    public int Col;           // 0-based column; mission number = Col + 1
    public int Row;           // 0-based vertical slot within its column
    public int RowCount;      // nodes in this column (for layout)
    public NodeKind Kind;
    public MissionCard Card;  // objective + deltas + reward derived from Kind
    public List<int> Next = new();  // outgoing edges (node ids in the next column)
    public bool Visited;
    public int Mission => Col + 1;
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

    public List<Unit> Squad = new();
    public int Mission;                       // current mission number (1-based)
    public int Intel;                         // requisition currency spent in the barracks shop
    public List<string> Fallen = new();       // names of KIA soldiers
    // co-survival tally per soldier pair ("A|B"); a bond forms at BondThreshold
    public Dictionary<string, int> BondTally = new();
    public List<string> Report = new();       // promotion/heal lines for the barracks
    public List<PerkOffer> PendingPerks = new(); // rank-up perk choices awaiting the player
    public List<MissionCard> Offers = new();  // next-mission deployment choices (fallback)
    public MissionCard CurrentCard;           // the card the active mission was launched from

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
                return new MissionCard { Objective = Objective.Eliminate, ModName = "BOSS", Reward = RewardKind.None, RewardText = "Warlord" };
            case NodeKind.Elite:
                return new MissionCard { Objective = obj, ModName = "ELITE", EnemyDelta = 2, StatDelta = 1, Reward = RewardKind.BonusPerk, RewardText = "Bonus perk" };
            case NodeKind.Supply:
                return new MissionCard { Objective = obj, ModName = "SUPPLY", EnemyDelta = -1, StatDelta = -1, Reward = RewardKind.Heal, RewardText = "Full squad heal" };
            default:
                return new MissionCard { Objective = obj, ModName = "STANDARD", Reward = RewardKind.None, RewardText = "-" };
        }
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

    /// Objective rotation baseline: Eliminate / Hack / Evac / Escort, repeating.
    public static Objective ObjectiveFor(int n) => ((n - 1) % 4) switch
    {
        1 => Objective.Hack,
        2 => Objective.Evac,
        3 => Objective.Escort,
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
        var pool = new List<Objective> { Objective.Eliminate, Objective.Hack, Objective.Evac, Objective.Escort };
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
        int i = Util.RandInt(0, avail.Count - 1);
        int j = Util.RandInt(0, avail.Count - 2); if (j >= i) j++;
        PendingPerks.Add(new PerkOffer { Unit = u, A = avail[i], B = avail[j] });
        Report.Add($"{u.Name} earns a bonus perk ({reason})");
        return true;
    }

    static int CountAvail(Unit u)
    {
        int c = 0;
        foreach (var p in PerkDef.All) if (!u.HasPerk(p)) c++;
        return c;
    }

    public void Start()
    {
        Squad = Sightline.Mission.NewRunSquad();
        Mission = 0;
        Intel = 0;
        Fallen.Clear();
        Report.Clear();
        PendingPerks.Clear();
        // generate the branching campaign map and seat the squad at its START node
        MapSeed = Util.RandInt(1, int.MaxValue - 1);
        GenerateMap(MapSeed);
        MapPos = 0;
        Map[0].Visited = true;
        CurrentCard = Map[0].Card;
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
        foreach (var u in Squad.ToList())
        {
            // attrition: a wound from a previous mission recovers one step, then fresh
            // damage from THIS mission (gauged before the field-heal below) can add a new
            // one. Ending near-death wounds worse. -Aim/-Mobility apply while Wound > 0.
            int w0 = u.Wound;
            if (u.Wound > 0) u.Wound--;
            int sev = u.Hp <= u.MaxHp / 4 ? 2 : (u.Hp <= u.MaxHp / 2 ? 1 : 0);
            if (sev > u.Wound) u.Wound = sev;
            if (u.Wound > w0) Report.Add($"{u.Name} is WOUNDED ({u.Wound} mission{(u.Wound > 1 ? "s" : "")})");
            else if (w0 > 0 && u.Wound == 0) Report.Add($"{u.Name} recovered from wounds");

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

            // field medicine: partial heal between missions
            int before = u.Hp;
            int heal = (int)MathF.Ceiling(u.MaxHp * 0.4f);
            u.Hp = Math.Min(u.MaxHp, u.Hp + heal);
            if (u.Hp > before) Report.Add($"{u.Name} patched up  (+{u.Hp - before} HP)");
        }

        // bonds: every pair of survivors that shared this mission grows closer
        AdvanceBonds();

        // backfill the squad up to 4 with rookie recruits
        while (Squad.Count < 4)
        {
            var rec = Sightline.Mission.MakeRecruit();
            Squad.Add(rec);
            Report.Add($"{rec.Name} joins the squad  (ROOKIE {rec.Cls})");
        }

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

    /// Offer two distinct perks the soldier doesn't already own (null if <2 left).
    static PerkOffer MakePerkOffer(Unit u)
    {
        var avail = new List<Perk>();
        foreach (var p in PerkDef.All) if (!u.HasPerk(p)) avail.Add(p);
        if (avail.Count < 2) return null;
        int i = Util.RandInt(0, avail.Count - 1);
        int j = Util.RandInt(0, avail.Count - 2);
        if (j >= i) j++;                         // distinct second pick
        return new PerkOffer { Unit = u, A = avail[i], B = avail[j] };
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
