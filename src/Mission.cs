using System;
using System.Collections.Generic;

namespace Sightline;

/// Builds battlefields and squads. The player squad persists across a run
/// (see Run); each mission regenerates the map + a scaled hostile force.
public static partial class Mission
{
    // Staggered deployment footprint: four soldiers across cols 0-3 in a loose wedge
    // (upper-forward / back-left / lower-forward / back-right), plus a 5th slot for the
    // VIP/captive near squad centre. Spread deliberately avoids a single-column firing-line
    // while staying in the left third (cols 0-3) so standoff to the mid-field screen holds.
    // Up to 6 soldier spawns (deploy size grows to 6 in the back half) + the VIP slot LAST
    // (the seating loop always seats the VIP at PlayerSpawns[Length-1]). A loose left-side wedge
    // in cols 0-3, all distinct, clear of the VIP's (2,5) seat.
    static readonly (int x, int y)[] PlayerSpawns = { (2, 2), (0, 4), (3, 5), (1, 7), (0, 9), (3, 8), (2, 5) };

    // Per-pod column offsets for enemy spawns: vary across cols 14-17 so the right side
    // doesn't mirror a parallel firing line. Indexed by pod id (i/2), cycling if more pods.
    static readonly int[] EnemyPodColOffset = { 1, 3, 0, 2, 1, 3 };


    // ════════════════════════ W4 "THE SECOND AXIS" — DEPLOYMENT GEOMETRY ════════════════════════
    // Every fight in the game opened the same way: squad in cols 0-3, every pod in cols 14-17, a
    // single left-to-right push. X1's instrumentation proved the binding decision-density
    // constraint is `choices/ARMED-soldier-turn` (~1.5 — the typical armed soldier sees exactly
    // ONE worthwhile target), and named the cause: a single advancing front presents pods
    // SERIALLY. This block makes the opening geometry a per-mission variable so the force can be
    // presented on more than one bearing at once.
    //
    // HARD CONTRACT (load-bearing for every CRN pairing in the project): the shape is derived
    // PURELY from (DeckSeed, missionNum) by an FNV-1a mix — ZERO Util.Rng draws, exactly like the
    // FUL-9 arena deck. SIGHTLINE_PAIRTEST is the gate.
    public const int DeployFrontal = 0;    // today: squad west, every pod on the east edge
    public const int DeployPincer = 1;     // squad west; the force splits front + both flanks
    public const int DeployCrossfire = 2;  // squad west; two dense masses, NE and SE
    public const int DeployEnvelop = 3;    // squad CENTRE, pods on every rim (the surrounded open)
    public const int DeployShapes = 4;

    /// Measurement/harness pin (SIGHTLINE_DEPLOY): -1 = the shipped mix, >=0 pins one shape for
    /// every mission. A pinned ENVELOP still falls back where the objective forbids it (below).
    public static int ForcedDeploy = -1;

    /// The shipped MIX — relative weights per shape, indexed by the Deploy* constants. Weights
    /// (not a shape list) so a measured round can re-balance the deal without touching the
    /// derivation. All-zero or a bad table degrades to FRONTAL.
    /// SHIPPED 3/3/1/3, measured end-to-end at h0 and h4 (DEVLOG §W4 round S1). ENVELOP is
    /// legal on ~60% of objectives and falls back to FRONTAL elsewhere, so the EFFECTIVE deal
    /// is roughly FRONTAL 42% / PINCER 35% / CROSSFIRE 13% / ENVELOP 11%. CROSSFIRE is the low
    /// weight deliberately: pinned, it was the only shape to move the "which target?" axis
    /// (+0.06) but also the only one that DRAGS Escort (13.40t vs PINCER's 5.65t), because its
    /// NE mass lands on the cols 16-17 extraction corner. `SIGHTLINE_DEPLOYMIX=1,0,0,0`
    /// restores the pre-W4 all-FRONTAL board exactly.
    public static int[] DeployMix = { 3, 3, 1, 3 };

    /// Telemetry: the shape the LAST Build actually used (read by Game.SetupMission for Stats).
    public static int AppliedDeploy = DeployFrontal;

    /// Under an ENVELOP opening, rotate the rim reinforcement waves arrive from (Game.
    /// SpawnReinforcements) so a surrounded hold stays surrounded. Measured as its own round —
    /// OFF by default until it is; SIGHTLINE_RIMWAVES=1 turns it on.
    public static bool EnvelopRimWaves = false;

    /// ENVELOP seats the squad in the MIDDLE of the board, which would trivialise any objective
    /// whose key tile sits at board centre or whose extraction is a far corner. It is therefore
    /// only legal on the objectives that have no placed geography of their own: Eliminate,
    /// Decapitate and Defend (a hold-out with no zone — the surrounded opening the shape exists
    /// for). Evac/Escort/Rescue (evac zone), Hack (centre terminal) and Sabotage (mid-field
    /// sites) all keep a directional opening, so no extraction/hack routing changes at all.
    public static bool EnvelopLegal(List<(int x, int y)> evac, (int x, int y)? terminal,
                                    List<(int x, int y)> sabotage)
        => (evac == null || evac.Count == 0) && !terminal.HasValue
           && (sabotage == null || sabotage.Count == 0);

    /// The deployment shape for this mission. PURE — no RNG draw, no state read beyond the two
    /// arguments and the static mix/pin. `canEnvelop` comes from EnvelopLegal.
    public static int DeployFor(int deckSeed, int missionNum, bool canEnvelop)
    {
        if (ForcedDeploy >= 0)
        {
            int f = ForcedDeploy % DeployShapes;
            return (f == DeployEnvelop && !canEnvelop) ? DeployFrontal : f;
        }
        int total = 0;
        for (int i = 0; i < DeployMix.Length && i < DeployShapes; i++) total += Math.Max(0, DeployMix[i]);
        if (total <= 0) return DeployFrontal;
        // FNV-1a over the two keys, then an avalanche so adjacent missions don't correlate.
        uint h = 2166136261u;
        unchecked
        {
            h = (h ^ (uint)(deckSeed & 0xffff)) * 16777619u;
            h = (h ^ (uint)((deckSeed >> 16) & 0xffff)) * 16777619u;
            h = (h ^ (uint)(missionNum & 0xff)) * 16777619u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
        }
        int r = (int)(h % (uint)total);
        for (int i = 0; i < DeployMix.Length && i < DeployShapes; i++)
        {
            r -= Math.Max(0, DeployMix[i]);
            if (r < 0) return (i == DeployEnvelop && !canEnvelop) ? DeployFrontal : i;
        }
        return DeployFrontal;
    }

    // ENVELOP's player footprint: a tight centre cluster (cols 7-10, rows 3-6) with the same
    // VIP-LAST contract as PlayerSpawns. All tiles distinct, none on the board's exact centre
    // (Rescue re-seats a captive there — and Rescue can never draw ENVELOP anyway).
    static readonly (int x, int y)[] PlayerSpawnsCentre =
        { (8, 3), (7, 5), (9, 6), (7, 4), (10, 3), (10, 6), (8, 5) };

    static (int x, int y)[] SpawnTableFor(int shape, int gw, int gh)
    {
        if (shape != DeployEnvelop) return PlayerSpawns;
        var (ox, oy) = EnvelopOffset(gw, gh);
        if (ox == 0 && oy == 0) return PlayerSpawnsCentre;
        var t = new (int x, int y)[PlayerSpawnsCentre.Length];
        for (int i = 0; i < t.Length; i++) t[i] = (PlayerSpawnsCentre[i].x + ox, PlayerSpawnsCentre[i].y + oy);
        return t;
    }

    // ══ 0d+1 — ENVELOP ON A BIG BOARD ════════════════════════════════════════════════════════
    /// **`SIGHTLINE_ENVELOPCENTRE=0` restores the pre-wave ENVELOP exactly.** A NO-OP on the
    /// shipped 18x11 board BY CONSTRUCTION (both the offset and the depth blend are zero there), so
    /// it is live only under `SIGHTLINE_BIGMAP`.
    ///
    /// ENVELOP is "squad in the CENTRE, pods on every rim" — and both halves were written in
    /// ABSOLUTE 18x11 coordinates. On 36x22 the squad's table (cols 7-10, rows 3-6) put it in the
    /// north-west QUARTER, the west pod 7 tiles off and the east pod 26: not surrounded, just
    /// lopsided. P57 left it exempt from the depth spread for exactly this reason ("toward the
    /// squad is not one axis").
    ///
    /// THE RULE. (1) The squad's table is translated by half the extra width and height, so it
    /// sits where it sits on 18x11 relative to the board's CENTRE. (2) Each rim pod's lead takes a
    /// point between the BIG board's rim anchor and the REFERENCE board's anchor carried to that
    /// same centre, by P57's `DepthFrac` for its pod id: pod 0 (the boss's slot) stays on the far
    /// rim, pod 1 opens at exactly the 18x11 standoff, the rest fill the depth in between — a ring
    /// with DEPTH on every bearing, the same bodies, and no pod ever nearer than the shipped
    /// board puts it. Zero RNG draws.
    public static bool EnvelopCentre = true;

    /// Columns/rows the ENVELOP squad and its reference ring are carried by. (0,0) at 18x11.
    public static (int ox, int oy) EnvelopOffset(int gw, int gh)
        => EnvelopCentre ? (Math.Max(0, (gw - RefW) / 2), Math.Max(0, (gh - RefH) / 2)) : (0, 0);

    /// The ENVELOP pod lead on a board of any size (see EnvelopCentre). Identity at 18x11.
    static (int x, int y) EnvelopAnchor(int podId, int row, int gw, int gh)
    {
        var rim = PodAnchor(DeployEnvelop, podId, row, gw, gh);
        var (ox, oy) = EnvelopOffset(gw, gh);
        if (ox == 0 && oy == 0) return rim;
        var r = PodAnchor(DeployEnvelop, podId, row, RefW, RefH);
        double f = DepthFrac[podId % DepthFrac.Length];
        int x = (int)Math.Round(rim.x + (r.x + ox - rim.x) * f);
        int y = (int)Math.Round(rim.y + (r.y + oy - rim.y) * f);
        return (Math.Clamp(x, 0, gw - 1), Math.Clamp(y, 0, gh - 1));
    }

    /// Where pod `podId`'s LEAD body deploys. `row` is the shuffled row the FRONTAL path would
    /// have used (kept as the jitter source so the shared RNG stream is untouched — reading
    /// rows[] is not a draw). Returns a tile; the caller's collision-relocate loop is unchanged
    /// and is still the only conditional draw source.
    static (int x, int y) PodAnchor(int shape, int podId, int row, int gw, int gh)
    {
        switch (shape)
        {
            case DeployPincer:
                // Front + both flanks. The two flank pairs sit in the rim lanes the mid-field
                // screen deliberately leaves open (cols 12-13), at a standoff comparable to the
                // frontal column so the squad is not shot off its own spawn.
                switch (podId % 6)
                {
                    case 0: return (gw - 2, Math.Clamp(row, 3, gh - 4));
                    case 1: return (gw - 5, 0);
                    case 2: return (gw - 5, gh - 1);
                    case 3: return (gw - 1, row);
                    case 4: return (gw - 6, 1);
                    default: return (gw - 6, gh - 2);
                }
            case DeployCrossfire:
                // Two dense masses on the NE and SE bearings with the middle rows left EMPTY, so
                // a squad in the centre lane holds both in one arc instead of meeting a wall of
                // evenly-spread bodies one pod at a time.
                {
                    int step = podId / 2;
                    int x = Math.Max(gw - 5, gw - 2 - step);
                    return (podId % 2 == 0) ? (x, Math.Min(gh - 1, step)) : (x, Math.Max(0, gh - 1 - step));
                }
            case DeployEnvelop:
                // The surrounded opening: pods on all four rims around a centre-deployed squad.
                switch (podId % 6)
                {
                    case 0: return (gw - 2, gh / 2);
                    case 1: return (1, gh / 2);
                    case 2: return (gw / 2 + 2, 0);
                    case 3: return (gw / 2 - 2, gh - 1);
                    case 4: return (gw - 3, 1);
                    default: return (2, gh - 2);
                }
            default:
                return (gw - 1 - EnemyPodColOffset[podId % EnemyPodColOffset.Length], row);
        }
    }

    // ══ P57 — THE DEPTH SPREAD ═══════════════════════════════════════════════════════════════
    /// **`SIGHTLINE_DEPTHSPREAD=0` restores the far-edge deployment exactly.** A NO-OP on the
    /// shipped 18x11 board BY CONSTRUCTION, so it is live only under `SIGHTLINE_BIGMAP`.
    ///
    /// Every `PodAnchor` above is written relative to the FAR EDGE (`gw - 1 - offset`, `gw - 2`,
    /// `gw - 5`). The deployment shapes vary the BEARING a pod comes from, but on any board they all
    /// land in the last ~6 columns — so `SIGHTLINE_MAPSHAPEPROBE` measured the whole force massed
    /// 3.2 turns from the squad on 36x22 and 4.2 on 48x30, with nothing in between. That is not a
    /// shortage of bodies (P56 gated the headcount as board-neutral and it is correct); it is WHERE
    /// they stand. Crossing the board was a walk through nothing to one clump at the end, which is
    /// the "throw grenades at the blob and move on" mission the owner named, with a walk added.
    ///
    /// **THE RULE.** On a board wider than the reference, each pod is pulled toward the squad by a
    /// fixed FRACTION of the extra width (`gw - RefW`). Pod 0 — the slot the named mid-boss and the
    /// finale boss take — stays at the far edge; pod 1 comes to the reference line; the rest fill
    /// in between. The SAME bodies, spread through the board's depth, so crossing it is a series of
    /// engagements and the squad spends its grenades, ammo and HP along the way instead of on one
    /// clump.
    ///
    /// **THE NEAREST A POD CAN COME IS THE DISTANCE IT ALREADY COMES ON THE SHIPPED BOARD**: the
    /// shift is at most `gw - RefW`, which places a pod exactly where its anchor falls at 18x11. So
    /// the turn-1 ambush risk (`docs/DESIGN.md` §5) is today's, never worse.
    ///
    /// **ZERO RNG DRAWS, AND ZERO SHIFT AT THE REFERENCE WIDTH** — `gw - RefW` is 0 on the shipped
    /// board, the fractions are a fixed table indexed by pod id, and the collision relocate keeps
    /// its two draws with only the column it lands in shifted. `DEPTHSPREADTEST` leg (A) asserts
    /// the 18x11 board is byte-identical, every layer and every unit, across all four shapes.
    ///
    /// ENVELOP is EXEMPT: its squad deploys in the CENTRE with pods on every rim, so "toward the
    /// squad" is not one axis. It gets its own rule — see `EnvelopCentre` (0d+1).
    public static bool DepthSpread = true;

    /// P57 telemetry (harness-only; two int stores, read by no live path, like `LastForceCount`):
    /// how many bodies the collision relocate moved during the LAST Build, and how many of those
    /// belonged to a pod the spread had shifted. `DEPTHSPREADTEST` leg (E) needs the second one to
    /// prove its relocate check is not VACUOUS — the first version of that gate passed with the
    /// relocate regression in place, because at its fixed seed no shifted pod ever collided.
    public static int LastRelocations, LastShiftedRelocations;

    /// Pod `podId`'s share of the extra depth. A van der Corput-style order so any number of pods
    /// covers the depth evenly: far edge, reference line, middle, then the quarters.
    static readonly double[] DepthFrac = { 0.0, 1.0, 0.5, 0.25, 0.75, 0.125, 0.625, 0.375, 0.875 };

    /// Columns pod `podId` is pulled toward the squad. 0 at the reference width, 0 under ENVELOP.
    public static int DepthShift(int shape, int podId, int gw)
    {
        if (!DepthSpread || shape == DeployEnvelop) return 0;
        int extra = Math.Max(0, gw - RefW);
        if (extra == 0) return 0;
        return (int)Math.Round(extra * DepthFrac[podId % DepthFrac.Length]);
    }

    /// Which way pod members stack off their lead. FRONTAL/PINCER/CROSSFIRE keep the historical
    /// downward row stack; ENVELOP's rim pods stack ALONG their own edge so a pod on the north
    /// rim doesn't march into the squad's lap. Returns (dx, dy) for member 1; member 2 doubles it.
    static (int dx, int dy) PodStack(int shape, int podId)
    {
        if (shape != DeployEnvelop) return (0, 1);
        switch (podId % 6)
        {
            case 0: case 1: case 4: return (0, 1);   // E / W rim pods stack DOWN their column
            case 5: return (0, -1);                  // the SW pod is already low: stack UP
            default: return (1, 0);                  // N / S rim pods stack ALONG their row
        }
    }

    /// The unit step from `u` toward the NEAREST body in `foes`, on the dominant axis only (so
    /// the result is always one of the four cardinals). Used to put protective cover on the side
    /// a body is actually threatened from, whatever bearing this mission's deployment used.
    /// Identity-preserving for a FRONTAL opening: the squad sits in cols 0-3 and the force in
    /// cols 12-17, so |dx| >= 11 always dominates |dy| <= 10 and the step is the historical
    /// +1 (soldiers) / −1 (hostiles) column.
    static (int dx, int dy) FacingStep(Unit u, List<Unit> foes)
    {
        if (u == null || foes == null || foes.Count == 0) return (1, 0);
        Unit near = null; int bestD = int.MaxValue;
        foreach (var f in foes)
        {
            int d = Util.ChebyDist(u.X, u.Y, f.X, f.Y);
            if (d < bestD) { bestD = d; near = f; }
        }
        if (near == null) return (1, 0);
        int dx = near.X - u.X, dy = near.Y - u.Y;
        if (Math.Abs(dx) >= Math.Abs(dy)) return (dx >= 0 ? 1 : -1, 0);
        return (0, dy >= 0 ? 1 : -1);
    }

    // test hook (SIGHTLINE_MAP): force a specific authored layout index; -1 = normal roll
    public static int ForcedLayout = -1;
    /// P20 THE STALE GROUND — clear the biome GROUND layer at the top of Build (the default and
    /// the correct behaviour: see the note in Build). `SIGHTLINE_STALEGROUND=1` restores the
    /// pre-fix read, in which Build's floor/cost/connectivity queries saw the PREVIOUS mission's
    /// ground. It exists for two reasons: the house rule that a change which moves the board must
    /// be switchable so it can be attributed and priced, and because MODETEST leg (14) flips it to
    /// prove its own detector can fail. NEVER a shipping configuration — with it on, the SEEDED
    /// DAILY's headline contract ("the same day deals the same board to everyone") is false.
    /// ══ P28 — BUILDINGS. The edge layer's first producer. ═════════════════════════════════
    /// Off (`SIGHTLINE_BUILDINGS=0`) this spends ZERO `Util.Rng` draws and stamps nothing, so the
    /// board is the pre-P28 board exactly — the restore flag has to be free, not merely faithful,
    /// or it cannot be used as a measurement arm.
    ///
    /// IT VALIDATES RATHER THAN TRUSTS, on `Terrain.StampRift`'s precedent and for the same
    /// reason: a wall is the first thing on this board that can make a mission UNWINNABLE, and it
    /// can do so silently. Every candidate is stamped, re-flooded through `Grid.CostMap` (the one
    /// movement model both teams read, so there is no second connectivity model to drift), and
    /// REVERTED WHOLE unless every tile reachable before is still reachable. A building can
    /// therefore never seal a pocket, never strand a unit and never orphan an objective — not
    /// because the placement rules are clever, but because the failures are undone.
    ///
    /// ═══ P28's BLOCKER IS GONE (P40). THE DEFAULT IS STILL OFF, FOR A DIFFERENT REASON. ══════
    /// P28 measured this and recorded the cause exactly: `SIGHTLINE_AICOVTEST` guards C1's
    /// dead-row class — no AI branch may fall under 0.10% of acts — and turning buildings on drove
    /// the SAPPER branch under it. Not a tuning problem: `Ai`'s sap branch destroyed a COVER TILE,
    /// `Grid.CoverHp` is a per-TILE array, so a soldier sheltering behind a building WALL could not
    /// be sapped at all. Cover had moved from tiles to edges and the AI's cover-destruction branch
    /// could not follow it. Buildings did not make the opponent dumber by accident; they removed
    /// one of its options.
    ///
    /// P40 SHIPPED THE NAMED FIX — destructible edges: per-edge HP, High -> Low -> None
    /// (`Grid.DamageEdge`), and `SapTile` gaining an edge form (`EnemyPlan.SapEdge`). Re-measured
    /// on the same instrument (AICOVTEST=6, ~9,800 acts, deterministic):
    ///     buildings OFF                      sap = 19 (0.21%)   PASS
    ///     buildings ON  + destructible edges sap = 12 (0.12%)   PASS
    ///     buildings ON  + DESTRUCTEDGE=0     sap =  9 (0.09%)   FAIL   (the pre-P40 tree)
    /// The third row reproduces P28's recorded failure exactly, so the fix is red-before,
    /// green-after on a gate that already existed rather than one written to suit it.
    ///
    /// **SO WHY IS THE DEFAULT STILL OFF?** Because the blocker was never the only question. The
    /// same census says buildings change the FIGHT, not just the board: hunker 16.05% -> 26.76%,
    /// terminal-hunker 0.11% -> 2.81%, idle 28.60% -> 21.72%, shoot 40.84% -> 34.26%. That is a
    /// different game, and this project does not ship a different game unpriced — a default flip
    /// is a LEVEL lever on every mission and wants a measured round against the ladder of record.
    /// The margin is also thin (0.12% against a 0.10% floor), which is a second reason to price it
    /// deliberately rather than to flip it because a gate went green.
    ///
    /// `SIGHTLINE_BUILDINGS=1` turns them on. Everything below this line is live either way.
    public static bool Buildings = false;
    /// Why candidates were refused, for BUILDINGTEST to report. `Sealed` is the interesting one:
    /// it counts would-be buildings the reachability validator actually caught, so a zero there
    /// would mean the validator is decoration rather than a guard.
    public static int BuildDbgDirty, BuildDbgSealed, BuildDbgOob, BuildDbgOverlap;

    public static bool ClearGroundOnBuild = true;

    /// P21 BUILD OWNS THE BOARD — clear the two HAZARD layers (Fire, Barrel) at the top of Build,
    /// beside the ground clear, so `Mission.Build` owns all EIGHT of `Grid`'s per-tile arrays
    /// rather than six. `SIGHTLINE_STALEHAZARDS=1` restores the pre-fix seam, in which Build
    /// neither cleared nor was guaranteed clean hazards and its floor/connectivity queries could
    /// read the PREVIOUS mission's barrels.
    ///
    /// UNLIKE `ClearGroundOnBuild`, THIS FLAG IS LIVE-PATH INERT AND THAT IS THE POINT.
    /// `Game.SetupMission` — the only production caller of Build — calls `Grid.ClearHazards()`
    /// unconditionally 28 lines before the Build call, and that call is DELIBERATELY KEPT (see the
    /// comment there). So on every shipped path the arrays are already zero when Build runs and
    /// this clear is a proven no-op; the defect L6 measured was LATENT, not live. What the flag
    /// buys is the house rule (a change to the board seam must be switchable) and a detector that
    /// can be shown to fail: MODETEST leg (14a-2) flips it to prove its own dirt still bites.
    public static bool ClearHazardsOnBuild = true;

    // ════════════════════════ P26 "THE ARENA OWNS THE FIGHT" ════════════════════════
    // Before P26 every objective site was a literal expression in Game.SetupMission — the HACK
    // terminal was ALWAYS (Grid.W/2+1, Grid.H/2) = (10,5), evac ALWAYS the 2x4 block at cols
    // 16-17 — and Build force-cleared a 3x3 ring around each one into `occupied`, which
    // TryApplyLayout then SKIPS. So the authored arena was erased in a 3x3 at the exact tile the
    // mission converges on, on all 35 boards, at every heat, in every mode. Measured over the
    // 6,400-campaign P24 archive: corr(arena open-floor %, win %) = -0.15, and -0.153 controlling
    // for mission number. Thirty-five hand-authored boards moved nothing, because the geometry was
    // deleted where it would have mattered.
    //
    // SIGHTLINE_ARENASITES=0 restores the PRE-WAVE ORDER as well as the literal sites: no
    // PlanBoard call, no roll before SpawnEnemies, the arena gate back inside Build, and Game's
    // four literal blocks live again. That is a full restore by CONSTRUCTION (the old gate block is
    // kept verbatim in an else branch), not by argument.
    //
    // ⚠ THIS MOVES THE CRN STREAM. The draw COUNT per build is unchanged (still exactly one
    // Util.Roll(80) gate, skipped under ForcedLayout; PickLayout/DeckPick draw zero), so the FUL-9
    // draw-order contract's letter holds. But the draw MOVES ahead of SpawnEnemies' row shuffle,
    // and three loops in Build consume a draw count that depends on board CONTENT (Sprinkle's
    // reject-retry, PlaceBarrels' identical shape, SpawnEnemies' collision-relocate). The moment a
    // site moves, occupancy moves and the streams diverge irrecoverably. Every archived CRN world
    // is invalidated — the W1 class of break. Do NOT rescale a P24 number onto this tree;
    // re-measure, with SIGHTLINE_ARENASITES=0 as the bridge arm.
    public static bool ArenaSites = true;

    // SIGHTLINE_ARENAANCHORS=0 ignores the 'A' glyph while keeping arena sites. Two dials, not
    // one (P23's precedent): the SITE lever moves objective geometry, the ANCHOR lever moves the
    // deployment geometry W4 measured. A round that wants to price one without the other must be
    // able to.
    public static bool ArenaAnchors = true;

    /// P50 — the order an anchored pod fills outward from its tile. Fixed and draw-free on purpose:
    /// a garrison must be reproducible slot for slot or it cannot be measured against anything.
    /// Eight-neighbours first (a pod that holds a room should be IN the room), then the next ring.
    static readonly (int x, int y)[] GarrisonRing =
    {
        (0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1),
        (0, -2), (2, 0), (0, 2), (-2, 0), (2, -2), (2, 2), (-2, 2), (-2, -2),
    };

    // Telemetry mirroring AppliedLayout: which site categories the LAST build actually took from
    // the arena. Read by SIGHTLINE_ARENASITETEST; never by gameplay.
    public static SitePlan LastPlan;



    // W2 arena telemetry: the authored layout index the LAST Build actually applied, or -1 for
    // the procedural fallback. Recorded only AFTER TryApplyLayout's connectivity guard accepted
    // the template (PickLayout merely PROPOSES one — a rejected proposal falls back procedural,
    // and logging the proposal would misattribute those missions). Read by Game.SetupMission
    // when it stamps Stats.BeginMission. Static like ForcedLayout (one Build at a time).
    public static int AppliedLayout = -1;

    // FUL-9 THE DECK: the run's MapSeed, published by Game.SetupMission before every Build so
    // the per-run no-repeat arena deck derives PURELY from it (no persisted list, no Util.Rng
    // draws — see PickLayout). 0 = a bare harness Build with no run context (still deterministic).
    public static int DeckSeed = 0;

    // Soft biome->layout affinity: each biome index (matching Biome.All order —
    // STEEL=0 ARID=1 TUNDRA=2 VERDANT=3 ASH=4 VOID=5 NEON=6 MAGMA=7) hints at a preferred
    // arena index. FUL-9: the hint is now a REDUCED weight WITHIN the no-repeat deck (25%
    // pull-forward of the DISPLAYED biome's arena — was a 50% mission-number-keyed pick,
    // which piled 57% of authored missions onto these 8 and left 6/35 arenas unseen in 251
    // missions). -1 means no preference. This stays SOFT: ForcedLayout overrides it
    // completely, and the connectivity guard can still fall back to procedural if a hinted
    // layout fails (though the arenas are designed to pass). Keep this array length-aligned
    // with Biome.All (one entry per biome); PickLayout deals from the plain deck past the end.
    static readonly int[] BiomeLayoutHint =
    {
        34,  // STEEL   → DONJON (walled tier-2 keep taken by a single ramp)
        31,  // ARID    → ENTRENCHED (asymmetric dug-in trench network)
        32,  // TUNDRA  → CAUSEWAY (a frozen ford: an elevated land-bridge crossing)
        10,  // VERDANT → THICKET (dense organic cover clusters)
        33,  // ASH     → REDANS (a ruined earthworks line: diagonal sawtooth gauntlet)
        30,  // VOID    → COLONNADE (cavernous long-sightline pillar gallery)
        15,  // NEON    → GRID (orthogonal server-room rack lattice)
        28,  // MAGMA   → CRUCIBLE (barrel-rigged refinery throat chokepoint)
    };

    /// The four starting soldiers for a fresh run.
    public static List<Unit> NewRunSquad()
    {
        var squad = new List<Unit>();
        squad.Add(MakeSoldier("VEGA",   "ASSAULT",      WeaponKind.Rifle,   8, 70, 7));
        squad.Add(MakeSoldier("KRESS",  "RANGER",       WeaponKind.Shotgun, 7, 70, 8));
        squad.Add(MakeSoldier("NOX",    "SHARPSHOOTER", WeaponKind.Sniper,  6, 72, 6));
        squad.Add(MakeSoldier("BISHOP", "GUNNER",       WeaponKind.Lmg,    10, 62, 6));
        return squad;
    }

    /// Lay out a mission: regenerate terrain, place the (persistent) players,
    /// and spawn a hostile force scaled by missionNum.
    public static void Build(Grid grid, List<Unit> players, List<Unit> enemies, int missionNum,
                             List<(int x, int y)> evac = null, (int x, int y)? terminal = null,
                             int enemyDelta = 0, int statDelta = 0, List<(int x, int y)> sabotage = null,
                             int dmgDelta = 0, bool defend = false, int defendKeep = 0,
                             int rosterTier = -1, bool midBossSlot = false, bool eliteNode = false,
                             bool finalApproach = false, int heatStat = 0,
                             SitePlan plan = default)
    {
        enemies.Clear();
        grid.ClearSmoke();
        // P20 THE STALE GROUND — the ground layer belongs to the PREVIOUS mission here, and this
        // build must not read it. Tiles, Height and Smoke are wiped above/below; Ground was not,
        // and Build asks for it three ways before it is restamped: Grid.IsFloor (a RIFT tile is
        // not floor), Grid.CostMap (rift blocks, ice/sand/vent reprice a step) and every
        // connectivity flood built on them — TryApplyLayout's accept/reject guard, SpawnEnemies'
        // pod scatter, PlaceBarrels' candidate filter and EnsureConnectivity's carve. THIS
        // mission's layer cannot exist yet: Game.StampBiomeGround runs AFTER Build because its
        // `reserved` set is derived from the board Build produces. So the correct ground during a
        // build is NO ground, and leaving the last one in place made the arena a function of the
        // board before it. MODETEST leg (14) is the gate.
        if (ClearGroundOnBuild) grid.ClearGround();
        // P28 — the EDGE layer joins P20's ground and P21's hazards: Build owns every per-tile
        // layer without exception. A wall left over from the previous mission would move this
        // board through exactly the predicates P20's stale ground did.
        grid.ClearEdges();
        // P21 BUILD OWNS THE BOARD — and the same argument for the other two layers L6 found.
        // `Grid.IsFloor` is `InBounds && Tiles==Floor && !Barrel[x,y] && !rift`: a BARREL sits in
        // the same predicate the rift was added to, so every connectivity flood inside Build reads
        // it — TryApplyLayout's accept/reject guard, EnsureConnectivity's carve ("a barrel keeps a
        // tile non-walkable"), PlaceBarrels' own candidate filter — and Build then stamps its own
        // barrels. Fire is not read here but is equally not Build's caller's business to leave
        // behind. Measured by MODETEST (14a-2): dirtying Barrel alone moved Tiles, Height, CoverHp,
        // CoverSeed and Barrel. That was LATENT, never live — Game.SetupMission has always cleared
        // both immediately before calling this — but the invariant belonged HERE, in Build, exactly
        // as the ground layer's did. Build now owns all eight per-tile arrays.
        if (ClearHazardsOnBuild) grid.ClearHazards();
        for (int x = 0; x < grid.W; x++)
            for (int y = 0; y < grid.H; y++)
            {
                grid.Tiles[x, y] = TileType.Floor;
                grid.Height[x, y] = 0;
            }

        // W4 THE SECOND AXIS — pick this mission's deployment SHAPE first: it decides both the
        // squad footprint (below) and every pod's bearing (SpawnEnemies). Pure derivation from
        // (DeckSeed, missionNum): zero RNG draws, so the shared stream is untouched.
        int shape = DeployFor(DeckSeed, missionNum, EnvelopLegal(evac, terminal, sabotage));
        AppliedDeploy = shape;
        // P26: the ARENA owns where the squad STANDS when it declared a 'P' table; the deployment
        // SHAPE still owns where the force comes from (AppliedDeploy stays published, so pod
        // bearings, SpawnReinforcements' rim wave and the HORDETEST/BALANCE telemetry are unmoved).
        var spawnTable = plan.Valid && plan.ArenaSpawns ? plan.Spawns : SpawnTableFor(shape, grid.W, grid.H);

        // place players at their deployment footprint, refresh per-mission state (HP persists)
        for (int i = 0; i < players.Count && i < spawnTable.Length; i++)
        {
            var u = players[i];
            // the VIP/captive always takes the dedicated 5th slot, even when the squad is
            // short-handed (benched soldier) and the VIP would otherwise land on a soldier's
            // lower index and spawn far from the squad/extraction (review Major). Rescue
            // re-seats its captive at centre after Build, so this only matters for Escort.
            var sp = u.IsVip ? spawnTable[spawnTable.Length - 1] : spawnTable[i];
            u.X = sp.x;
            u.Y = sp.y;
            u.Ammo = u.Weapon.Clip;
            u.Grenades = 1 + u.BonusGrenades + (u.HasPerk(Perk.Bandolier) ? 1 : 0);  // refill (+cache +Bandolier)
            u.AbilityCd = 0;                                       // signature ability ready (off cooldown)
            // utility item: 1 charge/mission — 2 under the FIELD STORES boon (W10; read via the
            // per-mission Combat.RunBoons static, published by Game.SetupMission BEFORE Build runs.
            // This loop seats PLAYERS only, so enemy items are never doubled).
            u.ItemCharge = u.Item != ItemKind.None
                ? (Combat.RunBoons.Contains(Boon.FieldStores) ? 2 : 1) : 0;
            // FUL-1 PROC: FIELD STORES actually granted a double charge (per soldier-item, the
            // grant IS the effect — the boon has no in-mission fire site of its own)
            if (u.ItemCharge == 2) Stats.RecordProc("FST");
            u.Suppress = 0;
            u.OnOverwatch = false;
            u.Hunkered = false;
            u.Recoil = System.Numerics.Vector2.Zero;
            u.Flash = 0;
        }

        var evacSet = new HashSet<(int, int)>(evac ?? new List<(int, int)>());
        // SABOTAGE is the weakest objective (~65% vs ~90% peers, the m5 gate): unlike Hack/Evac
        // which let the squad mass at ONE zone, its 3 charge sites are spread across the mid-field,
        // so the squad must SPLIT and cross open ground while every PLANT "goes loud" (rouses pods +
        // breaks stealth). That triple tax compounds with the full force, so we ease the ENCOUNTER:
        // a lighter hostile force (the loud-tempo IS the difficulty) + covered fighting positions
        // at each site (below) so the split squad can hold.
        bool sabotageObj = sabotage != null && sabotage.Count > 0;
        SpawnEnemies(grid, enemies, missionNum, evacSet, enemyDelta, statDelta, sabotageObj, dmgDelta, defend, defendKeep, shape,
                     rosterTier, midBossSlot, eliteNode, finalApproach, heatStat,
                     ArenaAnchors && plan.Anchors != null && plan.Anchors.Length > 0 ? plan.Anchors : null);

        var occupied = new HashSet<(int, int)>();
        foreach (var u in players) occupied.Add((u.X, u.Y));
        foreach (var u in enemies) occupied.Add((u.X, u.Y));
        foreach (var t in evacSet) occupied.Add(t);   // keep the extraction zone clear of cover
        // ═══ P26: THE RING IS A PROPERTY OF A *LITERAL* SITE, NOT OF A SITE ═══
        // A literal site lands on terrain nobody authored around it, so Build punched a bare 3x3
        // to guarantee it was approachable — and TryApplyLayout skips `occupied`, so that 3x3 was
        // erased from the arena. A template that placed its own 'T'/'X' has authored the approach
        // it wants, and PlanBoard already proved every site reachable with >= 1 walkable neighbour.
        // So an ARENA-OWNED site reserves its own tile only (it must stay standable — a barrel or a
        // cover block on the terminal is an unwinnable mission) and keeps its authored surroundings.
        // This one distinction is the whole milestone.
        bool ringTerminal = !(plan.Valid && plan.ArenaTerminal);
        bool ringSabotage = !(plan.Valid && plan.ArenaSabotage);
        if (terminal.HasValue)
        {
            if (ringTerminal)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        occupied.Add((terminal.Value.x + dx, terminal.Value.y + dy));
            else occupied.Add((terminal.Value.x, terminal.Value.y));
        }
        if (sabotage != null)
            foreach (var s in sabotage)
            {
                if (ringSabotage)
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            occupied.Add((s.x + dx, s.y + dy));
                else occupied.Add((s.x, s.y));
            }

        // Either lay down a hand-authored arena (with a connectivity guard) or fall
        // back to the procedural generator. Both keep reserved tiles open.
        // W2 telemetry: AppliedLayout records the template index only once TryApplyLayout has
        // ACCEPTED it (the connectivity guard can reject a proposal); -1 = procedural fallback.
        // FUL-9 DRAW-ORDER CONTRACT (load-bearing for CRN pairing): the authored gate takes
        // EXACTLY ONE Util.Roll and the arena pick takes ZERO — PickLayout derives purely from
        // (DeckSeed, missionNum), so the shared stream is identical whichever arena is dealt.
        // Roll 55->80 is the whole procedural lever: FUL-1 measured the reject lane EMPTY
        // (authored 52.6% / reject 0.0% / proc-roll 47.4% at n~190), so the lost roll was the
        // only road to procedural; 80 targets the 20-25% procedural share.
        bool authored = false;
        bool attempted = false;   // FUL-1 funnel: a template reached the connectivity guard
        AppliedLayout = -1;
        // P26: when PlanBoard already chose and validated a template, the gate is SETTLED — it must
        // not roll again (that would double-spend the draw) and must not re-derive the pick. The
        // else branch below is the PRE-P26 BLOCK VERBATIM, which is what makes SIGHTLINE_ARENASITES=0
        // a restore by construction rather than by argument.
        NoteArenaFit();          // P29: say ONCE, loudly, if this board cannot use the arenas
        if (plan.GateSpent && ArenasFitBoard)
        {
            // PlanBoard already spent the gate roll and made the pick. Rolling again here would
            // double-spend the shared stream. A spent gate with Layout < 0 is the procedural
            // outcome; a spent gate with Valid false but a Layout is a template whose own sites
            // failed validation -- it still gets stamped, just with the literal sites and the ring.
            if (plan.Layout >= 0)
            {
                attempted = true;
                authored = TryApplyLayout(grid, occupied, players, enemies, evacSet, terminal,
                                          Maps.ArenaAt(plan.Layout), sabotage, relaxEnemies: plan.Valid);
                if (authored) AppliedLayout = plan.Layout;
            }
        }
        else if (ArenasFitBoard && ForcedLayout >= 0 && ForcedLayout < Maps.Layouts.Length)
        {
            attempted = true;
            authored = TryApplyLayout(grid, occupied, players, enemies, evacSet, terminal, Maps.ArenaAt(ForcedLayout), sabotage);
            if (authored) AppliedLayout = ForcedLayout;
        }
        else if (ArenasFitBoard && Util.Roll(80))
        {
            attempted = true;
            int pick = PickLayout(missionNum);
            authored = TryApplyLayout(grid, occupied, players, enemies, evacSet, terminal, Maps.ArenaAt(pick), sabotage);
            if (authored) AppliedLayout = pick;
        }
        if (!authored)
            BuildProcedural(grid, occupied, evacSet, missionNum);
        LastPlan = plan;   // P26 telemetry: which categories this build took from the arena
        // FUL-1 ARENA FUNNEL (telemetry only, no-op unless Stats.Enabled): the three exits sum
        // to 100% of builds — a guard REJECT was previously indistinguishable from a lost roll.
        Stats.RecordArenaFunnel(authored ? Stats.ArenaAuthored
                                : attempted ? Stats.ArenaReject : Stats.ArenaProcRoll);

        // Protective cover beside each soldier and hostile (both layout paths), on the tile facing
        // the OTHER side. W4: with the force no longer always due east, "facing" is derived from
        // the opposing centroid on its dominant axis — which reproduces the historical +1 / −1
        // column exactly for a FRONTAL opening (the centroids are ~13 columns apart and at most
        // ~5 rows apart, so the dominant axis is always x there).
        {
            foreach (var u in players)
            { var f = FacingStep(u, enemies); TryCover(grid, occupied, u.X + f.dx, u.Y + f.dy, TileType.LowCover); }
            foreach (var u in enemies)
            { var f = FacingStep(u, players); TryCover(grid, occupied, u.X + f.dx, u.Y + f.dy, TileType.HighCover); }
        }

        // SABOTAGE: drop covered fighting positions just OUTSIDE each charge site's reserved ring,
        // on the squad-facing (west) side, so a split planter isn't planting in the open. Two low
        // blocks per site (NW/SW of the site) — they don't seal the ring (it stays open floor), and
        // EnsureConnectivity below guarantees reachability if they ever pinch a lane.
        if (sabotage != null)
            // P26: these two blocks are the procedural apology for a site with no authored cover
            // near it, and they encode a WEST-facing assumption a template need not share. A
            // template that placed its own 'X' authored the fighting positions it wants.
            if (!(plan.Valid && plan.ArenaSabotage))
                foreach (var s in sabotage)
                {
                    TryCover(grid, occupied, s.x - 2, s.y - 1, TileType.LowCover);
                    TryCover(grid, occupied, s.x - 2, s.y + 1, TileType.LowCover);
                }

        // Environmental hazards: scatter a few explosive barrels on open floor (both the
        // procedural AND authored-layout paths), biased toward the contested mid-field /
        // enemy-half so they're worth shooting (a barrel where a pod scatters is gold). A
        // barrel tile is non-floor (Grid.IsFloor false), so PlaceBarrels' own flood check
        // removes any barrel that would wall an objective/spawn off; the EnsureConnectivity
        // net below is the final safeguard for both cover and barrels.
        PlaceBarrels(grid, occupied, players, enemies, evacSet, terminal, sabotage, missionNum);

        // 4.2 safety net: the denser mid-field cover (+ sprinkles + protective cover + barrels)
        // must never wall a hostile or objective off from the squad — carve a lane if it did.
        EnsureConnectivity(grid, players, enemies, evacSet, terminal, sabotage);


        grid.ResetCoverHp();   // charge every cover tile to full now the terrain is final (3.6)

        foreach (var u in players) u.SyncPos();
        foreach (var u in enemies) u.SyncPos();
    }


    /// Lay 1-2 rectangular buildings as EDGE walls with doors. Adds no tiles: the interior stays
    /// walkable floor, which is the whole point of an edge wall and the reason a building here is
    /// a place to fight over rather than a lump of cover to walk around.
    /// CALLED FROM `Game.SetupMission`, AFTER `StampBiomeGround`, NOT FROM `Build`.
    ///
    /// That placement is a measured correction, not a preference. Build runs BEFORE the biome
    /// ground layer exists, so a building stamped there cannot see the RIFT — and the two compete
    /// for the same resource. `Terrain.StampRift` guarantees a minimum number of chasm tiles by
    /// re-walking until it gets them, rejecting every candidate that would cut the board; walls
    /// eat exactly that connectivity headroom, so with buildings on, VOID boards fell under
    /// `Terrain.RiftFloor` and SIGHTLINE_BIOMETEST went red (riftFloorMissed=7). The rift's
    /// guarantee is older, load-bearing and already has a test; buildings yield to it.
    /// Running here also means the reachability baseline is the TRUE final board.
    public static void StampBuildings(Grid g, List<Unit> players, List<Unit> enemies)
    {
        if (!Buildings || !Edges.Enabled) return;      // MUST spend zero draws when off
        if (players.Count == 0) return;

        // The baseline every candidate is judged against. Taken ONCE: a building that passes has
        // changed nothing about reachability, so the next candidate may reuse it.
        var before = g.CostMap(players[0].X, players[0].Y, null, out _, 9999);
        int reachBefore = 0;
        for (int x = 0; x < g.W; x++) for (int y = 0; y < g.H; y++) if (before[x, y] >= 0) reachBefore++;

        // P40 — scale with the board, exactly as P37 scaled the cover archetypes and for the same
        // reason: 1-2 buildings is an 18x11 number, and on 40x28 it is one or two huts in 1,120
        // tiles. The cell grid is the same one the archetypes are tiled on, so a building belongs
        // to a ROOM rather than being sprinkled over an expanse. Exactly 1-2 at 18x11 (one cell),
        // so the shipped board is untouched.
        CellGrid(g, out int bcx, out int bcy, out _, out _);
        int want = Util.RandInt(1, 2) * bcx * bcy;
        var undo = new List<(bool vert, int x, int y, EdgeKind was)>();
        // Footprints already taken. Two buildings that overlap produce a double-walled shape
        // nobody authored and no player can read as a building — and the reachability validator
        // happily passes it, because a nonsense shape with doors is still connected. Geometry
        // this layer cannot check for itself has to be refused up front.
        var taken = new List<(int x0, int y0, int x1, int y1)>();

        for (int attempt = 0, made = 0; attempt < 14 * want && made < want; attempt++)
        {
            int bw = Util.RandInt(3, 5), bh = Util.RandInt(3, 4);
            int x0 = Util.RandInt(1, Math.Max(1, g.W - bw - 2));
            int y0 = Util.RandInt(1, Math.Max(1, g.H - bh - 2));
            int x1 = x0 + bw, y1 = y0 + bh;                     // exclusive upper bounds
            if (x1 >= g.W || y1 >= g.H) { BuildDbgOob++; continue; }

            bool clash = false;                       // keep a clear tile of air between buildings
            foreach (var t in taken)
                if (x0 <= t.x1 + 1 && x1 + 1 >= t.x0 && y0 <= t.y1 + 1 && y1 + 1 >= t.y0) { clash = true; break; }
            if (clash) { BuildDbgOverlap++; continue; }

            // The interior needs to be a ROOM, not solid rock — but it does not need to be
            // pristine. A crate or a plateau inside a building is furniture, and a rift inside one
            // is a hole in the floor; neither is a defect, and the reachability validator below
            // covers the only thing that actually matters. Measured: demanding a pristine
            // rectangle rejected 330 candidates against the validator's 1, so the "clean footprint"
            // rule was doing all the rejecting and none of the protecting.
            int floorTiles = 0, interior = (x1 - x0) * (y1 - y0);
            for (int x = x0; x < x1; x++)
                for (int y = y0; y < y1; y++) if (g.IsFloor(x, y)) floorTiles++;
            if (floorTiles * 2 < interior) { BuildDbgDirty++; continue; }

            undo.Clear();
            void PutV(int x, int y, EdgeKind k) { undo.Add((true,  x, y, g.EdgeV[x, y])); g.SetEdgeV(x, y, k); }
            void PutH(int x, int y, EdgeKind k) { undo.Add((false, x, y, g.EdgeH[x, y])); g.SetEdgeH(x, y, k); }

            for (int y = y0; y < y1; y++) { PutV(x0, y, EdgeKind.High); PutV(x1, y, EdgeKind.High); }
            for (int x = x0; x < x1; x++) { PutH(x, y0, EdgeKind.High); PutH(x, y1, EdgeKind.High); }

            // At least two doors, on two different walls, so the inside is never a one-way trap
            // and a breach is a choice of approach rather than a queue at the only opening.
            for (int d = 0; d < 2; d++)
                switch ((Util.RandInt(0, 3) + d * 2) % 4)
                {
                    case 0: PutH(Util.RandInt(x0, x1 - 1), y0, EdgeKind.Door); break;
                    case 1: PutH(Util.RandInt(x0, x1 - 1), y1, EdgeKind.Door); break;
                    case 2: PutV(x0, Util.RandInt(y0, y1 - 1), EdgeKind.Door); break;
                    default: PutV(x1, Util.RandInt(y0, y1 - 1), EdgeKind.Door); break;
                }

            // THE VALIDATOR. Same predicate the game moves through, so a pass means a pass.
            var after = g.CostMap(players[0].X, players[0].Y, null, out _, 9999);
            int reachAfter = 0;
            for (int x = 0; x < g.W; x++) for (int y = 0; y < g.H; y++) if (after[x, y] >= 0) reachAfter++;

            bool ok = reachAfter == reachBefore;
            if (ok)                                   // and nobody is standing inside a wall's grip
                foreach (var u in enemies) if (after[u.X, u.Y] < 0) { ok = false; break; }

            if (!ok)
            {
                for (int i = undo.Count - 1; i >= 0; i--)
                {
                    var e = undo[i];
                    if (e.vert) g.EdgeV[e.x, e.y] = e.was; else g.EdgeH[e.x, e.y] = e.was;
                }
                BuildDbgSealed++; continue;
            }
            taken.Add((x0, y0, x1, y1));
            made++;
        }
    }


    // ══════════════════ SIGHTLINE_BUILDINGTEST ══════════════════
    /// Buildings, on REAL boards through the REAL Build, because the interesting failure is not
    /// "the stamper is wrong" but "the stamper is right and the board it lands on is not".
    ///
    /// The load-bearing leg is (C): a wall is the first thing on this board that can make a
    /// mission unwinnable, and it can do it silently. The validator's promise is that a building
    /// never removes a reachable tile, and this checks that promise against the SAME board with
    /// the edge queries switched off — so the comparison is one board, not two, and cannot be
    /// confounded by the RNG. It forces `Buildings` on itself, so it still tests the stamper
    /// while the shipped default is off.
    // ══ P37 — SIGHTLINE_DENSITYTEST ═════════════════════════════════════════════════════════
    /// What this pins:
    ///
    /// (A) THE SHIPPED BOARD DOES NOT MOVE. At 18x11 the cell grid is one cell at origin (0,0) with
    ///     an area ratio of exactly 1, so `DensityScaling` is a no-op there BY CONSTRUCTION. The leg
    ///     asserts it over the whole tile+height board rather than over a cover count, because a
    ///     count is the one thing a density wave could keep while moving everything else.
    /// (B) THE BIG BOARD FILLS, and the leg is not vacuous. Cover density at 40x28 with the wave ON
    ///     must be within a quarter of the reference board's; with it OFF it must be under HALF of
    ///     it. The second half is what makes the first mean something: the defect this wave fixes
    ///     has to be visible in the same instrument that shows the fix.
    /// (C) A ROOM STAYS IN ITS ROOM. The archetypes promise "no column is fully walled", and the
    ///     spine archetype used to run `for y < grid.H` — which on a taller board is a wall through
    ///     every room below this one. Asserted on columns AND rows.
    /// (D) NO POCKETS. At least 95% of floor tiles are reachable from a player spawn.
    ///     `EnsureConnectivity` guarantees the named POINTS are mutually reachable and says nothing
    ///     about the rest of the board; four rooms of walls is exactly the shape that could strand a
    ///     corner nobody named.
    public static string DensitySelfTest(int seeds = 16)
    {
        var fails = new List<string>();
        bool savedD = DensityScaling;
        int savedW = Cfg.GridW, savedH = Cfg.GridH, savedT = Cfg.Tile;

        // A cover fraction over the WHOLE board, which is what a player reads as clutter.
        double CoverFrac(Grid g)
        {
            int cov = 0;
            for (int x = 0; x < g.W; x++)
                for (int y = 0; y < g.H; y++)
                    if (g.Tiles[x, y] == TileType.LowCover || g.Tiles[x, y] == TileType.HighCover) cov++;
            return (double)cov / (g.W * g.H);
        }

        Grid BuildOne(int seed, bool scaled)
        {
            DensityScaling = scaled;
            Util.Reseed(77000 + seed);
            var g = new Grid(); var sq = TrainingSquad(); var fo = new List<Unit>();
            Build(g, sq, fo, 3);
            return g;
        }

        // ── (A) the shipped board, both ways.
        Cfg.SetBoard(Maps.TemplateW, Maps.TemplateH);
        double refFrac = 0;
        for (int s = 0; s < seeds; s++)
        {
            var on = BuildOne(s, true);
            var off = BuildOne(s, false);
            refFrac += CoverFrac(on);
            for (int x = 0; x < on.W; x++)
                for (int y = 0; y < on.H; y++)
                    if (on.Tiles[x, y] != off.Tiles[x, y] || on.Height[x, y] != off.Height[x, y])
                    { fails.Add($"(A) seed{s}: the shipped 18x11 board moved at {x},{y}"); s = seeds; break; }
        }
        refFrac /= seeds;

        // ── (B)(C)(D) a board that is four reference rooms across.
        Cfg.SetBoard(40, 28);
        double onFrac = 0, offFrac = 0;
        int worstColumn = 0, worstRow = 0; double worstReach = 1.0;
        for (int s = 0; s < seeds; s++)
        {
            var on = BuildOne(s, true);
            onFrac += CoverFrac(on);
            offFrac += CoverFrac(BuildOne(s, false));

            for (int x = 0; x < on.W; x++)
            {
                int run = 0;
                for (int y = 0; y < on.H; y++) if (!on.IsFloor(x, y)) run++;
                worstColumn = Math.Max(worstColumn, run);
            }
            for (int y = 0; y < on.H; y++)
            {
                int run = 0;
                for (int x = 0; x < on.W; x++) if (!on.IsFloor(x, y)) run++;
                worstRow = Math.Max(worstRow, run);
            }

            Util.Reseed(77000 + s);
            var sq = TrainingSquad(); var fo = new List<Unit>();
            DensityScaling = true;
            var g2 = new Grid(); Build(g2, sq, fo, 3);
            var cost = g2.CostMap(sq[0].X, sq[0].Y, null, out _, 99999);
            int floor = 0, seen = 0;
            for (int x = 0; x < g2.W; x++)
                for (int y = 0; y < g2.H; y++)
                    if (g2.IsFloor(x, y)) { floor++; if (cost[x, y] >= 0) seen++; }
            if (floor > 0) worstReach = Math.Min(worstReach, (double)seen / floor);
        }
        onFrac /= seeds; offFrac /= seeds;

        // A THIRD EITHER WAY, not equality. The reference frame is 198 tiles and a cell on this
        // board is 280, so a motif fills ~70% of its room and the sprinkle covers the rest; the
        // result sits a little UNDER the reference, which is the intended direction — a bigger
        // board is meant to buy lateral choice, and a board packed to 18x11 density at five times
        // the area is a maze, not a battlefield. What the leg is for is "the same ORDER of
        // density", which is exactly what the unscaled 3.6% is not.
        if (onFrac < refFrac * 0.70 || onFrac > refFrac * 1.30)
            fails.Add($"(B) 40x28 cover density {onFrac:P1} against the 18x11 reference {refFrac:P1}");
        if (offFrac >= refFrac * 0.5)
            fails.Add($"(B) SIGHTLINE_DENSITY=0 gives {offFrac:P1} at 40x28 — the leg cannot see the defect it exists for");
        if (worstColumn >= 28) fails.Add($"(C) a column is fully walled ({worstColumn} of 28)");
        if (worstRow >= 40) fails.Add($"(C) a row is fully walled ({worstRow} of 40)");
        if (worstReach < 0.95) fails.Add($"(D) only {worstReach:P1} of floor tiles reachable from a spawn");

        DensityScaling = savedD;
        Cfg.SetBoard(savedW, savedH, savedT);
        return fails.Count == 0
            ? $"DENSITYTEST: PASS (18x11 byte-identical both ways over {seeds} seeds; 40x28 fills to {onFrac:P1} against a {refFrac:P1} reference and {offFrac:P1} unscaled; longest walled column {worstColumn}/28, row {worstRow}/40; {worstReach:P1} of floor reachable)"
            : "DENSITYTEST: FAIL\n  " + string.Join("\n  ", fails);
    }

    public static string BuildingSelfTest(int seeds = 24)
    {
        var fails = new List<string>();
        bool savedB = Buildings, savedE = Edges.Enabled;
        int withWalls = 0, totalWalls = 0, totalDoors = 0;

        for (int s = 0; s < seeds; s++)
        {
            Buildings = true; Edges.Enabled = true;
            Util.Reseed(42000 + s);
            var g1 = new Grid(); var sq1 = TrainingSquad(); var fo1 = new List<Unit>();
            Build(g1, sq1, fo1, 3);
            StampBuildings(g1, sq1, fo1);      // Game.SetupMission's call, reproduced

            Buildings = false; Edges.Enabled = true;
            Util.Reseed(42000 + s);
            var g2 = new Grid(); var sq2 = TrainingSquad(); var fo2 = new List<Unit>();
            Build(g2, sq2, fo2, 3);
            StampBuildings(g2, sq2, fo2);      // with Buildings=false this must do nothing at all

            // (A) the restore flag really restores
            if (g2.AnyEdges) fails.Add($"seed{s}: BUILDINGS=0 still placed an edge");

            // (B) StampBuildings is PURELY ADDITIVE to the tile board. It runs last and stamps no
            //     tiles, so the eight per-tile layers must be untouched — which is also why
            //     SIGHTLINE_BUILDINGS=0 is a free measurement arm rather than merely a faithful one.
            bool diverged = false;
            for (int x = 0; x < g1.W && !diverged; x++)
                for (int y = 0; y < g1.H && !diverged; y++)
                    if (g1.Tiles[x, y] != g2.Tiles[x, y] || g1.Height[x, y] != g2.Height[x, y]
                        || g1.Barrel[x, y] != g2.Barrel[x, y] || g1.Ground[x, y] != g2.Ground[x, y])
                    { fails.Add($"seed{s}: a building moved the TILE board at {x},{y}"); diverged = true; }

            if (!g1.AnyEdges) continue;
            withWalls++;
            for (int x = 0; x <= g1.W; x++) for (int y = 0; y < g1.H; y++)
            { if (g1.EdgeV[x, y] == EdgeKind.High) totalWalls++; if (g1.EdgeV[x, y] == EdgeKind.Door) totalDoors++; }
            for (int x = 0; x < g1.W; x++) for (int y = 0; y <= g1.H; y++)
            { if (g1.EdgeH[x, y] == EdgeKind.High) totalWalls++; if (g1.EdgeH[x, y] == EdgeKind.Door) totalDoors++; }

            // (C) THE PROMISE: no building removes a reachable tile. Same board, edge queries off
            //     versus on, so nothing but the walls can account for a difference.
            var withEdges = g1.CostMap(sq1[0].X, sq1[0].Y, null, out _, 9999);
            Edges.Enabled = false;
            var without = g1.CostMap(sq1[0].X, sq1[0].Y, null, out _, 9999);
            Edges.Enabled = true;
            int lost = 0;
            for (int x = 0; x < g1.W; x++) for (int y = 0; y < g1.H; y++)
                if (without[x, y] >= 0 && withEdges[x, y] < 0) lost++;
            if (lost > 0) fails.Add($"seed{s}: buildings STRANDED {lost} tile(s)");
        }

        Buildings = savedB; Edges.Enabled = savedE;

        // (D) the stamper must actually fire. A validator that reverts everything would pass every
        //     leg above and ship a feature that does nothing — this repo's characteristic failure.
        if (withWalls * 100 / seeds < 40)
            fails.Add($"only {withWalls}/{seeds} boards got a building - the validator is eating them");
        if (withWalls > 0 && totalDoors < withWalls * 2)
            fails.Add($"{totalDoors} doors across {withWalls} walled boards - expected >= 2 each");

        return fails.Count == 0
            ? $"BUILDINGTEST: PASS ({withWalls}/{seeds} boards walled, {totalWalls} wall segments, {totalDoors} doors, "
              + $"0 stranded; validator refused {BuildDbgSealed} would-be seals)"
            : "BUILDINGTEST: FAIL\n  " + string.Join("\n  ", fails);
    }

    // ---- Procedural map generation -----------------------------------------
    // The procedural path picks one of several mid-field cover ARCHETYPES at random so
    // generated missions don't all look the same. Every archetype preserves the 4.2
    // encounter-geometry intent: sightline-blocking HIGH cover concentrated in the
    // mid-field (cols ~6-12), at least one deliberately OPEN "risky direct" lane so the
    // board stays traversable, and no fully-walled column. Plateaus (incl. a tier-2
    // redoubt on later missions) are shared across archetypes, and `EnsureConnectivity`
    // (run by Build afterwards) is the final net should sprinkles/protective cover ever
    // pinch a path. Spawn columns (0-3) and enemy columns (14-17) are left clear.

    // ══ P37: THE BOARD FILLS ════════════════════════════════════════════════════════════════
    /// The reference board the four archetypes were authored on. Every literal coordinate in
    /// `ArchScreen` / `ArchRedoubt` / `ArchTwinCorridors` / `ArchDiagonalWall` is in THIS frame,
    /// and was until P37 stamped straight onto the grid — so on a 40x28 board all four motifs
    /// landed inside the top-left 18x11 and the other 922 tiles were an empty plain.
    public const int RefW = 18, RefH = 11;

    /// SIGHTLINE_DENSITY=0 restores the pre-P37 build: ONE archetype, ONE set of plateaus and the
    /// flat sprinkle count, wherever the board's corner happens to be. It is the arm for pricing
    /// this wave, and on the shipped 18x11 board it is a NO-OP BY CONSTRUCTION — one cell, at
    /// origin (0,0), with an area ratio of exactly 1 — which is what DENSITYTEST leg (A) asserts
    /// against the whole board signature rather than against a cover count.
    public static bool DensityScaling = true;

    /// How many reference-sized cells this board is, and where each one starts. A bigger board is
    /// MORE ROOMS, not one stretched room: stretching an archetype keeps its shape and loses its
    /// SCALE, and scale is the whole content of a cover motif — a screen whose gaps are six tiles
    /// wide is not a screen, it is four separate walls. Tiling keeps every gap, lane and breach at
    /// the size a soldier's six-tile move was tuned against, and gives a big board local structure
    /// instead of distant structure.
    ///
    /// Each cell gets its OWN archetype roll, so a large board is a patchwork of different rooms
    /// rather than the same motif repeated — which is also why the roll stays inside the loop and
    /// not above it.
    static void CellGrid(Grid grid, out int cx, out int cy, out int cw, out int ch)
    {
        cx = DensityScaling ? Math.Max(1, grid.W / RefW) : 1;
        cy = DensityScaling ? Math.Max(1, grid.H / RefH) : 1;
        cw = grid.W / cx;
        ch = grid.H / cy;
    }

    static void BuildProcedural(Grid grid, HashSet<(int, int)> occupied,
                                HashSet<(int, int)> evac, int missionNum)
    {
        CellGrid(grid, out int cx, out int cy, out int cw, out int ch);
        for (int j = 0; j < cy; j++)
            for (int i = 0; i < cx; i++)
            {
                // Centre the reference frame in its cell, so the slack a non-multiple board leaves
                // becomes a margin around each room rather than a fringe on one side. At 18x11 the
                // cell IS the frame and the offset is (0,0).
                int ox = i * cw + (cw - RefW) / 2, oy = j * ch + (ch - RefH) / 2;

                // contested high ground: raised plateaus in the mid-field (more on later missions).
                // Shared by all archetypes so elevation play is always present.
                RaisePlateau(grid, evac, ox + 7, oy + 3, 2, 2);
                RaisePlateau(grid, evac, ox + 11, oy + 7, 2, 2);
                if (Run.Pace(missionNum) >= 3) RaisePlateau(grid, evac, ox + Util.RandInt(6, 11), oy + Util.RandInt(1, 8), 2, 2);
                // a commanding tier-2 redoubt appears on later missions (sees over high cover)
                if (Run.Pace(missionNum) >= 4) RaisePlateau(grid, evac, ox + Util.RandInt(7, 10), oy + Util.RandInt(3, 6), 2, 2, 2);

                // pick a mid-field cover archetype (variety); each leaves an open lane + no walled column
                switch (Util.RandInt(0, 3))
                {
                    case 0:  ArchScreen(grid, occupied, ox, oy);        break;   // the 4.2 staggered screen
                    case 1:  ArchRedoubt(grid, occupied, ox, oy);       break;   // a central bunker, flank lanes
                    case 2:  ArchTwinCorridors(grid, occupied, ox, oy); break;   // two cover spines, a centre gap
                    default: ArchDiagonalWall(grid, occupied, ox, oy);  break;   // a slanted wall with a breach
                }
            }

        // random crates (a touch more clutter on later missions; biased toward LoS-blocking high
        // cover). Scaled by AREA, not by cell count: the cells are only approximately the reference
        // size, and it is tiles-per-tile that a player reads as clutter. Exactly the old count at
        // 18x11, because the ratio is exactly 1 there.
        int sprinkle = 14 + Math.Min(6, Run.Pace(missionNum));
        if (DensityScaling) sprinkle = (int)Math.Round(sprinkle * (double)(grid.W * grid.H) / (RefW * RefH));
        Sprinkle(grid, occupied, sprinkle);
    }

    /// Archetype 0 — the original 4.2 staggered mid-field SCREEN of high cover: breaks the
    /// long cross-board sightlines so the squad can advance into the midfield under cover
    /// before tripping a pod. No column is fully walled; row 5 is the one open risky lane.
    static void ArchScreen(Grid grid, HashSet<(int, int)> occupied, int ox, int oy)
    {
        var screen = new (int x, int y)[]
        {
            (7, 1), (7, 2), (7, 3),   (8, 6), (8, 7), (8, 8),
            (9, 0), (9, 1), (9, 9), (9, 10),   (10, 3), (10, 4), (10, 7), (10, 8),
            (11, 1), (11, 2),
        };
        foreach (var (sx, sy) in screen) PlaceCover(grid, occupied, ox + sx, oy + sy, TileType.HighCover);
        // low cover flanking the open central lane, for cover-fighting on the direct route
        PlaceCover(grid, occupied, ox + 6, oy + 5, TileType.LowCover);
        PlaceCover(grid, occupied, ox + 12, oy + 5, TileType.LowCover);
    }

    /// Archetype 1 — a central REDOUBT: a compact high-cover bunker mid-board with a low-cover
    /// apron, leaving wide flanking lanes top and bottom. Rewards a flank rather than a frontal
    /// push; the bunker breaks the central sightline while the rims stay open.
    static void ArchRedoubt(Grid grid, HashSet<(int, int)> occupied, int ox, int oy)
    {
        // high-cover ring of a hollow bunker around the mid-field (rows 3-7, cols 8-10).
        // The WEST face at row 5 is left open as a doorway, so the interior (and a centre
        // terminal/captive, if the objective seats one there) stays reachable without the
        // connectivity net having to carve in.
        var ring = new (int x, int y)[]
        {
            (8, 3), (9, 3), (10, 3),
            (8, 4),                 (10, 4),
                                    (10, 5),   // west doorway (8,5) open; east slit (10,5)
            (8, 6),                 (10, 6),
            (8, 7), (9, 7), (10, 7),
        };
        foreach (var (sx, sy) in ring) PlaceCover(grid, occupied, ox + sx, oy + sy, TileType.HighCover);
        // low-cover apron on the approaches (covered fighting positions outside the bunker)
        PlaceCover(grid, occupied, ox + 6, oy + 4, TileType.LowCover);
        PlaceCover(grid, occupied, ox + 6, oy + 6, TileType.LowCover);
        PlaceCover(grid, occupied, ox + 12, oy + 4, TileType.LowCover);
        PlaceCover(grid, occupied, ox + 12, oy + 6, TileType.LowCover);
        // top/bottom flanking lanes (rows 0-1 and 9-10) are deliberately left open.
    }

    /// Archetype 2 — TWIN CORRIDORS: two vertical high-cover spines (a forward and a rear
    /// staggered wall), each gapped so a soldier can slip through, with an open central seam
    /// between them. Creates layered cover and channels movement into the gaps.
    static void ArchTwinCorridors(Grid grid, HashSet<(int, int)> occupied, int ox, int oy)
    {
        // The spines run the height of the REFERENCE FRAME, not of the grid. At 18x11 those are the
        // same number; on a taller board they are not, and a spine run to grid.H would be a wall
        // through every room below this one.
        // forward spine at col 7, gap at rows 4-5 (the open seam)
        for (int y = 0; y < RefH; y++)
            if (y < 4 || y > 5) PlaceCover(grid, occupied, ox + 7, oy + y, TileType.HighCover);
        // rear spine at col 11, gap at rows 5-6 (offset from the forward gap -> staggered)
        for (int y = 0; y < RefH; y++)
            if (y < 5 || y > 6) PlaceCover(grid, occupied, ox + 11, oy + y, TileType.HighCover);
        // low cover bracketing the central seam (cover-fight in the gap between the spines)
        PlaceCover(grid, occupied, ox + 9, oy + 4, TileType.LowCover);
        PlaceCover(grid, occupied, ox + 9, oy + 6, TileType.LowCover);
    }

    /// Archetype 3 — a DIAGONAL WALL of high cover slashing across the mid-field with a single
    /// breach gap, plus a low-cover counter-diagonal. Strong sightline break on a slant; the
    /// breach is the contested crossing, and the wall's ends leave the rims open.
    static void ArchDiagonalWall(Grid grid, HashSet<(int, int)> occupied, int ox, int oy)
    {
        // a slanted high-cover wall from upper-mid to lower-mid, with a one-tile breach
        var wall = new (int x, int y)[]
        {
            (7, 1), (7, 2),
            (8, 3), (8, 4),
            (9, 5),                 // breach is the gap just below here (row 6 left open)
            (10, 7), (10, 8),
            (11, 9),
        };
        foreach (var (sx, sy) in wall) PlaceCover(grid, occupied, ox + sx, oy + sy, TileType.HighCover);
        // a short low-cover counter-diagonal giving the attacker covered footing to the breach
        PlaceCover(grid, occupied, ox + 6, oy + 6, TileType.LowCover);
        PlaceCover(grid, occupied, ox + 9, oy + 6, TileType.LowCover);   // flanks the breach, doesn't seal it
        PlaceCover(grid, occupied, ox + 12, oy + 6, TileType.LowCover);
    }

    /// Place a cover tile only on an unreserved, currently-empty floor tile (and mark it
    /// occupied). The shared primitive for all procedural archetypes.
    static void PlaceCover(Grid grid, HashSet<(int, int)> occupied, int x, int y, TileType t)
    {
        if (!grid.InBounds(x, y) || occupied.Contains((x, y)) || grid.Tiles[x, y] != TileType.Floor) return;
        grid.Tiles[x, y] = t;
        occupied.Add((x, y));
    }

    /// Scatter `count` random crates across the mid-board, biased toward LoS-blocking high cover.
    static void Sprinkle(Grid grid, HashSet<(int, int)> occupied, int count)
    {
        int guard = 0;
        while (count > 0 && guard++ < 500)
        {
            int x = Util.RandInt(3, grid.W - 4);
            int y = Util.RandInt(0, grid.H - 1);
            if (occupied.Contains((x, y)) || grid.Tiles[x, y] != TileType.Floor) continue;
            grid.Tiles[x, y] = Util.Roll(45) ? TileType.LowCover : TileType.HighCover;
            occupied.Add((x, y));
            count--;
        }
    }

    /// Stamp a hand-authored template onto the grid, then verify every spawn, the
    /// evac zone and the terminal stay mutually reachable over walkable terrain.
    /// Reverts and returns false if the layout is malformed or would wall anyone off.
    /// P28: how many times a layout was refused because its DIMENSIONS were wrong (never
    /// because the board it made was unreachable). Must be 0 in a healthy build; the gate asserts
    /// it, and Game.TemplateGate() checks every authored arena up front so this never has to fire
    /// in the first place.
    /// P29 — can the board being played use the hand-authored arenas at all? They are drawn for
    /// Maps.TemplateW x TemplateH; at any other size every one of them is orphaned. Asking ONCE,
    /// here, is the difference between a deliberate configuration and 35 silent per-mission
    /// rejections that look exactly like a legitimate connectivity refusal.
    public static bool ArenasFitBoard => Cfg.GridW == Maps.TemplateW && Cfg.GridH == Maps.TemplateH;
    static bool _saidArenasUnusable;

    public static void NoteArenaFit()
    {
        if (ArenasFitBoard || _saidArenasUnusable) return;
        _saidArenasUnusable = true;
        Console.Error.WriteLine($"SIGHTLINE: board is {Cfg.GridW}x{Cfg.GridH} but the {Maps.Layouts.Length} "
            + $"authored arenas are drawn for {Maps.TemplateW}x{Maps.TemplateH}. They are OUT OF PLAY on this "
            + "board and its missions are procedural. Expected on a big board (the campaign's board curve "
            + "from mission 3, or SIGHTLINE_BIGMAP).");
    }

    public static int LayoutDimMismatches;

    static bool TryApplyLayout(Grid g, HashSet<(int, int)> occupied, List<Unit> players,
                               List<Unit> enemies, HashSet<(int, int)> evac,
                               (int x, int y)? terminal, Maps.Arena arena, List<(int x, int y)> sabotage,
                               bool relaxEnemies = false)
    {
        // ══ P28 — A DIMENSION MISMATCH IS NOT A REJECTION ════════════════════════════════════
        // These two lines used to `return false` exactly as a legitimate connectivity rejection
        // does, so a template of the wrong size made the game fall back to a procedural board
        // SILENTLY, for every mission, forever. Nothing anywhere said so. That is survivable
        // today only because all 35 arenas happen to match Cfg.GridW/H — the moment the board
        // grows, every authored arena is orphaned INVISIBLY and the only symptom is that the
        // hand-made maps quietly stop appearing.
        // A rejection is a runtime outcome. A wrong SIZE is a BUG in the template, so it is
        // counted and shouted about. SIGHTLINE_TEMPLATEGATE turns the count into a PASS/FAIL.
        string[] tpl = arena.Tiles;
        if (tpl.Length != g.H || Array.Exists(tpl, r => r.Length != g.W))
        {
            LayoutDimMismatches++;
            if (LayoutDimMismatches <= 3)     // say it, but do not flood a 300-mission autoplay
                Console.Error.WriteLine($"SIGHTLINE: layout REJECTED for WRONG SIZE — template is " +
                    $"{tpl.Length} rows x {(tpl.Length > 0 ? tpl[0].Length : 0)} cols, board is " +
                    $"{g.H} x {g.W}. Falling back to a procedural board. This is a template bug, " +
                    $"not a map-fit rejection (see Mission.TryApplyLayout / SIGHTLINE_TEMPLATEGATE).");
            return false;
        }
        // defense-in-depth (matches EnsureConnectivity/PlaceBarrels): the connectivity flood
        // starts from players[0], so an empty deploy must refuse the layout, not crash. The
        // real guarantee is upstream — DebriefSurvivors never leaves the squad at zero.
        if (players.Count == 0) return false;

        for (int y = 0; y < g.H; y++)
            for (int x = 0; x < g.W; x++)
            {
                if (occupied.Contains((x, y))) continue;   // reserved -> stays open floor
                switch (tpl[y][x])
                {
                    case 'o': g.Tiles[x, y] = TileType.LowCover; break;
                    case '#': g.Tiles[x, y] = TileType.HighCover; break;
                    case '^': g.Height[x, y] = 1; break;   // walkable raised plateau (tier 1)
                    case '=': g.Height[x, y] = 2; break;   // walkable raised plateau (tier 2)
                    case 'B': g.Barrel[x, y] = true; break;// explosive barrel (tile stays floor underneath)
                    default:  break;                        // '.' open floor
                }
            }

        // ══ P47 — THE EDGE PASS ══════════════════════════════════════════════════════════════
        // Stamped AFTER the tiles and BEFORE the flood, unconditionally — including boundaries
        // touching a reserved tile. A wall beside a spawn is legitimate authored geometry (it is
        // how a squad deploys inside a building), and the `occupied` rule exists to stop a template
        // BURYING a reserved tile in cover, which an edge cannot do: an edge consumes no floor.
        // What it CAN do is seal one off, and that is precisely what the connectivity flood below
        // already catches, because `Grid.CostMap` reads the edge layer. So there is no new guard
        // here and there should not be: the guard that was already there is the right one.
        if (arena.HasEdges)
        {
            for (int y = 0; y < g.H; y++)
                for (int x = 0; x <= g.W; x++) g.SetEdgeV(x, y, arena.EdgeV[x, y]);
            for (int y = 0; y <= g.H; y++)
                for (int x = 0; x < g.W; x++) g.SetEdgeH(x, y, arena.EdgeH[x, y]);
        }

        // connectivity: flood from the first soldier across walkable tiles (cover = wall)
        var cost = g.CostMap(players[0].X, players[0].Y, (x, y) => false, out _, 9999);
        bool Reachable(int x, int y) => g.InBounds(x, y) && cost[x, y] >= 0;

        bool ok = true;
        foreach (var u in players) if (!Reachable(u.X, u.Y)) ok = false;
        // P26: on the arena-owned path PlanBoard already proved every SPAWN and every SITE mutually
        // reachable on a strict under-approximation of this board (the stamp only ever makes
        // `occupied` tiles MORE walkable), so the sole remaining reject cause is an enemy seated in
        // a pocket — and EnsureConnectivity carves for exactly that, unconditionally, a few lines
        // later. Handing it to the carve rather than to a revert is what makes the mid-build revert
        // UNREACHABLE on the arena path; ARENASITETEST leg (C) is the standing proof.
        if (!relaxEnemies)
            foreach (var u in enemies) if (!Reachable(u.X, u.Y)) ok = false;
        foreach (var t in evac) if (!Reachable(t.Item1, t.Item2)) ok = false;
        if (terminal.HasValue && !Reachable(terminal.Value.x, terminal.Value.y)) ok = false;
        if (sabotage != null) foreach (var s in sabotage) if (!Reachable(s.x, s.y)) ok = false;

        if (!ok)   // revert to a clean slate so the procedural path can run
        {
            for (int y = 0; y < g.H; y++)
                for (int x = 0; x < g.W; x++)
                    if (!occupied.Contains((x, y))) { g.Tiles[x, y] = TileType.Floor; g.Height[x, y] = 0; g.Barrel[x, y] = false; }
            // P47: and the walls with them. A revert that left the edges behind would hand the
            // procedural fallback a board shaped by the template it just rejected — L6's stale-layer
            // defect, one array further out.
            if (arena.HasEdges) g.ClearEdges();
            return false;
        }
        return true;
    }

    /// FUL-6 CRITICAL MASS: pure greedy pod-size split for an initial force of `count` —
    /// no RNG draw, and no pod of 1 from any count >= 2 (the waver telegraph needs a
    /// survivor): while remaining >= 5 take 3; then remainder 4 -> {2,2}, 3 -> {3},
    /// 2 -> {2}. So 7 -> {3,2,2}, 8 -> {3,3,2}, 9 -> {3,3,3}, 12 -> {3,3,3,3}.
    /// Public + static so the endless wave splitter (Game.Endless) and PODTEST share it.
    /// (count == 1 can only reach here from an endless top-up trickle; it keeps a 1-pod,
    /// which is morale-inert by construction — PodAtWaverPoint needs alive >= 2.)
    public static int[] PodPlan(int count) => PodPlan(count, PodMass);

    /// W4 THE SECOND AXIS — FORMATION MASS. `mass` 3 is the FUL-6 plan above, reproduced
    /// exactly (PODTEST pins its splits). A larger mass trades the number of SERIAL contacts for
    /// the number of bodies each contact presents at once, which is the raw
    /// `los-targets/ARMED-soldier-turn` number X1's decomposition named as the thing decision
    /// density is actually made of. Bodies are split as evenly as possible over
    /// round(count/mass) pods, so no pod is ever a lone body (the waver telegraph needs a
    /// survivor) and none is a shapeless blob. Pure — no RNG draw.
    public static int PodMass = 3;

    /// X2 measurement knob (SIGHTLINE_ENEMYBASE): the constant in `count = base + missionNum`.
    /// Default 4 = the historical force size.
    public static int EnemyBaseCount = 4;

    // ── P23 "THE APEX BITES" — THE CEILING BOUNDS THE BOARD, NOT THE LADDER ──────────────────
    //  L7 EVERY RUNG measured the finale's hostile count at heats 0-8 on the artifact and got
    //  6/7/7/8/9/9/9/9/9: **the boss mission has not grown a hostile since heat 4.** The heat
    //  table is correct (HEATLADDERTEST/MIDTOOTHTEST pin the cumulative vector and are green and
    //  right to be) — the loss is one level up, here, in two independent places:
    //    (A) `count` was CLAMPED to the board ceiling and the finale's de-stack subtracted from
    //        the CLAMPED value, so from heat 3 up the ladder's extra bodies were eaten by a
    //        ceiling the finale force never came near (its post-cut size is 6-11, against 12).
    //    (B) `bump` was reset to the bare per-mission growth, discarding heat's StatDelta.
    //  Both of rung 8's declared teeth were therefore switched off on the one mission that
    //  decides a campaign; L7 measured the apex rung at -0.6 (n=640, MDE 3.7) with mission 6
    //  moving the WRONG WAY (16.8 -> 23.9).
    //
    //  The two halves are SEPARATE DIALS on purpose. L7's partial arm (SIGHTLINE_ENEMYBASE=2)
    //  could only ease the ceiling, could not touch the stat strip, and its recovery therefore
    //  came from missions 2-5 rather than the finale — which is exactly why its +4.1 did not
    //  resolve on the odds scale. A combined lever would repeat that mistake.

    /// The BOARD-SEATING ceiling on an initial hostile force. It is a LAYOUT number, not a
    /// difficulty number: `SpawnEnemies`' collision-relocate pool is cols W-4..W-2 over all H
    /// rows (3 x 11 = 33 tiles on the 18x11 board) and every seated body is added to Build's
    /// `occupied` set, which both arena paths and `EnsureConnectivity` keep open. Shipped at 12,
    /// unchanged since it was raised from 10. `SIGHTLINE_FORCECEILING=<n>` prices it; P23
    /// deliberately did NOT spend it (see FORCETEST leg (E), which measures the real headroom).
    public static int ForceCeiling = 12;

    /// LEVER A — THE ORDER. `true` (shipped): the seating ceiling is applied LAST, to the force
    /// that is actually put on the board, instead of first, to the number the ladder ASKED for.
    /// Everything between the request and the seating is a SUBTRACTION (the opener grace, the
    /// sabotage and defend trims, the finale de-stack, the pod trim), so clamping first meant a
    /// ceiling the final force never came near still decided its size. `SIGHTLINE_CLAMPLAST=0`
    /// restores the pre-P23 order exactly.
    ///
    /// It cannot raise what the board must seat: the final value is clamped to `ForceCeiling`
    /// either way, so the worst case is the same 12 it has always been (FORCETEST leg (E)
    /// measures the seated worst case at 12, and 16 with the ceiling stressed to 16). And it is a
    /// NO-OP wherever the ceiling never bound — if
    /// `LastForceRequest <= ForceCeiling` the two orders are identical by construction, which is
    /// what leg (G) asserts. On the shipped tree that confines it to the finale from heat 5 up
    /// and to late/ELITE mid-run nodes at the top of the ladder.
    public static bool ClampLast = true;

    /// LEVER B. `true` (shipped): the finale keeps HEAT's own StatDelta and drops only the
    /// deployment CARD's (and the adaptive assist's) — the separation the pre-P23 comment's own
    /// wording ("the boss-card/heat StatDelta") had merged. The card's strip is the presentational
    /// half and is genuinely deliberate: the WARLORD *is* the elite, so its retinue should not be
    /// double-counted as one. Heat's is not presentational — it is the rung the player dialled.
    /// `SIGHTLINE_FINALESTAT=0` restores the pre-P23 strip (both discarded) exactly.
    public static bool FinaleHeatStat = true;

    /// Harness telemetry (SIGHTLINE_FORCETEST): the headcount and the force-wide stat bump the
    /// LAST `SpawnEnemies` actually built with, published after every trim, the finale de-stack
    /// and the pod trim — i.e. the numbers that reached the board, not the numbers requested.
    /// Written unconditionally (three int stores) and read by nothing in a live path.
    public static int LastForceCount = -1, LastStatBump = -1, LastForceRequest = -1;

    /// ...and whether any of the five declared FLOORS actually bound on that build (the opener,
    /// sabotage and defend trims and the pod trim floor at 3; the finale de-stack at 5). A rung
    /// whose body is missing because the force is already at its minimum is a different statement
    /// from a rung whose body was lost — FORCETEST leg (A2) needs to tell them apart, and it may
    /// not do it by re-deriving the arithmetic it is auditing.
    public static bool LastForceFloored = false;

    /// X2 (SIGHTLINE_OPENERTRIM): bodies removed from the BASE force on the opening missions —
    /// the full trim on mission 1, half (rounded up) on mission 2, none from mission 3. The same
    /// shape as Game.SetupMission's heat grace, applied to the force heat's grace cannot reach.
    ///
    /// SHIPPED at 1, measured end-to-end (DEVLOG §X2 round O1/S1). `SIGHTLINE_OPENERTRIM=0`
    /// restores the pre-X2 opener exactly.
    public static int OpenerTrim = 1;

    // ── P14 THE UNVERIFIED — THE SINGLE-MISSION MODES' DEPTH, IN ONE PLACE ────────────────────
    //  SKIRMISH and DAILY enter through Game.SetupMission(1), so `missionNum` is the literal 1 for
    //  every fight they field at every heat rung. P4 THE MODES GET THE BESTIARY threaded a second
    //  parameter (`rosterTier`) through Mission.Build for the ARCHETYPE gates and stopped there —
    //  measured on the pre-P14 tree (SIGHTLINE_MODEFORCEPROBE), FOUR other consumers of "how deep
    //  is this fight" were still reading that literal 1 at heat 8:
    //    * OpenerTrim below      — the CAMPAIGN cold-opener grace, firing in both modes (-1 body)
    //    * MakeVip               — the escort asset pinned at 16 HP / 0 armor on every rung
    //    * Combat.HvtHpBonus     — the Decapitate HVT's bonus pinned at +7 HP on every rung
    //    * Game.SpawnReinforcements / SpawnDefendWave — DEFEND waves built at tier 1 with the
    //                              heat stat explicitly zeroed by the m1-2 grace
    //  `ModeDepth` is that number, published by Game.SetupMission alongside `rosterTier` and -1
    //  for every other mode, and `DepthFor` is the one read. The CAMPAIGN is inert by
    //  construction: ModeDepth is -1 there, so DepthFor(n) == n at every call site.
    public static int ModeDepth = -1;

    /// SIGHTLINE_MODEDEPTH=0 — the pre-P14 modes, exactly (ModeDepth is never published, so every
    /// read below is the identity on the mission number and the immobility guard is off with it).
    public static bool ModeDepthOn = true;

    /// The heat-derived tier the single-mission modes stand in for a mission number: 3 (the full
    /// roster) at heat 0-2, 4 at 3-5, 5 at 6-8 — the campaign's own m3/m4/m5 tiers. P4 introduced
    /// it for the ARCHETYPE gates (Game.SetupMission's `rosterTier`); P14 publishes the same number
    /// as ModeDepth. One formula, two readers, so the two can never drift apart.
    public static int ModeTierFor(int heat) => Math.Clamp(3 + Math.Max(0, heat) / 3, 3, 5);

    /// The depth this fight should be PRICED at: the mode's own dial when a single-mission mode
    /// published one, otherwise the literal mission number.
    public static int DepthFor(int missionNum) => ModeDepth > 0 ? ModeDepth : Run.Pace(missionNum);   // C1: paced

    // ── P19 "THE ROSTER CONTESTS" — THE NAMED ELITE BELONGS TO THE ELITE NODE ────────────────
    // The campaign map ships a `NodeKind.Elite` whose whole advertised identity is "the heavier
    // fight, the biggest payout": Run.CardForNode gives it +2 bodies / +1 stat / a BONUS PERK, and
    // Run.ElitePremium pays it the map's top intel rate. The named mid-boss — the one piece of
    // content in the mid-game that forces a different plan (BREAKER rushes, BULWARK must be
    // flanked, WARDEN shells you off your tile) — was keyed on the MISSION NUMBER instead:
    // `n == 3 || n == 5`. The label and the thing it names were therefore disconnected in BOTH
    // directions, which is the exact failure C3 named: a label the game does not honour is worse
    // than no label.
    //   * a player who deliberately routed INTO an ELITE bought no named opponent for it;
    //   * a player who avoided every ELITE met one anyway, twice, on a schedule.
    // MEASURED on this tree before the change (n=120 campaigns, base 3d5c405): the flywheel's own
    // route played 31 ELITE nodes at h0-b0 alone, none of which fielded the elite they advertise.
    //
    // `EliteBoss` keys it on the NODE. `SIGHTLINE_ELITEBOSS=0` restores the mission-number rule
    // exactly (MidBossFor is the single predicate, so the restoration cannot drift).
    public static bool EliteBoss = true;

    /// The FINAL-APPROACH FLOOR, and the load-bearing half of the change. Keying PURELY on the
    /// node would gate the game's most distinctive body behind routing luck: `Run.GenerateMap`
    /// stamps only `max(1, mids/5)` ELITE nodes among ~8-12 mid nodes and a route takes exactly one
    /// node per column, so a large share of routes would meet no named elite at all — a content
    /// regression dressed as a fix. So the LAST FIGHT BEFORE THE FINALE always fields one, and the
    /// design statement is a sentence rather than a schedule: *the named elite fights you where you
    /// go looking for it — on the ELITE node — and once more, unavoidably, on the way to the boss.*
    ///
    /// `finalApproach` is that fight, computed on the ROUTE (Game.SetupMission walks the node's
    /// successors), NOT on the mission number: an Event node can occupy a route's column-4 slot, in
    /// which case the route plays no mission 5 at all and a bare `n == 5` floor silently misses it.
    /// MEASURED over all 1,098 enumerated routes of 200 maps (SIGHTLINE_ROSTERTEST leg p19-4): a
    /// mission-5 floor leaves 89 routes (8.1%) meeting no named elite — and the PRE-P19 mission-
    /// number rule itself left 30 (2.7%), which nobody had ever counted. The route-walked floor
    /// leaves zero. `MidBossFloorMission` is kept as a belt-and-braces proxy for any caller that
    /// cannot see the route.
    public static int MidBossFloorMission => Run.MaxMissions - 1;   // C1: "the last fight before the finale"

    /// The one predicate: does slot 0 of this force field the named mid-boss? `modeSlot` is the
    /// single-mission modes' own arm (SKIRMISH heat >= 4, Game.SetupMission) and is unchanged.
    public static bool MidBossFor(int missionNum, bool eliteNode, bool finalApproach, bool modeSlot)
        => modeSlot
        || (EliteBoss ? (eliteNode || finalApproach || missionNum == MidBossFloorMission)
                      : (missionNum == 3 || missionNum == 5));

    /// W4 — every body in a pod fields the pod LEAD's archetype (see the spawn loop). SHIPPED
    /// ON: measured exactly ladder-neutral (32.5% = 32.5% run completion, n=40) for the wave's
    /// biggest single gain on the "which target?" axis (+0.06 target-choices/ARMED) and
    /// Escort 12.57t -> 8.75t. `SIGHTLINE_PODUNIFORM=0` restores mixed pods.
    public static bool PodUniform = true;

    public static int[] PodPlan(int count, int mass)
    {
        var sizes = new List<int>();
        if (mass <= 3)
        {
            int rem = count;
            while (rem >= 5) { sizes.Add(3); rem -= 3; }
            if (rem == 4) { sizes.Add(2); sizes.Add(2); }
            else if (rem == 3) sizes.Add(3);
            else if (rem > 0) sizes.Add(rem);
            return sizes.ToArray();
        }
        if (count <= 0) return sizes.ToArray();
        int pods = Math.Max(1, (int)Math.Round(count / (double)mass, MidpointRounding.AwayFromZero));
        while (pods > 1 && count / pods < 2) pods--;      // never a pod of 1 while a merge is possible
        int baseSize = count / pods, extra = count % pods;
        for (int p = 0; p < pods; p++) sizes.Add(baseSize + (p < extra ? 1 : 0));
        return sizes.ToArray();
    }

    static void SpawnEnemies(Grid grid, List<Unit> enemies, int n, HashSet<(int, int)> evac,
                             int enemyDelta = 0, int statDelta = 0, bool sabotage = false,
                             int dmgDelta = 0, bool defend = false, int defendKeep = 0,
                             int shape = DeployFrontal, int rosterTier = -1, bool midBossSlot = false,
                             bool eliteNode = false, bool finalApproach = false, int heatStat = 0,
                             (int x, int y)[] garrison = null)
    {
        // THE MODES GET THE BESTIARY — ROSTER DEPTH is its own axis. `n` has always carried two
        // jobs: the NUMERIC ramp (headcount, the (n-1) stat bump, the opener trim, W9's heat
        // arithmetic) and the ROSTER tier (which archetypes SelectArchetype/FactionRoster may deal,
        // whether pods come in threes, whether slot 0 is the named mid-boss). SKIRMISH and DAILY
        // enter at n == 1 and dial difficulty through heat, so every one of the roster gates was
        // pinned to mission 1's teaching tier — measured pre-fix: 50 heat-0 skirmish builds fielded
        // exactly {GRUNT, SCOUT}. `rosterTier` is the second job on its own parameter. Default -1
        // means "the mission number", so the CAMPAIGN is byte-identical (every read below that
        // switched to rosterTier reads the same value it always did, and no draw moved); the modes
        // pass 3-5 off the heat dial (Game.SetupMission). `midBossSlot` is the mode's own mid-boss
        // arm (heat >= 4) — the campaign's `n == 3 || n == 5` rule is kept exactly as written.
        // C1: every SCALING read below uses the paced depth `pace`; the FINALE checks keep the mission
        // number `n` (only the last mission is the finale, whatever the run length).
        int pace = Run.Pace(n);
        if (rosterTier < 0) rosterTier = pace;
        // Headcount cap raised 10 -> 12 so the top-Heat "+enemy" rungs aren't silently wasted
        // (the +1/+1 from RELENTLESS/OVERWHELMING used to clip at 10 on later missions). 12 still
        // fits easily: spawns occupy cols 14-17 over grid.H rows (44 slots) and the collision loop
        // below relocates any overlap.
        // Difficulty RECALIBRATED to the grown squad: the curve was softened (3+n / (n-1)*2/3) back
        // when the squad was a struggling 4-strong. Since then deploy-growth (5-6 bodies), run boons,
        // Armor, and the Evac fix stacked huge squad power -> heat-0 hit ~97%/mission (too trivial).
        // Restored the enemy headcount (4+n, cap 12) and the full per-mission stat bump (n-1) so the
        // now-strong squad faces a real fight; Heat's deltas still stack for the mastery ladder.
        // X2: the base headcount is a static (default 4 — the historical `4 + n`) so a measured
        // round can price the BODY lever against the ACCURACY lever without a rebuild.
        // P23 THE APEX BITES — LEVER A. The REQUEST is what the deployment card and the heat ladder
        // ask for; `ForceCeiling` is what the BOARD can seat. Clamping here — before the five
        // subtractions below — let a ceiling the finished force never reaches decide its size, and
        // L7 measured the cost: the boss mission stopped growing a hostile at heat 4 and the apex
        // rung's declared body was eaten on the one mission that decides a campaign. Under
        // `ClampLast` the clamp moves to the END of the pipeline (just before the telemetry), so it
        // bounds what is SEATED. The seated worst case is unchanged (still <= ForceCeiling).
        int request = EnemyBaseCount + pace + enemyDelta;                     // deployment-card + Heat modifier
        int count = ClampLast ? request : Math.Clamp(request, 3, ForceCeiling);
        // Every trim below is floored. `Floor` is the same `Math.Max` with a witness attached, so
        // the harness can say "the force is at its minimum" without re-deriving the pipeline.
        bool floored = false;
        int Floor(int v, int f) { if (v < f) { floored = true; return f; } return v; }
        // R1 REVIEW FIX — the floor was `Math.Max(0, ...)`, which silently ATE the RECRUIT rung's
        // advertised relief on MISSION 1, the exact mission the on-ramp exists for: at n == 1 the
        // growth term is 0, so heat 0 gave max(0, 0) = 0 and RECRUIT (statDelta -1) gave
        // max(0, -1) = 0 — identical. Only the body count moved, while Hud.RecruitLines and
        // Heat.RecruitMod.Desc both promise "each -1 HP and aim". The floor drops to -1: one point
        // of force-wide relief may go BELOW the base, and no more (a deeper stack — RECRUIT plus a
        // multi-tier adaptive assist — still bottoms out at -1, so no archetype can be trivialised).
        // Heats 1-8 are bit-for-bit unchanged by construction: there statDelta = card.StatDelta +
        // heatStat is never negative, so (n-1)+statDelta >= 0 and the new floor is unreachable.
        int bump = Math.Max(-1, (pace - 1) + statDelta);        // stat growth per mission +/- card (relief floor -1)
        // X2 TRUE NORTH II — THE COLD OPENER. Game.SetupMission already ramps HEAT's escalation in
        // over m1-2 ("the measured ~20% mission-1 loss, which hard-caps run completion"), but that
        // grace is gated on heat > 0, so the BASE force meets the coldest squad in the game with no
        // ramp at all: 5 hostiles against 4 rookies with no promotion, perk, mod or boon — and,
        // since X1, +3 HP each. The X2 baseline measured mission 1 at 75% win (n=40) against
        // 90% for missions 3-4, i.e. the difficulty curve is U-SHAPED at heat 0 and the opener is
        // as lethal as the finale — exactly the front-loaded anxiety DESIGN.md §3.D forbids.
        // RECRUIT ran the experiment for us: over the SAME 40 worlds its only mission-1 difference
        // is one fewer body (its -1 stat is a no-op at m1, where bump is already 0), and mission 1
        // reads 100% (n=40, zero losses). OpenerTrim gives the base force the same ramp heat has:
        // full trim on m1, half on m2, nothing from m3. Default 0 = the pre-X2 opener.
        // INTEGRATION NOTE: R1's -1 relief floor now makes RECRUIT's m1 stat relief real, so the
        // "its -1 stat is a no-op at m1" clause above is no longer true post-R1 — the OpenerTrim
        // diagnosis and its measurement are unaffected (they turn on the BODY count, not the stat).
        // P14: gated on DEPTH, not on the raw mission number. A SKIRMISH/DAILY player DIALLED the
        // rung (W9's own words, when it took the heat grace away from these modes for exactly this
        // reason); the cold-opener grace is the same class of relief and was still firing there,
        // measured at -1 body on every rung. `Mission.ModeDepth` publishes 3-5 for those modes, so
        // DepthFor is >= 3 and the trim is skipped; the campaign reads DepthFor(n) == n, unchanged.
        int trimDepth = DepthFor(n);
        if (OpenerTrim > 0 && trimDepth <= 2)
            count = Floor(count - (trimDepth == 1 ? OpenerTrim : (OpenerTrim + 1) / 2), 3);
        // SABOTAGE relief (the weakest objective / m5 gate, ~65% -> aiming ~85%): the difficulty of
        // this objective IS the 3x split-and-go-loud tempo, not raw bodies, so trim the force by 2
        // (floored at 3) so a divided squad isn't also out-gunned. Stat bump is untouched and the
        // Heat ladder still applies on top, so the mastery curve is preserved.
        if (sabotage) count = Floor(count - 2, 3);
        // FUL-4 HOLDFAST (Defend 38% h0 measured pre-fix, target 60-80): DEFEND's real force is
        // the INITIAL screen PLUS every SpawnDefendWave reinforcement, so an untrimmed opener
        // double-counts the objective's difficulty — the timer IS the pressure. Mirror the
        // sabotage trim, one step deeper (waves keep arriving all mission; sabotage gets none).
        // FUL-13 R2 (defendKeep): the FLAT −3 was silently EATING the heat ladder's EnemyDelta
        // (+2..+4 bodies at rungs 4-8) — with the timer bounding total exposure, Defend became
        // the top rungs' free square (measured: 82% h0 -> 97% h6 / 91% h8 unpinned; 96% n=89
        // defend-pinned h8, still 95-100% after the R1 wave-stat lever alone). defendKeep gives
        // back half the GRACED heat bodies (0 at h0-2, 1 at h4-6, 2 at h8; the m1-2 grace zeroes
        // it with heatEnemy) so heat reaches the hold without re-breaking FUL-4's h0 repair.
        if (defend) count = Floor(count - 3 + Math.Clamp(defendKeep, 0, 2), 3);
        // Final mission (the WARLORD boss): de-stack the force. This was the core of the ~90% m6
        // loss wall -- the squad cleared m1-5 (m5 often wins ~100%, partly because it isn't always
        // forced Eliminate) then got alpha-struck on m6's forced full-clear. The compounding cause:
        // the BOSS campaign node's card adds EnemyDelta +2 / StatDelta +1 (Run.CardForNode) ON TOP
        // of the named boss itself, so the body count saturates the cap at 12 and every supporter
        // also gets +1 stat -- a double-counted "elite" mission. The boss IS the elite, so here we
        // (1) cut the supporting force HARD (boss is one slot, i==0) and (2) strip the rank-and-file
        // stat bump back to the plain per-mission growth (the boss keeps its own explicit stats set
        // below). Net at heat 0: 8 hostiles incl. the boss (was 12), supporters at +5 not +6.
        if (n >= Run.MaxMissions)
        {
            // SIGNAL W5 — m6 bite, MEASURED SIZE (paired flywheel, h0 slots 0-19): the finale is
            // startlingly body-count sensitive. With the kits live: restore +2 bodies (count-2) ->
            // m6 70-76% conditional and h0 run completion 75% -> 60% (3x the -5pt dip budget);
            // restore 0 (count-4) -> m6 100% (a formality again — the kit retinues are support
            // pieces and the faction rosters run softer than the mixed m6 cascade). Restore +1
            // (count-3) is the measured middle: m6 ~85-88%, completion ~70% (dip ~-5, on budget).
            // With m1-m5 untouched, the dip budget pins m6 to the TOP of the 80-88 band by
            // construction (h0 completion >= 70% requires m6 >= ~85%). An UNSTAMPED finale (the
            // Faction.None safety fallback) keeps the old count-4 exactly.
            // W6 (SIGNAL) — HEAT-GATED finale body: the restored kit body (~15-20pts of m6
            // conditional per body, W5-measured) now fields only for COORDINATED forces —
            // Ai.Tier >= 1, which the heat ladder publishes from ELITE CADRE (rung 4) up
            // (Game.SetupMission sets Ai.Tier from Heat.AiTier BEFORE Build, every mission;
            // Combat.EndMission clears it, so a stale tier can never leak in here). Low heat
            // (0-3) gets the softer count-4 finale: the fresh 06b65c2 baseline ran h0 62.5% /
            // h2 55% completion (well under the ~75-80 ladder-top goal) with the W5 finale
            // eating ~1/5 of otherwise-cleared runs; the ladder's top half keeps the
            // full-bite finale it was tuned against. Faction.None still means count-4.
            // The de-stack is a DIFFICULTY subtraction and reads the ladder's real request under
            // LEVER A. Measured finale headcount at heats 0-8 goes 6/7/7/8/9/9/9/9/9 ->
            // 6/7/7/8/9/10/10/10/11: rungs 5 and 8 land the body they declare, and the apex's
            // tooth reaches the mission that decides the campaign.
            count = Floor(count - (Combat.MissionFaction != Faction.None && Ai.Tier >= 1 ? 3 : 4), 5);
            // P23 LEVER B — THE STAT STRIP, SEPARATED. The line below used to read
            // `bump = Math.Max(0, n - 1)` with the comment "drop the boss-card/heat StatDelta for
            // the screen", merging two strips that are not the same decision:
            //   * the deployment CARD's StatDelta is genuinely presentational — the WARLORD IS the
            //     elite, and an ELITE-shaped card on top would double-count it (that is this
            //     block's founding argument, and it stands). The card's contribution is still
            //     dropped, in BOTH modes. (Today NodeKind.Boss ships StatDelta 0 anyway, so the
            //     strip's only live victim was heat's — see DEVLOG §P23.)
            //   * the adaptive assist's relief is likewise still dropped (unchanged).
            //   * HEAT's StatDelta is not presentational. It is the rung the player dialled, it is
            //     what `Heat.Mods` publishes, and discarding it made rungs 2/6/7/8 statless on
            //     mission 6 — half of L7's located defect.
            // `heatStat` is heat's OWN contribution, already m1-2-graced by Game.SetupMission and
            // arriving on its own parameter precisely so this line can separate it from the sum in
            // `statDelta`. Every other caller passes 0, so nothing but the campaign finale moves.
            bump = FinaleHeatStat
                 ? Math.Max(0, (pace - 1) + heatStat)           // drop the card's/assist's stat, KEEP heat's
                 : Math.Max(0, pace - 1);                       // pre-P23: drop the boss-card AND heat StatDelta
        }
        var rows = new List<int>();
        for (int y = 0; y < grid.H; y++) rows.Add(y);
        // shuffle rows
        for (int i = rows.Count - 1; i > 0; i--) { int j = Util.RandInt(0, i); (rows[i], rows[j]) = (rows[j], rows[i]); }

        var used = new HashSet<(int, int)>();
        LastRelocations = 0; LastShiftedRelocations = 0;           // P57 telemetry, per Build
        bool siegeSpawned = false;    // hard cap: at most ONE SIEGE/BOMBARD artillery per mission (fairness)
        bool bannerSpawned = false;   // W8 review: at most ONE WARBRINGER banner per mission — overlapping
                                      // auras could blanket an arena and switch the rout lever off entirely
        // FUL-6 CRITICAL MASS — pods of 3 for the mid/late campaign (missions 3+), via the pure
        // PodPlan split, so morale gets its full waver->rout arc (kill 1 of 3 -> WAVERING; kill
        // 2 -> the survivor routs) and one real multi-pod battle replaces six 2-enemy executions.
        // m1-2 keep i/2 pairs (the teaching tier's gentle first contact) and the FINALE keeps
        // i/2 EXACTLY — FUL-11's kit geometry (SIGNIFER at i==1 -> the boss's pod 0) is verified
        // against it, so FUL11PROBE stays green by construction. The m3/m5 mid-boss (i==0) joins
        // a pod of 3: its 2-body screen can rout out from under it — accepted (mid-bosses already
        // win at high rates), named a watch item. The plan feeds BOTH the PodId stamp and the
        // column-offset read below so a pod shares a column band. COHESION ride-along: members
        // 2-3 anchor to their pod's first member's POST-relocate row (anchor+1/anchor+2, flipped
        // downward at the board edge so rows stay distinct) instead of independent shuffled rows,
        // so pods land as visible clumps — the linked-activation geometry, the grenade stage, and
        // the POD x/y read all depend on this. ZERO extra RNG draws: rows[] reads are not draws,
        // and the collision-relocate loop stays the only conditional draw source, exactly as today.
        bool podsOf3 = rosterTier >= 3 && n < Run.MaxMissions;
        // FUL-6 ESCALATION LEVER 1 (measured breach): the full pod stack ran the h0 paired
        // flywheel at -12.5 pts completion vs the fresh same-slot R0 (chunk a -5, chunk b -20;
        // budget <= 8). The spec's first lever: trim the initial force by 1 on 3-pod missions
        // (the FUL-4 defend-trim precedent) — each contact is bigger now (3 guns wake at once,
        // a link can make it 6), so the unchanged body count priced a harder mission than the
        // budget allows. m1-2 and the finale are untouched (no pod stack there); floored at 3
        // like the sabotage/defend trims above.
        if (podsOf3) count = Floor(count - 1, 3);
        // P23 LEVER A — the seating ceiling, applied to the force that is actually seated. This is
        // the LAST thing that touches `count`; nothing below it may subtract again without moving
        // this line down with it.
        if (ClampLast) count = Math.Clamp(count, 3, ForceCeiling);
        // P23 telemetry (SIGHTLINE_FORCETEST): the numbers the board is about to be built with,
        // after EVERY trim and the ceiling. Three int stores; read by no live path.
        LastForceRequest = request; LastForceCount = count; LastStatBump = bump; LastForceFloored = floored;
        int[] podOf = null, memberOf = null;
        int[] podAnchor = null, podAnchorX = null;
        // P14 — A MODE FORCE IS NEVER ENTIRELY IMMOBILE. Measured on the pre-P14 tree
        // (SIGHTLINE_MODEFORCEPROBE, 40 builds/rung): a heat-0 skirmish was ONE pod 40/40 times and
        // ONE archetype 38/40, so 3/40 heat-0 fights — 7.5% — were three immobile SENTRYs and
        // nothing else, a whole mission against turrets that cannot follow you. Restoring the
        // opener body (above) breaks the single pod, but two pods can still BOTH roll SENTRY
        // (measured 1/40 after that change alone), so the guard stands on its own: in SKIRMISH and
        // DAILY the FIRST body — pod 0's lead, whose roll PodUniform then carries to its pod — may
        // not be immobile. Gated on ModeDepth, so the campaign (whose m3+ Defend/Sabotage forces
        // also plan as one pod of 3) is byte-identical.
        int[] plan = podsOf3 ? PodPlan(count) : null;
        // W4 POD UNIFORMITY: the pod lead's archetype roll, reused by its members. Sized for the
        // i/2 pairing too (m1-2), so the teaching tier's pairs field one kind of body as well;
        // the FINALE is excluded (its kit slots are explicit and FUL11PROBE pins their geometry).
        float[] podRoll = new float[count / 2 + 2];
        if (podsOf3)
        {
            podOf = new int[count]; memberOf = new int[count];
            podAnchor = new int[plan.Length]; podAnchorX = new int[plan.Length];
            for (int p = 0, idx = 0; p < plan.Length; p++)
                for (int m = 0; m < plan[p] && idx < count; m++, idx++) { podOf[idx] = p; memberOf[idx] = m; }
        }
        for (int i = 0; i < count; i++)
        {
            int podId = podsOf3 ? podOf[i] : i / 2;
            int member = podsOf3 ? memberOf[i] : 0;
            // the member index WITHIN the pod for uniformity purposes: the FUL-6 plan on m3-5,
            // and the i/2 pairing everywhere else (`member` itself must stay 0 off the plan —
            // the COHESION row stack below is keyed on it).
            int podMember = podsOf3 ? member : i % 2;
            // W4 — the pod's LEAD bearing comes from the deployment shape (FRONTAL reproduces the
            // historical `grid.W - 1 - colOff` column exactly); followers stack off the lead's
            // FINAL tile along the shape's own stacking axis. rows[] reads are not RNG draws, so
            // the shared stream is untouched; the collision-relocate loop below stays the only
            // conditional draw source, exactly as before.
            var lead = shape == DeployEnvelop
                ? EnvelopAnchor(podId, rows[i % rows.Count], grid.W, grid.H)   // 0d+1 — identity at 18x11
                : PodAnchor(shape, podId, rows[i % rows.Count], grid.W, grid.H);
            int depthShift = DepthShift(shape, podId, grid.W);         // P57 — 0 at 18x11
            lead = (Math.Max(0, lead.x - depthShift), lead.y);
            int x, y;
            // ══ P50 — THE GARRISON: THE 'A' GLYPH FINALLY DOES SOMETHING ════════════════════
            // P26 shipped the anchor glyph, its parser, its cardinality rule AND its restore flag
            // — and NOTHING that reads `plan.Anchors`. `Mission.ArenaAnchors` had no consumer for
            // three programs: a dial on a feature that did not exist, which is exactly the
            // "a flag on a change nobody can measure is decoration" case CLAUDE.md warns about.
            //
            // An anchored pod takes its LEAD's tile from the arena instead of from the deployment
            // shape, and its members fill outward from that tile on a fixed ring. The ring walk is
            // deterministic and draws NO RNG, so a board with no anchors is byte-identical and the
            // collision-relocate loop below (the only conditional draw source in this method)
            // still never fires for an anchored body, because the ring only ever returns a free
            // tile. P49's whole finding is that a room with the prize in it and nobody home
            // removes the fight; this is the thing that puts somebody home.
            bool anchored = garrison != null && podId < garrison.Length;
            if (anchored)
            {
                var a0 = garrison[podId];
                x = a0.x; y = a0.y;
                if (podMember != 0 || (podsOf3 && member != 0))
                    foreach (var (rx, ry) in GarrisonRing)
                    {
                        int nx = a0.x + rx, ny = a0.y + ry;
                        if (!grid.InBounds(nx, ny)) continue;
                        if (used.Contains((nx, ny)) || evac.Contains((nx, ny))) continue;
                        x = nx; y = ny; break;
                    }
            }
            else if (podsOf3 && member > 0 && shape == DeployEnvelop)
            {
                // ENVELOP's rim pods stack ALONG their own edge (a north-rim pod marching straight
                // down into the squad's lap would un-surround the opening), off the lead's FINAL
                // tile so a relocated lead keeps its formation.
                var (sdx, sdy) = PodStack(shape, podId);
                int ax = podAnchorX[podId], ay = podAnchor[podId];
                x = ax + sdx * member; y = ay + sdy * member;
                if (!grid.InBounds(x, y)) { x = ax - sdx * member; y = ay - sdy * member; }
                if (!grid.InBounds(x, y)) { x = ax; y = ay; }        // degenerate: the relocate loop deals
            }
            else if (podsOf3 && member > 0)
            {
                // the historical COHESION stack, unchanged: the pod's own column, rows off the
                // lead's final row, flipped upward at the board edge.
                int a = podAnchor[podId];
                x = lead.x;
                y = a + member < grid.H ? a + member : a - member;
            }
            else { x = lead.x; y = lead.y; }
            x = Math.Clamp(x, 0, grid.W - 1); y = Math.Clamp(y, 0, grid.H - 1);
            int guard = 0;
            while ((used.Contains((x, y)) || evac.Contains((x, y))) && guard++ < 30)
            {
                y = Util.RandInt(0, grid.H - 1); x = Math.Max(0, grid.W - 2 - depthShift - Util.RandInt(0, 2));
                LastRelocations++; if (depthShift > 0) LastShiftedRelocations++;   // P57 telemetry: int stores, read by no live path
            }
            // P57: the relocate lands at the pod's OWN depth. Unshifted, a collision threw a
            // mid-board body back to the far edge and quietly undid the spread. Same two draws,
            // in the same order; at 18x11 depthShift is 0 and the expression is the original.
            used.Add((x, y));
            if (podsOf3 && member == 0) { podAnchor[podId] = y; podAnchorX[podId] = x; }   // the pod lead's final tile

            bool finalMission = n >= Run.MaxMissions;
            bool midBoss = !finalMission && i == 0 && MidBossFor(n, eliteNode, finalApproach, midBossSlot);   // named elite
            bool finalBody = n >= Run.MaxMissions;
            float r = Util.RandF();
            // W4 THE SECOND AXIS — POD UNIFORMITY. The wave's instrumentation says an armed
            // soldier already SEES ~2.4 foes but almost never has two shots worth choosing
            // between: CountMeaningfulChoices only counts a rival target whose ShotValue is
            // within 12% of the best, and three independently-rolled archetypes have wildly
            // different HP, guns and PriorityWeight, so the shots are never comparable. A pod
            // that fields ONE kind of body presents genuinely interchangeable targets — the
            // "which one do I shoot?" call the metric is trying to detect — at no change in
            // force strength or draw count (the per-body roll still happens; members past the
            // lead just reuse the lead's). It also reads better: "three RAIDERS", not a trio of
            // strangers. The one-BOMBARD / one-WARBRINGER caps below still demote any extra.
            if (PodUniform && !finalBody && podId < podRoll.Length)
            {
                if (podMember == 0) podRoll[podId] = r;
                else r = podRoll[podId];
            }
            // SIGNAL W5 — BOSS IDENTITY: the finale boss (i==0) + its explicit kit retinue
            // (i==1/2 on Legion/Syndicate finales) and the m3/m5 mid-boss are all keyed off
            // Combat.MissionFaction (see MakeFinaleBoss/MakeFinaleRetinue/MakeMidBoss below),
            // so each faction's climax forces a DIFFERENT verb. Faction.None falls back to
            // today's plain WARLORD / mission-keyed mid-boss (the safety invariant). The RandF
            // draw above stays unconditional so the RNG stream is unchanged for every slot.
            Unit e = null;
            if (finalMission)
                e = i == 0 ? MakeFinaleBoss(pace, x, y) : MakeFinaleRetinue(i, pace, bump, x, y);
            if (e == null)
                e = midBoss ? MakeMidBoss(rosterTier, x, y)
                            : SelectArchetype(rosterTier, r, bump, x, y);   // tier-appropriate rank-and-file
            // P14 — the MODE force's first body is never immobile. Draw-free: the roll is REMAPPED
            // (r +/- 0.5), not re-drawn, so the shared Util.Rng stream — and therefore every CRN
            // pairing and the daily's cross-process reproducibility — is untouched. Only pod 0's
            // LEAD is remapped; PodUniform then carries the mobile pick to the rest of its pod.
            if (ModeDepth > 0 && i == 0 && !finalMission && !midBoss && e.Mobility == 0)
            {
                float r2 = r >= 0.5f ? r - 0.5f : r + 0.5f;
                var alt = SelectArchetype(rosterTier, r2, bump, x, y);
                if (alt.Mobility > 0)
                {
                    e = alt; r = r2;
                    if (PodUniform && podId < podRoll.Length) podRoll[podId] = r2;
                }
            }
            // FAIRNESS CAP: at most one SIEGE/BOMBARD per mission. SelectArchetype is stateless, so a
            // second roll could yield another -> demote any extra BOMBARD to a plain GRUNT here.
            // SIGNAL W5: this cap DELIBERATELY keys on Cls (not HasSiege) — a siege-armed BOSS elite
            // (WARDEN mid-boss / SIEGELORD finale) is EXEMPT: it never sets siegeSpawned and never
            // demotes the force's one real BOMBARD (the Legion finale retinue fields both by design).
            if (e.Cls == "BOMBARD")
            {
                if (siegeSpawned) e = MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);
                else siegeSpawned = true;
            }
            // W8 review — same BOMBARD-style cap for the WARBRINGER: one banner per mission. Keys
            // on Cls like the siege cap, so a hypothetical banner-flagged boss would stay exempt.
            if (e.Cls == "WARBRINGER")
            {
                if (bannerSpawned) e = MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);
                else bannerSpawned = true;
            }
            // Aim clamp raised 82 -> 88: the old 82 cap silently ATE the top-rung Heat StatDelta (+aim)
            // for any archetype whose base + bump + Heat exceeded 82, flattening the ladder's apex. 88
            // lets high-Heat aim bonuses land (the ladder stays meaningful at the top) while still
            // leaving the squad some miss chance. Low Heat is unaffected (its small StatDelta keeps
            // non-elite aim well under 88, so this is a no-op there).
            if (e.Cls != "ELITE") e.Aim = Math.Min(88, e.Aim);
            // grenades: bruisers + the elite always; some others from mission 2 on.
            // MORTAR already carries a deep frag pouch (set in SelectArchetype) — never overwrite it.
            // ELITE grenades: mid-bosses (BREAKER/WARDEN on m3/m5, which already win at high
            // rates) keep 2; the final WARLORD gets 1 -- two frags from the boss was a big part
            // of the m6 wall (it could AoE the whole squad before they closed). midBoss==true
            // only for the m3/m5 named elites; the final boss is finalMission && i==0.
            if (e.Cls == "ELITE") e.Grenades = midBoss ? 2 : 1;
            else if (e.Cls == "MORTAR") { /* keep MORTAR's 2-3 grenades from SelectArchetype */ }
            else if (rosterTier >= 2 && e.Cls != "MEDIC" && e.Cls != "SAPPER" && e.Cls != "BOMBARD" && (e.Cls == "BRUISER" || Util.Roll(22))) e.Grenades = 1;
            // utility items (S2-B): snipers/scouts carry smoke to cover their movement;
            // some grunts get smoke from mission 3+. Flash given to berserkers (mission 3+)
            // to disorient the squad before charging. Never given to ELITE/MEDIC/TURRET/
            // DRONE/SHIELD/SAPPER (they each have a dedicated role already).
            if (e.Cls == "SNIPER" || e.Cls == "SCOUT")
                { e.EnemyItem = ItemKind.Smoke; e.ItemCharge = 1; }
            else if (rosterTier >= 3 && e.Cls == "BERSERKER")
                { e.EnemyItem = ItemKind.Flash; e.ItemCharge = 1; }
            else if (rosterTier >= 3 && e.Cls == "GRUNT" && Util.Roll(18))
                { e.EnemyItem = ItemKind.Smoke; e.ItemCharge = 1; }
            // W6c — NO QUARTER bites: the rung-8 Heat row's +1 enemy damage, applied to the
            // per-unit Weapon instance (Weapon.Make returns a FRESH Weapon per unit, so this
            // never mutates a shared template; default 0 == today's spawns byte-for-byte).
            // Applied at this single chokepoint so EVERY spawned body — archetype, demoted
            // BOMBARD, mid-boss, WARLORD — carries it. Two known side effects, both deliberate:
            //  (1) +1 DmgMax WIDENS the AI finish band (Ai.Plan's `p.Hp <= e.Weapon.DmgMax`
            //      reads), so apex enemies also press kills on soldiers one HP point earlier —
            //      a coordination sharpening beyond the raw +1 per hit;
            //  (2) it leans AGAINST the BRACE comeback lever (the stagger's reduced-damage
            //      trade claws back relatively less at the apex) — watched via the heat-8
            //      flywheel; the comeback economy is the first re-tune if lead-swings collapse.
            // Scope: the INITIAL force only — pressure-clock/Defend reinforcement waves stay
            // deliberately light bodies (see MakeWaveHostile's do-not-upgrade note).
            if (dmgDelta != 0) { e.Weapon.DmgMin += dmgDelta; e.Weapon.DmgMax += dmgDelta; }
            e.Alert = AlertLevel.Unaware;  // dormant until sighted (escalates via 4.3 tiers)
            e.PodId = podId;               // FUL-6: PodPlan pods (m3+); i/2 pairs on m1-2 + the finale
            // APEX W5: composition telemetry — count the FINAL pick (post demote/clamp) at spawn
            // time, tagged faction-roster vs default-cascade (no-op unless the balance harness runs).
            Stats.RecordSpawn(e.Cls, Combat.MissionFaction != Faction.None);
            enemies.Add(e);
        }

        // FUL-11 — a retinue BANNER must actually ANCHOR the formation it ships with: rows are
        // shuffled, so slot-1's SIGNIFER could land Chebyshev 5-10 from its own pod-0 boss and the
        // kit's no-rout aura (Game.BannerRange = 4) covered nothing at spawn. Deterministic
        // relocation — PURE repositioning after every stream draw above has already happened, zero
        // RNG consumed, so the world-build draw count is byte-identical: walk Cheb rings 1..range
        // out from the boss in a fixed scan order and take the first tile that passes the spawn
        // loop's own invariants (in-bounds / unoccupied / off the evac zone; the grid is still bare
        // floor here — arenas/barrels stamp AFTER SpawnEnemies and keep unit tiles open). Scoped to
        // the EXPLICIT retinue slots (i<=2): Legion/Syndicate geometry is W5-measured and
        // banner-free, and a cascade-rolled WARBRINGER in a later pod is its own formation.
        if (n >= Run.MaxMissions && enemies.Count > 1)
        {
            var boss = enemies[0];
            for (int i = 1; i < enemies.Count && i <= 2; i++)
            {
                var ban = enemies[i];
                if (!ban.HasBanner || Util.ChebyDist(ban.X, ban.Y, boss.X, boss.Y) <= Game.BannerRange) continue;
                bool moved = false;
                for (int d = 1; d <= Game.BannerRange && !moved; d++)
                    for (int dy = -d; dy <= d && !moved; dy++)
                        for (int dx = -d; dx <= d && !moved; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != d) continue;   // ring cells only
                            int tx = boss.X + dx, ty = boss.Y + dy;
                            if (!grid.InBounds(tx, ty) || used.Contains((tx, ty)) || evac.Contains((tx, ty))) continue;
                            used.Remove((ban.X, ban.Y));
                            ban.X = tx; ban.Y = ty;
                            used.Add((tx, ty));
                            moved = true;
                        }
            }
        }
    }

    /// Pick a rank-and-file hostile archetype for mission tier `n` from a uniform roll `r` in
    /// [0,1). Each mission TIER owns an explicit, non-overlapping set of probability windows so
    /// the composition reads deliberately — no archetype dominates a tier as an accidental
    /// fall-through (the old single cascade leaked the whole 0.49-0.85 band onto the BRUISER on
    /// mission 2, because every n>=3 branch was skipped). Archetype STATS are unchanged; only the
    /// gating/order moved. First-appearance tiers are preserved:
    ///   - Mission 1  (basic force):   SCOUT / GRUNT only.
    ///   - Mission 2  (light skirmish): SNIPER, DRONE, HUNTER, BRUISER, SCOUT, GRUNT — balanced,
    ///                                  none over ~20% (fixes the m2 BRUISER-dominance leak).
    ///   - Missions 3+ (full roster):  the complete pool incl. TURRET/BERSERKER/SHIELD/SAPPER/
    ///                                  MORTAR/MEDIC, weighted toward variety.
    /// Boss / mid-boss slots (i == 0 on the final / m3 / m5) are handled by the caller, not here.
    static Unit SelectArchetype(int n, float r, int bump, int x, int y)
    {
        // ENEMY FACTIONS (Phase 4 foundation): when a faction is active, a mission's rank-and-file
        // is drawn from THAT faction's roster instead of the default tier cascade, so the force reads
        // as one named opponent. Boss / mid-boss slots are handled by the caller (SpawnEnemies), not
        // here, so the campaign's named elites are untouched. Game.SetupMission wires the campaign
        // node's stamp into Combat.BeginMission (Run.GenerateMap stamps EVERY Combat/Elite node), so
        // this branch runs on the majority of campaign fights; None (Start/Supply/Boss nodes,
        // skirmish/endless, and the harness default) falls through to the unchanged cascade below —
        // the SAFETY INVARIANT: spawns are byte-identical to the unstamped build while no faction is set.
        if (Combat.MissionFaction != Faction.None)
            return FactionRoster(Combat.MissionFaction, n, r, bump, x, y);

        if (n <= 1)   // MISSION 1 — basic force: a scout screen + grunts, nothing special.
            return r < 0.60f
                ? MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y)
                : MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);

        if (n == 2)   // MISSION 2 — light skirmishers (each ~11-14%; deliberately no dominant type).
        {
            if (r < 0.13f) return MakeHostile("VIPER", "SNIPER", WeaponKind.Sniper, 4 + bump, 62 + bump, 5, x, y); // 13% marksman
            if (r < 0.26f) return MakeHostile("WASP", "DRONE", WeaponKind.Smg, 3 + bump, 60 + bump, 7, x, y);      // 13% drone
            if (r < 0.38f) return MakeHostile("JACKAL", "HUNTER", WeaponKind.Smg, 5 + bump, 60 + bump, 9, x, y);   // 12% flanker
            // STRIKER (WRAITH): a fast, fragile LEAPER — see Ai.Plan. It rushes THROUGH player overwatch
            // (discounts the kill-zone like a berserker) to end on your soldier's flanked/soft side.
            // Appears from m2 as the light-skirmish flank threat. Counter: don't camp overwatch alone —
            // body-block or focus it (it's glass). Spawns here at ~11%.
            if (r < 0.49f) return MakeHostile("WRAITH", "STRIKER", WeaponKind.Smg, 4 + bump, 60 + bump, 9, x, y);  // 11% leaper
            if (r < 0.62f) return MakeHostile("HOPLITE", "LANCER", WeaponKind.Rifle, 6 + bump, 58 + bump, 5, x, y);// 13% formation trooper
            if (r < 0.74f) return MakeHostile("FERAL", "HOUND", WeaponKind.Smg, 3 + bump, 56 + bump, 9, x, y);     // 12% swarmer
            if (r < 0.86f) return MakeHostile("OGRE", "BRUISER", WeaponKind.Lmg, 9 + bump, 56 + bump, 5, x, y);// 12% bruiser
            return MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);                  // 14% scout
        }

        // MISSIONS 3+ — the full roster is available. Windows tuned for variety: every archetype
        // appears, with the specialists (TURRET/BERSERKER/SHIELD/SAPPER/MORTAR/MEDIC/SPOTTER)
        // collectively the bulk and the plain SCOUT/GRUNT now a small remainder (they carried too
        // much before). SPOTTER is a force-multiplier (see Ai.Plan): low priority body count but
        // high priority to KILL, so it's deliberately a single ~7% slot, not a swarm.
        if (r < 0.07f) return MakeHostile("SENTRY", "TURRET", WeaponKind.Lmg, 6 + bump, 66 + bump, 0, x, y);       //  7% immobile nest
        if (r < 0.15f) return MakeHostile("VIPER", "SNIPER", WeaponKind.Sniper, 4 + bump, 62 + bump, 5, x, y);     //  8% marksman
        if (r < 0.22f) return MakeHostile("REAVER", "BERSERKER", WeaponKind.Shotgun, 12 + bump, 58 + bump, 8, x, y); // 7% rusher
        if (r < 0.29f) return MakeHostile("WASP", "DRONE", WeaponKind.Smg, 3 + bump, 60 + bump, 7, x, y);          //  7% drone
        if (r < 0.36f) return MakeHostile("JACKAL", "HUNTER", WeaponKind.Smg, 5 + bump, 60 + bump, 9, x, y);       //  7% flanker
        // STRIKER (WRAITH): a fast, fragile LEAPER — see Ai.Plan. Highest-tier flank threat: it rushes
        // THROUGH player overwatch (it discounts the kill-zone like a berserker) and seeks to END on the
        // soldier's flanked/soft side even harder than the HUNTER. Counter: overwatch-camping does NOT
        // stop it — body-block the flank or focus it down (it's glass). ~7% slot.
        if (r < 0.43f) return MakeHostile("WRAITH", "STRIKER", WeaponKind.Smg, 4 + bump, 60 + bump, 9, x, y);      //  7% leaper
        // LANCER (HOPLITE): a formation trooper — see Ai.Plan. It is sturdier in a line (the AI rewards
        // ending adjacent to another hostile, so a pod forms a wall and presses forward in lockstep),
        // which makes it a tempting GRENADE / AoE target. Counter by breaking the formation up.
        if (r < 0.50f) return MakeHostile("HOPLITE", "LANCER", WeaponKind.Rifle, 7 + bump, 58 + bump, 5, x, y);    //  7% formation trooper
        // HOUND (FERAL): a fast, low-HP swarmer that hunts the ISOLATED soldier (see Ai.Plan: very high
        // advance weight + targets the squad member with the FEWEST nearby allies, beelining to it).
        // They spawn in pairs (the caller pods them ~2 each). Counter by staying massed / overwatching.
        if (r < 0.57f) return MakeHostile("FERAL", "HOUND", WeaponKind.Smg, 3 + bump, 56 + bump, 9, x, y);         //  7% swarmer
        if (r < 0.63f)                                                                                              //  6% shield
        {
            var s = MakeHostile("AEGIS", "SHIELD", WeaponKind.Rifle, 10 + bump * 2, 56 + bump, 4, x, y);
            s.ShieldDx = -1; s.ShieldDy = 0;            // shield faces the squad (west)
            return s;
        }
        if (r < 0.69f) return MakeHostile("BREACH", "SAPPER", WeaponKind.Shotgun, 7 + bump, 56 + bump, 6, x, y);   //  6% demolition
        if (r < 0.74f)                                                                                              //  5% grenadier
        {
            var m = MakeHostile("MORTAR", "MORTAR", WeaponKind.Smg, 6 + bump, 50 + bump, 5, x, y);
            m.Grenades = n >= 5 ? 3 : 2;                // a deep frag pouch — the EXISTING grenade AI uses it
            return m;
        }
        if (r < 0.79f) return MakeHostile("ORDERLY", "MEDIC", WeaponKind.Smg, 6 + bump, 52 + bump, 6, x, y);       //  5% medic
        // SPOTTER (BEACON): a fragile back-line designator. It barely fights (poor SMG, low HP) but
        // while it lives it "paints" the squad's priority target — Ai.Plan amplifies focus-fire
        // convergence for ALL allies (see Ai.SpotterActive). Kill it first to break the crossfire.
        if (r < 0.84f) return MakeHostile("BEACON", "SPOTTER", WeaponKind.Smg, 5 + bump, 48 + bump, 6, x, y);      //  5% designator
        // SCREENER (HAZE): a fragile back-line AREA-DENIAL zoner. It barely fights — its action is a
        // PROACTIVE SMOKE dropped ON your firing lane (see Ai.Plan/BestScreen), blinding your soldiers'
        // sightlines and FORCING you to reposition to re-acquire targets. Reuses the enemy smoke exec.
        // Counter: push through / around the cloud, or kill it before it screens. Carries the smoke
        // charge (set in SpawnEnemies). ~6% slot.
        if (r < 0.87f)                                                                                              //  3% zoner (FUL-8: was 4 — carved for PIKEMAN)
        {
            var z = MakeHostile("HAZE", "SCREENER", WeaponKind.Smg, 5 + bump, 46 + bump, 6, x, y);
            z.EnemyItem = ItemKind.Smoke; z.ItemCharge = 2;   // a deep smoke pouch — the EXISTING smoke AI uses it
            return z;
        }
        // PIKEMAN (SARISSA, FUL-8): the lane-holder — plants a braced stagger cone over a movement
        // lane (the enemy-side mirror of the player's own BRACE; see Ai.Plan). Wardens-native at 10%;
        // this is its ~3% cascade tail so the mixed default force can field one too. Carved from the
        // SCREENER/BOMBARD/WARBRINGER mid-tail — the 1% GRUNT/SCOUT/BRUISER tails and every other
        // archetype's first-appearance tier are unchanged.
        if (r < 0.90f) return MakeHostile("SARISSA", "PIKEMAN", WeaponKind.Smg, 7 + bump, 58 + bump, 5, x, y);      //  3% lane-holder
        // SIEGE (BOMBARD): a fragile back-line artillery piece. It does NOT fire — it CHARGES a
        // telegraphed 3x3 strike (shown for a full player turn) that lands cover-ignoring next enemy
        // turn (see Ai.Plan/Game.TickSiegeStrikes). Forces RELOCATION (a non-shoot tactical axis).
        // Rare (~3%); capped at 1 per mission by the post-pick guard in SpawnEnemies.
        if (r < 0.93f) return MakeHostile("SIEGE", "BOMBARD", WeaponKind.Smg, 7 + bump, 48 + bump, 4, x, y);       //  3% artillery (FUL-8: was 4)
        // WARBRINGER (SIGNIFER, W8): the Legion standard-bearer — a mid-HP banner anchor: pods with
        // a living banner within Chebyshev Game.BannerRange cannot rout and rally a turn faster
        // (Game.BreakPodMorale / BeginEnemyUnitTurn). A priority-target decision: the comeback
        // lever (focus a pod down to break it) is CONTESTED until the banner falls. ~2% slot.
        if (r < 0.95f) return MakeHostile("SIGNIFER", "WARBRINGER", WeaponKind.Rifle, 8 + bump, 56 + bump, 5, x, y); // 2% banner anchor (FUL-8: was 3)
        // CUSTODIAN (SEXTON, W8): the objective KEEPER — a low-threat unit that walks to the
        // terminal / a blown sabotage charge and undoes ONE step of progress per adjacent turn
        // (Ai.Plan -> Game.DoRelock, banner-telegraphed). Screen it out or shoot it first. ~2% slot.
        if (r < 0.97f) return MakeHostile("SEXTON", "CUSTODIAN", WeaponKind.Smg, 5 + bump, 48 + bump, 6, x, y);     // 2% keeper
        if (r < 0.98f) return MakeHostile("OGRE", "BRUISER", WeaponKind.Lmg, 9 + bump, 56 + bump, 5, x, y);    //  1% bruiser
        if (r < 0.99f) return MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);       //  1% scout
        return MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);                     //  1% grunt
    }

    /// FACTION-GATED rank-and-file pick (Phase 4 foundation). Returns one archetype drawn from the
    /// given faction's roster by the uniform roll `r` in [0,1). STATS are copied VERBATIM from the
    /// default cascade in SelectArchetype (same name/cls/weapon/hp/aim/mob lines, incl. SHIELD facing,
    /// MORTAR's frag pouch and SCREENER's smoke pouch) — only the gating moved, so a faction force is
    /// the SAME units, just grouped. Each roster ends with a GRUNT (Wardens: SCOUT) filler so any roll
    /// resolves. APEX W5: the four setup-verb archetypes (STRIKER/LANCER/HOUND/SCREENER) were authored
    /// into the default cascade but unreachable on faction-stamped nodes (the majority of the campaign);
    /// they're folded in here — Legion += STRIKER/LANCER/HOUND, Syndicate/Wardens += SCREENER — each
    /// tier-gated at its cascade first-appearance mission (r-window && n>=N, exactly like Wardens'
    /// existing BOMBARD gate; a failed gate falls through to the next window, so any roll resolves).
    /// Only ever called when Combat.MissionFaction != None (the None default uses the unchanged cascade).
    static Unit FactionRoster(Faction f, int n, float r, int bump, int x, int y)
    {
        switch (f)
        {
            // SYNDICATE (tech/mechanized) — DRONE, SHIELD, TURRET, SAPPER, SPOTTER, SCREENER m3+
            // (+ GRUNT filler). The SCREENER's proactive lane-smoke pairs with the SPOTTER's
            // focus-paint: the tech faction fights your INFORMATION, not just your HP bar.
            case Faction.Syndicate:
                if (r < 0.22f) return MakeHostile("WASP", "DRONE", WeaponKind.Smg, 3 + bump, 60 + bump, 7, x, y);
                if (r < 0.40f)
                {
                    var s = MakeHostile("AEGIS", "SHIELD", WeaponKind.Rifle, 10 + bump * 2, 56 + bump, 4, x, y);
                    s.ShieldDx = -1; s.ShieldDy = 0;            // shield faces the squad (west)
                    return s;
                }
                if (r < 0.56f) return MakeHostile("SENTRY", "TURRET", WeaponKind.Lmg, 6 + bump, 66 + bump, 0, x, y);
                if (r < 0.72f) return MakeHostile("BREACH", "SAPPER", WeaponKind.Shotgun, 7 + bump, 56 + bump, 6, x, y);
                if (r < 0.82f) return MakeHostile("BEACON", "SPOTTER", WeaponKind.Smg, 5 + bump, 48 + bump, 6, x, y);
                if (r < 0.92f && n >= 3)                        // 10% zoner (m3+ — cascade first appearance)
                {
                    var z = MakeHostile("HAZE", "SCREENER", WeaponKind.Smg, 5 + bump, 46 + bump, 6, x, y);
                    z.EnemyItem = ItemKind.Smoke; z.ItemCharge = 2;   // a deep smoke pouch — the EXISTING smoke AI uses it
                    return z;
                }
                // W8: the CUSTODIAN keeper suits the tech faction — it contests your PROGRESS
                // (re-locks the terminal / re-arms blown charges), like the SPOTTER/SCREENER
                // contest your information. m3+, matching the SCREENER's full-roster tier.
                if (r < 0.97f && n >= 3) return MakeHostile("SEXTON", "CUSTODIAN", WeaponKind.Smg, 5 + bump, 48 + bump, 6, x, y); // 5% keeper (m3+)
                return MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);              // filler

            // LEGION (shock assault) — BERSERKER, BRUISER, HUNTER + the m2+ skirmish tier:
            // STRIKER (overwatch-defying leaper), LANCER (formation line), HOUND (isolation
            // swarmer) — the coordination showcase, at home in the rush faction (+ SCOUT, GRUNT filler).
            case Faction.Legion:
                if (r < 0.22f) return MakeHostile("REAVER", "BERSERKER", WeaponKind.Shotgun, 12 + bump, 58 + bump, 8, x, y);
                if (r < 0.42f) return MakeHostile("OGRE", "BRUISER", WeaponKind.Lmg, 9 + bump, 56 + bump, 5, x, y);
                if (r < 0.60f) return MakeHostile("JACKAL", "HUNTER", WeaponKind.Smg, 5 + bump, 60 + bump, 9, x, y);
                if (r < 0.68f && n >= 2) return MakeHostile("WRAITH", "STRIKER", WeaponKind.Smg, 4 + bump, 60 + bump, 9, x, y);   // 8% leaper (m2+)
                if (r < 0.76f && n >= 2) return MakeHostile("HOPLITE", "LANCER", WeaponKind.Rifle, (n >= 3 ? 7 : 6) + bump, 58 + bump, 5, x, y); // 8% formation trooper (m2+; HP 6->7 at m3, like the cascade)
                if (r < 0.84f && n >= 2) return MakeHostile("FERAL", "HOUND", WeaponKind.Smg, 3 + bump, 56 + bump, 9, x, y);      // 8% swarmer (m2+)
                // W8: the WARBRINGER banner anchor is Legion-native — the shock faction's pods hold
                // the line under its standard (no rout + faster rally within Chebyshev BannerRange).
                if (r < 0.90f && n >= 3) return MakeHostile("SIGNIFER", "WARBRINGER", WeaponKind.Rifle, 8 + bump, 56 + bump, 5, x, y); // 6% banner anchor (m3+)
                if (r < 0.93f) return MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);
                return MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);              // filler

            // WARDENS (precision/control / area-denial) — SNIPER, MORTAR, SIEGE artillery, SCREENER
            // m3+, MEDIC, GRUNT (+ SCOUT filler). The standoff faction: thematically perfect for the
            // telegraphed artillery (capped at 1/mission by the SpawnEnemies post-pick guard) and the
            // lane-blinding smoke zoner (area denial in both directions).
            case Faction.Wardens:
            default:
                if (r < 0.20f) return MakeHostile("VIPER", "SNIPER", WeaponKind.Sniper, 4 + bump, 62 + bump, 5, x, y); // 20% marksman (FUL-8: was 24 — re-sliced for the PIKEMAN window)
                if (r < 0.38f)
                {
                    var m = MakeHostile("MORTAR", "MORTAR", WeaponKind.Smg, 6 + bump, 50 + bump, 5, x, y);
                    m.Grenades = n >= 5 ? 3 : 2;                // a deep frag pouch — the EXISTING grenade AI uses it
                    return m;
                }
                // The two m3+ gates route their FAILED (m2) rolls to the SCOUT filler, not the next
                // window — falling through would hand MEDIC their combined 22% and make mission-2
                // Wardens pods a 36%-medic heal-loop slog (W5 review LOW-3).
                if (r < 0.50f) return n >= 3
                    ? MakeHostile("SIEGE", "BOMBARD", WeaponKind.Smg, 7 + bump, 48 + bump, 4, x, y)   // 12% artillery (m3+ only — fairness tier)
                    : MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);
                if (r < 0.60f)                                  // 10% zoner (m3+ — cascade first appearance)
                {
                    if (n < 3) return MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);
                    var z = MakeHostile("HAZE", "SCREENER", WeaponKind.Smg, 5 + bump, 46 + bump, 6, x, y);
                    z.EnemyItem = ItemKind.Smoke; z.ItemCharge = 2;   // a deep smoke pouch — the EXISTING smoke AI uses it
                    return z;
                }
                // FUL-8: the PIKEMAN lane-holder is Wardens-native — the control faction now contests
                // MOVEMENT itself (a braced stagger cone over the squad's lane), completing the set:
                // information (SCREENER), position (SIEGE), progress (CUSTODIAN), movement (PIKEMAN).
                // 10% at m2+ — the teaching piece arrives early, like Legion's m2 STRIKER/LANCER; the
                // failed (m1) gate routes to the SCOUT filler so any roll resolves. Stats sit in the
                // W8 Wardens-support band: HP 7 survives one focused soldier-turn, dies to two; SMG
                // (MaxRange 10) keeps the cone LOCAL; Mob 5 — a holder, not a rusher.
                if (r < 0.70f) return n >= 2
                    ? MakeHostile("SARISSA", "PIKEMAN", WeaponKind.Smg, 7 + bump, 58 + bump, 5, x, y)  // 10% lane-holder (m2+)
                    : MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);
                if (r < 0.82f) return MakeHostile("ORDERLY", "MEDIC", WeaponKind.Smg, 6 + bump, 52 + bump, 6, x, y); // 12% medic (FUL-8: was 14)
                // W8: the CUSTODIAN keeper is Wardens-native — the control faction contests your
                // objective PROGRESS itself (re-locks the terminal / re-arms blown charges). m3+
                // like SIEGE/SCREENER; the failed (m2) gate routes to the GRUNT window's pick, so
                // any roll still resolves and mission-2 Wardens pods are unchanged.
                if (r < 0.88f && n >= 3) return MakeHostile("SEXTON", "CUSTODIAN", WeaponKind.Smg, 5 + bump, 48 + bump, 6, x, y); // 6% keeper (m3+; FUL-8: was 8)
                if (r < 0.94f) return MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 60 + bump, 6, x, y);
                return MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 58 + bump, 8, x, y);              // filler
        }
    }

    // APEX W5: 14 -> 40 callsigns. Rosters plus a draft pool can hold ~14 soldiers across a run,
    // so the old pool made duplicate names routine — and bond/memorial/veteran records key on the
    // name, silently merging two soldiers' histories. Kept terse/ASCII in the game's codename
    // voice; deliberately avoids every enemy archetype name, the fixed NewRunSquad names and the
    // Nicknames.Pool entries so "KRESS 'VIPER'" style overlaps can't read as two different units.
    /// C5: the longest callsign the generator can deal — derived from the pool, so a worst-case
    /// text stress can never be built from a name the game cannot produce.
    internal static string LongestCallsign
    {
        get { string best = ""; foreach (var c in Callsigns) if (c.Length > best.Length) best = c; return best; }
    }

    static readonly string[] Callsigns =
    {
        "HAWK", "ECHO", "RAVEN", "SLATE", "ONYX", "FOX", "WREN", "ASH", "CIPHER", "JINX",
        "ROOK", "DELTA", "MOTH", "QUILL", "TALON", "FLINT", "GALE", "SABLE", "PIKE", "VESPER",
        "COBALT", "DUSK", "EMBER", "GARNET", "HALO", "IBIS", "KESTREL", "LYNX", "MICA", "NOVA",
        "PRISM", "RUNE", "SPARK", "VECTOR", "WOLF", "ZEPHYR", "LARK", "FROST", "BRIAR", "CREED",
    };

    /// A fresh rookie of a random class, for backfilling the squad between missions.
    /// Includes the CORPSMAN (5th class) so casualties can pull in in-combat sustain.
    /// `taken` (optional, APEX W5): callsigns already in use (current squad / draft pool) — the
    /// name re-rolls away from them with the same bounded-guard pattern GenerateDraftPool uses
    /// for class variety, so duplicate soldier names stop silently merging bond/memorial records.
    /// With 40 callsigns and rosters <= ~14 names the re-roll never realistically exhausts; if it
    /// somehow does, the duplicate is accepted (a recruit must always be produced — never blocks).
    /// `mission` (optional, APEX W8): DEPTH-SCALED backfill — a recruit drafted mid-run arrives
    /// with (mission-1)/2 banked kills, so the casualty valve stops handing a mission-7 squad a
    /// 0-kill ROOKIE that drags the whole roster's power (the flagged sloppy-policy failure path;
    /// by construction only casualty-taking runs change). The caller runs Run.PromoteEligible on
    /// the recruit so the seeded kills rank it (SQUADDIE ~m3-4, CORPORAL + spec offer m7+ — the
    /// intended ceiling) in the SAME barracks visit. Default 1 == 0 kills: the run-opening draft
    /// pool, StartRun and Events.cs recruit grants deliberately stay unscaled.
    public static Unit MakeRecruit(HashSet<string> taken = null, int mission = 1)
    {
        string name = Util.Choice(Callsigns);
        if (taken != null)
        {
            int guard = 0;
            while (taken.Contains(name) && guard++ < 400) name = Util.Choice(Callsigns);
        }
        Unit u = Util.RandInt(0, 4) switch
        {
            0 => MakeSoldier(name, "ASSAULT", WeaponKind.Rifle, 8, 66, 7),
            1 => MakeSoldier(name, "RANGER", WeaponKind.Shotgun, 7, 66, 8),
            2 => MakeSoldier(name, "SHARPSHOOTER", WeaponKind.Sniper, 6, 68, 6),
            3 => MakeSoldier(name, "CORPSMAN", WeaponKind.Smg, 7, 62, 8),
            _ => MakeSoldier(name, "GUNNER", WeaponKind.Lmg, 10, 58, 6),
        };
        u.Kills = Math.Max(0, (Run.Pace(mission) - 1) / 2);   // C1: paced
        return u;
    }

    /// The escort asset: fragile, poor aim, carries only a panicky sidearm.
    /// Lives in the player roster for one mission and never joins the persistent squad.
    // ══ P55 — THE ASSET ANSWERS HEAT ══════════════════════════════════════════════════════════
    /// **`SIGHTLINE_VIPHEAT=0` restores the pre-P55 asset exactly** — a pure function of mission
    /// depth, with heat reaching it nowhere.
    ///
    /// P54 found this by cross-tabbing `campaigns[]` out of the existing archive: **at heat 8,
    /// 26.8% of campaign losses are the protected NPC dying; at heat 0 it is 0.38%** (four unforced
    /// rounds, 5,365 pooled h8 losses, Rescue 63% / Escort 37%). And the per-objective win rates are
    /// a CLIFF rather than a ladder — Rescue 97.9 / 96.2 / **40.0**, Escort 95.3 / 91.5 / **42.8**,
    /// the two flattest objectives in the game and then a wall. That is the signature of an asset
    /// whose survivability is constant in heat while the force around it gains ~4 bodies, ~4 stat,
    /// +1 weapon damage and AI tier 2.
    ///
    /// ⚠ **THIS FIXES ONE OF TWO DEFECTS AND MUST NOT BE WRITTEN UP AS FIXING BOTH.** The other is
    /// that 96-98% at h0/h4 is not an objective at all, and a heat term cannot touch it.
    public static bool VipHeat = true;

    /// The asset's heat surcharge, as a pair, so the self-test can assert it without rebuilding a
    /// unit. **THE DOSE IS ARGUED, NOT SEARCHED**: HP mirrors `Heat.StatDelta` — the same +1 per
    /// rung the ladder hands every hostile — and ARMOR mirrors `Heat.DmgDelta`, which exactly
    /// cancels heat's +1 weapon damage. Both are CLAMPED AT ZERO, which buys two things: RECRUIT
    /// (StatDelta −1) can never SHRINK the asset, and h0 is a provable no-op because
    /// `Heat.Active(0)` is empty. Those two rungs are therefore inertness controls for the round.
    ///
    /// It is deliberately CONSERVATIVE and under-compensates on purpose: the force gained four
    /// bodies as well as four stat points, and this answers only the stat. The claim being tested is
    /// "the asset should answer heat AT ALL", not "restore parity". X2's precedent — build the dial,
    /// measure the argued dose, escalate only if the round says to.
    ///
    /// NOTE ON THE ASSIST, because it looks like a coupling and is not: hostiles take
    /// `statDelta - Run.AssistStatRelief`, but `Run.AssistLevel` is **zero whenever heat > 0**, so
    /// the assist and this surcharge can never both be non-zero. No interaction exists to price.
    public static (int hp, int armor) VipHeatBonus(int heat)
        => VipHeat ? (Math.Max(0, Heat.StatDelta(heat)), Math.Max(0, Heat.DmgDelta(heat))) : (0, 0);

    public static Unit MakeVip(int missionNum = 1, int heat = 0)
    {
        // HP 6 -> 14 (Wave A.5) -> now scales with mission depth: balance data showed Escort still
        // gating runs (~55%, many "VIP LOST") because the fragile asset carries ZERO persistent
        // progression while the enemy force climbs every mission. A flat 14 HP that's sturdy at m2
        // is glass by m6. Scale it (14 + 2*mission: m2~18, m4~22, m6~26) so the asset stays a
        // believable survivor against the late force, while still having no cover-perks, weak aim,
        // and no frags — the squad must still screen for it.
        // P14: DepthFor, not the raw mission number. SKIRMISH/DAILY call this with 1 at every heat
        // rung, so the asset the objective is ABOUT read 16 HP / 0 armor from heat 0 to heat 8
        // while the force around it grew by four bodies and four stat points (measured, pre-fix:
        // SIGHTLINE_MODEFORCEPROBE `VIP hp=16 armor=0` on all five rungs). Campaign-inert.
        int depth = Math.Max(1, DepthFor(missionNum));
        var heatBonus = VipHeatBonus(heat);            // P55 — see VipHeatBonus for the argument
        int hp = 14 + 2 * depth + heatBonus.hp;
        var u = new Unit
        {
            Name = "VIP", Cls = "VIP", Team = Team.Player,
            Hp = hp, MaxHp = hp, Aim = 45, Mobility = 6,
            Weapon = Weapon.Make(WeaponKind.Smg), IsVip = true,
        };
        u.Ammo = u.Weapon.Clip;
        u.Grenades = 0;
        // The escort asset also gets light ARMOR that scales with mission depth (the squad's
        // bought plating doesn't help the VIP, so it carries its own): every incoming hit -armor,
        // floored at 1. Paired with the HP scaling + the reduced anti-VIP AI finish-frenzy, this
        // stops the fragile asset getting deleted in one focus-fire volley over a long escort.
        u.Armor = depth / 2 + heatBonus.armor;   // m2~1, m4~2, m6~3 (depth == the mission number
                                                 // outside the modes) + P55's heat surcharge
        return u;
    }

    static Unit MakeSoldier(string name, string cls, WeaponKind w, int hp, int aim, int mob)
    {
        var u = new Unit { Name = name, Cls = cls, Team = Team.Player, Hp = hp, MaxHp = hp, Aim = aim, Mobility = mob, Weapon = Weapon.Make(w) };
        u.Ammo = u.Weapon.Clip;
        u.Grenades = 1;
        u.AbilityCd = 0;
        // (Gunner's niche is its top HP (10) + PIN area-denial; an innate armor on top inflated the
        // overall win-rate well past redistribution-neutral, so it's intentionally NOT granted.)
        return u;
    }

    // ─── PROGRAM RESONANCE X1 "THE EXCHANGE" — HOSTILE TOUGHNESS ─────────────────────────
    /// A flat HP surcharge carried by EVERY hostile body. MakeHostile is the single funnel for
    /// hostiles (rank-and-file cascade, faction rosters, Defend/LAST STAND waves, finale retinue,
    /// mid-boss and finale boss), so this one constant is the whole lever.
    ///
    /// WHY (measured on the FUL-13 tree, h0 CRN pair-set slots 0-19, n=40 campaigns / 158 missions):
    /// a soldier's shot averaged 5.1 damage per SHOT (5.8 per hit) into an ~8 HP body, so
    /// time-to-kill was ONE hit and a fight resolved as an alpha-strike race — Eliminate 3.59
    /// turns, lead-swings 0.60/match, meaningful-choices/turn 2.33 (the project's own design doc
    /// cites a 3-5 band). Nine programs of comeback economy — pod morale/rout, the BRACE interrupt,
    /// focused overwatch, the 3-turn bleed-out with STABILIZE/revive, sixteen boons — were tuned
    /// for a fight that ended before any of them could bite. Bodies were never the constraint;
    /// one-shot lethality was.
    ///
    /// WHY FLAT, NOT A MULTIPLIER: the one-shot victims are the LIGHT bodies (DRONE/HOUND 3,
    /// SCOUT/SNIPER/STRIKER 4, GRUNT/HUNTER/SPOTTER 5). Player damage grows across a run through
    /// mods/perks while enemy HP grows through `bump` (= mission-1 + Heat.StatDelta), so a flat
    /// surcharge holds hits-to-kill near 2 at BOTH ends of the campaign, where a multiplier would
    /// leave m1 one-shot and turn the m6 boss (14+n) into a drag. It also leaves the archetype
    /// spread intact in absolute HP.
    ///
    /// SYMMETRY: soldier durability is deliberately NOT moved with it — the lead metric the wave
    /// targets is (sum player HP - sum ACTIVE enemy HP), and scaling both pools leaves that ratio
    /// (and therefore lead-swings) exactly where it was. The squad's exposure cost of the longer
    /// fight is the measured trade; see the wave's round table in docs/DEVLOG.md.
    /// X2 TRUE NORTH II: `const` -> static field so the post-merge correction wave can pin it
    /// per measured round from `SIGHTLINE_TOUGH` (Program.cs). Default is X1's shipped 3 — a
    /// batch with no override is byte-identical to the const form.
    public static int HostileToughness = 3;

    /// X1 THE EXCHANGE, the SYMMETRY half. Points trimmed off BOTH ends of every hostile
    /// weapon's damage band (DmgMin floored at 1). MEASURED necessity, not a guess: shipping
    /// HostileToughness alone (round R1, h0 CRN slots 0-19, n=40) moved Eliminate to 5.75
    /// turns and lead-swings to 0.79 but collapsed run completion 52.5% -> 22.5% — the longer
    /// fight simply handed the enemy ~40% more shooting turns at an unchanged 6-10 HP squad.
    /// Soldiers cannot absorb the extra exposure and the roster cannot be inflated to let them
    /// (soldier HP is the OTHER side of the lead metric this wave targets — raising it would
    /// restore the pool ratio and undo the swing gain), so the give-back is taken out of the
    /// hostile's per-shot lethality instead. Net design statement: a hostile is a BODY TO BE
    /// WORN DOWN, not a glass cannon trading one-shot kills — the same trade the player now
    /// faces going the other way.
    /// X2 TRUE NORTH II: `const` -> static field, pinnable from `SIGHTLINE_TRIM`. Default 1.
    public static int HostileDamageTrim = 1;

    /// X2 TRUE NORTH II — the POST-MERGE CORRECTION lever, and the third member of the X1 pair.
    /// Flat points off EVERY hostile's aim, applied in the same single funnel as HostileToughness
    /// (aim floored at 20 so no body becomes a harmless prop). Default 0 = the pre-X2 force.
    ///
    /// WHY ACCURACY, and not bodies or durability. Fourteen waves each measured on their own base
    /// composed into a tree 20 points below its own published band. Almost none of that drift was
    /// a difficulty DECISION: Q1 stopped pod scatter stacking two bodies in one tile, FUL-9
    /// repaired route exposure, FUL-6 fielded pods of 3 with linked activation, W6 gave the mid
    /// ladder a coordination tier, W4 opened the fight on more than one bearing. Every one of them
    /// made the SAME force put MORE FIRE on the squad. The give-back therefore comes out of the
    /// same quantity — how much of that fire lands — and out of nothing the waves deliberately
    /// bought: hostile HP (X1's two-hit trade), hostile COUNT (W4's contact breadth, armed/turn
    /// 1.39 -> 1.55) and player damage (shots-per-kill) are all untouched by construction, so
    /// Eliminate's turn budget and the decision-density instruments cannot move through this knob.
    ///
    /// ── P24 "THE TOP OF THE LADDER" — SHIPPED AT 5. `SIGHTLINE_AIMTRIM=0` restores the pre-P24
    /// force exactly. X2 built this dial for exactly this job, priced it, and then spent its one
    /// lever on `OpenerTrim` instead; P24 is the round that spends it.
    ///
    /// WHY A LEVEL LEVER AT ALL. On the L6/L7/P23 tree every rung of the heat ladder sits under
    /// its FUL-13 band CENTRE — RECRUIT -1.6, h0 -10.6, h2 -5.3, h4 -6.4, h6 -10.6, h8 -5.0, a
    /// mean of -7.6 over the five heat rungs — and two of them (h0 -2.6, h6 -2.6) have crossed
    /// their floors. That is not a defect at one rung, it is an offset in the whole ladder, and
    /// **`Heat.Mods` provably cannot reach it**: `Heat.Active(0)` yields NOTHING, so h0's win rate
    /// is a pure function of the base game and no row of the heat table can move it by any amount.
    ///
    /// WHY 5, AND WHY IT IS NOT A NUMBER SOMEONE SEARCHED FOR. X2 measured the dose-response of
    /// this exact dial at h0 (`docs/measurements/x2/`, n=40 per arm, pre-W1 stream): 35.0 baseline,
    /// **42.5 at dose 5**, 50.0 at dose 10 — and rejected dose 10 on TEXTURE (Eliminate 4.80t,
    /// Escort 13.66t), not on win rate. So 5 is the largest dose the project has already accepted,
    /// and its one archived price (+7.5 at h0) is within a point of the -7.6 mean shortfall this
    /// wave is correcting. P24 measured that value once and published what it did; it did not
    /// iterate the constant against the band.
    ///
    /// WHAT IT MAY NOT DO. It must not undo P23: `ClampLast` and `FinaleHeatStat` are untouched and
    /// the finale still hears the ladder. It must not push the apex DOWN — h8 sits on its >= 5 hard
    /// floor with no headroom below — and an easing lever cannot.
    ///
    /// IT IS UNIFORM, and that is checked rather than assumed. The lowest base aim in the roster is
    /// the SCREENER's 46, so the `Math.Max(20, ...)` floor cannot bind; the rank-and-file clamps
    /// (88 in SpawnEnemies, 82 in MakeWaveHostile) sit above every campaign body's `base + bump`,
    /// so nothing downstream eats part of the trim. `SIGHTLINE_FORCETEST` leg (H) asserts exactly
    /// that on the assembled force, which is where P23 learned to look.
    public static int HostileAimTrim = 5;

    // ── P19 "THE ROSTER CONTESTS" — the SMG monoculture's range bands ────────────────────────
    /// SIGHTLINE_ROSTERID=0 restores the pre-P19 roster exactly: no archetype is assigned an SMG
    /// band, so all twelve SMG carriers fall back to Weapon.SmgStandard (the shipped curve).
    public static bool RosterIdentity = true;

    /// Which SMG band an archetype carries. PURE in `cls` — no draw, no state — so it cannot move
    /// a CRN pairing, and `SIGHTLINE_ROSTERID=0` is a total restoration rather than a re-roll.
    ///   CQB      the four bodies whose whole plan is to CLOSE: the swarmer, the leaper, the
    ///            screen-probe and the beelining drone. Ai.cs already gives all four the top
    ///            advance weights (3.0-3.6); their gun now agrees, and kiting them finally works.
    ///   STANDOFF the two that hold a standoff by design: the MORTAR (its own Ai branch settles at
    ///            grenade range) and the BOMBARD (its branch MAXIMISES distance). Charging them is
    ///            the counter-play the design already assumed and the numbers did not pay.
    /// Everything else keeps STANDARD, including every player weapon (this is only ever called
    /// from MakeHostile).
    public static int SmgProfileFor(string cls) => cls switch
    {
        "HOUND" or "STRIKER" or "SCOUT" or "DRONE" => Weapon.SmgCqb,
        "MORTAR" or "BOMBARD"                      => Weapon.SmgStandoff,
        _                                          => Weapon.SmgStandard,
    };

    static Unit MakeHostile(string name, string cls, WeaponKind w, int hp, int aim, int mob, int x, int y)
    {
        int thp = hp + HostileToughness;
        if (HostileAimTrim > 0) aim = Math.Max(20, aim - HostileAimTrim);   // P24 ships 5; AIMTRIM=0 is the identity
        var u = new Unit { Name = name, Cls = cls, Team = Team.Enemy, X = x, Y = y, Hp = thp, MaxHp = thp, Aim = aim, Mobility = mob, Weapon = Weapon.Make(w) };
        if (RosterIdentity && w == WeaponKind.Smg) u.Weapon.SmgProfile = SmgProfileFor(cls);
        u.Weapon.TrimBaseDamage(HostileDamageTrim);
        u.Ammo = u.Weapon.Clip;
        return u;
    }

    // ─── SIGNAL W5 — BOSS IDENTITY (mid-boss signatures + finale kits) ─────────────────────
    // All three named bosses used to be the IDENTICAL unit (ELITE + Lmg); runs climaxed in the
    // same fight every time. Now each faction's named elite carries a SIGNATURE mechanic via
    // the Unit capability flags (HasShieldArc / HasSiege / RagesTwice) — mechanics the engine
    // already ships for rank-and-file SHIELD/BOMBARD/the enrage — so each climax forces a
    // DIFFERENT verb (DESIGN.md §A: no two kits may play the same):
    //   LEGION   — rage/rush:   kill it FAST or its low-HP tiers snowball (burst-down verb).
    //   SYNDICATE— shield arc:  its front is a wall; FLANK or take commanding height.
    //   WARDENS  — siege clock: telegraphed 3x3 strikes force RELOCATION every turn.
    // Every boss keeps Cls=="ELITE": the nameplate, enrage trigger, aim-clamp exemption, elite
    // grenade pouch and AI temperament are ELITE identity and stay Cls-keyed. Faction.None
    // (an unstamped fight: SKIRMISH/DAILY-style paths) falls back to today's plain bosses.

    // The three SIGNATURE arms (capability flags; Cls stays "ELITE" on every armed boss):
    static Unit ArmRage(Unit b)   { b.RagesTwice = true; return b; }                       // second rage tier at <=25% + the Ai rush temperament
    static Unit ArmShield(Unit b) { b.HasShieldArc = true; b.ShieldDx = -1; b.ShieldDy = 0; return b; }  // frontal barrier arc, opens facing the squad; FaceShields re-faces it each enemy turn
    static Unit ArmSiege(Unit b)  { b.HasSiege = true; return b; }                         // telegraphed 3x3 strikes; EXEMPT from the siegeSpawned cap by construction (the cap keys on Cls=="BOMBARD"); the Ai falls through to the ELITE gun when nothing is worth shelling

    /// The m3/m5 recurring named elite, keyed by the node's faction (a faction-signature fight).
    /// Unstamped (SKIRMISH/DAILY-style paths): today's plain mission-keyed BREAKER/WARDEN exactly.
    static Unit MakeMidBoss(int n, int x, int y)
    {
        Unit Mk(string name) => MakeHostile(name, "ELITE", WeaponKind.Lmg, 14 + n * 2, 68, 6, x, y);
        return Combat.MissionFaction switch
        {
            Faction.Legion    => ArmRage(Mk("BREAKER")),    // the rush: burst it down before the frenzy
            Faction.Syndicate => ArmShield(Mk("BULWARK")),  // the wall: flank-or-elevate puzzle
            Faction.Wardens   => ArmSiege(Mk("WARDEN")),    // the clock: relocate under telegraphed fire
            // P14 — an UNSTAMPED mid-boss is a plain ELITE with no signature arm, and it used to
            // wear a FACTION's callsign anyway: a MIXED skirmish at heat >= 4 fielded a "WARDEN"
            // with `siege=False` (measured, SIGHTLINE_MODEFORCEPROBE) — the Wardens mid-boss's name
            // on a body that cannot do the one thing that name means. The unfactioned fallback gets
            // its own callsign. Presentation only: same class, stats, draws and kit as before.
            _                 => Mk("MARSHAL"),
        };
    }

    /// The capstone named boss (m6), keyed by the Boss node's stamped faction (the FINALE KIT).
    /// Wardens keeps today's WARLORD fight (the reference kit: the enrage brick — burst/focus);
    /// Legion fields a siege-armed SIEGELORD whose strikes force RELOCATION while the rush faction
    /// closes; Syndicate a shield-arced SPYMASTER that must be FLANKED behind its screen cell.
    /// Faction.None == today's WARLORD exactly.
    ///
    /// MEASURED TUNE (flywheel, h0+h2 paired slots 0-9, vs the 80-88%-conditional target):
    ///  * the spec's first-cut Legion kit (WARDEN-stat 14+2n boss + LANCER/BOMBARD retinue)
    ///    measured 37% — the every-turn boss strike, a SECOND real artillery and the Legion
    ///    close-range warp taxed the same resource (position) three times over. The shipped kit
    ///    keeps the identity (one telegraphed strike per turn to dodge) on the standard boss
    ///    statline, escorted by a LANCER pair instead of the BOMBARD.
    ///  * the SPYMASTER runs one HP step lighter (12+n): behind a re-facing shield arc + the HVT
    ///    guards + a screen cell it measured 73% at 14+n — and a spymaster is a skulker, not a brick.
    static Unit MakeFinaleBoss(int n, int x, int y)
    {
        // (On the WARLORD statline history: HP 20+2n -> 14+n, aim 72 -> 68 — mission-6 was a
        // ~90%-loss wall; every kit boss keeps 68 aim and a 1-frag pouch via the ELITE branches.)
        Unit b = Combat.MissionFaction switch
        {
            Faction.Legion    => ArmSiege(MakeHostile("SIEGELORD", "ELITE", WeaponKind.Lmg, 14 + n, 68, 6, x, y)),
            Faction.Syndicate => ArmShield(MakeHostile("SPYMASTER", "ELITE", WeaponKind.Lmg, 12 + n, 68, 6, x, y)),
            _                 => MakeHostile("WARLORD", "ELITE", WeaponKind.Lmg, 14 + n, 68, 6, x, y),
        };
        b.IsBoss = true;   // FUL-11: presentation-only key (champion ring + HVT SIGHTED banner)
        return b;
    }

    /// The finale kit's EXPLICIT retinue (slots i==1/2, right behind the boss). Legion escorts its
    /// siege-lord with a LANCER phalanx pair (measured tune — see MakeFinaleBoss: pairing the boss's
    /// strikes with a second real artillery piece sank the kit to a 37% conditional; the boss IS the
    /// kit's artillery); Syndicate screens its spymaster with a lane-blinding zoner + a leaper;
    /// Wardens (FUL-11) anchors its warlord with a SIGNIFER banner + an ORDERLY medic.
    /// None returns null — the cascade fills every slot as before (the safety invariant).
    static Unit MakeFinaleRetinue(int i, int n, int bump, int x, int y)
    {
        if (i > 2) return null;
        switch (Combat.MissionFaction)
        {
            case Faction.Legion:                       // a phalanx pair (both retinue slots)
                return MakeHostile("HOPLITE", "LANCER", WeaponKind.Rifle, 7 + bump, 58 + bump, 5, x, y);
            case Faction.Syndicate:
                if (i == 1)
                {
                    var z = MakeHostile("HAZE", "SCREENER", WeaponKind.Smg, 5 + bump, 46 + bump, 6, x, y);
                    z.EnemyItem = ItemKind.Smoke; z.ItemCharge = 2;   // a deep smoke pouch — the EXISTING smoke AI uses it
                    return z;
                }
                return MakeHostile("WRAITH", "STRIKER", WeaponKind.Smg, 4 + bump, 60 + bump, 9, x, y);
            case Faction.Wardens:
                // FUL-11 — the WARDENS kit stops being "today's fight": the enrage brick arrives
                // ANCHORED. The SIGNIFER lands in the boss's own pod (i==1 -> PodId 0), so the whole
                // formation is held against rout until the banner falls, and the ORDERLY contests the
                // burst-down verb with heals — target priority (banner -> medic -> boss) instead of a
                // plain HP race. MEDIC over the spec's CUSTODIAN option: the boss node is always
                // Decapitate, so a keeper has no terminal/charge to re-lock — a dead mechanic on the
                // one map it would ship on (the TERROR lesson: verify the mechanic can actually fire).
                // Stats verbatim from the Wardens FactionRoster/W8 lines. COST-NEUTRAL: replaces the
                // two cascade-fill slots, and MakeHostile draws zero RNG at the PICK SITE — the
                // downstream class-conditional grenade/smoke rolls can differ from the replaced
                // picks, but only INSIDE the intentionally-changed m6 (pre-m6 stream and m6-reach
                // verified identical in the FUL-11 review's pre/post A/B).
                return i == 1
                    ? MakeHostile("SIGNIFER", "WARBRINGER", WeaponKind.Rifle, 8 + bump, 56 + bump, 5, x, y)
                    : MakeHostile("ORDERLY", "MEDIC", WeaponKind.Smg, 6 + bump, 52 + bump, 6, x, y);
            default:
                return null;
        }
    }

    /// A reinforcement wave hostile, scaled by mission. Two tiers (APEX W5):
    ///   rich == false (the default; anti-turtle PRESSURE CLOCK waves): the original cheap
    ///     GRUNT/SCOUT coin flip. DELIBERATE — the clock's punishment must stay light bodies,
    ///     not roster threats, or sloppy/slow play eats BRUISER-class waves and the already-
    ///     over-band policy gap widens further. Do not upgrade this path.
    ///   rich == true (DEFEND objective waves only — flagged by Game.SpawnDefendWave): draws
    ///     from the battle-tested endless roster (MakeEndlessHostile at tier = mission) so the
    ///     one enemy-forced-tempo objective fields real variety instead of a conveyor of grunts.
    ///     Two fairness guards: a bounded re-roll away from TURRET (Mobility 0 at the spawn edge
    ///     is a dead body; bounded like GenerateDraftPool's guard<400 re-rolls — a bare skip
    ///     would under-fill waves ~1-in-5 rolls on Syndicate Defend nodes) and a demote of any
    ///     rolled BOMBARD to a plain wave grunt (waves arrive already Alert — an off-screen
    ///     artillery telegraph the player never saw spawn is unfair).
    public static Unit MakeWaveHostile(int n, int x, int y, bool rich = false, int heatStat = 0)
    {
        int bump = Math.Max(0, n - 1);
        if (rich)
        {
            var h = MakeEndlessHostile(n, x, y);
            int guard = 0;
            while (h.Cls == "TURRET" && guard++ < 400) h = MakeEndlessHostile(n, x, y);
            if (h.Cls != "TURRET" && h.Cls != "BOMBARD") return HeatWave(h, heatStat, 88);   // MakeEndlessHostile already clamps aim (88)
            // fall through: demote BOMBARD (or a pathological all-TURRET streak) to a plain wave grunt
        }
        var e = rich || Util.Roll(50)
            ? MakeHostile("RAIDER", "GRUNT", WeaponKind.Rifle, 5 + bump, 58 + bump, 6, x, y)
            : MakeHostile("STALKER", "SCOUT", WeaponKind.Smg, 4 + bump, 56 + bump, 8, x, y);
        e.Aim = Math.Min(82, e.Aim);
        return HeatWave(e, heatStat, 82);
    }

    /// FUL-13 TRUE NORTH: DEFEND waves inherit the heat ladder's force-wide stat bump (+HP/+Aim,
    /// aim re-clamped at the path's own rank-and-file cap). The initial force always took
    /// Heat.StatDelta via SpawnEnemies' statDelta; waves were heat-BLIND (bump = mission only),
    /// so the one enemy-forced-tempo objective got RELATIVELY EASIER as heat rose — measured at
    /// the FUL-13 baseline: Defend 82% h0 -> 97% h6 / 91% h8 (defend-pinned h8: 96%, n=89) while
    /// every other objective fell with heat. Deliberately card/assist-blind (waves always were);
    /// the pressure clock keeps heatStat 0 — cheap punishment bodies by design (see
    /// SpawnReinforcements' doc). Zero extra draws: CRN pairing and h0 batches are untouched
    /// (Heat.StatDelta(0) == 0 -> byte-identical at heat 0 by construction).
    static Unit HeatWave(Unit e, int heatStat, int aimCap)
    {
        if (heatStat <= 0) return e;
        e.MaxHp += heatStat; e.Hp += heatStat;
        e.Aim = Math.Min(aimCap, e.Aim + heatStat);
        return e;
    }

    /// LAST STAND (HORIZON W2): a horde hostile at difficulty `tier` (the endless wave-scale). Low
    /// tiers are light skirmishers; tier >= 3 unlocks the FULL archetype roster (snipers/shields/
    /// drones/berserkers/siege/etc.), and HP/aim rise with the tier (bump capped + aim clamped so
    /// bodies stay killable). Reuses the campaign archetype cascade so the horde has real variety,
    /// not just grunts. `tier` is Game.EndlessWaveScale(wave) (= 1 + wave/4 + softened Heat —
    /// see that method's tuning note) for LAST STAND waves, or the raw mission number for
    /// DEFEND's rich campaign waves (MakeWaveHostile), which never see the endless curve.
    public static Unit MakeEndlessHostile(int tier, int x, int y)
    {
        int n = Math.Clamp(tier, 1, 6);           // roster depth: n>=3 opens the full cascade in SelectArchetype
        int bump = Math.Min(tier, 12);            // HP/aim bump rises with the tier (capped so it stays killable)
        var e = SelectArchetype(n, Util.RandF(), bump, x, y);
        e.Aim = Math.Min(88, e.Aim);              // clamp: escalation comes from numbers + toughness, not auto-hits
        return e;
    }

    /// APEX W7 "an ending" (LAST STAND, waves past saturation): the extra ELITE injected each
    /// deep wave so stands statistically terminate. Mirrors the campaign mid-boss stat line
    /// (14 + 2*tier HP, tier-capped; fixed 68 aim — ELITEs are exempt from the rank-and-file
    /// clamp and 68 sits below it anyway; LMG). Grenades set EXPLICITLY here: the campaign's
    /// ELITE-grenade branch lives in SpawnEnemies, which the endless spawner never runs through,
    /// so without this line a rolled elite would arrive frag-less by accident.
    public static Unit MakeEndlessElite(int tier, int x, int y)
    {
        var e = MakeHostile("REAPER", "ELITE", WeaponKind.Lmg, 14 + 2 * Math.Min(tier, 10), 68, 6, x, y);
        e.Grenades = 1;
        return e;
    }

    /// Procedural-path safety net: every SOLDIER / hostile / objective tile must stay reachable
    /// from the squad over walkable terrain. If a generated structure walled one off,
    /// carve an L-shaped lane back toward the squad by clearing the blocking cover. Runs
    /// universally (authored maps are pre-verified, but the protective cover is added
    /// after that check, so this catches any edge case for both paths).
    ///
    /// R2 FIX 1: the OTHER PLAYERS are repaired too. This net floods from players[0] and used
    /// to repair only enemies / evac / terminal / sabotage — the rest of the squad was never
    /// checked. Invisible while every deployment shape seated the squad in cols 0-3 (which
    /// BuildProcedural deliberately keeps clear), but W4's ENVELOP centre seat (cols 7-10,
    /// rows 3-6) drops soldiers into the mid-field HIGH-cover band, and Grid.CostMap's
    /// no-corner-cutting rule then seals pockets around them: measured 23 stranded soldiers in
    /// 540 ENVELOP builds (~4.3%, i.e. ~20-25% of the procedural ones), one fully entombed with
    /// no legal move for the whole mission. A stranded soldier is a silent force reduction, and
    /// if it is ever downed nobody can reach it to STABILIZE, so the bleed-out is a guaranteed
    /// KIA. PlaceBarrels already treats every player tile as required (see its `required` set)
    /// — this brings the second net in line with the first. SIGHTLINE_GEOMTEST is the standing
    /// guard (it sweeps all four deployment shapes; STACKTEST's fixed 16-board sample does not).
    static readonly int[,] ElbowDirs =
    {
        { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 },
        { 1, 1 }, { 1, -1 }, { -1, 1 }, { -1, -1 }
    };

    static void EnsureConnectivity(Grid g, List<Unit> players, List<Unit> enemies,
                                   HashSet<(int, int)> evac, (int x, int y)? terminal, List<(int x, int y)> sabotage)
    {
        if (players.Count == 0) return;
        var from = players[0];

        // R2 FIX 1b — ELBOW ROOM. A soldier can be perfectly *reachable* and still have no legal
        // move on turn 1: every neighbour is either cover or a teammate, and CostMap's corner rule
        // kills the diagonals. ENVELOP packs the squad four-abreast into the mid-field cover band,
        // so this shows up on its own and it is the same defect wearing a different hat. Open ONE
        // adjacent cover tile — cardinals first, in a fixed direction order — so nobody opens the
        // mission frozen. Deterministic: zero RNG draws, and a no-op on every build that already
        // had a step (so h0 batches stay paired).
        //
        // MEASURED FIRING RATE, 1536 fresh boards across all four shapes x 8 objectives x 6
        // missions x heats {0,8}: 22 builds (1.4%), and EVERY ONE of them under ENVELOP — 15 on
        // the procedural fallback, 7 on two authored arenas (28 and 3, whose centre seats can box
        // a soldier too). It never fires under FRONTAL/PINCER/CROSSFIRE, so no pre-W4 opening has
        // its geometry touched at all.
        var occupied = new HashSet<(int, int)>();
        foreach (var u in players) occupied.Add((u.X, u.Y));
        foreach (var u in enemies) occupied.Add((u.X, u.Y));
        bool Free(int x, int y) => g.IsFloor(x, y) && !occupied.Contains((x, y));
        bool HasStep(int x, int y)
        {
            for (int i = 0; i < 8; i++)
            {
                int dx = ElbowDirs[i, 0], dy = ElbowDirs[i, 1];
                if (!Free(x + dx, y + dy)) continue;
                if (dx != 0 && dy != 0 && (!Free(x + dx, y) || !Free(x, y + dy))) continue;   // no corner cutting
                return true;
            }
            return false;
        }
        foreach (var u in players)
        {
            if (HasStep(u.X, u.Y)) continue;
            for (int i = 0; i < 4; i++)   // cardinals only — a diagonal opening can still be corner-blocked
            {
                int nx = u.X + ElbowDirs[i, 0], ny = u.Y + ElbowDirs[i, 1];
                if (!g.InBounds(nx, ny) || occupied.Contains((nx, ny))) continue;
                g.Tiles[nx, ny] = TileType.Floor; g.Barrel[nx, ny] = false;
                break;
            }
        }

        for (int attempt = 0; attempt < 8; attempt++)
        {
            var cost = g.CostMap(from.X, from.Y, (x, y) => false, out _, 9999);
            bool Stuck(int x, int y) => g.InBounds(x, y) && cost[x, y] < 0;

            var stuck = new List<(int x, int y)>();
            foreach (var p in players) if (Stuck(p.X, p.Y)) stuck.Add((p.X, p.Y));
            foreach (var e in enemies) if (Stuck(e.X, e.Y)) stuck.Add((e.X, e.Y));
            foreach (var t in evac) if (Stuck(t.Item1, t.Item2)) stuck.Add(t);
            if (terminal.HasValue && Stuck(terminal.Value.x, terminal.Value.y)) stuck.Add(terminal.Value);
            if (sabotage != null) foreach (var s in sabotage) if (Stuck(s.x, s.y)) stuck.Add(s);
            if (stuck.Count == 0) return;

            // carve toward the squad until we meet ground that was already reachable
            foreach (var (tx, ty) in stuck)
            {
                int cx = tx, cy = ty, guard = 0;
                while (g.InBounds(cx, cy) && cost[cx, cy] < 0 && guard++ < g.W + g.H)
                {
                    if (g.Tiles[cx, cy] != TileType.Floor) g.Tiles[cx, cy] = TileType.Floor;
                    g.Barrel[cx, cy] = false;   // a barrel keeps a tile non-walkable (IsFloor false) — clear it so the carve actually opens the lane
                    if (cx != from.X) cx += Math.Sign(from.X - cx);
                    else if (cy != from.Y) cy += Math.Sign(from.Y - cy);
                    else break;
                }
            }
        }
    }

    /// Scatter a SMALL number of explosive barrels (2-5, scaling gently with mission size) on
    /// open floor, biased toward the contested mid-field / enemy half so they reward a shot
    /// (a barrel where a pod scatters to cover is a free area-denial / chain kill). A barrel
    /// makes its tile non-floor (Grid.IsFloor false), so it behaves as an obstacle for ALL
    /// pathing/connectivity automatically. SAFETY: each candidate is placed only after a flood
    /// from the squad confirms every spawn / hostile / objective tile stays reachable WITH the
    /// barrel down; any barrel that would pinch a required lane is reverted immediately. (The
    /// Build-level EnsureConnectivity below is a second net, but we never rely on it carving a
    /// barrel out — barrels are non-floor and that net only clears cover, so we keep the map
    /// connected here.)
    static void PlaceBarrels(Grid g, HashSet<(int, int)> occupied, List<Unit> players,
                             List<Unit> enemies, HashSet<(int, int)> evac,
                             (int x, int y)? terminal, List<(int x, int y)> sabotage, int missionNum)
    {
        if (players.Count == 0) return;

        // gentle count scaling: m1 -> 2, growing to a cap of 5 on later missions
        int target = Math.Clamp(2 + Run.Pace(missionNum) / 2, 2, 5);   // C1: paced
        var from = players[0];

        // the set of tiles that MUST remain reachable from the squad after each placement
        var required = new List<(int x, int y)>();
        foreach (var u in players) required.Add((u.X, u.Y));
        foreach (var u in enemies) required.Add((u.X, u.Y));
        foreach (var t in evac) required.Add(t);
        if (terminal.HasValue) required.Add(terminal.Value);
        if (sabotage != null) foreach (var s in sabotage) required.Add(s);

        bool AllReachable()
        {
            var cost = g.CostMap(from.X, from.Y, (x, y) => false, out _, 9999);
            foreach (var (rx, ry) in required)
                if (!g.InBounds(rx, ry) || cost[rx, ry] < 0) return false;
            return true;
        }

        int placed = 0, guard = 0;
        while (placed < target && guard++ < 400)
        {
            // bias toward the contested mid-field / enemy half (cols 6-15), all rows.
            int x = Util.RandInt(6, 15);
            int y = Util.RandInt(0, g.H - 1);

            // only an unreserved, currently-empty FLOOR tile is a candidate (never on cover,
            // a spawn, the evac zone, the terminal ring, or a sabotage ring).
            if (occupied.Contains((x, y))) continue;
            if (!g.IsFloor(x, y)) continue;                       // cover / existing barrel / OOB
            if (g.Barrel[x, y]) continue;
            // W4: never within blast reach of a soldier's DEPLOYMENT tile — a centre-deployed
            // squad (ENVELOP) would otherwise open the mission sitting next to a live barrel.
            // A no-op for every left-to-right opening (cols 0-3 vs the cols 6-15 bias).
            bool nearSquad = false;
            foreach (var pu in players) if (Util.ChebyDist(x, y, pu.X, pu.Y) <= 2) { nearSquad = true; break; }
            if (nearSquad) continue;

            // tentatively drop the barrel, then verify connectivity; revert if it walls anything off.
            g.Barrel[x, y] = true;
            if (!AllReachable())
            {
                g.Barrel[x, y] = false;                           // would pinch a lane — skip it
                continue;
            }
            occupied.Add((x, y));                                 // commit (keeps later passes off it)
            placed++;
        }
    }

    /// W10 INTEL CACHE placement: pick a mid/far-field FLOOR tile for the optional intel pickup
    /// (Game owns the pickup state; this is pure board geometry). PlaceBarrels-style guard, but
    /// INVERTED — the cache is walkable (it blocks nothing), so the check is that the tile itself
    /// is REACHABLE from the squad spawn (CostMap >= 0), never on a unit/objective/evac tile.
    /// Returns null when no legal tile is found (a pathological board just has no cache).
    public static (int x, int y)? PlaceIntelCache(Grid g, List<Unit> players, List<Unit> enemies,
                                                  List<(int x, int y)> evac,
                                                  (int x, int y)? terminal,
                                                  List<(int x, int y)> sabotage)
    {
        Unit from = null;
        foreach (var p in players) if (p.Alive && !p.IsVip) { from = p; break; }
        if (from == null && players.Count > 0) from = players[0];
        if (from == null) return null;
        var reserved = new HashSet<(int, int)>();
        foreach (var u in players) reserved.Add((u.X, u.Y));
        // W10 review: enemy tiles are reserved too — the gold diamond must never spawn UNDER a
        // (possibly dormant) hostile, where it would read as unreachable loot / a misleading lure.
        if (enemies != null) foreach (var u in enemies) if (u.Alive) reserved.Add((u.X, u.Y));
        if (evac != null) foreach (var t in evac) reserved.Add(t);
        if (terminal.HasValue) reserved.Add(terminal.Value);
        if (sabotage != null) foreach (var s in sabotage) reserved.Add(s);
        // one reachability map answers every probe (the cache blocks nothing, so it can't change it)
        var cost = g.CostMap(from.X, from.Y, (x, y) => false, out _, 9999);
        for (int guard = 0; guard < 400; guard++)
        {
            // mid/far-field bias (cols 6-15, like the barrels): the detour must cost real steps.
            int x = Util.RandInt(6, 15);
            // FUL-3: rows 0 and H-1 sit in HUD shadow (top-bar clip / action-bar cover), so the
            // gold diamond was born half-hidden there. Clamp the draw — never re-roll — so the
            // RNG draw count stays identical and paired seeds keep building identical worlds.
            int y = Math.Clamp(Util.RandInt(0, g.H - 1), 1, g.H - 2);
            if (reserved.Contains((x, y))) continue;
            if (!g.IsFloor(x, y)) continue;          // cover / barrel / OOB can't host a pickup
            if (cost[x, y] < 0) continue;            // walled off — a cache no one can reach is a lie
            return (x, y);
        }
        return null;
    }

    static void PlaceBlock(Grid g, HashSet<(int, int)> occ, int x, int y, TileType t, int w, int h)
    {
        for (int dx = 0; dx < w; dx++)
            for (int dy = 0; dy < h; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if (!g.InBounds(nx, ny) || occ.Contains((nx, ny))) continue;
                g.Tiles[nx, ny] = t;
                occ.Add((nx, ny));
            }
    }

    /// Raise a rectangular patch of ground to high ground (walkable plateau).
    /// Skips the extraction zone and the left-edge spawn columns.
    static void RaisePlateau(Grid g, HashSet<(int, int)> evac, int x, int y, int w, int h, int level = 1)
    {
        for (int dx = 0; dx < w; dx++)
            for (int dy = 0; dy < h; dy++)
            {
                int nx = x + dx, ny = y + dy;
                if (!g.InBounds(nx, ny) || nx < 3) continue;
                if (evac.Contains((nx, ny))) continue;
                g.Height[nx, ny] = level;
            }
    }

    /// FUL-9 THE DECK: mission n's arena is draw n of a per-run no-repeat deck — a MapSeed-keyed
    /// permutation of ALL authored layouts, with the DISPLAYED biome's themed arena pulled
    /// forward at reduced weight. Replaces the mission-number-keyed 50% hint + uniform roll
    /// (the FUL-1 confound: arena coupled to mission number, 57% of authored missions on the
    /// 8 hint arenas, 6/35 unseen in 251 missions). Pure derivation, zero Util.Rng draws (the
    /// Build draw-order contract), zero persisted state (round-trips on load by construction).
    /// The connectivity guard in TryApplyLayout still validates whatever is dealt.
    static int PickLayout(int missionNum) => DeckPick(DeckSeed, missionNum);

    // ═══════════════════ P26: READING A TEMPLATE'S OWN SITES (pure) ═══════════════════
    /// What one arena template declares about where the fight happens. Every field is resolved
    /// (arena-declared OR today's literal); the Arena* booleans say WHICH, because that is what
    /// decides whether a 3x3 ring gets punched through the authored terrain.
    public struct SitePlan
    {
        public int Layout;          // template index, or -1 for procedural
        public bool Valid;          // false => the pre-P26 path entirely (literal sites, gate in Build)
        // PlanBoard consumed the arena gate's Util.Roll(80). Build MUST NOT roll again when this is
        // set, or the two would double-spend the shared stream — the defect this field exists to
        // make impossible. Note it can be true with Valid FALSE: a procedural roll, or a template
        // whose sites failed validation, both spend the gate and then fall back.
        public bool GateSpent;
        public (int x, int y)[] Spawns;
        public (int x, int y)[] Anchors;
        public List<(int x, int y)> Evac;
        public (int x, int y)? Terminal;
        public List<(int x, int y)> Sabotage;
        public (int x, int y)? Captive;
        public bool ArenaEvac, ArenaTerminal, ArenaSabotage, ArenaCaptive, ArenaSpawns;
        public bool AnyArena => ArenaEvac || ArenaTerminal || ArenaSabotage || ArenaCaptive || ArenaSpawns;
    }

    /// Scan a template for site glyphs. PURE: zero Util.Rng draws, no Grid access, no statics read.
    /// Row-major (y outer, x inner) — the scan order is load-bearing, because the LAST 'P' is the
    /// VIP seat (carrying over PlayerSpawns[Length-1]).
    public static SitePlan ReadSites(string[] tpl)
    {
        var plan = new SitePlan { Layout = -1, Evac = new List<(int, int)>(), Sabotage = new List<(int, int)>() };
        var spawns = new List<(int x, int y)>();
        var anchors = new List<(int x, int y)>();
        if (tpl == null) { plan.Spawns = spawns.ToArray(); plan.Anchors = anchors.ToArray(); return plan; }
        for (int y = 0; y < tpl.Length; y++)
            for (int x = 0; x < tpl[y].Length; x++)
                switch (tpl[y][x])
                {
                    case 'T': plan.Terminal = (x, y); plan.ArenaTerminal = true; break;
                    case 'X': plan.Sabotage.Add((x, y)); plan.ArenaSabotage = true; break;
                    case 'E': plan.Evac.Add((x, y)); plan.ArenaEvac = true; break;
                    case 'C': plan.Captive = (x, y); plan.ArenaCaptive = true; break;
                    case 'P': spawns.Add((x, y)); plan.ArenaSpawns = true; break;
                    case 'A': anchors.Add((x, y)); break;
                }
        plan.Spawns = spawns.ToArray();
        plan.Anchors = anchors.ToArray();
        return plan;
    }

    /// ═══════════════════ P26: THE INVERSION ═══════════════════
    /// Choose the arena, read its sites, and VALIDATE the resolved set — all BEFORE Game.SetupMission
    /// seats anything. That ordering is the whole milestone: because accept/reject now happens before
    /// any unit or objective is placed, a rejected template costs nothing to unwind, so the
    /// "template rejected mid-build, sites already literal" coherence problem disappears by
    /// construction rather than by careful unwinding.
    ///
    /// DRAWS: exactly ONE Util.Roll(80), skipped under ForcedLayout — the same count Build made
    /// before, moved. PickLayout/DeckPick draw zero. See the ArenaSites block comment for why the
    /// MOVE still severs every archived CRN world even though the count is unchanged.
    ///
    /// Returns Valid=false for: the restore flag off, a procedural roll, a malformed template, or a
    /// template whose own sites cannot all be reached. Every one of those means "the pre-P26 path".
    public static SitePlan PlanBoard(int missionNum, bool needEvac, bool needTerminal,
                                     bool needSabotage, bool needCaptive, int squadSize)
    {
        var none = new SitePlan { Layout = -1, Valid = false };
        // Consume NOTHING unless we are actually taking over the gate. While every shipped template
        // is glyph-free, Maps.AnySiteTemplates is false, Build rolls exactly as it always did, and
        // this whole wave is byte-identical by construction.
        if (!ArenaSites || !Maps.AnySiteTemplates) return none;

        var spent = new SitePlan { Layout = -1, Valid = false, GateSpent = true };
        int cand;
        if (ForcedLayout >= 0 && ForcedLayout < Maps.Layouts.Length) cand = ForcedLayout;
        else if (Util.Roll(80)) cand = PickLayout(missionNum);
        else return spent;                                  // procedural: literal sites, gate spent
        if (cand < 0 || cand >= Maps.Layouts.Length) return spent;

        // P49 — THE EFFECTIVE ARENA, NOT THE RAW TEMPLATE ROW.
        //
        // This read was `Maps.Layouts[cand]`, and it was correct until P47 made a template able to
        // be DOUBLE-RESOLUTION and P48 made one of them so. After that it handed the site planner
        // the LEGACY 11-row CITADEL — which declares nothing — so `plan.AnyArena` came back false
        // and the arena-owned path silently declined on the one arena that had sites to offer. It
        // was invisible in P48 (no template declared a glyph yet) and armed the moment P49 did: a
        // forced-CITADEL HACK mission built a PROCEDURAL board with a literal terminal on it, which
        // is exactly the failure P26 was written to end. The screenshot found it; nothing else
        // could have, because every assertion in the tree was about the templates rather than
        // about what `Build` stamps.
        if (!Maps.TryParse(Maps.Source(cand), out Maps.Arena arena, out _)) return spent;
        var tpl = arena.Tiles;
        if (tpl.Length != Cfg.GridH) return spent;
        for (int y = 0; y < tpl.Length; y++) if (tpl[y].Length != Cfg.GridW) return spent;
        if (!ReadSitesWellFormed(tpl, out _)) return spent;

        var plan = ReadSites(tpl);
        plan.Layout = cand;
        // A template that declares nothing is the pre-P26 board exactly — take the old path so the
        // 35 shipped glyph-free arenas are untouched until they are re-authored.
        if (!plan.AnyArena) return spent;

        // resolve PER CATEGORY: the arena's list when it declares that category, else the literal
        // Game.SetupMission would have used. Keeping the literals here (rather than in Game) is what
        // lets the validation below see the SAME tiles the build will actually protect.
        if (!plan.ArenaEvac)
        {
            plan.Evac.Clear();
            if (needEvac)
                for (int x = Cfg.GridW - 2; x < Cfg.GridW; x++)
                    for (int y = 0; y < 4; y++) plan.Evac.Add((x, y));
        }
        if (!plan.ArenaTerminal && needTerminal) plan.Terminal = (Cfg.GridW / 2 + 1, Cfg.GridH / 2);
        if (!plan.ArenaSabotage && needSabotage)
        { plan.Sabotage.Clear(); plan.Sabotage.Add((5, 3)); plan.Sabotage.Add((10, 5)); plan.Sabotage.Add((13, 7)); }
        if (!plan.ArenaCaptive && needCaptive) plan.Captive = (Cfg.GridW / 2, Cfg.GridH / 2);
        // a category the objective does not use contributes nothing to validation
        if (!needEvac)     { plan.Evac.Clear();     plan.ArenaEvac = false; }
        if (!needTerminal) { plan.Terminal = null;  plan.ArenaTerminal = false; }
        if (!needSabotage) { plan.Sabotage.Clear(); plan.ArenaSabotage = false; }
        if (!needCaptive)  { plan.Captive = null;   plan.ArenaCaptive = false; }

        // SPAWNS. ENVELOP is W4's measured deployment lever (squad centre, pods on every rim); an
        // arena's 'P' table would price the two together, so ENVELOP keeps its own table.
        bool canEnvelop = !(needEvac || needTerminal || needSabotage || needCaptive);
        int shape = DeployFor(DeckSeed, missionNum, canEnvelop);
        if (shape == DeployEnvelop) plan.ArenaSpawns = false;
        var spawns = plan.ArenaSpawns ? plan.Spawns : SpawnTableFor(shape, Cfg.GridW, Cfg.GridH);
        if (spawns == null || spawns.Length == 0) return spent;
        plan.Spawns = spawns;

        // VALIDATE on the template's own chars. `forcedOpen` is what Build will clear regardless.
        var forced = new HashSet<(int, int)>();
        foreach (var t in plan.Evac) forced.Add(t);
        foreach (var t in plan.Sabotage) forced.Add(t);
        if (plan.Terminal.HasValue) forced.Add(plan.Terminal.Value);
        if (plan.Captive.HasValue) forced.Add(plan.Captive.Value);
        foreach (var t in spawns) forced.Add(t);

        var reach = TemplateReach(tpl, spawns[0], forced);
        bool Ok(int x, int y) => x >= 0 && y >= 0 && x < Cfg.GridW && y < Cfg.GridH && reach[x, y];
        foreach (var t in spawns)        if (!Ok(t.x, t.y)) return spent;
        foreach (var t in plan.Evac)     if (!Ok(t.Item1, t.Item2)) return spent;
        foreach (var t in plan.Sabotage) if (!Ok(t.Item1, t.Item2)) return spent;
        if (plan.Terminal.HasValue && !Ok(plan.Terminal.Value.x, plan.Terminal.Value.y)) return spent;
        if (plan.Captive.HasValue && !Ok(plan.Captive.Value.x, plan.Captive.Value.y)) return spent;

        // ADJACENCY. Game.CanHack, NearestSabotageSite and TryFreeCaptive all need ChebyDist <= 1,
        // so a site walled in on all eight sides is an UNWINNABLE mission — the failure the literal
        // 3x3 force-clear was silently preventing. This is the check that replaces it.
        bool HasNeighbour((int x, int y) t)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0) && Ok(t.x + dx, t.y + dy)) return true;
            return false;
        }
        if (plan.Terminal.HasValue && !HasNeighbour(plan.Terminal.Value)) return spent;
        if (plan.Captive.HasValue && !HasNeighbour(plan.Captive.Value)) return spent;
        foreach (var sTile in plan.Sabotage) if (!HasNeighbour((sTile.x, sTile.y))) return spent;

        plan.Valid = true;
        plan.GateSpent = true;
        return plan;
    }

    /// True if a template's declared glyph COUNTS are legal. `why` names the first rule broken, so
    /// ARENASITETEST can print which template broke which rule instead of a bare FAIL.
    public static bool ReadSitesWellFormed(string[] tpl, out string why)
    {
        why = "";
        if (tpl == null) { why = "null template"; return false; }
        int nT = 0, nx = 0, ne = 0, nc = 0, np = 0, na = 0;
        for (int y = 0; y < tpl.Length; y++)
            for (int x = 0; x < tpl[y].Length; x++)
                switch (tpl[y][x])
                {
                    case 'T': nT++; break; case 'X': nx++; break; case 'E': ne++; break;
                    case 'C': nc++; break; case 'P': np++; break; case 'A': na++; break;
                }
        if (nT > 1)             { why = $"T x{nT} (max 1)"; return false; }
        if (nx != 0 && nx != 3) { why = $"X x{nx} (must be 0 or 3)"; return false; }
        if (ne != 0 && ne < 8)  { why = $"E x{ne} (must be 0 or >= 8)"; return false; }
        if (nc > 1)             { why = $"C x{nc} (max 1)"; return false; }
        if (np != 0 && np < 7)  { why = $"P x{np} (must be 0 or >= 7)"; return false; }
        if (na > EnemyPodColOffset.Length) { why = $"A x{na} (max {EnemyPodColOffset.Length})"; return false; }
        return true;
    }

    /// Draw-free 8-direction reachability flood over a TEMPLATE's own characters — the stand-in for
    /// TryApplyLayout's post-stamp Grid.CostMap guard, run BEFORE anything is seated. It must match
    /// CostMap's no-corner-cutting rule exactly (src/Grid.cs): accepting a board the real guard
    /// would reject is the one direction of error that reintroduces the mid-build revert this whole
    /// design exists to remove. `forcedOpen` are tiles Build will clear regardless of the template.
    static bool[,] TemplateReach(string[] tpl, (int x, int y) from, HashSet<(int, int)> forcedOpen)
    {
        int h = tpl.Length, w = tpl[0].Length;
        bool Walk(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return false;
            if (forcedOpen != null && forcedOpen.Contains((x, y))) return true;
            char c = tpl[y][x];
            return c != 'o' && c != '#' && c != 'B';
        }
        var seen = new bool[w, h];
        if (!Walk(from.x, from.y)) return seen;
        var q = new Queue<(int x, int y)>();
        seen[from.x, from.y] = true; q.Enqueue(from);
        while (q.Count > 0)
        {
            var (cx, cy) = q.Dequeue();
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cx + dx, ny = cy + dy;
                    if (!Walk(nx, ny) || seen[nx, ny]) continue;
                    // no corner-cutting: a diagonal needs BOTH orthogonal neighbours open
                    if (dx != 0 && dy != 0 && (!Walk(cx + dx, cy) || !Walk(cx, cy + dy))) continue;
                    seen[nx, ny] = true; q.Enqueue((nx, ny));
                }
        }
        return seen;
    }


    /// The deck derivation itself — public for SIGHTLINE_EXPOSURETEST. Deterministic in
    /// (seed, missionNum); recomputes draws 1..n each call (n<=6 in every real mode, trivially
    /// cheap) so no state needs persisting. Draws never repeat an arena until the whole deck
    /// is exhausted (only reachable past 35 missions, i.e. never in shipping modes).
    public static int DeckPick(int seed, int missionNum)
    {
        int nLay = Maps.Layouts.Length;
        // seed-keyed Fisher-Yates via avalanche hash — NOT .NET Random (nearby MapSeeds stay
        // correlated for many draws: the measured W5 finale-kit collapse), NOT Util.Rng (zero draws)
        var deck = new int[nLay];
        for (int i = 0; i < nLay; i++) deck[i] = i;
        for (int i = nLay - 1; i > 0; i--)
        {
            int j = (int)(Util.Hash3(seed, 101, i) % (uint)(i + 1));
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        var used = new bool[nLay];
        int drawn = 0, pick = deck[0];
        for (int m = 1; m <= missionNum; m++)
        {
            if (drawn == nLay) { Array.Clear(used, 0, nLay); drawn = 0; }   // >35-mission recycle guard
            // theme hint at REDUCED weight (25%): pull the DISPLAYED biome's arena forward if
            // it's still in the deck (Biome.IndexFor is the same (mission,seed) function the
            // renderer uses, so hint and room agree). A hint pull does NOT consume the deck
            // front — that card is simply dealt next mission, so nothing is starved.
            int bi = Biome.IndexFor(m, seed);
            int hint = bi >= 0 && bi < BiomeLayoutHint.Length ? BiomeLayoutHint[bi] : -1;
            if (hint >= 0 && hint < nLay && !used[hint] && Util.Hash3(seed, 211, m) % 100 < 25)
                pick = hint;
            else
                foreach (int d in deck) { if (!used[d]) { pick = d; break; } }
            used[pick] = true; drawn++;
        }
        return pick;
    }

    static void TryCover(Grid g, HashSet<(int, int)> occ, int x, int y, TileType t)
    {
        if (!g.InBounds(x, y) || occ.Contains((x, y)) || g.Tiles[x, y] != TileType.Floor) return;
        g.Tiles[x, y] = t;
        occ.Add((x, y));
    }

    // ─── PROGRAM RESONANCE T1 — the TRAINING OP ────────────────────────────────────────────
    // A fixed, scripted drill: a 2-soldier squad, a hand-authored arena (Maps.TrainingArena) and
    // four dormant hostiles on fixed seats. Nothing here draws from Util.Rng and nothing scales
    // with mission depth, so the drill's BOARD is identical every time — which is the whole point:
    // the lesson table (Game.TrainLessons) is authored against these exact tiles. (Combat still
    // rolls dice; Game.BeginTraining pins the biome so the frame is fixed too.)

    /// The two drill soldiers. Deliberately NOT Mission.NewRunSquad(): the drill must never touch
    /// (or resemble) the campaign roster, and it needs exactly the two classes its lessons name —
    /// an ASSAULT (GRAPPLE + SMOKE) and a SHARPSHOOTER (MARK + FLASH). Extra HP is the low-cost-
    /// failure dial (DESIGN.md 3.G): a fumbled drill teaches, it doesn't punish.
    public static List<Unit> TrainingSquad()
    {
        var squad = new List<Unit>();
        squad.Add(MakeSoldier("RECRUIT-A", "ASSAULT",      WeaponKind.Rifle,  12, 72, 7));
        squad.Add(MakeSoldier("RECRUIT-B", "SHARPSHOOTER", WeaponKind.Sniper, 12, 74, 6));
        return squad;
    }

    /// Stamp the drill arena + seat the fixed force. Called from Game.SetupMission right after the
    /// normal Mission.Build (same seam LAST STAND uses): Build's terrain/force is thrown away and
    /// replaced wholesale, so the drill inherits none of the campaign's rolls.
    public static void BuildTraining(Grid grid, List<Unit> players, List<Unit> enemies)
    {
        enemies.Clear();
        grid.ClearSmoke();
        grid.ClearHazards();
        for (int x = 0; x < grid.W; x++)
            for (int y = 0; y < grid.H; y++)
            {
                grid.Tiles[x, y] = TileType.Floor;
                grid.Height[x, y] = 0;
                grid.Barrel[x, y] = false;
            }

        // seat the squad FIRST so its tiles are reserved against the template stamp
        var reserved = new HashSet<(int, int)>();
        for (int i = 0; i < players.Count && i < Maps.TrainingDeploy.Length; i++)
        {
            var u = players[i];
            var sp = Maps.TrainingDeploy[i];
            u.X = sp.x; u.Y = sp.y;
            u.Ammo = u.Weapon.Clip;
            u.Grenades = 1;
            u.AbilityCd = 0;
            u.ItemCharge = u.Item != ItemKind.None ? 1 : 0;
            u.Suppress = 0; u.OnOverwatch = false; u.Hunkered = false;
            u.Recoil = System.Numerics.Vector2.Zero; u.Flash = 0;
            u.SyncPos();
            reserved.Add((u.X, u.Y));
        }
        foreach (var f in Maps.TrainingFoes) reserved.Add(f);

        var tpl = Maps.TrainingArena;
        for (int y = 0; y < grid.H && y < tpl.Length; y++)
            for (int x = 0; x < grid.W && x < tpl[y].Length; x++)
            {
                if (reserved.Contains((x, y))) continue;   // deploy + hostile seats stay open floor
                switch (tpl[y][x])
                {
                    case 'o': grid.Tiles[x, y] = TileType.LowCover; break;
                    case '#': grid.Tiles[x, y] = TileType.HighCover; break;
                    case '^': grid.Height[x, y] = 1; break;
                    case '=': grid.Height[x, y] = 2; break;
                    default: break;
                }
            }
        grid.ResetCoverHp();

        // Four hostiles on fixed seats, in two pods. Low aim + low HP is the low-cost-failure dial:
        // the drill can be lost (it is a real fight, not a diorama) but rarely is, and a loss costs
        // nothing but a restart. Pod 0 is the pair behind cover the FLANK lesson is built around;
        // pod 1 waits in the open for the GRENADE / ABILITY lessons.
        for (int i = 0; i < Maps.TrainingFoes.Length; i++)
        {
            var (fx, fy) = Maps.TrainingFoes[i];
            var e = MakeHostile("DRONE-" + (char)('A' + i), "GRUNT", WeaponKind.Rifle, 4, 45, 5, fx, fy);
            e.PodId = i / 2;
            e.Alert = AlertLevel.Unaware;   // dormant: the recruit chooses when the fight starts
            e.SyncPos();
            enemies.Add(e);
        }
    }

    /// Screenshot-only debug (SIGHTLINE_CONTENT=1): replace the hostile force with one ALERT
    /// copy of each NEW content archetype (a LANCER phalanx + a HOUND pack) plus a reference
    /// pair, all in the mid-field, so the new silhouettes/AI read clearly in a single frame.
    /// Harness-gated in Program.cs; never runs in normal play. Mirrors how other Debug* hooks
    /// stage a clean showcase. Builds two LANCERs side-by-side (the phalanx wall) and two HOUNDs
    /// (the pack), wired Alert so they're drawn as live foes.
    public static void DebugContentShowcase(Game g)
    {
        g.Enemies.Clear();
        void Add(string name, string cls, WeaponKind w, int hp, int aim, int mob, int x, int y)
        {
            var e = MakeHostile(name, cls, w, hp, aim, mob, x, y);
            e.Alert = AlertLevel.Alert; e.PodId = -1; e.SyncPos();
            g.Enemies.Add(e);
        }
        // LANCER phalanx (shoulder-to-shoulder) mid-field
        Add("HOPLITE", "LANCER", WeaponKind.Rifle, 7, 60, 5, 9, 3);
        Add("HOPLITE", "LANCER", WeaponKind.Rifle, 7, 60, 5, 9, 4);
        // HOUND pack (the swarmers) lower mid-field
        Add("FERAL", "HOUND", WeaponKind.Smg, 3, 56, 9, 10, 7);
        Add("FERAL", "HOUND", WeaponKind.Smg, 3, 56, 9, 11, 8);

        // W8 — WARBRINGER banner anchor (diamond ring + pennant + aura outline) with a held pod
        // beside it: pod 5 is staged at its waver point (3 alive of an original 4) but sits inside
        // the banner's aura, so it draws NO WAVERING tag (the banner holds it — the honest read).
        Add("SIGNIFER", "WARBRINGER", WeaponKind.Rifle, 8, 56, 5, 15, 3);
        Add("RAIDER", "GRUNT", WeaponKind.Rifle, 5, 60, 6, 14, 2); g.Enemies[^1].PodId = 5;
        Add("RAIDER", "GRUNT", WeaponKind.Rifle, 5, 60, 6, 16, 2); g.Enemies[^1].PodId = 5;
        Add("RAIDER", "GRUNT", WeaponKind.Rifle, 5, 60, 6, 14, 4); g.Enemies[^1].PodId = 5;
        g.DebugPodOrig(5, 4);

        // W8 — a WAVERING pod far from any banner (Chebyshev > BannerRange from the SIGNIFER):
        // pod 6, 3 alive of an original 4 — exactly one kill from the rout threshold, so all three
        // draw the amber WVR crack tag (the telegraph screenshot's subject).
        Add("STALKER", "SCOUT", WeaponKind.Smg, 4, 58, 8, 3, 7); g.Enemies[^1].PodId = 6;
        Add("STALKER", "SCOUT", WeaponKind.Smg, 4, 58, 8, 4, 8); g.Enemies[^1].PodId = 6;
        Add("STALKER", "SCOUT", WeaponKind.Smg, 4, 58, 8, 3, 9); g.Enemies[^1].PodId = 6;
        g.DebugPodOrig(6, 4);

        // W8 — CUSTODIAN objective keeper (padlock silhouette), lower right, clear of both pods
        Add("SEXTON", "CUSTODIAN", WeaponKind.Smg, 5, 48, 6, 15, 8);
    }
}
