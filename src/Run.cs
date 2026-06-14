using System;
using System.Collections.Generic;

namespace Breach;

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

    public void Start()
    {
        Squad = Breach.Mission.NewRunSquad();
        Mission = 0;
        Fallen.Clear();
        Report.Clear();
    }

    /// Apply promotions (from accumulated kills) and field-heal to the survivors,
    /// then backfill empty squad slots with fresh rookie recruits.
    /// Builds the barracks Report; mutates Squad in place.
    public void DebriefSurvivors()
    {
        Report.Clear();
        foreach (var u in Squad.ToList())
        {
            // promotions: advance rank while kills clear the next threshold
            while (u.Rank < Ranks.Length - 1 && u.Kills >= KillReq[u.Rank + 1])
            {
                u.Rank++;
                string buff = ApplyPromotion(u, u.Rank);
                Report.Add($"{u.Name} promoted to {Ranks[u.Rank]}  ({buff})");
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
            var rec = Breach.Mission.MakeRecruit();
            Squad.Add(rec);
            Report.Add($"{rec.Name} joins the squad  (ROOKIE {rec.Cls})");
        }

        if (Report.Count == 0) Report.Add("No changes this mission.");
    }

    static string ApplyPromotion(Unit u, int rank)
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
