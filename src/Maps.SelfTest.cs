using System;
using System.Collections.Generic;
using System.Linq;

namespace Sightline;

/// SIGHTLINE_ARENAEDGETEST — P47. The double-resolution template format.
///
/// The fixture below is written as ASCII ART ON PURPOSE rather than generated. The format's whole
/// claim is that a person can draw a room and get that room, so the test has to read the way an
/// authored map reads — and a generated fixture would agree with a generator's bug instead of
/// disagreeing with it.
public static partial class Maps
{
    /// A 5x4 room at tiles (3,3)..(7,6) with ONE DOOR in its north wall at x=5. Every tile inside
    /// and out is plain floor, so the room is made entirely of BOUNDARIES — which is the thing a
    /// single-resolution template cannot say at all.
    public static readonly string[] EdgeFixture =
    {
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + +-+-+++-+-+ + + + + + + + + + +",
        " . . .|. . . . .|. . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . .|. . . . .|. . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . .|. . . . .|. . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . .|. . . . .|. . . . . . . . . . ",
        "+ + + +-+-+-+-+-+ + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
    };

    public static string EdgeSelfTest()
    {
        var fails = new List<string>();
        var detail = new System.Text.StringBuilder();
        bool savedEdges = Edges.Enabled;

        try
        {
            Edges.Enabled = true;

            // ── (A) INERTNESS, ASSERTED. Every shipped template is single-resolution, so no board
            // changes and the CRN stream is untouched. The reference-identity check is the strong
            // form: the stamp path gets the SAME array it always got, not a copy that happens to
            // match. The day a template goes double-res this leg fails, and it should — that is
            // the commit that severs the stream and it must not slip in quietly.
            {
                int withEdges = 0, notSame = 0;
                for (int i = 0; i < Layouts.Length; i++)
                {
                    // TryParse rather than ArenaAt: ArenaAt THROWS on a malformed template, which
                    // aborts this method with no verdict line at all. A gate that dies is a gate
                    // that says nothing, and this repository has been bitten by exactly that.
                    if (!TryParse(Layouts[i], out Arena a, out string tplWhy))
                    { fails.Add($"(A) Layouts[{i}] is malformed: {tplWhy}"); continue; }
                    if (a.HasEdges) withEdges++;
                    if (!ReferenceEquals(a.Tiles, Layouts[i])) notSame++;
                }
                detail.Append($"{Layouts.Length} shipped templates, {withEdges} with edges; ");
                if (withEdges != 0)
                    fails.Add($"(A) {withEdges} shipped templates declare edges — this wave is no longer inert and wants a measured round");
                if (AnyEdgeTemplates) fails.Add("(A) AnyEdgeTemplates is true with no edge template");
                if (notSame != 0) fails.Add($"(A) {notSame} single-res templates were copied rather than passed through");
            }

            // ── (B) THE FIXTURE PARSES TO THE ROOM IT DRAWS ───────────────────────────────────
            if (!TryParse(EdgeFixture, out Arena room, out string why))
                fails.Add($"(B) the fixture did not parse: {why}");
            else
            {
                if (!room.HasEdges) fails.Add("(B) the fixture parsed with no edges");
                else
                {
                    // the tile layer is untouched floor — the room is made of boundaries alone
                    int nonFloor = room.Tiles.Sum(r => r.Count(c => c != '.'));
                    if (nonFloor != 0) fails.Add($"(B) {nonFloor} non-floor tiles — the fixture should be boundaries only");

                    int wrong = 0;
                    void WantV(int x, int y, EdgeKind k)
                    { if (room.EdgeV[x, y] != k) { wrong++; fails.Add($"(B) EdgeV[{x},{y}] is {room.EdgeV[x, y]}, expected {k}"); } }
                    void WantH(int x, int y, EdgeKind k)
                    { if (room.EdgeH[x, y] != k) { wrong++; fails.Add($"(B) EdgeH[{x},{y}] is {room.EdgeH[x, y]}, expected {k}"); } }

                    for (int y = 3; y <= 6; y++) { WantV(3, y, EdgeKind.High); WantV(8, y, EdgeKind.High); }
                    for (int x = 3; x <= 7; x++)
                    {
                        WantH(x, 3, x == 5 ? EdgeKind.Door : EdgeKind.High);   // the door
                        WantH(x, 7, EdgeKind.High);
                    }
                    // and nothing anywhere else
                    int stray = 0;
                    for (int y = 0; y < TemplateH; y++)
                        for (int x = 0; x <= TemplateW; x++)
                            if (room.EdgeV[x, y] != EdgeKind.None && !(x is 3 or 8 && y >= 3 && y <= 6)) stray++;
                    for (int y = 0; y <= TemplateH; y++)
                        for (int x = 0; x < TemplateW; x++)
                            if (room.EdgeH[x, y] != EdgeKind.None && !(x >= 3 && x <= 7 && y is 3 or 7)) stray++;
                    if (stray != 0) fails.Add($"(B) {stray} edges outside the room — the grid is misaligned by a row or a column");
                    if (wrong == 0 && stray == 0) detail.Append("fixture: 18 walls + 1 door, aligned; ");
                }
            }

            // ── (C) A MALFORMED TEMPLATE IS A LOUD FAILURE, NOT A MISSING WALL ────────────────
            {
                string[] Mut(Action<char[][]> f)
                {
                    var rows = EdgeFixture.Select(r => r.ToCharArray()).ToArray();
                    f(rows);
                    return rows.Select(r => new string(r)).ToArray();
                }
                var bad = new (string tag, string[] src)[]
                {
                    ("rowCount",   EdgeFixture.Take(EdgeFixture.Length - 1).ToArray()),
                    ("rowWidth",   Mut(r => r[7] = (new string(r[7]) + "x").ToCharArray())),
                    ("junkGlyph",  Mut(r => r[7][6] = 'Z')),
                    ("dashInVert", Mut(r => r[7][6] = '-')),      // horizontal alias in a vertical slot
                    ("barInHoriz", Mut(r => r[6][7] = '|')),      // vertical alias in a horizontal slot
                };
                foreach (var (tag, src) in bad)
                    if (TryParse(src, out _, out string w))
                        fails.Add($"({tag}) a malformed template parsed clean");
                    else if (string.IsNullOrWhiteSpace(w))
                        fails.Add($"({tag}) rejected with no reason given");
            }

            // ── (D) THE ROOM WORKS ON A REAL BOARD ───────────────────────────────────────────
            // This is the leg the format exists for: a building whose inside is reachable ONLY
            // through its door, and which you cannot see into through its walls.
            if (TryParse(EdgeFixture, out Arena r2, out _))
            {
                var g = new Grid();
                for (int x = 0; x < g.W; x++) for (int y = 0; y < g.H; y++) g.Tiles[x, y] = TileType.Floor;
                g.ClearEdges();
                for (int y = 0; y < g.H; y++) for (int x = 0; x <= g.W; x++) g.SetEdgeV(x, y, r2.EdgeV[x, y]);
                for (int y = 0; y <= g.H; y++) for (int x = 0; x < g.W; x++) g.SetEdgeH(x, y, r2.EdgeH[x, y]);
                if (!g.AnyEdges) fails.Add("(D) AnyEdges stayed false after stamping 19 boundaries");

                // from a corner OUTSIDE the room, the inside is reachable — through the door
                var cost = g.CostMap(0, 0, (x, y) => false, out _, 9999);
                bool inside = cost[5, 4] >= 0;
                if (!inside) fails.Add("(D) the room's interior is unreachable — the door does not open");
                // and the ONLY way in is that door: seal it and the interior must go dark
                g.SetEdgeH(5, 3, EdgeKind.High);
                var sealedCost = g.CostMap(0, 0, (x, y) => false, out _, 9999);
                int reachableInside = 0;
                for (int x = 3; x <= 7; x++) for (int y = 3; y <= 6; y++) if (sealedCost[x, y] >= 0) reachableInside++;
                if (reachableInside != 0)
                    fails.Add($"(D) {reachableInside} of 20 interior tiles are still reachable with the door sealed — the walls leak");
                g.SetEdgeH(5, 3, EdgeKind.Door);

                // sight: a HIGH wall stops it, a DOOR does not
                bool throughWall = g.HasLineOfSight(1, 4, 4, 4);     // crosses EdgeV[3,4], a wall
                bool throughDoor = g.HasLineOfSight(5, 2, 5, 4);     // crosses EdgeH[5,3], the door
                if (throughWall) fails.Add("(D) line of sight passed straight through a high wall");
                if (!throughDoor) fails.Add("(D) line of sight did not pass through the door");
                detail.Append($"board: interior reachable {inside}, sealed leak {reachableInside}, ");
                detail.Append($"sightThroughWall {throughWall}, sightThroughDoor {throughDoor}");
            }

            // ── (E) THE ALIASES ARE ALIASES ──────────────────────────────────────────────────
            // `|` and `-` exist so an authored map looks like the room it describes. If they meant
            // anything of their own, the format would have two vocabularies for one state.
            {
                var canon = EdgeFixture.Select(r => r.Replace('|', '#').Replace('-', '#')).ToArray();
                if (!TryParse(canon, out Arena a1, out string w1)) fails.Add($"(E) the canonical form did not parse: {w1}");
                else if (TryParse(EdgeFixture, out Arena a2, out _))
                {
                    int diff = 0;
                    for (int y = 0; y < TemplateH; y++) for (int x = 0; x <= TemplateW; x++) if (a1.EdgeV[x, y] != a2.EdgeV[x, y]) diff++;
                    for (int y = 0; y <= TemplateH; y++) for (int x = 0; x < TemplateW; x++) if (a1.EdgeH[x, y] != a2.EdgeH[x, y]) diff++;
                    if (diff != 0) fails.Add($"(E) the alias form and the '#' form differ on {diff} edges");
                }
            }
        }
        finally { Edges.Enabled = savedEdges; }

        return fails.Count == 0
            ? "ARENAEDGETEST: PASS (every shipped template is single-resolution and passed through by reference, so "
              + "the board and the CRN stream are untouched; the hand-drawn fixture parses to exactly 18 walls and 1 "
              + "door with nothing stray; five malformed forms are each refused with a reason; on a real board the "
              + "interior is reachable through the door and unreachable without it, and sight stops at a wall and "
              + "passes through the door; the |/- aliases are aliases) [" + detail + "]"
            : "ARENAEDGETEST: FAIL (" + string.Join(" | ", fails.Distinct()) + ") [" + detail + "]";
    }
}
