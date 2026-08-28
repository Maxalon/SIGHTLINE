#!/usr/bin/env python3
"""SIGHTLINE board-region image metrics (manual QA tool — NOT wired to anything).

Two measurements, both taken over the BOARD RECT ONLY (Cfg.OriginX/Y + BoardW/H,
minus the three HUD panels drawn over it) so chrome never pollutes the numbers:

  hue   — chroma-weighted circular mean hue of the board's LEFT third (where the
          move overlay lives, because the squad deploys left) vs its RIGHT third
          (clean board). Weighting by S*V keeps near-black pixels, whose hue is
          numerical noise, from dominating a plain average. A large L/R gap means
          the overlay has repainted a third of the room in its own hue; a small
          gap means the biome survives the player turn.
          Also reports CYAN%, the share of board pixels in hue 175..215 with
          S>0.25 — the direct "how much of the room is friendly-blue" number.

  luma  — Rec.601 luma histogram (min / median / p95 / max) over the same board
          rect. RESONANCE V2 target: median ~65, p95 ~150, three visible zones.

usage:  python3 scripts/board-metrics.py hue  shots/*.png
        python3 scripts/board-metrics.py luma shots/*.png
"""
import sys, os
import numpy as np
from PIL import Image

# Board rect is x 64..1216 / y 40..744 (Cfg.OriginX/Y + BoardW/H), but three HUD
# panels are drawn OVER it: the roster strip (x<145), the action bar + unit card
# (y>660) and the top bar (y<62). Measure the clean interior only, or the metrics
# are reporting chrome instead of board.
X0, Y0, X1, Y1 = 145, 62, 1216, 660         # board rect minus HUD overlap

def board(path):
    a = np.asarray(Image.open(path).convert('RGB'))[Y0:Y1, X0:X1]
    return a

def circmean(h, w):
    s = (np.sin(np.radians(h)) * w).sum()
    c = (np.cos(np.radians(h)) * w).sum()
    return np.degrees(np.arctan2(s, c)) % 360

def circmedian(h):
    """Circular MEDIAN hue over a 5-degree histogram: the hue of the TYPICAL pixel.
    Unweighted and robust — unlike the chroma-weighted mean it cannot be swung by a
    handful of highly-saturated strokes sitting on a near-grey floor (ASH), which is
    exactly the case where the mean misreports."""
    cnt = np.bincount((h.ravel() / 5.0).astype(np.int32) % 72, minlength=72).astype(np.float64)
    ctr = np.arange(72) * 5.0 + 2.5
    d = np.abs(ctr[:, None] - ctr[None, :]); d = np.minimum(d, 360 - d)
    return float(ctr[np.argmin(d @ cnt)])

def hue_row(path):
    b = board(path)
    hsv = np.asarray(Image.fromarray(b).convert('HSV')).astype(np.float32)
    H = hsv[..., 0] * 360.0 / 255.0
    S = hsv[..., 1] / 255.0
    V = hsv[..., 2] / 255.0
    W = b.shape[1]
    L = slice(0, W // 3); R = slice(2 * W // 3, W)
    hl = circmean(H[:, L], (S * V)[:, L])
    hr = circmean(H[:, R], (S * V)[:, R])
    d = abs(hl - hr); d = min(d, 360 - d)
    cyan = float(((H >= 175) & (H <= 215) & (S > 0.25)).mean() * 100.0)
    ml, mr = circmedian(H[:, L]), circmedian(H[:, R])
    dm = abs(ml - mr); dm = min(dm, 360 - dm)
    return hl, hr, d, dm, cyan

def luma_row(path):
    b = board(path).astype(np.float32)
    y = 0.299 * b[..., 0] + 0.587 * b[..., 1] + 0.114 * b[..., 2]
    f = y.ravel()
    return f.min(), np.median(f), np.percentile(f, 95), f.max(), float((f > 180).mean() * 100)

def name(p):
    return os.path.basename(p).rsplit('.', 1)[0]

def main():
    mode = sys.argv[1]
    files = sys.argv[2:]
    if mode == 'hue':
        print(f"{'shot':<26}{'hueL':>7}{'hueR':>7}{'dHue':>7}{'dMed':>7}{'cyan%':>8}")
        for p in files:
            hl, hr, d, dm, c = hue_row(p)
            print(f"{name(p):<26}{hl:7.0f}{hr:7.0f}{d:7.0f}{dm:7.0f}{c:8.1f}")
    elif mode == 'luma':
        print(f"{'shot':<26}{'min':>6}{'median':>8}{'p95':>7}{'max':>6}{'>180%':>8}")
        for p in files:
            mn, md, p95, mx, hi = luma_row(p)
            print(f"{name(p):<26}{mn:6.0f}{md:8.0f}{p95:7.0f}{mx:6.0f}{hi:8.2f}")
    else:
        print(__doc__); sys.exit(2)

main()
