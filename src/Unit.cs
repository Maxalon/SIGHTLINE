using System;
using System.Numerics;

namespace Sightline;

public enum Team { Player, Enemy }

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
public enum WeaponMod { Scope, ExtendedMag, HollowPoint, Stabilizer }

/// Promotion perks: a soldier picks one each rank-up (see Run / barracks).
/// APPEND-ONLY: enum ordinals are the save keys (SaveGame stores perks by (int)Perk),
/// so new members go at the END — never reorder or remove the existing ones.
public enum Perk { LockOn, Hardened, Reflexes, Bandolier, CloseQuarters, Marksman, Deadeye, Tank, Sprinter, Adrenal,
    Executioner, Guardian, CoolHeaded,
    Opportunist, PointBlank, GiantSlayer,
    Bulwark, Vanguard }

/// Battlefield traits earned by FEATS (see Game feat hooks + Run.DebriefSurvivors).
/// Each is a small passive read in Combat.ComputeOdds, so veterans matter.
public enum Trait { Killer, ColdBlood, IronWill, Vengeful }

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
                return (int)Util.Clamp((dist - 3) * 3, -15, 18);   // already long-ranged; scope adds none here
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
        WeaponKind.Sniper  => New("Marksman",k, 5, 8, 5, 20, 3),
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

    // class signature ability (see AbilityKind); charge refilled each mission
    public int AbilityCharge;
    public bool RunGun;         // assault: next shot costs 1 action, doesn't end the turn
    public bool Blitz;          // ranger: next move costs one action less
    public bool Steady;         // sharpshooter: next shot gets +aim/+crit
    public int  Suppress;       // gunner debuff currently ON this unit (aim penalty)
    public bool Marked;         // sharpshooter MARK: this FOE is designated -> whole squad +aim/+crit vs it
                                // (per-mission, set on an enemy by DoAbility(Mark), cleared at the marker's next turn)
    public bool Slipstreaming;  // ranger SLIPSTREAM: this soldier's current free move is silent (no overwatch
                                // provoked) — set by DoAbility(Slipstream), consumed/cleared by the move it covers
    public int  Pinned;         // gunner SUPPRESSING FIRE: this FOE is pinned (turns remaining). While > 0 it
                                // takes the Suppress aim debuff AND cannot use a 2-action DASH (area denial).
                                // Decays one turn at the pinned unit's BeginTurn; never persisted (per-mission).

    // optional player-authored role label (overrides the auto strength tags in the
    // roster/dossier when set); persists across the run
    public string CustomTag;

    // promotion perks (persist across the run); pick one per rank-up
    public System.Collections.Generic.List<Perk> Perks = new();
    public bool HasPerk(Perk p) => Perks.Contains(p);

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
        foreach (var s in Statuses) if (s.Kind == k) { s.Turns = Math.Max(s.Turns, turns); return; }
        Statuses.Add(new Status { Kind = k, Turns = turns });
    }

    // transient per-mission feat tracking (reset in Game.SetupMission; never persisted)
    public bool FeatMultiKill;  // 2+ kills in a single turn this mission
    public bool FeatClutch;     // a kill while bloodied (<= 1/4 HP)
    public bool FeatVengeful;   // a kill after a squadmate fell this mission
    public bool WasNearDeath;   // dropped to <= 1/4 HP at some point this mission (survived = feat)
    public bool AllyDown;       // a squadmate has been killed this mission
    public int KillsThisTurn;   // reset each BeginTurn (multi-kill detection)
    public bool BondAura;       // a bonded squadmate is adjacent (refreshed each frame by Game)
    public bool FiredFromConcealment; // true for ONE shot after breaking concealment (4.4)

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
        AbilityKind.Slipstream => "Free long move: doesn't end your turn AND draws no overwatch",
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
    public bool Hunkered;
    public bool ReactedThisTurn; // overwatch fired this round
    // SHOVE (forced-movement verb): a soldier may shove at most ONCE per turn. Combined with
    // "shove always costs 1 action" this double-bounds it (no infinite reposition loop). Reset
    // every BeginTurn; never persisted (per-turn combat state only).
    public bool ShovedThisTurn;
    public bool Alive = true;

    // Awareness tier (4.3): enemies escalate Unaware -> Suspicious -> Alert instead of
    // waking instantly. Active (acts in combat / is a live threat) == fully Alert, so the
    // many read sites that gate on "is this enemy awake" keep working unchanged.
    public AlertLevel Alert = AlertLevel.Alert;
    public bool Active => Alert == AlertLevel.Alert;
    public int PodId = -1;      // activation-pod grouping (enemies only)

    public bool IsVip;          // escort objective: the asset to extract (mission-only, never persists)
    public bool Enraged;        // elite boss: one-time low-HP rage trigger
    public int ShieldDx, ShieldDy;  // SHIELD archetype: facing dir its frontal shield blocks (3.7)

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

    public int MoveBudget => Math.Max(1, Mobility - (Wound > 0 ? WoundMob : 0)) * 2;  // half-tile budget (−mob while wounded)
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
    public const int PerkCrit = 15;      // Deadeye (unconditional crit)
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
    // Opportunist: a FLANKER'S FINISHER — +crit ONLY vs a genuinely FLANKED target (cover.Flanked: the
    // foe HAD adjacent cover but you reached an angle it doesn't protect). Distinct from LockOn (+AIM vs
    // ANY no-cover target — exposed OR flanked) and Deadeye (+crit unconditionally): Opportunist rewards
    // the *maneuver that turns a covered foe's flank*, so it pays off exactly when you out-positioned cover.
    public const int OpportunistCrit = 18;
    // Point Blank: a CLOSE-RANGE CRIT build — +crit within 2 tiles. Distinct from CloseQuarters
    // (+AIM within 4 tiles, a wider band that helps you hit): Point Blank is tighter and adds CRIT,
    // so a shotgun/assault rusher hits HARDER in your face rather than just more reliably nearby.
    public const int PointBlankCrit = 20;
    public const int PointBlankRange = 2;   // crit applies at dist <= 2 tiles
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
    // VANGUARD: an AGGRESSION/breach perk for a flanker who closes the distance. +crit ONLY when the
    // target is BOTH genuinely FLANKED (cover.Flanked — you out-positioned its cover) AND ADJACENT
    // (dist <= 1, point-blank). Distinct from Opportunist (+crit on a flank at ANY range) and Point
    // Blank (+crit within 2 tiles vs ANY target, no flank needed): Vanguard demands you both flank
    // AND get in its face, the tightest gate of the three, so it pays the biggest crit. A pure
    // ComputeOdds read (flank flag + range), no new state.
    public const int VanguardCrit = 28;
    public const int VanguardRange = 1;  // crit applies at dist <= 1 tile (adjacent) AND flanked
    public const int WoundAim = 12;      // aim penalty while Wound > 0
    public const int WoundMob = 1;       // mobility penalty while Wound > 0

    // trait + bond magnitudes (read in Combat.ComputeOdds; one source of truth)
    public const int KillerAim = 12;     // Killer: +aim vs targets already below half HP
    public const int ColdBloodCrit = 15; // ColdBlood: +crit while bloodied (self <= half HP)
    public const int VengefulAim = 12;   // Vengeful: +aim while a squadmate has fallen this mission
    public const int IronWillHp = 2;     // IronWill: permanent +max HP (granted at debrief)
    public const int BondAim = 10;       // Bond: +aim while a bonded squadmate is adjacent

    // CORPSMAN PATCH ability: HP restored to the most-wounded adjacent squadmate (capped at MaxHp)
    public const int PatchHeal = 4;

    // status-effect magnitudes (3.5)
    public const int BurnDamage = 2;     // Burning: HP lost at the unit's turn start
    public const int BleedDamage = 1;    // Bleed: HP lost per tile moved
    public const int DisorientAim = 15;  // Disoriented: aim penalty (+ no overwatch)

    public void BeginTurn()
    {
        ActionsLeft = 2;
        OnOverwatch = false;
        Hunkered = false;
        ReactedThisTurn = false;
        ShovedThisTurn = false;    // SHOVE: one per soldier per turn
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
        _ => "?",
    };

    public static string Desc(Perk p) => p switch
    {
        Perk.LockOn => "+15 aim vs exposed targets",
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
        { WeaponMod.Scope, WeaponMod.ExtendedMag, WeaponMod.HollowPoint, WeaponMod.Stabilizer };

    // effect magnitudes (kept here so Weapon.ApplyMods + the shop description read one source)
    public const int ScopeAim = 12;         // SCOPE: +aim, and flattens long-range falloff (Weapon.Scoped)
    public const int MagClip = 2;           // EXTENDED MAG: +clip (fewer reloads = more shots/turn)
    public const int HollowCrit = 15;       // HOLLOW POINT: +crit chance...
    public const int HollowDmg = 1;         // ...and +1 to min & max damage
    public const int StabilizerAim = 6;     // STABILIZER: +aim...
    public const int StabilizerRange = 2;   // ...and +2 tiles of effective range

    public const int ScopeCost = 14;
    public const int MagCost = 10;
    public const int HollowCost = 14;
    public const int StabilizerCost = 12;

    public static int Cost(WeaponMod m) => m switch
    {
        WeaponMod.Scope => ScopeCost,
        WeaponMod.ExtendedMag => MagCost,
        WeaponMod.HollowPoint => HollowCost,
        WeaponMod.Stabilizer => StabilizerCost,
        _ => 99,
    };

    public static string Name(WeaponMod m) => m switch
    {
        WeaponMod.Scope => "SCOPE",
        WeaponMod.ExtendedMag => "EXTENDED MAG",
        WeaponMod.HollowPoint => "HOLLOW POINT",
        WeaponMod.Stabilizer => "STABILIZER",
        _ => "MOD",
    };

    // short tag for the dossier / roster
    public static string Code(WeaponMod m) => m switch
    {
        WeaponMod.Scope => "SCP",
        WeaponMod.ExtendedMag => "MAG",
        WeaponMod.HollowPoint => "HP",
        WeaponMod.Stabilizer => "STB",
        _ => "?",
    };

    public static string Desc(WeaponMod m) => m switch
    {
        WeaponMod.Scope => $"+{ScopeAim} aim; holds accuracy at long range",
        WeaponMod.ExtendedMag => $"+{MagClip} clip (fewer reloads)",
        WeaponMod.HollowPoint => $"+{HollowCrit} crit, +{HollowDmg} damage",
        WeaponMod.Stabilizer => $"+{StabilizerAim} aim, +{StabilizerRange} range",
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
