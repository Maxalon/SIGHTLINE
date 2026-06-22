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
    public bool SeesOver;    // high ground negates the target's LOW cover
    public bool Partial;     // diagonal-at-range: target only partly obscured (half cover)
    public bool Steady;      // attacker braced (sharpshooter ability) this shot
    public bool Ambush;      // attacker fired from concealment (one-shot bonus)
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
    // Concealment ambush bonus: firing from concealment before breaking it.
    public const int AmbushAim  = 20;
    public const int AmbushCrit = 25;

    public static ShotOdds ComputeOdds(Grid grid, Unit a, Unit d)
    {
        float dist = Util.TileDist(a.X, a.Y, d.X, d.Y);
        var cover = grid.GetCover(d.X, d.Y, a.X, a.Y);
        int heightAdv = grid.HeightAt(a.X, a.Y) - grid.HeightAt(d.X, d.Y);
        bool highGround = heightAdv > 0;

        // a DRONE attacks from above: it ignores the target's cover entirely (3.7)
        bool ignoresCover = a.Cls == "DRONE";

        // high ground sees over LOW cover; a commanding 2-tier advantage sees over HIGH
        // cover too (firing down negates the target's cover; it reads as fully exposed).
        bool seesOver = ignoresCover || (highGround && (cover.Level == 1 || heightAdv >= 2));
        int coverLevel = seesOver ? 0 : cover.Level;
        int coverDef = seesOver ? 0 : cover.Defense;   // cover.Defense is already halved when partial
        bool flanked = cover.Flanked && !seesOver;
        bool partial = cover.Partial && !seesOver;

        // a SHIELD's frontal barrier gives full cover from its facing side regardless of
        // terrain — flank it (or hit it from above / commanding height) to bypass (3.7).
        if (d.Cls == "SHIELD" && !seesOver && ShieldedFrom(d, a.X, a.Y) && coverLevel < 2)
        {
            coverLevel = 2; coverDef = 40; flanked = false; partial = false;
        }

        int hit = a.Aim + a.Weapon.AimBonus + a.Weapon.RangeMod(dist) - coverDef;
        if (d.Hunkered) hit -= 25;
        if (highGround) hit += HighGroundAim;
        if (a.Steady) hit += SteadyAim;          // sharpshooter: braced shot
        if (a.Suppress > 0) hit -= a.Suppress;   // gunner: suppressed shooter
        if (a.Wound > 0) hit -= Unit.WoundAim;   // attrition: a wounded shooter is shakier
        if (a.HasStatus(StatusKind.Disoriented)) hit -= Unit.DisorientAim;  // dazed: can't aim straight
        // promotion perks (attacker)
        if (a.HasPerk(Perk.LockOn) && coverLevel == 0) hit += Unit.PerkAim;
        if (a.HasPerk(Perk.CloseQuarters) && dist <= Unit.CloseRange) hit += Unit.PerkAim;
        if (a.HasPerk(Perk.Marksman) && dist >= Unit.LongRange) hit += Unit.PerkAim;

        // earned traits + bonds (attacker)
        if (a.HasTrait(Trait.Killer) && d.MaxHp > 0 && d.Hp * 2 <= d.MaxHp) hit += Unit.KillerAim;
        if (a.HasTrait(Trait.Vengeful) && a.AllyDown) hit += Unit.VengefulAim;
        if (a.BondAura) hit += Unit.BondAim;     // a bonded squadmate stands adjacent

        if (a.FiredFromConcealment) hit += AmbushAim;
        hit = Util.Clamp(hit, 3, 95);

        int crit = a.Weapon.CritBase;
        if (coverLevel == 0) crit += 35;        // exposed / flanked target
        if (a.FiredFromConcealment) crit += AmbushCrit;  // ambush bonus: caught off-guard
        if (highGround) crit += HighGroundCrit;  // shooting down rewards crits
        if (a.Steady) crit += SteadyCrit;        // braced shot also crits harder
        if (a.HasPerk(Perk.Deadeye)) crit += Unit.PerkCrit;
        if (a.HasTrait(Trait.ColdBlood) && a.MaxHp > 0 && a.Hp * 2 <= a.MaxHp) crit += Unit.ColdBloodCrit;
        if (d.Hunkered) crit = 0;               // hunkered can't be crit
        crit = Util.Clamp(crit, 0, 100);

        return new ShotOdds
        {
            HitChance = hit,
            CritChance = crit,
            DmgMin = a.Weapon.DmgMin,
            DmgMax = a.Weapon.DmgMax,
            CoverLevel = coverLevel,
            Flanked = flanked,
            Hunkered = d.Hunkered,
            HighGround = highGround,
            SeesOver = seesOver,
            Partial = partial,
            Steady = a.Steady,
            Ambush = a.FiredFromConcealment,
        };
    }

    /// True when an attack from (ax,ay) lands on a SHIELD unit's barred (front) side.
    static bool ShieldedFrom(Unit d, int ax, int ay)
    {
        if (d.ShieldDx == 0 && d.ShieldDy == 0) return false;
        int dx = ax - d.X, dy = ay - d.Y;
        if (Math.Abs(dx) >= Math.Abs(dy)) return d.ShieldDx != 0 && Util.Sign(dx) == d.ShieldDx;
        return d.ShieldDy != 0 && Util.Sign(dy) == d.ShieldDy;
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

    /// Headless self-test (SIGHTLINE_COMBATTEST): verify high ground negates a
    /// target's LOW cover but not HIGH cover. Returns a one-line report.
    public static string SelfTest()
    {
        var fails = new System.Collections.Generic.List<string>();

        // defender at (5,5) behind LOW cover toward an attacker on its +x side
        var grid = new Grid();
        grid.Tiles[6, 5] = TileType.LowCover;
        var a = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 8, Y = 5 };
        var d = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 5, Y = 5, Hp = 6, MaxHp = 6 };

        var ground = ComputeOdds(grid, a, d);
        if (ground.CoverLevel != 1) fails.Add("groundLowCover");
        if (ground.SeesOver) fails.Add("groundSeesOverFalse");

        grid.Height[8, 5] = 1;                      // raise the attacker
        var high = ComputeOdds(grid, a, d);
        if (!high.SeesOver) fails.Add("highSeesOver");
        if (high.CoverLevel != 0) fails.Add("highNegatesLow");
        if (high.HitChance <= ground.HitChance) fails.Add("highHitNotBetter");

        // --- the three cases from the cover sketch ---
        var unitA = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy };
        var unitD = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, Hp = 6, MaxHp = 6 };
        ShotOdds Case(Grid grid, int ax, int ay, int dxp, int dyp)
        { unitA.X = ax; unitA.Y = ay; unitD.X = dxp; unitD.Y = dyp; return ComputeOdds(grid, unitA, unitD); }

        // (A) FULL COVER: shallow-angle shot, cover on the defender's facing (west) side
        var gA = new Grid(); gA.Tiles[9, 5] = TileType.HighCover;
        var cA = Case(gA, 2, 4, 10, 5);                 // attacker far west, one row off
        if (cA.CoverLevel != 2 || cA.Flanked) fails.Add("sketchA_full");

        // (B) FLANK: the defender's cover is on a side the shot doesn't come from (north)
        var gB = new Grid(); gB.Tiles[10, 4] = TileType.HighCover;
        var cB = Case(gB, 2, 5, 10, 5);                 // attacker due west
        if (cB.CoverLevel != 0 || !cB.Flanked) fails.Add("sketchB_flank");

        // (C) NO COVER: adjacent diagonal, cover only on one (perpendicular) side
        var gC = new Grid(); gC.Tiles[9, 5] = TileType.HighCover;
        var cC = Case(gC, 9, 6, 10, 5);                 // attacker SW, point-blank
        if (cC.CoverLevel != 0 || !cC.Flanked) fails.Add("sketchC_nocover");

        // (D) diagonal at RANGE with one facing-side cover -> PARTIAL (half) cover, not a flank
        var gD2 = new Grid(); gD2.Tiles[9, 5] = TileType.HighCover;
        var cD2 = Case(gD2, 7, 8, 10, 5);               // attacker 3 tiles SW
        if (cD2.CoverLevel != 2 || cD2.Flanked || !cD2.Partial) fails.Add("diagRangePartial");
        // half defense check: partial high cover should beat full high cover (more hit)
        var gFull = new Grid(); gFull.Tiles[9, 5] = TileType.HighCover;
        var cFull = Case(gFull, 2, 5, 10, 5);           // straight west -> full high cover
        if (cD2.HitChance <= cFull.HitChance) fails.Add("partialNotHalf");

        // (E) true corner (cover on BOTH facing sides) -> full cover, not partial
        var gE = new Grid(); gE.Tiles[9, 5] = TileType.HighCover; gE.Tiles[10, 6] = TileType.LowCover;
        var cE = Case(gE, 7, 8, 10, 5);
        if (cE.CoverLevel != 1 || cE.Flanked || cE.Partial) fails.Add("diagCornerFull");

        // HIGH cover must still protect from a single-tier height edge...
        var grid2 = new Grid();
        grid2.Tiles[6, 5] = TileType.HighCover;
        grid2.Height[8, 5] = 1;
        var highVsHigh = ComputeOdds(grid2, a, d);
        if (highVsHigh.SeesOver) fails.Add("highCoverSeesOver");
        if (highVsHigh.CoverLevel != 2) fails.Add("highCoverKept");

        // ...but a commanding TIER-2 advantage sees over high cover (3.6b)
        grid2.Height[8, 5] = 2;
        var tier2 = ComputeOdds(grid2, a, d);
        if (!tier2.SeesOver) fails.Add("tier2SeesOverHigh");
        if (tier2.CoverLevel != 0) fails.Add("tier2NegatesHigh");
        if (tier2.HitChance <= highVsHigh.HitChance) fails.Add("tier2HitBetter");

        // DRONE ignores the target's cover entirely (attacks from above) (3.7)
        var gDrone = new Grid(); gDrone.Tiles[6, 5] = TileType.HighCover;
        var drone = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Smg), Team = Team.Enemy, X = 8, Y = 5, Cls = "DRONE" };
        var dTgt = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 5, Y = 5, Hp = 6, MaxHp = 6 };
        if (ComputeOdds(gDrone, drone, dTgt).CoverLevel != 0) fails.Add("droneIgnoresCover");

        // SHIELD: full cover from its barred (front) side, flankable from another (3.7)
        var gShield = new Grid();   // no terrain cover at all
        var sh = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 5, Y = 5, Hp = 10, MaxHp = 10, Cls = "SHIELD", ShieldDx = -1, ShieldDy = 0 };
        var atkW = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 2, Y = 5 };  // from the front (west)
        var atkE = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 8, Y = 5 };  // from behind the shield (east)
        if (ComputeOdds(gShield, atkW, sh).CoverLevel != 2) fails.Add("shieldFront");
        if (ComputeOdds(gShield, atkE, sh).CoverLevel != 0) fails.Add("shieldFlank");

        // AMBUSH: FiredFromConcealment grants +AmbushAim hit and +AmbushCrit crit (4.4)
        var gAmb = new Grid();
        var ambA = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5, FiredFromConcealment = false };
        var ambD = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 7, Y = 5, Hp = 6, MaxHp = 6 };
        var noAmb = ComputeOdds(gAmb, ambA, ambD);
        ambA.FiredFromConcealment = true;
        var yesAmb = ComputeOdds(gAmb, ambA, ambD);
        if (!yesAmb.Ambush) fails.Add("ambushFlag");
        if (yesAmb.HitChance != Util.Clamp(noAmb.HitChance + AmbushAim, 3, 95)) fails.Add("ambushHit");
        if (yesAmb.CritChance != Util.Clamp(noAmb.CritChance + AmbushCrit, 0, 100)) fails.Add("ambushCrit");

        return fails.Count == 0
            ? "COMBATTEST: PASS (cover A-E + high-ground + tier-2 + drone/shield + ambush all hold)"
            : "COMBATTEST: FAIL (" + string.Join(",", fails) + ")";
    }
}
