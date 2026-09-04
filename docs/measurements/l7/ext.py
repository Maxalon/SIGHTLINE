#!/usr/bin/env python3
"""L7 — THE EXTENSION: rungs 4-8 replicated on 16 slot sets the ladder round never saw.

Reports, in this order and deliberately:
  (1) the NEW 16 sets alone (bases 160-310) — an out-of-sample test with its own MDE,
  (2) the ORIGINAL 16 sets (bases 0-150) — the ladder round's own numbers, for the comparison,
  (3) the POOLED 32 sets, n=640/rung.
Reading (1) before (3) is the point: a step that reverses sign out of sample has not been
measured, however good the pooled p-value looks. This is L5's split-half and L6's P20 re-price.

Usage: ext.py [--dir DIR]
"""
import glob, json, math, os, statistics, sys

HERE = os.path.dirname(os.path.abspath(__file__))
RUNGS = ["4", "5", "6", "7", "8"]
OLD = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150]
NEW = [160, 170, 180, 190, 200, 210, 220, 230, 240, 250, 260, 270, 280, 290, 300, 310]


def outcomes(path):
    j = json.load(open(path))
    d = {}
    for s in j["pairedPolicy"]["slots"]:
        d[(s["slot"], "g")] = bool(s["greedyWin"])
        d[(s["slot"], "s")] = bool(s["sloppyWin"])
    return d


def load(d, prefix, bases):
    out = {}
    for h in RUNGS:
        per = {}
        for b in bases:
            f = os.path.join(d, f"{prefix}-h{h}-b{b}.json")
            if os.path.exists(f):
                per[b] = outcomes(f)
        if per:
            out[h] = per
    return out


def block(title, data):
    print(f"\n===== {title} =====")
    print(f"  {'rung':<6}{'k':>3}{'n':>6}{'win%':>7}{'binSE':>7}{'clSE':>7}")
    means = {}
    for h in RUNGS:
        if h not in data:
            continue
        vals = [100.0 * sum(v.values()) / len(v) for v in data[h].values()]
        n = sum(len(v) for v in data[h].values()); w = sum(sum(v.values()) for v in data[h].values())
        p = w / n
        k = len(vals)
        means[h] = 100.0 * p
        print(f"  h{h:<5}{k:>3}{n:>6}{100.0*p:>7.1f}{100*math.sqrt(p*(1-p)/n):>7.2f}"
              f"{statistics.stdev(vals)/math.sqrt(k) if k > 1 else float('nan'):>7.2f}")
    print(f"  {'step':<12}{'buys':>7}{'b':>5}{'c':>5}{'n_disc':>7}{'pairSE':>7}{'MDE80':>7}{'z':>7}"
          f"{'chunk':>7}{'clSE':>6}{'t':>7}  verdict")
    for i in range(len(RUNGS) - 1):
        a, b_ = RUNGS[i], RUNGS[i + 1]
        if a not in data or b_ not in data:
            continue
        npair = bb = cc = 0
        per_chunk = []
        for base in sorted(set(data[a]) & set(data[b_])):
            A, B = data[a][base], data[b_][base]
            ks = sorted(set(A) & set(B))
            per_chunk.append(100.0 * (sum(A[k] for k in ks) - sum(B[k] for k in ks)) / len(ks))
            npair += len(ks)
            bb += sum(1 for k in ks if A[k] and not B[k])
            cc += sum(1 for k in ks if B[k] and not A[k])
        nd = bb + cc
        se = 100.0 * math.sqrt(nd) / npair if nd else float("nan")
        mde = 2.80 * se if nd else float("nan")
        z = (bb - cc) / math.sqrt(nd) if nd else float("nan")
        k = len(per_chunk)
        cm = statistics.mean(per_chunk); cse = statistics.stdev(per_chunk) / math.sqrt(k) if k > 1 else float("nan")
        step = means[a] - means[b_]
        v = f"NOT RESOLVED (|{step:.1f}| < MDE {mde:.1f})" if abs(step) < mde else "RESOLVED at 80% power"
        print(f"  h{a}->h{b_:<8}{step:>7.1f}{bb:>5}{cc:>5}{nd:>7}{se:>7.2f}{mde:>7.2f}{z:>7.2f}"
              f"{cm:>7.2f}{cse:>6.2f}{cm/cse if cse else float('nan'):>7.2f}  {v}")
    return means


def main():
    d = HERE
    a = sys.argv[1:]
    for i, x in enumerate(a):
        if x == "--dir": d = a[i + 1]
    new = load(d, "L7x", NEW)
    old = load(d, "L7", OLD)
    pooled = {h: dict(list(old.get(h, {}).items()) + list(new.get(h, {}).items())) for h in RUNGS}
    mN = block("(1) THE NEW 16 SLOT SETS ALONE — bases 160-310, out of sample, n=320/rung", new)
    mO = block("(2) THE LADDER ROUND'S OWN 16 SETS — bases 0-150, n=320/rung", old)
    mP = block("(3) POOLED, 32 SLOT SETS — n=640/rung", pooled)
    print("\n===== THE REPLICATION, side by side =====")
    print(f"  {'rung':<6}{'old16':>8}{'new16':>8}{'pooled':>8}{'diff':>8}")
    for h in RUNGS:
        if h in mO and h in mN:
            print(f"  h{h:<5}{mO[h]:>8.1f}{mN[h]:>8.1f}{mP[h]:>8.1f}{mN[h]-mO[h]:>+8.1f}")
    print(f"  {'step':<12}{'old16':>8}{'new16':>8}{'pooled':>8}  same sign?")
    for i in range(len(RUNGS) - 1):
        a_, b_ = RUNGS[i], RUNGS[i + 1]
        if a_ in mO and b_ in mO and a_ in mN and b_ in mN:
            so, sn, sp = mO[a_] - mO[b_], mN[a_] - mN[b_], mP[a_] - mP[b_]
            print(f"  h{a_}->h{b_:<8}{so:>+8.1f}{sn:>+8.1f}{sp:>+8.1f}  {'YES' if so * sn > 0 else 'NO — the sign did not replicate'}")


if __name__ == "__main__":
    main()
