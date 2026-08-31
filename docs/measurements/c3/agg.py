#!/usr/bin/env python3
"""C3 aggregator — pools the 48 chunk JSONs of a round into the cross-tabs the wave needs.

Every chunk cell carries n and winRate(%); wins are recovered as round(n*winRate/100), which is
exact for the n's these chunks produce (winRate is emitted to 1 dp). Nothing here re-derives a
number the harness already computes at chunk level; it only POOLS.

Usage: agg.py <round-tag> [more-round-tags...]
"""
import json, glob, sys, math, os

KILL = {"Eliminate", "Decapitate"}
MIDRUN_NODES = {"Combat", "Elite"}          # W8's mid-run node kinds
DIR = os.path.dirname(os.path.abspath(__file__))


def se(w, n):
    if n == 0:
        return 0.0
    p = w / n
    return 100.0 * math.sqrt(max(p * (1 - p), 0.0) / n)


def load(tag):
    """Pool one round. Returns dicts keyed for each cross-tab plus ladder rows."""
    obj_node, obj_miss, ladder, byobj, byhvt = {}, {}, {}, {}, {}
    runs = 0
    files = sorted(glob.glob(os.path.join(DIR, tag + "-h*-b*.json")))
    for f in files:
        d = json.load(open(f))
        runs += d["runs"]
        rung = os.path.basename(f).split("-")[1]          # hR / h0 / ...
        a, b = ladder.get(rung, (0, 0))
        ladder[rung] = (a + round(d["runs"] * d["runWinRate"] / 100.0), b + d["runs"])
        for r in d["byObjectiveByNodeKind"]:
            k = (r["objective"], r["nodeKind"])
            w, n = obj_node.get(k, (0, 0))
            obj_node[k] = (w + round(r["n"] * r["winRate"] / 100.0), n + r["n"])
        for r in d["byObjectiveByMission"]:
            k = (r["objective"], r["mission"])
            w, n = obj_miss.get(k, (0, 0))
            obj_miss[k] = (w + round(r["n"] * r["winRate"] / 100.0), n + r["n"])
        for r in d["byObjective"]:
            k = r["objective"]
            w, n, t = byobj.get(k, (0, 0, 0.0))
            byobj[k] = (w + round(r["n"] * r["winRate"] / 100.0), n + r["n"], t + r["avgTurns"] * r["n"])
    return dict(runs=runs, files=len(files), ladder=ladder, obj_node=obj_node,
                obj_miss=obj_miss, byobj=byobj)


def line(lbl, w, n, extra=""):
    return f"  {lbl:<34} n={n:<5} win={100.0*w/n if n else 0:6.1f} +-{se(w,n):4.1f}{extra}"


def report(tag):
    d = load(tag)
    out = []
    out.append(f"===== ROUND {tag}  ({d['files']} chunks, {d['runs']} campaigns) =====")
    out.append("-- LADDER (run completion) --")
    for rung in ["hR", "h0", "h2", "h4", "h6", "h8"]:
        if rung in d["ladder"]:
            w, n = d["ladder"][rung]
            out.append(line(rung, w, n))
    out.append("-- POOLED byObjective (the row W8 warns about) --")
    for k in sorted(d["byobj"], key=lambda k: -d["byobj"][k][0] / max(d["byobj"][k][1], 1)):
        w, n, t = d["byobj"][k]
        out.append(line(k, w, n, f"  turns={t/n:5.2f}"))

    out.append("-- byObjectiveByNodeKind --")
    kinds = sorted({k[1] for k in d["obj_node"]})
    hdr = "  {:<12}".format("objective") + "".join(f"{k:>18}" for k in kinds)
    out.append(hdr)
    for o in sorted({k[0] for k in d["obj_node"]}):
        row = "  {:<12}".format(o)
        for kd in kinds:
            w, n = d["obj_node"].get((o, kd), (0, 0))
            row += f"{('%.1f (n=%d)' % (100.0*w/n, n)) if n else '-':>18}"
        out.append(row)

    out.append("-- byObjectiveByMission --")
    miss = sorted({k[1] for k in d["obj_miss"]})
    out.append("  {:<12}".format("objective") + "".join(f"{('m%d'%m):>16}" for m in miss))
    for o in sorted({k[0] for k in d["obj_miss"]}):
        row = "  {:<12}".format(o)
        for m in miss:
            w, n = d["obj_miss"].get((o, m), (0, 0))
            row += f"{('%.1f (n=%d)' % (100.0*w/n, n)) if n else '-':>16}"
        out.append(row)

    out.append("-- THE CLASS SPLIT on mid-run node kinds (Combat+Elite) --")
    for lbl, sel in [("KILL   (Eliminate,Decapitate)", lambda o: o in KILL),
                     ("NONKILL(other six)", lambda o: o not in KILL)]:
        w = n = 0
        for (o, kd), (cw, cn) in d["obj_node"].items():
            if kd in MIDRUN_NODES and sel(o):
                w += cw; n += cn
        out.append(line(lbl, w, n))
    kw = kn = nw = nn = 0
    for (o, kd), (cw, cn) in d["obj_node"].items():
        if kd in MIDRUN_NODES:
            if o in KILL: kw += cw; kn += cn
            else: nw += cw; nn += cn
    gap = (100.0*nw/nn if nn else 0) - (100.0*kw/kn if kn else 0)
    gse = math.sqrt(se(kw, kn)**2 + se(nw, nn)**2)
    out.append(f"  GAP = {gap:.1f} points, +-SE {gse:.1f}")

    out.append("-- THE CLASS SPLIT excluding mission 1 (all node kinds) --")
    kw = kn = nw = nn = 0
    for (o, m), (cw, cn) in d["obj_miss"].items():
        if m >= 2:
            if o in KILL: kw += cw; kn += cn
            else: nw += cw; nn += cn
    out.append(line("KILL   m>=2", kw, kn))
    out.append(line("NONKILL m>=2", nw, nn))
    out.append(f"  GAP = {(100.0*nw/nn)-(100.0*kw/kn):.1f} points, +-SE {math.sqrt(se(kw,kn)**2+se(nw,nn)**2):.1f}")
    return "\n".join(out), d


if __name__ == "__main__":
    for t in sys.argv[1:]:
        s, _ = report(t)
        print(s)
        print()
