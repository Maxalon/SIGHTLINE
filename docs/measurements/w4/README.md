# WAVE W4 "THE SECOND AXIS" — archived measurement artifacts

Every chunk's JSON summary and its report extract, as run. Kept because measurement rounds are
expensive and the sandbox container is ephemeral; the write-up in
[`docs/DEVLOG.md` §RESONANCE W4](../../DEVLOG.md) quotes only numbers that appear here.

**How every chunk was run** (`run_chunk.sh`, archived here — force-added, the repo `.gitignore`
blocks `run_chunk.sh` globally):

```bash
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg/<tag>"; export XDG_CONFIG_HOME="$PWD/.xdg/<tag>"
env [SIGHTLINE_DEPLOY=<shape>] [SIGHTLINE_DEPLOYMIX=a,b,c,d] [SIGHTLINE_ESCORTFIX=0] \
  SIGHTLINE_BALANCE=10 SIGHTLINE_BALANCE_HEAT=<h> SIGHTLINE_BALANCE_BASE=<0|10> \
  SIGHTLINE_BALANCE_JSON=<out.json> \
  xvfb-run -a -s "-screen 0 1280x800x24" <bin>/Sightline > <out.log>
```

`xvfb-run` is mandatory: without a display `SIGHTLINE_BALANCE` prints `runs=0 / (no data)`, still
claims N matches and exits 139 — a silent zero-data batch. `run_chunk.sh` asserts `runs=20` in
every log and prints OK/BAD; every chunk quoted in the DEVLOG printed **OK**.

Two disjoint CRN slot sets (`SIGHTLINE_BALANCE_BASE` 0 and 10) x greedy+sloppy = **40 campaigns
per rung**. `XDG_CONFIG_HOME` and `SIGHTLINE_BALANCE_JSON` are pinned per chunk because several
dev agents share the container and the harness otherwise defaults to a shared `/tmp` path.

**Binaries.** Rounds were run from *snapshots* of the Release build (`runbin/<tag>/Sightline`,
gitignored) so the tree could keep building while a round was in flight — rebuilding into
`bin/Release` mid-chunk would swap the binary under a running measurement.

`agg.py <tag-prefix>` pools the two chunks of a rung into the wave's gate table. Counts (runs,
wins, per-objective and per-shape n) pool EXACTLY; the decision-richness ratios pool as the
unweighted mean of the two equal-sized (20-run) chunks, because the harness JSON publishes those
as ratios rather than raw sums.

| tag | state | rung |
|---|---|---|
| `R0-h{0,4}-b{0,10}` | fresh baseline on the integration tip, no lever | h0 / h4 |
| `R0diag-h0-b0` | the W4 tree at its DEFAULT mix (FRONTAL only) — the logic-identity check against `R0-h0-b0` | h0 |
| `P1-h0-b*` | `SIGHTLINE_DEPLOY=pincer` pinned | h0 |
| `C1-h0-b*` | `SIGHTLINE_DEPLOY=crossfire` pinned | h0 |
| `E1-h0-b*` | `SIGHTLINE_DEPLOY=envelop` pinned (falls back to FRONTAL where the objective forbids a centre deployment) | h0 |
| `M1-h0-b0` | `SIGHTLINE_PODMASS=4` — bigger, fewer pods (**reverted**) | h0 |
| `U1-h0-b*` | `SIGHTLINE_PODUNIFORM=1` — a pod fields one kind of body (**shipped**) | h0 |
| `S1-h{0,4}-b*` | **the shipped state**: `SIGHTLINE_DEPLOYMIX=3,3,1,3` + `PODUNIFORM=1` | h0 / h4 |
| `S2-h{0,4}-b*` | the heavier deal `SIGHTLINE_DEPLOYMIX=1,4,1,4` + `PODUNIFORM=1` | h0 / h4 |
| `ESC-{off,fix}-h8-b0` | the `SmartEscort` downed-soldier instrument fix, `SIGHTLINE_OBJ=escort` pinned, `SIGHTLINE_ESCORTFIX=0` vs default | h8 |

Every lever round except the escort pair carries `EFIX=0` (`SIGHTLINE_ESCORTFIX=0`) so it is
compared against `R0` on the SAME instrument; the escort fix is isolated in its own pair.

## `shots/` — the openings, as they render

Downscaled (640x400, 128 colours; ~60 KB each) captures of every opening shape, all on the
same seed / mission / arena so the geometry is the only difference. Taken with
`SIGHTLINE_SEED=777 SIGHTLINE_MISSION=4 SIGHTLINE_DEPLOY=<shape> SIGHTLINE_SHOT=80`, plus a
`SIGHTLINE_OBJ=defend SIGHTLINE_DEPLOY=envelop` capture on seed 31337 for the surrounded hold.

| file | what it shows |
|---|---|
| `open-frontal.png` | the historical opening — squad cols 0-3, the whole force east. FIRE greyed: no contact on turn 1. |
| `open-pincer.png` | the same board with the force split front + NE flank + SE flank. The squad is NOT concealed at turn 1 and VEGA already has a shot. |
| `open-crossfire.png` | a five-body mass in the NE and a pair in the SE, middle rows empty. |
| `open-envelop.png` | the squad in the centre (cols 7-10) with pods on the west, north and east rims. |
| `defend-surrounded.png` | DEFEND under ENVELOP — the surrounded hold-out, hostiles on the west and east rims of a centre-deployed squad. |
