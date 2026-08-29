# RESONANCE W6 — key-rebinding evidence

Base commit: the W6 branch tip at the time of the run (see `git log wt-w6`).
Binary: `bin/Release/net8.0/Sightline`, driven live under a real `Xvfb` display with
`xdotool` — NOT the screenshot harness.

## The method note that makes these logs mean anything

`xdotool key X` presses **and releases** the key inside a single frame. Raylib's
`IsKeyPressed` compares this frame's key state against the previous frame's, so it never
observes such a tap — only the key **queue** (`GetKeyPressed`) does. The same is true of
`xdotool click 1` against `IsMouseButtonPressed`. Every press in these scripts is therefore
`keydown` / `sleep 0.35` (≈21 frames at 60 fps) / `keyup`, and every click is
`mousedown` / `sleep 0.35` / `mouseup`. R1 lost two runs to this; the scripts are archived so
nobody loses a third.

`SIGHTLINE_KEYLOG=1` makes `Keymap.Pressed` print `KEYACT <action-id> <KEY>` the moment a
bound action actually fires, so the logs record **which action a physical key reached**.

## 1. `keylog-baseline.log` — the shipped defaults

`bash evid-keys.sh baseline "" 1 q 2` — presses, in order: `1`, `q`, `2`.

    KEYACT shoot One
    KEYACT overwatch Two

`1` → FIRE, `2` → OVERWATCH, `q` → nothing (unbound by default). Baseline established.

## 2. `keylog-rebound.log` — two rows moved, via `SIGHTLINE_KEYBIND`

`bash evid-keys.sh rebound "shoot=Q;hunker=I" 1 q 3 i` — presses, in order:
`1` (old FIRE), `q` (new FIRE), `3` (old HUNKER), `i` (new HUNKER).

    KEYBIND: shoot -> Q OK
    KEYBIND: hunker -> I OK
    KEYACT shoot Q
    KEYACT hunker I

Exactly two actions fired, from the two NEW keys. **The old keys produced nothing** — `1` and
`3` are dead, which is the half of the claim that a naive test would miss.

## 3. `keylog-persisted.log` — rebound through the real UI, then a fresh process

`bash evid-ui-rebind.sh`. Launch 1: intro → `[O]` CONTROLS → click the FIRE row (held) → hold
`Q`. The screen showed `PRESS A KEY` while armed and `Q` after (screenshots taken in-run).
`display.json` afterwards:

    "UiScaleIdx":1,"Keys":"shoot=Q"

Launch 2 is a **fresh process with no environment override** — the map comes only off disk.
Presses, in order: `1` (old FIRE), `q` (new FIRE), `2` (untouched OVERWATCH).

    KEYACT shoot Q
    KEYACT overwatch Two

The persisted rebind survives the round trip, the old key stays dead, and an untouched row
is unaffected.

## Re-running

Both scripts pin `XDG_CONFIG_HOME` to the worktree and use their own Xvfb display (`:97` /
`:96`), so they can run beside other agents. `evid-ui-rebind.sh` WRITES a settings file at
`$XDG_CONFIG_HOME/Sightline/display.json` — delete it afterwards or the `shoot=Q` override
persists into later runs in that worktree.

## 4. The pause card, driven with the keyboard only (`evid-pause-keyboard.sh`)

Every comfort setting in the game lives on the pause card, and until W6 all of them were
mouse-only (a 320×42 plate to click, or a 304px fader track to drag). The route below uses
**no mouse at all**:

    intro -> [N] TRAINING OP -> [Esc] pause -> [Down] x11 -> [Enter] -> [Down] x2 -> [Left] x2

Result, read back out of `display.json`:

    UiScaleIdx = 2   (TEXT SIZE 100% -> 110%, by Enter on the selected row)
    VolMaster  = 0.5 (0.60 -> 0.50, by two Left nudges on the MASTER fader)

`pausekb_sel.png` (taken in-run) shows the selection ring + caret parked on TEXT SIZE after
the eleventh `[Down]`. The run also exercises the strand guarantee end to end: `[Esc]` is what
opened the card.

## 5. Colour-vision measurement (analysis, not a game run)

`cvd-palette.py` simulates the shipped palette through the Viénot-1999 dichromat matrices and
prints every pairwise separation for the normal and colorblind palettes. It is what found the
regression the W6 colorblind change fixes; re-run it after touching any `Pal` accent.
