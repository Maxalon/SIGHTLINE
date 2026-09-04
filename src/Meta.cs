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
    // ---- P18 "THE SECOND AXIS": the HEAT-GATED column. APPEND-ONLY, like everything above. ----
    // These are not bought with salvage alone: each also demands a heat rung CLEARED
    // (MetaProg.UnlockHeatGate), so the reward curve's domain is the DIFFICULTY curve's domain.
    CombatTrials,    // every perk offer is a pick-1-of-THREE
    DeepReserve,     // the cross-run veteran reserve holds DeepReserveCap records instead of 12
    DeepStores,      // the barracks requisition slate offers one MORE item (stacks with QUARTERMASTER)
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
        // P18 THE SECOND AXIS — heat-gated, and therefore last in display order.
        MetaUnlock.CombatTrials, MetaUnlock.DeepReserve, MetaUnlock.DeepStores,
    };

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  P18 "THE SECOND AXIS" — THE LADDER PAYS IN WIDTH
    //
    //  THE DEFECT. The WAR ROOM's six salvage unlocks cost 330 in total. A full six-mission clear
    //  at heat 0 banks (25 + 6*6) = 61 (Game.AwardMetaRunEnd), plus one-time achievement bounties
    //  of 20 — so a player owns the entire shop after roughly five wins. The DIFFICULTY curve does
    //  not stop there: Game.WinRun raises UnlockedHeat by one per win AT the cap, and the cap runs
    //  to Heat.Max = 8, i.e. EIGHT such wins. From win ~5 to win 8 the challenge keeps climbing
    //  while the permanent reward is flat at zero. That is the run-to-run pillar (DESIGN.md §1,
    //  §3.F) with one of its two curves switched off.
    //
    //  WHY THIS AXIS AND NOT ANOTHER. Three shapes were weighed:
    //   (1) REPEATABLE PURCHASES THAT SCALE IN PRICE. Rejected: an unbounded ladder of paid stat
    //       upgrades is vertical progression, which §3.F names as the thing to prefer AGAINST
    //       ("a player on run 100 should have MORE OPTIONS, not be 10x stronger"). It also feeds
    //       the difficulty curve from behind — every purchase makes the next rung easier, which is
    //       the opposite of the stair-step §3.D asks for.
    //   (2) A SINK CONVERTING SALVAGE INTO RUN-SCOPED ADVANTAGE. Rejected as ALREADY BUILT: wave
    //       W9 (SIGNAL) shipped exactly this — the priced veteran recall (RecallCost), the draft
    //       pool re-roll, the scar rehab and the shop-slate re-roll. Salvage is therefore NOT
    //       worthless after the sixth unlock, and any claim that it is would be wrong. What is
    //       missing is not a place to SPEND; it is a place to PROGRESS.
    //   (3) UNLOCKS GATED ON HEAT REACHED. Chosen. It is the only one of the three whose DOMAIN is
    //       the difficulty curve's own domain: the thing that keeps climbing after the shop empties
    //       is the heat ladder, so gating on the ladder makes the two curves terminate together
    //       (the last commission opens on the rung that is itself the last climb). It is horizontal
    //       by construction, and it cannot be farmed at RECRUIT — Game.WinRun already refuses to
    //       advance the ceiling below heat 0.
    //
    //  WHAT THEY GRANT — WIDTH, NOT POWER. Each of the three widens one recurring CHOICE in the
    //  loop, and none of them adds a point of anything: you still take exactly one perk, still pay
    //  Intel for what you requisition, still recall at most two or three veterans. The honest
    //  residual is that a wider menu is a small edge by SELECTION (best-of-3 beats best-of-2), and
    //  it is bounded by exactly that — one pick either way. See docs/DEVLOG.md §THE SECOND AXIS.
    //
    //  Every read of these is !NoPersist-gated at the call site, so a SIGHTLINE_BALANCE batch, an
    //  autoplay run and a screenshot draw the identical RNG they drew before P18.
    // ══════════════════════════════════════════════════════════════════════════════════════════
    /// SIGHTLINE_SECONDAXIS=0 hides the heat-gated column and applies none of it (the pre-P18 WAR ROOM).
    public static bool SecondAxis = true;

    /// The heat-gated unlocks, in gate order. A member of AllUnlocks is heat-gated iff it is here.
    public static readonly MetaUnlock[] HeatUnlocks =
    {
        MetaUnlock.CombatTrials, MetaUnlock.DeepReserve, MetaUnlock.DeepStores,
    };

    /// The heat rung that must have been CLEARED (won a campaign at that level or above) before this
    /// unlock may be bought. -1 == not heat-gated. The gates span the ladder: 2 / 5 / 8, with the
    /// last landing on Heat.Max so the ladder's final climb is also the axis's final purchase.
    public static int UnlockHeatGate(MetaUnlock u) => u switch
    {
        MetaUnlock.CombatTrials => 2,
        MetaUnlock.DeepReserve  => 5,
        MetaUnlock.DeepStores   => 8,
        _ => -1,
    };

    /// Is this unlock gated on heat at all? (False for every pre-P18 unlock, and for all of them
    /// when the axis is switched off — a hidden unlock is never offered, so it can never be locked.)
    public static bool IsHeatGated(MetaUnlock u) => UnlockHeatGate(u) >= 0;

    /// The unlocks the WAR ROOM should list for a profile whose best CLEARED heat is `bestHeat`.
    /// With the axis off, exactly the pre-P18 six. With it on, all nine — a LOCKED row is the axis
    /// being visible: it is what tells the player the ladder still pays.
    public static IEnumerable<MetaUnlock> ListedUnlocks()
    {
        foreach (var u in AllUnlocks) if (SecondAxis || !IsHeatGated(u)) yield return u;
    }

    /// May `u` be PURCHASED at this best-cleared-heat? Cost is checked separately (a locked unlock
    /// is refused even by an infinitely rich profile — see Game.TryBuyUnlock).
    public static bool UnlockHeatMet(MetaUnlock u, int bestHeatWon)
        => !SecondAxis ? !IsHeatGated(u) : (!IsHeatGated(u) || bestHeatWon >= UnlockHeatGate(u));

    /// The reserve cap DEEP RESERVE buys (SaveGame.MaxVeterans is the ungated 12).
    public const int DeepReserveCap = 20;
    /// The extra requisition slot DEEP STORES buys (stacks with QUARTERMASTER's).
    public const int DeepStoresSlots = 1;

    public static string UnlockName(MetaUnlock u) => u switch
    {
        MetaUnlock.StartIntel => "SUPPLY LINE",
        MetaUnlock.StartBoon  => "STANDING ORDERS",
        MetaUnlock.StartArmor => "ISSUED PLATING",
        MetaUnlock.CrossTraining   => "CROSS-TRAINING",
        MetaUnlock.Quartermaster   => "QUARTERMASTER",
        MetaUnlock.StandingReserve => "STANDING RESERVE",
        MetaUnlock.CombatTrials    => "COMBAT TRIALS",
        MetaUnlock.DeepReserve     => "DEEP RESERVE",
        MetaUnlock.DeepStores      => "DEEP STORES",
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
        MetaUnlock.CombatTrials    => "Every perk offer puts THREE perks on the table, not two.",
        MetaUnlock.DeepReserve     => $"The veteran reserve keeps {DeepReserveCap} records instead of {SaveGame.MaxVeterans}.",
        MetaUnlock.DeepStores      => "The barracks requisition slate offers one more item.",
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
        MetaUnlock.CombatTrials    => 60,
        MetaUnlock.DeepReserve     => 95,
        MetaUnlock.DeepStores      => 150,
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
        new("STREAK5",   "DAWN PATROL",   "Win 5 dailies in a row."),
    };

    public static string AchievementName(string id)
    {
        foreach (var a in All) if (a.Id == id) return a.Name;
        return id;
    }
}
