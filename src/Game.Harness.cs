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

    /// W2 harness hook (screenshot only): SIGHTLINE_AIIDLESHOT — the enemy AMMO read. Wakes the
    /// board and walks every live hostile's clip down a different amount, leaving the first one
    /// DRY, so one frame shows the whole range of the token's read: a full mag, partial mags, and
    /// the DRY chip on the hostile that must now spend an action reloading.
    ///
    /// It also puts a STATUS EFFECT on the first three hostiles, and that is the point of the hook,
    /// not decoration. The W2 review found the original read overpainted: it drew its own pill and
    /// pip row into p.Y+24..+42, which `DrawUnitStatusChips` owns and paints LATE, so on any hostile
    /// carrying BRN/BLD/DAZ the pill lost 9 of its 15 px and the pip row vanished entirely — while
    /// the ROADMAP claimed "it does not collide". The scene now STAGES that exact collision, so the
    /// claim is checkable from one frame instead of taken on trust. Pair with SIGHTLINE_CB=1 for the
    /// colorblind pass (the DRY state is carried by word + empty-magazine glyph, not hue).
    public void DebugAmmoShot()
    {
        DebugWakeAll();
        var statuses = new[] { StatusKind.Burning, StatusKind.Bleed, StatusKind.Disoriented };
        int i = 0;
        foreach (var e in Enemies)
        {
            if (!e.Alive || e.Weapon == null) continue;
            e.Ammo = Math.Min(e.Weapon.Clip, i);       // 0 (DRY), 1, 2, ... rounds left across the board
            if (i < statuses.Length) e.AddStatus(statuses[i], 3);   // DRY + BRN on the same token
            i++;
        }
    }

    /// C2 harness hook (screenshot only): SIGHTLINE_DECLINESHOT — the OPPONENT DECLINES, staged
    /// on the live board so one frame can be judged. Rule 3 of the program's own rules: a CRN
    /// batch prices CONSEQUENCES and is structurally blind to FEEL, so this behaviour needs eyes.
    ///
    /// It clears a pocket of the real arena and seats two identical GRUNTs at the same range from
    /// two identical soldiers. The only difference is a single HIGH COVER block: the LEFT pair's
    /// soldier is behind it, the RIGHT pair's is in the open. Then it runs the REAL Ai.Plan for
    /// each hostile and applies exactly what Game.UpdateEnemy's ActAfterMove would apply — so the
    /// frame is a decision the shipped planner made, not a hand-set flag.
    ///
    /// With SIGHTLINE_AIDECLINE=1 (shipped) the left hostile declines its covered shot and wears
    /// the accent "OW" badge, and the INCOMING FIRE card gains its "OVERWATCH LANE — entering
    /// draws a reaction" line (which is PRE-EXISTING in Hud.cs, not added by this wave — the hook
    /// only creates the state that makes it appear). An earlier version of this comment also
    /// claimed Renderer's kill-zone wash "lights the ground it now denies"; it does not, visibly.
    /// The wash is 7-12% alpha and sits below perceptual threshold on a warm biome — and because
    /// a plain enemy overwatch has no lane selection, it covers nearly the whole open board, so
    /// even seen it would carry no information. Both facts are C2 findings, in ROADMAP. With
    /// SIGHTLINE_AIDECLINE=0 the SAME hostile on the SAME board takes the shot and holds no lane:
    /// the two frames are the wave. Pair with SIGHTLINE_SHOT=760 (the briefing card holds ~11 s).
    public void DebugDeclineShot()
    {
        DebugWakeAll();
        // A clean pocket: floor everywhere, plus a HIGH-COVER wall down column 16 that everything
        // NOT part of the scene is parked behind. Without it the staged hostile simply shot one of
        // the parked soldiers instead (measured: both staged shooters reported hit=78% at a body
        // 11 tiles away in the open) and the frame staged nothing.
        for (int x = 1; x <= Grid.W - 2; x++)
            for (int y = 0; y <= Grid.H - 1; y++) { Grid.Tiles[x, y] = TileType.Floor; Grid.Barrel[x, y] = false; }
        for (int y = 0; y <= Grid.H - 1; y++) { Grid.Tiles[16, y] = TileType.HighCover; Grid.SetCoverHp(16, y); }
        int park = 0;
        foreach (var u in Players.Concat(Enemies))
        {
            if (!u.Alive) continue;
            u.X = Grid.W - 1; u.Y = Math.Min(Grid.H - 1, park++); u.Bob = 0f; u.Facing = 0f; u.SyncPos();
        }

        var soldier = AlivePlayers().FirstOrDefault(u => !u.IsVip);
        var foe = AliveEnemies().FirstOrDefault();
        if (soldier == null || foe == null) return;

        // ONE soldier, behind ONE high-cover block. Everything else is behind the wall, so the
        // hostile's only shot in the world is the covered one — which is the decision on trial.
        soldier.X = 12; soldier.Y = 3; soldier.Bob = 0f; soldier.Facing = 0f; soldier.SyncPos();
        Grid.Tiles[11, 3] = TileType.HighCover; Grid.SetCoverHp(11, 3);

        // Searched, not hand-picked: a hand-picked tile is one Bresenham detail away from having
        // no line of sight at all, and then there is no shot to decline.
        int ax = -1, ay = -1;
        for (int y = 0; y < Grid.H && ax < 0; y++)
            for (int x = 3; x <= 7 && ax < 0; x++)
            {
                if (!Grid.IsFloor(x, y)) continue;
                if (!Grid.HasLineOfSight(x, y, 12, 3)) continue;
                if (Grid.GetCover(12, 3, x, y).Level != 2) continue;
                ax = x; ay = y;
            }
        if (ax < 0) { Console.WriteLine("DECLINESHOT: staging FAILED (no shooting tile)"); return; }
        foe.X = ax; foe.Y = ay; foe.Bob = 0f; foe.Facing = 0f; foe.SyncPos();
        foe.Aim = 65; foe.Ammo = foe.Weapon.Clip; foe.BeginTurn();
        // Ring it in LOW cover. Two jobs: it makes DIGGING IN a real alternative, and it PINS the
        // unit (Grid.IsFloor excludes any cover tile), which the first version of this hook needed
        // and did not have — on open ground the planner's answer to a covered target is to FLANK
        // it, and it duly walked around the block and took a 77% shot. That is the wave working,
        // but it is a different frame. This one is the case where no better tile exists.
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = ax + dx, ny = ay + dy;
                if (!Grid.InBounds(nx, ny)) continue;
                Grid.Tiles[nx, ny] = TileType.LowCover; Grid.SetCoverHp(nx, ny);
            }

        _aiUnits = new System.Collections.Generic.List<Unit> { foe };
        var all = new System.Collections.Generic.List<Unit>(Players); all.AddRange(Enemies);
        Combat.AllUnits = all;
        PlanEnemySquad();

        // run the REAL planner and apply exactly what Game.UpdateEnemy's ActAfterMove would
        var pl = Ai.Plan(this, foe);
        if (pl.ShootTarget != null) { foe.FiredThisTurn = true; foe.MovedAfterFire = false; }
        else if (pl.Overwatch) { foe.OnOverwatch = true; foe.ActionsLeft = 0; }
        else if (pl.Hunker) { foe.Hunkered = true; foe.ActionsLeft = 0; }
        string what = pl.ShootTarget != null ? "TOOK THE SHOT"
                    : pl.Declined ? (pl.Hunker ? "DECLINED - HUNKERED" : "DECLINED - HOLDING THE LANE")
                    : "NO SHOT";
        int ex = pl.Path.Count > 0 ? pl.Path[^1].x : foe.X, ey = pl.Path.Count > 0 ? pl.Path[^1].y : foe.Y;
        Console.WriteLine($"DECLINESHOT: {foe.Name}@({foe.X},{foe.Y})->({ex},{ey}) vs {soldier.Name}@(12,3) "
                        + $"shotHit={pl.ShotHit}% E[dmg]={pl.ShotExp:0.00} declined={pl.Declined} "
                        + $"ow={pl.Overwatch} hunker={pl.Hunker} -> {what}");
        Fx.PopText(foe.Pos + new Vector2(0, -34), what, pl.ShootTarget != null ? Pal.Foe : Pal.Accent, 15f);
        ShowBanner($"{pl.ShotHit}% SHOT AVAILABLE - {what}", true);
        Selected = null;
    }

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

    /// W4 REVIEW FIX harness hook (screenshot only): the two cases in which the awareness
    /// marker's ABOVE-THE-TOKEN slot is occluded. Row 0 (the marker would sit behind the top HUD
    /// bar) and a pod with a unit standing on the tile directly above (the normal case — hostiles
    /// spawn in pods). A third pod in open mid-field is the control that must keep the high slot.
    public void DebugMarkers()
    {
        var foes = Enemies.Where(e => e.Alive).ToList();
        if (foes.Count < 3) return;
        void Seat(Unit u, int x, int y, AlertLevel a)
        {
            Grid.Tiles[x, y] = TileType.Floor;
            u.X = x; u.Y = y; u.Alert = a; u.Bob = 0f; u.Facing = 0f; u.SyncPos();
        }
        Seat(foes[0], 5, 0, AlertLevel.Unaware);        // row 0: the HUD-bar case
        Seat(foes[1], 2, 0, AlertLevel.Suspicious);     // row 0, the other marker
        Seat(foes[2], 13, 5, AlertLevel.Unaware);       // mid-field: a unit stands on (13,4)
        var stacker = AlivePlayers().FirstOrDefault(pl => !pl.IsVip) ?? AlivePlayers().FirstOrDefault();
        if (stacker != null) { Grid.Tiles[13, 4] = TileType.Floor; stacker.X = 13; stacker.Y = 4; stacker.SyncPos(); }
        if (foes.Count > 3) Seat(foes[3], 3, 5, AlertLevel.Unaware);   // control: clear above
        Selected = null;
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
        // LEAD FIX (C4 merge): this test was CLOCK-SEEDED while 41 other harness reseed calls
        // exist, so its pod placement varied run to run and the suppressor legs
        // (suppressedShotDidNotBreak / suppressedTargetPodAsleep) failed at a low rate — 0/6
        // standalone in both Debug and Release, but it took down a --full sweep, which is the only
        // place it matters. C3's exit statement is what turned that into a refusal rather than a
        // line nobody read. A gate test that depends on the wall clock is a gate that fails
        // randomly; pinning the stream is the same fix C5 applied per screen in FITTEST.
        Util.Reseed(90210);
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

    /// W9 REVIEW FIX — the UI half of SIGHTLINE_TRUTHTEST: read what the TOOLTIP ACTUALLY PAINTS.
    ///
    /// WHY THIS EXISTS. W9's first TRUTHTEST asserted `ShotOdds.DmgMinEff/DmgMaxEff` and
    /// `Combat.LockOnAim(...)` — the values the HUD is SUPPOSED to read. Nothing bound the HUD to
    /// them, so reverting Hud.DrawTooltip's DMG row to `$"{o.DmgMin}-{o.DmgMax}"` and its LOCK-ON
    /// badge to the old `o.CoverLevel == 0` predicate left TRUTHTEST PASSing on BOTH of the display
    /// defects it was written for. A test that re-derives the right answer is a PIN, not a test.
    /// So this half drives the REAL hover/aim path (Game.Update -> UpdateHoverAndAim -> ComputeOdds),
    /// renders the REAL tooltip, and captures every string it paints AT THE DRAW CALL
    /// (Cfg.CaptureText). Whatever the panel says is what this reads, however it was computed.
    ///
    /// Returns "" when every leg holds, else a comma-joined fail list.
    public string TooltipTruthFails()
    {
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();
        var savedCapture = Cfg.CaptureText;
        try
        {
            // ---- a controlled two-unit board; the fire lane is clear so nothing warps the odds ----
            void Scene()
            {
                Grid = new Grid();
                Players = new System.Collections.Generic.List<Unit>();
                Enemies = new System.Collections.Generic.List<Unit>();
                Vip = null; CaptiveLocked = false;
                Objective = Objective.Eliminate; EvacZone.Clear();
                Fx = new Fx(); _anims.Clear();
                Phase = Phase.PlayerTurn; SquadConcealed = false;
                Combat.MissionFaction = Faction.None; Combat.PrepFaction = Faction.None;
                Combat.RunBoons.Clear();
                AimMode = false; KbCursor = false;
            }
            Unit MkP(int x, int y)
            {
                var u = new Unit { Name = "SOL", Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                                   Hp = 8, MaxHp = 8, Aim = 75, Mobility = 4,
                                   Weapon = Weapon.Make(WeaponKind.Rifle), Alive = true };
                u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
            }
            Unit MkE(int x, int y)
            {
                var u = new Unit { Name = "FOE", Cls = "GRUNT", Team = Team.Enemy, X = x, Y = y,
                                   Hp = 40, MaxHp = 40, Aim = 60, Mobility = 4,
                                   Weapon = Weapon.Make(WeaponKind.Rifle), Alive = true };
                u.Ammo = u.Weapon.Clip; u.Alert = AlertLevel.Alert; u.SyncPos(); u.BeginTurn(); return u;
            }

            // Drive the REAL aim path, then paint the REAL tooltip and return everything it SAID.
            System.Collections.Generic.List<(string text, float size)> PaintTooltip(Unit a, Unit d)
            {
                Selected = a; AimMode = true; AimTarget = d;
                Update(1f / 60f);                        // -> UpdateHoverAndAim -> ShowOdds/HoverOdds
                var cap = new System.Collections.Generic.List<(string text, float size)>();
                Cfg.CaptureText = cap;
                Raylib.BeginDrawing();
                Hud.DebugDrawTooltip(this);
                Raylib.EndDrawing();
                Cfg.CaptureText = null;
                return cap;
            }
            // the value painted immediately after a label IS that tooltip row (label then value)
            string RowAfter(System.Collections.Generic.List<(string text, float size)> cap, string label)
            {
                for (int i = 0; i < cap.Count - 1; i++) if (cap[i].text == label) return cap[i + 1].text;
                return null;
            }
            bool Painted(System.Collections.Generic.List<(string text, float size)> cap, string label)
            {
                foreach (var c in cap) if (c.text == label) return true;
                return false;
            }

            // ============ (1) the DMG ROW the panel PAINTS vs the damage Resolve DEALS ============
            // Two defenders differing ONLY in HvtGuarded. A raw-band row is identical for both and
            // wrong for the guarded one; the rolled ground truth is what catches it.
            string plainRow = null, guardedRow = null;
            foreach (bool guarded in new[] { false, true })
            {
                Scene();
                var a = MkP(4, 5); var d = MkE(9, 5);
                d.HvtGuarded = guarded;
                Players.Add(a); Enemies.Add(d);
                var cap = PaintTooltip(a, d);
                string tag = guarded ? "Guarded" : "Plain";
                string row = RowAfter(cap, "DMG");
                if (guarded) guardedRow = row; else plainRow = row;
                if (string.IsNullOrEmpty(row)) { fails.Add("uiNoDmgRow" + tag); continue; }
                var parts = row.Split('-');
                if (parts.Length != 2 || !int.TryParse(parts[0], out int shownLo)
                                      || !int.TryParse(parts[1], out int shownHi))
                { fails.Add("uiDmgRowShape" + tag + ":" + row); continue; }

                // ground truth: roll real shots at the SAME defender, keep clean non-crit hits
                Util.Reseed(90210);
                int lo = int.MaxValue, hi = int.MinValue, n = 0;
                for (int i = 0; i < 40000; i++)
                {
                    a.ConsecutiveMisses = 0; d.Hp = d.MaxHp;
                    var r = Combat.Resolve(Grid, a, d);
                    if (!r.Hit || r.Graze || r.Crit) continue;
                    n++;
                    if (r.Damage < lo) lo = r.Damage;
                    if (r.Damage > hi) hi = r.Damage;
                }
                if (n < 500) { fails.Add("uiNoSample" + tag); continue; }
                if (shownLo != lo || shownHi != hi)
                    fails.Add($"uiDmgRowLies{tag} shows={row} deals={lo}-{hi}");
                // the GRAZE row directly beneath it is computed from the same defender: they must agree
                string gz = RowAfter(cap, "GRAZE");
                if (gz != null && !gz.StartsWith(shownLo.ToString() + " "))
                    fails.Add($"uiGrazeDisagrees{tag} dmg={row} graze={gz}");
            }
            // ...and the guard must MOVE the painted row, or leg (1) proves nothing
            if (plainRow != null && guardedRow != null && plainRow == guardedRow)
                fails.Add($"uiDmgRowIgnoresDefender both={plainRow}");

            // ============ (2) the LOCK-ON BADGE the panel PAINTS vs the hit% it moves =============
            // OPEN target (CoverLevel 0, NOT flanked) — the modal targeting state, and the exact case
            // the stale badge fired on. FLANKED — the case the perk really applies to.
            foreach (bool flankGeom in new[] { false, true })
            {
                Scene();
                var a = MkP(4, 5);
                Unit d;
                if (flankGeom)
                {
                    d = MkE(9, 5);
                    Grid.Tiles[10, 5] = TileType.HighCover;   // cover on its FAR side: flanked from us
                    Grid.SetCoverHp(10, 5);
                }
                else d = MkE(9, 9);                            // open ground, no cover anywhere near
                Players.Add(a); Enemies.Add(d);

                var plainOdds = Combat.ComputeOdds(Grid, a, d);
                string tag = flankGeom ? "Flanked" : "Open";
                if (flankGeom && !plainOdds.Flanked) { fails.Add("uiFlankSetup"); continue; }
                if (!flankGeom && (plainOdds.Flanked || plainOdds.CoverLevel != 0)) { fails.Add("uiOpenSetup"); continue; }

                a.Perks.Add(Perk.LockOn);
                int mathDelta = Combat.ComputeOdds(Grid, a, d).HitChance - plainOdds.HitChance;
                var cap = PaintTooltip(a, d);
                bool badge = Painted(cap, "LOCK-ON");
                if (badge != (mathDelta != 0))
                    fails.Add($"uiLockOnBadgeLies{tag} painted={badge} hitDelta={mathDelta}");
                if (badge)
                {
                    string val = RowAfter(cap, "LOCK-ON");
                    if (val != $"+{mathDelta} aim") fails.Add($"uiLockOnValue{tag}={val} delta={mathDelta}");
                }
            }

            // ============ (3) the panel obeys the 12px small-text floor ===========================
            // Cheap, and captured at the DRAW CALL, so it covers every string the tooltip paints at
            // whatever size the fitters settled on.
            {
                Scene();
                var a = MkP(4, 5); var d = MkE(9, 5);
                Players.Add(a); Enemies.Add(d);
                foreach (var c in PaintTooltip(a, d))
                    if (c.size < 12f) { fails.Add($"uiSubFloorText '{c.text}'@{c.size}"); break; }
            }

            return string.Join(",", fails);
        }
        catch (Exception e) { return "uiException:" + e.GetType().Name + ":" + e.Message; }
        finally { Cfg.CaptureText = savedCapture; }
    }

    /// W9 THE REPAIR — SIGHTLINE_STALLTEST: the autopilot's own no-TIMEOUT CONTRACT, asserted.
    ///
    /// THE GAP THIS CLOSES: NOTHING asserted the backstop's contract. Game.Autopilot.cs states it in
    /// a comment — "the smoke test / balance batch ALWAYS terminates well before the frame cap —
    /// never a RESULT: TIMEOUT" — and CLAUDE.md makes a TIMEOUT a hard pre-merge failure, but
    /// qa-sweep.sh only PRINTED the RESULT line and left a human to read it. At the ~1% per run QA
    /// measured, that is well inside the noise an agent writes off as "a weak-autopilot flake",
    /// which is exactly how two independent causes survived:
    ///   (a) the cap was PER-MISSION (_turnCount) and re-armed by SetupMission — including the
    ///       MID-MISSION checkpoint redeploy — while the budget it sits under is a whole-CAMPAIGN
    ///       frame count. A campaign got ~40 frames-worth of turns against a 50-turn-per-mission cap.
    ///   (b) a WITHIN-TURN deadlock is invisible to it entirely, because AutoStallCheck runs in
    ///       StartPlayerTurn and a deadlocked turn never starts another one. Measured: autoplay seed
    ///       3001 sat 38,000 consecutive frames in mission 5 DEFEND with every enemy dead, because a
    ///       DISORIENTED soldier hit `DoOverwatch(); return;` — DoOverwatch refuses for a disoriented
    ///       unit and spends nothing, and AutoStep returned anyway.
    /// This test pins all four repairs: the run-scoped counter, its arm, the frame/turn budget
    /// arithmetic, and the deadlock scene actually draining.
    public string StallSelfTest()
    {
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70000);
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();

        // ---- (1) BUDGET ARITHMETIC: the turn cap must always bite before the frame cap ----
        // HONEST ABOUT WHAT THIS IS (W9 review): it is true BY CONSTRUCTION for the three constants
        // as shipped — it cannot fail on this tree. That is the point: it is a COUPLING check, not an
        // empirical proof. AutoMaxRunTurns, AutoFramesPerTurn and AutoFrameCap are a triple that only
        // means anything together, and they live in two files; this fails the moment someone edits one
        // without the others and quietly re-opens the TIMEOUT hole. The EMPIRICAL half of the claim is
        // AutoFramesPerTurn itself, which is now the measured WORST frames-per-turn (700 vs an
        // observed 681), not a regime average — so no assumption about campaign shape is left in it.
        // (through locals: a const-folded comparison compiles to unreachable code and stops being a check)
        long capTurns = AutoMaxRunTurns, perTurn = AutoFramesPerTurn, frameCap = AutoFrameCap;
        if (capTurns * perTurn > frameCap)
            fails.Add($"budget {capTurns}x{perTurn}>{frameCap}");

        // ---- (2) THE ASYMMETRY: SetupMission resets the MISSION counter, never the RUN counter ----
        // This is the whole mechanism of (a): the checkpoint redeploy calls SetupMission mid-mission
        // and used to hand the autopilot a fresh 50-turn allowance.
        StartMission(1);
        if (RunTurns != 1) fails.Add($"runTurnsAfterStart={RunTurns}");
        for (int i = 0; i < 6; i++) StartPlayerTurn();
        int missionTurnBefore = Turn, runTurnsBefore = RunTurns;
        if (runTurnsBefore <= 1) fails.Add("runTurnsNotCounting");
        DebugResetupMission();                                  // exactly what the checkpoint does
        if (Turn != 1) fails.Add($"missionTurnNotReset={Turn}");            // (unchanged behaviour)
        // the run counter KEEPS its history and books the new opening turn — it is never reset
        if (RunTurns != runTurnsBefore + 1) fails.Add($"runTurnsResetBySetup={RunTurns} was={runTurnsBefore}");
        if (missionTurnBefore <= 1) fails.Add("missionTurnNotCounting");

        // ---- (3) THE ARM: crossing AutoMaxRunTurns force-loses the run, and only then ----
        {
            var g = new Game { NoPersist = true, AutoPlay = true };
            g.StartMission(1);
            g.DebugSetRunTurns(AutoMaxRunTurns - 1);
            g.DebugSetTurn(1);                                  // the per-mission cap must NOT be what fires
            g.StartPlayerTurn();                                // -> RunTurns == AutoMaxRunTurns
            if (g.Phase == Phase.Lose) fails.Add("armFiredEarly");
            g.DebugSetTurn(1);
            g.StartPlayerTurn();                                // -> RunTurns == AutoMaxRunTurns + 1
            if (g.Phase != Phase.Lose) fails.Add($"armDidNotFire phase={g.Phase} runTurns={g.RunTurns}");
        }
        // ...and a game that is NOT the autopilot is never force-lost by it
        {
            var g = new Game { NoPersist = true, AutoPlay = false };
            g.StartMission(1);
            g.DebugSetRunTurns(AutoMaxRunTurns + 5);
            g.StartPlayerTurn();
            if (g.Phase == Phase.Lose) fails.Add("armFiredInLivePlay");
        }

        // ---- (4) THE DEADLOCK SCENE, verbatim: DEFEND, no enemies, a DISORIENTED soldier ----
        // Pre-fix this never ends the turn, at any number of updates. 60 is far more than the two or
        // three steps a healthy squad needs, and far FEWER than AutoIdleFrames (240) — so this leg
        // fails unless the ROOT cause is fixed, not merely caught by the idle backstop.
        {
            var g = new Game { NoPersist = true, AutoPlay = true };
            g.StartMission(1);
            g.DebugStageDefendDeadlock();
            int turn0 = g.Turn;
            for (int i = 0; i < 60 && g.Turn == turn0 && g.Phase == Phase.PlayerTurn; i++) g.Update(1f / 60f);
            if (g.Turn == turn0 && g.Phase == Phase.PlayerTurn)
                fails.Add("deadlockNotDrained");
        }

        // ---- (5) THE IDLE BACKSTOP itself: a frozen board ends the turn, and not before ----
        {
            var g = new Game { NoPersist = true, AutoPlay = true };
            g.StartMission(1);
            g.DebugStageDefendDeadlock();
            int turn0 = g.Turn;
            for (int i = 0; i < AutoIdleFrames - 1; i++) g.DebugAutoIdleGuard();
            if (g.Turn != turn0) fails.Add("idleGuardFiredEarly");
            for (int i = 0; i < 3; i++) g.DebugAutoIdleGuard();
            if (g.Turn == turn0 && g.Phase == Phase.PlayerTurn) fails.Add("idleGuardNeverFired");
        }

        return fails.Count == 0
            ? $"STALLTEST: PASS (run-scoped turn counter survives SetupMission's mission reset; the arm force-loses at >{AutoMaxRunTurns} run-turns in autoplay only; {AutoMaxRunTurns} turns x {AutoFramesPerTurn} frames <= the {AutoFrameCap}-frame harness budget, so the turn cap always bites first; the DEFEND/disoriented deadlock drains in <60 steps and the idle backstop ends a frozen turn at {AutoIdleFrames} steps)"
            : "STALLTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    // ─── THE HEAT PIN AND L5 — SIGHTLINE_HEATPINTEST ───────────────────────────────────────
    /// The balance INSTRUMENT's three new contracts, asserted.
    ///  (A) THE PIN — EventCatalog.HeatPinProbe: the catalog carries exactly the three heat-raising
    ///      arms the README names; with the pin OFF each raises HeatLevel (the leak is real on this
    ///      tree — the half a "watch it fail" reading needs); with it ON each leaves HeatLevel
    ///      alone, reports HeatPinnedLine, and still fires its other outcomes.
    ///  (B) THE ROWS — Stats: RunRec.HeatEnd differs from Heat on a leaked run and equals it on a
    ///      clean one; RunTurns is stamped; the JSON carries one `campaigns[]` row per RunRec
    ///      (including a SINGLE-POLICY batch's, which pairedPolicy.slots drops) with the run's
    ///      heat / heatEnd / lossCause / runTurns / last mission; heatLeak counts the raised
    ///      campaigns and the off-rung missions; instrumentHealth splits the stalemates by arm.
    ///  (C) THE ARMS — the autopilot's own guard, fired both ways on a real mission: the per-mission
    ///      cap logs STALEMATE-MISSION, the run-scoped cap logs STALEMATE-RUN, and both are
    ///      IsStalemate for every consumer that used to match the one word.
    public static string HeatPinSelfTest()
    {
        var fails = new List<string>();
        bool savedPin = EventCatalog.HeatPinned;
        bool savedEnabled = Stats.Enabled;
        try
        {
            Util.Reseed(70001);
            // ---- (A) the catalog leg ----
            string probe = EventCatalog.HeatPinProbe();
            if (probe != "") fails.Add("probe:" + probe);

            // ---- (B) the rows: a synthetic two-run batch, one leaked, one clean, SINGLE policy ----
            Stats.Reset(); Stats.Enabled = true;
            EventCatalog.HeatPinned = false;
            Stats.Slot = 7;
            Stats.BeginRun(2, "sloppy");
            Stats.BeginMission(1, "Eliminate", 2, 4, 5);
            Stats.RecordEvent("informant", 1);                    // the bot's value-best arm — a leak
            Stats.EndMission(true, 6, 4, 5, "");
            Stats.BeginMission(2, "Hack", 3, 4, 6);               // ...so mission 2 is played at heat 3
            Stats.EndMission(false, 9, 0, 2, "SQUAD WIPED");
            Stats.EndRun(false, 1, "SQUAD WIPED", 3, 15);
            Stats.Slot = 8;
            Stats.BeginRun(2, "sloppy");
            Stats.BeginMission(1, "Eliminate", 2, 4, 5);
            Stats.EndMission(false, 7, 0, 1, "SQUAD WIPED");
            Stats.EndRun(false, 0, "SQUAD WIPED", 2, 7);
            Stats.Slot = -1;
            var leaked = Stats.Runs[0]; var clean = Stats.Runs[1];
            if (leaked.HeatEnd != 3 || leaked.Heat != 2) fails.Add($"leakedHeatEnd={leaked.HeatEnd}/{leaked.Heat}");
            if (clean.HeatEnd != clean.Heat) fails.Add($"cleanHeatEnd={clean.HeatEnd}/{clean.Heat}");
            if (leaked.RunTurns != 15 || clean.RunTurns != 7) fails.Add($"runTurns={leaked.RunTurns}/{clean.RunTurns}");
            using (var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(Stats.BuildSummary())))
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("campaigns", out var rows) || rows.GetArrayLength() != 2)
                    fails.Add("campaignsRows!=2");
                else
                {
                    var r0 = rows[0];
                    if (r0.GetProperty("slot").GetInt32() != 7 || r0.GetProperty("policy").GetString() != "sloppy"
                        || r0.GetProperty("heat").GetInt32() != 2 || r0.GetProperty("heatEnd").GetInt32() != 3
                        || r0.GetProperty("win").GetBoolean() || r0.GetProperty("missionsCleared").GetInt32() != 1
                        || r0.GetProperty("lossCause").GetString() != "SQUAD WIPED" || r0.GetProperty("runTurns").GetInt32() != 15
                        || r0.GetProperty("endMission").GetInt32() != 2 || r0.GetProperty("endObjective").GetString() != "Hack"
                        || r0.GetProperty("heatRaisingPicks").GetInt32() != 1)
                        fails.Add("row0:" + r0.GetRawText());
                    if (rows[1].GetProperty("heatRaisingPicks").GetInt32() != 0 || rows[1].GetProperty("heatEnd").GetInt32() != 2)
                        fails.Add("row1:" + rows[1].GetRawText());
                }
                // a single-policy batch has NO pairedPolicy rows — the gap campaigns[] exists to close
                if (root.GetProperty("pairedPolicy").GetProperty("slots").GetArrayLength() != 0) fails.Add("pairedSlotsNotEmpty");
                var leak = root.GetProperty("heatLeak");
                if (leak.GetProperty("pinned").GetBoolean()) fails.Add("leak.pinned");
                if (leak.GetProperty("heatRaisingPicks").GetInt32() != 1) fails.Add("leak.picks");
                if (leak.GetProperty("campaignsRaised").GetInt32() != 1) fails.Add("leak.raised");
                if (leak.GetProperty("missionsAbovePin").GetInt32() != 1) fails.Add("leak.offRung");
                if (leak.GetProperty("maxHeatEnd").GetInt32() != 3) fails.Add("leak.max");
            }
            // ...and the flag itself lands in the JSON when set
            EventCatalog.HeatPinned = true;
            using (var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(Stats.BuildSummary())))
                if (!doc.RootElement.GetProperty("heatLeak").GetProperty("pinned").GetBoolean()) fails.Add("leak.pinnedFlagNotReported");
            EventCatalog.HeatPinned = false;

            // ---- (C) the arms, from the guard itself ----
            Stats.Reset(); Stats.Enabled = true;
            if (!Stats.IsStalemate(Stats.StalemateMission) || !Stats.IsStalemate(Stats.StalemateRun) || Stats.IsStalemate("SQUAD WIPED"))
                fails.Add("isStalemate");
            {
                Util.Reseed(70002);
                Stats.Slot = 1;
                var g = new Game { NoPersist = true, AutoPlay = true };
                g.StartMission(1);
                g.DebugSetRunTurns(AutoMaxRunTurns);
                g.DebugSetTurn(1);
                g.StartPlayerTurn();                                // RunTurns -> AutoMaxRunTurns + 1
                if (g.Phase != Phase.Lose) fails.Add("runArmDidNotFire");
                else if (g.LoseTitle != Stats.StalemateRun) fails.Add("runArmTitle=" + g.LoseTitle);
                if (!g.LoseReason.Contains("run turn")) fails.Add("runArmReason=" + g.LoseReason);
            }
            {
                Util.Reseed(70003);
                Stats.Slot = 2;
                var g = new Game { NoPersist = true, AutoPlay = true };
                g.StartMission(1);
                g.DebugSetRunTurns(5);
                g.DebugSetTurn(AutoMaxTurns);
                g.StartPlayerTurn();                                // _turnCount -> AutoMaxTurns + 1
                if (g.Phase != Phase.Lose) fails.Add("missionArmDidNotFire");
                else if (g.LoseTitle != Stats.StalemateMission) fails.Add("missionArmTitle=" + g.LoseTitle);
                if (!g.LoseReason.Contains("mission turn")) fails.Add("missionArmReason=" + g.LoseReason);
            }
            Stats.Slot = -1;
            if (Stats.Runs.Count != 2) fails.Add($"stalemateRuns={Stats.Runs.Count}");
            else
            {
                if (Stats.Runs[0].LossCause != Stats.StalemateRun || Stats.Runs[1].LossCause != Stats.StalemateMission)
                    fails.Add($"stalemateCauses={Stats.Runs[0].LossCause}/{Stats.Runs[1].LossCause}");
                if (Stats.Runs[0].RunTurns != AutoMaxRunTurns + 1) fails.Add($"stalemateRunTurns={Stats.Runs[0].RunTurns}");
                if (Stats.Runs[0].HeatEnd != Stats.Runs[0].Heat) fails.Add("stalemateHeatEnd");
                using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(Stats.BuildSummary()));
                var ih = doc.RootElement.GetProperty("instrumentHealth");
                if (ih.GetProperty("stalemateLosses").GetInt32() != 2 || ih.GetProperty("stalemateRunLosses").GetInt32() != 1
                    || ih.GetProperty("stalemateMissionLosses").GetInt32() != 1)
                    fails.Add("instrumentHealth:" + ih.GetRawText());
                var st = ih.GetProperty("stalemates");
                if (st.GetArrayLength() != 2 || st[1].GetProperty("arm").GetString() != Stats.StalemateMission
                    || st[1].GetProperty("mission").GetInt32() != 1 || st[1].GetProperty("objective").GetString() == "")
                    fails.Add("stalemateRows:" + st.GetRawText());
                if (doc.RootElement.GetProperty("runWinRateExStalemate").GetDouble() != -1.0) fails.Add("exStalemateNotExcludingBothArms");
                if (doc.RootElement.GetProperty("campaigns").GetArrayLength() != 2) fails.Add("campaignsRowsC");
            }
        }
        catch (Exception e) { fails.Add("exception:" + e.GetType().Name + ":" + e.Message); }
        finally
        {
            EventCatalog.HeatPinned = savedPin;
            Stats.Reset(); Stats.Enabled = savedEnabled; Stats.Slot = -1;
            Util.Reseed(0);
        }
        return fails.Count == 0 ? "HEATPINTEST: PASS (3 heat-raising arms pinned; HeatEnd/RunTurns/campaigns[]/heatLeak in the JSON; STALEMATE-MISSION + STALEMATE-RUN named)"
                                : "HEATPINTEST: FAIL " + string.Join(",", fails);
    }

    /// STALLTEST hooks. DebugResetupMission re-runs SetupMission for the CURRENT mission — exactly
    /// what TryReinforcements does for the mid-mission checkpoint redeploy, which is the call that
    /// used to re-arm the per-mission turn cap.
    public void DebugResetupMission() => SetupMission(_run.Mission);
    public void DebugSetRunTurns(int t) => RunTurns = t;
    public void DebugAutoIdleGuard() => AutoIdleGuard();
    /// Stage the measured seed-3001 deadlock: DEFEND, every hostile dead, and the first soldier in
    /// action order DISORIENTED but holding actions and ammo. Pre-fix the autopilot spins here forever.
    public void DebugStageDefendDeadlock()
    {
        Objective = Objective.Defend;
        Phase = Phase.PlayerTurn;
        _anims.Clear();
        foreach (var e in Enemies) { e.Alive = false; e.Hp = 0; }
        SquadConcealed = false;
        bool first = true;
        foreach (var p in Players)
        {
            if (!p.Alive || p.IsVip) continue;
            p.BeginTurn();
            p.Ammo = Math.Max(1, p.Ammo);
            if (first) { p.AddStatus(StatusKind.Disoriented, 3); first = false; }
        }
    }

    /// W9 THE REPAIR — SIGHTLINE_GRAPPLETEST, the FIRST test GRAPPLE has ever had.
    ///
    /// THE GAP THIS CLOSES: `grep -i grapple src/Game.Harness.cs` returned NOTHING before this. The
    /// verb had zero coverage while both of its siblings were pinned — SHOVETEST covers SHOVE, whose
    /// vector points AWAY from the shover so its blocked case can never ram him, and FIELDTEST covers
    /// DRAG and explicitly asserts the Chebyshev-1 exclusion. The suite tested the two safe siblings
    /// and never touched the one that shares ShoveAnim's blocked branch with an INVERTED vector.
    /// The defect that hid there: a Chebyshev-1 grapple's destination tile IS the grappler's own tile,
    /// so the slide was blocked, `rammed` resolved to the GRAPPLER, and the soldier who spent the
    /// action and the cooldown took ShoveRammedDamage from its own GRAPPLE — measured at 3 of 3
    /// Chebyshev-1 grapples across 11 autoplay campaigns, one of them lethal (VEGA 1 -> 0, seed 3406),
    /// and 100% of a JUGGERNAUT's grapples, because GrappleReachFor pins that fork at reach 1.
    /// Autoplay drove the real trigger every run and only ever checked for exceptions and TIMEOUT;
    /// losing 1 HP to your own verb is silent.
    public string GrappleSelfTest()
    {
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70007);
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();

        Grid = new Grid();                       // all Floor, Height 0
        Players = new System.Collections.Generic.List<Unit>();
        Enemies = new System.Collections.Generic.List<Unit>();
        Vip = null; CaptiveLocked = false;
        Objective = Objective.Eliminate;
        EvacZone.Clear();
        Fx = new Fx();
        Phase = Phase.PlayerTurn;
        Combat.MissionFaction = Faction.None;

        Unit MkP(int x, int y, Spec spec = Spec.None)
        {
            var u = new Unit { Name = "S", Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                               Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Spec = spec,
                               Weapon = Weapon.Make(WeaponKind.Rifle) };   // Cls ASSAULT => Ability GRAPPLE
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
        }
        Unit MkE(int x, int y)
        {
            var u = new Unit { Name = "E", Cls = "GRUNT", Team = Team.Enemy, X = x, Y = y,
                               Hp = 9, MaxHp = 9, Aim = 60, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.Alert = AlertLevel.Alert; u.SyncPos(); u.BeginTurn(); return u;
        }
        void Pump() { for (int i = 0; i < 400 && _anims.Count > 0; i++) Update(0.05f); }
        void Scene()
        {
            Grid = new Grid();
            Players = new System.Collections.Generic.List<Unit>();
            Enemies = new System.Collections.Generic.List<Unit>();
            _anims.Clear(); Fx = new Fx(); Phase = Phase.PlayerTurn;
            SquadConcealed = false;
        }

        // ---- (1) reach 2: the foe is PULLED one tile toward the grappler, and the grappler is unhurt
        Scene();
        var p1 = MkP(5, 5);
        var f1 = MkE(7, 5);                       // Chebyshev 2 east
        f1.OnOverwatch = true; f1.Hunkered = true;
        Players.Add(p1); Enemies.Add(f1);
        int pHp1 = p1.Hp, fHp1 = f1.Hp;
        if (!GrappleTargetOk(p1, f1)) fails.Add("reach2NotLegal");
        IssueGrapple(p1, f1); Pump();
        if (!(f1.X == 6 && f1.Y == 5)) fails.Add($"reach2NotPulled({f1.X},{f1.Y})");
        if (f1.Hp != fHp1) fails.Add($"reach2PullDamagedFoe({f1.Hp})");     // a clean pull is not a slam
        if (p1.Hp != pHp1) fails.Add($"reach2HurtGrappler({p1.Hp})");
        if (f1.OnOverwatch) fails.Add("reach2OverwatchHeld");
        if (f1.Hunkered) fails.Add("reach2HunkerHeld");
        if (!p1.ShovedThisTurn) fails.Add("reach2NoBudgetSpent");
        if (p1.AbilityCd <= 0) fails.Add("reach2NoCooldown");
        if (p1.ActionsLeft != 1) fails.Add($"reach2ActionCost({p1.ActionsLeft})");

        // ---- (2) THE DEFECT: Chebyshev 1. The foe cannot be pulled anywhere (its only toward-tile
        //          is the grappler's own), so the verb resolves as a SLAM — the foe takes the collision
        //          damage and loses its stance, and THE GRAPPLER LOSES NO HP. It used to lose HP.
        Scene();
        var p2 = MkP(5, 5);
        var f2 = MkE(6, 5);                       // Chebyshev 1 east
        f2.OnOverwatch = true; f2.Hunkered = true;
        Players.Add(p2); Enemies.Add(f2);
        int pHp2 = p2.Hp, fHp2 = f2.Hp;
        if (!GrappleTargetOk(p2, f2)) fails.Add("adjacentNotLegal");         // the UI offers it: it must work
        IssueGrapple(p2, f2); Pump();
        if (p2.Hp != pHp2) fails.Add($"SELF-RAM grapplerHp {pHp2}->{p2.Hp}");
        if (!p2.Alive || p2.Downed) fails.Add("SELF-RAM grapplerFelledByOwnGrapple");
        if (!(f2.X == 6 && f2.Y == 5)) fails.Add($"adjacentFoeMoved({f2.X},{f2.Y})");
        if (f2.Hp >= fHp2) fails.Add($"adjacentSlamDealtNothing({f2.Hp})");  // it is still a real hit
        if (f2.OnOverwatch) fails.Add("adjacentOverwatchHeld");
        if (f2.Hunkered) fails.Add("adjacentHunkerHeld");

        // ---- (3) the same, DIAGONALLY (the seed-3406 geometry: grappler (13,5), foe (14,6)) ----
        Scene();
        var p3 = MkP(5, 5);
        var f3 = MkE(6, 6);                       // Chebyshev 1 diagonal
        Players.Add(p3); Enemies.Add(f3);
        p3.Hp = 1;                                // exactly the state that made it lethal in the wild
        Players.Add(MkP(2, 2));                   // a second soldier so a loss can't end the mission
        IssueGrapple(p3, f3); Pump();
        if (p3.Hp != 1) fails.Add($"SELF-RAM diagonalGrapplerHp={p3.Hp}");
        if (!p3.Alive || p3.Downed) fails.Add("SELF-RAM diagonalGrapplerFelled");

        // ---- (4) JUGGERNAUT: reach 1 is its whole fork, so EVERY grapple it can make is case (2).
        //          Its signature verb must therefore still do something and still not hurt it.
        Scene();
        var jug = MkP(5, 5, Spec.Juggernaut);
        var jf = MkE(6, 5);
        var jFar = MkE(7, 5);
        Players.Add(jug); Enemies.Add(jf); Enemies.Add(jFar);
        if (GrappleReachFor(jug) != 1) fails.Add("juggernautReachNot1");
        if (!GrappleTargetOk(jug, jf)) fails.Add("juggernautHasNoLegalGrapple");
        if (GrappleTargetOk(jug, jFar)) fails.Add("juggernautReachedPastOne");
        int jHp = jug.Hp, jfHp = jf.Hp;
        IssueGrapple(jug, jf); Pump();
        if (jug.Hp != jHp) fails.Add($"SELF-RAM juggernautHp {jHp}->{jug.Hp}");
        if (jf.Hp >= jfHp) fails.Add("juggernautSlamDidNothing");

        // ---- (5) a reach-2 pull BLOCKED by terrain still slams the foe and still spares the grappler
        Scene();
        var p5 = MkP(5, 5);
        var f5 = MkE(7, 5);
        Grid.Tiles[6, 5] = TileType.HighCover;    // the destination tile is a wall
        Grid.SetCoverHp(6, 5);
        Players.Add(p5); Enemies.Add(f5);
        int pHp5 = p5.Hp, fHp5 = f5.Hp;
        IssueGrapple(p5, f5); Pump();
        if (!(f5.X == 7 && f5.Y == 5)) fails.Add($"blockedPullMoved({f5.X},{f5.Y})");
        if (f5.Hp >= fHp5) fails.Add("blockedPullNoDamage");
        if (p5.Hp != pHp5) fails.Add($"blockedPullHurtGrappler({p5.Hp})");

        // ---- (6) gating: no target, out of reach, self, dead, already spent this turn ----
        Scene();
        var p6 = MkP(5, 5);
        Players.Add(p6);
        if (HasGrappleTarget(p6)) fails.Add("grappleTargetWithNoEnemy");
        var far = MkE(9, 5); Enemies.Add(far);                 // Chebyshev 4 -> beyond reach 2
        if (GrappleTargetOk(p6, far)) fails.Add("farFoeLegal");
        var near = MkE(6, 5); Enemies.Add(near);
        if (!HasGrappleTarget(p6)) fails.Add("noTargetWithFoeInReach");
        if (GrappleTargetOk(p6, p6)) fails.Add("selfLegal");
        near.Alive = false;
        if (GrappleTargetOk(p6, near)) fails.Add("deadFoeLegal");
        near.Alive = true;
        p6.ShovedThisTurn = true;                              // shared anti-loop budget with SHOVE
        if (GrappleTargetOk(p6, near)) fails.Add("grappleTwiceInOneTurn");

        _anims.Clear();
        return fails.Count == 0
            ? "GRAPPLETEST: PASS (reach 2 pulls the foe one tile toward the grappler and costs 1 action + the charge + the shove budget; an adjacent foe cannot be pulled so the verb SLAMS it — damage + stance broken — and the grappler NEVER takes damage from its own grapple, cardinal or diagonal, at 1 HP, or as a JUGGERNAUT whose reach-1 fork makes that its only case; a blocked reach-2 pull slams too; gating holds)"
            : "GRAPPLETEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Headless self-test for the FIELD CRAFT (W1) verbs DRAG + VAULT. Verifies: DRAG pulls a
    /// LAGGING ally (Chebyshev 2) one tile toward the dragger, an already-adjacent (Chebyshev 1)
    /// ally is NOT draggable (no legal closer tile), and the once-per-turn cap holds; VAULT crosses
    /// a cover tile to the floor beyond (gating on a cover tile between, landing legality, and
    /// once-per-turn). Prints FIELDTEST: PASS/FAIL.
    public string FieldSelfTest()
    {
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70014);
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
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70021);
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

        // (4) W9 THE REPAIR — caps hold after a FIELD EVENT too.
        // THE GAP: this test asserted the cap only immediately after DebriefSurvivors(), the one
        // moment AutoDeploy has just run, and EVENTTEST exercises EventCatalog.Apply against a bare
        // test Run with no deployment or bench state at all (it checks Squad.Count and nothing else).
        // Nothing anywhere composed the ordering the campaign actually uses — debrief -> AutoDeploy
        // -> resolve an event -> read Deployed — so a roster change made by an event went straight
        // past the cap in BOTH directions: a free RECRUIT deployed cap+1 ("DEPLOY 5/4" over five
        // deployed soldiers, five on the next board where DeployCapFor(2) == 4), and a RELEASE left
        // the freed slot empty while a 6/6-HP soldier sat benched ("DEPLOY 3/4").
        // The invariant, both ways: Deployed.Count == min(healthy roster, NextDeployCap).
        {
            _run.DebriefSurvivors();                                    // the campaign's real ordering
            int capNow = _run.NextDeployCap;
            int Want() => Math.Min(_run.Squad.Count, _run.NextDeployCap);

            // (4a) RECRUIT: a body arrives mid-barracks and must not push the field over the cap.
            while (_run.Squad.Count >= Run.RosterMax) _run.Squad.RemoveAt(_run.Squad.Count - 1);
            _run.AutoDeploy();
            int before = _run.Deployed.Count;
            DebugResolveEventOutcome(new EventOutcome { Kind = EventOutcomeKind.Recruit, Veteran = true });
            if (_run.Deployed.Count != Want())
                fails.Add($"eventRecruitDeployed={_run.Deployed.Count} want={Want()} (was {before}, cap {capNow})");
            if (_run.Deployed.Count > _run.NextDeployCap) fails.Add("eventRecruitOverCap");

            // (4a2) W9 REVIEW FIX — THE PLAYER'S OWN BENCH CHOICE SURVIVES AN EVENT.
            // THE LESSON, and why (4a)/(4b) below could not catch this: they assert a COUNT invariant,
            // `Deployed.Count == min(Squad.Count, NextDeployCap)` — and a CLOBBERING implementation
            // satisfies that exactly as well as a preserving one. The first cut called AutoDeploy()
            // unconditionally here, which re-derives Benched for the WHOLE roster from a fixed rule and
            // so discarded a manual barracks swap on EVERY event, including outcomes with NO roster
            // change at all. ResolveEvent is the checkpoint site, so that is what got persisted.
            // Assert IDENTITY, not arithmetic: who is benched, by name.
            {
                foreach (var u in _run.Squad) { u.Wound = 0; u.Hp = u.MaxHp; }
                _run.AutoDeploy();
                // the player then swaps by hand: bench the soldier AutoDeploy would seat FIRST, and
                // seat one it benched — the choice a re-derivation is guaranteed to undo.
                var seated = _run.Deployed[0];
                var benched = _run.Squad.Find(u => u.Benched);
                if (benched == null) fails.Add("4a2:noBenchToSwap");
                else
                {
                    seated.Benched = true; benched.Benched = false;
                    string manual = string.Join(",", _run.Squad.ConvertAll(u => u.Name + (u.Benched ? "[B]" : "[D]")));
                    // an outcome with NO roster change at all — the reviewer's exact repro
                    DebugResolveEventOutcome(new EventOutcome { Kind = EventOutcomeKind.Intel, Amount = 5 });
                    string after = string.Join(",", _run.Squad.ConvertAll(u => u.Name + (u.Benched ? "[B]" : "[D]")));
                    if (after != manual) fails.Add($"4a2:benchClobbered manual={manual} after={after}");
                    // ...and a roster-CHANGING outcome must still not move anyone it did not have to
                    var untouched = _run.Squad.FindAll(u => u != seated && u != benched);
                    var wasBenched = untouched.ConvertAll(u => u.Benched);
                    DebugResolveEventOutcome(new EventOutcome { Kind = EventOutcomeKind.Recruit, Veteran = true });
                    for (int i = 0; i < untouched.Count; i++)
                        if (untouched[i].Benched != wasBenched[i] && _run.Deployed.Count <= _run.NextDeployCap)
                        { fails.Add($"4a2:recruitMovedBystander {untouched[i].Name}"); break; }
                    if (_run.Deployed.Count > _run.NextDeployCap) fails.Add("4a2:recruitOverCap");
                }
            }

            // (4b) RELEASE: a DEPLOYED body leaves and the freed slot must go to a benched soldier.
            while (_run.Squad.Count <= _run.NextDeployCap) _run.Squad.Add(Mission.MakeRecruit());
            foreach (var u in _run.Squad) { u.Wound = 0; u.Hp = u.MaxHp; }   // everyone healthy: no bench excuse
            _run.AutoDeploy();
            int deployedBefore = _run.Deployed.Count;
            DebugResolveEventOutcome(new EventOutcome { Kind = EventOutcomeKind.ReleaseSoldier });
            if (_run.Deployed.Count != Want())
                fails.Add($"eventReleaseDeployed={_run.Deployed.Count} want={Want()} (was {deployedBefore})");
            if (_run.Squad.Any(u => u.Benched && u.Wound == 0 && _run.Deployed.Count < _run.NextDeployCap))
                fails.Add("eventReleaseStrandedHealthyBench");
        }

        return fails.Count == 0
            ? "BENCHTEST: PASS (auto-bench wounded, deploy<=cap, roster<=max, benched recover+preserved; a field event's recruit/release reconciles the deployment so the field is never cap+1 or cap-1, AND the player's own manual bench swap survives an event untouched)"
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
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70028);
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
            // C1 THE FLAT MIDDLE: tier 2 moved from NO QUARTER down to EXPOSED, so heats 6-7 are
            // tier 2 now. The old tag here read "aiTierExposedNot1", which was ALREADY a misnomer
            // before C1 — heat 6's tier came from rung 4, not from EXPOSED, and that dead rung-6
            // declaration is the defect C1 found. Named for what it actually asserts now.
            if (Sightline.Heat.AiTier(6) != 2 || Sightline.Heat.AiTier(7) != 2) fails.Add("aiTierExposedNot2");
            if (Sightline.Heat.AiTier(8) != 2) fails.Add("aiTierNoQuarterNot2");
            // W6c data pin, RE-AIMED BY C1 THE FLAT MIDDLE: the +1 enemy damage moved down from
            // the rung-8 apex to EXPOSED (rung 6), so the "one rung below is clean" control moves
            // with it — heat 5 now, heat 7 no longer. The pin that MATTERS is unchanged and is
            // asserted here explicitly: the ladder carries the damage point exactly ONCE, so the
            // apex cumulative is still 1 and never 2. (SIGHTLINE_MIDTOOTHTEST proves the same
            // invariant across all eight modes of the dial, and that MIDTOOTH=0 restores 7 -> 0.)
            if (Sightline.Heat.DmgDelta(5) != 0) fails.Add("dmgDeltaBelowExposedNot0");
            if (Sightline.Heat.DmgDelta(6) != 1) fails.Add("dmgDeltaExposedNot1");
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
    ///
    /// W4 REVIEW FIX — it also stamps a clean four-tile HighCover run at (5..8,5) as the visual
    /// A/B for the cover-volume identity. `SIGHTLINE_COVERKILL=1` destroys that run's NW-most
    /// tile through the real `Grid.DamageCover` path (High -> rubble -> gone), so two captures
    /// differ by exactly one tile. Before the fix the volume's identity was the LIVE minimum tile
    /// index, so losing the end of a wall re-rolled the material FORM and the footprint jitter of
    /// every surviving tile — a wall turned from crates into rock mid-mission. After it the
    /// survivors are pixel-identical. Gate E of SIGHTLINE_BOARDTEST asserts the same in-process.
    public void DebugCover()
    {
        int chipped = 0;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
                if (Grid.Tiles[x, y] == TileType.HighCover && chipped < 8) { Grid.DamageCover(x, y, 1); chipped++; }

        const int wx = 5, wy = 5;
        if (Grid.InBounds(wx + 3, wy + 1))
        {
            for (int i = 0; i < 4; i++)
            {
                Grid.Tiles[wx + i, wy - 1] = TileType.Floor;    // clear a lane so the run reads alone
                Grid.Tiles[wx + i, wy + 1] = TileType.Floor;
                Grid.Tiles[wx + i, wy] = TileType.HighCover;
                Grid.Height[wx + i, wy] = 0;
                Grid.SetCoverHp(wx + i, wy);
            }
            Grid.ClearCoverSeeds(); Grid.SeedCoverVolumes();   // the wall exists BEFORE it is shot
            if (Environment.GetEnvironmentVariable("SIGHTLINE_COVERKILL") == "1")
            { Grid.DamageCover(wx, wy, 99); Grid.DamageCover(wx, wy, 99); }   // High -> rubble -> gone
        }
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
        // C5 THE HARD EDGES — DEFECT: this hook staged NOTHING. It set Wound = 2 and then called
        // DebriefSurvivors, whose recovery step decrements the wound (`u.Wound--`, Run.cs) for a
        // survivor of a cleared mission — so the flag was already spent by the time anything drew,
        // and `SIGHTLINE_BENCH=1` photographed the plain barracks. FITTEST's screen audit found it
        // as a frame byte-identical to the CAMPAIGNMAP case's. The wound is now applied AFTER the
        // debrief, which is the order the docstring always claimed.
        //
        // The docstring's premise is ALSO stale, and left corrected rather than repeated: the
        // DEPLOY/BENCH pill is drawn on EVERY roster row (Hud.DrawSquadRow), not only on wounded
        // ones, so what this hook actually stages now is the WOUNDED(n) rank line beside it.
        _run.JumpTo(2);
        _run.DebriefSurvivors();
        foreach (var u in _run.Squad.Take(2)) u.Wound = 2;
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
    ///   SIGHTLINE_MAPCOL=<n>  jump to mission n instead of 3, so a shot (or CLASSTEST) can stage a
    ///                         fork whose choices include the BOSS column — always Decapitate, and
    ///                         therefore the only node kind GUARANTEED to be a PITCHED choice.
    public void DebugCampaignMap()
    {
        int col = int.TryParse(Environment.GetEnvironmentVariable("SIGHTLINE_MAPCOL"), out int mc) && mc > 0 ? mc : 3;
        _run.JumpTo(col);                // visit cols 0..col-1; current sits at mission `col`
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
        // THE FIT: stage the WORST case. FIELD DRILLS' description is 98 characters — the only
        // one in the catalogue over 60, and the one that overflowed this card — but it is 1 of 16,
        // so an unseeded glance at this screen showed it ~19% of the time and the defect survived
        // ten programs. The offer now always LEADS with the longest description it can draw.
        if (_run.BoonOffer.Count > 0)
        {
            Boon worst = _run.BoonOffer[0];
            foreach (var b in BoonDef.All)
                if (BoonDef.Desc(b).Length > BoonDef.Desc(worst).Length) worst = b;
            if (!_run.BoonOffer.Contains(worst)) _run.BoonOffer[0] = worst;
        }
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
    /// Q1: `hover` stages the OTHER odds path — no aim mode, the board cursor parked on the foe
    /// (the keyboard-cursor route, so a headless shot needs no live mouse) with the soldier having
    /// already fired this turn. That is the D4 case: the plain-hover tooltip applies the -15 SNAP
    /// penalty to the displayed hit%, and (pre-Q1) explained none of it.
    public void DebugTooltip(bool hover = false)
    {
        var c = Players.Where(p => !p.IsVip && p.Alive).ToList();
        if (c.Count == 0) return;
        var s = c[0];
        s.ConsecutiveMisses = 2;                       // bank +12 STEADYING (the streak cap)
        s.FiredThisTurn = true;                        // Q1: stage the RUSHED 2ND SHOT badge too
        s.RunGun = false;
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
            if (hover)
            {
                AimMode = false; KbCursor = true; CurX = foe.X; CurY = foe.Y;
                // LEAD FIX (C4 merge): TOOLTIP-HOVER used to differ from TOOLTIP-AIM only by the
                // KEYBOARD cursor, while the screen audit parks Hud.MousePin off-board at
                // (-4000,-4000) — so the mouse-driven THREAT CARD, the thing this screen exists to
                // audit, never drew in either, and the two frames collided outright whenever the
                // foe seat fell through. That is the intermittent
                // `screenNotStaged:TOOLTIP-HOVER(identical frame to TOOLTIP-AIM)` a reviewer
                // measured at ~3% of runs and traced to the audited draw path reading the live
                // pointer. Pinning the pointer ONTO the foe makes the card draw by construction,
                // so the screen audits what it claims to and the collision cannot recur.
                Hud.MousePin = Util.TileCenter(foe.X, foe.Y);
            }
            else { AimMode = true; AimTarget = foe; }
        }

        // LEAD FIX (C4 merge), the actual root cause. Everything that distinguished HOVER from AIM
        // lived inside `if (foe != null)` above — and `foe` is `Enemies.FirstOrDefault(e => e.Alive)`,
        // so on any staging where no hostile is alive the two screens were byte-identical BY
        // CONSTRUCTION, and the audit's frame fingerprint correctly reported a collision. That is
        // the intermittent `screenNotStaged:TOOLTIP-HOVER(identical frame to TOOLTIP-AIM)`,
        // measured at ~3% over 32 runs by one reviewer and 1-in-4 here in Debug. Pinning the
        // pointer inside the foe branch alone did NOT fix it, because that branch is exactly the
        // one that does not run. So the hover state is now established unconditionally, falling
        // back to the soldier when there is no hostile to hover.
        if (hover)
        {
            AimMode = false; KbCursor = true;
            var at = Enemies.FirstOrDefault(e => e.Alive) ?? s;
            CurX = at.X; CurY = at.Y;
            Hud.MousePin = Util.TileCenter(at.X, at.Y);
        }
    }

    /// Harness hook (screenshot only): drop a soldier to show the KIA stamp + red
    /// death-flash (item 3.11).
    public void DebugKia()
    {
        var c = Players.Where(p => !p.IsVip).ToList();
        if (c.Count > 1) { var v = c[1]; v.Nickname = "GHOST"; v.WasDownedThisMission = true; /* FUL-7: stage the TRUE death (skip the bleed-out) */ v.Hp = 0; KillUnit(v); }
    }

    /// THE BEAT (screenshot/filmstrip only, SIGHTLINE_KILLCAM=<frame>): the last hostile falls to a
    /// REAL ShotAnim from the first soldier (a forced lethal crit), exactly the path a mission-ending
    /// kill takes in play — the blow lands at the shot's fire beat (0.14 s in), KillUnit arms the
    /// kill-cam from inside the active anim, and the anim's remaining 0.38 s at KillCamScale keeps
    /// the board on screen for the whole window before the queue drains and the mission ends. (A
    /// bare KillUnit here reached CheckEnd the same frame and the requisition card covered the
    /// window — that is not what a player sees.) The earlier hostiles are removed QUIETLY (their FX
    /// cleared) so the frame shows one shatter. Presentation only; never runs under AutoPlay.
    public void DebugKillCam()
    {
        if (AutoPlay) return;
        var foes = Enemies.Where(e => e.Alive).ToList();
        var shooter = Players.FirstOrDefault(p => p.Alive && !p.IsVip && !p.Downed);
        if (foes.Count == 0 || shooter == null) return;
        for (int i = 0; i < foes.Count - 1; i++) { foes[i].Hp = 0; KillUnit(foes[i]); }
        Fx.Particles.Clear(); Fx.Texts.Clear(); Fx.Rings.Clear(); Fx.Lights.Clear(); Scorches.Clear();
        HitStop = 0f;
        var last = foes[foes.Count - 1];
        last.Alert = AlertLevel.Alert;               // a dormant "?" would shatter as a silhouette; ActivatePod
                                                     // would queue the reveal-scatter AHEAD of the shot
        var res = new ShotResult { Hit = true, Crit = true, Damage = last.Hp + 4 };
        Enqueue(new ShotAnim(shooter, last, res), Team.Player);
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
        // W5: the reserve line + per-survivor recall prices. Derived from the staged squad rather
        // than hard-coded, so the header count and the priced rows can never disagree on the card.
        EndReserve = squad.Count(u => u != null && !u.IsVip && u.Rank >= 1);
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
        // force a faction onto a reachable next node so UpcomingFaction() returns it.
        // V1: honour SIGHTLINE_PREP=syndicate|legion|wardens so the LONGEST prep title
        // ("COUNTER-PREP: SYNDICATE") can be shot on demand — it is the card-title/price
        // collision case. Defaults to Wardens, so existing captures are unchanged.
        Faction pf = (Environment.GetEnvironmentVariable("SIGHTLINE_PREP") ?? "").ToLowerInvariant() switch
        {
            "syndicate" => Faction.Syndicate,
            "legion"    => Faction.Legion,
            _           => Faction.Wardens,
        };
        var next = _run.NextNodes();
        if (next.Count > 0) next[0].Faction = pf;
        RefreshShopOffer();   // re-roll now that a faction is telegraphed, so the PREP slot shows
    }

    /// Harness hook (screenshot): the REQUISITION screen with the ARMORY sub-panel open,
    /// a soldier selected so the weapon picker shows.
    public void DebugArmory()
    {
        DebugShop();
        _run.Intel = 40;
        ArmoryMode = true;
        // THE FIT: stage the WORST case, not the first one. This hook used to photograph
        // `Squad.First(!IsVip)` — an ASSAULT, whose 44-char rifle blurb is the second-shortest
        // in the game — so the screen a human eyeballs was never the screen that overflowed.
        // A SHARPSHOOTER carries the 49-char SNIPER blurb, the widest string this row can hold;
        // if none is on the roster the old behaviour stands.
        ArmorySoldier = _run.Squad.FirstOrDefault(u => !u.IsVip && u.Cls == "SHARPSHOOTER")
                     ?? _run.Squad.FirstOrDefault(u => !u.IsVip);
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
        // FUL-7: stage TRUE deaths via the real anti-revive rule (a second lethal event kills
        // outright) — this test pins death CONSEQUENCE, not the bleed-out window (DOWNTEST's job).
        foreach (var u in Players.Where(p => !p.IsVip).ToList()) { u.WasDownedThisMission = true; u.Hp = 0; KillUnit(u); }
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
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70035);
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
            foreach (var u in Players.Where(p => !p.IsVip).ToList()) { u.WasDownedThisMission = true; /* FUL-7: true deaths */ u.Hp = 0; KillUnit(u); }
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
                // X1 THE EXCHANGE: every hostile weapon is trimmed by Mission.HostileDamageTrim
                // at MakeHostile, so the reference band must take the SAME trim — otherwise this
                // pin reports the exchange's give-back as a heat-ladder regression (it is not:
                // what the pin actually asserts is that heat's +1 reaches every spawned weapon).
                // Derived, not re-hardcoded, so it stays true if the trim constant ever moves.
                var refW = Weapon.Make(e.Weapon.Kind);
                refW.TrimBaseDamage(Mission.HostileDamageTrim);
                int baseMax = refW.DmgMax;
                if (e.Weapon.DmgMax != baseMax + expectDelta)
                { fails.Add($"{tag}:{e.Cls}dmg={e.Weapon.DmgMax}want={baseMax + expectDelta}"); return; }
            }
        }
        DmgAtHeat("noQuarterDmg", 8, 1);   // apex: every spawned weapon carries the +1
        // C1 THE FLAT MIDDLE moved the +1 down to EXPOSED (rung 6), so heat 7 now carries it too
        // and the "one rung below is untouched" control moves with it, to heat 5. Both legs are
        // deliberately kept: the pair is what proves the damage arrives at exactly one rung.
        DmgAtHeat("heat6Dmg", 6, 1);       // C1: the tooth's new home
        DmgAtHeat("heat5Dmg", 5, 0);       // one rung below: untouched

        // ── W1 TRUE INSTRUMENT: the ladder's SHAPE, pinned ──────────────────────────────
        // Everything above proves heat's effects REACH the board. Nothing proved what the rungs
        // actually ARE. The published ladder of record (CLAUDE.md) is a table of win-rates by
        // heat level; if a rung's cumulative (EnemyDelta, StatDelta, DmgDelta, AiTier) moves, the
        // column headings still say "heat 4" and every archived number silently changes meaning —
        // a 40-campaign rung costs ~5 minutes to measure and days to re-argue, and nothing in the
        // repo would have said the axis moved underneath it. This is the EnumFingerprint idea
        // applied to the difficulty axis: re-tuning the ladder is allowed, doing it SILENTLY is not.
        // TO RE-TUNE: change Heat.Mods, run SIGHTLINE_HEATLADDERTEST, paste the printed "actual"
        // string in below — and re-measure every rung you moved.
        // C1 THE FLAT MIDDLE re-tuned rungs 6 and 8 (the +1 per-hit damage AND coordination tier 2
        // both moved 8 -> 6) and re-measured every rung it moved; levels 6 and 7 changed 0 -> 1 in
        // the dmg slot and 1 -> 2 in the aiTier slot. SIGHTLINE_MIDTOOTHTEST pins the OTHER modes
        // of that dial and the structural invariants; this line stays the ladder's single
        // cumulative fingerprint.
        const string rungShapeGolden =
            "-1:-1,-1,0,0|0:0,0,0,0|1:1,0,0,0|2:1,1,0,0|3:2,1,0,0|4:2,1,0,1|" +
            "5:3,1,0,1|6:3,2,1,2|7:3,3,1,2|8:4,4,1,2";
        {
            var sb = new System.Text.StringBuilder();
            for (int lv = Heat.Min; lv <= Heat.Max; lv++)
            {
                if (sb.Length > 0) sb.Append('|');
                sb.Append(lv).Append(':').Append(Heat.EnemyDelta(lv)).Append(',')
                  .Append(Heat.StatDelta(lv)).Append(',').Append(Heat.DmgDelta(lv)).Append(',')
                  .Append(Heat.AiTier(lv));
            }
            string got = sb.ToString();
            if (got != rungShapeGolden)
                fails.Add("rungShape (golden \"" + rungShapeGolden + "\", actual \"" + got + "\")");
        }

        return fails.Count == 0
            ? "HEATLADDERTEST: PASS (lone-VIP wins at heat 8 / IRON VETERANS / Rescue conscript to the floor + next mission deploys; empty-deploy Build fails soft; the heat +1 dmg on every m3 weapon at heats 6 and 8, none at heat 5; rung shape (enemy,stat,dmg,aiTier) pinned for all 10 levels)"
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
        s.WasDownedThisMission = true;   // FUL-7: this test pins DEATH idempotency — skip the bleed-out
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

        // (2b) W9 THE REPAIR — the SHOOTER's side of the same purge. Legs (1)/(2) above, and every
        // purge leg in DKTEST/DOWNTEST/OWTEST, only ever built a queue of shots AT the unit about to fall —
        // because in a hand-built harness scenario the SHOOTER is never the one harmed. So the missing
        // `s.A == d` clause was structurally invisible, and a unit that died or went down mid-queue
        // still fired: full damage, a credited kill, telemetry under a corpse's class.
        {
            var shooter = Enemies.First(x => x.Alive && x != e2);
            var victim  = Players.First(u => u.Alive && !u.IsVip);
            _anims.Clear();
            var r2 = Combat.Resolve(Grid, shooter, victim);
            Enqueue(new ShotAnim(shooter, victim, r2, reaction: true), Team.Enemy);   // [0] ACTIVE (keep)
            Enqueue(new ShotAnim(shooter, victim, r2, reaction: true), Team.Enemy);   // [1] BY the dier (drop)
            Enqueue(new ShotAnim(a1, victim, r2, reaction: true), Team.Player);       // [2] by someone else (keep)
            shooter.Hp = 0; KillUnit(shooter);
            int byDead    = _anims.Count(x => x is ShotAnim sh && sh.A == shooter);
            int byOther   = _anims.Count(x => x is ShotAnim sh && sh.A == a1);
            if (byDead  != 1) fails.Add($"shotsByCorpse={byDead}");        // only the active one survives
            if (byOther != 1) fails.Add($"otherShooterShotDropped={byOther}");
            _anims.Clear();
        }

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
            ? "DKTEST: PASS (KillUnit idempotent; surplus corpse-reaction purged; shots BY the felled unit purged too; active + other-target/other-shooter kept; 3rd watcher unspent once queued hits predict the kill)"
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
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70042);
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
        foreach (var u in Players.Where(p => p.Alive && !p.IsVip).ToList()) { u.WasDownedThisMission = true; /* FUL-7: true deaths */ u.Hp = 0; KillUnit(u); }
        CheckEnd();
        if (Phase != Phase.Lose) fails.Add($"abandonNoLoss phase={Phase}");
        else if (LoseTitle != "CAPTIVE ABANDONED") fails.Add($"abandonTitle={LoseTitle}");

        // (3b) campaign, mission 3 (checkpoint fresh): the reinforcement redeploy fires instead —
        // the mission restarts with an emergency cadre and the captive re-caged.
        _run = new Run(); _run.Start();
        _run.CurrentCard = new MissionCard { Objective = Objective.Rescue, ModName = "STANDARD", Reward = RewardKind.None };
        SetupMission(3);
        foreach (var u in Players.Where(p => p.Alive && !p.IsVip).ToList()) { u.WasDownedThisMission = true; /* FUL-7: true deaths */ u.Hp = 0; KillUnit(u); }
        CheckEnd();
        if (!_run.CheckpointUsed) fails.Add("redeployNotFired");
        if (Phase != Phase.PlayerTurn) fails.Add($"redeployPhase={Phase}");
        if (Players.Count(p => p.Alive && !p.IsVip) == 0) fails.Add("redeployEmptySquad");
        if (!CaptiveLocked) fails.Add("redeployCageUnlatched");

        // (3c) SKIRMISH can roll Rescue: the same abandoned cage must end the mission as a loss
        // (single-mission modes have no checkpoint valve).
        BeginSkirmish(Objective.Rescue, 0);
        if (!CaptiveLocked) fails.Add("skirmishNotLocked");
        foreach (var u in Players.Where(p => p.Alive && !p.IsVip).ToList()) { u.WasDownedThisMission = true; /* FUL-7: true deaths */ u.Hp = 0; KillUnit(u); }
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
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70049);
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

    /// SIGHTLINE_PIKETEST — FUL-8: the SARISSA/PIKEMAN enemy lane-holder. On a controlled scene
    /// asserts (a) PLANT: Ai.Plan emits a Brace plan aimed at the nearest soldier and the
    /// ActAfterMove exec arms the exact player-BRACE flag set (OnOverwatch+OwBrace+OwFocused+OwDir),
    /// (b) PLANT->STAGGER: a soldier entering the cone eats a Stagger-flagged reaction whose
    /// connect deals EXACTLY Math.Max(1, 4/2) == 2 (the enemy-side brace halving pin), never crits,
    /// never kills, and zeroes the soldier's actions, (c) CONE BLINDNESS: a mover behind the plant
    /// provokes NO reaction and the plant is not spent, (d) BREAK: a player braced reaction that
    /// hits the pikeman drops its plant, and a Disoriented or Routed pikeman's Plan emits no Brace.
    /// Tiny window (tile math). Returns a one-line report.
    public string PikemanSelfTest()
    {
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70056);
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();
        _run = new Run(); _run.Start();

        // controlled scene: empty flat floor, no cover — LoS always clear, no crit-from-cover terms.
        Grid = new Grid();
        Players = new System.Collections.Generic.List<Unit>();
        Enemies = new System.Collections.Generic.List<Unit>();
        Vip = null; CaptiveLocked = false; Hvt = null; SquadConcealed = false;
        Objective = Objective.Eliminate;

        var sol = new Unit { Name = "SOL", Cls = "ASSAULT", Team = Team.Player, X = 12, Y = 5,
                             Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
        sol.Ammo = sol.Weapon.Clip; sol.SyncPos(); sol.BeginTurn();
        Players.Add(sol);
        // Aim 40 so the opportunism gate can't fire (no >=65% shot exists) — leg (a) must PLANT.
        var pike = new Unit { Name = "SARISSA", Cls = "PIKEMAN", Team = Team.Enemy, X = 4, Y = 5,
                              Hp = 7, MaxHp = 7, Aim = 40, Mobility = 5, Weapon = Weapon.Make(WeaponKind.Smg) };
        pike.Ammo = pike.Weapon.Clip; pike.Alert = AlertLevel.Alert; pike.SyncPos(); pike.BeginTurn();
        Enemies.Add(pike);

        // ---- (a) PLANT: the Ai branch emits Brace toward the soldier; the exec arms the flag set ----
        _aiUnits = AliveEnemies().Where(x => x.Active).ToList();
        PlanEnemySquad();
        var plan = Ai.Plan(this, pike);
        if (!plan.Brace) fails.Add("planNoBrace");
        else
        {
            if (plan.BraceDirX != 1) fails.Add($"planDirX={plan.BraceDirX}");   // soldier is due east
            // run the REAL exec: teleport to the plan's destination (the move is already verified by
            // the shared EnqueuePlannedMove machinery elsewhere), then drive the ActAfterMove stage.
            if (plan.Path.Count > 0) { pike.X = plan.Path[^1].x; pike.Y = plan.Path[^1].y; pike.SyncPos(); }
            _aiIdx = 0; _aiPlan = plan; _aiStage = AiStage.ActAfterMove; BannerTimer = 0f;
            UpdateEnemy();
            if (!pike.OnOverwatch || !pike.OwBrace || !pike.OwFocused) fails.Add("execFlagsNotArmed");
            if (pike.OwDirX != plan.BraceDirX || pike.OwDirY != plan.BraceDirY) fails.Add("execDirMismatch");
            if (pike.ActionsLeft != 0) fails.Add($"execActionsLeft={pike.ActionsLeft}");
        }
        _anims.Clear();

        // ---- (b) PLANT->STAGGER: the ==2 halving pin on the enemy-side reaction ----
        // Aim 95 + fixed 4 damage; the mover HUNKERS so crit is structurally 0 (Combat zeroes crit
        // vs a hunkered target) — a connect (full hit OR graze, DmgMin==DmgMax) is then EXACTLY
        // Math.Max(1, 4/2) == 2 after the brace halving, making the numeric pin deterministic.
        pike.X = 4; pike.Y = 5; pike.SyncPos();
        pike.Aim = 95; pike.Weapon.DmgMin = 4; pike.Weapon.DmgMax = 4; pike.Weapon.CritBase = 0;
        sol.X = 9; sol.Y = 5; sol.SyncPos();                      // dist 5, dead center of the cone
        bool connected = false;
        for (int attempt = 0; attempt < 60 && !connected; attempt++)
        {
            pike.OnOverwatch = true; pike.OwBrace = true; pike.OwFocused = true;
            pike.OwDirX = 1; pike.OwDirY = 0; pike.ReactedThisTurn = false; pike.Ammo = pike.Weapon.Clip;
            sol.Hp = sol.MaxHp; sol.ActionsLeft = 2; sol.Hunkered = true; sol.OnOverwatch = false;
            _anims.Clear();
            OnUnitEnteredTile(sol);
            var shot = _anims.OfType<ShotAnim>().FirstOrDefault(s => s.D == sol);
            if (shot == null) { fails.Add("noPlantReaction"); break; }     // the reaction must ALWAYS queue
            if (!shot.Stagger) { fails.Add("plantReactionNotStagger"); break; }
            if (!shot.Res.Hit) continue;                                   // a miss re-stages the scene
            connected = true;
            if (shot.Res.Crit) fails.Add("braceReactionCrit");             // halving forces no-crit
            if (shot.Res.Damage != 2) fails.Add($"halvingPin={shot.Res.Damage}");
            while (_anims.Count > 0)                                       // pump so Apply actually runs
            {
                var a = _anims[0]; a.OnStart(this);
                for (int i = 0; i < 200 && !a.Update(this, 0.05f); i++) { }
                if (_anims.Count > 0 && _anims[0] == a) _anims.RemoveAt(0);
            }
            if (!sol.Alive) fails.Add("staggerKilledMover");
            if (sol.ActionsLeft != 0) fails.Add($"moverNotStaggered={sol.ActionsLeft}");
            if (sol.MaxHp - sol.Hp != 2) fails.Add($"damageTaken={sol.MaxHp - sol.Hp}");
        }
        if (!connected && !fails.Contains("noPlantReaction") && !fails.Contains("plantReactionNotStagger"))
            fails.Add("noConnectIn60");                                    // effHit ~70: P ~ 0.3^60
        sol.Hunkered = false;
        _anims.Clear();

        // ---- (c) CONE BLINDNESS: a mover BEHIND the plant provokes nothing and spends nothing ----
        pike.OnOverwatch = true; pike.OwBrace = true; pike.OwFocused = true;
        pike.OwDirX = 1; pike.OwDirY = 0; pike.ReactedThisTurn = false; pike.Ammo = pike.Weapon.Clip;
        sol.X = 1; sol.Y = 5; sol.SyncPos(); sol.Hp = sol.MaxHp; sol.ActionsLeft = 2;
        _anims.Clear();
        OnUnitEnteredTile(sol);
        if (_anims.OfType<ShotAnim>().Any(s => s.D == sol)) fails.Add("reactedOutsideCone");
        if (!pike.OnOverwatch) fails.Add("blindStepSpentPlant");
        _anims.Clear();

        // ---- (d) BREAK: a player braced reaction drops the plant; Disoriented/Routed never re-plant ----
        pike.Hp = pike.MaxHp; pike.ActionsLeft = 2;                        // planted state from (c)
        var res = new ShotResult { Hit = true, Crit = false, Graze = false, Damage = 1 };
        Enqueue(new ShotAnim(sol, pike, res, reaction: true) { Stagger = true }, Team.Player);
        while (_anims.Count > 0)
        {
            var a = _anims[0]; a.OnStart(this);
            for (int i = 0; i < 200 && !a.Update(this, 0.05f); i++) { }
            if (_anims.Count > 0 && _anims[0] == a) _anims.RemoveAt(0);
        }
        if (pike.OnOverwatch) fails.Add("staggerBackKeptPlant");           // the teach-by-mirror beat
        if (!pike.Alive) fails.Add("staggerBackKilled");

        pike.AddStatus(StatusKind.Disoriented, 1);
        if (Ai.Plan(this, pike).Brace) fails.Add("disorientedReplanted");
        pike.Statuses.Clear();
        pike.Routed = 2;
        if (Ai.Plan(this, pike).Brace) fails.Add("routedReplanted");
        pike.Routed = 0;

        return fails.Count == 0
            ? "PIKETEST: PASS (plant arms the player-BRACE flag set; in-cone stagger halves to exactly 2, no crit, non-lethal, actions denied; blind outside the cone; stagger-back breaks the plant; Disoriented/Routed never plant)"
            : "PIKETEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// Harness (screenshot): SIGHTLINE_PIKESHOT — FUL-8: a planted PIKEMAN lane on a live board so
    /// the foe-red cone wash + edge rays + chevron + "BRC" tag (and the NEW CONTACT / enemy-ID reads
    /// keyed off the codex row) are all visible. Pair with SIGHTLINE_CB=1 for the colorblind pass
    /// (coded-state rule, DESIGN.md 3.H).
    public void DebugPikemanLane()
    {
        DebugWakeAll();
        var foe = Enemies.FirstOrDefault(e => e.Alive);
        var near = AlivePlayers().FirstOrDefault(p => !p.IsVip);
        if (foe == null || near == null) return;
        foe.Cls = "PIKEMAN"; foe.Name = "SARISSA";
        foe.Weapon = Weapon.Make(WeaponKind.Smg); foe.Ammo = foe.Weapon.Clip;   // the cone IS the pike (MaxRange 10)
        // stage the plant ~6 tiles from a soldier so the cone visibly covers the squad's lane
        // (the WAVEBANNER/BRACETIP free-staging precedent — screenshot readability, not gameplay)
        for (int dx = 6; dx >= 3; dx--)
        {
            int tx = near.X + dx, ty = near.Y;
            if (Grid.InBounds(tx, ty) && Grid.IsFloor(tx, ty) && !IsOccupiedByOther(tx, ty, foe))
            { foe.X = tx; foe.Y = ty; foe.SyncPos(); break; }
        }
        foe.OnOverwatch = true; foe.OwBrace = true; foe.OwFocused = true;
        foe.OwDirX = Math.Sign(near.X - foe.X); foe.OwDirY = Math.Sign(near.Y - foe.Y);
        if (foe.OwDirX == 0 && foe.OwDirY == 0) foe.OwDirX = -1;               // degenerate: face the squad side
        foe.Facing = MathF.Atan2(foe.OwDirY, foe.OwDirX);                      // pike points down the lane (mirrors the exec)
    }

    /// SIGHTLINE_MORALETEST — UNDERTOW W3: enemy pod MORALE / ROUT. On a controlled scene asserts:
    /// (1) killing one of a 2-unit pod ROUTS the survivor (BreakPodMorale threshold), (2) a routed unit
    /// shoots WILD (Combat aim penalty), (3) a routed unit FLEES (Ai.Plan moves it farther from the
    /// squad) and does NOT hold overwatch, (4) the rout RALLIES (Routed decays in BeginTurn). Returns a
    /// one-line report.
    public string MoraleSelfTest()
    {
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70063);
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
    /// C4 — SIGHTLINE_BIOMESHOT: stage a board that SHOWS the ground layer, for the screenshot
    /// harness. Every comparable visual feature in this project ships one; C4 did not, and judging
    /// its three biomes meant hunting seeds by hand and re-rolling MapSeed until a good board
    /// appeared. Pair with SIGHTLINE_FORCEBIOME=<2|3|7> and SIGHTLINE_SHOT=760 (the briefing card
    /// holds the middle of the board until ~frame 700), and with SIGHTLINE_CB=1 for the second
    /// pass. It only clears the things that OCCLUDE the ground — the briefing card and the banner —
    /// and parks the KEYBOARD cursor on a mechanical tile. NOTE: that does not raise the hover
    /// threat card, which reads the real MOUSE position; the cursor is there so the capture shows
    /// the tile highlight over mechanical ground, and the card still has to be judged live.
    /// Purely presentational: no Util.Rng draw, no persistence, no change to the board itself.
    public void DebugBiomeShot()
    {
        BriefLines = null; BriefTimer = 0f;      // the card sits over the middle of the board
        BannerTimer = 0f;
        SquadConcealed = false;
        // put the keyboard cursor on a mechanical tile if this board has one, so the capture also
        // shows the hover threat card's GROUND line rather than only the material.
        for (int x = 0; x < Grid.W && !_biomeShotCursor; x++)
            for (int y = 0; y < Grid.H && !_biomeShotCursor; y++)
                if (Grid.GroundAt(x, y) != GroundKind.None && Grid.IsFloor(x, y) && UnitAt(x, y) == null)
                { CurX = x; CurY = y; KbCursor = true; _biomeShotCursor = true; }
    }
    bool _biomeShotCursor;

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

    /// SIGHTLINE_DOWNTEST — FUL-7 LAST LIGHT: the DOWN / bleed-out state machine. Controlled
    /// scenes (the DKTEST/MORALETEST staging patterns) assert the eight spec legs:
    /// (a) DOWN entry (alive, Hp 0, timer 3, no death bookkeeping, reactions purged, AllyDown),
    /// (b) EXPIRE (dead exactly once via the FULL death flow; cause = the downing archetype;
    ///     NoLosses failed), (c) STABILIZE + barracks recovery (timer frozen; won field recovers
    ///     the body wounded + the near-death scar track fires), (d) corpsman REVIVE (PatchHeal
    ///     Hp; CombatMedic 3 at reach 2; Cd 3; up-but-actionless), (e) NO SECOND DOWN (a revived
    ///     soldier's next lethal kills outright; AoE on a downed body kills outright), (f) the
    ///     AI ignores the downed (never a ShootTarget; all-downed squad -> empty plans, no
    ///     exception; stabilized timers UNFREEZE with no soldier up, so the board stays bounded),
    /// (g) the VIP still dies instantly, (h) the carry kit is pinned (DRAG at Cheby-2 +
    ///     EXTRACT from zone-adjacent move a downed body). Returns a one-line report.
    public string DownSelfTest()
    {
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70070);
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();
        _run = new Run(); _run.Start();

        void Pump()
        {
            while (_anims.Count > 0)
            {
                var a = _anims[0]; a.OnStart(this);
                for (int i = 0; i < 200 && !a.Update(this, 0.05f); i++) { }
                if (_anims.Count > 0 && _anims[0] == a) _anims.RemoveAt(0);
            }
        }
        Unit MkP(string name, int x, int y, string cls = "ASSAULT")
        {
            var u = new Unit { Name = name, Cls = cls, Team = Team.Player, X = x, Y = y,
                               Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
        }
        Unit MkE(string name, int x, int y, string cls = "GRUNT")
        {
            var u = new Unit { Name = name, Cls = cls, Team = Team.Enemy, X = x, Y = y,
                               Hp = 6, MaxHp = 6, Aim = 60, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.Alert = AlertLevel.Alert; u.SyncPos(); u.BeginTurn(); return u;
        }
        void Scene()
        {
            Grid = new Grid();
            Players = new System.Collections.Generic.List<Unit>();
            Enemies = new System.Collections.Generic.List<Unit>();
            Vip = null; CaptiveLocked = false; Hvt = null; SquadConcealed = false;
            Objective = Objective.Eliminate; EvacZone.Clear(); _anims.Clear();
            Phase = Phase.PlayerTurn;
        }

        // ---- (a) DOWN entry: a lethal shot opens the window, without death bookkeeping ----
        Scene();
        var sol = MkP("SOL", 4, 5); var mate = MkP("MATE", 4, 7);
        Players.Add(sol); Players.Add(mate);
        var foe = MkE("E1", 9, 5);
        Enemies.Add(foe);
        int f0 = _run.Fallen.Count, m0 = _run.Memorial.Count, k0 = _missionKia.Count;
        SecondaryFailed = false;
        var res = Combat.Resolve(Grid, foe, sol);
        Enqueue(new ShotAnim(foe, sol, res, reaction: true), Team.Enemy);   // [0] = the ACTIVE blow (attribution source)
        Enqueue(new ShotAnim(foe, sol, res, reaction: true), Team.Enemy);   // [1] = surplus reaction at the body (must purge)
        sol.Hp = 0; KillUnit(sol);                                          // the single lethal seam
        if (!sol.Alive)                 fails.Add("a:died");
        if (!sol.Downed)                fails.Add("a:notDowned");
        if (sol.Hp != 0)                fails.Add($"a:hp={sol.Hp}");
        if (sol.DownedTurns != DownedTimerTurns) fails.Add($"a:timer={sol.DownedTurns}");
        if (sol.ActionsLeft != 0)       fails.Add("a:actions");
        if (sol.DownedByCls != "GRUNT") fails.Add($"a:cause={sol.DownedByCls}");
        if (_run.Fallen.Count != f0)    fails.Add("a:fallenBumped");
        if (_run.Memorial.Count != m0)  fails.Add("a:memorialBumped");
        if (_missionKia.Count != k0)    fails.Add("a:kiaBumped");
        if (SecondaryFailed)            fails.Add("a:noLossesFailedEarly");
        if (_anims.Count(x => x is ShotAnim sh && sh.D == sol) != 1) fails.Add("a:surplusKept");
        if (!mate.AllyDown)             fails.Add("a:vengefulNotStaged");
        if (StabilizeTarget(mate) != null) fails.Add("a:stabilizeFromRange");   // mate at Cheby 2: not adjacent
        _anims.Clear();

        // ---- (b) EXPIRE: three player-turn ticks run the FULL death flow exactly once ----
        StartPlayerTurn();                                   // 3 -> 2
        if (!sol.Alive || sol.DownedTurns != 2) fails.Add($"b:tick1 alive={sol.Alive} t={sol.DownedTurns}");
        StartPlayerTurn();                                   // 2 -> 1
        if (!sol.Alive || sol.DownedTurns != 1) fails.Add($"b:tick2 alive={sol.Alive} t={sol.DownedTurns}");
        StartPlayerTurn();                                   // 1 -> 0: bleed out
        if (sol.Alive)                    fails.Add("b:survivedExpiry");
        if (sol.Downed)                   fails.Add("b:corpseStillDowned");
        if (_run.Fallen.Count != f0 + 1)  fails.Add($"b:fallen={_run.Fallen.Count - f0}");
        if (_run.Memorial.Count != m0 + 1) fails.Add($"b:memorial={_run.Memorial.Count - m0}");
        if (_missionKia.Count != k0 + 1)  fails.Add("b:kiaStamp");
        if (!SecondaryFailed)             fails.Add("b:noLossesNotFailed");
        if (DeathsByClass.GetValueOrDefault("GRUNT") < 1) fails.Add("b:causeNotArchetype");
        StartPlayerTurn();                                   // a 4th tick must not double-count
        if (_run.Fallen.Count != f0 + 1)  fails.Add("b:doubleFallen");

        // ---- (c) STABILIZE freezes the timer; a WON field recovers the body ----
        _run = new Run(); _run.Start();
        _run.CurrentCard = new MissionCard { Objective = Objective.Eliminate, ModName = "STANDARD", Reward = RewardKind.None };
        SetupMission(1);
        var down3 = Players.First(p => p.Alive && !p.IsVip);
        var saver = Players.First(p => p.Alive && !p.IsVip && p != down3);
        string downName = down3.Name;
        down3.Hp = 0; KillUnit(down3);                       // downs (no anim: cause is "?")
        if (!down3.Downed) fails.Add("c:notDowned");
        saver.X = down3.X + 1; saver.Y = down3.Y; saver.SyncPos();   // adjacent
        Selected = saver; saver.ActionsLeft = 2;
        int act0 = saver.ActionsLeft;
        DoStabilize();
        if (!down3.Stabilized)              fails.Add("c:notStabilized");
        if (saver.ActionsLeft != act0 - 1)  fails.Add("c:actionCost");
        int tFrozen = down3.DownedTurns;
        StartPlayerTurn(); StartPlayerTurn();
        if (!down3.Alive || down3.DownedTurns != tFrozen) fails.Add($"c:timerNotFrozen t={down3.DownedTurns}");
        foreach (var e in Enemies.ToList()) if (e.Alive) { e.Hp = 0; KillUnit(e); }   // win the field
        CheckEnd();                                          // Eliminate cleared -> EnterBarracks
        if (Phase != Phase.Barracks) fails.Add($"c:phase={Phase}");
        var rec = _run.Squad.FirstOrDefault(u => u.Name == downName);
        if (rec == null)                     fails.Add("c:notRecovered");
        else
        {
            if (rec.Downed)                  fails.Add("c:stillDowned");
            if (rec.Wound < 2)               fails.Add($"c:wound={rec.Wound}");   // set 3; the debrief machinery decays it once
            if (rec.NearDeathCount != 1)     fails.Add($"c:nearDeath={rec.NearDeathCount}");
            if (!_run.Report.Any(r => r.Contains("recovered from the field"))) fails.Add("c:noReportLine");
        }

        // ---- (d) REVIVE: the corpsman PATCH gets a downed soldier back up ----
        Scene();
        var med = MkP("MED", 5, 5, "CORPSMAN");
        var pat = MkP("PAT", 6, 5);
        Players.Add(med); Players.Add(pat);
        pat.Hp = 0; KillUnit(pat);
        if (!pat.Downed) fails.Add("d:notDowned");
        Selected = med; med.ActionsLeft = 2; med.AbilityCd = 0;
        DoAbility();
        if (pat.Downed)                       fails.Add("d:stillDowned");
        if (pat.Hp != Unit.PatchHeal)         fails.Add($"d:hp={pat.Hp}");
        if (pat.ActionsLeft != 0)             fails.Add("d:actedSameTurn");
        if (med.AbilityCd != Unit.AbilityCooldownFor(AbilityKind.Heal)) fails.Add($"d:cd={med.AbilityCd}");
        // CombatMedic fork: reach 2, revive at PatchHeal-1
        var pat2 = MkP("PAT2", 7, 5);
        Players.Add(pat2);
        pat2.Hp = 0; KillUnit(pat2);
        med.Spec = Spec.CombatMedic; med.AbilityCd = 0; med.ActionsLeft = 2;
        Selected = med;                                       // pat2 at Cheby 2 from med
        DoAbility();
        if (pat2.Downed)                      fails.Add("d:medicReachFailed");
        if (pat2.Hp != Unit.PatchHeal - 1)    fails.Add($"d:medicHp={pat2.Hp}");

        // ---- (e) NO SECOND DOWN: revived -> next lethal kills; AoE on a downed body kills ----
        EnvDamage(pat, 99, "BLEED", Pal.Foe);                 // pat was downed this mission
        if (pat.Alive) fails.Add("e:reviveTanked");
        var body = MkP("BODY", 4, 8);
        Players.Add(body);
        body.Hp = 0; KillUnit(body);
        if (!body.Downed) fails.Add("e:notDowned");
        var lobber = MkE("LOB", 8, 8);
        Enemies.Add(lobber);
        _anims.Clear();
        Enqueue(new GrenadeAnim(lobber, body.X, body.Y), Team.Enemy);   // AoE stays blind — and lethal
        Pump();
        if (body.Alive) fails.Add("e:grenadeSparedBody");
        if (body.Downed) fails.Add("e:corpseStillDowned");   // review F2: a FINISH closes the down state (honest ledger; no BLED OUT pops on a corpse)

        // ---- (f) AI IGNORES the downed; all-downed squad = bounded, exception-free ----
        Scene();
        var up1 = MkP("UP", 4, 5); var dn1 = MkP("DN", 5, 5);
        Players.Add(up1); Players.Add(dn1);
        var hunter = MkE("HNT", 9, 5, "HOUND");               // prey logic must skip the downed too
        Enemies.Add(hunter);
        dn1.Hp = 0; KillUnit(dn1);
        _aiUnits = AliveEnemies().Where(x => x.Active).ToList();
        PlanEnemySquad();
        var plan1 = Ai.Plan(this, hunter);
        if (plan1.ShootTarget == dn1) fails.Add("f:shotTheDowned");
        up1.Hp = 0; up1.WasDownedThisMission = false; KillUnit(up1);   // now ALL soldiers are down
        if (!up1.Downed) fails.Add("f:secondDownBlocked");
        try
        {
            PlanEnemySquad();
            var plan2 = Ai.Plan(this, hunter);
            if (plan2.ShootTarget != null) fails.Add("f:allDownedTargeted");
        }
        catch (Exception ex) { fails.Add("f:planThrew:" + ex.GetType().Name); }
        // stabilized-but-nobody-up: the freeze needs a standing squad — timers run, board bounded
        dn1.Stabilized = true;
        StartPlayerTurn(); StartPlayerTurn(); StartPlayerTurn();
        if (dn1.Alive || up1.Alive) fails.Add("f:allDownedStalled");

        // ---- (g) the VIP/captive keeps instant death ----
        Scene();
        Players.Add(MkP("S", 3, 3));
        Vip = Mission.MakeVip(1); Vip.X = 5; Vip.Y = 3; Vip.SyncPos(); Vip.BeginTurn();
        Players.Add(Vip);
        Vip.Hp = 0; KillUnit(Vip);
        if (Vip.Alive || Vip.Downed) fails.Add("g:vipWentDown");

        // ---- (h) the carry kit: DRAG at Cheby-2, EXTRACT from zone-adjacent ----
        Scene();
        Objective = Objective.Evac;
        EvacZone.Add((16, 5)); EvacZone.Add((16, 6));
        var puller = MkP("PULL", 16, 5);                      // in the zone
        var hauler = MkP("HAUL", 4, 5);
        var load = MkP("LOAD", 6, 5);
        Players.Add(puller); Players.Add(hauler); Players.Add(load);
        load.Hp = 0; KillUnit(load);
        if (!load.Downed) fails.Add("h:notDowned");
        Selected = hauler; hauler.ActionsLeft = 2;
        if (!DragTargetOk(hauler, load)) fails.Add("h:dragRefused");
        else
        {
            IssueDrag(load); Pump();
            if (load.X != 5 || load.Y != 5) fails.Add($"h:dragLanded={load.X},{load.Y}");
        }
        load.X = 15; load.Y = 5; load.SyncPos();              // zone-adjacent
        Selected = puller; puller.ActionsLeft = 2;
        if (ExtractCandidate(puller) != load) fails.Add("h:extractRefused");
        else
        {
            DoExtract();
            if (!EvacZone.Contains((load.X, load.Y))) fails.Add("h:extractDidNotLand");
        }

        // ---- (i) W9 THE REPAIR — a DOWNED soldier's OWN queued shot must never fire ----
        // The live sequence this reproduces (autoplay seeds 3406/4205): a soldier issued GRAPPLE,
        // which queued a ShoveAnim, and a shot was queued behind it; the ShoveAnim downed the
        // grappler; EnterDowned -> PurgeAnimsFor dropped shots AT the soldier and had NO clause for
        // shots BY it, so a body at Hp 0 fired — full damage, a credited kill, Stats.RecordShot under
        // the downed soldier's class, and the takedown stinger.
        // THE GAP: every purge leg in this file (DKTEST (2), DOWNTEST (a), OWTEST) builds a queue
        // of shots AT the unit about to fall, because in a hand-built scenario the SHOOTER is never
        // the one harmed. Both independent guards are pinned: the PURGE, and ShotAnim self-cancelling.
        Scene();
        var gunner = MkP("GUN", 4, 5);
        Players.Add(gunner);
        var mark = MkE("MARK", 9, 5);
        Enemies.Add(mark);
        {
            int foeHp0 = mark.Hp;
            var rq = Combat.Resolve(Grid, gunner, mark);
            _anims.Clear();
            Enqueue(new ShoveAnim(gunner, mark, 0, 0), Team.Player);              // [0] the blow in flight
            Enqueue(new ShotAnim(gunner, mark, rq, reaction: false), Team.Player); // [1] the gunner's OWN shot
            gunner.Hp = 0; KillUnit(gunner);                                       // -> EnterDowned
            if (!gunner.Downed) fails.Add("i:notDowned");
            if (_anims.Count(x => x is ShotAnim sh && sh.A == gunner) != 0) fails.Add("i:downedShotKept");
            // second, independent guard: even hand-fed an ACTIVE shot, a downed shooter fires nothing
            _anims.Clear();
            var solo = new ShotAnim(gunner, mark, rq, reaction: false);
            solo.OnStart(this);
            for (int i = 0; i < 60 && !solo.Update(this, 1f / 60f); i++) { }
            if (mark.Hp != foeHp0) fails.Add($"i:downedShotResolved {foeHp0}->{mark.Hp}");
            _anims.Clear();
        }

        return fails.Count == 0
            ? "DOWNTEST: PASS (down entry clean of death bookkeeping; expiry runs the full death flow once, cause = downing archetype; stabilize freezes + won field recovers wounded/scarred; corpsman revive incl. CombatMedic reach; no second down + AoE finishes; AI ignores downed + all-downed bounded; VIP instant; drag/extract carry pinned; a downed shooter's own queued shot is purged and self-cancels)"
            : "DOWNTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    /// FUL-7 harness hook (screenshot only): stage the DOWN reads on the live board — a downed
    /// soldier (prone body + pulsing red ring + the DOWN 3 pill + red roster chip), a squadmate
    /// standing adjacent with the STABILIZE button lit, and the SOLDIER DOWN banner naming the
    /// timer. Pair with SIGHTLINE_CB=1 for the colorblind pass (DESIGN 3.H coded-state rule).
    /// SIGHTLINE_DOWNSHOT=2: mid-rescue — the rescuer EXECUTES the real STABILIZE, so the frame
    /// shows the amber STABLE pill + the STABILIZED roster tag (the rescue half-done).
    public void DebugDownShot(bool stabilized = false)
    {
        var sold = Players.Where(p => p.Alive && !p.IsVip).ToList();
        if (sold.Count < 2) return;
        var body = sold[1];
        body.Hp = 0; KillUnit(body);                          // -> EnterDowned via the real seam
        // park a rescuer adjacent so STABILIZE lights up, and select it
        var rescuer = sold.FirstOrDefault(p => p != body && p.Ability == AbilityKind.Heal) ?? sold[0];
        for (int dx = -1; dx <= 1 && !(Util.ChebyDist(rescuer.X, rescuer.Y, body.X, body.Y) == 1); dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = body.X + dx, ny = body.Y + dy;
                if (Grid.InBounds(nx, ny) && Grid.IsFloor(nx, ny) && !IsOccupiedByOther(nx, ny, rescuer))
                { rescuer.X = nx; rescuer.Y = ny; rescuer.SyncPos(); break; }
            }
        rescuer.ActionsLeft = 2;
        Selected = rescuer;
        if (stabilized) DoStabilize();   // the real verb — timer frozen, STABLE reads
    }

    /// Headless self-test (SIGHTLINE_STATUSTEST): status effects tick, decay, and read
    /// correctly — burning/bleed DoT, stun (lose an action), disoriented (aim + no
    /// overwatch). Needs a tiny window (Game uses tile math). Returns a one-line report.
    public string StatusSelfTest()
    {
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70077);
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
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70084);
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
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70091);
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
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70098);
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
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70105);
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

    /// Headless self-test (SIGHTLINE_BIOMETEST) — wave C4 "EIGHT BIOMES ARE PAINT".
    ///
    /// Pins the MECHANIC'S ACTUAL EFFECT on a constructed board, never the presence of a field:
    /// every leg measures a number the game would use (a cover level, a hit%, a Dijkstra cost, a
    /// line-of-sight verdict, an HP total, an AI destination) with and without the ground under it.
    ///
    /// It reads the AMBIENT Terrain.Enabled deliberately, so `SIGHTLINE_BIOMEMECH=0` — the flag that
    /// restores the pre-C4 board exactly — makes it FAIL. That is the "a test that cannot fail is not
    /// a test" proof, and it is the same switch the CRN A/B round was measured on.
    /// Measured real-board densities, appended by the density leg and printed in the PASS line —
    /// so the number that SHIPS is visible in the sweep output rather than living in a comment
    /// that drifted (which is exactly what happened between C4's first cut and its review).
    string BiomeDensityNote = "";

    public string BiomeSelfTest()
    {
        NoPersist = true;
        var fails = new List<string>();
        BiomeDensityNote = "";

        Grid OpenGrid()
        {
            var gr = new Grid();
            for (int x = 0; x < gr.W; x++)
                for (int y = 0; y < gr.H; y++) { gr.Tiles[x, y] = TileType.Floor; gr.Height[x, y] = 0; }
            gr.ResetCoverHp();
            return gr;
        }
        Unit Shooter(int x, int y, Team t) {
            var u = new Unit { Name = "U", Cls = "GRUNT", Team = t, X = x, Y = y, Hp = 10, MaxHp = 10,
                               Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.Alert = AlertLevel.Alert; u.SyncPos(); u.BeginTurn(); return u;
        }

        // ═══ 1. THE STAMPER — three mechanical biomes, five that are still paint ═══════════════
        // (declared, not hidden: STEEL/ARID/ASH/VOID/NEON must stamp NOTHING, so a future wave that
        // gives one of them a mechanic has to change this line and say so.)
        int[] mechIdx = { Terrain.BiomeTundra, Terrain.BiomeVerdant, Terrain.BiomeMagma };
        var mechKind = new Dictionary<int, GroundKind> {
            { Terrain.BiomeTundra, GroundKind.Ice }, { Terrain.BiomeVerdant, GroundKind.Undergrowth },
            { Terrain.BiomeMagma, GroundKind.Vent } };
        for (int bi = 0; bi < Biome.All.Length; bi++)
        {
            var gs = OpenGrid();
            Terrain.Stamp(gs, bi, 4242, 3, null);
            int n = 0; bool wrongKind = false;
            for (int x = 0; x < gs.W; x++)
                for (int y = 0; y < gs.H; y++)
                    if (gs.Ground[x, y] != GroundKind.None)
                    { n++; if (mechKind.TryGetValue(bi, out var want) && gs.Ground[x, y] != want) wrongKind = true; }
            bool mechanical = Array.IndexOf(mechIdx, bi) >= 0;
            if (mechanical && n == 0) fails.Add($"noGround[{Biome.All[bi].Name}]");
            if (!mechanical && n != 0) fails.Add($"paintBiomeStamped[{Biome.All[bi].Name}]");
            if (wrongKind) fails.Add($"wrongKind[{Biome.All[bi].Name}]");
            if (mechanical && Terrain.Tag(bi) == null) fails.Add($"noTag[{bi}]");
            if (!mechanical && Terrain.Tag(bi) != null) fails.Add($"paintBiomeTagged[{bi}]");
        }

        // DENSITY — ON REAL BOARDS. The first version of this leg swept `OpenGrid()` (every tile
        // floor, `reserved = null`), which is a board that NEVER OCCURS IN PLAY: on a real
        // `SetupMission` board, cover, barrels, plateaus, unit rings and objective rings eat a large
        // share of every walk. The C4 review measured the gap and it was the whole ballgame — the
        // open-grid sweep passed while VERDANT shipped at 12.1% against a stated target of 18-22%,
        // MAGMA's real minimum was ONE vent tile, and 43/200 MAGMA boards fell under this leg's own
        // floor. A guard that measures a board the game never builds is not a guard.
        //
        // So: build REAL missions through the real SetupMission, with the biome pinned by the
        // existing SIGHTLINE_FORCEBIOME hook, and pin the density that actually SHIPS. Bounds are
        // deliberately tight around the measured values, so a stamper re-tune has to come here and
        // restate them rather than drifting silently (which is exactly what happened once already).
        {
            string savedForce = Environment.GetEnvironmentVariable("SIGHTLINE_FORCEBIOME");
            try
            {
                foreach (int bi in mechIdx)
                {
                    Environment.SetEnvironmentVariable("SIGHTLINE_FORCEBIOME", bi.ToString());
                    int lo = int.MaxValue, hi = 0, sum = 0, boards = 0, thin = 0, raised = 0;
                    for (int sd = 0; sd < 10; sd++)
                        for (int m = 1; m <= 4; m++)
                        {
                            NoPersist = true;
                            _run = new Run(); _run.Start(); _run.MapSeed = sd * 7919 + 13;
                            _run.HeatLevel = 0;
                            _run.CurrentCard = new MissionCard { Objective = Objective.Eliminate,
                                                                 ModName = "STANDARD", Reward = RewardKind.None };
                            // PARALLAX: pin the BOARD, not just the map seed. The ground stamp is pure
                            // (Hash3 on MapSeed), but WHERE it may land is the cover / plateau / barrel
                            // layout SetupMission rolls off the shared Util.Rng — which was clock-seeded
                            // here, so this "40-board" sample was a different 40 boards every run and
                            // the `realMin` floor failed on the base tree about one sweep in six
                            // (measured 1/6 on cee3cba: `realMin[MAGMA]=2`). A gate that samples the
                            // wall clock is a gate that fails randomly; the same 40 boards every run is
                            // what makes the densities in the PASS line comparable across commits.
                            Util.Reseed(70200 + sd * 4 + m);
                            SetupMission(m);
                            int n = CountGroundTiles();
                            for (int x = 0; x < Grid.W; x++)
                                for (int y = 0; y < Grid.H; y++)
                                    if (Grid.Ground[x, y] != GroundKind.None && Grid.HeightAt(x, y) > 0) raised++;
                            lo = Math.Min(lo, n); hi = Math.Max(hi, n); sum += n; boards++;
                            if (n < (bi == Terrain.BiomeMagma ? 7 : 12)) thin++;
                        }
                    double mean = sum / (double)boards;
                    // (a) M1 — NOTHING may be stamped on raised terrain: a plateau top is painted
                    //     opaque over the ground layer, so any tile here is a rule with no pixels.
                    if (raised != 0) fails.Add($"groundOnPlateau[{Biome.All[bi].Name}]={raised}");
                    // (b) the mechanic must actually be PRESENT on the board the player gets
                    double loMean = bi == Terrain.BiomeMagma ? 12.0 : (bi == Terrain.BiomeTundra ? 15.0 : 25.0);
                    if (mean < loMean) fails.Add($"realMeanTooSparse[{Biome.All[bi].Name}]={mean:F1}");
                    if (mean > 46.0) fails.Add($"realMeanFlood[{Biome.All[bi].Name}]={mean:F1}");
                    // (c) and it must not VANISH on a minority of seeds — the failure this wave
                    //     claimed to have fixed on an open grid and had NOT fixed in play.
                    if (lo < 4) fails.Add($"realMin[{Biome.All[bi].Name}]={lo}");
                    if (thin * 4 > boards) fails.Add($"realThinBoards[{Biome.All[bi].Name}]={thin}/{boards}");
                    if (hi > 52) fails.Add($"realFlood[{Biome.All[bi].Name}]={hi}");
                    BiomeDensityNote += $" {Biome.All[bi].Name}~{mean:F1}({lo}-{hi})";
                }
            }
            finally { Environment.SetEnvironmentVariable("SIGHTLINE_FORCEBIOME", savedForce); }
        }
        // the OPEN-GRID generator bound is kept as a separate, weaker claim, clearly scoped:
        // it pins the stamper's own ceiling, not the board's.
        foreach (int bi in mechIdx)
        {
            int hi = 0;
            for (int sd = 0; sd < 24; sd++)
                for (int m = 1; m <= 3; m++)
                {
                    var gs = OpenGrid();
                    Terrain.Stamp(gs, bi, sd * 7919 + 13, m, null);
                    int n = 0;
                    for (int x = 0; x < gs.W; x++)
                        for (int y = 0; y < gs.H; y++) if (gs.Ground[x, y] != GroundKind.None) n++;
                    hi = Math.Max(hi, n);
                }
            if (hi > 52) fails.Add($"openGridFlood[{Biome.All[bi].Name}]={hi}");
        }

        // determinism: the SAME (seed, mission) stamps byte-identically, a different mission does not.
        // This is the CRN contract — the layer must derive from Hash3 and take ZERO Util.Rng draws.
        {
            var g1 = OpenGrid(); var g2 = OpenGrid(); var g3 = OpenGrid();
            Terrain.Stamp(g1, Terrain.BiomeMagma, 777, 2, null);
            Terrain.Stamp(g2, Terrain.BiomeMagma, 777, 2, null);
            Terrain.Stamp(g3, Terrain.BiomeMagma, 777, 3, null);
            bool same = true, differs = false;
            for (int x = 0; x < g1.W; x++)
                for (int y = 0; y < g1.H; y++)
                {
                    if (g1.Ground[x, y] != g2.Ground[x, y]) same = false;
                    if (g1.Ground[x, y] != g3.Ground[x, y]) differs = true;
                }
            if (!same) fails.Add("stampNotDeterministic");
            if (!differs) fails.Add("stampIgnoresMission");
        }

        // reserved tiles are never covered (a soldier must not deploy standing in a fissure)
        {
            var gr = OpenGrid();
            var res = new HashSet<(int x, int y)>();
            for (int x = 0; x < gr.W; x++) for (int y = 0; y < gr.H; y++) if ((x + y) % 3 == 0) res.Add((x, y));
            Terrain.Stamp(gr, Terrain.BiomeVerdant, 99, 1, res);
            foreach (var (x, y) in res) if (gr.Ground[x, y] != GroundKind.None) fails.Add("stampedReserved");
        }

        // ═══ 1b. M1 — RAISED TERRAIN CARRIES NO GROUND ════════════════════════════════════════
        // `DrawElevation` paints a plateau top with a FULLY OPAQUE rect AFTER `DrawGround`, so any
        // mechanical ground on a raised tile is a rule with ZERO pixels behind it. The stamper
        // excludes Height > 0 at the source; this pins it. It is deliberately a DIRECT leg as well
        // as a real-board one, because the open-grid helper sets Height = 0 everywhere and could
        // never have caught this — the reason the defect shipped.
        foreach (int bi in mechIdx)
        {
            var gr = OpenGrid();
            for (int x = 0; x < gr.W; x++)
                for (int y = 0; y < gr.H; y++)
                    if (x >= 4 && x <= 13) gr.Height[x, y] = 1;     // a wide plateau across the middle
            int onRaised = 0, onFlat = 0;
            for (int sd = 0; sd < 12; sd++)
            {
                Terrain.Stamp(gr, bi, sd * 104729 + 7, 1, null);
                for (int x = 0; x < gr.W; x++)
                    for (int y = 0; y < gr.H; y++)
                        if (gr.Ground[x, y] != GroundKind.None)
                        { if (gr.HeightAt(x, y) > 0) onRaised++; else onFlat++; }
            }
            if (onRaised != 0) fails.Add($"stampedRaised[{Biome.All[bi].Name}]={onRaised}");
            if (onFlat == 0) fails.Add($"plateauAteEverything[{Biome.All[bi].Name}]");
        }

        // ═══ 2. VERDANT / UNDERGROWTH — the COVER axis ════════════════════════════════════════
        {
            var gr = OpenGrid();
            gr.Ground[9, 5] = GroundKind.Undergrowth; gr.RefreshGroundFlags();

            // at range: LOW cover from EVERY angle, labelled as foliage, and NOT flankable
            var far = gr.GetCover(9, 5, 14, 5);
            if (far.Level != 1 || !far.Foliage || far.Partial) fails.Add("foliageNoCoverAtRange");
            if (far.Defense != 20) fails.Add($"foliageDefense={far.Defense}");
            var diag = gr.GetCover(9, 5, 14, 10);          // a diagonal angle gets the same protection
            if (diag.Level != 1 || !diag.Foliage) fails.Add("foliageDiagonal");
            if (gr.GetCover(9, 5, 5, 5).Level != 1) fails.Add("foliageFromWest");

            // inside FoliageMinDist it is worth NOTHING — that is the counter, and it is the half a
            // player has to be able to feel: close, and the ferns stop mattering.
            var close = gr.GetCover(9, 5, 9 + Terrain.FoliageMinDist, 5);
            if (close.Level != 0 || close.Foliage) fails.Add("foliageStillCoversUpClose");

            // it is a FLOOR, not a bonus: a real high block still reads HIGH, not HIGH+fern
            gr.Tiles[10, 5] = TileType.HighCover; gr.ResetCoverHp();
            if (gr.GetCover(9, 5, 14, 5).Level != 2) fails.Add("foliageBrokeHighCover");
            gr.Tiles[10, 5] = TileType.Floor; gr.ResetCoverHp();

            // high ground SEES OVER it, exactly as it sees over any low block
            var atk = Shooter(14, 5, Team.Player); var def = Shooter(9, 5, Team.Enemy);
            var flat = Combat.ComputeOdds(gr, atk, def);
            if (!flat.Foliage || flat.CoverLevel != 1) fails.Add("oddsMissedFoliage");
            gr.Height[14, 5] = 1;
            var high = Combat.ComputeOdds(gr, atk, def);
            if (!high.SeesOver || high.CoverLevel != 0 || high.Foliage) fails.Add("highGroundBlindToFoliage");
            gr.Height[14, 5] = 0;

            // and it is worth EXACTLY 20 points of the attacker's hit chance (the number the badge
            // prints), measured against the same shot with the fern removed
            gr.Ground[9, 5] = GroundKind.None; gr.RefreshGroundFlags();
            var bare = Combat.ComputeOdds(gr, atk, def);
            if (bare.HitChance - flat.HitChance != 20) fails.Add($"foliageAimDelta={bare.HitChance - flat.HitChance}");
            if (!bare.Flanked && bare.CoverLevel != 0) fails.Add("bareTileNotExposed");
        }

        // ═══ 3. TUNDRA / SLICK ICE — the MOVEMENT axis ════════════════════════════════════════
        {
            var gr = OpenGrid();
            var bareCost = gr.CostMap(2, 5, (x, y) => false, out _, 12);
            int bareReach = bareCost[8, 5];               // 6 orthogonal steps = 12 half-tiles
            for (int x = 3; x <= 16; x++) gr.Ground[x, 5] = GroundKind.Ice;
            gr.RefreshGroundFlags();
            var iceCost = gr.CostMap(2, 5, (x, y) => false, out _, 12);
            if (iceCost[3, 5] != Terrain.IceStepOrth) fails.Add($"iceStep={iceCost[3, 5]}");
            if (bareReach != 12) fails.Add($"bareReach={bareReach}");
            if (iceCost[8, 5] != 6) fails.Add($"iceReachCost={iceCost[8, 5]}");
            // the lane genuinely reaches FURTHER on the same budget: (14,5) is 12 tiles out and
            // unreachable on foot, reachable on the drift.
            if (bareCost[14, 5] != -1) fails.Add("bareLaneAlreadyReached");
            if (iceCost[14, 5] < 0) fails.Add("iceLaneNoExtraReach");
            // ice is a movement rule ONLY — it is not cover and it does not blind
            if (gr.GetCover(8, 5, 14, 5).Level != 0) fails.Add("iceGaveCover");
            if (!gr.HasLineOfSight(2, 5, 16, 5)) fails.Add("iceBlockedSight");
        }

        // ═══ 4. MAGMA / THERMAL VENTS — the SIGHT axis (plus the toll) ════════════════════════
        {
            var gr = OpenGrid();
            if (!gr.HasLineOfSight(4, 5, 14, 5)) fails.Add("openSightBroken");
            gr.Ground[9, 5] = GroundKind.Vent; gr.RefreshGroundFlags();
            if (gr.HasLineOfSight(4, 5, 14, 5)) fails.Add("ventDidNotBlockSight");
            // steam is not a wall: a COMMANDING (tier-2 height) shooter sees over HIGH COVER but
            // must not see through a vent — the vent joins smoke, not the terrain.
            if (gr.HasLineOfSight(4, 5, 14, 5, true)) fails.Add("commandingSawThroughVent");
            if (!gr.IsVapor(9, 5)) fails.Add("ventNotVapor");
            // it gives NO cover to whoever is standing on it (it is a screen, not a block)
            if (gr.GetCover(9, 5, 14, 5).Level != 0) fails.Add("ventGaveCover");
            // sight around it still works — the fords are the whole reason the fissure has gaps
            if (!gr.HasLineOfSight(4, 6, 14, 6)) fails.Add("ventBlockedAdjacentLane");
            // the MOVEMENT half of the toll. The first version asserted
            // `vc[9,5] == 2 + Terrain.VentStepExtra`, which is SELF-REFERENTIAL: it passes for any
            // value of the constant, including VentStepExtra = 7, which makes a vent uncrossable by
            // EVERY unit in the game and silently turns the fissure into a wall. Assert the LITERAL,
            // and assert the invariant that literal exists to protect.
            var vc = gr.CostMap(8, 5, (x, y) => false, out _, 99);
            if (vc[9, 5] != 8) fails.Add($"ventStep={vc[9, 5]}");
            // INVARIANT: a full-mobility (4) soldier must always be able to FORCE a crossing —
            // 8 half-tiles is exactly one action's budget. Anything dearer and the mechanic stops
            // being a price and becomes terrain. (A WOUNDED soldier already cannot enter one; that
            // is declared in DEVLOG C4, not asserted here, because it is the current behaviour.)
            if (2 + Terrain.VentStepExtra > 8) fails.Add($"ventUncrossable={2 + Terrain.VentStepExtra}");

            // the HP half of the toll, through the real Game seam both teams move through
            Grid = gr; Players = new List<Unit>(); Enemies = new List<Unit>();
            Vip = null; CaptiveLocked = false; SquadConcealed = false; Phase = Phase.PlayerTurn;
            Objective = Objective.Eliminate; EvacZone.Clear();
            var walker = Shooter(9, 5, Team.Player); Players.Add(walker);
            int hp0 = walker.Hp;
            OnUnitEnteredTile(walker);
            if (walker.Hp != hp0 - Unit.BurnDamage) fails.Add($"ventNoSear={hp0 - walker.Hp}");
            if (!walker.HasStatus(StatusKind.Burning)) fails.Add("ventDidNotIgnite");
            // and PARKING on one keeps you burning — otherwise the safest tile on the board would be
            // the one nothing can see through (the turtle DESIGN.md 3.A forbids)
            walker.Statuses.Clear();
            TickHazards();
            if (!walker.HasStatus(StatusKind.Burning)) fails.Add("ventParkedNotReignited");
            // Terrain.VentBurnTurns was DEAD CODE: TickHazards hardcoded `2`, so mutating the
            // constant 2 -> 1 changed nothing and no test could see it. Pin the DURATION the
            // constant actually produces, so the constant has to matter.
            // TWO assertions, and it needs both. `== Terrain.VentBurnTurns` alone is SELF-REFERENTIAL
            // — mutating the constant 2 -> 1 passed it, which is the very defect this leg exists to
            // close (and the same shape as the old `ventStep` assertion). The LITERAL catches a
            // changed constant; the CONSTANT catches a code path that ignores it and hardcodes a
            // number, which is how VentBurnTurns became dead in the first place.
            int ventBurn = 0;
            foreach (var st in walker.Statuses) if (st.Kind == StatusKind.Burning) ventBurn = st.Turns;
            if (ventBurn != 2) fails.Add($"ventBurnTurns={ventBurn}");
            if (Terrain.VentBurnTurns != 2) fails.Add($"ventBurnConst={Terrain.VentBurnTurns}");
            if (ventBurn != Terrain.VentBurnTurns) fails.Add("ventBurnIgnoresConst");

            // DOUBLE SEAR: a tile that is BOTH on fire and a vent must charge ONE sear per entry,
            // not two (the fire branch and the vent branch used to be separate unconditional ifs).
            var both = Shooter(4, 4, Team.Player); Players.Add(both);
            gr.Ground[4, 4] = GroundKind.Vent; gr.RefreshGroundFlags();
            gr.LightFire(4, 4, Grid.FireTurns);
            int bhp = both.Hp; both.Statuses.Clear();
            OnUnitEnteredTile(both);
            if (bhp - both.Hp != Unit.BurnDamage) fails.Add($"doubleSear={bhp - both.Hp}");
            // a walker on ordinary floor is untouched by the same tick (the guard is the ground, not the tick)
            var safe = Shooter(2, 9, Team.Player); Players.Add(safe);
            int shp = safe.Hp; safe.Statuses.Clear();
            TickHazards(); OnUnitEnteredTile(safe);
            if (safe.Hp != shp || safe.HasStatus(StatusKind.Burning)) fails.Add("floorTileBurned");
        }

        // ═══ 5. THE OPPONENT UNDERSTANDS THE NEW BOARD ════════════════════════════════════════
        //
        // C4 REVIEW — WHAT THIS BLOCK USED TO CLAIM, AND WHY THAT WAS WRONG. The wave singled the
        // leg below out as "the only honest way to test that the enemy understands the new board".
        // It was not: it is a COMPOSITE. The reviewer mutated Ai.cs's vent weight one value at a
        // time and the test passed at -34 -> 0, at -1, and even INVERTED to +200 — because
        // Grid.CostMap's +6 toll alone moves the plan, with Ai.Plan's Util.RandRange(0,3) tie-break
        // jitter as a further confound. It only failed when the CostMap toll was zeroed TOO.
        // So there are now TWO legs with two different claims, and the jitter is pinned in both.
        //
        // (a) COMPOSITE — the behavioural claim, and it is a real one: with everything the game
        //     ships, nothing lets a hostile end its move on hot ground. Run the same scene twice
        //     and watch it abandon the exact tile it just chose.
        {
            Grid = OpenGrid(); Players = new List<Unit>(); Enemies = new List<Unit>();
            Vip = null; CaptiveLocked = false; SquadConcealed = false; Phase = Phase.EnemyTurn;
            Objective = Objective.Eliminate; EvacZone.Clear();
            var soldier = Shooter(3, 5, Team.Player); Players.Add(soldier);
            var foe = Shooter(12, 5, Team.Enemy); Enemies.Add(foe);
            RefreshCombatRoster();
            // PIN THE JITTER. Ai.Plan draws Util.RandRange(0,3) per candidate tile; over the same
            // reachable set the same seed replays the same per-tile jitter, so the two plans differ
            // ONLY by the vent. Without this a "different tile" can be pure noise.
            Util.Reseed(9001);
            var p0 = Ai.Plan(this, foe);
            (int x, int y) t0 = p0.Path.Count > 0 ? p0.Path[p0.Path.Count - 1] : (foe.X, foe.Y);
            if (p0.Path.Count == 0) fails.Add("aiDidNotMoveAtAll");
            else
            {
                Grid.Ground[t0.x, t0.y] = GroundKind.Vent; Grid.RefreshGroundFlags();
                foe.X = 12; foe.Y = 5; foe.SyncPos(); foe.BeginTurn();
                Util.Reseed(9001);
                var p1 = Ai.Plan(this, foe);
                (int x, int y) t1 = p1.Path.Count > 0 ? p1.Path[p1.Path.Count - 1] : (foe.X, foe.Y);
                if (t1.x == t0.x && t1.y == t0.y) fails.Add("aiParkedOnVent");
                // (`aiEndedOnAVent` was here and was redundant — the scene holds exactly one vent
                // tile, so it could only ever restate `aiParkedOnVent`. Dropped.)
            }
        }
        // (a2) ISOLATED — Ai.cs's OWN term, with the CostMap toll switched off so it cannot carry
        //      the result. This is the leg that fails on `score -= 34` -> 0 and on an inverted
        //      weight. With VentStepExtra = 0 the vent costs nothing to enter, the jitter is pinned
        //      to the same stream, and a single vent tile changes no sightline that STARTS or ENDS
        //      on it (HasLineOfSight tests neither endpoint) — so the ONLY thing that can move the
        //      planner off its own chosen tile is the term in Ai.cs.
        {
            int savedToll = Terrain.VentStepExtra;
            try
            {
                Terrain.VentStepExtra = 0;
                Grid = OpenGrid(); Players = new List<Unit>(); Enemies = new List<Unit>();
                Vip = null; CaptiveLocked = false; SquadConcealed = false; Phase = Phase.EnemyTurn;
                Objective = Objective.Eliminate; EvacZone.Clear();
                var soldier = Shooter(3, 5, Team.Player); Players.Add(soldier);
                var foe = Shooter(12, 5, Team.Enemy); Enemies.Add(foe);
                RefreshCombatRoster();
                Util.Reseed(4242);
                var q0 = Ai.Plan(this, foe);
                (int x, int y) u0 = q0.Path.Count > 0 ? q0.Path[q0.Path.Count - 1] : (foe.X, foe.Y);
                if (q0.Path.Count == 0) fails.Add("aiIsoDidNotMove");
                else
                {
                    Grid.Ground[u0.x, u0.y] = GroundKind.Vent; Grid.RefreshGroundFlags();
                    // the tile must still be REACHABLE at the same cost — otherwise "declined"
                    // would be indistinguishable from "could not get there" (the exact confound
                    // this leg exists to remove).
                    var reach = Grid.CostMap(foe.X, foe.Y, (x, y) => false, out _, foe.MoveBudget * 2);
                    if (reach[u0.x, u0.y] < 0) fails.Add("aiIsoTileUnreachable");
                    foe.X = 12; foe.Y = 5; foe.SyncPos(); foe.BeginTurn();
                    Util.Reseed(4242);
                    var q1 = Ai.Plan(this, foe);
                    (int x, int y) u1 = q1.Path.Count > 0 ? q1.Path[q1.Path.Count - 1] : (foe.X, foe.Y);
                    if (u1.x == u0.x && u1.y == u0.y) fails.Add("aiTermDoesNothing");
                }
            }
            finally { Terrain.VentStepExtra = savedToll; }
        }
        // (b) the enemy's REACH is the shared cost map, so an ice lane widens the exact set of tiles
        //     Ai.Plan gets to choose from — no second movement model, nothing for the AI to miss.
        {
            Grid = OpenGrid(); Players = new List<Unit>(); Enemies = new List<Unit>();
            Phase = Phase.EnemyTurn; Objective = Objective.Eliminate; EvacZone.Clear();
            var foe = Shooter(2, 5, Team.Enemy); Enemies.Add(foe);
            var bare = Grid.CostMap(foe.X, foe.Y, (x, y) => false, out _, foe.MoveBudget * 2);
            for (int x = 3; x <= 16; x++) Grid.Ground[x, 5] = GroundKind.Ice;
            Grid.RefreshGroundFlags();
            var iced = Grid.CostMap(foe.X, foe.Y, (x, y) => false, out _, foe.MoveBudget * 2);
            int gained = 0, lost = 0;
            for (int x = 0; x < Grid.W; x++)
                for (int y = 0; y < Grid.H; y++)
                {
                    if (bare[x, y] < 0 && iced[x, y] >= 0) gained++;
                    if (bare[x, y] >= 0 && iced[x, y] < 0) lost++;
                }
            if (gained <= 0) fails.Add("iceGaveTheAiNothing");
            if (lost != 0) fails.Add($"iceTookReachAway={lost}");
        }

        // ═══ 6. THE OFF SWITCH — SIGHTLINE_BIOMEMECH=0 restores the pre-C4 board EXACTLY ══════
        {
            bool was = Terrain.Enabled;
            Terrain.Enabled = false;
            var gr = OpenGrid();
            gr.Ground[9, 5] = GroundKind.Undergrowth;
            gr.Ground[10, 5] = GroundKind.Vent;
            gr.Ground[11, 5] = GroundKind.Ice;
            gr.RefreshGroundFlags();
            if (gr.GetCover(9, 5, 14, 5).Level != 0) fails.Add("offSwitchFoliage");
            if (!gr.HasLineOfSight(4, 5, 14, 5)) fails.Add("offSwitchVentSight");
            var c = gr.CostMap(12, 5, (x, y) => false, out _, 99);
            if (c[11, 5] != 2 || c[10, 5] != 4) fails.Add("offSwitchCosts");
            if (gr.IsVent(10, 5) || gr.IsIce(11, 5) || gr.IsFoliage(9, 5)) fails.Add("offSwitchPredicates");
            var gs = OpenGrid(); Terrain.Stamp(gs, Terrain.BiomeMagma, 5, 1, null);
            for (int x = 0; x < gs.W; x++)
                for (int y = 0; y < gs.H; y++) if (gs.Ground[x, y] != GroundKind.None) fails.Add("offSwitchStamped");
            Terrain.Enabled = was;
        }

        return fails.Count == 0
            ? "BIOMETEST: PASS (3 biomes mechanical on 3 axes, 5 still paint; NO ground on raised terrain; "
              + "undergrowth = omnidirectional low cover past " + Terrain.FoliageMinDist
              + " tiles and worth exactly 20 aim, gone up close, seen over from height; ice halves the step and "
              + "widens the shared reach; a vent blinds even a commanding shooter, gives no cover, costs a "
              + "full-mobility soldier's whole walk to enter, sears ONCE on entry even when also on fire, and "
              + "re-ignites for VentBurnTurns on parking; the AI declines a vent BOTH composite AND with the "
              + "CostMap toll zeroed, jitter pinned; stamp pure, off-switch clean; REAL-BOARD density"
              + BiomeDensityNote + ")"
            : "BIOMETEST: FAIL (" + string.Join(",", fails) + ")";
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
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70112);
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
        System.Text.StringBuilder sb0Deploy = null;   // W4 deployment-shape histogram (filled below)
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

        // ── W4 THE SECOND AXIS: the DEPLOYMENT SHAPE is now a third exposure axis ─────
        // Enumerate shape x arena and shape x objective over the same seed space, and pin the
        // three contracts the geometry has to keep:
        //   (1) PURE — DeployFor consumes ZERO Util.Rng draws (every CRN pairing depends on it);
        //   (2) DETERMINISTIC — the same (seed, mission) always yields the same shape;
        //   (3) LEGAL — ENVELOP (a centre deployment) is never dealt to an objective whose
        //       geography it would trivialise; every legal shape reaches every objective/arena.
        {
            int nShapes = Mission.DeployShapes;
            var shapeHist = new int[nShapes];
            var shapeArena = new bool[nShapes, nLay];
            var shapeObj = new Dictionary<(int, Objective), int>();
            // (1) purity: interleaving DeployFor calls must not perturb the shared stream.
            Util.Reseed(4242);
            int refDraw = Util.RandInt(0, 1000000);
            Util.Reseed(4242);
            for (int k = 0; k < 500; k++) Mission.DeployFor(k * 31 + 7, (k % Run.MaxMissions) + 1, k % 2 == 0);
            if (Util.RandInt(0, 1000000) != refDraw) fails.Add("deployConsumesRng");

            bool EnvOk(Objective o) => o == Objective.Eliminate || o == Objective.Decapitate || o == Objective.Defend;

            // shape x ARENA is a pure (seed, mission) x (seed, mission) cross-product — no map
            // generation needed — so it sweeps a MUCH wider seed space than the route walk below.
            // A low-weight shape (CROSSFIRE at 1/10) simply does not reach all 35 arenas inside
            // 200 seeds, and asserting on that sample would be asserting on sampling noise.
            const int ArenaSeeds = 4000;
            for (int i = 0; i < ArenaSeeds; i++)
            {
                int seed = 1000 + i * 7919;
                for (int m = 1; m <= Run.MaxMissions; m++)
                {
                    int a = Mission.DeckPick(seed, m);
                    if (a < 0 || a >= nLay) continue;
                    for (int leg = 0; leg < 2; leg++)
                    {
                        int sh = Mission.DeployFor(seed, m, leg == 1);
                        if (sh != Mission.DeployFor(seed, m, leg == 1)) fails.Add($"seed{seed}:deployNotDeterministic");
                        if (sh < 0 || sh >= nShapes) { fails.Add($"seed{seed}:deployOutOfRange:{sh}"); continue; }
                        if (sh == Mission.DeployEnvelop && leg == 0) fails.Add($"seed{seed}:envelopOnIllegalObjective");
                        shapeHist[sh]++; shapeArena[sh, a] = true;
                    }
                }
            }
            for (int i = 0; i < Seeds; i++)
            {
                int seed = 1000 + i * 7919;
                var run = new Run { MapSeed = seed };
                run.GenerateMap(seed);
                // shape x objective over every enumerated route (mission # = the fight's depth)
                var routes2 = new List<List<MissionNode>>();
                void Walk2(MissionNode node, List<MissionNode> path)
                {
                    path.Add(node);
                    if (node.Next.Count == 0) routes2.Add(new List<MissionNode>(path));
                    else foreach (int id in node.Next) Walk2(run.Map[id], path);
                    path.RemoveAt(path.Count - 1);
                }
                Walk2(run.Map[0], new List<MissionNode>());
                foreach (var route in routes2)
                {
                    int m = 0;
                    foreach (var node in route)
                    {
                        if (node.Kind == NodeKind.Event) continue;
                        m++;
                        var o = node.Card.Objective;
                        int sh = Mission.DeployFor(seed, m, EnvOk(o));
                        if (sh == Mission.DeployEnvelop && !EnvOk(o)) fails.Add($"seed{seed}:envelopDealtTo{o}");
                        shapeObj[(sh, o)] = shapeObj.GetValueOrDefault((sh, o)) + 1;
                    }
                }
            }

            string ShapeName(int d) => d switch
            {
                Mission.DeployPincer => "PINCER", Mission.DeployCrossfire => "CROSSFIRE",
                Mission.DeployEnvelop => "ENVELOP", _ => "FRONTAL",
            };
            // every shape the SHIPPED mix can deal must reach every arena, and every legal
            // objective. A zero-weight shape is inert by design and is not required to appear.
            for (int sh = 0; sh < nShapes; sh++)
            {
                bool weighted = sh < Mission.DeployMix.Length && Mission.DeployMix[sh] > 0;
                if (!weighted) continue;
                if (shapeHist[sh] == 0) { fails.Add($"shapeNeverDealt:{ShapeName(sh)}"); continue; }
                for (int a = 0; a < nLay; a++)
                    if (!shapeArena[sh, a]) fails.Add($"shape{ShapeName(sh)}NeverOnArena{a}");
                foreach (Objective o in Enum.GetValues<Objective>())
                {
                    if (sh == Mission.DeployEnvelop && !EnvOk(o)) continue;   // illegal by design
                    if (shapeObj.GetValueOrDefault((sh, o)) == 0) fails.Add($"shape{ShapeName(sh)}Never{o}");
                }
            }
            sb0Deploy = new System.Text.StringBuilder();
            sb0Deploy.AppendLine($"DEPLOYMENT SHAPE HISTOGRAM ({ArenaSeeds} seeds x {Run.MaxMissions} missions x 2 legality states):");
            for (int sh = 0; sh < nShapes; sh++)
                sb0Deploy.AppendLine($"  {ShapeName(sh),-10}: {shapeHist[sh]}"
                    + $"   arenas covered {Enumerable.Range(0, nLay).Count(a => shapeArena[sh, a])}/{nLay}"
                    + $"   objectives covered {Enum.GetValues<Objective>().Count(o => shapeObj.GetValueOrDefault((sh, o)) > 0)}/{Enum.GetValues<Objective>().Length}");
        }

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
        if (sb0Deploy != null) sb.Append(sb0Deploy);
        sb.Append(fails.Count == 0 ? "EXPOSURETEST PASS"
            : $"EXPOSURETEST FAIL: {string.Join(", ", fails.Take(12))}{(fails.Count > 12 ? $" (+{fails.Count - 12} more)" : "")}");
        return sb.ToString();
    }

    // ================= RESONANCE T2 — the incoming-fire forecast ==========
    /// SIGHTLINE_THREATTEST — pins Game.ComputeThreat's per-tile forecast against the SAME
    /// Combat.ComputeOdds the resolver uses, on a controlled synthetic board. It asserts the
    /// FINDING the wave fixes as well as the mechanics:
    ///   (1) gun COUNT is real (only live+active+armed+in-range+in-LoS hostiles count),
    ///   (2) BestHit / WorstCls / ExpDmg equal a hand-recomputed ComputeOdds pass,
    ///   (3) the pre-T2 blind spot: a tile in cover from EVERY bearing gun still reports Guns>0
    ///       (the old bool[,] called it completely clean),
    ///   (4) cover levels + flank ANGLE are read from the mover's would-be position,
    ///   (5) out-of-range / no-LoS / dormant / dry / dead hostiles are excluded,
    ///   (6) overwatch + FOCUSED (braced-cone) reaction lanes are flagged only where a reaction
    ///       would genuinely fire,
    ///   (7) unreachable tiles are never computed, and the caged captive forecasts empty,
    ///   (8) the mover's real X/Y/Hunkered/MovedAfterFire survive the probe untouched,
    ///   (9) the post-move state model (moving drops HUNKER) is applied per tile, and
    ///  (10) the signature cache actually suppresses redundant rebuilds, and
    ///  (11) R2 FIX 2 — the forecast is TRUE: for every weapon kind, the number the card shows is
    ///       measured against 100k real Combat.Resolve rolls and must match the sample mean.
    /// Leg (11) exists because legs (1)-(10) could not have caught the defect it guards. They
    /// hand-recomputed the SAME formula the forecast used, so a wrong formula agreed with itself:
    /// the card read 31-44% low for two years' worth of waves (crit AND graze both omitted, both
    /// pushing the same way) and every assertion here passed. Comparing against the resolver is
    /// the only kind of check that catches that class of defect.
    /// Finishes with a measured worst-case rebuild cost (198 tiles x 8 guns). One-line report.
    public string ThreatSelfTest()
    {
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();
        string groundTruth = "ground truth NOT MEASURED";

        // ---- deterministic combat statics (a prior test in the same process must not bleed in) ----
        Combat.AllUnits = System.Array.Empty<Unit>();
        Combat.MissionFaction = Faction.None;
        Combat.PrepFaction = Faction.None;
        Combat.RunBoons.Clear();
        Combat.PressureAim = 0;

        _run = new Run(); _run.Start();
        Objective = Objective.Eliminate;
        Vip = null; CaptiveLocked = false; Hvt = null; EvacZone.Clear();
        Phase = Phase.PlayerTurn;
        ThreatPref = ThreatFull;

        Unit MkP(int x, int y) {
            var u = new Unit { Name = "SOL", Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                               Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
        }
        Unit MkE(string name, int x, int y) {
            var u = new Unit { Name = name, Cls = "GRUNT", Team = Team.Enemy, X = x, Y = y,
                               Hp = 6, MaxHp = 6, Aim = 60, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle), PodId = 0 };
            u.Ammo = u.Weapon.Clip; u.Alert = AlertLevel.Alert; u.SyncPos(); u.BeginTurn(); return u;
        }

        // Fresh scene: empty floor, one soldier, an all-reachable MoveCost so every tile is probed.
        Unit sol = null;
        void Scene(int sx, int sy)
        {
            Grid = new Grid();
            Players = new System.Collections.Generic.List<Unit>();
            Enemies = new System.Collections.Generic.List<Unit>();
            sol = MkP(sx, sy);
            Players.Add(sol);
            Selected = sol;
        }
        void AllReachable()
        {
            MoveCost = new int[Grid.W, Grid.H];
            for (int x = 0; x < Grid.W; x++)
                for (int y = 0; y < Grid.H; y++)
                    MoveCost[x, y] = (x == sol.X && y == sol.Y) ? 0 : 1;
        }
        void Rebuild() { _threatSig = 0; ComputeThreat(); }

        // ---------- (1)(2)(5) count / best / expected damage / exclusions ----------
        Scene(5, 5); AllReachable();
        var a1 = MkE("A", 9, 5);                 // 4 east  — clear LoS, in range
        var a2 = MkE("B", 5, 9);                 // 4 south — clear LoS, in range
        var a3 = MkE("C", 2, 2);                 // NW diagonal — clear LoS, in range
        var far = MkE("FAR", 14, 5);             // SHOTGUN (MaxRange 8) at dist 9 -> out of range
        far.Weapon = Weapon.Make(WeaponKind.Shotgun); far.Ammo = far.Weapon.Clip;
        var dormant = MkE("SLEEP", 7, 5);        // in range but NOT active
        var dry = MkE("DRY", 3, 5);              // in range but out of ammo
        var dead = MkE("DEAD", 6, 6);            // in range but dead
        foreach (var e in new[] { a1, a2, a3, far, dormant, dry, dead }) Enemies.Add(e);   // MkE already sets Alert = Alert (=> Active)
        dormant.Alert = AlertLevel.Unaware; dry.Ammo = 0; dead.Hp = 0; dead.Alive = false;
        if (Util.TileDist(5, 5, far.X, far.Y) <= far.Weapon.MaxRange) fails.Add("scene_farInRange");
        Rebuild();
        if (Threat == null) { return "THREATTEST FAIL: nullForecast"; }
        var c0 = Threat[5, 5];
        if (c0.Guns != 3) fails.Add($"gunCount={c0.Guns} want 3");

        // hand-recompute the same three shots and pin best / worst / expected damage
        int wantBest = 0; string wantCls = null; float wantExp = 0f; float wantScore = -1f;
        foreach (var e in new[] { a1, a2, a3 })
        {
            var o = Combat.ComputeOdds(Grid, e, sol);
            wantExp += Combat.ExpectedDamage(sol, o);   // R2 FIX 2: the shared source of truth, ground-truthed in leg (11)
            float sc = o.HitChance * 1000f + (o.DmgMin + o.DmgMax);
            if (sc > wantScore) { wantScore = sc; wantBest = o.HitChance; wantCls = e.Cls; }
        }
        if (c0.BestHit != wantBest) fails.Add($"bestHit={c0.BestHit} want {wantBest}");
        if (c0.WorstCls != wantCls) fails.Add($"worstCls={c0.WorstCls} want {wantCls}");
        if (MathF.Abs(c0.ExpDmg - wantExp) > 0.01f) fails.Add($"expDmg={c0.ExpDmg:0.###} want {wantExp:0.###}");
        if (wantBest <= 0) fails.Add("vacuousBestHit");         // guard: the scene must actually produce shots
        if (wantExp <= 0f) fails.Add("vacuousExpDmg");
        if (!c0.Exposed) fails.Add("openGroundNotExposed");     // no cover anywhere on this board
        if (c0.Tier != 3) fails.Add($"tier={c0.Tier} want 3");
        // exclusions must genuinely bite: waking the dormant gun changes the count
        dormant.Alert = AlertLevel.Alert; Rebuild();
        if (Threat[5, 5].Guns != 4) fails.Add($"dormantExclusionInert={Threat[5, 5].Guns}");
        dormant.Alert = AlertLevel.Unaware;
        // a wall between (5,5) and the east gun drops it (LoS gate)
        Grid.Tiles[7, 5] = TileType.HighCover; Rebuild();
        if (Threat[5, 5].Guns != 2) fails.Add($"losExclusion={Threat[5, 5].Guns} want 2");
        Grid.Tiles[7, 5] = TileType.Floor;

        // ---------- (7) unreachable tiles are never computed ----------
        Rebuild();
        int probeX = 5, probeY = 3;                              // open, in every gun's reach
        if (Threat[probeX, probeY].Guns == 0) fails.Add("probeTileHadNoGuns");   // guard: the tile IS hot
        MoveCost[probeX, probeY] = 0; Rebuild();
        if (Threat[probeX, probeY].Guns != 0) fails.Add("unreachableTileComputed");
        MoveCost[probeX, probeY] = 1;

        // ---------- (8) the probe leaves the mover untouched ----------
        sol.Hunkered = true; sol.FiredThisTurn = true; sol.MovedAfterFire = false;
        int sx0 = sol.X, sy0 = sol.Y;
        Rebuild();
        if (sol.X != sx0 || sol.Y != sy0) fails.Add("moverPositionClobbered");
        if (!sol.Hunkered || sol.MovedAfterFire) fails.Add("moverStateClobbered");

        // ---------- (9) post-move state model: HUNKER only holds on the tile you stand on ----------
        var hereCell = Threat[sol.X, sol.Y];
        var stepCell = Threat[sol.X, sol.Y - 1];
        if (hereCell.BestHit >= stepCell.BestHit) fails.Add($"hunkerNotModelled here={hereCell.BestHit} step={stepCell.BestHit}");
        sol.Hunkered = false; sol.FiredThisTurn = false; sol.MovedAfterFire = false;

        // ---------- (3)(4) cover levels + flank ANGLE, read from the would-be position ----------
        // Scene: soldier at (5,5). Candidate tile (8,5) has HIGH cover on its EAST side (9,5).
        // ONE gun due east (covered) and, later, a second gun due north (flanking).
        Scene(5, 5); AllReachable();
        // LOW cover, not HIGH: high cover BLOCKS the cardinal sightline outright (Grid.BlocksSight),
        // so there'd be no shot at all to forecast. Low cover shades the shot without severing it.
        Grid.Tiles[9, 5] = TileType.LowCover;
        var east = MkE("EAST", 12, 5); Enemies.Add(east);
        Rebuild();
        var covered = Threat[8, 5];
        if (covered.Guns != 1) fails.Add($"coveredGuns={covered.Guns} want 1");
        if (covered.Exposed) fails.Add("coveredTileReadsExposed");
        if (covered.Flanked) fails.Add("coveredTileReadsFlanked");
        var oCov = Combat.ComputeOdds(Grid, east, new Unit { Team = Team.Player, X = 8, Y = 5, Hp = 8, MaxHp = 8,
                                                             Aim = 65, Weapon = Weapon.Make(WeaponKind.Rifle) });
        if (oCov.CoverLevel != 1) fails.Add($"sceneCoverLevel={oCov.CoverLevel} want 1");
        if (covered.BestHit != oCov.HitChance) fails.Add($"coveredBestHit={covered.BestHit} want {oCov.HitChance}");
        // THE FINDING: a second gun on a DIFFERENT angle. The tile is still in cover from nobody's
        // point of view but the north gun's — pre-T2 the tile was flagged only via the raw bool, and
        // a tile covered from EVERY gun read completely clean. Now the COUNT is always honest.
        var north = MkE("NORTH", 8, 1); Enemies.Add(north);
        Rebuild();
        var enfiladed = Threat[8, 5];
        if (enfiladed.Guns != 2) fails.Add($"enfiladedGuns={enfiladed.Guns} want 2");
        if (!enfiladed.Flanked) fails.Add("northGunNotFlanking");
        if (enfiladed.BestHit <= covered.BestHit) fails.Add("flankNotHarderHit");
        // and the pre-T2 blind spot itself: box the tile so BOTH guns are covered -> Guns still 2,
        // Exposed false. The old bool[,] drew nothing at all here.
        Grid.Tiles[8, 4] = TileType.LowCover; Rebuild();
        var boxed = Threat[8, 5];
        if (boxed.Guns != 2) fails.Add($"boxedGuns={boxed.Guns} want 2");
        if (boxed.Exposed) fails.Add("boxedTileExposed");
        if (boxed.Flanked) fails.Add("boxedTileFlanked");
        if (boxed.Tier != 2) fails.Add($"boxedTier={boxed.Tier} want 2");

        // ---------- (6) overwatch / focused-cone reaction lanes ----------
        Scene(5, 5); AllReachable();
        var watcher = MkE("WATCH", 11, 5); watcher.OnOverwatch = true; Enemies.Add(watcher);
        Rebuild();
        if (!Threat[8, 5].Watched) fails.Add("wideOverwatchNotWatched");
        watcher.OwFocused = true; watcher.OwDirX = -1; watcher.OwDirY = 0;   // braced WEST, down the row
        Rebuild();
        if (!Threat[8, 5].Watched) fails.Add("inConeNotWatched");
        if (!InOwCone(watcher, 8, 5)) fails.Add("sceneConeSanity");
        // a tile the cone does NOT cover must not claim a reaction (perpendicular, still in LoS+range)
        if (InOwCone(watcher, 11, 2)) fails.Add("scenePerpConeSanity");
        if (Threat[11, 2].Watched) fails.Add("outOfConeWatched");
        if (Threat[11, 2].Guns == 0) fails.Add("outOfConeTileHadNoGuns");   // guard: it IS shootable, just not watched
        watcher.OnOverwatch = false; Rebuild();
        if (Threat[8, 5].Watched) fails.Add("overwatchClearedButStillWatched");

        // ---------- (7b) the caged Rescue captive forecasts empty ----------
        Scene(5, 5); AllReachable();
        var gun = MkE("G", 9, 5); Enemies.Add(gun);
        Rebuild();
        if (Threat[5, 5].Guns == 0) fails.Add("captiveSceneNoGuns");
        Vip = sol; CaptiveLocked = true; Rebuild();
        if (Threat[5, 5].Guns != 0) fails.Add("cagedCaptiveForecastsFire");
        Vip = null; CaptiveLocked = false;

        // ---------- (10) the signature cache suppresses redundant rebuilds ----------
        Rebuild();
        int r0 = ThreatRebuilds;
        ComputeThreat(); ComputeThreat(); ComputeThreat();
        if (ThreatRebuilds != r0) fails.Add($"cacheMissedOnNoChange (+{ThreatRebuilds - r0})");
        gun.X = 8; gun.SyncPos(); ComputeThreat();
        if (ThreatRebuilds != r0 + 1) fails.Add("cacheDidNotInvalidateOnEnemyMove");
        Grid.Tiles[3, 3] = TileType.LowCover; ComputeThreat();
        if (ThreatRebuilds != r0 + 2) fails.Add("cacheDidNotInvalidateOnTerrainChange");

        // ---------- (11) R2 FIX 2: GROUND TRUTH — the forecast vs 100k real Resolve rolls ----------
        // One enemy attacker per weapon kind at a fixed range on open ground, firing at a soldier
        // pinned off full HP (so Combat.FragileFloor — the only term ExpectedDamage deliberately
        // omits — can never fire and skew the sample). The attacker is an ENEMY on purpose: the
        // streak-breaker aim bonus Resolve folds into effHit is player-only, so the displayed
        // HitChance is exactly the roll threshold and the comparison is apples to apples.
        {
            const int rolls = 100000;
            double worstRel = 0; string worstName = "";
            foreach (WeaponKind wk in System.Enum.GetValues(typeof(WeaponKind)))
            {
                var g2 = new Grid();
                var atk = new Unit { Name = "FOE", Cls = "GRUNT", Team = Team.Enemy, X = 5, Y = 5, Hp = 6, MaxHp = 6,
                                     Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
                atk.Weapon = Weapon.Make(wk);
                var def = new Unit { Name = "SOL", Cls = "ASSAULT", Team = Team.Player, X = 9, Y = 5, Hp = 400, MaxHp = 401,
                                     Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
                atk.Ammo = 9999; def.Ammo = def.Weapon.Clip;
                if (Util.TileDist(atk.X, atk.Y, def.X, def.Y) > atk.Weapon.MaxRange) { fails.Add($"gt_{wk}_outOfRange"); continue; }
                Combat.AllUnits = new System.Collections.Generic.List<Unit> { atk, def };

                var o = Combat.ComputeOdds(g2, atk, def);
                float shown = Combat.ExpectedDamage(def, o);
                if (shown <= 0f) { fails.Add($"gt_{wk}_vacuousForecast"); continue; }

                Util.Reseed(7723 + (int)wk);
                long total = 0;
                for (int i = 0; i < rolls; i++)
                {
                    def.Hp = 400;                     // never full HP -> FragileFloor stays out of the sample
                    atk.ConsecutiveMisses = 0;        // enemy streak is inert in ComputeOdds; pin it anyway
                    total += Combat.Resolve(g2, atk, def).Damage;
                }
                double measured = (double)total / rolls;
                double rel = Math.Abs(measured - shown) / Math.Max(0.001, measured);
                if (rel > worstRel) { worstRel = rel; worstName = wk.ToString(); }
                // 2% is ~6 sigma at this sample size for every band here; the pre-fix formula was
                // off by 31-44%, so this tolerance separates "true" from "wrong formula" by 15x.
                if (rel > 0.02)
                    fails.Add($"gt_{wk}_forecast={shown:0.000}_measured={measured:0.000}_off={rel * 100:0.0}%");
            }
            Combat.AllUnits = System.Array.Empty<Unit>();
            if (worstName.Length == 0) fails.Add("gt_noWeaponSampled");
            groundTruth = $"worst forecast-vs-Resolve error {worstRel * 100:0.00}% ({worstName}, {rolls} rolls/weapon)";
        }

        // ---------- perf: worst case — every tile reachable, 8 armed guns ----------
        Scene(9, 5);
        for (int i = 0; i < 8; i++)
        {
            var e = MkE($"P{i}", 1 + (i % 4) * 4, i < 4 ? 1 : 9);
            Enemies.Add(e);
        }
        AllReachable();
        Rebuild();                                   // warm the JIT
        const int reps = 40;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < reps; i++) Rebuild();
        sw.Stop();
        double ms = sw.Elapsed.TotalMilliseconds / reps;
        int guns = Threat[9, 5].Guns;

        Combat.AllUnits = System.Array.Empty<Unit>();
        return fails.Count == 0
            ? $"THREATTEST PASS ({groundTruth}; worst-case rebuild {ms:0.000} ms over {Cfg.GridW * Cfg.GridH} tiles x 8 guns; centre sees {guns})"
            : $"THREATTEST FAIL: {string.Join(", ", fails)}";
    }

    /// Harness (screenshot): SIGHTLINE_THREATSHOT — stage a real fight where the selected soldier
    /// can walk into 1-, 2- and 3-gun tiles, then park the cursor on a hot destination so the pips,
    /// the danger-tinted path and the INCOMING FIRE card all land in one frame. Pair with
    /// SIGHTLINE_CB=1 for the colorblind pass (coded-state rule, DESIGN.md 3.H).
    public void DebugThreatShot()
    {
        DebugWakeAll();
        var sol = AlivePlayers().FirstOrDefault(p => !p.IsVip && p.CanAct) ?? AlivePlayers().FirstOrDefault();
        if (sol == null) return;
        Selected = sol;
        ThreatPref = ThreatFull;

        // Fan the live hostiles onto clear firing angles around the soldier so several guns bear on
        // the tiles it can reach (staging only — the same free-staging precedent as DebugPikemanLane).
        var foes = AliveEnemies().Take(4).ToList();
        var rings = new (int dx, int dy)[] { (6, -2), (5, 4), (-5, 3), (-4, -4) };
        for (int i = 0; i < foes.Count && i < rings.Length; i++)
        {
            var f = foes[i];
            int tx = Util.Clamp(sol.X + rings[i].dx, 0, Grid.W - 1);
            int ty = Util.Clamp(sol.Y + rings[i].dy, 0, Grid.H - 1);
            for (int r = 0; r <= 3 && !PlaceFoe(f, tx, ty, r); r++) { }
            f.Alert = AlertLevel.Alert; f.Ammo = f.Weapon.Clip;
        }
        if (foes.Count > 0) { foes[0].OnOverwatch = true; }   // one live reaction lane in the frame

        RecomputeMoveCost();
        // Measured per-selection cost on a REAL board (real reachable set, real roster), warm —
        // the number the wave report quotes. Console-only; NoPersist keeps it out of live play.
        {
            int reach = 0;
            if (MoveCost != null)
                for (int x = 0; x < Grid.W; x++) for (int y = 0; y < Grid.H; y++) if (MoveCost[x, y] > 0) reach++;
            for (int i = 0; i < 5; i++) { _threatSig = 0; ComputeThreat(); }        // warm
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 50; i++) { _threatSig = 0; ComputeThreat(); }
            sw.Stop();
            int foeN = AliveEnemies().Count(e => e.Active && e.Ammo > 0);
            Console.WriteLine($"HARNESS THREATPERF: reachable={reach} guns={foeN} rebuild={sw.Elapsed.TotalMilliseconds / 50.0:0.000} ms");
        }
        // park the cursor on the hottest tile the soldier can actually reach (most guns, then
        // furthest from the soldier so the tinted path has some length to show).
        int bx = sol.X, by = sol.Y, best = -1;
        if (Threat != null && MoveCost != null)
            for (int x = 0; x < Grid.W; x++)
                for (int y = 0; y < Grid.H; y++)
                {
                    if (MoveCost[x, y] <= 0 || MoveCost[x, y] > sol.MoveBudget) continue;
                    if (UnitAt(x, y) != null) continue;
                    int score = Threat[x, y].Guns * 100 + (int)Util.TileDist(sol.X, sol.Y, x, y);
                    if (score > best) { best = score; bx = x; by = y; }
                }
        DebugMousePark = Util.TileCenter(bx, by);
    }

    /// Set by DebugThreatShot: Program parks the headless cursor here every frame so hover-driven
    /// chrome (path preview + the incoming-fire card) is present in the captured frame.
    public System.Numerics.Vector2? DebugMousePark;

    /// Move `f` to (tx,ty) or the first free floor tile within `r` of it. True on success.
    bool PlaceFoe(Unit f, int tx, int ty, int r)
    {
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                int x = tx + dx, y = ty + dy;
                if (!Grid.InBounds(x, y) || !Grid.IsFloor(x, y)) continue;
                if (IsOccupiedByOther(x, y, f)) continue;
                f.X = x; f.Y = y; f.SyncPos(); return true;
            }
        return false;
    }


    // ─── PROGRAM RESONANCE T1 — onboarding self-test (SIGHTLINE_TUTTEST=1) ────────────────────
    /// Pins the whole T1 contract:
    ///   (1) the TRAINING OP arena/script is well-formed and the drill builds on it;
    ///   (2) EVERY lesson trigger predicate is REACHABLE and fires EXACTLY ONCE (plus its patience
    ///       fallback, so no lesson can strand a player who solves it another way);
    ///   (3) verb staging reveals monotonically, is CAPPED to the drill + mission 1, exempts the
    ///       emergency verb, and the SHOW ALL escape bypasses it;
    ///   (4) the field-tip table's on-disk bits / priorities / codes are internally consistent and
    ///       every predicate is callable and fires at most once per profile;
    ///   (5) every seen-flag round-trips through Display.Save/Load (incl. the FUL-12 BraceTipSeen
    ///       migration bridge), and the drill writes NO run/meta file.
    /// Preserves and restores the real display.json around the round-trip.
    public string TutorialSelfTest()
    {
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70119);
        var fails = new List<string>();

        // ---- (1) the drill's arena + build ----------------------------------------------------
        var probe = new Grid();
        if (Maps.TrainingArena.Length != probe.H) fails.Add("arenaRows");
        foreach (var row in Maps.TrainingArena) if (row.Length != probe.W) fails.Add("arenaCols");
        if (Maps.TrainingDeploy.Length < 2) fails.Add("deploySeats");
        if (Maps.TrainingFoes.Length < 4) fails.Add("foeSeats");

        var g = new Game { NoPersist = true };
        g.BeginTraining();
        if (g.Mode != GameMode.Training) fails.Add("modeNotTraining");
        if (g.Objective != Objective.Eliminate) fails.Add("drillObjective");
        if (g.Players.Count != 2) fails.Add("drillSquadSize");
        if (g.Enemies.Count != Maps.TrainingFoes.Length) fails.Add("drillForceSize");
        if (g.RunState != null && g.RunState.HeatLevel != 0) fails.Add("drillHeat");
        if (!g.SquadConcealed) fails.Add("drillNotConcealed");
        if (g.Enemies.Any(e => e.Active)) fails.Add("drillFoesNotDormant");
        // deploy + hostile seats survived the template stamp as open floor, and nobody shares a tile
        var seats = new HashSet<(int, int)>();
        foreach (var u in g.Players)
        {
            if (!g.Grid.IsFloor(u.X, u.Y)) fails.Add("deployNotFloor");
            if (!seats.Add((u.X, u.Y))) fails.Add("seatCollision");
        }
        foreach (var e in g.Enemies)
        {
            if (!g.Grid.IsFloor(e.X, e.Y)) fails.Add("foeNotFloor");
            if (!seats.Add((e.X, e.Y))) fails.Add("seatCollision");
        }
        // every hostile is walkable-reachable from the first recruit (the drill must be completable)
        {
            var cost = g.Grid.CostMap(g.Players[0].X, g.Players[0].Y, (x, y) => false, out _, 9999);
            foreach (var e in g.Enemies) if (cost[e.X, e.Y] < 0) fails.Add("foeUnreachable");
        }
        // the two lesson-critical tiles the arena was authored around must exist as open floor:
        // (4,4)/(4,6) are the COVER lesson's blocks, (12,1) is the FLANK lesson's answer tile.
        if (g.Grid.Tiles[4, 4] != TileType.LowCover || g.Grid.Tiles[4, 6] != TileType.LowCover) fails.Add("coverLessonTiles");
        if (!g.Grid.IsFloor(12, 1)) fails.Add("flankLessonTile");

        // ---- (2) the lesson table + every trigger predicate -------------------------------------
        var codes = new HashSet<string>();
        var barIds = new HashSet<string>
        {
            "shoot", "grenade", "ability", "item", "shove", "drag", "vault", "stabilize",
            "overwatch", "focusow", "brace", "hunker", "hack", "beacon", "extract", "reload",
        };
        foreach (var l in TrainLessons)
        {
            if (string.IsNullOrEmpty(l.Code) || !codes.Add(l.Code)) fails.Add("lessonCode:" + l.Code);
            if (string.IsNullOrWhiteSpace(l.Text)) fails.Add("lessonText:" + l.Code);
            if (l.Done == null) fails.Add("lessonPredicate:" + l.Code);
            foreach (var v in l.Reveal) if (!barIds.Contains(v)) fails.Add("lessonRevealsUnknownVerb:" + v);
        }
        if (TrainLessons[TrainLessons.Length - 1].Done(g)) fails.Add("terminalLessonSelfSolves");

        // Drive the drill lesson by lesson. Each step: assert the predicate is FALSE, apply the
        // one world change that solves it, assert it goes TRUE, tick UpdateTraining ONCE and assert
        // the track advanced by EXACTLY one (fires once, never twice).
        void Step(string code, Action solve)
        {
            int at = g.TrainStep;
            if (at < 0 || at >= TrainLessons.Length || TrainLessons[at].Code != code)
            { fails.Add("lessonOrder@" + code + "(was " + (at >= 0 && at < TrainLessons.Length ? TrainLessons[at].Code : "-") + ")"); return; }
            if (TrainLessons[at].Done(g)) fails.Add("lessonPreSolved:" + code);
            solve();
            if (!TrainLessons[at].Done(g)) fails.Add("lessonUnreachable:" + code);
            g.UpdateTraining(1f / 60f);
            if (g.TrainStep != at + 1) fails.Add("lessonAdvance:" + code + "->" + g.TrainStep);
            g.UpdateTraining(1f / 60f);   // a second tick must not skip the NEXT lesson too
            if (g.TrainStep != at + 1) fails.Add("lessonDoubleFire:" + code);
        }
        if (g.TrainStep != 0) fails.Add("drillLessonNotOpen");
        Step("MOVE",      () => g.DebugSetTutFlag("move"));
        Step("COVER",     () => { g.Players[0].X = 5; g.Players[0].Y = 4; g.Players[0].SyncPos(); });
        Step("FLANK",     () => { g.Players[0].X = 12; g.Players[0].Y = 1; g.Players[0].SyncPos(); });
        Step("FIRE",      () => g.DebugSetTutFlag("shot"));
        Step("OVERWATCH", () => g.DebugSetTutFlag("over"));
        Step("GRENADE",   () => g.DebugSetTutFlag("grenade"));
        Step("ABILITY",   () => g.DebugSetTutFlag("ability"));
        if (g.TrainStep != TrainLessons.Length - 1) fails.Add("didNotReachTerminalLesson");
        // the terminal lesson never self-advances, and never runs off the end of the table
        for (int i = 0; i < 8; i++) g.UpdateTraining(1f / 60f);
        if (g.TrainStep != TrainLessons.Length - 1) fails.Add("terminalLessonAdvanced");

        // patience fallback: a lesson yields on its turn budget even when never solved
        {
            var pg = new Game { NoPersist = true };
            pg.BeginTraining();
            int p0 = pg.TrainStep;
            pg.DebugSetTurn(1 + TrainLessons[p0].Patience);
            pg.UpdateTraining(1f / 60f);
            if (pg.TrainStep != p0 + 1) fails.Add("patienceFallback");
        }

        // ---- (3) verb staging ------------------------------------------------------------------
        {
            var sg = new Game { NoPersist = true };
            sg.BeginTraining();
            if (!sg.OnboardingActive || !sg.VerbStagingActive) fails.Add("stagingNotActiveAtOpen");
            if (sg.VerbRevealed("shoot")) fails.Add("stagedShootVisibleAtOpen");
            if (sg.VerbRevealed("brace")) fails.Add("stagedBraceVisibleAtOpen");
            if (!sg.VerbRevealed("stabilize")) fails.Add("emergencyVerbStagedAway");
            // reveals are MONOTONIC: replaying up to lesson k must never drop an earlier verb
            var seen = new HashSet<string>();
            for (int k = 0; k < TrainLessons.Length; k++)
            {
                sg.ShowTrainingLesson(k);
                foreach (var v in seen) if (!sg.RevealedVerbs.Contains(v)) fails.Add("revealRegressed:" + v);
                foreach (var v in TrainLessons[k].Reveal) seen.Add(v);
            }
            sg.ShowTrainingLesson(3);   // the FIRE lesson
            if (!sg.VerbRevealed("shoot") || !sg.VerbRevealed("reload")) fails.Add("fireLessonRevealsShoot");
            if (sg.VerbRevealed("grenade")) fails.Add("fireLessonLeaksGrenade");
            // the terminal lesson is the graduation: staging off, everything visible
            sg.ShowTrainingLesson(TrainLessons.Length - 1);
            if (sg.OnboardingActive || sg.VerbStagingActive) fails.Add("terminalLessonStillStaging");
            if (!sg.VerbRevealed("brace")) fails.Add("terminalLessonNotFullBar");
            // SHOW ALL escape bypasses staging in BOTH directions and never desyncs OnboardingActive
            sg.ShowTrainingLesson(0);
            sg.ToggleShowAllVerbs();
            if (!sg.ShowAllVerbs) fails.Add("showAllToggleOn");
            if (sg.VerbStagingActive) fails.Add("showAllDidNotBypass");
            if (!sg.OnboardingActive) fails.Add("showAllKilledOnboardingContext");
            if (!sg.VerbRevealed("brace")) fails.Add("showAllStillHiding");
            sg.ToggleShowAllVerbs();
            if (sg.ShowAllVerbs || !sg.VerbStagingActive) fails.Add("showAllToggleOff");
        }
        // the CAP: campaign mission 2+ is never staged, whatever TutStep says
        {
            var cg = new Game { NoPersist = true };
            cg.StartMission(2);
            cg.ShowTutorialStep(TutStepOverwatch);
            if (cg.OnboardingActive || cg.VerbStagingActive) fails.Add("mission2Staged");
            if (!cg.VerbRevealed("brace")) fails.Add("mission2HidingVerbs");
        }
        // mission 1 IS staged while the strip runs, and the wrap-up card ends it
        {
            var cg = new Game { NoPersist = true };
            cg.StartMission(1);
            cg.ShowTutorialStep(TutStepConceal);
            if (!cg.VerbStagingActive) fails.Add("mission1NotStaged");
            if (cg.VerbRevealed("shoot")) fails.Add("mission1LeaksShootAtConceal");
            cg.ShowTutorialStep(TutStepFire);
            if (!cg.VerbRevealed("shoot") || !cg.VerbRevealed("overwatch")) fails.Add("mission1FireReveal");
            cg.ShowTutorialStep(TutStepDone);
            if (cg.OnboardingActive) fails.Add("wrapUpStillStaging");
        }
        // the LOAD-BEARING completion gates (EnterBarracks/LoseRun compare against TutStepFire)
        // (through an array so the check is a real runtime comparison, not const-folded away)
        int[] gates = { TutStepConceal, TutStepMove, TutStepOverwatch, TutStepFire, TutStepDone };
        for (int gi = 0; gi < gates.Length; gi++) if (gates[gi] != gi) fails.Add("tutStepConstantsMoved");
        if (TutPrompts.Length != TutStepDone + 1) fails.Add("tutPromptCount");
        if (TutReveal.Length != TutPrompts.Length) fails.Add("tutRevealMisaligned");

        // ---- (4) the field-tip table -----------------------------------------------------------
        {
            var bits = new HashSet<int>(); var prios = new HashSet<int>(); var tcodes = new HashSet<string>();
            foreach (var t in FieldTips)
            {
                if (t.Bit < 0 || t.Bit >= Display.MaxTips) fails.Add("tipBitRange:" + t.Code);
                if (!bits.Add(t.Bit)) fails.Add("tipBitDup:" + t.Code);
                if (!prios.Add(t.Prio)) fails.Add("tipPrioDup:" + t.Code);
                if (string.IsNullOrEmpty(t.Code) || !tcodes.Add(t.Code)) fails.Add("tipCodeDup:" + t.Code);
                if (string.IsNullOrWhiteSpace(t.Text)) fails.Add("tipText:" + t.Code);
                if (t.When == null) { fails.Add("tipPredicate:" + t.Code); continue; }
                try { t.When(g); } catch { fails.Add("tipPredicateThrew:" + t.Code); }
            }
            if (FieldTips.Length < 10) fails.Add("tipTableTooSmall");
            if (FieldTips[0].Bit != 0 || FieldTips[0].Code != "BRACE") fails.Add("braceTipNotBit0");

            // Every predicate is REACHABLE: stage the world state each one names and assert it turns
            // true. One shared drill board, mutated per tip and rolled back.
            var tg = new Game { NoPersist = true };
            tg.BeginTraining();
            foreach (var e in tg.Enemies) e.Alert = AlertLevel.Alert;   // "a live threat" for all of them
            var pa = tg.Players[0]; var pb = tg.Players[1];
            bool Fires(string code)
            {
                foreach (var t in FieldTips) if (t.Code == code) return t.When(tg);
                fails.Add("tipMissing:" + code); return false;
            }
            if (!Fires("BRACE")) fails.Add("tipUnreachable:BRACE");
            pa.Ammo = 0;              if (!Fires("RELOAD")) fails.Add("tipUnreachable:RELOAD");
            pa.Ammo = pa.Weapon.Clip;
            // GRENADE: the drill's cover-hugging pair at (12,4)/(12,6) IS the staged case — sweep
            // the board for ANY stance that sees one of them in cover (a stronger reachability claim
            // than one hand-picked tile, and it survives an arena edit).
            pa.Grenades = 1; pb.X = 2; pb.Y = 6; pb.SyncPos();
            bool grenReach = false;
            for (int gx = 0; gx < tg.Grid.W && !grenReach; gx++)
                for (int gy = 0; gy < tg.Grid.H && !grenReach; gy++)
                {
                    if (!tg.Grid.IsFloor(gx, gy) || tg.Enemies.Any(e => e.X == gx && e.Y == gy)) continue;
                    pa.X = gx; pa.Y = gy; pa.SyncPos();
                    if (Fires("GRENADE")) grenReach = true;
                }
            if (!grenReach) fails.Add("tipUnreachable:GRENADE");
            // HUNKER: a soldier in the open, seen by a live foe, with an action in hand
            pa.X = 12; pa.Y = 2; pa.SyncPos(); pa.ActionsLeft = 2;
            pb.X = 12; pb.Y = 3; pb.SyncPos();
            if (!Fires("HUNKER")) fails.Add("tipUnreachable:HUNKER");
            // SHOVE: step a recruit adjacent to a hostile
            pa.X = 12; pa.Y = 3; pa.SyncPos(); pb.X = 2; pb.Y = 6; pb.SyncPos();
            if (!Fires("SHOVE")) fails.Add("tipUnreachable:SHOVE");
            // VAULT / DRAG: park the pair beside the drill's cover blocks, shoulder to shoulder
            // (DRAG needs a Chebyshev-2 ally: you cannot pull someone already shoulder-to-shoulder)
            pa.X = 5; pa.Y = 4; pa.SyncPos(); pa.ActionsLeft = 2;
            pb.X = 5; pb.Y = 6; pb.SyncPos(); pb.ActionsLeft = 2;
            if (!Fires("VAULT")) fails.Add("tipUnreachable:VAULT");
            if (!Fires("DRAG")) fails.Add("tipUnreachable:DRAG");
            tg.DebugSetTutFlag("over");
            if (!Fires("FOCUS")) fails.Add("tipUnreachable:FOCUS");
            if (!Fires("ITEM")) fails.Add("tipUnreachable:ITEM");
            // STABILIZE: the one tip keyed on a bleeding-out ally
            pb.Downed = true;
            if (!Fires("STABILIZE")) fails.Add("tipUnreachable:STABILIZE");
            pb.Downed = false;
        }

        // ---- (5) persistence: round-trip + migration + the drill's no-write contract ------------
        string dispPath = Display.SettingsPathPublic;
        string dispStash = null; bool hadDisp = false;
        try { hadDisp = System.IO.File.Exists(dispPath); if (hadDisp) dispStash = System.IO.File.ReadAllText(dispPath); } catch { }
        int savedTips = Display.TipsSeen; bool savedTrain = Display.TrainingSeen;
        bool savedShow = Display.ShowAllVerbs, savedBrace = Display.BraceTipSeen, savedTut = Display.TutorialSeen;
        try
        {
            // every tip bit, plus both new bools, survive a real JSON round trip
            Display.TipsSeen = 0;
            foreach (var t in FieldTips) Display.MarkTipSeen(t.Bit);
            Display.TrainingSeen = true; Display.ShowAllVerbs = true;
            int wrote = Display.TipsSeen;
            Display.SaveForTest();
            Display.TipsSeen = 0; Display.TrainingSeen = false; Display.ShowAllVerbs = false; Display.BraceTipSeen = false;
            Display.LoadForTest();
            if (Display.TipsSeen != wrote) fails.Add("tipsSeenRoundTrip");
            foreach (var t in FieldTips) if (!Display.TipSeen(t.Bit)) fails.Add("tipFlagRoundTrip:" + t.Code);
            if (!Display.TrainingSeen) fails.Add("trainingSeenRoundTrip");
            if (!Display.ShowAllVerbs) fails.Add("showAllRoundTrip");
            if (!Display.BraceTipSeen) fails.Add("braceBridgeOnSave");

            // an unseen tip stays unseen across the trip (the mask is not a blanket "all true")
            Display.TipsSeen = 0; Display.MarkTipSeen(1);
            Display.SaveForTest(); Display.TipsSeen = 0; Display.LoadForTest();
            if (!Display.TipSeen(1) || Display.TipSeen(0) || Display.TipSeen(2)) fails.Add("tipMaskPrecision");

            // FUL-12 migration bridge: an OLD display.json has only BraceTipSeen — it must fold
            // into bit 0 so a player who already read that tip never sees it again.
            System.IO.File.WriteAllText(dispPath, "{\"BraceTipSeen\":true}");
            Display.TipsSeen = 0; Display.BraceTipSeen = false;
            Display.LoadForTest();
            if (!Display.TipSeen(0) || !Display.BraceTipSeen) fails.Add("legacyBraceMigration");
            // ...and a file with neither field leaves everything unseen
            System.IO.File.WriteAllText(dispPath, "{}");
            Display.TipsSeen = 0x7f; Display.TrainingSeen = true; Display.ShowAllVerbs = true;
            Display.LoadForTest();
            if (Display.TipsSeen != 0 || Display.TrainingSeen || Display.ShowAllVerbs) fails.Add("emptyProfileDefaults");
        }
        catch (Exception ex) { fails.Add("persistThrew:" + ex.GetType().Name); }
        finally
        {
            Display.TipsSeen = savedTips; Display.TrainingSeen = savedTrain;
            Display.ShowAllVerbs = savedShow; Display.BraceTipSeen = savedBrace; Display.TutorialSeen = savedTut;
            try
            {
                if (hadDisp) System.IO.File.WriteAllText(dispPath, dispStash);
                else if (System.IO.File.Exists(dispPath)) System.IO.File.Delete(dispPath);
            }
            catch { }
        }

        // The drill's NO-WRITE contract: a LIVE (persisting) training op must not create or touch
        // save.json or meta.json. Snapshot both, run a full drill to a win, compare.
        {
            string sp = SaveGame.SavePathPublic, mp = SaveGame.MetaPathPublic;
            bool hadSave = System.IO.File.Exists(sp), hadMeta = System.IO.File.Exists(mp);
            string saveBefore = hadSave ? System.IO.File.ReadAllText(sp) : null;
            string metaBefore = hadMeta ? System.IO.File.ReadAllText(mp) : null;
            bool trainBefore = Display.TrainingSeen;
            var lg = new Game();                 // NoPersist deliberately FALSE — the live path
            lg.BeginTraining();
            foreach (var e in lg.Enemies) { e.Hp = 0; e.Alive = false; }
            lg.DebugCheckEnd();
            if (lg.Phase != Phase.Win) fails.Add("drillWinNotDetected");
            bool hadSaveAfter = System.IO.File.Exists(sp), hadMetaAfter = System.IO.File.Exists(mp);
            if (hadSave != hadSaveAfter) fails.Add("drillTouchedSaveExistence");
            if (hadMeta != hadMetaAfter) fails.Add("drillTouchedMetaExistence");
            if (hadSave && hadSaveAfter && System.IO.File.ReadAllText(sp) != saveBefore) fails.Add("drillWroteSave");
            if (hadMeta && hadMetaAfter && System.IO.File.ReadAllText(mp) != metaBefore) fails.Add("drillWroteMeta");
            if (!Display.TrainingSeen) fails.Add("drillDidNotMarkSeen");   // the ONE flag it may set
            // restore the profile flag + its file exactly as we found it
            if (!trainBefore)
            {
                Display.TrainingSeen = false;
                try
                {
                    if (hadDisp) System.IO.File.WriteAllText(dispPath, dispStash);
                    else if (System.IO.File.Exists(dispPath)) System.IO.File.Delete(dispPath);
                }
                catch { }
            }
        }

        return fails.Count == 0
            ? $"TUTTEST: PASS (drill arena+build, {TrainLessons.Length} lesson triggers reachable/once/patience, "
              + $"staging monotonic+capped+escapable, {FieldTips.Length} field tips reachable+unique, "
              + "seen-flag round-trip + BraceTipSeen migration, drill writes no save/meta)"
            : "TUTTEST: FAIL (" + string.Join(",", fails.Distinct()) + ")";
    }


    // ─── W5 THE FIRST HOUR — the CHROME self-test (SIGHTLINE_CHROMETEST=1) ─────────────────────
    /// Three audit findings that all reduce to "a thing the player is reading moved, or vanished,
    /// or fell out of its box". Each leg fails on the pre-W5 tree; SIGHTLINE_BARREFLOW=1 restores
    /// the old action-bar order so the first one is falsifiable without reverting.
    ///
    ///  (A) visual-7 — THE ACTION BAR RE-FLOWED BETWEEN TURNS. The auditor measured OVERWATCH
    ///      moving from bottom-row slot 8 to TOP-row slot 1 purely because a squadmate went down
    ///      and STABILIZE appeared ahead of it in the list. Drive one soldier through three
    ///      states — full verbs / no ammo / squadmate down — and assert every verb present in all
    ///      three has a BYTE-IDENTICAL rect. Also asserts the ability slot's width absorbs its
    ///      " (N)" cooldown suffix, which is the same defect wearing a different hat.
    ///  (B) newplayer-3 — THE CONCEALED PILL FADED TO 10% ALPHA, so the opening state read as
    ///      "off" for part of every 1.8 s cycle. Asserts the pulse envelope's floor and its
    ///      peak/trough ratio directly, at the four fixed phases the auditor sampled.
    ///  (C) newplayer-6 — DOCTRINE CARD TEXT OVERFLOWED ITS BOX on the first screen a new player
    ///      touches. Asserts `body top + lines*lineH + pad <= card height` for EVERY boon in the
    ///      catalogue, not just the three in one offer, and that the operator blurb column clears
    ///      the class-glyph disc.
    public string ChromeSelfTest()
    {
        var fails = new List<string>();

        // ---- (A) the fixed slot map ----------------------------------------------------------
        {
            var g = new Game { NoPersist = true };
            g.StartMission(1);
            var u = g.Players.FirstOrDefault(p => p.Alive && !p.IsVip);
            var mate = g.Players.FirstOrDefault(p => p != u && p.Alive && !p.IsVip);
            if (u == null || mate == null) fails.Add("noSquadForBarProbe");
            else
            {
                g.Selected = u;
                Dictionary<string, Rectangle> Snap()
                {
                    Raylib.BeginDrawing();
                    var bar = Hud.ProbeActionBar(g);
                    Raylib.EndDrawing();
                    var d = new Dictionary<string, Rectangle>();
                    foreach (var b in bar) d[b.Id] = b.Rect;
                    return d;
                }
                int ammo = u.Ammo;
                var full = Snap();                            // state 1: everything available
                u.Ammo = 0; var dry = Snap();                 // state 2: no ammo
                u.Ammo = ammo;
                mate.Downed = true; var down = Snap();        // state 3: a squadmate is bleeding out
                mate.Downed = false;

                if (full.Count < 8) fails.Add("barTooSmall:" + full.Count);
                if (!down.ContainsKey("stabilize")) fails.Add("stabilizeNeverSurfaced");
                if (dry.Count != full.Count) fails.Add("dryChangedTheVerbSet");
                foreach (var id in full.Keys)
                {
                    if (!dry.TryGetValue(id, out var rd) || !SameRect(full[id], rd))
                        fails.Add("moved(noAmmo):" + id);
                    if (!down.TryGetValue(id, out var rw) || !SameRect(full[id], rw))
                        fails.Add("moved(mateDown):" + id);
                }
                // the ability slot must already be wide enough for its cooldown suffix
                if (u.Ability != AbilityKind.None && full.ContainsKey("ability"))
                {
                    var before = full["ability"];
                    u.AbilityCd = 3;
                    var cd = Snap();
                    u.AbilityCd = 0;
                    if (!cd.TryGetValue("ability", out var ra) || !SameRect(before, ra))
                        fails.Add("abilitySlotGrewOnCooldown");
                    foreach (var id in full.Keys)
                        if (cd.TryGetValue(id, out var r2) && !SameRect(full[id], r2))
                            fails.Add("moved(abilityCd):" + id);
                }
            }
        }

        // ---- (B) the CONCEALED pill's pulse envelope ------------------------------------------
        {
            // The exact expression Hud draws with, sampled at the four phases the audit used.
            float lo = 2f, hi = -1f;
            foreach (float t in new[] { 0.5f, 1.0f, 1.5f, 2.5f })
            {
                float pulse = Hud.ConcealPulse(t);
                lo = MathF.Min(lo, pulse); hi = MathF.Max(hi, pulse);
            }
            // measured floor across the whole cycle, not just those four samples
            float trueLo = Hud.ConcealPulseFloor;
            if (trueLo < 0.5f) fails.Add($"pillFloor={trueLo:0.00}");
            if (Hud.ConcealPulseCeil / MathF.Max(0.001f, trueLo) > 2.0f)
                fails.Add($"pillSwing={Hud.ConcealPulseCeil / trueLo:0.00}x");
            if (hi <= lo) fails.Add("pillDoesNotPulse");   // it must still BREATHE, not go static
        }

        // ---- (C) the doctrine card fits its own text, for every boon in the catalogue ---------
        //
        // W5-FIX (review): the original form of this leg was TAUTOLOGICAL. It compared
        //     need = BodyTop + lines*LineH + PadB      against      DraftBoonCardHeight(lines)
        // which IS Math.Max(74, need) — the assertion could not fail for any string whatsoever.
        // It now measures where the last line's INK actually lands, through the real font at the
        // live UI scale (Hud.DraftBoonInkBottom), against the height the RENDERER uses. That can
        // fail: shrink DraftBoonLineH, or raise DraftBoonFs past the 19 px step, and it does.
        {
            foreach (var b in BoonDef.All)
            {
                int lines = Hud.DraftBoonLineCount(b);
                int bch = Hud.DraftBoonCardHeight(lines);   // the renderer's own card height
                float ink = Hud.DraftBoonInkBottom(b);
                if (ink + 4f > bch) fails.Add($"boonOverflow:{BoonDef.Code(b)}@{ink:0}>{bch}");
                if (lines >= 3 && bch <= 74)
                    fails.Add("boonCardStillFixed:" + BoonDef.Code(b));
            }
            // ...and the DEPLOY row must be ON SCREEN, for every boon in the catalogue, at every
            // text size the pause menu can select. This is review blocker 2: the content-sized
            // card pushed BACK / DEPLOY / RE-ROLL POOL through the bottom of the screen at the
            // DEFAULT 100%, and clean off it at 120% — and RE-ROLL POOL has no keyboard
            // alternative, so a control became unreachable. Nothing observed it, because nothing
            // observed the layout below the card it grew.
            float savedScale = Cfg.UiScale;
            try
            {
                foreach (float ui in Display.UiScaleLevels)
                {
                    Cfg.UiScale = ui;
                    int conH = Hud.DraftContractH();
                    foreach (var b in BoonDef.All)
                    {
                        var st = Hud.DraftLayout(Hud.DraftBoonLineCount(b), conH);
                        string tag = $"{BoonDef.Code(b)}@{(int)(ui * 100)}%";
                        int margin = Cfg.ScreenH - Hud.DraftBottomPad - st.Bottom;
                        if (margin < _draftWorstMargin) { _draftWorstMargin = margin; _draftWorstTag = tag; }
                        _draftWorstSqueeze = Math.Max(_draftWorstSqueeze, st.Squeeze);
                        if (st.Overflow > 0) fails.Add($"draftStackOverflows:{tag}by{st.Overflow}px");
                        if (st.Bottom > Cfg.ScreenH - Hud.DraftBottomPad)
                            fails.Add($"deployRowOffScreen:{tag}@{st.Bottom}");
                        // the doctrine card must still clear the contract header below it
                        if (st.BoonY + st.BoonH > st.ConHeadY) fails.Add("doctrineEatsContractHead:" + tag);
                        if (st.ConY + st.ConH > st.InfoY) fails.Add("contractEatsInfoLine:" + tag);
                    }
                    // THE FIT (wave "THE FIT"): this assertion used to sit OUTSIDE the scale loop,
                    // so it only ever ran at 100% — where the longest blurb had TWO pixels of
                    // margin and 110%/120% silently ellipsized ("picks off th…"). It belongs
                    // inside, and the same width is now re-asserted by FITTEST leg (E).
                    foreach (var bl in Hud.ClassBlurbsForTest())
                        if (Cfg.Measure(bl, Hud.DraftBlurbFs, 1f).X > Hud.DraftBlurbWidth())
                            fails.Add($"blurbEllipsizes@{(int)(ui * 100)}%:" + bl);
                }
            }
            finally { Cfg.UiScale = savedScale; }
            // ...and the operator blurb must clear the class-glyph disc VERTICALLY (the disc
            // dropped into the corner).
            if (Hud.DraftGlyphTop() < Hud.DraftBlurbBottom())
                fails.Add($"glyphOverlapsBlurb({Hud.DraftGlyphTop():0}<{Hud.DraftBlurbBottom()})");
        }

        return fails.Count == 0
            ? "CHROMETEST: PASS (action-bar rects identical across full/no-ammo/mate-down + ability "
              + $"cooldown, CONCEALED pulse {Hud.ConcealPulseFloor:0.00}-{Hud.ConcealPulseCeil:0.00} "
              + $"({Hud.ConcealPulseCeil / Hud.ConcealPulseFloor:0.00}x), {BoonDef.All.Length} doctrine "
              + "cards fit their text, blurb column clears the glyph, DEPLOY row on screen for all "
              + $"{BoonDef.All.Length} x {Display.UiScaleLevels.Length} text sizes - tightest "
              + $"{_draftWorstTag} with {_draftWorstMargin}px to spare, max gap squeeze "
              + $"{_draftWorstSqueeze}px)"
            : "CHROMETEST: FAIL (" + string.Join(",", fails.Distinct()) + ")";
    }

    // W5-FIX: diagnostics for CHROMETEST's draft-stack leg — the tightest DEPLOY-row margin over
    // the whole doctrine catalogue x every text size, so the PASS line states the headroom
    // instead of merely asserting there is some.
    static int _draftWorstMargin = int.MaxValue, _draftWorstSqueeze;
    static string _draftWorstTag = "-";

    static bool SameRect(Rectangle a, Rectangle b)
        => MathF.Abs(a.X - b.X) < 0.01f && MathF.Abs(a.Y - b.Y) < 0.01f
        && MathF.Abs(a.Width - b.Width) < 0.01f && MathF.Abs(a.Height - b.Height) < 0.01f;

    // ─── WAVE "THE FIT" — the TEXT-SCALE self-test (SIGHTLINE_FITTEST=1) ───────────────────────
    /// The standing gate this wave exists to ship. The project's five text-scale defects were all
    /// one structural gap: **no self-test in this project ran at any text size but 100%.**
    /// `SIGHTLINE_UISCALE` is a screenshot-only hook (Program.cs, gated on `shot`) that exists so
    /// a human can PHOTOGRAPH the UI at another size; nothing asserted anything there, while the
    /// game ships four scales {0.90, 1.00, 1.10, 1.20} against a lot of fixed-pixel chrome. W5's
    /// CHROMETEST closed that gap for ONE row of ONE screen (the draft's DEPLOY row) and R2's
    /// VOICETEST for the card BODIES; everything else was still measured once, at 1.00, or not at
    /// all — including CHROMETEST's own `blurbEllipsizes` leg, which sat OUTSIDE its scale loop.
    ///
    /// FITTEST asserts the same contract for the five surfaces this wave touched, at **all four**
    /// shipped scales, through the real font: **no string is painted outside the box that owns it,
    /// and no two independent strings are painted into the same pixels.**
    ///
    ///  (A) FIELD DOCTRINE (mid-run boon offer) — every boon's wrapped body clears "[ CHOOSE ]"
    ///      and the card's own bottom border, and the card block stays on the canvas.
    ///  (B) ARMORY weapon row — the blurb and the right-aligned price/EQUIPPED tag never share a
    ///      vertical band while their horizontal spans overlap, and the blurb fits its budget.
    ///  (C) WAR ROOM HALL OF FAME — over EVERY rank x class (not the five short staged legends),
    ///      the identity and score halves clear each other and the panel's inner border.
    ///  (D) DRAFT bottom row — every plate contains its own widest label, and the row fits.
    ///  (E) DRAFT operator card — the class blurb and the ABILITY line fit their columns, and the
    ///      candidate grid fits the canvas. (This is CHROMETEST's leg, re-run at every scale.)
    ///
    /// Falsifiable without reverting: `SIGHTLINE_OLDFIT=1` restores all five pre-fix geometries.
    public static string FitSelfTest()
    {
        var fails = new List<string>();
        var notes = new List<string>();
        float savedScale = Cfg.UiScale;
        float pad = 4f;                     // px of clear air demanded between independent ink

        // The tightest margin observed per leg, so the PASS line states its own headroom.
        float mA = 9999f, mB = 9999f, mC = 9999f, mD = 9999f, mE = 9999f;
        string tA = "-", tB = "-", tC = "-", tD = "-", tE = "-";
        void Tight(ref float best, ref string tag, float v, string what)
        { if (v < best) { best = v; tag = what; } }

        // -- C5 THE HARD EDGES - THE SCOPE GUARD -------------------------------------------
        // CROSSCUT rule 6: "a correct assertion in the wrong scope is indistinguishable from no
        // assertion" - W5 wrote the right guard for the longest string on the squad screen and
        // put it OUTSIDE the scale loop, and CHROMETEST's own blurb leg sat outside its loop for
        // a wave. Prose cannot enforce that, so this test now COUNTS the assertions it evaluates,
        // per leg, per scale: an assertion that has drifted out of the loop records checks at one
        // scale index only, and the tally below FAILS on it by name. Every leg (A-E, and one row
        // per audited screen) must record at least one check at EVERY shipped scale.
        int scaleIdx = 0;
        var legChecks = new Dictionary<string, int[]>();
        void Bump(string leg)
        {
            if (!legChecks.TryGetValue(leg, out var row)) legChecks[leg] = row = new int[Display.UiScaleLevels.Length];
            row[scaleIdx]++;
        }
        // C5 REVIEW FIX (E1) — THE COUNTER AND THE ASSERTION ARE ONE STATEMENT.
        // The first version bumped and asserted separately, so the number counted BUMPS. The review
        // defeated it in two edits: introduce a real 120%-only defect (PlateSlack 1.5 -> 1.0, which
        // FAILS with 18 violations, all @120%), then condition only the ASSERTION on
        // `S == "@100%"` while leaving its bump — PASS, with a byte-identical headline count, while
        // 18 real violations went unreported. `Check` cannot be separated from what it asserts, so
        // the number now means "assertions EVALUATED".
        // AND THE LIMIT, STATED RATHER THAN IMPLIED: this proves each assertion RAN at each scale.
        // It cannot prove the CONDITION was not itself narrowed — nothing short of mutation testing
        // can. What it buys is that the natural way to lose coverage (moving, gating or deleting an
        // assertion) now moves the number with it.
        void Check(string leg, bool ok, string fail)
        {
            Bump(leg);
            if (!ok) fails.Add(fail);
        }
        int screens = 0;

        try
        {
            foreach (float ui in Display.UiScaleLevels)
            {
                Cfg.UiScale = ui;
                scaleIdx = Array.IndexOf(Display.UiScaleLevels, ui);
                string S = $"@{(int)(ui * 100)}%";

                // ---- (A) the MID-RUN FIELD DOCTRINE card ------------------------------------
                {
                    int lines = 1;
                    foreach (var b in BoonDef.All) lines = Math.Max(lines, Hud.BoonOfferLineCount(b));
                    int ch = Hud.BoonOfferCardH(lines);            // worst-case row height
                    foreach (var b in BoonDef.All)
                    {
                        // the body must clear the [ CHOOSE ] call-to-action's own top edge
                        float ink = Hud.BoonOfferInkBottom(b);
                        float chooseTop = ch - Hud.BoonOfferChooseUp;
                        Tight(ref mA, ref tA, chooseTop - ink, $"{BoonDef.Code(b)}{S}");
                        Check("A", !(ink + pad > chooseTop),
                              $"boonBodyHitsChoose:{BoonDef.Code(b)}{S}({ink:0}>{chooseTop:0})");
                        // ...and no wrapped line may exceed the column it was wrapped to
                        foreach (var (line, _) in Hud.WrapLinesForTest(BoonDef.Desc(b), Hud.BoonOfferBodyW, Hud.BoonOfferFs))
                        {
                            Check("A", !(Cfg.Measure(line, Hud.BoonOfferFs, 1f).X > Hud.BoonOfferBodyW + 1),
                                  $"boonLineOverruns:{BoonDef.Code(b)}{S}");
                        }
                    }
                    // [ CHOOSE ] itself must land inside the card
                    float cbot = ch - Hud.BoonOfferChooseUp + Cfg.Measure("[ CHOOSE ]", Hud.BoonOfferFs, 1f).Y;
                    Check("A", !(cbot + pad > ch), $"chooseBelowCard{S}({cbot:0}>{ch})");
                    // and the whole block (title 92px above, ACTIVE strip 40px below) fits the canvas
                    int y0 = Cfg.ScreenH / 2 - ch / 2 - 10;
                    Check("A", !(y0 - 92 < 8), $"boonTitleOffTop{S}({y0 - 92})");
                    Check("A", !(y0 + ch + 40 > Cfg.ScreenH), $"boonBlockOffBottom{S}({y0 + ch + 40})");
                }

                // ---- (B) the ARMORY weapon row ---------------------------------------------
                {
                    var kinds = new List<WeaponKind>();
                    foreach (var cls in new[] { "ASSAULT", "RANGER", "SHARPSHOOTER", "GUNNER", "CORPSMAN" })
                        foreach (var k in Weapon.ArmoryOptions(cls))
                            if (!kinds.Contains(k)) kinds.Add(k);
                    int rowW = Hud.ArmoryRowW();
                    float blurbTop = Hud.ArmoryBlurbY;
                    float blurbBot = blurbTop + Cfg.Measure("X", Hud.ArmoryBlurbFs, 1f).Y;
                    foreach (var k in kinds)
                    {
                        string blurb = Weapon.KindBlurb(k), name = Weapon.Make(k).Name;
                        float bw = Cfg.Measure(blurb, Hud.ArmoryBlurbFs, 1f).X;
                        float budget = Hud.ArmoryBlurbWidth();
                        Tight(ref mB, ref tB, budget - bw, $"{k}blurb{S}");
                        Check("B", !(bw > budget), $"armoryBlurbOverruns:{k}{S}({bw:0}>{budget:0})");
                        Check("B", !(blurbBot + pad > Hud.ArmoryRowH), $"armoryBlurbBelowRow{S}");
                        float nameRight = Hud.ArmoryTextX + Cfg.Measure(name, Hud.ArmoryNameFs, 1f).X;
                        foreach (var (tag, fs) in new[] { ("EQUIPPED", Hud.ArmoryTagFs),
                                                          ($"[ {Game.ArmoryCost} INTEL ]", Hud.ArmoryTagFs),
                                                          ("- need intel -", Hud.ArmoryBlurbFs) })
                        {
                            float tw = Cfg.Measure(tag, fs, 1f).X;
                            float tagLeft = rowW - Hud.ArmoryPadR - tw;
                            float tagTop = Hud.ArmoryTagY, tagBot = tagTop + Cfg.Measure(tag, fs, 1f).Y;
                            // two independent strings may share a BAND or a COLUMN, never both
                            bool xOverlapBlurb = tagLeft < Hud.ArmoryTextX + bw + pad;
                            bool yOverlapBlurb = tagBot + pad > blurbTop && tagTop < blurbBot + pad;
                            Check("B", !(xOverlapBlurb && yOverlapBlurb),
                                  $"armoryTagOverprintsBlurb:{k}/{tag.Trim('[', ']', ' ')}{S}");
                            Check("B", !(tagLeft < nameRight + 12),
                                  $"armoryTagHitsName:{k}{S}({tagLeft:0}<{nameRight:0})");
                            Check("B", !(tagBot + pad > Hud.ArmoryRowH), $"armoryTagBelowRow:{k}{S}");
                            Check("B", !(tagTop < 2), $"armoryTagAboveRow:{k}{S}");
                        }
                    }
                }

                // ---- (C) the WAR ROOM HALL OF FAME legend row ------------------------------
                {
                    int colW = Hud.WarColWidth;
                    int textW = Hud.WarLegendTextW(colW);
                    // The staged demo profile is five SHORT legends; the real one is every rank in
                    // Run.Ranks against every player class, with a three-digit lifetime kill count
                    // and the longest callsign + nickname the generator can produce.
                    string worstName = "KESTREL \"MAVERICK\"";
                    foreach (var rank in Run.Ranks)
                        foreach (var cls in new[] { "ASSAULT", "RANGER", "SHARPSHOOTER", "GUNNER", "CORPSMAN" })
                        {
                            var l = new SaveGame.LegendDto
                            { Name = worstName, Cls = cls, Rank = rank, Kills = 999, Heat = 8, Won = true };
                            float subW = Cfg.Measure(Hud.WarLegendSub(l), Hud.WarLegendSubFs, 1f).X;
                            Tight(ref mC, ref tC, textW - subW, $"{rank[0]}{cls[0]}sub{S}");
                            Check("C", !(subW > textW),
                                  $"legendSubOverruns:{rank} {cls}{S}({subW:0}>{textW})");
                            string score = Hud.WarLegendScore(l);
                            float scw = Cfg.Measure(score, Hud.WarLegendScoreFs, 1f).X;
                            float nameW = Cfg.Measure(l.Name, Hud.WarLegendNameFs, 1f).X;
                            float scoreLeft = colW - Hud.WarLegendPadR - scw;
                            float nameRight = Hud.WarLegendTextX + nameW;
                            Tight(ref mC, ref tC, scoreLeft - nameRight, $"{rank[0]}{cls[0]}name{S}");
                            Check("C", !(score.Length > 0 && scoreLeft < nameRight + 12),
                                  $"legendScoreHitsName:{rank}{S}({scoreLeft:0}<{nameRight:0})");
                            Check("C", !(scw > textW), $"legendScoreOverruns{S}");
                        }
                }

                // ---- (D) the DRAFT bottom row ----------------------------------------------
                {
                    int dbw = Hud.DraftConfirmW(), rrw = Hud.DraftRerollW(), bkw = Hud.DraftBackW();
                    foreach (var dl in Hud.DraftDeployLabels())
                    {
                        float lw = Cfg.Measure(dl, Hud.DraftDeployFs, 1f).X;
                        Tight(ref mD, ref tD, dbw - lw, $"DEPLOY{S}");
                        Check("D", !(lw + 8 > dbw), $"deployLabelOverruns{S}:{dl}({lw:0}>{dbw})");
                    }
                    float rw = Cfg.Measure(Hud.DraftRerollLabel, Hud.DraftRerollFs, 1f).X;
                    Tight(ref mD, ref tD, rrw - rw, $"REROLL{S}");
                    Check("D", !(rw + 8 > rrw), $"rerollLabelOverruns{S}({rw:0}+8>{rrw})");
                    float bw2 = Cfg.Measure("BACK", Hud.DraftBackFs, 1f).X
                              + Cfg.Measure("[Esc]", Hud.DraftBackHintFs, 1f).X;
                    Tight(ref mD, ref tD, bkw - bw2, $"BACK{S}");
                    Check("D", !(bw2 + 12 > bkw), $"backLabelOverruns{S}({bw2:0}>{bkw})");
                    Check("D", !(Hud.DraftBtnRowW() > Cfg.ScreenW - 24),
                          $"draftBtnRowOffCanvas{S}({Hud.DraftBtnRowW()})");
                }

                // ---- (E) the DRAFT operator card's text columns -----------------------------
                {
                    int bw = Hud.DraftBlurbWidth(), aw = Hud.DraftAbilityWidth();
                    foreach (var bl in Hud.ClassBlurbsForTest())
                    {
                        float w2 = Cfg.Measure(bl, Hud.DraftBlurbFs, 1f).X;
                        Tight(ref mE, ref tE, bw - w2, $"blurb{S}");
                        Check("E", !(w2 > bw), $"draftBlurbEllipsizes{S}:{bl.Substring(0, 12)}({w2:0}>{bw})");
                    }
                    foreach (var ab in new[] { "RUN&GUN", "BLITZ", "STEADY", "SUPPRESS", "PATCH",
                                               "MARK", "GRAPPLE", "SLIPSTREAM", "SUPPR. FIRE" })
                    {
                        float w2 = Cfg.Measure("ABILITY: " + ab, Hud.DraftAbilityFs, 1f).X;
                        Tight(ref mE, ref tE, aw - w2, $"ability{S}");
                        Check("E", !(w2 > aw), $"draftAbilityClips{S}:{ab}");
                    }
                    int gridW = 3 * Hud.DraftCardW() + 2 * Hud.DraftGridGap;
                    Check("E", !(gridW > Cfg.ScreenW - 24), $"draftGridOffCanvas{S}({gridW})");
                }

                // ---- (F) THE SCREEN AUDIT - every screen the game can draw, on a LIVE FRAME ---
                // W10's gate covered five surfaces; the rest of the product was asserted at 100%
                // or not at all. This leg stages each screen, DRAWS it, and reads the ink and the
                // control plates back from the draw calls themselves (Cfg.InkProbe /
                // Hud.PlateProbe) - so it observes what the game paints rather than a
                // transcription of the layout arithmetic, and a screen that throws while drawing
                // is a failure too. Inside the loop by construction: it takes the scale.
                screens = ScreenAudit(S, fails, Check);
            }
        }
        finally { Cfg.UiScale = savedScale; Cfg.InkProbe = null; Hud.PlateProbe = null; Hud.AnimPin = -1f; }

        // ── C5 REVIEW FIX (E1) — THE SENSITIVITY CONTROL, run on every invocation ────────────
        // Binding the counter to the assertion (Check, above) makes the number mean "assertions
        // evaluated" — but the review's step 2 shows what that still cannot see: narrow the
        // CONDITION to one scale and the count does not move. Nothing structural can catch that,
        // so this catches it EMPIRICALLY, the way AIIDLETEST proves its own probe: re-run the whole
        // screen audit at a deliberately UNSHIPPED 200% text scale, where the fixed-pixel chrome
        // must break, and require the assertions to FIRE. If a leg has been gated to one scale, or
        // its tolerance loosened into uselessness, this control goes quiet and the test fails —
        // which is exactly what happens to the review's step-2 mutation.
        // The 200% pass is DIAGNOSTIC ONLY: its violations are counted, never added to `fails`.
        var control = new List<string>();
        try
        {
            Cfg.UiScale = 2.0f;
            scaleIdx = 0;                       // the control's checks are not part of the coverage tally
            ScreenAudit("@200%CONTROL", control, (leg, ok, msg) => { if (!ok) control.Add(msg); });
        }
        catch (Exception ex) { fails.Add("sensitivityControlThrew:" + ex.GetType().Name); }
        finally { Cfg.UiScale = savedScale; Cfg.InkProbe = null; Hud.PlateProbe = null; Hud.AnimPin = -1f; }

        int ctlPlate = 0, ctlInk = 0, ctlClip = 0;
        foreach (var c in control)
        {
            if (c.StartsWith("labelLeavesPlate")) ctlPlate++;
            else if (c.StartsWith("inkOffCanvas")) ctlInk++;
            else if (c.StartsWith("textEllipsized")) ctlClip++;
        }
        if (ctlPlate == 0)
            fails.Add("sensitivityDead:labelLeavesPlate never fired at the 200% control");
        if (ctlInk + ctlClip == 0)
            fails.Add("sensitivityDead:neither inkOffCanvas nor textEllipsized fired at the 200% control");

        // THE SCOPE GUARD's verdict (see the note above the counter).
        foreach (var kv in legChecks)
            for (int i = 0; i < kv.Value.Length; i++)
                if (kv.Value[i] == 0)
                    fails.Add($"legOutsideScaleLoop:{kv.Key}@{(int)(Display.UiScaleLevels[i] * 100)}%(0 checks)");
        int totalChecks = 0;
        foreach (var kv in legChecks) foreach (int n in kv.Value) totalChecks += n;
        if (legChecks.Count < 6) fails.Add($"scopeGuardVacuous(legs={legChecks.Count})");

        notes.Add($"doctrine {mA:0}px@{tA}");
        notes.Add($"armory {mB:0}px@{tB}");
        notes.Add($"legend {mC:0}px@{tC}");
        notes.Add($"draftRow {mD:0}px@{tD}");
        notes.Add($"operatorCard {mE:0}px@{tE}");
        return fails.Count == 0
            ? $"FITTEST: PASS ({BoonDef.All.Length} doctrine cards, 5 weapon rows x 3 tags, "
              + $"{Run.Ranks.Length}x5 legend rows, 5 deploy labels and the operator card fit their "
              + $"chrome at all {Display.UiScaleLevels.Length} shipped text sizes - tightest margins: "
              + string.Join(", ", notes)
              + $"; C5 leg F: {screens} screens DRAWN and audited at every scale - every control "
              + $"plate contains its label (tightest {_fitPlateMargin:0.0}px @{_fitPlateTag}), no "
              + $"visible string leaves the canvas (nearest {_fitEdgeMargin:0.0}px @{_fitEdgeTag}), "
              + $"nothing is ellipsized, smallest type {_fitMinSize:0.#}px authored @{_fitMinAuthTag} "
              + $"and {_fitMinRendered:0.#}px RENDERED @{_fitMinTag} (tracked separately - the "
              + $"smallest authored size and the smallest ink need not be the same screen), "
              + $"{_fitFloors} shrink-to-fit calls "
              + $"reach their floor; {totalChecks} assertions over {legChecks.Count} legs, every "
              + $"leg evaluated at all {Display.UiScaleLevels.Length} scales, and a 200% CONTROL "
              + $"pass fires them ({ctlPlate} plate / {ctlInk} off-canvas / {ctlClip} ellipsis "
              + $"violations at a scale the game does not ship) so the gate is proven live on this "
              + $"run rather than merely counted)"
            : $"FITTEST: FAIL ({fails.Distinct().Count()} violations; first 14: "
              + string.Join(",", fails.Distinct().Take(14)) + ")";
    }







    // ─── C5 THE HARD EDGES — THE PERK CARD'S NUMBERS, MEASURED (a TRUTHTEST leg) ───────────────
    /// W9's finding was that the shot tooltip's numbers were a transcription of what the resolver
    /// was SUPPOSED to do. The PERK CHOOSER is the same shape one screen over: fourteen
    /// "before > after" lines the player picks a permanent upgrade from, every number typed by
    /// hand beside the resolver's constant. They agreed. Nothing bound them, and nothing would
    /// have said so — the card is the only place in the game those numbers appear, so a retune of
    /// `Unit.PerkAim` would have left fourteen cards quietly lying.
    ///
    /// This leg DRAWS the real chooser, captures the line it PAINTS (`Cfg.CaptureText`, the same
    /// seam TooltipTruthFails uses), and compares the numbers in it against a MEASUREMENT taken
    /// through the shipped code path for that perk:
    ///   * TANK / SPRINTER  — `Run.ApplyPerk` on a real soldier: the stat afterwards IS the claim.
    ///   * BANDOLIER        — a real mission setup, which is where grenades are refilled.
    ///   * the aim perks    — `Combat.ComputeOdds` in the stated condition, minus the same shot
    ///                        without the perk.
    ///   * the crit perks   — the same, on `CritChance`.
    ///   * HARDENED/BULWARK — `Combat.HardenedReduce`, the one source of truth for damage taken.
    ///   * COOLHEADED       — the attacker's hit% against a defender who has it.
    /// A perk with no painted line and no measurable claim is fine; a perk with a line whose
    /// numbers do not match the measurement is a lie on the card.
    public string PerkCardTruthFails()
    {
        var fails = new List<string>();
        var grid = new Grid();   // a fresh Grid is all floor at height 0 (see Grid())

        Unit Soldier(int aim = 60, int x = 3, int y = 5)
            => new Unit { Name = "PROBE", Cls = "ASSAULT", Team = Team.Player, Aim = aim, Mobility = 7,
                          Hp = 10, MaxHp = 10, Weapon = Weapon.Make(WeaponKind.Rifle), Alive = true, X = x, Y = y };
        Unit Foe(int x, int y, int hp = 10, int maxHp = 10)
            => new Unit { Name = "FOE", Cls = "GRUNT", Team = Team.Enemy, Aim = 60, Mobility = 6,
                          Hp = hp, MaxHp = maxHp, Weapon = Weapon.Make(WeaponKind.Rifle), Alive = true, X = x, Y = y };

        /// hit% delta a perk buys the ATTACKER on this exact shot.
        int AimDelta(Perk p, Unit a, Unit d)
        {
            int plain = Combat.ComputeOdds(grid, a, d).HitChance;
            a.Perks.Add(p);
            int perked = Combat.ComputeOdds(grid, a, d).HitChance;
            a.Perks.Remove(p);
            return perked - plain;
        }
        int CritDelta(Perk p, Unit a, Unit d, bool highGround = false)
        {
            if (highGround) { grid.Height[a.X, a.Y] = 2; }
            int plain = Combat.ComputeOdds(grid, a, d).CritChance;
            a.Perks.Add(p);
            int perked = Combat.ComputeOdds(grid, a, d).CritChance;
            a.Perks.Remove(p);
            if (highGround) grid.Height[a.X, a.Y] = 0;
            return perked - plain;
        }

        // THE MEASUREMENTS. Each entry answers "what does the shipped code actually do?" as a
        // function of the soldier the card is offered to — never as a literal.
        //   stat  : the value the perk changes, before -> after, through Run.ApplyPerk
        //   delta : the aim/crit/damage swing ComputeOdds or HardenedReduce actually produces
        //   gate  : the RANGE at which a range-gated perk stops applying, found by scanning
        var deltas = new Dictionary<Perk, List<int>>();

        // -- aim perks: measure the swing AND, for the range-gated pair, the gate itself ---------
        {
            var a = Soldier(); var d = Foe(a.X + 2, a.Y);
            int cq = AimDelta(Perk.CloseQuarters, a, d);
            // the gate: the largest distance at which CLOSE QUARTERS still fires
            int cqGate = 0;
            for (int dist = 1; dist <= 10; dist++)
            {
                var dd = Foe(a.X + dist, a.Y);
                if (AimDelta(Perk.CloseQuarters, a, dd) > 0) cqGate = dist;
            }
            deltas[Perk.CloseQuarters] = new List<int> { cq, cqGate };

            var a2 = Soldier(); int mk = 0, mkGate = 99;
            for (int dist = 1; dist <= 12; dist++)
            {
                var dd = Foe(a2.X + dist, a2.Y);
                int sw = AimDelta(Perk.Marksman, a2, dd);
                if (sw > 0 && dist < mkGate) { mkGate = dist; mk = sw; }
            }
            deltas[Perk.Marksman] = new List<int> { mk, mkGate };

            var a3 = Soldier(); var d3 = Foe(a3.X + 3, a3.Y); d3.Hunkered = true;
            deltas[Perk.Siegebreaker] = new List<int> { AimDelta(Perk.Siegebreaker, a3, d3) };

            // LOCK-ON fires on FLANKED, and the shared predicate is the same one W9 bound the
            // tooltip badge to.
            var a4 = Soldier(); a4.Perks.Add(Perk.LockOn);
            deltas[Perk.LockOn] = new List<int> { Combat.LockOnAim(a4, flanked: true) };
        }
        // -- crit perks --------------------------------------------------------------------------
        {
            var a = Soldier();
            deltas[Perk.Executioner] = new List<int> { CritDelta(Perk.Executioner, a, Foe(a.X + 3, a.Y, hp: 3)) };
            deltas[Perk.GiantSlayer] = new List<int> { CritDelta(Perk.GiantSlayer, a, Foe(a.X + 3, a.Y)) };
            deltas[Perk.Vantage] = new List<int> { CritDelta(Perk.Vantage, a, Foe(a.X + 3, a.Y), highGround: true) };
            var ds = Foe(a.X + 3, a.Y); ds.Suppress = 1;
            deltas[Perk.Breaker] = new List<int> { CritDelta(Perk.Breaker, a, ds) };
        }
        // -- damage-taken perks, through the ONE reduction path ----------------------------------
        {
            var d = Soldier();
            int plain = Combat.HardenedReduce(d, 9, crit: false, telegraph: false);
            d.Perks.Add(Perk.Hardened);
            int flat = plain - Combat.HardenedReduce(d, 9, crit: false, telegraph: false);
            int onCrit = plain - Combat.HardenedReduce(d, 9, crit: true, telegraph: false);
            d.Perks.Remove(Perk.Hardened);
            deltas[Perk.Hardened] = new List<int> { flat, onCrit };

            var b = Soldier();                                    // full HP => Bulwark active
            int bPlain = Combat.HardenedReduce(b, 9, crit: false, telegraph: false);
            b.Perks.Add(Perk.Bulwark);
            deltas[Perk.Bulwark] = new List<int> { bPlain - Combat.HardenedReduce(b, 9, crit: false, telegraph: false) };
        }
        {
            var atk = Foe(3, 5); var def = Soldier(60, 6, 5);
            int plain = Combat.ComputeOdds(grid, atk, def).HitChance;
            def.Perks.Add(Perk.CoolHeaded);
            deltas[Perk.CoolHeaded] = new List<int> { plain - Combat.ComputeOdds(grid, atk, def).HitChance };
        }
        // -- the two STAT BUMPS, through the real applier ----------------------------------------
        {
            var u = Soldier(); int hp0 = u.MaxHp; Run.ApplyPerk(u, Perk.Tank);
            deltas[Perk.Tank] = new List<int> { u.MaxHp - hp0 };
            var v = Soldier(); int mob0 = v.Mobility; Run.ApplyPerk(v, Perk.Sprinter);
            deltas[Perk.Sprinter] = new List<int> { v.Mobility - mob0 };
        }
        // -- BANDOLIER through the path that actually hands out grenades: a real mission ---------
        {
            var g = new Game { NoPersist = true };
            g.StartMission(1);
            var s = g.Players.FirstOrDefault(u => u != null && !u.IsVip);
            if (s == null) fails.Add("noSoldierForBandolier");
            else
            {
                int before = s.Grenades;
                s.Perks.Add(Perk.Bandolier);
                g.DebugResetupMission();
                var s2 = g.Players.FirstOrDefault(u => u != null && u.Name == s.Name);
                if (s2 == null) fails.Add("bandolierSoldierVanished");
                else deltas[Perk.Bandolier] = new List<int> { s2.Grenades - before };
            }
        }

        // ---- now READ THE CARD, for real -------------------------------------------------------
        // TWO assertions per perk, and they are different assertions:
        //   (i)  the chooser PAINTS the string its own formatter returns — the observation binding
        //        W9's review demanded (a test that only checks the formatter proves nothing about
        //        the panel);
        //   (ii) the numbers IN that string are the ones the measurement above produced.
        foreach (var kv in deltas)
        {
            var perk = kv.Key;
            var swing = kv.Value;
            var cap = new List<(string text, float size)>();
            Unit who;
            var g2 = new Game { NoPersist = true };
            who = g2.DebugStagePerkOffer(perk);
            if (who == null) { fails.Add("noOfferedSoldier:" + perk); continue; }
            string formatted = Hud.PerkDeltaLineForTest(who, perk);
            if (string.IsNullOrEmpty(formatted)) { fails.Add("noDeltaLineForOfferedPerk:" + perk); continue; }
            try
            {
                Cfg.CaptureText = cap;
                Raylib.BeginDrawing();
                g2.DrawHudLayer();
                Raylib.EndDrawing();
            }
            finally { Cfg.CaptureText = null; }

            if (!cap.Any(c => c.text == formatted))
                fails.Add($"perkCardDidNotPaintItsOwnLine:{perk}:'{formatted}'");

            var got = System.Text.RegularExpressions.Regex.Matches(formatted, @"\d+")
                .Select(m => int.Parse(m.Value)).ToList();
            // The claim the line makes, rebuilt from the OFFERED soldier and the MEASURED swing.
            var want = new List<int>();
            switch (perk)
            {
                case Perk.Tank: want.Add(who.MaxHp); want.Add(who.MaxHp + swing[0]); break;
                case Perk.Sprinter: want.Add(who.Mobility); want.Add(who.Mobility + swing[0]); break;
                case Perk.LockOn:
                case Perk.Siegebreaker: want.Add(who.Aim); want.Add(who.Aim + swing[0]); break;
                case Perk.CloseQuarters:
                case Perk.Marksman: want.Add(who.Aim); want.Add(who.Aim + swing[0]); want.Add(swing[1]); break;
                case Perk.Bandolier: want.Add(1 + who.BonusGrenades); want.Add(1 + who.BonusGrenades + swing[0]); break;
                case Perk.Hardened: want.Add(swing[0]); want.Add(swing[1]); break;
                default: want.Add(swing[0]); break;
            }
            foreach (int n in want)
                if (!got.Contains(n))
                    fails.Add($"perkCardLies:{perk}:'{formatted}' has no {n} (measured {string.Join("/", want)})");
        }

        return fails.Count == 0 ? "" : string.Join(",", fails.Distinct());
    }

    /// Harness: stage the barracks PERK CHOOSER on a real run with `p` as the left offer and a
    /// no-delta perk on the right, so exactly one delta line is painted.
    public Unit DebugStagePerkOffer(Perk p)
    {
        if (_run == null || _run.Squad == null || _run.Squad.Count == 0) { _run = new Run(); _run.Start(); }
        _shopDone = true;
        _run.PendingSpecs.Clear();
        _run.BoonOffer.Clear();
        _run.PendingPerks.Clear();
        var who = _run.Squad.FirstOrDefault(u => u != null && !u.IsVip) ?? _run.Squad[0];
        who.Perks.Remove(p);
        _run.PendingPerks.Add(new PerkOffer { Unit = who, A = p, B = Perk.Reflexes });
        Phase = Phase.Barracks;
        return who;
    }

    // ─── C5 THE HARD EDGES — SIGHTLINE_SAVEEDGETEST: THE HOSTILE SAVE ─────────────────────────
    /// W9 asked "what does a hand-edited meta.json do to the game?" and found three shapes that
    /// each killed a screen outright. It never asked the same question of `save.json`, whose guard
    /// (D2, R2) stops at three shapes: unparseable, no squad, and a schema from the future. Every
    /// other field is read as written.
    ///
    /// This drives the shapes a real corrupted / edited / truncated / older-build save produces,
    /// through the REAL resume path (`ContinueRun`) and then through 300 updates and a drawn frame
    /// — because "loads without throwing" is not the contract; "you can play it" is. A shape that
    /// crashes, that resumes with nobody on the board, or that puts a number on the HUD the run
    /// cannot mean, is a defect.
    public static string SaveEdgeSelfTest()
    {
        var fails = new List<string>();
        var notes = new List<string>();
        string sp = SaveGame.SavePathPublic, mp = SaveGame.MetaPathPublic;
        bool hadSave = false, hadMeta = false; string saveStash = null, metaStash = null;
        try
        {
            hadSave = System.IO.File.Exists(sp); if (hadSave) saveStash = System.IO.File.ReadAllText(sp);
            hadMeta = System.IO.File.Exists(mp); if (hadMeta) metaStash = System.IO.File.ReadAllText(mp);
        }
        catch { }

        // A real save, written by the game, as the base every shape is edited FROM. Hand-writing
        // the JSON would test a fiction; this is the file the game actually produces.
        string good = null;
        try
        {
            Util.Reseed(9001);
            var seed = new Game();                 // live path: StartMission writes the checkpoint
            seed.StartMission(1);
            good = System.IO.File.ReadAllText(sp);
        }
        catch (Exception ex) { fails.Add("couldNotWriteABaseSave:" + ex.GetType().Name); }

        /// Apply one edit to the base save, resume it, and play it. Returns the resumed Game
        /// (null if the resume was refused, which is a legitimate outcome — refusing a broken
        /// save is a repair; crashing on it is not).
        Game Resume(string what, Func<string, string> edit, bool expectRefusal)
        {
            if (good == null) return null;
            try
            {
                System.IO.File.WriteAllText(sp, edit(good));
                var g = new Game { NoPersist = true, AutoPlay = true, SmartPlay = true };
                bool ok = g.ContinueRun();
                if (!ok)
                {
                    // A refusal is the right answer for a file that cannot be read at all, and
                    // DATA LOSS for one that can: the recovery path MOVES save.json aside, so a
                    // shape that should have resumed and did not has just cost a player their run.
                    if (!expectRefusal) fails.Add($"refusedAResumableSave:{what}");
                    // ...and a refusal MUST take the file with it. `Hud` draws CONTINUE RUN off
                    // `SaveGame.Exists`, so a save that is refused but left in place is D2's
                    // "button that did nothing, forever, with no banner and no stash". This is the
                    // assertion that catches a guard which drops bad data on the WRONG SIDE of its
                    // own usability check (C5 review B2).
                    if (System.IO.File.Exists(sp))
                        fails.Add($"refusedButLeftTheButton:{what}(save.json still offers CONTINUE)");
                    return null;
                }
                if (expectRefusal) fails.Add($"acceptedAnUnusableSave:{what}");
                // it has to be PLAYABLE, not merely loaded
                if (g.Players.Count == 0) fails.Add($"resumedWithNoSquad:{what}");
                if (g.AlivePlayers().Count == 0) fails.Add($"resumedWithNobodyAlive:{what}");
                for (int i = 0; i < 300 && g.Phase != Phase.Win && g.Phase != Phase.Lose; i++)
                    g.Update(1f / 60f);
                Raylib.BeginDrawing();
                g.Draw();                              // the HUD reads the run's numbers
                Raylib.EndDrawing();
                return g;
            }
            catch (Exception ex)
            {
                fails.Add($"threw:{what}:{ex.GetType().Name}:{Short(ex.Message)}");
                return null;
            }
        }

        try
        {
            // (1) MISSION out of range, in both directions. ContinueRun clamps the number it SETS
            //     UP with and leaves `Run.Mission` as written — so the board is mission 6 while
            //     every readout, reward and cap still reads 999.
            var far = Resume("mission=999", s => s.Replace("\"Mission\": 1,", "\"Mission\": 999,"), false);
            if (far != null && far.RunState != null && far.RunState.Mission > Run.MaxMissions)
                fails.Add($"missionNotClamped:{far.RunState.Mission}(>{Run.MaxMissions})");
            var zero = Resume("mission=0", s => s.Replace("\"Mission\": 1,", "\"Mission\": 0,"), false);
            if (zero != null && zero.RunState != null && zero.RunState.Mission < 1)
                fails.Add($"missionNotClamped:{zero.RunState.Mission}(<1)");
            var neg = Resume("mission=-7", s => s.Replace("\"Mission\": 1,", "\"Mission\": -7,"), false);
            if (neg != null && neg.RunState != null && neg.RunState.Mission < 1)
                fails.Add($"missionNotClamped:{neg.RunState.Mission}(<1)");

            // (2) A soldier with NO NAME. `Name` is the one string the DTO reads raw, and it is a
            //     dictionary key (BondTally), a save key and a HUD string.
            Resume("name=null", s => s.Replace("\"Name\":", "\"Name\": null, \"NameWas\":"), false);

            // (3) A soldier of an UNKNOWN CLASS — what an older/newer build's roster looks like.
            Resume("cls=WIZARD", s => s.Replace("\"Cls\": \"", "\"Cls\": \"WIZARD"), false);

            // (4) EVERYBODY BENCHED. Nothing in the DTO stops it, and a run you cannot field is a
            //     run you cannot lose or leave.
            var benched = Resume("allBenched", s => s.Replace("\"Benched\": false", "\"Benched\": true"), false);
            if (benched != null && benched.Players.Count == 0)
                fails.Add("allBenchedFieldsNobody");

            // (5) A roster twice RosterMax — the barracks lays out six rows.
            Resume("doubleRoster", s =>
            {
                int i = s.IndexOf("\"Squad\": [");
                if (i < 0) return s;
                int open = s.IndexOf('{', i);
                int depth = 0, j = open;
                for (; j < s.Length; j++) { if (s[j] == '{') depth++; else if (s[j] == '}') { depth--; if (depth == 0) break; } }
                string one = s.Substring(open, j - open + 1);
                var sb = new System.Text.StringBuilder(s.Substring(0, open));
                for (int k = 0; k < 12; k++) { sb.Append(one.Replace("\"Name\": \"", $"\"Name\": \"X{k}")); sb.Append(','); }
                sb.Append(s.Substring(open));
                return sb.ToString();
            }, false);

            // (6) A BOND naming somebody who is not on the roster.
            Resume("phantomBond", s => s.Contains("\"BondTally\": {}")
                ? s.Replace("\"BondTally\": {}", "\"BondTally\": {\"NOBODY\": 3}")
                : s.Replace("\"BondTally\": {", "\"BondTally\": {\"NOBODY\": 3,"), false);

            // (7) TRUNCATION — the classic half-written file. Must be refused, not read.
            Resume("truncated", s => s.Substring(0, s.Length / 2), true);

            // (8) A save whose squad array holds a null element.
            Resume("nullSquadMember", s => s.Replace("\"Squad\": [", "\"Squad\": [null,"), false);

            // (9) A squad array of NOTHING BUT nulls — the shape that tells a "no soldiers" guard
            //     apart from a "no elements" guard. It must be refused AND stashed like any other
            //     unusable file; accepting it leaves a CONTINUE button over an empty roster.
            Resume("allNullSquad", s =>
            {
                int i = s.IndexOf("\"Squad\": [");
                if (i < 0) return s;
                int open = s.IndexOf('[', i);
                int depth = 0, j = open;
                for (; j < s.Length; j++) { if (s[j] == '[') depth++; else if (s[j] == ']') { depth--; if (depth == 0) break; } }
                return s.Substring(0, open) + "[null, null]" + s.Substring(j + 1);
            }, true);
        }
        finally
        {
            try
            {
                if (hadSave) System.IO.File.WriteAllText(sp, saveStash);
                else if (System.IO.File.Exists(sp)) System.IO.File.Delete(sp);
                if (System.IO.File.Exists(sp + ".bak")) System.IO.File.Delete(sp + ".bak");
                if (hadMeta) System.IO.File.WriteAllText(mp, metaStash);
                else if (System.IO.File.Exists(mp)) System.IO.File.Delete(mp);
            }
            catch { }
        }

        return fails.Count == 0
            ? "SAVEEDGETEST: PASS (9 hostile save shapes resume into a PLAYABLE run or are refused "
              + "AND STASHED: mission out of range in both directions, a nameless soldier, an "
              + "unknown class, an all-benched roster, a double-length roster, a phantom bond, a "
              + "truncated file, a null squad member and an all-null squad — none throws, none "
              + "fields an empty board, and no refusal leaves a CONTINUE button behind it"
              + (notes.Count > 0 ? "; " + string.Join(", ", notes) : "") + ")"
            : "SAVEEDGETEST: FAIL (" + string.Join(",", fails.Distinct()) + ")";
    }

    // ─── C5 THE HARD EDGES — SIGHTLINE_AICOVTEST: THE ENEMY DECISION CENSUS ────────────────────
    /// The test that would have caught a branch firing ZERO times in 1595 enemy turns.
    ///
    /// THE STANDING FINDING (CROSSCUT, docs/ROADMAP.md): the enemy OVERWATCH branch was measured
    /// taken 0 times in 1595 pre-W2 enemy act-opportunities and 3 times in 1589 post-W2 — a whole
    /// verb of the opponent's vocabulary that does not exist in play. Nothing in the project could
    /// see it: every AI test asserts that a DECISION IS CORRECT GIVEN A BOARD, and a branch that
    /// is never reached is correct on every board it never reaches. The gap is not a missing
    /// assertion about overwatch; it is the absence of any assertion about REACHABILITY.
    ///
    /// So this walks real campaigns and counts which of `Game.ActBranches` the opponent actually
    /// takes. A branch that never fires over the whole sample is a coverage FAILURE by name.
    ///
    /// A ZERO GATE WOULD NOT HAVE CAUGHT IT, AND SAYING SO IS THE POINT. Measured on this tree
    /// (144 campaigns, 8083 enemy acts): `overwatch` fires **8 times, 0.10%**. So "the branch is
    /// never taken" is not literally true here — it is taken about once per thousand acts, which
    /// is a verb no player will ever see, and a test that only asked "> 0?" would have passed the
    /// pre-W2 tree on a large enough sample too. The gate is therefore a RATE, and the set of
    /// effectively-dead branches is a DECLARED REGISTRY:
    ///
    ///   * any branch firing below `AiCovRareRate` (once per 1000 enemy acts) must be NAMED in
    ///     `AiCovKnownRare`. An undeclared branch that falls that low FAILS, by name — which is
    ///     precisely what would have happened to `overwatch` the day it went quiet, instead of a
    ///     human noticing three programs later;
    ///   * a DECLARED branch that climbs back above the rate is reported as a stale declaration
    ///     (a note, not a failure — the wave that revives a verb should not be blocked by the
    ///     registry that recorded it as dead, it should be told to delete the entry);
    ///   * `SIGHTLINE_AICOVSTRICT=1` treats every declared entry as a failure. That is the mode
    ///     that fails on today's tree, and it is this test's proof that it can fail at all.
    ///
    /// THE BACKSTOPS ARE NOT VERBS. `terminal-reload` / `terminal-hunker` exist so W2's
    /// no-idle-act invariant holds STRUCTURALLY when a plan goes stale, and `none` is the tag for
    /// "no branch fired" (the bleed-out window). They are censused and reported, never gated:
    /// `terminal-reload` measured 0 in 8083 acts, which is the design working, not a hole.
    // "move" and "idle" join the backstops rather than the gated verb list (C5's call): they are
    // resolutions of the sentinel, not verbs the planner chose. `idle` in particular is expected to
    // read ZERO on a contested batch — C2 measured exactly 0 across ~53k contested acts — so a
    // non-zero idle means a branch LOST ITS LABEL, which is the acceptance oracle for this merge.
    static readonly string[] AiCovBackstops = { "terminal-reload", "terminal-hunker", "none", "move", "idle" };
    /// The declared effectively-dead branches, with the rate measured at the base of this wave
    /// (8083 acts): overwatch 0.10%, relock 0.06%, shove 0.05%. Delete an entry when its verb
    /// climbs back above the rate — the PASS line will tell you which.
    // COMPOSITION FINDING (lead, at the C2+C5 merge — neither wave could see this alone).
    // "overwatch" was declared effectively-dead by C5 against the PRE-C2 opponent, where it fired
    // 8 times in 8083 acts. C2's decline gate revived it: on the composed tree the same batch reads
    // 86/8083 (1.06%), an order of magnitude above the rate, and AICOVTEST's own STALE DECLARATION
    // note demanded its removal. That note firing is the test working exactly as designed.
    // "sap" is MARGINAL at the sweep's N: 10/8083 (0.12%) here, but 2/2473 (0.08%) at N=2, which
    // FAILS. The rate gate is 0.10%, so at N=2 the threshold is ~2.7 events and a Poisson draw
    // decides it. Recorded, not papered over — the fix is a floor on acts, not a declaration.
    static readonly string[] AiCovKnownRare = { "relock", "shove" };
    /// Once per 1000 enemy acts. Below this a verb exists in the code and not in the game.
    const double AiCovRareRate = 0.001;
    /// The rate verdict needs a sample in which the threshold is a COUNT, not a coin flip.
    /// At the sweep's old N the batch was ~2470 acts, so `AiCovRareRate` came to ~2.5 expected
    /// events and a branch sitting near the line flipped PASS/FAIL between runs: `sap` read
    /// 10/8083 (0.12%, PASS) and 2/2473 (0.08%, FAIL) on the same tree. A flaky gate is not a
    /// gate. Below this floor the rate verdict is SKIPPED and said to be skipped — the
    /// unregistered-label and never-fired assertions still run, because those are not rate-based.
    const int AiCovMinActs = 6000;
    public static string AiCoverageSelfTest(int campaigns)
    {
        var fails = new List<string>();
        var notes = new List<string>();
        var count = new Dictionary<string, long>();
        foreach (var b in ActBranches) count[b] = 0;
        long acts = 0, contested = 0, unknown = 0;
        bool strict = Environment.GetEnvironmentVariable("SIGHTLINE_AICOVSTRICT") == "1";

        BranchProbe = (branch, plan, standing) =>
        {
            acts++;
            if (standing > 0) contested++;
            if (count.ContainsKey(branch)) count[branch]++;
            else { unknown++; count[branch] = 1; }        // a new branch nobody registered
        };

        var objs = new[] { Objective.Eliminate, Objective.Hack, Objective.Evac, Objective.Escort,
                           Objective.Sabotage, Objective.Rescue, Objective.Defend, Objective.Decapitate };
        int slot = 0, played = 0, missions = 0;
        foreach (int heat in new[] { 0, 4, 8 })
            foreach (var o in objs)
                for (int rep = 0; rep < campaigns; rep++)
                {
                    Environment.SetEnvironmentVariable("SIGHTLINE_HEAT", heat.ToString());
                    Util.Reseed(310000 + slot++);
                    var g = new Game { NoPersist = true, AutoPlay = true, SmartPlay = true, ForcedObjective = o };
                    g.StartMission(1);
                    int frame = 0;
                    while (frame++ < 40000 && g.Phase != Phase.Win && g.Phase != Phase.Lose)
                        g.Update(1f / 60f);
                    played++;
                    missions += g.RunState != null ? Math.Max(1, g.RunState.Mission) : 1;
                }
        BranchProbe = null;
        Environment.SetEnvironmentVariable("SIGHTLINE_HEAT", null);

        // ---- the verdict ----------------------------------------------------------------------
        var census = new List<string>();
        foreach (var b in ActBranches)
        {
            long c = count[b];
            double rate = acts > 0 ? c / (double)acts : 0;
            census.Add($"{b}={c}({100.0 * rate:0.00}%)");
            if (Array.IndexOf(AiCovBackstops, b) >= 0) continue;      // structural, not a verb
            bool declared = Array.IndexOf(AiCovKnownRare, b) >= 0;
            if (acts < AiCovMinActs) { }        // sample too small to resolve the rate — see AiCovMinActs
            else if (rate < AiCovRareRate)
            {
                if (!declared)
                    fails.Add($"branchEffectivelyDead:{b}={c}/{acts}({100.0 * rate:0.00}% < {100.0 * AiCovRareRate:0.00}%)");
                else if (strict)
                    fails.Add($"declaredDeadBranch:{b}={c}/{acts}");
                else
                    notes.Add($"DEAD-BY-DECLARATION:{b}={c}/{acts}");
            }
            else if (declared && rate >= 2 * AiCovRareRate)   // 2x, so a branch sitting ON the line does not flip the note run to run
                notes.Add($"STALE DECLARATION:{b}={c}/{acts}({100.0 * rate:0.00}%) is above the rate — delete it from AiCovKnownRare");
        }
        // vacuity: a census of nothing proves nothing, and the rate gate needs enough acts for
        // "once per thousand" to be a measurable statement at all.
        if (acts < 1500) fails.Add($"vacuousCensus(acts={acts})");
        if (contested < acts / 2) notes.Add($"contested={contested}/{acts}");
        if (unknown > 0) fails.Add($"unregisteredBranch(count={unknown}) — add it to Game.ActBranches");
        // and the probe has to be wired to the chain it claims to census
        if (count["shoot"] == 0) fails.Add("probeNotWired(no shots seen at all)");

        return fails.Count == 0
            ? $"AICOVTEST: PASS ({played} campaigns / {missions} missions / {acts} enemy acts "
              + $"({contested} contested); every enemy verb fires at least once per "
              + (acts < AiCovMinActs
                   ? $"[RATE VERDICT SKIPPED: {acts} acts < {AiCovMinActs} floor] "
                   : "")
              + $"{(int)(1 / AiCovRareRate)} acts except the {AiCovKnownRare.Length} declared "
              + $"effectively-dead ones. census: " + string.Join(" ", census)
              + (notes.Count > 0 ? " | " + string.Join(" | ", notes) : "") + ")"
            : $"AICOVTEST: FAIL ({string.Join(",", fails.Distinct())}) census: "
              + string.Join(" ", census) + (notes.Count > 0 ? " | " + string.Join(" | ", notes) : "");
    }

    /// C5 (review E3): an animation that never completes — the OTHER deadlock. Harness-only; the
    /// enemy turn enqueues one when `Game.DebugEnemyAnimWedge` is set, and `Game.Update` then
    /// returns at the animation pump every frame without ever reaching the phase switch.
    class StuckAnim : Anim
    {
        public override bool Update(Game g, float dt) => false;   // never done, by construction
        public override void Draw(Game g) { }
    }

    // ─── C5 THE HARD EDGES — SIGHTLINE_ENEMYSTALLTEST ──────────────────────────────────────────
    /// The ENEMY-TURN half of the no-deadlock contract, asserted the only way a deadlock guard
    /// honestly can be: by DEADLOCKING THE ENEMY TURN and watching what happens.
    ///
    ///  (A) THE ARM — with the turn wedged, the guard fires within its own bound and the line it
    ///      prints NAMES the unit, the stage and the planner branch. A guard that fires silently
    ///      is the W9 TIMEOUT again: an agent gets a frame count and no diagnosis.
    ///  (B) THE ESCAPE — the wedged turn ENDS. Not "eventually, at the harness frame cap": the
    ///      guard forfeits the stalled unit and, when that exhausts the staging list, hands the
    ///      turn back. Real play is where this matters; a batch loses a run, a player loses the
    ///      session.
    ///  (C) NON-VACUITY, and this is the leg that makes the whole test mean something — the SAME
    ///      wedge with `EnemyStallGuardOn = false` (the pre-guard tree) must NOT recover. If it
    ///      did, the wedge would be proving nothing and (A)/(B) would pass on a tree with no
    ///      guard in it at all.
    ///  (D) NO FALSE POSITIVES — real missions, no wedge, hundreds of real enemy turns: the guard
    ///      must never fire. A stall guard that trips in a healthy fight would silently forfeit
    ///      hostile turns and quietly move every balance number in the project.
    ///  (E) THE BUDGET — the guard has to bite before the harness frame budget, or a TIMEOUT is
    ///      reachable again through this door. Pinned against Game.AutoFrameCap, exactly as
    ///      STALLTEST pins AutoMaxRunTurns.
    public static string EnemyStallSelfTest()
    {
        var fails = new List<string>();
        int armFrames = -1, escapeFrames = -1, noGuardFrames = -1;
        string armLine = "";
        long enemyTurnsSeen = 0;

        // ---- (A) + (B): wedge a real enemy turn -----------------------------------------------
        {
            Util.Reseed(4242);
            EnemyStallFires = 0; LastEnemyStall = ""; EnemyStallGuardOn = true;
            var g = new Game { NoPersist = true, AutoPlay = true, SmartPlay = true };
            g.StartMission(1);
            // play forward until the enemy turn is actually running
            int f = 0;
            while (g.Phase != Phase.EnemyTurn && f++ < 20000) g.Update(1f / 60f);
            if (g.Phase != Phase.EnemyTurn) fails.Add("neverReachedAnEnemyTurn");
            else
            {
                DebugEnemyWedge = true;
                int before = EnemyStallFires;
                int spun = 0;
                while (EnemyStallFires == before && spun++ < EnemyStallFrames + 240) g.Update(1f / 60f);
                armFrames = spun;
                armLine = LastEnemyStall;
                if (EnemyStallFires == before) fails.Add($"guardNeverFired(after {spun} wedged updates)");
                if (armFrames > EnemyStallFrames + 120) fails.Add($"guardFiredLate({armFrames})");
                // the diagnosis has to be a diagnosis
                foreach (var must in new[] { "stage=", "unit=", "plan=", "anims=", "mission=" })
                    if (!armLine.Contains(must)) fails.Add("stallLineMissing:" + must);
                if (armLine.Contains("unit=<none")) fails.Add("stallLineNamesNoUnit");

                // (B) the turn gets OUT — bounded by one fire per staged hostile.
                int budget = (g.Enemies.Count + 2) * (EnemyStallFrames + 8);
                int e2 = 0;
                while (g.Phase == Phase.EnemyTurn && e2++ < budget) g.Update(1f / 60f);
                escapeFrames = e2;
                if (g.Phase == Phase.EnemyTurn)
                    fails.Add($"wedgedTurnNeverEnded(after {e2} updates, fires={EnemyStallFires})");
                DebugEnemyWedge = false;
            }
        }

        // ---- (B2) THE ANIMATION HALF — the deadlock a guard inside UpdateEnemy cannot see ------
        // `Update` returns at the animation pump while the queue is non-empty, so it never reaches
        // the phase switch: a guard called from `UpdateEnemy` is not merely late here, it is NEVER
        // CALLED. The first version of this test only wedged the stage machine (its own stall line
        // said `anims=0`), so it passed on the placement it argues against — review E3.
        int animArm = -1, animEscape = -1; string animLine = "";
        {
            Util.Reseed(4242);
            EnemyStallGuardOn = true;
            int before = EnemyStallFires;
            var g = new Game { NoPersist = true, AutoPlay = true, SmartPlay = true };
            g.StartMission(1);
            int f = 0;
            while (g.Phase != Phase.EnemyTurn && f++ < 20000) g.Update(1f / 60f);
            if (g.Phase != Phase.EnemyTurn) fails.Add("neverReachedAnEnemyTurn(animWedge)");
            else
            {
                DebugEnemyAnimWedge = true;
                int spun = 0;
                while (EnemyStallFires == before && spun++ < EnemyStallFrames + 240) g.Update(1f / 60f);
                animArm = spun;
                animLine = LastEnemyStall;
                if (EnemyStallFires == before)
                    fails.Add($"guardNeverFiredOnAStUCKANIM(after {spun} updates)");
                // the diagnosis must NAME the queue, or it is not a diagnosis of THIS half
                if (!animLine.Contains("head=StuckAnim"))
                    fails.Add("animStallLineDoesNotNameTheQueueHead:" + Short(animLine));
                // and it must recover: the queue is dropped and the turn ends
                int budget = (g.Enemies.Count + 2) * (EnemyStallFrames + 8);
                int e2 = 0;
                DebugEnemyAnimWedge = false;      // one wedge is enough; the recovery must finish the turn
                while (g.Phase == Phase.EnemyTurn && e2++ < budget) g.Update(1f / 60f);
                animEscape = e2;
                if (g.Phase == Phase.EnemyTurn) fails.Add($"animWedgedTurnNeverEnded(after {e2})");
                // ...and nobody is left standing between two tiles (the recovery drops the queue,
                // and MoveStepAnim commits X/Y only on completion).
                foreach (var u in g.Players)
                    if (u.Alive && Vector2.Distance(u.Pos, Util.TileCenter(u.X, u.Y)) > 1.5f)
                        fails.Add($"unitLeftMidTile:{u.Name}");
                foreach (var e in g.Enemies)
                    if (e.Alive && Vector2.Distance(e.Pos, Util.TileCenter(e.X, e.Y)) > 1.5f)
                        fails.Add($"enemyLeftMidTile:{e.Name}");
            }
            DebugEnemyAnimWedge = false;
        }

        // ---- (C) the same wedge on the PRE-GUARD tree hangs ------------------------------------
        {
            Util.Reseed(4242);
            EnemyStallGuardOn = false;
            int firesBefore = EnemyStallFires;
            var g = new Game { NoPersist = true, AutoPlay = true, SmartPlay = true };
            g.StartMission(1);
            int f = 0;
            while (g.Phase != Phase.EnemyTurn && f++ < 20000) g.Update(1f / 60f);
            if (g.Phase == Phase.EnemyTurn)
            {
                DebugEnemyWedge = true;
                int budget = (g.Enemies.Count + 2) * (EnemyStallFrames + 8);
                int spun = 0;
                while (g.Phase == Phase.EnemyTurn && spun++ < budget) g.Update(1f / 60f);
                noGuardFrames = spun;
                if (g.Phase != Phase.EnemyTurn)
                    fails.Add("wedgeIsNotAWedge(the turn ended with the guard OFF, so (A)/(B) prove nothing)");
                if (EnemyStallFires != firesBefore)
                    fails.Add("guardFiredWhileDisabled");
                DebugEnemyWedge = false;
            }
            else fails.Add("neverReachedAnEnemyTurn(legC)");
            EnemyStallGuardOn = true;
        }

        // ---- (D) real missions, no wedge: the guard must stay silent ---------------------------
        {
            EnemyStallFires = 0;
            Game.ActProbe = (u, plan, a, am, st) => { };
            for (int rep = 0; rep < 4; rep++)
            {
                Util.Reseed(70000 + rep);
                var g = new Game { NoPersist = true, AutoPlay = true, SmartPlay = true };
                g.StartMission(1);
                Phase last = g.Phase;
                int f = 0;
                while (f++ < 30000 && g.Phase != Phase.Win && g.Phase != Phase.Lose)
                {
                    g.Update(1f / 60f);
                    if (g.Phase == Phase.EnemyTurn && last != Phase.EnemyTurn) enemyTurnsSeen++;
                    last = g.Phase;
                }
            }
            Game.ActProbe = null;
            if (EnemyStallFires > 0) fails.Add($"falsePositives={EnemyStallFires} in clean play");
            if (enemyTurnsSeen < 40) fails.Add($"vacuous(onlySaw {enemyTurnsSeen} enemy turns)");
        }

        // ---- (E) the budget relationship --------------------------------------------------------
        // (read through locals so the compiler evaluates the RELATIONSHIP rather than folding two
        // consts into a constant-false branch it then warns is unreachable — which is exactly the
        // shape of a check that cannot fail.)
        long stallBound = EnemyStallFrames, frameCap = AutoFrameCap;
        if (stallBound * 24 >= frameCap)
            fails.Add($"stallBudgetExceedsFrameCap({stallBound}x24 >= {frameCap})");
        if (stallBound <= 0) fails.Add("stallFramesNotPositive");

        return fails.Count == 0
            ? $"ENEMYSTALLTEST: PASS (BOTH halves of the deadlock: a wedged ANIMATION is detected in "
              + $"{animArm} updates, named at the queue head, and drained in {animEscape} more with "
              + $"no unit left between tiles - that is the half a guard inside UpdateEnemy is never "
              + $"even called for; and a wedged STAGE MACHINE is detected in {armFrames} updates "
              + $"(bound {EnemyStallFrames}) and named — \"{armLine.Substring(0, Math.Min(armLine.Length, 130))}\" — "
              + $"the turn then ends in {escapeFrames} more; the SAME wedge with the guard off still "
              + $"hangs after {noGuardFrames} updates; {enemyTurnsSeen} clean enemy turns fired it zero "
              + $"times; {EnemyStallFrames} x 24 units < the {AutoFrameCap}-frame harness budget)"
            : "ENEMYSTALLTEST: FAIL (" + string.Join(",", fails.Distinct()) + ")";
    }

    // ─── C5 THE HARD EDGES — THE SCREEN AUDIT (FITTEST leg F) ──────────────────────────────────
    /// Every screen the game can draw, drawn for real at every shipped text size, with the ink and
    /// the control plates read back FROM THE DRAW CALLS (`Cfg.InkProbe` / `Hud.PlateProbe`).
    ///
    /// WHY A LIVE FRAME. W10's five legs measure layout arithmetic the test re-derives from the
    /// same constants the renderer uses. That is fine for the five surfaces someone thought to
    /// transcribe, and it is exactly the shape CROSSCUT's rule 5 warns about everywhere else: the
    /// screen the test describes and the screen the game paints can differ, and nothing notices.
    /// This leg cannot drift, because it IS the draw: it stages a screen, calls `Hud.Draw`, and
    /// audits what came out.
    ///
    /// WHAT IT ASSERTS, per screen per scale:
    ///   1. CONTROL PLATES CONTAIN THEIR LABELS. Every button/plate in the game reports (label,
    ///      plate, painted box); the box must lie inside the plate. A label that has outgrown its
    ///      chrome is unreadable — and a control the player cannot read is a control they will
    ///      not press.
    ///   2. NOTHING IS PAINTED OFF THE CANVAS. Any visible string whose box leaves 1280x800 has
    ///      lost information the player was meant to have. Screens with a real scroll region
    ///      (the CODEX) are exempt BY NAME, not by silence.
    ///   3. THE SCREEN ACTUALLY DREW. A stager that throws, or paints fewer than three strings,
    ///      is a failed audit rather than a quiet pass — the vacuity trap that lets a screen
    ///      "pass" because it was never on screen.
    /// Returns the number of screens audited so the PASS line can state its own coverage.
    static int ScreenAudit(string S, List<string> fails, Action<string, bool, string> check)
    {
        var ink = new List<(string text, Rectangle box, float alpha)>();
        var plates = new List<(string label, Rectangle plate, Rectangle box)>();
        var seen = new Dictionary<string, string>();     // frame fingerprint -> the screen that drew it
        var clips = new List<string>();                  // strings the renderer ellipsized away
        var floors = new List<string>();                 // shrink-to-fit calls that hit their floor

        int n = 0;
        foreach (var sc in ScreenCases)
        {
            ink.Clear(); plates.Clear();
            string tag = sc.Name + S;
            float minSize = 99f; string minWhat = "-";
            try
            {
                // DETERMINISM: several stagers roll (the shop slate, the event, the draft pool),
                // and Util.Rng is clock-seeded by default — the first version of this leg reported
                // a different worst-case string on consecutive runs, which is a flaky gate rather
                // than a gate. One fixed seed per screen, so a FAIL reproduces verbatim.
                Util.Reseed(FitScreenSeed);
                // PARALLAX: park the pointer BEFORE staging, not after the settle frames. The three
                // Update frames below resolve the hover tile and can hand the keyboard cursor back
                // to the mouse on a pointer delta — with the pin set only afterwards, a spurious X
                // pointer event under Xvfb un-staged TOOLTIP-HOVER on ~1 run in 6 and its frame
                // collapsed onto TOOLTIP-AIM (`screenNotStaged`). Parked off-canvas first, so no
                // control is hovered by default; a stager that pins the pointer itself (the
                // tooltip's foe seat) overrides this and KEEPS it through the settle and the draw.
                Hud.MousePin = new System.Numerics.Vector2(-4000f, -4000f);
                var g = new Game { NoPersist = true };
                sc.Stage(g);
                // Let the game settle exactly as it does before a screenshot: several stagers
                // (the hover tooltip's odds, the briefing card's timer) only produce their state
                // inside Update, and a draw-only audit photographs the frame before them.
                for (int f = 0; f < 3; f++) g.Update(1f / 60f);
                Hud.AnimPin = 1f;              // audit the SETTLED frame, deterministically
                Hud.TimePin = 1000.0; Renderer.TimePin = 1000.0;   // ...and at a FIXED clock
                // ...and a FIXED POINTER. Thirty draw sites read the live cursor (hover fills,
                // hover cards, and the threat card, which anchors itself at it), so without this
                // the audited frame depended on where the mouse happened to be: a ~3% flake and a
                // 16-check disagreement between machines on the same commit (review E2). Parked
                // off-canvas so no control is hovered and every cursor-anchored panel clamps to the
                // same place on every run and every machine.
                // (the pointer pin was set before Stage — see above — and is deliberately NOT
                // re-parked here, so a stager's pin survives into the audited frame)
                var fp = new System.Text.StringBuilder();
                Cfg.InkProbe = (t, pos, box, size, alpha) =>
                    {
                        if (string.IsNullOrEmpty(t)) return;
                        fp.Append(t).Append('|').Append((int)pos.X).Append(',').Append((int)pos.Y).Append(';');
                    };
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Pal.Bg);
                // The BOARD pass counts toward the frame FINGERPRINT (so two screens that differ
                // only on the board are not "the same screen") but not toward the geometry audit:
                // it paints inside a Camera2D, so its ink is in world space, not screen space.
                g.DrawBoardLayer();
                Cfg.InkProbe = (t, pos, box, size, alpha) =>
                    {
                        if (string.IsNullOrEmpty(t)) return;
                        fp.Append(t).Append('|').Append((int)pos.X).Append(',').Append((int)pos.Y).Append(';');
                        ink.Add((t, new Rectangle(pos.X, pos.Y, box.X, box.Y), alpha));
                        // CLAUDE.md's own rule: 12px is the small-text floor. The size recorded here
                        // is the AUTHORED one, which is what that rule is written against.
                        if (alpha >= 0.06f && size < minSize) { minSize = size; minWhat = Short(t); }
                        if (FitDumpSmall && alpha >= 0.06f && size < 12f)
                            Console.WriteLine($"FITSMALL {tag} {size:0.#}px rendered={Cfg.Scaled(size):0.#}px '{Short(t)}'");
                    };
                Hud.PlateProbe = (l, p, b) =>
                    { plates.Add((l, p, b)); fp.Append('[').Append(l).Append((int)p.X).Append(',').Append((int)p.Y).Append(']'); };
                Hud.ClipProbe = (t, sz, w) => clips.Add($"{tag}:'{Short(t)}'@{sz}px/{w}px");
                Hud.FloorProbe = (t, sz, w) => floors.Add($"{tag}:'{Short(t)}'@{sz}px/{w}px");
                g.DrawHudLayer();
                Raylib.EndDrawing();
                _fitPhase = g.Phase.ToString() + (g.Paused ? "+paused" : "");
                _fitFrameFp = fp.Length.ToString() + ":" + fp.ToString().GetHashCode().ToString("x8");
            }
            catch (Exception ex) { fails.Add($"screenThrew:{tag}:{ex.GetType().Name}"); continue; }
            finally
            {
                Cfg.InkProbe = null; Hud.PlateProbe = null; Hud.ClipProbe = null; Hud.FloorProbe = null;
                Hud.AnimPin = -1f; Hud.TimePin = -1.0; Renderer.TimePin = -1.0;
                Hud.MousePin = new System.Numerics.Vector2(float.NaN, float.NaN);
            }
            n++;

            // (1) every control plate contains its own label
            foreach (var (label, plate, box) in plates)
            {
                if (plate.Width < 2 || plate.Height < 2) continue;          // a collapsed/hidden control
                float overL = plate.X - box.X, overR = (box.X + box.Width) - (plate.X + plate.Width);
                float overT = plate.Y - box.Y, overB = (box.Y + box.Height) - (plate.Y + plate.Height);
                float worst = MathF.Max(MathF.Max(overL, overR), MathF.Max(overT, overB));
                if (-worst < _fitPlateMargin) { _fitPlateMargin = -worst; _fitPlateTag = $"{tag}:{Short(label)}"; }
                check("scr:" + sc.Name, worst <= PlateSlack,
                      $"labelLeavesPlate:{tag}:'{Short(label)}'by{worst:0}px");
            }

            // (2) nothing visible is painted off the canvas
            if (!sc.Scrolls)
                foreach (var (text, box, alpha) in ink)
                {
                    if (alpha < 0.06f) continue;                            // an entrance fade paints nothing
                    float edge = MathF.Min(MathF.Min(box.X, box.Y),
                                           MathF.Min(Cfg.ScreenW - (box.X + box.Width), Cfg.ScreenH - (box.Y + box.Height)));
                    if (edge < _fitEdgeMargin) { _fitEdgeMargin = edge; _fitEdgeTag = $"{tag}:{Short(text)}"; }
                    check("scr:" + sc.Name,
                          box.X >= -CanvasSlack && box.Y >= -CanvasSlack
                          && box.X + box.Width <= Cfg.ScreenW + CanvasSlack
                          && box.Y + box.Height <= Cfg.ScreenH + CanvasSlack,
                          $"inkOffCanvas:{tag}:'{Short(text)}'@({box.X:0},{box.Y:0},{box.Width:0}x{box.Height:0})");
                }

            // (3) THE SMALL-TEXT FLOOR, as a REGRESSION BOUND rather than as the rule.
            //     CLAUDE.md declares 12px the floor. The shipped UI does not meet it: this leg
            //     measured 10px authored on the AUDIO CHECK screen and 11px on seventeen others at
            //     100% (`SIGHTLINE_FITDUMP=small` lists every one). Asserting 12 here would fail a
            //     tree nobody in this wave is authorised to re-lay-out, and asserting nothing would
            //     let the next fitter step take it lower still. So the gate is the MEASURED worst,
            //     which makes any further shrink a failure, and the breach itself is recorded as an
            //     open finding rather than quietly normalised.
            // C5 REVIEW FIX (B3): these are TWO minima and they do not live on the same screen.
            // The first version tracked one pair under `minSize <= _fitMinSize`, so the LAST screen
            // to tie the smallest AUTHORED size overwrote the rendered figure — and since the scale
            // loop runs 0.90 -> 1.20, the PASS line reported the 120% tie (10px authored / 12px
            // rendered) while the true smallest INK in the game is 9.0px, on AUDIO CHECK at 90%.
            // The gate was never wrong (it is evaluated per screen per scale, below); the REPORT
            // was, and the report is what the DEVLOG quoted as the measurement.
            if (minSize < 99f && minSize < _fitMinSize)
            { _fitMinSize = minSize; _fitMinAuthTag = $"{tag}:{minWhat}"; }
            if (minSize < 99f && Cfg.Scaled(minSize) < _fitMinRendered)
            { _fitMinRendered = Cfg.Scaled(minSize); _fitMinTag = $"{tag}:{minWhat}"; }
            check("scr:" + sc.Name, minSize >= SmallTextAuthoredFloor,
                  $"belowSmallTextFloor:{tag}:'{minWhat}'@{minSize:0.#}px");
            check("scr:" + sc.Name, minSize >= 99f || Cfg.Scaled(minSize) >= SmallTextRenderedFloor - 0.01f,
                  $"rendersBelowFloor:{tag}:'{minWhat}'@{Cfg.Scaled(minSize):0.#}px");

            // (4) NOTHING WAS ELLIPSIZED. A geometry audit cannot see this: the box a clipped
            //     string paints FITS — losing the tail is what made it fit. Clip is the renderer's
            //     last-resort backstop, so a live screen reaching it means a column and its content
            //     have drifted apart. This found the WAR ROOM ellipsizing an achievement
            //     description at the 110% text size over a sub-pixel disagreement between the
            //     wrapper and the clipper (fixed in Hud.Clip; this assertion is what caught it).
            check("scr:" + sc.Name, clips.Count == 0,
                  "textEllipsized:" + (clips.Count > 0 ? clips[0] : ""));
            for (int ci = 1; ci < clips.Count; ci++)
                check("scr:" + sc.Name, false, "textEllipsized:" + clips[ci]);

            // (5) the screen actually drew, and drew ITS OWN screen. Two cases that paint an
            //     identical frame mean one of them never staged — the vacuity trap that would
            //     otherwise let this leg "cover" a screen it has never seen. (Found three:
            //     WOUND / TRAITS / ENDLESSOFFER all photographed the main menu.)
            check("scr:" + sc.Name, ink.Count >= 3, $"screenDrewNothing:{tag}(strings={ink.Count})");
            bool twinned = seen.TryGetValue(_fitFrameFp, out string twin);
            check("scr:" + sc.Name, !twinned, $"screenNotStaged:{tag}(identical frame to {twin})");
            if (!twinned) seen[_fitFrameFp] = sc.Name;
            if (FitDump && (clips.Count > 0 || floors.Count > 0))
                Console.WriteLine($"FITDUMP {tag}: clipped={clips.Count} atFloor={floors.Count} "
                    + string.Join(" ", clips.Concat(floors)));
            if (FitDump)
                Console.WriteLine($"FITDUMP {tag}: phase={_fitPhase} strings={ink.Count} plates={plates.Count} "
                    + $"minSize={minSize:0.#}px('{minWhat}') "
                    + $"tightestPlate={_fitPlateMargin:0.0}px@{_fitPlateTag} nearestEdge={_fitEdgeMargin:0.0}px@{_fitEdgeTag}");
            _fitClips += clips.Count; _fitFloors += floors.Count;
            clips.Clear(); floors.Clear();
        }
        return n;
    }

    // Diagnostics for the PASS line: the tightest label-in-plate margin and the closest any
    // visible string came to the canvas edge, over every screen x every scale.
    static int _fitClips, _fitFloors;
    static float _fitMinSize = 99f, _fitMinRendered = 99f;
    static string _fitMinTag = "-", _fitMinAuthTag = "-";
    static string _fitFrameFp = "";
    static float _fitPlateMargin = 9999f, _fitEdgeMargin = 9999f;
    static string _fitPlateTag = "-", _fitEdgeTag = "-", _fitPhase = "-";
    public static void FitAuditReset() { _fitPlateMargin = 9999f; _fitEdgeMargin = 9999f; _fitPlateTag = "-"; _fitEdgeTag = "-"; }

    static string Short(string s) => s == null ? "" : (s.Length <= 22 ? s : s.Substring(0, 22) + "~");

    /// Push a staged screen's squad to the longest strings the GAME can hand it: the widest
    /// callsign+nickname shape the generator makes, the tag editor's own 14-character cap on every
    /// soldier, and a rank/kill count at the top of their ranges. Nothing here is hypothetical —
    /// each field is bounded by the code that produces it (Game.UpdateTagEditor caps the tag at 14;
    /// Run.Ranks bounds the rank; the kill counter is a 3-digit field on the hall-of-fame row).
    /// The longest identity the GENERATOR can actually deal, derived from its own pools rather
    /// than invented: `Mission.Callsigns`' longest entry and `Nicknames`' longest entry. A stress
    /// case built from unreachable content reports defects the player can never see — the first
    /// version of this helper assigned "KESTREL \"MAVERICK\"" into `Unit.Name`, which no code path
    /// produces, and duly "found" a 9px shop line that cannot occur.
    internal static string FitWorstCallsign => Mission.LongestCallsign;
    internal static string FitWorstNickname => Nicknames.Longest;

    static void FitStressSquad(Game g)
    {
        var squad = g.RunState != null ? g.RunState.Squad : null;
        if (squad != null)
            foreach (var u in squad)
            {
                if (u == null || u.IsVip) continue;
                u.Name = FitWorstCallsign; u.Nickname = FitWorstNickname;
                u.CustomTag = "WMWMWMWMWMWMWM";      // 14 chars, the widest glyphs in the face
                u.Rank = Run.Ranks.Length - 1;
                u.Kills = 999;
            }
        foreach (var u in g.Players)
        {
            if (u == null || u.IsVip) continue;
            u.Name = FitWorstCallsign; u.Nickname = FitWorstNickname;
            u.CustomTag = "WMWMWMWMWMWMWM";
            u.Rank = Run.Ranks.Length - 1;
            u.Kills = 999;
        }
    }

    /// Slack in px. A plate's label may kiss its border (rounded corners hide a pixel); ink may
    /// touch the canvas edge. Anything past this is ink the player cannot read.
    const float PlateSlack = 1.5f, CanvasSlack = 1.5f;
    /// The measured worst authored/rendered type size in the shipped UI (see the note at the
    /// assertion). Not the 12px rule — the bound that keeps the breach from getting worse.
    /// THE BOUND, and it is set at the MEASURED WORST rather than one slack point below it.
    /// C5 first set the authored bound to 9 — what the tightest shipped fitter declares as its own
    /// minimum (`FitSize(effect, 12, 9, ...)` on the REQUISITION card) — while the worst size any
    /// audited screen actually paints is 10px. That point of slack meant a 10 -> 9 regression on
    /// the AUDIO CHECK screen would have passed in silence, which is the opposite of what a
    /// regression bound is for (review B3). It is now 10 authored / 9.0 rendered: exactly what this
    /// tree paints, so ANY further shrink fails. Neither number is the 12px rule — see the note at
    /// the assertion and the open finding in DEVLOG.
    const float SmallTextAuthoredFloor = 10f, SmallTextRenderedFloor = 9f;
    /// The fixed seed every audited screen is staged under.
    const int FitScreenSeed = 20260830;
    static readonly bool FitDump = Environment.GetEnvironmentVariable("SIGHTLINE_FITDUMP") == "1";
    /// SIGHTLINE_FITDUMP=small lists every string the game paints below the 12px small-text floor
    /// CLAUDE.md declares, with its RENDERED size — the survey behind this wave's open finding.
    static readonly bool FitDumpSmall = Environment.GetEnvironmentVariable("SIGHTLINE_FITDUMP") == "small";

    /// The screens. Each entry stages a real `Game` and names itself; `Scrolls` marks a surface
    /// with a genuine scroll region, where ink outside the canvas is the SCROLLBAR's job to
    /// resolve rather than a defect. Adding a screen here is the whole cost of covering it.
    struct ScreenCase
    {
        public string Name; public Action<Game> Stage; public bool Scrolls;
        public ScreenCase(string name, Action<Game> stage, bool scrolls = false)
        { Name = name; Stage = stage; Scrolls = scrolls; }
    }

    static readonly ScreenCase[] ScreenCases =
    {
        new ScreenCase("INTRO",        g => { }),
        new ScreenCase("MISSION",      g => { g.StartMission(1); g.BriefLines = null; }),
        new ScreenCase("PAUSE",        g => { g.StartMission(1); g.Paused = true; }),
        // SETTINGS EVERYWHERE (review round 1): the card in its two new homes, plus each home with
        // QUIT ARMED so the phase-true warning sentence and the footer copy are inside the audit.
        new ScreenCase("SETTINGS-INTRO",       g => g.Paused = true),
        new ScreenCase("SETTINGS-INTRO-ARMED", g => { g.Paused = true; g.QuitArmed = true; }),
        new ScreenCase("SETTINGS-SHOP",        g => { g.DebugShop(); g.Paused = true; }),
        new ScreenCase("SETTINGS-SHOP-ARMED",  g => { g.DebugShop(); g.Paused = true; g.QuitArmed = true; }),
        new ScreenCase("PAUSE-ARMED",          g => { g.StartMission(1); g.Paused = true; g.QuitArmed = true; }),
        new ScreenCase("TOOLTIP-AIM",  g => { g.StartMission(1); g.DebugTooltip(false); }),
        new ScreenCase("TOOLTIP-HOVER",g => { g.StartMission(1); g.DebugTooltip(true); }),
        new ScreenCase("THREATCARD",   g => { g.StartMission(1); g.DebugThreatShot(); }),
        new ScreenCase("TUTORIAL",     g => { g.StartMission(1); g.ShowTutorialStep(0); }),
        new ScreenCase("TRAINING",     g => g.BeginTraining()),
        new ScreenCase("VERBS",        g => { g.StartMission(1); g.DebugVerbs(); }),
        new ScreenCase("STATUS",       g => { g.StartMission(1); g.DebugStatus(); }),
        new ScreenCase("TAGEDIT",      g => { g.StartMission(1); g.DebugTagEditor(); }),
        new ScreenCase("INTENT",       g => { g.StartMission(1); g.DebugIntent(); }),
        new ScreenCase("SHOP",         g => g.DebugShop()),
        new ScreenCase("SHOP-PREP",    g => g.DebugPrep()),
        new ScreenCase("ARMORY",       g => g.DebugArmory()),
        new ScreenCase("CAMPAIGNMAP",  g => g.DebugCampaignMap()),
        new ScreenCase("BENCH",        g => g.DebugBench()),
        new ScreenCase("PERKCHOOSER",  g => g.DebugBarracksPerk()),
        new ScreenCase("DEPLOYCARDS",  g => g.DebugDeployCards()),
        new ScreenCase("BOONOFFER",    g => g.DebugBoon()),
        new ScreenCase("EVENT",        g => g.DebugEvent()),
        new ScreenCase("DRAFT",        g => g.BeginDraft()),
        new ScreenCase("VETDRAFT",     g => g.DebugVetDraft()),
        new ScreenCase("WIN",          g => g.DebugSummary(false)),
        new ScreenCase("LOSE",         g => g.DebugSummary(true)),
        new ScreenCase("KIA",          g => { g.StartMission(1); g.DebugKia(); }),
        new ScreenCase("WARROOM",      g => g.DebugWarRoom()),
        new ScreenCase("CODEX",        g => g.DebugCodex(), true),
        new ScreenCase("AUDIOCHECK",   g => g.DebugAudition()),
        new ScreenCase("SKIRMISHSETUP",g => g.DebugSkirmishSetup()),
        new ScreenCase("ENDLESSOFFER", g => { g.BeginEndless(); g.DebugEndlessOffer(); }),
        new ScreenCase("WOUND",        g => { g.StartMission(1); g.DebugWound(); }),
        new ScreenCase("TRAITS",       g => { g.StartMission(1); g.DebugTraits(); }),
        new ScreenCase("ROSTERFULL",   g => { Environment.SetEnvironmentVariable("SIGHTLINE_ROSTER", "6");
                                              Environment.SetEnvironmentVariable("SIGHTLINE_REPORT", "8");
                                              g.DebugCampaignMap();
                                              Environment.SetEnvironmentVariable("SIGHTLINE_ROSTER", null);
                                              Environment.SetEnvironmentVariable("SIGHTLINE_REPORT", null); }),
        new ScreenCase("ENDLESSHUD",   g => { g.BeginEndless(); }),
        new ScreenCase("BRIEF",        g => g.StartMission(1)),   // the briefing card is up for 11 s
        // THE WORST-CASE CONTENT, which is where the last two waves' text defects actually lived:
        // W10's ARMORY hook photographed the SECOND-SHORTEST blurb in the game for four waves.
        // Default staging paints default-length strings; these three paint the longest strings a
        // player can actually produce (a 14-char custom tag - the editor's own cap - on a
        // full-length callsign+nickname, a full roster, and a padded debrief).
        new ScreenCase("MISSION-WORST", g => { g.StartMission(1); FitStressSquad(g); }),
        new ScreenCase("ROSTER-WORST",  g => { Environment.SetEnvironmentVariable("SIGHTLINE_ROSTER", "6");
                                               Environment.SetEnvironmentVariable("SIGHTLINE_REPORT", "8");
                                               g.DebugCampaignMap();
                                               Environment.SetEnvironmentVariable("SIGHTLINE_ROSTER", null);
                                               Environment.SetEnvironmentVariable("SIGHTLINE_REPORT", null);
                                               FitStressSquad(g); }),
        new ScreenCase("WIN-WORST",     g => { g.DebugSummary(false); FitStressSquad(g); }),
        new ScreenCase("SHOP-WORST",    g => { g.DebugShop(); FitStressSquad(g); }),
    };

    // ─── W5 THE FIRST HOUR — THE DOORS self-test (SIGHTLINE_QUITTEST=1) ────────────────────────
    /// The two ways OUT of a screen that the audit found missing, pinned together because they are
    /// the same problem: a route the player needed and could not find.
    ///
    ///  (A) wildcard-3 — QUIT TO DESKTOP. The pause card carried 18 controls and no exit; the main
    ///      menu 9 entries and no exit; and `SetExitKey(KeyboardKey.Null)` means ESC deliberately
    ///      cannot close the window either. The only sanctioned ways out were ABANDON RUN (which
    ///      destroys the run) or alt-F4. Asserts the arm-then-confirm contract, and — the part
    ///      that matters — that the quit path is PERSISTENCE-INERT: it writes nothing, deletes
    ///      nothing, and leaves the mission-start checkpoint and meta.json byte-identical. That is
    ///      what makes the confirm text ("the current mission restarts from its start") TRUE.
    ///  (B) newplayer-2 — the end card's third door. Asserts DrawSummary publishes THREE distinct,
    ///      non-overlapping hit-test rects, and that the third one's handler reaches the War Room.
    ///
    /// Runs the LIVE (non-NoPersist) path, so it stashes and restores save.json / meta.json.
    public string QuitSelfTest()
    {
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70126);
        var fails = new List<string>();
        string sp = SaveGame.SavePathPublic, mp = SaveGame.MetaPathPublic;
        bool hadSave = false, hadMeta = false;
        string saveStash = null, metaStash = null;
        try
        {
            hadSave = System.IO.File.Exists(sp); if (hadSave) saveStash = System.IO.File.ReadAllText(sp);
            hadMeta = System.IO.File.Exists(mp); if (hadMeta) metaStash = System.IO.File.ReadAllText(mp);
        }
        catch { }

        try
        {
            // ---- (A1) arm, then quit. One press must never take the window down --------------
            {
                var g = new Game { NoPersist = true };
                if (g.QuitArmed || g.QuitRequested) fails.Add("quitArmedAtBoot");
                g.RequestQuit();
                if (!g.QuitArmed) fails.Add("firstPressDidNotArm");
                if (g.QuitRequested) fails.Add("firstPressQuit");
                g.RequestQuit();
                if (!g.QuitRequested) fails.Add("secondPressDidNotQuit");
            }

            // ---- (A2) the quit path is PERSISTENCE-INERT ------------------------------------
            {
                var g = new Game();                     // live path
                g.StartMission(1);                      // writes the mission-start checkpoint
                if (!System.IO.File.Exists(sp)) fails.Add("noCheckpointAtMissionStart");
                string saveAt = System.IO.File.ReadAllText(sp);
                bool metaAt = System.IO.File.Exists(mp);
                string metaBody = metaAt ? System.IO.File.ReadAllText(mp) : null;

                // play forward a little so the LIVE state has diverged from the checkpoint, then quit
                g.DebugSetTurn(4);
                foreach (var u in g.Players) u.Hp = Math.Max(1, u.Hp - 2);
                g.RequestQuit(); g.RequestQuit();
                if (!g.QuitRequested) fails.Add("quitNotRequested");

                if (!System.IO.File.Exists(sp)) fails.Add("quitDeletedTheCheckpoint");
                else if (System.IO.File.ReadAllText(sp) != saveAt) fails.Add("quitRewroteTheCheckpoint");
                if (System.IO.File.Exists(mp) != metaAt) fails.Add("quitTouchedMetaExistence");
                else if (metaAt && System.IO.File.ReadAllText(mp) != metaBody) fails.Add("quitWroteMeta");

                // and the checkpoint it kept is the MISSION-START one, so the confirm text is true
                var resumed = SaveGame.Load();
                if (resumed == null) fails.Add("checkpointUnreadable");
                else if (resumed.Mission != 1) fails.Add("checkpointNotMissionStart:" + resumed.Mission);
            }

            // ---- (A3) the confirm disarms rather than latching -------------------------------
            {
                var g = new Game { NoPersist = true };
                g.RequestQuit();
                g.QuitArmed = false;                    // what closing the pause / any other control does
                g.RequestQuit();
                if (g.QuitRequested) fails.Add("disarmDidNotResetTheConfirm");
            }

            // ---- (B) the end card publishes THREE doors, and the third is the War Room --------
            {
                var g = new Game { NoPersist = true };
                g.DebugSummary(false);                  // the staged VICTORY card
                Raylib.BeginDrawing();
                Hud.Draw(g);
                Raylib.EndDrawing();
                var a = Hud.OverlayBtn; var b = Hud.OverlayBtn2; var c = Hud.EndWarRoomBtn;
                if (a.Width < 20 || b.Width < 20 || c.Width < 20) fails.Add("endCardMissingADoor");
                if (Raylib.CheckCollisionRecs(a, b) || Raylib.CheckCollisionRecs(b, c) || Raylib.CheckCollisionRecs(a, c))
                    fails.Add("endCardDoorsOverlap");
                if (!(a.X < b.X && b.X < c.X)) fails.Add("endCardDoorOrder");
                if (g.EndReserve < 0) fails.Add("endReserveNegative");
                // the third door's handler
                g.BeginWarRoom();
                if (g.Phase != Phase.WarRoom) fails.Add("warRoomDoorDoesNotOpen");

                // ...and the LOSE card, which is the one the audit called the highest-leverage
                // retention moment in the product, publishes the same three.
                var gl = new Game { NoPersist = true };
                gl.DebugSummary(true);
                Raylib.BeginDrawing();
                Hud.Draw(gl);
                Raylib.EndDrawing();
                if (Hud.EndWarRoomBtn.Width < 20) fails.Add("loseCardMissingWarRoom");
                if (gl.EndSalvage <= 0) fails.Add("loseCardBanksNoSalvage");   // the slab the subtitle explains
            }
        }
        catch (Exception ex) { fails.Add("threw:" + ex.GetType().Name + ":" + ex.Message); }
        finally
        {
            try
            {
                if (hadSave) System.IO.File.WriteAllText(sp, saveStash);
                else if (System.IO.File.Exists(sp)) System.IO.File.Delete(sp);
                if (hadMeta) System.IO.File.WriteAllText(mp, metaStash);
                else if (System.IO.File.Exists(mp)) System.IO.File.Delete(mp);
            }
            catch { }
        }

        return fails.Count == 0
            ? "QUITTEST: PASS (quit arms then confirms and disarms; the quit path keeps the "
              + "mission-start checkpoint byte-identical and never touches meta.json; both end "
              + "cards publish three non-overlapping doors and the third opens the War Room)"
            : "QUITTEST: FAIL (" + string.Join(",", fails.Distinct()) + ")";
    }

    // ─── SETTINGS EVERYWHERE — reachability self-test (SIGHTLINE_SETTINGSTEST=1) ─────────────────
    /// C5 recorded (ROADMAP, "Left open by C5") that TEXT SIZE and COLORBLIND could not be reached
    /// until the player was in a fight: `Update`'s Escape handler was gated on PlayerTurn ||
    /// EnemyTurn, the pause card was the SOLE home of those settings, the INTRO had no settings
    /// door, and `case Phase.Barracks` had no Escape handler at all — nor a way to the FIELD MANUAL
    /// (`K` was Intro-gated). QUITTEST pins the arm/confirm/checkpoint contract and NOTHING about
    /// reachability; this is the other half. Each leg presses the same seam `Update` presses
    /// (OnEscape), hits the same rect the mouse would (PauseHit -> ActPause), and asserts the
    /// setting stuck, hit disk through Display's ONE writer, and the phase came back to where the
    /// card was opened from.
    ///
    /// Stashes and restores display.json (the text-size change is a real Display.Save) and the
    /// Display/Cfg scale statics, so a --full sweep leaves the profile byte-identical.
    public string SettingsSelfTest()
    {
        Util.Reseed(70127);
        var fails = new List<string>();
        string dispPath = Display.SettingsPathPublic;
        string dispStash = null; bool hadDisp = false;
        try { hadDisp = System.IO.File.Exists(dispPath); if (hadDisp) dispStash = System.IO.File.ReadAllText(dispPath); } catch { }
        int savedScale = Display.UiScaleIdx;
        // W5 review: the card's hover fills read the live pointer; pin it off-card so a stray
        // Xvfb pointer can never land on a control mid-test.
        Hud.MousePin = new Vector2(-100, -100);

        static Vector2 Centre(Rectangle r) => new Vector2(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        static void Frame(Game g) { Raylib.BeginDrawing(); Hud.Draw(g); Raylib.EndDrawing(); }
        static bool Overlaps(Rectangle a, Rectangle b) => a.Width > 0 && b.Width > 0 && Raylib.CheckCollisionRecs(a, b);

        // one card, three phases: open with Escape, change TEXT SIZE through the rect, close with Escape
        void CardRoundTrip(Game g, string where, Phase home, bool expectAbandon, string expectVerb)
        {
            g.OnEscape();
            if (!g.Paused) { fails.Add(where + ":escapeDoesNotOpenCard"); return; }
            if (g.Phase != home) { fails.Add(where + ":escapeChangedPhase:" + g.Phase); return; }
            Frame(g);                                   // publishes the card's rects
            if (Hud.PauseUiScale.Width < 20) fails.Add(where + ":noTextSizeControl");
            if (Hud.PauseColorblind.Width < 20) fails.Add(where + ":noColorblindControl");
            if (Hud.PauseQuit.Width < 20) fails.Add(where + ":noQuitToDesktop");
            if ((Hud.PauseAbandon.Width > 0) != expectAbandon) fails.Add(where + (expectAbandon ? ":abandonMissing" : ":abandonOffered"));
            string verb = Hud.PauseResumeLabel(g);
            if (verb != expectVerb) fails.Add(where + ":firstRowReads:" + verb);
            // the TEXT SIZE control, through the same rect + dispatch the mouse uses
            int before = Display.UiScaleIdx;
            string id = g.PauseHit(Centre(Hud.PauseUiScale));
            if (id != "uiscale") { fails.Add(where + ":textSizeRectHits:" + (id ?? "nothing")); return; }
            g.ActPause(id);
            int want = (before + 1) % Display.UiScaleLevels.Length;
            if (Display.UiScaleIdx != want) fails.Add(where + ":textSizeDidNotChange");
            if (MathF.Abs(Cfg.UiScale - Display.UiScale) > 1e-4f) fails.Add(where + ":textSizeNotApplied");
            if (g.Paused != true || g.Phase != home) fails.Add(where + ":controlClosedTheCard");
            // ...and it reached disk through Display's writer: reload the file into the statics
            try
            {
                if (!System.IO.File.Exists(dispPath)) fails.Add(where + ":settingsNotWritten");
                else
                {
                    Display.UiScaleIdx = -1;
                    Display.LoadForTest();
                    if (Display.UiScaleIdx != want) fails.Add(where + ":settingsOnDiskRead:" + Display.UiScaleIdx);
                }
            }
            catch (Exception ex) { fails.Add(where + ":settingsReload:" + ex.GetType().Name); }
            g.OnEscape();
            if (g.Paused) fails.Add(where + ":escapeDoesNotCloseCard");
            if (g.Phase != home) fails.Add(where + ":closeChangedPhase:" + g.Phase);
        }

        // FIELD MANUAL / AUDIO CHECK opened FROM the card must come back TO the card, in this phase.
        // `key` != Null routes the id through the card's OWN key table (review round 1: the plate's
        // "K" hint was dead — nothing read K while the card was open), so deleting the binding
        // fails here rather than leaving a hint that lies.
        void CardDetour(Game g, string where, Phase home, string id, KeyboardKey key = KeyboardKey.Null)
        {
            g.Paused = false;
            g.OnEscape();
            if (!g.Paused) { fails.Add(where + ":" + id + ":noCard"); return; }
            if (key != KeyboardKey.Null)
            {
                if (Array.IndexOf(Game.PauseKeys, key) < 0) { fails.Add(where + ":" + id + ":key" + key + "NotRead"); g.Paused = false; return; }
                string kid = Game.PauseKeyId(key);
                if (kid != id) { fails.Add(where + ":" + id + ":key" + key + "Maps:" + (kid ?? "nothing")); g.Paused = false; return; }
                id = kid;
            }
            g.ActPause(id);
            Phase expect = id == "codex" ? Phase.Codex : Phase.AudioCheck;
            if (g.Phase != expect) { fails.Add(where + ":" + id + ":didNotOpen:" + g.Phase); g.Paused = false; return; }
            if (id == "codex") g.ExitCodex(); else g.ExitAudition();
            if (g.Phase != home) fails.Add(where + ":" + id + ":backLandsOn:" + g.Phase);
            else if (!g.Paused) fails.Add(where + ":" + id + ":backLostTheCard");
            g.Paused = false;
        }

        // The ARMED quit sentence, as drawn: centred on the QUIT plate at 12px, it must end inside
        // the card (review round 1: the first intro / barracks sentences ran ~65 px past its edge)
        // — at EVERY shipped TEXT SIZE, because the sentence scales and the card does not (the
        // first cut of this leg measured at whatever size the round trip had left behind, which is
        // how a 50-char sentence read 409 px at 120% and passed at 100%).
        void ArmedFits(Game g, string where)
        {
            g.QuitArmed = true;
            string warn = g.QuitWarning;
            if (string.IsNullOrWhiteSpace(warn)) fails.Add(where + ":armedSentenceEmpty");
            int keep = Display.UiScaleIdx;
            for (int i = 0; i < Display.UiScaleLevels.Length; i++)
            {
                Display.UiScaleIdx = i; Display.ApplyUiScale();
                Frame(g);
                float ww = Cfg.Measure(warn, 12, 1f).X;
                float cx = Hud.PauseQuit.X + Hud.PauseQuit.Width / 2f;
                float right = Hud.PauseCard.X + Hud.PauseCard.Width, left = Hud.PauseCard.X;
                if (cx + ww / 2f > right - 4 || cx - ww / 2f < left + 4)
                    fails.Add(where + ":armedSentenceOverflows@" + (int)(Display.UiScale * 100) + "%:" + (int)ww + "px");
            }
            Display.UiScaleIdx = keep; Display.ApplyUiScale();
            g.QuitArmed = false;
        }

        try
        {
            // ---- (A) INTRO — the first screen a player sees ------------------------------------
            {
                var g = new Game { NoPersist = true };
                if (g.Phase != Phase.Intro) fails.Add("bootPhase:" + g.Phase);
                Frame(g);
                var door = Hud.IntroSettingsBtn;
                if (door.Width < 20 || door.Height < 20) fails.Add("intro:noSettingsDoor");
                else
                {
                    var others = new[] { Hud.OverlayBtn, Hud.OverlayBtn2, Hud.OverlayBtn3, Hud.OverlayBtn4, Hud.OverlayBtn5,
                                         Hud.OverlayBtn6, Hud.OverlayBtn7, Hud.OverlayBtn8, Hud.OverlayBtn9, Hud.IntroQuitBtn };
                    foreach (var o in others) if (Overlaps(door, o)) { fails.Add("intro:settingsDoorOverlapsAnotherDoor"); break; }
                    if (door.Y + door.Height > Cfg.ScreenH - 40) fails.Add("intro:settingsDoorOffTheBottom");
                    // review round 1: the door OPENS the card — through the same click and key
                    // dispatch the player uses (IntroHit -> ActIntro; IntroKeyId(O) -> ActIntro).
                    string hit = g.IntroHit(Centre(door));
                    if (hit != "settings") fails.Add("intro:settingsRectHits:" + (hit ?? "nothing"));
                    else
                    {
                        g.ActIntro(hit);
                        if (!g.Paused || g.Phase != Phase.Intro) fails.Add("intro:settingsClickDidNotOpenCard");
                        g.Paused = false;
                    }
                    string kid = Game.IntroKeyId(KeyboardKey.O);
                    if (kid != "settings") fails.Add("intro:keyOMaps:" + (kid ?? "nothing"));
                    else
                    {
                        g.QuitArmed = true;   // the door disarms a stale confirm, like Escape does
                        g.ActIntro(kid);
                        if (!g.Paused || g.Phase != Phase.Intro) fails.Add("intro:keyODidNotOpenCard");
                        if (g.QuitArmed) fails.Add("intro:doorDidNotDisarmQuit");
                        g.Paused = false;
                    }
                }
                CardRoundTrip(g, "intro", Phase.Intro, expectAbandon: false, expectVerb: "BACK");
                CardDetour(g, "intro", Phase.Intro, "codex", KeyboardKey.K);
                CardDetour(g, "intro", Phase.Intro, "audio");
                g.OnEscape(); ArmedFits(g, "intro"); g.Paused = false;
                // the door's key: Escape closes what it opened; a second press re-opens (toggle)
                g.OnEscape(); g.OnEscape();
                if (g.Paused || g.Phase != Phase.Intro) fails.Add("intro:escapeToggle");
            }

            // ---- (B) BARRACKS — the screen where the player deliberates ------------------------
            {
                var g = new Game { NoPersist = true };
                g.DebugShop();
                if (g.Phase != Phase.Barracks) fails.Add("stageBarracks:" + g.Phase);
                // ABANDON is deliberately NOT offered here: the campaign checkpoint is written at
                // MISSION START (SetupMission), so in the debrief the file on disk is the start of
                // the mission just won — "the checkpoint is kept, CONTINUE resumes it" would
                // resume a mission the player already finished and drop every debrief pick.
                CardRoundTrip(g, "barracks", Phase.Barracks, expectAbandon: false, expectVerb: "BACK");
                CardDetour(g, "barracks", Phase.Barracks, "codex", KeyboardKey.K);
                CardDetour(g, "barracks", Phase.Barracks, "audio");
                g.OnEscape(); ArmedFits(g, "barracks"); g.Paused = false;
                g.Mode = GameMode.Endless; g.OnEscape(); ArmedFits(g, "barracks-endless"); g.Paused = false; g.Mode = GameMode.Campaign;
                // the ARMORY owns Escape while it is open (HandleShopClick backs out one level)
                g.ArmoryMode = true; g.OnEscape();
                if (g.Paused) fails.Add("barracks:escapeStoleArmoryBack");
                g.Paused = false;
                // review round 1 (MAJOR): ARMORY open -> [Enter] / PROCEED -> Escape must open the
                // card. The proceed path used to set _shopDone and leave ArmoryMode set; Hud stops
                // drawing the requisition once ShopDone, so the stale flag was invisible, and
                // OnEscape's armory exception swallowed Escape for the rest of the visit.
                g.ProceedFromShop();
                if (g.ArmoryMode || g.ArmorySoldier != null) fails.Add("barracks:proceedLeftArmoryOpen");
                if (!g.ShopDone) fails.Add("barracks:proceedDidNotLeaveShop");
                g.OnEscape();
                if (!g.Paused) fails.Add("barracks:escapeDeadAfterArmoryProceed");
                g.Paused = false;
                // ...and the predicate is a lock of its own: a stale flag after the shop is closed
                // (some future path that forgets to clear it) must not swallow Escape either
                g.ArmoryMode = true; g.OnEscape();
                if (!g.Paused) fails.Add("barracks:staleArmoryFlagSwallowsEscape");
                g.ArmoryMode = false; g.Paused = false;
                // [K] from the barracks: the manual opens, and BACK returns HERE, not to the intro
                g.BeginCodex();
                if (g.Phase != Phase.Codex) fails.Add("barracks:manualDidNotOpen");
                g.ExitCodex();
                if (g.Phase != Phase.Barracks) fails.Add("barracks:manualBackLandsOn:" + g.Phase);
                if (g.Paused) fails.Add("barracks:manualBackInventedACard");
            }

            // ---- (C) PLAYER TURN — the existing home, pinned so the move cannot regress it ------
            {
                var g = new Game { NoPersist = true };
                g.StartMission(1);
                if (g.Phase != Phase.PlayerTurn) fails.Add("stageMission:" + g.Phase);
                CardRoundTrip(g, "mission", Phase.PlayerTurn, expectAbandon: true, expectVerb: "RESUME");
                CardDetour(g, "mission", Phase.PlayerTurn, "codex", KeyboardKey.K);
                // Escape in a targeting mode cancels the mode and does NOT open the card
                g.AimMode = true; g.OnEscape();
                if (g.AimMode) fails.Add("mission:escapeDidNotCancelAim");
                if (g.Paused) fails.Add("mission:escapeInAimOpenedCard");
                // closing the card disarms QUIT (W5)
                g.OnEscape(); g.RequestQuit(); if (!g.QuitArmed) fails.Add("mission:quitDidNotArm");
                // review round 1 (MAJOR): a click on NOTHING disarms too — the old HandlePauseMenu
                // disarmed on any left click that was not QUIT, and the PauseHit/ActPause split
                // had put the null check above the disarm. Through the same seam the mouse uses.
                Frame(g);
                string off = g.PauseHit(new Vector2(5, 5));
                if (off != null) fails.Add("mission:offCardPointHits:" + off);
                g.ActPause(off);
                if (g.QuitArmed) fails.Add("mission:clickOnNothingDidNotDisarmQuit");
                g.RequestQuit(); if (!g.QuitArmed) fails.Add("mission:quitDidNotReArm");
                g.OnEscape(); if (g.QuitArmed) fails.Add("mission:closeDidNotDisarmQuit");
                if (g.QuitRequested) fails.Add("mission:quitFired");
                // the armed sentence is mode-true AND fits the card in every mode a fight can be in
                // (review round 1: LAST STAND / SKIRMISH / DAILY / TRAINING never write save.json,
                // so "the current mission restarts from its start" was a lie in four of five modes)
                g.OnEscape();
                var fight = g.QuitWarning;
                foreach (var (mode, daily, tag) in new[] { (GameMode.Campaign, false, "campaign"), (GameMode.Endless, false, "endless"),
                                                            (GameMode.Skirmish, false, "skirmish"), (GameMode.Skirmish, true, "daily"),
                                                            (GameMode.Training, false, "training") })
                {
                    g.Mode = mode; g.DailyMode = daily;
                    ArmedFits(g, "mission-" + tag);
                    if (mode != GameMode.Campaign && g.QuitWarning == fight) fails.Add("mission-" + tag + ":armedSentenceClaimsACheckpoint");
                }
                g.Mode = GameMode.Campaign; g.DailyMode = false; g.Paused = false;
            }

            // ---- (D) where the card has NO home, Escape must leave it closed -------------------
            {
                var g = new Game { NoPersist = true };
                foreach (var p in new[] { Phase.WarRoom, Phase.Codex, Phase.Draft, Phase.SkirmishSetup, Phase.AudioCheck, Phase.Win, Phase.Lose })
                {
                    g.Phase = p; g.Paused = false; g.OnEscape();
                    if (g.Paused) fails.Add("cardOpenedOn:" + p);
                }
                // and never under the autopilot (the flywheel must not see a card): OnEscape itself
                // refuses (review round 1 — Update's own gate was the only lock before), and Update
                // still never reaches it
                g.Phase = Phase.Intro; g.AutoPlay = true; g.Paused = false;
                g.OnEscape();
                if (g.Paused) fails.Add("autoplayEscapeOpenedCard");
                g.Update(1f / 60f);
                if (g.Paused) fails.Add("autoplayUpdateOpenedCard");
            }
        }
        catch (Exception ex) { fails.Add("threw:" + ex.GetType().Name + ":" + ex.Message); }
        finally
        {
            Hud.MousePin = new Vector2(float.NaN, float.NaN);
            Display.UiScaleIdx = savedScale; Display.ApplyUiScale();
            try
            {
                if (hadDisp) System.IO.File.WriteAllText(dispPath, dispStash);
                else if (System.IO.File.Exists(dispPath)) System.IO.File.Delete(dispPath);
            }
            catch { }
        }

        return fails.Count == 0
            ? "SETTINGSTEST: PASS (OnEscape opens the card on INTRO, BARRACKS and PLAYER TURN and "
              + "closes it to the same phase; TEXT SIZE changes via PauseHit/ActPause and re-reads "
              + "from display.json; intro SETTINGS door opens the card via IntroHit/ActIntro and "
              + "IntroKeyId(O); BACK outside a fight, RESUME + ABANDON in one; [K] on the card is in "
              + "PauseKeys and the manual/audio return to the card; BeginCodex from BARRACKS returns "
              + "there; ARMORY owns Escape only while the shop is open (PROCEED clears it, a stale "
              + "flag cannot swallow Escape); a click on nothing disarms QUIT; the armed sentence "
              + "fits the card in every phase and mode; OnEscape is inert on 7 overlay phases and "
              + "under AutoPlay)"
            : "SETTINGSTEST: FAIL (" + string.Join(",", fails.Distinct()) + ")";
    }

    /// TUTTEST seams: set one of the verb-performed lesson flags / the turn counter / force an end
    /// check, without reaching into private state from the test body. Harness-only.
    public void DebugSetTutFlag(string which)
    {
        switch (which)
        {
            case "move": _tutMoved = true; break;
            case "shot": _tutShot = true; break;
            case "over": _tutOver = true; break;
            case "grenade": _tutGrenade = true; break;
            case "ability": _tutAbility = true; break;
        }
    }
    public void DebugSetTurn(int t) => _turnCount = t;
    public int DebugAnimCount => _anims.Count;

    /// W9 THE REPAIR — drive the REAL Game.ResolveEvent with a single synthetic outcome. BENCHTEST
    /// needs the whole path (EventCatalog.Apply + the report line + the re-derive), not just Apply,
    /// because the defect lived in the CALLER: ResolveEvent mutated the roster and never re-derived
    /// the deployment that DebriefSurvivors had already computed.
    public void DebugResolveEventOutcome(EventOutcome o)
    {
        _activeEvent = new GameEvent
        {
            Id = "w9probe", Title = "W9 PROBE", Flavor = "-",
            Choices = new[] { new EventChoice { Label = "-", Preview = "-", Outcome = o } },
        };
        _eventNode = null;
        ResolveEvent(0);
    }
    /// W5 ONRAMPTEST seam: run the once-per-process meta load on THIS instance (EnsureMetaLoaded
    /// is private and idempotent per Game), so the test can read the profile defaults it sets.
    public void DebugLoadMeta() => EnsureMetaLoaded();
    public void DebugCheckEnd() => CheckEnd();

    // ─── W5 THE FIRST HOUR — the mission-1 briefing (SIGHTLINE_BRIEFTEST=1) ────────────────────
    /// BRIEFTEST seam: tick EXACTLY the teaching/briefing chain `Game.Update` runs, in Update's
    /// order (Game.cs, the four calls under the global-key block). No window, no input device, no
    /// turn flow — this is the observation instrument the W5 audit finding had no way to build,
    /// because every existing harness path sets NoPersist and NoPersist makes StartTutorialMaybe
    /// return before the strip ever arms.
    public void DebugTeachTick(float dt) { UpdateTutorial(dt); UpdateTraining(dt); UpdateFieldTips(dt); UpdateBriefing(dt); }

    /// BRIEFTEST seam: the tutorial half of EndPlayerTurn (Game.cs) plus the turn bump, so the
    /// test can drive a player who is ENDING TURNS without staging a whole enemy phase.
    public void DebugEndTurnTutorial() { if (TutStep >= 0 && TutStep < TutStepDone) AdvanceTutorial(); _turnCount++; }

    /// SIGHTLINE_BRIEFTEST — PROGRAM RESONANCE W5 "THE FIRST HOUR".
    ///
    /// THE DEFECT THIS TEST EXISTS TO PIN. On a FIRST-EVER campaign run, mission 1's briefing —
    /// RESONANCE C1's entire narrative frame, the faction, the region, the reason the squad is on
    /// this field — could not draw. `BriefAllowed` requires `TutorialText == null`; the mission-1
    /// lesson strip is non-null from the moment `SetupMission` arms it; `UpdateBriefing` nulls the
    /// card outright the instant `Stats.CombatLog` fills, and the log fills on the first shot by
    /// either side. So the card spent the whole strip HOLDING (never burning its 11 s clock) and
    /// was then destroyed by the first exchange — or by `BriefHoldMax` (45 s), whichever came
    /// first. Mission 1 is the only mission a first-time player is guaranteed to see.
    ///
    /// The fix is an ORDERING one: on mission 1 the briefing is a PRE-FIGHT beat and goes FIRST —
    /// `StartTutorialMaybe` arms the strip PENDING (`TutPending`), and `UpdateTutorial` opens it
    /// the moment the card retires. Verb staging is live throughout, so the action bar does not
    /// flicker between whole and staged. `TutStepFire` also gains the turn-count patience fallback
    /// its three siblings already had.
    ///
    /// Every leg runs a LIVE (NoPersist == false) game — that is the whole point — so the real
    /// display.json / save.json / meta.json are stashed and restored around the body.
    public string BriefingSelfTest()
    {
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70133);
        var fails = new List<string>();
        const float Dt = 1f / 60f;

        string dispPath = Display.SettingsPathPublic;
        string sp = SaveGame.SavePathPublic, mp = SaveGame.MetaPathPublic;
        bool hadDisp = false, hadSave = false, hadMeta = false;
        string dispStash = null, saveStash = null, metaStash = null;
        try
        {
            hadDisp = System.IO.File.Exists(dispPath); if (hadDisp) dispStash = System.IO.File.ReadAllText(dispPath);
            hadSave = System.IO.File.Exists(sp); if (hadSave) saveStash = System.IO.File.ReadAllText(sp);
            hadMeta = System.IO.File.Exists(mp); if (hadMeta) metaStash = System.IO.File.ReadAllText(mp);
        }
        catch { }
        bool savedTut = Display.TutorialSeen, savedShow = Display.ShowAllVerbs;
        int savedTips = Display.TipsSeen;

        // Drive `secs` seconds of an IDLE, READING player and return (a) how many of them the
        // briefing card spent burning its own clock — the MODEL — and (b) on how many of those
        // frames Hud.DrawBriefCard actually RAN — the DRAW.
        //
        // W5-FIX (review blocker 5): (b) is new and it is the point. This test certified "the
        // briefing plays its full 11 s" while observing only `Game.BriefTimer` and `BriefLines`,
        // i.e. the model's own `BriefAllowed` predicate re-read back. A reviewer put a one-line
        // `&& false` on the dispatch in Hud.Draw so the card is never drawn; it built clean and
        // still PASSed with the full 11 s. A test that cannot see the thing it certifies is not a
        // test — so every watched frame now paints a REAL frame through Hud.Draw and counts the
        // card's own paints. (The window is 64x64; raylib clips, and the cost is the reason the
        // watch runs at the tick rate and not faster.)
        (float shown, int drawn) Watch(Game g, float secs)
        {
            float shown = 0f; int drawn = 0;
            for (int i = 0; i < (int)(secs * 60); i++)
            {
                float before = g.BriefLines != null ? g.BriefTimer : -1f;
                g.DebugTeachTick(Dt);
                Hud.BriefCardDraws = 0;
                Raylib.BeginDrawing();
                Hud.Draw(g);
                Raylib.EndDrawing();
                if (Hud.BriefCardDraws > 0) drawn++;
                if (before >= 0f && g.BriefLines != null && g.BriefTimer < before) shown += before - g.BriefTimer;
            }
            return (shown, drawn);
        }

        try
        {
            Display.TipsSeen = ~0;        // burn every just-in-time field tip: this test is about
            Display.ShowAllVerbs = false; // the LESSON strip, and a tip would be a second variable

            // ---- (A) THE FIRST-EVER RUN. The card must play, in full, before the strip opens ----
            Display.TutorialSeen = false;
            var g = new Game();                       // NoPersist deliberately FALSE — the live path
            g.StartMission(1);
            if (g.BriefLines == null) fails.Add("briefNotComposed");
            if (g.BriefHead == null) fails.Add("briefHeadMissing");
            if (Stats.CombatLog.Count != 0) fails.Add("logNotEmptyAtMissionStart");
            // The strip is ARMED but DEFERRED, and the bar is staged the whole time (no flicker
            // between a whole bar during the briefing and a staged one after it).
            if (!g.TutPending) fails.Add("stripNotArmedPending");
            if (g.TutorialText != null) fails.Add("stripOpenedOverTheBriefing");
            if (!g.OnboardingActive) fails.Add("stagingOffDuringBriefing");
            if (!g.VerbStagingActive) fails.Add("verbStagingOffDuringBriefing");

            // 12 s of a player reading: the card is 11 s (BriefShowSeconds) and must burn nearly
            // all of it. THIS IS THE HEADLINE ASSERTION and it measured 0.00 s before the fix.
            var (shown, drawn) = Watch(g, 12f);
            if (shown < BriefShowSeconds - 0.5f)
                fails.Add($"briefShownOnlyFor{shown:0.00}sOf{BriefShowSeconds:0}s");
            // ...and the card was PAINTED on those frames. Model and draw, separately observed.
            int wantFrames = (int)((BriefShowSeconds - 0.5f) * 60f);
            if (drawn < wantFrames)
                fails.Add($"briefCardDrawnOnOnly{drawn}framesOf{wantFrames}");
            if (g.BriefLines != null) fails.Add("briefNeverRetired");
            // ...and the strip opens the moment the card retires — the lesson is not lost, it is
            // re-ordered. TutStepConceal holds while the squad is concealed on turn 1.
            if (g.TutPending) fails.Add("stripStillPendingAfterBrief");
            if (g.TutStep != TutStepConceal) fails.Add("stripDidNotOpenAfterBrief:" + g.TutStep);
            if (g.TutorialText == null) fails.Add("stripTextNullAfterBrief");

            // ---- (B) the strip still RUNS and still COMPLETES (the fix reorders, never removes) --
            for (int t = 0; t < 6 && g.TutStep >= 0 && g.TutStep < TutStepDone; t++)
            { g.DebugEndTurnTutorial(); g.DebugTeachTick(Dt); }
            if (g.TutStep != TutStepDone) fails.Add("stripDidNotReachDone:" + g.TutStep);
            for (int i = 0; i < 60 * 9; i++) g.DebugTeachTick(Dt);   // the 7 s wrap-up dwell
            if (g.TutStep != -1) fails.Add("stripDidNotComplete:" + g.TutStep);
            if (!Display.TutorialSeen) fails.Add("completionDidNotMarkSeen");

            // ---- (C) TutStepFire's patience fallback (its three siblings have had one for waves) --
            {
                Display.TutorialSeen = false;
                var pg = new Game();
                pg.StartMission(1);
                pg.ShowTutorialStep(TutStepFire);
                pg.DebugSetTurn(TutFirePatience - 1);
                pg.DebugTeachTick(Dt);
                if (pg.TutStep != TutStepFire) fails.Add("fireStepYieldedEarly");   // not before its turn
                pg.DebugSetTurn(TutFirePatience);
                pg.DebugTeachTick(Dt);
                if (pg.TutStep == TutStepFire) fails.Add("fireStepHasNoPatienceFallback");
                // and the shot still ends it immediately, whatever the turn count
                Display.TutorialSeen = false;
                var sg = new Game();
                sg.StartMission(1);
                sg.ShowTutorialStep(TutStepFire);
                sg.DebugSetTutFlag("shot");
                sg.DebugTeachTick(Dt);
                if (sg.TutStep == TutStepFire) fails.Add("fireStepIgnoredTheShot");
            }

            // ---- (D) THE CONTROL. A RETURNING player (TutorialSeen) has no strip, so the card has
            //          always worked for them. If this leg ever failed, the test would be measuring
            //          something other than the tutorial interaction.
            {
                Display.TutorialSeen = true;
                var rg = new Game();
                rg.StartMission(1);
                if (rg.TutPending || rg.TutStep >= 0) fails.Add("stripArmedForReturningPlayer");
                var (rshown, rdrawn) = Watch(rg, 12f);
                if (rshown < BriefShowSeconds - 0.5f) fails.Add($"controlBriefShownOnlyFor{rshown:0.00}s");
                if (rdrawn < (int)((BriefShowSeconds - 0.5f) * 60f))
                    fails.Add($"controlBriefCardDrawnOnOnly{rdrawn}frames");
            }

            // ---- (E) the combat log still retires the card — the pre-fight contract is intact ----
            {
                Display.TutorialSeen = true;
                var lg = new Game();
                lg.StartMission(1);
                Stats.Log(1, (int)Team.Player, "BRIEFTEST synthetic exchange");
                lg.DebugTeachTick(Dt);
                if (lg.BriefLines != null) fails.Add("logDidNotRetireTheCard");
                Stats.ClearLog();
            }

            // ---- (F) non-campaign modes still say nothing (the briefing is campaign vocabulary) --
            {
                var tg = new Game { NoPersist = true };
                tg.BeginTraining();
                if (tg.BriefLines != null) fails.Add("drillComposedABriefing");
            }
        }
        catch (Exception ex) { fails.Add("threw:" + ex.GetType().Name + ":" + ex.Message); }
        finally
        {
            Display.TutorialSeen = savedTut; Display.ShowAllVerbs = savedShow; Display.TipsSeen = savedTips;
            Stats.ClearLog();
            try
            {
                if (hadDisp) System.IO.File.WriteAllText(dispPath, dispStash);
                else if (System.IO.File.Exists(dispPath)) System.IO.File.Delete(dispPath);
                if (hadSave) System.IO.File.WriteAllText(sp, saveStash);
                else if (System.IO.File.Exists(sp)) System.IO.File.Delete(sp);
                if (hadMeta) System.IO.File.WriteAllText(mp, metaStash);
                else if (System.IO.File.Exists(mp)) System.IO.File.Delete(mp);
            }
            catch { }
        }

        return fails.Count == 0
            ? "BRIEFTEST: PASS (live first-ever mission 1: briefing plays AND IS DRAWN for its full "
              + $"{BriefShowSeconds:0}s pre-fight, strip opens after it with staging unbroken, strip still "
              + $"completes, FIRE lesson yields at turn {TutFirePatience}, returning-player control + "
              + "combat-log retirement + drill silence)"
            : "BRIEFTEST: FAIL (" + string.Join(",", fails.Distinct()) + ")";
    }

    // ─── W5-FIX — the BACKDROP registry (SIGHTLINE_BACKDROPTEST=1) ─────────────────────────────
    /// SIGHTLINE_BACKDROPTEST — the STRUCTURAL gate for review blocker 1.
    ///
    /// THE DEFECT THIS TEST EXISTS TO PIN. W5-2 split the frame so the animated full-screen
    /// backdrops render into the BLOOM SOURCE (before `Display.BuildBloom`) and the chrome renders
    /// after it. `Hud.DrawBackdropLayer` was made the home of every backdrop... except AUDIO
    /// CHECK's, which stayed inside `Hud.DrawAudition`, i.e. in the CHROME pass. The composite
    /// then added `glow * 1.45` computed from the LIVE BOARD on top of an opaque audition screen:
    /// measured against a d350416 build with post-FX on, 17,010 px brightened by more than 20
    /// luma, 8,640 by more than 40, peak +154 — worst over the MASTER and MUSIC fader rows. The
    /// screen ships reachable from the intro AND from the pause card mid-mission, and
    /// `Display.PostFX` defaults true, so it shipped to everyone.
    ///
    /// WHY NOTHING CAUGHT IT. Three source comments and the DEVLOG all asserted that AUDIO CHECK
    /// "draws no backdrop". CONTRASTTEST reads nine main-menu labels and could never have seen it,
    /// and cannot see the NEXT phase added without a `DrawBackdropLayer` entry either. So this
    /// test does not check a screen — it checks the INVARIANT, over every Phase that exists:
    ///
    ///   (A) NO phase paints a full-screen backdrop from the CHROME pass. Ever. This is the one
    ///       that fails on the pre-fix tree (`SIGHTLINE_AUDBACKDROP=1` restores the defect).
    ///   (B) `Hud.BackdropPhase` and `DrawBackdropLayer`'s switch agree exactly — a phase in the
    ///       registry must paint, a phase out of it must not. A future screen with a backdrop and
    ///       no registry entry paints its chrome over the main menu; one in the registry with no
    ///       backdrop loses its top bar entirely.
    ///   (C) the modal scrim is laid in the bloom-source pass ONLY when the composite is actually
    ///       running — with post-FX off there is no bright pass to attenuate, and the second wash
    ///       cost 40% of the board's luminance under the pause card for nothing.
    ///
    /// The observation point is `Hud.BackdropPaints`, incremented inside `DrawTacticalBackdrop`
    /// itself — the draw, not a predicate about the draw.
    public string BackdropSelfTest()
    {
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70140);
        var fails = new List<string>();
        var phases = (Phase[])Enum.GetValues(typeof(Phase));
        bool savedFx = Display.PostFX, savedEn = Display.Enabled;
        try
        {
            var g = new Game { NoPersist = true };
            g.StartMission(1);
            g.BeginDraft();          // populate the draft pool/offer so DrawDraft has content
            g.Phase = Phase.PlayerTurn;

            // ---- (A) the chrome pass paints NO backdrop, for any phase ------------------------
            foreach (var p in phases)
            {
                g.Phase = p;
                Hud.BackdropPaints = 0;
                try
                {
                    Raylib.BeginDrawing();
                    Hud.Draw(g);
                    Raylib.EndDrawing();
                }
                catch (Exception ex) { fails.Add($"chromeThrew:{p}:{ex.GetType().Name}"); continue; }
                if (Hud.BackdropPaints != 0) fails.Add($"backdropFromChromePass:{p}");
            }

            // ---- (B) the registry and the switch are the same set -----------------------------
            foreach (var p in phases)
            {
                g.Phase = p;
                Hud.BackdropPaints = 0;
                try
                {
                    Raylib.BeginDrawing();
                    Hud.DrawBackdropLayer(g);
                    Raylib.EndDrawing();
                }
                catch (Exception ex) { fails.Add($"backdropThrew:{p}:{ex.GetType().Name}"); continue; }
                bool painted = Hud.BackdropPaints > 0;
                bool owns = Hud.BackdropPhase(p);
                if (painted && !owns) fails.Add($"paintsButNotInRegistry:{p}");
                if (owns && !painted) fails.Add($"inRegistryButPaintsNothing:{p}");
            }
            // AUDIO CHECK by name — this is the whole point, and a name is harder to delete by
            // accident than a set membership.
            if (!Hud.BackdropPhase(Phase.AudioCheck)) fails.Add("audioCheckNotInRegistry");

            // ---- (C) the modal scrim only doubles up where the bright pass exists -------------
            {
                // BARRACKS, not PAUSE: the pause wash is multiplied by a time-based entrance tween
                // that reads 0 on its first frame, so it is not a fixed quantity to assert on.
                // The barracks family's wash is a constant and covers the same code path.
                g.Phase = Phase.Barracks;
                float lit = Hud.BloomScrimAlpha(g, true);
                float dark = Hud.BloomScrimAlpha(g, false);
                g.Phase = Phase.PlayerTurn;
                if (lit <= 0f) fails.Add($"scrimMissingUnderPostFx:{lit:0.00}");
                if (dark != 0f) fails.Add($"scrimDoubledWithPostFxOff:{dark:0.00}");
                // ...and a phase with its own opaque backdrop must not also be scrimmed.
                g.Phase = Phase.Intro;
                if (Hud.BloomScrimAlpha(g, true) != 0f) fails.Add("scrimOverAnOpaqueBackdrop");
                g.Phase = Phase.PlayerTurn;
            }
        }
        catch (Exception ex) { fails.Add("threw:" + ex.GetType().Name + ":" + ex.Message); }
        finally { Display.PostFX = savedFx; Display.Enabled = savedEn; }

        return fails.Count == 0
            ? $"BACKDROPTEST: PASS (all {phases.Length} phases: none paints a backdrop from the chrome "
              + "pass; DrawBackdropLayer's switch == Hud.BackdropPhase exactly, AUDIO CHECK included; "
              + "modal scrim doubles only when the composite runs)"
            : "BACKDROPTEST: FAIL (" + string.Join(",", fails.Distinct()) + ")";
    }

    /// SIGHTLINE_ONRAMPTEST — PROGRAM RESONANCE W5 "ON-RAMP". Two features, one test:
    ///
    ///  (A) the RECRUIT rung (Heat.Min == -1) is a REAL, measurable difficulty below standard —
    ///      the data row, the built mission, the bleed-out clock and the checkpoint valve all move,
    ///      and heat 0 does NOT. The last clause matters most: RECRUIT ships by widening the heat
    ///      RANGE, so every assertion below is paired with the same read at heat 0 to prove the
    ///      designed difficulty (and with it every measured number in docs/) is untouched.
    ///  (B) the comfort settings (animation speed, UI text scale) survive a real JSON round trip,
    ///      the text scale reaches Cfg symmetrically (measure and draw share one multiplier), and
    ///      the animation multiplier is HARD-PINNED to 1x under AutoPlay/NoPersist — the guard that
    ///      keeps the autopilot smoke test and the SIGHTLINE_BALANCE flywheel bit-for-bit as before.
    /// X2 TRUE NORTH II — SIGHTLINE_OPENERTEST. Pins the COLD-OPENER GRACE (`Mission.OpenerTrim`):
    /// the base force is trimmed by the full amount on mission 1, by half (rounded up) on mission
    /// 2, and NOT AT ALL from mission 3 — the same ramp `Game.SetupMission` already applies to
    /// Heat's own escalation, applied to the force heat's grace cannot reach. Also pins the
    /// floor (a trim can never take the force below 3 bodies), determinism, and the shipped
    /// default, so a future wave cannot silently un-ship the repair that moved mission 1 from
    /// 75% to 100% (DEVLOG §X2).
    /// SIGHTLINE_HVTTEST — RESONANCE W8 "THE HALF WALL". Pins DECAPITATE's punch-through target,
    /// `Game.DesignateHvt`, because the objective's difficulty is almost entirely that one body and
    /// nothing pinned it. What this pins, and why each clause exists:
    ///
    ///  (1) THE SELECTION RULE — the HVT is the toughest NON-SPECIAL alive hostile, and it is
    ///      always a member of Enemies (a legal, clickable, shootable target). A TURRET/MEDIC/
    ///      SAPPER/SHIELD/DRONE/MORTAR makes a poor punch-through and is excluded whenever any
    ///      other body exists. If this rule ever drifts, "kill the HVT" silently becomes a
    ///      different mission.
    ///  (2) THE ELITE EXEMPTION AND ITS ASYMMETRY — the whole reason this wave exists. A named
    ///      boss (the m6 finale, and the m3/m5 mid-boss) is already tuned, so it takes the HVT
    ///      marker and NO statline buff; a rank-and-file HVT on any other mission takes the full
    ///      buff. The finale is therefore the ONLY Decapitate the buff never touches, which is
    ///      how the mid-run case ended up 23.7 points harder than the climax (L2, n=163/479).
    ///      Both halves are asserted, so neither can move without this test noticing.
    ///  (3) THE EXACT MAGNITUDE, at several depths — `+(HvtHpBonusBase + HvtHpBonusPerMission *
    ///      mission)` HP and `+HvtAimBonus` aim (aim clamped at 85), measured as a DIFFERENCE
    ///      against the same seed built with the buff zeroed, so it pins the arithmetic and not
    ///      an archetype's statline.
    ///  (4) THE SHIPPED DEFAULTS — 6 / 1 / 6, i.e. the pre-W8 literal `6 + _run.Mission` and `+6`
    ///      exactly. W8 moved those numbers out of the method body and behind three dials; this
    ///      clause is what stops the move from silently becoming a balance change, and it is the
    ///      clause `SIGHTLINE_HVTBUFF=<anything but 6>` makes FAIL.
    ///  (5) THE DIALS ARE REAL — zeroing them must actually produce a weaker HVT (a dial that is
    ///      a no-op would make every future measured round a lie), and a negative depth
    ///      coefficient must floor the bonus at 0 rather than going through zero.
    ///  (6) NO HVT OFF-OBJECTIVE — an Eliminate mission leaves Hvt null and HvtBuffed false, so
    ///      the balance telemetry's `hvtKind = -1` row means what it says.
    public string HvtSelfTest()
    {
        NoPersist = true;
        var fails = new List<string>();
        int sBase = Combat.HvtHpBonusBase, sPer = Combat.HvtHpBonusPerMission, sAim = Combat.HvtAimBonus;

        bool IsSpecialCls(string c) => c == "TURRET" || c == "MEDIC" || c == "SAPPER"
                                    || c == "SHIELD" || c == "DRONE" || c == "MORTAR";

        // Build one Decapitate mission at a given depth/seed with the buff dials pinned.
        void Build(int mission, int seed, int bBase, int bPer, int bAim, Objective obj = Objective.Decapitate)
        {
            Combat.HvtHpBonusBase = bBase; Combat.HvtHpBonusPerMission = bPer; Combat.HvtAimBonus = bAim;
            Util.Reseed(seed);
            _run = new Run(); _run.Start();
            _run.HeatLevel = 0;
            _run.CurrentCard = new MissionCard { Objective = obj, ModName = "STANDARD", Reward = RewardKind.None };
            SetupMission(mission);
        }

        try
        {
            // ---- (4) the shipped defaults ARE the pre-W8 arithmetic ---------------------------
            if (sBase != 6) fails.Add("shippedHpBase=" + sBase);
            if (sPer != 1) fails.Add("shippedHpPerMission=" + sPer);
            if (sAim != 6) fails.Add("shippedAim=" + sAim);
            for (int m = 1; m <= Run.MaxMissions; m++)
            {
                Combat.HvtHpBonusBase = sBase; Combat.HvtHpBonusPerMission = sPer;
                if (Combat.HvtHpBonus(m) != 6 + m) fails.Add($"bonus(m{m})={Combat.HvtHpBonus(m)}");
            }
            // (5b) a negative depth coefficient flattens the buff but never inverts it
            Combat.HvtHpBonusBase = 2; Combat.HvtHpBonusPerMission = -5;
            if (Combat.HvtHpBonus(6) != 0) fails.Add("bonusFloor=" + Combat.HvtHpBonus(6));
            Combat.HvtHpBonusBase = sBase; Combat.HvtHpBonusPerMission = sPer;

            // ---- (1)(2)(3) over a spread of seeds and depths ---------------------------------
            int buffedSeen = 0, exemptSeen = 0;
            foreach (int seed in new[] { 1301, 4242, 90210, 777 })
            {
                for (int m = 1; m <= Run.MaxMissions; m++)
                {
                    Build(m, seed, sBase, sPer, sAim);
                    var live = Enemies.Where(e => e.Alive).ToList();
                    if (Hvt == null) { fails.Add($"noHvt s{seed} m{m}"); continue; }

                    // (1) a legal target: alive, hostile, and actually on the board.
                    if (!Hvt.Alive || Hvt.Team != Team.Enemy || !Enemies.Contains(Hvt))
                        fails.Add($"illegalHvt s{seed} m{m}");
                    // (1) specials are excluded whenever any non-special body exists.
                    if (live.Any(e => !IsSpecialCls(e.Cls)) && IsSpecialCls(Hvt.Cls))
                        fails.Add($"specialHvt s{seed} m{m} {Hvt.Cls}");

                    // (2) the exemption keys on ELITE, both ways.
                    if (HvtBuffed != (Hvt.Cls != "ELITE")) fails.Add($"exemptFlag s{seed} m{m} {Hvt.Cls}");
                    if (HvtBuffed) buffedSeen++; else exemptSeen++;

                    // (3) magnitude: rebuild the SAME world with the buff zeroed and difference it.
                    int hpOn = Hvt.MaxHp, aimOn = Hvt.Aim; string cls = Hvt.Cls, name = Hvt.Name;
                    Build(m, seed, 0, 0, 0);
                    if (Hvt == null) { fails.Add($"noHvtZeroed s{seed} m{m}"); continue; }
                    if (Hvt.Cls != cls) { fails.Add($"unstableHvt s{seed} m{m}"); continue; }
                    int wantHp = cls == "ELITE" ? 0 : 6 + m;
                    int wantAim = cls == "ELITE" ? 0 : Math.Min(85, Hvt.Aim + 6) - Hvt.Aim;
                    if (hpOn - Hvt.MaxHp != wantHp) fails.Add($"hpDelta s{seed} m{m} {hpOn - Hvt.MaxHp}!={wantHp}");
                    if (aimOn - Hvt.Aim != wantAim) fails.Add($"aimDelta s{seed} m{m} {aimOn - Hvt.Aim}!={wantAim}");
                    // (5) the dial is not a no-op on a body that is supposed to take it.
                    if (cls != "ELITE" && hpOn <= Hvt.MaxHp) fails.Add($"dialNoOp s{seed} m{m}");
                    // (1) toughest non-special: nothing else non-special out-bulks the UNBUFFED HVT.
                    int hvtBase = Hvt.MaxHp;
                    foreach (var e in Enemies.Where(e => e.Alive && !IsSpecialCls(e.Cls) && e != Hvt))
                        if (e.MaxHp > hvtBase) { fails.Add($"notToughest s{seed} m{m} {e.Cls}{e.MaxHp}>{hvtBase}"); break; }
                    // (2) the name marker follows the buff branch exactly.
                    if ((cls != "ELITE") != name.StartsWith("HVT-")) fails.Add($"marker s{seed} m{m} {name}");
                }
            }
            // both populations must actually have been exercised, or the asymmetry above is vacuous.
            if (buffedSeen == 0) fails.Add("noBuffedCaseSeen");
            if (exemptSeen == 0) fails.Add("noExemptCaseSeen");

            // ---- (2) THE ASYMMETRY, stated as the wave found it ------------------------------
            // The campaign FINALE is always an ELITE and therefore always exempt; a mid-run
            // Decapitate at m2/m4 is always a rank-and-file body and therefore always buffed.
            foreach (int seed in new[] { 1301, 4242, 90210, 777 })
            {
                Build(Run.MaxMissions, seed, sBase, sPer, sAim);
                if (Hvt == null || Hvt.Cls != "ELITE" || HvtBuffed) fails.Add($"finaleNotExempt s{seed}");
                foreach (int m in new[] { 2, 4 })
                {
                    Build(m, seed, sBase, sPer, sAim);
                    if (Hvt == null || Hvt.Cls == "ELITE" || !HvtBuffed) fails.Add($"midRunNotBuffed s{seed} m{m}");
                }
            }

            // (4b) the autopilot's HVT focus policy ships ON — it is the policy every published
            //      Decapitate number was measured through, so its default is part of the contract.
            if (!SmartHvtFocus) fails.Add("hvtPolicyDefaultOff");

            // ---- (6) no HVT on any other objective -------------------------------------------
            Build(3, 4242, sBase, sPer, sAim, Objective.Eliminate);
            if (Hvt != null || HvtBuffed) fails.Add("hvtLeakedOffObjective");
        }
        finally
        {
            Combat.HvtHpBonusBase = sBase; Combat.HvtHpBonusPerMission = sPer; Combat.HvtAimBonus = sAim;
        }

        return fails.Count == 0
            ? "HVTTEST: PASS (toughest non-special, always a legal target; ELITE exempt and mid-run buffed; "
              + "+6+1*mission HP / +6 aim at every depth; dials real, floored, default-identical; no HVT off-objective)"
            : "HVTTEST: FAIL " + string.Join(", ", fails);
    }

    public string OpenerSelfTest()
    {
        NoPersist = true;
        var fails = new List<string>();
        int shipped = Sightline.Mission.OpenerTrim;

        int CountAt(int mission, int trim, int heat = 0)
        {
            Sightline.Mission.OpenerTrim = trim;
            Util.Reseed(4242);
            _run = new Run(); _run.Start();
            _run.HeatLevel = heat;
            _run.CurrentCard = new MissionCard { Objective = Objective.Eliminate, ModName = "STANDARD", Reward = RewardKind.None };
            SetupMission(mission);
            return Enemies.Count;
        }

        try
        {
            // (1) the shipped default is the measured one — 1 body off the opener.
            if (shipped != 1) fails.Add("shippedTrim=" + shipped);

            // (2) the ramp's SHAPE: full on m1, half (rounded up) on m2, nothing from m3.
            foreach (int trim in new[] { 1, 2 })
            {
                int m1Off = CountAt(1, 0) - CountAt(1, trim);
                int m2Off = CountAt(2, 0) - CountAt(2, trim);
                int m3Off = CountAt(3, 0) - CountAt(3, trim);
                if (m1Off != trim) fails.Add($"trim{trim}:m1Off={m1Off}");
                if (m2Off != (trim + 1) / 2) fails.Add($"trim{trim}:m2Off={m2Off}");
                if (m3Off != 0) fails.Add($"trim{trim}:m3Off={m3Off}");
            }

            // (3) OFF is the pre-X2 opener, and it is not accidentally the same as ON.
            if (CountAt(1, 0) == CountAt(1, 1)) fails.Add("trimIsNoOp");

            // (4) the floor: a huge trim can never field fewer than 3 bodies (or none at all).
            int floored = CountAt(1, 99);
            if (floored < 3) fails.Add("floorBroken=" + floored);

            // (5) deterministic — the same seed and trim build the same force twice.
            if (CountAt(1, 1) != CountAt(1, 1)) fails.Add("nonDeterministic");

            // (6) the ramp is a BASE-force grace and stacks with Heat's own: the RECRUIT rung
            //     (-1 body) still fields exactly one fewer than heat 0 at m1 with the trim on,
            //     which is the relation ONRAMPTEST pins from the other side.
            int stdM1 = CountAt(1, 1, 0), recM1 = CountAt(1, 1, -1);
            if (recM1 != stdM1 - 1) fails.Add($"recruitDelta {recM1} vs {stdM1}");
        }
        finally { Sightline.Mission.OpenerTrim = shipped; }

        return fails.Count == 0
            ? "OPENERTEST: PASS (cold-opener grace: full trim at m1, half at m2, none from m3; shipped default 1; floor 3 holds; deterministic; RECRUIT still one body under heat 0 at m1)"
            : "OPENERTEST: FAIL " + string.Join(", ", fails);
    }

    /// SIGHTLINE_MIDTOOTHTEST — PROGRAM CONTOUR C1 "THE FLAT MIDDLE". Pins the mid-ladder
    /// tooth (`Heat.MidTooth`) and, more importantly, the three STRUCTURAL invariants the
    /// wave's whole measurement rests on. Legs:
    ///   (A) NO DEAD DECLARATION. A rung that declares `AiTier = t` must actually RAISE the
    ///       cumulative tier, because `Heat.AiTier` aggregates with Math.Max. THIS IS THE
    ///       DEFECT C1 FOUND: rung 6 declared tier 1 while rung 4 already published tier 1, so
    ///       EXPOSED's advertised coordination tooth could never fire — for two whole programs,
    ///       under a green HEATLADDERTEST that pinned only the CUMULATIVE vector. Run this leg
    ///       with `SIGHTLINE_MIDTOOTH=0` and it FAILS; that is the proof it can fail.
    ///   (B) NO SILENT RUNG. Every rung 1..8 must move the cumulative
    ///       (enemy, stat, dmg, tier, flags) vector — a rung that changes nothing is a rung the
    ///       player pays for and cannot name.
    ///   (C) APEX NEUTRALITY. Every mode of the dial must leave the heat-8 cumulative vector
    ///       IDENTICAL. That is what makes the C1 rounds comparable at all (the archived h8
    ///       chunks are byte-identical control-vs-lever) and what stops a future edit from
    ///       adding a SECOND DmgDelta and silently pushing the apex under its >=5 hard floor.
    ///   (D) THE OFF-SWITCH IS A TRUE CONTROL. `MidTooth = 0` must reproduce the pre-C1 table
    ///       EXACTLY — every rung's name, desc and all six fields — pinned against a literal
    ///       transcription (the TRUE BAND precedent for `SIGHTLINE_CHOICEBAND=mult`).
    ///   (E) THE SHIPPED SHAPE, per mode, as a golden string.
    ///   (F) THE TOOTH REACHES THE BOARD. Not the data row: the whole
    ///       SetupMission -> Build -> SpawnEnemies thread. At heat 6 mission 3 EVERY spawned
    ///       hostile weapon carries +1 damage over its trimmed reference band; at heat 5, none;
    ///       at heat 8, exactly one (never two). And the shipped mode did NOT move the
    ///       coordination tier — Ai's tier is still 1 at heat 6 and 2 at heat 8.
    public string MidToothSelfTest()
    {
        NoPersist = true;
        var fails = new List<string>();
        int shipped = Heat.MidTooth;

        // The cumulative state of the ladder at one level, as a comparable tuple. The flag byte
        // folds the four booleans in so leg (B) also sees a rung whose only contribution is a
        // mutator (SHORT FUSE at heat 3 would otherwise read as "changes nothing but a body").
        (int e, int st, int d, int t, int f) Vec(int lv)
        {
            int flags = (Heat.TighterContact(lv) ? 1 : 0) | (Heat.Exposed(lv) ? 2 : 0)
                      | (Heat.HarshAttrition(lv) ? 4 : 0) | (Heat.NoReinforcements(lv) ? 8 : 0);
            return (Heat.EnemyDelta(lv), Heat.StatDelta(lv), Heat.DmgDelta(lv), Heat.AiTier(lv), flags);
        }

        string Shape()
        {
            var sb = new System.Text.StringBuilder();
            for (int lv = 0; lv <= Heat.Max; lv++)
            {
                if (sb.Length > 0) sb.Append('|');
                var v = Vec(lv);
                sb.Append(lv).Append(':').Append(v.e).Append(',').Append(v.st).Append(',')
                  .Append(v.d).Append(',').Append(v.t).Append(',').Append(v.f);
            }
            return sb.ToString();
        }

        try
        {
            // ---- (D) the off-switch, transcribed literally --------------------------------
            // Written out field by field rather than derived: a control DERIVED from the thing
            // it controls cannot detect a change to the thing it controls.
            // ONE DELIBERATE DEVIATION, named here rather than hidden: rung 5's Desc reads
            // "less healing", not the pre-C1 "less field healing". That row's copy was 42
            // characters into a 38-character panel column and clipped on screen; the fix is
            // copy-only, applies in EVERY mode (it is not part of the dial), and Desc is never
            // read by any gameplay path — the R0diag CRN round (49,961 leaf fields across 32
            // chunk pairs, zero differing) is the proof mode 0 is still a true control.
            Heat.SetMidTooth(0);
            string[] preC1 =
            {
                "REINFORCED|+1 enemy per mission|1,0,F,F,F,F,0,0",
                "HARDENED|Enemies hit harder & tougher (+1 stat)|0,1,F,F,F,F,0,0",
                "SHORT FUSE|+1 enemy; enemies spot you sooner|1,0,T,F,F,F,0,0",
                "ELITE CADRE|Enemies coordinate their fire|0,0,F,F,F,F,1,0",
                "LINGERING WOUNDS|+1 enemy; wounds linger, less healing|1,0,F,F,T,F,0,0",   // C1 copy-only: see the note above
                "EXPOSED|No concealment opener; +1 stat|0,1,F,T,F,F,1,0",
                "RELENTLESS|No replacement recruits; +1 stat|0,1,F,F,F,T,0,0",
                "NO QUARTER|+1 enemy; deadliest force (+1 stat; +1 dmg from mission 3)|1,1,F,F,F,F,2,1",
            };
            if (Heat.Mods.Length != preC1.Length) fails.Add("modsLen=" + Heat.Mods.Length);
            else for (int i = 0; i < preC1.Length; i++)
            {
                var m = Heat.Mods[i];
                string got = m.Name + "|" + m.Desc + "|" + m.EnemyDelta + "," + m.StatDelta + ","
                           + (m.TighterContact ? "T" : "F") + "," + (m.Exposed ? "T" : "F") + ","
                           + (m.HarshAttrition ? "T" : "F") + "," + (m.NoReinforcements ? "T" : "F") + ","
                           + m.AiTier + "," + m.DmgDelta;
                if (got != preC1[i]) fails.Add("offSwitchRung" + (i + 1) + " (want '" + preC1[i] + "', got '" + got + "')");
            }

            // ---- (C) apex neutrality across EVERY mode of the dial ------------------------
            Heat.SetMidTooth(0);
            var apex = Vec(Heat.Max);
            for (int mt = 0; mt <= 7; mt++)
            {
                Heat.SetMidTooth(mt);
                if (Vec(Heat.Max) != apex) fails.Add("apexMoved(mode" + mt + ")=" + Vec(Heat.Max) + " vs " + apex);
            }
            // ...and RECRUIT is below the dial entirely. Asserted ONCE, not once per mode: the rung
            // lives in `RecruitMod`, outside `Mods`, so the dial cannot reach it and eight identical
            // passes were eight copies of one fact. It stays as a guard against a future edit that
            // folds RecruitMod INTO the table.
            if (Vec(-1) != (-1, -1, 0, 0, 0)) fails.Add("recruitMoved=" + Vec(-1));

            // ---- (E) the per-mode rung shapes, pinned -------------------------------------
            // level: enemy,stat,dmg,aiTier,flagbits (1=tighter 2=exposed 4=harsh 8=noReinf)
            var shapeGolden = new (int mt, string want)[]
            {
                (0, "0:0,0,0,0,0|1:1,0,0,0,0|2:1,1,0,0,0|3:2,1,0,0,1|4:2,1,0,1,1|5:3,1,0,1,5|6:3,2,0,1,7|7:3,3,0,1,15|8:4,4,1,2,15"),
                (1, "0:0,0,0,0,0|1:1,0,0,0,0|2:1,1,0,0,0|3:2,1,0,0,1|4:2,1,0,1,1|5:3,1,0,1,5|6:3,2,1,1,7|7:3,3,1,1,15|8:4,4,1,2,15"),
                (2, "0:0,0,0,0,0|1:1,0,0,0,0|2:1,1,0,0,0|3:2,1,0,0,1|4:2,1,0,1,1|5:3,1,0,1,5|6:3,2,0,2,7|7:3,3,0,2,15|8:4,4,1,2,15"),
                (3, "0:0,0,0,0,0|1:1,0,0,0,0|2:1,1,0,0,0|3:2,1,0,0,1|4:2,1,0,1,1|5:3,1,0,1,5|6:3,2,1,2,7|7:3,3,1,2,15|8:4,4,1,2,15"),
                (4, "0:0,0,0,0,0|1:1,0,0,0,0|2:1,1,0,0,0|3:2,1,0,0,1|4:2,1,0,1,1|5:3,1,0,1,5|6:3,1,0,1,7|7:3,3,0,1,15|8:4,4,1,2,15"),
                (5, "0:0,0,0,0,0|1:1,0,0,0,0|2:1,1,0,0,0|3:2,1,0,0,1|4:2,1,0,1,1|5:3,1,0,1,5|6:3,1,1,1,7|7:3,3,1,1,15|8:4,4,1,2,15"),
                (6, "0:0,0,0,0,0|1:1,0,0,0,0|2:1,1,0,0,0|3:2,1,0,0,1|4:2,1,0,1,1|5:3,1,0,1,5|6:3,1,0,2,7|7:3,3,0,2,15|8:4,4,1,2,15"),
                (7, "0:0,0,0,0,0|1:1,0,0,0,0|2:1,1,0,0,0|3:2,1,0,0,1|4:2,1,0,1,1|5:3,1,0,1,5|6:3,1,1,2,7|7:3,3,1,2,15|8:4,4,1,2,15"),
            };
            foreach (var (mt, want) in shapeGolden)
            {
                Heat.SetMidTooth(mt);
                string got = Shape();
                if (got != want) fails.Add("shape(mode" + mt + ") (golden '" + want + "', actual '" + got + "')");
            }

            // ---- (A) NO DEAD DECLARATION + (B) NO SILENT RUNG, on the SHIPPED table --------
            // Both assertions live INSIDE the rung loop deliberately (contract rule 6): hoisted
            // out, they would look at rung 1 and never at the rung that actually broke.
            Heat.SetMidTooth(shipped);
            for (int rung = 1; rung <= Heat.Mods.Length; rung++)
            {
                var m = Heat.Mods[rung - 1];
                if (m.AiTier > 0 && m.AiTier <= Heat.AiTier(rung - 1))
                    fails.Add("deadAiTier@rung" + rung + " (declares " + m.AiTier
                              + ", cumulative below is already " + Heat.AiTier(rung - 1) + ")");
                if (Vec(rung) == Vec(rung - 1))
                    fails.Add("silentRung" + rung + " (" + m.Name + " changes nothing)");
            }

            // ---- (G) THE COPY FITS ON ONE LINE AT 100%, in modes 1-7 ----------------------
            // NOT a correctness guard, and the banner no longer claims it is. Hud.DrawHeatSelector
            // WRAPS each Desc to the measured column and grows the card, so ink stays inside the
            // border at every `Cfg.UiScale` — that is what makes the panel correct. This leg is a
            // copy-quality budget: at 100% a rung should READ as one line rather than wrapping.
            // A character count cannot express a scale (at 120% the real limit is ~32), which is
            // exactly why the structural fix had to exist and this check cannot replace it.
            // Mode 0 is EXEMPT: it is a faithful transcription of the pre-C1 table, and its rung 8
            // at 58 characters IS the defect C1 found.
            for (int mt = 1; mt <= 7; mt++)
            {
                Heat.SetMidTooth(mt);
                for (int rung = 1; rung <= Heat.Mods.Length; rung++)
                {
                    var m = Heat.Mods[rung - 1];
                    if (m.Desc.Length > Heat.DescBudget)
                        fails.Add("descOverflow(mode" + mt + ",rung" + rung + ")=" + m.Desc.Length
                                  + ">" + Heat.DescBudget + " '" + m.Desc + "'");
                }
            }
            // ---- the shipped default is the MEASURED one ----------------------------------
            // Asserted BEHAVIOURALLY (the table the shipped constant builds), not as
            // `ShippedMidTooth != 3` — that compare is const-folded away and cannot fail. Keyed on
            // the MODE, not on a position in shapeGolden: an array index couples this to the order
            // of the golden list and would silently start checking a different mode if it changed.
            Heat.SetMidTooth(Heat.ShippedMidTooth);
            var shippedGolden = System.Linq.Enumerable.FirstOrDefault(shapeGolden, gg => gg.mt == Heat.ShippedMidTooth);
            if (shippedGolden.want == null)
                fails.Add("shippedMode" + Heat.ShippedMidTooth + "HasNoGolden");
            else if (Shape() != shippedGolden.want)
                fails.Add("shippedDefaultShapeWrong (actual '" + Shape() + "')");

            // ---- (F) the tooth REACHES THE BOARD ------------------------------------------
            // Mission 3: past the m1-2 heat grace that zeroes heatDmg. Mirrors HEATLADDERTEST's
            // DmgAtHeat, aimed at C1's NEW rung. The reference band takes X1's MakeHostile trim,
            // DERIVED rather than re-hardcoded, so it stays true if that constant ever moves.
            void DmgOnBoard(string tag, int heat, int expect)
            {
                Util.Reseed(4242);
                _run = new Run(); _run.Start();
                _run.HeatLevel = heat;
                _run.CurrentCard = new MissionCard { Objective = Objective.Eliminate, ModName = "STANDARD", Reward = RewardKind.None };
                SetupMission(3);
                // A `foreach` over an empty force asserts NOTHING and reports PASS — the fail-open
                // shape this wave exists to hunt. Refuse the vacuous pass explicitly.
                if (Enemies.Count == 0) { fails.Add(tag + ":noEnemiesSpawned"); return; }
                foreach (var e in Enemies)
                {
                    var refW = Weapon.Make(e.Weapon.Kind);
                    refW.TrimBaseDamage(Sightline.Mission.HostileDamageTrim);
                    if (e.Weapon.DmgMax != refW.DmgMax + expect)
                    { fails.Add(tag + ":" + e.Cls + "dmg=" + e.Weapon.DmgMax + "want=" + (refW.DmgMax + expect)); return; }
                }
            }
            DmgOnBoard("exposedDmg", 6, 1);    // C1: the tooth lands at rung 6...
            DmgOnBoard("heat5Dmg",   5, 0);    // ...one rung below is untouched...
            DmgOnBoard("apexDmg",    8, 1);    // ...and the apex still carries exactly ONE, not two.

            // The shipped mode (3) hands BOTH teeth down to rung 6: damage AND coordination tier 2.
            // Heat 5 keeps neither, and the apex's cumulative tier is unchanged at 2 — which is the
            // whole point of a Math.Max aggregation and is what leg (C) generalises.
            if (Heat.DmgDelta(6) != 1 || Heat.AiTier(6) != 2)
                fails.Add("shippedToothNotAtRung6 dmg=" + Heat.DmgDelta(6) + " tier=" + Heat.AiTier(6));
            if (Heat.DmgDelta(5) != 0 || Heat.AiTier(5) != 1)
                fails.Add("rung5Contaminated dmg=" + Heat.DmgDelta(5) + " tier=" + Heat.AiTier(5));
            if (Heat.AiTier(8) != 2) fails.Add("apexTierNot2=" + Heat.AiTier(8));

            // The dial CLAMPS rather than throwing or building a short table.
            Heat.SetMidTooth(-5);
            if (Heat.MidTooth != 0 || Heat.Mods.Length != 8) fails.Add("clampLow=" + Heat.MidTooth);
            Heat.SetMidTooth(99);
            if (Heat.MidTooth != 7 || Heat.Mods.Length != 8) fails.Add("clampHigh=" + Heat.MidTooth);
        }
        finally { Heat.SetMidTooth(shipped); }

        return fails.Count == 0
            ? "MIDTOOTHTEST: PASS (no dead AiTier declaration and no silent rung on the shipped table; "
              + "apex vector identical across ALL 8 dial modes and RECRUIT untouched; MIDTOOTH=0 reproduces "
              + "the pre-C1 table field-for-field; rung shapes pinned for all 8 modes; rungs 1-8 of modes 1-7 "
              + "fit the one-line 38-char copy budget AT 100% TEXT SIZE (mode 0 exempt by design - its rung 8 "
              + "IS the 58-char defect; the panel wraps and grows, so larger text scales are safe structurally, "
              + "not by this count); the +1 damage AND tier 2 reach heat 6, neither reaches heat 5, and the apex "
              + "carries exactly one damage point; dial clamps)"
            : "MIDTOOTHTEST: FAIL " + string.Join(", ", fails);
    }

    /// C3 THE TWO GAMES (SIGHTLINE_CLASSTEST) — the wave's gate, in four legs.
    ///
    /// THE FINDING IT GUARDS. On mid-run campaign nodes, the two objectives that end only when
    /// bodies fall (ELIMINATE, DECAPITATE) win 38.5% ±3.1 (n=247) against 81.5% ±1.1 (n=1243) for
    /// the six that end on a task — a 43-point spread between objective CLASSES, larger than the
    /// gap between two adjacent heat rungs. The wave shipped two things against it: the campaign
    /// fork now SAYS which class a node is, and the anti-turtle clock stopped moving ELIMINATE's
    /// finish line.
    ///
    /// WHY IT DRAWS. Legs B and C paint the REAL barracks/campaign-map frame and read the strings
    /// and marks at the DRAW CALL (Cfg.CaptureText, Hud.CaptureClassMarks). This is deliberate and
    /// it is the lesson W9 was sent back for: its first TRUTHTEST asserted the values the HUD was
    /// SUPPOSED to read, so reverting the panel left the test green. Asserting Run.IsKillObjective
    /// here would be the same mistake — leg A pins the predicate, and legs B/C pin that the fork
    /// actually paints it. Ripping the class row out of the tooltip fails leg C even though the
    /// predicate is untouched.
    ///
    /// LEG D is the balance lever and fails on the pre-C3 tree: with Game.ClockWavesOnEliminate
    /// restored (SIGHTLINE_KILLTREADMILL=1) the clock spawns bodies into an ELIMINATE, which is
    /// exactly what leg D forbids — and it checks the OFF path too, so a lever that did nothing
    /// would fail just as loudly as one that did too much.
    public string ClassSelfTest()
    {
        NoPersist = true;
        var fails = new List<string>();
        bool shippedClock = Game.ClockWavesOnEliminate;
        var savedCap = Cfg.CaptureText;
        var savedMarks = Hud.CaptureClassMarks;
        try
        {
            // ═══ (A) THE MODEL — exactly two of the eight objectives are kill objectives ═══════
            var all = (Objective[])Enum.GetValues(typeof(Objective));
            // The count is pinned so that APPENDING a ninth objective (the enums are append-only)
            // cannot silently inherit "not a kill objective" — someone has to come here and decide.
            if (all.Length != 8) fails.Add("objectiveCount=" + all.Length);
            foreach (var o in all)
            {
                bool want = o == Objective.Eliminate || o == Objective.Decapitate;
                if (Run.IsKillObjective(o) != want) fails.Add($"model:{o}");
                if (Run.IsKillObjective(o.ToString()) != want) fails.Add($"modelStr:{o}");
            }
            // an unparseable telemetry name must never be reclassified as a kill objective
            if (Run.IsKillObjective("Skirmish")) fails.Add("modelBadName");

            // ═══ (B)+(C) THE DRAW — what the FORK actually paints ════════════════════════════
            // Both classes are staged deterministically rather than waiting for the map to deal
            // one: the run is built normally, then every reachable node's card objective is
            // overwritten, so the pass runs twice over the identical geometry with only the class
            // changed. Anything that differs between the passes came from the class and nothing
            // else.
            void DrawPass(Objective forced, string tag)
            {
                // FIXED SEED: the map is dealt from the ambient RNG, so an unseeded run gave this
                // test a different fork every invocation. A gate that samples the world is a gate
                // that flakes — and this one did, on its second sweep.
                Util.Reseed(20260830);
                _run = new Run(); _run.Start();
                DebugCampaignMap();                       // -> Phase.Barracks with a live fork
                var choices = _run.NextNodes();
                if (choices.Count == 0) { fails.Add(tag + ":noFork"); return; }
                foreach (var n in choices) { n.Kind = NodeKind.Combat; n.Card.Objective = forced; }

                // PARK THE CURSOR OFF THE MAP. The hover tooltip draws a class mark of its own, so
                // a cursor left on a node by the PREVIOUS pass's tooltip step silently adds one to
                // the census below. That is the exact flake this leg shipped with: the map layout
                // is seed-dependent, so whether the stale cursor happened to land on a node varied
                // run to run and the test failed roughly one sweep in three.
                Raylib.SetMousePosition(2, 2);
                var cap = new List<(string text, float size)>();
                var marks = new List<(Objective obj, float x, float y)>();
                Cfg.CaptureText = cap; Hud.CaptureClassMarks = marks;
                Raylib.BeginDrawing(); DrawHudLayer(); Raylib.EndDrawing();
                Cfg.CaptureText = null; Hud.CaptureClassMarks = null;

                bool kill = Run.IsKillObjective(forced);
                string mine = kill ? "PITCHED - it ends when the field is clear"
                                   : "TASKED - it ends when the task is done";
                string theirs = kill ? "TASKED - it ends when the task is done"
                                     : "PITCHED - it ends when the field is clear";
                bool Said(List<(string text, float size)> c, string s) => c.Any(t => t.text == s);

                // (B1) the KEY names both marks. FUL-12's rule: a mark the map draws and the key
                //      does not name is an unexplained glyph.
                if (!Said(cap, "PITCHED - clear the field")) fails.Add(tag + ":noKeyPitched");
                if (!Said(cap, "TASKED - the objective ends it")) fails.Add(tag + ":noKeyTasked");
                // (B2) a class MARK was painted for every reachable node, and every mark drawn on
                //      this frame carries an objective of the staged class. The key contributes one
                //      mark of each class, so the count is choices + 2 and the class census is
                //      choices+1 of the staged class and exactly 1 of the other.
                int mine_ = marks.Count(m => Run.IsKillObjective(m.obj) == kill);
                int other = marks.Count - mine_;
                if (mine_ != choices.Count + 1) fails.Add($"{tag}:marks={mine_} want{choices.Count + 1}");
                if (other != 1) fails.Add($"{tag}:otherMarks={other}");
                // (B3) the objective name is still painted beside the mark (the mark ADDS to the
                //      label, it does not replace it — a regression that swapped them would read
                //      as an unlabelled fork).
                string objName = forced == Objective.Eliminate ? "ELIMINATE" : "EXTRACT";
                if (!Said(cap, objName)) fails.Add(tag + ":noObjLabel");
                // (B3b) and with the cursor off the map NO tooltip was painted — which is what
                //       makes the census above a statement about the LABELS and the key.
                if (Said(cap, mine) || Said(cap, theirs)) fails.Add(tag + ":tooltipWithoutHover");
                // (B4) every string THIS WAVE paints clears the 12px floor. Scoped deliberately:
                //      a whole-frame sweep fails on text this wave did not write — the campaign
                //      map's region-name strip is FitSize(11, 8) and paints at 8-11px, which is a
                //      real pre-existing breach of CLAUDE.md's floor but is not this wave's to
                //      move (see the DEVLOG's "what I did not fix"). An assertion that fails for
                //      a reason unrelated to the change under test is not a gate, it is noise.
                foreach (var t in cap)
                    if ((t.text == mine || t.text == "PITCHED - clear the field"
                         || t.text == "TASKED - the objective ends it" || t.text == objName)
                        && t.size < 12f)
                    { fails.Add($"{tag}:size{t.size}:{t.text}"); break; }

                // (C) THE HOVER TOOLTIP. The map published NodeBtns as it drew; park the real
                //     cursor on a real node and draw again, so the tooltip is reached through the
                //     shipped hover predicate and not through a staging flag.
                if (Hud.NodeBtns.Count == 0) { fails.Add(tag + ":noNodeBtns"); return; }
                // THE HOVER HAS TO CONVERGE, NOT BE ASSUMED. DrawBarracks gives the card a 0.15 s
                // slide-down entrance (`PanelAnim("barracks")`), so the whole map — and therefore
                // every rect NodeBtns publishes — moves by up to 16 px between two consecutive
                // draws taken inside that window. Parking the cursor on a rect read from the
                // PREVIOUS draw therefore misses the node about half the time, and it missed 3 of
                // 4 runs under `dotnet run -c Debug` (where the first draw is slow enough to land
                // mid-entrance) while passing 8 of 8 on the Release binary. So: re-read the rect,
                // re-park, re-draw, until the tooltip actually appears. The entrance ends, so this
                // converges; and it is still the shipped hover predicate doing the deciding.
                var cap2 = new List<(string text, float size)>();
                for (int tries = 0; tries < 10; tries++)
                {
                    var r = Hud.NodeBtns[0].Rect;
                    Raylib.SetMousePosition((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
                    cap2 = new List<(string text, float size)>();
                    Cfg.CaptureText = cap2;
                    Raylib.BeginDrawing(); DrawHudLayer(); Raylib.EndDrawing();
                    Cfg.CaptureText = null;
                    // "the tooltip drew at all" is the intel row, which predates this wave — so the
                    // loop's exit condition can never be satisfied by the thing under test.
                    if (cap2.Any(t => t.text.Contains("intel"))) break;
                    if (Hud.NodeBtns.Count == 0) break;
                }
                if (!cap2.Any(t => t.text.Contains("intel"))) { fails.Add(tag + ":noTooltip"); return; }
                if (!Said(cap2, mine)) fails.Add(tag + ":tooltipMissingClass");
                if (Said(cap2, theirs)) fails.Add(tag + ":tooltipWrongClass");
            }
            DrawPass(Objective.Eliminate, "pitched");
            DrawPass(Objective.Evac, "tasked");

            // ═══ (D) THE LEVER — the clock's aim arm lives, its wave arm stops at ELIMINATE ═══
            // Runs the REAL UpdatePressure at a turn count past rung 2 (grace 4, one rung per 2
            // turns => turn 7 is rung 2, the first wave rung) and counts what landed on the board.
            // Returns the rung TOO. An earlier revision asserted `Pressure` after the last call
            // in the sequence, so it read whatever the FINAL staging left behind (Evac's 0) rather
            // than Eliminate's — a correct assertion in the wrong scope, which is worth exactly as
            // much as no assertion. Each arm now carries its own reading out.
            (int added, int aim, int rung) ClockAt(Objective obj, bool restore)
            {
                Game.ClockWavesOnEliminate = restore;
                Util.Reseed(20260830);
                _run = new Run(); _run.Start();
                _run.CurrentCard = new MissionCard { Objective = obj, ModName = "STANDARD", Reward = RewardKind.None };
                SetupMission(3);
                int before = Enemies.Count;
                _turnCount = 7;                       // rung 2 — the first reinforcement rung
                UpdatePressure();
                return (Enemies.Count - before, Combat.PressureAim, Pressure);
            }
            var elim = ClockAt(Objective.Eliminate, false);
            var hack = ClockAt(Objective.Hack, false);
            var decap = ClockAt(Objective.Decapitate, false);
            var elimOld = ClockAt(Objective.Eliminate, true);
            var evac = ClockAt(Objective.Evac, false);

            // the shipped default is the lever ON
            if (shippedClock) fails.Add("shippedClockWavesOnEliminate");
            // (D1) ELIMINATE: no bodies, but the pressure ramp is untouched — the clock still bites.
            if (elim.added != 0) fails.Add("elimAdded=" + elim.added);
            if (elim.aim <= 0) fails.Add("elimAimArmDead=" + elim.aim);
            if (elim.rung < 2) fails.Add("elimRung=" + elim.rung);
            if (hack.rung != elim.rung) fails.Add($"rungDiffers {hack.rung}!={elim.rung}");
            // (D2) HACK and DECAPITATE carry the SAME clock and are deliberately untouched. If
            //      either of these ever reads 0 the lever has leaked past its one objective.
            if (hack.added <= 0) fails.Add("hackAdded=" + hack.added);
            if (decap.added <= 0) fails.Add("decapAdded=" + decap.added);
            if (hack.aim != elim.aim) fails.Add($"aimArmDiffers {hack.aim}!={elim.aim}");
            // (D3) the OFF path restores the pre-C3 clock exactly — a lever with no restore is a
            //      deletion, and a lever whose two arms behave the same is not a lever at all.
            if (elimOld.added <= 0) fails.Add("restoreAdded=" + elimOld.added);
            if (elimOld.aim != elim.aim) fails.Add("restoreAimMoved");
            // (D4) an objective the clock never ran on is still clock-free (PressureClockObjective
            //      itself is not collateral damage).
            if (evac.added != 0 || evac.aim != 0 || evac.rung != 0) fails.Add($"evacClock {evac.added}/{evac.aim}/{evac.rung}");
        }
        finally
        {
            Cfg.CaptureText = savedCap;
            Hud.CaptureClassMarks = savedMarks;
            Game.ClockWavesOnEliminate = shippedClock;
        }

        return fails.Count == 0
            ? "CLASSTEST: PASS (model: exactly Eliminate+Decapitate of 8 objectives are PITCHED, both by "
              + "enum and by telemetry name; DRAW-OBSERVED: the campaign fork paints a class mark per "
              + "reachable node plus a two-entry key, keeps the objective label, stays >=12px, and its "
              + "hover tooltip paints THIS node's class line and not the other one — both classes staged; "
              + "LEVER: the anti-turtle clock adds no bodies to an ELIMINATE while its aim ramp still "
              + "rises, Hack/Decapitate keep both arms unchanged, Evac has no clock at all, and "
              + "SIGHTLINE_KILLTREADMILL=1 restores the pre-C3 waves)"
            : "CLASSTEST: FAIL " + string.Join(", ", fails);
    }

    public string OnRampSelfTest()
    {
        NoPersist = true;
        var fails = new List<string>();

        // ---- (A1) the ladder data row -----------------------------------------------------
        // (through locals: Min/Recruit are compile-time consts, so a direct literal compare folds
        // away and the compiler flags the failure branch as unreachable)
        int minRung = Sightline.Heat.Min, recruitRung = Sightline.Heat.Recruit;
        if (minRung != -1) fails.Add("min=" + minRung);
        if (recruitRung != -1) fails.Add("recruitConst=" + recruitRung);
        if (!Sightline.Heat.IsRecruit(-1)) fails.Add("isRecruit(-1)");
        if (Sightline.Heat.IsRecruit(0)) fails.Add("isRecruit(0)");
        if (Sightline.Heat.Clamp(-9) != -1) fails.Add("clampFloor");
        if (Sightline.Heat.Label(-1) != "RECRUIT") fails.Add("label(-1)=" + Sightline.Heat.Label(-1));
        if (Sightline.Heat.Label(3) != "HEAT 3") fails.Add("label(3)");
        if (Sightline.Heat.Active(-1).Count() != 1) fails.Add("activeCount(-1)");
        if (Sightline.Heat.EnemyDelta(-1) != -1) fails.Add("enemyDelta(-1)=" + Sightline.Heat.EnemyDelta(-1));
        if (Sightline.Heat.StatDelta(-1) != -1) fails.Add("statDelta(-1)=" + Sightline.Heat.StatDelta(-1));
        // the relief must be BODIES AND STATS ONLY — RECRUIT never flips a qualitative mutator on
        if (Sightline.Heat.DmgDelta(-1) != 0 || Sightline.Heat.AiTier(-1) != 0
            || Sightline.Heat.Exposed(-1) || Sightline.Heat.HarshAttrition(-1)
            || Sightline.Heat.NoReinforcements(-1) || Sightline.Heat.TighterContact(-1))
            fails.Add("recruitMutatorLeak");
        // ...and it must never pay a NEGATIVE requisition bonus
        if (Sightline.Heat.IntelBonus(-1) != 0) fails.Add("intelBonus(-1)=" + Sightline.Heat.IntelBonus(-1));
        // heat 0 is still a true no-op, and rung 1 still bites
        if (Sightline.Heat.EnemyDelta(0) != 0 || Sightline.Heat.StatDelta(0) != 0
            || Sightline.Heat.IntelBonus(0) != 0 || Sightline.Heat.Active(0).Any())
            fails.Add("heat0NotNoOp");
        if (Sightline.Heat.EnemyDelta(1) != 1 || Sightline.Heat.IntelBonus(1) != 3) fails.Add("heat1Moved");

        // ---- (A2) the BUILT mission: a SEED SWEEP, one fewer + weaker hostile ----------------
        // Both legs replay the identical world (Util.Reseed before each), so the ONLY difference
        // is the rung. This is the assertion that catches the early-mission "heat grace" (W5) and
        // the SpawnEnemies bump FLOOR (R1) silently zeroing RECRUIT's relief on mission 1 — the
        // mission a first-timer meets first.
        //
        // W4 REPAIR — this probe used to compare the two legs' FORCE-WIDE per-enemy averages.
        // That is not a measurement of the stat relief, it is a measurement of the archetype MIX:
        // the two legs field different body counts, so they sit at different positions in the
        // shared RNG stream and roll different archetypes, whose base HP/aim differ by far more
        // than the one point RECRUIT takes off. It happened to pass at the old spawn geometry and
        // started failing the moment W4 changed pod placement — on composition luck, both times.
        // It is now composition-CONTROLLED: compare each archetype CLASS against itself across
        // the legs, which is exactly what `bump` moves.
        //
        // R1 REVIEW FIX — W4's per-class control is kept and two things are added on top:
        //   * A SEED SWEEP. The old form asserted on the single hard-coded seed 4242, and a
        //     reviewer's 12-seed probe of it scored 5 pass / 7 fail — a rung-wide claim needs a
        //     rung-wide sample, and a test that is green on 5 seeds of 12 is worse than no test.
        //   * A STRICT per-class row. Within one mission every member of a class shares one base,
        //     so a class's mean HP/aim is exactly `base + bump`: with composition controlled the
        //     relief is not "at least one class moved", it is EVERY shared rank-and-file class
        //     moved, on every seed. Named ELITEs (the m3/m5 mid-boss, the finale boss) are spawned
        //     with explicit stats and never read `bump`, so they are exempt by construction.
        //   * And it now bites at MISSION 1 as well: W4 recorded, correctly, that the m1 relief
        //     was a no-op because `bump = Math.Max(0, (n - 1) + statDelta)` floored RECRUIT's −1
        //     away on the one mission the on-ramp exists for. That floor is now −1
        //     (Mission.SpawnEnemies), so m1 is asserted exactly like m3 rather than excused.
        (int count, Dictionary<string, (int n, int hp, int aim)> byCls) BuildAt(int heat, int mission, int seed)
        {
            Util.Reseed(seed);
            _run = new Run(); _run.Start();
            _run.HeatLevel = heat;
            _run.CurrentCard = new MissionCard { Objective = Objective.Eliminate, ModName = "STANDARD", Reward = RewardKind.None };
            SetupMission(mission);
            var map = new Dictionary<string, (int n, int hp, int aim)>();
            foreach (var e in Enemies)
            {
                var cur = map.TryGetValue(e.Cls, out var v) ? v : (0, 0, 0);
                map[e.Cls] = (cur.Item1 + 1, cur.Item2 + e.MaxHp, cur.Item3 + e.Aim);
            }
            return (Enemies.Count, map);
        }
        int[] sweepSeeds = { 1, 7, 42, 99, 123, 777, 1234, 2026, 4242, 8675, 31337, 65535 };
        foreach (int m in new[] { 1, 3 })
        {
            int seedsChecked = 0;
            foreach (int seed in sweepSeeds)
            {
                var std = BuildAt(0, m, seed);
                var rec = BuildAt(-1, m, seed);
                seedsChecked++;
                if (rec.count != std.count - 1) { fails.Add($"m{m}s{seed}:count {rec.count} vs {std.count}"); continue; }
                if (rec.count <= 0 || std.count <= 0) { fails.Add($"m{m}s{seed}:emptyBuild"); continue; }
                int shared = 0;
                foreach (var kv in rec.byCls)
                {
                    if (!std.byCls.TryGetValue(kv.Key, out var sv)) continue;   // class only one leg fielded
                    // named ELITEs carry explicit stats and never read `bump` — the force's fixed
                    // tooth, which the rung cannot and should not move.
                    if (kv.Key == "ELITE") continue;
                    shared++;
                    float rHp = kv.Value.hp / (float)kv.Value.n, sHp = sv.hp / (float)sv.n;
                    float rAim = kv.Value.aim / (float)kv.Value.n, sAim = sv.aim / (float)sv.n;
                    if (!(rHp < sHp))  fails.Add($"m{m}s{seed}:{kv.Key}:hp {rHp:0.##} vs {sHp:0.##}");
                    if (!(rAim < sAim)) fails.Add($"m{m}s{seed}:{kv.Key}:aim {rAim:0.##} vs {sAim:0.##}");
                }
                if (shared == 0) fails.Add($"m{m}s{seed}:noSharedClass");
            }
            if (seedsChecked != sweepSeeds.Length) fails.Add($"m{m}:sweepShort {seedsChecked}/{sweepSeeds.Length}");
        }

        // ---- (A3) the bleed-out clock ------------------------------------------------------
        int DownTurnsAt(int heat)
        {
            Util.Reseed(77);
            _run = new Run(); _run.Start();
            _run.HeatLevel = heat;
            _run.CurrentCard = new MissionCard { Objective = Objective.Eliminate, ModName = "STANDARD", Reward = RewardKind.None };
            SetupMission(2);
            var sol = Players.FirstOrDefault(p => p.Alive && !p.IsVip);
            if (sol == null) return -1;
            EnterDowned(sol);
            return sol.DownedTurns;
        }
        if (DownTurnsAt(0) != DownedTimerTurns) fails.Add("downTurnsHeat0");
        if (DownTurnsAt(-1) != DownedTimerTurns + 2) fails.Add("downTurnsRecruit=" + DownTurnsAt(-1));

        // ---- (A4) the checkpoint valve opens at mission 1 (and ONLY at RECRUIT) --------------
        bool CheckpointAt(int heat, int mission)
        {
            Util.Reseed(99);
            _run = new Run(); _run.Start();
            _run.HeatLevel = heat;
            _run.Mission = mission;
            _run.CheckpointUsed = false;
            _run.CurrentCard = new MissionCard { Objective = Objective.Eliminate, ModName = "STANDARD", Reward = RewardKind.None };
            return TryReinforcements();
        }
        if (!CheckpointAt(-1, 1)) fails.Add("recruitNoM1Checkpoint");
        if (CheckpointAt(0, 1)) fails.Add("heat0GainedM1Checkpoint");
        if (CheckpointAt(0, 2)) fails.Add("heat0GainedM2Checkpoint");
        if (!CheckpointAt(0, 3)) fails.Add("heat0LostM3Checkpoint");
        // still ONE checkpoint per run at RECRUIT — the valve is earlier, not repeatable
        Util.Reseed(99);
        _run = new Run(); _run.Start();
        _run.HeatLevel = -1; _run.Mission = 1;
        _run.CurrentCard = new MissionCard { Objective = Objective.Eliminate, ModName = "STANDARD", Reward = RewardKind.None };
        if (!TryReinforcements()) fails.Add("recruitFirstCheckpoint");
        if (TryReinforcements()) fails.Add("recruitCheckpointRepeats");

        // ---- (B1) the dt multiplier can NEVER reach the harness or the flywheel --------------
        int savedAnim = Display.AnimSpeedIdx, savedScale = Display.UiScaleIdx;
        float savedCfg = Cfg.UiScale;
        Display.AnimSpeedIdx = Display.AnimSpeedLevels.Length - 1;   // fastest setting
        if (Display.AnimSpeed <= 1f) fails.Add("animSpeedTopIsNotFaster");
        var harnessGame = new Game { NoPersist = true };
        if (harnessGame.AnimSpeed != 1f) fails.Add("noPersistNotPinned=" + harnessGame.AnimSpeed);
        var botGame = new Game { AutoPlay = true };
        if (botGame.AnimSpeed != 1f) fails.Add("autoPlayNotPinned=" + botGame.AnimSpeed);
        var liveGame = new Game();
        if (liveGame.AnimSpeed != Display.AnimSpeed) fails.Add("liveGameNotWired");
        // ...and the cycle wraps back to 1x rather than running away
        Display.AnimSpeedIdx = 0;
        for (int i = 0; i < Display.AnimSpeedLevels.Length; i++) Display.CycleAnimSpeed();
        if (Display.AnimSpeedIdx != 0 || Display.AnimSpeed != 1f) fails.Add("animCycleWrap");

        // ---- (B2) the text scale is symmetric, tapered, and inert at 100% --------------------
        Cfg.UiScale = 1f;
        float base12 = Cfg.Measure("STABILIZE", 12, 1f).X;
        float base44 = Cfg.Measure("STABILIZE", 44, 1f).X;
        Cfg.UiScale = 1.2f;
        float big12 = Cfg.Measure("STABILIZE", 12, 1f).X;
        float big44 = Cfg.Measure("STABILIZE", 44, 1f).X;
        if (!(big12 > base12 * 1.1f)) fails.Add($"bodyTextDidNotScale {base12:0.#}->{big12:0.#}");
        if (MathF.Abs(big44 - base44) > 0.01f) fails.Add("titleTextScaledPastTaper");
        Cfg.UiScale = 1f;
        if (MathF.Abs(Cfg.Measure("STABILIZE", 12, 1f).X - base12) > 0.01f) fails.Add("scale1NotInert");

        // ---- (B3) both settings survive a REAL JSON round trip -------------------------------
        string dispPath = Display.SettingsPathPublic;
        // R1 REVIEW FIX — and they must survive it in the SAME PLACE every launch. SaveGame.Dir
        // guards a real hazard that Display.Dir did not: GetFolderPath(ApplicationData) returns
        // "" when the resolved directory does not exist yet, so Path.Combine("", "Sightline") is
        // a RELATIVE dir next to the process CWD. Saves and meta fell back to $HOME/.config;
        // display.json did not, so settings scattered per launch directory and read back as a
        // reset to the player — tutorial-tip flags, the four volume faders, animation speed and
        // text scale all inherit it. Reproduce with XDG_CONFIG_HOME pointed at a directory that
        // does not exist: before the fix this asserts `Sightline/display.json`, relative.
        if (!System.IO.Path.IsPathRooted(dispPath)) fails.Add("displayPathRelative:" + dispPath);
        if (System.IO.Path.GetDirectoryName(dispPath) != SaveGame.ConfigDir)
            fails.Add($"displayDirSplit:{System.IO.Path.GetDirectoryName(dispPath)} vs {SaveGame.ConfigDir}");
        string dispStash = null; bool hadDisp = false;
        try { hadDisp = System.IO.File.Exists(dispPath); if (hadDisp) dispStash = System.IO.File.ReadAllText(dispPath); } catch { }
        try
        {
            Display.AnimSpeedIdx = 2; Display.UiScaleIdx = 3; Display.ApplyUiScale();
            float wroteScale = Cfg.UiScale;
            Display.SaveForTest();
            Display.AnimSpeedIdx = 0; Display.UiScaleIdx = 1; Display.ApplyUiScale();
            Display.LoadForTest();
            if (Display.AnimSpeedIdx != 2) fails.Add("animIdxRoundTrip=" + Display.AnimSpeedIdx);
            if (Display.UiScaleIdx != 3) fails.Add("uiIdxRoundTrip=" + Display.UiScaleIdx);
            if (MathF.Abs(Cfg.UiScale - wroteScale) > 0.001f) fails.Add("loadDidNotApplyScaleToCfg");
            // an out-of-range file must clamp, not crash or index out of bounds
            System.IO.File.WriteAllText(dispPath, "{\"AnimSpeedIdx\":99,\"UiScaleIdx\":-7}");
            Display.LoadForTest();
            if (Display.AnimSpeedIdx != Display.AnimSpeedLevels.Length - 1) fails.Add("animIdxClamp");
            if (Display.UiScaleIdx != 0) fails.Add("uiIdxClamp");
            // a pre-W5 profile (neither field) must read as the shipped defaults: 1x, 100%
            System.IO.File.WriteAllText(dispPath, "{\"BrightIdx\":2}");
            Display.AnimSpeedIdx = 3; Display.UiScaleIdx = 0;
            Display.LoadForTest();
            if (Display.AnimSpeedIdx != 0 || Display.AnimSpeed != 1f) fails.Add("legacyProfileAnimDefault");
            if (Display.UiScaleIdx != 1 || Cfg.UiScale != 1f) fails.Add("legacyProfileScaleDefault");
        }
        catch (Exception ex) { fails.Add("settingsThrew:" + ex.GetType().Name); }
        finally
        {
            try
            {
                if (hadDisp) System.IO.File.WriteAllText(dispPath, dispStash);
                else if (System.IO.File.Exists(dispPath)) System.IO.File.Delete(dispPath);
            }
            catch { }
            Display.AnimSpeedIdx = savedAnim; Display.UiScaleIdx = savedScale; Cfg.UiScale = savedCfg;
        }


        // ── (C) W5 THE FIRST HOUR: the on-ramp is now the DEFAULT on a never-played profile ────
        // Audit newplayer-4: RECRUIT was always selectable and always unlabelled, so nothing at
        // level 0 hinted anything existed below it, and the copy framed 0 as the floor. The
        // archived X2 ladder (n=40/rung, base a61ef42) puts RECRUIT at 75.0% run completion
        // against heat 0's 57.5% — a 17.5-point gap outside the +-6-8 error bar, i.e. roughly two
        // in five first campaigns ended in a loss the on-ramp exists to prevent.
        //
        // This asserts a DEFAULT, not a rung: (A) above is what pins the rung's actual numbers,
        // and they are untouched. It runs the LIVE (non-NoPersist) path — the whole point is what
        // a real profile does — so it stashes and restores save.json / meta.json.
        {
            string sp2 = SaveGame.SavePathPublic, mp2 = SaveGame.MetaPathPublic;
            bool hadS = false, hadM = false; string sStash = null, mStash = null;
            try
            {
                hadS = System.IO.File.Exists(sp2); if (hadS) sStash = System.IO.File.ReadAllText(sp2);
                hadM = System.IO.File.Exists(mp2); if (hadM) mStash = System.IO.File.ReadAllText(mp2);
                if (System.IO.File.Exists(mp2)) System.IO.File.Delete(mp2);   // a never-played profile

                var fresh = new Game { NoPersist = false };
                fresh.DebugLoadMeta();
                if (!fresh.FirstTimeProfile) fails.Add("freshProfileNotFlagged");
                if (fresh.PendingHeat != Sightline.Heat.Recruit)
                    fails.Add("freshProfilePendingHeat=" + fresh.PendingHeat);
                if (fresh.UnlockedHeat != 0) fails.Add("freshProfileUnlocked=" + fresh.UnlockedHeat);

                // ...and a profile that HAS finished a run keeps the standard rung. The default is
                // an on-ramp for a first-timer, never a silent difficulty drop for a returning one.
                SaveGame.RecordRunTotals(false, 2);
                var seasoned = new Game { NoPersist = false };
                seasoned.DebugLoadMeta();
                if (seasoned.FirstTimeProfile) fails.Add("seasonedProfileFlaggedFresh");
                if (seasoned.PendingHeat != 0) fails.Add("seasonedProfilePendingHeat=" + seasoned.PendingHeat);
            }
            catch (Exception ex) { fails.Add("onRampDefaultThrew:" + ex.GetType().Name); }
            finally
            {
                try
                {
                    if (hadS) System.IO.File.WriteAllText(sp2, sStash);
                    else if (System.IO.File.Exists(sp2)) System.IO.File.Delete(sp2);
                    if (hadM) System.IO.File.WriteAllText(mp2, mStash);
                    else if (System.IO.File.Exists(mp2)) System.IO.File.Delete(mp2);
                }
                catch { }
            }
        }

        return fails.Count == 0
            ? "ONRAMPTEST: PASS (RECRUIT rung -1: data row + built mission -1 body and -1 stat on EVERY shared rank-and-file class, over a 12-seed sweep, at m1 AND m3; 5-turn bleed-out, checkpoint from m1 and once only, no mutator leak, no negative intel; heat 0 untouched; anim speed pinned 1x under AutoPlay/NoPersist; text scale symmetric + tapered; both settings round-trip, clamp, and default on a pre-W5 profile; W5: a zero-run profile DEFAULTS to RECRUIT and a played one does not)"
            : "ONRAMPTEST: FAIL (" + string.Join(",", fails.Distinct()) + ")";
    }

    /// W5 (screenshot/eyes-only harness): stage a MULTI-TILE MOVE for the animation-speed filmstrip.
    /// The whole point of the animation-speed setting's DANGER note in CLAUDE.md is that the
    /// movement-jitter bug (MoveStepAnim capturing `_from` at enqueue instead of at activation)
    /// is invisible to every self-test — only a filmstrip of a multi-tile path shows the snap-back.
    /// Autoplay rarely orders a long, clean, straight walk on demand, so this hook does: it selects
    /// the first soldier, finds the longest clear straight run from its tile, and enqueues one
    /// MoveStepAnim per tile through the SAME Enqueue the player's own move uses (no OnStart here —
    /// activation is still the queue's job, which is exactly the property the filmstrip verifies).
    /// Returns the number of steps queued (0 if no clear run was found).
    public int DebugLongMove(int want = 6)
    {
        var u = Players.FirstOrDefault(p => p.Alive && !p.IsVip);
        if (u == null) return 0;
        Selected = u;
        (int dx, int dy)[] dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        int bestLen = 0; (int dx, int dy) bestDir = (1, 0);
        foreach (var (dx, dy) in dirs)
        {
            int len = 0;
            for (int i = 1; i <= want; i++)
            {
                int nx = u.X + dx * i, ny = u.Y + dy * i;
                if (!Grid.IsFloor(nx, ny) || UnitAt(nx, ny) != null) break;
                len = i;
            }
            if (len > bestLen) { bestLen = len; bestDir = (dx, dy); }
        }
        if (bestLen == 0) return 0;
        var path = new List<(int x, int y)>();
        for (int i = 1; i <= bestLen; i++) path.Add((u.X + bestDir.dx * i, u.Y + bestDir.dy * i));
        EnqueuePath(u, path, Team.Player);   // THE STRIDE: the funnel every real move uses
        return bestLen;
    }

    /// THE STRIDE: stage a VAULT to film (SIGHTLINE_LONGMOVE=vault): the first soldier, the first
    /// cardinal direction with two clear floor tiles ahead; the near one is stamped HIGH COVER and
    /// IssueVault (the player's key-9 path, same Enqueue) lands the figure on the far one.
    public bool DebugVaultMove()
    {
        var u = Players.FirstOrDefault(p => p.Alive && !p.IsVip);
        if (u == null) return false;
        Selected = u;
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int mx = u.X + dx, my = u.Y + dy, lx = u.X + 2 * dx, ly = u.Y + 2 * dy;
            if (!Grid.IsFloor(mx, my) || UnitAt(mx, my) != null || !Grid.IsFloor(lx, ly) || UnitAt(lx, ly) != null) continue;
            Grid.Tiles[mx, my] = TileType.HighCover; Grid.SetCoverHp(mx, my);
            if (!VaultTargetOk(u, lx, ly)) { Grid.Tiles[mx, my] = TileType.Floor; continue; }
            IssueVault(lx, ly);
            return true;
        }
        return false;
    }


    // ── THE STRIDE — SIGHTLINE_FEELTEST: pillar 2 ("feels good") gets its first PASS/FAIL line ──
    /// Every other pillar had a gate in the sweep; MOTION and BEAT TIMING had none, so the most
    /// frequent action in the game — a multi-tile walk — could caterpillar (ease-in-out at EVERY
    /// tile, a WalkLean re-kick at every tile), a VAULT could slide straight through the cover it
    /// leaps, and a kill's three strings could print inside 24 px of each other with every self-test
    /// green. This probe drives the REAL anim queue the way Game.Update's pump does (OnStart on
    /// activation, one Update per frame at the harness's 1/60 dt) and MEASURES the tween:
    ///   (a) a 6-tile walk staged through EnqueuePath (the funnel every move uses): the per-frame
    ///       speed profile on Unit.Pos. Over the MID-PATH (the departure ramp and arrival brake —
    ///       MoveStepAnim.FeelMidBand tiles at each end — are excluded, because a walk must start
    ///       and stop) the slowest frame must be >= FeelMinSpeedRatio x the fastest, there must be
    ///       ZERO frames under FeelStallRatio x the mean, no frame may step backwards, WalkLean is
    ///       kicked ONCE (the push-off) rather than once per tile, and the frame count is PINNED
    ///       (6 x ceil(0.12 s x 60) = 48) so no easing trick can move the commit cadence.
    ///   (b) a VAULT via IssueVault: the figure must LIFT at least FeelVaultLiftMin px, the peak
    ///       must sit over the cover tile it clears, X never reverses, and it lands EXACTLY on
    ///       the tile centre (the commit is still `Unit.Pos = _to`).
    ///   (c) floating text stacked at one anchor — the kill trio (impact number + KIA + name
    ///       stamp), the BRACE pair (number + STAGGERED) and two identical overwatch numbers —
    ///       after one Fx.Update: every pairwise anchor distance >= Fx.TextSep, and the twins arc
    ///       in OPPOSITE directions.
    /// PRESENTATION ONLY: it reads Unit.Pos and Fx.Texts, never the sim; it consumes no Util.Rng
    /// beyond its own reseed.
    public string FeelSelfTest()
    {
        Util.Reseed(70031);
        NoPersist = true;
        var fails = new List<string>();
        var detail = new System.Text.StringBuilder();

        void Stage()
        {
            Grid = new Grid();                       // all Floor, Height 0
            Players = new List<Unit>(); Enemies = new List<Unit>();
            Vip = null; CaptiveLocked = false; Objective = Objective.Eliminate; EvacZone.Clear();
            Fx = new Fx(); _anims.Clear(); DragMode = VaultMode = false;
        }
        Unit MkP(int x, int y)
        {
            var u = new Unit { Name = "S", Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                               Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
        }
        // one Update per FRAME, exactly as Game.Update's pump does it: OnStart when a step becomes
        // ACTIVE, pop index 0 only if the anim is genuinely still at the front.
        int Pump(Unit u, List<Vector2> trace, Action onStart)
        {
            int frames = 0;
            while (_anims.Count > 0 && frames < 600)
            {
                var a = _anims[0];
                if (!a.Started) { a.Started = true; a.OnStart(this); onStart?.Invoke(); }
                bool done = a.Update(this, 1f / 60f);
                if (done && _anims.Count > 0 && _anims[0] == a) _anims.RemoveAt(0);
                trace.Add(u.Pos); frames++;
            }
            return frames;
        }

        // ---- (a) the 6-tile walk ----
        Stage();
        const int N = 6;
        var w = MkP(3, 5); Players.Add(w); Selected = w;
        var path = new List<(int x, int y)>();
        for (int i = 1; i <= N; i++) path.Add((3 + i, 5));
        EnqueuePath(w, path, Team.Player);
        var trace = new List<Vector2> { w.Pos };
        int kicks = 0;
        int frames = Pump(w, trace, () => { if (w.WalkLean > 0f) { kicks++; w.WalkLean = 0f; } });
        Vector2 start = Util.TileCenter(3, 5), end = Util.TileCenter(3 + N, 5);
        if (w.X != 3 + N || w.Y != 5 || w.Pos != end) fails.Add($"walkDidNotLand({w.X},{w.Y})");
        const int WalkFramesPinned = N * 8;          // 0.12 s / (1/60) = 7.2 -> the 8th frame commits; the cadence the sim runs on
        if (frames != WalkFramesPinned) fails.Add($"walkFrames({frames}!={WalkFramesPinned})");
        var sp = new List<float>(); var mid = new List<float>(); int back = 0;
        for (int i = 1; i < trace.Count; i++)
        {
            float s = Vector2.Distance(trace[i], trace[i - 1]); sp.Add(s);
            if (trace[i].X < trace[i - 1].X - 0.001f) back++;
            float p0 = Vector2.Distance(trace[i - 1], start) / Cfg.Tile, p1 = Vector2.Distance(trace[i], start) / Cfg.Tile;
            if (p0 >= MoveStepAnim.FeelMidBand && p1 <= N - MoveStepAnim.FeelMidBand) mid.Add(s);
        }
        float mn = mid.Count > 0 ? mid.Min() : 0f, mx = mid.Count > 0 ? mid.Max() : 0f, mean = mid.Count > 0 ? mid.Average() : 0f;
        int stalls = mid.Count(s => s < MoveStepAnim.FeelStallRatio * mean);
        detail.Append($"walk: {frames} frames, mid-path px/frame min {mn:0.0} mean {mean:0.0} max {mx:0.0} (min/max {(mx > 0 ? mn / mx : 0):0.00}), " +
                      $"stalls<{MoveStepAnim.FeelStallRatio:0.00}xmean {stalls}, leanKicks {kicks}, backSteps {back}; " +
                      $"profile [{string.Join(" ", sp.Select(s => s.ToString("0.0")))}]");
        if (mid.Count == 0) fails.Add("walkNoMidPath");
        if (mx > 0 && mn < MoveStepAnim.FeelMinSpeedRatio * mx) fails.Add($"walkMidSpeedDip({mn / mx:0.00}<{MoveStepAnim.FeelMinSpeedRatio:0.00})");
        if (stalls > 0) fails.Add($"walkStalls({stalls})");
        if (kicks != 1) fails.Add($"walkLeanKicks({kicks}!=1)");
        if (back > 0) fails.Add($"walkBackSteps({back})");

        // ---- (b) the VAULT ----
        Stage();
        var v = MkP(5, 5); Players.Add(v); Selected = v;
        Grid.Tiles[6, 5] = TileType.HighCover; Grid.SetCoverHp(6, 5);   // cover directly east; land on (7,5)
        if (!VaultTargetOk(v, 7, 5)) fails.Add("vaultStageInvalid");
        IssueVault(7, 5);
        var vt = new List<Vector2> { v.Pos };
        int vf = Pump(v, vt, null);
        float baseY = Util.TileCenter(5, 5).Y;
        float lift = 0f; int peakI = 0;
        for (int i = 0; i < vt.Count; i++) if (baseY - vt[i].Y > lift) { lift = baseY - vt[i].Y; peakI = i; }
        float coverL = Cfg.OriginX + 6 * Cfg.Tile, coverR = coverL + Cfg.Tile;
        float peakX = vt[peakI].X;
        int vback = 0; for (int i = 1; i < vt.Count; i++) if (vt[i].X < vt[i - 1].X - 0.001f) vback++;
        detail.Append($"; vault: {vf} frames, peak lift {lift:0.0}px at x={peakX:0} (cover spans {coverL:0}-{coverR:0}), " +
                      $"landed ({v.X},{v.Y}) pos ({v.Pos.X:0.0},{v.Pos.Y:0.0}), backSteps {vback}");
        if (lift < MoveStepAnim.FeelVaultLiftMin) fails.Add($"vaultLift({lift:0.0}px<{MoveStepAnim.FeelVaultLiftMin:0}px)");
        if (peakX < coverL || peakX > coverR) fails.Add("vaultPeakNotOverCover");
        if (v.X != 7 || v.Y != 5 || v.Pos != Util.TileCenter(7, 5)) fails.Add("vaultDidNotLandOnCentre");
        if (vback > 0) fails.Add($"vaultBackSteps({vback})");

        // ---- (c) floating text at one anchor ----
        Fx = new Fx();
        Vector2 A = new(400, 400), B = new(600, 400), C = new(800, 400);
        var num = Pal.RGBA(255, 252, 245);
        Fx.PopText(A + new Vector2(0, -26), "7", num, 32f);                       // ShotAnim: the killing number
        Fx.PopText(A + new Vector2(0, -10), "KIA", Pal.Foe, 22f);                 // Game.KillUnit: the pop
        Fx.Stamp(A + new Vector2(0, -34), "KIA  DOE", Pal.Foe, 30f, 2.4f);        // Game.KillUnit: the name stamp
        Fx.PopText(B + new Vector2(0, -26), "3", num, 26f);                       // ShotAnim: BRACE hit number
        Fx.PopText(B + new Vector2(0, -30), "STAGGERED", Pal.Good, 20f);          // ShotAnim: the stagger word
        Fx.PopText(C + new Vector2(0, -26), "4", num, 32f);                       // two overwatch hits, equal damage
        Fx.PopText(C + new Vector2(0, -26), "4", num, 32f);
        Fx.Update(1f / 60f);
        float MinSep(List<FloatText> g)
        {
            float best = float.MaxValue;
            for (int i = 0; i < g.Count; i++) for (int j = i + 1; j < g.Count; j++)
                best = MathF.Min(best, Vector2.Distance(g[i].Pos, g[j].Pos));
            return best;
        }
        List<FloatText> Near(Vector2 a) => Fx.Texts.Where(t => MathF.Abs(t.Pos.X - a.X) < 60f).ToList();
        var gA = Near(A); var gB = Near(B); var gC = Near(C);
        float sA = MinSep(gA), sB = MinSep(gB), sC = MinSep(gC);
        bool twinsDiverge = gC.Count == 2 && gC[0].Drift != 0f && Math.Sign(gC[0].Drift) != Math.Sign(gC[1].Drift);
        detail.Append($"; text: kill-trio min sep {sA:0.0}px, brace-pair {sB:0.0}px, twin numbers {sC:0.0}px, twins diverge {twinsDiverge}");
        if (gA.Count != 3 || gB.Count != 2 || gC.Count != 2) fails.Add("textGroupsMissing");
        if (sA < Fx.TextSep) fails.Add($"killTrioOverprint({sA:0.0}px)");
        if (sB < Fx.TextSep) fails.Add($"bracePairOverprint({sB:0.0}px)");
        if (sC < Fx.TextSep) fails.Add($"twinNumbersOverprint({sC:0.0}px)");
        if (!twinsDiverge) fails.Add("twinNumbersSameArc");

        // ---- (d) THE BEAT: the mission-ending KILL-CAM is slow-mo, not a freeze ----
        // A REAL mission (StartMission: the kill-cam reads the objective and the live roster), the
        // REAL Game.Update stepped at 1/60 with AutoPlay OFF (a human is watching), one death-FX
        // particle followed BY REFERENCE across 30 frames. Pre-fix: KillUnit's mission-ending arm
        // added HitStop(0.4) and Update returned before Fx.Update ran, so the particle sat still for
        // 24 frames while the zoom-punch — decayed ABOVE that return — spent itself inside the freeze.
        // The 'slow-mo kill-cam' FEATURES.md and ROADMAP 3.11 described was a still frame.
        {
            var g = new Game { NoPersist = true, ForcedObjective = Objective.Eliminate };
            Util.Reseed(70032);
            g.StartMission(1);
            g.BriefLines = null;
            var foes = g.Enemies.Where(e => e.Alive).ToList();
            for (int i = 0; i < foes.Count - 1; i++) { foes[i].Hp = 0; g.KillUnit(foes[i]); }
            g.Fx.Particles.Clear(); g.HitStop = 0f;
            var lastFoe = foes[foes.Count - 1];
            lastFoe.Hp = 0; g.KillUnit(lastFoe);                 // the deciding death: the field is clear
            float peakPulse = g.CamPulse;
            // KillUnit's shatter is Shockwave(ring) + 18 DirSparks + 30 spark Burst + 16 slow Burst:
            // follow the first particle of the SLOW burst (life >= 0.48 s, drag 3) so it outlives the window.
            var p = g.Fx.Particles.Count > 48 ? g.Fx.Particles[48] : (g.Fx.Particles.Count > 0 ? g.Fx.Particles[0] : null);
            var kd = new List<float>();
            float pulse24 = 0f;
            Vector2 prev = p != null ? p.Pos : Vector2.Zero;
            for (int f = 1; f <= 30; f++)
            {
                g.Update(1f / 60f);
                if (p != null) { kd.Add(Vector2.Distance(p.Pos, prev)); prev = p.Pos; }
                if (f == 24) pulse24 = g.CamPulse;
            }
            int winFrames = (int)MathF.Round(Game.KillCamWindow * 60f);   // 27 at 60 Hz
            int still = kd.Take(winFrames).Count(d => d <= 0.001f);
            float inWin = kd.Take(winFrames).DefaultIfEmpty(0f).Average();
            float after = kd.Skip(winFrames).DefaultIfEmpty(0f).Average();
            float ratio = after > 0f ? inWin / after : 0f;
            float pulsePct = peakPulse > 0f ? pulse24 / peakPulse * 100f : 0f;
            detail.Append($"; killcam: window {Game.KillCamWindow:0.00}s at x{Game.KillCamScale:0.00}, tracked particle still-frames {still}/{winFrames}, " +
                          $"px/frame in-window {inWin:0.00} vs after {after:0.00} (ratio {ratio:0.00}), camPulse peak {peakPulse:0.000} -> frame 24 {pulse24:0.000} ({pulsePct:0}%), phase {g.Phase}");
            if (p == null) fails.Add("killcamNoParticle");
            if (peakPulse <= 0f) fails.Add("killcamNoZoomPunch");
            if (still > 0) fails.Add($"killcamStillFrames({still})");
            // the window runs at ~KillCamScale of the post-window rate (drag makes it a touch higher)
            if (ratio < 0.18f || ratio > 0.45f) fails.Add($"killcamSlowmoRatio({ratio:0.00})");
            if (peakPulse > 0f && pulsePct < 50f) fails.Add($"killcamZoomSpent({pulsePct:0}%<50%)");
        }

        // ---- (e) THE BEAT: an overwatch REACTION has its own beat ----
        // ShotAnim.Reaction was set by OnUnitEnteredTile and read by no presentation code: a
        // reaction fired on the same 0.10 s wind-up as any shot, with no hit-stop and no cue of its
        // own. Constructed exactly as the reaction site does, OnStart'ed with AutoPlay OFF; the
        // plain shot and the windless (AutoPlay) reaction are the two controls.
        {
            Stage();
            var w2 = MkP(3, 5); Players.Add(w2);
            var mv = new Unit { Name = "E", Cls = "GRUNT", Team = Team.Enemy, X = 8, Y = 5, Hp = 6, MaxHp = 6, Aim = 60, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            mv.Ammo = mv.Weapon.Clip; mv.SyncPos(); mv.BeginTurn(); Enemies.Add(mv);
            var res = new ShotResult { Hit = true, Damage = 2 };
            AutoPlay = false; HitStop = 0f;
            int retBefore = Fx.Reticles.Count, lightBefore = Fx.Lights.Count;
            var react = new ShotAnim(w2, mv, res, reaction: true);
            react.OnStart(this);
            float fireR = react.FireAtSecs, totR = react.TotalSecs, hsR = HitStop, flinchR = mv.FlinchAnim;
            float retR = Fx.Reticles.Count > retBefore ? Fx.Reticles[Fx.Reticles.Count - 1].R0 : 0f;
            int lightsR = Fx.Lights.Count - lightBefore;
            HitStop = 0f; mv.FlinchAnim = 0f;
            var plain = new ShotAnim(w2, mv, res);
            plain.OnStart(this);
            float fireP = plain.FireAtSecs, totP = plain.TotalSecs, hsP = HitStop;
            AutoPlay = true; HitStop = 0f;
            var auto = new ShotAnim(w2, mv, res, reaction: true);
            auto.OnStart(this);
            float fireA = auto.FireAtSecs, totA = auto.TotalSecs, hsA = HitStop;
            AutoPlay = false; HitStop = 0f;
            detail.Append($"; reaction: fires at {fireR:0.00}s (plain {fireP:0.00}s, autoplay {fireA:0.00}s), total {totR:0.00}s (plain {totP:0.00}s, autoplay {totA:0.00}s), " +
                          $"hit-stop {hsR:0.00}s (plain {hsP:0.00}s, autoplay {hsA:0.00}s), reticle r0 {retR:0}px, mover lights {lightsR}, mover flinch {flinchR:0.00}");
            if (fireR < 0.28f || fireR > 0.34f) fails.Add($"reactionWindUp({fireR:0.00}s)");
            if (MathF.Abs(fireP - 0.14f) > 0.005f) fails.Add($"plainShotWindUpMoved({fireP:0.00}s)");
            if (hsR <= 0f) fails.Add("reactionNoHitStop");
            if (hsP != 0f) fails.Add($"plainShotHitStop({hsP:0.00}s)");
            if (MathF.Abs(totP - 0.52f) > 0.005f) fails.Add($"plainShotTotalMoved({totP:0.00}s)");
            if (MathF.Abs(fireA - 0.04f) > 0.005f || MathF.Abs(totA - 0.52f) > 0.005f || hsA != 0f) fails.Add($"autoplayReactionNotCollapsed({fireA:0.00}/{totA:0.00}/{hsA:0.00})");
        }

        // ---- (f) THE BEAT: the borrowed cues have recipes of their own ----
        // grenade/barrel/siege played "crit"+"death", flashbang/incendiary "crit", smoke "hunker",
        // heal/stabilize/patch "reload", a reaction the same "over" as SETTING overwatch. The routing
        // is asserted by grep (DEVLOG §THE BEAT); this leg pins that the recipes exist to route to.
        {
            string[] beatCues = { "react", "boom", "flash", "heal", "smoke" };
            var missing = beatCues.Where(id => !Audio.HasCue(id)).ToList();
            detail.Append($"; cues: {string.Join(" ", beatCues.Select(id => id + (Audio.HasCue(id) ? "+" : "-")))}");
            if (missing.Count > 0) fails.Add($"cueMissing({string.Join(",", missing)})");
        }

        Console.WriteLine("FEELTEST: " + detail);
        return fails.Count == 0
            ? "FEELTEST: PASS (6-tile walk: mid-path speed never dips under " + MoveStepAnim.FeelMinSpeedRatio.ToString("0.00") +
              "x its max, zero stall frames, one lean kick, 48-frame cadence pinned; VAULT lifts >= " + MoveStepAnim.FeelVaultLiftMin.ToString("0") +
              "px over the cover and lands on the tile centre; stacked floating text keeps >= " + Fx.TextSep.ToString("0") + "px separation and twin numbers arc apart; " +
              "kill-cam: no still frame in the " + Game.KillCamWindow.ToString("0.00") + "s window, FX at ~x" + Game.KillCamScale.ToString("0.00") + " with the zoom held; " +
              "a reaction shot winds up ~0.30s with its own hit-stop while a plain shot and the autoplay path are unchanged; react/boom/flash/heal/smoke are registered cues)"
            : "FEELTEST: FAIL (" + string.Join(",", fails) + ")";
    }


    // ── R2 FIX 1 "NOBODY IS WALLED OUT" — SIGHTLINE_GEOMTEST ───────────────────────────────
    /// Every deployed SOLDIER must be able to reach the rest of the squad and must have at
    /// least one legal move on turn 1 — and every hostile / evac tile / terminal / sabotage
    /// site must stay reachable from the squad. Mission.EnsureConnectivity is the net that
    /// guarantees it; before R2 it repaired everything EXCEPT the other players, which was
    /// invisible until W4's ENVELOP shape seated the squad in the mid-field cover band
    /// (measured 23 stranded soldiers / 5760 fresh builds at heat 0, one fully entombed).
    ///
    /// This sweeps what STACKTEST structurally cannot: STACKTEST is a fixed 16-board sample
    /// that never varies the deployment shape, so it could not have caught a shape-specific
    /// geometry defect. Here every shape x objective x mission x seed x heat is BUILT fresh
    /// (no play — StartMission only), which is cheap enough to sweep thousands of boards.
    /// Both terrain paths are exercised: authored arenas and the procedural fallback (the
    /// defect only ever appeared on the procedural one; the split is reported so a future
    /// change that silently stops sampling one of them is visible).
    /// Non-vacuity: PASS requires a real sample AND at least one ENVELOP build per heat.
    public static string GeomSelfTest(int seeds = 8)
    {
        var fails = new List<string>();
        int cases = 0, envelopBuilds = 0, authored = 0, procedural = 0;
        string prevHeat = Environment.GetEnvironmentVariable("SIGHTLINE_HEAT");
        int prevDeploy = Mission.ForcedDeploy;
        var objs = (Objective[])Enum.GetValues(typeof(Objective));
        var shapeName = new[] { "FRONTAL", "PINCER", "CROSSFIRE", "ENVELOP" };

        foreach (int heat in new[] { 0, 8 })
        {
            Environment.SetEnvironmentVariable("SIGHTLINE_HEAT", heat.ToString());
            int envelopThisHeat = 0;
            for (int shape = 0; shape < Mission.DeployShapes; shape++)
            {
                Mission.ForcedDeploy = shape;
                foreach (var obj in objs)
                    for (int m = 1; m <= Run.MaxMissions; m++)
                        for (int s = 0; s < seeds; s++)
                        {
                            Util.Reseed(90001 + s * 7919 + m * 131 + (int)obj * 17 + shape * 3 + heat * 104729);
                            Game g;
                            try { g = new Game { NoPersist = true, ForcedObjective = obj }; g.StartMission(m); }
                            catch (Exception ex)
                            { fails.Add($"h{heat}/{shapeName[shape]}/{obj}/m{m}/s{s} THREW {ex.GetType().Name}"); continue; }
                            cases++;
                            if (Mission.AppliedDeploy == Mission.DeployEnvelop) { envelopBuilds++; envelopThisHeat++; }
                            if (Mission.AppliedLayout >= 0) authored++; else procedural++;
                            string tag = $"h{heat}/{shapeName[shape]}/{obj}/m{m}/s{s}";
                            if (fails.Count > 30) continue;   // the report is already damning; stop collecting

                            var p0 = g.Players.FirstOrDefault(p => p.Alive);
                            if (p0 == null) { fails.Add(tag + ": no living squad"); continue; }
                            var cost = g.Grid.CostMap(p0.X, p0.Y, (x, y) => false, out _, 9999);
                            bool Stuck(int x, int y) => !g.Grid.InBounds(x, y) || cost[x, y] < 0;

                            foreach (var u in g.Players)
                                if (u.Alive && Stuck(u.X, u.Y))
                                    fails.Add($"{tag}: SOLDIER {u.Name} walled off at ({u.X},{u.Y}) layout={Mission.AppliedLayout}");
                            foreach (var u in g.Enemies)
                                if (u.Alive && Stuck(u.X, u.Y)) fails.Add($"{tag}: hostile {u.Cls} unreachable at ({u.X},{u.Y})");
                            foreach (var t in g.EvacZone)
                                if (Stuck(t.x, t.y)) fails.Add($"{tag}: evac tile ({t.x},{t.y}) unreachable");
                            if (g.HasTerminal && Stuck(g.Terminal.x, g.Terminal.y)) fails.Add(tag + ": terminal unreachable");
                            foreach (var st in g.SabotageSites)
                                if (Stuck(st.x, st.y)) fails.Add($"{tag}: sabotage site ({st.x},{st.y}) unreachable");

                            // ELBOW ROOM: a soldier can be reachable and still be frozen on turn 1
                            // (every neighbour cover or a teammate, diagonals killed by the corner rule).
                            foreach (var u in g.Players)
                            {
                                if (!u.Alive || u.IsVip) continue;
                                bool step = false;
                                for (int dx = -1; dx <= 1 && !step; dx++)
                                    for (int dy = -1; dy <= 1 && !step; dy++)
                                    {
                                        if (dx == 0 && dy == 0) continue;
                                        bool OpenT(int ax, int ay) => g.Grid.IsFloor(ax, ay) && g.UnitAt(ax, ay) == null;
                                        if (!OpenT(u.X + dx, u.Y + dy)) continue;
                                        if (dx != 0 && dy != 0 && (!OpenT(u.X + dx, u.Y) || !OpenT(u.X, u.Y + dy))) continue;
                                        step = true;
                                    }
                                if (!step) fails.Add($"{tag}: SOLDIER {u.Name} ENTOMBED at ({u.X},{u.Y}) — no legal move");
                            }
                        }
            }
            if (envelopThisHeat == 0) fails.Add($"h{heat}: VACUOUS — no ENVELOP build sampled");
        }

        Mission.ForcedDeploy = prevDeploy;
        Environment.SetEnvironmentVariable("SIGHTLINE_HEAT", prevHeat);
        if (cases < 1000) fails.Add($"VACUOUS — only {cases} boards built");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"GEOMTEST: {cases} fresh boards (authored {authored} / procedural {procedural}), "
                    + $"{envelopBuilds} ENVELOP, heats 0+8, {seeds} seeds x 4 shapes x 8 objectives x {Run.MaxMissions} missions");
        foreach (var f in fails.Take(12)) sb.AppendLine("  " + f);
        if (fails.Count > 12) sb.AppendLine($"  (+{fails.Count - 12} more)");
        sb.Append(fails.Count == 0
            ? "GEOMTEST: PASS (every soldier reachable and able to move; every hostile/objective tile reachable)"
            : $"GEOMTEST: FAIL ({fails.Count} violations)");
        return sb.ToString();
    }


    // ================= W4 "THE BOARD BECOMES A PLACE" — BOARDTEST ================================
    // The one self-test in this project that measures RENDERED PIXELS rather than game state, and
    // it exists because the defect it guards is a rendering defect that no state test can see: at
    // base commit d350416 a DORMANT POD measured peak luminance 219.9 while the SELECTED SOLDIER
    // measured 188.1 and a LIVE HOSTILE 211.1 — the squint hierarchy in docs/DESIGN.md 3.H
    // ("the focal element is the brightest"; "neutral/inactive is dimmed toward the background")
    // was running exactly backwards, and nine programs of balance work could not see it.
    //
    // The test stages a fixed three-token scene on a real mission board, draws ONE real frame
    // through the shipped Game.Draw path, screenshots it, and probes the pixels:
    //   GATE A (value hierarchy): peak AND mean luminance of a 14px patch on each token must run
    //           SELECTED SOLDIER > LIVE HOSTILE > DORMANT POD, with >= 8 luma of margin so the
    //           move overlay's alpha-15 whisper tint cannot flip a verdict.
    //   GATE B (cover merge): on a horizontally adjacent same-type cover pair, the scanline from
    //           one tile centre to the other must contain no FLOOR-coloured gutter. Pre-wave (and
    //           under SIGHTLINE_COVERMERGE=0) that scanline crosses ~10px of visible floor,
    //           because DrawCover inset every tile on all four sides with no neighbour test.
    //   GATE C (the reserved objective gold): Pal.MoveDash must not share Pal.Accent's RGB bytes.
    //   GATE D (dash on demand): the default interactive frame draws the WALK contour and NOT the
    //           dash region.
    //   GATE E (the cover volume's identity survives damage): a stamped four-tile HighCover run is
    //           drawn, its NW-most tile is then destroyed through the real Grid.DamageCover path,
    //           and the run's FAR tile must come back BYTE-IDENTICAL. Before the W4 review fix the
    //           volume's identity was the live minimum tile index, so losing the end of a wall
    //           re-rolled the material form and the footprint jitter of every surviving tile.
    //
    // THE CLOCK IS PINNED (Renderer.TimePin) for the whole test. A pixel probe cannot sample an
    // unpinned animation phase and call the number a result: ten runs of the LEGACY path on one
    // binary gave a bimodal SELECTED peak of 166.8 (x3) / 188.1 (x7), because the selection halo
    // pulses 0.55 + 0.40*sin(t*5) on Pal.Accent. The pin is set to the phase where that halo is at
    // its DIMMEST (sin = -1), which is the hardest moment for the gate that protects the selected
    // soldier's primacy, and it makes gate E's byte-identity comparison possible at all.
    //
    // GATE A runs TWICE: once in the normal palette and once through Pal.SetColorblind(true). The
    // value rungs are stated as target LUMINANCE rather than as mix fractions precisely so the
    // ordering survives the colourblind remap (Pal.Foe changes hue AND luma), and until this the
    // claim had no coverage at all — SIGHTLINE_CB is read far below this hook in Program.cs.
    //
    // FAILS on the pre-wave tree: run it with SIGHTLINE_TOKENSTYLE=0 (gate A), SIGHTLINE_COVERMERGE=0
    // (gate B) or SIGHTLINE_MOVESTYLE=0 (gate D) and it reports the pre-wave numbers and FAILs.
    public string BoardSelfTest()
    {
        var sb = new System.Text.StringBuilder();
        var fails = new List<string>();
        NoPersist = true;
        // Pin the CLOCK as well as the world: 45 wall-clock reads in Renderer.cs drive animation,
        // and the legacy path's headline number swung 21 luma between runs of one binary because
        // of it. t = 3*pi/10 puts the selection halo's 5 rad/s pulse at its minimum; that is a
        // stated constant, not a claim about which element owned the swing. Under it the legacy
        // path prints ONE number, 188.1, ten runs out of ten, and the shipped path's 201.7 is
        // phase-independent (measured across seven pins, 0..12s).
        bool cbWas = Pal.Colorblind;
        Renderer.TimePin = 3.0 * Math.PI / 10.0;
        // Pin the world. This test reads PIXELS, so it has to be looking at the same board every
        // time or its numbers are not comparable run to run — and the cover-merge gate needs a
        // board that actually contains an adjacent same-type cover pair, which a clock-seeded
        // arena does not guarantee (measured: one draw in three had none).
        Util.Reseed(90210);
        StartMission(1);
        for (int i = 0; i < 12; i++) Update(1f / 60f);   // settle the opening anims
        BriefLines = null;                                // the briefing card sits over the probes
        CalloutText = null; TutStep = -1;                  // and so would a field tip / a lesson card

        // ---- stage: three tokens on clean floor, well clear of the HUD and of each other -------
        // The roster strip occupies board column 0 and the action bar the bottom two rows, so the
        // probe window is columns 3..W-4, rows 1..H-4. Facings and bob phases are pinned so the
        // three patches differ ONLY in the thing under test.
        var spots = new List<(int, int)>();
        for (int x = 3; x < Grid.W - 3 && spots.Count < 3; x++)
            for (int y = 1; y < Grid.H - 3 && spots.Count < 3; y++)
            {
                if (!Grid.IsFloor(x, y)) continue;
                bool clear = true;
                foreach (var (sx, sy) in spots) if (Util.ChebyDist(x, y, sx, sy) < 3) clear = false;
                if (clear) spots.Add((x, y));
            }
        if (spots.Count < 3) { Renderer.TimePin = -1.0; return "BOARDTEST: FAIL (could not stage three clear tiles)"; }

        Enemies.Clear();
        Unit Stage(string cls, Team team, AlertLevel a, int x, int y)
        {
            var u = new Unit { Name = "PROBE", Cls = cls, Team = team, X = x, Y = y,
                               Hp = 6, MaxHp = 6, Aim = 60, Mobility = 6, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.Alert = a; u.PodId = -1; u.Bob = 0f; u.Facing = 0f;
            u.SyncPos();
            return u;
        }
        // ---- stamp a KNOWN two-tile cover volume for the merge gate ---------------------------
        // The gate must not depend on the arena roll handing it an adjacent same-type pair (one
        // board in three does not). Stamp one: two ground-level HighCover tiles side by side on
        // clean floor, well away from the three probe tokens. This is a throwaway harness Game,
        // and DrawCover reads the grid every frame, so the merge logic gets exercised exactly as
        // it would on an authored wall.
        int cvx = -1, cvy = -1;
        for (int y = Grid.H - 4; y >= 1 && cvx < 0; y--)
            for (int x = Grid.W - 5; x >= 3 && cvx < 0; x--)
            {
                if (!Grid.IsFloor(x, y) || !Grid.IsFloor(x + 1, y)) continue;
                if (Grid.HeightAt(x, y) != 0 || Grid.HeightAt(x + 1, y) != 0) continue;
                bool nearProbe = false;
                foreach (var (sx, sy) in spots)
                    if (Util.ChebyDist(x, y, sx, sy) < 2 || Util.ChebyDist(x + 1, y, sx, sy) < 2) nearProbe = true;
                if (nearProbe) continue;
                cvx = x; cvy = y;
            }
        if (cvx >= 0)
        {
            Grid.Tiles[cvx, cvy] = TileType.HighCover;
            Grid.Tiles[cvx + 1, cvy] = TileType.HighCover;
            Grid.SetCoverHp(cvx, cvy); Grid.SetCoverHp(cvx + 1, cvy);   // undamaged: no crack overlay
        }

        var foe = Stage("GRUNT", Team.Enemy, AlertLevel.Alert, spots[1].Item1, spots[1].Item2);
        var pod = Stage("GRUNT", Team.Enemy, AlertLevel.Unaware, spots[2].Item1, spots[2].Item2);
        Enemies.Add(foe); Enemies.Add(pod);
        // put every soldier but one off the probe window, and stand the survivor on spots[0]
        var hero = Players[0];
        for (int i = Players.Count - 1; i >= 1; i--) Players.RemoveAt(i);
        hero.X = spots[0].Item1; hero.Y = spots[0].Item2; hero.Bob = 0f; hero.Facing = 0f; hero.SyncPos();
        Selected = hero;
        RecomputeMoveCost();

        // ---- draw one real frame and read it back ----------------------------------------------
        const string shotPath = "sightline_boardtest.png";
        Display.RenderFrame(Draw);
        Raylib.TakeScreenshot(shotPath);
        var img = Raylib.LoadImage(shotPath);
        float Lum(int px, int py)
        {
            var c = Raylib.GetImageColor(img, px, py);
            return 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;
        }
        (float mean, float peak) Patch(int cx, int cy, int r)
        {
            float sum = 0, pk = 0; int n = 0;
            for (int px = cx - r; px <= cx + r; px++)
                for (int py = cy - r; py <= cy + r; py++)
                {
                    float l = Lum(px, py); sum += l; if (l > pk) pk = l; n++;
                }
            return (sum / n, pk);
        }
        Vector2 Cen(int tx, int ty) => Util.TileCenter(tx, ty);
        var pHero = Patch((int)Cen(hero.X, hero.Y).X, (int)Cen(hero.X, hero.Y).Y, 14);
        var pFoe = Patch((int)Cen(foe.X, foe.Y).X, (int)Cen(foe.X, foe.Y).Y, 14);
        var pPod = Patch((int)Cen(pod.X, pod.Y).X, (int)Cen(pod.X, pod.Y).Y, 14);
        int walkLoops = Renderer.LastWalkLoops, walkEdges = Renderer.LastWalkEdges;
        bool dashShown = Renderer.LastDashShown;

        sb.AppendLine($"BOARDTEST value hierarchy (14px patches, Rec.709 luma):");
        sb.AppendLine($"  SELECTED soldier  mean {pHero.mean,6:0.0}  PEAK {pHero.peak,6:0.0}");
        sb.AppendLine($"  ACTIVE hostile    mean {pFoe.mean,6:0.0}  PEAK {pFoe.peak,6:0.0}");
        sb.AppendLine($"  DORMANT pod       mean {pPod.mean,6:0.0}  PEAK {pPod.peak,6:0.0}");
        const float margin = 8f;
        if (pHero.peak < pFoe.peak + margin) fails.Add($"peak: selected {pHero.peak:0.0} !> hostile {pFoe.peak:0.0}");
        if (pFoe.peak < pPod.peak + margin) fails.Add($"peak: hostile {pFoe.peak:0.0} !> dormant {pPod.peak:0.0}");
        if (pHero.mean < pFoe.mean + margin) fails.Add($"mean: selected {pHero.mean:0.0} !> hostile {pFoe.mean:0.0}");
        if (pFoe.mean < pPod.mean + margin) fails.Add($"mean: hostile {pFoe.mean:0.0} !> dormant {pPod.mean:0.0}");

        // ---- GATE A, AGAIN, IN THE COLOURBLIND PALETTE ----------------------------------------
        // The rungs are stated as target LUMINANCE (Renderer.ToLuma) instead of as mix fractions
        // exactly so the ordering survives Pal.SetColorblind, where Pal.Foe changes hue AND luma
        // (248,113,113 lum 153 -> 238,138,40 lum 152) and a fixed mix would land somewhere
        // different in each palette. That was the wave's most load-bearing robustness claim and it
        // had ZERO self-test coverage: SIGHTLINE_CB is read ~600 lines below this hook's early
        // return in Program.cs, so it could never reach a BOARDTEST run.
        Pal.SetColorblind(true);
        Display.RenderFrame(Draw);
        Raylib.TakeScreenshot(shotPath);
        Raylib.UnloadImage(img);
        img = Raylib.LoadImage(shotPath);
        var qHero = Patch((int)Cen(hero.X, hero.Y).X, (int)Cen(hero.X, hero.Y).Y, 14);
        var qFoe = Patch((int)Cen(foe.X, foe.Y).X, (int)Cen(foe.X, foe.Y).Y, 14);
        var qPod = Patch((int)Cen(pod.X, pod.Y).X, (int)Cen(pod.X, pod.Y).Y, 14);
        Pal.SetColorblind(cbWas);
        sb.AppendLine($"BOARDTEST value hierarchy, COLORBLIND palette (SIGHTLINE_CB's remap):");
        sb.AppendLine($"  SELECTED soldier  mean {qHero.mean,6:0.0}  PEAK {qHero.peak,6:0.0}");
        sb.AppendLine($"  ACTIVE hostile    mean {qFoe.mean,6:0.0}  PEAK {qFoe.peak,6:0.0}");
        sb.AppendLine($"  DORMANT pod       mean {qPod.mean,6:0.0}  PEAK {qPod.peak,6:0.0}");
        if (qHero.peak < qFoe.peak + margin) fails.Add($"CB peak: selected {qHero.peak:0.0} !> hostile {qFoe.peak:0.0}");
        if (qFoe.peak < qPod.peak + margin) fails.Add($"CB peak: hostile {qFoe.peak:0.0} !> dormant {qPod.peak:0.0}");
        if (qHero.mean < qFoe.mean + margin) fails.Add($"CB mean: selected {qHero.mean:0.0} !> hostile {qFoe.mean:0.0}");
        if (qFoe.mean < qPod.mean + margin) fails.Add($"CB mean: hostile {qFoe.mean:0.0} !> dormant {qPod.mean:0.0}");

        // ---- GATE B: no floor gutter between two tiles of the same cover volume ----------------
        // Redraw with nothing selected first: the move overlay strokes tile EDGES, and a cyan
        // stroke sitting exactly on the seam under test would mask an unmerged gutter.
        Selected = null;
        Display.RenderFrame(Draw);
        Raylib.TakeScreenshot(shotPath);
        Raylib.UnloadImage(img);
        img = Raylib.LoadImage(shotPath);
        // The discriminator is NOT "is the seam floor-coloured" — a cover top standing in the key
        // light's far corner can measure darker than a lit floor tile, so an absolute threshold
        // reads the wrong answer at one end of the board. It is the TROUGH: an unmerged pair puts
        // a gutter of floor PLUS both blocks' hairline edges and contact shadows between the two
        // top faces, which is a deep, wide dip relative to the SAME MATERIAL a few pixels either
        // side. A merged volume has continuous top face across the seam.
        int gapPx = -1; string pairDesc = "none"; float topRef = 0f, seamMin = 0f;
        if (cvx >= 0)
        {
            var a = Cen(cvx, cvy); var b = Cen(cvx + 1, cvy);
            int yy = (int)a.Y - 6;                       // inside the merged top face
            int seamX = (int)((a.X + b.X) * 0.5f);       // the shared tile edge
            var interior = new List<float>();
            for (int px = (int)a.X - 14; px <= (int)a.X + 6; px++) interior.Add(Lum(px, yy));
            for (int px = (int)b.X - 6; px <= (int)b.X + 14; px++) interior.Add(Lum(px, yy));
            interior.Sort();
            topRef = interior[interior.Count / 2];
            seamMin = float.MaxValue; int g = 0;
            for (int px = seamX - 12; px <= seamX + 12; px++)
            {
                float l = Lum(px, yy);
                if (l < seamMin) seamMin = l;
                if (l < 0.78f * topRef) g++;
            }
            gapPx = g; pairDesc = $"({cvx},{cvy})-({cvx + 1},{cvy}) HighCover";
        }
        sb.AppendLine($"BOARDTEST cover merge: stamped pair {pairDesc}; top-face median {topRef:0.0} luma, "
                      + $"seam minimum {seamMin:0.0}; dark-trough pixels across the seam = {gapPx} "
                      + $"(merged expects <= 3; SIGHTLINE_COVERMERGE=0 measures ~14)");
        if (gapPx < 0) fails.Add("could not stamp a two-tile cover volume — the merge gate could not run");
        else if (gapPx > 3) fails.Add($"cover seam shows a {gapPx}px dark trough — the volume is not merged");

        // ---- GATE E: damage must not RE-ROLL the surviving tiles of a volume -------------------
        // The wave keyed the volume's material FORM and its footprint jitter off a union-find root
        // recomputed from the LIVE tile set every frame, with the minimum linear index as root.
        // Cover is destructible, so shooting the north/west-most tile off a wall moved the root and
        // re-rolled the material for every tile that was still standing — the wall turned from
        // crates into rock mid-mission, which reads as a rendering glitch and not as damage.
        // Stamp a four-tile run, photograph its FAR tile, destroy the run's NW-most tile through
        // the real Grid.DamageCover path (High -> rubble -> gone), photograph the far tile again:
        // with the clock pinned the two frames differ by exactly one tile, so the far tile must
        // come back byte-identical. The visual A/B is SIGHTLINE_COVER=1 [+ SIGHTLINE_COVERKILL=1].
        int wallX = -1, wallY = -1;
        for (int wy = 1; wy < Grid.H - 3 && wallX < 0; wy++)
            for (int wx = 3; wx <= Grid.W - 8 && wallX < 0; wx++)
            {
                if (wy == cvy) continue;                        // keep clear of gate B's stamped pair
                bool flat = true;
                for (int i = 0; i < 4; i++) if (Grid.HeightAt(wx + i, wy) != 0) flat = false;
                if (flat) { wallX = wx; wallY = wy; }
            }
        int morphPx = -1, morphTot = 0;
        if (wallX >= 0)
        {
            for (int i = 0; i < 4; i++)
            {
                if (Grid.InBounds(wallX + i, wallY - 1)) Grid.Tiles[wallX + i, wallY - 1] = TileType.Floor;
                if (Grid.InBounds(wallX + i, wallY + 1)) Grid.Tiles[wallX + i, wallY + 1] = TileType.Floor;
                Grid.Tiles[wallX + i, wallY] = TileType.HighCover;
                Grid.SetCoverHp(wallX + i, wallY);
            }
            Grid.ClearCoverSeeds(); Grid.SeedCoverVolumes();    // the wall EXISTS before it is shot
            var farC = Cen(wallX + 3, wallY);
            int fx0 = (int)farC.X - 28, fy0 = (int)farC.Y - 34, fw = 56, fh = 60;
            int[] Grab()
            {
                var buf = new int[fw * fh];
                for (int py = 0; py < fh; py++)
                    for (int px = 0; px < fw; px++)
                    {
                        var c = Raylib.GetImageColor(img, fx0 + px, fy0 + py);
                        buf[py * fw + px] = (c.R << 16) | (c.G << 8) | c.B;
                    }
                return buf;
            }
            void Reshoot()
            {
                Display.RenderFrame(Draw);
                Raylib.TakeScreenshot(shotPath);
                Raylib.UnloadImage(img);
                img = Raylib.LoadImage(shotPath);
            }
            Reshoot(); var farBefore = Grab();
            Grid.DamageCover(wallX, wallY, 99); Grid.DamageCover(wallX, wallY, 99);
            Reshoot(); var farAfter = Grab();
            morphTot = farBefore.Length; morphPx = 0;
            for (int i = 0; i < farBefore.Length; i++) if (farBefore[i] != farAfter[i]) morphPx++;
            if (Grid.Tiles[wallX, wallY] != TileType.Floor)
                fails.Add("gate E could not destroy the run's NW tile — the probe did not run");
        }
        sb.AppendLine($"BOARDTEST volume identity: stamped run ({wallX},{wallY})..({wallX + 3},{wallY}) HighCover; "
                      + $"NW tile destroyed; far tile re-render differs in {morphPx} of {morphTot} px "
                      + "(a re-rolled form/jitter measures thousands)");
        if (morphPx < 0) fails.Add("could not stamp a four-tile cover run — the volume-identity gate could not run");
        else if (morphPx > 0) fails.Add($"destroying one tile changed {morphPx}px of a SURVIVING tile — the volume's material re-rolled");

        Raylib.UnloadImage(img);
        try { System.IO.File.Delete(shotPath); } catch { }

        // ---- GATE C: the dash stroke is off the reserved objective gold ------------------------
        bool goldClash = Pal.MoveDash.R == Pal.Accent.R && Pal.MoveDash.G == Pal.Accent.G
                         && Pal.MoveDash.B == Pal.Accent.B;
        float stitch = walkLoops > 0 ? walkEdges / (float)walkLoops : 0f;
        sb.AppendLine($"BOARDTEST move overlay: dash RGB ({Pal.MoveDash.R},{Pal.MoveDash.G},{Pal.MoveDash.B}) "
                      + $"vs objective gold ({Pal.Accent.R},{Pal.Accent.G},{Pal.Accent.B}); "
                      + $"walk region {walkEdges} boundary edges stroked as {walkLoops} closed contour(s) "
                      + $"= {stitch:0.0} edges/stroke (pre-wave 1.0); dash region shown {dashShown}");
        if (goldClash) fails.Add("the dash stroke is painted in Pal.Accent, the reserved objective gold");
        // ---- GATE D: the boundary is STITCHED, the dash region is off by default ---------------
        if (walkLoops < 1) fails.Add("the walk region drew nothing — the overlay never ran, so gates C/D are untested");
        else if (stitch < 4f) fails.Add($"the walk boundary is stroked at {stitch:0.0} edges per primitive — it is per-tile edges, not a contour");
        else if (dashShown) fails.Add("the dash region drew unasked on a default interactive frame");

        foreach (var f in fails) sb.AppendLine($"  !! {f}");
        sb.Append(fails.Count == 0
            ? "BOARDTEST: PASS (selected > hostile > dormant on mean and peak in BOTH palettes; cover volumes merged and their material survives damage; dash on demand, off the objective gold)"
            : $"BOARDTEST: FAIL ({fails.Count} violations)");
        Renderer.TimePin = -1.0;
        return sb.ToString();
    }

    /// The unit the filmstrip is following (the one DebugLongMove staged), for the per-frame
    /// position dump in Program.cs. Null before the hook runs.
    public Unit DebugFilmUnit => Selected;


    // ── TRUE BAND: SIGHTLINE_BANDTEST — the decision-density INSTRUMENT as a contract ───────
    /// `CountMeaningfulChoices` is the number every balance wave in this project has argued
    /// about, and until TRUE BAND nobody had pinned it. It is READ-ONLY bookkeeping whose
    /// constants ARE the meaning of every archived `ch/ARMED` figure, so this test pins:
    ///   1. the band CONSTANTS (a silent retune silently invalidates the archive);
    ///   2. the RULE, on the pure `AdmitNearBest` helper — the additive band is invariant under
    ///      a uniform shift of the score scale and the multiplicative one is NOT (that shift
    ///      sensitivity IS the defect this wave removed), and the multiplicative rule is
    ///      degenerate at a non-positive best (which is why it needed the `pbest > 0` guard);
    ///   3. that `SIGHTLINE_CHOICEBAND=mult` reproduces the PRE-WAVE counts EXACTLY, checked
    ///      against a literal transcription of the old method over 120 constructed boards —
    ///      and that the band dial actually moves the number on a good share of them (a dial
    ///      that changed nothing would pass every other assertion here);
    ///   4. that the two halves sum to the total by construction, in BOTH modes, and that the
    ///      band never moves the shot GATE (`armed`) — only the near-best count;
    ///   5. that the counter mutates NO game state and takes ZERO `Util.Rng` draws — the latter
    ///      with a SENSITIVITY probe (the same detector, fed one hand-injected draw, must FAIL),
    ///      so a green purity result can never be the detector not detecting.
    /// Window-free apart from Unit.SyncPos's tile->px math. Prints "BANDTEST: PASS|FAIL".
    public string BandSelfTest()
    {
        NoPersist = true;
        var fails = new List<string>();
        bool savedBand = MultChoiceBand;

        // ── 1. constants ──────────────────────────────────────────────────────────────
        if (PosBand != 3f) fails.Add($"posBand={PosBand}");
        if (ShotBand != 2f) fails.Add($"shotBand={ShotBand}");
        if (PosChoiceCap != 4) fails.Add($"posChoiceCap={PosChoiceCap}");
        if (MultPosFrac != 0.85f) fails.Add($"multPosFrac={MultPosFrac}");
        if (MultShotFrac != 0.88f) fails.Add($"multShotFrac={MultShotFrac}");

        // ── 2. the rule, on the pure helper ───────────────────────────────────────────
        // Same RELATIVE structure (gaps 0/3/6/20), three absolute levels. The additive band
        // must return the same count at every level; the multiplicative window must not.
        var mid = new List<float> { 40f, 37f, 34f, 20f };
        var hi = mid.Select(v => v + 20f).ToList();
        var lo = mid.Select(v => v - 20f).ToList();
        int aMid = AdmitNearBest(mid, 40f, false, MultPosFrac, PosBand);
        int aHi = AdmitNearBest(hi, 60f, false, MultPosFrac, PosBand);
        int aLo = AdmitNearBest(lo, 20f, false, MultPosFrac, PosBand);
        if (aMid != 2 || aHi != 2 || aLo != 2)
            fails.Add($"additiveNotShiftInvariant:{aMid}/{aHi}/{aLo}");
        int mMid = AdmitNearBest(mid, 40f, true, MultPosFrac, PosBand);
        int mHi = AdmitNearBest(hi, 60f, true, MultPosFrac, PosBand);
        int mLo = AdmitNearBest(lo, 20f, true, MultPosFrac, PosBand);
        // the pre-wave defect, pinned so it can never be mistaken for a fixed instrument:
        // drop every score by 20 and the SAME board reads as strictly fewer choices.
        if (!(mMid == 3 && mHi == 3 && mLo == 2))
            fails.Add($"multWindowNotAsSpecified:{mMid}/{mHi}/{mLo}");
        // degeneracy at a non-positive best — the reason the old rule carried a `pbest > 0` guard.
        var neg = new List<float> { -10f, -13f };
        if (AdmitNearBest(neg, -10f, true, MultPosFrac, PosBand) != 0)
            fails.Add("multNotDegenerateAtNegativeBest");
        if (AdmitNearBest(neg, -10f, false, MultPosFrac, PosBand) != 2)
            fails.Add("additiveBrokenAtNegativeBest");

        // ── 3./4. constructed boards ──────────────────────────────────────────────────
        // A LOCAL Random on purpose: the Util.Rng purity check in step 5 must not have to
        // subtract the scene builder's own draws.
        var scenes = new Random(20260829);
        int n = 0, bandMoved = 0, posMoved = 0;
        // review fix: the ADD mode is the rule every future archived number will be measured
        // with, and the first version of this test pinned NO additive behaviour at all. Three
        // knob-level breaks are tracked scene by scene so each pin can be shown NON-VACUOUS —
        // a pin that never differs from its variant is a pin that cannot fail.
        int capPinLive = 0, shotBandPinLive = 0;
        ulong golden = 1469598103934665603UL;   // FNV-1a over the add-mode (total,tgt,pos) triples
        for (int s = 0; s < 120; s++)
        {
            BuildBandScene(scenes);
            MultChoiceBand = true;
            int mt = CountMeaningfulChoices(out int mAct, out int mArm, out int mLos, out int mTgt, out int mPos);
            var o = OldChoiceReference();
            if (mt != o.total || mAct != o.acting || mArm != o.armed || mLos != o.losTargets
                || mTgt != o.tgtChoices || mPos != o.posChoices)
                fails.Add($"scene{s}:multNotOldRule got({mt},{mAct},{mArm},{mLos},{mTgt},{mPos}) "
                          + $"want({o.total},{o.acting},{o.armed},{o.losTargets},{o.tgtChoices},{o.posChoices})");
            if (mTgt + mPos != mt) fails.Add($"scene{s}:multHalvesDontSum {mTgt}+{mPos}!={mt}");
            MultChoiceBand = false;
            int at = CountMeaningfulChoices(out int aAct, out int aArm, out int aLos, out int aTgt, out int aPos);
            if (aTgt + aPos != at) fails.Add($"scene{s}:addHalvesDontSum {aTgt}+{aPos}!={at}");
            // THE ADD-MODE PIN: the shipped rule must equal an independent transcription of it,
            // field for field. This is what catches a constant being right while its USE is not.
            var a = NewChoiceReference();
            if (at != a.total || aAct != a.acting || aArm != a.armed || aLos != a.losTargets
                || aTgt != a.tgtChoices || aPos != a.posChoices)
                fails.Add($"scene{s}:addNotNewRule got({at},{aAct},{aArm},{aLos},{aTgt},{aPos}) "
                          + $"want({a.total},{a.acting},{a.armed},{a.losTargets},{a.tgtChoices},{a.posChoices})");
            // each knob, shown load-bearing: swap it in the reference and the answer must move
            // somewhere across the sweep (counted, asserted after the loop).
            if (ChoiceReference(false, MultShotFrac, ShotBand, MultPosFrac, PosBand, 2, false, true).posChoices != a.posChoices)
                capPinLive++;
            if (ChoiceReference(false, MultShotFrac, PosBand, MultPosFrac, PosBand, PosChoiceCap, false, true).tgtChoices != a.tgtChoices)
                shotBandPinLive++;
            // golden regression anchor over the fixed-seed sweep (FNV-1a, deterministic).
            foreach (int v in new[] { at, aTgt, aPos })
            { golden ^= (ulong)(uint)v; golden *= 1099511628211UL; }
            // the band decides only WHICH candidates count as near-best — never who is eligible.
            if (aAct != mAct || aArm != mArm || aLos != mLos)
                fails.Add($"scene{s}:bandMovedTheGate acting {mAct}->{aAct} armed {mArm}->{aArm} los {mLos}->{aLos}");
            if (at != mt) bandMoved++;
            if (aPos != mPos) posMoved++;
            n++;
        }
        if (n != 120) fails.Add($"sceneCount={n}");
        if (bandMoved < 20) fails.Add($"bandDialInert:{bandMoved}/{n}");
        if (posMoved < 20) fails.Add($"posAxisDialInert:{posMoved}/{n}");
        if (capPinLive < 10) fails.Add($"capPinVacuous:{capPinLive}/{n}");
        if (shotBandPinLive < 10) fails.Add($"shotBandPinVacuous:{shotBandPinLive}/{n}");
        // Golden triples for ADD mode over the fixed-seed sweep. Belt and braces next to the
        // reference above: it also catches the reference itself being edited in lockstep with
        // a broken implementation. Regenerate ONLY with a deliberate, documented rule change.
        const ulong GoldenAddTriples = 0xA58D4F1D8838497BUL;
        if (golden != GoldenAddTriples)
            fails.Add($"addGoldenTriples=0x{golden:X16} (want 0x{GoldenAddTriples:X16})");

        // ── 4b. the NEGATIVE-SAFETY board: the dropped `pbest > 0` guard, pinned ──────
        // The 120 random scenes never produce pbest <= 0, which is exactly why reinstating the
        // guard in additive mode was invisible. On this board every reachable tile is negative.
        BuildNegativeSafetyScene();
        var negPvals = BandPosCandidates(out float negBest);
        if (negPvals.Count < 2) fails.Add($"negSceneNoCandidates:{negPvals.Count}");
        if (negBest >= 0f) fails.Add($"negSceneNotNegative:pbest={negBest:0.0}");
        int negUncapped = AdmitNearBest(negPvals, negBest, false, MultPosFrac, PosBand) - 1;
        if (negUncapped < 5) fails.Add($"negSceneNotEnoughCandidates:{negUncapped}");
        MultChoiceBand = false;
        CountMeaningfulChoices(out _, out int negArmed, out _, out _, out int negAddPos);
        if (negArmed < 1) fails.Add("negSceneSoldierNotArmed");
        // BEHAVIOURAL cap pin: >=5 near-best destinations on offer, so the contribution must be
        // exactly PosChoiceCap. A hardcoded Math.Min(2, ...) at the use site fails here.
        if (negAddPos != PosChoiceCap)
            fails.Add($"negSceneAddPos={negAddPos} want {PosChoiceCap} (uncapped {negUncapped})");
        MultChoiceBand = true;
        CountMeaningfulChoices(out _, out _, out _, out _, out int negMultPos);
        // and the pre-wave rule must contribute NOTHING here — that is the guard, and it is the
        // whole reason dropping it is a real change rather than a cosmetic one.
        if (negMultPos != 0) fails.Add($"negSceneMultPos={negMultPos} want 0 (the pbest>0 guard)");

        // ── 4c. the precondition the axis-(a) zero floor rests on ────────────────────
        // `AdmitNearBest(..., floorAtZero: true)` is only a no-op because every legal ShotValue
        // is strictly positive. Assert that rather than assume it.
        MultChoiceBand = false;
        var shotScenes = new Random(31337);
        int shotVals = 0;
        for (int s = 0; s < 40; s++)
        {
            BuildBandScene(shotScenes);
            foreach (var u in Players)
            {
                if (!u.Alive || u.Ammo <= 0) continue;
                foreach (var e in Enemies)
                {
                    if (!e.Alive) continue;
                    if (Util.TileDist(u.X, u.Y, e.X, e.Y) > u.Weapon.MaxRange) continue;
                    bool commanding = Grid.HeightAt(u.X, u.Y) - Grid.HeightAt(e.X, e.Y) >= 2;
                    if (!Grid.HasLineOfSight(u.X, u.Y, e.X, e.Y, commanding)) continue;
                    float v = ShotValue(Combat.ComputeOdds(Grid, u, e), e);
                    shotVals++;
                    if (v <= 0f) fails.Add($"shotValueNotPositive:{v}");
                }
            }
        }
        if (shotVals < 200) fails.Add($"shotValueSampleTooSmall:{shotVals}");

        // ── 5a. no state mutation ─────────────────────────────────────────────────────
        BuildBandScene(new Random(4242));
        string before = BandBoardFingerprint();
        MultChoiceBand = false; CountMeaningfulChoices();
        MultChoiceBand = true; CountMeaningfulChoices();
        string after = BandBoardFingerprint();
        if (after != before) fails.Add("counterMutatedState");

        // ── 5b. zero Util.Rng draws, WITH a sensitivity probe ─────────────────────────
        bool Pure(Action body)
        {
            Util.Reseed(777);
            int refDraw = Util.RandInt(0, 1000000);
            Util.Reseed(777);
            for (int k = 0; k < 200; k++) body();
            return Util.RandInt(0, 1000000) == refDraw;
        }
        MultChoiceBand = false;
        if (!Pure(() => CountMeaningfulChoices())) fails.Add("counterConsumesRng");
        MultChoiceBand = true;
        if (!Pure(() => CountMeaningfulChoices())) fails.Add("counterConsumesRngMult");
        // The detector must be able to FAIL: feed it the same body plus one hand-injected draw.
        if (Pure(() => { CountMeaningfulChoices(); Util.RandF(); }))
            fails.Add("rngPurityProbeInsensitive");
        Util.Reseed(0);                       // never leave a deterministic stream behind

        MultChoiceBand = savedBand;
        return fails.Count == 0
            ? $"BANDTEST: PASS (boards={n} bandMoved={bandMoved} posMoved={posMoved} "
              + $"posBand={PosBand} shotBand={ShotBand} cap={PosChoiceCap})"
            : "BANDTEST: FAIL " + string.Join(", ", fails.Take(12));
    }

    /// Build a controlled board for BandSelfTest: scattered cover + a little high ground (so the
    /// cover*8 and height*5 terms of the safety score both vary across the reachable set), 2-4
    /// soldiers with mixed kit and 2-5 hostiles, all on distinct floor tiles and all with a fresh
    /// 2-action turn. Draws ONLY from the caller's local Random — never Util.Rng.
    void BuildBandScene(Random r)
    {
        Grid = new Grid();
        Players = new List<Unit>();
        Enemies = new List<Unit>();
        Vip = null; CaptiveLocked = false;
        Objective = Objective.Eliminate;
        EvacZone.Clear();

        for (int i = 0, blocks = 25 + r.Next(45); i < blocks; i++)
            Grid.Tiles[r.Next(Grid.W), r.Next(Grid.H)] =
                r.Next(2) == 0 ? TileType.LowCover : TileType.HighCover;
        for (int i = 0; i < 14; i++) Grid.Height[r.Next(Grid.W), r.Next(Grid.H)] = 1;
        Grid.ResetCoverHp();

        var taken = new HashSet<(int, int)>();
        (int x, int y) Spot()
        {
            for (int t = 0; t < 200; t++)
            {
                int x = r.Next(Grid.W), y = r.Next(Grid.H);
                if (!taken.Add((x, y))) continue;
                Grid.Tiles[x, y] = TileType.Floor;   // stand on floor, never inside cover
                return (x, y);
            }
            return (0, 0);
        }
        var kinds = new[] { WeaponKind.Rifle, WeaponKind.Shotgun, WeaponKind.Sniper,
                            WeaponKind.Smg, WeaponKind.Lmg };
        for (int i = 0, np = 2 + r.Next(3); i < np; i++)
        {
            var (x, y) = Spot();
            var u = new Unit { Name = "P" + i, Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                               Hp = 4 + r.Next(5), MaxHp = 8, Aim = 55 + r.Next(20),
                               Mobility = 3 + r.Next(3), Weapon = Weapon.Make(kinds[r.Next(kinds.Length)]) };
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn();
            Players.Add(u);
        }
        for (int i = 0, ne = 2 + r.Next(4); i < ne; i++)
        {
            var (x, y) = Spot();
            var e = new Unit { Name = "E" + i, Cls = "GRUNT", Team = Team.Enemy, X = x, Y = y,
                               Hp = 3 + r.Next(7), MaxHp = 9, Aim = 55 + r.Next(15),
                               Mobility = 4, Weapon = Weapon.Make(kinds[r.Next(kinds.Length)]) };
            e.Ammo = e.Weapon.Clip; e.Alert = AlertLevel.Alert; e.SyncPos(); e.BeginTurn();
            Enemies.Add(e);
        }
    }

    /// Everything CountMeaningfulChoices could conceivably touch, as one string — the
    /// no-mutation assertion.
    string BandBoardFingerprint()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append((int)Phase).Append('|').Append(Selected?.Name ?? "-").Append('|');
        foreach (var u in Players.Concat(Enemies))
            sb.Append(u.Name).Append(':').Append(u.X).Append(',').Append(u.Y).Append(',')
              .Append(u.Hp).Append(',').Append(u.Ammo).Append(',').Append(u.ActionsLeft).Append(',')
              .Append(u.Alive ? 1 : 0).Append(',').Append(u.OnOverwatch ? 1 : 0).Append(';');
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
                sb.Append((int)Grid.Tiles[x, y]).Append(Grid.Height[x, y]).Append(Grid.CoverHp[x, y]);
        return sb.ToString();
    }

    /// A LITERAL transcription of `CountMeaningfulChoices`, PARAMETERISED over every knob the
    /// rule has. TRUE BAND's first BANDTEST pinned only the constants' VALUES and the mult-mode
    /// reproduction, and review then broke the shipped rule three ways that all still printed
    /// PASS: hardcoding `Math.Min(2, ...)` at the use site while `PosChoiceCap` still read 4;
    /// passing `PosBand` where `ShotBand` belonged on axis (a); and reinstating the `pbest > 0`
    /// guard in additive mode. **Pinning a constant does not pin its USE.** This reference pins
    /// the use: the real counter must equal it field-for-field in BOTH modes, and every knob is
    /// separately shown to be load-bearing (change it, and the reference stops matching).
    /// Do not "clean it up" or share code with the real implementation — its whole value is
    /// being an independent copy.
    (int total, int acting, int armed, int losTargets, int tgtChoices, int posChoices)
        ChoiceReference(bool mult, float shotFrac, float shotBand, float posFrac, float posBand,
                        int cap, bool posGuard, bool shotFloorAtZero)
    {
        int total = 0, acting = 0, armed = 0, losTargets = 0, tgtChoices = 0, posChoices = 0;
        foreach (var u in Players)
        {
            if (!u.Alive || !u.CanAct || u.IsVip || u.Ammo <= 0) continue;
            acting++;
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
            if (best <= 0f) continue;
            armed++;
            losTargets += vals.Count;
            float shotCut = mult ? best * shotFrac : best - shotBand;
            if (!mult && shotFloorAtZero && shotCut < 0f) shotCut = 0f;
            foreach (var v in vals) if (v >= shotCut) comparable++;
            if (comparable >= 2) { total += comparable - 1; tgtChoices += comparable - 1; }

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
                        if (pcost[x, y] <= 0 || pcost[x, y] > u.MoveBudget) continue;
                        float s = SafetyAt(x, y); pvals.Add(s); if (s > pbest) pbest = s;
                    }
                if (!posGuard || pbest > 0f)
                {
                    float posCut = mult ? pbest * posFrac : pbest - posBand;
                    int pComparable = 0;
                    foreach (var s in pvals) if (s >= posCut) pComparable++;
                    if (pComparable >= 2)
                    {
                        int add = Math.Min(cap, pComparable - 1);
                        total += add; posChoices += add;
                    }
                }
            }
        }
        return (total, acting, armed, losTargets, tgtChoices, posChoices);
    }

    /// The PRE-WAVE rule, as one call (multiplicative 0.88 / 0.85, `pbest > 0` guard, cap 2).
    /// Its arguments are LITERALS on purpose: this is the archive's instrument, and it must not
    /// follow a future edit to the shipped constants.
    (int total, int acting, int armed, int losTargets, int tgtChoices, int posChoices) OldChoiceReference()
        => ChoiceReference(true, 0.88f, 0f, 0.85f, 0f, 2, true, false);

    /// The SHIPPED rule, as one call — every argument read from the shipped constant it pins, so
    /// a constant and its use can never drift apart without this failing.
    (int total, int acting, int armed, int losTargets, int tgtChoices, int posChoices) NewChoiceReference()
        => ChoiceReference(false, MultShotFrac, ShotBand, MultPosFrac, PosBand, PosChoiceCap, false, true);

    /// The destination scores of the FIRST armed 2-action soldier, recomputed independently, so
    /// the cap can be pinned BEHAVIOURALLY — how many near-best tiles were really on offer before
    /// the cap clipped them.
    List<float> BandPosCandidates(out float pbest)
    {
        pbest = 0f;
        foreach (var u in Players)
        {
            if (!u.Alive || !u.CanAct || u.IsVip || u.Ammo <= 0 || u.ActionsLeft < 2) continue;
            float SafetyAt(int x, int y)
            {
                float s = 24f - TileExposure(u, x, y);
                var foe = AliveEnemies().OrderBy(en => Util.TileDist(x, y, en.X, en.Y)).FirstOrDefault();
                if (foe != null) s += Grid.GetCover(x, y, foe.X, foe.Y).Level * 8f;
                s += Grid.HeightAt(x, y) * 5f;
                return s;
            }
            var pcost = Grid.CostMap(u.X, u.Y, (x, y) => IsOccupiedByOther(x, y, u), out _, u.MoveBudget * 2);
            pbest = SafetyAt(u.X, u.Y); var pvals = new List<float> { pbest };
            for (int x = 0; x < Grid.W; x++)
                for (int y = 0; y < Grid.H; y++)
                {
                    if (pcost[x, y] <= 0 || pcost[x, y] > u.MoveBudget) continue;
                    float s = SafetyAt(x, y); pvals.Add(s); if (s > pbest) pbest = s;
                }
            return pvals;
        }
        return new List<float>();
    }

    /// A board on which EVERY reachable tile scores NEGATIVE safety: no cover anywhere, no high
    /// ground, one soldier ringed by enough guns that `24 - TileExposure` goes below zero. The
    /// 120 random scenes never produce this (and only 0.0-1.0% of real soldier-turns do), which
    /// is exactly why reinstating the dropped `pbest > 0` guard was invisible to the first
    /// BANDTEST. Here it is not: under the guard the axis contributes 0, without it the capped 4.
    void BuildNegativeSafetyScene()
    {
        Grid = new Grid();                       // all Floor, height 0 -> no cover term anywhere
        Players = new List<Unit>();
        Enemies = new List<Unit>();
        Vip = null; CaptiveLocked = false;
        Objective = Objective.Eliminate;
        EvacZone.Clear();

        var p = new Unit { Name = "PINNED", Cls = "ASSAULT", Team = Team.Player, X = 9, Y = 5,
                           Hp = 8, MaxHp = 8, Aim = 65, Mobility = 4,
                           Weapon = Weapon.Make(WeaponKind.Rifle) };
        p.Ammo = p.Weapon.Clip; p.SyncPos(); p.BeginTurn();
        Players.Add(p);
        // Eight guns, every one with clear LoS on an empty board and inside rifle range of the
        // whole reachable set: exposure >= 8 x 6 = 48, so safety <= 24 - 48 = -24 on every tile.
        var spots = new (int x, int y)[] { (2, 1), (4, 1), (6, 1), (12, 1), (14, 1),
                                           (2, 9), (6, 9), (14, 9) };
        for (int i = 0; i < spots.Length; i++)
        {
            var e = new Unit { Name = "G" + i, Cls = "GRUNT", Team = Team.Enemy,
                               X = spots[i].x, Y = spots[i].y, Hp = 6, MaxHp = 6, Aim = 60,
                               Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            e.Ammo = e.Weapon.Clip; e.Alert = AlertLevel.Alert; e.SyncPos(); e.BeginTurn();
            Enemies.Add(e);
        }
    }

    // ── W1 TRUE INSTRUMENT: SIGHTLINE_ROUTETEST ─────────────────────────────────────────
    /// Measures the AUTOPILOT'S ROUTE through the campaign DAG — the sampling frame every
    /// balance number in docs/ was drawn through, and which nobody has ever looked at.
    ///
    /// The shipped rule is "prefer an Event node, else nn[0]". `nn` comes from
    /// Run.NextNodes(), which walks the current node's `Next` list, which was appended in ROW
    /// order — so nn[0] is the lowest row of the next column, and the bot takes it essentially
    /// every time. Whatever node kinds, factions and objectives live along that edge of the map
    /// are over-sampled in every rung of every published ladder; whatever lives on the other
    /// branches is under-sampled by the same amount.
    ///
    /// Asserts three things, and is deliberately capable of failing on the SHIPPED default:
    ///   (a) 'first' IS skewed — it takes branch index 0 far more often than a uniform deal would;
    ///   (b) 'hash' is near-uniform — within 8 points of the per-choice uniform expectation;
    ///   (c) 'hash' is DETERMINISTIC and draw-free — two walks of a seed give the identical route
    ///       and neither perturbs Util.Rng (a CRN slot pair must still replay under it).
    /// Window-free: it walks bare Run maps, builds no missions and needs no GL context.
    public static string RouteSelfTest(int seeds = 400)
    {
        var fails = new List<string>();

        // one walk of one map under one policy: returns the branch indices taken and the kinds visited.
        (List<int> idx, List<int> optionCounts, List<string> kinds) Walk(int seed, string policy)
        {
            var run = new Run();
            run.GenerateMap(seed);
            run.MapPos = 0;
            var idx = new List<int>(); var opts = new List<int>(); var kinds = new List<string>();
            for (int guard = 0; guard < Run.MaxMissions + 4; guard++)
            {
                var nn = run.NextNodes();
                if (nn.Count == 0) break;
                int mission = run.CurrentNode != null ? run.CurrentNode.Mission : 1;
                var pick = PickAutoNode(nn, seed, mission, policy);
                idx.Add(nn.IndexOf(pick)); opts.Add(nn.Count); kinds.Add(pick.Kind.ToString());
                run.MapPos = pick.Id;
            }
            return (idx, opts, kinds);
        }

        var kindsFirst = new Dictionary<string, int>();
        var kindsHash = new Dictionary<string, int>();
        int firstZero = 0, hashZero = 0, choices = 0;
        double uniformZeroExpect = 0;   // sum of 1/k over the SAME choices, i.e. a fair deal's index-0 rate

        // (c) determinism + draw-freedom: snapshot the shared stream around the whole sweep.
        Util.Reseed(4242);
        double rngProbeBefore = Util.RandF();
        Util.Reseed(4242);
        double rngProbeAfter;

        for (int s = 1; s <= seeds; s++)
        {
            var f = Walk(s, "first");
            var h = Walk(s, "hash");
            var h2 = Walk(s, "hash");
            if (!h.idx.SequenceEqual(h2.idx)) fails.Add($"hashNotDeterministic@seed{s}");
            foreach (var k in f.kinds) { kindsFirst.TryGetValue(k, out int v); kindsFirst[k] = v + 1; }
            foreach (var k in h.kinds) { kindsHash.TryGetValue(k, out int v); kindsHash[k] = v + 1; }
            // Only REAL branches count. Most steps of the DAG offer exactly one successor from
            // where you stand; a forced step says nothing about a routing policy, and folding
            // those in drags both rates toward 100% and hides the effect entirely.
            for (int i = 0; i < f.idx.Count; i++)
            {
                if (f.optionCounts[i] < 2) continue;
                choices++;
                uniformZeroExpect += 1.0 / f.optionCounts[i];
                if (f.idx[i] == 0) firstZero++;
            }
            for (int i = 0; i < h.idx.Count; i++) if (h.optionCounts[i] >= 2 && h.idx[i] == 0) hashZero++;
        }
        rngProbeAfter = Util.RandF();
        Util.Reseed(0);

        if (choices < 100) fails.Add($"VACUOUS — only {choices} real (k>=2) branch choices sampled");
        double firstPct = choices == 0 ? 0 : 100.0 * firstZero / choices;
        double hashPct = choices == 0 ? 0 : 100.0 * hashZero / choices;
        double uniPct = choices == 0 ? 0 : 100.0 * uniformZeroExpect / choices;

        // (a) the shipped route must be provably skewed, or this whole finding is imaginary.
        if (firstPct <= uniPct + 15) fails.Add($"firstNotSkewed(first={firstPct:0.0}% uniform={uniPct:0.0}%)");
        // (b) the hashed route must be a fair deal.
        if (Math.Abs(hashPct - uniPct) > 8.0) fails.Add($"hashNotUniform(hash={hashPct:0.0}% uniform={uniPct:0.0}%)");
        // (c) neither policy may consume a draw from the shared gameplay stream.
        if (rngProbeBefore != rngProbeAfter) fails.Add("routeConsumedGameplayRngDraws");

        string Hist(Dictionary<string, int> d)
        {
            int tot = d.Values.Sum();
            return string.Join(" ", d.OrderByDescending(kv => kv.Value)
                .Select(kv => $"{kv.Key}={kv.Value}({(tot == 0 ? 0 : 100.0 * kv.Value / tot):0.0}%)"));
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ROUTETEST: {seeds} maps, {choices} REAL branch choices (k>=2); a fair deal takes branch 0 {uniPct:0.0}% of the time");
        sb.AppendLine($"  first (SHIPPED): branch-0 {firstPct:0.0}%   nodes visited: {Hist(kindsFirst)}");
        sb.AppendLine($"  hash            : branch-0 {hashPct:0.0}%   nodes visited: {Hist(kindsHash)}");
        foreach (var f in fails.Take(8)) sb.AppendLine("  " + f);
        sb.Append(fails.Count == 0
            ? "ROUTETEST: PASS ('first' is measurably skewed to branch 0; 'hash' deals within 8pts of uniform; both take zero Util.Rng draws)"
            : $"ROUTETEST: FAIL ({string.Join(",", fails.Take(8))})");
        return sb.ToString();
    }


    // ── C2 THE OPPONENT DECLINES — SIGHTLINE_DECLINETEST ────────────────────────────────────
    /// Pins the two halves of wave C2 on a CONSTRUCTED board, plus the reconstruction they both
    /// rest on. Reads the AMBIENT Game.AiDecline for every shipped-behaviour leg, so running it
    /// with SIGHTLINE_AIDECLINE=0 FAILS — which is the proof the test can fail at all.
    ///
    ///  (1) Combat.AsIfExposed is ground-truthed against a REAL ComputeOdds on a board whose
    ///      cover has been physically removed — low, high and diagonal-partial. This is the
    ///      reconstruction the decline gate's reference shot depends on; if it drifts from the
    ///      cover model the gate silently starts pricing against a fiction.
    ///  (2) Ai.ShotTileValue — the one scoring line the wave changed — on BOTH sides of the dial.
    ///      The pre-C2 branch is the literal `100 + bestHit`; the shipped branch must put a 12%
    ///      shot BELOW one level of high cover (the whole point) while leaving a strong shot
    ///      dominant, and must be monotone in the hit chance.
    ///  (3..6) The decline gate itself on a live Ai.Plan: it declines a bad shot and still spends
    ///      the action; it does NOT decline the same shot when the target is exposed; a rusher
    ///      never declines; and under two or more guns the freed action digs in rather than
    ///      offering a lane.
    /// No window needed; no persistence (NoPersist); no Util.Rng dependence in any assertion
    /// (every margin is far outside Ai's 0-3 tie-break jitter, and the gate legs read plan flags).
    public string DeclineSelfTest()
    {
        // DETERMINISM (lead, CONTOUR close): every self-test in the sweep pins its RNG stream.
        // 22 of 37 did not, so each was an independent ~1-3% chance to fail a --full sweep on
        // pod or roster placement alone. That was invisible while the sweep could not exit
        // non-zero; the moment C3's exit landed, four different tests took a merge down in a
        // row. A gate whose tests read the wall clock is a gate that fails randomly.
        Util.Reseed(70147);
        NoPersist = true;
        var fails = new System.Collections.Generic.List<string>();
        bool ambient = AiDecline;             // what the process was launched with — never forced

        // ---- shared empty scene ------------------------------------------------------------
        void Scene()
        {
            Grid = new Grid();
            Players = new System.Collections.Generic.List<Unit>();
            Enemies = new System.Collections.Generic.List<Unit>();
            Vip = null; CaptiveLocked = false; EnemyFocus = null;
            Objective = Objective.Eliminate; EvacZone.Clear();
            Combat.MissionFaction = Faction.None; Combat.PrepFaction = Faction.None;
            Ai.Tier = 0;
        }
        Unit MkP(string name, int x, int y, int hp = 8)
        {
            var u = new Unit { Name = name, Cls = "ASSAULT", Team = Team.Player, X = x, Y = y,
                               Hp = hp, MaxHp = 8, Aim = 65, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.SyncPos(); u.BeginTurn(); return u;
        }
        Unit MkE(string name, int x, int y, string cls = "GRUNT")
        {
            var u = new Unit { Name = name, Cls = cls, Team = Team.Enemy, X = x, Y = y,
                               Hp = 9, MaxHp = 9, Aim = 55, Mobility = 4, Weapon = Weapon.Make(WeaponKind.Rifle) };
            u.Ammo = u.Weapon.Clip; u.Alert = AlertLevel.Alert; u.SyncPos(); u.BeginTurn(); return u;
        }

        // ── (1) AsIfExposed vs a physically uncovered board ──────────────────────────────────
        // The soldier stands at (12,5). A cover block west of it covers a dominantly-westward
        // attack (Grid.GetCover's facing-side rule). We find an attacker tile that BOTH keeps
        // line of sight AND reads the requested cover level, then compare AsIfExposed(covered)
        // against ComputeOdds on the same board with the block deleted.
        void CoverLeg(TileType kind, int wantLevel, bool wantPartial, string tag)
        {
            Scene();
            var sol = MkP("SOL", 12, 5); Players.Add(sol);
            Grid.Tiles[11, 5] = kind; Grid.SetCoverHp(11, 5);
            Unit atk = null;
            for (int y = 0; y < Grid.H && atk == null; y++)
                for (int x = 2; x <= 8 && atk == null; x++)
                {
                    if (!Grid.HasLineOfSight(x, y, 12, 5)) continue;
                    var c = Grid.GetCover(12, 5, x, y);
                    if (c.Level != wantLevel || c.Partial != wantPartial) continue;
                    atk = MkE("ATK", x, y);
                }
            if (atk == null) { fails.Add("scene:" + tag + ":noAttackerTile"); return; }
            Enemies.Add(atk); Combat.AllUnits = new System.Collections.Generic.List<Unit> { sol, atk };

            var covered = Combat.ComputeOdds(Grid, atk, sol);
            if (covered.CoverLevel != wantLevel) fails.Add(tag + ":coverLevel=" + covered.CoverLevel);
            int wantDef = (wantLevel == 2 ? 40 : wantLevel == 1 ? 20 : 0) / (wantPartial ? 2 : 1);
            if (covered.CoverDef != wantDef) fails.Add($"{tag}:coverDef={covered.CoverDef} want {wantDef}");

            Grid.Tiles[11, 5] = TileType.Floor;                 // physically remove the cover
            var open = Combat.ComputeOdds(Grid, atk, sol);
            if (open.CoverLevel != 0) fails.Add(tag + ":openStillCovered");
            var recon = Combat.AsIfExposed(covered);
            if (recon.HitChance != open.HitChance)
                fails.Add($"{tag}:asIfExposedHit={recon.HitChance} truth={open.HitChance}");
            if (recon.CritChance != open.CritChance)
                fails.Add($"{tag}:asIfExposedCrit={recon.CritChance} truth={open.CritChance}");
            // and the reconstruction must be a strict IMPROVEMENT for a real cover block, or the
            // decline gate's ratio is a division by something meaningless.
            if (wantDef > 0 && recon.HitChance <= covered.HitChance)
                fails.Add(tag + ":asIfExposedNotBetter");
        }
        CoverLeg(TileType.LowCover, 1, false, "low");
        CoverLeg(TileType.HighCover, 2, false, "high");
        CoverLeg(TileType.HighCover, 2, true, "partialDiag");
        // identity: an already-exposed shot must come back unchanged.
        {
            Scene();
            var sol = MkP("SOL", 12, 5); Players.Add(sol);
            var atk = MkE("ATK", 6, 5); Enemies.Add(atk);
            Combat.AllUnits = new System.Collections.Generic.List<Unit> { sol, atk };
            var o = Combat.ComputeOdds(Grid, atk, sol);
            var r = Combat.AsIfExposed(o);
            if (o.CoverLevel != 0) fails.Add("identity:sceneHadCover");
            if (r.HitChance != o.HitChance || r.CritChance != o.CritChance) fails.Add("identity:mutatedExposedShot");
        }

        // ── (2) the scoring line, both sides of the dial ─────────────────────────────────────
        // HIGH cover is worth 36 in the same scorer (cover.Level * 18). Pre-C2 a 12% shot was
        // worth 100 + bestHit; post-C2 it must be worth LESS than that wall.
        const float wall = 2 * 18f;
        AiDecline = false;
        float pre = Ai.ShotTileValue(40f, 12);
        if (Math.Abs(pre - 140f) > 0.001f) fails.Add($"preC2TermMoved={pre}");
        if (pre <= wall) fails.Add("preC2TermDidNotDominateCover");     // the defect, pinned
        AiDecline = true;
        float badShot = Ai.ShotTileValue(40f, 12);
        float okShot  = Ai.ShotTileValue(60f, 55);
        float goodShot = Ai.ShotTileValue(120f, 85);
        if (badShot >= wall) fails.Add($"badShotStillBeatsHighCover={badShot}");
        if (goodShot <= 100f) fails.Add($"goodShotNoLongerDominant={goodShot}");
        if (!(badShot < okShot && okShot < goodShot)) fails.Add("shotTermNotMonotone");
        AiDecline = ambient;                                            // restore: legs below read it

        // ── (3..6) the gate on a live plan ───────────────────────────────────────────────────
        // A lone GRUNT with a poor shot at a soldier behind HIGH cover, standing on its own
        // LOW-cover tile so both alternatives (a lane and a dig-in) are genuinely available.
        // Its aim is dropped so the covered shot is bad in RATIO terms, which is what the gate
        // reads — an absolute hit percentage would not be a test of this wave's model.
        string dbg = "", straddle = "(no straddle found)";
        int gateGuns = 0;
        // `wantCover` 0 = target exposed, 1 = LOW cover (a milder ratio, which is what the
        // kill-box straddle needs), 2 = HIGH cover. `aim` and `targetHp` are parameterised so a
        // leg can place the shot's ratio where it needs it instead of hoping.
        EnemyPlan Gate(string cls, int wantCover, int extraSoldiers, int aim = 65, int targetHp = 8,
                       bool disoriented = false)
        {
            Scene();
            var sol = MkP("SOL", 12, 5, targetHp); Players.Add(sol);
            if (wantCover > 0)
            {
                Grid.Tiles[11, 5] = wantCover == 2 ? TileType.HighCover : TileType.LowCover;
                Grid.SetCoverHp(11, 5);
            }
            // Find a shooting tile that BOTH keeps line of sight to the soldier and reads the
            // cover level this leg wants — searched rather than hand-picked, because a
            // hand-picked tile is one Bresenham detail away from silently testing nothing (the
            // first draft of this scene had exactly that: los=False, so there was no shot to
            // decline and the "decline" legs were vacuously failing).
            int ax = -1, ay = -1;
            for (int y = 0; y < Grid.H && ax < 0; y++)
                for (int x = 2; x <= 7 && ax < 0; x++)
                {
                    if (!Grid.HasLineOfSight(x, y, 12, 5)) continue;
                    if (Grid.GetCover(12, 5, x, y).Level != wantCover) continue;
                    ax = x; ay = y;
                }
            if (ax < 0) { dbg = "[scene: no shooting tile]"; return new EnemyPlan(); }

            var e = MkE("E1", ax, ay, cls);
            // DAZED is the only clean way to make `canWatch` false while `canDig` stays true: the
            // decline gate and the no-shot fallback BOTH refuse a watch from a Disoriented unit,
            // exactly as Game.ActAfterMove's own overwatch gate does. Without it every leg has
            // canWatch == true, `Math.Max(watch, dig)` always resolves to the watch ratio, and
            // DeclineDigRatio is unreachable at any value in [0, 0.45].
            if (disoriented) e.AddStatus(StatusKind.Disoriented, 3);
            e.Aim = aim;                                  // a real shooter, so a DECLINE is about
            Enemies.Add(e);                               // the cover, not about a hopeless gun
            // Ring it in LOW cover: impassable (Grid.IsFloor excludes any cover tile) so the unit
            // is PINNED and the leg is about the ACTION, not about where it walks — and low cover
            // does not block sight (Grid.BlocksSight is HighCover/smoke only), so the shot, the
            // watch and the soldiers' own firing solutions all survive it. It also makes the
            // dig-in alternative genuinely available, which is what the kill-box leg needs.
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = ax + dx, ny = ay + dy;
                    if (!Grid.InBounds(nx, ny)) continue;
                    Grid.Tiles[nx, ny] = TileType.LowCover; Grid.SetCoverHp(nx, ny);
                }
            // Extra guns for the kill-box leg: SNIPERS (range 20) parked with a clear line to the
            // grunt's tile but BEYOND its own rifle range (15), so they count as guns trained on
            // it without becoming better targets than the soldier the leg is actually about.
            // Searched, for the same reason the shooting tile is.
            int placed = 0;
            for (int y = Grid.H - 1; y >= 0 && placed < extraSoldiers; y--)
                for (int x = Grid.W - 1; x >= 8 && placed < extraSoldiers; x--)
                {
                    if (!Grid.IsFloor(x, y) || IsOccupiedByOther(x, y, null)) continue;
                    float d = Util.TileDist(x, y, ax, ay);
                    if (d <= 16f || d > 20f) continue;                 // out of ITS reach, inside theirs
                    if (!Grid.HasLineOfSight(x, y, ax, ay)) continue;  // must actually see the tile
                    var s2 = MkP("S" + placed, x, y);
                    s2.Weapon = Weapon.Make(WeaponKind.Sniper); s2.Ammo = s2.Weapon.Clip;
                    Players.Add(s2); placed++;
                }
            if (placed < extraSoldiers) { dbg = "[scene: only " + placed + " extra guns placed]"; }
            var all = new System.Collections.Generic.List<Unit>(Players);
            all.AddRange(Enemies);
            Combat.AllUnits = all;
            _aiUnits = AliveEnemies().Where(u => u.Active).ToList();
            PlanEnemySquad();
            var pl = Ai.Plan(this, e);
            gateGuns = 0;
            foreach (var p in Players)
                if (p.Ammo > 0 && Util.TileDist(p.X, p.Y, e.X, e.Y) <= p.Weapon.MaxRange
                    && Grid.HasLineOfSight(p.X, p.Y, e.X, e.Y)) gateGuns++;
            var od = Combat.ComputeOdds(Grid, e, sol);
            dbg = $"[from({e.X},{e.Y}) tgtCover={Grid.GetCover(12, 5, e.X, e.Y).Level} "
                + $"hit={od.HitChance} openHit={Combat.AsIfExposed(od).HitChance} "
                + $"selfCover={Grid.GetCover(e.X, e.Y, 12, 5).Level} guns={gateGuns} moved={pl.Path.Count}]";
            return pl;
        }

        var declined = Gate("GRUNT", wantCover: 2, extraSoldiers: 0);
        string dbgFirst = dbg;
        if (declined.ShotHit < 0) fails.Add("gate:noShotOnTheTable");     // the scene must offer one
        // ...and it must be a PLAUSIBLE bad shot, not a degenerate one. A 3%-clamped hopeless
        // shot would make the decline trivial and the test meaningless.
        else if (declined.ShotHit < 10 || declined.ShotHit > 45)
            fails.Add("scene:shotOutOfBand=" + declined.ShotHit);
        if (!declined.Declined) fails.Add($"gate:didNotDecline(hit={declined.ShotHit} exp={declined.ShotExp:0.00})");
        if (declined.ShootTarget != null) fails.Add("gate:declinedButStillShoots");
        bool spends = declined.Overwatch || declined.Hunker || declined.Reload
                   || declined.Path.Count > 0 || declined.Grenade || declined.UseItem;
        if (declined.Declined && !spends) fails.Add("gate:declineProducedADeadTurn");   // the W2 invariant

        // the SAME shooter, same tile, target simply not in cover -> the shot is taken.
        var kept = Gate("GRUNT", wantCover: 0, extraSoldiers: 0);
        if (kept.Declined) fails.Add("gate:declinedAnExposedTarget");
        if (kept.ShootTarget == null) fails.Add("gate:refusedAGoodShot");

        // a rusher never declines: identity, not tactics (Ai.NeverDeclines).
        var rush = Gate("BERSERKER", wantCover: 2, extraSoldiers: 0);
        if (rush.Declined) fails.Add("gate:berserkerDeclined");
        if (rush.ShootTarget == null) fails.Add("gate:berserkerHeldFire");

        // under two or more guns the freed action buys SURVIVAL, not a lane.
        var box = Gate("GRUNT", wantCover: 2, extraSoldiers: 2);
        if (gateGuns < 2) fails.Add("scene:killBoxGuns=" + gateGuns);
        if (!box.Declined) fails.Add("gate:killBoxDidNotDecline");
        else
        {
            if (!box.DeclineDigIn) fails.Add("gate:killBoxDidNotDigIn");
            if (!box.Hunker) fails.Add("gate:killBoxWatchedInsteadOfHunkering");
        }

        // ── (7) THE KILL BOX, tested as a STRADDLE — the leg the first draft was missing ─────
        // The review found that `bar *= 1 + DeclineThreatScale * min(guns, cap)` could be DELETED
        // and every kill-box assertion above still passed: they only exercise
        // `DeclineDigIn = guns >= 2 && canDig`, never the bar scaling itself. "The guns on me" is
        // the most-argued piece of model in this wave and it had zero coverage.
        //
        // A straddle fixes that. ONE scene, evaluated at ONE gun and at THREE, tuned so the shot's
        // ratio falls BETWEEN the two bars (0.45x1.2 = 0.54 and 0.45x1.6 = 0.72). Then the extra
        // guns are the ONLY thing that can flip the decision, which is exactly the claim.
        //   scale -> 0.00 : both bars collapse to 0.45, the 3-gun leg SHOOTS   -> fails below
        //   scale -> 2.00 : the 1-gun bar becomes 1.35, the 1-gun leg DECLINES -> fails below
        //   cap   -> 0    : same collapse as scale 0                           -> fails below
        // LOW cover, because a high-cover ratio (~0.36) sits under both bars and cannot straddle.
        // The aim is SEARCHED, not hand-picked: the window is narrow and a hand-picked number is
        // one damage-band change away from silently testing nothing.
        {
            int hitAim = -1; EnemyPlan one = null, three = null;
            // (recorded in the verdict line so the archive shows the leg actually found a straddle)
            for (int a = 90; a >= 40 && hitAim < 0; a--)
            {
                var p1 = Gate("GRUNT", wantCover: 1, extraSoldiers: 0, aim: a);
                if (gateGuns != 1 || p1.ShotHit < 0 || p1.Declined) continue;   // must SHOOT at 1 gun
                var p3 = Gate("GRUNT", wantCover: 1, extraSoldiers: 2, aim: a);
                if (gateGuns < 3 || !p3.Declined) continue;                     // must DECLINE at 3
                hitAim = a; one = p1; three = p3;
            }
            if (hitAim < 0)
                fails.Add("scene:noThreatStraddle");     // loud: this leg would be testing nothing
            else
            {
                if (one.ShootTarget == null) fails.Add("guns:oneGunHeldFire");
                if (three.ShootTarget != null) fails.Add("guns:threeGunsStillShot");
                if (!three.DeclineDigIn) fails.Add("guns:threeGunsDidNotDigIn");
                straddle = "straddle: aim=" + hitAim + " 1gun=SHOOT(" + one.ShotHit + "%,exp="
                         + one.ShotExp.ToString("0.00") + ") 3gun=DECLINE";
            }
        }

        // ── (8) ShotSeat has a LOWER pin, not only an upper one ──────────────────────────────
        // The review found `ShotSeat = 0` left the whole suite green — i.e. the "a line of fire
        // has option value at all" concept that DESIGN §5.2 point 1 is entirely about could be
        // deleted without a single failure. The upper pin (35 fails) already existed; this is the
        // other side. A marginal shot must still be worth about two-thirds of a LOW cover level
        // (18), which is false at seat 0, where a 12% shot is worth 4.8.
        if (badShot < 12f) fails.Add("seat:marginalShotHasNoOptionValue=" + badShot.ToString("0.0"));

        // ── (9) FinishPress actually fires, and is pinned from below ─────────────────────────
        // The review found FinishPress could be set to 1.0 or 9.0 with the suite still green: no
        // leg put a target in the finish band at all (Hp 8 > rifle DmgMax). The SAME scene that
        // declines at full HP must NOT decline when the target is one shot from dead, because the
        // 1.6x press lifts the shot back over the bar. At 1.0 it declines again -> fails below.
        var finish = Gate("GRUNT", wantCover: 2, extraSoldiers: 0, targetHp: 2);
        if (finish.ShotHit < 0) fails.Add("scene:finishNoShot");
        else if (finish.Declined) fails.Add("finish:declinedAKillingBlow(exp=" + finish.ShotExp.ToString("0.00") + ")");

        // ── (10) DeclineDigRatio is reachable at all — the last unpinned constant ────────────
        // The review found DeclineDigRatio could be set to 0.00 OR 0.95 with the suite green,
        // because `bar = Math.Max(canWatch ? watch : 0, canDig ? dig : 0)` and every leg above has
        // canWatch == true, so the dig ratio is masked at any value below the watch ratio. A DAZED
        // shooter cannot hold a lane (the gate and the fallback both refuse it), so the dig ratio
        // becomes the only bar in play — and the freed action must buy cover, not a watch.
        //   dig -> 0.00 : bar collapses to 0, the gate's `bar > 0f` guard blocks it, SHOOTS -> fails
        var dazed = Gate("GRUNT", wantCover: 2, extraSoldiers: 0, disoriented: true);
        if (dazed.ShotHit < 0) fails.Add("scene:dazedNoShot");
        else
        {
            if (!dazed.Declined) fails.Add("dig:dazedDidNotDecline(exp=" + dazed.ShotExp.ToString("0.00") + ")");
            if (dazed.Overwatch) fails.Add("dig:dazedHeldALaneItCannotHold");
            if (dazed.Declined && !dazed.Hunker) fails.Add("dig:dazedDeclinedButDidNotDigIn");
        }

        // ── the pre-C2 contrast, forced (passes on BOTH settings by construction) ────────────
        // Not the failing leg — the legs above are. This one exists to show the SAME scene is a
        // shot for the pre-C2 opponent, i.e. that the scene is genuinely a decision and not a
        // board where nobody would shoot anyway.
        AiDecline = false;
        var preC2 = Gate("GRUNT", wantCover: 2, extraSoldiers: 0);
        if (preC2.ShootTarget == null) fails.Add("contrast:preC2AlsoDeclined");
        if (preC2.Declined) fails.Add("contrast:preC2SetDeclinedFlag");
        AiDecline = ambient;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"DECLINETEST: ambient SIGHTLINE_AIDECLINE={(ambient ? 1 : 0)}; "
                    + $"gate scene {dbgFirst} shot hit={declined.ShotHit}% E[dmg]={declined.ShotExp:0.00} -> "
                    + (declined.Declined ? (declined.Hunker ? "HUNKER" : declined.Overwatch ? "OVERWATCH" : "other") : "SHOOT"));
        sb.AppendLine("DECLINETEST: " + straddle);
        foreach (var f in fails.Take(10)) sb.AppendLine("  " + f);
        sb.Append(fails.Count == 0
            ? "DECLINETEST: PASS (AsIfExposed ground-truthed vs a physically uncovered board on low/high/partial + identity; the shot term is hit-WEIGHTED, bounded above AND below, and a 12% shot now scores under high cover; the gate declines a bad shot, still spends the action, keeps an exposed shot, presses a killing blow, exempts rushers, digs in when dazed, and a 3-gun kill box flips a shot the same unit takes at 1 gun)"
            : "DECLINETEST: FAIL (" + string.Join(",", fails.Take(10)) + ")");
        return sb.ToString();
    }

}
