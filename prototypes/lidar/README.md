# prototypes/lidar — the scanned-hologram vision model

**This is a PROTOTYPE, not engine code.** It is excluded from `Sightline.csproj`
(`<Compile Remove="prototypes/**" />`) and builds as its own project. Nothing in `src/` depends
on it. It exists because it answered a pile of design questions that would otherwise have to be
re-answered from scratch, and because the container it was built in is ephemeral.

## What it establishes

The premise: the player is an operator at HQ looking at a **holographic reconstruction** built
from what the squad's own LiDAR has actually scanned. Three tiers — never seen (nothing drawn),
scanned earlier (remembered), in sightline now (bright).

1. **Walls live on tile EDGES**, not on tiles. A soldier holds either face. `World.EV` / `World.EH`
   store each edge exactly once, so the two sides can never disagree.
2. **The unit of knowledge is a FACE.** A wall scanned from one side renders as a single plane —
   the operator cannot read its thickness, because the data is not there.
3. **A ray is a CELL OF SOLID ANGLE, not a line.** What it reveals is the AREA its footprint
   covers. Surfaces then render their OWN material through a coverage mask, so ray budget moves
   the sharpness of the reveal boundary and never the look of the geometry.
4. **Peek origins.** Rays leave from a ring over the soldier's own footprint (radius 0.34 against
   a half-tile of 0.5, so a lean can never reach through a wall), not from a point at their centre.
   Measured: +43% of the world revealed for the same ray budget.
5. **Props are real meshes.** Ray-vs-triangle with per-triangle coverage — no UV unwrap, works for
   any shape. A curved surface self-terminates: you get the near hemisphere and nothing behind it.

## Running it

```bash
dotnet build -c Release prototypes/lidar/lidar.csproj
export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
# a scan + a render:   <kit dir> <out.png> [nAz] [nEl] [fovy tgx tgz yaw pitch]
xvfb-run -a -s "-screen 0 1500x900x24" \
  prototypes/lidar/bin/Release/net8.0/lidar assets/props out.png 420 40
```

| env | effect |
|---|---|
| `LID_BENCH=1`    | the cost curve (warmed up, best-of-3), then exit |
| `LID_NOSTAMP=1`  | disable coverage stamping — isolates ray-marching cost |
| `LID_ORIGINS=n`  | peek origins: 1 = centre only, 7 = default ring |
| `LID_SCEN=door`  | a single scan position beside the doorway instead of a walked path |

## Four bugs it already found, so they are not re-found

- **Returns recorded on the edge LINE, not the struck FACE.** The renderer draws faces at
  `x ± T/2`, so returns sat inside the slab and read through the unscanned side. `World.WallT`
  is now one constant both the ray model and the renderer use.
- **Material sampled once per mask cell**, which made the scan's mask resolution set the
  texture's grain. Fixed properly only by the atlas (see ROADMAP) — the per-cell quad render
  here is a shortcut that still leaks it.
- **The floor was 384 per-tile masks** and `Stamp` clipped each footprint at its tile, so the
  cone edge broke on grid lines. It is one continuous world-space grid now.
- **Isotropic footprints + a clamp.** A scan cell's ground footprint is an ELLIPSE whose
  along-ground extent IS the spacing to the next ring; clamping a circle destroyed the property
  that makes footprints tile the ground, and far coverage fell apart into rings.
  Related: uniform-ANGLE elevation sampling cannot look closer than `h/tan(elMax)` — a 3.63-tile
  blind circle around the soldier. Downward rays are placed by ground radius now.

All four are the same class: **an indexing or approximation convention leaking into appearance.**
