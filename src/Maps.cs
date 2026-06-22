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
    };
}
