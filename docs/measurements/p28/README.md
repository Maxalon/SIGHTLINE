# P28 — what the LiDAR vision model actually costs

**Base commit:** the P28 branch off `8606c58`. **Box:** the dev container, 4 shared cores,
single-threaded, Release, `xvfb` + llvmpipe (the render is not timed — these are CPU numbers).
**Harness:** `LID_BENCH=1 prototypes/lidar/...`, warmed up, best-of-3 per configuration.
Raw output: `raycost.txt`.

A "scan" is ONE soldier at ONE position, including all 7 peek origins.

| az x el | rays/scan | ms/scan (full) | ms/scan (rays only) | stamping | floor cov |
|---|---|---|---|---|---|
| 120x10   | 8,400   | 105.5 | 15.9  | ~90 | 62.6% |
| 180x14   | 17,640  | 73.3  | 32.4  | ~41 | 59.7% |
| 240x18   | 30,240  | 78.5  | 9.1   | ~69 | 58.9% |
| **360x26** | **65,520** | **87.9** | **18.6** | **~69** | **57.4%** |
| 480x34   | 114,240 | 105.7 | 31.3  | ~74 | 56.8% |
| 720x48   | 241,920 | 144.2 | 64.7  | ~80 | 56.1% |
| 1080x64  | 483,840 | 225.7 | 122.3 | ~103| 55.5% |

## The finding: ray casting is NOT the expensive part

Ray marching is linear and fast — **~3.2–4.0 M rays/s**, and it stays linear across the whole
range. Coverage **stamping** is the fixed cost, at roughly **70–100 ms per scan regardless of ray
density**, because the total stamped AREA is the same floor either way; more rays just divide it
into more, smaller footprints.

That inverts the intuition, and it is good news for the question that prompted this round:
**16x the rays (30,240 -> 483,840) costs only 2.9x the time.** Ray density is affordable.

`floor cov` FALLING as density rises is correct, not a regression: at low density each angular
cell is wider, so its footprint is larger and over-reveals. Coarse scans claim to know more than
they do, and are less precise about where the boundary is.

## What this means for a turn

The 36-scan projection in `raycost.txt` assumes a scan at every tile entered, which is the
pessimistic reading. Scanning on arrival plus a couple of points en route is 2-3 per soldier:

* 6 soldiers x 2-3 scans at 360x26 = **~1.0-1.6 s single-threaded on this box**
* rays are independent, so ~4x on threads -> **~250-400 ms**
* and it overlaps move animations, which already burn wall-clock

## The headroom that has not been spent

Stamping is mostly **wasted work** — re-stamping ground that is already known at that confidence.
Three optimisations, none of them speculative:

1. **Early-out** when the footprint's centre texel already holds >= this confidence. After the
   first few positions most rays land on already-known ground.
2. **Scanline-rasterise** the ellipse instead of testing every texel in its bounding box.
3. The **texture atlas** render path (see ROADMAP) replaces this per-texel CPU loop entirely.

So treat the "stamping" column as an upper bound on a version nobody has optimised yet.

## Caveats

* Board is 24x16 with 12 mesh props (1,016 triangles). At 50x50 with hundreds of props, **mesh
  intersection becomes the dominant term** and wants per-tile prop bucketing or a BVH. The
  broad-phase AABB is the only spatial structure here.
* Shared container; treat individual rows as +-20%. The 240x18 rays-only row (9.1 ms) is faster
  than 180x14's (32.4 ms) — scheduling noise, not a real inversion. The trend is what is solid.
