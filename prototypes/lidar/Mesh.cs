using System; using System.Collections.Generic; using System.Numerics; using Raylib_cs;
namespace Lid;

/// A prop as the SCANNER sees it: real triangles, with per-triangle coverage.
/// This is the general answer to "does the mask idea survive a non-flat surface" -- it needs no
/// UV unwrap and no special case per shape. A cylinder, a sphere and a box are all just triangles.
public class PropMesh {
  public Vector3[] A, B, C;      // triangle corners, world space
  public Vector3[] N;            // face normals
  public byte[] Shade;           // the mesh's OWN baked lighting -- never touched by the scan
  public byte[] Cov;             // confidence 0..255, written by the scan
  public Vector3 Mn, Mx;         // AABB for the broad phase
  public int Count;

  public static unsafe PropMesh From(Model m, Vector3 pos, float rotDeg, float scale) {
    var me = m.Meshes[0];
    int tris = me.TriangleCount;
    var p = new PropMesh { A=new Vector3[tris], B=new Vector3[tris], C=new Vector3[tris],
                           N=new Vector3[tris], Shade=new byte[tris], Cov=new byte[tris], Count=tris };
    float ca = MathF.Cos(rotDeg*MathF.PI/180f), sa = MathF.Sin(rotDeg*MathF.PI/180f);
    Vector3 Xf(int vi) {
      var v = new Vector3(me.Vertices[vi*3], me.Vertices[vi*3+1], me.Vertices[vi*3+2]) * scale;
      return new Vector3(v.X*ca + v.Z*sa, v.Y, -v.X*sa + v.Z*ca) + pos;
    }
    byte Col(int vi) => me.Colors != null ? me.Colors[vi*4] : (byte)200;
    p.Mn = new Vector3(float.MaxValue); p.Mx = new Vector3(float.MinValue);
    for (int t = 0; t < tris; t++) {
      int i0,i1,i2;
      if (me.Indices != null) { i0=me.Indices[t*3]; i1=me.Indices[t*3+1]; i2=me.Indices[t*3+2]; }
      else { i0=t*3; i1=t*3+1; i2=t*3+2; }
      p.A[t]=Xf(i0); p.B[t]=Xf(i1); p.C[t]=Xf(i2);
      p.N[t]=Vector3.Normalize(Vector3.Cross(p.B[t]-p.A[t], p.C[t]-p.A[t]));
      p.Shade[t]=(byte)((Col(i0)+Col(i1)+Col(i2))/3);
      foreach (var v in new[]{p.A[t],p.B[t],p.C[t]}) {
        p.Mn = Vector3.Min(p.Mn, v); p.Mx = Vector3.Max(p.Mx, v);
      }
    }
    return p;
  }

  public bool HitsBox(Vector3 o, Vector3 d, float maxT) {
    float t0 = 0f, t1 = maxT;
    for (int i = 0; i < 3; i++) {
      float oi = i==0?o.X:i==1?o.Y:o.Z, di = i==0?d.X:i==1?d.Y:d.Z;
      float mn = i==0?Mn.X:i==1?Mn.Y:Mn.Z, mx = i==0?Mx.X:i==1?Mx.Y:Mx.Z;
      if (MathF.Abs(di) < 1e-8f) { if (oi < mn || oi > mx) return false; continue; }
      float a = (mn-oi)/di, b = (mx-oi)/di; if (a > b) (a,b) = (b,a);
      t0 = MathF.Max(t0,a); t1 = MathF.Min(t1,b); if (t0 > t1) return false;
    }
    return true;
  }

  /// Moller-Trumbore. Returns the nearest triangle the ray strikes.
  public int Trace(Vector3 o, Vector3 d, float maxT, out float best) {
    best = maxT; int hit = -1;
    if (!HitsBox(o, d, maxT)) return -1;
    for (int t = 0; t < Count; t++) {
      Vector3 e1 = B[t]-A[t], e2 = C[t]-A[t], pv = Vector3.Cross(d, e2);
      float det = Vector3.Dot(e1, pv);
      if (MathF.Abs(det) < 1e-9f) continue;
      float inv = 1f/det; Vector3 tv = o - A[t];
      float u = Vector3.Dot(tv, pv)*inv; if (u < 0f || u > 1f) continue;
      Vector3 qv = Vector3.Cross(tv, e1);
      float v = Vector3.Dot(d, qv)*inv; if (v < 0f || u+v > 1f) continue;
      float tt = Vector3.Dot(e2, qv)*inv;
      if (tt > 1e-4f && tt < best) { best = tt; hit = t; }
    }
    return hit;
  }
}
