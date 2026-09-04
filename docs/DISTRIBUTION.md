# DISTRIBUTION — shipping a SIGHTLINE build

Measured on this repo (linux-x64, self-contained, .NET SDK 8.0.130). The publish matrix, the
licence status and the player-data section were **re-measured from scratch by PROGRAM CONTOUR
wave C6 on 2026-08-30**, base commit `17934ee` + `wave/ships`; the F1-era numbers they replace are
noted where they differ. Commands are hand-run; **nothing in this document is wired to CI, and
nothing here may be** (see CLAUDE.md, hard constraints).

---

## 1. How to publish

```bash
bash scripts/publish.sh            # recommended build -> dist/linux-x64-release/
bash scripts/publish.sh --small    # smallest binary
bash scripts/publish.sh --no-trim  # fallback if trimming ever becomes unsafe again
bash scripts/publish.sh --rid win-x64
bash scripts/publish.sh --tag      # ...and create the LOCAL release tag (never pushes) — §8
bash scripts/publish.sh --no-archive   # stop at the directory; no archive/checksum/changelog
```

**Since P17 a publish does not stop at a directory** — it also derives `CHANGELOG.md`, packs a
versioned archive, writes a checksum beside it and re-verifies all three. **§8** is the contract.

The script publishes, then **re-proves the artifact against the binary it just built** by running
`SIGHTLINE_SAVETEST`, `SIGHTLINE_METATEST`, (C6) `SIGHTLINE_SHIPTEST` and (P11)
`SIGHTLINE_CRASHTEST` on it, and fails the publish if any of the four does not say PASS. The first
two exist because of §3; the third exists because the source tree can resolve a bundled file the
build output is missing (see below); the fourth because a crash reporter that only works in the
development build is that same defect wearing a different hat (§6). On a **win-\*** RID it
additionally reads the published `.exe`'s PE subsystem byte and fails if a console window would
appear behind the game (§7).

**Do not publish with a bare `dotnet publish`.** It is not that the flags are wrong — the script
just passes them — it is that a bare publish skips all three verifications, which is precisely how
a totally broken build once looked green. C6 additionally made the two §3 mitigations a **build
error to remove** (`C6GuardTrimmedPersistence` in `Sightline.csproj`): a trimmed publish with
`JsonSerializerIsReflectionEnabledByDefault` off or `TrimmerRootAssembly` empty now fails to build,
with the reason and a pointer to §3. Verified by running exactly that command.

### What ships

`dist/<rid>-<mode>/` contains **eleven files**, and you must ship **all** of them:

| File | Why |
|---|---|
| `Sightline` (`Sightline.exe`) | the game (self-contained: no .NET install needed on the target machine) |
| `libraylib.so` (`raylib.dll`) | raylib is a native library the runtime `dlopen()`s — it cannot be linked into the single file |
| `assets/NotoMono-Regular.ttf`, `assets/ChakraPetch-Bold.ttf` | the two baked font faces; without them the game boots and silently falls back to raylib's bitmap font |
| `assets/NotoMono-LICENSE.txt`, `assets/ChakraPetch-LICENSE.txt` | OFL obligation: the licence text ships beside the font it covers |
| `assets/sfx/CREDITS.txt`, `assets/music/CREDITS.txt` | the drop-in audio provenance ledger (and the directories the file-first loader looks in) |
| `THIRD-PARTY-NOTICES.txt` | licence obligation (§4) — not optional |
| `CHANGELOG.md` | (P17) what changed, **derived from git history at publish time** by `scripts/changelog.sh` — see §8. Not committed and not in `Ship.RequiredFiles`: a `dotnet build` output legitimately has none, which is why its self-test leg is gated on `SIGHTLINE_RELEASEDIR` |
| `LICENSE` | **the project's own terms.** C6 fix: §4 decided this in CROSSCUT and committed a root `LICENSE`, but nothing ever copied it into a build — every distributable this repo had ever produced shipped the third-party notices and *no statement of its own terms*, so a recipient could not tell what they were allowed to do with it |

Assets are resolved against **the executable's own directory**, not the process working
directory, so the game runs correctly from a shortcut or a launcher. (Verified by C6 against the
published binary, installed at `/home/user/player path/SIGHTLINE Game` — a path outside the source
tree, *with a space in it* — against an empty player-data directory: both font atlases load from
the install directory by absolute path and a full campaign reaches `RESULT: WIN mission=6`. Before
the F1 fix it printed `FILEIO: [assets/NotoMono-Regular.ttf] Failed to open file` and silently fell
back to raylib's built-in font.)

**`SIGHTLINE_SHIPTEST` is the guard for this table** (C6). It resolves every entry in
`Ship.RequiredFiles` **strictly** against `AppContext.BaseDirectory` — deliberately refusing
`Cfg.AssetPath`'s cwd fallback, which is what made the F1 bug invisible to a suite that only ever
ran from the source tree — checks each is non-empty, checks the two `.ttf`s carry a real sfnt
magic, and checks `THIRD-PARTY-NOTICES.txt` actually *names* all eight redistributed components
rather than merely existing. `scripts/publish.sh` runs it against the published directory on every
publish. Measured: delete `assets/NotoMono-Regular.ttf` from a published directory and the game
still boots (one `WARNING:` line, then the bitmap-font fallback) while SHIPTEST reads
`FAIL (missing:assets/NotoMono-Regular.ttf)`.

**Adding a bundled file means adding it in TWO places** — `Ship.RequiredFiles` and the `.csproj`
copy list. Those two disagreeing is exactly what the manifest leg exists to name — and C6's own
first draft shipped them disagreeing: this table said ten files while `RequiredFiles` listed six,
omitting both `CREDITS.txt` ledgers, so a build output with both deleted read `SHIPTEST: PASS`.
Two independent reviewers found it separately. The manifest now carries all eight non-binary
entries (the executable and `libraylib.so` are the artifact itself, not things it resolves).

---

## 2. The publish matrix (re-measured, C6, 2026-08-30)

`start` = wall time for one window-free `SIGHTLINE_SAVETEST` launch. It measures startup + JIT,
not frame time. **This round was measured INTERLEAVED** — one launch of each mode per round, nine
rounds — so all four modes see the same container load. That matters: this box is shared with five
other agents running balance batches, and a *sequential* pass taken twenty minutes apart put
`no-trim` at 117 ms and then 184 ms. Read the ORDERING as the result and the absolute numbers as
"on a loaded four-core container".

**SIZE CONVENTION: MB = 10^6 bytes**, stated because C6's first draft did not state it and shipped
a README saying "27 MB" (that was MiB) against a §2 saying 28.4 MB for the same directory. Exact
byte counts are given so nobody has to guess again.

| mode | flags | executable | whole directory | files | start (median of 9, min–max) |
|---|---|---|---|---|---|
| **release** (default) | `PublishTrimmed` + `PublishReadyToRun` + `PublishSingleFile` | **26.5 MB** (26,531,848 B) | **29.3 MB** (29,284,496 B) | 10 | **169 ms** (102–323) |
| small | `PublishTrimmed` + `PublishSingleFile` | 16.1 MB | 18.8 MB (18,844,773 B) | 10 | 545 ms (397–696) |
| no-trim | `PublishReadyToRun` + `PublishSingleFile` | 82.3 MB | 85.1 MB (85,076,690 B) | 10 | 206 ms (144–288) |
| plain | `PublishSingleFile` | 68.3 MB | 71.0 MB (71,049,575 B) | 10 | 317 ms (256–405) |
| win-x64 release | cross-published from Linux | 24.8 MB (`.exe`) | 26.9 MB (26,921,328 B) | 10 | not runnable here |

**THE WHOLE TABLE IS THE C6 MEASUREMENT AND IS NOW STALE ON SIZE AS WELL AS ON FILE COUNT.** Two
things moved:

* **Files: 10 → 11.** `CHANGELOG.md` is generated into the payload on every publish (P17, §8). It
  is ~7 KB and below the noise in every size row.
* **Size: the binary GREW between C6 and P17.** The default `release` mode measured here on
  `e57e151` + P17 is **28.2 MB executable (28,152,021 B) / 30.9 MB directory (30,912,495 B) /
  11 files** — against C6's 26.5 MB / 29.3 MB / 10. That is +1.6 MB of code from the waves in
  between (C1–C6, P10–P16), not from this wave. **Quote the row you measured, not this table.**
* **The start column was NOT re-measured by P17** and stays attached to its C6/`d3feb90`
  provenance on a loaded container. Nothing here justifies re-quoting it.

**Sizes are from the final C6 binary; the START COLUMN IS NOT.** The timings were measured on the
pre-review-fix binary earlier the same day. They were deliberately **not** re-run after the review
fixes: the container was under nine concurrent reviewers at load ~60, which would have produced a
worse number rather than a truer one. The review fixes added ~0.9 MB of code and touched nothing on
the startup path, but that is an argument, not a measurement — treat the start column as attached to
commit `d3feb90` and re-measure on a quiet box before quoting it anywhere that matters.

Trimming is what makes `small` slow: it strips the framework's precompiled ReadyToRun code, so
everything JITs at startup. Adding ReadyToRun back costs ~10 MB and produces the **fastest** start
of any configuration — hence the default is both. On a quiet box the default measured **108–114 ms**
median-of-10.

**What moved since F1's table**, and why: the file count went 6 -> 10 (the Chakra Petch pair and
the two `CREDITS.txt` ledgers were added by later waves; `LICENSE` by C6), `small`'s executable
went 18 -> 15.8 MB, and F1's `size` column was the EXECUTABLE while the row now carries both that
and the whole shippable directory — the number you actually hand someone.

**`--rid win-x64` cross-publishes cleanly from Linux** (verified C6: 10 files, P17: 11 with the
changelog — re-verified P17, `SIGHTLINE-v1.0.0-win-x64.zip` 12,624,409 B, PE subsystem 2;
`Sightline.exe` +
`raylib.dll` + the same assets and licence set). Its self-tests cannot be run here, so the Windows
build is **unverified beyond "it produces the right files"** — say so rather than implying it was
exercised.

---

## 3. The `PublishTrimmed` hazard (fixed, but read this before touching serialization)

`PublishTrimmed` used to **silently destroy all persistence**. The trimmed build compiled
with 0 errors, booted, played, and finished a whole autoplay campaign — while saving
nothing at all: no `save.json`, and the entire cross-run meta profile (salvage, veterans,
achievements, hall of fame) gone. `SIGHTLINE_SAVETEST` and `SIGHTLINE_METATEST` both
FAILED against the trimmed binary; `METATEST` reported 38 broken items. A publish wave
grepping for "0 Errors" would have shipped it.

Cause: reflection-based `System.Text.Json` needs type metadata that trimming strips, and
trimming additionally turns the reflection fallback **off** by default
(`InvalidOperationException: Reflection-based serialization has been disabled for this
application`), which the persistence layer's `catch { }` swallowed.

> **The old CLAUDE.md line "never publish with `-p:PublishTrimmed=true`" was STALE and actively
> harmful** — trimmed has been the recommended default since F1 fixed this, and following that line
> would have cost 57 MB and the fastest start in the matrix. C6 corrected it. Trimmed is the
> default; what you must not do is publish *without the script*.

The fix has FOUR parts (C6 added the fourth), all of which must stay in place:

1. **`SaveGame` and `Display` use source-generated `JsonSerializerContext`s**
   (`SaveGame.SaveJson`, `Display.DisplayJson`). No reflection, so nothing to strip. Any
   new persisted DTO must be reachable from a `[JsonSerializable]` root, or it will fail
   only in the trimmed build.
2. **`Sightline.csproj` roots our own assembly** (`TrimmerRootAssembly`) and re-enables
   `JsonSerializerIsReflectionEnabledByDefault` **for trimmed publishes only**. This is
   for the one remaining reflective site, `Stats.WriteJson`, which serialises anonymous
   types no generator can see. Verified: the trimmed binary writes a balance JSON
   byte-identical to the untrimmed one (1777 bytes; it previously wrote nothing).
3. **`Stats.WriteJson` prints its exception** instead of swallowing it. A silent total
   failure is how a broken publish config survives review.
4. **(C6) The mitigations are now enforced, in two independent places.** MSBuild's
   `C6GuardTrimmedPersistence` target errors the build if either knob in (2) is removed — narrow
   by design, because MSBuild cannot possibly know about part (1). Part (1) is covered instead by
   `SIGHTLINE_SHIPTEST`'s TRIMSAFE leg, which asks each source-generated context whether it can
   actually see `RunDto` / `MetaDto` / `Display.Dto`. That leg runs in the UNTRIMMED build, where
   everyone develops — so a new persisted DTO that no `[JsonSerializable]` root can see fails at
   `qa-sweep` time instead of shipping and losing the player's profile.

**What each knob actually protects, measured (C6 review, second reviewer) — the guard's first error
text got this wrong and has been corrected.** With `TrimmerRootAssembly` removed and the reflection
knob kept: `SAVETEST` **passes**, and a run round-trips squad/perks/weapon-mods/card/heat correctly.
With **both** removed: `SAVETEST`, `METATEST` and `SHIPTEST` all still pass. **Player persistence is
protected by source generation alone** — part (1) of the fix, working as designed. What the two
knobs keep alive is the `SIGHTLINE_BALANCE` telemetry export, the one remaining reflective site
(`NotSupportedException: …parameter names have been trimmed by ILLink` without the root;
`InvalidOperationException: Reflection-based serialization has been disabled` without the knob).
Losing the flywheel silently is reason enough for a build error — but the error text must name what
actually breaks, not something worse.

**Trim-safety verified at the metadata level, not by test coverage** (same review): a
`MetadataLoadContext` diff of `obj/…/linux-x64/Sightline.dll` against `obj/…/linked/Sightline.dll`
shows **356 types before trimming, 356 after — zero types removed, no persisted-DTO member
trimmed.** Rooting the assembly means ILLink removes literally nothing from our code.

**`PublishAot` was NOT tested here.** By inspection it sets `PublishTrimmed`, so it trips the same
guard and therefore fails safe — but that is inspection, not a measurement.

**Honest scope of the guard:** it catches *removal of the mitigation*, not every way persistence
can break. A new DTO reachable from a root but with an unsupported member shape, or a trimmer
warning nobody read, still needs `bash scripts/publish.sh` and its printed verification lines. Do
not judge a publish by the build log.

**If you change serialization, run `bash scripts/publish.sh` and read its verification
lines.** Do not judge a publish by the build log.

---

## 4. Licence status

### Third-party components — compliant

`THIRD-PARTY-NOTICES.txt` (repo root, copied into every build and publish output) carries
the required notices. Each text was taken from the package or tag this build actually
consumes, not transcribed from memory; the sources are cited inside the file.

| Component | Licence | Obligation | Status |
|---|---|---|---|
| raylib 6.0 (`libraylib.so`) | Zlib | retain the notice | in `THIRD-PARTY-NOTICES.txt` |
| Raylib-cs 8.0.0 | Zlib | retain the notice | in `THIRD-PARTY-NOTICES.txt` |
| .NET 8 runtime | MIT | retain the copyright notice (a self-contained build redistributes the runtime) | in `THIRD-PARTY-NOTICES.txt` |
| Noto Mono | OFL-1.1 | ship the licence with the font | `assets/NotoMono-LICENSE.txt`, copied to output |

Adding another OFL font means one new entry in the FONTS section of
`THIRD-PARTY-NOTICES.txt` plus its `<Name>-LICENSE.txt` committed in `assets/`.

Nothing else in the shipped build is third-party: all art, audio, shaders and map content
are generated in-engine (CLAUDE.md, "Art policy").

### The repo's own licence — **DECIDED (PROGRAM CROSSCUT, 2026-08-29), SHIPPED (C6, 2026-08-30)**

There is a root **`LICENSE`**: explicit **all rights reserved**, with a note recording why.

**C6 found it was decided but never delivered.** Nothing copied `LICENSE` into the build output, so
every distributable this repo has ever produced carried `THIRD-PARTY-NOTICES.txt` and no statement
of its own terms — a recipient of the zip could read what raylib and Noto Mono allow and had
nothing at all telling them what *this* allows. One `<None Include="LICENSE" .../>` line fixes it;
`SIGHTLINE_SHIPTEST`'s manifest leg is what keeps it fixed (it reads `FAIL (missing:LICENSE)`
without that line, which is how the defect was found).

The decision was made on the reversibility argument F1 itself framed, not on taste. Of the three
options below, all-rights-reserved is the only one that is **one-way reversible**: it can become
MIT (or PolyForm, or anything) in a single commit at any future moment, whereas source once
published under MIT stays MIT for every copy already taken. It costs nothing and forecloses
nothing, and unlike silence it is **unambiguous to a recipient of a build**.

| Option | What it means | Good if |
|---|---|---|
| **All rights reserved** (CHOSEN) | Nobody may copy, modify or redistribute the source. Shipping compiled builds is unaffected. | The game may be sold, or the owner has not decided. Costs nothing and forecloses nothing. |
| **MIT** | Anyone may do anything, including sell it, with attribution. | The goal is portfolio visibility and maximum reuse. |
| **Source-available** (e.g. PolyForm Noncommercial, BSL) | Source is readable and forkable for non-commercial use; commercial use reserved. | The owner wants the code public but not resold. |

The `LICENSE` explicitly does **not** restrict distributing compiled builds, and explicitly does
not limit the rights the bundled third-party licences (Zlib / MIT / OFL) grant in those
components — so it cannot accidentally contradict `THIRD-PARTY-NOTICES.txt`.

**To change it later:** replace `LICENSE` wholesale and say so in `docs/DEVLOG.md`. Nothing else
in the repo keys off it.

---

## 5. Where the game keeps player data

Verified empirically (`Environment.GetFolderPath(SpecialFolder.ApplicationData)` under this
runtime), **not** the `~/.local/share` the code comment used to claim:

| OS | Directory |
|---|---|
| Linux | `$XDG_CONFIG_HOME/Sightline`, else `~/.config/Sightline` |
| Windows | `%AppData%\Sightline` |
| macOS | `~/Library/Application Support/Sightline` |

Files in it:

| File | Contents | Lifetime |
|---|---|---|
| `save.json` | the in-progress campaign run | deleted when a run ends |
| `meta.json` | the cross-run profile: salvage, veterans, achievements, unlocks, hall of fame, max heat | permanent |
| `display.json` | window size, fullscreen, brightness/gamma, colorblind mode, post-FX | permanent |
| `*.json.bak` | a file that could not be read, moved aside instead of destroyed | until overwritten |
| `*.json.tmp` | transient — every write goes to a `.tmp` then renames over the target, so a crash mid-write cannot tear a file | should not persist; a *failed* write sweeps its own, but see below |
| `*.json.selftest-stash` | a self-test moved your profile aside and was killed before putting it back. **Your data, intact** — rename it back over the original | should never persist |
| `crash-<yyyyMMdd-HHmmss>-<pid>.txt` | a crash report (P11) — the file to attach to a bug report. Newest 5 kept, 64 KB each, at most 3 per launch. See §6 | until pruned by a newer one |

**To uninstall completely**, delete the publish directory and that folder.

Two edge cases worth knowing:

* `GetFolderPath` returns `""` when the resolved config directory does not exist yet, which
  would put the save dir at a *relative* `Sightline/` next to whatever the working
  directory happened to be. `SaveGame.Dir` falls back to `$HOME/.config/Sightline` instead.
* A save that is unreadable **or** that parses but cannot produce a resumable run (`{}`,
  `null`, an empty squad) is moved to `save.json.bak` and removed, so the intro stops
  offering a CONTINUE that cannot work. The bytes are always preserved for diagnosis.

Saves are versioned (`SchemaVersion`, currently 1) and every persisted enum is guarded by a
golden fingerprint in `SIGHTLINE_SAVETEST` — appending an enum member is safe, anything
else fails the test loudly. See CLAUDE.md, "Combat model".

### C6: what the atomic-write claim above was actually worth, measured

The sentence "saves write a `.tmp` then rename" has been in this document since W5 and was
presented as a property of **the directory**. It was true of `save.json` and `meta.json` and
**false of `display.json`**, which was a single bare `File.WriteAllText` on the target — and which
is the most frequently written of the three (every volume drag, every toggle, every one-shot tip
dismissed). All three now go through the one writer, `SaveGame.WriteAtomic`.

`SIGHTLINE_SHIPTEST` proves it **by mechanism, not by inspection**: it puts a marker in the target,
holds an open read handle across a real save, and asserts the handle still sees the OLD bytes. A
rename streams into a new inode and swaps the directory entry, so the old handle keeps the old
content; a truncate-in-place writer rewrites the same inode and the handle sees the new bytes. That
difference *is* atomicity. Measured on the pre-fix tree: `SHIPTEST: FAIL
(missing:LICENSE,displayNotAtomic:handleSawNewBytes)`.

**The full-disk case, measured** (nothing had ever tested it). A 48 KB tmpfs filled to 100% with a
good `meta.json` already in it, then a live non-`NoPersist` mission 1 driven through the published
binary: **no exception, the game plays on, and the existing `meta.json` is byte-identical
afterwards.** A second pass with `SIGHTLINE_SHIPTEST` on the same full filesystem shows every write
attempt refused (`IOException` / "did not land") with the process still standing. So the game
degrades to "cannot save" rather than to "lost your profile" — which is the right failure and is
now a measurement rather than an assertion.

One real thing that full-disk pass found: on `ENOSPC` the old code path leaves a **0-byte
`<name>.json.tmp` behind forever**, contradicting the "should never persist" line in the table
above. `WriteAtomic` sweeps it **in the failure path**. Measured side by side: old shape leaves
`old.json.tmp len=0`, new shape leaves nothing. `SIGHTLINE_SHIPTEST`'s `SweepProbe` proves it by
forcing a rename to fail (it makes the target a directory) and asserting the `.tmp` is gone; remove
the sweep and the leg reads `sweep:tmpNotSwept`.

### The exact scope of the atomicity claim — read this before quoting it

**REVIEW FIX (C6, sent back).** This section's first draft said `*.json.tmp` "should never persist,
**and now does not**", which was an over-claim on precisely the case the sweep does not cover.
Three separate limits, stated plainly:

1. **The sweep is in the `catch`, so it only runs when the write itself fails.** A `kill -9`, an OOM
   kill or a power cut *between* `File.WriteAllText(tmp, …)` and `File.Move(tmp, target)` still
   leaves a `.tmp` behind, and there is **no startup sweep**. That is deliberate: a `.tmp` left by a
   real crash is the one artefact a maintainer can reason about the crash from, and deleting it at
   boot would destroy evidence to tidy a directory listing. "A crash mid-write" is the whole reason
   the rename exists, so asserting the corpse is impossible there was exactly the wrong sentence.
2. **The probe tests the INODE-SWAP property, not durability.** Holding a handle across the write
   and finding the old bytes proves a reader — or a crashed process's half-finished work — can
   never see a torn file. It does **not** prove power-loss safety: that needs an `fsync` of the tmp
   **and** of the directory before the rename, and this code does neither. So: **safe against
   process death and concurrent readers; NOT proven against power loss.**
3. **The mechanism is verified on Unix only.** `Ship.AtomicityProbe` self-skips on Windows (the file
   semantics differ and the rename path is not observable this way), while `SaveGame.WriteAtomic` —
   which `display.json` was moved onto — changed behaviour on **all** platforms. The Windows
   behaviour of that change is **unverified**, not assumed-good.

---

## 6. When it crashes: the crash report (PROGRAM PARALLAX wave P11, 2026-09-03)

C6 left this at the top of its own not-fixed list: *"No crash reporter and no log file. An
exception on a player's machine goes to a stdout nobody reads. The version stamp lets them name a
build; there is nothing to attach."* A player who double-clicked the game has no terminal at all —
the window simply vanishes, and the entire bug report you will ever receive is "it crashed".

`src/Crash.cs` closes that. `Program.Main` is now nothing but `Crash.Guard(...)` around the whole
launch, plus `Crash.Install()` for throws that unwind on a background thread or an unobserved task.

### Where the file goes, and what to ask a player for

**The same directory as their save** (§5) — resolved through `SaveGame.ConfigDir`, never a second
derivation of that path, so the empty-`ApplicationData` fallback applies here too:

| OS | Crash reports |
|---|---|
| Linux | `$XDG_CONFIG_HOME/Sightline/crash-*.txt`, else `~/.config/Sightline/crash-*.txt` |
| Windows | `%AppData%\Sightline\crash-*.txt` |
| macOS | `~/Library/Application Support/Sightline/crash-*.txt` |

Named `crash-<yyyyMMdd-HHmmss>-<pid>.txt`, UTC. **Ask a player for the newest one**, and note that
the terminal message already tells them: on a crash the game prints a headline to stderr naming
the exact path and saying to attach it.

### What it contains

`SIGHTLINE CRASH REPORT`, then four blocks, each independently guarded so a section that cannot be
read degrades to one `<unavailable: TypeName>` line instead of destroying the report:

1. **BUILD AND MACHINE** — `Ship.Version` (so a report names a build), the UTC timestamp, *where*
   in the process it died, OS description, OS/process architecture, the .NET runtime and RID, the
   base directory, the working directory, the player-data directory, and process uptime.
2. **NATIVE LIBRARY FAILURE** — only when one occurred; see below.
3. **GAME STATE** — mode, phase, objective, mission, heat, map seed, map node, mission turn, run
   turns, wave, soldiers alive/total, hostiles alive/total, the selected soldier, the animation in
   flight, and `NoPersist`. `<no game was running>` when it died before the `Game` existed.
4. **SIGHTLINE_\* ENVIRONMENT** — every `SIGHTLINE_*` variable in force. These change what the game
   *does*, so a report without them can send you hunting a bug that only exists under a harness
   pin. Nothing outside that namespace is read.
5. **EXCEPTION CHAIN** — every level to a depth of 12, with type, message, source and stack trace,
   and `AggregateException.InnerExceptions` expanded (walking only `InnerException` reports one of
   N failures and hides the rest).

It contains **no personal data**: no account details, and no paths outside the game's own
directories. The file says so at the top, because a player deciding whether to send it should not
have to take that on trust.

### The native-library case

A missing or unusable `libraylib.so` / `raylib.dll` is not a bug report a player can write. The
report and the terminal headline both LEAD with plain English naming the file, whether it is
present beside the executable, and every directory the loader searched
(`AppContext.BaseDirectory`, `NATIVE_DLL_SEARCH_DIRECTORIES`, and `LD_LIBRARY_PATH` /
`DYLD_LIBRARY_PATH` / `PATH`). Three shapes are recognised:

| Exception | Meaning | What the report says |
|---|---|---|
| `DllNotFoundException` | the library is not there | "needs the file `libraylib.so` in the SAME FOLDER as the game program" + where it looked |
| `BadImageFormatException` | wrong architecture | "built for a different kind of processor", with this process's arch |
| `EntryPointNotFoundException` | wrong raylib version | "a function this build needs is missing — a different raylib on this machine is being picked up" |

**Measured, not asserted.** Deleting every `libraylib.so` from a build output and launching it
produces `EXIT=70`, the plain-English headline above, and a complete report — reproduced verbatim
in `docs/DEVLOG.md` §THE CRASH FILE. Note the message text: raylib-cs installs its own
`DllImportResolver` and throws `DllNotFoundException("Failed to load raylib.")`, which carries no
quoted library name at all — hence the fallback in `Crash.LibNameFrom`.

> **WHAT IS NOT COVERED, SAID PLAINLY.** This catches **managed exceptions**. A raylib ABI
> mismatch that faults *inside native code* — a SIGSEGV in `libraylib.so` itself — kills the
> process without unwinding, and no managed handler anywhere runs: no file, no message, no exit
> code but the signal. That is a real hole and it cannot be closed from managed code; closing it
> would mean a native signal handler or an out-of-process supervisor, neither of which is in scope
> here. `EntryPointNotFoundException` is the *catchable* corner of ABI mismatch (a function that is
> simply absent), and it is covered. Nothing else is.
>
> Two smaller uncovered cases, for completeness: `StackOverflowException` and `Environment.FailFast`
> are both uncatchable by design in .NET.

### The four properties, and why each is a property rather than a hope

| Property | Mechanism | How it is proven |
|---|---|---|
| **Atomic** | writes through `SaveGame.WriteAtomic` — C6's one writer, `.tmp` + rename | `SIGHTLINE_CRASHTEST` leg (b) holds an open read handle across the write and asserts it still sees the OLD bytes (the inode-swap probe from §5). Unix-only mechanism; self-skips off Unix like `Ship.AtomicityProbe` |
| **Never throws** | every section composed in its own try/catch; a final backstop `catch` | leg (f) throws an exception whose `Message`, `StackTrace` *and* `ToString` all throw, and asserts a report is still written with `<unavailable: …>` in it |
| **Bounded** | newest `Crash.MaxReports` (5) files kept; `Crash.MaxReportBytes` (64 KB) per file, truncated with a marker; `Crash.MaxPerProcess` (3) per launch | legs (c), (d), (i) |
| **Degrades** | an unwritable directory ⇒ `LastPath` null, the WHOLE report to stderr, exit 70 | leg (e) makes the crash directory impossible (a regular file where a directory must be) |

Exit code on a crash is **70** (`EX_SOFTWARE`), not an unhandled-exception abort.

`SIGHTLINE_CRASHTEST` runs in `scripts/qa-sweep.sh` **and** in `scripts/publish.sh` against the
published binary — where trimming, single-file packing and `AppContext.BaseDirectory` all behave
differently from the source tree, which is the C6 lesson applied. It writes only to a temp
directory: it redirects `Crash.DirOverride` rather than stashing the real profile (nothing to fail
to put back), and leg (g) diffs the real player-data directory before and after to prove it.

---

## 7. The Windows console window — half measured, half declared open

**The defect (C6, left open).** The published Windows executable was built as a *console*-subsystem
binary, so a Windows player double-clicking the game got a black console window sitting behind it
for the whole session.

**Fixed and MEASURED.** `Sightline.csproj` sets `OutputType=WinExe` for `win-x64` / `win-x86` /
`win-arm64` RIDs only. This is verifiable from Linux without running anything, because the defect
is one field in the file — the PE optional header's `Subsystem` word:

| | `Subsystem` | meaning |
|---|---|---|
| before | **3** | `IMAGE_SUBSYSTEM_WINDOWS_CUI` — the OS gives the process a console window |
| after | **2** | `IMAGE_SUBSYSTEM_WINDOWS_GUI` — it does not |

Both numbers were read off real `dist/win-x64-release/Sightline.exe` files cross-published from
this container. `scripts/publish.sh` now reads that byte on **every** win-RID publish and **fails
the publish** if it is not 2, so the fix cannot silently regress.

**The cost, and the half that is NOT verified.** A `WinExe` has no console at all, so on Windows
`Console.WriteLine` goes nowhere — and this project's entire verification story is stdout: every
`SIGHTLINE_*TEST` prints PASS/FAIL to it and `qa-sweep.sh` greps for those lines. Making the flag
unconditional would have silenced the harness on Windows. Two things keep both:

1. The flag is scoped to Windows RIDs. The Linux build, the Debug build everyone develops against
   and the whole headless harness set no RID, so they are byte-for-byte unaffected — `OutputType`
   stays `Exe`.
2. `Crash.AttachWindowsConsole()` runs first in `Main` and, on Windows only, calls
   `AttachConsole(ATTACH_PARENT_PROCESS)` so a game launched from `cmd.exe` or PowerShell prints
   into that window; if there is no parent console but a `SIGHTLINE_*` variable is set, it calls
   `AllocConsole()`. A player double-clicking from Explorer has neither, so they get no window —
   which is the entire point. It is a no-op off Windows (first line is the guard).

> **DECLARED OPEN — item 2 IS NOT OBSERVED.** Nothing in this sandbox can execute a Windows binary.
> `AttachWindowsConsole` is written from the documented `AttachConsole(ATTACH_PARENT_PROCESS)`
> contract, not from a measurement, and no claim is made that the harness prints on Windows. **The
> exact command a Windows machine should run to close this**, from the published directory, in
> `cmd.exe`:
>
> ```
> set SIGHTLINE_SAVETEST=1 && Sightline.exe
> ```
>
> **PASS** = a `SAVETEST: PASS` line appears in that same console window. **FAIL** = the command
> returns with no output at all, in which case the attach did not work and the honest options are
> (a) a separate console-subsystem `Sightline-harness.exe` publish profile for the harness only, or
> (b) accepting that the harness is Linux-only, which is what it is in practice today. Do not tick
> this item off any list until somebody has seen that line on Windows.
>
> Also still open from C6 and untouched here: the Windows build remains **unverified beyond its
> file list** (nothing here can run it), the binary is **unsigned** so SmartScreen will warn (code
> signing costs money and is permanently out of scope under this project's rules), and **macOS has
> never been cross-published**.

---

## 8. Cutting a release (PROGRAM PARALLAX wave P17, 2026-09-04)

C6 made the build a thing you can hand someone. It still was not a thing you could **give**
someone: `dist/linux-x64-release/` is a directory, and a directory cannot be attached to a
message, downloaded, checked for damage, or told apart from the one you built last week. There
was no tag, no changelog, no archive and no checksum. This section is the contract for all four.

### 8.1 The one command

```bash
bash scripts/publish.sh          # build + verify + CHANGELOG + archive + checksum + re-verify
bash scripts/publish.sh --tag    # ...and create the LOCAL annotated tag as well
```

It produces, beside the payload directory:

| File | What it is |
|---|---|
| `dist/SIGHTLINE-v<version>-<rid>[-<mode>].tar.gz` | the archive (`.zip` on a `win-*` RID). Unpacks into ONE directory of the same name — never into the recipient's cwd |
| `dist/SIGHTLINE-v<version>-<rid>[-<mode>].tar.gz.sha256` | the checksum, in `sha256sum(1)` format with a **bare** filename so `sha256sum -c` works where it was downloaded |
| `dist/CHANGELOG.md` | a copy, so the release directory is readable without unpacking |
| `<payload>/CHANGELOG.md` | the same file inside the archive, beside the game |

**The version in the name is the one the BINARY reports.** It is scraped from the `SHIPTEST` line
the script already prints (`build stamped v1.0.0`), not read out of `Sightline.csproj` a second
time, so an archive name and the build inside it cannot disagree. If that scrape fails the script
falls back to the csproj **and says so on stderr**.

Measured on this container (base `e57e151` + this wave, linux-x64, default `release` mode):

```
>> packing SIGHTLINE-v1.0.0-linux-x64.tar.gz
   13M  (13300463 bytes)
   4e697027c44c5d4e880029350826d9ea3d2ea626715014a8c8b7894c1a4cc3d7  SIGHTLINE-v1.0.0-linux-x64.tar.gz
```

**A digest names ONE artifact, not "the v1.0.0 archive" — the archive is NOT bit-reproducible.**
Measured, not assumed: **three publishes of the same commit within an hour produced 13,298,368 /
13,300,476 / 13,300,463 bytes and three different digests**, because `dotnet publish` does not emit
a byte-identical binary run to run. `tar` is invoked with `--owner=0 --group=0 --numeric-owner
--sort=name` so the *packing* adds no variance and no container uid leaks into a distributable —
but the checksum exists so a recipient can verify **the file you sent them**, not so two people can
rebuild independently and compare. **Do not quote the digest above as the digest of v1.0.0**; read
the one your own publish printed. Open item in `docs/ROADMAP.md`.

### 8.2 How a recipient verifies the download

From the directory holding both files:

```bash
sha256sum -c SIGHTLINE-v1.0.0-linux-x64.tar.gz.sha256
# -> SIGHTLINE-v1.0.0-linux-x64.tar.gz: OK
tar -xzf SIGHTLINE-v1.0.0-linux-x64.tar.gz
./SIGHTLINE-v1.0.0-linux-x64/Sightline
```

**Measured end to end (P17, linux-x64):**

```
$ sha256sum -c SIGHTLINE-v1.0.0-linux-x64.tar.gz.sha256
SIGHTLINE-v1.0.0-linux-x64.tar.gz: OK
$ tar -xzf … -C /tmp/unpack && ls /tmp/unpack
SIGHTLINE-v1.0.0-linux-x64          <- ONE directory, not eleven loose files
$ cd /tmp/unpack/SIGHTLINE-v1.0.0-linux-x64 && SIGHTLINE_SAVETEST=1 ./Sightline
SAVETEST: PASS                       <- runs from a directory nobody chose (the F1 lesson)
```

A `--rid win-x64` publish was also cut and verified as far as this container can: 11 files,
`Sightline.exe` + `raylib.dll`, PE subsystem **2** (`WINDOWS_GUI` — no console window, §7),
`SIGHTLINE-v1.0.0-win-x64.zip` 12,624,409 B, `sha256sum -c` **OK**. On Windows, PowerShell's
`Get-FileHash -Algorithm SHA256` prints the same digest (uppercase). **Unverified from here:**
nothing in this sandbox can *execute* a Windows binary, so the Windows unpack-and-run path is
checked file-by-file, not measured — see §7.

### 8.3 Where the changelog comes from, and why it is derived

`scripts/changelog.sh` generates it from **`git log --first-parent`**. It is regenerated on every
publish and never committed.

**Why derived rather than written.** This repository has been burned by hand-maintained registries
repeatedly and expensively: the sweep's self-test count was wrong six times, `CLAUDE.md`'s
"free keys" line advertised four letters that were already bound, `Ship.RequiredFiles` shipped
listing six files while the document it guards said ten, and a `Heat.Mods` row declared a tooth
that could never fire for two whole programs. A hand-written changelog is the same object, and it
rots *worse*, because nobody re-reads a changelog to check it against anything.

**Why first-parent is the right granularity.** This project lands work as one merge per wave or
milestone, and those subjects already read like release notes — *"Merge wave C3 THE TWO GAMES — the
objective-class gap, and the gate that never was"*. The first-parent walk is therefore the
changelog the repository is already maintaining for free: **71 entries for 676 commits.** Every
commit would be a wall nobody reads; a curated list would be a list that drifts.

Sections are cut at `v*` tags, newest first. With no tag yet, the whole history goes under the
version being published — correct for a first release. Once `v1.0.0` exists, the next publish
emits only what landed after it under the new heading and leaves the earlier sections alone.

> **HONEST SCOPE.** It reports what was **merged**, not what a player will **notice**. A wave that
> only moved measurements gets a line exactly like a wave that added a mode. Fixing that would mean
> curating, which is the thing this script exists not to be.

If the tree has no git history (publishing from an export), the file is still written, still has
the right shape, and **says in its own body** that the history was unavailable — a reader must be
able to tell that apart from "nothing changed".

### 8.4 The tag — and why the script will not push

A release needs a commit you can go back to. `--tag` creates a **local annotated tag** `v<version>`
and nothing else. It **never contacts a remote**, and it refuses rather than tagging the wrong
thing:

* not a git repository → refuse;
* **working tree dirty** → refuse, and print `git status --short`. A release tag must name the tree
  that was actually built and verified;
* the tag already exists → refuse, and print the commit it is on. Bump `<Version>` in
  `Sightline.csproj` instead.

Publishing the tag is a separate, deliberate, human act:

```bash
git push origin v1.0.0
```

A publish run without `--tag` prints that pair of commands at the end, so the option is never
invisible. **P17 did not create the tag**: `v1.0.0` does not exist in this repository yet, and this
document does not claim it does.

### 8.5 What the self-test actually checks

`SIGHTLINE_SHIPTEST` gained three legs, all gated on `SIGHTLINE_RELEASEDIR` (which
`scripts/publish.sh` sets when it re-runs the hook from inside the published payload). With the
variable unset — `qa-sweep.sh`, or running the hook by hand — they **SKIP and say so in the PASS
line** (`RELEASE LEGS SKIPPED: no SIGHTLINE_RELEASEDIR`), the `_secondLaunchSkip` pattern. A leg
that silently stops running is a lie.

| Leg | Assertion | Reads |
|---|---|---|
| (9) ARCHIVE | an archive named `SIGHTLINE-v<the version this binary reports>-*` exists in the release directory and is non-empty | `releaseNoArchive` / `releaseArchiveEmpty` |
| (10) CHECKSUM | a `.sha256` sits beside it, is `<64 hex>  <bare basename>`, and **the digest is recomputed in-process and compared** — presence is not the assertion, so a stale checksum beside a rebuilt archive fails | `releaseNoChecksum` / `releaseChecksumShape` / `releaseChecksumMismatch` |
| (11) CHANGELOG | `CHANGELOG.md` resolves next to the binary, is titled, carries a `## v<version>` section for **this** build, and has entries | `changelogMissing` / `changelogEmpty` / `changelogTitle` / `changelogNoVersionSection` / `changelogNoEntries` |

Two more legs landed with them and run **everywhere**, because they assert pure functions:

| Leg | Assertion |
|---|---|
| (7) ICON | the generated window-icon pixels: 64×64, fully opaque, **only** `Pal.Bg` / `Pal.BoardEdge` / `Pal.Friend` / `Pal.Foe`, four-fold mirror symmetric, the danger accent **reserved to the core** (§3.H's semantic colour table), mark coverage 8–40%, and a ≥3× luminance step from field to core (the squint test). Deliberately **not** a golden hash: a hash fails on any redesign and says nothing about whether the redesign is legible |
| (8) WINDOW FIT | `Display.FitLaunchSize(monW, monH)` over eleven monitors incl. 1366×768, 1280×800, 1024×768, 0×0 and −1×−1: never enlarges, always fits inside `(monW, monH − LaunchChromeH)`, holds 16:10 within 2%, is monotone in monitor height, and `Display.AllowLaunchFit` is **false** in the harness. Plus (8b): `WinW`/`WinH` round-trip through a real `display.json`, and a hand-edited absurd value reads as *unset* rather than as a 0-wide window |

**Cross-published RIDs.** The release legs only run on a RID this machine can execute. A
`--rid win-x64` publish still writes and `sha256sum -c`-verifies its `.zip` and checksum, but the
binary does not judge them. That is stated rather than implied.

### 8.6 The window icon, and the first-launch window size

Two product defects P17 closed that are not about the archive:

**The window icon.** There was none — the taskbar, alt-tab strip and title bar showed GLFW's blank
default. `Ship.IconPixels()` generates it in engine (`Raylib.GenImageColor` + `ImageDrawPixel` →
`Raylib.SetWindowIcon`) from the same palette and the same axis-aligned rectangles the game draws
with: the game's own aim reticle — four brackets in `Pal.Friend` around a `Pal.Foe` core, on
`Pal.Bg` inside a `Pal.BoardEdge` frame. **No committed binary, so no `CREDITS.txt` entry, no
`THIRD-PARTY-NOTICES.txt` entry, no cost and no licence risk** — which is the main reason to prefer
procedural here and not only an aesthetic one. `SIGHTLINE_ICONSHOT=1` writes it out as
`sightline_icon.png` (64×64) plus a 256×256 nearest-neighbour blow-up so a human can look at it.

> **UNVERIFIED, SAID PLAINLY.** `Raylib.SetWindowIcon` is called on the real launch path and does
> not throw. Whether a desktop *shows* it cannot be observed from here — Xvfb has no window
> manager — and **GLFW ignores window icons on Wayland entirely, by design**. So: the pixels are
> asserted, the call is made, the display is not measured. `ApplyWindowIcon` swallows any exception
> because a cosmetic icon must never be able to stop a launch.

**The first-launch window size.** `Display.Sizes[0]` is 1280×800 and the launch never asked the
monitor whether that fits. On a **1366×768** laptop the window is 32 px taller than the screen, so
the action bar — the row carrying every verb the player needs — is under the bottom edge on the
**first frame of the first launch**, before the player knows any settings screen exists. That is
the worst class of defect this project can ship.

`Display.FitLaunchSize` is a **pure** function of the monitor, and it only ever **shrinks**: on any
monitor where the authored size fits inside `(monW, monH − LaunchChromeH = 64)` first launch is
byte-for-byte what it always was. Otherwise it scales 1280×800 down by the tighter ratio, holding
16:10 (so the letterbox is empty and nothing is cropped) and rounding down to even pixels. This is
cheap because `Display` already renders the game into a virtual 1280×800 render target and
letterbox-scales it — every layout constant, hit-test and self-test still works in 1280×800 space.
The result is written to `display.json` (`WinW`/`WinH`, additive; absent = 0 = the pre-P17
behaviour), so it is decided **once** and not re-derived every boot.

Measured live under Xvfb, first launch against an empty profile:

| monitor | `display.json` | note |
|---|---|---|
| 1366×768 | `WinW 1126, WinH 704` | the motivating case; 704 ≤ 768 − 64 |
| 1280×800 | `WinW 1176, WinH 736` | a window exactly as tall as the screen is still clipped by a title bar |
| 1920×1080 | *nothing written* | the authored size fits — the fit changes nothing and writes nothing |

Relaunching on the same monitor keeps 1126×704; moving that profile to a 1920×1080 screen **also**
keeps 1126×704 (the fit is a first-launch decision, not a per-boot one — the settings ladder is how
you change it, and cycling window size retires the fit).

> **HOW THE HARNESS IS KEPT FIXED.** `Display.AllowLaunchFit` defaults to **false**. The single
> caller that sets it true is the real game launch in `Program.RealMain`, and even there only when
> neither `SIGHTLINE_SHOT` nor `SIGHTLINE_AUTOPLAY` is set. Every headless path — the screenshot
> hook, autoplay, `SIGHTLINE_PAIRTEST`'s byte-identity determinism gate, the balance flywheel, and
> every `*TEST` that calls `Display.Init(true)` for a real render target — leaves it false and gets
> exactly `Cfg.ScreenW × Cfg.ScreenH` from `InitWindow`, as before. **Window size in this sandbox
> is therefore not a function of anything that varies**, and SHIPTEST leg (8) asserts the flag is
> off while it runs.

### 8.7 The version, in-game

**This was already done and the research docket was stale.** C6 painted `Ship.VersionLabel` in two
places a player looks: the main menu's bottom footer
(`GEOMETRY · PARTICLES · NO QUARTER   ·   SIGHTLINE v1.0.0`, `Hud.IntroFooter`) and the pause
card's top-right corner, where somebody about to file a bug report is already looking. P17 verified
it by screenshot rather than by reading the code, and added one guard: the footer string is held to
a **72-character budget**, because it is *centred* and so overflows both edges at once. That is a
character budget and not a measurement — `LoadGameFonts()` does not run in the SHIPTEST hook, so
`Cfg.Measure` there would report the raylib fallback face. `SIGHTLINE_FITTEST` measures for real.
