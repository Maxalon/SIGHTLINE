using System; using System.Numerics;
namespace Lid;

// A wall lives on a tile EDGE. Stored once, so the two sides can never disagree.
public enum Edge : byte { None = 0, Low = 1, High = 2 }

public class World {
  public readonly int W, H;
  public Edge[,] EV;      // [W+1, H] wall on the WEST edge of tile (x,y)  -- runs along Z
  public Edge[,] EH;      // [W, H+1] wall on the NORTH edge of tile (x,y) -- runs along X
  public float[,] Prop;   // per-tile obstacle height, 0 = open floor
  public byte[,] Kind;    // 0 none 1 tree 2 crate 3 car

  public const float LowH = 0.55f, HighH = 1.9f;
  public const float WallT = 0.16f;   // the ONE thickness. Ray model and renderer both read it.
  public float EdgeH(Edge e) => e == Edge.None ? 0f : e == Edge.Low ? LowH : HighH;

  public World(int w, int h) {
    W = w; H = h;
    EV = new Edge[w+1, h]; EH = new Edge[w, h+1];
    Prop = new float[w, h]; Kind = new byte[w, h];
  }
  public Edge VAt(int x, int y) => (x < 0 || x > W || y < 0 || y >= H) ? Edge.None : EV[x,y];
  public Edge HAt(int x, int y) => (x < 0 || x >= W || y < 0 || y > H) ? Edge.None : EH[x,y];
  public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
}

// What a scan RETURN looks like. A surface is identified so we can mark it seen.
public enum SurfKind : byte { Floor, EdgeV, EdgeH, Prop }
public struct Hit {
  public bool Any; public Vector3 P; public SurfKind K;
  public float U, V;      // where on that face the return landed, 0..1
  public float Cos;       // |cos incidence| -- a grazing hit smears its footprint
  public int A, B;        // surface index (x,y of the edge/tile)
  public int Side;        // which FACE of it: 0/1 for a wall, 0..4 for a prop (4 = top)
  public float Dist;
}
