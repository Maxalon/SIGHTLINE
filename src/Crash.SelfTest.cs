using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sightline;

/// SIGHTLINE_CRASHTEST — the gate for `Crash`.
///
/// It is not enough to assert the reporter EXISTS. Every leg below throws a REAL exception through
/// `Crash.Guard` — the same function `Program.Main` is — and then reads the file back off the disk
/// and asserts its CONTENTS, its LOCATION and the MECHANISM of its write. A crash reporter is
/// exactly the kind of code that is never exercised until the day it matters, so the only version
/// of this test worth having is one that runs it for real.
///
/// ISOLATION. CLAUDE.md's standing warning is that `SIGHTLINE_SHIPTEST` writes the LIVE
/// player-data directory and stashes/restores on the way out, so a sweep killed mid-test can
/// strand a profile. This test does not stash anything: it REDIRECTS `Crash.DirOverride` at a
/// fresh temp directory, which cannot strand what it never moved. Leg (g) proves the redirection
/// actually held, by snapshotting the real profile directory before and after and failing on any
/// new `crash-*.txt` in it.
public static class CrashTest
{
    public static string SelfTest()
    {
        var fails = new List<string>();
        string sandbox = null;
        string savedOverride = Crash.DirOverride, savedPin = Crash.NamePin;
        var savedErr = Console.Error;

        // (g) prep — what does the REAL player-data directory hold right now?
        string realDir = null; var realBefore = new List<string>();
        try
        {
            realDir = SaveGame.ConfigDir;
            if (Directory.Exists(realDir)) realBefore.AddRange(Directory.GetFiles(realDir, "crash-*.txt"));
        }
        catch { }

        try
        {
            sandbox = Path.Combine(Path.GetTempPath(), "sightline-crashtest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);

            // CAPTURE STDERR FOR THE WHOLE TEST. Two reasons, both practical:
            //   (1) the handler is SUPPOSED to be loud, and leg (d) deliberately throws an
            //       exception with a 400,000-character message. `xvfb-run` merges the child's
            //       stderr into stdout (`"$@" 2>&1`, /usr/bin/xvfb-run line 184), so an uncaptured
            //       run floods the sweep's kept log for this hook with it.
            //   (2) the stderr text is itself an assertion target — leg (a2) below reads it back.
            // Every leg re-points this at a fresh writer, so each reads only its own output.
            var loud = new StringWriter();
            Console.SetError(loud);

            // ---- (0) THE DEFAULT LOCATION ------------------------------------------------------
            // With no override the report must land in the SAME directory the save lives in. A
            // crash file that is not beside the save is a crash file nobody finds and nobody sends.
            Crash.DirOverride = null;
            if (Crash.Dir != SaveGame.ConfigDir) fails.Add("defaultDirNotPlayerData:" + Crash.Dir);
            if (string.IsNullOrEmpty(Crash.Dir) || !Path.IsPathRooted(Crash.Dir))
                fails.Add("defaultDirNotRooted:" + Crash.Dir);
            Crash.DirOverride = sandbox;

            // ---- (a) THE REAL PATH -------------------------------------------------------------
            // Guard() is what Program.Main calls. Throw through it, then read the file back.
            Crash.ResetCounter();
            Crash.NamePin = "a-realpath";
            int rc = Crash.Guard("crashtest (leg a)", () =>
                throw new InvalidOperationException("CRASHTEST synthetic failure",
                      new ArgumentOutOfRangeException("tileIndex", "CRASHTEST inner cause")));
            if (rc != 70) fails.Add("guardExitCode=" + rc);
            string pa = Crash.PathFor("a-realpath");
            if (!File.Exists(pa)) fails.Add("noReportWritten:" + pa);
            else
            {
                if (Crash.LastPath != pa) fails.Add("lastPathWrong:" + (Crash.LastPath ?? "null"));
                string t = File.ReadAllText(pa);

                // build + machine + when + where
                foreach (string need in new[] { "SIGHTLINE CRASH REPORT", "crashtest (leg a)" })
                    if (t.IndexOf(need, StringComparison.Ordinal) < 0) fails.Add("reportOmits:" + need);
                foreach (string key in new[] { "version", "whenUtc", "where", "os", "osArch", "processArch",
                                               "runtime", "baseDirectory", "playerDataDir" })
                    if (t.IndexOf(Crash.Field(key), StringComparison.Ordinal) < 0) fails.Add("reportOmitsField:" + key);
                if (t.IndexOf(Ship.Version, StringComparison.Ordinal) < 0) fails.Add("reportOmitsVersion:" + Ship.Version);

                // the UTC timestamp must PARSE and be now-ish. A report stamped in local time or in
                // 1970 misorders a bug report's timeline, which is most of what it is for.
                int ix = t.IndexOf(Crash.Field("whenUtc"), StringComparison.Ordinal);
                if (ix >= 0)
                {
                    string stamp = t.Substring(ix + Crash.Field("whenUtc").Length, 24);
                    if (!DateTime.TryParse(stamp, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AdjustToUniversal
                          | System.Globalization.DateTimeStyles.AssumeUniversal, out var when)
                        || Math.Abs((DateTime.UtcNow - when).TotalMinutes) > 10)
                        fails.Add("timestampNotUtcNow:" + stamp);
                }

                // the WHOLE exception chain, both levels, with a real stack
                if (t.IndexOf("System.InvalidOperationException", StringComparison.Ordinal) < 0)
                    fails.Add("reportOmitsOuterType");
                if (t.IndexOf("System.ArgumentOutOfRangeException", StringComparison.Ordinal) < 0)
                    fails.Add("reportOmitsInnerType");
                if (t.IndexOf("CRASHTEST inner cause", StringComparison.Ordinal) < 0)
                    fails.Add("reportOmitsInnerMessage");
                if (t.IndexOf("Sightline.Crash.Guard", StringComparison.Ordinal) < 0)
                    fails.Add("reportOmitsStackTrace");

                // game state — a report that cannot say what was happening is the C6 status quo
                if (Crash.Live == null) fails.Add("harnessDidNotPublishALiveGame");
                else
                {
                    foreach (string key in new[] { "mode", "phase", "objective", "mission", "heat",
                                                   "missionTurn", "runTurns", "soldiers", "hostiles" })
                        if (t.IndexOf(Crash.Field(key), StringComparison.Ordinal) < 0)
                            fails.Add("stateOmitsField:" + key);
                    if (t.IndexOf("<no game was running>", StringComparison.Ordinal) >= 0)
                        fails.Add("stateSaysNoGameButOneWasLive");
                    // and they must be the LIVE numbers, not a default-constructed shell
                    if (t.IndexOf(Crash.Field("soldiers") + "0/0", StringComparison.Ordinal) >= 0)
                        fails.Add("stateReadNoSoldiers");
                    if (t.IndexOf(Crash.Field("hostiles") + "0/0", StringComparison.Ordinal) >= 0)
                        fails.Add("stateReadNoHostiles");
                }

                // the harness pins in force (this process is running under SIGHTLINE_CRASHTEST)
                if (t.IndexOf("SIGHTLINE_CRASHTEST=", StringComparison.Ordinal) < 0)
                    fails.Add("reportOmitsEnv");
                // a plain managed bug must NOT carry the native-library page
                if (t.IndexOf("NATIVE LIBRARY FAILURE", StringComparison.Ordinal) >= 0)
                    fails.Add("nativeSectionOnAnOrdinaryCrash");
            }

            // ---- (a2) THE TERMINAL HEADLINE ----------------------------------------------------
            // The other half of "a player can report this": somebody who launched from a terminal
            // must be TOLD a file exists and where. A silent handler that writes a file nobody
            // knows about is only half a fix.
            {
                string e1 = loud.ToString();
                if (e1.IndexOf("SIGHTLINE CRASHED", StringComparison.Ordinal) < 0)
                    fails.Add("stderrHasNoHeadline");
                if (e1.IndexOf("CRASHTEST synthetic failure", StringComparison.Ordinal) < 0)
                    fails.Add("stderrHeadlineOmitsTheException");
                if (e1.IndexOf(pa, StringComparison.Ordinal) < 0)
                    fails.Add("stderrDidNotNameTheReportPath");
                if (e1.IndexOf("bug report", StringComparison.Ordinal) < 0)
                    fails.Add("stderrDoesNotSayWhatToDoWithIt");
            }

            // ---- (b) ATOMICITY, BY MECHANISM ---------------------------------------------------
            // C6's inode probe, same shape: plant a marker at the path the handler is about to
            // write, hold a read handle open across the write, and assert the handle STILL SEES THE
            // OLD BYTES. A rename streams into a new inode; a truncate-in-place writer would show
            // the new bytes through that same handle. That difference IS atomicity.
            // Unix-only mechanism, exactly like Ship.AtomicityProbe — it self-skips off Unix rather
            // than reporting a failure it cannot judge.
            if (!OperatingSystem.IsWindows())
            {
                Crash.ResetCounter();
                Crash.NamePin = "b-atomic";
                string pb = Crash.PathFor("b-atomic");
                const string marker = "{\"P11\":\"OLD-INODE-MARKER-PADDING-PADDING-PADDING\"}";
                File.WriteAllText(pb, marker);
                using (var fs = new FileStream(pb, FileMode.Open, FileAccess.Read,
                                               FileShare.ReadWrite | FileShare.Delete))
                {
                    Crash.Guard("crashtest (leg b)", () => throw new Exception("CRASHTEST atomicity"));
                    fs.Seek(0, SeekOrigin.Begin);
                    var buf = new byte[marker.Length];
                    int n = fs.Read(buf, 0, buf.Length);
                    if (Encoding.UTF8.GetString(buf, 0, n) != marker) fails.Add("notAtomic:handleSawNewBytes");
                }
                string after = File.Exists(pb) ? File.ReadAllText(pb) : "";
                if (after == marker || after.IndexOf("SIGHTLINE CRASH REPORT", StringComparison.Ordinal) < 0)
                    fails.Add("atomicWriteDidNotLand");
                if (File.Exists(pb + ".tmp")) fails.Add("tmpLeftBehind");
            }

            // ---- (c) THE FILE-COUNT CAP --------------------------------------------------------
            // A crash loop must not fill a disk. Write MaxReports+3 and assert the OLDEST are gone
            // and the NEWEST survive — a pruner that keeps the wrong end is worse than none.
            foreach (string f in Directory.GetFiles(sandbox, "crash-*.txt")) File.Delete(f);
            int over = Crash.MaxReports + 3;
            for (int i = 0; i < over; i++)
            {
                Crash.ResetCounter();
                Crash.NamePin = "c-" + i.ToString("00");
                Crash.Guard("crashtest (leg c)", () => throw new Exception("CRASHTEST cap"));
                // distinct mtimes, so the ordering is unambiguous on a coarse-grained filesystem
                try
                {
                    File.SetLastWriteTimeUtc(Crash.PathFor("c-" + i.ToString("00")),
                        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i));
                }
                catch { }
                Crash.Prune();
            }
            int kept = Directory.GetFiles(sandbox, "crash-*.txt").Length;
            if (kept != Crash.MaxReports) fails.Add($"capKept={kept} want={Crash.MaxReports}");
            for (int i = 0; i < over - Crash.MaxReports; i++)
                if (File.Exists(Crash.PathFor("c-" + i.ToString("00"))))
                    fails.Add("capKeptOldest:c-" + i.ToString("00"));
            for (int i = over - Crash.MaxReports; i < over; i++)
                if (!File.Exists(Crash.PathFor("c-" + i.ToString("00"))))
                    fails.Add("capDroppedNewest:c-" + i.ToString("00"));

            // ---- (d) THE PER-FILE SIZE CAP -----------------------------------------------------
            Crash.ResetCounter();
            Crash.NamePin = "d-size";
            string huge = new string('X', 400_000);
            var loudD = new StringWriter();
            Console.SetError(loudD);
            Crash.Guard("crashtest (leg d)", () => throw new Exception("CRASHTEST huge " + huge));
            Console.SetError(savedErr);
            // and the TERMINAL line must be capped too — the file holds the full text, but nobody
            // wants 400 KB of it pasted into their shell.
            if (loudD.ToString().Length > 4000)
                fails.Add("stderrHeadlineNotCapped=" + loudD.ToString().Length);
            string pd = Crash.PathFor("d-size");
            if (!File.Exists(pd)) fails.Add("sizeCapNoFile");
            else
            {
                long len = new FileInfo(pd).Length;
                if (len > Crash.MaxReportBytes) fails.Add($"sizeCapExceeded={len}");
                if (len < 1000) fails.Add($"sizeCapAteTheReport={len}");
                string t = File.ReadAllText(pd);
                if (t.IndexOf("[TRUNCATED", StringComparison.Ordinal) < 0) fails.Add("sizeCapNotAnnounced");
                if (t.IndexOf("SIGHTLINE CRASH REPORT", StringComparison.Ordinal) < 0) fails.Add("sizeCapAteTheHeader");
            }

            // ---- (e) UNWRITABLE DIRECTORY -> STDERR, CLEAN EXIT --------------------------------
            // Make the crash directory impossible: a REGULAR FILE where a directory has to be, so
            // Directory.CreateDirectory throws. The handler must not throw, must report LastPath
            // null, and must put the WHOLE report on stderr so the information still exists.
            string blocker = Path.Combine(sandbox, "blocker");
            File.WriteAllText(blocker, "not a directory");
            Crash.DirOverride = Path.Combine(blocker, "Sightline");
            Crash.ResetCounter();
            Crash.NamePin = "e-unwritable";
            var cap = new StringWriter();
            int rce;
            Console.SetError(cap);
            try { rce = Crash.Guard("crashtest (leg e)", () => throw new Exception("CRASHTEST unwritable")); }
            finally { Console.SetError(savedErr); }
            string errText = cap.ToString();
            if (rce != 70) fails.Add("unwritableExitCode=" + rce);
            if (Crash.LastPath != null) fails.Add("unwritableClaimedAPath:" + Crash.LastPath);
            if (errText.IndexOf("SIGHTLINE CRASH REPORT", StringComparison.Ordinal) < 0)
                fails.Add("unwritableDidNotFallBackToStderr");
            if (errText.IndexOf("CRASHTEST unwritable", StringComparison.Ordinal) < 0)
                fails.Add("unwritableStderrOmitsTheException");
            if (errText.IndexOf("Could not write a crash report", StringComparison.Ordinal) < 0)
                fails.Add("unwritableDidNotSaySo");
            Crash.DirOverride = sandbox;

            // ---- (f) THE HANDLER ITSELF MUST NEVER THROW ---------------------------------------
            // A null exception, and a hostile one whose own Message / StackTrace / ToString throw.
            // Either would turn one crash into two and lose the report entirely.
            try { Crash.ResetCounter(); Crash.NamePin = "f-null"; Crash.Handle(null, "crashtest (leg f)"); }
            catch (Exception e) { fails.Add("handlerThrewOnNull:" + e.GetType().Name); }
            if (!File.Exists(Crash.PathFor("f-null"))) fails.Add("nullExceptionProducedNoReport");

            try
            {
                Crash.ResetCounter();
                Crash.NamePin = "f-hostile";
                Crash.Guard("crashtest (leg f)", () => throw new HostileException());
            }
            catch (Exception e) { fails.Add("handlerThrewOnHostile:" + e.GetType().Name); }
            string pf = Crash.PathFor("f-hostile");
            if (!File.Exists(pf)) fails.Add("hostileProducedNoReport");
            else
            {
                string t = File.ReadAllText(pf);
                if (t.IndexOf("<unavailable:", StringComparison.Ordinal) < 0)
                    fails.Add("hostileReportDidNotDegradeGracefully");
                if (t.IndexOf("Sightline.CrashTest+HostileException", StringComparison.Ordinal) < 0)
                    fails.Add("hostileReportOmitsItsOwnType");
            }

            // ---- (h) THE NATIVE-LIBRARY CASE ---------------------------------------------------
            // The player-facing half of this wave. A missing libraylib.so must produce plain
            // English naming the file and where the loader looked — not a P/Invoke stack trace.
            // Wrapped in a TypeInitializationException because that is how raylib-cs's first call
            // actually surfaces it, and matching only the outermost type would miss it.
            string diag = Crash.NativeDiagnosis(new TypeInitializationException("Raylib_cs.Raylib",
                new DllNotFoundException("Unable to load shared library 'raylib' or one of its dependencies. "
                    + "In order to help diagnose loading problems, consider setting the LD_DEBUG environment "
                    + "variable: liblibraylib.so: cannot open shared object file: No such file or directory")));
            if (diag == null) fails.Add("nativeDiagnosisMissedDllNotFound");
            else
            {
                string want = Crash.NativeFileName("raylib");
                if (diag.IndexOf(want, StringComparison.Ordinal) < 0) fails.Add("nativeDiagOmitsFileName:" + want);
                if (diag.IndexOf("SAME FOLDER", StringComparison.Ordinal) < 0) fails.Add("nativeDiagOmitsTheFix");
                if (diag.IndexOf("the loader searched", StringComparison.Ordinal) < 0) fails.Add("nativeDiagOmitsSearchPath");
                if (diag.IndexOf("AppContext.BaseDirectory", StringComparison.Ordinal) < 0) fails.Add("nativeDiagOmitsBaseDir");
            }
            if (Crash.NativeDiagnosis(new EntryPointNotFoundException("Unable to find an entry point named 'InitWindow'")) == null)
                fails.Add("nativeDiagnosisMissedEntryPoint");
            if (Crash.NativeDiagnosis(new BadImageFormatException("bad image")) == null)
                fails.Add("nativeDiagnosisMissedBadImage");
            if (Crash.NativeDiagnosis(new InvalidOperationException("an ordinary bug")) != null)
                fails.Add("nativeDiagnosisFiredOnAnOrdinaryBug");
            if (Crash.NativeFileName("raylib") != (OperatingSystem.IsWindows() ? "raylib.dll"
                    : OperatingSystem.IsMacOS() ? "libraylib.dylib" : "libraylib.so"))
                fails.Add("nativeFileNameWrongForThisOs:" + Crash.NativeFileName("raylib"));
            // and it must reach the FILE, not just the API
            Crash.ResetCounter();
            Crash.NamePin = "h-native";
            Crash.Guard("crashtest (leg h)", () => throw new TypeInitializationException("Raylib_cs.Raylib",
                new DllNotFoundException("Unable to load shared library 'raylib' or one of its dependencies.")));
            string ph = Crash.PathFor("h-native");
            if (!File.Exists(ph)) fails.Add("nativeCrashProducedNoReport");
            else
            {
                string t = File.ReadAllText(ph);
                if (t.IndexOf("NATIVE LIBRARY FAILURE", StringComparison.Ordinal) < 0) fails.Add("reportOmitsNativeSection");
                if (t.IndexOf(Crash.NativeFileName("raylib"), StringComparison.Ordinal) < 0) fails.Add("reportOmitsNativeFileName");
            }

            // ---- (i) THE PER-LAUNCH WRITE CEILING ----------------------------------------------
            // Past MaxPerProcess the handler must stop writing files entirely (a game throwing every
            // frame). The ONE leg that does not call ResetCounter — that is the point of it.
            foreach (string f in Directory.GetFiles(sandbox, "crash-*.txt")) File.Delete(f);
            Crash.ResetCounter();
            for (int i = 0; i < Crash.MaxPerProcess + 2; i++)
            {
                Crash.NamePin = "i-" + i.ToString("00");
                Crash.Guard("crashtest (leg i)", () => throw new Exception("CRASHTEST ceiling"));
            }
            int made = Directory.GetFiles(sandbox, "crash-i-*.txt").Length;
            if (made > Crash.MaxPerProcess) fails.Add($"perLaunchCeilingIgnored={made}");
            if (made == 0) fails.Add("perLaunchCeilingWroteNothingAtAll");
        }
        catch (Exception e)
        {
            fails.Add("selfTestThrew:" + e.GetType().Name + ":" + OneLine(e.Message));
        }
        finally
        {
            Console.SetError(savedErr);
            Crash.DirOverride = savedOverride;
            Crash.NamePin = savedPin;
            Crash.ResetCounter();
            try { if (sandbox != null && Directory.Exists(sandbox)) Directory.Delete(sandbox, true); } catch { }
        }

        // ---- (g) NO DEBRIS IN THE REAL PROFILE -------------------------------------------------
        try
        {
            if (realDir != null && Directory.Exists(realDir))
                foreach (string p in Directory.GetFiles(realDir, "crash-*.txt"))
                    if (!realBefore.Contains(p)) fails.Add("debrisInRealProfile:" + Path.GetFileName(p));
        }
        catch { }

        return fails.Count == 0
            ? $"CRASHTEST: PASS (dir={Crash.Dir}, keep={Crash.MaxReports} files, cap={Crash.MaxReportBytes}B, "
              + $"perLaunch={Crash.MaxPerProcess}, native={Crash.NativeFileName("raylib")}, "
              + $"atomicity={(OperatingSystem.IsWindows() ? "skipped-off-unix" : "probed")})"
            : "CRASHTEST: FAIL (" + string.Join(",", fails) + ")";
    }

    static string OneLine(string s) { try { return s == null ? "-" : s.Replace('\n', ' ').Replace('\r', ' '); } catch { return "-"; } }

    /// An exception that fights the handler: reading its message, its stack or its ToString throws.
    /// Property 2 says the report degrades to `<unavailable: ...>` rather than the handler dying
    /// and taking the evidence of the ORIGINAL failure with it.
    sealed class HostileException : Exception
    {
        public override string Message => throw new NotSupportedException("hostile message");
        public override string StackTrace => throw new NotSupportedException("hostile stack");
        public override string ToString() => throw new NotSupportedException("hostile ToString");
    }
}
