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
        public int Veterans;      // COUNTERPLAY: size of the cross-run veteran reserve
        public int VeteranCap = SaveGame.MaxVeterans;   // P18: the cap this profile RUNS at (DEEP RESERVE)
        public int BestHeatWon = -1;   // P18 THE SECOND AXIS: highest heat rung ever CLEARED (-1 = none)
        public int DailyStreak;   // W9 SIGNAL: consecutive-day daily-win streak
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
        p.VeteranCap = SaveGame.VeteranCapNow();
        p.BestHeatWon = SaveGame.LoadBestHeatWon();   // P18: the heat-gated column's currency
        p.DailyStreak = SaveGame.LoadDailyStreak();
        foreach (var a in SaveGame.LoadAchievements()) p.Achievements.Add(a);
        foreach (var u in SaveGame.LoadUnlocks()) p.Unlocks.Add(u);
        p.Legends = SaveGame.LoadLegends();
        return p;
    }

    /// WAR ROOM BACK: always lands on the intro. Public so MODETEST can walk the end-card ->
    /// WAR ROOM -> BACK -> main menu path the player walks (P12 C1).
    public void ExitWarRoom() { WarRoom = null; Phase = Phase.Intro; Audio.Play("select"); }

    /// WAR ROOM input: BACK (button/Esc) returns to the intro; an unlock BUY spends salvage.
    void HandleWarRoomClick()
    {
        if (WarRoom == null) WarRoom = LoadWarRoomProfile();   // defensive (harness may set Phase directly)

        // BACK -> intro (button or Esc)
        bool back = (Raylib.IsMouseButtonPressed(MouseButton.Left) &&
                     Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), Hud.WarRoomBack))
                    || Raylib.IsKeyPressed(KeyboardKey.Escape);
        if (back) { ExitWarRoom(); return; }

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
        // P18 "THE SECOND AXIS": a heat-gated unlock is refused until the rung has been CLEARED, and
        // refused the same way an unaffordable one is — the ledger row publishes a hit-rect on
        // purpose (the standing invariant is that every unowned unlock has one), so the click has to
        // be ANSWERED. Salvage is never touched on this path: the gate is checked before the spend.
        if (!MetaProg.UnlockHeatMet(u, WarRoom.BestHeatWon)) { Audio.Cue(Audio.GameEvent.ShopNo); return; }
        int cost = MetaProg.UnlockCost(u);
        if (WarRoom.Salvage < cost) { Audio.Cue(Audio.GameEvent.ShopNo); return; }               // can't afford
        if (SaveGame.SpendSalvage(cost))
        {
            SaveGame.AddUnlock((int)u);
            Audio.Cue(Audio.GameEvent.ShopOk);
            WarRoom = LoadWarRoomProfile();   // reflect the spend + new unlock immediately
        }
    }

    /// Harness seam: METATEST drives the REAL buy path (the P18 heat gate lives inside it, and a
    /// test that re-implemented the gate could not see a gate that was never wired to the button).
    public void TryBuyUnlockForTest(MetaUnlock u) => TryBuyUnlock(u);

    // ---- W9 (SIGNAL): repeatable salvage SINKS (the TryBuyUnlock pattern: the UI reads a cached
    // bank, SpendSalvage refuses-and-spends-nothing when short, a success refreshes the cache).
    // All NoPersist-gated — the harness/flywheel can never reach disk through these. ----

    /// The salvage bank cached for the BARRACKS sink UI (loaded at EnterBarracks + after each spend;
    /// the draw path never touches disk). 0 under NoPersist. Always shows the AVAILABLE bank
    /// (disk minus the pending ledger below).
    public int BarracksSalvage;

    // ── W9 review fix: the barracks PENDING LEDGER ────────────────────────────────────────────
    // The scar REHAB and slate re-roll buy RUN-STATE goods, but the campaign only checkpoints at
    // MISSION START — a quit at the barracks reloads the mission with the scar back / the slate
    // re-derived, so an immediate meta write would burn durable money for rolled-back goods.
    // Barracks-phase sinks therefore accumulate HERE (in memory, never persisted) and the total
    // is committed via SpendSalvage inside SetupMission immediately BEFORE the checkpoint save —
    // the charge and the goods enter permanence in the same breath. Quit at the barracks -> the
    // pending total vanishes with the in-memory run: goods roll back AND the money never left.
    // NOT pending: ConfirmDraft's recall fee (immediately followed by the mission-start save) and
    // the draft pool re-roll (its good — seeing a fresh pool — is consumed on sight).
    int _pendingSalvage;
    /// The uncommitted barracks spend (test/UI visibility).
    public int PendingSalvage => _pendingSalvage;
    /// The bank a barracks sink may still spend against: disk MINUS the uncommitted pending total.
    int AvailableSalvage => NoPersist ? 0 : Math.Max(0, SaveGame.LoadSalvage() - _pendingSalvage);

    /// Commit the pending barracks spends to the durable meta. Called by SetupMission immediately
    /// BEFORE the mission-start checkpoint save (and only there), so the paid-for goods and the
    /// charge persist together or not at all.
    void CommitPendingSalvage()
    {
        if (NoPersist || _pendingSalvage <= 0) return;
        SaveGame.SpendSalvage(_pendingSalvage);   // affordability was enforced against disk-minus-pending
        _pendingSalvage = 0;
    }

    /// W9 sink: re-roll the run-opening draft candidate pool. Clears the current picks (those cards
    /// are gone) and re-recalls veterans from the reserve — nothing is charged for the picks
    /// themselves; only CONFIRM pays the recall fee. Charged IMMEDIATELY (not pending): the good —
    /// seeing a fresh pool — is consumed the moment it appears, so there is nothing to roll back.
    public void TryRerollDraftPool()
    {
        if (NoPersist || Phase != Phase.Draft) return;
        if (!SaveGame.SpendSalvage(MetaProg.DraftRerollCost)) { Audio.Cue(Audio.GameEvent.ShopNo); return; }
        DraftPool = BuildDraftPool();
        DraftPicked.Clear();
        DraftSalvage = SaveGame.LoadSalvage();
        Audio.Cue(Audio.GameEvent.ShopOk);
    }

    /// W9 sink: buy ONE scar off a soldier (their oldest first). A true undo of Run.GrantScar:
    /// BURN-SCARRED's granted max-HP is reverted and a VENDETTA's faction brand is cleared.
    /// PENDING-charged: committed at the next mission-start checkpoint (see the ledger above).
    public void TryBuyScarRemoval(Unit u)
    {
        if (NoPersist || _run == null || u == null || u.Scars.Count == 0) return;
        if (AvailableSalvage < MetaProg.ScarRehabCost) { Audio.Cue(Audio.GameEvent.ShopNo); return; }
        _pendingSalvage += MetaProg.ScarRehabCost;
        var s = u.Scars[0];
        u.Scars.RemoveAt(0);
        if (s == Scar.BurnScarred) { u.MaxHp = Math.Max(1, u.MaxHp - Unit.BurnScarHp); u.Hp = Math.Min(u.Hp, u.MaxHp); }
        if (s == Scar.Vendetta) u.VendettaFaction = Faction.None;
        BarracksSalvage = AvailableSalvage;
        _run.Report.Insert(0, $"{u.Name} rehabilitated - {ScarDef.Name(s)} bought off  (-{MetaProg.ScarRehabCost} salvage)");
        Audio.Cue(Audio.GameEvent.ShopOk);
    }

    /// W9 sink: re-roll this barracks' requisition slate (cheap + repeatable; each re-roll perturbs
    /// the deterministic slate seed via _shopReroll). PENDING-charged like the rehab.
    public void TryRerollShopSlate()
    {
        if (NoPersist || _run == null) return;
        if (AvailableSalvage < MetaProg.ShopRerollCost) { Audio.Cue(Audio.GameEvent.ShopNo); return; }
        _pendingSalvage += MetaProg.ShopRerollCost;
        _shopReroll++;
        RefreshShopOffer();
        BarracksSalvage = AvailableSalvage;
        Audio.Cue(Audio.GameEvent.ShopOk);
    }

    // ---- harness: seed a demo WAR ROOM profile for the SIGHTLINE_WARROOM screenshot ----
    /// Populate the cached profile with representative demo data (salvage / stats / achievements /
    /// legends / a couple owned unlocks) and switch to the WAR ROOM. Screenshot-only; touches NO disk.
    /// C6: `cold` renders the ZERO STATE — the WAR ROOM a player sees the first time they open it,
    /// before a single run has finished. Nobody had ever looked at it: this hook has hard-coded a
    /// rich demo profile since W3, so every WAR ROOM screenshot in the project's history is of a
    /// twelve-run career, and the screen a NEW player actually meets (no salvage, no legends, eight
    /// unearned achievements, every unlock unaffordable) had no photograph and no coverage.
    /// SIGHTLINE_COLD=1 alongside SIGHTLINE_WARROOM=1 selects it.
    public void DebugWarRoom(bool cold = false)
    {
        if (cold) { WarRoom = new WarRoomProfile(); Phase = Phase.WarRoom; return; }
        WarRoom = new WarRoomProfile
        {
            Salvage = 155,
            Runs = 12, Wins = 3, BestMissions = 6, BestWave = 14, Veterans = 5,
            DailyStreak = 3,   // W9: the streak row reads live
            // P18 THE SECOND AXIS: a twelve-run career that has cleared heat 3 — so the staged shot
            // shows the axis in BOTH of its states at once (COMBAT TRIALS earned and buyable; DEEP
            // RESERVE and DEEP STORES still locked behind their rungs). A demo profile that had
            // cleared nothing would photograph the axis as three grey rows and prove less.
            BestHeatWon = 3,
            VeteranCap = SaveGame.MaxVeterans,
        };
        WarRoom.Achievements.Add("FIRST_WIN");
        WarRoom.Achievements.Add("HEAT3");
        WarRoom.Achievements.Add("DEEP");
        WarRoom.Achievements.Add("STAND5");
        WarRoom.Achievements.Add("DAILY_WIN");             // W9: one of the new daily achievements lit
        WarRoom.Unlocks.Add((int)MetaUnlock.StartIntel);   // one owned, the rest buyable
        WarRoom.Unlocks.Add((int)MetaUnlock.Quartermaster);   // W9: a new horizontal unlock owned
        WarRoom.Legends.Add(new SaveGame.LegendDto { Name = "VEGA \"REAPER\"", Cls = "ASSAULT", Rank = "CAPTAIN", Kills = 21, Heat = 3, Won = true });
        // THE FIT: the staged Hall of Fame used to be five SHORT legends, so a human looking at
        // this screen saw ~50px of slack and no reason to suspect the row was one text-size step
        // from touching the panel border. NOX now carries the longest rank+class pair the game
        // can produce (LIEUTENANT SHARPSHOOTER) plus a nickname — the worst case, staged.
        WarRoom.Legends.Add(new SaveGame.LegendDto { Name = "NOX \"MAVERICK\"", Cls = "SHARPSHOOTER", Rank = "LIEUTENANT", Kills = 17, Heat = 3, Won = true });
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
        // W9 fix (routed from W1's discovery): legs (6)+(7) run StartMission with NoPersist=false,
        // which reaches SaveGame.Save(_run) inside SetupMission — WITHOUT this stash METATEST silently
        // OVERWRITES a real player's save.json. Preserve/restore it exactly like meta.json above.
        string saveSaved = System.IO.File.Exists(SaveGame.SavePathPublic)
            ? System.IO.File.ReadAllText(SaveGame.SavePathPublic) : null;
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

            // ── (3b) P18 "THE SECOND AXIS" — the HEAT-GATED column ────────────────────────────
            // THE DEFECT IT GUARDS. Six salvage unlocks cost 330 in total; a heat-0 clear banks 61.
            // The shop therefore empties after ~5 wins while the heat cap — which rises by one per
            // win AT the cap, to Heat.Max = 8 — keeps climbing for three more. The three unlocks
            // below are gated on a heat rung CLEARED rather than on salvage banked, so the reward
            // curve's domain is the difficulty curve's domain. Asserted UNCONDITIONALLY, so
            // `SIGHTLINE_SECONDAXIS=0 SIGHTLINE_METATEST=1` FAILS — the proof this leg can see it.
            {
                if (MetaProg.HeatUnlocks.Length == 0) fails.Add("axisEmpty");
                // every gated unlock is in the catalogue, gated, and APPENDED (ordinal > every
                // pre-P18 member) — the save format's own rule, checked here as well as in SAVETEST.
                foreach (var hu in MetaProg.HeatUnlocks)
                {
                    if (System.Array.IndexOf(MetaProg.AllUnlocks, hu) < 0) fails.Add("axisNotListed:" + hu);
                    if (!MetaProg.IsHeatGated(hu)) fails.Add("axisNotGated:" + hu);
                    if ((int)hu <= (int)MetaUnlock.StandingReserve) fails.Add("axisNotAppended:" + hu);
                    if (MetaProg.UnlockHeatGate(hu) > Sightline.Heat.Max) fails.Add("axisGateUnreachable:" + hu);
                }
                // the top gate must be REACHABLE, and it is only reachable through BestHeatWon:
                // UnlockedHeat stops rising at Heat.Max, so a heat-8 clear moves no ceiling at all.
                int top = 0;
                foreach (var hu in MetaProg.HeatUnlocks) top = Math.Max(top, MetaProg.UnlockHeatGate(hu));
                if (top != Sightline.Heat.Max) fails.Add($"axisTopGate={top}(want {Sightline.Heat.Max})");

                // BestHeatWon: raises only, round-trips, and MIGRATES a pre-P18 profile off MaxHeat.
                SaveGame.RecordBestHeatWon(3);
                if (SaveGame.LoadBestHeatWon() != 3) fails.Add("bestHeatRoundTrip");
                SaveGame.RecordBestHeatWon(1);
                if (SaveGame.LoadBestHeatWon() != 3) fails.Add("bestHeatLowered");
                SaveGame.RecordBestHeatWon(Sightline.Heat.Max);
                if (SaveGame.LoadBestHeatWon() != Sightline.Heat.Max) fails.Add("bestHeatCeiling");
                // a profile written before the field existed: MaxHeat 6 == cleared 5.
                System.IO.File.WriteAllText(SaveGame.MetaPathPublic, "{\"MaxHeat\":6,\"Salvage\":9999}");
                if (SaveGame.LoadBestHeatWon() != 5) fails.Add($"bestHeatMigrate={SaveGame.LoadBestHeatWon()}");

                // THE GATE ITSELF, through the REAL buy path: a rich profile that has cleared
                // nothing cannot buy a gated unlock, and the refusal must not cost it a single
                // point of salvage (the pre-spend order — this is the leg a "check after
                // SpendSalvage" implementation would fail).
                System.IO.File.WriteAllText(SaveGame.MetaPathPublic, "{\"Salvage\":9999}");
                var gm = new Game { NoPersist = false };
                gm.BeginWarRoom();
                var gated = MetaProg.HeatUnlocks[0];
                int gate = MetaProg.UnlockHeatGate(gated);
                gm.TryBuyUnlockForTest(gated);
                if (SaveGame.HasUnlock((int)gated)) fails.Add("axisBoughtWhileLocked");
                if (SaveGame.LoadSalvage() != 9999) fails.Add($"axisLockedRefundLeak={SaveGame.LoadSalvage()}");
                // clear the rung and the SAME click now lands
                SaveGame.RecordBestHeatWon(gate);
                gm.BeginWarRoom();
                gm.TryBuyUnlockForTest(gated);
                if (!SaveGame.HasUnlock((int)gated)) fails.Add("axisUnbuyableAfterGateMet");
                if (SaveGame.LoadSalvage() != 9999 - MetaProg.UnlockCost(gated)) fails.Add("axisNotCharged");

                // DEEP RESERVE actually widens the reserve the WAR ROOM reports and EnshrineVeterans caps at.
                if (SaveGame.VeteranCapNow() != SaveGame.MaxVeterans) fails.Add("reserveCapEarly");
                SaveGame.AddUnlock((int)MetaUnlock.DeepReserve);
                if (SaveGame.VeteranCapNow() != MetaProg.DeepReserveCap) fails.Add("reserveCapUnlock");

                // and the column's row plan still paints (and publishes a hit-rect for) every
                // unowned entry at every owned/unowned split once LOCKED rows take their space.
                for (int owned = 0; owned <= MetaProg.AllUnlocks.Length; owned++)
                {
                    int rest = MetaProg.AllUnlocks.Length - owned;
                    for (int locked = 0; locked <= 1 && locked <= rest; locked++)
                    {
                        var pl = Hud.WarUnlockPlan(Hud.WarColumnHeight, owned, rest - locked, locked);
                        if (pl.drawn != Math.Max(0, rest - locked - 1))
                            fails.Add($"axisPlanDropped owned={owned} locked={locked} drew={pl.drawn}");
                        if (pl.panelH > Hud.WarColumnHeight) fails.Add($"axisPlanOverflow owned={owned} locked={locked}");
                        if (pl.descRows > 0 && pl.bodySize < 12) fails.Add($"axisSubFloorBody owned={owned}");
                    }
                }
                try { if (System.IO.File.Exists(SaveGame.MetaPathPublic)) System.IO.File.Delete(SaveGame.MetaPathPublic); } catch { }
                SaveGame.AddSalvage(60);   // restore the bank legs (5)+ below expect
                SaveGame.AddUnlock((int)MetaUnlock.StartIntel);
                SaveGame.UnlockAchievement("FIRST_WIN");
            }

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

            // (6b) FUL-2: a run END refreshes the IN-SESSION assist cache. _metaLossStreak was only
            //      ever assigned in EnsureMetaLoaded, so a second run started in the same sitting
            //      inherited the pre-loss streak (and AssistPreview lied on the intro).
            {
                var gl = new Game { NoPersist = false };
                gl.StartMission(1);
                gl._metaLossStreak = 7;                     // simulate a stale sitting cache
                int before = gl.RunState.LossStreak;
                gl.LoseRun("METATEST", "assist-cache leg");
                if (gl.RunState.LossStreak != before + 1) fails.Add("lossStreakNotGrown");
                if (gl._metaLossStreak != gl.RunState.LossStreak) fails.Add("lossStreakCacheStale");
            }

            // (6c) W5 THE DOORS: the end card's "N JOIN THE RESERVE" line can never over-claim.
            //      Game.EndReserve is set as the DELTA of SaveGame.VeteranCount() across
            //      EnshrineVeterans, so the card's count is the number that actually landed on
            //      disk — not vets.Count, which would double-count a survivor who was ALREADY a
            //      reserve record (a recalled veteran who came home again). Both legs asserted.
            {
                var gr = new Game { NoPersist = false };
                gr.StartMission(1);
                foreach (var u in gr.RunState.Squad) if (!u.IsVip) u.Rank = 2;   // all reserve-eligible
                int vetsBefore = SaveGame.VeteranCount();
                gr.LoseRun("METATEST", "reserve-delta leg");
                int delta = SaveGame.VeteranCount() - vetsBefore;
                if (gr.EndReserve != delta) fails.Add($"reserveDelta={gr.EndReserve}!={delta}");
                if (gr.EndReserve > SaveGame.VeteranCount()) fails.Add("reserveOverClaim");
                // ...and re-enshrining the SAME names adds nobody, so the card must read 0.
                var gr2 = new Game { NoPersist = false };
                gr2.StartMission(1);
                var names = new List<string>();
                foreach (var u in gr.RunState.Squad) if (!u.IsVip) names.Add(u.Name);
                for (int i = 0; i < gr2.RunState.Squad.Count && i < names.Count; i++)
                { gr2.RunState.Squad[i].Name = names[i]; gr2.RunState.Squad[i].Rank = 2; }
                int before2 = SaveGame.VeteranCount();
                gr2.LoseRun("METATEST", "reserve-rejoin leg");
                if (gr2.EndReserve != SaveGame.VeteranCount() - before2) fails.Add("reserveRejoinDelta");
                // ...and when EVERY survivor was already a reserve record under the same name, the
                // delta is exactly 0 — vets.Count would have claimed the whole squad joined again.
                int renamed = Math.Min(gr2.RunState.Squad.Count, names.Count);
                if (renamed == gr2.RunState.Squad.Count(u => !u.IsVip) && gr2.EndReserve != 0)
                    fails.Add("reserveRejoinNotZero=" + gr2.EndReserve);
            }

            // ── W9 (SIGNAL): the standing economy ─────────────────────────────────────────────
            // Start from a wiped meta again so the pricing/bounty numbers are deterministic.
            try { if (System.IO.File.Exists(SaveGame.MetaPathPublic)) System.IO.File.Delete(SaveGame.MetaPathPublic); } catch { }

            // (7) PRICED RECALL: ConfirmDraft charges the summed 10+8xRank fee exactly ONCE; picks
            //     pre-confirm never touch the bank (the W1 BACK path is charge-free by construction);
            //     an unaffordable CONFIRM refuses — nothing charged, nothing seated, picks intact.
            {
                SaveGame.AddSalvage(60);
                var vr3 = new Unit { Name = "VEGA", Cls = "ASSAULT", Team = Team.Player, MaxHp = 12, Hp = 12, Aim = 80, Mobility = 8, Kills = 9, Rank = 3, Alive = true, Weapon = Weapon.Make(WeaponKind.Rifle) };
                var vr1 = new Unit { Name = "NOX", Cls = "SHARPSHOOTER", Team = Team.Player, MaxHp = 8, Hp = 8, Aim = 82, Mobility = 6, Kills = 4, Rank = 1, Alive = true, Weapon = Weapon.Make(WeaponKind.Sniper) };
                SaveGame.EnshrineVeterans(new[] { vr3, vr1 });

                var gd = new Game { NoPersist = false };
                gd.BeginDraft();
                var vets = gd.DraftPool.FindAll(u => u.FromReserve);
                if (vets.Count != 2) fails.Add($"draftVets={vets.Count}");
                foreach (var v in vets) gd.DraftPicked.Add(v);
                foreach (var u in gd.DraftPool)
                { if (gd.DraftPicked.Count >= DraftCap) break; if (!u.FromReserve) gd.DraftPicked.Add(u); }
                gd.DraftSelectedBoon = gd.DraftBoonOffer.Count > 0 ? gd.DraftBoonOffer[0] : Boon.Marksmen;
                int expect = MetaProg.RecallCost(3) + MetaProg.RecallCost(1);   // 34 + 18 = 52
                if (expect != 52) fails.Add($"recallTable={expect}");
                if (gd.DraftRecallCost != expect) fails.Add($"recallCost={gd.DraftRecallCost}");
                // picks selected but NOT confirmed -> the bank is untouched (BACK charges nothing)
                if (SaveGame.LoadSalvage() != 60) fails.Add("chargedPreConfirm");
                gd.ConfirmDraft();   // affordable (60 >= 52): charges once, seats the squad
                if (SaveGame.LoadSalvage() != 60 - expect) fails.Add($"confirmCharge={SaveGame.LoadSalvage()}");
                if (gd.Phase == Phase.Draft) fails.Add("confirmDidNotProceed");
                int seatedVets = 0;
                foreach (var u in gd.RunState.Squad) if (u.FromReserve) seatedVets++;
                if (seatedVets != 2) fails.Add($"seatedVets={seatedVets}");

                // unaffordable CONFIRM (bank now 8 < 18): refuse, keep picks, charge nothing, seat nothing
                var g2 = new Game { NoPersist = false };
                g2.BeginDraft();
                var vets2 = g2.DraftPool.FindAll(u => u.FromReserve);
                if (vets2.Count != 2) fails.Add($"draftVets2={vets2.Count}");
                foreach (var v in vets2) g2.DraftPicked.Add(v);
                foreach (var u in g2.DraftPool)
                { if (g2.DraftPicked.Count >= DraftCap) break; if (!u.FromReserve) g2.DraftPicked.Add(u); }
                g2.DraftSelectedBoon = g2.DraftBoonOffer.Count > 0 ? g2.DraftBoonOffer[0] : Boon.Marksmen;
                int bank = SaveGame.LoadSalvage();
                g2.ConfirmDraft();
                if (g2.Phase != Phase.Draft) fails.Add("brokeConfirmProceeded");
                if (SaveGame.LoadSalvage() != bank) fails.Add("brokeConfirmCharged");
                if (g2.DraftPicked.Count != DraftCap) fails.Add("brokeConfirmLostPicks");
                // a fresh Game holds a default empty Run (never null) — "seats no veteran" means the
                // refused confirm never ran StartMission, so nothing was drafted into the squad.
                int seated2 = 0;
                if (g2.RunState?.Squad != null) foreach (var u in g2.RunState.Squad) if (u.FromReserve) seated2++;
                if (seated2 != 0) fails.Add("brokeConfirmSeated");

                // (8) SINKS — the PENDING LEDGER (review fix): barracks sinks (slate re-roll, scar
                //     rehab) charge NOTHING to the durable meta until the next mission-start
                //     checkpoint, so a quit at the barracks rolls back the goods AND keeps the
                //     money. The draft pool re-roll stays an immediate charge (consumed on sight).
                SaveGame.AddSalvage(100);
                gd.BarracksSalvage = SaveGame.LoadSalvage();
                string slate0 = string.Join(",", gd.ShopOffer());
                int disk0 = SaveGame.LoadSalvage();
                gd.TryRerollShopSlate();
                if (SaveGame.LoadSalvage() != disk0) fails.Add("slatePendingWroteMeta");   // a quit HERE keeps the money
                if (gd.PendingSalvage != MetaProg.ShopRerollCost) fails.Add("slatePendingLedger");
                bool rotated = string.Join(",", gd.ShopOffer()) != slate0;
                for (int i = 0; i < 2 && !rotated; i++)   // a coincidental identical shuffle is possible; 3 tries isn't
                { gd.TryRerollShopSlate(); rotated = string.Join(",", gd.ShopOffer()) != slate0; }
                if (!rotated) fails.Add("shopRerollStatic");

                // scar REHAB: pending +30, scar removed, BURN-SCARRED max-HP grant reverted — disk untouched
                var scarred = gd.RunState.Squad[0];
                int hp0 = scarred.MaxHp;
                scarred.Scars.Add(Scar.BurnScarred);
                scarred.MaxHp += Unit.BurnScarHp; scarred.Hp = scarred.MaxHp;   // mimic Run.GrantScar
                int pend0 = gd.PendingSalvage;
                gd.TryBuyScarRemoval(scarred);
                if (scarred.Scars.Count != 0) fails.Add("rehabScarStuck");
                if (scarred.MaxHp != hp0) fails.Add("rehabBurnHpNotReverted");
                if (SaveGame.LoadSalvage() != disk0) fails.Add("rehabPendingWroteMeta");
                if (gd.PendingSalvage != pend0 + MetaProg.ScarRehabCost) fails.Add("rehabPendingLedger");
                // The two disk asserts above ARE the simulated quit/reload guarantee: meta.json still
                // holds disk0 and the scar removal lives only in this in-memory run — a reload from
                // the checkpoint restores the scar while the bank never moved.

                // ... and the next mission-start checkpoint commits EXACTLY the pending total, once.
                int owed = gd.PendingSalvage;
                gd.SetupMission(2);
                if (SaveGame.LoadSalvage() != disk0 - owed) fails.Add($"pendingCommit={SaveGame.LoadSalvage()}(want{disk0 - owed})");
                if (gd.PendingSalvage != 0) fails.Add("pendingNotCleared");

                // affordability reads disk MINUS pending: with bank 34, one rehab (30) fits; a second
                // rehab (avail 4 < 30) and a slate re-roll (avail 4 < 5) must both refuse untouched.
                SaveGame.SpendSalvage(SaveGame.LoadSalvage());   // drain the bank
                SaveGame.AddSalvage(34);
                gd.BarracksSalvage = 34;
                scarred.Scars.Add(Scar.ShellShocked); scarred.Scars.Add(Scar.HardBitten);
                gd.TryBuyScarRemoval(scarred);
                if (scarred.Scars.Count != 1 || gd.PendingSalvage != MetaProg.ScarRehabCost) fails.Add("pendingAffordFirst");
                gd.TryBuyScarRemoval(scarred);
                if (scarred.Scars.Count != 1) fails.Add("rehabBrokeRemoved");
                gd.TryRerollShopSlate();
                if (gd.PendingSalvage != MetaProg.ScarRehabCost) fails.Add("brokeSinkPended");
                gd.SetupMission(3);
                if (SaveGame.LoadSalvage() != 4) fails.Add($"pendingCommit2={SaveGame.LoadSalvage()}");

                // draft-pool re-roll: IMMEDIATE -10 (not pending), picks cleared, pool refilled, cache refreshed
                SaveGame.AddSalvage(30);
                var g3 = new Game { NoPersist = false };
                g3.BeginDraft();
                g3.DraftPicked.Add(g3.DraftPool[0]);
                int bank2 = SaveGame.LoadSalvage();
                g3.TryRerollDraftPool();
                if (SaveGame.LoadSalvage() != bank2 - MetaProg.DraftRerollCost) fails.Add("draftRerollCharge");
                if (g3.DraftPicked.Count != 0) fails.Add("draftRerollKeptPicks");
                if (g3.DraftPool.Count != Run.DraftPoolSize) fails.Add("draftRerollPoolSize");
                if (g3.DraftSalvage != SaveGame.LoadSalvage()) fails.Add("draftRerollCacheStale");

                // (9) HORIZONTAL UNLOCKS: QUARTERMASTER widens the slate by exactly one;
                //     STANDING RESERVE recalls a third veteran; CROSS-TRAINING only ever deals
                //     class-legal weapons (ArmoryOptions — a sidegrade, never off-role).
                gd.RefreshShopOffer();   // rebase the cache on the CURRENT mission before comparing
                int slotsBefore = gd.ShopOffer().Count;
                SaveGame.AddUnlock((int)MetaUnlock.Quartermaster);
                gd.RefreshShopOffer();
                if (gd.ShopOffer().Count != slotsBefore + 1) fails.Add("quartermasterSlate");
                var vr2 = new Unit { Name = "KRESS", Cls = "RANGER", Team = Team.Player, MaxHp = 9, Hp = 9, Aim = 70, Mobility = 7, Kills = 6, Rank = 2, Alive = true, Weapon = Weapon.Make(WeaponKind.Shotgun) };
                SaveGame.EnshrineVeterans(new[] { vr2 });
                SaveGame.AddUnlock((int)MetaUnlock.StandingReserve);
                SaveGame.AddSalvage(100);
                var g4 = new Game { NoPersist = false };
                g4.BeginDraft();
                int vets3 = 0;
                foreach (var u in g4.DraftPool) if (u.FromReserve) vets3++;
                if (vets3 != 3) fails.Add($"standingReserve={vets3}");
                var poolCT = Run.GenerateDraftPool(null, Run.MaxDraftVeterans, true);
                foreach (var u in poolCT)
                    if (Array.IndexOf(Weapon.ArmoryOptions(u.Cls), u.Weapon.Kind) < 0) fails.Add("crossTrainIllegal");
            }

            // (10) DAILY PAYOUT: a daily win pays 10+heat salvage ONCE per stamp (+ the DAY SHIFT
            //      achievement bounty); a same-stamp re-win pays nothing; five consecutive-day wins
            //      drive the streak to 5 and unlock DAWN PATROL; a gap resets the streak to 1.
            {
                try { if (System.IO.File.Exists(SaveGame.MetaPathPublic)) System.IO.File.Delete(SaveGame.MetaPathPublic); } catch { }
                var gdaily = new Game { NoPersist = true };
                gdaily.BeginDaily();               // deterministic stamp under NoPersist
                gdaily.NoPersist = false;          // flip so EndSkirmish's payout path runs (meta is stashed)
                int stamp0 = gdaily.DailyStamp;
                int s0 = SaveGame.LoadSalvage();
                gdaily.EndSkirmish(true);
                int paid = SaveGame.LoadSalvage() - s0;
                int want = 10 + gdaily.RunState.HeatLevel + MetaProg.AchievementSalvage;
                if (paid != want) fails.Add($"dailyBounty={paid}(want{want})");
                if (!SaveGame.LoadAchievements().Contains("DAILY_WIN")) fails.Add("dailyAch");
                if (SaveGame.LoadDailyStreak() != 1) fails.Add($"dailyStreak={SaveGame.LoadDailyStreak()}");
                int s1 = SaveGame.LoadSalvage();
                gdaily.EndSkirmish(true);          // SAME stamp again -> must pay nothing more
                if (SaveGame.LoadSalvage() != s1) fails.Add("dailyDoublePaid");
                // climb 4 more consecutive calendar days through the real wiring -> streak 5 + DAWN PATROL
                var d0 = new DateTime(stamp0 / 10000, stamp0 / 100 % 100, stamp0 % 100);
                for (int i = 1; i <= 4; i++)
                {
                    var di = d0.AddDays(i);
                    gdaily.DailyStamp = di.Year * 10000 + di.Month * 100 + di.Day;
                    gdaily.EndSkirmish(true);
                }
                if (SaveGame.LoadDailyStreak() != 5) fails.Add($"streakClimb={SaveGame.LoadDailyStreak()}");
                if (!SaveGame.LoadAchievements().Contains("STREAK5")) fails.Add("streak5Ach");
                // a gap (non-consecutive day) resets the streak to 1 — primitive-level. Also the
                // review-fix contract: pay + mark are ONE write — the returned paid=true must come
                // with the bounty already banked (bounty 7 here, asserted via the salvage delta).
                int sGap = SaveGame.LoadSalvage();
                var (gp2, gs2) = SaveGame.RecordDailyWin(stamp0 + 8000, 7);   // not next-day
                if (!gp2 || gs2 != 1) fails.Add("streakGapReset");
                if (SaveGame.LoadSalvage() != sGap + 7) fails.Add("dailyPayMarkNotAtomic");
            }

            // ── (11) FUL-10 CONTRACT FORKS: MRC + LGD engage the veteran economy end-to-end ──
            {
                try { if (System.IO.File.Exists(SaveGame.MetaPathPublic)) System.IO.File.Delete(SaveGame.MetaPathPublic); } catch { }

                // (11a) MRC: the bill halves per veteran (round up, EXACTLY once — pinned against
                // the un-discounted table), ConfirmDraft charges the discounted total, and run end
                // enshrines NOBODY.
                SaveGame.AddSalvage(60);
                var m3 = new Unit { Name = "VEGA", Cls = "ASSAULT", Team = Team.Player, MaxHp = 12, Hp = 12, Aim = 80, Mobility = 8, Kills = 9, Rank = 3, Alive = true, Weapon = Weapon.Make(WeaponKind.Rifle) };
                var m1 = new Unit { Name = "NOX", Cls = "SHARPSHOOTER", Team = Team.Player, MaxHp = 8, Hp = 8, Aim = 82, Mobility = 6, Kills = 4, Rank = 1, Alive = true, Weapon = Weapon.Make(WeaponKind.Sniper) };
                SaveGame.EnshrineVeterans(new[] { m3, m1 });
                var gm = new Game { NoPersist = false };
                gm.BeginDraft();
                foreach (var v in gm.DraftPool.FindAll(u => u.FromReserve)) gm.DraftPicked.Add(v);
                foreach (var u in gm.DraftPool)
                { if (gm.DraftPicked.Count >= DraftCap) break; if (!u.FromReserve) gm.DraftPicked.Add(u); }
                gm.DraftSelectedBoon = gm.DraftBoonOffer.Count > 0 ? gm.DraftBoonOffer[0] : Boon.Marksmen;
                gm.DraftSelectedContract = Contract.MercenaryClause;
                int half = (MetaProg.RecallCost(3) + 1) / 2 + (MetaProg.RecallCost(1) + 1) / 2;   // 17 + 9 = 26
                if (gm.DraftRecallCost != half) fails.Add($"mrcBill={gm.DraftRecallCost}(want{half})");
                gm.ConfirmDraft();
                if (SaveGame.LoadSalvage() != 60 - half) fails.Add($"mrcCharge={SaveGame.LoadSalvage()}");
                if (gm.RunState.Contract != Contract.MercenaryClause) fails.Add("mrcNotThreaded");
                // wipe the reserve, then end the run: the ranked recalled survivors must NOT re-enshrine
                if (SaveGame.RemoveVeterans(new[] { "VEGA", "NOX" }) != 2 || SaveGame.VeteranCount() != 0)
                    fails.Add("removeVeterans");
                gm.LoseRun("METATEST", "mrc leg");
                if (SaveGame.VeteranCount() != 0) fails.Add("mrcEnshrined");

                // (11b) LGD: pensions pay ONCE at run end (folded into the bounty with the pending
                // event claims), survivors STILL enshrine, and the fallen erase EXACTLY their own
                // reserve records — nobody else's.
                var m2 = new Unit { Name = "KRESS", Cls = "RANGER", Team = Team.Player, MaxHp = 9, Hp = 9, Aim = 70, Mobility = 7, Kills = 6, Rank = 2, Alive = true, Weapon = Weapon.Make(WeaponKind.Shotgun) };
                SaveGame.EnshrineVeterans(new[] { m3, m1, m2 });   // reserve: VEGA / NOX / KRESS
                var gl2 = new Game { NoPersist = false };
                gl2.StartMission(1);
                gl2.RunState.Contract = Contract.LivingLegends;
                var sq = gl2.RunState.Squad;
                sq[0].Rank = 2; sq[0].Name = "ALPHA";     // pensionable
                sq[1].Rank = 3; sq[1].Name = "BRAVO";     // pensionable
                sq[2].Rank = 1; sq[2].Name = "CHARLIE";   // below the Rank>=2 bar -> no pension
                gl2.RunState.Fallen.Add("NOX");           // matches a reserve record -> erased
                gl2.RunState.Fallen.Add("NOBODY");        // matches nothing -> no-op
                gl2.RunState.PendingSalvageReward = 25;   // an event claim rides the same commit
                int bank0 = SaveGame.LoadSalvage();
                gl2.LoseRun("METATEST", "lgd leg");
                int wantPay = 25 + MetaProg.LegendPension * (2 + 3);   // consolation at m1/h0 is 0
                if (SaveGame.LoadSalvage() != bank0 + wantPay) fails.Add($"lgdPension={SaveGame.LoadSalvage() - bank0}(want{wantPay})");
                if (gl2.RunState.PendingSalvageReward != 0) fails.Add("claimNotCleared");
                var reserve = SaveGame.LoadVeterans();
                if (reserve.Exists(v => v.Name == "NOX")) fails.Add("lgdKiaKept");
                if (!reserve.Exists(v => v.Name == "VEGA") || !reserve.Exists(v => v.Name == "KRESS")) fails.Add("lgdOvercull");
                if (!reserve.Exists(v => v.Name == "BRAVO")) fails.Add("lgdEnshrineSkipped");   // LGD never blocks enshrine
            }
            // (12) R2 FIX 4 — the unlock CEILING can never lock the difficulty picker.
            // MaxHeat is a ceiling, not a dialled level: W5 moved Heat.Clamp's floor to -1
            // (RECRUIT) and LoadMetaHeat clamped through it, so a corrupt/edited {"MaxHeat":-9}
            // produced UnlockedHeat = -1 -> PendingHeat pinned to -1 -> BOTH intro steppers dead
            // (minus needs level > Heat.Min, plus needs level < unlocked). Assert the load floors
            // at 0, that the write path floors too, and that the picker's own predicates stay live.
            foreach (int bad in new[] { -9, -1, 0, 3, 9999 })
            {
                System.IO.File.WriteAllText(SaveGame.MetaPathPublic, "{\"MaxHeat\":" + bad + "}");
                int unlocked = SaveGame.LoadMetaHeat();
                if (unlocked < 0 || unlocked > Sightline.Heat.Max)
                    fails.Add($"metaHeat({bad})={unlocked} outside 0..{Sightline.Heat.Max}");
                // EnsureMetaLoaded's exact two lines, then the two stepper predicates from
                // Game.UpdateIntro — the picker must be able to move in at least one direction.
                int pending = Math.Min(0, unlocked);
                bool minusLive = pending > Sightline.Heat.Min, plusLive = pending < unlocked;
                if (!minusLive && !plusLive)
                    fails.Add($"pickerLOCKED at meta MaxHeat={bad} (unlocked={unlocked} pending={pending})");
            }
            SaveGame.SaveMetaHeat(-9);
            if (SaveGame.LoadMetaHeat() != 0) fails.Add($"saveMetaHeat(-9)={SaveGame.LoadMetaHeat()} want 0");
            SaveGame.SaveMetaHeat(9999);
            if (SaveGame.LoadMetaHeat() != Sightline.Heat.Max) fails.Add("saveMetaHeat(9999) not capped");

            // W9 THE REPAIR — EVERY UNOWNED UNLOCK MUST GET A CARD (and so a WarRoomBuyBtns hit-rect).
            // THE GAP: this test covers the unlock MODEL exhaustively — round-trip, HasUnlock, the
            // Quartermaster/StandingReserve effects — and never once asked whether the WAR ROOM can
            // DRAW them. The only WAR ROOM screenshot hook, DebugWarRoom, hard-codes a demo profile
            // with TWO already owned, which is exactly the configuration that fits (4 unowned needs
            // 416px in a 446px column). On a FRESH PROFILE all six want 508px and DrawWarUnlocks' old
            // overflow `break` fired before WarRoomBuyBtns.Add, so STANDING RESERVE — the third
            // cheapest unlock in the game — was invisible AND unclickable in the one profile state
            // every new player is in. There is no keyboard path to an unlock, only the mouse rects.
            // Pure geometry, so it needs no window: assert the column's row plan paints every unowned
            // entry at EVERY owned/unowned split, which also fails loudly the day a MetaUnlock is
            // appended past what the column can hold.
            {
                int n = MetaProg.AllUnlocks.Length;
                int colH = Hud.WarColumnHeight;
                for (int owned = 0; owned <= n; owned++)
                {
                    int unowned = n - owned;
                    var plan = Hud.WarUnlockPlan(colH, owned, unowned);
                    int wantCompact = Math.Max(0, unowned - 1);   // the hero card carries the first
                    if (plan.drawn != wantCompact)
                        fails.Add($"warUnlockDropped owned={owned} unowned={unowned} drew={plan.drawn}/{wantCompact}");
                    if (plan.panelH > colH) fails.Add($"warUnlockPanelOverflow owned={owned}");
                    if (unowned > 1 && plan.cardH < 20) fails.Add($"warUnlockCardCollapsed owned={owned}");
                    // W9 REVIEW FIX — the 12px SMALL-TEXT FLOOR, asserted where it broke. The first
                    // cut let a compressed card run FitWrap, whose floor is CardBodyMinSize = 10, so
                    // a FRESH PROFILE (0 owned / 6 unowned -> cardH 49 -> descRows 1) painted five of
                    // six unlock descriptions at 10px. A body row is now either >= 12px or absent.
                    if (plan.descRows > 0 && plan.bodySize < 12)
                        fails.Add($"warUnlockSubFloorBody owned={owned} size={plan.bodySize} rows={plan.descRows}");
                    // and a card that claims description rows must have the height to paint them
                    if (plan.descRows > 0 && plan.cardH < 27 + plan.descRows * Hud.TextRowPublic(13))
                        fails.Add($"warUnlockBodyOverflows owned={owned} cardH={plan.cardH} rows={plan.descRows}");
                }
            }
        }
        catch (Exception e) { return "METATEST: FAIL (exception " + e.Message + ")"; }
        finally
        {
            if (metaSaved != null) { try { System.IO.File.WriteAllText(SaveGame.MetaPathPublic, metaSaved); } catch { } }
            else { try { if (System.IO.File.Exists(SaveGame.MetaPathPublic)) System.IO.File.Delete(SaveGame.MetaPathPublic); } catch { } }
            // W9 fix: restore (or remove) save.json exactly as it was before the test ran.
            if (saveSaved != null) { try { System.IO.File.WriteAllText(SaveGame.SavePathPublic, saveSaved); } catch { } }
            else { try { if (System.IO.File.Exists(SaveGame.SavePathPublic)) System.IO.File.Delete(SaveGame.SavePathPublic); } catch { } }
        }
        return fails.Count == 0
            ? "METATEST: PASS (salvage/achievements/unlocks/legends/totals round-trip; unlock gated by NoPersist; "
              + "recall charged once in ConfirmDraft + broke-confirm refuses; barracks sinks pend until the checkpoint commit "
              + "(quit-at-barracks keeps the money); daily bounty once-per-stamp, pay+mark atomic; save.json preserved; "
              + "a corrupt MaxHeat can never lock the difficulty picker; the WAR ROOM publishes a buy-rect for every unowned unlock at every owned/unowned split, and never paints an unlock body below the 12px floor; "
              + "P18 THE SECOND AXIS: three heat-gated unlocks appended at the END, gates 2/5/8 with the top ON Heat.Max, BestHeatWon raises-only + migrates off MaxHeat, a locked buy is refused BEFORE the spend, and DEEP RESERVE widens the reserve cap)"
            : "METATEST: FAIL (" + string.Join(",", fails) + ")";
    }
}
