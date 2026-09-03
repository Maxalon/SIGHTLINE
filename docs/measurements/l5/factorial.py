#!/usr/bin/env python3
"""L5 — the h6 2^3 FACTORIAL, chunk-paired: MIDTOOTH{0,3} x AIDECLINE{0,1} x BIOMEMECH{0,1}.

L4 attributed h6 one dial at a time and found single removals summing to +3.6 against a joint
removal of +9.4 — an apparent +5.8 interaction, chunk-paired t(7) = +2.05, p ~ 0.08, NOT resolved
(ROADMAP: "resolve the interaction term, or stop citing it"). This is the design that can: every
one of the 8 arms replays the SAME 16 CRN slot sets (bases 0..150), so every contrast is a paired
difference of chunk means and its SE is the cluster SE over 16 clusters (t on 15 df).

Coding: x = +1 when a lever is REMOVED (MIDTOOTH=0 / AIDECLINE=0 / BIOMEMECH=0), -1 when shipped,
so a positive main effect means "removing it makes h6 EASIER", the sign L4's table used.
  main_X    = (1/4) sum_arms x_X w        the factorial main effect (averaged over the other two)
  int_XY    = (1/4) sum_arms x_X x_Y w    the factorial two-way term
  int_XYZ   = (1/4) sum_arms x_X x_Y x_Z w
  s_X       = w(X off, others shipped) - w(composed)      L4's single-removal contrast
  joint     = w(all off) - w(composed)                    L4's all-off contrast
  Q         = joint - (s_M + s_A + s_B)                   L4's "+5.8" quantity, three-lever form
  c_XY      = [w(X off,Y off,Z on) - w(Y off,Z on)] - s_X   the two-way term AT THE COMPOSED POINT
'resolved' for Q: |t| >= 2.3, or the 95% CI excludes L4's +5.8. Both are printed; the reader gets
the verdict AND the numbers it came from.

Also asserts the CRN chain: the all-shipped arm (m3a1b1, dials set explicitly to their defaults)
must be byte-identical to the ladder's own L5-h6 chunks once harness{} is dropped.

Usage: factorial.py [--dir DIR] [--ladder L5] [--heat 6]
"""
import glob, json, math, os, statistics, sys

HERE = os.path.dirname(os.path.abspath(__file__))
BASES = list(range(0, 160, 10))
T975_15 = 2.131        # t(0.975, 15)


def arm_tag(m, a, b):
    return f"m{m}a{a}b{b}"


ARMS = [arm_tag(m, a, b) for m in (3, 0) for a in (1, 0) for b in (1, 0)]
CODE = {t: (+1 if t[1] == "0" else -1, +1 if t[3] == "0" else -1, +1 if t[5] == "0" else -1) for t in ARMS}


def win_pct(j):
    rs = [r for r in j["campaigns"] if r["mode"] == "campaign"]
    return 100.0 * sum(r["win"] for r in rs) / len(rs), len(rs)


def stat(vals):
    k = len(vals)
    m = statistics.mean(vals)
    sd = statistics.stdev(vals) if k > 1 else float("nan")
    se = sd / math.sqrt(k)
    t = m / se if se > 0 else float("inf")
    return m, sd, se, t, (m - T975_15 * se, m + T975_15 * se)


def main():
    args = sys.argv[1:]
    d, ladder, H = HERE, "L5", "6"
    i = 0
    while i < len(args):
        if args[i] == "--dir": d = args[i + 1]; i += 2
        elif args[i] == "--ladder": ladder = args[i + 1]; i += 2
        elif args[i] == "--heat": H = args[i + 1]; i += 2
        else: i += 1
    W = {}          # (arm, base) -> win%
    N = 0
    missing = []
    for t in ARMS:
        for b in BASES:
            f = os.path.join(d, f"L5fac-{t}-h{H}-b{b}.json")
            if not os.path.exists(f):
                missing.append(f); continue
            j = json.load(open(f))
            if j["heatLeak"]["campaignsRaised"] or j["heatLeak"]["missionsAbovePin"] or not j["heatLeak"]["pinned"]:
                print(f"!! {f}: not pinned / leaked {j['heatLeak']}")
            W[(t, b)], n = win_pct(j); N += n
    if missing:
        print(f"MISSING {len(missing)} chunk(s): {missing[:4]} ..."); sys.exit(2)
    bases = BASES
    k = len(bases)
    print(f"===== h{H} 2^3 FACTORIAL — 8 arms x {k} CRN slot sets x 20 = {N} campaigns =====")
    # ---- CRN-chain check: the all-shipped arm == the ladder's own h6 chunks ----
    same = 0
    for b in bases:
        f1 = os.path.join(d, f"L5fac-m3a1b1-h{H}-b{b}.json"); f2 = os.path.join(d, f"{ladder}-h{H}-b{b}.json")
        if os.path.exists(f2):
            a, c = json.load(open(f1)), json.load(open(f2))
            a.pop("harness", None); c.pop("harness", None)
            same += json.dumps(a, sort_keys=True) == json.dumps(c, sort_keys=True)
    print(f"  CRN CHAIN: m3a1b1 (dials set to their defaults explicitly) == {ladder}-h{H} chunks byte-for-byte minus harness: {same}/{k}")
    # ---- the arm table ----
    print(f"\n  {'arm':<8}{'MIDTOOTH':>9}{'AIDECLINE':>10}{'BIOMEMECH':>10}{'win%':>7}{'clustSE':>8}  per-chunk")
    for t in ARMS:
        vals = [W[(t, b)] for b in bases]
        m, sd, se, _, _ = stat(vals)
        print(f"  {t:<8}{t[1]:>9}{t[3]:>10}{t[5]:>10}{m:>7.2f}{se:>8.2f}  {' '.join(f'{v:.0f}' for v in vals)}")
    # ---- per-chunk contrasts ----
    def w(t, b): return W[(t, b)]
    rows = {}
    for b in bases:
        comp = w("m3a1b1", b)
        sM = w("m0a1b1", b) - comp; sA = w("m3a0b1", b) - comp; sB = w("m3a1b0", b) - comp
        joint = w("m0a0b0", b) - comp
        rows.setdefault("s_M  (MIDTOOTH=0, others shipped)", []).append(sM)
        rows.setdefault("s_A  (AIDECLINE=0, others shipped)", []).append(sA)
        rows.setdefault("s_B  (BIOMEMECH=0, others shipped)", []).append(sB)
        rows.setdefault("sum of single removals", []).append(sM + sA + sB)
        rows.setdefault("joint (all three off)", []).append(joint)
        rows.setdefault("Q = joint - sum  [L4's '+5.8']", []).append(joint - (sM + sA + sB))
        rows.setdefault("c_MA (two-way at composed point)", []).append((w("m0a0b1", b) - w("m3a0b1", b)) - sM)
        rows.setdefault("c_MB (two-way at composed point)", []).append((w("m0a1b0", b) - w("m3a1b0", b)) - sM)
        rows.setdefault("c_AB (two-way at composed point)", []).append((w("m3a0b0", b) - w("m3a1b0", b)) - sA)
        for name, idx in (("main M (MIDTOOTH removed)", 0), ("main A (AIDECLINE removed)", 1), ("main B (BIOMEMECH removed)", 2)):
            rows.setdefault(name, []).append(sum(CODE[t][idx] * w(t, b) for t in ARMS) / 4)
        for name, (i1, i2) in (("int MxA", (0, 1)), ("int MxB", (0, 2)), ("int AxB", (1, 2))):
            rows.setdefault(name, []).append(sum(CODE[t][i1] * CODE[t][i2] * w(t, b) for t in ARMS) / 4)
        rows.setdefault("int MxAxB", []).append(sum(CODE[t][0] * CODE[t][1] * CODE[t][2] * w(t, b) for t in ARMS) / 4)
    print(f"\n  {'contrast':<40}{'mean':>7}{'clustSE':>8}{'t(15)':>7}{'95% CI':>16}  per-chunk")
    verdict = None
    for name, vals in rows.items():
        m, sd, se, t, ci = stat(vals)
        flag = ""
        if name.startswith("Q ="):
            excl = not (ci[0] <= 5.8 <= ci[1])
            resolved = abs(t) >= 2.3 or excl
            flag = f"   <- {'RESOLVED' if resolved else 'NOT resolved'} (|t|{'>=' if abs(t) >= 2.3 else '<'}2.3; CI {'excludes' if excl else 'includes'} +5.8)"
            verdict = (m, se, t, ci, resolved, excl)
        print(f"  {name:<40}{m:>+7.2f}{se:>8.2f}{t:>+7.2f}  [{ci[0]:+6.1f},{ci[1]:+6.1f}]  {' '.join(f'{v:+.0f}' for v in vals)}{flag}")
    m, se, t, ci, resolved, excl = verdict
    print(f"\n  VERDICT on the interaction term Q: {m:+.2f} +- {se:.2f} (cluster SE, 16 clusters), t(15) = {t:+.2f}, "
          f"95% CI [{ci[0]:+.1f}, {ci[1]:+.1f}] -> {'RESOLVED' if resolved else 'NOT RESOLVED'}"
          f"{'; the CI excludes +5.8' if excl else '; the CI includes +5.8'}")


if __name__ == "__main__":
    main()
