# L1 — THE LADDER OF RECORD AT n = 80

The first six-rung ladder this project has measured at **80 campaigns per rung**, and the first
one whose absolute level was checked against slot sets the project had never run.

**Base commit of every number in this directory: `636112c`** (PROGRAM CROSSCUT housekeeping on
top of `d350416`, RESONANCE milestone 2). The tree is unmodified game code — the only commits
between `d350416` and the snapshot are a root `LICENSE` and documentation, so these numbers are
the shipped `main` of RESONANCE milestone 2.

**Binary:** a snapshot of the Release build at `runbin/L1/` (gitignored), so the working tree
could keep building while the round was in flight.

## How every chunk was run

```bash
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg/<tag>"; export XDG_CONFIG_HOME="$PWD/.xdg/<tag>"
SIGHTLINE_BALANCE=10 SIGHTLINE_BALANCE_HEAT=<h> SIGHTLINE_BALANCE_BASE=<0|10|20|30> \
  SIGHTLINE_BALANCE_JSON="$PWD/docs/measurements/l1/<tag>.json" \
  setsid xvfb-run -a -s "-screen 0 1280x800x24" runbin/L1/Sightline > docs/measurements/l1/<tag>.log
```

`xvfb-run` is mandatory: without a display `SIGHTLINE_BALANCE` prints `runs=0 / (no data)`, still
claims N matches and exits 139 — a silent zero-data batch that looks completed. The runner
asserts the JSON's own `runs` field (it cannot be half-written) and prints OK/BAD.
**All 24 chunks printed `OK ... runs=20`.** 24 chunks x 20 = **480 campaigns**.

Six rungs (RECRUIT / h0 / h2 / h4 / h6 / h8) x four disjoint CRN slot sets
(`SIGHTLINE_BALANCE_BASE` 0, 10, 20, 30) x greedy+sloppy paired = **80 campaigns per rung**.
Counts pool exactly; the ratio-only decision-richness fields pool as the unweighted mean of the
four equal-sized 20-run chunks (the W4/X2 convention, kept so rows stay comparable).

## THE LADDER

| rung | L1 (n=80) | ±SE | X2 (n=40) | band | verdict | step |
|---|---|---|---|---|---|---|
| **RECRUIT** | **73.8** | 4.9 | 75.0 | 75 ±8 | in band | — |
| **heat 0** | **48.8** | 5.6 | 57.5 | 55 ±8 | in band | −25.0 |
| **heat 2** | **33.8** | 5.3 | 35.0 | 40 ±8 | in band | −15.0 |
| **heat 4** | **21.2** | 4.6 | 30.0 | 30 ±8 | **BELOW by 0.8** | −12.5 |
| **heat 6** | **17.5** | 4.2 | 20.0 | 20 ±8 | in band | **−3.8** |
| **heat 8** | **10.0** | 3.4 | 17.5 | 10 ±5 | in band, **on target** | −7.5 |

Three things n=40 could not say, and this table can:

1. **The ladder is monotone at every single step.** X2 explicitly recorded that "rung ORDER is
   not resolvable at n=40" and that two waves had argued about inversions no data could settle.
   At n=80 the order is clean, first try, with no lever spent.
2. **heat 8 lands on its published target to the decimal.** X2 measured it **out of band at
   17.5%** (+2.5 over the ceiling) and recorded it as an open defect. It is 10.0%. The defect was
   the measurement.
3. **The middle of the ladder is flat and the bottom of it is a cliff.** The steps run
   −25.0, −15.0, −12.5, **−3.8**, −7.5. Heat 5 and 6 together buy **3.8 points**. This is the
   audit's `balance-3` finding confirmed at n=80: rung 8 is the only entry in `Heat.Mods` carrying
   either `DmgDelta` or `AiTier`, so the ladder adds bodies and stats through the middle and only
   changes KIND at the very top.

`heat 4`'s miss is 0.8 points below the band floor at 1.9σ from the band centre — a boundary
case, not a hole. It is recorded, not repaired.

## THE SLOT-SET FINDING — read this before quoting any pre-L1 number

`L1-SLOTSETS.txt` decomposes every rung into its four 20-campaign slot sets. **Bases 0 and 10 are
the only sets any wave in this project has used since W2.** Bases 20 and 30 had never been run.

| rung | b0 | b10 | b20 | b30 | old (0+10) | new (20+30) | spread |
|---|---|---|---|---|---|---|---|
| RECRUIT | 65.0 | 80.0 | 80.0 | 70.0 | 72.5 | 75.0 | 15.0 |
| heat 0 | 65.0 | 55.0 | 40.0 | 35.0 | **60.0** | **37.5** | 30.0 |
| heat 2 | 40.0 | 30.0 | 35.0 | 30.0 | 35.0 | 32.5 | 10.0 |
| heat 4 | 25.0 | 25.0 | 5.0 | 30.0 | 25.0 | 17.5 | 25.0 |
| heat 6 | 35.0 | 5.0 | 15.0 | 15.0 | 20.0 | 15.0 | 30.0 |
| heat 8 | 15.0 | 20.0 | 0.0 | 5.0 | **17.5** | **2.5** | 20.0 |

Pooled over all six rungs: the old slot sets run **92/240 = 38.3%** and the new ones
**72/240 = 30.0%** — a **+8.3 point** difference, SE 4.3, **z = 1.93, two-sided p = 0.053**, with
5 of 6 rungs positive.

**That is suggestive and it is NOT established.** p = 0.053 at n = 240 per arm is exactly the
kind of number this project has learned not to bank. What it is sufficient for is a **method
rule**, which costs nothing to adopt and which would have caught it years ago:

> **No wave may measure a ladder on bases 0 and 10 alone.** A rung is 4 slot sets or it is not a
> rung. Any wave that wants a 40-campaign round must draw its two sets from a rotating pool, and
> must say which two it drew.

The mechanism, if it is real, is not mysterious: `Util.Reseed(50000 + slot)` means slot *i* is a
fixed world, forever. Twenty fixed worlds became the project's definition of "the game", and every
lever since W2 has been priced against them.

## SAME-SLOT REPRODUCTION — the tree has not drifted

Run on X2's own slot set (bases 0+10 only, n=40, directly comparable to X2's archived S1 chunks):

| rung | X2 S1 (n=40) | L1 same slots (n=40) | Δ |
|---|---|---|---|
| RECRUIT | 75.0 | 72.5 | −2.5 |
| heat 0 | 57.5 | 60.0 | +2.5 |
| heat 2 | 35.0 | 35.0 | 0.0 |

Within 2.5 points at every rung. **This independently confirms that A3 AUDITION and R2 QA FIXES
were gameplay-inert**, which both waves claimed against their own batches and neither checked
against the ladder. (R2 changes procedural ENVELOP geometry in ~1-4% of builds, which is the size
of the residual.)

## POOLED byObjective — 480 campaigns, every rung

| objective | n | win% | ±SE | turns |
|---|---|---|---|---|
| Escort | 156 | 83.3 | 3.0 | **10.84** |
| Evac | 56 | 78.6 | 5.5 | **10.45** |
| Defend | 368 | 81.5 | 2.0 | 8.73 |
| **Decapitate** | **326** | **64.1** | **2.7** | 4.79 |
| Rescue | 110 | 85.5 | 3.4 | 4.68 |
| Hack | 130 | 88.5 | 2.8 | 3.68 |
| Eliminate | 536 | 92.5 | 1.1 | 3.66 |
| Sabotage | 115 | 85.2 | 3.3 | 3.64 |

**Decapitate pools to 64.1% — but the pooled number is misleading, and the decomposition is the
real finding.** `Run.cs:556-558` sets `Map[Map.Count-1].Kind = NodeKind.Boss`, exactly one Boss
node, always the map's last, and `CardForNode` makes the Boss **always Decapitate**. So every one
of the 234 mission-6 attempts is a Decapitate, and the objective's row pools the campaign finale
together with the mid-run ones:

| Decapitate | n | win% | ±SE |
|---|---|---|---|
| **finale (the BOSS node, m6)** | 234 | **70.1** | 3.0 |
| **mid-run (every other node)** | **92** | **48.9** | **5.2** |

**A mid-run Decapitate is 21 points HARDER than the campaign's climactic boss fight**, and at
48.9% it is the worst mission of any kind in the game — against a per-mission average near 80%.

The mechanism is in `Game.DesignateHvt` (src/Game.cs:2059) and it is documented in its own
comment. On the Boss node the HVT is the WARLORD, an **ELITE**, and the code deliberately skips
the buff: *"an ELITE is ALREADY a tuned boss — double-buffing it would re-create the stat-check
wall we're removing."* On every OTHER Decapitate the HVT is the toughest rank-and-file body and it
takes `+6 + mission` HP (so +8 to +11 at missions 2-5) **and** +6 aim — on top of X1's
`Mission.HostileToughness` +3 that every hostile already carries.

**The stat-check wall was removed from the boss and left in the mid-run case.** That is a
hypothesis with a named mechanism, not a proven cause — it needs its own paired round — but it is
specific, it is cheap to test (`DesignateHvt`'s bonus is one expression), and nothing in the
project has ever looked at it.

What is true of Escort and Evac is that they are **slow** (10.84 and 10.45 turns against
Eliminate's 3.66), not that they are lost. Slow and lost are different defects with different
repairs, and the project has been conflating them.

Evac at n=56 is finally large enough to read at all — X2 had it at n=4 and n=25 pooled.

## Round index

| tag | rung | slot sets |
|---|---|---|
| `L1-hR-b{0,10,20,30}` | RECRUIT | 0-9 / 10-19 / 20-29 / 30-39 |
| `L1-h0-b{0,10,20,30}` | heat 0 | " |
| `L1-h2-b{0,10,20,30}` | heat 2 | " |
| `L1-h4-b{0,10,20,30}` | heat 4 | " |
| `L1-h6-b{0,10,20,30}` | heat 6 | " |
| `L1-h8-b{0,10,20,30}` | heat 8 | " |

`L1-LADDER.txt` is the pooled table as the aggregator printed it; `L1-SLOTSETS.txt` is the
slot-set decomposition; `POOLED.json` is the machine-readable pooled record. Every chunk's raw
`.log` is force-added alongside its `.json` and `.report.txt` (the repo `.gitignore` blocks
`*.log` globally).

## THE STANDING WARNING

A later wave in this program (**W1 TRUE INSTRUMENT**) severs `src/Fx.cs`'s screen-shake jitter
from the shared gameplay `Util.Rng`. `Fx.Update` draws `Util.RandF()` once per **rendered frame**
while shake is active, so today the dice are a function of the frame count — and screen shake is a
shipped comfort toggle, which means **a player who turns it off is playing different dice.**

That repair is correct and necessary, and it **invalidates every CRN world in this directory**.
L1 is therefore the **pre-repair ladder**, and it is the only n≥80 picture of the pre-repair tree
that will ever exist. It is what lets a later wave say how much of the ladder's shape was the
frame-coupled dice.
