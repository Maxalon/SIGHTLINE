using System;
using System.Numerics;

namespace Sightline;

public enum Team { Player, Enemy }

public enum WeaponKind { Rifle, Shotgun, Sniper, Lmg, Smg }

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

    public int ActionsLeft;
    public bool OnOverwatch;
    public bool Hunkered;
    public bool ReactedThisTurn; // overwatch fired this round
    public bool Alive = true;

    public bool Active = true;  // enemies start dormant until their pod is sighted
    public int PodId = -1;      // activation-pod grouping (enemies only)

    public bool IsVip;          // escort objective: the asset to extract (mission-only, never persists)

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

    public void BeginTurn()
    {
        ActionsLeft = 2;
        OnOverwatch = false;
        Hunkered = false;
        ReactedThisTurn = false;
    }
}
