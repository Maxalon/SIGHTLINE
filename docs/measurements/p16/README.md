# P16 "GROUND TRUTH" — the CRN A/B round

**Base commit `ba34279`** (branch `wave/ground-truth`; the tree that shipped).
**Binary: a snapshot** — `dotnet publish -c Release -o runbin/p16` — so the tree could keep
building while the round was in flight. `runbin/` is gitignored.

## The lever

| arm | env | board |
|---|---|---|
| **A** | *(none)* | the shipped tree: VOID carries RIFTS, ARID carries SOFT SAND |
| **B** | `SIGHTLINE_NEWGROUND=0` | the **pre-P16** board exactly: VOID and ARID are paint again |

`SIGHTLINE_NEWGROUND` exists *because* `SIGHTLINE_BIOMEMECH=0` is the wrong arm for this wave:
that one restores the pre-**C4** board, so a round against it would price C4's three biomes and
P16's two together and report the sum as P16's. One lever per round (CLAUDE.md).

**The arm is verified from inside the artifact**, not asserted: `byBiome[].avgGroundTiles` reads
`VOID 12.3 / ARID 31.2` in arm A and **exactly `0.0 / 0.0`** in arm B, while `MAGMA 11.8 vs 11.7`,
`TUNDRA 16.2 vs 16.0` and `VERDANT 31.2 vs 31.6` are untouched (they differ only because the arms
diverge in play, so slightly different missions get played).

## Shape

3 rungs (h0 / h4 / h8) x 8 CRN slot bases (0,10,…,70) x 20 campaigns = **160 per rung per arm**,
**960 campaigns**, 48 chunks, all `OK`, `runs=20` asserted by `check_chunk.py`, heat **pinned**
(`EventCatalog.HeatPinned`; `campaignsRaised = missionsAbovePin = 0` on every chunk).
Pairing is **exact**: `campaigns[]` carries `(slot, policy)` per campaign and both arms run the
same slot seeds off the same binary, so arm A's campaign *k* and arm B's campaign *k* are the same
world with one lever moved. `analyse.py` asserts the two slot sets are equal before pairing.

## How to reproduce

```bash
dotnet publish -c Release -o runbin/p16
bash docs/measurements/p16/round.sh          # ~8 min, 48 chunks
python3 docs/measurements/p16/analyse.py     # the tables in RESULTS.txt
```
`run_chunk.sh` / `check_chunk.py` are verbatim copies of `docs/measurements/p15/` (the runner of
record), with `OUT` re-pointed here. All three completion layers are unchanged: `rm -f` the target
first, check the process EXIT CODE, then `check_chunk.py` on the artifact's own accounting.

## Result — the layer is NOT a difficulty lever at this n, and it is NOT inert either

| rung | A win% | B win% | delta | McNemar SE | cluster SE | t(7) | discordant | b/c | MDE(80%) |
|---|---|---|---|---|---|---|---|---|---|
| h0 | 43.1 | 45.6 | **−2.5** | 4.05 | 4.12 | −0.61 | 42/160 | 19/23 | ±11.3 |
| h4 | 22.5 | 23.8 | **−1.2** | 3.75 | 3.24 | −0.39 | 36/160 | 17/19 | ±10.5 |
| h8 | 6.2 | 5.0 | **+1.2** | 1.98 | 2.06 | +0.61 | 10/160 | 6/4 | ±5.5 |

**Pooled McNemar over all 480 CRN pairs: b=42, c=46, discordant 88, z = −0.43, delta −0.83 pts,
SE 1.95, 95% CI [−4.7, +3.0].**

**Read the discordance before reading the delta.** This is deliberately not the shape this project
has misread before (a flat row at low discordance, which is an absence of evidence dressed as
neutrality): **88 of 480 paired worlds — 18.3% — came out differently**, so the lever demonstrably
changes the game. What the round says is that the change does not resolve into a win-rate shift:
per rung it can detect about **±10 points** (±5.5 at h8, where the 5–6% base rate compresses
everything), and pooled about **±4**. Every observed value is far inside that. A real effect
smaller than ±4 points pooled is NOT excluded.

## The opener cell — clean, and this is the cell C4 failed

C4's own layer put its cost on mission 1 (`byNodeKind` **Start −4.37, t = −4.53**), which is the
front-loaded anxiety `docs/DESIGN.md` §3.D forbids and X2's `Mission.OpenerTrim` exists to prevent.
P16 checked the same cell:

| rung | Start (A vs B) | Combat | Elite | Boss |
|---|---|---|---|---|
| h0 | 91.9 vs 91.9 = **+0.0** | 69.7 vs 76.1 = −6.4 | 64.0 vs 65.3 = −1.3 | 55.2 vs 55.3 = −0.1 |
| h4 | 91.9 vs 91.2 = **+0.6** | 60.3 vs 57.9 = +2.4 | 51.0 vs 55.1 = −4.1 | 33.0 vs 36.2 = −3.2 |
| h8 | 92.5 vs 92.5 = **+0.0** | 35.1 vs 35.9 = −0.8 | 39.2 vs 34.6 = +4.6 | 23.3 vs 18.6 = +4.7 |

n = 160 per arm per rung on `Start` (every campaign has exactly one), unpaired SE ≈ 3.1.
**Nothing lands on the opener.** The `Combat` −6.4 at h0 is the largest cell in the table and is
1.5 unpaired SE; it does not reproduce at h4 (+2.4) or h8 (−0.8), so it is not a result.

## The two biomes the lever touches — and a control that proves the cell is noisy

| rung | VOID (A vs B) | ARID | STEEL *(paint control, ground 0 in BOTH arms)* |
|---|---|---|---|
| h0 | 72.4 vs 70.1 = +2.3 | 70.3 vs 71.4 = −1.1 | **74.7 vs 79.5 = −4.8** |
| h4 | 69.0 vs 69.5 = −0.5 | 62.7 vs 64.0 = −1.3 | **67.9 vs 67.1 = +0.9** |
| h8 | 58.9 vs 53.2 = +5.7 | 51.6 vs 60.3 = −8.8 | **54.4 vs 56.2 = −1.8** |

`STEEL` is the read that makes the other two interpretable. It is paint in both arms and cannot
have moved, and it reads **−4.8 / +0.9 / −1.8** — so a per-biome cell at n ≈ 80 carries roughly
±5 of pure noise, and none of the VOID or ARID values (largest: ARID h8 −8.8, its own unpaired
SE 8.8) is a result. **Do not quote a per-biome cell from this round as a measurement.**

## Instrument health

| arm | runs | missions | stalemate losses | frame-cap losses | aborted |
|---|---|---|---|---|---|
| A | 480 | 1915 | 8 (1.7%) | 0 | 0 |
| B | 480 | 1913 | 6 (1.3%) | 0 | 0 |

In line with L5's 1.41%. `missions` differs by 2 between arms because the arms diverge in play.

## What this round CANNOT say

* It cannot separate RIFT from SOFT SAND. They ride on one flag and each biome is 1 mission in 8,
  so splitting them would halve an already under-powered design. A per-mechanic round needs
  `SIGHTLINE_FORCEBIOME` pinned per arm and its own slot space.
* It cannot see FEEL, decision density, or whether a chasm makes a fight more interesting. Win rate
  is the only axis here. `choices/ARMED-soldier-turn` was not read (and the thresholds for it are
  VOID per CLAUDE.md's TRUE BAND note anyway).
* It is a 3-rung round at n=160, not a ladder. **The ladder of record is still L5.** Nothing here
  supersedes it, and nothing here may be subtracted from it.
