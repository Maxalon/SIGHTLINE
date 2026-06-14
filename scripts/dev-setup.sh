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

EOF
