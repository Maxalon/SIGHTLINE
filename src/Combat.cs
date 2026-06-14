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
    public static ShotOdds ComputeOdds(Grid grid, Unit a, Unit d)
    {
        float dist = Util.TileDist(a.X, a.Y, d.X, d.Y);
        var cover = grid.GetCover(d.X, d.Y, a.X, a.Y);

        int hit = a.Aim + a.Weapon.AimBonus + a.Weapon.RangeMod(dist) - cover.Defense;
        if (d.Hunkered) hit -= 25;

        hit = Util.Clamp(hit, 3, 95);

        int crit = a.Weapon.CritBase;
        if (cover.Level == 0) crit += 35;       // exposed / flanked target
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
        res.Damage = dmg;
        return res;
    }
}
