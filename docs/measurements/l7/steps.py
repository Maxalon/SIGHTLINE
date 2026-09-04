#!/usr/bin/env python3
"""L7 EVERY RUNG — THE BUYS ROW, with the uncertainty C1's version did not carry.

C1's ladder printed a "rung N buys" row and priced each step with the SE of the difference of
two INDEPENDENT binomial proportions. That is the right first number and it is not the best one
available here, because **the rungs are CRN-paired to each other**: `Program.cs` calls
`Util.Reseed(50000 + slot)` BEFORE the Game is constructed and the heat rung is dialled in
through an environment variable read at StartMission, so slot s plays the SAME world seed (map,
campaign DAG, objective deal) at every rung. Slot s at h5 and slot s at h6 are the same campaign
with one rung of difference — a paired unit, not two independent draws.

So every step is priced three ways and all three are printed:

  independent  the difference of the two rung proportions, SE = sqrt(seA^2 + seB^2). C1's number,
               kept so the two ladders can be read against each other.
  McNemar      over the (slot, policy) pairs shared by the two rungs: b = the EASIER rung won and
               the harder one did not, c = the reverse. `n_disc = b + c` is the round's real
               resolving power (C2's rule) and `MDE(80%) = 2.80 * SE` is the smallest true step
               this many discordant pairs could have detected. **A step below its own MDE is an
               absence of evidence, not a measured zero.**
  chunk        the same step averaged over the k=16 CRN slot sets, with the cluster SE and t on
               k-1 df, because the clusters disagree (every round since C3's review says so).

A positive step means the HARDER rung wins LESS, i.e. the rung bought difficulty.

Usage: steps.py [prefix] [--dir DIR]
"""
import glob, json, math, os, statistics, sys

HERE = os.path.dirname(os.path.abspath(__file__))
RUNGS = ["R", "0", "1", "2", "3", "4", "5", "6", "7", "8"]


def base_of(f):
    return int(os.path.basename(f).rsplit("-b", 1)[1].split(".")[0])


def load(prefix, d):
    """rung -> {base -> {(slot, policy): win}}"""
    out = {}
    for h in RUNGS:
        per_base = {}
        for f in sorted(glob.glob(os.path.join(d, f"{prefix}-h{h}-b*.json")), key=base_of):
            j = json.load(open(f))
            per_base[base_of(f)] = {}
            for s in j["pairedPolicy"]["slots"]:
                per_base[base_of(f)][(s["slot"], "g")] = bool(s["greedyWin"])
                per_base[base_of(f)][(s["slot"], "s")] = bool(s["sloppyWin"])
        if per_base:
            out[h] = per_base
    return out


def rung_wr(per_base):
    n = sum(len(v) for v in per_base.values())
    w = sum(sum(v.values()) for v in per_base.values())
    p = w / n
    return 100.0 * p, 100.0 * math.sqrt(p * (1 - p) / n), n


def main():
    args = sys.argv[1:]
    d = HERE
    pos = []
    i = 0
    while i < len(args):
        if args[i] == "--dir": d = args[i + 1]; i += 2
        else: pos.append(args[i]); i += 1
    prefix = pos[0] if pos else "L7"
    data = load(prefix, d)
    keys = [h for h in RUNGS if h in data]

    print(f"===== {prefix} — WHAT EACH RUNG BUYS (step DOWN from the rung below; + = harder) =====")
    print(f"  {'step':<12}{'easier%':>8}{'harder%':>8}{'buys':>7}{'indSE':>7}"
          f"{'b':>5}{'c':>5}{'n_disc':>7}{'pairSE':>7}{'MDE80':>7}{'z':>7}"
          f"{'chunk':>7}{'clSE':>6}{'t(15)':>7}  verdict")
    rows = []
    for i in range(len(keys) - 1):
        a, b_ = keys[i], keys[i + 1]          # a = easier rung, b_ = harder rung
        pa, sea, na = rung_wr(data[a])
        pb, seb, nb = rung_wr(data[b_])
        step = pa - pb
        indse = math.sqrt(sea * sea + seb * seb)

        # CRN pairing: same slot base, same slot, same policy leg — one rung apart.
        nb_pairs = bb = cc = 0
        per_chunk = []
        for base in sorted(set(data[a]) & set(data[b_])):
            A, B = data[a][base], data[b_][base]
            ks = sorted(set(A) & set(B))
            if not ks:
                continue
            per_chunk.append(100.0 * (sum(A[k] for k in ks) - sum(B[k] for k in ks)) / len(ks))
            nb_pairs += len(ks)
            bb += sum(1 for k in ks if A[k] and not B[k])    # easier-only win  -> the rung bit
            cc += sum(1 for k in ks if B[k] and not A[k])    # harder-only win  -> against the rung
        nd = bb + cc
        pse = 100.0 * math.sqrt(nd) / nb_pairs if nd else float("nan")
        mde = 2.80 * pse if nd else float("nan")
        z = (bb - cc) / math.sqrt(nd) if nd else float("nan")
        k = len(per_chunk)
        cm = statistics.mean(per_chunk) if k else float("nan")
        cse = statistics.stdev(per_chunk) / math.sqrt(k) if k > 1 else float("nan")
        t = cm / cse if cse else float("nan")

        if nd == 0:
            v = "IDENTICAL on every paired campaign"
        elif abs(step) < mde:
            v = f"NOT RESOLVED (|{step:.1f}| < MDE {mde:.1f})"
        else:
            v = "RESOLVED at 80% power"
        # THE FLOOR PROBLEM. An ABSOLUTE step in win-rate points cannot tell a flat rung from a
        # rung standing on a floor: at h6 = 11 there are only 11 points left for two rungs, so
        # "h6->h8 buys 2.5" is partly arithmetic (C1-5 made the same argument about rungs 7-8).
        # The odds ratio is the scale-free companion: OR < 1 means the harder rung really did cut
        # the player's odds, however few points that is worth. Woolf SE on the 2x2 with a 0.5
        # continuity correction; treated as unpaired, so it is conservative here.
        wa_, wb_ = round(pa * na / 100), round(pb * nb / 100)
        A2, B2, C2_, D2 = wa_ + .5, na - wa_ + .5, wb_ + .5, nb - wb_ + .5
        orat = (C2_ / D2) / (A2 / B2)
        selnor = math.sqrt(1 / A2 + 1 / B2 + 1 / C2_ + 1 / D2)
        znor = math.log(orat) / selnor
        rows.append((a, b_, step, indse, mde, z, t, nd, v, orat, znor))
        print(f"  h{a}->h{b_:<8}{pa:>8.1f}{pb:>8.1f}{step:>7.1f}{indse:>7.2f}"
              f"{bb:>5}{cc:>5}{nd:>7}{pse:>7.2f}{mde:>7.2f}{z:>7.2f}"
              f"{cm:>7.2f}{cse:>6.2f}{t:>7.2f}{orat:>7.2f}{znor:>8.2f}  {v}")

    print()
    res = [r for r in rows if "RESOLVED at" in r[8]]
    nres = [r for r in rows if "NOT RESOLVED" in r[8] or "IDENTICAL" in r[8]]
    print(f"  RESOLVED steps ({len(res)}/{len(rows)}): " + ", ".join(f"h{a}->h{b} {s:+.1f}" for a, b, s, *_ in res))
    print(f"  NOT RESOLVED  ({len(nres)}/{len(rows)}): " + ", ".join(f"h{a}->h{b} {s:+.1f}" for a, b, s, *_ in nres))
    tot = sum(r[2] for r in rows)
    print(f"  SUM of the {len(rows)} steps: {tot:.2f} points (RECRUIT -> h8)")
    print()
    print("  ODDS-RATIO VIEW (scale-free; OR<1 = the harder rung cut the player's odds; |z|>1.96 = resolved):")
    for a, b_, step, indse, mde, z, t, nd, v, orat, znor in rows:
        mark = "RESOLVED" if abs(znor) > 1.96 else "not resolved"
        print(f"    h{a}->h{b_:<4} OR {orat:>5.2f}  lnOR z {znor:>+6.2f}  {mark}")


if __name__ == "__main__":
    main()
