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

    /// P49's CITADEL, kept VERBATIM as the red control for P52's authoring gate. The terminal and
    /// the captive both sit inside the walled room, behind its single door — the placement P49
    /// measured as an uncontested mission at every heat rung and P51 diagnosed as CARDINALITY
    /// rather than architecture. It is not reachable from any shipping path; leg (G) is its only
    /// caller, and its only job is to be REFUSED.
    public static readonly string[] CitadelP49 =
    {
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . X . . . ",
        "+ + + + + + +-+-+-+-+ + + + + + + + +",
        " . . . . . .|. . X T|. . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . .|. ^ ^ .|. . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . .+. ^ ^ .|. . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . .|. A . C|. . . . . . . . ",
        "+ + + + + + +-+-+-+-+ + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . X . . . . . . . . . . . . . . ",
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
        bool savedGlyphs = SiteGlyphs;
        bool savedRoomSite = RoomSite;

        try
        {
            Edges.Enabled = true;
            // P49 ships the site glyphs OFF (the round said so), so the legs below turn them ON
            // explicitly: a gate that only ever sees the shipped default cannot see the feature.
            SiteGlyphs = true;

            // ── (A) INERTNESS, ASSERTED. Every shipped template is single-resolution, so no board
            // changes and the CRN stream is untouched. The reference-identity check is the strong
            // form: the stamp path gets the SAME array it always got, not a copy that happens to
            // match. The day a template goes double-res this leg fails, and it should — that is
            // the commit that severs the stream and it must not slip in quietly.
            {
                // P48 REDREW EXACTLY ONE ARENA, and this leg is the ledger for that number. It was
                // written in P47 asserting ZERO, and it was meant to fail the day content arrived:
                // that commit is the one that severs the CRN stream. It now asserts ONE, by name,
                // so the NEXT one is just as loud.
                const int ExpectedEdged = 1;
                int withEdges = 0, notSame = 0;
                var edged = new List<int>();
                for (int i = 0; i < Layouts.Length; i++)
                {
                    // TryParse rather than ArenaAt: ArenaAt THROWS on a malformed template, which
                    // aborts this method with no verdict line at all. A gate that dies is a gate
                    // that says nothing, and this repository has been bitten by exactly that.
                    if (!TryParse(Source(i), out Arena a, out string tplWhy))
                    { fails.Add($"(A) arena {i} is malformed: {tplWhy}"); continue; }
                    if (a.HasEdges) { withEdges++; edged.Add(i); }
                    else if (!ReferenceEquals(a.Tiles, Layouts[i])) notSame++;
                }
                detail.Append($"{Layouts.Length} arenas, {withEdges} with edges {{{string.Join(",", edged)}}}; ");
                if (withEdges != ExpectedEdged)
                    fails.Add($"(A) {withEdges} arenas declare edges, expected {ExpectedEdged} — a board change this size wants a measured round and a note here");
                if (edged.Count > 0 && edged[0] != CitadelIndex)
                    fails.Add($"(A) the edged arena is index {edged[0]}, expected CITADEL at {CitadelIndex}");
                if (!AnyEdgeTemplates) fails.Add("(A) AnyEdgeTemplates is false with an edge template present");
                if (notSame != 0) fails.Add($"(A) {notSame} single-res templates were copied rather than passed through");

                // THE RESTORE FLAG IS AN EXACT RESTORE, not an approximate one: with it off,
                // ArenaAt hands back the very array it always handed back.
                EdgeArenas = false;
                var legacy = ArenaAt(CitadelIndex);
                EdgeArenas = true;
                if (legacy.HasEdges) fails.Add("(A) SIGHTLINE_EDGEARENAS=0 still returned an edged arena");
                if (!ReferenceEquals(legacy.Tiles, Layouts[CitadelIndex]))
                    fails.Add("(A) the restored CITADEL is not Layouts[CitadelIndex] itself");
                if (!Layouts[CitadelIndex].Contains(CitadelSignature))
                    fails.Add($"(A) Layouts[{CitadelIndex}] is not CITADEL any more — Layouts was reordered");

                // AND THE ROOM IS A ROOM. The fourteen tiles the old walls ate are floor now, and
                // the inside is reachable only through the door.
                var cit = ArenaAt(CitadelIndex);
                if (!cit.HasEdges) fails.Add("(A) the redrawn CITADEL parsed with no edges");
                else
                {
                    int cover = cit.Tiles.Sum(r => r.Count(c => c == '#' || c == 'o'));
                    if (cover != 0) fails.Add($"(A) the redrawn CITADEL still spends {cover} tiles on cover — the walls did not move to the boundaries");
                    var cg = new Grid();
                    for (int x = 0; x < cg.W; x++) for (int y = 0; y < cg.H; y++) cg.Tiles[x, y] = TileType.Floor;
                    cg.ClearEdges();
                    for (int y = 0; y < cg.H; y++) for (int x = 0; x <= cg.W; x++) cg.SetEdgeV(x, y, cit.EdgeV[x, y]);
                    for (int y = 0; y <= cg.H; y++) for (int x = 0; x < cg.W; x++) cg.SetEdgeH(x, y, cit.EdgeH[x, y]);
                    var cc = cg.CostMap(0, 0, (x, y) => false, out _, 9999);
                    if (cc[7, 4] < 0) fails.Add("(A) the CITADEL room's interior is unreachable — the door does not open");
                    cg.SetEdgeV(6, 5, EdgeKind.High);          // seal the one door
                    var cs = cg.CostMap(0, 0, (x, y) => false, out _, 9999);
                    int leak = 0;
                    for (int x = 6; x <= 9; x++) for (int y = 3; y <= 6; y++) if (cs[x, y] >= 0) leak++;
                    if (leak != 0) fails.Add($"(A) {leak} of 16 CITADEL interior tiles stay reachable with its door sealed — the walls leak");
                    // ── P49: THE SITES ARE INSIDE THE ROOM, AND THAT IS THE WHOLE CLAIM ────
                    // A terminal in a room is only a reason to go in if the room is the only way
                    // to it. Both sites must be interior tiles, and both must go dark when the
                    // one door is sealed — the same measurement as the room itself, aimed at the
                    // thing the mission converges on.
                    // P50 widened this from the OBJECTIVE glyphs to ALL of them: the arena now also
                    // declares an enemy-pod ANCHOR, and an anchor that is not inside the room is a
                    // garrison standing in the street.
                    var sites = new List<(char g, int x, int y)>();
                    for (int y = 0; y < TemplateH; y++)
                        for (int x = 0; x < TemplateW; x++)
                            if (cit.Tiles[y][x] is 'T' or 'C' or 'X' or 'E' or 'P' or 'A') sites.Add((cit.Tiles[y][x], x, y));
                    detail.Append($"CITADEL: {cover} cover tiles, sealed leak {leak}, glyphs {sites.Count}; ");
                    if (SiteGlyphs)
                    {
                        var kinds = string.Concat(sites.Select(z => z.g).OrderBy(c => c));
                        // P51: three SABOTAGE charges join the set — one INSIDE the room, two far
                        // outside. The point of the round is that a SINGLETON objective in a room is
                        // what P49 measured as a collapse; a three-site objective is the control.
                        if (kinds != "ACTXXX") fails.Add($"(A) the redrawn CITADEL declares '{kinds}', expected ACTXXX (anchor, captive, terminal, 3 charges)");
                        // ── P52: THE SINGLETONS CAME OUT OF THE ROOM ──────────────────────────
                        // P51 named the culprit and this is the repair. The room keeps the GARRISON
                        // and ONE charge — a defended place worth taking, which is what a room is
                        // for. The TERMINAL and the CAPTIVE are the two SINGLETON objectives, and a
                        // singleton behind one door is the exact shape P49 measured as a collapse,
                        // so both now stand outside it: `T` on the western approach to its door,
                        // `C` far east. The assertion is the design, tile by tile, because a glyph
                        // that drifts one column is a different mission and nothing else would say.
                        var want = new Dictionary<char, (int x, int y, bool inside)[]>
                        {
                            ['A'] = new[] { (7, 6, true) },
                            ['T'] = new[] { (4, 5, false) },
                            ['C'] = new[] { (14, 7, false) },
                            ['X'] = new[] { (8, 3, true), (14, 2, false), (3, 8, false) },
                        };
                        bool Inside(int sx, int sy) => sx >= 6 && sx <= 9 && sy >= 3 && sy <= 6;
                        foreach (var kv in want)
                        {
                            var got = sites.Where(z => z.g == kv.Key).Select(z => (z.x, z.y)).OrderBy(z => z.x).ThenBy(z => z.y).ToList();
                            var exp = kv.Value.Select(z => (z.x, z.y)).OrderBy(z => z.x).ThenBy(z => z.y).ToList();
                            if (!got.SequenceEqual(exp))
                                fails.Add($"(A) '{kv.Key}' sits at {string.Join("/", got.Select(z => $"{z.Item1},{z.Item2}"))}, expected {string.Join("/", exp.Select(z => $"{z.Item1},{z.Item2}"))}");
                        }
                        foreach (var kv in want)
                            foreach (var (wx, wy, wIn) in kv.Value)
                            {
                                if (!sites.Any(z => z.g == kv.Key && z.x == wx && z.y == wy)) continue;  // reported above
                                if (Inside(wx, wy) != wIn)
                                    fails.Add($"(A) site '{kv.Key}' at {wx},{wy} is {(wIn ? "OUTSIDE" : "INSIDE")} the room — the design says the other");
                                // and the room really is what makes the difference: an interior glyph
                                // goes dark with the one door sealed, an exterior one does not.
                                bool dark = cs[wx, wy] < 0;
                                if (dark != wIn)
                                    fails.Add($"(A) site '{kv.Key}' at {wx},{wy} is {(dark ? "cut off" : "still reachable")} with the door sealed — that contradicts its placement");
                            }
                        // the two outside charges must be FAR apart, or "split the squad" is a word
                        var outs = sites.Where(z => z.g == 'X' && !Inside(z.x, z.y)).ToList();
                        if (outs.Count == 2 && Util.ChebyDist(outs[0].x, outs[0].y, outs[1].x, outs[1].y) < 8)
                            fails.Add($"(A) the two outside charges are only {Util.ChebyDist(outs[0].x, outs[0].y, outs[1].x, outs[1].y)} tiles apart");
                        if (!AnySiteTemplates) fails.Add("(A) AnySiteTemplates is false with glyphs declared");
                    }

                    // ── P49: THE GLYPH STRIP IS EXACT. `CitadelEdgedNoSites` is DERIVED rather
                    // than hand-written precisely so it cannot drift — and this is what proves the
                    // derivation touches the site cells and nothing else, so `SIGHTLINE_SITEGLYPHS=0`
                    // is the pre-P49 room and not a subtly different board.
                    {
                        var a = CitadelEdged; var b = CitadelEdgedNoSites;
                        if (a.Length != b.Length) fails.Add("(A) the stripped CITADEL has a different row count");
                        else
                        {
                            int diffs = 0, wrong = 0;
                            for (int i = 0; i < a.Length; i++)
                                for (int c = 0; c < a[i].Length; c++)
                                    if (a[i][c] != b[i][c])
                                    {
                                        diffs++;
                                        if (!(a[i][c] is 'T' or 'X' or 'E' or 'C' or 'P' or 'A') || b[i][c] != '.') wrong++;
                                    }
                            if (diffs != sites.Count) fails.Add($"(A) the strip changed {diffs} cells for {sites.Count} sites");
                            if (wrong != 0) fails.Add($"(A) the strip changed {wrong} cells that are not a site glyph -> floor");
                        }
                        // and with the dial off, the arena really has no sites and the gate follows
                        SiteGlyphs = false;
                        bool anyOff = AnySiteTemplates;
                        var stripped = ArenaAt(CitadelIndex);
                        SiteGlyphs = true;
                        int leftOver = stripped.Tiles.Sum(r => r.Count(c => c is 'T' or 'C' or 'X' or 'E' or 'P' or 'A'));
                        if (leftOver != 0) fails.Add($"(A) SIGHTLINE_SITEGLYPHS=0 left {leftOver} glyphs in the arena");
                        if (anyOff) fails.Add("(A) AnySiteTemplates stayed true with the glyphs stripped — PlanBoard would still spend the gate roll");
                    }
                }
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

            // ── (F) THE GARRISON ACTUALLY GARRISONS ──────────────────────────────────────────
            // P26 shipped the 'A' glyph, its parser, its cardinality rule AND `SIGHTLINE_ARENAANCHORS`
            // — and nothing that READ `plan.Anchors`. The dial had no consumer for three programs.
            // This leg is what makes it real: build the arena for a real mission and assert a
            // hostile stands on the anchor tile, and that the restore flag puts it back outside.
            {
                bool anchWas = Mission.ArenaAnchors, glyphWas = SiteGlyphs;
                int layoutWas = Mission.ForcedLayout;
                var anchorTile = (x: -1, y: -1);
                var citA = ArenaAt(CitadelIndex);
                for (int y = 0; y < TemplateH; y++)
                    for (int x = 0; x < TemplateW; x++)
                        if (citA.Tiles[y][x] == 'A') anchorTile = (x, y);
                if (anchorTile.x < 0) fails.Add("(F) the redrawn CITADEL declares no enemy anchor");
                else
                {
                    int Inside(bool anchorsOn)
                    {
                        Mission.ArenaAnchors = anchorsOn; SiteGlyphs = true;
                        Mission.ForcedLayout = CitadelIndex;
                        int inRoom = 0;
                        for (int seed = 0; seed < 8; seed++)
                        {
                            Util.Reseed(4400 + seed);
                            var g2 = new Game { NoPersist = true, ForcedObjective = Objective.Hack };
                            g2.StartMission(1);
                            foreach (var e in g2.Enemies)
                                if (e.Alive && e.X >= 6 && e.X <= 9 && e.Y >= 3 && e.Y <= 6) inRoom++;
                        }
                        return inRoom;
                    }
                    int withAnchors = Inside(true);
                    int without = Inside(false);
                    Mission.ArenaAnchors = anchWas; SiteGlyphs = glyphWas; Mission.ForcedLayout = layoutWas;
                    detail.Append($"garrison: {withAnchors} hostiles in the room over 8 builds, {without} with the anchors off; ");
                    if (withAnchors < 8)
                        fails.Add($"(F) only {withAnchors} hostiles stood inside the room over 8 builds — the anchor is not seating a pod");
                    if (without >= withAnchors)
                        fails.Add($"(F) SIGHTLINE_ARENAANCHORS=0 put {without} hostiles in the room against {withAnchors} — the flag is not the lever");
                }
            }

            // ── (G) P52: THE AUTHORING GATE, AND IT IS SHOWN TO BITE ─────────────────────────
            // `Maps.SitesDoorLocked` is the rule P49's round cost a measured round to discover.
            // This leg does the two things a gate in this repository has to do: run over the shipped
            // content, and FAIL against the shape it exists to refuse. The second half is the one
            // that matters — PARALLAX's thesis is that the checks here fail QUIET, and a gate whose
            // red has never been seen is a comment.
            {
                int locked = 0;
                for (int i = 0; i < Layouts.Length; i++)
                {
                    if (!TryParse(Source(i), out Arena ar, out _)) continue;
                    if (SitesDoorLocked(ar, out string lockWhy))
                    { locked++; fails.Add($"(G) shipped arena {i} is door-locked: {lockWhy}"); }
                }
                // THE RED CONTROL. `CitadelP49` is the placement P49 shipped and P51 diagnosed —
                // the terminal and the captive both inside the room, behind the one door. The gate
                // must refuse it, and it must name the door.
                if (!TryParse(CitadelP49, out Arena bad, out string badWhy))
                    fails.Add($"(G) the P49 control did not parse: {badWhy}");
                else if (!SitesDoorLocked(bad, out string ctlWhy))
                    fails.Add("(G) the gate ACCEPTED P49's placement — a singleton objective sealed behind one door is exactly what it exists to refuse");
                else detail.Append($"gate: {locked} shipped arenas door-locked, P49 control refused ({ctlWhy}); ");

                // AND IT DOES NOT DEPEND ON THE SESSION'S DIAL. With `Edges.Enabled` false every
                // edge query answers None, so a gate that honoured the dial would flood an open
                // board and clear every template silently. It forces the layer on; this asserts it.
                {
                    bool was = Edges.Enabled;
                    Edges.Enabled = false;
                    bool stillRed = TryParse(CitadelP49, out Arena b2, out _) && SitesDoorLocked(b2, out _);
                    Edges.Enabled = was;
                    if (!stillRed) fails.Add("(G) the gate went QUIET with SIGHTLINE_EDGES=0 — it is honouring a runtime dial instead of reading the drawn template");
                    if (!Edges.Enabled) fails.Add("(G) the gate did not restore Edges.Enabled");
                }

                // and the rule is CARDINALITY, not the room: the same board with the terminal moved
                // out but the three charges left as they are must PASS, and a board whose ONLY
                // charge is the interior one must not. Built by editing the shipped template, so
                // the two differ in one glyph and nothing else.
                var citRows = (string[])CitadelEdged.Clone();
                if (TryParse(citRows, out Arena shipped, out _) && SitesDoorLocked(shipped, out string sw))
                    fails.Add($"(G) the SHIPPED CITADEL is door-locked: {sw}");
                // strip the two OUTSIDE charges, leaving the interior one alone in its category
                var soloRows = citRows.Select(r => r).ToArray();
                soloRows[5]  = soloRows[5].Replace('X', '.');
                soloRows[17] = soloRows[17].Replace('X', '.');
                if (!TryParse(soloRows, out Arena solo, out string sowhy))
                    fails.Add($"(G) the solo-charge control did not parse: {sowhy}");
                else if (!SitesDoorLocked(solo, out _))
                    fails.Add("(G) the gate ACCEPTED a sabotage objective whose only charge is behind the door — it is counting rooms, not sites");
            }

            // ── (H) P53: THE FALSIFICATION ARM IS EXACTLY ONE MOVED CHARGE ───────────────────
            // `CitadelEdgedNoRoomSite` is DERIVED so it cannot drift from the shipped template, and
            // this is what proves the derivation moves ONE glyph and nothing else. An arm that
            // quietly changed a second thing would price two levers at once, which is the mistake
            // L7 made and P23 had to unpick.
            {
                var a = CitadelEdged; var b = CitadelEdgedNoRoomSite;
                if (a.Length != b.Length) fails.Add("(H) the no-room-site CITADEL has a different row count");
                else
                {
                    var moved = new List<(int r, int c, char from, char to)>();
                    for (int i = 0; i < a.Length; i++)
                        for (int c = 0; c < a[i].Length; c++)
                            if (a[i][c] != b[i][c]) moved.Add((i, c, a[i][c], b[i][c]));
                    if (moved.Count != 2)
                        fails.Add($"(H) the arm changes {moved.Count} cells, expected exactly 2 (the charge leaves, the charge lands)");
                    int rOut = RoomChargeAt.y * 2 + 1, cOut = RoomChargeAt.x * 2 + 1;
                    int rIn  = RoomChargeOut.y * 2 + 1, cIn = RoomChargeOut.x * 2 + 1;
                    if (!moved.Any(m => m.r == rOut && m.c == cOut && m.from == 'X' && m.to == '.'))
                        fails.Add($"(H) the interior charge at {RoomChargeAt.x},{RoomChargeAt.y} did not leave");
                    if (!moved.Any(m => m.r == rIn && m.c == cIn && m.from == '.' && m.to == 'X'))
                        fails.Add($"(H) no charge landed at {RoomChargeOut.x},{RoomChargeOut.y}");
                }

                // THE CHARGE COUNT IS THE WHOLE POINT: three either way, or this prices
                // "how many sites" instead of "where the required one is".
                RoomSite = false;
                var armA = ArenaAt(CitadelIndex);
                RoomSite = true;
                var shipA = ArenaAt(CitadelIndex);
                int nArm = armA.Tiles.Sum(r => r.Count(c => c == 'X'));
                int nShip = shipA.Tiles.Sum(r => r.Count(c => c == 'X'));
                if (nArm != 3 || nShip != 3)
                    fails.Add($"(H) charge counts are arm {nArm} / shipped {nShip}, both must be 3");
                if (!Mission.ReadSitesWellFormed(armA.Tiles, out string awhy))
                    fails.Add($"(H) the arm's template is not well-formed: {awhy}");

                // AND THE ARM REALLY HAS NOTHING REQUIRED IN THE ROOM — which is the hypothesis.
                bool Inside(int x, int y) => x >= 6 && x <= 9 && y >= 3 && y <= 6;
                int armInside = 0, shipInside = 0;
                for (int y = 0; y < TemplateH; y++)
                    for (int x = 0; x < TemplateW; x++)
                    {
                        if (armA.Tiles[y][x] == 'X' && Inside(x, y)) armInside++;
                        if (shipA.Tiles[y][x] == 'X' && Inside(x, y)) shipInside++;
                    }
                if (armInside != 0) fails.Add($"(H) SIGHTLINE_ROOMSITE=0 still leaves {armInside} charges in the room");
                if (shipInside != 1) fails.Add($"(H) the shipped board has {shipInside} charges in the room, expected 1");

                // THE CACHES FOLLOW THE DIAL. A cache keyed on two of three dials makes the arm
                // silently not take — P48 lost 39 chunks to that exact shape, so it is asserted
                // rather than inspected.
                if (ReferenceEquals(armA.Tiles, shipA.Tiles))
                    fails.Add("(H) ArenaAt handed back the same rows for both arms — its cache does not follow RoomSite");
                detail.Append($"arm: 3 charges either way, room holds {shipInside} shipped / {armInside} in arm; ");

                // and the authoring gate still passes on the arm (no category sealed behind a door)
                if (SitesDoorLocked(armA, out string alk)) fails.Add($"(H) the falsification arm is door-locked: {alk}");
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
        finally { Edges.Enabled = savedEdges; SiteGlyphs = savedGlyphs; RoomSite = savedRoomSite; }

        return fails.Count == 0
            ? "ARENAEDGETEST: PASS (exactly one arena is double-resolution (CITADEL) and every other is passed "
              + "through by reference; SIGHTLINE_EDGEARENAS=0 hands back the original array itself; the redrawn "
              + "CITADEL spends no tile on cover, its interior is sealed without its door, its GARRISON and one "
              + "sabotage charge are inside that room and sealed with it while its terminal, its captive and its "
              + "other two charges stand outside and stay reachable; no shipped arena is door-locked and the gate "
              + "refuses P49's placement and a lone interior charge; the glyph strip touches exactly the "
              + "site cells and takes AnySiteTemplates down with it; the hand-drawn "
              + "fixture parses to exactly 18 walls and 1 "
              + "door with nothing stray; SIGHTLINE_ROOMSITE=0 moves exactly one charge out of the room and "
              + "leaves three on the board, and both caches follow that dial; "
              + "door with nothing stray; five malformed forms are each refused with a reason; on a real board the "
              + "interior is reachable through the door and unreachable without it, and sight stops at a wall and "
              + "passes through the door; the |/- aliases are aliases) [" + detail + "]"
            : "ARENAEDGETEST: FAIL (" + string.Join(" | ", fails.Distinct()) + ") [" + detail + "]";
    }
}
