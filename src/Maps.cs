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
    };
}
