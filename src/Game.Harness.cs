using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

// Headless HARNESS — the SIGHTLINE_* test/debug hooks dispatched from Program.cs. Two kinds live here:
//   - Debug* (screenshot hooks): stage a controlled scene / force a state so a single SHOT frame
//     shows a specific feature (alert tiers, pressure meter, cover degrade, event card, ...). Called
//     only from Program.cs under SIGHTLINE_SHOT + a feature flag; never in normal play.
//   - *SelfTest / *Test (window-free self-tests): drive real game primitives on a controlled scene and
//     return a "XTEST: PASS/FAIL" string (concealment, shove, field-craft, bench, AI-squad, status,
//     snap-refund, contracts, cooldowns, scars, items, hazards, siege, cover, draft, death-consequence).
// This is a pure mechanical slice of Game.cs — no behaviour change. (MetaSelfTest/DebugWarRoom live in
// Game.Meta.cs; CodexSelfTest/DebugCodex in Game.Codex.cs; ModeSelfTest/DebugSkirmishSetup in
// Game.Modes.cs; HordeSelfTest in Game.Endless.cs — left in their own slices.)
public partial class Game
{
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
        killer.ActionsLeft = 1; killer.FiredThisTurn = true; killer.MovedAfterFire = false;   // as if a flank-shot just fired (1 action, no end-turn)
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

    /// Headless self-test (SIGHTLINE_CONTRACTTEST): the W6 run contracts. Pure run-state logic, no
    /// window. Asserts each contract's read FIRES and is INERT as None:
    ///   (a) ContractDef wiring (All excludes None; ordinals; names/codes).
    ///   (b) None == STANDARD: backfill + field-heal happen, no kill bonus (the no-regression baseline).
    ///   (c) IronVeterans: no recruit backfill AND survivors bank +1 promotion-kill credit/mission.
    ///   (d) HighStakes: no field-heal in DebriefSurvivors (survivors carry damage forward).
    public string ContractSelfTest()
    {
        NoPersist = true;
        var fails = new List<string>();

        // (a) ContractDef wiring + append-only ordinals
        if (ContractDef.All.Length != 3) fails.Add("allCount");
        if (System.Array.IndexOf(ContractDef.All, Contract.None) >= 0) fails.Add("allHasNone");
        var cv = (Contract[])Enum.GetValues(typeof(Contract));
        if (cv.Length < 4 || cv[0] != Contract.None || cv[^1] != Contract.Spearhead) fails.Add("ordinals");
        if (ContractDef.Name(Contract.IronVeterans) != "IRON VETERANS") fails.Add("name");
        if (ContractDef.Code(Contract.HighStakes) != "HST") fails.Add("code");

        // Build a tiny run with two healthy-but-chipped survivors below the recruit floor, so a
        // backfill WOULD normally fire and a field-heal WOULD normally raise HP.
        Run MakeRun(Contract c)
        {
            var r = new Run { Mission = 2, Contract = c, Squad = new List<Unit>() };
            for (int i = 0; i < 2; i++)
            {
                var u = new Unit { Name = "S" + i, Cls = "ASSAULT", Team = Team.Player,
                                   Hp = 5, MaxHp = 10, Aim = 70, Mobility = 6,
                                   Weapon = Weapon.Make(WeaponKind.Rifle), Alive = true,
                                   Kills = 0, Rank = 0 };
                r.Squad.Add(u);
            }
            return r;
        }

        // (b) None baseline: backfill to the AttritionFloor happens; field-heal raises HP; no kill bonus.
        var rNone = MakeRun(Contract.None);
        rNone.DebriefSurvivors();
        if (rNone.Squad.Count < Run.AttritionFloor) fails.Add("none:noBackfill");
        if (rNone.Squad[0].Hp <= 5) fails.Add("none:noHeal");     // 0.55*10 ceil = +6 -> capped 10
        if (rNone.Squad[0].Kills != 0) fails.Add("none:killBonus");

        // (c) IronVeterans: NO backfill (squad stays 2 even below the floor) + each survivor +1 kill.
        var rIron = MakeRun(Contract.IronVeterans);
        rIron.DebriefSurvivors();
        if (rIron.Squad.Count != 2) fails.Add("iron:backfilled");
        if (rIron.Squad[0].Kills != 1) fails.Add("iron:noKillBonus(" + rIron.Squad[0].Kills + ")");

        // (d) HighStakes: NO field-heal (HP unchanged at 5); backfill still happens (it's a heal contract,
        // not a recruit one) so the floor is met.
        var rStakes = MakeRun(Contract.HighStakes);
        rStakes.DebriefSurvivors();
        if (rStakes.Squad[0].Hp != 5) fails.Add("stakes:healed(" + rStakes.Squad[0].Hp + ")");
        if (rStakes.Squad.Count < Run.AttritionFloor) fails.Add("stakes:noBackfill");

        return fails.Count == 0
            ? "CONTRACTTEST: PASS (None inert; IronVeterans no-backfill+fast-rank; HighStakes no-heal)"
            : "CONTRACTTEST: FAIL (" + string.Join(",", fails) + ")";
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

    /// Headless self-test (SIGHTLINE_SCARTEST): the W5 SCARS — earn each via the trauma flags
    /// through the real DebriefSurvivors path, then assert the Combat reads fire:
    ///   ShellShocked  -> -mobility (MoveBudget) AND Disorient/Stun immunity (AddStatus)
    ///   BurnScarred   -> +max HP on grant (idempotent) AND -aim while Burning
    ///   HardBitten    -> +crit while bloodied, -aim at full HP
    ///   Vendetta      -> +aim/+crit vs MissionFaction == VendettaFaction (inert otherwise)
    /// Window-free (grid + tile math + static Combat reads only).
    public string ScarSelfTest()
    {
        NoPersist = true;
        var fails = new List<string>();
        Faction savedMission = Combat.MissionFaction;   // restore at the end (don't bleed into runtime)
        try
        {
            // open arena so ComputeOdds has clear LoS
            var grid = new Grid();
            for (int x = 0; x < grid.W; x++)
                for (int y = 0; y < grid.H; y++) grid.Tiles[x, y] = TileType.Floor;

            // --- (1) EARNING via the real Run.DebriefSurvivors path ---
            // A run whose just-played mission was a WARDENS fight; one soldier survived a near-death
            // AND walked out of fire. Debrief must brand: Vendetta(Wardens), and (NearDeathCount 1->,
            // not yet 2) BurnScarred. A second near-death debrief pushes NearDeathCount to 2 -> ShellShocked,
            // a third -> HardBitten.
            var run = new Run { Mission = 2, Squad = new List<Unit>() };
            run.LastMissionFaction = Faction.Wardens;
            var vet = new Unit { Name = "VEGA", Cls = "ASSAULT", Team = Team.Player, Hp = 4, MaxHp = 12,
                                 Aim = 70, Mobility = 7, Weapon = Weapon.Make(WeaponKind.Rifle), Rank = 1, Kills = 0 };
            run.Squad.Add(vet);
            int hpBefore = vet.MaxHp;

            // mission 1 debrief: near-death + burned under Wardens. A near-death also grants the
            // IronWill TRAIT (+IronWillHp max HP), so the expected bump is BurnScarHp + IronWillHp.
            vet.WasNearDeath = true; vet.FeatBurned = true;
            run.DebriefSurvivors();
            if (!vet.HasScar(Scar.Vendetta)) fails.Add("noVendettaGrant");
            if (vet.VendettaFaction != Faction.Wardens) fails.Add("vendettaFaction");
            if (!vet.HasScar(Scar.BurnScarred)) fails.Add("noBurnScarGrant");
            if (vet.MaxHp != hpBefore + Unit.BurnScarHp + Unit.IronWillHp) fails.Add("burnScarHpNotApplied");
            if (vet.NearDeathCount != 1) fails.Add("nearDeathCount1");
            if (vet.HasScar(Scar.ShellShocked)) fails.Add("shellShockedTooEarly");

            // idempotency: a second BURN with the scar already held must NOT re-add or re-bump HP
            int hpAfterBurn = vet.MaxHp;
            vet.FeatBurned = true; vet.WasNearDeath = false;
            run.DebriefSurvivors();
            if (vet.Scars.FindAll(s => s == Scar.BurnScarred).Count != 1) fails.Add("burnScarDoubleAdd");
            if (vet.MaxHp != hpAfterBurn) fails.Add("burnScarDoubleHp");

            // mission debriefs to push NearDeathCount 1 -> 2 (ShellShocked) -> 3 (HardBitten)
            vet.WasNearDeath = true; run.DebriefSurvivors();
            if (vet.NearDeathCount != 2 || !vet.HasScar(Scar.ShellShocked)) fails.Add("shellShockedAt2");
            if (vet.HasScar(Scar.HardBitten)) fails.Add("hardBittenTooEarly");
            vet.WasNearDeath = true; run.DebriefSurvivors();
            if (vet.NearDeathCount != 3 || !vet.HasScar(Scar.HardBitten)) fails.Add("hardBittenAt3");

            // --- (2) SHELL-SHOCKED reads ---
            // -mob in MoveBudget (vs an unscarred clone with the same Mobility)
            var clean = new Unit { Mobility = vet.Mobility, Weapon = Weapon.Make(WeaponKind.Rifle) };
            if (vet.MoveBudget != clean.MoveBudget - Unit.ShellShockMob * 2) fails.Add("shellShockMobBudget");
            // Disorient + Stun immunity via AddStatus (no-op when ShellShocked)
            vet.AddStatus(StatusKind.Disoriented, 3);
            vet.AddStatus(StatusKind.Stun, 3);
            if (vet.HasStatus(StatusKind.Disoriented)) fails.Add("shellShockDisorientNotImmune");
            if (vet.HasStatus(StatusKind.Stun)) fails.Add("shellShockStunNotImmune");
            // a non-scarred soldier is still affected (proves the gate is the scar, not a global change)
            var ctrl = new Unit { Weapon = Weapon.Make(WeaponKind.Rifle) };
            ctrl.AddStatus(StatusKind.Stun, 3);
            if (!ctrl.HasStatus(StatusKind.Stun)) fails.Add("controlStunImmuneWrong");

            // --- (3) Combat aim/crit reads (build controlled attacker/defender pairs) ---
            Combat.MissionFaction = Faction.None;   // baseline: no faction gate active
            var foe = new Unit { Name = "G", Cls = "GRUNT", Team = Team.Enemy, Hp = 6, MaxHp = 6,
                                 Aim = 50, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle), X = 8, Y = 5,
                                 Alert = AlertLevel.Alert };

            Unit Shooter() => new Unit { Cls = "ASSAULT", Team = Team.Player, Aim = 70, Mobility = 6,
                                         Weapon = Weapon.Make(WeaponKind.Rifle), X = 5, Y = 5, Hp = 12, MaxHp = 12 };

            // HARD-BITTEN: -aim at FULL HP, +crit while bloodied
            var hbFull = Shooter(); hbFull.Scars.Add(Scar.HardBitten); hbFull.Hp = hbFull.MaxHp;
            var plain  = Shooter();
            int fullAimOn  = Combat.ComputeOdds(grid, hbFull, foe).HitChance;
            int fullAimOff = Combat.ComputeOdds(grid, plain,  foe).HitChance;
            if (fullAimOn >= fullAimOff) fails.Add("hardBittenFullAimNotPenalised");

            var hbLow = Shooter(); hbLow.Scars.Add(Scar.HardBitten); hbLow.Hp = 3;   // bloodied
            var plainLow = Shooter(); plainLow.Hp = 3;
            int lowCritOn  = Combat.ComputeOdds(grid, hbLow, foe).CritChance;
            int lowCritOff = Combat.ComputeOdds(grid, plainLow, foe).CritChance;
            if (lowCritOn <= lowCritOff) fails.Add("hardBittenBloodiedCritMissing");

            // BURN-SCARRED: -aim while Burning (vs not burning)
            var bsBurn = Shooter(); bsBurn.Scars.Add(Scar.BurnScarred); bsBurn.AddStatus(StatusKind.Burning, 2);
            var bsCalm = Shooter(); bsCalm.Scars.Add(Scar.BurnScarred);
            int burnAim = Combat.ComputeOdds(grid, bsBurn, foe).HitChance;
            int calmAim = Combat.ComputeOdds(grid, bsCalm, foe).HitChance;
            if (burnAim >= calmAim) fails.Add("burnScarBurnAimNotPenalised");

            // VENDETTA: +aim/+crit vs MissionFaction == VendettaFaction; inert when mismatched
            var venom = Shooter(); venom.Scars.Add(Scar.Vendetta); venom.VendettaFaction = Faction.Wardens;
            Combat.MissionFaction = Faction.None;
            var vNone = Combat.ComputeOdds(grid, venom, foe);
            Combat.MissionFaction = Faction.Legion;          // mismatched faction -> inert
            var vMiss = Combat.ComputeOdds(grid, venom, foe);
            Combat.MissionFaction = Faction.Wardens;         // matched -> +aim/+crit
            var vOn   = Combat.ComputeOdds(grid, venom, foe);
            if (vMiss.HitChance != vNone.HitChance) fails.Add("vendettaMismatchNotInert");
            if (vOn.HitChance <= vNone.HitChance) fails.Add("vendettaAimMissing");
            if (vOn.CritChance <= vNone.CritChance) fails.Add("vendettaCritMissing");

            return fails.Count == 0
                ? "SCARTEST: PASS (earn via debrief; ShellShocked -mob+immune; BurnScarred +HP/-aim; HardBitten crit/full-aim; Vendetta faction-gated)"
                : "SCARTEST: FAIL (" + string.Join(",", fails) + ")";
        }
        catch (Exception e) { return "SCARTEST: FAIL (exception " + e.Message + ")"; }
        finally { Combat.MissionFaction = savedMission; }
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

    // ── VETTEST (SIGHTLINE_VETTEST): the cross-run VETERAN reserve — enshrine promoted survivors, recall
    // them into a future draft carrying rank/perks/traits/spec/scars, dedupe by name, cap the reserve, and
    // keep the fresh/harness draft path an all-rookie pool. Preserves/restores the real meta.json. ──────
    public static string VetSelfTest()
    {
        var fails = new List<string>();
        string metaSaved = System.IO.File.Exists(SaveGame.MetaPathPublic)
            ? System.IO.File.ReadAllText(SaveGame.MetaPathPublic) : null;
        try
        {
            try { if (System.IO.File.Exists(SaveGame.MetaPathPublic)) System.IO.File.Delete(SaveGame.MetaPathPublic); } catch { }

            // (1) a fresh profile has no veterans, and an all-fresh draft pool carries NO reserve unit
            if (SaveGame.LoadVeterans().Count != 0) fails.Add("freshNotEmpty");
            foreach (var u in Run.GenerateDraftPool()) if (u.FromReserve) fails.Add("freshPoolHasVet");

            // (2) enshrine a promoted, storied veteran -> it round-trips with its progression intact
            var v = new Unit { Name = "VEGA", Cls = "ASSAULT", Team = Team.Player, MaxHp = 12, Hp = 12, Aim = 80, Mobility = 8, Kills = 9, Rank = 3, Alive = true };
            v.Nickname = "REAPER"; v.Weapon = Weapon.Make(WeaponKind.Rifle);
            v.Perks.Add(Perk.Deadeye); v.Perks.Add(Perk.Tank);
            v.Traits.Add(Trait.Killer); v.Spec = Spec.Breacher; v.Scars.Add(Scar.ShellShocked);
            SaveGame.EnshrineVeterans(new[] { v });
            var got = SaveGame.LoadVeterans();
            if (got.Count != 1) fails.Add($"enshrineCount={got.Count}");
            else
            {
                var g0 = got[0];
                if (g0.Name != "VEGA" || g0.Rank != 3 || g0.Kills != 9) fails.Add("vetStats");
                if (!g0.HasPerk(Perk.Deadeye) || !g0.HasPerk(Perk.Tank)) fails.Add("vetPerks");
                if (!g0.HasTrait(Trait.Killer) || g0.Spec != Spec.Breacher || !g0.HasScar(Scar.ShellShocked)) fails.Add("vetIdentity");
                if (g0.Nickname != "REAPER") fails.Add("vetNick");
                if (!g0.FromReserve) fails.Add("vetNotFlagged");
            }

            // (3) dedupe by name — a returning name updates in place (newer record wins), not duplicated
            var v2 = new Unit { Name = "VEGA", Cls = "ASSAULT", Team = Team.Player, MaxHp = 12, Hp = 12, Aim = 82, Kills = 14, Rank = 4, Alive = true, Weapon = Weapon.Make(WeaponKind.Rifle) };
            SaveGame.EnshrineVeterans(new[] { v2 });
            var ded = SaveGame.LoadVeterans();
            if (ded.Count != 1) fails.Add($"dedupeCount={ded.Count}");
            else if (ded[0].Kills != 14 || ded[0].Rank != 4) fails.Add("dedupeNotUpdated");

            // (4) cap at MaxVeterans, keeping the most-storied (highest kills). Enshrine MaxVeterans+4 fresh
            //     names with ASCENDING kills; the lowest-kills few must be dropped.
            int extra = SaveGame.MaxVeterans + 4;
            var batch = new List<Unit>();
            for (int i = 0; i < extra; i++)
                batch.Add(new Unit { Name = $"OP{i}", Cls = "RANGER", Team = Team.Player, MaxHp = 8, Hp = 8, Kills = 20 + i, Rank = 2, Alive = true, Weapon = Weapon.Make(WeaponKind.Shotgun) });
            SaveGame.EnshrineVeterans(batch);
            var capped = SaveGame.LoadVeterans();
            if (capped.Count != SaveGame.MaxVeterans) fails.Add($"cap={capped.Count}(want{SaveGame.MaxVeterans})");
            if (capped.Exists(u => u.Name == "OP0")) fails.Add("capKeptLeastStoried");   // OP0 (lowest kills) must be gone
            if (!capped.Exists(u => u.Name == $"OP{extra - 1}")) fails.Add("capDroppedMostStoried");

            // (5) a draft recalls up to MaxDraftVeterans of them (FromReserve), pool stays DraftPoolSize
            var pool = Run.GenerateDraftPool(SaveGame.LoadVeterans());
            if (pool.Count != Run.DraftPoolSize) fails.Add($"vetPoolSize={pool.Count}");
            int vetInPool = pool.FindAll(u => u.FromReserve).Count;
            if (vetInPool == 0 || vetInPool > Run.MaxDraftVeterans) fails.Add($"vetInPool={vetInPool}(want1..{Run.MaxDraftVeterans})");
            int freshInPool = pool.FindAll(u => !u.FromReserve).Count;
            if (freshInPool != Run.DraftPoolSize - vetInPool) fails.Add("poolFreshFill");
        }
        catch (Exception e) { return "VETTEST: FAIL (exception " + e.Message + ")"; }
        finally
        {
            if (metaSaved != null) { try { System.IO.File.WriteAllText(SaveGame.MetaPathPublic, metaSaved); } catch { } }
            else { try { if (System.IO.File.Exists(SaveGame.MetaPathPublic)) System.IO.File.Delete(SaveGame.MetaPathPublic); } catch { } }
        }
        return fails.Count == 0
            ? $"VETTEST: PASS (enshrine+recall carries rank/perks/traits/spec/scars; dedupe; cap {SaveGame.MaxVeterans}; draft seats <={Run.MaxDraftVeterans} veterans)"
            : "VETTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Harness (screenshot): the run-opening DRAFT with recalled VETERANS seeded into the pool, so the
    /// gold veteran cards + carried-progression dossier are visible (BeginDraft suppresses veterans under
    /// NoPersist for byte-stability, so this injects demo veterans directly).
    public void DebugVetDraft()
    {
        var demo = new List<Unit>();
        var a = new Unit { Name = "VEGA", Cls = "ASSAULT", Team = Team.Player, MaxHp = 13, Hp = 13, Aim = 82, Mobility = 8, Kills = 21, Rank = 4, Alive = true, Weapon = Weapon.Make(WeaponKind.Rifle), FromReserve = true };
        a.Nickname = "REAPER"; a.Perks.Add(Perk.Deadeye); a.Perks.Add(Perk.Tank); a.Perks.Add(Perk.LockOn); a.Traits.Add(Trait.Killer); a.Traits.Add(Trait.ColdBlood);
        var b = new Unit { Name = "NOX", Cls = "SHARPSHOOTER", Team = Team.Player, MaxHp = 8, Hp = 8, Aim = 88, Mobility = 6, Kills = 15, Rank = 3, Alive = true, Weapon = Weapon.Make(WeaponKind.Sniper), FromReserve = true };
        b.Nickname = "GHOST"; b.Perks.Add(Perk.Marksman); b.Traits.Add(Trait.Vengeful);
        demo.Add(a); demo.Add(b);
        DraftPool = Run.GenerateDraftPool(demo);
        DraftBoonOffer = Run.GenerateDraftBoonOffer();
        DraftPicked = new HashSet<Unit>();
        DraftSelectedBoon = null;
        DraftSelectedContract = null;
        Phase = Phase.Draft;
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

}
