using System;

namespace Sightline;

/// ════════════════════ P28 — THE EDGE LAYER: WALLS BETWEEN TILES ════════════════════
/// A wall belongs on the BOUNDARY between two tiles, not on a tile of its own. A soldier can
/// hold either face of it, it blocks sight and gives cover, and it consumes no floor — which is
/// what a building wall actually is and what a `TileType.HighCover` tile can never be.
///
/// WHY THIS IS A GENERALISATION AND NOT A REWRITE. `Grid.GetCover` was already an edge model in
/// disguise: its `LevelAt(sx, sy)` asks "does the neighbouring tile have cover?", which is only
/// ever a proxy for "is there something on the edge between us?". A cover TILE is just an object
/// that blocks all four of its own edges at once. So the edge layer slots in underneath the
/// existing cover model rather than replacing it, and `Ai.cs` gains ZERO lines — exactly as C4
/// and P16 did, and for the same reason: both teams read one truth through
/// `Grid.GetCover` / `CostMap` / `HasLineOfSight`.
///
/// STORED ONCE PER EDGE. `Grid.EdgeV[x,y]` is the edge on the WEST side of tile (x,y);
/// `Grid.EdgeH[x,y]` is the edge on its NORTH side. Every boundary therefore has exactly one
/// home, so "the two tiles disagree about the wall between them" is not a bug that can be
/// written — it is a state that cannot be represented.
///
/// NOT A SAVE-FORMAT BREAK. `EdgeKind` is NOT one of the thirteen persisted-by-ordinal enums:
/// `Grid` never enters `SaveGame` (a mission is rebuilt from the map seed), so this enum may be
/// reordered freely. Verified by grep, not assumed.
public enum EdgeKind : byte
{
    None = 0,   // open boundary
    Low  = 1,   // waist high: stops a mover, gives LOW cover, you SEE and SHOOT over it
    High = 2,   // full height: stops a mover, gives HIGH cover, blocks sight
    Door = 3,   // an opening in a wall: passable, sight passes, and it shelters nobody
}

public static class Edges
{
    /// THE RESTORE FLAG (`SIGHTLINE_EDGES=0`). Off, every edge query answers `None`, so the board
    /// is the pre-P28 board exactly — not approximately. It gates the QUERIES rather than the
    /// arrays, so a harness that stamped edges by hand still gets the old behaviour.
    ///
    /// This is a LEVEL lever the moment a map actually declares an edge, so it severs the CRN
    /// stream like W1 did. Until then it is inert BY CONSTRUCTION: nothing builds an edge, so
    /// `Grid.AnyEdges` is false on every board and every predicate below short-circuits.
    public static bool Enabled = true;

    /// Height in world units, for the 3D view. Mirrors View3D's own LowH/HighH so the projected
    /// board and the rules cannot disagree about what "high" means.
    public const float LowH = 0.55f, HighH = 1.9f;

    /// A mover is stopped by anything except an opening. Door is the only passable kind: it is
    /// the reason a building can have an inside worth entering.
    public static bool BlocksMove(EdgeKind e) => e == EdgeKind.Low || e == EdgeKind.High;

    /// Only a FULL-height wall stops sight. A low wall is the classic tactics trade — it protects
    /// you and it does not blind you — and a door is a hole.
    public static bool BlocksSight(EdgeKind e) => e == EdgeKind.High;

    /// The cover level this edge grants a defender standing against it, on the same 0/1/2 scale
    /// `Grid.CoverInfo.Level` already uses, so every downstream rule (flanking, the crit-vs-exposed
    /// bonus, high ground seeing over LOW cover, the HUD pip, `Ai`'s tile scoring) prices it with
    /// no second implementation to drift.
    public static int CoverLevel(EdgeKind e) => e == EdgeKind.High ? 2 : e == EdgeKind.Low ? 1 : 0;

    // ══════════════════ SIGHTLINE_EDGETEST ══════════════════
    /// The edge layer's whole contract, asserted on a bare grid rather than on a built mission,
    /// so a failure names a RULE and not a map.
    ///
    /// WHAT IT CANNOT SEE: it proves the three predicates agree with the table above; it cannot
    /// prove any MAP places a sensible wall, and it deliberately does not try — nothing builds an
    /// edge yet (see Mission), so the layer is campaign-inert by construction and this test is the
    /// only thing in the repository that exercises it at all.
    public static string SelfTest()
    {
        var fails = new System.Collections.Generic.List<string>();
        void Is(bool cond, string what) { if (!cond) fails.Add(what); }
        bool savedEnabled = Enabled;

        Grid Fresh()
        {
            var g = new Grid();
            for (int x = 0; x < g.W; x++) for (int y = 0; y < g.H; y++) g.Tiles[x, y] = TileType.Floor;
            g.ClearEdges();
            return g;
        }
        bool CanStep(Grid g, int x0, int y0, int x1, int y1)
        {
            var c = g.CostMap(x0, y0, null, out _, 4);      // 4 = one orthogonal step (2) with slack
            return c[x1, y1] >= 0 && c[x1, y1] <= 3;
        }
        bool Reaches(Grid g, int x0, int y0, int x1, int y1)
        { var c = g.CostMap(x0, y0, null, out _, 9999); return c[x1, y1] >= 0; }

        Enabled = true;

        // (A) A HIGH edge stops the step — and BOTH tiles stay floor. That separation is the
        //     entire reason the layer exists: a wall consumes no ground.
        {
            var g = Fresh(); g.SetEdgeV(5, 5, EdgeKind.High);
            Is(!CanStep(g, 4, 5, 5, 5), "A: high edge did not stop the step");
            Is(g.IsFloor(4, 5) && g.IsFloor(5, 5), "A: an edge wall consumed a tile");
            Is(Reaches(g, 4, 5, 5, 5), "A: a single wall segment SEVERED the board");
            Is(!g.HasLineOfSight(4, 5, 5, 5), "A: high edge did not stop sight");
            Is(!g.HasLineOfSight(5, 5, 4, 5), "A: high edge is not symmetric");
        }

        // (B) A LOW edge stops a mover, does NOT stop sight, and grants LOW cover.
        {
            var g = Fresh(); g.SetEdgeV(5, 5, EdgeKind.Low);
            Is(!CanStep(g, 4, 5, 5, 5), "B: low edge did not stop the step");
            Is(g.HasLineOfSight(4, 5, 5, 5), "B: low edge blocked sight");
            Is(g.GetCover(5, 5, 2, 5).Level == 1, "B: low edge did not give LOW cover");
        }

        // (C) A DOOR is a hole: passable, sight-permeable, shelters nobody.
        {
            var g = Fresh(); g.SetEdgeV(5, 5, EdgeKind.Door);
            Is(CanStep(g, 4, 5, 5, 5), "C: door blocked movement");
            Is(g.HasLineOfSight(4, 5, 5, 5), "C: door blocked sight");
            Is(g.GetCover(5, 5, 2, 5).Level == 0, "C: a door gave cover");
        }

        // (D) COVER IS DIRECTIONAL, and this is what makes a wall a wall rather than a buff.
        //     A wall on the NORTH edge shelters against fire from the north and not from the south.
        {
            var g = Fresh(); g.SetEdgeH(5, 5, EdgeKind.High);      // north face of (5,5)
            Is(g.GetCover(5, 5, 5, 1).Level == 2, "D: no cover from the sheltered side");
            Is(g.GetCover(5, 5, 5, 9).Level == 0, "D: wall sheltered the UNPROTECTED side");
            Is(g.GetCover(5, 5, 5, 9).Flanked, "D: unprotected side did not read as flanked");
        }

        // (E) A diagonal cannot cut a walled corner. Conservative on purpose: it matches the tile
        //     rule directly above it in CostMap (both facing tiles must be walkable) instead of
        //     inventing a laxer model for edges. Connectivity is never severed by it — the two
        //     orthogonal routes are judged on their own, and leg (A) proves the board stays whole.
        {
            var g = Fresh();
            g.SetEdgeV(5, 5, EdgeKind.High);       // west face of (5,5)
            g.SetEdgeH(4, 5, EdgeKind.High);       // north face of (4,5)
            Is(!CanStep(g, 4, 4, 5, 5), "E: diagonal cut a fully walled corner");
        }

        // (F) A cover TILE still reads exactly as before — the two sources MAX together, they do
        //     not fight. A regression here would mean the edge layer broke the old model.
        {
            var g = Fresh(); g.Tiles[6, 5] = TileType.HighCover;
            Is(g.GetCover(5, 5, 9, 5).Level == 2, "F: cover TILE regressed");
            var g2 = Fresh(); g2.Tiles[6, 5] = TileType.LowCover; g2.SetEdgeV(6, 5, EdgeKind.High);
            Is(g2.GetCover(5, 5, 9, 5).Level == 2, "F: edge+tile did not compose to the stronger");
        }

        // (G) THE RESTORE FLAG. Off, every query answers None and the board is the pre-P28 board
        //     exactly. It gates the QUERIES, not the arrays, so even a hand-stamped edge is inert.
        {
            var g = Fresh(); g.SetEdgeV(5, 5, EdgeKind.High); g.SetEdgeH(5, 5, EdgeKind.High);
            Enabled = false;
            Is(CanStep(g, 4, 5, 5, 5), "G: SIGHTLINE_EDGES=0 still blocked movement");
            Is(g.HasLineOfSight(4, 5, 5, 5), "G: SIGHTLINE_EDGES=0 still blocked sight");
            Is(g.GetCover(5, 5, 5, 1).Level == 0, "G: SIGHTLINE_EDGES=0 still gave cover");
            Enabled = true;
        }

        // (H) ClearEdges really clears — including the AnyEdges fast-out, which every predicate
        //     checks first. A stale flag would be worse than a stale wall: it costs on every query
        //     and hides nothing.
        {
            var g = Fresh(); g.SetEdgeV(5, 5, EdgeKind.High);
            Is(g.AnyEdges, "H: AnyEdges did not arm");
            g.ClearEdges();
            Is(!g.AnyEdges, "H: AnyEdges survived ClearEdges");
            Is(CanStep(g, 4, 5, 5, 5), "H: a cleared edge still blocked movement");
        }

        Enabled = savedEnabled;
        return fails.Count == 0
            ? "EDGETEST: PASS"
            : "EDGETEST: FAIL\n  " + string.Join("\n  ", fails);
    }
}
