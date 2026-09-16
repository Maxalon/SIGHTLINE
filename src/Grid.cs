using System;
using System.Collections.Generic;

namespace Sightline;

public enum TileType { Floor, HighCover, LowCover }

/// The battlefield: tile types, line-of-sight, cover queries, pathfinding.
public class Grid
{
    public readonly int W = Cfg.GridW;
    public readonly int H = Cfg.GridH;
    public TileType[,] Tiles;
    public int[,] Height;       // elevation layer: 0 = ground, 1 = high ground
    public int[,] Smoke;        // utility-item smoke: turns remaining a tile blocks sight (3.4)
    public int[,] CoverHp;      // hits a cover tile takes before degrading High->Low->gone (3.6)
    public int[,] CoverSeed;    // PURELY VISUAL: stable per-tile identity of the drawn cover VOLUME
    public int[,] Fire;         // environmental fire: turns remaining a tile burns (hazards)
    public bool[,] Barrel;      // explosive barrel present on a tile (hazards)
    // C4 "EIGHT BIOMES ARE PAINT": what a floor tile is MADE OF (see src/Terrain.cs). Stamped
    // once per mission via Util.Hash3 — ZERO Util.Rng draws (the property CRN needs), though the
    // board is keyed on the reserved set too, not on (MapSeed, mission) alone — and read
    // by the three functions BOTH teams already ask for the truth: GetCover, CostMap and
    // HasLineOfSight. NOT persisted (SaveGame never serialises a Grid).
    public GroundKind[,] Ground;

    // ── P28: THE EDGE LAYER. Two arrays, not one per tile: a boundary is shared, so it gets
    //    exactly one home. EdgeV[x,y] is the edge on the WEST side of tile (x,y) (so x runs
    //    0..W inclusive); EdgeH[x,y] is the edge on its NORTH side (y runs 0..H inclusive).
    public EdgeKind[,] EdgeV;      // [W+1, H]
    public EdgeKind[,] EdgeH;      // [W, H+1]
    /// Fast-out: true only once something has actually placed an edge. Every predicate below
    /// checks it first, so a board with no walls pays nothing for the layer existing.
    public bool AnyEdges;
    // Fast "does this board have any at all" flags, so the hot paths (HasLineOfSight is called
    // W*H*foes times per threat rebuild) pay one static bool + one field read on a normal board.
    public bool AnyFoliage, AnyIce, AnyVent, AnyRift, AnySand;

    public const int HighCoverHp = 2;   // chips to crack High -> Low
    public const int LowCoverHp = 1;    // chips to clear Low -> Floor
    public const int FireTurns = 3;     // how long a freshly-lit tile burns before guttering out

    public Grid()
    {
        Tiles = new TileType[W, H];
        Height = new int[W, H];
        Smoke = new int[W, H];
        CoverHp = new int[W, H];
        CoverSeed = new int[W, H];
        Fire = new int[W, H];
        Barrel = new bool[W, H];
        Ground = new GroundKind[W, H];
        EdgeV = new EdgeKind[W + 1, H];
        EdgeH = new EdgeKind[W, H + 1];
        ClearCoverSeeds();
    }

    // ---------- C4: the biome GROUND layer ----------
    // Every predicate gates on Terrain.Enabled as well as the tile, so SIGHTLINE_BIOMEMECH=0
    // makes the whole layer inert even on a board a harness stamped by hand.
    public GroundKind GroundAt(int x, int y) =>
        Terrain.Enabled && InBounds(x, y) ? Ground[x, y] : GroundKind.None;
    /// VERDANT fern: low cover from every angle, but only against fire from beyond
    /// Terrain.FoliageMinDist (the rule itself lives in GetCover, the one truth both teams read).
    public bool IsFoliage(int x, int y) =>
        Terrain.Enabled && AnyFoliage && InBounds(x, y) && Ground[x, y] == GroundKind.Undergrowth;
    /// TUNDRA drift: half-price to step onto (see CostMap).
    public bool IsIce(int x, int y) =>
        Terrain.Enabled && AnyIce && InBounds(x, y) && Ground[x, y] == GroundKind.Ice;
    /// MAGMA fissure: opaque, dear to cross, and it sets you alight.
    public bool IsVent(int x, int y) =>
        Terrain.Enabled && AnyVent && InBounds(x, y) && Ground[x, y] == GroundKind.Vent;
    /// P16 / VOID chasm: IMPASSABLE (see IsFloor, the one chokepoint every mover already asks),
    /// but TRANSPARENT and giving NO cover — so it is the only blocker on this board that hides
    /// nothing. Deliberately NOT wired into BlocksSight, IsVapor or GetCover: those three
    /// omissions ARE the mechanic, and BIOMETEST asserts each of them.
    public bool IsRift(int x, int y) =>
        Terrain.NewOn && AnyRift && InBounds(x, y) && Ground[x, y] == GroundKind.Rift;
    /// P16 / ARID soft sand: costs half a step MORE to enter (see CostMap). The exact inverse of
    /// ice, and — like ice — a MOVEMENT rule only: no cover, no sight change, no hazard.
    public bool IsSand(int x, int y) =>
        Terrain.NewOn && AnySand && InBounds(x, y) && Ground[x, y] == GroundKind.Sand;

    public void ClearGround()
    {
        Array.Clear(Ground, 0, Ground.Length);
        AnyFoliage = AnyIce = AnyVent = AnyRift = AnySand = false;
    }

    /// Recompute the five "board has any" flags after a stamp.
    public void RefreshGroundFlags()
    {
        AnyFoliage = AnyIce = AnyVent = AnyRift = AnySand = false;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                switch (Ground[x, y])
                {
                    case GroundKind.Undergrowth: AnyFoliage = true; break;
                    case GroundKind.Ice:         AnyIce = true;     break;
                    case GroundKind.Vent:        AnyVent = true;    break;
                    case GroundKind.Rift:        AnyRift = true;    break;
                    case GroundKind.Sand:        AnySand = true;    break;
                }
    }

    // ---- PURELY VISUAL: stable cover-volume identity (W4 review fix) -------------------------
    // src/Renderer.cs draws 4-connected same-type cover as ONE merged volume and hashes the
    // volume's MATERIAL FORM and its footprint jitter off the volume's identity. That identity
    // used to be recomputed from the LIVE tile set every frame (union-find, min linear index as
    // root), which meant destroying or downgrading a wall's north/west-most tile moved the root
    // and RE-ROLLED the material for every surviving tile — a wall visibly turned from crates
    // into rock mid-mission, reading as a rendering glitch rather than as damage. Grenades,
    // barrels and sustained fire all triggered it.
    //
    // The identity is therefore assigned ONCE, when a cover tile first exists, and never
    // re-derived from the live set. A tile keeps its seed for as long as it holds cover; a tile
    // destroyed to Floor drops its seed so a barricade later deployed there starts a new volume.
    // Two adjacent tiles draw as one volume only when type, elevation tier AND seed all match,
    // so a High tile downgraded to rubble splits off from its run instead of dragging a
    // mismatched jitter into it.
    //
    // Gameplay-inert by construction: nothing outside Renderer.cs reads CoverSeed, it takes no
    // draws from Util.Rng, and it is derived from the tile layout alone.
    public const int NoSeed = -1;
    const int Pending = -2;        // in-flight marker used only inside SeedCoverVolumes

    public void ClearCoverSeeds()
    {
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++) CoverSeed[x, y] = NoSeed;
    }

    /// Give every cover tile that does not have one yet a volume seed: the minimum linear index
    /// of the 4-connected run of same-type, same-elevation, still-unseeded cover it belongs to.
    /// Idempotent and cheap (one scan; work only where seeds are missing), so the renderer can
    /// call it every frame and no stamping site has to remember to.
    public void SeedCoverVolumes()
    {
        var run = new List<int>(32);
        var frontier = new List<int>(32);
        for (int y0 = 0; y0 < H; y0++)                 // scan in linear-index order, so the first
            for (int x0 = 0; x0 < W; x0++)             // tile of a run IS the run's minimum index
            {
                if (Tiles[x0, y0] == TileType.Floor || CoverSeed[x0, y0] != NoSeed) continue;
                var tt = Tiles[x0, y0]; int hh = Height[x0, y0];
                int seed = y0 * W + x0;
                int adopt = int.MaxValue;
                run.Clear(); frontier.Clear();
                CoverSeed[x0, y0] = Pending;           // marked, so the walk terminates — and so a
                run.Add(seed); frontier.Add(seed);     // tile of THIS run is never mistaken for an
                                                       // already-standing volume to adopt from
                while (frontier.Count > 0)
                {
                    int cur = frontier[frontier.Count - 1]; frontier.RemoveAt(frontier.Count - 1);
                    int cx = cur % W, cy = cur / W;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = cx + (d == 0 ? -1 : d == 1 ? 1 : 0);
                        int ny = cy + (d == 2 ? -1 : d == 3 ? 1 : 0);
                        if (!InBounds(nx, ny)) continue;
                        if (Tiles[nx, ny] != tt || Height[nx, ny] != hh) continue;
                        int ns = CoverSeed[nx, ny];
                        if (ns == Pending) continue;   // already walked, this run
                        if (ns == NoSeed)
                        {
                            CoverSeed[nx, ny] = Pending;
                            run.Add(ny * W + nx); frontier.Add(ny * W + nx);
                            continue;
                        }
                        // ADOPT: a tile that appears mid-mission JOINS the volume it is touching
                        // (a barricade deployed against a wall is part of that wall; a tile rebuilt
                        // where one was destroyed rejoins its run WITH THE RUN'S OWN MATERIAL). The
                        // direction matters: the new tiles take the standing volume's identity, so
                        // the standing volume is never relabelled and can never re-roll its
                        // material because something was built next to it. At mission build nothing
                        // is seeded yet, so this never fires and the partition is exactly the
                        // 4-connected same-type/same-tier grouping, rooted on the same minimum
                        // linear index, that the renderer used to rebuild every frame.
                        if (ns < adopt) adopt = ns;
                    }
                }
                int final = adopt != int.MaxValue ? adopt : seed;
                foreach (int i in run) CoverSeed[i % W, i / W] = final;
            }
    }

    public enum CoverHit { None, Chipped, Downgraded, Destroyed }

    /// Full HP for a tile's CURRENT cover level (0 for floor).
    public int MaxCoverHp(int x, int y) => !InBounds(x, y) ? 0 :
        (Tiles[x, y] == TileType.HighCover ? HighCoverHp : (Tiles[x, y] == TileType.LowCover ? LowCoverHp : 0));

    /// Charge a freshly-placed cover tile to full HP (e.g. a deployed barricade).
    public void SetCoverHp(int x, int y) { if (InBounds(x, y)) CoverHp[x, y] = MaxCoverHp(x, y); }

    /// (Re)initialise HP for every cover tile — call once a mission's terrain is final.
    /// One Game owns ONE Grid for its whole life (Game.Grid is a field initialiser), so the
    /// visual volume seeds are re-derived here too: a mission-2 wall standing where a mission-1
    /// wall stood would otherwise inherit mission 1's identity.
    public void ResetCoverHp()
    {
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++) CoverHp[x, y] = MaxCoverHp(x, y);
        ResetEdgeHp();          // P40: walls charge with the blocks, by the same call
        ClearCoverSeeds();
        SeedCoverVolumes();
    }

    /// Apply `dmg` to a cover tile, degrading High->Low->Floor as its HP runs out.
    public CoverHit DamageCover(int x, int y, int dmg)
    {
        if (!IsCover(x, y) || dmg <= 0) return CoverHit.None;
        CoverHp[x, y] -= dmg;
        if (CoverHp[x, y] > 0) return CoverHit.Chipped;
        if (Tiles[x, y] == TileType.HighCover)
        {
            Tiles[x, y] = TileType.LowCover;
            CoverHp[x, y] = LowCoverHp;            // the rubble still gives low cover
            return CoverHit.Downgraded;
        }
        Tiles[x, y] = TileType.Floor;
        CoverHp[x, y] = 0;
        CoverSeed[x, y] = NoSeed;               // the volume identity dies with the tile (visual only)
        return CoverHit.Destroyed;
    }

    /// The cover tile shielding (tx,ty) from fire at (fx,fy), or null — mirrors GetCover's
    /// dominant-side pick so heavy fire chips the right frontal block.
    public (int x, int y)? CoverTile(int tx, int ty, int fx, int fy)
    {
        int dx = fx - tx, dy = fy - ty;
        bool diagonal = dx != 0 && dy != 0 && Math.Abs(dx) == Math.Abs(dy);
        (int, int)? pick = null; int best = 0;
        void Consider(int sx, int sy)
        {
            int nx = tx + sx, ny = ty + sy;
            if (!InBounds(nx, ny)) return;
            int lv = Tiles[nx, ny] == TileType.HighCover ? 2 : (Tiles[nx, ny] == TileType.LowCover ? 1 : 0);
            if (lv > best) { best = lv; pick = (nx, ny); }
        }
        if (diagonal) { Consider(Util.Sign(dx), 0); Consider(0, Util.Sign(dy)); }
        else if (Math.Abs(dx) >= Math.Abs(dy) && dx != 0) Consider(Util.Sign(dx), 0);
        else if (dy != 0) Consider(0, Util.Sign(dy));
        return pick;
    }

    // ---------- P28: the EDGE layer ----------
    // Every accessor gates on Edges.Enabled AND AnyEdges, so SIGHTLINE_EDGES=0 restores the
    // pre-P28 board exactly and a board with no walls never pays for the check.
    public EdgeKind EdgeVAt(int x, int y) =>
        (!Edges.Enabled || !AnyEdges || x < 0 || x > W || y < 0 || y >= H) ? EdgeKind.None : EdgeV[x, y];
    public EdgeKind EdgeHAt(int x, int y) =>
        (!Edges.Enabled || !AnyEdges || x < 0 || x >= W || y < 0 || y > H) ? EdgeKind.None : EdgeH[x, y];

    /// The edge between two ORTHOGONALLY adjacent tiles. A diagonal pair shares no single edge,
    /// so it answers None by design — callers must decompose a diagonal into its two L-routes
    /// (CostMap and HasLineOfSight both do). Returning None there rather than guessing is what
    /// keeps "can I cut this corner" a decision made in one place.
    public EdgeKind EdgeBetween(int x0, int y0, int x1, int y1)
    {
        int dx = x1 - x0, dy = y1 - y0;
        if (dy == 0 && dx ==  1) return EdgeVAt(x1, y0);   // west face of the tile we ENTER
        if (dy == 0 && dx == -1) return EdgeVAt(x0, y0);   // west face of the tile we LEAVE
        if (dx == 0 && dy ==  1) return EdgeHAt(x0, y1);   // north face of the tile we ENTER
        if (dx == 0 && dy == -1) return EdgeHAt(x0, y0);   // north face of the tile we LEAVE
        return EdgeKind.None;
    }

    /// WHICH boundary lies between two adjacent tiles, as an addressable thing rather than as a
    /// kind. `EdgeBetween` answers "what is there"; a sapper needs "what do I hit", and those are
    /// different questions the moment anything can damage a wall.
    public readonly struct EdgeRef
    {
        public readonly bool Vertical;
        public readonly int X, Y;
        public EdgeRef(bool vertical, int x, int y) { Vertical = vertical; X = x; Y = y; }
    }

    public EdgeRef? EdgeRefBetween(int x0, int y0, int x1, int y1)
    {
        int dx = x1 - x0, dy = y1 - y0;
        if (dy == 0 && dx ==  1) return new EdgeRef(true, x1, y0);
        if (dy == 0 && dx == -1) return new EdgeRef(true, x0, y0);
        if (dx == 0 && dy ==  1) return new EdgeRef(false, x0, y1);
        if (dx == 0 && dy == -1) return new EdgeRef(false, x0, y0);
        return null;
    }

    public EdgeKind KindOf(EdgeRef e) => e.Vertical ? EdgeVAt(e.X, e.Y) : EdgeHAt(e.X, e.Y);

    // ══ P40: DESTRUCTIBLE EDGES ══════════════════════════════════════════════════════════════
    // Cover moved from TILES to EDGES in P28 and the damage model did not follow it, which is the
    // whole reason buildings shipped behind a flag: `Ai`'s sapper destroys a cover TILE, and a
    // soldier sheltering behind a building wall could not be sapped at all. These arrays are the
    // edge half of `CoverHp`, deliberately built to the same shape and the same two constants, so
    // a wall degrades exactly the way a block does — High -> Low -> gone.
    public int[,] EdgeVHp, EdgeHHp;

    /// Full HP for an edge's CURRENT kind. A DOOR has none: it is already a hole, and "breaching a
    /// doorway" is not a thing a sapper needs to spend an action on.
    public int MaxEdgeHp(EdgeKind k) =>
        k == EdgeKind.High ? HighCoverHp : (k == EdgeKind.Low ? LowCoverHp : 0);

    /// (Re)charge every edge. Called from ResetCoverHp, so a mission's walls and its blocks are
    /// charged by the same call and neither can be forgotten independently of the other.
    public void ResetEdgeHp()
    {
        if (EdgeVHp == null) { EdgeVHp = new int[W + 1, H]; EdgeHHp = new int[W, H + 1]; }
        for (int x = 0; x <= W; x++) for (int y = 0; y < H; y++) EdgeVHp[x, y] = MaxEdgeHp(EdgeV[x, y]);
        for (int x = 0; x < W; x++) for (int y = 0; y <= H; y++) EdgeHHp[x, y] = MaxEdgeHp(EdgeH[x, y]);
    }

    /// Apply `dmg` to one boundary, degrading High -> Low -> None. Returns what happened, in the
    /// same vocabulary `DamageCover` uses, so a caller that already knows how to react to a chipped
    /// or downgraded block needs no new branch for a wall.
    ///
    /// **A DESTROYED WALL OPENS A ROUTE.** `Grid.CostMap` and `HasLineOfSight` both read the edge
    /// layer, so this is the one damage call on this board that changes the shape of the map
    /// rather than the cost of standing somewhere. That is the point of a breach — and it is why
    /// `Edges.Destructible` exists to switch it off whole.
    public CoverHit DamageEdge(EdgeRef e, int dmg)
    {
        if (!Edges.Enabled || !Edges.Destructible || dmg <= 0) return CoverHit.None;
        if (EdgeVHp == null) ResetEdgeHp();
        bool v = e.Vertical;
        if (v ? (e.X < 0 || e.X > W || e.Y < 0 || e.Y >= H) : (e.X < 0 || e.X >= W || e.Y < 0 || e.Y > H))
            return CoverHit.None;

        EdgeKind k = v ? EdgeV[e.X, e.Y] : EdgeH[e.X, e.Y];
        if (k != EdgeKind.High && k != EdgeKind.Low) return CoverHit.None;   // None and Door take none

        int hp = (v ? EdgeVHp[e.X, e.Y] : EdgeHHp[e.X, e.Y]) - dmg;
        if (hp > 0) { if (v) EdgeVHp[e.X, e.Y] = hp; else EdgeHHp[e.X, e.Y] = hp; return CoverHit.Chipped; }

        if (k == EdgeKind.High)
        {
            if (v) { EdgeV[e.X, e.Y] = EdgeKind.Low; EdgeVHp[e.X, e.Y] = LowCoverHp; }
            else   { EdgeH[e.X, e.Y] = EdgeKind.Low; EdgeHHp[e.X, e.Y] = LowCoverHp; }
            return CoverHit.Downgraded;
        }
        if (v) { EdgeV[e.X, e.Y] = EdgeKind.None; EdgeVHp[e.X, e.Y] = 0; }
        else   { EdgeH[e.X, e.Y] = EdgeKind.None; EdgeHHp[e.X, e.Y] = 0; }
        return CoverHit.Destroyed;
    }

    /// The EDGE shielding (tx,ty) from fire at (fx,fy), or null. Deliberately the same
    /// dominant-side pick as `CoverTile`, on the same sides, so the sapper breaches the boundary
    /// the shooter is actually being stopped by rather than the nearest wall.
    public EdgeRef? CoverEdge(int tx, int ty, int fx, int fy)
    {
        int dx = fx - tx, dy = fy - ty;
        bool diagonal = dx != 0 && dy != 0 && Math.Abs(dx) == Math.Abs(dy);
        EdgeRef? pick = null; int best = 0;
        void Consider(int sx, int sy)
        {
            var er = EdgeRefBetween(tx, ty, tx + sx, ty + sy);
            if (er == null) return;
            var k = KindOf(er.Value);
            int lv = Edges.CoverLevel(k);
            if (lv > best) { best = lv; pick = er; }
        }
        if (diagonal) { Consider(Util.Sign(dx), 0); Consider(0, Util.Sign(dy)); }
        else if (Math.Abs(dx) >= Math.Abs(dy) && dx != 0) Consider(Util.Sign(dx), 0);
        else if (dy != 0) Consider(0, Util.Sign(dy));
        return pick;
    }

    public bool EdgeStopsMove(int x0, int y0, int x1, int y1) => Edges.BlocksMove(EdgeBetween(x0, y0, x1, y1));
    public bool EdgeStopsSight(int x0, int y0, int x1, int y1) => Edges.BlocksSight(EdgeBetween(x0, y0, x1, y1));

    public void SetEdgeV(int x, int y, EdgeKind k)
    { if (x >= 0 && x <= W && y >= 0 && y < H) { EdgeV[x, y] = k; if (k != EdgeKind.None) AnyEdges = true; } }
    public void SetEdgeH(int x, int y, EdgeKind k)
    { if (x >= 0 && x < W && y >= 0 && y <= H) { EdgeH[x, y] = k; if (k != EdgeKind.None) AnyEdges = true; } }

    /// P20/P21's rule extended: `Mission.Build` owns EVERY per-tile layer, and the edge layer is
    /// no exception. A stale wall from the previous mission would move this board exactly as the
    /// stale GROUND layer did, and through the same predicates.
    public void ClearEdges()
    {
        Array.Clear(EdgeV, 0, EdgeV.Length);
        Array.Clear(EdgeH, 0, EdgeH.Length);
        AnyEdges = false;
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
    public TileType At(int x, int y) => Tiles[x, y];
    public bool IsCover(int x, int y) => InBounds(x, y) && Tiles[x, y] != TileType.Floor;
    // High cover OR an opaque VAPOUR (smoke cloud / MAGMA steam vent) blocks line of sight
    // (and overwatch) through a tile.
    public bool BlocksSight(int x, int y) =>
        InBounds(x, y) && (Tiles[x, y] == TileType.HighCover || IsVapor(x, y));
    public bool IsSmoke(int x, int y) => InBounds(x, y) && Smoke[x, y] > 0;
    /// Anything OPAQUE that a commanding (tier-2) shooter cannot see over either: a smoke cloud
    /// or a MAGMA thermal vent's steam column. C4: the vent joins smoke here rather than joining
    /// HighCover, because a fissure is a screen, not a wall — it grants no cover, it just blinds.
    public bool IsVapor(int x, int y) =>
        InBounds(x, y) && (Smoke[x, y] > 0 || (Terrain.Enabled && AnyVent && Ground[x, y] == GroundKind.Vent));

    /// Reset all smoke (called at mission build).
    public void ClearSmoke() { Array.Clear(Smoke, 0, Smoke.Length); }

    /// Decay every smoke cloud by one turn (called once per player turn).
    public void TickSmoke()
    {
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (Smoke[x, y] > 0) Smoke[x, y]--;
    }

    /// Lay a smoke cloud of `turns` over a Chebyshev `radius` around (cx,cy).
    public void AddSmoke(int cx, int cy, int radius, int turns)
    {
        for (int x = cx - radius; x <= cx + radius; x++)
            for (int y = cy - radius; y <= cy + radius; y++)
                if (InBounds(x, y)) Smoke[x, y] = Math.Max(Smoke[x, y], turns);
    }

    // ---------- Hazards: fire + explosive barrels ----------
    public bool IsFire(int x, int y) => InBounds(x, y) && Fire[x, y] > 0;
    public bool IsBarrel(int x, int y) => InBounds(x, y) && Barrel[x, y];

    /// Reset all hazards (called at mission build, before barrels are stamped).
    public void ClearHazards() { Array.Clear(Fire, 0, Fire.Length); Array.Clear(Barrel, 0, Barrel.Length); }

    /// Light a single tile on fire for `turns` (only floor tiles burn — cover/barrels handle
    /// their own destruction). Never overrides a longer-burning tile down.
    public void LightFire(int x, int y, int turns)
    {
        // P16: routed through IsFloor rather than restating its clauses, so a VOID rift — a tile
        // no unit can occupy — cannot be set alight either. Identical to the old predicate
        // (InBounds && Tiles==Floor && !Barrel) on every board without a rift on it.
        if (IsFloor(x, y)) Fire[x, y] = Math.Max(Fire[x, y], turns);
    }

    /// Lay fire over a Chebyshev `radius` of floor tiles around (cx,cy).
    public void AddFire(int cx, int cy, int radius, int turns)
    {
        for (int x = cx - radius; x <= cx + radius; x++)
            for (int y = cy - radius; y <= cy + radius; y++)
                LightFire(x, y, turns);
    }

    /// Decay every burning tile by one turn (called once per round). Returns the count still lit.
    public int TickFire()
    {
        int lit = 0;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (Fire[x, y] > 0) { Fire[x, y]--; if (Fire[x, y] > 0) lit++; }
        return lit;
    }

    /// Terrain elevation at a tile (0 ground, 1 high ground). High ground grants
    /// an aim/crit edge when firing down on a lower target.
    public int HeightAt(int x, int y) => InBounds(x, y) ? Height[x, y] : 0;
    public bool IsHigh(int x, int y) => HeightAt(x, y) > 0;

    /// A tile a unit can stand on (floor + in bounds, not occupied by an explosive barrel, and
    /// not a VOID rift). Barrels are physical obstacles, so routing this through IsFloor makes
    /// them impassable everywhere (pathing/CostMap, deployment, shove/extract destinations) via
    /// one chokepoint.
    ///
    /// P16: the RIFT joins them HERE and nowhere else. That single word is the whole impassability
    /// mechanic — pathing, the move overlay, Ai.Plan's candidate set, deployment, shove and extract
    /// destinations, reinforcement and horde spawn seats, LightFire and PlaceIntelCache all ask
    /// this one predicate, so there is no second passability model for the opponent to be missing.
    /// The rift is deliberately absent from BlocksSight / IsVapor / GetCover: you shoot straight
    /// across a hole and it shelters nobody.
    public bool IsFloor(int x, int y) => InBounds(x, y) && Tiles[x, y] == TileType.Floor && !Barrel[x, y]
                                         && !(Terrain.NewOn && AnyRift && Ground[x, y] == GroundKind.Rift);

    // ---------- Line of sight ----------
    // Supercover line between tile centres; blocked by any intermediate HighCover tile
    // (or smoke). `overHighCover` lets a commanding (tier-2) shooter see over high
    // cover — smoke still blocks either way.
    public bool HasLineOfSight(int x0, int y0, int x1, int y1) => HasLineOfSight(x0, y0, x1, y1, false);
    public bool HasLineOfSight(int x0, int y0, int x1, int y1, bool overHighCover)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int cx = x0, cy = y0;
        int guard = 0;
        // FUL-2: the docstring always promised supercover, but the walk never checked the corner
        // pair on a diagonal step — sight (both teams', plus overwatch and focus cones) slipped
        // between two diagonally-touching blockers that seal the corridor, exactly the cut
        // CostMap forbids for movement. A diagonal step is blocked only when BOTH facing tiles
        // block (one corner stays sighted — that's the diagonal half-cover read; two is a wall).
        // Point-blank (Chebyshev 1) keeps its exception: an adjacent diagonal shot slips past a
        // TRUE corner and resolves as full cover, not as no-LOS (Grid.GetCover's contract).
        bool pointBlank = Math.Max(dx, dy) <= 1;
        while (true)
        {
            if (guard++ > 1000) return false;  // fail closed: deny sight rather than grant a free sightline on runaway
            if (cx == x1 && cy == y1) return true;
            int e2 = 2 * err;
            bool stepX = e2 > -dy, stepY = e2 < dx;
            if (stepX && stepY && !pointBlank)
            {
                bool cornerA = overHighCover ? IsVapor(cx + sx, cy) : BlocksSight(cx + sx, cy);
                bool cornerB = overHighCover ? IsVapor(cx, cy + sy) : BlocksSight(cx, cy + sy);
                if (cornerA && cornerB) return false;
            }
            // P28 — THE EDGE the step CROSSES. `overHighCover` (a commanding shooter seeing over
            // high cover) deliberately does NOT see over a wall: it is an elevation allowance
            // against a chest-high block, not x-ray vision, and a building wall is the one thing
            // on this board that should still stop it. Smoke keeps its own rule above.
            if (AnyEdges && Edges.Enabled)
            {
                if (stepX && stepY)
                {
                    // Same shape as the corner-pair rule above: one open route still sees.
                    if (!pointBlank)
                    {
                        bool routeA = !EdgeStopsSight(cx, cy, cx + sx, cy) && !EdgeStopsSight(cx + sx, cy, cx + sx, cy + sy);
                        bool routeB = !EdgeStopsSight(cx, cy, cx, cy + sy) && !EdgeStopsSight(cx, cy + sy, cx + sx, cy + sy);
                        if (!routeA && !routeB) return false;
                    }
                }
                else if (stepX) { if (EdgeStopsSight(cx, cy, cx + sx, cy)) return false; }
                else if (stepY) { if (EdgeStopsSight(cx, cy, cx, cy + sy)) return false; }
            }
            if (stepX) { err -= dy; cx += sx; }
            if (stepY) { err += dx; cy += sy; }
            // endpoint reached after step?
            if (cx == x1 && cy == y1) return true;
            bool blocked = overHighCover ? IsVapor(cx, cy) : BlocksSight(cx, cy);
            if (blocked) return false;
        }
    }

    // ---------- Cover ----------
    public struct CoverInfo
    {
        public int Level;     // 0 none, 1 low, 2 high
        public bool Flanked;  // had adjacent cover, but not protecting from this angle
        public bool Partial;  // diagonal-at-range: defender only partly obscured -> half defense
        public bool Foliage;  // C4/VERDANT: this level is UNDERGROWTH (omnidirectional, distance-gated),
                              // not a terrain block — the HUD labels it differently and no block chips
        public int Defense
        {
            get { int d = Level == 2 ? 40 : (Level == 1 ? 20 : 0); return Partial ? d / 2 : d; }
        }
    }

    /// Cover that a unit standing on (tx,ty) gets against fire coming from (fx,fy).
    public CoverInfo GetCover(int tx, int ty, int fx, int fy)
    {
        int dx = fx - tx, dy = fy - ty;

        // P28: the facing side of a tile is sheltered by EITHER a wall on that edge OR a cover
        // tile beyond it, whichever is stronger. That is the whole of the edge layer's effect on
        // combat — every rule downstream (the diagonal corner read, Partial, Flanked, the foliage
        // floor, high ground seeing over LOW cover, Ai's tile scoring, the HUD pip) is untouched,
        // because they all consume this one number. A cover TILE is simply an object that blocks
        // all four of its own edges, which is why the two sources max together rather than fight.
        int LevelAt(int sx, int sy)
        {
            int nx = tx + sx, ny = ty + sy;
            int lvl = Edges.CoverLevel(EdgeBetween(tx, ty, nx, ny));
            if (!InBounds(nx, ny)) return lvl;
            if (Tiles[nx, ny] == TileType.HighCover) return Math.Max(lvl, 2);
            if (Tiles[nx, ny] == TileType.LowCover) return Math.Max(lvl, 1);
            return lvl;
        }

        bool horiz = Math.Abs(dx) > Math.Abs(dy) && dx != 0;
        bool vert  = Math.Abs(dy) > Math.Abs(dx) && dy != 0;
        bool diagonal = dx != 0 && dy != 0 && Math.Abs(dx) == Math.Abs(dy);

        int best;
        bool partial = false;
        if (diagonal)
        {
            int h = LevelAt(Util.Sign(dx), 0);
            int v = LevelAt(0, Util.Sign(dy));
            if (h > 0 && v > 0)
            {
                best = Math.Min(h, v);            // a true corner (both facing sides) -> full cover
            }
            else
            {
                int side = Math.Max(h, v);        // a single facing-side cover block
                int dist = Math.Abs(dx);          // == |dy| on a diagonal
                if (side > 0 && dist > 1)
                {
                    best = side; partial = true;  // at range the defender is partly obscured -> half cover
                }
                else
                {
                    best = 0;                     // point-blank diagonal slips past the corner -> flank
                }
            }
        }
        else
        {
            // attack is dominantly along one axis: the facing side on that axis covers
            best = horiz ? LevelAt(Util.Sign(dx), 0) : (vert ? LevelAt(0, Util.Sign(dy)) : 0);
        }

        // C4 / VERDANT — UNDERGROWTH. Standing in the ferns is LOW cover from EVERY angle, but
        // only against fire from beyond Terrain.FoliageMinDist tiles: close in and the foliage is
        // worth nothing. It is a FLOOR under the terrain read, never a bonus on top of it — a
        // soldier already behind a high block gains nothing, and a soldier the block does not
        // protect (flanked, or only half-covered on a diagonal) is lifted to a clean low cover.
        // Deliberately a cover LEVEL and not a separate aim modifier, so every existing rule that
        // knows about cover — high ground and a SYNDICATE optic see over LOW cover, a DRONE
        // ignores cover, the crit-vs-exposed bonus, the LOCK-ON flank perk, Ai's cover scoring,
        // the HUD's cover pip — prices it correctly with no second implementation to drift.
        bool foliage = false;
        if (Terrain.Enabled && AnyFoliage && InBounds(tx, ty) && Ground[tx, ty] == GroundKind.Undergrowth
            && Math.Max(Math.Abs(dx), Math.Abs(dy)) > Terrain.FoliageMinDist
            && (best == 0 || (best == 1 && partial)))
        {
            best = 1; partial = false; foliage = true;
        }

        // FLANKED means "you had cover here, and it is not helping against THIS angle". P28: an
        // edge wall is cover you have, so it has to count here too — otherwise a soldier sheltered
        // by a building wall and shot from the open side reads as simply out in the open, and the
        // crit-vs-exposed bonus and the LOCK-ON flank perk both silently stop firing against
        // exactly the geometry the edge layer exists to create. SIGHTLINE_EDGETEST leg (D) is
        // this line: it failed on the first build, which is how the fifth touchpoint was found.
        bool anyAdjacent = false;
        int[,] dirs = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
        for (int i = 0; i < 4; i++)
        {
            int ax = tx + dirs[i, 0], ay = ty + dirs[i, 1];
            if (IsCover(ax, ay)) anyAdjacent = true;
            if (Edges.CoverLevel(EdgeBetween(tx, ty, ax, ay)) > 0) anyAdjacent = true;
        }

        return new CoverInfo { Level = best, Partial = partial, Foliage = foliage,
                               Flanked = best == 0 && anyAdjacent };
    }

    // ---------- Pathfinding (8-directional Dijkstra) ----------
    // Costs are in half-tiles: orthogonal = 2, diagonal = 3. Budget = mobility * 2.
    static readonly int[,] Dirs8 =
    {
        { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 },
        { 1, 1 }, { 1, -1 }, { -1, 1 }, { -1, -1 }
    };

    /// Dijkstra cost map from (sx,sy). blocked(x,y) returns true for impassable tiles
    /// (used to mark other units as obstacles). Returns cost[,] (-1 = unreachable)
    /// and fills cameFrom for path reconstruction.
    public int[,] CostMap(int sx, int sy, Func<int, int, bool> blocked,
                          out (int, int)[,] cameFrom, int maxCost)
    {
        var cost = new int[W, H];
        cameFrom = new (int, int)[W, H];
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++) { cost[x, y] = -1; cameFrom[x, y] = (-1, -1); }

        var pq = new SortedSet<(int c, int x, int y)>();
        cost[sx, sy] = 0;
        pq.Add((0, sx, sy));

        while (pq.Count > 0)
        {
            var cur = pq.Min;
            pq.Remove(cur);
            int cc = cur.c, cx = cur.x, cy = cur.y;
            if (cc != cost[cx, cy]) continue;

            for (int i = 0; i < 8; i++)
            {
                int ddx = Dirs8[i, 0], ddy = Dirs8[i, 1];
                int nx = cx + ddx, ny = cy + ddy;
                if (!InBounds(nx, ny)) continue;
                if (!IsFloor(nx, ny)) continue;
                if (blocked != null && blocked(nx, ny)) continue;

                bool diagonal = ddx != 0 && ddy != 0;
                if (diagonal)
                {
                    // forbid cutting around the corner of any non-walkable tile
                    if (!IsFloor(cx + ddx, cy) || !IsFloor(cx, cy + ddy)) continue;
                    if (blocked != null && (blocked(cx + ddx, cy) || blocked(cx, cy + ddy))) continue;
                    // P28 — THE EDGE LAYER, same rule one level down. A diagonal is really two
                    // orthogonal steps, and there are two ways round the corner; a wall on either
                    // leg of BOTH routes closes it. This deliberately matches the conservative
                    // tile rule directly above (both facing tiles must be walkable) rather than
                    // inventing a laxer one for edges — one movement model, not two.
                    if (AnyEdges && Edges.Enabled)
                    {
                        bool routeA = !EdgeStopsMove(cx, cy, cx + ddx, cy) && !EdgeStopsMove(cx + ddx, cy, nx, ny);
                        bool routeB = !EdgeStopsMove(cx, cy, cx, cy + ddy) && !EdgeStopsMove(cx, cy + ddy, nx, ny);
                        if (!routeA || !routeB) continue;
                    }
                }
                else if (EdgeStopsMove(cx, cy, nx, ny)) continue;   // P28: a wall between the tiles

                int step = diagonal ? 3 : 2;
                // C4 — the GROUND has a price, and BOTH teams pay it out of this one cost map
                // (Ai.Plan, the player's move overlay, the VIP leash and every reachability probe
                // all come through here, so there is no second movement model to keep in step).
                //   TUNDRA ice   — half a step: the drift is a fast LANE you can ride.
                //   MAGMA  vent  — dear: the fissure is a barrier you force a crossing through,
                //                  and OnUnitEnteredTile charges the second half of the toll in HP.
                //   ARID   sand  — dear by half a step: a basin you route around rather than force.
                // (VOID's RIFT is NOT here: it is impassable, which IsFloor above already says.)
                if (Terrain.Enabled)
                {
                    var gk = Ground[nx, ny];
                    if (gk == GroundKind.Ice && AnyIce) step = diagonal ? Terrain.IceStepDiag : Terrain.IceStepOrth;
                    // P16 / ARID — SOFT SAND, the exact inverse of the drift and the whole of that
                    // mechanic. One line, in the one cost map, so the opponent re-prices itself.
                    else if (gk == GroundKind.Sand && AnySand && Terrain.NewGround) step = diagonal ? Terrain.SandStepDiag : Terrain.SandStepOrth;
                    else if (gk == GroundKind.Vent && AnyVent) step += Terrain.VentStepExtra;
                }
                int nc = cc + step;
                if (nc > maxCost) continue;
                if (cost[nx, ny] == -1 || nc < cost[nx, ny])
                {
                    if (cost[nx, ny] != -1) pq.Remove((cost[nx, ny], nx, ny));
                    cost[nx, ny] = nc;
                    cameFrom[nx, ny] = (cx, cy);
                    pq.Add((nc, nx, ny));
                }
            }
        }
        return cost;
    }

    public List<(int x, int y)> ReconstructPath((int, int)[,] cameFrom, int sx, int sy, int tx, int ty)
    {
        var path = new List<(int, int)>();
        int cx = tx, cy = ty;
        int guard = 0;
        while (!(cx == sx && cy == sy))
        {
            if (guard++ > 1000) break;
            path.Add((cx, cy));
            var (px, py) = cameFrom[cx, cy];
            if (px == -1) { path.Clear(); break; }
            cx = px; cy = py;
        }
        path.Reverse();
        return path;
    }
}
