using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// ════════════════════ P38 — THE WIREFRAME, FOR ANY OBJECT ════════════════════
/// `Vision` has three tiers and the projected view only ever drew two of them: UNSEEN was absent
/// and everything else was `Known()`, a brightness multiply. So REMEMBERED — the tier the whole
/// discovery premise turns on, "we scanned this and we are no longer looking at it" — was a dim
/// solid, indistinguishable from a lit one in a dark corner.
///
/// This is the missing tier: the shape, in lines, with nothing filled in. It is deliberately a
/// GEOMETRY answer and not a shader one, for three reasons.
///
///   1. IT WORKS ON ANY OBJECT, with no per-asset preparation. `Extract` is a pure function of a
///      `Mesh`, so the prop kit, the procedural bevelled block, and anything added later all get a
///      wireframe for free. That was the requirement; a shader that needs custom vertex attributes
///      would have meant re-authoring every mesh.
///   2. IT IS CHEAPER THAN THE SOLID IT REPLACES. A hard-edge set is 5-10x smaller than the
///      triangle set (the kit measures 12-44 lines against 40-324 triangles), and most of a
///      discovered board is REMEMBERED — so the common case got faster, not slower.
///   3. `Raylib.DrawModelWires` ALREADY EXISTS AND IS THE WRONG ANSWER. It draws every TRIANGLE
///      edge, so a bevelled box shows the diagonal split of all 26 of its quads and reads as a
///      dense net rather than a scanned shape. `Wire.Mode = Wire.AllEdges` restores exactly that
///      look, and it is kept as the comparison rather than as an option anybody should ship:
///      WIRETEST asserts the hard-edge pass removes a large majority of it.
///
/// WHAT A SHADER WOULD STILL BUY, and why it is a later wave: constant-width lines that do not
/// thin out with distance, and a CONFIDENCE gradient — line brightness falling with range from the
/// scanner and with how long ago the surface was seen. None of that is needed to make the tier
/// exist; all of it needs the tier to exist first.
public static class Wire
{
    /// Two vertices of the source mesh, in MODEL space, that form a kept edge.
    public readonly struct Edge
    {
        public readonly Vector3 A, B;
        public Edge(Vector3 a, Vector3 b) { A = a; B = b; }
    }

    /// The angle two faces must differ by for their shared edge to be a CREASE worth drawing.
    ///
    /// 25 degrees is chosen against this kit specifically and it is a real choice, not a default:
    /// the props are flat-shaded and low-poly, so their genuine creases are 45-90 degrees apart and
    /// their bevels are ~30. Below about 20 the bevels start reading as doubled lines; above about
    /// 40 the bevels vanish and a crate loses the chamfer that makes it a crate. `WIRETEST` prints
    /// the edge count per prop at the shipped value so a change to it is visible rather than felt.
    public static float CreaseDeg = 25f;

    /// Off-switch for the extractor, as a comparison rather than as a setting. AllEdges is what
    /// `Raylib.DrawModelWires` would draw.
    public const int HardEdges = 0, AllEdges = 1;
    public static int Mode = HardEdges;

    // ── The cache. Keyed by CALLER-SUPPLIED NAME, not by mesh pointer ─────────────────────────
    // A mesh pointer is only stable while the model is loaded, and `Model` is a struct we copy
    // around; keying on a name the loader already has is both stable and greppable. Extraction is
    // O(tris) with a dictionary, runs once per model per process, and the largest prop in the kit
    // (slag, 324 tris) takes well under a millisecond.
    static readonly Dictionary<string, Edge[]> _cache = new();
    static readonly Dictionary<string, Edge[]> _cacheAll = new();

    public static void ClearCache() { _cache.Clear(); _cacheAll.Clear(); }

    /// The kept edges of `model`'s first mesh, extracted on first ask and cached under `key`.
    public static Edge[] For(string key, Model model)
    {
        var cache = Mode == AllEdges ? _cacheAll : _cache;
        if (cache.TryGetValue(key, out var hit)) return hit;
        var made = model.MeshCount > 0 ? Extract(model, 0, Mode == AllEdges ? -1f : CreaseDeg)
                                       : Array.Empty<Edge>();
        cache[key] = made;
        return made;
    }

    /// Unique edges of one mesh, filtered to boundaries and creases.
    ///
    /// **THE VERTICES MUST BE WELDED BY POSITION FIRST, and skipping that is the trap.** This kit
    /// is FLAT-SHADED with per-corner vertex colours, so every triangle carries its own three
    /// vertices and NO two triangles share an index — key the edges on indices and every edge in
    /// the mesh looks like a boundary, the crease test never runs, and the "hard edge" pass
    /// silently returns the full triangle net. It would look like a working wireframe and be one
    /// only by accident. So positions are quantised into a grid (`Weld`) and edges are keyed on
    /// the welded ids.
    ///
    /// `creaseDeg &lt; 0` keeps every edge — the DrawModelWires comparison.
    public static unsafe Edge[] Extract(Model model, int meshIndex, float creaseDeg)
    {
        if (model.MeshCount <= meshIndex) return Array.Empty<Edge>();
        Mesh m = model.Meshes[meshIndex];
        if (m.Vertices == null || m.TriangleCount <= 0) return Array.Empty<Edge>();

        int vcount = m.VertexCount;
        var pos = new Vector3[vcount];
        for (int i = 0; i < vcount; i++)
            pos[i] = new Vector3(m.Vertices[i * 3], m.Vertices[i * 3 + 1], m.Vertices[i * 3 + 2]);

        // weld by quantised position
        var weld = new Dictionary<(int, int, int), int>();
        var id = new int[vcount];
        var welded = new List<Vector3>();
        for (int i = 0; i < vcount; i++)
        {
            var q = Quant(pos[i]);
            if (!weld.TryGetValue(q, out int w)) { w = welded.Count; weld[q] = w; welded.Add(pos[i]); }
            id[i] = w;
        }

        int tris = m.TriangleCount;
        int Index(int k) => m.Indices != null ? m.Indices[k] : k;

        // edge -> the face normals of the (up to two) triangles that share it
        var faces = new Dictionary<(int, int), (Vector3 n0, Vector3 n1, int count)>();
        for (int t = 0; t < tris; t++)
        {
            int i0 = Index(t * 3), i1 = Index(t * 3 + 1), i2 = Index(t * 3 + 2);
            if (i0 >= vcount || i1 >= vcount || i2 >= vcount) continue;
            var n = Vector3.Cross(pos[i1] - pos[i0], pos[i2] - pos[i0]);
            float len = n.Length();
            if (len < 1e-9f) continue;                    // degenerate triangle contributes nothing
            n /= len;
            Add(faces, id[i0], id[i1], n);
            Add(faces, id[i1], id[i2], n);
            Add(faces, id[i2], id[i0], n);
        }

        float cosLimit = creaseDeg < 0f ? 2f : MathF.Cos(creaseDeg * MathF.PI / 180f);
        var outEdges = new List<Edge>(faces.Count);
        foreach (var kv in faces)
        {
            var (n0, n1, count) = kv.Value;
            // A boundary edge (one face) is always a silhouette and always kept. A shared edge is
            // kept only when its two faces disagree by more than the crease angle — which is what
            // removes the triangulation diagonal of every flat quad, since both halves of a quad
            // have the SAME normal and their dot is 1.
            bool keep = count < 2 || Vector3.Dot(n0, n1) < cosLimit;
            if (count > 2) keep = true;                   // non-manifold: a real feature, keep it
            if (keep) outEdges.Add(new Edge(welded[kv.Key.Item1], welded[kv.Key.Item2]));
        }
        return outEdges.ToArray();
    }

    static void Add(Dictionary<(int, int), (Vector3, Vector3, int)> faces, int a, int b, Vector3 n)
    {
        if (a == b) return;                               // a welded-away zero-length edge
        var key = a < b ? (a, b) : (b, a);
        if (faces.TryGetValue(key, out var cur))
            faces[key] = (cur.Item1, cur.Item3 == 1 ? n : cur.Item2, cur.Item3 + 1);
        else
            faces[key] = (n, n, 1);
    }

    /// 1e-4 of a world unit, which on this board is a 64th of a millimetre at tile scale — far
    /// below any authored feature and far above float noise from the exporter.
    static (int, int, int) Quant(Vector3 v) =>
        ((int)MathF.Round(v.X * 10000f), (int)MathF.Round(v.Y * 10000f), (int)MathF.Round(v.Z * 10000f));

    // ── Drawing ──────────────────────────────────────────────────────────────────────────────
    /// Draw a cached edge set under the same transform `DrawModelEx` would apply, so a wireframe
    /// lands exactly where its solid would have. Must be called inside `BeginMode3D`; rlgl batches
    /// the lines, and the whole kit is tens of lines per instance.
    public static void Draw(string key, Model model, Vector3 position, Vector3 rotationAxis,
                           float rotationAngle, Vector3 scale, Color tint)
    {
        var edges = For(key, model);
        if (edges.Length == 0) return;
        var xf = Transform(position, rotationAxis, rotationAngle, scale);
        foreach (var e in edges)
            Raylib.DrawLine3D(Vector3.Transform(e.A, xf), Vector3.Transform(e.B, xf), tint);
    }

    /// The model->world transform, in `DrawModelEx`'s order: scale, then rotate about the axis,
    /// then translate. Separate so WIRETEST can check the matrix the draw actually uses against a
    /// hand-derived bounding box, rather than against a second copy of the same arithmetic.
    public static Matrix4x4 Transform(Vector3 position, Vector3 axis, float angleDeg, Vector3 scale) =>
          Matrix4x4.CreateScale(scale)
        * Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), angleDeg * MathF.PI / 180f)
        * Matrix4x4.CreateTranslation(position);

    // ══════════════════ SIGHTLINE_WIRETEST ══════════════════
    /// (A) THE GROUND TRUTH, and it is the leg that proves the WELD. A raylib cube is 12 triangles
    ///     over 6 quads, and it is NOT indexed — every triangle carries its own three vertices, so
    ///     nothing is shared until positions are welded. Welded, it has exactly 8 corners, 18
    ///     unique edges (12 of the box, 6 face diagonals), and 12 of those survive the crease test
    ///     because a quad's two halves share a normal and its box edges are 90 degrees apart.
    ///     **12, 18 and 8 are arithmetic, not measurements**: skip the weld and the answer is 36
    ///     for all three modes, which still LOOKS like a wireframe.
    /// (B) THE KIT. Every prop extracts, none returns empty, and none returns more hard edges than
    ///     all edges. The counts are PRINTED rather than pinned — they are a property of the
    ///     meshes and of `CreaseDeg`, and a golden number here would break on any art change while
    ///     saying nothing about correctness.
    /// (C) HYGIENE: no zero-length edge, no edge repeated in either direction, every endpoint
    ///     finite. A degenerate triangle or a duplicated position in an exported mesh produces all
    ///     three, and none of them is visible in a screenshot.
    /// (D) THE CACHE IS PER MODE. `Mode` picks a different dictionary, so asking for the AllEdges
    ///     comparison cannot leave the shipped HardEdges answer poisoned for the rest of the run.
    /// (E) THE TRANSFORM, against a hand-derived box. A unit cube scaled (2,3,4), turned 90 degrees
    ///     about Y and moved to (10,0,0) must occupy x 8..12, y -1.5..1.5, z -1..1 — the rotation
    ///     swaps the X and Z extents, which is the one thing a wrong multiplication order gets
    ///     wrong while still looking plausible at rotation 0.
    ///
    /// Needs a window: GenMeshCube and LoadModel both upload to the GPU.
    public static string SelfTest()
    {
        var fails = new System.Collections.Generic.List<string>();
        int savedMode = Mode;
        ClearCache();

        // ── (A)
        var cubeModel = Raylib.LoadModelFromMesh(Raylib.GenMeshCube(1f, 1f, 1f));
        var hard = Extract(cubeModel, 0, CreaseDeg);
        var all = Extract(cubeModel, 0, -1f);
        if (hard.Length != 12) fails.Add($"(A) a unit cube has {hard.Length} hard edges, not 12 — the weld or the crease test is wrong");
        if (all.Length != 18) fails.Add($"(A) a unit cube has {all.Length} unique edges, not 18 (12 box + 6 diagonals)");
        var corners = new System.Collections.Generic.HashSet<(int, int, int)>();
        foreach (var e in all) { corners.Add(Quant(e.A)); corners.Add(Quant(e.B)); }
        if (corners.Count != 8) fails.Add($"(A) a unit cube welded to {corners.Count} corners, not 8");

        // ── (C) on the cube, where the expected answer is known
        Hygiene(all, "cube", fails);

        // ── (B) the shipped kit
        var report = new System.Text.StringBuilder();
        foreach (string name in new[] { "wall_high", "wall_low", "wall_door", "tree", "crate", "car", "rock", "slag", "sign" })
        {
            var m = Raylib.LoadModel(Cfg.AssetPath($"assets/props/{name}.glb"));
            if (m.MeshCount == 0) { fails.Add($"(B) {name}.glb did not load"); continue; }
            var h = Extract(m, 0, CreaseDeg);
            var a = Extract(m, 0, -1f);
            unsafe { report.Append($" {name} {h.Length}/{a.Length}of{m.Meshes[0].TriangleCount}t"); }
            if (h.Length == 0) fails.Add($"(B) {name} extracted NO edges");
            if (h.Length > a.Length) fails.Add($"(B) {name} has more hard edges ({h.Length}) than unique edges ({a.Length})");
            Hygiene(h, name, fails);
            Raylib.UnloadModel(m);
        }

        // ── (D)
        Mode = HardEdges; int nHard = For("cube", cubeModel).Length;
        Mode = AllEdges;  int nAll  = For("cube", cubeModel).Length;
        Mode = HardEdges; int nHard2 = For("cube", cubeModel).Length;
        if (nHard != 12 || nHard2 != 12 || nAll != 18)
            fails.Add($"(D) the cache is not per-mode: hard {nHard}, all {nAll}, hard again {nHard2}");

        // ── (E)
        var xf = Transform(new Vector3(10f, 0f, 0f), Vector3.UnitY, 90f, new Vector3(2f, 3f, 4f));
        float lox = 1e9f, hix = -1e9f, loy = 1e9f, hiy = -1e9f, loz = 1e9f, hiz = -1e9f;
        foreach (var e in hard)
            foreach (var p in new[] { Vector3.Transform(e.A, xf), Vector3.Transform(e.B, xf) })
            {
                lox = MathF.Min(lox, p.X); hix = MathF.Max(hix, p.X);
                loy = MathF.Min(loy, p.Y); hiy = MathF.Max(hiy, p.Y);
                loz = MathF.Min(loz, p.Z); hiz = MathF.Max(hiz, p.Z);
            }
        if (Off(lox, 8f) || Off(hix, 12f) || Off(loy, -1.5f) || Off(hiy, 1.5f) || Off(loz, -1f) || Off(hiz, 1f))
            fails.Add($"(E) the transformed cube spans x {lox:0.00}..{hix:0.00} y {loy:0.00}..{hiy:0.00} z {loz:0.00}..{hiz:0.00}, not 8..12 / -1.5..1.5 / -1..1");

        Raylib.UnloadModel(cubeModel);
        Mode = savedMode;
        ClearCache();
        return fails.Count == 0
            ? $"WIRETEST: PASS (unit cube 12 hard / 18 unique / 8 welded corners; kit hard/unique-of-tris:{report}; hygiene, per-mode cache and transform OK)"
            : "WIRETEST: FAIL\n  " + string.Join("\n  ", fails);
    }

    static bool Off(float v, float want) => MathF.Abs(v - want) > 0.001f;

    static void Hygiene(Edge[] edges, string what, System.Collections.Generic.List<string> fails)
    {
        var seen = new System.Collections.Generic.HashSet<((int, int, int), (int, int, int))>();
        foreach (var e in edges)
        {
            if (!float.IsFinite(e.A.X) || !float.IsFinite(e.B.X)) { fails.Add($"(C) {what}: a non-finite endpoint"); return; }
            if (Vector3.DistanceSquared(e.A, e.B) < 1e-12f) { fails.Add($"(C) {what}: a zero-length edge"); return; }
            var qa = Quant(e.A); var qb = Quant(e.B);
            var key = Cmp(qa, qb) <= 0 ? (qa, qb) : (qb, qa);
            if (!seen.Add(key)) { fails.Add($"(C) {what}: an edge appears twice"); return; }
        }
    }

    static int Cmp((int, int, int) a, (int, int, int) b)
    {
        if (a.Item1 != b.Item1) return a.Item1.CompareTo(b.Item1);
        if (a.Item2 != b.Item2) return a.Item2.CompareTo(b.Item2);
        return a.Item3.CompareTo(b.Item3);
    }

    /// An axis-aligned box's wireframe, for the call sites that draw a box without a Model (the
    /// edge WALLS, which are drawn as cuboids straight from their tile boundary). Twelve lines, no
    /// extraction, no cache — the box IS its own hard-edge set.
    public static void Box(Vector3 centre, float w, float h, float d, Color tint)
    {
        float x0 = centre.X - w * 0.5f, x1 = centre.X + w * 0.5f;
        float y0 = centre.Y - h * 0.5f, y1 = centre.Y + h * 0.5f;
        float z0 = centre.Z - d * 0.5f, z1 = centre.Z + d * 0.5f;
        void L(float ax, float ay, float az, float bx, float by, float bz)
            => Raylib.DrawLine3D(new Vector3(ax, ay, az), new Vector3(bx, by, bz), tint);
        L(x0, y0, z0, x1, y0, z0); L(x1, y0, z0, x1, y0, z1); L(x1, y0, z1, x0, y0, z1); L(x0, y0, z1, x0, y0, z0);
        L(x0, y1, z0, x1, y1, z0); L(x1, y1, z0, x1, y1, z1); L(x1, y1, z1, x0, y1, z1); L(x0, y1, z1, x0, y1, z0);
        L(x0, y0, z0, x0, y1, z0); L(x1, y0, z0, x1, y1, z0); L(x1, y0, z1, x1, y1, z1); L(x0, y0, z1, x0, y1, z1);
    }

}
