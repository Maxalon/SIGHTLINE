using System;

namespace Sightline;

/// Hand-authored arena layouts, applied over the procedural generator for map
/// character. Legend (one char per tile):
///   '.' floor   'o' low cover   '#' high cover
///   '^' tier-1 plateau   '=' tier-2 plateau   (both walkable high ground)
///   'B' explosive barrel (impassable like cover, but blows up when shot — a hazard;
///       the tile stays floor underneath, so a destroyed barrel leaves open ground)
/// Each layout is GridH (11) rows of GridW (18) chars.
///
/// P26 "THE ARENA OWNS THE FIGHT" — SITE GLYPHS. A template may declare where the
/// objective actually happens. Each of these is OPEN FLOOR for terrain purposes (the
/// stamp writes nothing for it, exactly like '.'); it only tells Mission.PlanBoard
/// where a site goes:
///   'T' HACK terminal        — exactly 0 or 1 per template
///   'X' SABOTAGE site        — exactly 0 or 3
///   'E' EVAC / extraction    — 0, or >= 8 mutually reachable tiles
///   'C' RESCUE captive       — exactly 0 or 1
///   'P' player spawn         — 0, or >= 7; the LAST 'P' in row-major scan order is the
///                              VIP/captive seat (carries PlayerSpawns[Length-1] over)
///   'A' enemy pod anchor     — 0..6 (EnemyPodColOffset.Length)
/// A template that declares NO site glyphs behaves exactly as it did before P26.
///
/// WHY THIS EXISTS. Before P26 every objective sat on a literal tile — the HACK terminal
/// was ALWAYS (10,5) — and Mission.Build force-cleared a 3x3 ring around it that
/// TryApplyLayout then SKIPPED, so the authored terrain was erased at the exact point the
/// fight converges on. CITADEL's bunker and PLAZA's plateau could not shape the fight, by
/// construction. Measured over 6,400 campaigns: corr(open-floor %, win %) = -0.15 across
/// all 35 boards. The ring is a property of a LITERAL site, not of a site: a template that
/// places its own 'T' has authored the terrain around it, so no ring is punched.
///
/// Reserved tiles — player and enemy spawns, the evac zone, and a LITERAL terminal or
/// sabotage site + its ring — are still left as open floor regardless of the template, and
/// `Mission` verifies connectivity before committing to a layout (falling back to the
/// procedural generator otherwise).
public static partial class Maps
{
    /// P26: does ANY shipped template declare a site glyph? Computed once, purely, at type init.
    ///
    /// THIS IS THE INERTNESS GATE, and it is load-bearing. Mission.PlanBoard must not spend the
    /// arena gate's Util.Roll(80) unless it is actually going to take over the gate, because Build
    /// still rolls on the pre-P26 path and two rolls would double-spend the shared stream. While
    /// every template is glyph-free this is false, PlanBoard consumes NOTHING, and the wave is
    /// byte-identical by construction. The first template to declare a site flips it — and THAT is
    /// the commit that moves the CRN stream, not the engine change.
    /// Lazy, NOT a field initializer: `Layouts` is declared BELOW this point and C# runs static
    /// field initializers in declaration order, so an eager `= ComputeAnySiteTemplates()` would
    /// read a null Layouts and throw at type init.
    static int _anySite = -1;
    static bool _anySiteFor, _anySiteGlyphs;
    public static bool AnySiteTemplates
    {
        get
        {
            // P49: the cache must follow BOTH dials. It was a one-shot latch, which is correct while
            // nothing can change the answer — and the moment a template declares a glyph behind a
            // switch, a latched `true` makes `SIGHTLINE_SITEGLYPHS=0` a HALF restore: PlanBoard
            // would keep spending the arena gate's roll on a tree with no sites in it.
            if (_anySite < 0 || _anySiteFor != EdgeArenas || _anySiteGlyphs != SiteGlyphs)
            {
                _anySiteFor = EdgeArenas; _anySiteGlyphs = SiteGlyphs;
                _anySite = ComputeAnySiteTemplates() ? 1 : 0;
            }
            return _anySite == 1;
        }
    }

    static bool ComputeAnySiteTemplates()
    {
        // P48: the EFFECTIVE arenas, not the raw `Layouts` — a redrawn arena is what a mission
        // actually stamps, so it is what this gate has to read. (Today no redrawn arena declares a
        // glyph, which is the point of doing edges and sites in separate waves.)
        for (int i = 0; i < Layouts.Length; i++)
            foreach (var row in Source(i))
                foreach (char c in row)
                    if (c == 'T' || c == 'X' || c == 'E' || c == 'C' || c == 'P' || c == 'A') return true;
        return false;
    }

    /// P29 — THE SIZE THESE ARENAS WERE DRAWN FOR, which is not necessarily the size of the
    /// board being played. Every template below is TemplateH rows of TemplateW characters, and
    /// that is a property of the FILE, fixed forever; `Cfg.GridW/H` is a property of the RUN and
    /// can now change. Conflating the two is what let a board-size change orphan all 35 arenas in
    /// silence. SIGHTLINE_TEMPLATEGATE checks the templates against THESE and then, separately,
    /// reports whether the live board can use them at all.
    public const int TemplateW = 18, TemplateH = 11;

    // ══ P47 — THE DOUBLE-RESOLUTION TEMPLATE: WALLS ON THE BOUNDARIES ═══════════════════════
    /// P28 gave the board an EDGE layer — a wall that lives on the boundary between two tiles,
    /// consumes no floor, and can have a DOOR in it. Thirty-five hand-authored arenas could not
    /// express one, because a template is one character per TILE and a boundary has no character.
    /// So the only building anybody could author was a ring of `#` cover tiles, which is a solid
    /// block with no inside — and P42 then measured exactly what that buys: procedural rectangles
    /// of wall move win rate not at all and cost `meaningfulChoicesPerTurn` at every rung, because
    /// **a rectangle gives you somewhere to sit and sitting is not a decision.** An AUTHORED
    /// building — an objective inside it, a door worth breaching, a roof worth holding — is a
    /// different object, and P42 named it as the unpriced one. This is the notation for it.
    ///
    /// A template may be given in either form and `Parse` tells them apart by its ROW COUNT:
    ///
    ///   SINGLE (TemplateH rows x TemplateW chars) — every template shipped before P47. Edges all
    ///   `None`; byte-for-byte the board it always built.
    ///
    ///   DOUBLE (2*TemplateH+1 rows x 2*TemplateW+1 chars) — odd row/column indices are TILES and
    ///   even ones are the boundaries between them:
    ///       row 2y+1, col 2x+1  ->  tile (x, y), the same legend as above
    ///       row 2y+1, col 2x    ->  EdgeV[x, y]   (the WEST side of tile (x,y))
    ///       row 2y,   col 2x+1  ->  EdgeH[x, y]   (the NORTH side of tile (x,y))
    ///       row 2y,   col 2x    ->  a corner. Carries no state and is IGNORED; write `+` or a
    ///                               space, whichever draws better.
    ///
    /// EDGE LEGEND. The canonical glyphs are the TILE legend's own, because the position already
    /// says whether a character is a tile or a boundary and a second vocabulary is a second thing
    /// to get wrong:
    ///   `#` HIGH wall (stops movement and sight, HIGH cover)
    ///   `o` LOW wall  (stops movement, sight and fire pass over, LOW cover)
    ///   `+` DOOR      (passable, sight passes, shelters nobody — the reason a building has an
    ///                  inside worth entering)
    ///   ` ` or `.`    open boundary
    /// Plus two ALIASES that exist purely so an authored map looks like the room it describes:
    /// `|` in a vertical slot and `-` in a horizontal slot both mean HIGH. They are aliases, not a
    /// second meaning — `Parse` folds them immediately.
    ///
    /// INERT UNTIL A TEMPLATE USES IT, by construction and not by promise: `Layouts` below is
    /// entirely single-resolution today, so every `Arena` has `HasEdges == false`, `Mission` stamps
    /// nothing and `Grid.AnyEdges` stays false — which is the fast-out every edge predicate in
    /// `Grid` short-circuits on. **The commit that adds the first double-resolution template is the
    /// one that severs the CRN stream**, exactly as P26 recorded for its site glyphs; this one does
    /// not, and `ARENAEDGETEST` leg (A) is the standing proof.
    public readonly struct Arena
    {
        public readonly string[] Tiles;        // TemplateH rows of TemplateW chars
        public readonly EdgeKind[,] EdgeV;     // [TemplateW+1, TemplateH] or null
        public readonly EdgeKind[,] EdgeH;     // [TemplateW, TemplateH+1] or null
        public bool HasEdges => EdgeV != null;

        public Arena(string[] tiles, EdgeKind[,] v, EdgeKind[,] h) { Tiles = tiles; EdgeV = v; EdgeH = h; }
    }

    /// One edge glyph. Returns false for anything not in the legend, so a typo in an authored map
    /// is a loud gate failure rather than a silently missing wall.
    public static bool EdgeGlyph(char c, bool vertical, out EdgeKind k)
    {
        switch (c)
        {
            case ' ': case '.': k = EdgeKind.None; return true;
            case '#': k = EdgeKind.High; return true;
            case 'o': k = EdgeKind.Low;  return true;
            case '+': k = EdgeKind.Door; return true;
            case '|': k = EdgeKind.High; return vertical;      // alias, vertical slots only
            case '-': k = EdgeKind.High; return !vertical;     // alias, horizontal slots only
            default:  k = EdgeKind.None; return false;
        }
    }

    /// Parse either form. `why` names the first structural fault and the result is unusable when
    /// it returns false — `SIGHTLINE_TEMPLATEGATE` runs this over every shipped template, so a
    /// malformed one cannot reach a mission.
    public static bool TryParse(string[] src, out Arena arena, out string why)
    {
        arena = default; why = null;
        if (src == null || src.Length == 0) { why = "empty template"; return false; }

        if (src.Length == TemplateH)
        {
            foreach (var r in src)
                if (r.Length != TemplateW) { why = $"single-res row is {r.Length} chars, expected {TemplateW}"; return false; }
            arena = new Arena(src, null, null);
            return true;
        }

        int dh = TemplateH * 2 + 1, dw = TemplateW * 2 + 1;
        if (src.Length != dh)
        { why = $"{src.Length} rows — expected {TemplateH} (single) or {dh} (double)"; return false; }
        for (int i = 0; i < src.Length; i++)
            if (src[i].Length != dw) { why = $"double-res row {i} is {src[i].Length} chars, expected {dw}"; return false; }

        var tiles = new string[TemplateH];
        var ev = new EdgeKind[TemplateW + 1, TemplateH];
        var eh = new EdgeKind[TemplateW, TemplateH + 1];
        var row = new char[TemplateW];
        for (int y = 0; y < TemplateH; y++)
        {
            for (int x = 0; x < TemplateW; x++) row[x] = src[2 * y + 1][2 * x + 1];
            tiles[y] = new string(row);
            for (int x = 0; x <= TemplateW; x++)
            {
                char c = src[2 * y + 1][2 * x];
                if (!EdgeGlyph(c, true, out ev[x, y]))
                { why = $"row {2 * y + 1} col {2 * x}: '{c}' is not a vertical edge glyph"; return false; }
            }
        }
        for (int y = 0; y <= TemplateH; y++)
            for (int x = 0; x < TemplateW; x++)
            {
                char c = src[2 * y][2 * x + 1];
                if (!EdgeGlyph(c, false, out eh[x, y]))
                { why = $"row {2 * y} col {2 * x + 1}: '{c}' is not a horizontal edge glyph"; return false; }
            }
        arena = new Arena(tiles, ev, eh);
        return true;
    }

    // ══ P48 — THE FIRST ROOM ═══════════════════════════════════════════════════════════════
    /// CITADEL, REDRAWN. The original is the exact object P42 measured and P47 named: a "fortified
    /// high-cover bunker with a plateau and a doorway" built out of FOURTEEN `#` TILES, which is a
    /// solid block with no inside. You could stand behind it. You could not go in.
    ///
    /// Redrawn on the edge layer it is a ROOM: the same 4x4 footprint, but its walls live on the
    /// boundaries and consume no floor, so the fourteen tiles they used to eat are now the room's
    /// interior — four tiles of open ground and a tier-1 firing platform, behind a single door on
    /// the west wall where the original's doorway gap was. Same silhouette, same one way in, and an
    /// inside that exists.
    ///
    /// THIS IS THE COMMIT P47 SAID WOULD SEVER THE CRN STREAM, and it is deliberately alone: edges
    /// only, no site glyphs. P26's `T`/`C`/`E` machinery stays inert for one more wave, because
    /// L7's lesson is that two levers measured together give a number that does not resolve, and
    /// "a room" and "an objective inside the room" are two levers.
    ///
    /// `SIGHTLINE_EDGEARENAS=0` restores `Layouts[CitadelIndex]` — the pre-P48 board exactly, by
    /// handing back the very array it always handed back.
    public const int CitadelIndex = 4;

    /// The signature row of the legacy CITADEL. `EdgeSelfTest` asserts `Layouts[CitadelIndex]`
    /// still contains it, so reordering `Layouts` fails loudly instead of silently redrawing some
    /// other arena.
    public const string CitadelSignature = ".....######.......";

    public static bool EdgeArenas = true;

    // ══ P49 — SOMETHING WORTH GOING IN FOR ══════════════════════════════════════════════════
    /// P48 gave CITADEL an inside. This puts something in it.
    ///
    /// P42's finding was that a rectangle of wall buys HUNKERING, because it gives cover without
    /// giving a REASON to go in; P48's forced round then measured that the room trades POSITION
    /// choices for TARGET choices, which is a different trade but still not a reason. The reason is
    /// the objective: `T` seats the HACK terminal in the interior's far corner from the door, and
    /// `C` seats the RESCUE captive in the other. On those two objectives the fight now converges
    /// inside a room with one way in — which is the thing thirty-five arenas could never say,
    /// because before P26 every site was a LITERAL tile with a bare 3x3 punched around it.
    ///
    /// THIS IS THE COMMIT THAT ARMS P26, three programs after P26 shipped. `Maps.AnySiteTemplates`
    /// flips true here, `Mission.PlanBoard` starts taking over the arena gate, and
    /// `ARENASITETEST` leg (E) — written in P26 specifically to stay SKIPPED until this day —
    /// arms. It is a separate lever from P48's room on purpose (L7: two measured together do not
    /// resolve), and it has its own dial.
    ///
    /// ⚠ **IT DEFAULTS OFF, AND THE MEASUREMENT IS WHY.** Priced on the doubly-forced instrument
    /// (`SIGHTLINE_MAP=4 SIGHTLINE_OBJ=hack`, so every mission is a HACK on this arena), 960 CRN
    /// pairs per rung, `docs/measurements/p49/`:
    ///
    ///     win rate   h0 66.2 -> 91.9   h4 40.0 -> 94.1   h8 18.1 -> 90.6   pooled +50.7, z +21.1
    ///     choices    h0 4.06 -> 1.18   h4 3.93 -> 1.34   h8 2.06 -> 0.77   every rung RESOLVED
    ///     shots      turnsWithAShot 54.3% -> 36.8%;  soldier deaths 5,592 -> 1,410;  turns 4.1 -> 3.4
    ///
    /// A terminal behind one door with NOTHING SEATED INSIDE is not a reason to go in — it is a
    /// place the fight cannot follow you into. The squad walks in, hacks, and wins at 90%+ at every
    /// rung including the apex, where heat stops mattering at all. That is the signature of an
    /// UNCONTESTED objective, and it removes the fight rather than concentrating it.
    ///
    /// The machinery is right and is kept: the format expresses the room, `PlanBoard` seats the
    /// sites in it, and the gates hold. What is missing is a GARRISON — P26's `A` enemy-pod anchor
    /// glyph, still unused by every template. `SIGHTLINE_SITEGLYPHS=1` turns the sites on and is
    /// the arm that round was read on.
    public static bool SiteGlyphs = false;

    static string[] _stripped;

    /// The glyph-free form of `CitadelEdged`: every site glyph replaced by open floor. Derived, not
    /// a second copy — a hand-maintained twin of a 23-row template is a transcription error waiting
    /// to happen, and `EdgeSelfTest` asserts the two differ ONLY in the glyph cells.
    public static string[] CitadelEdgedNoSites
    {
        get
        {
            if (_stripped == null)
            {
                _stripped = new string[CitadelEdged.Length];
                for (int i = 0; i < CitadelEdged.Length; i++)
                {
                    var row = CitadelEdged[i].ToCharArray();
                    for (int c = 0; c < row.Length; c++)
                        if (row[c] is 'T' or 'X' or 'E' or 'C' or 'P' or 'A') row[c] = '.';
                    _stripped[i] = new string(row);
                }
            }
            return _stripped;
        }
    }

    public static readonly string[] CitadelEdged =
    {
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + +-+-+-+-+ + + + + + + + +",
        " . . . . . .|. . . T|. . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . .|. ^ ^ .|. . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . .+. ^ ^ .|. . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . .|. . . C|. . . . . . . . ",
        "+ + + + + + +-+-+-+-+ + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
        " . . . . . . . . . . . . . . . . . . ",
        "+ + + + + + + + + + + + + + + + + + +",
    };

    /// The SOURCE rows for `Layouts[i]` — the redrawn form where one exists and the flag is on.
    public static string[] Source(int i)
        => EdgeArenas && i == CitadelIndex
             ? (SiteGlyphs ? CitadelEdged : CitadelEdgedNoSites)
             : Layouts[i];

    static Arena[] _arenas;
    static bool _arenasFor, _arenasGlyphs;   // which arms `_arenas` was parsed for


    /// The parsed form of `Layouts[i]`, cached. Parsing is pure and the templates are `const`-ish,
    /// so this is done once — but it is LAZY for the same reason `AnySiteTemplates` is: `Layouts`
    /// is declared below and C# runs static initializers in declaration order.
    public static Arena ArenaAt(int i)
    {
        if (_arenas == null || _arenasFor != EdgeArenas || _arenasGlyphs != SiteGlyphs)
        {
            _arenas = new Arena[Layouts.Length];
            _arenasFor = EdgeArenas; _arenasGlyphs = SiteGlyphs;
            for (int k = 0; k < Layouts.Length; k++)
                if (!TryParse(Source(k), out _arenas[k], out string why))
                    throw new InvalidOperationException($"Maps arena {k} is malformed: {why}");
        }
        return _arenas[i];
    }

    /// Does ANY shipped template declare an edge? The inertness gate, mirroring `AnySiteTemplates`.
    public static bool AnyEdgeTemplates
    {
        get
        {
            for (int i = 0; i < Layouts.Length; i++) if (ArenaAt(i).HasEdges) return true;
            return false;
        }
    }


    public static readonly string[][] Layouts =
    {
        new[] // PLAZA — a raised central platform ringed with cover
        {
            "..................",
            "......o....o......",
            "....#........#....",
            ".......^^^^.......",
            "......^^^^^^......",
            ".......^^^^.......",
            "......^^^^^^......",
            ".......^^^^.......",
            "....#........#....",
            "......o....o......",
            "..................",
        },
        new[] // GAUNTLET — staggered cover lanes flanking a central plateau spine
        {
            "..................",
            "...oo......##.....",
            ".........^^.......",
            "....##...^^...oo..",
            ".........^^.......",
            "...oo....##....o..",
            ".........^^.......",
            "....##...^^...oo..",
            ".........^^.......",
            "...oo......##.....",
            "..................",
        },
        new[] // PILLARS — a regular field of high-cover columns with open aisles; two explosive
              // BARRELS ('B') sit in the mid-field aisles, a tempting shot to catch a foe who
              // ducked behind a column for cover. They drop in already-open aisle tiles, so the
              // wide cross-aisles top/bottom and every vertical lane stay clear.
        {
            "..................",
            "...#..#..#..#..#..",
            "..................",
            "...#..#.B#..#..#..",
            "..................",
            "...#..#..#..#..#..",
            "..............B...",
            "...#..#..#..#..#..",
            "..................",
            "...#..#..#..#..#..",
            "..................",
        },
        new[] // CHEVRON — a diagonal cover wall + a raised redoubt, breaking sightlines
        {
            "..................",
            ".....#............",
            "......#....^^.....",
            ".......#..^^^^....",
            "....o...#..^^.....",
            ".....o...#........",
            "......o...#.......",
            ".......o...#......",
            "........o...#.....",
            "..................",
            "..................",
        },
        new[] // CITADEL — a fortified high-cover bunker with a plateau and a doorway
        {
            "..................",
            "..................",
            ".....######.......",
            ".....#....#.......",
            ".....#.^^.#.......",
            ".......^^.#.......",
            ".....#....#.......",
            ".....######.......",
            "..................",
            "..................",
            "..................",
        },
        new[] // ZIGGURAT — a stepped mound: a commanding tier-2 core ringed by tier-1
        {
            "..................",
            "...o...^^^^...o...",
            "......^^^^^^......",
            "....#^^====^^#....",
            ".....^^====^^.....",
            ".....^^====^^.....",
            "....#^^====^^#....",
            "......^^^^^^......",
            "...o...^^^^...o...",
            "..................",
            "..................",
        },
        new[] // CROSSROADS — staggered pillar rows create sightline-channelling lanes;
              // a central tier-1 plateau is contested high ground; three open horizontal
              // routes (top / centre / bottom) let squads pick approach angle
        {
            "..................",
            "....o.........o...",
            "...#..#...#..#....",
            "..................",
            ".....o.....o......",
            "....#...^^...#....",
            ".....o.....o......",
            "..................",
            "...#..#...#..#....",
            "....o.........o...",
            "..................",
        },
        new[] // FOXHOLES — dense CQB low-cover warren with two high-cover strongpoints; a couple
              // of explosive BARRELS ('B') tuck against the strongpoints — shoot one to blow a
              // hole in an enemy nest. Short engagement ranges, lots of duck-and-move; the open
              // central seam (col 7-8) and the flanks stay clear so the warren is still traversable.
        {
            "..................",
            "...oo.....oo......",
            ".....oo.oo........",
            "....#B....#.......",
            "....oo..oo........",
            "..................",
            "....oo..oo........",
            "....#....B#.......",
            ".....oo.oo........",
            "...oo.....oo......",
            "..................",
        },
        new[] // RIDGE — a diagonal tier-1 ridge with a tier-2 commanding peak at centre;
              // low-cover approaches bracket the slope; seizing height is decisive
        {
            "..................",
            ".....o............",
            "......^...o.......",
            ".......^^.........",
            "........^^^.......",
            "......o.=^^.o.....",
            ".......^^^........",
            "..........^^......",
            "...o.......^......",
            "............o.....",
            "..................",
        },
        new[] // RUINS — an exposed arena with shattered perimeter walls and an open
              // centre; long sightlines reward ranged classes but the raised slabs give
              // a height advantage to whoever seizes them first. Biome hint: VOID.
        {
            "..................",
            "....##......##....",
            "....#....o....#...",
            "..................",
            ".......^^.........",
            ".......^^..o......",
            "..................",
            "....o..........o..",
            "....#....o....#...",
            "....##......##....",
            "..................",
        },
        new[] // THICKET — dense organic low-cover clusters separated by winding
              // corridors; two high-cover anchors give the squad fixed strongpoints;
              // short engagements, lots of duck-and-move. Biome hint: VERDANT.
        {
            "..................",
            "....oo......oo....",
            "...o.o....oo......",
            "....oo..#.........",
            "..........oo.oo...",
            "....o.....#.o.....",
            "..........oo.oo...",
            "....oo..#.........",
            "...o.o....oo......",
            "....oo......oo....",
            "..................",
        },
        new[] // BASTION — a central fortress: a commanding tier-2 ('=') keep walled by
              // high cover, breached by gaps (north & south of the wall, plus a west
              // doorway through the keep itself). The high ground is the prize, but you
              // must fight to a breach to seize it — a set-piece assault. Biome hint: STEEL.
        {
            "..................",
            "......##..##......",
            "......#....#......",
            "....###.==.###....",
            ".......===........",
            "......#.==.#......",
            "....###.==.###....",
            "......#....#......",
            "......##..##......",
            "..................",
            "..................",
        },
        new[] // CHASM — a vertical "river" of high cover splits the board top-to-bottom,
              // pierced by two clear crossing points (rows 3 & 7) bracketed by low cover.
              // The fight funnels through the chokepoints; holding a crossing controls the
              // flow between the two halves. Explosive BARRELS ('B') sit just off each crossing's
              // low-cover bracket — a shot blows the chokepoint as a foe funnels through it. The
              // crossing rows (3 & 7) stay fully open, so neither chokepoint is ever sealed.
              // Biome hint: TUNDRA (a frozen ravine).
        {
            "..................",
            "........##........",
            "......Bo##o.......",
            "..................",
            "........##........",
            "........##........",
            "........##........",
            "..................",
            ".......o##oB......",
            "........##........",
            "..................",
        },
        new[] // SPUR — a diagonal tier-1 high-ground spine sweeps corner to corner: a
              // commanding kill-lane that dominates the centre but is exposed at both
              // ends. Low-cover nests bracket the slope as covered firing steps onto it.
              // Biome hint: ARID (a sun-baked ridge).
        {
            "..................",
            "...^..............",
            "....^^...o........",
            ".....^^...........",
            "..o...^^..........",
            ".......^^....o....",
            "........^^........",
            ".....o...^^.......",
            "..........^^...o..",
            "............^^....",
            "..................",
        },
        new[] // HOOK — asymmetric: a fortified high-cover strongpoint (with a redoubt
              // arm) anchors the top, forcing attackers to either grind through it or
              // swing the wide-open bottom flank. A low-cover diagonal channels that
              // bottom hook into a covered approach. Biome hint: ASH (a ruined outpost).
        {
            "..................",
            ".....####.........",
            ".....#..#....o....",
            ".....#..####......",
            ".....#.....#......",
            ".......o...#......",
            ".........o........",
            "...........o......",
            "..................",
            "..................",
            "..................",
        },
        new[] // GRID — a NEON server-room: a regular lattice of 2x2 high-cover "racks"
              // separated by clean orthogonal aisles (vertical at cols 0-1/4-5/8-9/12-13/
              // 16-17, horizontal at rows 0/3/6/9-10), with low-cover terminals dotting the
              // mid aisles. Movement is corridor-bound and right-angled (no diagonals through
              // a rack), so it plays as tight, blind-corner CQB unlike the open pillar field.
              // Biome hint: NEON.
        {
            "..................",
            "..##..##..##..##..",
            "..##..##..##..##..",
            "....o......o......",
            "..##..##..##..##..",
            "..##..##..##..##..",
            "....o......o......",
            "..##..##..##..##..",
            "..##..##..##..##..",
            "..................",
            "..................",
        },
        new[] // FORGE — a MAGMA foundry: a commanding tier-2 ('=') casting platform at the
              // centre, wrapped in a walkable tier-1 ('^') apron you can simply walk up onto
              // (no walls — the height is openly contested, unlike BASTION's breach-only keep).
              // Four corner high-cover smelters + low-cover ingot piles give covered firing
              // steps onto the slope. Seizing the platform dominates the whole field.
              // Biome hint: MAGMA.
        {
            "..................",
            "...#..........#...",
            "......^^^^^^......",
            ".....^^====^^.....",
            "..o..^^====^^..o..",
            ".....^^====^^.....",
            "..o..^^====^^..o..",
            ".....^^^^^^^^.....",
            "...#..........#...",
            "..................",
            "..................",
        },
        new[] // CONDUIT — a horizontally-split complex: fortified high-cover bunkers banking
              // the NORTH and SOUTH, divided by a wide open central channel (row 5, the
              // "conduit"). The fight runs ALONG and ACROSS the channel — the inverse axis of
              // CHASM's vertical river. Low-cover nodes flank the channel as contested
              // stepping points; the open lane is the fast-but-exposed flanking route.
        {
            "..................",
            "...####..####.....",
            "...#..o..o..#.....",
            "...#........#.....",
            "......o..o........",
            "..................",
            "......o..o........",
            "...#........#.....",
            "...#..o..o..#.....",
            "...####..####.....",
            "..................",
        },
        new[] // PALISADE — a staggered mid-field SCREEN of high cover (cols 7-11) that breaks the
              // long cross-board sightlines, in the Phase-4.2 encounter-geometry spirit: no column is
              // fully walled, ROW 5 is the one open "risky direct" lane straight up the middle, and
              // low-cover firing steps bracket the screen so the squad can advance under cover and
              // pick its breach rather than being seen across the whole board. A deliberate approach.
        {
            "..................",
            ".......#.#........",
            ".....o.#...#.o....",
            ".......#.#.#......",
            ".....#...#...#....",
            "..................",   // row 5: the open risky lane
            ".....#...#...#....",
            ".......#.#.#......",
            ".....o.#...#.o....",
            ".......#.#........",
            "..................",
        },
        new[] // TERRACE — a split-level set-piece: a commanding tier-2 ('=') firing terrace banks the
              // NORTH, openly walkable up a tier-1 ('^') ramp (no walls — the height is contested, not
              // gated), while a high-cover screen breaks the centre and the SOUTH stays an open flank.
              // Seizing the terrace dominates the field; taking the open south lane trades height for
              // speed. The first arena to put the tier-2 legend on a reachable, fought-over vantage.
        {
            "....===.==........",
            "....^^^.^^........",
            "..................",
            ".....#..#..#......",
            "......#..#..#.....",
            ".....#..#..#......",
            "..................",
            "......o....o......",
            ".....#......#.....",
            "..................",
            "..................",
        },
        new[] // WISHBONE — two diagonal high-cover walls fan out from a central spine into a wide V,
              // funnelling the approach through a single mid-field BREACH (the contested crossing) while
              // leaving both rims open to a wide flank. Low-cover nests give covered footing to the
              // breach. A strong slanted sightline break that rewards committing to a lane or swinging wide.
        {
            "..................",
            ".......#..#.......",
            "......#....#......",
            ".....#......#.....",
            "....#...oo...#....",
            ".......o..o.......",   // the breach is the gap between the walls' inner mouths
            "....#...oo...#....",
            ".....#......#.....",
            "......#....#......",
            ".......#..#.......",
            "..................",
        },
        new[] // HIGHLAND — an OFF-CENTRE commanding redoubt: a tier-2 ('=') vantage on the
              // RIGHT-of-centre, walkable up a tier-1 ('^') apron that wraps its west + north
              // (no walls — the height is openly contested, but it's a flank prize tucked toward
              // the enemy half, not a central pyramid like ZIGGURAT/FORGE). Sparse low/high-cover
              // firing steps bracket the slope. Seizing the redoubt dominates the right-side
              // approach lanes and sees over low cover across the field. Biome hint: ARID.
        {
            "..................",
            "..............=...",
            "...........^^==...",
            "..........^^==^...",
            ".....o....^^==....",
            "..........^^==.o..",
            "...........^^=^...",
            "....#......^^.....",
            ".......o.....#....",
            "..................",
            "..................",
        },
        new[] // APPROACH — an ASYMMETRIC density gradient: the NORTH half is a dense high-cover
              // maze (slow, safe, lots of sightline breaks) while the SOUTH half is wide-open
              // ground (fast, exposed, no footing). The squad chooses a side — grind the covered
              // top lane or race the open bottom flank and trade safety for tempo. No column is
              // walled and the mid rows stay porous so either commitment stays traversable.
        {
            "..................",
            "....#..##..#.#....",
            "...o..#..#..o.....",
            "......##..##......",
            "....o...#...o.....",
            ".......#..#.......",
            ".........o........",
            "....o.............",
            "..................",
            "..................",
            "..................",
        },
        new[] // KILLBOX — a wide central open PLAZA ringed by a broken wall of high cover, with
              // deliberate BREACHES at the cardinal mid-points (a north gap, a south gap, and the
              // whole of row 5 left open east-west). Whoever holds the ring's firing slits dominates
              // anyone caught crossing the plaza — but the gaps mean it's never a sealed bunker
              // (unlike CITADEL); you fight FOR the ring, then fight ACROSS the killing floor.
              // Low-cover slits on the east/west walls give covered angles into the centre.
        {
            "..................",
            "....######.##.....",
            "....#........#....",
            "....#........#....",
            "....o........o....",
            "..................",
            "....o........o....",
            "....#........#....",
            "....##.######.....",
            "..................",
            "..................",
        },
        new[] // TRENCHES — staggered parallel LINES of low cover spanning the width, offset row to
              // row so there's never a clean firing lane straight down the board. Plays as advance-
              // by-bounds: a soldier dashes from one trench to the next under cover while overwatch
              // holds the gap, leapfrogging toward the enemy. Low cover only (no LoS blocks), so the
              // whole field stays readable and every position is half-protected — a war of footing
              // and tempo, distinct from FOXHOLES' tight clustered CQB warren.
        {
            "..................",
            "...ooo...ooo......",
            "..................",
            "......ooo...ooo...",
            "..................",
            "...ooo...ooo......",
            "..................",
            "......ooo...ooo...",
            "..................",
            "...ooo...ooo......",
            "..................",
        },
        new[] // GARRISON — a single full-height high-cover WALL across the mid-field (col 9) pierced by
              // ONE central breach (rows 4-6) bracketed by low-cover firing steps. The whole fight
              // funnels through the gap: hold the breach and you control the crossing, charge it and you
              // eat the overwatch. A deliberate CHOKEPOINT set-piece (the inverse of an open plaza) that
              // rewards a phalanx push or a patient overwatch hold. The wall has gaps top (rows 0-1) and
              // bottom (rows 9-10) so the rims are a wide-but-exposed flank, never a sealed bunker.
        {
            "..................",
            "..................",
            ".........#........",
            "........o#o.......",
            "..................",   // central breach (rows 4-6 open)
            ".........#........",
            "..................",
            "........o#o.......",
            ".........#........",
            "..................",
            "..................",
        },
        new[] // PINNACLE — a VERTICALITY set-piece: TWO commanding tier-2 ('=') peaks (north-left &
              // south-right) each walkable up a tier-1 ('^') ramp (no walls — the height is openly
              // contested), with a tier-1 saddle bridging the centre. Whoever seizes a peak sees over
              // low cover across the field and dominates one diagonal; the two peaks face off across the
              // open middle. Low-cover nests give covered footing at the base of each ramp. Uses the
              // tier-2 legend to make ELEVATION the whole point of the map.
        {
            "..................",
            ".....==^...o......",
            ".....=^^..........",
            "....o^^...^^......",
            "........^^^^......",
            ".......^^.^^......",
            "......^^...^^o....",
            "......^^...^=.....",
            "......o...^==.....",
            "..................",
            "..................",
        },
        new[] // REFINERY — a HAZARD set-piece: explosive BARRELS ('B') clustered around high-cover tank
              // berms in the mid-field, so the cover you'd duck behind is wired to blow. A well-placed
              // shot (or a foe's own grenade) chains the barrels and demolishes a whole nest — but a
              // barrel beside YOUR cover is a liability too. Open aisles (the spawn columns, the central
              // seam, and the top/bottom edges) keep it traversable; the barrels sit in already-open
              // tiles adjacent to cover so connectivity holds. The most volatile arena in the rotation.
        {
            "..................",
            ".......#.#........",
            "......B#.#B.......",
            ".......o.o........",
            "..................",
            "....o.B...B.o.....",
            "..................",
            ".......o.o........",
            "......B#.#B.......",
            ".......#.#........",
            "..................",
        },
        new[] // CRUCIBLE — a barrel-RIGGED central chokepoint: two high-cover bastions clamp the
              // mid-field into a narrow crossing (the open centre column), with explosive BARRELS ('B')
              // wired into the throat so the contested ground is itself a hazard — shoot a barrel as a
              // foe funnels through and the whole choke goes up, demolishing the bastions' inner wall.
              // The crossing column (col 9) and the top/bottom edge rows stay clear, and the flanks
              // are wide-open exposed lanes (fast but no cover). Hold the throat or burn it down.
              // Biome hint: MAGMA (a volatile refinery throat).
        {
            "..................",
            "......##...##.....",
            "......#o...o#.....",
            "......#B...B#.....",
            "......##...##.....",
            "..................",   // row 5: the open crossing seam
            "......##...##.....",
            "......#B...B#.....",
            "......#o...o#.....",
            "......##...##.....",
            "..................",
        },
        new[] // STEPWELL — a stepped pyramid with a commanding tier-2 ('=') summit, but UNLIKE the
              // symmetric ZIGGURAT/FORGE it is wrapped by two DISTINCT flanking lanes: a covered
              // low-cover gully along the NORTH and an open ramp-up along the SOUTH. Walk the tier-1
              // ('^') apron up onto the summit to dominate the field, or sweep a flank to avoid the
              // exposed climb. High cover anchors the corners as firing steps onto the slope.
              // Biome hint: STEEL.
        {
            "..................",
            "....o.o....o.o....",
            ".......^^^^.......",
            "....#.^^==^^.#....",
            "......^^==^^......",
            "......^^==^^......",
            "....#.^^^^^^.#....",
            ".......^^^^.......",
            "..................",
            "....o........o....",
            "..................",
        },
        new[] // COLONNADE — a long-sightline GALLERY: two ranks of paired high-cover pillars march
              // down the field in a regular rhythm, leaving wide firing lanes BETWEEN the ranks that
              // reward ranged duelling, while the pillars themselves give covered bounding steps from
              // one rank to the next. Low-cover plinths dot the central aisle as half-cover footholds.
              // No column is walled (each pillar pair has open tiles either side) and the long axis
              // stays readable end-to-end. Biome hint: VOID (a cavernous hall).
        {
            "..................",
            ".....##....##.....",
            ".....##....##.....",
            "........oo........",
            ".....##....##.....",
            ".....##....##.....",
            "........oo........",
            ".....##....##.....",
            ".....##....##.....",
            "..................",
            "..................",
        },
        new[] // ENTRENCHED — an ASYMMETRIC trench network: the NORTH half is a dense zig-zag warren of
              // low-cover trench lines (slow, half-protected advance-by-bounds) anchored by a high-cover
              // strongpoint, while the SOUTH half is wide-open exposed ground broken only by a lone
              // forward redoubt. The squad chooses the grinding covered top or the fast exposed bottom.
              // Low cover only up top means the whole field stays readable; no lane is fully sealed.
              // Biome hint: ARID (a dug-in desert front).
        {
            "..................",
            "....ooo..#.ooo....",
            "......oo..oo......",
            "....oo..oo..oo....",
            "......oo..oo......",
            "....ooo..#.ooo....",
            "..................",
            "..................",
            ".........#........",
            ".......o...o......",
            "..................",
        },
        new[] // CAUSEWAY — a raised tier-1 land-BRIDGE spans the mid-field as the ONLY good crossing:
              // walk up onto it and you command the centre, but you're the exposed high silhouette on
              // it. Impassable high-cover BANKS bracket the bridge (cols 7 & 12) so the fight funnels
              // ONTO and ACROSS the elevated span, and low-cover shorelines give covered footing at
              // each ramp. Unlike CHASM's cover river this chokepoint is ELEVATION, not a wall — you
              // seize the height OR skirt the open ends. Biome hint: TUNDRA (a frozen ford).
        {
            "..................",
            "..................",
            ".......#....#.....",
            "....o..#....#..o..",
            "......^^^^^^^^....",
            "......^^^^^^^^....",
            "......^^^^^^^^....",
            "....o..#....#..o..",
            ".......#....#.....",
            "..................",
            "..................",
        },
        new[] // REDANS — an ASYMMETRIC diagonal GAUNTLET: a staggered sawtooth of angular high-cover
              // fieldworks marches corner-to-corner so every advance up the slant is enfiladed by the
              // NEXT work's firing face — you're never safe in the open between them. A tier-1 KNOLL
              // anchors the upper-right flank as commanding high ground; seize it to see over the works
              // and break the gauntlet, or grind the covered zig-zag lane. Low-cover nests give footing
              // between the teeth. Distinct from SPUR's clean spine — this is repeated angular works.
              // Biome hint: ASH (a ruined earthworks line).
        {
            "..................",
            "....#......^^.....",
            "...#.#....^^^^....",
            "....#......^^.....",
            ".....#....o.......",
            "......#....#......",
            ".......o..#.#.....",
            "....o...#....#....",
            ".........#...#....",
            "..........#.......",
            "..................",
        },
        new[] // DONJON — a walled tier-2 ('=') KEEP whose commanding core is reached by a SINGLE
              // walkable tier-1 ('^') RAMP on the west face; the other three faces are high-cover walls
              // pierced only by firing SLITS (the gaps in row 2 & the flanks). Unlike BASTION (breach
              // the wall) you take the keep by fighting to the ramp mouth and climbing — a set-piece
              // assault on a gated vantage. Scattered pillars + low-cover nests bracket the two open
              // approach lanes. The tier-2 summit sees over all cover and dominates the field.
              // Biome hint: STEEL.
        {
            "..................",
            "......#####.......",
            "......#.#.#.......",
            "......#.=.#..o....",
            "....o.^^==.#......",
            "......^.==.#......",
            "....o.^^==.#......",
            "......#.=.#..o....",
            "......#####.......",
            "..................",
            "..................",
        },
    };
    // ── PROGRAM RESONANCE T1 — the TRAINING OP arena ────────────────────────────────────────
    // DELIBERATELY NOT in `Layouts`: appending it there would change Layouts.Length, which feeds
    // the daily's arena derivation and the per-run no-repeat deck — i.e. it would move the whole
    // measured campaign. The drill arena is applied by its own path (Mission.BuildTraining), so
    // the flywheel stays byte-stable.
    //
    // Read it as the lesson plan it is (x -> 0..17, y -> 0..10):
    //   * the squad deploys at (2,4)/(2,6) with LOW cover at (4,4)/(4,6) two steps away
    //     -> lesson 2 (take cover) is solvable on the first move.
    //   * two hostiles sit at (12,4)/(12,6) behind HIGH cover at (11,4)/(11,6) — full cover from
    //     due west, NO cover north/south, so lesson 3 (flank) is a real positioning problem with
    //     one clean answer: walk the open column x=12 to (12,1)/(12,9), which are themselves
    //     beside the HIGH cover at (13,1)/(13,9) — the flank tile is also a safe tile.
    //   * the '^' plateau at (6..8,5) is the optional high-ground read down the centre lane.
    //   * two more hostiles wait at (16,3)/(16,7) in the open for the grenade/ability lessons.
    public static readonly string[] TrainingArena =
    {
        "..................",
        "....#........#....",
        ".......o..........",
        "..........o.......",
        "....o......#......",
        "......^^^.........",
        "....o......#......",
        "..........o.......",
        ".......o..........",
        "....#........#....",
        "..................",
    };

    /// Drill deploy tiles + the fixed hostile seats, kept next to the template they were
    /// authored against so a future edit to one can't silently invalidate the other.
    public static readonly (int x, int y)[] TrainingDeploy = { (2, 4), (2, 6) };
    public static readonly (int x, int y)[] TrainingFoes   = { (12, 4), (12, 6), (16, 3), (16, 7) };
}
