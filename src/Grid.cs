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

    public const int HighCoverHp = 2;   // chips to crack High -> Low
    public const int LowCoverHp = 1;    // chips to clear Low -> Floor

    public Grid()
    {
        Tiles = new TileType[W, H];
        Height = new int[W, H];
        Smoke = new int[W, H];
        CoverHp = new int[W, H];
    }

    public enum CoverHit { None, Chipped, Downgraded, Destroyed }

    /// Full HP for a tile's CURRENT cover level (0 for floor).
    public int MaxCoverHp(int x, int y) => !InBounds(x, y) ? 0 :
        (Tiles[x, y] == TileType.HighCover ? HighCoverHp : (Tiles[x, y] == TileType.LowCover ? LowCoverHp : 0));

    /// Charge a freshly-placed cover tile to full HP (e.g. a deployed barricade).
    public void SetCoverHp(int x, int y) { if (InBounds(x, y)) CoverHp[x, y] = MaxCoverHp(x, y); }

    /// (Re)initialise HP for every cover tile — call once a mission's terrain is final.
    public void ResetCoverHp()
    {
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++) CoverHp[x, y] = MaxCoverHp(x, y);
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

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
    public TileType At(int x, int y) => Tiles[x, y];
    public bool IsCover(int x, int y) => InBounds(x, y) && Tiles[x, y] != TileType.Floor;
    // High cover OR an active smoke cloud blocks line of sight (and overwatch) through a tile.
    public bool BlocksSight(int x, int y) =>
        InBounds(x, y) && (Tiles[x, y] == TileType.HighCover || Smoke[x, y] > 0);
    public bool IsSmoke(int x, int y) => InBounds(x, y) && Smoke[x, y] > 0;

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

    /// Terrain elevation at a tile (0 ground, 1 high ground). High ground grants
    /// an aim/crit edge when firing down on a lower target.
    public int HeightAt(int x, int y) => InBounds(x, y) ? Height[x, y] : 0;
    public bool IsHigh(int x, int y) => HeightAt(x, y) > 0;

    /// A tile a unit can stand on (floor + in bounds). Occupancy handled by Game.
    public bool IsFloor(int x, int y) => InBounds(x, y) && Tiles[x, y] == TileType.Floor;

    // ---------- Line of sight ----------
    // Supercover line between tile centres; blocked by any intermediate HighCover tile.
    public bool HasLineOfSight(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        int cx = x0, cy = y0;
        int guard = 0;
        while (true)
        {
            if (guard++ > 1000) break;
            if (cx == x1 && cy == y1) return true;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; cx += sx; }
            if (e2 < dx)  { err += dx; cy += sy; }
            // endpoint reached after step?
            if (cx == x1 && cy == y1) return true;
            if (BlocksSight(cx, cy)) return false;
        }
        return true;
    }

    // ---------- Cover ----------
    public struct CoverInfo
    {
        public int Level;     // 0 none, 1 low, 2 high
        public bool Flanked;  // had adjacent cover, but not protecting from this angle
        public bool Partial;  // diagonal-at-range: defender only partly obscured -> half defense
        public int Defense
        {
            get { int d = Level == 2 ? 40 : (Level == 1 ? 20 : 0); return Partial ? d / 2 : d; }
        }
    }

    /// Cover that a unit standing on (tx,ty) gets against fire coming from (fx,fy).
    public CoverInfo GetCover(int tx, int ty, int fx, int fy)
    {
        int dx = fx - tx, dy = fy - ty;

        int LevelAt(int sx, int sy)
        {
            int nx = tx + sx, ny = ty + sy;
            if (!InBounds(nx, ny)) return 0;
            if (Tiles[nx, ny] == TileType.HighCover) return 2;
            if (Tiles[nx, ny] == TileType.LowCover) return 1;
            return 0;
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

        bool anyAdjacent = false;
        int[,] dirs = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
        for (int i = 0; i < 4; i++)
            if (IsCover(tx + dirs[i, 0], ty + dirs[i, 1])) anyAdjacent = true;

        return new CoverInfo { Level = best, Partial = partial, Flanked = best == 0 && anyAdjacent };
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
                }

                int step = diagonal ? 3 : 2;
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
