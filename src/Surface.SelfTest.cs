using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// SIGHTLINE_SURFACETEST — P43. The seam that decides WHERE a UI panel lives.
///
/// The thing this has to be able to fail on is the one PARALLAX keeps finding: a gate that goes
/// QUIET rather than loud. The dangerous shape here is a placement that silently falls back to the
/// screen and a test that then measures the screen and reports PASS — so leg (A) asserts the
/// fallback IS the identity (that is the claim, not an accident), and every other leg asserts the
/// panel was actually PLACED before it measures anything on it.
public static partial class Surface
{
    public static string SelfTest()
    {
        var fails = new List<string>();
        var g = new Grid();
        for (int x = 0; x < g.W; x++) for (int y = 0; y < g.H; y++) g.Tiles[x, y] = TileType.Floor;

        float savedP = View3D.PitchDeg, savedY = View3D.YawDeg, savedZ = View3D.Zoom;
        var savedPan = View3D.Pan;
        bool savedEnabled = View3D.Enabled;
        Kind savedPlacement = ActionBarPlacement;
        float aspect = (float)Cfg.ScreenW / Cfg.ScreenH;
        const float PivotY = 690f;

        Camera3D Cam(float pd, float yd, float z, float px, float pz)
        {
            View3D.PitchDeg = pd; View3D.YawDeg = yd; View3D.Zoom = z;
            View3D.Pan = new Vector2(px, pz); View3D.ClampPan(g);
            return View3D.MakeCamera(g, aspect);
        }

        // Camera states a player can actually reach — the last three are the ones that broke the
        // board bridge in P34: zoom and pan both move the camera TARGET, which is the term the
        // inverse has to undo.
        var states = new[]
        {
            (52f,   0f, 1f,   0f,  0f),
            (40f,  20f, 1f,   0f,  0f),
            (64f, -35f, 1f,   0f,  0f),
            (70f, 135f, 1f,   0f,  0f),
            (52f,   0f, 2.2f, 0f,  0f),
            (44f,  30f, 3.5f, -4f, 3f),
        };

        try
        {
            // ── (A) THE DEFAULT IS THE IDENTITY, AND THAT IS AN ASSERTION ────────────────────
            // Two ways the bar stays flat: the placement says Screen, or the flat renderer is
            // drawing. In BOTH, Begin must push nothing and the pointer must be untouched — that
            // is what makes "the flat game is unchanged" a checked claim rather than a hope.
            foreach (var (placement, enabled, tag) in new[]
                     { (Kind.Screen, true, "screen/3d"), (Kind.Screen, false, "screen/flat"),
                       (Kind.Table, false, "table/flat") })
            {
                Reset();
                ActionBarPlacement = placement; View3D.Enabled = enabled;
                var cam = Cam(52f, 0f, 1f, 0f, 0f);
                if (Begin(Slot.ActionBar, cam, PivotY)) { End(); fails.Add($"(A) {tag}: Begin pushed a surface"); }
                if (Active) fails.Add($"(A) {tag}: Active after a refused Begin");
                var probe = new Vector2(412f, 701f);
                if (PointerIn(Slot.ActionBar, probe) != probe) fails.Add($"(A) {tag}: the pointer was remapped");
                if (MapPointer(probe) != probe) fails.Add($"(A) {tag}: MapPointer was not the identity");
            }

            ActionBarPlacement = Kind.Table; View3D.Enabled = true;

            foreach (var (pd, yd, z, px, pz) in states)
            {
                var cam = Cam(pd, yd, z, px, pz);
                string tag = $"p{pd:0}/y{yd:0}/z{View3D.Zoom:0.0}";
                if (!Console(cam, PivotY, out Panel p))
                { fails.Add($"(B) {tag}: the console refused a camera the player can reach"); continue; }

                // ── (B) AFFINITY. Three projections claim to be the whole map; check that claim
                // against Raylib's own projection of the SAME world points, over the panel's
                // working area (the bar sits between y=560 and y=800 in local pixels).
                float worst = 0f; Vector2 wl = default;
                for (float ly = 400f; ly <= 820f; ly += 35f)
                    for (float lx = 0f; lx <= Cfg.ScreenW; lx += 40f)
                    {
                        var local = new Vector2(lx, ly);
                        float d = Vector2.Distance(Project(p, cam, local),
                                                   Raylib.GetWorldToScreen(p.World(local), cam));
                        if (d > worst) { worst = d; wl = local; }
                    }
                if (worst > 0.05f)
                    fails.Add($"(B) {tag}: the affine map and GetWorldToScreen disagree by {worst:0.000}px at local {wl.X:0},{wl.Y:0}");

                // ── (C) ROUND TRIP. Pointing is the inverse of drawing or the buttons are not
                // where they look.
                float rworst = 0f;
                for (float ly = 400f; ly <= 820f; ly += 35f)
                    for (float lx = 0f; lx <= Cfg.ScreenW; lx += 40f)
                    {
                        var local = new Vector2(lx, ly);
                        if (!Unproject(p, cam, Project(p, cam, local), out Vector2 back))
                        { fails.Add($"(C) {tag}: Unproject refused an on-panel point"); break; }
                        rworst = MathF.Max(rworst, Vector2.Distance(local, back));
                    }
                if (rworst > 0.05f) fails.Add($"(C) {tag}: local -> screen -> local drifts {rworst:0.000}px");

                // ── (D) THE RAY. Unproject is a 2x2 inverse; a headset will hand over a RAY. The
                // two are only the same answer because the camera is orthographic, so measure it
                // rather than assert it — this is the leg that would go red the day a perspective
                // camera is introduced, which is exactly when someone needs to be told.
                float rayWorst = 0f;
                for (float ly = 420f; ly <= 800f; ly += 60f)
                    for (float lx = 60f; lx <= Cfg.ScreenW - 60f; lx += 90f)
                    {
                        var screen = Project(p, cam, new Vector2(lx, ly));
                        var r = Raylib.GetScreenToWorldRay(screen, cam);
                        var n = p.Normal;
                        float denom = Vector3.Dot(r.Direction, n);
                        if (MathF.Abs(denom) < 1e-6f) { fails.Add($"(D) {tag}: a ray ran parallel to the console"); continue; }
                        var hit = r.Position + r.Direction * (Vector3.Dot(p.Origin - r.Position, n) / denom);
                        var d3 = hit - p.Origin;
                        float a = Vector3.Dot(p.U, p.U), b = Vector3.Dot(p.U, p.V), c = Vector3.Dot(p.V, p.V);
                        float du = Vector3.Dot(p.U, d3), dv = Vector3.Dot(p.V, d3);
                        float det = a * c - b * b;
                        var viaRay = new Vector2((c * du - b * dv) / det, (a * dv - b * du) / det);
                        Unproject(p, cam, screen, out Vector2 viaMatrix);
                        rayWorst = MathF.Max(rayWorst, Vector2.Distance(viaRay, viaMatrix));
                    }
                if (rayWorst > 0.05f)
                    fails.Add($"(D) {tag}: the ray/plane hit and the affine inverse disagree by {rayWorst:0.000}px");

                // ── (E) THE ANCHOR AND THE SQUASH. The pivot row must land exactly where the flat
                // bar's top edge is (that is what keeps the verbs under the player's hand), local
                // +x must carry NO shear and NO scale, and local +y must be foreshortened by
                // sin(pitch) — the proof that the console is a HORIZONTAL surface in the world and
                // not a camera-facing panel wearing a table's clothes.
                Axes(p, cam, out Vector2 ax, out Vector2 ay, out _);
                if (Vector2.Distance(ax, new Vector2(1f, 0f)) > 0.01f)
                    fails.Add($"(E) {tag}: local +x projects to {ax.X:0.000},{ay.Y:0.000} — expected 1,0");
                float want = MathF.Sin(pd * MathF.PI / 180f);
                if (MathF.Abs(ay.X) > 0.01f || MathF.Abs(ay.Y - want) > 0.01f)
                    fails.Add($"(E) {tag}: local +y projects to {ay.X:0.000},{ay.Y:0.000} — expected 0,{want:0.000}");
                foreach (float sx in new[] { 0f, 300f, 980f })
                {
                    var landed = Project(p, cam, new Vector2(sx, PivotY));
                    if (Vector2.Distance(landed, new Vector2(sx, PivotY)) > 0.05f)
                        fails.Add($"(E) {tag}: the pivot row moved — local {sx:0},{PivotY:0} landed at {landed.X:0.0},{landed.Y:0.0}");
                }
            }

            // ── (F) THE REFUSAL. A shallow camera makes a horizontal console edge-on, and an
            // illegible control panel is worse than a flat one. The gate must REFUSE, and the
            // refusal must leave the pointer untouched so the bar is still clickable.
            {
                Reset();
                var shallow = Cam(12f, 0f, 1f, 0f, 0f);
                if (Console(shallow, PivotY, out _)) fails.Add("(F) the console accepted a 12-degree camera");
                if (Begin(Slot.ActionBar, shallow, PivotY)) { End(); fails.Add("(F) Begin pushed a surface at 12 degrees"); }
                var probe = new Vector2(412f, 701f);
                if (PointerIn(Slot.ActionBar, probe) != probe)
                    fails.Add("(F) a refused placement remapped the pointer anyway");
                // and the gate is a FLOOR, not a blanket refusal: one degree of pitch either side
                // of the sin(pitch)=MinSquash crossing must decide differently.
                float crossing = MathF.Asin(MinSquash) * 180f / MathF.PI;
                if (Console(Cam(crossing - 3f, 0f, 1f, 0f, 0f), PivotY, out _)) fails.Add("(F) accepted below the squash floor");
                if (!Console(Cam(crossing + 3f, 0f, 1f, 0f, 0f), PivotY, out _)) fails.Add("(F) refused above the squash floor");
            }

            // ── (G) THE rlgl CONVENTION. The matrix is the TRANSPOSE of System.Numerics and that
            // has been measured, never assumed, since P34. Draw a mark through the pushed stack
            // and read the framebuffer where the map says it went.
            {
                Reset();
                var cam = Cam(52f, 0f, 1f, 0f, 0f);
                if (!Console(cam, PivotY, out Panel p)) fails.Add("(G) no console to draw on");
                else
                {
                    var local = new Vector2(Cfg.ScreenW * 0.5f, PivotY + 40f);
                    var want = Project(p, cam, local);
                    Raylib.BeginDrawing();
                    Raylib.ClearBackground(Pal.RGBA(0, 0, 0));
                    Rlgl.PushMatrix();
                    Rlgl.MultMatrixf(Matrix(p, cam));
                    Raylib.DrawRectangle((int)local.X - 6, (int)local.Y - 6, 13, 13, Pal.RGBA(255, 0, 255));
                    Rlgl.PopMatrix();
                    Raylib.EndDrawing();
                    var img = Raylib.LoadImageFromScreen();
                    var hit = Raylib.GetImageColor(img, (int)want.X, (int)want.Y);
                    if (hit.R < 200 || hit.B < 200)
                        fails.Add($"(G) nothing drawn at the projected point {want.X:0},{want.Y:0} — rlgl matrix convention");
                    Raylib.UnloadImage(img);
                }
            }

            // ── (H) END TO END, ON THE REAL BAR. Take the verbs the game actually built, put the
            // pointer on a button's CENTRE as it appears on the console, and check the pointer
            // maps back into that button's rect and no other. This is the leg that fails if the
            // draw and the hit-test ever disagree about which surface they are on.
            {
                Reset();
                var game = new Game { NoPersist = true };
                game.StartMission(1);
                var u = game.Players.FirstOrDefault(pl => pl.Alive && !pl.IsVip);
                if (u == null) fails.Add("(H) no squad to build a bar from");
                else
                {
                    game.Selected = u;
                    var cam = Cam(52f, 0f, 1f, 0f, 0f);
                    Raylib.BeginDrawing();
                    bool placed = Begin(Slot.ActionBar, cam, PivotY);
                    var bar = Hud.ProbeActionBar(game);
                    if (placed) End();
                    Raylib.EndDrawing();

                    if (!placed) fails.Add("(H) the bar drew on the screen with the table placement set");
                    else if (bar.Length < 8) fails.Add($"(H) only {bar.Length} verbs on the bar");
                    else
                    {
                        int miss = 0, cross = 0;
                        foreach (var b in bar)
                        {
                            var centre = new Vector2(b.Rect.X + b.Rect.Width * 0.5f, b.Rect.Y + b.Rect.Height * 0.5f);
                            var screen = Project(p: _panelOf(Slot.ActionBar), cam: cam, local: centre);
                            var back = PointerIn(Slot.ActionBar, screen);
                            if (!Raylib.CheckCollisionPointRec(back, b.Rect)) { if (++miss <= 2) fails.Add($"(H) '{b.Id}' centre did not map back into its own rect"); continue; }
                            foreach (var o in bar)
                                if (!o.Id.Equals(b.Id, StringComparison.Ordinal) && Raylib.CheckCollisionPointRec(back, o.Rect)) cross++;
                        }
                        if (miss > 2) fails.Add($"(H) {miss} verbs missed their own rect");
                        if (cross > 0) fails.Add($"(H) {cross} pointer hits landed in a second verb's rect");

                        // THE HUD SEAM, AND IT IS WRITTEN TO BE FALSIFIABLE. `Hud.Mouse()` is what
                        // all ~30 hover tests read, so the hover a player sees and the click
                        // `Game` resolves agree only if it maps too.
                        //
                        // THE FIRST VERSION OF THIS LEG PASSED WITH THE FEATURE DELETED — P35's
                        // mistake, repeated: it pinned the pointer at a verb's PROJECTED centre and
                        // asked whether the answer landed in that verb's rect, and an UNMAPPED
                        // pointer lands there too, because the console's squash moves a point 20 px
                        // below the pivot by ~4 px and the rect is 30 px tall. A test whose
                        // discriminator is smaller than its tolerance is not a test. So: assert the
                        // ROUTE (Hud.Mouse() is exactly what the surface says, not what the OS
                        // says), and assert the two actually DIFFER, which is what makes the first
                        // assertion mean something.
                        {
                            var b0 = bar.OrderByDescending(x => MathF.Abs(x.Rect.Y + x.Rect.Height * 0.5f - PivotY)).First();
                            var centre0 = new Vector2(b0.Rect.X + b0.Rect.Width * 0.5f, b0.Rect.Y + b0.Rect.Height * 0.5f);
                            var savedPin = Hud.MousePin;
                            var pinned = Project(_panelOf(Slot.ActionBar), cam, centre0);
                            Hud.MousePin = pinned;
                            Raylib.BeginDrawing();
                            bool p2 = Begin(Slot.ActionBar, cam, PivotY);
                            var seen = Hud.Mouse();
                            var seenRaw = Hud.RawMouse();
                            if (p2) End();
                            var seenOut = Hud.Mouse();
                            Raylib.EndDrawing();
                            Hud.MousePin = savedPin;

                            if (!p2) fails.Add("(H) the hover probe could not place the console");
                            else
                            {
                                var want = PointerIn(Slot.ActionBar, pinned);
                                if (Vector2.Distance(seen, want) > 0.01f)
                                    fails.Add($"(H) Hud.Mouse() read {seen.X:0.0},{seen.Y:0.0} inside the bind; the surface says {want.X:0.0},{want.Y:0.0}");
                                if (Vector2.Distance(seen, seenRaw) < 1f)
                                    fails.Add($"(H) the mapped and raw pointers are {Vector2.Distance(seen, seenRaw):0.00}px apart — nothing was being discriminated");
                                if (!Raylib.CheckCollisionPointRec(seen, b0.Rect))
                                    fails.Add($"(H) the mapped pointer missed '{b0.Id}', the verb it was aimed at");
                            }
                            if (seenOut != seenRaw)
                                fails.Add("(H) Hud.Mouse() stayed remapped after the bind ended");
                        }

                        // and a point off the console's near edge must hit NOTHING — a pointer that
                        // clamps turns the whole margin into a live button.
                        var far = new Vector2(-4000f, -4000f);
                        var offBack = PointerIn(Slot.ActionBar, far);
                        if (bar.Any(b => Raylib.CheckCollisionPointRec(offBack, b.Rect)))
                            fails.Add("(H) a pointer far off the console still hit a verb");
                    }
                }
            }
        }
        finally
        {
            Reset();
            ActionBarPlacement = savedPlacement;
            View3D.Enabled = savedEnabled;
            View3D.PitchDeg = savedP; View3D.YawDeg = savedY; View3D.Zoom = savedZ; View3D.Pan = savedPan;
        }

        return fails.Count == 0
            ? $"SURFACETEST: PASS (screen placement is the identity in 3 configurations; {states.Length} camera states "
              + "affine vs GetWorldToScreen, round-tripped, and agreeing with a ray/plane hit; the pivot row is "
              + $"pinned and local +y is foreshortened by sin(pitch); the squash floor refuses at {MinSquash:0.00} "
              + "and leaves the pointer alone; the rlgl transpose is read off the framebuffer; every verb on the "
              + "real bar maps back into its own rect and no other)"
            : "SURFACETEST: FAIL (" + string.Join(" | ", fails.Distinct()) + ")";
    }

    /// Harness only — leg (H) needs the panel `Begin` cached so it can project a button's own rect.
    static Panel _panelOf(Slot s) => _panel[(int)s];
}
