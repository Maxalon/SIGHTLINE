using System;

namespace Sightline;

/// Enemy FACTIONS (Phase 4 foundation). A mission's hostiles all belong to ONE faction, which
/// (a) restricts the spawn pool (Mission.FactionRoster) and (b) warps the combat math via a
/// faction RULE that bends POSITIONING (not flat stats). The rule is an ENEMY-only trait — a
/// player attacker is NEVER affected. `None` is the default and means EXACTLY today's behavior
/// (the safety invariant: until Game.SetupMission sets Combat.MissionFaction, nothing changes).
///   - Syndicate (tech/mechanized): an enemy attacker SEES OVER the target's LOW cover (negate it,
///     like high ground) — counter with HIGH cover / elevation / staying mobile.
///   - Legion (shock assault): an enemy attacker within close range (dist <= 4) gets +aim AND +crit
///     (a closing-rush reward) — counter by kiting / killing them before they reach you.
///   - Wardens (precision/control): an enemy attacker at long range (dist >= Unit.LongRange) gets
///     +aim (precision back-line) — counter by closing / breaking line of sight.
public enum Faction { None, Syndicate, Legion, Wardens }

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
    public bool Crossfire;   // target caught in converging fire from two diverging angles
    public bool Marked;      // target designated by a sharpshooter MARK (squad-wide +aim/+crit)
    // ---- visible randomness-mitigation surfacing (S2-A graze + S4-C streak) ----
    // These mirror EXISTING hidden mechanics so the HUD can show the player the safety nets
    // (DESIGN.md 3B: reduce %-to-hit save-scum). They DO NOT change the math: HitChance above
    // stays pure (the streak bonus is applied only inside Resolve's effHit, never here).
    public int StreakBonus;  // S4-C: hidden +aim this soldier has banked from consecutive misses (0..MaxStreakBonus)
    public int GrazeFloor;   // S2-A: guaranteed damage a near-miss (graze) would still deal to THIS target (>=1)
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

    // Sharpshooter "Mark" ability (focus-fire designator): EVERY squad member's shot vs the marked
    // foe lands easier + crits harder. The flag lives on the target (Unit.Marked), set by the
    // sharpshooter and cleared at the marker's next turn — a squad-wide "everyone shoot THIS one".
    public const int MarkAim  = 10;
    public const int MarkCrit = 0;

    // SHOVE (forced-movement verb): when a shoved enemy can't move (destination blocked by a
    // wall, cover, another unit, or the board edge) it slams the obstacle and takes this much
    // collision damage instead of repositioning; the unit it was rammed INTO takes the lesser
    // amount. Both run through Game.EnvDamage so the guaranteed-damage floor + kill handling
    // apply (final damage is always >= 1). Small on purpose: shove is a setup/expose verb, the
    // collision is a consolation, not a primary damage source.
    public const int ShoveCollisionDamage = 2;   // dealt to the shoved enemy on a blocked shove
    public const int ShoveRammedDamage    = 1;   // dealt to a unit the shoved enemy was rammed into

    // ---- run-scoped BOONS (Wave 3) ----
    // The active run's boons, set once per mission by Game.SetupMission (like Stats.Enabled), so the
    // static combat reads can see them without threading run state through every ComputeOdds call.
    // Player-only: each read gates on the relevant unit's Team so an enemy never gets a player boon.
    public static System.Collections.Generic.HashSet<Boon> RunBoons = new();
    static bool HasRunBoon(Boon b) => RunBoons.Contains(b);
    public const int BoonMarksAim  = 12;   // MARKSMEN: +aim at long range
    public const int BoonFervorCrit = 30;  // FERVOR: +crit on overwatch reactions
    public const int BoonExecCrit  = 20;   // EXECUTIONERS: +crit vs sub-half-HP targets

    // ---- CROSSFIRE (Wave 2): the live unit roster (both teams), set once per mission by Game.SetupMission
    // (like RunBoons) so ComputeOdds can see an attacker's squadmates without a signature change.
    // Defaults empty -> crossfire is simply inert until Game populates it (existing callers/tests unaffected).
    public static System.Collections.Generic.IReadOnlyList<Unit> AllUnits = System.Array.Empty<Unit>();
    // Two attackers firing on one target from sufficiently DIFFERENT angles catch it in a crossfire:
    // it can't use cover/position against both at once, and it's pinned/distracted. Rewards pincering
    // (spreading the squad to flanking angles) over stacking a single firing line. Symmetric — BOTH
    // teams earn it when they converge, so it also gives counterplay to the enemy AI's flank-positioning.
    public const int CrossfireAim  = 10;   // converging fire: harder for the target to use cover/position
    public const int CrossfireCrit = 10;
    // Crossfire fires when the two firing vectors diverge by > ~72.5 degrees (cosine < this threshold)...
    const float CrossfireCosMax = 0.30f;
    // ...and the converging ally is a credible threat (has LoS and is within this range of the target).
    const float CrossfireAllyRange = 10f;

    // ---- enemy FACTIONS (Wave 4 foundation) ----
    // The active mission's enemy faction, set once per mission by Game.SetupMission (mirrors the
    // RunBoons/AllUnits static pattern) so the static combat reads see it without threading state
    // through every ComputeOdds call. DEFAULT None == today's behavior exactly (safety invariant).
    // Each faction read below gates on `a.Team == Team.Enemy` so a PLAYER attacker is never warped.
    public static Faction MissionFaction = Faction.None;

    // ---- faction COUNTER-PREP (one-mission, bought at the barracks requisition) ----
    // The player can pre-empt a telegraphed faction with a one-mission counter (Run.PrepFaction,
    // copied here by Game.SetupMission, cleared at mission end). Static like MissionFaction so the
    // ComputeOdds reads see it without a signature change. DEFAULT None == today's behavior exactly
    // (safety invariant). The counter ONLY bites when PrepFaction == MissionFaction (an honest bet):
    //  - vs SYNDICATE -> HARDENED OPTICS: the squad's LOW cover can't be seen over (cancel see-over-low).
    //  - vs LEGION    -> REACTIVE PLATING: squad-wide +PrepLegionArmor damage reduction (HardenedReduce).
    //  - vs WARDENS   -> FIELD SMOKE: cancel the Wardens long-range aim bonus (break the long sightline).
    public static Faction PrepFaction = Faction.None;
    public const int PrepLegionArmor = 1;   // REACTIVE PLATING: -1 dmg/hit to every soldier next mission

    // Anti-turtle PRESSURE CLOCK (camp-friendly objectives only): a small, telegraphed enemy
    // accuracy bonus that ramps after a grace period, so sitting in overwatch gets strictly
    // worse over time. Static like MissionFaction/RunBoons so ComputeOdds reads it without a
    // signature change. Game.UpdatePressure sets it (enemy attacker only); reset to 0 each
    // mission. DEFAULT 0 == today's behavior exactly (safety invariant).
    public static int PressureAim = 0;

    // Legion (shock assault): a closing enemy within close range hits harder. Modest — these stack
    // with the whole existing model, so kept small to avoid a swingy point-blank one-shot.
    public const int LegionCloseAim  = 12;   // +aim   for a Legion enemy attacker at dist <= 4
    public const int LegionCloseCrit = 12;   // +crit  for a Legion enemy attacker at dist <= 4
    // Wardens (precision/control): a back-line enemy at long range aims truer.
    public const int WardenLongAim   = 12;   // +aim   for a Wardens enemy attacker at dist >= Unit.LongRange

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

        // SYNDICATE faction (enemy attacker only): "cover won't save you" — a Syndicate enemy
        // attacker sees over the target's LOW cover exactly as high ground does (negate it; HIGH
        // cover still blocks). Folds into seesOver below. Enemy-only (a.Team) so the player's own
        // shots are never warped. None (the default) leaves this false => today's behavior.
        bool syndicateLowSee = a.Team == Team.Enemy && MissionFaction == Faction.Syndicate && cover.Level == 1;
        // COUNTER-PREP vs SYNDICATE (HARDENED OPTICS): the squad's cover discipline denies the
        // see-over-low this mission. No-op unless the matching prep was bought (safety invariant).
        if (syndicateLowSee && PrepFaction == Faction.Syndicate) syndicateLowSee = false;

        // high ground sees over LOW cover; a commanding 2-tier advantage sees over HIGH
        // cover too (firing down negates the target's cover; it reads as fully exposed).
        bool seesOver = ignoresCover || syndicateLowSee || (highGround && (cover.Level == 1 || heightAdv >= 2));
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
        if (a.Pinned > 0) hit -= SuppressAim;    // gunner SUPPRESSING FIRE: pinned foe shoots wild (area denial)
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
        // MARK (sharpshooter focus-fire designator, player attacker vs a marked foe): the whole
        // squad's shots vs the designated target land easier. Flat (a situational squad rule, like
        // crossfire), so it composes cleanly with everything else.
        bool marked = a.Team == Team.Player && d.Marked;
        if (marked) hit += MarkAim;
        // run boon (player attacker): MARKSMEN sharpens the squad's long shots
        if (a.Team == Team.Player && RunBoons.Count > 0 && HasRunBoon(Sightline.Boon.Marksmen) && dist >= Unit.LongRange) hit += BoonMarksAim;
        // CROSSFIRE (symmetric, both teams): a target converged on from two diverging angles can't use
        // cover/position against both and is pinned/distracted -> the attacker's shot lands easier.
        bool crossfire = InCrossfire(grid, a, d);
        if (crossfire) hit += CrossfireAim;
        // enemy FACTION aim rules (enemy attacker only; None = no-op): LEGION rewards closing in,
        // WARDENS rewards holding the back line. Applied before the hit clamp.
        if (a.Team == Team.Enemy)
        {
            if (MissionFaction == Faction.Legion  && dist <= Unit.CloseRange) hit += LegionCloseAim;
            // COUNTER-PREP vs WARDENS (FIELD SMOKE): the opening smoke screen breaks the long
            // sightline, so the Wardens long-range aim bonus is denied this mission (gated so it's
            // a no-op unless the matching prep was bought).
            if (MissionFaction == Faction.Wardens && dist >= Unit.LongRange && PrepFaction != Faction.Wardens) hit += WardenLongAim;
            hit += PressureAim;   // anti-turtle pressure clock: escalating enemy accuracy on camp-friendly objectives
        }
        hit = Util.Clamp(hit, 3, 95);

        // BASE crit: the weapon's intrinsic crit + the exposed situational bonus. Every OPTIONAL crit
        // source (ambush / high-ground / braced + the crit PERKS / traits / boons) is summed FLAT below
        // — no diminishing-returns curve — so the displayed crit% is a predictable sum the player can
        // reason about (the legibility fix; the old DampedCritStack made the number unpredictable).
        // The exposed bonus is +18 (was +35): a smaller, still-meaningful punish for an out-of-cover
        // foe. Cutting it shifts the reward for positioning toward HIT% (cover defense lowers their
        // hit) and away from a rote expose-then-crit dominant strategy — crit becomes a build payoff,
        // not a free reward for any uncovered shot.
        int crit = a.Weapon.CritBase;
        if (coverLevel == 0) crit += 18;        // exposed / flanked target (base situational)

        var critBonuses = new System.Collections.Generic.List<int>();
        void AddCrit(int v) { if (v > 0) critBonuses.Add(v); }

        if (a.FiredFromConcealment) AddCrit(AmbushCrit);  // ambush bonus: caught off-guard
        if (highGround) AddCrit(HighGroundCrit);  // shooting down rewards crits
        if (a.Steady) AddCrit(SteadyCrit);        // braced shot also crits harder
        // Executioner: a FINISHER — +crit only vs sub-half-HP prey (the opposite end from First Strike).
        if (a.HasPerk(Perk.Executioner) && d.MaxHp > 0 && d.Hp * 2 < d.MaxHp) AddCrit(Unit.ExecutionerCrit);
        // First Strike (enum member GiantSlayer, reworked): an ALPHA-STRIKE/OPENER — +crit vs a target
        // still at FULL HP. Rewards focus-firing a FRESH enemy (the first shot that connects); it stops
        // helping the instant the target is chipped, so it pairs with picking targets, not finishing them
        // (the opposite end from Executioner's sub-half-HP crit). Executioner vs First Strike is the
        // kept, build-defining, mutually-exclusive crit PAIR — a real choice, not a redundant stack.
        if (a.HasPerk(Perk.GiantSlayer) && d.MaxHp > 0 && d.Hp >= d.MaxHp) AddCrit(Unit.FirstStrikeCrit);
        // Guardian: overwatch LETHALITY. A reaction shot (ReactedThisTurn is set by Game right before
        // it Resolves this shot) crits hard — Reflexes makes overwatch reliable, Guardian makes it lethal.
        if (a.HasPerk(Perk.Guardian) && IsOverwatchReaction(a)) AddCrit(Unit.GuardianReactCrit);
        if (a.HasTrait(Trait.ColdBlood) && a.MaxHp > 0 && a.Hp * 2 <= a.MaxHp) AddCrit(Unit.ColdBloodCrit);
        // run boons (player attacker): FERVOR makes overwatch lethal; EXECUTIONERS finishes the wounded
        if (a.Team == Team.Player && RunBoons.Count > 0)
        {
            if (HasRunBoon(Sightline.Boon.Fervor) && IsOverwatchReaction(a)) AddCrit(BoonFervorCrit);
            if (HasRunBoon(Sightline.Boon.Executioners) && d.MaxHp > 0 && d.Hp * 2 < d.MaxHp) AddCrit(BoonExecCrit);
        }
        foreach (var b in critBonuses) crit += b;   // FLAT sum: crit% is a predictable total

        // Crossfire + faction crit stay FLAT (outside the damped stack): they're symmetric/enemy
        // situational rules whose self-tests assert an exact +CrossfireCrit / +LegionCloseCrit delta.
        if (crossfire) crit += CrossfireCrit;   // converging fire also crits harder (target distracted/exposed)
        if (marked) crit += MarkCrit;           // designated foe: the whole squad crits it harder (flat, like crossfire)
        // enemy FACTION crit rule (enemy attacker only; None = no-op): LEGION's closing rush also
        // crits harder within close range. Applied before the crit clamp (and before the hunker zero).
        if (a.Team == Team.Enemy && MissionFaction == Faction.Legion && dist <= Unit.CloseRange) crit += LegionCloseCrit;
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
            Crossfire = crossfire,
            Marked = marked,
            // Surface the hidden safety nets for the tooltip (no math change — purely informational):
            //  - StreakBonus mirrors the player-only streak-breaker that Resolve folds into effHit.
            //  - GrazeFloor is the guaranteed damage a near-miss (graze) would still deal to THIS
            //    defender = min weapon damage after the defender's flat reduction, floored at 1
            //    (exactly Resolve's graze branch). FragileFloor only ever CAPS damage, so it can't
            //    lower this guaranteed minimum.
            StreakBonus = a.Team == Team.Player ? Math.Min(StreakBonusPerMiss * a.ConsecutiveMisses, MaxStreakBonus) : 0,
            GrazeFloor = Math.Max(1, HardenedReduce(d, a.Weapon.DmgMin, crit: false)),
        };
    }

    /// True when attacker `a` shooting defender `d` is a CROSSFIRE: at least one OTHER living
    /// same-team COMBATANT has line of sight to `d` AND threatens it from a sufficiently
    /// different angle (the two firing vectors diverge by > ~72.5 degrees). Excludes the attacker
    /// itself, the dead, and non-combatant assets (Cls=="VIP"). Reads Combat.AllUnits.
    /// Side-effect-free + public so the HUD/Renderer can reuse it for an indicator. O(allies): a
    /// single early-return loop over AllUnits, no allocations (squads are <=6, AllUnits <= ~18).
    public static bool InCrossfire(Grid grid, Unit a, Unit d)
    {
        if (a == null || d == null || grid == null) return false;
        var all = AllUnits;
        if (all == null || all.Count == 0) return false;

        // v1 = the attacker's firing vector toward the target (target - attacker).
        float v1x = d.X - a.X, v1y = d.Y - a.Y;
        float len1Sq = v1x * v1x + v1y * v1y;
        if (len1Sq <= 0f) return false;                    // attacker is on the target's tile
        float len1 = MathF.Sqrt(len1Sq);

        for (int i = 0; i < all.Count; i++)
        {
            var ally = all[i];
            if (ally == null || ally == a) continue;       // not the attacker itself
            if (!ally.Alive) continue;                      // dead don't converge fire
            if (ally.Team != a.Team) continue;              // same team only
            if (ally.Cls == "VIP") continue;                // non-combatant asset (VIP / caged captive)

            // v2 = the ally's firing vector toward the same target.
            float v2x = d.X - ally.X, v2y = d.Y - ally.Y;
            float len2Sq = v2x * v2x + v2y * v2y;
            if (len2Sq <= 0f) continue;                     // ally is on the target's tile

            // Credible threat: within range and with an actual line of sight to the target.
            if (Util.TileDist(ally.X, ally.Y, d.X, d.Y) > CrossfireAllyRange) continue;
            if (!grid.HasLineOfSight(ally.X, ally.Y, d.X, d.Y)) continue;

            // Angle test: cosine of the angle between the two firing vectors. A small cosine means a
            // wide angle (the shots come from very different bearings) -> a genuine crossfire.
            float cos = (v1x * v2x + v1y * v2y) / (len1 * MathF.Sqrt(len2Sq));
            if (cos < CrossfireCosMax) return true;
        }
        return false;
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
    ///   - BULWARK perk ("PLATING"): an extra -BulwarkFlat off every hit WHILE the defender is at/above
    ///     HALF HP — ablative armor that's intact while healthy and spent once chipped below half (a
    ///     durability curve, no stance required; distinct from Hardened's always-on/crit-weighted cut).
    /// ALWAYS floored at 1, so the guaranteed-damage floor still holds: every hit deals >= 1 no matter
    /// how much armor/perk reduction stacks. With no armor and no perks this is a pass-through (dmg).
    public static int HardenedReduce(Unit d, int dmg, bool crit)
    {
        int reduce = d.Armor;                                   // persistent shop armor (flat, always)
        if (d.HasPerk(Perk.Hardened))
            reduce += Unit.HardenedFlat + (crit ? Unit.HardenedCrit : 0);  // tank perk: flat + extra vs crit
        if (d.HasPerk(Perk.Bulwark) && d.MaxHp > 0 && d.Hp * 2 >= d.MaxHp)
            reduce += Unit.BulwarkFlat;                         // PLATING: ablative armor while at/above half HP
        if (d.Team == Team.Player && RunBoons.Count > 0 && HasRunBoon(Sightline.Boon.Fortified))
            reduce += 1;                                        // FORTIFIED boon: squad-wide +1 armor
        // COUNTER-PREP vs LEGION (REACTIVE PLATING): squad-wide damage reduction this mission to
        // weather the close-range alpha (only when MissionFaction matches the prep — an honest bet).
        if (d.Team == Team.Player && PrepFaction == Faction.Legion && MissionFaction == Faction.Legion)
            reduce += PrepLegionArmor;
        if (reduce <= 0) return dmg;                            // nothing to subtract: pass through
        return Math.Max(1, dmg - reduce);                       // guaranteed-damage floor (>= 1)
    }

    // ---- reworked-perk RULE PREDICATES (single source of truth, called by Game + COMBATTEST) ----
    // These encode the GATING for two perks whose EFFECT is a Game-side action (an action refund /
    // skipping the overwatch loop) rather than a combat-odds read, so the condition can still be
    // unit-tested in COMBATTEST without duplicating the Game logic.

    /// MOMENTUM (perk Adrenal, reworked): a KILL on the player's own turn refunds the killer +1
    /// action — capped at once per soldier per turn (Game enforces the per-turn cap via the same
    /// _refundedThisTurn guard the flank-kill refund uses, so the two never compound). Distinct from
    /// the universal flank-kill refund: MOMENTUM fires on ANY kill (no flank required), turning a
    /// soldier into an aggressive chainer. Returns true only for a real player combatant with the perk.
    public static bool KillRefundsAction(Unit killer)
        => killer != null && killer.Team == Team.Player && !killer.IsVip && killer.HasPerk(Perk.Adrenal);

    /// OUTRUNNER (perk Sprinter, reworked): this soldier ignores enemy OVERWATCH reaction fire while
    /// moving (Game skips the overwatch loop for its steps, like a Ranger SLIPSTREAM). A mobility VERB
    /// for a flanker who must cross open lanes — no aim/crit/stat, distinct from every other perk.
    public static bool IgnoresOverwatch(Unit mover)
        => mover != null && mover.HasPerk(Perk.Sprinter);

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
            // dist 8 so the rifle's RangeMod is exactly 0 (Soften((8-8)*1.5)) -> effHit == 60, matching the
            // bands below. (At the old dist 4 the +6 range bonus pushed effHit to ~66, sitting on the [53,67]
            // upper edge -> the statistical hit-rate band flaked ~1/150 runs. Recentered, not widened.)
            var gDef = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 11, Y = 5, Hp = 20, MaxHp = 20 };

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

        // PERK BALANCE: the kept, mutually-exclusive crit PAIR is Executioner (finisher: +crit vs
        // sub-half-HP) vs First Strike (opener: +crit vs full-HP). Each fires under its OWN condition
        // and is a no-op otherwise, so the pick is a real choice (one for finishing, one for opening).
        {
            var gP = new Grid();   // no cover -> identical baseline for both perks
            var baseA = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            var fsA   = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            var excA  = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            fsA.Perks.Add(Perk.GiantSlayer);    // FIRST STRIKE
            excA.Perks.Add(Perk.Executioner);

            // Wounded target (below half HP): Executioner's bonus applies, First Strike is inert.
            var wounded = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 2, MaxHp = 10 };
            int baseCritW = ComputeOdds(gP, baseA, wounded).CritChance;
            int excCritW  = ComputeOdds(gP, excA,  wounded).CritChance;
            if (excCritW != baseCritW + Unit.ExecutionerCrit) fails.Add("execBonus");
            if (ComputeOdds(gP, fsA, wounded).CritChance != baseCritW) fails.Add("firstStrikeWoundedNoOp");

            // Healthy target (full HP): First Strike's bonus applies, Executioner is inert.
            var healthy = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 10, MaxHp = 10 };
            int baseCritH = ComputeOdds(gP, baseA, healthy).CritChance;
            if (ComputeOdds(gP, excA, healthy).CritChance != baseCritH) fails.Add("execHealthyNoOp");
            if (ComputeOdds(gP, fsA, healthy).CritChance != baseCritH + Unit.FirstStrikeCrit) fails.Add("firstStrikeHealthyBonus");
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

        // KEPT CRIT PERK (First Strike, enum member GiantSlayer): a pure ComputeOdds read that fires
        // ONLY vs a FULL-HP target and is a no-op once the target is chipped. The opener half of the
        // mutually-exclusive crit pair (Executioner is the finisher, tested above). The four redundant
        // conditional-crit perks (Deadeye / Opportunist / Point Blank / Vanguard) are no longer offered
        // and no longer read by ComputeOdds, so they're intentionally not tested here.
        {
            var gV = new Grid();
            Unit Perked(Perk p) { var u = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 }; u.Perks.Add(p); return u; }
            Unit Plain() => new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };

            // FIRST STRIKE: +crit vs a FULL-HP target; nothing once it's been chipped (same cover/range
            // both times). The alpha-strike / opener perk. Now a FLAT add (no damping), so the delta is exact.
            var fresh   = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 10, MaxHp = 10 };  // full HP -> fires
            var chipped = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 9,  MaxHp = 10 };  // 1 dmg taken -> inert
            int fsFresh = ComputeOdds(gV, Perked(Perk.GiantSlayer), fresh).CritChance;
            int plainFr = ComputeOdds(gV, Plain(),                  fresh).CritChance;
            if (fsFresh != Util.Clamp(plainFr + Unit.FirstStrikeCrit, 0, 100)) fails.Add("firstStrikeFull");
            if (ComputeOdds(gV, Perked(Perk.GiantSlayer), chipped).CritChance != ComputeOdds(gV, Plain(), chipped).CritChance) fails.Add("firstStrikeChippedNoOp");
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

        // BULWARK ("PLATING", reworked): an extra flat reduction off EVERY hit WHILE the defender is
        // at/above HALF HP — ablative armor, no stance required (read in HardenedReduce off d.Hp/d.MaxHp).
        // Verify it fires while healthy, drops off once chipped below half, ignores the hunker stance,
        // stacks with armor/Hardened, and respects the guaranteed-damage floor (>= 1).
        {
            // Healthy (Hp==MaxHp): -BulwarkFlat off the hit (crit and non-crit alike).
            var blwFull = new Unit { Team = Team.Player, Hp = 20, MaxHp = 20 };
            blwFull.Perks.Add(Perk.Bulwark);
            if (HardenedReduce(blwFull, 7, false) != 7 - Unit.BulwarkFlat) fails.Add("bulwarkHealthyFires");
            if (HardenedReduce(blwFull, 7, true)  != 7 - Unit.BulwarkFlat) fails.Add("bulwarkHealthyCritFires");
            // Exactly half HP: boundary is inclusive (at/above half) -> still fires.
            var blwHalf = new Unit { Team = Team.Player, Hp = 10, MaxHp = 20 };
            blwHalf.Perks.Add(Perk.Bulwark);
            if (HardenedReduce(blwHalf, 7, false) != 7 - Unit.BulwarkFlat) fails.Add("bulwarkHalfBoundaryFires");
            // Chipped below half (Hp*2 < MaxHp): the plating is spent -> inert (a plain pass-through).
            var blwLow = new Unit { Team = Team.Player, Hp = 9, MaxHp = 20 };
            blwLow.Perks.Add(Perk.Bulwark);
            if (HardenedReduce(blwLow, 7, false) != 7) fails.Add("bulwarkBelowHalfInert");
            // Healthy must reduce MORE than the same defender once it drops below half (the durability curve).
            if (HardenedReduce(blwFull, 7, false) >= HardenedReduce(blwLow, 7, false)) fails.Add("bulwarkCurve");
            // No longer stance-gated: a HEALTHY non-hunkered Bulwark still fires (the old version needed hunker).
            blwFull.Hunkered = false;
            if (HardenedReduce(blwFull, 7, false) != 7 - Unit.BulwarkFlat) fails.Add("bulwarkNoStanceNeeded");
            // Stacks with Armor while healthy, still floored at 1.
            var blwArm = new Unit { Team = Team.Player, Hp = 20, MaxHp = 20, Armor = 2 };
            blwArm.Perks.Add(Perk.Bulwark);
            if (HardenedReduce(blwArm, 9, false) != Math.Max(1, 9 - 2 - Unit.BulwarkFlat)) fails.Add("bulwarkStacksArmor");
            if (HardenedReduce(blwArm, 1, false) < 1) fails.Add("bulwarkFloor");
        }

        // MOMENTUM (perk Adrenal, reworked): the KillRefundsAction GATE — fires for a player combatant
        // WITH the perk, is a no-op without it / for the VIP / for an enemy (the Game-side per-turn cap
        // + PlayerTurn guard are exercised by SNAPTEST's flank-refund machinery; here we lock the gate).
        {
            var momP = new Unit { Team = Team.Player }; momP.Perks.Add(Perk.Adrenal);
            if (!KillRefundsAction(momP)) fails.Add("momentumPlayerPerkFires");
            var noPerk = new Unit { Team = Team.Player };
            if (KillRefundsAction(noPerk)) fails.Add("momentumNoPerkNoOp");
            var momVip = new Unit { Team = Team.Player, IsVip = true }; momVip.Perks.Add(Perk.Adrenal);
            if (KillRefundsAction(momVip)) fails.Add("momentumVipExcluded");
            var momE = new Unit { Team = Team.Enemy }; momE.Perks.Add(Perk.Adrenal);
            if (KillRefundsAction(momE)) fails.Add("momentumEnemyExcluded");
            if (KillRefundsAction(null)) fails.Add("momentumNullSafe");
        }

        // OUTRUNNER (perk Sprinter, reworked): the IgnoresOverwatch GATE — true only with the perk, so
        // the Game OnUnitEnteredTile loop skips reaction fire for this mover (no aim/crit, pure mobility).
        {
            var outU = new Unit { Team = Team.Player }; outU.Perks.Add(Perk.Sprinter);
            if (!IgnoresOverwatch(outU)) fails.Add("outrunnerPerkFires");
            var plainMover = new Unit { Team = Team.Player };
            if (IgnoresOverwatch(plainMover)) fails.Add("outrunnerNoPerkNoOp");
            if (IgnoresOverwatch(null)) fails.Add("outrunnerNullSafe");
        }

        // (VANGUARD perk cut from the offered pool + its ComputeOdds crit branch removed — the
        // false-choice crit cluster is gone; the kept crit pair is Executioner / First Strike, tested
        // above. The enum member + Name/Code/Desc stay for save compatibility.)

        // CROSSFIRE: a target converged on from two diverging angles (a flanking ally with LoS) reads
        // as InCrossfire -> +CrossfireAim hit / +CrossfireCrit crit; a same-angle ally (no divergence)
        // or no ally at all does NOT. Symmetric (both teams). Sets Combat.AllUnits for the case, then
        // restores it to empty so every other test (which assumes no roster) is unaffected.
        {
            var savedAll = AllUnits;                 // restore at the end no matter what
            var gX = new Grid();                     // empty -> clear LoS for everyone
            // target at (10,5); attacker due WEST at (5,5) -> firing vector points +x.
            var xATk = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 5, Y = 5 };
            var xTgt = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 10, Y = 5, Hp = 10, MaxHp = 10 };
            // a SAME-team ally due SOUTH at (10,10) -> firing vector points -y (90 deg off the attacker): crossfire.
            var xAllyCross = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 10, Y = 10 };
            // a SAME-team ally further WEST at (3,5) -> firing vector also +x (same bearing): NOT a crossfire.
            var xAllySame  = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3,  Y = 5 };
            // an ENEMY at the crossfire position -> different team, must be ignored for the player attacker.
            var xFoeCross  = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 10, Y = 10 };

            // (1) No roster at all -> inert: no crossfire, odds equal a plain (empty-roster) shot.
            AllUnits = System.Array.Empty<Unit>();
            var baseOdds = ComputeOdds(gX, xATk, xTgt);
            if (InCrossfire(gX, xATk, xTgt)) fails.Add("crossfireEmptyRoster");
            if (baseOdds.Crossfire) fails.Add("crossfireEmptyOddsFlag");

            // (2) Attacker + a diverging ally with LoS -> crossfire fires: flag set, +CrossfireAim hit.
            AllUnits = new System.Collections.Generic.List<Unit> { xATk, xTgt, xAllyCross };
            if (!InCrossfire(gX, xATk, xTgt)) fails.Add("crossfirePredicateTrue");
            var crossOdds = ComputeOdds(gX, xATk, xTgt);
            if (!crossOdds.Crossfire) fails.Add("crossfireOddsFlag");
            if (crossOdds.HitChance != Util.Clamp(baseOdds.HitChance + CrossfireAim, 3, 95)) fails.Add("crossfireHit");
            if (crossOdds.CritChance != Util.Clamp(baseOdds.CritChance + CrossfireCrit, 0, 100)) fails.Add("crossfireCrit");

            // (3) Same-angle ally (no angular divergence) -> NOT a crossfire (odds back to the base shot).
            AllUnits = new System.Collections.Generic.List<Unit> { xATk, xTgt, xAllySame };
            if (InCrossfire(gX, xATk, xTgt)) fails.Add("crossfireSameAngle");
            if (ComputeOdds(gX, xATk, xTgt).HitChance != baseOdds.HitChance) fails.Add("crossfireSameAngleNoBonus");

            // (4) The ONLY converging unit is an ENEMY -> ignored for a player attacker (same-team only).
            AllUnits = new System.Collections.Generic.List<Unit> { xATk, xTgt, xFoeCross };
            if (InCrossfire(gX, xATk, xTgt)) fails.Add("crossfireWrongTeam");

            // (5) Symmetric: the ENEMY gets a crossfire too when ITS allies converge. Attacker = the foe,
            // target = a player unit, with a second enemy at a diverging angle.
            var eAtk = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 5, Y = 5 };
            var pTgt = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 10, Y = 5, Hp = 10, MaxHp = 10 };
            var eAlly = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 10, Y = 10 };
            AllUnits = new System.Collections.Generic.List<Unit> { eAtk, pTgt, eAlly };
            if (!InCrossfire(gX, eAtk, pTgt)) fails.Add("crossfireEnemySymmetric");

            // (6) The diverging ally is a VIP (non-combatant asset) -> does NOT count as a converging gun.
            var vipAlly = new Unit { Aim = 45, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 10, Y = 10, Cls = "VIP" };
            AllUnits = new System.Collections.Generic.List<Unit> { xATk, xTgt, vipAlly };
            if (InCrossfire(gX, xATk, xTgt)) fails.Add("crossfireVipExcluded");

            // (7) The diverging ally is too FAR (beyond CrossfireAllyRange) -> not a credible threat.
            var farAlly = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 10, Y = 24 };  // dist 19 south of target
            AllUnits = new System.Collections.Generic.List<Unit> { xATk, xTgt, farAlly };
            if (InCrossfire(gX, xATk, xTgt)) fails.Add("crossfireOutOfRange");

            // (8) The diverging ally's LoS is BLOCKED (high cover between it and the target) -> no crossfire.
            var gBlock = new Grid();
            gBlock.Tiles[10, 8] = TileType.HighCover;     // wall between (10,10) ally and (10,5) target
            AllUnits = new System.Collections.Generic.List<Unit> { xATk, xTgt, xAllyCross };
            if (InCrossfire(gBlock, xATk, xTgt)) fails.Add("crossfireBlockedLoS");

            AllUnits = savedAll;                          // restore (back to empty) for every later/other test
        }

        // ENEMY FACTIONS (Phase 4 foundation): each faction RULE warps an ENEMY attacker's odds and
        // NEVER a player attacker's; with MissionFaction==None nothing changes (the safety invariant).
        // We flip MissionFaction case-by-case and ALWAYS restore it to None at the end so the global
        // static can't leak into any later test or into runtime.
        {
            var gFac = new Grid();
            // baselines captured with MissionFaction==None (today's math) so the deltas are exact.

            // ---- LEGION: an enemy attacker within close range (dist<=4) gets +aim AND +crit; a
            // player attacker at the same range gets NEITHER (factions are an enemy-only trait). ----
            var legEnemy  = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 3, Y = 5 };
            var legTarget = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 5, Y = 5, Hp = 10, MaxHp = 10 }; // dist 2 (close)
            var legPlayer = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            var legFoe    = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 5, Y = 5, Hp = 10, MaxHp = 10 };
            MissionFaction = Faction.None;
            int legEnemyBaseHit  = ComputeOdds(gFac, legEnemy,  legTarget).HitChance;
            int legEnemyBaseCrit = ComputeOdds(gFac, legEnemy,  legTarget).CritChance;
            int legPlayerBaseHit = ComputeOdds(gFac, legPlayer, legFoe).HitChance;
            MissionFaction = Faction.Legion;
            var legEnemyOdds  = ComputeOdds(gFac, legEnemy,  legTarget);
            var legPlayerOdds = ComputeOdds(gFac, legPlayer, legFoe);
            if (legEnemyOdds.HitChance  != Util.Clamp(legEnemyBaseHit  + LegionCloseAim,  3, 95)) fails.Add("legionEnemyAim");
            if (legEnemyOdds.CritChance != Util.Clamp(legEnemyBaseCrit + LegionCloseCrit, 0, 100)) fails.Add("legionEnemyCrit");
            if (legPlayerOdds.HitChance != legPlayerBaseHit) fails.Add("legionPlayerUnaffected");
            // Legion must NOT fire at long range (dist >= LongRange): an enemy far from the target.
            var legFarEnemy = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 0, Y = 5 }; // dist 10 from (10,5)
            var legFarTgt   = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 10, Y = 5, Hp = 10, MaxHp = 10 };
            MissionFaction = Faction.None;   int legFarBase = ComputeOdds(gFac, legFarEnemy, legFarTgt).HitChance;
            MissionFaction = Faction.Legion; int legFarOn   = ComputeOdds(gFac, legFarEnemy, legFarTgt).HitChance;
            if (legFarOn != legFarBase) fails.Add("legionFarNoOp");

            // ---- SYNDICATE: an enemy attacker negates a LOW-cover target (CoverLevel 1 -> 0); a
            // player attacker against the same low-cover target keeps the cover. Geometry: attacker
            // EAST (X=8) of the target (5,5) with low cover on the target's EAST (facing) side (6,5),
            // mirroring the high-ground baseline at the top of this test, so the cover really faces
            // the shot (CoverLevel==1) rather than reading as a flank. ----
            var gSyn = new Grid(); gSyn.Tiles[6, 5] = TileType.LowCover;   // low cover on the target's facing (east) side
            var synEnemy  = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 8, Y = 5 };
            var synTarget = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 5, Y = 5, Hp = 10, MaxHp = 10 };
            MissionFaction = Faction.None;
            if (ComputeOdds(gSyn, synEnemy, synTarget).CoverLevel != 1) fails.Add("syndicateBaselineLow");  // guard: it really is low cover
            int synBaseHit = ComputeOdds(gSyn, synEnemy, synTarget).HitChance;                              // hit WITH the low cover
            MissionFaction = Faction.Syndicate;
            var synOdds = ComputeOdds(gSyn, synEnemy, synTarget);
            if (synOdds.CoverLevel != 0) fails.Add("syndicateNegatesLow");
            if (!synOdds.SeesOver)       fails.Add("syndicateSeesOver");
            if (synOdds.HitChance <= synBaseHit) fails.Add("syndicateHitBetter");   // negating low cover must raise the hit
            // HIGH cover must STILL block a Syndicate enemy attacker (the rule only negates LOW cover).
            var gSynHigh = new Grid(); gSynHigh.Tiles[6, 5] = TileType.HighCover;
            if (ComputeOdds(gSynHigh, synEnemy, synTarget).CoverLevel != 2) fails.Add("syndicateKeepsHigh");
            // A PLAYER attacker is unaffected by Syndicate: low cover still protects the (enemy) target.
            var gSynP = new Grid(); gSynP.Tiles[6, 5] = TileType.LowCover;
            var synPlayer = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 8, Y = 5 };
            var synFoe    = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 5, Y = 5, Hp = 10, MaxHp = 10 };
            if (ComputeOdds(gSynP, synPlayer, synFoe).CoverLevel != 1) fails.Add("syndicatePlayerUnaffected");

            // ---- WARDENS: an enemy attacker at long range (dist >= LongRange) gets +aim; a player
            // attacker at the same range gets nothing; a CLOSE enemy gets nothing. ----
            var gWar = new Grid();
            var warEnemy  = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 1, Y = 5 };
            var warTarget = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 12, Y = 5, Hp = 10, MaxHp = 10 }; // dist 11 (long)
            var warPlayer = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 1, Y = 5 };
            var warFoe    = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 12, Y = 5, Hp = 10, MaxHp = 10 };
            MissionFaction = Faction.None;
            int warEnemyBase  = ComputeOdds(gWar, warEnemy,  warTarget).HitChance;
            int warPlayerBase = ComputeOdds(gWar, warPlayer, warFoe).HitChance;
            MissionFaction = Faction.Wardens;
            if (ComputeOdds(gWar, warEnemy,  warTarget).HitChance != Util.Clamp(warEnemyBase + WardenLongAim, 3, 95)) fails.Add("wardensEnemyLongAim");
            if (ComputeOdds(gWar, warPlayer, warFoe).HitChance    != warPlayerBase) fails.Add("wardensPlayerUnaffected");
            // A CLOSE Wardens enemy (dist <= 4) gets no bonus (the rule is long-range only).
            var warCloseEnemy = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 3, Y = 5 };
            var warCloseTgt   = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 5, Y = 5, Hp = 10, MaxHp = 10 }; // dist 2
            MissionFaction = Faction.None;    int warCloseBase = ComputeOdds(gWar, warCloseEnemy, warCloseTgt).HitChance;
            MissionFaction = Faction.Wardens; int warCloseOn   = ComputeOdds(gWar, warCloseEnemy, warCloseTgt).HitChance;
            if (warCloseOn != warCloseBase) fails.Add("wardensCloseNoOp");

            // ---- SAFETY INVARIANT: with None restored, an enemy attacker reads identically to the
            // pre-faction baseline (no static leak). ----
            MissionFaction = Faction.None;
            if (ComputeOdds(gFac, legEnemy, legTarget).HitChance  != legEnemyBaseHit)  fails.Add("factionNoneRestoredHit");
            if (ComputeOdds(gFac, legEnemy, legTarget).CritChance != legEnemyBaseCrit) fails.Add("factionNoneRestoredCrit");
        }
        MissionFaction = Faction.None;

        // ---- FACTION COUNTER-PREP: each prep CANCELS its faction's edge, only when the mission's
        // faction matches the prep (an honest, telegraphed bet). PrepFaction defaults None == no-op.
        {
            PrepFaction = Faction.None;   // start clean

            // SYNDICATE prep (HARDENED OPTICS): the see-over-low is denied -> the enemy reads the
            // low cover again (CoverLevel back to 1, hit drops to the no-faction baseline).
            var gSyn2 = new Grid(); gSyn2.Tiles[6, 5] = TileType.LowCover;
            var synEnemy2  = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 8, Y = 5 };
            var synTarget2 = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 5, Y = 5, Hp = 10, MaxHp = 10 };
            MissionFaction = Faction.None;      int synPlainHit = ComputeOdds(gSyn2, synEnemy2, synTarget2).HitChance;  // low cover intact
            MissionFaction = Faction.Syndicate; int synOnHit    = ComputeOdds(gSyn2, synEnemy2, synTarget2).HitChance;  // sees over low
            PrepFaction = Faction.Syndicate;    var synPrep = ComputeOdds(gSyn2, synEnemy2, synTarget2);                 // prep cancels it
            if (synOnHit <= synPlainHit)         fails.Add("prepSyndicateBaselineActive");  // guard: faction really helped first
            if (synPrep.CoverLevel != 1)         fails.Add("prepSyndicateCoverRestored");
            if (synPrep.HitChance  != synPlainHit) fails.Add("prepSyndicateCancels");
            // a NON-matching prep must NOT cancel Syndicate's edge
            PrepFaction = Faction.Legion;
            if (ComputeOdds(gSyn2, synEnemy2, synTarget2).HitChance != synOnHit) fails.Add("prepSyndicateWrongPrepNoOp");
            PrepFaction = Faction.None;

            // WARDENS prep (FIELD SMOKE): the long-range aim bonus is denied (hit back to baseline).
            var gWar2 = new Grid();
            var warEnemy2  = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 1,  Y = 5 };
            var warTarget2 = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 12, Y = 5, Hp = 10, MaxHp = 10 };
            MissionFaction = Faction.None;    int warPlainHit = ComputeOdds(gWar2, warEnemy2, warTarget2).HitChance;
            MissionFaction = Faction.Wardens; int warOnHit    = ComputeOdds(gWar2, warEnemy2, warTarget2).HitChance;
            PrepFaction = Faction.Wardens;    int warPrepHit  = ComputeOdds(gWar2, warEnemy2, warTarget2).HitChance;
            if (warOnHit  <= warPlainHit) fails.Add("prepWardensBaselineActive");
            if (warPrepHit != warPlainHit) fails.Add("prepWardensCancels");
            PrepFaction = Faction.None;

            // LEGION prep (REACTIVE PLATING): a soldier takes PrepLegionArmor less damage, but ONLY
            // when the mission faction is Legion (HardenedReduce gates on both).
            var plainD = new Unit { Team = Team.Player, Hp = 10, MaxHp = 10 };
            MissionFaction = Faction.Legion; PrepFaction = Faction.Legion;
            if (HardenedReduce(plainD, 7, false) != 7 - PrepLegionArmor) fails.Add("prepLegionPlating");
            // wrong mission faction -> no plating (the bet didn't pay off)
            MissionFaction = Faction.Wardens;
            if (HardenedReduce(plainD, 7, false) != 7) fails.Add("prepLegionWrongMissionNoOp");
            // an ENEMY defender is never plated by a player prep
            var enemyD = new Unit { Team = Team.Enemy, Hp = 10, MaxHp = 10 };
            MissionFaction = Faction.Legion;
            if (HardenedReduce(enemyD, 7, false) != 7) fails.Add("prepLegionEnemyUnaffected");
            PrepFaction = Faction.None; MissionFaction = Faction.None;
            if (HardenedReduce(plainD, 7, false) != 7) fails.Add("prepLegionNoneRestored");
        }
        PrepFaction = Faction.None;
        MissionFaction = Faction.None;   // belt-and-braces: never leave the global static set for later tests/runtime

        return fails.Count == 0
            ? "COMBATTEST: PASS (cover A-E + high-ground + tier-2 + drone/shield + ambush + graze + streak + perk-balance + build-perks + fragile-floor + armor + bulwark-plating + momentum + outrunner + vanguard + crossfire + factions + faction-prep all hold)"
            : "COMBATTEST: FAIL (" + string.Join(",", fails) + ")";
    }
}
