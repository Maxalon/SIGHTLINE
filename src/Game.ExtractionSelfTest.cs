using System;
using System.Collections.Generic;
using System.Linq;

namespace Sightline;

/// SIGHTLINE_EXTRACTIONTEST — B1, the owner's extraction rule (`docs/DESIGN.md` §6.6).
///
/// It stages real campaign missions through `SetupMission` and reads the PHASE the game lands in,
/// not a helper's return value, because the defect B1 fixes is a mission that ends too EARLY:
///
/// (A) 18x11 is untouched: no extraction mission there runs the new rules.
/// (B) EVAC on a big board: the whole squad in the zone does NOT end the mission (the old rule did,
///     and the control arm proves it still would). BOARD takes a soldier off the field.
/// (C) CALL EVAC: the boarded and the in-zone get out, a soldier outside the zone is LEFT BEHIND and
///     dies, and the mission is won with the survivors on the roster.
/// (D) Boarding everyone ends the mission on its own.
/// (E) ESCORT: no CALL EVAC while the asset is on the ground outside the zone; a dead asset that
///     never got out still loses the run.
/// (F) `ExtractionModel = false` restores the pre-B1 rule on the same big board.
/// (G)-(J) B2: HACK/SABOTAGE hide their evac until the task is done, a cleared field wins, no
///     respawns on a big board, and 18x11 still ends on the task.
///
/// WHAT IT CANNOT SEE: whether the withdrawal is TENSE. That needs B3's reinforcements and a
/// measured big-board round.
public partial class Game
{
    public static string ExtractionSelfTest()
    {
        var fails = new List<string>();
        var detail = new System.Text.StringBuilder();
        int w0 = Cfg.GridW, h0 = Cfg.GridH, t0 = Cfg.Tile;
        bool m0 = ExtractionModel;

        Game Stage(int w, int h, Objective obj, int seed = 4242)
        {
            Cfg.SetBoard(w, h, w > Mission.RefW ? 32 : 64);
            Util.Reseed(seed);
            var g = new Game { NoPersist = true, ForcedObjective = obj };
            g._run = new Run(); g._run.Start(); g._run.HeatLevel = 0;
            g._run.Mission = 2;
            g.SetupMission(2);
            g.Phase = Phase.PlayerTurn;
            g._anims.Clear();
            return g;
        }
        // seat every living soldier (and the asset, if asked) on distinct zone tiles
        void IntoZone(Game g, IEnumerable<Unit> who)
        {
            var free = new Queue<(int x, int y)>(g.EvacZone);
            foreach (var u in who)
            {
                if (free.Count == 0) break;
                var t = free.Dequeue(); u.X = t.x; u.Y = t.y; u.SyncPos();
            }
        }
        List<Unit> Soldiers(Game g) => g.Players.Where(p => p.Alive && !p.IsVip).ToList();

        try
        {
            ExtractionModel = true;

            // ── (A) 18x11 keeps today's rules ─────────────────────────────────────────────────
            foreach (var o in new[] { Objective.Evac, Objective.Escort, Objective.Rescue })
            {
                var g = Stage(18, 11, o);
                if (g.ExtractionLive) fails.Add($"(A) 18x11 {o}: the extraction rules are live on the tutorial board");
            }

            // ── (B) the whole squad in the zone does not end a big-board EVAC ─────────────────
            {
                var g = Stage(36, 22, Objective.Evac);
                if (!g.ExtractionLive) fails.Add("(B) 36x22 EVAC: the extraction rules are not live");
                if (g.EvacZone.Count < Soldiers(g).Count) fails.Add($"(B) zone of {g.EvacZone.Count} cannot seat {Soldiers(g).Count}");
                IntoZone(g, Soldiers(g));
                g.CheckEnd();
                if (g.Phase != Phase.PlayerTurn) fails.Add($"(B) the squad standing in the zone ended the mission ({g.Phase}) — only BOARD/CALL may");
                var first = Soldiers(g)[0];
                g.Selected = first;
                if (!g.CanBoard(first)) fails.Add("(B) a soldier in the zone with actions cannot BOARD");
                g.DoBoard();
                if (g.Players.Contains(first) || !g.Aboard.Contains(first)) fails.Add("(B) BOARD did not take the soldier off the field");
                if (g.Phase != Phase.PlayerTurn) fails.Add($"(B) one soldier boarding ended the mission ({g.Phase})");

                // ── (C) CALL EVAC leaves the soldier outside the zone behind ──────────────────
                var outside = Soldiers(g).Last();
                (int x, int y) far = (2, g.Grid.H / 2);
                outside.X = far.x; outside.Y = far.y; outside.SyncPos();
                var mustKeep = Soldiers(g).Where(p => p != outside).Concat(g.Aboard).ToList();
                int squadBefore = mustKeep.Count + 1;
                if (!g.CanCallEvac) fails.Add("(C) CALL EVAC unavailable with a soldier aboard");
                g.DoCallEvac();
                if (outside.Alive) fails.Add("(C) the soldier outside the zone survived the CALL — it must be left behind");
                if (g.LeftBehindLastMission != 1) fails.Add($"(C) left-behind count {g.LeftBehindLastMission}, want 1");
                if (g.Phase == Phase.PlayerTurn || g.Phase == Phase.Lose) fails.Add($"(C) CALL EVAC did not win the mission ({g.Phase})");
                int kept = mustKeep.Count(k => g._run.Squad.Contains(k));
                if (kept != mustKeep.Count) fails.Add($"(C) only {kept} of the {mustKeep.Count} boarded/in-zone soldiers are on the roster after the CALL");
                if (g._run.Squad.Contains(outside)) fails.Add("(C) the abandoned soldier is still on the roster");
                detail.Append($"EVAC call: {squadBefore} -> {kept} kept, 1 left behind; ");
            }

            // ── (D) boarding everyone ends the mission on its own ─────────────────────────────
            {
                var g = Stage(36, 22, Objective.Evac, 777);
                IntoZone(g, Soldiers(g));
                var all = Soldiers(g); int n = all.Count;
                foreach (var s in all) { g.Selected = s; g.DoBoard(); }
                if (g.Phase == Phase.PlayerTurn) fails.Add("(D) every soldier aboard and the mission did not end");
                int keptD = all.Count(k => g._run.Squad.Contains(k));
                if (keptD != n) fails.Add($"(D) {n} boarded, only {keptD} of them on the roster after");
                detail.Append($"all-aboard ends it ({n} kept); ");
            }

            // ── (E) ESCORT: the asset gates the CALL; a lost asset still loses ───────────────
            {
                var g = Stage(36, 22, Objective.Escort, 909);
                if (g.Vip == null) fails.Add("(E) ESCORT staged no asset");
                else
                {
                    IntoZone(g, Soldiers(g));
                    g.Vip.X = 2; g.Vip.Y = g.Grid.H / 2; g.Vip.SyncPos();
                    var s0 = Soldiers(g)[0]; g.Selected = s0; g.DoBoard();
                    if (g.CanCallEvac) fails.Add("(E) CALL EVAC offered with the asset still on the ground outside the zone");
                    g.CheckEnd();
                    if (g.Phase != Phase.PlayerTurn) fails.Add($"(E) the mission ended with the asset on the ground ({g.Phase})");
                    g.Vip.Hp = 0; g.Vip.Alive = false;
                    g.CheckEnd();
                    if (g.Phase != Phase.Lose) fails.Add($"(E) a dead asset that never got out did not lose the run ({g.Phase})");
                }
            }

            // ── (G) B2: a big-board HACK hides its evac until the task is done ───────────────
            {
                var g = Stage(36, 22, Objective.Hack, 515);
                if (!g.TaskExtractRules) fails.Add("(G) 36x22 HACK: the task-extraction rules are not live");
                if (g.EvacZone.Count != 0) fails.Add("(G) the evac is visible before the task is done");
                if (g.PendingEvac.Count < Soldiers(g).Count) fails.Add($"(G) the reserved evac seats {g.PendingEvac.Count}, the squad is {Soldiers(g).Count}");
                int blocked = g.PendingEvac.Count(t => !g.Grid.IsFloor(t.x, t.y));
                if (blocked > 0) fails.Add($"(G) {blocked} reserved evac tiles are not floor — Build did not keep the block clear");
                int near = g.PendingEvac.Min(t => Util.ChebyDist(t.x, t.y, g.Terminal.x, g.Terminal.y));
                if (near < g.Grid.W / 3) fails.Add($"(G) the evac opens {near} tiles from the terminal — not a withdrawal");
                g.HackProgress = HackRequired;
                g.CheckEnd();
                if (g.Phase != Phase.PlayerTurn) fails.Add($"(G) finishing the hack ended the mission ({g.Phase}) — it must open the evac");
                if (!g.EvacOpen || !new HashSet<(int, int)>(g.EvacZone).SetEquals(g.PendingEvac)) fails.Add("(G) finishing the hack did not open the reserved evac");
                IntoZone(g, Soldiers(g));
                foreach (var s0 in Soldiers(g)) { g.Selected = s0; g.DoBoard(); }
                if (g.Phase == Phase.PlayerTurn || g.Phase == Phase.Lose) fails.Add($"(G) the whole squad aboard after the hack did not win ({g.Phase})");
                detail.Append($"HACK evac opens {near} tiles from the terminal; ");
            }

            // ── (H) B2: GUNS BLAZING — a cleared field wins a task mission, task or no task ─────
            {
                var g = Stage(36, 22, Objective.Sabotage, 616);
                if (g.SabotageBlown.Count != 0) fails.Add("(H) staged with charges already blown");
                foreach (var e in g.Enemies) { e.Hp = 0; e.Alive = false; }
                g.CheckEnd();
                if (g.Phase == Phase.PlayerTurn || g.Phase == Phase.Lose) fails.Add($"(H) every hostile dead did not end a big-board SABOTAGE ({g.Phase})");
            }

            // ── (I) B2: no respawns on a big-board task mission; 18x11 keeps the clock's waves ──
            {
                var g = Stage(36, 22, Objective.Hack, 717);
                if (g.ClockMayReinforce) fails.Add("(I) the pressure clock may still reinforce a big-board HACK");
                var g0 = Stage(18, 11, Objective.Hack, 717);
                if (!g0.ClockMayReinforce) fails.Add("(I) 18x11 HACK lost the clock's reinforcement arm");
            }

            // ── (J) 18x11 HACK still ends on the task ────────────────────────────────────────
            {
                var g = Stage(18, 11, Objective.Hack, 818);
                g.HackProgress = HackRequired;
                g.CheckEnd();
                if (g.Phase == Phase.PlayerTurn) fails.Add("(J) 18x11 HACK no longer ends when the hack completes");
            }

            // ── (F) the flag restores the pre-B1 rule on the same big board ──────────────────
            {
                ExtractionModel = false;
                var g = Stage(36, 22, Objective.Evac);
                if (g.ExtractionLive) fails.Add("(F) SIGHTLINE_EXTRACTION=0 left the rules live");
                IntoZone(g, Soldiers(g));
                g.CheckEnd();
                if (g.Phase == Phase.PlayerTurn) fails.Add("(F) the control: the old rule should end EVAC with the squad in the zone, and did not — (B) proves nothing without it");
                var gh = Stage(36, 22, Objective.Hack);
                if (gh.TaskExtractRules || gh.PendingEvac.Count > 0) fails.Add("(F) the flag left B2's hidden evac in place");
                ExtractionModel = true;
            }
        }
        catch (Exception e) { fails.Add($"threw: {e.GetType().Name}: {e.Message}"); }
        finally { ExtractionModel = m0; Cfg.SetBoard(w0, h0, t0); }

        return fails.Count == 0
            ? "EXTRACTIONTEST: PASS (18x11 keeps today's rules; on a big board the squad standing in the zone does not end "
              + "an EVAC (the flag-off control does), BOARD takes a soldier off the field, CALL EVAC wins with the boarded and "
              + "the in-zone and leaves the soldier outside behind dead and off the roster, boarding everyone ends the mission "
              + "on its own, ESCORT will not call the bird with the asset on the ground, and a lost asset still loses; B2: a "
              + "big-board HACK hides a clear evac far from the terminal and opens it on completion instead of ending, a cleared "
              + "field wins a SABOTAGE outright, the clock fields no respawns there, and 18x11 HACK still ends on the task) [" + detail + "]"
            : "EXTRACTIONTEST: FAIL (" + string.Join(" | ", fails.Distinct()) + ") [" + detail + "]";
    }
}
