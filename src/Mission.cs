using System;
using System.Collections.Generic;

namespace Sightline;

/// Builds battlefields and squads. The player squad persists across a run
/// (see Run); each mission regenerates the map + a scaled hostile force.
public static class Mission
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

    static (int x, int y)[] SpawnTableFor(int shape)
        => shape == DeployEnvelop ? PlayerSpawnsCentre : PlayerSpawns;

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
                             int dmgDelta = 0, bool defend = false, int defendKeep = 0)
    {
        enemies.Clear();
        grid.ClearSmoke();
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
        var spawnTable = SpawnTableFor(shape);

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
        SpawnEnemies(grid, enemies, missionNum, evacSet, enemyDelta, statDelta, sabotageObj, dmgDelta, defend, defendKeep, shape);

        var occupied = new HashSet<(int, int)>();
        foreach (var u in players) occupied.Add((u.X, u.Y));
        foreach (var u in enemies) occupied.Add((u.X, u.Y));
        foreach (var t in evacSet) occupied.Add(t);   // keep the extraction zone clear of cover
        if (terminal.HasValue)                         // keep the terminal + its ring open
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    occupied.Add((terminal.Value.x + dx, terminal.Value.y + dy));
        if (sabotage != null)                          // keep each sabotage site + its ring open
            foreach (var s in sabotage)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        occupied.Add((s.x + dx, s.y + dy));

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
        if (ForcedLayout >= 0 && ForcedLayout < Maps.Layouts.Length)
        {
            attempted = true;
            authored = TryApplyLayout(grid, occupied, players, enemies, evacSet, terminal, Maps.Layouts[ForcedLayout], sabotage);
            if (authored) AppliedLayout = ForcedLayout;
        }
        else if (Util.Roll(80))
        {
            attempted = true;
            int pick = PickLayout(missionNum);
            authored = TryApplyLayout(grid, occupied, players, enemies, evacSet, terminal, Maps.Layouts[pick], sabotage);
            if (authored) AppliedLayout = pick;
        }
        if (!authored)
            BuildProcedural(grid, occupied, evacSet, missionNum);
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

    // ---- Procedural map generation -----------------------------------------
    // The procedural path picks one of several mid-field cover ARCHETYPES at random so
    // generated missions don't all look the same. Every archetype preserves the 4.2
    // encounter-geometry intent: sightline-blocking HIGH cover concentrated in the
    // mid-field (cols ~6-12), at least one deliberately OPEN "risky direct" lane so the
    // board stays traversable, and no fully-walled column. Plateaus (incl. a tier-2
    // redoubt on later missions) are shared across archetypes, and `EnsureConnectivity`
    // (run by Build afterwards) is the final net should sprinkles/protective cover ever
    // pinch a path. Spawn columns (0-3) and enemy columns (14-17) are left clear.

    static void BuildProcedural(Grid grid, HashSet<(int, int)> occupied,
                                HashSet<(int, int)> evac, int missionNum)
    {
        // contested high ground: raised plateaus in the mid-field (more on later missions).
        // Shared by all archetypes so elevation play is always present.
        RaisePlateau(grid, evac, 7, 3, 2, 2);
        RaisePlateau(grid, evac, 11, 7, 2, 2);
        if (missionNum >= 3) RaisePlateau(grid, evac, Util.RandInt(6, 11), Util.RandInt(1, 8), 2, 2);
        // a commanding tier-2 redoubt appears on later missions (sees over high cover)
        if (missionNum >= 4) RaisePlateau(grid, evac, Util.RandInt(7, 10), Util.RandInt(3, 6), 2, 2, 2);

        // pick a mid-field cover archetype (variety); each leaves an open lane + no walled column
        switch (Util.RandInt(0, 3))
        {
            case 0:  ArchScreen(grid, occupied);        break;   // the 4.2 staggered screen
            case 1:  ArchRedoubt(grid, occupied);       break;   // a central bunker, flank lanes
            case 2:  ArchTwinCorridors(grid, occupied); break;   // two cover spines, a centre gap
            default: ArchDiagonalWall(grid, occupied);  break;   // a slanted wall with a breach
        }

        // random crates (a touch more clutter on later missions; biased toward LoS-blocking high cover)
        Sprinkle(grid, occupied, 14 + Math.Min(6, missionNum));
    }

    /// Archetype 0 — the original 4.2 staggered mid-field SCREEN of high cover: breaks the
    /// long cross-board sightlines so the squad can advance into the midfield under cover
    /// before tripping a pod. No column is fully walled; row 5 is the one open risky lane.
    static void ArchScreen(Grid grid, HashSet<(int, int)> occupied)
    {
        var screen = new (int x, int y)[]
        {
            (7, 1), (7, 2), (7, 3),   (8, 6), (8, 7), (8, 8),
            (9, 0), (9, 1), (9, 9), (9, 10),   (10, 3), (10, 4), (10, 7), (10, 8),
            (11, 1), (11, 2),
        };
        foreach (var (sx, sy) in screen) PlaceCover(grid, occupied, sx, sy, TileType.HighCover);
        // low cover flanking the open central lane, for cover-fighting on the direct route
        PlaceCover(grid, occupied, 6, 5, TileType.LowCover);
        PlaceCover(grid, occupied, 12, 5, TileType.LowCover);
    }

    /// Archetype 1 — a central REDOUBT: a compact high-cover bunker mid-board with a low-cover
    /// apron, leaving wide flanking lanes top and bottom. Rewards a flank rather than a frontal
    /// push; the bunker breaks the central sightline while the rims stay open.
    static void ArchRedoubt(Grid grid, HashSet<(int, int)> occupied)
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
        foreach (var (sx, sy) in ring) PlaceCover(grid, occupied, sx, sy, TileType.HighCover);
        // low-cover apron on the approaches (covered fighting positions outside the bunker)
        PlaceCover(grid, occupied, 6, 4, TileType.LowCover);
        PlaceCover(grid, occupied, 6, 6, TileType.LowCover);
        PlaceCover(grid, occupied, 12, 4, TileType.LowCover);
        PlaceCover(grid, occupied, 12, 6, TileType.LowCover);
        // top/bottom flanking lanes (rows 0-1 and 9-10) are deliberately left open.
    }

    /// Archetype 2 — TWIN CORRIDORS: two vertical high-cover spines (a forward and a rear
    /// staggered wall), each gapped so a soldier can slip through, with an open central seam
    /// between them. Creates layered cover and channels movement into the gaps.
    static void ArchTwinCorridors(Grid grid, HashSet<(int, int)> occupied)
    {
        // forward spine at col 7, gap at rows 4-5 (the open seam)
        for (int y = 0; y < grid.H; y++)
            if (y < 4 || y > 5) PlaceCover(grid, occupied, 7, y, TileType.HighCover);
        // rear spine at col 11, gap at rows 5-6 (offset from the forward gap -> staggered)
        for (int y = 0; y < grid.H; y++)
            if (y < 5 || y > 6) PlaceCover(grid, occupied, 11, y, TileType.HighCover);
        // low cover bracketing the central seam (cover-fight in the gap between the spines)
        PlaceCover(grid, occupied, 9, 4, TileType.LowCover);
        PlaceCover(grid, occupied, 9, 6, TileType.LowCover);
    }

    /// Archetype 3 — a DIAGONAL WALL of high cover slashing across the mid-field with a single
    /// breach gap, plus a low-cover counter-diagonal. Strong sightline break on a slant; the
    /// breach is the contested crossing, and the wall's ends leave the rims open.
    static void ArchDiagonalWall(Grid grid, HashSet<(int, int)> occupied)
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
        foreach (var (sx, sy) in wall) PlaceCover(grid, occupied, sx, sy, TileType.HighCover);
        // a short low-cover counter-diagonal giving the attacker covered footing to the breach
        PlaceCover(grid, occupied, 6, 6, TileType.LowCover);
        PlaceCover(grid, occupied, 9, 6, TileType.LowCover);   // flanks the breach, doesn't seal it
        PlaceCover(grid, occupied, 12, 6, TileType.LowCover);
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
    static bool TryApplyLayout(Grid g, HashSet<(int, int)> occupied, List<Unit> players,
                               List<Unit> enemies, HashSet<(int, int)> evac,
                               (int x, int y)? terminal, string[] tpl, List<(int x, int y)> sabotage)
    {
        if (tpl.Length != g.H) return false;
        for (int y = 0; y < g.H; y++) if (tpl[y].Length != g.W) return false;
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

        // connectivity: flood from the first soldier across walkable tiles (cover = wall)
        var cost = g.CostMap(players[0].X, players[0].Y, (x, y) => false, out _, 9999);
        bool Reachable(int x, int y) => g.InBounds(x, y) && cost[x, y] >= 0;

        bool ok = true;
        foreach (var u in players) if (!Reachable(u.X, u.Y)) ok = false;
        foreach (var u in enemies) if (!Reachable(u.X, u.Y)) ok = false;
        foreach (var t in evac) if (!Reachable(t.Item1, t.Item2)) ok = false;
        if (terminal.HasValue && !Reachable(terminal.Value.x, terminal.Value.y)) ok = false;
        if (sabotage != null) foreach (var s in sabotage) if (!Reachable(s.x, s.y)) ok = false;

        if (!ok)   // revert to a clean slate so the procedural path can run
        {
            for (int y = 0; y < g.H; y++)
                for (int x = 0; x < g.W; x++)
                    if (!occupied.Contains((x, y))) { g.Tiles[x, y] = TileType.Floor; g.Height[x, y] = 0; g.Barrel[x, y] = false; }
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
                             int shape = DeployFrontal)
    {
        // Headcount cap raised 10 -> 12 so the top-Heat "+enemy" rungs aren't silently wasted
        // (the +1/+1 from RELENTLESS/OVERWHELMING used to clip at 10 on later missions). 12 still
        // fits easily: spawns occupy cols 14-17 over grid.H rows (44 slots) and the collision loop
        // below relocates any overlap.
        // Difficulty RECALIBRATED to the grown squad: the curve was softened (3+n / (n-1)*2/3) back
        // when the squad was a struggling 4-strong. Since then deploy-growth (5-6 bodies), run boons,
        // Armor, and the Evac fix stacked huge squad power -> heat-0 hit ~97%/mission (too trivial).
        // Restored the enemy headcount (4+n, cap 12) and the full per-mission stat bump (n-1) so the
        // now-strong squad faces a real fight; Heat's deltas still stack for the mastery ladder.
        int count = Math.Clamp(4 + n + enemyDelta, 3, 12);   // deployment-card + Heat modifier
        // R1 REVIEW FIX — the floor was `Math.Max(0, ...)`, which silently ATE the RECRUIT rung's
        // advertised relief on MISSION 1, the exact mission the on-ramp exists for: at n == 1 the
        // growth term is 0, so heat 0 gave max(0, 0) = 0 and RECRUIT (statDelta -1) gave
        // max(0, -1) = 0 — identical. Only the body count moved, while Hud.RecruitLines and
        // Heat.RecruitMod.Desc both promise "each -1 HP and aim". The floor drops to -1: one point
        // of force-wide relief may go BELOW the base, and no more (a deeper stack — RECRUIT plus a
        // multi-tier adaptive assist — still bottoms out at -1, so no archetype can be trivialised).
        // Heats 1-8 are bit-for-bit unchanged by construction: there statDelta = card.StatDelta +
        // heatStat is never negative, so (n-1)+statDelta >= 0 and the new floor is unreachable.
        int bump = Math.Max(-1, (n - 1) + statDelta);        // stat growth per mission +/- card (relief floor -1)
        // SABOTAGE relief (the weakest objective / m5 gate, ~65% -> aiming ~85%): the difficulty of
        // this objective IS the 3x split-and-go-loud tempo, not raw bodies, so trim the force by 2
        // (floored at 3) so a divided squad isn't also out-gunned. Stat bump is untouched and the
        // Heat ladder still applies on top, so the mastery curve is preserved.
        if (sabotage) count = Math.Max(3, count - 2);
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
        if (defend) count = Math.Max(3, count - 3 + Math.Clamp(defendKeep, 0, 2));
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
            count = Math.Max(5, count - (Combat.MissionFaction != Faction.None && Ai.Tier >= 1 ? 3 : 4));
            bump = Math.Max(0, n - 1);                       // drop the boss-card/heat StatDelta for the screen
        }
        var rows = new List<int>();
        for (int y = 0; y < grid.H; y++) rows.Add(y);
        // shuffle rows
        for (int i = rows.Count - 1; i > 0; i--) { int j = Util.RandInt(0, i); (rows[i], rows[j]) = (rows[j], rows[i]); }

        var used = new HashSet<(int, int)>();
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
        bool podsOf3 = n >= 3 && n < Run.MaxMissions;
        // FUL-6 ESCALATION LEVER 1 (measured breach): the full pod stack ran the h0 paired
        // flywheel at -12.5 pts completion vs the fresh same-slot R0 (chunk a -5, chunk b -20;
        // budget <= 8). The spec's first lever: trim the initial force by 1 on 3-pod missions
        // (the FUL-4 defend-trim precedent) — each contact is bigger now (3 guns wake at once,
        // a link can make it 6), so the unchanged body count priced a harder mission than the
        // budget allows. m1-2 and the finale are untouched (no pod stack there); floored at 3
        // like the sabotage/defend trims above.
        if (podsOf3) count = Math.Max(3, count - 1);
        int[] podOf = null, memberOf = null;
        int[] podAnchor = null, podAnchorX = null;
        // W4 POD UNIFORMITY: the pod lead's archetype roll, reused by its members. Sized for the
        // i/2 pairing too (m1-2), so the teaching tier's pairs field one kind of body as well;
        // the FINALE is excluded (its kit slots are explicit and FUL11PROBE pins their geometry).
        float[] podRoll = new float[count / 2 + 2];
        if (podsOf3)
        {
            int[] plan = PodPlan(count);
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
            var lead = PodAnchor(shape, podId, rows[i % rows.Count], grid.W, grid.H);
            int x, y;
            if (podsOf3 && member > 0 && shape == DeployEnvelop)
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
            { y = Util.RandInt(0, grid.H - 1); x = grid.W - 2 - Util.RandInt(0, 2); }
            used.Add((x, y));
            if (podsOf3 && member == 0) { podAnchor[podId] = y; podAnchorX[podId] = x; }   // the pod lead's final tile

            bool finalMission = n >= Run.MaxMissions;
            bool midBoss = !finalMission && i == 0 && (n == 3 || n == 5);   // recurring named elite
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
                e = i == 0 ? MakeFinaleBoss(n, x, y) : MakeFinaleRetinue(i, n, bump, x, y);
            if (e == null)
                e = midBoss ? MakeMidBoss(n, x, y)
                            : SelectArchetype(n, r, bump, x, y);   // tier-appropriate rank-and-file
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
            else if (n >= 2 && e.Cls != "MEDIC" && e.Cls != "SAPPER" && e.Cls != "BOMBARD" && (e.Cls == "BRUISER" || Util.Roll(22))) e.Grenades = 1;
            // utility items (S2-B): snipers/scouts carry smoke to cover their movement;
            // some grunts get smoke from mission 3+. Flash given to berserkers (mission 3+)
            // to disorient the squad before charging. Never given to ELITE/MEDIC/TURRET/
            // DRONE/SHIELD/SAPPER (they each have a dedicated role already).
            if (e.Cls == "SNIPER" || e.Cls == "SCOUT")
                { e.EnemyItem = ItemKind.Smoke; e.ItemCharge = 1; }
            else if (n >= 3 && e.Cls == "BERSERKER")
                { e.EnemyItem = ItemKind.Flash; e.ItemCharge = 1; }
            else if (n >= 3 && e.Cls == "GRUNT" && Util.Roll(18))
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
        u.Kills = Math.Max(0, (mission - 1) / 2);
        return u;
    }

    /// The escort asset: fragile, poor aim, carries only a panicky sidearm.
    /// Lives in the player roster for one mission and never joins the persistent squad.
    public static Unit MakeVip(int missionNum = 1)
    {
        // HP 6 -> 14 (Wave A.5) -> now scales with mission depth: balance data showed Escort still
        // gating runs (~55%, many "VIP LOST") because the fragile asset carries ZERO persistent
        // progression while the enemy force climbs every mission. A flat 14 HP that's sturdy at m2
        // is glass by m6. Scale it (14 + 2*mission: m2~18, m4~22, m6~26) so the asset stays a
        // believable survivor against the late force, while still having no cover-perks, weak aim,
        // and no frags — the squad must still screen for it.
        int hp = 14 + 2 * Math.Max(1, missionNum);
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
        u.Armor = Math.Max(1, missionNum) / 2;   // m2~1, m4~2, m6~3
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
    public const int HostileToughness = 3;

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
    public const int HostileDamageTrim = 1;

    static Unit MakeHostile(string name, string cls, WeaponKind w, int hp, int aim, int mob, int x, int y)
    {
        int thp = hp + HostileToughness;
        var u = new Unit { Name = name, Cls = cls, Team = Team.Enemy, X = x, Y = y, Hp = thp, MaxHp = thp, Aim = aim, Mobility = mob, Weapon = Weapon.Make(w) };
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
            _                 => Mk(n == 3 ? "BREAKER" : "WARDEN"),
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

    /// Procedural-path safety net: every hostile / objective tile must stay reachable
    /// from the squad over walkable terrain. If a generated structure walled one off,
    /// carve an L-shaped lane back toward the squad by clearing the blocking cover. Runs
    /// universally (authored maps are pre-verified, but the protective cover is added
    /// after that check, so this catches any edge case for both paths).
    static void EnsureConnectivity(Grid g, List<Unit> players, List<Unit> enemies,
                                   HashSet<(int, int)> evac, (int x, int y)? terminal, List<(int x, int y)> sabotage)
    {
        if (players.Count == 0) return;
        var from = players[0];
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var cost = g.CostMap(from.X, from.Y, (x, y) => false, out _, 9999);
            bool Stuck(int x, int y) => g.InBounds(x, y) && cost[x, y] < 0;

            var stuck = new List<(int x, int y)>();
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
        int target = Math.Clamp(2 + missionNum / 2, 2, 5);
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
