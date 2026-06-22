using System;
using System.Numerics;

namespace Sightline;

public enum Team { Player, Enemy }

public enum WeaponKind { Rifle, Shotgun, Sniper, Lmg, Smg }

/// Per-class signature ability (self-cast, one charge per mission).
public enum AbilityKind { None, RunGun, Blitz, Steady, Suppress }

/// Utility-item slot (3.4): a second throwable beyond grenades, assigned by class.
public enum ItemKind { None, Smoke, Flash, Barricade }

/// Promotion perks: a soldier picks one each rank-up (see Run / barracks).
public enum Perk { LockOn, Hardened, Reflexes, Bandolier, CloseQuarters, Marksman, Deadeye, Tank, Sprinter, Adrenal }

/// Battlefield traits earned by FEATS (see Game feat hooks + Run.DebriefSurvivors).
/// Each is a small passive read in Combat.ComputeOdds, so veterans matter.
public enum Trait { Killer, ColdBlood, IronWill, Vengeful }

/// Transient combat status effects (per-mission, never persisted). Burning/Bleed are
/// damage-over-time, Stun costs an action, Disoriented dulls aim + denies overwatch.
public enum StatusKind { Burning, Bleed, Stun, Disoriented }

public class Status { public StatusKind Kind; public int Turns; }

/// Awareness tier for activation pods (4.3). Enemies escalate gradually rather than
/// flipping awake instantly, so first contact is telegraphed (never a turn-1 gotcha):
///   Unaware   - hasn't noticed the squad; dormant, doesn't act ("?")
///   Suspicious- spotted at range this turn; alerted but not yet engaging ("!"); it
///               confirms (-> Alert) if still in sight at the player's turn end, or
///               loses interest (-> Unaware) if the squad breaks line of sight.
///   Alert     - fully awake; acts, shoots, and is a live threat (the old "Active").
public enum AlertLevel { Unaware, Suspicious, Alert }

public class Weapon
{
    public string Name;
    public WeaponKind Kind;
    public int DmgMin, DmgMax;
    public int AimBonus;
    public int CritBase;
    public int Clip;

    /// Maximum effective firing range in tiles.
    public int MaxRange => Kind switch
    {
        WeaponKind.Shotgun => 8,
        WeaponKind.Smg => 10,
        WeaponKind.Sniper => 20,
        WeaponKind.Lmg => 13,
        _ => 15,
    };

    /// Aim modifier from range (in tiles). Each weapon has its own profile.
    public int RangeMod(float dist)
    {
        switch (Kind)
        {
            case WeaponKind.Shotgun: // brutal up close, useless at range
                return (int)Util.Clamp((5 - dist) * 8, -45, 30);
            case WeaponKind.Sniper:  // rewards distance, punished point-blank
                return (int)Util.Clamp((dist - 3) * 3, -15, 18);
            case WeaponKind.Smg:     // slight close-range edge
                return (int)Util.Clamp((7 - dist) * 2, -12, 12);
            case WeaponKind.Lmg:     // flat, mild long-range falloff
                return (int)Util.Clamp(-(dist - 10) * 2, -16, 4);
            default:                 // rifle: balanced, gentle falloff
                return (int)Util.Clamp((8 - dist) * 1.5f, -18, 10);
        }
    }

    public static Weapon Make(WeaponKind k) => k switch
    {
        WeaponKind.Rifle   => new Weapon { Name = "Rifle",   Kind = k, DmgMin = 3, DmgMax = 5, AimBonus = 0,  CritBase = 10, Clip = 4 },
        WeaponKind.Shotgun => new Weapon { Name = "Shotgun", Kind = k, DmgMin = 4, DmgMax = 7, AimBonus = 0,  CritBase = 15, Clip = 2 },
        WeaponKind.Sniper  => new Weapon { Name = "Marksman",Kind = k, DmgMin = 5, DmgMax = 8, AimBonus = 5,  CritBase = 20, Clip = 3 },
        WeaponKind.Lmg     => new Weapon { Name = "LMG",     Kind = k, DmgMin = 3, DmgMax = 6, AimBonus = -5, CritBase = 5,  Clip = 5 },
        WeaponKind.Smg     => new Weapon { Name = "SMG",     Kind = k, DmgMin = 2, DmgMax = 4, AimBonus = 0,  CritBase = 10, Clip = 4 },
        _ => new Weapon { Name = "Rifle", Kind = WeaponKind.Rifle, DmgMin = 3, DmgMax = 5, Clip = 4 },
    };
}

public class Unit
{
    public string Name;
    public string Cls;          // class label, e.g. "ASSAULT"
    public Team Team;
    public int X, Y;            // tile position
    public int Hp, MaxHp;
    public int Aim;
    public int Mobility;        // tiles per single move action
    public Weapon Weapon;
    public int Ammo;
    public int Grenades;        // thrown AoE charges (refilled each mission)
    public int BonusGrenades;   // permanent extra grenade capacity (FRAG CACHE purchase)

    // class signature ability (see AbilityKind); charge refilled each mission
    public int AbilityCharge;
    public bool RunGun;         // assault: next shot costs 1 action, doesn't end the turn
    public bool Blitz;          // ranger: next move costs one action less
    public bool Steady;         // sharpshooter: next shot gets +aim/+crit
    public int  Suppress;       // gunner debuff currently ON this unit (aim penalty)

    // optional player-authored role label (overrides the auto strength tags in the
    // roster/dossier when set); persists across the run
    public string CustomTag;

    // promotion perks (persist across the run); pick one per rank-up
    public System.Collections.Generic.List<Perk> Perks = new();
    public bool HasPerk(Perk p) => Perks.Contains(p);

    // ---- soldier identity (3.2): nickname + earned traits + bonds, all persist ----
    public string Nickname;     // earned with the first feat; shown as NAME "NICK"
    public System.Collections.Generic.List<Trait> Traits = new();
    public bool HasTrait(Trait t) => Traits.Contains(t);
    public System.Collections.Generic.List<string> Bonds = new();  // names of bonded squadmates

    // a name with the earned nickname folded in, e.g. VEGA "REAPER"
    public string FullName => string.IsNullOrEmpty(Nickname) ? Name : $"{Name} \"{Nickname}\"";

    // ---- combat status effects (3.5); per-mission, cleared in Game.SetupMission ----
    public System.Collections.Generic.List<Status> Statuses = new();
    public bool HasStatus(StatusKind k)
    {
        foreach (var s in Statuses) if (s.Kind == k && s.Turns > 0) return true;
        return false;
    }
    /// Apply a status, or refresh it to the longer of the two durations.
    public void AddStatus(StatusKind k, int turns)
    {
        foreach (var s in Statuses) if (s.Kind == k) { s.Turns = Math.Max(s.Turns, turns); return; }
        Statuses.Add(new Status { Kind = k, Turns = turns });
    }

    // transient per-mission feat tracking (reset in Game.SetupMission; never persisted)
    public bool FeatMultiKill;  // 2+ kills in a single turn this mission
    public bool FeatClutch;     // a kill while bloodied (<= 1/4 HP)
    public bool FeatVengeful;   // a kill after a squadmate fell this mission
    public bool WasNearDeath;   // dropped to <= 1/4 HP at some point this mission (survived = feat)
    public bool AllyDown;       // a squadmate has been killed this mission
    public int KillsThisTurn;   // reset each BeginTurn (multi-kill detection)
    public bool BondAura;       // a bonded squadmate is adjacent (refreshed each frame by Game)
    public bool FiredFromConcealment; // true for ONE shot after breaking concealment (4.4)

    public AbilityKind Ability => AbilityKindFor(Cls);
    public string AbilityName => Ability switch
    {
        AbilityKind.RunGun  => "RUN&GUN",
        AbilityKind.Blitz   => "BLITZ",
        AbilityKind.Steady  => "STEADY",
        AbilityKind.Suppress=> "SUPPRESS",
        _ => "ABILITY",
    };
    public string AbilityDesc => Ability switch
    {
        AbilityKind.RunGun   => "Next shot costs 1 action (won't end your turn)",
        AbilityKind.Blitz    => "Next move costs one action less",
        AbilityKind.Steady   => "Next shot: +25 aim, +20 crit",
        AbilityKind.Suppress => "Pin the nearest foe: -30 aim + overwatch it",
        _ => "",
    };
    public static AbilityKind AbilityKindFor(string cls) => cls switch
    {
        "ASSAULT"      => AbilityKind.RunGun,
        "RANGER"       => AbilityKind.Blitz,
        "SHARPSHOOTER" => AbilityKind.Steady,
        "GUNNER"       => AbilityKind.Suppress,
        _ => AbilityKind.None,
    };

    // ---- utility item (3.4): a second throwable slot, 1 charge/mission, by class ----
    public int ItemCharge;                       // remaining uses this mission (refilled in Mission.Build)
    public ItemKind Item => ItemKindFor(Cls);    // derived from class (never persisted)
    public string ItemName => Item switch
    {
        ItemKind.Smoke     => "SMOKE",
        ItemKind.Flash     => "FLASH",
        ItemKind.Barricade => "BARRICADE",
        _ => "ITEM",
    };
    public string ItemDesc => Item switch
    {
        ItemKind.Smoke     => "Lob a smoke cloud: blocks line of sight + overwatch through it for a few turns",
        ItemKind.Flash     => "Lob a flashbang: disorients everyone in the blast (-aim, no overwatch next turn)",
        ItemKind.Barricade => "Deploy a low-cover barricade on an empty tile",
        _ => "",
    };
    public static ItemKind ItemKindFor(string cls) => cls switch
    {
        "ASSAULT"      => ItemKind.Flash,      // breacher: blind the room
        "RANGER"       => ItemKind.Smoke,      // flanker: cover the approach
        "SHARPSHOOTER" => ItemKind.Smoke,      // marksman: break enemy sightlines
        "GUNNER"       => ItemKind.Barricade,  // nest-builder: drop cover
        _ => ItemKind.None,
    };

    public int ActionsLeft;
    public bool OnOverwatch;
    public bool Hunkered;
    public bool ReactedThisTurn; // overwatch fired this round
    public bool Alive = true;

    // Awareness tier (4.3): enemies escalate Unaware -> Suspicious -> Alert instead of
    // waking instantly. Active (acts in combat / is a live threat) == fully Alert, so the
    // many read sites that gate on "is this enemy awake" keep working unchanged.
    public AlertLevel Alert = AlertLevel.Alert;
    public bool Active => Alert == AlertLevel.Alert;
    public int PodId = -1;      // activation-pod grouping (enemies only)

    public bool IsVip;          // escort objective: the asset to extract (mission-only, never persists)
    public bool Enraged;        // elite boss: one-time low-HP rage trigger
    public int ShieldDx, ShieldDy;  // SHIELD archetype: facing dir its frontal shield blocks (3.7)

    // meta / campaign progression (persists across missions)
    public int Kills;
    public int Rank;            // index into Run.Ranks
    public string RankName => Run.Ranks[Util.Clamp(Rank, 0, Run.Ranks.Length - 1)];

    // attrition: missions a battle wound lingers (>0 = −Aim/−Mobility); decays per
    // mission in Run.DebriefSurvivors, cleared by a FIELD MEDKIT.
    public int Wound;

    // render state
    public Vector2 Pos;         // pixel-space centre (tweened)
    public Vector2 Recoil;      // transient recoil/knockback offset (decays)
    public float Facing;        // radians, for the facing tick
    public float Flash;         // 0..1 damage flash
    public float Bob;           // idle bob phase

    public int MoveBudget => Math.Max(1, Mobility - (Wound > 0 ? WoundMob : 0)) * 2;  // half-tile budget (−mob while wounded)
    public bool CanAct => Alive && ActionsLeft > 0;

    public Unit()
    {
        Bob = Util.RandF() * MathF.PI * 2f;
    }

    public void SyncPos()
    {
        Pos = Util.TileCenter(X, Y);
    }

    // perk magnitudes (kept here so Combat/Mission/Hud read one source)
    public const int PerkAim = 15;       // LockOn / CloseQuarters / Marksman
    public const int PerkCrit = 15;      // Deadeye
    public const int CloseRange = 4;     // CloseQuarters threshold (tiles)
    public const int LongRange = 7;      // Marksman threshold (tiles)
    public const int WoundAim = 12;      // aim penalty while Wound > 0
    public const int WoundMob = 1;       // mobility penalty while Wound > 0

    // trait + bond magnitudes (read in Combat.ComputeOdds; one source of truth)
    public const int KillerAim = 12;     // Killer: +aim vs targets already below half HP
    public const int ColdBloodCrit = 15; // ColdBlood: +crit while bloodied (self <= half HP)
    public const int VengefulAim = 12;   // Vengeful: +aim while a squadmate has fallen this mission
    public const int IronWillHp = 2;     // IronWill: permanent +max HP (granted at debrief)
    public const int BondAim = 10;       // Bond: +aim while a bonded squadmate is adjacent

    // status-effect magnitudes (3.5)
    public const int BurnDamage = 2;     // Burning: HP lost at the unit's turn start
    public const int BleedDamage = 1;    // Bleed: HP lost per tile moved
    public const int DisorientAim = 15;  // Disoriented: aim penalty (+ no overwatch)

    public void BeginTurn()
    {
        ActionsLeft = 2;
        OnOverwatch = false;
        Hunkered = false;
        ReactedThisTurn = false;
        RunGun = false;            // ability stances don't carry between turns
        Blitz = false;
        Steady = false;
        KillsThisTurn = 0;         // multi-kill feat is per-turn
        FiredFromConcealment = false; // ambush bonus is for one shot only (4.4)
        // note: Suppress (a debuff applied by an enemy gunner) is cleared on the
        // victim's owner's next turn, NOT here, so it bites during the turn it's set.
    }
}

/// Names + one-line descriptions for promotion perks, and the perk pool.
public static class PerkDef
{
    public static readonly Perk[] All =
    {
        Perk.LockOn, Perk.Hardened, Perk.Reflexes, Perk.Bandolier, Perk.CloseQuarters,
        Perk.Marksman, Perk.Deadeye, Perk.Tank, Perk.Sprinter, Perk.Adrenal,
    };

    public static string Name(Perk p) => p switch
    {
        Perk.LockOn => "LOCK-ON",
        Perk.Hardened => "HARDENED",
        Perk.Reflexes => "REFLEXES",
        Perk.Bandolier => "BANDOLIER",
        Perk.CloseQuarters => "CLOSE QUARTERS",
        Perk.Marksman => "MARKSMAN",
        Perk.Deadeye => "DEADEYE",
        Perk.Tank => "TANK",
        Perk.Sprinter => "SPRINTER",
        Perk.Adrenal => "ADRENAL",
        _ => "PERK",
    };

    public static string Code(Perk p) => p switch
    {
        Perk.LockOn => "LCK",
        Perk.Hardened => "HRD",
        Perk.Reflexes => "RFX",
        Perk.Bandolier => "BND",
        Perk.CloseQuarters => "CQB",
        Perk.Marksman => "MRK",
        Perk.Deadeye => "DDE",
        Perk.Tank => "TNK",
        Perk.Sprinter => "SPR",
        Perk.Adrenal => "ADR",
        _ => "?",
    };

    public static string Desc(Perk p) => p switch
    {
        Perk.LockOn => "+15 aim vs exposed targets",
        Perk.Hardened => "-1 damage taken",
        Perk.Reflexes => "overwatch shots rarely miss",
        Perk.Bandolier => "+1 grenade each mission",
        Perk.CloseQuarters => "+15 aim within 4 tiles",
        Perk.Marksman => "+15 aim beyond 7 tiles",
        Perk.Deadeye => "+15 crit chance",
        Perk.Tank => "+3 max HP",
        Perk.Sprinter => "+1 mobility",
        Perk.Adrenal => "+1 ability charge each mission",
        _ => "",
    };
}

/// Names + descriptions for earned traits, and the feat that grants each.
public static class TraitDef
{
    public static string Name(Trait t) => t switch
    {
        Trait.Killer    => "KILLER INSTINCT",
        Trait.ColdBlood => "COLD BLOOD",
        Trait.IronWill  => "IRON WILL",
        Trait.Vengeful  => "VENGEFUL",
        _ => "TRAIT",
    };

    public static string Code(Trait t) => t switch
    {
        Trait.Killer    => "KIL",
        Trait.ColdBlood => "CLD",
        Trait.IronWill  => "IRN",
        Trait.Vengeful  => "VNG",
        _ => "?",
    };

    public static string Desc(Trait t) => t switch
    {
        Trait.Killer    => "+12 aim vs wounded targets",
        Trait.ColdBlood => "+15 crit while bloodied",
        Trait.IronWill  => "+2 max HP (toughened)",
        Trait.Vengeful  => "+12 aim after a squadmate falls",
        _ => "",
    };

    // short note describing the feat that earns the trait (barracks report)
    public static string Feat(Trait t) => t switch
    {
        Trait.Killer    => "a multi-kill turn",
        Trait.ColdBlood => "a clutch kill while bloodied",
        Trait.IronWill  => "surviving near death",
        Trait.Vengeful  => "avenging a fallen squadmate",
        _ => "",
    };
}

/// Display metadata for combat status effects (short code + label).
public static class StatusDef
{
    public static string Code(StatusKind k) => k switch
    {
        StatusKind.Burning => "BRN",
        StatusKind.Bleed => "BLD",
        StatusKind.Stun => "STN",
        StatusKind.Disoriented => "DAZ",
        _ => "?",
    };

    public static string Name(StatusKind k) => k switch
    {
        StatusKind.Burning => "BURNING",
        StatusKind.Bleed => "BLEEDING",
        StatusKind.Stun => "STUNNED",
        StatusKind.Disoriented => "DISORIENTED",
        _ => "",
    };
}

/// A pool of earned nicknames, assigned with a soldier's first feat.
public static class Nicknames
{
    public static readonly string[] Pool =
    {
        "REAPER", "GHOST", "MAVERICK", "DOC", "ACE", "VIPER", "BULLDOG", "HAWKEYE",
        "SHADE", "IRON", "BLAZE", "NOMAD", "WIDOW", "TITAN", "SAINT", "FANG",
    };
}
