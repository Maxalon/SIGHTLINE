# WAVE X1 "THE EXCHANGE" — archived measurement artifacts

Every chunk's balance-report log and JSON summary, as run. Kept because measurement rounds
are expensive and the sandbox container is ephemeral; the write-up in
[`docs/DEVLOG.md` §RESONANCE X1](../../DEVLOG.md) quotes only numbers that appear here.

**How every chunk was run** (`run_chunk.sh`, archived here):

```bash
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
export XDG_CONFIG_HOME="$PWD/.xdg" SIGHTLINE_BALANCE_JSON="$PWD/balance.json"
dotnet build -c Release
SIGHTLINE_BALANCE=10 SIGHTLINE_BALANCE_HEAT=<h> SIGHTLINE_BALANCE_BASE=<0|10> \
  SIGHTLINE_BALANCE_JSON=<out.json> \
  xvfb-run -a -s "-screen 0 1280x800x24" ./bin/Release/net8.0/Sightline > <out.log>
```

`xvfb-run` is mandatory: without a display `SIGHTLINE_BALANCE` prints `runs=0 / (no data)`,
still claims N matches and exits 139 — a silent zero-data batch. `run_chunk.sh` asserts
`runs=20` in every log and prints OK/BAD; every chunk used here printed **OK**.

Two disjoint CRN slot sets (`BASE` 0 and 10) x greedy+sloppy = **40 campaigns per rung**.
`XDG_CONFIG_HOME` and `SIGHTLINE_BALANCE_JSON` are pinned into the worktree because several
dev agents share the container and the harness otherwise defaults to a shared `/tmp` path.

| tag | state | rung |
|---|---|---|
| `R0-h{0,4,8}-b{0,10}` | baseline (no lever) | h0 / h4 / h8 |
| `R0diag-h{0,4}-b0` | baseline + the shot-gate instrumentation (logic-identity check: per-slot records match `R0-*` exactly) | h0 / h4 |
| `R1-h0-b*` | HostileToughness +4, no trim | h0 |
| `R2-h0-b*` | +4 / trim −1 | h0 |
| `R3-h0-b*` | +4 / trim −2 (reverted) | h0 |
| `R4-T2D1-h{0,4}-b*` | +2 / trim −1 | h0 / h4 |
| `R5-T3D1-h{0,4}-b*`, `SHIP-h8-b*` | **+3 / trim −1 — SHIPPED** | h0 / h4 / h8 |

`agg.py` pools chunks; `table.py` regenerates the DEVLOG round table verbatim:
`python3 table.py` from a directory holding a `runs/` symlink to this one.
