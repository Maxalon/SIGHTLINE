#!/usr/bin/env python3
"""RESONANCE W6 — colour-vision separation for SIGHTLINE's semantic palette.

Simulates every accent through the Vienot-1999 dichromat matrices and prints the pairwise
RGB separation for the NORMAL palette and the COLORBLIND palette side by side. A pair under
~40 is a real risk of two different meanings reading as one colour.

This is the measurement that found the W6 regression: the colorblind swap moved the hostile
hue INTO the amber band the objective accent already occupies, so for a deuteranope
Foe/Accent went 50.5 (normal) -> 39.8 (colorblind) — turning the accessibility option ON made
that pair WORSE. Keep the constants below in sync with src/Util.cs and re-run after any
accent change.
"""
import math

M = {'none': [[1, 0, 0], [0, 1, 0], [0, 0, 1]],
     'deut': [[0.625, 0.375, 0], [0.700, 0.300, 0], [0, 0.300, 0.700]],
     'prot': [[0.567, 0.433, 0], [0.558, 0.442, 0], [0, 0.242, 0.758]],
     'trit': [[0.950, 0.050, 0], [0, 0.433, 0.567], [0, 0.475, 0.525]]}


def sim(c, k):
    m = M[k]
    return tuple(max(0., min(255., m[i][0] * c[0] + m[i][1] * c[1] + m[i][2] * c[2])) for i in range(3))


def d(a, b):
    return math.dist(a, b)


# --- keep in sync with Pal (src/Util.cs) ---
NORM = {'Friend': (56, 189, 248), 'Foe': (248, 113, 113), 'Good': (74, 222, 128),
        'Accent': (251, 191, 36), 'Suspect': (245, 184, 64), 'VipGold': (245, 200, 70),
        'Elite': (255, 140, 90)}
CB = dict(NORM)
CB['Foe'] = (255, 120, 0)      # W6 (was 238,138,40)
CB['Good'] = (40, 200, 168)

# biome floors: (FloorA+FloorB)/2 per Biome.All, plus two floors sampled off a lit board
FLOORS = {'STEEL': (25, 34, 46), 'ARID': (48, 39, 24), 'TUNDRA': (30, 43, 54),
          'VERDANT': (23, 42, 28), 'ASH': (38, 34, 33), 'VOID': (33, 27, 50),
          'NEON': (17, 35, 41), 'MAGMA': (39, 25, 21),
          'MAGMA_lit': (68, 55, 52), 'ARID_lit': (120, 93, 86)}

PAIRS = [('Foe', 'Friend'), ('Foe', 'Good'), ('Foe', 'Accent'), ('Foe', 'Suspect'),
         ('Foe', 'VipGold'), ('Foe', 'Elite'), ('Good', 'Accent'), ('Good', 'Friend'),
         ('Accent', 'Suspect'), ('Accent', 'VipGold')]

for k in M:
    print(f"\n=== {k} ===   (a pair under 40 risks reading as one colour)")
    print(f"{'pair':22s} {'NORMAL':>9s} {'CB':>9s}")
    for a, b in PAIRS:
        dn, dc = d(sim(NORM[a], k), sim(NORM[b], k)), d(sim(CB[a], k), sim(CB[b], k))
        flag = '  <-- CB COLLIDES' if dc < 40 else ('  <-- NORMAL collides' if dn < 40 else '')
        print(f"{a + '/' + b:22s} {dn:9.1f} {dc:9.1f}{flag}")

print("\n=== worst separation of the FOE accent, across every simulation ===")
for name, foe in [('normal  Foe', NORM['Foe']), ('CB      Foe', CB['Foe'])]:
    wu = min(d(sim(foe, k), sim(c, k)) for k in M for n, c in (NORM if 'normal' in name else CB).items() if n != 'Foe')
    wf = min(d(sim(foe, k), sim(c, k)) for k in M for c in FLOORS.values())
    print(f"  {name}  vs other UI accents {wu:6.1f}   vs the darkest biome floors {wf:6.1f}")
