# P20 "THE STALE GROUND" — pricing the fix

**Base commit:** `dac9f2f` (PROGRAM PARALLAX milestone-12 composed tree) **plus P20's own fix**.
Binary snapshot: `runbin/P20/` (gitignored). Heat **PINNED** (`EventCatalog.HeatPinned`, the
default) — every chunk reports `campaignsRaised = missionsAbovePin = 0`.

## What is being compared

One lever, two arms, common random numbers:

| arm | file prefix | what it is |
|---|---|---|
| **fix** | `fix-h<H>-b<B>.json` | the default: `Mission.Build` clears the biome ground layer at its top |
| **stale** | `stale-h<H>-b<B>.json` | `SIGHTLINE_STALEGROUND=1` — the pre-P20 seam, in which Build's floor / cost / connectivity queries read the PREVIOUS mission's ground |

Two rungs (h0, h4) × 8 CRN slot bases (0, 10, 20, 30, 40, 50, 60, 70) × 20 campaigns × two
policies (greedy + sloppy) = **320 campaigns per rung per arm, 1,280 in all**. 32/32 chunks
asserted `runs=40 == batch.expectedRuns` through `docs/measurements/p15/check_chunk.py`.

## Commands

Exactly as run (`run.sh` covers bases 0/10, `run2.sh` the other six):

```bash
cd /home/user/SIGHTLINE
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p runbin/P20 && cp -r bin/Release/net8.0/. runbin/P20/

for h in 0 4; do for b in 0 10 20 30 40 50 60 70; do
  BIN=runbin/P20 OUT=docs/measurements/p20 \
    bash docs/measurements/p15/run_chunk.sh "fix-h$h-b$b"   "$h" "$b" 20
  BIN=runbin/P20 OUT=docs/measurements/p20 EXTRA="SIGHTLINE_STALEGROUND=1" \
    bash docs/measurements/p15/run_chunk.sh "stale-h$h-b$b" "$h" "$b" 20
done; done
```

`analyse.py` reproduces the table below from the JSON.

## Result

| rung | fix | stale | delta | worlds that end differently | McNemar (fix-only / stale-only wins) | chunk-paired t(7) |
|---|---|---|---|---|---|---|
| h0 | 42.5% | 40.3% | +2.2 | 25.3% | 16 / 9, z = +1.40 | +1.05 (mean +2.2 ± 2.1) |
| h4 | 18.1% | 21.9% | **−3.8** | 16.6% | 2 / 14, z = **−3.00** | **−3.97** (mean −3.8 ± 0.9) |

Per-slot-set delta, h4: `−10 −2 −2 −2 −2 −2 −2 −5` — **all eight negative**.
Per-slot-set delta, h0: `−2 −5 +2 +8 +12 +5 −2 +0`.

**h0 is not resolved. h4 is: the fix is a −3.8-point tightening at that rung.** A quarter of h0
worlds and a sixth of h4 worlds play out differently, so the change is bounded, not absent.

**This is a PRICE, not a ladder.** It compares the fix against its own control on one tree and one
8-set slot space. It is not comparable with the L5 rungs and no rescaling to them is legitimate.
L5 was measured on the stale board; the ladder owes a re-measure (see `CLAUDE.md`).
