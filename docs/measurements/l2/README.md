# L2 — THE POST-REPAIR LADDER AT n = 160

**960 campaigns.** 6 rungs x **eight** disjoint CRN slot sets (`SIGHTLINE_BALANCE_BASE`
0/10/20/30/40/50/60/70) x greedy+sloppy = **160 campaigns per rung**. All 48 chunks printed
`OK ... runs=20`, and every chunk's own `runs` field was re-asserted from the JSON afterwards.

**Base commit: `4784803`** — the integration tip carrying wave **W1 TRUE INSTRUMENT** and wave
**TRUE BAND**. Binary: a snapshot at `runbin/L2/`.

**Why this exists and why it is affordable.** W1 severed `src/Fx.cs`'s per-frame `Util.RandF()`
from the shared gameplay stream, which **re-rolled every CRN world in the project** — L1 and every
figure before it are formally incomparable to this table. The same wave stopped the batch rendering
a GL frame nobody looked at, so a chunk that cost ~500 s now costs ~20 s (independently timed here
at **4 campaigns in 4.0 s**). That surplus was spent on sample size, not banked: L2 is double L1's
n and quadruple the project's historical n=40.

## THE LADDER

| rung | L2 (n=160) | ±SE | L1 (n=80) | Δ | band | verdict | step |
|---|---|---|---|---|---|---|---|
| **RECRUIT** | **72.5** | 3.5 | 73.8 | −1.3 | 75 ±8 | in band | — |
| **heat 0** | **46.9** | 3.9 | 48.8 | −1.9 | 55 ±8 | at the floor (−0.1) | −25.6 |
| **heat 2** | **36.9** | 3.8 | 33.8 | +3.1 | 40 ±8 | in band | −10.0 |
| **heat 4** | **23.1** | 3.3 | 21.2 | +1.9 | 30 ±8 | in band | −13.8 |
| **heat 6** | **20.6** | 3.2 | 17.5 | +3.1 | 20 ±8 | in band, on target | **−2.5** |
| **heat 8** | **8.8** | 2.2 | 10.0 | −1.2 | 10 ±5 | in band | −11.9 |

Monotone at every step. Five of six comfortably in band; heat 0 sits **0.1 points under its floor**,
which at SE 3.9 is indistinguishable from the floor itself.

## FINDING 1 — severing the frame-coupled dice did NOT change the difficulty

The largest per-rung move between L1 and L2 is **3.1 points**, against a combined standard error of
~5. **W1's repair changed WHICH worlds you get, not how hard they are.** That is worth stating
plainly because it was not obvious in advance: a defect that made the dice depend on the frame
count could easily have been biasing the ladder, and it was not. The archive it invalidated was
mis-*indexed*, not mis-*calibrated*.

## FINDING 2 — the flat middle REPLICATES on disjoint worlds

Step sizes, two independent ladders on completely different world sets:

| step | L1 (pre-W1, n=80) | L2 (post-W1, n=160) |
|---|---|---|
| RECRUIT → h0 | −25.0 | −25.6 |
| h0 → h2 | −15.0 | −10.0 |
| h2 → h4 | −12.5 | −13.8 |
| **h4 → h6** | **−3.8** | **−2.5** |
| h6 → h8 | −7.5 | −11.9 |

Both measurements make `h4 → h6` the smallest step by a wide margin. `Heat.Mods` explains it
exactly: **rung 8 is the only entry in the table carrying either `DmgDelta` or `AiTier`**, so the
middle rungs add bodies and stats while only the apex changes KIND. This is now a replicated
finding rather than a suspicion, and it is the clearest open balance target in the project.

## FINDING 3 — L1's slot-set finding FAILED TO REPLICATE, and is RETRACTED

L1 reported that the two slot sets every wave has used since W2 (bases 0 and 10) ran **+8.3 points
easier** than fresh sets (z=1.93, **p=0.053**, 5 of 6 rungs positive), and explicitly recorded it as
"suggestive and NOT established". L2 is a far better-powered test of the same hypothesis — **six**
fresh sets against those two, 960 campaigns:

| rung | old (0,10) | new (20-70) | Δ |
|---|---|---|---|
| RECRUIT | 85.0 | 68.3 | +16.7 |
| heat 0 | 52.5 | 45.0 | +7.5 |
| heat 2 | 35.0 | 37.5 | −2.5 |
| heat 4 | 25.0 | 22.5 | +2.5 |
| heat 6 | 17.5 | 21.7 | −4.2 |
| heat 8 | 7.5 | 9.2 | −1.7 |

Pooled: old **89/240 = 37.1%**, new **245/720 = 34.0%** — **+3.1 points, SE 3.6, z = 0.85,
p = 0.394.** Three of six rungs now go the other way.

**The effect is not there.** L1's p=0.053 was a near-miss that a better-powered test has not
reproduced, and this directory retracts it rather than leaving it to be quoted. It is the exact
outcome L1's own hedge existed to permit.

**The method rule STAYS anyway** — a rung is four slot sets or it is not a rung — but it is now
justified on cost (it is free, and it insures against a world-set artefact) rather than on evidence
of one. That distinction matters: the rule is cheap insurance, not a finding.

## FINDING 4 — the mid-run Decapitate REPLICATES and strengthens

`Run.cs:556-558` makes exactly one Boss node, always the map's last, and `CardForNode` makes the
Boss **always Decapitate** — so the objective's row pools the campaign finale with the mid-run ones.
Split, on both ladders:

| | L1 (n=80/rung) | L2 (n=160/rung) |
|---|---|---|
| finale (BOSS node, m6) | 70.1% (n=234) | **69.7%** (n=479) |
| **mid-run Decapitate** | 48.9% ±5.2 (n=92) | **46.0% ±3.9** (n=163) |

**A mid-run Decapitate is 23.7 points harder than the campaign's climactic boss fight**, measured
twice on disjoint worlds. Pooled over all 960 campaigns Decapitate reads 63.7% ±1.9 at n=642 — the
worst objective row in the game, and the next-worst (Sabotage) is 83.5%.

The mechanism is named by the code's own comment. `Game.DesignateHvt` skips the HVT buff for an
**ELITE**, because *"an ELITE is ALREADY a tuned boss — double-buffing it would re-create the
stat-check wall we're removing."* On every OTHER Decapitate the HVT is the toughest rank-and-file
body and takes `+6 + mission` HP **and** +6 aim, on top of the `+3` every hostile already carries.
**The stat-check wall was removed from the boss and left in the mid-run case.** That remains a
hypothesis with a named mechanism — it needs its own paired round — but it is one expression to
test and it is now the best-evidenced open defect in the project.

## POOLED byObjective — 960 campaigns

| objective | n | win% | ±SE | turns |
|---|---|---|---|---|
| **Decapitate** | 642 | **63.7** | 1.9 | 4.84 |
| Sabotage | 164 | 83.5 | 2.9 | 3.90 |
| Defend | 735 | 84.2 | 1.3 | 8.74 |
| Rescue | 299 | 85.0 | 2.1 | 5.50 |
| Hack | 116 | 85.3 | 3.3 | 3.66 |
| Escort | 306 | 86.9 | 1.9 | 9.05 |
| Evac | 127 | 88.2 | 2.9 | 6.47 |
| Eliminate | 1158 | 89.6 | 0.9 | 4.27 |

Escort and Evac are **slow** (9.05 and 6.47 turns) and NOT lost — the ROADMAP's "Escort is the drag
objective" framing conflates two different defects with two different repairs.

## A NOTE ON THE DECISION-RICHNESS COLUMNS

`L2-LADDER.txt`'s `ch/ARM` column reads 2.0-2.6 against L1's 1.5-1.8. **That is not a change in the
game** — it is wave TRUE BAND replacing `CountMeaningfulChoices`' multiplicative near-best windows
with additive ones. The two ladders' decision columns are measured on different instruments and
must never be compared. The win-rate columns are unaffected (TRUE BAND was proven gameplay-inert
five ways).

## Files
`L2-LADDER.txt` is the pooled table as the aggregator printed it; `POOLED.json` the machine-readable
record; every chunk's `.json`, `.report.txt` and force-added `.log` alongside.
