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
```

The script publishes, then **re-proves the artifact against the binary it just built** by running
`SIGHTLINE_SAVETEST`, `SIGHTLINE_METATEST` and (C6) `SIGHTLINE_SHIPTEST` on it, and fails the
publish if any of the three does not say PASS. The first two exist because of §3; the third exists
because the source tree can resolve a bundled file the build output is missing (see below).

**Do not publish with a bare `dotnet publish`.** It is not that the flags are wrong — the script
just passes them — it is that a bare publish skips all three verifications, which is precisely how
a totally broken build once looked green. C6 additionally made the two §3 mitigations a **build
error to remove** (`C6GuardTrimmedPersistence` in `Sightline.csproj`): a trimmed publish with
`JsonSerializerIsReflectionEnabledByDefault` off or `TrimmerRootAssembly` empty now fails to build,
with the reason and a pointer to §3. Verified by running exactly that command.

### What ships

`dist/<rid>-<mode>/` contains **ten files**, and you must ship **all** of them:

| File | Why |
|---|---|
| `Sightline` (`Sightline.exe`) | the game (self-contained: no .NET install needed on the target machine) |
| `libraylib.so` (`raylib.dll`) | raylib is a native library the runtime `dlopen()`s — it cannot be linked into the single file |
| `assets/NotoMono-Regular.ttf`, `assets/ChakraPetch-Bold.ttf` | the two baked font faces; without them the game boots and silently falls back to raylib's bitmap font |
| `assets/NotoMono-LICENSE.txt`, `assets/ChakraPetch-LICENSE.txt` | OFL obligation: the licence text ships beside the font it covers |
| `assets/sfx/CREDITS.txt`, `assets/music/CREDITS.txt` | the drop-in audio provenance ledger (and the directories the file-first loader looks in) |
| `THIRD-PARTY-NOTICES.txt` | licence obligation (§4) — not optional |
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
copy list. Those two disagreeing is exactly what the manifest leg exists to name.

---

## 2. The publish matrix (re-measured, C6, 2026-08-30)

`start` = wall time for one window-free `SIGHTLINE_SAVETEST` launch. It measures startup + JIT,
not frame time. **This round was measured INTERLEAVED** — one launch of each mode per round, nine
rounds — so all four modes see the same container load. That matters: this box is shared with five
other agents running balance batches, and a *sequential* pass taken twenty minutes apart put
`no-trim` at 117 ms and then 184 ms. Read the ORDERING as the result and the absolute numbers as
"on a loaded four-core container".

| mode | flags | executable | whole directory | files | start (median of 9, min–max) |
|---|---|---|---|---|---|
| **release** (default) | `PublishTrimmed` + `PublishReadyToRun` + `PublishSingleFile` | **25.6 MB** | **28.4 MB** | 10 | **169 ms** (102–323) |
| small | `PublishTrimmed` + `PublishSingleFile` | 15.8 MB | 18.5 MB | 10 | 545 ms (397–696) |
| no-trim | `PublishReadyToRun` + `PublishSingleFile` | 82.3 MB | 85.1 MB | 10 | 206 ms (144–288) |
| plain | `PublishSingleFile` | 68.3 MB | 71.0 MB | 10 | 317 ms (256–405) |
| win-x64 release | cross-published from Linux | 24.3 MB (`.exe`) | 26.4 MB | 10 | not runnable here |

Trimming is what makes `small` slow: it strips the framework's precompiled ReadyToRun code, so
everything JITs at startup. Adding ReadyToRun back costs ~10 MB and produces the **fastest** start
of any configuration — hence the default is both. On a quiet box the default measured **108–114 ms**
median-of-10.

**What moved since F1's table**, and why: the file count went 6 -> 10 (the Chakra Petch pair and
the two `CREDITS.txt` ledgers were added by later waves; `LICENSE` by C6), `small`'s executable
went 18 -> 15.8 MB, and F1's `size` column was the EXECUTABLE while the row now carries both that
and the whole shippable directory — the number you actually hand someone.

**`--rid win-x64` cross-publishes cleanly from Linux** (verified C6: 10 files, `Sightline.exe` +
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
| `*.json.tmp` | transient — every write goes to a `.tmp` then renames over the target, so a crash mid-write cannot tear a file | should never persist, and now does not |

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
above. `WriteAtomic` sweeps it in the failure path. Measured side by side: old shape leaves
`old.json.tmp len=0`, new shape leaves nothing.

**`kill -9` mid-write is NOT directly tested** — the write window is sub-millisecond and racing it
from a shell is not a test, it is a coin flip. What is tested is the property that makes the
outcome safe (the rename), on all three files. Stated as a limit rather than dressed up as a pass.
