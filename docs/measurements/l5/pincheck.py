#!/usr/bin/env python3
"""L5 — THE PIN'S CONTRACT, per campaign: a campaign that never took a heat-raising arm must be
IDENTICAL with the pin on and off; a campaign that did may differ, and only from its leak on.

Pairs two chunks (same slots, one with SIGHTLINE_HEATPIN=0, one pinned) by (slot, policy, heat)
from their `campaigns[]` rows and checks, for every row whose LEAKY leg has heatRaisingPicks == 0:
win, missionsCleared, lossCause, runTurns, endMission, endObjective all equal. Reports the counts
either way and the pinned leg's heatLeak block (campaignsRaised / missionsAbovePin must be 0).

Usage: pincheck.py <leaky.json> <pinned.json> [more leaky/pinned pairs...]
"""
import json, sys

KEYS = ["win", "missionsCleared", "lossCause", "runTurns", "endMission", "endObjective"]


def rows(path):
    d = json.load(open(path))
    return d, {(r["slot"], r["policy"], r["heat"]): r for r in d["campaigns"] if r["mode"] == "campaign"}


def main(pairs):
    tot_clean = tot_clean_same = tot_leak = tot_leak_same = 0
    bad = 0
    for leaky, pinned in pairs:
        dl, rl = rows(leaky); dp, rp = rows(pinned)
        hl = dp["heatLeak"]
        if hl["pinned"] is not True or hl["campaignsRaised"] != 0 or hl["missionsAbovePin"] != 0:
            print(f"!! {pinned}: pinned leg reports a leak: {hl}"); bad += 1
        if dl["heatLeak"]["pinned"] is not False:
            print(f"!! {leaky}: leaky leg claims to be pinned"); bad += 1
        if set(rl) != set(rp):
            print(f"!! {leaky} vs {pinned}: slot sets differ"); bad += 1; continue
        clean = clean_same = leak = leak_same = 0
        for k, a in rl.items():
            b = rp[k]
            same = all(a[f] == b[f] for f in KEYS)
            if a["heatRaisingPicks"] == 0:
                clean += 1; clean_same += same
                if not same:
                    print(f"!! {k}: NO heat-raising pick yet the legs differ: {[(f, a[f], b[f]) for f in KEYS if a[f] != b[f]]}")
                    bad += 1
            else:
                leak += 1; leak_same += same
                if a["heatEnd"] <= a["heat"]:
                    # a pick at the ceiling, or on the last node, raises nothing observable
                    pass
        print(f"{leaky.split('/')[-1]:>28} vs {pinned.split('/')[-1]:<28} clean {clean_same}/{clean} identical   "
              f"leaked {leak} (of which {leak_same} still identical; leaky campaignsRaised={dl['heatLeak']['campaignsRaised']} "
              f"missionsAbovePin={dl['heatLeak']['missionsAbovePin']})")
        tot_clean += clean; tot_clean_same += clean_same; tot_leak += leak; tot_leak_same += leak_same
    print(f"TOTAL: campaigns with no heat-raising pick {tot_clean_same}/{tot_clean} identical across the pin; "
          f"campaigns with a pick {tot_leak}, {tot_leak_same} of them still identical")
    print("PINCHECK:", "PASS" if bad == 0 and tot_clean_same == tot_clean else "FAIL")
    return 0 if bad == 0 and tot_clean_same == tot_clean else 1


if __name__ == "__main__":
    a = sys.argv[1:]
    sys.exit(main(list(zip(a[0::2], a[1::2]))))
