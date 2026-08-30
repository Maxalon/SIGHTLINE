using System;
using System.Collections.Generic;

namespace Sightline;

/// What a floor tile is MADE OF. Until wave C4 the eight biomes were a colour scheme:
/// `grep -ci biome` returned 0 in Combat.cs, Ai.cs, Grid.cs and Unit.cs, so the place you
/// were fighting in never changed the fight. V3 "SURFACES" drew a fissure that snaked across
/// six tiles and went nowhere; this layer is the other half — the fissure is now ON THE BOARD,
/// in tile space, and it bites.
///
/// NOT PERSISTED. `SaveGame` stores the campaign map as one int (`MapSeed`) and never
/// serialises a Grid, so this enum is NOT one of the thirteen persisted-by-ordinal enums and
/// carries no append-only obligation. (Verified: `Tiles`/`Height`/`Smoke`/`Fire`/`Barrel` appear
/// nowhere in SaveGame.cs either.) It is regenerated from (MapSeed, mission) at every
/// SetupMission, exactly like the arena itself.
public enum GroundKind
{
    None = 0,
    Undergrowth,   // VERDANT — dense fern: low cover from EVERY angle, but only against distant fire
    Ice,           // TUNDRA  — slick frost drift: costs half a step to cross
    Vent,          // MAGMA   — a steaming fissure: opaque, dear to cross, and it sets you alight
}

/// The BIOME MECHANIC layer (wave C4 "EIGHT BIOMES ARE PAINT").
///
/// THREE biomes are mechanical, on three DIFFERENT axes, and each one is the mechanic its own
/// art already implied:
///   VERDANT -> UNDERGROWTH  (the COVER axis)     "the ferns hide you from anything far away"
///   TUNDRA  -> SLICK ICE    (the MOVEMENT axis)  "the drift is a fast lane"
///   MAGMA   -> THERMAL VENTS(the SIGHT axis)     "you cannot see across the crack, and it burns"
/// The other five (STEEL / ARID / ASH / VOID / NEON) are still paint. That is declared, not
/// hidden — see docs/DEVLOG.md §C4 "what I did not do".
///
/// SYMMETRY is structural, not promised: every rule lives in one of the three functions BOTH
/// sides already ask for the truth — `Grid.GetCover`, `Grid.CostMap`, `Grid.HasLineOfSight` —
/// so `Ai.cs` and `Combat.cs` cannot play the old game on the new board. Nothing here reads
/// `a.Team`.
///
/// DETERMINISM: every tile is derived from (MapSeed, mission) through `Util.Hash3`. ZERO draws
/// from `Util.Rng` and no `System.Random`, so `SIGHTLINE_PAIRTEST` stays byte-identical and the
/// balance flywheel's CRN pairing is untouched.
public static class Terrain
{
    /// Master gate (SIGHTLINE_BIOMEMECH=0 turns the whole layer off, for the A/B measurement and
    /// for the "a test that cannot fail is not a test" proof). Gated at the PREDICATES, not just
    /// at the stamper, so a hand-stamped harness board is inert too.
    public static bool Enabled = true;

    // Indices into Biome.All (STEEL ARID TUNDRA VERDANT ASH VOID NEON MAGMA).
    public const int BiomeTundra  = 2;
    public const int BiomeVerdant = 3;
    public const int BiomeMagma   = 7;

    // ---- VERDANT: UNDERGROWTH ----------------------------------------------------------
    // Standing in the ferns is LOW cover from every angle — but only against fire from more
    // than FoliageMinDist tiles away. Close in and the foliage is worth nothing, so the counter
    // to a soldier in the undergrowth is to CLOSE, not to out-angle. It is exactly a cover
    // level, so high ground / a DRONE / a SYNDICATE optic see over it like any low block.
    public const int FoliageMinDist = 2;   // Chebyshev; concealment applies at dist > this

    // ---- TUNDRA: SLICK ICE -------------------------------------------------------------
    // Costs (Grid.CostMap is in HALF-tiles: orthogonal 2, diagonal 3). Ice halves both, so a
    // drift is a lane you can ride twice as far along. Shared by both teams through the one
    // cost map, and visible to the player as a bulge in the move overlay.
    public const int IceStepOrth = 1;
    public const int IceStepDiag = 2;

    // ---- MAGMA: THERMAL VENTS ----------------------------------------------------------
    // A vent is opaque (steam — it blocks sight exactly like smoke, for BOTH teams and for the
    // commanding see-over-high shooter), dear to force a crossing through, and hot.
    public const int VentStepExtra = 6;    // extra half-tiles added to the step onto a vent
    public const int VentBurnTurns = 2;    // Burning applied to whoever touches one

    /// The board-space tag shown beside the biome name in the mission banner ("VERDANT ·
    /// UNDERGROWTH"), or null for the five biomes that are still paint.
    public static string Tag(int biomeIndex) => !Enabled ? null : biomeIndex switch
    {
        BiomeVerdant => "UNDERGROWTH",
        BiomeTundra  => "SLICK ICE",
        BiomeMagma   => "THERMAL VENTS",
        _            => null,
    };

    /// The one-sentence player-facing rule (briefing card / codex / pause help).
    public static string Rule(int biomeIndex) => !Enabled ? null : biomeIndex switch
    {
        BiomeVerdant => "UNDERGROWTH: the ferns give LOW COVER from every angle - but only against fire from more than "
                        + FoliageMinDist + " tiles away. Close in to strip it.",
        BiomeTundra  => "SLICK ICE: crossing a frost drift costs HALF a step, so the drift is a fast lane - for both sides.",
        BiomeMagma   => "THERMAL VENTS: no one can see across a steaming fissure, forcing a crossing costs movement, "
                        + "and touching one sets you alight.",
        _            => null,
    };

    /// deterministic [0,1) from (seed, index, salt) — the same Hash3 mixer the campaign map,
    /// the arena deck and V3's board features all derive from. ZERO Util.Rng draws.
    static float H(int seed, int i, int salt) => (Util.Hash3(seed, i, salt) & 0xFFFFFFu) / 16777216f;
    static int HI(int seed, int i, int salt, int n) => n <= 0 ? 0 : (int)(Util.Hash3(seed, i, salt) % (uint)n);

    /// Stamp this mission's ground layer. Called from Game.SetupMission AFTER the arena, the
    /// force and every objective fixture are final, so `reserved` can hold everything the layer
    /// must not touch (unit tiles + their ring, evac, terminal, sabotage sites, the captive's
    /// cage, the intel cache). Idempotent: it clears first, so a re-stamp is exact.
    public static void Stamp(Grid grid, int biomeIndex, int seed, int missionNum,
                             HashSet<(int x, int y)> reserved)
    {
        if (grid == null) return;
        grid.ClearGround();
        if (!Enabled) return;
        // The mission number is folded into the seed so consecutive missions of one run get
        // different fissures/drifts even when the biome repeats (it does, every 8 missions).
        int s = unchecked((int)Util.Hash3(seed, missionNum, 0x1A7E));

        // HARD TILE BUDGET per biome. A blob/lane/crack walker has a long tail — one seed in
        // twenty puts three patches on top of each other — and a board where a third of the floor
        // is fern is a board where cover has stopped meaning anything. The budget is the ceiling
        // BIOMETEST pins; the walkers just stop when they hit it.
        int budget = biomeIndex switch { BiomeVerdant => 44, BiomeTundra => 34, BiomeMagma => 24, _ => 0 };
        bool Free(int x, int y)
        {
            if (budget <= 0) return false;
            if (!grid.IsFloor(x, y)) return false;                  // floor only; barrels excluded
            if (grid.Ground[x, y] != GroundKind.None) return false;
            if (reserved != null && reserved.Contains((x, y))) return false;
            return true;
        }
        void Put(int x, int y, GroundKind k) { if (Free(x, y)) { grid.Ground[x, y] = k; budget--; } }

        switch (biomeIndex)
        {
            case BiomeVerdant: StampPatches(grid, s, GroundKind.Undergrowth, Put); break;
            case BiomeTundra:  StampLanes(grid, s, GroundKind.Ice, Put);           break;
            case BiomeMagma:   StampFissure(grid, s, Put);                          break;
        }
        grid.RefreshGroundFlags();
    }

    /// VERDANT — 4..6 lobed fern patches, each a short walk of overlapping radius-1 discs.
    /// Deliberately BLOBBY: undergrowth is a place you stand IN, so it has to be several tiles
    /// wide or it is just a decorated tile.
    static void StampPatches(Grid grid, int s, GroundKind kind, Action<int, int, GroundKind> put)
    {
        // Tuned to land ~18-22% of the 198-tile board under fern: enough that "stand in the
        // undergrowth" is a live option on most turns, far short of a board where cover stops
        // meaning anything. BIOMETEST's groundFlood guard pins the ceiling.
        int n = 3 + HI(s, 0, 11, 2);                       // 3..4 patches
        for (int i = 0; i < n; i++)
        {
            float px = 2f + H(s, i, 21) * (grid.W - 4f);
            float py = 1f + H(s, i, 22) * (grid.H - 2f);
            float ang = H(s, i, 23) * MathF.Tau;
            int lobes = 2 + HI(s, i, 24, 2);               // 2..3 lobes
            for (int k = 0; k < lobes; k++)
            {
                int cx = (int)MathF.Round(px), cy = (int)MathF.Round(py);
                int r = 1 + (H(s, i, 40 + k) < 0.20f ? 1 : 0);   // radius 1, sometimes 2
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                        if (Math.Abs(dx) + Math.Abs(dy) <= r) put(cx + dx, cy + dy, kind);
                ang += (H(s, i, 60 + k) - 0.5f) * 1.6f;
                float step = 1.4f + H(s, i, 80 + k) * 1.4f;
                px += MathF.Cos(ang) * step; py += MathF.Sin(ang) * step;
            }
        }
    }

    /// TUNDRA — 2..3 frost drifts, each a LONG walked lane (that is the point: a lane you can
    /// ride, not a puddle you can stand in). Width 1, widened to 2 on about a third of steps.
    static void StampLanes(Grid grid, int s, GroundKind kind, Action<int, int, GroundKind> put)
    {
        int n = 2 + HI(s, 0, 12, 2);                       // 2..3 drifts
        for (int i = 0; i < n; i++)
        {
            float px = 1f + H(s, i, 31) * (grid.W - 2f);
            float py = 1f + H(s, i, 32) * (grid.H - 2f);
            float ang = H(s, i, 33) * MathF.Tau;
            int len = 9 + HI(s, i, 34, 6);                 // 9..14 steps
            for (int k = 0; k < len; k++)
            {
                int cx = (int)MathF.Round(px), cy = (int)MathF.Round(py);
                put(cx, cy, kind);
                if (H(s, i, 100 + k) < 0.24f)              // a wider shoulder here and there (kept RARE: a drift that widens often stops being a lane and becomes half the board)
                {
                    put(cx + (H(s, i, 120 + k) < 0.5f ? 1 : -1), cy, kind);
                    put(cx, cy + (H(s, i, 140 + k) < 0.5f ? 1 : -1), kind);
                }
                ang += (H(s, i, 160 + k) - 0.5f) * 0.85f;  // gently snaking, never a scribble
                px += MathF.Cos(ang) * 1.15f; py += MathF.Sin(ang) * 1.15f;
                Reflect(grid, ref px, ref py, ref ang);
            }
        }
    }

    /// Keep a walker on the board by REFLECTING its heading off the edge rather than stopping.
    /// The first cut broke out of the loop instead, which is why a drift that started near a wall
    /// and pointed at it laid down two tiles and a fissure laid down one — a mechanic that
    /// silently vanishes on some seeds is worse than one that is merely small (BIOMETEST's
    /// per-biome sparsity floor is what caught it).
    static void Reflect(Grid grid, ref float px, ref float py, ref float ang)
    {
        if (px < 0.5f)            { px = 0.5f;            ang = MathF.PI - ang; }
        else if (px > grid.W - 1.5f) { px = grid.W - 1.5f; ang = MathF.PI - ang; }
        if (py < 0.5f)            { py = 0.5f;            ang = -ang; }
        else if (py > grid.H - 1.5f) { py = grid.H - 1.5f; ang = -ang; }
    }

    /// MAGMA — 1..2 fissures: a snaking crack walked across the board with DELIBERATE GAPS.
    /// The gaps are load-bearing, not decoration: a continuous 1-tile crack would be a total
    /// sight barrier (Bresenham supercover always passes through a 1-wide wall), which would cut
    /// the board in half for shooting and stall the fight. The gaps are the FORDS — contested
    /// corridors where sight, and therefore the firefight, still crosses.
    static void StampFissure(Grid grid, int s, Action<int, int, GroundKind> put)
    {
        // TWO cracks, always. One was the first cut, and on a seed whose single walker reflected
        // back over its own track it laid down six tiles — a "mechanic" the player would meet on
        // maybe two turns of a mission. Two also reads better: a board with two fissures has two
        // sets of fords, so there is a route CHOICE rather than one obvious gap.
        const int n = 2;
        for (int i = 0; i < n; i++)
        {
            float px = 2f + H(s, i, 41) * (grid.W - 4f);
            float py = 0.5f + H(s, i, 42) * (grid.H - 1f);
            // bias the heading toward vertical so a fissure tends to divide the board across
            // the squad's left-to-right axis rather than lying along it
            float ang = (H(s, i, 43) < 0.5f ? MathF.PI * 0.5f : -MathF.PI * 0.5f)
                        + (H(s, i, 44) - 0.5f) * 1.5f;
            int len = 10 + HI(s, i, 45, 5);                // 10..14 steps each
            for (int k = 0; k < len; k++)
            {
                int cx = (int)MathF.Round(px), cy = (int)MathF.Round(py);
                if (H(s, i, 200 + k) >= 0.18f) put(cx, cy, GroundKind.Vent);   // ~18% of steps are FORDS
                // Wander kept LOW (0.55, vs 0.85 for a drift). The first MAGMA capture walked at
                // 0.9 and — after cover tiles ate a third of the steps — produced ten scattered
                // singles rather than a crack. A fissure has to read as a LINE or the sight-block
                // is just ten unexplained tiles you cannot shoot through.
                ang += (H(s, i, 220 + k) - 0.5f) * 0.55f;
                px += MathF.Cos(ang) * 1.05f; py += MathF.Sin(ang) * 1.05f;
                Reflect(grid, ref px, ref py, ref ang);
            }
        }
    }
}
