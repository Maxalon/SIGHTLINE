#!/usr/bin/env bash
# C3 — run SIGHTLINE_CLASSTEST eight times back to back. The first version of this gate flaked
# (a cursor left on a node by the previous pass's tooltip step added a class mark to the census,
# and whether it did depended on the seed-dealt map layout), so "it passed once" is not evidence.
set -u
cd "$(dirname "$0")/../../.." || exit 1
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
for i in 1 2 3 4 5 6 7 8; do
  echo -n "$i: "
  SIGHTLINE_CLASSTEST=1 xvfb-run -a -s "-screen 0 1280x800x24" ./bin/Release/net8.0/Sightline 2>/dev/null \
    | grep -oE "CLASSTEST: (PASS|FAIL.*)" | head -1
done
