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

    /// FUL-4 harness hook (screenshot only; pair with SIGHTLINE_OBJ=defend): stage the wave-edge
    /// telegraph by jumping the turn counter to the first wave turn and running the REAL
    /// BeginPlayerTurn path (MaybeWaveTelegraph reads the spawner's own DefendWaveTurn schedule),
    /// so the frame shows exactly what a live t3 shows — banner, sub-line, DEFEND 3/8 pill.
    public void DebugWaveTelegraph()
    {
        _turnCount = 3;
        MaybeWaveTelegraph();
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

        // ---- W10 SUPPRESSOR: a suppressed AMBUSH still breaks concealment, but the seen-pod wake
        // loop narrows to the TARGET's own pod; an unsuppressed shot wakes every pod in sight.
        // Both legs drive the REAL firing site (IssueShoot -> BreakConcealment threading), on a
        // controlled two-pod scene: shooter at p, pod 90's body straight east (the shot target),
        // pod 91's body also in clear sight. CheckPodActivation's sight rules are untouched.
        void StageSuppressorScene(bool suppressed, out Unit shooter, out Unit tgtPod, out Unit bystanderPod)
        {
            StartMission(1);
            shooter = Players.First(u => u.Alive && !u.IsVip);
            var foes = Enemies.Where(x => x.Alive).Take(2).ToList();
            tgtPod = foes[0]; bystanderPod = foes[1];
            // clear a floor window around the firing lane so LoS/targeting is unconditional
            for (int x = shooter.X; x <= shooter.X + 4 && x < Grid.W; x++)
                for (int y = Math.Max(0, shooter.Y - 1); y <= Math.Min(Grid.H - 1, shooter.Y + 3); y++)
                { Grid.Tiles[x, y] = TileType.Floor; Grid.Barrel[x, y] = false; }
            tgtPod.PodId = 90; tgtPod.Alert = AlertLevel.Unaware;
            tgtPod.X = Math.Min(Grid.W - 1, shooter.X + 3); tgtPod.Y = shooter.Y; tgtPod.SyncPos();
            bystanderPod.PodId = 91; bystanderPod.Alert = AlertLevel.Unaware;
            bystanderPod.X = Math.Min(Grid.W - 1, shooter.X + 3); bystanderPod.Y = Math.Min(Grid.H - 1, shooter.Y + 2); bystanderPod.SyncPos();
            shooter.WeaponMods.Clear();
            if (suppressed) shooter.InstallMod(WeaponMod.Suppressor);
            shooter.Ammo = Math.Max(1, shooter.Ammo);
            Selected = shooter;
        }
        // (a) SUPPRESSED: concealment breaks, the TARGET's pod wakes, the bystander pod stays dormant.
        StageSuppressorScene(true, out var sup, out var supTgt, out var supBys);
        if (!SquadConcealed) fails.Add("supSceneNotConcealed");
        IssueShoot(supTgt);
        if (SquadConcealed) fails.Add("suppressedShotDidNotBreak");     // still breaks NORMALLY
        if (!supTgt.Active) fails.Add("suppressedTargetPodAsleep");     // the shot-at pod always wakes
        if (supBys.Active) fails.Add("suppressedWokeBystanderPod");     // the narrow: no one else places it
        sup.WeaponMods.Clear();                                          // leave no mod on the squad copy
        // (b) UNSUPPRESSED control: the same shot wakes EVERY pod in sight (target + bystander).
        StageSuppressorScene(false, out _, out var ctlTgt, out var ctlBys);
        IssueShoot(ctlTgt);
        if (SquadConcealed) fails.Add("controlShotDidNotBreak");
        if (!ctlTgt.Active) fails.Add("controlTargetPodAsleep");
        if (!ctlBys.Active) fails.Add("controlBystanderPodAsleep");     // full wake without the mod

        return fails.Count == 0
            ? "CONCEALTEST: PASS (start concealed; pods gated; break arms+wakes; RevealRange breaks w/o bonus; suppressor narrows the wake to the target pod, unsuppressed wakes all seen)"
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
        if (dragger.DragsThisTurn < 1) fails.Add("dragDidNotSetFlag");   // W10: bool -> per-turn counter
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
        if (v.VaultsThisTurn < 1) fails.Add("vaultDidNotSetFlag");   // W10: bool -> per-turn counter
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

        // ---- UNDERTOW W4 — sequenced coordination: setup-first ordering + incremental focus recompute ----
        {
            Grid = new Grid();
            Players = new System.Collections.Generic.List<Unit>();
            Enemies = new System.Collections.Generic.List<Unit>();
            Unit MkE2(string n, string cls, int x, int y) {
                var u = new Unit { Name = n, Cls = cls, Team = Team.Enemy, X = x, Y = y, Hp = 6, MaxHp = 6,
                                   Aim = 60, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
                u.Ammo = u.Weapon.Clip; u.Alert = AlertLevel.Alert; u.SyncPos(); return u;
            }
            Unit MkP2(string n, int x, int y, int hp) {
                var u = new Unit { Name = n, Cls = "ASSAULT", Team = Team.Player, X = x, Y = y, Hp = hp, MaxHp = 8,
                                   Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
                u.Ammo = u.Weapon.Clip; u.SyncPos(); return u;
            }
            // (a) IsSetupUnit: a SAPPER is a setup verb; a plain grunt far from any covered soldier is not.
            var sapper = MkE2("SAP", "SAPPER", 8, 5);
            var plain  = MkE2("GRT", "GRUNT",  9, 1);
            var solF   = MkP2("SF", 4, 5, 8);
            Players.Add(solF); Enemies.Add(plain); Enemies.Add(sapper);   // sapper added SECOND
            if (!IsSetupUnit(sapper)) fails.Add("sapperNotSetup");
            if (IsSetupUnit(plain))   fails.Add("gruntIsSetup");
            // (b) the stable OrderBy puts the setup unit FIRST even though it was added last.
            var ordered = AliveEnemies().Where(e => e.Active).ToList().OrderBy(e => IsSetupUnit(e) ? 0 : 1).ToList();
            if (ordered.Count < 1 || ordered[0].Cls != "SAPPER") fails.Add("setupNotFirst");

            // (c) incremental focus recompute responds to a LIVE board change: two exposed, shootable
            //     soldiers -> focus picks the lower-HP one; drop the OTHER's HP and re-run -> focus flips.
            Players.Clear();
            var sHi = MkP2("HI", 6, 5, 8);
            var sLo = MkP2("LO", 6, 6, 3);
            Players.Add(sHi); Players.Add(sLo);
            _aiUnits = AliveEnemies().Where(e => e.Active).ToList();
            PlanEnemySquad();
            if (EnemyFocus != sLo) fails.Add("focusNotLowHp=" + (EnemyFocus?.Name ?? "null"));
            sHi.Hp = 1;                                   // a mid-turn hit/exposure drops the other soldier
            PlanEnemySquad();                             // W4 recompute must see it and flip the focus
            if (EnemyFocus != sHi) fails.Add("focusDidNotFlipLive=" + (EnemyFocus?.Name ?? "null"));
        }

        // ---- W6a (1) — COMMANDING LoS: the planner must SEE the climb+shot a tier-2 plateau
        // unlocks. A full-height HIGH-COVER wall seals every ground-level sightline to the
        // soldier; the ONLY shot on the board is the commanding one from the '='-style tier-2
        // plateau beside the enemy (Game.CanTarget grants >=2-tier shooters sight over high
        // cover, and ComputeOdds' seesOver prices it). Before W6a the planner's LoS filter never
        // passed the commanding overload, so it filtered out the exact shot the resolver allows
        // and the enemy never sought the perch.
        {
            Grid = new Grid();
            Players = new System.Collections.Generic.List<Unit>();
            Enemies = new System.Collections.Generic.List<Unit>();
            for (int y = 0; y < Grid.H; y++) { Grid.Tiles[10, y] = TileType.HighCover; Grid.SetCoverHp(10, y); }
            Grid.Height[8, 5] = 2;                          // the tier-2 vantage ('=' in Maps.cs)
            var marks   = MkP("MARKS", 13, 5, 8, 8);        // soldier behind the wall
            var climber = MkE("CLIMB", 7, 5);               // one step from the plateau
            Players.Add(marks); Enemies.Add(climber);
            _aiUnits = AliveEnemies().Where(e => e.Active).ToList();
            PlanEnemySquad();
            var cplan = Ai.Plan(this, climber);
            if (cplan.ShootTarget != marks) fails.Add("commandingShotNotPlanned");
            (int x, int y) cEnd = cplan.Path.Count > 0 ? cplan.Path[cplan.Path.Count - 1] : (climber.X, climber.Y);
            if (Grid.HeightAt(cEnd.x, cEnd.y) - Grid.HeightAt(marks.X, marks.Y) < 2)
                fails.Add("commandingNoClimb");             // the plan must actually take the perch
        }

        // ---- W6a (2) — CROSSFIRE PIN: Ai.CrossfireWith must return Combat.InCrossfire's verdict
        // term-for-term (it is the planner's prediction of the resolver's +CrossfireAim). Three
        // staged cases; (a) and (b) are DISAGREEMENTS that failed before the pin.
        {
            Grid = new Grid();                              // open floor: LoS clear everywhere
            Players = new System.Collections.Generic.List<Unit>();
            Enemies = new System.Collections.Generic.List<Unit>();
            var xTgt = MkP("XTGT", 10, 0, 8, 8);            // target on the north edge
            var xShot = MkE("XSHOT", 2, 0);                 // shooter due WEST (v1 = +x)
            var xAlly = MkE("XALLY", 10, 10);               // ally due SOUTH at Euclid dist 10
            xAlly.Weapon = Weapon.Make(WeaponKind.Shotgun); // MaxRange 8 — the old planner gate
            xAlly.Ammo = xAlly.Weapon.Clip;
            Players.Add(xTgt); Enemies.Add(xShot); Enemies.Add(xAlly);
            var savedAll = Combat.AllUnits;
            Combat.AllUnits = new System.Collections.Generic.List<Unit> { xTgt, xShot, xAlly };
            // (a) 10-tile SHOTGUN ally: inside the resolver's CrossfireAllyRange (10), outside the
            // ally's own weapon range (8). The resolver pays the bonus; the pre-pin planner
            // (min-with-weapon-range) predicted none — the exact drift W6a closes.
            bool res = Combat.InCrossfire(Grid, xShot, xTgt);
            bool pln = Ai.CrossfireWith(this, xShot, xShot.X, xShot.Y, xTgt);
            if (!res) fails.Add("xfPinResolverShouldPay");
            if (pln != res) fails.Add("xfPinShotgunAllyDrift");
            // (b) DORMANT pod-mate: InCrossfire counts every alive same-team gun regardless of
            // alertness; the pre-pin planner skipped !Active allies. Agreement must hold.
            xAlly.X = 10; xAlly.Y = 5; xAlly.SyncPos();     // dist 5 — well inside every gate
            xAlly.Alert = AlertLevel.Unaware;               // dormant
            res = Combat.InCrossfire(Grid, xShot, xTgt);
            pln = Ai.CrossfireWith(this, xShot, xShot.X, xShot.Y, xTgt);
            if (!res) fails.Add("xfPinResolverDormantShouldPay");
            if (pln != res) fails.Add("xfPinDormantAllyDrift");
            // (c) beyond CrossfireAllyRange: both sides must refuse (agreement on the negative).
            // Geometry keeps the ANGLE valid (cos ~0.196 < 0.30) so range is the sole
            // discriminator: target (3,0), shooter due EAST (16,0), ally (5,10) at ~10.2 tiles.
            xAlly.Alert = AlertLevel.Alert;
            xTgt.X = 3;  xTgt.Y = 0;  xTgt.SyncPos();
            xShot.X = 16; xShot.Y = 0; xShot.SyncPos();
            xAlly.X = 5; xAlly.Y = 10; xAlly.SyncPos();
            res = Combat.InCrossfire(Grid, xShot, xTgt);
            pln = Ai.CrossfireWith(this, xShot, xShot.X, xShot.Y, xTgt);
            if (res) fails.Add("xfPinResolverOverRange");
            if (pln != res) fails.Add("xfPinOverRangeDrift");
            Combat.AllUnits = savedAll;                     // never leak the staged roster
        }

        // ---- W6b — COORDINATION TIER: data rows, damp identity/cap, tier-0 invariance,
        // tier-2 divergence, and the stale-tier lifecycle. ----
        {
            // (a) data: the ladder's AiTier rungs. W6 (SIGNAL) re-pin: tier 1 now arrives at
            // ELITE CADRE (rung 4) — the mid-ladder qualitative tooth — not first at EXPOSED;
            // rungs 4-7 hold tier 1 (Math.Max aggregation) and NO QUARTER stays the tier-2 apex.
            if (Sightline.Heat.AiTier(0) != 0 || Sightline.Heat.AiTier(3) != 0) fails.Add("aiTierLowHeatNot0");
            if (Sightline.Heat.AiTier(4) != 1 || Sightline.Heat.AiTier(5) != 1) fails.Add("aiTierEliteCadreNot1");
            if (Sightline.Heat.AiTier(6) != 1 || Sightline.Heat.AiTier(7) != 1) fails.Add("aiTierExposedNot1");
            if (Sightline.Heat.AiTier(8) != 2) fails.Add("aiTierNoQuarterNot2");
            // W6c data pin: +1 enemy damage is the rung-8 apex ONLY (0 through RELENTLESS, so
            // heats 0-7 spawn today's weapons byte-for-byte; the default param keeps every
            // harness Mission.Build call at 0).
            if (Sightline.Heat.DmgDelta(7) != 0) fails.Add("dmgDeltaBelowApexNot0");
            if (Sightline.Heat.DmgDelta(8) != 1) fails.Add("dmgDeltaNoQuarterNot1");

            // (b) the smoke/flash damp read: tier 0 == the shipped constants EXACTLY; tier 2
            //     rises but is CAPPED at 75 (never certainty) and never lifts a roll already
            //     at/above the cap (those are strong-reason/identity rolls, not knobs).
            Ai.Tier = 0;
            if (Ai.Damp(55) != 55 || Ai.Damp(70) != 70 || Ai.Damp(80) != 80 || Ai.Damp(90) != 90)
                fails.Add("dampTier0NotIdentity");
            Ai.Tier = 2;
            if (Ai.Damp(55) != 75 || Ai.Damp(70) != 75) fails.Add("dampTier2NotCapped75");
            if (Ai.Damp(80) != 80 || Ai.Damp(90) != 90) fails.Add("dampTier2LiftedStrongRoll");
            Ai.Tier = 0;

            // (c) one fixed scene, three plans. A PINNED enemy (ringed by dormant pod-mates ->
            //     single-tile reach, jitter-proof) sees two exposed soldiers: the squad FOCUS at
            //     full HP (dist 8) and a NON-focus in the finish band (dist 2, hp 2) whose
            //     intrinsic value beats the tier-0 focus bias by ~5pts but LOSES to the tier-2
            //     bias (30 -> 40). Tier 0 must take the opportunistic finish — today's shipped
            //     behaviour, asserted TWICE around a tier flip so the tier reads are proven
            //     pure/hysteresis-free — while Tier 2 must converge on the squad's focus (the
            //     more coordinated play; the focus-bias read demonstrably changed).
            Grid = new Grid();
            Players = new System.Collections.Generic.List<Unit>();
            Enemies = new System.Collections.Generic.List<Unit>();
            var tFoc = MkP("TFOC", 13, 5, 8, 8);            // the squad focus: full HP, dist 8
            var tFin = MkP("TFIN", 5, 3, 2, 8);             // finishable: hp 2 <= DmgMax 5, dist 2
            Players.Add(tFoc); Players.Add(tFin);
            var pinned = MkE("PIN", 5, 5);
            Enemies.Add(pinned);
            for (int bd = 0; bd < 8; bd++)                  // ring of DORMANT pod-mates pins it in place
            {
                int[] bdx = { -1, 0, 1, -1, 1, -1, 0, 1 }, bdy = { -1, -1, -1, 0, 0, 1, 1, 1 };
                var blk = MkE("BLK" + bd, 5 + bdx[bd], 5 + bdy[bd]);
                blk.Alert = AlertLevel.Unaware;             // dormant: pins movement, no active AI terms
                Enemies.Add(blk);
            }
            _aiUnits = AliveEnemies().Where(e => e.Active).ToList();
            PlanEnemySquad();
            EnemyFocus = tFoc;                              // stage the coordination conflict
            Ai.Tier = 0;
            var t0a = Ai.Plan(this, pinned);
            if (t0a.ShootTarget != tFin) fails.Add("tier0NotShippedPick=" + (t0a.ShootTarget?.Name ?? "null"));
            Ai.Tier = 2;
            EnemyFocus = tFoc;                              // (Plan never mutates it; explicit for clarity)
            var t2 = Ai.Plan(this, pinned);
            if (t2.ShootTarget != tFoc) fails.Add("tier2NoConvergence=" + (t2.ShootTarget?.Name ?? "null"));
            Ai.Tier = 0;
            var t0b = Ai.Plan(this, pinned);
            if (t0b.ShootTarget != t0a.ShootTarget || t0b.Path.Count != t0a.Path.Count
                || t0b.MoveActions != t0a.MoveActions || t0b.Overwatch != t0a.Overwatch
                || t0b.Hunker != t0a.Hunker || t0b.Grenade != t0a.Grenade || t0b.UseItem != t0a.UseItem)
                fails.Add("tier0NotInvariantAfterFlip");

            // (d) lifecycle: a stale tier survives neither mission SETUP (SetupMission publishes
            //     unconditionally — default heat 0 -> tier 0) nor the Combat.EndMission mirror.
            Ai.Tier = 2;
            StartMission(1);                                // NoPersist; no SIGHTLINE_HEAT -> heat 0
            if (Ai.Tier != 0) fails.Add("staleTierSurvivedSetup=" + Ai.Tier);
            Ai.Tier = 2;
            Combat.EndMission(null);
            if (Ai.Tier != 0) fails.Add("staleTierSurvivedEndMission=" + Ai.Tier);
        }

        return fails.Count == 0
            ? "AITEST: PASS (focus picks killable+exposed; Ai.Plan biases to focus + flips; overwatch map mirrors reaction; retreat plan still acts; W4 setup-first + live focus recompute; W6a commanding climb+shot + crossfire planner==resolver pin; W6b tier data/damp-cap/tier-0-invariance/tier-2-convergence/lifecycle)"
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
        // W12: pin the offered pair (the roll is clock-seeded) so the shot is reproducible and
        // exercises both delta-line shapes: LOCK-ON (conditional aim) + TANK (flat before>after).
        if (_run.PendingPerks.Count > 0)
        { _run.PendingPerks[0].A = Perk.LockOn; _run.PendingPerks[0].B = Perk.Tank; }
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
    /// W12 extremes staging (pair with SIGHTLINE_CAMPAIGN=1):
    ///   SIGHTLINE_ROSTER=<n>  grows the squad to n soldiers (recruits, name-deduped) so the
    ///                         barracks panel's WORST-CASE height (6 rows) can be screenshot;
    ///   SIGHTLINE_REPORT=<n>  pads the debrief to n report lines (the 5-line display cap).
    public void DebugCampaignMap()
    {
        _run.JumpTo(3);                  // visit cols 0-2; current sits at mission 3
        _run.DebriefSurvivors();
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_ROSTER"), out int nRoster))
        {
            var taken = new HashSet<string>();
            foreach (var u in _run.Squad) taken.Add(u.Name);
            while (_run.Squad.Count < Math.Min(nRoster, 6))
            {
                var rec = Mission.MakeRecruit(taken);
                taken.Add(rec.Name);
                rec.Benched = _run.Deployed.Count >= _run.NextDeployCap;   // stay inside the deploy cap
                _run.Squad.Add(rec);
            }
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_REPORT"), out int nReport))
            while (_run.Report.Count < nReport)
                _run.Report.Add($"Field exercise {_run.Report.Count + 1} logged  (harness filler line)");
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

    /// Harness hook (screenshot only, APEX W7): stage LAST STAND's mid-stand FIELD PROMOTION
    /// offer. Pair with SIGHTLINE_ENDLESS=1 + SIGHTLINE_SHOT — runs after BeginEndless: banks
    /// promotion kills on the point soldier, clears the wave-3 board, and fires CheckEndless so
    /// the heartbeat queues the offers and detours into Phase.Barracks. Shot mode never sets
    /// AutoPlay, so the perk chooser stays on screen for the shot frame (exactly what a player
    /// sees between waves 3 and 4).
    public void DebugEndlessOffer()
    {
        if (Mode != GameMode.Endless) return;
        foreach (var e in Enemies) { e.Hp = 0; e.Alive = false; }   // wave "cleared"
        Wave = 3;
        _run.Squad[0].Kills = 3;         // ROOKIE -> CORPORAL: perk picks + the spec fork queue
        CheckEndless();                  // sustain + heartbeat -> Barracks detour w/ the offer up
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
    /// W9 re-stage: the target is HUNKERED behind HIGH cover (full -40 facing-side cover on a
    /// dominant-axis shot, -25 hunker), so the shown HIT lands well under 40% — exercising the
    /// tooltip's red low-confidence band (>=70 good / 40-69 caution / <40 threat) inside the
    /// neutral PanelBd frame. The next Update's UpdateHoverAndAim recomputes + shows the odds
    /// naturally (no special draw path), so the screenshot matches real play.
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
            // Seat the foe at (+4,+2) — a dominant-x shot whose Bresenham line skirts the
            // facing cover tile — hunkered behind a HIGH-cover block on its west (facing) side.
            int fy = s.Y + 2 < Grid.H ? s.Y + 2 : s.Y - 2;
            int fx = Math.Min(Grid.W - 1, s.X + 4);
            if (Grid.InBounds(fx, fy))
            {
                // Clear the fire lane (floor, ground level) so nothing else warps the odds,
                // then stand the facing high-cover block back up beside the target.
                for (int tx = s.X; tx <= fx; tx++)
                    for (int ty = Math.Min(s.Y, fy); ty <= Math.Max(s.Y, fy); ty++)
                    {
                        var occ = UnitAt(tx, ty);
                        if (occ != null && occ != s) continue;   // don't pull terrain from under another unit
                        Grid.Tiles[tx, ty] = TileType.Floor;
                        Grid.Height[tx, ty] = 0;
                    }
                Grid.Tiles[fx - 1, fy] = TileType.HighCover;
                foe.X = fx; foe.Y = fy; foe.SyncPos();
                foe.Hunkered = true;
            }
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
        if (squad.Count > 1) squad[1].Nickname = "HALO";   // FUL-12 review: staging data had a stray leading space (rendered KRESS " HALO"); FullName's formatter is fine
        // a couple of fallen, recorded across the run for the memorial roll.
        _run.Memorial.Add(new FallenRec { Name = "DALES \"BISHOP\"", Cls = "RANGER",  Rank = "SERGEANT", Kills = 7, Mission = 2 });
        _run.Memorial.Add(new FallenRec { Name = "OKONKWO",        Cls = "GUNNER",  Rank = "CORPORAL", Kills = 4, Mission = 4 });
        _run.Memorial.Add(new FallenRec { Name = "VEGA \"ASH\"",    Cls = "ASSAULT", Rank = "ROOKIE",   Kills = 1, Mission = 5 });
        _run.Mission = lose ? 5 : Run.MaxMissions;
        if (lose) { LoseTitle = "RUN OVER"; LoseReason = "The squad fell on mission 5."; }
        // FUL-12: stage the meta-payoff FIELDS deterministically (AwardMetaRunEnd never runs under
        // NoPersist), so the SALVAGE slab / HEAT UNLOCKED line / achievement roll can be framed.
        // Fixed values -> the SUMMARY shot stays byte-stable; a lose card shows the consolation only.
        EndSalvage = lose ? 18 : 79;
        EndHeatUnlocked = lose ? 0 : 4;
        EndAchievements.Clear();
        if (!lose) { EndAchievements.Add("TURNING UP"); EndAchievements.Add("THE LONG WAR"); }   // HEAT3 + DEEP: both true of this staged run (3 KIA -> never FLAWLESS)
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
        // W10 (screenshot pin): guarantee the two NEW rule mods (BIPOD / SUPPRESSOR) are on the
        // demo slate regardless of the seed-rotated pick, so the dossier shot can be verified.
        // Demo-screen only (this hook fabricates intel/report already); live slates are untouched.
        var offer = ShopOffer();
        int bipodItem = ModBase + System.Array.IndexOf(WeaponModDef.All, WeaponMod.Bipod);
        int supItem   = ModBase + System.Array.IndexOf(WeaponModDef.All, WeaponMod.Suppressor);
        if (!offer.Contains(bipodItem)) offer.Add(bipodItem);
        if (!offer.Contains(supItem))   offer.Add(supItem);
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

    /// Headless self-test (SIGHTLINE_HEATLADDERTEST) — APEX W1: the ladder's top exists. A lone-VIP
    /// objective win (Escort VIP-to-evac, or Rescue freed-captive-to-evac) can clear a mission with
    /// EVERY soldier dead. Under a no-reinforcements regime (Heat RELENTLESS, rung 7, so heat >= 7;
    /// or CONTRACT IRON VETERANS) the debrief used to skip the AttritionFloor entirely, so the next
    /// mission built with an empty deploy and Mission.TryApplyLayout crashed indexing players[0].
    /// Asserts for all three seams that the debrief conscripts an emergency squad — at Count == 0
    /// ONLY (a surviving under-floor roster stays short; CONTRACTTEST pins that) — with the distinct
    /// report line, and that the next mission builds a non-empty deploy. Also pins the Mission.Build
    /// empty-deploy guard (defense-in-depth: refuse the layout, never throw).
    public string HeatLadderSelfTest()
    {
        NoPersist = true;
        var fails = new List<string>();

        // one seam: a lone-VIP win at mission 1, then build mission 2 and count the deploy
        void LoneVipWin(string tag, int heat, Contract contract, Objective obj)
        {
            _run = new Run(); _run.Start();
            _run.HeatLevel = heat;
            _run.Contract = contract;
            _run.CurrentCard = new MissionCard { Objective = obj, ModName = "STANDARD", Reward = RewardKind.None };
            SetupMission(1);
            if (obj == Objective.Rescue) CaptiveLocked = false;   // the captive was freed before the squad fell
            foreach (var u in Players.Where(p => !p.IsVip).ToList()) { u.Hp = 0; KillUnit(u); }
            Vip.X = EvacZone[0].x; Vip.Y = EvacZone[0].y;         // the asset walks out alone
            CheckEnd();                                            // objective win -> EnterBarracks -> DebriefSurvivors
            if (Phase != Phase.Barracks) { fails.Add($"{tag}:phase={Phase}"); return; }
            if (_run.Squad.Count == 0) { fails.Add($"{tag}:squadEmpty"); return; }
            if (_run.Squad.Count < Run.AttritionFloor) fails.Add($"{tag}:underFloor={_run.Squad.Count}");
            if (!_run.Report.Any(r => r.Contains("SHATTERED COMMAND"))) fails.Add($"{tag}:noConscriptLine");
            // the next mission must field a real squad (this build crashed before the fix)
            _run.CurrentCard = new MissionCard { Objective = Objective.Eliminate, ModName = "STANDARD", Reward = RewardKind.None };
            SetupMission(2);
            if (Players.Count(p => p.Alive && !p.IsVip) == 0) fails.Add($"{tag}:emptyDeploy");
        }

        LoneVipWin("heat8", 8, Contract.None, Objective.Escort);          // RELENTLESS via the heat ladder
        LoneVipWin("iron", 0, Contract.IronVeterans, Objective.Escort);   // same regime via the contract
        LoneVipWin("rescue", 8, Contract.None, Objective.Rescue);         // the lone-captive variant

        // defense-in-depth: Mission.Build with an EMPTY deploy must refuse the authored layout
        // (players[0] flood) and fall through the guarded procedural path without throwing.
        int savedLayout = Mission.ForcedLayout;
        try
        {
            Mission.ForcedLayout = 0;   // force the TryApplyLayout path
            Mission.Build(new Grid(), new List<Unit>(), new List<Unit>(), 2);
        }
        catch (Exception e) { fails.Add("emptyBuildThrew:" + e.GetType().Name); }
        finally { Mission.ForcedLayout = savedLayout; }

        // W6c behavior pin (review MED-2): the NO QUARTER +1 damage must survive the whole
        // SetupMission -> Build -> SpawnEnemies thread, not just the Heat.DmgDelta data row —
        // a Build/SpawnEnemies signature reshuffle that drops the mutation would stay green otherwise.
        // Mission 3: past the m1-2 grace that zeroes heatDmg.
        void DmgAtHeat(string tag, int heat, int expectDelta)
        {
            _run = new Run(); _run.Start();
            _run.HeatLevel = heat;
            _run.CurrentCard = new MissionCard { Objective = Objective.Eliminate, ModName = "STANDARD", Reward = RewardKind.None };
            SetupMission(3);
            foreach (var e in Enemies)
            {
                int baseMax = Weapon.Make(e.Weapon.Kind).DmgMax;
                if (e.Weapon.DmgMax != baseMax + expectDelta)
                { fails.Add($"{tag}:{e.Cls}dmg={e.Weapon.DmgMax}want={baseMax + expectDelta}"); return; }
            }
        }
        DmgAtHeat("noQuarterDmg", 8, 1);   // apex: every spawned weapon carries the +1
        DmgAtHeat("heat7Dmg", 7, 0);       // one rung below: untouched

        return fails.Count == 0
            ? "HEATLADDERTEST: PASS (lone-VIP wins at heat 8 / IRON VETERANS / Rescue conscript to the floor + next mission deploys; empty-deploy Build fails soft; NO QUARTER +1 dmg on every m3 weapon, none at heat 7)"
            : "HEATLADDERTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// SIGHTLINE_DKTEST — UNDERTOW W1: a death is processed EXACTLY ONCE. Asserts (1) KillUnit is
    /// idempotent (a 2nd call on a corpse does NOT re-add _run.Fallen/Memorial), and (2) a SURPLUS
    /// queued reaction ShotAnim aimed at a unit that just died is PURGED — so it can't re-resolve on
    /// the corpse and double-count Stats.RecordShot/CreditKill + replay the death FX — while the
    /// active blow and shots at OTHER targets are kept. This corrects the class-lethality telemetry
    /// the flywheel ranks. Needs a tiny window (Game uses tile math). Returns a one-line report.
    public string DoubleKillTest()
    {
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();
        _run = new Run(); _run.Start();
        SetupMission(1);

        // (1) idempotency: killing a soldier twice adds EXACTLY one Fallen + one Memorial entry.
        var s = Players.First(u => u.Alive && !u.IsVip);
        int fb = _run.Fallen.Count, mb = _run.Memorial.Count;
        s.Hp = 0; KillUnit(s);
        int fa1 = _run.Fallen.Count, ma1 = _run.Memorial.Count;
        KillUnit(s);                                         // corpse — must be a no-op
        int fa2 = _run.Fallen.Count, ma2 = _run.Memorial.Count;
        if (fa1 - fb != 1) fails.Add($"fallenFirst={fa1 - fb}");
        if (ma1 - mb != 1) fails.Add($"memorialFirst={ma1 - mb}");
        if (fa2 != fa1)    fails.Add($"fallenReKill={fa2 - fa1}");
        if (ma2 != ma1)    fails.Add($"memorialReKill={ma2 - ma1}");
        if (s.Alive)       fails.Add("soldierStillAlive");

        // (2) surplus-reaction purge: queue an ACTIVE reaction + a SURPLUS reaction both aimed at one
        // enemy, plus a reaction at a DIFFERENT enemy + a queued move for the dying enemy. KillUnit
        // keeps the active shot ([0]) + the other-target shot; drops the surplus corpse-shot + the
        // dead unit's queued move.
        var e  = Enemies.First(x => x.Alive);
        var e2 = Enemies.First(x => x.Alive && x != e);
        var a1 = Players.First(u => u.Alive && !u.IsVip);
        _anims.Clear();
        var res = Combat.Resolve(Grid, a1, e);
        Enqueue(new ShotAnim(a1, e,  res, reaction: true), Team.Player);  // [0] = ACTIVE (the killing blow)
        Enqueue(new ShotAnim(a1, e,  res, reaction: true), Team.Player);  // [1] = SURPLUS at the corpse
        Enqueue(new ShotAnim(a1, e2, res, reaction: true), Team.Player);  // [2] = shot at ANOTHER foe (keep)
        Enqueue(new MoveStepAnim(e, e.X, e.Y), Team.Player);             // dead unit's queued move (drop)
        e.Hp = 0; KillUnit(e);
        int shotsAtE  = _anims.Count(x => x is ShotAnim sh && sh.D == e);
        int shotsAtE2 = _anims.Count(x => x is ShotAnim sh && sh.D == e2);
        int movesForE = _anims.Count(x => x is MoveStepAnim mm && mm.Unit == e);
        if (shotsAtE  != 1) fails.Add($"shotsAtCorpse={shotsAtE}");       // only the active one survives
        if (shotsAtE2 != 1) fails.Add($"otherTargetShotDropped={shotsAtE2}");
        if (movesForE != 0) fails.Add($"deadMoveKept={movesForE}");
        _anims.Clear();

        // (3) APEX W2 — the overwatch RESOURCE LEAK: with 3 watchers covering one lane, the reaction
        // loop must stop SPENDING (OnOverwatch/ReactedThisTurn/Ammo) the moment the already-queued
        // hits cumulatively predict the mover's death — the old per-shot check only caught a single
        // lethal blow, so watcher #3 burned its watch + a round on a shot KillUnit would purge.
        // Controlled scene, fixed damage (DmgMin=DmgMax=3, crit 0) vs a 5 HP mover: two CONNECTS
        // predict death at watcher #2, so watcher #3 must stay armed. Reaction rolls are RNG (effHit
        // 99 via Reflexes; a clean miss survives the graze band), so re-stage a bounded number of
        // times until the first two reactions both connect (P≈0.98/attempt), then assert watcher #3's
        // state AT QUEUE TIME — right after OnUnitEnteredTile returns, before any ShotAnim applies.
        Grid = new Grid();
        Players = new System.Collections.Generic.List<Unit>();
        Enemies = new System.Collections.Generic.List<Unit>();
        Vip = null; CaptiveLocked = false; Hvt = null; SquadConcealed = false;
        Objective = Objective.Eliminate;
        Unit MkWatcher(string name, int x, int y)
        {
            var u = new Unit { Name = name, Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                               Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Weapon.DmgMin = 3; u.Weapon.DmgMax = 3; u.Weapon.CritBase = 0;   // fixed 3 dmg per connect
            u.Perks.Add(Perk.Reflexes);                                        // near-certain reaction (effHit 99)
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
        }
        var w1 = MkWatcher("W1", 4, 4);
        var w2 = MkWatcher("W2", 4, 5);
        var w3 = MkWatcher("W3", 4, 6);
        Players.Add(w1); Players.Add(w2); Players.Add(w3);
        var mv = new Unit { Name = "MV", Cls = "GRUNT", Team = Team.Enemy, X = 7, Y = 5,
                            Hp = 5, MaxHp = 5, Aim = 60, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
        mv.Ammo = mv.Weapon.Clip; mv.Alert = AlertLevel.Alert; mv.SyncPos(); mv.BeginTurn();
        Enemies.Add(mv);

        bool leakChecked = false;
        for (int attempt = 0; attempt < 60 && !leakChecked; attempt++)
        {
            foreach (var w in Players)
            { w.OnOverwatch = true; w.OwBrace = false; w.OwFocused = false; w.ReactedThisTurn = false; w.Ammo = w.Weapon.Clip; }
            mv.Hp = 5;
            _anims.Clear();
            OnUnitEnteredTile(mv);   // QUEUE TIME: no ShotAnim has applied yet (the mover is still at full HP)
            var queued = _anims.OfType<ShotAnim>().Where(s => s.D == mv).ToList();
            // judge only the case the spec pins: watchers #1 and #2 both CONNECT (3+3 >= 5 HP predicts
            // the death at #2's queued hit). Any miss in the first two re-rolls the scene.
            if (queued.Count >= 2 && queued[0].A == w1 && queued[1].A == w2 && queued[0].Res.Hit && queued[1].Res.Hit)
            {
                leakChecked = true;
                if (queued.Count != 2)          fails.Add($"owLeakShots={queued.Count}");   // #3 must not have fired
                if (!w3.OnOverwatch)            fails.Add("owLeakWatchSpent");
                if (w3.ReactedThisTurn)         fails.Add("owLeakReacted");
                if (w3.Ammo != w3.Weapon.Clip)  fails.Add($"owLeakAmmo={w3.Ammo}");
            }
        }
        if (!leakChecked) fails.Add("owLeakNoLethalPair");   // P(fail all 60 attempts) ~ 0.02^60: a real defect
        _anims.Clear();

        return fails.Count == 0
            ? "DKTEST: PASS (KillUnit idempotent; surplus corpse-reaction purged; active + other-target kept; 3rd watcher unspent once queued hits predict the kill)"
            : "DKTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// SIGHTLINE_RESCUETEST — APEX W2: the Rescue captive's cage is real. Asserts (1) the caged
    /// captive is ACTIONLESS at mission setup AND at the start-of-turn re-grant (it used to be fully
    /// player-controllable — an invulnerable unit that could walk itself to the squad and self-trigger
    /// its rescue), (1c — SIGNAL W4) the caged captive is truly INVULNERABLE to the source-less paths:
    /// TickHazards never ignites it and EnvDamage bounces (fire/shove could kill it into an
    /// unwinnable-unlosable soft-lock), (2) freeing it (TryFreeCaptive) restores actions + movement,
    /// (2b — SIGNAL W4) the freed captive is leash-owned (LeashVip's Rescue arm walks it; the caged
    /// state never moves), (3) the abandoned-cage soft-lock resolves: all soldiers dead while caged is
    /// an immediate CAPTIVE ABANDONED loss at mission 1, a checkpoint reinforcement redeploy at
    /// mission 3+, and an EndSkirmish(false) loss in SKIRMISH mode (which can roll Rescue). Tiny
    /// window (tile math). Returns a one-line report.
    public string RescueSelfTest()
    {
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();

        // (1) caged at setup: no actions (SetupMission BeginTurn loop zeroes the captive)
        _run = new Run(); _run.Start();
        _run.CurrentCard = new MissionCard { Objective = Objective.Rescue, ModName = "STANDARD", Reward = RewardKind.None };
        SetupMission(1);
        if (!CaptiveLocked) fails.Add("notLockedAtSetup");
        if (Vip == null) return "RESCUETEST: FAIL (noCaptiveSpawned)";
        if (Vip.CanAct) fails.Add("cagedCanActAtSetup");

        // (1b) the start-of-turn re-grant is denied too (StartPlayerTurn BeginTurn loop)
        StartPlayerTurn();
        if (Vip.CanAct) fails.Add("cagedCanActAtTurnStart");

        // (1c) W4 (SIGNAL) soft-lock: fire can NEVER cook the caged captive. TickHazards must not
        // ignite it (Burning skip), and the source-less damage funnel (EnvDamage) must refuse to
        // scratch it — a caged death has no reachable loss (CheckEnd's Rescue-loss test only sees a
        // freed asset... belt-and-braces aside) and used to soft-lock the mission.
        int cagedHp = Vip.Hp;
        Grid.LightFire(Vip.X, Vip.Y, Grid.FireTurns);
        TickHazards();
        if (Vip.HasStatus(StatusKind.Burning)) fails.Add("cagedIgnitedByHazard");
        Vip.AddStatus(StatusKind.Burning, 2);                  // force the status anyway: the DoT must still bounce
        TickStatuses(Vip);
        EnvDamage(Vip, 99, "BURN", Pal.RGBA(255, 140, 40));    // and the funnel itself refuses
        if (Vip.Hp != cagedHp) fails.Add($"cagedBurnedHp={Vip.Hp}vs{cagedHp}");
        if (!Vip.Alive) fails.Add("cagedCaptiveDied");
        Vip.Statuses.Clear();
        Grid.ClearHazards();

        // (2) freeing restores actions + movement (TryFreeCaptive -> Mobility 6 + Vip.BeginTurn)
        var sol = Players.First(p => p.Alive && !p.IsVip);
        bool seated = false;
        for (int dx = -1; dx <= 1 && !seated; dx++)
            for (int dy = -1; dy <= 1 && !seated; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = Vip.X + dx, ny = Vip.Y + dy;
                if (Grid.InBounds(nx, ny) && Grid.Tiles[nx, ny] == TileType.Floor && !IsOccupiedByOther(nx, ny, sol))
                { sol.X = nx; sol.Y = ny; sol.SyncPos(); seated = true; }
            }
        if (!seated) fails.Add("noFreeSeatByCage");
        TryFreeCaptive();
        if (CaptiveLocked) fails.Add("adjacentDidNotFree");
        if (!Vip.CanAct) fails.Add("freedStillActionless");
        if (Vip.Mobility <= 0) fails.Add($"freedNoMobility={Vip.Mobility}");

        // (2b) W4 (SIGNAL) — the FREED captive is LEASH-OWNED: LeashVip's new Rescue arm walks it
        // toward the squad via real MoveStepAnims (the Escort de-drag treatment), and the CAGED
        // state NEVER moves (the cage holds until a soldier springs it — the leash must not drag
        // the asset out of its own cage). Controlled all-floor scene (BEACONTEST's drain pattern);
        // stage (3a) below rebuilds a real mission, so trashing the scene here is safe.
        void Pump()
        {
            while (_anims.Count > 0)
            {
                var a = _anims[0]; a.OnStart(this);
                for (int i = 0; i < 200 && !a.Update(this, 0.05f); i++) { }
                if (_anims.Count > 0 && _anims[0] == a) _anims.RemoveAt(0);
            }
        }
        Grid = new Grid();
        Players = new System.Collections.Generic.List<Unit>();
        Enemies = new System.Collections.Generic.List<Unit>();
        Objective = Objective.Rescue;
        Mode = GameMode.Campaign;
        EvacZone.Clear();                               // no zone: the leash tags along to the soldier
        var walker = new Unit { Name = "S", Cls = "ASSAULT", Team = Team.Player, X = 14, Y = 5,
                                Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
        walker.Ammo = walker.Weapon.Clip; walker.SyncPos(); walker.BeginTurn();
        Vip = Mission.MakeVip(1); Vip.Name = "CAPTIVE"; Vip.X = 3; Vip.Y = 5; Vip.SyncPos(); Vip.BeginTurn();
        Players.Add(walker); Players.Add(Vip);
        _anims.Clear();
        CaptiveLocked = true;                           // still caged: the leash must HOLD it
        LeashVip(); Pump();
        if (!(Vip.X == 3 && Vip.Y == 5)) fails.Add("leashMovedCagedCaptive");
        CaptiveLocked = false;                          // freed: the leash walks it toward the squad
        int leash0 = Util.ChebyDist(Vip.X, Vip.Y, walker.X, walker.Y);
        LeashVip(); Pump();
        if (Util.ChebyDist(Vip.X, Vip.Y, walker.X, walker.Y) >= leash0) fails.Add("freedCaptiveNotLeashed");

        // (3a) campaign, mission 1: all soldiers dead while STILL caged -> immediate loss (the
        // checkpoint valve needs mission >= 3), with the distinct CAPTIVE ABANDONED cause.
        _run = new Run(); _run.Start();
        _run.CurrentCard = new MissionCard { Objective = Objective.Rescue, ModName = "STANDARD", Reward = RewardKind.None };
        SetupMission(1);
        foreach (var u in Players.Where(p => p.Alive && !p.IsVip).ToList()) { u.Hp = 0; KillUnit(u); }
        CheckEnd();
        if (Phase != Phase.Lose) fails.Add($"abandonNoLoss phase={Phase}");
        else if (LoseTitle != "CAPTIVE ABANDONED") fails.Add($"abandonTitle={LoseTitle}");

        // (3b) campaign, mission 3 (checkpoint fresh): the reinforcement redeploy fires instead —
        // the mission restarts with an emergency cadre and the captive re-caged.
        _run = new Run(); _run.Start();
        _run.CurrentCard = new MissionCard { Objective = Objective.Rescue, ModName = "STANDARD", Reward = RewardKind.None };
        SetupMission(3);
        foreach (var u in Players.Where(p => p.Alive && !p.IsVip).ToList()) { u.Hp = 0; KillUnit(u); }
        CheckEnd();
        if (!_run.CheckpointUsed) fails.Add("redeployNotFired");
        if (Phase != Phase.PlayerTurn) fails.Add($"redeployPhase={Phase}");
        if (Players.Count(p => p.Alive && !p.IsVip) == 0) fails.Add("redeployEmptySquad");
        if (!CaptiveLocked) fails.Add("redeployCageUnlatched");

        // (3c) SKIRMISH can roll Rescue: the same abandoned cage must end the mission as a loss
        // (single-mission modes have no checkpoint valve).
        BeginSkirmish(Objective.Rescue, 0);
        if (!CaptiveLocked) fails.Add("skirmishNotLocked");
        foreach (var u in Players.Where(p => p.Alive && !p.IsVip).ToList()) { u.Hp = 0; KillUnit(u); }
        CheckEnd();
        if (Phase != Phase.Lose) fails.Add($"skirmishAbandonPhase={Phase}");

        return fails.Count == 0
            ? "RESCUETEST: PASS (caged captive actionless at setup + turn start; fire/EnvDamage can't touch the cage; freeing restores actions/movement; freed captive leash-walks, caged never; abandoned cage = m1 loss, m3 checkpoint redeploy, skirmish loss)"
            : "RESCUETEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// SIGHTLINE_STAGGERTEST — UNDERTOW W2: the BRACE interrupt. Asserts (1) a BRACED watcher enqueues a
    /// STAGGER reaction while a plain watch does not, and (2) a braced reaction that HITS a surviving
    /// target zeroes its remaining actions this turn (its post-move offense is denied) + drops any held
    /// watch, non-lethally. Uses the anim-drain pump so ShotAnim.Apply actually runs. Tiny window for
    /// tile math. Returns a one-line report.
    public string StaggerSelfTest()
    {
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();
        _run = new Run(); _run.Start();
        SetupMission(1);

        var w = Players.First(u => u.Alive && !u.IsVip);
        var e = Enemies.First(x => x.Alive);
        e.X = w.X + 1; e.Y = w.Y; e.SyncPos(); e.Alert = AlertLevel.Alert;   // adjacent, clear LoS

        // (1) reaction-site branch: a BRACED watcher enqueues a Stagger-flagged reaction; a plain watch doesn't.
        w.OnOverwatch = true; w.OwBrace = true; w.OwFocused = false; w.ReactedThisTurn = false; w.Ammo = 5;
        _anims.Clear();
        OnUnitEnteredTile(e);
        var braceShot = _anims.OfType<ShotAnim>().FirstOrDefault(s => s.D == e);
        if (braceShot == null) fails.Add("noBraceReaction");
        else if (!braceShot.Stagger) fails.Add("braceReactionNotFlagged");

        w.OnOverwatch = true; w.OwBrace = false; w.OwFocused = false; w.ReactedThisTurn = false; w.Ammo = 5;
        _anims.Clear();
        OnUnitEnteredTile(e);
        var owShot = _anims.OfType<ShotAnim>().FirstOrDefault(s => s.D == e);
        if (owShot != null && owShot.Stagger) fails.Add("plainOverwatchStaggered");
        _anims.Clear();

        // (2) Apply effect: a braced reaction that HITS zeroes the surviving target's remaining actions this
        // turn + drops its watch (deterministic — forced-hit, small non-lethal damage).
        e.Hp = e.MaxHp; e.ActionsLeft = 2; e.OnOverwatch = true;
        var res = new ShotResult { Hit = true, Crit = false, Graze = false, Damage = 1 };
        Enqueue(new ShotAnim(w, e, res, reaction: true) { Stagger = true }, Team.Player);
        while (_anims.Count > 0)
        {
            var a = _anims[0]; a.OnStart(this);
            for (int i = 0; i < 200 && !a.Update(this, 0.05f); i++) { }
            if (_anims.Count > 0 && _anims[0] == a) _anims.RemoveAt(0);
        }
        if (e.ActionsLeft != 0) fails.Add($"notStaggered={e.ActionsLeft}");
        if (!e.Alive)          fails.Add("staggerKilledSurvivor");
        if (e.OnOverwatch)     fails.Add("staggerKeptWatch");

        return fails.Count == 0
            ? "STAGGERTEST: PASS (brace flags a disrupting reaction; plain watch doesn't; a hit zeroes the target's actions + drops its watch, non-lethally)"
            : "STAGGERTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// SIGHTLINE_MORALETEST — UNDERTOW W3: enemy pod MORALE / ROUT. On a controlled scene asserts:
    /// (1) killing one of a 2-unit pod ROUTS the survivor (BreakPodMorale threshold), (2) a routed unit
    /// shoots WILD (Combat aim penalty), (3) a routed unit FLEES (Ai.Plan moves it farther from the
    /// squad) and does NOT hold overwatch, (4) the rout RALLIES (Routed decays in BeginTurn). Returns a
    /// one-line report.
    public string MoraleSelfTest()
    {
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();
        _run = new Run(); _run.Start();          // KillUnit reads run state; enemy death doesn't touch Fallen but be safe

        // ---- controlled scene: empty 18x11 floor, no cover (LoS always clear) ----
        Grid = new Grid();
        Players = new System.Collections.Generic.List<Unit>();
        Enemies = new System.Collections.Generic.List<Unit>();
        Vip = null; CaptiveLocked = false; Hvt = null;
        Objective = Objective.Eliminate;
        EvacZone.Clear();

        Unit MkP(string name, int x, int y) {
            var u = new Unit { Name = name, Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                               Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
        }
        Unit MkE(string name, int x, int y, int pod) {
            var u = new Unit { Name = name, Cls = "GRUNT", Team = Team.Enemy, X = x, Y = y,
                               Hp = 6, MaxHp = 6, Aim = 60, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle), PodId = pod };
            u.Ammo = u.Weapon.Clip; u.Alert = AlertLevel.Alert; u.SyncPos(); u.BeginTurn(); return u;
        }

        var sol = MkP("SOL", 4, 5);
        Players.Add(sol);
        var e1 = MkE("E1", 8, 5, 0);
        var e2 = MkE("E2", 9, 5, 0);             // same pod 0, spawn size 2
        Enemies.Add(e1); Enemies.Add(e2);
        _podOrig.Clear(); _podOrig[0] = 2;

        // (1) killing one of the 2-unit pod routs the survivor
        e1.Hp = 0; KillUnit(e1);
        if (e2.Routed != RoutDuration) fails.Add($"survivorNotRouted={e2.Routed}");

        // (2) a routed unit shoots WILD (Combat aim penalty vs the same unit calm)
        int routedHit = Combat.ComputeOdds(Grid, e2, sol).HitChance;
        e2.Routed = 0;
        int calmHit = Combat.ComputeOdds(Grid, e2, sol).HitChance;
        e2.Routed = RoutDuration;                // restore for the flee check
        if (routedHit >= calmHit) fails.Add($"routNoAimPenalty r={routedHit} c={calmHit}");

        // (3) a routed unit FLEES (Ai.Plan moves it FARTHER from the soldier) and won't overwatch
        _aiUnits = AliveEnemies().Where(x => x.Active).ToList();
        PlanEnemySquad();
        var plan = Ai.Plan(this, e2);
        var dest = plan.Path.Count > 0 ? plan.Path[^1] : (x: e2.X, y: e2.Y);
        float distNow  = Util.TileDist(e2.X, e2.Y, sol.X, sol.Y);
        float distDest = Util.TileDist(dest.x, dest.y, sol.X, sol.Y);
        if (distDest <= distNow) fails.Add($"routedDidNotFlee now={distNow:0.0} dest={distDest:0.0}");
        if (plan.Overwatch) fails.Add("routedHeldOverwatch");

        // (4) rout RALLIES: BeginTurn counts Routed down
        int before = e2.Routed;
        e2.BeginTurn();
        if (e2.Routed != before - 1) fails.Add($"routDidNotDecay {before}->{e2.Routed}");

        // ---- SIGNAL W8: BANNER anchor / accelerated rally / WAVERING telegraph / CUSTODIAN ----

        // (5) a pod with a living WARBRINGER banner within Chebyshev BannerRange does NOT rout at
        // half strength (Game.BreakPodMorale skips held members)...
        var b1 = MkE("B1", 8, 8, 1); var b2 = MkE("B2", 9, 8, 1);
        Enemies.Add(b1); Enemies.Add(b2); _podOrig[1] = 2;
        var wb = new Unit { Name = "SIGNIFER", Cls = "WARBRINGER", Team = Team.Enemy, X = 11, Y = 8,
                            Hp = 8, MaxHp = 8, Aim = 56, Mobility = 5, Weapon = Weapon.Make(WeaponKind.Rifle) };
        wb.Ammo = wb.Weapon.Clip; wb.Alert = AlertLevel.Alert; wb.SyncPos(); wb.BeginTurn();
        Enemies.Add(wb);
        if (!wb.HasBanner) fails.Add("clsBannerFlagOff");           // capability defaults from Cls (W5 pattern)
        b1.Hp = 0; KillUnit(b1);
        if (b2.Routed != 0) fails.Add($"bannerDidNotHold={b2.Routed}");

        // (5b) ...and the SAME break OUT of aura range still routs (the anchor is spatial, not global)
        var c1 = MkE("C1", 2, 2, 2); var c2 = MkE("C2", 3, 2, 2);   // Cheby 8 from the banner
        Enemies.Add(c1); Enemies.Add(c2); _podOrig[2] = 2;
        c1.Hp = 0; KillUnit(c1);
        if (c2.Routed != RoutDuration) fails.Add($"outOfAuraNotRouted={c2.Routed}");

        // (6) a bannered rout rallies ONE TURN FASTER: the turn-boundary step (BeginEnemyUnitTurn)
        // decrements once normally, twice inside the aura.
        c2.Routed = RoutDuration;
        BeginEnemyUnitTurn(c2);                                     // out of aura: normal pace
        if (c2.Routed != RoutDuration - 1) fails.Add($"plainRallyPace={c2.Routed}");
        c2.Routed = RoutDuration; c2.X = 10; c2.Y = 8; c2.SyncPos();  // step inside the aura
        BeginEnemyUnitTurn(c2);
        if (c2.Routed != RoutDuration - 2) fails.Add($"bannerRallyNotFaster={c2.Routed}");

        // (7) WAVERING telegraph: pod 3 (orig 4, far from the banner) — not wavering at full
        // strength; EXACTLY one kill from the threshold flips PodWavering on; the breaking kill
        // routs the survivors and the tag drops (Routed>0 is excluded).
        var w1 = MkE("W1", 2, 9, 3); var w2 = MkE("W2", 3, 9, 3);
        var w3 = MkE("W3", 2, 10, 3); var w4 = MkE("W4", 3, 10, 3);
        Enemies.Add(w1); Enemies.Add(w2); Enemies.Add(w3); Enemies.Add(w4); _podOrig[3] = 4;
        if (PodWavering(w1)) fails.Add("waverAtFullStrength");
        w4.Hp = 0; KillUnit(w4);                                    // 3/4 alive -> next kill breaks
        if (!PodWavering(w1)) fails.Add("noWaverOneFromThreshold");
        w3.Hp = 0; KillUnit(w3);                                    // 2 <= 4/2 -> the pod breaks
        if (w1.Routed != RoutDuration) fails.Add($"waverPodDidNotBreak={w1.Routed}");
        if (PodWavering(w1)) fails.Add("waverTagOnRoutedPod");

        // (8) a banner-HELD pod at the waver point reads HELD, not WAVERING (the tag never lies:
        // PodAtWaverPoint stays true for the tooltip's POD n/m line, PodWavering false for the tag)
        var h1 = MkE("H1", 12, 8, 5); var h2 = MkE("H2", 12, 9, 5);
        Enemies.Add(h1); Enemies.Add(h2); _podOrig[5] = 2;
        if (!PodAtWaverPoint(h1)) fails.Add("heldPodNotAtWaverPoint");
        if (PodWavering(h1)) fails.Add("heldPodShowsWavering");

        // (9) CUSTODIAN: with a partially-hacked terminal, Ai.Plan returns a RelockTile plan for
        // an adjacent keeper, and executing it decrements HackProgress; a blown sabotage charge
        // re-arms the same way. Nothing left to undo -> CanRelock refuses (no below-zero lock).
        Objective = Objective.Hack;
        Terminal = (6, 5); HackProgress = 1;
        var cu = new Unit { Name = "SEXTON", Cls = "CUSTODIAN", Team = Team.Enemy, X = 7, Y = 5,
                            Hp = 5, MaxHp = 5, Aim = 48, Mobility = 6, Weapon = Weapon.Make(WeaponKind.Smg) };
        cu.Ammo = cu.Weapon.Clip; cu.Alert = AlertLevel.Alert; cu.SyncPos(); cu.BeginTurn();
        Enemies.Add(cu);
        _aiUnits = AliveEnemies().Where(x => x.Active).ToList();
        PlanEnemySquad();
        var cplan = Ai.Plan(this, cu);
        if (cplan.RelockTile == null) fails.Add("custodianNoRelockPlan");
        if (!CanRelock(cu, Terminal)) fails.Add("custodianCannotRelock");
        DoRelock(cu, Terminal);
        if (HackProgress != 0) fails.Add($"relockNoDecrement={HackProgress}");
        if (CanRelock(cu, Terminal)) fails.Add("relockBelowZero");

        Objective = Objective.Sabotage;
        SabotageSites = new System.Collections.Generic.List<(int x, int y)> { (7, 6), (12, 2) };
        SabotageBlown.Clear(); SabotageBlown.Add(0);
        var splan = Ai.Plan(this, cu);
        if (splan.RelockTile == null || splan.RelockTile.Value != (7, 6)) fails.Add("custodianNoRearmPlan");
        if (!CanRelock(cu, (7, 6))) fails.Add("custodianCannotRearm");
        DoRelock(cu, (7, 6));
        if (SabotageBlown.Count != 0) fails.Add("rearmDidNotClear");

        // (9b) W8 review — a ROUTED custodian does NOT work the objective: morale overrides the
        // specialist branch (it falls through to the generic loop and flees like everyone else,
        // so breaking the keeper's pod is a real answer to the objective pressure).
        SabotageBlown.Add(0);                                       // a re-armable site is available again
        cu.Routed = RoutDuration;
        var rplan = Ai.Plan(this, cu);
        if (rplan.RelockTile != null) fails.Add("routedCustodianStillWorks");
        cu.Routed = 0;

        // (10) W10 TERROR boon (redesigned per review): broken enemies stay broken LONGER — the
        // boon extends the rout DURATION assigned at the break (+Game.TerrorRoutBonus via
        // RoutDurationFor), never the threshold (real pods spawn size 2, where a threshold change
        // is arithmetic dead weight). W8's banner semantics must stay intact on top: out of aura
        // the extended rout still rallies one per own turn; inside the aura it still rallies at
        // DOUBLE pace — TERROR raises the base the banner recovers from, never the counter itself.
        var t1 = MkE("T1", 2, 4, 7); var t2 = MkE("T2", 3, 4, 7);   // far from the WARBRINGER at (11,8)
        Enemies.Add(t1); Enemies.Add(t2); _podOrig[7] = 2;
        _run.ActiveBoons.Add(Boon.Terror);
        t1.Hp = 0; KillUnit(t1);
        if (t2.Routed != RoutDuration + TerrorRoutBonus) fails.Add($"terrorRoutNotExtended={t2.Routed}");
        BeginEnemyUnitTurn(t2);                                     // out of aura: normal rally pace
        if (t2.Routed != RoutDuration + TerrorRoutBonus - 1) fails.Add($"terrorPlainRallyPace={t2.Routed}");
        t2.X = 10; t2.Y = 8; t2.SyncPos();                          // step inside the banner's aura
        BeginEnemyUnitTurn(t2);                                     // banner: double pace, on the RAISED base
        if (t2.Routed != RoutDuration + TerrorRoutBonus - 3) fails.Add($"terrorBannerRallyPace={t2.Routed}");
        // control: without the boon the same 2-pod break assigns exactly the BASE duration
        _run.ActiveBoons.Remove(Boon.Terror);
        var t3 = MkE("T3", 2, 6, 8); var t4 = MkE("T4", 3, 6, 8);
        Enemies.Add(t3); Enemies.Add(t4); _podOrig[8] = 2;
        t3.Hp = 0; KillUnit(t3);
        if (t4.Routed != RoutDuration) fails.Add($"terrorControlBase={t4.Routed}");

        return fails.Count == 0
            ? "MORALETEST: PASS (pod break routs survivor; routed flees + drops watch + shoots wild; rallies over turns; "
              + "W8: banner holds in-aura pods + doubles rally pace, WAVERING flags the one-kill-from-rout pod truthfully, "
              + "custodian plans + executes the re-lock/re-arm and stops when routed; "
              + "W10: TERROR extends the rout duration (+2), plain/banner rally pace intact)"
            : "MORALETEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// FUL-6 CRITICAL MASS self-test (SIGHTLINE_PODTEST). Six legs:
    ///  (a) PLAN PIN — PodPlan {7,8,9,12} -> {3,2,2}/{3,3,2}/{3,3,3}/{3,3,3,3}, no pod of 1
    ///      from any count >= 2; a built m3 force groups to plan sizes with _podOrig matching;
    ///      an m1 force stays i/2 pairs; a finale force stays i/2 (FUL11PROBE by construction).
    ///  (b) COHESION — every m3 pod's max intra-pod Chebyshev spread <= 3 as spawned.
    ///  (c) LINK — waking pod A links ONLY the nearest in-earshot pod B (Suspicious +
    ///      _linkedPods); ResolveSuspicion confirms B to Alert UNSEEN, never chains to C
    ///      (in earshot of B, out of earshot of A); an out-of-earshot pod never links.
    ///  (d) ARC — in a pod of 3, kill 1 -> survivors WAVERING (not routed); kill 2 -> rout.
    ///  (e) ENDLESS — wave bodies land in sub-pods with ids >= 100 and sealed _podOrig;
    ///      the injected elite stays PodId -1 (morale-exempt).
    ///  (f) FDR — a boon-held drag drills once (+2 half-steps MoveBudget, exactly one FDR
    ///      proc); no boon -> no grant; a second drag the same turn -> no second proc.
    public string PodSelfTest()
    {
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();

        // ---- (a) PLAN PIN: the pure split ----
        var pins = new (int n, int[] want)[]
        {
            (7, new[] { 3, 2, 2 }), (8, new[] { 3, 3, 2 }), (9, new[] { 3, 3, 3 }), (12, new[] { 3, 3, 3, 3 }),
            (2, new[] { 2 }), (3, new[] { 3 }), (4, new[] { 2, 2 }),
        };
        foreach (var (pn, pwant) in pins)
        {
            var got = Mission.PodPlan(pn);
            if (!got.SequenceEqual(pwant)) fails.Add($"plan{pn}=[{string.Join(",", got)}]");
        }
        for (int c = 2; c <= 12; c++)
        {
            var pl = Mission.PodPlan(c);
            if (pl.Sum() != c) fails.Add($"planSum{c}");
            if (pl.Any(s => s < 2)) fails.Add($"podOf1@{c}");
        }

        // ---- (a)+(b): built forces. m3 groups to plan sizes, _podOrig matches, pods clump. ----
        for (int slot = 0; slot < 3; slot++)
        {
            Util.Reseed(50000 + slot);
            var g = new Game { NoPersist = true, ForcedObjective = Objective.Eliminate };
            g.StartMission(3);
            var pods = g.Enemies.Where(e => e.PodId >= 0 && e.PodId < 100)
                                .GroupBy(e => e.PodId).OrderBy(gr => gr.Key).ToList();
            int nBodies = pods.Sum(gr => gr.Count());
            var plan = Mission.PodPlan(nBodies);
            if (pods.Count != plan.Length) fails.Add($"m3s{slot}podCount={pods.Count}(want{plan.Length})");
            else
                for (int p = 0; p < plan.Length; p++)
                    if (pods[p].Count() != plan[p]) fails.Add($"m3s{slot}pod{p}size={pods[p].Count()}(want{plan[p]})");
            foreach (var gr in pods)
            {
                if (g._podOrig.GetValueOrDefault(gr.Key) != gr.Count())
                    fails.Add($"m3s{slot}podOrig{gr.Key}={g._podOrig.GetValueOrDefault(gr.Key)}");
                int spread = 0;
                foreach (var a in gr) foreach (var b in gr)
                    spread = Math.Max(spread, Util.ChebyDist(a.X, a.Y, b.X, b.Y));
                if (spread > 3) fails.Add($"m3s{slot}pod{gr.Key}spread={spread}");
            }
        }
        {
            Util.Reseed(50001);
            var g = new Game { NoPersist = true, ForcedObjective = Objective.Eliminate };
            g.StartMission(1);                              // teaching tier: i/2 pairs exactly
            for (int i = 0; i < g.Enemies.Count; i++)
                if (g.Enemies[i].PodId != i / 2) { fails.Add("m1NotPairs"); break; }
        }
        {
            Util.Reseed(50002);
            var g = new Game { NoPersist = true };
            g.StartMission(6);                              // finale: i/2 EXACTLY (FUL-11 kit geometry)
            for (int i = 0; i < g.Enemies.Count; i++)
                if (g.Enemies[i].PodId != i / 2) { fails.Add("finaleNotIOver2"); break; }
        }

        // ---- controlled scene helpers (the MORALETEST staging pattern) ----
        Unit MkP(string name, int x, int y) {
            var u = new Unit { Name = name, Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                               Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
        }
        Unit MkE(string name, int x, int y, int pod, AlertLevel alert) {
            var u = new Unit { Name = name, Cls = "GRUNT", Team = Team.Enemy, X = x, Y = y,
                               Hp = 6, MaxHp = 6, Aim = 60, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle), PodId = pod };
            u.Ammo = u.Weapon.Clip; u.Alert = alert; u.SyncPos(); u.BeginTurn(); return u;
        }
        void FreshScene()
        {
            Grid = new Grid();
            Players = new System.Collections.Generic.List<Unit>();
            Enemies = new System.Collections.Generic.List<Unit>();
            Vip = null; CaptiveLocked = false; Hvt = null;
            Objective = Objective.Eliminate; EvacZone.Clear();
            SquadConcealed = false;
            _podOrig.Clear(); _linkedPods.Clear(); _anims.Clear();
        }

        // ---- (c) LINK: nearest-only, confirm-unseen, no chain ----
        _run = new Run(); _run.Start();
        _run.Mission = 3;                                   // the link is a mission-3+ rule
        FreshScene();
        var sol = MkP("SOL", 0, 0);                         // far from B: out of sight for the resolve
        Players.Add(sol);
        var a1 = MkE("A1", 4, 5, 90, AlertLevel.Unaware); var a2 = MkE("A2", 5, 5, 90, AlertLevel.Unaware);
        var b1 = MkE("B1", 10, 5, 91, AlertLevel.Unaware); var b2 = MkE("B2", 11, 5, 91, AlertLevel.Unaware);
        var c1 = MkE("C1", 9, 9, 92, AlertLevel.Unaware); var c2 = MkE("C2", 10, 9, 92, AlertLevel.Unaware);
        Enemies.AddRange(new[] { a1, a2, b1, b2, c1, c2 });
        _podOrig[90] = 2; _podOrig[91] = 2; _podOrig[92] = 2;
        // geometry: B's closest member is TileDist 5.0 from A's; C's is 5.66 from A's (also in
        // earshot!) and 4.0 from B's — the NEAREST-only rule must pick B, and the resolve must
        // not chain B -> C even though C is well inside B's earshot.
        ActivatePod(90);
        if (Enemies.Where(e => e.PodId == 90).Any(e => e.Alert != AlertLevel.Alert)) fails.Add("linkPodANotAwake");
        if (b1.Alert != AlertLevel.Suspicious || b2.Alert != AlertLevel.Suspicious) fails.Add($"linkBNotSuspicious={b1.Alert}/{b2.Alert}");
        if (!_linkedPods.Contains(91)) fails.Add("linkBNotFlagged");
        if (c1.Alert != AlertLevel.Unaware || c2.Alert != AlertLevel.Unaware) fails.Add("linkCWoken(nearest-only broke)");
        if (_linkedPods.Contains(92)) fails.Add("linkCFlagged");
        ResolveSuspicion();                                 // no soldier in sight of B (dist ~11 > SightRange 9)
        if (b1.Alert != AlertLevel.Alert || b2.Alert != AlertLevel.Alert) fails.Add($"linkBNotConfirmedUnseen={b1.Alert}/{b2.Alert}");
        if (c1.Alert != AlertLevel.Unaware || c2.Alert != AlertLevel.Unaware) fails.Add("linkChainedToC");
        if (_linkedPods.Count != 0) fails.Add("linkSetNotCleared");
        // second scene: a pod OUT of earshot (7.3 > LinkRange 6) never links
        FreshScene();
        Players.Add(sol);
        var d1 = MkE("D1", 4, 5, 93, AlertLevel.Unaware); var d2 = MkE("D2", 5, 5, 93, AlertLevel.Unaware);
        var f1 = MkE("F1", 12, 7, 94, AlertLevel.Unaware); var f2 = MkE("F2", 13, 7, 94, AlertLevel.Unaware);
        Enemies.AddRange(new[] { d1, d2, f1, f2 });
        _podOrig[93] = 2; _podOrig[94] = 2;
        ActivatePod(93);
        if (f1.Alert != AlertLevel.Unaware || f2.Alert != AlertLevel.Unaware) fails.Add("outOfEarshotLinked");
        if (_linkedPods.Count != 0) fails.Add("outOfEarshotFlagged");

        // ---- (d) ARC: pods of 3 give morale its full waver -> rout arc ----
        FreshScene();
        Players.Add(sol);
        var w1 = MkE("W1", 10, 3, 95, AlertLevel.Alert);
        var w2 = MkE("W2", 11, 3, 95, AlertLevel.Alert);
        var w3 = MkE("W3", 10, 4, 95, AlertLevel.Alert);
        Enemies.AddRange(new[] { w1, w2, w3 });
        DebugPodOrig(95, 3);
        if (PodWavering(w1)) fails.Add("arcWaverAtFull");
        w3.Hp = 0; KillUnit(w3);                            // 2 of 3 alive: one kill from the threshold
        if (!PodWavering(w1) || !PodWavering(w2)) fails.Add("arcNoWaverAt2of3");
        if (w1.Routed != 0 || w2.Routed != 0) fails.Add("arcRoutedEarly");
        w2.Hp = 0; KillUnit(w2);                            // 1 <= 3/2 -> the survivor breaks
        if (w1.Routed <= 0) fails.Add($"arcSurvivorNotRouted={w1.Routed}");

        // ---- (e) ENDLESS: wave sub-pods 100+ sealed; the elite stays -1 ----
        {
            var g = new Game { NoPersist = true };
            g._run = new Run(); g._run.Start(); g._run.HeatLevel = 0;
            g.Mode = GameMode.Endless;
            g.Players = g._run.Squad;
            g.Wave = 0;
            g.SetupMission(1);                              // arena + wave 1 (the HORDETEST staging)
            var alive = g.AliveEnemies();
            if (alive.Count == 0) fails.Add("endlessWaveEmpty");
            if (alive.Any(e => e.PodId < 100)) fails.Add("endlessBodyBelow100");
            var wavePods = alive.GroupBy(e => e.PodId).ToList();
            foreach (var gr in wavePods)
                if (g._podOrig.GetValueOrDefault(gr.Key) != gr.Count())
                    fails.Add($"endlessPodOrig{gr.Key}={g._podOrig.GetValueOrDefault(gr.Key)}(want{gr.Count()})");
            var sizes = wavePods.Select(gr => gr.Count()).OrderByDescending(s => s).ToList();
            var wantSizes = Mission.PodPlan(alive.Count).OrderByDescending(s => s).ToList();
            if (!sizes.SequenceEqual(wantSizes)) fails.Add($"endlessSizes=[{string.Join(",", sizes)}]");
            g.SpawnEndlessElite(5);
            var elite = g.AliveEnemies().FirstOrDefault(e => e.Cls == "ELITE");
            if (elite == null) fails.Add("endlessNoElite");
            else if (elite.PodId != -1) fails.Add($"elitePodJoined={elite.PodId}");
        }

        // ---- (f) FDR: the drill grant (proc-at-grant, once per soldier per turn) ----
        _run = new Run(); _run.Start();                     // fresh boons (none held)
        FreshScene();
        var dr = MkP("DRAGGER", 5, 5);
        var m1 = MkP("MATE1", 7, 5);                        // Cheby 2 -> legal drag, lands (6,5)
        var m2 = MkP("MATE2", 3, 5);                        // Cheby 2 -> legal drag, lands (4,5)
        Players.AddRange(new[] { dr, m1, m2 });
        bool statsWere = Stats.Enabled; Stats.Enabled = true;
        int p0 = Stats.ProcCount("FDR");
        Selected = dr;
        IssueDrag(m1);                                      // NO boon: a plain drag must not drill
        if (dr.DrilledThisTurn) fails.Add("fdrDrillWithoutBoon");
        if (Stats.ProcCount("FDR") != p0) fails.Add("fdrProcWithoutBoon");
        _run.ActiveBoons.Add(Boon.FieldDrills);
        Combat.RunBoons.Add(Boon.FieldDrills);              // publish (FieldCraftLimit reads the static)
        foreach (var u in Players) u.BeginTurn();           // fresh turn (Drags/DrilledThisTurn reset)
        _anims.Clear();
        int mb0 = dr.MoveBudget;
        Selected = dr;
        IssueDrag(m2);                                      // boon held: first drag drills
        if (!dr.DrilledThisTurn) fails.Add("fdrNoDrill");
        if (Stats.ProcCount("FDR") != p0 + 1) fails.Add($"fdrProcs={Stats.ProcCount("FDR") - p0}(want1)");
        if (dr.MoveBudget != mb0 + 2) fails.Add($"fdrBudget={dr.MoveBudget}(want{mb0 + 2})");
        m1.X = 7; m1.Y = 7; m1.SyncPos();                   // give the SECOND drag a legal target
        IssueDrag(m1);                                      // limit 2/turn: legal — but no second proc
        if (dr.DragsThisTurn != 2) fails.Add($"fdrSecondDragBlocked={dr.DragsThisTurn}");
        if (Stats.ProcCount("FDR") != p0 + 1) fails.Add("fdrDoubleProc");
        if (dr.MoveBudget != mb0 + 2) fails.Add("fdrDrillStacked");
        dr.BeginTurn();
        if (dr.DrilledThisTurn) fails.Add("fdrDrillPersistedTurn");
        Stats.Enabled = statsWere;

        return fails.Count == 0
            ? "PODTEST: PASS (plan pins + m3 groups/cohesion + m1/finale i/2; link nearest-only, confirms unseen, "
              + "never chains, earshot-gated; 3-pod waver->rout arc; endless wave sub-pods 100+ sealed, elite exempt; "
              + "FIELD DRILLS drills once at the grant site)"
            : "PODTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// FUL-6 harness hook (screenshot only; pair with SIGHTLINE_MISSION=3): stage the CRITICAL
    /// MASS reads — pods land as visible 3-clumps (the m3+ PodPlan spawn), and waking one pod
    /// through the REAL ActivatePod fires the linked-activation rider, so the frame shows the
    /// woken pod + the "!"-telegraphed linked pod (HEARD THE GUNS pop, CONTACT! banner + sub).
    /// Picks the dormant pod with the smallest closest-member gap to another dormant pod
    /// (<= LinkRange) so a link is guaranteed to fire on any seed that allows one.
    public void DebugPodShot()
    {
        SquadConcealed = false;                 // the wake must not be masked by squad stealth
        int bestPod = -1; float best = float.MaxValue;
        foreach (var a in Enemies)
        {
            if (!a.Alive || a.PodId < 0 || a.Alert != AlertLevel.Unaware) continue;
            foreach (var b in Enemies)
            {
                if (!b.Alive || b.PodId < 0 || b.PodId == a.PodId || b.Alert != AlertLevel.Unaware) continue;
                float d = Util.TileDist(a.X, a.Y, b.X, b.Y);
                if (d <= LinkRange && d < best) { best = d; bestPod = a.PodId; }
            }
        }
        if (bestPod >= 0) ActivatePod(bestPod);
        else if (Enemies.Count > 0) ActivatePod(Enemies[0].PodId);   // no linkable pair on this seed
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

        // (a) ContractDef wiring + append-only ordinals. FUL-10: the tail advanced Spearhead ->
        // LivingLegends; Spearhead's ordinal POSITION [3] is pinned so a mid-enum insertion fails too.
        if (ContractDef.All.Length != 5) fails.Add("allCount");
        if (System.Array.IndexOf(ContractDef.All, Contract.None) >= 0) fails.Add("allHasNone");
        var cv = (Contract[])Enum.GetValues(typeof(Contract));
        if (cv.Length < 6 || cv[0] != Contract.None || cv[3] != Contract.Spearhead
            || cv[^1] != Contract.LivingLegends) fails.Add("ordinals");
        if (ContractDef.Name(Contract.IronVeterans) != "IRON VETERANS") fails.Add("name");
        if (ContractDef.Code(Contract.HighStakes) != "HST") fails.Add("code");
        if (ContractDef.Name(Contract.MercenaryClause) != "MERCENARY CLAUSE"
            || ContractDef.Code(Contract.LivingLegends) != "LGD") fails.Add("ful10Names");
        if (ContractDef.Parse("mrc") != Contract.MercenaryClause
            || ContractDef.Parse("lgd") != Contract.LivingLegends) fails.Add("ful10Parse");

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

        // (e) FUL-10 LGD: CreditKill credits DOUBLE toward rank; None stays single (inertness).
        {
            var gN = new Game { NoPersist = true }; gN.StartMission(1);
            var uN = gN.Players.Find(p => p.Alive && !p.IsVip);
            int k0 = uN.Kills; gN.CreditKill(uN);
            if (uN.Kills != k0 + 1) fails.Add("lgd:noneKills=" + (uN.Kills - k0));
            var gL = new Game { NoPersist = true }; gL.StartMission(1);
            gL.RunState.Contract = Contract.LivingLegends;
            var uL = gL.Players.Find(p => p.Alive && !p.IsVip);
            int k1 = uL.Kills; gL.CreditKill(uL);
            if (uL.Kills != k1 + 2) fails.Add("lgd:doubleKills=" + (uL.Kills - k1));
            if (uL.KillsThisTurn != 1) fails.Add("lgd:featDoubled");   // feats stay single-credit

            // (f) FUL-10 MRC: the per-veteran recall fee halves (round up) EXACTLY once, only while
            // MERCENARY CLAUSE is the draft selection; deselecting restores full price. Pure — no disk.
            var vet = new Unit { Name = "VR", Cls = "ASSAULT", Team = Team.Player, Rank = 3, Alive = true,
                                 Weapon = Weapon.Make(WeaponKind.Rifle), FromReserve = true };
            gL.DraftPicked = new HashSet<Unit> { vet };
            gL.DraftSelectedContract = null;
            if (gL.DraftRecallFee(vet) != 34 || gL.DraftRecallCost != 34) fails.Add("mrc:fullPrice=" + gL.DraftRecallCost);
            gL.DraftSelectedContract = Contract.MercenaryClause;
            if (gL.DraftRecallFee(vet) != 17 || gL.DraftRecallCost != 17) fails.Add("mrc:half=" + gL.DraftRecallCost);
            gL.DraftSelectedContract = Contract.IronVeterans;   // any other contract: full price
            if (gL.DraftRecallCost != 34) fails.Add("mrc:leaked=" + gL.DraftRecallCost);
        }

        // (g) FUL-10 orphaned-perk fix: the ClassLine table's own doc rule ("every perk appears in
        // >=1 line") must be TRUE — enumerated, not asserted by comment.
        var orphans = Run.PerksInNoClassLine();
        if (orphans.Count != 0) fails.Add("perkOrphans:" + string.Join("/", orphans));

        return fails.Count == 0
            ? "CONTRACTTEST: PASS (None inert; IronVeterans no-backfill+fast-rank; HighStakes no-heal; "
              + "LGD double kill credit; MRC half-price recall; class lines cover every perk)"
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
    /// NewRunSquad (the harness path is unchanged); (d) APEX W5 callsign dedup — no duplicate
    /// names inside a generated pool, across the squad after a barracks backfill, or from
    /// MakeRecruit under an explicit taken-names set (dup names silently merged bond/memorial
    /// records before W5). Window-free (no Raylib, no disk).
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

        // (d) APEX W5 — callsign dedup across pool + squad + backfill.
        // A fresh pool must carry DraftPoolSize DISTINCT names (GenerateDraftPool threads its
        // taken-names set through every MakeRecruit).
        var pool3 = Run.GenerateDraftPool();
        var poolNames = new HashSet<string>();
        foreach (var u in pool3) if (!poolNames.Add(u.Name)) fails.Add($"dupPoolName:{u.Name}");
        // Draft a squad from it, wipe it down to one survivor, and run the barracks debrief:
        // the emergency-floor + trickle backfill must never draft a name already on the squad.
        var run3 = new Run();
        var picked3 = new List<Unit>();
        for (int i = 0; i < DraftCap && i < pool3.Count; i++) picked3.Add(pool3[i]);
        run3.Start(picked3);
        while (run3.Squad.Count > 1) run3.Squad.RemoveAt(run3.Squad.Count - 1);   // simulate casualties
        run3.DebriefSurvivors();                                                  // backfills via TakenCallsigns()
        if (run3.Squad.Count < 2) fails.Add($"backfillCount={run3.Squad.Count}(want>=2)");
        var squadNames = new HashSet<string>();
        foreach (var u in run3.Squad) if (!squadNames.Add(u.Name)) fails.Add($"dupSquadName:{u.Name}");
        // Direct taken-set contract: 20 sequential recruits against a growing set stay distinct
        // (40-callsign pool, so the bounded re-roll has plenty of headroom).
        var taken = new HashSet<string>(squadNames);
        for (int i = 0; i < 20; i++)
        {
            var rec = Mission.MakeRecruit(taken);
            if (!taken.Add(rec.Name)) { fails.Add($"takenNameReused:{rec.Name}"); break; }
        }

        // (e) W9 — the null-veterans path stays BYTE-STABLE under the pricing/unlock work: the same
        // RNG seed must yield the identical pool twice (names/classes/weapons), with no reserve unit
        // and no disk read (this whole test runs without meta.json). The default GenerateDraftPool()
        // signature (maxVeterans/crossTrain defaulted) must consume ZERO extra RNG draws.
        Util.Reseed(424242);
        var sigA = new System.Text.StringBuilder();
        foreach (var u in Run.GenerateDraftPool()) sigA.Append(u.Name).Append('/').Append(u.Cls).Append('/').Append(u.Weapon.Kind).Append(';');
        Util.Reseed(424242);
        var sigB = new System.Text.StringBuilder();
        foreach (var u in Run.GenerateDraftPool()) { sigB.Append(u.Name).Append('/').Append(u.Cls).Append('/').Append(u.Weapon.Kind).Append(';'); if (u.FromReserve) fails.Add("nullPathHasVet"); }
        if (sigA.ToString() != sigB.ToString()) fails.Add("nullPathNotByteStable");
        Util.Reseed(0);   // release back to a clock seed (mirrors the daily's release)

        return fails.Count == 0
            ? $"DRAFTTEST: PASS (pool={pool.Count} variety={byCls.Count}cls, drafted {picked.Count}+boon seated, default squad intact, callsigns distinct across pool+squad+backfill)"
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

            // (6) W9 — the recall PRICE TABLE (10 + 8xRank) and "the pool is a shop window, not a
            //     till": building a draft pool with veterans charges NOTHING (only ConfirmDraft pays;
            //     the charge semantics themselves are covered in METATEST).
            if (MetaProg.RecallCost(1) != 18 || MetaProg.RecallCost(2) != 26 ||
                MetaProg.RecallCost(3) != 34 || MetaProg.RecallCost(4) != 42) fails.Add("recallPriceTable");
            if (MetaProg.RecallCost(-3) != MetaProg.RecallBase) fails.Add("recallPriceNegRank");
            SaveGame.AddSalvage(50);
            Run.GenerateDraftPool(SaveGame.LoadVeterans());
            if (SaveGame.LoadSalvage() != 50) fails.Add("poolBuildCharged");
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

    // ── OWTEST (SIGHTLINE_OWTEST): FOCUSED-overwatch braced-cone geometry — a mover ahead of the cone
    // centre is covered, one behind / perpendicular / outside the 90-degree arc is NOT, and a zero-direction
    // (degenerate) watcher behaves as a wide watch. This is the exact gate OnUnitEnteredTile + the AI's
    // PlayerOverwatchTiles both consult, so a correct cone here means the reaction + the AI routing agree. ──
    public string OwSelfTest()
    {
        var fails = new List<string>();
        var w = new Unit { X = 5, Y = 5, OwFocused = true, OwDirX = 1, OwDirY = 0 };   // braced facing east (+X)
        if (!InOwCone(w, 9, 5)) fails.Add("aheadNotInCone");         // straight ahead
        if (!InOwCone(w, 9, 3)) fails.Add("nearAxisNotInCone");      // ~27deg off-axis, inside the arc
        if (InOwCone(w, 5, 1)) fails.Add("perpInCone");              // 90deg (perpendicular) -> outside
        if (InOwCone(w, 1, 5)) fails.Add("behindInCone");           // 180deg (behind) -> outside
        if (InOwCone(w, 9, 0)) fails.Add("justOutsideInCone");      // ~51deg -> just outside the 45deg edge
        if (!InOwCone(w, 5, 5)) fails.Add("selfNotInCone");         // degenerate same-tile -> in
        var wide = new Unit { X = 5, Y = 5, OwFocused = false, OwDirX = 0, OwDirY = 0 };
        if (!InOwCone(wide, 1, 5)) fails.Add("zeroDirNotWide");     // no direction -> behaves as wide (all-true)
        return fails.Count == 0
            ? "OWTEST: PASS (cone covers the braced arc, blind behind/perpendicular/outside; zero-dir = wide)"
            : "OWTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Harness (screenshot): arm a FOCUSED overwatch on a soldier so the gold cone kill-lane renders.
    public void DebugFocusOw()
    {
        StartMission(1);
        var u = Players.Find(p => p.Alive && !p.IsVip);
        if (u != null)
        {
            Selected = u;
            u.OnOverwatch = true; u.OwFocused = true; u.OwDirX = 1; u.OwDirY = 0;   // brace east toward the enemy side
        }
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
        // FUL-10: pre-pick NOX + pre-select MERCENARY CLAUSE so the shot also demos the live
        // recall discount (VEGA 42->21, NOX 34->17) and the discounted RECALL BILL row.
        DraftPicked = new HashSet<Unit> { b };
        DraftSelectedBoon = null;
        DraftSelectedContract = Contract.MercenaryClause;
        // A demo bank that AFFORDS discounted NOX (17) but NOT discounted VEGA (21), so the shot
        // keeps both the priced gold card and the greyed unaffordable one. Touches NO disk.
        DraftSalvage = 20;
        Phase = Phase.Draft;
    }

    // ── BEACONTEST (SIGHTLINE_BEACONTEST): UNDERTOW W6 + APEX W8 — the forward beacon (Evac AND the
    // new gated Escort plant) + the Escort VIP leash. Drives the REAL primitives (DoBeacon / the
    // CheckEnd Evac predicate / LeashVip) on a controlled all-floor scene so it's deterministic +
    // window-free. Asserts:
    //   (Evac)   planting adds the walkable 3x3 to EvacZone; the fixed fallback corner STILL counts; a
    //            non-floor plant refuses gracefully; standing all soldiers on beacon tiles wins;
    //            the beacon is one-per-mission.
    //   (Escort beacon, W8 — FLIPS the deliberate W6 "Escort must not offer a beacon" decision) the
    //            plant is OFFERED on Escort but refused mid-board (far-third gate) and refused on a
    //            WARM LZ (a living DORMANT enemy within Chebyshev 3 vetoes; a ROUTED one doesn't);
    //            accepted in the cold far third.
    //   (Escort leash) the leash steps the VIP TOWARD the nearest soldier via REAL MoveStepAnims
    //            (W8 — every position assert drains the anim queue), never off-board / onto an
    //            occupied tile, short-circuits (holds) once adjacent, routes AROUND fire, and a step
    //            through an enemy overwatch lane DRAWS the reaction (parity with an ordered move).
    public string BeaconSelfTest()
    {
        NoPersist = true;
        var fails = new List<string>();

        // W8: the leash enqueues real MoveStepAnims now — drive the queue to completion before any
        // position assertion (the in-file drain pattern used by the drag/vault/stagger tests).
        void Pump()
        {
            while (_anims.Count > 0)
            {
                var a = _anims[0]; a.OnStart(this);
                for (int i = 0; i < 200 && !a.Update(this, 0.05f); i++) { }
                if (_anims.Count > 0 && _anims[0] == a) _anims.RemoveAt(0);
            }
        }
        void Leash() { LeashVip(); Pump(); }

        Grid = new Grid();                       // all Floor, Height 0
        Players = new List<Unit>();
        Enemies = new List<Unit>();
        Vip = null; CaptiveLocked = false;
        Fx = new Fx();
        Mode = GameMode.Campaign;

        Unit MkP(int x, int y) {
            var u = new Unit { Name = "S", Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                               Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
        }

        // ---------- (A) EVAC forward beacon ----------
        Objective = Objective.Evac;
        BeaconPlanted = false; BeaconZone.Clear(); BeaconTile = default;
        EvacZone.Clear();
        // a fixed 2x4 fallback corner (top-right), exactly like SetupMission's build
        for (int ey = 0; ey < 4; ey++) { EvacZone.Add((Grid.W - 2, ey)); EvacZone.Add((Grid.W - 1, ey)); }
        var corner = new List<(int x, int y)>(EvacZone);

        // a planter mid-board (all floor, so its full 3x3 is walkable and NONE overlaps the corner). A
        // second soldier sits OFF every evac tile so the plant doesn't complete the extraction (which
        // would route CheckEnd -> EnterBarracks and need run state this window-free scene doesn't build).
        var planter = MkP(Grid.W / 2, Grid.H / 2);
        var lagger = MkP(1, 1);                             // far from the corner AND the beacon 3x3
        Players.Add(planter); Players.Add(lagger);
        Selected = planter;
        if (!CanBeacon(planter)) fails.Add("cannotBeaconOnFloorEvac");
        int act0 = planter.ActionsLeft;
        DoBeacon();
        if (!BeaconPlanted) fails.Add("beaconNotPlanted");
        if (planter.ActionsLeft != act0 - 1) fails.Add("beaconDidNotSpendAction");
        // the walkable 3x3 around the planter must ALL be evac tiles now
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var t = (planter.X + dx, planter.Y + dy);
                if (!EvacZone.Contains(t)) fails.Add($"beacon3x3Missing({t.Item1},{t.Item2})");
            }
        // the beacon centre is where the planter stood
        if (BeaconTile != (planter.X, planter.Y)) fails.Add("beaconTileWrong");
        // the FALLBACK corner must STILL count (union, not replace) — the load-bearing safety invariant
        foreach (var t in corner) if (!EvacZone.Contains(t)) fails.Add($"fallbackCornerLost({t.x},{t.y})");
        // one-per-mission: a second plant is refused
        if (CanBeacon(planter)) fails.Add("canBeaconTwice");

        // WIN: stand every living soldier on a beacon tile -> the Evac predicate passes.
        Players.Clear();
        var s1 = MkP(BeaconTile.x, BeaconTile.y);
        var s2 = MkP(BeaconTile.x + 1, BeaconTile.y);       // a ring tile of the beacon
        Players.Add(s1); Players.Add(s2);
        bool evacWin = AlivePlayers().All(p => EvacZone.Contains((p.X, p.Y)));
        if (!evacWin) fails.Add("beaconStandDoesNotWin");
        // and the fallback corner still wins too (safety): move a soldier off the beacon into the corner
        s2.X = corner[0].x; s2.Y = corner[0].y; s2.SyncPos();
        if (!AlivePlayers().All(p => EvacZone.Contains((p.X, p.Y)))) fails.Add("mixedBeaconCornerNoWin");

        // graceful refusal: a planter NOT on floor cannot beacon (and never crashes)
        BeaconPlanted = false; BeaconZone.Clear();
        var wallStander = MkP(3, 3);
        Grid.Tiles[3, 3] = TileType.HighCover;              // now standing on non-floor
        Players.Clear(); Players.Add(wallStander); Selected = wallStander;
        if (CanBeacon(wallStander)) fails.Add("beaconOnNonFloorAllowed");
        DoBeacon();                                         // must be a graceful no-op
        if (BeaconPlanted) fails.Add("beaconPlantedOnNonFloor");
        Grid.Tiles[3, 3] = TileType.Floor;

        // ---------- (A2) ESCORT forward beacon (APEX W8 — flips the W6 refusal) ----------
        // Escort now OFFERS the beacon behind a strict anti-trivialization gate: CheckEnd's Escort
        // test is just "VIP in zone", so the gate (far third + cold LZ) is the load-bearing design.
        Objective = Objective.Escort;
        if (!HasBeaconAction) fails.Add("noBeaconOfferedOnEscort");
        BeaconPlanted = false; BeaconZone.Clear();
        EvacZone.Clear();
        for (int ey = 0; ey < 4; ey++) { EvacZone.Add((Grid.W - 2, ey)); EvacZone.Add((Grid.W - 1, ey)); }
        Players.Clear(); Enemies.Clear();
        // mid-board planter (x < W*2/3): refused — a spawn-side plant + one leash step would be a
        // free win (the VIP spawns in the squad wedge).
        var escortPlanter = MkP(Grid.W / 2, Grid.H / 2);
        Players.Add(escortPlanter); Selected = escortPlanter;
        if (CanBeacon(escortPlanter)) fails.Add("escortBeaconMidBoardAllowed");
        DoBeacon();                                          // graceful no-op
        if (BeaconPlanted) fails.Add("escortBeaconPlantedMidBoard");
        // far third — but a LIVING DORMANT enemy 2 tiles away: WARM LZ, refused. Dormant must veto:
        // pods aren't Alert under concealment and DoBeacon doesn't break stealth, so an Alert-only
        // test would let a concealed squad plant beside a sleeping pod and leash-win before it wakes.
        escortPlanter.X = Grid.W * 2 / 3 + 1; escortPlanter.Y = Grid.H / 2; escortPlanter.SyncPos();
        var sleeper = new Unit { Name = "POD", Cls = "GRUNT", Team = Team.Enemy,
                                 X = escortPlanter.X + 2, Y = escortPlanter.Y,
                                 Hp = 6, MaxHp = 6, Aim = 60, Mobility = 4,
                                 Weapon = Weapon.Make(WeaponKind.Rifle), Alert = AlertLevel.Unaware };
        sleeper.Ammo = sleeper.Weapon.Clip; sleeper.SyncPos();
        Enemies.Add(sleeper);
        if (CanBeacon(escortPlanter)) fails.Add("escortBeaconWarmLzAllowed");
        // a ROUTED survivor is fleeing, not holding the LZ — it must NOT veto the plant
        sleeper.Routed = 3;
        if (!CanBeacon(escortPlanter)) fails.Add("escortBeaconRoutedVetoed");
        sleeper.Routed = 0;
        // push the sleeper beyond Chebyshev 3: COLD far third — accepted, zone unioned here
        sleeper.X = escortPlanter.X + 4; sleeper.SyncPos();
        if (!CanBeacon(escortPlanter)) fails.Add("escortBeaconColdRefused");
        DoBeacon();
        if (!BeaconPlanted) fails.Add("escortBeaconNotPlanted");
        if (!EvacZone.Contains((escortPlanter.X, escortPlanter.Y))) fails.Add("escortBeaconZoneMissing");
        Objective = Objective.Evac;

        // ---------- (B) ESCORT VIP leash ----------
        Objective = Objective.Escort;
        CaptiveLocked = false;
        EvacZone.Clear();                                   // no zone: the leash follows the soldier (not evac)
        Players = new List<Unit>();
        Enemies = new List<Unit>();
        // an anchor soldier far to the east; the VIP starts far to the west (not adjacent)
        var anchor = MkP(14, 5);
        Vip = Mission.MakeVip(1); Vip.X = 3; Vip.Y = 5; Vip.SyncPos(); Vip.BeginTurn();
        Players.Add(anchor); Players.Add(Vip);
        int vipStartDist = Util.ChebyDist(Vip.X, Vip.Y, anchor.X, anchor.Y);
        int vx0 = Vip.X, vy0 = Vip.Y;
        Leash();
        int vipNewDist = Util.ChebyDist(Vip.X, Vip.Y, anchor.X, anchor.Y);
        if (vipNewDist >= vipStartDist) fails.Add("leashDidNotCloseGap");                 // must step toward the soldier
        if (!(Vip.X == vx0 && Vip.Y == vy0) && !Grid.InBounds(Vip.X, Vip.Y)) fails.Add("leashWentOffBoard");
        if (IsOccupiedByOther(Vip.X, Vip.Y, Vip)) fails.Add("leashOntoOccupiedTile");
        // step it repeatedly (a turn boundary each call): it must CONVERGE and never overshoot onto the soldier
        for (int i = 0; i < 8; i++)
        {
            Leash();
            if (Vip.X == anchor.X && Vip.Y == anchor.Y) { fails.Add("leashSteppedOntoSoldier"); break; }
        }
        if (Util.ChebyDist(Vip.X, Vip.Y, anchor.X, anchor.Y) > 1) fails.Add("leashDidNotReachAdjacency");
        // once adjacent, the leash HOLDS (short-circuit) — no further movement
        int hx = Vip.X, hy = Vip.Y;
        Leash();
        if (!(Vip.X == hx && Vip.Y == hy)) fails.Add("leashMovedWhileAdjacent");
        // a caged (Rescue-style) or dead VIP never moves via the escort leash
        Objective = Objective.Escort; CaptiveLocked = true;
        int cx = Vip.X, cy = Vip.Y; Vip.X = 3; Vip.Y = 5; Vip.SyncPos();   // re-separate it
        Leash();
        if (!(Vip.X == 3 && Vip.Y == 5)) fails.Add("leashMovedCagedVip");
        CaptiveLocked = false;

        // LEASH HAZARD-AVOID (APEX W8): the leash walks REAL MoveStepAnims through OnUnitEnteredTile
        // now, so it must ROUTE AROUND fire — never park on, or step through, a burning tile while a
        // clean closing route exists (a burning picket across the direct lane, open rows above/below).
        Players = new List<Unit>();
        Enemies = new List<Unit>();
        EvacZone.Clear();
        var fireAnchor = MkP(14, 5);
        Vip = Mission.MakeVip(1); Vip.X = 3; Vip.Y = 5; Vip.SyncPos(); Vip.BeginTurn();
        Vip.MaxHp = 99; Vip.Hp = 99;                     // any burn tick at all must show as a delta
        Players.Add(fireAnchor); Players.Add(Vip);
        for (int fy = 3; fy <= 7; fy++) Grid.LightFire(6, fy, 99);
        int hp0 = Vip.Hp;
        for (int i = 0; i < 10 && Util.ChebyDist(Vip.X, Vip.Y, fireAnchor.X, fireAnchor.Y) > 1; i++) Leash();
        if (Vip.Hp != hp0) fails.Add($"leashWalkedThroughFire(hp{hp0}->{Vip.Hp})");
        if (Vip.HasStatus(StatusKind.Burning)) fails.Add("leashIgnitedVip");
        if (Grid.IsFire(Vip.X, Vip.Y)) fails.Add("leashParkedInFire");
        if (Util.ChebyDist(Vip.X, Vip.Y, fireAnchor.X, fireAnchor.Y) > 1) fails.Add("leashStalledAtFireWall");
        for (int fy = 3; fy <= 7; fy++) Grid.Fire[6, fy] = 0;   // clear the picket

        // LEASH-vs-OVERWATCH PARITY (APEX W8): a leash step through an enemy overwatch lane draws the
        // reaction exactly like a player-ordered move (the old teleport skipped OnUnitEnteredTile — a
        // free stealth-walk past a held lane on the most important unit of the mission).
        Players = new List<Unit>();
        Enemies = new List<Unit>();
        EvacZone.Clear();
        var owAnchor = MkP(14, 5);
        Vip = Mission.MakeVip(1); Vip.X = 8; Vip.Y = 5; Vip.SyncPos(); Vip.BeginTurn();
        Vip.MaxHp = 99; Vip.Hp = 99;                     // the reaction may connect: survival-proof the asset
        Players.Add(owAnchor); Players.Add(Vip);
        var watcher = new Unit { Name = "OW", Cls = "GRUNT", Team = Team.Enemy, X = 10, Y = 8,
                                 Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4,
                                 Weapon = Weapon.Make(WeaponKind.Rifle) };
        watcher.Ammo = watcher.Weapon.Clip; watcher.SyncPos(); watcher.BeginTurn();
        watcher.OnOverwatch = true; watcher.ReactedThisTurn = false;
        Enemies.Add(watcher);
        int ammo0 = watcher.Ammo;
        Leash();                                         // the auto-move crosses the watched lane
        if (!watcher.ReactedThisTurn) fails.Add("leashDrewNoOverwatch");
        if (watcher.Ammo != ammo0 - 1) fails.Add("owReactionSpentNoAmmo");
        if (watcher.OnOverwatch) fails.Add("owStillHeldAfterReaction");
        Enemies.Clear();

        // SQUAD-AT-EVAC: once a soldier has reached the zone, the leash heads the VIP INTO the zone (the win
        // is the VIP on an evac tile) rather than parking it adjacent forever. Build a small corner zone,
        // seat the anchor IN it, put the VIP one step outside, and assert the leash closes onto an evac tile.
        Players = new List<Unit>();
        EvacZone.Clear();
        for (int ey = 0; ey < 3; ey++) { EvacZone.Add((Grid.W - 1, ey)); EvacZone.Add((Grid.W - 2, ey)); }
        var zoneSoldier = MkP(Grid.W - 1, 1);               // a soldier standing in the zone
        Vip = Mission.MakeVip(1); Vip.X = Grid.W - 4; Vip.Y = 1; Vip.SyncPos(); Vip.BeginTurn();   // just outside
        Players.Add(zoneSoldier); Players.Add(Vip);
        int vipToZone0 = DistToEvac(Vip.X, Vip.Y);
        for (int i = 0; i < 6 && !EvacZone.Contains((Vip.X, Vip.Y)); i++) Leash();
        if (!EvacZone.Contains((Vip.X, Vip.Y))) fails.Add($"leashDidNotEnterZone({Vip.X},{Vip.Y})");
        if (DistToEvac(Vip.X, Vip.Y) > vipToZone0) fails.Add("leashMovedAwayFromZone");

        return fails.Count == 0
            ? "BEACONTEST: PASS (Evac: plant adds walkable 3x3 to EvacZone + fallback corner still wins; non-floor refuses gracefully; all-on-beacon wins; 1/mission. Escort beacon (W8): offered, mid-board refused, warm LZ (dormant within 3) refused, routed doesn't veto, cold far third plants. Escort leash: real MoveStepAnims — steps VIP toward nearest soldier, converges to adjacency, never off-board/occupied/onto-soldier, holds when adjacent, inert while caged, routes around fire, draws overwatch parity, and walks INTO the zone once the squad has arrived)"
            : "BEACONTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Harness (screenshot): plant a forward EVAC beacon so the render shows the beacon 3x3 zone + the
    /// mast/broadcast marker alongside the fixed far-corner fallback. Forces Evac, walks a soldier to a
    /// clear forward floor tile past mid-field, then drops the beacon there via the real DoBeacon path.
    public void DebugBeacon()
    {
        if (Objective != Objective.Evac) DebugForceObjective(Objective.Evac);
        var u = Players.FirstOrDefault(p => p.Alive && !p.IsVip);
        if (u == null) return;
        // seat the soldier on a clear forward floor tile (past mid-field) so its full 3x3 is walkable.
        for (int x = Grid.W - 4; x >= Grid.W / 2 && !BeaconPlanted; x--)
            for (int y = 2; y < Grid.H - 2 && !BeaconPlanted; y++)
            {
                if (!Grid.IsFloor(x, y) || IsOccupiedByOther(x, y, u) || EvacZone.Contains((x, y))) continue;
                // require the full ring to be floor so the demo beacon reads as a clean 3x3
                bool ringOk = true;
                for (int dx = -1; dx <= 1 && ringOk; dx++)
                    for (int dy = -1; dy <= 1 && ringOk; dy++)
                        if (!Grid.IsFloor(x + dx, y + dy)) ringOk = false;
                if (!ringOk) continue;
                u.X = x; u.Y = y; u.SyncPos();
                Selected = u;
                DoBeacon();
            }
    }

    /// FUL-11 — finale-kit spawn DISTRIBUTION probe (the TERROR lesson: verify the mechanic can
    /// actually fire in the real distribution BEFORE measuring win-rates). For each kit x heat
    /// {0 = Ai.Tier 0, 4 = Ai.Tier 1 via the ladder} x `seeds` flywheel-identical worlds
    /// (Util.Reseed(50000+slot), the RunOne pairing seed), rebuild the m6 force and assert:
    ///   * enemies[0] is the kit's named boss and carries IsBoss;
    ///   * the EXPLICIT retinue occupies slots 1-2 at LOW heat too (the cost-neutral guarantee —
    ///     the W6 heat gate trims CASCADE bodies, never the retinue);
    ///   * Wardens: the SIGNIFER sits in pod 0 with its aura covering the boss AS SPAWNED
    ///     (Cheb <= Game.BannerRange, the relocation post-pass contract), the ORDERLY is present,
    ///     and the banner cap holds (exactly one WARBRINGER in the force).
    /// Prints per-kit composition tallies + the banner-distance max, then PASS/FAIL.
    public static string Ful11ProbeTest(int seeds)
    {
        var fails = new System.Collections.Generic.List<string>();
        string oldHeat = Environment.GetEnvironmentVariable("SIGHTLINE_HEAT");
        string oldFin = Environment.GetEnvironmentVariable("SIGHTLINE_FINALE");
        var sb = new System.Text.StringBuilder();
        foreach (string kit in new[] { "wardens", "legion", "syndicate" })
            foreach (int heat in new[] { 0, 4 })
            {
                Environment.SetEnvironmentVariable("SIGHTLINE_FINALE", kit);
                Environment.SetEnvironmentVariable("SIGHTLINE_HEAT", heat.ToString());
                var comp = new System.Collections.Generic.Dictionary<string, int>();
                int maxBanDist = -1, minCount = int.MaxValue, maxCount = 0;
                for (int slot = 0; slot < seeds; slot++)
                {
                    Util.Reseed(50000 + slot);                      // the flywheel's pairing seed
                    var g = new Game { NoPersist = true };
                    g.StartMission(6);
                    string tag = $"{kit}/h{heat}/s{slot}";
                    var es = g.Enemies;
                    minCount = Math.Min(minCount, es.Count); maxCount = Math.Max(maxCount, es.Count);
                    foreach (var e in es) comp[e.Cls] = comp.GetValueOrDefault(e.Cls) + 1;
                    if (es.Count < 3) { fails.Add($"{tag}:tooFewBodies={es.Count}"); continue; }
                    var boss = es[0];
                    if (!boss.IsBoss || boss.Cls != "ELITE") fails.Add($"{tag}:slot0NotBoss={boss.Name}");
                    string wantBoss = kit == "legion" ? "SIEGELORD" : kit == "syndicate" ? "SPYMASTER" : "WARLORD";
                    if (boss.Name != wantBoss) fails.Add($"{tag}:bossName={boss.Name}");
                    switch (kit)
                    {
                        case "wardens":
                            if (es[1].Cls != "WARBRINGER") fails.Add($"{tag}:slot1={es[1].Cls}");
                            if (es[2].Cls != "MEDIC") fails.Add($"{tag}:slot2={es[2].Cls}");
                            if (es[1].PodId != 0) fails.Add($"{tag}:bannerPod={es[1].PodId}");
                            int bd = Util.ChebyDist(es[1].X, es[1].Y, boss.X, boss.Y);
                            maxBanDist = Math.Max(maxBanDist, bd);
                            if (bd > Game.BannerRange) fails.Add($"{tag}:auraMiss={bd}");
                            if (es.Count(e => e.Cls == "WARBRINGER") != 1) fails.Add($"{tag}:bannerCap");
                            break;
                        case "legion":
                            if (es[1].Cls != "LANCER" || es[2].Cls != "LANCER") fails.Add($"{tag}:legionRetinue={es[1].Cls}/{es[2].Cls}");
                            break;
                        default:
                            if (es[1].Cls != "SCREENER" || es[2].Cls != "STRIKER") fails.Add($"{tag}:syndRetinue={es[1].Cls}/{es[2].Cls}");
                            break;
                    }
                }
                sb.Append($"  {kit,-9} h{heat}: bodies {minCount}-{maxCount}");
                if (kit == "wardens") sb.Append($"  bannerDistMax {maxBanDist}");
                sb.Append("  comp[");
                sb.Append(string.Join(" ", comp.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}:{kv.Value}")));
                sb.AppendLine("]");
            }
        Environment.SetEnvironmentVariable("SIGHTLINE_HEAT", oldHeat);
        Environment.SetEnvironmentVariable("SIGHTLINE_FINALE", oldFin);
        Console.Write(sb.ToString());
        return fails.Count == 0 ? $"FUL11PROBE PASS ({seeds} seeds x 3 kits x 2 heats)"
                                : "FUL11PROBE FAIL: " + string.Join(", ", fails.Take(12));
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
        // FUL-10: SIGHTLINE_EVENTID=<id> pins WHICH catalog event is staged (default: first, the
        // pre-FUL-10 behaviour, so plain SIGHTLINE_EVENT shots are unchanged). Unknown ids fall
        // back rather than crash a shot batch.
        var wantId = Environment.GetEnvironmentVariable("SIGHTLINE_EVENTID");
        GameEvent pinned = string.IsNullOrEmpty(wantId) ? null : System.Array.Find(EventCatalog.All, e => e.Id == wantId);
        _activeEvent = pinned ?? (EventCatalog.All.Length > 0 ? EventCatalog.All[0] : null);
        Phase = Phase.Barracks;
    }

    // ── FUL-9: SIGHTLINE_EXPOSURETEST — content-exposure histogram ────────────────
    /// 200-seed check of THE DECK's two contracts. Per generated campaign map:
    ///   (a) the objective invariant on EVERY route — routes are ENUMERATED (mid columns hold
    ///       2-3 rows, so sampling could miss a branch): >=1 Eliminate fight, >=1 Defend-or-
    ///       Rescue fight, <=1 Escort fight, boss card Decapitate, every route ends at BOSS;
    ///   (b) the arena deck deals zero repeats within a run (draws 1..MaxMissions).
    /// Across all seeds: every one of the 8 objectives is dealt somewhere, and every authored
    /// arena is dealt somewhere. Prints both histograms + PASS/FAIL. Windowless + persistence-
    /// free: Run.GenerateMap and Mission.DeckPick are pure derivations off the seed.
    public static string ExposureSelfTest()
    {
        var fails = new List<string>();
        const int Seeds = 200;
        int nLay = Maps.Layouts.Length;
        var arenaHist = new int[nLay];
        var objHist = new Dictionary<Objective, int>();
        int routesTotal = 0, escortNodes = 0;

        for (int i = 0; i < Seeds; i++)
        {
            int seed = 1000 + i * 7919;   // spread the seed space; Hash3 decorrelates regardless
            var run = new Run { MapSeed = seed };
            run.GenerateMap(seed);

            // (a) enumerate every route: edges only go col -> col+1, so DFS terminates
            var routes = new List<List<MissionNode>>();
            void Walk(MissionNode node, List<MissionNode> path)
            {
                path.Add(node);
                if (node.Next.Count == 0) routes.Add(new List<MissionNode>(path));
                else foreach (int id in node.Next) Walk(run.Map[id], path);
                path.RemoveAt(path.Count - 1);
            }
            Walk(run.Map[0], new List<MissionNode>());
            if (routes.Count == 0) fails.Add($"seed{seed}:noRoutes");
            foreach (var route in routes)
            {
                routesTotal++;
                if (route[^1].Kind != NodeKind.Boss) fails.Add($"seed{seed}:routeEndsOffBoss");
                int elim = 0, dr = 0, esc = 0;
                foreach (var node in route)
                {
                    if (node.Kind == NodeKind.Event) continue;   // not a fight — the sentinel card must not count
                    var o = node.Card.Objective;
                    if (o == Objective.Eliminate) elim++;
                    if (o == Objective.Defend || o == Objective.Rescue) dr++;
                    if (o == Objective.Escort) esc++;
                }
                if (elim < 1) fails.Add($"seed{seed}:noEliminate");
                if (dr < 1) fails.Add($"seed{seed}:noDefendOrRescue");
                if (esc > 1) fails.Add($"seed{seed}:escortX{esc}");
            }
            if (run.Map[^1].Card.Objective != Objective.Decapitate) fails.Add($"seed{seed}:bossNotDecapitate");
            foreach (var node in run.Map)
                if (node.Kind != NodeKind.Event)
                {
                    objHist[node.Card.Objective] = objHist.GetValueOrDefault(node.Card.Objective) + 1;
                    if (node.Card.Objective == Objective.Escort) escortNodes++;
                }

            // (b) the deck: draws 1..MaxMissions repeat-free + in range
            var seen = new HashSet<int>();
            for (int m = 1; m <= Run.MaxMissions; m++)
            {
                int a = Mission.DeckPick(seed, m);
                if (a < 0 || a >= nLay) { fails.Add($"seed{seed}:deckOutOfRange:{a}"); continue; }
                if (!seen.Add(a)) fails.Add($"seed{seed}:arenaRepeat:{a}");
                arenaHist[a]++;
            }
        }

        foreach (Objective o in Enum.GetValues<Objective>())
            if (objHist.GetValueOrDefault(o) == 0) fails.Add($"objectiveNeverDealt:{o}");
        for (int a = 0; a < nLay; a++)
            if (arenaHist[a] == 0) fails.Add($"arenaNeverDealt:{a}");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"EXPOSURETEST: {Seeds} seeds | {routesTotal} routes enumerated | {Seeds * Run.MaxMissions} deck draws over {nLay} arenas");
        sb.AppendLine("ARENA DECK HISTOGRAM (arena:draws):");
        for (int a = 0; a < nLay; a++)
        {
            sb.Append($"  {a,2}:{arenaHist[a],-4}");
            if (a % 7 == 6 || a == nLay - 1) sb.AppendLine();
        }
        sb.AppendLine($"  min {arenaHist.Min()} / mean {arenaHist.Average():0.0} / max {arenaHist.Max()} draws per arena");
        sb.AppendLine("OBJECTIVES DEALT (fight nodes, all seeds):");
        foreach (Objective o in Enum.GetValues<Objective>())
            sb.AppendLine($"  {o,-10}: {objHist.GetValueOrDefault(o)}");
        sb.AppendLine($"  (Escort nodes total {escortNodes} — exactly one per map by construction, <=1 per route)");
        sb.Append(fails.Count == 0 ? "EXPOSURETEST PASS"
            : $"EXPOSURETEST FAIL: {string.Join(", ", fails.Take(12))}{(fails.Count > 12 ? $" (+{fails.Count - 12} more)" : "")}");
        return sb.ToString();
    }

}
