using System; using System.Collections.Generic; using System.Diagnostics; using System.Numerics;
using Raylib_cs; using Lid;

class Prog {
  const int W = 24, H = 16;
  const int FPT = 16;                 // floor coverage texels PER TILE
  const int MW = 16, MV = 22;         // wall mask: across one segment, up its height
  static Color C(int r,int g,int b,int a=255)=>new Color(r,g,b,a);

  // ── COVERAGE ──────────────────────────────────────────────────────────────
  // THE FLOOR IS ONE SURFACE. It was 384 separate per-tile masks, and Stamp clipped every
  // footprint at the tile it happened to be indexed by -- which is why the cone edge broke up
  // on tile lines. A continuous world grid has no seams to break on.
  static byte[,] floorCov = new byte[W*FPT, H*FPT];
  static byte[,,][] mkV = new byte[W+1,H,2][], mkH = new byte[W,H+1,2][];
  static List<PropMesh> props = new();
  static float DAng = 0.02f;
  static bool MASK = true;
  static int NORIG = 7;

  // ── confidence, not a flag ────────────────────────────────────────────────
  // A return's worth depends on how tightly it resolved the surface. A crisp near hit is
  // knowledge; a smeared far one is a rumour. That single byte gives the gradient.
  static byte Conf(float foot, bool active) {
    float q = 1f - Math.Clamp(foot / 0.42f, 0f, 1f);        // 1 = crisp, 0 = smeared
    return (byte)(active ? 180 + 75*q : 74 + 116*q);
  }

  /// A scan cell's ground footprint is an ELLIPSE, elongated along the ray's ground bearing --
  /// and its along-ground extent IS the spacing to the next ring. Size it correctly and the
  /// footprints TILE the floor with no gaps, at any range. The old isotropic-circle-plus-clamp
  /// broke exactly that property, which is why the far coverage fell apart into rings.
  static bool NoStamp = Environment.GetEnvironmentVariable("LID_NOSTAMP")=="1";
  static void StampFloorEll(float wx, float wz, Vector2 axis, float hAlong, float hPerp, byte val) {
    if (NoStamp) return;
    float r = MathF.Max(hAlong, hPerp);
    int i0=(int)MathF.Floor((wx-r)*FPT), i1=(int)MathF.Ceiling((wx+r)*FPT);
    int j0=(int)MathF.Floor((wz-r)*FPT), j1=(int)MathF.Ceiling((wz+r)*FPT);
    var perp = new Vector2(-axis.Y, axis.X);
    for (int j=j0;j<=j1;j++) for (int i=i0;i<=i1;i++) {
      if (i<0||j<0||i>=W*FPT||j>=H*FPT) continue;
      var q = new Vector2((i+0.5f)/FPT-wx, (j+0.5f)/FPT-wz);
      float a=Vector2.Dot(q,axis)/MathF.Max(hAlong,1e-4f), b=Vector2.Dot(q,perp)/MathF.Max(hPerp,1e-4f);
      if (a*a+b*b<=1f && floorCov[i,j]<val) floorCov[i,j]=val;
    }
  }

  static void StampRaw(byte[] m,int mw,int mh,float u,float v,float ru,float rv,byte val){
    if (NoStamp) return;
    int u0=(int)MathF.Floor((u-ru)*mw), u1=(int)MathF.Ceiling((u+ru)*mw);
    int v0=(int)MathF.Floor((v-rv)*mh), v1=(int)MathF.Ceiling((v+rv)*mh);
    for(int j=v0;j<=v1;j++) for(int i=u0;i<=u1;i++){
      if(i<0||j<0||i>=mw||j>=mh) continue;
      float du=((i+0.5f)/mw-u)/MathF.Max(ru,1e-4f), dv=((j+0.5f)/mh-v)/MathF.Max(rv,1e-4f);
      if(du*du+dv*dv<=1f && m[j*mw+i]<val) m[j*mw+i]=val;
    }
  }

  // A wall RUN is one surface too, even though it is stored as one mask per segment. A footprint
  // near a segment join spills into its neighbour -- only where a wall actually continues, so it
  // can never leak across a doorway.
  static void StampWall(World w, bool vert, int ex, int ez, int side,
                        float u, float v, float ru, float rv, byte val) {
    for (int k=-1;k<=1;k++) {
      int nx = vert ? ex : ex+k, nz = vert ? ez+k : ez;
      Edge e = vert ? w.VAt(nx,nz) : w.HAt(nx,nz);
      if (e == Edge.None) continue;
      float uu = u - k;
      if (uu+ru < 0f || uu-ru > 1f) continue;
      var arr = vert ? (mkV[nx,nz,side] ??= new byte[MW*MV]) : (mkH[nx,nz,side] ??= new byte[MW*MV]);
      StampRaw(arr, MW, MV, uu, v, ru, rv, val);
    }
  }

  static float Foot(float dist, float cos) => MathF.Min(0.42f, dist*DAng/Math.Clamp(cos,0.22f,1f));

  /// The surface's OWN look. World-keyed, so it never aligns to the grid we index by, and it
  /// knows nothing about the scan.
  static float Frac(float v){ v-=MathF.Floor(v); return v; }
  static float Material(float wx,float wy,float wz){
    float seam = (Frac(wx*0.77f)<0.035f || Frac(wz*0.77f)<0.035f || Frac(wy*1.31f)<0.04f) ? 0.7f : 1f;
    return seam;
  }

  // ── one ray ───────────────────────────────────────────────────────────────
  static void Shoot(World w, Vector3 o, Vector3 d, float range, bool active, float dEl, float dAz, float ringHalf) {
    var h = Lid.Ray.Cast(w, o, d, range);
    float bestT = h.Any ? h.Dist : range;
    PropMesh bp = null; int bt = -1;
    foreach (var p in props) {
      int t = p.Trace(o, d, bestT, out float tt);
      if (t >= 0) { bestT = tt; bp = p; bt = t; }
    }
    if (bp != null) {                                   // a prop was nearer than wall/floor
      float cos = MathF.Abs(Vector3.Dot(d, bp.N[bt]));
      byte val = Conf(Foot(bestT, cos), active);
      if (bp.Cov[bt] < val) bp.Cov[bt] = val;
      return;
    }
    if (!h.Any) return;
    if (h.K == SurfKind.Floor) {
      var g = new Vector2(d.X, d.Z); float gl = g.Length();
      if (gl < 1e-5f) g = new Vector2(1,0); else g /= gl;
      float rad = new Vector2(h.P.X-o.X, h.P.Z-o.Z).Length();
      float hPerp  = MathF.Max(0.035f, rad * dAz * 0.62f);     // arc between neighbouring bearings
      float hAlong = MathF.Max(0.035f, ringHalf);              // half the gap to the next ring
      StampFloorEll(h.P.X, h.P.Z, g, hAlong, hPerp, Conf(MathF.Max(hAlong,hPerp), active));
      return;
    }
    // A WALL smears HORIZONTALLY with grazing incidence; its vertical extent does not care.
    float vHalf = MathF.Min(0.55f, h.Dist * dEl * 0.62f);
    float uHalf = MathF.Min(0.55f, h.Dist * dAz * 0.62f / Math.Clamp(h.Cos, 0.25f, 1f));
    byte v2 = Conf(MathF.Max(uHalf, vHalf), active);
    switch (h.K) {
      case SurfKind.EdgeV: { float hh = w.EdgeH(w.EV[h.A,h.B]);
        StampWall(w, true,  h.A, h.B, h.Side, h.U, h.V, uHalf, vHalf/hh, v2); break; }
      case SurfKind.EdgeH: { float hh = w.EdgeH(w.EH[h.A,h.B]);
        StampWall(w, false, h.A, h.B, h.Side, h.U, h.V, uHalf, vHalf/hh, v2); break; }
    }
  }

  static Vector3[] Origins(Vector3 eye, int n) {
    if (n <= 1) return new[]{ eye };
    var l = new List<Vector3>{ eye }; const float R = 0.34f;
    for (int i=0;i<n-1;i++){ float a=MathF.Tau*i/(n-1);
      l.Add(eye + new Vector3(MathF.Cos(a)*R, (i%2==0)?0f:-0.52f, MathF.Sin(a)*R)); }
    return l.ToArray();
  }

  /// The scanner's vertical sweep. DOWNWARD rays are placed by the GROUND RADIUS they land on,
  /// not by uniform angle: uniform angle packs ten rings inside 4 m, leaves a 10-tile gap at the
  /// horizon, and cannot look closer than h/tan(elMax) -- the blind circle under the soldier.
  static (List<float>, float[], List<float>) Ladder(int nAz, int nEl) {
    const float EyeH = 1.62f, RMin = 0.30f, RMax = 23f;
    int nDown = Math.Max(2, nEl*3/4), nUp = Math.Max(1, nEl-nDown);
    var els = new List<float>(); var ring = new List<float>();
    float step = (RMax-RMin)/(nDown-1);
    for (int k=0;k<nDown;k++){ float r=RMin+step*k; els.Add(-MathF.Atan2(EyeH,r)); ring.Add(step*0.62f); }
    for (int k=0;k<nUp;k++){ els.Add(0.02f + 0.34f*k/MathF.Max(1,nUp-1)); ring.Add(0.2f); }
    var dEls = new float[els.Count];
    for (int k=0;k<els.Count;k++){
      float lo = k>0 ? els[k]-els[k-1] : els[1]-els[0];
      float hi = k<els.Count-1 ? els[k+1]-els[k] : els[^1]-els[^2];
      dEls[k] = 0.5f*(MathF.Abs(lo)+MathF.Abs(hi));
    }
    return (els, dEls, ring);
  }

  static unsafe void Main(string[] a) {
    string kit = a[0];
    int nAz = a.Length>2?int.Parse(a[2]):360, nEl = a.Length>3?int.Parse(a[3]):20;
    NORIG = int.Parse(Environment.GetEnvironmentVariable("LID_ORIGINS") ?? "7");
    DAng = MathF.Max(MathF.Tau/nAz, 0.72f/Math.Max(1,nEl-1));

    Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
    Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint);
    Raylib.InitWindow(1500, 900, "lidar");
    Model mTree = Raylib.LoadModel($"{kit}/tree.glb"),
          mCrate= Raylib.LoadModel($"{kit}/crate.glb"),
          mCar  = Raylib.LoadModel($"{kit}/car.glb");

    var w = new World(W,H);
    for (int x=4;x<12;x++){ w.EH[x,3]=Edge.High; if(x!=7) w.EH[x,9]=Edge.High; }
    for (int y=3;y<9;y++){ w.EV[4,y]=Edge.High; w.EV[12,y]=Edge.High; }
    for (int x=4;x<9;x++)  w.EH[x,6]=Edge.High;
    for (int y=6;y<9;y++)  if(y!=7) w.EV[9,y]=Edge.High;
    for (int y=11;y<15;y++) w.EV[17,y]=Edge.Low;

    // props are MESHES now -- no box proxy in the DDA, the triangles are the truth
    void Add(Model m,int x,int y,float rot,float sc){ props.Add(PropMesh.From(m,new Vector3(x+0.5f,0,y+0.5f),rot,sc)); }
    foreach (var t in new[]{(6,11),(9,12),(2,13),(20,2),(1,8),(13,13)}) Add(mTree,t.Item1,t.Item2,(t.Item1*47+t.Item2*13)%360,1.15f);
    foreach (var c in new[]{(15,5),(16,6),(5,4),(10,7),(20,13)}) Add(mCrate,c.Item1,c.Item2,(c.Item1*31)%90,1f);
    props.Add(PropMesh.From(mCar,new Vector3(15.5f,0,12.0f),0f,1f));

    var path = (Environment.GetEnvironmentVariable("LID_SCEN")=="door")
      ? new (int x,int y)[]{ (6,8) }
      : new (int x,int y)[]{ (7,14),(7,13),(7,12),(7,11),(7,10),(7,8),(6,7) };

    var (elsL, dElsL, ringL) = Ladder(nAz, nEl);
    var els = elsL; var dEls = dElsL; var ringH = ringL; float dAz = MathF.Tau/nAz;
    // ── build the elevation ladder ────────────────────────────────────────
    // DOWNWARD rays are placed by the GROUND RADIUS they land on, not by uniform angle. Uniform
    // angle puts ten rings inside 4 m and then leaves a 10-tile gap at the horizon, and it cannot
    // look closer than h/tan(elMax) -- the 3.6-tile blind circle around the soldier.
    const float EyeH = 1.62f;

    // ── LID_BENCH: the cost curve. Per-scan, then projected to a whole player turn, because
    //    that is the number that decides how much resolution is affordable.
    if (Environment.GetEnvironmentVariable("LID_BENCH")=="1") {
      Console.WriteLine($"{"az x el",-11}{"rays/scan",11}{"ms/scan",10}{"ms/turn*",10}  {"floor cov",10}");
      Console.WriteLine("  (*turn = 6 soldiers x 6 tiles walked = 36 scans, single-threaded)");
      // WARM UP FIRST. Without this the first configurations pay the JIT bill for every method
      // in the scan path and the table reads backwards -- 8,400 rays "slower" than 65,520.
      { var (we,wd,wr) = Ladder(240,18);
        var wo = new Vector3(path[0].x+0.5f,1.62f,path[0].y+0.5f);
        for (int rep=0;rep<2;rep++) for (int ia=0;ia<240;ia++){
          float az=MathF.Tau*ia/240, ca=MathF.Cos(az), sa2=MathF.Sin(az);
          for (int je=0;je<we.Count;je++){ float el=we[je], ce=MathF.Cos(el);
            Shoot(w,wo,new Vector3(ca*ce,MathF.Sin(el),sa2*ce),26f,true,wd[je],MathF.Tau/240,wr[je]); } }
      }
      foreach (var cfg in new[]{(120,10),(180,14),(240,18),(360,26),(480,34),(720,48),(1080,64)}) {
        Array.Clear(floorCov,0,floorCov.Length);
        mkV=new byte[W+1,H,2][]; mkH=new byte[W,H+1,2][];
        foreach(var pr in props) Array.Clear(pr.Cov,0,pr.Cov.Length);
        var (e2,d2,r2) = Ladder(cfg.Item1, cfg.Item2);
        var eye2=new Vector3(path[0].x+0.5f,1.62f,path[0].y+0.5f);
        double bestMs = double.MaxValue; long n2=0;
        for (int rep=0;rep<3;rep++) {
          var s2=Stopwatch.StartNew(); n2=0;
          foreach (var o in Origins(eye2, NORIG))
            for (int ia=0;ia<cfg.Item1;ia++){
              float az=MathF.Tau*ia/cfg.Item1, ca=MathF.Cos(az), sa2=MathF.Sin(az);
              for (int je=0;je<e2.Count;je++){
                float el=e2[je], ce=MathF.Cos(el);
                Shoot(w,o,new Vector3(ca*ce,MathF.Sin(el),sa2*ce),26f,true,d2[je],MathF.Tau/cfg.Item1,r2[je]); n2++;
              }
            }
          s2.Stop(); bestMs = Math.Min(bestMs, s2.Elapsed.TotalMilliseconds);
        }
        int cov=0; foreach(var b in floorCov) if(b>0) cov++;
        double ms=bestMs;
        Console.WriteLine($"{cfg.Item1+"x"+cfg.Item2,-11}{n2,11:N0}{ms,10:F1}{ms*36,10:F0}  {cov*100.0/(W*FPT*H*FPT),9:F1}%");
      }
      Raylib.CloseWindow(); return;
    }

    var sw = Stopwatch.StartNew(); long rays=0;
    for (int i=0;i<path.Length;i++) {
      bool act = i==path.Length-1;
      foreach (var o in Origins(new Vector3(path[i].x+0.5f,EyeH,path[i].y+0.5f), NORIG))
        for (int ia=0;ia<nAz;ia++){
          float az=MathF.Tau*ia/nAz, ca=MathF.Cos(az), sa2=MathF.Sin(az);
          for (int je=0;je<els.Count;je++){
            float el=els[je], ce=MathF.Cos(el);
            Shoot(w,o,new Vector3(ca*ce,MathF.Sin(el),sa2*ce),26f,act,dEls[je],dAz,ringH[je]); rays++;
          }
        }
    }
    sw.Stop();
    int covTris=0, totTris=0; foreach(var p in props){ totTris+=p.Count; foreach(var c in p.Cov) if(c>0) covTris++; }
    Console.WriteLine($"SCAN {rays:N0} rays in {sw.Elapsed.TotalMilliseconds:F0} ms  " +
                      $"prop triangles seen {covTris}/{totTris}");

    // ══ RENDER ═══════════════════════════════════════════════════════════════
    float fovy=a.Length>4?float.Parse(a[4]):19.5f, tgx=a.Length>5?float.Parse(a[5]):W/2f-1f;
    float tgz=a.Length>6?float.Parse(a[6]):H/2f, yawD=a.Length>7?float.Parse(a[7]):24f;
    float pitD=a.Length>8?float.Parse(a[8]):46f;
    float pitch=pitD*MathF.PI/180f, yaw=yawD*MathF.PI/180f;
    var tgt=new Vector3(tgx,0.4f,tgz);
    var dir=new Vector3(MathF.Cos(pitch)*MathF.Sin(yaw),MathF.Sin(pitch),MathF.Cos(pitch)*MathF.Cos(yaw));
    var cam=new Camera3D(tgt+dir*80f,tgt,new Vector3(0,1,0),fovy,CameraProjection.Orthographic);
    Color Mono(int v,int al=255)=>C((int)(v*0.62f),(int)(v*0.88f),Math.Min(255,v),al);
    void Quad(Vector3 p0,Vector3 p1,Vector3 p2,Vector3 p3,Color c){
      Raylib.DrawTriangle3D(p0,p1,p2,c); Raylib.DrawTriangle3D(p0,p2,p3,c);
      Raylib.DrawTriangle3D(p0,p2,p1,c); Raylib.DrawTriangle3D(p0,p3,p2,c); }

    Raylib.BeginDrawing();
    Raylib.ClearBackground(C(7,9,13));
    Raylib.BeginMode3D(cam);

    for (int i=0;i<W*FPT;i++) for (int j=0;j<H*FPT;j++) {
      byte c=floorCov[i,j]; if(c==0) continue;
      float x0=(float)i/FPT, x1=(float)(i+1)/FPT, z0=(float)j/FPT, z1=(float)(j+1)/FPT;
      int b=(int)(Material(x0,0f,z0)*(16+150*(c/255f)));
      Quad(new(x0,0,z0),new(x1,0,z0),new(x1,0,z1),new(x0,0,z1),Mono(b));
    }
    void WallDraw(byte[] m,bool vert,int ex,int ez,int side,float hh){
      for(int j=0;j<MV;j++) for(int i=0;i<MW;i++){
        byte c=m[j*MW+i]; if(c==0) continue;
        float u=(i+0.5f)/MW, v=(j+0.5f)/MV, y0=hh*j/MV, y1=hh*(j+1)/MV;
        float wx=vert?ex:ex+u, wz=vert?ez+u:ez;
        int b=(int)(Material(wx,hh*v,wz)*(30+195*(c/255f)));
        if(vert){ float px=ex+(side==0?-World.WallT/2f:World.WallT/2f);
          float a0=ez+(float)i/MW,a1=ez+(float)(i+1)/MW;
          Quad(new(px,y0,a0),new(px,y1,a0),new(px,y1,a1),new(px,y0,a1),Mono(b)); }
        else { float pz=ez+(side==0?-World.WallT/2f:World.WallT/2f);
          float a0=ex+(float)i/MW,a1=ex+(float)(i+1)/MW;
          Quad(new(a0,y0,pz),new(a0,y1,pz),new(a1,y1,pz),new(a1,y0,pz),Mono(b)); }
      }
    }
    for(int x=0;x<=W;x++) for(int y=0;y<H;y++) for(int s=0;s<2;s++){
      var m=mkV[x,y,s]; if(m!=null) WallDraw(m,true,x,y,s,w.EdgeH(w.EV[x,y])); }
    for(int x=0;x<W;x++) for(int y=0;y<=H;y++) for(int s=0;s<2;s++){
      var m=mkH[x,y,s]; if(m!=null) WallDraw(m,false,x,y,s,w.EdgeH(w.EH[x,y])); }

    // PROPS: only the triangles the scan actually struck, each with the mesh's OWN baked shade
    foreach (var p in props) for (int t=0;t<p.Count;t++) {
      byte c=p.Cov[t]; if(c==0) continue;
      int b=(int)(p.Shade[t]/255f*(34+200*(c/255f)));
      Raylib.DrawTriangle3D(p.A[t],p.B[t],p.C[t],Mono(b));
      Raylib.DrawTriangle3D(p.A[t],p.C[t],p.B[t],Mono(b));
    }

    var e=new Vector3(path[^1].x+0.5f,0.03f,path[^1].y+0.5f);
    Raylib.DrawCylinderEx(e,e+new Vector3(0,0.12f,0),0.34f,0.34f,16,Mono(255));
    Raylib.EndMode3D();
    Raylib.BeginBlendMode(BlendMode.Additive);
    for(int y=0;y<900;y+=3) Raylib.DrawRectangle(0,y,1500,1,Mono(26,14));
    Raylib.EndBlendMode();
    Raylib.DrawText("LIDAR COMPOSITE // CONTINUOUS FLOOR, MESH PROPS, CONFIDENCE GRADIENT",28,26,20,Mono(215));
    Raylib.DrawText($"{rays:N0} rays  {sw.Elapsed.TotalMilliseconds:F0} ms  prop tris {covTris}/{totTris}",28,52,16,Mono(120));
    Raylib.EndDrawing();
    Raylib.TakeScreenshot(a[1]);
    Raylib.CloseWindow();
  }
}
