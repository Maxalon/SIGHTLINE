#!/usr/bin/env python3
"""Crop each finale screenshot's HUD strip and stack BEFORE/AFTER into one evidence image."""
import sys, os
from PIL import Image, ImageDraw

src, out = sys.argv[1], sys.argv[2]
CROP = (500, 4, 1118, 40)          # FINALE .. SQUAD/hostile chip .. HEAT chip
W, H = CROP[2] - CROP[0], CROP[3] - CROP[1]
SCALE = 2
LAB, HDR, PAD = 96, 52, 6
cols = ["before", "after"]
rows = [str(h) for h in range(9)]
cw, ch = W * SCALE, H * SCALE
img = Image.new("RGB", (LAB + len(cols) * (cw + PAD) + PAD, HDR + len(rows) * (ch + PAD) + PAD + 26),
                (10, 10, 14))
d = ImageDraw.Draw(img)
d.text((10, 10), "P23 THE APEX BITES - the finale force at every rung, seed 4242, SIGHTLINE_MISSION=6", fill=(235, 235, 240))
d.text((10, 24), "the pink chip is the hostile count; the box on the right is the rung", fill=(150, 150, 160))
for ci, c in enumerate(cols):
    title = "BEFORE (CLAMPLAST=0 FINALESTAT=0)" if c == "before" else "AFTER (shipped)"
    d.text((LAB + ci * (cw + PAD) + 4, HDR - 14), title, fill=(255, 180, 170) if c == "before" else (170, 255, 190))
for ri, r in enumerate(rows):
    y = HDR + ri * (ch + PAD) + 6
    d.text((10, y + ch // 2 - 6), f"HEAT {r}", fill=(200, 200, 210))
    for ci, c in enumerate(cols):
        p = os.path.join(src, f"{c}-h{r}.png")
        im = Image.open(p).crop(CROP).resize((cw, ch), Image.NEAREST)
        img.paste(im, (LAB + ci * (cw + PAD), y))
d.text((10, img.height - 20),
       "BEFORE stops growing at HEAT 4 (6/7/7/8/9/9/9/9/9). AFTER: 6/7/7/8/9/10/10/10/11 - rungs 5 and 8 land the body they declare.",
       fill=(180, 180, 190))
img.save(out)
print(out, img.size)
