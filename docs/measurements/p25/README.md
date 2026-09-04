# P25 "NOBODY HAS LOOKED" — the pillar-2 instrument and its baseline

**Base commit `bea240c`** (main, PROGRAM PARALLAX milestone 18). Branch `wave/the-feel`.
Everything here is produced by one hook, `SIGHTLINE_JUICETEST`, which is in
`scripts/qa-sweep.sh` and routed through `verdict` (so it moves the sweep's exit code).

## The commands

```bash
export PATH="$PATH:/usr/lib/dotnet"
export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
cd /path/to/worktree && mkdir -p "$PWD/.xdg"
export XDG_CONFIG_HOME="$PWD/.xdg" SIGHTLINE_BALANCE_JSON="$PWD/balance.json"

SIGHTLINE_JUICETEST=1 xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug
bash scripts/qa-sweep.sh --full
```

Runtime: **2.7 s** (Debug, under xvfb, this container). Deterministic — two consecutive
runs are byte-identical (`diff` of two captures, 0 lines). Every row reseeds
(`Util.Reseed`), gets a **fresh `Game`**, and resets the static per-mission bark budget
(`Voice.BeginMission`), because all three leak across rows otherwise.

## The files

| file | what it is |
|---|---|
| `baseline-prefix.txt` | the measured state of the tree **before** P25's two fixes. Verdict line: `FAIL (missOutPunchesGraze(shake:2.50>2.00),blastNoBloom(grenade))`. Produced on an earlier revision of the instrument (before `Moved`/`Words`), so its `seen` column runs 1 higher on anim-driven rows and it has no `beat` block — the **weight columns, which are what the two defects are about, are identical**. |
| `baseline-postfix.txt` | the shipped state, on the final instrument. Verdict: `PASS`. |
| `red-demonstrations.txt` | **22 deliberate one-edit breaks**, each built, run and reverted, proving every assertion in the hook can go red. B1 and B2 are the two shipped fixes reverted — they are the *before*. |
| `sweep-full.txt` | `bash scripts/qa-sweep.sh --full`, 87/87 hooks, `SWEEP-EXIT-CODE=0`, no COVERAGE GAP. (Named without the `qa` prefix on purpose: `.gitignore`'s bare `qa*.txt` matches at ANY depth and would silently swallow it — the same trap the `run_chunk.sh` comments in that file describe.) |

## What the instrument measures

The **feedback footprint** of an event, on the real code paths, driven through the real
anim pump, with the real `Audio.Spy` cue log. Four channel classes:

* **SEEN** — particles, floating words, rings, streaks, lights, reticles, a figure's drawn
  position actually changing, and body FX (flash / flinch / recoil).
* **HEARD** — cue ids that reached `Audio.Play`, recorded device-free.
* **FELT** — screen shake, hit-stop, zoom-punch, bloom.
* **READ** — lines added to the always-on combat log.
* plus **ACK** — the frame index, at 1/60, on which the first channel moved.

Four legs: **(a)** the six shot outcomes as a proportionality ladder; **(b)** a census over
`Hud.VerbTable` (the action bar's own list, so a new verb joins the census or fails it),
16 bar verbs expanded to 23 rows because ABILITY and UTILITY ITEM are class-derived, plus
MOVE; **(c)** one unit taking damage by nine routes, non-lethal and lethal; **(d)** a
frame-by-frame trace of the shot's three-beat.

## What it CANNOT see — read this before quoting any number from it

* **It cannot hear.** It records that a cue id fired, with its pan. Whether the sound is
  right, pleasant, audible over the bed, or distinguishable from its neighbours is outside
  it. (`AUDIOTEST`/`AUDIOGATE` measure the rendered buffer; nobody has listened.)
* **It cannot see.** It counts what was pushed into `Fx`; it does not render. A channel can
  fire and be invisible — occluded, off-screen, alpha 0, behind the HUD. `FEELTEST` leg (c2)
  found exactly that class of defect. Concretely: the **twelve modal-verb ARM rows read
  `seen 0`** and that number is meaningless — an arm's real feedback is a renderer overlay
  driven off `AimMode`/`GrenadeMode`/..., which this probe never draws. Those rows are
  reported, never asserted.
* **It cannot judge proportion perceptually.** `9 > 5` is an ordering, not a perception.
  Two rungs this table separates may be indistinguishable to a player.
* **It cannot see timing jitter.** Synthetic 1/60 dt, no GPU. A hitch on a real machine is
  invisible here.
* **It cannot measure fun**, pacing across a session, or whether any of this is satisfying.
  It measures the code path's response, not a player's experience of it.

It is a **starvation-and-proportionality instrument**. That is all it is.
