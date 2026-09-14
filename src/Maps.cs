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
public static class Maps
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
    public static bool AnySiteTemplates
    {
        get { if (_anySite < 0) _anySite = ComputeAnySiteTemplates() ? 1 : 0; return _anySite == 1; }
    }

    static bool ComputeAnySiteTemplates()
    {
        foreach (var tpl in Layouts)
            foreach (var row in tpl)
                foreach (char c in row)
                    if (c == 'T' || c == 'X' || c == 'E' || c == 'C' || c == 'P' || c == 'A') return true;
        return false;
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
