# P18 "THE SECOND AXIS" — the INERTNESS round

**Base commit: `f81d3fa`** (PROGRAM PARALLAX milestone 10, "ships as v1.0.0").
Branch: `wave/the-fork-choice`. Measured 2026-09-04 in the standard container.

## What this round is, and what it deliberately is NOT

It is **not a ladder**. P18 ships no lever that a `SIGHTLINE_BALANCE` batch can see, and running a
rung round would have produced six numbers that could only ever restate the base tree's. This round
is the **proof of that claim**, which is the thing the wave actually owes: three CRN-paired batches,
one per rung, base binary vs P18 binary, diffed field-for-field.

**Why the wave is campaign-inert at the flywheel's default.** Every behaviour P18 adds is behind a
gate the batch cannot open:

| change | gate | what the batch sees |
|---|---|---|
| the three heat-gated unlocks (COMBAT TRIALS / DEEP RESERVE / DEEP STORES) | every read is `!NoPersist && SaveGame.HasUnlock(...)`; `Game.RefreshMetaWidths` **resets** `Run.PerkOfferWidth` to 2 under `NoPersist` rather than merely skipping | `NoPersist` is set for every batch campaign, so no meta is read and no width is raised |
| the BONUS-perk RECIPIENT picker | `Run.TryQueueBonusPerk`'s roll is **unchanged** and remains the default recipient; `RetargetBonusPerk` runs only from a mouse click and is derived by pure hash (`Run.Mix`), spending **zero** `Util.Rng` draws (asserted: `SIGHTLINE_REWARDTEST` leg D) | nothing — the bot never opens the chooser, and the draw sequence is byte-identical |
| the adaptive-assist LATCH | `Run.AssistLevel` needs `LossStreak > 0`, and `_metaLossStreak` is 0 under `NoPersist` (meta is never loaded). Independently, `EventCatalog.HeatPinned` makes every `AddHeat` arm a no-op in a batch | nothing, twice over |

## The batches

`BIN=runbin/<arm> bash run_chunk.sh <tag> <heat> <base> 20` — Release binaries from gitignored
snapshots (`runbin/p18base` = `f81d3fa`, `runbin/p18` = this branch), under `xvfb-run`, with the
three-layer completion check the CLAUDE.md contract requires (rm -f first / exit code / `runs`).

| chunk | rung | slot base | N | greedy+sloppy campaigns | `runs` asserted |
|---|---|---|---|---|---|
| `INERT-{base,p18}-h0-b0` | heat 0 | 0 | 20 | 40 per arm | 40/40 ✓ |
| `INERT-{base,p18}-h4-b10` | heat 4 | 10 | 20 | 40 per arm | 40/40 ✓ |
| `INERT-{base,p18}-h8-b20` | heat 8 | 20 | 20 | 40 per arm | 40/40 ✓ |

**240 campaigns, 120 per arm, three disjoint CRN slot sets.**

## Result

```
$ bash inert_diff.sh INERT-base-h0-b0.json  INERT-p18-h0-b0.json  harness   -> (empty diff — IDENTICAL)
$ bash inert_diff.sh INERT-base-h4-b10.json INERT-p18-h4-b10.json harness   -> (empty diff — IDENTICAL)
$ bash inert_diff.sh INERT-base-h8-b20.json INERT-p18-h8-b20.json harness   -> (empty diff — IDENTICAL)
```

**2,356 aggregate leaf fields per pair, zero differences on all three pairs** — every per-slot
`RunRec`, win, loss, turn count, shots-per-kill, objective cell and `campaigns[]` row is identical.
`harness{}` is excluded because it records nproc/loadavg/elapsed and is designed to vary (W1's
`inert_diff.sh`, copied here unchanged).

`SIGHTLINE_PAIRTEST` (the CRN byte-identity gate itself) is green in the same tree's
`qa-sweep.sh --full`.

## What this round does NOT establish

- **Nothing about a profile that OWNS the unlocks.** The flywheel has no meta profile at all
  (`NoPersist`), so it cannot price COMBAT TRIALS' wider perk offer, DEEP STORES' extra slate slot
  or DEEP RESERVE's larger recall roster. Pricing those needs a harness that can run a batch
  against a staged profile, which does not exist. It is in `docs/ROADMAP.md` as an open item, and
  **no P18 number claims otherwise.**
- **Nothing about the assist.** `LossStreak` is 0 in every batch, so the latch's real-play effect
  (a heat-0 run keeps up to 5 tiers of enemy stat relief after a heat-raising field event instead
  of losing them) is unmeasured by the flywheel and is asserted structurally by `SIGHTLINE_EVENTTEST`.
