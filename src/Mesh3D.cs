using System;
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
    static readonly Vector3 Key = Vector3.Normalize(new Vector3(-0.40f, 0.86f, -0.32f));

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
