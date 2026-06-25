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
        // CoolHeaded (composure) is a DEFENDER perk now: a CoolHeaded TARGET is hard to rattle, so any
        // attacker firing at it loses CoolHeadedEvade aim (its daze-immunity half lives in Unit.AddStatus).
        // A survivability pick, distinct from the attacker-side aim line (LockOn/CloseQuarters/Marksman).
        if (d.HasPerk(Perk.CoolHeaded)) hit -= Unit.CoolHeadedEvade;

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
        if (a.HasPerk(Perk.Deadeye)) crit += Unit.PerkCrit;   // Deadeye: flat crit, any target
        // Executioner: a FINISHER — bigger crit than Deadeye, but only vs sub-half-HP prey. So it
        // BEATS Deadeye against the wounded and LOSES against the healthy (a real choice, not a subset).
        if (a.HasPerk(Perk.Executioner) && d.MaxHp > 0 && d.Hp * 2 < d.MaxHp) crit += Unit.ExecutionerCrit;
        // Opportunist: a FLANKER'S FINISHER — +crit ONLY vs a genuinely FLANKED target (the foe HAD
        // adjacent cover but you maneuvered to an angle it doesn't protect; cover.Flanked == true).
        // Deliberately NOT "any no-cover target" (that's LockOn's +AIM gate) — Opportunist rewards the
        // *move that turns a covered foe's flank*, so it fires when LockOn would NOT (a foe in the open,
        // never in cover, isn't a flank). Distinct trigger, distinct payoff (crit, not aim).
        if (a.HasPerk(Perk.Opportunist) && flanked) crit += Unit.OpportunistCrit;
        // Vanguard: a BREACHER'S FINISHER — +crit ONLY vs a target that is BOTH genuinely FLANKED
        // (you out-positioned its cover) AND ADJACENT (dist <= 1, in its face). The tightest gate of
        // the crit perks (Opportunist needs only the flank at any range; Point Blank needs only the
        // range vs any target) — so it pays the biggest crit. Rewards closing the distance to finish
        // a flanked foe; goes inert at range or against an unflanked target.
        if (a.HasPerk(Perk.Vanguard) && flanked && dist <= Unit.VanguardRange) crit += Unit.VanguardCrit;
        // First Strike (enum member GiantSlayer, reworked): an ALPHA-STRIKE/OPENER — +crit vs a target
        // still at FULL HP. Rewards focus-firing a FRESH enemy (the first shot that connects); it stops
        // helping the instant the target is chipped, so it pairs with picking targets, not finishing them
        // (the opposite end from Executioner's sub-half-HP crit). Fires on ~every new engagement, fodder
        // or boss alike — so it's a live choice, not the old dead "MaxHp >= 12" gate (see Unit.FirstStrikeCrit).
        if (a.HasPerk(Perk.GiantSlayer) && d.MaxHp > 0 && d.Hp >= d.MaxHp) crit += Unit.FirstStrikeCrit;
        // Point Blank: a CLOSE-RANGE CRIT build — +crit within 2 tiles (vs CloseQuarters' +aim within 4).
        if (a.HasPerk(Perk.PointBlank) && dist <= Unit.PointBlankRange) crit += Unit.PointBlankCrit;
        // Guardian: overwatch LETHALITY. A reaction shot (ReactedThisTurn is set by Game right before
        // it Resolves this shot) crits hard — Reflexes makes overwatch reliable, Guardian makes it lethal.
        if (a.HasPerk(Perk.Guardian) && IsOverwatchReaction(a)) crit += Unit.GuardianReactCrit;
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

    /// True if this shot is an overwatch REACTION. Game's OnUnitEnteredTile sets
    /// ReactedThisTurn=true on the watcher immediately before calling Combat.Resolve for the
    /// reaction shot (and an overwatch unit has 0 actions left, so it can't also take a normal
    /// aimed shot that turn). So ReactedThisTurn==true uniquely flags the reaction shot here —
    /// it's never set for the hovered-aim preview of a selected, still-acting soldier. Guardian
    /// keys its overwatch-only bonuses off this without needing a Game.cs edit.
    static bool IsOverwatchReaction(Unit a) => a.ReactedThisTurn;

    /// INCOMING-DAMAGE REDUCTION — the ONE source of truth for every incoming-hit path: Resolve's
    /// hit + graze branches AND the grenade blast in Anim (which all call this with the defender).
    /// Despite the historical name (kept stable because Anim.cs calls it), this folds in EVERY
    /// damage-in mitigation that lives on the DEFENDER, stacking additively:
    ///   - ARMOR (d.Armor): a persistent flat reduction bought in the shop — subtracted from every hit.
    ///   - HARDENED perk: -HardenedFlat off any hit, plus an extra -HardenedCrit off a CRITICAL hit
    ///     (crits are the spiky shots that drop soldiers — a tank shrugs them off).
    ///   - BULWARK perk: an extra -BulwarkFlat WHILE the defender is HUNKERED (a deliberate hold-the-
    ///     line trade — worthless if you never dig in).
    /// ALWAYS floored at 1, so the guaranteed-damage floor still holds: every hit deals >= 1 no matter
    /// how much armor/perk reduction stacks. With no armor and no perks this is a pass-through (dmg).
    public static int HardenedReduce(Unit d, int dmg, bool crit)
    {
        int reduce = d.Armor;                                   // persistent shop armor (flat, always)
        if (d.HasPerk(Perk.Hardened))
            reduce += Unit.HardenedFlat + (crit ? Unit.HardenedCrit : 0);  // tank perk: flat + extra vs crit
        if (d.HasPerk(Perk.Bulwark) && d.Hunkered)
            reduce += Unit.BulwarkFlat;                         // turtle perk: extra while braced
        if (reduce <= 0) return dmg;                            // nothing to subtract: pass through
        return Math.Max(1, dmg - reduce);                       // guaranteed-damage floor (>= 1)
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
        // Guardian: cancel the standard -10 overwatch reaction penalty (which Game folds into aimMod,
        // invisible to ComputeOdds) so its reactions fire at full accuracy. Applied here, not in
        // ComputeOdds, because the penalty it offsets isn't part of the displayed HitChance either.
        int guardianBonus = (a.HasPerk(Perk.Guardian) && IsOverwatchReaction(a)) ? Unit.GuardianReactAim : 0;
        int effHit = Util.Clamp(odds.HitChance + aimMod + streakBonus + guardianBonus, 1, 99);

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
            int dmg = HardenedReduce(d, odds.DmgMin, crit: false);   // tank: damage resistance
            res.Damage = Math.Max(1, dmg);   // guaranteed-damage floor
            res.Damage = FragileFloor(d, res.Damage);   // a full-HP player survives any one shot
            return res;
        }

        // Normal hit path.
        int dmgN = Util.RandInt(odds.DmgMin, odds.DmgMax);
        if (Util.Roll(odds.CritChance))
        {
            res.Crit = true;
            dmgN = (int)MathF.Ceiling(dmgN * 1.5f) + 1;
        }
        dmgN = HardenedReduce(d, dmgN, res.Crit);   // tank: -1 always, -3 more from crits
        res.Damage = Math.Max(1, dmgN);   // guaranteed-damage floor
        res.Damage = FragileFloor(d, res.Damage);   // a full-HP player survives any one shot
        return res;
    }

    /// Fragile-unit anti-one-shot floor: a PLAYER unit (Team.Player, incl. the VIP) that is at
    /// FULL HP cannot be dropped below 1 HP by a SINGLE shot — cap the damage at MaxHp-1 so a lucky
    /// crit leaves them clinging at 1 HP instead of dead. Softens the worst output-randomness
    /// feel-bad (losing a soldier/VIP from full to one crit) WITHOUT helping the player's offense:
    /// enemies are never protected. Silent (no FX) — a pure damage cap kept inside Combat.
    static int FragileFloor(Unit d, int dmg)
    {
        if (d.Team == Team.Player && d.MaxHp >= 2 && d.Hp >= d.MaxHp)
            return Math.Min(dmg, d.MaxHp - 1);
        return dmg;
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

        // PERK BALANCE: Executioner must now BEAT Deadeye vs a sub-half-HP target (finisher),
        // and LOSE to Deadeye vs a healthy target (so it's a real choice, not a dominated subset).
        {
            var gP = new Grid();   // no cover -> identical baseline for both perks
            var baseA = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            var ddeA  = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            var excA  = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            ddeA.Perks.Add(Perk.Deadeye);
            excA.Perks.Add(Perk.Executioner);

            // Wounded target (below half HP): Executioner's bonus applies and exceeds Deadeye's flat one.
            var wounded = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 2, MaxHp = 10 };
            int baseCritW = ComputeOdds(gP, baseA, wounded).CritChance;
            int ddeCritW  = ComputeOdds(gP, ddeA,  wounded).CritChance;
            int excCritW  = ComputeOdds(gP, excA,  wounded).CritChance;
            if (ddeCritW != baseCritW + Unit.PerkCrit) fails.Add("deadeyeFlat");
            if (excCritW != baseCritW + Unit.ExecutionerCrit) fails.Add("execBonus");
            if (excCritW <= ddeCritW) fails.Add("execNotBeatDeadeyeOnWounded");   // the un-domination check

            // Healthy target (full HP): Executioner does nothing, so Deadeye wins (real trade-off).
            var healthy = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 10, MaxHp = 10 };
            int ddeCritH = ComputeOdds(gP, ddeA, healthy).CritChance;
            int excCritH = ComputeOdds(gP, excA, healthy).CritChance;
            if (excCritH != ComputeOdds(gP, baseA, healthy).CritChance) fails.Add("execHealthyNoOp");
            if (ddeCritH <= excCritH) fails.Add("deadeyeNotBeatExecOnHealthy");
        }

        // COOL-HEADED (reworked): a DEFENDER composure perk. (1) An attacker shooting a CoolHeaded
        // target loses CoolHeadedEvade aim; a normal (non-perked) target gives no such discount.
        // (2) A CoolHeaded soldier is immune to Disoriented (AddStatus is a no-op for that kind).
        {
            var gCH = new Grid();
            var atk     = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 3, Y = 5 };
            var plainD  = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 7, Y = 5, Hp = 10, MaxHp = 10 };
            var coolD   = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 7, Y = 5, Hp = 10, MaxHp = 10 };
            coolD.Perks.Add(Perk.CoolHeaded);

            // Shooting a CoolHeaded target: the attacker's hit drops by exactly CoolHeadedEvade.
            int hitVsPlain = ComputeOdds(gCH, atk, plainD).HitChance;
            int hitVsCool  = ComputeOdds(gCH, atk, coolD).HitChance;
            if (hitVsCool != Util.Clamp(hitVsPlain - Unit.CoolHeadedEvade, 3, 95)) fails.Add("coolHeadedEvade");

            // CoolHeaded does NOT help its own offense (it's a defender perk): a CoolHeaded SHOOTER reads
            // the same as a plain shooter against the same target.
            var coolAtk = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            coolAtk.Perks.Add(Perk.CoolHeaded);
            var plainAtk = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            var foeT = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 10, MaxHp = 10 };
            if (ComputeOdds(gCH, coolAtk, foeT).HitChance != ComputeOdds(gCH, plainAtk, foeT).HitChance) fails.Add("coolHeadedNoSelfAim");

            // Disoriented immunity: AddStatus(Disoriented) is a no-op on a CoolHeaded unit (so its aim is
            // never docked by the daze), but a plain unit catches it and reads -DisorientAim.
            coolD.AddStatus(StatusKind.Disoriented, 3);
            if (coolD.HasStatus(StatusKind.Disoriented)) fails.Add("coolHeadedDazeImmune");
            var dazPlain = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            int plainShootClean = ComputeOdds(gCH, dazPlain, foeT).HitChance;
            dazPlain.AddStatus(StatusKind.Disoriented, 3);
            if (!dazPlain.HasStatus(StatusKind.Disoriented)) fails.Add("plainDazeApplies");
            if (ComputeOdds(gCH, dazPlain, foeT).HitChance != Util.Clamp(plainShootClean - Unit.DisorientAim, 3, 95)) fails.Add("plainDazePenalty");
        }

        // HARDENED (reworked): -HardenedFlat off any hit, -HardenedCrit MORE off a crit. Verify the
        // damage reduction via the helper directly (deterministic) so the random Resolve path can't flake.
        {
            var plainD = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, Hp = 20, MaxHp = 20 };
            var hardD  = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, Hp = 20, MaxHp = 20 };
            hardD.Perks.Add(Perk.Hardened);
            // No perk: damage passes through untouched (both crit and non-crit).
            if (HardenedReduce(plainD, 7, false) != 7 || HardenedReduce(plainD, 7, true) != 7) fails.Add("hardenedNoOpWithoutPerk");
            // Non-crit hit: -HardenedFlat.
            if (HardenedReduce(hardD, 7, false) != 7 - Unit.HardenedFlat) fails.Add("hardenedFlat");
            // Crit hit: -HardenedFlat - HardenedCrit (the spiky-shot mitigation that makes it a tank perk).
            if (HardenedReduce(hardD, 9, true) != 9 - Unit.HardenedFlat - Unit.HardenedCrit) fails.Add("hardenedCrit");
            // Crit reduction must exceed the non-crit reduction (the whole point — shrugs off crits harder).
            if ((9 - HardenedReduce(hardD, 9, true)) <= (9 - HardenedReduce(hardD, 9, false))) fails.Add("hardenedCritStronger");
            // Floor: a tiny hit still deals >= 1 even with the full crit reduction.
            if (HardenedReduce(hardD, 1, true) < 1) fails.Add("hardenedFloor");
        }

        // GUARDIAN: an overwatch REACTION shot (flagged by ReactedThisTurn) ignores the -10 reaction
        // penalty (Resolve) and crits hard (ComputeOdds); a normal (non-reaction) shot gets neither.
        {
            var gG2 = new Grid();
            var grd = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            grd.Perks.Add(Perk.Guardian);
            var foe = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 10, MaxHp = 10 };

            // Not reacting: Guardian is inert (it's an overwatch-only perk).
            grd.ReactedThisTurn = false;
            int restingCrit = ComputeOdds(gG2, grd, foe).CritChance;
            if (restingCrit != ComputeOdds(gG2, new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 }, foe).CritChance)
                fails.Add("guardianRestingInert");

            // Reacting: the crit bonus shows in the odds...
            grd.ReactedThisTurn = true;
            int reactCrit = ComputeOdds(gG2, grd, foe).CritChance;
            if (reactCrit != Util.Clamp(restingCrit + Unit.GuardianReactCrit, 0, 100)) fails.Add("guardianReactCrit");

            // ...and the -10 reaction penalty is cancelled in Resolve. Compare effHit on a reaction
            // shot with the standard -10 aimMod: a Guardian unit should hit as if there were no penalty.
            // Probe via hit rate at the same aimMod=-10 (Guardian) vs aimMod=0 (no perk, no penalty).
            int gHits = 0, refHits = 0; int N = 4000;
            var refU = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            for (int i = 0; i < N; i++)
            {
                grd.ReactedThisTurn = true; grd.ConsecutiveMisses = 0; foe.Hp = 10;
                if (Resolve(gG2, grd, foe, aimMod: -10).Hit) gHits++;        // Guardian reaction (penalty cancelled)
                refU.ConsecutiveMisses = 0; foe.Hp = 10;
                if (Resolve(gG2, refU, foe, aimMod: 0).Hit) refHits++;        // baseline with no penalty
            }
            // The two hit rates should be statistically equal (within tolerance) since Guardian
            // negates the -10. Allow a 5-point band at N=4000.
            float gPct = gHits * 100f / N, refPct = refHits * 100f / N;
            if (Math.Abs(gPct - refPct) > 5f) fails.Add($"guardianPenaltyCancel(g={gPct:F1},ref={refPct:F1})");
            grd.ReactedThisTurn = false;
        }

        // BUILD-VARIETY PERKS (Opportunist / Point Blank / First Strike): each is a pure ComputeOdds
        // read that fires ONLY under its condition and NOT otherwise, and is DISTINCT from the others.
        {
            var gV = new Grid();
            Unit Perked(Perk p) { var u = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 }; u.Perks.Add(p); return u; }
            Unit Plain() => new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };

            // ---- OPPORTUNIST: +crit ONLY on a GENUINE FLANK (foe HAD cover, hit from an unprotected
            // angle), NOT on a merely-exposed (open-ground) foe, NOT on a covered foe ----
            // Genuine flank: attacker at x=3 (west), target at (7,5) with cover on its EAST side (x=8) —
            // the facing (west) neighbour is open so cover.Level collapses to 0 but cover.Flanked is true.
            var gFlank = new Grid(); gFlank.Tiles[8, 5] = TileType.HighCover;
            var flankFoe = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 10, MaxHp = 10 };
            var oppFlankOdds = ComputeOdds(gFlank, Perked(Perk.Opportunist), flankFoe);
            if (!oppFlankOdds.Flanked) fails.Add("opportunistFlankSetup");        // guard: the foe really is flanked (had cover)
            if (oppFlankOdds.CritChance != Util.Clamp(ComputeOdds(gFlank, Plain(), flankFoe).CritChance + Unit.OpportunistCrit, 0, 100)) fails.Add("opportunistFlankFires");
            // Merely exposed (open ground, NO adjacent cover => not a flank): Opportunist must NOT fire,
            // even though coverLevel is also 0 here. This is the LockOn-vs-Opportunist differentiation.
            var exposed = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 10, MaxHp = 10 };
            var oppExpOdds = ComputeOdds(gV, Perked(Perk.Opportunist), exposed);
            if (oppExpOdds.CoverLevel != 0 || oppExpOdds.Flanked) fails.Add("opportunistExposedSetup");  // guard: exposed, NOT flanked
            if (oppExpOdds.CritChance != ComputeOdds(gV, Plain(), exposed).CritChance) fails.Add("opportunistExposedNoOp");
            // Covered target (full high cover on the facing side, coverLevel==2): Opportunist must NOT fire.
            var gCov = new Grid(); gCov.Tiles[5, 5] = TileType.HighCover;
            var coveredFoe = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 6, Y = 5, Hp = 10, MaxHp = 10 };
            var oppCovOdds = ComputeOdds(gCov, Perked(Perk.Opportunist), coveredFoe);
            if (oppCovOdds.CoverLevel == 0) fails.Add("opportunistCoverSetup");   // guard: the foe really is covered
            if (oppCovOdds.CritChance != ComputeOdds(gCov, Plain(), coveredFoe).CritChance) fails.Add("opportunistCoveredNoOp");

            // ---- POINT BLANK: +crit within 2 tiles; nothing beyond (same NO-COVER state both times) ----
            var pbClose = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 5, Y = 5, Hp = 10, MaxHp = 10 };  // dist 2 from x=3
            var pbFar   = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 8, Y = 5, Hp = 10, MaxHp = 10 };  // dist 5 from x=3
            int pbCloseOn  = ComputeOdds(gV, Perked(Perk.PointBlank), pbClose).CritChance;
            int pbCloseOff = ComputeOdds(gV, Plain(),               pbClose).CritChance;
            if (pbCloseOn != Util.Clamp(pbCloseOff + Unit.PointBlankCrit, 0, 100)) fails.Add("pointBlankClose");
            if (ComputeOdds(gV, Perked(Perk.PointBlank), pbFar).CritChance != ComputeOdds(gV, Plain(), pbFar).CritChance) fails.Add("pointBlankFarNoOp");

            // ---- FIRST STRIKE (enum member GiantSlayer, reworked): +crit vs a FULL-HP target; nothing
            // once it's been chipped (same cover/range both times). The alpha-strike / opener perk ----
            var fresh   = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 10, MaxHp = 10 };  // full HP -> fires
            var chipped = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 9,  MaxHp = 10 };  // 1 dmg taken -> inert
            int fsFresh = ComputeOdds(gV, Perked(Perk.GiantSlayer), fresh).CritChance;
            int plainFr = ComputeOdds(gV, Plain(),                  fresh).CritChance;
            if (fsFresh != Util.Clamp(plainFr + Unit.FirstStrikeCrit, 0, 100)) fails.Add("firstStrikeFull");
            if (ComputeOdds(gV, Perked(Perk.GiantSlayer), chipped).CritChance != ComputeOdds(gV, Plain(), chipped).CritChance) fails.Add("firstStrikeChippedNoOp");

            // ---- DISTINCTNESS: the three perks key off independent conditions ----
            // Against a FLANKED, point-blank, FULL-HP target all three fire; against an exposed-but-not-
            // flanked, far, chipped target none do. (A sanity check that they're not the same gate.)
            // allYes: target at (5,5) [dist 2 from x=3], cover on its EAST side (x=6) = flanked, full HP.
            var gAll = new Grid(); gAll.Tiles[6, 5] = TileType.HighCover;
            var allYes = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 5, Y = 5, Hp = 14, MaxHp = 14 };
            if (ComputeOdds(gAll, Perked(Perk.Opportunist), allYes).CritChance <= ComputeOdds(gAll, Plain(), allYes).CritChance) fails.Add("distinctOppFires");
            if (ComputeOdds(gAll, Perked(Perk.PointBlank),  allYes).CritChance <= ComputeOdds(gAll, Plain(), allYes).CritChance) fails.Add("distinctPbFires");
            if (ComputeOdds(gAll, Perked(Perk.GiantSlayer), allYes).CritChance <= ComputeOdds(gAll, Plain(), allYes).CritChance) fails.Add("distinctFsFires");
        }

        // FRAGILE-UNIT ONE-SHOT FLOOR: a full-HP PLAYER unit can't be dropped below 1 HP by a single
        // shot (capped at MaxHp-1); enemies are NOT protected. Use Sniper (DmgMin=5) vs a 4-HP unit so
        // EVERY hit (crit or not) would otherwise be lethal — the floor must always leave HP >= 1.
        {
            var gF = new Grid();
            var sniper = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Sniper), Team = Team.Enemy, X = 8, Y = 5 };

            // Player at full HP: never dies to one shot.
            var pFull = new Unit { Aim = 50, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 5, Y = 5, Hp = 4, MaxHp = 4 };
            bool floorHeld = true, sawHit = false, sawCrit = false;
            for (int i = 0; i < 3000; i++)
            {
                pFull.Hp = 4;   // reset to full each shot
                var r = Resolve(gF, sniper, pFull, aimMod: 200);   // guarantee a hit
                if (!r.Hit) continue;
                sawHit = true; if (r.Crit) sawCrit = true;
                if (r.Damage > pFull.MaxHp - 1) floorHeld = false;          // capped at MaxHp-1...
                if (pFull.Hp - r.Damage < 1) floorHeld = false;             // ...so survivor clings at >=1
            }
            if (!sawHit) fails.Add("floorNoHit");
            if (!sawCrit) fails.Add("floorNoCritSampled");   // make sure the lethal-crit case was exercised
            if (!floorHeld) fails.Add("fragileFloorBroken");

            // Player NOT at full HP: the floor does not apply (a wounded soldier can still die).
            var pHurt = new Unit { Aim = 50, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 5, Y = 5, Hp = 3, MaxHp = 4 };
            bool sawLethalOnHurt = false;
            for (int i = 0; i < 3000; i++)
            {
                pHurt.Hp = 3;
                var r = Resolve(gF, sniper, pHurt, aimMod: 200);
                if (r.Hit && pHurt.Hp - r.Damage < 1) { sawLethalOnHurt = true; break; }
            }
            if (!sawLethalOnHurt) fails.Add("floorWronglyProtectsHurt");

            // Enemy at full HP: NOT protected (the player's offense isn't weakened).
            var eFull = new Unit { Aim = 50, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 5, Y = 5, Hp = 4, MaxHp = 4 };
            var shooter = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Sniper), Team = Team.Player, X = 8, Y = 5 };
            bool sawLethalOnEnemy = false;
            for (int i = 0; i < 3000; i++)
            {
                eFull.Hp = 4;
                var r = Resolve(gF, shooter, eFull, aimMod: 200);
                if (r.Hit && eFull.Hp - r.Damage < 1) { sawLethalOnEnemy = true; break; }
            }
            if (!sawLethalOnEnemy) fails.Add("floorWronglyProtectsEnemy");
        }

        // ARMOR: a persistent flat damage-reducer folded into HardenedReduce (the shared chokepoint
        // for every incoming-hit path). Verify it subtracts d.Armor from any hit, stacks ON TOP of
        // the Hardened perk, and never breaks the guaranteed-damage floor (>= 1).
        {
            var plain = new Unit { Team = Team.Player, Hp = 20, MaxHp = 20 };                       // 0 armor, no perk
            var armored = new Unit { Team = Team.Player, Hp = 20, MaxHp = 20, Armor = 2 };          // 2 armor, no perk
            // No armor + no perk: pass-through (both crit and non-crit).
            if (HardenedReduce(plain, 7, false) != 7 || HardenedReduce(plain, 7, true) != 7) fails.Add("armorNoneNoOp");
            // Armor=2: a hit deals 2 less (crit and non-crit alike — Armor is a flat, perk-independent cut).
            if (HardenedReduce(armored, 7, false) != 5) fails.Add("armorFlat");
            if (HardenedReduce(armored, 7, true)  != 5) fails.Add("armorFlatCrit");
            // Floor: armor can never drop a hit below 1 (the guaranteed-damage floor holds).
            if (HardenedReduce(armored, 1, false) < 1) fails.Add("armorFloor");
            var bigArmor = new Unit { Team = Team.Player, Hp = 20, MaxHp = 20, Armor = Unit.ArmorMax };
            if (HardenedReduce(bigArmor, 2, false) < 1) fails.Add("armorMaxFloor");
            // Armor STACKS on top of Hardened: 9-dmg crit, Armor=2 + Hardened (-HardenedFlat -HardenedCrit).
            var armHard = new Unit { Team = Team.Player, Hp = 20, MaxHp = 20, Armor = 2 };
            armHard.Perks.Add(Perk.Hardened);
            int expectStack = Math.Max(1, 9 - 2 - Unit.HardenedFlat - Unit.HardenedCrit);
            if (HardenedReduce(armHard, 9, true) != expectStack) fails.Add("armorStacksHardened");
            // Armor reduces MORE than no armor on the same defender profile (sanity: it actually helps).
            if (HardenedReduce(armored, 9, false) >= HardenedReduce(plain, 9, false)) fails.Add("armorActuallyReduces");
        }

        // BULWARK: an extra flat reduction WHILE HUNKERED (read in HardenedReduce off d.Hunkered).
        // Verify it fires only when hunkered, stacks with armor/Hardened, and respects the floor.
        {
            var blw = new Unit { Team = Team.Player, Hp = 20, MaxHp = 20 };
            blw.Perks.Add(Perk.Bulwark);
            // Not hunkered: Bulwark is inert (a normal pass-through, no perk effect).
            blw.Hunkered = false;
            if (HardenedReduce(blw, 7, false) != 7) fails.Add("bulwarkRestingInert");
            // Hunkered: -BulwarkFlat off the hit.
            blw.Hunkered = true;
            if (HardenedReduce(blw, 7, false) != 7 - Unit.BulwarkFlat) fails.Add("bulwarkHunkered");
            // Hunkered Bulwark must reduce MORE than the same hit when standing (the whole point).
            blw.Hunkered = false; int standing = HardenedReduce(blw, 7, false);
            blw.Hunkered = true;  int braced   = HardenedReduce(blw, 7, false);
            if (braced >= standing) fails.Add("bulwarkBracedStronger");
            // Stacks with Armor while hunkered, still floored at 1.
            var blwArm = new Unit { Team = Team.Player, Hp = 20, MaxHp = 20, Armor = 2, Hunkered = true };
            blwArm.Perks.Add(Perk.Bulwark);
            if (HardenedReduce(blwArm, 9, false) != Math.Max(1, 9 - 2 - Unit.BulwarkFlat)) fails.Add("bulwarkStacksArmor");
            if (HardenedReduce(blwArm, 1, false) < 1) fails.Add("bulwarkFloor");
        }

        // VANGUARD: +crit ONLY vs a target that is BOTH flanked AND adjacent (dist <= VanguardRange).
        // A pure ComputeOdds read; verify it fires under both conditions and is inert otherwise.
        {
            // local builders: a Vanguard-perked attacker and a plain one, both at attacker-X = ax.
            Unit VanU(int ax) { var u = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = ax, Y = 5 }; u.Perks.Add(Perk.Vanguard); return u; }
            Unit PlainU(int ax) => new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = ax, Y = 5 };

            // Adjacent + flanked: target at (10,5) with cover on its EAST side (x=11) -> flanked; attacker
            // due west at (9,5) is dist 1. Vanguard fires.
            var gAdj = new Grid(); gAdj.Tiles[11, 5] = TileType.HighCover;
            var adjFlankFoe = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 10, Y = 5, Hp = 10, MaxHp = 10 };
            var vanAdj = ComputeOdds(gAdj, VanU(9), adjFlankFoe);
            if (!vanAdj.Flanked) fails.Add("vanguardAdjSetup");           // guard: it really is a flank
            if (vanAdj.CritChance != Util.Clamp(ComputeOdds(gAdj, PlainU(9), adjFlankFoe).CritChance + Unit.VanguardCrit, 0, 100)) fails.Add("vanguardAdjFlankFires");

            // Flanked but NOT adjacent (dist 3): same flank setup, attacker at (7,5). Vanguard must NOT fire.
            var farFlankFoe = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 10, Y = 5, Hp = 10, MaxHp = 10 };
            var vanFar = ComputeOdds(gAdj, VanU(7), farFlankFoe);
            if (!vanFar.Flanked) fails.Add("vanguardFarSetup");           // guard: still a flank, just at range
            if (vanFar.CritChance != ComputeOdds(gAdj, PlainU(7), farFlankFoe).CritChance) fails.Add("vanguardFarNoOp");

            // Adjacent but NOT flanked (open-ground exposed foe, no cover): attacker at (9,5), foe at (10,5).
            // Vanguard must NOT fire (it needs the genuine flank, distinguishing it from Point Blank).
            var gVan = new Grid();
            var adjExposed = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 10, Y = 5, Hp = 10, MaxHp = 10 };
            var vanExp = ComputeOdds(gVan, VanU(9), adjExposed);
            if (vanExp.Flanked) fails.Add("vanguardExposedSetup");        // guard: adjacent but NOT a flank
            if (vanExp.CritChance != ComputeOdds(gVan, PlainU(9), adjExposed).CritChance) fails.Add("vanguardExposedNoOp");
        }

        return fails.Count == 0
            ? "COMBATTEST: PASS (cover A-E + high-ground + tier-2 + drone/shield + ambush + graze + streak + perk-balance + build-perks + fragile-floor + armor + bulwark + vanguard all hold)"
            : "COMBATTEST: FAIL (" + string.Join(",", fails) + ")";
    }
}
