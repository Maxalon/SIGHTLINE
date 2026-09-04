#!/usr/bin/env python3
"""P19 aggregator — CRN-paired arm contrasts with n_discordant and the MDE.

A flat row at low discordance is an ABSENCE OF EVIDENCE, not neutrality (this project has
misread that before), so every contrast prints:
  * the paired difference and its McNemar z over the DISCORDANT pairs only,
  * n_discordant itself,
  * the MDE — the smallest true effect a round of this size could have detected at 80% power,
    computed from the OBSERVED discordance rate (the only honest way to state the power of a
    paired binary test).
Also prints the chunk-clustered SE (8 clusters per rung) beside the binomial one, and the
mission-1 cell (byNodeKind Start) which the design contract forbids a wave from taxing.
"""
import json, glob, math, sys
from collections import defaultdict

OUT = 'docs/measurements/p19'
ARMS = ['pre', 'boss', 'full']
RUNGS = [0, 4, 8]
BASES = [0, 10, 20, 30, 40, 50, 60, 70]


def load(arm, h, b):
    return json.load(open(f'{OUT}/p19-{arm}-h{h}-b{b}.json'))


def rows(arm, h):
    """key -> win, one row per (base, slot, policy) campaign."""
    out = {}
    per_chunk = []
    for b in BASES:
        j = load(arm, h, b)
        wins = 0
        for c in j['campaigns']:
            out[(b, c['slot'], c['policy'])] = bool(c['win'])
            wins += bool(c['win'])
        per_chunk.append(wins / len(j['campaigns']) * 100.0)
    return out, per_chunk


def cluster_se(chunks):
    m = len(chunks)
    mu = sum(chunks) / m
    var = sum((x - mu) ** 2 for x in chunks) / (m - 1)
    return math.sqrt(var / m)


def mde(n_pairs, disc_rate):
    """Smallest paired difference (percentage points) detectable at 80% power, alpha .05,
    given the OBSERVED discordance rate. For McNemar, with d = n*disc_rate discordant pairs
    and the effect expressed as the imbalance between them: delta = (b-c)/n."""
    if disc_rate <= 0:
        return float('inf')
    d = n_pairs * disc_rate
    # normal approximation: need |b-c| >= (1.96+0.84)*sqrt(d)  => delta >= 2.80*sqrt(d)/n
    return 100.0 * 2.80 * math.sqrt(d) / n_pairs


def contrast(a, bb, h):
    ra, ca = rows(a, h)
    rb, cb = rows(bb, h)
    keys = sorted(set(ra) & set(rb))
    n = len(keys)
    wa = sum(ra[k] for k in keys)
    wb = sum(rb[k] for k in keys)
    b_only = sum(1 for k in keys if rb[k] and not ra[k])   # bb wins, a loses
    c_only = sum(1 for k in keys if ra[k] and not rb[k])
    disc = b_only + c_only
    z = (b_only - c_only) / math.sqrt(disc) if disc else 0.0
    pa, pb = 100.0 * wa / n, 100.0 * wb / n
    return dict(n=n, pa=pa, pb=pb, d=pb - pa, disc=disc, bo=b_only, co=c_only, z=z,
                mde=mde(n, disc / n), sea=cluster_se(ca), seb=cluster_se(cb),
                bina=100 * math.sqrt(pa / 100 * (1 - pa / 100) / n),
                binb=100 * math.sqrt(pb / 100 * (1 - pb / 100) / n))


def node_cell(arm, h, kind):
    tot = wins = 0
    for b in BASES:
        j = load(arm, h, b)
        for r in j.get('byNodeKind', []):
            if r['nodeKind'] == kind:
                tot += r['n']; wins += r['n'] * r['winRate'] / 100.0
    return (100.0 * wins / tot if tot else float('nan')), tot


print('=' * 96)
print('P19 THE ROSTER CONTESTS — CRN round.  3 arms x 3 rungs x 8 slot bases x 20 (greedy+sloppy)')
print('n = 320 campaigns per rung per arm (160 CRN worlds x 2 policies); 2,880 campaigns total.')
print('=' * 96)
for a, bb, label in [('pre', 'boss', 'ITEM 1 alone   (pre -> boss)'),
                     ('boss', 'full', 'ITEM 2 alone   (boss -> full)'),
                     ('pre', 'full', 'SHIPPED, both  (pre -> full)')]:
    print(f'\n--- {label} ---')
    print(f'{"rung":>5} {"n":>5} {"base%":>7} {"arm%":>7} {"delta":>7} {"clSE_a":>7} {"clSE_b":>7} '
          f'{"n_disc":>7} {"b/c":>9} {"z":>6} {"MDE":>6}')
    for h in RUNGS:
        r = contrast(a, bb, h)
        print(f'h{h:<4} {r["n"]:5d} {r["pa"]:7.1f} {r["pb"]:7.1f} {r["d"]:+7.1f} '
              f'{r["sea"]:7.2f} {r["seb"]:7.2f} {r["disc"]:7d} {r["bo"]:4d}/{r["co"]:<4d} '
              f'{r["z"]:+6.2f} {r["mde"]:6.1f}')

print('\n--- THE MISSION-1 CELL (byNodeKind "Start") — the design contract forbids taxing it ---')
print(f'{"rung":>5}  {"pre":>14}  {"boss":>14}  {"full":>14}')
for h in RUNGS:
    cells = [node_cell(arm, h, 'Start') for arm in ARMS]
    print(f'h{h:<4}  ' + '  '.join(f'{c:6.1f}% (n={t:4d})' for c, t in cells))
print('\n--- THE ELITE-NODE CELL (byNodeKind "Elite") — the node the wave gave its content to ---')
print(f'{"rung":>5}  {"pre":>14}  {"boss":>14}  {"full":>14}')
for h in RUNGS:
    cells = [node_cell(arm, h, 'Elite') for arm in ARMS]
    print(f'h{h:<4}  ' + '  '.join(f'{c:6.1f}% (n={t:4d})' for c, t in cells))
print('\n--- byNodeKind "Combat" (the plain fights that LOST the m3 named elite) ---')
for h in RUNGS:
    cells = [node_cell(arm, h, 'Combat') for arm in ARMS]
    print(f'h{h:<4}  ' + '  '.join(f'{c:6.1f}% (n={t:4d})' for c, t in cells))
