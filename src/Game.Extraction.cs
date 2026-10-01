using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Sightline;

/// B1 — THE EXTRACTION MODEL, as the owner specified it (`docs/DESIGN.md` §6.6, 2026-10-01):
///
/// > Soldiers can board the extraction one by one, but the mission doesn't end on its own without
/// > everyone still alive on board. The mission can be ended as soon as one soldier is extracted,
/// > but that would leave any soldier not on board the extraction or in the extraction zone behind.
///
/// * BOARD — a unit standing in the zone with an action left leaves the board. It is safe: it is
///   moved from `Players` to `Aboard`, so no enemy, vision pass or turn-flow rule can see it.
/// * The mission NEVER ends on its own while a living unit is on the ground.
/// * CALL EVAC — once one unit is aboard (and the asset is secured, on an asset mission) the player
///   may end it: everything in the zone gets out with the boarded, everything else is LEFT BEHIND
///   and dies (a real KIA through `KillUnit`).
///
/// LIVE ON BIG BOARDS ONLY (`BigBoard`), and in the campaign. The 18x11 tutorial band and every gate
/// pinned to it keep today's rules by construction. `SIGHTLINE_EXTRACTION=0` restores them on a big
/// board as well. B1 covers the objectives that already extract (EVAC, ESCORT, RESCUE); B2 adds HACK
/// and SABOTAGE, B3 reshapes ESCORT, B4 adds STEAL.
public partial class Game
{
    /// `SIGHTLINE_EXTRACTION=0` restores the pre-B1 end conditions on a big board.
    public static bool ExtractionModel = true;

    /// The owner's direction calls the 18x11 board the tutorial band. Any board larger than the
    /// reference on either axis plays by the big-board rules.
    public static bool BigBoard => Cfg.GridW > Mission.RefW || Cfg.GridH > Mission.RefH;

    /// Units that have boarded the extraction this mission. Off the board; rejoin `Players` when the
    /// mission ends so the debrief sees them.
    public readonly List<Unit> Aboard = new();

    static bool ExtractObjective(Objective o) => o == Objective.Evac || o == Objective.Escort || o == Objective.Rescue;
    static bool TaskObjective(Objective o) => o == Objective.Hack || o == Objective.Sabotage;

    /// Harness-only: JUICETEST stages every verb on an 18x11 scene, where the rules are never live.
    public static bool ExtractionForceForTest;

    bool ExtractionArena => ExtractionModel && (BigBoard || ExtractionForceForTest) && Mode == GameMode.Campaign;

    /// B2 — HACK / SABOTAGE on a big board: the evac opens when the task is done, the mission ALSO
    /// ends when every hostile is dead, and the pressure clock fields no reinforcements.
    public bool TaskExtractRules => ExtractionArena && TaskObjective(Objective);

    /// B2: the extraction a task mission will open, reserved at setup so Build keeps it clear, and
    /// hidden until the task is done.
    public readonly HashSet<(int x, int y)> PendingEvac = new();
    public bool EvacOpen;

    bool TaskDone => Objective == Objective.Hack ? HackProgress >= HackRequired
                   : Objective == Objective.Sabotage && SabotageSites.Count > 0 && SabotageBlown.Count >= SabotageSites.Count;

    /// The extraction rules are in force for THIS mission.
    public bool ExtractionLive => ExtractionArena && EvacZone.Count > 0
                                  && (ExtractObjective(Objective) || (TaskObjective(Objective) && EvacOpen));

    /// B2: the 2x4 evac block a task mission will open — on the far (east) edge, in whichever corner
    /// is farther from the objective, so the withdrawal is a real walk. Zero RNG draws.
    void ReserveTaskEvac()
    {
        int sy = Objective == Objective.Sabotage && SabotageSites.Count > 0
            ? (int)Math.Round(SabotageSites.Average(t => t.y))
            : Terminal.y;
        bool bottom = sy < Grid.H / 2;          // objective in the top half -> extract bottom-right
        for (int k = 0; k < 4; k++)
        {
            int ey = bottom ? Grid.H - 1 - k : k;
            EvacZone.Add((Grid.W - 2, ey));
            EvacZone.Add((Grid.W - 1, ey));
        }
    }

    /// Called right after Build: the reserved block goes into hiding.
    void StashTaskEvac()
    {
        PendingEvac.Clear();
        foreach (var t in EvacZone) PendingEvac.Add(t);
        EvacZone.Clear();
        EvacOpen = false;
    }

    void OpenTaskEvac()
    {
        foreach (var t in PendingEvac) EvacZone.Add(t);
        EvacOpen = true;
        ShowBanner(Objective == Objective.Hack ? "DATA SECURED - GET TO THE EVAC" : "CHARGES SET - GET TO THE EVAC",
                   true, Audio.CueFor(Audio.GameEvent.Objective));
        BannerSub = "board at the extraction zone - or clear the field";
        var zc = Vector2.Zero;
        foreach (var t in EvacZone) zc += Util.TileCenter(t.x, t.y);
        zc /= Math.Max(1, EvacZone.Count);
        Fx.PopText(zc + new Vector2(0, -30), "EVAC", Pal.Good, 24f);
    }

    bool AssetMission => Objective == Objective.Escort || Objective == Objective.Rescue;

    /// The asset is out, or standing in the zone and free (so a CALL takes it along).
    bool AssetSecured => !AssetMission
        || (Vip != null && (Aboard.Contains(Vip)
            || (Vip.Alive && !CaptiveLocked && EvacZone.Contains((Vip.X, Vip.Y)))));

    public bool CanBoard(Unit u) =>
        ExtractionLive && u != null && u.Team == Team.Player && u.Alive && !u.Downed && u.CanAct
        && !(u.IsVip && CaptiveLocked) && EvacZone.Contains((u.X, u.Y)) && Players.Contains(u);

    public bool CanCallEvac => ExtractionLive && Aboard.Any(a => !a.IsVip) && AssetSecured
                               && Phase == Phase.PlayerTurn;

    void DoBoard()
    {
        var u = Selected;
        if (!CanBoard(u)) return;
        Stats.RecordAction("BOARD");
        Fx.Burst(u.Pos, u.IsVip ? Pal.VipGold : Pal.Friend, 18, 240f, 0.5f, 4f, true);
        Fx.PopText(u.Pos + new Vector2(0, -28), "ABOARD", u.IsVip ? Pal.VipGold : Pal.Friend, 22f);
        Fx.AddShake(3f);
        Audio.Play("select");
        u.ActionsLeft = 0;
        Players.Remove(u);
        Aboard.Add(u);
        Selected = Players.FirstOrDefault(p => p.Alive && p.CanAct && !p.IsVip)
                ?? Players.FirstOrDefault(p => p.Alive);
        AimMode = false;
        if (_anims.Count == 0) CheckEnd();
    }

    void DoCallEvac()
    {
        if (!CanCallEvac) return;
        Stats.RecordAction("CALLEVAC");
        var ground = Players.Where(p => p.Alive).ToList();
        int left = 0;
        foreach (var p in ground)
        {
            if (EvacZone.Contains((p.X, p.Y)) && !(p.IsVip && CaptiveLocked))
            {
                Players.Remove(p); Aboard.Add(p);       // in the zone: out with the rest
            }
            else
            {
                // LEFT BEHIND. Marked down first so KillUnit takes the outright-death branch rather
                // than opening a bleed-out timer on a mission that is over.
                p.LastDotSource = "LEFT BEHIND";
                p.Downed = true;
                KillUnit(p);
                left++;
            }
        }
        // feel: the bird lifts — a burst over every zone tile, one readout, a shake
        var zc = Vector2.Zero;
        foreach (var t in EvacZone) { var c = Util.TileCenter(t.x, t.y); zc += c; Fx.Burst(c, Pal.Good, 6, 200f, 0.6f, 3f, true); }
        zc /= Math.Max(1, EvacZone.Count);
        Fx.PopText(zc + new Vector2(0, -36), $"EXTRACTED x{Aboard.Count}", Pal.Good, 26f);
        Fx.AddShake(5f);
        LeftBehindLastMission = left;
        if (left > 0) ShowBanner(left == 1 ? "1 SOLDIER LEFT BEHIND" : $"{left} SOLDIERS LEFT BEHIND", true);
        FinishExtraction();
    }

    /// How many soldiers the last CALL EVAC abandoned (harness/telemetry read).
    public int LeftBehindLastMission;

    /// Everyone aboard rejoins the roster and the mission is won.
    void FinishExtraction()
    {
        foreach (var a in Aboard) if (!Players.Contains(a)) Players.Add(a);
        Aboard.Clear();
        EnterBarracks();
    }

    /// CheckEnd's extraction branch. Returns true when it decided the mission (or decided that
    /// nothing may end it yet); false hands control back to the normal checks (a wipe, a lost asset).
    bool CheckExtractionEnd(List<Unit> alivePlayers)
    {
        if (TaskExtractRules)
        {
            if (alivePlayers.Count == 0 && Aboard.Count == 0) return false;          // a true wipe
            // GUNS BLAZING: a cleared field ends a task mission whether or not the task got done
            if (AliveEnemies().Count == 0) { FinishExtraction(); return true; }
            if (!EvacOpen)
            {
                if (TaskDone && PendingEvac.Count > 0) OpenTaskEvac();
                else return true;               // the task is the only way forward; nothing ends yet
            }
        }
        if (!ExtractionLive) return false;
        // a dead asset that never got out is lost exactly as before — let the objective branch say so
        if (AssetMission && (Vip == null || (!Vip.Alive && !Aboard.Contains(Vip)))) return false;
        if (alivePlayers.Count == 0)
        {
            if (Aboard.Count == 0) return false;               // a true wipe: the normal valve/loss
            if (!AssetSecured)
            {
                LoseRun(Objective == Objective.Rescue ? "CAPTIVE ABANDONED" : "VIP ABANDONED",
                        $"The squad pulled out without the asset on mission {_run.Mission}.");
                return true;
            }
            FinishExtraction();                                // everyone alive is aboard
            return true;
        }
        return true;   // someone is still on the ground: only BOARD or CALL EVAC can end this
    }

    // ══ B3 — RESCUE AND ESCORT ARE ONE MISSION; THE WITHDRAWAL IS OPPOSED ═══════════════════════
    /// The owner: "rescue and escort (which in my mind are the same, what's the difference?)". Today
    /// ESCORT starts with the asset in the squad, which fails §6.5's fiction check (why bring it into
    /// a combat zone?). On a big board an ESCORT node plays as RESCUE — the asset is REACHED mid-board
    /// — and is labelled so (`Codex.ObjectiveName`). `Objective` is append-only, so the member stays.
    public static bool EscortIsRescue => ExtractionModel && BigBoard;

    /// `SIGHTLINE_WITHDRAWAL=0` turns the reinforcement trickle off (the remap stays).
    public static bool WithdrawalWaves = true;

    /// Once the asset is reached, hostiles arrive one or two at a time from the extraction's half of
    /// the board, every player turn, until the squad is out — so the walk out gets harder the longer
    /// it takes. Replaces the pressure clock's reinforcements on these missions.
    public bool WithdrawalLive => WithdrawalWaves && ExtractionLive && AssetMission
                                  && Vip != null && Vip.Alive && !CaptiveLocked;

    /// Harness read: hostiles the withdrawal has fielded this mission.
    public int WithdrawalSpawned;

    /// The tiles a withdrawal hostile may arrive on: the board's PERIMETER, on the extraction's side
    /// of the long axis, and never within `WithdrawalStandoff` of the zone — the first cut put them
    /// on the east edge in the zone's own rows, i.e. ON the exit, and the measured batch went from
    /// 7/8 wins to 0/8. The exit is where the squad is going, not where the opposition lives.
    public const int WithdrawalStandoff = 6;

    List<(int x, int y)> WithdrawalTiles()
    {
        var list = new List<(int x, int y)>();
        if (EvacZone.Count == 0) return list;
        bool east = EvacZone.Average(t => t.x) >= Grid.W / 2.0;
        for (int x = 0; x < Grid.W; x++)
            for (int y = 0; y < Grid.H; y++)
            {
                bool rim = x == 0 || y == 0 || x == Grid.W - 1 || y == Grid.H - 1;
                if (!rim || (east ? x < Grid.W / 2 : x >= Grid.W / 2)) continue;
                if (!Grid.IsFloor(x, y) || IsOccupiedByOther(x, y, null)) continue;
                if (EvacZone.Any(t => Util.ChebyDist(t.x, t.y, x, y) < WithdrawalStandoff)) continue;
                list.Add((x, y));
            }
        return list;
    }

    /// The per-mission ceiling on withdrawal arrivals: it grows with heat, so the curve stays the
    /// ladder's, not the board's (§6.5: board size is not a difficulty lever).
    public int WithdrawalCap => 4 + (_run?.HeatLevel ?? 0) / 2;

    void MaybeWithdrawalTrickle()
    {
        if (!WithdrawalLive || WithdrawalSpawned >= WithdrawalCap) return;
        int heat = _run?.HeatLevel ?? 0;
        int want = Math.Min(1 + (heat >= 4 ? 1 : 0), WithdrawalCap - WithdrawalSpawned);
        var tiles = WithdrawalTiles();
        int depth = Mission.DepthFor(_run?.Mission ?? 1);
        int got = 0;
        while (got < want && tiles.Count > 0 && AliveEnemies().Count < 12)
        {
            int i = Util.RandInt(0, tiles.Count - 1);
            var (x, y) = tiles[i]; tiles.RemoveAt(i);
            var e = Mission.MakeWaveHostile(depth, x, y, false, 0);
            e.Alert = AlertLevel.Alert;              // they know where the squad is going
            e.PodId = -1;
            e.SyncPos();
            Stats.RecordSpawn(e.Cls, Combat.MissionFaction != Faction.None);
            Enemies.Add(e);
            Fx.Burst(e.Pos, Pal.Foe, 14, 160f, 0.5f, 3f, true);
            got++;
        }
        if (got == 0) return;
        WithdrawalSpawned += got;
        RefreshCombatRoster();
        Stats.RecordReinforce(got);
        Audio.Cue(Audio.GameEvent.Reinforce, foe: true);
        ShowBanner(got == 1 ? "HOSTILE INBOUND - EXTRACTION SIDE" : $"{got} HOSTILES INBOUND - EXTRACTION SIDE", true,
                   Audio.CueFor(Audio.GameEvent.Reinforce));
        BannerSub = WithdrawalSpawned >= WithdrawalCap ? "that's all of them - get out" : "the longer the walk, the more of them";
    }

    /// Harness/autopilot: board everyone who can, then call it once nobody on the ground can still
    /// reach the zone on their own (everyone left is in it, or down).
    bool AutoExtractStep()
    {
        if (!ExtractionLive) return false;
        // on an asset mission the escort stays with the asset until it is secured: a squad that
        // boards early leaves the asset to walk the last tiles alone
        if (AssetSecured)
            foreach (var p in Players.ToList())
                if (CanBoard(p)) { Selected = p; DoBoard(); return true; }
        if (CanCallEvac && Players.Where(p => p.Alive).All(p => EvacZone.Contains((p.X, p.Y)) || p.Downed))
        { DoCallEvac(); return true; }
        return false;
    }
}
