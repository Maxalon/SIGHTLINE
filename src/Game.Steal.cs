using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Sightline;

/// B4 — STEAL, the objective the owner proposed (2026-10-01, `docs/DESIGN.md` §6.6):
///
/// > Stealing might also be an interesting objective, like stealing a flash drive or PC or a piece of
/// > technology that could be picked up by a soldier with extraction mechanics, but that object can be
/// > passed between soldiers as well.
///
/// * The LOOT sits mid-board. A soldier on or next to it GRABs it (1 action) and becomes the CARRIER.
/// * The carrier can PASS it to an adjacent soldier (1 action) — one verb slot, shared with HACK/PLANT
///   (no letter is free), labelled by what it would do.
/// * A carrier that goes down or dies DROPS it on its tile; anyone can pick it back up.
/// * It leaves through the extraction: aboard with the carrier, or in the zone with the carrier at the
///   CALL. Calling without it is refused; pulling everyone out without it loses the run.
///
/// WHERE IT LIVES. Adding STEAL to the campaign's objective deal would move `Run.GenerateMap`'s draw
/// order — a save-format break and a new CRN world. So it takes ESCORT->RESCUE's route instead: on a
/// big board an EVAC node (the thinnest objective, "walk to the exit") plays as STEAL, labelled so.
/// `SIGHTLINE_OBJ=steal` forces it anywhere; on 18x11 it wins with the carrier standing in the zone.
public partial class Game
{
    /// On a big board an EVAC node plays as STEAL (gated with the rest of the extraction model).
    public static bool EvacIsSteal => ExtractionModel && BigBoard && StealOnEvac;

    /// `SIGHTLINE_STEAL=0` keeps a big-board EVAC node a plain EVAC (B1's board-and-call rule only).
    public static bool StealOnEvac = true;

    public bool HasLoot => Objective == Objective.Steal;
    public (int x, int y) LootTile;
    public Unit Carrier;
    public bool LootAboard;

    public bool LootOnGround => HasLoot && Carrier == null && !LootAboard;

    /// The loot is out, or in the zone in the hands of a living carrier (so a CALL takes it).
    bool LootSecured => LootAboard
        || (Carrier != null && Carrier.Alive && EvacZone.Contains((Carrier.X, Carrier.Y)));

    public bool CanGrab(Unit u) =>
        LootOnGround && u != null && u.Team == Team.Player && !u.IsVip && u.Alive && !u.Downed && u.CanAct
        && Players.Contains(u) && Util.ChebyDist(u.X, u.Y, LootTile.x, LootTile.y) <= 1;

    /// Who the carrier would hand it to: an adjacent, able soldier — the one nearest the extraction,
    /// because a pass exists to move the loot forward.
    public Unit PassTarget(Unit u)
    {
        if (!HasLoot || u == null || u != Carrier || !u.CanAct || !Players.Contains(u)) return null;
        Unit best = null; int bestD = int.MaxValue;
        foreach (var p in Players)
        {
            if (p == u || !p.Alive || p.Downed || p.IsVip || p.Team != Team.Player) continue;
            if (Util.ChebyDist(u.X, u.Y, p.X, p.Y) > 1) continue;
            int d = EvacZone.Count > 0 ? EvacZone.Min(t => Util.ChebyDist(p.X, p.Y, t.x, t.y)) : 0;
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    public bool CanPass(Unit u) => PassTarget(u) != null;

    /// The shared objective verb's label for this soldier on a STEAL mission.
    public string LootVerbLabel(Unit u) => u != null && u == Carrier ? "PASS" : "GRAB";

    void DoGrabOrPass()
    {
        var u = Selected;
        if (CanGrab(u))
        {
            Carrier = u;
            u.ActionsLeft -= 1;
            Stats.RecordAction("GRAB");
            // QUIET LIFT (P66): no HackNoise here - measured below; the withdrawal is the pressure
            var at = Util.TileCenter(LootTile.x, LootTile.y);
            Fx.PopText(at + new Vector2(0, -30), "LOOT SECURED", Pal.VipGold, 22f);
            Fx.Burst(at, Pal.VipGold, 20, 220f, 0.55f, 4f, true);
            Fx.AddShake(4f);
            Audio.Cue(Audio.GameEvent.Objective);
            ShowBanner("LOOT SECURED - GET IT TO THE EVAC", true);
            BannerSub = "a carrier who goes down drops it";
            return;
        }
        var to = PassTarget(u);
        if (to == null) return;
        Carrier = to;
        u.ActionsLeft -= 1;
        Stats.RecordAction("PASS");
        Fx.PopText(to.Pos + new Vector2(0, -30), "PASSED", Pal.VipGold, 20f);
        Fx.Burst(to.Pos, Pal.VipGold, 12, 160f, 0.4f, 3f, true);
        Audio.Play("select");
    }

    /// A carrier that goes down or dies lets go of it where it fell.
    void DropLoot(Unit u)
    {
        if (!HasLoot || u == null || u != Carrier || LootAboard) return;
        LootTile = (u.X, u.Y);
        Carrier = null;
        Fx.PopText(Util.TileCenter(u.X, u.Y) + new Vector2(0, -44), "LOOT DROPPED", Pal.Foe, 22f);
    }

    /// Mission setup: the loot sits mid-board on a cleared tile (its ring cleared so it can be reached).
    void SeatLoot()
    {
        LootTile = (Grid.W / 2, Grid.H / 2);
        Carrier = null; LootAboard = false;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = LootTile.x + dx, ny = LootTile.y + dy;
                if (Grid.InBounds(nx, ny) && !IsOccupiedByOther(nx, ny, null))
                { Grid.Tiles[nx, ny] = TileType.Floor; Grid.Barrel[nx, ny] = false; }
            }
        Grid.ResetCoverHp();
    }

    /// Autopilot: the whole squad converges on the loot; whoever reaches it grabs it; then everyone
    /// runs the extraction brain (the carrier included — SmartEvac beelines for the zone).
    bool SmartSteal(Unit u)
    {
        if (CanGrab(u)) { DoGrabOrPass(); return true; }
        if (LootOnGround)
        {
            if (Util.ChebyDist(u.X, u.Y, LootTile.x, LootTile.y) > 1)
            {
                if (HasStrongShot(u) && TakeBestShot(u)) return true;
                if (TryMoveTowardTile(u, LootTile.x, LootTile.y)) return true;
            }
            if (TakeBestShot(u)) return true;
            return false;
        }
        return SmartEvac(u);
    }
}
