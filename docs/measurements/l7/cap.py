#!/usr/bin/env python3
"""L7 — THE CLAMP ARM. What rung 8 buys with the 12-body ceiling binding, and with it clear.

`Mission.Build`: `count = Math.Clamp(EnemyBaseCount + n + enemyDelta, 3, 12)`. At the shipped
base of 4 the ceiling binds at the top of the ladder — at mission 6 heat 7 asks for 13 bodies
and heat 8 asks for 14, and BOTH are cut to 12, so rung 8's whole body tooth is arithmetic that
never reaches the finale. `SIGHTLINE_ENEMYBASE=2` moves m5 and m6 back under the ceiling.

Two arms, the SAME 16 CRN slot sets, rungs 6-8:
  L7    the shipped base (4) — the ceiling binds at m5/m6
  L7cap base 2               — the ceiling is clear, so rung 8's body lands at every mission
A MECHANISM comparison, not a level one: base 2 is an easier game and the two arms' absolute
win rates are not comparable. What is comparable is what each RUNG BUYS inside its own arm.

Usage: cap.py [--dir DIR]
"""
import glob, json, math, os, statistics, sys

HERE = os.path.dirname(os.path.abspath(__file__))
RUNGS = ["6", "7", "8"]
OLD = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150]
NEW = [160, 170, 180, 190, 200, 210, 220, 230, 240, 250, 260, 270, 280, 290, 300, 310]
BASES = OLD + NEW


def outcomes(p):
    j = json.load(open(p))
    return {(s["slot"], k[0]): bool(s[k]) for s in j["pairedPolicy"]["slots"] for k in ("greedyWin", "sloppyWin")}


def load(d, prefix, bases=None):
    out = {}
    for h in RUNGS:
        per = {}
        for b in (bases or BASES):
            f = os.path.join(d, f"{prefix}-h{h}-b{b}.json")
            if os.path.exists(f):
                per[b] = outcomes(f)
        out[h] = per
    return out


def arm(name, data):
    print(f"\n===== {name} =====")
    means = {}
    print(f"  {'rung':<6}{'n':>6}{'win%':>7}{'m6cond%':>9}{'m6 n':>7}{'avgMis':>8}")
    for h in RUNGS:
        n = sum(len(v) for v in data[h].values()); w = sum(sum(v.values()) for v in data[h].values())
        means[h] = 100.0 * w / n
        print(f"  h{h:<5}{n:>6}{means[h]:>7.1f}" + "".join(f"{x:>9}" for x in ["", "", ""]))
    print(f"  {'step':<12}{'buys':>7}{'b':>5}{'c':>5}{'n_disc':>7}{'MDE80':>7}{'z':>7}{'chunk t':>9}  verdict")
    steps = {}
    for i in range(len(RUNGS) - 1):
        a, b_ = RUNGS[i], RUNGS[i + 1]
        npair = bb = cc = 0
        pc = []
        for base in sorted(set(data[a]) & set(data[b_])):
            A, B = data[a][base], data[b_][base]
            ks = sorted(set(A) & set(B))
            pc.append(100.0 * (sum(A[k] for k in ks) - sum(B[k] for k in ks)) / len(ks))
            npair += len(ks)
            bb += sum(1 for k in ks if A[k] and not B[k])
            cc += sum(1 for k in ks if B[k] and not A[k])
        nd = bb + cc
        se = 100.0 * math.sqrt(nd) / npair
        mde = 2.80 * se
        z = (bb - cc) / math.sqrt(nd)
        cm = statistics.mean(pc); cse = statistics.stdev(pc) / math.sqrt(len(pc))
        steps[(a, b_)] = (means[a] - means[b_], cm, cse, nd, mde, z)
        v = f"NOT RESOLVED (|{means[a]-means[b_]:.1f}| < MDE {mde:.1f})" if abs(means[a] - means[b_]) < mde else "RESOLVED"
        print(f"  h{a}->h{b_:<8}{means[a]-means[b_]:>7.1f}{bb:>5}{cc:>5}{nd:>7}{mde:>7.2f}{z:>7.2f}{cm/cse:>9.2f}  {v}")
    return means, steps


def missions(d, prefix, h):
    bym = {}
    for f in sorted(glob.glob(os.path.join(d, f"{prefix}-h{h}-b*.json"))):
        j = json.load(open(f))
        for r in j["byMission"]:
            a = bym.setdefault(r["mission"], [0, 0.0])
            a[0] += r["n"]; a[1] += r["n"] * r["winRate"] / 100.0
    return bym


def main():
    d = HERE
    a = sys.argv[1:]
    for i, x in enumerate(a):
        if x == "--dir": d = a[i + 1]
    shipO, capO = load(d, "L7", OLD), load(d, "L7cap", OLD)
    shipN, capN = load(d, "L7x", NEW), load(d, "L7cap", NEW)
    ship = {h: dict(list(shipO[h].items()) + list(shipN[h].items())) for h in RUNGS}
    cap = {h: dict(list(capO[h].items()) + list(capN[h].items())) for h in RUNGS}
    print("===== ARM SIZES: 32 CRN slot sets per rung per arm, n=640 =====")
    mS, sS = arm("SHIPPED BASE 4 — the 12-body ceiling BINDS at m5/m6 (the ladder round's own chunks)", ship)
    mC, sC = arm("BASE 2 (SIGHTLINE_ENEMYBASE=2) — the ceiling is CLEAR at every mission (32 sets)", cap)
    print("\n--- the same two arms on the ORIGINAL 16 sets only, for the split-half ---")
    mS16, sS16 = arm("base 4, sets 0-150", shipO)
    mC16, sC16 = arm("base 2, sets 0-150", capO)
    print("\n--- and on the NEW 16 sets only ---")
    mS16b, sS16b = arm("base 4, sets 160-310", shipN)
    mC16b, sC16b = arm("base 2, sets 160-310", capN)

    print("\n===== THE DIFFERENCE-IN-DIFFERENCES: does rung 8 buy anything once its body can land? =====")
    print(f"  {'step':<12}{'base4':>8}{'base2':>8}{'DiD':>8}{'SE(DiD)':>9}{'t':>7}")
    for k in sS:
        s4, c4, e4, *_ = sS[k]
        s2, c2, e2, *_ = sC[k]
        did = c2 - c4
        se = math.sqrt(e4 * e4 + e2 * e2)
        print(f"  h{k[0]}->h{k[1]:<8}{c4:>8.2f}{c2:>8.2f}{did:>+8.2f}{se:>9.2f}{did/se:>7.2f}")
    print()
    print("  ODDS-SCALE DiD (the absolute scale flatters base 2, which wins more and so has more")
    print("  points to lose; the odds ratio does not):")
    print(f"  {'step':<12}{'OR base4':>10}{'OR base2':>10}{'ratio':>8}{'z':>7}")
    for (a, b_) in sS:
        def od(m, x, y):
            wa, wb = m[x] / 100.0, m[y] / 100.0
            N = 640
            A, B = wa * N + .5, (1 - wa) * N + .5
            C, D = wb * N + .5, (1 - wb) * N + .5
            import math as _m
            return (C / D) / (A / B), _m.sqrt(1 / A + 1 / B + 1 / C + 1 / D)
        o4, s4 = od(mS, a, b_)
        o2, s2 = od(mC, a, b_)
        z = (math.log(o2) - math.log(o4)) / math.sqrt(s4 * s4 + s2 * s2)
        print(f"  h{a}->h{b_:<8}{o4:>10.2f}{o2:>10.2f}{o2/o4:>8.2f}{z:>7.2f}")
    print()
    print("  (the two arms share their slot sets but not their worlds — base 2 changes the board's force")
    print("   size from mission 1, so the arms diverge immediately. The DiD is chunk-paired on the slot")
    print("   SET, which is why its SE is the two cluster SEs added in quadrature and not a McNemar SE.)")

    print("\n===== MISSION WIN RATE BY MISSION, both arms (where the ceiling was eating the rung) =====")
    for prefix, lbl in (("L7", "base 4 (shipped), sets 0-150"), ("L7cap", "base 2, both halves")):
        print(f"  -- {lbl} --")
        for h in RUNGS:
            bym = missions(d, prefix, h)
            print(f"    h{h}: " + "  ".join(f"m{m}:{100*v[1]/v[0]:5.1f}%(n{v[0]})" for m, v in sorted(bym.items())))


if __name__ == "__main__":
    main()
