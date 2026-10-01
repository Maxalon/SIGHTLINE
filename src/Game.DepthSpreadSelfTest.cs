using System;
using System.Collections.Generic;
using System.Linq;

namespace Sightline;

/// SIGHTLINE_DEPTHSPREADTEST — P57. The hostile force is spread through the board's DEPTH on a big
/// board, and the shipped 18x11 board is byte-identical.
///
/// It reads what the BOARD BUILT — every hostile's tile, and the whole-board signature over all
/// eight per-tile layers, the edge layer and every unit — never the shift table itself. The one
/// consumer that could have quietly undone the spread is the collision relocate, which threw a body
/// back to the far edge; leg (E) exists for it.
///
/// ⚠ LEG (E) WAS NOT IN THE FIRST VERSION, AND THE FIRST VERSION'S DOC CLAIMED IT WAS COVERED. It
/// said leg (C) guarded the relocate. It did not: with the relocate reverted to the far edge the
/// gate still PASSED, because at its single fixed seed no shifted pod ever collided. A survey then
/// measured shifted-pod collisions in ~28% of builds (118-127 per 432) — common in play, invisible
/// to one seed. Leg (E) sweeps enough builds to exercise the path, FAILS if it exercised none, and
/// asserts the invariant the regression breaks. PARALLAX's thesis, on this wave's own gate.
///
/// WHAT IT CANNOT SEE: whether a spread board PLAYS better. That is a measured question on a big
/// board and it has no baseline yet (a big board is a different game — `docs/DESIGN.md` §6.5).
public partial class Game
{
    public static string DepthSpreadSelfTest()
    {
        var fails = new List<string>();
        var detail = new System.Text.StringBuilder();
        int savedW = Cfg.GridW, savedH = Cfg.GridH, savedTile = Cfg.Tile;
        bool savedSpread = Mission.DepthSpread;
        int savedDeploy = Mission.ForcedDeploy;
        var shapes = new[] { Mission.DeployFrontal, Mission.DeployPincer, Mission.DeployCrossfire, Mission.DeployEnvelop };
        string[] shapeName = { "FRONTAL", "PINCER", "CROSSFIRE", "ENVELOP" };

        (string board, List<(int x, int y)> foes, int nFoes, List<(int x, int y)> squad) Build(int w, int h, int shape, bool spread, int seed, Objective obj, int mission)
        {
            Cfg.SetBoard(w, h, 32);
            Mission.DepthSpread = spread;
            Mission.ForcedDeploy = shape;
            Util.Reseed(seed);
            var g = new Game { NoPersist = true, ForcedObjective = obj };
            g._run = new Run(); g._run.Start(); g._run.HeatLevel = 0;
            g.SetupMission(mission);
            return (g.BoardSignature(),
                    g.Enemies.Where(e => e.Alive).Select(e => (e.X, e.Y)).ToList(),
                    g.Enemies.Count(e => e.Alive),
                    g.Players.Where(p => p.Alive && !p.IsVip).Select(p => (p.X, p.Y)).ToList());
        }

        try
        {
            // ── (A) THE SHIPPED BOARD IS BYTE-IDENTICAL ──────────────────────────────────────
            // Every shape, several seeds, two objectives and two missions: the whole-board signature
            // (all eight layers + edges + every unit) must not move. If it did, the RNG stream would
            // have moved, and every CRN pairing and the ladder of record with it.
            int cellsA = 0;
            for (int si = 0; si < shapes.Length; si++)
                foreach (int seed in new[] { 101, 202, 303 })
                    foreach (var obj in new[] { Objective.Eliminate, Objective.Hack })
                        foreach (int m in new[] { 1, 4 })
                        {
                            var on = Build(18, 11, shapes[si], true, seed, obj, m);
                            var off = Build(18, 11, shapes[si], false, seed, obj, m);
                            cellsA++;
                            if (on.board != off.board)
                                fails.Add($"(A) 18x11 {shapeName[si]} seed {seed} {obj} m{m}: board signature moved ({off.board} -> {on.board})");
                        }
            detail.Append($"18x11 byte-identical on {cellsA} cells; ");

            // ── (B) ON A BIG BOARD THE FORCE IS SPREAD THROUGH THE DEPTH ─────────────────────
            foreach (var (w, h) in new[] { (36, 22), (48, 30) })
                foreach (int si in new[] { 0, 1, 2 })        // the three west-squad shapes
                {
                    var on = Build(w, h, shapes[si], true, 555, Objective.Eliminate, 3);
                    var off = Build(w, h, shapes[si], false, 555, Objective.Eliminate, 3);
                    int spanOn = on.foes.Max(f => f.x) - on.foes.Min(f => f.x);
                    int spanOff = off.foes.Max(f => f.x) - off.foes.Min(f => f.x);
                    detail.Append($"{w}x{h} {shapeName[si]} foe x {off.foes.Min(f => f.x)}-{off.foes.Max(f => f.x)} -> {on.foes.Min(f => f.x)}-{on.foes.Max(f => f.x)}; ");

                    // the spread must be REAL — the depth the force occupies must grow substantially
                    if (spanOn < spanOff + (w - Mission.RefW) / 2)
                        fails.Add($"(B) {w}x{h} {shapeName[si]}: foe depth span {spanOff} -> {spanOn}, expected it to grow by at least {(w - Mission.RefW) / 2}");
                    // the SAME bodies — P56's gate, restated here where a spread could break it
                    if (on.nFoes != off.nFoes)
                        fails.Add($"(B) {w}x{h} {shapeName[si]}: {off.nFoes} hostiles became {on.nFoes} — the spread must move bodies, never add them");

                    // ── (C) THE STANDOFF: no body closer to the squad than the REFERENCE allows ──
                    // The nearest a pod may come is where its anchor falls on 18x11. Measured as the
                    // gap between the squad's front column and the nearest hostile column, against
                    // the same gap on the reference board.
                    var refB = Build(18, 11, shapes[si], true, 555, Objective.Eliminate, 3);
                    int refGap = refB.foes.Min(f => f.x) - refB.squad.Max(p => p.x);
                    int gap = on.foes.Min(f => f.x) - on.squad.Max(p => p.x);
                    if (gap < refGap - 2)
                        fails.Add($"(C) {w}x{h} {shapeName[si]}: nearest hostile is {gap} columns from the squad against the reference's {refGap} — the spread made the opening an ambush");
                }

            // ── (E) THE RELOCATE LANDS AT THE POD'S OWN DEPTH — and the leg cannot pass empty ──
            // Invariant: every body's column plus its pod's shift is at most the far column. A
            // relocate that ignored the shift puts a shifted body at W-4..W-2 and breaks it. The
            // sweep is broad on purpose (one seed exercised zero collisions), and the leg FAILS if
            // it saw no shifted-pod relocation, because a relocate check that never relocated is a
            // comment.
            {
                // AIMED, not broad. A breakdown over 432 builds found shifted-pod collisions ONLY
                // at missions 2 and 6 (the i/2-pair missions) under PINCER and CROSSFIRE, where a
                // pair's members share their lead's anchor tile. The first version of this leg
                // swept missions 1/3/5 and exercised exactly zero — the non-vacuity check below is
                // what said so. Forcing the shape makes the leg independent of the deck's mix.
                int builds = 0, shiftedRel = 0, bad = 0;
                string firstBad = null;
                foreach (int shape in new[] { Mission.DeployPincer, Mission.DeployCrossfire })
                    foreach (var obj in new[] { Objective.Eliminate, Objective.Hack })
                    foreach (int m in new[] { 2, 6 })
                        foreach (int heat in new[] { 0, 8 })
                            for (int seed = 0; seed < 3; seed++)
                            {
                                Cfg.SetBoard(36, 22, 32);
                                Mission.DepthSpread = true;
                                Mission.ForcedDeploy = shape;
                                Util.Reseed(1000 + seed * 7 + m);
                                var g = new Game { NoPersist = true, ForcedObjective = obj };
                                g._run = new Run(); g._run.Start(); g._run.HeatLevel = heat;
                                g.SetupMission(m);
                                builds++; shiftedRel += Mission.LastShiftedRelocations;
                                foreach (var e in g.Enemies)
                                {
                                    if (e.PodId < 0) continue;
                                    int sh = Mission.DepthShift(Mission.AppliedDeploy, e.PodId, g.Grid.W);
                                    if (sh > 0 && e.X + sh > g.Grid.W - 1)
                                    {
                                        bad++;
                                        firstBad ??= $"{obj} m{m} h{heat}: pod {e.PodId} (shift {sh}) body at x={e.X} on a {g.Grid.W}-wide board";
                                    }
                                }
                            }
                detail.Append($"relocate sweep: {builds} builds, {shiftedRel} shifted-pod relocations exercised, {bad} bodies off their depth; ");
                if (shiftedRel == 0)
                    fails.Add($"(E) {builds} builds exercised ZERO shifted-pod relocations — this leg is vacuous, widen the sweep");
                if (bad > 0)
                    fails.Add($"(E) {bad} bodies of shifted pods landed beyond their pod's depth (first: {firstBad}) — the collision relocate is throwing them back to the far edge");
            }

            // ── (D) ENVELOP IS EXEMPT, AND THE FLAG IS AN EXACT RESTORE ───────────────────────
            {
                var on = Build(36, 22, Mission.DeployEnvelop, true, 777, Objective.Eliminate, 3);
                var off = Build(36, 22, Mission.DeployEnvelop, false, 777, Objective.Eliminate, 3);
                if (on.board != off.board) fails.Add("(D) ENVELOP moved under the spread — it is declared exempt");
                if (Mission.DepthShift(Mission.DeployFrontal, 1, 18) != 0)
                    fails.Add("(D) DepthShift is non-zero at the reference width");
            }

            // ── (F) 0d+1: ENVELOP IS CENTRED AND RINGED ON A BIG BOARD ───────────────────────
            //     Its squad table and rim anchors were ABSOLUTE 18x11 coordinates, so on 36x22 the
            //     "surrounded" squad opened in the north-west quarter with one pod 7 tiles off and
            //     another 26. Read off the BUILT board, against the flag's off arm.
            Mission.DepthSpread = true;
            bool savedEnv = Mission.EnvelopCentre;
            try
            {
                int cellsF = 0;
                foreach (int seed in new[] { 101, 202, 303 })
                    foreach (var obj in new[] { Objective.Eliminate, Objective.Defend, Objective.Decapitate })
                        foreach (int m in new[] { 1, 4, 6 })
                        {
                            Mission.EnvelopCentre = true;  var on = Build(18, 11, Mission.DeployEnvelop, true, seed, obj, m);
                            Mission.EnvelopCentre = false; var off = Build(18, 11, Mission.DeployEnvelop, true, seed, obj, m);
                            cellsF++;
                            if (on.board != off.board)
                                fails.Add($"(F) 18x11 ENVELOP seed {seed} {obj} m{m}: board signature moved under EnvelopCentre");
                        }
                detail.Append($"ENVELOP 18x11 byte-identical on {cellsF} cells; ");

                int offLopsided = 0, cellsBig = 0;
                int Cheb((int x, int y) a, (int x, int y) b) => Math.Max(Math.Abs(a.x - b.x), Math.Abs(a.y - b.y));
                foreach (var (w, h) in new[] { (36, 22), (48, 30) })
                    foreach (int seed in new[] { 555, 556, 557 })
                        foreach (int m in new[] { 1, 4 })
                        {
                            Mission.EnvelopCentre = true;  var on = Build(w, h, Mission.DeployEnvelop, true, seed, Objective.Eliminate, m);
                            Mission.EnvelopCentre = false; var off = Build(w, h, Mission.DeployEnvelop, true, seed, Objective.Eliminate, m);
                            Mission.EnvelopCentre = true;  var refB = Build(18, 11, Mission.DeployEnvelop, true, seed, Objective.Eliminate, m);
                            string tag = $"{w}x{h} seed {seed} m{m}";
                            cellsBig++;
                            if (Mission.AppliedDeploy != Mission.DeployEnvelop) { fails.Add($"(F) {tag}: the build did not deploy ENVELOP — the leg tested nothing"); continue; }

                            (double x, double y) Cen(List<(int x, int y)> l) => (l.Average(p => p.x), l.Average(p => p.y));
                            var c = Cen(on.squad); var c0 = Cen(off.squad);
                            // (F1) the squad opens at the board's centre, as it does on 18x11
                            var rc = Cen(refB.squad);
                            double wantX = rc.x + (w - Mission.RefW) / 2, wantY = rc.y + (h - Mission.RefH) / 2;
                            if (Math.Abs(c.x - wantX) > 0.01 || Math.Abs(c.y - wantY) > 0.01)
                                fails.Add($"(F1) {tag}: squad centroid ({c.x:F1},{c.y:F1}), want the 18x11 seat carried to the centre ({wantX:F1},{wantY:F1})");
                            // (F2) NOT LOPSIDED: the defect was a squad in a CORNER of a ring, so one
                            //      rim was most of the board away (36x22: 27 tiles). From the centre no
                            //      hostile can be further than half the board's longer side.
                            var cc = ((int)Math.Round(c.x), (int)Math.Round(c.y));
                            var c0i = ((int)Math.Round(c0.x), (int)Math.Round(c0.y));
                            int far = on.foes.Max(f => Cheb(f, cc)), farOff = off.foes.Max(f => Cheb(f, c0i));
                            int halfSide = (Math.Max(w, h) + 1) / 2;
                            if (far > halfSide)
                                fails.Add($"(F2) {tag}: farthest hostile {far} tiles from the squad, more than half the board ({halfSide}) — lopsided (off arm {farOff})");
                            if (farOff > halfSide) offLopsided++;
                            // (F3) the standoff: no hostile nearer than the shipped board puts one
                            var ci = ((int)Math.Round(c.x), (int)Math.Round(c.y));
                            var rci = ((int)Math.Round(rc.x), (int)Math.Round(rc.y));
                            int near = on.foes.Min(f => Cheb(f, ci)), refNear = refB.foes.Min(f => Cheb(f, rci));
                            if (near < refNear - 1)
                                fails.Add($"(F3) {tag}: nearest hostile {near} tiles from the squad centre against the reference's {refNear} — an ambush");
                            // (F5) A RING WITH DEPTH: centring the squad alone would leave every pod on
                            //      the far rim (36x22: all of them 15-17 tiles out). The span between
                            //      the nearest and farthest hostile must grow past the reference's by
                            //      half the smaller extra dimension, as P57's leg (B) asks of the others.
                            int refFar = refB.foes.Max(f => Cheb(f, rci));
                            int needSpan = (refFar - refNear) + Math.Min(w - Mission.RefW, h - Mission.RefH) / 2;
                            if (far - near < needSpan)
                                fails.Add($"(F5) {tag}: hostiles {near}-{far} tiles out, a depth span of {far - near}; want at least {needSpan} (reference {refNear}-{refFar})");
                            // (F4) the same bodies
                            if (on.nFoes != off.nFoes) fails.Add($"(F4) {tag}: {off.nFoes} hostiles became {on.nFoes}");
                            if (seed == 555)
                                detail.Append($"ENVELOP {w}x{h} m{m} squad ({c0.x:F0},{c0.y:F0})->({c.x:F0},{c.y:F0}) foe dist {off.foes.Min(f => Cheb(f, ((int)c0.x, (int)c0.y)))}-{off.foes.Max(f => Cheb(f, ((int)c0.x, (int)c0.y)))} -> {near}-{on.foes.Max(f => Cheb(f, ci))} (ref {refNear}); ");
                        }
                detail.Append($"ENVELOP off arm lopsided on {offLopsided}/{cellsBig} big-board cells; ");
                if (offLopsided == 0) fails.Add("(F2) the off arm was never lopsided — the leg cannot tell the fix from the defect");
                Mission.EnvelopCentre = false;
                if (Mission.EnvelopOffset(36, 22) != (0, 0)) fails.Add("(F) the flag's off arm still carries the ENVELOP seat");
            }
            finally { Mission.EnvelopCentre = savedEnv; }
        }
        catch (Exception e) { fails.Add($"threw: {e.Message}"); }
        finally
        {
            Mission.DepthSpread = savedSpread; Mission.ForcedDeploy = savedDeploy;
            Cfg.SetBoard(savedW, savedH, savedTile);
        }

        return fails.Count == 0
            ? "DEPTHSPREADTEST: PASS (the shipped 18x11 board is byte-identical - all eight layers, edges and every unit - "
              + "across all four deployment shapes, three seeds, two objectives and two missions; on 36x22 and 48x30 the "
              + "SAME bodies occupy a much deeper span of the board; no hostile opens closer to the squad than the reference "
              + "allows; across a 48-build sweep AIMED at the collision relocate (and failing if it exercised none), every body of a shifted pod "
              + "stays at its pod's depth; ENVELOP is exempt from the spread and the flag is an exact restore; 0d+1: ENVELOP "
              + "is byte-identical on 18x11 under its own flag, and on 36x22 and 48x30 its squad opens at the board's centre "
              + "no hostile further than half the board (the off arm is lopsided, and the leg fails if it never is), none nearer than the shipped board puts one, the ring has DEPTH (its span grows past the reference's), the same bodies) [" + detail + "]"
            : "DEPTHSPREADTEST: FAIL (" + string.Join(" | ", fails.Distinct()) + ") [" + detail + "]";
    }
}
