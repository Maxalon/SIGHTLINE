# P15 "THE UNVERIFIED" — the instrument's own accounting

Base commit **`a933cfe`** (PARALLAX milestone 8), branch `wave/qa-instrument`.

This directory holds two things: the **inertness round** that prices the one fix in this wave
which moves a published number, and the **fixtures** that make the two script-side fixes
re-runnable by the next person.

Nothing here is a balance measurement. No lever was shipped; no campaign outcome moved.

---

## 1. The inertness round — `inert/`

The one fix in P15 that changes a reported number is the **erased mission** (finding 6):
`Stats.BeginMission` used to overwrite the open `MissionRec`, so the mid-run checkpoint redeploy
(`Game.TryReinforcements`) destroyed the attempt it was retrying. It is now filed as a loss with
cause `REDEPLOYED`. That corrects every per-mission table; it must not touch a campaign outcome.

**Method.** Two Release binaries from the same tree: `post` = this branch, `pre` = the same tree
with the five P15 behaviours reverted line-for-line (silent `_HEAT`/`_BASE` fallback, no `batch{}`
block, `runWinRate` → 0.0, the open mission dropped, `EndEndless` ignoring its cause). Same CRN
slots, `SIGHTLINE_BALANCE=10 SIGHTLINE_BALANCE_BASE=0`, one chunk per rung, heat pinned.

```
docs/measurements/p15/inert/{pre,post}.json        heat 0
docs/measurements/p15/inert/{pre,post}-h4.json     heat 4
docs/measurements/p15/inert/{pre,post}-h8.json     heat 8
```

Command (per rung, `H` in 0/4/8; `$PRE` is the reverted build's snapshot dir):

```bash
SIGHTLINE_BALANCE=10 SIGHTLINE_BALANCE_HEAT=$H SIGHTLINE_BALANCE_BASE=0 \
  SIGHTLINE_BALANCE_JSON=.../pre-h$H.json \
  xvfb-run -a -s "-screen 0 1280x800x24" $PRE/Sightline
```

### Result — campaign-level: IDENTICAL at every rung

`runs`, `runWinRate`, `runWinRateExStalemate`, `avgMissionsCleared`, `policyGap`,
`pairedPolicy`, **`campaigns[]`**, `heatLeak`, `runsByMode` compare **equal on all three rungs**.
Every CRN world played out the same way. The fix is gameplay-inert.

### Result — mission-level: the correction, and its size

| rung | missions pre → post | redeployed attempts recovered | `meaningfulChoicesPerTurn` pre → post |
|---|---|---|---|
| h0 | 85 → 94 | 9 | 4.035 → 3.724 |
| h4 | 77 → 91 | 14 | 4.609 → 4.049 |
| h8 | 53 → 65 | 12 | 2.389 → 2.260 |

Per-mission win rate, pre → post:

| rung | m1 | m2 | m3 | m4 | m5 | m6 |
|---|---|---|---|---|---|---|
| h0 | 90 → 90 | 100 → 100 | 93.8 → **78.9** | 86.7 → **76.5** | 100 → **91.7** | 80 → **66.7** |
| h4 | 90 → 90 | 100 → 100 | 75 → **57.1** | 91.7 → **68.8** | 88.9 → 88.9 | 58.3 → **41.2** |
| h8 | 95 → 95 | 50 → 50 | 38.5 → **22.7** | 57.1 → **44.4** | 25 → **20** | 100 → 100 |

**Missions 1 and 2 do not move at any rung, and that is the internal check.** The checkpoint
valve does not open before mission 3 on the standard ladder (`TryReinforcements`: `ckMin = 3`,
or 1 on RECRUIT), so the correction is *exactly zero* everywhere the defect cannot fire. Nothing
else in the batch produced a difference.

**Read this before quoting an old per-mission or decision-density number.** Every
`byMission` / `byObjective` / `byObjectiveByMission` / `byNodeKind` / `byBiome` / `lossCauses` /
`decisionRichness` figure in every archive in this repository was computed over a mission set
that had dropped exactly the missions where the squad was wiped and redeployed — the losses.
The bias is one-directional (upward on win rate) and it is not small: **13–23 points on the
mid-run missions above**, and −5% to −12% on `meaningfulChoicesPerTurn`. The campaign-level
ladder (the L5 table of record) is unaffected.

`SIGHTLINE_MISSIONFLUSH=0` restores the pre-P15 drop, so this is priced with one dial.

---

## 2. Fixtures — `fixtures/`

All four are REAL artifacts written by the Release binary of this tree (`SIGHTLINE_BALANCE=3`,
heat 4, base 0), not hand-written JSON.

| file | what it is | old check | new check |
|---|---|---|---|
| `fx-good-h4-b0.json` | the control: greedy+sloppy, `runs=6` | OK | OK |
| `fx-single-h4-b0.json` | `SIGHTLINE_BALANCE_SLOPPY=1`, one leg, `runs=3` | **BAD** (wants `N*2`) | OK |
| `fx-unnamed-h4-b0.json` | filed as `-h4` but run with NO `_HEAT` — the batch cycled `{0,2,4,6,8}` | **OK** (`runs=6`) | **BAD** |
| `fx-unpinned-h4-b0.json` | `SIGHTLINE_HEATPIN=0`; really leaks (2 campaigns raised, 7 missions off-rung, `maxHeatEnd=5` on a "heat 4" chunk) | note, exit 0 | **BAD** in a mixed round |

```bash
bash docs/measurements/p15/regress.sh     # PASS/FAIL, before and after, on all four
```

`regress.sh` fetches the **actual pre-wave scripts out of git at `a933cfe`** for the "before"
column, so the comparison cannot drift with an edited copy. It also re-runs the fixed
`l5/cluster.py` over the whole 96-chunk L5 archive and asserts it still reads `LEAK-CHECK: PASS`
and the published ladder to the decimal.

---

## 3. The runner of record — `run_chunk.sh` + `check_chunk.py`

**Copy `docs/measurements/p15/run_chunk.sh`, not `w1/run_chunk.sh`.** The three-layer completion
check in CLAUDE.md is unchanged in spirit; layer (c) is no longer a guess:

* `runs` is asserted against the artifact's own `batch.expectedRuns` (N x policy legs), so a
  single-policy chunk is a valid chunk instead of a `BAD` line.
* the rung and the slot base in the artifact are asserted against what the runner exported, so a
  chunk's **file name is checkable**.
* exit **3** is a new refusal: the batch could not name itself (an unparseable
  `SIGHTLINE_BALANCE` / `_HEAT` / `_BASE`) and wrote nothing. Exit 2 is still "no display".
* an artifact with no `batch{}` block (a pre-P15 binary) is `BAD` unless the caller passes
  `--legacy` **explicitly**. The script never downgrades itself — that self-downgrade is the
  exact defect P15 fixed in `l5/cluster.py`.
