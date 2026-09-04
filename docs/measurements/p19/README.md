# P19 "THE ROSTER CONTESTS" — raw measurement round

**Base commit `3d5c405`** (branch `wave/roster-contests`). Binary snapshot: `runbin/p19/new`
(gitignored), built `dotnet build -c Release` from that tree. Every chunk ran under `xvfb-run`
through `run_chunk.sh`, which does the three-layer completion check the CLAUDE.md measurement
contract requires: `rm -f` the target JSON first, check the process EXIT CODE (2 = no display,
nothing written), and assert the JSON's own `runs` field.

## What is here

| file(s) | what |
|---|---|
| `p19-basebin-h{0,4,8}-b0.*` | the **BASE-COMMIT** binary (`runbin/p19/base`, built before any P19 edit). The pre-fix spawn-rate derivation quoted in DEVLOG §3 comes from these + the round's `pre` arm. |
| `p19-R0diag-h{0,4,8}-b0.*` | the **NEW** binary with both dials off, same chunks. `inert_diff.py` against the basebin files: **0 differing fields of 2,374 / 2,315 / 2,412.** The `pre` arm is an exact restoration; the CRN chain is intact. |
| `p19-pre-h{0,4,8}-b{0..70}.*` | arm **pre** — `SIGHTLINE_ELITEBOSS=0 SIGHTLINE_ROSTERID=0`, the pre-P19 tree. |
| `p19-boss-h{0,4,8}-b{0..70}.*` | arm **boss** — item 1 alone (the named elite moves to the ELITE node + the route-walked final-approach floor). |
| `p19-full-h{0,4,8}-b{0..70}.*` | arm **full** — SHIPPED: item 1 + item 2 (the SMG monoculture's three range bands). |
| `summary.txt` | the aggregated tables (contrasts with n_discordant / McNemar z / MDE, the mission-1 and node-kind cells, and the re-derived spawn exposure). |
| `agg.py` / `agg2.py` | the aggregators that produce `summary.txt`. |
| `inert_diff.py` | the R0diag field-for-field comparator (strips only the `harness{}` block, which is designed to vary). |
| `battery.sh` / `run_chunk.sh` | the round, reproducible. |
| `eliteshot-{on,off}.png` | the same ELITE node, `SIGHTLINE_ELITEBOSS` flipped: BREAKER on the board vs no named elite. |
| `bandshot-{on,off}.png` | the same board/tile/pair at 7.28 tiles, `SIGHTLINE_ROSTERID` flipped: the INCOMING FIRE card names a different worst gun. |

## Exact command lines

```bash
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg"; export XDG_CONFIG_HOME="$PWD/.xdg"

# binary snapshots (gitignored) — the tree can keep building while a chunk is in flight
dotnet build -c Release && mkdir -p runbin/p19/new && cp -r bin/Release/net8.0/. runbin/p19/new/

# (1) R0diag FIRST — the tree gained instrumentation, so prove logic identity before quoting
for h in 0 4 8; do BIN=runbin/p19/new ELITEBOSS=0 ROSTERID=0 \
  bash docs/measurements/p19/run_chunk.sh "p19-R0diag-h$h-b0" "$h" 0 20 & done; wait
for h in 0 4 8; do python3 docs/measurements/p19/inert_diff.py \
  docs/measurements/p19/p19-basebin-h$h-b0.json docs/measurements/p19/p19-R0diag-h$h-b0.json; done

# (2) the round: 3 arms x 3 rungs x 8 slot bases x N=20  => 72 chunks, 2,880 campaigns
BIN=runbin/p19/new bash docs/measurements/p19/battery.sh 4

# (3) the tables
python3 docs/measurements/p19/agg.py ; python3 docs/measurements/p19/agg2.py

# (4) the screenshots
SIGHTLINE_SEED=8811 SIGHTLINE_ELITESHOT=1 SIGHTLINE_SHOT=760 \
  xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Release     # add SIGHTLINE_ELITEBOSS=0 for the contrast
SIGHTLINE_SEED=4242 SIGHTLINE_BANDSHOT=1 SIGHTLINE_SHOT=760 \
  xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Release     # add SIGHTLINE_ROSTERID=0 for the contrast
```

> The per-chunk `.log` files are `.gitignore`d project-wide (`*.log`); `run_chunk.sh` extracts the
> BALANCE REPORT from each into the committed `.report.txt` beside its `.json`.

## Completion

**72/72 chunks asserted `runs=40`.** `heatPinned=True` and `missionsAbovePin=0` on all 72 (the
HEAT PIN wave's `EventCatalog.HeatPinned` is on for every `SIGHTLINE_BALANCE` batch, so a rung
means the rung). STALEMATE 17/960 campaigns (1.77%) in `pre` and in `full`, 11/960 (1.15%) in
`boss` — all on the MISSION arm; the RUN arm fired zero times in 2,880 campaigns. No TIMEOUTs.

## The one number to read

**No contrast in this round is resolved.** The MDE column in `summary.txt` is the smallest true
paired effect the round could have detected at 80% power given the OBSERVED discordance rate:
**8.6 / 6.7-7.9 / 3.4-5.2 points at h0 / h4 / h8.** Every observed |Δ| is smaller than that. The
correct statement is *"any true effect larger than the MDE would have been detected and none was"*
— an absence of evidence, not a demonstration of neutrality. The discordance counts are printed
beside every row so that reading cannot be skipped.

The cells that DID move are descriptive (routes diverge, so the arms' per-node counts differ and
these are not paired): the ELITE node got heavier (h8 38.7% -> 30.1%) and the plain COMBAT fights
got lighter (h0 69.5% -> 71.7%). The wave redistributed difficulty onto the node the player
chooses; it did not add or remove it.

**The mission-1 cell is untaxed** — item 1 is exactly identical at every rung by construction
(mission 1 is always the Start node), and item 2 moves it +0.3 / +1.2 in the player's favour.
