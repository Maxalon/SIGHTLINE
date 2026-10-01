# C3 — THE NEW BASELINE (the ten-mission run on the board curve)

**Base commit `65e34a9`** (`main` after P68). The binary snapshot was `runbin/C3`, built Release from that commit.
Heat is PINNED. 6 rungs (RECRUIT, h0, h2, h4, h6, h8) x 16 CRN slot bases (0..150), N=10 slots per chunk,
greedy+sloppy, so n=320 campaigns per rung per arm.
- 2 arms, 192 chunks, 3,840 campaigns.
- Zero BAD chunks. ARM CHECK PASS: every chunk's `levers.board` matches its arm and `runLength` is 10.

```
bash docs/measurements/c3/round.sh          # runs both arms, 4 chunks in parallel (~13 min here)
python3 docs/measurements/c3/analyse.py     # -> C3-TABLES.txt
```
- **curve** is the shipped game: 10 missions; missions 1-2 on 18x11, then the per-type big boards.
- **flat** is `SIGHTLINE_BOARDCURVE=0`: 10 missions, all on 18x11. It prices the curve alone.

**This is a new instrument.** Do not read it against the 18x11 six-mission ladder of record
(P24). The run is longer and the board is different, so it is a different game.

## The baseline (curve arm)

| rung | RECRUIT | h0 | h2 | h4 | h6 | h8 |
|---|---|---|---|---|---|---|
| **win%** | **65.0** | **50.6** | **33.8** | **18.8** | **5.6** | **2.5** |
| cluster SE | 1.65 | 3.02 | 2.80 | 2.64 | 1.57 | 0.79 |
| greedy / sloppy | 63.8 / 66.2 | 55.0 / 46.2 | 35.0 / 32.5 | 21.2 / 16.2 | 7.5 / 3.8 | 2.5 / 2.5 |

The ladder is monotone, and every step is resolved at this n. The steps are 14.4, 16.8, 15.0, 13.2 and 3.1.
**The top step (h6->h8) buys 3.1 points**, at the floor.

## The owner's target: "an inexperienced first run usually loses, but can win"

**A fresh profile starts its first run on RECRUIT** (`PendingHeat = Heat.Recruit`, W5's on-ramp).
On that rung the autopilot wins **65.0%**. The SLOPPY policy, the closer proxy for inexperience,
wins **66.2%**. **By the autopilot, the first run usually WINS.** That is the opposite of the
target, and it holds at h0 too, which reads 50.6 (sloppy 46.2).

**What this instrument cannot say:** how a human playing for the first time does. The autopilot
knows every rule and every key. Greedy and sloppy differ by about 2 points across the ladder
(skill is worth ~0.2 points on the archive, P26). So this is an upper bound on a beginner only if
the autopilot plays better than a beginner, and nothing here measures that. No lever was shipped,
because which rung a first run starts on is a design call.

## Where campaigns end (losses by mission number)

- **m1-2 are identical in both arms at every rung** (RECRUIT 2/16, h0 19/34, ... h8 24/62). The
  opener is 18x11 in both arms, so the two arms play the SAME world until mission 3. That doubles
  as a bridge check on the pairing.
- **Mission 2 is the opener's killer at every rung.** It ends 16-62 campaigns, more than mission 1
  at every rung.
- **Mission 3, the first big board, is a spike at the top.** It ends 78 (h6) and 99 (h8) campaigns
  against flat's 60 / 61. The losses at h6+ are RESCUE 46, STEAL 45, DECAPITATE 36, HACK 23.
- **The finale on a big board kills at the LOW rungs.** Mission 10 ends 16 (RECRUIT) and 32 (h0)
  campaigns against flat's 4 / 11. All 48 such losses at RECRUIT+h0 are DECAPITATE.
- **CAPTIVE LOST** at h6/h8 is 67 / 67 on the curve against 39 / 43 flat. That is the big-board
  rescue withdrawal (P64).

## Curve vs flat, paired on the same world

| rung | curve | flat | delta | b | c | z |
|---|---|---|---|---|---|---|
| RECRUIT | 65.0 | 67.5 | -2.5 | 52 | 60 | -0.76 |
| h0 | 50.6 | 45.6 | +5.0 | 72 | 56 | +1.41 |
| h2 | 33.8 | 36.9 | -3.1 | 52 | 62 | -0.94 |
| h4 | 18.8 | 25.6 | -6.9 | 40 | 62 | -2.18 |
| h6 | 5.6 | 6.6 | -0.9 | 17 | 20 | -0.49 |
| h8 | 2.5 | 2.8 | -0.3 | 7 | 8 | -0.26 |

**The curve is not a difficulty lever** (DESIGN §6.5), and the round agrees: the sign alternates,
and only h4 reaches |z| > 2 (one of six rungs, no correction). The per-mission picture above
moves a lot. The campaign total does not.

## Stalemates

- **Curve: 36 / 1,920 = 1.9%** (RESCUE 16, ELIMINATE 10, EVAC 7, ESCORT 2, DECAPITATE 1).
- **Flat: 98 / 1,920 = 5.1%.** A ten-mission run on 18x11 stalls more than either shipped
  configuration ever did. Flat is not a shipping configuration.
