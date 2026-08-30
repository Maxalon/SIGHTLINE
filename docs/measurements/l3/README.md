# L3 — THE COMPOSED-TREE LADDER

**The first ladder in this project's history measured on a tree carrying every wave of its
program**, rather than on the tree one wave branched from.

**Base commit: `d814f0c`** — PROGRAM CROSSCUT with all eight waves merged (W1 TRUE INSTRUMENT,
TRUE BAND, W4 THE BOARD BECOMES A PLACE, W5 THE FIRST HOUR, W8 THE HALF WALL, W9 THE REPAIR,
W10 THE FIT, W2 THE OPPONENT ACTS). **960 campaigns**: 6 rungs × 8 disjoint CRN slot sets
(`SIGHTLINE_BALANCE_BASE` 0-70) × greedy+sloppy = **160 per rung**. All 48 chunks printed
`OK ... runs=20`; zero `BAD`.

**Why this round exists.** L2 is void. W2, W9 and W8 each move gameplay or composition, and W1
had already re-rolled every CRN world before that. X2's closing finding was that *fourteen
consecutive waves each published a ladder measured on their own branch point and nobody ever
measured the composition* — this round is the standing answer to that.

## THE LADDER

| rung | L3 (n=160) | ±SE | L2 | Δ | band | floor | verdict | step |
|---|---|---|---|---|---|---|---|---|
| **RECRUIT** | **71.2** | 3.6 | 72.5 | −1.2 | 75 ±8 | 67 | in | — |
| **heat 0** | **47.5** | 3.9 | 46.9 | +0.6 | 55 ±8 | 47 | in (at the floor) | −23.8 |
| **heat 2** | **31.2** | 3.7 | 36.9 | −5.6 | 40 ±8 | 32 | **below by 0.8** | −16.2 |
| **heat 4** | **23.8** | 3.4 | 23.1 | +0.6 | 30 ±8 | 22 | in | −7.5 |
| **heat 6** | **20.0** | 3.2 | 20.6 | −0.6 | 20 ±8 | 12 | **in, on target** | −3.8 |
| **heat 8** | **6.9** | 2.0 | 8.8 | −1.9 | 10 ±5 | 5 | in | −13.1 |

**Five of six rungs in band, monotone at every step.** The one miss is heat 2, 0.8 points under
its floor — a fifth of a standard error. **The largest move at any rung across eight waves is
5.6 points**, and four of six moved by less than 2.

That is the headline: a program that repaired sixteen defects, severed the gameplay RNG from the
frame rate, restructured the render pipeline, changed enemy behaviour and reshaped four screens
moved the difficulty ladder **almost not at all**. Every wave that touched gameplay priced itself,
and the composition confirms each of them.

## THE FLAT MIDDLE, REPLICATED A THIRD TIME

| step | L1 (pre-W1) | L2 (post-W1) | **L3 (composed)** |
|---|---|---|---|
| RECRUIT → h0 | −25.0 | −25.6 | −23.8 |
| h0 → h2 | −15.0 | −10.0 | −16.2 |
| h2 → h4 | −12.5 | −13.8 | **−7.5** |
| **h4 → h6** | **−3.8** | **−2.5** | **−3.8** |
| h6 → h8 | −7.5 | −11.9 | −13.1 |

`h4 → h6` is the smallest step on **all three ladders**, measured on three disjoint world sets by
two different instruments — and on the composed tree `h2 → h4` joins it at −7.5. The ends of the
ladder buy 23.8, 16.2 and 13.1 points; its middle buys 7.5 and 3.8.

`Heat.Mods` explains it exactly and the explanation has not changed: **rung 8 is the only entry in
the table carrying either `DmgDelta` or `AiTier`.** The middle rungs add bodies and stats; only
the apex changes KIND. This is a static-table property, which is what makes it fixable — and it is
now the best-replicated open finding in the project.

## THE WITHIN-RUN CURVE IS NOW A RAMP

| mission | 1 | 2 | 3 | 4 | 5 | 6 |
|---|---|---|---|---|---|---|
| win% | 98 | 83 | 77 | 78 | 73 | **68** |
| n | 960 | 433 | 695 | 548 | 407 | 472 |

Monotone apart from a 1-point m3/m4 wobble inside its own error. L1's curve was **U-shaped** —
X2's whole finding was a mission-1 failure hiding behind a rung average. The opening is now safe
and the difficulty climbs to the finale, which is the shape `docs/DESIGN.md` §3.D asks for.

## POOLED byObjective — 960 campaigns

| objective | n | win% | ±SE | turns |
|---|---|---|---|---|
| **Decapitate** | 633 | **62.1** | 1.9 | 4.90 |
| Sabotage | 163 | 80.4 | 3.1 | 3.74 |
| Hack | 113 | 81.4 | 3.7 | 3.69 |
| Escort | 307 | 82.7 | 2.2 | 9.16 |
| Rescue | 294 | 84.4 | 2.1 | 5.16 |
| Defend | 723 | 84.9 | 1.3 | 8.77 |
| Eliminate | 1155 | 89.1 | 0.9 | 4.22 |
| Evac | 127 | 90.6 | 2.6 | 5.48 |

**Read this row-by-row only with W8's warning attached.** W8 proved a pooled objective row can
hide a 49.5-point artifact: `Eliminate`'s 89.1% is largely 960 mission-1s, and pooled over its
*mid-run* cells it reads ~40%. Use `byObjectiveByNodeKind` and `byObjectiveByMission` — the
cross-tab W8 shipped — before drawing any conclusion from this table.

## THE MID-RUN DECAPITATE, REPLICATED A THIRD TIME

| | L1 | L2 | **L3 (composed)** |
|---|---|---|---|
| finale (BOSS node, m6) | 70.1% (n=234) | 69.7% (n=479) | **68.0%** (n=472) |
| **mid-run Decapitate** | 48.9% ±5.2 (n=92) | 46.0% ±3.9 (n=163) | **44.7% ±3.9** (n=161) |

**23.3 points harder than the campaign's climactic boss fight**, on three disjoint trees. W8
refuted the mechanism the lead first proposed (it is not the HVT buff — the buffed half is
*easier*) and located it in the **force**: `Mission.Build` de-stacks the finale by 3-4 bodies and
resets `bump`, and no mid-run Decapitate gets that. **That is the next lever, and it is unspent.**

## Method

`run_chunk.sh` is committed in this directory. It implements all three layers of W1's rewritten
measurement contract, in order: **(a)** `rm -f` the target JSON first — since W1 a display-less
batch REFUSES and leaves the previous chunk's file byte- and mtime-identical, so a bare
`runs`-assertion would read the PREVIOUS batch and print OK for a batch that measured nothing;
**(b)** check the process **exit code** (2 = no display, nothing written — the only signal a stale
file cannot fake); **(c)** assert the JSON's own `runs` field as a third line of defence.

The first attempt at this round produced **zero** chunks because a shared-scratchpad copy of the
runner had been overwritten by another agent. It failed loudly and wrote no data, which is the
contract working. The runner lives in the repo now.
