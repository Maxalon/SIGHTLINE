using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

public enum Phase { Intro, PlayerTurn, EnemyTurn, Barracks, Win, Lose, Draft, WarRoom, Codex, SkirmishSetup, AudioCheck }   // WarRoom (W3), Codex (W6), SkirmishSetup (W4), AudioCheck (A3) appended; none persisted
// APPEND-ONLY: serialized as a raw (int) in SaveGame (CardDto.Objective). Never reorder or
// remove a member — a saved run stores the ordinal, so a reorder silently corrupts the loaded
// objective. Add new objectives at the END only. (SaveGame.SelfTest asserts the tail ordinal.)
public enum Objective { Eliminate, Evac, Hack, Escort, Sabotage, Rescue, Defend, Decapitate }
// APPEND-ONLY: treat like the persisted enums above (new members at the END only; never reorder or
// remove — SaveGame.SelfTest guards the tail ordinal). W10 appends three PLAYSTYLE bonuses: Ghost
// (stay concealed through turn 2), Demolition (destroy 3 cover/barrels), Bounty (kill the mission's
// named specialist, picked in RollSecondary).
public enum SecondaryKind { None, NoLosses, Swift, CleanSweep, Ghost, Demolition, Bounty }  // optional per-mission bonus goal (3.9)
// PROGRAM HORIZON W2/W4: game modes. Campaign = the 6-mission run (all prior behaviour); Endless =
// LAST STAND horde survival on one arena; Skirmish = a SINGLE-MISSION mode (both free SKIRMISH and the
// seeded DAILY, differentiated by Game.DailyMode). APPEND-ONLY (Mode isn't persisted, but keep it stable).
// T1: Training appended at the END (GameMode is not persisted anywhere — verified against
// SaveGame/Run/Stats — but the append-only habit costs nothing and protects the next reader).
public enum GameMode { Campaign, Endless, Skirmish, Training }
enum AiStage { PickNext, Telegraph, ActAfterMove }

/// RESONANCE T2 — the INCOMING-FIRE FORECAST for one board tile: everything the player needs to
/// answer "what happens to me if I stand HERE?", derived from the SAME Combat.ComputeOdds the
/// shot tooltip uses (so the read side can never drift from the resolver).
///
/// The old model was a single bool ("some enemy sees this tile and it has no cover"), which made a
/// tile covered from one gun but enfiladed by four others read completely clean. This carries the
/// count, the worst hit%, the expected damage and the flank/reaction state instead.
public struct ThreatCell
{
    public byte Guns;        // how many live, ACTIVE, armed hostiles can actually shoot a soldier standing here
    public sbyte BestHit;    // the best (highest) enemy hit% among those guns, 0 when none
    public float ExpDmg;     // TRUE expected incoming damage if every bearing gun fires once (post-armor,
                             // crit- and graze-inclusive -- Combat.ExpectedDamage; R2 FIX 2)
    public bool Flanked;     // at least one bearing gun would have the mover FLANKED (cover negated)
    public bool Exposed;     // at least one bearing gun sees the mover with NO cover at all (the pre-T2 bool, now via ComputeOdds so see-over/DRONE/SHIELD count)
    public bool Watched;     // the tile sits inside a live enemy OVERWATCH / braced (PIKEMAN) reaction lane
    public string WorstCls;  // archetype of the gun with the best hit% (named on the hover card)
    /// Danger grade 0..3 — the pip count. 0 = clean, 3 = three or more guns bear.
    public int Tier => Guns >= 3 ? 3 : Guns;
    public bool Any => Guns > 0 || Watched;
}

public partial class Game
{
    // PROGRAM HORIZON W2: which mode this game instance is running. Default Campaign keeps every
    // existing path byte-identical; the endless logic lives in the Game.Endless.cs partial.
    public GameMode Mode = GameMode.Campaign;
    public int Wave;   // LAST STAND: current wave (0 until the first spawns). HUD/end card read it.
    public Grid Grid = new();
    public List<Unit> Players = new();
    public List<Unit> Enemies = new();
    public Fx Fx = new();

    public Phase Phase = Phase.Intro;
    public Team AnimOwner = Team.Player;

    readonly List<Anim> _anims = new();
    public Anim ActiveAnim => _anims.Count > 0 ? _anims[0] : null;

    // Animation-speed toggle (QoL fast-forward). A player-cyclable multiplier (1x/2x/3x) that
    // scales ONLY the animation-queue stepping (move steps / shots / grenades / shove) and the
    // hit-stop decay — NOT the whole game tick, so the autopilot/timers/sim are unaffected.
    // Default 1f, and nothing reads it unless the key is pressed, so the headless screenshot
    // path stays byte-stable and the autoplay smoke test is unchanged.
    /// W5 COMFORT: the animation-playback multiplier. The VALUE lives in Display (persisted in
    /// display.json, cycled from the pause menu or [F2]); this property is the single read point
    /// and it HARD-PINS 1x for the headless harness and the autopilot. That gate is load-bearing:
    /// SIGHTLINE_BALANCE / autoplay / screenshot runs must step the queue at exactly the pace they
    /// always did, or every measured number in docs/ shifts. Display.Init(false) also never Loads,
    /// so a headless process cannot pick a speed up off disk either — belt and braces.
    /// Harness-only escape hatch for the animation-speed FILMSTRIP (SIGHTLINE_ANIMSPEED, shot mode
    /// only). The pin below is what keeps the flywheel honest, so the filmstrip cannot simply turn
    /// it off — instead it names an explicit speed here. Default 0 = inert, so autoplay, the balance
    /// batch and every other headless path are untouched (ONRAMPTEST asserts the pin with it unset).
    public float AnimSpeedOverride;
    public float AnimSpeed => AnimSpeedOverride > 0f ? AnimSpeedOverride
                            : (AutoPlay || NoPersist) ? 1f : Display.AnimSpeed;
    public void CycleAnimSpeed() { if (!AutoPlay && !NoPersist) Display.CycleAnimSpeed(); }

    // selection / hover
    public Unit Selected;
    public int HoverX, HoverY;
    public bool HoverValid;
    public int[,] MoveCost;
    // RESONANCE T2: the per-tile INCOMING-FIRE FORECAST over reachable tiles (null when the
    // preview is off / nothing is selected). Recomputed only when the board actually changes
    // (see ComputeThreat's signature cache) — NOT every frame.
    public ThreatCell[,] Threat;
    public double ThreatMs;         // wall-clock cost of the last real forecast rebuild (perf probe)
    public int ThreatRebuilds;      // how many rebuilds happened (cache-miss counter, for the perf probe)
    (int, int)[,] _cameFrom;
    public List<(int x, int y)> PathPreview = new();

    // aiming
    public bool AimMode;
    public Unit AimTarget;
    public bool AimValid;
    public bool ShowOdds;
    public ShotOdds HoverOdds;

    // Per-turn DEPTH (Wave 3): aimed-vs-snap shot.
    // The normal "FIRE" is the AIMED shot (full aim, ends the turn). SNAP is a second fire
    // option that costs only ONE action and does NOT end the turn, at an aim penalty — so
    // "shoot" is a real choice every turn: the reliable aimed shot, or snap-fire and keep
    // acting (reposition / second snap / overwatch). Both share the one aim-targeting mode
    // (AimMode); SnapShot just records which variant the pending shot is. RUN&GUN is the
    // free, no-penalty version and takes priority over the snap penalty when both are set.
    public const int SnapAim = -15;   // snap-fire aim penalty (applied as the Resolve aimMod)
    public bool SnapShot;             // the pending aim-mode shot is a snap (1 action, no end-turn)

    // Flank-kill action refund ("press the advantage", Wave 3 anti-turtle). A player shot
    // that KILLS a FLANKED/EXPOSED target refunds +1 action to the shooter, capped at one
    // refund per soldier per turn (this set, cleared each StartPlayerTurn). Rewards aggressive
    // flanking + chains (snap -> flank-kill -> refund -> act again), out-competing passive
    // overwatch-camping. The 1/turn cap + finite enemies guarantee no infinite loop.
    readonly HashSet<Unit> _refundedThisTurn = new();

    // grenade targeting
    public const int GrenadeRange = 7;
    public bool GrenadeMode;
    public int GrenTx, GrenTy;
    public bool GrenValid;

    // utility-item targeting (3.4): a second throwable slot (smoke / flash / barricade)
    public const int ItemRange = 7;
    public bool ItemMode;
    public int ItemTx, ItemTy;
    public bool ItemValid;

    // SHOVE targeting (forced-movement verb): pick an ADJACENT enemy to shove one tile away.
    // Mirrors the ItemMode targeting pattern (ToggleShove/ShoveTargetOk/IssueShove + an action
    // key + every mode-reset site). ShoveTarget is the hovered adjacent enemy (null = no valid
    // target under the cursor); ShoveValid gates the click. Costs 1 action, never ends the turn,
    // 1 use per soldier per turn (Unit.ShovedThisTurn) — bounded, no infinite reposition loop.
    // Reach is Chebyshev <= ShoveReach (2) so it's usable at typical engagement range, not just
    // point-blank; the PUSH is still a single tile directly away from the shover.
    public const int ShoveReach = 2;
    public bool ShoveMode;
    public Unit ShoveTarget;
    public bool ShoveValid;

    // MARK targeting (sharpshooter ability VERB): designate a foe in line of sight -> the whole
    // squad gets +aim/+crit vs it (Combat.MarkAim/MarkCrit) until the sharpshooter's next turn.
    // Mirrors the ShoveMode pattern (ToggleMark/MarkTargetOk/IssueMark + key 5 + reset sites).
    // A targeting verb, not a self-stance: it changes "who do we all shoot" on the board.
    public bool MarkMode;
    public Unit MarkTarget;     // hovered enemy under the cursor while in MarkMode (null = none)
    public bool MarkValid;      // gates the click (target is a legal MARK target)
    Unit _markedBy;             // the sharpshooter who placed the current MARK (clears it on their next turn)

    // GRAPPLE targeting (assault ability VERB): yank a nearby foe ONE tile toward you, out of its
    // cover (reuses ShoveAnim with the pull direction = sign(assault - target)). A forced-movement
    // repositioning toy. Reach is Chebyshev <= GrappleReach; 1 use/soldier/turn (Unit.ShovedThisTurn,
    // shared with SHOVE so the two repositioning verbs share one anti-loop budget).
    public const int GrappleReach = 2;
    /// Effective GRAPPLE reach for `u`: 1 (adjacent only) for a JUGGERNAUT (the armor fork's verb
    /// nerf — it must close in), else GrappleReach. Single source of truth (GrappleTargetOk + the
    /// Renderer grapple-highlight both call this). Inert on Spec.None.
    public int GrappleReachFor(Unit u) => u != null && u.HasSpec(Spec.Juggernaut) ? 1 : GrappleReach;
    public bool GrappleMode;
    public Unit GrappleTarget;
    public bool GrappleValid;

    // SUPPRESSING FIRE targeting (gunner ability VERB): paint a foe in line of sight -> it AND every
    // enemy Chebyshev-adjacent to it is PINNED next turn (Combat.SuppressAim penalty + cannot DASH —
    // EnqueuePlannedMove caps a pinned foe to a single-action move). An AREA-DENIAL verb: it controls a
    // zone of the board, not a single foe. Mirrors the MarkMode targeting pattern; the gunner also trains
    // overwatch on the painted tile. Costs the action + ends the turn (it's the full suppression burst).
    public const int PinRange = 10;        // designation range (a suppressing burst reaches out)
    public bool PinMode;
    public Unit PinTarget;                 // hovered enemy under the cursor while in PinMode (null = none)
    public bool PinValid;                  // gates the click (target is a legal SUPPRESS target)
    public const int PinTurns = 2;         // FUL-2 comment truth: nothing decrements Pinned — any value >0 pins
                                           // through the enemy turn and ClearPins() lifts ALL pins at the next
                                           // StartPlayerTurn. The const is a flag, not a duration.

    // DRAG targeting (FIELD CRAFT W1, universal): pull a LAGGING ally (Chebyshev 1..DragReach) one
    // tile TOWARD the dragger. Reach-2 is the useful version: a Chebyshev-2 ally is pulled to the
    // tile 1 away (legal + useful — closes the gap); an already-adjacent (Chebyshev-1) ally has its
    // only toward-tile == the dragger's own tile, so it's illegal (correct: you can't pull someone
    // already beside you). Mirrors the ShoveMode pattern (ToggleDrag/DragTargetOk/IssueDrag + key 7 +
    // every mode-reset site). Costs 1 action, never ends the turn, once/soldier/turn (DraggedThisTurn).
    public const int DragReach = 2;
    public bool DragMode;
    public Unit DragTarget;
    public bool DragValid;

    // VAULT targeting (FIELD CRAFT W1, universal): leap an adjacent cover tile to the empty floor on
    // its far side (a straight 2-tile hop with a cover tile between). A NEW capability — all cover
    // blocks movement, so vaulting crosses an otherwise-impassable screen. Mirrors the ItemMode tile-
    // targeting pattern (ToggleVault/VaultTargetOk/IssueVault + key 9 + reset sites). Once per turn
    // (Unit.VaultedThisTurn), 1 action, never ends the turn.
    public bool VaultMode;
    public int VaultTx, VaultTy;
    public bool VaultValid;

    // SLIPSTREAM (ranger ability VERB): a free, overwatch-immune long move. Set on the soldier by
    // DoAbility(Slipstream); IssueMove reads it to make the next move cost 0 actions, and OnUnitEnteredTile
    // skips overwatch reactions while the mover is slipstreaming. The flag is consumed when the move's
    // destination tile is reached (or at BeginTurn). _slipDest tracks that destination so a multi-tile
    // slipstream move stays silent across all its steps, then clears cleanly.
    (int x, int y)? _slipDest;

    // banner
    public string BannerText = "";
    public float BannerTimer, BannerMax;
    public bool BannerEnemy;
    // W11: optional smaller second line under the banner (the NEW CONTACT ID line). Cleared by
    // ShowBanner so an ordinary banner never inherits a stale sub-line.
    public string BannerSub;

    // ai staging
    AiStage _aiStage;
    List<Unit> _aiUnits = new();
    int _aiIdx;
    EnemyPlan _aiPlan;

    // ---- enemy-intent telegraph (Into-the-Breach fairness lever) ----
    // Right before a hostile acts, we hold for a brief beat and show the player exactly what
    // it intends to do: its planned move PATH, its TARGET, and the tiles it will threaten from
    // its post-move tile. The renderer reads these; they are set in UpdateEnemy's PickNext (from
    // the SAME _aiPlan that then executes, so the telegraph never lies) and cleared the moment
    // the beat ends and at turn boundaries. The telegraph is SKIPPED entirely under AutoPlay so
    // the headless smoke test's frame counts stay essentially unchanged (TIMEOUT-safe).
    public Unit IntentUnit;                 // the hostile about to act (null = no telegraph showing)
    public EnemyPlan IntentPlan;            // its plan (Path / ShootTarget / Grenade / Overwatch / ...)
    public (int x, int y) IntentDest;       // the tile it will stand on after moving (threat origin)
    public const float TelegraphBeat = 0.5f;  // how long the intent is held before the unit acts

    // ---- squad coordination (computed once per enemy turn in PlanEnemySquad) ----
    // Shared, ADVISORY hints read by Ai.Plan; they bias per-unit scoring but never override
    // the fundamentals (having a shot, cover, advancing), so stall/timeout invariants hold.
    public Unit EnemyFocus;                                  // priority target the squad converges on
    public HashSet<(int, int)> PlayerOverwatchTiles = new(); // tiles under active player overwatch fire

    int _turnCount;

    /// W9 THE REPAIR — player turns taken since this RUN began, across every mission. Distinct from
    /// _turnCount, which SetupMission resets to 1 at every mission start AND at the mid-mission
    /// checkpoint redeploy. That reset is why the "never a RESULT: TIMEOUT" guarantee was false:
    /// AutoStallCheck's cap was PER-MISSION and re-armable, while the harness budget it claims to sit
    /// under (Program.cs autoCap) is a whole-campaign FRAME count. Reset only at a mode seam
    /// (ResetModeState), never by SetupMission. Public so the harness can report it.
    public int RunTurns { get; private set; }

    // campaign run
    Run _run = new();
    public Run RunState => _run;

    // run persistence: when false (normal play) the run is checkpointed to disk at
    // each mission start and CONTINUE is offered on the intro. The headless harness
    // sets this true so the smoke test never touches the save file.
    public bool NoPersist;

    // ---- Heat / Ascension difficulty ladder ----
    // UnlockedHeat = the highest selectable level (META: persisted in meta.json, survives run
    // end; rises by 1 when a run is WON at the current cap). PendingHeat = the level the player
    // has dialled in on the intro for the NEXT new run (0..UnlockedHeat). The CURRENT run's heat
    // lives on _run.HeatLevel (persisted in the run save). All default 0 -> heat 0 plays exactly
    // like today, which keeps the harness/autoplay path byte-stable.
    public int UnlockedHeat;
    public int PendingHeat;
    public int HeatLevel => _run?.HeatLevel ?? 0;   // the active run's heat (for HUD/barracks readouts)

    // barracks requisition shop: spend Intel before choosing the next deployment.
    // _shopDone gates the barracks flow (shop -> promotions -> deployment cards).
    bool _shopDone = true;
    public bool ShopDone => _shopDone;

    // ---- ARMORY (re-arm a soldier with a different weapon they can carry) ----
    // A sub-screen of REQUISITION: spend Intel to swap a soldier's weapon within their
    // class's thematic option set (Unit.ArmoryOptions). The chosen weapon persists on the
    // Unit (Run.Squad units survive across missions, and SaveGame round-trips Weapon.Kind),
    // so the player's pick sticks. A flat cost keeps it readable + balance-safe (you can only
    // pick within the role, not upgrade to a strictly-better gun).
    public const int ArmoryCost = 7;
    public bool ArmoryMode;          // true = the requisition screen shows the armory picker
    public Unit ArmorySoldier;       // the soldier being re-armed (null = pick one)
    public void ToggleArmory() { ArmoryMode = !ArmoryMode; ArmorySoldier = null; Audio.Play("select"); }
    public List<Unit> ArmoryRoster => _run.Squad.Where(u => !u.IsVip).ToList();

    /// Can this soldier be re-armed to weapon kind k right now? (different from current, affordable,
    /// and a legal option for their class.)
    public bool CanRearm(Unit u, WeaponKind k)
    {
        if (u == null || u.IsVip || u.Weapon == null) return false;
        if (u.Weapon.Kind == k) return false;                      // already carrying it
        if (_run.Intel < ArmoryCost) return false;
        return System.Array.IndexOf(Weapon.ArmoryOptions(u.Cls), k) >= 0;
    }

    /// Re-arm a soldier with a new weapon: pay Intel, swap the weapon, re-bake installed mods,
    /// reseed the clip. Persistent (the weapon is on the Unit; SaveGame stores Weapon.Kind).
    public void DoRearm(Unit u, WeaponKind k)
    {
        if (!CanRearm(u, k)) { Audio.Play("miss"); return; }
        _run.Intel -= ArmoryCost;
        Stats.RecordIntel(-ArmoryCost);   // FUL-13 intel cash-flow
        u.Weapon = Weapon.Make(k);
        u.RefreshWeaponMods();          // re-apply any installed mods onto the fresh weapon
        u.Ammo = u.Weapon.Clip;
        _run.Report.Add($"{u.Name} re-armed with {u.Weapon.Name}");
        Stats.RecordPurchase("ARMORY");
        Stats.RecordAction("REARM");   // W2 verb telemetry
        Audio.Play("select");
    }

    // The shop is two tiers: a fixed block of consumable/stat purchases (indices 0..ModBase-1)
    // then the PERSISTENT WEAPON UPGRADES (indices ModBase..) appended from WeaponModDef — the
    // run's real reward sink, so Intel buys permanent firepower that out-paces attrition. The
    // parallel arrays + the switch handlers below auto-cover the appended items, and the shop
    // card auto-sizes to ShopName.Length (Hud.DrawRequisition), so no UI rework is needed.
    public const int ModBase = 5;                              // count of fixed (non-mod) shop items
    static WeaponMod ModForItem(int item) => WeaponModDef.All[item - ModBase];
    public static bool IsModItem(int item) => item >= ModBase && item < ModBase + WeaponModDef.All.Length;

    // FACTION COUNTER-PREP item: a one-mission counter to the UPCOMING faction (telegraphed on the
    // campaign map). It's the LAST shop index — its name/desc/cost are dynamic (they depend on which
    // faction the squad is about to face), so the Hud reads them through ShopNameAt/DescAt/CostAt
    // instead of the static arrays. The static-array slots hold a neutral placeholder.
    public static readonly int PrepItem = ModBase + WeaponModDef.All.Length;
    public const int PrepCost = 12;                            // a one-mission situational edge, priced like FRAG CACHE
    static bool IsPrepItem(int item) => item == PrepItem;

    // ---------------- rotating requisition offer ----------------
    // Showing ALL ~10 items every barracks made BALLISTIC PLATING the obvious always-buy and let
    // the rest rot (measured: 486 plating buys vs ~zero of everything else over 40 runs). Instead
    // we present a ROTATING SLATE of ~5 items, chosen deterministically per barracks from
    // MapSeed+Mission (like the boon offer, so it round-trips on load and the harness is stable).
    // This turns "buy the obvious thing" into "prioritise among a limited slate". GUARANTEE: the
    // slate always includes a survivability option (FIELD MEDKIT and BALLISTIC PLATING), so the
    // player is never starved of healing/armor. Slot order in the returned list is the display
    // order (Hud) and the click/buy index space (HandleShopClick / AutoShop map slot -> item id).
    public const int ShopOfferSize = 5;          // target rotating-slate size (excl. the conditional PREP slot)
    List<int> _shopOfferCache;                   // recomputed each EnterBarracks (derived, not persisted)
    // W9 (SIGNAL): paid slate re-rolls THIS barracks — perturbs the slate seed so a re-roll actually
    // rotates the offer. Reset to 0 at EnterBarracks, so a re-load/round-trip reproduces the base
    // slate (the nonce is deliberately NOT persisted: a re-roll is a spent consumable, and on load
    // the worst case is the player seeing the base slate again having already paid — acceptable and
    // rare — versus threading a new field through the save format).
    int _shopReroll;

    /// The rotating requisition slate (a list of underlying item ids) for THIS barracks. Cached so
    /// it's stable across frames within a barracks; rebuilt by RefreshShopOffer at EnterBarracks /
    /// debug-shop entry. Falls back to building on demand (covers any path that reaches the shop
    /// without an explicit refresh).
    public List<int> ShopOffer()
    {
        if (_shopOfferCache == null) RefreshShopOffer();
        return _shopOfferCache;
    }

    /// (Re)build the rotating slate deterministically from MapSeed+Mission. Always seats the two
    /// survivability options first, then fills the remaining slots from a seed-shuffled pool of the
    /// rest, then appends the situational COUNTER-PREP slot only when a faction is telegraphed.
    public void RefreshShopOffer()
    {
        var offer = new List<int>();
        // (1) survivability guarantee: FIELD MEDKIT is always seated + first (healing is always
        // reasonable, never starved). BALLISTIC PLATING is NO LONGER hard-seated — armor was the
        // degenerate always-buy (the autopilot drained Intel into it every barracks, a non-decision).
        // PLATING now joins the rotating pool, so it competes for slate slots and appears only
        // sometimes — survivability-via-armor is a real choice, not a default.
        offer.Add(0);   // FIELD MEDKIT

        // (2) the rotating pool: everything else except prep (which is conditional + appended last).
        var pool = new List<int> { 1, 2, 3, 4 };              // stims / training / frag cache / BALLISTIC PLATING
        for (int i = 0; i < WeaponModDef.All.Length; i++) pool.Add(ModBase + i);  // weapon mods

        // deterministic shuffle off MapSeed+Mission so the slate is fixed per barracks + round-trips.
        // W9: a paid RE-ROLL bumps _shopReroll, perturbing the seed — still deterministic per
        // (barracks, reroll-count) so the slate is stable across frames within a barracks.
        int seed = (_run != null ? _run.MapSeed : 0) * 131 + (_run != null ? _run.Mission : 0) * 7 + 17
                   + _shopReroll * 7919;
        var rng = new Random(seed);
        for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }

        // W9 QUARTERMASTER (WAR ROOM unlock): +1 slate slot — more OPTIONS per barracks, still paid
        // for in Intel. Read once per (re)build, never per frame; NoPersist-gated so the flywheel/
        // autoplay slate is byte-identical to today.
        int slate = ShopOfferSize + (!NoPersist && SaveGame.HasUnlock((int)MetaUnlock.Quartermaster) ? 1 : 0);
        for (int i = 0; i < pool.Count && offer.Count < slate; i++) offer.Add(pool[i]);

        // (3) COUNTER-PREP: a situational extra slot, only when a faction is actually telegraphed
        // next (otherwise it's a dead, greyed row). It's an ADD-ON, not a slate slot it could crowd out.
        if (PrepFactionOffered != Faction.None) offer.Add(PrepItem);

        _shopOfferCache = offer;
    }

    public static readonly int[] ShopCost = BuildShopCost();
    public static readonly string[] ShopName = BuildShopName();
    public static readonly string[] ShopDesc = BuildShopDesc();

    static int[] BuildShopCost()
    {
        var b = new[] { 6, 8, 16, 8, 8 };    // STIMS 8 / FRAG CACHE 8 (revived sinks) +BALLISTIC PLATING (survivability, front-loaded cheap)
        var all = new int[ModBase + WeaponModDef.All.Length + 1];   // +1 for the dynamic PREP item
        b.CopyTo(all, 0);
        for (int i = 0; i < WeaponModDef.All.Length; i++) all[ModBase + i] = WeaponModDef.Cost(WeaponModDef.All[i]);
        all[ModBase + WeaponModDef.All.Length] = PrepCost;
        return all;
    }
    static string[] BuildShopName()
    {
        var b = new[] { "FIELD MEDKIT", "COMBAT STIMS", "ADV. TRAINING", "FRAG CACHE", "BALLISTIC PLATING" };
        var all = new string[ModBase + WeaponModDef.All.Length + 1];
        b.CopyTo(all, 0);
        for (int i = 0; i < WeaponModDef.All.Length; i++) all[ModBase + i] = "WPN: " + WeaponModDef.Name(WeaponModDef.All[i]);
        all[ModBase + WeaponModDef.All.Length] = "COUNTER-PREP";   // placeholder; ShopNameAt overrides per faction
        return all;
    }
    static string[] BuildShopDesc()
    {
        var b = new[]
        {
            "Heal your most-wounded soldier to full.",
            "+3 max HP to your frailest soldier (permanent).",
            "Grant a soldier a bonus perk choice.",
            "+1 grenade every mission for a soldier (permanent).",
            "+1 armor to your least-armored soldier (permanent: -1 damage per hit).",
        };
        var all = new string[ModBase + WeaponModDef.All.Length + 1];
        b.CopyTo(all, 0);
        for (int i = 0; i < WeaponModDef.All.Length; i++) all[ModBase + i] = WeaponModDef.Desc(WeaponModDef.All[i]) + " (installed on a soldier)";
        all[ModBase + WeaponModDef.All.Length] = "One-mission counter to the faction you're about to face.";
        return all;
    }

    // Dynamic shop text/cost: the PREP item's name/desc reflect the UPCOMING faction so the player
    // sees exactly what they're buying; every other item falls through to the static arrays. The Hud
    // routes ShopName[i]/ShopDesc[i]/ShopCost[i] through these so the prep row reads correctly.
    public string ShopNameAt(int item)
    {
        if (IsPrepItem(item)) return "COUNTER-PREP: " + Run.FactionName(PrepFactionOffered);
        return (item >= 0 && item < ShopName.Length) ? ShopName[item] : "";
    }
    public string ShopDescAt(int item)
    {
        if (IsPrepItem(item)) return PrepDescFor(PrepFactionOffered);
        return (item >= 0 && item < ShopDesc.Length) ? ShopDesc[item] : "";
    }
    public int ShopCostAt(int item) => (item >= 0 && item < ShopCost.Length) ? ShopCost[item] : 0;

    // The faction the COUNTER-PREP item targets this barracks (the upcoming reachable threat). None
    // if every reachable node is mixed-force -> the prep row is unavailable/greyed.
    public Faction PrepFactionOffered => _run != null ? _run.UpcomingFaction() : Faction.None;

    public static string PrepDescFor(Faction f) => f switch
    {
        Faction.Syndicate => "HARDENED OPTICS: deny their see-over-low cover next mission.",
        Faction.Legion    => "REACTIVE PLATING: squad takes -1 damage next mission.",
        Faction.Wardens   => "FIELD SMOKE: break their long sightlines (no long-range aim edge).",
        _ => "No faction telegraphed on the next mission.",
    };

    // mission objective
    public Objective Objective;
    // The extraction zone the WIN test reads: the UNION of the FIXED far-corner fallback (always
    // present so the win is ALWAYS reachable — a dead planter must never soft-lock the mission) and,
    // once a soldier plants one, a forward 3x3 BEACON zone. Every EvacZone read (renderer / threat /
    // HUD / autopilot / CheckEnd) treats the two as one set — planting just widens it, cutting the
    // long empty march to the corner without touching difficulty.
    public List<(int x, int y)> EvacZone = new();
    // BEACON (Evac only, one per mission): a soldier spends 1 action to drop a forward evac beacon on
    // their tile; its walkable 3x3 (centre + ring) is UNIONed into EvacZone. These fields are per-mission
    // transient (reset in SetupMission) — no persisted state, so no enum/ordinal churn.
    public bool BeaconPlanted;                       // true once the single beacon has been dropped
    public (int x, int y) BeaconTile;                // the beacon's centre tile (for the renderer marker)
    public List<(int x, int y)> BeaconZone = new();  // the walkable 3x3 tiles the beacon added to EvacZone
    // A soldier may DEPLOY a beacon on the plain Evac objective (forward staging past the half-line — the
    // shipped W6 de-drag) and — APEX W8 — on ESCORT, where marching the leashed VIP to the far corner was
    // the flagged ~14-turn drag. Escort's gate is STRICTER (far third + cold LZ, see EscortBeaconOk):
    // CheckEnd's Escort test is just "VIP in zone", so a permissive plant would be an instant win.
    // W4 (SIGNAL): RESCUE gets the same treatment ONCE THE CAPTIVE IS FREED — the freed walk-out to
    // the fixed corner was the same drag Escort had, and its win test is likewise just "freed VIP in
    // zone", so the plant carries Escort's FULL strict gate (see CanBeacon). While the captive is
    // still caged there is no asset to extract, so no beacon. Endless has no extraction. As
    // always the beacon is once-per-mission, planted by a real soldier standing on WALKABLE FLOOR
    // (a non-floor planter refuses gracefully, never crashes).
    public bool HasBeaconAction => (Objective == Objective.Evac || Objective == Objective.Escort
                                    || (Objective == Objective.Rescue && !CaptiveLocked))
                                   && Mode != GameMode.Endless;
    public bool CanBeacon(Unit u)
        => HasBeaconAction && !BeaconPlanted && u != null && u.Team == Team.Player && !u.IsVip
           && u.CanAct && Grid.IsFloor(u.X, u.Y) && !EvacZone.Contains((u.X, u.Y))
           // the strict far-third + cold-LZ gate applies to BOTH asset-walk objectives (Escort, and
           // Rescue once freed — HasBeaconAction only admits Rescue in the freed state): their win
           // is "VIP in zone", so a permissive plant would be a near-instant win. Evac stays on the
           // shipped half-line discipline (its win needs the WHOLE squad in the zone).
           && ((Objective != Objective.Escort && Objective != Objective.Rescue) || EscortBeaconOk(u));

    /// APEX W8 — the ESCORT anti-trivialization gate. The planter must have genuinely PUSHED the map:
    ///   (1) FAR THIRD — u.X >= Grid.W*2/3. The VIP spawns in the squad wedge, so a spawn-side plant
    ///       plus one leash step would win Escort without crossing the board (Evac's shipped HALF-LINE
    ///       staging must NOT be shared here — its win needs the whole squad in the zone; Escort's
    ///       needs only the VIP).
    ///   (2) COLD LZ — no LIVING, non-routed enemy within Chebyshev 3 of the planter, at ANY alert
    ///       tier. Dormant counts: pods stay non-Alert while the squad is concealed and DoBeacon
    ///       deliberately doesn't break stealth, so an "Alert-only" test would let a concealed squad
    ///       plant beside a sleeping pod and leash-win before it ever wakes. A ROUTED survivor is
    ///       fleeing, not holding ground, so it doesn't veto the plant.
    bool EscortBeaconOk(Unit u)
        => u.X >= Grid.W * 2 / 3
           && !Enemies.Any(e => e.Alive && e.Routed == 0
                                && Util.ChebyDist(u.X, u.Y, e.X, e.Y) <= 3);

    // ---- W10 INTEL CACHE: an optional gold-diamond pickup tile spawned mid/far-field every
    // campaign mission (Mission.PlaceIntelCache, PlaceBarrels-style reachability guard). A soldier
    // ENDING a tile-entry on it banks CacheIntelMin..Max intel (OnUnitEnteredTile); it expires after
    // CacheTurns player turns (StartPlayerTurn), so the detour is a real risk/reward routing bet,
    // not free money. Per-mission transient — reset in SetupMission, never persisted.
    public bool CachePresent;
    public int CacheX, CacheY;
    public int CacheTurnsLeft;
    public const int CacheTurns = 6;       // player turns before the cache goes dark
    public const int CacheIntelMin = 8, CacheIntelMax = 10;

    // ---- W10 secondary-objective state (per-mission transient, reset in SetupMission) ----
    public int DemoProgress;               // DEMOLITION: player-destroyed cover tiles + barrels this mission
    public const int DemoRequired = 3;
    public Unit BountyTarget;              // BOUNTY: the named specialist picked by RollSecondary (null otherwise)
    public const int GhostTurns = 2;       // GHOST: concealment must survive through this player turn

    // DEFEND objective (3.8): survive this many player turns vs mid-mission waves
    public const int DefendTurns = 8;
    public int Turn => _turnCount;

    // ---------------- anti-turtle PRESSURE CLOCK ----------------
    // On "camp-friendly" objectives (Eliminate / Hack / Decapitate) there is no movement
    // pressure, so the dominant strategy is to sit in overwatch and let the enemy come. The
    // pressure clock fixes that: after a GRACE period (so the deliberate opening this game
    // prizes is preserved -- DESIGN.md S5), an escalating threat ramps every couple of turns,
    // making turtling strictly worse than advancing. It's telegraphed + gradual (Into-the-Breach
    // "communicate not compel") and tuned NOT to make a reasonable pace unwinnable.
    public const int PressureGrace = 4;        // turns 1..4 are free (no pressure)
    public const int PressureStep  = 2;        // one rung per this many turns after grace
    public const int PressureMax   = 4;        // rung cap
    public const int PressureAimPerRung = 3;   // enemy aim bonus per rung (+3..+12)
    public int Pressure;                        // current rung 0..PressureMax (HUD reads this)
    public bool PressureActive => Pressure > 0; // for HUD pulse
    int _pressureWaves;                         // count of reinforcement waves the clock has dropped

    // ── C3 THE TWO GAMES: the clock's two arms are not the same thing ────────────────────────
    // The anti-turtle clock has an AIM arm (a telegraphed, escalating enemy accuracy bonus) and a
    // REINFORCEMENT arm (a small wave every fresh rung from 2). On HACK and DECAPITATE both arms
    // do the same job: they make a slow squad's position worse without touching what ENDS the
    // mission (a full hack bar; one named body). On ELIMINATE the second arm does something else
    // entirely — the win condition is "no hostile is alive", so every body the clock adds is also
    // WIN CONDITION. Being slow there does not raise the price of the finish line, it MOVES it.
    //
    // Measured on this wave's own tree (D0, 960 campaigns, base 17934ee): on mid-run campaign
    // nodes 41.0% of Eliminates took at least one wave, averaging 1.69 bodies over ALL of them —
    // ~4.1 bodies on each mission that took one, on top of a 7.78-body deploy force. Those same
    // missions read 37.4% ±4.1 against 81.5% ±1.1 for the six objectives that end on a task.
    //
    // So the reinforcement arm is suppressed on ELIMINATE and ONLY on Eliminate. The aim ramp,
    // its banner and its HUD meter are untouched — camping still gets strictly worse, which is
    // the clock's actual charter (docs/DESIGN.md §3.A). DECAPITATE deliberately keeps both arms:
    // its win condition is one body, so a wave there raises the price without moving the line —
    // and that makes it this wave's within-round CONTROL. If Decapitate moves in the paired
    // measurement, something other than this lever moved it.
    //
    // SIGHTLINE_KILLTREADMILL=1 restores the pre-C3 behaviour exactly (both arms everywhere).
    public static bool ClockWavesOnEliminate = false;

    /// True when the anti-turtle clock's REINFORCEMENT arm may fire this mission. The aim arm is
    /// never gated by this — see UpdatePressure.
    bool ClockMayReinforce => ClockWavesOnEliminate || Objective != Objective.Eliminate;

    // The clock only runs on objectives where camping is the exploit. Defend is already
    // wave-based; Evac/Escort/Rescue are movement-pressured; Sabotage already makes you move
    // to sites -- none of those need (or want) it.
    bool PressureClockObjective() =>
        // PROGRAM HORIZON W4: no anti-turtle clock in SKIRMISH/DAILY — a single fight isn't a camp
        // exploit, and the reinforcement waves would muddy the seeded daily's determinism.
        Mode != GameMode.Skirmish &&
        // APEX W1: no clock in LAST STAND either. Endless forces Eliminate and never resets
        // _turnCount, so a deep stand inherited a permanent hidden +12..+16 enemy aim ramp plus
        // phantom mission-1-scaled reinforcement waves on top of its own wave economy. Endless
        // difficulty is owned by the wave escalation, not the campaign clock.
        Mode != GameMode.Endless &&
        // T1: and none in the TRAINING OP — a drill you are reading lesson cards through must not
        // quietly ramp enemy aim while you think.
        Mode != GameMode.Training &&
        (Objective == Objective.Eliminate || Objective == Objective.Hack || Objective == Objective.Decapitate);

    // HUD reads this to decide whether to draw the PRESSURE meter (only on clock objectives,
    // and only once we're in a live mission phase).
    public bool PressureClockHud => PressureClockObjective() && (Phase == Phase.PlayerTurn || Phase == Phase.EnemyTurn);

    // Rung for a given player-turn count: 0 through grace, then one per PressureStep turns.
    int PressureRungFor(int turn)
    {
        if (turn <= PressureGrace) return 0;
        int rung = 1 + (turn - PressureGrace - 1) / PressureStep;
        return Math.Min(rung, PressureMax);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // ONBOARDING (3.12 -> PROGRAM RESONANCE T1)
    //
    // Three cooperating pieces, deliberately kept in one place:
    //   A. TRAINING OP  — TrainStep / TrainLessons: a scripted, non-persistent, restartable drill
    //                     (Mode == GameMode.Training) that teaches by posing small problems.
    //   B. VERB STAGING — RevealedVerbs: during the drill AND campaign mission 1 the action bar
    //                     shows only what has been taught, and grows as lessons land. Permanent
    //                     SHOW ALL escape ([V]) so a returning player is never locked out.
    //   C. FIELD TIPS   — FieldTips: ~10 once-per-profile just-in-time cards, each fired the first
    //                     time its precondition is actually TRUE in play.
    // ═══════════════════════════════════════════════════════════════════════════════════════

    // ---- A(campaign): the original mission-1 callout strip. UNCHANGED semantics ----
    public int TutStep = -1;                 // -1 = inactive
    bool _tutMoved, _tutOver, _tutShot;
    bool _tutGrenade, _tutAbility;           // T1: two more verb-performed flags the drill reads
    float _tutDoneTimer;
    // FUL-12: named step indices — every gate below compares against the SEMANTIC step, so a
    // future insert/renumber can't silently re-point the "reached the FIRE lesson" completion
    // gates (EnterBarracks/LoseRun) or the bar-hierarchy map (Hud.DrawActionButtons).
    public const int TutStepConceal = 0, TutStepMove = 1, TutStepOverwatch = 2, TutStepFire = 3, TutStepDone = 4;
    public static readonly string[] TutPrompts =
    {
        // FUL-12: the opening CONCEALED state was the one core rule the onboarding never named —
        // a new player read the quiet board as "no threat" and walked into the first pod blind.
        "WELCOME, COMMANDER. The squad opens CONCEALED - the enemy pods ahead are dormant and blind to you. Position freely: your first attack from hiding is an AMBUSH (bonus aim + crit), so you choose where the fight starts.",
        // W5 (audit newplayer-5): this used to point the player at "a glow". Wave V deliberately
        // replaced the old flood-fill with a thin cyan CONTOUR plus corner ticks the code itself
        // describes as ~0.6% of a tile's area — measured at a 5/255 modal inner lift, a ~2% luma
        // change. The visual was right; the copy was never updated with it, so the very first
        // sentence of instruction in the game pointed at a cue that had been reduced to a whisper.
        "Click inside the CYAN OUTLINE to MOVE the selected soldier - the corner ticks mark each tile you can reach, and the DASHED outer ring costs both actions. Cover (the raised blocks) shields you from fire: end your move beside one.",
        "Now set OVERWATCH: press [2] (or the button). That soldier will fire on the first enemy that moves into its line of sight.",
        "Click a hostile to FIRE. A shot costs 1 action and does NOT end the turn - keep the other action to reposition (one shot per turn). Attacking from a side a foe has no cover on FLANKS it - far deadlier.",
        "That's the basics: move into cover, flank, overwatch, fire - then END TURN. Press [K] anytime for the FIELD MANUAL - every enemy, verb and rule lives there. Good hunting.",
    };
    /// T1 (Part B): what each mission-1 lesson PUTS ON THE BAR when it opens. Index-aligned with
    /// TutPrompts through the same named constants, so a renumber moves both together. The first
    /// two lessons are BOARD lessons (move/conceal are clicks, not buttons) and add nothing —
    /// the bar carries only the SHOW ALL escape then, which is the honest state: nothing taught yet.
    static readonly string[][] TutReveal =
    {
        new string[0],                                   // TutStepConceal — board lesson
        new string[0],                                   // TutStepMove    — board lesson
        new[] { "overwatch" },                           // TutStepOverwatch
        new[] { "shoot", "reload" },                     // TutStepFire (RELOAD rides with FIRE: a
                                                         // staged-away RELOAD could strand a dry soldier)
        new string[0],                                   // TutStepDone — staging is OFF by then
    };
    public string TutorialText => (TutStep >= 0 && TutStep < TutPrompts.Length) ? TutPrompts[TutStep] : null;

    // ── W5 THE FIRST HOUR: the briefing goes FIRST on mission 1 ───────────────────────────────
    // The defect (audit newplayer-1, pinned by SIGHTLINE_BRIEFTEST): on a FIRST-EVER campaign run
    // the mission-1 briefing could not draw at all. `BriefAllowed` requires TutorialText == null,
    // the strip was non-null from the frame SetupMission armed it, and `UpdateBriefing` destroys
    // the card outright the moment Stats.CombatLog fills — which the strip's own FIRE lesson does.
    // So the card HELD for the whole strip (never burning its 11 s clock) and was then killed by
    // the first exchange, or by BriefHoldMax at 45 s. Measured before the fix: 0.00 s of 11 s.
    //
    // The repair is ORDERING, not content (DESIGN.md §1.1 caps the narrative frame: this makes an
    // EXISTING card reachable, it does not write more of it). The board is not yet contested on
    // turn 1, so the briefing is a genuine PRE-FIGHT beat: arm the strip PENDING, let the card
    // play, open the strip the frame it retires. Any key or click still dismisses the card, so a
    // player who wants to move immediately reaches the lesson in one input.
    //
    // THIS INVERTS A STATED PRIORITY AND IS RECORDED AS ONE: docs/DESIGN.md §1.2 is the amendment
    // (§1.1 says the briefing yields to the teaching layers ABSOLUTELY; here, for mission 1 of a
    // first-ever campaign only, teaching waits up to 11 s behind it). The never-simultaneous
    // invariant is untouched — read §1.2's limits before widening this to any other mission.
    bool _tutPending;
    /// True while the mission-1 lesson strip is armed but yielding to the pre-fight briefing.
    public bool TutPending => _tutPending;
    /// Turn at which the FIRE lesson yields anyway. Its three siblings have had a patience
    /// fallback for waves (CONCEAL 2, MOVE 3, OVERWATCH 6); FIRE was the one lesson whose only
    /// exit was performing the verb, which is also the action that destroyed the briefing.
    public const int TutFirePatience = 9;
    /// Off-switch for the reordering (SIGHTLINE_BRIEFFIRST=0 restores the pre-W5 behaviour), so
    /// BRIEFTEST is falsifiable without reverting the tree. Default ON.
    static readonly bool BriefFirst = Environment.GetEnvironmentVariable("SIGHTLINE_BRIEFFIRST") != "0";

    /// Harness seam (SIGHTLINE_FIRSTRUN=1, screenshot only). The NoPersist gate below is EXACTLY
    /// what hid the W5 defect for ten programs: every SHOT/AUTOPLAY path sets NoPersist, so the
    /// strip never armed and every mission-1 screenshot ever taken showed the briefing precisely
    /// because the tutorial was not running. This override arms the strip under NoPersist so a
    /// frame can show what a FIRST-EVER player actually sees. Disk stays untouched (CompleteTutorial
    /// keeps its own !NoPersist gate), and it is inert unless the variable is set -> PAIRTEST-safe.
    static readonly bool FirstRunShot = Environment.GetEnvironmentVariable("SIGHTLINE_FIRSTRUN") == "1";

    void StartTutorialMaybe()
    {
        if (_run.Mission != 1) return;
        if (NoPersist ? !FirstRunShot : Display.TutorialSeen) return;
        // SetupMission calls this BEFORE BeginBriefing, so "will there be a card?" is not knowable
        // here — arm PENDING and let UpdateTutorial open the strip on the first frame the card is
        // gone. When no briefing composes (BriefLines stays null) that is the very next tick, so a
        // non-campaign or briefing-less path is unchanged in everything but one frame of latency.
        _tutPending = BriefFirst;
        TutStep = BriefFirst ? -1 : 0;
        _tutMoved = _tutOver = _tutShot = _tutGrenade = _tutAbility = false;
        RevealedVerbs.Clear();
        ApplyReveal(TutReveal[0]);
        // APEX W2: "seen" is now marked at tutorial COMPLETION (CompleteTutorial), not here — a
        // player who quit on step 0 used to have the whole onboarding burned without reading it.
    }

    void UpdateTutorial(float dt)
    {
        if (_tutPending)
        {
            if (BriefLines != null) return;      // the pre-fight card still owns the slot
            _tutPending = false;
            TutStep = 0;
            ApplyReveal(TutReveal[0]);
        }
        if (TutStep < 0) return;
        switch (TutStep)
        {
            // FUL-12: the concealment lesson lives while the opening is actually concealed — it
            // yields the moment stealth breaks (lesson demonstrated) or turn 2 starts (the squad
            // has seen the opening; on a no-conceal run — SPEARHEAD/EXPOSED — it yields at once).
            case TutStepConceal: if (!SquadConcealed || _turnCount >= 2) AdvanceTutorial(); break;
            // W11: turn-count fallback on the MOVE step — a player who's already ending turns
            // without a "move" click (e.g. opened on overwatch/fire) clearly knows how to act;
            // don't hold the MOVE card up forever, advance after a couple of full turns.
            case TutStepMove: if (_tutMoved || _turnCount >= 3) AdvanceTutorial(); break;
            // W11 review: same fallback on the OVERWATCH lesson — a reaction-averse player who
            // never arms a watch would otherwise park here below the reached-FIRE "seen" gate and
            // get the whole onboarding re-offered every future run.
            case TutStepOverwatch: if (_tutOver || _turnCount >= 6) AdvanceTutorial(); break;
            // W5: the patience fallback the other three lessons already had. FIRE was the one step
            // whose ONLY exit was performing the verb — and performing it wrote the combat-log
            // entry that destroyed the briefing, so the action that ended the lesson was the same
            // action that killed the card. A player who wins mission 1 on overwatch reactions
            // alone (their soldier never takes an aimed shot) used to hold this card all mission.
            case TutStepFire: if (_tutShot || (BriefFirst && _turnCount >= TutFirePatience)) AdvanceTutorial(); break;
            case TutStepDone: _tutDoneTimer -= dt; if (_tutDoneTimer <= 0) CompleteTutorial(); break;
        }
    }

    void AdvanceTutorial()
    {
        TutStep++;
        if (TutStep == TutStepDone) _tutDoneTimer = 7f;
        if (TutStep >= 0 && TutStep < TutReveal.Length) ApplyReveal(TutReveal[TutStep]);
        if (TutStep >= TutPrompts.Length) CompleteTutorial();
    }

    /// Harness seam (SIGHTLINE_TUTORIAL=<n>): show a step directly. Seeds the final step's dwell
    /// timer — without it, the done step completes on the first Update tick and the shot frames a
    /// bare board. T1: also replays the cumulative reveal set so a staged-bar screenshot is honest.
    public void ShowTutorialStep(int step)
    {
        TutStep = Math.Clamp(step, 0, TutPrompts.Length - 1);
        _tutPending = false;   // W5: forcing a step IS opening the strip — drop the pre-fight arm
        if (TutStep == TutStepDone) _tutDoneTimer = 7f;
        RevealedVerbs.Clear();
        for (int i = 0; i <= TutStep && i < TutReveal.Length; i++) ApplyReveal(TutReveal[i]);
    }

    /// APEX W2: finish the onboarding and persist the one-time "seen" flag. The !NoPersist gate is
    /// LOAD-BEARING: Display.MarkTutorialSeen -> Display.Save() writes settings.json unconditionally,
    /// and the SIGHTLINE_TUTORIAL screenshot hook sets TutStep directly (bypassing StartTutorialMaybe's
    /// gate) — an ungated call here would break the harness no-disk / byte-stability contract.
    /// Also called as a mission-1-end fallback (EnterBarracks/LoseRun) so a player who never performs
    /// a mid-tutorial step (e.g. skips overwatch) doesn't re-see the tutorial every run forever.
    void CompleteTutorial()
    {
        TutStep = -1;
        _tutPending = false;   // W5: a strip that never opened is still finished
        RevealedVerbs.Clear();      // staging ends with the lessons: the full bar is the graduation
        if (!NoPersist) Display.MarkTutorialSeen();
    }

    // ───────────────────────────── A. TRAINING OP ─────────────────────────────────────────────
    /// One drill lesson: a small problem, the verbs it puts on the bar, and the predicate that
    /// says the recruit solved it. `Patience` is the turn budget after which the lesson yields
    /// anyway — the drill must never be able to strand someone who solves it a different way.
    public sealed class Lesson
    {
        public string Code;                 // stable id (harness + TUTTEST)
        public string Text;                 // the card body
        public string[] Reveal;             // verbs added to the bar when this lesson OPENS
        public Func<Game, bool> Done;       // solved?
        public int Patience;                // player turns before it yields anyway (0 = never)
    }

    /// The drill's well-ordered problems (DESIGN.md 3.G): each one is a thing to DO, in an order
    /// where every step is solvable with what the previous step taught. The last lesson has no
    /// predicate — CheckTraining ends the drill when the field is clear.
    public static readonly Lesson[] TrainLessons =
    {
        new Lesson { Code = "MOVE", Patience = 3,
            Reveal = new string[0],
            // W5 (audit newplayer-5): same repair as the campaign strip's MOVE card — name the
            // outline and the corner ticks the board actually draws, not a glow it does not.
            Text = "TRAINING OP. Two recruits, four dormant targets, no consequences - nothing here touches your campaign. Select a soldier and click inside the CYAN OUTLINE to MOVE; the corner ticks mark each reachable tile. Move costs 1 of 2 actions; a tile in the DASHED outer ring costs both.",
            Done = g => g._tutMoved },
        new Lesson { Code = "COVER", Patience = 4,
            Reveal = new string[0],
            Text = "TAKE COVER. The raised blocks are cover: low blocks cut enemy aim by 20, high blocks by 40 - but only from the side they sit on. End a move with a soldier BESIDE one of the blocks ahead of you.",
            Done = g => g.AnySoldierBesideCover() },
        new Lesson { Code = "FLANK", Patience = 6,
            Reveal = new string[0],
            Text = "FLANK THEM. The two targets ahead hide behind blocks on their WEST side - shooting straight down the lane wastes the shot. Walk a soldier around to their north or south until the target reads FLANKED.",
            Done = g => g.AnySoldierHasFlankingShot() },
        new Lesson { Code = "FIRE", Patience = 5,
            Reveal = new[] { "shoot", "reload" },
            Text = "OPEN FIRE. Press [1] or FIRE, then click a target. A shot costs 1 action and does NOT end the turn - you keep the second action to reposition. Your first shot from concealment is an AMBUSH: bonus aim and crit.",
            Done = g => g._tutShot },
        new Lesson { Code = "OVERWATCH", Patience = 4,
            Reveal = new[] { "overwatch" },
            Text = "SET A TRAP. OVERWATCH [2] ends a soldier's turn but fires automatically at the first enemy that moves into its line of sight. Arm one, then end the turn and let them walk into it.",
            Done = g => g._tutOver },
        new Lesson { Code = "GRENADE", Patience = 4,
            Reveal = new[] { "grenade" },
            Text = "BREAK THE COVER. A GRENADE [4] always hits - it damages everything in a 3x3 and DESTROYS the cover they are hiding behind. One per soldier per mission. Throw one.",
            Done = g => g._tutGrenade },
        new Lesson { Code = "ABILITY", Patience = 4,
            Reveal = new[] { "ability" },
            Text = "USE THE KIT. Every class has a signature ability on key [5], on a short cooldown - the ASSAULT yanks a foe out of cover with GRAPPLE, the SHARPSHOOTER designates one with MARK. Use one now.",
            Done = g => g._tutAbility },
        new Lesson { Code = "CLEAR", Patience = 0,
            Reveal = new string[0],   // graduation: staging switches OFF at this lesson, full bar
            Text = "FINISH IT. That is the loop: move, cover, flank, fire, react. Clear the remaining targets to complete the drill. The full action bar is now unlocked - press [K] any time for the FIELD MANUAL.",
            Done = g => false },
    };

    public int TrainStep = -1;          // -1 = the drill's lesson track is inactive
    int _trainLessonTurn = 1;           // _turnCount when the current lesson opened (patience clock)

    public string TrainingText => (TrainStep >= 0 && TrainStep < TrainLessons.Length)
        ? TrainLessons[TrainStep].Text : null;

    /// True while a drill lesson card is up; Hud uses it to title the card TRAINING OP n/N.
    public bool TrainingActive => Mode == GameMode.Training && TrainStep >= 0;

    void UpdateTraining(float dt)
    {
        if (Mode != GameMode.Training || TrainStep < 0) return;
        if (TrainStep >= TrainLessons.Length) { TrainStep = -1; return; }
        var l = TrainLessons[TrainStep];
        bool solved = l.Done != null && l.Done(this);
        bool spent  = l.Patience > 0 && _turnCount - _trainLessonTurn >= l.Patience;
        if (solved || spent) AdvanceTraining();
    }

    void AdvanceTraining()
    {
        if (TrainStep < 0) return;
        TrainStep++;
        _trainLessonTurn = _turnCount;
        if (TrainStep >= TrainLessons.Length) { TrainStep = TrainLessons.Length - 1; return; }
        ApplyReveal(TrainLessons[TrainStep].Reveal);
        Audio.Play("select");
    }

    /// Harness seam (SIGHTLINE_TRAINLESSON=<n>): park the drill on one lesson for a screenshot,
    /// replaying the cumulative reveal set so the staged bar in the frame is the real one.
    public void ShowTrainingLesson(int step)
    {
        TrainStep = Math.Clamp(step, 0, TrainLessons.Length - 1);
        _trainLessonTurn = _turnCount;
        RevealedVerbs.Clear();
        for (int i = 0; i <= TrainStep; i++) ApplyReveal(TrainLessons[i].Reveal);
    }

    /// A soldier is standing beside (4-way) a cover block — the COVER lesson's solved state.
    public bool AnySoldierBesideCover()
    {
        foreach (var p in Players)
        {
            if (!p.Alive || p.IsVip || p.Downed) continue;
            for (int d = 0; d < 4; d++)
            {
                int nx = p.X + (d == 0 ? 1 : d == 1 ? -1 : 0);
                int ny = p.Y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (!Grid.InBounds(nx, ny)) continue;
                var t = Grid.Tiles[nx, ny];
                if (t == TileType.LowCover || t == TileType.HighCover) return true;
            }
        }
        return false;
    }

    /// Some soldier currently has a FLANKING line on some live hostile — the FLANK lesson's
    /// solved state. Reads the same Combat.ComputeOdds the HUD shows, so the lesson can never
    /// disagree with the reticle the player is looking at.
    public bool AnySoldierHasFlankingShot()
    {
        foreach (var p in Players)
        {
            if (!p.Alive || p.IsVip || p.Downed) continue;
            foreach (var e in Enemies)
            {
                if (!e.Alive) continue;
                if (!Grid.HasLineOfSight(p.X, p.Y, e.X, e.Y)) continue;
                if (Combat.ComputeOdds(Grid, p, e).Flanked) return true;
            }
        }
        return false;
    }

    // ───────────────────────────── B. VERB STAGING ────────────────────────────────────────────
    /// Verbs the player has been shown. Empty + staging-active = only the SHOW ALL escape is on
    /// the bar. Cleared when onboarding ends, which IS the "full bar" graduation.
    public readonly HashSet<string> RevealedVerbs = new();
    void ApplyReveal(string[] ids) { if (ids != null) foreach (var i in ids) RevealedVerbs.Add(i); }

    bool _showAllLocal;   // NoPersist mirror of Display.ShowAllVerbs (the harness never writes disk)
    public bool ShowAllVerbs => NoPersist ? _showAllLocal : Display.ShowAllVerbs;
    public void ToggleShowAllVerbs()
    {
        if (NoPersist) _showAllLocal = !_showAllLocal; else Display.ToggleShowAllVerbs();
        Audio.Play("select");
    }

    /// Staging is CAPPED, by design, to the two places a player can still be learning: the drill
    /// and campaign mission 1's lesson strip. From mission 2 (and in every other mode) the bar is
    /// always whole — a staged verb the player already knows would be a bug, not a lesson.
    public bool OnboardingActive
    {
        get
        {
            // The drill's LAST lesson ("CLEAR") is the graduation: staging switches off there and
            // the recruit finishes the fight with the whole bar.
            if (Mode == GameMode.Training) return TrainStep >= 0 && TrainStep < TrainLessons.Length - 1;
            // Mission 1 only, and only while the callout strip is actually running. TutStepDone is
            // the wrap-up card ("that's the basics") — the bar is whole from there.
            // W5: _tutPending counts. The strip is armed and about to open; without this the bar
            // would draw WHOLE for the briefing's 11 s and then visibly collapse to one button.
            return Mode == GameMode.Campaign && _run != null && _run.Mission <= 1
                   && (_tutPending || (TutStep >= 0 && TutStep < TutStepDone));
        }
    }

    /// Onboarding is running AND the player hasn't taken the SHOW ALL escape.
    public bool VerbStagingActive => OnboardingActive && !ShowAllVerbs;

    /// Should this action-bar verb be drawn at all this frame?
    public bool VerbRevealed(string id)
    {
        if (!VerbStagingActive) return true;
        // Emergency verbs are NEVER staged away: STABILIZE only appears at all while a squadmate
        // is bleeding out, and hiding the one answer to that would be the exact failure mode the
        // SHOW ALL escape exists to prevent.
        if (id == "stabilize") return true;
        return RevealedVerbs.Contains(id);
    }

    // ───────────────────────────── C. JUST-IN-TIME FIELD TIPS ─────────────────────────────────
    /// One once-per-profile card for a verb the lessons never reach, fired the first time its
    /// precondition is actually TRUE in play. `Bit` is the Display.TipsSeen bit (STABLE — it is
    /// on disk; append new tips at the end and never renumber). `Prio` orders simultaneous
    /// candidates by teaching urgency, so a bleeding-out ally outranks a spare-ammo nicety.
    public sealed class FieldTip
    {
        public int Bit;
        public int Prio;
        public string Code;
        public string Text;
        public Func<Game, bool> When;
    }

    /// Helper predicates kept tiny and side-effect-free — TUTTEST calls every one of them.
    static bool AnyLiveThreat(Game g) => g.Enemies.Any(e => e.Alive && e.Active);
    static IEnumerable<Unit> Soldiers(Game g) => g.Players.Where(p => p.Alive && !p.IsVip && !p.Downed);

    /// BIT ORDER IS ON DISK. Bit 0 is BRACE (it inherits FUL-12's BraceTipSeen bool through the
    /// Display migration bridge). Append only.
    public static readonly FieldTip[] FieldTips =
    {
        new FieldTip { Bit = 0, Prio = 2, Code = "BRACE",
            Text = "BRACE [B]: a disrupting reaction. On a hit it STAGGERS the mover - the foe loses its action this turn (for reduced damage). Deny a rushing enemy's alpha instead of racing it for the kill.",
            When = g => AnyLiveThreat(g) },
        new FieldTip { Bit = 1, Prio = 0, Code = "STABILIZE",
            Text = "STABILIZE [E]: that soldier is BLEEDING OUT, not dead - three turns on the clock. Step a squadmate adjacent and STABILIZE to freeze the timer; a CORPSMAN can PATCH them back onto their feet. Win the field and they come home wounded.",
            When = g => g.Players.Any(p => p.Alive && p.Downed) },
        new FieldTip { Bit = 2, Prio = 1, Code = "RELOAD",
            Text = "RELOAD [R]: that soldier's clip is DRY - it cannot fire or set overwatch until it reloads, and reloading costs an action. Reload behind cover on a quiet turn, not in the open mid-firefight.",
            When = g => AnyLiveThreat(g) && Soldiers(g).Any(p => p.Ammo <= 0) },
        new FieldTip { Bit = 3, Prio = 3, Code = "GRENADE",
            Text = "GRENADE [4]: that target is behind cover, and a grenade does not care - it cannot miss, it hits a 3x3, and it DESTROYS the cover itself. One per soldier per mission: spend it on a dug-in pod, not a straggler.",
            When = g => Soldiers(g).Any(p => p.Grenades > 0 && g.Enemies.Any(e =>
                        e.Alive && e.Active && g.Grid.HasLineOfSight(p.X, p.Y, e.X, e.Y)
                        && Combat.ComputeOdds(g.Grid, p, e).CoverLevel > 0)) },
        new FieldTip { Bit = 4, Prio = 4, Code = "HUNKER",
            Text = "HUNKER [3]: that soldier is standing in the open with hostiles looking at it. Hunkering ends its turn but doubles the cover bonus and cuts incoming crits - the right answer when you cannot reach cover and cannot kill.",
            When = g => AnyLiveThreat(g) && Soldiers(g).Any(p => p.ActionsLeft > 0 && !g.SoldierBesideCover(p)
                        && g.Enemies.Any(e => e.Alive && e.Active && g.Grid.HasLineOfSight(e.X, e.Y, p.X, p.Y))) },
        new FieldTip { Bit = 5, Prio = 5, Code = "SHOVE",
            Text = "SHOVE [8]: an enemy is standing right next to you. A shove costs 1 action, does not end the turn, and knocks it a tile back - out of its cover, off high ground, or into a fire. Position is damage.",
            When = g => Soldiers(g).Any(p => g.CanShove(p)) },
        new FieldTip { Bit = 6, Prio = 6, Code = "VAULT",
            Text = "VAULT [9]: you can hop straight over that cover block instead of walking around it. One action, no turn end - the fastest way to break a stalemate across a wall.",
            When = g => AnyLiveThreat(g) && Soldiers(g).Any(p => g.CanVault(p)) },
        new FieldTip { Bit = 7, Prio = 7, Code = "DRAG",
            Text = "DRAG [7]: pull an adjacent squadmate one tile toward you. Use it to haul a bleeding-out soldier out of a firing lane, or to yank a pinned ally into cover without spending their turn.",
            When = g => AnyLiveThreat(g) && Soldiers(g).Any(p => g.CanDrag(p)) },
        new FieldTip { Bit = 8, Prio = 8, Code = "FOCUS",
            Text = "FOCUS [F]: overwatch, narrowed. It watches a CONE instead of the full arc, but reacts with better aim - the answer when you know which way they are coming and want the reaction to land.",
            When = g => g._tutOver && g.Enemies.Count(e => e.Alive && e.Active) >= 2 },
        new FieldTip { Bit = 9, Prio = 9, Code = "ITEM",
            Text = "UTILITY [6]: each class carries a second throwable beyond the grenade - SMOKE to blind a lane, FLASH to disorient a pod, BARRICADE to build cover, INCENDIARY to deny ground. One charge a mission; it is not a grenade, it is a tool.",
            When = g => AnyLiveThreat(g) && Soldiers(g).Any(p => p.Item != ItemKind.None && p.ItemCharge > 0) },
    };

    public string CalloutText;      // non-null => Hud draws the FIELD TIP card
    public string CalloutHead = "FIELD TIP";
    public float CalloutTimer;      // seconds left on screen
    float _tipCooldown;             // breathing room between two tips

    /// True when the tip layer may speak at all: an interactive player turn with no lesson card up.
    bool TipsAllowed => Phase == Phase.PlayerTurn && TutorialText == null && TrainingText == null;

    /// Public so the COVER tip predicate and the lesson share ONE definition of "beside cover".
    public bool SoldierBesideCover(Unit p)
    {
        for (int d = 0; d < 4; d++)
        {
            int nx = p.X + (d == 0 ? 1 : d == 1 ? -1 : 0);
            int ny = p.Y + (d == 2 ? 1 : d == 3 ? -1 : 0);
            if (!Grid.InBounds(nx, ny)) continue;
            var t = Grid.Tiles[nx, ny];
            if (t == TileType.LowCover || t == TileType.HighCover) return true;
        }
        return false;
    }

    /// FUL-12 -> T1: was UpdateBraceCallout (one hard-coded card). Now a scan of the FieldTips
    /// table by teaching priority. Interactive-only: NoPersist runs (autoplay/shots/the balance
    /// flywheel) never see a tip unless the harness explicitly stages one, so the measured game
    /// is untouched — the byte-stability contract this whole wave rides on.
    void UpdateFieldTips(float dt)
    {
        if (CalloutText != null)
        {
            CalloutTimer -= dt;
            if (CalloutTimer <= 0) { CalloutText = null; _tipCooldown = 5f; }
            return;
        }
        if (_tipCooldown > 0) { _tipCooldown -= dt; return; }

        int forced = -1;
        if (NoPersist)
        {
            // harness staging: SIGHTLINE_TIP=<bit>, plus FUL-12's SIGHTLINE_BRACETIP=1 (== bit 0)
            if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_TIP"), out int tb)) forced = tb;
            else if (Environment.GetEnvironmentVariable("SIGHTLINE_BRACETIP") == "1") forced = 0;
            if (forced < 0) return;
        }
        if (!TipsAllowed) return;

        FieldTip pick = null;
        if (forced >= 0)
        {
            foreach (var t in FieldTips) if (t.Bit == forced) pick = t;
        }
        else
        {
            foreach (var t in FieldTips)
            {
                if (Display.TipSeen(t.Bit)) continue;
                if (pick != null && t.Prio >= pick.Prio) continue;
                if (!t.When(this)) continue;
                pick = t;
            }
        }
        if (pick == null) return;

        CalloutHead = "FIELD TIP - " + pick.Code;
        CalloutText = pick.Text;
        CalloutTimer = 9f;
        if (!NoPersist) Display.MarkTipSeen(pick.Bit);   // one-shot: burned the moment it shows
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  RESONANCE C1 (VOICE) — the mission BRIEFING and the squad's BARKS.
    //
    //  Both are pure presentation: the text comes from src/Voice.cs, which takes ZERO draws from
    //  Util.Rng (see that file's header — the whole CRN balance methodology depends on it), and
    //  nothing here is read by combat, AI, mission generation or the save file.
    // ═══════════════════════════════════════════════════════════════════════════════════════

    /// The three composed briefing lines for this mission, or null when there is nothing to say
    /// (non-campaign modes) or the card is done. Hud.DrawBriefCard renders it; it is never
    /// hit-tested, so it cannot swallow a click.
    public string[] BriefLines;
    public string BriefHead;
    public float BriefTimer;          // seconds of card left
    float _briefHold;                 // seconds spent WAITING for the teaching layers to finish
    public const float BriefShowSeconds = 11f;
    const float BriefHoldMax = 45f;   // give up waiting rather than ambush the player mid-fight

    /// True when the briefing may draw at all: an interactive phase with NO teaching card up.
    /// Wave T1's lesson strip and just-in-time field tips outrank the briefing absolutely — a
    /// player learning to move must never have flavour competing for the same card slot.
    bool BriefAllowed => (Phase == Phase.PlayerTurn || Phase == Phase.EnemyTurn)
                         && TutorialText == null && TrainingText == null && CalloutText == null;

    /// Compose the briefing for the mission that just built. CAMPAIGN only: regions, factions and
    /// operation numbers are campaign vocabulary, and TRAINING/SKIRMISH/DAILY/LAST STAND already
    /// say their own thing on the banner. Deterministic — a reloaded save briefs identically.
    void BeginBriefing(int n)
    {
        BriefLines = null; BriefHead = null; BriefTimer = 0f; _briefHold = 0f;
        if (Mode != GameMode.Campaign || _run == null) return;
        bool finale = n >= Run.MaxMissions && Objective == Objective.Decapitate;
        var kf = Combat.MissionFaction;
        BriefLines = Voice.Brief(n, _run.MapSeed, Mission.AppliedLayout, Objective, kf, finale,
                                 finale ? Run.FinaleBossName(kf) : null,
                                 finale ? Run.FinaleKitClause(kf) : null);
        BriefHead = Voice.BriefHead(Objective);
        // C4 — TEACH THE GROUND. A biome mechanic the player has to infer is invisible unfairness
        // (brief C4 / DESIGN.md 3.B "don't spring state changes with no warning"), so the one card
        // that already exists to say "here is what this mission is" names the rule in one sentence.
        // Appended HERE and not inside Voice.Brief on purpose: src/Voice.cs is under a zero-Util.Rng
        // contract and its VOICETEST pre-measures its own three lines — this line is Game's, and the
        // card auto-sizes to its row count. Null on the five biomes that are still paint.
        string groundRule = BiomeMechRule();
        if (groundRule != null && BriefLines != null && BriefLines.Length > 0)
        {
            var withRule = new string[BriefLines.Length + 1];
            System.Array.Copy(BriefLines, withRule, BriefLines.Length);
            // C4 REVIEW: the rule line was typographically identical to the three FLAVOUR lines
            // above it and sat last — the one sentence on the card that changes how the fight works
            // looked exactly like "No colours flying". The card draws every row in one colour, so
            // the separation has to be lexical: a GROUND — prefix, in the em dash the adjacent
            // Voice lines use (this line shipped with a hyphen while its neighbours used —).
            withRule[BriefLines.Length] = "GROUND — " + groundRule;
            BriefLines = withRule;
        }
        BriefTimer = BriefShowSeconds;
    }

    /// Tick the card. It HOLDS (does not burn its clock) while a lesson/tip owns the slot, and
    /// gives up entirely after BriefHoldMax so a long tutorial can never make a briefing surface
    /// three turns into a firefight. Any key or click dismisses it — that is the whole "skippable"
    /// contract, and because the dismissal is a passive read the click still does its normal job.
    void UpdateBriefing(float dt)
    {
        if (BriefLines == null) return;
        // The briefing is a PRE-FIGHT object. The moment real events start hitting the combat log
        // the card's job is over — and the log panel is the one piece of chrome the centred card
        // would sit on top of. The ledger is load-bearing ("why did that happen?"); flavour yields.
        if (Stats.CombatLog.Count > 0) { BriefLines = null; return; }
        if (!BriefAllowed)
        {
            _briefHold += dt;
            if (_briefHold > BriefHoldMax) BriefLines = null;
            return;
        }
        if (!AutoPlay && (Raylib.GetKeyPressed() != 0
                          || Raylib.IsMouseButtonPressed(MouseButton.Left)
                          || Raylib.IsMouseButtonPressed(MouseButton.Right)))
        { BriefLines = null; return; }
        BriefTimer -= dt;
        if (BriefTimer <= 0) BriefLines = null;
    }

    /// True when the squad may speak: the same absolute deference to the teaching layers the
    /// briefing shows. Barks ride the ENEMY turn too (a bond partner falls on their turn, not
    /// yours), so this is deliberately not gated on PlayerTurn.
    bool BarksAllowed => TutorialText == null && TrainingText == null && CalloutText == null;

    /// Speak one line into the combat log, if every gate lets it through (Voice.TryBark owns the
    /// per-turn / per-speaker / per-beat budget; this owns the teaching-layer veto and the
    /// speaker's own eligibility). Silent by construction when `speaker` is dead, a VIP or null.
    bool Bark(Voice.Beat beat, Unit speaker, Unit other = null)
    {
        if (speaker == null || speaker.Team != Team.Player || speaker.IsVip || !speaker.Alive) return false;
        if (!BarksAllowed) return false;
        string line = Voice.TryBark(beat, speaker.Name, other?.Name, _turnCount);
        if (line == null) return false;
        Stats.Log(_turnCount, (int)Team.Player, line, Voice.LogTag);
        return true;
    }

    /// The living soldier standing closest to `at` — the one who would plausibly call a beat that
    /// happened over there (a pod breaking, for instance). Null when nobody is on their feet.
    Unit NearestSoldierTo(Unit at)
    {
        if (at == null) return null;
        Unit best = null; int bd = int.MaxValue;
        foreach (var p in Players)
        {
            if (!p.Alive || p.IsVip || p.Downed) continue;
            int d = Util.ChebyDist(p.X, p.Y, at.X, at.Y);
            if (d < bd) { bd = d; best = p; }
        }
        return best;
    }

    /// First player kill of the mission — so "first blood" can never be said over the fifth body.
    bool _firstBloodSeen;

    /// The bonded squadmate best placed to react to `d` going down: alive, on their feet, and
    /// actually bonded to them. Null when the soldier has no bond on the field — which is exactly
    /// why a bondless soldier can never draw a bond line.
    Unit BondPartnerOf(Unit d)
    {
        if (d == null || d.Bonds == null || d.Bonds.Count == 0) return null;
        foreach (var p in Players)
            if (p != d && p.Alive && !p.Downed && !p.IsVip && d.Bonds.Contains(p.Name)) return p;
        return null;
    }

    /// The last soldier on their feet, or null while two or more are still up.
    Unit LoneSurvivor()
    {
        Unit only = null;
        foreach (var p in Players)
        {
            if (!p.Alive || p.IsVip || p.Downed) continue;
            if (only != null) return null;
            only = p;
        }
        return only;
    }

    /// Fired after any soldier falls or goes down: if that left exactly one on their feet with
    /// hostiles still active, they say so. Once per mission (Voice's per-beat gate).
    void CheckLastStanding()
    {
        if (!Enemies.Any(e => e.Alive && e.Active)) return;
        Bark(Voice.Beat.LastStanding, LoneSurvivor());
    }

    // optional secondary objective (3.9): a per-mission bonus goal worth extra intel
    public const int SwiftTurns = 7;
    public const int SecondaryIntel = 12;
    public SecondaryKind Secondary;
    public bool SecondaryFailed;     // a soldier was lost (fails NO LOSSES)

    // per-mission visual theme
    public Biome Biome = Biome.All[0];

    // escort objective: a fragile VIP that must reach the extraction zone alive.
    // Mission-only — it rides in Players for the mission but never joins the squad.
    public Unit Vip;
    public bool CaptiveLocked;   // RESCUE: the asset starts caged + invulnerable until a soldier frees it (3.8)

    // hack objective: reach the terminal and hack it over several actions. With the one-cycle-
    // per-turn channel (HackedThisTurn) this is a 2-turn HOLD under fire — a 3-turn hold proved
    // too lethal once the hack goes loud (competent-AI win-rate cratered); 2 keeps it a real
    // fight the squad can win with good play.
    public const int HackRequired = 2;
    public (int x, int y) Terminal;
    public int HackProgress;
    // Balance fix: the terminal accepts only ONE breach cycle per player turn (a sustained
    // channel), so the 3-charge hack is a genuine multi-turn HOLD — the squad can't stack 3
    // soldiers to finish in one turn, it must defend the console across several turns while the
    // hack-noise-roused pods converge. Reset each StartPlayerTurn. (Sabotage's spread-out charge
    // sites already force a multi-turn traverse, so only the single-terminal hack needs this.)
    public bool HackedThisTurn;
    public bool HasTerminal => Objective == Objective.Hack;

    // SABOTAGE: K charge sites, each demolished by one PLANT action (3.8)
    public List<(int x, int y)> SabotageSites = new();
    public HashSet<int> SabotageBlown = new();
    public bool HasSabotage => Objective == Objective.Sabotage;
    public bool HasHackAction => HasTerminal || HasSabotage;
    // EXTRACT (lift-out): on the zone-based extraction objectives, a soldier standing in the
    // evac zone can haul an adjacent ally / VIP / freed captive aboard — pulling them the last
    // step into the zone. Cuts the long Evac/Escort "everyone walk to the corner" drag.
    public bool HasExtractAction => Objective == Objective.Evac || Objective == Objective.Escort
                                    || Objective == Objective.Rescue;

    // DECAPITATE: one designated enemy is the High-Value Target; killing it WINS the
    // mission outright (no need to clear the map). Designated from the Enemies list in
    // SetupMission after Mission.Build; null on every other objective.
    public Unit Hvt;
    public bool HasHvt => Objective == Objective.Decapitate && Hvt != null;
    // W8: did THIS mission's HVT take DesignateHvt's statline buff, or was it exempt (an ELITE —
    // i.e. the campaign finale's named boss, or the m3/m5 mid-boss)? Balance telemetry only; the
    // harness reports the two populations separately so a mid-run Decapitate can never again be
    // pooled with the capstone. Reset with Hvt in SetupMission.
    public bool HvtBuffed;
    // DECAPITATE GUARDED HVT (W4): up to 2 bodyguards picked near the HVT. While any is alive within
    // Combat.HvtGuardRange of the HVT, the HVT takes reduced (never zero) damage — peel the guards or
    // pull the HVT out of the bubble to execute it. Transient (enemies aren't persisted). Recomputed
    // by UpdateHvtGuard at every turn boundary + after any death (see KillUnit / StartPlayerTurn / etc).
    readonly List<Unit> _hvtGuards = new();

    public bool CanHack(Unit u)
    {
        if (u == null || u.Team != Team.Player || !u.CanAct) return false;
        // terminal: one breach cycle per turn (HackedThisTurn) so it's a multi-turn hold, not a
        // stack-and-finish — the autopilot/HUD then route the other soldiers to defend instead.
        if (HasTerminal) return !HackedThisTurn && HackProgress < HackRequired
                              && Util.ChebyDist(u.X, u.Y, Terminal.x, Terminal.y) <= 1;
        if (HasSabotage) return NearestSabotageSite(u) >= 0;
        return false;
    }

    int NearestSabotageSite(Unit u)
    {
        for (int i = 0; i < SabotageSites.Count; i++)
            if (!SabotageBlown.Contains(i) && Util.ChebyDist(u.X, u.Y, SabotageSites[i].x, SabotageSites[i].y) <= 1) return i;
        return -1;
    }

    // end-turn confirmation when soldiers still have actions
    public bool EndTurnArmed;
    int _lastSig = -1;

    // camera: player-controlled zoom/pan for readability (identity by default,
    // so default mouse picking + the headless harness are unaffected)
    public float CamZoom = 1f;
    public Vector2 CamPan = Vector2.Zero;

    // keyboard tile cursor (mouse-free play): arrows/WASD move it, Space acts
    public bool KbCursor;
    public int CurX, CurY;

    // pause / settings overlay
    public bool Paused;
    // RESONANCE A2: which mix fader (0..3) the mouse is currently dragging in the pause menu,
    // or -1. Held across frames so a drag keeps tracking once it leaves the row's rect.
    int _volDrag = -1;
    // THREAT PREVIEW preference — three-state (RESONANCE T2): 0 OFF / 1 SIMPLE (the pre-T2 minimal
    // "this tile is exposed" tick) / 2 FULL (graded pips + hover card + danger-tinted path). Not
    // persisted (a per-session view pref, like the camera).
    public const int ThreatOff = 0, ThreatSimple = 1, ThreatFull = 2;
    public int ThreatPref = ThreatFull;
    public bool ShowThreatPref => ThreatPref > ThreatOff;
    public void CycleThreatPref() { ThreatPref = ThreatPref >= ThreatFull ? ThreatOff : ThreatPref + 1; }

    // custom-tag text editor (modal): key T in a mission, or from the perk chooser
    public bool EditingTag;
    public string TagBuffer = "";
    public Unit TagTarget;

    // game-feel: hit-stop freeze + camera zoom-punch + red death-flash (3.11)
    public float HitStop;
    float _camPulse;
    bool  _autoCamManual;   // true = player manually moved camera; suppresses auto-follow until C-reset
    public float DeathFlash;                 // 0..1 red full-screen pulse on a soldier's death
    readonly List<string> _missionKia = new(); // soldiers KIA this mission (for the debrief)
    // UNDERTOW W3 — pod MORALE: a pod that drops to <= half its original strength ROUTS its survivors.
    // _podOrig snapshots each pod's spawn size at mission start; the break/threshold logic is BreakPodMorale.
    readonly Dictionary<int, int> _podOrig = new();
    // FUL-4 — DEFEND waves are real pods too (morale/rout must play on the one objective built
    // from waves). Ids allocated from 100 up: initial pods are i/2 (<=5), harness scenes use 90/91,
    // so wave pods can never collide with either. Reset per mission beside _podOrig.
    int _nextWavePod = 100;
    // W4 THE SECOND AXIS: how many reinforcement waves this mission has already landed. Only
    // read by the ENVELOP rim rotation below (a surrounded hold must keep being surrounded);
    // every other opening ignores it, so the east-edge arrival is unchanged.
    int _waveIndex;
    // FUL-6 CRITICAL MASS — LINKED ACTIVATION ("they heard the guns"): pods flagged here were put
    // Suspicious by a nearby pod's wake (ActivatePod's link rider) and CONFIRM to Alert at the next
    // ResolveSuspicion pass even when unseen (the sound was enough). Cleared per mission in
    // SetupMission beside _podOrig, and wholesale after each ResolveSuspicion pass. Transient,
    // never persisted (enemies are never serialized).
    readonly HashSet<int> _linkedPods = new();
    // Sound radius (Util.TileDist — the HackNoiseRange metric) for the link: a waking pod alerts
    // the nearest OTHER dormant pod whose closest member is within this range of the woken pod's
    // closest member. Missions 3+ only; exactly ONE pod per activation call; never chains (a
    // linked pod wakes via ResolveSuspicion, which never calls ActivatePod).
    public const int LinkRange = 6;
    public const int RoutDuration = 2;         // enemy turns a broken pod flees before it can rally (decrements in BeginTurn)
    // SIGNAL W8 — the WARBRINGER's banner aura reach (Chebyshev tiles). Pods with a living, active
    // banner inside this range cannot rout (BreakPodMorale) and rally one turn faster
    // (BeginEnemyUnitTurn). The player's counter is spatial: kill the banner or bait the pod out.
    public const int BannerRange = 4;

    /// SIGNAL W8 — true when a living, ACTIVE banner-bearer (Unit.HasBanner, default the
    /// WARBRINGER) stands within Chebyshev BannerRange of `u`. A banner anchors ITSELF too
    /// (distance 0), so a live WARBRINGER never routs. Dormant banners project nothing.
    public bool BannerNear(Unit u)
    {
        foreach (var b in Enemies)
            if (b.Alive && b.Active && b.HasBanner && Util.ChebyDist(b.X, b.Y, u.X, u.Y) <= BannerRange)
                return true;
        return false;
    }

    /// SIGNAL W8 — a pod's live head-count vs its spawn strength, for the WAVERING telegraph +
    /// the "POD 2/4" tooltip line. `orig` falls back to the current count for un-snapshotted pods,
    /// mirroring BreakPodMorale's mates.Count+1 post-kill view. (FUL-4: DEFEND waves are now
    /// snapshotted at spawn; the fallback still guards hypothetical future un-snapshotted pods.)
    public (int alive, int orig) PodStrength(int podId)
    {
        int alive = 0;
        foreach (var e in Enemies) if (e.Alive && e.PodId == podId) alive++;
        return (alive, _podOrig.GetValueOrDefault(podId, alive));
    }

    /// The ONE shared rout threshold (BreakPodMorale + the WAVERING telegraph both read it, so the
    /// telegraph can never lie): a pod holds while its live head-count EXCEEDS half its spawn
    /// strength (floored at 1 — a lone survivor of a 2-pod always qualifies). W10 note: TERROR was
    /// originally a 2/3 threshold here, but real pods spawn size 2 (Mission.SpawnEnemies pairs
    /// them), where half-strength already routs the survivor on the first kill — a threshold change
    /// was arithmetic dead weight. TERROR now extends the rout DURATION instead (RoutDurationFor).
    public int RoutThreshold(int orig) => Math.Max(1, orig / 2);

    /// W10 TERROR boon (redesigned per review): broken enemies stay broken LONGER — the duration
    /// assigned when a pod breaks (BreakPodMorale, the only assignment site; a rallied pod that
    /// re-breaks passes through it again) is extended by TerrorRoutBonus. W8's banner semantics
    /// are untouched: an in-aura survivor still rallies at DOUBLE pace (BeginEnemyUnitTurn's extra
    /// decrement) — TERROR raises the base the banner recovers from, it never disables the counter.
    public const int TerrorRoutBonus = 2;
    public int RoutDurationFor() => RoutDuration + (HasBoon(Boon.Terror) ? TerrorRoutBonus : 0);

    /// SIGNAL W8 — this pod member is ONE KILL from the rout threshold: the NEXT pod death breaks
    /// the survivors (mirrors BreakPodMorale's `mates.Count > RoutThreshold(orig)` exactly, one
    /// kill ahead). Covers the rallied-below-threshold pod too (any kill re-breaks it) — the
    /// telegraph must never lie. Active + unbroken members only; needs a survivor left to rout.
    public bool PodAtWaverPoint(Unit e)
    {
        if (e == null || e.Team != Team.Enemy || e.PodId < 0 || !e.Alive || !e.Active || e.Routed > 0) return false;
        var (alive, orig) = PodStrength(e.PodId);
        return alive >= 2 && alive - 1 <= RoutThreshold(orig);
    }

    /// SIGNAL W8 — the WAVERING tag: at the waver point AND not held by a banner (a banner-anchored
    /// member will NOT rout on the next kill, so tagging it would lie — the tooltip explains why).
    public bool PodWavering(Unit e) => PodAtWaverPoint(e) && !BannerNear(e);

    /// SIGNAL W8 (harness/showcase only): seed a pod's spawn-strength snapshot so controlled scenes
    /// (SIGHTLINE_CONTENT / MORALETEST) can stage WAVERING states without running SetupMission.
    public void DebugPodOrig(int pod, int orig) => _podOrig[pod] = orig;

    // Death scorch decals: where a unit fell, a dark team-tinted burn mark lingers on the tile
    // and fades over ~ScorchLife seconds (decayed in Update, drawn under units in Renderer, cleared
    // per mission). So a kill reads as a burst + a lingering scorch, not an instant disappearance.
    public struct Scorch { public Vector2 Pos; public Color Tint; public float Life, MaxLife; }
    public readonly List<Scorch> Scorches = new();
    public const float ScorchLife = 1.6f;    // seconds a scorch lingers before it's fully gone
    /// Register a fading scorch decal at a fallen unit's position (team-tinted).
    public void AddScorch(Vector2 at, Color tint)
    {
        // cap the list so a long mission can't accumulate unbounded decals (cheap; cosmetic)
        if (Scorches.Count > 64) Scorches.RemoveAt(0);
        Scorches.Add(new Scorch { Pos = at, Tint = tint, Life = ScorchLife, MaxLife = ScorchLife });
    }
    public void AddHitStop(float s) { HitStop = MathF.Max(HitStop, s); }
    public void AddZoomPunch(float p) { _camPulse = MathF.Max(_camPulse, p); }

    // Phase 5.2 post-FX: bloom spikes on hits/kills/crits and decays smoothly.
    float _postFxBloom;   // 0..1, decays ~1.5 s
    // AddBloom is called alongside AddHitStop; magnitude maps s (0.1 normal, 0.4 kill-cam) -> bloom.
    public void AddBloom(float s) { _postFxBloom = MathF.Min(1f, _postFxBloom + s * 2.2f); }

    // ---------------- run-opening squad draft (Wave 3) ----------------
    // The interactive new-run path (intro / post-run) routes through a DRAFT screen where the
    // player picks a founding squad + a starting boon. These hold the in-progress / confirmed
    // picks; both default null so the harness path (StartMission called DIRECTLY, bypassing the
    // intro) uses the fixed default squad and no starting boon — byte-stable + no TIMEOUT.
    public List<Unit> DraftPool = new();        // the 6 candidate recruits on offer
    public List<Boon> DraftBoonOffer = new();   // the 3 starting boons on offer
    public HashSet<Unit> DraftPicked = new();   // candidates currently selected
    public Boon? DraftSelectedBoon;             // the selected starting boon (null until chosen)
    // W6 RUN CONTRACT: the run-long ruleset trade-off picked at the draft. null == STANDARD (Contract.None,
    // the opt-out), so the draft never BLOCKS on a contract — DraftReady ignores it. Threaded into
    // _run.Contract on CONFIRM (mirrors DraftSelectedBoon -> DraftBoon -> ActiveBoons).
    public Contract? DraftSelectedContract;     // the selected contract (null until clicked == STANDARD/None)
    public const int DraftCap = 4;              // founding core size (roster still backfills to 6 over the run)
    // Threaded into StartMission's new Run on CONFIRM (then cleared back to null/empty):
    public List<Unit> DraftedSquad;             // the confirmed 4 picked Units (null = default squad)
    public Boon? DraftBoon;                     // the confirmed starting boon (null = none)
    public Contract DraftContract = Contract.None;  // the confirmed contract (None = STANDARD)
    // W6 harness hook: SIGHTLINE_CONTRACT forces a contract on the autopilot/balance run so the smoke
    // test can verify each for no-crash/no-TIMEOUT. Program.cs sets this (a public field) BEFORE
    // StartMission; default None keeps plain autoplay byte-stable. Only honoured under NoPersist.
    public Contract ForcedContract = Contract.None;
    // FUL-1 probe hook: SIGHTLINE_PERK forces the bot to TAKE this perk whenever a rank-up offer
    // contains it (ChoosePerk), so a paired probe batch can price ONE perk against its absence.
    // Honoured only under NoPersist; the override lands AFTER the value roll has drawn, so the
    // probe leg and its paired baseline consume identical Util.Rng draws (CRN-safe).
    public Perk? ForcedPerk = null;
    // W2 harness hook: WHOLE-RUN objective pin (SIGHTLINE_OBJ under the balance batch / autoplay).
    // DebugForceObjective rewrites only the CURRENT card — missions 2+ come from the campaign map,
    // so an objective sweep with the old pin measured 1 forced mission + ~5 normal ones. This pin
    // is honoured inside SetupMission for EVERY mission of the run, gated on NoPersist so normal
    // play can never see it. Null = no pin (byte-stable default).
    public Objective? ForcedObjective;

    // W9 (SIGNAL) priced recall: the salvage bank CACHED for the draft UI (loaded once at BeginDraft
    // and after a paid re-roll — the draw path must never touch disk). 0 under NoPersist (no read);
    // DebugVetDraft seeds a demo value so the priced/greyed card states can be screenshot headless.
    public int DraftSalvage;

    /// The summed recall fee for the CURRENTLY PICKED veterans (10 + 8xRank each; fresh recruits free).
    public int DraftRecallCost
    {
        get
        {
            int c = 0;
            foreach (var u in DraftPicked) if (u.FromReserve) c += DraftRecallFee(u);
            return c;
        }
    }

    /// FUL-10 MRC: ONE veteran's recall fee under the draft-screen selection — half, rounded up,
    /// when MERCENARY CLAUSE is picked. Reads DraftSelectedContract (both live on the same screen),
    /// so the per-card fee, the bill row and ConfirmDraft's charge all discount together — honest
    /// by construction, and the halving happens in exactly one place.
    public int DraftRecallFee(Unit u)
    {
        int c = MetaProg.RecallCost(u.Rank);
        if ((DraftSelectedContract ?? Contract.None) == Contract.MercenaryClause) c = (c + 1) / 2;
        return c;
    }

    /// Can the bank cover the picked veterans? (Always true under NoPersist — the harness never pays.)
    public bool DraftRecallAffordable => NoPersist || DraftRecallCost <= DraftSalvage;

    /// Build the draft candidate pool honouring the WAR ROOM unlocks (veteran recall window +
    /// cross-training). Shared by BeginDraft and the paid pool re-roll so both agree. Under
    /// NoPersist no disk is read -> an all-fresh default pool (byte-stable + no TIMEOUT).
    List<Unit> BuildDraftPool()
    {
        if (NoPersist) return Run.GenerateDraftPool();
        int maxVets = Run.MaxDraftVeterans + (SaveGame.HasUnlock((int)MetaUnlock.StandingReserve) ? 1 : 0);
        bool crossTrain = SaveGame.HasUnlock((int)MetaUnlock.CrossTraining);
        return Run.GenerateDraftPool(SaveGame.LoadVeterans(), maxVets, crossTrain);
    }

    // W12 first-run RECOMMENDED draft: the pre-selected fixed squad + safe doctrine, so a brand-new
    // player's first decision screen is one DEPLOY click (everything stays re-pickable). Cards in
    // DraftRecommended / the DraftRecommendedBoon get a badge in DrawDraft.
    public HashSet<Unit> DraftRecommended = new();
    public Boon? DraftRecommendedBoon;
    public bool DraftHasRecommendation => DraftRecommended.Count > 0;

    /// Seat the fixed NewRunSquad four over the pool's fresh recruits (never displacing a recalled
    /// veteran card), pre-pick them, and pre-select the steadiest doctrine the offer rolled.
    /// Pure state, no disk/RNG beyond NewRunSquad's fixed builds — callers gate WHEN it runs.
    void ApplyRecommendedDraft()
    {
        var fixedSquad = Sightline.Mission.NewRunSquad();
        int fi = 0;
        for (int i = 0; i < DraftPool.Count && fi < fixedSquad.Count; i++)
        {
            if (DraftPool[i].FromReserve) continue;   // a returning legend outranks the training default
            DraftPool[i] = fixedSquad[fi++];
        }
        DraftPicked.Clear();
        for (int i = 0; i < fi; i++)
        {
            if (DraftPicked.Count < DraftCap) DraftPicked.Add(fixedSquad[i]);
            DraftRecommended.Add(fixedSquad[i]);
        }
        // FUL-12: the safety ranking now covers the FULL 16-boon pool (a 6-entry list left 21.4%
        // of first-run offers falling through to "whatever rolled first" — an arbitrary pick
        // wearing the RECOMMENDED badge). The first six keep their measured order; the tail ranks
        // by new-player value: passive/always-on before conditional, simple verbs before stealth
        // micro (Ghost last — expert play wearing a beginner badge was the worst failure mode).
        Boon[] pref =
        {
            Boon.Fortified, Boon.Scavenger, Boon.Marksmen, Boon.Executioners, Boon.Grenadier, Boon.Fervor,
            Boon.RapidDeploy,    // +1 body all run: the most forgiving thing a new squad can have
            Boon.FieldStores,    // doubled item charges: passive, no decision cost
            Boon.Adrenaline,     // kill -> +1 action: triggers on the thing beginners already do
            Boon.ShockDoctrine,  // BRACE at full damage: pairs with the FUL-12 brace field tip
            Boon.Pyromaniacs,    // fire immunity half is pure safety even if the burn half idles
            Boon.Venom,          // free chip damage on every hit; zero micro
            Boon.FieldDrills,    // doubled DRAG/VAULT: useful but assumes the verbs are known
            Boon.Reclaimer,      // focused-cone re-arm: needs the FOCUS verb in the vocabulary
            Boon.Terror,         // longer routs: strong, but morale play is a mid-game concept
            Boon.Ghost,          // concealment micro is expert tempo — never a first-run default
        };
        foreach (var b in pref)
            if (DraftBoonOffer.Contains(b)) { DraftRecommendedBoon = b; break; }
        // unreachable while pref spans the whole enum — kept as a guard for a future pool grow
        if (!DraftRecommendedBoon.HasValue && DraftBoonOffer.Count > 0) DraftRecommendedBoon = DraftBoonOffer[0];
        DraftSelectedBoon = DraftRecommendedBoon;
    }

    /// Set up + enter the run-opening DRAFT (interactive new-run path only). Builds the candidate
    /// pool + the starting-boon offer and switches to Phase.Draft. NEVER called by the harness
    /// (the autoplay/balance/screenshot paths call StartMission directly — except the SIGHTLINE_DRAFT
    /// shot hook, which stays on the no-recommendation leg via NoPersist).
    public void BeginDraft()
    {
        // COUNTERPLAY: recall the cross-run VETERAN reserve into the draft (up to Run.MaxDraftVeterans,
        // +1 with STANDING RESERVE). Under NoPersist (harness) no disk is read -> an all-fresh pool.
        DraftPool = BuildDraftPool();
        DraftSalvage = NoPersist ? 0 : SaveGame.LoadSalvage();
        DraftBoonOffer = Run.GenerateDraftBoonOffer();
        DraftPicked = new HashSet<Unit>();
        DraftSelectedBoon = null;
        DraftSelectedContract = null;   // default STANDARD (Contract.None) until a contract card is clicked
        DraftRecommended = new HashSet<Unit>();
        DraftRecommendedBoon = null;
        // W12: FIRST-EVER run (tutorial not yet seen AND no veteran reserve — a recalled legend in
        // the pool means this account has finished runs, so the training default would be noise and
        // METATEST's veteran-pricing legs must see an untouched draft) -> pre-select the recommended
        // loadout. !NoPersist keeps every harness/self-test leg (incl. DRAFTTEST's byte-stable
        // null-veterans path) untouched; SIGHTLINE_DRAFTREC=1 stages the state for a headless shot.
        bool anyReserve = false;
        foreach (var u in DraftPool) if (u.FromReserve) { anyReserve = true; break; }
        if ((!NoPersist && !Display.TutorialSeen && !anyReserve) ||
            (NoPersist && Environment.GetEnvironmentVariable("SIGHTLINE_DRAFTREC") == "1"))
            ApplyRecommendedDraft();
        Phase = Phase.Draft;
        Audio.Play("select");
    }

    /// Draft input: toggle a candidate (cap DraftCap), single-select a boon, CONFIRM when valid.
    void HandleDraftClick()
    {
        // W1 mode-seam: BACK/Esc returns to the intro without founding a run (mirrors
        // HandleSkirmishSetup) — a mis-click into NEW RUN is no longer a one-way door.
        bool back = Raylib.IsKeyPressed(KeyboardKey.Escape)
                    || (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                        Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Hud.DraftBack));
        if (back) { Phase = Phase.Intro; Audio.Play("select"); return; }

        var m = Raylib.GetMousePosition();
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            // W9: paid pool RE-ROLL (a repeatable salvage sink; TryRerollDraftPool refuses when broke)
            if (Raylib.CheckCollisionPointRec(m, Hud.DraftReroll)) { TryRerollDraftPool(); return; }
            // candidate cards
            foreach (var (unit, rect) in Hud.DraftCardBtns)
            {
                if (!Raylib.CheckCollisionPointRec(m, rect)) continue;
                if (DraftPicked.Contains(unit)) DraftPicked.Remove(unit);
                else if (DraftPicked.Count < DraftCap) DraftPicked.Add(unit);
                Audio.Play("select");
                return;
            }
            // boon cards (single-select)
            foreach (var (boon, rect) in Hud.DraftBoonBtns)
            {
                if (!Raylib.CheckCollisionPointRec(m, rect)) continue;
                DraftSelectedBoon = boon;
                Audio.Play("select");
                return;
            }
            // contract cards (single-select; clicking the held one toggles back to STANDARD/None)
            foreach (var (contract, rect) in Hud.DraftContractBtns)
            {
                if (!Raylib.CheckCollisionPointRec(m, rect)) continue;
                DraftSelectedContract = (DraftSelectedContract.HasValue && DraftSelectedContract.Value == contract)
                    ? (Contract?)null : contract;   // re-click to deselect -> STANDARD
                Audio.Play("select");
                return;
            }
            // confirm
            if (DraftReady && Raylib.CheckCollisionPointRec(m, Hud.DraftConfirm)) { ConfirmDraft(); return; }
        }
        // keyboard: Enter deploys when the draft is complete
        if (DraftReady && Raylib.IsKeyPressed(KeyboardKey.Enter)) ConfirmDraft();
        // W5-FIX (review blocker 2): [R] re-rolls the pool. RE-ROLL POOL was the one control on
        // this screen with NO keyboard route, which is why a layout that pushed it off the bottom
        // made it unreachable rather than merely awkward. R is free in this phase (the draft reads
        // only Esc and Enter) and is the same mnemonic RELOAD uses in the fight.
        if (Raylib.IsKeyPressed(KeyboardKey.R)) TryRerollDraftPool();
    }

    /// True when exactly DraftCap soldiers and one boon are chosen (CONFIRM/Enter enabled).
    public bool DraftReady => DraftPicked.Count == DraftCap && DraftSelectedBoon.HasValue;

    /// Finalize the draft: stage the picks, then run the SAME new-run start the intro would have.
    /// W9 priced recall: the summed veteran fee (10+8xRank each) is charged ONCE here — never at
    /// pick time (picks toggle freely, and the BACK button must always leave the bank untouched).
    /// An unaffordable CONFIRM refuses outright: nothing is charged, nothing is seated, and the
    /// picks stay intact so the player can rearrange toward what they CAN afford.
    void ConfirmDraft()
    {
        if (!NoPersist)
        {
            int cost = DraftRecallCost;
            if (cost > 0)
            {
                if (!SaveGame.SpendSalvage(cost)) { Audio.Play("miss"); return; }   // refuse; picks intact
                DraftSalvage = SaveGame.LoadSalvage();
            }
        }
        DraftedSquad = new List<Unit>(DraftPicked);
        DraftBoon = DraftSelectedBoon;
        DraftContract = DraftSelectedContract ?? Contract.None;   // null == STANDARD
        Audio.Play("turn");
        StartMission();   // threads DraftedSquad/DraftBoon/DraftContract into the new Run, then clears them
    }

    // ---------------- lifecycle ----------------
    /// W1 mode-seam: zero every cross-mode field before ANY mode entry (StartMission / BeginEndless /
    /// BeginSkirmish / BeginDaily / ContinueRun) so no mode can inherit another's state — mode flags,
    /// the daily stamp + cached best, the endless wave counter, a daily-forced arena, or the daily's
    /// deterministic RNG seed. Each Begin* re-applies its own needs right after. The forced-layout
    /// clear + reseed are INTERACTIVE-ONLY (!NoPersist): the harness pins arenas via SIGHTLINE_MAP
    /// and needs the RNG stream untouched for byte-stable shots/measurement.
    void ResetModeState()
    {
        RunTurns = 0;   // W9: the run-scoped autopilot budget resets at the mode seam, and ONLY here
        Mode = GameMode.Campaign;
        DailyMode = false;
        DailyStamp = 0;
        _dailyBest = -1;
        Wave = 0;
        // W9 review fix: an abandoned barracks' uncommitted pending spend dies with its run — a new
        // mode entry must never inherit (and later commit) a charge for goods that no longer exist.
        _pendingSalvage = 0;
        if (!NoPersist) { Mission.ForcedLayout = -1; Util.Reseed(0); }
        // W11: per-RUN teaching state — the honest-loss tally and the NEW CONTACT memory reset at
        // every mode entry (this is the one choke-point all of StartMission / BeginEndless /
        // BeginSkirmish / BeginDaily / ContinueRun pass through). Note a CONTINUEd run restarts
        // both: the save doesn't carry them (Run.cs / the save format are outside this seam), so
        // a resumed run re-IDs contacts and tallies causes from the resume point onward.
        DeathsByClass.Clear();
        _seenArchetypes.Clear();
        // FUL-11 review hardening: SetupMission already re-arms this per mission, but clearing
        // at the mode seam too means a future second IsBoss spawn point can't silently turn
        // "once per sighting ceremony" into a stale carry-over across mode entries.
        _bossSighted = false;
        // W5: the mission-1 strip's PENDING arm is per-run state too — a new mode entry must never
        // inherit an arm from a run that ended before the briefing retired.
        _tutPending = false;
        // FUL-12: the end-card meta payoff is per-RUN — a new mode entry must not inherit the
        // previous run's SALVAGE slab / HEAT UNLOCKED line / achievement roll.
        EndSalvage = 0; EndReserve = 0; EndHeatUnlocked = 0; EndAchievements.Clear();
        // Harness affordance (screenshot only, mirrors the SIGHTLINE_HEAT pattern): pre-seed the
        // cause-of-death tally, e.g. SIGHTLINE_DEATHS=SNIPER:2,GRUNT:1 — so the lose-card line can
        // be framed without playing a full losing run. Inert when unset -> plain shots byte-stable.
        if (NoPersist)
        {
            var seed = Environment.GetEnvironmentVariable("SIGHTLINE_DEATHS");
            if (!string.IsNullOrEmpty(seed))
                foreach (var part in seed.Split(','))
                {
                    var kv = part.Split(':');
                    if (kv.Length == 2 && int.TryParse(kv[1], out int n) && n > 0)
                        DeathsByClass[kv[0].Trim().ToUpperInvariant()] = n;
                }
        }
    }

    /// Start a brand-new campaign run (called from intro / after a run ends).
    /// startAt lets the headless harness jump straight to a given mission.
    public void StartMission(int startAt = 1)
    {
        ResetModeState();
        EnsureMetaLoaded();
        _run = new Run();
        _run.Start(DraftedSquad);           // builds the campaign map, seats at the START node (drafted squad if any)
        if (DraftBoon.HasValue) { _run.ActiveBoons.Add(DraftBoon.Value); Stats.RecordBoon(BoonDef.Code(DraftBoon.Value)); }   // adopt the chosen starting boon
        // W6 RUN CONTRACT: adopt the drafted contract (interactive path). The harness never runs the
        // draft, so DraftContract stays None there -> no balance change. The SIGHTLINE_CONTRACT hook
        // (ForcedContract, set by Program.cs) overrides it under NoPersist so the balance bot can
        // smoke-test each contract for no-crash/no-TIMEOUT. Default None = STANDARD = byte-stable.
        _run.Contract = NoPersist && ForcedContract != Contract.None ? ForcedContract : DraftContract;
        if (_run.Contract != Contract.None) Stats.RecordContract(ContractDef.Code(_run.Contract));
        DraftedSquad = null; DraftBoon = null; DraftContract = Contract.None;   // consumed: the harness path leaves these default
        _spearheadSurgeUsed = false;   // W6 SPEARHEAD turn-1 action surge is fresh each run (and per mission, below)
        // adopt the dialled-in Heat for this run. The harness can't set PendingHeat (it doesn't
        // touch the intro), so it reads SIGHTLINE_HEAT here instead — defaulting to 0 so plain
        // autoplay/screenshots are byte-stable.
        int heat = PendingHeat;
        if (NoPersist && int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_HEAT"), out int hEnv)) heat = hEnv;
        _run.HeatLevel = Sightline.Heat.Clamp(heat);
        // W11 harness affordance (screenshot only, same family as SIGHTLINE_HEAT/VETSIM above):
        // SIGHTLINE_BOONS=<k> grants the first k boons so the in-mission boon-chip strip and its
        // hover card can be framed headless. Deterministic; inert when unset -> byte-stable.
        if (NoPersist && int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_BOONS"), out int boonsN) && boonsN > 0)
            for (int bi = 0; bi < BoonDef.All.Length && bi < boonsN; bi++)
                if (!_run.ActiveBoons.Contains(BoonDef.All[bi])) _run.ActiveBoons.Add(BoonDef.All[bi]);
        _run.LossStreak = _metaLossStreak;  // adaptive assist: carry the loss history into this run
        // balance telemetry (no-op unless Stats.Enabled); tag the policy so the report can
        // split greedy vs sloppy win-rates and surface the optimal-vs-error GAP.
        Stats.BeginRun(_run.HeatLevel, SmartPlay && SmartSloppy ? "sloppy" : "greedy");
        // APEX W4 (c): SIGHTLINE_VETSIM=<n> — honoured only under NoPersist, mirroring the
        // SIGHTLINE_CONTRACT hook above. The flywheel calls StartMission directly (the Phase.Draft
        // veteran recall never runs there), so recalled veterans were invisible to measurement;
        // this swaps n founding rookies for deterministic synthetic Rank-3 veterans (two class-
        // line perks, +1 armor — Run.ApplyVetSim) so paired VETSIM=n vs 0 batches can price the
        // veteran power floor. In-memory only; inert when unset, so plain autoplay/screenshot
        // runs are untouched. It prices a NOMINAL Rank-3 veteran, not the exact recall payload.
        if (NoPersist && int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_VETSIM"), out int vetSimN) && vetSimN > 0)
        {
            int vetsMade = _run.ApplyVetSim(vetSimN);
            if (vetsMade > 0) Console.WriteLine($"VETSIM: {vetsMade} founding rookie(s) -> synthetic Rank-3 veterans (nominal recall-floor probe)");
        }
        // PROGRAM HORIZON W3 (WAR ROOM): apply purchased cross-run UNLOCKS to the founding run. ADDITIVE
        // only, campaign only, and STRICTLY gated behind !NoPersist — the flywheel/harness never read
        // meta, so a measured/screenshot run is byte-identical to today (a fresh profile owns none anyway).
        ApplyMetaUnlocks();
        // SIGNAL W5 harness affordance (SIGHTLINE_HEAT family; NoPersist only): pin the FINALE KIT.
        // SIGHTLINE_FINALE=legion|syndicate|wardens re-stamps the Boss node's faction (normally
        // drawn deterministically from MapSeed in Run.GenerateMap) so per-kit screenshots and
        // per-kit balance batches are reproducible. Inert when unset -> plain runs untouched.
        if (NoPersist)
        {
            var fin = Environment.GetEnvironmentVariable("SIGHTLINE_FINALE");
            if (!string.IsNullOrEmpty(fin))
            {
                Faction? f = fin.Trim().ToLowerInvariant() switch
                {
                    "legion" => Faction.Legion, "syndicate" => Faction.Syndicate,
                    "wardens" => Faction.Wardens, _ => (Faction?)null,
                };
                if (f.HasValue) _run.StampFinaleKit(f.Value);
                else Console.WriteLine($"HARNESS: unknown SIGHTLINE_FINALE '{fin}' — kit unpinned");
            }
        }
        // SIGNAL W5 measurement: surface this run's (seed-chosen or pinned) FINALE KIT to the
        // balance-batch log so per-seed kit reachability is countable. No-op outside the flywheel.
        if (Stats.Enabled && _run.Map.Count > 0)
            Console.WriteLine($"FINALE-KIT: {_run.Map[_run.Map.Count - 1].Faction}");
        Players = _run.Squad;
        int n = Util.Clamp(startAt, 1, Run.MaxMissions);
        if (n > 1) _run.JumpTo(n);           // harness: advance along the map to the requested op
        SetupMission(n);
    }

    /// Apply the persisted WAR ROOM unlocks to the just-started campaign run. No-op under NoPersist
    /// (harness/flywheel) and in endless (never called from BeginEndless), so measurement stays clean.
    void ApplyMetaUnlocks()
    {
        if (NoPersist) return;
        if (SaveGame.HasUnlock((int)MetaUnlock.StartIntel))
            _run.Intel += 15;
        if (SaveGame.HasUnlock((int)MetaUnlock.StartBoon))
        {
            // grant one random boon not already active this run (a fresh run owns none).
            var pool = new List<Boon>();
            foreach (var b in BoonDef.All) if (!_run.HasBoon(b)) pool.Add(b);
            if (pool.Count > 0)
            {
                var pick = pool[Util.RandInt(0, pool.Count - 1)];
                _run.ActiveBoons.Add(pick);
                Stats.RecordBoon(BoonDef.Code(pick));
            }
        }
        if (SaveGame.HasUnlock((int)MetaUnlock.StartArmor))
            foreach (var u in _run.Squad) u.Armor += 1;
    }

    // Lazily load the persisted unlocked-max Heat once (gated by NoPersist like all save I/O,
    // so the headless harness never reads disk and stays at the default unlock of 0).
    bool _metaLoaded;
    int _metaLossStreak;          // adaptive-assist loss streak loaded from meta.json (0 headless)

    /// W11 HONEST LOSSES: the assist tier the NEXT run would start with at the currently dialled
    /// heat (Run.AssistLevel's exact formula, previewed from the meta loss streak before a Run
    /// exists). The intro heat panel shows it as a FIELD SUPPORT chip — the easing was invisible.
    /// 0 headless (streak never loads under NoPersist), so plain intro shots stay byte-stable.
    public int AssistPreview => PendingHeat > 0 ? 0 : Math.Min(Run.AssistMax, _metaLossStreak);

    // ── W5 THE ON-RAMP (audit newplayer-4) ────────────────────────────────────────────────────
    /// True when this profile has never finished a run. The intro uses it twice: it DEFAULTS the
    /// difficulty dial to RECRUIT, and it changes level 0's hint so the rung below is named
    /// instead of hidden behind an unlabelled "-".
    ///
    /// The game already built a proper beginner rung and then defaulted every first-time player
    /// off it, with copy ("standard difficulty - the designed fight") that framed 0 as the floor.
    /// The archived X2 ladder (n=40/rung, base a61ef42, docs/measurements/x2/) puts RECRUIT at
    /// 75.0% run completion against heat 0's 57.5% — a 17.5-point gap, outside the +-6-8 error
    /// bar. Roughly two in five first campaigns were ending in a loss the on-ramp exists to
    /// prevent. This moves a DEFAULT, not a rung: every heat number in docs/ is untouched, and
    /// the measurement harness sets heat explicitly (SIGHTLINE_HEAT / SIGHTLINE_BALANCE_HEAT)
    /// under NoPersist, which returns from EnsureMetaLoaded before this can be read.
    public bool FirstTimeProfile;

    void EnsureMetaLoaded()
    {
        if (_metaLoaded) return;
        _metaLoaded = true;
        if (NoPersist)
        {
            // W5: SIGHTLINE_FIRSTRUN=1 (the same flag that arms the mission-1 strip under the
            // harness) also simulates a NEVER-PLAYED profile here, so the intro's on-ramp default
            // can be photographed headless. An explicit SIGHTLINE_HEAT below still overrides it.
            // Inert unless the variable is set -> plain shots and PAIRTEST stay byte-stable.
            if (FirstRunShot) { FirstTimeProfile = true; PendingHeat = Sightline.Heat.Recruit; }
            // Harness/screenshot affordance only: SIGHTLINE_HEAT lets the headless intro shot
            // preview the dialled-in level + its unlocked ceiling. No disk I/O; default 0 keeps
            // a plain shot byte-stable.
            // W5: the hook now also accepts the sub-standard rung (-1 = RECRUIT) so the intro
            // DIFFICULTY card can be photographed at it. Unset / 0 still changes nothing, so a
            // plain intro shot stays exactly as before.
            if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_HEAT"), out int hEnv) && hEnv != 0)
            {
                UnlockedHeat = Sightline.Heat.Clamp(Math.Max(0, hEnv));
                PendingHeat = Sightline.Heat.Clamp(hEnv);
            }
            // W11 (same affordance family): SIGHTLINE_LOSSTREAK=<n> seeds the assist streak so the
            // intro FIELD SUPPORT chip can be screenshot headless. No disk; default 0 = byte-stable.
            if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_LOSSTREAK"), out int lsEnv) && lsEnv > 0)
                _metaLossStreak = lsEnv;
            return;
        }
        UnlockedHeat = SaveGame.LoadMetaHeat();
        var (metaRuns, _, _) = SaveGame.LoadRunTotals();
        FirstTimeProfile = metaRuns == 0;
        // W5 THE ON-RAMP: a profile that has never finished a run opens on RECRUIT. One-shot —
        // EnsureMetaLoaded runs once per process (_metaLoaded), so every later stepper press is
        // the player's and sticks. The Min() below still applies, and clamps to -1 at UnlockedHeat
        // 0, so this can never dial a rung the player has not earned.
        if (FirstTimeProfile) PendingHeat = Sightline.Heat.Recruit;
        PendingHeat = Math.Min(PendingHeat, UnlockedHeat);
        _metaLossStreak = SaveGame.LoadMetaLossStreak();
    }

    // ---- C4 "EIGHT BIOMES ARE PAINT": the biome GROUND layer -------------------------------
    /// The index into Biome.All this mission is actually SHOWING (Biome.IndexFor is the same
    /// (mission, seed) function that picked `Biome` itself, so the mechanic can never disagree
    /// with the room the player is looking at — the FUL-9 lesson, applied again).
    public int BiomeIndex => _run != null ? Sightline.Biome.IndexFor(_run.Mission, _run.MapSeed) : 0;

    /// " · UNDERGROWTH" etc for the mission banner, or "" on the five biomes that are still paint.
    public string BiomeMechTag()
    {
        var tag = Terrain.Tag(BiomeIndex);
        return tag == null ? "" : " - " + tag;
    }

    /// The one-sentence rule for the briefing card / codex, or null.
    public string BiomeMechRule() => Terrain.Rule(BiomeIndex);

    /// How many tiles of mechanical ground this board carries (0 on the five paint biomes).
    /// Telemetry only — Stats records it so a rung can be split by how much ground was stamped.
    public int CountGroundTiles()
    {
        if (Grid == null || !Terrain.Enabled) return 0;
        int n = 0;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++) if (Grid.Ground[x, y] != GroundKind.None) n++;
        return n;
    }

    /// Stamp the ground layer for mission `n`. Every tile the layer must not touch is reserved
    /// first: a unit's own tile and its ring (nobody deploys standing in a fissure), the evac zone,
    /// the hack terminal and each sabotage charge, all WITH their rings, because those are approach
    /// tiles the objective depends on. The INTEL CACHE is the one exception and takes its own tile
    /// only (`Ring(...,0)`): it is an optional pickup, not a win condition, and a soldier detouring
    /// for it may reasonably have to pay for the ground around it. The doc comment used to claim
    /// "all with their rings", which was wrong (C4 review).
    void StampBiomeGround(int n)
    {
        var reserved = new HashSet<(int x, int y)>();
        void Ring(int cx, int cy, int r)
        {
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++) reserved.Add((cx + dx, cy + dy));
        }
        foreach (var u in Players) if (u != null) Ring(u.X, u.Y, 1);
        foreach (var u in Enemies) if (u != null) Ring(u.X, u.Y, 1);
        if (EvacZone != null) foreach (var t in EvacZone) Ring(t.x, t.y, 1);
        if (HasTerminal) Ring(Terminal.x, Terminal.y, 1);
        if (HasSabotage && SabotageSites != null) foreach (var s in SabotageSites) Ring(s.x, s.y, 1);
        if (CachePresent) Ring(CacheX, CacheY, 0);
        Terrain.Stamp(Grid, BiomeIndex, _run != null ? _run.MapSeed : 0, n, reserved);
    }

    void SetupMission(int n)
    {
        _run.Mission = n;
        // TEMPO wave 4: publish ALL this mission's combat statics in one lifecycle call — the run's
        // boons, the mission faction (read by the spawn roster in Mission.Build below, so it MUST be
        // set first), the bought counter-prep, and a clean PressureAim/AllUnits. Replaces the four
        // scattered manual resets that used to live here / at lines further down.
        Combat.BeginMission(_run.ActiveBoons, _run.CurrentNode?.Faction ?? Faction.None, _run.PrepFaction);
        _run.PrepFaction = Faction.None;   // counter-prep is one mission only — consume it
        Stats.ClearLog();   // fresh combat-log ledger each mission
        // reset camera to identity each new mission (auto-cam will gently ease in if enabled)
        CamZoom = 1f; CamPan = Vector2.Zero; _autoCamManual = false;
        // per-mission roster: a copy of the persistent squad (+ an optional VIP),
        // so adding the escort asset never pollutes the campaign squad.
        // Benched soldiers sit this mission out (deploy short-handed). They get accelerated
        // recovery in DebriefSurvivors and the flag is cleared THERE (EnterBarracks, after the
        // debrief consumes it) - so it must survive the whole mission. Do NOT clear it here,
        // or the recovery path is dead code (review Blocker 2).
        // C5: a run that has soldiers must field one. See Run.EnsureFieldable — the UI guards the
        // click, nothing guarded the persisted flag, and an all-benched roster set up a mission
        // with an empty board.
        _run.EnsureFieldable();
        Players = new List<Unit>(_run.Squad.Where(u => !u.Benched));

        // objective + difficulty come from the chosen deployment card (Run.ObjectiveFor baseline)
        var card = _run.CurrentCard ?? Run.StandardCard(n);
        Objective = card.Objective;
        // W2: whole-run objective pin (harness only). Overrides the card's objective on EVERY
        // mission so SIGHTLINE_OBJ sweeps measure N missions of the pinned type, not 1 + noise.
        // Applied BEFORE the endless force below so LAST STAND's Eliminate invariant still wins.
        if (NoPersist && ForcedObjective.HasValue) Objective = ForcedObjective.Value;
        // PROGRAM HORIZON W2: LAST STAND is a pure kill-the-horde arena — force Eliminate so every
        // objective-gated setup block below (evac/terminal/sabotage/escort/rescue) is a no-op.
        if (Mode == GameMode.Endless) Objective = Objective.Eliminate;
        // T1: the TRAINING OP is a fixed kill-the-targets drill — force Eliminate so every
        // objective-gated setup block below (evac/terminal/sabotage/escort/rescue) is a no-op.
        if (Mode == GameMode.Training) Objective = Objective.Eliminate;
        EvacZone.Clear();
        BeaconPlanted = false; BeaconZone.Clear(); BeaconTile = default;   // forward evac beacon is fresh each mission
        HackProgress = 0;
        SabotageSites.Clear();
        SabotageBlown.Clear();
        Vip = null;
        Hvt = null;
        HvtBuffed = false;
        CaptiveLocked = false;
        // Evac / Escort / Rescue all extract to the same top-right zone
        if (Objective == Objective.Evac || Objective == Objective.Escort || Objective == Objective.Rescue)
        {
            // A 2x4 extraction block (8 tiles) in the top-right. CRITICAL: with deploy-growth the
            // squad can field up to DeployCapMax soldiers, and EVAC requires ALL of them to stand
            // in the zone — a 2x2 (4 tiles) was unwinnable (and TIMEOUT-looping the autopilot) once
            // 5+ soldiers survived. 8 tiles fits the largest squad (+ the escort VIP) with margin.
            for (int ey = 0; ey < 4; ey++)
            {
                EvacZone.Add((Grid.W - 2, ey));
                EvacZone.Add((Grid.W - 1, ey));
            }
        }
        if (Objective == Objective.Hack)
            Terminal = (Grid.W / 2 + 1, Grid.H / 2);
        if (Objective == Objective.Sabotage)
        {
            int my = Grid.H / 2;
            SabotageSites.Add((Grid.W / 2 - 4, my - 2));
            SabotageSites.Add((Grid.W / 2 + 1, my));
            SabotageSites.Add((Grid.W / 2 + 4, my + 2));
        }
        if (Objective == Objective.Escort)
        {
            Vip = Mission.MakeVip(n);      // VIP durability scales with mission depth
            Vip.X = 2; Vip.Y = 5;          // valid pre-build tile (spawn table refines it)
            Players.Add(Vip);
        }
        if (Objective == Objective.Rescue)
        {
            Vip = Mission.MakeVip(n);
            Vip.Name = "CAPTIVE";
            Vip.X = 2; Vip.Y = 5;          // pre-build placeholder; re-seated at centre below
            Players.Add(Vip);
            CaptiveLocked = true;
        }

        // Heat folds into the SAME difficulty params the deployment cards use (no Mission.cs
        // signature change): extra bodies + an extra stat bump as the ladder climbs.
        int heat = _run.HeatLevel;
        // W6b — publish the AI coordination tier UNCONDITIONALLY every mission (0 at heats 0-5,
        // so DAILY/SKIRMISH/harness stay byte-stable by default AND a stale NO QUARTER tier can
        // never leak into the next fight through this shared DEPLOY/SKIRMISH/DAILY setup path;
        // Combat.EndMission mirrors it with a clear). Deliberately NOT ramped by the
        // early-mission heat grace below: the tier is a qualitative mutator like EXPOSED (which
        // also bites from mission 1), not a quantitative delta. The LAST STAND wave path
        // (SpawnEndlessWave) raises it as a stand deepens.
        Ai.Tier = Sightline.Heat.AiTier(heat);
        // SIGNAL W5 — the spec's finale Ai.Tier raise (floor the coordination tier at 1 for the
        // boss mission) was implemented and then MEASURED OUT: with it, the finale INVERTED the
        // policy ordering at heat 0 (greedy paired completion 80% -> 45% while sloppy held 70% ->
        // 75%) — the tier-1 focus-fire collapse punishes exactly the aggressive optimal policy,
        // recreating the punish-gap failure UNDERTOW closed. The m6 bite ships as the kit
        // signatures + the count restore instead; heats 6+ still bring their own tier via the
        // Heat row above. (Revisit only with flywheel evidence that the inversion is gone.)
        int heatEnemy = Sightline.Heat.EnemyDelta(heat);
        int heatStat  = Sightline.Heat.StatDelta(heat);
        // W6c introduced this as a rung-8-only +1 enemy damage; CONTOUR C1 moved the single point
        // that feeds it down to EXPOSED (rung 6), so it is live from heat 6 up, not just at the apex.
        int heatDmg   = Sightline.Heat.DmgDelta(heat);
        // EARLY-MISSION HEAT GRACE. The measured ~20% mission-1 loss (which hard-caps run
        // completion, a geometric product) was almost entirely a heat-3/4 alpha-strike on the
        // COLD OPENER: Heat adds +2 bodies / +2 stat to a force a green 4-rookie squad meets
        // before it has earned a single promotion, perk, or boon. Ramp Heat's contribution in
        // over the first missions so the ladder bites once the squad can answer it (m1 x0, m2
        // x1/2, m3+ full). Card deltas and the per-mission growth curve (Mission.cs) are
        // untouched — only Heat's extra bodies/stats ramp. Heat 0 stays a true no-op.
        // W5: the grace ramps HEAT's escalation in — it must not also ramp the RECRUIT rung's
        // RELIEF out. Rung -1's whole point is that mission 1 is survivable, which is exactly the
        // mission the grace would zero. Gated on heat > 0 so heats 1-8 are bit-for-bit unchanged.
        // W9 THE REPAIR — the grace is gated on CAMPAIGN, which makes it the thing it always claimed
        // to be. It exists to protect "a green 4-rookie squad meeting Heat before it has earned a
        // single promotion, perk or boon" — a statement about the CAMPAIGN OPENER. But SKIRMISH and
        // DAILY both enter through SetupMission(1) (Game.Modes.cs), so `n <= 1` was true for EVERY
        // skirmish and EVERY daily, and the grace zeroed the entire numeric ladder in two of the four
        // shipped modes. MEASURED: skirmish eliminate, seed 4242, arena 5 reads "4 SQUAD 4 HOSTILES"
        // at heat 0 and "4 SQUAD 4 HOSTILES  HEAT 8" at heat 8 — the red chip was the only difference
        // on screen — against a campaign control of 6 vs 10 hostiles on the same seed and arena. Only
        // the qualitative flags (Ai.Tier at rung 6+, SHORT FUSE, EXPOSED, NO QUARTER's label)
        // survived; every body, stat and damage point the dial promises was discarded.
        // This is ROADMAP:1066's own recommendation, which it left as an owner decision. THE CALL,
        // made: a SKIRMISH or DAILY player explicitly DIALLED the rung — the setup screen renders the
        // ladder and its per-rung modifier text, the intro sells "pick the objective and the heat",
        // and FEATURES.md line 13 sells "one fight with a chosen objective+heat". There is no green
        // squad to protect and no campaign ahead to front-load anxiety into; there is only the fight
        // they asked for. The grace stays exactly as it was for CAMPAIGN (and for ENDLESS, which
        // opens at n==1 too and whose escalation is the wave ladder, not heat).
        if (heat > 0 && Mode != GameMode.Skirmish)
        {
            if (n <= 1)      { heatEnemy = 0; heatStat = 0; heatDmg = 0; }
            else if (n == 2) { heatEnemy /= 2; heatStat /= 2; heatDmg /= 2; }   // W6c: +1 dmg graces to 0 on m1-2 like the other deltas
        }
        int enemyDelta = card.EnemyDelta + heatEnemy;
        // adaptive assist eases the force-wide enemy stat bump (base Heat only; 0 otherwise).
        int statDelta = card.StatDelta + heatStat - _run.AssistStatRelief;

        // (MissionFaction + PrepFaction were published above by Combat.BeginMission — the faction is
        // set before Mission.Build so the faction-gated spawn roster sees it; the counter-prep only
        // bites when PrepFaction == MissionFaction, and was consumed off _run right after BeginMission.)

        // reserve + connectivity-verify a key tile: the Hack terminal, or the Rescue captive's seat
        (int x, int y)? reserve = HasTerminal ? Terminal
            : (Objective == Objective.Rescue ? (Grid.W / 2, Grid.H / 2) : ((int, int)?)null);
        Grid.ClearHazards();              // wipe last mission's fire/barrels before terrain is rebuilt
        // FUL-9: publish the run seed for the arena deck (pure derivation — Mission.PickLayout
        // deals draw n of a MapSeed-keyed no-repeat deck; all five mode entries route through here)
        Mission.DeckSeed = _run != null ? _run.MapSeed : 0;
        Mission.Build(Grid, Players, Enemies, n, EvacZone, reserve,
                      enemyDelta, statDelta, HasSabotage ? SabotageSites : null, heatDmg,
                      Objective == Objective.Defend,    // FUL-4: trim the opener — waves are the force
                      // FUL-13 R2: give the hold back HALF the GRACED heat bodies (heatEnemy is
                      // already m1-2-graced above) so FUL-4's flat trim stops eating the heat
                      // ladder's EnemyDelta. Floor division is DELIBERATE: the R3 probe (ceil —
                      // h6 keep 1->2) measured NO movement at its target (pinned h6 Defend 97%,
                      // n=89, unchanged) while costing the h4 spot chunk −15 (its m2 ripple), so
                      // it was reverted — at h6 extra bodies feed the rout economy instead of
                      // pressuring the hold; it's h8's +4 stats that bite. The residual h6 cell
                      // is recorded in DEVLOG §FUL-13 with this mechanism.
                      Objective == Objective.Defend ? heatEnemy / 2 : 0);
        // PROGRAM HORIZON W2: Mission.Build laid out the arena + spawned a normal campaign force.
        // For LAST STAND we don't want that force — clear it and drop in the first horde wave (the
        // arena/terrain stays). SpawnEndlessWave uses the SpawnReinforcements machinery.
        if (Mode == GameMode.Endless) { Enemies.Clear(); SpawnEndlessWave(1); }
        // T1: same seam — throw away the campaign terrain/force and stamp the authored drill
        // arena with its fixed 4-hostile scripted force. Nothing below this line special-cases
        // the drill: it reuses the whole normal per-mission reset that follows.
        if (Mode == GameMode.Training) Mission.BuildTraining(Grid, Players, Enemies);
        if (Vip != null) { Vip.Grenades = 0; Vip.AbilityCd = 99; }  // the asset has no kit (never ready)
        if (Objective == Objective.Rescue && Vip != null)
        {
            // seat the caged captive mid-field and clear its tile + ring so soldiers can reach it.
            // W4 (SIGNAL): clear Grid.Barrel too — Tiles=Floor alone left Mission.Build's hazard
            // barrels in the ring, blocking the freeing approach (IsFloor excludes barrels) and
            // parking a chain-detonatable bomb beside the win-condition asset.
            Vip.X = Grid.W / 2; Vip.Y = Grid.H / 2;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = Vip.X + dx, ny = Vip.Y + dy;
                    if (Grid.InBounds(nx, ny) && !IsOccupiedByOther(nx, ny, Vip))
                    { Grid.Tiles[nx, ny] = TileType.Floor; Grid.Barrel[nx, ny] = false; }
                }
            Grid.ResetCoverHp();
            Vip.Mobility = 0;              // can't move while caged
            Vip.SyncPos();
        }
        if (Objective == Objective.Decapitate) DesignateHvt();
        // CROSSFIRE (Wave 2): expose the full live roster to Combat.ComputeOdds so it can see an
        // attacker's squadmates (the converging-fire bonus) without threading the list through every
        // call — the same static-state pattern as Combat.RunBoons. Rebuilt per mission here; InCrossfire
        // guards on Alive, so dead units left in the list are harmless. RefreshCombatRoster() re-snaps it
        // when the roster grows mid-mission (Defend reinforcement waves).
        RefreshCombatRoster();
        Fx.Particles.Clear();
        Fx.Texts.Clear();
        _anims.Clear();
        HitStop = 0;
        _turnCount = 1;
        RunTurns++;   // W9: the mission's OPENING turn is a real turn — SetupMission seats it without
                      // going through StartPlayerTurn, so the run-scoped counter has to book it here.
                      // Deliberately an INCREMENT, not a reset: that is the entire point (see the field).
        Pressure = 0; _pressureWaves = 0;   // anti-turtle clock resets each mission (Combat.PressureAim cleared by BeginMission)
        _autoSig = -1; _autoStall = 0;
        Phase = Phase.PlayerTurn;
        // 4.4: every mission opens with the squad concealed -- UNLESS Heat "EXPOSED" strips it, OR
        // CONTRACT "SPEARHEAD" (aggressive doctrine: open loud, no ambush). Inert as None.
        // PROGRAM HORIZON W2: LAST STAND opens LOUD — the foes are an already-engaged horde, so there
        // is no ambush window; the squad starts unconcealed and fights immediately.
        SquadConcealed = Mode != GameMode.Endless
                         && !Sightline.Heat.Exposed(_run.HeatLevel)
                         && _run.Contract != Contract.Spearhead;
        _spearheadSurgeUsed = false;          // the turn-1 action surge is fresh each mission
        // APEX W2: the caged RESCUE captive takes NO actions until freed — without this it could
        // walk itself (2 tiles/turn) toward the squad and self-trigger its own rescue while still
        // invulnerable. TryFreeCaptive re-grants via Vip.BeginTurn() the moment the cage is sprung.
        foreach (var u in Players) { u.BeginTurn(); if (u == Vip && CaptiveLocked) u.ActionsLeft = 0; }
        // CONTRACT "SPEARHEAD": a turn-1 alpha — every soldier gets +1 action on the mission's first
        // player turn (this is it: SetupMission runs once/mission and BeginTurn just seated 2 actions).
        // Gated by _spearheadSurgeUsed so it fires EXACTLY once per mission and never stacks. The VIP
        // (added to Players for Escort/Rescue) is excluded — the asset has no kit. Inert as None.
        if (_run.Contract == Contract.Spearhead && !_spearheadSurgeUsed)
        {
            _spearheadSurgeUsed = true;
            foreach (var u in Players) if (u.Alive && !u.IsVip) u.ActionsLeft += 1;
        }
        // per-mission feat tracking + status effects start clean each mission
        // (LastDotSource too — review fix: a BURN label from mission N must not mis-bucket an
        // anim-less death in mission N+2; enemies/VIP are constructed fresh each mission anyway)
        foreach (var u in Players)
        { u.FeatMultiKill = u.FeatClutch = u.FeatVengeful = u.WasNearDeath = u.FeatBurned = u.AllyDown = false; u.BondAura = false; u.ConsecutiveMisses = 0; u.Statuses.Clear(); u.LastDotSource = null;
          u.Downed = u.Stabilized = u.WasDownedThisMission = false; u.DownedTurns = 0; u.DownedByCls = null; }   // FUL-7: fresh mission, fresh down budget (mode-seam belt-and-braces)
        _missionKia.Clear();
        _bossSighted = false;        // FUL-11: the HVT SIGHTED ceremony banner re-arms per mission
        Scorches.Clear();            // death decals don't carry between missions
        _refundedThisTurn.Clear();   // flank-kill refund is per-turn; clear it for the mission's first turn too (review #2)
        _smartConcealTurns = 0;      // SmartStep: concealed-turn counter (hard anti-TIMEOUT cap)
        DeathFlash = 0;
        // per-mission bonus goal is a CAMPAIGN feature only — no secondary in LAST STAND or SKIRMISH/DAILY.
        DemoProgress = 0;              // W10: per-mission demolition tally (counted even off-secondary; cheap)
        BountyTarget = null;
        if (Mode == GameMode.Campaign) RollSecondary(n);
        else { Secondary = SecondaryKind.None; SecondaryFailed = false; }
        // W10 INTEL CACHE: an optional mid/far-field pickup, CAMPAIGN only (intel is the campaign
        // economy; LAST STAND/SKIRMISH have no requisition to spend it in). PlaceIntelCache carries
        // the PlaceBarrels-style reachability guard; a pathological board just spawns no cache.
        CachePresent = false;
        if (Mode == GameMode.Campaign)
        {
            var cache = Mission.PlaceIntelCache(Grid, Players, Enemies, EvacZone,
                                                HasTerminal ? Terminal : ((int, int)?)null,
                                                HasSabotage ? SabotageSites : null);
            if (cache != null)
            {
                CachePresent = true;
                CacheX = cache.Value.x; CacheY = cache.Value.y;
                CacheTurnsLeft = CacheTurns;
            }
        }
        foreach (var u in Enemies) { u.BeginTurn(); u.OnOverwatch = false; u.Routed = 0; }
        // UNDERTOW W3: snapshot each pod's spawn strength so BreakPodMorale can tell when a pod has
        // been chewed down to <= half and should rout its survivors (FUL-4: DEFEND waves join the
        // snapshot as pods 100+ at spawn time; pressure-clock/endless hostiles stay PodId<0, ungrouped).
        _podOrig.Clear();
        _linkedPods.Clear();   // FUL-6: no linked-alert carryover across missions
        _nextWavePod = 100;
        _waveIndex = 0;
        foreach (var e in Enemies) if (e.PodId >= 0) _podOrig[e.PodId] = _podOrig.GetValueOrDefault(e.PodId) + 1;
        Selected = Players.FirstOrDefault(p => p.CanAct);
        AimMode = false;
        SnapShot = false;
        GrenadeMode = false;
        ItemMode = false;
        ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false;
        Biome = Biome.For(n, _run.MapSeed);   // per-run biome variety (surfaces NEON/MAGMA across seeds)
        // C4 "EIGHT BIOMES ARE PAINT" — stamp this mission's GROUND layer (VERDANT undergrowth /
        // TUNDRA ice / MAGMA vents). Deliberately LAST: the arena, the force, every objective
        // fixture and the intel cache are all final by here, so `reserved` can name every tile the
        // layer must not cover. Stamped through Util.Hash3 with ZERO Util.Rng draws, so the shared
        // stream and the flywheel's CRN pairing are untouched. NOTE (C4 review): `reserved` is
        // itself derived from unit/fixture positions that Mission.Build drew from Util.Rng, so the
        // resulting BOARD is a function of (MapSeed, mission, arena, reserved) — not of
        // (MapSeed, mission) alone. Terrain.Stamp is pure; this call site is not, and that is fine:
        // zero draws is the invariant, not board-identity across ambient streams.
        StampBiomeGround(n);
        if (Mode == GameMode.Campaign)
        {
            string facTag = Combat.MissionFaction != Faction.None ? $" - {Run.FactionName(Combat.MissionFaction)}" : "";
            // FUL-11 CEREMONY: the finale opens on a card that names the HUNT, not a mission
            // number — the named boss in the headline, its kit's verb clause on the W11 sub-line,
            // in the danger colour (a threat announcement, not a turn cue). Gated on the boss
            // node's Decapitate so SKIRMISH/forced-objective m6 builds keep the plain banner.
            if (n >= Run.MaxMissions && Objective == Objective.Decapitate)
            {
                var kf = Combat.MissionFaction;
                ShowBanner($"FINALE - KILL THE {Run.FinaleBossName(kf).ToUpperInvariant()}{BiomeMechTag()}", true);
                BannerSub = Run.FinaleKitClause(kf);
            }
            else ShowBanner($"MISSION {n} - {Biome.Name}{BiomeMechTag()}{facTag}", false);
            StartTutorialMaybe();   // first-run onboarding is a campaign-only feature
        }
        else if (Mode == GameMode.Skirmish)
            ShowBanner($"{(DailyMode ? $"DAILY {DailyStamp}" : "SKIRMISH")} - {SkirmishObjectiveLabel(Objective)} - {Biome.Name}{BiomeMechTag()}", false);
        else if (Mode == GameMode.Training)
        {
            ShowBanner("TRAINING OP - LIVE-FIRE DRILL", false);
            BannerSub = "Nothing here is saved. [P] restarts the drill.";
            TrainStep = 0;                       // open lesson 1
            _trainLessonTurn = _turnCount;
            _tutMoved = _tutOver = _tutShot = _tutGrenade = _tutAbility = false;
            RevealedVerbs.Clear();
            ApplyReveal(TrainLessons[0].Reveal);
        }
        else ShowBanner($"LAST STAND - {Biome.Name}{BiomeMechTag()}", false);   // SpawnEndlessWave already banner'd WAVE 1

        // RESONANCE C1 (VOICE): re-seed the DEDICATED bark stream and clear the per-mission bark
        // budget, then compose the briefing. Voice never touches Util.Rng — see src/Voice.cs.
        Voice.BeginMission(_run?.MapSeed ?? 0, n);
        // C4 REVIEW (M4) — THE RULE HAS TO REACH EVERY MODE. `BeginBriefing` returns early for
        // anything but CAMPAIGN, so SKIRMISH, DAILY, LAST STAND and TRAINING stamp the ground layer
        // and never compose the card that states its rule. The banner tag alone names the mechanic
        // ("SLICK ICE") without saying what it does. Those modes get the sentence on the banner's
        // SUB-line instead — the surface they already have — and only when nothing else has claimed
        // it (TRAINING's "nothing here is saved" and the finale's kit clause both outrank it).
        if (Mode != GameMode.Campaign && BannerSub == null)
        {
            string modeRule = BiomeMechRule();
            if (modeRule != null) BannerSub = modeRule;
        }
        _firstBloodSeen = false;
        BeginBriefing(n);

        // checkpoint the run at the start of each mission (CAMPAIGN only). LAST STAND, SKIRMISH, and
        // the DAILY are all transient single-mode fights — never resumable, so they never write save.json.
        // W9 review fix: commit the barracks' PENDING salvage spends (scar rehab / slate re-roll)
        // immediately BEFORE the checkpoint — the sink's goods (the removed scar) become durable in
        // the very save that follows, so the charge and the goods persist together or not at all.
        if (!NoPersist && Mode == GameMode.Campaign)
        {
            CommitPendingSalvage();
            SaveGame.Save(_run);
        }

        // balance telemetry (no-op unless Stats.Enabled): record the encounter we just built.
        // W2: Mission.AppliedLayout = the authored arena the guard actually ACCEPTED (-1 procedural).
        // W1: + the two survivorship coordinates (campaign node kind, squad HP% at deploy).
        // Both are pure reads of state that already exists; no draw, no mutation.
        int hpNow = Players.Where(p => p.Alive && !p.IsVip).Sum(p => p.Hp);
        int hpMax = Players.Where(p => p.Alive && !p.IsVip).Sum(p => p.MaxHp);
        Stats.BeginMission(n, Objective.ToString(), _run.HeatLevel,
                           Players.Count(p => p.Alive && !p.IsVip), Enemies.Count(e => e.Alive),
                           Mission.AppliedLayout, Mission.AppliedDeploy,
                           Mode == GameMode.Campaign && _run.CurrentNode != null ? _run.CurrentNode.Kind.ToString() : Mode.ToString(),
                           hpMax > 0 ? (int)Math.Round(100.0 * hpNow / hpMax) : 100,
                           // W8: the DECAPITATE punch-through target's shape. -1 no HVT (any other
                           // objective), 0 an ELITE that took no buff, 1 a rank-and-file that did.
                           Hvt == null ? -1 : (HvtBuffed ? 1 : 0), Hvt == null ? 0 : Hvt.MaxHp,
                           // C4: the biome fought in + how many tiles of mechanical ground it
                           // carried, so a rung can be split by ROOM. Pure reads; no draw.
                           Biome != null ? Biome.Name : "", CountGroundTiles());
        // FUL-7: the PATCH per-presence denominator (corpsman enters via backfill only)
        if (Players.Any(p => p.Alive && !p.IsVip && p.Ability == AbilityKind.Heal))
            Stats.RecordCorpsmanFielded();
    }

    void NextMission() => SetupMission(_run.Mission + 1);

    // DECAPITATE: pick ONE spawned enemy to be the High-Value Target and buff it so it is a
    // real, distinct objective. Preference: the toughest rank-and-file (highest MaxHp) that is
    // NOT a dedicated special role (turret/medic/sapper/shield/drone make poor "punch-through"
    // targets); otherwise the first pod leader. The HVT gets +HP so it survives a few hits and
    // the player must commit to focusing it. Called only on Decapitate, after Mission.Build.
    void DesignateHvt()
    {
        var pool = Enemies.Where(e => e.Alive).ToList();
        HvtBuffed = false;
        if (pool.Count == 0) { Hvt = null; return; }
        bool IsSpecial(Unit e) => e.Cls == "TURRET" || e.Cls == "MEDIC" || e.Cls == "SAPPER"
                               || e.Cls == "SHIELD" || e.Cls == "DRONE" || e.Cls == "MORTAR";
        // prefer a non-special, toughest body; fall back to any toughest; then pod leader.
        Hvt = pool.Where(e => !IsSpecial(e)).OrderByDescending(e => e.MaxHp).ThenBy(e => e.PodId).FirstOrDefault()
           ?? pool.OrderByDescending(e => e.MaxHp).ThenBy(e => e.PodId).FirstOrDefault();
        if (Hvt == null) return;
        // buff a rank-and-file body into a worthwhile target: a meaningful HP bump + a small aim
        // edge. It is already an Enemy so every generic system (targeting/overwatch/FX/AI) treats
        // it normally. EXCEPTION: an ELITE (the WARLORD on the boss node, now a Decapitate) is
        // ALREADY a tuned boss — double-buffing it would re-create the stat-check wall we're
        // removing, so the ELITE keeps its own stats and only takes the HVT marker/name.
        // W8: the three magnitudes now live on Combat (HvtHpBonusBase/PerMission/AimBonus) so a
        // measured round can pin them; the defaults 6 / 1 / 6 are this line's original arithmetic
        // exactly. HvtBuffed records which branch ran — the balance harness needs it to tell a
        // BUFFED mid-run Decapitate apart from an EXEMPT one (see Stats.BeginMission).
        HvtBuffed = Hvt.Cls != "ELITE";
        if (HvtBuffed)
        {
            int bonus = Combat.HvtHpBonus(_run.Mission);   // scales with mission depth
            Hvt.MaxHp += bonus; Hvt.Hp += bonus;
            Hvt.Aim = Math.Min(85, Hvt.Aim + Combat.HvtAimBonus);
            Hvt.Name = "HVT-" + Hvt.Name;
        }

        // GUARDED HVT (W4): pick up to 2 nearest alive non-special bodyguards (reuse the same
        // exclusion set; never the HVT itself). While a guard lives near the HVT it takes reduced
        // damage, so killing the HVT becomes a positioning puzzle (peel the guards / GRAPPLE it out)
        // instead of a turn-1 snipe. <2 eligible enemies is fine (a 1-guard or 0-guard HVT just
        // unguards sooner — no NRE). Guards are recomputed-for-protection each boundary in UpdateHvtGuard.
        _hvtGuards.Clear();
        foreach (var u in Enemies) u.IsHvtGuard = false;   // clean slate (no stale guards from a prior mission)
        Hvt.HvtGuarded = false;
        foreach (var g in pool.Where(e => e != Hvt && e.Alive && !IsSpecial(e))
                              .OrderBy(e => Util.ChebyDist(e.X, e.Y, Hvt.X, Hvt.Y))
                              .Take(2))
        { g.IsHvtGuard = true; _hvtGuards.Add(g); }
        UpdateHvtGuard();
    }

    // GUARDED HVT (W4): recompute whether the HVT is currently protected. The HVT is GUARDED while
    // ANY of its (alive) bodyguards stands within Chebyshev Combat.HvtGuardRange of it. Mirrors how
    // FaceShields() runs each enemy turn; called at every turn boundary AND right after any death
    // (KillUnit) so killing the last in-range guard immediately exposes the HVT (no lag). Inert unless
    // HasHvt (gated), so it never touches non-Decapitate missions.
    void UpdateHvtGuard()
    {
        if (!HasHvt || !Hvt.Alive) { if (Hvt != null) Hvt.HvtGuarded = false; return; }
        Hvt.HvtGuarded = _hvtGuards.Any(g => g != null && g.Alive
                                          && Util.ChebyDist(g.X, g.Y, Hvt.X, Hvt.Y) <= Combat.HvtGuardRange);
    }

    /// Resume a saved campaign from the intro. Reloads the run and restarts its
    /// current mission from the start; returns false if there is no readable save.
    public bool ContinueRun()
    {
        var run = SaveGame.Load();
        if (run == null || run.Squad == null || run.Squad.Count == 0) return false;
        ResetModeState();     // W1 mode-seam: a resumed campaign inherits nothing from a prior mode
        EnsureMetaLoaded();   // so a resumed run that gets WON can still unlock the next Heat
        _run = run;
        _run.LossStreak = _metaLossStreak;   // adaptive assist carries across a resumed run
        // (no Players assignment here: SetupMission(n) below rebuilds Players as a fresh per-mission
        // copy — aliasing Players = _run.Squad reintroduces the VIP-duplication bug, so leave it out.)
        int n = Util.Clamp(_run.Mission < 1 ? 1 : _run.Mission, 1, Run.MaxMissions);
        _run.CurrentCard ??= Run.StandardCard(n);
        SetupMission(n);
        return true;
    }





















    // ---- secondary objective (3.9) ----
    /// Roll an optional bonus goal for the mission (none on mission 1; CLEAN SWEEP is
    /// skipped on Eliminate where it's automatic).
    void RollSecondary(int n)
    {
        SecondaryFailed = false;
        BountyTarget = null;
        if (n <= 1) { Secondary = SecondaryKind.None; return; }
        var pool = new List<SecondaryKind> { SecondaryKind.NoLosses };
        if (Objective != Objective.Defend) pool.Add(SecondaryKind.Swift);   // can't finish a hold-out early
        // CLEAN SWEEP (kill every hostile) is a mismatched / near-impossible bonus on:
        //  - Eliminate  (it's the primary objective — automatic, no challenge)
        //  - Decapitate (you win the instant the HVT dies, so clearing the rest is harder)
        //  - Defend     (waves spawn until the timer, so the board never fully clears)
        if (Objective != Objective.Eliminate && Objective != Objective.Decapitate
            && Objective != Objective.Defend) pool.Add(SecondaryKind.CleanSweep);
        // ---- W10 playstyle bonuses (gated like the exclusions above — never offer a dead bonus) ----
        // GHOST: only meaningful when the mission actually STARTS concealed (SetupMission set
        // SquadConcealed before calling us; heat's EXPOSED / SPEARHEAD strip it -> no offer).
        if (SquadConcealed) pool.Add(SecondaryKind.Ghost);
        // DEMOLITION: cover + barrels exist on every arena; player-credited destruction counts.
        pool.Add(SecondaryKind.Demolition);
        // BOUNTY: kill the mission's named SPECIALIST — pick it here at setup (prefer a non-GRUNT,
        // non-boss specialist body; never the Decapitate HVT, which is already the primary). No
        // eligible body (a tiny GRUNT-only force) -> the bonus simply isn't offered.
        var bounty = Enemies.Where(e => e.Alive && e != Hvt && e.Cls != "GRUNT" && e.Cls != "ELITE")
                            .OrderByDescending(e => e.MaxHp).ThenBy(e => e.PodId).FirstOrDefault()
                  ?? Enemies.Where(e => e.Alive && e != Hvt && e.Cls != "ELITE")
                            .OrderByDescending(e => e.MaxHp).ThenBy(e => e.PodId).FirstOrDefault();
        if (bounty != null) pool.Add(SecondaryKind.Bounty);
        Secondary = pool[Util.RandInt(0, pool.Count - 1)];
        if (Secondary == SecondaryKind.Bounty) BountyTarget = bounty;
    }

    /// BOUNTY display name: the specialist's callsign (falls back if the target evaporates).
    public string BountyName => BountyTarget != null ? BountyTarget.Name : "?";

    public string SecondaryName => Secondary switch
    {
        SecondaryKind.NoLosses  => "NO LOSSES",
        SecondaryKind.Swift     => $"SWIFT (<={SwiftTurns} turns)",
        SecondaryKind.CleanSweep => "CLEAN SWEEP",
        SecondaryKind.Ghost      => $"GHOST (hidden thru turn {GhostTurns})",
        SecondaryKind.Demolition => $"DEMOLITION (destroy {DemoRequired})",
        SecondaryKind.Bounty     => $"BOUNTY: {BountyName}",
        _ => "",
    };

    /// Compact top-bar label (shows the live turn count for SWIFT).
    public string SecondaryHud => Secondary switch
    {
        SecondaryKind.NoLosses  => "BONUS  NO LOSSES",
        SecondaryKind.Swift     => $"BONUS  SWIFT {_turnCount}/{SwiftTurns}",
        SecondaryKind.CleanSweep => "BONUS  CLEAN SWEEP",
        SecondaryKind.Ghost      => $"BONUS  GHOST {Math.Min(_turnCount, GhostTurns)}/{GhostTurns}",
        SecondaryKind.Demolition => $"BONUS  DEMO {Math.Min(DemoProgress, DemoRequired)}/{DemoRequired}",
        SecondaryKind.Bounty     => $"BONUS  BOUNTY {BountyName}",
        _ => "",
    };

    /// Live status for the HUD: is the bonus still attainable right now?
    public bool SecondaryOnTrack => Secondary switch
    {
        SecondaryKind.NoLosses  => !SecondaryFailed,
        SecondaryKind.Swift     => _turnCount <= SwiftTurns,
        SecondaryKind.CleanSweep => true,
        SecondaryKind.Ghost      => !SecondaryFailed,           // a pre-turn-3 break flips SecondaryFailed
        SecondaryKind.Demolition => true,                       // always attainable while the mission runs
        SecondaryKind.Bounty     => BountyTarget != null,       // dead target == achieved (still green)
        _ => false,
    };

    /// Final evaluation at mission end.
    bool SecondaryAchieved() => Secondary switch
    {
        SecondaryKind.NoLosses  => _missionKia.Count == 0,
        SecondaryKind.Swift     => _turnCount <= SwiftTurns,
        SecondaryKind.CleanSweep => AliveEnemies().Count == 0,
        SecondaryKind.Ghost      => !SecondaryFailed,
        SecondaryKind.Demolition => DemoProgress >= DemoRequired,
        SecondaryKind.Bounty     => BountyTarget != null && !BountyTarget.Alive,
        _ => false,
    };






    void EnterBarracks()
    {
        // APEX W2: tutorial completion fallback — the first mission ended with steps still pending
        // (e.g. the player never set overwatch), so close it out and mark it seen (NoPersist-gated
        // inside) rather than re-running the onboarding at the start of every future run.
        // W11: mark "seen" only if the player actually reached the FIRE lesson — someone who never
        // got past MOVE hasn't been onboarded; let the tutorial re-offer next run. (FUL-12: the
        // named TutStepFire constant keeps this gate on the same SEMANTIC step across renumbers.)
        // W5: a strip still PENDING behind the briefing never opened, so it can never have
        // reached FIRE — clear the arm and let the onboarding re-offer next run.
        if (TutStep >= TutStepFire) CompleteTutorial(); else { TutStep = -1; _tutPending = false; }
        // W5 SCARS: capture the just-played mission's faction BEFORE EndMission clears it, so
        // DebriefSurvivors can brand a VENDETTA grudge on a survived near-death (the faction that
        // nearly killed them). None on a mixed-force mission -> no grudge stamped (inert).
        _run.LastMissionFaction = Combat.MissionFaction;
        // TEMPO wave 4: drop every mission-scoped combat static in one call so no stale value warps a
        // barracks-phase odds read; RunBoons is refreshed to the run's current boons (a FIELD DOCTRINE
        // pick may have just changed them).
        Combat.EndMission(_run.ActiveBoons);
        // FUL-7: a WON field never abandons a breathing soldier — every downed survivor
        // (stabilized OR still ticking: the field is won) is RECOVERED before the squad rebuild:
        // back on the roster at Hp 1, Wound 3 (max — DebriefSurvivors' attrition machinery owns
        // it from here), and the near-death scar track runs (bleeding out on the field IS a
        // near-death, even off a full-HP one-shot). The triage decision stayed fully live DURING
        // the mission — the timer can beat you; Wound-3 + the scar cost keep this a real price.
        var recovered = new List<Unit>();
        foreach (var p in Players)
        {
            if (!p.Alive || !p.Downed || p.IsVip) continue;
            p.Downed = false; p.Stabilized = false; p.DownedTurns = 0;
            p.Hp = 1;
            p.Wound = 3;
            p.WasNearDeath = true;
            Stats.RecordDownRecovered();
            recovered.Add(p);
        }
        // a benched soldier sat this mission out: it's still in _run.Squad (flagged) but was
        // never in Players, so it's absent from AlivePlayers(). Preserve it across the rebuild,
        // or benching would silently destroy the veteran (review Blocker 1).
        var benched = _run.Squad.Where(u => u.Benched && u.Alive).ToList();
        _run.Squad = AlivePlayers().Where(u => !u.IsVip).ToList();  // the VIP never joins the squad
        foreach (var b in benched) if (!_run.Squad.Contains(b)) _run.Squad.Add(b);
        int survivors = _run.Squad.Count;
        bool finished = _run.Mission >= Run.MaxMissions;

        // balance telemetry: this mission was just cleared (a win). A finished run also ends here.
        Stats.EndMission(true, _turnCount, survivors, Enemies.Count(e => !e.Alive), "");
        if (finished) Stats.EndRun(true, _run.Mission, "");

        // reward for clearing the chosen deployment
        if (!finished && _run.CurrentCard != null && _run.CurrentCard.Reward == RewardKind.Heal)
            foreach (var u in _run.Squad) u.Hp = u.MaxHp;

        // DebriefSurvivors reads the just-played bench state (benched soldiers recover faster),
        // backfills the roster, then calls AutoDeploy() to set the DEFAULT deployment for next
        // mission (best healthy DeployCap; wounded benched). So Benched is now meaningful coming
        // out of the debrief — the player tunes it in the barracks; we must NOT clear it here.
        _run.DebriefSurvivors();

        // secondary objective (3.9): award bonus intel if the optional goal was met
        if (Secondary != SecondaryKind.None)
        {
            if (SecondaryAchieved())
            {
                _run.Intel += SecondaryIntel;
                Stats.RecordIntel(SecondaryIntel);   // FUL-13 intel cash-flow
                _run.Report.Insert(0, $"BONUS: {SecondaryName} cleared  (+{SecondaryIntel} intel)");
            }
            else _run.Report.Insert(0, $"Bonus missed: {SecondaryName}");
        }

        // FUL-7: name the recovered (after DebriefSurvivors — it clears Report first)
        foreach (var p in recovered)
            _run.Report.Insert(0, $"{p.Name} recovered from the field - gravely wounded");

        // surface this mission's fallen at the top of the debrief (3.11)
        foreach (var name in _missionKia) _run.Report.Insert(0, $"KIA  {name}");

        if (!finished && _run.CurrentCard != null && _run.CurrentCard.Reward == RewardKind.BonusPerk)
            _run.AddBonusPerk();

        if (finished)
        {
            // Heat/Ascension: winning a run at the current cap unlocks the next rung (META).
            UnlockHeatOnWin();
            // adaptive assist: a win clears the loss streak (the next run starts un-assisted).
            _run.RecordRunResult(true);
            // FUL-2: refresh the in-session cache too — _metaLossStreak was only ever assigned in
            // EnsureMetaLoaded, so a second run STARTED IN THE SAME SITTING inherited the stale
            // pre-win streak (and AssistPreview lied on the intro). NoPersist-gated like the save
            // so harness batches keep their zero-assist invariant.
            if (!NoPersist) { _metaLossStreak = _run.LossStreak; SaveGame.SaveMetaLossStreak(_run.LossStreak); }
            // W3 WAR ROOM: bank salvage, enshrine the victorious squad + fallen, and check achievements.
            AwardMetaRunEnd(true);
            Phase = Phase.Win; Audio.Play("win"); Audio.PlayStinger("victory"); if (!NoPersist) SaveGame.Delete();
            // VICTORY FLOURISH: a celebratory burst over the board (each surviving soldier cheers,
            // plus a centre fountain) the instant the final mission falls. The end-screen card then
            // takes over with its own confetti. Presentation only.
            Fx.VictoryBurst(BoardCenter, Pal.Good, 1.2f);
            foreach (var u in Players) if (u.Alive) Fx.VictoryConfetti(u.Pos, 14, 220f);
        }
        else
        {
            // Intel salvage is DEPTH-weighted, not survivor-weighted, so a hurting squad isn't
            // also poorer (the old 3*survivors term was a rich-get-richer / death-spiral loop —
            // fewer survivors meant less intel meant a weaker squad). A small survivor bonus
            // remains as a reward for keeping people alive, but depth (which tracks the rising
            // difficulty) is the main driver so the reward sink keeps pace with the gate.
            // ONSLAUGHT pays a risk premium; higher Heat pays a flat per-mission bonus.
            // Routing economy: the cleared node carries its own Intel reward (SUPPLY/ELITE pay
            // premiums -- the route is a trade-off). Falls back to the old flat depth term when no
            // map node is current (legacy deploy-card path / harness). A small survivor bonus stays
            // as a reward for keeping people alive.
            var clearedNode = _run.CurrentNode;
            int nodeIntel = clearedNode != null ? clearedNode.Intel : (10 + 4 * _run.Mission);
            int gained = nodeIntel + survivors;
            if (_run.CurrentCard != null && _run.CurrentCard.ModName == "ONSLAUGHT") gained += 6;
            int heatBonus = Sightline.Heat.IntelBonus(_run.HeatLevel);
            gained += heatBonus;
            // CONTRACT "HIGH STAKES": +50% mission Intel (the reward side of "no field-heal" above).
            // Applied to the whole grant. Inert as None.
            string stakesNote = "";
            if (_run.Contract == Contract.HighStakes)
            {
                int bonus = gained / 2;       // +50%, integer (floor) so it's deterministic
                gained += bonus;
                stakesNote = $"  (+{bonus} STAKES)";
            }
            _run.Intel += gained;
            Stats.RecordIntel(gained, heatBonus);   // FUL-13 intel cash-flow (+ the heat-bonus share)
            string heatNote = heatBonus > 0 ? $"  (+{heatBonus} HEAT {_run.HeatLevel})" : "";
            heatNote += stakesNote;
            _run.Report.Insert(0, $"Recovered {gained} intel{heatNote}  (total {_run.Intel})");
            _shopDone = false;
            _shopReroll = 0;                            // W9: paid slate re-rolls are per-barracks
            // W9: cached bank for the sink UI — the AVAILABLE bank (disk minus any uncommitted
            // pending, which is normally 0 here: the previous SetupMission committed it).
            BarracksSalvage = NoPersist ? 0 : Math.Max(0, SaveGame.LoadSalvage() - PendingSalvage);
            RefreshShopOffer();                         // roll this barracks' rotating requisition slate
            ArmoryMode = false; ArmorySoldier = null;   // open requisition in the shop view, not armory
            _run.GenerateOffers(_run.Mission + 1);
            _run.GenerateBoonOffer();                // offer a run-scoped boon pick this barracks
            Phase = Phase.Barracks;
            Audio.Play("win");
        }
    }

    // Heat/Ascension unlock: a run cleared AT the current max raises the cap by one (capped at
    // the ladder ceiling). The increment + persistence are gated by !NoPersist, so an autoplay
    // win never mutates/writes the meta (the harness never loaded it). Safe to call always.
    void UnlockHeatOnWin()
    {
        if (NoPersist) return;
        if (_run.HeatLevel >= UnlockedHeat && UnlockedHeat < Sightline.Heat.Max)
        {
            UnlockedHeat++;
            SaveGame.SaveMetaHeat(UnlockedHeat);
            _run.Report.Insert(0, $"HEAT {UnlockedHeat} UNLOCKED");
            EndHeatUnlocked = UnlockedHeat;   // FUL-12: the end card reads the field, not the report
        }
    }

    // ── W5 THE DOORS (audit wildcard-3) ───────────────────────────────────────────────────────
    /// Set when the player has confirmed QUIT TO DESKTOP; Program's frame loop breaks on it.
    /// The quit path is deliberately INERT with respect to persistence: it writes nothing, deletes
    /// nothing, and touches neither save.json nor meta.json. The campaign checkpoint is written at
    /// MISSION START (SetupMission), so quitting mid-mission resumes that mission from its start —
    /// which is exactly what the confirm text says out loud. SIGHTLINE_QUITTEST pins all of it.
    public bool QuitRequested;
    /// One-click-arms, two-clicks-quits. Disarmed by any other pause interaction and by closing the
    /// pause card, so a stray click can never take the window down mid-fight.
    public bool QuitArmed;

    /// The QUIT verb. Arms on the first press, quits on the second.
    public void RequestQuit()
    {
        if (!QuitArmed) { QuitArmed = true; Audio.Play("select"); return; }
        QuitRequested = true;
    }

    // run-over screen text (set by LoseRun so the cause reads accurately)
    public string LoseTitle = "RUN OVER";
    public string LoseReason = "";

    // FUL-12 SIGNPOSTS — end-card meta payoff, piped through FIELDS (never parsed back out of the
    // Report strings). Set only inside the !NoPersist meta award path (AwardMetaRunEnd /
    // UnlockHeatOnWin / TryAchievement / AwardMetaEndless), so every harness end card stays
    // byte-stable (fields sit at defaults there); reset per mode entry in ResetModeState.
    public int EndSalvage;                                  // salvage banked at run end (0 = no slab)
    // W5 THE DOORS: how many survivors NEWLY joined the persistent reserve at run end. Measured as
    // the DELTA of SaveGame.VeteranCount() across EnshrineVeterans — never as vets.Count — so the
    // end card structurally cannot over-claim: a survivor who was already a reserve record does not
    // "join" twice, and the MERCENARY CLAUSE run (which enshrines nobody) reads 0 without a special
    // case. SIGHTLINE_METATEST pins that identity.
    public int EndReserve;
    public int EndHeatUnlocked;                             // freshly-opened heat rung (0 = no line)
    public readonly List<string> EndAchievements = new();   // display names of NEW unlocks this run-end

    // W11 HONEST LOSSES — which enemy archetype is killing this run's soldiers. Always-on (a
    // Dictionary bump costs nothing), bumped in KillUnit, read by the lose card's CAUSE OF DEATH
    // line. Lives on GAME, not Run: it must survive the per-mission Run.Squad rebuilds but reset
    // per run, and ResetModeState is the one choke-point every mode entry passes through. Keyed
    // by archetype string (Unit.Cls); "?" buckets source-less deaths (DoT / environment / friendly).
    public readonly Dictionary<string, int> DeathsByClass = new();

    // W11 NEW CONTACT — archetypes already ID'd this run (banner fires once per archetype per run).
    readonly HashSet<string> _seenArchetypes = new();
    // FUL-11 — the finale boss's first-sighting ceremony banner already fired this mission.
    bool _bossSighted;

    /// End the run as a loss and clear the checkpoint so the intro stops offering CONTINUE.
    void LoseRun(string title, string reason)
    {
        // APEX W2: tutorial completion fallback (mirror of EnterBarracks) — a first-mission loss
        // still counts as "the onboarding ran"; don't re-show it forever. NoPersist-gated inside.
        // W11: same reached-the-FIRE-lesson gate as EnterBarracks — an early washout re-offers.
        // W5: a strip still PENDING behind the briefing never opened, so it can never have
        // reached FIRE — clear the arm and let the onboarding re-offer next run.
        if (TutStep >= TutStepFire) CompleteTutorial(); else { TutStep = -1; _tutPending = false; }
        Combat.EndRun();   // TEMPO wave 4: clear every mission-scoped combat static (+ run boons) on run end
        LoseTitle = title;
        LoseReason = reason;
        Phase = Phase.Lose;
        Audio.Play("lose");
        // a full squad wipe gets the heavier ominous wipe stinger; other run-enders (VIP lost,
        // abandoned) get the standard sinking-minor lose stinger.
        Audio.PlayStinger(AlivePlayers().Count(p => !p.IsVip) == 0 ? "squadwipe" : "lose");
        // balance telemetry: the active mission AND the run end here as a loss.
        Stats.EndMission(false, _turnCount, AlivePlayers().Count(p => !p.IsVip),
                         Enemies.Count(e => !e.Alive), title);
        Stats.EndRun(false, _run.Mission - 1, title);
        // adaptive assist: a lost run grows the streak, so a persistently-stuck player gets a
        // small, capped, reversible easing on their NEXT base-Heat run (Hades God-Mode).
        _run.RecordRunResult(false);
        // FUL-2: refresh the in-session cache (see the win path) — without it a same-sitting next
        // run read the PRE-loss streak and under-assisted exactly the player the assist targets.
        if (!NoPersist) { _metaLossStreak = _run.LossStreak; SaveGame.SaveMetaLossStreak(_run.LossStreak); SaveGame.Delete(); }
        // W3 WAR ROOM: bank consolation salvage, enshrine the fallen, and check the DEEP achievement.
        AwardMetaRunEnd(false);
    }

    // ---- PROGRAM HORIZON W3 (WAR ROOM): cross-run meta award/record ----
    // Bank salvage, populate the HALL OF FAME (Legends), record lifetime totals, and unlock any
    // freshly-earned achievements (each grants a one-time salvage bounty). STRICTLY gated behind
    // !NoPersist — the flywheel/harness never touch meta, so balance/screenshots stay byte-stable.
    void AwardMetaRunEnd(bool win)
    {
        if (NoPersist || _run == null) return;
        int heat = _run.HeatLevel;
        int missions = win ? _run.Mission : Math.Max(0, _run.Mission - 1);

        // 1) SALVAGE bounty. W9: heat MULTIPLIES the win bounty (+10% per heat rung) instead of the
        // old flat +5h — a heat-8 clear now pays 1.8x the h0 income, so pushing the ladder is what
        // funds the standing economy. h0 is UNCHANGED at 25+6m (~61 for a full clear). The loss
        // consolation stays additive.
        int salvage = win ? (25 + 6 * _run.Mission) * (10 + heat) / 10 : (4 * Math.Max(0, _run.Mission - 1) + 2 * heat);
        // FUL-10: field-event salvage claims were EARNED mid-run (events never touch meta directly —
        // they pend on the run); pay them win OR loss, folded into the same bounty + end-card slab.
        if (_run.PendingSalvageReward > 0)
        {
            salvage += _run.PendingSalvageReward;
            _run.Report.Insert(0, $"EVENT SALVAGE CLAIMS +{_run.PendingSalvageReward}");
            _run.PendingSalvageReward = 0;   // committed — never bankable twice
        }
        // FUL-10 LGD: surviving Rank>=2 soldiers pension out at +6/rank each — the reserve pays income
        // to balance the mortality below. Inert at Contract.None.
        if (_run.Contract == Contract.LivingLegends)
        {
            int pension = 0;
            foreach (var u in _run.Squad) if (u.Alive && !u.IsVip && u.Rank >= 2) pension += MetaProg.LegendPension * u.Rank;
            if (pension > 0) { salvage += pension; _run.Report.Insert(0, $"LIVING LEGENDS pensions +{pension}"); }
        }
        if (salvage > 0) { SaveGame.AddSalvage(salvage); _run.Report.Insert(0, $"SALVAGE +{salvage}"); EndSalvage = salvage; }   // FUL-12: field feeds the end-card slab

        // 2) HALL OF FAME — surviving squad (won runs) as legends, plus this run's fallen (KIA).
        var legends = new List<SaveGame.LegendDto>();
        if (win)
            foreach (var u in _run.Squad)
                if (u.Alive && !u.IsVip)
                    legends.Add(new SaveGame.LegendDto { Name = u.FullName, Cls = u.Cls, Rank = u.RankName, Kills = u.Kills, Heat = heat, Won = true });
        foreach (var f in _run.Memorial)
            legends.Add(new SaveGame.LegendDto { Name = f.Name, Cls = f.Cls, Rank = f.Rank, Kills = f.Kills, Heat = heat, Won = false });
        if (legends.Count > 0) SaveGame.AddLegends(legends);

        // 3) lifetime totals
        SaveGame.RecordRunTotals(win, missions);

        // 3b) VETERAN reserve (COUNTERPLAY): promoted survivors of this run retire into the persistent
        // reserve, recallable by a future run's draft carrying their rank/perks/traits/spec/scars. Applies
        // on a WIN or a survivable loss (a squad wipe leaves no survivors -> enshrines nobody). A Rank>=1
        // (promoted-at-least-once) gate keeps green rookies out so the reserve stays a roster of legends.
        var vets = _run.Squad.Where(u => u.Alive && !u.IsVip && u.Rank >= 1).ToList();
        // FUL-10 MRC: cheap recalls now, no pipeline later — this run's survivors never enshrine.
        // W5: measure the reserve DELTA around the enshrine (before LGD's RemoveVeterans below can
        // touch it) so the end card's "N JOIN THE RESERVE" line is the count that actually landed
        // on disk — a name already in the reserve updates its record and does not re-join.
        int vetsBefore = SaveGame.VeteranCount();
        if (vets.Count > 0 && _run.Contract != Contract.MercenaryClause) SaveGame.EnshrineVeterans(vets);
        EndReserve = Math.Max(0, SaveGame.VeteranCount() - vetsBefore);
        // FUL-10 LGD: a KIA whose name matches a reserve record ERASES it — veterans are mortal
        // across runs, not just priced. Name-keyed exactly like EnshrineVeterans' dedupe.
        if (_run.Contract == Contract.LivingLegends && _run.Fallen.Count > 0)
        {
            int gone = SaveGame.RemoveVeterans(_run.Fallen);
            if (gone > 0) _run.Report.Insert(0, $"LIVING LEGENDS: {gone} reserve record{(gone > 1 ? "s" : "")} died with the fallen");
        }

        // 4) ACHIEVEMENTS (each a one-time salvage bounty on first unlock)
        if (win)
        {
            TryAchievement("FIRST_WIN");
            if (heat >= 3) TryAchievement("HEAT3");
            if (heat >= 6) TryAchievement("HEAT6");
            if (_run.Memorial.Count == 0) TryAchievement("FLAWLESS");   // no soldier lost all run
        }
        if (_run.Mission >= Run.MaxMissions) TryAchievement("DEEP");     // reached mission 6 (win or loss)
    }

    /// Try to unlock an achievement; on a NEW unlock, bank the bounty + a report line.
    void TryAchievement(string id)
    {
        if (NoPersist) return;
        if (SaveGame.UnlockAchievement(id))
        {
            SaveGame.AddSalvage(MetaProg.AchievementSalvage);
            _run.Report.Insert(0, $"ACHIEVEMENT: {MetaProg.AchievementName(id)}  (+{MetaProg.AchievementSalvage} salvage)");
            EndAchievements.Add(MetaProg.AchievementName(id));   // FUL-12: end-card line (display name)
        }
    }

    void ShowBanner(string text, bool enemy)
    {
        BannerText = text; BannerEnemy = enemy; BannerSub = null;
        BannerMax = BannerTimer = 1.2f;
        Audio.Play("turn");
    }

    // ---------------- W11 NEW CONTACT (teach the roster where it's played) ----------------
    /// First sighting of an enemy archetype this run: banner its callsign + the bestiary ID clause.
    /// "Sighting" = the unit is alive AND alert (Active) — dormant pods aren't a contact yet, so the
    /// banner lands exactly when the threat becomes real. One archetype per banner window (the next
    /// unseen one fires after this banner fades), so a multi-archetype wake never strobes the screen.
    /// Interactive-only (!NoPersist), mirroring the tutorial, so autoplay/shots stay byte-stable;
    /// SIGHTLINE_NEWCONTACT=1 forces it under NoPersist for the screenshot harness.
    void CheckNewContact()
    {
        if (NoPersist && Environment.GetEnvironmentVariable("SIGHTLINE_NEWCONTACT") != "1") return;
        // FUL-11 CEREMONY — the finale boss's first sighting outranks the generic bestiary ID:
        // a one-shot HVT SIGHTED card on the same lane/window, naming the target + its kit's
        // verb clause. Consumes the archetype's NEW CONTACT slot too, so the SAME unit can't
        // double-banner as a generic contact one window later.
        if (!_bossSighted)
            foreach (var e in Enemies)
            {
                if (!e.Alive || !e.Active || !e.IsBoss) continue;
                _bossSighted = true;
                _seenArchetypes.Add(e.Cls);
                ShowBanner($"HVT SIGHTED: {e.Name}", true);
                BannerSub = Run.FinaleKitClause(Combat.MissionFaction);
                return;
            }
        foreach (var e in Enemies)
        {
            if (!e.Alive || !e.Active || _seenArchetypes.Contains(e.Cls)) continue;
            _seenArchetypes.Add(e.Cls);
            string blurb = Codex.BlurbClause(e.Cls);
            if (string.IsNullOrEmpty(blurb)) continue;   // unknown archetype: no half-empty banner
            ShowBanner($"NEW CONTACT: {Codex.NameFor(e.Cls)}", true);
            BannerSub = $"{e.Cls} — {blurb}";
            return;
        }
    }

    // ---------------- queries ----------------
    public List<Unit> AlivePlayers() => Players.Where(u => u.Alive).ToList();
    public List<Unit> AliveEnemies() => Enemies.Where(u => u.Alive).ToList();

    public bool IsPlayerInteractive() => Phase == Phase.PlayerTurn && _anims.Count == 0;

    /// Is a run-scoped boon active? (Public so Anim.cs's shot path can read VENOM etc.)
    public bool HasBoon(Boon b) => _run != null && _run.HasBoon(b);

    public bool IsOccupiedByOther(int x, int y, Unit except)
    {
        foreach (var u in Players) if (u.Alive && u != except && u.X == x && u.Y == y) return true;
        foreach (var u in Enemies) if (u.Alive && u != except && u.X == x && u.Y == y) return true;
        return false;
    }

    public Unit UnitAt(int x, int y)
    {
        foreach (var u in Players) if (u.Alive && u.X == x && u.Y == y) return u;
        foreach (var u in Enemies) if (u.Alive && u.X == x && u.Y == y) return u;
        return null;
    }

    public bool CanTarget(Unit a, Unit d)
    {
        if (a == null || d == null || !a.Alive || !d.Alive || a.Ammo <= 0) return false;
        if (a.Team == d.Team) return false;
        if (d == Vip && CaptiveLocked) return false;   // the caged captive is invulnerable until freed
        if (Util.TileDist(a.X, a.Y, d.X, d.Y) > a.Weapon.MaxRange) return false;
        // a commanding 2-tier height advantage lets the shooter see over high cover
        bool commanding = Grid.HeightAt(a.X, a.Y) - Grid.HeightAt(d.X, d.Y) >= 2;
        return Grid.HasLineOfSight(a.X, a.Y, d.X, d.Y, commanding);
    }

    public bool HasAnyTarget(Unit u)
    {
        var foes = u.Team == Team.Player ? Enemies : Players;
        return foes.Any(f => f.Alive && CanTarget(u, f));
    }

    Unit FirstTargetFor(Unit u)
    {
        var foes = (u.Team == Team.Player ? Enemies : Players).Where(f => f.Alive && CanTarget(u, f));
        return foes.OrderBy(f => Util.TileDist(u.X, u.Y, f.X, f.Y)).FirstOrDefault();
    }

    // Note: OnStart runs when the anim becomes active (see the advance loop), NOT at
    // enqueue time — otherwise every queued step of a multi-tile path captures its
    // _from at the original tile and the unit snaps back to the start each step.
    void Enqueue(Anim a, Team owner) { AnimOwner = owner; _anims.Add(a); }

    // ---------------- combat events ----------------
    public void OnUnitEnteredTile(Unit mover)
    {
        if (mover.FiredThisTurn) mover.MovedAfterFire = true;   // HORIZON: any tile entry after firing clears exposed-by-fire
        mover.MovedThisTurn = true;   // W10 BIPOD: ANY tile entry (walk/vault/drag/shove/grapple) disarms the planted bonus
        if (!mover.Alive) return;
        if (mover.HasStatus(StatusKind.Bleed))   // bleeding worsens with every step
        {
            EnvDamage(mover, Unit.BleedDamage, "BLEED", Pal.RGBA(205, 45, 45));
            if (!mover.Alive) return;
        }
        // Stepping onto hot ground sears + ignites. C4 REVIEW FIX — the vent branch used to be a
        // second unconditional `if`, so a tile that was BOTH on fire and a vent (a grenade or a
        // barrel can light a vent tile: LightFire only requires Floor, and a vent IS floor) charged
        // Unit.BurnDamage TWICE in one tile entry. One sear per step, fire named first because it
        // is the transient one. The C4 balance round was measured WITH the double charge, so its
        // published cost is an upper bound on the layer's; the incidence is tiny (it needs fire and
        // a vent on the same tile) and the fix only ever reduces damage.
        bool onFire = Grid.IsFire(mover.X, mover.Y);
        bool onVent = Grid.IsVent(mover.X, mover.Y);
        if (onFire || onVent)
        {
            // C4 / MAGMA — the second half of the fissure's toll. CostMap already charged the
            // MOVEMENT half (Terrain.VentStepExtra), which is why both the AI and the player's own
            // path route around a vent unless the crossing is genuinely worth it; this is the HP
            // half. Symmetric by construction (no Team read), and it reuses the sear+ignite the
            // fire hazard has used since 3.6 rather than inventing a second burn.
            mover.AddStatus(StatusKind.Burning, onFire ? 2 : Terrain.VentBurnTurns);
            EnvDamage(mover, Unit.BurnDamage, onFire ? "BURN" : "VENT", Pal.RGBA(255, 140, 40));
            if (!mover.Alive) return;
        }
        if (mover.Team == Team.Player)
        {
            // 4.4: stepping within RevealRange of an already-active foe blows concealment.
            // No actor - getting spotted is not your aimed shot, so no ambush bonus.
            // GHOST boon: the squad moves unseen — proximity never breaks stealth (only an aggressive
            // action does), so a GHOST run can reposition right up to a foe before springing the ambush.
            if (SquadConcealed && !(_run != null && _run.HasBoon(Boon.Ghost))
                    && Enemies.Any(e => e.Alive && e.Active
                    && Util.TileDist(mover.X, mover.Y, e.X, e.Y) <= RevealRange))
                BreakConcealment();
            CheckPodActivation();  // reveal pods while advancing (no-op while still concealed)
            // W10 INTEL CACHE: a soldier stepping onto the cache tile banks it (any tile entry —
            // walking through collects too; the detour is the cost, not pixel-perfect stopping).
            if (CachePresent && !mover.IsVip && mover.X == CacheX && mover.Y == CacheY)
                CollectIntelCache(mover);
        }
        // FUL-7: a DOWNED body being DRAGGED/EXTRACTed is a tile entry, but never a reaction
        // target — enemies do not direct-fire the downed (the same rule as Ai.Plan's filter; the
        // hazard checks above still ran, so hauling a body THROUGH fire still kills it — honest).
        if (mover.Downed) return;
        // RANGER SLIPSTREAM: a free, overwatch-immune reposition. While slipstreaming the mover draws no
        // reaction fire; the flag is consumed when its destination tile is reached so the move ends silent.
        if (mover.Slipstreaming)
        {
            if (_slipDest != null && mover.X == _slipDest.Value.x && mover.Y == _slipDest.Value.y)
            { mover.Slipstreaming = false; _slipDest = null; }
            return;   // skip overwatch entirely for this step (the whole point of the slipstream)
        }
        // OUTRUNNER (perk Sprinter, reworked): this soldier slips past reaction fire — moving never
        // draws OVERWATCH (like a permanent, passive slipstream). A mobility verb for a flanker who has
        // to cross open lanes; concealment-break + pod checks above still run (only the watcher loop is
        // skipped). Single source of truth for the gate is Combat.IgnoresOverwatch (also COMBATTEST'd).
        if (Combat.IgnoresOverwatch(mover)) return;
        var watchers = mover.Team == Team.Player ? Enemies : Players;
        int insertAt = 1;
        // APEX W2: predicted mover HP across the reactions queued by THIS tile entry. Each queued
        // CONNECT (res.Hit covers full hits AND grazes; damage read AFTER the brace halving below)
        // decrements it; once it reaches 0 the mover is corpse-bound, so the loop stops BEFORE a
        // later watcher spends its OnOverwatch/ReactedThisTurn/Ammo on a shot KillUnit's purge would
        // only throw away (the old per-shot check missed cumulative lethality, silently taxing the
        // third-plus watcher). The KillUnit purge stays as the backstop for staleness between steps.
        int predHp = mover.Hp;
        foreach (var w in watchers)
        {
            if (!w.Alive || !w.OnOverwatch || w.ReactedThisTurn || w.Ammo <= 0) continue;
            if (!CanTarget(w, mover)) continue;
            // COUNTERPLAY: a FOCUSED watcher only reacts to movers inside its braced cone (blind outside).
            if (w.OwFocused && !InOwCone(w, mover.X, mover.Y)) continue;
            // 4.4 (review M1): a player's overwatch shot is still a shot — it reveals the
            // squad. No actor -> no ambush bonus on a reaction (it already has its own mod).
            // W10 SUPPRESSOR: the reaction is a firing site too — thread the mover through as the
            // shot's target so a suppressed watcher's break wakes only the mover's own pod.
            if (w.Team == Team.Player && SquadConcealed)
                BreakConcealment(target: mover, suppressed: w.HasMod(WeaponMod.Suppressor));
            w.OnOverwatch = false;
            w.ReactedThisTurn = true;
            w.Ammo--;
            // C2: an enemy lane that actually paid off (see Stats.RecordEnemyReaction).
            if (w.Team == Team.Enemy) Stats.RecordEnemyReaction();
            // overwatch reaction aim: base -10; Reflexes makes it near-certain, Guardian adds a
            // precision bump. ADDITIVE (not a ternary) so a soldier with BOTH gets both (review
            // S7: the old ternary silently discarded Guardian whenever Reflexes was also held).
            int reactMod = -10 + (w.HasPerk(Perk.Reflexes) ? 75 : 0) + (w.HasPerk(Perk.Guardian) ? Unit.GuardianAim : 0)
                              + (w.OwFocused ? Combat.FocusOwAim : 0);   // COUNTERPLAY: braced kill-lane aim
            var res = Combat.Resolve(Grid, w, mover, reactMod);
            // UNDERTOW W2 — BRACE: a disrupting reaction. It STAGGERS on a hit (ShotAnim.Apply zeroes the
            // mover's remaining actions) but deals reduced damage + never crits, so it's a real trade vs a
            // lethal overwatch (deny tempo instead of going for the kill), not a strict upgrade.
            // W10 SHOCK DOCTRINE boon: the halving/no-crit trade is waived — a braced reaction deals
            // FULL damage AND staggers (Combat.BraceFullDamage; the stagger flag below is unchanged).
            bool brace = w.OwBrace;
            if (brace && res.Hit && !Combat.BraceFullDamage(w)) { res.Damage = Math.Max(1, res.Damage / 2); res.Crit = false; }
            // FUL-1 PROC: the boon actually waived the halving on a landed brace (no-op unless Stats.Enabled)
            if (brace && res.Hit && Combat.BraceFullDamage(w)) Stats.RecordProc("SHK");
            Fx.PopText(w.Pos + new Vector2(0, -30), brace ? "BRACE" : "OVERWATCH", brace ? Pal.Good : Pal.Accent, 18f);
            Audio.Play("over");
            var shot = new ShotAnim(w, mover, res, reaction: true) { Stagger = brace };
            // OnStart runs when this reaction becomes the active anim (Started is false),
            // by which point the mover has settled on the reacted-to tile.
            _anims.Insert(Math.Min(insertAt, _anims.Count), shot);
            insertAt++;
            if (res.Hit) predHp -= res.Damage;
            if (predHp <= 0) break;   // predicted dead: stop before another watcher spends its reaction
        }
    }

    /// Heavy weapons (LMG anywhere, shotgun point-blank) chew through a hit target's
    /// frontal cover, degrading High->Low->gone (3.6).
    public void TryChipCover(Unit shooter, Unit target)
    {
        var w = shooter.Weapon.Kind;
        bool heavy = w == WeaponKind.Lmg
                     || (w == WeaponKind.Shotgun && Util.TileDist(shooter.X, shooter.Y, target.X, target.Y) <= 2);
        if (!heavy) return;
        if (Grid.GetCover(target.X, target.Y, shooter.X, shooter.Y).Level <= 0) return;
        var tile = Grid.CoverTile(target.X, target.Y, shooter.X, shooter.Y);
        if (tile == null) return;
        var hit = Grid.DamageCover(tile.Value.x, tile.Value.y, 1);
        if (hit != Grid.CoverHit.None) CoverHitFx(tile.Value.x, tile.Value.y, hit);
        // W10 DEMOLITION secondary: a player's heavy fire finishing a cover tile counts.
        if (hit == Grid.CoverHit.Destroyed && shooter.Team == Team.Player) DemoProgress++;
    }

    /// Feedback for a cover tile taking damage / degrading.
    public void CoverHitFx(int x, int y, Grid.CoverHit hit)
    {
        var c = Util.TileCenter(x, y);
        switch (hit)
        {
            case Grid.CoverHit.Chipped:
                Fx.Burst(c, Pal.RGBA(150, 160, 175), 7, 130f, 0.4f, 3f);
                break;
            case Grid.CoverHit.Downgraded:
                Fx.Burst(c, Pal.RGBA(150, 160, 175), 15, 190f, 0.5f, 4f);
                Fx.PopText(c + new Vector2(0, -20), "COVER CRACKED", Pal.TxtDim, 16f);
                Audio.Play("hit");
                break;
            case Grid.CoverHit.Destroyed:
                Fx.Burst(c, Pal.RGBA(120, 130, 145), 18, 220f, 0.6f, 4.5f);
                Fx.PopText(c + new Vector2(0, -20), "COVER DOWN", Pal.TxtDim, 16f);
                Audio.Play("hit");
                break;
        }
    }

    public void KillUnit(Unit d)
    {
        // IDEMPOTENT: a death is processed exactly once. A surplus blow reaching an already-dead
        // unit (e.g. a 3rd non-lethal overwatch reaction resolving on the corpse, or an AoE that
        // overlaps a body) must NOT re-run the kill — doing so double-counted _run.Fallen/Memorial,
        // Stats.RecordKill, CreditKill and replayed the death FX, corrupting the class-lethality
        // telemetry the flywheel ranks (UNDERTOW W1). The queued-reaction purge below is the primary
        // guard; this makes KillUnit robust to every double-call path.
        if (!d.Alive) return;
        // FUL-7 LAST LIGHT: the whole bleed-out state machine enters HERE — the single lethal
        // seam (ShotAnim/GrenadeAnim/EnvDamage/siege/barrel all funnel through KillUnit). A
        // soldier's FIRST lethal event becomes a 3-turn DOWN instead of a death; the VIP/captive
        // keeps instant death (Escort's VIP-loss + the DEATHTEST-pinned solo-win semantics),
        // enemies never go down (morale/rout is their drama), and the second lethal event on a
        // soldier this mission — including ANY damage reaching a body already down (AoE/fire:
        // the telegraphed-weapons honesty valve) — falls through and kills outright.
        if (CanGoDown(d)) { EnterDowned(d); return; }
        // SIEGE interrupt: killing a charging artillery piece cancels its strike (the zone reads
        // off live enemies, so it clears automatically; this is a cosmetic confirmation of the
        // interrupt). W5: HasSiege flag (mirrors Cls=="BOMBARD"; also covers a siege-armed boss).
        if (d.HasSiege && d.ChargeTurns > 0)
            Fx.PopText(d.Pos + new Vector2(0, -34), "STRIKE ABORTED", Pal.Good, 16f);
        d.Alive = false;
        d.Hp = 0;
        // balance telemetry (no-op unless Stats.Enabled): attribute this kill to the unit
        // whose shot/grenade is the active anim (the blow that caused this death). Covers
        // both directions (player kills enemies; an enemy's shot kills a soldier) so the
        // report can rank player-class lethality AND which enemy class kills soldiers.
        // A source-less death (DoT/environment, e.g. a burn tick on an idle queue) has no
        // anim attacker → killer is null; we tag it killerTeam=1 so a soldier killed by DoT
        // still buckets sensibly and an enemy killed by DoT is simply not credited to a
        // player class (a minor per-class undercount; the EnemiesKilled total is reconciled
        // by Stats.EndMission's dead-enemy count, so aggregate kill counts stay accurate).
        // W2: the anim-less fallback now reads Unit.LastDotSource (set by EnvDamage / the
        // barrel blast) so the threat ranking shows BURN/BLEED/STRIKE/BARREL instead of "?".
        Unit killer = ActiveAnim switch { ShotAnim sa => sa.A, GrenadeAnim ga => ga.Thrower, _ => null };
        Stats.RecordKill(killer?.Cls ?? (string.IsNullOrEmpty(d.LastDotSource) ? "?" : d.LastDotSource),
                         killer != null ? (int)killer.Team : 1, d.Cls, (int)d.Team);
        // W10 RECLAIMER boon: a kill INSIDE a focused-overwatch cone re-arms the watcher's reaction
        // (OnOverwatch + ReactedThisTurn reset; the spent bullet stays spent). The kill context is
        // the active REACTION ShotAnim — the watcher's OwFocused/OwDir survive the arming (only
        // OnOverwatch was dropped when the reaction queued in OnUnitEnteredTile), so InOwCone still
        // describes the braced lane. Makes a focused kill-lane a genuine mow-them-down build.
        if (ActiveAnim is ShotAnim rsa && rsa.Reaction && rsa.A != null && rsa.A.Alive
            && rsa.A.Team == Team.Player && d.Team == Team.Enemy
            && rsa.A.OwFocused && InOwCone(rsa.A, d.X, d.Y)
            && HasBoon(Boon.Reclaimer) && !rsa.A.OnOverwatch && rsa.A.Ammo > 0)
        {
            rsa.A.OnOverwatch = true;
            rsa.A.ReactedThisTurn = false;
            Stats.RecordProc("RCL");   // FUL-1 PROC: a cone kill actually re-armed the watch
            Fx.PopText(rsa.A.Pos + new Vector2(0, -30), "RECLAIMED", Pal.Accent, 17f);
            Fx.Flash(rsa.A.Pos, Pal.Accent, 18f, 0.14f, 0.45f);
        }
        // W11 HONEST LOSSES: tally which enemy archetype killed this soldier (always-on; the lose
        // card's CAUSE OF DEATH line reads it). Source-less / friendly-fire deaths bucket under "?"
        // (DoT labels like BURN stay out of this dict — the lose card resolves causes through the
        // codex bestiary, which only knows archetypes).
        if (d.Team == Team.Player && !d.IsVip)
        {
            string cause = killer != null && killer.Team == Team.Enemy ? killer.Cls : "?";
            // FUL-7: a bleed-out KIA names the DOWNING archetype (the honest loss card resolves
            // causes through the bestiary). A DoT-caused down carries a BURN/BLEED label, not an
            // archetype — that buckets "?" exactly as DoT deaths do today (BlurbFor gates it).
            if (cause == "?" && d.Downed && !string.IsNullOrEmpty(d.DownedByCls)
                && !string.IsNullOrEmpty(Codex.BlurbFor(d.DownedByCls)))
                cause = d.DownedByCls;
            DeathsByClass[cause] = DeathsByClass.GetValueOrDefault(cause) + 1;
        }
        // FUL-7 (review F2): a body killed while DOWN is a DEATH, not a save — close the down
        // state on the corpse HERE (after the cause read above). Without this, a blast/fire
        // FINISH left Downed true on a corpse: the save-rate ledger counted it as saved, and a
        // same-turn DoT tick could reach the countdown on a body that was already dead and pop
        // "BLED OUT" over a burned corpse. A bleed-out expiry is counted at ExpireDowned's own
        // site (_expiringDown distinguishes it from a finish).
        if (d.Team == Team.Player && d.Downed)
        {
            if (!_expiringDown) Stats.RecordDownFinished();
            d.Downed = false; d.Stabilized = false; d.DownedTurns = 0;
        }
        if (d.Team == Team.Player)
        {
            // _run is null only in controlled test scenes (normal play always has a Run) — guard
            // the run-state writes so a no-Run scene can't NRE here.
            if (_run != null)
            {
                _run.Fallen.Add(d.Name);
                // run-end MEMORIAL (presentation only): snapshot the fallen squad member's identity for
                // the run-summary KIA roll. VIP/captive isn't a persistent squad member, so it's excluded.
                if (!d.IsVip)
                    _run.Memorial.Add(new FallenRec { Name = d.FullName, Cls = d.Cls, Rank = d.RankName, Kills = d.Kills, Mission = _run.Mission });
            }
            if (!d.IsVip) SecondaryFailed = true;   // a lost soldier fails the NO LOSSES bonus
            // a fallen squadmate fires up the survivors (Vengeful feat / trait)
            if (!d.IsVip)
                foreach (var p in Players) if (p.Alive && p != d && !p.IsVip) p.AllyDown = true;
        }
        TryFlankKillRefund(d);   // "press the advantage": a player flank-kill refunds an action
        Color c = d.Team == Team.Player ? Pal.Friend : Pal.Foe;
        // team-colored SHATTER: an expanding ring + a radial spark spray + ember clouds, so a
        // kill reads as a figure breaking apart (not a vanish). Tuned to the existing juice scale.
        Fx.Shockwave(d.Pos, c, 8f, 38f, 4f, 0.85f, 0.34f);
        Fx.DirSparks(d.Pos, new Vector2(0, -1f), c, 18, 300f, MathF.PI, 3.5f);   // full-circle spray (spread=PI)
        Fx.Burst(d.Pos, c, 30, 280f, 0.7f, 4f, true);
        Fx.Burst(d.Pos, Pal.RGBA(20, 25, 33), 16, 150f, 0.8f, 5f);
        // lingering scorch decal on the tile (drawn under units, fades over ScorchLife)
        AddScorch(d.Pos, c);
        // FUL-7 vocabulary honesty: a soldier's true death pops "KIA" (DOWN now means the
        // bleeding-out state); enemies keep the generic "DOWN" (they have no bleed-out).
        Fx.PopText(d.Pos + new Vector2(0, -10), d.IsVip ? "VIP DOWN" : (d.Team == Team.Player ? "KIA" : "DOWN"), c, 22f);
        if (d.IsVip) { ShowBanner("VIP DOWN", true); Fx.AddShake(13f); }
        Fx.AddShake(7f);
        AddHitStop(0.1f);
        AddZoomPunch(0.05f);
        AddBloom(0.1f);
        Audio.Play("death");

        // KIA feedback (3.11): a fallen soldier gets a prominent stamp with their
        // name/nickname, a red screen-flash, and is logged for the debrief.
        if (d.Team == Team.Player && !d.IsVip)
        {
            Fx.Stamp(d.Pos + new Vector2(0, -34), "KIA  " + d.FullName, Pal.Foe, 30f, 2.4f);
            _missionKia.Add(d.FullName);
            DeathFlash = 1f;
            Fx.AddShake(9f);
            // C1 VOICE: the same two beats as the DOWN path — a bonded mate reacting, and the
            // moment the squad is down to one. Both are once-per-mission inside Voice, so a death
            // that follows a down never repeats what was already said.
            Bark(Voice.Beat.BondDown, BondPartnerOf(d), d);
            CheckLastStanding();
        }

        // final-blow kill-cam (3.11): the mission-deciding death lingers in slow-mo
        if (IsMissionEndingKill(d))
        {
            AddHitStop(0.4f);
            AddZoomPunch(0.13f);
            AddBloom(0.4f);
            Fx.AddShake(11f);
        }

        // purge any queued movement for the dead unit, AND any queued reaction shots aimed AT it:
        // when several overwatchers react to one mover, the first lethal reaction kills it while the
        // others are still queued — a surplus reaction must not resolve on the corpse (that path
        // re-ran Stats.RecordShot + CreditKill + the death FX). The active anim (the blow that caused
        // this death) is excluded so the current shot still finishes normally.
        PurgeAnimsFor(d);   // FUL-7: factored — EnterDowned needs the same purge (shared helper)
        if (Selected == d) Selected = null;
        // DECAPITATE: a death may have removed the HVT's last in-range guard — re-evaluate now so the
        // HVT is immediately exposed (the telegraph + reduced-damage gate flip the same frame).
        UpdateHvtGuard();
        // UNDERTOW W3: a hostile's death may break its pod's morale (rout the survivors).
        if (d.Team == Team.Enemy && d.PodId >= 0) BreakPodMorale(d);
    }

    /// UNDERTOW W3 — pod ROUT: when a pod is chewed down to <= half its spawn strength (a lone survivor
    /// of a 2-unit pod always qualifies), its remaining ACTIVE members break and ROUT for RoutDuration
    /// turns — they flee toward their own edge, drop overwatch, and shoot wild (Unit.Routed drives
    /// Ai.Plan + Combat). This makes the SECOND kill in a pod worth far more than the first: focus-firing
    /// a pod down is a genuine, earnable comeback swing (a routed pod stops trading -> the player's
    /// HP-sum stabilizes). Survivors RALLY when Routed counts back to 0 (BeginTurn), so it's never a stall.
    void BreakPodMorale(Unit dead)
    {
        int pod = dead.PodId;
        if (pod < 0) return;
        var mates = Enemies.Where(e => e.Alive && e.PodId == pod).ToList();
        if (mates.Count == 0) return;                       // whole pod gone — no one left to break
        int orig = _podOrig.GetValueOrDefault(pod, mates.Count + 1);
        // still at fighting strength — holds the line. RoutThreshold is the ONE shared threshold
        // (PodAtWaverPoint mirrors it one kill ahead, so the WAVERING tag never lies).
        if (mates.Count > RoutThreshold(orig)) return;
        bool broke = false, held = false;
        foreach (var m in mates)
            if (m.Active && m.Routed == 0)
            {
                // SIGNAL W8 — the BANNER anchor: a mate within a living banner's aura does not
                // break. The rout is CONTESTED, not free: kill the WARBRINGER (or catch the pod
                // outside its reach) and the break lands. Per-member, so a split pod can half-rout.
                if (BannerNear(m)) { held = true; continue; }
                // W10 TERROR boon: the break lasts TerrorRoutBonus turns longer (RoutDurationFor).
                m.Routed = RoutDurationFor(); broke = true;
            }
        if (held && !broke)
        {
            // the banner held the whole pod — say so loudly (the player's cue: banner first)
            var anchor = mates.FirstOrDefault(m => m.Active) ?? mates[0];
            Fx.PopText(anchor.Pos + new Vector2(0, -34), "HELD BY BANNER", Pal.Suspect, 18f);
            ShowBanner("THE BANNER HOLDS THE LINE", true);
            return;
        }
        if (!broke) return;                                 // survivors dormant or already routing
        // FUL-1 PROC: a rout started with the extended TERROR duration applied (RoutDurationFor)
        if (HasBoon(Boon.Terror)) Stats.RecordProc("TRR");
        var ldr = mates.FirstOrDefault(m => m.Active) ?? mates[0];
        Fx.PopText(ldr.Pos + new Vector2(0, -34), "BROKEN", Pal.Good, 20f);
        Fx.Flash(ldr.Pos, Pal.Good, 26f, 0.2f, 0.5f);
        ShowBanner("POD ROUTED", false);
        // C1 VOICE: whoever is standing closest to the break calls it. Once per mission.
        Bark(Voice.Beat.PodRout, NearestSoldierTo(ldr));
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  FUL-7 LAST LIGHT — the DOWN / bleed-out window (death stakes get counterplay)
    //  Lethal damage on a soldier opens a 3-turn window instead of an instant cut:
    //  STABILIZE (any adjacent soldier, 1 action) freezes the timer; the corpsman's
    //  PATCH revives; DRAG/EXTRACT carry the body; blasts and fire finish the job.
    //  ONE down per soldier per mission (WasDownedThisMission) — no revive-tanking.
    // ──────────────────────────────────────────────────────────────────────────
    public const int DownedTimerTurns = 3;   // player-turn countdown (ticks in StartPlayerTurn)

    /// W5 ON-RAMP: the bleed-out clock the CURRENT run actually plays with. Standard and every
    /// Heat rung keep the designed 3 turns; the sub-standard RECRUIT rung holds for 5, which is
    /// the difference between "you must already know STABILIZE exists" and "you have time to find
    /// it in the action bar". Read at EnterDowned (and by the banner copy) — the const stays the
    /// baseline so Codex/harness references and every non-recruit run are unchanged.
    public int DownedTimerTurnsNow
        => DownedTimerTurns + (Sightline.Heat.IsRecruit(HeatLevel) ? 2 : 0);

    /// The single purge shared by KillUnit and EnterDowned: drop the unit's queued moves AND any
    /// queued surplus reaction ShotAnims aimed at it (the active blow — the one that caused this —
    /// is excluded so the current shot still finishes normally).
    void PurgeAnimsFor(Unit d)
    {
        _anims.RemoveAll(a => (a is MoveStepAnim m && m.Unit == d)
                           || (a is ShotAnim s && s.D == d && a != ActiveAnim)
                           // W9 THE REPAIR: ...and shots BY the felled unit. The purge only ever
                           // covered the VICTIM's side, so a soldier downed while its OWN shot sat in
                           // the queue behind the blow that felled it still fired: full damage, a
                           // credited kill, Stats bucketed under the downed soldier's class, and the
                           // takedown stinger — off a body at Hp 0.
                           // W9 REVIEW FIX to this comment: it used to claim the ActiveAnim exemption
                           // means "the blow in flight still finishes". TRUE for the target clause
                           // above; FALSE here — ShotAnim.Update self-cancels for a dead/downed
                           // ATTACKER, so an exempted active shot by the felled unit is cancelled one
                           // frame later anyway. The exemption is kept for symmetry and to avoid
                           // mutating the list under the active anim, but on this clause it is inert.
                           // A body does not shoot, in flight or not — which is the intended rule.
                           || (a is ShotAnim s2 && s2.A == d && a != ActiveAnim));
    }

    /// FUL-7: does this lethal event open a bleed-out window instead of killing? Soldiers only
    /// (the VIP/captive dies instantly — mission-shape semantics; enemies rout, not bleed), and
    /// only ONCE per soldier per mission — the anti-revive-tank rule.
    bool CanGoDown(Unit d)
        => d.Team == Team.Player && !d.IsVip && !d.Downed && !d.WasDownedThisMission;

    /// A soldier drops: Hp 0 but ALIVE, 3 turns on the squad's clock. Deliberately NOT fired here:
    /// Fallen/Memorial, _missionKia/KIA stamp/DeathFlash, SecondaryFailed (NoLosses), Stats.RecordKill,
    /// DeathsByClass — death bookkeeping is death's (ExpireDowned runs the full flow via KillUnit).
    void EnterDowned(Unit d)
    {
        d.Downed = true;
        d.WasDownedThisMission = true;
        d.Stabilized = false;
        d.DownedTurns = DownedTimerTurnsNow;
        d.Hp = 0;
        // attribution snapshot — the same switch KillUnit computes, taken NOW so a later bleed-out
        // names the archetype that actually downed them (or the DoT label, which buckets "?").
        Unit downer = ActiveAnim switch { ShotAnim sa => sa.A, GrenadeAnim ga => ga.Thrower, _ => null };
        d.DownedByCls = downer != null ? downer.Cls
                      : (string.IsNullOrEmpty(d.LastDotSource) ? "?" : d.LastDotSource);
        d.Statuses.Clear();                      // no double timers (ground fire still kills via the AoE rule)
        d.WasNearDeath = true;                   // review F6: going down IS a near-death — a PATCH-revived
                                                 // survivor earns the scar track too (recovery forced it,
                                                 // the revive path didn't)
        d.OnOverwatch = false; d.OwFocused = false; d.OwBrace = false; d.Hunkered = false;
        d.RunGun = false; d.Blitz = false; d.Steady = false; d.Slipstreaming = false;
        d.ActionsLeft = 0;
        PurgeAnimsFor(d);                        // queued moves + surplus reactions at the falling body
        if (Selected == d) Selected = null;
        // the Vengeful stage fires at the FALL — an "avenged" revenge shot over a still-breathing
        // squadmate is the drama working (true death re-sets the same flag; idempotent).
        foreach (var p in Players) if (p.Alive && p != d && !p.IsVip && !p.Downed) p.AllyDown = true;
        Stats.RecordDown();                      // FUL-7 telemetry: downs staged (save-rate denominator)
        // feel: decisive but NOT death — the KIA stamp/flash stay reserved for the real thing.
        Color c = Pal.Foe;
        Fx.Stamp(d.Pos + new Vector2(0, -34), "DOWN  " + d.FullName, c, 28f, 2.0f);
        Fx.Shockwave(d.Pos, c, 8f, 30f, 3f, 0.7f, 0.3f);
        Fx.Burst(d.Pos, c, 18, 200f, 0.6f, 3.5f, true);
        Fx.AddShake(5f);                         // softer than the kill's 7+9
        AddHitStop(0.08f);
        Audio.Play("death");
        // review F3 (honesty): the pill counts 3->2->1 and death lands when it would hit 0, so
        // the player ACTS on pills 2 and 1 — say the truthful count instead of promising three.
        ShowBanner($"SOLDIER DOWN - THEY HOLD FOR {DownedTimerTurnsNow}, {DownedTimerTurnsNow - 1} TURNS TO ACT", true);
        BannerSub = "stabilize to stop the bleeding - a corpsman's PATCH gets them up";
        // C1 VOICE: the squad has bonds and the game has never once acknowledged one. BondPartnerOf
        // returns null unless a REAL bonded squadmate is on their feet, so a bondless soldier can
        // never draw this line. If nobody is bonded to them, the last one up may speak instead.
        Bark(Voice.Beat.BondDown, BondPartnerOf(d), d);
        CheckLastStanding();
    }

    /// The timer ran out: the FULL death flow runs — Fallen + Memorial append, KIA stamp,
    /// DeathsByClass keyed on the DOWNING archetype (via Downed still true + DownedByCls), and a
    /// bleed-out KIA reaches Run.Fallen identically to an instant KIA (FUL-10's RemoveVeterans
    /// needs no special case, by construction). Downed stays true through the KillUnit call so
    /// CanGoDown refuses a re-down; cleared after so a corpse never renders/reads as "down".
    bool _expiringDown;   // review F2: lets KillUnit tell a bleed-out expiry from an AoE/fire FINISH

    void ExpireDowned(Unit d)
    {
        d.LastDotSource = d.DownedByCls;         // flywheel threat ranking: the downing cause owns the KIA
        d.Hp = 0;
        Fx.PopText(d.Pos + new Vector2(0, -26), "BLED OUT", Pal.Foe, 20f);
        Stats.RecordDownExpired();
        _expiringDown = true;
        KillUnit(d);                             // KillUnit closes the down state on the corpse (review F2)
        _expiringDown = false;
    }

    /// STABILIZE (universal verb): any soldier with an action, Chebyshev-adjacent to a downed,
    /// un-stabilized ally. 1 action, does NOT end the turn (the DRAG/EXTRACT support convention).
    public Unit StabilizeTarget(Unit u)
    {
        if (u == null || u.Team != Team.Player || u.IsVip || !u.CanAct || u.ActionsLeft < 1) return null;
        foreach (var p in Players)
        {
            if (p == u || !p.Alive || !p.Downed || p.Stabilized) continue;
            if (Util.ChebyDist(u.X, u.Y, p.X, p.Y) <= 1) return p;
        }
        return null;
    }

    public bool CanStabilize(Unit u) => StabilizeTarget(u) != null;

    void DoStabilize()
    {
        var t = StabilizeTarget(Selected);
        if (t == null) return;
        Selected.ActionsLeft -= 1;               // a support action — never ends the turn
        t.Stabilized = true;
        Stats.RecordAction("STABILIZE");         // W2 verb telemetry chokepoint
        Fx.PopText(t.Pos + new Vector2(0, -30), "STABILIZED", Pal.Good, 20f);
        Fx.Burst(t.Pos, Pal.Good, 12, 120f, 0.45f, 3f);
        Fx.PopText(Selected.Pos + new Vector2(0, -30), "STABILIZE", Pal.Good, 15f);
        Audio.Play("reload");
        // C1 VOICE: the clutch save. This is the beat the game most needed words for — 146 soldiers
        // went down across 16 measured campaigns and exactly ONE was revived, in total silence.
        Bark(Voice.Beat.Stabilize, Selected, t);
    }

    /// True when killing `d` decides the mission (last hostile on an Eliminate, a squad
    /// wipe, or the lost VIP) — used to punch up the final blow into a brief kill-cam.
    bool IsMissionEndingKill(Unit d)
    {
        if (d.IsVip) return true;                                   // escort failed
        if (d == Hvt) return true;                                  // decapitation: the HVT fell
        if (d.Team == Team.Enemy)
            return Objective == Objective.Eliminate && AliveEnemies().Count == 0;
        // player down: a wipe (no combatant soldiers left) ends the run
        return !d.IsVip && AlivePlayers().Count(p => !p.IsVip) == 0;
    }

    /// Credit a kill to a player and watch for FEATS (resolved into traits at the
    /// barracks). Replaces the old inline `killer.Kills++` so both the rifle and the
    /// grenade paths run the same feat checks. VIPs earn nothing.
    public void CreditKill(Unit killer)
    {
        killer.Kills++;
        if (killer.Team != Team.Player || killer.IsVip) return;
        // FUL-10 LGD: kills count DOUBLE toward rank — a second credit, the same contract-gated
        // kill-credit family as IronVeterans' debrief bonus. Feats stay single (KillsThisTurn
        // below is untouched). Inert at Contract.None.
        if (_run != null && _run.Contract == Contract.LivingLegends) killer.Kills++;
        killer.KillsThisTurn++;
        if (killer.KillsThisTurn >= 2 && !killer.FeatMultiKill)
        { killer.FeatMultiKill = true; FeatBanner(killer, "MULTI-KILL"); }
        if (killer.MaxHp > 0 && killer.Hp * 4 <= killer.MaxHp && !killer.FeatClutch)
        { killer.FeatClutch = true; FeatBanner(killer, "CLUTCH KILL"); }
        if (killer.AllyDown && !killer.FeatVengeful)
        { killer.FeatVengeful = true; FeatBanner(killer, "AVENGED"); }

        // C1 VOICE — two beats ride the kill. The VENDETTA line is the rarer and more specific of
        // the two (this soldier has a standing grudge against the outfit they just shot), so it is
        // offered first; FIRST BLOOD only ever gets the chance on the mission's actual first kill.
        bool spoke = false;
        if (killer.VendettaFaction != Faction.None && Combat.MissionFaction == killer.VendettaFaction)
            spoke = Bark(Voice.Beat.Vendetta, killer);
        if (!_firstBloodSeen)
        {
            _firstBloodSeen = true;
            if (!spoke) Bark(Voice.Beat.FirstBlood, killer);
        }

        // run boons (on a player kill): reward aggression / sustain.
        if (_run != null && _run.ActiveBoons.Count > 0)
        {
            if (_run.HasBoon(Boon.Grenadier) && killer.Grenades < 1 + killer.BonusGrenades + (killer.HasPerk(Perk.Bandolier) ? 1 : 0))
                killer.Grenades++;                                   // a kill tops the grenade back up
            if (_run.HasBoon(Boon.Scavenger) && killer.Hp < killer.MaxHp)
            {
                int before = killer.Hp;
                killer.Hp = Math.Min(killer.MaxHp, killer.Hp + 2);   // SCAVENGER (re-themed): a kill heals the killer +2 HP (run sustain)
                if (killer.Hp > before) Fx.PopText(killer.Pos + new Vector2(0, -16), $"+{killer.Hp - before}", Pal.Good, 18f);
            }
            // ADRENALINE: a kill on the player's turn refunds +1 action, capped once/turn per soldier
            // (shares the flank-kill refund guard so the two never compound into an endless chain).
            if (_run.HasBoon(Boon.Adrenaline) && Phase == Phase.PlayerTurn && !_refundedThisTurn.Contains(killer))
            {
                killer.ActionsLeft = Math.Min(3, killer.ActionsLeft + 1);
                killer.FiredThisTurn = false;   // TEMPO: a kill-refund re-enables firing (the aggressive chain)
                _refundedThisTurn.Add(killer);
                Fx.PopText(killer.Pos + new Vector2(0, -16), "ADRENALINE", Pal.Accent, 18f);
            }
        }

        // MOMENTUM (perk Adrenal, reworked): any kill on the player's turn refunds +1 action, capped
        // ONCE per soldier per turn via the SAME _refundedThisTurn guard the flank-kill refund + the
        // Adrenaline boon use — so perk + boon + flank-kill can never compound into an endless chain
        // (the autopilot's CanAct loop stays bounded -> no TIMEOUT). Distinct from the universal flank-
        // kill refund: MOMENTUM fires on ANY kill (no flank required), making an aggressive chainer.
        if (Phase == Phase.PlayerTurn && Combat.KillRefundsAction(killer) && !_refundedThisTurn.Contains(killer))
        {
            killer.ActionsLeft = Math.Min(3, killer.ActionsLeft + 1);
            killer.FiredThisTurn = false;   // TEMPO: a kill-refund re-enables firing (the aggressive chain)
            _refundedThisTurn.Add(killer);
            Fx.PopText(killer.Pos + new Vector2(0, -40), "+1 ACTION", Pal.Accent, 22f);
            Fx.PopText(killer.Pos + new Vector2(0, -22), "MOMENTUM", Pal.Good, 16f);
            Fx.Burst(killer.Pos, Pal.Accent, 10, 140f, 0.42f, 3f, true);
            Audio.Play("over");
        }
    }

    /// "Press the advantage" (Wave 3 anti-turtle): when a PLAYER shot KILLS a FLANKED /
    /// EXPOSED enemy on the player's turn, refund +1 action to the shooter — capped at one
    /// refund per soldier per turn. Rewards aggressive flanking and chains (snap -> flank-kill
    /// -> refund -> act again), out-competing passive overwatch-camping.
    ///
    /// The kill is applied from inside ShotAnim.Apply while that shot is the ACTIVE anim, so
    /// the active anim IS the shot that caused this death — we read its ShotResult to tell
    /// whether the target was flanked/exposed (no Combat/Anim signature change needed).
    /// Guards keep it bounded: PlayerTurn only (an enemy-turn overwatch kill grants nothing
    /// the soldier could spend anyway), one /soldier /turn, and finite enemies — so the
    /// autopilot's CanAct loop can never spin forever on refunds.
    void TryFlankKillRefund(Unit d)
    {
        if (Phase != Phase.PlayerTurn || d.Team != Team.Enemy) return;
        if (ActiveAnim is not ShotAnim sa) return;          // only a direct shot refunds (not a grenade/DoT)
        var killer = sa.A;
        // W9: `|| killer.Downed` — a soldier at Hp 0 must not be handed ActionsLeft = 1 (which would
        // make CanAct true again for a body). QA saw no escalation in 214 campaigns, but the
        // guard read only !Alive while Downed is exactly the state a felled soldier is IN.
        if (sa.D != d || killer == null || killer.Team != Team.Player || killer.IsVip
            || !killer.Alive || killer.Downed) return;
        // Require a GENUINE FLANK (not merely any exposed target): the refund rewards
        // *maneuvering to a flank*, not finishing an already-open foe. This de-snowballs the
        // ambush+refund chain a balance audit flagged (an ambush-snap-kill on an exposed-but-
        // -unflanked pod enemy no longer refunds, so one soldier can't clear a whole pod free).
        bool flankKill = sa.Res.Odds.Flanked;
        if (!flankKill || _refundedThisTurn.Contains(killer)) return;
        _refundedThisTurn.Add(killer);
        killer.ActionsLeft = Math.Min(3, killer.ActionsLeft + 1);
        killer.FiredThisTurn = false;   // TEMPO: a flank-kill re-enables firing (the aggressive chain)
        Fx.PopText(killer.Pos + new Vector2(0, -46), "+1 ACTION", Pal.Accent, 22f);
        Fx.PopText(killer.Pos + new Vector2(0, -28), "MOMENTUM", Pal.Good, 16f);
        Fx.Burst(killer.Pos, Pal.Accent, 12, 150f, 0.45f, 3f, true);
        Audio.Play("over");
    }

    /// Note damage to a player so a near-death survival becomes a feat (IronWill).
    public void MarkPlayerHurt(Unit d)
    {
        if (d.Team == Team.Player && !d.IsVip && d.Alive && d.MaxHp > 0 && d.Hp * 4 <= d.MaxHp)
            d.WasNearDeath = true;
    }

    /// Apply per-turn status effects when a unit's turn begins (call right after its
    /// BeginTurn): burning DoT, stun (lose one action), then decay every timer. Bleed
    /// is ticked separately on movement (OnUnitEnteredTile). Per-mission, no persistence.
    public void TickStatuses(Unit u)
    {
        if (!u.Alive || u.Statuses.Count == 0) return;
        // FUL-7: iterate a SNAPSHOT — a lethal DoT tick now routes into EnterDowned, which
        // CLEARS the live Statuses list mid-enumeration (the old foreach threw). And a unit
        // that just went DOWN stops ticking (its timers were cleared; the bleed-out clock owns
        // it now) — without the break, a second status in the same pass would tick a fresh DoT
        // into the downed body and kill it outright.
        foreach (var s in u.Statuses.ToList())
        {
            if (s.Turns <= 0) continue;
            switch (s.Kind)
            {
                case StatusKind.Burning:
                    EnvDamage(u, Unit.BurnDamage, "BURN", Pal.RGBA(255, 140, 40));
                    break;
                case StatusKind.Stun:
                    if (u.ActionsLeft > 0) u.ActionsLeft--;
                    Fx.PopText(u.Pos + new Vector2(0, -30), "STUNNED", Pal.RGBA(225, 205, 95), 18f);
                    break;
            }
            s.Turns--;
            if (!u.Alive || u.Downed) break;   // a DoT can drop the unit mid-tick (death or down)
        }
        u.Statuses.RemoveAll(s => s.Turns <= 0);
    }

    /// Source-less damage (status DoT / hazards): apply, splash FX, kill or note hurt.
    public void EnvDamage(Unit u, int dmg, string label, Color col)
    {
        if (!u.Alive) return;
        // W4 (SIGNAL): the caged RESCUE captive is invulnerable — full stop. Shots/blasts are
        // guarded at their call sites (CanTarget / GrenadeAnim / DetonateBarrel / DetonateSiege),
        // but the source-less paths (Burning DoT, shove SLAM/STAGGER collisions) funnel HERE and
        // had no guard: fire or a shove could kill the caged captive, and CheckEnd's Rescue loss
        // requires !CaptiveLocked — an unwinnable, unlosable soft-lock. Guard the funnel itself.
        if (u == Vip && CaptiveLocked) return;
        u.LastDotSource = label;   // W2: KillUnit's attribution fallback (a DoT death buckets under its cause, not "?")
        u.Hp -= dmg;
        u.Flash = 1f;
        Fx.Burst(u.Pos, col, 8, 130f, 0.4f, 3f, true);
        Fx.PopText(u.Pos + new Vector2(0, -26), $"-{dmg} {label}", col, 20f);
        if (u.Hp <= 0) { u.Hp = 0; KillUnit(u); }
        else
        {
            MarkPlayerHurt(u);
            // SCAR trauma flag (W5): a soldier that takes FIRE damage and lives bears the BURN-SCARRED
            // mark at debrief. "BURN" is the fire/Burning DoT label (status tick + stepping into fire).
            if (u.Team == Team.Player && !u.IsVip && label == "BURN") u.FeatBurned = true;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Environmental hazards: explosive barrels + spreading fire (Wave 2)
    //  Barrels are physical obstacles (Grid.IsFloor excludes them) that chain-detonate
    //  when shot, caught in a grenade blast, or reached by fire — dealing cover-ignoring
    //  AoE and leaving a deny-ground fire field. Fire applies Burning + denies ground.
    // ──────────────────────────────────────────────────────────────────────────
    public const int BarrelDmg = 6;          // base barrel-blast damage (a touch above a frag)
    public const int BarrelRadius = 1;       // Chebyshev blast radius

    // SIEGE / BOMBARD artillery (telegraphed area-denial). A charging SIEGE marks a 3x3 zone on its
    // turn that is shown for the player's WHOLE next turn, then detonates at the start of the
    // following enemy turn (TickSiegeStrikes) for heavy cover-ignoring AoE — unless it's killed
    // (interrupt) or the squad vacates. No fire field / no cover demolition (its identity is "move").
    public const int SiegeFuse   = 1;        // ChargeTurns value set at charge time (see TickSiegeStrikes timing)
    public const int SiegeRadius = 1;        // Chebyshev radius -> a 3x3 zone
    public const int SiegeDmg    = 7;        // cover-ignoring base AoE (a touch above BarrelDmg=6)

    /// True when any live siege-armed enemy has a strike charged (drives the HUD/banner "strike
    /// inbound" cue). W5: HasSiege flag (mirrors Cls=="BOMBARD"; also covers a siege-armed boss).
    public bool SiegeActive
    {
        get { foreach (var e in Enemies) if (e.Alive && e.HasSiege && e.ChargeTurns > 0) return true; return false; }
    }

    /// True if (x,y) is inside a live BOMBARD strike zone. Cover-ignoring, so cover doesn't save you —
    /// the only outs are KILL the artillery or VACATE the tile. Read by the smart autopilot (flee) and
    /// the renderer/self-test. Reads live charge state, so a killed SIEGE's zone auto-clears.
    public bool InSiegeZone(int x, int y)
    {
        foreach (var e in Enemies)
            if (e.Alive && e.HasSiege && e.ChargeTurns > 0
                && Util.ChebyDist(x, y, e.ChargeX, e.ChargeY) <= SiegeRadius) return true;
        return false;
    }

    /// FUL-8 PIKEMAN: true if entering (x,y) would draw a live enemy BRACE reaction — any alive+active
    /// enemy holding a braced watch with ammo, an unspent reaction, range + LoS, and (if focused) the
    /// tile inside its cone. Mirrors the exact OnUnitEnteredTile gate (the InSiegeZone pattern) so the
    /// bot's danger read is truthful; read by TileExposure to route SmartApproach/SmartStep AROUND the
    /// lane instead of feeding it a soldier-turn.
    public bool InEnemyBraceLane(int x, int y)
    {
        foreach (var e in Enemies)
        {
            if (!e.Alive || !e.Active || !e.OnOverwatch || !e.OwBrace || e.ReactedThisTurn || e.Ammo <= 0) continue;
            if (Util.TileDist(x, y, e.X, e.Y) > e.Weapon.MaxRange) continue;
            bool commanding = Grid.HeightAt(e.X, e.Y) - Grid.HeightAt(x, y) >= 2;
            if (!Grid.HasLineOfSight(e.X, e.Y, x, y, commanding)) continue;
            if (e.OwFocused && !InOwCone(e, x, y)) continue;
            return true;
        }
        return false;
    }

    /// Resolve every charged BOMBARD strike at the START of the enemy turn (before any hostile acts
    /// and before reinforcements/pressure add bodies — see EndPlayerTurn). Cover-ignoring 3x3 AoE on
    /// the charged center; both teams in the zone are hit (friendly fire, consistent with every other
    /// AoE; the AI never centers on its own). A dead SIEGE's strike never fires (the kill-interrupt).
    /// Bounded (one pass over the tiny enemy list) -> can never loop/TIMEOUT. NO fire / NO cover demo.
    void TickSiegeStrikes()
    {
        foreach (var e in Enemies.ToList())   // ToList: a strike can kill units; don't mutate mid-scan
        {
            if (!e.Alive || !e.HasSiege || e.ChargeTurns <= 0) continue;
            e.ChargeTurns = 0;                // consume the charge (fired)
            DetonateSiege(e, e.ChargeX, e.ChargeY);
        }
    }

    /// The strike lands: cover-ignoring 3x3 AoE on (cx,cy), reusing the DetonateBarrel pattern
    /// (HardenedReduce + fragile-floor for full-HP players + pod-wake), but WITHOUT cover demolition
    /// or a lingering fire field (keep it clean — its identity is forcing relocation, not terrain
    /// destruction). Damage routes through EnvDamage (kill / near-death / FX handled once). Synchronous.
    void DetonateSiege(Unit src, int cx, int cy)
    {
        var center = Util.TileCenter(cx, cy);
        Audio.Play("crit"); Audio.Play("death");
        Fx.AddShake(13f); AddHitStop(0.06f); AddZoomPunch(0.06f); AddBloom(0.4f);
        float blastR = (SiegeRadius + 0.5f) * Cfg.Tile;
        Fx.Burst(center, Pal.RGBA(255, 140, 90), 38, 360f, 0.6f, 5f, true);
        Fx.Shockwave(center, Pal.RGBA(255, 180, 130), 10f, blastR, 5f, 0.95f, 0.30f);
        Fx.Impact(center, Pal.RGBA(255, 130, 70), blastR * 0.5f, 0.95f, 0.16f);

        var wokePods = new HashSet<int>();
        foreach (var u in Players.Concat(Enemies).ToList())
        {
            if (!u.Alive) continue;
            if (u == src) continue;                              // the firing artillery never catches itself in its own strike
            if (u == Vip && CaptiveLocked) continue;             // the caged captive is invulnerable
            if (Util.ChebyDist(u.X, u.Y, cx, cy) > SiegeRadius) continue;
            if (u.Team == Team.Enemy && !u.Active) wokePods.Add(u.PodId);
            int dmg = SiegeDmg + Util.RandInt(0, 2);
            dmg = Combat.HardenedReduce(u, dmg, crit: false);
            if (u.Team == Team.Player && u.MaxHp >= 2 && u.Hp >= u.MaxHp) dmg = Math.Min(dmg, u.MaxHp - 1);  // fragile floor
            // EnvDamage = the single source-less-damage helper (FX + kill/near-death). No kill-credit
            // to a player — this is enemy artillery (consistent with a fire-cooked barrel crediting no one).
            EnvDamage(u, Math.Max(1, dmg), "STRIKE", Pal.RGBA(255, 160, 90));
        }
        foreach (int pod in wokePods) ActivatePod(pod);
    }

    /// True when a barrel sits adjacent-or-on a soldier cluster worth detonating (AI/autopilot aid).
    public bool BarrelNearFoesOf(int x, int y, Team victims, int radius = BarrelRadius)
    {
        var list = victims == Team.Player ? Players : Enemies;
        return list.Count(u => u.Alive && Util.ChebyDist(u.X, u.Y, x, y) <= radius) >= 1;
    }

    /// Detonate the barrel at (bx,by): cover-ignoring AoE to both teams, cover demolition, a
    /// lingering fire field, and a CHAINED detonation of any other barrel in the blast. `depth`
    /// guards the recursion (a dense barrel field can't blow forever).
    public void DetonateBarrel(int bx, int by, int depth = 0)
    {
        if (!Grid.IsBarrel(bx, by)) return;
        Grid.Barrel[bx, by] = false;        // consumed before the blast so chains don't re-hit it
        // W10 DEMOLITION secondary: a player-credited detonation (shot / grenade / incendiary —
        // SetBarrelCredit at each trigger site) counts the barrel; chained barrels inherit the
        // credit, so a chain reaction is a demolitionist's jackpot. Fire-cooked barrels credit no one.
        if (_barrelCreditTeam == Team.Player) DemoProgress++;
        var center = Util.TileCenter(bx, by);

        Audio.Play("crit"); Audio.Play("death");
        Fx.AddShake(14f); AddHitStop(0.06f); AddZoomPunch(0.07f); AddBloom(0.5f);
        float blastR = (BarrelRadius + 0.5f) * Cfg.Tile;
        Fx.Burst(center, Pal.RGBA(255, 170, 70), 40, 380f, 0.6f, 5f, true);
        Fx.Burst(center, Pal.RGBA(120, 70, 40), 22, 210f, 0.85f, 5f);
        Fx.Shockwave(center, Pal.RGBA(255, 210, 150), 10f, blastR, 5f, 0.95f, 0.30f);
        Fx.Impact(center, Pal.RGBA(255, 150, 60), blastR * 0.5f, 0.95f, 0.16f);

        // cover demolition + lay fire on the floor tiles in the blast
        var chain = new List<(int x, int y)>();
        var wokePods = new HashSet<int>();
        // FUL-1 PROC: once per player-credited blast under PYROMANIACS (the per-tile ternary
        // below extends every tile's burn — counting per tile would inflate the column)
        if (_barrelCreditTeam == Team.Player && HasBoon(Boon.Pyromaniacs)) Stats.RecordProc("PYR");
        for (int x = bx - BarrelRadius; x <= bx + BarrelRadius; x++)
            for (int y = by - BarrelRadius; y <= by + BarrelRadius; y++)
            {
                if (!Grid.InBounds(x, y)) continue;
                var ch = Grid.DamageCover(x, y, Grid.HighCoverHp);
                if (ch != Grid.CoverHit.None) CoverHitFx(x, y, ch);
                // W10 DEMOLITION secondary: cover levelled by a player-credited blast counts too.
                if (ch == Grid.CoverHit.Destroyed && _barrelCreditTeam == Team.Player) DemoProgress++;
                if ((x != bx || y != by) && Grid.IsBarrel(x, y)) chain.Add((x, y));   // catch neighbours
                // residue fire. W10 PYROMANIACS boon: fire the SQUAD starts burns +2 turns (denial).
                Grid.LightFire(x, y, Grid.FireTurns
                    + (_barrelCreditTeam == Team.Player && HasBoon(Boon.Pyromaniacs) ? 2 : 0));
            }

        // damage every unit in radius (friendly fire included), cover ignored (it's an explosion)
        foreach (var u in Players.Concat(Enemies).ToList())
        {
            if (!u.Alive) continue;
            if (u == Vip && CaptiveLocked) continue;             // the caged captive is invulnerable
            if (Util.ChebyDist(u.X, u.Y, bx, by) > BarrelRadius) continue;
            if (u.Team == Team.Enemy && !u.Active) wokePods.Add(u.PodId);
            int dmg = BarrelDmg + Util.RandInt(0, 2);
            dmg = Combat.HardenedReduce(u, dmg, crit: false);
            if (u.Team == Team.Player && u.MaxHp >= 2 && u.Hp >= u.MaxHp) dmg = Math.Min(dmg, u.MaxHp - 1);  // fragile floor
            u.LastDotSource = "BARREL";   // W2: attribution fallback (BarrelShotAnim isn't a Shot/GrenadeAnim, so killer is anim-less)
            u.Hp -= dmg; u.Flash = 1f; u.FlinchAnim = 1f;
            var kick = u.Pos - center;
            if (kick.LengthSquared() > 0.01f) u.Recoil = Vector2.Normalize(kick) * 8f;
            Color c = u.Team == Team.Player ? Pal.Friend : Pal.Foe;
            Fx.Burst(u.Pos, c, 12, 200f, 0.5f, 3.5f, true);
            Fx.PopText(u.Pos + new Vector2(0, -26), dmg.ToString(), Pal.RGBA(255, 200, 140), 26f);
            if (u.Hp <= 0)
            {
                u.Hp = 0;
                bool wasLast = u.Team == Team.Enemy && AliveEnemies().Count <= 1;
                bool byPlayer = u.Team == Team.Enemy && _barrelCreditTeam == Team.Player;
                KillUnit(u);
                if (byPlayer && _barrelCreditUnit != null && _barrelCreditUnit.Alive) CreditKill(_barrelCreditUnit);
                if (byPlayer) Audio.PlayStinger(wasLast ? "lastkill" : "kill");
            }
            else { MarkPlayerHurt(u); u.AddStatus(StatusKind.Burning, 2); }
        }
        foreach (int pod in wokePods) ActivatePod(pod);
        if (depth < 6) foreach (var (cx, cy) in chain) DetonateBarrel(cx, cy, depth + 1);   // chain reaction
    }

    // kill-credit context for a player-triggered barrel (set by the shot/grenade that lit it)
    Team _barrelCreditTeam = Team.Enemy;
    Unit _barrelCreditUnit = null;
    public void SetBarrelCredit(Unit u) { _barrelCreditUnit = u; _barrelCreditTeam = u?.Team ?? Team.Enemy; }

    /// Once-per-round hazard upkeep: detonate barrels reached by fire, refresh Burning on units
    /// standing in fire, then decay the flames. Bounded (no spread loop) so it can never TIMEOUT.
    void TickHazards()
    {
        // a barrel whose tile (or a neighbour) is on fire cooks off
        var cook = new List<(int x, int y)>();
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                if (!Grid.IsBarrel(x, y)) continue;
                bool nearFlame = false;
                for (int dx = -1; dx <= 1 && !nearFlame; dx++)
                    for (int dy = -1; dy <= 1 && !nearFlame; dy++)
                        if (Grid.IsFire(x + dx, y + dy)) nearFlame = true;
                if (nearFlame) cook.Add((x, y));
            }
        _barrelCreditTeam = Team.Enemy; _barrelCreditUnit = null;   // fire-cooked barrels credit no one
        foreach (var (x, y) in cook) DetonateBarrel(x, y);

        // units standing in fire keep burning (the Burning DoT does the damage in TickStatuses).
        // W4 (SIGNAL): never ignite the caged RESCUE captive — it can't move off the tile
        // (Mobility 0, actionless) and EnvDamage refuses to hurt it anyway, so the status
        // would only spam BURN FX on an invulnerable unit every turn the fire lingers.
        // C4 / MAGMA: a unit PARKED on a thermal vent keeps burning, exactly like one parked in
        // fire. Without this, the crossing toll could be dodged by simply stopping on the crack —
        // the fissure would become the safest tile on the board (it also breaks line of sight),
        // which is the dominant-defensive-strategy failure DESIGN.md 3.A forbids. Same caged-VIP
        // exemption for the same reason.
        // C4 REVIEW FIX: this branch hardcoded `2`, which made Terrain.VentBurnTurns DEAD CODE —
        // mutating it 2 -> 1 changed nothing and no test could see it. The vent now re-ignites for
        // its own constant, and BIOMETEST pins the constant's effect rather than its value.
        foreach (var u in Players.Concat(Enemies))
        {
            if (!u.Alive || (u == Vip && CaptiveLocked)) continue;
            if (Grid.IsFire(u.X, u.Y)) u.AddStatus(StatusKind.Burning, 2);
            else if (Grid.IsVent(u.X, u.Y)) u.AddStatus(StatusKind.Burning, Terrain.VentBurnTurns);
        }

        Grid.TickFire();
    }

    void FeatBanner(Unit u, string what)
    {
        ShowBanner($"FEAT: {u.Name} - {what}", false);
        Fx.PopText(u.Pos + new Vector2(0, -40), "FEAT!", Pal.Accent, 22f);
    }

    /// Refresh each living soldier's bond aura: true when a bonded squadmate stands
    /// adjacent (Chebyshev 1). Cheap (squad is tiny); drives the +aim bond bonus.
    void UpdateBondAuras()
    {
        foreach (var u in Players)
        {
            u.BondAura = false;
            if (!u.Alive || u.Bonds.Count == 0) continue;
            foreach (var o in Players)
            {
                if (o == u || !o.Alive || !u.Bonds.Contains(o.Name)) continue;
                if (Util.ChebyDist(u.X, u.Y, o.X, o.Y) <= 1) { u.BondAura = true; break; }
            }
        }
    }

    // ---------------- update ----------------
    /// Drives the procedural music crossfade (0 calm .. 1 combat): tense on the enemy
    /// turn, moderate while live hostiles are about, calm in menus / when clear.
    float MusicIntensity()
    {
        switch (Phase)
        {
            case Phase.EnemyTurn: return 1f;
            case Phase.PlayerTurn:
                return Enemies.Any(e => e.Alive && e.Active) ? 0.5f : 0.15f;
            default: return 0f;   // intro / barracks / win / lose
        }
    }

    /// Relax a unit's transient render-only state each frame: damage flash, recoil/knockback
    /// offset, and the procedural anim poses (fire-recoil / hit-flinch / walk-lean). All decay
    /// deterministically toward 0 so a unit eases back to its idle breathing — kept out of the
    /// sim (purely cosmetic), so the headless screenshot harness stays reproducible.
    static void DecayUnitFx(Unit u, float t)
    {
        u.Flash = MathF.Max(0, u.Flash - t * 4f);
        u.Recoil *= MathF.Exp(-t * 17f);
        u.RecoilAnim = MathF.Max(0, u.RecoilAnim - t * 6.5f);   // snappy: a quick kick that settles fast
        u.FlinchAnim = MathF.Max(0, u.FlinchAnim - t * 6.0f);   // a brief shudder
        u.WalkLean   = MathF.Max(0, u.WalkLean   - t * 7.5f);   // leans through the step, settles when idle
    }

    public void Update(float dt)
    {
        // custom-tag editor is modal: it swallows all other input while open
        if (EditingTag) { UpdateTagEditor(); return; }

        // ── GLOBAL keys (read every phase, before any per-phase handler) ────────────────────
        // These fire ON TOP of whatever the current phase binds, so anything claimed here is
        // claimed EVERYWHERE. Keep the set tiny, and never give a global a letter that an
        // in-mission verb wants.
        //
        // R1 REVIEW FIX — fullscreen was on `F`, which UpdatePlayer also binds to FOCUS (cone
        // overwatch, advertised as "FOCUS F" on the action bar). IsKeyPressed is true for BOTH
        // reads in the same frame, so pressing F during the player turn spent the soldier's
        // action AND toggled fullscreen. The HUD advertises the verb, so the verb wins: fullscreen
        // moves to F11, the platform convention for it, and the only function key besides F2 that
        // this game binds. (The pause menu's FULLSCREEN button is unchanged and still the
        // discoverable path; its key hint now reads F11.)
        //
        // Audited with it: the full in-mission player-turn keymap is
        //   global   M mute · F11 fullscreen · F2 anim speed · Esc cancel-target/pause · C cam reset
        //   verbs    1 aim · 2 overwatch · F focus · B brace · 3 hunker · 4 grenade · 5 ability
        //            6 item · 7 drag · 8 shove · 9 vault · E stabilize · G beacon · H hack
        //            X extract · R reload · T tag · V show-all-verbs · P restart drill
        //            Tab cycle · Enter end turn · Space act · WASD/arrows cursor
        // — no other key appears twice in one context. The other contexts (Intro, skirmish setup,
        // codex, barracks/shop, tag editor) are each internally unique and are reached only when
        // UpdatePlayer is not, so a letter may safely mean different things across them.
        //
        // W5, and read this before you trust the line that used to follow: this list and
        // CLAUDE.md's disagreed with each other AND with the binary (audit wildcard-4 — N/P/U/V
        // were advertised as free and were bound). DERIVE the set instead:
        //     grep -ohE 'KeyboardKey\.[A-Z][a-z0-9]*' src/*.cs | sort -u
        // W5 bound Q (QUIT TO DESKTOP — the pause card and the main menu; not an in-mission verb,
        // so it does not collide with UpdatePlayer's set above). SETTINGS EVERYWHERE bound O (the
        // main menu's SETTINGS door). Free letters as of that wave: I J Z — but DERIVE it with the
        // grep above before binding; this line has been stale before.
        if (Raylib.IsKeyPressed(KeyboardKey.M)) Audio.ToggleMute();
        if (!AutoPlay && Raylib.IsKeyPressed(KeyboardKey.F11)) Display.ToggleFullscreen();
        if (!AutoPlay && Raylib.IsKeyPressed(KeyboardKey.F2)) CycleAnimSpeed();   // fast-forward anim pacing (persisted; also in the pause menu)
        Audio.SetMusicIntensity(MusicIntensity());
        UpdateTutorial(dt);
        UpdateTraining(dt);            // T1: the TRAINING OP lesson track (drill mode only)
        UpdateFieldTips(dt);           // FUL-12 -> T1: once-per-profile JIT tips (never overlap a lesson)
        UpdateBriefing(dt);            // RESONANCE C1: the mission briefing card's own clock
        Fx.UpdateAmbient(Biome, dt);   // per-biome ambient atmosphere (Wave B)

        // camera zoom-punch always relaxes; hit-stop freezes the rest of the sim
        _camPulse *= MathF.Exp(-dt * 11f);
        if (_camPulse < 0.001f) _camPulse = 0;
        if (DeathFlash > 0) DeathFlash = MathF.Max(0, DeathFlash - dt * 1.6f);

        // Phase 5.2: bloom decays smoothly (half-life ~0.6 s) and drives Display post-FX.
        _postFxBloom = MathF.Max(0, _postFxBloom - dt * 0.9f);
        Display.AdvanceTime(dt);
        // Biome colour-grade tint: a gentle push toward the biome hue (neutral at 1,1,1).
        // Values are close to 1 to avoid washing out readability; the squint test must pass.
        var biomeT = Biome.Tint;
        // Normalise biome tint to produce a subtle multiplicative grade near 1.0.
        // biomeT components are 22..108 raw; map to 0.97..1.03 range.
        float invBase = 1f / 80f;
        var grade = new Vector3(
            1f + (biomeT.R - 60) * invBase * 0.04f,
            1f + (biomeT.G - 60) * invBase * 0.04f,
            1f + (biomeT.B - 60) * invBase * 0.04f);
        Display.SetPostFxParams(_postFxBloom, _postFxBloom * 0.55f, grade);

        if (HitStop > 0) { HitStop -= dt * AnimSpeed; return; }   // fast-forward shortens the hit-stop freeze too

        // pause/settings overlay + camera controls (live play only, never in autoplay)
        if (!AutoPlay && SettingsCardPhase(Phase))
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Escape)) OnEscape();
            if (Paused) { HandlePauseMenu(); return; }
            if (Phase == Phase.PlayerTurn || Phase == Phase.EnemyTurn) { HandleCamera(); UpdateAutoCam(dt); }
        }

        float t = MathF.Min(dt, 0.05f);
        Fx.Update(t);
        // DECAPITATE telegraph: a guarded HVT hit was softened this frame — pop a single "GUARDED"
        // float at the HVT (Combat can't reach Fx; it just raises the one-shot flag, drained here).
        if (Combat.HvtGuardReducePending)
        {
            Combat.HvtGuardReducePending = false;
            if (HasHvt && Hvt.Alive) Fx.PopText(Hvt.Pos + new Vector2(0, -42), "GUARDED", Pal.Foe, 18f);
        }
        foreach (var u in Players) DecayUnitFx(u, t);
        foreach (var u in Enemies) DecayUnitFx(u, t);
        for (int i = Scorches.Count - 1; i >= 0; i--)   // death scorch decals fade out
        {
            var s = Scorches[i]; s.Life -= t;
            if (s.Life <= 0) Scorches.RemoveAt(i); else Scorches[i] = s;
        }
        UpdateBondAuras();   // bonded squadmates buff each other while adjacent
        if (BannerTimer > 0) BannerTimer -= t;
        // W11 NEW CONTACT: with the banner lane free, ID the next unseen alert archetype (one per
        // banner window). Live phases only; internally !NoPersist-gated like the tutorial.
        else if (Phase == Phase.PlayerTurn || Phase == Phase.EnemyTurn) CheckNewContact();

        // C5 THE HARD EDGES: the enemy-turn deadlock guard runs BEFORE the animation pump — an
        // animation that never completes returns below, so a guard placed after it (or inside
        // UpdateEnemy) could never see that half of the deadlock. See EnemyStallGuard.
        if (Phase == Phase.EnemyTurn) EnemyStallGuard();

        // advance animation queue — but NEVER while the codex is open (FUL-2: BeginCodex clears
        // Paused for the overlay, which let queued enemy ShotAnims resolve while the player read
        // the field manual; the queue freezes with the fight and resumes on ExitCodex).
        if (Phase != Phase.Codex && Phase != Phase.AudioCheck && _anims.Count > 0)   // A3: the audition screen freezes the fight exactly like the codex does
        {
            var a = _anims[0];
            if (!a.Started) { a.Started = true; a.OnStart(this); }
            // a.Update can MUTATE the queue: KillUnit purges the dead unit's queued moves, and an
            // overwatch reaction can kill the moving unit (removing `a` itself) or empty the queue.
            // So only pop index 0 when `a` is genuinely still at the front — never RemoveAt(0) on an
            // emptied/reordered queue (that was an intermittent IndexOutOfRange crash deep in a batch).
            // Scale ONLY the anim-stepping dt by AnimSpeed (default 1f = unchanged) — this fast-forwards
            // the visual playback without touching the autopilot/sim timers elsewhere in Update.
            bool done = a.Update(this, t * AnimSpeed);
            if (done && _anims.Count > 0 && _anims[0] == a) _anims.RemoveAt(0);
            return;
        }

        switch (Phase)
        {
            case Phase.Intro: HandleOverlayClick(); break;
            case Phase.PlayerTurn: UpdatePlayer(); break;
            case Phase.EnemyTurn: UpdateEnemy(); break;
            case Phase.Barracks:
                // SETTINGS EVERYWHERE: [K] opens the FIELD MANUAL from the barracks (it was
                // Intro-gated; the screen where a player deliberates over perks and the shop had no
                // route to the reference). ExitCodex returns HERE, not to the intro. Live play only —
                // the rename editor already returned above, so a typed K never reaches this line.
                if (!AutoPlay && Raylib.IsKeyPressed(KeyboardKey.K)) { BeginCodex(); break; }
                // APEX W7 — LAST STAND mid-stand progression detour. Endless enters Barracks ONLY
                // to resolve queued perk/spec/boon offers (CheckEndless sets _shopDone before the
                // detour). The moment every offer is resolved, return to the fight and spawn the
                // next wave. This guard MUST sit above the shop/event/node branches: BeginEndless's
                // Run.Start() built a real campaign map, so a fall-through would let autoplay
                // ChooseNode into a campaign mission from inside a stand.
                if (Mode == GameMode.Endless
                    && _run.PendingPerks.Count == 0 && _run.PendingSpecs.Count == 0 && _run.BoonOffer.Count == 0)
                {
                    // W1 mode-seam: a mid-stand boon pick (Run.ChooseBoon during this detour) must
                    // reach the static combat reads NOW — endless never passes through EndMission/
                    // BeginMission between waves, so without this republish the pick was cosmetic.
                    Combat.RefreshRunBoons(_run.ActiveBoons);
                    Phase = Phase.PlayerTurn;
                    SpawnEndlessWave(Wave + 1);
                    return;
                }
                if (!_shopDone)                          // spend intel first (requisition)
                {
                    if (AutoPlay) AutoShop(); else HandleShopClick();
                }
                else if (_run.PendingPerks.Count > 0)    // then resolve rank-up perk picks
                {
                    if (AutoPlay) ChoosePerk(0); else HandlePerkClick();
                }
                else if (_run.PendingSpecs.Count > 0)    // W2: then a one-time class SPECIALIZATION fork
                {
                    if (AutoPlay) ChooseSpec(0); else HandleSpecClick();
                }
                else if (_run.BoonOffer.Count > 0)       // then pick a run-scoped boon
                {
                    if (AutoPlay) _run.ChooseBoon(_run.BoonOffer[0]); else HandleBoonClick();
                }
                else if (EventPending)                   // W4: resolve a "?" FIELD EVENT before the node pick
                {
                    if (AutoPlay) ResolveEvent(AutoEventChoice()); else HandleEventClick();
                }
                else
                {
                    // debrief screen: bench toggles are available before choosing a node/card
                    if (!AutoPlay) HandleBenchClick();
                    if (_run.NextNodes().Count > 0)      // pick the next node on the campaign map
                    {
                        if (AutoPlay)
                        {
                            var nn = _run.NextNodes();
                            ChooseNode(PickAutoNode(nn).Id);
                        }
                        else HandleNodeClick();
                    }
                    else if (AutoPlay) ChooseCard(0);    // fallback: deployment cards
                    else HandleCardClick();
                }
                break;
            case Phase.Win:
            case Phase.Lose: HandleOverlayClick(); break;
            case Phase.Draft: HandleDraftClick(); break;
            case Phase.WarRoom: HandleWarRoomClick(); break;   // W3: cross-run meta screen
            case Phase.Codex: HandleCodexInput(); break;       // W6: field manual / reference
            case Phase.AudioCheck: HandleAudition(t); break;   // A3: the AUDIO CHECK audition screen
            case Phase.SkirmishSetup: HandleSkirmishSetup(); break;  // W4: skirmish objective/heat picker
        }

        CheckEnd();
    }

    void CheckEnd()
    {
        if (Phase != Phase.PlayerTurn && Phase != Phase.EnemyTurn) return;
        if (_anims.Count > 0) return;
        // PROGRAM HORIZON W2: LAST STAND runs its own end/advance logic (wipe = run over; clearing a
        // wave spawns the next). It NEVER touches the campaign objective ladder or the checkpoint valve.
        if (Mode == GameMode.Endless) { CheckEndless(); return; }
        // PROGRAM HORIZON W4: SKIRMISH / DAILY are single-mission — reuse the SAME per-objective win
        // tests, but route to Phase.Win/Lose (no barracks / checkpoint valve / save.json).
        if (Mode == GameMode.Training) { CheckTraining(); return; }   // T1: the drill ends on its own terms
        if (Mode == GameMode.Skirmish) { CheckSkirmish(); return; }
        var alivePlayers = AlivePlayers();
        // APEX W2: RESCUE soft-lock. The caged captive is invulnerable AND actionless, so if every
        // actual soldier dies while it is still locked, nothing on the board can ever free it (or
        // kill it) — the mission would sit forever. That's a wipe in all but name: burn the one-time
        // checkpoint if it's available, else the run is lost. A FREED captive is untouched by this —
        // it can still walk itself out (the lone-captive win HEATLADDERTEST pins), mirroring Escort's
        // intentional VIP-solo win (pinned by DEATHTEST).
        if (Objective == Objective.Rescue && CaptiveLocked && !alivePlayers.Any(p => !p.IsVip))
        {
            if (TryReinforcements()) return;
            LoseRun("CAPTIVE ABANDONED", $"Every soldier fell with the captive still caged on mission {_run.Mission}."); return;
        }
        if (alivePlayers.Count == 0)
        {
            // ONE-TIME CHECKPOINT: a squad wipe at/after the threshold mission triggers an emergency
            // redeploy of fresh rookies to retry THIS mission (you lose your veterans, but the run
            // survives). A second wipe — or a wipe before the threshold — is a real loss.
            if (TryReinforcements()) return;
            LoseRun("RUN OVER", $"The squad fell on mission {_run.Mission}."); return;
        }

        if (Objective == Objective.Eliminate)
        {
            if (AliveEnemies().Count == 0) EnterBarracks();
        }
        else if (Objective == Objective.Hack) // hold the terminal until it's fully hacked
        {
            if (HackProgress >= HackRequired) EnterBarracks();
        }
        else if (Objective == Objective.Sabotage) // plant charges on every site
        {
            if (SabotageBlown.Count >= SabotageSites.Count) EnterBarracks();
        }
        else if (Objective == Objective.Escort) // get the VIP to extraction; losing it is a wipe
        {
            if (Vip == null || !Vip.Alive) { LoseRun("VIP LOST", $"The asset was lost on mission {_run.Mission}."); return; }
            if (EvacZone.Contains((Vip.X, Vip.Y))) EnterBarracks();
        }
        else if (Objective == Objective.Rescue) // free the captive, then walk it to extraction
        {
            // W4 (SIGNAL) belt-and-braces: a DEAD captive is CAPTIVE LOST whether or not the cage
            // was sprung. The old !CaptiveLocked qualifier meant a captive killed while still caged
            // (every damage path is guarded now, but guards can regress) left the mission with no
            // reachable win OR loss — the soft-lock. Never let a dead asset stall the run.
            if (Vip == null || !Vip.Alive)
            { LoseRun("CAPTIVE LOST", $"The captive died on mission {_run.Mission}."); return; }
            // (no Vip null-check: the guard above returns on a null/dead captive)
            if (!CaptiveLocked && EvacZone.Contains((Vip.X, Vip.Y))) EnterBarracks();
        }
        else if (Objective == Objective.Defend) // hold out for DefendTurns player turns
        {
            if (_turnCount > DefendTurns) EnterBarracks();
        }
        else if (Objective == Objective.Decapitate) // kill the marked HVT; the rest don't matter
        {
            if (Hvt == null || !Hvt.Alive) EnterBarracks();
        }
        else // Evac: every surviving soldier must stand in the extraction zone
        {
            if (alivePlayers.All(p => EvacZone.Contains((p.X, p.Y)))) EnterBarracks();
        }
    }

    /// One-time mid-run recovery valve. Called from the SQUAD-WIPE branch of CheckEnd and from the
    /// Rescue CAPTIVE-ABANDONED branch (all soldiers down, captive still caged — W2); other VIP/
    /// captive-lost losses stay instant. Returns false — letting the wipe become a real loss — when
    /// the checkpoint is already spent OR the wipe came too early (mission < 3: early failure ends
    /// cleanly). Otherwise it burns the checkpoint, rebuilds the squad as a fresh emergency cadre of
    /// rookies (keeping Intel / heat / map position), and RESTARTS the current mission from its start
    /// (SetupMission re-checkpoints the save). The squad's veterans are gone — a bad mission is now
    /// survivable but costly, not run-ending. Can fire at most once per run, so there's no loop risk.
    bool TryReinforcements()
    {
        // W5 ON-RAMP: the valve opens at mission 1 on the RECRUIT rung. On the standard ladder an
        // m1-2 wipe still ends cleanly (a 4-rookie opener loss is a fast restart, not a tragedy);
        // for a first-time player it is the single most run-ending moment in the game, so RECRUIT
        // spends the one-time checkpoint there instead.
        int ckMin = _run != null && Sightline.Heat.IsRecruit(_run.HeatLevel) ? 1 : 3;
        if (_run == null || _run.CheckpointUsed || _run.Mission < ckMin) return false;
        _run.CheckpointUsed = true;

        // fresh emergency squad: deploy as many rookies as this mission fields (fall back to the
        // attrition floor of 3). The cadre carries no bought mods/veteran history — that's the price
        // of the wipe — but it is DEPTH-SCALED (APEX W8): each recruit arrives with (Mission-1)/2
        // seeded kills and is promoted at the draft (rank + a queued perk offer for the next
        // barracks; DebriefSurvivors prunes rather than clears, so the offer survives). A mission-5
        // wipe redeploying literal 0-kill rookies against the mission-5 force was the flagged
        // death-spiral. Distinct callsigns (APEX W5): the cadre is drawn with a taken-names set so
        // two "ROOK"s can't share (and silently merge) bond/memorial records.
        int cap = Math.Max(Run.AttritionFloor, Run.DeployCapFor(_run.Mission));
        _run.Squad = new List<Unit>();
        var cadreNames = new HashSet<string>();
        for (int i = 0; i < cap; i++)
        {
            var rec = Mission.MakeRecruit(cadreNames, _run.Mission);
            cadreNames.Add(rec.Name);
            _run.Squad.Add(rec);
            _run.PromoteEligible(rec);
        }

        // Intel, heat, and map position are untouched. Restart THIS mission from its start (the same
        // setup the normal flow uses; it re-checkpoints the save). SetupMission sets its own
        // MISSION banner, so override it AFTER with the reinforcements telegraph.
        SetupMission(_run.Mission);
        ShowBanner("REINFORCEMENTS DEPLOYED - hold the line.", true);
        return true;
    }

    // ---------------- player turn ----------------
    // Test-only autopilot (enabled via SIGHTLINE_AUTOPLAY): drives real player actions
    // so the whole loop can be exercised headlessly. Never enabled in normal play.
    public bool AutoPlay;

    // Competent-AI flag (enabled via SIGHTLINE_SMARTPLAY / the SIGHTLINE_BALANCE batch
    // runner). When set, the autopilot routes through SmartStep() — a heuristic player
    // that actually plays to win (cover/threat-aware positioning, best-target selection,
    // deliberate ability/ambush use) — so headless games become a real balance gauge.
    // The default AutoStep() remains the path-coverage smoke test.
    public bool SmartPlay;

    // SLOPPY policy flag (balance harness only; SIGHTLINE_BALANCE_SLOPPY or the batch's
    // paired second policy). When set, SmartStep injects human-like error: ~15% of decisions
    // skip overwatch / take a slightly-worse action, so the report can measure the optimal-
    // vs-sloppy GAP (difficulty slack). Gated entirely behind SmartPlay; never affects normal
    // play. The perturbation RNG (_sloppyRng) is seeded deterministically per run (SeedSloppy)
    // so a batch is reproducible. A coin-flip helper that's a no-op unless sloppy is on.
    public bool SmartSloppy;
    System.Random _sloppyRng;
    /// Seed the sloppy-policy RNG from the campaign index so runs are reproducible. Called by
    /// the balance batch right after constructing the Game; harmless if SmartSloppy is off.
    public void SeedSloppy(int seed) => _sloppyRng = new System.Random(seed);
    /// True with probability pct ONLY when the sloppy policy is active — the single gate every
    /// SmartStep perturbation routes through (so greedy play is byte-for-byte the optimal path).
    bool Slip(int pct)
    {
        if (!SmartSloppy) return false;
        _sloppyRng ??= new System.Random(12345);
        return _sloppyRng.Next(100) < pct;
    }
    /// W2: a sloppy-only index draw (0..n-1) off the SAME deterministic perturbation stream as
    /// Slip(), for picking WHICH mediocre tile a positional slip settles on. Only ever called
    /// after a Slip() returned true, so the greedy policy never touches the stream.
    int SlipPick(int n)
    {
        _sloppyRng ??= new System.Random(12345);
        return n <= 1 ? 0 : _sloppyRng.Next(n);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    // SmartStep — the COMPETENT headless autopilot (SIGHTLINE_SMARTPLAY / the balance
    // runner). Where AutoStep() is a deliberate smoke-test (nearest target, march straight
    // in, random ability/grenade rolls — loses almost everything), SmartStep plays to WIN
    // so headless games are a real balance gauge.
    //
    // Same per-step contract as AutoStep: called repeatedly while it's the player turn and
    // the anim queue is idle; each call commits at most ONE action for the best soldier,
    // then returns (the dispatcher calls us again until every soldier is spent and the turn
    // ends). It REUSES every game primitive (Combat.ComputeOdds via SmartOdds, the same
    // tile-scoring philosophy as Ai.Plan, ComputeThreat's exposure model, the real action
    // issuers) so it never desyncs from the rules.
    //
    // PROGRESS INVARIANT (sacred — must never TIMEOUT): every code path either issues a
    // real action (shoot/move/grenade/item/ability/hack/overwatch/reload) or falls through
    // to a guaranteed-progress fallback (advance toward the objective/enemy, else hunker),
    // exactly like AutoStep. The objective routing (Evac/Hack/Sabotage/Escort/Rescue/
    // Defend/Decapitate) is preserved — combat is just layered on top of it.
    // ════════════════════════════════════════════════════════════════════════════════
    int _lastTelemetryTurn = -1;   // balance harness: record decision/swing once per player turn


    // Stall guard for the headless autopilot: if no progress is made for several
    // player turns (e.g. only unreachable dormant pods remain), force a pod awake
    // so the match always resolves. Test-only; never runs in normal play.
    int _autoSig = -1, _autoStall;
    int _smartConcealTurns;  // SmartStep: player turns spent concealed (hard anti-TIMEOUT cap)
    const int AutoMaxTurns = 50;  // hard autopilot MISSION cap: force-end a dragging match as a LOSS
    /// W9 THE REPAIR — the RUN-scoped autopilot cap, and the one that actually makes "never a
    /// RESULT: TIMEOUT" true. AutoMaxTurns above is per-MISSION and is re-armed by SetupMission's
    /// `_turnCount = 1`, INCLUDING the mid-mission checkpoint redeploy — so a campaign could spend
    /// 21 turns on mission 5, wipe on mission 6, take the checkpoint, and be handed a fresh 50-turn
    /// allowance, while Program.cs's autoCap = 20000 frames buys the WHOLE campaign about 40 turns.
    /// The backstop was roughly 5x too loose to bound what it claimed to bound. QA measured 2 TIMEOUTs
    /// in 214 seeded campaigns (~1%); W9 reproduced both (seeds 2001 and 3001) and, on a 20-seed
    /// census of its own, saw 2 of 20 — a rate qa-sweep only PRINTED and never failed on, and which
    /// BalanceBatch scores as a LOSS, right-censoring exactly the longest campaigns.
    /// CALIBRATED FROM A MEASURED CENSUS of this tree's own autoplay, 20 seeded campaigns, RESULT
    /// lines carrying `turns=`: every run finished, the longest took 75 run-turns / 18,992 frames and
    /// the next longest 38, and the per-turn frame cost falls as a campaign lengthens (a long campaign
    /// is long because it is ATTRITED, and a small squad takes cheap turns — the 75-turn outlier ran
    /// 253 frames/turn against 400-680 on short full-squad runs).
    /// 150 is 2x the longest campaign measured. That headroom is the point: firing this cap on a
    /// LEGITIMATE run would score it a LOSS and put the same downward bias into the ladder that the
    /// old 20,000-frame budget did. It must only ever catch something genuinely stuck.
    public const int AutoMaxRunTurns = 150;
    /// The harness's whole-campaign FRAME budget (Program.cs's autoplay loop and BalanceBatch both
    /// read it). It lives HERE, next to the turn cap it must dominate, because the two numbers are a
    /// PAIR: if the frame budget can expire before AutoMaxRunTurns is reached, RESULT: TIMEOUT is
    /// reachable again. STALLTEST pins AutoMaxRunTurns * AutoFramesPerTurn <= AutoFrameCap so a future
    /// edit to either number fails loudly instead of quietly re-opening the hole.
    public const int AutoFrameCap = 120000;
    /// The per-turn frame ceiling for that arithmetic. W9 REVIEW FIX: this was 600, chosen from the
    /// LONG-CAMPAIGN regime (253 frames/turn at 75 turns) — but the measured SPREAD runs to 681
    /// (Program.cs), so a campaign that somehow sustained its worst observed rate for 150 turns would
    /// have hit the frame cap at ~147 turns, three turns before the turn cap. "TIMEOUT is unreachable"
    /// would then have been EMPIRICAL rather than STRUCTURAL — and that matters more now the sweep
    /// hard-fails on a TIMEOUT, because a rare false positive becomes a merge block. Set to the
    /// measured WORST case instead: 150 x 700 = 105,000 <= the 120,000 budget, with no regime
    /// assumption left in the argument.
    public const int AutoFramesPerTurn = 700;

    // ---------------- activation pods (4.3 awareness tiers) ----------------
    // Baselines; Heat "SHORT FUSE"/"RELENTLESS" shrink first-contact ranges by 1 (read off the
    // active run's HeatLevel). Floored at 1 so a pod can always still be spotted.
    public const int BaseSightRange = 9;   // spotted at range -> Suspicious (4.2: down from 12)
    public const int BaseAlertRange = 4;   // spotted up close -> straight to Alert (no grace turn)
    public const int BaseRevealRange = 3;  // 4.4: stepping this close to an ACTIVE foe auto-breaks concealment
    int ContactTighten => Heat.TighterContact(_run?.HeatLevel ?? 0) ? 1 : 0;
    public int SightRange  => Math.Max(1, BaseSightRange  - ContactTighten);
    public int AlertRange  => Math.Max(1, BaseAlertRange  - ContactTighten);
    public int RevealRange => Math.Max(1, BaseRevealRange - ContactTighten);
    // Balance fix: a hack/plant is LOUD — the noise rouses dormant pods within this radius of
    // the objective site even without line of sight (a sound cue, not a sight cue). Kept SMALL
    // so it wakes only the immediately-adjacent pods, NOT the whole force at once: a wide wake
    // proved unwinnable (Sabotage's charge sites sit in the enemy half, so going loud there can
    // rouse a dense late-mission force). The fight still escalates after the first loud act
    // because concealment is broken, so the normal alert tiers (CheckPodActivation, via sight)
    // take over. Heat's tighter-contact does NOT shrink it (noise carries regardless of stealth).
    public const int HackNoiseRange = 2;
    public bool SquadConcealed;        // 4.4: squad starts each mission concealed (set in SetupMission)
    // W6 CONTRACT "SPEARHEAD": the once-per-mission turn-1 +1-action surge. Set false at each mission
    // SETUP (its own first turn), flipped true the moment the surge is granted, so it fires EXACTLY
    // once per mission and never stacks across turns. Inert unless _run.Contract == Spearhead.
    bool _spearheadSurgeUsed;

    // Closest distance at which any living soldier currently has line of sight on this
    // enemy (within SightRange), or -1 if it is unseen.
    float ClosestSightedDist(Unit e)
    {
        float best = -1f;
        foreach (var p in Players)
        {
            if (!p.Alive) continue;
            float d = Util.TileDist(p.X, p.Y, e.X, e.Y);
            if (d > SightRange || (best >= 0 && d >= best)) continue;
            if (!Grid.HasLineOfSight(p.X, p.Y, e.X, e.Y)) continue;
            best = d;
        }
        return best;
    }

    void CheckPodActivation()
    {
        TryFreeCaptive();                       // captive proximity isn't aggression - always checked
        if (SquadConcealed) return;             // 4.4: concealment masks the squad; pods can't escalate via sight
        foreach (var e in Enemies)
        {
            if (!e.Alive || e.Active) continue;          // already fully alert
            float d = ClosestSightedDist(e);
            if (d < 0) continue;                         // not in sight
            if (d <= AlertRange) ActivatePod(e.PodId);   // blundered in close -> snap awake (+scatter)
            else SetPodSuspicious(e.PodId);              // spotted at range -> telegraphed warning
        }
    }

    /// 4.3: a whole pod goes Suspicious (alerted "!"), but does NOT act or scatter yet — it
    /// confirms (-> Alert) at the player's turn end if still in sight (ResolveSuspicion),
    /// else loses interest (-> Unaware). This is the telegraph that kills the turn-1 gotcha.
    void SetPodSuspicious(int podId)
    {
        bool any = false;
        foreach (var e in Enemies)
        {
            if (!e.Alive || e.PodId != podId || e.Alert != AlertLevel.Unaware) continue;
            e.Alert = AlertLevel.Suspicious;
            Fx.PopText(e.Pos + new Vector2(0, -30), "!", Pal.Suspect, 22f);
            any = true;
        }
        if (any) { BannerText = "CONTACT?"; BannerEnemy = true; BannerMax = BannerTimer = 0.9f; Audio.Play("select"); }
    }

    /// At the player's turn end, resolve every Suspicious pod: it confirms the threat
    /// (-> Alert, acts this enemy turn, but with NO free scatter since it had a turn's
    /// warning) when a soldier is still in sight, or loses interest (-> Unaware) when
    /// contact was broken. So lingering in view wakes them; retreating keeps them dormant.
    void ResolveSuspicion()
    {
        foreach (var e in Enemies)
        {
            if (!e.Alive || e.Alert != AlertLevel.Suspicious) continue;
            // FUL-6 LINKED ACTIVATION arm: a Suspicious enemy whose pod was linked by a nearby
            // wake (_linkedPods) confirms to Alert even when UNSEEN — the sound was enough.
            // NO scatter either way (the telegraphed path, exactly like the sighted confirm),
            // and never a chain: this pass never calls ActivatePod.
            if (ClosestSightedDist(e) >= 0 || _linkedPods.Contains(e.PodId))
            {
                e.Alert = AlertLevel.Alert;
                Fx.PopText(e.Pos + new Vector2(0, -32), "ALERT", Pal.Foe, 18f);
            }
            else e.Alert = AlertLevel.Unaware;   // squad broke contact in time
        }
        _linkedPods.Clear();   // every linked pod resolves in the pass above; entries never linger
    }

    /// RESCUE: free the caged captive once a soldier reaches it; it then becomes a
    /// fragile escort that must be walked to extraction.
    void TryFreeCaptive()
    {
        if (!CaptiveLocked || Vip == null || !Vip.Alive) return;
        foreach (var p in Players)
            if (p.Alive && !p.IsVip && Util.ChebyDist(p.X, p.Y, Vip.X, Vip.Y) <= 1)
            {
                CaptiveLocked = false;
                Vip.Mobility = 6;                       // can move now
                Vip.BeginTurn();                        // give it actions this turn
                ShowBanner("CAPTIVE FREED", false);
                Fx.PopText(Vip.Pos + new Vector2(0, -30), "FREED", Pal.VipGold, 22f);
                Fx.Burst(Vip.Pos, Pal.VipGold, 20, 200f, 0.6f, 4f, true);
                return;
            }
    }

    // Snap a whole pod to fully Alert with a reaction scatter. This is the SURPRISE path
    // (blundered in close, or shot/pinned/grenaded). The telegraphed path — a pod that was
    // merely Suspicious — wakes via ResolveSuspicion instead, with NO scatter (it had its
    // warning), so the criticized "free move on reveal" only happens on a genuine surprise.
    public void ActivatePod(int podId)
    {
        bool any = false;
        // Q1 "NO TWO IN ONE PLACE" — the running CLAIM SET for this scatter batch.
        // Unit.X/Y only commits when a MoveStepAnim FINISHES, but this loop plans EVERY dormant
        // member against one board snapshot and enqueues all their steps before any of them run.
        // Ai.Plan's blocked predicate reads live X/Y, so without a claim set member 2 planned
        // blind to member 1's destination — and the whole pod planned blind to the PLAYER's
        // destination, because the steps that carry the player there are still queued AHEAD of
        // this scatter and land first. Result: two living bodies on one tile, sometimes for
        // several turns. That is not cosmetic — UnitAt returns the FIRST match (Players before
        // Enemies) and both hover and click route through it, so the buried unit cannot be
        // hovered, cannot show odds, and cannot be clicked to target.
        // Seed with the FINAL destination of every already-queued move (any unit, any team;
        // last step per unit wins), then add each scatter's own landing tile as it is decided.
        // Current tiles need no entry — Ai.Plan's IsOccupiedByOther already blocks those.
        // Guarded by SIGHTLINE_STACKTEST.
        var claimed = new HashSet<(int x, int y)>();
        var lastDest = new Dictionary<Unit, (int x, int y)>();
        foreach (var a in _anims)
            if (a is MoveStepAnim ms && ms.Unit != null && ms.Unit.Alive) lastDest[ms.Unit] = (ms.Tx, ms.Ty);
        foreach (var kv in lastDest) claimed.Add(kv.Value);
        foreach (var e in Enemies)
        {
            if (!e.Alive || e.Active || e.PodId != podId) continue;
            e.Alert = AlertLevel.Alert;
            any = true;
            // free scatter toward cover/line of fire (move only, no shot); 4.2 caps it to a
            // SINGLE move — no free dash on reveal (an immobile turret gets none).
            var plan = Ai.Plan(this, e, claimed);
            int cap = Math.Max(0, e.Mobility) * 2, spent = 0, lx = e.X, ly = e.Y;
            foreach (var (px, py) in plan.Path)
            {
                int step = (px != lx && py != ly) ? 3 : 2;   // diag costs 3, ortho 2 (matches CostMap)
                if (spent + step > cap) break;
                spent += step; lx = px; ly = py;
                Enqueue(new MoveStepAnim(e, px, py), Team.Enemy);
            }
            // claim where this member actually LANDS — after the move cap truncates the plan,
            // which can be short of plan.Path's end (or nowhere at all, in which case its
            // current tile is already covered by IsOccupiedByOther).
            if (lx != e.X || ly != e.Y) claimed.Add((lx, ly));
        }
        if (any)
        {
            BannerText = "CONTACT!"; BannerEnemy = true; BannerMax = BannerTimer = 1.0f;
            Fx.AddShake(3f);
            Audio.Play("over");
            // FUL-6 CRITICAL MASS — LINKED ACTIVATION rider ("they heard the guns"): from
            // mission 3 on, waking a real pod puts the NEAREST other pod with a dormant member
            // within LinkRange (closest member to closest member, Util.TileDist — a sound
            // radius) on the telegraphed Suspicious track, flagged in _linkedPods so the next
            // ResolveSuspicion pass confirms it to Alert EVEN UNSEEN. Exactly ONE pod per
            // activation call (the nearest); a linked pod can never chain (ResolveSuspicion
            // never calls ActivatePod) — but directly shooting/blundering into pod B still
            // links pod C (that routes through ActivatePod), which is correct: new gunfire,
            // new sound. Endless/Defend waves spawn already-Alert, so the rider is inert
            // there by construction. Zero RNG — the link fires off positions alone (CRN-safe).
            // The read never lies: a linked pod IS coming, with the existing 4.3 Suspicious
            // warning (the remainder of this turn + the turn-end beat; no scatter either way).
            if (podId >= 0 && _run != null && _run.Mission >= 3)
            {
                int bestPod = -1; float bestDist = float.MaxValue; Unit bestMember = null;
                foreach (var s in Enemies)
                {
                    if (!s.Alive || s.PodId != podId) continue;
                    foreach (var o in Enemies)
                    {
                        if (!o.Alive || o.PodId < 0 || o.PodId == podId || o.Alert != AlertLevel.Unaware) continue;
                        float d = Util.TileDist(s.X, s.Y, o.X, o.Y);
                        if (d <= LinkRange && d < bestDist) { bestDist = d; bestPod = o.PodId; bestMember = o; }
                    }
                }
                if (bestPod >= 0)
                {
                    SetPodSuspicious(bestPod);
                    _linkedPods.Add(bestPod);
                    Fx.PopText(bestMember.Pos + new Vector2(0, -30), "HEARD THE GUNS", Pal.Suspect, 16f);
                    // SetPodSuspicious's CONTACT? banner must not mask the wake itself — restore
                    // the CONTACT! banner and hang the link telegraph under it as the sub-line.
                    BannerText = "CONTACT!"; BannerEnemy = true; BannerMax = BannerTimer = 1.0f;
                    BannerSub = "a nearby pod is moving to the sound";
                }
            }
        }
    }

    /// 4.4: break squad concealment. The first aggressive action (or stepping too close)
    /// springs the ambush: the breaking shot gets the ambush bonus (FiredFromConcealment),
    /// and any pod already in sight wakes with the usual capped scatter. After this, the
    /// normal 4.3 alert-tier rules resume for the rest of the mission. Call BEFORE the
    /// action mutates state so the bonus is in place when Combat.Resolve reads it.
    /// W10 SUPPRESSOR: `target` + `suppressed` thread the SHOT's context through from the firing
    /// sites (IssueShoot and the overwatch reaction) — a suppressed shot still breaks squad
    /// concealment normally (the flag drops, the banner fires, the ambush bonus arms), but the
    /// seen-pod wake loop below NARROWS to the target's own pod: everyone else heard nothing they
    /// can place. Non-shot breaks (proximity, grenades, hacks, barrels) pass neither and keep the
    /// full wake. CheckPodActivation's post-concealment sight rules are untouched.
    public void BreakConcealment(Unit actor = null, Unit target = null, bool suppressed = false)
    {
        if (!SquadConcealed) return;
        SquadConcealed = false;
        if (actor != null) actor.FiredFromConcealment = true;   // only a deliberate first shot earns the bonus
        // W10 GHOST secondary: the bonus asks the squad to stay hidden through turn GhostTurns —
        // any break on an earlier turn (shot, proximity, hack: every path funnels here) blows it.
        if (Secondary == SecondaryKind.Ghost && _turnCount <= GhostTurns) SecondaryFailed = true;
        ShowBanner("AMBUSH!", false);
        Fx.AddShake(4f);
        Audio.Play("turn");
        // wake every pod a soldier can currently see (each pod activates once). A SUPPRESSED shot
        // narrows the wake to the TARGET's own pod (pod-less targets fall back to the full wake —
        // ungrouped hostiles are spawned already alert, so there is nothing to narrow to).
        bool narrow = suppressed && target != null && target.PodId >= 0;
        var seen = new HashSet<int>();
        foreach (var e in Enemies)
        {
            if (!e.Alive || e.Active || e.PodId < 0 || ClosestSightedDist(e) < 0) continue;
            if (narrow && e.PodId != target.PodId) continue;   // SUPPRESSOR: only the target's pod places the shot
            seen.Add(e.PodId);
        }
        if (narrow && actor != null)
            Fx.PopText(actor.Pos + new Vector2(0, -44), "SUPPRESSED SHOT", Pal.TxtDim, 15f);
        foreach (int pid in seen) ActivatePod(pid);
    }

    /// W10 INTEL CACHE pickup: bank the intel, clear the tile, and record the verb (ACTION MIX).
    void CollectIntelCache(Unit finder)
    {
        if (!CachePresent) return;
        CachePresent = false;
        int gain = Util.RandInt(CacheIntelMin, CacheIntelMax);
        if (_run != null) { _run.Intel += gain; Stats.RecordIntel(gain); }   // FUL-13 intel cash-flow
        Stats.RecordAction("INTEL");   // W2 verb telemetry: cache pickups visible in the flywheel
        var c = Util.TileCenter(CacheX, CacheY);
        Fx.PopText(c + new Vector2(0, -26), $"+{gain} INTEL", Pal.VipGold, 22f);
        Fx.Burst(c, Pal.VipGold, 18, 200f, 0.55f, 4f, true);
        Fx.Flash(c, Pal.VipGold, 24f, 0.16f, 0.5f);
        ShowBanner("INTEL CACHE SECURED", false);
        Audio.Play("select");
    }

    /// Balance fix: a hack/plant GOES LOUD. First it breaks concealment — which springs the
    /// ambush on any pod already in SIGHT (BreakConcealment). Then the bang additionally rouses
    /// dormant pods physically near the objective site (sx,sy) even with NO line of sight (a
    /// sound cue, within HackNoiseRange), each to full Alert with the usual 4.2-capped reveal-
    /// scatter. Works whether or not still concealed, so a later charge of a Sabotage run also
    /// rouses any pod that has since crept into earshot. (The main tempo lever that turns Hack
    /// into a multi-turn hold is the one-cycle-per-turn channel; this is the "wake the area" half.)
    void HackNoise(int sx, int sy)
    {
        if (SquadConcealed) BreakConcealment();   // first hack/plant springs the ambush + reveals
        var roused = new HashSet<int>();
        foreach (var e in Enemies)
            if (e.Alive && !e.Active && e.PodId >= 0
                && Util.TileDist(sx, sy, e.X, e.Y) <= HackNoiseRange) roused.Add(e.PodId);
        foreach (int pid in roused) ActivatePod(pid);   // wake + the usual 4.2-capped reveal-scatter
    }

    void UpdatePlayer()
    {
        if (AutoPlay) { AutoIdleGuard(); if (SmartPlay) SmartStep(); else AutoStep(); return; }

        CheckPodActivation();
        if (_anims.Count > 0) return;   // a pod just activated — let the scatter play

        // clear the end-turn confirmation if anything changed (action taken / reselect)
        int sig = AlivePlayers().Sum(p => p.ActionsLeft) * 8 + (Selected != null ? Players.IndexOf(Selected) : 0);
        if (sig != _lastSig) { EndTurnArmed = false; _lastSig = sig; }

        // keep selection valid
        if (Selected != null && !Selected.Alive) Selected = null;
        if (Selected == null || !Selected.CanAct)
        {
            var next = Players.FirstOrDefault(p => p.CanAct);
            if (next != null) Selected = next;   // (inside `next != null`, `next ?? Selected` was always `next`)
        }

        RecomputeMoveCost();
        UpdateHoverAndAim();
        HandlePlayerInput();
    }

    void RecomputeMoveCost()
    {
        if (Selected != null && Selected.Team == Team.Player && Selected.CanAct)
        {
            Func<int, int, bool> blocked = (x, y) => IsOccupiedByOther(x, y, Selected);
            MoveCost = Grid.CostMap(Selected.X, Selected.Y, blocked, out _cameFrom, Selected.MoveBudget * 2);
            ComputeThreat();
        }
        else { MoveCost = null; _cameFrom = null; Threat = null; _threatSig = 0; }
    }

    // ---------------- RESONANCE T2: the incoming-fire forecast ----------------
    // Cache key for the forecast. ComputeThreat runs off RecomputeMoveCost, which fires EVERY
    // frame of the player turn, and the forecast is ~(reachable tiles x hostiles) ComputeOdds
    // calls — far too much to redo 60x a second for a board that hasn't changed. The signature
    // folds in everything the forecast reads (selection, both rosters' positions/state, the
    // mutable terrain layers, the pref) so a real change always misses the cache and nothing
    // else ever does. 0 = "no valid cache".
    long _threatSig;
    readonly System.Diagnostics.Stopwatch _threatClock = new();

    long ThreatSignature()
    {
        unchecked
        {
            long h = 1469598103934665603L;
            void Mix(long v) { h = (h ^ v) * 1099511628211L; }
            Mix(ThreatPref);
            Mix(Selected == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Selected));
            if (Selected != null) { Mix(Selected.X * 31 + Selected.Y); Mix(Selected.ActionsLeft); Mix(Selected.MoveBudget);
                                    Mix((Selected.Hunkered ? 1 : 0) | (Selected.FiredThisTurn ? 2 : 0) | (Selected.MovedAfterFire ? 4 : 0) | (Selected.Hp << 4)); }
            Mix(Turn); Mix(SquadConcealed ? 1 : 0); Mix(Pressure);   // coarse catch-alls for turn-scoped combat state
            foreach (var e in Enemies)
            {
                if (!e.Alive) continue;
                Mix(e.X * 31 + e.Y);
                Mix((e.Active ? 1 : 0) | (e.Ammo > 0 ? 2 : 0) | (e.OnOverwatch ? 4 : 0) | (e.OwFocused ? 8 : 0)
                    | (e.Hp << 8) | (e.Routed << 16) | (e.Pinned << 20) | (e.Suppress << 24));
                Mix(e.OwDirX * 7 + e.OwDirY);
            }
            foreach (var p in Players) { if (!p.Alive) continue; Mix(p.X * 31 + p.Y); Mix(p.Hp); }   // crossfire reads squadmates
            // mutable terrain layers (cover can be chipped, smoke/fire tick, barrels blow up)
            for (int x = 0; x < Grid.W; x++)
                for (int y = 0; y < Grid.H; y++)
                    Mix((long)Grid.Tiles[x, y] | ((long)Grid.Height[x, y] << 3) | ((long)(Grid.Smoke[x, y] > 0 ? 1 : 0) << 6)
                        | ((long)(Grid.Fire[x, y] > 0 ? 1 : 0) << 7) | ((long)(Grid.Barrel[x, y] ? 1 : 0) << 8));
            return h == 0 ? 1 : h;
        }
    }

    /// The INCOMING-FIRE FORECAST (RESONANCE T2). For every tile the selected soldier can reach
    /// (plus the tile it stands on), answer the defensive question the board never answered:
    /// how many hostiles bear on it, how well the best of them shoots, how much damage that adds
    /// up to, whether standing there is a FLANK, and whether it sits in a reaction lane.
    ///
    /// TRUTHFULNESS: the numbers come from Combat.ComputeOdds with the mover TEMPORARILY placed on
    /// the candidate tile (the exact call Resolve would make if the enemy fired at it), gated by the
    /// same range + commanding-LoS test as Game.CanTarget. So the forecast can never disagree with
    /// the shot that actually happens. The mover's X/Y is restored in a finally.
    void ComputeThreat()
    {
        if (!ShowThreatPref || Selected == null || MoveCost == null) { Threat = null; _threatSig = 0; return; }

        long sig = ThreatSignature();
        if (sig == _threatSig && Threat != null) return;   // nothing the forecast reads has changed
        _threatSig = sig;
        _threatClock.Restart();

        var cells = new ThreatCell[Grid.W, Grid.H];
        Threat = cells;
        ThreatRebuilds++;

        // the caged Rescue captive is invulnerable until freed — nothing bears on it anywhere
        bool untouchable = Selected == Vip && CaptiveLocked;
        List<Unit> foes = null;
        if (!untouchable)
            foreach (var e in Enemies)
                if (e.Alive && e.Active && e.Ammo > 0 && e.Weapon != null)
                    (foes ??= new List<Unit>()).Add(e);
        if (foes == null) { _threatClock.Stop(); ThreatMs = _threatClock.Elapsed.TotalMilliseconds; return; }

        int ox = Selected.X, oy = Selected.Y;
        // Moving CLEARS two defensive states (MoveStepAnim.OnStart drops Hunkered;
        // OnUnitEnteredTile sets MovedAfterFire, ending EXPOSED BY FIRE), so a forecast for any
        // tile the soldier has to WALK to must model the post-move soldier or it lies about both.
        bool oHunk = Selected.Hunkered, oMaf = Selected.MovedAfterFire;
        try
        {
            for (int x = 0; x < Grid.W; x++)
                for (int y = 0; y < Grid.H; y++)
                {
                    bool here = x == ox && y == oy;
                    if (!here && MoveCost[x, y] <= 0) continue;

                    // place the mover on the candidate tile so ComputeOdds sees the real geometry
                    Selected.X = x; Selected.Y = y;
                    Selected.Hunkered = here && oHunk;
                    Selected.MovedAfterFire = here ? oMaf : true;
                    ref var c = ref cells[x, y];
                    int bestHit = 0; float bestScore = -1f;

                    for (int i = 0; i < foes.Count; i++)
                    {
                        var e = foes[i];
                        if (Util.TileDist(x, y, e.X, e.Y) > e.Weapon.MaxRange) continue;
                        bool commanding = Grid.HeightAt(e.X, e.Y) - Grid.HeightAt(x, y) >= 2;   // CanTarget's rule
                        if (!Grid.HasLineOfSight(e.X, e.Y, x, y, commanding)) continue;

                        var o = Combat.ComputeOdds(Grid, e, Selected);
                        if (c.Guns < 255) c.Guns++;
                        if (o.Flanked) c.Flanked = true;
                        if (o.CoverLevel == 0) c.Exposed = true;
                        // R2 FIX 2: the TRUE expectation of the damage roll — clean hit (uniform band,
                        // crit-rolled) PLUS the graze leg. This used to be hit% x mean(post-armor band),
                        // whose comment claimed crits (up) and "the graze floor (down)" cancelled. They
                        // don't: a graze deals max(1, reduce(DmgMin)) on a roll that would otherwise deal
                        // ZERO, so both omissions pushed the same way and the card read 31-44% low.
                        // Combat.ExpectedDamage is the single source of truth (THREATTEST measures it
                        // against real Resolve rolls); see its doc for what stays excluded and why.
                        c.ExpDmg += Combat.ExpectedDamage(Selected, o);
                        // "worst" gun = highest hit%, tie-broken by the bigger average bite
                        float score = o.HitChance * 1000f + (o.DmgMin + o.DmgMax);
                        if (score > bestScore) { bestScore = score; bestHit = o.HitChance; c.WorstCls = e.Cls; }

                        // a live reaction lane: mirrors Game.OnUnitEnteredTile's overwatch gate exactly
                        // (range + commanding LoS already checked above, plus the FOCUSED cone).
                        if (e.OnOverwatch && (!e.OwFocused || InOwCone(e, x, y))) c.Watched = true;
                    }
                    c.BestHit = (sbyte)Math.Min(bestHit, (int)sbyte.MaxValue);
                }
        }
        finally { Selected.X = ox; Selected.Y = oy; Selected.Hunkered = oHunk; Selected.MovedAfterFire = oMaf; }

        _threatClock.Stop();
        ThreatMs = _threatClock.Elapsed.TotalMilliseconds;
    }

    void UpdateHoverAndAim()
    {
        ShowOdds = false;
        PathPreview.Clear();
        // mouse -> board tile, through the (stable) picking camera so zoom/pan work
        var world = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), ViewCamera(false));
        HoverValid = Util.ScreenToTile(world, out HoverX, out HoverY);
        if (KbCursor) { HoverX = CurX; HoverY = CurY; HoverValid = Grid.InBounds(CurX, CurY); }

        Unit hovered = HoverValid ? UnitAt(HoverX, HoverY) : null;

        if (GrenadeMode)
        {
            GrenTx = HoverX; GrenTy = HoverY;
            GrenValid = HoverValid && Selected != null &&
                        Util.TileDist(Selected.X, Selected.Y, HoverX, HoverY) <= GrenadeRange;
            return;
        }

        if (ItemMode)
        {
            ItemTx = HoverX; ItemTy = HoverY;
            ItemValid = HoverValid && Selected != null &&
                        Util.TileDist(Selected.X, Selected.Y, HoverX, HoverY) <= ItemRange &&
                        ItemTargetOk(Selected, HoverX, HoverY);
            return;
        }

        if (ShoveMode)
        {
            // valid target = an alive enemy standing Chebyshev-adjacent to the selected soldier.
            ShoveTarget = (hovered != null && Selected != null) ? hovered : null;
            ShoveValid = Selected != null && ShoveTarget != null && ShoveTargetOk(Selected, ShoveTarget);
            return;
        }

        if (DragMode)
        {
            // valid target = an adjacent alive friendly with a legal landing tile (one step toward us).
            DragTarget = (hovered != null && Selected != null) ? hovered : null;
            DragValid = Selected != null && DragTarget != null && DragTargetOk(Selected, DragTarget);
            return;
        }

        if (VaultMode)
        {
            VaultTx = HoverX; VaultTy = HoverY;
            VaultValid = HoverValid && Selected != null && VaultTargetOk(Selected, HoverX, HoverY);
            return;
        }

        if (MarkMode)
        {
            MarkTarget = (hovered != null && Selected != null) ? hovered : null;
            MarkValid = Selected != null && MarkTarget != null && MarkTargetOk(Selected, MarkTarget);
            return;
        }

        if (GrappleMode)
        {
            GrappleTarget = (hovered != null && Selected != null) ? hovered : null;
            GrappleValid = Selected != null && GrappleTarget != null && GrappleTargetOk(Selected, GrappleTarget);
            return;
        }

        if (PinMode)
        {
            PinTarget = (hovered != null && Selected != null) ? hovered : null;
            PinValid = Selected != null && PinTarget != null && PinTargetOk(Selected, PinTarget);
            return;
        }

        if (AimMode)
        {
            if (hovered != null && hovered.Team == Team.Enemy && CanTarget(Selected, hovered))
                AimTarget = hovered;
            if (AimTarget != null && !AimTarget.Alive) AimTarget = FirstTargetFor(Selected);
            AimValid = AimTarget != null && CanTarget(Selected, AimTarget);
            // explosive-barrel target feedback: hovering a shootable barrel (no enemy under cursor)
            BarrelAimValid = hovered == null && CanShootBarrel(Selected, HoverX, HoverY);
            BarrelAimX = HoverX; BarrelAimY = HoverY;
            if (AimTarget != null)
            {
                ShowOdds = true;
                HoverOdds = Combat.ComputeOdds(Grid, Selected, AimTarget);
                // TEMPO: a rushed SECOND shot this turn lowers the displayed hit% by the same penalty
                // Resolve will apply, so the number the player sees stays truthful (perfect-info
                // contract). RUN&GUN's bonus shot + the GUNSLINGER perk are full aim — no penalty.
                if (Selected.FiredThisTurn && !Selected.RunGun && !Selected.HasPerk(Perk.Gunslinger))
                    HoverOdds.HitChance = Util.Clamp(HoverOdds.HitChance + SnapAim, 1, 99);
            }
            return;
        }

        // hovering an enemy we could shoot -> show odds
        if (Selected != null && Selected.CanAct && Selected.Ammo > 0 &&
            hovered != null && hovered.Team == Team.Enemy && CanTarget(Selected, hovered))
        {
            ShowOdds = true;
            HoverOdds = Combat.ComputeOdds(Grid, Selected, hovered);
            if (Selected.FiredThisTurn && !Selected.RunGun && !Selected.HasPerk(Perk.Gunslinger))   // TEMPO: a rushed 2nd shot shows its penalty (GUNSLINGER negates it)
                HoverOdds.HitChance = Util.Clamp(HoverOdds.HitChance + SnapAim, 1, 99);
        }
        // path preview to reachable floor
        else if (Selected != null && Selected.CanAct && MoveCost != null && HoverValid &&
                 Grid.IsFloor(HoverX, HoverY) && MoveCost[HoverX, HoverY] > 0)
        {
            int c = MoveCost[HoverX, HoverY];
            bool dash = c > Selected.MoveBudget;
            if (!dash || Selected.ActionsLeft >= 2)
                PathPreview = Grid.ReconstructPath(_cameFrom, Selected.X, Selected.Y, HoverX, HoverY);
        }
    }

    void HandlePlayerInput()
    {
        // keys
        if (Raylib.IsKeyPressed(KeyboardKey.Enter)) { RequestEndTurn(); return; }
        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) CycleSelection();
        if (Raylib.IsKeyPressed(KeyboardKey.One)) ToggleAim();
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) DoOverwatch();
        if (Raylib.IsKeyPressed(KeyboardKey.F)) DoFocusOverwatch();   // COUNTERPLAY: braced cone watch
        if (Raylib.IsKeyPressed(KeyboardKey.B)) DoBrace();            // UNDERTOW W2: disrupting interrupt watch
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) DoHunker();
        if (Raylib.IsKeyPressed(KeyboardKey.Four)) ToggleGrenade();
        if (Raylib.IsKeyPressed(KeyboardKey.Five)) DoAbility();
        if (Raylib.IsKeyPressed(KeyboardKey.Six)) ToggleItem();
        if (Raylib.IsKeyPressed(KeyboardKey.Eight)) ToggleShove();
        if (Raylib.IsKeyPressed(KeyboardKey.Seven)) ToggleDrag();
        if (Raylib.IsKeyPressed(KeyboardKey.Nine)) ToggleVault();
        if (Raylib.IsKeyPressed(KeyboardKey.H)) DoHack();
        if (Raylib.IsKeyPressed(KeyboardKey.E)) DoStabilize();   // FUL-7: stabilize an adjacent downed ally (T is the tag editor)
        if (Raylib.IsKeyPressed(KeyboardKey.G)) DoBeacon();          // UNDERTOW W6: deploy forward evac beacon (moved off B — collided with W2 BRACE)
        if (Raylib.IsKeyPressed(KeyboardKey.X)) DoExtract();
        if (Raylib.IsKeyPressed(KeyboardKey.R)) DoReload();
        // T1: [P] restarts the TRAINING OP from the top — the drill is the one place where
        // "just start over" must be one keystroke away. Drill-only, so it can never nuke a run.
        if (Mode == GameMode.Training && Raylib.IsKeyPressed(KeyboardKey.P)) { BeginTraining(); return; }
        if (Raylib.IsKeyPressed(KeyboardKey.V)) { ToggleShowAllVerbs(); return; }   // T1: SHOW ALL verbs (staging escape)
        if (Raylib.IsKeyPressed(KeyboardKey.T)) { OpenTagEditor(Selected); return; }

        // keyboard tile cursor: arrows / WASD move it, Space acts on it
        int cdx = 0, cdy = 0;
        if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W)) cdy = -1;
        else if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S)) cdy = 1;
        else if (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressed(KeyboardKey.A)) cdx = -1;
        else if (Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressed(KeyboardKey.D)) cdx = 1;
        if (cdx != 0 || cdy != 0) MoveCursor(cdx, cdy);
        if (Raylib.IsKeyPressed(KeyboardKey.Space) && HoverValid) { BoardAct(HoverX, HoverY); return; }
        if (KbCursor && Raylib.GetMouseDelta() != Vector2.Zero) KbCursor = false;  // mouse takes back over

        if (Raylib.IsMouseButtonPressed(MouseButton.Right)) { AimMode = false; SnapShot = false; GrenadeMode = false; ItemMode = false; ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false; return; }

        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            KbCursor = false;
            var m = Raylib.GetMousePosition();
            // HUD first (screen space — not affected by the board camera)
            if (Raylib.CheckCollisionPointRec(m, Hud.EndTurnRect)) { RequestEndTurn(); return; }
            foreach (var b in Hud.ActionButtons)
                if (b.Enabled && Raylib.CheckCollisionPointRec(m, b.Rect)) { DoAction(b.Id); return; }
            foreach (var c in Hud.RosterChips)
                if (Raylib.CheckCollisionPointRec(m, c.rect)) { SelectUnit(c.unit); return; }

            if (!HoverValid) return;
            BoardAct(HoverX, HoverY);
        }
    }

    // Resolve a board action on tile (hx,hy): throw / aim / select / fire / move.
    // Shared by mouse clicks and the keyboard cursor.
    void BoardAct(int hx, int hy)
    {
        if (!Grid.InBounds(hx, hy)) return;
        var hovered = UnitAt(hx, hy);

        if (GrenadeMode)
        {
            if (GrenValid) IssueGrenade(hx, hy);
            else GrenadeMode = false;
            return;
        }
        if (ItemMode)
        {
            if (ItemValid) IssueItem(hx, hy);
            else ItemMode = false;
            return;
        }
        if (ShoveMode)
        {
            if (hovered != null && Selected != null && ShoveTargetOk(Selected, hovered)) IssueShove(Selected, hovered);
            else ShoveMode = false;
            return;
        }
        if (DragMode)
        {
            if (hovered != null && Selected != null && DragTargetOk(Selected, hovered)) IssueDrag(hovered);
            else DragMode = false;
            return;
        }
        if (VaultMode)
        {
            if (Selected != null && VaultTargetOk(Selected, hx, hy)) IssueVault(hx, hy);
            else VaultMode = false;
            return;
        }
        if (MarkMode)
        {
            if (hovered != null && Selected != null && MarkTargetOk(Selected, hovered)) IssueMark(Selected, hovered);
            else MarkMode = false;
            return;
        }
        if (GrappleMode)
        {
            if (hovered != null && Selected != null && GrappleTargetOk(Selected, hovered)) IssueGrapple(Selected, hovered);
            else GrappleMode = false;
            return;
        }
        if (PinMode)
        {
            if (hovered != null && Selected != null && PinTargetOk(Selected, hovered)) IssuePin(Selected, hovered);
            else PinMode = false;
            return;
        }
        if (AimMode)
        {
            if (hovered != null && hovered.Team == Team.Enemy && CanTarget(Selected, hovered)) IssueShoot(hovered);
            else if (CanShootBarrel(Selected, hx, hy)) IssueShootBarrel(hx, hy);   // shoot an explosive barrel
            else { AimMode = false; SnapShot = false; }
            return;
        }
        if (hovered != null && hovered.Team == Team.Player) { SelectUnit(hovered); return; }
        if (hovered != null && hovered.Team == Team.Enemy && Selected != null &&
            Selected.CanAct && CanTarget(Selected, hovered)) { IssueShoot(hovered); return; }
        if (Selected != null && Selected.CanAct && MoveCost != null &&
            Grid.IsFloor(hx, hy) && MoveCost[hx, hy] > 0)
            IssueMove(hx, hy);
    }

    void MoveCursor(int dx, int dy)
    {
        if (!KbCursor)
        {
            KbCursor = true;
            CurX = Selected?.X ?? Grid.W / 2;
            CurY = Selected?.Y ?? Grid.H / 2;
        }
        CurX = Util.Clamp(CurX + dx, 0, Grid.W - 1);
        CurY = Util.Clamp(CurY + dy, 0, Grid.H - 1);
    }

    // ---------------- camera + pause ----------------
    void HandleCamera()
    {
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0)
        {
            _autoCamManual = true;
            var mouse = Raylib.GetMousePosition();
            var before = Raylib.GetScreenToWorld2D(mouse, ViewCamera(false));
            CamZoom = Util.Clamp(CamZoom + wheel * 0.12f, 1f, 2.4f);
            var after = Raylib.GetScreenToWorld2D(mouse, ViewCamera(false));
            CamPan += before - after;                 // keep the point under the cursor anchored
        }
        if (Raylib.IsMouseButtonDown(MouseButton.Middle))
        {
            _autoCamManual = true;
            CamPan -= Raylib.GetMouseDelta() / CamZoom;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.C)) { CamZoom = 1f; CamPan = Vector2.Zero; _autoCamManual = false; }

        if (CamZoom <= 1.001f) { CamZoom = 1f; CamPan = Vector2.Zero; }  // no pan when fully out
        else
        {
            CamPan.X = Util.Clamp(CamPan.X, -Cfg.BoardW * 0.5f, Cfg.BoardW * 0.5f);
            CamPan.Y = Util.Clamp(CamPan.Y, -Cfg.BoardH * 0.5f, Cfg.BoardH * 0.5f);
        }
    }

    // Auto-cam: gently lerps CamZoom/CamPan toward the focus unit each frame.
    // Focus = Selected on player turn; the currently-acting enemy on enemy turn.
    // Only runs when Display.AutoCam is on, we are NOT in autoplay, and the player
    // hasn't manually overridden (wheel/middle-drag). C-reset re-enables it.
    void UpdateAutoCam(float dt)
    {
        if (!Display.AutoCam || AutoPlay || _autoCamManual) return;
        if (Phase != Phase.PlayerTurn && Phase != Phase.EnemyTurn) return;

        // Determine the focus unit.
        Unit focus = null;
        if (Phase == Phase.PlayerTurn)
            focus = Selected;
        else if (Phase == Phase.EnemyTurn && _aiIdx < _aiUnits.Count)
            focus = _aiUnits[_aiIdx];

        if (focus == null || !focus.Alive) return;

        // Target zoom: modest 1.35x so the board edge is still visible.
        const float TargetZoom = 1.35f;
        // Target pan: shift so the focus unit's world position is at BoardCenter.
        // CamPan is added to BoardCenter as the camera Target, so to centre on
        // TileCenter(focus) we want CamPan = TileCenter(focus) - BoardCenter.
        var unitPos = Util.TileCenter(focus.X, focus.Y);
        var boardCenter = BoardCenter;
        var targetPan = unitPos - boardCenter;

        // Clamp pan so we never show blank space beyond the board.
        float halfW = Cfg.BoardW * 0.5f * (1f - 1f / TargetZoom);
        float halfH = Cfg.BoardH * 0.5f * (1f - 1f / TargetZoom);
        targetPan.X = Util.Clamp(targetPan.X, -halfW, halfW);
        targetPan.Y = Util.Clamp(targetPan.Y, -halfH, halfH);

        // Frame-rate-aware lerp (exp decay): ~6 units/s feel — smooth glide.
        float alpha = 1f - MathF.Exp(-dt * 6f);
        CamZoom = CamZoom + (TargetZoom - CamZoom) * alpha;
        CamPan.X = CamPan.X + (targetPan.X - CamPan.X) * alpha;
        CamPan.Y = CamPan.Y + (targetPan.Y - CamPan.Y) * alpha;
    }

    /// Where along a fader's track a mouse-x lands, 0..1 (the track is inset 8px each side —
    /// keep in sync with Hud.DrawVolSlider).
    static float VolFrac(Rectangle r, float mx) => Util.Clamp((mx - (r.X + 8f)) / MathF.Max(1f, r.Width - 16f), 0f, 1f);

    /// The phases the pause / settings card has a home in — the one gate `Update` reads before it
    /// looks at Escape. SETTINGS EVERYWHERE: this was the in-mission pair only, which made the
    /// card — the SOLE home of TEXT SIZE, COLORBLIND, BRIGHTNESS, GAMMA and the rest — unreachable
    /// from the first screen a player sees and from the screen where they deliberate (ROADMAP,
    /// "Left open by C5"). INTRO and BARRACKS now open the same card; SIGHTLINE_SETTINGSTEST pins
    /// each phase's round trip. Every OTHER overlay phase (Draft, SkirmishSetup, WarRoom, Codex,
    /// AudioCheck, the end cards) already takes Escape as its own BACK, so none is listed here.
    public static bool SettingsCardPhase(Phase p) =>
        p == Phase.PlayerTurn || p == Phase.EnemyTurn || p == Phase.Intro || p == Phase.Barracks;

    /// TRUE while the card sits over a live fight — the only time it PAUSES anything, the only
    /// time ABANDON is a coherent verb, and the only time the first row reads RESUME. On the intro
    /// and in the barracks the same card is a SETTINGS card: nothing is in flight, so the first
    /// row reads BACK and there is nothing to abandon (see DrawPause for the barracks reasoning).
    public bool CardInFight => Phase == Phase.PlayerTurn || Phase == Phase.EnemyTurn;

    /// The honest sentence under an ARMED quit, per phase AND per mode. In a campaign fight the
    /// checkpoint is the mission start (QUITTEST proves the quit path never rewrites it). In the
    /// BARRACKS that same checkpoint is the start of the mission just CLEARED — SetupMission is the
    /// only gameplay writer and the debrief has not reached it yet — so the debrief's picks are
    /// what a quit costs there. LAST STAND / SKIRMISH / DAILY / TRAINING never write save.json
    /// (SetupMission's checkpoint is Campaign-only; Game.Endless.cs), and their one persisted
    /// result (best wave, today's daily) is recorded at the END of the fight — so a quit mid-fight
    /// simply loses it. Review round 1: every sentence is <= 46 chars, because it is centred on
    /// the QUIT plate (x 660..980) inside a card whose right edge is x 1020 — 200 px of room each
    /// side of the plate's centre — and the CARD does not scale with TEXT SIZE while the sentence
    /// does: at the 120% level a 12px glyph is ~8.2 px wide, so 46 chars is ~376 px. The first
    /// drafts ran 74-75 chars and ~65 px past the card at 100%. SETTINGSTEST measures every
    /// sentence against Hud.PauseCard at all four shipped text sizes.
    public string QuitWarning =>
        Phase == Phase.Intro ? "nothing is in flight - your save is untouched"
        : Mode == GameMode.Endless ? "the stand ends here - its waves are not saved"
        : Mode == GameMode.Training ? "the drill is not saved - run it again any time"
        : Mode == GameMode.Skirmish ? (DailyMode ? "today's run is not recorded - retry any time"
                                                 : "nothing is saved - the fight simply ends here")
        : Phase == Phase.Barracks ? "the debrief is lost - the won mission restarts"
        : "the current mission restarts from its start";

    /// One Escape press, exactly as `Update` reads it: cancels a targeting mode first; otherwise
    /// toggles the card. Public so SIGHTLINE_SETTINGSTEST can press it from every phase.
    /// BARRACKS exception: while the ARMORY sub-screen is open, Escape belongs to it (HandleShopClick
    /// backs out one level) — the card must not steal the key the player is using to leave a dossier.
    /// Review round 1: that exception is gated on `!_shopDone` because HandleShopClick's own Escape
    /// read is only reachable under `if (!_shopDone)` — Enter used to leave ArmoryMode set after the
    /// requisition stopped drawing, and this line then swallowed Escape for the rest of the visit.
    /// The proceed path clears the flag too (ProceedFromShop); this predicate is the second lock.
    /// Refuses under AutoPlay in its own right (Update never calls it there; the flywheel must not
    /// see a card even if a future caller does).
    public void OnEscape()
    {
        if (AutoPlay) return;
        if (!SettingsCardPhase(Phase)) return;
        if (Phase == Phase.Barracks && ArmoryMode && !_shopDone && !Paused) return;
        if (AimMode || GrenadeMode || ItemMode || ShoveMode || MarkMode || GrappleMode || PinMode || DragMode || VaultMode) { AimMode = false; SnapShot = false; GrenadeMode = false; ItemMode = false; ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false; }
        else { Paused = !Paused; QuitArmed = false; }   // W5: closing the card disarms QUIT
    }

    /// The intro's SETTINGS door (button or [O]): opens the card the way Escape does, through the
    /// same disarm rule. Split out so the door and the key share one line.
    public void OpenSettings() { Paused = true; QuitArmed = false; Audio.Play("select"); }

    /// Which pause-card control a click at `m` lands on (the rects Hud.DrawPause published this
    /// frame), or null. Split from ActPause so a self-test can hit the SAME rect the mouse would.
    public string PauseHit(Vector2 m)
    {
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseQuit)) return "quit";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseResume)) return "resume";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseFullscreen)) return "fullscreen";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseWindow)) return "window";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseMute)) return "mute";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseShake)) return "shake";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseThreat)) return "threat";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseBright)) return "bright";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseGamma)) return "gamma";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseColorblind)) return "colorblind";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseAutoCam)) return "autocam";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseAnimSpeed)) return "animspeed";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseUiScale)) return "uiscale";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseCodex)) return "codex";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseAudio)) return "audio";
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseAbandon)) return "abandon";
        return null;
    }

    /// Run one pause-card control by id (see PauseHit). "quit" is the only one that does NOT
    /// disarm the quit confirm — every other control does, AND SO DOES A CLICK ON NOTHING (id null):
    /// that is the W5 contract as HandlePauseMenu always had it ("any left click that is not QUIT
    /// disarms"). Review round 1 put the null check back BELOW the disarm, where the first cut of
    /// this split had silently moved it above.
    public void ActPause(string id)
    {
        if (id == "quit") { RequestQuit(); return; }
        QuitArmed = false;   // any other pause control — or no control at all — disarms the confirm
        if (id == null) return;
        switch (id)
        {
            case "resume":     Paused = false; break;
            case "fullscreen": Display.ToggleFullscreen(); break;
            case "window":     Display.CycleSize(); break;
            case "mute":       Audio.ToggleMute(); break;
            case "shake":      Fx.ShakeOn = !Fx.ShakeOn; break;
            case "threat":     CycleThreatPref(); break;
            case "bright":     Display.CycleBrightness(); break;
            case "gamma":      Display.CycleGamma(); break;   // W9: true gamma (post-FX pass)
            case "colorblind": Display.ToggleColorblind(); break;
            case "autocam":    Display.ToggleAutoCam(); if (!Display.AutoCam) { CamZoom = 1f; CamPan = Vector2.Zero; } break;
            case "animspeed":  CycleAnimSpeed(); break;   // W5 comfort: playback pacing
            case "uiscale":    Display.CycleUiScale(); break;  // W5 comfort: UI text size
            case "codex":      BeginCodex(); break;   // W6: open the field manual (remembers this phase for BACK)
            case "audio":      BeginAudition(); break;   // A3: open AUDIO CHECK (same remember-and-restore contract as the codex)
            case "abandon":    AbandonRun(); break;
        }
    }

    /// The pause card's keyboard shortcuts, as ActPause ids. [Q] is QUIT (W5 — arms, then quits,
    /// the same two-step as the plate). [K] is FIELD MANUAL: the plate has carried a "K" hint since
    /// W6 and the README promised it, but nothing READ the key while the card was open (review
    /// round 1) — HandlePauseMenu now walks this table, and SETTINGSTEST asserts K is in it and
    /// maps to the manual. Add a shortcut here and it is bound; the test sees the same table.
    public static readonly KeyboardKey[] PauseKeys = { KeyboardKey.Q, KeyboardKey.K };
    public static string PauseKeyId(KeyboardKey k) => k == KeyboardKey.Q ? "quit" : k == KeyboardKey.K ? "codex" : null;

    void HandlePauseMenu()
    {
        var m = Raylib.GetMousePosition();
        foreach (var k in PauseKeys)
            if (Raylib.IsKeyPressed(k)) { ActPause(PauseKeyId(k)); return; }
        // A2 mix faders: a drag in progress owns the mouse until it is released, and only THEN
        // does the setting hit disk (Display.SetVol is live, CommitVol writes display.json).
        if (_volDrag >= 0)
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left)) { Display.SetVol(_volDrag, VolFrac(Hud.PauseVol[_volDrag], m.X)); return; }
            Display.CommitVol();
            _volDrag = -1;
            return;
        }
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        for (int i = 0; i < Hud.PauseVol.Length; i++)
            if (Raylib.CheckCollisionPointRec(m, Hud.PauseVol[i]))
            {
                _volDrag = i;
                Display.SetVol(i, VolFrac(Hud.PauseVol[i], m.X));
                return;
            }
        ActPause(PauseHit(m));
    }

    /// PAUSE-menu ABANDON — mode-aware teardown (W1 mode-seam). LAST STAND and SKIRMISH/DAILY route
    /// through their own enders (EndEndless / EndSkirmish), which record results, restore the clock
    /// seed after a daily, and set a mode-true lose title. The campaign abandon is deliberately
    /// CHECKPOINT-PRESERVING (unlike LoseRun): the run parks at its last checkpoint for CONTINUE —
    /// explicitly NO SaveGame.Delete, NO LossStreak bump, NO consolation salvage. It does close what
    /// the old inline handler leaked: the mission/run telemetry records (Combat.EndRun mirrors
    /// LoseRun: clears mission statics incl. Ai.Tier — W6 review LOW-3).
    void AbandonRun()
    {
        Paused = false;
        QuitArmed = false;
        if (TutStep >= 0 || _tutPending) CompleteTutorial();   // mirrors LoseRun: the onboarding ran
        if (Mode == GameMode.Training) { EndTraining(false); return; }   // T1: abandoning a drill is just leaving it
        if (Mode == GameMode.Skirmish) { EndSkirmish(false); return; }
        if (Mode == GameMode.Endless) { EndEndless(); return; }
        Combat.EndRun();
        LoseTitle = "RUN ABANDONED";
        LoseReason = "You called off the campaign. The checkpoint is kept - CONTINUE resumes it.";
        Phase = Phase.Lose;
        Audio.Play("lose");
        Stats.EndMission(false, _turnCount, AlivePlayers().Count(p => !p.IsVip),
                         Enemies.Count(e => !e.Alive), "abandoned");
        Stats.EndRun(false, _run.Mission - 1, "abandoned");
    }

    void DoAction(string id)
    {
        switch (id)
        {
            case "shoot": ToggleAim(); break;
            case "grenade": ToggleGrenade(); break;
            case "item": ToggleItem(); break;
            case "shove": ToggleShove(); break;
            case "drag": ToggleDrag(); break;
            case "vault": ToggleVault(); break;
            case "ability": DoAbility(); break;
            case "overwatch": DoOverwatch(); break;
            case "focusow": DoFocusOverwatch(); break;
            case "brace": DoBrace(); break;
            case "hunker": DoHunker(); break;
            case "hack": DoHack(); break;
            case "beacon": DoBeacon(); break;
            case "extract": DoExtract(); break;
            case "stabilize": DoStabilize(); break;   // FUL-7: freeze an adjacent downed ally's timer
            case "reload": DoReload(); break;
            case "showall": ToggleShowAllVerbs(); break;   // T1: the permanent verb-staging escape
        }
    }

    void SelectUnit(Unit u) { if (u != null && u.Downed) return; /* FUL-7: a downed body is never selectable */ Selected = u; AimMode = false; SnapShot = false; GrenadeMode = false; ItemMode = false; ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false; Audio.Play("select"); }

    void CycleSelection()
    {
        var actable = Players.Where(p => p.CanAct).ToList();
        if (actable.Count == 0) return;
        int idx = Selected != null ? actable.IndexOf(Selected) : -1;
        Selected = actable[(idx + 1) % actable.Count];
        AimMode = false;
        SnapShot = false;
        GrenadeMode = false;
        ItemMode = false;
        ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false;
        Audio.Play("select");
    }

    // FIRE: enter aim mode. TEMPO: the shot is 1 action and does NOT end the turn (one shot/turn).
    void ToggleAim() => EnterAim(false);

    void EnterAim(bool snap)
    {
        if (Selected == null || !Selected.CanAct || Selected.Ammo <= 0) return;

        // pressing the active variant again toggles aim OFF; switching variants (FIRE<->SNAP)
        // just re-flags the pending shot and keeps the current target lock.
        if (AimMode && SnapShot == snap) { AimMode = false; SnapShot = false; return; }
        if (AimMode) { SnapShot = snap; return; }      // already aiming: flip the variant, keep AimTarget
        if (!HasAnyTarget(Selected)) return;
        GrenadeMode = false;
        ItemMode = false;
        ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false;
        AimMode = true;
        SnapShot = snap;
        AimTarget = FirstTargetFor(Selected);
    }

    void ToggleGrenade()
    {
        if (Selected == null || !Selected.CanAct || Selected.Grenades <= 0) return;
        GrenadeMode = !GrenadeMode;
        if (GrenadeMode) { AimMode = false; SnapShot = false; ItemMode = false; ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false; }   // clear the snap variant too (review #3)
    }

    void IssueGrenade(int tx, int ty)
    {
        if (Selected == null || !Selected.CanAct || Selected.Grenades <= 0) return;
        if (Util.TileDist(Selected.X, Selected.Y, tx, ty) > GrenadeRange) return;
        if (SquadConcealed) BreakConcealment(Selected);  // 4.4: a thrown grenade breaks stealth
        Selected.Grenades--;
        Selected.ActionsLeft = 0;
        Enqueue(new GrenadeAnim(Selected, tx, ty), Team.Player);
        Stats.RecordAction("GRENADE");   // W2 verb telemetry
        _tutGrenade = true;              // T1: the drill's GRENADE lesson watches this
        GrenadeMode = false;
    }

    void ToggleItem()
    {
        if (Selected == null || !Selected.CanAct || Selected.ItemCharge <= 0 || Selected.Item == ItemKind.None) return;
        ItemMode = !ItemMode;
        if (ItemMode) { AimMode = false; SnapShot = false; GrenadeMode = false; ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false; }   // clear the snap variant too (review #3)
    }

    /// Whether a utility item can legally land on (tx,ty): barricade needs an empty
    /// floor tile; smoke/flash just need a tile in range.
    bool ItemTargetOk(Unit u, int tx, int ty)
    {
        if (u.Item == ItemKind.Barricade)
            return Grid.IsFloor(tx, ty) && !IsOccupiedByOther(tx, ty, u) && !EvacZone.Contains((tx, ty))
                   && !(HasTerminal && Terminal.x == tx && Terminal.y == ty)
                   && !(HasSabotage && SabotageSites.Contains((tx, ty)));
        return true;
    }

    void IssueItem(int tx, int ty)
    {
        var u = Selected;
        if (u == null || !u.CanAct || u.ItemCharge <= 0 || u.Item == ItemKind.None) return;
        if (Util.TileDist(u.X, u.Y, tx, ty) > ItemRange || !ItemTargetOk(u, tx, ty)) return;
        u.ItemCharge--;
        u.ActionsLeft = 0;            // a thrown item ends the turn, like a grenade
        Stats.RecordAction("ITEM:" + u.Item.ToString().ToUpperInvariant());   // W2 verb telemetry
        ItemMode = false;
        switch (u.Item)
        {
            case ItemKind.Smoke: Enqueue(new SmokeAnim(u, tx, ty), Team.Player); break;
            case ItemKind.Flash:
                if (SquadConcealed) BreakConcealment(u);   // 4.4: a flashbang is aggression
                Enqueue(new FlashAnim(u, tx, ty), Team.Player); break;
            case ItemKind.Incendiary:
                if (SquadConcealed) BreakConcealment(u);   // setting a fire is aggression
                Enqueue(new IncendiaryAnim(u, tx, ty), Team.Player); break;
            case ItemKind.Barricade:
                Grid.Tiles[tx, ty] = TileType.LowCover;
                Grid.SetCoverHp(tx, ty);
                Fx.Burst(Util.TileCenter(tx, ty), Pal.RGBA(150, 200, 120), 16, 150f, 0.6f, 4.5f);
                Fx.PopText(Util.TileCenter(tx, ty) + new Vector2(0, -22), "COVER UP", Pal.Good, 18f);
                Audio.Play("hunker");
                break;
        }
    }

    // ---- SHOVE (forced-movement verb) ----
    /// Can the selected soldier shove right now? Needs an action, no shove spent this turn,
    /// and at least one alive enemy standing within ShoveReach (Chebyshev <= 2).
    public bool CanShove(Unit u)
    {
        if (u == null || u.Team != Team.Player || !u.CanAct || u.ShovedThisTurn) return false;
        foreach (var e in Enemies)
            if (e.Alive && Util.ChebyDist(u.X, u.Y, e.X, e.Y) <= ShoveReach) return true;
        return false;
    }

    /// Is `target` a legal shove target for `u`? An alive enemy within ShoveReach tiles
    /// (Chebyshev <= 2, never the same tile), with the shover able + not having shoved yet.
    bool ShoveTargetOk(Unit u, Unit target)
    {
        if (u == null || target == null || !u.CanAct || u.ShovedThisTurn) return false;
        if (!target.Alive || target.Team != Team.Enemy) return false;
        int dx = target.X - u.X, dy = target.Y - u.Y;
        if (dx == 0 && dy == 0) return false;
        return Math.Abs(dx) <= ShoveReach && Math.Abs(dy) <= ShoveReach;   // within shove reach
    }

    void ToggleShove()
    {
        if (!CanShove(Selected)) return;
        ShoveMode = !ShoveMode;
        if (ShoveMode) { AimMode = false; SnapShot = false; GrenadeMode = false; ItemMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false; }
    }

    // ---- MARK (sharpshooter VERB): designate a foe; the whole squad shoots it better this round ----

    /// Is `target` a legal MARK target for sharpshooter `u`? An alive, visible (line-of-sight)
    /// enemy that isn't already marked. No reach cap (a designator works at range — its whole
    /// point), but it must be in LoS so it's a real sightline call.
    bool MarkTargetOk(Unit u, Unit target)
    {
        if (u == null || target == null || !u.CanAct || u.ActionsLeft < 1 || u.AbilityCd > 0) return false;
        if (!target.Alive || target.Team != Team.Enemy || target.Marked) return false;
        if (target.IsVip && CaptiveLocked) return false;             // can't mark the caged captive
        return Grid.HasLineOfSight(u.X, u.Y, target.X, target.Y);
    }

    void ToggleMark()
    {
        if (Selected == null || Selected.Ability != AbilityKind.Mark || !CanAbility(Selected)) return;
        MarkMode = !MarkMode;
        if (MarkMode) { AimMode = false; SnapShot = false; GrenadeMode = false; ItemMode = false; ShoveMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false; }
    }

    /// Designate `target`: set Unit.Marked so Combat.ComputeOdds gives EVERY squad member +MarkAim/
    /// +MarkCrit vs it (Combat.cs). Costs 1 action + the ability charge; does NOT end the turn (the
    /// sharpshooter can still fire). The mark clears at the marker's next turn (StartPlayerTurn) or
    /// when the foe dies. Pinning a foe is aggression -> breaks concealment + wakes its pod.
    void IssueMark(Unit u, Unit target)
    {
        if (!MarkTargetOk(u, target)) { MarkMode = false; return; }
        if (SquadConcealed) BreakConcealment(u);   // calling out a target gives the squad away
        target.Marked = true;
        // HEADHUNTER fork: this marker ALSO paints squad-wide +crit (only a Headhunter's mark does).
        target.MarkedByHeadhunter = u.HasSpec(Spec.Headhunter);
        _markedBy = u;                              // remember who marked, to clear it on their next turn
        u.AbilityCd = Unit.AbilityCooldownFor(u.Ability); u.ActionsLeft = Math.Max(0, u.ActionsLeft - 1);
        Stats.RecordAction("MARK");   // W2 verb telemetry
        if (!target.Active) ActivatePod(target.PodId);
        Fx.PopText(target.Pos + new Vector2(0, -34), "MARKED", Pal.Foe, 18f);
        Fx.PopText(u.Pos + new Vector2(0, -34), "MARK", Pal.Good, 16f);
        Fx.Burst(target.Pos, Pal.Foe, 10, 120f, 0.4f, 3f);
        Audio.Play("over");
        MarkMode = false; ShoveMode = false; GrappleMode = false;
    }

    /// Clear every MARK on the board (called at the marker's next player-turn start, so a mark
    /// lasts through the enemy turn — the focus-fire window — then expires).
    void ClearMarks()
    {
        foreach (var e in Enemies) { e.Marked = false; e.MarkedByHeadhunter = false; }
        _markedBy = null;
    }

    // ---- GRAPPLE (assault VERB): yank a nearby foe 1 tile toward you, out of its cover ----

    /// Is `target` a legal GRAPPLE target for assault `u`? An alive enemy within GrappleReach
    /// (Chebyshev) that hasn't been repositioned this turn (shares the SHOVE budget). Reuses the
    /// ShoveAnim, so it's anti-loop-bounded the same way.
    bool GrappleTargetOk(Unit u, Unit target)
    {
        if (u == null || target == null || !u.CanAct || u.ShovedThisTurn || u.AbilityCd > 0) return false;
        if (!target.Alive || target.Team != Team.Enemy) return false;
        if (target.IsVip && CaptiveLocked) return false;
        int dx = target.X - u.X, dy = target.Y - u.Y;
        if (dx == 0 && dy == 0) return false;
        int reach = GrappleReachFor(u);   // JUGGERNAUT fork: adjacent-only (1); else GrappleReach (2)
        return Math.Abs(dx) <= reach && Math.Abs(dy) <= reach;
    }

    void ToggleGrapple()
    {
        if (Selected == null || Selected.Ability != AbilityKind.Grapple || !CanAbility(Selected)) return;
        GrappleMode = !GrappleMode;
        if (GrappleMode) { AimMode = false; SnapShot = false; GrenadeMode = false; ItemMode = false; ShoveMode = false; MarkMode = false; PinMode = false; DragMode = false; VaultMode = false; }
    }

    /// Yank `target` ONE tile TOWARD the assault (pull direction = sign(u - target)), reusing
    /// ShoveAnim with the inverted vector. Pulls a foe out of its cover into the open. Costs 1
    /// action + the ability charge; does NOT end the turn. Uses ShovedThisTurn as the per-turn
    /// budget (shared with SHOVE) so a soldier can't loop reposition verbs.
    void IssueGrapple(Unit u, Unit target)
    {
        if (!GrappleTargetOk(u, target)) { GrappleMode = false; return; }
        // direction the target MOVES = toward the assault (one tile closer)
        int dx = Math.Sign(u.X - target.X), dy = Math.Sign(u.Y - target.Y);
        u.AbilityCd = Unit.AbilityCooldownFor(u.Ability); u.ShovedThisTurn = true; u.ActionsLeft = Math.Max(0, u.ActionsLeft - 1);
        Stats.RecordAction("GRAPPLE");   // W2 verb telemetry
        if (SquadConcealed) BreakConcealment(u);   // a grapple is aggression
        if (!target.Active) ActivatePod(target.PodId);
        Fx.PopText(target.Pos + new Vector2(0, -34), "GRAPPLED", Pal.Friend, 18f);
        Enqueue(new ShoveAnim(u, target, dx, dy), Team.Player);
        // BREACHER fork: the grapple also STAGGERS — the yanked foe loses overwatch + hunker and takes
        // chip damage (the ShoveAnim already clears OnOverwatch/Hunkered on a successful slide, but
        // Breacher guarantees it even on a blocked grapple + adds the chip damage). Inert on Spec.None.
        if (u.HasSpec(Spec.Breacher) && target.Alive)
        {
            target.OnOverwatch = false; target.Hunkered = false;
            Fx.PopText(target.Pos + new Vector2(0, -18), "STAGGER", Pal.Foe, 16f);
            EnvDamage(target, Combat.ShoveCollisionDamage, "STAGGER", Pal.Foe);
        }
        GrappleMode = false; ShoveMode = false; MarkMode = false;
    }

    /// Shove an adjacent enemy 1 tile directly away (soldier -> target direction). Costs 1
    /// action, does NOT end the turn, once per soldier per turn. The ShoveAnim resolves the
    /// slide (clear destination -> move + break the target's overwatch/hunker) or the collision
    /// (blocked -> small damage + stagger) when it becomes the active anim.
    void IssueShove(Unit u, Unit target)
    {
        if (!ShoveTargetOk(u, target)) { ShoveMode = false; return; }
        int dx = Math.Sign(target.X - u.X), dy = Math.Sign(target.Y - u.Y);
        u.ActionsLeft = Math.Max(0, u.ActionsLeft - 1);   // 1 action; never ends the turn
        u.ShovedThisTurn = true;                          // one shove per soldier per turn (anti-loop)
        Stats.RecordAction("SHOVE");                      // W2 verb telemetry (review fix: no invisible verbs)
        // shoving a dormant pod is aggression -> it wakes (mirrors a shot revealing a pod).
        if (!target.Active) ActivatePod(target.PodId);
        Enqueue(new ShoveAnim(u, target, dx, dy), Team.Player);
        ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false;
    }

    /// FUL-6 FIELD DRILLS rework (the FUL-5 verdict consumed — REWORK, not retire): the old proc
    /// (a SECOND drag/vault by one soldier in one turn) was self-consuming — a legal drag lands
    /// its target at Cheby-1, which is un-draggable (DragTargetOk's toward-tile rule), so the
    /// geometry the second use needs is destroyed by the first; measured 0 procs across every
    /// batch, vaults 0. New effect: *a DRAG or VAULT drills the soldier forward — +1 tile of
    /// movement for the rest of that turn* (MoveBudget +2 half-steps; Combat.FieldCraftLimit
    /// stays 2/turn — COMBATTEST's fieldDrills pins untouched). PROC honesty: RecordProc fires
    /// HERE at the grant site — the effect deterministically exists once granted (the TRR
    /// rout-start precedent). Once per soldier per turn (DrilledThisTurn, reset in BeginTurn).
    void GrantFieldDrill(Unit u)
    {
        if (!HasBoon(Boon.FieldDrills) || u.DrilledThisTurn) return;
        u.DrilledThisTurn = true;
        Stats.RecordProc("FDR");
        Fx.PopText(u.Pos + new Vector2(0, -46), "DRILLED +1 MOVE", Pal.Good, 15f);
        RecomputeMoveCost();   // the surplus tile must appear in the move overlay immediately
    }

    // ---- DRAG (FIELD CRAFT W1, universal): pull an adjacent ALLY one tile toward you ----
    /// Can the selected soldier DRAG right now? Needs an action, no drag spent this turn, and at
    /// least one adjacent (Chebyshev==1) alive friendly with a legal landing tile (one step toward us).
    public bool CanDrag(Unit u)
    {
        // W10: per-turn COUNTER vs Combat.FieldCraftLimit (1; FIELD DRILLS boon 2) instead of a bool
        if (u == null || u.Team != Team.Player || !u.CanAct || u.ActionsLeft < 1
            || u.DragsThisTurn >= Combat.FieldCraftLimit(u)) return false;
        foreach (var a in Players)
            if (DragTargetOk(u, a)) return true;
        return false;
    }

    /// Is `ally` a legal DRAG target for `u`? An alive friendly (incl. VIP/freed captive) within
    /// Chebyshev DragReach (1..2), not self, where the tile one step CLOSER to the dragger
    /// (dir = sign(u - ally)) is in-bounds floor + unoccupied + NOT the dragger's own tile. A
    /// Chebyshev-1 ally fails (its only toward-tile is the dragger) — by design, you can't pull
    /// someone already beside you; a Chebyshev-2 ally is pulled to the tile 1 away (legal + useful).
    bool DragTargetOk(Unit u, Unit ally)
    {
        if (u == null || ally == null || !u.CanAct || u.ActionsLeft < 1
            || u.DragsThisTurn >= Combat.FieldCraftLimit(u)) return false;
        if (ally == u || !ally.Alive || ally.Team != Team.Player) return false;
        if (ally.IsVip && CaptiveLocked) return false;          // caged captive is immovable until freed (mirrors Mark/Grapple/Pin/Extract)
        int dx = ally.X - u.X, dy = ally.Y - u.Y;
        if (dx == 0 && dy == 0) return false;
        if (Math.Abs(dx) > DragReach || Math.Abs(dy) > DragReach) return false;   // within drag reach (Chebyshev<=2)
        int lx = ally.X + Math.Sign(u.X - ally.X), ly = ally.Y + Math.Sign(u.Y - ally.Y);   // one step toward the dragger
        if (lx == u.X && ly == u.Y) return false;                        // never onto the dragger's own tile (a Chebyshev-1 ally hits this)
        return Grid.IsFloor(lx, ly) && !IsOccupiedByOther(lx, ly, ally);
    }

    void ToggleDrag()
    {
        if (!CanDrag(Selected)) return;
        DragMode = !DragMode;
        if (DragMode) { AimMode = false; SnapShot = false; GrenadeMode = false; ItemMode = false; ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; VaultMode = false; }
    }

    /// Pull `ally` ONE tile toward the dragger (move dir = sign(u - ally)), reusing ShoveAnim with the
    /// friendly target — the slide routes the dragged unit's arrival through OnUnitEnteredTile
    /// (overwatch/bleed/fire/concealment-reveal apply like any move). Costs 1 action; does NOT end the
    /// turn; once/soldier/turn (DraggedThisTurn). Moving an ally is not aggression -> does NOT break
    /// concealment, but if the ally lands within RevealRange of an active foe the normal reveal fires.
    void IssueDrag(Unit ally)
    {
        if (!DragTargetOk(Selected, ally)) { DragMode = false; return; }
        var u = Selected;
        int dx = Math.Sign(u.X - ally.X), dy = Math.Sign(u.Y - ally.Y);   // direction the ally MOVES (toward us)
        u.ActionsLeft = Math.Max(0, u.ActionsLeft - 1);   // 1 action; never ends the turn
        u.DragsThisTurn++;                                // counted vs Combat.FieldCraftLimit (anti-loop)
        GrantFieldDrill(u);                               // FUL-6: FIELD DRILLS +1-move drill (proc at grant)
        Stats.RecordAction("DRAG");                       // W2 verb telemetry
        Fx.PopText(ally.Pos + new Vector2(0, -32), "DRAG", Pal.Friend, 17f);
        Fx.Burst(ally.Pos, Pal.Friend, 8, 100f, 0.35f, 2.5f);
        Audio.Play("move");
        Enqueue(new ShoveAnim(u, ally, dx, dy), Team.Player);
        DragMode = false; ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; VaultMode = false;
    }

    // ---- VAULT (FIELD CRAFT W1, universal): leap an adjacent cover tile to the floor on its far side ----
    /// Can the selected soldier VAULT right now? Needs an action, no vault spent this turn, and at least
    /// one legal vault landing tile (a 2-step straight hop over a cover tile to empty floor).
    public bool CanVault(Unit u)
    {
        // W10: per-turn COUNTER vs Combat.FieldCraftLimit (1; FIELD DRILLS boon 2) instead of a bool
        if (u == null || u.Team != Team.Player || !u.CanAct || u.ActionsLeft < 1
            || u.VaultsThisTurn >= Combat.FieldCraftLimit(u)) return false;
        for (int sx = -1; sx <= 1; sx++)
            for (int sy = -1; sy <= 1; sy++)
            {
                if (sx == 0 && sy == 0) continue;
                if (VaultTargetOk(u, u.X + sx * 2, u.Y + sy * 2)) return true;
            }
        return false;
    }

    /// Is (tx,ty) a legal VAULT landing for `u`? Exactly 2 tiles away in a straight line (orthogonal or
    /// diagonal), the single tile between is a COVER tile (Low/High — not floor/barrel), and the landing
    /// is in-bounds, floor, and unoccupied.
    bool VaultTargetOk(Unit u, int tx, int ty)
    {
        if (u == null || !u.CanAct || u.ActionsLeft < 1
            || u.VaultsThisTurn >= Combat.FieldCraftLimit(u)) return false;
        int dx = tx - u.X, dy = ty - u.Y;
        // must be a straight 2-tile hop (ortho: (±2,0)/(0,±2); diag: (±2,±2))
        bool straight = (Math.Abs(dx) == 2 && dy == 0) || (dx == 0 && Math.Abs(dy) == 2) || (Math.Abs(dx) == 2 && Math.Abs(dy) == 2);
        if (!straight) return false;
        int mx = u.X + Math.Sign(dx), my = u.Y + Math.Sign(dy);          // the tile we vault over
        if (!Grid.IsCover(mx, my)) return false;                         // must clear an actual cover tile
        return Grid.IsFloor(tx, ty) && !IsOccupiedByOther(tx, ty, u);
    }

    void ToggleVault()
    {
        if (!CanVault(Selected)) return;
        VaultMode = !VaultMode;
        if (VaultMode) { AimMode = false; SnapShot = false; GrenadeMode = false; ItemMode = false; ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; }
    }

    /// Hop the selected soldier to the landing tile (a single MoveStepAnim so arrival routes through
    /// OnUnitEnteredTile — overwatch/concealment(RevealRange)/bleed/fire all apply). Costs 1 action;
    /// does NOT end the turn; once/soldier/turn (VaultedThisTurn). A vault is a positional move, not a
    /// shot, so it doesn't break concealment unless it lands within RevealRange of an active foe.
    void IssueVault(int tx, int ty)
    {
        if (!VaultTargetOk(Selected, tx, ty)) { VaultMode = false; return; }
        var u = Selected;
        u.ActionsLeft = Math.Max(0, u.ActionsLeft - 1);   // 1 action; never ends the turn
        u.VaultsThisTurn++;                               // counted vs Combat.FieldCraftLimit (anti-loop)
        GrantFieldDrill(u);                               // FUL-6: FIELD DRILLS +1-move drill (proc at grant)
        Stats.RecordAction("VAULT");                      // W2 verb telemetry (review fix: no invisible verbs)
        Fx.PopText(u.Pos + new Vector2(0, -32), "VAULT", Pal.Good, 17f);
        Fx.Burst(u.Pos, Pal.Good, 8, 110f, 0.35f, 2.5f);
        Enqueue(new MoveStepAnim(u, tx, ty), Team.Player);   // A2: the footfall is per-tile now (MoveStepAnim.OnStart)
        VaultMode = false; ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false;
    }

    void IssueMove(int tx, int ty)
    {
        if (MoveCost == null) return;
        int c = MoveCost[tx, ty];
        if (c <= 0) return;
        int need = c <= Selected.MoveBudget ? 1 : 2;
        bool blitz = Selected.Blitz;
        bool slip = Selected.Slipstreaming;                 // ranger SLIPSTREAM: this move is silent (no overwatch)
        // SLIPSTREAM action cost: the standard reposition is a DISCOUNTED one-action move (silent, never
        // a 2-action dash). PATHFINDER (Ranger fork) makes it TRULY FREE (0 actions) — that strictly-cheaper
        // action-economy gain is the fork's load-bearing differentiator (not just a faster cooldown).
        int slipCost = Selected.HasSpec(Spec.Pathfinder) ? 0 : 1;
        int cost = slip ? slipCost : (blitz ? Math.Max(0, need - 1) : need);   // Blitz: one action cheaper
        if (cost > Selected.ActionsLeft) return;
        var path = Grid.ReconstructPath(_cameFrom, Selected.X, Selected.Y, tx, ty);
        if (path.Count == 0) return;
        Selected.ActionsLeft -= cost;
        if (blitz) Selected.Blitz = false;
        if (slip) _slipDest = (tx, ty);                     // mark the silent move's destination (cleared on arrival)
        foreach (var (px, py) in path) Enqueue(new MoveStepAnim(Selected, px, py), Team.Player);
        Stats.RecordAction("MOVE");   // W2 verb telemetry (no-op unless the balance harness)
        AimMode = false;
        PathPreview.Clear();
        _tutMoved = true;   // A2: no cue here — MoveStepAnim.OnStart plays one footfall PER TILE
    }

    void IssueShoot(Unit target)
    {
        if (Selected == null || !Selected.CanAct || Selected.Ammo <= 0) return;
        if (!CanTarget(Selected, target)) return;
        // 4.4: the ambush shot springs the trap. W10 SUPPRESSOR: thread the shot's target through —
        // a suppressed ambush wakes only the target's own pod (BreakConcealment narrows the loop).
        if (SquadConcealed) BreakConcealment(Selected, target, Selected.HasMod(WeaponMod.Suppressor));
        Selected.Ammo--;
        // TEMPO action cost: a shot costs 1 action and does NOT end the turn — the soldier keeps its
        // second action to REPOSITION (duck into cover / break LoS), take a rushed FOLLOW-UP shot, or
        // a support action. The FIRST shot each turn is full aim; a SECOND shot the same turn is
        // "rushed" at the SnapAim penalty (so the 2-shots/turn DPS ceiling matches the old double-snap,
        // but the new bet is "duck vs double-tap"). RUN&GUN is the Assault's free bonus shot (full aim).
        int aimMod = 0;
        if (Selected.RunGun) { Selected.RunGun = false; Selected.ActionsLeft = Math.Max(0, Selected.ActionsLeft - 1); }
        else
        {
            // a rushed follow-up shot takes the SnapAim penalty — UNLESS the soldier has GUNSLINGER
            // (TEMPO wave 5: the double-tap build fires its 2nd shot at full aim).
            if (Selected.FiredThisTurn && !Selected.HasPerk(Perk.Gunslinger)) aimMod = SnapAim;
            Selected.FiredThisTurn = true;
            Selected.MovedAfterFire = false;   // HORIZON: fired-and-stationary => exposed until we move
            Selected.ActionsLeft = Math.Max(0, Selected.ActionsLeft - 1);
        }
        var res = Combat.Resolve(Grid, Selected, target, aimMod);
        Selected.Steady = false;                         // braced shot consumed
        Selected.FiredFromConcealment = false;           // ambush bonus is for this one shot only
        Enqueue(new ShotAnim(Selected, target, res), Team.Player);
        Stats.RecordAction("SHOOT");   // W2 verb telemetry
        if (!target.Active) ActivatePod(target.PodId);   // gunfire reveals the pod
        AimMode = false;
        SnapShot = false;
        _tutShot = true;
    }

    // ── shootable explosive barrels (hazards) ──────────────────────────────────
    public bool BarrelAimValid;            // hovering a shootable barrel while aiming (Renderer reticle)
    public int BarrelAimX, BarrelAimY;

    /// A soldier can shoot a barrel it has a clear LoS to, within weapon range, with ammo + an action.
    public bool CanShootBarrel(Unit u, int x, int y)
    {
        if (u == null || !u.CanAct || u.Ammo <= 0) return false;
        if (!Grid.IsBarrel(x, y)) return false;
        if (Util.TileDist(u.X, u.Y, x, y) > u.Weapon.MaxRange) return false;
        return Grid.HasLineOfSight(u.X, u.Y, x, y);
    }

    /// Fire at a barrel tile: a tracer flies over and detonates it (BarrelShotAnim -> DetonateBarrel).
    /// Mirrors IssueShoot's action-cost model (RUN&GUN / SNAP / aimed-ends-turn) so it's a real shot.
    void IssueShootBarrel(int bx, int by)
    {
        if (!CanShootBarrel(Selected, bx, by)) return;
        if (SquadConcealed) BreakConcealment(Selected);   // shooting a barrel is going loud
        Selected.Ammo--;
        // TEMPO: 1 action, no end-turn (mirrors IssueShoot). A 2nd shot/turn is a rushed follow-up.
        if (Selected.RunGun) { Selected.RunGun = false; Selected.ActionsLeft = Math.Max(0, Selected.ActionsLeft - 1); }
        else                 { Selected.FiredThisTurn = true; Selected.MovedAfterFire = false; Selected.ActionsLeft = Math.Max(0, Selected.ActionsLeft - 1); }
        Selected.Steady = false;
        Selected.FiredFromConcealment = false;
        Enqueue(new BarrelShotAnim(Selected, bx, by), Team.Player);
        Stats.RecordAction("BARREL");   // W2 verb telemetry
        AimMode = false; SnapShot = false; _tutShot = true;
    }

    void DoOverwatch()
    {
        if (Selected == null || !Selected.CanAct || Selected.Ammo <= 0) return;
        if (Selected.HasStatus(StatusKind.Disoriented))
        { Fx.PopText(Selected.Pos + new Vector2(0, -30), "DISORIENTED", Pal.Foe, 16f); return; }
        Selected.OnOverwatch = true;
        Selected.ActionsLeft = 0;
        Stats.RecordAction("OVERWATCH");   // W2 verb telemetry
        Fx.PopText(Selected.Pos + new Vector2(0, -30), "OVERWATCH", Pal.Accent, 18f);
        Audio.Play("over");
        AimMode = false;
        _tutOver = true;
    }

    /// COUNTERPLAY — FOCUSED overwatch: brace a 90-degree kill-lane toward the aimed tile instead of a
    /// wide watch. Reacts only inside the cone but with +FocusOwAim (a braced shot). The direction is the
    /// current cursor/hover tile; if that gives no usable direction, it orients toward the nearest visible
    /// enemy; if there's still none it falls back to a plain WIDE watch so the action is never wasted.
    void DoFocusOverwatch()
    {
        if (Selected == null || !Selected.CanAct || Selected.Ammo <= 0) return;
        if (Selected.HasStatus(StatusKind.Disoriented))
        { Fx.PopText(Selected.Pos + new Vector2(0, -30), "DISORIENTED", Pal.Foe, 16f); return; }

        int dx = 0, dy = 0;
        if (HoverValid && (HoverX != Selected.X || HoverY != Selected.Y))
        { dx = HoverX - Selected.X; dy = HoverY - Selected.Y; }
        else
        {
            // no aimed tile -> orient toward the nearest visible enemy
            Unit near = null; int nd = int.MaxValue;
            foreach (var e in Enemies)
            {
                if (!e.Alive || !e.Active) continue;
                int d = Util.ChebyDist(Selected.X, Selected.Y, e.X, e.Y);
                if (d < nd && Grid.HasLineOfSight(Selected.X, Selected.Y, e.X, e.Y)) { nd = d; near = e; }
            }
            if (near != null) { dx = near.X - Selected.X; dy = near.Y - Selected.Y; }
        }

        Selected.OnOverwatch = true;
        Selected.ActionsLeft = 0;
        if (dx == 0 && dy == 0)
        {
            // truly no direction -> a plain wide watch (never waste the action)
            Selected.OwFocused = false;
            Stats.RecordAction("OVERWATCH");   // W2 verb telemetry (the fallback IS a wide watch)
            Fx.PopText(Selected.Pos + new Vector2(0, -30), "OVERWATCH", Pal.Accent, 18f);
        }
        else
        {
            Selected.OwFocused = true;
            Selected.OwDirX = dx; Selected.OwDirY = dy;
            Stats.RecordAction("FOCUS");       // W2 verb telemetry
            Fx.PopText(Selected.Pos + new Vector2(0, -30), "FOCUS", Pal.VipGold, 18f);
        }
        Audio.Play("over");
        AimMode = false;
        _tutOver = true;
    }

    /// UNDERTOW W2 — BRACE: the INTERRUPT half of the reaction economy. Instead of a lethal overwatch,
    /// the soldier holds a DISRUPTING reaction: its reaction shot deals reduced damage but, on a hit,
    /// STAGGERS the mover (zeroes its remaining actions this turn -> its post-move offense is denied).
    /// A behind player trades a kill for tempo — the earnable comeback lever. Rides the OnOverwatch
    /// plumbing (threat map, ReactedThisTurn one-reaction cap); the reaction site reads OwBrace.
    void DoBrace()
    {
        if (Selected == null || !Selected.CanAct || Selected.Ammo <= 0) return;
        if (Selected.HasStatus(StatusKind.Disoriented))
        { Fx.PopText(Selected.Pos + new Vector2(0, -30), "DISORIENTED", Pal.Foe, 16f); return; }
        Selected.OnOverwatch = true;
        Selected.OwBrace = true;
        Selected.OwFocused = false;   // brace is a wide disrupting watch, not a cone
        Selected.ActionsLeft = 0;
        Stats.RecordAction("BRACE");   // W2 verb telemetry
        Fx.PopText(Selected.Pos + new Vector2(0, -30), "BRACE", Pal.Good, 18f);
        Audio.Play("over");
        AimMode = false;
        _tutOver = true;
    }

    /// True when tile (tx,ty) lies inside watcher w's braced 90-degree overwatch cone (centre = OwDir).
    public bool InOwCone(Unit w, int tx, int ty) => InConeDir(w.X, w.Y, w.OwDirX, w.OwDirY, tx, ty);

    /// The cone test with an EXPLICIT origin + direction (W2: shared by InOwCone and the
    /// autopilot's FOCUS probe, which must test a candidate cone BEFORE committing OwDir).
    public static bool InConeDir(int ox, int oy, int dirX, int dirY, int tx, int ty)
    {
        int tox = tx - ox, toy = ty - oy;
        if (tox == 0 && toy == 0) return true;
        float dl = MathF.Sqrt(dirX * (float)dirX + dirY * (float)dirY);
        if (dl < 0.01f) return true;   // no direction (defensive) -> behave as a wide watch
        float tl = MathF.Sqrt(tox * (float)tox + toy * (float)toy);
        float dot = (dirX * tox + dirY * toy) / (dl * tl);
        return dot >= 0.70710678f;     // within +-45 degrees of the cone centre (90-degree arc)
    }

    /// W2 — autopilot FOCUS issuer: a focused overwatch with an EXPLICIT lane direction. The
    /// interactive DoFocusOverwatch derives its cone from the hover tile, which doesn't exist
    /// headless; the smart bot computes the approach lane itself (HoldOverwatch's FOCUS probe)
    /// and commits it here. Falls back to a plain wide watch on a degenerate direction so the
    /// action is never wasted (progress invariant).
    void IssueFocusWatch(Unit u, int dx, int dy)
    {
        if (u == null || !u.CanAct || u.Ammo <= 0) return;
        if (u.HasStatus(StatusKind.Disoriented)) return;   // caller (HoldOverwatch) pre-gates this
        if (dx == 0 && dy == 0) { Selected = u; DoOverwatch(); return; }
        u.OnOverwatch = true;
        u.OwFocused = true;
        u.OwDirX = dx; u.OwDirY = dy;
        u.ActionsLeft = 0;
        Stats.RecordAction("FOCUS");   // W2 verb telemetry
        Fx.PopText(u.Pos + new Vector2(0, -30), "FOCUS", Pal.VipGold, 18f);
        Audio.Play("over");
        AimMode = false;
        _tutOver = true;
    }

    void DoHunker()
    {
        if (Selected == null || !Selected.CanAct) return;
        Selected.Hunkered = true;
        Selected.ActionsLeft = 0;
        Stats.RecordAction("HUNKER");   // W2 verb telemetry
        Fx.PopText(Selected.Pos + new Vector2(0, -30), "HUNKERED", Pal.Good, 18f);
        Audio.Play("hunker");
        AimMode = false;
    }

    void DoHack()
    {
        if (!CanHack(Selected)) return;
        Selected.ActionsLeft -= 1;
        AimMode = false;
        Stats.RecordAction(HasSabotage ? "PLANT" : "HACK");   // W2 verb telemetry
        if (HasSabotage)
        {
            int i = NearestSabotageSite(Selected);
            if (i < 0) return;
            SabotageBlown.Add(i);
            // balance fix: planting a charge GOES LOUD — break stealth + rouse pods near the
            // site (no free, uncontested sabotage; the squad must hold while it plants all 3).
            HackNoise(SabotageSites[i].x, SabotageSites[i].y);
            var sat = Util.TileCenter(SabotageSites[i].x, SabotageSites[i].y);
            Fx.PopText(sat + new Vector2(0, -30), "CHARGE SET", Pal.Foe, 20f);
            Fx.Burst(sat, Pal.Accent, 22, 240f, 0.6f, 4.5f, true);
            Fx.AddShake(6f);
            Audio.Play("reload");
            return;
        }
        HackProgress++;
        HackedThisTurn = true;          // one breach cycle per turn — finishing now takes a hold
        // balance fix: hacking the terminal GOES LOUD — break stealth + rouse nearby pods so
        // the squad must defend the console across all HackRequired charges, not sneak it.
        HackNoise(Terminal.x, Terminal.y);
        var at = Util.TileCenter(Terminal.x, Terminal.y);
        Fx.PopText(at + new Vector2(0, -30), HackProgress >= HackRequired ? "HACKED" : "HACK +1", Pal.Accent, 20f);
        Fx.Burst(at, Pal.Accent, 14, 160f, 0.5f, 3f);
        Audio.Play("reload");
    }

    // ---- SIGNAL W8 — CUSTODIAN re-lock/re-arm (the enemy contests objective PROGRESS) ----------

    /// The index of a BLOWN sabotage charge at exactly `site`, or -1. (A blown entry is the only
    /// re-armable one; an intact charge needs no keeper.)
    int BlownSiteAt((int x, int y) site)
    {
        for (int i = 0; i < SabotageSites.Count; i++)
            if (SabotageBlown.Contains(i) && SabotageSites[i] == site) return i;
        return -1;
    }

    /// True when enemy `e`, standing adjacent to `site`, has objective progress there to undo:
    /// a partially-hacked terminal (HackProgress in 1..HackRequired-1 — at HackRequired the
    /// mission already ended) or a blown sabotage charge. Mirrors the player's CanHack gating.
    bool CanRelock(Unit e, (int x, int y) site)
    {
        if (e == null || !e.Alive || Util.ChebyDist(e.X, e.Y, site.x, site.y) > 1) return false;
        if (HasTerminal && site == Terminal) return HackProgress > 0 && HackProgress < HackRequired;
        // W8 review: mirror the hack arm's completed-objective guard — all charges blown means the
        // mission is already won (the plant path ends it same-tick today, but a future deferred-end
        // path must never let a keeper re-arm a won mission).
        if (HasSabotage) return SabotageBlown.Count < SabotageSites.Count && BlownSiteAt(site) >= 0;
        return false;
    }

    /// Undo ONE step of objective progress at `site` (validated by CanRelock): -1 HackProgress on
    /// the terminal, or re-arm one blown sabotage charge. Telegraphed with a banner line + site FX
    /// so the swing is never silent — the counter is the same as ever: kill the keeper.
    void DoRelock(Unit e, (int x, int y) site)
    {
        var at = Util.TileCenter(site.x, site.y);
        if (HasTerminal && site == Terminal && HackProgress > 0)
        {
            HackProgress--;
            Fx.PopText(at + new Vector2(0, -30), "RE-LOCKED", Pal.Foe, 20f);
            ShowBanner("CUSTODIAN RE-LOCKS THE TERMINAL", true);
        }
        else if (HasSabotage)
        {
            int i = BlownSiteAt(site);
            if (i < 0) return;
            SabotageBlown.Remove(i);
            Fx.PopText(at + new Vector2(0, -30), "RE-ARMED", Pal.Foe, 20f);
            ShowBanner("CUSTODIAN RE-ARMS THE CHARGE", true);
        }
        else return;
        Fx.Burst(at, Pal.Foe, 14, 160f, 0.5f, 3f);
        Audio.Play("reload");
    }

    /// DEPLOY BEACON (Evac only, one/mission): the selected soldier spends ONE action to drop a
    /// forward extraction beacon on THEIR tile. Its walkable 3x3 (centre + ring, floor tiles only —
    /// non-floor tiles are clipped) is UNIONed into EvacZone alongside the fixed far-corner fallback,
    /// so the squad can extract HERE instead of marching to the corner. Modelled on DoHack (validate,
    /// spend 1 action, no turn-end, FX). Refuses gracefully if CanBeacon is false (never crashes).
    void DoBeacon()
    {
        if (!CanBeacon(Selected)) return;
        var u = Selected;
        u.ActionsLeft -= 1;
        AimMode = false;
        BeaconPlanted = true;
        BeaconTile = (u.X, u.Y);
        BeaconZone.Clear();
        // stamp the 3x3, skipping non-floor / off-board tiles and any tile already in the fallback zone
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int bx = u.X + dx, by = u.Y + dy;
                if (!Grid.IsFloor(bx, by)) continue;          // clip walls / cover / off-board
                if (EvacZone.Contains((bx, by))) continue;    // don't double-count the fallback corner
                BeaconZone.Add((bx, by));
                EvacZone.Add((bx, by));
            }
        Stats.RecordAction("BEACON");   // W2 verb telemetry
        var at = Util.TileCenter(u.X, u.Y);
        Fx.PopText(at + new Vector2(0, -30), "BEACON SET", Pal.Good, 22f);
        Fx.Burst(at, Pal.Good, 22, 240f, 0.6f, 4.5f, true);
        Fx.AddShake(4f);
        Audio.Play("reload");
        CheckEnd();   // planting where the squad already stands can complete the extraction outright
    }

    /// The nearest free evac tile to (x,y) within Chebyshev `maxStep`, or null. "Free" = an evac
    /// tile not already occupied by another unit.
    (int x, int y)? NearestFreeEvac(int x, int y, Unit mover, int maxStep)
    {
        (int x, int y)? best = null; int bestD = int.MaxValue;
        foreach (var t in EvacZone)
        {
            if (IsOccupiedByOther(t.x, t.y, mover)) continue;
            int d = Util.ChebyDist(x, y, t.x, t.y);
            if (d <= maxStep && d < bestD) { bestD = d; best = t; }
        }
        return best;
    }

    /// The ally a soldier standing in the evac zone could lift out: an adjacent (Chebyshev 1)
    /// friendly that ISN'T already in the zone — the VIP/freed captive on Escort/Rescue, else any
    /// other soldier on Evac. Returns null if `u` isn't a securing soldier in the zone, or nobody
    /// adjacent needs a pull, or there's no free zone tile near the candidate.
    Unit ExtractCandidate(Unit u)
    {
        if (u == null || u.Team != Team.Player || u.IsVip || !u.CanAct) return null;
        if (!HasExtractAction || !EvacZone.Contains((u.X, u.Y))) return null;
        Unit best = null;
        foreach (var c in Players)
        {
            if (c == u || !c.Alive) continue;
            if (EvacZone.Contains((c.X, c.Y))) continue;             // already secured
            if (Util.ChebyDist(u.X, u.Y, c.X, c.Y) > 1) continue;    // must be adjacent to the puller
            if (c.IsVip && CaptiveLocked) continue;                  // a still-caged captive can't be hauled
            if (NearestFreeEvac(c.X, c.Y, c, 2) == null) continue;   // need a free zone tile to pull them to
            // priority: the VIP/captive (the win-critical asset) over an ordinary soldier
            if (best == null || (c.IsVip && !best.IsVip)) best = c;
        }
        return best;
    }

    public bool CanExtract(Unit u) => ExtractCandidate(u) != null;

    void DoExtract()
    {
        var cand = ExtractCandidate(Selected);
        if (cand == null) return;
        var dest = NearestFreeEvac(cand.X, cand.Y, cand, 2);
        if (dest == null) return;
        Selected.ActionsLeft -= 1;                  // a support action — does NOT end the turn
        Stats.RecordAction("EXTRACT");              // W2 verb telemetry
        cand.X = dest.Value.x; cand.Y = dest.Value.y; cand.SyncPos();
        // FUL-2: the pull is a tile entry like any other — without this, an EXTRACTed unit kept a
        // planted BIPOD, skipped bleed/burn ticks, never picked up a cache on the zone tile, and
        // drew no overwatch (the one teleport left in a game whose verbs all route arrivals
        // through OnUnitEnteredTile — the ShoveAnim/DRAG landing does exactly this call).
        OnUnitEnteredTile(cand);
        if (!cand.Alive) return;                    // the arrival itself can kill (bleed/burn/reaction)
        // feel: a quick haul-aboard flash on both soldier + asset
        Fx.Burst(cand.Pos, cand.IsVip ? Pal.VipGold : Pal.Friend, 16, 220f, 0.5f, 4f, true);
        Fx.PopText(cand.Pos + new Vector2(0, -28), "EXTRACTED", cand.IsVip ? Pal.VipGold : Pal.Friend, 22f);
        Fx.AddShake(3f);
        Audio.Play("select");
        // pulling the last unit in can win outright — but if the arrival queued reaction fire,
        // let the frame loop call CheckEnd after the queue drains (a queued shot may still kill).
        if (_anims.Count == 0) CheckEnd();
    }

    void DoReload()
    {
        if (Selected == null || !Selected.CanAct || Selected.Ammo >= Selected.Weapon.Clip) return;
        Selected.Ammo = Selected.Weapon.Clip;
        Selected.ActionsLeft -= 1;
        Stats.RecordAction("RELOAD");   // W2 verb telemetry
        Fx.PopText(Selected.Pos + new Vector2(0, -30), "RELOAD", Pal.TxtDim, 18f);
        Audio.Play("reload");
        AimMode = false;
    }

    // Whether the selected unit could fire its signature ability right now.
    public bool CanAbility(Unit u)
    {
        if (u == null || u.Team != Team.Player || !u.CanAct || u.AbilityCd > 0) return false;
        return u.Ability switch
        {
            AbilityKind.RunGun  => !u.RunGun,
            AbilityKind.Blitz   => !u.Blitz,
            AbilityKind.Steady  => !u.Steady && u.ActionsLeft >= 1,
            AbilityKind.Suppress=> u.Ammo > 0 && HasAnyTarget(u),
            AbilityKind.Heal    => u.ActionsLeft >= 1 && MostWoundedAdjacentAlly(u) != null,
            AbilityKind.Mark    => u.ActionsLeft >= 1 && HasMarkTarget(u),
            AbilityKind.Grapple => !u.ShovedThisTurn && u.ActionsLeft >= 1 && HasGrappleTarget(u),
            AbilityKind.Slipstream => !u.Slipstreaming && u.ActionsLeft >= 1,   // free move stance (needs an action to actually move)
            AbilityKind.Pin     => u.Ammo > 0 && u.ActionsLeft >= 1 && HasPinTarget(u),
            _ => false,
        };
    }

    /// CORPSMAN PATCH target: the most-wounded (lowest HP-fraction) alive, non-VIP squadmate
    /// standing Chebyshev-adjacent to the corpsman. Returns null if no one nearby needs aid.
    Unit MostWoundedAdjacentAlly(Unit medic)
    {
        if (medic == null) return null;
        // COMBAT MEDIC fork: longer reach (Cheby<=2) AND may patch SELF; the default PATCH is Cheby<=1
        // and skips self. (No new targeting mode — auto-target the most-wounded eligible ally; bounded Cd 3.)
        int reach = medic.HasSpec(Spec.CombatMedic) ? 2 : 1;
        bool allowSelf = medic.HasSpec(Spec.CombatMedic);
        Unit best = null;
        float worst = 1f;
        foreach (var p in AlivePlayers())
        {
            if ((p == medic && !allowSelf) || p.IsVip || p.Hp >= p.MaxHp || p.MaxHp <= 0) continue;
            if (Util.ChebyDist(medic.X, medic.Y, p.X, p.Y) > reach) continue;
            float frac = (float)p.Hp / p.MaxHp;
            if (best == null || frac < worst) { best = p; worst = frac; }
        }
        return best;
    }

    /// Any legal MARK target (visible un-marked foe) for sharpshooter `u`?
    bool HasMarkTarget(Unit u)
    {
        foreach (var e in Enemies) if (MarkTargetOk(u, e)) return true;
        return false;
    }
    /// Any legal GRAPPLE target (foe within reach) for assault `u`?
    bool HasGrappleTarget(Unit u)
    {
        foreach (var e in Enemies) if (GrappleTargetOk(u, e)) return true;
        return false;
    }
    /// The best AI/autopilot MARK pick: a live foe in LoS we'd want the squad to collapse on
    /// (highest priority weight). Null if none visible.
    Unit BestMarkTarget(Unit u)
    {
        Unit best = null; float bestW = -1f;
        foreach (var e in Enemies)
        {
            if (!MarkTargetOk(u, e)) continue;
            float w = PriorityWeight(e);
            if (w > bestW) { bestW = w; best = e; }
        }
        return best;
    }
    /// The best AI/autopilot GRAPPLE pick: a foe in reach that's currently IN cover from us
    /// (yanking it out is the point); fall back to the nearest foe in reach.
    Unit BestGrappleTarget(Unit u)
    {
        Unit best = null; int bestScore = int.MinValue;
        foreach (var e in Enemies)
        {
            if (!GrappleTargetOk(u, e)) continue;
            int cover = Grid.GetCover(e.X, e.Y, u.X, u.Y).Level;   // how protected it is from us now
            int score = cover * 100 - Util.ChebyDist(u.X, u.Y, e.X, e.Y);
            if (score > bestScore) { bestScore = score; best = e; }
        }
        return best;
    }

    // ---- SUPPRESSING FIRE (gunner VERB): pin a foe + its neighbours (area denial) ----

    /// Is `target` a legal SUPPRESS target for gunner `u`? An alive, visible (LoS) enemy within PinRange.
    /// (Pinning an already-pinned foe is allowed — it refreshes the zone / re-anchors it on a new cluster.)
    bool PinTargetOk(Unit u, Unit target)
    {
        if (u == null || target == null || !u.CanAct || u.ActionsLeft < 1 || u.AbilityCd > 0 || u.Ammo <= 0) return false;
        if (!target.Alive || target.Team != Team.Enemy) return false;
        if (target.IsVip && CaptiveLocked) return false;             // can't suppress the caged captive
        if (Util.TileDist(u.X, u.Y, target.X, target.Y) > PinRange) return false;
        return Grid.HasLineOfSight(u.X, u.Y, target.X, target.Y);
    }
    /// Any legal SUPPRESS target for gunner `u`?
    bool HasPinTarget(Unit u)
    {
        foreach (var e in Enemies) if (PinTargetOk(u, e)) return true;
        return false;
    }
    /// The best AI/autopilot SUPPRESS pick: the visible foe whose 3x3 zone catches the MOST live enemies
    /// (area denial wants a cluster), tie-broken by priority weight. Null if none visible.
    Unit BestPinTarget(Unit u)
    {
        Unit best = null; int bestCluster = 0; float bestW = -1f;
        foreach (var e in Enemies)
        {
            if (!PinTargetOk(u, e)) continue;
            int cluster = 0;
            foreach (var o in Enemies)
                if (o.Alive && o.Team == Team.Enemy && Util.ChebyDist(e.X, e.Y, o.X, o.Y) <= 1) cluster++;
            float w = PriorityWeight(e);
            if (cluster > bestCluster || (cluster == bestCluster && w > bestW))
            { bestCluster = cluster; bestW = w; best = e; }
        }
        return best;
    }

    void TogglePin()
    {
        if (Selected == null || Selected.Ability != AbilityKind.Pin || !CanAbility(Selected)) return;
        PinMode = !PinMode;
        if (PinMode) { AimMode = false; SnapShot = false; GrenadeMode = false; ItemMode = false; ShoveMode = false; MarkMode = false; GrappleMode = false; DragMode = false; VaultMode = false; }
    }

    /// Lay down SUPPRESSING FIRE on `target`: pin it AND every enemy Chebyshev-adjacent to it for PinTurns
    /// (Combat.SuppressAim penalty + cannot dash — see EnqueuePlannedMove). Costs the action + the charge +
    /// a bullet and ENDS the turn (the full burst); the gunner also trains overwatch on the painted tile.
    /// Suppressing fire is aggression -> breaks concealment + wakes the painted pod.
    void IssuePin(Unit u, Unit target)
    {
        if (!PinTargetOk(u, target)) { PinMode = false; return; }
        if (SquadConcealed) BreakConcealment();   // a suppressing burst gives the squad away (no actor -> no ambush flag)
        u.AbilityCd = Unit.AbilityCooldownFor(u.Ability); u.Ammo--; u.ActionsLeft = 0; u.OnOverwatch = true;
        Stats.RecordAction("PIN");   // W2 verb telemetry
        // SPEC FORK footprint: AREA DENIAL widens the pin to a 5x5 (Cheby<=2); ANCHOR (Spec.Bulwark)
        // shrinks it to the single target only (radius 0 — paired with its +2 armor); default 3x3 (1).
        int pinRadius = u.HasSpec(Spec.AreaDenial) ? 2 : (u.HasSpec(Spec.Bulwark) ? 0 : 1);
        int pinned = 0;
        foreach (var e in Enemies)
        {
            if (!e.Alive || e.Team != Team.Enemy) continue;
            if (Util.ChebyDist(target.X, target.Y, e.X, e.Y) > pinRadius) continue;
            if (e.IsVip && CaptiveLocked) continue;
            e.Pinned = PinTurns;
            if (!e.Active) ActivatePod(e.PodId);   // suppressing a dormant foe wakes its pod
            Fx.PopText(e.Pos + new Vector2(0, -34), "PINNED", Pal.Foe, 16f);
            Fx.Burst(e.Pos, Pal.Foe, 8, 110f, 0.4f, 3f);
            pinned++;
        }
        Fx.PopText(u.Pos + new Vector2(0, -34), "SUPPRESS", Pal.Accent, 16f);
        Audio.Play("over");
        PinMode = false; MarkMode = false; ShoveMode = false; GrappleMode = false;
    }

    /// Clear every PIN on the board (called at the gunner-owner's next player-turn start, mirroring how the
    /// Suppress debuff is reset — so the pin lasts through the enemy turn it was meant to deny, then expires).
    void ClearPins() { foreach (var e in Enemies) e.Pinned = 0; }

    void DoAbility()
    {
        var u = Selected;
        if (!CanAbility(u)) return;
        _tutAbility = true;   // T1: the drill's ABILITY lesson — counts the three targeting verbs
                              // (MARK/GRAPPLE/PIN) at the point the player opens their mode, since
                              // reaching the mode is the thing the lesson is teaching.
        // the targeting VERBS enter a targeting mode for the human player (the AI/autopilot calls
        // IssueMark/IssueGrapple/IssuePin directly via PrepAbilityFor, so it never opens a mode).
        if (u.Ability == AbilityKind.Mark)    { ToggleMark();    return; }
        if (u.Ability == AbilityKind.Grapple) { ToggleGrapple(); return; }
        if (u.Ability == AbilityKind.Pin)     { TogglePin();     return; }
        var at = u.Pos + new Vector2(0, -34);
        switch (u.Ability)
        {
            case AbilityKind.RunGun:
                u.RunGun = true; u.AbilityCd = Unit.AbilityCooldownFor(u.Ability);
                Stats.RecordAction("RUNGUN");   // W2 verb telemetry
                Fx.PopText(at, "RUN & GUN", Pal.Accent, 18f);
                Fx.Burst(u.Pos, Pal.Accent, 10, 120f, 0.4f, 3f);
                Audio.Play("reload");
                break;
            case AbilityKind.Slipstream:
                // RANGER SLIPSTREAM: arm a free, overwatch-immune move (consumed by the next IssueMove).
                u.Slipstreaming = true;
                // PATHFINDER fork: faster cooldown (3->2) AND a truly-free move (IssueMove zeroes the
                // slip action cost for Pathfinder — the standard slip still spends 1). Only Pathfinder
                // touches AbilityCd here (the call site), so AbilityCooldownFor / CDTEST stay green (R6).
                u.AbilityCd = u.HasSpec(Spec.Pathfinder) ? 2 : Unit.AbilityCooldownFor(u.Ability);
                // PHANTOM fork: the next shot strikes from ambush (reuses the built concealment-ambush
                // path; cleared in BeginTurn, consumed by the next shot).
                if (u.HasSpec(Spec.Phantom)) u.FiredFromConcealment = true;
                Stats.RecordAction("SLIPSTREAM");   // W2 verb telemetry
                Fx.PopText(at, "SLIPSTREAM", Pal.Accent, 18f);
                Fx.Burst(u.Pos, Pal.Accent, 12, 150f, 0.4f, 3f);
                Audio.Play("reload");
                break;
            case AbilityKind.Blitz:
                u.Blitz = true; u.AbilityCd = Unit.AbilityCooldownFor(u.Ability);
                Stats.RecordAction("BLITZ");   // W2 verb telemetry
                Fx.PopText(at, "BLITZ", Pal.Accent, 18f);
                Fx.Burst(u.Pos, Pal.Accent, 10, 120f, 0.4f, 3f);
                Audio.Play("reload");
                break;
            case AbilityKind.Steady:
                u.Steady = true; u.AbilityCd = Unit.AbilityCooldownFor(u.Ability); u.ActionsLeft -= 1;
                Stats.RecordAction("STEADY");   // W2 verb telemetry
                Fx.PopText(at, "STEADY", Pal.Good, 18f);
                Fx.Burst(u.Pos, Pal.Good, 10, 120f, 0.4f, 3f);
                Audio.Play("reload");
                break;
            case AbilityKind.Suppress:
                var t = FirstTargetFor(u);
                if (t == null) return;
                // 4.4 (review Mi1): pinning fire breaks stealth, but Suppress isn't a damage
                // shot (no Combat.Resolve), so pass no actor - no dangling ambush flag.
                if (SquadConcealed) BreakConcealment();
                u.AbilityCd = Unit.AbilityCooldownFor(u.Ability); u.Ammo--; u.ActionsLeft = 0; u.OnOverwatch = true;
                t.Suppress = Combat.SuppressAim;
                Stats.RecordAction("SUPPRESS");   // W2 verb telemetry
                Fx.PopText(t.Pos + new Vector2(0, -34), "SUPPRESSED", Pal.Foe, 18f);
                Fx.PopText(at, "SUPPRESS", Pal.Accent, 16f);
                Audio.Play("over");
                if (!t.Active) ActivatePod(t.PodId);   // pinning fire reveals the pod
                break;
            case AbilityKind.Heal:
                var ally = MostWoundedAdjacentAlly(u);
                if (ally == null) return;
                // COMBAT MEDIC fork heals 1 less (PatchHeal-1) — the trade for self-target + reach 2.
                int baseHeal = u.HasSpec(Spec.CombatMedic) ? Unit.PatchHeal - 1 : Unit.PatchHeal;
                // FUL-7 REVIVE — the corpsman's stage: a DOWNED squadmate (Hp 0 is always the
                // most-wounded eligible target) gets back UP at the heal value instead. They act
                // on their NEXT turn (ActionsLeft 0 now); FieldSurgeon's triage rides along;
                // Cd 3 unchanged; the same PATCH telemetry measures the new stage for free.
                if (ally.Downed)
                {
                    ally.Downed = false; ally.Stabilized = false; ally.DownedTurns = 0;
                    ally.Hp = Math.Min(ally.MaxHp, Math.Max(1, baseHeal));
                    ally.ActionsLeft = 0;                       // up, but they act next turn
                    u.AbilityCd = Unit.AbilityCooldownFor(u.Ability); u.ActionsLeft -= 1;
                    Stats.RecordAction("PATCH");                // the FUL-5 PATCH counter
                    Stats.RecordDownRevived();                  // FUL-7 telemetry: a save, not a KIA
                    if (u.HasSpec(Spec.FieldSurgeon))
                    {
                        ally.Wound = 0; ally.Statuses.Clear();
                        Fx.PopText(ally.Pos + new Vector2(0, -50), "TRIAGE", Pal.Good, 16f);
                    }
                    Fx.PopText(ally.Pos + new Vector2(0, -34), "REVIVED", Pal.Good, 22f);
                    Fx.Burst(ally.Pos, Pal.Good, 16, 160f, 0.5f, 3.5f, true);
                    Fx.Flash(ally.Pos, Pal.Good, 22f, 0.16f, 0.5f);
                    Fx.PopText(at, "PATCH", Pal.Good, 16f);
                    ally.Flash = 0.6f;
                    Audio.Play("reload");
                    break;
                }
                int healed = Math.Min(baseHeal, ally.MaxHp - ally.Hp);
                if (healed <= 0) return;
                ally.Hp += healed;
                u.AbilityCd = Unit.AbilityCooldownFor(u.Ability); u.ActionsLeft -= 1;     // patching costs one action (like STEADY)
                Stats.RecordAction("PATCH");   // W2 verb telemetry
                // FIELD SURGEON fork: PATCH also clears the patient's wound + all status effects (triage).
                if (u.HasSpec(Spec.FieldSurgeon))
                {
                    ally.Wound = 0; ally.Statuses.Clear();
                    Fx.PopText(ally.Pos + new Vector2(0, -50), "TRIAGE", Pal.Good, 16f);
                }
                Fx.PopText(ally.Pos + new Vector2(0, -34), $"+{healed}", Pal.Good, 20f);
                Fx.Burst(ally.Pos, Pal.Good, 12, 120f, 0.45f, 3f);
                Fx.PopText(at, "PATCH", Pal.Good, 16f);
                ally.Flash = 0.6f;                          // a brief restorative flash on the patient
                Audio.Play("reload");
                break;
        }
        AimMode = false;
        SnapShot = false;
        GrenadeMode = false;
        ItemMode = false;
        ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false;
    }

    void RequestEndTurn()
    {
        if (!EndTurnArmed && AlivePlayers().Any(p => p.CanAct)) { EndTurnArmed = true; return; }
        EndTurnArmed = false;
        EndPlayerTurn();
    }

    /// DEFEND: spawn a wave of reinforcements at the right edge on early enemy turns.
    /// APEX W5: Defend waves are `rich` — the full endless roster (guarded: no TURRET/BOMBARD)
    /// instead of the grunt/scout coin flip, so the one enemy-forced-tempo objective has variety.
    /// This flag is the ONLY rich call site by design: pressure-clock waves must stay cheap bodies
    /// (see MakeWaveHostile's doc — hardening turtle punishment would widen the policy gap).
    void SpawnDefendWave()
    {
        if (!DefendWaveTurn(_turnCount)) return;
        // FUL-4: wave size 1+m/2 (was 2+m/2) — with three waves landing per mission the old +1
        // body per wave compounded to +3 per mission over the whole timer. podded: each wave is
        // a real morale pod (focus-firing a wave down routs its survivors, like any pod).
        // FUL-13 review fix: the wave heatStat honors the m1-2 opener grace exactly like the
        // initial force (Game.cs SetupMission ramp) — without it a heat-8 SKIRMISH Defend (n=1,
        // grace zeroes every numeric delta) fielded +4-stat waves, contradicting the documented
        // grace contract and pre-empting the skirmish-grace owner decision.
        int waveHeatStat = Sightline.Heat.StatDelta(_run.HeatLevel);
        if (_run.Mission <= 1) waveHeatStat = 0;
        else if (_run.Mission == 2) waveHeatStat /= 2;
        SpawnReinforcements(1 + _run.Mission / 2, 12, "WAVE", rich: true, podded: true,
                            heatStat: waveHeatStat);   // FUL-13: waves were heat-blind
    }

    /// FUL-4: the DEFEND wave schedule — ONE shared read for the spawner and the start-of-turn
    /// telegraph (the W8 never-lies pattern). Waves land at the END of odd player turns from t3
    /// (first wave graced past t1 — a rich wave on a squad with ZERO player turns to set a line
    /// was a coin-flip opener), never on the timer's final turn.
    bool DefendWaveTurn(int turn) =>
        Objective == Objective.Defend && turn >= 3 && turn % 2 == 1 && turn < DefendTurns;

    /// Shared reinforcement spawner: drops up to `want` active wave-hostiles in from the right
    /// board edge (already engaged), honoring a live-enemy `cap`. Used by both the DEFEND objective
    /// (`rich` waves — full roster) and the anti-turtle PRESSURE CLOCK (default cheap grunt/scout
    /// mix). Returns how many it actually added. FUL-4 `podded`: the wave lands as ONE fresh
    /// morale pod (id 100+, _podOrig-snapshotted) so rout plays; default keeps PodId=-1 —
    /// pressure-clock punishment waves stay morale-exempt (a routable punishment isn't one).
    int SpawnReinforcements(int want, int cap, string label, bool rich = false, bool podded = false, int heatStat = 0)
    {
        if (AliveEnemies().Count >= cap) return 0;                     // clutter cap
        int n = _run.Mission;
        var rows = Enumerable.Range(0, Grid.H).OrderBy(_ => Util.RandF()).ToList();
        int added = 0;
        var waveClasses = new List<string>();   // harness-only composition echo (AutoPlay)
        // W4 — a SURROUNDED hold has to keep being surrounded: under an ENVELOP opening the
        // squad sits at board centre, so waves that all walk in from the east edge would quietly
        // turn the second half of every Defend back into a one-bearing fight. Rotate the arrival
        // rim deterministically (no RNG draw — a plain per-mission wave counter). Every other
        // opening keeps the historical east edge byte-for-byte.
        bool rimRotate = Mission.EnvelopRimWaves && Mission.AppliedDeploy == Mission.DeployEnvelop;
        int rim = rimRotate ? _waveIndex % 4 : 0;
        _waveIndex++;
        // Along-rim scan order: reuse the shuffled row list on the E/W rims, and a shuffled
        // COLUMN list on the N/S rims (same draw kind, so nothing else about the wave changes).
        var lane = rim == 2 || rim == 3
            ? Enumerable.Range(0, Grid.W).OrderBy(_ => Util.RandF()).ToList()
            : rows;
        foreach (int t in lane)
        {
            if (added >= want || AliveEnemies().Count >= cap) break;
            // (outer tile, fallback one step further out) for the chosen rim
            int x, y, x2, y2;
            switch (rim)
            {
                case 1:  x = 1; y = t; x2 = 0; y2 = t; break;                          // west
                case 2:  x = t; y = 1; x2 = t; y2 = 0; break;                          // north
                case 3:  x = t; y = Grid.H - 2; x2 = t; y2 = Grid.H - 1; break;        // south
                default: x = Grid.W - 2; y = t; x2 = Grid.W - 1; y2 = t; break;        // east (today)
            }
            if (!Grid.IsFloor(x, y) || IsOccupiedByOther(x, y, null))
            {
                x = x2; y = y2;
                if (!Grid.IsFloor(x, y) || IsOccupiedByOther(x, y, null)) continue;
            }
            var e = Mission.MakeWaveHostile(n, x, y, rich, heatStat);   // FUL-13: DEFEND waves inherit heat
            e.Alert = AlertLevel.Alert;                          // reinforcements arrive already engaged
            e.PodId = podded ? _nextWavePod : -1;                // FUL-4: defend waves are morale pods
            e.SyncPos();
            Stats.RecordSpawn(e.Cls, Combat.MissionFaction != Faction.None);   // APEX W5 composition tally
            Enemies.Add(e);
            waveClasses.Add(e.Cls);
            Fx.Burst(e.Pos, Pal.Foe, 14, 160f, 0.5f, 3f, true);
            added++;
        }
        // FUL-4: seal the wave's morale snapshot (the _podOrig pattern SetupMission uses for the
        // initial force) — one pod per wave, sized to what ACTUALLY landed under the cap.
        if (podded && added > 0) { _podOrig[_nextWavePod] = added; _nextWavePod++; }
        if (added > 0) { Fx.PopText(Util.TileCenter(Grid.W - 2, 0) + new Vector2(0, -10), label, Pal.Foe, 20f); Audio.Play("turn"); RefreshCombatRoster(); }
        // C3: every mid-mission body, whatever spawned it, lands on this one counter. EnemiesStart
        // is the DEPLOY force only, so without this the report cannot tell a mission that beat six
        // hostiles from one that beat six and then six more.
        Stats.RecordReinforce(added);
        // APEX W5: headless-harness echo so an autoplay log shows what the waves actually field
        // (AutoPlay is the env-gated smoke/balance path only — never set in normal play).
        if (AutoPlay && added > 0)
            Console.WriteLine($"{label}: +{added} ({string.Join(",", waveClasses)}){(rich ? " [rich]" : "")}");
        return added;
    }

    /// Anti-turtle PRESSURE CLOCK. Called at the top of EndPlayerTurn (before the enemy acts) on
    /// camp-friendly objectives. Recomputes the rung from the turn count, banners a telegraph when
    /// it rises, sets Combat.PressureAim (the escalating enemy accuracy bonus), and -- at the higher
    /// rungs -- calls in reinforcements from the right edge. Tuned so a reasonable pace stays winnable:
    /// nothing happens during the grace period, the aim bonus is modest (+3..+12), and waves are small,
    /// rare (one per two-turn step, max one per turn) and capped. Turtling becomes strictly worse than
    /// closing the distance, which is the whole point.
    void UpdatePressure()
    {
        if (!PressureClockObjective()) { Pressure = 0; Combat.PressureAim = 0; return; }
        int rung = PressureRungFor(_turnCount);
        // scale the aim bias up a touch with Heat so the clock keeps teeth on harder rungs
        int heatBump = _run != null && _run.HeatLevel >= 4 ? 1 : 0;
        Combat.PressureAim = rung * (PressureAimPerRung + heatBump);
        if (rung > Pressure)
        {
            // telegraphed escalation -- the player sees it coming and can choose to advance
            ShowBanner(rung >= PressureMax ? "ENEMY REINFORCEMENTS - MAX PRESSURE" : "PRESSURE RISING", false);
            Audio.Play("turn");
        }
        Pressure = rung;
        Stats.RecordPressure(rung);   // C3: harness-only high-water mark of the anti-turtle rung
        // Reinforcements kick in from rung 2 onward, once per fresh rung (not every turn) so the
        // board doesn't flood: a small wave that scales with the rung. One per rung-up event.
        // C3: ...but not on ELIMINATE, where a reinforcement is not pressure — it is the finish
        // line moving away from the squad. See the ClockWavesOnEliminate contract above.
        if (rung >= 2 && rung > _pressureWaves && ClockMayReinforce)
        {
            int want = 1 + rung / 2;                       // rung2->2, rung3->2, rung4->3
            SpawnReinforcements(want, 11 + _run.Mission, "REINFORCEMENTS");
            _pressureWaves = rung;
        }
    }

    /// CROSSFIRE wiring: re-snapshot the full live roster into Combat.AllUnits so ComputeOdds'
    /// converging-fire check sees every unit (both teams). Called per mission and whenever the
    /// roster grows mid-mission (Defend waves). Cheap; the list is tiny.
    void RefreshCombatRoster() =>
        Combat.AllUnits = new List<Unit>(Players.Where(u => u != null).Concat(Enemies.Where(u => u != null)));

    /// AEGIS shields re-face toward the nearest soldier each enemy turn, so the squad
    /// must keep moving to flank the barrier rather than parking on one open side.
    /// W5: HasShieldArc flag (mirrors Cls=="SHIELD"; a shield-arc boss re-faces too).
    void FaceShields()
    {
        foreach (var e in Enemies)
        {
            if (!e.Alive || !e.HasShieldArc) continue;
            // FUL-7 (review F5): never face the shield toward a DOWNED body — it is not a threat,
            // and facing it hands the standing squad a free flank.
            var p = AlivePlayers().Where(q => !q.Downed).OrderBy(q => Util.ChebyDist(e.X, e.Y, q.X, q.Y)).FirstOrDefault();
            if (p == null) continue;
            int dx = p.X - e.X, dy = p.Y - e.Y;
            if (Math.Abs(dx) >= Math.Abs(dy)) { e.ShieldDx = Math.Sign(dx); e.ShieldDy = 0; }
            else { e.ShieldDx = 0; e.ShieldDy = Math.Sign(dy); }
            if (e.ShieldDx == 0 && e.ShieldDy == 0) e.ShieldDx = -1;   // degenerate (same tile): keep a facing
        }
    }

    void EndPlayerTurn()
    {
        // SmartStep: count player turns spent concealed (a hard anti-TIMEOUT cap on the
        // stealth-race plan — see the concealment block in SmartStep). Harmless otherwise.
        if (SquadConcealed) _smartConcealTurns++;
        // keep the tutorial progressing even if the player skipped a prompted action
        if (TutStep >= 0 && TutStep < TutStepDone) AdvanceTutorial();
        EndTurnArmed = false;
        AimMode = false;
        SnapShot = false;
        GrenadeMode = false;
        ItemMode = false;
        ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false;
        Selected = null;
        MoveCost = null;
        Phase = Phase.EnemyTurn;
        TickSiegeStrikes();                                      // charged artillery lands BEFORE any enemy acts / before reinforcements
        if (Objective == Objective.Defend) SpawnDefendWave();    // reinforcements assault the holdout
        UpdatePressure();                                        // anti-turtle clock: escalate on camp-friendly objectives
        ResolveSuspicion();                                      // 4.3: suspicious pods confirm or lose contact
        FaceShields();                                           // AEGIS turns its barrier toward the squad
        UpdateHvtGuard();                                        // DECAPITATE: refresh the HVT's guarded state at the boundary
        foreach (var e in Enemies) if (e.Alive) { BeginEnemyUnitTurn(e); TickStatuses(e); }
        _aiUnits = AliveEnemies().Where(e => e.Active).ToList();  // dormant/suspicious pods don't act
        // UNDERTOW W4 — sequenced coordination: act SETUP verbs before FINISHERS. A SAPPER breach or a
        // STRIKER/adjacent shove EXPOSES a soldier; ordering those units first lets the incremental focus
        // recompute in PickNext collapse the pod onto the freshly-exposed target THIS SAME turn. Stable
        // OrderBy — every unit still acts exactly once, so it's TIMEOUT-safe (only reorders a bounded list).
        _aiUnits = _aiUnits.OrderBy(e => IsSetupUnit(e) ? 0 : 1).ToList();
        PlanEnemySquad();                                        // shared focus + overwatch map (advisory)
        _aiIdx = 0;
        _aiStage = AiStage.PickNext;
        _aiPlan = null;
        ClearIntent();
        ShowBanner("ENEMY TURN", true);
        Enqueue(new WaitAnim(0.5f), Team.Enemy);
    }

    /// SIGNAL W8 — the per-enemy turn-boundary step, factored out of EndPlayerTurn so MORALETEST
    /// can drive it directly. BeginTurn ticks Routed down one; a living banner in aura range ticks
    /// it down ONCE more — a bannered pod rallies one turn faster (RoutDuration 2 -> back in 1).
    /// Lives in GAME (not Unit.BeginTurn) because the aura needs the Enemies list.
    void BeginEnemyUnitTurn(Unit e)
    {
        e.BeginTurn();
        if (e.Routed > 0 && BannerNear(e)) e.Routed--;
    }

    void StartPlayerTurn()
    {
        _turnCount++;
        RunTurns++;      // W9: run-scoped, never reset by SetupMission — see the field
        Phase = Phase.PlayerTurn;
        // W10 INTEL CACHE: the pickup window closes after CacheTurns player turns — the routing
        // detour is a bet against this clock, not free money whenever the fight happens to drift by.
        if (CachePresent && --CacheTurnsLeft <= 0)
        {
            CachePresent = false;
            Fx.PopText(Util.TileCenter(CacheX, CacheY) + new Vector2(0, -20), "CACHE LOST", Pal.TxtDim, 18f);
            ShowBanner("INTEL CACHE WENT DARK", true);
        }
        LeashVip();                       // ESCORT / freed-RESCUE: the asset tags along with the squad (no hand-walking)
        ClearIntent();                    // no enemy intent lingers into the player's turn
        Grid.TickSmoke();                 // smoke clouds decay one turn per round
        TickHazards();                    // fire cooks off barrels + reignites units, then decays
        _refundedThisTurn.Clear();        // flank-kill refund is one per soldier per turn
        HackedThisTurn = false;           // the terminal accepts one breach cycle per turn (hold)
        ClearMarks();                     // a sharpshooter's MARK lasts until the marker's next turn
        ClearPins();                      // a gunner's SUPPRESSING FIRE pin lasts through one enemy turn, then lifts
        UpdateHvtGuard();                 // DECAPITATE: refresh the HVT's guarded state at the boundary (a guard may have moved)
        if (AutoPlay) AutoStallCheck();
        // APEX W2: deny the caged RESCUE captive its start-of-turn action re-grant (see SetupMission).
        // FUL-7: the bleed-out countdown ticks HERE — on the squad's clock, where the player
        // decides. STABILIZE freezes it — but only while a soldier is still standing: with the
        // whole squad down (or dead) there is nobody left to hold the dressing, so stabilized
        // timers run too — the degenerate all-downed board resolves in <= 3 bounded turns
        // (expire -> KillUnit -> the real wipe), never an infinite stall. Pinned in DOWNTEST.
        bool anySoldierUp = Players.Any(q => q.Alive && !q.Downed && !q.IsVip);
        foreach (var p in Players) if (p.Alive)
        {
            p.BeginTurn(); TickStatuses(p);
            if (p == Vip && CaptiveLocked) p.ActionsLeft = 0;
            if (p.Downed)
            {
                p.ActionsLeft = 0;               // never acts, never selectable (CanAct false)
                if ((!p.Stabilized || !anySoldierUp) && --p.DownedTurns <= 0) ExpireDowned(p);
                else if (p.Alive && !p.Stabilized)
                    Fx.PopText(p.Pos + new Vector2(0, -30), $"DOWN {p.DownedTurns}", Pal.Foe, 16f);
            }
        }
        foreach (var e in Enemies) if (e.Alive) { e.ReactedThisTurn = false; e.Suppress = 0; } // OW resets; suppression expires
        Selected = Players.FirstOrDefault(p => p.CanAct);
        AimMode = false;
        SnapShot = false;
        GrenadeMode = false;
        ItemMode = false;
        ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false;
        ShowBanner("PLAYER TURN", false);
        MaybeWaveTelegraph();
    }

    /// FUL-4: DEFEND wave-edge telegraph, ONE PLAYER TURN ahead of the wave acting — this turn
    /// ENDS with reinforcements at the east edge (DefendWaveTurn is the spawner's own schedule
    /// read, so the warning can never lie about timing). Deliberately overrides the PLAYER TURN
    /// banner in the W8 lane: the higher-stakes information wins the single banner slot.
    /// Shared by BeginPlayerTurn and the SIGHTLINE_WAVEBANNER shot hook (same real path).
    void MaybeWaveTelegraph()
    {
        if (!DefendWaveTurn(_turnCount)) return;
        ShowBanner("WAVE INBOUND - EAST EDGE", true);
        BannerSub = "reinforcements land when this turn ends";
    }

    /// ESCORT VIP LEASH: at the start of each player turn the fragile asset TAGS ALONG with the squad
    /// instead of being hand-walked (the old "drag" that made Escort a 10-turn micro-chore). If the VIP
    /// is alive, mobile (not caged), and NOT already Chebyshev-adjacent to a living non-VIP soldier, it
    /// auto-steps toward the NEAREST such soldier — up to its Mobility — preferring a SAFE tile (in cover
    /// and out of an active enemy's line of fire when a safer option exists). The player still advances /
    /// clears; the VIP follows. It stays fully player-selectable (manual override intact) and never
    /// auto-charges toward evac or into danger alone. Deterministic + TIMEOUT-safe: it always moves toward
    /// an EXISTING soldier, so it strictly converges (and short-circuits the instant it's adjacent).
    /// W4 (SIGNAL): the FREED RESCUE captive rides the same leash — post-free, Rescue IS an escort
    /// (fragile asset to the zone), and hand-walking it was the same micro-chore. The caged state is
    /// untouched (the CaptiveLocked check below holds it in the cage until a soldier springs it).
    void LeashVip()
    {
        if (Objective != Objective.Escort && Objective != Objective.Rescue) return;
        if (Vip == null || !Vip.Alive || CaptiveLocked || Vip.MoveBudget <= 0) return;
        // the soldiers the asset follows: living, non-VIP squad members (FUL-7: not a downed
        // body — the asset must never park itself beside a bleeding-out soldier in a fire lane)
        var soldiers = Players.Where(p => p.Alive && !p.IsVip && !p.Downed).ToList();
        if (soldiers.Count == 0) return;                       // nobody to follow (a wipe handles the loss)
        if (EvacZone.Contains((Vip.X, Vip.Y))) return;         // already extracted position — win check handles it
        // The asset FOLLOWS the squad toward evac. The leash anchor is the nearest soldier that is CLOSER to
        // evac than the VIP (the squad's forward element) — following the SPEARHEAD, not a straggler parked
        // beside the VIP at spawn (that mutual "VIP holds beside laggard, laggard waits by VIP" deadlock froze
        // the asset at spawn for a whole match). If NO soldier is ahead of the VIP (it's already the most
        // forward), tag along toward the plain nearest soldier and hold once beside it (don't charge alone).
        int vipEvac = DistToEvac(Vip.X, Vip.Y);
        var ahead = soldiers.Where(s => DistToEvac(s.X, s.Y) < vipEvac).ToList();
        // APEX W8: the anchor is the MOST FORWARD ahead-soldier (min DistToEvac; nearest-to-VIP only
        // as the tie-break). The old nearest-to-VIP pick zigzagged: with the squad spread out, a
        // DIFFERENT barely-ahead soldier was "nearest" every turn and the VIP chased each in turn —
        // a traced m5 escort walked the asset AWAY from evac for four straight turns. Following the
        // true spearhead makes leash progress monotone; the besideForward hold below (adjacent to ANY
        // ahead soldier) still keeps it from charging past its screen alone.
        var anchor = (ahead.Count > 0 ? ahead : soldiers)
                     .OrderBy(s => DistToEvac(s.X, s.Y))
                     .ThenBy(s => Util.ChebyDist(Vip.X, Vip.Y, s.X, s.Y)).First();
        // Hold when tucked beside the FORWARD element (the spearhead) — the VIP has kept pace and shouldn't
        // charge on alone into the contested corner ahead of its escort. If it's already the most-forward
        // unit (nothing `ahead`), hold beside the nearest soldier. Never hold merely beside a straggler
        // BEHIND the VIP — that laggard/VIP mutual wait froze the asset at spawn for a whole match.
        bool besideForward = ahead.Count > 0
            ? ahead.Any(s => Util.ChebyDist(Vip.X, Vip.Y, s.X, s.Y) <= 1)
            : soldiers.Any(s => Util.ChebyDist(Vip.X, Vip.Y, s.X, s.Y) <= 1);
        // APEX W8: the leash walks REAL steps through OnUnitEnteredTile now, so a burning route sears
        // the win-condition asset. Hoisted "is any tile burning" gates the whole two-pass machinery —
        // the common fire-free board runs exactly one pass on the plain occupancy map.
        bool anyFire = false;
        for (int x = 0; x < Grid.W && !anyFire; x++)
            for (int y = 0; y < Grid.H && !anyFire; y++)
                if (Grid.Fire[x, y] > 0) anyFire = true;
        var foes = Enemies.Where(e => e.Alive && e.Active && e.Ammo > 0).ToList();   // active shooters (hoisted)

        // One leash pass over a given passability rule. `avoidFire` treats burning tiles as walls, so
        // Dijkstra itself DETOURS around a fire field (a destination-score penalty alone can't do it:
        // the hazard-blind came-from paths cross the fire even when a clean route exists). Returns true
        // when the pass RESOLVED the turn (issued a move, or held for a legitimate reason).
        bool LeashPass(bool avoidFire)
        {
            Func<int, int, bool> blocked = avoidFire
                ? ((x, y) => IsOccupiedByOther(x, y, Vip) || Grid.IsFire(x, y))
                : ((x, y) => IsOccupiedByOther(x, y, Vip));
            // reachable tiles within ONE move (VIP.MoveBudget), never onto an occupied / off-board /
            // non-floor tile (CostMap only relaxes walkable floor and honours the blocker).
            var cost = Grid.CostMap(Vip.X, Vip.Y, blocked, out var cameFrom, Vip.MoveBudget);
            // If a FREE evac tile is reachable THIS move, step straight into the zone — that is the win,
            // and it stops the "walled one lane short of the corner while soldiers crowd the doorway"
            // stall outright.
            (int x, int y)? reachEvac = null; int reachEvacCost = int.MaxValue;
            foreach (var t in EvacZone)
            {
                if (IsOccupiedByOther(t.x, t.y, Vip)) continue;
                int c = cost[t.x, t.y];
                if (c > 0 && c < reachEvacCost) { reachEvacCost = c; reachEvac = t; }
            }
            if (reachEvac != null) { EnqueueLeashMove(cameFrom, reachEvac.Value.x, reachEvac.Value.y); return true; }
            if (besideForward) return true;                    // tucked beside the SPEARHEAD, zone not yet in reach
            // Measure progress by the ACTUAL walkable path distance to the anchor (a Dijkstra field FROM
            // it, under the SAME passability rule), not Chebyshev — so the VIP steps correctly AROUND
            // walls/screens/fire toward the squad instead of stalling when the straight line is blocked.
            var goalField = Grid.CostMap(anchor.X, anchor.Y, blocked, out _, 9999);
            int hereDist = goalField[Vip.X, Vip.Y];
            int bx = -1, by = -1; float bestScore = float.NegativeInfinity;
            for (int x = 0; x < Grid.W; x++)
                for (int y = 0; y < Grid.H; y++)
                {
                    if (cost[x, y] < 0) continue;                  // unreachable this turn
                    if (x == Vip.X && y == Vip.Y) continue;        // must actually move
                    int d = goalField[x, y];
                    if (d < 0) continue;                           // can't reach the anchor from here at all
                    if (hereDist >= 0 && d >= hereDist) continue;  // only tiles that close the path gap
                    // safety: prefer cover from the nearest active shooter + tiles no active foe can see.
                    float safety = 0f;
                    var near = foes.OrderBy(e => Util.ChebyDist(x, y, e.X, e.Y)).FirstOrDefault();
                    if (near != null) safety += Grid.GetCover(x, y, near.X, near.Y).Level * 3f;
                    if (!foes.Any(e => Grid.HasLineOfSight(e.X, e.Y, x, y))) safety += 6f;   // fully unseen tile
                    // APEX W8 hazards (fallback pass only — the avoid-fire pass can't touch fire at all):
                    // when fire is UNAVOIDABLE, still steer to the route that burns least — penalize a
                    // burning destination (the Ai.cs -60 pattern) and each burning tile the reconstructed
                    // route enters (every tile ENTRY ticks the hazard).
                    if (Grid.IsFire(x, y)) safety -= 60f;
                    // C4 / MAGMA: the escorted asset must not be leashed onto a thermal vent, and
                    // must not be routed THROUGH one when a cooler lane exists (every tile ENTRY
                    // sears — same reason the fire route term above exists). CostMap has already
                    // made the crossing dear, so this only shapes the choice among tiles it allowed.
                    if (Grid.IsVent(x, y)) safety -= 60f;
                    if (Grid.AnyVent && Terrain.Enabled)
                        foreach (var (rx, ry) in Grid.ReconstructPath(cameFrom, Vip.X, Vip.Y, x, y))
                            if ((rx != x || ry != y) && Grid.IsVent(rx, ry)) safety -= 40f;
                    if (anyFire && !avoidFire)
                        foreach (var (rx, ry) in Grid.ReconstructPath(cameFrom, Vip.X, Vip.Y, x, y))
                            if ((rx != x || ry != y) && Grid.IsFire(rx, ry)) safety -= 60f;
                    // APEX W8 stealth-grief guard: routing through OnUnitEnteredTile also runs the pod-wake
                    // (CheckPodActivation) and concealment-break (RevealRange) checks the teleport skipped.
                    // An auto-move the player never ordered must not wake a sleeping pod or blow stealth.
                    // Penalize ONLY what actually bites (a penalty, not a veto — if every closing tile is
                    // bad the leash still takes the least-bad one, so it can never stall):
                    //   * dormant pod — sight-wake needs the squad REVEALED (CheckPodActivation no-ops
                    //     under concealment: creeping past sleepers is the stealth race working as
                    //     designed) plus proximity AND line of sight. A blanket radius penalty made the
                    //     leash tiptoe around every sleeper it could safely pass, ballooning Escort turns.
                    //   * active foe while CONCEALED — stepping into RevealRange blows squad stealth.
                    foreach (var e in Enemies)
                    {
                        if (!e.Alive) continue;
                        if (!e.Active)
                        {
                            // ... and only when the VIP would be the FIRST to wake it: a pod already
                            // inside a SOLDIER's sight/wake bubble is waking on the squad's own advance
                            // regardless, so the asset gains nothing by tiptoeing around it (that extra
                            // caution was measured crawling deep escorts to ~17 turns).
                            if (!SquadConcealed && Util.TileDist(x, y, e.X, e.Y) <= AlertRange + 1
                                && Grid.HasLineOfSight(x, y, e.X, e.Y)
                                && !soldiers.Any(s => Util.TileDist(s.X, s.Y, e.X, e.Y) <= AlertRange
                                                      && Grid.HasLineOfSight(s.X, s.Y, e.X, e.Y)))
                                safety -= 40f;
                        }
                        else if (SquadConcealed && Util.TileDist(x, y, e.X, e.Y) <= RevealRange + 1) safety -= 40f;
                    }
                    // progress dominates (the leash must converge), then safety, then a mild cost tie-break.
                    float score = (hereDist - d) * 2f + safety - cost[x, y] * 0.1f;
                    if (score > bestScore) { bestScore = score; bx = x; by = y; }
                }
            if (bx < 0) return false;                          // nothing closes under THIS passability rule
            EnqueueLeashMove(cameFrom, bx, by);
            return true;
            // (win detection stays with the normal CheckEnd calls after the player's actions — CheckEnd is
            //  gated on an empty anim queue, so it fires right after the leash steps finish playing.)
        }

        // Pass 1 detours around fire; pass 2 (only when fire exists AND pass 1 found no way to act) is
        // the old hazard-blind map, so a fully fire-walled lane still moves — burning beats stalling,
        // and the in-loop penalties pick the least-burning route. On a fire-free board this is exactly
        // one pass. If neither pass acts, hold this turn (walls/crowding — same as the pre-W8 hold).
        if (!LeashPass(anyFire) && anyFire) LeashPass(false);
    }

    /// APEX W8 — the leash walks REAL MoveStepAnims along the already-computed CostMap came-from path
    /// (exactly as ActivatePod's reveal-scatter does for enemies) instead of teleporting via direct
    /// X/Y writes. Enemy overwatch reactions, bleed/fire ticks, concealment breaks and pod wakes all
    /// compose for free — OnUnitEnteredTile fires per tile, same as a player-ordered move. MoveStepAnim
    /// spends no MoveBudget/actions, matching the old teleport's economy; a small tag-along puff marks
    /// the auto-move (feel only).
    void EnqueueLeashMove((int, int)[,] cameFrom, int tx, int ty)
    {
        Fx.Burst(Vip.Pos, Pal.VipGold, 8, 120f, 0.35f, 3f);
        foreach (var (px, py) in Grid.ReconstructPath(cameFrom, Vip.X, Vip.Y, tx, ty))
            Enqueue(new MoveStepAnim(Vip, px, py), Team.Player);
    }

    // ---------------- squad coordination ----------------
    // Computed ONCE per enemy turn (right after _aiUnits is snapshotted in EndPlayerTurn).
    // Produces two shared, ADVISORY hints that Ai.Plan reads to act as a coordinated squad
    // rather than a pack of independent greedy units:
    //   * EnemyFocus           — the soldier the squad should collapse on (focus fire).
    //   * PlayerOverwatchTiles — tiles a live player overwatch currently threatens, so units
    //                            can route around the kill zone (overwatch-aware movement).
    // Both are biases only; per-unit scoring still lets the fundamentals dominate, so no enemy
    // is ever forced into a no-progress choice (stall/timeout invariants are preserved).
    /// UNDERTOW W4: true when this unit can EXPOSE a soldier THIS turn (a setup verb) — a SAPPER (breach
    /// cover), a STRIKER (leap-shove), or any enemy standing adjacent to an IN-COVER soldier it could shove
    /// out. These are ordered to act BEFORE the finishers so the pod collapses on the opening they create.
    bool IsSetupUnit(Unit e)
    {
        if (e.Cls == "SAPPER" || e.Cls == "STRIKER") return true;
        foreach (var p in Players)
            if (p.Alive && !p.IsVip && Util.ChebyDist(e.X, e.Y, p.X, p.Y) == 1
                && Grid.GetCover(p.X, p.Y, e.X, e.Y).Level > 0) return true;
        return false;
    }

    void PlanEnemySquad()
    {
        // ---- 1. shared focus target ----------------------------------------------------
        // Pick the soldier most worth concentrating fire on: low effective HP (closest to a
        // kill), exposed (little/no cover from the squad's vantage), the VIP, and how many of
        // our active shooters can currently bring fire on it (a target several enemies can hit
        // is collapsible THIS turn). Perfect-information: we only read public live state.
        EnemyFocus = null;
        var soldiers = AlivePlayers();
        // shooters that actually contribute to a focus-fire collapse (MEDIC heals, SAPPER
        // demolishes — neither converges fire, so they don't define the priority target).
        var shooters = _aiUnits.Where(e => e.Alive && e.Active && e.Ammo > 0
                                        && e.Cls != "MEDIC" && e.Cls != "SAPPER").ToList();
        float bestScore = float.NegativeInfinity;
        foreach (var p in soldiers)
        {
            // a caged captive (Rescue) is invulnerable until freed — CanTarget blocks every
            // shot on it, so it can never be collapsed; skip it so the focus lands on a
            // shootable soldier instead of an inert target (review #6).
            if (p.IsVip && CaptiveLocked) continue;
            // FUL-7 (review F1): a DOWNED body scores the 60-pt near-dead base + exposure and
            // would usually WIN the focus pick — but Ai.Plan excludes downed from its targets,
            // so every focus bonus (kill-press, crossfire pulls) would then apply to NOBODY:
            // the coordination layer silently switched off while a body was down. The downed
            // exit every enemy-attention seam.
            if (p.Downed) continue;
            // how many active enemies can hit p right now (mirrors CanTarget exactly)
            int shootersOnTarget = 0;
            float bestHitOnTarget = 0f;
            foreach (var e in shooters)
            {
                if (!CanTarget(e, p)) continue;
                shootersOnTarget++;
                int h = Combat.ComputeOdds(Grid, e, p).HitChance;
                if (h > bestHitOnTarget) bestHitOnTarget = h;
            }

            // effective-HP term: the less HP, the juicier (a soldier near death is the kill).
            float hpFrac = p.MaxHp > 0 ? (float)p.Hp / p.MaxHp : 1f;
            float score = (1f - hpFrac) * 60f;                 // 0 (full) .. 60 (near-dead)
            // exposure: no cover from the nearest shooter's angle => easier to drop
            Unit anchor = shooters.Count > 0 ? shooters.OrderBy(e => Util.TileDist(e.X, e.Y, p.X, p.Y)).First() : null;
            if (anchor != null && Grid.GetCover(p.X, p.Y, anchor.X, anchor.Y).Level == 0) score += 25f;
            if (p.IsVip) score += 70f;                          // the asset is always the prize
            score += shootersOnTarget * 22f;                    // collapsible THIS turn
            score += bestHitOnTarget * 0.25f;                   // and we can actually land it
            // a target nobody can currently hit is a weak focus; only pick it as a fallback
            if (shootersOnTarget == 0) score -= 40f;

            if (score > bestScore) { bestScore = score; EnemyFocus = p; }
        }

        // ---- 2. player overwatch kill-zone map -----------------------------------------
        // Mirror the EXACT reaction test in OnUnitEnteredTile: a watcher w reacts to a unit
        // entering tile T iff w is on overwatch, has ammo, hasn't reacted, and CanTarget(w, T).
        // CanTarget = within w.Weapon.MaxRange AND HasLineOfSight(w -> T, commanding-if-2-tier).
        // We replicate that per tile so the AI's threat model is TRUTHFUL (no phantom denial).
        PlayerOverwatchTiles.Clear();
        // Mirror CanTarget's reaction gate EXACTLY (Alive && OnOverwatch && !ReactedThisTurn
        // && Ammo>0). We deliberately do NOT add a Disoriented filter the real reaction lacks
        // (review #3): today disorient only comes from FLASH, which clears OnOverwatch, so the
        // case is unreachable — but mirroring CanTarget keeps the model truthful if a future
        // non-flash disorient source is ever added (so the AI never treats a watched tile as safe).
        var watchers = Players.Where(w => w.Alive && w.OnOverwatch && !w.ReactedThisTurn
                                       && w.Ammo > 0).ToList();
        if (watchers.Count > 0)
        {
            for (int x = 0; x < Grid.W; x++)
                for (int y = 0; y < Grid.H; y++)
                {
                    foreach (var w in watchers)
                    {
                        if (w.X == x && w.Y == y) continue;
                        if (Util.TileDist(w.X, w.Y, x, y) > w.Weapon.MaxRange) continue;
                        bool commanding = Grid.HeightAt(w.X, w.Y) - Grid.HeightAt(x, y) >= 2;
                        if (!Grid.HasLineOfSight(w.X, w.Y, x, y, commanding)) continue;
                        // COUNTERPLAY: a FOCUSED watcher threatens only its cone, so the AI reads (and can
                        // exploit) the blind zone — mirror the exact reaction gate in OnUnitEnteredTile.
                        if (w.OwFocused && !InOwCone(w, x, y)) continue;
                        PlayerOverwatchTiles.Add((x, y));
                        break;   // one watcher is enough to mark the tile threatened
                    }
                }
        }
    }


    // ── C5 THE HARD EDGES — THE ENEMY-TURN DEADLOCK GUARD ─────────────────────────────────────
    /// W9 shipped a within-turn idle guard and it covers the PLAYER turn only (`AutoIdleGuard`
    /// runs from `UpdatePlayer`, autoplay-only). The enemy turn had nothing: a stage machine that
    /// stops advancing, or an animation that never reports done, freezes the game until something
    /// outside it gives up — the harness frame cap after ~13.5k frames in a batch (reported as
    /// `RESULT: TIMEOUT` with no diagnosis), and NOTHING AT ALL in front of a player, who simply
    /// watches a hostile that never acts. This is the missing half.
    ///
    /// It runs in REAL PLAY, not only in autoplay, because that is where the failure is worse: a
    /// batch loses a run, a player loses the session. It costs one integer hash per frame of the
    /// enemy turn.
    ///
    /// WHERE IT SITS, and why that is the whole design: in `Update`, BEFORE the animation pump —
    /// not inside `UpdateEnemy`. While `_anims` is non-empty `Update` returns before the phase
    /// switch, so a guard inside `UpdateEnemy` is structurally blind to the "an animation never
    /// completes" half of the deadlock — the same shape of blindness W9 found in the turn-boundary
    /// guard. From `Update` it sees both halves.
    ///
    /// WHAT COUNTS AS PROGRESS: the staging index, the stage, the queue depth and the head
    /// animation's type, plus every unit's position/HP/actions/ammo/state. Any real activity moves
    /// one of them. A banner beat (`BannerTimer > 0.55`, which `UpdateEnemy` deliberately waits
    /// out) resets the counter, so a telegraph is never mistaken for a stall.
    public const int EnemyStallFrames = 480;   // 8 s at 60 fps of an enemy turn with NOTHING moving

    /// Diagnostics. `EnemyStallFires` is the count for this process; `LastEnemyStall` is the line
    /// the guard printed, which names the unit and the planner branch that stalled.
    public static int EnemyStallFires;
    public static string LastEnemyStall = "";
    /// Falsifiability lever + the escape hatch's own off switch (ENEMYSTALLTEST leg C runs the
    /// wedged turn with this false and asserts the game hangs, which is what the pre-guard tree
    /// does). Never false in play.
    public static bool EnemyStallGuardOn = true;
    /// Harness-only fault injectors. There is no way to write an honest test for a deadlock guard
    /// without a deadlock, and the two halves of the deadlock need DIFFERENT wedges:
    ///   `DebugEnemyWedge`     — `UpdateEnemy` returns without doing anything: the STAGE MACHINE
    ///                           stops advancing with an EMPTY queue.
    ///   `DebugEnemyAnimWedge` — an animation is enqueued that never reports done: `Update` then
    ///                           returns at the animation pump and never reaches the phase switch
    ///                           at all. THIS is the half a guard inside `UpdateEnemy` cannot see,
    ///                           and it is the reason this guard runs from `Update` instead
    ///                           (C5 review E3: the first version of ENEMYSTALLTEST only wedged the
    ///                           stage machine, so it PASSED on the placement it argues against).
    public static bool DebugEnemyWedge;
    public static bool DebugEnemyAnimWedge;

    int _enemyStallSig, _enemyStallFrames;

    /// The branch a plan intends, named. Deliberately lives HERE and not on `EnemyPlan`: `Ai.cs`
    /// is another wave's lane this wave must not touch.
    public static string PlanBranch(EnemyPlan p)
    {
        if (p == null) return "none";
        if (p.SiegeCharge != null) return "siege";
        if (p.SapTile != null) return "sap";
        if (p.RelockTile != null) return "relock";
        if (p.HealTarget != null) return "heal";
        if (p.Grenade) return "grenade";
        if (p.UseItem) return "item";
        if (p.ShoveTarget != null) return "shove";
        if (p.Brace) return "brace";
        if (p.ShootTarget != null) return "shoot";
        if (p.Overwatch) return "overwatch";
        if (p.Reload) return "reload";
        if (p.Hunker) return p.IdleRepair ? "hunker(terminal-else)" : "hunker";
        if (p.Path.Count > 0) return "move-only";
        return "empty";
    }

    void EnemyStallGuard()
    {
        if (!EnemyStallGuardOn) return;
        if (BannerTimer > 0.55f) { _enemyStallFrames = 0; return; }   // the telegraph beat, not a stall

        int sig = 17;
        sig = sig * 31 + _aiIdx;
        sig = sig * 31 + (int)_aiStage;
        sig = sig * 31 + _anims.Count;
        if (_anims.Count > 0) sig = sig * 31 + _anims[0].GetType().Name.Length * 7919;
        foreach (var e in Enemies)
            sig = sig * 31 + (e.Hp * 7 + e.X * 31 + e.Y + e.ActionsLeft * 8191 + e.Ammo * 127
                              + (e.Alive ? 1 : 0) + (e.OnOverwatch ? 2 : 0) + (e.Hunkered ? 4 : 0));
        foreach (var p in Players)
            sig = sig * 31 + (p.Hp * 7 + p.X * 31 + p.Y + p.ActionsLeft * 3
                              + (p.Alive ? 1 : 0) + (p.Downed ? 2 : 0));
        if (sig != _enemyStallSig) { _enemyStallSig = sig; _enemyStallFrames = 0; return; }
        if (++_enemyStallFrames < EnemyStallFrames) return;
        _enemyStallFrames = 0;

        // FAIL LOUD. The point of this guard is not that the game keeps running — it is that the
        // next agent gets a NAME instead of a frame count. Every field here answers a question the
        // W9 TIMEOUT could not: which unit, at which stage of its turn, intending which branch,
        // with what still in the animation queue.
        var stuck = (_aiUnits != null && _aiIdx >= 0 && _aiIdx < _aiUnits.Count) ? _aiUnits[_aiIdx] : null;
        string who = stuck == null
            ? $"<none: index {_aiIdx} of {(_aiUnits == null ? 0 : _aiUnits.Count)}>"
            : $"{stuck.Name}/{stuck.Cls} #{_aiIdx} at ({stuck.X},{stuck.Y}) hp={stuck.Hp} "
              + $"act={stuck.ActionsLeft} ammo={stuck.Ammo}{(stuck.Alive ? "" : " DEAD")}";
        string head = _anims.Count > 0 ? _anims[0].GetType().Name : "-";
        LastEnemyStall = $"ENEMY-STALL: enemy turn made no progress for {EnemyStallFrames} updates. "
            + $"stage={_aiStage} unit={who} plan={PlanBranch(_aiPlan)} anims={_anims.Count} head={head} "
            + $"turn={_turnCount} mission={(_run != null ? _run.Mission : 0)} mode={Mode}";
        EnemyStallFires++;
        Console.Error.WriteLine(LastEnemyStall);

        // ...and then GET OUT. A wedged animation is dropped, the stalled unit forfeits its turn,
        // and the staging index advances; if that exhausts the list the turn ends here rather than
        // handing control back to the machine that just failed to advance it.
        // The queue is dropped — and this is the codebase's ONLY production `_anims.Clear()`, so it
        // owes the board a repair that no other caller has ever had to make: `MoveStepAnim` commits
        // `Unit.X/Y` when it FINISHES, so dropping one in flight leaves the unit's logical tile at
        // the step's origin with its drawn position frozen between two tiles. Re-sync every unit to
        // the tile it is actually standing on (C5 review).
        if (_anims.Count > 0)
        {
            _anims.Clear();
            foreach (var u in Players) u.SyncPos();
            foreach (var e in Enemies) e.SyncPos();
        }
        ClearIntent();
        if (stuck != null) stuck.ActionsLeft = 0;
        _aiIdx++;
        _aiStage = AiStage.PickNext;
        if (_aiUnits == null || _aiIdx >= _aiUnits.Count) StartPlayerTurn();
    }

    // ---------------- enemy turn ----------------
    void UpdateEnemy()
    {
        if (DebugEnemyWedge) return;    // C5 harness-only fault injector (see EnemyStallGuard)
        if (DebugEnemyAnimWedge && _anims.Count == 0)
            Enqueue(new StuckAnim(), Team.Enemy);   // C5: the animation half of the deadlock
        if (BannerTimer > 0.55f) return; // let banner breathe before acting

        if (_aiStage == AiStage.PickNext)
        {
            while (_aiIdx < _aiUnits.Count && !_aiUnits[_aiIdx].Alive) _aiIdx++;
            if (_aiIdx >= _aiUnits.Count) { StartPlayerTurn(); return; }

            var e = _aiUnits[_aiIdx];
            // elite boss: enrage once when first acting below half HP
            if (e.Cls == "ELITE" && !e.Enraged && e.Hp <= e.MaxHp / 2)
            {
                e.Enraged = true;
                e.Aim += 15; e.Mobility += 2;
                Fx.PopText(e.Pos + new Vector2(0, -34), "ENRAGED", Pal.Foe, 20f);
                Fx.AddShake(8f);
                ShowBanner(e.Name + " ENRAGED", true);
            }
            // SIGNAL W5 — the Legion BREAKER's SECOND rage tier: an already-enraged RagesTwice
            // elite FRENZIES once when first acting at <=25% HP (+aim/+mob again, and Ai.Plan
            // flips it to the berserker rush). The `else if` means a boss burst straight from
            // >50% past both thresholds pops ENRAGED this act and FRENZY on its NEXT act — two
            // readable beats, never both in one act / never a silent double-spike. Telegraphs
            // the finish-it-NOW decision: leaving the breaker alive at a sliver is the one
            // thing you must not do.
            else if (e.RagesTwice && e.Enraged && !e.Frenzied && e.Hp * 4 <= e.MaxHp)
            {
                e.Frenzied = true;
                e.Aim += 10; e.Mobility += 2;
                Fx.PopText(e.Pos + new Vector2(0, -34), "FRENZY", Pal.Foe, 22f);
                Fx.AddShake(11f);
                ShowBanner(e.Name + " FRENZIES", true);
            }
            // UNDERTOW W4 — incremental coordination: recompute the shared focus against the CURRENT board
            // right before this unit plans, so a shove/breach an EARLIER unit just landed (exposing a
            // soldier) redirects the pod onto that fresh opening THIS turn — vs the once-per-turn snapshot
            // that never saw the setup. Advisory only (Ai.Plan reads focus as a bias), so no unit is ever
            // forced into a no-progress choice; the stall/TIMEOUT invariants hold.
            PlanEnemySquad();
            _aiPlan = Ai.Plan(this, e);

            // TELEGRAPH (non-autoplay only): before the unit moves/acts, hold a brief beat and
            // show its INTENT (move path + target + threatened tiles). The telegraph uses the SAME
            // _aiPlan that executes next, so it never lies. AutoPlay skips this entirely — no extra
            // WaitAnim, no intent state — so the smoke test's frame counts stay unchanged.
            if (!AutoPlay)
            {
                IntentUnit = e;
                IntentPlan = _aiPlan;
                IntentDest = _aiPlan.Path.Count > 0 ? _aiPlan.Path[^1] : (e.X, e.Y);
                Enqueue(new WaitAnim(TelegraphBeat), Team.Enemy);
                _aiStage = AiStage.Telegraph;
                return;
            }

            EnqueuePlannedMove(e);
            _aiStage = AiStage.ActAfterMove;
            return;
        }

        if (_aiStage == AiStage.Telegraph)
        {
            // the telegraph beat has elapsed (queue drained) — clear the intent so it doesn't
            // linger past the unit's action, then enqueue the planned move and proceed to act.
            var e = _aiUnits[_aiIdx];
            ClearIntent();
            if (e.Alive) EnqueuePlannedMove(e);
            _aiStage = AiStage.ActAfterMove;
            return;
        }

        if (_aiStage == AiStage.ActAfterMove)
        {
            var e = _aiUnits[_aiIdx];
            // W2 THE OPPONENT ACTS — the idle detector's baseline. EVERY branch in the chain below
            // changes ActionsLeft (the shot and the reload decrement it; the other ten zero it), so
            // "ActionsLeft > 0 AND unchanged" is an exact structural test for "no branch fired" —
            // the auditor's definition of an idle act — without a `fired` flag threaded through all
            // twelve branch bodies.
            int actionsBefore = e.ActionsLeft, ammoBefore = e.Ammo;
            // THE DECISION CENSUS — ONE instrument, TWO consumers. C2 and C5 independently
            // instrumented these same branches (C5 as a coverage census, C2 as the enemy decision
            // mix) and disagreed on the terminal else. Merging both verbatim would have put two
            // parallel censuses over one code path, free to diverge silently, with BOTH waves'
            // tests green — the exact defect class this project keeps rediscovering. So: every
            // branch writes ONE variable, the sentinel is resolved ONCE at the publish site below,
            // and both consumers are handed the resolved label. Named at the branch rather than
            // re-derived from state afterwards, because a shot that then repositions into cover
            // would misclassify under any after-the-fact reading.
            // It exists because the OVERWATCH branch was measured firing 8 times in 8083 acts and
            // no test in the project could have noticed.
            _actBranch = "none";
            // W2 REVIEW FIX — the BLEED-OUT WINDOW, and it is the whole shape of this wave's number.
            // When every surviving soldier is on the floor, Ai.Plan takes its `players.Count == 0`
            // early return (Ai.cs:78, after the FUL-7 LAST LIGHT downed filter): there is nothing to
            // target and nobody who can act. EVERY hostile then idles, by design — and that window
            // supplied 86% of the raw idle count this wave first published as though it were combat
            // paralysis. It is not a defect and it must not be "fixed": a squad bleeding out is the
            // game's most dramatic beat, and filling it with hostiles dashing and barking HUNKERED
            // turn after turn is a feel regression the price measurement is STRUCTURALLY BLIND to
            // (no soldier can act, so run completion cannot move). So the repair below — and its
            // pops and SFX — are gated on there being somebody left standing to act against, and
            // the invariant this wave actually enforces is "no CONTESTED act-opportunity ends
            // unspent". The all-downed acts stay silent, exactly as they were pre-wave.
            int standing = 0;
            foreach (var p in Players) if (p.Alive && !p.Downed) standing++;
            if (e.Alive)
            {
                if (_aiPlan.SiegeCharge != null && e.HasSiege && e.ChargeTurns == 0 && e.ActionsLeft > 0)
                {
                    _actBranch = "siege";
                    // SIEGE charges a telegraphed strike: NO damage now — the 3x3 danger zone IS the
                    // telegraph (drawn for the whole next player turn); it lands at the top of the
                    // following enemy turn (TickSiegeStrikes). Spends the action -> no dead turn.
                    _actBranch = "siege";
                    e.ActionsLeft = 0;
                    var (cx, cy) = _aiPlan.SiegeCharge.Value;
                    e.ChargeX = cx; e.ChargeY = cy;
                    e.ChargeTurns = SiegeFuse;
                    Fx.PopText(e.Pos + new Vector2(0, -30), "CHARGING STRIKE", Pal.Foe, 16f);
                    ShowBanner("ARTILLERY INCOMING", true);
                    Audio.Play("over");                          // a charge "whine" stand-in
                    Enqueue(new WaitAnim(0.25f), Team.Enemy);
                }
                else if (_aiPlan.SapTile != null && e.ActionsLeft > 0 &&
                    Grid.IsCover(_aiPlan.SapTile.Value.x, _aiPlan.SapTile.Value.y) &&
                    Util.ChebyDist(e.X, e.Y, _aiPlan.SapTile.Value.x, _aiPlan.SapTile.Value.y) <= 1)
                {
                    _actBranch = "sap";
                    e.ActionsLeft = 0;
                    var (sx, sy) = _aiPlan.SapTile.Value;
                    Fx.PopText(e.Pos + new Vector2(0, -30), "BREACH", Pal.Foe, 16f);
                    var hit = Grid.DamageCover(sx, sy, Grid.HighCoverHp);  // demolish a full level
                    if (hit != Grid.CoverHit.None) CoverHitFx(sx, sy, hit);
                    Fx.AddShake(5f);
                    Enqueue(new WaitAnim(0.25f), Team.Enemy);
                }
                else if (_aiPlan.RelockTile != null && e.ActionsLeft > 0 &&
                    CanRelock(e, _aiPlan.RelockTile.Value))
                {
                    _actBranch = "relock";
                    // SIGNAL W8 — CUSTODIAN: standing at the objective, spend the action undoing
                    // one step of the player's progress (validated NOW, post-move — a re-blown
                    // charge or a dead keeper mid-path falls through to the generic branches).
                    _actBranch = "relock";
                    e.ActionsLeft = 0;
                    DoRelock(e, _aiPlan.RelockTile.Value);
                    Enqueue(new WaitAnim(0.25f), Team.Enemy);
                }
                else if (_aiPlan.HealTarget != null && _aiPlan.HealTarget.Alive && e.ActionsLeft > 0 &&
                    _aiPlan.HealTarget.Hp < _aiPlan.HealTarget.MaxHp &&
                    Util.TileDist(e.X, e.Y, _aiPlan.HealTarget.X, _aiPlan.HealTarget.Y) <= Ai.HealRange &&
                    Grid.HasLineOfSight(e.X, e.Y, _aiPlan.HealTarget.X, _aiPlan.HealTarget.Y))
                {
                    _actBranch = "heal";
                    e.ActionsLeft = 0;
                    Fx.PopText(e.Pos + new Vector2(0, -30), "MEDIC", Pal.Good, 16f);
                    Enqueue(new WaitAnim(0.2f), Team.Enemy);
                    Enqueue(new HealAnim(e, _aiPlan.HealTarget), Team.Enemy);
                }
                else if (_aiPlan.Grenade && e.Grenades > 0 && e.ActionsLeft > 0 &&
                    Util.TileDist(e.X, e.Y, _aiPlan.GrenX, _aiPlan.GrenY) <= GrenadeRange)
                {
                    _actBranch = "grenade";
                    e.Grenades--;
                    e.ActionsLeft = 0;
                    Fx.PopText(e.Pos + new Vector2(0, -30), "FRAG OUT", Pal.Foe, 16f);
                    Enqueue(new WaitAnim(0.2f), Team.Enemy);
                    Enqueue(new GrenadeAnim(e, _aiPlan.GrenX, _aiPlan.GrenY), Team.Enemy);
                }
                else if (_aiPlan.UseItem && e.ItemCharge > 0 && e.ActionsLeft > 0 &&
                    Grid.InBounds(_aiPlan.ItemTx, _aiPlan.ItemTy) &&
                    Util.TileDist(e.X, e.Y, _aiPlan.ItemTx, _aiPlan.ItemTy) <= ItemRange)
                {
                    _actBranch = "item";
                    e.ItemCharge--;
                    // item use takes one action but does NOT necessarily end the turn,
                    // so the enemy can still shoot after laying smoke (if ShootTarget != null).
                    // However we set ActionsLeft=0 here so it acts like a grenade (one big
                    // action per turn), keeping the autopilot loop predictable + no TIMEOUT risk.
                    e.ActionsLeft = 0;
                    string label = e.EnemyItem == ItemKind.Smoke ? "SMOKE OUT" : "FLASH OUT";
                    Fx.PopText(e.Pos + new Vector2(0, -30), label, Pal.RGBA(180, 190, 200), 16f);
                    Enqueue(new WaitAnim(0.18f), Team.Enemy);
                    if (e.EnemyItem == ItemKind.Smoke)
                        Enqueue(new SmokeAnim(e, _aiPlan.ItemTx, _aiPlan.ItemTy), Team.Enemy);
                    else
                        Enqueue(new FlashAnim(e, _aiPlan.ItemTx, _aiPlan.ItemTy), Team.Enemy);
                }
                else if (_aiPlan.ShoveTarget != null && _aiPlan.ShoveTarget.Alive && e.ActionsLeft > 0 &&
                    !_aiPlan.ShoveTarget.IsVip &&
                    Util.ChebyDist(e.X, e.Y, _aiPlan.ShoveTarget.X, _aiPlan.ShoveTarget.Y) == 1)
                {
                    _actBranch = "shove";
                    // AI SHOVE (Wave 5): slam an adjacent covered soldier 1 tile to expose it (or deal
                    // collision damage if it's pinned). Reuses the player's ShoveAnim verbatim; the action
                    // is spent here (no TIMEOUT). The exposed soldier is then a soft target for the pod.
                    _actBranch = "shove";
                    e.ActionsLeft = 0;
                    var t = _aiPlan.ShoveTarget;
                    int sdx = Math.Sign(t.X - e.X), sdy = Math.Sign(t.Y - e.Y);
                    Fx.PopText(e.Pos + new Vector2(0, -30), "SHOVE", Pal.Foe, 16f);
                    Enqueue(new WaitAnim(0.15f), Team.Enemy);
                    Enqueue(new ShoveAnim(e, t, sdx, sdy), Team.Enemy);
                }
                else if (_aiPlan.Brace && e.ActionsLeft > 0 && e.Ammo > 0 && !e.HasStatus(StatusKind.Disoriented))
                {
                    _actBranch = "brace";
                    // FUL-8 PIKEMAN: plant the braced lane — the exact flag set the player's own BRACE+FOCUS
                    // arms, so OnUnitEnteredTile reacts through the identical (COMBATTEST-pinned) path: the
                    // first soldier through the cone eats a halved, no-crit, STAGGERING reaction. The plant
                    // lives one round (the enemy BeginTurn wipes OnOverwatch/OwBrace/OwFocused), so holding
                    // the lane costs the PIKEMAN its action EVERY turn — symmetric movement-economy trade.
                    _actBranch = "brace";
                    e.OnOverwatch = true; e.OwBrace = true; e.OwFocused = true;
                    e.OwDirX = _aiPlan.BraceDirX; e.OwDirY = _aiPlan.BraceDirY;
                    // face down the lane: the silhouette's pike IS the direction read — without this
                    // the figure keeps its walk-in facing and points away from its own cone.
                    e.Facing = MathF.Atan2(e.OwDirY, e.OwDirX);
                    e.ActionsLeft = 0;
                    Fx.PopText(e.Pos + new Vector2(0, -30), "BRACED", Pal.Foe, 16f);
                    Audio.Play("over");
                }
                else if (_aiPlan.ShootTarget != null && _aiPlan.ShootTarget.Alive &&
                    e.ActionsLeft > 0 && e.Ammo > 0 && CanTarget(e, _aiPlan.ShootTarget))
                {
                    _actBranch = "shoot";
                    e.Ammo--;
                    // TEMPO: the enemy shot is 1 action and does NOT end the turn (mirrors the player).
                    e.FiredThisTurn = true;
                    e.MovedAfterFire = false;   // HORIZON: fired-and-stationary => exposed until it moves
                    e.ActionsLeft = Math.Max(0, e.ActionsLeft - 1);
                    var res = Combat.Resolve(Grid, e, _aiPlan.ShootTarget);
                    Enqueue(new WaitAnim(0.2f), Team.Enemy);
                    Enqueue(new ShotAnim(e, _aiPlan.ShootTarget, res), Team.Enemy);
                    TryEnemyReposition(e);   // shoot-then-reposition: duck to cover / advance with the spare action
                }
                else if (_aiPlan.Overwatch && e.ActionsLeft > 0 && e.Ammo > 0 && !e.HasStatus(StatusKind.Disoriented))
                {
                    _actBranch = "overwatch";
                    e.OnOverwatch = true; e.ActionsLeft = 0;
                    Fx.PopText(e.Pos + new Vector2(0, -30), "OVERWATCH", Pal.Accent, 16f);
                    Audio.Play("over");
                }
                else if (_aiPlan.Reload && e.ActionsLeft > 0 && e.Ammo < e.Weapon.Clip)
                {
                    _actBranch = "reload";
                    // W2 THE OPPONENT ACTS — the enemy AMMO ECONOMY, decided rather than defaulted
                    // into (docs/DESIGN.md §5.1). Hostiles used to be handed exactly one clip at spawn
                    // with no reload verb anywhere, so "dry" was PERMANENT. Measured on this tree over
                    // CONTESTED act-opportunities only (at least one soldier standing — an all-downed
                    // board idles every hostile by design): 9.1% of acts at n=16 campaigns and 3.3% at
                    // n=32 were made on an empty weapon, and ~61% of those produced nothing in both
                    // frames. That is the LARGER half of contested enemy paralysis. A dry gun is
                    // now RELOADED, on the player's own terms — one action, same clip refill as
                    // Game.DoReload — which makes running a hostile dry a real tempo window the player
                    // can bait and push into, instead of a unit that silently stops existing.
                    _actBranch = "reload";
                    e.Ammo = e.Weapon.Clip;
                    e.ActionsLeft = Math.Max(0, e.ActionsLeft - 1);
                    Fx.PopText(e.Pos + new Vector2(0, -30), "RELOADING", Pal.TxtDim, 16f);
                    Audio.Play("reload");
                    Enqueue(new WaitAnim(0.2f), Team.Enemy);
                    // a spare action after the mag change digs in behind it (never a free extra shot —
                    // choosing what to do with a fresh clip is W3's question, not this wave's).
                    if (e.ActionsLeft > 0) { e.Hunkered = true; e.ActionsLeft = 0; }
                }
                else if (_aiPlan.Hunker && e.ActionsLeft > 0)
                {
                    _actBranch = "hunker";
                    e.Hunkered = true; e.ActionsLeft = 0;
                    // W2: the enemy hunker was the one branch that fired in COMPLETE silence — no pop,
                    // no sound, only a small diamond in the status row. It reads identically to the
                    // paralysis this wave removes, so give it the same beat the player's own HUNKER has.
                    // `standing > 0`: never during the bleed-out window (see the note at the top of
                    // this stage) — 99.7% of the review's measured HUNKERED chorus was there.
                    if (AiIdleFix && standing > 0)
                    {
                        Fx.PopText(e.Pos + new Vector2(0, -30), "HUNKERED", Pal.Foe, 16f);
                        Audio.Play("hunker");
                    }
                }
                // W2 TERMINAL GUARANTEE: no branch fired and the unit still holds an action. Every
                // gate above is a plan-vs-board disagreement (the planned target died or slid out of
                // CanTarget, a PINNED clamp shortened the move out of range, a Disoriented unit's
                // watch was refused, ...). Ai.Plan's own terminal else covers the planner's side; this
                // covers the EXEC's, so the invariant holds structurally rather than by enumerating
                // the ways a plan can go stale. Reload if the gun is empty (the same economy
                // decision), else dig in — both are real, readable, mechanically live actions
                // (HUNKERED is -25 to hit against it and no crit).
                //
                // THE INVARIANT IS "no act-opportunity ends with NO BRANCH HAVING FIRED", and the
                // review corrected an earlier comment here that claimed the stronger "never ends
                // unspent". It does not: measured 30 of 963 acts (3.1%) end holding exactly one
                // action, via the SHOT branch decrementing 2 -> 1 and TryEnemyReposition then
                // declining at `curExp < 2.0f`. That is a covered shooter deciding to hold its
                // ground and it is the intended behaviour — but it is a spent branch, not a spent
                // action, and the two are not the same claim.
                else if (AiIdleFix && standing > 0 && e.ActionsLeft > 0 && e.ActionsLeft == actionsBefore)
                {
                    // C5's ammo split is kept over C2's single "staleplan" tag: these are two
                    // separately reachable paths with different reachability (terminal-hunker 4,
                    // terminal-reload 0 in 8083 acts), and collapsing them hides that one is dead.
                    // Stats maps the pair back onto "staleplan" at the publish site — see below.
                    _actBranch = e.Ammo <= 0 && e.Weapon.Clip > 0 ? "terminal-reload" : "terminal-hunker";
                    if (e.Ammo <= 0 && e.Weapon.Clip > 0)
                    {
                        e.Ammo = e.Weapon.Clip;
                        Fx.PopText(e.Pos + new Vector2(0, -30), "RELOADING", Pal.TxtDim, 16f);
                        Audio.Play("reload");
                    }
                    else
                    {
                        Fx.PopText(e.Pos + new Vector2(0, -30), "HUNKERED", Pal.Foe, 16f);
                        Audio.Play("hunker");
                    }
                    e.Hunkered = true;
                    e.ActionsLeft = 0;
                }
            }
            // W2 AIIDLETEST probe (harness-only; ALWAYS null in normal play, so this costs one null
            // check per enemy act). Fires once per act-opportunity with the plan that was executed and
            // the unit's pre-chain action/ammo counts, so the harness can classify idle / dry / no-target
            // without the game itself carrying a counter.
            if (ActProbe != null && e.Alive) ActProbe(e, _aiPlan, actionsBefore, ammoBefore, standing);
            // Resolve the sentinel ONCE, upstream of the fork, so NEITHER consumer sees "none".
            // C5's objection to a bare "none" is that it conflates the all-downed bleed-out window
            // with move-only acts (34.4% of acts at n=2740); C2's mix needs move and idle
            // separable, because `move` is a reported figure (~8.4%) and `idle` is a load-bearing
            // ZERO — its ABSENCE is the assertion. Resolving here satisfies both objections.
            string act = _actBranch == "none" ? (_aiPlan.Path.Count > 0 ? "move" : "idle") : _actBranch;
            LastActBranch = act;
            // Both consumers are called side by side at this ONE site. Stats is deliberately NOT
            // routed through BranchProbe: that is a single static delegate with one subscriber,
            // which AICOVTEST assigns and nulls, so whichever assigned second would silently win
            // and the loser would record nothing — the very divergence this reconciliation exists
            // to prevent. RecordEnemyDecision is already a no-op when Stats is disabled.
            if (BranchProbe != null) BranchProbe(act, _aiPlan, standing);
            if (Stats.Enabled && e.Alive)
                Stats.RecordEnemyDecision(StatsVerb(act), standing > 0,
                                          _aiPlan.ShotHit, _aiPlan.ShotExp, _aiPlan.Declined);
            _aiIdx++;
            _aiStage = AiStage.PickNext;
        }
    }

    // ── W2 THE OPPONENT ACTS ────────────────────────────────────────────────────────────────
    /// SIGHTLINE_AIIDLEFIX — the wave's single dial. `=0` restores the pre-W2 opponent EXACTLY
    /// (the planner ignores its own ammo, the no-shot fallback has no terminal else, and a dry
    /// hostile never reloads); proven identical by an R0diag pair at h0/b0 and h4/b10 whose
    /// balance JSONs diff empty outside the `harness` block.
    ///
    /// SHIPPED ON, and argued on the CONTESTED rate — not on the 32.4% unsplit figure this wave
    /// first published, which was 86% bleed-out window (see the note in ActAfterMove). Pre-wave a
    /// contested act-opportunity idled 6.2% of the time at n=16 campaigns and 3.8% at n=32; it is
    /// now 0.0% in both. Price, over 800 CRN-paired campaigns (5 heat rungs x 4 disjoint slot sets
    /// x 2 policies, base commit 4784803): run completion 25.2% -> 23.8% pooled, McNemar p=0.451
    /// on 25/19 discordant worlds; mission win-rate 78.94% -> 78.56% over ~1400 missions; soldier
    /// deaths per mission 1.340 -> 1.340. See docs/measurements/w2/.
    ///
    /// Mutable so SIGHTLINE_AIIDLETEST can run BOTH legs in one process.
    public static bool AiIdleFix = true;

    // ── C2 THE OPPONENT DECLINES ────────────────────────────────────────────────────────────
    /// SIGHTLINE_AIDECLINE — this wave's single dial. `=0` restores the pre-C2 opponent EXACTLY:
    /// Ai.cs's per-tile shot term goes back to the flat `100 + bestHit` constant that dominated
    /// every terrain term in the scorer, and the decline gate never runs, so a hostile pays ANY
    /// positional price for a line of fire and never holds one. (Note what `=0` does NOT restore:
    /// a wild-odds shooter. Measured over 15407 pre-change shots, only 0.4% were under 20% — the
    /// constant's damage was positional, not shot quality. See docs/DEVLOG.md §C2.)
    /// Shipped ON. Mutable so SIGHTLINE_DECLINETEST can run BOTH legs in one process, and so an
    /// R0diag chunk can prove the wave's telemetry inert.
    public static bool AiDecline = true;

    /// SIGHTLINE_AIIDLETEST probe (harness-only; ALWAYS null in normal play). Called once per
    /// enemy act-opportunity, immediately after the ActAfterMove branch chain:
    /// (unit, the plan it executed, ActionsLeft before the chain, Ammo before the chain, and the
    /// STANDING (alive, not downed) soldier count — without which an idle count cannot be read,
    /// because an all-downed board idles every hostile by design and supplied 86% of the raw rate).
    public static Action<Unit, EnemyPlan, int, int, int> ActProbe;

    /// C5 THE HARD EDGES — the branch each enemy act actually took (see the census note in
    /// ActAfterMove). `LastActBranch` is the last one for a reader/debugger; `BranchProbe` is the
    /// harness hook SIGHTLINE_AICOVTEST counts through: (branch, the plan it executed, the number
    /// of soldiers still standing — an all-downed board is not a decision).
    string _actBranch = "none";
    public static string LastActBranch = "none";
    public static Action<string, EnemyPlan, int> BranchProbe;

    /// Every branch the enemy exec chain can take, in chain order. The census asserts against
    /// THIS list, so a new verb that nobody sampled is a coverage failure the day it lands rather
    /// than three programs later.
    /// C2/C5 reconciliation: the DECISION MIX names a decision, the CENSUS names a code path.
    /// The terminal else is one decision (the plan no longer fits the board) reached by two
    /// mechanically different cleanups, so the census keeps them apart and the mix folds them
    /// back onto C2's name. NOT folded into "hunker"/"reload" — that would move C2's published
    /// 23.0/25.0% hunker and 501/419 reload rows, which are the UN-folded ones.
    internal static string StatsVerb(string b) =>
        b == "terminal-reload" || b == "terminal-hunker" ? "staleplan" : b;

    public static readonly string[] ActBranches =
    {
        "siege", "sap", "relock", "heal", "grenade", "item", "shove", "brace",
        "shoot", "overwatch", "reload", "hunker", "terminal-reload", "terminal-hunker", "none",
        // C2/C5 reconciliation: the sentinel is resolved at the publish site, so the census now
        // sees "move" and "idle" instead of a bare "none". Both MUST be registered here —
        // AICOVTEST fails on an unregistered label, and that assertion is deliberate.
        "move", "idle",
    };

    // Enqueue the move steps for the just-planned enemy (shared by the AutoPlay fast path and
    // the post-telegraph path so the move timing/cost accounting is identical either way).
    void EnqueuePlannedMove(Unit e)
    {
        if (_aiPlan == null || _aiPlan.Path.Count == 0) return;
        var path = _aiPlan.Path;
        int moveActions = _aiPlan.MoveActions;
        // GUNNER SUPPRESSING FIRE (area denial): a PINNED foe cannot DASH — clamp a 2-action move down to a
        // single action's worth of tiles (it can reposition a little, but can't sprint across the zone). Cost
        // model matches CostMap (ortho 2 / diag 3 half-tiles); MoveBudget is the single-action budget.
        if (e.Pinned > 0 && moveActions >= 2)
        {
            int budget = Math.Max(1, e.Mobility) * 2, acc = 0, px = e.X, py = e.Y, keep = 0;
            foreach (var (nx, ny) in path)
            {
                acc += (nx != px && ny != py) ? 3 : 2;
                if (acc > budget) break;
                keep++; px = nx; py = ny;
            }
            if (keep < path.Count) { path = path.GetRange(0, keep); moveActions = keep == 0 ? 0 : 1; }
            if (keep == 0) { Fx.PopText(e.Pos + new Vector2(0, -34), "PINNED", Pal.Foe, 16f); return; }
        }
        e.ActionsLeft -= moveActions;
        foreach (var (px, py) in path) Enqueue(new MoveStepAnim(e, px, py), Team.Enemy);   // A2: per-tile footfalls
    }

    // TEMPO mirror: after an enemy fires (a 1-action, non-turn-ending shot) it spends any remaining
    // action to REPOSITION — duck to cover / break the squad's line of fire (kiters + standard) or
    // close the gap (rushers). Mirrors the player's new shoot-then-move so the tempo buff is
    // SYMMETRIC, not a one-sided player advantage. Only "stationary shooters" (an enemy already in
    // range that didn't burn both actions moving) have a spare action here, so it adds little extra
    // movement. Bounded + safe: one fresh CostMap, a single ≤1-action move, enqueued exactly once
    // (the AI state machine gives each enemy ONE ActAfterMove pass — no re-entry, no loop/TIMEOUT).
    void TryEnemyReposition(Unit e)
    {
        if (!e.Alive || e.ActionsLeft <= 0 || e.Mobility <= 0) return;
        var players = AlivePlayers();
        if (players.Count == 0) return;

        // exposure of a tile = how readily the squad can shoot a unit standing there (less cover =
        // more exposed). Mirrors why a player ducks after firing.
        float ExposureAt(int tx, int ty)
        {
            float ex = 0f;
            foreach (var p in players)
            {
                if (!p.Alive || p.Downed || p.Ammo <= 0) continue;   // FUL-7 (review F5): a downed body holds no fire lane
                if (Util.TileDist(tx, ty, p.X, p.Y) > p.Weapon.MaxRange) continue;
                if (!Grid.HasLineOfSight(p.X, p.Y, tx, ty)) continue;
                int cov = Grid.GetCover(tx, ty, p.X, p.Y).Level;   // cover of (tx,ty) vs attacker p
                ex += cov == 2 ? 1f : (cov == 1 ? 2f : 3.5f);      // high cover safest; exposed worst
            }
            return ex;
        }

        float curExp = ExposureAt(e.X, e.Y);
        // An enemy with a meaningfully-exposed firing angle ducks for cover after shooting; a
        // well-covered shooter holds its ground. Purely DEFENSIVE (reduce exposure); we do NOT
        // advance rushers here, to keep the mirror a fair "shoot-then-cover" parity with the player
        // rather than an extra aggression buff. The 2.0 gate is the measured sweet spot — enough
        // enemy self-preservation to reward the player's positioning, not so much it craters the
        // sloppy-play floor (3.5 was too forgiving at 75%, full-aggression too punishing at 50%).
        if (curExp < 2.0f) return;

        Func<int, int, bool> blocked = (x, y) => IsOccupiedByOther(x, y, e);
        var cost = Grid.CostMap(e.X, e.Y, blocked, out var cameFrom, e.MoveBudget * 2);

        (int x, int y) best = (e.X, e.Y); float bestScore = 0.01f; bool found = false;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                int c = cost[x, y];
                if (c <= 0 || c > e.MoveBudget) continue;   // single-action steps only
                float score = (curExp - ExposureAt(x, y)) * 6f;   // duck: reduce exposure
                if (score > bestScore) { bestScore = score; best = (x, y); found = true; }
            }

        if (!found || (best.x == e.X && best.y == e.Y)) return;
        var path = Grid.ReconstructPath(cameFrom, e.X, e.Y, best.x, best.y);
        if (path.Count == 0) return;
        e.ActionsLeft = Math.Max(0, e.ActionsLeft - 1);
        foreach (var (px, py) in path) Enqueue(new MoveStepAnim(e, px, py), Team.Enemy);   // A2: per-tile footfalls
    }

    // Clear the enemy-intent telegraph (so it doesn't render past the unit's action or into the
    // player's turn). Called when the telegraph beat ends and at every turn boundary.
    void ClearIntent() { IntentUnit = null; IntentPlan = null; }

    // ---------------- barracks perk choice ----------------
    void ChoosePerk(int which)
    {
        if (_run.PendingPerks.Count == 0) return;
        var off = _run.PendingPerks[0];
        // APEX W4 (d): under the balance flywheel the pick is value-BIASED but RANDOMIZED, following
        // the ChooseSpec precedent (randomized "so win-rate-by-spec is measurable"). The old greedy
        // ChoosePerk(0) always took slot A — but Run.MakePerkOffer RESERVES slot A for the class
        // line, so "perk pick frequency" was a census of ClassLine, not a measurement of value.
        // Now: 70% the higher-valued perk per the small class+kit prior (SmartPerkValue, in
        // Game.Autopilot.cs), 30% the other — the better build is usually taken (competent-play
        // proxy) while BOTH slots keep real exposure, so win-rate-by-perk stays interpretable.
        // The dumb AutoPlay smoke test (SmartPlay off) keeps its deterministic slot-0 pick, and
        // interactive play is untouched (a human click always passes an explicit slot).
        if (SmartPlay && (which == 0 || which == 1))
        {
            int better = SmartPerkValue(off.Unit, off.A) >= SmartPerkValue(off.Unit, off.B) ? 0 : 1;
            which = Util.Roll(70f) ? better : 1 - better;
        }
        // FUL-1: SIGHTLINE_PERK probe — override AFTER the value roll above (its Util.Rng draw
        // must land in both probe legs, keeping the worlds CRN-paired). NoPersist only.
        if (NoPersist && ForcedPerk.HasValue)
        {
            if (off.A == ForcedPerk.Value) which = 0;
            else if (off.B == ForcedPerk.Value) which = 1;
        }
        Perk p = which == 0 ? off.A : off.B;
        Run.ApplyPerk(off.Unit, p);
        Stats.RecordPerk(PerkDef.Code(p));   // balance telemetry (no-op unless Stats.Enabled)
        _run.Report.Add($"{off.Unit.Name} gains {PerkDef.Name(p)}");
        _run.PendingPerks.RemoveAt(0);
        Audio.Play("select");
    }

    void HandlePerkClick()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var m = Raylib.GetMousePosition();
        if (Raylib.CheckCollisionPointRec(m, Hud.PerkTagBtn) && _run.PendingPerks.Count > 0)
            OpenTagEditor(_run.PendingPerks[0].Unit);
        else if (Raylib.CheckCollisionPointRec(m, Hud.PerkBtnA)) ChoosePerk(0);
        else if (Raylib.CheckCollisionPointRec(m, Hud.PerkBtnB)) ChoosePerk(1);
    }

    /// CLASS SPECIALIZATION FORK pick (W2): apply the chosen fork to the soldier + record telemetry.
    /// Mirrors ChoosePerk. In balance/smart mode the pick is randomized (Util.Roll) so win-rate-by-spec
    /// is measurable; the AutoPlay smoke test stays deterministic at 0 so it never stalls.
    void ChooseSpec(int which)
    {
        if (_run.PendingSpecs.Count == 0) return;
        var off = _run.PendingSpecs[0];
        // balance flywheel: randomize so both forks of each pair are exercised; smoke-test stays at `which`.
        if (SmartPlay && (which == 0 || which == 1)) which = Util.Roll(50f) ? 0 : 1;
        Spec s = which == 0 ? off.A : off.B;
        off.Unit.Spec = s;
        Stats.RecordSpec(SpecDef.Code(s));   // balance telemetry (no-op unless Stats.Enabled)
        _run.Report.Add($"{off.Unit.Name} specializes -> {SpecDef.Name(s)}");
        _run.PendingSpecs.RemoveAt(0);
        Audio.Play("select");
    }

    void HandleSpecClick()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var m = Raylib.GetMousePosition();
        if (Raylib.CheckCollisionPointRec(m, Hud.SpecBtnA)) ChooseSpec(0);
        else if (Raylib.CheckCollisionPointRec(m, Hud.SpecBtnB)) ChooseSpec(1);
    }

    // ---------------- custom tag editor ----------------
    void OpenTagEditor(Unit u)
    {
        if (u == null || u.Team != Team.Player || u.IsVip) return;
        EditingTag = true;
        TagTarget = u;
        TagBuffer = u.CustomTag ?? "";
    }

    void UpdateTagEditor()
    {
        if (TagTarget == null) { EditingTag = false; return; }
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { EditingTag = false; return; }   // cancel
        if (Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            TagTarget.CustomTag = TagBuffer.Trim();   // empty string clears -> auto tags return
            EditingTag = false;
            Audio.Play("select");
            return;
        }
        if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace))
            && TagBuffer.Length > 0)
            TagBuffer = TagBuffer.Substring(0, TagBuffer.Length - 1);
        int ch = Raylib.GetCharPressed();
        while (ch > 0)
        {
            if (TagBuffer.Length < 14 && ch >= 32 && ch < 127)
                TagBuffer += char.ToUpper((char)ch);   // ASCII-only, uppercase to match the UI font
            ch = Raylib.GetCharPressed();
        }
    }

    // ---------------- barracks requisition shop ----------------
    /// True if item i can currently be bought (affordable + has an effect).
    public bool CanBuy(int item)
    {
        if (item < 0 || item >= ShopCost.Length || _run.Intel < ShopCost[item]) return false;
        if (IsModItem(item)) return ModTarget(ModForItem(item)) != null;   // a soldier who lacks this mod
        // COUNTER-PREP: only buyable when a faction is actually telegraphed next, and not already bought.
        if (IsPrepItem(item)) return PrepFactionOffered != Faction.None && _run.PrepFaction == Faction.None;
        return item switch
        {
            0 => _run.Squad.Any(u => u.Hp < u.MaxHp || u.Wound > 0),  // medkit needs someone hurt or wounded
            2 => _run.Squad.Any(u => CountPerksLeft(u) >= 2), // training needs an un-maxed soldier
            3 => _run.Squad.Any(u => u.BonusGrenades < 2),    // cache caps at +2 per soldier
            4 => ArmorTarget() != null,                       // plating: a soldier under the armor cap
            _ => _run.Squad.Count > 0,
        };
    }

    /// The soldier a PLATING purchase armors: the least-armored combatant under the cap, tie-broken
    /// toward the veteran who'll carry the run. Spreads survivability before stacking it on one body.
    Unit ArmorTarget() => _run.Squad
        .Where(u => !u.IsVip && u.Armor < Unit.ArmorMax)
        .OrderBy(u => u.Armor).ThenByDescending(u => u.Kills).ThenBy(u => u.Name)
        .FirstOrDefault();

    static int CountPerksLeft(Unit u)
    {
        int c = 0;
        foreach (var p in PerkDef.All) if (!u.HasPerk(p)) c++;
        return c;
    }

    /// The soldier a weapon-mod purchase installs on: a combatant who doesn't already own it,
    /// spreading upgrades across the squad (fewest mods first) so a single loss doesn't sink the
    /// whole investment, tie-broken toward the veteran (most kills) who'll carry the run.
    Unit ModTarget(WeaponMod m) => _run.Squad
        .Where(u => !u.IsVip && u.Weapon != null && !u.HasMod(m))
        .OrderBy(u => u.WeaponMods.Count).ThenByDescending(u => u.Kills).ThenBy(u => u.Name)
        .FirstOrDefault();

    /// The soldier a purchase would affect (for the shop preview). Matches DoPurchase.
    public Unit ShopTarget(int item)
    {
        if (IsModItem(item)) return ModTarget(ModForItem(item));
        return item switch
        {
            0 => _run.Squad.Where(u => u.Hp < u.MaxHp || u.Wound > 0).OrderByDescending(u => u.Wound).ThenBy(u => u.Hp).FirstOrDefault(),
            1 => _run.Squad.OrderBy(u => u.MaxHp).FirstOrDefault(),
            3 => _run.Squad.Where(u => u.BonusGrenades < 2).OrderBy(u => u.BonusGrenades).FirstOrDefault(),
            4 => ArmorTarget(),
            _ => null,
        };
    }

    /// One-line concrete effect of a purchase, so the player can judge its value.
    public string ShopEffect(int item)
    {
        if (IsPrepItem(item))
        {
            if (_run.PrepFaction != Faction.None) return "prep already secured";
            return PrepFactionOffered == Faction.None
                ? "no faction telegraphed next"
                : $"counters {Run.FactionName(PrepFactionOffered)} for one mission";
        }
        var t = ShopTarget(item);
        if (IsModItem(item))
            return t == null ? "every soldier has it" : $"{t.Name}: install {WeaponModDef.Name(ModForItem(item))}";
        switch (item)
        {
            case 0:
                if (t == null) return "no one is hurt or wounded";
                string heal = t.Hp < t.MaxHp ? $"{t.Hp} -> {t.MaxHp} HP" : "full HP";
                return t.Wound > 0 ? $"{t.Name}: {heal} + cure wound" : $"{t.Name}: {heal}  (+{t.MaxHp - t.Hp})";
            case 1: return t == null ? "-" : $"{t.Name}: max HP {t.MaxHp} -> {t.MaxHp + 2}";
            case 2: return _run.Squad.Any(u => CountPerksLeft(u) >= 2) ? "a soldier gains a perk pick" : "every soldier is maxed";
            case 3: return t == null ? "all soldiers at the cap" : $"{t.Name}: +1 grenade/mission";
            case 4: return t == null ? "all soldiers fully plated" : $"{t.Name}: armor {t.Armor} -> {t.Armor + 1}";
            default: return "";
        }
    }

    void DoPurchase(int item)
    {
        if (!CanBuy(item)) { Audio.Play("miss"); return; }
        if (IsPrepItem(item))
        {
            var f = PrepFactionOffered;
            _run.PrepFaction = f;   // consumed at the next SetupMission (one mission only)
            _run.Report.Add($"COUNTER-PREP staged: {PrepDescFor(f)}");
            _run.Intel -= ShopCost[item];
            Stats.RecordIntel(-ShopCost[item]);   // FUL-13 intel cash-flow
            Stats.RecordPurchase("COUNTER-PREP");
            Audio.Play("select");
            return;
        }
        if (IsModItem(item))
        {
            var mod = ModForItem(item);
            var t = ModTarget(mod);
            if (t == null) { Audio.Play("miss"); return; }
            t.InstallMod(mod);   // persistent: baked into the soldier's Weapon, carried across the run
            _run.Report.Add($"{t.Name} fitted {WeaponModDef.Name(mod)}  ({WeaponModDef.Desc(mod)})");
            _run.Intel -= ShopCost[item];
            Stats.RecordIntel(-ShopCost[item]);   // FUL-13 intel cash-flow
            Stats.RecordPurchase(ShopName[item]);
            Audio.Play("select");
            return;
        }
        switch (item)
        {
            case 0:
                var hurt = ShopTarget(0);
                int amt = hurt.MaxHp - hurt.Hp; hurt.Hp = hurt.MaxHp;
                bool cured = hurt.Wound > 0; hurt.Wound = 0;
                _run.Report.Add($"{hurt.Name} field-treated  (+{amt} HP{(cured ? ", wound cured" : "")})");
                break;
            case 1:
                var weak = _run.Squad.OrderBy(u => u.MaxHp).First();
                weak.MaxHp += 3; weak.Hp += 3;
                _run.Report.Add($"{weak.Name} stimmed  (+3 max HP)");
                break;
            case 2:
                if (!_run.TryQueueBonusPerk("requisition")) { Audio.Play("miss"); return; }
                break;
            case 3:
                var carrier = _run.Squad.Where(u => u.BonusGrenades < 2).OrderBy(u => u.BonusGrenades).First();
                carrier.BonusGrenades += 1;
                _run.Report.Add($"{carrier.Name} issued a frag cache  (+1 grenade/mission)");
                break;
            case 4:
                var plated = ArmorTarget();
                if (plated == null) { Audio.Play("miss"); return; }
                plated.Armor += 1;
                _run.Report.Add($"{plated.Name} fitted ballistic plating  (armor {plated.Armor}, -1 dmg/hit)");
                break;
        }
        _run.Intel -= ShopCost[item];
        Stats.RecordIntel(-ShopCost[item]);   // FUL-13 intel cash-flow
        Stats.RecordPurchase(ShopName[item]);
        Audio.Play("select");
    }

    /// Leave the requisition — [Enter] or the PROCEED plate. Review round 1: this also closes the
    /// ARMORY sub-screen. Enter used to set _shopDone and nothing else; Hud stops drawing the
    /// requisition once ShopDone, so a still-set ArmoryMode was invisible — and OnEscape, which
    /// yields Escape to an open armory, kept yielding it for the rest of the barracks visit. Public
    /// so SETTINGSTEST can leave the shop the way the player does.
    public void ProceedFromShop() { _shopDone = true; ArmoryMode = false; ArmorySoldier = null; Audio.Play("turn"); }

    void HandleShopClick()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Enter)) { ProceedFromShop(); return; }
        // [A] toggles the ARMORY sub-screen; Esc backs out of it.
        if (Raylib.IsKeyPressed(KeyboardKey.A)) { ToggleArmory(); return; }
        if (ArmoryMode && Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (ArmorySoldier != null) ArmorySoldier = null; else ArmoryMode = false;
            Audio.Play("select"); return;
        }
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var m = Raylib.GetMousePosition();

        // the ARMORY toggle is available in both views
        if (Raylib.CheckCollisionPointRec(m, Hud.ArmoryToggle)) { ToggleArmory(); return; }

        if (ArmoryMode)
        {
            if (ArmorySoldier == null)
            {
                // pick a soldier to re-arm
                var roster = ArmoryRoster;
                for (int i = 0; i < Hud.ArmorySoldierBtns.Count && i < roster.Count; i++)
                    if (Raylib.CheckCollisionPointRec(m, Hud.ArmorySoldierBtns[i])) { ArmorySoldier = roster[i]; Audio.Play("select"); return; }
            }
            else
            {
                // pick a weapon for the selected soldier
                var opts = Weapon.ArmoryOptions(ArmorySoldier.Cls);
                for (int i = 0; i < Hud.ArmoryWeaponBtns.Count && i < opts.Length; i++)
                    if (Raylib.CheckCollisionPointRec(m, Hud.ArmoryWeaponBtns[i])) { DoRearm(ArmorySoldier, opts[i]); return; }
            }
            return;   // armory swallows other clicks while open
        }

        // W9: paid slate RE-ROLL (salvage sink; TryRerollShopSlate refuses when broke)
        if (Raylib.CheckCollisionPointRec(m, Hud.ShopReroll)) { TryRerollShopSlate(); return; }

        // ShopBtns are laid out by the Hud over the ROTATING OFFER (display order); map the clicked
        // slot back to its underlying item id before purchasing.
        var offer = ShopOffer();
        for (int i = 0; i < Hud.ShopBtns.Length && i < offer.Count; i++)
            if (Raylib.CheckCollisionPointRec(m, Hud.ShopBtns[i])) { DoPurchase(offer[i]); return; }
        if (Raylib.CheckCollisionPointRec(m, Hud.ShopProceed)) ProceedFromShop();
    }

    // autopilot: spend Intel as a varied, realistic economy so the BALANCE analytics reflect a real
    // player's spread rather than a degenerate single-item loop.
    // W2: converted from CHEAPEST-FIRST to the W4b VALUE-BIASED RANDOM pattern (the ChoosePerk
    // precedent): each pass buys 70% the highest-VALUE affordable slate item / 30% a uniformly
    // random other affordable one. Cheapest-first structurally starved the expensive rows — the
    // four weapon mods and ADV. TRAINING (16) almost never beat a 6-8 cost consumable, so the
    // BY PURCHASE value table had no exposure to price them with. TRAINING (item 2) is now IN
    // the pool: the autoplay barracks loop already resolves queued perk offers via ChoosePerk.
    // COUNTER-PREP stays a situational player call. ALWAYS terminates: every DoPurchase that
    // fires reduces Intel by a positive cost, and the iteration cap bounds the loop defensively.
    void AutoShop()
    {
        var offer = ShopOffer();        // buy only from this barracks' rotating slate

        // W2: ARMORY exposure — the re-arm sink was 0 buys in 140 measured runs because only the
        // interactive armory screen ever reached DoRearm. Give it the same "real exposure" the
        // mods/perks got: once per barracks, a 20% roll re-arms a random soldier to a random
        // legal kit option (ArmoryOptions is class-curated, so it's a sideways bet, not a grief),
        // making REARM visible in the verb mix and ARMORY priceable in the BY PURCHASE table.
        // Rolled BEFORE the spend loop so it competes for the budget — after the loop the intel
        // is already drained below ArmoryCost and the leg never fires (measured: 1 buy/20 runs).
        if (Util.Roll(20f) && _run.Intel >= ArmoryCost)
        {
            var soldiers = _run.Squad.Where(s => !s.IsVip && s.Weapon != null).ToList();
            if (soldiers.Count > 0)
            {
                var s = soldiers[Util.RandInt(0, soldiers.Count - 1)];
                var opts = Weapon.ArmoryOptions(s.Cls).Where(k => CanRearm(s, k)).ToList();
                if (opts.Count > 0) DoRearm(s, opts[Util.RandInt(0, opts.Count - 1)]);
            }
        }

        for (int guard = 0; guard < 40; guard++)
        {
            var buyable = new List<int>();
            foreach (int i in offer)
                if (CanBuy(i)) buyable.Add(i);   // FUL-5: PREP now competes (was excluded — 0 buys ever)
            if (buyable.Count == 0) break;      // nothing useful/affordable left

            // value prior (competent-play proxy, mirrors SmartPerkValue's role): healing a hurt
            // soldier first, then permanent firepower (mods), then the permanent stat/kit rows.
            int best = buyable[0]; float bestV = float.NegativeInfinity;
            foreach (int i in buyable)
            {
                float v = ShopValue(i);
                if (v > bestV) { bestV = v; best = i; }
            }
            int pick = best;
            if (buyable.Count > 1 && !Util.Roll(70f))
            {
                var rest = buyable.Where(i => i != best).ToList();
                pick = rest[Util.RandInt(0, rest.Count - 1)];
            }

            int before = _run.Intel;
            DoPurchase(pick);
            if (_run.Intel >= before) break;   // safety: never spin on a no-op purchase
        }
        _shopDone = true;
    }

    /// W2: the shop VALUE prior for AutoShop's biased pick. A heuristic (like SmartPerkValue),
    /// not ground truth — the point is which item a competent player would USUALLY take, while
    /// the 30% off-pick keeps every row exposed for the BY PURCHASE win-rate table.
    float ShopValue(int item)
    {
        // FUL-5 R6: DE-FLATTENED mod priors — the flat 6f let AFFORDABILITY order the mod
        // economy (SUPPRESSOR 45 of 165 mod buys in the R0 reference, ~2x its slate share:
        // the cheap rows stay buyable after the good rows drain the intel). Rank by what a
        // competent player installs first: the universal always-on rows (damage, aim) over
        // the conditional/situational ones. The 30% off-pick keeps every row exposed anyway.
        if (IsModItem(item))
            return ModForItem(item) switch
            {
                WeaponMod.HollowPoint => 7f,    // +crit +dmg — universal damage, every kit
                WeaponMod.Scope       => 7f,    // +12 aim + long-range hold — universal accuracy
                WeaponMod.Stabilizer  => 6f,    // +6 aim +2 range — universal, smaller
                WeaponMod.ExtendedMag => 5f,    // tempo (fewer reloads); shines on 2-clip kits
                WeaponMod.Bipod       => 5f,    // +10 aim but only while unmoved — positional bet
                WeaponMod.Suppressor  => 3f,    // one ambush-wake read per MISSION — situational tech
                _ => 6f,                        // future mods: the old flat prior
            };
        // FUL-5: COUNTER-PREP — a MODEST prior. The slot only exists when a faction IS
        // telegraphed (RefreshShopOffer) and CanBuy re-gates it, so this is the honest
        // "one-mission edge vs the fight we KNOW is coming" bet: below the permanent rows
        // (a mod outlives the mission), above the generic floor. Was excluded outright — the
        // prep's Combat reads were 0-exposure in every measured batch.
        if (IsPrepItem(item)) return 4f;
        return item switch
        {
            0 => 9f,                            // FIELD MEDKIT (CanBuy gates on someone actually hurt)
            1 => 5f,                            // COMBAT STIMS (+3 max HP, permanent)
            2 => 5f,                            // ADV. TRAINING (a bonus perk pick)
            3 => 4f,                            // FRAG CACHE (+1 grenade/mission)
            4 => 5f,                            // BALLISTIC PLATING (+1 armor)
            _ => 3f,
        };
    }






















    void ChooseCard(int i)
    {
        if (i < 0 || i >= _run.Offers.Count) return;
        _run.CurrentCard = _run.Offers[i];
        Audio.Play("select");
        NextMission();
    }

    void HandleCardClick()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var m = Raylib.GetMousePosition();
        for (int i = 0; i < Hud.MissionCards.Length; i++)
            if (Raylib.CheckCollisionPointRec(m, Hud.MissionCards[i])) { ChooseCard(i); return; }
    }

    /// Advance along the campaign map: a reachable node was chosen, so adopt its
    /// deployment card and deploy to the next mission.
    void ChooseNode(int nodeId)
    {
        var cur = _run.CurrentNode;
        if (cur == null || !cur.Next.Contains(nodeId)) return;
        var node = _run.Map[nodeId];
        _run.MapPos = nodeId;        // advance position == "this node is resolved once"
        node.Visited = true;
        Audio.Play("select");
        if (node.Kind == NodeKind.Event)
        {
            // W4 C1 fix: an Event occupies its campaign COLUMN's mission slot without a fight. Keep the
            // mission counter in lockstep with the column so the run still reaches MaxMissions at the
            // Boss (the win gate is _run.Mission >= MaxMissions) — otherwise an event route can never win.
            _run.Mission = node.Mission;     // = Col + 1
            EnterEvent(node);                // resolve a "?" beat, don't deploy
            return;
        }
        _run.CurrentCard = node.Card;
        NextMission();
    }

    // ---------------- W4: between-mission FIELD EVENTS ("?" nodes) ----------------
    // An Event node presents a situation + choices INSIDE the barracks phase (no fight). It is
    // resolved-once via MapPos (already advanced to this node in ChooseNode), so reload can never
    // re-trigger it; the outcome bakes into persisted Run state and is checkpointed on resolution.
    GameEvent _activeEvent;
    MissionNode _eventNode;
    public bool EventPending => _activeEvent != null;
    public GameEvent ActiveEvent => _activeEvent;

    void EnterEvent(MissionNode node)
    {
        _eventNode = node;
        _activeEvent = EventCatalog.ForNode(_run, node);
        // stay in Phase.Barracks; the Update switch's EventPending branch renders/handles it.
    }

    /// Is a choice's outcome legal given current Intel/roster (so the autopilot never picks an
    /// illegal one and the HUD can grey it out)? Cost is the up-front intel a choice spends.
    public bool ChoiceLegal(EventChoice ch)
    {
        // affordability: any negative-intel mutation (a costed buy / a rescue cost / a paid gamble)
        int cost = 0;
        if (ch.Outcome.Kind == EventOutcomeKind.Intel && ch.Outcome.Amount < 0) cost += -ch.Outcome.Amount;
        if (ch.HasSecond && ch.Outcome2.Kind == EventOutcomeKind.Intel && ch.Outcome2.Amount < 0) cost += -ch.Outcome2.Amount;
        if (ch.HasThird && ch.Outcome3.Kind == EventOutcomeKind.Intel && ch.Outcome3.Amount < 0) cost += -ch.Outcome3.Amount;   // FUL-10
        if (cost > 0 && _run.Intel < cost) return false;
        // roster: a recruit requires a free roster slot
        if (ch.Outcome.Kind == EventOutcomeKind.Recruit && _run.Squad.Count >= Run.RosterMax) return false;
        // FUL-10: a scar cure needs a scarred soldier; a release must leave a roster behind
        if (ChoiceHas(ch, EventOutcomeKind.CureScar) && !_run.Squad.Exists(u => u.Scars.Count > 0)) return false;
        if (ChoiceHas(ch, EventOutcomeKind.ReleaseSoldier) && _run.Squad.Count <= 1) return false;
        // FUL-13: a GrantPrep arm with no telegraphed faction is a dead buy (informant's
        // 12-intel dossier for a report line) — illegal, so the HUD greys it and the bot
        // never spends into it. One rule, EventCatalog.PrepDead (EVENTTEST-pinned).
        if (EventCatalog.PrepDead(_run, ch)) return false;
        return true;
    }

    /// FUL-10: does any of the (up to three) outcomes on this arm carry the given kind?
    static bool ChoiceHas(EventChoice ch, EventOutcomeKind k)
        => ch.Outcome.Kind == k || (ch.HasSecond && ch.Outcome2.Kind == k) || (ch.HasThird && ch.Outcome3.Kind == k);

    /// FUL-5: 70/30 VALUE-BIASED event choice (the ChoosePerk/AutoShop pattern), randomness
    /// HASHED off (MapSeed, node id) — NEVER an Rng draw: a Util.Rng draw here would shift the
    /// shared stream the paired legs replay (CRN) and desync every world after the first event;
    /// the hash is fixed per (run, node), identical across legs and reloads (the Events.cs
    /// GambleSucceeds precedent). The old "always the safe arm" rule left every costed/gamble/
    /// trade-off arm at 0 exposure in ~500 measured missions — BY EVENT-CHOICE had nothing to
    /// price (it replaced the IsSafeChoice/HasDownside pair, removed with it). 70%: the highest-
    /// VALUE legal arm (EventChoiceValue, a competent-play prior); 30%: a hash-picked OTHER
    /// legal arm, so every arm accrues honest exposure over a batch. Always returns a legal
    /// index -> one-tick resolve, no stall.
    int AutoEventChoice()
    {
        if (_activeEvent == null) return 0;
        var legal = new List<int>();
        for (int i = 0; i < _activeEvent.Choices.Length; i++)
            if (ChoiceLegal(_activeEvent.Choices[i])) legal.Add(i);
        if (legal.Count == 0) return _activeEvent.Choices.Length - 1;   // defensive; the last arm is the no-op
        int best = legal[0]; float bestV = float.NegativeInfinity;
        foreach (int i in legal)
        {
            float v = EventChoiceValue(_activeEvent.Choices[i]);
            if (v > bestV) { bestV = v; best = i; }
        }
        if (legal.Count == 1) return best;
        int h = unchecked((_run != null ? _run.MapSeed : 0) * 92821 ^ ((_eventNode != null ? _eventNode.Id : 0) + 3) * 68917);
        if (((h % 100) + 100) % 100 < 70) return best;
        var rest = legal.Where(i => i != best).ToList();
        return rest[(((h >> 7) % rest.Count) + rest.Count) % rest.Count];
    }

    /// FUL-5: the event-arm VALUE prior (competent-play proxy — the SmartPerkValue/ShopValue
    /// family: a heuristic to bias exposure, not ground truth). Roughly on the ShopValue scale;
    /// intel converts at ~0.3/pt so a 15-intel arm (4.5) competes with a free mod grant (5).
    // FUL-13: each outcome is weighted by the probability it actually FIRES (EventCatalog.
    // FireWeight — success partner x p, OnFail partner x (1−p); GambleIntel/GrantScar
    // self-price and stay x1), so a seeded gamble pair is priced as an EV instead of
    // "both fire" (the FUL-10 review's warchest-arm0 asymmetry: 2.25 vs true ~1.5).
    float EventChoiceValue(EventChoice ch)
        => EventOutcomeValue(ch.Outcome) * EventCatalog.FireWeight(ch.Outcome)
           + (ch.HasSecond ? EventOutcomeValue(ch.Outcome2) * EventCatalog.FireWeight(ch.Outcome2) : 0f)
           + (ch.HasThird ? EventOutcomeValue(ch.Outcome3) * EventCatalog.FireWeight(ch.Outcome3) : 0f);   // FUL-10: triple arms

    float EventOutcomeValue(EventOutcome o)
    {
        switch (o.Kind)
        {
            case EventOutcomeKind.Intel: return o.Amount * 0.3f;             // signed: costs subtract
            case EventOutcomeKind.HealSoldier:
            {
                // worth what it actually restores: the most-wounded soldier's deficit (full heal)
                // or the capped amount. Zero when nobody is hurt — never overvalue a no-op arm.
                var w = _run != null ? _run.Squad.Where(s => s.Alive && s.MaxHp > 0)
                            .OrderBy(s => (float)s.Hp / s.MaxHp).FirstOrDefault() : null;
                if (w == null) return 0f;
                int deficit = w.MaxHp - w.Hp;
                float v = (o.Amount < 0 ? deficit : Math.Min(o.Amount, deficit)) * 0.8f;
                if (o.Amount < 0 && w.Wound > 0) v += 2f;                    // the cure rides along
                return v;
            }
            case EventOutcomeKind.WoundSoldier: return -3f;                  // a fatigued next fight
            case EventOutcomeKind.GambleIntel:
                // EV = stake * (2*chance - 1); stake = half our intel. Slightly positive at 55%,
                // but variance on the run economy keeps a competent prior lukewarm.
                return (_run != null ? Math.Max(5, _run.Intel / 2) : 5) * (2f * o.ChancePct / 100f - 1f) * 0.3f;
            case EventOutcomeKind.GrantWeaponMod: return 5f;                 // a 10-14 intel row, free
            case EventOutcomeKind.GrantBonusPerk: return 5f;                 // ADV. TRAINING's effect
            case EventOutcomeKind.GrantGrenades: return 2f;
            case EventOutcomeKind.GrantArmor: return o.SquadWide ? 6f : 3f;
            case EventOutcomeKind.Recruit:
                return _run != null && _run.Squad.Count < Run.RosterMax ? (o.Veteran ? 7f : 5f) : 0f;
            case EventOutcomeKind.GrantTrait: return o.Tr == Trait.IronWill ? 4f : 3f;
            case EventOutcomeKind.AddHeat: return -4f * Math.Max(1, o.Amount);
            case EventOutcomeKind.GrantBoon: return 6f;
            // FUL-10 kinds (integration): the old safe-first bot called scars/releases "downsides";
            // here that judgment becomes a signed value so the 70/30 chooser prices the trade.
            case EventOutcomeKind.GrantScar:
                return -3f * (o.ChancePct > 0 ? o.ChancePct / 100f : 1f);
            case EventOutcomeKind.CureScar:
                return _run != null && _run.Squad.Any(u => u.Alive && u.Scars.Count > 0) ? 4f : 0f;
            case EventOutcomeKind.Salvage: return o.Amount * 0.15f;          // meta-bank, not this run
            case EventOutcomeKind.GrantPrep:
                return _run != null && _run.UpcomingFaction() != Faction.None ? 4f : 0f;
            case EventOutcomeKind.RankKills: return 2f;
            case EventOutcomeKind.ReleaseSoldier: return -6f;                // a roster slot is a life
            default: return 0f;                                              // Nothing / unknown
        }
    }

    void ResolveEvent(int choiceIdx)
    {
        if (_activeEvent == null) return;
        if (choiceIdx < 0 || choiceIdx >= _activeEvent.Choices.Length) choiceIdx = AutoEventChoice();
        var ch = _activeEvent.Choices[choiceIdx];
        if (!ChoiceLegal(ch)) return;   // ignore clicks on illegal choices
        // W9 REVIEW FIX: snapshot the roster BEFORE the outcomes so the reconcile below can tell an
        // event that actually moved a body from one that did not (see the comment at the call).
        var rosterBefore = new System.Collections.Generic.HashSet<Unit>(_run.Squad);
        int deployedBefore = _run.Deployed.Count;
        Stats.RecordEvent(_activeEvent.Id, choiceIdx);   // FUL-1: BY EVENT-CHOICE telemetry (no-op unless Enabled)
        string line = EventCatalog.Apply(_run, ch.Outcome, _eventNode);
        if (ch.HasSecond)
        {
            string line2 = EventCatalog.Apply(_run, ch.Outcome2, _eventNode);
            line = line + "; " + line2;
        }
        if (ch.HasThird) line = line + "; " + EventCatalog.Apply(_run, ch.Outcome3, _eventNode);   // FUL-10
        _run.Report.Insert(0, $"EVENT: {_activeEvent.Title} -- {line}");
        // W9 THE REPAIR — RECONCILE THE DEPLOYMENT. A field event can add or remove a body, and
        // Run.DebriefSurvivors' AutoDeploy() has ALREADY run by the time this resolves, so the
        // roster and the deployment disagreed in BOTH directions:
        //   * a free RECRUIT (DEFECTOR) arrived un-benched, so the barracks header printed
        //     "DEPLOY 5/4 (squad at capacity)" over five DEPLOYED soldiers and the next mission
        //     fielded FIVE where Run.DeployCapFor(2) == 4. HandleBenchClick refuses to un-bench past
        //     the cap, so the event path was the only way over it. A cap is a cap.
        //   * a RELEASE (THE RESERVE CALLS) freed a DEPLOYED slot and never handed it to the healthy
        //     benched soldier, so the squad fielded 3/4 with a 6/6-HP body sitting out while the
        //     header printed its "cap grows over the campaign - field up to it" hint.
        //
        // W9 REVIEW FIX — GATED, and NOT AutoDeploy. The first cut called _run.AutoDeploy()
        // UNCONDITIONALLY here, and the comment claimed it was "a no-op for every event that does not
        // touch the roster". It is not: AutoDeploy re-derives Benched across the WHOLE roster from a
        // fixed rule, so it silently discarded the player's own barracks swap on EVERY event —
        // measured on an Intel-only outcome, MANUAL VEGA[B],KRESS[D],NOX[D],BISHOP[D],LYNX[D] came
        // back VEGA[D],...,NOX[B],... . Reachable in normal play (HandleBenchClick and ChooseNode sit
        // in the same barracks branch), and ResolveEvent is the CHECKPOINT site, so the clobbered
        // deployment is what gets persisted.
        // So: fire only when the roster ACTUALLY moved, and reconcile by exception —
        // Run.ReconcileDeployment benches only what the event added, fills only slots the event
        // freed, trims only over the cap, and leaves every deliberate choice alone. No RNG draw.
        bool rosterMoved = _run.Squad.Count != rosterBefore.Count
                           || _run.Squad.Exists(u => !rosterBefore.Contains(u))
                           || _run.Deployed.Count != deployedBefore;
        if (rosterMoved) _run.ReconcileDeployment(rosterBefore);
        Audio.Play("turn");
        _activeEvent = null; _eventNode = null;
        // The event node is now CurrentNode (MapPos already advanced), so NextNodes() offers its
        // outgoing edges -> the player picks the next real node (the same barracks pass surfaces any
        // queued PendingPerks first). W4 M1/M2 fix: do NOT checkpoint mid-barracks — the outcome
        // (incl. any queued perk) bakes into Run state and is saved at the NEXT mission start, exactly
        // like shop/perk/boon picks. Saving here would (a) lose a queued PendingPerk on a quit before
        // the pick (not persisted) and (b) make CONTINUE replay an already-cleared mission.
    }

    void HandleEventClick()
    {
        if (_activeEvent == null) return;
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var m = Raylib.GetMousePosition();
        for (int i = 0; i < Hud.EventBtns.Length && i < _activeEvent.Choices.Length; i++)
            if (Raylib.CheckCollisionPointRec(m, Hud.EventBtns[i]) && ChoiceLegal(_activeEvent.Choices[i]))
            { ResolveEvent(i); return; }
    }


    void HandleNodeClick()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var m = Raylib.GetMousePosition();
        foreach (var (id, rect) in Hud.NodeBtns)
            if (Raylib.CheckCollisionPointRec(m, rect)) { ChooseNode(id); return; }
    }

    // ---------------- deployment / bench mechanic ----------------
    /// Toggle a roster soldier between DEPLOYED and BENCHED (the deploy-picker). With the deep
    /// roster (up to RosterMax, deploy up to DeployCap) ANY soldier may be benched — you field
    /// your best DeployCap and the rest recover off the line. Guards: keep at least 1 deployed,
    /// and never deploy more than DeployCap. Never called by the autopilot (it takes AutoDeploy's
    /// default). AutoDeploy already benches the wounded by default; this lets the player override.
    public void ToggleBench(Unit u)
    {
        if (AutoPlay || u == null) return;
        if (!u.Benched)
        {
            if (_run.Squad.Count(s => !s.Benched && s != u) < 1) return;   // keep >=1 deployed
        }
        else
        {
            if (_run.Deployed.Count >= _run.NextDeployCap) return;         // don't exceed the cap
        }
        u.Benched = !u.Benched;
        Audio.Play("select");
    }

    void HandleBenchClick()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var m = Raylib.GetMousePosition();
        foreach (var (unit, rect) in Hud.BenchBtns)
            if (Raylib.CheckCollisionPointRec(m, rect)) { ToggleBench(unit); return; }
        // W9: REHAB — buy one scar off a soldier (salvage sink; chips published by Hud.DrawSquadRow)
        foreach (var (unit, rect) in Hud.RehabBtns)
            if (Raylib.CheckCollisionPointRec(m, rect)) { TryBuyScarRemoval(unit); return; }
    }

    /// Barracks: click one of the offered run-scoped boons to adopt it for the rest of the run.
    void HandleBoonClick()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var m = Raylib.GetMousePosition();
        foreach (var (boon, rect) in Hud.BoonBtns)
            if (Raylib.CheckCollisionPointRec(m, rect)) { _run.ChooseBoon(boon); Audio.Play("select"); return; }
    }

    // ---------------- overlay click ----------------
    /// The intro's doors, (id, key), in the order HandleOverlayClick always resolved them:
    /// CONTINUE [C] (only while a save exists), LAST STAND [L], WAR ROOM [W], FIELD MANUAL [K],
    /// SKIRMISH [S], DAILY [Y], TRAINING OP [N], AUDIO CHECK [U], SETTINGS [O] (SETTINGS EVERYWHERE —
    /// O was derived free with `grep -ohE 'KeyboardKey\.[A-Z][a-z0-9]*' src/*.cs | sort -u`
    /// before it was claimed; the free letters were I J O Z, now I J Z), QUIT [Q] (W5 — no confirm
    /// on the title screen: nothing is in flight, and a campaign in progress is on disk at its
    /// last mission start). NEW RUN and the HEAT dial are dispatched above this table.
    public static readonly (string id, KeyboardKey key)[] IntroKeys =
    {
        ("continue", KeyboardKey.C), ("endless",  KeyboardKey.L), ("warroom", KeyboardKey.W),
        ("codex",    KeyboardKey.K), ("skirmish", KeyboardKey.S), ("daily",   KeyboardKey.Y),
        ("training", KeyboardKey.N), ("audio",    KeyboardKey.U), ("settings", KeyboardKey.O),
        ("quit",     KeyboardKey.Q),
    };
    /// The door a key opens, or null.
    public static string IntroKeyId(KeyboardKey k)
    {
        foreach (var (id, key) in IntroKeys) if (key == k) return id;
        return null;
    }
    /// The plate a door is drawn on (the rects Hud.DrawIntro published this frame).
    static Rectangle IntroRect(string id) => id switch
    {
        "continue" => Hud.OverlayBtn2, "endless" => Hud.OverlayBtn3, "warroom" => Hud.OverlayBtn4,
        "codex" => Hud.OverlayBtn5, "skirmish" => Hud.OverlayBtn6, "daily" => Hud.OverlayBtn7,
        "training" => Hud.OverlayBtn8, "audio" => Hud.OverlayBtn9, "settings" => Hud.IntroSettingsBtn,
        "quit" => Hud.IntroQuitBtn, _ => new Rectangle(0, 0, 0, 0),
    };
    /// Which intro door a click at `m` lands on, or null.
    public string IntroHit(Vector2 m)
    {
        foreach (var (id, _) in IntroKeys)
        {
            if (id == "continue" && !SaveGame.Exists) continue;
            if (Raylib.CheckCollisionPointRec(m, IntroRect(id))) return id;
        }
        return null;
    }
    /// Open one intro door by id. Returns TRUE when the door took the input (the caller returns);
    /// FALSE for null and for a CONTINUE that refused, so the frame's other reads still run.
    public bool ActIntro(string id)
    {
        switch (id)
        {
            case "continue": return SaveGame.Exists && ContinueRun();
            case "endless":  BeginEndless(); return true;
            case "warroom":  BeginWarRoom(); return true;
            case "codex":    BeginCodex(); return true;
            case "skirmish": BeginSkirmishSetup(); return true;
            case "daily":    BeginDaily(); return true;
            case "training": BeginTraining(); return true;
            case "audio":    BeginAudition(); return true;
            case "settings": OpenSettings(); return true;
            case "quit":     QuitRequested = true; return true;
            default: return false;
        }
    }

    void HandleOverlayClick()
    {
        // intro HEAT/Ascension selector: dial the difficulty for the NEXT new run (0..unlocked).
        // Arrows/A-D adjust; the +/- buttons (rects from Hud) are clickable. CONTINUE keeps the
        // saved run's own heat, so this only affects a fresh DEPLOY.
        if (Phase == Phase.Intro)
        {
            EnsureMetaLoaded();
            PendingHeat = Sightline.Heat.Clamp(Math.Min(PendingHeat, UnlockedHeat));
            int delta = 0;
            if (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressed(KeyboardKey.A) || Raylib.IsKeyPressed(KeyboardKey.KpSubtract)) delta = -1;
            else if (Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressed(KeyboardKey.D) || Raylib.IsKeyPressed(KeyboardKey.KpAdd)) delta = 1;
            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                var m = Raylib.GetMousePosition();
                if (Raylib.CheckCollisionPointRec(m, Hud.HeatMinus)) delta = -1;
                else if (Raylib.CheckCollisionPointRec(m, Hud.HeatPlus)) delta = 1;
            }
            if (delta != 0)
            {
                // W5: the floor is Heat.Min (-1 = RECRUIT), not 0 — the on-ramp is always
                // selectable; only the ceiling above standard is gated by the earned unlock.
                PendingHeat = Sightline.Heat.Clamp(Math.Clamp(PendingHeat + delta, Sightline.Heat.Min, UnlockedHeat));
                Audio.Play("select");
            }
        }

        // The intro's doors. Review round 1 of SETTINGS EVERYWHERE folded the nine copies of
        // `bool x = (click && rect) || key; if (x) { ...; return; }` into one table so a self-test can
        // hit the SAME rect and the SAME key path the player uses (IntroHit / IntroKeyId / ActIntro,
        // the PauseHit / ActPause shape). Order is the old order; a click is resolved before a key,
        // as it was (each old door read its click before its key). CONTINUE's plate is only drawn
        // while a save exists, and ActIntro("continue") falls through when ContinueRun refuses,
        // exactly as `if (cont && ContinueRun()) return;` did.
        if (Phase == Phase.Intro)
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && ActIntro(IntroHit(Raylib.GetMousePosition()))) return;
            foreach (var (id, key) in IntroKeys)
                if (Raylib.IsKeyPressed(key) && ActIntro(id)) return;
        }

        // W1 mode-seam: end-card MAIN MENU (OverlayBtn2, button or Esc) — back to the intro without
        // founding a new run and WITHOUT touching the campaign checkpoint. Same mode resets as NEW
        // RUN below, so a LAST STAND / SKIRMISH / DAILY end card can never leak its mode (or a
        // daily-forced arena) into whatever is picked next.
        if (Phase == Phase.Win || Phase == Phase.Lose)
        {
            // W5 THE DOORS (audit newplayer-2): the end card banks salvage and never named the
            // room that spends it. WAR ROOM [W] is the third plate; BACK from there lands on the
            // main menu, which is where MAIN MENU would have gone anyway.
            bool war = (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                        Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Hud.EndWarRoomBtn))
                       || Raylib.IsKeyPressed(KeyboardKey.W);
            if (war)
            {
                Hud.EndWarRoomBtn = new Rectangle(0, 0, 0, 0);   // no stale rect into the next screen
                BeginWarRoom();
                return;
            }
            bool menu = (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                         Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Hud.OverlayBtn2))
                        || Raylib.IsKeyPressed(KeyboardKey.Escape);
            if (menu)
            {
                Mode = GameMode.Campaign;
                DailyMode = false;
                if (!NoPersist) Mission.ForcedLayout = -1;
                // zero the rect BEFORE entering the intro: the intro's CONTINUE branch reads the
                // same OverlayBtn2, so a stale end-card rect could otherwise turn a second click
                // at this position into an accidental CONTINUE before the next Draw republishes it.
                Hud.OverlayBtn2 = new Rectangle(0, 0, 0, 0);
                Hud.EndWarRoomBtn = new Rectangle(0, 0, 0, 0);
                Phase = Phase.Intro;
                Audio.Play("select");
                return;
            }
        }

        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                     Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Hud.OverlayBtn);
        bool enter = Raylib.IsKeyPressed(KeyboardKey.Enter);
        if (!click && !enter) return;

        if (Phase == Phase.Barracks) NextMission();   // deploy to next mission
        // RESONANCE T1: a finished TRAINING OP re-runs the drill on its primary button (MAIN MENU,
        // handled above, is the way out) — restartable is half the point of a low-cost drill.
        else if (Mode == GameMode.Training) BeginTraining();
        // intro / win / lose -> new run. INTERACTIVELY this opens the run-opening DRAFT (pick a
        // founding squad + starting boon). The harness NEVER reaches here (it calls StartMission
        // DIRECTLY, bypassing the intro), but gate on !NoPersist defensively so the smoke test /
        // balance batch can never enter Phase.Draft (which would have no autopilot path -> hang).
        // PROGRAM HORIZON W2/W4: NEW RUN after a LAST STAND / SKIRMISH / DAILY returns to the CAMPAIGN —
        // reset the mode so the fresh run isn't left in a single-mission mode. Also clear any daily-forced
        // arena so the campaign picks arenas normally (interactive only — the harness keeps SIGHTLINE_MAP).
        else { Mode = GameMode.Campaign; DailyMode = false; if (!NoPersist) Mission.ForcedLayout = -1; if (!NoPersist) BeginDraft(); else StartMission(); }
    }

    // ---------------- draw ----------------
    Vector2 BoardCenter => new(Cfg.OriginX + Cfg.BoardW / 2f, Cfg.OriginY + Cfg.BoardH / 2f);

    /// The board camera. withShake folds in the transient screen-shake + zoom-punch
    /// (visual only); the picking variant omits them so mouse->tile stays stable.
    public Camera2D ViewCamera(bool withShake)
    {
        var bc = BoardCenter;
        return new Camera2D
        {
            Target = bc + CamPan,
            Offset = bc + (withShake ? Fx.ShakeOffset : Vector2.Zero),
            Rotation = 0f,
            Zoom = CamZoom * (withShake ? (1f + _camPulse) : 1f),
        };
    }

    // W5 THE FIRST HOUR: the frame is drawn in TWO passes so `Display.RenderFrame` can build the
    // BLOOM from the board alone and NOT from the type (audit visual-2 — a saturated UI plate was
    // flooding its own label; TRAINING OP measured 2.19:1 with post-FX on against 8.67:1 with it
    // off). The seam is atmosphere-vs-chrome, not board-vs-menu: the overlay screens' full-screen
    // animated backdrop rides in the bloom-source pass and keeps its glow, while every plate,
    // glyph and number is painted on top of it afterwards, contributing nothing to the bright
    // pass. Both passes still land in the SAME render target, so brightness, gamma, the biome
    // grade and the vignette stay uniform across the whole frame — an earlier version of this
    // wave drew the chrome after the composite and stranded the accessibility settings on the
    // board (Display.RenderFrame's header records that, and why). Nothing here changes WHAT is
    // drawn or in what order — only which pass it lands in.

    /// Pass 1 — the bloom source. Board, death-flash, overlay-screen atmosphere.
    public void DrawBoardLayer()
    {
        Raylib.ClearBackground(Pal.Bg);

        Raylib.BeginMode2D(ViewCamera(true));
        Renderer.DrawBoard(this);
        Raylib.EndMode2D();

        // red death-flash over the board (under the HUD) when a soldier falls
        if (DeathFlash > 0)
            Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.Foe, DeathFlash * 0.35f));

        Hud.DrawBackdropLayer(this);
    }

    /// Pass 2 — the chrome. Every plate, label and number, drawn after the bright pass has run.
    public void DrawHudLayer() => Hud.Draw(this);

    /// Single-pass draw, kept for callers that don't split (and as the definition of the order).
    public void Draw() { DrawBoardLayer(); DrawHudLayer(); }
}
