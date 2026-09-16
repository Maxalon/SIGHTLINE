using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// ════════════════════ P27 PROTOTYPE — THE PROJECTED VIEW ════════════════════
/// A 3D camera over the EXISTING flat board. This is the cheap half of the "should SIGHTLINE
/// become a 3D tactics game?" question, built to be looked at and thrown away if it does not
/// convince: it changes NO data model, NO gameplay, and nothing outside this file.
///
/// THE ARCHITECTURE THAT MAKES IT CHEAP. The frame splits in two:
///   * TERRAIN goes 3D — floor grid, cover, plateaus and barrels as extruded boxes inside a
///     Camera3D pass. That is the ~30 draw sites below.
///   * EVERYTHING ELSE STAYS 2D — units, glyphs, rings, labels — drawn AFTER EndMode3D at a
///     screen position from Raylib.GetWorldToScreen. Renderer.cs has ~480 raw 2D draw calls and
///     they survive a move to 3D unchanged; only the ~52 tile->pixel conversion sites become
///     world->screen ones. That is why this is a view change and not a renderer rewrite.
///
/// AND THE ART DIRECTION PAYS FOR THE HARD PART. 2D drawn after a 3D pass is not depth-tested, so
/// a unit behind a building draws THROUGH it. Under any other art direction that is a bug needing
/// manual occlusion. For a HOLOGRAM — a tactical projection of the mission ground — a contact
/// reading faintly through a wall is what the thing would actually do. The stalk under each marker
/// (DrawOverlay) exists for the same reason: on a projected view you cannot tell a unit standing
/// ON a plateau from one standing BEHIND it, and a vertical tether to the floor resolves it
/// instantly. That is the readability question this prototype is FOR.
///
/// ORTHOGRAPHIC, not perspective: a tactics grid wants every tile the same size wherever it is.
///
/// Nothing here runs unless View3D.Enabled is set, and only SIGHTLINE_VIEW3DSHOT sets it.
public static class View3D
{
    /// Master gate. Default FALSE and never set by normal play or by any other harness hook, so
    /// every existing screenshot, self-test and balance run is untouched by construction.
    public static bool Enabled;

    /// Camera elevation above the horizon, degrees. 90 = straight down (today's game), 0 = ground
    /// level. The whole point of the prototype is that we do not know what this should be.
    public static float PitchDeg = 52f;

    /// Rotation around the board's vertical axis, degrees. 0 = today's orientation (north up).
    public static float YawDeg = 0f;

    /// Framing slack. 1.0 fits the board's rotated bounding box exactly; >1 pulls back.
    public static float Margin = 1.04f;

    // ── P33: CAMERA CONTROL ──────────────────────────────────────────────────────────────────
    /// Zoom, as a divisor on the fitted extent. 1 = the whole board framed (what MakeCamera
    /// computes); higher = closer. The board ALWAYS fits at Zoom 1 by construction, which is why
    /// a big map needs no pan until you have zoomed into it.
    public static float Zoom = 1f;
    public const float ZoomMin = 1f, ZoomMax = 3.5f;

    /// Ground-plane offset of the camera's target, in world units (tiles).
    public static Vector2 Pan;

    public const float PitchMin = 18f, PitchMax = 82f;

    /// Back to the framing every mission opens on. Bound to the same C the flat camera resets with,
    /// so one key means "show me the board again" in either projection.
    public static void ResetCamera() { PitchDeg = 52f; YawDeg = 0f; Zoom = 1f; Pan = Vector2.Zero; }

    /// How far the target may stray and still leave the view full of board.
    ///
    /// At zoom z the camera sees 1/z of the fitted extent, so the target can range over
    /// (span - span/z) / 2 on each axis — EXACT at yaw 0 and a close approximation as the board
    /// rotates under it. At zoom 1 it collapses to zero, which is correct rather than a special
    /// case: the whole board is already on screen and there is nothing to pan to.
    public static void ClampPan(Grid g)
    {
        float sx = MathF.Max(0f, g.W * 0.5f * (1f - 1f / MathF.Max(Zoom, 0.01f)));
        float sz = MathF.Max(0f, g.H * 0.5f * (1f - 1f / MathF.Max(Zoom, 0.01f)));
        Pan = new Vector2(Util.Clamp(Pan.X, -sx, sx), Util.Clamp(Pan.Y, -sz, sz));
    }

    /// Screen-space drag -> ground-plane pan. A drag has to move the BOARD under the cursor, not
    /// the camera in some unrelated frame, so the delta is rotated into the yaw the player is
    /// actually looking along and un-foreshortened by the pitch. Without the pitch term a drag
    /// away from the camera moves the board far less than the hand does, which reads as the pan
    /// "sticking" at steep angles.
    ///
    /// THE ROTATION IS THE INVERSE, NOT THE ROTATION. The camera's screen-right axis on the ground
    /// plane is (cos yaw, -sin yaw) and its screen-up axis is -sin(pitch) * (sin yaw, cos yaw), so
    /// turning a SCREEN delta back into a WORLD one is solving that pair — which is R transposed.
    /// Written as R it is exactly right at yaw 0 and at yaw 180, and wrong everywhere else; that is
    /// why PICKTEST leg (B) drags at four yaws and not at one.
    public static void DragPan(Grid g, Vector2 screenDelta, float pxPerUnit)
    {
        float yaw = YawDeg * MathF.PI / 180f;
        float pitch = MathF.Max(0.2f, PitchDeg * MathF.PI / 180f);
        float dx = -screenDelta.X / pxPerUnit;
        float dz = -screenDelta.Y / pxPerUnit / MathF.Sin(pitch);
        Pan += new Vector2( dx * MathF.Cos(yaw) + dz * MathF.Sin(yaw),
                           -dx * MathF.Sin(yaw) + dz * MathF.Cos(yaw));
        ClampPan(g);
    }

    /// Screen pixels per world unit at the camera's current framing. Orthographic FovY IS the
    /// vertical extent in world units, so this is one division — but it has to be taken from the
    /// LIVE camera rather than recomputed, or a drag disagrees with the frame it is dragging.
    public static float PixelsPerUnit(Camera3D cam) => Cfg.ScreenH / MathF.Max(cam.FovY, 0.001f);

    // ── World mapping ────────────────────────────────────────────────────────────────────────
    // Tile (x,y) occupies the unit square [x,x+1] x [y,y+1] on the XZ plane; +Y is up. One tile is
    // one world unit, so a height of 1.0 is exactly one tile wide — the proportion a person reads
    // as "chest high" on a grid this size.
    const float LowH = 0.45f;    // low cover: hip height, you can see over it
    const float HighH = 1.15f;   // high cover: taller than a soldier — the only sight blocker
    const float TierH = 0.5f;    // one elevation tier
    const float CapH = 0.06f;    // the lit top plate that makes a box read as a solid
    const float ChipR = 0.38f;      // soldier chip radius
    const float ChipH = 0.13f;      // chip thickness
    const float ChipFloat = 0.30f;  // how far the chip hovers above its tile — the GAP is what
                                    // makes it read as a piece resting on the projection

    public static Vector3 TileWorld(int x, int y, float h = 0f) => new Vector3(x + 0.5f, h, y + 0.5f);

    /// Frame the whole board for the current pitch/yaw. Orthographic FovY is the VERTICAL extent in
    /// world units; the horizontal extent is FovY * aspect. The board's footprint rotates with yaw,
    /// so the bounding box has to be recomputed per angle or the board drifts out of frame at 45.
    public static Camera3D MakeCamera(Grid g, float aspect)
    {
        float pitch = PitchDeg * MathF.PI / 180f;
        float yaw = YawDeg * MathF.PI / 180f;
        var target = new Vector3(g.W * 0.5f + Pan.X, (HighH + TierH) * 0.35f, g.H * 0.5f + Pan.Y);

        float cs = MathF.Abs(MathF.Cos(yaw)), sn = MathF.Abs(MathF.Sin(yaw));
        float spanX = g.W * cs + g.H * sn;        // screen-horizontal footprint after rotation
        float spanZ = g.W * sn + g.H * cs;        // footprint running away from the camera
        // Depth compresses by sin(pitch) on screen; add headroom for the tallest geometry.
        float needV = spanZ * MathF.Sin(pitch) + (HighH + TierH * 2f) * MathF.Cos(pitch) + 1.0f;
        float needH = spanX + 1.0f;
        float fovY = MathF.Max(needV, needH / MathF.Max(aspect, 0.01f)) * Margin
                   / Util.Clamp(Zoom, ZoomMin, ZoomMax);

        var dir = new Vector3(MathF.Cos(pitch) * MathF.Sin(yaw),
                              MathF.Sin(pitch),
                              MathF.Cos(pitch) * MathF.Cos(yaw));
        return new Camera3D
        {
            Position = target + dir * 40f,   // ortho: distance affects only clipping/depth precision
            Target = target,
            Up = new Vector3(0, 1, 0),
            FovY = fovY,
            Projection = CameraProjection.Orthographic,
        };
    }

    static Color Fade(Color c, float a) => Raylib.Fade(c, a);

    // ── The 3D pass: terrain only ────────────────────────────────────────────────────────────
    /// FACE SHADING IS FAKED ON PURPOSE. Raylib's DrawCube takes ONE colour and does no lighting, so
    /// a plain cube renders every face identically and reads as a flat card — which defeats the
    /// entire point of an angled camera. The fix is the same trick the 2D renderer already uses, and
    /// the palette was already built for it: every cover and plateau colour ships as a SIDE/TOP pair
    /// (CoverHi/CoverHiTop, HighSide/HighA). So each solid is drawn as a dark body in the side
    /// colour plus a thin bright CAP in the top colour, and the volume reads instantly.
    /// P30 — ONE LIT, BEVELLED MESH instead of two flat cubes.
    ///
    /// This used to be `DrawCube` for the body plus a second, brighter `DrawCube` sitting on top
    /// faking a lit cap. Raylib's default shader applies NO lighting, so every face of a cube is
    /// the same flat colour and terrain built from it reads as coloured paper however the camera
    /// is angled. That — not the camera — is why the projected view still looked 2D.
    ///
    /// `Mesh3D.BevelBox` bakes the key light into per-vertex colours, and the default shader
    /// multiplies texel x material x vertex, so the per-draw tint still supplies the biome/team
    /// colour on top. The chamfer is what makes the silhouette catch a highlight.
    ///
    /// EVERY CALL SITE IS UNCHANGED: same centre-and-size signature, so plateaus, cover, barrels
    /// and anything added later all gain the lighting through this one chokepoint. `top` becomes
    /// the tint (the bake only ever DARKENS, so tinting with the brighter of the pair lands the
    /// lit face where the old flat cap sat); `side` and `capA` are kept for signature
    /// compatibility and are deliberately unused.
    static void Solid(Vector3 centre, float w, float h, float d, Color side, Color top, float capA = 1f)
    {
        // P38 — the remembered tier. A cover block's hard-edge set would include its BEVEL (each
        // chamfer face sits ~45 degrees off its neighbours), which draws every silhouette twice
        // and reads as a doubled line rather than a scanned box. The box's twelve edges are what a
        // scan of a box actually tells you, so this primitive states them directly instead of
        // extracting them from a mesh whose chamfer is a lighting device.
        if (_wire) { Wire.Box(centre, w, h, d, WireTint(_wireDim)); return; }
        // The bake only ever darkens, so tint with a LIFT: the lit top face then lands where the
        // old flat cap sat instead of a third under it.
        var tint = Pal.RGBA(Math.Min(255, top.R * 5 / 4), Math.Min(255, top.G * 5 / 4),
                            Math.Min(255, top.B * 5 / 4), top.A);
        Raylib.DrawModelEx(Blocks, centre with { Y = centre.Y - h * 0.5f }, Vector3.UnitY, 0f,
                           new Vector3(w, h, d), tint);
    }

    // The unit block, built once on first use because UploadMesh needs a live GL context. Scaled
    // per draw: the lighting is BAKED into the vertices, so a non-uniform scale cannot break the
    // shading the way it would break a runtime normal.
    /// Scale a colour toward black by the knowledge factor. REMEMBERED terrain is DIMMED, never
    /// recoloured: brightness alone then carries "how well do we know this", which leaves hue free
    /// to mean something else later.
    static Color Known(Color c, float f) =>
        Pal.RGBA((int)(c.R * f), (int)(c.G * f), (int)(c.B * f), c.A);

    // ── P38: THE REMEMBERED TIER, AS ONE FLAG ────────────────────────────────────────────────
    /// Set for the duration of one object's draw; every solid primitive below reads it and lands
    /// as LINES instead of a lit body. ONE SEAM rather than a branch at each call site, for the
    /// same reason `Game.PickTile` is one seam: a second draw site added later gets the tier free
    /// and cannot forget it.
    static bool _wire;
    /// The tier's own brightness, carried beside the flag. `Solid` cannot derive it: `Known()`
    /// scales a colour's RGB and leaves its ALPHA alone, so the alpha a primitive receives says
    /// nothing about how well the surface is known. Reading it from there happens to work today
    /// only because `_wire` is set for exactly one tier — which is the kind of accident that comes
    /// apart the moment a second one wants lines.
    static float _wireDim = 1f;

    /// A remembered surface does not take the biome's material — it never had one, because nobody
    /// has light on it. It takes the SCAN's colour: the room's own edge hue pulled most of the way
    /// to a cold instrument blue, so a remembered board still reads as that place while reading as
    /// a reconstruction of it rather than a view of it.
    /// P39 — a remembered line's brightness IS the squad's confidence in it, and the number comes
    /// from what the squad did: how close it stood and how long ago (`Vision.Confidence`). A
    /// low-confidence line also loses saturation, because two cues beat one for anybody reading
    /// this at a glance or without full colour vision — and the range is floored well above zero,
    /// since a memory this layer stops drawing is a memory the player is not told they have.
    static Color WireTint(float conf)
    {
        float k = Util.Clamp(conf, 0f, 1f);
        var hot = Pal.Mix(Scene.Edge, Pal.RGBA(150, 214, 240), 0.72f);
        var cold = Pal.Mix(hot, Pal.RGBA(96, 116, 132), 0.55f);      // less sure -> less colour
        return Fade(Pal.Mix(cold, hot, k), 0.34f + 0.62f * k);
    }

    /// THE DISCIPLINE, since this is a mutable flag read by a primitive: every site that SETS it
    /// clears it on the same straight line, with no `return` or `continue` between the two. The
    /// obvious alternative — a scope helper taking a lambda — allocates a closure per TILE per
    /// FRAME (1,120 of them on a big board, 67k a second at 60fps) in the hottest loop the renderer
    /// has, to buy safety against a shape this code does not contain.

    /// Cover and plateaus take the room's hue, with the same 0.55 pull and shade mix
    /// Renderer.DrawCover uses — one number honoured by two renderers, so the board looks like the
    /// same place whichever way you are looking at it.
    static Color Biomed(Color c, float pull = 0.55f) =>
        Pal.Mix(Pal.Mix(c, Scene.Tint, pull), Pal.RGBA(8, 11, 15), 0.10f);

    // ── P31: THE AUTHORED PROP KIT ───────────────────────────────────────────────────────────
    // assets/props/*.glb, generated by tools/props/props.py in P28 and unused until now. Every
    // solid on the board being a box is most of why the projected view reads as bland: the 2D
    // renderer draws crates with strapping, ferns, plateau hatching and barrel bands, and 3D
    // replaced the lot with one grey cuboid.
    //
    // LOADED ONCE, LAZILY (UploadMesh needs a live GL context) and through Cfg.AssetPath, never a
    // bare relative path. If any mesh fails to resolve the whole kit is declared absent and every
    // call site falls back to the bevelled block — a missing asset must degrade the LOOK, never
    // drop terrain the player is standing behind.
    static Model _tree, _crate, _wallHi, _wallLo, _wallDoor, _rock, _slag, _sign;
    static bool _propsTried, _propsOk;

    static bool Props
    {
        get
        {
            if (_propsTried) return _propsOk;
            _propsTried = true;
            Model L(string n) => Raylib.LoadModel(Cfg.AssetPath($"assets/props/{n}.glb"));
            _tree = L("tree"); _crate = L("crate");
            _wallHi = L("wall_high"); _wallLo = L("wall_low"); _wallDoor = L("wall_door");
            _rock = L("rock"); _slag = L("slag"); _sign = L("sign");   // P36 — the biome species
            _propsOk = _tree.MeshCount > 0 && _crate.MeshCount > 0 && _wallHi.MeshCount > 0
                    && _wallLo.MeshCount > 0 && _wallDoor.MeshCount > 0
                    && _rock.MeshCount > 0 && _slag.MeshCount > 0 && _sign.MeshCount > 0;
            if (!_propsOk)
                Console.Error.WriteLine("VIEW3D: assets/props/*.glb did not load - falling back to blocks.");
            return _propsOk;
        }
    }

    /// P36 — THE BIOME'S OWN COVER. C4 and P16 made five biomes MECHANICAL and P31 gave them a
    /// ground layer, but every biome's COVER was the same crate: the room recoloured and the things
    /// in it did not, which is RESONANCE V3's "forty-five grey widgets in a coloured room" wearing
    /// a different hat.
    ///
    /// Returns the biome's species and the scale it wants, or a null model for "no species, use the
    /// kit default". Only the biomes with an obvious vocabulary get one — STEEL is a depot, ASH is a
    /// burn scar and TUNDRA is a snowfield, and a crate is the right answer in all three.
    ///
    /// **A SPECIES IS A CHANGE OF MATERIAL, NOT OF WHAT THE TILE DOES.** Every one of these is built
    /// to the crate's envelope and swapped in on the same scale factors, so the silhouette a player
    /// reads as "waist-high thing I can shoot over" is the same in every room. Nothing here is read
    /// by `Grid`, `Ai` or `Combat`; `Grid.CoverSeed`, which picks the variant, is documented as
    /// purely visual and is ignored by every rule.
    /// The `Wire` cache key for whatever `BiomeSpecies` + the crate fallback just chose. It must
    /// track that routing exactly: two different meshes under one key would hand the second one the
    /// first one's edges, and the result — a boulder drawn with a tree's silhouette — is a bug that
    /// looks like a style.
    static string SpeciesKey(bool high)
    {
        switch (Scene?.Name)
        {
            case "VERDANT": return high ? "tree" : "crate";
            case "ARID":    return "rock";
            case "MAGMA":   return "slag";
            case "NEON":    return "sign";
            default:        return "crate";
        }
    }

    static Model BiomeSpecies(bool high, out float scale)
    {
        scale = high ? 1.45f : 0.95f;
        switch (Scene?.Name)
        {
            case "VERDANT": if (!high) return default; scale = 1.25f; return _tree;
            case "ARID":    scale = high ? 1.55f : 1.10f; return _rock;
            case "MAGMA":   scale = high ? 1.30f : 0.92f; return _slag;
            case "NEON":    scale = high ? 1.35f : 1.00f; return _sign;
            default:        return default;
        }
    }

    /// A stable, per-tile variant roll. `Grid.CoverSeed` is documented as "PURELY VISUAL: stable
    /// per-tile identity of the drawn cover VOLUME" — exactly the slot a prop choice belongs in,
    /// already persisted for the mission and already ignored by every rule.
    static int Variant(Grid g, int x, int y) =>
        Math.Abs(g.CoverSeed[x, y] == Grid.NoSeed ? (x * 31 + y * 17) : g.CoverSeed[x, y]);

    /// Cover, as an OBJECT rather than a cuboid. A tree IS high cover in a forest, so VERDANT's
    /// blocks become trees; elsewhere a crate stack alternates with the plain block so a line of
    /// cover stops reading as extruded wallpaper.
    static void CoverProp(Grid g, int x, int y, float baseY, bool high, float vf)
    {
        Color tint = Known(Biomed(high ? Pal.CoverHiTop : Pal.CoverLoTop), vf);
        // The .glb kit carries its OWN baked key light AND ambient occlusion in vertex colours
        // (roughly 0.27..1.0), and the shader multiplies that by the tint. Tinting a prop with the
        // same colour a flat block gets therefore darkens it TWICE and the mesh lands as a near
        // black silhouette — detail rendered invisible, which is worse than the cuboid it replaced.
        // Props get their own lift so the LIT faces land where a block's lit face does.
        Color propTint = Pal.RGBA(Math.Min(255, tint.R * 9 / 5), Math.Min(255, tint.G * 9 / 5),
                                  Math.Min(255, tint.B * 9 / 5), tint.A);
        if (Props)
        {
            int v = Variant(g, x, y);
            Model m; float sc;
            var species = BiomeSpecies(high, out float speciesScale);
            // The species is the ROOM's answer and wins where it exists; VERDANT's tree has always
            // taken every high-cover tile and the other three do the same, because a landscape whose
            // boulders are half crates reads as a landscape with crates in it.
            if (species.MeshCount > 0) { m = species; sc = speciesScale; }
            else if ((v & 1) == 0) { m = _crate; sc = high ? 1.45f : 0.95f; }
            else { Solid(TileWorld(x, y, baseY + (high ? HighH : LowH) * 0.5f),
                         high ? 0.92f : 0.86f, high ? HighH : LowH, high ? 0.92f : 0.86f, tint, tint); return; }
            if (_wire)
            {
                // THIS is where the extractor earns its keep: a tree, a boulder, a slag heap and a
                // hoarding are all arbitrary meshes, and none of them was authored with a wireframe
                // in mind. `Wire.For` turns any of them into a line set once, on first sight.
                Wire.Draw(SpeciesKey(high), m, TileWorld(x, y, baseY), Vector3.UnitY,
                          (v * 37) % 360, Vector3.One * sc, WireTint(_wireDim));
                return;
            }
            Rlgl.DrawRenderBatchActive();
            Raylib.DrawModelEx(m, TileWorld(x, y, baseY), Vector3.UnitY, (v * 37) % 360,
                               Vector3.One * sc, propTint);
            Rlgl.DrawRenderBatchActive();
            return;
        }
        Solid(TileWorld(x, y, baseY + (high ? HighH : LowH) * 0.5f),
              high ? 0.92f : 0.86f, high ? HighH : LowH, high ? 0.92f : 0.86f, tint, tint);
    }

    static Model _block; static bool _blockReady;
    static Model Blocks
    {
        get
        {
            if (!_blockReady)
            {
                _block = Raylib.LoadModelFromMesh(Mesh3D.BevelBox(1f, 1f, 1f, 0.07f, 0.52f));
                _blockReady = true;
            }
            return _block;
        }
    }

    // ── P30: THE EDGE LAYER IN THREE DIMENSIONS ──────────────────────────────────────────────
    /// P28 put walls on tile BOUNDARIES and taught the 2D renderer to draw them; the projected
    /// view never learned, so a building placed under SIGHTLINE_BUILDINGS=1 was simply INVISIBLE
    /// here — the one thing a 3D view should show better than a top-down one.
    ///
    /// A wall is a thin slab straddling the grid line, never a filled cell: the player has to be
    /// able to see that both tiles beside it are still standable. A door is two jambs and a lintel
    /// so the opening reads as a way through rather than as a gap in the geometry.
    public static void DrawEdges(Grid g)
    {
        if (g == null || !g.AnyEdges || !Edges.Enabled) return;
        DrawEdgePass(g, false);
        if (Vision.Enabled) WirePass(g, () => DrawEdgePass(g, true));
    }

    /// Run one wireframe pass with the confidence shader bound, or plainly if it is unavailable.
    /// Once per pass, twice per frame — not per object.
    static void WirePass(Grid g, Action body)
    {
        bool bound = Wire.Begin(g.W, Renderer.NowPublic);
        body();
        if (bound) Wire.End();
    }

    static void DrawEdgePass(Grid g, bool wantWire)
    {
        float T = 0.16f;
        void Slab(Vector3 baseCentre, float w, float h, float d, Color c) =>
            Solid(baseCentre with { Y = baseCentre.Y + h * 0.5f }, w, h, d, c, c);

        for (int x = 0; x <= g.W; x++)
            for (int y = 0; y < g.H; y++)
            {
                var k = g.EdgeV[x, y]; if (k == EdgeKind.None) continue;
                byte w0 = Vision.FaceVAt(x, y, 0), w1 = Vision.FaceVAt(x, y, 1);
                if (w0 == Vision.Unseen && w1 == Vision.Unseen) continue;
                if ((Math.Max(w0, w1) == Vision.Remembered) != wantWire) continue;
                // ONE SIDE KNOWN = ONE PLANE. Half the thickness, flush to the face that was
                // actually observed, so the operator cannot read a depth nobody has been round
                // the back to measure. Both sides known and the wall gets its real thickness.
                float th = (w0 != Vision.Unseen && w1 != Vision.Unseen) ? T : T * 0.5f;
                float off = (w0 != Vision.Unseen && w1 != Vision.Unseen) ? 0f
                          : (w0 != Vision.Unseen ? -T * 0.25f : T * 0.25f);
                float vf = Vision.Dim(Math.Max(w0, w1));
                var at = new Vector3(x + off, 0f, y + 0.5f);
                // A wall is remembered only if NEITHER face is in sight now; one live face means
                // you are looking at the wall, and the far side's staleness is already carried by
                // the half-thickness above.
                _wire = Math.Max(w0, w1) == Vision.Remembered;
                _wireDim = _wire ? Vision.ConfidenceV(x, y) : vf;
                if (k == EdgeKind.Door)
                {
                    Slab(at with { Z = y + 0.18f }, th, HighH * 0.95f, 0.36f, Known(Biomed(Pal.CoverHiTop), vf));
                    Slab(at with { Z = y + 0.82f }, th, HighH * 0.95f, 0.36f, Known(Biomed(Pal.CoverHiTop), vf));
                    Slab(at with { Y = HighH * 0.78f }, th, HighH * 0.22f, 1f, Known(Biomed(Pal.CoverHiTop), vf));
                }
                else Slab(at, th, k == EdgeKind.High ? HighH : LowH, 1f, Known(Biomed(Pal.CoverHiTop), vf));
                _wire = false; _wireDim = 1f;
            }
        for (int x = 0; x < g.W; x++)
            for (int y = 0; y <= g.H; y++)
            {
                var k = g.EdgeH[x, y]; if (k == EdgeKind.None) continue;
                byte n0 = Vision.FaceHAt(x, y, 0), n1 = Vision.FaceHAt(x, y, 1);
                if (n0 == Vision.Unseen && n1 == Vision.Unseen) continue;
                if ((Math.Max(n0, n1) == Vision.Remembered) != wantWire) continue;
                float th = (n0 != Vision.Unseen && n1 != Vision.Unseen) ? T : T * 0.5f;
                float off = (n0 != Vision.Unseen && n1 != Vision.Unseen) ? 0f
                          : (n0 != Vision.Unseen ? -T * 0.25f : T * 0.25f);
                float vf = Vision.Dim(Math.Max(n0, n1));
                var at = new Vector3(x + 0.5f, 0f, y + off);
                _wire = Math.Max(n0, n1) == Vision.Remembered;
                _wireDim = _wire ? Vision.ConfidenceH(x, y) : vf;
                if (k == EdgeKind.Door)
                {
                    Slab(at with { X = x + 0.18f }, 0.36f, HighH * 0.95f, th, Known(Biomed(Pal.CoverHiTop), vf));
                    Slab(at with { X = x + 0.82f }, 0.36f, HighH * 0.95f, th, Known(Biomed(Pal.CoverHiTop), vf));
                    Slab(at with { Y = HighH * 0.78f }, 1f, HighH * 0.22f, th, Known(Biomed(Pal.CoverHiTop), vf));
                }
                else Slab(at, 1f, k == EdgeKind.High ? HighH : LowH, th, Known(Biomed(Pal.CoverHiTop), vf));
                _wire = false; _wireDim = 1f;
            }
    }

    public static void DrawTerrain(Grid g)
    {
        // Floor: a slab per tile in the board's own checker, so the ground reads as TILES rather
        // than as a void with things standing in it. Same FloorA/FloorB alternation as the 2D board.
        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                if (Terrain.Enabled && g.Ground != null && g.Ground[x, y] == GroundKind.Rift) continue;
                byte st = Vision.At(x, y); if (st == Vision.Unseen) continue;
                float f = Vision.Dim(st);
                Raylib.DrawCube(TileWorld(x, y, -0.05f), 1f, 0.1f, 1f,
                                Known(((x + y) & 1) == 0 ? Scene.FloorA : Scene.FloorB, f));
            }
        DrawGround(g);     // P31: the biome's mechanical ground layer, which 3D had ignored entirely

        // Per-TILE outlines rather than board-spanning lines: a line drawn across the whole board
        // would run through ground nobody has looked at, which is the one thing this layer exists
        // to stop. The lattice has to stop where the knowledge does.
        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                byte st = Vision.At(x, y); if (st == Vision.Unseen) continue;
                var c = Known(Scene.Grid, Vision.Dim(st));
                Raylib.DrawLine3D(new Vector3(x, 0.006f, y), new Vector3(x + 1, 0.006f, y), c);
                Raylib.DrawLine3D(new Vector3(x, 0.006f, y), new Vector3(x, 0.006f, y + 1), c);
                if (x == g.W - 1) Raylib.DrawLine3D(new Vector3(x + 1, 0.006f, y), new Vector3(x + 1, 0.006f, y + 1), c);
                if (y == g.H - 1) Raylib.DrawLine3D(new Vector3(x, 0.006f, y + 1), new Vector3(x + 1, 0.006f, y + 1), c);
            }

        var e = Fade(Pal.Accent, 0.55f);
        Raylib.DrawLine3D(new Vector3(0, 0.03f, 0), new Vector3(g.W, 0.03f, 0), e);
        Raylib.DrawLine3D(new Vector3(g.W, 0.03f, 0), new Vector3(g.W, 0.03f, g.H), e);
        Raylib.DrawLine3D(new Vector3(g.W, 0.03f, g.H), new Vector3(0, 0.03f, g.H), e);
        Raylib.DrawLine3D(new Vector3(0, 0.03f, g.H), new Vector3(0, 0.03f, 0), e);

        DrawObjects(g, false);                 // everything in sight, lit
        // P39 — THE WIREFRAME IS ITS OWN PASS, and that is what the shader costs. A bind per tile
        // would flush rlgl's batch 1,120 times a frame to change nothing between them, so the
        // remembered tier is drawn in one group with the program bound once around it. The second
        // walk over the board is a handful of microseconds; the flushes would not be.
        //
        // With discovery OFF — the shipped default — `Vision.At` answers VISIBLE everywhere, so
        // there is no remembered tier at all and the whole second pass is skipped rather than run
        // to draw nothing.
        if (Vision.Enabled) WirePass(g, () => DrawObjects(g, true));

        DrawEdges(g);      // P30: the walls P28 put between tiles, finally visible in 3D
    }

    /// One tier of the board's OBJECTS — plateaus, cover and barrels. `wantWire` picks which:
    /// false draws what is in sight, true draws what is only remembered.
    static void DrawObjects(Grid g, bool wantWire)
    {
        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                // A RIFT is impassable but TRANSPARENT and gives no cover (P16), so it must read as
                // ABSENCE, never as an obstacle — a hole cut clean through the plate.
                if (Terrain.Enabled && g.Ground != null && g.Ground[x, y] == GroundKind.Rift)
                {
                    Raylib.DrawCube(TileWorld(x, y, -0.55f), 0.99f, 1.0f, 0.99f, Pal.Bg);
                    Raylib.DrawCubeWires(TileWorld(x, y, -0.05f), 0.99f, 0.02f, 0.99f, Fade(Pal.HighEdge, 0.30f));
                    continue;
                }

                byte tier = Vision.At(x, y);
                if (tier == Vision.Unseen) continue;             // P30: not known, not drawn
                if ((tier == Vision.Remembered) != wantWire) continue;   // P39: one tier per pass
                float vf = Vision.Dim(tier);
                // P38 — the object tier. THE FLOOR STAYS A SLAB whichever tier it is on, and that
                // is deliberate: ground you have walked is ground you KNOW, and outlining it too
                // would make a remembered board read as an unseen one. What memory costs you is
                // the THINGS on it, so the things are what go to lines.
                _wire = tier == Vision.Remembered;
                _wireDim = _wire ? Vision.Confidence(x, y) : vf;
                int h = g.Height[x, y];
                float baseY = 0f;
                if (h > 0)
                {
                    float ht = TierH * h;
                    Solid(TileWorld(x, y, ht * 0.5f), 1f, ht, 1f,
                          Known(Biomed(Pal.HighSide), vf), Known(Biomed(((x + y) & 1) == 0 ? Pal.HighA : Pal.HighB), vf));
                    baseY = ht;
                }

                var t = g.Tiles[x, y];
                if (t == TileType.LowCover) CoverProp(g, x, y, baseY, false, vf);
                else if (t == TileType.HighCover) CoverProp(g, x, y, baseY, true, vf);

                if (g.Barrel[x, y])
                    Solid(TileWorld(x, y, baseY + 0.32f), 0.52f, 0.64f, 0.52f,
                          Known(Pal.RGBA(110, 82, 24), vf), Known(Pal.VipGold, vf));
                _wire = false; _wireDim = 1f;
            }
    }

    /// ── P31: THE GROUND LAYER, IN THREE DIMENSIONS ──────────────────────────────────────────
    /// C4 and P16 made five biomes MECHANICAL on five axes, and the 2D board draws every one of
    /// them. The projected view drew none: it skipped the RIFT (so a hole read as absence, which
    /// was correct) and was blind to undergrowth, ice, vents and sand. A player looking at the 3D
    /// board could not see the fern that is giving them cover, the drift that is halving their
    /// step, or the fissure that is about to set them alight — rules with no picture attached.
    ///
    /// Deterministic scatter from `Util.Hash3`, never `Util.Rng`: this runs inside the draw, and a
    /// clock- or stream-seeded shape here would make the ground crawl between frames AND spend
    /// draws the flywheel is counting.
    static void DrawGround(Grid g)
    {
        if (!Terrain.Enabled || g.Ground == null) return;
        float Hg(int x, int y, int salt) => (Util.Hash3(x * 73856093, y * 19349663, salt) & 0xFFFFu) / 65536f;

        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                byte st = Vision.At(x, y); if (st == Vision.Unseen) continue;
                float f = Vision.Dim(st);
                if (g.Height[x, y] > 0) continue;         // ground never sits on raised terrain
                var c = new Vector3(x + 0.5f, 0f, y + 0.5f);
                switch (g.Ground[x, y])
                {
                    case GroundKind.Undergrowth:
                        // Blades, not a mat: the fern has to read as something you stand IN.
                        for (int i = 0; i < 5; i++)
                        {
                            float bx = x + 0.18f + Hg(x, y, i * 7 + 1) * 0.64f;
                            float bz = y + 0.18f + Hg(x, y, i * 7 + 2) * 0.64f;
                            float bh = 0.16f + Hg(x, y, i * 7 + 3) * 0.20f;
                            Raylib.DrawCube(new Vector3(bx, bh * 0.5f, bz), 0.07f, bh, 0.07f,
                                            Known(Pal.RGBA(52, 104 + (int)(Hg(x, y, i) * 40), 58), f));
                        }
                        break;
                    case GroundKind.Ice:
                        // A drift is flat and it CATCHES the light — the one ground that is bright.
                        Raylib.DrawCube(c with { Y = 0.012f }, 0.98f, 0.02f, 0.98f,
                                        Known(Pal.RGBA(132, 168, 196), f));
                        break;
                    case GroundKind.Sand:
                        Raylib.DrawCube(c with { Y = 0.010f }, 0.98f, 0.02f, 0.98f,
                                        Known(Pal.RGBA(122, 96, 52), f));
                        break;
                    case GroundKind.Vent:
                        // Recessed and hot: the fissure reads as a cut in the plate with fire in it.
                        Raylib.DrawCube(c with { Y = 0.008f }, 0.96f, 0.02f, 0.96f, Known(Pal.RGBA(26, 14, 10), f));
                        for (int i = 0; i < 3; i++)
                        {
                            float ex = x + 0.24f + Hg(x, y, i * 11 + 4) * 0.52f;
                            float ez = y + 0.24f + Hg(x, y, i * 11 + 5) * 0.52f;
                            Raylib.DrawCube(new Vector3(ex, 0.03f, ez), 0.16f, 0.03f, 0.16f,
                                            Known(Pal.RGBA(196, 88, 30), f));
                        }
                        break;
                }
            }
    }

    // ── The unit pass: CHIPS, not map pins ───────────────────────────────────────────────────
    /// The tallest solid standing on a tile (plateau + whatever sits on it), in world units.
    /// Used by the occlusion test — this is "what could hide something".
    static float SolidTop(Grid g, int x, int y)
    {
        if (!g.InBounds(x, y)) return 0f;
        if (Terrain.Enabled && g.Ground != null && g.Ground[x, y] == GroundKind.Rift) return 0f;
        float h = TierH * g.Height[x, y];
        var t = g.Tiles[x, y];
        if (t == TileType.LowCover) h += LowH;
        else if (t == TileType.HighCover) h += HighH;
        else if (g.Barrel[x, y]) h += 0.64f;
        return h;
    }

    /// Is this world point visible from the camera, or is a solid in the way?
    ///
    /// The CHIP itself needs no help — it is real 3D geometry, so the depth buffer occludes it
    /// correctly and for free. This exists for the GLYPH on its top face, which is 2D drawn after
    /// EndMode3D and therefore not depth-tested. Marching the view ray is exact enough and cheap:
    /// orthographic means one shared direction for the whole board, so this is a short walk over
    /// tile heights, not a raycast against geometry.
    static bool VisibleFrom(Grid g, Vector3 p, Vector3 toCamera)
    {
        for (float t = 0.55f; t < 48f; t += 0.2f)
        {
            Vector3 q = p + toCamera * t;
            int tx = (int)MathF.Floor(q.X), tz = (int)MathF.Floor(q.Z);
            if (!g.InBounds(tx, tz)) return true;          // ray left the board — nothing left to hide it
            if (q.Y < SolidTop(g, tx, tz) - 0.03f) return false;
        }
        return true;
    }

    /// A soldier reads as a CHIP resting on the projection — a translucent disc floating just above
    /// its tile with a light pooled underneath and its marker on the top face. Not a map pin: a pin
    /// says "a location on a diagram", a chip says "a piece on a table", and the second is the thing
    /// a hologram operator is looking at.
    /// One soldier chip. Modelled as a shallow CUP, not a plain disc: an outer wall rising a little
    /// above a recessed inner face. That lip is what makes it read as a machined token rather than a
    /// coloured circle, and it is the shape that catches an edge highlight.
    static void Chip(Grid g, Unit u, bool ghost)
    {
        bool friend = u.Team == Team.Player;
        Color ring = u.IsVip ? Pal.VipGold : (friend ? Pal.Friend : Pal.Elite);
        Color dk = u.IsVip ? Pal.VipDk : (friend ? Pal.FriendDk : Pal.EliteDk);

        float baseY = TierH * g.Height[u.X, u.Y];
        float cx = u.X + 0.5f, cz = u.Y + 0.5f;
        float y = baseY + ChipFloat;

        // A ring, made by overdraw: Raylib has no annulus, and DrawCylinderWires is not one either
        // — it draws a vertical line per slice, which reads as gear teeth. That hatching WAS the
        // prominent-lines artifact on the chip edges.
        void Ring(float atY, float rad, float thick, Color c, float a, Color inner, float innerA)
        {
            Raylib.DrawCylinder(new Vector3(cx, atY, cz), rad, rad, 0.004f, 44, Fade(c, a));
            Raylib.DrawCylinder(new Vector3(cx, atY + 0.003f, cz), rad - thick, rad - thick, 0.004f, 44, Fade(inner, innerA));
        }

        if (ghost)
        {
            // OUTLINES, NOT A FILLED FORM — and no hatching. Two clean rings and a whisper of fill
            // say "someone is there, behind this" without competing with the chips you can see.
            // Drawn a hair SMALLER than the solid: at identical size the ghost's silhouette survived
            // around the rim of every VISIBLE chip too, which is the other half of the same artifact.
            const float G = 0.94f;
            float gr = ChipR * G;
            Raylib.DrawCylinder(new Vector3(cx, y + 0.012f, cz), gr, gr, ChipH * G, 44, Fade(dk, 0.10f));
            Ring(y + 0.012f, gr, 0.030f, ring, 0.50f, Pal.Bg, 0f);
            Ring(y + ChipH * G, gr, 0.034f, ring, 0.62f, Pal.Bg, 0f);
            Ring(y + ChipH * G + 0.006f, gr * 0.72f, 0.022f, ring, 0.30f, Pal.Bg, 0f);
            return;
        }

        // light pooled on the tile: three stacked discs rather than one, so it falls off toward the
        // edge instead of reading as a flat sticker
        Raylib.DrawCylinder(new Vector3(cx, baseY + 0.055f, cz), 0.48f, 0.48f, 0.004f, 32, Fade(ring, 0.16f));
        Raylib.DrawCylinder(new Vector3(cx, baseY + 0.060f, cz), 0.38f, 0.38f, 0.004f, 32, Fade(ring, 0.26f));
        Raylib.DrawCylinder(new Vector3(cx, baseY + 0.065f, cz), 0.24f, 0.24f, 0.004f, 32, Fade(ring, 0.40f));

        // The lip stays a drawn RING rather than lathed geometry: it is a highlight, and it is what
        // keeps the chip legible at full-board zoom where the whole mesh is a few pixels tall.
        Ring(y + 0.135f, ChipR * 0.885f, 0.048f, ring, 0.9f, dk, 0.95f);
    }

    static Model _chipModel;
    static bool _chipReady;

    /// Lazy because a mesh upload needs a live GL context, which does not exist at type init.
    /// LoadModelFromMesh rather than a bare Mesh + Material: DrawModel sets up the material,
    /// transform and shader state itself, where hand-rolled DrawMesh calls interleaved with rlgl's
    /// batched primitives left state the batch then drew under — visible as huge coloured wedges at
    /// exactly the yaws where more chips were occluded (so more batched ghost geometry preceded the
    /// mesh draws). The mesh data was never wrong: 1728/1728 verts, bbox +-0.38, counts correct.
    static void EnsureChipMesh()
    {
        if (_chipReady) return;
        _chipModel = Raylib.LoadModelFromMesh(Mesh3D.Lathe(Mesh3D.ChipProfile, 48));
        _chipReady = true;
    }

    /// THE 3D HALF — must be called INSIDE BeginMode3D. (It was not, the first time: the cylinder
    /// calls landed outside the 3D pass and every chip silently vanished, leaving only the 2D
    /// glyphs floating on an empty board. Raylib does not complain; it just draws nothing.)
    ///
    /// THE X-RAY PASS, and why it needs no shader. The obvious idea — a shader making the BLOCKS
    /// translucent — is the wrong lever: it would mean you always see through walls, which throws
    /// away the occlusion the view exists to give. The effect belongs on the CHIP.
    ///
    /// Two passes, ordered, and the result is exact PER PIXEL:
    ///   1. GHOST with the depth test OFF, so it paints over whatever is in front of it.
    ///   2. SOLID with the depth test ON, which covers the ghost everywhere the chip is genuinely
    ///      visible and leaves it standing everywhere the chip is not.
    /// So a chip half behind a wall is half solid and half x-ray, on the exact pixel boundary, with
    /// no depth-function control (which Raylib does not expose) and no shader.
    ///
    /// Rlgl.DrawRenderBatchActive() before each state change is NOT optional: Raylib batches draw
    /// calls, so flipping depth state without flushing applies it to geometry already queued.
    /// P30 — A HOSTILE IS ONLY DRAWN WHERE THE SQUAD CAN SEE IT.
    ///
    /// The first build of the discovery layer gated TERRAIN and forgot the units, so on a big
    /// board the enemy chips sat out in the black, plainly visible on ground nobody had scanned.
    /// That does not merely leak information, it defeats the entire mechanic: an operator who can
    /// see every hostile has no reason to care what the terrain memory says.
    ///
    /// VISIBLE only, never REMEMBERED: terrain that was seen an hour ago is still where it was,
    /// but a soldier who was seen an hour ago has moved. Drawing a stale hostile at a stale tile
    /// would be an outright lie rather than an honest memory. Friendlies are always drawn — HQ
    /// knows where it sent its own people.
    static bool Shown(Unit u) =>
        u.Team == Team.Player || !Vision.Enabled || Vision.At(u.X, u.Y) == Vision.Visible;

    public static void DrawChips(Grid g, List<Unit> units)
    {
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthTest();
        Rlgl.DisableDepthMask();     // the ghost must not write depth or it occludes the solid pass
        foreach (var u in units) if (u.Alive && Shown(u)) Chip(g, u, ghost: true);
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthMask();
        Rlgl.EnableDepthTest();

        foreach (var u in units) if (u.Alive && Shown(u)) Chip(g, u, ghost: false);

        // MESH DRAWS GO TOGETHER, ONCE — never interleaved with batched primitives.
        // Raylib has two drawing paths that do not mix freely: DrawCube/DrawCylinder queue into
        // rlgl's vertex batch under the default shader, while DrawMesh binds its own VAO and shader
        // and draws immediately. Interleaving them per-unit left the batch drawing under the mesh's
        // shader state and painted huge garbage wedges across the frame. One flush, every mesh, one
        // flush back is both correct and cheaper than 2N context switches.
        EnsureChipMesh();
        Rlgl.DrawRenderBatchActive();
        foreach (var u in units)
        {
            if (!u.Alive || !Shown(u)) continue;
            bool friend = u.Team == Team.Player;
            Color dk = u.IsVip ? Pal.VipDk : (friend ? Pal.FriendDk : Pal.EliteDk);
            Raylib.DrawModel(_chipModel,
                             new Vector3(u.X + 0.5f, TierH * g.Height[u.X, u.Y] + ChipFloat, u.Y + 0.5f),
                             1f, Fade(dk, 0.97f));
        }
        Rlgl.DrawRenderBatchActive();
    }

    /// THE 2D HALF — the marker on each chip's top face, drawn after EndMode3D and therefore not
    /// depth-tested, so it carries the occlusion test itself.
    public static void DrawMarkers(Grid g, List<Unit> units, Camera3D cam)
    {
        Vector3 toCam = Vector3.Normalize(cam.Position - cam.Target);
        var order = new List<Unit>(units);
        order.Sort((a, b) => Vector3.Distance(cam.Position, TileWorld(b.X, b.Y))
                            .CompareTo(Vector3.Distance(cam.Position, TileWorld(a.X, a.Y))));
        Vector2 o = Raylib.GetWorldToScreen(new Vector3(0, 0, 0), cam);
        float px = Vector2.Distance(o, Raylib.GetWorldToScreen(new Vector3(1, 0, 0), cam));

        foreach (var u in order)
        {
            if (!u.Alive || !Shown(u)) continue;   // P30: no glyph for a hostile nobody can see
            float baseY = TierH * g.Height[u.X, u.Y];
            var top = new Vector3(u.X + 0.5f, baseY + ChipFloat + ChipH + 0.05f, u.Y + 0.5f);
            // Paired with the x-ray pass below the glyph: an occluded chip keeps its identity but
            // drops out of the foreground read, so you can see WHO is behind the wall without them
            // competing with the units you actually have eyes on.
            bool vis = VisibleFrom(g, top, toCam);
            float a = vis ? 1f : 0.45f;

            bool friend = u.Team == Team.Player;
            Color ring = u.IsVip ? Pal.VipGold : (friend ? Pal.Friend : Pal.Elite);
            Vector2 sp = Raylib.GetWorldToScreen(top, cam);
            string ini = string.IsNullOrEmpty(u.Name) ? "?" : u.Name.Substring(0, 1);
            int fs = (int)MathF.Max(13f, px * 0.52f);
            Vector2 m = Cfg.Measure(ini, fs, 1f);
            Cfg.Text(ini, sp - m * 0.5f, fs, 1f, Fade(ring, a));
        }
    }

    /// One full projected frame. Caller owns BeginDrawing/EndDrawing.
    /// P31 — the room this board is IN. The 2D renderer pulls cover and plateaus 0.55 toward the
    /// biome's hue and takes its floor checker from it, which is most of why eight biomes read as
    /// eight PLACES. The projected view ignored all of it and painted one grey for every room —
    /// "forty-five grey widgets in a coloured room read as a whitebox level" was RESONANCE V3's
    /// finding about the 2D board, and the 3D view had quietly reintroduced exactly that.
    public static Biome Scene = Biome.All[0];



    // ══════════════════ SIGHTLINE_PICKTEST ══════════════════
    /// THE INPUT PATH, ASSERTED. A projected view that draws beautifully and picks the wrong tile
    /// is not playable, and nothing else in this repository can tell the difference — every other
    /// check looks at pixels or at rules, and this is the seam BETWEEN them.
    ///
    /// The claim is a ROUND TRIP: project a tile's centre to a screen pixel with the same camera
    /// the frame is drawn with, hand that pixel back to the picker, and get the same tile. Run over
    /// every tile of the board, at several pitch/yaw pairs, because the failure mode that matters
    /// (an axis flipped, a half-tile offset, a yaw the inverse does not undo) is invisible at one
    /// angle and obvious at another.
    ///
    /// Needs a real window: Raylib.GetWorldToScreen reads the live framebuffer size.
    public static string PickSelfTest()
    {
        var fails = new System.Collections.Generic.List<string>();
        var g = new Grid();
        for (int x = 0; x < g.W; x++) for (int y = 0; y < g.H; y++) g.Tiles[x, y] = TileType.Floor;
        float savedP = PitchDeg, savedY = YawDeg, savedZ = Zoom;
        var savedPan = Pan;
        int checkedTiles = 0;

        // P33 added ZOOM and PAN, and they are exactly the states a round-trip can break in a way
        // four fixed angles cannot see: both move the camera TARGET, which is the term the inverse
        // has to undo. The last four rows are the new ones.
        var states = new[]
        {
            (52f,   0f, 1f,   0f,  0f),
            (40f,  20f, 1f,   0f,  0f),
            (64f, -35f, 1f,   0f,  0f),
            (30f,  45f, 1f,   0f,  0f),
            (52f,   0f, 2.2f, 0f,  0f),          // zoomed, centred
            (52f,   0f, 2.2f, 3.5f, -2.5f),      // zoomed and panned, no yaw
            (44f,  30f, 3.5f, -4f,  3f),         // zoomed, panned and rotated — all three at once
            (22f, 135f, 1.8f, 2f,   2f),         // a shallow tilt past the pitch the board opens on
        };

        foreach (var (pd, yd, z, panX, panY) in states)
        {
            PitchDeg = pd; YawDeg = yd; Zoom = z;
            Pan = new Vector2(panX, panY);
            ClampPan(g);                          // a state the player could not reach is not a test
            var cam = MakeCamera(g, (float)Cfg.ScreenW / Cfg.ScreenH);
            string tag = $"p{pd:0}/y{yd:0}/z{Zoom:0.0}/pan{Pan.X:0.0},{Pan.Y:0.0}";
            int bad = 0;
            for (int y = 0; y < g.H; y++)
                for (int x = 0; x < g.W; x++)
                {
                    var screen = Raylib.GetWorldToScreen(TileWorld(x, y), cam);
                    checkedTiles++;
                    if (!PickTile(g, screen, cam, out int px, out int py) || px != x || py != y)
                        if (++bad <= 2) fails.Add($"{tag}: tile {x},{y} picked as {px},{py}");
                }
            if (bad > 2) fails.Add($"{tag}: {bad} tiles mis-picked in total");

            // A pixel well outside the board must be refused, not clamped to an edge tile — a
            // picker that clamps makes the whole HUD margin act like a live board click.
            var far = Raylib.GetWorldToScreen(new Vector3(g.W + 25f, 0f, g.H + 25f), cam);
            if (PickTile(g, far, cam, out _, out _)) fails.Add($"{tag}: a point off the board picked a tile");
        }

        // ── Leg (B): the PAN CONTRACT. A drag has to move the BOARD under the hand, so the tile
        // under a pixel before the drag must be the tile under pixel+delta after it. This is what
        // the yaw rotation and the 1/sin(pitch) term in DragPan are FOR, and neither of them is
        // visible to the round-trip above, which never calls DragPan at all.
        foreach (var (pd, yd) in new[] { (52f, 0f), (40f, 60f), (30f, -120f), (70f, 210f) })
        {
            PitchDeg = pd; YawDeg = yd; Zoom = 2.5f; Pan = Vector2.Zero;
            var cam0 = MakeCamera(g, (float)Cfg.ScreenW / Cfg.ScreenH);
            var anchor = new Vector2(Cfg.ScreenW * 0.5f, Cfg.ScreenH * 0.5f);
            if (!PickTile(g, anchor, cam0, out int bx, out int by)) { fails.Add($"pan p{pd:0}/y{yd:0}: centre picked nothing"); continue; }

            var delta = new Vector2(60f, 40f);
            DragPan(g, delta, PixelsPerUnit(cam0));
            var cam1 = MakeCamera(g, (float)Cfg.ScreenW / Cfg.ScreenH);
            if (!PickTile(g, anchor + delta, cam1, out int ax, out int ay))
                fails.Add($"pan p{pd:0}/y{yd:0}: dragged point picked nothing");
            else if (ax != bx || ay != by)
                fails.Add($"pan p{pd:0}/y{yd:0}: board slipped under the drag — {bx},{by} -> {ax},{ay}");
        }

        // ── Leg (C): the clamp. At zoom 1 the whole board is framed, so there is nowhere to pan
        // and Pan must collapse to zero rather than drifting the board off one edge.
        Zoom = 1f; Pan = new Vector2(9f, -9f); ClampPan(g);
        if (Pan != Vector2.Zero) fails.Add($"clamp: zoom 1 left pan at {Pan.X:0.0},{Pan.Y:0.0}");
        Zoom = 3f; Pan = new Vector2(999f, -999f); ClampPan(g);
        float limX = g.W * 0.5f * (1f - 1f / 3f), limY = g.H * 0.5f * (1f - 1f / 3f);
        if (MathF.Abs(Pan.X - limX) > 0.01f || MathF.Abs(Pan.Y + limY) > 0.01f)
            fails.Add($"clamp: zoom 3 let pan reach {Pan.X:0.00},{Pan.Y:0.00} (limit {limX:0.00},{limY:0.00})");

        // ── Leg (D): ResetCamera returns EVERY term, not the two it started life with. A reset
        // that forgets zoom or pan strands the player looking at a corner with no way back.
        PitchDeg = 11f; YawDeg = 123f; Zoom = 3.4f; Pan = new Vector2(4f, 4f);
        ResetCamera();
        if (PitchDeg != 52f || YawDeg != 0f || Zoom != 1f || Pan != Vector2.Zero)
            fails.Add($"reset: left p{PitchDeg:0}/y{YawDeg:0}/z{Zoom:0.0}/pan{Pan.X:0.0},{Pan.Y:0.0}");

        PitchDeg = savedP; YawDeg = savedY; Zoom = savedZ; Pan = savedPan;
        return fails.Count == 0
            ? $"PICKTEST: PASS ({checkedTiles} tile round-trips over {states.Length} camera states; pan/clamp/reset legs OK)"
            : "PICKTEST: FAIL\n  " + string.Join("\n  ", fails);
    }

    // ══════════════════ P34 — THE BRIDGE'S SELF-TEST ══════════════════
    /// What this pins, and why each leg exists.
    ///
    /// (A) THE rlgl MATRIX CONVENTION. `Rlgl.MultMatrixf` wants the TRANSPOSE of the
    ///     System.Numerics layout — translation in M14/M24, not M41/M42 — and nothing in the type
    ///     system says so. Passed the wrong way it does not throw and does not warn: the whole Fx
    ///     layer simply draws at the board's top-left corner, which is a plausible-looking bug to
    ///     chase for an hour. CLAUDE.md's Raylib gotchas list is full of this class, so the leg
    ///     DRAWS A PIXEL THROUGH THE REAL STACK and reads the framebuffer back, rather than
    ///     asserting arithmetic that would agree with itself either way.
    /// (B) THE MAP IS THE CAMERA'S. `ProjectBoardPx(TileCenter(x,y))` must equal
    ///     `GetWorldToScreen(TileWorld(x,y, FxPlaneY))` for every tile — i.e. the affine shortcut
    ///     agrees with Raylib's own projection, at every camera state, or a tracer lands one tile
    ///     off at yaw 45 and nowhere else.
    /// (C) SHAKE MOVES THE IMAGE BY THE SHAKE. `ApplyShake` folds a pixel offset into a 3D camera
    ///     through its own screen axes; the leg projects a fixed world point with and without it
    ///     and checks the screen delta IS the offset asked for. Sign errors here are invisible in
    ///     a still frame and read as "the shake feels wrong" in motion.
    /// (D) THE BRIDGE AND THE BOARD SHAKE TOGETHER. The matrix is built from the shaken camera on
    ///     purpose; if it were built from the unshaken one, the board would shake and every tracer
    ///     over it would stand still. The leg checks the bridge's own output moved by the same
    ///     amount as (C).
    ///
    /// Needs a real window: two of the four legs read the live framebuffer.
    public static string FxBridgeSelfTest()
    {
        var fails = new System.Collections.Generic.List<string>();
        var g = new Grid();
        for (int x = 0; x < g.W; x++) for (int y = 0; y < g.H; y++) g.Tiles[x, y] = TileType.Floor;
        float savedP = PitchDeg, savedY = YawDeg, savedZ = Zoom; var savedPan = Pan;

        // ── (A) the convention, measured through the real stack.
        {
            PitchDeg = 52f; YawDeg = 0f; Zoom = 1f; Pan = Vector2.Zero;
            var cam = MakeCamera(g, (float)Cfg.ScreenW / Cfg.ScreenH);
            var probe = Util.TileCenter(g.W / 2, g.H / 2);
            var want = ProjectBoardPx(probe, cam);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
            Rlgl.PushMatrix();
            Rlgl.MultMatrixf(BoardPxMatrix(cam));
            Raylib.DrawRectangle((int)probe.X - 3, (int)probe.Y - 3, 7, 7, Pal.RGBA(255, 0, 255));
            Rlgl.PopMatrix();
            Raylib.EndDrawing();

            var img = Raylib.LoadImageFromScreen();
            var hit = Raylib.GetImageColor(img, (int)want.X, (int)want.Y);
            if (hit.R < 200 || hit.B < 200)
                fails.Add($"(A) nothing drawn at the projected point {want.X:0},{want.Y:0} — rlgl matrix convention");
            Raylib.UnloadImage(img);
        }

        // ── (B) the affine shortcut against Raylib's own projection, at five camera states.
        foreach (var (pd, yd, z, pnx, pny) in new[]
                 { (52f, 0f, 1f, 0f, 0f), (40f, 45f, 1f, 0f, 0f), (22f, -100f, 1f, 0f, 0f),
                   (52f, 0f, 2.4f, 3f, -2f), (70f, 210f, 1.8f, -2f, 2f) })
        {
            PitchDeg = pd; YawDeg = yd; Zoom = z; Pan = new Vector2(pnx, pny); ClampPan(g);
            var cam = MakeCamera(g, (float)Cfg.ScreenW / Cfg.ScreenH);
            float worst = 0f; int wx = -1, wy = -1;
            for (int y = 0; y < g.H; y++)
                for (int x = 0; x < g.W; x++)
                {
                    var viaBridge = ProjectBoardPx(Util.TileCenter(x, y), cam);
                    var viaRaylib = Raylib.GetWorldToScreen(TileWorld(x, y, FxPlaneY), cam);
                    float d = Vector2.Distance(viaBridge, viaRaylib);
                    if (d > worst) { worst = d; wx = x; wy = y; }
                }
            if (worst > 0.05f)
                fails.Add($"(B) p{pd:0}/y{yd:0}/z{z:0.0}: bridge and GetWorldToScreen disagree by {worst:0.000}px at tile {wx},{wy}");
        }

        // ── (C) + (D) shake, on the camera and through the bridge.
        {
            PitchDeg = 52f; YawDeg = 37f; Zoom = 1f; Pan = Vector2.Zero;
            var probeWorld = TileWorld(g.W / 2, g.H / 2, FxPlaneY);
            var probePx = Util.TileCenter(g.W / 2, g.H / 2);
            var calm = MakeCamera(g, (float)Cfg.ScreenW / Cfg.ScreenH);
            var calmScreen = Raylib.GetWorldToScreen(probeWorld, calm);
            var calmBridge = ProjectBoardPx(probePx, calm);

            var shake = new Vector2(9f, -5f);
            var shook = MakeCamera(g, (float)Cfg.ScreenW / Cfg.ScreenH);
            ApplyShake(ref shook, shake, 0f);
            var moved = Raylib.GetWorldToScreen(probeWorld, shook) - calmScreen;
            if (Vector2.Distance(moved, shake) > 0.2f)
                fails.Add($"(C) shake {shake.X:0},{shake.Y:0} moved the image by {moved.X:0.0},{moved.Y:0.0}");

            var movedBridge = ProjectBoardPx(probePx, shook) - calmBridge;
            if (Vector2.Distance(movedBridge, moved) > 0.05f)
                fails.Add($"(D) the board moved {moved.X:0.0},{moved.Y:0.0} and the Fx bridge moved {movedBridge.X:0.0},{movedBridge.Y:0.0}");

            // the zoom punch is a pure extent scale — bigger pulse, tighter frame, never a shift
            var punched = MakeCamera(g, (float)Cfg.ScreenW / Cfg.ScreenH);
            float beforeFov = punched.FovY;
            ApplyShake(ref punched, Vector2.Zero, 0.25f);
            if (MathF.Abs(punched.FovY - beforeFov / 1.25f) > 0.001f)
                fails.Add($"(C) a 0.25 zoom punch took FovY {beforeFov:0.000} to {punched.FovY:0.000}");
            if (punched.Position != calm.Position || punched.Target != calm.Target)
                fails.Add("(C) the zoom punch moved the camera as well as its extent");
        }

        // ── (E) P35: THE TEXT ESCAPE. Three claims, and the middle one is the subtle one.
        //   1. A glyph drawn under the bridge lands at the PROJECTED position, upright — not at the
        //      raw board pixel, and not sheared into the transform with everything else.
        //   2. `Cfg.Measure` reports widths in BOARD space, such that a call site centring by
        //      `pos -= Measure/2` gets EXACTLY the screen-space offset it meant. Stated as the
        //      identity it is: project(p - unmap(v)) == project(p) - v. Skip this and every centred
        //      label slides half its own width diagonally at yaw 45 while looking perfect at yaw 0.
        //   3. EndBridge disarms. A leaked escape would project the HUD.
        {
            PitchDeg = 44f; YawDeg = 62f; Zoom = 1f; Pan = Vector2.Zero;
            var cam = MakeCamera(g, (float)Cfg.ScreenW / Cfg.ScreenH);
            // Pick the tile whose projected position is FURTHEST from its raw board pixel, and
            // refuse to run the ink half unless that distance is large compared with the glyph.
            // At the board's centre the two coincide to within a glyph's width, and a leg that
            // looks for ink "here but not there" when here and there overlap proves nothing.
            Vector2 anchor = Vector2.Zero, want = Vector2.Zero; float far = -1f;
            for (int y = 0; y < g.H; y++)
                for (int x = 0; x < g.W; x++)
                {
                    var raw = Util.TileCenter(x, y);
                    var prj = ProjectBoardPx(raw, cam);
                    float d = Vector2.Distance(raw, prj);
                    if (d > far) { far = d; anchor = raw; want = prj; }
                }
            if (far < 120f) fails.Add($"(E) no tile projects further than {far:0}px from its board pixel — the ink leg would be vacuous");

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
            BeginBridge(cam);

            if (Cfg.TextProject == null || Cfg.TextUnmap == null)
                fails.Add("(E) BeginBridge did not arm the text escape");
            else
            {
                if (Vector2.Distance(Cfg.TextProject(anchor), want) > 0.05f)
                    fails.Add("(E) the armed projector disagrees with ProjectBoardPx");
                foreach (var v in new[] { new Vector2(40f, 0f), new Vector2(0f, 17f), new Vector2(-23f, 9f) })
                {
                    var lhs = ProjectBoardPx(anchor - Cfg.TextUnmap(v), cam);
                    var rhs = want - v;
                    if (Vector2.Distance(lhs, rhs) > 0.05f)
                        fails.Add($"(E) unmap is not the inverse: a {v.X:0},{v.Y:0} screen offset came back as {(want - lhs).X:0.0},{(want - lhs).Y:0.0}");
                }
            }
            // The expected UPRIGHT screen extent, measured through the same font and the same size
            // the escape is about to draw at. Taken from Raylib directly rather than from
            // Cfg.Measure, which under the bridge deliberately answers in BOARD space — and rather
            // than from a constant, because this self-test runs before the game loads its atlases
            // and a hard-coded 60x24 measures a font that is not there.
            float escScale = Cfg.TextScale;
            var flatSize = Raylib.MeasureTextEx(Cfg.FontFor(24f), "HHHH", Cfg.Scaled(24f) * escScale, 1f);
            Cfg.Text("HHHH", anchor, 24f, 1f, Pal.RGBA(255, 0, 255));
            EndBridge();
            Raylib.EndDrawing();

            if (Cfg.TextProject != null || Cfg.TextUnmap != null || Cfg.TextScale != 1f)
                fails.Add("(E) EndBridge left the text escape armed");

            // THE INK LEG MEASURES SHAPE, NOT POSITION, and that is the whole point. Without the
            // escape the glyph still lands near the projected anchor — the pushed matrix takes it
            // there — so "is there ink here" cannot tell the two apart and a leg written that way
            // passes with the escape deleted. What the escape actually buys is that the glyph is
            // UPRIGHT, so the claim is about its ink BOUNDING BOX: at yaw 62 / pitch 44 a 4-glyph
            // run measures about 50x20 drawn upright and about 49x45 sheared into the ground plane.
            // The width barely moves. The HEIGHT is the discriminator, so that is what is asserted.
            var img = Raylib.LoadImageFromScreen();
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int dy = -120; dy <= 120; dy++)
                for (int dx = -120; dx <= 120; dx++)
                {
                    int px = (int)want.X + dx, py = (int)want.Y + dy;
                    if (px < 0 || py < 0 || px >= Cfg.ScreenW || py >= Cfg.ScreenH) continue;
                    var c = Raylib.GetImageColor(img, px, py);
                    if (c.R <= 150 || c.B <= 150) continue;
                    if (px < minX) minX = px; if (px > maxX) maxX = px;
                    if (py < minY) minY = py; if (py > maxY) maxY = py;
                }
            Raylib.UnloadImage(img);

            if (maxX < minX) fails.Add($"(E) no glyph ink anywhere near the projected anchor {want.X:0},{want.Y:0}");
            else
            {
                float inkH = maxY - minY + 1, inkW = maxX - minX + 1;
                float wantH = flatSize.Y, wantW = flatSize.X;
                if (inkH > wantH * 1.5f)
                    fails.Add($"(E) the glyph is {inkH:0}px tall against {wantH:0} upright — it was drawn INTO the transform, sheared");
                if (inkW < wantW * 0.6f || inkW > wantW * 1.6f)
                    fails.Add($"(E) the glyph is {inkW:0}px wide against {wantW:0} expected");
            }
        }

        PitchDeg = savedP; YawDeg = savedY; Zoom = savedZ; Pan = savedPan;
        return fails.Count == 0
            ? "FXBRIDGETEST: PASS (rlgl convention drawn and read back; bridge matches GetWorldToScreen within 0.05px on 990 tiles over 5 camera states; shake and punch pinned; text escapes the matrix and Measure inverts it)"
            : "FXBRIDGETEST: FAIL\n  " + string.Join("\n  ", fails);
    }

    // ══════════════════ P32 — PLAYABLE: PICKING AND OVERLAYS ══════════════════
    /// Screen pixel -> board tile, through the projected camera. The ground is the y = 0 plane, so
    /// this is one ray/plane intersection and nothing more; the camera being ORTHOGRAPHIC means the
    /// ray direction is the same everywhere and the result is exact rather than perspective-warped.
    ///
    /// This is the half that makes a VIEW into a GAME. Everything the projected camera drew was
    /// output; until the mouse can be turned back into a tile there is nothing to click.
    public static bool PickTile(Grid g, Vector2 screen, Camera3D cam, out int tx, out int ty)
    {
        tx = ty = -1;
        var r = Raylib.GetScreenToWorldRay(screen, cam);
        if (MathF.Abs(r.Direction.Y) < 1e-6f) return false;
        float t = -r.Position.Y / r.Direction.Y;
        if (t < 0f) return false;
        var hit = r.Position + r.Direction * t;
        tx = (int)MathF.Floor(hit.X); ty = (int)MathF.Floor(hit.Z);
        return g.InBounds(tx, ty);
    }

    /// A flat quad on the ground plane. Overlays are drawn as REAL GEOMETRY rather than projected
    /// 2D, so they are depth-tested against the terrain for free — a move-range tile behind a wall
    /// is occluded by that wall without anyone writing an occlusion test.
    static void GroundQuad(int x, int y, float h, float inset, Color c)
    {
        float a = x + inset, b = x + 1 - inset, p = y + inset, q = y + 1 - inset;
        Raylib.DrawTriangle3D(new(a, h, p), new(a, h, q), new(b, h, q), c);
        Raylib.DrawTriangle3D(new(a, h, p), new(b, h, q), new(b, h, p), c);
    }
    static void GroundOutline(int x, int y, float h, float inset, Color c)
    {
        float a = x + inset, b = x + 1 - inset, p = y + inset, q = y + 1 - inset;
        Raylib.DrawLine3D(new(a, h, p), new(b, h, p), c); Raylib.DrawLine3D(new(b, h, p), new(b, h, q), c);
        Raylib.DrawLine3D(new(b, h, q), new(a, h, q), c); Raylib.DrawLine3D(new(a, h, q), new(a, h, p), c);
    }

    /// The interactive feedback a tactics game cannot be played without: where can I go, what will
    /// I walk, what am I pointing at. Deliberately NOT a port of Renderer.cs's ~480 2D draw calls —
    /// this is the short list that turns "a picture of a board" into "a board you can act on", and
    /// everything else (roster, action bar, cards, numbers) is screen-space HUD that already works
    /// unchanged because it never knew about the board's projection in the first place.
    static void DrawOverlays(Game g)
    {
        float Top(int x, int y) => TierH * g.Grid.HeightAt(x, y) + 0.03f;

        // MOVE RANGE — every tile the selected soldier can reach this turn.
        if (g.MoveCost != null && g.Selected != null)
            for (int y = 0; y < g.Grid.H; y++)
                for (int x = 0; x < g.Grid.W; x++)
                {
                    if (g.MoveCost[x, y] < 0) continue;
                    if (Vision.At(x, y) == Vision.Unseen) continue;
                    bool dash = g.MoveCost[x, y] > g.Selected.MoveBudget;   // second-action reach
                    // Quiet FILL, legible EDGE. On the flat board the move overlay is a tint on a
                    // tile; here it is a plate lying on lit geometry, so the same alpha reads far
                    // heavier and buries the terrain the 3D view exists to show. Carry the
                    // information in the outline and let the fill only group it.
                    var c = dash ? Fade(Pal.Accent, 0.07f) : Fade(Pal.Friend, 0.09f);
                    GroundQuad(x, y, Top(x, y), 0.08f, c);
                    GroundOutline(x, y, Top(x, y) + 0.002f, 0.08f, Fade(dash ? Pal.Accent : Pal.Friend, 0.30f));
                }

        // PATH PREVIEW — the actual walk, not a straight line to the cursor.
        if (g.PathPreview.Count > 0)
        {
            Vector3 prev = default; bool have = false;
            foreach (var (px, py) in g.PathPreview)
            {
                var pt = new Vector3(px + 0.5f, Top(px, py) + 0.04f, py + 0.5f);
                if (have) Raylib.DrawLine3D(prev, pt, Pal.Accent);
                Raylib.DrawCube(pt, 0.10f, 0.02f, 0.10f, Pal.Accent);
                prev = pt; have = true;
            }
        }

        // HOVER — the tile under the cursor, always drawn last so it wins.
        if (g.HoverValid)
            GroundOutline(g.HoverX, g.HoverY, Top(g.HoverX, g.HoverY) + 0.006f, 0.02f, Pal.Txt);
    }

    // ══════════════════ P34 — THE BOARD-PIXEL BRIDGE ══════════════════
    /// The whole `Fx` layer and every `Anim` draw in this game work in BOARD-PIXEL space: the 2D
    /// coordinates `Util.TileCenter` produces, drawn inside `BeginMode2D`. That is thousands of
    /// lines across `Fx.cs`, `Anim.cs` and `Renderer.cs`, and porting it call-by-call to 3D was
    /// never going to happen — which is why the projected view shipped with no tracers, no floating
    /// damage numbers, no particles and no screen shake, i.e. with pillar 2 switched off.
    ///
    /// IT DOES NOT NEED PORTING, because the map is AFFINE. The projected camera is ORTHOGRAPHIC,
    /// so a ground-plane point maps to a screen point with no perspective divide: board pixel ->
    /// tile is a scale and an offset, tile -> screen is a fixed 2x2 (the camera's two ground axes,
    /// sheared by yaw and squashed by pitch) plus an offset. Compose them and the entire 2D layer
    /// is one matrix away from being correct in 3D.
    ///
    /// So `Fx` and the anims are drawn UNCHANGED, with that matrix pushed on rlgl's stack — the
    /// same mechanism `BeginMode2D` itself uses. A tracer between two tiles lands between those
    /// two tiles; an impact ring lies on the ground and is squashed by the pitch exactly as the
    /// ground is; dust drifts along the board, not up the screen.
    ///
    /// THE ONE THING THAT MUST NOT GO THROUGH IT IS TEXT. A floating damage number sheared into the
    /// ground plane is unreadable, and at yaw 45 it is a parallelogram. `Fx.ProjectText` gets the
    /// anchor projected and the glyphs drawn upright — see `Fx.DrawText`.
    ///
    /// Everything here rides at CHIP HEIGHT rather than at the floor, because the layer is almost
    /// all combat feedback (muzzles, tracers, impacts, blood) and combat happens at the height of
    /// the pieces, not under them.
    public const float FxPlaneY = ChipFloat + ChipH;

    /// rlgl wants the TRANSPOSE of the System.Numerics convention — translation in M14/M24, not
    /// M41/M42. Measured, not assumed: a translate-only matrix pushed the un-transposed way leaves
    /// the drawn rect at the origin. This is exactly the "version-volatile signature" class
    /// CLAUDE.md warns about, so the assertion lives in `FxBridgeSelfTest` rather than in a comment.
    public static Matrix4x4 BoardPxMatrix(Camera3D cam, float worldY = FxPlaneY)
    {
        BoardPxAxes(cam, worldY, out Vector2 ax, out Vector2 az, out Vector2 t);
        var m = new Matrix4x4();
        m.M11 = ax.X; m.M12 = az.X; m.M13 = 0f; m.M14 = t.X;
        m.M21 = ax.Y; m.M22 = az.Y; m.M23 = 0f; m.M24 = t.Y;
        m.M33 = 1f;
        m.M44 = 1f;
        return m;
    }

    /// The affine map, as its parts: screen = ax * boardPx.X + az * boardPx.Y + t.
    public static void BoardPxAxes(Camera3D cam, float worldY, out Vector2 ax, out Vector2 az, out Vector2 t)
    {
        var p0 = Raylib.GetWorldToScreen(new Vector3(0f, worldY, 0f), cam);
        ax = (Raylib.GetWorldToScreen(new Vector3(1f, worldY, 0f), cam) - p0) / Cfg.Tile;
        az = (Raylib.GetWorldToScreen(new Vector3(0f, worldY, 1f), cam) - p0) / Cfg.Tile;
        t  = p0 - ax * Cfg.OriginX - az * Cfg.OriginY;
    }

    /// One board pixel through the bridge, for the call sites that need a POINT rather than a
    /// pushed transform (text anchors, anything measured before it is drawn).
    public static Vector2 ProjectBoardPx(Vector2 px, Camera3D cam, float worldY = FxPlaneY)
    {
        BoardPxAxes(cam, worldY, out Vector2 ax, out Vector2 az, out Vector2 t);
        return ax * px.X + az * px.Y + t;
    }

    /// An ISOTROPIC scale for things the bridge must not shear — glyph sizes, mostly. The map
    /// squashes one axis and not the other, so there is no single honest answer; the square root
    /// of the AREA scale is the one that keeps a number the same visual weight as the board it is
    /// floating over, at every pitch.
    public static float BoardPxScale(Camera3D cam, float worldY = FxPlaneY)
    {
        BoardPxAxes(cam, worldY, out Vector2 ax, out Vector2 az, out _);
        float area = MathF.Abs(ax.X * az.Y - ax.Y * az.X);
        return MathF.Sqrt(MathF.Max(area, 1e-6f));
    }

    /// Screen shake and the hit-stop zoom punch, applied to the PROJECTED camera.
    ///
    /// The flat view gets both from `Camera2D` (an Offset and a Zoom multiplier) and there is no
    /// such field on a 3D camera, so they are folded into the camera itself: the punch scales the
    /// orthographic extent, and the shake slides BOTH position and target along the camera's own
    /// screen-right and screen-up axes, which is the only offset that moves the image without
    /// turning the camera. `ppu` converts the shake's PIXELS into the world units those axes are in.
    ///
    /// Doing it on the camera rather than on the final image is what keeps the Fx bridge honest:
    /// the matrix is built FROM this camera, so the board and everything drawn over it shake
    /// together instead of sliding apart by the shake amount.
    public static void ApplyShake(ref Camera3D cam, Vector2 shakePx, float pulse)
    {
        if (pulse > 0.0001f) cam.FovY /= 1f + pulse;
        if (shakePx == Vector2.Zero) return;
        float ppu = Cfg.ScreenH / MathF.Max(cam.FovY, 0.001f);
        float yaw = YawDeg * MathF.PI / 180f, pitch = PitchDeg * MathF.PI / 180f;
        var right = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
        var up    = new Vector3(-MathF.Sin(pitch) * MathF.Sin(yaw), MathF.Cos(pitch),
                                -MathF.Sin(pitch) * MathF.Cos(yaw));
        var d = right * (-shakePx.X / ppu) + up * (shakePx.Y / ppu);
        cam.Position += d;
        cam.Target += d;
    }

    /// The PLAYABLE frame: the same board the screenshot hook draws, plus the interaction layer.
    /// Called from Game.DrawBoardLayer in place of the 2D board; Hud.Draw runs after it untouched.
    public static void DrawPlayable(Game g)
    {
        Raylib.ClearBackground(Pal.Bg);
        Vision.Stamp = g.Turn;          // P39: the clock a memory's AGE is measured against
        Vision.Refresh(g.Grid, AllUnits(g));
        var cam = MakeCamera(g.Grid, (float)Cfg.ScreenW / Cfg.ScreenH);
        ApplyShake(ref cam, g.Fx.ShakeOffset, g.CamPulse);
        Raylib.BeginMode3D(cam);
        DrawTerrain(g.Grid);
        DrawOverlays(g);
        DrawChips(g.Grid, AllUnits(g));
        Raylib.EndMode3D();
        DrawMarkers(g.Grid, AllUnits(g), cam);
        DrawFxLayer(g, cam);
    }

    /// P34 — the 2D feedback layer, over the projected board, through the bridge.
    ///
    /// Drawn AFTER the 3D pass and therefore over everything, which is a deliberate difference
    /// from the flat view (where ambient sits under the units). There is nowhere else to put it:
    /// these are screen-space primitives with no depth, and the ground plane is opaque, so
    /// "under the board" means "invisible". Over reads as atmosphere between the operator and the
    /// hologram, which is what this view is supposed to be anyway.
    static void DrawFxLayer(Game g, Camera3D cam)
    {
        BeginBridge(cam);
        Renderer.DrawGroundOverlays(g);    // P35 — the board's own decal layer, on the ground
        g.Fx.DrawAmbient();
        g.ActiveAnim?.Draw(g);
        g.Fx.Draw();
        g.Fx.DrawText();
        EndBridge();
    }

    /// P35 — push the bridge matrix AND arm the text escape, as one operation, because they are one
    /// operation: any code drawing under this transform may paint a glyph, and a glyph under this
    /// transform is a parallelogram. P34 shipped them apart (`Fx.ProjectText`, a special case for
    /// the one call site that was known to draw text), and the first overlay ported in P35 — EVAC,
    /// which paints a label — showed why that does not generalise.
    ///
    /// `TextUnmap` is the LINEAR part inverted, with no translation: it turns a screen-space
    /// measurement back into the board-space offset a centring call site is about to subtract. See
    /// the long note on `Cfg.TextProject`.
    public static void BeginBridge(Camera3D cam, float worldY = FxPlaneY)
    {
        BoardPxAxes(cam, worldY, out Vector2 ax, out Vector2 az, out Vector2 tr);
        Rlgl.PushMatrix();
        Rlgl.MultMatrixf(BoardPxMatrix(cam, worldY));

        float det = ax.X * az.Y - ax.Y * az.X;
        if (MathF.Abs(det) < 1e-6f) det = det < 0f ? -1e-6f : 1e-6f;   // degenerate only at pitch 0
        Cfg.TextProject = px => ax * px.X + az * px.Y + tr;
        Cfg.TextUnmap = v => new Vector2(( az.Y * v.X - az.X * v.Y) / det,
                                         (-ax.Y * v.X + ax.X * v.Y) / det);
        Cfg.TextScale = BoardPxScale(cam, worldY);
    }

    public static void EndBridge()
    {
        Cfg.TextProject = null;
        Cfg.TextUnmap = null;
        Cfg.TextScale = 1f;
        Rlgl.PopMatrix();
    }

    /// P33 — the camera's state, as one line of chrome. It exists because ORBIT costs the player
    /// "north": once the board is turned, a mission's deployment edge is no longer the bottom of
    /// the screen and there is nothing on the board itself to re-anchor to. The chip is drawn in
    /// the CHROME pass (Game.DrawHudLayer), not with the board, so it contributes nothing to the
    /// bloom — the same seam every other number in this game is on.
    ///
    /// It says nothing at rest. A line that is always there is a line nobody reads, and a camera
    /// sitting at its opening framing has nothing to report; the chip appears the moment one of
    /// the three terms leaves its default and names only the terms that moved.
    public static void DrawCameraChip()
    {
        var parts = new List<string>();
        // No degree sign: the baked atlases carry ASCII plus nine punctuation codepoints and U+00B0
        // is not one of them, so it paints as '?'. Adding it would repack both atlases and move
        // every glyph in the game for one cosmetic character.
        if (MathF.Abs(YawDeg) > 0.5f && MathF.Abs(YawDeg - 360f) > 0.5f) parts.Add($"YAW {YawDeg:0} DEG");
        if (MathF.Abs(PitchDeg - 52f) > 0.5f) parts.Add($"TILT {PitchDeg:0} DEG");
        if (Zoom > 1.01f) parts.Add($"x{Zoom:0.0}");
        if (parts.Count == 0) return;

        string line = string.Join("   ", parts) + "   [C] RESET";
        // TOP RIGHT, under the END TURN plate. P33 put it bottom-left, which collides with the
        // fifth roster card — the VIP / CAPTIVE slot an ESCORT or RESCUE mission fills, so the one
        // objective whose asset you most need to see is the one it covers. The band under the top
        // bar is empty at every screen this view can be on, and the centred BONUS line stops well
        // short of it.
        var sz = Cfg.Measure(line, 14, 1f);
        int x = Cfg.ScreenW - (int)sz.X - 20, y = 52;
        Raylib.DrawRectangle(x - 8, y - 5, (int)sz.X + 16, (int)sz.Y + 10, Pal.RGBA(8, 12, 17, 170));
        Raylib.DrawRectangleLinesEx(new Rectangle(x - 8, y - 5, (int)sz.X + 16, (int)sz.Y + 10), 1f,
                                    Pal.RGBA(90, 130, 160, 110));
        Cfg.Text(line, new Vector2(x, y), 14, 1f, Pal.TxtDim);
    }

    static List<Unit> AllUnits(Game g)
    {
        var all = new List<Unit>(g.Players);
        all.AddRange(g.Enemies);
        return all;
    }

    public static void DrawFrame(Grid g, List<Unit> units)
    {
        Raylib.ClearBackground(Pal.Bg);
        Vision.Refresh(g, units);   // P30: what HQ knows, before anything is drawn from it
        var cam = MakeCamera(g, (float)Cfg.ScreenW / Cfg.ScreenH);
        Raylib.BeginMode3D(cam);
        DrawTerrain(g);
        DrawChips(g, units);      // real geometry: the depth buffer occludes these for free
        Raylib.EndMode3D();
        DrawMarkers(g, units, cam);

        string label = $"PITCH {PitchDeg:0} DEG   YAW {YawDeg:0} DEG";
        Cfg.Text(label, new Vector2(18, 14), 18, 1f, Pal.TxtDim);
    }
}
