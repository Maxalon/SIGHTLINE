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

    /// The extraction rules are in force for THIS mission.
    /// Harness-only: JUICETEST stages every verb on an 18x11 scene, where the rules are never live.
    public static bool ExtractionForceForTest;

    public bool ExtractionLive => ExtractionModel && (BigBoard || ExtractionForceForTest) && Mode == GameMode.Campaign
                                  && ExtractObjective(Objective) && EvacZone.Count > 0;

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
