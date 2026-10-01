using System;
using System.Collections.Generic;
using System.Linq;

namespace Sightline;

/// SIGHTLINE_PACETEST — C1, the 10-mission run.
///
/// Every difficulty number in this game was tuned on a six-mission run (depth 1..6). A ten-mission
/// run walks the SAME curve in finer steps (`Run.Pace`), so the gates that pin the depth model by
/// mission number still run on the tuned six-mission scale (`Run.OnTunedScale`), and THIS gate is
/// the bridge between the two:
///
/// (A) `Pace` is 1,2,2,3,3,4,4,5,5,6 at ten missions and the identity at six.
/// (B) THE BRIDGE: mission m of a 10-mission run seats exactly the force that mission Pace(m) of a
///     6-mission run seats — at heat 0 for every m, and at heats 4/8 wherever neither side is in the
///     m1-2 heat grace (which is keyed on the real mission number on purpose: the first two
///     missions are the short opener whatever the run length).
/// (C) The finale is the LAST mission: mission 10 seats the six-mission finale's force; mission 6
///     of a ten-mission run is an ordinary depth-4 fight.
/// (D) A save written by a six-mission build (no RunLength) is refused, never loaded into the
///     ten-column map its MapSeed would now regenerate; a ten-mission save round-trips.
public partial class Game
{
    public static string PaceSelfTest()
    {
        var fails = new List<string>();
        var detail = new System.Text.StringBuilder();
        int mm0 = Run.MaxMissions;
        try
        {
            // ── (A) the table ─────────────────────────────────────────────────────────────────
            Run.MaxMissions = 10;
            var want10 = new[] { 1, 2, 2, 3, 3, 4, 4, 5, 5, 6 };
            var got10 = Enumerable.Range(1, 10).Select(Run.Pace).ToArray();
            if (!got10.SequenceEqual(want10)) fails.Add($"(A) Pace at 10 is {string.Join(",", got10)}");
            Run.MaxMissions = 6;
            for (int m = 1; m <= 6; m++) if (Run.Pace(m) != m) fails.Add($"(A) Pace at 6 is not the identity at m{m}");
            detail.Append($"pace {string.Join(",", got10)}; ");

            // ── (B)+(C) the bridge, measured on the force the BOARD seats ──────────────────────
            int Seat(int runLen, int mission, int heat)
            {
                Run.MaxMissions = runLen;
                Util.Reseed(9000 + heat);
                return SeatedForceForSelfTest(mission, heat);
            }
            int pairs = 0;
            foreach (int heat in new[] { 0, 4, 8 })
                for (int m = 1; m <= 10; m++)
                {
                    Run.MaxMissions = 10;
                    int p = Run.Pace(m);
                    if (heat > 0 && (m <= 2 || p <= 2)) continue;   // the m1-2 grace is real-mission keyed
                    int ten = Seat(10, m, heat), six = Seat(6, p, heat);
                    pairs++;
                    if (ten != six) fails.Add($"(B) h{heat} m{m}: a ten-mission run seats {ten}, the six-mission run's m{p} seats {six}");
                }
            detail.Append($"{pairs} bridged cells; ");
            int fin10 = Seat(10, 10, 0), fin6 = Seat(6, 6, 0), mid10 = Seat(10, 6, 0), d4 = Seat(6, 4, 0);
            if (fin10 != fin6) fails.Add($"(C) mission 10's finale seats {fin10}, the six-mission finale seats {fin6}");
            if (mid10 != d4) fails.Add($"(C) mission 6 of ten seats {mid10}, not the depth-4 fight's {d4} — it is being built as a finale");
            detail.Append($"finale {fin10}; ");

            // ── (D) the save guard ────────────────────────────────────────────────────────────
            Run.MaxMissions = 10;
            var stash = SaveGame.StashForSelfTest(SaveGame.SavePathPublic);
            try
            {
                var r = new Run(); r.Start(); r.Mission = 3;
                SaveGame.Save(r);
                if (SaveGame.Load() == null) fails.Add("(D) a ten-mission save did not round-trip");
                SaveGame.Save(r);
                var json = System.IO.File.ReadAllText(SaveGame.SavePathPublic);
                var legacy = System.Text.RegularExpressions.Regex.Replace(json, "\"RunLength\"\\s*:\\s*\\d+\\s*,?", "");
                if (legacy == json) fails.Add("(D) could not strip RunLength — the leg tested nothing");
                System.IO.File.WriteAllText(SaveGame.SavePathPublic, legacy);
                if (SaveGame.Load() != null) fails.Add("(D) a six-mission (legacy) save loaded into the ten-mission map");
            }
            finally { SaveGame.RestoreForSelfTest(stash); }
        }
        catch (Exception e) { fails.Add($"threw: {e.GetType().Name}: {e.Message}"); }
        finally { Run.MaxMissions = mm0; }

        return fails.Count == 0
            ? "PACETEST: PASS (a ten-mission run walks the tuned 1..6 depth curve as 1,2,2,3,3,4,4,5,5,6 and a six-mission "
              + "run maps to itself; every mission of the ten seats exactly the force its paced depth seats on the six, at "
              + "heat 0 everywhere and at heats 4/8 outside the m1-2 grace; the finale is mission 10 and mission 6 is an "
              + "ordinary fight; a six-mission save is refused and a ten-mission save round-trips) [" + detail + "]"
            : "PACETEST: FAIL (" + string.Join(" | ", fails.Distinct()) + ") [" + detail + "]";
    }
}
