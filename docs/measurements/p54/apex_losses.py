#!/usr/bin/env python3
"""P54 — WHAT ACTUALLY ENDS A CAMPAIGN AT THE APEX.

No new compute: this reads `campaigns[]` out of the EXISTING archive. Every balance chunk since
THE HEAT PIN carries one record per campaign with `lossCause`, `endMission` and `endObjective`, and
nothing had ever cross-tabbed them by rung.

Only UNFORCED ladder rounds are read (l6, l7, p23, p24). Chunks with `levers.siteGlyphs` true are
skipped: those are P49-P53's forced content arms, which play one arena and one objective and are not
the shipped distribution.

Usage: apex_losses.py [rung ...]     (default 0 4 8)
"""
import collections, glob, json, os, sys

ROUNDS = ["l6", "l7", "p23", "p24"]
OBJECTIVE_FAILURES = {"VIP LOST", "CAPTIVE LOST", "CAPTIVE ABANDONED"}
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")


def campaigns(rnd, rung):
    for f in glob.glob(os.path.join(ROOT, rnd, f"*-h{rung}-b*.json")):
        j = json.load(open(f))
        if (j.get("levers") or {}).get("siteGlyphs"):
            continue                      # forced content arm, not the shipped distribution
        for c in j.get("campaigns") or []:
            if c.get("mode") == "campaign":
                yield c


def main():
    rungs = [int(a) for a in sys.argv[1:]] or [0, 4, 8]

    print("== PER-ROUND REPLICATION: share of campaign LOSSES that are the protected NPC dying ==")
    for rnd in ROUNDS:
        row = []
        for rung in rungs:
            L = [c for c in campaigns(rnd, rung) if not c["win"]]
            if not L:
                continue
            objf = sum(1 for c in L if c.get("lossCause") in OBJECTIVE_FAILURES)
            m6 = sum(1 for c in L if c.get("endMission") == 6)
            row.append(f"h{rung}: n={len(L):5d} npc={100*objf/len(L):5.1f}% m6={100*m6/len(L):5.1f}%")
        print(f"  {rnd:4s}  " + "   ".join(row))

    print("\n== POOLED over all four rounds ==")
    for rung in rungs:
        L = [c for c in (x for rnd in ROUNDS for x in campaigns(rnd, rung)) if not c["win"]]
        if not L:
            continue
        lc = collections.Counter(c.get("lossCause") for c in L)
        objf = [c for c in L if c.get("lossCause") in OBJECTIVE_FAILURES]
        print(f"\n  h{rung}: {len(L)} losses")
        for k, v in lc.most_common(6):
            print(f"      {str(k):20s} {v:6d}  {100*v/len(L):5.1f}%")
        if objf:
            byobj = collections.Counter(c.get("endObjective") for c in objf)
            print("      NPC deaths by objective: "
                  + "  ".join(f"{k} {100*v/len(objf):.0f}%" for k, v in byobj.most_common()))
            em = collections.Counter(c.get("endMission") for c in objf)
            t = sum(em.values())
            print("      NPC deaths by mission:   "
                  + "  ".join(f"m{k}:{100*v/t:.0f}%" for k, v in sorted(em.items()) if k))
        wipe = [c for c in L if c.get("lossCause") == "RUN OVER"]
        if wipe:
            em = collections.Counter(c.get("endMission") for c in wipe)
            t = sum(em.values())
            print("      WIPES by mission:        "
                  + "  ".join(f"m{k}:{100*v/t:.0f}%" for k, v in sorted(em.items()) if k))


if __name__ == "__main__":
    main()
