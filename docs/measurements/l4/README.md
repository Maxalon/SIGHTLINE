# L4 — THE COMPOSED-TREE LADDER OF RECORD

**Base commit: `7315425` (main, all six PROGRAM CONTOUR waves merged).**
Binary snapshot: `runbin/L4` (gitignored), published from that commit.

## Why this round exists

C1, C2, C3 and C4 each shipped a balance lever and each measured it **alone**, against its own
fresh baseline on its own branch point. That is correct wave discipline and it is exactly why a
composed-tree round is mandatory: **no wave measured the tree that actually ships.** CROSSCUT
learned this the same way (its L3 round exists for the same reason).

## Method — replicates L3 EXACTLY, so L3 -> L4 is like-for-like

6 rungs (RECRUIT, h0, h2, h4, h6, h8) x 8 CRN slot bases (0,10,...,70) x `N=10` (greedy+sloppy,
`runs=20` per chunk) = **160 campaigns per rung, 960 total, 48 chunks.** Same slot space as L3.

```bash
bash docs/measurements/l4/run_ladder.sh          # 48 chunks, asserts runs=20 on every one
bash docs/measurements/l4/attribute.sh 6         # one-lever-off arms at h6 (32 chunks)
```
Every chunk goes through `docs/measurements/c1/run_chunk.sh`, which carries all three layers of
W1's completion contract (rm the target first / check exit code 2 / assert the JSON's own `runs`).
**48/48 `OK ... runs=20`, zero BAD.**

## THE LADDER

| rung | L4 % | n | binomial SE | **cluster SE** | band | verdict | L3 | Δ |
|---|---|---|---|---|---|---|---|---|
| RECRUIT | **71.9** | 160 | 3.55 | 3.65 | 67–83 | IN | 71.2 | +0.6 |
| h0 | **53.1** | 160 | 3.95 | 3.53 | 47–63 | IN | 47.5 | +5.6 |
| h2 | **33.1** | 160 | 3.72 | 4.72 | 32–48 | IN (+1.1) | 31.2 | +1.9 |
| h4 | **21.9** | 160 | 3.27 | 4.11 | 22–38 | **OUT −0.1** | 23.8 | −1.9 |
| h6 | **10.6** | 160 | 2.44 | 2.20 | 12–28 | **OUT −1.4** | 20.0 | **−9.4** |
| h8 | **4.4** | 160 | 1.62 | 1.48 | 5–15 | **OUT −0.6** | 6.9 | −2.5 |

**Monotone at every step. Three of six in band.**

### The SHAPE is fixed — which was C1's whole objective
| step | L4 |
|---|---|
| RECRUIT → h0 | 18.8 |
| h0 → h2 | 20.0 |
| h2 → h4 | 11.2 |
| h4 → h6 | 11.2 |
| h6 → h8 | 6.2 |

No flat step anywhere. The "flat middle" that opened this program — `h4→h6` at −2.3 on the
pre-CONTOUR tree, and a rung 5 buying **exactly zero** — is gone. Note this happened even though
C1's lever, measured *alone*, made dispersion WORSE on every metric (its review found that and C1
re-decided to mode 3 on the evidence). **Shape is a property of the composition, not of a lever.**

### The LEVEL collapsed at the top, and that is the open problem
h6 fell 20.0 → 10.6. h4, h6 and h8 all sit below their floors, though **h4 (−0.1) and h8 (−0.6)
are far inside their own cluster SE and are not measured breaches** — only h6 is a real move, and
even it clears the floor by 0.64 cluster-SE.

## ATTRIBUTION — which lever? (h6, same 8 slot sets, one dial at a time)

| arm | h6 % | vs composed | cluster SE |
|---|---|---|---|
| composed (all four ON) | 10.6 | — | 2.20 |
| **C1 `SIGHTLINE_MIDTOOTH=0`** | **16.9** | **+6.2** | 2.98 |
| C2 `SIGHTLINE_AIDECLINE=0` | 6.9 | **−3.8** | 2.10 |
| C3 `SIGHTLINE_KILLTREADMILL=1` | 10.6 | **0.0** | 2.20 |
| C4 `SIGHTLINE_BIOMEMECH=0` | 11.9 | +1.2 | 2.82 |
| **ALL FOUR OFF** | **20.0** | **+9.4** | 3.78 |

Three things fall out, in descending order of confidence:

1. **The levers are the whole story.** All-four-off reproduces L3's h6 at **20.0%, to the
   decimal.** So C5 and C6 are confirmed gameplay-inert at this rung, and the CRN chain is intact
   from L3 through six merges.
2. **C1's `Heat.MidTooth` is the largest single contributor (+6.2).** Expected in direction — C1
   deliberately moved the +1 damage down to rung 6, which *is* h6 — but larger on the composed
   tree than the −4.7 C1 measured for it alone.
3. **C3 moves h6 by exactly 0.0**, independently confirming C3's own "heat 6 moved by exactly
   nothing" finding on a different tree. C2 removal makes h6 *harder* (−3.8): the post-C2 opponent
   is easier for the player at this rung than the pre-C2 constant-scoring one, consistent with C2's
   review finding that its dominant effect is +2 points of **hunkering**.

### NON-ADDITIVITY — stated as apparent, not established
Sum of the four single-lever removals is **+3.6**; joint removal is **+9.4**; the apparent
interaction term is **+5.8**, larger than any single lever. That would be the most interesting
result here — **and it is not resolved.** The joint contrast is chunk-paired
**t(7) = +2.05, p ≈ 0.08**, with per-slot deltas `+25 +5 −10 +20 0 +25 +10 0` (5 of 8 slots favour
all-off, 1 favours composed, 2 tie). The interaction term is a difference of two noisy quantities
and inherits both errors. **Do not quote +5.8 as a measured interaction.** What IS solid: the
direction, and that all-four-off lands exactly on L3.

## What this round does NOT do

It ships **no corrective lever.** Three rungs are under their floors and the disciplined response
is to publish that, not to repair it with an unmeasured change inside a measurement round — the
rule C4 correctly refused to break and that this program enforced on every wave. The dials are all
default-off-able and the attribution above says which one to reach for first.

Caveats inherited by this round, all pre-existing and all documented elsewhere: `Events.cs`
`AddHeat` contaminates every rung below 8 **upward** (heat 8 is clamped and cannot leak), so the
instrument compresses the top of the ladder it is used to diagnose — the exact region that
collapsed here. `instrumentHealth.stalemateLosses` is a ~2.1% harness-forced-loss floor. And at
n=160 with 8 clusters a rung resolves roughly 13–14 points, so every "in band" verdict above is a
point estimate, not a robustness claim.
