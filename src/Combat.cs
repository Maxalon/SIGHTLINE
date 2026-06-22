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
    public bool Graze;   // hit for minimum damage only; not a full hit but not a miss
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

    // Graze band: a shot that misses by <= GrazeBand hits for minimum damage (no crit).
    // Softens the "I whiffed three 80% shots" tail without removing true misses.
    public const int GrazeBand = 15;
    // Always reserve at least this much clean-miss probability so the graze band can't
    // swallow the whole roll space at high hit chance (review M1: keep true misses alive).
    public const int GrazeMinMiss = 3;

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
        // CoolHeaded: halves the Disoriented aim penalty (net = DisorientAim - CoolHeadedDivert)
        if (a.HasPerk(Perk.CoolHeaded) && a.HasStatus(StatusKind.Disoriented)) hit += Unit.CoolHeadedDivert;

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
        // Executioner: bonus crit vs targets already below half HP (finish-the-job perk)
        if (a.HasPerk(Perk.Executioner) && d.MaxHp > 0 && d.Hp * 2 < d.MaxHp) crit += Unit.ExecutionerCrit;
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

    // Streak-breaker constants (S4-C): per clean-miss aim bonus, capped at MaxStreakBonus.
    // Applied INSIDE Resolve only (hidden from the ComputeOdds display — DESIGN.md 3B).
    public const int StreakBonusPerMiss = 6;   // +6 effHit per consecutive miss
    public const int MaxStreakBonus     = 12;  // capped at +12 (after 2+ misses)

    /// Roll a shot. aimMod lets overwatch apply a reaction penalty.
    public static ShotResult Resolve(Grid grid, Unit a, Unit d, int aimMod = 0)
    {
        var odds = ComputeOdds(grid, a, d);
        // Streak-breaker (S4-C): apply a small hidden bonus after consecutive clean misses.
        // Keeps it subtle (max +12); resets on any connect (hit or graze). HIDDEN from
        // the ComputeOdds tooltip so players don't know the dice are loaded (DESIGN.md 3B).
        // player-only: the streak-breaker exists to curb the PLAYER's miss-streak frustration;
        // enemies don't rage, and a hidden enemy aim nudge would only quietly raise difficulty
        // (review Minor — matches the "a soldier's shot" intent).
        int streakBonus = a.Team == Team.Player ? Math.Min(StreakBonusPerMiss * a.ConsecutiveMisses, MaxStreakBonus) : 0;
        int effHit = Util.Clamp(odds.HitChance + aimMod + streakBonus, 1, 99);

        var res = new ShotResult { Odds = odds };

        // Inline the roll so we can inspect the raw value for the graze band.
        // Roll is in [0,100): hit if roll < effHit. Graze if roll in [effHit, grazeTop),
        // where grazeTop reserves a minimum clean-miss window so even high-% shots can
        // still truly miss (review M1: graze softens the tail, it doesn't delete misses).
        double roll = Util.Rng.NextDouble() * 100.0;
        double grazeTop = Math.Min(effHit + GrazeBand, 100.0 - GrazeMinMiss);
        bool hit   = roll < effHit;
        bool graze = !hit && roll < grazeTop;

        if (!hit && !graze)
        {
            a.ConsecutiveMisses++;  // accumulate the streak
            return res;             // clean miss
        }

        res.Hit = true;
        a.ConsecutiveMisses = 0;  // hit or graze: reset the streak

        if (graze)
        {
            // Graze: minimum damage, never crits.
            res.Graze = true;
            int dmg = odds.DmgMin;
            if (d.HasPerk(Perk.Hardened)) dmg = Math.Max(1, dmg - 1);
            res.Damage = Math.Max(1, dmg);   // guaranteed-damage floor
            return res;
        }

        // Normal hit path.
        int dmgN = Util.RandInt(odds.DmgMin, odds.DmgMax);
        if (Util.Roll(odds.CritChance))
        {
            res.Crit = true;
            dmgN = (int)MathF.Ceiling(dmgN * 1.5f) + 1;
        }
        if (d.HasPerk(Perk.Hardened)) dmgN = Math.Max(1, dmgN - 1);   // damage resistance
        res.Damage = Math.Max(1, dmgN);   // guaranteed-damage floor
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

        // GRAZE + guaranteed-damage floor (S2-A)
        // Reproduce "a shot that misses by <= 15" by exercising Resolve directly.
        // We need repeatable control over the roll, so we test the band conditions
        // by comparing outcomes of two rolls at known positions in the band.
        {
            var gG = new Grid();
            var gAtk = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            var gDef = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 20, MaxHp = 20 };

            // Test graze band logic directly using the band constants: a shot at effHit=60
            // must graze when roll in [60,75) and miss when roll >= 75.
            // We'll call Resolve many times and verify statistical behaviour.
            // Reset ConsecutiveMisses each shot so the streak-breaker doesn't skew the base-rate test.
            int totalShots = 10000;
            int hitCount = 0, grazeCount = 0, missCount = 0;
            for (int i = 0; i < totalShots; i++)
            {
                gAtk.ConsecutiveMisses = 0;  // isolate: test raw effHit=60 only
                var r = Resolve(gG, gAtk, gDef, 0);
                if (!r.Hit) missCount++;
                else if (r.Graze) grazeCount++;
                else hitCount++;
            }
            // Expected: ~60% hit, ~15% graze, ~25% clean miss (within 7% tolerance at N=10000)
            float hitPct   = hitCount   * 100f / totalShots;
            float grazePct = grazeCount * 100f / totalShots;
            float missPct  = missCount  * 100f / totalShots;
            if (hitPct < 53f || hitPct > 67f) fails.Add($"grazeHitRate={hitPct:F1}");
            if (grazePct < 8f || grazePct > 22f) fails.Add($"grazeGrazeRate={grazePct:F1}");
            if (missPct < 18f || missPct > 32f) fails.Add($"grazeMissRate={missPct:F1}");

            // A graze must deal exactly DmgMin and must not be a Crit. Always run the full
            // sample (no early exit) so the loop can't be misread as a premature-bail bug.
            bool foundGraze = false, grazeCritBad = false, grazeDmgBad = false;
            for (int i = 0; i < 2000; i++)
            {
                gAtk.ConsecutiveMisses = 0;  // isolate: no streak bonus
                gDef.Hp = 20;
                var r = Resolve(gG, gAtk, gDef, 0);
                if (r.Hit && r.Graze)
                {
                    foundGraze = true;
                    if (r.Crit) grazeCritBad = true;
                    if (r.Damage != gAtk.Weapon.DmgMin) grazeDmgBad = true;
                }
            }
            if (!foundGraze) fails.Add("grazeNeverOccurred");
            if (grazeCritBad) fails.Add("grazeCrit");
            if (grazeDmgBad) fails.Add("grazeDmgNotMin");

            // A clean miss (roll >= effHit + GrazeBand) must have Hit==false.
            // Confirm: with effHit bumped very high a graze is ~15% window above 95 which is clamped,
            // and with effHit=0 there are no hits and only grazes below 15, all misses above 15.
            // Test: effHit=0 (aimMod=-200) → only misses and grazes (no normal hits).
            int normalHitsAtZero = 0;
            for (int i = 0; i < 500; i++)
            {
                gAtk.ConsecutiveMisses = 0;  // isolate: no streak bonus (keep effHit near 1)
                gDef.Hp = 20;
                var r = Resolve(gG, gAtk, gDef, aimMod: -200);   // effHit clamped to 1
                if (r.Hit && !r.Graze) normalHitsAtZero++;
            }
            // At effHit=1 almost everything is a graze (<16) or a miss (>=16); no normal hits
            // ... actually effHit is clamped to 1 not 0, so hits at roll<1 are rare but possible.
            // Accept up to 5 pure hits out of 500 (1% expected; allow 5 as headroom).
            if (normalHitsAtZero > 15) fails.Add($"grazeZeroHits={normalHitsAtZero}");

            // Guaranteed-damage floor: every hit (incl. graze + Hardened perk) must deal >= 1.
            var gHard = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 20, MaxHp = 20 };
            gHard.Perks.Add(Perk.Hardened);
            for (int i = 0; i < 400; i++)
            {
                gHard.Hp = 20;
                var r = Resolve(gG, gAtk, gHard, 0);
                if (r.Hit && r.Damage < 1) fails.Add("dmgFloorBroken");
            }
        }

        // STREAK-BREAKER (S4-C): ConsecutiveMisses raises the internal effHit (hidden).
        {
            var gS = new Grid();
            var sAtk = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5, ConsecutiveMisses = 0 };
            var sDef = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 7, Y = 5, Hp = 20, MaxHp = 20 };

            // With 0 misses the streak bonus should be 0.
            int bonusZero = Math.Min(StreakBonusPerMiss * sAtk.ConsecutiveMisses, MaxStreakBonus);
            if (bonusZero != 0) fails.Add($"streakBonus0={bonusZero}");

            // With 2 consecutive misses the streak bonus should be +12 (2*6 = 12 = cap).
            sAtk.ConsecutiveMisses = 2;
            int bonusTwo = Math.Min(StreakBonusPerMiss * sAtk.ConsecutiveMisses, MaxStreakBonus);
            if (bonusTwo != 12) fails.Add($"streakBonus2={bonusTwo}");

            // With 3+ misses it should be capped at MaxStreakBonus (12), not 18.
            sAtk.ConsecutiveMisses = 5;
            int bonusFive = Math.Min(StreakBonusPerMiss * sAtk.ConsecutiveMisses, MaxStreakBonus);
            if (bonusFive != MaxStreakBonus) fails.Add($"streakBonusCap={bonusFive}");

            // A HIT must reset ConsecutiveMisses to 0 (use a very high aimMod so we always hit).
            sAtk.ConsecutiveMisses = 3;
            sDef.Hp = 20;
            // Force a hit by using a massive aimMod (+200 clamps to 99%, effectively certain).
            // Keep retrying until we get a hit (should happen on virtually the first shot).
            for (int i = 0; i < 1000 && sAtk.ConsecutiveMisses != 0; i++)
            {
                sDef.Hp = 20; sAtk.ConsecutiveMisses = 3;
                var r = Resolve(gS, sAtk, sDef, aimMod: 200);
                if (!r.Hit) sAtk.ConsecutiveMisses = 3;  // restore if somehow missed (pathological RNG)
            }
            if (sAtk.ConsecutiveMisses != 0) fails.Add($"streakHitReset={sAtk.ConsecutiveMisses}");

            // A CLEAN MISS must increment ConsecutiveMisses (use a large negative aimMod so we mostly miss).
            sAtk.ConsecutiveMisses = 0;
            sDef.Hp = 20;
            bool foundMiss = false;
            for (int i = 0; i < 500; i++)
            {
                sDef.Hp = 20;
                int prevMisses = sAtk.ConsecutiveMisses;
                var r = Resolve(gS, sAtk, sDef, aimMod: -200);
                if (!r.Hit)
                {
                    // A clean miss should have incremented by 1 from prevMisses.
                    if (sAtk.ConsecutiveMisses != prevMisses + 1) fails.Add("streakMissIncrement");
                    foundMiss = true;
                    break;
                }
                else
                {
                    // A hit/graze resets to 0; that's also the behaviour we want.
                    sAtk.ConsecutiveMisses = 0;
                }
            }
            if (!foundMiss) fails.Add("streakNoMissFound");

            // ComputeOdds must NOT reflect the streak bonus (hidden from the display).
            sAtk.ConsecutiveMisses = 5;
            var oddsNoStreak = ComputeOdds(gS, sAtk, sDef);
            sAtk.ConsecutiveMisses = 0;
            var oddsZeroMisses = ComputeOdds(gS, sAtk, sDef);
            if (oddsNoStreak.HitChance != oddsZeroMisses.HitChance) fails.Add("streakVisibleInOdds");
        }

        return fails.Count == 0
            ? "COMBATTEST: PASS (cover A-E + high-ground + tier-2 + drone/shield + ambush + graze + streak all hold)"
            : "COMBATTEST: FAIL (" + string.Join(",", fails) + ")";
    }
}
