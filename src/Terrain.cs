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
    Rift,          // VOID    — P16: a hole in the floor. IMPASSABLE, but TRANSPARENT and giving NO cover
    Sand,          // ARID    — P16: soft sand. Crossing costs MORE than a step — the inverse of ice
}

/// The BIOME MECHANIC layer (wave C4 "EIGHT BIOMES ARE PAINT", extended by P16 "GROUND TRUTH").
///
/// FIVE biomes are mechanical, on FIVE DIFFERENT axes, and each one is the mechanic its own
/// art already implied:
///   VERDANT -> UNDERGROWTH  (the COVER axis)     "the ferns hide you from anything far away"
///   TUNDRA  -> SLICK ICE    (the MOVEMENT axis)  "the drift is a fast lane"
///   MAGMA   -> THERMAL VENTS(the SIGHT axis)     "you cannot see across the crack, and it burns"
///   VOID    -> RIFT         (the TOPOLOGY axis)  "you cannot cross the hole - but you can see and
///                                                 shoot clean across it"                    (P16)
///   ARID    -> SOFT SAND    (the DRAG axis)      "the basin drags: the exact inverse of ice" (P16)
/// The other three (STEEL / ASH / NEON) are still paint. That is declared, not hidden — see
/// docs/DEVLOG.md §C4 "what I did not do" and §GROUND TRUTH. `SIGHTLINE_BIOMETEST` asserts the
/// split in BOTH directions (a mechanical biome must stamp, a paint biome must not), so promoting
/// a sixth has to come here and restate it.
///
/// P16 — WHY THESE TWO, AND WHY ONLY ONE OF THEM HAS A GUARD. A RIFT is the first ground that
/// changes what the board IS rather than what a tile COSTS: it is the only shape on this board
/// that stops MOVEMENT without stopping SIGHT and without granting COVER, so it cuts open floor
/// into lanes and chokepoints while hiding nothing — the exact opposite of a MAGMA vent, which
/// hides everything and can still be walked through. That power is also the hazard: one
/// impassable tile in the wrong place cuts a spawn from the evac zone and makes a mission
/// unwinnable, which is far worse than a biome that is merely paint. So `StampRift` is the ONLY
/// stamper in this file that VALIDATES: each candidate tile is laid, the board is re-flooded
/// through `Grid.CostMap` — the same cost map both teams path with — and the tile is REVERTED
/// unless the reachable set shrank by exactly itself. A chasm therefore cannot seal, and the gaps
/// the guard refuses to fill ARE the bridges. They are the only gap source: unlike MAGMA's fords
/// there is no decorative gap roll, so every gap in a rift means "this is where you cross".
/// SOFT SAND needs no guard and no argument: it is one line in `Grid.CostMap` and nothing else.
///
/// SYMMETRY is structural, not promised: every rule lives in one of the three functions BOTH
/// sides already ask for the truth — `Grid.GetCover`, `Grid.CostMap`, `Grid.HasLineOfSight` —
/// so `Ai.cs` and `Combat.cs` cannot play the old game on the new board. Nothing here reads
/// `a.Team`.
///
/// DETERMINISM — the accurate statement, after the C4 review REFUTED the first one.
/// `Terrain.Stamp` itself is a pure function of (grid, biome, seed, mission, reserved) through
/// `Util.Hash3`: it takes ZERO draws from `Util.Rng` and uses no `System.Random`, which is the
/// property CRN pairing and `SIGHTLINE_PAIRTEST` actually need, and it is independently proven.
/// But the BOARD is NOT a function of (MapSeed, mission) alone: `Game.StampBiomeGround` passes a
/// `reserved` set built from unit and fixture positions, and those come out of `Util.Rng` inside
/// `Mission.Build`. Holding (MapSeed=424242, mission=3) fixed and varying only the ambient stream
/// produced 10-11 DISTINCT stamped boards. Nothing depends on the stronger claim — say the true
/// one: zero draws, regenerated per mission, exactly like the arena itself.
public static class Terrain
{
    /// Master gate (SIGHTLINE_BIOMEMECH=0 turns the whole layer off, for the A/B measurement and
    /// for the "a test that cannot fail is not a test" proof). Gated at the PREDICATES, not just
    /// at the stamper, so a hand-stamped harness board is inert too.
    public static bool Enabled = true;

    /// P16's OWN off switch (SIGHTLINE_NEWGROUND=0), and the arm P16's measurement round was
    /// actually run against. `Terrain.Enabled` (SIGHTLINE_BIOMEMECH=0) restores the pre-**C4**
    /// board — no ground layer at all — so an A/B on it would have priced C4 and P16 TOGETHER and
    /// called the sum P16's. This one restores the pre-**P16** board exactly: VERDANT / TUNDRA /
    /// MAGMA keep their mechanics, VOID and ARID go back to being paint. One lever per round
    /// (CLAUDE.md), which the coarser flag could not give.
    /// Gated at the PREDICATES as well as at the stamper, for the same reason Enabled is: a board
    /// a harness stamped by hand has to be inert too.
    public static bool NewGround = true;

    /// Both gates at once — the condition every RIFT and SAND rule reads.
    public static bool NewOn => Enabled && NewGround;

    // Indices into Biome.All (STEEL ARID TUNDRA VERDANT ASH VOID NEON MAGMA).
    public const int BiomeArid    = 1;
    public const int BiomeTundra  = 2;
    public const int BiomeVerdant = 3;
    public const int BiomeVoid    = 5;
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
    // NOT const: SIGHTLINE_BIOMETEST zeroes it to ISOLATE Ai.cs's own vent term from this toll
    // (the reviewer showed the composite leg passed with the Ai term at 0, at -1 and even INVERTED
    // to +200, because CostMap alone was carrying it). Gameplay never writes it.
    // BOUND, and it is load-bearing: an orthogonal vent step costs 2 + this = 8 half-tiles, which is
    // EXACTLY a full-mobility (4) soldier's single-action budget. At 7 a vent is uncrossable by
    // anyone; BIOMETEST asserts the literal 8, so raising it fails loudly instead of silently
    // turning the fissure into a wall. A WOUNDED soldier (budget 6) already cannot enter one --
    // that is a declared consequence, not an accident (DEVLOG C4 addendum).
    public static int VentStepExtra = 6;   // extra half-tiles added to the step onto a vent
    // static, not const, for the same reason VentStepExtra is: a const folds at compile time, so
    // BIOMETEST's literal pin on it became UNREACHABLE CODE (a 0-warning build caught that) and
    // could never have failed. A tuning number a test asserts must be readable at runtime.
    public static int VentBurnTurns = 2;   // Burning turns applied to whoever touches a vent

    // ---- P16 / ARID: SOFT SAND ---------------------------------------------------------
    // The exact inverse of ice, in the same units (Grid.CostMap is in HALF-tiles: orthogonal 2,
    // diagonal 3). Ice HALVES a step; sand costs HALF A STEP MORE — 2 -> 3 and 3 -> 5 (4.5,
    // rounded AGAINST the mover, which is the same direction ice's 1.5 -> 2 rounds; both round
    // away from the mover's advantage, so the pair is symmetric in spirit as well as in sign).
    // NOT const, for the reason VentStepExtra is not: a const folds at compile time, so
    // BIOMETEST's literal pin on it would become unreachable code and could never fail.
    //
    // BOUND, and it is load-bearing the way the vent's 8 is. A full-mobility (4) soldier's single
    // action is 8 half-tiles: at 3 that is TWO sand tiles crossed and 2 half-tiles left over, so
    // sand is always a price and never a wall. At 5 it would be one tile per action and sand would
    // BE terrain; BIOMETEST asserts the literal 3 and the >= 2-tiles-per-action invariant, so
    // raising it fails loudly instead of quietly turning a basin into a barrier.
    // ---- P16 / VOID: the RIFT's shape floor ---------------------------------------------
    // How hard the stamper works to put a REAL chasm on the board. A rift that shrinks to two
    // scattered tiles is not a weak mechanic, it is a rendering artefact the player has to route
    // around for no reason, so the walk repeats until the board carries a chasm or the tries run
    // out. BIOMETEST pins the resulting real-board minimum, which is the number that matters.
    public static int RiftFloor = 8;       // rift tiles that must survive the orphan cull
    public static int RiftTries = 8;       // max cracks walked to get there (2 is the usual answer)

    public static int SandStepOrth = 3;    // total half-tiles for an orthogonal step ONTO sand
    public static int SandStepDiag = 5;    // total half-tiles for a diagonal step ONTO sand

    /// The board-space tag shown beside the biome name in the mission banner ("VERDANT ·
    /// UNDERGROWTH"), or null for the three biomes that are still paint.
    public static string Tag(int biomeIndex) => !Enabled ? null : biomeIndex switch
    {
        BiomeVerdant => "UNDERGROWTH",
        BiomeTundra  => "SLICK ICE",
        BiomeMagma   => "THERMAL VENTS",
        BiomeVoid    => NewGround ? "RIFT" : null,
        BiomeArid    => NewGround ? "SOFT SAND" : null,
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
        BiomeVoid    => !NewGround ? null : "RIFT: nothing crosses the chasm - but sight and fire cross it freely, and it gives no cover. "
                        + "Find the bridge, or shoot across.",
        BiomeArid    => !NewGround ? null : "SOFT SAND: the basins drag. A step onto sand costs half a step MORE, so going around is "
                        + "often faster - for both sides.",
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
        // P16: ARID's basins get the biggest budget of any biome (sand is the mildest rule on the
        // board — a slow tile, no cover, no sight change — so it has to cover enough floor that
        // routing AROUND one is a real decision rather than a rounding error). VOID's gets the
        // smallest: a rift tile is the only ground that removes a tile from the board entirely, and
        // the guard below rejects a large share of the walk anyway.
        int budget = biomeIndex switch { BiomeVerdant => 44, BiomeTundra => 34, BiomeMagma => 24,
                                         BiomeArid => NewGround ? 46 : 0, BiomeVoid => NewGround ? 26 : 0,
                                         _ => 0 };
        bool Free(int x, int y)
        {
            if (budget <= 0) return false;
            if (!grid.IsFloor(x, y)) return false;                  // floor only; barrels excluded
            // C4 REVIEW (M1) — NEVER stamp onto RAISED terrain. `IsFloor` is `Tiles==Floor &&
            // !Barrel` and has never looked at `Height`, so the layer used to land on plateaus —
            // where `Renderer.DrawElevation` paints the plateau top with a FULLY OPAQUE rect,
            // offset by -lift, AFTER `DrawGround`. Fern and ice on a plateau therefore rendered as
            // literally ZERO pixels: a soldier could stand on raised undergrowth and take
            // omnidirectional low cover with NO mark on the board. That is the invisible
            // unfairness this whole wave exists to avoid, and it is worse than no mechanic.
            // Excluded at the SOURCE rather than repaired in the renderer, because a plateau is
            // already a distinct tactical surface with its own rule (high ground sees over low
            // cover) and stacking a second ground rule on it is muddier than keeping them apart.
            if (grid.HeightAt(x, y) > 0) return false;
            if (grid.Ground[x, y] != GroundKind.None) return false;
            if (reserved != null && reserved.Contains((x, y))) return false;
            return true;
        }
        void Put(int x, int y, GroundKind k) { if (Free(x, y)) { grid.Ground[x, y] = k; budget--; } }

        // ---- P16 / VOID: the RIFT's REACHABILITY GUARD --------------------------------------
        // The one stamper here that can make a mission UNWINNABLE, so it is the one stamper that
        // proves it did not. `Mission.TryApplyLayout` already refuses an arena whose cover cuts a
        // soldier off from the objective; a rift is laid AFTER that check has passed, so it needs
        // its own — and a stronger one, because a rift also has to survive things that appear LATER
        // than the stamp (DEFEND reinforcement waves, endless hordes, the pressure clock's
        // spawns, a shoved body, a planted evac beacon). Those pick their tile at spawn time, so
        // "the objectives I know about today are reachable" is not enough.
        //
        // The invariant is therefore the strongest one available and the cheapest to state:
        // THE RIFT MAY NOT DISCONNECT ANYTHING AT ALL. Every tile that was reachable from the
        // anchor before the stamp is still reachable after it, except the rift tiles themselves.
        // A candidate is laid, the board is re-flooded through Grid.CostMap — the SAME cost map
        // Ai.Plan, the move overlay, the VIP leash and every reachability probe already use, so
        // there is no second connectivity model to drift — and reverted unless the reachable set
        // shrank by exactly one. A cut vertex can never be laid; a pocket can never be sealed.
        //
        // Cost: one Dijkstra over 198 tiles per candidate, ~30 candidates per board, once per
        // mission. Measured in the noise beside SetupMission's own work.
        int anchorX = -1, anchorY = -1, reach = 0;
        if (biomeIndex == BiomeVoid)
        {
            // The flags are recomputed at the end of Stamp, but IsFloor has to see a rift tile as
            // non-floor WHILE we are laying them or the flood below is measuring the wrong board.
            grid.AnyRift = true;
            // The anchor must be a walkable tile the stamp can never take. Prefer a RESERVED one
            // (a unit's own tile — Free already excludes those); fall back to the first walkable
            // tile and exclude it explicitly. Scanned in grid order, never over the HashSet, so
            // the choice is deterministic by construction rather than by hash-order luck.
            for (int x = 0; x < grid.W && anchorX < 0; x++)
                for (int y = 0; y < grid.H && anchorX < 0; y++)
                    if (grid.IsFloor(x, y) && reserved != null && reserved.Contains((x, y))) { anchorX = x; anchorY = y; }
            for (int x = 0; x < grid.W && anchorX < 0; x++)
                for (int y = 0; y < grid.H && anchorX < 0; y++)
                    if (grid.IsFloor(x, y)) { anchorX = x; anchorY = y; }
            reach = anchorX >= 0 ? ReachCount(grid, anchorX, anchorY) : 0;
        }
        bool PutRift(int x, int y)
        {
            if (anchorX < 0) return false;
            if (x == anchorX && y == anchorY) return false;   // never eat the anchor
            if (!Free(x, y)) return false;
            grid.Ground[x, y] = GroundKind.Rift;
            int after = ReachCount(grid, anchorX, anchorY);
            // >= reach-1 rather than == reach-1 on purpose: a candidate that was ALREADY
            // unreachable (a pocket the pre-existing cover had sealed) leaves the count flat, and
            // removing an unreachable tile disconnects nothing. Anything that costs MORE than
            // itself is a cut and goes back.
            if (after < reach - 1) { grid.Ground[x, y] = GroundKind.None; return false; }
            reach = after; budget--;
            return true;
        }

        switch (biomeIndex)
        {
            case BiomeVerdant: StampPatches(grid, s, GroundKind.Undergrowth, Put); break;
            case BiomeTundra:  StampLanes(grid, s, GroundKind.Ice, Put);           break;
            case BiomeMagma:   StampFissure(grid, s, Put);                          break;
            case BiomeArid:    StampBasins(grid, s, Put);                           break;
            case BiomeVoid:    StampRift(grid, s, PutRift); CullOrphanRifts(grid);  break;
        }
        grid.RefreshGroundFlags();
    }

    /// How many tiles the shared cost map can reach from (ax,ay), the anchor included. The rift
    /// guard's whole measurement — deliberately routed through `Grid.CostMap` rather than a
    /// bespoke flood fill, so "reachable" means exactly what it means to `Ai.Plan` and to the
    /// player's move overlay (8-directional, no corner-cutting past a blocker, barrels and rifts
    /// excluded by `Grid.IsFloor`).
    static int ReachCount(Grid grid, int ax, int ay)
    {
        var cost = grid.CostMap(ax, ay, null, out _, 1 << 22);
        int n = 0;
        for (int x = 0; x < grid.W; x++)
            for (int y = 0; y < grid.H; y++) if (cost[x, y] >= 0) n++;
        return n;
    }

    /// VERDANT — 4..6 lobed fern patches, each a short walk of overlapping radius-1 discs.
    /// Deliberately BLOBBY: undergrowth is a place you stand IN, so it has to be several tiles
    /// wide or it is just a decorated tile.
    static void StampPatches(Grid grid, int s, GroundKind kind, Action<int, int, GroundKind> put)
    {
        // C4 REVIEW (M2) — TUNED AGAINST REAL BOARDS, NOT AN OPEN GRID. The first cut was tuned on
        // `OpenGrid()` (all floor, nothing reserved) and this comment claimed "~18-22% of the
        // 198-tile board". On real `SetupMission` boards — where cover, barrels, plateaus, units
        // and objective rings eat a large share of every patch — it SHIPPED at 12.1% (mean 23.99
        // tiles, measured over the wave's own 24 instrumented chunks), and a mission-6 capture
        // showed a "patch" that was five separate single tiles. That contradicted this file's own
        // rationale two lines down. Patch count and lobe count are both raised so a patch survives
        // the board it is actually stamped on.
        // MEASURED ON REAL BOARDS AFTER THE RE-TUNE (BIOMETEST prints these every sweep, so the
        // number in this comment can never drift from the shipped one again):
        //   VERDANT mean 35.0 tiles (19-44) = 17.7% of 198  |  TUNDRA 18.3 (10-34) = 9.2%
        //   MAGMA   mean 13.0 tiles (7-20)  =  6.6%
        int n = 4 + HI(s, 0, 11, 2);                       // 4..5 patches
        for (int i = 0; i < n; i++)
        {
            float px = 2f + H(s, i, 21) * (grid.W - 4f);
            float py = 1f + H(s, i, 22) * (grid.H - 2f);
            float ang = H(s, i, 23) * MathF.Tau;
            int lobes = 3 + HI(s, i, 24, 2);               // 3..4 lobes
            for (int k = 0; k < lobes; k++)
            {
                int cx = (int)MathF.Round(px), cy = (int)MathF.Round(py);
                int r = 1 + (H(s, i, 40 + k) < 0.34f ? 1 : 0);   // radius 1, often 2 (a patch has to
                                                                 // survive the cover on a real board)
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

    /// ARID — 3..4 broad SAND BASINS: overlapping radius-2 discs walked a short way, so a basin is
    /// a wide soft bowl rather than a lane. Deliberately the opposite SHAPE from TUNDRA's drift as
    /// well as the opposite SIGN: a drift is a line you ride ALONG, a basin is an area you route
    /// AROUND, and if sand came in lanes the two biomes would read as the same board painted twice.
    /// It is also the widest ground on the board, because sand is the mildest rule on it — a basin
    /// nobody has to detour around is a rule nobody meets.
    static void StampBasins(Grid grid, int s, Action<int, int, GroundKind> put)
    {
        int n = 3 + HI(s, 0, 13, 2);                       // 3..4 basins
        for (int i = 0; i < n; i++)
        {
            float px = 2f + H(s, i, 51) * (grid.W - 4f);
            float py = 1f + H(s, i, 52) * (grid.H - 2f);
            float ang = H(s, i, 53) * MathF.Tau;
            const int lobes = 2;                           // two overlapping discs = one oval bowl
            for (int k = 0; k < lobes; k++)
            {
                int cx = (int)MathF.Round(px), cy = (int)MathF.Round(py);
                for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                        if (dx * dx + dy * dy <= 4) put(cx + dx, cy + dy, GroundKind.Sand);  // a rounded disc, not a diamond
                ang += (H(s, i, 240 + k) - 0.5f) * 1.4f;
                float step = 1.7f + H(s, i, 260 + k) * 1.3f;
                px += MathF.Cos(ang) * step; py += MathF.Sin(ang) * step;
                Reflect(grid, ref px, ref py, ref ang);
            }
        }
    }

    /// VOID — 2 RIFTS: long, low-wander cracks biased toward vertical, so a chasm tends to divide
    /// the board across the squad's left-to-right axis and the run has to commit to a side.
    ///
    /// WIDTH 1 AND NO DELIBERATE GAPS — both are deliberate, and both are the opposite of MAGMA.
    /// A fissure needs decorative gaps because a continuous 1-wide crack would be a total SIGHT
    /// barrier; a rift blocks no sight at all, so it needs none, and every gap in it is one the
    /// reachability guard REFUSED to fill. That makes a gap mean something exact — "this is the
    /// bridge" — instead of meaning "the walker rolled a gap here". Width stays 1 because a
    /// 2-wide impassable band is not twice as interesting, it is twice as likely to be a wall.
    static void StampRift(Grid grid, int s, Func<int, int, bool> put)
    {
        // FIRST CAPTURE, AND WHAT IT CHANGED. The first cut walked like MAGMA's fissure (a free
        // start anywhere on the board, wander 0.50, 15-19 steps) and the screenshots showed exactly
        // the failure C4's review recorded against MAGMA's first cut: SIX ISOLATED BLACK SQUARES,
        // not a chasm. The cause is the board, not the walker — cover, barrels and the reserved
        // rings eat ~60% of a walk, and once a meandering line is gapped that hard, nothing about
        // the survivors says they were ever one line. Three changes, all aimed at that:
        //   (a) EDGE-ANCHORED. A crack starts ON a board edge and heads across, so it spans the
        //       short axis instead of wandering in the middle of the room.
        //   (b) NEARLY STRAIGHT (wander 0.22 against the fissure's 0.55). A straight line still
        //       reads as a line when a third of it is missing; a meander does not.
        //   (c) NO ORPHANS. Stamp.CullOrphanRifts drops any rift tile with no rift neighbour at
        //       all, because a lone 1-tile hole is confetti: it neither reads as a chasm nor asks
        //       anything of a player, who simply steps around it.
        // (d) A FLOOR, MET BY WALKING AGAIN — never by relaxing the guard. Two cracks is the
        // usual answer, but an edge-anchored straight line that happens to start against a wall of
        // cover can lay almost nothing, and the orphan cull then takes the rest: the first version
        // of this measured realMin = 0 over 40 real boards, which is precisely the thin tail
        // CLAUDE.md records against MAGMA ("about 1 board in 240") and tells the next wave not to
        // reproduce. So the stamper keeps walking — up to RiftTries cracks — until the board
        // carries RiftFloor tiles that will SURVIVE the cull. Deterministic (each attempt is its
        // own salt), bounded, and it cannot overrun: `budget` still stops it.
        int n = RiftTries;
        for (int i = 0; i < n; i++)
        {
            if (i >= 2 && ConnectedRiftCount(grid) >= RiftFloor) break;
            // (a) start on the TOP or BOTTOM edge and head into the board. The short axis is 11
            // tiles, so a crack that crosses it genuinely divides the room left from right — the
            // axis the squad advances along, which is what makes the bridge a decision.
            bool fromTop = H(s, i, 63) < 0.5f;
            float px = 2f + H(s, i, 61) * (grid.W - 4f);
            float py = fromTop ? 0f : grid.H - 1f;
            float ang = (fromTop ? MathF.PI * 0.5f : -MathF.PI * 0.5f)
                        + (H(s, i, 64) - 0.5f) * 0.9f;     // a slanted crack, never a scribble
            int len = 15 + HI(s, i, 65, 5);                // 15..19 steps each
            for (int k = 0; k < len; k++)
            {
                put((int)MathF.Round(px), (int)MathF.Round(py));
                ang += (H(s, i, 280 + k) - 0.5f) * 0.22f;  // (b)
                px += MathF.Cos(ang) * 1.05f; py += MathF.Sin(ang) * 1.05f;
                Reflect(grid, ref px, ref py, ref ang);
            }
        }
    }

    /// How many rift tiles would SURVIVE CullOrphanRifts — i.e. have at least one rift neighbour.
    /// Non-destructive on purpose: culling between attempts would delete tiles the next crack was
    /// about to adjoin, so the walk counts what it would keep and only culls once, at the end.
    static int ConnectedRiftCount(Grid grid)
    {
        int n = 0;
        for (int x = 0; x < grid.W; x++)
            for (int y = 0; y < grid.H; y++)
            {
                if (grid.Ground[x, y] != GroundKind.Rift) continue;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int ax = x + dx, ay = y + dy;
                        if (grid.InBounds(ax, ay) && grid.Ground[ax, ay] == GroundKind.Rift)
                        { n++; dx = 2; break; }
                    }
            }
        return n;
    }

    /// (c) Drop every rift tile that has no rift neighbour in any of the eight directions. Removing
    /// an impassable tile can only ADD reachability, so this cannot break the guard's invariant —
    /// which is why it is safe to run after the walk rather than inside it.
    static void CullOrphanRifts(Grid grid)
    {
        var doomed = new List<(int x, int y)>();
        for (int x = 0; x < grid.W; x++)
            for (int y = 0; y < grid.H; y++)
            {
                if (grid.Ground[x, y] != GroundKind.Rift) continue;
                bool friend = false;
                for (int dx = -1; dx <= 1 && !friend; dx++)
                    for (int dy = -1; dy <= 1 && !friend; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (grid.InBounds(nx, ny) && grid.Ground[nx, ny] == GroundKind.Rift) friend = true;
                    }
                if (!friend) doomed.Add((x, y));
            }
        foreach (var (x, y) in doomed) grid.Ground[x, y] = GroundKind.None;
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
            int len = 13 + HI(s, i, 45, 5);                // 13..17 steps each (real boards eat ~a third)
            for (int k = 0; k < len; k++)
            {
                int cx = (int)MathF.Round(px), cy = (int)MathF.Round(py);
                // C4 REVIEW (M2): DELIBERATE fords cut to ~8%. On an open grid 18% read as a line
                // with gaps; on a REAL board cover, barrels and reserved rings already eat a large
                // share of the walk, and the two gap sources COMPOUNDED — measured mean 9.66 vents
                // over two walkers, i.e. ~4.8 per crack, which on some seeds is eight isolated
                // singles rather than a fissure. The involuntary gaps ARE the fords now; the
                // deliberate ones only stop the crack becoming a perfect wall.
                if (H(s, i, 200 + k) >= 0.08f) put(cx, cy, GroundKind.Vent);
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
