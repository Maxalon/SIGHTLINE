#!/usr/bin/env bash
# Dev/CI-less setup for the SIGHTLINE project in an ephemeral Linux container.
# Installs the .NET 8 SDK plus the bits needed to BUILD and to RUN headlessly
# (Xvfb + software OpenGL) so the game can be smoke-tested without a display.
#
# Usage:  bash scripts/dev-setup.sh
# Then:   export PATH="$PATH:/usr/lib/dotnet"
#
# This script is for the development sandbox only. End users just need the
# .NET 8 SDK from https://dotnet.microsoft.com/download and `dotnet run`.
set -euo pipefail

echo ">> apt update"
sudo apt-get update -y || true

echo ">> installing .NET 8 SDK"
sudo apt-get install -y dotnet-sdk-8.0

echo ">> installing headless GL + screenshot tools (for verification only)"
sudo apt-get install -y xvfb libgl1-mesa-dri libglu1-mesa imagemagick

# SANDBOX-ONLY audio verification tooling (PROGRAM RESONANCE A1 "THE EAR").
# scripts/audio-report.py turns the WAVs written by SIGHTLINE_AUDIODUMP=1 into a
# spectrogram contact sheet. The GAME NEVER DEPENDS ON PYTHON — this is a verification
# tool for the sandbox only, and a failure here must not fail setup.
echo ">> installing audio-analysis tooling (sandbox verification only; the game needs none of it)"
python3 -c "import numpy, matplotlib, soundfile" 2>/dev/null \
  || pip3 install --quiet --break-system-packages numpy matplotlib soundfile \
  || sudo apt-get install -y python3-numpy python3-matplotlib python3-soundfile \
  || echo "   (skipped: audio contact sheet unavailable — SIGHTLINE_AUDIODUMP/AUDIOGATE still work)"

export PATH="$PATH:/usr/lib/dotnet"
echo ">> dotnet version: $(dotnet --version)"
echo ">> restoring + building (Release)"
dotnet build -c Release

cat <<'EOF'

Setup complete. Quick reference:

  Build:   export PATH="$PATH:/usr/lib/dotnet"; dotnet build -c Release
  Run:     dotnet run -c Release                 # needs a real display

  Headless screenshot (writes sightline_shot.png):
    export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe SIGHTLINE_SHOT=90
    xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug

  Headless full-match smoke test (prints RESULT: WIN/LOSE):
    export LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe SIGHTLINE_AUTOPLAY=1
    xvfb-run -a -s "-screen 0 1280x800x24" dotnet run -c Debug

  Audio (device-free; no window, no display needed):
    SIGHTLINE_AUDIOGATE=1 dotnet run -c Debug        # PASS/FAIL vs the committed budget
    SIGHTLINE_AUDIODUMP=1 dotnet run -c Debug        # measurement table + audio_dump/*.wav
    python3 scripts/audio-report.py                  # -> audio_dump/contact-sheet.png

EOF
