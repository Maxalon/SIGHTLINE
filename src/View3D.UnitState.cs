using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;

namespace Sightline;

/// SIGHTLINE_UNITSTATETEST — P46. A unit's own state, at the unit, in the projected view.
///
/// Every leg is a DIFFERENTIAL over the framebuffer: render the same frame twice, changing exactly
/// one piece of unit state, and count the pixels that moved. That shape is chosen deliberately —
/// it needs no knowledge of what a badge looks like, so it cannot go stale when one is restyled,
/// and it fails loudly the day a state stops reaching the screen. It is also why the clock is
/// pinned on both `Renderer` and `Hud`: half these badges pulse, and an unpinned pair of frames
/// differs by hundreds of pixels for reasons that have nothing to do with the state under test.
///
/// The pre-P46 arm (`UnitState = false`) is the control on every leg. Without it, "the pixels
/// changed" would be satisfied by the chip disc alone.
public static partial class View3D
{
    public static string UnitStateSelfTest()
    {
        var fails = new List<string>();
        var detail = new System.Text.StringBuilder();

        float savedP = PitchDeg, savedY = YawDeg, savedZ = Zoom;
        var savedPan = Pan;
        bool savedEnabled = Enabled, savedState = UnitState, savedDecal = DecalLayer;
        double savedRT = Renderer.TimePin, savedHT = Hud.TimePin;
        var savedMouse = Hud.MousePin;

        var game = new Game { NoPersist = true };
        game.StartMission(1);
        var grid = game.Grid;
        float aspect = (float)Cfg.ScreenW / Cfg.ScreenH;

        try
        {
            Enabled = true; UnitState = true; DecalLayer = false;   // the decal sheet is P44's leg, not this one
            Renderer.TimePin = 12.0; Hud.TimePin = 12.0;
            Hud.MousePin = new Vector2(-9999, -9999);               // no hover state anywhere near a unit
            PitchDeg = 46f; YawDeg = 0f; Zoom = 1f; Pan = Vector2.Zero; ClampPan(grid);

            var foe = game.Enemies.FirstOrDefault(e => e.Alive);
            var pal = game.Players.FirstOrDefault(p => p.Alive && !p.IsVip);
            if (foe == null || pal == null) { fails.Add("no units to stage"); goto done; }

            // `Shown` drops a hostile the squad has not seen, so discovery is off here or every leg
            // measures an empty box for the right reason and the wrong test.
            Vision.Enabled = false;

            Camera3D Cam() => MakeCamera(grid, aspect);

            Vector2 AnchorOf(Unit u)
            {
                var cw = ChipWorld(grid, u);
                return Raylib.GetWorldToScreen(cw with { Y = cw.Y + ChipFloat + ChipHPublic + 0.05f }, Cam());
            }

            Image Frame()
            {
                var cam = Cam();
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Pal.Bg);
                Raylib.BeginMode3D(cam);
                DrawTerrain(grid);
                DrawChips(grid, AllUnitsPublic(game));
                Raylib.EndMode3D();
                DrawMarkers(grid, AllUnitsPublic(game), cam);
                DrawUnitState(game, AllUnitsPublic(game), cam);
                Raylib.EndDrawing();
                return Raylib.LoadImageFromScreen();
            }

            // Pixels that differ inside a box around one unit. The box is the band the badges use:
            // 60px above the anchor for pips and tags, 45 below for the chip row.
            int DiffNear(Image a, Image b, Vector2 at)
            {
                int x0 = Math.Max(0, (int)at.X - 70), x1 = Math.Min(a.Width - 1, (int)at.X + 70);
                int y0 = Math.Max(0, (int)at.Y - 60), y1 = Math.Min(a.Height - 1, (int)at.Y + 45);
                int n = 0;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var ca = Raylib.GetImageColor(a, x, y); var cb = Raylib.GetImageColor(b, x, y);
                        if (ca.R != cb.R || ca.G != cb.G || ca.B != cb.B) n++;
                    }
                return n;
            }

            /// Change one thing, measure the frame, put it back — with and without the feature.
            void Leg(string tag, Unit u, Action apply, Action undo, int floorPx)
            {
                var anchor = AnchorOf(u);

                UnitState = true;
                var before = Frame();
                apply();
                var after = Frame();
                undo();
                int on = DiffNear(before, after, anchor);
                Raylib.UnloadImage(before); Raylib.UnloadImage(after);

                UnitState = false;
                var cBefore = Frame();
                apply();
                var cAfter = Frame();
                undo();
                int off = DiffNear(cBefore, cAfter, anchor);
                Raylib.UnloadImage(cBefore); Raylib.UnloadImage(cAfter);
                UnitState = true;

                detail.Append($"{tag} {on}/{off}px; ");
                if (on < floorPx) fails.Add($"({tag}) the state moved only {on}px near the unit — it is not reaching the screen");
                if (off >= on) fails.Add($"({tag}) the pre-P46 arm moved {off}px too — this leg is measuring something other than the badges");
            }

            // ── (A) A HOSTILE'S HEALTH. The headline: before P46 an enemy had no HP anywhere on
            // screen, in either view, because the roster strip is friendlies only.
            int fhp = foe.Hp;
            Leg("foeHp", foe, () => foe.Hp = 1, () => foe.Hp = fhp, 12);

            // ── (B) A FRIENDLY'S HEALTH, which the roster DOES carry — so this one is about
            // putting it where the fight is, not about it existing at all.
            int php = pal.Hp;
            Leg("palHp", pal, () => pal.Hp = 1, () => pal.Hp = php, 12);

            // ── (C) THE STANCE BADGES, one per state the flat view distinguishes.
            Leg("overwatch", pal, () => pal.OnOverwatch = true, () => pal.OnOverwatch = false, 20);
            Leg("hunker", pal, () => pal.Hunkered = true, () => pal.Hunkered = false, 20);
            Leg("suppress", foe, () => foe.Suppress = 2, () => foe.Suppress = 0, 20);
            Leg("routed", foe, () => foe.Routed = 2, () => foe.Routed = 0, 20);

            // ── (D) THE STATUS CHIP ROW — the late opaque pass, below the unit.
            Leg("burning", foe, () => foe.AddStatus(StatusKind.Burning, 2),
                                 () => foe.Statuses.Clear(), 60);

            // ── (E) IT IS ANCHORED TO THE UNIT, NOT TO A CORNER. Move the piece a tile and the ink
            // has to move with it by exactly the projected delta — the one thing a differential
            // cannot see on its own.
            {
                foe.AddStatus(StatusKind.Burning, 2);
                var a0 = AnchorOf(foe);
                var f0 = Frame();
                int ox = foe.X, oy = foe.Y;
                // TOWARD THE MIDDLE, not "+2": hostiles deploy on the far edge, so a blind `+2`
                // clamped to the same tile and the leg reported "the state is not following it" for
                // a unit that had not moved. It is the first version of this leg's own failure.
                foe.X = Util.Clamp(ox > grid.W / 2 ? ox - 3 : ox + 3, 0, grid.W - 1); foe.SyncPos();
                if (foe.X == ox) fails.Add("(E) the probe hostile could not be moved");
                var a1 = AnchorOf(foe);
                var f1 = Frame();
                foe.X = ox; foe.Y = oy; foe.SyncPos();
                foe.Statuses.Clear();

                int atOld = DiffNear(f0, f1, a0), atNew = DiffNear(f0, f1, a1);
                Raylib.UnloadImage(f0); Raylib.UnloadImage(f1);
                detail.Append($"moved old {atOld}px new {atNew}px; ");
                if (Vector2.Distance(a0, a1) < 30f)
                    fails.Add("(E) two tiles apart project less than 30px apart — the leg cannot discriminate");
                if (atOld < 40 || atNew < 40)
                    fails.Add($"(E) moving the unit changed {atOld}px at its old anchor and {atNew}px at its new one — the state is not following it");
            }

            done: ;
        }
        finally
        {
            UnitState = savedState; DecalLayer = savedDecal; Enabled = savedEnabled;
            Renderer.TimePin = savedRT; Hud.TimePin = savedHT; Hud.MousePin = savedMouse;
            PitchDeg = savedP; YawDeg = savedY; Zoom = savedZ; Pan = savedPan;
        }

        return fails.Count == 0
            ? "UNITSTATETEST: PASS (a hostile's HP, a friendly's HP, overwatch, hunker, suppression, rout and a "
              + "status chip row all reach the projected view, none of them do with the feature off, and the ink "
              + "follows the unit when it moves) [" + detail.ToString().TrimEnd() + "]"
            : "UNITSTATETEST: FAIL (" + string.Join(" | ", fails.Distinct()) + ") [" + detail.ToString().TrimEnd() + "]";
    }
}
