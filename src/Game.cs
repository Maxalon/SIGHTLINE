using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

public enum Phase { Intro, PlayerTurn, EnemyTurn, Barracks, Win, Lose, Draft }
// APPEND-ONLY: serialized as a raw (int) in SaveGame (CardDto.Objective). Never reorder or
// remove a member — a saved run stores the ordinal, so a reorder silently corrupts the loaded
// objective. Add new objectives at the END only. (SaveGame.SelfTest asserts the tail ordinal.)
public enum Objective { Eliminate, Evac, Hack, Escort, Sabotage, Rescue, Defend, Decapitate }
public enum SecondaryKind { None, NoLosses, Swift, CleanSweep }  // optional per-mission bonus goal (3.9)
enum AiStage { PickNext, Telegraph, ActAfterMove }

public class Game
{
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
    public float AnimSpeed = 1f;
    public void CycleAnimSpeed() { AnimSpeed = AnimSpeed >= 3f ? 1f : AnimSpeed + 1f; }

    // selection / hover
    public Unit Selected;
    public int HoverX, HoverY;
    public bool HoverValid;
    public int[,] MoveCost;
    public bool[,] Threat;          // reachable tiles exposed to active-enemy fire (no cover)
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
    public const int PinTurns = 2;         // turns the pin lasts (survives one enemy BeginTurn -> bites that turn)

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
        u.Weapon = Weapon.Make(k);
        u.RefreshWeaponMods();          // re-apply any installed mods onto the fresh weapon
        u.Ammo = u.Weapon.Clip;
        _run.Report.Add($"{u.Name} re-armed with {u.Weapon.Name}");
        Stats.RecordPurchase("ARMORY");
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
        int seed = (_run != null ? _run.MapSeed : 0) * 131 + (_run != null ? _run.Mission : 0) * 7 + 17;
        var rng = new Random(seed);
        for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }

        int want = ShopOfferSize - offer.Count;               // fill the rest of the slate
        for (int i = 0; i < pool.Count && offer.Count < ShopOfferSize; i++) offer.Add(pool[i]);

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

    static string PrepDescFor(Faction f) => f switch
    {
        Faction.Syndicate => "HARDENED OPTICS: deny their see-over-low cover next mission.",
        Faction.Legion    => "REACTIVE PLATING: squad takes -1 damage next mission.",
        Faction.Wardens   => "FIELD SMOKE: break their long sightlines (no long-range aim edge).",
        _ => "No faction telegraphed on the next mission.",
    };

    // mission objective
    public Objective Objective;
    public List<(int x, int y)> EvacZone = new();

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

    // The clock only runs on objectives where camping is the exploit. Defend is already
    // wave-based; Evac/Escort/Rescue are movement-pressured; Sabotage already makes you move
    // to sites -- none of those need (or want) it.
    bool PressureClockObjective() =>
        Objective == Objective.Eliminate || Objective == Objective.Hack || Objective == Objective.Decapitate;

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

    // onboarding tutorial (3.12): non-blocking contextual callouts on the first-ever run
    public int TutStep = -1;                 // -1 = inactive
    bool _tutMoved, _tutOver, _tutShot;
    float _tutDoneTimer;
    public static readonly string[] TutPrompts =
    {
        "WELCOME, COMMANDER. Click a glowing tile to MOVE the selected soldier. Cover (the raised blocks) shields you from fire - end your move beside one.",
        "Now set OVERWATCH: press [2] (or the button). That soldier will fire on the first enemy that moves into its line of sight.",
        "Click a hostile to FIRE. A shot ends the soldier's turn. Attacking from a side a foe has no cover on FLANKS it - far deadlier.",
        "That's the basics: move into cover, flank, overwatch, fire - then END TURN. Promotions, perks and a branching campaign await. Good hunting.",
    };
    public string TutorialText => (TutStep >= 0 && TutStep < TutPrompts.Length) ? TutPrompts[TutStep] : null;

    void StartTutorialMaybe()
    {
        if (NoPersist || _run.Mission != 1 || Display.TutorialSeen) return;
        TutStep = 0;
        _tutMoved = _tutOver = _tutShot = false;
        Display.MarkTutorialSeen();           // only ever shows once
    }

    void UpdateTutorial(float dt)
    {
        if (TutStep < 0) return;
        switch (TutStep)
        {
            case 0: if (_tutMoved) AdvanceTutorial(); break;
            case 1: if (_tutOver) AdvanceTutorial(); break;
            case 2: if (_tutShot) AdvanceTutorial(); break;
            case 3: _tutDoneTimer -= dt; if (_tutDoneTimer <= 0) TutStep = -1; break;
        }
    }

    void AdvanceTutorial()
    {
        TutStep++;
        if (TutStep == 3) _tutDoneTimer = 7f;
        if (TutStep >= TutPrompts.Length) TutStep = -1;
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
    public bool ShowThreatPref = true;

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
    public const int DraftCap = 4;              // founding core size (roster still backfills to 6 over the run)
    // Threaded into StartMission's new Run on CONFIRM (then cleared back to null/empty):
    public List<Unit> DraftedSquad;             // the confirmed 4 picked Units (null = default squad)
    public Boon? DraftBoon;                     // the confirmed starting boon (null = none)

    /// Set up + enter the run-opening DRAFT (interactive new-run path only). Builds the candidate
    /// pool + the starting-boon offer and switches to Phase.Draft. NEVER called by the harness
    /// (the autoplay/balance/screenshot paths call StartMission directly).
    public void BeginDraft()
    {
        DraftPool = Run.GenerateDraftPool();
        DraftBoonOffer = Run.GenerateDraftBoonOffer();
        DraftPicked = new HashSet<Unit>();
        DraftSelectedBoon = null;
        Phase = Phase.Draft;
        Audio.Play("select");
    }

    /// Draft input: toggle a candidate (cap DraftCap), single-select a boon, CONFIRM when valid.
    void HandleDraftClick()
    {
        var m = Raylib.GetMousePosition();
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
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
            // confirm
            if (DraftReady && Raylib.CheckCollisionPointRec(m, Hud.DraftConfirm)) { ConfirmDraft(); return; }
        }
        // keyboard: Enter deploys when the draft is complete
        if (DraftReady && Raylib.IsKeyPressed(KeyboardKey.Enter)) ConfirmDraft();
    }

    /// True when exactly DraftCap soldiers and one boon are chosen (CONFIRM/Enter enabled).
    public bool DraftReady => DraftPicked.Count == DraftCap && DraftSelectedBoon.HasValue;

    /// Finalize the draft: stage the picks, then run the SAME new-run start the intro would have.
    void ConfirmDraft()
    {
        DraftedSquad = new List<Unit>(DraftPicked);
        DraftBoon = DraftSelectedBoon;
        Audio.Play("turn");
        StartMission();   // threads DraftedSquad/DraftBoon into the new Run, then clears them
    }

    // ---------------- lifecycle ----------------
    /// Start a brand-new campaign run (called from intro / after a run ends).
    /// startAt lets the headless harness jump straight to a given mission.
    public void StartMission(int startAt = 1)
    {
        EnsureMetaLoaded();
        _run = new Run();
        _run.Start(DraftedSquad);           // builds the campaign map, seats at the START node (drafted squad if any)
        if (DraftBoon.HasValue) { _run.ActiveBoons.Add(DraftBoon.Value); Stats.RecordBoon(BoonDef.Code(DraftBoon.Value)); }   // adopt the chosen starting boon
        DraftedSquad = null; DraftBoon = null;   // consumed: the harness path leaves these null (default squad)
        // adopt the dialled-in Heat for this run. The harness can't set PendingHeat (it doesn't
        // touch the intro), so it reads SIGHTLINE_HEAT here instead — defaulting to 0 so plain
        // autoplay/screenshots are byte-stable.
        int heat = PendingHeat;
        if (NoPersist && int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_HEAT"), out int hEnv)) heat = hEnv;
        _run.HeatLevel = Sightline.Heat.Clamp(heat);
        _run.LossStreak = _metaLossStreak;  // adaptive assist: carry the loss history into this run
        // balance telemetry (no-op unless Stats.Enabled); tag the policy so the report can
        // split greedy vs sloppy win-rates and surface the optimal-vs-error GAP.
        Stats.BeginRun(_run.HeatLevel, SmartPlay && SmartSloppy ? "sloppy" : "greedy");
        Players = _run.Squad;
        int n = Util.Clamp(startAt, 1, Run.MaxMissions);
        if (n > 1) _run.JumpTo(n);           // harness: advance along the map to the requested op
        SetupMission(n);
    }

    // Lazily load the persisted unlocked-max Heat once (gated by NoPersist like all save I/O,
    // so the headless harness never reads disk and stays at the default unlock of 0).
    bool _metaLoaded;
    int _metaLossStreak;          // adaptive-assist loss streak loaded from meta.json (0 headless)
    void EnsureMetaLoaded()
    {
        if (_metaLoaded) return;
        _metaLoaded = true;
        if (NoPersist)
        {
            // Harness/screenshot affordance only: SIGHTLINE_HEAT lets the headless intro shot
            // preview the dialled-in level + its unlocked ceiling. No disk I/O; default 0 keeps
            // a plain shot byte-stable.
            if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_HEAT"), out int hEnv) && hEnv > 0)
            {
                UnlockedHeat = Sightline.Heat.Clamp(hEnv);
                PendingHeat = UnlockedHeat;
            }
            return;
        }
        UnlockedHeat = SaveGame.LoadMetaHeat();
        PendingHeat = Math.Min(PendingHeat, UnlockedHeat);
        _metaLossStreak = SaveGame.LoadMetaLossStreak();
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
        Players = new List<Unit>(_run.Squad.Where(u => !u.Benched));

        // objective + difficulty come from the chosen deployment card (Run.ObjectiveFor baseline)
        var card = _run.CurrentCard ?? Run.StandardCard(n);
        Objective = card.Objective;
        EvacZone.Clear();
        HackProgress = 0;
        SabotageSites.Clear();
        SabotageBlown.Clear();
        Vip = null;
        Hvt = null;
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
        int heatEnemy = Sightline.Heat.EnemyDelta(heat);
        int heatStat  = Sightline.Heat.StatDelta(heat);
        // EARLY-MISSION HEAT GRACE. The measured ~20% mission-1 loss (which hard-caps run
        // completion, a geometric product) was almost entirely a heat-3/4 alpha-strike on the
        // COLD OPENER: Heat adds +2 bodies / +2 stat to a force a green 4-rookie squad meets
        // before it has earned a single promotion, perk, or boon. Ramp Heat's contribution in
        // over the first missions so the ladder bites once the squad can answer it (m1 x0, m2
        // x1/2, m3+ full). Card deltas and the per-mission growth curve (Mission.cs) are
        // untouched — only Heat's extra bodies/stats ramp. Heat 0 stays a true no-op.
        if (n <= 1)      { heatEnemy = 0; heatStat = 0; }
        else if (n == 2) { heatEnemy /= 2; heatStat /= 2; }
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
        Mission.Build(Grid, Players, Enemies, n, EvacZone, reserve,
                      enemyDelta, statDelta, HasSabotage ? SabotageSites : null);
        if (Vip != null) { Vip.Grenades = 0; Vip.AbilityCd = 99; }  // the asset has no kit (never ready)
        if (Objective == Objective.Rescue && Vip != null)
        {
            // seat the caged captive mid-field and clear its tile + ring so soldiers can reach it
            Vip.X = Grid.W / 2; Vip.Y = Grid.H / 2;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = Vip.X + dx, ny = Vip.Y + dy;
                    if (Grid.InBounds(nx, ny) && !IsOccupiedByOther(nx, ny, Vip)) Grid.Tiles[nx, ny] = TileType.Floor;
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
        Pressure = 0; _pressureWaves = 0;   // anti-turtle clock resets each mission (Combat.PressureAim cleared by BeginMission)
        _autoSig = -1; _autoStall = 0;
        Phase = Phase.PlayerTurn;
        // 4.4: every mission opens with the squad concealed -- UNLESS Heat "EXPOSED" strips it.
        SquadConcealed = !Sightline.Heat.Exposed(_run.HeatLevel);
        foreach (var u in Players) u.BeginTurn();
        // per-mission feat tracking + status effects start clean each mission
        foreach (var u in Players)
        { u.FeatMultiKill = u.FeatClutch = u.FeatVengeful = u.WasNearDeath = u.AllyDown = false; u.BondAura = false; u.ConsecutiveMisses = 0; u.Statuses.Clear(); }
        _missionKia.Clear();
        Scorches.Clear();            // death decals don't carry between missions
        _refundedThisTurn.Clear();   // flank-kill refund is per-turn; clear it for the mission's first turn too (review #2)
        _vipWaitTurns = 0;           // SmartStep Escort: VIP-hold patience (anti-TIMEOUT)
        _smartConcealTurns = 0;      // SmartStep: concealed-turn counter (hard anti-TIMEOUT cap)
        DeathFlash = 0;
        RollSecondary(n);
        foreach (var u in Enemies) { u.BeginTurn(); u.OnOverwatch = false; }
        Selected = Players.FirstOrDefault(p => p.CanAct);
        AimMode = false;
        SnapShot = false;
        GrenadeMode = false;
        ItemMode = false;
        ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false;
        Biome = Biome.For(n, _run.MapSeed);   // per-run biome variety (surfaces NEON/MAGMA across seeds)
        string facTag = Combat.MissionFaction != Faction.None ? $" - {Run.FactionName(Combat.MissionFaction)}" : "";
        ShowBanner($"MISSION {n} - {Biome.Name}{facTag}", false);
        StartTutorialMaybe();

        // checkpoint the run at the start of each mission (normal play only)
        if (!NoPersist) SaveGame.Save(_run);

        // balance telemetry (no-op unless Stats.Enabled): record the encounter we just built.
        Stats.BeginMission(n, Objective.ToString(), _run.HeatLevel,
                           Players.Count(p => p.Alive && !p.IsVip), Enemies.Count(e => e.Alive));
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
        if (Hvt.Cls != "ELITE")
        {
            int bonus = 6 + _run.Mission;      // scales gently with mission depth
            Hvt.MaxHp += bonus; Hvt.Hp += bonus;
            Hvt.Aim = Math.Min(85, Hvt.Aim + 6);
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

    /// Harness hook: force the current mission's objective (verify new objective types).
    public void DebugForceObjective(Objective o)
    {
        _run.CurrentCard = new MissionCard { Objective = o, ModName = "STANDARD", Reward = RewardKind.None };
        SetupMission(_run.Mission < 1 ? 1 : _run.Mission);
    }

    /// Harness hook (screenshot only): reveal all dormant enemies (fully alert).
    public void DebugWakeAll() { foreach (var e in Enemies) if (e.Alive) e.Alert = AlertLevel.Alert; }

    /// Harness hook (screenshot only): drive the anti-turtle pressure clock to its max rung so a
    /// single frame shows the PRESSURE meter filled in the top bar (and its escalation banner).
    public void DebugPressure()
    {
        Pressure = PressureMax;
        Combat.PressureAim = PressureMax * PressureAimPerRung;
        ShowBanner("ENEMY REINFORCEMENTS - MAX PRESSURE", false);
    }

    /// Harness hook (screenshot only): spread the three awareness tiers (4.3) across the
    /// enemies so one frame shows Unaware ("?") / Suspicious ("!") / Alert glyph states.
    public void DebugAlertTiers()
    {
        var foes = Enemies.Where(e => e.Alive).ToList();
        for (int i = 0; i < foes.Count; i++)
            foes[i].Alert = (AlertLevel)(i % 3);   // 0 Unaware, 1 Suspicious, 2 Alert
    }

    /// Harness hook (screenshot only): the squad is concealed at mission start anyway;
    /// this just adds a banner so the intent reads in the frame (the CONCEALED pill +
    /// ghost rings are already drawn because SquadConcealed is true).
    public void DebugConcealment() => ShowBanner("CONCEALED - PICK YOUR MOMENT", false);

    /// Harness hook (screenshot only): stage an enemy-intent telegraph so a single SHOT frame
    /// shows the planned move path + target reticle + threatened-tile wash + caption. Picks a
    /// live foe, wakes it, runs the real Ai.Plan (so the drawn intent matches what would execute),
    /// and forces a ShootTarget at the nearest soldier if the plan didn't already produce one.
    public void DebugIntent()
    {
        DebugWakeAll();
        // prefer a foe whose real plan includes a MOVE so the path reads in the demo frame;
        // otherwise take any live active foe.
        Unit foe = null; EnemyPlan plan = null;
        foreach (var e in Enemies)
        {
            if (!e.Alive || !e.Active) continue;
            var p = Ai.Plan(this, e);
            if (foe == null) { foe = e; plan = p; }
            if (p.Path.Count > 0) { foe = e; plan = p; break; }
        }
        if (foe == null) return;
        var dest = plan.Path.Count > 0 ? plan.Path[^1] : (foe.X, foe.Y);
        if (plan.ShootTarget == null && !plan.Grenade && !plan.UseItem && plan.HealTarget == null && plan.SapTile == null)
        {
            // ensure the marquee read (a target) is present in the demo frame
            Unit nearest = null; int nd = int.MaxValue;
            foreach (var p in AlivePlayers())
            {
                int d = Util.ChebyDist(dest.Item1, dest.Item2, p.X, p.Y);
                if (d < nd) { nd = d; nearest = p; }
            }
            plan.ShootTarget = nearest;
        }
        IntentUnit = foe;
        IntentPlan = plan;
        IntentDest = dest;
        ShowBanner("ENEMY INTENT", true);
    }

    /// Harness hook (screenshot only): stage a live SIEGE strike so one SHOT frame shows the
    /// persistent pulsing 3x3 danger zone + source line + warning triangle (DrawSiegeZones). Wakes
    /// all, forces one live enemy to BOMBARD, charges it over the nearest soldier cluster.
    public void DebugSiege()
    {
        DebugWakeAll();
        Unit foe = Enemies.FirstOrDefault(e => e.Alive);
        if (foe == null) return;
        foe.Cls = "BOMBARD";
        var (bx, by, hits) = (foe.X, foe.Y, 0);
        // center the zone on the nearest soldier (the strike's intended target)
        Unit nearest = null; int nd = int.MaxValue;
        foreach (var p in AlivePlayers())
        { int d = Util.ChebyDist(foe.X, foe.Y, p.X, p.Y); if (d < nd) { nd = d; nearest = p; } }
        if (nearest != null) { bx = nearest.X; by = nearest.Y; }
        foe.ChargeX = bx; foe.ChargeY = by; foe.ChargeTurns = SiegeFuse;
        ShowBanner("ARTILLERY INCOMING", true);
    }

    /// Headless self-test for 4.4 concealment: starts concealed, pods are gated from
    /// escalating while concealed, and breaking concealment ungates them + arms the
    /// breaking actor's ambush bonus. Prints CONCEALTEST: PASS/FAIL.
    public string ConcealSelfTest()
    {
        NoPersist = true;                       // never touch the save file in a test
        var fails = new System.Collections.Generic.List<string>();
        StartMission(1);
        if (!SquadConcealed) fails.Add("notConcealedAtStart");

        var p = Players.FirstOrDefault(u => u.Alive && !u.IsVip);
        var e = Enemies.FirstOrDefault(x => x.Alive);
        if (p == null || e == null) fails.Add("setupMissingUnits");
        else
        {
            // a dormant foe stood right next to a soldier must NOT wake while concealed
            e.Alert = AlertLevel.Unaware;
            e.X = p.X + 1; e.Y = p.Y; e.SyncPos();
            CheckPodActivation();
            if (e.Active) fails.Add("wokeWhileConcealed");

            // breaking concealment clears the flag, arms the actor, and wakes the sighted pod
            BreakConcealment(p);
            if (SquadConcealed) fails.Add("stillConcealedAfterBreak");
            if (!p.FiredFromConcealment) fails.Add("actorNotArmed");
            if (!e.Active) fails.Add("sightedPodNotWokenOnBreak");
        }

        // (Mi2) RevealRange proximity break: stepping within 3 of an ACTIVE foe auto-breaks
        // concealment via OnUnitEnteredTile, and does NOT arm an ambush bonus (not a shot).
        StartMission(1);
        var p2 = Players.FirstOrDefault(u => u.Alive && !u.IsVip);
        var e2 = Enemies.FirstOrDefault(x => x.Alive);
        if (p2 != null && e2 != null)
        {
            if (!SquadConcealed) fails.Add("concealNotResetForProxTest");
            e2.Alert = AlertLevel.Alert;                 // an already-active foe
            e2.X = p2.X + RevealRange; e2.Y = p2.Y; e2.SyncPos();
            p2.FiredFromConcealment = false;
            OnUnitEnteredTile(p2);                        // simulate the soldier stepping here
            if (SquadConcealed) fails.Add("revealRangeDidNotBreak");
            if (p2.FiredFromConcealment) fails.Add("proximityArmedAmbush");
        }

        return fails.Count == 0
            ? "CONCEALTEST: PASS (start concealed; pods gated; break arms+wakes; RevealRange breaks w/o bonus)"
            : "CONCEALTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Headless self-test for the SHOVE forced-movement verb. Builds a controlled open-field
    /// scene (no random map / no run) and asserts:
    ///   (1) shoving an enemy with a CLEAR tile behind it MOVES it there + breaks its overwatch.
    ///   (2) shoving an enemy against a WALL deals collision damage (HP drops) + doesn't move it.
    ///   (3) CanShove is false when there is no adjacent enemy, true when one is adjacent,
    ///       and false once the soldier has already shoved this turn (ShovedThisTurn).
    /// Prints SHOVETEST: PASS/FAIL. No window needed beyond tile-math (caller inits a tiny one).
    public string ShoveSelfTest()
    {
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();

        // ---- controlled scene: empty 18x11 floor, no cover, no run state ----
        Grid = new Grid();                       // all Floor, Height 0
        Players = new System.Collections.Generic.List<Unit>();
        Enemies = new System.Collections.Generic.List<Unit>();
        Vip = null; CaptiveLocked = false;
        Objective = Objective.Eliminate;
        EvacZone.Clear();
        Fx = new Fx();

        Unit MkP(int x, int y) {
            var u = new Unit { Name = "S", Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                               Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
        }
        Unit MkE(int x, int y) {
            var u = new Unit { Name = "E", Cls = "GRUNT", Team = Team.Enemy, X = x, Y = y,
                               Hp = 6, MaxHp = 6, Aim = 60, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.Alert = AlertLevel.Alert; u.SyncPos(); u.BeginTurn(); return u;
        }

        // Helper: run a ShoveAnim to completion (mirrors how the anim queue drives it in play).
        void RunShove(Unit shover, Unit target, int dx, int dy)
        {
            var anim = new ShoveAnim(shover, target, dx, dy);
            anim.OnStart(this);
            for (int i = 0; i < 200 && !anim.Update(this, 0.05f); i++) { }
        }

        // ---- (1) clear destination -> the enemy SLIDES + overwatch breaks ----
        var p1 = MkP(5, 5);
        var e1 = MkE(6, 5);                       // directly east, adjacent
        e1.OnOverwatch = true; e1.Hunkered = true;
        Players.Add(p1); Enemies.Add(e1);
        RunShove(p1, e1, 1, 0);                   // shove east -> tile (7,5) is clear floor
        if (!(e1.X == 7 && e1.Y == 5)) fails.Add($"clearShoveDidNotMove({e1.X},{e1.Y})");
        if (e1.OnOverwatch) fails.Add("shoveDidNotBreakOverwatch");
        if (e1.Hunkered) fails.Add("shoveDidNotClearHunker");

        // ---- (2) blocked by a wall -> collision damage, no move ----
        Players = new System.Collections.Generic.List<Unit>();
        Enemies = new System.Collections.Generic.List<Unit>();
        var p2 = MkP(5, 5);
        var e2 = MkE(6, 5);                       // adjacent east
        Grid.Tiles[7, 5] = TileType.HighCover;   // a WALL directly behind the enemy
        Grid.SetCoverHp(7, 5);
        Players.Add(p2); Enemies.Add(e2);
        int hp0 = e2.Hp;
        RunShove(p2, e2, 1, 0);                   // shove into the wall
        if (!(e2.X == 6 && e2.Y == 5)) fails.Add($"blockedShoveMoved({e2.X},{e2.Y})");
        if (e2.Hp >= hp0) fails.Add($"blockedShoveNoDamage(hp={e2.Hp})");
        if (!e2.Alive && e2.Hp > 0) fails.Add("blockedShoveDeathInconsistent");

        // ---- (3) CanShove gating (reach 2) ----
        Players = new System.Collections.Generic.List<Unit>();
        Enemies = new System.Collections.Generic.List<Unit>();
        var p3 = MkP(5, 5);
        Players.Add(p3);
        if (CanShove(p3)) fails.Add("canShoveWithNoEnemy");      // no enemy anywhere
        var eFar = MkE(8, 5); Enemies.Add(eFar);                // Chebyshev 3 east -> beyond reach 2
        if (CanShove(p3)) fails.Add("canShoveWithFarEnemy");     // enemy out of shove reach
        if (ShoveTargetOk(p3, eFar)) fails.Add("farEnemyValidTarget");
        var eAdj = MkE(6, 5); Enemies.Add(eAdj);                // Chebyshev 1 east -> adjacent
        if (!CanShove(p3)) fails.Add("cannotShoveWithAdjacentEnemy");
        if (!ShoveTargetOk(p3, eAdj)) fails.Add("adjacentEnemyNotValidTarget");
        var eReach2 = MkE(7, 7); Enemies.Add(eReach2);          // Chebyshev 2 (diagonal) -> within reach
        if (!ShoveTargetOk(p3, eReach2)) fails.Add("reach2EnemyNotValidTarget");
        // once shoved this turn, CanShove must be false (anti-loop / 1-per-turn cap)
        p3.ShovedThisTurn = true;
        if (CanShove(p3)) fails.Add("canShoveTwiceInOneTurn");

        return fails.Count == 0
            ? "SHOVETEST: PASS (reach 2: clear shove moves + breaks overwatch/hunker; blocked shove damages + holds; CanShove gates Chebyshev<=2 + 1/turn)"
            : "SHOVETEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Headless self-test for the FIELD CRAFT (W1) verbs DRAG + VAULT. Verifies: DRAG pulls a
    /// LAGGING ally (Chebyshev 2) one tile toward the dragger, an already-adjacent (Chebyshev 1)
    /// ally is NOT draggable (no legal closer tile), and the once-per-turn cap holds; VAULT crosses
    /// a cover tile to the floor beyond (gating on a cover tile between, landing legality, and
    /// once-per-turn). Prints FIELDTEST: PASS/FAIL.
    public string FieldSelfTest()
    {
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();

        Grid = new Grid();                       // all Floor, Height 0
        Players = new System.Collections.Generic.List<Unit>();
        Enemies = new System.Collections.Generic.List<Unit>();
        Vip = null; CaptiveLocked = false;
        Objective = Objective.Eliminate;
        EvacZone.Clear();
        Fx = new Fx();
        DragMode = VaultMode = false;

        Unit MkP(int x, int y) {
            var u = new Unit { Name = "S", Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                               Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
        }

        // ---- (1) DRAG (reach-2 semantics): a LAGGING ally (Chebyshev 2) is pulled one tile closer ----
        var dragger = MkP(5, 5);
        // (1a) too far: a Chebyshev-3 ally is out of DragReach -> illegal
        var farAlly = MkP(8, 5);                 // 3 east
        Players.Add(dragger); Players.Add(farAlly);
        Selected = dragger;
        if (DragTargetOk(dragger, farAlly)) fails.Add("dragChebyshev3Valid");
        // (1b) Chebyshev-1 (already adjacent): NOT draggable — its only toward-tile is the dragger
        var adjAlly = MkP(6, 5);                  // 1 east
        Players.Add(adjAlly);
        if (DragTargetOk(dragger, adjAlly)) fails.Add("dragChebyshev1Valid");   // no legal closer tile
        // (1c) Chebyshev-2: draggable, lands one tile closer (the intermediate floor tile)
        Players.Remove(farAlly); Players.Remove(adjAlly);
        var ally = MkP(7, 5);                     // 2 east of the dragger
        Players.Add(ally);
        if (!DragTargetOk(dragger, ally)) fails.Add("dragChebyshev2NotValid");
        if (!CanDrag(dragger)) fails.Add("cannotDragLaggingAlly");
        int ax0 = ally.X;                         // 7
        // execute via IssueDrag (the real path: validates, spends the action, enqueues ShoveAnim)
        IssueDrag(ally);
        // drive the enqueued ShoveAnim to completion (the anim queue would otherwise)
        while (_anims.Count > 0) { var a = _anims[0]; a.OnStart(this); for (int i = 0; i < 200 && !a.Update(this, 0.05f); i++) { } if (_anims.Count > 0 && _anims[0] == a) _anims.RemoveAt(0); }
        // ally moved one tile WEST (toward the dragger): 7 -> 6
        if (!(ally.X == ax0 - 1 && ally.Y == 5)) fails.Add($"dragDidNotPullCloser({ally.X},{ally.Y})");
        if (!dragger.DraggedThisTurn) fails.Add("dragDidNotSetFlag");
        // once-per-turn cap (the flag is now set by IssueDrag) — re-seat a fresh reach-2 ally
        ally.X = 7; ally.Y = 5; ally.SyncPos();
        if (CanDrag(dragger)) fails.Add("canDragTwiceInOneTurn");

        // ---- (2) VAULT: cross a cover tile to the floor beyond ----
        Players = new System.Collections.Generic.List<Unit>();
        Enemies = new System.Collections.Generic.List<Unit>();
        var v = MkP(5, 5);
        Players.Add(v); Selected = v;
        // no cover anywhere yet -> no legal vault
        if (CanVault(v)) fails.Add("canVaultWithNoCover");
        if (VaultTargetOk(v, 7, 5)) fails.Add("vaultOverFloorValid");          // (6,5) is floor, not cover
        Grid.Tiles[6, 5] = TileType.HighCover; Grid.SetCoverHp(6, 5);          // cover directly east
        if (!VaultTargetOk(v, 7, 5)) fails.Add("vaultOverCoverInvalid");        // land (7,5) clear floor
        if (!CanVault(v)) fails.Add("cannotVaultWithCover");
        // landing blocked by an occupant -> illegal
        var occ = MkP(7, 5); Players.Add(occ);
        if (VaultTargetOk(v, 7, 5)) fails.Add("vaultOntoOccupiedValid");
        Players.Remove(occ);
        // landing on a cover tile -> illegal (must be floor)
        Grid.Tiles[7, 5] = TileType.LowCover; Grid.SetCoverHp(7, 5);
        if (VaultTargetOk(v, 7, 5)) fails.Add("vaultOntoCoverValid");
        Grid.Tiles[7, 5] = TileType.Floor;
        // execute via IssueVault (the real path: validates, spends 1 action, sets VaultedThisTurn, hops over cover)
        int vx0 = v.X, vact0 = v.ActionsLeft;
        IssueVault(7, 5);
        while (_anims.Count > 0) { var a = _anims[0]; a.OnStart(this); for (int i = 0; i < 200 && !a.Update(this, 0.05f); i++) { } if (_anims.Count > 0 && _anims[0] == a) _anims.RemoveAt(0); }
        if (!(v.X == 7 && v.Y == 5)) fails.Add($"vaultDidNotLand({v.X},{v.Y})");
        if (v.X == vx0) fails.Add("vaultDidNotMove");
        if (v.ActionsLeft != vact0 - 1) fails.Add("vaultDidNotSpendAction");
        if (!v.VaultedThisTurn) fails.Add("vaultDidNotSetFlag");
        // once-per-turn cap (the flag is now set by IssueVault)
        if (CanVault(v)) fails.Add("canVaultTwiceInOneTurn");

        return fails.Count == 0
            ? "FIELDTEST: PASS (DRAG reach-2: a Chebyshev-2 lagging ally is pulled one tile closer; Chebyshev-1 ally not draggable; Chebyshev-3 out of reach; 1-per-turn cap. VAULT crosses a cover tile to clear floor + gates cover-between/landing/1-per-turn)"
            : "FIELDTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Headless self-test for the WAVE 1 deep-roster + deployment lifecycle.
    /// Verifies: AutoDeploy benches the wounded when healthy bodies exist, deploy never exceeds
    /// DeployCap, the roster backfills toward RosterMax, and a benched (wounded) soldier is
    /// preserved + recovers faster across a debrief. Prints BENCHTEST: PASS/FAIL.
    public string BenchSelfTest()
    {
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();
        StartMission(1);                                  // fresh run + mission 1 deployed
        if (_run.Squad.Count(u => !u.IsVip) < 1) return "BENCHTEST: FAIL (noSquad)";
        int cap = _run.NextDeployCap;
        if (_run.Deployed.Count > cap) fails.Add("startDeployedOverCap");

        // (1) AutoDeploy benches the wounded when healthy alternatives exist + caps the deploy.
        while (_run.Squad.Count < Run.RosterMax) _run.Squad.Add(Mission.MakeRecruit());
        var wounded = _run.Squad[0]; wounded.Wound = 2; wounded.Hp = 1;
        foreach (var u in _run.Squad.Skip(1)) { u.Wound = 0; u.Hp = u.MaxHp; }   // the rest healthy
        _run.AutoDeploy();
        if (!wounded.Benched) fails.Add("woundedNotBenched");
        if (_run.Deployed.Count != cap) fails.Add($"deployedNot{cap}");
        if (_run.Deployed.Any(u => u.Wound > 0)) fails.Add("deployedWoundedDespiteHealthy");

        // (2) a benched wounded soldier is preserved + recovers faster across a debrief.
        int hp0 = wounded.Hp;
        _run.DebriefSurvivors();                          // benched-recovery path + re-AutoDeploy
        if (!_run.Squad.Contains(wounded)) fails.Add("benchedLostAtDebrief");
        if (wounded.Hp <= hp0) fails.Add("benchedNotHealed");
        if (wounded.Wound > 1) fails.Add($"benchedWoundNotRecovered={wounded.Wound}");

        // (3) caps hold after a debrief.
        if (_run.Squad.Count > Run.RosterMax) fails.Add("rosterOverMax");
        if (_run.Deployed.Count > _run.NextDeployCap) fails.Add("deployedOverCapAfterDebrief");

        return fails.Count == 0
            ? "BENCHTEST: PASS (auto-bench wounded, deploy<=cap, roster<=max, benched recover+preserved)"
            : "BENCHTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Headless self-test for squad coordination (focus fire + overwatch-aware routing).
    /// Builds a controlled open-field scenario (no random map / no run state) and asserts:
    ///   * PlanEnemySquad picks the most-killable EXPOSED soldier as EnemyFocus, over a
    ///     full-HP one and over a wounded-but-unreachable one.
    ///   * Ai.Plan biases an enemy that can hit BOTH soldiers toward shooting the focus.
    ///   * PlayerOverwatchTiles mirrors the real reaction test (in-range+LoS tile is marked;
    ///     an out-of-range tile is not), and a retreat decision triggers for a cornered,
    ///     low-HP, no-shot grunt. Prints AITEST: PASS/FAIL. No window needed.
    public string AiSquadSelfTest()
    {
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();

        // ---- controlled scene: empty 18x11 floor, no cover (LoS always clear) ----
        Grid = new Grid();                       // all Floor, Height 0, no smoke/cover
        Players = new System.Collections.Generic.List<Unit>();
        Enemies = new System.Collections.Generic.List<Unit>();
        Vip = null; CaptiveLocked = false;
        Objective = Objective.Eliminate;
        EvacZone.Clear();

        Unit MkP(string name, int x, int y, int hp, int maxHp) {
            var u = new Unit { Name = name, Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                               Hp = hp, MaxHp = maxHp, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
        }
        Unit MkE(string name, int x, int y) {
            var u = new Unit { Name = name, Cls = "GRUNT", Team = Team.Enemy, X = x, Y = y,
                               Hp = 6, MaxHp = 6, Aim = 60, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.Alert = AlertLevel.Alert; u.SyncPos(); u.BeginTurn(); return u;
        }

        // VICTIM: low HP (2/8), exposed, sat between two shooters -> should be the focus.
        // HEALTHY: full HP, equally reachable -> a worse focus.
        // HURT_FAR: also low HP but parked in the far corner, beyond BOTH enemies' rifle range
        // (15) -> nobody can currently hit it, so it's a WEAK focus despite the low HP. (On this
        // small board the shooters must sit in the opposite corner for a tile to be out of range.)
        var victim  = MkP("VICTIM",  5, 4, 2, 8);
        var healthy = MkP("HEALTHY", 5, 6, 8, 8);
        var hurtFar = MkP("HURTFAR", 17, 10, 1, 8);   // > rifle range 15 from e1(2,4) & e2(2,5)
        Players.Add(victim); Players.Add(healthy); Players.Add(hurtFar);

        var e1 = MkE("E1", 2, 4);   // in range/LoS of VICTIM and HEALTHY, NOT of HURTFAR
        var e2 = MkE("E2", 2, 5);
        Enemies.Add(e1); Enemies.Add(e2);
        _aiUnits = AliveEnemies().Where(e => e.Active).ToList();

        // ---- 1. focus selection ----
        PlanEnemySquad();
        if (EnemyFocus != victim)
            fails.Add("focusNotVictim=" + (EnemyFocus?.Name ?? "null"));

        // ---- 2. Ai.Plan biases the shot toward the focus ----
        // E1 sits in range/LoS of both VICTIM and HEALTHY. With the focus on VICTIM (a near-
        // kill), its planned ShootTarget should be VICTIM. (We don't move it; it has the shot.)
        var plan = Ai.Plan(this, e1);
        if (plan.ShootTarget != victim)
            fails.Add("e1ShotNotFocus=" + (plan.ShootTarget?.Name ?? "null"));

        // load-bearing check on equal targets: two IDENTICAL exposed full-HP soldiers in range
        // of one grunt — the ONLY thing that can tie-break them is the focus bonus, so forcing
        // the focus to each in turn must pick that one. (Proves the bias actually drives choice,
        // without fighting an intrinsic near-kill value as VICTIM would.)
        var twinA = MkP("TWINA", 13, 4, 8, 8);
        var twinB = MkP("TWINB", 13, 6, 8, 8);
        var eg    = MkE("EG",    13, 5);            // equidistant (dist 1) to both twins
        Players.Add(twinA); Players.Add(twinB); Enemies.Add(eg);
        _aiUnits = AliveEnemies().Where(e => e.Active).ToList();
        EnemyFocus = twinA;
        if (Ai.Plan(this, eg).ShootTarget != twinA) fails.Add("biasFocusA");
        EnemyFocus = twinB;
        if (Ai.Plan(this, eg).ShootTarget != twinB) fails.Add("biasFocusB");

        // ---- 3. overwatch kill-zone map (mirror the real reaction test) ----
        // Fresh scene: a SHOTGUN watcher (range 8) in a corner so an out-of-range tile exists
        // on this small board, plus a HIGH-COVER wall to prove a blocked-LoS tile is NOT marked.
        Players = new System.Collections.Generic.List<Unit>();
        Enemies = new System.Collections.Generic.List<Unit>();
        var watcher = new Unit { Name = "WATCH", Cls = "GUNNER", Team = Team.Player, X = 1, Y = 1,
                                 Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Shotgun) };
        watcher.Ammo = watcher.Weapon.Clip; watcher.SyncPos(); watcher.BeginTurn();
        watcher.OnOverwatch = true;
        Players.Add(watcher);
        Grid.Tiles[3, 1] = TileType.HighCover;   // a wall directly east of the watcher
        Grid.SetCoverHp(3, 1);
        _aiUnits = new System.Collections.Generic.List<Unit>();
        PlanEnemySquad();
        if (!PlayerOverwatchTiles.Contains((4, 4)))
            fails.Add("owTileNotMarked");        // in range (dist 5 <= 8), clear LoS -> threatened
        if (PlayerOverwatchTiles.Contains((1, 1)))
            fails.Add("owMarkedWatcherTile");    // the watcher's own tile is never "entered"
        if (PlayerOverwatchTiles.Contains((17, 10)))
            fails.Add("owMarkedOutOfRange");     // dist ~18 > shotgun range 8 -> NOT threatened
        if (PlayerOverwatchTiles.Contains((6, 1)))
            fails.Add("owMarkedThroughWall");    // straight line crosses the HighCover at (3,1)

        // ---- 4. self-preservation (retreat decision) ----
        // A lone, low-HP (~12%) SHOTGUN grunt (range 8) whose only soldier sits ~17 tiles away
        // can never line up a shot even after moving its full budget, so retreat mode engages:
        // its plan must carry NO ShootTarget yet must STILL spend an action (move/overwatch/
        // hunker) — the core progress invariant that keeps autoplay from ever stalling.
        Enemies = new System.Collections.Generic.List<Unit>();
        Players = new System.Collections.Generic.List<Unit>();
        var loner = MkE("LONER", 1, 5);
        loner.Hp = 1; loner.MaxHp = 8;                                   // ~12% HP
        loner.Weapon = Weapon.Make(WeaponKind.Shotgun); loner.Ammo = loner.Weapon.Clip;  // range 8
        var faraway = MkP("FARP", 17, 10, 8, 8);                         // ~17 tiles away
        Enemies.Add(loner); Players.Add(faraway);
        _aiUnits = AliveEnemies().Where(e => e.Active).ToList();
        PlanEnemySquad();
        var rplan = Ai.Plan(this, loner);
        if (rplan.ShootTarget != null) fails.Add("retreatHadShot=" + rplan.ShootTarget.Name);
        bool spendsAction = rplan.ShootTarget != null || rplan.Overwatch || rplan.Hunker
                          || rplan.Path.Count > 0 || rplan.Grenade || rplan.SapTile != null || rplan.UseItem;
        if (!spendsAction) fails.Add("retreatPlanNoAction");
        // (The genuinely cornered case — best tile is the current one with no LoS for overwatch
        // and no cover for hunker — can't TIMEOUT regardless, because UpdateEnemy advances
        // _aiIdx unconditionally after ActAfterMove; that structural guarantee, not this scene,
        // is what makes retreat stall-proof. Autoplay exercises the messy cases — review #2.)

        // CONTRAST (#2): a FULL-HP loner in the SAME far scene is NOT in retreat mode, so its
        // advance term pulls it TOWARD the soldier. This proves retreatMode is genuinely
        // HP-GATED (the low-HP plan above is the exception, not the default behaviour). Non-flaky:
        // the advance gradient (advW*distNearest) dwarfs the 0-3 tie-break jitter over this span.
        float startDist = Util.TileDist(1, 5, faraway.X, faraway.Y);
        var bold = MkE("BOLD", 1, 5); bold.Hp = 8; bold.MaxHp = 8;        // full HP -> advances
        Enemies.Clear(); Enemies.Add(bold);
        _aiUnits = AliveEnemies().Where(e => e.Active).ToList();
        PlanEnemySquad();
        var bplan = Ai.Plan(this, bold);
        int bx = bplan.Path.Count > 0 ? bplan.Path[bplan.Path.Count - 1].x : bold.X;
        int by = bplan.Path.Count > 0 ? bplan.Path[bplan.Path.Count - 1].y : bold.Y;
        if (Util.TileDist(bx, by, faraway.X, faraway.Y) >= startDist) fails.Add("fullHpDidNotAdvance");

        return fails.Count == 0
            ? "AITEST: PASS (focus picks killable+exposed; Ai.Plan biases to focus + flips; overwatch map mirrors reaction; retreat plan still acts)"
            : "AITEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Harness hook (screenshot only): stamp a tier-2 plateau (with a tier-1 step and a
    /// high-cover block) mid-field so the 2nd elevation tier is visible.
    public void DebugElevation()
    {
        for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
                if (Grid.InBounds(8 + dx, 4 + dy)) Grid.Height[8 + dx, 4 + dy] = 2;   // tier-2 redoubt
        for (int dy = 0; dy < 2; dy++)
            if (Grid.InBounds(7, 4 + dy)) Grid.Height[7, 4 + dy] = 1;                 // tier-1 step beside it
        if (Grid.IsFloor(10, 5)) { Grid.Tiles[10, 5] = TileType.HighCover; Grid.SetCoverHp(10, 5); }  // a foe's high cover to see over
    }

    /// Harness hook (screenshot only): chip several high-cover blocks so the cracked
    /// damage state is visible, and fully degrade one to show the rubble (low) state.
    public void DebugCover()
    {
        int chipped = 0;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
                if (Grid.Tiles[x, y] == TileType.HighCover && chipped < 8) { Grid.DamageCover(x, y, 1); chipped++; }
    }

    /// Harness hook (screenshot only): stamp a barrel cluster near the squad + a live fire patch,
    /// select a soldier, and arm aim so the barrel reticle/blast staging renders.
    public void DebugHazards()
    {
        var u = Players.FirstOrDefault(p => p.Alive && !p.IsVip);
        if (u == null) return;
        int by = Util.Clamp(u.Y, 1, Grid.H - 2);
        int bx = Util.Clamp(u.X + 3, 1, Grid.W - 2);
        // clear a clean firing lane in front of the soldier so CanShootBarrel (LoS + range) holds,
        // then seat the barrel at the end of it + a second barrel below + a live fire patch.
        for (int x = u.X + 1; x <= bx; x++) { Grid.Tiles[x, by] = TileType.Floor; Grid.Barrel[x, by] = false; }
        Grid.Barrel[bx, by] = true;
        Grid.Tiles[bx, by + 1] = TileType.Floor; Grid.Barrel[bx, by + 1] = true;
        Grid.AddFire(Util.Clamp(u.X + 6, 1, Grid.W - 2), Util.Clamp(u.Y + 2, 1, Grid.H - 2), 1, Grid.FireTurns);
        Selected = u; RecomputeMoveCost(); AimMode = true; KbCursor = true; CurX = bx; CurY = by;
        // drive the hover state directly so the reticle is live on the very first rendered frame
        HoverX = bx; HoverY = by; HoverValid = true;
        BarrelAimValid = CanShootBarrel(u, bx, by); BarrelAimX = bx; BarrelAimY = by;
    }

    /// Harness hook (screenshot only): arm a smoke-carrier's item targeting preview.
    public void DebugItem()
    {
        var u = Players.FirstOrDefault(p => p.Alive && p.Item == ItemKind.Smoke)
                ?? Players.FirstOrDefault(p => p.Alive && p.Item != ItemKind.None);
        if (u == null) return;
        Selected = u;
        RecomputeMoveCost();
        ItemMode = true;
        KbCursor = true;
        CurX = Util.Clamp(u.X + 4, 0, Grid.W - 1);
        CurY = u.Y;
        // also drop a live cloud elsewhere so the screenshot shows the deployed haze
        Grid.AddSmoke(Util.Clamp(u.X + 5, 0, Grid.W - 1), Util.Clamp(u.Y + 3, 0, Grid.H - 1), SmokeAnim.Radius, SmokeAnim.Turns);
    }

    /// Harness hook (screenshot only): stage the SHOVE targeting overlay. Wakes pods, teleports
    /// the nearest live enemy adjacent to a soldier, selects that soldier, enters ShoveMode, and
    /// parks the keyboard cursor on the enemy so the push arrow + destination preview render.
    public void DebugShove()
    {
        DebugWakeAll();
        var u = Players.FirstOrDefault(p => p.Alive && !p.IsVip);
        if (u == null) return;
        // find a clear floor tile adjacent to the soldier to seat the foe, with a clear tile
        // BEHIND it (in the push direction) so the destination-tile preview shows.
        var foe = Enemies.Where(e => e.Alive).OrderBy(e => Util.ChebyDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
        if (foe == null) return;
        // prefer pushing east: seat foe at (u.X+1,u.Y) if its destination (u.X+2,u.Y) is clear.
        if (Grid.IsFloor(u.X + 1, u.Y) && Grid.IsFloor(u.X + 2, u.Y) && !IsOccupiedByOther(u.X + 1, u.Y, foe))
        { foe.X = u.X + 1; foe.Y = u.Y; foe.SyncPos(); }
        Selected = u;
        RecomputeMoveCost();
        ShoveMode = true;
        KbCursor = true;
        CurX = foe.X; CurY = foe.Y;
        ShoveTarget = foe;
        ShoveValid = ShoveTargetOk(u, foe);
    }

    /// Harness hook (screenshot only): show the MARK verb in use — one foe already designated
    /// (always-on indicator) + the sharpshooter in MarkMode designating a second one (preview line).
    public void DebugMark()
    {
        DebugWakeAll();
        var u = Players.FirstOrDefault(p => p.Alive && !p.IsVip);
        if (u == null) return;
        u.Cls = "SHARPSHOOTER";        // force the MARK ability for the demo
        u.AbilityCd = 0;
        var foes = Enemies.Where(e => e.Alive && Grid.HasLineOfSight(u.X, u.Y, e.X, e.Y))
                          .OrderBy(e => Util.ChebyDist(u.X, u.Y, e.X, e.Y)).ToList();
        if (foes.Count >= 1) { foes[0].Marked = true; _markedBy = u; }     // already-marked foe
        Selected = u;
        RecomputeMoveCost();
        if (foes.Count >= 2)
        {
            MarkMode = true; KbCursor = true;
            CurX = foes[1].X; CurY = foes[1].Y;
            MarkTarget = foes[1];
            MarkValid = MarkTargetOk(u, foes[1]);
        }
    }

    /// Harness hook (screenshot only): show the NEW verbs — gunner SUPPRESSING FIRE (a pinned foe's
    /// cage marker + the gunner in PinMode painting a 3x3 zone) and the ranger's SLIPSTREAM armed pill.
    public void DebugVerbs()
    {
        DebugWakeAll();
        var u = Players.FirstOrDefault(p => p.Alive && !p.IsVip);
        if (u == null) return;
        u.Cls = "GUNNER";              // force the SUPPRESSING FIRE ability for the demo
        u.AbilityCd = 0; u.Ammo = Math.Max(u.Ammo, 1);
        var foes = Enemies.Where(e => e.Alive && Grid.HasLineOfSight(u.X, u.Y, e.X, e.Y))
                          .OrderBy(e => Util.ChebyDist(u.X, u.Y, e.X, e.Y)).ToList();
        if (foes.Count >= 1) foes[0].Pinned = PinTurns;     // an already-pinned foe (cage indicator)
        // arm a second ranger's SLIPSTREAM so the action-bar pill shows for that class too
        var r = Players.FirstOrDefault(p => p.Alive && !p.IsVip && p != u);
        if (r != null) { r.Cls = "RANGER"; r.Slipstreaming = true; }
        Selected = u;
        RecomputeMoveCost();
        if (foes.Count >= 2)
        {
            PinMode = true; KbCursor = true;
            CurX = foes[1].X; CurY = foes[1].Y;
            PinTarget = foes[1];
            PinValid = PinTargetOk(u, foes[1]);
        }
    }

    /// Harness hook (screenshot only): freeze a sample of the procedural unit-animation poses
    /// (fire-recoil / hit-flinch / walk-lean) on live units + drop a couple of death-scorch decals,
    /// so a static SHOT frame demonstrates the new juice (which is otherwise transient in play).
    public void DebugUnitFx()
    {
        var live = Players.Where(p => p.Alive).ToList();
        if (live.Count > 0) { live[0].RecoilAnim = 1f; Selected = live[0]; }   // a soldier mid-fire-recoil
        if (live.Count > 1) live[1].FlinchAnim = 1f;                            // a soldier mid-hit-flinch
        if (live.Count > 2) live[2].WalkLean = 1f;                              // a soldier mid-stride
        var foes = Enemies.Where(e => e.Alive).ToList();
        if (foes.Count > 0) foes[0].FlinchAnim = 1f;
        // a few lingering scorch decals on nearby empty tiles to show where units fell
        if (live.Count > 0)
            for (int i = 0; i < 3; i++)
            {
                int sx = Util.Clamp(live[0].X + 2 + i * 2, 0, Grid.W - 1);
                int sy = Util.Clamp(live[0].Y + 1 + i, 0, Grid.H - 1);
                AddScorch(Util.TileCenter(sx, sy), i % 2 == 0 ? Pal.Foe : Pal.Friend);
            }
    }

    // ---- secondary objective (3.9) ----
    /// Roll an optional bonus goal for the mission (none on mission 1; CLEAN SWEEP is
    /// skipped on Eliminate where it's automatic).
    void RollSecondary(int n)
    {
        SecondaryFailed = false;
        if (n <= 1) { Secondary = SecondaryKind.None; return; }
        var pool = new List<SecondaryKind> { SecondaryKind.NoLosses };
        if (Objective != Objective.Defend) pool.Add(SecondaryKind.Swift);   // can't finish a hold-out early
        // CLEAN SWEEP (kill every hostile) is a mismatched / near-impossible bonus on:
        //  - Eliminate  (it's the primary objective — automatic, no challenge)
        //  - Decapitate (you win the instant the HVT dies, so clearing the rest is harder)
        //  - Defend     (waves spawn until the timer, so the board never fully clears)
        if (Objective != Objective.Eliminate && Objective != Objective.Decapitate
            && Objective != Objective.Defend) pool.Add(SecondaryKind.CleanSweep);
        Secondary = pool[Util.RandInt(0, pool.Count - 1)];
    }

    public string SecondaryName => Secondary switch
    {
        SecondaryKind.NoLosses  => "NO LOSSES",
        SecondaryKind.Swift     => $"SWIFT (<={SwiftTurns} turns)",
        SecondaryKind.CleanSweep => "CLEAN SWEEP",
        _ => "",
    };

    /// Compact top-bar label (shows the live turn count for SWIFT).
    public string SecondaryHud => Secondary switch
    {
        SecondaryKind.NoLosses  => "BONUS  NO LOSSES",
        SecondaryKind.Swift     => $"BONUS  SWIFT {_turnCount}/{SwiftTurns}",
        SecondaryKind.CleanSweep => "BONUS  CLEAN SWEEP",
        _ => "",
    };

    /// Live status for the HUD: is the bonus still attainable right now?
    public bool SecondaryOnTrack => Secondary switch
    {
        SecondaryKind.NoLosses  => !SecondaryFailed,
        SecondaryKind.Swift     => _turnCount <= SwiftTurns,
        SecondaryKind.CleanSweep => true,
        _ => false,
    };

    /// Final evaluation at mission end.
    bool SecondaryAchieved() => Secondary switch
    {
        SecondaryKind.NoLosses  => _missionKia.Count == 0,
        SecondaryKind.Swift     => _turnCount <= SwiftTurns,
        SecondaryKind.CleanSweep => AliveEnemies().Count == 0,
        _ => false,
    };

    /// Harness hook (screenshot only): force a barracks rank-up perk choice.
    public void DebugBarracksPerk()
    {
        if (_run.Squad.Count > 0)
        {
            _run.Squad[0].Kills = 3;
            _run.Squad[0].Perks.Add(Perk.Reflexes);   // show an existing perk in the dossier
            _run.Squad[0].Hp = Math.Max(1, _run.Squad[0].Hp - 3);
        }
        _run.DebriefSurvivors();
        if (_run.Squad.Count > 0) _run.Squad[0].Wound = 2;   // show the WOUNDED dossier line
        Phase = Phase.Barracks;
    }

    /// Harness hook (screenshot only): show the barracks deployment-card screen.
    public void DebugDeployCards()
    {
        _run.DebriefSurvivors();
        _run.GenerateOffers(_run.Mission + 1);
        Phase = Phase.Barracks;
    }

    /// Harness hook (screenshot only): show the barracks debrief with two wounded soldiers
    /// so the BENCH toggle buttons are visible (S3-A).
    public void DebugBench()
    {
        // wound two soldiers so the BENCH button appears in their rows
        foreach (var u in _run.Squad.Take(2)) u.Wound = 2;
        _run.JumpTo(2);
        _run.DebriefSurvivors();
        _run.PendingPerks.Clear();
        _run.PendingSpecs.Clear();
        _shopDone = true;
        Phase = Phase.Barracks;
    }

    /// Harness hook (screenshot only): show the branching campaign map mid-run with a
    /// couple of columns already cleared, the shop/perks skipped.
    public void DebugCampaignMap()
    {
        _run.JumpTo(3);                  // visit cols 0-2; current sits at mission 3
        _run.DebriefSurvivors();
        _run.PendingPerks.Clear();       // skip promotions for the screenshot
        _run.PendingSpecs.Clear();
        _shopDone = true;                // skip requisition for the screenshot
        Phase = Phase.Barracks;
    }

    /// Harness (screenshot): show the run-scoped BOON pick screen.
    public void DebugBoon()
    {
        _run.JumpTo(2);
        _run.DebriefSurvivors();
        _run.PendingPerks.Clear();
        _run.PendingSpecs.Clear();
        _shopDone = true;
        _run.GenerateBoonOffer();        // populate the pick-1-of-3 doctrine card
        Phase = Phase.Barracks;
    }

    void EnterBarracks()
    {
        // TEMPO wave 4: drop every mission-scoped combat static in one call so no stale value warps a
        // barracks-phase odds read; RunBoons is refreshed to the run's current boons (a FIELD DOCTRINE
        // pick may have just changed them).
        Combat.EndMission(_run.ActiveBoons);
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
                _run.Report.Insert(0, $"BONUS: {SecondaryName} cleared  (+{SecondaryIntel} intel)");
            }
            else _run.Report.Insert(0, $"Bonus missed: {SecondaryName}");
        }

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
            if (!NoPersist) SaveGame.SaveMetaLossStreak(_run.LossStreak);
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
            _run.Intel += gained;
            string heatNote = heatBonus > 0 ? $"  (+{heatBonus} HEAT {_run.HeatLevel})" : "";
            _run.Report.Insert(0, $"Recovered {gained} intel{heatNote}  (total {_run.Intel})");
            _shopDone = false;
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
        }
    }

    // run-over screen text (set by LoseRun so the cause reads accurately)
    public string LoseTitle = "RUN OVER";
    public string LoseReason = "";

    /// End the run as a loss and clear the checkpoint so the intro stops offering CONTINUE.
    void LoseRun(string title, string reason)
    {
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
        if (!NoPersist) { SaveGame.SaveMetaLossStreak(_run.LossStreak); SaveGame.Delete(); }
    }

    void ShowBanner(string text, bool enemy)
    {
        BannerText = text; BannerEnemy = enemy;
        BannerMax = BannerTimer = 1.2f;
        Audio.Play("turn");
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
        if (!mover.Alive) return;
        if (mover.HasStatus(StatusKind.Bleed))   // bleeding worsens with every step
        {
            EnvDamage(mover, Unit.BleedDamage, "BLEED", Pal.RGBA(205, 45, 45));
            if (!mover.Alive) return;
        }
        if (Grid.IsFire(mover.X, mover.Y))       // stepping into a burning tile sears + ignites
        {
            mover.AddStatus(StatusKind.Burning, 2);
            EnvDamage(mover, Unit.BurnDamage, "BURN", Pal.RGBA(255, 140, 40));
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
        }
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
        foreach (var w in watchers)
        {
            if (!w.Alive || !w.OnOverwatch || w.ReactedThisTurn || w.Ammo <= 0) continue;
            if (!CanTarget(w, mover)) continue;
            // 4.4 (review M1): a player's overwatch shot is still a shot — it reveals the
            // squad. No actor -> no ambush bonus on a reaction (it already has its own mod).
            if (w.Team == Team.Player && SquadConcealed) BreakConcealment();
            w.OnOverwatch = false;
            w.ReactedThisTurn = true;
            w.Ammo--;
            // overwatch reaction aim: base -10; Reflexes makes it near-certain, Guardian adds a
            // precision bump. ADDITIVE (not a ternary) so a soldier with BOTH gets both (review
            // S7: the old ternary silently discarded Guardian whenever Reflexes was also held).
            int reactMod = -10 + (w.HasPerk(Perk.Reflexes) ? 75 : 0) + (w.HasPerk(Perk.Guardian) ? Unit.GuardianAim : 0);
            var res = Combat.Resolve(Grid, w, mover, reactMod);
            Fx.PopText(w.Pos + new Vector2(0, -30), "OVERWATCH", Pal.Accent, 18f);
            Audio.Play("over");
            var shot = new ShotAnim(w, mover, res, reaction: true);
            // OnStart runs when this reaction becomes the active anim (Started is false),
            // by which point the mover has settled on the reacted-to tile.
            _anims.Insert(Math.Min(insertAt, _anims.Count), shot);
            insertAt++;
            if (res.Hit && mover.Hp - res.Damage <= 0) break; // will die; stop further reactions
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
        // SIEGE interrupt: killing a charging BOMBARD cancels its strike (the zone reads off live
        // enemies, so it clears automatically; this is a cosmetic confirmation of the interrupt).
        if (d.Cls == "BOMBARD" && d.ChargeTurns > 0)
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
        Unit killer = ActiveAnim switch { ShotAnim sa => sa.A, GrenadeAnim ga => ga.Thrower, _ => null };
        Stats.RecordKill(killer?.Cls ?? "?", killer != null ? (int)killer.Team : 1, d.Cls, (int)d.Team);
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
        Fx.PopText(d.Pos + new Vector2(0, -10), d.IsVip ? "VIP DOWN" : "DOWN", c, 22f);
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
        }

        // final-blow kill-cam (3.11): the mission-deciding death lingers in slow-mo
        if (IsMissionEndingKill(d))
        {
            AddHitStop(0.4f);
            AddZoomPunch(0.13f);
            AddBloom(0.4f);
            Fx.AddShake(11f);
        }

        // purge any queued movement for the dead unit
        _anims.RemoveAll(a => a is MoveStepAnim m && m.Unit == d);
        if (Selected == d) Selected = null;
        // DECAPITATE: a death may have removed the HVT's last in-range guard — re-evaluate now so the
        // HVT is immediately exposed (the telegraph + reduced-damage gate flip the same frame).
        UpdateHvtGuard();
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
        killer.KillsThisTurn++;
        if (killer.KillsThisTurn >= 2 && !killer.FeatMultiKill)
        { killer.FeatMultiKill = true; FeatBanner(killer, "MULTI-KILL"); }
        if (killer.MaxHp > 0 && killer.Hp * 4 <= killer.MaxHp && !killer.FeatClutch)
        { killer.FeatClutch = true; FeatBanner(killer, "CLUTCH KILL"); }
        if (killer.AllyDown && !killer.FeatVengeful)
        { killer.FeatVengeful = true; FeatBanner(killer, "AVENGED"); }

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
        if (sa.D != d || killer == null || killer.Team != Team.Player || killer.IsVip || !killer.Alive) return;
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
        foreach (var s in u.Statuses)
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
            if (!u.Alive) break;          // a DoT can drop the unit mid-tick
        }
        u.Statuses.RemoveAll(s => s.Turns <= 0);
    }

    /// Source-less damage (status DoT / hazards): apply, splash FX, kill or note hurt.
    public void EnvDamage(Unit u, int dmg, string label, Color col)
    {
        if (!u.Alive) return;
        u.Hp -= dmg;
        u.Flash = 1f;
        Fx.Burst(u.Pos, col, 8, 130f, 0.4f, 3f, true);
        Fx.PopText(u.Pos + new Vector2(0, -26), $"-{dmg} {label}", col, 20f);
        if (u.Hp <= 0) { u.Hp = 0; KillUnit(u); }
        else MarkPlayerHurt(u);
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

    /// True when any live BOMBARD has a strike charged (drives the HUD/banner "strike inbound" cue).
    public bool SiegeActive
    {
        get { foreach (var e in Enemies) if (e.Alive && e.Cls == "BOMBARD" && e.ChargeTurns > 0) return true; return false; }
    }

    /// True if (x,y) is inside a live BOMBARD strike zone. Cover-ignoring, so cover doesn't save you —
    /// the only outs are KILL the artillery or VACATE the tile. Read by the smart autopilot (flee) and
    /// the renderer/self-test. Reads live charge state, so a killed SIEGE's zone auto-clears.
    public bool InSiegeZone(int x, int y)
    {
        foreach (var e in Enemies)
            if (e.Alive && e.Cls == "BOMBARD" && e.ChargeTurns > 0
                && Util.ChebyDist(x, y, e.ChargeX, e.ChargeY) <= SiegeRadius) return true;
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
            if (!e.Alive || e.Cls != "BOMBARD" || e.ChargeTurns <= 0) continue;
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
        for (int x = bx - BarrelRadius; x <= bx + BarrelRadius; x++)
            for (int y = by - BarrelRadius; y <= by + BarrelRadius; y++)
            {
                if (!Grid.InBounds(x, y)) continue;
                var ch = Grid.DamageCover(x, y, Grid.HighCoverHp);
                if (ch != Grid.CoverHit.None) CoverHitFx(x, y, ch);
                if ((x != bx || y != by) && Grid.IsBarrel(x, y)) chain.Add((x, y));   // catch neighbours
                Grid.LightFire(x, y, Grid.FireTurns);                                 // residue fire
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

        // units standing in fire keep burning (the Burning DoT does the damage in TickStatuses)
        foreach (var u in Players.Concat(Enemies))
            if (u.Alive && Grid.IsFire(u.X, u.Y)) u.AddStatus(StatusKind.Burning, 2);

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

        if (Raylib.IsKeyPressed(KeyboardKey.M)) Audio.ToggleMute();
        if (!AutoPlay && Raylib.IsKeyPressed(KeyboardKey.F)) Display.ToggleFullscreen();
        if (!AutoPlay && Raylib.IsKeyPressed(KeyboardKey.F2)) CycleAnimSpeed();   // fast-forward anim pacing (1x/2x/3x)
        Audio.SetMusicIntensity(MusicIntensity());
        UpdateTutorial(dt);
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
        if (!AutoPlay && (Phase == Phase.PlayerTurn || Phase == Phase.EnemyTurn))
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                if (AimMode || GrenadeMode || ItemMode || ShoveMode || MarkMode || GrappleMode || PinMode || DragMode || VaultMode) { AimMode = false; SnapShot = false; GrenadeMode = false; ItemMode = false; ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false; }
                else Paused = !Paused;
            }
            if (Paused) { HandlePauseMenu(); return; }
            HandleCamera();
            UpdateAutoCam(dt);
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

        // advance animation queue
        if (_anims.Count > 0)
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
                            // Prefer an Event node when reachable so autoplay/balance always exercise
                            // the "?" path (and continuously verify the event mission-lockstep fix).
                            var nn = _run.NextNodes();
                            var ev = nn.FirstOrDefault(x => x.Kind == NodeKind.Event);
                            ChooseNode((ev ?? nn[0]).Id);
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
        }

        CheckEnd();
    }

    void CheckEnd()
    {
        if (Phase != Phase.PlayerTurn && Phase != Phase.EnemyTurn) return;
        if (_anims.Count > 0) return;
        var alivePlayers = AlivePlayers();
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
            if (!CaptiveLocked && (Vip == null || !Vip.Alive))
            { LoseRun("CAPTIVE LOST", $"The captive died on mission {_run.Mission}."); return; }
            if (!CaptiveLocked && Vip != null && EvacZone.Contains((Vip.X, Vip.Y))) EnterBarracks();
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

    /// One-time mid-run recovery valve. Called only from the SQUAD-WIPE branch of CheckEnd (VIP/
    /// captive-lost losses stay instant). Returns false — letting the wipe become a real loss — when
    /// the checkpoint is already spent OR the wipe came too early (mission < 3: early failure ends
    /// cleanly). Otherwise it burns the checkpoint, rebuilds the squad as a fresh emergency cadre of
    /// rookies (keeping Intel / heat / map position), and RESTARTS the current mission from its start
    /// (SetupMission re-checkpoints the save). The squad's veterans are gone — a bad mission is now
    /// survivable but costly, not run-ending. Can fire at most once per run, so there's no loop risk.
    bool TryReinforcements()
    {
        if (_run == null || _run.CheckpointUsed || _run.Mission < 3) return false;
        _run.CheckpointUsed = true;

        // fresh emergency squad: deploy as many rookies as this mission fields (fall back to the
        // attrition floor of 3). Rookies carry no rank/perks/mods — that's the price of the wipe.
        int cap = Math.Max(Run.AttritionFloor, Run.DeployCapFor(_run.Mission));
        _run.Squad = new List<Unit>();
        for (int i = 0; i < cap; i++) _run.Squad.Add(Mission.MakeRecruit());

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

    void SmartStep()
    {
        if (_anims.Count > 0 || Phase != Phase.PlayerTurn) return;
        // Decision-richness + swing telemetry: record ONCE at the first idle SmartStep of each
        // player turn (balance harness only; Stats early-outs unless Enabled). Cheap + read-only.
        if (Stats.Enabled && _turnCount != _lastTelemetryTurn)
        {
            _lastTelemetryTurn = _turnCount;
            Stats.RecordPlayerTurn(CountMeaningfulChoices(), CurrentLead());
        }
        TryFreeCaptive();                       // free a captive a soldier already stands next to
        var u = Players.FirstOrDefault(p => p.CanAct);
        if (u == null) { EndPlayerTurn(); return; }
        Selected = u;
        RecomputeMoveCost();

        // ── SIEGE response (non-shoot tactical axis): a soldier standing in a charged strike zone
        // must (a) interrupt the artillery if it has a good shot at it (PriorityWeight 24 already
        // biases TakeBestShot toward the BOMBARD), else (b) step OUT of the zone. This makes the
        // autopilot exercise the intended "relocate / focus the source" play so balance reflects it,
        // and the SmartFleeSiege fall-through always reaches a guaranteed-progress action (no TIMEOUT).
        if (!SquadConcealed && InSiegeZone(u.X, u.Y))
        {
            if (!Slip(15) && TakeBestShot(u)) return;   // interrupt: kill the charging SIEGE if we can
            if (SmartFleeSiege(u)) return;              // vacate: step to the best out-of-zone tile
            // penned in (rare): fall through to normal routing -> always ends in DoHunker.
        }

        // ── CONCEALMENT / AMBUSH (4.4): spend the opener deliberately ──────────────────
        // While concealed the squad can reposition freely AND pods can't wake by sight, so
        // concealment is a HUGE asset for the "race" objectives: keep stealth and walk the
        // goal (the fragile VIP can cross the map untouched; an Evac squad can slip into the
        // zone). For combat objectives, hold the break until a soldier has a GOOD ambush shot
        // (fire it next step with +20 aim/+25 crit), or until proximity forces the reveal.
        if (SquadConcealed)
        {
            // Stealth-race objectives: ones we approach hidden without needing to fire first.
            // Evac/Escort/Rescue qualify because they're pure "reach a tile" goals (no shot at
            // all while hidden). Hack/Sabotage qualify for the covert APPROACH — but the first
            // hack/plant now GOES LOUD (DoHack breaks concealment, balance fix), so after that
            // the squad drops into the normal combat objective routing to hold & finish. We
            // still creep in concealed (safe approach) rather than ambush-opening from afar.
            // (Eliminate/Decapitate/Defend inherently require killing, so they ambush instead.)
            bool stealthRace = Objective == Objective.Evac || Objective == Objective.Escort
                            || Objective == Objective.Rescue || Objective == Objective.Hack
                            || Objective == Objective.Sabotage;

            // auto-reveal is imminent (a soldier is about to step inside RevealRange of an
            // active foe): break NOW so the ambush bonus isn't wasted on a forced reveal.
            bool forced = Players.Any(p => p.Alive && Enemies.Any(e => e.Alive && e.Active
                    && Util.TileDist(p.X, p.Y, e.X, e.Y) <= RevealRange + 1));

            if (!stealthRace)
            {
                // COMBAT objective: a worthwhile ambush shot from where this soldier stands?
                var (ambTgt, ambVal) = BestShotFrom(u, u.X, u.Y);
                if (ambTgt != null && ambVal >= 8f) { BreakConcealment(u); return; }
                if (forced) { BreakConcealment(); return; }
                // creep into a better firing position before tipping our hand; hold if none.
                if (SmartApproach(u)) return;
                DoHunker(); return;
            }

            // STEALTH-RACE objective: stay hidden and make objective progress (NO shooting —
            // a shot would break stealth and wake the pods we're sneaking past). Only break if
            // a reveal is forced anyway (then spring it with the best-positioned soldier).
            // HARD ANTI-TIMEOUT CAP: if the covert plan ever drags on (squad can't consolidate
            // in the zone / path to the objective is blocked), abandon stealth so the normal
            // combat objective logic — which has bulletproof guaranteed-progress fallbacks —
            // resolves the match. This is the safety net that makes the stealth plan TIMEOUT-proof.
            if (forced || _smartConcealTurns >= 30)
            {
                var breaker = BestSquadAmbushUnit() ?? u;
                Selected = breaker; BreakConcealment(breaker); return;
            }
            if (SmartConcealedRace(u)) return;     // move the VIP/squad toward the goal, hidden
            DoHunker(); return;
        }

        // ── OBJECTIVE ROUTING (preserved from AutoStep, with smart combat layered in) ──
        switch (Objective)
        {
            case Objective.Evac:    if (SmartEvac(u))    return; break;
            case Objective.Hack:    if (SmartHack(u))    return; break;
            case Objective.Sabotage:if (SmartSabotage(u))return; break;
            case Objective.Escort:  if (SmartEscort(u))  return; break;
            case Objective.Rescue:  if (SmartRescue(u))  return; break;
            case Objective.Defend:  if (SmartDefend(u))  return; break;
            case Objective.Decapitate: if (SmartDecapitate(u)) return; break;
        }

        // ELIMINATE (and the combat-clearing fall-through for every other objective):
        SmartCombatStep(u);
    }

    /// CONCEALED stealth-race movement: advance the acting soldier toward the goal WITHOUT
    /// firing (pods stay dormant while we're hidden, so a covert dash to evac / the cage is
    /// far safer than waking the board). The VIP/captive heads for extraction; escorts head
    /// for the goal too (to be in position when stealth eventually breaks). Returns true if a
    /// move was issued. NEVER calls a shooting/ability path (that would break concealment).
    bool SmartConcealedRace(Unit u)
    {
        // HACK / SABOTAGE: creep to the objective concealed (safe approach), then hack/plant.
        // NOTE: the first hack/plant now BREAKS stealth (DoHack → BreakConcealment, balance
        // fix), so the very next SmartStep frame falls through to the engaged routing
        // (SmartHack/SmartSabotage) which fights to hold the objective and finish it.
        if (Objective == Objective.Hack)
        {
            if (CanHack(u)) { DoHack(); return true; }
            return TryMoveTowardTile(u, Terminal.x, Terminal.y);
        }
        if (Objective == Objective.Sabotage)
        {
            if (CanHack(u)) { DoHack(); return true; }     // plants the nearest adjacent charge
            var site = SabotageSites.Where((s, i) => !SabotageBlown.Contains(i))
                .OrderBy(s => Util.TileDist(u.X, u.Y, s.x, s.y)).FirstOrDefault();
            return site != default && TryMoveTowardTile(u, site.x, site.y);
        }
        // VIP / freed captive: walk to the extraction zone (concealed → no enemy fire, so just
        // beeline; VipAdvance's exposure scoring is moot while hidden).
        if (u.IsVip)
        {
            if (CaptiveLocked) return false;   // caged: can't move (hunker)
            if (EvacZone.Contains((u.X, u.Y))) return false;
            var g = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                            .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).FirstOrDefault();
            return g != default && TryMoveTowardTile(u, g.x, g.y);
        }
        // RESCUE escort: rush to spring the still-caged captive (concealed → safe approach).
        if (Objective == Objective.Rescue && CaptiveLocked && Vip != null
            && Util.ChebyDist(u.X, u.Y, Vip.X, Vip.Y) > 1)
            return TryMoveTowardTile(u, Vip.X, Vip.Y);
        // Evac/Escort soldiers: move toward the extraction zone to get into position.
        var ahead = EvacZone.OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).FirstOrDefault();
        if (ahead != default && !EvacZone.Contains((u.X, u.Y)))
            return TryMoveTowardTile(u, ahead.x, ahead.y);
        return false;   // already staged: hold concealed (caller hunkers)
    }

    /// The squad's best-positioned ambusher right now: the soldier whose current tile yields
    /// the highest-value shot on a live foe. Used to pick WHO springs a forced concealment
    /// break so the +ambush bonus lands the biggest hit. Null if no soldier has any shot.
    Unit BestSquadAmbushUnit()
    {
        Unit best = null; float bestVal = 0f;
        foreach (var p in Players)
        {
            if (!p.Alive || !p.CanAct || p.IsVip || p.Ammo <= 0) continue;
            var (tgt, val) = BestShotFrom(p, p.X, p.Y);
            if (tgt != null && val > bestVal) { bestVal = val; best = p; }
        }
        return best;
    }

    // ── balance instrumentation (harness only) ──────────────────────────────────────────

    /// Current HP "lead" = (sum of living soldier HP) − (sum of living, ACTIVE enemy HP).
    /// Dormant pods don't count (they aren't in the fight yet). Sign-changes in this across a
    /// match = lead swings (a tension proxy); fed to Stats.RecordPlayerTurn.
    int CurrentLead()
    {
        int p = Players.Where(x => x.Alive && !x.IsVip).Sum(x => x.Hp);
        int e = Enemies.Where(x => x.Alive && x.Active).Sum(x => x.Hp);
        return p - e;
    }

    /// Decision-richness proxy for THIS player turn. Two axes of real choice, summed across every
    /// soldier that can still act:
    ///   (a) WHICH TARGET — how many distinct candidate shots are within ~12% of the soldier's best
    ///       shot (a real "who do I shoot?" call, not a forced single option).
    ///   (b) WHERE TO STAND AFTER FIRING (TEMPO) — because the aimed shot no longer ends the turn,
    ///       a soldier that has a shot AND a spare action faces a genuine "where do I end up after
    ///       firing?" bet: how many distinct safe destinations (low exposure / good cover) are
    ///       near-best. This axis simply DID NOT EXIST before the tempo change (firing zeroed the
    ///       budget), so its contribution is new decision depth, not a re-weighting. Capped per
    ///       soldier so an open map can't trivially inflate it.
    /// Reuses ShotValue/ComputeOdds/TileExposure (read-only — never mutates state). Cheap + bounded.
    int CountMeaningfulChoices()
    {
        int total = 0;
        foreach (var u in Players)
        {
            if (!u.Alive || !u.CanAct || u.IsVip || u.Ammo <= 0) continue;
            // (a) which target — gather the value of every legal shot from where this soldier stands.
            float best = 0f; int comparable = 0;
            var vals = new List<float>();
            foreach (var e in Enemies)
            {
                if (!e.Alive || (e == Vip && CaptiveLocked)) continue;
                if (Util.TileDist(u.X, u.Y, e.X, e.Y) > u.Weapon.MaxRange) continue;
                bool commanding = Grid.HeightAt(u.X, u.Y) - Grid.HeightAt(e.X, e.Y) >= 2;
                if (!Grid.HasLineOfSight(u.X, u.Y, e.X, e.Y, commanding)) continue;
                float v = ShotValue(Combat.ComputeOdds(Grid, u, e), e);
                vals.Add(v);
                if (v > best) best = v;
            }
            if (best <= 0f) continue;                                      // no shot -> no shot/positioning decision
            foreach (var v in vals) if (v >= best * 0.88f) comparable++;   // within ~12% of best
            if (comparable >= 2) total += comparable - 1;                  // count the real target alternatives

            // (b) where to stand after firing — only when the soldier can fire AND still has an
            // action left to move (the post-shot positioning bet). Score each 1-action-reachable
            // tile by safety (low exposure) + cover + height; count distinct near-best destinations.
            if (u.ActionsLeft >= 2)
            {
                float SafetyAt(int x, int y)
                {
                    float s = 24f - TileExposure(u, x, y);
                    var foe = AliveEnemies().OrderBy(en => Util.TileDist(x, y, en.X, en.Y)).FirstOrDefault();
                    if (foe != null) s += Grid.GetCover(x, y, foe.X, foe.Y).Level * 8f;
                    s += Grid.HeightAt(x, y) * 5f;
                    return s;
                }
                var pcost = Grid.CostMap(u.X, u.Y, (x, y) => IsOccupiedByOther(x, y, u), out _, u.MoveBudget * 2);
                float pbest = SafetyAt(u.X, u.Y); var pvals = new List<float> { pbest };
                for (int x = 0; x < Grid.W; x++)
                    for (int y = 0; y < Grid.H; y++)
                    {
                        if (pcost[x, y] <= 0 || pcost[x, y] > u.MoveBudget) continue;  // 1-action steps only
                        float s = SafetyAt(x, y); pvals.Add(s); if (s > pbest) pbest = s;
                    }
                if (pbest > 0f)
                {
                    int pComparable = 0;
                    foreach (var s in pvals) if (s >= pbest * 0.85f) pComparable++;
                    if (pComparable >= 2) total += Math.Min(2, pComparable - 1);       // capped: anti-inflation
                }
            }
        }
        return total;
    }

    /// Coverage hook (balance harness): occasionally throw the soldier's utility item when it's
    /// clearly worthwhile and cheaply decidable, so item balance stops being invisible to the
    /// flywheel. Conservative (never wastes a charge on a bad throw, never frags an ally) and
    /// always SAFE (returns false unless it actually issued a throw — progress is guaranteed by
    /// the caller's fallbacks). Items end the turn, so this is a deliberate commitment.
    ///   SMOKE       — drop on a soldier badly exposed to 2+ guns (deny the enemy's sightlines).
    ///   FLASH       — lob onto a cluster of 2+ active foes (disorients them; no ally in blast).
    ///   INCENDIARY  — same cluster test (lays a fire field that denies ground + ignites foes).
    ///   BARRICADE   — drop adjacent cover when the soldier is exposed and has nothing to shoot.
    bool TrySmartItem(Unit u)
    {
        if (u.ItemCharge <= 0 || u.Item == ItemKind.None || u.ActionsLeft <= 0) return false;
        Selected = u;   // IssueItem acts on Selected (already set by SmartStep, set again defensively)

        switch (u.Item)
        {
            case ItemKind.Smoke:
            {
                // worth it only if this soldier is genuinely exposed (≥2 guns can hit it with no
                // cover) AND has no good shot of its own — smoke its own tile to break the lanes.
                if (TileExposure(u, u.X, u.Y) < 12f) return false;
                var (tgt, _) = BestShotFrom(u, u.X, u.Y);
                if (tgt != null) return false;                       // prefer shooting if we can
                if (Util.TileDist(u.X, u.Y, u.X, u.Y) > ItemRange) return false;
                if (!ItemTargetOk(u, u.X, u.Y)) return false;
                IssueItem(u.X, u.Y); return true;
            }
            case ItemKind.Flash:
            case ItemKind.Incendiary:
            {
                // lob onto the densest reachable cluster of ACTIVE foes (≥2), never near an ally.
                int bx = -1, by = -1, best = 1;
                foreach (var e in Enemies)
                {
                    if (!e.Alive || !e.Active) continue;
                    if (Util.TileDist(u.X, u.Y, e.X, e.Y) > ItemRange || !ItemTargetOk(u, e.X, e.Y)) continue;
                    if (!Grid.HasLineOfSight(u.X, u.Y, e.X, e.Y)) continue;
                    int foes = Enemies.Count(o => o.Alive && o.Active && Util.ChebyDist(e.X, e.Y, o.X, o.Y) <= 1);
                    bool ally = Players.Any(p => p.Alive && Util.ChebyDist(e.X, e.Y, p.X, p.Y) <= 1);
                    if (ally || foes < 2) continue;
                    if (foes > best) { best = foes; bx = e.X; by = e.Y; }
                }
                if (bx < 0) return false;
                IssueItem(bx, by); return true;
            }
            case ItemKind.Barricade:
            {
                // drop cover when the soldier is exposed and has nothing to shoot — on an
                // adjacent empty floor tile between it and the nearest foe.
                if (TileExposure(u, u.X, u.Y) < 12f) return false;
                var (tgt, _) = BestShotFrom(u, u.X, u.Y);
                if (tgt != null) return false;
                var foe = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
                if (foe == null) return false;
                int dx = Math.Sign(foe.X - u.X), dy = Math.Sign(foe.Y - u.Y);
                int tx = u.X + dx, ty = u.Y + dy;
                if (Util.TileDist(u.X, u.Y, tx, ty) > ItemRange || !ItemTargetOk(u, tx, ty)) return false;
                IssueItem(tx, ty); return true;
            }
        }
        return false;
    }

    // ── objective sub-routines ────────────────────────────────────────────────────────
    // Each returns true once it has committed this soldier's action. They prioritise the
    // OBJECTIVE move but interleave smart combat (best shot / cover) so the squad fights
    // its way to the goal instead of marching straight into fire. Returning false hands the
    // soldier to SmartCombatStep (the shared combat brain + guaranteed-progress fallback).

    bool SmartEvac(Unit u)
    {
        // EXTRACTION IS A RACE: the longer the squad lingers the more pods wake and grind it
        // down (smart positioning that adds turns LOSES Evac). So beeline to the zone FIRST;
        // only fight when genuinely blocked. Exception: take a kill ONLY when it doesn't cost
        // tempo — a near-certain finisher of a foe that already threatens the lane.
        if (EvacZone.Contains((u.X, u.Y)))
        {
            // arrived: haul any adjacent straggler aboard (lift-out — cuts the drag), then hold it.
            if (CanExtract(u)) { DoExtract(); return true; }
            if (TrySmartDrag(u)) return true;   // pull a lagging ally one step closer to the zone
            if (TakeBestShot(u)) return true;
            if (u.Ammo == 0 && u.ActionsLeft > 0) { DoReload(); return true; }
            if (HoldOverwatch(u)) return true;
            DoHunker(); return true;
        }
        // push for the nearest free extraction tile — distance-greedy (speed over cover).
        var cand = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                           .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).ToList();
        if (cand.Count > 0 && TryMoveTowardTile(u, cand[0].x, cand[0].y)) return true;
        // couldn't advance this turn (path blocked): clear a blocker, blast a squatter, re-arm.
        if (TakeBestShot(u)) return true;
        if (u.Grenades > 0)
        {
            // blast a squatter loose — but NEVER if a soldier is in the blast (friendly fire);
            // the very "path jammed" state that got us here often means an ally is close by.
            var blocker = AliveEnemies()
                .Where(e => EvacZone.Contains((e.X, e.Y)) && CanGrenade(u, e.X, e.Y) && NoAllyInBlast(e.X, e.Y))
                .OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
            if (blocker != null) { IssueGrenade(blocker.X, blocker.Y); return true; }
        }
        if (TrySmartDrag(u)) return true;   // can't advance: at least pull a straggler forward
        if (u.Ammo == 0) { DoReload(); return true; }
        DoHunker(); return true;
    }

    /// True if NO living soldier sits within a grenade's blast (Chebyshev GrenadeAnim.Radius)
    /// of (tx,ty) — the friendly-fire safety check shared by every SmartStep grenade path.
    bool NoAllyInBlast(int tx, int ty)
        => !AlivePlayers().Any(f => Util.ChebyDist(tx, ty, f.X, f.Y) <= GrenadeAnim.Radius);

    /// Chebyshev distance from (x,y) to the nearest extraction tile (0 inside the zone).
    int DistToEvac(int x, int y)
    {
        if (EvacZone.Count == 0) return 0;
        int best = int.MaxValue;
        foreach (var t in EvacZone) best = Math.Min(best, Util.ChebyDist(x, y, t.x, t.y));
        return best;
    }

    /// FIELD CRAFT autopilot (W1, bounded): on the march-to-the-corner objectives, if `u` is within
    /// drag reach of a friendly that lags BEHIND it (farther from evac) and DRAGging that ally pulls it
    /// one step CLOSER to evac, do it. The once-per-turn DraggedThisTurn flag bounds it to a single pull,
    /// so it
    /// can never loop; callers still fall through to their normal logic, so it's never the sole stalling
    /// action. Returns true iff a drag was issued.
    bool TrySmartDrag(Unit u)
    {
        if (!CanDrag(u)) return false;
        int myDist = DistToEvac(u.X, u.Y);
        Unit best = null; int bestGain = 0;
        foreach (var a in Players)
        {
            if (!DragTargetOk(u, a)) continue;
            int allyDist = DistToEvac(a.X, a.Y);
            if (allyDist <= myDist) continue;                 // only pull a straggler that's BEHIND us
            int landDist = DistToEvac(a.X + Math.Sign(u.X - a.X), a.Y + Math.Sign(u.Y - a.Y));
            int gain = allyDist - landDist;                   // how much closer the pull lands the ally
            if (gain > bestGain) { bestGain = gain; best = a; }
        }
        if (best != null) { IssueDrag(best); return true; }
        return false;
    }

    /// FIELD CRAFT autopilot (W1, bounded): in combat, if a badly-wounded ally (≤⅓ HP) sits within
    /// drag reach and pulling it toward `u` lands it on a LESS-exposed tile (or at least no worse),
    /// drag it out of the line of fire. Once/turn via DraggedThisTurn; falls through otherwise.
    bool TryRescueDrag(Unit u)
    {
        if (!CanDrag(u)) return false;
        Unit best = null; float bestDrop = 0.01f;          // require a real exposure reduction
        foreach (var a in Players)
        {
            if (a == u || a.IsVip) continue;               // the VIP is handled by the escort brain
            if (a.Hp * 3 > a.MaxHp) continue;              // only genuinely-wounded allies (≤ 1/3 HP)
            if (!DragTargetOk(u, a)) continue;
            int lx = a.X + Math.Sign(u.X - a.X), ly = a.Y + Math.Sign(u.Y - a.Y);
            float drop = TileExposure(a, a.X, a.Y) - TileExposure(a, lx, ly);   // positive = safer landing
            if (drop > bestDrop) { bestDrop = drop; best = a; }
        }
        if (best != null) { IssueDrag(best); return true; }
        return false;
    }

    bool SmartHack(Unit u)
    {
        if (CanHack(u)) { DoHack(); return true; }          // adjacent: hack it down
        // a strong shot is worth taking; otherwise RUSH the terminal (speed limits how many
        // pods wake before we're done — distance-greedy, like the dumb baseline but smarter
        // about when to pause). Once stuck, clear blockers / re-arm.
        if (HasStrongShot(u) && TakeBestShot(u)) return true;
        if (TryMoveTowardTile(u, Terminal.x, Terminal.y)) return true;
        if (TakeBestShot(u)) return true;                   // pinned: clear blockers
        if (u.Ammo == 0) { DoReload(); return true; }
        return false;                                       // hand to combat brain (overwatch/hunker)
    }

    bool SmartSabotage(Unit u)
    {
        if (CanHack(u)) { DoHack(); return true; }          // plant the charge
        if (HasStrongShot(u) && TakeBestShot(u)) return true;
        var site = SabotageSites.Where((s, i) => !SabotageBlown.Contains(i))
            .OrderBy(s => Util.TileDist(u.X, u.Y, s.x, s.y)).FirstOrDefault();
        if (site != default && TryMoveTowardTile(u, site.x, site.y)) return true;
        if (TakeBestShot(u)) return true;
        if (u.Ammo == 0) { DoReload(); return true; }
        return false;
    }

    bool SmartEscort(Unit u)
    {
        if (u.IsVip)
        {
            // the asset is fragile (6 HP) and the enemy AI hunts it, so SURVIVAL beats raw
            // speed here (unlike Evac where the whole squad must arrive): advance toward evac
            // along the SAFEST route — minimise exposure, hug cover — rather than sprinting
            // through the open. VipAdvance falls back to a plain beeline if no path is safer.
            if (!EvacZone.Contains((u.X, u.Y)))
            {
                var goal = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                                   .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).FirstOrDefault();
                if (goal != default && VipAdvance(u, goal.x, goal.y)) return true;
            }
            DoHunker(); return true;     // arrived or blocked: tuck in (the VIP's gun is irrelevant)
        }
        // escorts: a soldier who has reached the zone hauls the asset (VIP) aboard the instant
        // it's adjacent — the lift-out that ends the long escort walk early (win-critical pull).
        if (EvacZone.Contains((u.X, u.Y)) && CanExtract(u)) { DoExtract(); return true; }
        // FIELD CRAFT: even outside the zone, a soldier adjacent to the lagging VIP can DRAG it one
        // step toward evac (accelerates the fragile asset's march). Bounded once/turn; falls through.
        if (TrySmartDrag(u)) return true;
        // clear the path AHEAD of the VIP and kill threats to it. Take the best shot; if there's
        // nothing to shoot, push toward the evac zone to screen the VIP's route (don't hang back
        // letting the VIP walk into fire alone), then fall through to combat.
        if (TakeBestShot(u)) return true;
        if (u.Ammo == 0) { DoReload(); return true; }
        var ahead = EvacZone.OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).FirstOrDefault();
        if (ahead != default && SmartMoveToward(u, ahead.x, ahead.y)) return true;
        return false;
    }

    bool SmartRescue(Unit u)
    {
        if (u.IsVip)
        {
            if (CaptiveLocked) { DoHunker(); return true; }     // caged: can't move
            // freed: race to extraction (beeline — speed beats cover for the fragile asset).
            if (!EvacZone.Contains((u.X, u.Y)))
            {
                var cand = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                                   .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).ToList();
                if (cand.Count > 0 && TryMoveTowardTile(u, cand[0].x, cand[0].y)) return true;
            }
            DoHunker(); return true;
        }
        // PHASE 1 — spring the captive ASAP: the WHOLE squad converges on the cage (the
        // dumb baseline does this and it's right — the captive sits mid-board, so dawdling
        // in cover just lets the enemies mass). Take a free finisher en route, else beeline.
        if (CaptiveLocked && Vip != null)
        {
            if (Util.ChebyDist(u.X, u.Y, Vip.X, Vip.Y) > 1)
            {
                if (HasStrongShot(u) && TakeBestShot(u)) return true;     // a sure kill on the way is fine
                if (TryMoveTowardTile(u, Vip.X, Vip.Y)) return true;     // otherwise rush the cage
            }
            // adjacent already (TryFreeCaptive will spring it next tick): fight from here.
        }
        // PHASE 2 (freed) — screen the captive's extraction: a soldier in the zone hauls the
        // freed captive aboard the instant it's adjacent (lift-out), else kill threats.
        if (EvacZone.Contains((u.X, u.Y)) && CanExtract(u)) { DoExtract(); return true; }
        // FIELD CRAFT: pull the freed captive (or a lagging ally) one step toward evac if adjacent.
        if (!CaptiveLocked && TrySmartDrag(u)) return true;
        if (TakeBestShot(u)) return true;
        if (u.Ammo == 0) { DoReload(); return true; }
        return false;
    }

    bool SmartDefend(Unit u)
    {
        // HOLD THE LINE: win = survive N turns, so DON'T wander (every step out of cover is
        // risk and there's nowhere to "go"). Prep a steady shot, fire the best target, then
        // overwatch the approach unconditionally (waves keep coming — a held lane is never
        // wasted), and hunker as the floor. No repositioning: a defending squad stays put.
        if (PrepAbility(u)) return true;
        if (TakeBestShot(u)) return true;
        if (u.Ammo == 0) { DoReload(); return true; }
        if (u.Ammo > 0 && u.ActionsLeft > 0 && !u.HasStatus(StatusKind.Disoriented))
        { DoOverwatch(); return true; }     // always worth watching on a defend
        DoHunker(); return true;
    }

    bool SmartDecapitate(Unit u)
    {
        if (Hvt != null && Hvt.Alive)
        {
            // GUARDED HVT (W4): while the HVT shrugs off damage, PEEL its bodyguards first — shoot a
            // living, in-range guard this soldier can hit (bounded: at most 2 guards). If none is
            // reachable, fall through to grind the HVT directly (reduced-not-zero damage → no stall).
            if (Hvt.HvtGuarded && u.Ammo > 0)
            {
                Unit guard = _hvtGuards
                    .Where(g => g != null && g.Alive
                                && Util.ChebyDist(g.X, g.Y, Hvt.X, Hvt.Y) <= Combat.HvtGuardRange
                                && CanTarget(u, g))
                    .OrderBy(g => g.Hp)
                    .FirstOrDefault();
                if (guard != null) { AutoShootSmart(u, guard); return true; }
            }
            // shoot the HVT on sight (prep a steady shot first if it sharpens the kill).
            if (u.Ammo > 0 && CanTarget(u, Hvt))
            {
                if (PrepAbilityFor(u, Hvt)) return true;
                AutoShootSmart(u, Hvt); return true;
            }
            if (u.Ammo == 0) { DoReload(); return true; }
            if (u.Grenades > 0 && CanGrenade(u, Hvt.X, Hvt.Y)
                && !Players.Any(f => f.Alive && Util.ChebyDist(f.X, f.Y, Hvt.X, Hvt.Y) <= 1))
            { IssueGrenade(Hvt.X, Hvt.Y); return true; }       // flush it out of cover
            // can't reach it: drop a close blocker, else maneuver onto the HVT.
            var blk = FirstTargetFor(u);
            if (blk != null && blk != Hvt && Util.TileDist(u.X, u.Y, blk.X, blk.Y) <= 3
                && TakeBestShot(u)) return true;
            if (SmartMoveToward(u, Hvt.X, Hvt.Y)) return true;
        }
        return false;   // HVT dead/unreachable → generic combat
    }

    // ── the shared combat brain (ELIMINATE + every objective's clear-and-advance) ──────
    // The heart of the competent AI. Order of preference for a single soldier:
    //   1. prep a value-adding ability (STEADY before a strong shot, SUPPRESS/SMOKE a
    //      dangerous foe, PATCH a badly-hurt adjacent ally, RUN&GUN/BLITZ for tempo);
    //   2. fire the best expected-value target (finishers + flanks + priority foes first);
    //   3. grenade a 2+ cluster, or a well-covered target we can't shoot well;
    //   4. reposition toward cover / a flanking angle (subtracting tile exposure);
    //   5. reload if dry, overwatch if foes will push, else hunker (always progresses).
    void SmartCombatStep(Unit u)
    {
        // 1 — deliberate ability prep that improves THIS turn's outcome.
        if (PrepAbility(u)) return;

        // 2 — best shot by expected value (only when it's actually worth firing). SLOPPY: ~15%
        //     of the time mis-judge and skip an otherwise-good shot (a human hesitation) — falls
        //     through to a worse action below, so the GAP measures the cost of that error.
        if (!Slip(15) && TakeBestShot(u)) return;

        // 2b — TOY COVERAGE (harness): occasionally commit a utility item when it's clearly the
        //      right call (smoke when pinned in the open, flash/incendiary a cluster, drop cover).
        //      Ends the turn, so it comes after shooting but before the positional fallbacks.
        if (TrySmartItem(u)) return;

        // 3 — out of ammo: reload now so next step can fire.
        if (u.Ammo == 0 && u.ActionsLeft > 0) { DoReload(); return; }

        // 4 — grenade: catch a cluster, or flush a target our gun can't crack.
        if (u.Grenades > 0 && SmartGrenade(u)) return;

        // 4b — FIELD CRAFT (W1): if a badly-wounded ally is within drag reach, pull it one tile
        //      toward us (out of the open / back toward the squad). Bounded once/turn (DraggedThisTurn),
        //      a free repositioning support act; falls through if no good pull exists. (Rarely satisfiable
        //      under the greedy bot's spacing — soldiers cluster at Chebyshev-1, which is non-draggable —
        //      so DRAG is primarily a player tool; this keeps the autopilot path covered when it does align.)
        if (TryRescueDrag(u)) return;

        // 5 — no shot available this turn: maneuver toward a covered firing position on the
        //     nearest foe (cover + flank − exposure). If we're already well-placed and a foe
        //     is in sight, hold overwatch; otherwise keep closing. Always ends in hunker.
        if (SmartApproach(u)) return;
        // SLOPPY: ~15% of the time forget to set overwatch (a common human omission).
        if (!Slip(15) && HoldOverwatch(u)) return;
        if (SmartReposition(u)) return;     // shuffle into the best adjacent cover if any
        DoHunker();                         // guarantees progress
    }

    // ════════════════════ smart combat helpers ════════════════════════════════════════

    /// ComputeOdds as if `a` stood at (ax,ay) — the Ai.Plan trick (move, compute, restore).
    /// Lets us score a prospective firing tile without actually moving the unit.
    ShotOdds SmartOdds(Unit a, int ax, int ay, Unit d)
    {
        int ox = a.X, oy = a.Y;
        a.X = ax; a.Y = ay;
        var odds = Combat.ComputeOdds(Grid, a, d);
        a.X = ox; a.Y = oy;
        return odds;
    }

    /// How dangerous is this enemy → how much we want it dead first. VIP/HVT-style high-value
    /// kills and the roles that punish us hardest (snipers, mortars, the medic that undoes our
    /// damage, elites/bosses, cover-stripping sappers) get a priority premium.
    float PriorityWeight(Unit e)
    {
        switch (e.Cls)
        {
            case "ELITE":     return 30f;   // boss / mid-boss: ends the mission, hits hard
            case "WARLORD":   return 32f;
            case "BOMBARD":   return 24f;   // SIEGE artillery: silence it to cancel the telegraphed strike
            case "SNIPER":    return 22f;   // long-range chip from safety
            case "MORTAR":    return 22f;   // back-line AoE we can't easily reach
            case "MEDIC":     return 20f;   // undoes our damage — kill it to stop the heals
            case "SAPPER":    return 14f;   // strips our cover
            case "BERSERKER": return 14f;   // rushes us; better dead before it arrives
            case "HUNTER":    return 13f;   // flanker
            case "DRONE":     return 11f;   // ignores cover; usually fragile, finish it
            case "TURRET":    return 8f;    // immobile but free overwatch
            default:          return 4f;    // grunt / scout / shield
        }
    }

    /// Expected value of a shot described by `odds` against `target`. Roughly
    /// hitChance × expectedDamage, with big bonuses for a finishing blow and a crit-prone
    /// flank/exposed shot, plus the target's threat priority. Used to rank both WHICH foe
    /// to shoot and WHERE to stand to shoot it.
    float ShotValue(ShotOdds odds, Unit target)
    {
        float hit = odds.HitChance / 100f;
        // expected damage of a connecting shot: average dmg, lifted by the crit chance
        // (a crit deals ~1.5×+1). Grazes (the miss-by-≤15 band) add a little guaranteed chip.
        float avgDmg = (odds.DmgMin + odds.DmgMax) * 0.5f;
        float critDmg = avgDmg * 1.5f + 1f;
        float pc = odds.CritChance / 100f;
        float expConnect = avgDmg * (1f - pc) + critDmg * pc;
        // a rough "partial-hit tail" nudge: shots that miss by <= GrazeBand still chip for
        // DmgMin. GrazeBand is a margin in aim-points, not a true probability, so this is a
        // small heuristic bonus (ranking-only), NOT a precise EV term — fine for tie-breaking.
        float grazeChip = Combat.GrazeBand / 100f * odds.DmgMin;
        float ev = hit * expConnect + grazeChip;

        // finisher: if a connecting hit very likely kills, that's worth far more than raw EV
        // (removing a gun from the board). Scale by how reliably we'd land it.
        if (target.Hp <= odds.DmgMin) ev += 14f * hit;               // even a min-roll kills
        else if (target.Hp <= avgDmg) ev += 9f * hit;                // an average roll kills
        else if (target.Hp <= odds.DmgMax) ev += 4f * hit;           // a good roll kills

        if (odds.Flanked) ev += 5f;                                   // flank → reliable crit
        else if (odds.CoverLevel == 0) ev += 2f;                     // exposed
        ev += PriorityWeight(target) * hit * 0.30f;                  // kill the dangerous ones first
        return ev;
    }

    /// Best targetable enemy from tile (ax,ay) and the value of that shot. Considers every
    /// living foe in range+LoS from there. Returns (null, 0) if no shot exists from the tile.
    (Unit tgt, float val) BestShotFrom(Unit u, int ax, int ay)
    {
        if (u.Ammo <= 0) return (null, 0f);
        Unit best = null; float bestVal = 0f;
        int ox = u.X, oy = u.Y; u.X = ax; u.Y = ay;
        bool can(Unit e)                                  // CanTarget evaluated from (ax,ay)
        {
            if (e == null || !e.Alive || (e == Vip && CaptiveLocked)) return false;
            if (Util.TileDist(ax, ay, e.X, e.Y) > u.Weapon.MaxRange) return false;
            bool commanding = Grid.HeightAt(ax, ay) - Grid.HeightAt(e.X, e.Y) >= 2;
            return Grid.HasLineOfSight(ax, ay, e.X, e.Y, commanding);
        }
        foreach (var e in Enemies)
        {
            if (!can(e)) continue;
            var odds = Combat.ComputeOdds(Grid, u, e);
            float v = ShotValue(odds, e);
            if (v > bestVal) { bestVal = v; best = e; }
        }
        u.X = ox; u.Y = oy;
        return (best, bestVal);
    }

    /// The 2nd-highest-value targetable foe from the soldier's current tile, or null if it has
    /// fewer than two shots. Used ONLY by the sloppy policy to model a human mis-prioritisation
    /// (it's a worse-but-legal shot, so the turn still progresses). Read-only.
    Unit SecondBestTarget(Unit u)
    {
        if (u.Ammo <= 0) return null;
        Unit best = null, second = null; float bv = float.NegativeInfinity, sv = float.NegativeInfinity;
        foreach (var e in Enemies)
        {
            if (!e.Alive || (e == Vip && CaptiveLocked)) continue;
            if (Util.TileDist(u.X, u.Y, e.X, e.Y) > u.Weapon.MaxRange) continue;
            bool commanding = Grid.HeightAt(u.X, u.Y) - Grid.HeightAt(e.X, e.Y) >= 2;
            if (!Grid.HasLineOfSight(u.X, u.Y, e.X, e.Y, commanding)) continue;
            float v = ShotValue(Combat.ComputeOdds(Grid, u, e), e);
            if (v > bv) { sv = bv; second = best; bv = v; best = e; }
            else if (v > sv) { sv = v; second = e; }
        }
        return second;
    }

    /// Fire the best expected-value shot the soldier can take from where it stands — but
    /// only if that shot is worth taking (a desperate 3% poke that ends the turn is usually
    /// worse than repositioning). Returns true if it shot.
    /// Autopilot: shoot an explosive barrel that would catch 2+ enemies (and no friendly) in its
    /// blast — a guaranteed multi-hit AoE worth more than a single aimed shot. Exercises the
    /// hazard system in the flywheel. Returns true if it fired.
    bool TryShootBarrel(Unit u)
    {
        if (u.Ammo <= 0 || !u.CanAct) return false;
        int bx = -1, by = -1, best = 0;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                if (!Grid.IsBarrel(x, y)) continue;
                if (!CanShootBarrel(u, x, y)) continue;
                int foes = Enemies.Count(e => e.Alive && Util.ChebyDist(e.X, e.Y, x, y) <= BarrelRadius);
                bool allyHit = Players.Any(p => p.Alive && Util.ChebyDist(p.X, p.Y, x, y) <= BarrelRadius);
                if (allyHit || foes < 2) continue;
                if (foes > best) { best = foes; bx = x; by = y; }
            }
        if (best < 2) return false;
        if (SquadConcealed) BreakConcealment(u);
        u.Ammo--; u.ActionsLeft = 0;
        u.Steady = false; u.FiredFromConcealment = false;
        SetBarrelCredit(u);
        Enqueue(new BarrelShotAnim(u, bx, by), Team.Player);
        return true;
    }

    bool TakeBestShot(Unit u)
    {
        if (u.Ammo <= 0) return false;
        if (TryShootBarrel(u)) return true;     // a 2+-enemy barrel beats any single shot
        var (tgt, val) = BestShotFrom(u, u.X, u.Y);
        // SLOPPY: ~15% of the time aim at the 2nd-best target instead (a human mis-prioritisation)
        // — still a real shot, just a worse pick, so the run is harder but never stalls.
        if (Slip(15)) { var alt = SecondBestTarget(u); if (alt != null) tgt = alt; }
        if (tgt == null) return false;
        var odds = Combat.ComputeOdds(Grid, u, tgt);
        // worth firing? a hit chance floor OR a likely finisher (a near-certain kill of a
        // low-HP foe is worth a poor-percentage shot). Otherwise prefer to reposition.
        bool finisher = tgt.Hp <= odds.DmgMax && odds.HitChance >= 35;
        bool decent   = odds.HitChance >= 45;
        // if the soldier has BOTH actions, a weak shot is fine via SNAP (keeps acting); a
        // turn-ending aimed shot should clear a higher bar. Either way, take a real chance.
        // TEMPO: firing no longer ends the turn, so a marginal shot is cheap (the soldier keeps an
        // action to reposition). With two actions in hand, take any real chance; with one, hold a
        // higher bar so we don't waste the soldier's only action on a coin-flip.
        bool twoActions = u.ActionsLeft >= 2;
        if (!finisher && !decent && !(twoActions && odds.HitChance >= 30)) return false;
        AutoShootSmart(u, tgt);
        return true;
    }

    /// True if the soldier has a high-confidence shot from where it stands (used by the
    /// objective routines to decide "is a kill worth pausing the advance for?").
    bool HasStrongShot(Unit u)
    {
        if (u.Ammo <= 0) return false;
        var (tgt, _) = BestShotFrom(u, u.X, u.Y);
        if (tgt == null) return false;
        var odds = Combat.ComputeOdds(Grid, u, tgt);
        return odds.HitChance >= 60 || (tgt.Hp <= odds.DmgMax && odds.HitChance >= 50);
    }

    /// Fire at `tgt`. TEMPO: the aimed shot is now always 1 action and never ends the turn, so
    /// there's no SNAP/AIMED decision to make — fire at full aim and let the soldier keep its
    /// second action for repositioning (handled by SmartRetreatAfterShot in the cascade).
    void AutoShootSmart(Unit u, Unit tgt)
    {
        IssueShoot(tgt);
    }

    /// Prep a class ability when it improves THIS soldier's turn. Deliberate (never random):
    ///   - PATCH (corpsman): heal the most-wounded adjacent ally if it's meaningfully hurt;
    ///   - STEADY (sharpshooter): brace before a real shot to sharpen it;
    ///   - SUPPRESS (gunner): pin a dangerous foe we can't cleanly kill;
    ///   - RUN&GUN (assault) / BLITZ (ranger): free tempo stances — take them when they help.
    /// Returns true if it spent the turn on the ability (Steady/Suppress/Heal cost an action;
    /// RunGun/Blitz are free, so they DON'T return true — the soldier acts with them this step).
    bool PrepAbility(Unit u) => PrepAbilityFor(u, null);

    bool PrepAbilityFor(Unit u, Unit forcedTarget)
    {
        if (!CanAbility(u)) return false;
        switch (u.Ability)
        {
            case AbilityKind.Heal:
            {
                // only patch when an adjacent ally is genuinely hurt (>=4 missing HP, so the
                // +PatchHeal isn't wasted) — MostWoundedAdjacentAlly already gates on Hp<MaxHp.
                var ally = MostWoundedAdjacentAlly(u);
                if (ally != null && ally.MaxHp - ally.Hp >= 4) { DoAbility(); return true; }
                return false;
            }
            case AbilityKind.Steady:
            {
                // brace only if there's a real shot to sharpen and we can still fire after
                // (Steady costs one action; need >=2 so a shot remains). Worth it for a shot
                // that isn't already near-certain.
                if (u.ActionsLeft < 2 || u.Ammo <= 0) return false;
                var tgt = forcedTarget != null && CanTarget(u, forcedTarget) ? forcedTarget : FirstTargetFor(u);
                if (tgt == null) return false;
                var odds = Combat.ComputeOdds(Grid, u, tgt);
                if (odds.HitChance >= 40 && odds.HitChance <= 90) { DoAbility(); return true; }
                return false;
            }
            case AbilityKind.Suppress:
            {
                // pin a foe we can see — best used on a dangerous attacker we can't reliably
                // kill outright (it slashes its aim and trains overwatch on it). Skip if we
                // already have a clean kill shot (just take the kill instead).
                var tgt = FirstTargetFor(u);
                if (tgt == null) return false;
                var odds = Combat.ComputeOdds(Grid, u, tgt);
                bool cleanKill = tgt.Hp <= odds.DmgMax && odds.HitChance >= 55;
                if (cleanKill) return false;                       // prefer the kill
                if (PriorityWeight(tgt) >= 14f || odds.HitChance < 45) { DoAbility(); return true; }
                return false;
            }
            case AbilityKind.RunGun:
            {
                // free stance: only worth it if firing this turn (so the shot doesn't end the
                // turn, letting the soldier move+shoot or shoot twice). Take it before a shot.
                if (u.Ammo > 0 && FirstTargetFor(u) != null) { DoAbility(); return false; }  // free → act with it
                return false;
            }
            case AbilityKind.Blitz:
            {
                // free stance: cheap movement. Take it when we have no shot and need to close
                // distance toward a foe/objective this turn (so the move costs one less action).
                if (FirstTargetFor(u) == null && AliveEnemies().Count > 0) { DoAbility(); return false; }
                return false;
            }
            case AbilityKind.Mark:
            {
                // designate a high-value visible foe so the whole squad shoots it better this round.
                // Costs an action; only worth it when we can still fire after AND there's a
                // worthwhile target nobody's marked. Don't mark a foe we can already cleanly kill.
                if (u.ActionsLeft < 2) return false;     // keep an action to actually shoot
                var tgt = forcedTarget != null && MarkTargetOk(u, forcedTarget) ? forcedTarget : BestMarkTarget(u);
                if (tgt == null) return false;
                var odds = Combat.ComputeOdds(Grid, u, tgt);
                if (tgt.Hp <= odds.DmgMax && odds.HitChance >= 60) return false;  // just kill it
                IssueMark(u, tgt);
                return false;   // didn't end the turn — fall through and act (shoot) with the action left
            }
            case AbilityKind.Grapple:
            {
                // yank a foe out of cover so the squad can hit it. Only when there's a covered foe
                // in reach AND we can act after (the grapple costs an action but not the turn).
                if (u.ActionsLeft < 2 || u.ShovedThisTurn) return false;
                var tgt = BestGrappleTarget(u);
                if (tgt == null) return false;
                if (Grid.GetCover(tgt.X, tgt.Y, u.X, u.Y).Level <= 0) return false;  // only worth it vs a covered foe
                IssueGrapple(u, tgt);
                return false;   // free of end-turn; act with the remaining action
            }
            case AbilityKind.Slipstream:
            {
                // RANGER free reposition: take it when we have no shot but need to close on a foe/objective —
                // a 0-action, overwatch-immune move. Like Blitz it's free, so DON'T return true (act with it).
                if (FirstTargetFor(u) == null && AliveEnemies().Count > 0) { DoAbility(); return false; }
                return false;
            }
            case AbilityKind.Pin:
            {
                // GUNNER area denial: lay suppressing fire on a CLUSTER of foes (2+ in the 3x3) we can't
                // cleanly kill — it ends the turn (the full burst), so prefer it over a single weak shot.
                var tgt = BestPinTarget(u);
                if (tgt == null) return false;
                int cluster = 0;
                foreach (var o in Enemies)
                    if (o.Alive && o.Team == Team.Enemy && Util.ChebyDist(tgt.X, tgt.Y, o.X, o.Y) <= 1) cluster++;
                var odds = Combat.ComputeOdds(Grid, u, tgt);
                bool cleanKill = tgt.Hp <= odds.DmgMax && odds.HitChance >= 55;
                if (cleanKill) return false;                  // just take the kill
                if (cluster >= 2 || odds.HitChance < 45) { IssuePin(u, tgt); return true; }  // burst denies the zone
                return false;
            }
        }
        return false;
    }

    /// Grenade decision: lob at the cluster of enemies that catches the most foes (≥2),
    /// or flush a single well-covered/high-priority target our gun can't crack.
    /// Never catches an ally. Mirrors the enemy grenade AI's fairness (LoS-gated by CanGrenade
    /// through IssueGrenade's range check + our own LoS test). Returns true if it threw.
    bool SmartGrenade(Unit u)
    {
        if (u.Grenades <= 0) return false;
        int bx = -1, by = -1, bestHits = 0; bool bestCovered = false; float bestPrio = 0f;
        foreach (var e in AliveEnemies())
        {
            if (!CanGrenade(u, e.X, e.Y)) continue;                 // in range + LoS from here
            if (!NoAllyInBlast(e.X, e.Y)) continue;                 // never frag our own
            int hits = 0;
            foreach (var q in AliveEnemies()) if (Util.ChebyDist(e.X, e.Y, q.X, q.Y) <= GrenadeAnim.Radius) hits++;
            // is the aim foe well-covered from us (so our bullets are weak)?
            var odds = SmartOdds(u, u.X, u.Y, e);
            bool covered = odds.CoverLevel >= 1 || odds.HitChance < 45;
            float prio = PriorityWeight(e);
            if (hits > bestHits || (hits == bestHits && prio > bestPrio))
            { bestHits = hits; bx = e.X; by = e.Y; bestCovered = covered; bestPrio = prio; }
        }
        if (bx < 0) return false;
        // throw when it catches 2+, OR a single target that's well-covered or high-priority
        // (a frag ignores cover) — i.e. when the grenade beats what our gun would do.
        if (bestHits >= 2 || (bestHits == 1 && (bestCovered || bestPrio >= 20f)))
        { IssueGrenade(bx, by); return true; }
        return false;
    }

    /// True if (tx,ty) is in grenade range AND the soldier has line of sight to it (no
    /// blind lobbing over high cover / through smoke — matches the perfect-info contract).
    bool CanGrenade(Unit u, int tx, int ty)
        => Util.TileDist(u.X, u.Y, tx, ty) <= GrenadeRange && Grid.HasLineOfSight(u.X, u.Y, tx, ty);

    /// Per-tile exposure (mirrors ComputeThreat, but always available and not gated on the
    /// player pref): how many live, active, armed enemies could fire on (x,y) with NO cover
    /// for the mover. Weighted by the shooter's threat priority, so standing exposed to a
    /// sniper hurts the score more than exposure to a grunt. This is the threat term the
    /// brief asks us to SUBTRACT from destination tiles.
    float TileExposure(Unit mover, int x, int y)
    {
        float threat = 0f;
        if (Grid.IsFire(x, y)) threat += 20f;   // never voluntarily end a move standing in fire (hazards)
        if (InSiegeZone(x, y)) threat += 30f;   // a charged SIEGE strike WILL land here -> vacate (cover-ignoring)
        foreach (var e in Enemies)
        {
            if (!e.Alive || !e.Active || e.Ammo <= 0) continue;
            if (Util.TileDist(x, y, e.X, e.Y) > e.Weapon.MaxRange) continue;
            if (!Grid.HasLineOfSight(e.X, e.Y, x, y)) continue;
            // cover for the MOVER standing at (x,y) against this shooter
            if (Grid.GetCover(x, y, e.X, e.Y).Level == 0)
                threat += 6f + PriorityWeight(e) * 0.5f;            // exposed to this gun
        }
        return threat;
    }

    /// Score a prospective destination tile for `u`, Ai.Plan-style but from the PLAYER's
    /// perspective: reward a good shot available from there, cover, high ground, a flank on
    /// the nearest foe; subtract exposure (threat) and movement cost. `advanceTarget`, when
    /// given, adds a mild pull toward it (objective/foe) so positioning still makes progress.
    float ScoreDestTile(Unit u, int x, int y, int actionsToReach, Unit nearest, (int x, int y)? advanceTarget)
    {
        float score = 0f;

        // a shot from here is the biggest prize (only if we'd keep an action to fire it).
        if (actionsToReach <= 1)
        {
            var (tgt, val) = BestShotFrom(u, x, y);
            if (tgt != null) score += 60f + val * 2.2f;
        }

        // terrain: cover + height vs the nearest foe (use it as the reference angle).
        if (nearest != null)
        {
            var cov = Grid.GetCover(x, y, nearest.X, nearest.Y);
            score += cov.Level * 16f;
            if (cov.Flanked) score -= 18f;                         // our own cover useless from here
        }
        score += Grid.HeightAt(x, y) * 12f;                        // seize high ground

        // exposure: avoid tiles a live enemy can shoot with no cover for us (the threat term).
        score -= TileExposure(u, x, y);

        score -= actionsToReach * 5f;                              // prefer cheaper moves

        // mild pull toward the advance target so repositioning still closes the gap.
        if (advanceTarget != null)
            score -= Util.ChebyDist(x, y, advanceTarget.Value.x, advanceTarget.Value.y) * 1.4f;

        score += Util.RandRange(0f, 2f);                           // tie-break jitter
        return score;
    }

    /// Reposition toward the best firing tile on the nearest foe: an Ai.Plan-style sweep of
    /// reachable tiles scored by ScoreDestTile (cover/height/flank/shot − exposure − cost,
    /// pulled toward the foe). Moves there if it beats standing still. This is the core
    /// "advance behind cover and set up flanks" behaviour. Returns true if it moved.
    bool SmartApproach(Unit u)
    {
        var foe = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
        if (foe == null) return false;
        return MoveToBestTile(u, foe, (foe.X, foe.Y));
    }

    /// Like SmartApproach but with no advance pull — purely shuffle into better cover/safety
    /// near where we already are (used when holding a position, e.g. DEFEND / a held zone).
    bool SmartReposition(Unit u)
    {
        var foe = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
        return MoveToBestTile(u, foe, null);
    }

    /// Move `u` to the best-scoring reachable tile (vs the value of staying put). `nearest`
    /// is the reference foe for cover/flank scoring; `advance`, if set, pulls toward a goal.
    /// Falls back to the distance-only TryMoveTowardTile if scoring finds nothing better, so
    /// progress toward the goal is still guaranteed. Returns true if it issued a move.
    bool MoveToBestTile(Unit u, Unit nearest, (int x, int y)? advance)
    {
        if (MoveCost == null) return false;
        // score of staying at the current tile (it's always "reachable" at cost 0).
        float stayScore = ScoreDestTile(u, u.X, u.Y, 0, nearest, advance);
        int bx = -1, by = -1; float bestScore = stayScore;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                int c = MoveCost[x, y];
                if (c <= 0) continue;                              // unreachable / current tile
                int need = c <= u.MoveBudget ? 1 : 2;
                int cost = u.Blitz ? Math.Max(0, need - 1) : need;
                if (cost > u.ActionsLeft) continue;                // can't afford it
                float s = ScoreDestTile(u, x, y, need, nearest, advance);
                if (s > bestScore) { bestScore = s; bx = x; by = y; }
            }
        if (bx >= 0) { IssueMove(bx, by); return true; }
        // nothing scored better than standing still: if we have a goal, still close on it so
        // the match never stalls (guaranteed progress). Otherwise stay put (caller hunkers).
        if (advance != null) return TryMoveTowardTile(u, advance.Value.x, advance.Value.y);
        return false;
    }

    /// SIEGE flee: move `u` to the best reachable tile NOT inside any live strike zone (cover-aware
    /// via ScoreDestTile, which now penalizes zone tiles by +30, so this picks a safe-AND-good spot).
    /// Returns false only if no out-of-zone tile is reachable (very rare given squad mobility vs a 3x3)
    /// — the caller then falls through to normal routing, which always ends in DoHunker (no TIMEOUT).
    bool SmartFleeSiege(Unit u)
    {
        if (MoveCost == null) return false;
        var nearest = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
        (int x, int y)? adv = nearest != null ? (nearest.X, nearest.Y) : ((int, int)?)null;
        int bx = -1, by = -1; float best = float.NegativeInfinity;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                int c = MoveCost[x, y]; if (c <= 0) continue;
                int need = c <= u.MoveBudget ? 1 : 2;
                int cost = u.Blitz ? Math.Max(0, need - 1) : need;
                if (cost > u.ActionsLeft) continue;
                if (InSiegeZone(x, y)) continue;                  // must leave the zone
                float s = ScoreDestTile(u, x, y, need, nearest, adv);
                if (s > best) { best = s; bx = x; by = y; }
            }
        if (bx >= 0) { IssueMove(bx, by); return true; }
        return false;
    }

    /// Path toward (gx,gy) but prefer covered/safe stepping tiles when the move can reach
    /// the goal area: try the cover-aware sweep first (pulled toward the goal), then fall
    /// back to the plain distance-only step so progress is always guaranteed.
    bool SmartMoveToward(Unit u, int gx, int gy)
    {
        var nearest = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
        // only bother with cover-aware routing when there's a live threat to avoid; with no
        // active enemies, march straight (faster to the objective, and TileExposure is 0).
        if (nearest != null && Enemies.Any(e => e.Alive && e.Active))
        {
            if (MoveToBestTile(u, nearest, (gx, gy))) return true;
        }
        return TryMoveTowardTile(u, gx, gy);
    }

    /// VIP/asset advance toward (gx,gy). The asset dies in one or two hits and the enemy AI
    /// hunts it, so this is SURVIVAL-FIRST: only bound forward into a tile that's genuinely
    /// SAFE (no live enemy can shoot it there — exposure 0 — or it ends in cover). Among safe
    /// forward tiles, take the one that closes the most distance. If NO safe forward tile
    /// exists, WAIT in place (let the escorts clear the lane) — UNLESS no active enemy can
    /// even see the asset right now (the lane is already clear → just walk), which also doubles
    /// as the anti-stall escape (a clear board → beeline → reach evac → win). Returns true if
    /// it issued a move; false means "hold here" (the caller hunkers — turn still ends).
    bool VipAdvance(Unit u, int gx, int gy)
    {
        if (MoveCost == null) return TryMoveTowardTile(u, gx, gy);
        int hereDist = Util.ChebyDist(u.X, u.Y, gx, gy);
        int bx = -1, by = -1; int bestProg = 0; float bestScore = float.NegativeInfinity;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                int c = MoveCost[x, y];
                if (c <= 0) continue;
                int need = c <= u.MoveBudget ? 1 : 2;
                int cost = u.Blitz ? Math.Max(0, need - 1) : need;
                if (cost > u.ActionsLeft) continue;
                int prog = hereDist - Util.ChebyDist(x, y, gx, gy);     // tiles closer to evac
                if (prog <= 0) continue;                                // forward only
                float expo = TileExposure(u, x, y);
                var nearest = AliveEnemies().OrderBy(e => Util.ChebyDist(x, y, e.X, e.Y)).FirstOrDefault();
                int cov = nearest != null ? Grid.GetCover(x, y, nearest.X, nearest.Y).Level : 2;
                bool safe = expo <= 0f || cov >= 1;                     // no exposed-gun OR in cover
                if (!safe) continue;
                // among safe forward tiles, maximise progress, then cover, then least exposure.
                float score = prog * 4f + cov * 3f - expo;
                if (score > bestScore) { bestScore = score; bx = x; by = y; bestProg = prog; }
            }
        if (bx >= 0) { _vipWaitTurns = 0; IssueMove(bx, by); return true; }   // safe step: reset patience
        // no SAFE forward tile. If the asset is currently unseen by any active foe, the lane
        // is clear enough — just beeline (also doubles as an anti-stall valve: a cleared board
        // ends in a win). Otherwise hold and let the escorts clear the lane.
        bool unseen = !Enemies.Any(e => e.Alive && e.Active && Grid.HasLineOfSight(e.X, e.Y, u.X, u.Y));
        if (unseen) { _vipWaitTurns = 0; return TryMoveTowardTile(u, gx, gy); }
        // PATIENCE / ANTI-TIMEOUT: don't hold forever (the escorts may never clear that
        // watcher). After several held turns, accept the risk and push toward evac so the
        // mission always resolves. Bounded — the match can never stall on a waiting VIP.
        if (++_vipWaitTurns >= 2) { _vipWaitTurns = 0; return TryMoveTowardTile(u, gx, gy); }
        return false;   // wait this turn (hold in current cover); caller hunkers (turn still ends)
    }

    /// Hold overwatch when it's the right call: the soldier has ammo + an action, isn't
    /// disoriented, and a live enemy is near enough to plausibly walk into the lane this
    /// enemy turn (so we don't waste overwatch staring at an empty board). Returns true if set.
    bool HoldOverwatch(Unit u)
    {
        if (u.Ammo <= 0 || u.ActionsLeft <= 0 || u.HasStatus(StatusKind.Disoriented)) return false;
        // a foe that's active and within a turn's move + weapon reach is a credible pusher.
        bool foesWillPush = Enemies.Any(e => e.Alive && e.Active
            && Util.TileDist(u.X, u.Y, e.X, e.Y) <= e.Weapon.MaxRange + e.Mobility);
        if (!foesWillPush) return false;
        DoOverwatch();
        return true;
    }

    // Stall guard for the headless autopilot: if no progress is made for several
    // player turns (e.g. only unreachable dormant pods remain), force a pod awake
    // so the match always resolves. Test-only; never runs in normal play.
    int _autoSig = -1, _autoStall;
    int _vipWaitTurns;       // SmartStep Escort: consecutive turns the VIP held for safety (anti-stall)
    int _smartConcealTurns;  // SmartStep: player turns spent concealed (hard anti-TIMEOUT cap)
    const int AutoMaxTurns = 50;  // hard autopilot match cap: force-end a dragging match as a LOSS
    void AutoStallCheck()
    {
        // HARD no-TIMEOUT backstop: a match still going at AutoMaxTurns is effectively stalled (normal
        // matches resolve in ~5-15 turns; Defend caps at 8). Force-lose so the smoke test / balance
        // batch ALWAYS terminates well before the frame cap — never a RESULT: TIMEOUT. Only ever fires
        // in autoplay (this method is autoplay-only); real play is unaffected.
        if (_turnCount > AutoMaxTurns)
        { LoseRun("STALEMATE", $"Autopilot exceeded the turn cap on mission {_run.Mission}."); return; }

        int sig = AliveEnemies().Count * 1000
                + AliveEnemies().Count(e => e.Active) * 10
                + HackProgress
                + AlivePlayers().Count(p => EvacZone.Contains((p.X, p.Y)));
        if (sig != _autoSig) { _autoSig = sig; _autoStall = 0; return; }
        if (++_autoStall < 10) return;
        _autoStall = 0;
        if (SquadConcealed) BreakConcealment();   // 4.4: a stalled autopilot reveals itself
        var dormant = Enemies.Where(e => e.Alive && !e.Active).ToList();
        if (dormant.Count > 0) ActivatePod(dormant[0].PodId);
    }

    void AutoStep()
    {
        if (_anims.Count > 0 || Phase != Phase.PlayerTurn) return;
        TryFreeCaptive();                       // free a captive a soldier is already standing next to
        var u = Players.FirstOrDefault(p => p.CanAct);
        if (u == null) { EndPlayerTurn(); return; }
        Selected = u;
        RecomputeMoveCost();

        // 4.4: autopilot springs the ambush once it has a shot (u then fires it next step
        // with the bonus), else when it has stalked within range; otherwise it keeps
        // advancing concealed. Returns so any reveal-scatter plays before the shot.
        if (SquadConcealed)
        {
            if (u.Ammo > 0 && FirstTargetFor(u) != null) { BreakConcealment(u); return; }
            if (Players.Any(p => p.Alive && Enemies.Any(e => e.Alive
                    && Util.TileDist(p.X, p.Y, e.X, e.Y) <= AlertRange + 1))) { BreakConcealment(); return; }
        }

        // EVAC objective: get everyone to the extraction zone
        if (Objective == Objective.Evac)
        {
            if (!EvacZone.Contains((u.X, u.Y)))
            {
                var cand = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                                   .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).ToList();
                if (cand.Count > 0 && TryMoveTowardTile(u, cand[0].x, cand[0].y)) return;
            }
            // can't make extraction progress this turn — clear blockers / re-arm
            var et = FirstTargetFor(u);
            if (et != null && u.Ammo > 0) { IssueShoot(et); return; }
            // an enemy squatting on the extraction zone: blast it loose
            if (u.Grenades > 0)
            {
                var blocker = AliveEnemies()
                    .Where(e => EvacZone.Contains((e.X, e.Y)) && Util.TileDist(u.X, u.Y, e.X, e.Y) <= GrenadeRange)
                    .OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
                if (blocker != null) { IssueGrenade(blocker.X, blocker.Y); return; }
            }
            if (u.Ammo == 0) { DoReload(); return; }   // re-arm instead of stalling forever
            DoHunker();
            return;
        }

        // HACK objective: get a soldier to the terminal and hack it down
        if (Objective == Objective.Hack)
        {
            if (CanHack(u)) { DoHack(); return; }
            var et = FirstTargetFor(u);
            if (et != null && u.Ammo > 0) { IssueShoot(et); return; }
            if (TryMoveTowardTile(u, Terminal.x, Terminal.y)) return;
            DoHunker();
            return;
        }

        // SABOTAGE objective: plant charges on each site in turn
        if (Objective == Objective.Sabotage)
        {
            if (CanHack(u)) { DoHack(); return; }
            var et = FirstTargetFor(u);
            if (et != null && u.Ammo > 0) { IssueShoot(et); return; }
            var site = SabotageSites
                .Where((s, i) => !SabotageBlown.Contains(i))
                .OrderBy(s => Util.TileDist(u.X, u.Y, s.x, s.y)).FirstOrDefault();
            if (site != default && TryMoveTowardTile(u, site.x, site.y)) return;
            DoHunker();
            return;
        }

        // ESCORT objective: walk the VIP to extraction; soldiers screen for it
        if (Objective == Objective.Escort)
        {
            if (u.IsVip)
            {
                if (!EvacZone.Contains((u.X, u.Y)))
                {
                    var cand = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                                       .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).ToList();
                    if (cand.Count > 0 && TryMoveTowardTile(u, cand[0].x, cand[0].y)) return;
                }
                DoHunker(); return;        // arrived or no path this turn
            }
            var st = FirstTargetFor(u);
            if (st != null && u.Ammo > 0) { IssueShoot(st); return; }
            if (u.Ammo == 0) { DoReload(); return; }
            var foe = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
            if (foe != null && TryMoveTowardTile(u, foe.X, foe.Y)) return;
            DoHunker(); return;
        }

        // RESCUE objective: reach the caged captive to free it, then walk it to extraction
        if (Objective == Objective.Rescue)
        {
            if (u.IsVip)
            {
                if (CaptiveLocked) { DoHunker(); return; }     // can't move while caged
                if (!EvacZone.Contains((u.X, u.Y)))
                {
                    var cand = EvacZone.Where(t => !IsOccupiedByOther(t.x, t.y, u))
                                       .OrderBy(t => Util.TileDist(u.X, u.Y, t.x, t.y)).ToList();
                    if (cand.Count > 0 && TryMoveTowardTile(u, cand[0].x, cand[0].y)) return;
                }
                DoHunker(); return;
            }
            if (CaptiveLocked && Vip != null && Util.ChebyDist(u.X, u.Y, Vip.X, Vip.Y) > 1
                && TryMoveTowardTile(u, Vip.X, Vip.Y)) return;   // go spring the captive
            var rt = FirstTargetFor(u);
            if (rt != null && u.Ammo > 0) { IssueShoot(rt); return; }
            if (u.Ammo == 0) { DoReload(); return; }
            var rfoe = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
            if (rfoe != null && TryMoveTowardTile(u, rfoe.X, rfoe.Y)) return;
            DoHunker(); return;
        }

        // DEFEND objective: hold position, shoot, overwatch, hunker until the timer runs out
        if (Objective == Objective.Defend)
        {
            var dt = FirstTargetFor(u);
            if (dt != null && u.Ammo > 0) { AutoShoot(u, dt); return; }
            if (u.Ammo == 0) { DoReload(); return; }
            if (u.ActionsLeft > 0 && u.Ammo > 0) { DoOverwatch(); return; }
            DoHunker(); return;
        }

        // DECAPITATE objective: focus the marked HVT. Shoot it on sight, grenade it if it's the
        // only reachable play, otherwise advance on it; fall through to the generic combat
        // behaviour (clear blockers / reload / hunker) so progress is always guaranteed.
        if (Objective == Objective.Decapitate)
        {
            if (Hvt != null && Hvt.Alive)
            {
                if (u.Ammo > 0 && CanTarget(u, Hvt)) { AutoShoot(u, Hvt); return; }     // kill the target
                if (u.Ammo == 0) { DoReload(); return; }
                if (u.Grenades > 0 && Util.TileDist(u.X, u.Y, Hvt.X, Hvt.Y) <= GrenadeRange
                    && !Players.Any(f => f.Alive && Util.ChebyDist(f.X, f.Y, Hvt.X, Hvt.Y) <= 1))
                { IssueGrenade(Hvt.X, Hvt.Y); return; }                                  // flush it out
                // can't hit it yet: shoot anything blocking the path, else close on the HVT
                var blk = FirstTargetFor(u);
                if (blk != null && blk != Hvt && u.Ammo > 0
                    && Util.TileDist(u.X, u.Y, blk.X, blk.Y) <= 3) { AutoShoot(u, blk); return; }
                if (TryMoveTowardTile(u, Hvt.X, Hvt.Y)) return;
            }
            // HVT already dead (CheckEnd will end the mission) or unreachable: keep generic.
            var dtgt = FirstTargetFor(u);
            if (dtgt != null && u.Ammo > 0) { AutoShoot(u, dtgt); return; }
            if (u.Ammo == 0) { DoReload(); return; }
            var dfoe = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
            if (dfoe != null && TryMoveTowardTile(u, dfoe.X, dfoe.Y)) return;
            DoHunker(); return;
        }

        // ELIMINATE objective
        // (test) exercise the class signature ability to keep its path covered
        if (CanAbility(u) && Util.Roll(45))
        {
            var kind = u.Ability;
            // the targeting VERBS resolve directly (DoAbility would open a mode the dumb smoke-test
            // autopilot can't drive) -> exercise the IssueMark/IssueGrapple/IssuePin paths.
            if (kind == AbilityKind.Mark)
            {
                var mt = BestMarkTarget(u);
                if (mt != null) IssueMark(u, mt);    // costs an action but not the turn -> fall through and shoot
            }
            else if (kind == AbilityKind.Grapple)
            {
                var gt = BestGrappleTarget(u);
                if (gt != null) IssueGrapple(u, gt); // costs an action but not the turn -> fall through and shoot
            }
            else if (kind == AbilityKind.Pin)
            {
                var pt = BestPinTarget(u);
                if (pt != null) { IssuePin(u, pt); return; } // SUPPRESSING FIRE ends the turn (full burst)
            }
            else
            {
                DoAbility();
                if (kind == AbilityKind.Steady || kind == AbilityKind.Suppress || kind == AbilityKind.Heal) return; // spent an action
                // RunGun / Blitz / Slipstream are free stances — fall through and act with them
            }
        }

        var tgt = FirstTargetFor(u);
        if (tgt != null && u.Ammo > 0) { AutoShoot(u, tgt); return; }
        if (u.Ammo == 0) { DoReload(); return; }

        // (test) deploy the class utility item to keep its path covered
        if (u.ItemCharge > 0 && Util.Roll(30))
        {
            if (u.Item == ItemKind.Barricade)
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nx = u.X + dx, ny = u.Y + dy;
                        if ((dx != 0 || dy != 0) && ItemTargetOk(u, nx, ny)) { IssueItem(nx, ny); return; }
                    }
            }
            else
            {
                var near = AliveEnemies().Where(e => Util.TileDist(u.X, u.Y, e.X, e.Y) <= ItemRange)
                                         .OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
                if (near != null) { IssueItem(near.X, near.Y); return; }
            }
        }

        // lob a grenade at any hostile in range (exercises the AoE path)
        if (u.Grenades > 0)
        {
            var near = AliveEnemies().Where(e => Util.TileDist(u.X, u.Y, e.X, e.Y) <= GrenadeRange)
                                     .OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
            if (near != null) { IssueGrenade(near.X, near.Y); return; }
        }

        var enemy = AliveEnemies().OrderBy(e => Util.TileDist(u.X, u.Y, e.X, e.Y)).FirstOrDefault();
        if (enemy != null && TryMoveTowardTile(u, enemy.X, enemy.Y)) return;
        DoHunker(); // guarantees progress
    }

    // autopilot helper: fire at `tgt`, exercising the SNAP path so it stays covered. When the
    // soldier has both actions, ~40% take a snap (1 action, no end-turn, -aim) instead of the
    // turn-ending aimed shot; the soldier then keeps acting next AutoStep (move/second snap/
    // overwatch). Bounded: a snap always costs >=1 action, so at most two snaps end the turn —
    // no infinite loop. (The flank-kill refund is capped 1/turn, so it can't unbound this.)
    void AutoShoot(Unit u, Unit tgt)
    {
        if (u.ActionsLeft >= 2 && !u.RunGun && Util.Roll(40)) SnapShot = true;  // consumed by IssueShoot
        IssueShoot(tgt);
    }

    // autopilot helper: step toward (gx,gy) along true path distance; random hop if stuck
    bool TryMoveTowardTile(Unit u, int gx, int gy)
    {
        if (MoveCost == null) return false;
        var gd = Grid.CostMap(gx, gy, (x, y) => IsOccupiedByOther(x, y, u), out _, 9999);
        int my = gd[u.X, u.Y];
        int bx = -1, by = -1, best = int.MaxValue;
        var reachable = new List<(int, int)>();
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                int c = MoveCost[x, y];
                if (c <= 0) continue;
                int need = c <= u.MoveBudget ? 1 : 2;
                if (need > u.ActionsLeft) continue;
                reachable.Add((x, y));
                int d = gd[x, y];
                if (d >= 0 && d < best) { best = d; bx = x; by = y; }
            }
        if (bx >= 0 && (my < 0 || best < my)) { IssueMove(bx, by); return true; }
        if (reachable.Count > 0) { var (rx, ry) = Util.Choice(reachable); IssueMove(rx, ry); return true; }
        return false;
    }

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
            if (ClosestSightedDist(e) >= 0)
            {
                e.Alert = AlertLevel.Alert;
                Fx.PopText(e.Pos + new Vector2(0, -32), "ALERT", Pal.Foe, 18f);
            }
            else e.Alert = AlertLevel.Unaware;   // squad broke contact in time
        }
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
        foreach (var e in Enemies)
        {
            if (!e.Alive || e.Active || e.PodId != podId) continue;
            e.Alert = AlertLevel.Alert;
            any = true;
            // free scatter toward cover/line of fire (move only, no shot); 4.2 caps it to a
            // SINGLE move — no free dash on reveal (an immobile turret gets none).
            var plan = Ai.Plan(this, e);
            int cap = Math.Max(0, e.Mobility) * 2, spent = 0, lx = e.X, ly = e.Y;
            foreach (var (px, py) in plan.Path)
            {
                int step = (px != lx && py != ly) ? 3 : 2;   // diag costs 3, ortho 2 (matches CostMap)
                if (spent + step > cap) break;
                spent += step; lx = px; ly = py;
                Enqueue(new MoveStepAnim(e, px, py), Team.Enemy);
            }
        }
        if (any)
        {
            BannerText = "CONTACT!"; BannerEnemy = true; BannerMax = BannerTimer = 1.0f;
            Fx.AddShake(3f);
            Audio.Play("over");
        }
    }

    /// 4.4: break squad concealment. The first aggressive action (or stepping too close)
    /// springs the ambush: the breaking shot gets the ambush bonus (FiredFromConcealment),
    /// and any pod already in sight wakes with the usual capped scatter. After this, the
    /// normal 4.3 alert-tier rules resume for the rest of the mission. Call BEFORE the
    /// action mutates state so the bonus is in place when Combat.Resolve reads it.
    public void BreakConcealment(Unit actor = null)
    {
        if (!SquadConcealed) return;
        SquadConcealed = false;
        if (actor != null) actor.FiredFromConcealment = true;   // only a deliberate first shot earns the bonus
        ShowBanner("AMBUSH!", false);
        Fx.AddShake(4f);
        Audio.Play("turn");
        // wake every pod a soldier can currently see (each pod activates once)
        var seen = new HashSet<int>();
        foreach (var e in Enemies)
            if (e.Alive && !e.Active && e.PodId >= 0 && ClosestSightedDist(e) >= 0) seen.Add(e.PodId);
        foreach (int pid in seen) ActivatePod(pid);
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
        if (AutoPlay) { if (SmartPlay) SmartStep(); else AutoStep(); return; }

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
            if (next != null && (Selected == null || !Selected.CanAct)) Selected = next ?? Selected;
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
        else { MoveCost = null; _cameFrom = null; Threat = null; }
    }

    // Mark each reachable tile (and the current one) that a live, active enemy could
    // fire on with no cover for the mover — i.e. tiles you'd be exposed standing on.
    void ComputeThreat()
    {
        if (!ShowThreatPref) { Threat = null; return; }   // disabled in settings
        Threat = new bool[Grid.W, Grid.H];
        var foes = Enemies.Where(e => e.Alive && e.Active && e.Ammo > 0).ToList();
        if (foes.Count == 0) return;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                bool here = x == Selected.X && y == Selected.Y;
                if (!here && MoveCost[x, y] <= 0) continue;
                foreach (var e in foes)
                {
                    if (Util.TileDist(x, y, e.X, e.Y) > e.Weapon.MaxRange) continue;
                    if (!Grid.HasLineOfSight(e.X, e.Y, x, y)) continue;
                    if (Grid.GetCover(x, y, e.X, e.Y).Level == 0) { Threat[x, y] = true; break; }
                }
            }
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
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) DoHunker();
        if (Raylib.IsKeyPressed(KeyboardKey.Four)) ToggleGrenade();
        if (Raylib.IsKeyPressed(KeyboardKey.Five)) DoAbility();
        if (Raylib.IsKeyPressed(KeyboardKey.Six)) ToggleItem();
        if (Raylib.IsKeyPressed(KeyboardKey.Eight)) ToggleShove();
        if (Raylib.IsKeyPressed(KeyboardKey.Seven)) ToggleDrag();
        if (Raylib.IsKeyPressed(KeyboardKey.Nine)) ToggleVault();
        if (Raylib.IsKeyPressed(KeyboardKey.H)) DoHack();
        if (Raylib.IsKeyPressed(KeyboardKey.X)) DoExtract();
        if (Raylib.IsKeyPressed(KeyboardKey.R)) DoReload();
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

    void HandlePauseMenu()
    {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var m = Raylib.GetMousePosition();
        if (Raylib.CheckCollisionPointRec(m, Hud.PauseResume)) Paused = false;
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseFullscreen)) Display.ToggleFullscreen();
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseWindow)) Display.CycleSize();
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseMute)) Audio.ToggleMute();
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseShake)) Fx.ShakeOn = !Fx.ShakeOn;
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseThreat)) ShowThreatPref = !ShowThreatPref;
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseBright)) Display.CycleBrightness();
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseColorblind)) Display.ToggleColorblind();
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseAutoCam)) { Display.ToggleAutoCam(); if (!Display.AutoCam) { CamZoom = 1f; CamPan = Vector2.Zero; } }
        else if (Raylib.CheckCollisionPointRec(m, Hud.PauseAbandon)) { Paused = false; Phase = Phase.Lose; LoseTitle = "RUN ABANDONED"; LoseReason = "You called off the campaign."; Audio.Play("lose"); }
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
            case "hunker": DoHunker(); break;
            case "hack": DoHack(); break;
            case "extract": DoExtract(); break;
            case "reload": DoReload(); break;
        }
    }

    void SelectUnit(Unit u) { Selected = u; AimMode = false; SnapShot = false; GrenadeMode = false; ItemMode = false; ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false; Audio.Play("select"); }

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
        // shoving a dormant pod is aggression -> it wakes (mirrors a shot revealing a pod).
        if (!target.Active) ActivatePod(target.PodId);
        Enqueue(new ShoveAnim(u, target, dx, dy), Team.Player);
        ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false;
    }

    // ---- DRAG (FIELD CRAFT W1, universal): pull an adjacent ALLY one tile toward you ----
    /// Can the selected soldier DRAG right now? Needs an action, no drag spent this turn, and at
    /// least one adjacent (Chebyshev==1) alive friendly with a legal landing tile (one step toward us).
    public bool CanDrag(Unit u)
    {
        if (u == null || u.Team != Team.Player || !u.CanAct || u.ActionsLeft < 1 || u.DraggedThisTurn) return false;
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
        if (u == null || ally == null || !u.CanAct || u.ActionsLeft < 1 || u.DraggedThisTurn) return false;
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
        u.DraggedThisTurn = true;                         // one drag per soldier per turn (anti-loop)
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
        if (u == null || u.Team != Team.Player || !u.CanAct || u.ActionsLeft < 1 || u.VaultedThisTurn) return false;
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
        if (u == null || !u.CanAct || u.ActionsLeft < 1 || u.VaultedThisTurn) return false;
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
        u.VaultedThisTurn = true;                         // one vault per soldier per turn (anti-loop)
        Fx.PopText(u.Pos + new Vector2(0, -32), "VAULT", Pal.Good, 17f);
        Fx.Burst(u.Pos, Pal.Good, 8, 110f, 0.35f, 2.5f);
        Audio.Play("move");
        Enqueue(new MoveStepAnim(u, tx, ty), Team.Player);
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
        AimMode = false;
        PathPreview.Clear();
        Audio.Play("move");
        _tutMoved = true;
    }

    void IssueShoot(Unit target)
    {
        if (Selected == null || !Selected.CanAct || Selected.Ammo <= 0) return;
        if (!CanTarget(Selected, target)) return;
        if (SquadConcealed) BreakConcealment(Selected);  // 4.4: the ambush shot springs the trap
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
            Selected.ActionsLeft = Math.Max(0, Selected.ActionsLeft - 1);
        }
        var res = Combat.Resolve(Grid, Selected, target, aimMod);
        Selected.Steady = false;                         // braced shot consumed
        Selected.FiredFromConcealment = false;           // ambush bonus is for this one shot only
        Enqueue(new ShotAnim(Selected, target, res), Team.Player);
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
        else                 { Selected.FiredThisTurn = true; Selected.ActionsLeft = Math.Max(0, Selected.ActionsLeft - 1); }
        Selected.Steady = false;
        Selected.FiredFromConcealment = false;
        Enqueue(new BarrelShotAnim(Selected, bx, by), Team.Player);
        AimMode = false; SnapShot = false; _tutShot = true;
    }

    void DoOverwatch()
    {
        if (Selected == null || !Selected.CanAct || Selected.Ammo <= 0) return;
        if (Selected.HasStatus(StatusKind.Disoriented))
        { Fx.PopText(Selected.Pos + new Vector2(0, -30), "DISORIENTED", Pal.Foe, 16f); return; }
        Selected.OnOverwatch = true;
        Selected.ActionsLeft = 0;
        Fx.PopText(Selected.Pos + new Vector2(0, -30), "OVERWATCH", Pal.Accent, 18f);
        Audio.Play("over");
        AimMode = false;
        _tutOver = true;
    }

    void DoHunker()
    {
        if (Selected == null || !Selected.CanAct) return;
        Selected.Hunkered = true;
        Selected.ActionsLeft = 0;
        Fx.PopText(Selected.Pos + new Vector2(0, -30), "HUNKERED", Pal.Good, 18f);
        Audio.Play("hunker");
        AimMode = false;
    }

    void DoHack()
    {
        if (!CanHack(Selected)) return;
        Selected.ActionsLeft -= 1;
        AimMode = false;
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
        cand.X = dest.Value.x; cand.Y = dest.Value.y; cand.SyncPos();
        // feel: a quick haul-aboard flash on both soldier + asset
        Fx.Burst(cand.Pos, cand.IsVip ? Pal.VipGold : Pal.Friend, 16, 220f, 0.5f, 4f, true);
        Fx.PopText(cand.Pos + new Vector2(0, -28), "EXTRACTED", cand.IsVip ? Pal.VipGold : Pal.Friend, 22f);
        Fx.AddShake(3f);
        Audio.Play("select");
        CheckEnd();                                 // pulling the last unit in can win outright
    }

    void DoReload()
    {
        if (Selected == null || !Selected.CanAct || Selected.Ammo >= Selected.Weapon.Clip) return;
        Selected.Ammo = Selected.Weapon.Clip;
        Selected.ActionsLeft -= 1;
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
                Fx.PopText(at, "SLIPSTREAM", Pal.Accent, 18f);
                Fx.Burst(u.Pos, Pal.Accent, 12, 150f, 0.4f, 3f);
                Audio.Play("reload");
                break;
            case AbilityKind.Blitz:
                u.Blitz = true; u.AbilityCd = Unit.AbilityCooldownFor(u.Ability);
                Fx.PopText(at, "BLITZ", Pal.Accent, 18f);
                Fx.Burst(u.Pos, Pal.Accent, 10, 120f, 0.4f, 3f);
                Audio.Play("reload");
                break;
            case AbilityKind.Steady:
                u.Steady = true; u.AbilityCd = Unit.AbilityCooldownFor(u.Ability); u.ActionsLeft -= 1;
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
                int healed = Math.Min(baseHeal, ally.MaxHp - ally.Hp);
                if (healed <= 0) return;
                ally.Hp += healed;
                u.AbilityCd = Unit.AbilityCooldownFor(u.Ability); u.ActionsLeft -= 1;     // patching costs one action (like STEADY)
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
    void SpawnDefendWave()
    {
        if (_turnCount % 2 == 0 || _turnCount >= DefendTurns) return;  // waves on odd turns, not the last
        SpawnReinforcements(2 + _run.Mission / 2, 12, "WAVE");
    }

    /// Shared reinforcement spawner: drops up to `want` active wave-hostiles in from the right
    /// board edge (already engaged), honoring a live-enemy `cap`. Used by both the DEFEND objective
    /// and the anti-turtle PRESSURE CLOCK. Returns how many it actually added.
    int SpawnReinforcements(int want, int cap, string label)
    {
        if (AliveEnemies().Count >= cap) return 0;                     // clutter cap
        int n = _run.Mission;
        var rows = Enumerable.Range(0, Grid.H).OrderBy(_ => Util.RandF()).ToList();
        int added = 0;
        foreach (int y in rows)
        {
            if (added >= want || AliveEnemies().Count >= cap) break;
            int x = Grid.W - 2;
            if (!Grid.IsFloor(x, y) || IsOccupiedByOther(x, y, null))
            {
                x = Grid.W - 1;
                if (!Grid.IsFloor(x, y) || IsOccupiedByOther(x, y, null)) continue;
            }
            var e = Mission.MakeWaveHostile(n, x, y);
            e.Alert = AlertLevel.Alert; e.PodId = -1;   // reinforcements arrive already engaged
            e.SyncPos();
            Enemies.Add(e);
            Fx.Burst(e.Pos, Pal.Foe, 14, 160f, 0.5f, 3f, true);
            added++;
        }
        if (added > 0) { Fx.PopText(Util.TileCenter(Grid.W - 2, 0) + new Vector2(0, -10), label, Pal.Foe, 20f); Audio.Play("turn"); RefreshCombatRoster(); }
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
        // Reinforcements kick in from rung 2 onward, once per fresh rung (not every turn) so the
        // board doesn't flood: a small wave that scales with the rung. One per rung-up event.
        if (rung >= 2 && rung > _pressureWaves)
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
    void FaceShields()
    {
        foreach (var e in Enemies)
        {
            if (!e.Alive || e.Cls != "SHIELD") continue;
            var p = AlivePlayers().OrderBy(q => Util.ChebyDist(e.X, e.Y, q.X, q.Y)).FirstOrDefault();
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
        if (TutStep >= 0 && TutStep < 3) AdvanceTutorial();
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
        foreach (var e in Enemies) if (e.Alive) { e.BeginTurn(); TickStatuses(e); }
        _aiUnits = AliveEnemies().Where(e => e.Active).ToList();  // dormant/suspicious pods don't act
        PlanEnemySquad();                                        // shared focus + overwatch map (advisory)
        _aiIdx = 0;
        _aiStage = AiStage.PickNext;
        _aiPlan = null;
        ClearIntent();
        ShowBanner("ENEMY TURN", true);
        Enqueue(new WaitAnim(0.5f), Team.Enemy);
    }

    void StartPlayerTurn()
    {
        _turnCount++;
        Phase = Phase.PlayerTurn;
        ClearIntent();                    // no enemy intent lingers into the player's turn
        Grid.TickSmoke();                 // smoke clouds decay one turn per round
        TickHazards();                    // fire cooks off barrels + reignites units, then decays
        _refundedThisTurn.Clear();        // flank-kill refund is one per soldier per turn
        HackedThisTurn = false;           // the terminal accepts one breach cycle per turn (hold)
        ClearMarks();                     // a sharpshooter's MARK lasts until the marker's next turn
        ClearPins();                      // a gunner's SUPPRESSING FIRE pin lasts through one enemy turn, then lifts
        UpdateHvtGuard();                 // DECAPITATE: refresh the HVT's guarded state at the boundary (a guard may have moved)
        if (AutoPlay) AutoStallCheck();
        foreach (var p in Players) if (p.Alive) { p.BeginTurn(); TickStatuses(p); }
        foreach (var e in Enemies) if (e.Alive) { e.ReactedThisTurn = false; e.Suppress = 0; } // OW resets; suppression expires
        Selected = Players.FirstOrDefault(p => p.CanAct);
        AimMode = false;
        SnapShot = false;
        GrenadeMode = false;
        ItemMode = false;
        ShoveMode = false; MarkMode = false; GrappleMode = false; PinMode = false; DragMode = false; VaultMode = false;
        ShowBanner("PLAYER TURN", false);
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
                        PlayerOverwatchTiles.Add((x, y));
                        break;   // one watcher is enough to mark the tile threatened
                    }
                }
        }
    }

    // ---------------- enemy turn ----------------
    void UpdateEnemy()
    {
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
            if (e.Alive)
            {
                if (_aiPlan.SiegeCharge != null && e.Cls == "BOMBARD" && e.ChargeTurns == 0 && e.ActionsLeft > 0)
                {
                    // SIEGE charges a telegraphed strike: NO damage now — the 3x3 danger zone IS the
                    // telegraph (drawn for the whole next player turn); it lands at the top of the
                    // following enemy turn (TickSiegeStrikes). Spends the action -> no dead turn.
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
                    e.ActionsLeft = 0;
                    var (sx, sy) = _aiPlan.SapTile.Value;
                    Fx.PopText(e.Pos + new Vector2(0, -30), "BREACH", Pal.Foe, 16f);
                    var hit = Grid.DamageCover(sx, sy, Grid.HighCoverHp);  // demolish a full level
                    if (hit != Grid.CoverHit.None) CoverHitFx(sx, sy, hit);
                    Fx.AddShake(5f);
                    Enqueue(new WaitAnim(0.25f), Team.Enemy);
                }
                else if (_aiPlan.HealTarget != null && _aiPlan.HealTarget.Alive && e.ActionsLeft > 0 &&
                    _aiPlan.HealTarget.Hp < _aiPlan.HealTarget.MaxHp &&
                    Util.TileDist(e.X, e.Y, _aiPlan.HealTarget.X, _aiPlan.HealTarget.Y) <= Ai.HealRange &&
                    Grid.HasLineOfSight(e.X, e.Y, _aiPlan.HealTarget.X, _aiPlan.HealTarget.Y))
                {
                    e.ActionsLeft = 0;
                    Fx.PopText(e.Pos + new Vector2(0, -30), "MEDIC", Pal.Good, 16f);
                    Enqueue(new WaitAnim(0.2f), Team.Enemy);
                    Enqueue(new HealAnim(e, _aiPlan.HealTarget), Team.Enemy);
                }
                else if (_aiPlan.Grenade && e.Grenades > 0 && e.ActionsLeft > 0 &&
                    Util.TileDist(e.X, e.Y, _aiPlan.GrenX, _aiPlan.GrenY) <= GrenadeRange)
                {
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
                    // AI SHOVE (Wave 5): slam an adjacent covered soldier 1 tile to expose it (or deal
                    // collision damage if it's pinned). Reuses the player's ShoveAnim verbatim; the action
                    // is spent here (no TIMEOUT). The exposed soldier is then a soft target for the pod.
                    e.ActionsLeft = 0;
                    var t = _aiPlan.ShoveTarget;
                    int sdx = Math.Sign(t.X - e.X), sdy = Math.Sign(t.Y - e.Y);
                    Fx.PopText(e.Pos + new Vector2(0, -30), "SHOVE", Pal.Foe, 16f);
                    Enqueue(new WaitAnim(0.15f), Team.Enemy);
                    Enqueue(new ShoveAnim(e, t, sdx, sdy), Team.Enemy);
                }
                else if (_aiPlan.ShootTarget != null && _aiPlan.ShootTarget.Alive &&
                    e.ActionsLeft > 0 && e.Ammo > 0 && CanTarget(e, _aiPlan.ShootTarget))
                {
                    e.Ammo--;
                    // TEMPO: the enemy shot is 1 action and does NOT end the turn (mirrors the player).
                    e.FiredThisTurn = true;
                    e.ActionsLeft = Math.Max(0, e.ActionsLeft - 1);
                    var res = Combat.Resolve(Grid, e, _aiPlan.ShootTarget);
                    Enqueue(new WaitAnim(0.2f), Team.Enemy);
                    Enqueue(new ShotAnim(e, _aiPlan.ShootTarget, res), Team.Enemy);
                    TryEnemyReposition(e);   // shoot-then-reposition: duck to cover / advance with the spare action
                }
                else if (_aiPlan.Overwatch && e.ActionsLeft > 0 && e.Ammo > 0 && !e.HasStatus(StatusKind.Disoriented))
                {
                    e.OnOverwatch = true; e.ActionsLeft = 0;
                    Fx.PopText(e.Pos + new Vector2(0, -30), "OVERWATCH", Pal.Accent, 16f);
                    Audio.Play("over");
                }
                else if (_aiPlan.Hunker && e.ActionsLeft > 0)
                {
                    e.Hunkered = true; e.ActionsLeft = 0;
                }
            }
            _aiIdx++;
            _aiStage = AiStage.PickNext;
        }
    }

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
        foreach (var (px, py) in path) Enqueue(new MoveStepAnim(e, px, py), Team.Enemy);
        Audio.Play("move");
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
                if (!p.Alive || p.Ammo <= 0) continue;
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
        foreach (var (px, py) in path) Enqueue(new MoveStepAnim(e, px, py), Team.Enemy);
        Audio.Play("move");
    }

    // Clear the enemy-intent telegraph (so it doesn't render past the unit's action or into the
    // player's turn). Called when the telegraph beat ends and at every turn boundary.
    void ClearIntent() { IntentUnit = null; IntentPlan = null; }

    // ---------------- barracks perk choice ----------------
    void ChoosePerk(int which)
    {
        if (_run.PendingPerks.Count == 0) return;
        var off = _run.PendingPerks[0];
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
        Stats.RecordPurchase(ShopName[item]);
        Audio.Play("select");
    }

    void HandleShopClick()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Enter)) { _shopDone = true; Audio.Play("turn"); return; }
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

        // ShopBtns are laid out by the Hud over the ROTATING OFFER (display order); map the clicked
        // slot back to its underlying item id before purchasing.
        var offer = ShopOffer();
        for (int i = 0; i < Hud.ShopBtns.Length && i < offer.Count; i++)
            if (Raylib.CheckCollisionPointRec(m, Hud.ShopBtns[i])) { DoPurchase(offer[i]); return; }
        if (Raylib.CheckCollisionPointRec(m, Hud.ShopProceed)) { _shopDone = true; Audio.Play("turn"); }
    }

    // autopilot: spend Intel as a varied, realistic economy so the BALANCE analytics reflect a real
    // player's spread rather than the old degenerate "buy armor to zero" loop. Each pass buys the
    // SINGLE most useful affordable item it can and STOPS when nothing useful/affordable remains:
    //   1) heal — if a soldier is hurt or wounded (cheap, high-value survivability),
    //   2) armor (PLATING) — if a soldier is still under the armor cap (compounding survivability),
    //   3) else the cheapest other beneficial item in the slate (weapon mod / frag / stims).
    // Perk pick (2, needs the chooser flow) and COUNTER-PREP (a situational call) are left to the
    // player. ALWAYS terminates: every DoPurchase that fires reduces Intel by a positive cost, and
    // the iteration cap bounds the loop defensively against any future zero-cost item.
    void AutoShop()
    {
        var offer = ShopOffer();        // buy only from this barracks' rotating slate

        for (int guard = 0; guard < 40; guard++)
        {
            int pick = -1;
            // 1) heal a hurt/wounded soldier first (only ever needed once per pass — CanBuy(0)
            //    goes false once everyone is topped up).
            if (offer.Contains(0) && CanBuy(0)) pick = 0;
            // 2) else top up armor while a soldier is under the cap and it's on this slate.
            else if (offer.Contains(4) && CanBuy(4)) pick = 4;
            else
            {
                // 3) else the cheapest other beneficial item in the slate (weapon mods / frag /
                //    stims). Skip the perk pick (2) and PREP (situational player calls).
                int bestCost = int.MaxValue;
                foreach (int i in offer)
                {
                    if (i == 0 || i == 2 || i == 4) continue;
                    if (IsPrepItem(i)) continue;
                    if (CanBuy(i) && ShopCostAt(i) < bestCost) { pick = i; bestCost = ShopCostAt(i); }
                }
            }
            if (pick < 0) break;        // nothing useful/affordable left
            int before = _run.Intel;
            DoPurchase(pick);
            if (_run.Intel >= before) break;   // safety: never spin on a no-op purchase
        }
        _shopDone = true;
    }

    /// Harness hook (screenshot only): mark a couple of soldiers wounded.
    public void DebugWound()
    {
        var c = Players.Where(p => !p.IsVip).ToList();
        if (c.Count > 0) c[0].Wound = 2;
        if (c.Count > 1) c[1].Wound = 1;
    }

    /// Harness hook (screenshot only): arm a shot tooltip on an enemy so the randomness-
    /// mitigation surfacing (DMG range + GRAZE floor + "+N STEADYING" streak badge) is visible.
    /// Seats a live enemy in clean LoS of the first soldier, banks a miss streak on that soldier,
    /// then enters aim mode locked on the enemy. The next Update's UpdateHoverAndAim recomputes
    /// + shows the odds naturally (no special draw path), so the screenshot matches real play.
    public void DebugTooltip()
    {
        var c = Players.Where(p => !p.IsVip && p.Alive).ToList();
        if (c.Count == 0) return;
        var s = c[0];
        s.ConsecutiveMisses = 2;                       // bank +12 STEADYING (the streak cap)
        SquadConcealed = false;                        // CanTarget refuses while concealed
        var foe = Enemies.FirstOrDefault(e => e.Alive);
        if (foe != null)
        {
            // Re-seat the foe a few tiles directly east of the soldier on clear floor so LoS holds.
            int fx = Math.Min(Grid.W - 1, s.X + 4), fy = s.Y;
            if (Grid.InBounds(fx, fy)) { foe.X = fx; foe.Y = fy; foe.SyncPos(); }
            foe.Alert = AlertLevel.Alert;
            Selected = s; RecomputeMoveCost();
            AimMode = true; AimTarget = foe;
        }
    }

    /// Harness hook (screenshot only): drop a soldier to show the KIA stamp + red
    /// death-flash (item 3.11).
    public void DebugKia()
    {
        var c = Players.Where(p => !p.IsVip).ToList();
        if (c.Count > 1) { var v = c[1]; v.Nickname = "GHOST"; v.Hp = 0; KillUnit(v); }
    }

    /// Harness hook (screenshot only, SIGHTLINE_SUMMARY): stage a finished run and jump to the
    /// VICTORY run-summary card so the rich payoff (surviving roster + MVP + KIA memorial +
    /// totals + confetti) can be inspected. Presentation only; never runs in normal play.
    /// Pass lose=true to view the RUN OVER variant instead.
    public void DebugSummary(bool lose = false)
    {
        if (_run == null || _run.Squad == null || _run.Squad.Count == 0) { _run = new Run(); _run.Start(); }
        _run.Intel = 86;
        _run.HeatLevel = 3;
        // decorate survivors: ranks, kills, a nickname/trait + an MVP.
        var squad = _run.Squad;
        for (int i = 0; i < squad.Count; i++)
        {
            var u = squad[i];
            u.Kills = 2 + i * 3;
            u.Rank = Math.Min(Run.Ranks.Length - 1, 1 + i);
        }
        if (squad.Count > 0) { squad[0].Nickname = "REAPER"; squad[0].Kills = 11; squad[0].Traits.Add(Trait.Killer); }
        if (squad.Count > 1) squad[1].Nickname = " HALO";
        // a couple of fallen, recorded across the run for the memorial roll.
        _run.Memorial.Add(new FallenRec { Name = "DALES \"BISHOP\"", Cls = "RANGER",  Rank = "SERGEANT", Kills = 7, Mission = 2 });
        _run.Memorial.Add(new FallenRec { Name = "OKONKWO",        Cls = "GUNNER",  Rank = "CORPORAL", Kills = 4, Mission = 4 });
        _run.Memorial.Add(new FallenRec { Name = "VEGA \"ASH\"",    Cls = "ASSAULT", Rank = "ROOKIE",   Kills = 1, Mission = 5 });
        _run.Mission = lose ? 5 : Run.MaxMissions;
        if (lose) { LoseTitle = "RUN OVER"; LoseReason = "The squad fell on mission 5."; }
        Phase = lose ? Phase.Lose : Phase.Win;
    }

    /// Harness hook (screenshot only): paint sample status effects on soldiers/foes so
    /// the on-unit status codes (BRN/BLD/STN/DAZ) can be verified (item 3.5).
    public void DebugStatus()
    {
        var c = Players.Where(p => !p.IsVip).ToList();
        if (c.Count > 0) c[0].AddStatus(StatusKind.Burning, 2);
        if (c.Count > 1) c[1].AddStatus(StatusKind.Bleed, 2);
        if (c.Count > 2) c[2].AddStatus(StatusKind.Stun, 1);
        if (c.Count > 3) c[3].AddStatus(StatusKind.Disoriented, 2);
        foreach (var e in Enemies) { e.Alert = AlertLevel.Alert; if (e.Alive) { e.AddStatus(StatusKind.Burning, 2); break; } }
    }

    /// Harness hook (screenshot only): a decorated veteran (nickname/traits/bond) in
    /// the barracks promotion screen, to verify the dossier surfaces identity (3.2).
    public void DebugTraits()
    {
        if (_run.Squad.Count > 0)
        {
            var u = _run.Squad[0];
            u.Nickname = "REAPER";
            u.Traits.Add(Trait.Killer); u.Traits.Add(Trait.ColdBlood);
            if (_run.Squad.Count > 1) { u.Bonds.Add(_run.Squad[1].Name); _run.Squad[1].Bonds.Add(u.Name); }
            u.Kills = 3;                        // force a rank-up so the perk chooser opens
        }
        _run.DebriefSurvivors();
        Phase = Phase.Barracks;
    }

    /// Harness hook (screenshot only): open the custom-tag editor on a soldier.
    public void DebugTagEditor()
    {
        TagTarget = Players.FirstOrDefault(p => !p.IsVip) ?? (_run.Squad.Count > 0 ? _run.Squad[0] : null);
        if (TagTarget == null) return;
        TagBuffer = "BREACHER";
        EditingTag = true;
    }

    /// Harness hook (screenshot only): show the barracks requisition shop.
    public void DebugShop()
    {
        if (_run.Squad.Count == 0) { _run = new Run(); _run.Start(); }
        _run.Intel = 24;
        _run.DebriefSurvivors();
        if (_run.Squad.Count > 0) _run.Squad[0].Hp = Math.Max(1, _run.Squad[0].Hp - 4);  // wound for the medkit demo
        _run.Report.Insert(0, $"Recovered 17 intel  (total {_run.Intel})");
        _shopDone = false;
        RefreshShopOffer();
        Phase = Phase.Barracks;
    }

    /// Harness hook (screenshot, SIGHTLINE_PREP): the REQUISITION screen with a faction
    /// telegraphed on the next node so the COUNTER-PREP item is offered (and buyable).
    public void DebugPrep()
    {
        DebugShop();
        _run.Intel = 40;
        // force a faction onto a reachable next node so UpcomingFaction() returns it
        var next = _run.NextNodes();
        if (next.Count > 0) next[0].Faction = Faction.Wardens;
        RefreshShopOffer();   // re-roll now that a faction is telegraphed, so the PREP slot shows
    }

    /// Harness hook (screenshot): the REQUISITION screen with the ARMORY sub-panel open,
    /// a soldier selected so the weapon picker shows.
    public void DebugArmory()
    {
        DebugShop();
        _run.Intel = 40;
        ArmoryMode = true;
        ArmorySoldier = _run.Squad.FirstOrDefault(u => !u.IsVip);
    }

    /// Headless self-test (SIGHTLINE_DEATHTEST): kill the whole squad on an escort
    /// mission (VIP solos to evac) and confirm the dead, leveled soldiers do NOT carry
    /// into the next mission. Returns a one-line report.
    public string DeathConsequenceTest()
    {
        NoPersist = true;
        _run = new Run(); _run.Start();
        // give one soldier a rank/perk so we'd notice if a "leveled" unit survived death
        _run.Squad[0].Rank = 2; _run.Squad[0].Perks.Add(Perk.Deadeye);
        var before = _run.Squad.Select(u => u.Name).ToList();
        _run.CurrentCard = new MissionCard { Objective = Objective.Escort, ModName = "STANDARD", Reward = RewardKind.None };
        SetupMission(1);
        foreach (var u in Players.Where(p => !p.IsVip).ToList()) { u.Hp = 0; KillUnit(u); }
        int stillAlive = AlivePlayers().Count(p => !p.IsVip);
        Vip.X = EvacZone[0].x; Vip.Y = EvacZone[0].y;     // VIP reaches extraction -> escort win
        CheckEnd();                                        // -> EnterBarracks
        var after = _run.Squad.Select(u => u.Name).ToList();
        int carried = after.Count(n => before.Contains(n));
        string verdict = (stillAlive == 0 && carried == 0) ? "PASS" : "FAIL";
        return $"DEATHTEST: {verdict} | soldiersAliveAfterKill={stillAlive} phase={Phase} " +
               $"fallen={_run.Fallen.Count} before=[{string.Join(",", before)}] after=[{string.Join(",", after)}]";
    }

    /// Headless self-test (SIGHTLINE_STATUSTEST): status effects tick, decay, and read
    /// correctly — burning/bleed DoT, stun (lose an action), disoriented (aim + no
    /// overwatch). Needs a tiny window (Game uses tile math). Returns a one-line report.
    public string StatusSelfTest()
    {
        NoPersist = true;
        _run = new Run(); _run.Start();
        _run.CurrentCard = new MissionCard { Objective = Objective.Eliminate, ModName = "STANDARD", Reward = RewardKind.None };
        SetupMission(1);
        var fails = new List<string>();
        var u = Players.First(p => !p.IsVip);

        // (1) Burning DoT applies at turn start, twice, then expires
        u.Hp = u.MaxHp; int hp0 = u.Hp;
        u.AddStatus(StatusKind.Burning, 2);
        u.BeginTurn(); TickStatuses(u);
        if (u.Hp != hp0 - Unit.BurnDamage) fails.Add("burnDmg");
        if (!u.HasStatus(StatusKind.Burning)) fails.Add("burnPersist");
        u.BeginTurn(); TickStatuses(u);
        if (u.HasStatus(StatusKind.Burning)) fails.Add("burnExpire");
        if (u.Hp != hp0 - 2 * Unit.BurnDamage) fails.Add("burnDmg2");

        // (2) Stun costs one action, then expires
        u.AddStatus(StatusKind.Stun, 1);
        u.BeginTurn(); TickStatuses(u);
        if (u.ActionsLeft != 1) fails.Add("stunAction");
        u.BeginTurn(); TickStatuses(u);
        if (u.ActionsLeft != 2) fails.Add("stunExpire");

        // (3) Disoriented dulls aim and blocks overwatch (clear cover so the delta is clean)
        var d = Enemies.First();
        u.X = 5; u.Y = 5; d.X = 9; d.Y = 5;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (Grid.InBounds(d.X + dx, d.Y + dy)) { Grid.Tiles[d.X + dx, d.Y + dy] = TileType.Floor; Grid.Height[d.X + dx, d.Y + dy] = 0; }
        Grid.Height[u.X, u.Y] = 0;
        int baseHit = Combat.ComputeOdds(Grid, u, d).HitChance;
        u.AddStatus(StatusKind.Disoriented, 2);
        int dazHit = Combat.ComputeOdds(Grid, u, d).HitChance;
        if (baseHit - dazHit != Unit.DisorientAim) fails.Add("dazAim");
        Selected = u; u.ActionsLeft = 2; u.OnOverwatch = false; u.SyncPos();
        DoOverwatch();
        if (u.OnOverwatch) fails.Add("dazOverwatch");

        // (4) Bleed costs HP on each step
        u.Hp = u.MaxHp; int bhp = u.Hp;
        u.AddStatus(StatusKind.Bleed, 2);
        OnUnitEnteredTile(u);
        if (u.Hp != bhp - Unit.BleedDamage) fails.Add("bleedMove");

        return fails.Count == 0
            ? "STATUSTEST: PASS (burn/bleed DoT, stun, disorient aim+overwatch all hold)"
            : "STATUSTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Headless self-test (SIGHTLINE_SNAPTEST): the TEMPO per-turn action economy —
    ///   (1) the first aimed shot costs exactly 1 action and does NOT end the turn, setting FiredThisTurn;
    ///   (1b) a SECOND shot the same turn is allowed (a rushed follow-up — costs 1 action + ammo);
    ///   (1c) RUN&GUN gives a FREE bonus shot (doesn't consume the turn's full shot);
    ///   (2) a player flank-kill refunds +1 action AND re-enables firing, ONCE per soldier per turn
    ///       (a second flank-kill the same turn grants nothing); a COVERED kill refunds nothing.
    /// Drives the real IssueShoot / TryFlankKillRefund paths on a controlled open field.
    public string SnapRefundSelfTest()
    {
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();

        // ---- controlled scene: empty open field, perfect LoS, no concealment ----
        Grid = new Grid();
        Players = new System.Collections.Generic.List<Unit>();
        Enemies = new System.Collections.Generic.List<Unit>();
        Vip = null; CaptiveLocked = false; SquadConcealed = false;
        Objective = Objective.Eliminate; EvacZone.Clear();
        Phase = Phase.PlayerTurn;
        _refundedThisTurn.Clear();

        Unit MkP(int x, int y) {
            var u = new Unit { Name = "SOLDIER", Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                               Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
        }
        Unit MkE(int x, int y, int hp) {
            var u = new Unit { Name = "FOE", Cls = "GRUNT", Team = Team.Enemy, X = x, Y = y,
                               Hp = hp, MaxHp = 6, Aim = 60, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.Alert = AlertLevel.Alert; u.SyncPos(); u.BeginTurn(); return u;
        }

        // ---- 1. TEMPO: an aimed shot costs 1 action, does NOT end the turn, sets FiredThisTurn ----
        var shooter = MkP(5, 5);
        var dummy   = MkE(9, 5, 6);            // full HP so the shot can't kill (isolates the action cost)
        Players.Add(shooter); Enemies.Add(dummy);
        Selected = shooter;
        IssueShoot(dummy);
        if (shooter.ActionsLeft != 1) fails.Add($"shotCost={shooter.ActionsLeft}(want1)");
        if (!shooter.FiredThisTurn) fails.Add("firedFlagNotSet");
        if (AimMode) fails.Add("shotLeftAimModeOn");
        _anims.Clear();                         // discard the queued ShotAnim; we don't pump frames here

        // ---- 1b. a SECOND shot the same turn is allowed (a rushed follow-up: costs 1 action + ammo) ----
        int ammoBefore = shooter.Ammo;
        IssueShoot(dummy);
        if (shooter.ActionsLeft != 0) fails.Add($"secondShotAction={shooter.ActionsLeft}(want0)");
        if (shooter.Ammo != ammoBefore - 1) fails.Add("secondShotNoAmmoSpent");
        _anims.Clear();

        // ---- 1c. RUN&GUN gives a FREE bonus shot (doesn't consume the turn's full shot) ----
        var rg = MkP(2, 2);
        Players.Add(rg);
        Selected = rg; rg.RunGun = true;
        IssueShoot(dummy);
        if (rg.ActionsLeft != 1) fails.Add($"rungunCost={rg.ActionsLeft}(want1)");
        if (rg.RunGun) fails.Add("rungunNotConsumed");
        if (rg.FiredThisTurn) fails.Add("rungunSetFiredFlag");   // the bonus shot leaves the full shot available
        _anims.Clear();

        // ---- 2. flank-kill refund: +1 action AND re-enables firing, once per soldier per turn ----
        // Build a flanked shot result and stage its ShotAnim as the ACTIVE anim, exactly as the
        // live kill path does (KillUnit -> TryFlankKillRefund reads ActiveAnim's ShotResult).
        var killer = MkP(5, 9);
        var victim = MkE(7, 9, 1);
        Players.Add(killer); Enemies.Add(victim);
        killer.ActionsLeft = 1; killer.FiredThisTurn = true;   // as if a flank-shot just fired (1 action, no end-turn)
        var flankRes = new ShotResult { Hit = true, Damage = 5, Odds = new ShotOdds { Flanked = true, CoverLevel = 0 } };
        _anims.Clear(); _anims.Add(new ShotAnim(killer, victim, flankRes));   // active anim = this shot
        _refundedThisTurn.Clear();
        victim.Alive = false;                   // the victim has just been downed
        TryFlankKillRefund(victim);
        if (killer.ActionsLeft != 2) fails.Add($"refund1={killer.ActionsLeft}(want2)");
        if (killer.FiredThisTurn) fails.Add("refundDidNotReEnableFire");   // a kill-refund clears FiredThisTurn
        TryFlankKillRefund(victim);             // second flank-kill same turn -> capped, no extra
        if (killer.ActionsLeft != 2) fails.Add($"refundCap={killer.ActionsLeft}(want2)");

        // ---- 2b. a COVERED kill refunds nothing ----
        var killer2 = MkP(3, 9);
        var victim2 = MkE(1, 9, 1);
        Players.Add(killer2); Enemies.Add(victim2);
        killer2.ActionsLeft = 0;
        var coverRes = new ShotResult { Hit = true, Damage = 5, Odds = new ShotOdds { Flanked = false, CoverLevel = 2 } };
        _anims.Clear(); _anims.Add(new ShotAnim(killer2, victim2, coverRes));
        _refundedThisTurn.Clear();
        victim2.Alive = false;
        TryFlankKillRefund(victim2);
        if (killer2.ActionsLeft != 0) fails.Add($"coveredRefunded={killer2.ActionsLeft}(want0)");
        _anims.Clear();

        return fails.Count == 0
            ? "SNAPTEST: PASS (shot=1 action/no-end-turn, rushed 2nd shot, RUN&GUN free bonus, flank-kill refunds+re-enables once/turn, covered kill refunds nothing)"
            : "SNAPTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Headless self-test (SIGHTLINE_CDTEST): renewable signature-ability COOLDOWN.
    /// (a) fresh soldiers AbilityReady; (b) a Corpsman Heal sets Cd 3 and CanAbility -> false;
    /// (c) 3x BeginTurn ticks 3->0 and CanAbility -> true again; (d) per-kind wiring: a
    /// Sharpshooter Mark -> Cd 2 and a Gunner Pin -> Cd 2. Deterministic; tiny scene (no run).
    public string CdSelfTest()
    {
        NoPersist = true;
        var fails = new List<string>();

        // open arena so LoS / targeting always succeed
        Grid = new Grid();
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++) Grid.Tiles[x, y] = TileType.Floor;
        Players = new List<Unit>();
        Enemies = new List<Unit>();

        // (a) AbilityCooldownFor wiring is per-kind
        if (Unit.AbilityCooldownFor(AbilityKind.Heal) != 3) fails.Add("cdHeal!=3");
        if (Unit.AbilityCooldownFor(AbilityKind.Slipstream) != 3) fails.Add("cdSlip!=3");
        if (Unit.AbilityCooldownFor(AbilityKind.Mark) != 2) fails.Add("cdMark!=2");
        if (Unit.AbilityCooldownFor(AbilityKind.Pin) != 2) fails.Add("cdPin!=2");
        if (Unit.AbilityCooldownFor(AbilityKind.None) != 0) fails.Add("cdNone!=0");

        // helper to make a fresh, ready player unit at (x,y)
        Unit Make(string cls, int x, int y) {
            var u = new Unit { Name = cls, Cls = cls, Team = Team.Player, Hp = 10, MaxHp = 10,
                               Aim = 70, Mobility = 6, Weapon = Weapon.Make(WeaponKind.Rifle), X = x, Y = y };
            u.Ammo = u.Weapon.Clip; u.AbilityCd = 0; u.ActionsLeft = 2; u.SyncPos();
            return u;
        }

        // --- (b)+(c) Corpsman Heal: Cd 3, gate off, ticks back to ready ---
        var medic = Make("CORPSMAN", 5, 5);
        var hurt  = Make("ASSAULT", 6, 5); hurt.Hp = 3;   // adjacent wounded ally
        Players.Add(medic); Players.Add(hurt);
        if (!medic.AbilityReady) fails.Add("medicNotReadyAtStart");
        if (!CanAbility(medic)) fails.Add("medicCannotHealReady");
        Selected = medic;
        DoAbility();                                       // routes to the Heal case -> spends cooldown
        if (medic.AbilityCd != 3) fails.Add("healCd!=3(" + medic.AbilityCd + ")");
        if (CanAbility(medic)) fails.Add("healStillUsableOnCd");
        // re-wound the ally so a heal target persists, then cool down 3 of the medic's turns
        hurt.Hp = 3;
        for (int i = 0; i < 3; i++) medic.BeginTurn();     // BeginTurn ticks Cd 3->2->1->0 (also refills ActionsLeft)
        if (medic.AbilityCd != 0) fails.Add("healCdNotTickedTo0(" + medic.AbilityCd + ")");
        if (!medic.AbilityReady) fails.Add("medicNotReadyAfterCooldown");
        if (!CanAbility(medic)) fails.Add("medicCannotHealAfterCooldown");

        // --- (d) Sharpshooter Mark -> Cd 2 ---
        var sniper = Make("SHARPSHOOTER", 2, 2);
        Players.Add(sniper);
        var foe1 = new Unit { Name = "G", Cls = "GRUNT", Team = Team.Enemy, Hp = 6, MaxHp = 6,
                              Aim = 50, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle), X = 4, Y = 2, Alert = AlertLevel.Alert };
        foe1.SyncPos(); Enemies.Add(foe1);
        if (sniper.Ability != AbilityKind.Mark) fails.Add("sniperWrongAbility");
        if (!CanAbility(sniper)) fails.Add("sniperCannotMarkReady");
        IssueMark(sniper, foe1);
        if (sniper.AbilityCd != 2) fails.Add("markCd!=2(" + sniper.AbilityCd + ")");
        if (CanAbility(sniper)) fails.Add("markStillUsableOnCd");

        // --- (d) Gunner Pin -> Cd 2 ---
        var gunner = Make("GUNNER", 8, 8); gunner.Ammo = Math.Max(gunner.Ammo, 1);
        Players.Add(gunner);
        var foe2 = new Unit { Name = "S", Cls = "SCOUT", Team = Team.Enemy, Hp = 5, MaxHp = 5,
                              Aim = 50, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Smg), X = 9, Y = 8, Alert = AlertLevel.Alert };
        foe2.SyncPos(); Enemies.Add(foe2);
        if (gunner.Ability != AbilityKind.Pin) fails.Add("gunnerWrongAbility");
        if (!CanAbility(gunner)) fails.Add("gunnerCannotPinReady");
        IssuePin(gunner, foe2);
        if (gunner.AbilityCd != 2) fails.Add("pinCd!=2(" + gunner.AbilityCd + ")");

        return fails.Count == 0
            ? "CDTEST: PASS (fresh=ready; Heal Cd3 gates+ticks to ready; Mark Cd2; Pin Cd2)"
            : "CDTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Headless self-test (SIGHTLINE_ITEMTEST): the utility-item mechanics — smoke
    /// blocks + decays line of sight, a barricade lays cover, loadouts map per class.
    /// Window-free (grid + tile math only).
    public static string ItemSelfTest()
    {
        var fails = new List<string>();
        var grid = new Grid();
        for (int x = 0; x < grid.W; x++)
            for (int y = 0; y < grid.H; y++) grid.Tiles[x, y] = TileType.Floor;

        // (1) clear LoS across an open lane
        if (!grid.HasLineOfSight(2, 5, 9, 5)) fails.Add("clearLoS");

        // (2) a smoke cloud blocks LoS through it, over a 3x3 footprint
        grid.AddSmoke(5, 5, SmokeAnim.Radius, SmokeAnim.Turns);
        if (grid.HasLineOfSight(2, 5, 9, 5)) fails.Add("smokeBlocks");
        if (!grid.IsSmoke(5, 5) || !grid.IsSmoke(5, 4) || !grid.IsSmoke(6, 6)) fails.Add("smokeArea");

        // (3) smoke decays one turn per tick; LoS returns once it clears
        for (int i = 0; i < SmokeAnim.Turns; i++) grid.TickSmoke();
        if (grid.IsSmoke(5, 5)) fails.Add("smokePersist");
        if (!grid.HasLineOfSight(2, 5, 9, 5)) fails.Add("smokeCleared");

        // (4) a barricade lays low cover on a floor tile
        grid.Tiles[6, 5] = TileType.LowCover;
        if (!grid.IsCover(6, 5)) fails.Add("barricadeCover");

        // (5) class -> item loadouts exercise all three kinds
        if (Unit.ItemKindFor("RANGER") != ItemKind.Smoke) fails.Add("mapSmoke");
        if (Unit.ItemKindFor("ASSAULT") != ItemKind.Flash) fails.Add("mapFlash");
        if (Unit.ItemKindFor("GUNNER") != ItemKind.Barricade) fails.Add("mapBarricade");

        return fails.Count == 0
            ? "ITEMTEST: PASS (smoke blocks+decays LoS, barricade=cover, loadouts map)"
            : "ITEMTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Headless self-test (SIGHTLINE_HAZARDTEST): environmental-hazard mechanics — a barrel blocks
    /// movement (IsFloor chokepoint), fire only lights floor + decays, and pathing routes around a
    /// barrel. Window-free (grid + CostMap only).
    public static string HazardSelfTest()
    {
        var fails = new List<string>();
        var grid = new Grid();
        for (int x = 0; x < grid.W; x++)
            for (int y = 0; y < grid.H; y++) grid.Tiles[x, y] = TileType.Floor;

        // (1) a barrel makes its tile non-floor (impassable everywhere via the IsFloor chokepoint)
        grid.Barrel[5, 5] = true;
        if (grid.IsFloor(5, 5)) fails.Add("barrelNotBlocking");
        if (!grid.IsBarrel(5, 5)) fails.Add("barrelFlag");

        // (2) pathing routes AROUND the barrel: a Dijkstra cost map never enters the barrel tile
        var cost = grid.CostMap(2, 5, (x, y) => false, out _, 9999);
        if (cost[5, 5] != -1) fails.Add("pathEntersBarrel");
        if (cost[8, 5] <= 0) fails.Add("pathBlockedEntirely");   // far tile still reachable around it

        // (3) fire lights only floor; a barrel tile won't ignite (it's not floor)
        grid.AddFire(7, 5, 1, Grid.FireTurns);
        if (!grid.IsFire(7, 5) || !grid.IsFire(7, 4) || !grid.IsFire(6, 6)) fails.Add("fireArea");
        grid.LightFire(5, 5, Grid.FireTurns);
        if (grid.IsFire(5, 5)) fails.Add("barrelTileBurns");

        // (4) fire decays to nothing over FireTurns ticks
        for (int i = 0; i < Grid.FireTurns; i++) grid.TickFire();
        if (grid.IsFire(7, 5)) fails.Add("firePersist");

        // (5) ClearHazards wipes both layers
        grid.AddFire(3, 3, 1, Grid.FireTurns); grid.Barrel[4, 4] = true;
        grid.ClearHazards();
        if (grid.IsFire(3, 3) || grid.IsBarrel(4, 4)) fails.Add("clearHazards");

        return fails.Count == 0
            ? "HAZARDTEST: PASS (barrel blocks move + pathing routes around; fire lights floor only + decays; clear works)"
            : "HAZARDTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Headless self-test (SIGHTLINE_SIEGETEST): the full SIEGE charge/telegraph/detonate/interrupt
    /// cycle + the no-target fallback (no TIMEOUT). Drives the real Ai.Plan + TickSiegeStrikes paths
    /// on a controlled open field. Prints SIEGETEST: PASS/FAIL.
    public string SiegeSelfTest()
    {
        NoPersist = true;
        var fails = new List<string>();

        // ---- controlled scene: empty open field, perfect LoS, no concealment ----
        Grid = new Grid();
        Players = new List<Unit>();
        Enemies = new List<Unit>();
        Vip = null; CaptiveLocked = false; SquadConcealed = false;
        Objective = Objective.Eliminate; EvacZone.Clear();
        Phase = Phase.EnemyTurn;

        Unit MkP(int x, int y) {
            var u = new Unit { Name = "SOLDIER", Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                               Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
        }
        Unit MkSiege(int x, int y) {
            var u = new Unit { Name = "SIEGE", Cls = "BOMBARD", Team = Team.Enemy, X = x, Y = y,
                               Hp = 7, MaxHp = 7, Aim = 48, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Smg) };
            u.Ammo = u.Weapon.Clip; u.Alert = AlertLevel.Alert; u.SyncPos(); u.BeginTurn(); return u;
        }

        // ---- 1. Ai.Plan charges a strike centered on the soldier cluster ----
        var siege = MkSiege(5, 5);
        var a = MkP(10, 5);          // the strike target (clustered alone but valid)
        var b = MkP(10, 6);          // adjacent -> also caught in the 3x3 around (10,5) or (10,6)
        Players.Add(a); Players.Add(b); Enemies.Add(siege);

        var plan = Ai.Plan(this, siege);
        if (plan.SiegeCharge == null) fails.Add("noCharge");
        else
        {
            // execute the charge branch by hand (UpdateEnemy's branch, minus the anim queue):
            var (cx, cy) = plan.SiegeCharge.Value;
            // the chosen center must catch both clustered soldiers (a 3x3 over (10,5) or (10,6) does)
            int caught = Players.Count(p => Util.ChebyDist(p.X, p.Y, cx, cy) <= SiegeRadius);
            if (caught < 2) fails.Add($"chargeCenterHits={caught}");
            siege.ChargeX = cx; siege.ChargeY = cy; siege.ChargeTurns = SiegeFuse;
            if (siege.ChargeTurns != SiegeFuse) fails.Add("chargeTurns");

            // ---- 2. telegraph predicate: zone tiles read in, far tiles read out ----
            if (!InSiegeZone(cx, cy)) fails.Add("zoneCenterFalse");
            if (!InSiegeZone(cx + 1, cy)) fails.Add("zoneEdgeFalse");
            if (InSiegeZone(cx + 5, cy)) fails.Add("zoneFarTrue");

            // ---- 3. detonate hits in-zone, spares out-of-zone, consumes the charge ----
            a.X = cx; a.Y = cy; a.SyncPos(); a.Hp = a.MaxHp; int aHp0 = a.Hp;   // A in the zone center
            b.X = cx + 6; b.Y = cy; b.SyncPos(); b.Hp = b.MaxHp; int bHp0 = b.Hp; // B well outside
            // ensure no occupancy overlap with the gunner
            siege.X = 0; siege.Y = 0; siege.SyncPos();
            TickSiegeStrikes();
            if (!a.Alive) { /* A may die at very low rolls — but at 8 HP a 7-9 strike won't one-shot via fragile floor */ }
            if (a.Hp >= aHp0) fails.Add("inZoneNotHit");
            if (b.Hp != bHp0) fails.Add("outZoneHit");
            if (siege.ChargeTurns != 0) fails.Add("chargeNotConsumed");
            if (InSiegeZone(cx, cy)) fails.Add("zoneNotClearedAfterFire");
        }

        // ---- 4. interrupt: a dead SIEGE's strike never fires ----
        Players.Clear(); Enemies.Clear();
        var siege2 = MkSiege(5, 5);
        var c = MkP(10, 5);
        Players.Add(c); Enemies.Add(siege2);
        siege2.ChargeX = c.X; siege2.ChargeY = c.Y; siege2.ChargeTurns = SiegeFuse;
        if (!InSiegeZone(c.X, c.Y)) fails.Add("preKillNoZone");
        siege2.Alive = false;                       // kill it (interrupt) — don't route through KillUnit (no _run)
        if (InSiegeZone(c.X, c.Y)) fails.Add("deadSiegeZoneLive");   // dead -> zone clears
        int cHp0 = c.Hp;
        TickSiegeStrikes();
        if (c.Hp != cHp0) fails.Add("deadSiegeStillFired");

        // ---- 5. no-target fallback (no dead turn): with no soldier in reach, Ai.Plan still spends an
        //         action (a move/shoot/overwatch/hunker) and does NOT return an empty no-op plan ----
        Players.Clear(); Enemies.Clear();
        var siege3 = MkSiege(2, 2);
        var far = MkP(2, 2);                         // place far away after building the grid below
        // put the lone soldier in indirect reach so BestSiege finds it... then move it adjacent to an
        // ALLY enemy so the "never shell our own" veto trips and BestSiege returns hits==0 (fallback).
        var ally = MkSiege(15, 9); ally.Cls = "GRUNT";  // an ordinary ally next to the soldier
        far.X = 15; far.Y = 9 - 1; far.SyncPos();        // adjacent to the ally -> any 3x3 catches the ally
        Players.Add(far); Enemies.Add(siege3); Enemies.Add(ally);
        var fb = Ai.Plan(this, siege3);
        if (fb.SiegeCharge != null) fails.Add("fallbackStillCharged");   // veto should have blocked the charge
        bool spends = fb.Path.Count > 0 || fb.ShootTarget != null || fb.Overwatch || fb.Hunker
                      || fb.Grenade || fb.SapTile != null || fb.HealTarget != null || fb.UseItem || fb.ShoveTarget != null;
        if (!spends) fails.Add("fallbackDeadTurn");

        return fails.Count == 0
            ? "SIEGETEST: PASS (charge sets zone; telegraph reads; detonate hits in-zone/spares out; kill cancels; no-target falls through)"
            : "SIEGETEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Headless self-test (SIGHTLINE_COVERTEST): destructible cover — High chips to Low
    /// to Floor, with LoS + cover level tracking the degrade, and a frag drops a level
    /// in one blow. Window-free (grid only).
    public static string CoverSelfTest()
    {
        var fails = new List<string>();
        var grid = new Grid();
        for (int x = 0; x < grid.W; x++)
            for (int y = 0; y < grid.H; y++) grid.Tiles[x, y] = TileType.Floor;

        // a high block: full HP, full cover from the facing side, blocks line of sight
        grid.Tiles[5, 5] = TileType.HighCover; grid.ResetCoverHp();
        if (grid.CoverHp[5, 5] != Grid.HighCoverHp) fails.Add("initHp");
        if (grid.GetCover(5, 4, 5, 9).Level != 2) fails.Add("highCover");
        if (grid.HasLineOfSight(5, 2, 5, 9)) fails.Add("highBlocksLoS");
        if (!grid.HasLineOfSight(5, 2, 5, 9, true)) fails.Add("commandingSeesOverHigh");  // 3.6b

        // chip it down: HighCoverHp hits to degrade High -> Low
        if (grid.DamageCover(5, 5, 1) != Grid.CoverHit.Chipped || grid.Tiles[5, 5] != TileType.HighCover) fails.Add("chip");
        if (grid.DamageCover(5, 5, 1) != Grid.CoverHit.Downgraded || grid.Tiles[5, 5] != TileType.LowCover) fails.Add("downgrade");
        if (grid.GetCover(5, 4, 5, 9).Level != 1) fails.Add("nowLow");
        if (!grid.HasLineOfSight(5, 2, 5, 9)) fails.Add("lowSeesThrough");

        // one more chip clears Low -> Floor
        if (grid.DamageCover(5, 5, 1) != Grid.CoverHit.Destroyed || grid.Tiles[5, 5] != TileType.Floor) fails.Add("destroy");
        if (grid.GetCover(5, 4, 5, 9).Level != 0) fails.Add("noCover");

        // a grenade-strength blow drops High -> Low in one hit
        grid.Tiles[7, 7] = TileType.HighCover; grid.SetCoverHp(7, 7);
        if (grid.DamageCover(7, 7, Grid.HighCoverHp) != Grid.CoverHit.Downgraded || grid.Tiles[7, 7] != TileType.LowCover) fails.Add("frag");

        return fails.Count == 0
            ? "COVERTEST: PASS (high->low->gone, LoS + cover follow, frag drops a level)"
            : "COVERTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Headless self-test (SIGHTLINE_DRAFTTEST): the run-opening squad-draft plumbing.
    /// (a) GenerateDraftPool returns DraftPoolSize recruits with class variety (<=2/class);
    /// (b) a drafted founding squad + a starting boon seats EXACTLY those Units + the boon
    /// active via Run.Start(picked)+ActiveBoons; (c) Run.Start(null) keeps the fixed default
    /// NewRunSquad (the harness path is unchanged). Window-free (no Raylib, no disk).
    public static string DraftSelfTest()
    {
        var fails = new List<string>();

        // (a) pool size + class variety
        var pool = Run.GenerateDraftPool();
        if (pool.Count != Run.DraftPoolSize) fails.Add($"poolSize={pool.Count}(want{Run.DraftPoolSize})");
        var byCls = new Dictionary<string, int>();
        foreach (var u in pool) { byCls.TryGetValue(u.Cls, out int c); byCls[u.Cls] = c + 1; }
        foreach (var kv in byCls) if (kv.Value > 2) fails.Add($"class {kv.Key}x{kv.Value}(>2)");
        if (byCls.Count < 2) fails.Add($"variety={byCls.Count}(want>=2)");

        // (b) a drafted core of DraftCap + a chosen starting boon seats exactly those + the boon
        var picked = new List<Unit>();
        for (int i = 0; i < DraftCap && i < pool.Count; i++) picked.Add(pool[i]);
        var run = new Run();
        run.Start(picked);
        run.ActiveBoons.Add(Boon.Marksmen);
        if (run.Squad.Count != picked.Count) fails.Add($"draftCount={run.Squad.Count}(want{picked.Count})");
        for (int i = 0; i < picked.Count; i++)
            if (i >= run.Squad.Count || !ReferenceEquals(run.Squad[i], picked[i])) fails.Add($"draftSeat[{i}]");
        if (!run.HasBoon(Boon.Marksmen)) fails.Add("boonNotActive");

        // (c) the default (harness) path still yields the fixed NewRunSquad
        var run2 = new Run();
        run2.Start();
        var def = Sightline.Mission.NewRunSquad();
        if (run2.Squad.Count != def.Count) fails.Add($"defaultCount={run2.Squad.Count}(want{def.Count})");
        else for (int i = 0; i < def.Count; i++)
            if (run2.Squad[i].Name != def[i].Name || run2.Squad[i].Cls != def[i].Cls) fails.Add($"defaultSquad[{i}]");
        if (run2.ActiveBoons.Count != 0) fails.Add($"defaultBoons={run2.ActiveBoons.Count}(want0)");

        return fails.Count == 0
            ? $"DRAFTTEST: PASS (pool={pool.Count} variety={byCls.Count}cls, drafted {picked.Count}+boon seated, default squad intact)"
            : "DRAFTTEST: FAIL (" + string.Join(",", fails) + ")";
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
        if (cost > 0 && _run.Intel < cost) return false;
        // roster: a recruit requires a free roster slot
        if (ch.Outcome.Kind == EventOutcomeKind.Recruit && _run.Squad.Count >= Run.RosterMax) return false;
        return true;
    }

    /// Autopilot/balance default: prefer a SAFE, beneficial legal choice (no intel cost, no gamble,
    /// no self-wound, no heat) so the balance harness models SENSIBLE play — a human wouldn't gamble
    /// half their intel or wound a soldier on every event. Falls back to the first legal choice, then
    /// the last (always a no-op/safe option). Always returns a legal index -> one-tick resolve, no stall.
    int AutoEventChoice()
    {
        if (_activeEvent == null) return 0;
        int firstLegal = -1;
        for (int i = 0; i < _activeEvent.Choices.Length; i++)
        {
            if (!ChoiceLegal(_activeEvent.Choices[i])) continue;
            if (firstLegal < 0) firstLegal = i;
            if (IsSafeChoice(_activeEvent.Choices[i])) return i;   // prefer a downside-free benefit
        }
        return firstLegal >= 0 ? firstLegal : _activeEvent.Choices.Length - 1;
    }

    /// A choice with no downside in either outcome (no intel spend, gamble, self-wound, or heat gain).
    static bool IsSafeChoice(EventChoice ch)
        => !HasDownside(ch.Outcome) && (!ch.HasSecond || !HasDownside(ch.Outcome2));
    static bool HasDownside(EventOutcome o)
        => (o.Kind == EventOutcomeKind.Intel && o.Amount < 0)
           || o.Kind == EventOutcomeKind.GambleIntel
           || o.Kind == EventOutcomeKind.WoundSoldier
           || o.Kind == EventOutcomeKind.AddHeat;

    void ResolveEvent(int choiceIdx)
    {
        if (_activeEvent == null) return;
        if (choiceIdx < 0 || choiceIdx >= _activeEvent.Choices.Length) choiceIdx = AutoEventChoice();
        var ch = _activeEvent.Choices[choiceIdx];
        if (!ChoiceLegal(ch)) return;   // ignore clicks on illegal choices
        string line = EventCatalog.Apply(_run, ch.Outcome, _eventNode);
        if (ch.HasSecond)
        {
            string line2 = EventCatalog.Apply(_run, ch.Outcome2, _eventNode);
            line = line + "; " + line2;
        }
        _run.Report.Insert(0, $"EVENT: {_activeEvent.Title} -- {line}");
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

    /// Harness (screenshot): show the event screen at a mid column.
    public void DebugEvent()
    {
        _run.JumpTo(3);
        _run.DebriefSurvivors();
        _run.PendingPerks.Clear();
        _run.PendingSpecs.Clear();
        _shopDone = true;
        // synthesize an event node so the screen shows even if this seed placed none on the route
        var node = _run.CurrentNode ?? (_run.Map.Count > 0 ? _run.Map[0] : null);
        _eventNode = node;
        _activeEvent = EventCatalog.All.Length > 0 ? EventCatalog.All[0] : null;
        Phase = Phase.Barracks;
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
                PendingHeat = Sightline.Heat.Clamp(Math.Clamp(PendingHeat + delta, 0, UnlockedHeat));
                Audio.Play("select");
            }
        }

        // intro CONTINUE: resume a saved campaign (button or key C)
        if (Phase == Phase.Intro && SaveGame.Exists)
        {
            bool cont = (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                         Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Hud.OverlayBtn2))
                        || Raylib.IsKeyPressed(KeyboardKey.C);
            if (cont && ContinueRun()) return;
        }

        bool click = Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                     Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Hud.OverlayBtn);
        bool enter = Raylib.IsKeyPressed(KeyboardKey.Enter);
        if (!click && !enter) return;

        if (Phase == Phase.Barracks) NextMission();   // deploy to next mission
        // intro / win / lose -> new run. INTERACTIVELY this opens the run-opening DRAFT (pick a
        // founding squad + starting boon). The harness NEVER reaches here (it calls StartMission
        // DIRECTLY, bypassing the intro), but gate on !NoPersist defensively so the smoke test /
        // balance batch can never enter Phase.Draft (which would have no autopilot path -> hang).
        else if (!NoPersist) BeginDraft();
        else StartMission();
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

    public void Draw()
    {
        Raylib.ClearBackground(Pal.Bg);

        Raylib.BeginMode2D(ViewCamera(true));
        Renderer.DrawBoard(this);
        Raylib.EndMode2D();

        // red death-flash over the board (under the HUD) when a soldier falls
        if (DeathFlash > 0)
            Raylib.DrawRectangle(0, 0, Cfg.ScreenW, Cfg.ScreenH, Raylib.Fade(Pal.Foe, DeathFlash * 0.35f));

        Hud.Draw(this);
    }
}
