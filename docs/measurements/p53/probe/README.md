# P53 — THE ONE-CHUNK PROBE, run before the round

CLAUDE.md's P48 rule: **"a gameplay flag read after the `SIGHTLINE_BALANCE` entry point is a flag
that does not exist. Put a board lever with the others, above that read, and PROVE it with a
one-chunk probe before spending an hour."** This is that probe, archived rather than discarded,
because the proof is the point.

Two 6-run chunks at h4/b0, identical but for `SIGHTLINE_ROOMSITE`:

    BIN=runbin/P53 OUT=docs/measurements/p53/probe \
      EXTRA="SIGHTLINE_SITEGLYPHS=1 SIGHTLINE_MAP=4 SIGHTLINE_OBJ=sabotage SIGHTLINE_ROOMSITE=1" \
      bash docs/measurements/p15/run_chunk.sh probe1 4 0 6      # and =0 -> probe0

What it establishes, in the order it matters:

    levers.roomSite     True / False          the dial REACHES the batch and is RECORDED
    levers.siteGlyphs   True / True           both arms keep the glyphs on — not the lever here
    byArena             [4] / [4]             the double force took
    byObjective         Sabotage / Sabotage
    paired legs         12, DISCORDANT 2      the arm MOVES the world, it is not inert plumbing
    missions            51 / 52
    choices/turn        4.05 / 5.009

**The discordant count is the half that matters.** `levers.roomSite` differing only proves the JSON
records the request; two discordant legs out of twelve prove the board the batch actually built is a
different board. A probe that checked only the `levers{}` block would pass on a dial wired to
nothing — which is precisely the P48 failure it exists to prevent, one layer further in.

Win rate read 41.7 on both chunks. **That is n=12 and means nothing either way** — it is recorded
here so nobody later mistakes it for a result.
