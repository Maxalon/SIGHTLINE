# P59 — ON A BIG BOARD THE TASK ENDS BEFORE THE FIGHT ARRIVES

**Base commit `9841b0c`** (`main` after P58). Heat PINNED at 0. Runner: `docs/measurements/p15/run_chunk.sh`.
Binary: `runbin/P59/` (gitignored). **36 chunks, 720 campaigns, ARM CHECK PASS on all 36.**

## The question it was built for, and why it could not answer it

P56 gated the hostile HEADCOUNT as board-neutral. That gate cannot see DISTANCE: Defend waves and the
anti-turtle pressure clock's reinforcements drop in at the east edge (`Game.SpawnReinforcements`,
`x = Grid.W - 2`), so on a bigger board they walk further. The round was designed to isolate that:

- a size ladder over **procedural-only** boards (24x15 / 36x22 / 48x30). 18x11 is excluded because it
  plays authored arenas ~80% of the time and would confound terrain with distance;
- three forced objectives: **Defend** and **Hack** (both get east-edge waves) as treatments, and
  **Sabotage** (no clock, no waves) as the control;
- the control's trend subtracted from each treatment's.

`levers.board` was **added in this wave** so a chunk names its own board. `ARM CHECK` asserts it
against the file name, that `byObjective` is exactly the forced objective, and that `byArena` is empty.

## Result — a ceiling, and the ceiling is the finding

    forced objective, heat 0     24-wide          36-wide          48-wide
    Defend  (treatment)          86.4%            99.5%           100.0%
    Hack    (treatment)          82.3%           100.0%            99.7%
    Sabotage (CONTROL)           94.2%            99.0%           100.0%

    campaign win rate            45-95%          100% on every chunk (24 of 24 at 36 and 48 wide)

**Every objective saturates at 36 wide, the control included.** The difference-in-differences
(+7.8 Defend, +11.6 Hack) is therefore NOT a result: the control is capped and cannot rise, which
inflates the gap. **The distance question is unanswered by this round.**

## What the telemetry says happened

    per mission            soldier deaths      enemy acts WITH a shot     enemy contested acts
                           24 -> 36 -> 48       24 -> 36 -> 48             24 -> 36 -> 48
    Defend                 1.57  0.46  0.23     11.9   4.4   3.2           26.5  31.3  30.7
    Hack                   1.00  0.06  0.02      4.0   1.6   1.1            5.4   2.1   1.5
    Sabotage               0.36  0.09  0.01      2.6   1.3   0.5            3.3   1.8   0.8

**The enemy acts but does not shoot.** On Defend it takes ~30 contested actions a mission at every
size, and its turns with a shot fall by more than two thirds: it spends the mission walking. On Hack
and Sabotage the mission is over in ~4 turns — **the task completes before the fight arrives.** The
squad still shoots in about half its turns (it is fighting); the enemy almost never answers.

## The conclusion, and what it does to the plan

**A big board is currently trivially easy, and map geometry alone cannot fix it.** P57 (the depth
spread) and P58 (the zoom floor) made the big board USABLE. But as long as a task mission ends the
moment the task is done, a bigger board only gives the squad more room to finish before contact.

That is precisely what the owner's extraction model supplies (`docs/DESIGN.md` §6.5): the mission does
not end at the terminal — the squad must then reach an exit far enough away to take several turns,
while reinforcements arrive from the half the exit is in. **It is no longer a follow-on to the map
work; the data says it is the thing the map work was waiting for.** The same reinforcement spawner
this round was built to measure is the one extraction needs, so its placement rule is designed once,
there.

Two cautions for anyone reading this later:

- **This does NOT say big boards are easy because of the depth spread.** Not isolated here. The
  spread's own arm (`SIGHTLINE_DEPTHSPREAD=0`) would be the control for that, and was not run.
- **Nothing here is comparable with the 18x11 heat band** — forced objective, procedural-only, and a
  different board: a different game on three axes at once.

## Reproducing

    BIN=runbin/P59 OUT=docs/measurements/p59 \
      EXTRA="SIGHTLINE_OBJ=hack SIGHTLINE_BIGMAP=36x22" \
      bash docs/measurements/p15/run_chunk.sh hack-s36-h0-b0 0 0 10
    # over obj in {defend,hack,sabotage}, size in {24x15,36x22,48x30}, base in {0,10,20,30}, then:
    python3 docs/measurements/p59/analyse.py docs/measurements/p59
