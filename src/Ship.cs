using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Sightline;

/// PROGRAM CONTOUR wave C6 "SHIPS LIKE A PRODUCT" — the DISTRIBUTABLE's own contract.
///
/// Nine autonomous programs made this game good. None of them made it a thing you can hand
/// someone. "Builds clean and passes autoplay" is a development standard; a product standard is a
/// fresh machine, an empty profile, a double-clicked binary in a directory nobody chose, and no
/// forgiveness. The one time anyone checked a neighbouring case (RESONANCE F1) they found a
/// published build launched from the wrong directory silently lost its font — a total-failure bug
/// that every self-test passed straight through, BECAUSE EVERY SELF-TEST RAN FROM THE SOURCE TREE.
///
/// That is the class this file exists for. Two things make it invisible to the rest of the suite:
///   1. `Cfg.AssetPath` falls back to a cwd-relative path when the baked path is missing. That
///      fallback is deliberate (dev convenience, dropped-in audio) and it is also a MASK: run from
///      the source tree, a bundled file that the .csproj forgot to copy still resolves — off the
///      repo, not off the build output — and the test passes on a build that is broken for a
///      player. So the manifest leg below resolves STRICTLY against AppContext.BaseDirectory.
///   2. Every harness hook in the project sets `NoPersist`, so no headless path has ever written a
///      real profile and read it back. The round-trip leg below deliberately does not.
public static class Ship
{
    // ── VERSION STAMP ────────────────────────────────────────────────────────────────────────
    /// The build's version, read off the assembly (single source: <Version> in Sightline.csproj).
    /// Never hard-code it here — a second copy is a second thing to forget to bump.
    public static string Version
    {
        get
        {
            try
            {
                var a = typeof(Ship).Assembly;
                string v = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (string.IsNullOrWhiteSpace(v)) v = a.GetName().Version?.ToString();
                if (string.IsNullOrWhiteSpace(v)) return "0.0.0";
                // SourceLink appends "+<commit sha>" to the informational version on some builds.
                int plus = v.IndexOf('+');
                return plus > 0 ? v.Substring(0, plus) : v;
            }
            catch { return "0.0.0"; }
        }
    }

    /// What the HUD paints. One string, so the main menu and the pause card can never disagree,
    /// and so a bug report says something a maintainer can act on.
    public static string VersionLabel => "SIGHTLINE v" + Version;

    // ── THE BUNDLED MANIFEST ─────────────────────────────────────────────────────────────────
    /// Every file that must sit next to the executable in a shipped build. Three kinds of thing
    /// are in here and all are load-bearing:
    ///   • assets the game cannot render without (the two font faces);
    ///   • LICENCE OBLIGATIONS — the OFL text for each font, the third-party notices, and the
    ///     project's own LICENSE. Shipping the fonts without their licence text is a compliance
    ///     break; shipping the build with no statement of its own terms leaves a recipient unable
    ///     to tell what they may do with it;
    ///   • the two CREDITS ledgers, which are the drop-in audio provenance record AND the
    ///     directories the file-first audio loader looks in (Audio.cs resolves
    ///     assets/sfx/<cue>.ogg and assets/music/{ambient,combat}.ogg through Cfg.AssetPath).
    /// Adding a bundled file means adding it HERE and to the .csproj's copy list. The two halves
    /// disagreeing is precisely the failure this manifest exists to name.
    ///
    /// REVIEW FIX (C6, sent back): the first version of this array shipped with SIX entries while
    /// docs/DISTRIBUTION.md section 1 — written by the same wave, in the same commit — called the
    /// output "ten files, and you must ship all of them" and listed both CREDITS ledgers. So the
    /// two halves this comment warns about ALREADY disagreed at merge time, and a build output
    /// with both ledgers deleted read SHIPTEST: PASS. A manifest that does not match the document
    /// it is the guard for is worse than no manifest, because it gets quoted as evidence.
    public static readonly string[] RequiredFiles =
    {
        "assets/NotoMono-Regular.ttf",
        "assets/NotoMono-LICENSE.txt",
        "assets/ChakraPetch-Bold.ttf",
        "assets/ChakraPetch-LICENSE.txt",
        "assets/sfx/CREDITS.txt",
        "assets/music/CREDITS.txt",
        "THIRD-PARTY-NOTICES.txt",
        "LICENSE",
    };

    /// Strings that MUST appear in the shipped THIRD-PARTY-NOTICES.txt. File.Exists is not
    /// compliance: an empty or truncated notices file satisfies it and satisfies nothing else.
    ///
    /// REVIEW FIX (C6, sent back): these used to be ONE array of eight advertised as "components",
    /// which was wrong twice over — three of the eight are LICENCE names, not components, and the
    /// raylib entry was the bare string "raylib", trivially satisfied by the substring inside
    /// "Raylib-cs". A notices file that had lost the entire raylib section would still have passed.
    /// Split, and each component is now keyed on a string that appears ONLY in its own section.
    static readonly string[] NoticeComponents =
    {
        "Ramon Santamaria",   // raylib's copyright holder — absent iff the raylib notice is gone
        "raylib-cs",          // the binding's own copyright line ("Copyright (C) 2018-2025 raylib-cs")
        ".NET Foundation",    // the runtime's copyright holder
        "Noto Mono",
        "Chakra Petch",
    };
    /// ...and the licences those components are under. Losing the licence TEXT while keeping a
    /// component's header is its own compliance failure, so it gets its own assertion.
    static readonly string[] NoticeLicences = { "zlib", "MIT License", "Open Font License" };

    /// Resolve a bundled file STRICTLY against the executable's own directory — no cwd fallback.
    /// This is the path a player's launch actually takes; Cfg.AssetPath's fallback is the mask.
    public static string BakedPath(string rel) =>
        Path.Combine(AppContext.BaseDirectory, rel.Replace('/', Path.DirectorySeparatorChar));

    // ── SIGHTLINE_SHIPTEST ───────────────────────────────────────────────────────────────────
    /// Six legs, all aimed at the SEAM between the built artifact and the machine it lands on
    /// rather than at the game model:
    ///   (1) MANIFEST      every RequiredFiles entry exists next to the binary, is non-empty, and
    ///                     is what Cfg.AssetPath actually returns (i.e. the cwd fallback is NOT
    ///                     what is carrying it). The fonts are checked for a real sfnt magic.
    ///   (2) NOTICES       the shipped notices file names every component this build redistributes
    ///                     (each keyed on a string unique to its own section) AND every licence
    ///                     those components are under.
    ///   (3) PROFILE       the player-data dir is an ABSOLUTE path, a profile written through the
    ///                     real public API reads back off disk AND is present in the file's raw
    ///                     bytes, and — leg (3b) — a SECOND PROCESS of this same binary banks a
    ///                     sentinel that this one then reads back. Literally "launch it twice".
    ///   (4) ATOMIC        save.json / meta.json / display.json are each written by rename, proven
    ///                     by MECHANISM: hold an open handle across the write and assert it still
    ///                     sees the old inode's bytes. A truncate-in-place writer fails this.
    ///                     SCOPE, STATED PLAINLY: the inode swap makes the write atomic against a
    ///                     CONCURRENT READER and against PROCESS DEATH (crash, kill -9, OOM kill).
    ///                     It is NOT proof of POWER-LOSS durability — that needs an fsync of the
    ///                     tmp AND of the directory, which this code does not do. Plus SweepProbe:
    ///                     a write that FAILS must not leave its .tmp behind.
    ///   (5) TRIMSAFE      every persisted DTO is reachable from a source-generated JSON context —
    ///                     the half of the PublishTrimmed hazard MSBuild cannot see.
    ///   (6) VERSION       the assembly carries a MAJOR.MINOR.PATCH stamp and the HUD's label
    ///                     contains it.
    ///
    /// Stashes and restores the real save.json / meta.json / display.json around leg (3) and (4),
    /// the SAVETEST/METATEST house pattern — these legs write to the live player-data directory on
    /// purpose, because writing somewhere else is exactly the bug they are looking for.
    public static string SelfTest()
    {
        var fails = new List<string>();
        try
        {
            // ---- (1) MANIFEST ---------------------------------------------------------------
            foreach (string rel in RequiredFiles)
            {
                string baked = BakedPath(rel);
                if (!File.Exists(baked)) { fails.Add("missing:" + rel); continue; }
                long len = new FileInfo(baked).Length;
                if (len <= 0) { fails.Add("empty:" + rel); continue; }
                // The resolution a player gets must be the BAKED one. If AssetPath hands back the
                // bare relative string, the file is not next to the binary and only the cwd is
                // saving us — which is the F1 bug, one directory away from happening again.
                if (rel.StartsWith("assets/") && Cfg.AssetPath(rel) != baked) fails.Add("notBaked:" + rel);
                if (rel.EndsWith(".ttf") && !LooksLikeFont(baked)) fails.Add("notAFont:" + rel);
            }

            // ---- (2) NOTICES ----------------------------------------------------------------
            string noticePath = BakedPath("THIRD-PARTY-NOTICES.txt");
            if (File.Exists(noticePath))
            {
                string notices = File.ReadAllText(noticePath);
                foreach (string need in NoticeComponents)
                    if (notices.IndexOf(need, StringComparison.OrdinalIgnoreCase) < 0)
                        fails.Add("noticeOmitsComponent:" + need);
                foreach (string need in NoticeLicences)
                    if (notices.IndexOf(need, StringComparison.OrdinalIgnoreCase) < 0)
                        fails.Add("noticeOmitsLicence:" + need);
            }

            // ---- (3)+(4) the player-data directory ------------------------------------------
            // Absolute-path guard first: GetFolderPath returns "" when the config dir does not
            // exist yet, which used to scatter a RELATIVE Sightline/ per launch directory.
            string dir = SaveGame.ConfigDir;
            if (string.IsNullOrEmpty(dir) || !Path.IsPathRooted(dir)) fails.Add("configDirNotRooted:" + dir);

            string savePath = SaveGame.SavePathPublic, metaPath = SaveGame.MetaPathPublic,
                   dispPath = Display.SettingsPathPublic;
            string saveWas = Read(savePath), metaWas = Read(metaPath), dispWas = Read(dispPath);
            // REVIEW FIX (C6, sent back) — STASH THE .tmp SIBLINGS TOO. Restore used to delete
            // <path>.tmp unconditionally, so running qa-sweep.sh silently DESTROYED a .tmp left
            // behind by a real crash — the single artefact a maintainer would have to reason about
            // that crash from. A test may only remove what it created.
            string saveTmpWas = Read(savePath + ".tmp"), metaTmpWas = Read(metaPath + ".tmp"),
                   dispTmpWas = Read(dispPath + ".tmp");
            try
            {
                // (3) PROFILE ROUND-TRIP — write through the REAL API, then read back. Every
                // Load*() below re-parses meta.json from disk (there is no in-process profile
                // cache), so this is a genuine disk round trip: launch 1 writes, launch 2 reads.
                try { if (File.Exists(metaPath)) File.Delete(metaPath); } catch { }
                SaveGame.AddSalvage(137);
                SaveGame.AddUnlock(1);
                SaveGame.UnlockAchievement("C6_SHIPTEST");
                SaveGame.SaveMetaHeat(2);
                if (!File.Exists(metaPath)) fails.Add("relaunchNoMetaFile");
                // ...and it landed in THE PLAYER-DATA FILE, not somewhere else. Reading the raw
                // bytes is the half that catches a write which "succeeded" into a relative path.
                else if (!(Read(metaPath) ?? "").Contains("137")) fails.Add("relaunchNotInMetaFile");
                if (SaveGame.LoadSalvage() != 137) fails.Add("relaunchSalvage");
                if (!SaveGame.HasUnlock(1)) fails.Add("relaunchUnlock");
                if (!SaveGame.LoadAchievements().Contains("C6_SHIPTEST")) fails.Add("relaunchAchievement");
                if (SaveGame.LoadMetaHeat() != 2) fails.Add("relaunchHeat");

                // (3b) THE ACTUAL SECOND LAUNCH. Everything above happens in ONE process, and one
                // process cannot prove "quit the game, start it again, your progress is there" —
                // which is the property a player cares about and the one no hook in this project
                // could check, because the house rule (never touch the real profile) makes every
                // harness hook stash-and-restore inside a single run. So: fork THIS SAME BINARY
                // with SIGHTLINE_SHIPCHILD=1, let the child write a sentinel through the ordinary
                // public API, wait for it to exit, and read the sentinel back HERE. Two processes,
                // one player-data directory, the shipped code on both sides.
                string childWhy = SecondLaunchProbe();
                if (childWhy != null) fails.Add("secondLaunch:" + childWhy);

                // (4) ATOMIC WRITES, by mechanism.
                foreach (var (label, path, write) in new (string, string, Action)[]
                {
                    ("meta",    metaPath, () => SaveGame.SaveMetaHeat(3)),
                    ("display", dispPath, Display.SaveForTest),
                    ("save",    savePath, () => { var r = new Run(); r.Start(); SaveGame.Save(r); }),
                })
                {
                    string why = AtomicityProbe(path, write);
                    if (why != null) fails.Add(label + "NotAtomic:" + why);
                    // REVIEW FIX (C6, sent back) — THIS USED TO BE `if (File.Exists(path + ".tmp"))
                    // fails.Add(label + "StaleTmp")`, WHICH COULD NOT FAIL. It ran only after a
                    // SUCCESSFUL WriteAtomic, whose File.Move has already consumed the .tmp, so it
                    // asserted a tautology — and a stale .tmp pre-seeded in the profile directory
                    // still read PASS while the PASS string advertised "no stale .tmp". It is kept
                    // only as a SHAPE guard (a future writer that stopped renaming would trip it);
                    // the actual sweep is proven by SweepProbe below, on a scratch path, by forcing
                    // the rename to fail. The PASS string no longer claims anything for this line.
                    if (File.Exists(path + ".tmp")) fails.Add(label + "TmpSurvivedSuccessfulWrite");
                }

                // ...and the sweep itself, which is a property of the FAILURE path and therefore
                // cannot be observed after a write that worked.
                string sweepWhy = SweepProbe();
                if (sweepWhy != null) fails.Add("sweep:" + sweepWhy);
            }
            finally
            {
                Restore(savePath, saveWas, saveTmpWas);
                Restore(metaPath, metaWas, metaTmpWas);
                Restore(dispPath, dispWas, dispTmpWas);
                Display.LoadForTest();   // hand the player's own settings back to the process too
            }

            // ---- (5) TRIMSAFE ---------------------------------------------------------------
            // Reflection-based System.Text.Json loses the metadata `-p:PublishTrimmed=true` strips;
            // a DTO that is not reachable from a [JsonSerializable] root serialises fine here and
            // fails ONLY in the shipped build. Ask each context whether it can actually see the type.
            foreach (var (label, ok) in new (string, bool)[]
            {
                ("RunDto",  SaveGame.SaveJson.Default.GetTypeInfo(typeof(SaveGame.RunDto))  != null),
                ("MetaDto", SaveGame.SaveJson.Default.GetTypeInfo(typeof(SaveGame.MetaDto)) != null),
                ("DisplayDto", Display.DisplayJson.Default.GetTypeInfo(typeof(Display.Dto)) != null),
            })
                if (!ok) fails.Add("notSourceGenerated:" + label);

            // ---- (6) VERSION ----------------------------------------------------------------
            var parts = Version.Split('.');
            if (parts.Length < 3 || !int.TryParse(parts[0], out _) || !int.TryParse(parts[1], out _)
                                 || !int.TryParse(parts[2], out _))
                fails.Add("versionShape:" + Version);
            if (VersionLabel.IndexOf(Version, StringComparison.Ordinal) < 0) fails.Add("versionLabelDrift");
            if (Hud.IntroFooter.IndexOf(Version, StringComparison.Ordinal) < 0) fails.Add("versionNotPainted");

            return fails.Count == 0
                ? $"SHIPTEST: PASS ({RequiredFiles.Length} bundled files resolve next to the binary, none via the cwd fallback; "
                  + $"notices name all {NoticeComponents.Length} redistributed components and all {NoticeLicences.Length} of their licences; "
                  + "player-data dir rooted; profile round-trips through disk"
                  + (_secondLaunchSkip == null ? " and survives a real second launch of this binary"
                                               : $" (SECOND-LAUNCH LEG SKIPPED: {_secondLaunchSkip})")
                  + "; save/meta/display all written by rename, proven by an open-handle inode probe (process death "
                  + "and concurrent readers, NOT power loss - no fsync); a failed write sweeps its own .tmp; "
                  + $"3 persisted DTOs source-generated; build stamped v{Version} and painted)"
                : "SHIPTEST: FAIL (" + string.Join(",", fails) + ")";
        }
        catch (Exception e) { return "SHIPTEST: FAIL (exception " + e.GetType().Name + ": " + e.Message + ")"; }
    }

    /// THE SWEEP PROBE — does a FAILED WriteAtomic leave its .tmp behind?
    ///
    /// docs/DISTRIBUTION.md section 5 says a *.json.tmp "should never persist", and the measured
    /// full-disk run that motivated the sweep showed the pre-C6 shape leaving a 0-byte corpse on
    /// ENOSPC forever. That is a property of the FAILURE path, so it is unobservable after a write
    /// that succeeded — which is exactly how the first version of this check ended up tautological.
    ///
    /// Force the failure deterministically and at the right MOMENT: make the TARGET a directory.
    /// The .tmp write then succeeds and the rename onto a directory cannot, so control lands in
    /// WriteAtomic's catch with a corpse on disk — the precise state the sweep exists for. Runs on
    /// a scratch directory under the OS temp path, never in the player's data directory.
    /// Returns null on pass, else the reason. Remove the sweep from WriteAtomic and this reads
    /// "sweep:tmpNotSwept".
    static string SweepProbe()
    {
        string dir = Path.Combine(Path.GetTempPath(), "c6sweep_" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(dir, "blocked.json");
        try
        {
            Directory.CreateDirectory(target);        // the target IS a directory -> rename must fail
            bool threw = false;
            try { SaveGame.WriteAtomic(target, "{\"probe\":1}"); }
            catch { threw = true; }
            if (!threw) return "writeUnexpectedlySucceeded";   // the probe is not exercising failure
            if (File.Exists(target + ".tmp")) return "tmpNotSwept";
            return null;
        }
        catch (Exception e) { return "probeThrew:" + e.GetType().Name; }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── the second-launch child ──────────────────────────────────────────────────────────────
    /// The sentinel the child banks and the parent reads back. A value no real profile produces.
    const int ChildSalvage = 4242;
    const string ChildAchievement = "C6_SECOND_LAUNCH";

    /// Run by the CHILD process (SIGHTLINE_SHIPCHILD=1, dispatched from Program.Main before the
    /// window opens). Writes the sentinel through the same public API the game uses and exits.
    public static void SecondLaunchChild()
    {
        try
        {
            SaveGame.AddSalvage(ChildSalvage);
            SaveGame.UnlockAchievement(ChildAchievement);
            Console.WriteLine("SHIPCHILD: wrote");
        }
        catch (Exception e) { Console.WriteLine("SHIPCHILD: threw " + e.GetType().Name); }
    }

    /// Set when the second-launch leg could not identify a way to relaunch this build. Reported in
    /// the PASS string rather than swallowed — a leg that silently stops running is a lie.
    static string _secondLaunchSkip;

    /// How to start a second copy of THIS build: the executable, plus any arguments it needs.
    ///
    /// REVIEW FIX (C6, sent back — and it was THE SAME BUG CLASS THIS WAVE EXISTS TO KILL).
    /// The first version used `Environment.ProcessPath` unconditionally. Under `dotnet Sightline.dll`
    /// that is the **dotnet muxer**, so the probe spawned a bare `dotnet`, which printed its usage
    /// banner, exited 0, and wrote no sentinel — `SHIPTEST: FAIL (secondLaunch:salvageNotCarried)`
    /// **on a completely correct build**. Neither `qa-sweep.sh` (`dotnet run`) nor `publish.sh`
    /// (`./Sightline`) uses that invocation so the gate was never wrong in practice, but that is
    /// exactly the excuse that let SAVETEST's `metaUnlockPhantom` sit on the publish gate: an
    /// environment-dependent FAIL on a good build is a defect whether or not today's callers trip it.
    ///
    /// So: identify the launcher instead of assuming it.
    ///   • ProcessPath's filename matches our assembly  -> apphost or published single-file exe. Use it.
    ///   • otherwise                                    -> we are under the muxer; relaunch as
    ///                                                     `<ProcessPath> <our .dll>`, which is a
    ///                                                     genuine second process of this build.
    ///   • neither identifiable                         -> SKIP with a stated reason. Never FAIL.
    static (string exe, string arg, string skip) SecondLaunchCommand()
    {
        string exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            return (null, null, "noProcessPath");

        var asm = typeof(Ship).Assembly;
        string asmName = asm.GetName().Name;                                   // "Sightline"
        string procName = Path.GetFileNameWithoutExtension(exe);               // "Sightline" or "dotnet"
        if (string.Equals(procName, asmName, StringComparison.OrdinalIgnoreCase))
            return (exe, null, null);                                          // apphost / single-file

        // Under the muxer. Assembly.Location is "" for a single-file build, but a single-file build
        // never lands here (its ProcessPath IS the assembly name), so a blank one means we genuinely
        // cannot name the thing to relaunch.
        string dll = null;
        try { dll = asm.Location; } catch { }
        if (string.IsNullOrEmpty(dll) || !File.Exists(dll))
            return (null, null, "launcherIsHost:" + procName);
        return (exe, dll, null);
    }

    /// Parent side. Returns null on pass, else the reason.
    /// The child is a second process of THIS build (see SecondLaunchCommand). It inherits the
    /// environment minus every SIGHTLINE_* variable, so it resolves the same player-data directory
    /// (XDG_CONFIG_HOME included, which is what keeps parallel agents isolated). Bounded wait; a
    /// child that hangs is a failure, not a hang here.
    static string SecondLaunchProbe()
    {
        var (exe, arg, skip) = SecondLaunchCommand();
        if (skip != null) { _secondLaunchSkip = skip; return null; }
        int before = SaveGame.LoadSalvage();
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            if (arg != null) psi.ArgumentList.Add(arg);
            // GUARD (b) — see the note at the top of Program.Main. psi.Environment starts as a COPY
            // of ours, which includes SIGHTLINE_SHIPTEST=1: the first version of this leg let the
            // child inherit it, the child ran SHIPTEST, SHIPTEST forked a grandchild, and the thing
            // fork-bombed to 184 processes on a shared container. Strip EVERY SIGHTLINE_* variable
            // so the child inherits no test mode, no forced objective, no balance slot — nothing
            // but the one flag below. XDG_CONFIG_HOME is NOT a SIGHTLINE_ variable and survives,
            // which is what keeps the child pointed at the same isolated player-data directory.
            foreach (var key in new List<string>(psi.Environment.Keys))
                if (key.StartsWith("SIGHTLINE_", StringComparison.Ordinal)) psi.Environment.Remove(key);
            psi.Environment["SIGHTLINE_SHIPCHILD"] = "1";
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return "childDidNotStart";
            // REVIEW FIX (C6, sent back) — "bounded wait" has to be TRUE, not nearly true. This was
            // two sequential blocking `ReadToEnd()` calls BEFORE WaitForExit, and ReadToEnd has no
            // timeout: a child that filled a redirected pipe would hang the whole sweep for ever.
            // It cannot happen today (the child writes ~15 bytes and exits before any window opens)
            // — but the claim in the comment was stronger than the code, and that is the thing this
            // project punishes. Drain both pipes on background threads, THEN wait with a deadline.
            proc.OutputDataReceived += (_, __) => { };
            proc.ErrorDataReceived  += (_, __) => { };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            if (!proc.WaitForExit(60_000)) { try { proc.Kill(true); } catch { } return "childTimedOut"; }
        }
        catch (Exception e) { return "childThrew:" + e.GetType().Name; }

        // Read what the OTHER process banked. Not our own in-memory state — the file it left.
        if (SaveGame.LoadSalvage() != before + ChildSalvage) return "salvageNotCarried";
        if (!SaveGame.LoadAchievements().Contains(ChildAchievement)) return "achievementNotCarried";
        return null;
    }

    // ── probes ───────────────────────────────────────────────────────────────────────────────

    /// Is this a real sfnt container? Guards the case where a font "exists" as a 0-byte placeholder
    /// or an HTML error page a download turned into.
    static bool LooksLikeFont(string path)
    {
        try
        {
            var b = new byte[4];
            using (var fs = File.OpenRead(path)) { if (fs.Read(b, 0, 4) != 4) return false; }
            uint tag = (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
            return tag == 0x00010000u          // TrueType outlines
                || tag == 0x74727565u          // 'true'
                || tag == 0x74746366u          // 'ttcf' collection
                || tag == 0x4F54544Fu;         // 'OTTO' CFF outlines
        }
        catch { return false; }
    }

    /// THE ATOMICITY PROBE. Puts a known marker in `path`, opens a shared read handle on it, runs
    /// `write`, and then reads through THAT HANDLE.
    ///
    /// A rename-based writer streams into a NEW inode and swaps the directory entry, so the handle
    /// — which still refers to the old, now-unlinked inode — keeps seeing the old bytes. A
    /// truncate-in-place writer (`File.WriteAllText(target, ...)`) rewrites the SAME inode, so the
    /// handle sees the new bytes. That difference IS atomicity: it is exactly whether a concurrent
    /// reader, or a crash, or a full disk, can catch the file half-written.
    ///
    /// Returns null on pass, or the reason it failed. Unix-only mechanism (Windows file semantics
    /// differ and the rename path is not observable this way), so it self-skips off Unix rather
    /// than reporting a failure it cannot judge.
    static string AtomicityProbe(string path, Action write)
    {
        if (OperatingSystem.IsWindows()) return null;
        const string marker = "{\"C6\":\"OLD-INODE-MARKER-PADDING-PADDING-PADDING\"}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, marker);
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                           FileShare.ReadWrite | FileShare.Delete))
            {
                write();
                fs.Seek(0, SeekOrigin.Begin);
                var buf = new byte[marker.Length];
                int n = fs.Read(buf, 0, buf.Length);
                string seen = System.Text.Encoding.UTF8.GetString(buf, 0, n);
                if (seen != marker) return "handleSawNewBytes";   // same inode -> truncated in place
            }
            // and the write must actually have landed (an atomic writer that silently did nothing
            // would also pass the line above)
            string now = File.Exists(path) ? File.ReadAllText(path) : "";
            if (now == marker || now.Length == 0) return "writeDidNotLand";
            return null;
        }
        catch (Exception e) { return "probeThrew:" + e.GetType().Name; }
    }

    static string Read(string p) { try { return File.Exists(p) ? File.ReadAllText(p) : null; } catch { return null; } }

    /// Put the player's directory back exactly as it was — the FILE and its .tmp sibling, each
    /// restored to the bytes it held (or removed if it did not exist). `tmpWas` is not an
    /// afterthought: the previous version deleted <p>.tmp unconditionally, which meant this test
    /// destroyed the evidence of somebody else's crash every time the sweep ran.
    static void Restore(string p, string was, string tmpWas)
    {
        Put(p, was);
        Put(p + ".tmp", tmpWas);
    }
    static void Put(string p, string was)
    {
        try
        {
            if (was != null) { Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllText(p, was); }
            else if (File.Exists(p)) File.Delete(p);
        }
        catch { }
    }
}
