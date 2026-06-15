using System;
using System.Collections.Generic;

namespace Sightline;

/// A rank-up perk choice presented in the barracks: pick A or B.
public class PerkOffer
{
    public Unit Unit;
    public Perk A, B;
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
