# DISTRIBUTION — shipping a SIGHTLINE build

Everything here was measured on this repo (linux-x64, self-contained, .NET SDK 8.0.130)
during PROGRAM RESONANCE wave F1. Commands are hand-run; **nothing in this document is
wired to CI, and nothing here may be** (see CLAUDE.md, hard constraints).

---

## 1. How to publish

```bash
bash scripts/publish.sh            # recommended build -> dist/linux-x64-release/
bash scripts/publish.sh --small    # smallest binary
bash scripts/publish.sh --no-trim  # fallback if trimming ever becomes unsafe again
bash scripts/publish.sh --rid win-x64
```

The script publishes, then **re-proves persistence against the binary it just built** by
running `SIGHTLINE_SAVETEST` and `SIGHTLINE_METATEST` on it, and fails the publish if
either does not say PASS. That check exists because of §3.

### What ships

`dist/<rid>-<mode>/` contains, and you must ship **all** of it:

| File | Why |
|---|---|
| `Sightline` | the game (self-contained: no .NET install needed on the target machine) |
| `libraylib.so` | raylib is a native library the runtime `dlopen()`s — it cannot be linked into the single file |
| `assets/` | the font, its OFL licence, and any dropped-in audio |
| `THIRD-PARTY-NOTICES.txt` | licence obligation (§4) — not optional |

Assets are resolved against **the executable's own directory**, not the process working
directory, so the game runs correctly from a shortcut or a launcher. (Verified: a full
autoplay campaign launched from `/tmp` loads the font and reaches `RESULT: WIN mission=6`.
Before the F1 fix it printed `FILEIO: [assets/NotoMono-Regular.ttf] Failed to open file`
and silently fell back to raylib's built-in font.)

---

## 2. The publish matrix (measured)

`start` = wall time for one window-free `SIGHTLINE_SAVETEST` launch, median of 10 after a
warm run. It measures startup + JIT, not frame time.

| mode | flags | size | start | files |
|---|---|---|---|---|
| **release** (default) | `PublishTrimmed` + `PublishReadyToRun` + `PublishSingleFile` | **25 MB** | **95 ms** | 6 |
| small | `PublishTrimmed` + `PublishSingleFile` | 18 MB | 350 ms | 6 |
| no-trim | `PublishReadyToRun` + `PublishSingleFile` | 80 MB | 115 ms | 6 |
| plain | `PublishSingleFile` | 68 MB | 168 ms | 6 |
| — | folder (no single file) | 75 MB | — | 193 |

Trimming is what makes `small` slow: it strips the framework's precompiled ReadyToRun
code, so everything JITs at startup. Adding ReadyToRun back costs 7 MB and produces the
**fastest** start of any configuration — hence the default is both.

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

The fix has three parts, all of which must stay in place:

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

### The repo's own licence — **DECIDED (PROGRAM CROSSCUT, 2026-08-29)**

There is now a root **`LICENSE`**: explicit **all rights reserved**, with a note recording why.

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
| `*.json.tmp` | transient — saves write a `.tmp` then rename over the target, so a crash mid-write cannot tear a save | should never persist |

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
