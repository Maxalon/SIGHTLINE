namespace Sightline;

/// Hand-authored arena layouts, applied over the procedural generator for map
/// character. Legend (one char per tile):
///   '.' floor   'o' low cover   '#' high cover
///   '^' tier-1 plateau   '=' tier-2 plateau   (both walkable high ground)
/// Each layout is GridH (11) rows of GridW (18) chars. Reserved tiles — player and
/// enemy spawns, the evac zone, the terminal + its ring — are always left as open
/// floor regardless of the template, and `Mission` verifies connectivity before
/// committing to a layout (falling back to the procedural generator otherwise).
public static class Maps
{
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
        new[] // PILLARS — a regular field of high-cover columns with open aisles
        {
            "..................",
            "...#..#..#..#..#..",
            "..................",
            "...#..#..#..#..#..",
            "..................",
            "...#..#..#..#..#..",
            "..................",
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
        new[] // FOXHOLES — dense CQB low-cover warren with two high-cover strongpoints;
              // short engagement ranges, lots of duck-and-move; flanks stay open
        {
            "..................",
            "...oo.....oo......",
            ".....oo.oo........",
            "....#.....#.......",
            "....oo..oo........",
            "..................",
            "....oo..oo........",
            "....#.....#.......",
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
              // flow between the two halves. Biome hint: TUNDRA (a frozen ravine).
        {
            "..................",
            "........##........",
            ".......o##o.......",
            "..................",
            "........##........",
            "........##........",
            "........##........",
            "..................",
            ".......o##o.......",
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
    };
}
