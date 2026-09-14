using System; using System.Numerics;
namespace Lid;

public static class Ray {
  const float Eps = 0.012f;   // depth bias: a return sits ON its surface, never inside it
  /// March one LiDAR ray. The world is a HEIGHT FIELD + EDGE WALLS, so this is a 2D DDA with a
  /// height test at each crossing -- not a triangle intersection. That is the whole reason a
  /// full scan is affordable.
  public static Hit Cast(World w, Vector3 o, Vector3 d, float maxD) {
    var hit = new Hit();
    int cx = (int)MathF.Floor(o.X), cz = (int)MathF.Floor(o.Z);
    if (!w.In(cx, cz)) return hit;

    int stepX = d.X > 0 ? 1 : -1, stepZ = d.Z > 0 ? 1 : -1;
    float invX = d.X != 0 ? MathF.Abs(1f/d.X) : float.MaxValue;
    float invZ = d.Z != 0 ? MathF.Abs(1f/d.Z) : float.MaxValue;
    float nx = d.X > 0 ? (cx + 1 - o.X) : (o.X - cx);
    float nz = d.Z > 0 ? (cz + 1 - o.Z) : (o.Z - cz);
    float tMaxX = d.X != 0 ? nx * invX : float.MaxValue;
    float tMaxZ = d.Z != 0 ? nz * invZ : float.MaxValue;
    float t = 0f;

    float RayY(float tt) => o.Y + d.Y * tt;

    for (int guard = 0; guard < 512; guard++) {
      float tExit = MathF.Min(tMaxX, tMaxZ);
      if (t >= maxD) break;
      float segEnd = MathF.Min(tExit, maxD);

      // --- FLOOR: does the ray drop through y=0 inside this cell? ---
      if (d.Y < 0) {
        float tf = -o.Y / d.Y;
        if (tf >= t && tf <= segEnd) {
          hit.Any = true; hit.K = SurfKind.Floor; hit.A = cx; hit.B = cz;
          hit.Side = 4; hit.Dist = tf; hit.P = o + d * (tf - Eps);
          hit.U = hit.P.X - cx; hit.V = hit.P.Z - cz; hit.Cos = MathF.Abs(d.Y); return hit;
        }
      }
      // --- PROP occupying this cell ---
      float ph = w.Prop[cx, cz];
      if (ph > 0f) {
        if (RayY(t) < ph) {                       // entered its side
          hit.Any = true; hit.K = SurfKind.Prop; hit.A = cx; hit.B = cz;
          hit.Side = 0; hit.Dist = t; hit.P = o + d * (t - Eps); return hit;
        }
        if (d.Y < 0) {                            // descends onto its top
          float tt = (ph - o.Y) / d.Y;
          if (tt >= t && tt <= segEnd) {
            hit.Any = true; hit.K = SurfKind.Prop; hit.A = cx; hit.B = cz;
            hit.Side = 4; hit.Dist = tt; hit.P = o + d * (tt - Eps); return hit;
          }
        }
      }
      if (tExit > maxD) break;

      // --- cross into the next cell, testing the EDGE we pass through ---
      if (tMaxX < tMaxZ) {
        int ex = stepX > 0 ? cx + 1 : cx;                 // the vertical edge line we cross
        float y = RayY(tMaxX);
        var e = w.VAt(ex, cz);
        if (e != Edge.None && y >= 0f && y <= w.EdgeH(e)) {
          hit.Any = true; hit.K = SurfKind.EdgeV; hit.A = ex; hit.B = cz;
          hit.Side = stepX > 0 ? 0 : 1;                   // 0 = its WEST face, 1 = its EAST face
          hit.Dist = tMaxX; hit.P = o + d * tMaxX;
          // THE FACE IS WHERE THE GEOMETRY IS. The DDA crosses the edge LINE, but the surface a
          // renderer draws sits half a thickness in front of it, so a return recorded on the line
          // floats inside the slab and reads through the far side. Snap to the struck face.
          hit.P.X = ex + (stepX > 0 ? -World.WallT/2f : World.WallT/2f);
          hit.U = hit.P.Z - cz; hit.V = y / w.EdgeH(e); hit.Cos = MathF.Abs(d.X);
          hit.P -= d * Eps; return hit;
        }
        t = tMaxX; tMaxX += invX; cx += stepX;
      } else {
        int ez = stepZ > 0 ? cz + 1 : cz;
        float y = RayY(tMaxZ);
        var e = w.HAt(cx, ez);
        if (e != Edge.None && y >= 0f && y <= w.EdgeH(e)) {
          hit.Any = true; hit.K = SurfKind.EdgeH; hit.A = cx; hit.B = ez;
          hit.Side = stepZ > 0 ? 0 : 1;                   // 0 = its NORTH face, 1 = its SOUTH face
          hit.Dist = tMaxZ; hit.P = o + d * tMaxZ;
          hit.P.Z = ez + (stepZ > 0 ? -World.WallT/2f : World.WallT/2f);
          hit.U = hit.P.X - cx; hit.V = y / w.EdgeH(e); hit.Cos = MathF.Abs(d.Z);
          hit.P -= d * Eps; return hit;
        }
        t = tMaxZ; tMaxZ += invZ; cz += stepZ;
      }
      if (!w.In(cx, cz)) break;
    }
    return hit;
  }
}
