using System;
using System.Collections.Generic;
using System.Linq;

namespace Sightline;

/// SIGHTLINE_VIPHEATTEST — P55. The protected asset answers HEAT.
///
/// P54 found the defect by cross-tabbing the archive, not by reading code, and the code had looked
/// fine for two programs: `Mission.MakeVip` is a tidy function of mission depth and P14 had already
/// audited it once (on the MODE axis) without asking the other question. So this gate asserts the
/// property that was missing rather than the implementation that provides it — **it reads the unit
/// the game builds, not the table the number came from**, which is the lesson `SIGHTLINE_FORCETEST`
/// was written for after L7.
///
/// WHAT IT CANNOT SEE: whether the dose is RIGHT. That is a measured round's job and no assertion
/// can stand in for it. This gate only says the asset answers the dial at all, by the argued
/// amount, at the rungs where the dial is non-empty — and that it answers it NOWHERE at the two
/// rungs that are inertness controls.
public static partial class Mission
{
    public static string VipHeatSelfTest()
    {
        var fails = new List<string>();
        var detail = new System.Text.StringBuilder();
        bool savedFlag = VipHeat;
        int savedDepth = ModeDepth;

        try
        {
            ModeDepth = -1;                       // campaign: DepthFor(n) == n

            // ── (A) THE SURCHARGE IS THE LADDER'S OWN NUMBER, CLAMPED AT ZERO ────────────────
            // Not a table of literals: a literal table would pass against a re-tuned ladder while
            // the asset silently stopped tracking it. The assertion is the RELATIONSHIP.
            VipHeat = true;
            for (int h = 0; h <= Heat.Max; h++)
            {
                var (hp, armor) = VipHeatBonus(h);
                int wantHp = Math.Max(0, Heat.StatDelta(h));
                int wantAr = Math.Max(0, Heat.DmgDelta(h));
                if (hp != wantHp)   fails.Add($"(A) h{h} hp surcharge {hp}, expected Heat.StatDelta clamped = {wantHp}");
                if (armor != wantAr) fails.Add($"(A) h{h} armor surcharge {armor}, expected Heat.DmgDelta clamped = {wantAr}");
                if (hp < 0 || armor < 0) fails.Add($"(A) h{h} produced a NEGATIVE surcharge ({hp}/{armor})");
            }

            // ── (B) THE TWO INERTNESS CONTROLS ARE EXACT, NOT APPROXIMATE ───────────────────
            // h0 because Heat.Active(0) is empty; RECRUIT because its StatDelta is -1 and the
            // clamp must not let a weaker force also SHRINK the asset. The round reads both as
            // controls, so a silent non-zero here would corrupt its baseline.
            {
                var z = VipHeatBonus(0);
                if (z != (0, 0)) fails.Add($"(B) h0 surcharge is {z}, must be (0,0) — Heat.Active(0) is empty");
                if (Heat.StatDelta(Heat.Recruit) > 0)
                    fails.Add("(B) RECRUIT's StatDelta is positive — this leg's premise is stale, re-derive it");
                var r = VipHeatBonus(Heat.Recruit);
                if (r != (0, 0)) fails.Add($"(B) RECRUIT surcharge is {r}, must be (0,0) — the clamp is what stops a weaker force shrinking the asset");
            }

            // ── (C) THE UNIT THE GAME BUILDS ACTUALLY CARRIES IT ────────────────────────────
            // L7's lesson: a cumulative pin cannot see a consumer that does not honour it. So this
            // reads MakeVip's OUTPUT at several depths and rungs, not VipHeatBonus again.
            {
                int worst = 0;
                for (int m = 1; m <= 6; m++)
                    for (int h = 0; h <= Heat.Max; h++)
                    {
                        var (bhp, bar) = VipHeatBonus(h);
                        var u = MakeVip(m, h);
                        int baseHp = 14 + 2 * m, baseAr = m / 2;
                        if (u.MaxHp != baseHp + bhp)
                            fails.Add($"(C) m{m} h{h}: MaxHp {u.MaxHp}, expected {baseHp}+{bhp}");
                        if (u.Armor != baseAr + bar)
                            fails.Add($"(C) m{m} h{h}: Armor {u.Armor}, expected {baseAr}+{bar}");
                        if (u.Hp != u.MaxHp) fails.Add($"(C) m{m} h{h}: spawned wounded ({u.Hp}/{u.MaxHp})");
                        worst = Math.Max(worst, u.MaxHp);
                    }
                detail.Append($"asset peaks at {worst} HP; ");
            }

            // ── (D) THE RESTORE FLAG IS AN EXACT RESTORE ───────────────────────────────────
            // Every rung, every depth, byte-for-byte the pre-P55 statline — because a flag that
            // only approximately restores cannot serve as a measurement arm.
            {
                int diffs = 0;
                for (int m = 1; m <= 6; m++)
                    for (int h = 0; h <= Heat.Max; h++)
                    {
                        VipHeat = false;
                        var off = MakeVip(m, h);
                        VipHeat = true;
                        if (off.MaxHp != 14 + 2 * m || off.Armor != m / 2) diffs++;
                    }
                if (diffs != 0) fails.Add($"(D) SIGHTLINE_VIPHEAT=0 differs from the pre-P55 statline on {diffs} of 54 cells");
            }

            // ── (E) IT IS RED AGAINST THE DEFECT, which is the only leg that proves anything ──
            // A gate whose red has never been seen is a comment (PARALLAX's thesis). The defect was
            // "heat reaches the asset nowhere", so the control IS the restore flag: with it off the
            // asset must be IDENTICAL across the whole ladder, and with it on it must not be.
            {
                VipHeat = false;
                var flat = Enumerable.Range(0, Heat.Max + 1).Select(h => MakeVip(4, h).MaxHp).Distinct().ToList();
                VipHeat = true;
                var live = Enumerable.Range(0, Heat.Max + 1).Select(h => MakeVip(4, h).MaxHp).Distinct().ToList();
                if (flat.Count != 1)
                    fails.Add($"(E) with the flag OFF the asset already varies with heat ({flat.Count} distinct) — the premise of this wave is wrong");
                if (live.Count < 2)
                    fails.Add("(E) with the flag ON the asset is STILL constant across the whole ladder — the fix does not reach the unit");
                detail.Append($"m4 HP across h0..h{Heat.Max}: off={flat.Count} distinct, on={live.Count} distinct; ");

                // and it must be MONOTONE — a surcharge that dips would make a rung easier than the
                // one below it, which is the shape L7 had to publish as a defect.
                VipHeat = true;
                var seq = Enumerable.Range(0, Heat.Max + 1).Select(h => MakeVip(4, h).MaxHp).ToList();
                for (int i = 1; i < seq.Count; i++)
                    if (seq[i] < seq[i - 1])
                        fails.Add($"(E) the asset SHRINKS from h{i - 1} ({seq[i - 1]}) to h{i} ({seq[i]})");
                detail.Append("seq " + string.Join("/", seq) + "; ");
            }
            // ── (F) THE WIRING, WHICH IS WHERE THE DEFECT ACTUALLY LIVED ───────────────────
            // Legs (A)-(E) all test `MakeVip`. **P54's defect was not in `MakeVip`** — it was that
            // `Game.SetupMission` built the asset sixteen lines before it read `_run.HeatLevel`, so
            // the function was simply never asked. A gate that stops at the function would have
            // been green through the entire defect. This one runs the real setup path and reads the
            // asset the GAME seated, for both objectives, at a low rung and the apex.
            foreach (var obj in new[] { Objective.Rescue, Objective.Escort })
            {
                var seen = new Dictionary<int, int>();
                foreach (int h in new[] { 0, 8 })
                {
                    int hp = Game.SeatedAssetHpForSelfTest(obj, 4, h);
                    if (hp <= 0) { fails.Add($"(F) {obj} h{h}: no asset was seated"); continue; }
                    seen[h] = hp;
                }
                if (seen.Count == 2)
                {
                    int want = VipHeatBonus(8).hp - VipHeatBonus(0).hp;
                    int got = seen[8] - seen[0];
                    detail.Append($"{obj} seated {seen[0]} -> {seen[8]} HP (h0 -> h8); ");
                    if (got != want)
                        fails.Add($"(F) {obj}: the SEATED asset gained {got} HP from h0 to h8, expected {want} — Game.SetupMission is not passing the heat");
                }
            }
        }
        catch (Exception e) { fails.Add($"threw: {e.Message}"); }
        finally { VipHeat = savedFlag; ModeDepth = savedDepth; }

        return fails.Count == 0
            ? "VIPHEATTEST: PASS (the protected asset's heat surcharge IS Heat.StatDelta/DmgDelta clamped at zero, "
              + "at every rung; h0 and RECRUIT are exactly (0,0) so both are inertness controls; the unit MakeVip "
              + "actually builds carries it at all 6 depths x all rungs and spawns unwounded; SIGHTLINE_VIPHEAT=0 "
              + "reproduces the pre-P55 statline on all 54 cells; and the flag is the RED CONTROL — off, the asset "
              + "is constant across the whole ladder, on, it is not, and it never shrinks; and the asset the real "
              + "Game.SetupMission SEATS gains the surcharge for BOTH objectives, which is the wiring P54's defect "
              + "actually lived in) [" + detail + "]"
            : "VIPHEATTEST: FAIL (" + string.Join(" | ", fails.Distinct()) + ") [" + detail + "]";
    }
}

public partial class Game
{
    /// P55 leg (F)'s access hook. `_run` and `SetupMission` are private to `Game`, and the leg has
    /// to go through the REAL setup path rather than call `Mission.MakeVip` again — the defect P54
    /// found was in this method's ORDERING, not in that function, so a gate that cannot reach here
    /// cannot see the class of bug it exists for. Harness-only: NoPersist, and it touches no disk.
    public static int SeatedAssetHpForSelfTest(Objective obj, int depth, int heat)
    {
        Util.Reseed(9100 + heat);
        var g = new Game { NoPersist = true, ForcedObjective = obj };
        g._run = new Run();
        g._run.Start();
        g._run.HeatLevel = heat;
        Util.Reseed(9100 + heat);
        g.SetupMission(depth);
        return g.Vip?.MaxHp ?? 0;
    }
}
