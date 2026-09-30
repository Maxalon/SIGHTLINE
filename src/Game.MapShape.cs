using System;
using System.Collections.Generic;
using System.Linq;

namespace Sightline;

/// SIGHTLINE_MAPSHAPEPROBE — the big-board program's INSTRUMENT (a REPORT, not an assertion).
///
/// The owner's direction (`docs/DESIGN.md` §6.5) is a mission SHAPE: enter one side, find the
/// objective in the middle, extract on the other side, with positioning a commitment because the
/// board is big relative to a move. Whether a built mission has that shape is a geometric fact
/// about where the squad, the hostiles, the objective and the exit actually land — so this probe
/// builds real missions at several board sizes and prints exactly that, in TURNS OF MOVEMENT
/// rather than tiles, because "far" only means something against how far a soldier walks.
///
/// It exists because P56 started by fixing something nobody had looked at. Look first.
public partial class Game
{
    public static string MapShapeProbe()
    {
        var sb = new System.Text.StringBuilder();
        int savedW = Cfg.GridW, savedH = Cfg.GridH, savedTile = Cfg.Tile;
        var objs = new[] { Objective.Hack, Objective.Evac, Objective.Escort, Objective.Sabotage,
                           Objective.Rescue, Objective.Eliminate };
        var sizes = new[] { (18, 11), (36, 22), (48, 30) };
        try
        {
            sb.AppendLine("MAPSHAPEPROBE: where a built mission's pieces land, in TURNS of movement "
                        + "(one turn = two move actions at the squad's mean Mobility)");
            foreach (var (w, h) in sizes)
            {
                sb.AppendLine($"\n=== {w}x{h} ({w * h} tiles) ===");
                sb.AppendLine("  objective   squad-x   foes  pods  foe-x  site-x    evac-x   "
                            + "squad->site  site->evac  squad->foes  cover%  reach%");
                foreach (var obj in objs)
                {
                    Cfg.SetBoard(w, h, 32);
                    Util.Reseed(4242 + w);
                    var g = new Game { NoPersist = true, ForcedObjective = obj };
                    g._run = new Run(); g._run.Start(); g._run.HeatLevel = 0;
                    g.SetupMission(3);
                    sb.AppendLine("  " + g.ShapeRow(obj));
                }
            }
        }
        catch (Exception e) { sb.AppendLine("MAPSHAPEPROBE: threw " + e.Message); }
        finally { Cfg.SetBoard(savedW, savedH, savedTile); }
        return sb.ToString();
    }

    string ShapeRow(Objective obj)
    {
        var squad = Players.Where(p => p.Alive && !p.IsVip).ToList();
        var foes = Enemies.Where(e => e.Alive).ToList();
        if (squad.Count == 0) return $"{obj,-10} (no squad)";
        double sx = squad.Average(p => p.X), sy = squad.Average(p => p.Y);
        int mob = Math.Max(1, (int)Math.Round(squad.Average(p => p.Mobility)));
        int ax = (int)Math.Round(sx), ay = (int)Math.Round(sy);
        // nearest walkable tile to the squad centroid, so the flood starts on the floor
        var start = squad.OrderBy(p => Math.Abs(p.X - sx) + Math.Abs(p.Y - sy)).First();
        var cost = Grid.CostMap(start.X, start.Y, null, out _, 99999);

        double Turns(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Grid.W || y >= Grid.H || cost[x, y] < 0) return double.NaN;
            return cost[x, y] / 2.0 / (2.0 * mob);     // half-tiles -> tiles -> turns
        }
        double TurnsTo(IEnumerable<(int x, int y)> tiles)
        {
            var t = tiles.Select(p => Turns(p.x, p.y)).Where(v => !double.IsNaN(v)).ToList();
            return t.Count == 0 ? double.NaN : t.Min();
        }

        // THE SITE the objective is about
        List<(int x, int y)> site = obj switch
        {
            Objective.Hack => new() { Terminal },
            Objective.Sabotage => SabotageSites.ToList(),
            Objective.Rescue or Objective.Escort => Vip != null ? new() { (Vip.X, Vip.Y) } : new(),
            _ => new(),
        };
        if (obj == Objective.Eliminate && foes.Count > 0)
            site = new() { ((int)Math.Round(foes.Average(f => f.X)), (int)Math.Round(foes.Average(f => f.Y))) };

        // pods: hostiles chained by Chebyshev <= 2
        int pods = 0; var seen = new HashSet<Unit>();
        foreach (var f in foes)
        {
            if (seen.Contains(f)) continue;
            pods++; var q = new Queue<Unit>(); q.Enqueue(f); seen.Add(f);
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var o in foes)
                    if (!seen.Contains(o) && Util.ChebyDist(c.X, c.Y, o.X, o.Y) <= 2) { seen.Add(o); q.Enqueue(o); }
            }
        }

        double toSite = site.Count > 0 ? TurnsTo(site) : double.NaN;
        double siteToEvac = double.NaN;
        if (site.Count > 0 && EvacZone.Count > 0)
        {
            var s0 = site[0];
            var sc = Grid.CostMap(Math.Clamp(s0.x, 0, Grid.W - 1), Math.Clamp(s0.y, 0, Grid.H - 1), null, out _, 99999);
            var ev = EvacZone.Where(e => sc[e.x, e.y] >= 0).Select(e => sc[e.x, e.y] / 2.0 / (2.0 * mob)).ToList();
            siteToEvac = ev.Count > 0 ? ev.Min() : double.NaN;
        }
        double toFoes = foes.Count > 0 ? TurnsTo(foes.Select(f => (f.X, f.Y))) : double.NaN;

        int cover = 0, floor = 0, reach = 0;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                if (Grid.Tiles[x, y] != TileType.Floor) cover++;
                if (Grid.IsFloor(x, y)) { floor++; if (cost[x, y] >= 0) reach++; }
            }

        string Rng(IEnumerable<int> xs) { var l = xs.ToList(); return l.Count == 0 ? "-" : $"{l.Min()}-{l.Max()}"; }
        string F(double v) => double.IsNaN(v) ? "   -" : $"{v,4:F1}";
        return $"{obj,-10} {Rng(squad.Select(p => p.X)),7}   {foes.Count,4}  {pods,4}  "
             + $"{Rng(foes.Select(f => f.X)),5}  {Rng(site.Select(p => p.x)),6}  {Rng(EvacZone.Select(e => e.x)),8}   "
             + $"{F(toSite),8}    {F(siteToEvac),8}    {F(toFoes),8}   "
             + $"{100.0 * cover / (Grid.W * Grid.H),5:F1}  {100.0 * reach / Math.Max(1, floor),5:F1}";
    }
}
