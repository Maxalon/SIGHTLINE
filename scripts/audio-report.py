#!/usr/bin/env python3
"""SIGHTLINE — audio contact sheet (SANDBOX VERIFICATION TOOL ONLY).

The game NEVER depends on Python. This script only consumes the WAV files that
`SIGHTLINE_AUDIODUMP=1 dotnet run -c Debug` writes into audio_dump/, and renders a
spectrogram contact sheet so a human (or an agent with eyes) can SEE the mix that
nobody has ever heard: where each cue puts its energy, how long its tail really is,
and whether the music beds have any top end at all.

Usage:
    python3 scripts/audio-report.py [audio_dump] [-o audio_dump/contact-sheet.png]

Deps (installed by scripts/dev-setup.sh): numpy matplotlib soundfile
"""
import argparse
import os
import sys

import numpy as np
import soundfile as sf
import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
from matplotlib.colors import LogNorm  # noqa: E402

# The order the game thinks in: weapons, impacts, reactions, UI, stingers, beds.
ORDER = [
    "w_rifle", "w_shotgun", "w_sniper", "w_lmg", "w_smg", "shoot",
    "hit", "crit", "miss", "over", "death",
    "select", "move", "reload", "hunker", "turn",
    "win", "lose",
    "st_kill", "st_lastkill", "st_victory", "st_lose", "st_squadwipe",
    "music_ambient", "music_combat",
]


def spectrogram(x, sr, nfft=1024, hop=256):
    if len(x) < nfft:
        x = np.pad(x, (0, nfft - len(x)))
    win = np.hanning(nfft)
    frames = 1 + (len(x) - nfft) // hop
    out = np.empty((nfft // 2 + 1, frames), dtype=np.float64)
    for i in range(frames):
        seg = x[i * hop:i * hop + nfft] * win
        out[:, i] = np.abs(np.fft.rfft(seg)) ** 2
    return out


def db(v, floor=-110.0):
    return np.maximum(10.0 * np.log10(v + 1e-14), floor)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dump", nargs="?", default="audio_dump")
    ap.add_argument("-o", "--out", default=None)
    args = ap.parse_args()
    out = args.out or os.path.join(args.dump, "contact-sheet.png")

    names = [n for n in ORDER if os.path.exists(os.path.join(args.dump, n + ".wav"))]
    extra = sorted(
        f[:-4] for f in os.listdir(args.dump)
        if f.endswith(".wav") and f[:-4] not in names
    )
    names += extra
    if not names:
        sys.exit(f"no .wav files in {args.dump} — run SIGHTLINE_AUDIODUMP=1 first")

    cols = 5
    rows = (len(names) + cols - 1) // cols
    fig, axes = plt.subplots(rows, cols, figsize=(cols * 3.6, rows * 2.7))
    axes = np.atleast_1d(axes).ravel()
    fig.patch.set_facecolor("#12151a")

    for ax, name in zip(axes, names):
        x, sr = sf.read(os.path.join(args.dump, name + ".wav"))
        if x.ndim > 1:
            x = x.mean(axis=1)
        # music beds are 8s — show only the first 2s so the SFX stay readable
        clip = x[: int(2.0 * sr)] if name.startswith("music_") else x
        P = spectrogram(clip, sr)
        # normalise each panel to ITS OWN peak: absolute FFT magnitudes are meaningless
        # across cues of different length, and the eye wants dynamic range, not level.
        S = db(P / max(P.max(), 1e-14), floor=-80.0)
        peak = 20 * np.log10(max(np.max(np.abs(x)), 1e-9))
        rms = 20 * np.log10(max(np.sqrt(np.mean(x ** 2)), 1e-9))
        k1k = int(round(1000.0 / (sr / 1024.0)))
        hi = float(np.sum(P[k1k:, :]))
        tot = float(np.sum(P[1:, :])) or 1.0
        ax.imshow(
            S, origin="lower", aspect="auto", cmap="magma",
            vmin=-80, vmax=0,
            extent=[0, len(clip) / sr * 1000.0, 0, sr / 2000.0],
        )
        ax.set_title(
            f"{name}  pk{peak:+.1f} rms{rms:+.1f}  >1k {100*hi/tot:.1f}%",
            fontsize=7.5, color="#e6e9ef", pad=3,
        )
        ax.tick_params(labelsize=6, colors="#8b93a3")
        ax.set_ylabel("kHz", fontsize=6, color="#8b93a3")
        ax.set_xlabel("ms", fontsize=6, color="#8b93a3")
        ax.axhline(1.0, color="#4fd1c5", lw=0.6, ls="--", alpha=0.7)  # the 1 kHz line

    for ax in axes[len(names):]:
        ax.axis("off")

    fig.suptitle(
        "SIGHTLINE audio contact sheet — spectrograms (dashed line = 1 kHz)",
        color="#e6e9ef", fontsize=11,
    )
    fig.tight_layout(rect=[0, 0, 1, 0.97])
    fig.savefig(out, dpi=96, facecolor=fig.get_facecolor())
    print(f"wrote {out}  ({len(names)} cues)")


if __name__ == "__main__":
    main()
