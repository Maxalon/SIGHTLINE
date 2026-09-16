using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// PROGRAM PARALLAX — wave P43 "THE SURFACE".
///
/// WHERE A UI PANEL LIVES IS A POLICY, NOT A HARDCODE.
///
/// The question this answers is the one a hybrid flat/VR build forces: in the flat game the action
/// bar is a strip of screen pixels the player points a mouse at, and in a headset there is no
/// screen to point at — there is a room, a ray from a hand, and whatever physical thing that ray
/// lands on. Those look like two different UI systems. They are not. They are the SAME panel on
/// two different SURFACES:
///
///   * the SCREEN surface — local pixels ARE screen pixels; the map is the identity.
///   * a WORLD surface   — a plane sitting in the room; local pixels are positions on that plane.
///
/// And under the projected view's ORTHOGRAPHIC camera, plane -> screen is AFFINE (P34's finding,
/// which this wave generalises: P34's bridge is this file's `Panel` with the ground plane in it).
/// So one 3x2 matrix carries an ENTIRE 2D panel onto a surface in the world with no call site
/// rewritten, and its INVERSE turns a pointer back into the panel's own coordinates — which is
/// exactly the operation a VR controller ray performs. `Hud`'s ~6,900 lines of screen-space layout
/// and `UiButton`'s rects are already the data a VR build needs; what was missing was the seam
/// that says WHICH plane they are on, and that seam is here.
///
/// ── THE CONSOLE ───────────────────────────────────────────────────────────────────────────────
/// The table surface this wave ships is the board's OWN floor plane, continued toward the operator
/// past the near edge of the hologram. That choice is deliberate and it is what makes it read as a
/// surface rather than as a squashed 2D bar: because the console is horizontal in the WORLD, its
/// foreshortening is the board's foreshortening, and it changes as the camera tilts. A
/// camera-facing panel would be pixel-identical at every pitch, i.e. it would be a HUD wearing a
/// table's clothes.
///
/// It is ANCHORED TO THE SCREEN (the pivot line lands where the bar's top edge is today) and lies
/// on a WORLD plane. Both halves are load-bearing: the anchor is what keeps the verbs in the place
/// a player's hand already goes, and the plane is what makes them objects. A later wave that wants
/// a world-locked rim you can orbit around changes exactly one function — `Surface.Console` — and
/// nothing else in this file or any call site moves.
///
/// ── WHAT THIS IS NOT ──────────────────────────────────────────────────────────────────────────
/// It is not VR. Raylib-cs 8.0's VR surface is `BeginVrStereoMode` / `LoadVrStereoConfig` /
/// `VrDeviceInfo` — STEREO RENDERING AND BARREL DISTORTION ONLY. There is no OpenXR binding, no
/// head pose, and no controller input anywhere in it, so a real headset mode needs an external
/// binding (Silk.NET.OpenXR or similar) and that is a dependency decision, not a wave. What this
/// file does is make the UI side of that decision cheap: when a pose and a ray arrive, they come
/// in through `Unproject`, and every panel already knows how to be somewhere.
public static partial class Surface
{
    /// Which kind of plane a panel is drawn on.
    public enum Kind { Screen, Table }

    /// The panels that can be placed independently. `Chrome` is everything that has no policy yet
    /// (roster, cards, log, modal screens) and is always `Screen`.
    public enum Slot { Chrome = 0, ActionBar = 1 }
    const int SlotCount = 2;

    /// THE POLICY. `SIGHTLINE_UISURFACE=table` moves the action bar onto the console; the default
    /// is the flat game, unchanged byte-for-byte (leg A asserts the identity rather than assuming
    /// it). It only ever applies in the projected view — there is no world to put a plane in when
    /// the flat renderer is drawing.
    public static Kind ActionBarPlacement = Kind.Screen;

    /// The console's world height. The board's floor is Y=0, so this puts the table top in the
    /// hologram's own plane: the projection sits ON the console rather than floating over a second
    /// unexplained surface.
    public const float ConsoleY = 0f;

    /// The readability floor. A horizontal plane's vertical squash is sin(pitch), so at a shallow
    /// camera the console goes edge-on and its labels collapse. Below this the placement REFUSES
    /// and the bar falls back to the screen — an unusable control panel is worse than a flat one.
    /// (In a headset this gate does not exist: you lean.)
    public const float MinSquash = 0.45f;

    // ── The plane ────────────────────────────────────────────────────────────────────────────

    /// A UI plane, given as the world position of local (0,0) and the world delta of ONE local
    /// pixel along each local axis. Storing the axes per-PIXEL rather than as unit vectors plus a
    /// scale is what makes `World` one multiply-add and keeps the panel's units identical to the
    /// screen units every existing layout call site is already written in.
    public readonly struct Panel
    {
        public readonly Vector3 Origin, U, V;
        public Panel(Vector3 origin, Vector3 u, Vector3 v) { Origin = origin; U = u; V = v; }
        public Vector3 World(Vector2 local) => Origin + U * local.X + V * local.Y;
        public Vector3 Normal => Vector3.Normalize(Vector3.Cross(U, V));
        public bool Valid => Vector3.Cross(U, V).LengthSquared() > 1e-16f;
    }

    /// Build the console: the horizontal world plane whose local (x, pivotY) lands on screen
    /// (x, pivotY). Returns false — rather than something degenerate — for every camera the plane
    /// cannot be honestly placed under: a straight-down view (no horizontal "away" direction), a
    /// ray that never reaches the plane, and a pitch shallow enough to make the labels illegible.
    /// The caller's answer to false is always the same: use the screen.
    public static bool Console(Camera3D cam, float pivotY, out Panel panel)
    {
        panel = default;
        var fwd = cam.Target - cam.Position;
        if (fwd.LengthSquared() < 1e-12f) return false;
        fwd = Vector3.Normalize(fwd);

        var right = Vector3.Cross(fwd, cam.Up);
        if (right.LengthSquared() < 1e-12f) return false;
        right = Vector3.Normalize(right);

        var flat = new Vector3(fwd.X, 0f, fwd.Z);                  // the horizontal heading
        if (flat.LengthSquared() < 1e-8f) return false;            // looking straight down
        flat = Vector3.Normalize(flat);

        float ppu = View3D.PixelsPerUnit(cam);
        if (!(ppu > 0f)) return false;

        // The pivot, solved rather than assumed: where the plane actually is under that screen row.
        var r = Raylib.GetScreenToWorldRay(new Vector2(0f, pivotY), cam);
        if (MathF.Abs(r.Direction.Y) < 1e-4f) return false;
        float t = (ConsoleY - r.Position.Y) / r.Direction.Y;
        if (t < 0f || t > 1e5f || float.IsNaN(t)) return false;
        var pivot = r.Position + r.Direction * t;

        // local +x runs screen-right along the camera's own right axis (perpendicular to the view,
        // so it projects at exactly 1 px per local px and carries NO shear); local +y runs toward
        // the operator, which on a horizontal plane is down the screen and foreshortened.
        var u = right / ppu;
        var v = -flat / ppu;
        var cand = new Panel(pivot - v * pivotY, u, v);
        if (!cand.Valid) return false;

        Axes(cand, cam, out _, out Vector2 ay, out _);
        if (ay.Length() < MinSquash) return false;
        panel = cand;
        return true;
    }

    // ── The affine map, and its inverse ──────────────────────────────────────────────────────

    /// screen = ax * local.X + ay * local.Y + t. Three projections is the whole map, because the
    /// camera is ORTHOGRAPHIC — the same argument P34 made for the ground plane, which holds for
    /// any plane: an ortho projection is linear, so its restriction to a plane is affine.
    /// THE BASELINE IS NOT COSMETIC. `GetWorldToScreen` returns single-precision screen pixels of
    /// magnitude ~1e3, so each one carries ~1e-4 px of representation error; differencing two of
    /// them one LOCAL PIXEL apart divides that error by 1 and then the map multiplies it by the
    /// panel's width. Measured: a unit baseline put the far corner of a 1280-px panel 0.14 px out.
    /// Sampling the axis 512 px away divides the same error by 512 instead. Same map, three orders
    /// of magnitude less noise.
    const float AxisBaseline = 512f;

    public static void Axes(Panel p, Camera3D cam, out Vector2 ax, out Vector2 ay, out Vector2 t)
    {
        var p0 = Raylib.GetWorldToScreen(p.Origin, cam);
        ax = (Raylib.GetWorldToScreen(p.Origin + p.U * AxisBaseline, cam) - p0) / AxisBaseline;
        ay = (Raylib.GetWorldToScreen(p.Origin + p.V * AxisBaseline, cam) - p0) / AxisBaseline;
        t = p0;
    }

    /// rlgl wants the TRANSPOSE of the System.Numerics convention — translation in M14/M24. Same
    /// hazard `View3D.BoardPxMatrix` documents; the assertion lives in the self-test, not here.
    public static Matrix4x4 Matrix(Panel p, Camera3D cam)
    {
        Axes(p, cam, out Vector2 ax, out Vector2 ay, out Vector2 t);
        var m = new Matrix4x4();
        m.M11 = ax.X; m.M12 = ay.X; m.M13 = 0f; m.M14 = t.X;
        m.M21 = ax.Y; m.M22 = ay.Y; m.M23 = 0f; m.M24 = t.Y;
        m.M33 = 1f; m.M44 = 1f;
        return m;
    }

    public static Vector2 Project(Panel p, Camera3D cam, Vector2 local)
    {
        Axes(p, cam, out Vector2 ax, out Vector2 ay, out Vector2 t);
        return ax * local.X + ay * local.Y + t;
    }

    /// A screen point back to the panel's own coordinates — the pointing half, and the one a VR
    /// controller ray will use unchanged once it can hand over a screen-equivalent intersection.
    /// It is the 2x2 inverse rather than a ray/plane intersection because under an ortho camera
    /// the two are the SAME answer and this one needs no ray; the self-test proves they agree.
    public static bool Unproject(Panel p, Camera3D cam, Vector2 screen, out Vector2 local)
    {
        Axes(p, cam, out Vector2 ax, out Vector2 ay, out Vector2 t);
        float det = ax.X * ay.Y - ax.Y * ay.X;
        if (MathF.Abs(det) < 1e-9f) { local = default; return false; }
        var d = screen - t;
        local = new Vector2(( ay.Y * d.X - ay.X * d.Y) / det,
                            (-ax.Y * d.X + ax.X * d.Y) / det);
        return true;
    }

    // ── Binding ──────────────────────────────────────────────────────────────────────────────
    // A panel's rects are cached by its DRAW and hit-tested by the NEXT frame's input — which is
    // how `Hud.ActionButtons` has always worked. The panel it was drawn on is cached the same way
    // and for the same reason, so the pointer and the rects are always from one frame.

    static readonly Panel[] _panel = new Panel[SlotCount];
    static readonly Camera3D[] _cam = new Camera3D[SlotCount];
    static readonly bool[] _placed = new bool[SlotCount];

    static bool _active;
    static Slot _current = Slot.Chrome;

    /// True only between a successful `Begin` and its `End`. `Hud.Mouse()` reads it.
    public static bool Active => _active;
    public static Slot Current => _current;
    public static bool IsPlaced(Slot s) => _placed[(int)s];

    public static Kind PlacementOf(Slot s)
        => s == Slot.ActionBar && View3D.Enabled ? ActionBarPlacement : Kind.Screen;

    /// Push the slot's surface. Returns false when the slot is on the screen (the common case and
    /// the default), in which case NOTHING has been pushed and the caller draws exactly as before.
    public static bool Begin(Slot s, Camera3D cam, float pivotY)
    {
        int i = (int)s;
        _placed[i] = false;
        if (PlacementOf(s) != Kind.Table) return false;
        if (!Console(cam, pivotY, out Panel p)) return false;

        _panel[i] = p; _cam[i] = cam; _placed[i] = true;
        _current = s; _active = true;
        Rlgl.PushMatrix();
        Rlgl.MultMatrixf(Matrix(p, cam));
        return true;
    }

    public static void End()
    {
        if (!_active) return;
        Rlgl.PopMatrix();
        _active = false;
        _current = Slot.Chrome;
    }

    /// The pointer in the coordinates of whatever is currently bound. Outside a bind — which is
    /// every chrome draw site and the whole flat game — this is the identity.
    public static Vector2 MapPointer(Vector2 screen)
        => _active ? PointerIn(_current, screen) : screen;

    /// The pointer in one named slot's coordinates, for input handling, which runs outside the
    /// draw. A slot that is on the screen returns the pointer untouched; a slot on a surface the
    /// pointer cannot reach returns a point no rect contains, never a clamped one — a picker that
    /// clamps makes the whole margin act like a live button.
    public static Vector2 PointerIn(Slot s, Vector2 screen)
    {
        int i = (int)s;
        if (!_placed[i]) return screen;
        return Unproject(_panel[i], _cam[i], screen, out Vector2 local) ? local : new Vector2(-1e6f, -1e6f);
    }

    /// Harness only: forget every placement, so a self-test or a mode change cannot inherit one.
    public static void Reset()
    {
        for (int i = 0; i < SlotCount; i++) _placed[i] = false;
        _active = false; _current = Slot.Chrome;
    }
}
