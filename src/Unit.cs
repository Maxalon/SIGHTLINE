using System;
using System.Numerics;

namespace Sightline;

public enum Team { Player, Enemy }

// APPEND-ONLY — new members at the END only; never reorder/remove (persisted by ordinal).
public enum WeaponKind { Rifle, Shotgun, Sniper, Lmg, Smg }

/// Per-class signature ability (self-cast, one charge per mission).
/// APPEND-ONLY: AbilityKind is DERIVED from Cls (never serialised), so appending Mark/Grapple
/// is save-safe — a CORPSMAN persists as just its Cls string and re-derives its kit.
public enum AbilityKind { None, RunGun, Blitz, Steady, Suppress, Heal, Mark, Grapple, Slipstream, Pin }

/// Utility-item slot (3.4): a second throwable beyond grenades, assigned by class.
public enum ItemKind { None, Smoke, Flash, Barricade, Incendiary }

/// Persistent weapon upgrades bought with Intel at the barracks requisition shop — the
/// run's real reward sink, so kills compound into permanent firepower and a leveled squad
/// genuinely out-guns a fresh one (fixes the campaign attrition death-spiral). A mod is
/// INSTALLED on a Unit (Unit.WeaponMods, persisted) and its effect is baked into that
/// soldier's Weapon's effective stats via Weapon.ApplyMods, so it flows through every
/// combat read (incl. the HUD %-to-hit / crit tooltip) with no special-casing.
/// APPEND-ONLY: SaveGame persists installed mods by (int)WeaponMod, so new members go at
/// the END — never reorder or remove the existing ones.
/// W10 (SIGNAL): BIPOD and SUPPRESSOR are RULE mods, not stat mods — BIPOD is a conditional
/// ComputeOdds read (+aim while the shooter hasn't moved this turn; deliberate anti-synergy with
/// EXPOSED BY FIRE, which punishes firing-and-standing-still) and SUPPRESSOR narrows the
/// concealment-break pod wake (Game.BreakConcealment) to the shot target's own pod. Neither
/// touches Weapon.ApplyMods (no baked stat change).
public enum WeaponMod { Scope, ExtendedMag, HollowPoint, Stabilizer, Bipod, Suppressor }

/// Promotion perks: a soldier picks one each rank-up (see Run / barracks).
/// APPEND-ONLY: enum ordinals are the save keys (SaveGame stores perks by (int)Perk),
/// so new members go at the END — never reorder or remove the existing ones.
public enum Perk { LockOn, Hardened, Reflexes, Bandolier, CloseQuarters, Marksman, Deadeye, Tank, Sprinter, Adrenal,
    Executioner, Guardian, CoolHeaded,
    Opportunist, PointBlank, GiantSlayer,
    Bulwark, Vanguard,
    // TEMPO wave 5 — build choices that exploit the new "firing doesn't end the turn" second action:
    Skirmisher,   // after you fire, your repositioning move this turn ignores enemy overwatch (shoot-then-slip)
    Gunslinger,   // your rushed SECOND shot this turn fires at FULL aim instead of the penalty (double-tap)
    // HORIZON wave 6 — three more pure ComputeOdds-read build perks (no new state/hooks; auto-flow into
    // the offer pool, dossier, and save via PerkDef + the append-only ordinal):
    Vantage,      // +crit while firing from HIGH GROUND (elevation specialist)
    Breaker,      // +crit vs a SUPPRESSED or PINNED target (combined-arms punish)
    Siegebreaker }// +aim vs a HUNKERED target (anti-turtle / dig-them-out)

/// Battlefield traits earned by FEATS (see Game feat hooks + Run.DebriefSurvivors).
/// Each is a small passive read in Combat.ComputeOdds, so veterans matter.
// APPEND-ONLY — new members at the END only; never reorder/remove (persisted by ordinal).
public enum Trait { Killer, ColdBlood, IronWill, Vengeful }

/// SCARS (W5): the COST side of soldier identity — lasting marks left by TRAUMA, the dark
/// mirror of the positive feat→trait system. Each is EARNED by surviving a brutal mission
/// (near-death / fire) and is a clear DRAWBACK paired with a defiant upside (roughly a wash),
/// so it characterises a veteran without breaking balance. Granted in Run.DebriefSurvivors
/// (mirroring GrantTrait); read passively in Combat.ComputeOdds / Unit.MoveBudget / Unit.AddStatus.
// APPEND-ONLY — new members at the END only; never reorder/remove (persisted by ordinal).
public enum Scar { ShellShocked, BurnScarred, HardBitten, Vendetta }

/// CLASS SPECIALIZATION FORK (W2): a one-time, run-divergent pick-1-of-2 offered the first time a
/// soldier reaches Unit.SpecRank (CORPORAL). Each fork augments/replaces the class's signature VERB
/// or a core RULE, so progression becomes HORIZONTAL (changes HOW the class plays, not just stats).
/// APPEND-ONLY: persisted by raw (int)Spec via SaveGame (UnitDto.Spec), so new members go at the END
/// only — never reorder/remove. Spec.None (= ordinal 0) is the inert default (old saves / rookies),
/// and every fork read no-ops on None (mirrors how Combat.MissionFaction == None no-ops).
public enum Spec
{
    None,
    Breacher, Juggernaut,        // Assault (GRAPPLE)
    Phantom, Pathfinder,         // Ranger (SLIPSTREAM)
    Sentinel, Headhunter,        // Sharpshooter (MARK)
    AreaDenial, Bulwark,         // Gunner (PIN) -- display name of Bulwark = "ANCHOR"
    FieldSurgeon, CombatMedic,   // Corpsman (HEAL)
}

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
    // EFFECTIVE stats (base + installed weapon-mod bonuses). Read throughout Combat/Hud/Mission.
    // ApplyMods recomputes these from the captured base values (below) so it's idempotent.
    public int DmgMin, DmgMax;
    public int AimBonus;
    public int CritBase;
    public int Clip;

    // ---- weapon-mod plumbing (persistent upgrades, src/WeaponMod) ----
    // The pristine base stats captured at Make() time, so ApplyMods can re-derive the
    // effective fields above from scratch (idempotent — re-applying the same mods is safe).
    int _baseDmgMin, _baseDmgMax, _baseAimBonus, _baseCritBase, _baseClip;
    public int RangeBonus;   // +tiles of effective range from mods (Stabilizer); 0 by default
    public bool Scoped;      // SCOPE: flattens long-range aim falloff

    /// Maximum effective firing range in tiles (+ any mod range bonus).
    public int MaxRange => BaseMaxRange + RangeBonus;
    int BaseMaxRange => Kind switch
    {
        WeaponKind.Shotgun => 8,
        WeaponKind.Smg => 10,
        WeaponKind.Sniper => 20,
        WeaponKind.Lmg => 13,
        _ => 15,
    };

    /// Aim modifier from range (in tiles). Each weapon has its own profile. A SCOPE softens
    /// the long-range penalty (the falloff term is halved past the weapon's sweet spot), so a
    /// scoped weapon stays accurate further out without changing its close-range behaviour.
    public int RangeMod(float dist)
    {
        switch (Kind)
        {
            case WeaponKind.Shotgun: // brutal up close, useless at range
                return (int)Util.Clamp(Soften((5 - dist) * 8), -45, 30);
            case WeaponKind.Sniper:  // rewards distance, punished point-blank
                return (int)Util.Clamp((dist - 4) * 4, -30, 16);   // already long-ranged; scope adds none here
            case WeaponKind.Smg:     // slight close-range edge
                return (int)Util.Clamp(Soften((7 - dist) * 2), -12, 12);
            case WeaponKind.Lmg:     // suppression gun: wide flat medium band, gentle long falloff
                return (int)Util.Clamp(Soften(-(dist - 10) * 1.5f), -10, 6);
            default:                 // rifle: balanced, gentle falloff
                return (int)Util.Clamp(Soften((8 - dist) * 1.5f), -18, 10);
        }
    }

    // SCOPE: halve a NEGATIVE range term (long-range penalty); leave the close-range bonus alone.
    float Soften(float v) => (Scoped && v < 0) ? v * 0.5f : v;

    /// PROGRAM RESONANCE X1 "THE EXCHANGE" — trim this weapon's damage band at the BASE, by
    /// `trim` points off both ends (DmgMin floored at 1, DmgMax never below DmgMin). Used by
    /// Mission.MakeHostile for the hostile half of the exchange. It moves the PRISTINE base
    /// values, not just the effective ones, so a later ApplyMods/RefreshWeaponMods pass can
    /// never resurrect the untrimmed band (enemies carry no mods today; this keeps it true
    /// if one ever gets one).
    public void TrimBaseDamage(int trim)
    {
        if (trim <= 0) return;
        _baseDmgMin = System.Math.Max(1, _baseDmgMin - trim);
        _baseDmgMax = System.Math.Max(_baseDmgMin, _baseDmgMax - trim);
        DmgMin = _baseDmgMin; DmgMax = _baseDmgMax;
    }

    /// Re-derive the effective stats from base + the installed mods. Idempotent: it always
    /// starts from the captured base values, so calling it repeatedly (or after adding a mod)
    /// is safe. Magnitudes live in WeaponModDef (one source of truth, shared with the shop UI).
    public void ApplyMods(System.Collections.Generic.IEnumerable<WeaponMod> mods)
    {
        DmgMin = _baseDmgMin; DmgMax = _baseDmgMax;
        AimBonus = _baseAimBonus; CritBase = _baseCritBase; Clip = _baseClip;
        RangeBonus = 0; Scoped = false;
        if (mods == null) return;
        foreach (var m in mods)
            switch (m)
            {
                case WeaponMod.Scope:
                    AimBonus += WeaponModDef.ScopeAim; Scoped = true; break;
                case WeaponMod.ExtendedMag:
                    Clip += WeaponModDef.MagClip; break;
                case WeaponMod.HollowPoint:
                    CritBase += WeaponModDef.HollowCrit; DmgMin += WeaponModDef.HollowDmg; DmgMax += WeaponModDef.HollowDmg; break;
                case WeaponMod.Stabilizer:
                    AimBonus += WeaponModDef.StabilizerAim; RangeBonus += WeaponModDef.StabilizerRange; break;
            }
    }

    static Weapon New(string name, WeaponKind k, int dmgMin, int dmgMax, int aimBonus, int critBase, int clip)
    {
        var w = new Weapon
        {
            Name = name, Kind = k,
            DmgMin = dmgMin, DmgMax = dmgMax, AimBonus = aimBonus, CritBase = critBase, Clip = clip,
            _baseDmgMin = dmgMin, _baseDmgMax = dmgMax, _baseAimBonus = aimBonus, _baseCritBase = critBase, _baseClip = clip,
        };
        return w;
    }

    public static Weapon Make(WeaponKind k) => k switch
    {
        WeaponKind.Rifle   => New("Rifle",   k, 3, 5, 0, 10, 4),
        WeaponKind.Shotgun => New("Shotgun", k, 4, 7, 0, 15, 2),
        WeaponKind.Sniper  => New("Marksman",k, 5, 7, 3, 14, 3),
        WeaponKind.Lmg     => New("LMG",     k, 3, 6, 3,  5, 5),
        WeaponKind.Smg     => New("SMG",     k, 2, 4, 0, 10, 4),
        _ => New("Rifle", WeaponKind.Rifle, 3, 5, 0, 10, 4),
    };

    /// The weapons a class may carry, for the barracks ARMORY (re-arm decision). Each set is
    /// a small THEMATIC pool (2-3 options) around the class role, so the pick is a real
    /// trade-off (e.g. an Assault leaning shotgun for breach vs SMG for mobility) rather than a
    /// free pick of every gun — keeps balance intact. The class's default weapon leads the list.
    public static WeaponKind[] ArmoryOptions(string cls) => cls switch
    {
        "ASSAULT"      => new[] { WeaponKind.Rifle, WeaponKind.Shotgun, WeaponKind.Smg },
        "RANGER"       => new[] { WeaponKind.Shotgun, WeaponKind.Smg, WeaponKind.Rifle },
        "SHARPSHOOTER" => new[] { WeaponKind.Sniper, WeaponKind.Rifle },
        "GUNNER"       => new[] { WeaponKind.Lmg, WeaponKind.Rifle },
        "CORPSMAN"     => new[] { WeaponKind.Smg, WeaponKind.Rifle, WeaponKind.Shotgun },
        _ => new[] { WeaponKind.Rifle },
    };

    /// One-line tactical descriptor for a weapon kind (shown in the armory picker).
    public static string KindBlurb(WeaponKind k) => k switch
    {
        WeaponKind.Rifle   => "balanced - gentle range falloff, 4-round clip",
        WeaponKind.Shotgun => "brutal up close, useless at range - 2-round clip",
        WeaponKind.Sniper  => "rewards distance, punished point-blank - high crit",
        WeaponKind.Lmg     => "wide flat medium band, big clip - suppression",
        WeaponKind.Smg     => "mobile close-range snap - light damage",
        _ => "",
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

    // TEMPO (PROGRAM TEMPO): an aimed shot now costs 1 action and does NOT end the turn — so a
    // soldier can move-then-shoot OR shoot-then-reposition (the "where do I end up after firing"
    // bet). FiredThisTurn caps offensive output at ONE shot/turn (flat DPS, no double-tap) — the
    // spare action goes to movement/support, never a second bullet (RUN&GUN bypasses this for the
    // Assault's signature double-tap). Reset in BeginTurn.
    public bool FiredThisTurn;

    // HORIZON W1 — EXPOSED BY FIRE: a unit that fired this turn and did NOT move afterward is
    // easier to hit on the opponent's turn (symmetric to the player's concealment ambush). Set false
    // alongside FiredThisTurn wherever a real shot fires; flipped true by OnUnitEnteredTile (any tile
    // entry after firing = ducked). TRANSIENT — reset in BeginTurn, never persisted.
    public bool MovedAfterFire;

    // Renewable signature ability: a per-unit COOLDOWN (turns remaining until usable again).
    // 0 == ready. Set to AbilityCooldownFor(Ability) on use; ticked down 1 at the unit's BeginTurn.
    // TRANSIENT per-mission state (like the old AbilityCharge) — NEVER persisted; rebuilt by
    // Mission.Build / Game.SetupMission. Old saves load unchanged (no DTO field).
    public int AbilityCd;
    public bool AbilityReady => AbilityCd <= 0;
    public bool RunGun;         // assault: next shot costs 1 action, doesn't end the turn
    public bool Blitz;          // ranger: next move costs one action less
    public bool Steady;         // sharpshooter: next shot gets +aim/+crit
    public int  Suppress;       // gunner debuff currently ON this unit (aim penalty)
    public bool Marked;         // sharpshooter MARK: this FOE is designated -> whole squad +aim/+crit vs it
                                // (per-mission, set on an enemy by DoAbility(Mark), cleared at the marker's next turn)
    public bool Slipstreaming;  // ranger SLIPSTREAM: this soldier's current free move is silent (no overwatch
                                // provoked) — set by DoAbility(Slipstream), consumed/cleared by the move it covers
    public int  Pinned;         // gunner SUPPRESSING FIRE: this FOE is pinned (>0 = pinned; lifted wholesale by Game.ClearPins at next StartPlayerTurn — never decremented). While > 0 it
                                // takes the Suppress aim debuff AND cannot use a 2-action DASH (area denial).
                                // Decays one turn at the pinned unit's BeginTurn; never persisted (per-mission).

    // optional player-authored role label (overrides the auto strength tags in the
    // roster/dossier when set); persists across the run
    public string CustomTag;

    // promotion perks (persist across the run); pick one per rank-up
    public System.Collections.Generic.List<Perk> Perks = new();
    public bool HasPerk(Perk p) => Perks.Contains(p);

    // CLASS SPECIALIZATION FORK (W2): the soldier's chosen fork (persists across the run; one-time
    // pick at SpecRank). Spec.None = not yet specialized (rookies + old saves). Every fork read gates
    // on HasSpec(Spec.X) and is inert on None.
    public Spec Spec = Spec.None;
    public bool HasSpec(Spec s) => Spec == s;
    public bool IsSpecialized => Spec != Spec.None;
    public const int SpecRank = 2;          // Run.Ranks[2] == "CORPORAL" — the first real promotion (KillReq[2]=3)
    // HEADHUNTER (Sharpshooter fork): a foe MARKED by a Headhunter takes squad-wide +crit (only this
    // marker grants the crit). Transient per-mission state — never persisted; cleared in ClearMarks.
    public bool MarkedByHeadhunter;

    // ---- persistent weapon upgrades (the Intel reward sink): installed weapon mods ----
    // Bought at the barracks shop; baked into Weapon.ApplyMods so the effect flows through
    // every combat read. Persisted by SaveGame (as a list of ints). Each mod is one-per-soldier
    // (a soldier can own each upgrade once); HasMod gates re-purchase + the shop affordability.
    public System.Collections.Generic.List<WeaponMod> WeaponMods = new();
    public bool HasMod(WeaponMod m) => WeaponMods.Contains(m);
    /// Install a weapon mod (no-op if already owned) and re-bake the weapon's effective stats.
    public void InstallMod(WeaponMod m)
    {
        if (WeaponMods.Contains(m)) return;
        WeaponMods.Add(m);
        Weapon?.ApplyMods(WeaponMods);
    }
    /// Re-apply all installed mods onto the current Weapon (call after rebuilding the weapon,
    /// e.g. on save-load, so the persisted upgrades take effect).
    public void RefreshWeaponMods() => Weapon?.ApplyMods(WeaponMods);

    // ---- soldier identity (3.2): nickname + earned traits + bonds, all persist ----
    public string Nickname;     // earned with the first feat; shown as NAME "NICK"
    public System.Collections.Generic.List<Trait> Traits = new();
    public bool HasTrait(Trait t) => Traits.Contains(t);
    public System.Collections.Generic.List<string> Bonds = new();  // names of bonded squadmates

    // ---- SCARS (W5): earned-through-trauma identity (the cost mirror of Traits), all persist ----
    public System.Collections.Generic.List<Scar> Scars = new();
    public bool HasScar(Scar s) => Scars.Contains(s);
    // The faction that nearly killed this soldier (set at debrief = the mission's faction on the
    // first survived near-death). Read by the VENDETTA scar (a grudge). Persisted; default None.
    public Faction VendettaFaction = Faction.None;
    // How many missions this soldier has survived a near-death — drives ShellShocked@2 / HardBitten@3.
    // Persisted; default 0 (rookies / old saves).
    public int NearDeathCount;

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
        // COOL-HEADED composure: this soldier is immune to Disoriented — the daze slides right off.
        if (k == StatusKind.Disoriented && HasPerk(Perk.CoolHeaded)) return;
        // SHELL-SHOCKED (W5): unshakeable nerves — Disoriented and Stun just don't take hold (inert
        // without the scar). The flip side of the lasting -mobility caution; the soldier has seen worse.
        if ((k == StatusKind.Disoriented || k == StatusKind.Stun) && HasScar(Scar.ShellShocked)) return;
        // PYROMANIACS boon (W10): the squad works IN its own fire — no soldier ever catches Burning
        // (the status; the one-time step-in sear in Game.EnvDamage still applies). Read via the
        // per-mission Combat.RunBoons static (this is the ONE chokepoint every ignition path routes
        // through: fire step-in, TickHazards re-ignition, incendiary splash, grenade/barrel burns).
        // Team-gated so a burning ENEMY is never spared.
        if (k == StatusKind.Burning && Team == Team.Player && Combat.RunBoons.Contains(Boon.Pyromaniacs)) return;
        foreach (var s in Statuses) if (s.Kind == k) { s.Turns = Math.Max(s.Turns, turns); return; }
        Statuses.Add(new Status { Kind = k, Turns = turns });
    }

    // transient per-mission feat tracking (reset in Game.SetupMission; never persisted)
    public bool FeatMultiKill;  // 2+ kills in a single turn this mission
    public bool FeatClutch;     // a kill while bloodied (<= 1/4 HP)
    public bool FeatVengeful;   // a kill after a squadmate fell this mission
    public bool WasNearDeath;   // dropped to <= 1/4 HP at some point this mission (survived = feat)
    public bool FeatBurned;     // took fire/burn damage this mission (survived = BURN-SCARRED scar)
    public bool AllyDown;       // a squadmate has been killed this mission
    public int KillsThisTurn;   // reset each BeginTurn (multi-kill detection)
    public bool BondAura;       // a bonded squadmate is adjacent (refreshed each frame by Game)
    public bool FiredFromConcealment; // true for ONE shot after breaking concealment (4.4)
    // W2 telemetry: the LAST source-less damage label this unit took ("BURN"/"BLEED"/"STRIKE"/
    // "BARREL"/"SLAM"/...), set by Game.EnvDamage + the barrel blast. KillUnit's attribution
    // fallback reads it so a DoT/hazard death buckets under its real cause instead of "?".
    // Transient (never persisted, never gameplay-read).
    public string LastDotSource;

    // ---- FUL-7 LAST LIGHT: the DOWN (bleeding-out) state machine. ALL transient — never
    // persisted (ToUnitDto whitelist; reset in Game.SetupMission; EnterBarracks resolves every
    // Downed before the next checkpoint writes). Downed keeps Alive == true so the existing
    // carry kit (DRAG/EXTRACT gate on Alive), the wipe test, and Evac's all-in-zone win all see
    // a breathing body; CanAct stays false every turn (ActionsLeft zeroed in StartPlayerTurn),
    // so a downed soldier never acts, reacts, or is selectable. WasDownedThisMission is the
    // anti-revive-tank rule: ONE down per soldier per mission — the second lethal event kills
    // outright (Game.CanGoDown), as does ANY damage reaching a body already down (AoE/fire:
    // the telegraphed-weapons honesty valve).
    public bool Downed;              // bleeding out (Hp 0, Alive true, out of the fight)
    public bool Stabilized;          // timer frozen — stays down (drag-able); recovered on a won field
    public int DownedTurns;          // player-turn countdown to bleed-out (Game.DownedTimerTurns)
    public bool WasDownedThisMission;
    public string DownedByCls;       // the DOWNING attribution (enemy archetype, or a DoT label) — the honest cause at expiry

    // Streak-breaker (S4-C): counts consecutive CLEAN misses by this unit. After each
    // miss the next shot gets a small hidden aim bonus (see Combat.Resolve). Resets to
    // 0 on any hit or graze. Intentionally NOT persisted — per-mission accumulation only;
    // a fresh unit starts at 0, and a connect always clears it.
    public int ConsecutiveMisses;

    public AbilityKind Ability => AbilityKindFor(Cls);
    public string AbilityName => Ability switch
    {
        AbilityKind.RunGun  => "RUN&GUN",
        AbilityKind.Blitz   => "BLITZ",
        AbilityKind.Steady  => "STEADY",
        AbilityKind.Suppress=> "SUPPRESS",
        AbilityKind.Heal    => "PATCH",
        AbilityKind.Mark    => "MARK",
        AbilityKind.Grapple => "GRAPPLE",
        AbilityKind.Slipstream => "SLIPSTREAM",
        AbilityKind.Pin     => "SUPPR. FIRE",
        _ => "ABILITY",
    };
    public string AbilityDesc => Ability switch
    {
        AbilityKind.RunGun   => "Next shot costs 1 action (won't end your turn)",
        AbilityKind.Blitz    => "Next move costs one action less",
        AbilityKind.Steady   => "Next shot: +25 aim, +20 crit",
        AbilityKind.Suppress => "Pin the nearest foe: -30 aim + overwatch it",
        AbilityKind.Heal     => "Heal the most-wounded adjacent squadmate (+4 HP)",
        AbilityKind.Mark     => "Designate a foe: whole squad gets +aim/+crit vs it this round",
        AbilityKind.Grapple  => "Yank a nearby foe 1 tile toward you, out of its cover",
        AbilityKind.Slipstream => "Silent reposition: a 1-action move that draws no overwatch (PATHFINDER: free)",
        AbilityKind.Pin      => "Suppressing fire: pin a foe + its neighbours - they take -aim and can't dash next turn",
        _ => "",
    };
    public static AbilityKind AbilityKindFor(string cls) => cls switch
    {
        "ASSAULT"      => AbilityKind.Grapple,    // verb: yank a foe out of cover (was RunGun stance)
        "RANGER"       => AbilityKind.Slipstream, // verb: free, overwatch-safe reposition (was Blitz stance)
        "SHARPSHOOTER" => AbilityKind.Mark,       // verb: focus-fire designator (was Steady stance)
        "GUNNER"       => AbilityKind.Pin,         // verb: area-denial suppressing fire (was Suppress stance)
        "CORPSMAN"     => AbilityKind.Heal,
        _ => AbilityKind.None,
    };

    // Turns of cooldown after use. Stronger / turn-defining verbs cost more. 0 == every turn.
    // Counts the USER's own turns (ticked at their BeginTurn), so "Cd 3" = skip ~2 turns then usable.
    public static int AbilityCooldownFor(AbilityKind k) => k switch
    {
        AbilityKind.Heal       => 3,   // squad sustain — strongest meta lever, keep scarce
        AbilityKind.Slipstream => 3,   // free overwatch-immune move — mobility is oppressive if spammed
        AbilityKind.Mark       => 2,   // squad-wide focus-fire amp — strong but already action-costed
        AbilityKind.Pin        => 2,   // AoE area-denial; also ENDS the turn, so naturally rate-limited
        AbilityKind.Grapple    => 2,   // single-foe reposition; shares ShovedThisTurn budget too
        // legacy stances (only reachable if a class is ever re-pointed at them):
        AbilityKind.Steady     => 2,
        AbilityKind.Suppress   => 2,
        AbilityKind.RunGun     => 1,
        AbilityKind.Blitz      => 1,
        _ => 0,
    };

    // ---- utility item (3.4): a second throwable slot, 1 charge/mission, by class ----
    public int ItemCharge;                       // remaining uses this mission (refilled in Mission.Build)
    public ItemKind Item => ItemKindFor(Cls);    // derived from class (never persisted)
    public ItemKind EnemyItem;                   // explicit item for enemy units (set in SpawnEnemies, None for players)
    public string ItemName => Item switch
    {
        ItemKind.Smoke     => "SMOKE",
        ItemKind.Flash     => "FLASH",
        ItemKind.Barricade => "BARRICADE",
        ItemKind.Incendiary => "INCENDIARY",
        _ => "ITEM",
    };
    public string ItemDesc => Item switch
    {
        ItemKind.Smoke     => "Lob a smoke cloud: blocks line of sight + overwatch through it for a few turns",
        ItemKind.Flash     => "Lob a flashbang: disorients everyone in the blast (-aim, no overwatch next turn)",
        ItemKind.Barricade => "Deploy a low-cover barricade on an empty tile",
        ItemKind.Incendiary => "Lob an incendiary: sets a 3x3 fire field (denies ground, ignites foes, cooks barrels)",
        _ => "",
    };
    public static ItemKind ItemKindFor(string cls) => cls switch
    {
        "ASSAULT"      => ItemKind.Flash,      // breacher: blind the room
        "RANGER"       => ItemKind.Smoke,      // flanker: cover the approach
        "SHARPSHOOTER" => ItemKind.Incendiary, // marksman: area denial - flush foes from cover with fire
        "GUNNER"       => ItemKind.Barricade,  // nest-builder: drop cover
        "CORPSMAN"     => ItemKind.Smoke,      // medic: cover a casualty's extraction
        _ => ItemKind.None,
    };

    public int ActionsLeft;
    public bool OnOverwatch;
    // COUNTERPLAY: FOCUSED overwatch — the soldier braces a 90-degree cone toward (OwDirX,OwDirY).
    // It reacts only to movers inside the cone but with a braced +aim bonus (a kill-lane), vs the
    // default WIDE watch which reacts in any direction at base accuracy. Transient — same lifecycle
    // as OnOverwatch (armed on the player turn, reset in BeginTurn).
    public bool OwFocused;
    public int OwDirX, OwDirY;   // cone centre direction (raw dx,dy toward the aimed tile)
    // UNDERTOW W2: BRACE — the INTERRUPT half of the reaction economy. A braced soldier holds a
    // DISRUPTING reaction instead of a lethal one: its reaction shot deals reduced damage but, on a
    // hit, STAGGERS the mover (zeroes its remaining actions this turn -> its post-move offense is
    // denied). It's the inverse of overwatch (trade lethality for tempo denial) and a real comeback
    // lever for a behind player. Rides the OnOverwatch plumbing (armed on the player turn, reset in
    // BeginTurn; same ReactedThisTurn one-reaction cap). Transient, never persisted.
    public bool OwBrace;
    public bool Hunkered;
    public bool ReactedThisTurn; // overwatch fired this round
    // SHOVE (forced-movement verb): a soldier may shove at most ONCE per turn. Combined with
    // "shove always costs 1 action" this double-bounds it (no infinite reposition loop). Reset
    // every BeginTurn; never persisted (per-turn combat state only).
    public bool ShovedThisTurn;
    // FIELD CRAFT (W1): two universal positioning verbs, each Combat.FieldCraftLimit(u) times per
    // soldier per turn (reset in BeginTurn; base limit 1, the FIELD DRILLS boon raises it to 2 —
    // W10 converted these from once-per-turn bools to per-turn COUNTERS because a flag skip cannot
    // express "twice"). DRAG pulls an adjacent ally one tile toward the dragger; VAULT leaps the
    // soldier over an adjacent cover tile to the floor on its far side. Per-turn state, never persisted.
    public int DragsThisTurn;
    public int VaultsThisTurn;
    // FUL-6 FIELD DRILLS rework: the boon's effect is now "a DRAG or VAULT drills the soldier
    // forward — +1 tile of movement for the rest of that turn". Granted at the IssueDrag/
    // IssueVault sites (once per soldier per turn), read in MoveBudget as +2 half-steps.
    // Per-turn combat state, reset in BeginTurn, never persisted (ToUnitDto whitelist).
    public bool DrilledThisTurn;
    // W10 BIPOD: has this unit entered ANY tile this turn (walk/dash/vault/drag/shove/grapple all
    // route through Game.OnUnitEnteredTile, the single set-site)? BIPOD's +aim only holds while the
    // shooter is planted (false). Per-turn combat state, reset in BeginTurn, never persisted.
    public bool MovedThisTurn;
    public bool Alive = true;

    // Awareness tier (4.3): enemies escalate Unaware -> Suspicious -> Alert instead of
    // waking instantly. Active (acts in combat / is a live threat) == fully Alert, so the
    // many read sites that gate on "is this enemy awake" keep working unchanged.
    public AlertLevel Alert = AlertLevel.Alert;
    public bool Active => Alert == AlertLevel.Alert;
    public int PodId = -1;      // activation-pod grouping (enemies only)
    public int Routed;          // UNDERTOW W3: turns of ROUT remaining (enemies only; counts down in BeginTurn,
                                // rallies at 0). While >0 the unit flees + won't overwatch + shoots wild. Transient.

    public bool IsVip;          // escort objective: the asset to extract (mission-only, never persists)
    public bool FromReserve;    // COUNTERPLAY: a returning VETERAN recalled from the cross-run reserve
                                // (draft-screen display flag; transient, never persisted)
    public bool Enraged;        // elite boss: one-time low-HP rage trigger
    public int ShieldDx, ShieldDy;  // SHIELD archetype: facing dir its frontal shield blocks (3.7)

    // SIGNAL W5 — BOSS CAPABILITY FLAGS. Transient per-mission state, never persisted (enemies
    // aren't saved). Every signature mechanic used to be Cls-string-keyed (Cls=="SHIELD" /
    // "BOMBARD" scattered across Combat/Game/Ai/Renderer), which made a mechanic inseparable
    // from its rank-and-file archetype. These flags decouple them: each DEFAULTS to mirroring
    // its archetype Cls (a rank-and-file SHIELD/BOMBARD — including every inline harness-built
    // test unit — carries its signature with zero spawn-site changes, so behavior is identical
    // by construction), and can be GRANTED to any other unit. A named boss keeps Cls=="ELITE"
    // (nameplate / enrage / aim-clamp exemption / AI temperament all key on ELITE identity)
    // while carrying a signature mechanic on top. Set true only — a SHIELD can't opt out.
    bool _shieldArc, _hasSiege, _hasBanner;
    public bool HasShieldArc { get => _shieldArc || Cls == "SHIELD";  set => _shieldArc = value; }  // frontal barrier arc (ShieldDx/Dy facing; re-faced by Game.FaceShields)
    public bool HasSiege     { get => _hasSiege  || Cls == "BOMBARD"; set => _hasSiege  = value; }  // telegraphed 3x3 siege strike (ChargeTurns/ChargeX/Y)
    // SIGNAL W8 — the WARBRINGER's banner aura (same flag pattern): pods with a living banner
    // within Chebyshev Game.BannerRange cannot rout (Game.BreakPodMorale skips them) and rally one
    // turn faster (Game.BeginEnemyUnitTurn). BOTH reads live in GAME — BeginTurn here is
    // parameterless and world-blind, so the aura never touches Unit logic. Grantable to a boss.
    public bool HasBanner    { get => _hasBanner || Cls == "WARBRINGER"; set => _hasBanner = value; }
    // SIGNAL W5 — the Legion BREAKER's kit, two independent halves keyed on RagesTwice:
    //  (1) the berserker RUSH temperament in Ai.Plan (advW/elevMult) applies from SPAWN — keyed
    //      on the RagesTwice capability itself, NOT on the frenzy state;
    //  (2) the SECOND rage tier: at <=25% HP an already-Enraged elite FRENZIES once (a further
    //      +aim/+mob spike, popped by Game.UpdateEnemy on its acting beat).
    // Both transient; RagesTwice is the capability, Frenzied the one-shot tier-2 state
    // (mirrors the Enraged pair above).
    public bool RagesTwice;
    public bool Frenzied;
    // FUL-11 CEREMONY — the m6 FINALE boss (set only by Mission.MakeFinaleBoss). Presentation
    // key ONLY: the champion ground ring/aura (Renderer.DrawUnit) + the one-shot HVT SIGHTED
    // banner (Game.CheckNewContact) hang off it — no combat/AI read, so stats stay kit-tuned.
    // Transient like the capability flags above (enemies aren't saved).
    public bool IsBoss;

    // DECAPITATE GUARDED HVT (W4). Transient per-mission, never persisted (enemies aren't saved).
    // IsHvtGuard: this enemy is one of the (<=2) bodyguards the Game picked near the HVT.
    // HvtGuarded: set ONLY on the HVT, recomputed at every turn boundary + after any death by
    // Game.UpdateHvtGuard — true while any living guard is within Chebyshev HvtGuardRange of it.
    // Combat.HardenedReduce reads HvtGuarded to soften (never zero) incoming damage to the HVT.
    public bool IsHvtGuard;
    public bool HvtGuarded;

    // SIEGE / BOMBARD artillery charge (telegraphed area-denial). Transient per-mission state,
    // never persisted (enemies aren't saved). ChargeTurns is set to Game.SiegeFuse when a strike
    // is charged on the BOMBARD's turn; the strike resolves in Game.TickSiegeStrikes at the start
    // of the NEXT enemy turn (it is NOT decremented in BeginTurn — the single authoritative
    // resolve/decrement lives in TickSiegeStrikes so the charge survives across the unit's turns).
    public int ChargeTurns;          // >0 == a strike is in flight (the 3x3 danger zone is live)
    public int ChargeX, ChargeY;     // center tile of the charged 3x3 danger zone

    // meta / campaign progression (persists across missions)
    public int Kills;
    public int Rank;            // index into Run.Ranks
    public string RankName => Run.Ranks[Util.Clamp(Rank, 0, Run.Ranks.Length - 1)];

    // attrition: missions a battle wound lingers (>0 = −Aim/−Mobility); decays per
    // mission in Run.DebriefSurvivors, cleared by a FIELD MEDKIT.
    public int Wound;

    // ---- ARMOR: a persistent flat damage-reduction stat (the Intel survivability sink) ----
    // Bought in the barracks shop (the orchestrator wires that + SaveGame persistence). Every
    // incoming hit on this unit is reduced by Armor, on TOP of the Hardened perk, floored at 1
    // (the guaranteed-damage floor still holds — see Combat.HardenedReduce, the single chokepoint
    // for ALL incoming-damage paths: a normal hit, a graze, and a grenade blast). 0 by default so
    // old saves load unchanged; capped via the shop (ArmorMax) rather than here.
    public int Armor;
    // Shop cap for the Armor stat (the shop should refuse to sell past this). Kept here so the
    // shop UI / autopilot read one source. Modest so armor mitigates, never trivialises damage.
    public const int ArmorMax = 3;

    // bench (S3-A): a wounded soldier can sit out the next mission (deploy short-handed)
    // in exchange for accelerated recovery — Wound decays 2 steps + full HP heal. Cleared
    // at the start of the mission they sit out (SetupMission). Only wounded soldiers may be
    // benched; the minimum deployable squad is 1 (guard in ToggleBench). Never set by the
    // autopilot so smoke-test runs always deploy full-strength. Persists in SaveGame.
    public bool Benched;

    // render state
    public Vector2 Pos;         // pixel-space centre (tweened)
    public Vector2 Recoil;      // transient recoil/knockback offset (decays)
    public float Facing;        // radians, for the facing tick
    public float Flash;         // 0..1 damage flash
    public float Bob;           // idle bob phase

    // ---- procedural unit animation (transient render-only state; decays in Game.Update) ----
    // These drive small body+silhouette deformations in Renderer.DrawUnit so a unit reads as
    // alive (idle breathing already rides Bob): a recoil KICK when it fires, a FLINCH when it
    // takes a hit, and a forward LEAN while it walks. All are render-only (never affect the sim
    // or determinism — they decay deterministically and are seeded only from animation events +
    // the per-unit Bob phase), so the headless SIGHTLINE_SHOT harness stays reproducible.
    public float RecoilAnim;    // 0..1 fire-recoil pose: body rocks back along -Facing, settles fast
    public float FlinchAnim;    // 0..1 hit-flinch: a quick shudder/scale-pop when struck
    public float WalkLean;      // 0..1 walk lean: leans into the direction of travel while stepping

    // half-tile budget. Wound (-mob while wounded) and the SHELL-SHOCKED scar (-mob lasting caution)
    // both subtract mobility, mirroring each other; floored at 1 tile so a unit can always move.
    // FUL-6 FIELD DRILLS: +2 half-steps (one ortho tile) appended AFTER the *2 while drilled.
    public int MoveBudget => Math.Max(1, Mobility - (Wound > 0 ? WoundMob : 0) - (HasScar(Scar.ShellShocked) ? ShellShockMob : 0)) * 2 + (DrilledThisTurn ? 2 : 0);
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
    public const int CloseRange = 4;     // CloseQuarters threshold (tiles)
    public const int LongRange = 7;      // Marksman threshold (tiles)
    // Executioner: FINISHER crit vs targets already below half HP. Set higher than Deadeye's
    // flat +15 so it's a real alternative, not a dominated subset: Executioner beats Deadeye
    // against wounded prey, Deadeye wins against healthy targets (a genuine pick).
    public const int ExecutionerCrit = 25;
    // Guardian: an overwatch LETHALITY perk (vs Reflexes = overwatch RELIABILITY). On a reaction
    // shot Guardian (a) negates the -10 reaction aim penalty (GuardianReactAim, applied in Resolve
    // since the penalty lives in Game's aimMod) and (b) lands a big crit bonus (GuardianReactCrit,
    // applied in ComputeOdds) — so Reflexes makes overwatch HIT, Guardian makes it HURT.
    public const int GuardianReactAim  = 10;  // cancels the standard -10 overwatch reaction penalty
    public const int GuardianReactCrit = 30;  // overwatch reactions crit hard (caught mid-move, exposed)
    // GuardianAim is read by Game's overwatch path (reactMod). It's kept at 0 now: Guardian's whole
    // effect lives in Combat (Resolve cancels the penalty, ComputeOdds adds the crit) so there's one
    // source of truth and no double-counted aim. Don't drop it — Game.cs still references the symbol.
    public const int GuardianAim = 0;
    // HARDENED (reworked): a real TANK durability perk. The old flat "-1 damage" was a dead pick (the
    // graze floor already caps grazes at 1 and the fragile-floor already stops full-HP one-shots, so it
    // saved ~1). New effect: -1 off every hit AND an extra cut vs CRITS — crits are the spiky shots that
    // actually drop soldiers, so a tank that shrugs them off is exactly what a survivability build wants.
    // It never touches your OFFENSE (pure damage-in reduction) so it's "sometimes worth it", not a must-pick.
    // Read via Combat.HardenedReduce so all hit paths (Resolve hit/graze + the grenade in Anim) share one rule.
    public const int HardenedFlat = 1;      // -1 off any incoming hit (the old behaviour, kept as the floor)
    public const int HardenedCrit = 3;      // a critical hit deals an ADDITIONAL -3 (so a crit is -4 total)
    // COOL-HEADED (reworked): composure under fire — a DEFENSIVE perk, distinct from the aim/crit offense line.
    // The old "+5 aim when unhindered" was effectively a flat +5 aim, strictly worse than LockOn/Marksman. New
    // effect, both halves always-on: (1) enemies shooting this soldier suffer -CoolHeadedEvade aim (hard to
    // rattle — read defender-side in ComputeOdds), and (2) immunity to Disoriented (the daze just slides off —
    // enforced in Unit.AddStatus). A survivability pick a frail flanker/point-soldier wants; not a damage perk.
    public const int CoolHeadedEvade = 8;   // -aim to ANY attacker firing at a CoolHeaded soldier
    // ---- build-variety perks: pure CRIT/AIM reads in Combat.ComputeOdds (no new state/hooks) ----
    // First Strike (enum member is still `GiantSlayer` for save-ordinal stability; reworked from the old
    // dead "+aim vs MaxHp>=12" — ~70% of foes are sub-12 fodder, so it almost never fired). New effect: an
    // ALPHA-STRIKE/OPENER — +crit vs a target at FULL HP. Fires on the FIRST connecting shot at any fresh
    // enemy (fodder or boss), rewarding focus-firing a new target; it goes inert once the target is chipped
    // (the opposite axis from Executioner's sub-half-HP finisher). Read in Combat.ComputeOdds via d.Hp>=MaxHp.
    public const int FirstStrikeCrit = 15;
    // BULWARK (reworked -> "PLATING"): an ABLATIVE-armor survivability perk. The old "-2 while
    // hunkered" was a dead pick (flywheel 5x) — the aggressive meta almost never spends a turn to
    // hunker, so the condition rarely fired. New effect: while this soldier is at/above HALF HP its
    // armor plating is intact and absorbs an EXTRA BulwarkFlat off every incoming hit — NO stance
    // required, so a frontline soldier benefits just by leading the push. Once chipped below half
    // HP the plating is spent (the bonus drops off), giving it a distinct DURABILITY CURVE: it keeps
    // a healthy point-soldier healthy (front-loaded) but fades exactly when Hardened/Tank matter
    // most. Distinct from Hardened (always-on, crit-weighted) and Tank (+max HP, no per-hit cut).
    // Read in Combat.HardenedReduce off d.Hp/d.MaxHp (already on the defending Unit — no new hook).
    public const int BulwarkFlat = 2;    // extra -damage on every incoming hit while at/above half HP
    // ---- three MORE build-variety perks: pure CRIT/AIM reads in Combat.ComputeOdds (no new state/hooks) ----
    // VANTAGE: a HIGH-GROUND specialist. +crit ONLY when this attacker fires from elevated terrain
    // (highGround already computed in ComputeOdds). Distinct from the always-on HighGroundCrit
    // situational bonus: this is an EARNED payoff that turns holding the vantage into a build, and it
    // is inert on the flat ground everyone else fights on — a positioning reward, not a free stack.
    public const int VantageCrit = 15;   // +crit while firing from high ground (elevation build)
    // BREAKER: a COMBINED-ARMS finisher — +crit vs a target the squad has SUPPRESSED or PINNED
    // (d.Suppress>0 || d.Pinned>0, both set by a gunner's suppress/pin verb). Rewards the setup shot
    // AFTER the gunner locks a foe down; inert vs an unrattled enemy. A pin-punisher axis, orthogonal
    // to the HP-based (Executioner/First Strike) and cover-based (LockOn) crit perks.
    public const int BreakerCrit = 20;   // +crit vs a suppressed/pinned target (punish the pinned)
    // SIEGEBREAKER: an ANTI-TURTLE aim perk. +aim vs a HUNKERED target — a hunkered foe costs the
    // attacker -25 aim, so this claws back a chunk and rewards cracking a defensive/camped enemy.
    // Distinct axis (the TARGET's stance) from the range/cover aim perks (LockOn/CloseQuarters/Marksman);
    // inert vs any active (non-hunkered) foe, so it's a situational pick, not a flat aim upgrade.
    public const int SiegebreakerAim = 15; // +aim vs a hunkered target (dig them out)
    public const int WoundAim = 12;      // aim penalty while Wound > 0
    public const int WoundMob = 1;       // mobility penalty while Wound > 0

    // trait + bond magnitudes (read in Combat.ComputeOdds; one source of truth)
    public const int KillerAim = 12;     // Killer: +aim vs targets already below half HP
    public const int ColdBloodCrit = 15; // ColdBlood: +crit while bloodied (self <= half HP)
    public const int VengefulAim = 12;   // Vengeful: +aim while a squadmate has fallen this mission
    public const int IronWillHp = 2;     // IronWill: permanent +max HP (granted at debrief)
    public const int BondAim = 10;       // Bond: +aim while a bonded squadmate is adjacent

    // SCAR magnitudes (W5) — read in Combat.ComputeOdds / Unit.MoveBudget / Unit.AddStatus.
    public const int ShellShockMob   = 1;   // SHELL-SHOCKED: -mob (lasting caution); + immune to Disorient/Stun
    public const int BurnScarHp      = 3;   // BURN-SCARRED: +max HP scar tissue (applied once on grant, like IronWill)
    public const int BurnShyAim      = 8;   // BURN-SCARRED: -aim while Burning (fire-shy)
    public const int HardBittenCrit  = 12;  // HARD-BITTEN: +crit while bloodied (<= half HP)
    public const int HardBittenFullAim = 5; // HARD-BITTEN: -aim at FULL HP (only fights well when it's grim)
    public const int VendettaAim     = 10;  // VENDETTA: +aim vs the faction that scarred this soldier
    public const int VendettaCrit    = 8;   // VENDETTA: +crit vs that faction

    // CORPSMAN PATCH ability: HP restored to the most-wounded adjacent squadmate (capped at MaxHp)
    public const int PatchHeal = 4;

    // status-effect magnitudes (3.5)
    public const int BurnDamage = 2;     // Burning: HP lost at the unit's turn start
    public const int BleedDamage = 1;    // Bleed: HP lost per tile moved
    public const int DisorientAim = 15;  // Disoriented: aim penalty (+ no overwatch)

    // UNDERTOW W3 — ROUT: a broken enemy fights wild. While Routed>0 (set when its pod's morale
    // breaks — see Game.BreakPodMorale), it flees toward its own edge, won't hold overwatch, and
    // shoots at a heavy aim penalty. It counts down one of the unit's turns at a time and RALLIES at 0.
    public const int RoutAim = 18;       // aim penalty while routed (a panicked unit can't shoot straight)

    public void BeginTurn()
    {
        if (AbilityCd > 0) AbilityCd--;   // signature ability cools down one of THIS unit's turns
        if (Routed > 0) Routed--;         // UNDERTOW W3: a routed pod rallies one turn at a time
        ActionsLeft = 2;
        OnOverwatch = false;
        OwFocused = false;         // focused-overwatch cone is per-arming (same lifecycle as OnOverwatch)
        OwBrace = false;           // UNDERTOW W2: brace is per-arming, same lifecycle as OnOverwatch
        Hunkered = false;
        ReactedThisTurn = false;
        ShovedThisTurn = false;    // SHOVE: one per soldier per turn
        DragsThisTurn = 0;         // FIELD CRAFT: DRAG Combat.FieldCraftLimit(u)/turn (1; FIELD DRILLS 2)
        VaultsThisTurn = 0;        // FIELD CRAFT: VAULT Combat.FieldCraftLimit(u)/turn (1; FIELD DRILLS 2)
        DrilledThisTurn = false;   // FUL-6 FIELD DRILLS: the +1-move drill is per-turn
        MovedThisTurn = false;     // W10 BIPOD: the planted-shooter aim bonus re-arms each turn
        FiredThisTurn = false;     // TEMPO: one offensive shot per turn (reset each turn)
        MovedAfterFire = false;    // HORIZON: exposed-by-fire flag is per-turn
        RunGun = false;            // ability stances don't carry between turns
        Blitz = false;
        Steady = false;
        Slipstreaming = false;     // ranger SLIPSTREAM is a one-move stance (consumed on use)
        KillsThisTurn = 0;         // multi-kill feat is per-turn
        FiredFromConcealment = false; // ambush bonus is for one shot only (4.4)
        // note: Suppress (a debuff applied by an enemy gunner) is cleared on the
        // victim's owner's next turn, NOT here, so it bites during the turn it's set.
    }
}

/// Names + one-line descriptions for promotion perks, and the perk pool.
public static class PerkDef
{
    // OFFERED perks. The Perk ENUM stays append-only (save compat), but we no longer OFFER the
    // redundant crit-perk cluster (Deadeye / Opportunist / PointBlank / Vanguard) — those were
    // 4 overlapping conditional-crit picks (false choices). The kept crit pair is build-defining
    // and mutually exclusive: EXECUTIONER (finisher, +crit vs sub-half-HP) vs FIRST STRIKE
    // (opener, +crit vs full-HP). Cut perks keep their enum members + Name/Code/Desc so any
    // already-saved soldier that owns one still loads and reads correctly.
    public static readonly Perk[] All =
    {
        Perk.LockOn, Perk.Hardened, Perk.Reflexes, Perk.Bandolier, Perk.CloseQuarters,
        Perk.Marksman, Perk.Tank, Perk.Sprinter, Perk.Adrenal,
        Perk.Executioner, Perk.Guardian, Perk.CoolHeaded,
        Perk.GiantSlayer,
        Perk.Bulwark,
        Perk.Skirmisher, Perk.Gunslinger,   // TEMPO wave 5: the two ways to spend the post-shot action
        Perk.Vantage, Perk.Breaker, Perk.Siegebreaker,  // HORIZON wave 6: elevation / pin-punish / anti-turtle
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
        Perk.Sprinter => "OUTRUNNER",
        Perk.Adrenal => "MOMENTUM",
        Perk.Executioner => "EXECUTIONER",
        Perk.Guardian => "GUARDIAN",
        Perk.CoolHeaded => "COOL-HEADED",
        Perk.Opportunist => "OPPORTUNIST",
        Perk.PointBlank => "POINT BLANK",
        Perk.GiantSlayer => "FIRST STRIKE",
        Perk.Bulwark => "PLATING",
        Perk.Vanguard => "VANGUARD",
        Perk.Skirmisher => "SKIRMISHER",
        Perk.Gunslinger => "GUNSLINGER",
        Perk.Vantage => "VANTAGE",
        Perk.Breaker => "BREAKER",
        Perk.Siegebreaker => "SIEGEBREAKER",
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
        Perk.Sprinter => "OUT",
        Perk.Adrenal => "MOM",
        Perk.Executioner => "EXC",
        Perk.Guardian => "GRD",
        Perk.CoolHeaded => "CLH",
        Perk.Opportunist => "OPP",
        Perk.PointBlank => "PBK",
        Perk.GiantSlayer => "FST",
        Perk.Bulwark => "PLT",
        Perk.Vanguard => "VAN",
        Perk.Skirmisher => "SKR",
        Perk.Gunslinger => "GUN",
        Perk.Vantage => "VNT",
        Perk.Breaker => "BRK",
        Perk.Siegebreaker => "SGE",
        _ => "?",
    };

    /// FUL-1: parse a SIGHTLINE_PERK env value (case-insensitive telemetry CODE, e.g. "RFX")
    /// to a Perk; unknown/null => null. Mirrors ContractDef.Parse so the Program.cs probe
    /// hook is a one-liner. Matches OFFERED perks only (All) — a cut perk can't be probed
    /// because the offer pool never presents it.
    public static Perk? Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        foreach (var p in All)
            if (string.Equals(Code(p), s, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    public static string Desc(Perk p) => p switch
    {
        Perk.LockOn => "+15 aim vs flanked targets",   // FUL-3: was "exposed" — stale since the UNDERTOW W5 de-superset (Combat gates on flanked)
        Perk.Hardened => "-1 damage taken, and -3 more from crits (tank)",
        Perk.Reflexes => "overwatch shots rarely miss",
        Perk.Bandolier => "+1 grenade each mission",
        Perk.CloseQuarters => "+15 aim within 4 tiles",
        Perk.Marksman => "+15 aim beyond 7 tiles",
        Perk.Deadeye => "+15 crit chance",
        Perk.Tank => "+3 max HP",
        Perk.Sprinter => "+1 mobility, and moving never draws overwatch fire",
        Perk.Adrenal => "a kill on your turn refunds +1 action (once per turn)",
        Perk.Executioner => "+25 crit vs targets below half HP (finisher)",
        Perk.Guardian => "overwatch reactions ignore the aim penalty + crit hard",
        Perk.CoolHeaded => "enemies shooting you take -8 aim; immune to Disoriented",
        Perk.Opportunist => "+18 crit vs flanked targets (out-positioned their cover)",
        Perk.PointBlank => "+20 crit within 2 tiles",
        Perk.GiantSlayer => "+15 crit vs full-HP targets (alpha strike on a fresh foe)",
        Perk.Bulwark => "-2 damage from every hit while at/above half HP (ablative plating)",
        Perk.Vanguard => "+28 crit vs adjacent flanked targets (breach and finish)",
        Perk.Skirmisher => "after you fire, your move this turn draws no overwatch (shoot, then slip away)",
        Perk.Gunslinger => "your rushed second shot each turn fires at full aim (double-tap)",
        Perk.Vantage => "+15 crit while firing from high ground (hold the vantage)",
        Perk.Breaker => "+20 crit vs a suppressed or pinned target (punish the pinned)",
        Perk.Siegebreaker => "+15 aim vs a hunkered target (dig them out)",
        _ => "",
    };
}

/// Names + codes + descriptions for CLASS SPECIALIZATION FORKS (W2), and the per-class option pairs.
/// Mirrors PerkDef. One source of truth, read by the barracks chooser (Hud) + telemetry (Stats).
public static class SpecDef
{
    /// The 2 forks offered to a soldier of class `cls` (empty if the class has no fork table).
    public static Spec[] OptionsFor(string cls) => cls switch
    {
        "ASSAULT"      => new[] { Spec.Breacher,    Spec.Juggernaut },
        "RANGER"       => new[] { Spec.Phantom,     Spec.Pathfinder },
        "SHARPSHOOTER" => new[] { Spec.Sentinel,    Spec.Headhunter },
        "GUNNER"       => new[] { Spec.AreaDenial,  Spec.Bulwark    },
        "CORPSMAN"     => new[] { Spec.FieldSurgeon, Spec.CombatMedic },
        _ => System.Array.Empty<Spec>(),
    };

    public static string Name(Spec s) => s switch
    {
        Spec.Breacher     => "BREACHER",
        Spec.Juggernaut   => "JUGGERNAUT",
        Spec.Phantom      => "PHANTOM",
        Spec.Pathfinder   => "PATHFINDER",
        Spec.Sentinel     => "SENTINEL",
        Spec.Headhunter   => "HEADHUNTER",
        Spec.AreaDenial   => "AREA DENIAL",
        Spec.Bulwark      => "ANCHOR",            // distinct display name from Perk.Bulwark ("PLATING")
        Spec.FieldSurgeon => "FIELD SURGEON",
        Spec.CombatMedic  => "COMBAT MEDIC",
        _ => "SPEC",
    };

    public static string Code(Spec s) => s switch
    {
        Spec.Breacher     => "BRC",
        Spec.Juggernaut   => "JUG",
        Spec.Phantom      => "PHN",
        Spec.Pathfinder   => "PTH",
        Spec.Sentinel     => "SNT",
        Spec.Headhunter   => "HHT",
        Spec.AreaDenial   => "ADN",
        Spec.Bulwark      => "ANC",
        Spec.FieldSurgeon => "SRG",
        Spec.CombatMedic  => "MED",
        _ => "?",
    };

    public static string Desc(Spec s) => s switch
    {
        Spec.Breacher     => "GRAPPLE also staggers: the foe loses overwatch + hunker and takes chip damage",
        Spec.Juggernaut   => "+2 innate armor, but GRAPPLE reach drops to adjacent only (must close in)",
        Spec.Phantom      => "after a SLIPSTREAM, your next shot strikes from ambush (+aim/+crit)",
        Spec.Pathfinder   => "SLIPSTREAM is truly free (0 actions) and cools down faster (3 -> 2)",
        Spec.Sentinel     => "your overwatch reactions ignore the aim penalty and crit hard (built-in Guardian)",
        Spec.Headhunter   => "your MARK also paints the foe for squad-wide +crit (not just +aim)",
        Spec.AreaDenial   => "SUPPRESSING FIRE pins a wider 5x5 footprint (denies a whole zone)",
        Spec.Bulwark      => "+2 innate armor, but SUPPRESSING FIRE pins only the single target (no splash)",
        Spec.FieldSurgeon => "PATCH also clears the patient's wound and all status effects (triage)",
        Spec.CombatMedic  => "PATCH reaches farther (2 tiles), can target yourself, but heals 1 less",
        _ => "",
    };

    /// A one-line evocative fantasy blurb for the chooser card (flavour above the mechanical Desc).
    public static string Fantasy(Spec s) => s switch
    {
        Spec.Breacher     => "Hit first, hit hard — leave them reeling.",
        Spec.Juggernaut   => "An armored wall that drags the fight to itself.",
        Spec.Phantom      => "Slip the lines, then strike from nowhere.",
        Spec.Pathfinder   => "Always moving. Never caught.",
        Spec.Sentinel     => "Nothing crosses your lane and lives.",
        Spec.Headhunter   => "Paint the target. The squad does the rest.",
        Spec.AreaDenial   => "Own the ground. Make them flinch.",
        Spec.Bulwark      => "The anchor that doesn't break.",
        Spec.FieldSurgeon => "Back in the fight, whole again.",
        Spec.CombatMedic  => "A medic who keeps shooting — and keeps everyone up.",
        _ => "",
    };
}

/// Names + descriptions + tuning + cost for persistent weapon upgrades (the Intel reward
/// sink). One source of truth for the magnitudes (read in Weapon.ApplyMods) AND the shop
/// UI / autopilot. Costs are tuned so a run can buy a few upgrades across the squad but not
/// everything (Intel stays scarce) — a leveled squad out-guns a fresh one, outpacing attrition.
public static class WeaponModDef
{
    public static readonly WeaponMod[] All =
        { WeaponMod.Scope, WeaponMod.ExtendedMag, WeaponMod.HollowPoint, WeaponMod.Stabilizer,
          WeaponMod.Bipod, WeaponMod.Suppressor };   // W10: positional + stealth builds (rule mods)

    // effect magnitudes (kept here so Weapon.ApplyMods + the shop description read one source)
    public const int ScopeAim = 12;         // SCOPE: +aim, and flattens long-range falloff (Weapon.Scoped)
    public const int MagClip = 2;           // EXTENDED MAG: +clip (fewer reloads = more shots/turn)
    public const int HollowCrit = 15;       // HOLLOW POINT: +crit chance...
    public const int HollowDmg = 1;         // ...and +1 to min & max damage
    public const int StabilizerAim = 6;     // STABILIZER: +aim...
    public const int StabilizerRange = 2;   // ...and +2 tiles of effective range
    // W10 BIPOD: +aim while the shooter hasn't moved this turn (one Combat.ComputeOdds read off
    // Unit.MovedThisTurn). Deliberate anti-synergy with EXPOSED BY FIRE: planting to shoot leaves
    // you easier to hit until you move — the bipod pays you to accept that exposure.
    public const int BipodAim = 10;

    public const int ScopeCost = 14;
    public const int MagCost = 10;
    public const int HollowCost = 14;
    public const int StabilizerCost = 12;
    public const int BipodCost = 10;
    public const int SuppressorCost = 12;

    public static int Cost(WeaponMod m) => m switch
    {
        WeaponMod.Scope => ScopeCost,
        WeaponMod.ExtendedMag => MagCost,
        WeaponMod.HollowPoint => HollowCost,
        WeaponMod.Stabilizer => StabilizerCost,
        WeaponMod.Bipod => BipodCost,
        WeaponMod.Suppressor => SuppressorCost,
        _ => 99,
    };

    public static string Name(WeaponMod m) => m switch
    {
        WeaponMod.Scope => "SCOPE",
        WeaponMod.ExtendedMag => "EXTENDED MAG",
        WeaponMod.HollowPoint => "HOLLOW POINT",
        WeaponMod.Stabilizer => "STABILIZER",
        WeaponMod.Bipod => "BIPOD",
        WeaponMod.Suppressor => "SUPPRESSOR",
        _ => "MOD",
    };

    // short tag for the dossier / roster
    public static string Code(WeaponMod m) => m switch
    {
        WeaponMod.Scope => "SCP",
        WeaponMod.ExtendedMag => "MAG",
        WeaponMod.HollowPoint => "HP",
        WeaponMod.Stabilizer => "STB",
        WeaponMod.Bipod => "BPD",
        WeaponMod.Suppressor => "SUP",
        _ => "?",
    };

    public static string Desc(WeaponMod m) => m switch
    {
        WeaponMod.Scope => $"+{ScopeAim} aim; holds accuracy at long range",
        WeaponMod.ExtendedMag => $"+{MagClip} clip (fewer reloads)",
        WeaponMod.HollowPoint => $"+{HollowCrit} crit, +{HollowDmg} damage",
        WeaponMod.Stabilizer => $"+{StabilizerAim} aim, +{StabilizerRange} range",
        WeaponMod.Bipod => $"+{BipodAim} aim while this soldier hasn't moved this turn",
        WeaponMod.Suppressor => "Ambush shots wake only the target's pod, not every pod in sight",
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

/// Names + codes + descriptions for earned SCARS (W5), and the trauma that grants each.
/// Mirrors TraitDef. Codes are 3-letter, chosen NOT to collide with the status codes
/// (BRN already = Burning), so BURN-SCARRED uses "SCR" rather than "BRN".
public static class ScarDef
{
    public static string Name(Scar s) => s switch
    {
        Scar.ShellShocked => "SHELL-SHOCKED",
        Scar.BurnScarred  => "BURN-SCARRED",
        Scar.HardBitten   => "HARD-BITTEN",
        Scar.Vendetta     => "VENDETTA",
        _ => "SCAR",
    };

    public static string Code(Scar s) => s switch
    {
        Scar.ShellShocked => "SHK",
        Scar.BurnScarred  => "SCR",   // NOT "BRN" — that's the Burning status code
        Scar.HardBitten   => "GRZ",
        Scar.Vendetta     => "VND",
        _ => "?",
    };

    public static string Desc(Scar s) => s switch
    {
        Scar.ShellShocked => $"-{Unit.ShellShockMob} mobility, but immune to Disorient + Stun",
        Scar.BurnScarred  => $"+{Unit.BurnScarHp} max HP, but -{Unit.BurnShyAim} aim while burning",
        Scar.HardBitten   => $"+{Unit.HardBittenCrit} crit while bloodied, -{Unit.HardBittenFullAim} aim at full HP",
        Scar.Vendetta     => $"+{Unit.VendettaAim} aim / +{Unit.VendettaCrit} crit vs the faction that scarred you",
        _ => "",
    };

    // short note describing the trauma that earns the scar (barracks report)
    public static string Trauma(Scar s) => s switch
    {
        Scar.ShellShocked => "surviving two brushes with death",
        Scar.BurnScarred  => "walking out of the fire",
        Scar.HardBitten   => "a third brush with death",
        Scar.Vendetta     => "a grudge against the foe that nearly took them",
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
