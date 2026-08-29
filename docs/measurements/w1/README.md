# WAVE W1 "TRUE INSTRUMENT" — archived measurement artifacts

Every chunk's JSON summary, its report extract and the raw log, as run. The write-up in
[`docs/DEVLOG.md` §RESONANCE W1](../../DEVLOG.md) quotes only numbers that appear here.

**Base commit: `d350416`** (RESONANCE milestone 2). The wave lands as four commits:

| commit | what | comparable with the archive? |
|---|---|---|
| `d336016` W1/1 | batch loops stop drawing; a display-less batch refuses | **yes** — identical on every key |
| `7b9878d` W1/2 | additive read-only instrumentation | **yes** — identical on every key |
| **`5ae9149` W1/3** | **Fx, `Anim`'s miss-scatter and `Unit()`'s bob phase severed from the gameplay RNG** | **NO — this commit invalidates every archived CRN world** |
| `e264335` W1/4 | autoplay stops drawing; the frame cap derived from a measured max | (after the break) |

Commit hashes changed once after the W1 review: the missed `Unit()` bob draw was folded into
W1/3 rather than shipped separately, because `new Unit{...}` is the gameplay spawn path and a
separate commit would have been a **second** CRN invalidation. `d336016` and `7b9878d` are
unchanged; W1/3 and W1/4 were re-authored on top of them.

"inertness" means **identical on every key once the `harness{}` block is excluded** — that block
records nproc/loadavg/elapsed and is designed to vary, which is why `inert_diff.sh` strips it.

## Slot bases

**`SIGHTLINE_BALANCE_BASE=100`** for the inertness chunks (`G1-*`) — bases 0–39 belong to the
dev-instrument agent's in-flight L1 ladder and 50–99 were already spent by another wave.

The two `*-h0-b0` chunks deliberately use **base 0**, because their only purpose is to reproduce
X2's archived base-0 chunk on the same slot seeds. They are reproduction diagnostics and are not
a contribution to any ladder.

## How every chunk was run

```bash
BIN=runbin/<tag> bash docs/measurements/w1/run_chunk.sh <tag> <heat> <base> 10
```

`run_chunk.sh` pins `XDG_CONFIG_HOME` and `SIGHTLINE_BALANCE_JSON` per chunk (several agents
share this container), asserts the JSON's **own `runs` field** and prints OK/BAD. Every chunk
here printed `OK … runs=20`. Binaries are Release **snapshots** under `runbin/<tag>/`
(gitignored) so the tree can keep building while a chunk is in flight.

## The chunks

| tag | binary | what it is | batch wall-time |
|---|---|---|---|
| `G1-pre` | `runbin/W1pre` (base `d350416`) | the pre-wave instrument | **509.0 s** |
| `G1-c1` | `runbin/W1c1` (W1/1) | after the batch stopped drawing | **5.3 s** |
| `G1-c2` | `runbin/W1c2` (W1/2) | after the instrumentation was added | **4.2 s** |
| `R0diag-h0-b0` | `runbin/W1c2` (W1/2) | reproduces `x2/S1-h0-b0` — **10/10 slots** | 5 s |
| `W1post-h0-b0` | `runbin/W1post` (W1/4) | the same slots after the break — **3/10 slots** | 6 s |

**Container load: partly UNKNOWN, and the wall-clock column is therefore not a controlled
measurement.** `nproc=4` throughout. The three chunks that carry a `harness{}` block record
`loadAtStart` **8.55** (`G1-c2`), **13.00** (`R0diag`) and **27.80** (`W1post`); `G1-pre` and
`G1-c1` predate the block and have **no load record at all** — and those are precisely the two
rows the 509 s → 5.3 s comparison rests on. An earlier version of this README asserted "loadavg
20–30 for all of them", which its own instrument contradicts. Treat the table above as the raw
wall-clock of the chunks as they ran, not as an A/B.

**The controlled speed-up is `autoplay_ab.txt`: 33×** — one binary, one seed, identical frame
counts, only the GL work removed. That is the only timing figure quoted elsewhere in the docs.

## The gates, as scripts

| script | gate |
|---|---|
| `inert_diff.sh <a> <b> [keys…]` | GATE 1 — normalise two balance JSONs and diff; empty = inert |
| `gate2.sh` | GATE 2 — no display ⇒ non-zero exit, named message, target JSON's bytes AND mtime untouched |
| `gate3.sh` | GATE 3 — `RNGFRAMETEST` FAILs under `SIGHTLINE_FXRNG=0`, PASSes by default; plus the raw one-seed/two-anim-speeds reproduction |
| `gate4.py <a> <b>` | GATE 4 — per-slot CRN record comparison between two chunks |
| `framecount.sh [N]` | the frame-cap derivation — N fresh autoplays, median/p90/max |
| `gate6.sh` | the STALE-FILE trap the refusal path created, and `run_chunk.sh` refusing it |
| `hook.sh <bindir> VAR=val …` | run one env-gated hook with the house isolation exports |
| `sweep.sh [--full]` | `scripts/qa-sweep.sh` with the house isolation exports (it inherits, never sets, them) |

`framecount.txt` is the raw 30-run frame-count dump the cap was derived from. The cap is **3× the
observed MAXIMUM (13589)**, not "3× the p99": at n=30 the top two samples are 13589 and 13407, so
a "p99" is an interpolation between the two largest observations and carries nothing the max does
not. An earlier version of this wave called it `autoP99`; the W1 review was right about that.

## The completion check changed with W1 — read this before copying a chunk script

W1's no-display refusal deliberately leaves the target JSON **byte- and mtime-identical**. That is
right for the data and it BREAKS the X2-era assertion: `json.load(out)['runs'] == 2N` now reads the
PREVIOUS chunk's `runs` and prints OK for a batch that measured nothing (pre-W1 the same check
worked only because the file got clobbered with `runs=0`). `run_chunk.sh` therefore does three
things in order, and `gate6.sh` demonstrates both the trap and the refusal:

1. **`rm -f` the target first** — mandatory, not tidiness; it is what makes a missing file mean
   "no data".
2. **check the process EXIT CODE** — 2 means "no display, nothing written", and it is the only
   signal a stale file cannot fake.
3. the `runs`-field assertion, as a third line of defence (it still catches a short batch).

## Dials this wave added (all default-off / default-unchanged)

| env | effect |
|---|---|
| `SIGHTLINE_BALANCE_DRAW=1` | restore the pre-W1 per-frame GL clear in the batch loops |
| `SIGHTLINE_FXRNG=0` | restore the pre-W1 coupling of `Fx` to the gameplay RNG stream |
| `SIGHTLINE_ROUTE=first\|hash` | campaign routing policy; **default `first` = the shipped behaviour** |
| `SIGHTLINE_RNGFRAMETEST=1` | the new gate — frame-count invariance **and** render purity (also run by `qa-sweep.sh`) |
| `SIGHTLINE_ROUTETEST=1` | the route-skew histogram (also run by `qa-sweep.sh`) |

`qa-sweep.sh` also gains **FXSTREAM**, a shell-side grep (no env var) asserting `src/Fx.cs`
contains zero `Util.Rand*`. Its scope is limited on purpose and stated in the script: it would
**not** have caught the `Unit()` bob draw, which lives in `Unit.cs`. RNGFRAMETEST's render-purity
phase is the guard for that class.
