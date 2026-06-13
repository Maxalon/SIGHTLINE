using System;
using System.Collections.Generic;

namespace Breach;

public enum TileType { Floor, HighCover, LowCover }

/// The battlefield: tile types, line-of-sight, cover queries, pathfinding.
public class Grid
{
    public readonly int W = Cfg.GridW;
    public readonly int H = Cfg.GridH;
    public TileType[,] Tiles;

    public Grid()
    {
        Tiles = new TileType[W, H];
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
    public TileType At(int x, int y) => Tiles[x, y];
    public bool IsCover(int x, int y) => InBounds(x, y) && Tiles[x, y] != TileType.Floor;
    public bool BlocksSight(int x, int y) => InBounds(x, y) && Tiles[x, y] == TileType.HighCover;

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
        public int Defense => Level == 2 ? 40 : (Level == 1 ? 20 : 0);
    }

    /// Cover that a unit standing on (tx,ty) gets against fire coming from (fx,fy).
    public CoverInfo GetCover(int tx, int ty, int fx, int fy)
    {
        int dx = fx - tx, dy = fy - ty;
        int best = 0;

        // candidate protective sides, based on the dominant axis to the attacker
        var sides = new List<(int sx, int sy)>();
        if (Math.Abs(dx) >= Math.Abs(dy) && dx != 0) sides.Add((Util.Sign(dx), 0));
        if (Math.Abs(dy) >= Math.Abs(dx) && dy != 0) sides.Add((0, Util.Sign(dy)));

        foreach (var (sx, sy) in sides)
        {
            int nx = tx + sx, ny = ty + sy;
            if (!InBounds(nx, ny)) continue;
            if (Tiles[nx, ny] == TileType.HighCover) best = Math.Max(best, 2);
            else if (Tiles[nx, ny] == TileType.LowCover) best = Math.Max(best, 1);
        }

        bool anyAdjacent = false;
        int[,] dirs = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
        for (int i = 0; i < 4; i++)
            if (IsCover(tx + dirs[i, 0], ty + dirs[i, 1])) anyAdjacent = true;

        return new CoverInfo { Level = best, Flanked = best == 0 && anyAdjacent };
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
