# W8 — THE HALF WALL: the mid-run Decapitate, and what the cross-tab actually found

**Base commit: `ee5a85c`** (wave W8's instrument), branched from `dfa7c0f` — the integration tip
carrying **W1 TRUE INSTRUMENT**, **TRUE BAND** and the L1/L2 ladders. The instrument commit is
**proven gameplay-identical** to that tip (see INERTNESS below), so **L2's ladder is this wave's
baseline** and round B reproduces it exactly.

Binary snapshots (gitignored): `runbin/W8pre` = `dfa7c0f` as-built; `runbin/W8inst2` = `ee5a85c`.

## HOW EVERY NUMBER HERE WAS PRODUCED

`run_chunk.sh` is W1's three-layer chunk runner re-pointed here, plus the three HVT dials in its
env passthrough. It (a) `rm -f`s the target JSON first, (b) checks the process exit code (2 = the
no-display refusal), and (c) asserts the JSON's own `runs == 2N`. **Every chunk below printed
`OK ... runs=20`; zero `BAD`.**

```bash
# one chunk: <tag> <heat> <slot base> [N]   (N=10 => 20 campaigns: greedy + sloppy)
BIN=runbin/W8inst2 bash docs/measurements/w8/run_chunk.sh B-h0-b0 0 0 10

# round B  — UNPINNED, 6 rungs (hR=-1, h0/2/4/6/8) x 8 slot sets (0..70 step 10) x N=10
#            = 48 chunks, 960 campaigns. Same grid as L2, so it is directly comparable.
# round P0 — Decapitate-PINNED baseline: OBJ=decapitate, 6 rungs x 4 slot sets (0/10/20/30)
#            = 24 chunks, 480 campaigns.
# round P1 — Decapitate-PINNED lever:    OBJ=decapitate HVTDEPTH=-1, same 24 chunks.
OBJ=decapitate HVTDEPTH=-1 BIN=runbin/W8inst2 bash docs/measurements/w8/run_chunk.sh P1-h0-b0 0 0 10
```

## INERTNESS — the instrument is bookkeeping and nothing else

`I1-pre-*` vs `I1-post-*`: the same three (rung, slot-set) chunks — h0/b0, h4/b10, h8/b20, 60
campaigns per arm — run on `runbin/W8pre` and `runbin/W8inst2`. **42 of 43 aggregate JSON fields
are byte-identical on all three pairs.** The only field that moves is `harness{}`, which records
`startedUtc`, `loadAtStart/End` and elapsed seconds and is designed to vary. The three new keys
(`byObjectiveByNodeKind`, `byObjectiveByMission`, `hvt`) exist only on the post side.

Round B is the second, much larger inertness proof: 960 campaigns on the new instrument reproduce
L2's six-rung ladder and its whole `byObjective` table exactly.

## FILES

`<tag>.json` (machine record), `<tag>.report.txt` (the aggregator's printed report) and a
force-added `<tag>.log` per chunk. `I0-*` are an earlier inertness pair against a mid-wave
instrument and are kept for provenance; `D0-*` is a mid-wave diagnostic grid on that same
mid-wave instrument (identical win rates to B, without the depth cells).
