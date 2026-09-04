# P22 "NOTHING WITHOUT A SWITCH" — pricing the SUPPLY heal ORDERING

**Base commit `935d719`** (`main`, PROGRAM PARALLAX milestone 14). Branch `wave/unflagged-audit`.
One binary, `runbin/P22` (Release, snapshotted before any chunk ran), two arms one env var apart.

## Why this round is not an inertness round

`SIGHTLINE_HEALFIRST=1` puts THE FORK PAYS' SUPPLY/RECON full heal back *before*
`Run.DebriefSurvivors`' fresh-wound gauge, so nobody who ends a cleared SUPPLY node on their feet
can be wounded by it. SUPPLY is **828 of 5,413 played nodes (15.3%)** in P21's census. The arms are
*expected* to differ; the question is by how much and whether this round can resolve it.

## Cells

Three rungs (h0, h4, h8) x **16 CRN slot bases** (0, 10, … 150) x 20 campaigns x greedy+sloppy
= 40 campaigns per chunk, **48 chunks / 1,920 campaigns per arm, 96 chunks / 3,840 campaigns**.
Heat pinned (`EventCatalog.HeatPinned`, the default). Every chunk asserted by `check_chunk.py`
(the P15 runner of record, copied here): `runs=40`, the rung and slot base the file name claims,
heat pinned, `campaignsRaised = missionsAbovePin = 0`. **48/48 OK and 0 BAD on both arms.**

```bash
bash docs/measurements/p22/round.sh base        # runbin/P22, defaults
bash docs/measurements/p22/round.sh healfirst   # runbin/P22, SIGHTLINE_HEALFIRST=1
python3 docs/measurements/p22/pairs.py base docs/measurements/p22 healfirst docs/measurements/p22
```

## Result 0 — the wave's own code is inert at its shipped defaults

The 32 cells this round shares with P21's round (h0/h4 x 16 bases) are **32/32 byte-identical to
P21's shipped arm** once `harness{}` and `instrument{}` are excluded — different binary, different
commit, same worlds, same outcomes. The new flag defaults off and costs nothing, and the CRN chain
is intact across milestone 14's merge.

## Result 1 — the flag bites, and it is CRN-resolved pooled

| rung | n | shipped | `HEALFIRST=1` | delta | b/c | n_disc | MDE(80%) | McNemar z | chunk t(15) | course changed |
|---|---|---|---|---|---|---|---|---|---|---|
| h0 | 640 | 44.4 | 45.8 | **+1.4** | 11/20 | 31 | 2.44 | +1.62 | +2.33 | 12.5% |
| h4 | 640 | 23.8 | 24.5 | **+0.8** | 15/20 | 35 | 2.59 | +0.85 | +1.23 | 12.0% |
| h8 | 640 | 8.6 | 10.5 | **+1.9** | **0/12** | 12 | 1.52 | **+3.46** | +3.00 | 8.4% |
| **pooled** | **1,920** | **25.6** | **26.9** | **+1.35** | **26/52** | **78** | **1.29** | **+2.94** | — | **11.0%** |

A **positive** delta means the RESTORED (pre-wave) ordering wins more — i.e. **THE FORK PAYS made
the campaign harder by about 1.35 points of run win rate**, which is the direction the change was
designed to have: it removed a hidden subsidy.

**Read it with the same three cautions this project applies to every paired row.**

1. **Only the pooled row and h8 are resolved.** h0 (+1.4 against MDE 2.44) and h4 (+0.8 against
   2.59) are inside their own MDE — absence of evidence, not neutrality (C2's rule). h8 is resolved
   twice over and interestingly so: **all 12 of its discordant pairs go the same way (b=0)**, which
   is what a small, consistent effect on a low base rate looks like.
2. **The chunk-clustered t agrees with the pooled z but is the weaker statistic** — 16 clusters,
   t(15) = +2.33 / +1.23 / +3.00. Nothing here rests on a single slot set.
3. **This is a flag-vs-default contrast on TODAY's tree, not the milestone-4-vs-5 contrast.** It is
   not a price for THE FORK PAYS. L6 priced that wave's *prices* at −0.36 pooled over 1,920 pairs
   (401 discordant, MDE 2.9, unresolved) by building a whole extra tree; this measures one of its
   two halves against the tree as it stands eleven merges later. **Do not add them.**

## Result 2 — the change is BOUNDED, not enormous

**211 of 1,920 paired campaigns (11.0%) take a different course** (win, missions cleared, run
turns, final mission or objective, loss cause) and **0 of 48 chunk pairs are byte-identical**. So
the flag is unambiguously live — but a SUPPLY clear is a minority of nodes and a wound is a
temporary −Aim/−Mobility, not a death, and 89% of campaigns play out identically anyway.

## The audit command (P22's real deliverable)

The wave-time check that would have caught both of THE FORK PAYS' unflagged changes at merge:

```bash
echo "*.cs diff=csharp" > /tmp/attrs
GP="src/Ai.cs src/Combat.cs src/Events.cs src/Grid.cs src/Maps.cs src/Meta.cs src/Mission.cs \
    src/Run.cs src/Terrain.cs src/Unit.cs src/Game.cs src/Game.Modes.cs src/Game.Endless.cs \
    src/Game.Meta.cs src/Anim.cs src/Util.cs"
git -c core.attributesFile=/tmp/attrs diff -U0 <base> HEAD -- $GP | awk '
  /^diff --git/ { f=$3 } /^@@/ { h=$0; sub(/^@@[^@]*@@ ?/,"",h); s=h; next }
  /^[+-]/ { if ($0 ~ /^(\+\+\+|---)/) next
            l=substr($0,2); gsub(/^[ \t]+/,"",l)
            if (l ~ /^(\/\/|\*|\/\*)/ || l=="") next; c[f" :: "s]++ }
  END { for (k in c) printf "%4d  %s\n", c[k], k }' | sort -rn
git diff <base> HEAD -- src/ | grep '^+' | grep -oE 'SIGHTLINE_[A-Z0-9]+' | sort -u
```

Read every group in the first list and answer *"which flag turns this off?"*. The second list is
the flags the wave added. **The first list over-counts** (self-tests live in `Game.Modes.cs`,
`Game.Meta.cs` and `Events.cs` — L6 scored 103 lines and every one was `ModeSelfTest`), which is
exactly why it is grouped by enclosing method instead of counted.
