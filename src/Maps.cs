namespace Sightline;

/// Hand-authored arena layouts, applied over the procedural generator for map
/// character. Legend (one char per tile):
///   '.' floor   'o' low cover   '#' high cover   '^' high-ground (walkable plateau)
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
    };
}
