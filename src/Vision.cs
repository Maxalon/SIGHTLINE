using System;
using System.Collections.Generic;

namespace Sightline;

/// ════════════════════ P30 — THE DISCOVERY LAYER ════════════════════
/// What HQ knows, as opposed to what is there. Three states per surface, and the middle one is
/// the whole point:
///
///   UNSEEN     never observed. Not drawn AT ALL — not dimmed, not hinted. The operator is not
///              told there is something they cannot see; the board simply stops.
///   REMEMBERED observed earlier, not in sight now. Drawn from MEMORY, so it can be stale.
///   VISIBLE    in a soldier's line of sight this instant.
///
/// THE UNIT OF KNOWLEDGE IS A FACE, NOT A TILE. A wall observed from one side is recorded on that
/// side only, so the operator sees a single plane and cannot read its thickness — they have not
/// been round the back. That falls out of the edge layer P28 built: a boundary already has two
/// sides and one home, so knowledge about it has two slots.
///
/// IT READS THE GAME'S OWN SIGHT, NOT A SECOND MODEL. Visibility comes from
/// `Grid.HasLineOfSight`, the same predicate that decides whether a soldier can shoot — so what
/// the operator is shown and what the rules permit can never drift apart. A LiDAR-style ray sweep
/// would give finer, partial coverage of each surface (prototypes/lidar shows what that looks
/// like); it would also be a SECOND visibility model, and this project's whole architecture is
/// that both teams read one truth. Fidelity second, agreement first.
///
/// PRESENTATION ONLY, FOR NOW. Nothing in `Ai`, `Combat` or `Mission` consults this. It decides
/// what is DRAWN. Making the rules honour it is true fog of war and a separate decision — see
/// docs/ROADMAP.md.
public static class Vision
{
    public const byte Unseen = 0, Remembered = 1, Visible = 2;

    /// The owner's rule, recorded in docs/DEVLOG.md §P27-N: nine tiles at your level or above,
    /// one more per level you are looking DOWN. It works on the existing `Grid.Height` scalar, so
    /// elevation buys sight without anyone paying for a 3D data model first.
    public const int BaseSight = 9;

    public static byte[,] Tile;           // [W, H]
    public static byte[,,] FaceV, FaceH;  // [W+1, H, 2] / [W, H+1, 2] — per EDGE FACE
    public static bool Enabled = true;    // SIGHTLINE_DISCOVERY=0 restores "the board is all known"

    static int _w, _h;

    public static void Reset(Grid g)
    {
        _w = g.W; _h = g.H;
        Tile = new byte[_w, _h];
        FaceV = new byte[_w + 1, _h, 2];
        FaceH = new byte[_w, _h + 1, 2];
    }

    public static byte At(int x, int y) =>
        !Enabled ? Visible
        : (Tile == null || x < 0 || y < 0 || x >= _w || y >= _h) ? Unseen : Tile[x, y];

    public static byte FaceVAt(int x, int y, int side) =>
        !Enabled ? Visible
        : (FaceV == null || x < 0 || y < 0 || x > _w || y >= _h) ? Unseen : FaceV[x, y, side];

    public static byte FaceHAt(int x, int y, int side) =>
        !Enabled ? Visible
        : (FaceH == null || x < 0 || y < 0 || x >= _w || y > _h) ? Unseen : FaceH[x, y, side];

    /// Recompute. Everything currently VISIBLE decays to REMEMBERED first, then each living
    /// soldier re-lights what it can see — so a surface the squad has walked away from keeps its
    /// last known state rather than vanishing.
    public static void Refresh(Grid g, List<Unit> units)
    {
        if (!Enabled) return;
        if (Tile == null || _w != g.W || _h != g.H) Reset(g);

        for (int x = 0; x < _w; x++) for (int y = 0; y < _h; y++)
            if (Tile[x, y] == Visible) Tile[x, y] = Remembered;
        for (int x = 0; x <= _w; x++) for (int y = 0; y < _h; y++) for (int s = 0; s < 2; s++)
            if (FaceV[x, y, s] == Visible) FaceV[x, y, s] = Remembered;
        for (int x = 0; x < _w; x++) for (int y = 0; y <= _h; y++) for (int s = 0; s < 2; s++)
            if (FaceH[x, y, s] == Visible) FaceH[x, y, s] = Remembered;

        foreach (var u in units)
        {
            if (u == null || !u.Alive || u.Team != Team.Player) continue;
            int eye = g.HeightAt(u.X, u.Y);
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    // Looking DOWN buys range: +1 per level below the viewer, per the owner's rule.
                    int drop = Math.Max(0, eye - g.HeightAt(x, y));
                    if (Util.TileDist(u.X, u.Y, x, y) > BaseSight + drop) continue;
                    if (!g.HasLineOfSight(u.X, u.Y, x, y)) continue;
                    Tile[x, y] = Visible;

                    // A seen tile reveals the INWARD face of each of its four boundaries — the
                    // side the soldier is standing on. The far side stays unknown, which is what
                    // makes a wall read as a plane of unknown thickness until you go round it.
                    FaceV[x, y, 1] = Visible;
                    if (x + 1 <= _w) FaceV[x + 1, y, 0] = Visible;
                    FaceH[x, y, 1] = Visible;
                    if (y + 1 <= _h) FaceH[x, y + 1, 0] = Visible;
                }
        }
    }

    /// How much of a surface's colour survives at this knowledge level. Remembered terrain is
    /// dimmed rather than recoloured, so brightness alone carries "how well do we know this" and
    /// hue stays free to mean something else later (the LiDAR-vs-camera split in
    /// prototypes/lidar/README.md).
    public static float Dim(byte state) => state == Visible ? 1f : state == Remembered ? 0.42f : 0f;

    // ══════════════════ SIGHTLINE_VISIONTEST ══════════════════
    /// A discovery bug is invisible BY NATURE — the failure mode is that something is drawn which
    /// should not be, or not drawn which should, and both look like a plausible board. The first
    /// build of this layer gated terrain and forgot the UNITS, so hostiles stood out in the black
    /// on unscanned ground; nothing caught it but a screenshot.
    public static string SelfTest()
    {
        var fails = new System.Collections.Generic.List<string>();
        void Is(bool c, string w) { if (!c) fails.Add(w); }
        bool saved = Enabled; Enabled = true;

        var g = new Grid();
        for (int x = 0; x < g.W; x++) for (int y = 0; y < g.H; y++) g.Tiles[x, y] = TileType.Floor;
        g.ClearEdges();
        Reset(g);

        // (A) A fresh board is entirely UNKNOWN. Not dim — unknown.
        Is(At(2, 2) == Unseen && At(g.W - 1, g.H - 1) == Unseen, "A: a fresh board was not unseen");

        var squad = new List<Unit>();
        var u = Mission.TrainingSquad()[0];
        u.X = 1; u.Y = 5; u.SyncPos(); squad.Add(u);

        // (B) Sight reaches BaseSight and stops. The boundary is the claim, not the middle.
        Refresh(g, squad);
        Is(At(1, 5) == Visible, "B: a soldier cannot see its own tile");
        Is(At(1 + BaseSight, 5) == Visible, $"B: nothing at exactly BaseSight ({BaseSight})");
        Is(At(1 + BaseSight + 1, 5) == Unseen, "B: sight reached PAST BaseSight on flat ground");

        // (C) A HIGH WALL casts a shadow. This is the leg that proves vision reads the game's own
        //     Grid.HasLineOfSight rather than a radius — a radius alone would light both tiles.
        for (int y = 0; y < g.H; y++) g.SetEdgeV(4, y, EdgeKind.High);
        Reset(g); Refresh(g, squad);
        Is(At(3, 5) == Visible, "C: the near side of a wall was not seen");
        Is(At(6, 5) == Unseen, "C: sight passed THROUGH a high wall");

        // (D) THE WALL IS KNOWN FROM ONE SIDE ONLY. This is what makes a wall render as a plane of
        //     unreadable thickness until somebody walks round it.
        Is(FaceVAt(4, 5, 0) == Visible, "D: the observed wall face is not known");
        Is(FaceVAt(4, 5, 1) == Unseen, "D: the FAR face of a wall was known without going round it");

        // (E) MEMORY. Walk away and what was seen must go REMEMBERED, never back to unseen — the
        //     middle state is the entire point of the layer.
        // Far enough that (3,5) is genuinely out of range — the first version of this leg moved
        // the soldier to (1,0), which is still five tiles from (3,5) on the SAME side of the wall,
        // so the tile stayed Visible and the test was asserting a wrong expectation rather than
        // catching a wrong behaviour.
        u.X = g.W - 3; u.Y = g.H - 1; u.SyncPos();
        Refresh(g, squad);
        Is(Util.TileDist(u.X, u.Y, 3, 5) > BaseSight, "E: the walk-away tile is still in range");
        Is(At(3, 5) == Remembered, $"E: a tile walked away from read {At(3, 5)}, want Remembered");
        Is(At(u.X, u.Y) == Visible, "E: the tile stood on is not visible");

        // (F) The restore flag makes the whole board known, so nothing can hide behind it.
        Enabled = false;
        Is(At(g.W - 1, g.H - 1) == Visible && FaceVAt(4, 5, 1) == Visible,
           "F: SIGHTLINE_DISCOVERY=0 did not reveal everything");
        Enabled = saved;

        return fails.Count == 0 ? "VISIONTEST: PASS"
                                : "VISIONTEST: FAIL\n  " + string.Join("\n  ", fails);
    }
}
