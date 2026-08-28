using System;
using System.Collections.Generic;

namespace Sightline;

// PROGRAM HORIZON — Wave 3: WAR ROOM cross-run meta-progression.
//
// The persistent-meta MODEL: an enum of additive UNLOCKS bought with the persistent SALVAGE currency,
// plus a table of ACHIEVEMENTS earned from run-end data (each granting a one-time salvage bounty). The
// persistence itself lives in SaveGame (meta.json, whole-DTO r-m-w, all append-only). The APPLICATION of
// unlocks + award/record hooks live in Game (StartMission / EnterBarracks / LoseRun / EndEndless), all
// gated behind !NoPersist so the flywheel/harness never read meta and stay byte-stable.

/// Additive campaign-start perks purchased in the WAR ROOM with SALVAGE. APPEND-ONLY (persisted by
/// ordinal in meta.json's Unlocks list — never reorder/remove; add new members at the END only).
public enum MetaUnlock
{
    StartIntel,     // campaign runs start with +15 Intel
    StartBoon,      // campaign runs start with 1 random boon already active
    StartArmor,     // every founding-squad soldier starts with +1 Armor
    // ---- W9 (SIGNAL) horizontal unlocks: wider OPTIONS, not raw power. APPEND-ONLY. ----
    CrossTraining,   // draft recruits may arrive carrying an alternate class-legal weapon
    Quartermaster,   // the barracks requisition slate offers one extra item
    StandingReserve, // the run-opening draft can recall a THIRD veteran (each still priced)
}

/// The WAR ROOM meta model: unlock definitions (name/desc/cost) + the achievement catalogue.
/// Pure data + helpers — no disk I/O (that's SaveGame) and no game state.
public static class MetaProg
{
    // The full unlock list, in display order (== ordinal order).
    public static readonly MetaUnlock[] AllUnlocks =
    {
        MetaUnlock.StartIntel, MetaUnlock.StartBoon, MetaUnlock.StartArmor,
        MetaUnlock.CrossTraining, MetaUnlock.Quartermaster, MetaUnlock.StandingReserve,
    };

    public static string UnlockName(MetaUnlock u) => u switch
    {
        MetaUnlock.StartIntel => "SUPPLY LINE",
        MetaUnlock.StartBoon  => "STANDING ORDERS",
        MetaUnlock.StartArmor => "ISSUED PLATING",
        MetaUnlock.CrossTraining   => "CROSS-TRAINING",
        MetaUnlock.Quartermaster   => "QUARTERMASTER",
        MetaUnlock.StandingReserve => "STANDING RESERVE",
        _ => u.ToString(),
    };

    public static string UnlockDesc(MetaUnlock u) => u switch
    {
        MetaUnlock.StartIntel => "Every campaign run starts with +15 Intel.",
        MetaUnlock.StartBoon  => "Start each campaign run with one random boon active.",
        MetaUnlock.StartArmor => "Every founding-squad soldier starts with +1 Armor.",
        MetaUnlock.CrossTraining   => "Draft recruits may carry an alternate class-legal weapon.",
        MetaUnlock.Quartermaster   => "The requisition slate offers one extra item each barracks.",
        MetaUnlock.StandingReserve => "The draft can recall a third veteran from the reserve.",
        _ => "",
    };

    public static int UnlockCost(MetaUnlock u) => u switch
    {
        MetaUnlock.StartIntel => 40,
        MetaUnlock.StartBoon  => 70,
        MetaUnlock.StartArmor => 90,
        MetaUnlock.CrossTraining   => 50,
        MetaUnlock.Quartermaster   => 35,
        MetaUnlock.StandingReserve => 45,
        _ => 0,
    };

    // ---- W9 (SIGNAL): the standing salvage economy — priced recall + repeatable sinks ----
    // The recall makes the measured +10pt veteran power floor a PURCHASE, not a freebie:
    // Rank-1 ~18 stays discoverable, Rank-3 ~34; a full 2-veteran draft (~52) ≈ one h0 win's
    // income (~61). Charged ONCE in Game.ConfirmDraft — never at pick time (picks toggle
    // freely; the draft BACK button must always leave the bank untouched).
    public const int RecallBase = 10, RecallPerRank = 8;
    public static int RecallCost(int rank) => RecallBase + RecallPerRank * Math.Max(0, rank);
    // FUL-10 LGD: run-end pension per rank for surviving Rank>=2 soldiers (LIVING LEGENDS only).
    public const int LegendPension = 6;
    // Repeatable sinks (Game.Meta.cs TryBuy* pattern): all opt-in, all NoPersist-gated.
    public const int DraftRerollCost = 10;   // re-roll the run-opening draft candidate pool
    public const int ScarRehabCost   = 30;   // buy one scar off a soldier in the barracks
    public const int ShopRerollCost  = 5;    // re-roll a barracks requisition slate

    // ---- achievements ----
    // Each achievement is checkable purely from run-end data and grants a ONE-TIME salvage bounty the
    // first time it unlocks. Ids are stable strings persisted in meta.json (order-independent).
    public const int AchievementSalvage = 20;   // bounty on first unlock of any achievement

    /// A stable achievement id + its display name + description.
    public readonly struct Achievement
    {
        public readonly string Id, Name, Desc;
        public Achievement(string id, string name, string desc) { Id = id; Name = name; Desc = desc; }
    }

    public static readonly Achievement[] All =
    {
        new("FIRST_WIN", "FIRST BLOOD",   "Win a campaign run."),
        new("HEAT3",     "TURNING UP",    "Win a run at Heat 3 or higher."),
        new("HEAT6",     "INFERNO",       "Win a run at Heat 6 or higher."),
        new("FLAWLESS",  "NO ONE LEFT",   "Win a run with no soldiers lost."),
        new("DEEP",      "THE LONG WAR",  "Reach mission 6 in any run."),
        new("STAND5",    "HOLD THE LINE", "Reach wave 5 in LAST STAND."),
        new("STAND10",   "UNBROKEN",      "Reach wave 10 in LAST STAND."),
        // W9 (SIGNAL): the retention modes feed the meta — appended at the END (ids are stable strings).
        new("DAILY_WIN", "DAY SHIFT",     "Win a SEEDED DAILY."),
        new("STREAK5",   "DAWN PATROL",   "Win dailies on 5 consecutive days."),
    };

    public static string AchievementName(string id)
    {
        foreach (var a in All) if (a.Id == id) return a.Name;
        return id;
    }
}
