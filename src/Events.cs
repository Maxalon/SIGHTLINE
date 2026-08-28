using System;
using System.Collections.Generic;

namespace Sightline;

// ============================================================================
//  BETWEEN-MISSION FIELD EVENTS ("?" nodes) — W4
//  Roguelike event beats on the branching campaign map. An Event node presents a
//  short situation + 2-3 choices; each choice's outcome mutates ALREADY-PERSISTED
//  Run/Unit state (Intel, Hp/Wound/Armor/BonusGrenades, WeaponMods, PendingPerks,
//  Squad, Traits, HeatLevel, ActiveBoons), so reload-after-event is correct with NO
//  new persisted field (the map regenerates from MapSeed; outcomes bake into Run state).
//
//  Pure data + a deterministic selector + an apply dispatcher — NO Raylib, NO mission
//  state, NO anims. Headlessly testable (EventCatalog.SelfTest).
// ============================================================================

public enum EventOutcomeKind
{
    Intel,            // delta Intel (Amount, may be negative)
    HealSoldier,      // heal most-wounded survivor by Amount (Amount<0 = full heal + wound cure)
    WoundSoldier,     // add a Wound (Amount missions) to a soldier
    GambleIntel,      // pay Cost intel; ChancePct to gain Amount, else lose Cost (seeded roll)
    GrantWeaponMod,   // install WeaponMod (Mod) on the soldier lacking it
    GrantBonusPerk,   // queue a pick-1-of-2 (Run.TryQueueBonusPerk)
    GrantGrenades,    // +Amount BonusGrenades on a soldier (cap +2)
    GrantArmor,       // +Amount Armor on a soldier (or squad-wide if SquadWide)
    Recruit,          // add a free recruit; if Veteran, pre-rank + a perk
    GrantTrait,       // add Trait (Tr) to a soldier
    AddHeat,          // +Amount HeatLevel (a curse/gamble cost), clamped
    GrantBoon,        // add a random un-owned Boon
    Nothing,          // decline / walk away (flavour only)
    // ---- FUL-10 FORKS (append at the END by house style; the kind is never persisted) ----
    GrantScar,        // add Scar Sc to the greenest soldier (ChancePct>0 = seeded risk); Vendetta brands the upcoming faction
    CureScar,         // remove the most-scarred soldier's NEWEST scar (BurnScarred's +MaxHp reverts)
    Salvage,          // +Amount META salvage, pended in Run.PendingSalvageReward and paid at run end
    GrantPrep,        // stage counter-prep vs the telegraphed upcoming faction (None -> report line only)
    RankKills,        // +Amount promotion-kill credit on the greenest soldier (barracks ranks it)
    ReleaseSoldier,   // remove the highest-rank soldier (a roster SLOT traded to the economy)
}

public struct EventOutcome
{
    public EventOutcomeKind Kind;
    public int Amount;        // delta / heal amount / chance payout
    public int Cost;          // intel paid up-front (GambleIntel) or cost for a costed reward
    public int ChancePct;     // 0..100 success chance (GambleIntel / FUL-10 seeded arms)
    public WeaponMod Mod;
    public Trait Tr;
    public bool SquadWide;
    public bool Veteran;
    public Scar Sc;           // FUL-10: scar granted by GrantScar (the Tr slot pattern)
    public bool OnFail;       // FUL-10: fire this outcome when the node's seeded roll FAILS (pairs
                              // with a ChancePct partner so exactly one of the two fires)
}

public struct EventChoice
{
    public string Label;       // button label, e.g. "Crack the cache"
    public string Preview;     // outcome preview sub-line
    public EventOutcome Outcome;
    public EventOutcome Outcome2;   // optional SECOND mutation applied with Outcome (trade-offs)
    public bool HasSecond;
    public EventOutcome Outcome3;   // FUL-10: optional THIRD mutation (triple-resource arms)
    public bool HasThird;
}

public class GameEvent
{
    public string Id;          // stable key, e.g. "cache"
    public string Title;       // "ABANDONED CACHE"
    public string Flavor;      // 1-2 sentences of situation
    public EventChoice[] Choices;   // 2-3
}

public static class EventCatalog
{
    // ---- builders ----
    static EventOutcome O(EventOutcomeKind k, int amount = 0, int cost = 0, int chance = 0,
                          WeaponMod mod = WeaponMod.Scope, Trait tr = Trait.Killer,
                          bool squadWide = false, bool veteran = false,
                          Scar sc = Scar.ShellShocked, bool onFail = false)
        => new EventOutcome { Kind = k, Amount = amount, Cost = cost, ChancePct = chance, Mod = mod, Tr = tr, SquadWide = squadWide, Veteran = veteran, Sc = sc, OnFail = onFail };

    static EventChoice C(string label, string preview, EventOutcome outcome)
        => new EventChoice { Label = label, Preview = preview, Outcome = outcome, HasSecond = false };

    static EventChoice C2(string label, string preview, EventOutcome outcome, EventOutcome second)
        => new EventChoice { Label = label, Preview = preview, Outcome = outcome, Outcome2 = second, HasSecond = true };

    // FUL-10: a triple-mutation arm (e.g. release a soldier + a salvage claim + intel).
    static EventChoice C3(string label, string preview, EventOutcome outcome, EventOutcome second, EventOutcome third)
        => new EventChoice { Label = label, Preview = preview, Outcome = outcome, Outcome2 = second, HasSecond = true, Outcome3 = third, HasThird = true };

    public static readonly GameEvent[] All =
    {
        new GameEvent
        {
            Id = "cache", Title = "ABANDONED CACHE",
            Flavor = "A sealed supply crate sits half-buried in the rubble. The lock looks fragile, but the crate could just as easily be mined.",
            Choices = new[]
            {
                C("Crack it open", "Install a SCOPE on a soldier (free)", O(EventOutcomeKind.GrantWeaponMod, mod: WeaponMod.Scope)),
                C("Strip it for intel", "+25 intel", O(EventOutcomeKind.Intel, 25)),
                C("Leave it (mined?)", "Walk away - nothing", O(EventOutcomeKind.Nothing)),
            },
        },
        new GameEvent
        {
            Id = "medic", Title = "WOUNDED MEDIC",
            Flavor = "A stranded field medic begs for evac. In exchange, they offer to patch up your worst-hurt soldier - or you could simply take their supplies.",
            Choices = new[]
            {
                C("Let them treat the squad", "Fully heal + cure your most-wounded soldier", O(EventOutcomeKind.HealSoldier, -1)),
                C("Take their supplies", "+20 intel (no heal)", O(EventOutcomeKind.Intel, 20)),
            },
        },
        new GameEvent
        {
            Id = "defector", Title = "DEFECTOR",
            Flavor = "An enemy operative wants out. They would fight for you - if you will take the risk of letting them in.",
            Choices = new[]
            {
                C("Recruit them", "Free VETERAN soldier (Rank 2 + a perk)", O(EventOutcomeKind.Recruit, veteran: true)),
                C("Demand intel for passage", "+15 intel", O(EventOutcomeKind.Intel, 15)),
                C("Turn them away", "Walk away - nothing", O(EventOutcomeKind.Nothing)),
            },
        },
        new GameEvent
        {
            Id = "blackmarket", Title = "BLACK MARKET",
            Flavor = "A fixer offers gear off the books, cheap - if your wallet can stomach it.",
            Choices = new[]
            {
                C2("Buy the mod (12 intel)", "-12 intel, install HOLLOW POINT", O(EventOutcomeKind.Intel, -12), O(EventOutcomeKind.GrantWeaponMod, mod: WeaponMod.HollowPoint)),
                C2("Buy a frag cache (8 intel)", "-8 intel, +1 grenade capacity", O(EventOutcomeKind.Intel, -8), O(EventOutcomeKind.GrantGrenades, 1)),
                C("Walk away", "Walk away - nothing", O(EventOutcomeKind.Nothing)),
            },
        },
        new GameEvent
        {
            Id = "drill", Title = "TRAINING DRILL",
            Flavor = "A veteran offers to drill a soldier hard overnight. They will come back sharper - but fatigued for the next fight.",
            Choices = new[]
            {
                C2("Run the drill", "A bonus perk, but a soldier is WOUNDED next mission", O(EventOutcomeKind.GrantBonusPerk), O(EventOutcomeKind.WoundSoldier, 1)),
                C("Light duty only", "+10 intel (sell the training time)", O(EventOutcomeKind.Intel, 10)),
            },
        },
        new GameEvent
        {
            Id = "distress", Title = "DISTRESS BEACON",
            Flavor = "Civilians are pinned down nearby. Diverting to help costs supplies, but the squad would carry the resolve forward.",
            Choices = new[]
            {
                C2("Mount a rescue", "-10 intel, a soldier earns VENGEFUL", O(EventOutcomeKind.Intel, -10), O(EventOutcomeKind.GrantTrait, tr: Trait.Vengeful)),
                C("Stay on mission", "+15 intel (report the coords for a bounty)", O(EventOutcomeKind.Intel, 15)),
            },
        },
        new GameEvent
        {
            Id = "profiteer", Title = "WAR PROFITEER",
            Flavor = "A broker will double your intel stake - if the dice favor you. House odds say he wins more than he loses.",
            Choices = new[]
            {
                C("Gamble half your intel", "55%: double your stake, else lose it", O(EventOutcomeKind.GambleIntel, chance: 55)),
                C("Decline", "Walk away - nothing", O(EventOutcomeKind.Nothing)),
            },
        },
        new GameEvent
        {
            Id = "relic", Title = "CURSED RELIC",
            Flavor = "An artifact hums with power. Claiming it would strengthen the squad - but it draws attention you may not want.",
            Choices = new[]
            {
                C2("Claim it", "Gain a BOON, but +1 Heat (tougher rest of run)", O(EventOutcomeKind.GrantBoon), O(EventOutcomeKind.AddHeat, 1)),
                C("Destroy it (safe)", "+12 intel", O(EventOutcomeKind.Intel, 12)),
            },
        },
        new GameEvent
        {
            Id = "depot", Title = "ARMS DEPOT",
            Flavor = "Crates of ablative plating and grenades, lightly guarded. Grab what you can carry.",
            Choices = new[]
            {
                C("Grab plating for the squad", "+1 armor, whole squad", O(EventOutcomeKind.GrantArmor, 1, squadWide: true)),
                C("Grab grenades", "+1 grenade capacity on two soldiers", O(EventOutcomeKind.GrantGrenades, 2)),
            },
        },
        new GameEvent
        {
            Id = "grave", Title = "OLD SOLDIER'S GRAVE",
            Flavor = "A fallen operative's effects lie scattered. Honoring them would steel the squad - or you could pawn the gear.",
            Choices = new[]
            {
                C("Take the dog tags", "A soldier earns IRON WILL (+2 max HP)", O(EventOutcomeKind.GrantTrait, tr: Trait.IronWill)),
                C("Pawn the gear", "+18 intel", O(EventOutcomeKind.Intel, 18)),
            },
        },
        // ---- FUL-10 FORKS: seven trade-off events wired into salvage/scar/veteran/faction ----
        // Ids and arm ORDER are FROZEN forever (Stats keys "id:arm"); append new arms at the end only.
        new GameEvent
        {
            Id = "warpension", Title = "OLD DEBTS",
            Flavor = "A discharged reserve veteran calls in a debt the outfit still owes. They will take payment - or a posting.",
            Choices = new[]
            {
                C2("Pay it", "-15 intel; +25 salvage at run end", O(EventOutcomeKind.Intel, -15), O(EventOutcomeKind.Salvage, 25)),
                C2("Press them back into service", "Greenest soldier +2 kill credit, but SHELL-SHOCKED",
                   O(EventOutcomeKind.RankKills, 2), O(EventOutcomeKind.GrantScar, sc: Scar.ShellShocked)),
                C("Refuse", "Walk away - nothing", O(EventOutcomeKind.Nothing)),
            },
        },
        new GameEvent
        {
            Id = "fieldhospital", Title = "FIELD HOSPITAL",
            Flavor = "A grey-market surgeon offers real work on old wounds - for intel, or for a body on the trial slab.",
            Choices = new[]
            {
                C2("Buy the surgery (20 intel)", "-20 intel, cure a soldier's newest scar", O(EventOutcomeKind.Intel, -20), O(EventOutcomeKind.CureScar)),
                C2("Volunteer for trials", "Fully heal your worst-hurt soldier; 40% a soldier is HARD-BITTEN",
                   O(EventOutcomeKind.HealSoldier, -1), O(EventOutcomeKind.GrantScar, chance: 40, sc: Scar.HardBitten)),
                C("Walk away", "Walk away - nothing", O(EventOutcomeKind.Nothing)),
            },
        },
        new GameEvent
        {
            Id = "informant", Title = "FACTION INFORMANT",
            Flavor = "A deserter sells the next force's doctrine. Buy it, sell THEM out, or let them vanish.",
            Choices = new[]
            {
                C2("Buy the dossier (12 intel)", "-12 intel, counter-prep the next known force", O(EventOutcomeKind.Intel, -12), O(EventOutcomeKind.GrantPrep)),
                C2("Turn them in", "+20 intel, but +1 Heat (the faction tightens up)", O(EventOutcomeKind.Intel, 20), O(EventOutcomeKind.AddHeat, 1)),
                C("Let them go", "Walk away - nothing", O(EventOutcomeKind.Nothing)),
            },
        },
        new GameEvent
        {
            Id = "quartermaster", Title = "CROOKED QUARTERMASTER",
            Flavor = "The requisition ledgers can be cooked, once. Somebody signs, and somebody carries the crates.",
            Choices = new[]
            {
                C2("Cook them", "+20 salvage at run end, but a soldier takes the fall (WOUNDED)",
                   O(EventOutcomeKind.Salvage, 20), O(EventOutcomeKind.WoundSoldier, 1)),
                C("Report the racket", "+14 intel", O(EventOutcomeKind.Intel, 14)),
                C("Skim the crates", "+1 grenade capacity on two soldiers", O(EventOutcomeKind.GrantGrenades, 2)),
            },
        },
        new GameEvent
        {
            Id = "bloodfeud", Title = "BLOOD FEUD",
            Flavor = "A soldier recognizes the outfit that nearly took them. The grudge wants a name on it.",
            Choices = new[]
            {
                C("Swear the feud", "A soldier takes a VENDETTA scar vs the force ahead", O(EventOutcomeKind.GrantScar, sc: Scar.Vendetta)),
                C("Counsel restraint", "Heal your worst-hurt soldier +3", O(EventOutcomeKind.HealSoldier, 3)),
                C2("Channel it", "A bonus perk, but a soldier is WOUNDED next mission",
                   O(EventOutcomeKind.GrantBonusPerk), O(EventOutcomeKind.WoundSoldier, 1)),
            },
        },
        new GameEvent
        {
            Id = "reservecall", Title = "THE RESERVE CALLS",
            Flavor = "HQ asks you to release a proven soldier to another cell. Refusal is noted; compliance is paid.",
            Choices = new[]
            {
                C3("Release them", "Your highest-rank soldier leaves; +30 salvage at run end, +10 intel",
                   O(EventOutcomeKind.ReleaseSoldier), O(EventOutcomeKind.Salvage, 30), O(EventOutcomeKind.Intel, 10)),
                C("Keep the roster", "+1 Heat - HQ notes the refusal", O(EventOutcomeKind.AddHeat, 1)),
            },
        },
        new GameEvent
        {
            Id = "warchest", Title = "SEALED WAR CHEST",
            Flavor = "A faction pay-chest, booby-trapped and singing. Force it, sell its location, or leave it humming.",
            Choices = new[]
            {
                C2("Force it", "55%: +35 salvage at run end; else a soldier is WOUNDED",
                   O(EventOutcomeKind.Salvage, 35, chance: 55), O(EventOutcomeKind.WoundSoldier, 1, chance: 55, onFail: true)),
                C("Sell the location", "+18 intel", O(EventOutcomeKind.Intel, 18)),
                C("Leave it", "Walk away - nothing", O(EventOutcomeKind.Nothing)),
            },
        },
    };

    // ---------------- deterministic, non-repeating selection (§3) ----------------
    // Stable across save/reload because it's a pure function of MapSeed + map structure
    // (both regenerated identically from MapSeed on load). NO persisted field.

    static int Hash(Run run, MissionNode node)
        => unchecked(run.MapSeed * 73856093 ^ (node.Id + 1) * 19349663);

    /// The catalog index for this event node, deduped across all Event nodes of the run so
    /// two events in one run never collide (linear probe from the seed-derived base index).
    public static int IndexForNode(Run run, MissionNode node)
    {
        int N = All.Length;
        if (N == 0) return 0;
        // Walk every Event node in Id order; assign each the next un-used base index. The
        // assignment is identical across regenerations because the map is deterministic.
        var used = new HashSet<int>();
        foreach (var n in run.Map)
        {
            if (n.Kind != NodeKind.Event) continue;
            int baseIdx = ((Hash(run, n) % N) + N) % N;
            int pick = baseIdx;
            for (int p = 0; p < N; p++) { int cand = (baseIdx + p) % N; if (!used.Contains(cand)) { pick = cand; break; } }
            used.Add(pick);
            if (n.Id == node.Id) return pick;
        }
        // node wasn't an Event node in the map (e.g. a synthesized debug node): fall back to base.
        return ((Hash(run, node) % N) + N) % N;
    }

    public static GameEvent ForNode(Run run, MissionNode node) => All[IndexForNode(run, node)];

    // ---------------- seeded gamble roll (stable across reload) ----------------
    // Derive from MapSeed + node id so the outcome is fixed the instant the node exists,
    // not a view-time Util.Roll. Returns true on "success".
    public static bool GambleSucceeds(Run run, MissionNode node, int chancePct)
    {
        if (node == null) return Util.RandInt(0, 99) < chancePct;   // synthetic/debug path
        int h = unchecked(run.MapSeed * 83492791 ^ (node.Id + 7) * 40503);
        int roll = ((h % 100) + 100) % 100;
        return roll < chancePct;
    }

    // ---------------- apply an outcome to persisted Run/Unit state (§4) ----------------
    // Pure mutation, no UI/anim/mission. `node` supplies the seeded gamble roll; may be null
    // (synthetic test path falls back to Util.RandInt). Returns a Report line.
    public static string Apply(Run run, EventOutcome o, MissionNode node = null)
    {
        switch (o.Kind)
        {
            case EventOutcomeKind.Intel:
            {
                run.Intel = Math.Max(0, run.Intel + o.Amount);
                return o.Amount >= 0 ? $"gained {o.Amount} intel" : $"spent {-o.Amount} intel";
            }
            case EventOutcomeKind.HealSoldier:
            {
                var u = MostWounded(run);
                if (u == null) return "no one to treat";
                if (o.Amount < 0) { u.Hp = u.MaxHp; u.Wound = 0; return $"{u.Name} fully healed"; }
                u.Hp = Math.Min(u.MaxHp, u.Hp + o.Amount);
                return $"{u.Name} healed +{o.Amount}";
            }
            case EventOutcomeKind.WoundSoldier:
            {
                // FUL-10: a ChancePct arm only wounds on the node's seeded roll (OnFail inverts, so
                // it can be the miss-half of a paired gamble). Existing arms pass 0 -> unchanged.
                if (!SeededFires(run, o, node)) return "everyone walks away unhurt";
                var u = LeastWounded(run);
                if (u == null) return "no one to wound";
                u.Wound = Math.Max(u.Wound, Math.Max(1, o.Amount));
                return $"{u.Name} fatigued (WOUND {u.Wound})";
            }
            case EventOutcomeKind.GambleIntel:
            {
                int stake = Math.Max(5, run.Intel / 2);
                if (run.Intel < stake) stake = run.Intel;   // can't stake more than you have
                if (stake <= 0) return "no intel to gamble";
                bool win = GambleSucceeds(run, node, o.ChancePct);
                if (win) { run.Intel += stake; return $"WON the gamble: +{stake} intel"; }
                run.Intel = Math.Max(0, run.Intel - stake);
                return $"LOST the gamble: -{stake} intel";
            }
            case EventOutcomeKind.GrantWeaponMod:
            {
                var u = SoldierLackingMod(run, o.Mod);
                if (u == null) return "no soldier could take the mod";
                u.InstallMod(o.Mod);
                return $"{u.Name} installed {ModName(o.Mod)}";
            }
            case EventOutcomeKind.GrantBonusPerk:
            {
                return run.TryQueueBonusPerk("FIELD EVENT") ? "a soldier earns a bonus perk" : "no soldier could train";
            }
            case EventOutcomeKind.GrantGrenades:
            {
                int targets = Math.Max(1, o.Amount);   // Amount==2 means "two soldiers, +1 each"
                int per = o.Amount >= 2 ? 1 : Math.Max(1, o.Amount);
                int n = o.Amount >= 2 ? 2 : 1;
                var done = new List<string>();
                for (int i = 0; i < n; i++)
                {
                    var u = SoldierLowestGrenades(run, done);
                    if (u == null) break;
                    u.BonusGrenades = Math.Min(2, u.BonusGrenades + per);
                    done.Add(u.Name);
                }
                return done.Count > 0 ? $"+grenades: {string.Join(", ", done)}" : "no soldier could take grenades";
            }
            case EventOutcomeKind.GrantArmor:
            {
                int amt = Math.Max(1, o.Amount);
                if (o.SquadWide)
                {
                    int cnt = 0;
                    foreach (var u in run.Squad) { u.Armor += amt; cnt++; }
                    return cnt > 0 ? $"squad +{amt} armor" : "no squad to armor";
                }
                var t = run.Squad.Count > 0 ? run.Squad[0] : null;
                if (t == null) return "no soldier to armor";
                t.Armor += amt;
                return $"{t.Name} +{amt} armor";
            }
            case EventOutcomeKind.Recruit:
            {
                if (run.Squad.Count >= Run.RosterMax) return "roster full - recruit turned away";
                // APEX W5: distinct callsign vs the current squad (dup names merge bond/memorial records)
                var rec = Sightline.Mission.MakeRecruit(run.TakenCallsigns());
                if (o.Veteran)
                {
                    rec.Rank = 2;
                    // a free perk: install the first perk the recruit doesn't have
                    foreach (var p in PerkDef.All) if (!rec.HasPerk(p)) { rec.Perks.Add(p); break; }
                }
                run.Squad.Add(rec);
                return $"{rec.Name} ({rec.Cls}) joins the squad" + (o.Veteran ? " as a veteran" : "");
            }
            case EventOutcomeKind.GrantTrait:
            {
                var u = SoldierLackingTrait(run, o.Tr);
                if (u == null) return "no soldier could take the trait";
                if (u.HasTrait(o.Tr)) return $"{u.Name} already steeled";
                u.Traits.Add(o.Tr);
                if (o.Tr == Trait.IronWill) { u.MaxHp += Unit.IronWillHp; u.Hp += Unit.IronWillHp; }
                return $"{u.Name} earned {TraitDef.Name(o.Tr)}";
            }
            case EventOutcomeKind.AddHeat:
            {
                int before = run.HeatLevel;
                run.HeatLevel = Heat.Clamp(run.HeatLevel + Math.Max(1, o.Amount));
                return run.HeatLevel > before ? $"Heat rises to {run.HeatLevel}" : "Heat already at ceiling";
            }
            case EventOutcomeKind.GrantBoon:
            {
                var avail = new List<Boon>();
                foreach (var b in BoonDef.All) if (!run.ActiveBoons.Contains(b)) avail.Add(b);
                if (avail.Count == 0) return "no new boon to claim";
                var pick = avail[Util.RandInt(0, avail.Count - 1)];
                run.ActiveBoons.Add(pick);
                return $"claimed the {BoonDef.Name(pick)} boon";
            }
            // ---- FUL-10 FORKS ----
            case EventOutcomeKind.GrantScar:
            {
                if (!SeededFires(run, o, node)) return "no lasting mark taken";
                var u = Greenest(run);
                if (u == null) return "no soldier to mark";
                // idempotent like Run.GrantScar: re-applies never double-add or double the HP grant
                if (u.HasScar(o.Sc)) return $"{u.Name} already bears {ScarDef.Name(o.Sc)}";
                u.Scars.Add(o.Sc);
                if (o.Sc == Scar.BurnScarred) { u.MaxHp += Unit.BurnScarHp; u.Hp += Unit.BurnScarHp; }
                if (o.Sc == Scar.Vendetta && u.VendettaFaction == Faction.None)
                {
                    var f = run.UpcomingFaction();
                    u.VendettaFaction = f != Faction.None ? f : Faction.Legion;   // fallback: the default force
                }
                return $"{u.Name} bears a scar: {ScarDef.Name(o.Sc)}";
            }
            case EventOutcomeKind.CureScar:
            {
                var u = MostScarred(run);
                if (u == null) return "no scars to treat";
                var s = u.Scars[u.Scars.Count - 1];   // NEWEST scar (the barracks REHAB takes the oldest)
                u.Scars.RemoveAt(u.Scars.Count - 1);
                // mirror the REHAB revert: BurnScarred's +MaxHp grant comes back off, Vendetta unbrands
                if (s == Scar.BurnScarred) { u.MaxHp = Math.Max(1, u.MaxHp - Unit.BurnScarHp); u.Hp = Math.Min(u.Hp, u.MaxHp); }
                if (s == Scar.Vendetta) u.VendettaFaction = Faction.None;
                return $"{u.Name} cured of {ScarDef.Name(s)}";
            }
            case EventOutcomeKind.Salvage:
            {
                // meta income NEVER writes meta/disk here (Apply stays pure) — it pends on the run
                // and Game.AwardMetaRunEnd commits it at run end, win or loss.
                if (!SeededFires(run, o, node)) return "the cache was a decoy - nothing gained";
                run.PendingSalvageReward += Math.Max(0, o.Amount);
                return $"+{Math.Max(0, o.Amount)} salvage claimed (paid at run end)";
            }
            case EventOutcomeKind.GrantPrep:
            {
                var f = run.UpcomingFaction();
                if (f == Faction.None) return "no faction telegraphed - the dossier is useless";
                run.PrepFaction = f;
                return $"counter-prep staged vs {Run.FactionName(f)}";
            }
            case EventOutcomeKind.RankKills:
            {
                var u = Greenest(run);
                if (u == null) return "no soldier to press";
                int amt = Math.Max(1, o.Amount);
                u.Kills += amt;
                return $"{u.Name} banks +{amt} kill credit";
            }
            case EventOutcomeKind.ReleaseSoldier:
            {
                if (run.Squad.Count <= 1) return "no one can be spared";   // ChoiceLegal gates too
                var u = HighestRank(run);
                run.Squad.Remove(u);
                return $"{u.Name} transferred to another cell ({u.RankName})";
            }
            default:
                return "walked away";
        }
    }

    // FUL-10 seeded-arm gate: ChancePct>0 outcomes fire only on the node's seeded roll (reload-
    // stable — the same GambleSucceeds hash GambleIntel uses, NO Util.Rng draw). OnFail inverts,
    // so a ChancePct pair on one arm fires exactly one of its two outcomes off the SAME roll.
    static bool SeededFires(Run run, EventOutcome o, MissionNode node)
        => o.ChancePct <= 0 || GambleSucceeds(run, node, o.ChancePct) != o.OnFail;

    // ---------------- target pickers ----------------
    static Unit MostWounded(Run run)
    {
        Unit best = null; int bestScore = int.MinValue;
        foreach (var u in run.Squad)
        {
            int score = (u.Wound > 0 ? 1000 + u.Wound * 100 : 0) + (u.MaxHp - u.Hp);
            if (score > bestScore) { bestScore = score; best = u; }
        }
        return best;
    }
    static Unit LeastWounded(Run run)
    {
        Unit best = null; int bestScore = int.MaxValue;
        foreach (var u in run.Squad)
        {
            int score = (u.Wound > 0 ? 1000 + u.Wound * 100 : 0) + (u.MaxHp - u.Hp);
            if (score < bestScore) { bestScore = score; best = u; }
        }
        return best;
    }
    static Unit SoldierLackingMod(Run run, WeaponMod m)
    {
        foreach (var u in run.Squad) if (!u.HasMod(m)) return u;
        return null;
    }
    static Unit SoldierLackingTrait(Run run, Trait t)
    {
        foreach (var u in run.Squad) if (!u.HasTrait(t)) return u;
        return run.Squad.Count > 0 ? run.Squad[0] : null;
    }
    // FUL-10: the greenest soldier — lowest Rank, squad-order tie-break. Rank never moves inside
    // an event resolution, so paired arms (RankKills + GrantScar) deterministically hit the SAME
    // soldier ("pressed back into service, marked by it").
    static Unit Greenest(Run run)
    {
        Unit best = null;
        foreach (var u in run.Squad) if (best == null || u.Rank < best.Rank) best = u;
        return best;
    }
    // FUL-10: the soldier carrying the most scars (squad-order tie-break); null when nobody is scarred.
    static Unit MostScarred(Run run)
    {
        Unit best = null;
        foreach (var u in run.Squad)
            if (u.Scars.Count > 0 && (best == null || u.Scars.Count > best.Scars.Count)) best = u;
        return best;
    }
    // FUL-10: the most-proven soldier — highest Rank, then kills, then squad order (deterministic).
    static Unit HighestRank(Run run)
    {
        Unit best = null;
        foreach (var u in run.Squad)
            if (best == null || u.Rank > best.Rank || (u.Rank == best.Rank && u.Kills > best.Kills)) best = u;
        return best;
    }

    static Unit SoldierLowestGrenades(Run run, List<string> exclude)
    {
        Unit best = null; int bestG = int.MaxValue;
        foreach (var u in run.Squad)
        {
            if (exclude.Contains(u.Name)) continue;
            if (u.BonusGrenades >= 2) continue;          // already capped
            if (u.BonusGrenades < bestG) { bestG = u.BonusGrenades; best = u; }
        }
        return best;
    }

    static string ModName(WeaponMod m) => m switch
    {
        WeaponMod.Scope => "SCOPE", WeaponMod.ExtendedMag => "EXT. MAG",
        WeaponMod.HollowPoint => "HOLLOW POINT", WeaponMod.Stabilizer => "STABILIZER", _ => "MOD",
    };

    // ============================================================================
    //  SELF-TEST (SIGHTLINE_EVENTTEST) — headless, no window.
    // ============================================================================
    public static string SelfTest()
    {
        var fails = new List<string>();

        // ---- 1. deterministic selection + non-repeat ----
        var r1 = new Run(); r1.GenerateMap(919191); r1.MapSeed = 919191;
        var r2 = new Run(); r2.GenerateMap(919191); r2.MapSeed = 919191;
        StampEventsForTest(r1); StampEventsForTest(r2);   // ensure at least one event present
        var ev1 = r1.Map.FindAll(n => n.Kind == NodeKind.Event);
        var ev2 = r2.Map.FindAll(n => n.Kind == NodeKind.Event);
        if (ev1.Count == 0) fails.Add("noEventNodes");
        if (ev1.Count != ev2.Count) fails.Add("eventCountUnstable");
        var seen = new HashSet<int>();
        for (int i = 0; i < ev1.Count && i < ev2.Count; i++)
        {
            if (ev1[i].Id != ev2[i].Id) fails.Add("eventIdUnstable");
            int idx1 = IndexForNode(r1, ev1[i]);
            int idx2 = IndexForNode(r2, ev2[i]);
            if (idx1 != idx2) fails.Add("selectionUnstable");
            if (!seen.Add(idx1)) fails.Add("eventRepeatedInRun");
        }

        // ---- 2. placement validity (real GenerateMap, no test stamping) ----
        var r3 = new Run(); r3.GenerateMap(424242); r3.MapSeed = 424242;
        var ev3 = r3.Map.FindAll(n => n.Kind == NodeKind.Event);
        if (ev3.Count > 2) fails.Add("tooManyEvents");
        foreach (var n in ev3)
            if (n.Col <= 0 || n.Col >= Run.MaxMissions - 1) fails.Add("eventOutOfMidColumn");
        // no column entirely events
        for (int c = 1; c < Run.MaxMissions - 1; c++)
        {
            var col = r3.Map.FindAll(n => n.Col == c);
            if (col.Count > 0 && col.TrueForAll(n => n.Kind == NodeKind.Event)) fails.Add("wholeColumnEvents");
        }
        // connectivity: every non-boss node leads onward (reachability fix-up invariant)
        foreach (var n in r3.Map)
            if (n.Kind != NodeKind.Boss && n.Next.Count == 0) fails.Add("deadEndNode");

        // ---- 3. outcome mutations ----
        var run = MakeTestRun();
        int intel0 = run.Intel;
        Apply(run, new EventOutcome { Kind = EventOutcomeKind.Intel, Amount = 25 });
        if (run.Intel != intel0 + 25) fails.Add("intelDelta");

        var hurt = run.Squad[0]; hurt.Hp = 1; hurt.Wound = 2;
        Apply(run, new EventOutcome { Kind = EventOutcomeKind.HealSoldier, Amount = -1 });
        if (hurt.Hp != hurt.MaxHp || hurt.Wound != 0) fails.Add("healFull");

        var modU = SoldierLackingMod(run, WeaponMod.Scope);
        Apply(run, new EventOutcome { Kind = EventOutcomeKind.GrantWeaponMod, Mod = WeaponMod.Scope });
        if (modU == null || !modU.HasMod(WeaponMod.Scope)) fails.Add("weaponMod");

        var grU = run.Squad[0]; grU.BonusGrenades = 0;
        Apply(run, new EventOutcome { Kind = EventOutcomeKind.GrantGrenades, Amount = 1 });
        if (run.Squad.TrueForAll(u => u.BonusGrenades == 0)) fails.Add("grenades");
        // grenade cap
        var capU = run.Squad[0]; capU.BonusGrenades = 2;
        Apply(run, new EventOutcome { Kind = EventOutcomeKind.GrantGrenades, Amount = 1 });
        foreach (var u in run.Squad) if (u.BonusGrenades > 2) fails.Add("grenadeCap");

        int sq0 = run.Squad.Count;
        Apply(run, new EventOutcome { Kind = EventOutcomeKind.Recruit, Veteran = true });
        if (run.Squad.Count != Math.Min(Run.RosterMax, sq0 + 1)) fails.Add("recruit");

        var trU = SoldierLackingTrait(run, Trait.Killer);
        Apply(run, new EventOutcome { Kind = EventOutcomeKind.GrantTrait, Tr = Trait.Killer });
        bool anyKiller = false; foreach (var u in run.Squad) if (u.HasTrait(Trait.Killer)) anyKiller = true;
        if (!anyKiller) fails.Add("trait");

        int heat0 = run.HeatLevel;
        Apply(run, new EventOutcome { Kind = EventOutcomeKind.AddHeat, Amount = 1 });
        if (run.HeatLevel != Heat.Clamp(heat0 + 1)) fails.Add("heat");
        run.HeatLevel = Heat.Max;
        Apply(run, new EventOutcome { Kind = EventOutcomeKind.AddHeat, Amount = 1 });
        if (run.HeatLevel != Heat.Max) fails.Add("heatClamp");

        int armorBefore = 0; foreach (var u in run.Squad) armorBefore += u.Armor;
        Apply(run, new EventOutcome { Kind = EventOutcomeKind.GrantArmor, Amount = 1, SquadWide = true });
        int armorAfter = 0; foreach (var u in run.Squad) armorAfter += u.Armor;
        if (armorAfter != armorBefore + run.Squad.Count) fails.Add("armorSquad");

        int boons0 = run.ActiveBoons.Count;
        Apply(run, new EventOutcome { Kind = EventOutcomeKind.GrantBoon });
        if (run.ActiveBoons.Count != boons0 + 1) fails.Add("boon");

        // GambleIntel deterministic branch (seeded from node)
        var gr = MakeTestRun(); gr.Intel = 40; gr.GenerateMap(7); gr.MapSeed = 7;
        StampEventsForTest(gr);
        var gnode = gr.Map.Find(n => n.Kind == NodeKind.Event);
        bool succ = GambleSucceeds(gr, gnode, 55);
        bool succ2 = GambleSucceeds(gr, gnode, 55);
        if (succ != succ2) fails.Add("gambleNotStable");
        Apply(gr, new EventOutcome { Kind = EventOutcomeKind.GambleIntel, ChancePct = 55 }, gnode);
        // 40 -> win +20 (60) or lose -20 (20)
        if (gr.Intel != 60 && gr.Intel != 20) fails.Add("gambleOutcome");

        // ---- 3b. FUL-10 outcome mutations (one leg per new kind) ----
        var fr = MakeTestRun();
        fr.Squad[0].Rank = 0; fr.Squad[1].Rank = 1; fr.Squad[2].Rank = 2; fr.Squad[3].Rank = 3;

        // RankKills + GrantScar land on the SAME greenest soldier (the warpension coupling)
        var green = fr.Squad[0];
        Apply(fr, new EventOutcome { Kind = EventOutcomeKind.RankKills, Amount = 2 });
        if (green.Kills != 2) fails.Add("rankKills");
        Apply(fr, new EventOutcome { Kind = EventOutcomeKind.GrantScar, Sc = Scar.ShellShocked });
        if (!green.HasScar(Scar.ShellShocked)) fails.Add("grantScar");
        Apply(fr, new EventOutcome { Kind = EventOutcomeKind.GrantScar, Sc = Scar.ShellShocked });
        if (green.Scars.Count != 1) fails.Add("grantScarDoubled");   // idempotent re-apply

        // GrantScar BurnScarred applies the +MaxHp grant exactly once; CureScar reverts it (newest-first)
        int hp0 = green.MaxHp;
        Apply(fr, new EventOutcome { Kind = EventOutcomeKind.GrantScar, Sc = Scar.BurnScarred });
        if (green.MaxHp != hp0 + Unit.BurnScarHp) fails.Add("scarBurnHp");
        Apply(fr, new EventOutcome { Kind = EventOutcomeKind.GrantScar, Sc = Scar.BurnScarred });
        if (green.MaxHp != hp0 + Unit.BurnScarHp) fails.Add("scarBurnHpDoubled");
        Apply(fr, new EventOutcome { Kind = EventOutcomeKind.CureScar });
        if (green.HasScar(Scar.BurnScarred)) fails.Add("cureScarNewest");   // BurnScarred was newest
        if (!green.HasScar(Scar.ShellShocked)) fails.Add("cureScarTookOldest");
        if (green.MaxHp != hp0) fails.Add("cureScarHpRevert");
        if (MostScarred(fr) != green) fails.Add("mostScarredPick");

        // GrantScar Vendetta brands the upcoming faction (no map here -> the Legion fallback)
        Apply(fr, new EventOutcome { Kind = EventOutcomeKind.GrantScar, Sc = Scar.Vendetta });
        if (!green.HasScar(Scar.Vendetta) || green.VendettaFaction != Faction.Legion) fails.Add("scarVendetta");

        // Salvage pends on the run (meta is NEVER touched here); GrantPrep degrades gracefully mapless
        int psr0 = fr.PendingSalvageReward;
        Apply(fr, new EventOutcome { Kind = EventOutcomeKind.Salvage, Amount = 25 });
        if (fr.PendingSalvageReward != psr0 + 25) fails.Add("salvagePend");
        Apply(fr, new EventOutcome { Kind = EventOutcomeKind.GrantPrep });
        if (fr.PrepFaction != Faction.None) fails.Add("prepMapless");

        // GrantPrep sets PrepFaction when a next node telegraphs a faction
        var pr = MakeTestRun(); pr.GenerateMap(31337); pr.MapSeed = 31337; pr.MapPos = 0;
        foreach (var nn in pr.NextNodes()) nn.Faction = Faction.Wardens;
        Apply(pr, new EventOutcome { Kind = EventOutcomeKind.GrantPrep });
        if (pr.PrepFaction != Faction.Wardens) fails.Add("prepSet");

        // ReleaseSoldier removes exactly the highest-rank soldier; a lone soldier is never released
        int sqN = fr.Squad.Count;
        var top = fr.Squad[3];   // Rank 3
        Apply(fr, new EventOutcome { Kind = EventOutcomeKind.ReleaseSoldier });
        if (fr.Squad.Count != sqN - 1 || fr.Squad.Contains(top)) fails.Add("release");
        var lone = new Run { Intel = 0 };
        lone.Squad.Add(Sightline.Mission.MakeRecruit());
        Apply(lone, new EventOutcome { Kind = EventOutcomeKind.ReleaseSoldier });
        if (lone.Squad.Count != 1) fails.Add("releaseLone");

        // seeded arms are reload-stable: double-apply of a ChancePct GrantScar mutates identically,
        // and a Salvage/WoundSoldier OnFail pair fires EXACTLY one of its outcomes off the one roll
        var sg = MakeTestRun(); sg.GenerateMap(77); sg.MapSeed = 77;
        StampEventsForTest(sg);
        var snode = sg.Map.Find(n => n.Kind == NodeKind.Event);
        string s1 = Apply(sg, new EventOutcome { Kind = EventOutcomeKind.GrantScar, Sc = Scar.HardBitten, ChancePct = 40 }, snode);
        string s2 = Apply(sg, new EventOutcome { Kind = EventOutcomeKind.GrantScar, Sc = Scar.HardBitten, ChancePct = 40 }, snode);
        bool landed = false; foreach (var u in sg.Squad) if (u.HasScar(Scar.HardBitten)) landed = true;
        if (landed != GambleSucceeds(sg, snode, 40)) fails.Add("seededScarRoll");
        if (landed && !s1.Contains("bears a scar")) fails.Add("seededScarFirstApply");
        if (landed && !s2.Contains("already bears")) fails.Add("seededScarDoubleApply");   // 2nd apply must be the idempotent path
        int wound0 = 0; foreach (var u in sg.Squad) wound0 += u.Wound;
        int pend0 = sg.PendingSalvageReward;
        Apply(sg, new EventOutcome { Kind = EventOutcomeKind.Salvage, Amount = 35, ChancePct = 55 }, snode);
        Apply(sg, new EventOutcome { Kind = EventOutcomeKind.WoundSoldier, Amount = 1, ChancePct = 55, OnFail = true }, snode);
        int wound1 = 0; foreach (var u in sg.Squad) wound1 += u.Wound;
        bool pairPaid = sg.PendingSalvageReward == pend0 + 35, pairHurt = wound1 > wound0;
        if (pairPaid == pairHurt) fails.Add("gamblePairExclusive");   // exactly one side of the pair fires

        // ---- 4. save round-trip after an event ----
        var sr = MakeTestRun();
        sr.GenerateMap(555); sr.MapSeed = 555;
        StampEventsForTest(sr);
        var evNode = sr.Map.Find(n => n.Kind == NodeKind.Event);
        sr.MapPos = evNode.Id; evNode.Visited = true;     // advance position == resolved-once
        Apply(sr, new EventOutcome { Kind = EventOutcomeKind.Intel, Amount = 30 });
        Apply(sr, new EventOutcome { Kind = EventOutcomeKind.GrantWeaponMod, Mod = WeaponMod.HollowPoint });
        Apply(sr, new EventOutcome { Kind = EventOutcomeKind.Recruit, Veteran = true });
        Apply(sr, new EventOutcome { Kind = EventOutcomeKind.Salvage, Amount = 25 });   // FUL-10: the pending claim must survive the trip
        int postIntel = sr.Intel, postSquad = sr.Squad.Count;
        bool hadMod = false; foreach (var u in sr.Squad) if (u.HasMod(WeaponMod.HollowPoint)) hadMod = true;

        var preserved = SaveGame.Exists ? SaveGame.Load() : null;   // snapshot any real save
        try
        {
            SaveGame.Save(sr);
            var got = SaveGame.Load();
            if (got == null) fails.Add("loadNull");
            else
            {
                if (got.Intel != postIntel) fails.Add("postIntel");
                if (got.Squad.Count != postSquad) fails.Add("postSquad");
                if (got.PendingSalvageReward != 25) fails.Add("postSalvagePend");   // FUL-10 round-trip
                bool gotMod = false; foreach (var u in got.Squad) if (u.HasMod(WeaponMod.HollowPoint)) gotMod = true;
                if (hadMod && !gotMod) fails.Add("postMod");
                // MapPos must point AT the event node (visited) so reload never re-offers it
                if (got.MapPos != evNode.Id) fails.Add("postMapPos");
                if (got.CurrentNode == null || got.CurrentNode.Kind != NodeKind.Event) fails.Add("postNodeKind");
                // and the event node is CURRENT, never a NextNode (cannot re-trigger)
                foreach (var nn in got.NextNodes()) if (nn.Id == evNode.Id) fails.Add("eventReoffered");
            }
        }
        finally
        {
            if (preserved != null) SaveGame.Save(preserved);   // restore the player's real save
            else SaveGame.Delete();
        }

        return fails.Count == 0 ? "EVENTTEST: PASS" : "EVENTTEST: FAIL (" + string.Join(", ", fails) + ")";
    }

    static Run MakeTestRun()
    {
        var run = new Run { Intel = 20 };
        for (int i = 0; i < 4; i++)
        {
            var u = Sightline.Mission.MakeRecruit();
            u.Name = "S" + i;   // distinct names so the grenade/recruit pickers don't collide
            run.Squad.Add(u);
        }
        return run;
    }

    // For the determinism/save tests: force at least one Event node onto a fresh map without
    // depending on GenerateMap's probabilistic stamping (which may emit 0 on some seeds).
    static void StampEventsForTest(Run run)
    {
        if (run.Map.Exists(n => n.Kind == NodeKind.Event)) return;
        var n = run.Map.Find(x => x.Kind == NodeKind.Combat && x.Col > 0 && x.Col < Run.MaxMissions - 1);
        if (n != null) n.Kind = NodeKind.Event;
    }
}
