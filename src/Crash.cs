using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Sightline;

/// PROGRAM PARALLAX wave P11 "THE CRASH FILE" — the thing a player can send you.
///
/// C6 "SHIPS LIKE A PRODUCT" listed this as the biggest gap it deliberately left open: an
/// unhandled exception on somebody else's machine "goes to a stdout nobody reads. The version
/// stamp lets them name a build; there is nothing to attach." A double-clicked binary has no
/// terminal. The window vanishes, and the only report you will ever get is "it crashed".
///
/// So: one top-level handler around the whole launch (`Crash.Guard`, which is all `Program.Main`
/// is), one file per crash in the SAME directory the player's save already lives in — found
/// through `SaveGame.ConfigDir`, never a second derivation of that path — and a headline on
/// stderr for the person who *did* launch from a terminal.
///
/// FOUR PROPERTIES THIS FILE IS RESPONSIBLE FOR, each asserted by SIGHTLINE_CRASHTEST:
///
///  1. **It writes through `SaveGame.WriteAtomic`.** C6 made `display.json`'s non-atomic write a
///     finding; a crash reporter that truncates a file in place would be a new one. Same writer,
///     same inode-swap property, proven the same way (an open handle held across the write).
///  2. **The handler cannot throw.** Every section of the report is composed inside its own
///     try/catch and degrades to an `<unavailable: reason>` line, because the process is already
///     half-dead and a second exception inside the handler destroys the evidence of the first. An
///     exception whose own Message and ToString THROW is a CRASHTEST leg.
///  3. **A crash loop cannot fill a disk.** At most `MaxReports` files are kept (oldest pruned),
///     each capped at `MaxReportBytes`, and at most `MaxPerProcess` are written per launch.
///  4. **An unwritable directory degrades to stderr and a clean exit** — never to a second crash.
///
/// AND ONE THING IT IS EXPLICITLY *NOT*: this cannot catch what is not a managed exception. A
/// raylib ABI mismatch that faults inside native code (SIGSEGV) kills the process without
/// unwinding, and no managed handler anywhere runs — saying otherwise would be the over-claim this
/// project bans. What IS catchable is the load-time family: a missing `libraylib.so`
/// (`DllNotFoundException`), a wrong-architecture one (`BadImageFormatException`) and a
/// wrong-version one whose entry point is absent (`EntryPointNotFoundException`). Those are turned
/// into plain English by `NativeDiagnosis` instead of a P/Invoke stack trace. The line between the
/// two halves is stated in docs/DISTRIBUTION.md §6 rather than papered over.
public static class Crash
{
    // ── caps (property 3) ────────────────────────────────────────────────────────────────────
    /// Newest N crash files are kept; older ones are pruned on every write. Five is enough to show
    /// a pattern ("it always dies on mission 3") and small enough that a boot loop writing one per
    /// second still occupies bounded space.
    public const int MaxReports = 5;
    /// Hard ceiling on ONE report. A runaway recursion's stack, or a generated exception message,
    /// can be arbitrarily large; 64 KB holds a very deep chain and cannot surprise anyone.
    public const int MaxReportBytes = 64 * 1024;
    /// Ceiling on how many reports ONE LAUNCH may write. A game throwing every frame would
    /// otherwise write a file per frame; past this the handler says so on stderr and stops
    /// touching the disk.
    public const int MaxPerProcess = 3;

    // ── harness seams (never set in normal play) ─────────────────────────────────────────────
    /// Redirect the crash directory. SIGHTLINE_CRASHTEST points this at a fresh temp dir so the
    /// test never writes the player's real profile directory. CLAUDE.md's standing warning is
    /// about SIGHTLINE_SHIPTEST, the one hook that writes the LIVE player-data directory and
    /// stashes/restores on the way out; REDIRECTION is strictly safer than that, because a
    /// redirected test killed halfway leaves nothing to fail to put back.
    public static string DirOverride;
    /// Pin the report's file name (basename, no extension). The open-handle atomicity probe has to
    /// plant a marker at the path the handler is ABOUT to write, and a real report's name carries
    /// a timestamp it cannot predict.
    public static string NamePin;
    /// Harness-only: forget how many reports this process has written, so a multi-leg self-test is
    /// not silenced by its own `MaxPerProcess` ceiling. Leg (i) is the one leg that does NOT call
    /// this, which is how the ceiling itself gets tested.
    public static void ResetCounter() { _written = 0; }

    /// The live game, published by `Program.Main` once it exists, so the report can say what was
    /// happening. Read defensively and never trusted: at crash time this object is by definition
    /// in a state nobody designed.
    public static Game Live;

    /// Path of the most recent report, or null when the write fell back to stderr.
    public static string LastPath;
    /// The exact text of the most recent report (also what goes to stderr on fallback).
    public static string LastText;

    static bool _installed;
    static int _written;

    /// The player-data directory the report lands in — the SAME one `SaveGame` / `Meta` /
    /// `Display` use, reached through their accessor so the empty-`ApplicationData` fallback (see
    /// `SaveGame.Dir`) applies here too. A second derivation of this path is exactly how you get a
    /// crash file the player cannot find.
    public static string Dir => !string.IsNullOrEmpty(DirOverride) ? DirOverride : SaveGame.ConfigDir;

    /// `crash-<stamp>.txt` inside `Dir`. The stamp sorts lexically in time order, which is what
    /// the pruner leans on to break same-second mtime ties.
    public static string PathFor(string stamp) => Path.Combine(Dir, "crash-" + stamp + ".txt");

    /// The exact left-hand column of a report field, so the self-test can assert a field is
    /// present WITHOUT anyone counting alignment spaces by eye. (An earlier draft hard-coded the
    /// padded strings in both files; they drift the first time a field name gets longer.)
    public static string Field(string key) => "  " + key.PadRight(14) + " : ";

    // ── installation ─────────────────────────────────────────────────────────────────────────

    /// Catch what escapes the main thread too. `Guard` covers everything `Main` does; this covers
    /// a throw on a background thread or an unobserved task, which would otherwise take the
    /// process down with no report at all. Idempotent.
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Handle(e.ExceptionObject as Exception, "background thread (unhandled)");
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
                Handle(e.Exception, "task (unobserved)");
        }
        catch { /* best effort: a game that cannot subscribe still runs */ }
    }

    /// THE TOP-LEVEL HANDLER. `Program.Main` is nothing but a call to this.
    ///
    /// Returns the process exit code: 0 on a clean return, 70 (`EX_SOFTWARE`) when the body threw.
    /// SIGHTLINE_CRASHTEST calls THIS function with a throwing body, so what it exercises is the
    /// path a player's crash actually takes rather than a re-implementation of it.
    public static int Guard(string where, Action body)
    {
        try { body(); return 0; }
        catch (Exception ex) { Handle(ex, where); return 70; }
    }

    // ── the handler (property 2: this must never throw) ──────────────────────────────────────

    public static void Handle(Exception ex, string where)
    {
        try
        {
            _written++;
            if (_written > MaxPerProcess)
            {
                try
                {
                    Console.Error.WriteLine($"SIGHTLINE: {_written} crashes this launch — no further "
                        + $"reports written (cap {MaxPerProcess} per launch). Last: "
                        + (ex == null ? "<null exception>" : ex.GetType().Name));
                }
                catch { }
                return;
            }

            string text = Compose(ex, where);
            LastText = text;
            LastPath = null;

            string stamp = !string.IsNullOrEmpty(NamePin)
                ? NamePin
                : DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + SafePid();
            string path = PathFor(stamp);
            string writeError = null;
            try
            {
                SaveGame.WriteAtomic(path, text);   // .tmp + rename — C6's one writer
                LastPath = path;
            }
            catch (Exception we) { writeError = we.GetType().Name + ": " + we.Message; }

            // The headline. Someone who launched from a terminal sees this; someone who
            // double-clicked still gets the file. The native-library case LEADS with plain
            // English, because a DllNotFoundException stack trace tells a player nothing at all.
            try
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("=== SIGHTLINE CRASHED =======================================");
                string native = NativeDiagnosis(ex);
                if (native != null) Console.Error.WriteLine(native);
                else Console.Error.WriteLine("  " + Headline(ex));
                if (LastPath != null)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("  A crash report was written to:");
                    Console.Error.WriteLine("    " + LastPath);
                    Console.Error.WriteLine("  Please send that file with your bug report — it names the build, the");
                    Console.Error.WriteLine("  machine and what the game was doing. It contains no personal data.");
                }
                else
                {
                    // Property 4: the report still has to exist SOMEWHERE a player can copy from.
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("  Could not write a crash report to " + SafeDir()
                        + (writeError != null ? "  (" + writeError + ")" : ""));
                    Console.Error.WriteLine("  The full report follows — copy it into your bug report.");
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(text);
                }
                Console.Error.WriteLine("=============================================================");
                Console.Error.Flush();
            }
            catch { /* a closed stderr must not turn one crash into two */ }

            Prune();
        }
        catch
        {
            // The absolute backstop. If composing, writing and reporting have all failed, say the
            // one thing that cannot fail and get out. Property 2 is not negotiable: the handler may
            // lose the report, but it may never replace the original exception with its own.
            try { Console.Error.WriteLine("SIGHTLINE: crashed, and the crash handler failed too."); } catch { }
        }
    }

    // ── composition ──────────────────────────────────────────────────────────────────────────

    /// Build the report. EVERY section is individually guarded: at crash time any property on any
    /// object may throw, and a report missing its game-state block is worth infinitely more than
    /// no report at all.
    public static string Compose(Exception ex, string where)
    {
        var sb = new StringBuilder();

        void Kv(string k, Func<string> v) { sb.Append(Field(k)).Append(Safe(v)).Append('\n'); }
        void Section(string title, Action body)
        {
            try
            {
                sb.Append('\n').Append("--- ").Append(title).Append(' ')
                  .Append('-', Math.Max(3, 58 - title.Length)).Append('\n');
                body();
            }
            catch (Exception e)
            {
                try { sb.Append("  <unavailable: ").Append(e.GetType().Name).Append(">\n"); } catch { }
            }
        }

        try
        {
            sb.Append("SIGHTLINE CRASH REPORT\n");
            sb.Append("======================\n");
            sb.Append("Send this whole file with your bug report. It contains no personal data: no\n");
            sb.Append("account details, and no file paths outside the game's own directories.\n");
        }
        catch { }

        Section("BUILD AND MACHINE", () =>
        {
            Kv("version", () => Ship.VersionLabel);
            Kv("whenUtc", () => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"));
            Kv("where", () => where ?? "-");
            Kv("os", () => RuntimeInformation.OSDescription.Trim());
            Kv("osArch", () => RuntimeInformation.OSArchitecture.ToString());
            Kv("processArch", () => RuntimeInformation.ProcessArchitecture.ToString());
            Kv("runtime", () => RuntimeInformation.FrameworkDescription);
            Kv("rid", () => RuntimeInformation.RuntimeIdentifier);
            Kv("baseDirectory", () => AppContext.BaseDirectory);
            Kv("workingDir", () => Directory.GetCurrentDirectory());
            Kv("playerDataDir", () => Dir);
            Kv("uptimeSec", () => (Environment.TickCount64 / 1000).ToString());
        });

        // Only when it applies. A gameplay bug should not carry a page about shared libraries, and
        // a library failure should not be buried under a stack trace.
        string native = NativeDiagnosis(ex);
        if (native != null) Section("NATIVE LIBRARY FAILURE", () => sb.Append(native).Append('\n'));

        Section("GAME STATE", () =>
        {
            var g = Live;
            if (g == null) { sb.Append("  <no game was running>\n"); return; }
            Kv("mode", () => g.Mode.ToString());
            Kv("phase", () => g.Phase.ToString());
            Kv("objective", () => g.Objective.ToString());
            Kv("mission", () => g.RunState == null ? "-" : g.RunState.Mission.ToString());
            Kv("heat", () => g.RunState == null ? "-" : g.RunState.HeatLevel.ToString());
            Kv("mapSeed", () => g.RunState == null ? "-" : g.RunState.MapSeed.ToString());
            Kv("mapPos", () => g.RunState == null ? "-" : g.RunState.MapPos.ToString());
            Kv("missionTurn", () => g.Turn.ToString());
            Kv("runTurns", () => g.RunTurns.ToString());
            Kv("wave", () => g.Wave.ToString());
            Kv("soldiers", () => Alive(g.Players) + "/" + Count(g.Players) + " alive");
            Kv("hostiles", () => Alive(g.Enemies) + "/" + Count(g.Enemies) + " alive");
            Kv("selected", () => g.Selected == null ? "-" : g.Selected.Name + " (" + g.Selected.Cls + ")");
            Kv("activeAnim", () => g.ActiveAnim == null ? "-" : g.ActiveAnim.GetType().Name);
            Kv("noPersist", () => g.NoPersist.ToString());
        });

        Section("SIGHTLINE_* ENVIRONMENT", () =>
        {
            // These change what the game DOES, so a report without them can send you hunting a bug
            // that only exists under a harness pin. Only this project's own namespace is dumped.
            var names = new List<string>();
            foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
                if (kv.Key is string k && k.StartsWith("SIGHTLINE_", StringComparison.Ordinal))
                    names.Add(k + "=" + kv.Value);
            names.Sort(StringComparer.Ordinal);
            if (names.Count == 0) sb.Append("  (none set — a normal launch)\n");
            else foreach (string n in names) sb.Append("  ").Append(n).Append('\n');
        });

        Section("EXCEPTION CHAIN", () =>
        {
            if (ex == null) { sb.Append("  <null exception>\n"); return; }
            int depth = 0;
            for (Exception e = ex; e != null && depth < 12; e = SafeInner(e), depth++)
            {
                sb.Append('[').Append(depth + 1).Append("] ").Append(Safe(() => e.GetType().FullName))
                  .Append(": ").Append(Safe(() => e.Message)).Append('\n');
                string src = Safe(() => e.Source);
                if (!string.IsNullOrEmpty(src) && src != "-" && !src.StartsWith("<unavailable"))
                    sb.Append("     source: ").Append(src).Append('\n');
                string st = Safe(() => e.StackTrace);
                if (!string.IsNullOrEmpty(st) && st != "-") sb.Append(st).Append('\n');
                // An AggregateException hides its real content in InnerExceptionS; walking only
                // InnerException reports one of N failures and hides the rest.
                if (e is AggregateException agg)
                {
                    try
                    {
                        for (int i = 0; i < agg.InnerExceptions.Count && i < 8; i++)
                            sb.Append("     aggregate[").Append(i).Append("] ")
                              .Append(agg.InnerExceptions[i].GetType().Name).Append(": ")
                              .Append(agg.InnerExceptions[i].Message).Append('\n');
                    }
                    catch { }
                }
            }
            if (depth >= 12) sb.Append("  ... chain deeper than 12; truncated\n");
        });

        try { sb.Append("\n--- END OF REPORT ---\n"); } catch { }

        return Cap(sb.ToString());
    }

    /// Property 3, the per-file half. Truncate on a UTF-8 character boundary and SAY SO — a
    /// silently cut report reads as a corrupt one and sends the reader after the wrong bug.
    static string Cap(string text)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length <= MaxReportBytes) return text;
            string marker = $"\n\n[TRUNCATED: this report exceeded the {MaxReportBytes}-byte cap "
                          + $"(it was {bytes.Length} bytes). Everything above the cut is complete.]\n";
            int budget = MaxReportBytes - Encoding.UTF8.GetByteCount(marker);
            if (budget < 0) budget = 0;
            while (budget > 0 && (bytes[budget] & 0xC0) == 0x80) budget--;   // back off to a boundary
            return Encoding.UTF8.GetString(bytes, 0, budget) + marker;
        }
        catch { return text; }
    }

    /// Property 3, the file-count half. Keep the newest `MaxReports`, delete the rest. Ordered by
    /// last-write time with the NAME as tie-break, because several reports inside one second (a
    /// boot loop is exactly that) can share an mtime while the stamp still orders them.
    public static void Prune()
    {
        try
        {
            string dir = Dir;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            var files = new List<FileInfo>();
            foreach (string p in Directory.GetFiles(dir, "crash-*.txt")) files.Add(new FileInfo(p));
            if (files.Count <= MaxReports) return;
            files.Sort((a, b) =>
            {
                int c = a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc);
                return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
            });
            for (int i = 0; i < files.Count - MaxReports; i++)
                try { files[i].Delete(); } catch { }
        }
        catch { }
    }

    // ── the native-library case ──────────────────────────────────────────────────────────────

    /// The file name the loader wants beside the executable for a given P/Invoke library name.
    public static string NativeFileName(string lib)
    {
        if (string.IsNullOrEmpty(lib)) lib = "raylib";
        if (OperatingSystem.IsWindows()) return lib + ".dll";
        if (OperatingSystem.IsMacOS()) return "lib" + lib + ".dylib";
        return "lib" + lib + ".so";
    }

    /// Turn a load-time P/Invoke failure into something a player can act on, or return null when
    /// this is an ordinary managed bug.
    ///
    /// Walks the WHOLE chain: raylib-cs's first call can surface as a TypeInitializationException
    /// wrapping the real DllNotFoundException, and matching only the outermost type would miss it.
    public static string NativeDiagnosis(Exception ex)
    {
        try
        {
            for (Exception e = ex; e != null; e = SafeInner(e))
            {
                if (e is DllNotFoundException) return Missing(Safe(() => e.Message));
                if (e is BadImageFormatException) return WrongArch(Safe(() => e.Message));
                if (e is EntryPointNotFoundException) return WrongVersion(Safe(() => e.Message));
            }
        }
        catch { }
        return null;
    }

    /// Pull the library name out of the runtime's message, which reads
    /// `Unable to load shared library 'raylib' or one of its dependencies. ...`
    static string LibNameFrom(string msg)
    {
        try
        {
            int a = msg.IndexOf('\'');
            int b = a < 0 ? -1 : msg.IndexOf('\'', a + 1);
            if (a >= 0 && b > a + 1)
            {
                string s = msg.Substring(a + 1, b - a - 1);
                if (s.StartsWith("lib", StringComparison.Ordinal)) s = s.Substring(3);
                int dot = s.IndexOf('.');
                if (dot > 0) s = s.Substring(0, dot);
                if (s.Length > 0) return s;
            }
        }
        catch { }
        return "raylib";
    }

    static string Missing(string msg)
    {
        string lib = LibNameFrom(msg);
        string file = NativeFileName(lib);
        var s = new StringBuilder();
        s.Append("  SIGHTLINE COULD NOT LOAD ITS GRAPHICS LIBRARY, so it cannot start.\n\n");
        s.Append("  The game needs the file  ").Append(file).Append("  to sit in the SAME FOLDER as\n");
        s.Append("  the game program. It is part of the download. If you moved or copied only the\n");
        s.Append("  program out of the folder it arrived in, move it back — or keep the whole folder\n");
        s.Append("  together and run it from there. Nothing else needs installing.\n\n");
        s.Append("  library the runtime asked for : ").Append(lib).Append('\n');
        s.Append("  file it looked for            : ").Append(file).Append('\n');
        s.Append("  beside the program            : ").Append(BesideExe(file)).Append('\n');
        s.Append("  the loader searched           :\n");
        foreach (string d in SearchPath()) s.Append("      ").Append(d).Append('\n');
        s.Append("  runtime message               : ").Append(OneLine(msg)).Append('\n');
        return s.ToString();
    }

    static string WrongArch(string msg)
    {
        string file = NativeFileName("raylib");
        var s = new StringBuilder();
        s.Append("  SIGHTLINE FOUND ITS GRAPHICS LIBRARY BUT CANNOT USE IT.\n\n");
        s.Append("  The file  ").Append(file).Append("  is there, but it was built for a different kind of\n");
        s.Append("  processor than this build of the game (for example a 64-bit game beside a\n");
        s.Append("  32-bit library, or an Intel library on an ARM machine). Download the build that\n");
        s.Append("  matches this machine and run it from the SAME FOLDER it arrived in.\n\n");
        s.Append("  this process                  : ").Append(Safe(() => RuntimeInformation.ProcessArchitecture.ToString()))
         .Append(" on ").Append(Safe(() => RuntimeInformation.OSArchitecture.ToString())).Append('\n');
        s.Append("  beside the program            : ").Append(BesideExe(file)).Append('\n');
        s.Append("  the loader searched           :\n");
        foreach (string d in SearchPath()) s.Append("      ").Append(d).Append('\n');
        s.Append("  runtime message               : ").Append(OneLine(msg)).Append('\n');
        return s.ToString();
    }

    static string WrongVersion(string msg)
    {
        string file = NativeFileName("raylib");
        var s = new StringBuilder();
        s.Append("  SIGHTLINE'S GRAPHICS LIBRARY IS THE WRONG VERSION.\n\n");
        s.Append("  The library loaded, but a function this build of the game needs is missing from\n");
        s.Append("  it — which usually means a DIFFERENT raylib already installed on this machine is\n");
        s.Append("  being picked up instead of the one that shipped with the game. Run the game from\n");
        s.Append("  the SAME FOLDER it arrived in and make sure that folder's  ").Append(file).Append("  is\n");
        s.Append("  present and is the one that came with this download.\n\n");
        s.Append("  beside the program            : ").Append(BesideExe(file)).Append('\n');
        s.Append("  the loader searched           :\n");
        foreach (string d in SearchPath()) s.Append("      ").Append(d).Append('\n');
        s.Append("  runtime message               : ").Append(OneLine(msg)).Append('\n');
        return s.ToString();
    }

    /// Is the native library actually next to the binary? The single most useful fact in the whole
    /// report, and the runtime's own message never says it.
    static string BesideExe(string file)
    {
        try
        {
            string p = Path.Combine(AppContext.BaseDirectory ?? ".", file);
            if (!File.Exists(p)) return "MISSING  (" + p + ")";
            return "present, " + new FileInfo(p).Length + " bytes  (" + p + ")";
        }
        catch (Exception e) { return "<unavailable: " + e.GetType().Name + ">"; }
    }

    /// Where the loader looked. `NATIVE_DLL_SEARCH_DIRECTORIES` is what the host actually consults
    /// for a self-contained app; the platform variable is the system half.
    public static List<string> SearchPath()
    {
        var outp = new List<string>();
        try
        {
            outp.Add("AppContext.BaseDirectory = " + AppContext.BaseDirectory);
            if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string nat && nat.Length > 0)
                foreach (string d in nat.Split(Path.PathSeparator))
                    if (!string.IsNullOrWhiteSpace(d)) outp.Add("NATIVE_DLL_SEARCH_DIRECTORIES: " + d);
            string envName = OperatingSystem.IsWindows() ? "PATH"
                           : OperatingSystem.IsMacOS() ? "DYLD_LIBRARY_PATH" : "LD_LIBRARY_PATH";
            string ev = Environment.GetEnvironmentVariable(envName);
            outp.Add(envName + " = " + (string.IsNullOrEmpty(ev) ? "(not set)" : ev));
            if (!OperatingSystem.IsWindows())
                outp.Add("plus the system library path (ldconfig / dyld defaults)");
        }
        catch (Exception e) { outp.Add("<unavailable: " + e.GetType().Name + ">"); }
        return outp;
    }

    // ── the Windows console (docs/DISTRIBUTION.md §7) ────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AllocConsole();
    const uint AttachParentProcess = 0x0FFFFFFF;   // ATTACH_PARENT_PROCESS

    /// THE WINDOWS CONSOLE WINDOW, second half.
    ///
    /// `Sightline.csproj` now publishes a Windows RID as `WinExe`, so a player double-clicking the
    /// game no longer gets a black console window sitting behind it — that was C6's open item and
    /// it is a real defect of the shipped artifact. The cost is that a `WinExe` has no console at
    /// all, so on Windows `Console.WriteLine` goes nowhere and every `SIGHTLINE_*TEST` in this
    /// project would print its PASS/FAIL into the void.
    ///
    /// This is the standard remedy: attach to the console of whichever process launched us (a
    /// cmd.exe or PowerShell window), and if there is none but a `SIGHTLINE_*` variable is set,
    /// allocate one. A player double-clicking from Explorer has no parent console and sets no
    /// harness variable, so they get neither — which is the entire point.
    ///
    /// **NOT OBSERVED ON WINDOWS.** Nothing in this sandbox can execute a Windows binary. This is
    /// written from the documented `AttachConsole(ATTACH_PARENT_PROCESS)` contract, not from a
    /// measurement, and docs/DISTRIBUTION.md §7 carries it as a declared-open item with the exact
    /// command a Windows machine should run to close it. What IS measured is the other half: the
    /// PE subsystem byte of the cross-published `.exe`, checked by `scripts/publish.sh`.
    ///
    /// It is a no-op everywhere else — the guard on the first line is what keeps the Linux build
    /// and this project's whole headless harness untouched by any of it.
    public static void AttachWindowsConsole()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            bool harness = false;
            foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
                if (kv.Key is string k && k.StartsWith("SIGHTLINE_", StringComparison.Ordinal)) { harness = true; break; }

            if (!AttachConsole(AttachParentProcess))
            {
                if (!harness) return;            // double-clicked by a player: stay silent
                if (!AllocConsole()) return;
            }
            // The streams were bound before a console existed; rebind or they stay inert.
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch { /* never let a console convenience stop the game from starting */ }
    }

    // ── small helpers, all of them non-throwing ──────────────────────────────────────────────

    static string Safe(Func<string> f)
    {
        try { return f() ?? "-"; } catch (Exception e) { return "<unavailable: " + e.GetType().Name + ">"; }
    }
    static Exception SafeInner(Exception e) { try { return e.InnerException; } catch { return null; } }
    static string SafeDir() { try { return Dir; } catch { return "<unknown>"; } }
    static string SafePid() { try { return Environment.ProcessId.ToString(); } catch { return "0"; } }
    static string OneLine(string s)
    {
        try { return s == null ? "-" : s.Replace('\r', ' ').Replace('\n', ' '); } catch { return "-"; }
    }
    /// The one-line stderr headline. CAPPED: an exception message can be arbitrarily long (a
    /// generated one, a serialized payload), and dumping four hundred kilobytes into somebody's
    /// terminal is its own small disaster. The FILE carries the full text — up to MaxReportBytes —
    /// so nothing diagnostic is lost by keeping the terminal line readable.
    const int HeadlineMax = 400;
    static string Headline(Exception ex)
    {
        try
        {
            if (ex == null) return "<null exception>";
            string s = ex.GetType().Name + ": " + ex.Message;
            return s.Length <= HeadlineMax
                 ? s
                 : s.Substring(0, HeadlineMax) + "... [" + (s.Length - HeadlineMax) + " more chars in the report]";
        }
        catch (Exception e) { return "<an exception whose own message could not be read: " + e.GetType().Name + ">"; }
    }
    static int Count(List<Unit> l) { try { return l == null ? 0 : l.Count; } catch { return -1; } }
    static int Alive(List<Unit> l)
    {
        try
        {
            if (l == null) return 0;
            int n = 0;
            foreach (var u in l) if (u != null && u.Alive) n++;
            return n;
        }
        catch { return -1; }
    }
}
