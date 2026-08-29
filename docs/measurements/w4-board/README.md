# W4 "THE BOARD BECOMES A PLACE" — the GAMEPLAY-INERTNESS chunks

This is **not a balance round**. W4 is a rendering wave and it must not move a gameplay number;
these two chunks are the proof that it does not.

**Base commit: `d350416`** (the wave's branch point).
**Wave commit: `f68d3fd`** (`wave/board-as-place`).

Both chunks were run per the `CLAUDE.md` `SIGHTLINE_BALANCE` measurement contract: the **Release
binary run directly** from a gitignored `runbin/<tag>/` snapshot, **under `xvfb-run`**, with
`XDG_CONFIG_HOME` and `SIGHTLINE_BALANCE_JSON` pinned per chunk, and the JSON's **own `runs`
field asserted** afterwards (`runs=10` in both — 5 slots × greedy+sloppy).

The slot base is **140** because slots 0–139 were owned by other agents' rounds in the same
container at the time.

```bash
# per chunk (tag = w4base on d350416, w4after on f68d3fd)
export PATH="$PATH:/usr/lib/dotnet" LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe
mkdir -p "$PWD/.xdg-$TAG" runbin/"$TAG" bal
export XDG_CONFIG_HOME="$PWD/.xdg-$TAG" SIGHTLINE_BALANCE_JSON="$PWD/bal/$TAG.json"
dotnet build -c Release
cp -r bin/Release/net8.0/. runbin/"$TAG"/
SIGHTLINE_BALANCE=5 SIGHTLINE_BALANCE_BASE=140 SIGHTLINE_BALANCE_HEAT=0 \
  xvfb-run -a -s "-screen 0 1280x800x24" runbin/"$TAG"/Sightline > "bal/$TAG.log" 2>&1
python3 -c "import json;print(json.load(open('bal/$TAG.json'))['runs'])"   # must print 10
```

## RESULT

```
python3 -c "import json; a=json.load(open('w4base.json')); b=json.load(open('w4after.json')); \
            print(a['runs'], b['runs'], 'IDENTICAL' if a==b else 'DIFFERENT')"
-> 10 10 IDENTICAL
```

The two summary documents are **field-for-field identical** — checked by a full recursive diff of
the parsed JSON trees, not by eyeballing the headline numbers. Together with
`SIGHTLINE_PAIRTEST: PASS` (byte-identical CRN legs) on the wave commit, this is the wave's claim
that it moved no gameplay number.

**What this chunk does NOT establish.** n=10 is far too small to say anything about balance, and
it is not trying to: identity of two summaries over the *same* pinned slots is a determinism
check, not a win-rate measurement. The ladder of record is unchanged and remains X2's
(`docs/measurements/x2/`). Nothing here should ever be quoted as a win-rate.
