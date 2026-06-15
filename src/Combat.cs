using System;

namespace Sightline;

/// Precomputed odds for a shot from attacker -> defender.
public struct ShotOdds
{
    public int HitChance;   // 0..100
    public int CritChance;  // 0..100
    public int DmgMin, DmgMax;
    public int CoverLevel;  // 0/1/2
    public bool Flanked;
    public bool Hunkered;
    public bool HighGround;  // attacker fires from raised terrain onto a lower foe
    public bool Steady;      // attacker braced (sharpshooter ability) this shot
}

/// The resolved outcome of a shot.
public struct ShotResult
{
    public bool Hit;
    public bool Crit;
    public int Damage;
    public ShotOdds Odds;
}

public static class Combat
{
    // High-ground bonus: firing from raised terrain onto a lower target.
    public const int HighGroundAim = 15;
    public const int HighGroundCrit = 10;

    // Sharpshooter "Steady" ability: a braced shot.
    public const int SteadyAim = 25;
    public const int SteadyCrit = 20;
    // Gunner "Suppress" ability: aim penalty inflicted on the pinned target.
    public const int SuppressAim = 30;

    public static ShotOdds ComputeOdds(Grid grid, Unit a, Unit d)
    {
        float dist = Util.TileDist(a.X, a.Y, d.X, d.Y);
        var cover = grid.GetCover(d.X, d.Y, a.X, a.Y);
        bool highGround = grid.HeightAt(a.X, a.Y) > grid.HeightAt(d.X, d.Y);

        int hit = a.Aim + a.Weapon.AimBonus + a.Weapon.RangeMod(dist) - cover.Defense;
        if (d.Hunkered) hit -= 25;
        if (highGround) hit += HighGroundAim;
        if (a.Steady) hit += SteadyAim;          // sharpshooter: braced shot
        if (a.Suppress > 0) hit -= a.Suppress;   // gunner: suppressed shooter
        // promotion perks (attacker)
        if (a.HasPerk(Perk.LockOn) && cover.Level == 0) hit += Unit.PerkAim;
        if (a.HasPerk(Perk.CloseQuarters) && dist <= Unit.CloseRange) hit += Unit.PerkAim;
        if (a.HasPerk(Perk.Marksman) && dist >= Unit.LongRange) hit += Unit.PerkAim;

        hit = Util.Clamp(hit, 3, 95);

        int crit = a.Weapon.CritBase;
        if (cover.Level == 0) crit += 35;       // exposed / flanked target
        if (highGround) crit += HighGroundCrit;  // shooting down rewards crits
        if (a.Steady) crit += SteadyCrit;        // braced shot also crits harder
        if (a.HasPerk(Perk.Deadeye)) crit += Unit.PerkCrit;
        if (d.Hunkered) crit = 0;               // hunkered can't be crit
        crit = Util.Clamp(crit, 0, 100);

        return new ShotOdds
        {
            HitChance = hit,
            CritChance = crit,
            DmgMin = a.Weapon.DmgMin,
            DmgMax = a.Weapon.DmgMax,
            CoverLevel = cover.Level,
            Flanked = cover.Flanked,
            Hunkered = d.Hunkered,
            HighGround = highGround,
            Steady = a.Steady,
        };
    }

    /// Roll a shot. aimMod lets overwatch apply a reaction penalty.
    public static ShotResult Resolve(Grid grid, Unit a, Unit d, int aimMod = 0)
    {
        var odds = ComputeOdds(grid, a, d);
        int effHit = Util.Clamp(odds.HitChance + aimMod, 1, 99);

        var res = new ShotResult { Odds = odds };
        if (!Util.Roll(effHit))
            return res; // miss

        res.Hit = true;
        int dmg = Util.RandInt(odds.DmgMin, odds.DmgMax);
        if (Util.Roll(odds.CritChance))
        {
            res.Crit = true;
            dmg = (int)MathF.Ceiling(dmg * 1.5f) + 1;
        }
        if (d.HasPerk(Perk.Hardened)) dmg = Math.Max(1, dmg - 1);   // damage resistance
        res.Damage = dmg;
        return res;
    }
}
