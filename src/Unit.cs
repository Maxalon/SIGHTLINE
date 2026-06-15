using System;
using System.Numerics;

namespace Sightline;

public enum Team { Player, Enemy }

public enum WeaponKind { Rifle, Shotgun, Sniper, Lmg, Smg }

/// Per-class signature ability (self-cast, one charge per mission).
public enum AbilityKind { None, RunGun, Blitz, Steady, Suppress }

/// Promotion perks: a soldier picks one each rank-up (see Run / barracks).
public enum Perk { LockOn, Hardened, Reflexes, Bandolier, CloseQuarters, Marksman, Deadeye, Tank, Sprinter, Adrenal }

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

    public int ActionsLeft;
    public bool OnOverwatch;
    public bool Hunkered;
    public bool ReactedThisTurn; // overwatch fired this round
    public bool Alive = true;

    public bool Active = true;  // enemies start dormant until their pod is sighted
    public int PodId = -1;      // activation-pod grouping (enemies only)

    public bool IsVip;          // escort objective: the asset to extract (mission-only, never persists)
    public bool Enraged;        // elite boss: one-time low-HP rage trigger

    // meta / campaign progression (persists across missions)
    public int Kills;
    public int Rank;            // index into Run.Ranks
    public string RankName => Run.Ranks[Util.Clamp(Rank, 0, Run.Ranks.Length - 1)];

    // render state
    public Vector2 Pos;         // pixel-space centre (tweened)
    public Vector2 Recoil;      // transient recoil/knockback offset (decays)
    public float Facing;        // radians, for the facing tick
    public float Flash;         // 0..1 damage flash
    public float Bob;           // idle bob phase

    public int MoveBudget => Mobility * 2;     // dijkstra half-tile budget for 1 action
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

    public void BeginTurn()
    {
        ActionsLeft = 2;
        OnOverwatch = false;
        Hunkered = false;
        ReactedThisTurn = false;
        RunGun = false;            // ability stances don't carry between turns
        Blitz = false;
        Steady = false;
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
