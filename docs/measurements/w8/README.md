# W8 — THE HALF WALL: the mid-run Decapitate, and what the cross-tab actually found

**Base commit: `dfa7c0f`** — the integration tip carrying **W1 TRUE INSTRUMENT**, **TRUE BAND** and
the L1/L2 ladders. The wave's instrument is `ee5a85c` and its autopilot probe `95b127a`; both are
**proven gameplay-identical** to the base (see INERTNESS), so **L2's ladder is this wave's baseline**
and round B reproduces it exactly.

Binary snapshots (gitignored): `runbin/W8pre` = `dfa7c0f` as built; `runbin/W8inst2` = the
instrument; `runbin/W8inst3` = the instrument + the autopilot policy dial.

## HOW EVERY NUMBER HERE WAS PRODUCED

`run_chunk.sh` is W1's three-layer chunk runner re-pointed here, plus the four W8 dials in its env
passthrough. It (a) `rm -f`s the target JSON first, (b) checks the process exit code (2 = the
no-display refusal), and (c) asserts the JSON's own `runs == 2N`. **Every chunk in every round below
printed `OK ... runs=20`; zero `BAD`.**

```bash
# one chunk: <tag> <heat> <slot base> [N]   (N=10 => 20 campaigns: greedy + sloppy)
BIN=runbin/W8inst2 bash docs/measurements/w8/run_chunk.sh B-h0-b0 0 0 10

# round B  — UNPINNED, 6 rungs (hR=-1, h0/2/4/6/8) x 8 slot sets (0..70 step 10) x N=10
#            = 48 chunks, 960 campaigns. L2's exact grid, so it is directly comparable.
# round P0 — Decapitate-PINNED baseline: 6 rungs x 4 slot sets (0/10/20/30) = 24 chunks,
#            480 campaigns.  OBJ=decapitate
# round P1 — Decapitate-PINNED lever, ONE dial:                            OBJ=decapitate HVTDEPTH=-1
# round P2 — Decapitate-PINNED autopilot probe, ONE dial:                  OBJ=decapitate HVTPOLICY=0
# round BP — UNPINNED autopilot probe on B's first four slot sets (0/10/20/30) = 24 chunks,
#            480 campaigns, so it pairs chunk-for-chunk against B's own subset.  HVTPOLICY=0
#            (P2 and BP run on runbin/W8inst3; R0diag proves that binary logic-identical to W8inst2.)
OBJ=decapitate HVTDEPTH=-1  BIN=runbin/W8inst2 bash docs/measurements/w8/run_chunk.sh P1-h0-b0 0 0 10
OBJ=decapitate HVTPOLICY=0  BIN=runbin/W8inst3 bash docs/measurements/w8/run_chunk.sh P2-h0-b0 0 0 10
```

`SIGHTLINE_BALANCE_BASE` sets the CRN slot set, so P0/P1/P2 replay **identical worlds**, as do B and
BP, and they are CRN-paired chunk for chunk. A probe round is pooled only over chunks that exist in
BOTH rounds — an unfinished round otherwise compares a different rung mix, which briefly produced
the opposite conclusion mid-wave (see DEVLOG §W8.7).

## INERTNESS — three proofs, in ascending strength

1. **`I1-pre` vs `I1-post`** (`runbin/W8pre` vs `runbin/W8inst2`): the same three (rung, slot-set)
   chunks — h0/b0, h4/b10, h8/b20, 60 campaigns per arm. **42 of 43 aggregate JSON fields
   byte-identical on all three pairs.** The only mover is `harness{}` (`startedUtc`, loadavg,
   elapsed) which is designed to vary. The three new keys exist only on the post side.
2. **`R0diag` vs `P0`** (`runbin/W8inst3` vs `runbin/W8inst2`, both with the dials at default,
   both Decapitate-pinned, same three rung/slot pairs): **45 of 46 fields byte-identical**, again
   only `harness{}`. This is CLAUDE.md measurement-contract item 4 — the policy-dial binary is the
   same logic as the round's binary, so P2 may be paired against P0.
3. **Round B vs the L2 archive** — the real one. 960 campaigns on the instrumented binary,
   re-running L2's exact grid, pooled and compared row by row: **zero differing rows** across
   `byObjective` (8 rows), `byNodeKind` (5), `byMission` (6) and all six ladder rungs, on both `n`
   and win count, not merely on rate. 72.5 / 46.9 / 36.9 / 23.1 / 20.6 / 8.8, exactly as published.

## THE RESULTS

**(1) The decomposition verifies from data.** Pooled over L2's 48 chunks, `byNodeKind` Boss is
n=479 / 334 wins and `byMission` m6 is n=479 / 334 wins — identical counts. Mission 6 ⟺ Boss node in
960 campaigns, so the mid-run figure **46.0% ±3.9 (n=163)** stands.

**(2) The HVT buff is NOT the mechanism.** Round B, split by whether the HVT took the buff:
mid-run **BUFFED 57.4% (n=61)** vs mid-run **EXEMPT 39.2% (n=102)** — the buffed half is 18.2 points
**easier**, ±8.0. The m3 HVT (23.0 MaxHp) and the m6 HVT (22.4) are the same size of body and read
38.3% and 69.7%.

**(3) The lever, priced and shipped OFF.** P0 vs P1 (`HVTDEPTH=-1`, buff `6+m` → `6−m`), 480
campaigns per arm on identical worlds: buffed missions **73.8% → 78.6% (+4.8 ±2.1)**; mission 1,
which every campaign plays and which therefore carries no survivorship, **89.8% → 92.9% (+3.1)**;
m4 **47.6% → 57.3%**. The removed HP is −2.0 / −4.3 / −7.8 at m1/m2/m4 against a predicted
−2 / −4 / −8. The EXEMPT rows are the control: their HVT MaxHp is identical to a tenth of a point
across the arms, so their +1.3 / +6.4 is downstream carry-over, not the dial. **Not shipped:** the
buffed population is **61 of the 3,547 missions round B played (1.72%, 0.064 per campaign)**, and it
is the easier half of the very gap the lever was meant to close.

**(3b) The measuring bot is a real tax on Decapitate, and it is NOT the asymmetry.** B vs BP
(`HVTPOLICY=0`, 480 CRN-paired campaigns per arm) turns off `SmartDecapitate`'s hard focus policy —
the one that makes every soldier walk at the HVT past an intact firing line. Mid-run Decapitate goes
**47.7% → 57.0% (+9.3 ±7.6, n=86)** and the finale **68.0% → 77.4% (+9.4 ±4.1, n=234/239)**: the
**gap between them is 20.3 ±6.2 before and 20.4 ±6.0 after — a change of +0.1 ±8.6, i.e. nothing.**
The policy costs about nine points on this objective and costs them on both sides. Under the pin
(P0 vs P2) the same dial is worth +4.8 at mission 1, where every campaign plays and the HVT MaxHp is
identical at 14.4 — **more than the game lever in (3)**.

**(4) The bigger defect the cross-tab found.** `Eliminate`'s 89.6% flat row is 960 mission-1s; its
mid-run cells are **42.3% on Combat nodes (n=104)** and **33.3% on Elite nodes (n=33)**. Pooled on
mid-run node kinds, the two KILL objectives read **38.3% ±3.1 (n=248)** against the six with a
non-combat win condition at **83.4% ±1.0 (n=1259)** — 45.1 points. Full tables in DEVLOG §W8.

## FILES

**153 chunks, 3,060 campaigns archived here; 205 chunks run, zero `BAD`.** Per chunk:
`<tag>.json` (machine record), `<tag>.report.txt` (the printed report) and a force-added
`<tag>.log`. A mid-wave diagnostic grid (`D0`, 48 chunks) and its inertness pair (`I0`) were run on
an earlier build of the same instrument and pruned before commit: `B` reproduces `D0`'s win rates
exactly and adds the depth cells `D0` lacked, and `I1` supersedes `I0`.
