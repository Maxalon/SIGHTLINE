using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// SIGHTLINE_HOLOTEST — P70, the hologram and the scan layer as the player's default.
///
/// (A) COLOUR DISCIPLINE. Eight biome sets, pairwise distinct KEYs, a KEY and a SECOND that never
///     share a hue, and no terrain colour within reach of the four colours that already MEAN
///     something on this board — the squad's cyan, the hostile's red, the boss's orange and the
///     objective's gold. The owner asked for a lot of colour; this is what keeps a lot of colour
///     from turning into noise. MAGMA's key is exempt by name: ember IS that room.
/// (B) THE PASS IS LIVE: the same staged frame with `Holo` on and off differs substantially.
/// (C) UNSCANNED GROUND DRAWS NOTHING, IN BOTH VIEWS. With the layer on and nothing scanned, the
///     projected board is the bare backing bar its boundary, and the flat board is blanked by the
///     veil — against controls with the layer off, so neither leg can pass by drawing nothing.
/// (D) WHAT YOU CAN SHOOT, YOU CAN SEE. A hostile 15 tiles out on open ground is past the scan but
///     inside a sniper's reach: it is a contact. Behind a high wall it is not. Out of everyone's
///     reach it is not.
/// (E) A NEW MISSION STARTS UNSCANNED. Before P70 the memory carried over between same-sized boards.
/// (F) THE OPENING CAMERA: angled; the home board fits whole; a big board opens zoomed in on the
///     squad; a harness-staged camera is left alone.
public static partial class View3D
{
    public static string HoloSelfTest()
    {
        var fails = new List<string>();
        var detail = new System.Text.StringBuilder();

        // ── (A) colour discipline ─────────────────────────────────────────────────────────────
        float D(Color a, Color b) => MathF.Sqrt((a.R - b.R) * (a.R - b.R) + (a.G - b.G) * (a.G - b.G) + (a.B - b.B) * (a.B - b.B));
        var reserved = new (string n, Color c)[] { ("squad", Pal.Friend), ("hostile", Pal.Foe), ("boss", Pal.Elite),
                                                   ("objective", Pal.VipGold), ("accent", Pal.Accent) };
        const float MinReserved = 55f, MinKeys = 40f, MinPair = 60f;
        var sets = Biome.All.Select(b => (b.Name, HoloFor(b))).ToArray();
        for (int i = 0; i < sets.Length; i++)
        {
            var (n, hs) = sets[i];
            if (D(hs.Key, hs.Second) < MinPair) fails.Add($"(A) {n}: KEY and SECOND are {D(hs.Key, hs.Second):0} apart");
            foreach (var (rn, rc) in reserved)
            {
                if (D(hs.Second, rc) < MinReserved) fails.Add($"(A) {n}: SECOND is {D(hs.Second, rc):0} from the {rn} colour");
                if (n != "MAGMA" && D(hs.Key, rc) < MinReserved) fails.Add($"(A) {n}: KEY is {D(hs.Key, rc):0} from the {rn} colour");
            }
            for (int j = i + 1; j < sets.Length; j++)
                if (D(hs.Key, sets[j].Item2.Key) < MinKeys) fails.Add($"(A) {n} and {sets[j].Name} share a key hue");
        }
        detail.Append($"{sets.Length} sets; ");

        // ── render legs ───────────────────────────────────────────────────────────────────────
        float savedP = PitchDeg, savedY = YawDeg, savedZ = Zoom; var savedPan = Pan;
        bool savedEnabled = Enabled, savedHolo = Holo, savedVision = Vision.Enabled, savedPinned = CameraPinned,
             savedDecal = DecalLayer;
        double savedRT = Renderer.TimePin, savedHT = Hud.TimePin;
        var savedMouse = Hud.MousePin;
        try
        {
            Renderer.TimePin = 12.0; Hud.TimePin = 12.0; Hud.MousePin = new Vector2(-9999, -9999);
            Vision.Enabled = false; Enabled = false; CameraPinned = false;
            var game = new Game { NoPersist = true };
            game.StartMission(1);
            var grid = game.Grid;
            Scene = game.Biome;
            float aspect = (float)Cfg.ScreenW / Cfg.ScreenH;
            DecalLayer = false;
            ResetCamera();

            Image Frame3D()
            {
                var cam = MakeCamera(grid, aspect);
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Pal.Bg);
                Raylib.BeginMode3D(cam);
                DrawTerrain(grid);
                Raylib.EndMode3D();
                Raylib.EndDrawing();
                return Raylib.LoadImageFromScreen();
            }
            Image FrameFlat()
            {
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Pal.Bg);
                Renderer.DrawBoard(game);
                Raylib.EndDrawing();
                return Raylib.LoadImageFromScreen();
            }
            int Diff(Image a, Image b)
            {
                int n = 0;
                for (int y = 0; y < a.Height; y += 2)
                    for (int x = 0; x < a.Width; x += 2)
                    {
                        var ca = Raylib.GetImageColor(a, x, y); var cb = Raylib.GetImageColor(b, x, y);
                        if (ca.R != cb.R || ca.G != cb.G || ca.B != cb.B) n++;
                    }
                return n;
            }
            // share of the BOARD rectangle left at the backing colour (sampled). `tol` is per view:
            // the hologram's plate is deliberately only a shade off the backing, so the 3D legs use
            // a tolerance tight enough to tell a dark plate from no plate.
            float BlankShare(Image a, Rectangle r, int tol = 12)
            {
                int n = 0, tot = 0;
                for (int y = (int)r.Y; y < r.Y + r.Height; y += 3)
                    for (int x = (int)r.X; x < r.X + r.Width; x += 3)
                    {
                        if (x < 0 || y < 0 || x >= a.Width || y >= a.Height) continue;
                        var c = Raylib.GetImageColor(a, x, y); tot++;
                        if (Math.Abs(c.R - Pal.Bg.R) + Math.Abs(c.G - Pal.Bg.G) + Math.Abs(c.B - Pal.Bg.B) <= tol
                            || Math.Abs(c.R - 7) + Math.Abs(c.G - 10) + Math.Abs(c.B - 14) <= 6) n++;
                    }
                return tot == 0 ? 0f : n / (float)tot;
            }

            // (B) live
            Holo = true;  var on = Frame3D();
            Holo = false; var off = Frame3D();
            Holo = true;
            int moved = Diff(on, off), total = (on.Width / 2) * (on.Height / 2);
            Raylib.UnloadImage(on); Raylib.UnloadImage(off);
            detail.Append($"holo vs lit {100f * moved / total:0}% px; ");
            if (moved < total * 0.05f) fails.Add($"(B) the hologram changed only {100f * moved / total:0.0}% of the frame");

            // (C) unscanned draws nothing — 3D
            var boardRect = new Rectangle(Cfg.ScreenW * 0.15f, Cfg.HudTopInset + 10, Cfg.ScreenW * 0.7f, Cfg.ScreenH - Cfg.HudTopInset - Cfg.HudBotInset - 20);
            Vision.Enabled = false;
            var known = Frame3D();
            Vision.Enabled = true; Vision.Reset(grid);
            var dark = Frame3D();
            float kb = BlankShare(known, boardRect, 3), db = BlankShare(dark, boardRect, 3);
            Raylib.UnloadImage(known); Raylib.UnloadImage(dark);
            detail.Append($"3D blank {100 * kb:0}%->{100 * db:0}%; ");
            // Compared as INK, not as a share of the rectangle: the board is a rotated parallelogram,
            // so a fixed share of the screen rectangle is backing whatever is drawn.
            float inkKnown = 1f - kb, inkDark = 1f - db;
            if (inkKnown < 0.2f) fails.Add($"(C) the control drew ink on only {100 * inkKnown:0}% of the board area — the leg cannot tell");
            if (inkDark > inkKnown * 0.05f) fails.Add($"(C) with nothing scanned the projected board still drew {100 * inkDark:0.0}% ink (control {100 * inkKnown:0}%)");

            // (C) unscanned draws nothing — flat (the veil). DrawBoard does not refresh, so the reset
            // state is what it sees; the squad's figures still draw, hence a lower bar than 3D.
            var flatRect = new Rectangle(Cfg.OriginX, Cfg.OriginY, Cfg.BoardW, Cfg.BoardH);
            Vision.Enabled = false; var fKnown = FrameFlat();
            Vision.Enabled = true; Vision.Reset(grid); var fDark = FrameFlat();
            float fkb = BlankShare(fKnown, flatRect), fdb = BlankShare(fDark, flatRect);
            Raylib.UnloadImage(fKnown); Raylib.UnloadImage(fDark);
            detail.Append($"flat blank {100 * fkb:0}%->{100 * fdb:0}%; ");
            if (fdb < 0.70f) fails.Add($"(C) with nothing scanned the flat board is only {100 * fdb:0}% blank — the veil is not drawing");
            if (fkb > 0.3f) fails.Add($"(C) the flat control is already {100 * fkb:0}% blank");

            // ── (D) contacts ────────────────────────────────────────────────────────────────
            {
                for (int x = 0; x < grid.W; x++) for (int y = 0; y < grid.H; y++)
                { grid.Tiles[x, y] = TileType.Floor; grid.Height[x, y] = 0; }
                grid.ClearEdges();
                if (grid.Ground != null) for (int x = 0; x < grid.W; x++) for (int y = 0; y < grid.H; y++) grid.Ground[x, y] = GroundKind.None;
                grid.ClearHazards();
                var sniper = game.Players.First(p => p.Alive && !p.IsVip);
                foreach (var p in game.Players) if (p != sniper) p.Alive = false;   // only one pair of eyes
                var foe = game.Enemies.First(e => e.Alive);
                foreach (var e in game.Enemies) if (e != foe) e.Alive = false;
                sniper.Weapon = Weapon.Make(WeaponKind.Sniper);
                sniper.X = 1; sniper.Y = 5; sniper.SyncPos();
                foe.X = 16; foe.Y = 5; foe.SyncPos();
                Vision.Enabled = true; Vision.Reset(grid);
                void Look() { Vision.Refresh(grid, game.Players.Concat(game.Enemies).ToList()); game.RefreshContacts(); }
                Look();
                if (Vision.At(foe.X, foe.Y) == Vision.Visible) fails.Add("(D) the probe hostile is inside the scan — the leg tests nothing");
                if (!Vision.Shows(foe)) fails.Add("(D) a hostile under a sniper's line of fire is not on the picture");
                for (int y = 0; y < grid.H; y++) grid.SetEdgeV(10, y, EdgeKind.High);
                Look();
                if (Vision.Shows(foe)) fails.Add("(D) a hostile behind a high wall is on the picture");
                grid.ClearEdges();
                sniper.Weapon = Weapon.Make(WeaponKind.Shotgun);
                Look();
                if (Vision.Shows(foe)) fails.Add("(D) a hostile out of every weapon's reach and past the scan is on the picture");
                Vision.Enabled = false; game.RefreshContacts();
                if (!Vision.Shows(foe)) fails.Add("(D) with the layer off a hostile is hidden");
            }

            // ── (E) a new mission starts unscanned ──────────────────────────────────────────
            {
                var g2 = new Game { NoPersist = true };
                Vision.Enabled = true;
                g2.StartMission(1);
                Vision.Refresh(g2.Grid, g2.Players.ToList());
                var p0 = g2.Players.First(p => p.Alive);
                if (Vision.At(p0.X, p0.Y) != Vision.Visible) fails.Add("(E) the squad's own tile is not scanned after a refresh");
                g2.DebugResetupMission();
                int lit = 0;
                for (int x = 0; x < g2.Grid.W; x++) for (int y = 0; y < g2.Grid.H; y++) if (Vision.At(x, y) != Vision.Unseen) lit++;
                if (lit > 0) fails.Add($"(E) the next mission opened with {lit} tiles already scanned from the last");
                Vision.Enabled = false;
            }

            // ── (F) the opening camera ──────────────────────────────────────────────────────
            {
                PitchDeg = 70f; YawDeg = 0f; Zoom = 2f; Pan = new Vector2(3, 3);
                FrameOnSquad(grid, game.Players);
                if (PitchDeg != DefaultPitch || YawDeg != DefaultYaw) fails.Add("(F) the opening framing is not the default angle");
                if (grid.W <= 18 && (Zoom != 1f || Pan != Vector2.Zero)) fails.Add("(F) the home board does not open whole");
                if (YawDeg % 90f == 0f) fails.Add("(F) the default framing is square-on to the grid — boxes show one face");

                int w0 = Cfg.GridW, h0 = Cfg.GridH, t0 = Cfg.Tile;
                try
                {
                    Cfg.SetBoard(40, 24);
                    var big = new Grid();
                    var squad = new List<Unit>();
                    foreach (var p in game.Players.Take(3)) { var u = Mission.TrainingSquad()[0]; squad.Add(u); }
                    for (int i = 0; i < squad.Count; i++) { squad[i].X = 2 + i; squad[i].Y = 20; squad[i].SyncPos(); }
                    FrameOnSquad(big, squad);
                    if (Zoom <= 1.01f) fails.Add($"(F) a 40x24 board opened at zoom {Zoom:0.00} — the whole map, not the squad");
                    if (!(Pan.X < 0f && Pan.Y > 0f)) fails.Add($"(F) a squad in the bottom-left corner opened the camera at pan {Pan.X:0.0},{Pan.Y:0.0}");
                    detail.Append($"40x24 zoom {Zoom:0.0} pan {Pan.X:0},{Pan.Y:0}; ");
                    CameraPinned = true; PitchDeg = 70f; Zoom = 1.5f;
                    FrameOnSquad(big, squad);
                    if (PitchDeg != 70f || Zoom != 1.5f) fails.Add("(F) a pinned camera was re-framed");
                    CameraPinned = false;
                }
                finally { Cfg.SetBoard(w0, h0, t0); }
            }
        }
        catch (Exception e) { fails.Add($"threw: {e.GetType().Name}: {e.Message}"); }
        finally
        {
            PitchDeg = savedP; YawDeg = savedY; Zoom = savedZ; Pan = savedPan;
            Enabled = savedEnabled; Holo = savedHolo; Vision.Enabled = savedVision; CameraPinned = savedPinned;
            DecalLayer = savedDecal; Vision.Contacts.Clear();
            Renderer.TimePin = savedRT; Hud.TimePin = savedHT; Hud.MousePin = savedMouse;
        }

        return fails.Count == 0
            ? "HOLOTEST: PASS (eight biome hologram sets with distinct keys, no terrain colour near the squad / hostile / "
              + "boss / objective colours bar MAGMA's own ember; the pass is live; unscanned ground draws nothing in either "
              + "view; a hostile under a line of fire is shown and one behind a wall or out of reach is not; a new mission "
              + "starts unscanned; the camera opens angled, whole on the home board and on the squad on a big one) ["
              + detail.ToString().TrimEnd() + "]"
            : "HOLOTEST: FAIL (" + string.Join(" | ", fails.Distinct()) + ") [" + detail.ToString().TrimEnd() + "]";
    }
}
