using System;
using System.Collections.Generic;
using Raylib_cs;

namespace Sightline;

// PROGRAM HORIZON — Wave 3: WAR ROOM screen state + input.
//
// The WAR ROOM is a between-run meta screen reached off the intro. It surfaces the persistent SALVAGE
// currency, lifetime stats, ACHIEVEMENTS, a HALL OF FAME (recent legends), and an UNLOCK shop. The disk
// profile is loaded ONCE on entry into a cached snapshot (WarRoomProfile) so the draw path never churns
// I/O per frame; a buy refreshes it. All of this is player-facing only — never reached under NoPersist
// (the harness sets Phase.WarRoom directly for a screenshot but never enters via the intro flow, and no
// meta write happens under NoPersist because SpendSalvage/AddUnlock are only called from a real buy).
public partial class Game
{
    /// A cached snapshot of the persisted meta, loaded on WAR ROOM entry (+ after a buy) so the per-frame
    /// draw never touches disk. Public so Hud.DrawWarRoom can read it.
    public class WarRoomProfile
    {
        public int Salvage;
        public int Runs, Wins, BestMissions, BestWave;
        public int Veterans;   // COUNTERPLAY: size of the cross-run veteran reserve
        public HashSet<string> Achievements = new();
        public HashSet<int> Unlocks = new();
        public List<SaveGame.LegendDto> Legends = new();
    }

    public WarRoomProfile WarRoom;   // null unless in the WAR ROOM

    /// Enter the WAR ROOM: snapshot the profile from disk and switch phase. Under NoPersist (harness),
    /// disk is not read — SeedWarRoomDemo() can inject a demo profile for a screenshot instead.
    public void BeginWarRoom()
    {
        WarRoom = LoadWarRoomProfile();
        Phase = Phase.WarRoom;
        Audio.Play("select");
    }

    WarRoomProfile LoadWarRoomProfile()
    {
        var p = new WarRoomProfile();
        if (NoPersist) return p;   // harness: no disk read (byte-stable); demo is injected separately
        p.Salvage = SaveGame.LoadSalvage();
        var (runs, wins, best) = SaveGame.LoadRunTotals();
        p.Runs = runs; p.Wins = wins; p.BestMissions = best;
        p.BestWave = SaveGame.LoadMetaBestWave();
        p.Veterans = SaveGame.VeteranCount();
        foreach (var a in SaveGame.LoadAchievements()) p.Achievements.Add(a);
        foreach (var u in SaveGame.LoadUnlocks()) p.Unlocks.Add(u);
        p.Legends = SaveGame.LoadLegends();
        return p;
    }

    /// WAR ROOM input: BACK (button/Esc) returns to the intro; an unlock BUY spends salvage.
    void HandleWarRoomClick()
    {
        if (WarRoom == null) WarRoom = LoadWarRoomProfile();   // defensive (harness may set Phase directly)

        // BACK -> intro (button or Esc)
        bool back = (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                     Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Hud.WarRoomBack))
                    || Raylib.IsKeyPressed(KeyboardKey.Escape);
        if (back) { WarRoom = null; Phase = Phase.Intro; Audio.Play("select"); return; }

        // BUY an unlock (mouse only). The rects are published by Hud.DrawWarRoom.
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            var m = Raylib.GetMousePosition();
            foreach (var (unlock, rect) in Hud.WarRoomBuyBtns)
                if (Raylib.CheckCollisionPointRec(m, rect)) { TryBuyUnlock(unlock); return; }
        }
    }

    /// Purchase a meta-unlock: spend salvage (persisted), grant the unlock, refresh the cache. Never
    /// under NoPersist (a screenshot never clicks); guarded anyway so a demo profile can't corrupt disk.
    void TryBuyUnlock(MetaUnlock u)
    {
        if (WarRoom == null || NoPersist) return;
        if (WarRoom.Unlocks.Contains((int)u)) { Audio.Play("select"); return; }   // already owned
        int cost = MetaProg.UnlockCost(u);
        if (WarRoom.Salvage < cost) { Audio.Play("miss"); return; }               // can't afford
        if (SaveGame.SpendSalvage(cost))
        {
            SaveGame.AddUnlock((int)u);
            Audio.Play("hit");
            WarRoom = LoadWarRoomProfile();   // reflect the spend + new unlock immediately
        }
    }

    // ---- harness: seed a demo WAR ROOM profile for the SIGHTLINE_WARROOM screenshot ----
    /// Populate the cached profile with representative demo data (salvage / stats / achievements /
    /// legends / a couple owned unlocks) and switch to the WAR ROOM. Screenshot-only; touches NO disk.
    public void DebugWarRoom()
    {
        WarRoom = new WarRoomProfile
        {
            Salvage = 155,
            Runs = 12, Wins = 3, BestMissions = 6, BestWave = 14, Veterans = 5,
        };
        WarRoom.Achievements.Add("FIRST_WIN");
        WarRoom.Achievements.Add("HEAT3");
        WarRoom.Achievements.Add("DEEP");
        WarRoom.Achievements.Add("STAND5");
        WarRoom.Unlocks.Add((int)MetaUnlock.StartIntel);   // one owned, the rest buyable
        WarRoom.Legends.Add(new SaveGame.LegendDto { Name = "VEGA \"REAPER\"", Cls = "ASSAULT", Rank = "CAPTAIN", Kills = 21, Heat = 3, Won = true });
        WarRoom.Legends.Add(new SaveGame.LegendDto { Name = "NOX", Cls = "SHARPSHOOTER", Rank = "SERGEANT", Kills = 17, Heat = 3, Won = true });
        WarRoom.Legends.Add(new SaveGame.LegendDto { Name = "KRESS", Cls = "RANGER", Rank = "CORPORAL", Kills = 9, Heat = 2, Won = false });
        WarRoom.Legends.Add(new SaveGame.LegendDto { Name = "DRAKE", Cls = "GUNNER", Rank = "PRIVATE", Kills = 4, Heat = 0, Won = false });
        WarRoom.Legends.Add(new SaveGame.LegendDto { Name = "ILO", Cls = "CORPSMAN", Rank = "PRIVATE", Kills = 2, Heat = 1, Won = false });
        Phase = Phase.WarRoom;
    }

    // ── METATEST self-test (SIGHTLINE_METATEST): round-trips all W3 meta fields, and asserts an owned
    // unlock changes Run.Start-derived state under !NoPersist but NOT under NoPersist (the flywheel
    // byte-stability invariant). Preserves/restores the real meta.json (mirrors HordeSelfTest). ──────
    public string MetaSelfTest()
    {
        var fails = new List<string>();
        // Round-trip check runs through SaveGame.SelfTest's meta block indirectly, but do a focused
        // pass here too so METATEST stands alone. Preserve any real meta.json first.
        string metaSaved = System.IO.File.Exists(SaveGame.MetaPathPublic)
            ? System.IO.File.ReadAllText(SaveGame.MetaPathPublic) : null;
        try
        {
            // start from a clean meta so the assertions are deterministic
            try { if (System.IO.File.Exists(SaveGame.MetaPathPublic)) System.IO.File.Delete(SaveGame.MetaPathPublic); } catch { }

            // (1) salvage add/spend + refusal
            SaveGame.AddSalvage(100);
            if (SaveGame.LoadSalvage() != 100) fails.Add("salvageAdd");
            if (!SaveGame.SpendSalvage(40) || SaveGame.LoadSalvage() != 60) fails.Add("salvageSpend");
            if (SaveGame.SpendSalvage(9999)) fails.Add("salvageOverspend");
            if (SaveGame.LoadSalvage() != 60) fails.Add("salvageOverspendMutated");

            // (2) achievements idempotent
            if (!SaveGame.UnlockAchievement("FIRST_WIN")) fails.Add("achNew");
            if (SaveGame.UnlockAchievement("FIRST_WIN")) fails.Add("achDup");
            if (!SaveGame.LoadAchievements().Contains("FIRST_WIN")) fails.Add("achLoad");

            // (3) unlocks add/has
            SaveGame.AddUnlock((int)MetaUnlock.StartIntel);
            if (!SaveGame.HasUnlock((int)MetaUnlock.StartIntel)) fails.Add("unlockHas");
            if (SaveGame.HasUnlock((int)MetaUnlock.StartArmor)) fails.Add("unlockPhantom");

            // (4) legends prepend + cap 40
            SaveGame.AddLegends(new[] { new SaveGame.LegendDto { Name = "A" } });
            SaveGame.AddLegends(new[] { new SaveGame.LegendDto { Name = "B" } });
            var legs = SaveGame.LoadLegends();
            if (legs.Count < 2 || legs[0].Name != "B") fails.Add("legendsPrepend");
            for (int i = 0; i < 60; i++) SaveGame.AddLegends(new[] { new SaveGame.LegendDto { Name = "F" + i } });
            if (SaveGame.LoadLegends().Count != 40) fails.Add("legendsCap");

            // (5) run totals
            var (r0, w0, b0) = SaveGame.LoadRunTotals();
            SaveGame.RecordRunTotals(true, 6);
            SaveGame.RecordRunTotals(false, 2);
            var (r1, w1, b1) = SaveGame.LoadRunTotals();
            if (r1 != r0 + 2 || w1 != w0 + 1 || b1 != Math.Max(b0, 6)) fails.Add("runTotals");

            // (6) the byte-stability invariant: an owned StartIntel unlock adds +15 Intel to a fresh
            //     campaign run ONLY when NoPersist=false; under NoPersist it must be inert.
            //     StartIntel is owned from (3) above.
            {
                var gp = new Game { NoPersist = false };
                gp.StartMission(1);   // applies unlocks (StartIntel -> +15 Intel)
                var gh = new Game { NoPersist = true };
                gh.StartMission(1);   // harness path: unlocks NEVER apply
                // A fresh run's baseline Intel is 0 (Run.Start sets Intel=0); the persistent path gains +15.
                if (gp.RunState.Intel != gh.RunState.Intel + 15) fails.Add("unlockAppliedPersist");
                if (gh.RunState.Intel != 0) fails.Add("unlockLeakedHarness");
            }
        }
        catch (Exception e) { return "METATEST: FAIL (exception " + e.Message + ")"; }
        finally
        {
            if (metaSaved != null) { try { System.IO.File.WriteAllText(SaveGame.MetaPathPublic, metaSaved); } catch { } }
            else { try { if (System.IO.File.Exists(SaveGame.MetaPathPublic)) System.IO.File.Delete(SaveGame.MetaPathPublic); } catch { } }
        }
        return fails.Count == 0
            ? "METATEST: PASS (salvage/achievements/unlocks/legends/totals round-trip; unlock gated by NoPersist)"
            : "METATEST: FAIL (" + string.Join(",", fails) + ")";
    }
}
