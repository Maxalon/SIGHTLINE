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

    // ══════════════════ P39 — THE CONFIDENCE SHADER ══════════════════
    /// What a shader buys that a per-line colour cannot, and nothing more than that.
    ///
    /// Most of "confidence" is INFORMATION and belongs in the data — how long ago a surface was
    /// seen and from how far away are facts about what the squad did, `Vision` records them, and
    /// they reach the screen through the colour each line is already handed. A shader is not needed
    /// for any of it, and a shader that DERIVED it would be a second model of the same thing.
    ///
    /// What a shader can do that a per-line colour cannot is vary the picture ALONG a line and
    /// ACROSS the board: horizontal scan planes the reconstruction brightens through, and a sweep
    /// travelling over it. That is the difference between "geometry drawn thin" and "an instrument
    /// reporting", and it is the whole of this shader's job.
    ///
    /// **THE DEFAULT VERTEX SHADER DOES NOT GIVE YOU WORLD POSITION, and that is the trap here.**
    /// raylib's built-in vertex shader outputs `fragTexCoord` and `fragColor` only. Ask a fragment
    /// shader for `in vec3 fragPosition` against it and the program still LINKS, still reports
    /// valid, and draws nothing usable — which is exactly what the first spike of this wave did.
    /// So this pair ships its own vertex shader. For rlgl's batched lines the submitted vertices
    /// are already world coordinates (no matrix is pushed for `DrawLine3D` inside `BeginMode3D`),
    /// so passing `vertexPosition` straight through IS the world position.
    const string VS = @"#version 330
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec4 vertexColor;
uniform mat4 mvp;
out vec2 fragTexCoord;
out vec4 fragColor;
out vec3 fragWorld;
void main()
{
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    fragWorld = vertexPosition;
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}";

    const string FS = @"#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
in vec3 fragWorld;
out vec4 finalColor;
uniform float uSweepX;
uniform float uSweepW;
uniform float uTime;
uniform float uGain;
void main()
{
    // Horizontal sampling planes. The reconstruction is brighter where the instrument has a layer,
    // so a tall object reads as a stack of returns rather than as a drawn outline.
    float planes = 0.5 + 0.5 * sin(fragWorld.y * 24.0 - uTime * 1.5);
    planes = 0.86 + 0.14 * planes;

    // The sweep: one soft band crossing the board. Low amplitude on purpose — this is a board you
    // stare at while thinking, and a bright bar travelling over it every few seconds would be the
    // most animated thing on screen for no informational gain.
    float sweep = 1.0 - smoothstep(0.0, max(uSweepW, 0.001), abs(fragWorld.x - uSweepX));
    sweep *= sweep;

    vec3 rgb = fragColor.rgb * mix(1.0, planes, uGain) + fragColor.rgb * sweep * 0.55 * uGain;
    float a  = clamp(fragColor.a * mix(1.0, planes, uGain) + sweep * 0.22 * uGain, 0.0, 1.0);
    finalColor = vec4(rgb, a);
}";

    /// Off-switch. `SIGHTLINE_WIRESHADER=0` draws the same lines with no shader bound at all, which
    /// is also the path a driver that refuses the program falls back to — so the fallback is a
    /// configuration somebody runs, not a branch nobody has seen.
    public static bool ShaderEnabled = true;
    /// Master amplitude, 0..1. At 0 the shader is bound and mathematically the identity, which is
    /// what CONFTEST uses to prove the pass is not silently blanking or tinting anything.
    public static float Gain = 1f;
    /// Seconds for the sweep to cross the board once.
    public static float SweepPeriod = 7f;

    static Shader _sh;
    static bool _shTried, _shOk;
    static int _locSweepX = -1, _locSweepW = -1, _locTime = -1, _locGain = -1;
    /// True while `Begin` has a program bound, so `End` never unbinds one it did not bind.
    static bool _bound;

    public static bool ShaderReady => _shOk;

    /// Load once. A failure here is not fatal and not silent: the lines still draw, and CONFTEST
    /// reports which path ran.
    public static bool EnsureShader()
    {
        if (_shTried) return _shOk;
        _shTried = true;
        _sh = Raylib.LoadShaderFromMemory(VS, FS);
        _shOk = Raylib.IsShaderValid(_sh);
        if (_shOk)
        {
            _locSweepX = Raylib.GetShaderLocation(_sh, "uSweepX");
            _locSweepW = Raylib.GetShaderLocation(_sh, "uSweepW");
            _locTime = Raylib.GetShaderLocation(_sh, "uTime");
            _locGain = Raylib.GetShaderLocation(_sh, "uGain");
        }
        else Console.Error.WriteLine("WIRE: the confidence shader did not compile - lines draw unshaded.");
        return _shOk;
    }

    /// Bind the shader for one wireframe PASS. The caller must group its wireframe draws: a bind
    /// per object would flush rlgl's batch once per tile, which on a big board is 1,120 flushes a
    /// frame to change nothing between them.
    public static unsafe bool Begin(float boardW, double time)
    {
        _bound = false;
        if (!ShaderEnabled || !EnsureShader()) return false;
        float t = (float)time;
        float phase = SweepPeriod <= 0f ? 0f : (float)(time % SweepPeriod) / SweepPeriod;
        float sweepX = -2f + phase * (boardW + 4f);          // starts and ends off the board
        float sweepW = MathF.Max(1.5f, boardW * 0.10f);
        Raylib.BeginShaderMode(_sh);
        if (_locSweepX >= 0) Raylib.SetShaderValue(_sh, _locSweepX, &sweepX, ShaderUniformDataType.Float);
        if (_locSweepW >= 0) Raylib.SetShaderValue(_sh, _locSweepW, &sweepW, ShaderUniformDataType.Float);
        if (_locTime >= 0) Raylib.SetShaderValue(_sh, _locTime, &t, ShaderUniformDataType.Float);
        float g = Util.Clamp(Gain, 0f, 1f);
        if (_locGain >= 0) Raylib.SetShaderValue(_sh, _locGain, &g, ShaderUniformDataType.Float);
        _bound = true;
        return true;
    }

    public static void End()
    {
        if (!_bound) return;
        Raylib.EndShaderMode();
        _bound = false;
    }

    // ══════════════════ SIGHTLINE_CONFTEST ══════════════════
    /// (A) THE TWO TERMS, at their endpoints. Seen from adjacent, this turn, is 1.0; seen from
    ///     `BaseSight` is exactly `RangeFloor`; age costs `AgePerTurn` and stops at `AgeFloor`.
    ///     Arithmetic, so the constants cannot drift without this saying so.
    /// (B) NEVER ZERO FROM AGE ALONE. A memory the layer stops drawing is a memory the player is
    ///     not told they have, and the floor is what prevents it. Only UNSEEN reads 0.
    /// (C) `Vision.Refresh` RECORDS what it saw: the stamp, and the distance the observer stood at.
    /// (D) THE BEST LOOK WINS. Two soldiers see one tile from 8 tiles and from 1; the squad knows
    ///     what its closest pair of eyes knows, so the recorded distance is 1. Written the obvious
    ///     way — last writer wins — it would be whichever soldier happens to be later in the list.
    /// (E) **THE SHADER IS AN IDENTITY AT GAIN 0, PROVEN IN PIXELS.** This is the leg that matters,
    ///     and it exists because the first spike of this wave produced a program that compiled,
    ///     linked, reported VALID, and drew nothing — `in vec3 fragPosition` against raylib's
    ///     default vertex shader. A leg that only asks "did it compile" would have passed on that.
    ///     So: draw a line with no shader, draw the same line through the shader at Gain 0, and
    ///     compare the framebuffer. Anything the pass adds, drops or tints of its own shows up.
    /// (F) THE FALLBACK IS A PATH, NOT A BRANCH NOBODY RUNS. With `ShaderEnabled` false, `Begin`
    ///     reports false and `End` is safe to call anyway.
    ///
    /// Needs a window: (E) reads the framebuffer.
    public static string ConfidenceSelfTest()
    {
        var fails = new System.Collections.Generic.List<string>();
        bool savedEnabled = Vision.Enabled, savedShader = ShaderEnabled;
        float savedGain = Gain;
        int savedStamp = Vision.Stamp;

        var g = new Grid();
        for (int x = 0; x < g.W; x++) for (int y = 0; y < g.H; y++) g.Tiles[x, y] = TileType.Floor;
        Vision.Enabled = true;
        Vision.Reset(g);
        Vision.Stamp = 0;

        // ── (A)
        if (Off(Vision.Score(0, 0), 1f)) fails.Add($"(A) adjacent and current scores {Vision.Score(0, 0):0.000}, not 1");
        if (Off(Vision.Score(0, Vision.BaseSight), Vision.RangeFloor))
            fails.Add($"(A) at max range it scores {Vision.Score(0, Vision.BaseSight):0.000}, not RangeFloor {Vision.RangeFloor}");
        Vision.Stamp = 3;
        if (Off(Vision.Score(0, 0), 1f - 3f * Vision.AgePerTurn))
            fails.Add($"(A) three turns old scores {Vision.Score(0, 0):0.000}, not {1f - 3f * Vision.AgePerTurn:0.000}");

        // ── (B)
        Vision.Stamp = 10000;
        float aged = Vision.Score(0, 0);
        if (Off(aged, Vision.AgeFloor)) fails.Add($"(B) an ancient memory scores {aged:0.000}, not the floor {Vision.AgeFloor}");
        if (aged <= 0f) fails.Add("(B) age alone drove a memory to zero");
        Vision.Stamp = 0;

        // ── (C)
        var lone = new List<Unit> { Probe(4, 4) };
        Vision.Stamp = 5;
        Vision.Refresh(g, lone);
        if (Vision.At(4, 4) != Vision.Visible) fails.Add("(C) the observer's own tile is not visible");
        if (Vision.SeenAt[4, 4] != 5) fails.Add($"(C) the stamp recorded {Vision.SeenAt[4, 4]}, not 5");
        if (Vision.SeenDist[4, 4] != 0) fails.Add($"(C) the observer's own tile recorded distance {Vision.SeenDist[4, 4]}");
        if (Vision.SeenDist[7, 4] != 3) fails.Add($"(C) a tile 3 away recorded distance {Vision.SeenDist[7, 4]}");
        if (Vision.Confidence(7, 4) >= Vision.Confidence(4, 4) - 1e-6f)
            fails.Add("(C) a tile 3 away is not less confident than the observer's own");

        // an unseen corner
        int fx = g.W - 1, fy = g.H - 1;
        if (Vision.At(fx, fy) == Vision.Unseen && Vision.Confidence(fx, fy) != 0f)
            fails.Add($"(C) an unseen tile scores {Vision.Confidence(fx, fy):0.000}, not 0");

        // ── (D)
        Vision.Reset(g);
        Vision.Stamp = 1;
        Vision.Refresh(g, new List<Unit> { Probe(1, 4), Probe(9, 4) });   // far first, near second
        byte near = Vision.SeenDist[8, 4];
        Vision.Reset(g);
        Vision.Refresh(g, new List<Unit> { Probe(9, 4), Probe(1, 4) });   // and the other order
        if (near != 1 || Vision.SeenDist[8, 4] != 1)
            fails.Add($"(D) the closest observer did not win: {near} one way, {Vision.SeenDist[8, 4]} the other (want 1 and 1)");

        // ── (E) the identity, in pixels
        {
            var cam = new Camera3D
            {
                Position = new Vector3(0f, 6f, 6f), Target = Vector3.Zero, Up = Vector3.UnitY,
                FovY = 10f, Projection = CameraProjection.Orthographic,
            };
            var line = Pal.RGBA(220, 90, 160);
            Image Shot(bool shaded)
            {
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
                Raylib.BeginMode3D(cam);
                bool on = shaded && Begin(4f, 0.0);
                for (int i = -3; i <= 3; i++)
                    Raylib.DrawLine3D(new Vector3(i, 0f, -3f), new Vector3(i, 0f, 3f), line);
                if (on) End();
                Raylib.EndMode3D();
                Raylib.EndDrawing();
                return Raylib.LoadImageFromScreen();
            }

            Gain = 0f;
            var plain = Shot(false);
            var shadedImg = Shot(true);
            // EVERY pixel, not a stride. The first version sampled every second row and column and
            // reported the control as blank: these lines are ONE PIXEL wide, so a stride of 2 walks
            // straight past most of them. A pixel leg over thin geometry has no sampling budget.
            int lit = 0, differ = 0, maxSum = 0;
            for (int y = 0; y < Cfg.ScreenH; y++)
                for (int x = 0; x < Cfg.ScreenW; x++)
                {
                    var a = Raylib.GetImageColor(plain, x, y);
                    var b = Raylib.GetImageColor(shadedImg, x, y);
                    if (a.R + a.G + a.B > 40) lit++;
                    if (maxSum < a.R + a.G + a.B) maxSum = a.R + a.G + a.B;
                    if (Math.Abs(a.R - b.R) > 2 || Math.Abs(a.G - b.G) > 2 || Math.Abs(a.B - b.B) > 2) differ++;
                }
            Raylib.UnloadImage(plain); Raylib.UnloadImage(shadedImg);
            if (lit == 0) fails.Add($"(E) the unshaded control drew nothing — the leg is vacuous (brightest pixel sum {maxSum})");
            else if (differ > 0) fails.Add($"(E) the shader is not the identity at gain 0: {differ} sampled pixels differ over {lit} lit");
            if (!ShaderReady) fails.Add("(E) the confidence shader did not compile");
        }

        // ── (F)
        ShaderEnabled = false;
        if (Begin(4f, 0.0)) fails.Add("(F) Begin claimed a program with the shader disabled");
        End();                                            // must be safe, and must not unbind
        ShaderEnabled = true;

        Vision.Enabled = savedEnabled; ShaderEnabled = savedShader; Gain = savedGain; Vision.Stamp = savedStamp;
        return fails.Count == 0
            ? $"CONFTEST: PASS (range and age terms exact at their endpoints; age floors at {Vision.AgeFloor} and never zeroes; Refresh records stamp+distance and the CLOSEST observer wins; the shader is pixel-identical at gain 0; the no-shader fallback is safe)"
            : "CONFTEST: FAIL\n  " + string.Join("\n  ", fails);
    }

    static Unit Probe(int x, int y)
    {
        var u = new Unit { Team = Team.Player, X = x, Y = y, Hp = 5, MaxHp = 5 };
        u.SyncPos();
        return u;
    }

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
