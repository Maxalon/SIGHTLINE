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

/// Holds the persistent squad across a campaign run, plus XP/rank progression.
public class Run
{
    public const int MaxMissions = 6;

    public static readonly string[] Ranks =
        { "ROOKIE", "SQUADDIE", "CORPORAL", "SERGEANT", "LIEUTENANT", "CAPTAIN", "MAJOR", "COLONEL" };

    // cumulative kills required to REACH each rank index
    public static readonly int[] KillReq = { 0, 1, 3, 6, 10, 15, 21, 28 };

    public List<Unit> Squad = new();
    public int Mission;                       // current mission number (1-based)
    public List<string> Fallen = new();       // names of KIA soldiers
    public List<string> Report = new();       // promotion/heal lines for the barracks
    public List<PerkOffer> PendingPerks = new(); // rank-up perk choices awaiting the player
    public List<MissionCard> Offers = new();  // next-mission deployment choices
    public MissionCard CurrentCard;           // the card the active mission was launched from

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
    public void AddBonusPerk()
    {
        var eligible = Squad.FindAll(u => CountAvail(u) >= 2);
        if (eligible.Count == 0) return;
        var u = eligible[Util.RandInt(0, eligible.Count - 1)];
        var avail = new List<Perk>();
        foreach (var p in PerkDef.All) if (!u.HasPerk(p)) avail.Add(p);
        int i = Util.RandInt(0, avail.Count - 1);
        int j = Util.RandInt(0, avail.Count - 2); if (j >= i) j++;
        PendingPerks.Add(new PerkOffer { Unit = u, A = avail[i], B = avail[j] });
        Report.Add($"{u.Name} earns a bonus perk (ONSLAUGHT)");
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
        Fallen.Clear();
        Report.Clear();
        PendingPerks.Clear();
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

        // backfill the squad up to 4 with rookie recruits
        while (Squad.Count < 4)
        {
            var rec = Sightline.Mission.MakeRecruit();
            Squad.Add(rec);
            Report.Add($"{rec.Name} joins the squad  (ROOKIE {rec.Cls})");
        }

        if (Report.Count == 0) Report.Add("No changes this mission.");
    }

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
}
