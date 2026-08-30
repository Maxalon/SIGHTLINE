#!/usr/bin/env python3
"""C1 inertness diff — W1's `inert_diff.sh` idea, applied per-chunk.

Compares two chunk JSONs field-by-field EXCLUDING the `harness` block (which records
nproc/loadavg/elapsed and is DESIGNED to vary) and the `instrumentHealth.*` wall-clock
fields. Prints every leaf path whose value differs. Empty output == the two batches played
identical worlds to identical outcomes, i.e. the change under test is gameplay-inert on
those rungs.

Usage: inert_diff.py <prefixA> <prefixB> <heat> [heat...]
"""
import json, os, sys, glob

HERE = os.path.dirname(os.path.abspath(__file__))
SKIP_TOP = {"harness"}


def leaves(o, p=""):
    if isinstance(o, dict):
        for k, v in o.items():
            if p == "" and k in SKIP_TOP:
                continue
            yield from leaves(v, f"{p}.{k}" if p else k)
    elif isinstance(o, list):
        for i, v in enumerate(o):
            yield from leaves(v, f"{p}[{i}]")
    else:
        yield p, o


def main():
    A, B = sys.argv[1], sys.argv[2]
    heats = [int(x) for x in sys.argv[3:]] or [0, 2, 4, 6, 8]
    total_fields = total_diff = 0
    for h in heats:
        hdiff = hfields = nch = 0
        shown = []
        for fa in sorted(glob.glob(os.path.join(HERE, f"{A}-h{h}-b*.json"))):
            fb = fa.replace(f"{A}-h", f"{B}-h").replace(os.path.basename(fa).split("-h")[0], B)
            fb = os.path.join(HERE, os.path.basename(fa).replace(A + "-", B + "-", 1))
            if not os.path.exists(fb):
                print(f"  MISSING {os.path.basename(fb)}")
                continue
            nch += 1
            la = dict(leaves(json.load(open(fa))))
            lb = dict(leaves(json.load(open(fb))))
            keys = set(la) | set(lb)
            hfields += len(keys)
            for k in sorted(keys):
                if la.get(k, "<absent>") != lb.get(k, "<absent>"):
                    hdiff += 1
                    if len(shown) < 12:
                        shown.append(f"    {os.path.basename(fa)}  {k}: {la.get(k)} -> {lb.get(k)}")
        total_fields += hfields
        total_diff += hdiff
        print(f"h{h}: {nch} chunk pairs, {hfields} leaf fields compared, {hdiff} DIFFER"
              + ("  <- INERT" if hdiff == 0 else ""))
        for s in shown:
            print(s)
    print(f"TOTAL: {total_fields} fields, {total_diff} differ")


if __name__ == "__main__":
    main()
