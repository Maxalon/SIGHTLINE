using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// ════════════════════ P27 — PROCEDURAL MESHES (no committed art) ════════════════════
/// Generated geometry for the projected view, built at runtime from a 2D profile. Nothing here
/// is loaded from disk, so the project's asset policy is untouched: a "model" is a handful of
/// numbers in source, diffable and tunable, not a binary blob.
///
/// WHY A LATHE IS THE RIGHT TOOL HERE. A soldier chip is rotationally symmetric, so its entire
/// shape IS its side profile — about eight (radius, height) pairs. Revolving that is trigonometry,
/// and it produces something smoother and more precisely proportioned than the same shape modelled
/// by hand would be. Sculpting is the wrong instrument for a machined token.
///
/// AND THE REAL FIX IS LIGHTING, NOT POLYGON COUNT. Raylib's default shader applies no lighting:
/// DrawCube and DrawCylinder paint every face one flat colour, which is why the primitive chip read
/// as a sticker no matter how many slices it had. So the lathe BAKES a directional light into
/// per-vertex colours as greyscale, and the per-draw material tint supplies the team colour (the
/// default shader multiplies texel x material x vertex, so the two compose). Flat-shaded facets,
/// real form, zero shader files.
public static class Mesh3D
{
    /// Fixed key light. Above, slightly to the viewer's left — the same direction the 2D board's
    /// faux-3D cover has always been lit from, so the projected view agrees with the flat one.
    /// P30 — THE Z SIGN WAS WRONG AND IT LIT THE BACK OF THE BOARD.
    /// `View3D.MakeCamera` puts the camera at +Z (dir = (cos p sin y, sin p, cos p cos y), and the
    /// position is target + dir * 40), so the faces a player SEES are the +Z ones. A key with
    /// Z = -0.32 lit the faces pointing AWAY from the camera. The lathed chip survived it because
    /// it is rotationally symmetric and the -0.40 X term still produced a left-right gradient; a
    /// BOX cannot, and every front face would have landed on the ambient floor — measured at
    /// exactly 0.34 x 255 = 87, dead flat, when the same bake was tried offline.
    static readonly Vector3 Key = Vector3.Normalize(new Vector3(-0.40f, 0.86f, 0.32f));

    /// Ambient floor: how lit an unlit face is. Below ~0.3 the underside of a chip goes to mud.
    const float Ambient = 0.34f;

    /// Revolve a profile around the Y axis into a flat-shaded mesh with baked lighting.
    ///
    /// The profile runs bottom-centre -> outward -> up the outside -> back inward across the top,
    /// i.e. counter-clockwise in the (radius, height) plane. That winding is what makes the
    /// computed normal (dy, -dr) point OUT of the solid rather than into it; reverse the profile
    /// and the whole thing lights inside-out.
    ///
    /// Vertices are NOT shared between segments. That is deliberate: a shared vertex averages the
    /// normals of two faces and smooths the edge, and this aesthetic wants crisp facets and a hard
    /// bright lip.
    public static unsafe Mesh Lathe((float r, float y)[] profile, int slices)
    {
        int segs = profile.Length - 1;
        int quads = segs * slices;
        int vCount = quads * 4, tCount = quads * 2;

        var mesh = new Mesh(vCount, tCount);
        mesh.Vertices = (float*)Raylib.MemAlloc((uint)(vCount * 3 * sizeof(float)));
        mesh.Normals = (float*)Raylib.MemAlloc((uint)(vCount * 3 * sizeof(float)));
        mesh.TexCoords = (float*)Raylib.MemAlloc((uint)(vCount * 2 * sizeof(float)));
        mesh.Colors = (byte*)Raylib.MemAlloc((uint)(vCount * 4 * sizeof(byte)));
        mesh.Indices = (ushort*)Raylib.MemAlloc((uint)(tCount * 3 * sizeof(ushort)));

        int vi = 0, ii = 0;
        for (int s = 0; s < segs; s++)
        {
            var (r0, y0) = profile[s];
            var (r1, y1) = profile[s + 1];
            float dr = r1 - r0, dy = y1 - y0;
            float len = MathF.Sqrt(dr * dr + dy * dy);
            if (len < 1e-6f) continue;
            float nr = dy / len, ny = -dr / len;   // outward normal in the (r,y) plane

            for (int k = 0; k < slices; k++)
            {
                float a0 = MathF.Tau * k / slices, a1 = MathF.Tau * (k + 1) / slices;
                float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0);
                float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);
                // one normal for the whole quad — flat shading, and the facet edge stays crisp
                float am = (a0 + a1) * 0.5f;
                var n = new Vector3(nr * MathF.Cos(am), ny, nr * MathF.Sin(am));
                float lit = Ambient + (1f - Ambient) * MathF.Max(0f, Vector3.Dot(n, Key));
                byte g = (byte)Util.Clamp(lit * 255f, 0f, 255f);

                int b = vi;
                Put(mesh, vi++, r0 * c0, y0, r0 * s0, n, g);
                Put(mesh, vi++, r0 * c1, y0, r0 * s1, n, g);
                Put(mesh, vi++, r1 * c1, y1, r1 * s1, n, g);
                Put(mesh, vi++, r1 * c0, y1, r1 * s0, n, g);
                mesh.Indices[ii++] = (ushort)(b + 0); mesh.Indices[ii++] = (ushort)(b + 1); mesh.Indices[ii++] = (ushort)(b + 2);
                mesh.Indices[ii++] = (ushort)(b + 0); mesh.Indices[ii++] = (ushort)(b + 2); mesh.Indices[ii++] = (ushort)(b + 3);
            }
        }
        // Buffers must be FULLY written: an unfilled tail renders as stray triangles. Verified on
        // this profile at 48 slices — 1728/1728 vertices, 2592/2592 indices, bbox x[-0.38,0.38]
        // y[0,0.134] — which is how the wedge artifact was cleared as a geometry fault and traced
        // to draw state instead.
        if (vi != vCount || ii != tCount * 3)
            Console.Error.WriteLine($"LATHE UNDERFILL verts={vi}/{vCount} idx={ii}/{tCount * 3}");
        Raylib.UploadMesh(&mesh, false);
        return mesh;
    }

    // ══════════════════ P30 — THE BEVELLED BLOCK ══════════════════
    /// A box with chamfered edges and a directional light BAKED PER FACE, which is the whole of
    /// docs/ROADMAP.md's top open item. `Raylib.DrawCube` paints every face of a solid the same
    /// flat colour — there is no lighting in the default shader — so terrain built from it reads
    /// as coloured paper no matter how the camera is angled. That is why the projected view looked
    /// 2D: not the camera, the SHADING.
    ///
    /// THE BEVEL IS NOT DECORATION. A chamfer gives every edge a narrow face at a different angle
    /// to the key, so the silhouette catches a highlight and the block reads as a solid with a
    /// direction rather than as a flat region of colour. 6 faces + 12 edge strips + 8 corners =
    /// 44 triangles, the same count Blender's bevel modifier produces for one segment.
    ///
    /// Base sits at y = 0 so a caller places a block by its FOOTPRINT, not by its centre.
    /// `ambient` overrides the module floor. TERRAIN wants a higher one than the chip does: the
    /// chip is a small object read against a flat board and can afford a deep unlit side, while a
    /// cover block's dark faces are most of what the player looks at. At the chip's 0.34 the side
    /// of a block landed at 0.34 x its tint, well UNDER the flat colour the old two-cube draw used
    /// — better form, worse legibility. 0.52 keeps the modelling and puts the dark faces back
    /// roughly where the eye expects them.
    public static unsafe Mesh BevelBox(float w, float h, float d, float bevel, float ambient = -1f)
    {
        float bx = MathF.Min(bevel, w * 0.49f), by = MathF.Min(bevel, h * 0.49f), bz = MathF.Min(bevel, d * 0.49f);
        float hw = w * 0.5f, hd = d * 0.5f;
        // inner corner grid: the eight points the chamfer is cut back to
        Vector3 P(int sx, int sy, int sz) => new(sx * (hw - bx), sy > 0 ? h - by : by, sz * (hd - bz));

        var verts = new List<(Vector3 p, Vector3 n)>();
        var tris = new List<int>();
        // WINDING IS ASSERTED, NOT HAND-CHECKED. A bevelled box is 26 faces built from three
        // sign loops, and getting the vertex ORDER right for every one of them by inspection is a
        // job nobody does correctly: the first version had the two Y faces and half the X-Z edge
        // strips wound backwards, so OpenGL back-face-culled them and a block rendered as a dark
        // hole with a lit rim round it. Since each face already declares the normal it WANTS, the
        // geometric normal of the winding can simply be compared against it and the order flipped
        // when they disagree. Self-correcting, and it cannot rot as faces are added.
        void Quad(Vector3 a, Vector3 b2, Vector3 c, Vector3 e, Vector3 n)
        {
            if (Vector3.Dot(Vector3.Cross(b2 - a, c - b2), n) < 0f) (a, b2, c, e) = (e, c, b2, a);
            int i0 = verts.Count;
            verts.Add((a, n)); verts.Add((b2, n)); verts.Add((c, n)); verts.Add((e, n));
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
            tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 3);
        }
        void Tri(Vector3 a, Vector3 b2, Vector3 c, Vector3 n)
        {
            if (Vector3.Dot(Vector3.Cross(b2 - a, c - b2), n) < 0f) (a, c) = (c, a);
            int i0 = verts.Count;
            verts.Add((a, n)); verts.Add((b2, n)); verts.Add((c, n));
            tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
        }
        // push an inner corner out onto one of the three face planes
        Vector3 OnX(int sx, int sy, int sz) { var v = P(sx, sy, sz); v.X = sx * hw; return v; }
        Vector3 OnY(int sx, int sy, int sz) { var v = P(sx, sy, sz); v.Y = sy > 0 ? h : 0f; return v; }
        Vector3 OnZ(int sx, int sy, int sz) { var v = P(sx, sy, sz); v.Z = sz * hd; return v; }

        for (int s = -1; s <= 1; s += 2)
        {
            var nx = new Vector3(s, 0, 0);
            Quad(OnX(s, -1, -s), OnX(s, 1, -s), OnX(s, 1, s), OnX(s, -1, s), nx);
            var nz = new Vector3(0, 0, s);
            Quad(OnZ(s, -1, s), OnZ(s, 1, s), OnZ(-s, 1, s), OnZ(-s, -1, s), nz);
            var ny = new Vector3(0, s, 0);
            Quad(OnY(-1, s, -s), OnY(1, s, -s), OnY(1, s, s), OnY(-1, s, s), ny);
        }
        // 12 edge strips
        for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
        {
            var n = Vector3.Normalize(new Vector3(sx, 0, sz));
            Quad(OnX(sx, -1, sz), OnX(sx, 1, sz), OnZ(sx, 1, sz), OnZ(sx, -1, sz), n);
        }
        for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2)
        {
            var n = Vector3.Normalize(new Vector3(sx, sy, 0));
            Quad(OnX(sx, sy, -1), OnX(sx, sy, 1), OnY(sx, sy, 1), OnY(sx, sy, -1), n);
        }
        for (int sz = -1; sz <= 1; sz += 2) for (int sy = -1; sy <= 1; sy += 2)
        {
            var n = Vector3.Normalize(new Vector3(0, sy, sz));
            Quad(OnZ(-1, sy, sz), OnZ(1, sy, sz), OnY(1, sy, sz), OnY(-1, sy, sz), n);
        }
        // 8 corners
        for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2) for (int sz = -1; sz <= 1; sz += 2)
            Tri(OnX(sx, sy, sz), OnY(sx, sy, sz), OnZ(sx, sy, sz), Vector3.Normalize(new Vector3(sx, sy, sz)));

        var mesh = new Mesh(verts.Count, tris.Count / 3);
        mesh.Vertices = (float*)Raylib.MemAlloc((uint)(verts.Count * 3 * sizeof(float)));
        mesh.Normals = (float*)Raylib.MemAlloc((uint)(verts.Count * 3 * sizeof(float)));
        mesh.TexCoords = (float*)Raylib.MemAlloc((uint)(verts.Count * 2 * sizeof(float)));
        mesh.Colors = (byte*)Raylib.MemAlloc((uint)(verts.Count * 4 * sizeof(byte)));
        mesh.Indices = (ushort*)Raylib.MemAlloc((uint)(tris.Count * sizeof(ushort)));
        for (int i = 0; i < verts.Count; i++)
        {
            var (pv, nv) = verts[i];
            float amb = ambient < 0f ? Ambient : ambient;
            float lit = amb + (1f - amb) * MathF.Max(0f, Vector3.Dot(nv, Key));
            Put(mesh, i, pv.X, pv.Y, pv.Z, nv, (byte)Util.Clamp(lit * 255f, 0f, 255f));
        }
        for (int i = 0; i < tris.Count; i++) mesh.Indices[i] = (ushort)tris[i];
        Raylib.UploadMesh(&mesh, false);
        return mesh;
    }

    static unsafe void Put(Mesh m, int i, float x, float y, float z, Vector3 n, byte g)
    {
        m.Vertices[i * 3 + 0] = x; m.Vertices[i * 3 + 1] = y; m.Vertices[i * 3 + 2] = z;
        m.Normals[i * 3 + 0] = n.X; m.Normals[i * 3 + 1] = n.Y; m.Normals[i * 3 + 2] = n.Z;
        m.TexCoords[i * 2 + 0] = 0f; m.TexCoords[i * 2 + 1] = 0f;
        m.Colors[i * 4 + 0] = g; m.Colors[i * 4 + 1] = g; m.Colors[i * 4 + 2] = g; m.Colors[i * 4 + 3] = 255;
    }

    /// THE SOLDIER CHIP, as a side profile. Read it bottom to top: a flat underside, a chamfer, the
    /// outer wall, a rolled top edge, the raised lip, then a step down into the recessed face the
    /// marker is printed on. Every one of those numbers is a design decision you can turn — which is
    /// the argument for generating the shape rather than sculpting it.
    public static readonly (float r, float y)[] ChipProfile =
    {
        (0.000f, 0.000f),
        (0.300f, 0.000f),   // underside
        (0.362f, 0.022f),   // bottom chamfer
        (0.380f, 0.052f),   // outer wall, lower
        (0.380f, 0.098f),   // outer wall, upper
        (0.366f, 0.124f),   // rolled top edge
        (0.336f, 0.134f),   // the lip
        (0.306f, 0.121f),   // step down
        (0.286f, 0.116f),   // recess wall
        (0.000f, 0.116f),   // recessed face
    };
}
