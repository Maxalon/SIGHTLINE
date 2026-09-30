using System;
using System.Collections.Generic;
using System.Linq;

namespace Sightline;

/// SIGHTLINE_BOARDNEUTRALTEST — P56. **BOARD SIZE IS NOT A DIFFICULTY LEVER.**
///
/// The owner's direction (`docs/DESIGN.md` §6.5) is that a bigger board buys COMMITMENT — the
/// dilemma of spending whole turns relocating — and explicitly NOT difficulty: *"we need a
/// difficulty curve very clearly, and map size doesn't do that alone."* The squad does not grow with
/// the board, so the opposition must not either.
///
/// **THIS WAVE STARTED BY GETTING THAT WRONG.** A 36x22 screenshot showing four hostiles on 792
/// tiles was read as a bug, and the force was scaled by board area to "fix" it. The first autoplay
/// on that build lost on mission 1 in 15 turns: 16 hostiles against a 4-soldier squad. The board had
/// been turned into a difficulty dial, which is exactly what it must not be. The scaling was
/// reverted and this gate is what replaced it — the property is now ASSERTED rather than true by
/// accident, because nothing in `src/` had ever checked it.
///
/// WHAT IT DOES NOT SAY: that a big board PLAYS well. Sparse contact over open ground is the
/// "empty traversal = boredom" risk `DESIGN.md` §3D names, and §6.5's answer is CONTESTED traversal
/// — reinforcements arriving during extraction, plus a fixed sight range that makes the space
/// genuinely unknown. Both are open roadmap items. This gate only holds the line that the fix must
/// not be "put more bodies on the bigger board".
public static partial class Mission
{
    public static string BoardNeutralSelfTest()
    {
        var fails = new List<string>();
        var detail = new System.Text.StringBuilder();
        int savedW = Cfg.GridW, savedH = Cfg.GridH, savedTile = Cfg.Tile;

        try
        {
            // The sizes under test. 18x11 is the reference; the rest are real BIGMAP shapes.
            var sizes = new[] { (18, 11), (24, 15), (36, 22) };
            // (mission, heat) cells spanning the curve: the opener X2 says ends a quarter of runs,
            // a mid-run node, and the finale at the apex where the ceiling actually binds.
            var cells = new[] { (1, 0), (3, 4), (6, 8) };

            foreach (var (m, heat) in cells)
            {
                var seated = new Dictionary<string, int>();
                var requested = new Dictionary<string, int>();
                foreach (var (w, h) in sizes)
                {
                    Cfg.SetBoard(w, h, 32);
                    Util.Reseed(8800 + m * 31 + heat);
                    int force = Game.SeatedForceForSelfTest(m, heat);
                    seated[$"{w}x{h}"] = force;
                    requested[$"{w}x{h}"] = LastForceRequest;
                }
                string row = string.Join(" ", sizes.Select(s => $"{s.Item1}x{s.Item2}={seated[$"{s.Item1}x{s.Item2}"]}"));
                detail.Append($"m{m}/h{heat}: {row}; ");

                int refSeated = seated["18x11"];
                foreach (var (w, h) in sizes)
                {
                    string k = $"{w}x{h}";
                    if (seated[k] != refSeated)
                        fails.Add($"m{m}/h{heat}: {k} seats {seated[k]} against the reference's {refSeated} — board size has become a difficulty lever");
                    if (requested[k] != requested["18x11"])
                        fails.Add($"m{m}/h{heat}: {k} REQUESTED {requested[k]} against the reference's {requested["18x11"]}");
                }
            }

            // AND THE SQUAD DOES NOT GROW EITHER — the other half of the same principle, and the
            // half that makes the first half matter. If the deploy cap scaled with the board, a
            // constant hostile force would be a difficulty DROP rather than neutrality.
            {
                var caps = new Dictionary<string, int>();
                foreach (var (w, h) in sizes)
                {
                    Cfg.SetBoard(w, h, 32);
                    caps[$"{w}x{h}"] = Run.DeployCapFor(3);
                }
                detail.Append("deployCap m3: " + string.Join(" ", caps.Select(kv => $"{kv.Key}={kv.Value}")) + "; ");
                if (caps.Values.Distinct().Count() != 1)
                    fails.Add($"the DEPLOY CAP varies with board size ({string.Join("/", caps.Values)}) — the squad must not grow with the board either");
            }
        }
        catch (Exception e) { fails.Add($"threw: {e.Message}"); }
        finally { Cfg.SetBoard(savedW, savedH, savedTile); }

        return fails.Count == 0
            ? "BOARDNEUTRALTEST: PASS (the hostile force REQUESTED and SEATED is identical at 18x11, 24x15 and 36x22 "
              + "across the opener, a mid-run node and the finale at the apex; and the deploy cap does not vary with "
              + "board size either - so a bigger board buys commitment and discovery, never difficulty) [" + detail + "]"
            : "BOARDNEUTRALTEST: FAIL (" + string.Join(" | ", fails.Distinct()) + ") [" + detail + "]";
    }
}

public partial class Game
{
    /// P56's access hook: build a real mission at (mission, heat) and return the force the BOARD
    /// SEATED. Through the real `SetupMission` path for the reason P55's leg (F) had to be — the
    /// numbers this asserts are consumed there, not where they are declared.
    public static int SeatedForceForSelfTest(int mission, int heat)
    {
        var g = new Game { NoPersist = true, ForcedObjective = Objective.Eliminate };
        g._run = new Run();
        g._run.Start();
        g._run.HeatLevel = heat;
        g.SetupMission(mission);
        return Mission.LastForceCount;
    }
}
