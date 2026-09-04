#!/usr/bin/env python3
"""L6 — the CRN-PAIRED CONTRAST between two prefixes, with the two numbers C2 said to always print.

C2's rule, from CLAUDE.md's own wave table: **a CRN round's resolving power is its DISCORDANT
count, not its n** — a flat row at low discordance is an absence of evidence, not neutrality. So
every row here carries `n_disc` and the MDE (the smallest true effect this many discordant pairs
could have detected at 80% power, two-sided alpha 0.05) beside the point estimate. A |delta| below
its own MDE is NOT a measured zero.

  McNemar   b = A-only wins, c = B-only wins over the paired (slot, policy) campaigns.
            SE(delta) = sqrt(b+c)/n  (percentage points x100); z = (b-c)/sqrt(b+c).
            MDE(80%) = 2.80 x SE.
  chunk     the same delta averaged over the k CRN slot sets, with the cluster SE and t on k-1 df,
            because the clusters disagree (every round since C3's review says to look).

Usage: pairs.py <prefixA> <dirA> <prefixB> <dirB> [--label "A vs B"]
       A positive delta means B wins more than A.
"""
import glob, json, math, os, statistics, sys

RUNGS = ["R", "0", "2", "4", "6", "8"]


def base_of(f):
    return int(os.path.basename(f).rsplit("-b", 1)[1].split(".")[0])


def outcomes(path):
    """(slot, policy) -> win, from pairedPolicy.slots (the CRN unit)."""
    j = json.load(open(path))
    d = {}
    for s in j["pairedPolicy"]["slots"]:
        d[(s["slot"], "g")] = s["greedyWin"]
        d[(s["slot"], "s")] = s["sloppyWin"]
    return d


def main():
    a_pre, a_dir, b_pre, b_dir = sys.argv[1:5]
    label = sys.argv[6] if len(sys.argv) > 6 and sys.argv[5] == "--label" else f"{a_pre} -> {b_pre}"
    print(f"===== CRN-PAIRED: {label} =====")
    print(f"  {'rung':<6}{'n':>5}{'A%':>7}{'B%':>7}{'delta':>7}{'b':>4}{'c':>4}{'n_disc':>7}"
          f"{'SE':>6}{'MDE':>6}{'z':>7}   {'chunkMean':>9}{'clSE':>6}{'t':>7}  verdict")
    TB = TC = TN = 0
    for h in RUNGS:
        per_chunk, n, wa, wb, b, c = [], 0, 0, 0, 0, 0
        for fa in sorted(glob.glob(os.path.join(a_dir, f"{a_pre}-h{h}-b*.json")), key=base_of):
            fb = os.path.join(b_dir, f"{b_pre}-h{h}-b{base_of(fa)}.json")
            if not os.path.exists(fb):
                continue
            A, B = outcomes(fa), outcomes(fb)
            keys = sorted(set(A) & set(B))
            if not keys:
                continue
            ca = sum(A[k] for k in keys); cb = sum(B[k] for k in keys)
            per_chunk.append(100.0 * (cb - ca) / len(keys))
            n += len(keys); wa += ca; wb += cb
            b += sum(1 for k in keys if A[k] and not B[k])
            c += sum(1 for k in keys if B[k] and not A[k])
        if not n:
            continue
        nd = b + c
        se = 100.0 * math.sqrt(nd) / n if nd else float("nan")
        mde = 2.80 * se if nd else float("nan")
        z = (c - b) / math.sqrt(nd) if nd else float("nan")
        k = len(per_chunk)
        cm = statistics.mean(per_chunk)
        cse = statistics.stdev(per_chunk) / math.sqrt(k) if k > 1 else float("nan")
        t = cm / cse if cse else float("nan")
        delta = 100.0 * (wb - wa) / n
        if nd == 0:
            v = "IDENTICAL on every paired campaign — the arms did not diverge here at all"
        elif abs(delta) < mde:
            v = f"NOT RESOLVED (|{delta:+.1f}| < MDE {mde:.1f})"
        else:
            v = "resolved at 80% power"
        print(f"  h{h:<5}{n:>5}{100.0*wa/n:>7.1f}{100.0*wb/n:>7.1f}{delta:>7.1f}{b:>4}{c:>4}{nd:>7}"
              f"{se:>6.2f}{mde:>6.2f}{z:>7.2f}   {cm:>9.2f}{cse:>6.2f}{t:>7.2f}  {v}")
        TB += b; TC += c; TN += n
    nd = TB + TC
    if nd:
        se = 100.0 * math.sqrt(nd) / TN
        print(f"  POOLED {TN} pairs: {TB} A-only / {TC} B-only, n_disc={nd}, "
              f"delta {100.0*(TC-TB)/TN:+.2f}, SE {se:.2f}, MDE(80%) {2.80*se:.2f}, "
              f"McNemar z = {(TC-TB)/math.sqrt(nd):+.2f}")
    else:
        print(f"  POOLED {TN} pairs: ZERO discordant — the two arms are outcome-identical.")


if __name__ == "__main__":
    main()
