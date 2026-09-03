using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Sightline;

/// SIGHTLINE_INSTRUMENTTEST — PARALLAX P15 "THE UNVERIFIED".
///
/// The gate for the four places the BALANCE INSTRUMENT produced a confident-looking answer where
/// no answer existed. Every leg here asserts the shape of a REFUSAL, not of a measurement: the
/// wave's whole thesis is that this project's claim to know anything about its own balance rests
/// on the instrument being honest about when it does not know.
///
///  (A) THE REQUEST IS PARSED, NOT GUESSED. `SIGHTLINE_BALANCE_HEAT` / `_BASE` went through a bare
///      `int.TryParse` with a silent fallback, so a typo'd chunk cycled the default heat set (or
///      reset the slot base to 0), was archived under the rung in its FILE NAME, and passed all
///      three layers of the measurement contract — `runs` correct, file fresh, exit 0.
///      `Stats.ParseBatchEnv` is now the one reader and an unparseable value is an ERROR.
///  (B) THE REQUEST IS IN THE ARTIFACT. `batch{}` echoes what was asked for, including
///      `expectedRuns` — the field a chunk runner asserts `runs` against instead of hard-coding
///      `N*2` (which marks every legitimate single-policy batch BAD).
///  (C) A RATE AND A REFUSAL DO NOT SHARE A VALUE. `runWinRate` read 0.0 — "lost every campaign" —
///      for a batch with no campaign runs, in the same object where `runWinRateExStalemate`
///      returns -1 precisely so that cannot happen.
///  (D) A MISSION THAT WAS PLAYED AND LOST IS RECORDED. `BeginMission` overwrote the open record,
///      so the checkpoint redeploy ERASED the attempt it retried and the harness's frame-cap /
///      abort exits erased the mission in flight. Driven here through the REAL redeploy call
///      (`DebugResetupMission`, which is what `Game.TryReinforcements` does).
///  (E) LAST STAND NAMES ITS OWN CENSORING. The autopilot's turn-cap force-stop routed through
///      `EndEndless` as the bare "last-stand", identical to a genuine wipe, and no endless caller
///      stamped `runTurns`.
public static class InstrumentTest
{
    public static string SelfTest()
    {
        var fails = new List<string>();
        // Each leg runs inside its own guard: a wave whose whole subject is "the machine reported a
        // confident answer where none existed" must not itself report ONE defect and stop. A leg
        // that throws is named and the rest still run.
        void Leg(string name, Action body)
        {
            try { body(); }
            catch (Exception ex) { fails.Add($"{name}:exception:{ex.GetType().Name}:{ex.Message}"); }
        }
        bool savedEnabled = Stats.Enabled;
        bool savedFlush = Stats.FlushOpenMissions;
        var savedBatch = Stats.Batch;
        try
        {
            Leg("A", () =>
            {
                // ── (A) the request is parsed, not guessed ────────────────────────────────────
                // absent is NOT an error: it is the documented "cycle the default set" / "base 0".
                var absent = Stats.ParseBatchEnv("10", null, "", "campaign", "greedy+sloppy", 2, true);
                if (absent.EnvErrors.Count != 0) fails.Add("A:absentIsError:" + string.Join("|", absent.EnvErrors));
                if (absent.Heat != null) fails.Add("A:absentHeatPinned");
                if (absent.HeatRequested != null || absent.BaseRequested != null) fails.Add("A:absentRawNotNull");
                if (absent.SlotBase != 0 || absent.N != 10) fails.Add($"A:absentDefaults={absent.SlotBase}/{absent.N}");

                var ok = Stats.ParseBatchEnv("10", "4", "80", "campaign", "greedy+sloppy", 2, true);
                if (ok.EnvErrors.Count != 0) fails.Add("A:okIsError");
                if (ok.Heat != 4 || ok.SlotBase != 80) fails.Add($"A:okParse={ok.Heat}/{ok.SlotBase}");
                if (ok.HeatRequested != "4" || ok.BaseRequested != "80") fails.Add("A:okRawNotEchoed");
                if (ok.ExpectedRuns != 20) fails.Add($"A:expectedRuns={ok.ExpectedRuns}");

                // ...and the typos. Each of these silently produced a MEASURED, ARCHIVED, WRONGLY-NAMED
                // chunk before this wave. `-h4` fat-fingered as `-h$` / `-hO`; a base pasted with its
                // flag; an N that picked up a stray character.
                foreach (var (bal, heat, bas, who) in new[]
                         {
                             ("10", "$", "80", "heat-punct"),
                             ("10", "O", "80", "heat-letter-O"),
                             ("10", "4.0", "80", "heat-float"),
                             ("10", "h4", "80", "heat-prefixed"),
                             ("10", "4", "b80", "base-prefixed"),
                             ("10", "4", "eighty", "base-word"),
                             ("ten", "4", "80", "n-word"),
                         })
                {
                    var bad = Stats.ParseBatchEnv(bal, heat, bas, "campaign", "greedy+sloppy", 2, true);
                    if (bad.EnvErrors.Count == 0) fails.Add("A:silentFallback:" + who);
                    // ...and it must not have quietly kept a usable-looking value either.
                    if (who.StartsWith("heat") && bad.Heat != null) fails.Add("A:badHeatStillPinned:" + who);
                    if (who.StartsWith("base") && bad.SlotBase != 0) fails.Add("A:badBaseStillSet:" + who);
                }
                // the shape half: a single-policy batch expects N runs, not N*2.
                if (Stats.ParseBatchEnv("10", "4", "0", "campaign", "sloppy", 1, true).ExpectedRuns != 10)
                    fails.Add("A:singlePolicyExpectedRuns");
                if (Stats.ParseBatchEnv("10", "4", "0", "endless", "dumb", 1, true).ExpectedRuns != 10)
                    fails.Add("A:dumbExpectedRuns");
            });

            Leg("B", () =>
            {
                // ── (B) the request lands in the artifact ─────────────────────────────────────
                Stats.Reset(); Stats.Enabled = true;
                Stats.Batch = Stats.ParseBatchEnv("10", "4", "80", "campaign", "sloppy", 1, true);
                Stats.BeginRun(4, "sloppy");
                Stats.BeginMission(1, "Eliminate", 4, 4, 5);
                Stats.EndMission(true, 5, 4, 5, "");
                Stats.EndRun(true, 1, "", 4, 9);
                using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(Stats.BuildSummary())))
                {
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("batch", out var b) || b.ValueKind != JsonValueKind.Object)
                        fails.Add("B:noBatchBlock");
                    else
                    {
                        if (b.GetProperty("heatRequested").GetString() != "4") fails.Add("B:heatRequested");
                        if (b.GetProperty("heat").GetInt32() != 4) fails.Add("B:heat");
                        if (b.GetProperty("baseRequested").GetString() != "80") fails.Add("B:baseRequested");
                        if (b.GetProperty("slotBase").GetInt32() != 80) fails.Add("B:slotBase");
                        if (b.GetProperty("expectedRuns").GetInt32() != 10) fails.Add("B:expectedRuns");
                        if (b.GetProperty("policies").GetString() != "sloppy") fails.Add("B:policies");
                        if (!b.GetProperty("heatPinned").GetBoolean()) fails.Add("B:heatPinned");
                        if (b.GetProperty("envErrors").GetArrayLength() != 0) fails.Add("B:envErrors");
                    }
                }
                // A CYCLED batch must say so with a null heat, not with a number that reads as a pin.
                Stats.Batch = Stats.ParseBatchEnv("10", null, null, "campaign", "greedy+sloppy", 2, true);
                using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(Stats.BuildSummary())))
                {
                    var b = doc.RootElement.GetProperty("batch");
                    if (b.GetProperty("heat").ValueKind != JsonValueKind.Null) fails.Add("B:cycledHeatNotNull");
                    if (b.GetProperty("heatRequested").ValueKind != JsonValueKind.Null) fails.Add("B:cycledRawNotNull");
                    if (b.GetProperty("expectedRuns").GetInt32() != 20) fails.Add("B:cycledExpectedRuns");
                }
                // ...and outside a batch there is no block at all, so an old archive is distinguishable.
                Stats.Batch = null;
                using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(Stats.BuildSummary())))
                    if (doc.RootElement.GetProperty("batch").ValueKind != JsonValueKind.Null) fails.Add("B:batchNotNullOutsideBatch");
            });

            Leg("C", () =>
            {
                // ── (C) a rate and a refusal do not share a value ─────────────────────────────
                // An ENDLESS batch has runs > 0 and campaign runs == 0, so WriteJson does NOT refuse and
                // the file is written with a runWinRate in it. It used to read 0.0.
                Stats.Reset(); Stats.Enabled = true;
                Stats.BeginRun(4, "greedy", "endless");
                Stats.BeginMission(1, "Endless", 4, 4, 6);
                Stats.EndMission(false, 12, 0, 9, "last-stand");
                Stats.EndRun(false, 7, "last-stand", 4, 31);
                using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(Stats.BuildSummary())))
                {
                    var root = doc.RootElement;
                    if (root.GetProperty("runs").GetInt32() != 1) fails.Add("C:runs");
                    double wr = root.GetProperty("runWinRate").GetDouble();
                    if (wr != -1.0) fails.Add($"C:runWinRate={wr} (0.0 reads as 'lost every campaign')");
                    if (root.GetProperty("runWinRateExStalemate").GetDouble() != -1.0) fails.Add("C:exStalemate");
                }
                // ...and it is still a real rate when there ARE campaigns.
                Stats.BeginRun(4, "greedy");
                Stats.BeginMission(1, "Eliminate", 4, 4, 5);
                Stats.EndMission(true, 5, 4, 5, "");
                Stats.EndRun(true, 1, "", 4, 5);
                using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(Stats.BuildSummary())))
                    if (doc.RootElement.GetProperty("runWinRate").GetDouble() != 100.0) fails.Add("C:realRate");
            });

            Leg("D", () =>
            {
                // ── (D) the erased mission, through the REAL redeploy ─────────────────────────
                // DebugResetupMission IS what Game.TryReinforcements calls: SetupMission on the SAME
                // mission number, which re-enters Stats.BeginMission with the wiped attempt still open.
                Stats.Reset(); Stats.Enabled = true; Stats.FlushOpenMissions = true;
                {
                    Util.Reseed(90101);
                    Stats.Slot = 3;
                    var g = new Game { NoPersist = true, AutoPlay = true };
                    g.StartMission(3);                       // the checkpoint valve opens at mission 3
                    g.DebugResetupMission();                 // == the redeploy: same mission, staged again
                    g.DebugResetupMission();                 // a second one must be recorded too
                    Stats.EndMission(true, 6, 3, 5, "");     // ...and then the retry is cleared
                    Stats.EndRun(true, 3, "", 0, 20);
                    Stats.Slot = -1;
                    var run = Stats.Runs.Count > 0 ? Stats.Runs[0] : null;
                    if (run == null) fails.Add("D:noRun");
                    else
                    {
                        if (run.Missions.Count != 3)
                            fails.Add($"D:missions={run.Missions.Count} (the redeploy ERASED the attempt it retried)");
                        else
                        {
                            var erased = run.Missions.Take(2).ToList();
                            if (erased.Any(m => m.Win)) fails.Add("D:erasedRecordedAsWin");
                            if (erased.Any(m => m.LossCause != Stats.MissionRedeployed))
                                fails.Add("D:erasedCause=" + string.Join("/", erased.Select(m => m.LossCause)));
                            if (erased.Any(m => m.Mission != 3)) fails.Add("D:erasedMissionNo");
                            if (!run.Missions[2].Win) fails.Add("D:retryNotWon");
                        }
                    }
                    if (Stats.MissionsDroppedNoRun != 0) fails.Add($"D:droppedNoRun={Stats.MissionsDroppedNoRun}");
                    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(Stats.BuildSummary()));
                    var ih = doc.RootElement.GetProperty("instrumentHealth");
                    if (ih.GetProperty("missionsRedeployed").GetInt32() != 2) fails.Add("D:healthRedeployed");
                    if (ih.GetProperty("missionsDroppedNoRun").GetInt32() != 0) fails.Add("D:healthDropped");
                    // the whole point: a NON-TERMINAL mission loss now exists in the artifact.
                    if (doc.RootElement.GetProperty("missions").GetInt32() != 3) fails.Add("D:missionCount");
                }
                // ...the harness's own mid-mission exit (frame-cap / abort: EndRun with no EndMission)
                // files the mission in flight on the run that played it, not on the next one.
                Stats.Reset(); Stats.Enabled = true;
                Stats.BeginRun(0, "greedy");
                Stats.BeginMission(2, "Hack", 0, 4, 6);
                Stats.EndRun(false, 1, "frame-cap", 0, 44);          // the batch's defensive close
                Stats.BeginRun(0, "sloppy");
                Stats.BeginMission(1, "Eliminate", 0, 4, 5);
                Stats.EndMission(true, 4, 4, 5, "");
                Stats.EndRun(true, 1, "", 0, 9);
                if (Stats.Runs.Count != 2) fails.Add("D:framecapRuns");
                else
                {
                    if (Stats.Runs[0].Missions.Count != 1 || Stats.Runs[0].Missions[0].LossCause != Stats.MissionUnclosed)
                        fails.Add("D:framecapMissionNotFiled");
                    if (Stats.Runs[1].Missions.Count != 1)
                        fails.Add($"D:framecapLeakedIntoNextRun={Stats.Runs[1].Missions.Count}");
                }
                if (Stats.MissionsDroppedNoRun != 0) fails.Add("D:framecapDroppedNoRun");

                // ...and the A/B dial really does restore the pre-P15 drop (so a re-measure can price it).
                Stats.Reset(); Stats.Enabled = true; Stats.FlushOpenMissions = false;
                Stats.BeginRun(0, "greedy");
                Stats.BeginMission(3, "Evac", 0, 4, 6);
                Stats.BeginMission(3, "Evac", 0, 3, 6);
                Stats.EndMission(true, 5, 3, 6, "");
                Stats.EndRun(true, 3, "", 0, 12);
                if (Stats.Runs[0].Missions.Count != 1) fails.Add("D:dialDidNotRestoreDrop");
                Stats.FlushOpenMissions = true;
            });

            Leg("E", () =>
            {
                // ── (E) LAST STAND names its own censoring ────────────────────────────────────
                // The autopilot's turn cap on an endless stand must log the SAME arm the campaign does,
                // must satisfy IsStalemate (the predicate every consumer filters on), and must stamp
                // runTurns. A genuine wipe must still read "last-stand" and must NOT be a stalemate.
                Stats.Reset(); Stats.Enabled = true;
                {
                    Util.Reseed(90102);
                    Stats.Slot = 5;
                    var g = new Game { NoPersist = true, AutoPlay = true };
                    g.BeginEndless();
                    g.DebugSetRunTurns(Game.AutoMaxRunTurns);
                    g.DebugSetTurn(1);
                    g.DebugStartPlayerTurn();                // RunTurns -> AutoMaxRunTurns + 1
                    Stats.Slot = -1;
                    if (g.Phase != Phase.Lose) fails.Add("E:standDidNotEnd");
                    if (Stats.Runs.Count != 1) fails.Add($"E:runs={Stats.Runs.Count}");
                    else
                    {
                        var r = Stats.Runs[0];
                        if (r.Mode != "endless") fails.Add("E:mode=" + r.Mode);
                        if (r.LossCause != Stats.StalemateRun)
                            fails.Add($"E:cause={r.LossCause} (a harness stop indistinguishable from a wipe)");
                        if (!Stats.IsStalemate(r.LossCause)) fails.Add("E:notIsStalemate");
                        if (r.RunTurns != Game.AutoMaxRunTurns + 1)
                            fails.Add($"E:runTurns={r.RunTurns} (no endless EndRun caller stamped it)");
                    }
                }
                // the genuine wipe keeps its own name, and the JSON separates the two.
                Stats.BeginRun(0, "greedy", "endless");
                Stats.BeginMission(1, "Endless", 0, 4, 6);
                Stats.EndMission(false, 9, 0, 8, "last-stand");
                Stats.EndRun(false, 6, "last-stand", 0, 22);
                using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(Stats.BuildSummary())))
                {
                    var e = doc.RootElement.GetProperty("endless");
                    if (e.GetProperty("runs").GetInt32() != 2) fails.Add("E:endlessRuns");
                    if (e.GetProperty("stalemateHits").GetInt32() != 1) fails.Add("E:stalemateHits");
                    if (e.GetProperty("wipes").GetInt32() != 1) fails.Add("E:wipes");
                    if (e.GetProperty("depthUncensored").GetProperty("n").GetInt32() != 1) fails.Add("E:depthUncensored");
                }
            });

            Leg("F", () =>
            {
                // ── (F) the force-loss leaves ONE closed run, and no phantom ──────────────────
                // P15 finding 7: `AutoStallCheck` force-loses the run from the MIDDLE of
                // StartPlayerTurn, which then runs to completion on a run already in Phase.Lose
                // (traced: "StartPlayerTurn CONTINUES with Phase=Lose ... and RAN TO THE END").
                // The honest fix is an early return in StartPlayerTurn, which lives in Game.cs and
                // is not this wave's file — it is handed back as a patch. What IS asserted here is
                // the invariant that makes the tail merely wasteful instead of corrupting: the
                // force-loss must leave EXACTLY ONE RunRec, closed, and must never open a second.
                // (A phantom run is one `CheckEnd` away: CheckEnd -> TryReinforcements ->
                // SetupMission -> Stats.BeginMission, and BeginMission opens a run when `_run` is
                // null. `missionsDroppedNoRun` is the alarm for the near miss.)
                Stats.Reset(); Stats.Enabled = true;
                Util.Reseed(90103);
                Stats.Slot = 11;
                var g = new Game { NoPersist = true, AutoPlay = true };
                g.StartMission(1);
                g.DebugSetRunTurns(Game.AutoMaxRunTurns);
                g.DebugSetTurn(1);
                g.DebugStartPlayerTurn();
                Stats.Slot = -1;
                if (g.Phase != Phase.Lose) fails.Add("F:didNotForceLose");
                if (Stats.Runs.Count != 1) fails.Add($"F:phantomRuns={Stats.Runs.Count}");
                else if (Stats.Runs[0].LossCause != Stats.StalemateRun) fails.Add("F:cause=" + Stats.Runs[0].LossCause);
                if (Stats.MissionsDroppedNoRun != 0) fails.Add($"F:droppedNoRun={Stats.MissionsDroppedNoRun}");
                // the mission the run was on is CLOSED, not left open for the next BeginRun to inherit
                if (Stats.Runs.Count == 1 && Stats.Runs[0].Missions.Count != 1)
                    fails.Add($"F:missions={Stats.Runs[0].Missions.Count}");
            });

        }
        catch (Exception e) { fails.Add("exception:" + e.GetType().Name + ":" + e.Message); }
        finally
        {
            Stats.Reset();
            Stats.Enabled = savedEnabled;
            Stats.FlushOpenMissions = savedFlush;
            Stats.Batch = savedBatch;
            Stats.Slot = -1;
            Util.Reseed(0);
        }
        return fails.Count == 0
            ? "INSTRUMENTTEST: PASS (batch request parsed strictly + echoed as batch{} with expectedRuns; "
              + "runWinRate = -1 on no campaign data; the redeployed / unclosed mission is FILED, not erased; "
              + "the LAST STAND harness stop is named and stamps runTurns; the force-loss opens no phantom run)"
            : "INSTRUMENTTEST: FAIL " + string.Join(",", fails);
    }
}
