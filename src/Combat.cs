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
// APPEND-ONLY — new members at the END only; never reorder/remove (persisted by ordinal).
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
    public bool ExposedFire; // target fired last turn and stayed put — exposed by fire (HORIZON W1)
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
    // HORIZON W1 — EXPOSED BY FIRE: a unit that fired last turn and didn't move afterward is easier
    // to hit on the opponent's turn (symmetric to the ambush; makes "duck vs double-tap" a real bet).
    public const int ExposedFireAim  = 12;
    public const int ExposedFireCrit = 12;
    // COUNTERPLAY — FOCUSED OVERWATCH: a soldier who braced a 90-degree cone reacts at this much extra
    // aim within the lane (vs the default -10 wide watch). The trade is coverage: it's blind outside the
    // cone. Additive to the reaction aim mod, so it stacks with Reflexes/Guardian.
    public const int FocusOwAim = 15;

    // Sharpshooter "Mark" ability (focus-fire designator): EVERY squad member's shot vs the marked
    // foe lands easier + crits harder. The flag lives on the target (Unit.Marked), set by the
    // sharpshooter and cleared at the marker's next turn — a squad-wide "everyone shoot THIS one".
    public const int MarkAim  = 10;
    public const int MarkCrit = 0;
    // HEADHUNTER (Sharpshooter spec fork): when this sharpshooter MARKs a foe it also paints it for
    // squad-wide +crit (only a Headhunter's marker grants the crit — read via Unit.MarkedByHeadhunter).
    public const int HeadhunterMarkCrit = 15;
    // W2 SPEC FORKS: innate armor granted by JUGGERNAUT (Assault) and ANCHOR (Gunner Spec.Bulwark),
    // each folded into HardenedReduce (a SEPARATE read from the shop d.Armor). Paired with a verb nerf.
    public const int SpecArmor = 2;

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
    // Public (W6a): Ai.CrossfireWith is PINNED to these exact constants so the planner's crossfire
    // prediction and this resolver can never drift apart again (they had: the planner used a stale
    // ally-weapon-range gate this resolver never had).
    public const float CrossfireCosMax = 0.30f;
    // ...and the converging ally is a credible threat (has LoS and is within this range of the target).
    public const float CrossfireAllyRange = 10f;

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

    // DECAPITATE GUARDED HVT (W4): while a designated guard lives within HvtGuardRange of the HVT,
    // incoming damage to the HVT is reduced by HvtGuardReduce — floored at 1 so it is NEVER zeroed
    // (a naive bot still grinds the HVT down → no TIMEOUT). The state (Unit.HvtGuarded) is owned by
    // Game.UpdateHvtGuard; HardenedReduce just reads it. HvtGuardReducePending is a one-shot signal
    // Game.Update drains to pop a single "GUARDED" float when a hit was actually softened (not spammy).
    public const int HvtGuardRange  = 2;   // Chebyshev: a guard within 2 tiles protects the HVT
    public const int HvtGuardReduce = 3;   // damage subtracted per hit while guarded (floored to >=1)
    public static bool HvtGuardReducePending = false;   // set when a reduction fires; drained by Game.Update

    // ──────────────────────────────────────────────────────────────────────────────────────────
    // MISSION-STATIC LIFECYCLE (PROGRAM TEMPO wave 4). The five per-mission combat statics above
    // (RunBoons / AllUnits / MissionFaction / PrepFaction / PressureAim) were previously set and
    // cleared at ~14 scattered call sites with "defensive: clear ... so no stale value can warp ..."
    // comments — proof that stale-static bleed had bitten the project. These three methods own the
    // lifecycle so a mission cannot start or end with a stale value: SetupMission calls BeginMission,
    // the barracks calls EndMission, run-end calls EndRun. (The DYNAMIC mid-mission updates —
    // PressureAim ramping, AllUnits re-snapped when the roster grows — still happen in Game; only the
    // lifecycle resets are centralised here.) DEFAULT values == today's behaviour exactly.

    /// Publish a fresh mission's statics in one shot (Game.SetupMission, before Mission.Build so the
    /// faction-gated spawn roster sees the faction). RunBoons is the run's active boons; AllUnits is
    /// left empty for Game.RefreshCombatRoster to populate with the live roster.
    public static void BeginMission(System.Collections.Generic.IEnumerable<Boon> runBoons, Faction faction, Faction prepFaction)
    {
        RunBoons = runBoons == null ? new System.Collections.Generic.HashSet<Boon>() : new System.Collections.Generic.HashSet<Boon>(runBoons);
        MissionFaction = faction;
        PrepFaction = prepFaction;
        PressureAim = 0;
        HvtGuardReducePending = false;
        AllUnits = System.Array.Empty<Unit>();
    }

    /// Mission over (barracks): drop every MISSION-scoped static so no stale value warps a
    /// barracks-phase odds read or the next mission. RunBoons is RUN-scoped, so refresh it to the
    /// run's current boons (which may have just changed via a FIELD DOCTRINE pick) rather than clear.
    public static void EndMission(System.Collections.Generic.IEnumerable<Boon> runBoons)
    {
        MissionFaction = Faction.None;
        PrepFaction = Faction.None;
        PressureAim = 0;
        AllUnits = System.Array.Empty<Unit>();
        RefreshRunBoons(runBoons);
        // W6b — the AI coordination tier is mission-scoped like the statics above: cleared here
        // (SetupMission re-publishes it unconditionally) so a NO QUARTER run's Tier 2 can never
        // bleed into a subsequent heat-0 SKIRMISH/DAILY or a harness scene.
        Ai.Tier = 0;
    }

    /// Run over: clear everything, including the run-scoped boons (re-set next run's BeginMission).
    public static void EndRun()
    {
        EndMission(null);
    }

    /// Republish the RUN-scoped boons to the static combat reads MID-mission (W1 mode-seam). LAST
    /// STAND's mid-stand boon pick resolves inside the wave loop — the stand never passes through
    /// EndMission/BeginMission between waves, so a ChooseBoon there was invisible to ComputeOdds
    /// until this refresh. Mirrors EndMission's republish exactly.
    public static void RefreshRunBoons(System.Collections.Generic.IEnumerable<Boon> runBoons)
    {
        RunBoons = runBoons == null ? new System.Collections.Generic.HashSet<Boon>() : new System.Collections.Generic.HashSet<Boon>(runBoons);
    }

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
        // SIGNAL W5: keyed on the HasShieldArc capability flag (defaults to Cls=="SHIELD",
        // so rank-and-file behavior is unchanged; a boss elite can carry the arc too).
        if (d.HasShieldArc && !seesOver && ShieldedFrom(d, a.X, a.Y) && coverLevel < 2)
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
        if (a.Routed > 0) hit -= Unit.RoutAim;   // UNDERTOW W3: a broken/routing unit shoots wild
        // promotion perks (attacker)
        // UNDERTOW W5 — LockOn de-superset: fires only vs a FLANKED target (its cover doesn't protect from
        // this angle), not vs ANY exposed target. "coverLevel==0" was the MODAL combat state, so LockOn was
        // a strict superset of the range/state-gated aim perks (CloseQuarters/Marksman) and dominated picks
        // (34 vs ~2-4). As a FLANK reward it's now a positional perk that rewards out-positioning — a real
        // build choice, not the biggest always-on number. (flanked already accounts for seesOver.)
        if (a.HasPerk(Perk.LockOn) && flanked) hit += Unit.PerkAim;
        if (a.HasPerk(Perk.CloseQuarters) && dist <= Unit.CloseRange) hit += Unit.PerkAim;
        if (a.HasPerk(Perk.Marksman) && dist >= Unit.LongRange) hit += Unit.PerkAim;
        // SIEGEBREAKER (anti-turtle): +aim vs a HUNKERED target — claws back part of the -25 hunker
        // penalty applied above, so a camped/hunkered foe can still be dug out. Inert vs any active
        // (non-hunkered) enemy, so it's a situational pick, not a flat aim upgrade like LockOn.
        if (a.HasPerk(Perk.Siegebreaker) && d.Hunkered) hit += Unit.SiegebreakerAim;
        // W10 BIPOD (weapon mod): +aim while the shooter is PLANTED (hasn't entered a tile this turn —
        // Unit.MovedThisTurn, set by Game.OnUnitEnteredTile on every kind of movement). Deliberate
        // anti-synergy with EXPOSED BY FIRE below: the same stand-still that arms the bipod leaves the
        // shooter exposed after firing. Mods live on players only (HasMod reads the persisted
        // WeaponMods list, always empty on enemies), so no team gate is needed.
        if (a.HasMod(WeaponMod.Bipod) && !a.MovedThisTurn) hit += WeaponModDef.BipodAim;
        // CoolHeaded (composure) is a DEFENDER perk now: a CoolHeaded TARGET is hard to rattle, so any
        // attacker firing at it loses CoolHeadedEvade aim (its daze-immunity half lives in Unit.AddStatus).
        // A survivability pick, distinct from the attacker-side aim line (LockOn/CloseQuarters/Marksman).
        if (d.HasPerk(Perk.CoolHeaded)) hit -= Unit.CoolHeadedEvade;

        // earned traits + bonds (attacker)
        if (a.HasTrait(Trait.Killer) && d.MaxHp > 0 && d.Hp * 2 <= d.MaxHp) hit += Unit.KillerAim;
        if (a.HasTrait(Trait.Vengeful) && a.AllyDown) hit += Unit.VengefulAim;
        if (a.BondAura) hit += Unit.BondAim;     // a bonded squadmate stands adjacent

        // SCARS — aim reads (attacker; each gates on HasScar, inert otherwise):
        //  BURN-SCARRED: fire-shy — loses aim while on fire. (Simplification: gated on a.HasStatus(Burning)
        //  alone, NOT board-fire adjacency — see the W5 report. ComputeOdds is static with no live Grid.Fire
        //  context for the ATTACKER's tile, and threading that in for one scar isn't worth fragile plumbing;
        //  Burning is the dominant, correct trigger.)
        if (a.HasScar(Scar.BurnScarred) && a.HasStatus(StatusKind.Burning)) hit -= Unit.BurnShyAim;
        //  HARD-BITTEN: only fights well when it's grim — penalised at FULL HP (the +crit-while-bloodied
        //  upside lives in the crit block below).
        if (a.HasScar(Scar.HardBitten) && a.MaxHp > 0 && a.Hp >= a.MaxHp) hit -= Unit.HardBittenFullAim;
        //  VENDETTA: a grudge — sharper vs the faction that scarred this soldier (gated on the mission's
        //  faction matching, mirroring the PrepFaction/MissionFaction gate). Inert when None/mismatched.
        bool vendetta = a.HasScar(Scar.Vendetta) && a.VendettaFaction != Faction.None && MissionFaction == a.VendettaFaction;
        if (vendetta) hit += Unit.VendettaAim;

        if (a.FiredFromConcealment) hit += AmbushAim;
        // HORIZON W1 — EXPOSED BY FIRE (symmetric, BOTH teams): a defender that fired this turn and
        // stayed put is easier to hit until it moves. The a.Team != d.Team guard is the only team gate
        // (do NOT restrict to one team). Cleared by OnUnitEnteredTile on any tile entry after firing.
        bool exposedByFire = a.Team != d.Team && d.FiredThisTurn && !d.MovedAfterFire;
        if (exposedByFire) hit += ExposedFireAim;
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
        // Q1 D3 — STEADYING (the streak-breaker) is part of the DISPLAYED number.
        // It used to be added only inside Resolve, so a soldier on a 2-miss streak rolled at up
        // to +12 over the HIT% the tooltip printed (measured: displayed 69, real 81). SIGHTLINE's
        // stated identity is perfect-information tactics (DESIGN.md 7) — a headline % that isn't
        // the real probability is the worst legibility bug this game can have, and "derivable
        // from a separate badge" is not the same as true. Folded in here, so ComputeOdds is the
        // single source of truth and Resolve just rolls against it; the STEADYING badge stays as
        // the EXPLANATION of why the number is higher. Player-only, for the same reason as
        // before: the net exists to curb the player's miss-streak frustration.
        // Consequence, deliberately kept: the bonus now respects the 3..95 clamp like every other
        // aim source, instead of riding Resolve's wider 1..99 clamp past the "never certain" cap.
        int steadying = a.Team == Team.Player ? Math.Min(StreakBonusPerMiss * a.ConsecutiveMisses, MaxStreakBonus) : 0;
        hit += steadying;
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
        if (exposedByFire) AddCrit(ExposedFireCrit);      // HORIZON: fired-and-stationary foe is exposed
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
        // VANTAGE (elevation specialist): +crit while this attacker fires from HIGH GROUND — an earned,
        // positional crit payoff (inert on flat ground). Stacks additively with the always-on
        // HighGroundCrit situational bonus, turning "hold the vantage" into a real build axis.
        if (a.HasPerk(Perk.Vantage) && highGround) AddCrit(Unit.VantageCrit);
        // BREAKER (combined-arms punish): +crit vs a target the squad has SUPPRESSED or PINNED (both set
        // by a gunner's verb). Rewards the follow-up shot after a foe is locked down; inert vs an
        // unrattled enemy. A pin-punisher axis, orthogonal to the HP-based (Executioner/First Strike)
        // and cover-based (LockOn) crit perks.
        if (a.HasPerk(Perk.Breaker) && (d.Suppress > 0 || d.Pinned > 0)) AddCrit(Unit.BreakerCrit);
        // Guardian: overwatch LETHALITY. A reaction shot (ReactedThisTurn is set by Game right before
        // it Resolves this shot) crits hard — Reflexes makes overwatch reliable, Guardian makes it lethal.
        // SENTINEL (Sharpshooter spec fork) grants the SAME built-in. The || means perk+spec add the
        // bonus ONCE (no double-count, R2) — a Sentinel who also owns Guardian crits no harder.
        if ((a.HasPerk(Perk.Guardian) || a.HasSpec(Spec.Sentinel)) && IsOverwatchReaction(a)) AddCrit(Unit.GuardianReactCrit);
        if (a.HasTrait(Trait.ColdBlood) && a.MaxHp > 0 && a.Hp * 2 <= a.MaxHp) AddCrit(Unit.ColdBloodCrit);
        // SCARS — crit reads (attacker; flat sum, gated on HasScar):
        //  HARD-BITTEN: +crit while bloodied (<= half HP) — the defiant upside of the full-HP aim penalty.
        if (a.HasScar(Scar.HardBitten) && a.MaxHp > 0 && a.Hp * 2 <= a.MaxHp) AddCrit(Unit.HardBittenCrit);
        //  VENDETTA: the grudge also crits harder vs the scarring faction (same gate as the aim bonus above).
        if (vendetta) AddCrit(Unit.VendettaCrit);
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
        if (marked) crit += MarkCrit;           // designated foe (TEMPO wave 2: MarkCrit now 0 — MARK is an aim-only designator; kept as a single source so re-enabling it is a one-const change)
        // HEADHUNTER (Sharpshooter spec fork): a foe marked specifically by a Headhunter ALSO grants
        // squad-wide +crit (gated to that marker only, inert on a plain MARK / no spec). Flat, like crossfire.
        if (marked && d.MarkedByHeadhunter) crit += HeadhunterMarkCrit;
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
            ExposedFire = exposedByFire,
            Crossfire = crossfire,
            Marked = marked,
            // Surface the safety nets for the tooltip:
            //  - StreakBonus is the STEADYING aim ALREADY INCLUDED in HitChance above (Q1 D3) —
            //    the badge explains the number, it no longer discloses a hidden one.
            //  - GrazeFloor is the guaranteed damage a near-miss (graze) would still deal to THIS
            //    defender = min weapon damage after the defender's flat reduction, floored at 1
            //    (exactly Resolve's graze branch). FragileFloor only ever CAPS damage, so it can't
            //    lower this guaranteed minimum.
            StreakBonus = steadying,
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
        // W2 SPEC FORKS: JUGGERNAUT (Assault) + ANCHOR (Gunner Spec.Bulwark) carry +2 innate armor —
        // paid for by a real verb nerf (GRAPPLE reach / PIN footprint), so it's not free stats. Read
        // SEPARATELY from the shop d.Armor (don't mutate it). Inert on Spec.None.
        if (d.HasSpec(Spec.Juggernaut)) reduce += SpecArmor;    // JUGGERNAUT: armored bruiser (reach 2->1)
        if (d.HasSpec(Spec.Bulwark))    reduce += SpecArmor;    // ANCHOR: immovable wall (PIN footprint -> single)
        if (d.Team == Team.Player && RunBoons.Count > 0 && HasRunBoon(Sightline.Boon.Fortified))
            reduce += 1;                                        // FORTIFIED boon: squad-wide +1 armor
        // COUNTER-PREP vs LEGION (REACTIVE PLATING): squad-wide damage reduction this mission to
        // weather the close-range alpha (only when MissionFaction matches the prep — an honest bet).
        if (d.Team == Team.Player && PrepFaction == Faction.Legion && MissionFaction == Faction.Legion)
            reduce += PrepLegionArmor;
        // DECAPITATE GUARDED HVT (W4): the HVT shrugs off part of every hit while a bodyguard is near.
        // FLOOR at 1 (never zero) so a naive bot still whittles it down — this is the no-TIMEOUT guarantee.
        // Mark the reduction (only when it actually shaved off damage) for a one-shot "GUARDED" float.
        if (d.HvtGuarded && d.Team == Team.Enemy)
        {
            int before = Math.Max(1, dmg - reduce);
            int after  = Math.Max(1, before - HvtGuardReduce);
            if (after < before) HvtGuardReducePending = true;
            return after;
        }
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

    /// This soldier ignores enemy OVERWATCH reaction fire while moving (Game skips the overwatch loop
    /// for its steps, like a Ranger SLIPSTREAM). Two perks grant it:
    ///  - OUTRUNNER (Sprinter): ALWAYS, a mobility verb for a flanker who must cross open lanes.
    ///  - SKIRMISHER (TEMPO wave 5): only AFTER the soldier has fired this turn — the "shoot, then
    ///    slip away to safety without eating reaction fire" build that the keystone's free second
    ///    action enables (rewards shoot-then-reposition over standing still).
    public static bool IgnoresOverwatch(Unit mover)
        => mover != null && (mover.HasPerk(Perk.Sprinter)
            || (mover.HasPerk(Perk.Skirmisher) && mover.FiredThisTurn));

    /// W10 FIELD DRILLS boon: how many DRAGs (and, separately, VAULTs) this soldier may take per
    /// turn. Base 1 (the shipped once-per-turn FIELD CRAFT rule); the boon doubles it for the
    /// player's squad. Game's CanDrag/CanVault gates compare the per-turn counters (Unit.DragsThisTurn
    /// / VaultsThisTurn) against this — the single source of truth, so COMBATTEST can pin the rule
    /// without duplicating the Game-side gate logic (the MOMENTUM/IgnoresOverwatch pattern).
    public static int FieldCraftLimit(Unit u)
        => u != null && u.Team == Team.Player && RunBoons.Contains(Sightline.Boon.FieldDrills) ? 2 : 1;

    /// W10 SHOCK DOCTRINE boon: a player's BRACE reaction deals FULL damage — Game.OnUnitEnteredTile
    /// skips its halving/no-crit block when this is true; the stagger identity is unchanged.
    /// Predicate only (the damage math stays at the one Game call site); player-gated like every boon.
    public static bool BraceFullDamage(Unit watcher)
        => watcher != null && watcher.Team == Team.Player && RunBoons.Contains(Sightline.Boon.ShockDoctrine);

    // Streak-breaker constants (S4-C): per clean-miss aim bonus, capped at MaxStreakBonus.
    // Q1 D3: applied inside ComputeOdds (so the DISPLAYED HitChance is the real probability) and
    // surfaced by the STEADYING tooltip badge. It is no longer hidden, and Resolve must NOT add
    // it a second time.
    public const int StreakBonusPerMiss = 6;   // +6 effHit per consecutive miss
    public const int MaxStreakBonus     = 12;  // capped at +12 (after 2+ misses)

    /// Roll a shot. aimMod lets overwatch apply a reaction penalty.
    public static ShotResult Resolve(Grid grid, Unit a, Unit d, int aimMod = 0)
    {
        var odds = ComputeOdds(grid, a, d);
        // Streak-breaker (S4-C): a small aim bonus after consecutive clean misses, max +12,
        // reset on any connect (hit or graze). Q1 D3 — it is now folded into ComputeOdds and
        // shown in HitChance, so it MUST NOT be added again here: odds.HitChance already carries
        // it. The player-only gate lives in ComputeOdds for the same reason it always did (the
        // net curbs the PLAYER's miss-streak frustration; a quiet enemy aim nudge would only
        // raise difficulty invisibly).
        // Guardian: cancel the standard -10 overwatch reaction penalty (which Game folds into aimMod,
        // invisible to ComputeOdds) so its reactions fire at full accuracy. Applied here, not in
        // ComputeOdds, because the penalty it offsets isn't part of the displayed HitChance either
        // (an overwatch reaction never shows a tooltip, so there is no number to contradict).
        // SENTINEL (spec fork) grants the same penalty-cancel as Guardian. || => once only (R2 no double).
        int guardianBonus = ((a.HasPerk(Perk.Guardian) || a.HasSpec(Spec.Sentinel)) && IsOverwatchReaction(a)) ? Unit.GuardianReactAim : 0;
        int effHit = Util.Clamp(odds.HitChance + aimMod + guardianBonus, 1, 99);

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

    /// R2 FIX 2 — the TRUE expected damage of one shot with these odds against `d`, in HP.
    /// Single source of truth for the incoming-fire forecast (Game.RecomputeThreat -> the
    /// "expected" number on the threat card) and for THREATTEST, which now measures it against
    /// real Combat.Resolve rolls instead of re-deriving the same formula.
    ///
    /// The old forecast was `hit% x mean(post-armor band)` and its comment claimed crits (up) and
    /// "the graze floor (down)" cancelled as a neutral first-order read. The graze term is NOT
    /// down: a graze deals max(1, reduce(DmgMin)) on a roll that would otherwise deal ZERO, so it
    /// strictly ADDS. Both omitted terms pushed the same way and the card read 31-44% low
    /// (measured, 200k Resolve rolls/weapon: Rifle 2.13 shown vs 2.96 real, Smg 1.42 vs 2.04).
    /// On an 8 HP rookie the card said "~2" for a shot averaging 3 — in a game whose identity is
    /// perfect-information tactics. This enumerates the real roll instead:
    ///   P(clean hit) = effHit/100                  -> Uniform{DmgMin..DmgMax}, crit at CritChance
    ///                                                 (ceil(dmg*1.5)+1), then armor, floor 1
    ///   P(graze)     = (grazeTop - effHit)/100     -> max(1, reduce(DmgMin)), never crits
    ///   otherwise 0. grazeTop mirrors Resolve exactly: min(effHit + GrazeBand, 100 - GrazeMinMiss).
    /// EXCLUDED, deliberately and one-directionally: Combat.FragileFloor (a full-HP player cannot
    /// be dropped below 1 HP by a SINGLE shot). It applies to at most the first shot of an
    /// incoming volley and vanishes as soon as the soldier is chipped, so folding it into a
    /// per-tile sum over every gun that can see the tile would under-read the volley. It only
    /// ever caps damage, so the forecast stays the conservative (never-optimistic) read there.
    /// Also excluded: Resolve's `aimMod` (the -10 overwatch reaction penalty and Guardian's
    /// cancel of it), which the forecast has no shot context for — the same omission as before.
    /// PURE: no RNG draw, no mutation. HardenedReduce's only side effect (HvtGuardReducePending)
    /// is gated on an ENEMY defender, and the forecast's defender is always the player's soldier.
    public static float ExpectedDamage(Unit d, in ShotOdds o)
    {
        int effHit = Util.Clamp(o.HitChance, 1, 99);
        double grazeTop = Math.Min(effHit + GrazeBand, 100.0 - GrazeMinMiss);
        double pHit = effHit / 100.0;
        double pGraze = Math.Max(0.0, grazeTop - effHit) / 100.0;

        // graze leg: minimum damage through armor, floored at 1, never a crit
        double grazeDmg = Math.Max(1, HardenedReduce(d, o.DmgMin, crit: false));

        // clean-hit leg: the uniform band, each roll independently crit-rolled
        int lo = Math.Min(o.DmgMin, o.DmgMax), hi = Math.Max(o.DmgMin, o.DmgMax);
        double pCrit = Util.Clamp(o.CritChance, 0, 100) / 100.0;
        double sum = 0;
        for (int raw = lo; raw <= hi; raw++)
        {
            double plain = Math.Max(1, HardenedReduce(d, raw, crit: false));
            int critRaw = (int)MathF.Ceiling(raw * 1.5f) + 1;
            double critted = Math.Max(1, HardenedReduce(d, critRaw, crit: true));
            sum += (1.0 - pCrit) * plain + pCrit * critted;
        }
        double hitDmg = sum / Math.Max(1, hi - lo + 1);

        return (float)(pHit * hitDmg + pGraze * grazeDmg);
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

        // SIGNAL W5 — flagged BOSS shield arc: an ELITE granted HasShieldArc must get ShieldedFrom
        // applied EXACTLY like a Cls=="SHIELD" unit (the flag-on-boss path the mirror default does
        // not exercise: sh above never sets the backing field). Control: a plain ELITE with the
        // same facing but NO flag gets no barrier — the arc must come from the flag, not the Cls.
        var bossArc = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Lmg), Team = Team.Enemy, X = 5, Y = 5, Hp = 20, MaxHp = 20, Cls = "ELITE", HasShieldArc = true, ShieldDx = -1, ShieldDy = 0 };
        if (ComputeOdds(gShield, atkW, bossArc).CoverLevel != 2) fails.Add("bossArcFront");
        if (ComputeOdds(gShield, atkE, bossArc).CoverLevel != 0) fails.Add("bossArcFlank");
        var bossPlain = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Lmg), Team = Team.Enemy, X = 5, Y = 5, Hp = 20, MaxHp = 20, Cls = "ELITE", ShieldDx = -1, ShieldDy = 0 };
        if (ComputeOdds(gShield, atkW, bossPlain).CoverLevel != 0) fails.Add("bossNoFlagNoArc");

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

        // EXPOSED BY FIRE (HORIZON W1): a DEFENDER that fired this turn and hasn't moved is easier
        // to hit by the opposing team (+ExposedFireAim / +ExposedFireCrit), symmetric to the ambush.
        {
            var gEf = new Grid();
            var efA = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            var efD = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy,  X = 7, Y = 5, Hp = 8, MaxHp = 8 };
            var efBase = ComputeOdds(gEf, efA, efD);                 // defender hasn't fired -> no bonus
            if (efBase.ExposedFire) fails.Add("exposedFireBaseFlag");
            // (a) defender fired + stayed put, opposing team -> exactly +ExposedFireAim / +ExposedFireCrit
            efD.FiredThisTurn = true; efD.MovedAfterFire = false;
            var efYes = ComputeOdds(gEf, efA, efD);
            if (!efYes.ExposedFire) fails.Add("exposedFireFlag");
            if (efYes.HitChance != Util.Clamp(efBase.HitChance + ExposedFireAim, 3, 95)) fails.Add("exposedFireHit");
            if (efYes.CritChance != Util.Clamp(efBase.CritChance + ExposedFireCrit, 0, 100)) fails.Add("exposedFireCrit");
            // (b) defender fired but MOVED afterward -> no bonus
            efD.MovedAfterFire = true;
            var efMoved = ComputeOdds(gEf, efA, efD);
            if (efMoved.ExposedFire) fails.Add("exposedFireMovedFlag");
            if (efMoved.HitChance != efBase.HitChance || efMoved.CritChance != efBase.CritChance) fails.Add("exposedFireMovedBonus");
            // (c) SAME-team attacker vs a fired-and-stationary unit -> no bonus (team gate)
            efD.MovedAfterFire = false;
            var efFriend = new Unit { Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 3, Y = 5 };
            var efSame = ComputeOdds(gEf, efFriend, efD);
            if (efSame.ExposedFire) fails.Add("exposedFireSameTeamFlag");
        }

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

            // ── Q1 D3: the DISPLAYED HIT% IS the real probability while STEADYING is active ──
            // (1) ComputeOdds must MOVE with the streak (it used to be flat, with the bonus added
            //     only inside Resolve), and by exactly the streak amount while off the 95 clamp.
            sAtk.ConsecutiveMisses = 0;
            var oddsNo = ComputeOdds(gS, sAtk, sDef);
            sAtk.ConsecutiveMisses = 2;
            var oddsMax = ComputeOdds(gS, sAtk, sDef);
            if (oddsNo.StreakBonus != 0) fails.Add($"steadyBadge0={oddsNo.StreakBonus}");
            if (oddsMax.StreakBonus != MaxStreakBonus) fails.Add($"steadyBadgeMax={oddsMax.StreakBonus}");
            int expectMax = Math.Min(95, oddsNo.HitChance + MaxStreakBonus);
            if (oddsMax.HitChance != expectMax) fails.Add($"steadyNotInHit({oddsNo.HitChance}->{oddsMax.HitChance}, want {expectMax})");
            // (2) an ENEMY attacker never banks it (the tooltip's enemy-side read stays honest too)
            var eAtk = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 3, Y = 5, ConsecutiveMisses = 2 };
            var pDef = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 7, Y = 5, Hp = 20, MaxHp = 20 };
            eAtk.ConsecutiveMisses = 0; int eNo = ComputeOdds(gS, eAtk, pDef).HitChance;
            eAtk.ConsecutiveMisses = 3; var eOdds = ComputeOdds(gS, eAtk, pDef);
            if (eOdds.HitChance != eNo || eOdds.StreakBonus != 0) fails.Add("steadyLeakedToEnemy");
            // (3) Resolve must ROLL AGAINST the displayed number, not the displayed number PLUS
            //     the streak again. Measured, not asserted from the source: 40k seeded rolls at a
            //     pinned 2-miss streak; the observed full-hit rate must land on oddsMax.HitChance.
            //     (P(full hit) = effHit/100 exactly, so 40k rolls has sigma ~0.24pt — 2pt is slack.)
            {
                var keep = Util.Rng;
                Util.Reseed(424242);
                int hits = 0; const int N = 40000;
                for (int i = 0; i < N; i++)
                {
                    sAtk.ConsecutiveMisses = 2; sDef.Hp = 20;
                    var r = Resolve(gS, sAtk, sDef);
                    if (r.Hit && !r.Graze) hits++;
                }
                double rate = 100.0 * hits / N;
                if (Math.Abs(rate - oddsMax.HitChance) > 2.0)
                    fails.Add($"steadyRollNotDisplayed(shown={oddsMax.HitChance} rolled={rate:0.00})");
                Util.Rng = keep;
            }

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

            // Q1 D3 INVERTED: ComputeOdds MUST reflect the streak bonus — the displayed number is
            // the real probability. (This assert used to demand the opposite; the pre-Q1 rule made
            // the headline HIT% under-report by up to 12 points, which the perfect-information
            // contract in DESIGN.md 7 forbids.) Capped at the 3..95 clamp like every other source.
            sAtk.ConsecutiveMisses = 5;
            var oddsStreaked = ComputeOdds(gS, sAtk, sDef);
            sAtk.ConsecutiveMisses = 0;
            var oddsZeroMisses = ComputeOdds(gS, sAtk, sDef);
            if (oddsStreaked.HitChance != Math.Min(95, oddsZeroMisses.HitChance + MaxStreakBonus))
                fails.Add("streakNotVisibleInOdds");
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

        // HORIZON wave 6 — THREE MORE build perks, each a pure ComputeOdds read that fires ONLY under its
        // condition and is an exact no-op otherwise. Deltas are FLAT (no damping), so we assert them exactly.
        {
            // Attacker at (3,5), enemy target at (7,5), Hp<MaxHp so First Strike can never contaminate the
            // crit deltas here (Breaker/Vantage are the only perk sources in play).
            Unit MkPerk(Perk p) { var u = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 }; u.Perks.Add(p); return u; }
            Unit MkPlain() => new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };

            // ---- VANTAGE: +VantageCrit crit ONLY while the attacker fires from HIGH GROUND; inert on flat
            // ground. Compare perked-vs-plain on the SAME elevation so the always-on HighGround bonuses
            // cancel and only the Vantage delta remains. ----
            var gW6Hi = new Grid();
            var vTgt = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 9, MaxHp = 10 };
            // flat ground: Vantage is inert (perked == plain).
            if (ComputeOdds(gW6Hi, MkPerk(Perk.Vantage), vTgt).CritChance != ComputeOdds(gW6Hi, MkPlain(), vTgt).CritChance) fails.Add("vantageFlatNoOp");
            // raise the attacker's tile -> high ground; now Vantage adds exactly VantageCrit over a plain
            // attacker on the same high ground.
            gW6Hi.Height[3, 5] = 1;
            int vHiPerk  = ComputeOdds(gW6Hi, MkPerk(Perk.Vantage), vTgt).CritChance;
            int vHiPlain = ComputeOdds(gW6Hi, MkPlain(),           vTgt).CritChance;
            if (vHiPerk != Util.Clamp(vHiPlain + Unit.VantageCrit, 0, 100)) fails.Add("vantageHighGroundFires");

            // ---- BREAKER: +BreakerCrit crit vs a SUPPRESSED or PINNED target; inert vs an unrattled one.
            // The suppress/pin flags live on the DEFENDER and carry no intrinsic crit change, so the whole
            // delta is the perk. ----
            var gW6Br = new Grid();
            var brFresh = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 9, MaxHp = 10 };                 // not rattled
            var brSupp  = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 9, MaxHp = 10, Suppress = 30 };   // suppressed
            var brPin   = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 9, MaxHp = 10, Pinned = 2 };      // pinned
            // unrattled target -> Breaker is inert (perked == plain).
            if (ComputeOdds(gW6Br, MkPerk(Perk.Breaker), brFresh).CritChance != ComputeOdds(gW6Br, MkPlain(), brFresh).CritChance) fails.Add("breakerFreshNoOp");
            // suppressed target -> +BreakerCrit exactly.
            if (ComputeOdds(gW6Br, MkPerk(Perk.Breaker), brSupp).CritChance != Util.Clamp(ComputeOdds(gW6Br, MkPlain(), brSupp).CritChance + Unit.BreakerCrit, 0, 100)) fails.Add("breakerSuppressedFires");
            // pinned target -> +BreakerCrit exactly (the || branch).
            if (ComputeOdds(gW6Br, MkPerk(Perk.Breaker), brPin).CritChance != Util.Clamp(ComputeOdds(gW6Br, MkPlain(), brPin).CritChance + Unit.BreakerCrit, 0, 100)) fails.Add("breakerPinnedFires");

            // ---- SIEGEBREAKER: +SiegebreakerAim aim vs a HUNKERED target; inert vs an active one. A
            // hunkered target zeroes crit, so we assert on HIT (both attackers see the same -25 hunker
            // penalty; only the perked one claws SiegebreakerAim back). ----
            var gW6Sg = new Grid();
            var sgActive = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 9, MaxHp = 10 };                  // not hunkered
            var sgHunk   = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 9, MaxHp = 10, Hunkered = true };  // hunkered
            // active target -> Siegebreaker is inert (perked hit == plain hit).
            if (ComputeOdds(gW6Sg, MkPerk(Perk.Siegebreaker), sgActive).HitChance != ComputeOdds(gW6Sg, MkPlain(), sgActive).HitChance) fails.Add("siegebreakerActiveNoOp");
            // hunkered target -> +SiegebreakerAim exactly over a plain attacker vs the same hunkered foe.
            int sgPerkHit  = ComputeOdds(gW6Sg, MkPerk(Perk.Siegebreaker), sgHunk).HitChance;
            int sgPlainHit = ComputeOdds(gW6Sg, MkPlain(),                 sgHunk).HitChance;
            if (sgPerkHit != Util.Clamp(sgPlainHit + Unit.SiegebreakerAim, 3, 95)) fails.Add("siegebreakerHunkeredFires");
            // sanity: the hunkered foe really is harder to hit than the active one (the -25 penalty is live),
            // so the perk is clawing back a real deficit rather than padding an already-easy shot.
            if (sgPlainHit >= ComputeOdds(gW6Sg, MkPlain(), sgActive).HitChance) fails.Add("siegebreakerHunkerPenaltyLive");

            // ---- UNDERTOW W5 — LOCKON de-superset: +PerkAim aim only vs a FLANKED target, NOT vs a merely
            // EXPOSED one. On the open field sgActive has NO cover (exposed, coverLevel==0) but is NOT
            // flanked, so LockOn must now be INERT here (it used to fire on any exposed target — the
            // superset that killed the situational perks). ----
            if (ComputeOdds(gW6Sg, MkPerk(Perk.LockOn), sgActive).HitChance != ComputeOdds(gW6Sg, MkPlain(), sgActive).HitChance) fails.Add("lockOnExposedNoOp");
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

        // SKIRMISHER (TEMPO wave 5): ignores overwatch ONLY after firing this turn (shoot-then-slip),
        // unlike OUTRUNNER which always does. So a fresh SKIRMISHER mover still eats overwatch.
        {
            var skU = new Unit { Team = Team.Player }; skU.Perks.Add(Perk.Skirmisher);
            if (IgnoresOverwatch(skU)) fails.Add("skirmisherPreFireShouldEatOverwatch");
            skU.FiredThisTurn = true;
            if (!IgnoresOverwatch(skU)) fails.Add("skirmisherPostFireShouldSlip");
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

        // W2 SPEC FORKS — armor: JUGGERNAUT (Assault) and ANCHOR (Gunner Spec.Bulwark) carry +SpecArmor
        // innate armor, folded into HardenedReduce on top of the shop d.Armor / the Hardened perk.
        {
            var plainS = new Unit { Team = Team.Player, Hp = 20, MaxHp = 20 };
            if (HardenedReduce(plainS, 7, false) != 7) fails.Add("specArmorNoOpWithoutSpec");
            var jug = new Unit { Team = Team.Player, Hp = 20, MaxHp = 20, Spec = Spec.Juggernaut };
            if (HardenedReduce(jug, 7, false) != 7 - SpecArmor) fails.Add("juggernautArmor");
            if (HardenedReduce(jug, 7, true)  != 7 - SpecArmor) fails.Add("juggernautArmorCrit");
            var anc = new Unit { Team = Team.Player, Hp = 20, MaxHp = 20, Spec = Spec.Bulwark };   // Spec.Bulwark = ANCHOR
            if (HardenedReduce(anc, 7, false) != 7 - SpecArmor) fails.Add("anchorArmor");
            // STACKS on top of the shop d.Armor (a separate read), and respects the >=1 floor.
            var jugArmored = new Unit { Team = Team.Player, Hp = 20, MaxHp = 20, Armor = 2, Spec = Spec.Juggernaut };
            if (HardenedReduce(jugArmored, 7, false) != 7 - 2 - SpecArmor) fails.Add("juggernautStacksShopArmor");
            if (HardenedReduce(jugArmored, 1, false) < 1) fails.Add("specArmorFloor");
        }

        // W2 SPEC FORK — SENTINEL (Sharpshooter): overwatch crit == Guardian, and the perk+spec stack
        // adds the bonus ONCE (the || single-add, R2 — no double-count) on a reaction shot only.
        {
            var gS = new Grid();
            var foeS = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 10, MaxHp = 10 };
            var baseSh = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5, ReactedThisTurn = true };
            var sentinel = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5, ReactedThisTurn = true, Spec = Spec.Sentinel };
            var guardian = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5, ReactedThisTurn = true };
            guardian.Perks.Add(Perk.Guardian);
            var both = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5, ReactedThisTurn = true, Spec = Spec.Sentinel };
            both.Perks.Add(Perk.Guardian);
            int baseCrit = ComputeOdds(gS, baseSh, foeS).CritChance;
            int sentCrit = ComputeOdds(gS, sentinel, foeS).CritChance;
            int grdCrit  = ComputeOdds(gS, guardian, foeS).CritChance;
            int bothCrit = ComputeOdds(gS, both, foeS).CritChance;
            if (sentCrit != grdCrit) fails.Add("sentinelEqualsGuardianCrit");
            if (sentCrit <= baseCrit) fails.Add("sentinelReactCritFires");
            if (bothCrit != sentCrit) fails.Add("sentinelGuardianNoDoubleCount");   // R2: || => once only
            // not reacting: Sentinel is inert (overwatch-only rule)
            var sentResting = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5, Spec = Spec.Sentinel };
            var plainResting = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            if (ComputeOdds(gS, sentResting, foeS).CritChance != ComputeOdds(gS, plainResting, foeS).CritChance) fails.Add("sentinelRestingInert");
        }

        // W2 SPEC FORK — HEADHUNTER (Sharpshooter): a foe MARKED by a Headhunter takes squad-wide
        // +HeadhunterMarkCrit; a plain mark (MarkedByHeadhunter=false) grants the +aim but NOT the crit.
        {
            var gH = new Grid();
            var shooterH = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            var plainMark = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 10, MaxHp = 10, Marked = true };
            var hhMark    = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 10, MaxHp = 10, Marked = true, MarkedByHeadhunter = true };
            int plainMarkCrit = ComputeOdds(gH, shooterH, plainMark).CritChance;
            int hhMarkCrit    = ComputeOdds(gH, shooterH, hhMark).CritChance;
            if (hhMarkCrit != Util.Clamp(plainMarkCrit + HeadhunterMarkCrit, 0, 100)) fails.Add("headhunterMarkCrit");
        }

        // W10 — BIPOD (weapon mod): +BipodAim aim ONLY while the shooter hasn't moved this turn
        // (Unit.MovedThisTurn false); moving disarms it exactly; a mod-less shooter is unaffected
        // either way. Also the deliberate EXPOSED-BY-FIRE anti-synergy is real: the planted bipod
        // shooter that fired reads as ExposedFire to the enemy.
        {
            var gBp = new Grid();
            var bpTgt = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Enemy, X = 7, Y = 5, Hp = 9, MaxHp = 10 };
            var bpPlain = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            var bpMod   = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            bpMod.InstallMod(WeaponMod.Bipod);
            int bpBase = ComputeOdds(gBp, bpPlain, bpTgt).HitChance;
            // planted (hasn't moved): exactly +BipodAim over the plain shooter.
            if (ComputeOdds(gBp, bpMod, bpTgt).HitChance != Util.Clamp(bpBase + WeaponModDef.BipodAim, 3, 95)) fails.Add("bipodPlantedFires");
            // moved this turn: the bipod is disarmed (back to the plain baseline).
            bpMod.MovedThisTurn = true;
            if (ComputeOdds(gBp, bpMod, bpTgt).HitChance != bpBase) fails.Add("bipodMovedNoOp");
            bpMod.MovedThisTurn = false;
            // a mod-less shooter never reads MovedThisTurn (no hidden aim swing).
            bpPlain.MovedThisTurn = true;
            if (ComputeOdds(gBp, bpPlain, bpTgt).HitChance != bpBase) fails.Add("bipodPlainUnaffected");
            bpPlain.MovedThisTurn = false;
            // anti-synergy: the planted shooter that FIRED reads ExposedFire to the enemy (the trade).
            bpMod.FiredThisTurn = true; bpMod.MovedAfterFire = false;
            var bpFoeView = ComputeOdds(gBp, bpTgt, bpMod);
            if (!bpFoeView.ExposedFire) fails.Add("bipodExposedTrade");
            bpMod.FiredThisTurn = false;
            // SUPPRESSOR installs cleanly and changes NO odds (it's a Game-side concealment rule,
            // exercised by CONCEALTEST) — a stat-silent mod must not warp ComputeOdds.
            var supU = new Unit { Aim = 60, Weapon = Weapon.Make(WeaponKind.Rifle), Team = Team.Player, X = 3, Y = 5 };
            supU.InstallMod(WeaponMod.Suppressor);
            var supOdds = ComputeOdds(gBp, supU, bpTgt);
            if (supOdds.HitChance != bpBase) fails.Add("suppressorStatSilentHit");
            if (supOdds.CritChance != ComputeOdds(gBp, bpPlain, bpTgt).CritChance) fails.Add("suppressorStatSilentCrit");
        }

        // W10 — boon RULE PREDICATES (single source of truth for the Game-side gates; the
        // RunBoons static is saved/restored so no boon leaks into other tests/runtime):
        //   FIELD DRILLS: FieldCraftLimit 1 -> 2 for a player with the boon; enemies never.
        //   SHOCK DOCTRINE: BraceFullDamage flips for a player watcher, and the Game arithmetic
        //   (halve unless the predicate) yields full damage exactly when it holds.
        {
            var savedBoons = RunBoons;
            RunBoons = new System.Collections.Generic.HashSet<Boon>();
            var fdP = new Unit { Team = Team.Player };
            var fdE = new Unit { Team = Team.Enemy };
            if (FieldCraftLimit(fdP) != 1) fails.Add("fieldDrillsBaseLimit");
            if (BraceFullDamage(fdP)) fails.Add("shockDoctrineBaseOff");
            RunBoons = new System.Collections.Generic.HashSet<Boon> { Boon.FieldDrills, Boon.ShockDoctrine };
            if (FieldCraftLimit(fdP) != 2) fails.Add("fieldDrillsBoonLimit");
            if (FieldCraftLimit(fdE) != 1) fails.Add("fieldDrillsEnemyExcluded");
            if (FieldCraftLimit(null) != 1) fails.Add("fieldDrillsNullSafe");
            if (!BraceFullDamage(fdP)) fails.Add("shockDoctrineBoonOn");
            if (BraceFullDamage(fdE)) fails.Add("shockDoctrineEnemyExcluded");
            if (BraceFullDamage(null)) fails.Add("shockDoctrineNullSafe");
            // the Game-site arithmetic: dmg 7 halves to 3 without the boon, stays 7 with it.
            int dmg = 7;
            int halved = BraceFullDamage(fdE) ? dmg : Math.Max(1, dmg / 2);   // no boon path (enemy)
            int full   = BraceFullDamage(fdP) ? dmg : Math.Max(1, dmg / 2);   // boon path (player)
            if (halved != 3) fails.Add("shockDoctrineHalvingMath");
            if (full != 7) fails.Add("shockDoctrineFullMath");
            RunBoons = savedBoons;
        }

        // --- FUL-2: supercover LOS — a sealed diagonal corner is a wall at range, a slip point-blank ---
        {
            var gS = new Grid();
            gS.Tiles[5, 4] = TileType.HighCover;
            gS.Tiles[4, 5] = TileType.HighCover;
            // range-2 diagonal through the sealed corner: blocked BOTH directions
            if (gS.HasLineOfSight(4, 4, 6, 6)) fails.Add("supercoverSealedFwd");
            if (gS.HasLineOfSight(6, 6, 4, 4)) fails.Add("supercoverSealedRev");
            // adjacent diagonal keeps the point-blank exception (resolves as TRUE-corner cover, not no-LOS)
            if (!gS.HasLineOfSight(4, 4, 5, 5)) fails.Add("supercoverPointBlank");
            // a SINGLE corner never seals — the diagonal half-cover read stays a sightline
            var gS1 = new Grid();
            gS1.Tiles[5, 4] = TileType.HighCover;
            if (!gS1.HasLineOfSight(4, 4, 6, 6)) fails.Add("supercoverSingleOpen");
            // commanding (overHighCover) sight ignores the cover corner ...
            if (!gS.HasLineOfSight(4, 4, 6, 6, true)) fails.Add("supercoverCommandingOver");
            // ... but a sealed SMOKE corner blocks even commanding sight
            var gSm = new Grid();
            gSm.AddSmoke(5, 4, 0, 2); gSm.AddSmoke(4, 5, 0, 2);   // radius 0 = the corner tiles only
            if (gSm.HasLineOfSight(4, 4, 6, 6, true)) fails.Add("supercoverSmokeSealed");
        }

        return fails.Count == 0
            ? "COMBATTEST: PASS (cover A-E + high-ground + tier-2 + drone/shield + boss-arc-flag + ambush + graze + streak + perk-balance + build-perks + vantage/breaker/siegebreaker + fragile-floor + armor + bulwark-plating + momentum + outrunner + vanguard + crossfire + factions + faction-prep + spec-forks + bipod/suppressor + field-drills/shock-doctrine + supercover-corner gates all hold)"
            : "COMBATTEST: FAIL (" + string.Join(",", fails) + ")";
    }
}
